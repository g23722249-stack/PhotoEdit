Imports PhotoEdit
Imports System.Threading

''' <summary>
''' 設定 → 模型：列出 PhotoEdit 用到的 AI 模型、各自的用途與狀態，可以逐個下載到 Models\。
''' 發佈的程式不附模型（檔案大、各有授權），使用者需要哪個功能就下載哪個；清單在 ModelCatalog.json。
''' </summary>
Friend Class ModelsPage
    Inherits Panel

    Private Const RowW As Integer = 438
    Private ReadOnly _catalog As ModelCatalog
    Private ReadOnly _font As Font
    Private ReadOnly _bold As Font
    Private ReadOnly _small As Font
    Private ReadOnly _list As New Panel With {.AutoScroll = True, .BackColor = Color.Transparent}
    Private ReadOnly _rows As New List(Of Row)()
    Private ReadOnly _folder As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .AutoEllipsis = True}

    ''' <summary>一個模型的一列。</summary>
    Friend Class Row
        Public Model As ModelEntry
        Public Panel As New Panel With {.BackColor = Color.Transparent}
        Public Title As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .AutoEllipsis = True}
        Public Status As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.TopRight}
        Public Meta As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
        Public Use As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
        Public Action As New Button With {.UseVisualStyleBackColor = True}
        Public Source As New LinkLabel With {.AutoSize = True, .BackColor = Color.Transparent}
        Public Progress As New ProgressBar With {.Visible = False, .Maximum = 1000}
        Public Cancel As CancellationTokenSource
    End Class

    Public Sub New(font As Font, catalog As ModelCatalog)
        _catalog = catalog
        _font = font
        _bold = New Font(font, FontStyle.Bold)
        _small = New Font(font.FontFamily, font.Size - 1.5F)
        BackColor = Color.Transparent
        Size = New Size(476, 540)

        Dim head As New Label With {.Text = Lang.T("AI 模型"), .AutoSize = False, .Font = _bold, .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
        head.SetBounds(12, 12, 452, 22)
        Dim intro As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120),
                                     .Text = Lang.T("發佈的 PhotoEdit 不附 AI 模型（檔案大、各有授權）。用到哪個功能就在這裡下載哪個，存到程式資料夾的 Models\。沒有下載的功能會停用，其他功能照常。")}
        intro.SetBounds(12, 36, 452, 56)
        _folder.Text = Lang.T("資料夾：") & _catalog.ModelsDir
        _folder.ForeColor = Color.FromArgb(105, 110, 120)
        _folder.SetBounds(12, 96, 330, 22)
        Dim open As New Button With {.Text = Lang.T("開啟資料夾"), .UseVisualStyleBackColor = True}
        open.SetBounds(350, 92, 114, 28)
        AddHandler open.Click, Sub()
                                   IO.Directory.CreateDirectory(_catalog.ModelsDir)
                                   Process.Start(New ProcessStartInfo("explorer.exe", """" & _catalog.ModelsDir & """") With {.UseShellExecute = True})
                               End Sub
        _list.SetBounds(4, 126, 468, 410)
        Controls.AddRange({head, intro, _folder, open, _list})

        Dim y = 0
        Dim lastGroup = ""
        For Each m In _catalog.Models
            If m.Group <> lastGroup Then
                lastGroup = m.Group
                Dim g As New Label With {.Text = m.Group, .AutoSize = False, .Font = _bold, .BackColor = Color.Transparent}
                g.SetBounds(8, y + 6, RowW, 22)
                _list.Controls.Add(g)
                y += 30
            End If
            Dim r = BuildRow(m)
            r.Panel.Location = New Point(8, y)
            _list.Controls.Add(r.Panel)
            y += r.Panel.Height + 8
        Next
        If _catalog.Models.Count = 0 Then
            Dim none As New Label With {.Text = Lang.T("找不到模型清單（ModelCatalog.json）。"), .AutoSize = False, .BackColor = Color.Transparent}
            none.SetBounds(8, 8, RowW, 24)
            _list.Controls.Add(none)
        End If
    End Sub

    Friend ReadOnly Property Rows As IReadOnlyList(Of Row)
        Get
            Return _rows
        End Get
    End Property

    Private Function BuildRow(m As ModelEntry) As Row
        Dim r As New Row With {.Model = m}
        r.Title.Text = m.Name
        r.Title.Font = _bold
        r.Title.SetBounds(0, 0, RowW - 120, 22)
        r.Status.SetBounds(RowW - 120, 2, 120, 20)
        r.Status.Font = _small
        r.Meta.Font = _small
        r.Meta.ForeColor = Color.FromArgb(120, 126, 136)
        r.Meta.Text = $"{ModelCatalog.SizeText(m.TotalSize)}｜{Lang.T("授權")} {m.License}"
        r.Meta.SetBounds(0, 22, RowW, 18)
        r.Use.Text = m.Use
        r.Use.ForeColor = Color.FromArgb(90, 96, 108)
        r.Use.Font = _font
        Dim useH = r.Use.GetPreferredSize(New Size(RowW, 0)).Height + 4 ' 用標籤自己的換行量（和畫出來一致）
        r.Use.SetBounds(0, 42, RowW, useH)
        Dim by = 42 + useH + 4
        r.Action.SetBounds(0, by, 110, 28)
        r.Progress.SetBounds(118, by + 5, 220, 18)
        r.Source.Text = Lang.T("來源")
        r.Source.Location = New Point(RowW - 40, by + 6)
        AddHandler r.Source.LinkClicked, Sub()
                                             If Not String.IsNullOrEmpty(m.Source) Then Process.Start(New ProcessStartInfo(m.Source) With {.UseShellExecute = True})
                                         End Sub
        AddHandler r.Action.Click, Sub() OnAction(r)
        r.Panel.Size = New Size(RowW, by + 32)
        r.Panel.Controls.AddRange({r.Title, r.Status, r.Meta, r.Use, r.Action, r.Progress, r.Source})
        _rows.Add(r)
        UpdateRow(r)
        Return r
    End Function

    ''' <summary>依檔案狀態更新一列的文字與按鈕。</summary>
    Friend Sub UpdateRow(r As Row)
        If r.Cancel IsNot Nothing Then Return ' 下載中由進度更新
        Dim st = _catalog.State(r.Model)
        Select Case st
            Case ModelState.Installed
                r.Status.Text = Lang.T("✓ 已安裝")
                r.Status.ForeColor = Color.FromArgb(40, 140, 70)
                r.Action.Text = Lang.T("刪除")
                r.Action.Enabled = True
            Case Else
                Dim remain = ModelCatalog.SizeText(_catalog.RemainingSize(r.Model))
                r.Status.Text = If(st = ModelState.Incomplete, Lang.T("不完整"), Lang.T("未安裝"))
                r.Status.ForeColor = If(st = ModelState.Incomplete, Color.FromArgb(200, 120, 20), Color.FromArgb(130, 136, 146))
                If _catalog.CanDownload(r.Model) Then
                    r.Action.Text = Lang.T("下載 ") & remain
                    r.Action.Enabled = True
                Else
                    r.Action.Text = Lang.T("需另外取得")
                    r.Action.Enabled = False
                End If
        End Select
        r.Progress.Visible = False
    End Sub

    Private Sub OnAction(r As Row)
        If r.Cancel IsNot Nothing Then
            r.Cancel.Cancel()
            Return
        End If
        If _catalog.State(r.Model) = ModelState.Installed Then
            If MessageBox.Show(FindForm(), Lang.F("刪除「{0}」的模型檔案（{1}）？用到它的功能會停用，之後可以再下載。", r.Model.Name, ModelCatalog.SizeText(r.Model.TotalSize)),
                               Lang.T("模型"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
            Try
                _catalog.Delete(r.Model)
            Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException
                MessageBox.Show(FindForm(), Lang.T("無法刪除（可能正在使用中）：") & ex.Message, Lang.T("模型"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
            UpdateRow(r)
            Return
        End If
        StartDownload(r)
    End Sub

    ''' <summary>開始下載這一列的模型（測試也從這裡進來）。</summary>
    Friend Function StartDownload(r As Row) As Task
        Dim cts As New CancellationTokenSource()
        r.Cancel = cts
        r.Action.Text = Lang.T("取消")
        r.Status.Text = Lang.T("下載中…")
        r.Status.ForeColor = Color.FromArgb(40, 110, 210)
        r.Progress.Value = 0
        r.Progress.Visible = True
        Dim report = Sub(done As Long, total As Long)
                         Dim f = If(total <= 0, 0, CInt(Math.Min(1000, done * 1000 \ total)))
                         Dim post = Sub()
                                        If IsDisposed OrElse r.Cancel Is Nothing Then Return
                                        r.Progress.Value = f
                                        r.Status.Text = $"{ModelCatalog.SizeText(done)} / {ModelCatalog.SizeText(total)}"
                                    End Sub
                         OnUi(post)
                     End Sub
        Dim t = _catalog.DownloadAsync(r.Model, report, cts.Token)
        Return t.ContinueWith(Sub(done)
                                  Dim finish = Sub()
                                                   If IsDisposed Then Return
                                                   r.Cancel = Nothing
                                                   cts.Dispose()
                                                   UpdateRow(r)
                                                   If done.IsCanceled OrElse (done.Exception IsNot Nothing AndAlso TypeOf done.Exception.GetBaseException() Is OperationCanceledException) Then
                                                       r.Status.Text = Lang.T("已取消")
                                                   ElseIf done.Exception IsNot Nothing Then
                                                       r.Status.Text = Lang.T("下載失敗")
                                                       r.Status.ForeColor = Color.FromArgb(200, 50, 40)
                                                       LastError = done.Exception.GetBaseException().Message
                                                       If Not Quiet Then MessageBox.Show(FindForm(), Lang.T("下載失敗：") & LastError, Lang.T("模型"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
                                                   End If
                                               End Sub
                                  OnUi(finish)
                              End Sub)
    End Function

    ''' <summary>回到介面執行緒執行（下載的進度與完成是在背景執行緒回報的）；頁面已關閉就不做。</summary>
    Private Sub OnUi(action As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        If InvokeRequired Then
            Try
                BeginInvoke(action)
            Catch ex As InvalidOperationException ' 剛好在關閉
            End Try
        Else
            action()
        End If
    End Sub

    ''' <summary>測試用：失敗時不跳視窗。</summary>
    Friend Property Quiet As Boolean
    Friend Property LastError As String

    ''' <summary>正在下載的數量（關閉設定前提醒）。</summary>
    Friend ReadOnly Property Downloading As Integer
        Get
            Return _rows.Where(Function(r) r.Cancel IsNot Nothing).Count()
        End Get
    End Property

    ''' <summary>取消所有下載（關閉設定時）。</summary>
    Friend Sub CancelAll()
        For Each r In _rows
            r.Cancel?.Cancel()
        Next
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            CancelAll()
            _bold.Dispose()
            _small.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
