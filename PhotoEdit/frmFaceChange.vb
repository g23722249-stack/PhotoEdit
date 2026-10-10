Imports System.IO
Imports System.Threading
Imports PhotoEdit

''' <summary>
''' 川劇變臉影片（影像 → 川劇變臉…，或手動調整戲曲頁的「變臉影片…」）：同一張照片依序換上打勾的臉譜，
''' 每次換臉用扯臉、抹臉或吹臉，可以先看本人、最後露出本人；輸出 MP4、GIF。臉上的遮擋物、頭髮不會被塗（和戲曲頁同一套畫法）。
''' </summary>
Friend Class frmFaceChange
    Inherits Aqua.AquaForm

    Private Const PreviewW As Integer = 420
    Private Const PreviewH As Integer = 520

    Private ReadOnly _source As Bitmap
    Private ReadOnly _name As String
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10.0F)
    Private ReadOnly _small As New Font("Microsoft JhengHei UI", 9.0F)
    Private ReadOnly _preview As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(30, 30, 32)}
    Private ReadOnly _pos As New Aqua.Slider With {.Minimum = 0, .Maximum = 1000, .ShowTicks = False}
    Private ReadOnly _time As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _play As New Button With {.Text = "播放"}
    Private ReadOnly _list As New CheckedListBox With {.CheckOnClick = True, .IntegralHeight = False}
    Private ReadOnly _startSelf As New CheckBox With {.Text = "開頭先看本人", .AutoSize = True, .Checked = True, .BackColor = Color.Transparent}
    Private ReadOnly _endSelf As New CheckBox With {.Text = "最後露出本人", .AutoSize = True, .Checked = True, .BackColor = Color.Transparent}
    Private ReadOnly _hold As New NumericUpDown With {.Minimum = 0.3D, .Maximum = 5, .DecimalPlaces = 1, .Increment = 0.1D, .Value = 1}
    Private ReadOnly _trans As New NumericUpDown With {.Minimum = 0.15D, .Maximum = 1.5D, .DecimalPlaces = 2, .Increment = 0.05D, .Value = 0.35D}
    Private ReadOnly _style As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _size As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _saveMp4 As New Button With {.Text = "存成 MP4…"}
    Private ReadOnly _saveGif As New Button With {.Text = "存成 GIF…"}
    Private ReadOnly _cancel As New Button With {.Text = "停止", .Enabled = False}
    Private ReadOnly _progress As New ProgressBar With {.Minimum = 0, .Maximum = 100}
    Private ReadOnly _status As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _rebuild As New System.Windows.Forms.Timer With {.Interval = 300}
    Private ReadOnly _playTimer As New System.Windows.Forms.Timer With {.Interval = 33}
    Private ReadOnly _ids As New List(Of Integer) ' 清單每一列的角色編號
    Private _fc As FaceChange
    Private _frames As List(Of (Render As Func(Of OpenCvSharp.Mat), Duration As Double))
    Private _starts As Double() ' 每格開始的秒數
    Private _playClock As Double
    Private _cts As CancellationTokenSource

    ''' <summary>輸出的資料夾（和變形動畫同一個）。</summary>
    Friend Shared ReadOnly Property OutputFolder As String
        Get
            Return frmMorph.OutputFolder
        End Get
    End Property

    ''' <summary>最後一次輸出的結果訊息（測試用）。</summary>
    Friend Property LastExportMessage As String

    ''' <summary>預設打勾、依序變的臉譜。</summary>
    Private Shared ReadOnly DefaultOrder As String() = {"火焰紅臉", "雲紋藍臉", "綠臉精怪", "金臉神佛", "包拯（月牙）"}

    Public Sub New(source As Bitmap, name As String, help As HelpTip)
        _source = source
        _name = name
        Text = "川劇變臉"
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        Dim top = 23 + 10
        Dim xR = 16 + PreviewW + 20
        ClientSize = New Size(xR + 330, top + PreviewH + 120)

        ' 左：預覽＋時間軸
        _preview.SetBounds(16, top, PreviewW, PreviewH)
        Controls.Add(_preview)
        Dim py = top + PreviewH + 8
        _pos.SetBounds(16, py + 2, PreviewW - 140, 24)
        _time.SetBounds(16 + PreviewW - 136, py, 70, 26)
        _play.SetBounds(16 + PreviewW - 62, py - 1, 62, 28)
        AddHandler _pos.ValueChanged, Sub() ShowAt(_pos.Value / 1000.0 * TotalSeconds())
        AddHandler _play.Click, Sub() TogglePlay()
        help?.SetHelpLinked("facechange.preview", _pos, _pos, _time, _play)
        Controls.AddRange({_pos, _time, _play})

        ' 右：臉譜順序
        Dim y = top
        Controls.Add(New Label With {.Text = "打勾的臉譜依序變（可調順序）", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(xR, y)})
        y += 24
        _list.SetBounds(xR, y, 240, 270)
        _list.Font = _small
        Dim order = DefaultOrder.Select(Function(n) OperaRoles.IdOf(n)).Where(Function(id) id > 0).ToList()
        Dim rest = Enumerable.Range(1, OperaRoles.All.Count).Where(Function(id) OperaRoles.Get(id).Group <> OperaGroup.Junban AndAlso Not order.Contains(id)).
                   OrderBy(Function(id) If(OperaRoles.Get(id).Group = OperaGroup.Chuan, 0, 1)).ToList()
        For Each id In order.Concat(rest)
            Dim role = OperaRoles.Get(id)
            _ids.Add(id)
            _list.Items.Add(If(role.Group = OperaGroup.Chuan, "川劇・", "京劇・") & role.Name, order.Contains(id))
        Next
        AddHandler _list.ItemCheck, Sub() BeginInvoke(Sub() Rebuild())
        help?.SetHelp("facechange.list", _list)
        Dim up As New Button With {.Text = "上移", .Font = _small}
        up.SetBounds(xR + 248, y, 70, 28)
        AddHandler up.Click, Sub() MoveItem(-1)
        Dim down As New Button With {.Text = "下移", .Font = _small}
        down.SetBounds(xR + 248, y + 32, 70, 28)
        AddHandler down.Click, Sub() MoveItem(1)
        Controls.AddRange({_list, up, down})
        y += 278
        _startSelf.Location = New Point(xR, y)
        _endSelf.Location = New Point(xR + 130, y)
        AddHandler _startSelf.CheckedChanged, Sub() Rebuild()
        AddHandler _endSelf.CheckedChanged, Sub() Rebuild()
        help?.SetHelp("facechange.self", _startSelf)
        help?.SetHelp("facechange.self", _endSelf)
        Controls.AddRange({_startSelf, _endSelf})
        y += 30
        Dim lab = Function(s As String, yy As Integer) New Label With {.Text = s, .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(xR, yy + 4)}
        Controls.Add(lab("每張停留（秒）", y))
        _hold.SetBounds(xR + 130, y, 70, 26)
        y += 32
        Controls.Add(lab("換臉時間（秒）", y))
        _trans.SetBounds(xR + 130, y, 70, 26)
        y += 32
        Controls.Add(lab("換臉方式", y))
        _style.Items.AddRange({"扯臉", "抹臉", "吹臉", "三種輪流"})
        _style.SelectedIndex = 3
        _style.SetBounds(xR + 130, y, 120, 26)
        y += 32
        Controls.Add(lab("輸出大小（長邊）", y))
        _size.Items.AddRange({"720", "1080", "原尺寸"})
        _size.SelectedIndex = 1
        _size.SetBounds(xR + 130, y, 120, 26)
        AddHandler _hold.ValueChanged, Sub() TimingChanged()
        AddHandler _trans.ValueChanged, Sub() TimingChanged()
        AddHandler _style.SelectedIndexChanged, Sub() TimingChanged()
        help?.SetHelp("facechange.timing", _hold)
        help?.SetHelp("facechange.timing", _trans)
        help?.SetHelp("facechange.style", _style)
        help?.SetHelp("morph.size", _size)
        Controls.AddRange({_hold, _trans, _style, _size})

        ' 下：輸出
        Dim by = top + PreviewH + 46
        _saveMp4.SetBounds(16, by, 110, 30)
        _saveGif.SetBounds(132, by, 110, 30)
        _cancel.SetBounds(248, by, 70, 30)
        _progress.SetBounds(330, by + 7, 220, 16)
        AddHandler _saveMp4.Click, Sub() Export(False)
        AddHandler _saveGif.Click, Sub() Export(True)
        AddHandler _cancel.Click, Sub() _cts?.Cancel()
        help?.SetHelp("morph.mp4", _saveMp4)
        help?.SetHelp("morph.gif", _saveGif)
        _status.Font = _small
        _status.SetBounds(16, by + 36, ClientSize.Width - 140, 22)
        Dim closeBtn As New Button With {.Text = "關閉", .DialogResult = DialogResult.Cancel}
        closeBtn.SetBounds(ClientSize.Width - 112, ClientSize.Height - 44, 96, 32)
        CancelButton = closeBtn
        Controls.AddRange({_saveMp4, _saveGif, _cancel, _progress, _status, closeBtn})

        AddHandler _rebuild.Tick, Sub()
                                      _rebuild.Stop()
                                      RebuildNow()
                                  End Sub
        AddHandler _playTimer.Tick, Sub() PlayStep()
        RebuildNow()
    End Sub

    '---------------------------------------------------------------------
    ' 順序與時間
    '---------------------------------------------------------------------

    ''' <summary>要變的臉（0＝本人）。</summary>
    Friend Function Sequence() As List(Of Integer)
        Dim s As New List(Of Integer)
        If _startSelf.Checked Then s.Add(0)
        For i = 0 To _list.Items.Count - 1
            If _list.GetItemChecked(i) Then s.Add(_ids(i))
        Next
        If _endSelf.Checked Then s.Add(0)
        Return s
    End Function

    Private Function Timing() As FaceChangeTiming
        Return New FaceChangeTiming With {.Hold = CDbl(_hold.Value), .Transition = CDbl(_trans.Value), .Style = CType(_style.SelectedIndex, FaceChangeStyle), .Fps = 30}
    End Function

    Private Sub MoveItem(delta As Integer)
        Dim i = _list.SelectedIndex
        Dim j = i + delta
        If i < 0 OrElse j < 0 OrElse j >= _list.Items.Count Then Return
        Dim text = _list.Items(i), chk = _list.GetItemChecked(i), id = _ids(i)
        _list.Items.RemoveAt(i)
        _ids.RemoveAt(i)
        _list.Items.Insert(j, text)
        _ids.Insert(j, id)
        _list.SetItemChecked(j, chk)
        _list.SelectedIndex = j
        Rebuild()
    End Sub

    Private Sub Rebuild()
        _rebuild.Stop()
        _rebuild.Start()
    End Sub

    Private Sub RebuildNow()
        _fc?.Dispose()
        _fc = Nothing
        Dim seq = Sequence()
        If seq.Count < 2 Then
            _status.Text = "至少要兩張臉（打勾幾張臉譜，或勾「開頭先看本人」）。"
            Return
        End If
        Cursor = Cursors.WaitCursor
        Try
            _fc = New FaceChange(_source, seq, 480)
        Finally
            Cursor = Cursors.Default
        End Try
        If Not _fc.HasFace Then _status.Text = "這張照片找不到臉，沒辦法變臉。"
        TimingChanged()
    End Sub

    Private Sub TimingChanged()
        If _fc Is Nothing Then Return
        _frames = _fc.Frames(Timing(), 30)
        Dim acc = 0.0
        _starts = _frames.Select(Function(f)
                                     Dim s = acc
                                     acc += f.Duration
                                     Return s
                                 End Function).ToArray()
        If _fc.HasFace Then _status.Text = $"{Sequence().Count} 張臉、共 {TotalSeconds():0.0} 秒。"
        ShowAt(_pos.Value / 1000.0 * TotalSeconds())
    End Sub

    Private Function TotalSeconds() As Double
        If _frames Is Nothing OrElse _frames.Count = 0 Then Return 0
        Return _starts(_starts.Length - 1) + _frames(_frames.Count - 1).Duration
    End Function

    Private Sub ShowAt(sec As Double)
        If _frames Is Nothing OrElse _frames.Count = 0 Then Return
        Dim i = Array.BinarySearch(_starts, sec)
        If i < 0 Then i = Math.Max(0, (Not i) - 1)
        i = Math.Min(_frames.Count - 1, i)
        Using m = _frames(i).Render()
            Dim old = _preview.Image
            _preview.Image = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(m)
            old?.Dispose()
        End Using
        _time.Text = $"{sec:0.0} / {TotalSeconds():0.0}"
    End Sub

    Private Sub TogglePlay()
        If _playTimer.Enabled Then
            _playTimer.Stop()
            _play.Text = "播放"
        ElseIf _frames IsNot Nothing Then
            _playClock = _pos.Value / 1000.0 * TotalSeconds()
            If _playClock >= TotalSeconds() - 0.05 Then _playClock = 0
            _playTimer.Start()
            _play.Text = "停止"
        End If
    End Sub

    Private Sub PlayStep()
        _playClock += _playTimer.Interval / 1000.0
        If _playClock >= TotalSeconds() Then _playClock = 0
        Dim v = CInt(_playClock / Math.Max(0.01, TotalSeconds()) * 1000)
        If v <> _pos.Value Then _pos.Value = Math.Min(1000, v) Else ShowAt(_playClock)
    End Sub

    '---------------------------------------------------------------------
    ' 輸出
    '---------------------------------------------------------------------

    Private Sub Export(gif As Boolean)
        If Sequence().Count < 2 OrElse _fc Is Nothing OrElse Not _fc.HasFace Then
            _status.Text = "先打勾至少兩張臉（照片要找得到臉）。"
            Return
        End If
        Directory.CreateDirectory(OutputFolder)
        Dim ext = If(gif, ".gif", ".mp4")
        Using dlg As New SaveFileDialog With {.Title = If(gif, "存成 GIF", "存成 MP4"), .Filter = If(gif, "GIF 動畫|*.gif", "MP4 影片|*.mp4"),
                                              .InitialDirectory = OutputFolder, .FileName = $"{_name}_川劇變臉{ext}"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ExportTo(dlg.FileName, gif)
        End Using
    End Sub

    ''' <summary>在背景輸出（測試也從這裡進）。</summary>
    Friend Sub ExportTo(file As String, gif As Boolean)
        Dim seq = Sequence()
        Dim tm = Timing()
        Dim side = If(_size.SelectedIndex = 2, Math.Max(_source.Width, _source.Height), Integer.Parse(CStr(_size.SelectedItem)))
        Dim img = CType(_source.Clone(), Bitmap)
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        SetExporting(True)
        _status.Text = "產生中…"
        Dim t0 = Environment.TickCount
        Threading.Tasks.Task.Run(Sub()
                                     Dim msg As String
                                     Try
                                         Using fc As New FaceChange(img, seq, side)
                                             Dim prog = Sub(c As Integer, n As Integer) BeginInvokeSafe(Sub() _progress.Value = Math.Min(100, c * 100 \ Math.Max(1, n)))
                                             If gif Then
                                                 fc.WriteGif(file, tm, Math.Min(side, 480), 15, prog, ct)
                                             Else
                                                 fc.WriteMp4(file, tm, prog, ct)
                                             End If
                                         End Using
                                         msg = $"完成（{(Environment.TickCount - t0) / 1000.0:0.0} 秒、{New FileInfo(file).Length / 1048576.0:0.0} MB）：{file}"
                                     Catch ex As OperationCanceledException
                                         msg = "已停止。"
                                         Try
                                             IO.File.Delete(file)
                                         Catch ex2 As IOException
                                         End Try
                                     Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is OpenCvSharp.OpenCVException OrElse TypeOf ex Is UnauthorizedAccessException
                                         msg = "輸出失敗：" & ex.Message
                                     Finally
                                         img.Dispose()
                                     End Try
                                     BeginInvokeSafe(Sub()
                                                         _status.Text = msg
                                                         SetExporting(False)
                                                         LastExportMessage = msg
                                                     End Sub)
                                 End Sub)
    End Sub

    Private Sub BeginInvokeSafe(a As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(a)
        Catch ex As InvalidOperationException
        End Try
    End Sub

    Private Sub SetExporting(busy As Boolean)
        _saveMp4.Enabled = Not busy
        _saveGif.Enabled = Not busy
        _cancel.Enabled = busy
        If Not busy Then _progress.Value = 0
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        _playTimer.Stop()
        _rebuild.Stop()
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _fc?.Dispose()
            _preview.Image?.Dispose()
            _playTimer.Dispose()
            _rebuild.Dispose()
            _font.Dispose()
            _small.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
