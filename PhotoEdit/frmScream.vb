Imports System.Threading.Tasks
Imports PhotoEdit

''' <summary>
''' 影像 → 吶喊（孟克）…：目前的照片 → 臉變成《吶喊》的臉 → AI 去背 → 人物放到背景（預設內建的《吶喊》名畫，可換別張）。
''' 打開時先找臉、用最快的人像模型（MODNet）去背；之後換模型要按「去背」才重算。去背用共用的去背面板（邊緣、筆刷修正）。
''' 左邊預覽可以拖曳人物、滾輪縮放（檢查遮罩或用筆刷時改顯示原照片＋遮罩）；右邊調整臉、位置大小、人物的色相／飽和度／亮度／對比／色溫，以及建議的位置與配色。
''' 按「套用」後由主畫面把背景存成新影像打開，人物放成一個圖片圖層（之後還能在主畫面拖曳、縮放）。
''' </summary>
Friend Class frmScream
    Inherits Aqua.AquaForm

    Private Const CanvasW As Integer = 760
    Private Const CanvasH As Integer = 680
    Private Const PanelW As Integer = 360
    ''' <summary>人物的工作尺寸（長邊）：預覽與輸出的人物圖層都用這個大小。</summary>
    Private Const WorkSide As Integer = 1600

    Private ReadOnly _help As HelpTip
    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _bold As New Font(Lang.UiFont, 10.5F, FontStyle.Bold)

    ' 人物來源（工作尺寸）與臉
    Private ReadOnly _src As Bitmap
    Private _faces As New List(Of FaceRegion)()
    Private ReadOnly _srcPx As Byte()
    ' 背景
    Private _backdrop As Bitmap
    Private _backdropView As Bitmap
    Private ReadOnly _backdropFiles As New List(Of String)() ' 背景下拉第 1 項之後的自選圖片
    ' 做好的人物圖層與位置（畫布 0..1）
    Private _layer As Bitmap
    Private _croppedAtBottom As Boolean
    Private _x As Double = 0.5, _y As Double = 0.65, _size As Double = 0.62
    Private _placed As Boolean
    Private _suggestIndex As Integer = -1
    Private _color As New ScreamPersonColor()
    Private _syncing As Boolean
    Private _dragFrom As Point?
    Private _dragStart As PointF
    Private _hover As Point?
    Private _painting As Boolean

    ' 介面
    Private ReadOnly _canvas As New ScreamCanvas()
    Private ReadOnly _panel As New Panel With {.AutoScroll = True, .BackColor = Color.Transparent}
    Private ReadOnly _status As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(24, 95, 165)}
    Private ReadOnly _backdropBox As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private _cut As CutoutPanel
    Private ReadOnly _bodyBox As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _flip As New CheckBox With {.Text = Lang.T("左右翻轉"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _faceOnly As New CheckBox With {.Text = Lang.T("只調整臉"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _ok As New Button With {.Text = Lang.T("套用")}
    Private ReadOnly _cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel}
    Private ReadOnly _sliders As New Dictionary(Of String, (Slider As Aqua.Slider, Value As Label))()
    Private ReadOnly _rebuildTimer As New Timer With {.Interval = 200}
    Private _rebuildFace As Boolean
    Private _rebuildMask As Boolean
    ' 同時只算一個；算的途中又有要求就記下來，算完再算一次（不會用到舊的快取）
    Private _running As Task
    Private _queued, _queuedFace As Boolean

    ''' <summary>套用的結果（OK 時才有）：背景（畫布大小）、人物圖層、圖層中心與高度（畫布比例）。</summary>
    Public Property ResultBackdrop As Bitmap
    Public Property ResultLayer As Bitmap
    Public Property ResultX As Double
    Public Property ResultY As Double
    Public Property ResultSize As Double

    Public Sub New(source As Bitmap, help As HelpTip)
        _help = help
        Dim k = Math.Min(1.0, WorkSide / CDbl(Math.Max(source.Width, source.Height)))
        _src = New Bitmap(Math.Max(1, CInt(source.Width * k)), Math.Max(1, CInt(source.Height * k)), Imaging.PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(_src)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
            g.DrawImage(source, 0, 0, _src.Width, _src.Height)
        End Using
        _srcPx = CompositeBaker.ReadPixels(_src)
        Text = Lang.T("吶喊（孟克）")
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
        AddHandler _canvas.MouseDown, AddressOf CanvasDown
        AddHandler _canvas.MouseMove, AddressOf CanvasMove
        AddHandler _canvas.MouseUp, AddressOf CanvasUp
        AddHandler _canvas.MouseLeave, Sub()
                                           _hover = Nothing
                                           _canvas.Invalidate()
                                       End Sub
        AddHandler _canvas.MouseWheel, AddressOf CanvasWheel
        _help?.SetHelp("scream.canvas", _canvas)
        Controls.Add(_canvas)

        Dim px = 16 + CanvasW + 16
        _panel.SetBounds(px, top, PanelW, CanvasH - 50)
        Controls.Add(_panel)
        _status.SetBounds(px, top + CanvasH - 46, PanelW, 46)
        Controls.Add(_status)
        _ok.SetBounds(ClientSize.Width - 16 - 2 * 110 - 8, ClientSize.Height - 44, 110, 32)
        _cancel.SetBounds(ClientSize.Width - 16 - 110, ClientSize.Height - 44, 110, 32)
        _ok.Enabled = False
        AddHandler _ok.Click, Sub() ApplyResult()
        Controls.AddRange({_ok, _cancel})
        CancelButton = _cancel
        BuildPanel()
        AddHandler _rebuildTimer.Tick, Sub()
                                           _rebuildTimer.Stop()
                                           RebuildAsync(_rebuildFace)
                                           _rebuildFace = False
                                       End Sub
        SetBackdrop(ScreamStudio.DefaultBackdrop())
    End Sub

    '=====================================================================
    ' 右邊的控制項
    '=====================================================================

    Private _y0 As Integer

    Private Sub Heading(text As String)
        Dim l As New Label With {.Text = text, .AutoSize = False, .Font = _bold, .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
        l.SetBounds(0, _y0 + 4, PanelW - 24, 22)
        _panel.Controls.Add(l)
        _y0 += 27
    End Sub

    Private Sub SliderRow(key As String, caption As String, min As Integer, max As Integer, value As Integer, changed As Action(Of Integer), Optional fmt As Func(Of Integer, String) = Nothing)
        Dim cap As New Label With {.Text = caption, .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
        cap.SetBounds(4, _y0, 80, 26)
        Dim s As New Aqua.Slider With {.Minimum = min, .Maximum = max, .Value = value, .ShowTicks = False}
        s.SetBounds(86, _y0 + 1, PanelW - 24 - 86 - 56, 24)
        Dim v As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        v.SetBounds(PanelW - 24 - 54, _y0, 52, 26)
        Dim f = If(fmt, Function(n As Integer) n.ToString())
        v.Text = f(value)
        AddHandler s.ValueChanged, Sub()
                                       v.Text = f(s.Value)
                                       If Not _syncing Then changed(s.Value)
                                   End Sub
        AddHandler v.DoubleClick, Sub() s.Value = Math.Max(min, Math.Min(max, 0))
        _help?.SetHelp("scream." & key, s)
        _help?.SetHelp("scream." & key, cap)
        _panel.Controls.AddRange({cap, s, v})
        _sliders(key) = (s, v)
        _y0 += 28
    End Sub

    Private Function AddButton(key As String, text As String, x As Integer, w As Integer, click As Action) As Button
        Dim b As New Button With {.Text = text}
        b.SetBounds(x, _y0, w, 28)
        AddHandler b.Click, Sub() click()
        _help?.SetHelp("scream." & key, b)
        _panel.Controls.Add(b)
        Return b
    End Function

    Private Sub BuildPanel()
        _y0 = 0
        Heading(Lang.T("背景"))
        _backdropBox.Items.AddRange({Lang.T("《吶喊》名畫（內建）"), Lang.T("選擇圖片…")})
        _backdropBox.SelectedIndex = 0
        _backdropBox.SetBounds(4, _y0, PanelW - 30, 26)
        AddHandler _backdropBox.SelectedIndexChanged, AddressOf BackdropChanged
        _help?.SetHelp("scream.backdrop", _backdropBox)
        _panel.Controls.Add(_backdropBox)
        _y0 += 32

        Heading(Lang.T("去背"))
        _cut = New CutoutPanel(_help, PanelW - 30, Lang.T("去背"), portrait:=True) With {.Location = New Point(4, _y0), .StretchFaint = True}
        _cut.SetFigure(_src, _srcPx, Nothing, False)
        AddHandler _cut.MaskChanged, Sub() ScheduleMaskRebuild()
        AddHandler _cut.OptionsChanged, Sub() ScheduleMaskRebuild()
        AddHandler _cut.ViewChanged, Sub() _canvas.Invalidate()
        AddHandler _cut.StatusChanged, Sub(sender, text) _status.Text = text
        _panel.Controls.Add(_cut)
        _y0 += _cut.Height + 6

        Heading(Lang.T("吶喊的臉"))
        SliderRow("face", Lang.T("強度"), 0, 100, 100, Sub(v) ScheduleRebuild(True))
        SliderRow("eyesize", Lang.T("眼睛大小"), 0, 100, 40, Sub(v) ScheduleRebuild(True))
        SliderRow("eyes", Lang.T("眼圈"), 0, 100, 40, Sub(v) ScheduleRebuild(True))
        SliderRow("mouthsize", Lang.T("嘴巴大小"), 0, 100, 50, Sub(v) ScheduleRebuild(True))
        SliderRow("mouthround", Lang.T("嘴巴圓度"), 0, 100, 100, Sub(v) ScheduleRebuild(True))
        SliderRow("body", Lang.T("身體扭曲"), 0, 100, 70, Sub(v) ScheduleRebuild(True))
        Dim bodyCap As New Label With {.Text = Lang.T("身體"), .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
        bodyCap.SetBounds(4, _y0, 80, 26)
        _bodyBox.Items.AddRange({Lang.T("照片的身體"), Lang.T("原作的手和身體")})
        _bodyBox.SelectedIndex = 0
        _bodyBox.SetBounds(86, _y0, PanelW - 24 - 86 - 2, 26)
        _bodyBox.Enabled = ScreamFigure.Available
        AddHandler _bodyBox.SelectedIndexChanged, Sub() BodyModeChanged()
        _help?.SetHelp("scream.bodymode", _bodyBox)
        _help?.SetHelp("scream.bodymode", bodyCap)
        _panel.Controls.AddRange({bodyCap, _bodyBox})
        _y0 += 32

        Heading(Lang.T("位置與大小"))
        SliderRow("size", Lang.T("大小"), 10, 150, 62, Sub(v)
                                                         _size = v / 100.0
                                                         _canvas.Invalidate()
                                                     End Sub, Function(n) n & "%")
        AddButton("suggestpos", Lang.T("建議位置"), 4, 120, AddressOf NextSuggestion)
        _flip.Location = New Point(140, _y0 + 4)
        AddHandler _flip.CheckedChanged, Sub()
                                             If Not _syncing Then ScheduleRebuild(False)
                                         End Sub
        _help?.SetHelp("scream.flip", _flip)
        _panel.Controls.Add(_flip)
        _y0 += 32

        Heading(Lang.T("人物色彩"))
        _faceOnly.Location = New Point(4, _y0)
        AddHandler _faceOnly.CheckedChanged, Sub()
                                                 _color.FaceOnly = _faceOnly.Checked
                                                 If Not _syncing Then ScheduleRebuild(False)
                                             End Sub
        _help?.SetHelp("scream.faceonly", _faceOnly)
        _panel.Controls.Add(_faceOnly)
        _y0 += 28
        Dim colorChanged = Sub() ScheduleRebuild(False)
        SliderRow("hue", Lang.T("色相"), -180, 180, 0, Sub(v)
                                                         _color.Hue = v : colorChanged()
                                                     End Sub, Function(n) n & "°")
        SliderRow("sat", Lang.T("飽和度"), -100, 100, 0, Sub(v)
                                                          _color.Saturation = v : colorChanged()
                                                      End Sub)
        SliderRow("bright", Lang.T("亮度"), -100, 100, 0, Sub(v)
                                                         _color.Brightness = v : colorChanged()
                                                     End Sub)
        SliderRow("contrast", Lang.T("對比"), -100, 100, 0, Sub(v)
                                                           _color.Contrast = v : colorChanged()
                                                       End Sub)
        SliderRow("temp", Lang.T("色溫"), -100, 100, 0, Sub(v)
                                                       _color.Temperature = v : colorChanged()
                                                   End Sub)
        AddButton("autocolor", Lang.T("自動配色（建議）"), 4, 160, AddressOf AutoColor)
        AddButton("resetcolor", Lang.T("重設色彩"), 172, 110, Sub() SetColor(New ScreamPersonColor With {.FaceOnly = _faceOnly.Checked}))
        _y0 += 32

        Heading(Lang.T("融合"))
        SliderRow("oil", Lang.T("油畫感"), 0, 100, 85, Sub(v) ScheduleRebuild(True))
        SliderRow("stroke", Lang.T("筆觸"), 0, 100, 30, Sub(v) ScheduleRebuild(True))
        SliderRow("feather", Lang.T("邊緣柔和"), 0, 100, 20, Sub(v) ScheduleRebuild(False))
        _y0 += 8
    End Sub

    ''' <summary>換身體：人物的形狀變了，位置重新建議；原作身體用不到扭曲和筆觸。</summary>
    Private Sub BodyModeChanged()
        Dim figure = _bodyBox.SelectedIndex = 1
        _sliders("body").Slider.Enabled = Not figure
        _sliders("stroke").Slider.Enabled = Not figure
        _placed = False
        _suggestIndex = -1
        ScheduleRebuild(True)
    End Sub

    Private Function SliderValue(key As String) As Integer
        Return _sliders(key).Slider.Value
    End Function

    Private Sub SetSlider(key As String, v As Integer)
        Dim s = _sliders(key).Slider
        s.Value = Math.Max(s.Minimum, Math.Min(s.Maximum, v))
    End Sub

    Private Sub SetColor(c As ScreamPersonColor)
        _syncing = True
        Try
            _color = c.Clone()
            SetSlider("hue", c.Hue) : SetSlider("sat", c.Saturation) : SetSlider("bright", c.Brightness)
            SetSlider("contrast", c.Contrast) : SetSlider("temp", c.Temperature)
            _faceOnly.Checked = c.FaceOnly
        Finally
            _syncing = False
        End Try
        ScheduleRebuild(False)
    End Sub

    '=====================================================================
    ' 背景
    '=====================================================================

    Private Sub SetBackdrop(bmp As Bitmap)
        If bmp Is Nothing Then
            _status.Text = Lang.T("找不到背景圖，請用「選擇圖片…」挑一張。")
            Return
        End If
        _backdrop?.Dispose()
        _backdrop = bmp
        _backdropView?.Dispose()
        Dim r = BackdropRect()
        _backdropView = New Bitmap(Math.Max(1, CInt(r.Width)), Math.Max(1, CInt(r.Height)))
        Using g = Graphics.FromImage(_backdropView)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.DrawImage(_backdrop, 0, 0, _backdropView.Width, _backdropView.Height)
        End Using
        ' 背景比例變了：建議位置重算
        If _layer IsNot Nothing Then
            _suggestIndex = -1
            NextSuggestion()
        End If
        _canvas.Invalidate()
    End Sub

    Private Sub BackdropChanged(sender As Object, e As EventArgs)
        If _syncing Then Return
        Dim i = _backdropBox.SelectedIndex
        Dim pickIndex = _backdropBox.Items.Count - 1
        If i = 0 Then
            SetBackdrop(ScreamStudio.DefaultBackdrop())
        ElseIf i = pickIndex Then
            Using dlg As New OpenFileDialog With {.Title = Lang.T("選擇背景圖片"), .Filter = Lang.T("圖片") & "|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif;*.tiff|" & Lang.T("所有檔案") & "|*.*"}
                If dlg.ShowDialog(Me) = DialogResult.OK Then
                    LoadBackdropFile(dlg.FileName)
                Else
                    _syncing = True
                    _backdropBox.SelectedIndex = 0
                    _syncing = False
                End If
            End Using
        Else
            Dim bmp = ScreamStudio.LoadBackdrop(_backdropFiles(i - 1))
            If bmp IsNot Nothing Then SetBackdrop(bmp)
        End If
    End Sub

    ''' <summary>換成自選的背景圖（測試也用這個）。</summary>
    Friend Function LoadBackdropFile(file As String) As Boolean
        Dim bmp = ScreamStudio.LoadBackdrop(file)
        If bmp Is Nothing Then
            _status.Text = Lang.T("無法讀取這張圖片。")
            Return False
        End If
        _syncing = True
        Try
            _backdropFiles.Add(file)
            _backdropBox.Items.Insert(_backdropBox.Items.Count - 1, IO.Path.GetFileName(file))
            _backdropBox.SelectedIndex = _backdropBox.Items.Count - 2
        Finally
            _syncing = False
        End Try
        SetBackdrop(bmp)
        Return True
    End Function

    ''' <summary>背景在預覽上的位置（等比例放進預覽區）。</summary>
    Private Function BackdropRect() As RectangleF
        If _backdrop Is Nothing Then Return New RectangleF(0, 0, CanvasW, CanvasH)
        Dim k = Math.Min(CanvasW / CDbl(_backdrop.Width), CanvasH / CDbl(_backdrop.Height))
        Dim w = _backdrop.Width * k, h = _backdrop.Height * k
        Return New RectangleF(CSng((CanvasW - w) / 2), CSng((CanvasH - h) / 2), CSng(w), CSng(h))
    End Function

    '=====================================================================
    ' 計算（背景執行）
    '=====================================================================

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        PrepareAsync()
    End Sub

    ''' <summary>
    ''' 找臉（含 68 點）→ 用最快的人像模型去背（處理中視窗，兩個步驟）→ 做圖層。完成後回傳。測試也用這個。
    ''' </summary>
    Friend Async Function PrepareAsync() As Task
        _preparing = True
        Try
            Await PrepareCore()
        Finally
            _preparing = False
        End Try
    End Function

    Private _preparing As Boolean

    Private Async Function PrepareCore() As Task
        _status.Text = Lang.T("找臉、去背中…")
        Dim copy = DirectCast(_src.Clone(), Bitmap)
        Dim model = _cut.Model
        Dim canCut = BackgroundRemover.ModelAvailable(model)
        Dim r As (Faces As List(Of FaceRegion), Mask As Byte())
        Try
            r = Await BusyDialog.RunAsync(Me, Lang.T("準備吶喊"),
                                          Function(p)
                                              Dim stages = If(canCut, 2, 1)
                                              p.Stage(1, stages, Lang.T("找臉與臉部特徵點"))
                                              Dim found As New List(Of FaceRegion)()
                                              If FaceDetector.ModelAvailable() Then
                                                  Using det As New FaceDetector()
                                                      found = det.Detect(copy)
                                                  End Using
                                                  If found.Count > 0 Then
                                                      FaceMesh.Fit(copy, found)
                                                      FaceLandmarks.Fit(copy, found.Where(Function(x) x.Dense Is Nothing).ToList())
                                                  End If
                                              End If
                                              Dim mask As Byte() = Nothing
                                              If canCut Then
                                                  p.Stage(2, 2, Lang.F("AI 去背：{0}", CutoutModels.DisplayName(model)))
                                                  mask = CutoutPanel.ComputeAi(copy, model, True)
                                              End If
                                              Return (found, mask)
                                          End Function, Lang.T("第一次載入模型會久一點。"))
        Catch ex As Exception ' 模型、記憶體的各種錯誤都只顯示在狀態列
            If Not IsDisposed Then _status.Text = Lang.T("處理失敗：") & ex.Message
            Return
        Finally
            copy.Dispose()
        End Try
        If IsDisposed Then Return
        _faces = r.Faces
        If r.Mask Is Nothing Then
            _status.Text = Lang.F("「{0}」模型還沒下載：請到 設定 → 模型 下載（或換一個已下載的模型）。", CutoutModels.DisplayName(model))
            Return
        End If
        _cut.ApplyAiMask(r.Mask, model)
        _rebuildTimer.Stop()
        Await RebuildAsync(True)
    End Function

    Private Sub ScheduleRebuild(face As Boolean)
        _rebuildFace = _rebuildFace OrElse face
        _rebuildTimer.Stop()
        _rebuildTimer.Start()
    End Sub

    ''' <summary>去背面板的遮罩或去除殘色改了：遮罩重新變形、圖層重做（臉不用重算）。</summary>
    Private Sub ScheduleMaskRebuild()
        _rebuildMask = True
        ScheduleRebuild(False)
    End Sub

    ' 快取：臉＋筆觸處理過的人物圖、變形後的遮罩、去除殘色後的人物圖（做圖層用）
    Private _faceImg As Bitmap
    Private _warped As Byte()
    Private _layerSrc As Bitmap
    ' 圖層來源的大小與臉的位置（原作身體時和照片不同）
    Private _layerW, _layerH As Integer
    Private _layerFaces As IReadOnlyList(Of FaceRegion)
    ' 原作身體在原畫裡的位置（內建背景是同一張畫，建議位置第一個就是它）
    Private _figureSpot As ScreamPlacement?

    ''' <summary>還在算（測試用：等這個變成 False）。</summary>
    Friend ReadOnly Property IsBusy As Boolean
        Get
            Return _preparing OrElse _running IsNot Nothing OrElse _queued OrElse _rebuildTimer.Enabled OrElse _cut.Busy
        End Get
    End Property

    ''' <summary>
    ''' 重算人物圖層：face＝True 時從臉（和筆觸）開始重算；遮罩改了時重新變形、去除殘色；否則只重做圖層（色彩、柔和、翻轉）。
    ''' 完成後回傳。
    ''' </summary>
    Friend Function RebuildAsync(face As Boolean) As Task
        If _running IsNot Nothing Then
            _queued = True
            _queuedFace = _queuedFace OrElse face
            Return _running
        End If
        Dim mask = _cut.Mask
        If mask Is Nothing Then Return Task.CompletedTask
        Dim scream = SliderValue("face"), eyes = SliderValue("eyes"), stroke = SliderValue("stroke"), oil = SliderValue("oil"), body = SliderValue("body"), feather = SliderValue("feather")
        Dim shape As New ScreamShape With {.EyeSize = SliderValue("eyesize"), .MouthSize = SliderValue("mouthsize"), .MouthRound = SliderValue("mouthround")}
        Dim color = _color.Clone(), flip = _flip.Checked
        Dim faces = _faces
        Dim figure = _bodyBox.SelectedIndex = 1
        ' 原作身體：頭、手、身體是一起畫的，遮罩改了也要整個重做
        Dim needFace = face OrElse _faceImg Is Nothing OrElse (figure AndAlso (_rebuildMask OrElse _layerSrc Is Nothing))
        Dim needMask = needFace OrElse _rebuildMask OrElse _warped Is Nothing OrElse _layerSrc Is Nothing
        _rebuildMask = False
        Dim src = If(needFace, DirectCast(_src.Clone(), Bitmap), Nothing)
        Dim faceImg = If(needFace OrElse Not needMask, Nothing, DirectCast(_faceImg.Clone(), Bitmap))
        Dim layerSrc = If(needMask, Nothing, DirectCast(_layerSrc.Clone(), Bitmap))
        Dim maskCopy = DirectCast(mask.Clone(), Byte())
        Dim warped = _warped
        Dim opts = _cut.Options.Clone(), model = _cut.MaskModel
        Dim w = If(needFace, _src.Width, _layerW), h = If(needFace, _src.Height, _layerH)
        Dim layerFaces As IReadOnlyList(Of FaceRegion) = If(needFace, faces, _layerFaces)
        If needFace Then _status.Text = Lang.T("處理中…")
        _running = Task.Run(Function()
                                Dim fi As Bitmap = Nothing
                                Dim ls As Bitmap = Nothing
                                Dim noFigure = False
                                Dim spot As ScreamPlacement? = Nothing
                                If needFace AndAlso figure Then
                                    Dim fig = ScreamFigure.Build(src, maskCopy, faces, scream, eyes, shape, oil)
                                    If fig IsNot Nothing Then
                                        fi = fig.Image
                                        ls = DirectCast(fi.Clone(), Bitmap)
                                        warped = fig.Alpha
                                        layerFaces = fig.Faces
                                        spot = fig.Spot
                                        w = fi.Width : h = fi.Height
                                        src.Dispose()
                                    Else
                                        noFigure = True
                                    End If
                                End If
                                If needFace AndAlso fi Is Nothing Then
                                    Using skin = ScreamStudio.WarmSkin(src, faces, scream), fx = ScreamStudio.FaceEffect(skin, faces, scream, eyes, shape)
                                        ' 先扭曲再畫油畫，筆觸才會順著扭曲後的身體走
                                        Using tinted = ScreamStudio.StrokeTint(fx, faces, stroke)
                                            fi = ScreamStudio.BodyWarp(tinted, faces, body)
                                        End Using
                                    End Using
                                    ScreamStudio.OilPaint(fi, oil, faces)
                                    src.Dispose()
                                End If
                                If needMask AndAlso ls Is Nothing Then
                                    warped = ScreamStudio.BodyWarpMask(ScreamStudio.WarpMask(maskCopy, w, h, faces, scream, shape), w, h, faces, body)
                                    Dim px = CompositeBaker.ReadPixels(If(fi, faceImg))
                                    CutoutEdit.Decontaminate(px, warped, w, h, opts, model)
                                    ls = CompositeBaker.ToBitmap(px, w, h)
                                    faceImg?.Dispose()
                                End If
                                Dim layer = ScreamStudio.BuildLayer(If(ls, layerSrc), warped, layerFaces, color, feather, flip)
                                layerSrc?.Dispose()
                                Dim cropped = ScreamStudio.CroppedAtBottom(warped, w, h)
                                Return (FaceImg:=fi, Warped:=warped, LayerSrc:=ls, Layer:=layer, Cropped:=cropped, W:=w, H:=h, Faces:=layerFaces, NoFigure:=noFigure, Spot:=spot)
                            End Function).ContinueWith(Sub(t)
                                                           If IsDisposed OrElse Not IsHandleCreated Then Return
                                                           Invoke(Sub() RebuildDone(t, needFace, needMask))
                                                       End Sub)
        Return _running
    End Function

    Private Sub RebuildDone(t As Task(Of (FaceImg As Bitmap, Warped As Byte(), LayerSrc As Bitmap, Layer As Bitmap, Cropped As Boolean, W As Integer, H As Integer, Faces As IReadOnlyList(Of FaceRegion), NoFigure As Boolean, Spot As ScreamPlacement?)), needFace As Boolean, needMask As Boolean)
        _running = Nothing
        If t.Status <> TaskStatus.RanToCompletion Then
            _status.Text = Lang.T("處理失敗：") & t.Exception?.GetBaseException().Message
        Else
            Dim r = t.Result
            _layerW = r.W : _layerH = r.H : _layerFaces = r.Faces
            If needFace Then _figureSpot = r.Spot
            If needFace Then
                _faceImg?.Dispose()
                _faceImg = r.FaceImg
            End If
            If needMask Then
                _warped = r.Warped
                _layerSrc?.Dispose()
                _layerSrc = r.LayerSrc
            End If
            _layer?.Dispose()
            _layer = r.Layer
            _croppedAtBottom = r.Cropped
            If _layer Is Nothing Then
                _status.Text = Lang.T("去背後沒有找到人，換一個去背模型試試。")
                _ok.Enabled = False
            Else
                If Not _placed Then
                    _placed = True
                    NextSuggestion()
                End If
                _ok.Enabled = True
                If r.NoFigure Then
                    _status.Text = Lang.T("這張照片抓不到臉部特徵點，只能用照片的身體。")
                ElseIf needFace Then
                    _status.Text = If(_faces.Count = 0, Lang.T("沒有偵測到臉：只換背景，臉不變。"),
                                      If(_faces.Any(Function(f) f.Dense IsNot Nothing), Lang.T("完成。拖曳人物移動、滾輪縮放；右邊可以調色彩。"),
                                         Lang.T("沒有臉部特徵點模型：臉不會變形，只換背景與色彩。")))
                End If
            End If
            _canvas.Invalidate()
        End If
        If _queued Then
            Dim f = _queuedFace
            _queued = False
            _queuedFace = False
            RebuildAsync(f)
        End If
    End Sub

    '=====================================================================
    ' 建議
    '=====================================================================

    ''' <summary>換到下一個建議的位置（中間、靠左、靠右輪流）。</summary>
    Friend Sub NextSuggestion()
        If _layer Is Nothing OrElse _backdrop Is Nothing Then Return
        Dim list = ScreamStudio.SuggestPlacements(_layer.Width, _layer.Height, _croppedAtBottom, _backdrop.Width, _backdrop.Height).ToList()
        If _figureSpot.HasValue AndAlso _backdropBox.SelectedIndex = 0 Then list.Insert(0, _figureSpot.Value)
        _suggestIndex = (_suggestIndex + 1) Mod list.Count
        Dim p = list(_suggestIndex)
        _x = p.X : _y = p.Y : _size = p.Size
        _syncing = True
        SetSlider("size", CInt(Math.Round(_size * 100)))
        _syncing = False
        _status.Text = Lang.T("建議位置：") & p.Name
        _canvas.Invalidate()
    End Sub

    ''' <summary>依背景建議人物的色彩（測試也用這個）。</summary>
    Friend Sub AutoColor()
        If _faceImg Is Nothing OrElse _warped Is Nothing OrElse _backdrop Is Nothing Then Return
        Dim c = ScreamStudio.SuggestColor(_faceImg, _warped, _layerFaces, _backdrop, _faceOnly.Checked)
        SetColor(c)
        _status.Text = Lang.F("建議配色：色相 {0}°、飽和度 {1}、亮度 {2}、對比 {3}、色溫 {4}。", c.Hue, c.Saturation, c.Brightness, c.Contrast, c.Temperature)
    End Sub

    '=====================================================================
    ' 預覽
    '=====================================================================

    Private Function LayerViewRect() As RectangleF
        Dim br = BackdropRect()
        Dim r = ScreamStudio.LayerRect(_layer, New Size(CInt(br.Width), CInt(br.Height)), _x, _y, _size)
        r.Offset(br.X, br.Y)
        Return r
    End Function

    ''' <summary>檢查遮罩、用筆刷時：原照片（人物圖）在預覽上的位置。</summary>
    Private Function FigureRect() As RectangleF
        Dim k = Math.Min((CanvasW - 24) / CDbl(_src.Width), (CanvasH - 24) / CDbl(_src.Height))
        Dim w = _src.Width * k, h = _src.Height * k
        Return New RectangleF(CSng((CanvasW - w) / 2), CSng((CanvasH - h) / 2), CSng(w), CSng(h))
    End Function

    Private Function CanvasToFigure(p As Point) As PointF
        Dim r = FigureRect()
        Return New PointF((p.X - r.X) * _src.Width / r.Width, (p.Y - r.Y) * _src.Height / r.Height)
    End Function

    Private ReadOnly Property FigureScale As Single
        Get
            Return FigureRect().Width / _src.Width
        End Get
    End Property

    Private Sub PaintCanvas(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear
        If _cut.WantsMaskView Then
            _cut.DrawView(g, FigureRect(), _hover)
            Return
        End If
        Dim br = BackdropRect()
        If _backdropView IsNot Nothing Then g.DrawImage(_backdropView, br)
        If _layer IsNot Nothing Then g.DrawImage(_layer, LayerViewRect())
        g.SetClip(br, Drawing2D.CombineMode.Exclude)
        Using outside As New SolidBrush(Color.FromArgb(40, 40, 44))
            g.FillRectangle(outside, _canvas.ClientRectangle) ' 超出背景的部分不會出現在結果裡
        End Using
    End Sub

    Private Sub CanvasDown(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left Then Return
        If _cut.WantsMaskView Then
            _painting = _cut.BeginStroke(CanvasToFigure(e.Location), FigureScale)
            Return
        End If
        If _layer Is Nothing Then Return
        _dragFrom = e.Location
        _dragStart = New PointF(CSng(_x), CSng(_y))
    End Sub

    Private Sub CanvasMove(sender As Object, e As MouseEventArgs)
        If _cut.WantsMaskView Then
            _canvas.Cursor = If(_cut.BrushActive, Cursors.Cross, Cursors.Default)
            If _cut.BrushActive Then
                _hover = e.Location
                If _painting Then _cut.ContinueStroke(CanvasToFigure(e.Location), FigureScale) Else _canvas.Invalidate()
            End If
            Return
        End If
        If _layer IsNot Nothing Then _canvas.Cursor = If(LayerViewRect().Contains(e.Location) OrElse _dragFrom.HasValue, Cursors.SizeAll, Cursors.Default)
        If Not _dragFrom.HasValue Then Return
        Dim br = BackdropRect()
        _x = _dragStart.X + (e.X - _dragFrom.Value.X) / br.Width
        _y = _dragStart.Y + (e.Y - _dragFrom.Value.Y) / br.Height
        _canvas.Invalidate()
    End Sub

    Private Sub CanvasUp(sender As Object, e As MouseEventArgs)
        _dragFrom = Nothing
        If _painting Then
            _painting = False
            _cut.EndStroke()
        End If
    End Sub

    Private Sub CanvasWheel(sender As Object, e As MouseEventArgs)
        If _layer Is Nothing OrElse _cut.WantsMaskView Then Return
        _size = Math.Max(0.1, Math.Min(1.5, _size * If(e.Delta > 0, 1.06, 1 / 1.06)))
        _syncing = True
        SetSlider("size", CInt(Math.Round(_size * 100)))
        _syncing = False
        _canvas.Invalidate()
    End Sub

    '=====================================================================
    ' 套用
    '=====================================================================

    ''' <summary>做出結果（背景放大成畫布大小、人物圖層與位置）並關閉。測試也用這個。</summary>
    Friend Sub ApplyResult()
        If _layer Is Nothing OrElse _backdrop Is Nothing Then Return
        Dim canvas = ScreamStudio.CanvasSize(_backdrop)
        Dim bg As New Bitmap(canvas.Width, canvas.Height, Imaging.PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bg)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
            g.DrawImage(_backdrop, 0, 0, canvas.Width, canvas.Height)
        End Using
        ResultBackdrop = bg
        ResultLayer = DirectCast(_layer.Clone(), Bitmap)
        ResultX = _x : ResultY = _y : ResultSize = _size
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ' 測試用
    Friend ReadOnly Property Layer As Bitmap
        Get
            Return _layer
        End Get
    End Property
    Friend ReadOnly Property Faces As List(Of FaceRegion)
        Get
            Return _faces
        End Get
    End Property
    Friend ReadOnly Property PersonColor As ScreamPersonColor
        Get
            Return _color
        End Get
    End Property
    Friend ReadOnly Property Placement As (X As Double, Y As Double, Size As Double)
        Get
            Return (_x, _y, _size)
        End Get
    End Property
    ''' <summary>身體：0 照片、1 原作（測試用）。</summary>
    Friend Sub SetBodyMode(i As Integer)
        _bodyBox.SelectedIndex = i
    End Sub
    Friend Sub SetValue(key As String, v As Integer)
        SetSlider(key, v)
    End Sub
    Friend ReadOnly Property Cutout As CutoutPanel
        Get
            Return _cut
        End Get
    End Property
    Friend Function SnapshotCanvas() As Bitmap
        Dim b As New Bitmap(_canvas.Width, _canvas.Height)
        _canvas.DrawToBitmap(b, New Rectangle(0, 0, b.Width, b.Height))
        Return b
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _rebuildTimer.Dispose()
            _src.Dispose()
            _backdrop?.Dispose()
            _backdropView?.Dispose()
            _layer?.Dispose()
            _faceImg?.Dispose()
            _layerSrc?.Dispose()
            _font.Dispose()
            _bold.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    Private NotInheritable Class ScreamCanvas
        Inherits Control
        Public Sub New()
            SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        End Sub
    End Class
End Class
