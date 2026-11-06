Imports System.Threading.Tasks
Imports PhotoEdit

''' <summary>
''' 影像 → 黑白照片上色…：黑白、泛黃的舊照片用 AI 上色。打開時把已下載的模型都算一次（處理中視窗），
''' 右邊每個模型一張小圖讓使用者挑；左邊大預覽可以拖中間的分隔線對照原圖。
''' 強度、保留原本色調、色溫、修復褪色在合起來時調（不用重跑模型）。按「套用」後由主畫面存成新影像打開，原照片不變。
''' </summary>
Friend Class frmColorize
    Inherits Aqua.AquaForm

    Private Const CanvasW As Integer = 760
    Private Const CanvasH As Integer = 640
    Private Const PanelW As Integer = 340
    Private Const TileW As Integer = 104
    ''' <summary>預覽用的大小（長邊）；套用時用原圖。</summary>
    Private Const PreviewSide As Integer = 1200

    Private ReadOnly _help As HelpTip
    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _bold As New Font(Lang.UiFont, 10.5F, FontStyle.Bold)

    Private ReadOnly _source As Bitmap   ' 原圖（呼叫的視窗的，不釋放）
    Private ReadOnly _small As Bitmap    ' 預覽大小的原圖
    Private ReadOnly _tone As (Tone As PhotoTone, Chroma As Double)
    Private ReadOnly _ab As New Dictionary(Of ColorizeModel, OpenCvSharp.Mat)()
    Private ReadOnly _thumbs As New Dictionary(Of ColorizeModel, Bitmap)()
    Private _model As ColorizeModel = ColorizeModel.DDColorTiny
    Private _settings As New ColorizeSettings()
    Private _preview As Bitmap
    Private _split As Double = 0.5
    Private _dragging As Boolean
    Private _busy As Boolean
    Private _syncing As Boolean

    Private ReadOnly _canvas As New PreviewCanvas()
    Private ReadOnly _toneLabel As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _tiles As New List(Of (Model As ColorizeModel, Box As Panel))()
    Private ReadOnly _run As New Button With {.Text = Lang.T("用這個模型上色"), .UseVisualStyleBackColor = True}
    Private ReadOnly _fixFading As New CheckBox With {.Text = Lang.T("修復褪色（亮度自動拉開）"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _sliders As New Dictionary(Of String, (Slider As Aqua.Slider, Value As Label))()
    Private ReadOnly _status As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(24, 95, 165)}
    Private ReadOnly _ok As New Button With {.Text = Lang.T("套用"), .UseVisualStyleBackColor = True}
    Private ReadOnly _cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel, .UseVisualStyleBackColor = True}
    Private ReadOnly _composeTimer As New Timer With {.Interval = 120}

    ''' <summary>套用的結果（原圖大小；OK 時才有）。</summary>
    Public Property Result As Bitmap

    Public Sub New(source As Bitmap, help As HelpTip)
        _source = source
        _help = help
        Dim k = Math.Min(1.0, PreviewSide / CDbl(Math.Max(source.Width, source.Height)))
        _small = New Bitmap(source, Math.Max(1, CInt(source.Width * k)), Math.Max(1, CInt(source.Height * k)))
        _tone = Colorizer.Analyze(_small)
        Text = Lang.T("黑白照片上色")
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        RoundCorners = False
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        Dim top = 23 + 10
        ClientSize = New Size(16 + CanvasW + 16 + PanelW + 16, top + CanvasH + 56)

        _canvas.SetBounds(16, top, CanvasW, CanvasH)
        _canvas.BackColor = Color.FromArgb(40, 40, 44)
        AddHandler _canvas.Paint, AddressOf PaintCanvas
        AddHandler _canvas.MouseDown, Sub(s, e)
                                          If e.Button = MouseButtons.Left Then _dragging = True : MoveSplit(e.X)
                                      End Sub
        AddHandler _canvas.MouseMove, Sub(s, e)
                                          If _dragging Then MoveSplit(e.X)
                                          _canvas.Cursor = If(_preview IsNot Nothing, Cursors.SizeWE, Cursors.Default)
                                      End Sub
        AddHandler _canvas.MouseUp, Sub() _dragging = False
        _help?.SetHelp("colorize.canvas", _canvas)
        Controls.Add(_canvas)

        BuildPanel(16 + CanvasW + 16, top)
        _ok.SetBounds(ClientSize.Width - 16 - 2 * 110 - 8, ClientSize.Height - 44, 110, 32)
        _cancel.SetBounds(ClientSize.Width - 16 - 110, ClientSize.Height - 44, 110, 32)
        _ok.Enabled = False
        AddHandler _ok.Click, Sub() ApplyResult()
        Controls.AddRange({_ok, _cancel})
        CancelButton = _cancel
        AddHandler _composeTimer.Tick, Sub()
                                           _composeTimer.Stop()
                                           RenderPreview()
                                       End Sub
        ThemeManager.Attach(Me)
    End Sub

    '=====================================================================
    ' 右邊
    '=====================================================================

    Private Sub BuildPanel(x As Integer, y As Integer)
        Dim heading = Function(text As String, yy As Integer)
                          Dim l As New Label With {.Text = text, .AutoSize = False, .Font = _bold, .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
                          l.SetBounds(x, yy, PanelW, 22)
                          Controls.Add(l)
                          Return yy + 26
                      End Function
        y = heading(Lang.T("照片"), y)
        _toneLabel.SetBounds(x, y, PanelW, 62)
        _toneLabel.Text = ToneText()
        Controls.Add(_toneLabel)
        y += 66

        y = heading(Lang.T("模型（點一張選）"), y)
        Dim i = 0
        For Each m In ColorizeModels.Order
            Dim model = m
            Dim box As New Panel With {.BackColor = Color.FromArgb(236, 238, 242), .Cursor = Cursors.Hand}
            box.SetBounds(x + i * (TileW + 14), y, TileW, TileW + 34)
            AddHandler box.Paint, Sub(s, e) PaintTile(e.Graphics, box, model)
            AddHandler box.Click, Sub() SelectModel(model)
            _help?.SetHelp("colorize.model", box)
            _tiles.Add((model, box))
            Controls.Add(box)
            i += 1
        Next
        y += TileW + 42
        _run.SetBounds(x, y, PanelW, 30)
        AddHandler _run.Click, Sub() ComputeAsync({_model}, force:=True)
        _help?.SetHelp("colorize.run", _run)
        Controls.Add(_run)
        y += 40

        y = heading(Lang.T("調整"), y)
        y = SliderRow(x, y, "strength", Lang.T("色彩強度"), 0, 200, 100, Sub(v) _settings.Strength = v, Function(n) n & "%")
        y = SliderRow(x, y, "keep", Lang.T("保留原本色調"), 0, 100, 0, Sub(v) _settings.KeepTone = v, Function(n) n & "%")
        y = SliderRow(x, y, "temp", Lang.T("色溫"), -100, 100, 0, Sub(v) _settings.Temperature = v, Function(n) n.ToString())
        _fixFading.Location = New Point(x, y + 2)
        _fixFading.Checked = _tone.Tone <> PhotoTone.Color
        _settings.FixFading = _fixFading.Checked
        AddHandler _fixFading.CheckedChanged, Sub()
                                                  _settings.FixFading = _fixFading.Checked
                                                  SchedulePreview()
                                              End Sub
        _help?.SetHelp("colorize.fixfading", _fixFading)
        Controls.Add(_fixFading)
        y += 32
        Dim reset As New Button With {.Text = Lang.T("重設調整"), .UseVisualStyleBackColor = True}
        reset.SetBounds(x, y, 110, 28)
        AddHandler reset.Click, Sub() ResetSettings()
        _help?.SetHelp("colorize.reset", reset)
        Controls.Add(reset)
        _status.SetBounds(x, ClientSize.Height - 56 - 52, PanelW, 48)
        Controls.Add(_status)
    End Sub

    Private Function SliderRow(x As Integer, y As Integer, key As String, caption As String, min As Integer, max As Integer, value As Integer,
                               changed As Action(Of Integer), fmt As Func(Of Integer, String)) As Integer
        Dim cap As New Label With {.Text = caption, .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
        cap.SetBounds(x, y, 100, 26)
        Dim s As New Aqua.Slider With {.Minimum = min, .Maximum = max, .Value = value, .ShowTicks = False}
        s.SetBounds(x + 102, y + 1, PanelW - 102 - 56, 24)
        Dim v As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent, .Text = fmt(value)}
        v.SetBounds(x + PanelW - 54, y, 54, 26)
        AddHandler s.ValueChanged, Sub()
                                       v.Text = fmt(s.Value)
                                       changed(s.Value)
                                       If Not _syncing Then SchedulePreview()
                                   End Sub
        _help?.SetHelpLinked("colorize." & key, s, cap, s, v)
        Controls.AddRange({cap, s, v})
        _sliders(key) = (s, v)
        Return y + 32
    End Function

    Private Function ToneText() As String
        Select Case _tone.Tone
            Case PhotoTone.Gray : Return Lang.T("黑白照片：可以直接上色。")
            Case PhotoTone.Toned : Return Lang.T("單色調的舊照片（泛黃、褐色等）：上色時會先去掉原本的色調；想留一點舊照片的味道可以調「保留原本色調」。")
            Case Else : Return Lang.T("這張已經是彩色照片：上色會換掉原本的顏色（適合褪色很嚴重的照片）。")
        End Select
    End Function

    Private Sub ResetSettings()
        _syncing = True
        _sliders("strength").Slider.Value = 100
        _sliders("keep").Slider.Value = 0
        _sliders("temp").Slider.Value = 0
        _fixFading.Checked = _tone.Tone <> PhotoTone.Color
        _syncing = False
        SchedulePreview()
    End Sub

    '=====================================================================
    ' 計算
    '=====================================================================

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Dim ready = ColorizeModels.Order.Where(Function(m) ColorizeModels.Available(m)).ToArray()
        If ready.Length = 0 Then
            _status.Text = Lang.T("還沒有上色模型：請到 設定 → 模型 下載「黑白照片上色」的模型。")
            Return
        End If
        _model = ready(0)
        ComputeAsync(ready, force:=False)
    End Sub

    ''' <summary>算這些模型（處理中視窗，一個模型一個步驟）；算過的不重算（force＝True 時重算）。完成後回傳。測試也用這個。</summary>
    Friend Async Function ComputeAsync(models As ColorizeModel(), force As Boolean) As Task
        If _busy Then Return
        Dim todo = models.Where(Function(m) ColorizeModels.Available(m) AndAlso (force OrElse Not _ab.ContainsKey(m))).ToArray()
        If todo.Length = 0 Then
            If Not ColorizeModels.Available(_model) Then _status.Text = Lang.F("「{0}」模型還沒下載：請到 設定 → 模型 下載。", ColorizeModels.DisplayName(_model))
            RenderPreview()
            Return
        End If
        _busy = True
        _run.Enabled = False
        Dim copy = DirectCast(_source.Clone(), Bitmap)
        Dim watch = Diagnostics.Stopwatch.StartNew()
        Try
            Dim results = Await BusyDialog.RunAsync(Me, Lang.T("上色中"),
                                                    Function(p)
                                                        Dim list As New List(Of (Model As ColorizeModel, Ab As OpenCvSharp.Mat))()
                                                        For i = 0 To todo.Length - 1
                                                            p.Stage(i + 1, todo.Length, Lang.F("AI 上色：{0}", ColorizeModels.DisplayName(todo(i))))
                                                            list.Add((todo(i), Colorizer.PredictAb(copy, todo(i))))
                                                        Next
                                                        Return list
                                                    End Function, Lang.T("第一次載入模型會久一點。"))
            If IsDisposed Then
                For Each r In results
                    r.Ab.Dispose()
                Next
                Return
            End If
            For Each r In results
                Dim old As OpenCvSharp.Mat = Nothing
                If _ab.TryGetValue(r.Model, old) Then old.Dispose()
                _ab(r.Model) = r.Ab
                MakeThumb(r.Model)
            Next
            If Not _ab.ContainsKey(_model) Then _model = results(0).Model
            _status.Text = Lang.F("上色完成（{0:0.0} 秒）。點右邊的小圖換模型；拖預覽中間的線對照原圖。", watch.Elapsed.TotalSeconds)
            RenderPreview()
        Catch ex As Exception ' 模型、記憶體的各種錯誤都只顯示在狀態列
            _status.Text = Lang.T("上色失敗：") & ex.Message
        Finally
            copy.Dispose()
            _busy = False
            _run.Enabled = True
            For Each t In _tiles
                t.Box.Invalidate()
            Next
        End Try
    End Function

    Private Sub MakeThumb(m As ColorizeModel)
        Dim old As Bitmap = Nothing
        If _thumbs.TryGetValue(m, old) Then old.Dispose()
        Dim k = Math.Min(TileW / CDbl(_small.Width), TileW / CDbl(_small.Height))
        Using tiny As New Bitmap(_small, Math.Max(1, CInt(_small.Width * k)), Math.Max(1, CInt(_small.Height * k)))
            _thumbs(m) = Colorizer.Compose(tiny, _ab(m), New ColorizeSettings With {.FixFading = _settings.FixFading})
        End Using
    End Sub

    ''' <summary>選模型：算過的直接換；沒算過的現在算。測試也用這個。</summary>
    Friend Function SelectModel(m As ColorizeModel) As Task
        If _busy Then Return Task.CompletedTask
        _model = m
        For Each t In _tiles
            t.Box.Invalidate()
        Next
        If Not ColorizeModels.Available(m) Then
            _status.Text = Lang.F("「{0}」模型還沒下載：請到 設定 → 模型 下載。", ColorizeModels.DisplayName(m))
            Return Task.CompletedTask
        End If
        If _ab.ContainsKey(m) Then
            _status.Text = Lang.F("模型：{0}", ColorizeModels.DisplayName(m))
            RenderPreview()
            Return Task.CompletedTask
        End If
        Return ComputeAsync({m}, force:=False)
    End Function

    Private Sub SchedulePreview()
        _composeTimer.Stop()
        _composeTimer.Start()
    End Sub

    Private Sub RenderPreview()
        _composeTimer.Stop()
        Dim ab As OpenCvSharp.Mat = Nothing
        If Not _ab.TryGetValue(_model, ab) Then Return
        Dim bmp = Colorizer.Compose(_small, ab, _settings)
        _preview?.Dispose()
        _preview = bmp
        _ok.Enabled = True
        _canvas.Invalidate()
    End Sub

    '=====================================================================
    ' 預覽
    '=====================================================================

    Private Function ImageRect() As RectangleF
        Dim k = Math.Min((CanvasW - 24) / CDbl(_small.Width), (CanvasH - 24) / CDbl(_small.Height))
        Dim w = _small.Width * k, h = _small.Height * k
        Return New RectangleF(CSng((CanvasW - w) / 2), CSng((CanvasH - h) / 2), CSng(w), CSng(h))
    End Function

    Private Sub MoveSplit(x As Integer)
        Dim r = ImageRect()
        _split = Math.Clamp((x - r.X) / r.Width, 0, 1)
        _canvas.Invalidate()
    End Sub

    ''' <summary>左邊原圖、右邊上色後，中間一條可以拖的分隔線。</summary>
    Private Sub PaintCanvas(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear
        Dim r = ImageRect()
        g.DrawImage(_small, r)
        If _preview Is Nothing Then Return
        Dim sx = CSng(r.X + r.Width * _split)
        g.SetClip(New RectangleF(sx, r.Y, r.Right - sx, r.Height))
        g.DrawImage(_preview, r)
        g.ResetClip()
        Using pen As New Pen(Color.White, 2)
            g.DrawLine(pen, sx, r.Y, sx, r.Bottom)
        End Using
        Using f As New Font(Lang.UiFont, 9.5F, FontStyle.Bold), back As New SolidBrush(Color.FromArgb(150, 0, 0, 0))
            For Each lab In {(Lang.T("原圖"), r.X + 8, sx > r.X + 70), (Lang.T("上色"), sx + 8, sx < r.Right - 70)}
                If Not lab.Item3 Then Continue For
                Dim size = TextRenderer.MeasureText(lab.Item1, f)
                g.FillRectangle(back, lab.Item2 - 4, r.Y + 8, size.Width + 8, size.Height + 4)
                TextRenderer.DrawText(g, lab.Item1, f, New Point(CInt(lab.Item2), CInt(r.Y + 10)), Color.White)
            Next
        End Using
    End Sub

    Private Sub PaintTile(g As Graphics, box As Panel, m As ColorizeModel)
        Dim selected = m = _model
        Dim img As Bitmap = Nothing
        Dim area As New Rectangle(0, 0, TileW, TileW)
        If _thumbs.TryGetValue(m, img) Then
            g.DrawImage(img, (TileW - img.Width) \ 2, (TileW - img.Height) \ 2, img.Width, img.Height)
        Else
            Dim msg = If(ColorizeModels.Available(m), If(_busy, Lang.T("計算中…"), Lang.T("點一下計算")), Lang.T("未下載"))
            TextRenderer.DrawText(g, msg, Font, area, Color.FromArgb(120, 126, 138), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
        End If
        Using f As New Font(Lang.UiFont, 8.5F, If(selected, FontStyle.Bold, FontStyle.Regular))
            TextRenderer.DrawText(g, ColorizeModels.DisplayName(m), f, New Rectangle(0, TileW + 2, TileW, 30), If(selected, Color.FromArgb(24, 60, 120), Color.FromArgb(70, 76, 88)),
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.WordBreak)
        End Using
        If selected Then
            Using pen As New Pen(Color.FromArgb(47, 128, 237), 3)
                g.DrawRectangle(pen, 1, 1, box.Width - 3, box.Height - 3)
            End Using
        End If
    End Sub

    '=====================================================================
    ' 套用
    '=====================================================================

    ''' <summary>原圖大小合起來，關閉（測試也用這個）。</summary>
    Friend Sub ApplyResult()
        Dim ab As OpenCvSharp.Mat = Nothing
        If Not _ab.TryGetValue(_model, ab) Then Return
        Cursor = Cursors.WaitCursor
        Try
            Result = Colorizer.Compose(_source, ab, _settings)
        Finally
            Cursor = Cursors.Default
        End Try
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ' 測試用
    Friend ReadOnly Property Tone As PhotoTone
        Get
            Return _tone.Tone
        End Get
    End Property
    Friend ReadOnly Property CurrentModel As ColorizeModel
        Get
            Return _model
        End Get
    End Property
    Friend ReadOnly Property ComputedModels As ColorizeModel()
        Get
            Return _ab.Keys.ToArray()
        End Get
    End Property
    Friend ReadOnly Property Preview As Bitmap
        Get
            Return _preview
        End Get
    End Property
    Friend ReadOnly Property IsBusy As Boolean
        Get
            Return _busy OrElse _composeTimer.Enabled
        End Get
    End Property
    Friend Sub SetValue(key As String, v As Integer)
        _sliders(key).Slider.Value = v
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _composeTimer.Dispose()
            _small.Dispose()
            _preview?.Dispose()
            For Each m In _ab.Values
                m.Dispose()
            Next
            For Each t In _thumbs.Values
                t.Dispose()
            Next
            _font.Dispose()
            _bold.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    Private NotInheritable Class PreviewCanvas
        Inherits Control
        Public Sub New()
            SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        End Sub
    End Class
End Class
