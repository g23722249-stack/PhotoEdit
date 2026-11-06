Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports PhotoEdit

''' <summary>
''' 背景工作回報進度用（背景執行緒呼叫；視窗每 100ms 讀一次，不會塞爆訊息佇列）。
''' Stage＝換到第幾步（訊息、進度重來）；Report＝這一步裡的進度或訊息。
''' </summary>
Friend NotInheritable Class BusyProgress
    Private ReadOnly _lock As New Object()
    Private _message As String = ""
    Private _stage, _stages, _current, _total As Integer

    Public ReadOnly Property Token As CancellationToken

    Public Sub New(token As CancellationToken)
        Me.Token = token
    End Sub

    ''' <summary>換到第 index 步（1 起算，共 count 步）；這一步的進度先當成不知道（跑動的進度條）。</summary>
    Public Sub Stage(index As Integer, count As Integer, message As String)
        SyncLock _lock
            _stage = index : _stages = count : _message = If(message, "")
            _current = 0 : _total = 0
        End SyncLock
    End Sub

    ''' <summary>只換訊息。</summary>
    Public Sub Report(message As String)
        SyncLock _lock
            _message = If(message, "")
        End SyncLock
    End Sub

    ''' <summary>這一步裡的進度（例如 AI 重繪的第幾步）；total 0＝不知道。</summary>
    Public Sub Report(current As Integer, total As Integer)
        SyncLock _lock
            _current = current : _total = total
        End SyncLock
    End Sub

    Friend Function Snapshot() As (Message As String, Stage As Integer, Stages As Integer, Current As Integer, Total As Integer)
        SyncLock _lock
            Return (_message, _stage, _stages, _current, _total)
        End SyncLock
    End Function
End Class

''' <summary>
''' 共用的「處理中」視窗：去背、找臉、AI 重繪這類要等的模型計算都用這個。
''' 工作在背景執行；呼叫的視窗游標變成等待，0.3 秒內做完就不跳視窗（避免閃一下）。
''' 視窗顯示目前的步驟（第幾步／共幾步）、進度條（知道進度時是實際進度，不知道時跑動）、經過的秒數與提示；
''' 給了 CancellationTokenSource 才有「取消」按鈕。視窗出現時呼叫的視窗不能操作（像對話框一樣），做完自動關閉。
''' </summary>
Friend NotInheritable Class BusyDialog
    Inherits Aqua.AquaForm

    ''' <summary>超過這麼久還沒做完才跳視窗（毫秒）。</summary>
    Friend Const ShowDelay As Integer = 300

    <DllImport("user32.dll")>
    Private Shared Function EnableWindow(hWnd As IntPtr, enable As Boolean) As Boolean
    End Function

    Private ReadOnly _progress As BusyProgress
    Private ReadOnly _cancelSource As CancellationTokenSource
    Private ReadOnly _watch As Diagnostics.Stopwatch
    Private ReadOnly _quiet As Boolean
    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _bold As New Font(Lang.UiFont, 11.0F, FontStyle.Bold)
    Private ReadOnly _stageLabel As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _messageLabel As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _bar As New ProgressBar With {.Minimum = 0, .Maximum = 1000, .Style = ProgressBarStyle.Marquee, .MarqueeAnimationSpeed = 30}
    Private ReadOnly _elapsed As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(110, 116, 128)}
    Private ReadOnly _hint As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(110, 116, 128)}
    Private ReadOnly _cancel As New Button With {.Text = Lang.T("取消"), .UseVisualStyleBackColor = True}
    Private _allowClose As Boolean

    Private Sub New(title As String, progress As BusyProgress, hint As String, cancel As CancellationTokenSource, watch As Diagnostics.Stopwatch, owner As Form)
        _progress = progress
        _cancelSource = cancel
        _watch = watch
        Text = title
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        RoundCorners = False
        MinButton = False
        MaxButton = False
        CloseButton = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.Manual
        UseWaitCursor = True
        Dim top = 23 + 12
        ClientSize = New Size(440, top + 132)
        _stageLabel.Font = _bold
        _stageLabel.SetBounds(18, top, 404, 24)
        _messageLabel.SetBounds(18, top + 26, 404, 22)
        _bar.SetBounds(18, top + 54, 404, 18)
        _elapsed.SetBounds(18, top + 80, 200, 20)
        _hint.SetBounds(18, top + 100, 404, 20)
        _hint.Text = If(hint, "")
        Controls.AddRange({_stageLabel, _messageLabel, _bar, _elapsed, _hint})
        If cancel IsNot Nothing Then
            _cancel.SetBounds(ClientSize.Width - 18 - 96, top + 78, 96, 30)
            _cancel.UseWaitCursor = False
            _cancel.Cursor = Cursors.Default
            AddHandler _cancel.Click, Sub()
                                          _cancelSource.Cancel()
                                          _cancel.Enabled = False
                                          _hint.Text = Lang.T("正在停止…")
                                      End Sub
            Controls.Add(_cancel)
        End If
        ' 測試時呼叫的視窗在畫面外、透明：這個視窗也跟著，而且不搶焦點
        _quiet = owner IsNot Nothing AndAlso owner.Opacity < 1
        If _quiet Then Opacity = owner.Opacity
        If owner IsNot Nothing Then
            Location = New Point(owner.Left + (owner.Width - Width) \ 2, owner.Top + (owner.Height - Height) \ 2)
        Else
            Dim wa = Screen.PrimaryScreen.WorkingArea
            Location = New Point(wa.Left + (wa.Width - Width) \ 2, wa.Top + (wa.Height - Height) \ 2)
        End If
        UpdateView()
        ThemeManager.Attach(Me)
    End Sub

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return _quiet
        End Get
    End Property

    Private Sub UpdateView()
        Dim s = _progress.Snapshot()
        _stageLabel.Text = If(s.Stages > 1, Lang.F("步驟 {0}／{1}：", s.Stage, s.Stages), "") & s.Message
        If s.Total > 0 Then
            ' 有幾步時進度條代表全部：前面做完的步驟＋這一步的比例
            Dim part = Math.Clamp(s.Current / CDbl(s.Total), 0, 1)
            Dim overall = If(s.Stages > 1, (Math.Max(0, s.Stage - 1) + part) / s.Stages, part)
            If _bar.Style <> ProgressBarStyle.Continuous Then _bar.Style = ProgressBarStyle.Continuous
            _bar.Value = CInt(Math.Round(overall * 1000))
            _messageLabel.Text = Lang.F("{0} / {1}", s.Current, s.Total)
        Else
            If _bar.Style <> ProgressBarStyle.Marquee Then _bar.Style = ProgressBarStyle.Marquee
            _messageLabel.Text = Lang.T("請稍候…")
        End If
        _elapsed.Text = Lang.F("已經過 {0:0} 秒", Math.Floor(_watch.Elapsed.TotalSeconds))
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' 只有工作做完才關（Alt+F4 不算）；要停請按「取消」
        If Not _allowClose AndAlso e.CloseReason = CloseReason.UserClosing Then e.Cancel = True
        MyBase.OnFormClosing(e)
    End Sub

    Private Sub CloseDone()
        _allowClose = True
        Close()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _font.Dispose()
            _bold.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    '=====================================================================
    ' 使用方式
    '=====================================================================

    ''' <summary>
    ''' 在背景執行 work，完成後回傳結果（例外照樣傳出；取消時是 OperationCanceledException）。
    ''' 要在 UI 執行緒呼叫；結果在 UI 執行緒完成，Await 之後可以直接改畫面。
    ''' </summary>
    ''' <param name="title">視窗標題，例如「去背中」。</param>
    ''' <param name="hint">視窗下方的灰色提示，例如「第一次載入模型會久一點」。</param>
    ''' <param name="cancel">給了才有「取消」按鈕；work 要自己看 BusyProgress.Token。</param>
    Friend Shared Function RunAsync(Of T)(owner As Form, title As String, work As Func(Of BusyProgress, T),
                                          Optional hint As String = Nothing, Optional cancel As CancellationTokenSource = Nothing) As Task(Of T)
        Dim progress As New BusyProgress(If(cancel Is Nothing, CancellationToken.None, cancel.Token))
        progress.Stage(1, 1, title)
        Dim tcs As New TaskCompletionSource(Of T)()
        Dim watch = Diagnostics.Stopwatch.StartNew()
        Dim job = Task.Run(Function() work(progress))
        SetWaitCursor(owner, True)
        ' Await 之後要回到 UI 執行緒：沒有 WinForms 的同步內容時（例如還沒進 Application.Run）裝一個，
        ' 不然後面的程式會跑到背景執行緒（在那裡啟動的計時器永遠不會觸發）
        If TypeOf SynchronizationContext.Current IsNot WindowsFormsSynchronizationContext Then
            SynchronizationContext.SetSynchronizationContext(New WindowsFormsSynchronizationContext())
        End If
        Dim dlg As BusyDialog = Nothing
        Dim ui = SynchronizationContext.Current
        Dim timer As New System.Windows.Forms.Timer With {.Interval = 100}
        Dim finish = Sub()
                         ' 不在計時器事件裡收尾：呼叫的程式接著會啟動自己的計時器（在別的計時器事件裡、又剛釋放計時器時啟動，曾經一直不觸發）
                         timer.Dispose()
                         If dlg IsNot Nothing AndAlso Not dlg.IsDisposed Then
                             ' 先讓呼叫的視窗回來可以操作，再關（焦點才會回到它，不會跳到別的程式）
                             SetOwnerEnabled(owner, True)
                             dlg.CloseDone()
                             dlg.Dispose()
                         End If
                         SetWaitCursor(owner, False)
                         If job.IsCanceled Then
                             tcs.SetCanceled()
                         ElseIf job.IsFaulted Then
                             tcs.SetException(job.Exception.InnerExceptions)
                         Else
                             tcs.SetResult(job.Result)
                         End If
                     End Sub
        AddHandler timer.Tick, Sub()
                                   If job.IsCompleted Then
                                       timer.Stop()
                                       If ui IsNot Nothing Then ui.Post(Sub(state) finish(), Nothing) Else finish()
                                       Return
                                   End If
                                   If dlg Is Nothing Then
                                       If watch.ElapsedMilliseconds < ShowDelay OrElse owner Is Nothing OrElse owner.IsDisposed Then Return
                                       dlg = New BusyDialog(title, progress, hint, cancel, watch, owner)
                                       dlg.Show(owner)
                                       SetOwnerEnabled(owner, False)
                                   Else
                                       dlg.UpdateView()
                                   End If
                               End Sub
        timer.Start()
        Return tcs.Task
    End Function

    ''' <summary>同步版：等工作做完才回來（中間畫面照常重畫）。給原本就在 UI 執行緒直接算的地方用。</summary>
    Friend Shared Function Run(Of T)(owner As Form, title As String, work As Func(Of BusyProgress, T),
                                     Optional hint As String = Nothing, Optional cancel As CancellationTokenSource = Nothing) As T
        Dim progress As New BusyProgress(If(cancel Is Nothing, CancellationToken.None, cancel.Token))
        progress.Stage(1, 1, title)
        Dim watch = Diagnostics.Stopwatch.StartNew()
        Dim job = Task.Run(Function() work(progress))
        SetWaitCursor(owner, True)
        Try
            If Not CType(job, IAsyncResult).AsyncWaitHandle.WaitOne(ShowDelay) AndAlso owner IsNot Nothing AndAlso Not owner.IsDisposed Then
                Using dlg As New BusyDialog(title, progress, hint, cancel, watch, owner)
                    Dim timer As New System.Windows.Forms.Timer With {.Interval = 100}
                    AddHandler timer.Tick, Sub()
                                               If job.IsCompleted Then
                                                   timer.Stop()
                                                   dlg.CloseDone()
                                               Else
                                                   dlg.UpdateView()
                                               End If
                                           End Sub
                    timer.Start()
                    dlg.ShowDialog(owner)
                    timer.Dispose()
                End Using
            End If
            Return job.GetAwaiter().GetResult()
        Finally
            SetWaitCursor(owner, False)
        End Try
    End Function

    Private Shared Sub SetWaitCursor(owner As Form, busy As Boolean)
        If owner Is Nothing OrElse owner.IsDisposed Then Return
        owner.UseWaitCursor = busy
        If busy Then Cursor.Current = Cursors.WaitCursor
    End Sub

    ''' <summary>
    ''' 呼叫的視窗不能操作（和對話框一樣用 EnableWindow，不改 Enabled 屬性，控制項不會變灰閃一下）。
    ''' </summary>
    Private Shared Sub SetOwnerEnabled(owner As Form, enabled As Boolean)
        If owner Is Nothing OrElse owner.IsDisposed OrElse Not owner.IsHandleCreated Then Return
        EnableWindow(owner.Handle, enabled)
    End Sub
End Class
