Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Threading.Tasks
Imports PhotoEdit

''' <summary>
''' 影像 → 人物合成…：一步一步把人物放進目前的照片。左邊是預覽，右邊是這一步的說明與控制項。
''' 1 選人物 → 2 去背 → 3 位置與大小 → 4 方向與透視 → 5 前後遮擋 → 6 光線與色彩 → 7 陰影 → 8 完成。
''' 按「套用」後由主畫面加成合成圖層（人物）與合成陰影圖層，原照片不變。
''' 背景可以在第一步換：目前的影像、開啟另一張、貼上，或不用背景——不用背景時只做去背，
''' 完成時存成透明 PNG、複製到剪貼簿，或開成新影像（第 3～7 步跳過）。
''' 背景是主畫面目前的結果（縮小過的預覽）；位置都用這張背景的像素算，套用時換算成照片的 0..1 座標，
''' 變形、遮擋、陰影都做進圖層 PNG（解析度依照片原尺寸與人物原圖決定）。
''' </summary>
Friend Class frmCompositeGuide
    Inherits Aqua.AquaForm

    Private Enum GuideStep
        Pick = 0
        Cutout = 1
        Place = 2
        Perspective = 3
        Occlusion = 4
        Color = 5
        Shadow = 6
        Finish = 7
    End Enum

    Private Const StepCount As Integer = 8
    Private Const CanvasW As Integer = 860
    Private Const CanvasH As Integer = 660
    Private Const PanelW As Integer = 340

    Private ReadOnly _help As HelpTip
    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _boldFont As New Font(Lang.UiFont, 12.0F, FontStyle.Bold)
    Private _outputSize As Size

    ' 背景（預覽尺寸）；沒有背景時 _bg 是 Nothing
    Private _bg As Bitmap
    Private _bgPx As Byte()
    Private _bw, _bh As Integer
    ' 主畫面目前的影像（可以換回來）；換成別張時記下檔案或貼上的原圖
    Private _currentBg As Bitmap
    Private ReadOnly _currentSize As Size
    Private _bgFile As String
    Private _bgPasted As Bitmap
    Private _scene As SceneAnalysis

    ' 人物（工作尺寸，長邊最多 CompositeBaker.MaxSide）
    Private _figure As Bitmap
    Private _figPx As Byte()
    Private _fw, _fh As Integer
    Private _sourceFile As String
    ''' <summary>最後的遮罩（去背面板算好的，跟著 MaskChanged 更新）。</summary>
    Private _mask As Byte()
    Private _sourceHadAlpha As Boolean
    Private _fig As New CompositeFigure()

    ' 位置：錨點（顯示方向）放在背景 (_tx, _ty)，縮放 _scale（背景像素 / 人物像素）
    Private _tx, _ty, _scale As Double
    Private _placed As Boolean
    Private _candidates As New List(Of PlacementCandidate)()
    Private _figAnalysis As FigureAnalysis

    ' 方向與透視：參考線（背景像素）
    Private ReadOnly _guides As New List(Of (A As PointF, B As PointF))()
    ' 遮擋（背景尺寸，在人物前面的東西）
    Private _occ As Byte()
    Private _shadowInit As Boolean
    ' 地平線（背景像素；沒有用時 -1）、邊緣透光用的模糊背景
    Private _horizon As Double = -1
    Private _bgBlur As Byte()

    ' 預覽
    Private _prepared As Byte()
    Private _layerPrev As Bitmap
    Private _layerRect As RectangleF
    Private _shadowPrev As Bitmap
    Private _shadowRect As RectangleF
    Private _bakedAt As PointF ' 預覽圖層是在哪個錨點位置做的（拖曳時只位移）
    Private _occluderView As Bitmap
    Private _occTint As Bitmap
    Private ReadOnly _preparedTimer As New Timer With {.Interval = 250}

    Private _step As GuideStep = GuideStep.Pick
    Private _syncing As Boolean

    ' 介面
    Private ReadOnly _canvas As New GuideCanvas()
    Private ReadOnly _stepLabels As New List(Of Label)()
    Private ReadOnly _title As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _desc As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _status As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(24, 95, 165)}
    Private ReadOnly _panels As New List(Of Panel)()
    Private ReadOnly _back As New Button With {.Text = Lang.T("上一步")}
    Private ReadOnly _next As New Button With {.Text = Lang.T("下一步")}
    Private ReadOnly _cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel}

    ' 1 選人物、背景
    Private ReadOnly _figInfo As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _bgInfo As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(70, 76, 88)}
    Private ReadOnly _bgOpen As New Button With {.Text = Lang.T("開啟背景…")}
    Private ReadOnly _bgPaste As New Button With {.Text = Lang.T("貼上背景")}
    Private ReadOnly _bgNone As New Button With {.Text = Lang.T("不用背景（透明）")}
    Private ReadOnly _bgCurrent As New Button With {.Text = Lang.T("目前的影像")}
    ' 8 完成（沒有背景時）
    Private ReadOnly _savePng As New Button With {.Text = Lang.T("存成透明 PNG…")}
    Private ReadOnly _copyPng As New Button With {.Text = Lang.T("複製到剪貼簿")}
    ' 2 去背（共用的去背面板）
    Private _cut As CutoutPanel
    ' 3 位置與大小
    Private ReadOnly _autoPlace As New Button With {.Text = Lang.T("自動定位")}
    Private ReadOnly _candList As New ListBox With {.IntegralHeight = False}
    Private ReadOnly _height As New Aqua.Slider()
    Private ReadOnly _heightValue As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight}
    Private ReadOnly _flip As New CheckBox With {.Text = Lang.T("左右翻轉"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _useHorizon As New CheckBox With {.Text = Lang.T("依地平線決定大小"), .AutoSize = True, .BackColor = Color.Transparent}
    ' 4 方向與透視
    Private ReadOnly _rotation As New Aqua.Slider()
    Private ReadOnly _rotationValue As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight}
    Private ReadOnly _keystone As New Aqua.Slider()
    Private ReadOnly _keystoneValue As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight}
    Private ReadOnly _guideMode As New CheckBox With {.Text = Lang.T("畫參考線"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter}
    Private ReadOnly _fromGuides As New Button With {.Text = Lang.T("依參考線推估")}
    Private ReadOnly _clearGuides As New Button With {.Text = Lang.T("清除參考線")}
    ' 5 前後遮擋
    Private ReadOnly _occPaint As New CheckBox With {.Text = Lang.T("前景筆刷"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter}
    Private ReadOnly _occErase As New CheckBox With {.Text = Lang.T("擦除"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter}
    Private ReadOnly _occWand As New CheckBox With {.Text = Lang.T("魔術棒"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter}
    Private ReadOnly _occSize As New Aqua.Slider()
    Private ReadOnly _occTol As New Aqua.Slider()
    Private ReadOnly _occClear As New Button With {.Text = Lang.T("清除遮擋")}
    ' 6 光線與色彩
    Private ReadOnly _keepColor As New RadioButton With {.Text = Lang.T("保持人物原本的顏色"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _matchColor As New RadioButton With {.Text = Lang.T("依背景自動融合"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _reanalyze As New Button With {.Text = Lang.T("重新分析")}
    Private ReadOnly _colorSliders As New Dictionary(Of String, (Slider As Aqua.Slider, Value As Label))()
    ' 7 陰影
    Private ReadOnly _shadowOn As New CheckBox With {.Text = Lang.T("加上陰影"), .AutoSize = True, .Checked = True, .BackColor = Color.Transparent}
    Private ReadOnly _shadowAuto As New Button With {.Text = Lang.T("自動")}
    Private ReadOnly _avoidSky As New CheckBox With {.Text = Lang.T("陰影不落在天空上"), .AutoSize = True, .Checked = True, .BackColor = Color.Transparent}
    Private ReadOnly _shadowSliders As New Dictionary(Of String, (Slider As Aqua.Slider, Value As Label))()
    ' 8 完成
    Private ReadOnly _summary As New Label With {.AutoSize = False, .BackColor = Color.Transparent}

    ''' <summary>套用後的結果（DialogResult.OK 時有值）。</summary>
    Friend Property Result As GuideResult

    Friend Class GuideResult
        Public Figure As CompositeFigure
        ''' <summary>做好的人物圖層（已變形、遮擋，對齊照片座標軸）。</summary>
        Public Baked As Bitmap
        ''' <summary>陰影圖層；沒有陰影時 Nothing。</summary>
        Public ShadowImage As Bitmap
        ''' <summary>工作用人物圖（原圖縮到長邊 MaxSide）、遮罩、AI 遮罩（原圖本來透明時 Nothing）。</summary>
        Public FigureImage As Bitmap
        Public Mask As Byte()
        Public AiMask As Byte()
        Public FigureWidth, FigureHeight As Integer
        ''' <summary>遮擋遮罩（背景預覽尺寸）；沒有遮擋時 Nothing。</summary>
        Public Occlusion As Byte()
        Public BackgroundWidth, BackgroundHeight As Integer
        Public SourceFile As String
        ''' <summary>圖片物件的中心（照片 0..1）與高度（照片高度的比例）。</summary>
        Public X, Y, Size As Double
        Public ShadowX, ShadowY, ShadowSize As Double
        ''' <summary>背景是在引導視窗裡換的另一張：檔案（或貼上的原圖）；用目前的影像時兩個都是 Nothing。</summary>
        Public BackgroundFile As String
        Public BackgroundImage As Bitmap
        ''' <summary>沒有背景：只有去背的人物（透明 PNG），其他欄位沒有用。</summary>
        Public NoBackground As Boolean
        Public Cutout As Bitmap
    End Class

    ''' <param name="background">主畫面目前的結果（預覽尺寸），由這裡複製一份；沒有開啟影像時 Nothing（先不用背景，可以在第一步選）。</param>
    ''' <param name="outputSize">照片輸出的原尺寸（圖層解析度依這個決定）。</param>
    Public Sub New(background As Bitmap, help As HelpTip, outputSize As Size)
        _help = help
        _currentSize = outputSize
        If background IsNot Nothing Then _currentBg = CompositeBaker.FitWorking(DirectCast(background.Clone(), Bitmap))
        Text = Lang.T("人物合成")
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        RoundCorners = False
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        KeyPreview = True
        Dim top = 23 + 8
        ClientSize = New Size(16 + CanvasW + 16 + PanelW + 16, top + 40 + CanvasH + 60)

        ' 步驟列
        Dim names = {Lang.T("選人物"), Lang.T("去背"), Lang.T("位置與大小"), Lang.T("方向與透視"), Lang.T("前後遮擋"), Lang.T("光線與色彩"), Lang.T("陰影"), Lang.T("完成")}
        Dim cell = (ClientSize.Width - 32) \ StepCount
        For i = 0 To names.Length - 1
            Dim index = i
            Dim l As New Label With {.Text = $"{i + 1}  {names(i)}", .AutoSize = False, .TextAlign = ContentAlignment.MiddleCenter, .Cursor = Cursors.Hand,
                                     .BackColor = Color.FromArgb(236, 238, 242), .ForeColor = Color.FromArgb(90, 96, 108)}
            l.SetBounds(16 + i * cell, top, cell - 6, 30)
            AddHandler l.Click, Sub() TryGoTo(CType(index, GuideStep))
            _stepLabels.Add(l)
            Controls.Add(l)
        Next
        _canvas.SetBounds(16, top + 40, CanvasW, CanvasH)
        _canvas.BackColor = Color.FromArgb(40, 40, 44)
        AddHandler _canvas.Paint, AddressOf PaintCanvas
        AddHandler _canvas.MouseDown, AddressOf CanvasMouseDown
        AddHandler _canvas.MouseMove, AddressOf CanvasMouseMove
        AddHandler _canvas.MouseUp, AddressOf CanvasMouseUp
        AddHandler _canvas.MouseWheel, AddressOf CanvasMouseWheel
        Controls.Add(_canvas)

        Dim px = 16 + CanvasW + 16, py = top + 40
        _title.Font = _boldFont
        _title.SetBounds(px, py, PanelW, 28)
        _desc.SetBounds(px, py + 32, PanelW, 92)
        _desc.ForeColor = Color.FromArgb(70, 76, 88)
        Controls.AddRange({_title, _desc})
        For i = 0 To StepCount - 1
            Dim p As New Panel With {.BackColor = Color.Transparent, .Visible = False}
            p.SetBounds(px, py + 130, PanelW, CanvasH - 130 - 50)
            _panels.Add(p)
            Controls.Add(p)
        Next
        _status.SetBounds(px, py + CanvasH - 46, PanelW, 46)
        Controls.Add(_status)
        BuildPickPanel(_panels(GuideStep.Pick))
        BuildCutoutPanel(_panels(GuideStep.Cutout))
        BuildPlacePanel(_panels(GuideStep.Place))
        BuildPerspectivePanel(_panels(GuideStep.Perspective))
        BuildOcclusionPanel(_panels(GuideStep.Occlusion))
        BuildColorPanel(_panels(GuideStep.Color))
        BuildShadowPanel(_panels(GuideStep.Shadow))
        BuildFinishPanel(_panels(GuideStep.Finish))

        Dim by = top + 40 + CanvasH + 14
        _cancel.SetBounds(ClientSize.Width - 16 - 96, by, 96, 32)
        _next.SetBounds(ClientSize.Width - 16 - 96 - 8 - 120, by, 120, 32)
        _back.SetBounds(ClientSize.Width - 16 - 96 - 8 - 120 - 8 - 110, by, 110, 32)
        _next.Font = New Font(_font, FontStyle.Bold)
        AddHandler _back.Click, Sub()
                                    ' 沒有背景時第 3～7 步跳過：往前找第一個能進的
                                    Dim s = _step - 1
                                    While s > GuideStep.Pick AndAlso Not CanEnter(CType(s, GuideStep))
                                        s -= 1
                                    End While
                                    TryGoTo(CType(s, GuideStep))
                                End Sub
        AddHandler _next.Click, Sub() NextClicked()
        Controls.AddRange({_back, _next, _cancel})
        CancelButton = _cancel
        AddHandler _preparedTimer.Tick, Sub()
                                            _preparedTimer.Stop()
                                            RebuildPrepared()
                                        End Sub
        ThemeManager.Attach(Me)
        If _currentBg IsNot Nothing Then UseBackground(DirectCast(_currentBg.Clone(), Bitmap), _currentSize, Nothing, Nothing) Else UseBackground(Nothing, Size.Empty, Nothing, Nothing)
        ShowStep(GuideStep.Pick)
    End Sub

    '=====================================================================
    ' 各步驟的控制項
    '=====================================================================

    Private Shared Function NewButton(b As ButtonBase, x As Integer, y As Integer, w As Integer) As ButtonBase
        b.SetBounds(x, y, w, 30)
        If TypeOf b Is Button Then DirectCast(b, Button).UseVisualStyleBackColor = True
        Return b
    End Function

    Private Function AddSlider(p As Panel, y As Integer, caption As String, s As Aqua.Slider, valueLabel As Label, min As Integer, max As Integer, value As Integer, helpKey As String) As Integer
        Dim l As New Label With {.Text = caption, .AutoSize = False, .BackColor = Color.Transparent}
        l.SetBounds(0, y + 3, 90, 22)
        s.Minimum = min : s.Maximum = max : s.Value = value : s.ShowTicks = False
        s.SetBounds(92, y, PanelW - 92 - 52, 24)
        valueLabel.SetBounds(PanelW - 50, y + 3, 50, 22)
        p.Controls.AddRange({l, s, valueLabel})
        _help.SetHelpLinked(helpKey, s, l, s)
        Return y + 34
    End Function

    ''' <summary>一組互斥的工具按鈕（可以都不選）。</summary>
    Private Shared Sub ExclusiveTools(ParamArray boxes As CheckBox())
        For Each b In boxes
            Dim me1 = b
            AddHandler b.CheckedChanged, Sub()
                                             If Not me1.Checked Then Return
                                             For Each other In boxes
                                                 If other IsNot me1 Then other.Checked = False
                                             Next
                                         End Sub
        Next
    End Sub

    Private Sub BuildPickPanel(p As Panel)
        Dim open = NewButton(New Button With {.Text = Lang.T("開啟人物圖片…")}, 0, 0, 160)
        Dim paste = NewButton(New Button With {.Text = Lang.T("貼上圖片")}, 168, 0, 120)
        AddHandler open.Click, Sub() OpenFigureWithDialog()
        AddHandler paste.Click, Sub() PasteFigure()
        _help.SetHelp("comp.open", open)
        _help.SetHelp("comp.paste", paste)
        _figInfo.SetBounds(0, 44, PanelW, 110)
        p.Controls.AddRange({open, paste, _figInfo})
        ' 背景
        Dim head As New Label With {.Text = Lang.T("背景"), .AutoSize = False, .Font = New Font(_font, FontStyle.Bold), .BackColor = Color.Transparent}
        head.SetBounds(0, 166, PanelW, 22)
        _bgInfo.SetBounds(0, 190, PanelW, 64)
        NewButton(_bgOpen, 0, 258, 160)
        NewButton(_bgPaste, 168, 258, 120)
        NewButton(_bgNone, 0, 296, 160)
        NewButton(_bgCurrent, 168, 296, 120)
        AddHandler _bgOpen.Click, Sub() OpenBackgroundWithDialog()
        AddHandler _bgPaste.Click, Sub() PasteBackground()
        AddHandler _bgNone.Click, Sub() UseBackground(Nothing, Size.Empty, Nothing, Nothing)
        AddHandler _bgCurrent.Click, Sub()
                                         If _currentBg IsNot Nothing Then UseBackground(DirectCast(_currentBg.Clone(), Bitmap), _currentSize, Nothing, Nothing)
                                     End Sub
        _help.SetHelp("comp.bgopen", _bgOpen)
        _help.SetHelp("comp.bgpaste", _bgPaste)
        _help.SetHelp("comp.bgnone", _bgNone)
        _help.SetHelp("comp.bgcurrent", _bgCurrent)
        p.Controls.AddRange({head, _bgInfo, _bgOpen, _bgPaste, _bgNone, _bgCurrent})
    End Sub

    Private Sub BuildCutoutPanel(p As Panel)
        _cut = New CutoutPanel(_help, PanelW, Lang.T("自動去背"), portrait:=False) With {.Location = New Point(0, 0)}
        AddHandler _cut.MaskChanged, Sub() CutMaskChanged()
        AddHandler _cut.OptionsChanged, Sub()
                                            SyncCutOptions()
                                            InvalidatePrepared()
                                        End Sub
        AddHandler _cut.ViewChanged, Sub()
                                         If _step = GuideStep.Cutout Then
                                             _canvas.Cursor = If(_cut.BrushActive, Cursors.Cross, Cursors.Default)
                                             _canvas.Invalidate()
                                         End If
                                     End Sub
        AddHandler _cut.StatusChanged, Sub(sender, text) _status.Text = text
        p.Controls.Add(_cut)
    End Sub

    ''' <summary>去背面板的遮罩變了：記下來，人物分析與預覽圖層重做。</summary>
    Private Sub CutMaskChanged()
        _mask = _cut.Mask
        _figAnalysis = Nothing
        SyncCutOptions()
        InvalidatePrepared()
        UpdateStepLabels()
    End Sub

    ''' <summary>做圖層要用的去背設定（去除殘色的半徑依精修與模型）記到 _fig。</summary>
    Private Sub SyncCutOptions()
        If Not _cut.AiMaskIsSource Then _fig.Model = _cut.MaskModel
        _fig.EdgeRefine = _cut.Options.EdgeRefine
        _fig.Decontaminate = _cut.Options.Decontaminate
    End Sub

    Private Sub BuildPlacePanel(p As Panel)
        NewButton(_autoPlace, 0, 0, PanelW)
        _autoPlace.Font = New Font(_font, FontStyle.Bold)
        AddHandler _autoPlace.Click, Sub() AutoPlace()
        _help.SetHelp("comp.autoplace", _autoPlace)
        Dim l As New Label With {.Text = Lang.T("推薦的位置（點一下套用）"), .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(0, 40)}
        _candList.SetBounds(0, 62, PanelW, 170)
        _candList.HorizontalScrollbar = True
        AddHandler _candList.SelectedIndexChanged, Sub()
                                                       If _syncing OrElse _candList.SelectedIndex < 0 OrElse _candList.SelectedIndex >= _candidates.Count Then Return
                                                       ApplyCandidate(_candidates(_candList.SelectedIndex))
                                                   End Sub
        _help.SetHelp("comp.candidates", _candList)
        p.Controls.AddRange({l, _candList})
        Dim y = AddSlider(p, 244, Lang.T("人物高度"), _height, _heightValue, 3, 100, 40, "comp.height")
        AddHandler _height.ValueChanged, Sub()
                                             _heightValue.Text = _height.Value & "%"
                                             If _syncing OrElse _mask Is Nothing Then Return
                                             _scale = _height.Value / 100.0 * _bh / Math.Max(1, DisplayAnalysis().Height)
                                             RebuildLayers()
                                         End Sub
        _flip.Location = New Point(0, y)
        AddHandler _flip.CheckedChanged, Sub()
                                             If _syncing Then Return
                                             _fig.FlipHorizontal = _flip.Checked
                                             InvalidatePrepared(immediate:=True)
                                         End Sub
        _help.SetHelp("comp.flip", _flip)
        _useHorizon.Location = New Point(0, y + 28)
        AddHandler _useHorizon.CheckedChanged, Sub()
                                                   If _syncing Then Return
                                                   If _useHorizon.Checked Then
                                                       If DisplayAnalysis().BottomCropped Then
                                                           _status.Text = Lang.T("半身像看不到腳，不適用地平線輔助。")
                                                           _syncing = True : _useHorizon.Checked = False : _syncing = False
                                                           Return
                                                       End If
                                                       ' 從目前的眼睛高度開始，打開時人物不會跳
                                                       _horizon = _ty - CompositePlacement.EyeFraction * DisplayAnalysis().Height * _scale
                                                       _status.Text = Lang.T("拖曳青色的地平線對齊照片的地平線；之後把人物往前、往後拖，大小會跟著遠近變。")
                                                   Else
                                                       _horizon = -1
                                                   End If
                                                   _canvas.Invalidate()
                                               End Sub
        _help.SetHelp("comp.horizon", _useHorizon)
        Dim tip As New Label With {.Text = Lang.T("在預覽上拖曳人物可以移動，滾輪放大縮小。"), .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
        tip.SetBounds(0, y + 60, PanelW, 44)
        p.Controls.AddRange({_flip, _useHorizon, tip})
    End Sub

    Private Sub BuildPerspectivePanel(p As Panel)
        Dim y = AddSlider(p, 0, Lang.T("旋轉"), _rotation, _rotationValue, -30, 30, 0, "comp.rotation")
        AddHandler _rotation.ValueChanged, Sub()
                                               _rotationValue.Text = _rotation.Value & "°"
                                               If _syncing Then Return
                                               _fig.Rotation = _rotation.Value
                                               RebuildLayers()
                                           End Sub
        _rotationValue.Text = "0°"
        y = AddSlider(p, y, Lang.T("仰角透視"), _keystone, _keystoneValue, 60, 140, 100, "comp.keystone")
        AddHandler _keystone.ValueChanged, Sub()
                                               _keystoneValue.Text = _keystone.Value & "%"
                                               If _syncing Then Return
                                               _fig.Keystone = _keystone.Value / 100.0
                                               RebuildLayers()
                                           End Sub
        _keystoneValue.Text = "100%"
        NewButton(_guideMode, 0, y + 8, 110)
        NewButton(_fromGuides, 118, y + 8, 120)
        NewButton(_clearGuides, 0, y + 46, 110)
        AddHandler _guideMode.CheckedChanged, Sub() _canvas.Cursor = If(_guideMode.Checked, Cursors.Cross, Cursors.Default)
        AddHandler _fromGuides.Click, Sub() ApplyGuides()
        AddHandler _clearGuides.Click, Sub()
                                           _guides.Clear()
                                           _canvas.Invalidate()
                                       End Sub
        _help.SetHelp("comp.guidemode", _guideMode)
        _help.SetHelp("comp.fromguides", _fromGuides)
        _help.SetHelp("comp.clearguides", _clearGuides)
        Dim tip As New Label With {.Text = Lang.T("仰拍建築、柱子時，畫面上的直線會往上收。按「畫參考線」沿著兩條本來應該平行的直線（柱子、門框的左右邊）各拖一條，再按「依參考線推估」，人物就會跟著傾斜、上窄下寬。"),
                                 .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
        tip.SetBounds(0, y + 90, PanelW, 110)
        p.Controls.AddRange({_guideMode, _fromGuides, _clearGuides, tip})
    End Sub

    Private Sub BuildOcclusionPanel(p As Panel)
        NewButton(_occPaint, 0, 0, 104)
        NewButton(_occErase, 110, 0, 104)
        NewButton(_occWand, 220, 0, 104)
        ExclusiveTools(_occPaint, _occErase, _occWand)
        For Each b In {_occPaint, _occErase, _occWand}
            AddHandler b.CheckedChanged, Sub() _canvas.Cursor = If(_occPaint.Checked OrElse _occErase.Checked OrElse _occWand.Checked, Cursors.Cross, Cursors.Default)
        Next
        _help.SetHelp("comp.occpaint", _occPaint)
        _help.SetHelp("comp.occerase", _occErase)
        _help.SetHelp("comp.occwand", _occWand)
        p.Controls.AddRange({_occPaint, _occErase, _occWand})
        Dim sizeValue As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight, .Text = "30"}
        Dim y = AddSlider(p, 42, Lang.T("筆刷大小"), _occSize, sizeValue, 4, 160, 30, "comp.occsize")
        AddHandler _occSize.ValueChanged, Sub() sizeValue.Text = _occSize.Value.ToString()
        Dim tolValue As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight, .Text = "28"}
        y = AddSlider(p, y, Lang.T("魔術棒範圍"), _occTol, tolValue, 4, 80, 28, "comp.occtol")
        AddHandler _occTol.ValueChanged, Sub() tolValue.Text = _occTol.Value.ToString()
        NewButton(_occClear, 0, y, 120)
        AddHandler _occClear.Click, Sub()
                                        _occ = Nothing
                                        RebuildOccluderViews()
                                    End Sub
        _help.SetHelp("comp.occclear", _occClear)
        Dim tip As New Label With {.Text = Lang.T("把背景裡應該在人物前面的東西（桌子、欄杆、樹幹、草）塗起來，人物被它擋住的部分就會藏到後面。紅色是塗到的範圍。沒有東西在人物前面時可以直接下一步。"),
                                 .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
        tip.SetBounds(0, y + 42, PanelW, 110)
        p.Controls.AddRange({_occClear, tip})
    End Sub

    Private Sub BuildColorPanel(p As Panel)
        _keepColor.Location = New Point(0, 0)
        _matchColor.Location = New Point(0, 26)
        _matchColor.Checked = True
        AddHandler _matchColor.CheckedChanged, Sub()
                                                   If _syncing Then Return
                                                   _fig.Color.Mode = If(_matchColor.Checked, CompositeColorMode.Environment, CompositeColorMode.Original)
                                                   SyncColorSliders()
                                                   InvalidatePrepared()
                                               End Sub
        _help.SetHelp("comp.keepcolor", _keepColor)
        _help.SetHelp("comp.matchcolor", _matchColor)
        NewButton(_reanalyze, 0, 56, 140)
        AddHandler _reanalyze.Click, Sub() AutoColor()
        _help.SetHelp("comp.reanalyze", _reanalyze)
        p.Controls.AddRange({_keepColor, _matchColor, _reanalyze})
        Dim y = 100
        Dim defs = {("strength", Lang.T("強度"), 0, 100), ("bright", Lang.T("亮度"), -30, 30), ("contrast", Lang.T("對比"), -30, 30),
                    ("sat", Lang.T("飽和度"), -50, 50), ("temp", Lang.T("色溫"), -60, 60),
                    ("wrap", Lang.T("邊緣透光"), 0, 100), ("grain", Lang.T("顆粒"), 0, 100)}
        For Each d In defs
            Dim key = d.Item1
            Dim s As New Aqua.Slider()
            Dim v As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight}
            y = AddSlider(p, y, d.Item2, s, v, d.Item3, d.Item4, 0, "comp.color." & key)
            _colorSliders(key) = (s, v)
            AddHandler s.ValueChanged, Sub()
                                           v.Text = s.Value.ToString()
                                           If _syncing Then Return
                                           ReadColorSlider(key, s.Value)
                                           If key = "wrap" OrElse key = "grain" Then RebuildLayers() Else InvalidatePrepared()
                                       End Sub
        Next
        Dim note As New Label With {.Text = Lang.T("自動融合只會小幅調整（有上限），保留人物原本的線條與配色。"), .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
        note.SetBounds(0, y + 4, PanelW, 44)
        p.Controls.Add(note)
    End Sub

    Private Sub BuildShadowPanel(p As Panel)
        _shadowOn.Location = New Point(0, 4)
        AddHandler _shadowOn.CheckedChanged, Sub()
                                                 If _syncing Then Return
                                                 _fig.Shadow.Enabled = _shadowOn.Checked
                                                 SyncShadowControls()
                                                 RebuildLayers()
                                             End Sub
        _help.SetHelp("comp.shadowon", _shadowOn)
        NewButton(_shadowAuto, 220, 0, 104)
        AddHandler _shadowAuto.Click, Sub()
                                          _fig.Shadow = CompositeShadow.Recommend(_scene, DisplayAnalysis())
                                          If DisplayAnalysis().BottomCropped Then _status.Text = Lang.T("半身像沒有踩在地上，不加陰影。")
                                          SyncShadowControls()
                                          RebuildLayers()
                                      End Sub
        _help.SetHelp("comp.shadowauto", _shadowAuto)
        p.Controls.AddRange({_shadowOn, _shadowAuto})
        Dim y = 42
        Dim defs = {("dir", Lang.T("方向"), -180, 180), ("len", Lang.T("長度"), 0, 150), ("opacity", Lang.T("濃淡"), 0, 70),
                    ("soft", Lang.T("柔和"), 0, 50), ("contact", Lang.T("接觸陰影"), 0, 100)}
        For Each d In defs
            Dim key = d.Item1
            Dim s As New Aqua.Slider()
            Dim v As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight}
            y = AddSlider(p, y, d.Item2, s, v, d.Item3, d.Item4, 0, "comp.shadow." & key)
            _shadowSliders(key) = (s, v)
            AddHandler s.ValueChanged, Sub()
                                           v.Text = s.Value.ToString() & If(key = "dir", "°", "")
                                           If _syncing Then Return
                                           Dim sh = _fig.Shadow
                                           Select Case key
                                               Case "dir" : sh.Direction = s.Value
                                               Case "len" : sh.Length = s.Value / 100.0
                                               Case "opacity" : sh.Opacity = s.Value
                                               Case "soft" : sh.Softness = s.Value
                                               Case "contact" : sh.Contact = s.Value
                                           End Select
                                           RebuildLayers()
                                       End Sub
        Next
        _avoidSky.Location = New Point(0, y)
        AddHandler _avoidSky.CheckedChanged, Sub()
                                                 If _syncing Then Return
                                                 _fig.Shadow.AvoidSky = _avoidSky.Checked
                                                 RebuildLayers()
                                             End Sub
        _help.SetHelp("comp.avoidsky", _avoidSky)
        Dim tip As New Label With {.Text = Lang.T("在預覽上點一下或拖曳，決定頭的影子落在哪裡（方向和長度會跟著改）。"), .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
        tip.SetBounds(0, y + 30, PanelW, 44)
        p.Controls.AddRange({_avoidSky, tip})
    End Sub

    Private Sub BuildFinishPanel(p As Panel)
        _summary.SetBounds(0, 0, PanelW, 360)
        NewButton(_savePng, 0, 120, 160)
        NewButton(_copyPng, 168, 120, 140)
        AddHandler _savePng.Click, Sub() SaveCutoutWithDialog()
        AddHandler _copyPng.Click, Sub() CopyCutout()
        _help.SetHelp("comp.savepng", _savePng)
        _help.SetHelp("comp.copypng", _copyPng)
        p.Controls.AddRange({_summary, _savePng, _copyPng})
    End Sub

    '=====================================================================
    ' 步驟切換
    '=====================================================================

    Private Function CanEnter(s As GuideStep) As Boolean
        If s <= GuideStep.Pick Then Return True
        If _figure Is Nothing Then Return False
        If s >= GuideStep.Place AndAlso _mask Is Nothing Then Return False
        ' 沒有背景：只有去背，位置、透視、遮擋、色彩、陰影都跳過
        If _bg Is Nothing AndAlso s >= GuideStep.Place AndAlso s < GuideStep.Finish Then Return False
        Return True
    End Function

    Private Sub TryGoTo(s As GuideStep)
        If s < GuideStep.Pick OrElse s > GuideStep.Finish OrElse _cut.Busy Then Return
        If Not CanEnter(s) Then
            _status.Text = If(_figure Is Nothing, Lang.T("請先選一張人物圖片。"), Lang.T("請先去背（或使用原圖的透明背景）。"))
            Return
        End If
        ShowStep(s)
    End Sub

    Private Sub NextClicked()
        If _step = GuideStep.Finish Then
            Finish()
            Return
        End If
        If _bg Is Nothing AndAlso _step = GuideStep.Cutout Then
            TryGoTo(GuideStep.Finish)
            Return
        End If
        TryGoTo(CType(_step + 1, GuideStep))
    End Sub

    Private Sub UpdateStepLabels()
        For i = 0 To _panels.Count - 1
            Dim current = i = CInt(_step)
            _stepLabels(i).BackColor = If(current, Color.FromArgb(210, 228, 250), Color.FromArgb(236, 238, 242))
            _stepLabels(i).ForeColor = If(current, Color.FromArgb(24, 60, 120), If(CanEnter(CType(i, GuideStep)), Color.FromArgb(70, 76, 88), Color.FromArgb(160, 165, 175)))
            _stepLabels(i).Font = If(current, New Font(_font, FontStyle.Bold), _font)
        Next
    End Sub

    Private Sub ShowStep(s As GuideStep)
        _step = s
        For i = 0 To _panels.Count - 1
            _panels(i).Visible = (i = CInt(s))
        Next
        UpdateStepLabels()
        _back.Enabled = s > GuideStep.Pick
        _next.Text = If(s = GuideStep.Finish, If(_bg Is Nothing, Lang.T("開成新影像"), Lang.T("套用")), Lang.T("下一步"))
        _savePng.Visible = _bg Is Nothing
        _copyPng.Visible = _bg Is Nothing
        _status.Text = ""
        _canvas.Cursor = Cursors.Default
        Select Case s
            Case GuideStep.Pick
                _title.Text = Lang.T("1. 選人物")
                _desc.Text = Lang.T("選一張要放進照片的人物圖（照片、插畫、動漫都可以）。已經是透明背景的 PNG 可以直接使用，不用再去背。下面可以換背景，或不用背景只去背。")
            Case GuideStep.Cutout
                _title.Text = Lang.T("2. 去背")
                _desc.Text = Lang.T("用 AI 把人物和原本的背景分開。按「自動去背」，再用筆刷修正漏掉或多出來的地方；「檢查遮罩」用紅色顯示被去掉的部分。")
                If _cut.AiMask Is Nothing AndAlso Not _cut.Busy Then
                    _status.Text = Lang.T("按「自動去背」開始。")
                ElseIf _sourceHadAlpha AndAlso _cut.AiMaskIsSource AndAlso _cut.StrokeCount = 0 Then
                    _status.Text = Lang.T("原圖已經是透明背景，可以直接下一步；也可以換成 AI 去背。")
                End If
                If _cut.BrushActive Then _canvas.Cursor = Cursors.Cross
            Case GuideStep.Place
                _title.Text = Lang.T("3. 位置與大小")
                _desc.Text = Lang.T("「自動定位」會分析背景（天空、柱頂、屋頂、地面）和人物（腳的位置、是不是半身像），推薦幾個位置。也可以直接拖曳、用滾輪調整大小。")
                EnsureScene()
                If Not _placed Then AutoPlace()
            Case GuideStep.Perspective
                _title.Text = Lang.T("4. 方向與透視")
                _desc.Text = Lang.T("背景是斜的、或是仰拍的建築時，讓人物跟著傾斜和上窄下寬，看起來才像站在同一個空間。一般的照片可以直接下一步。")
                SyncPerspectiveControls()
            Case GuideStep.Occlusion
                _title.Text = Lang.T("5. 前後遮擋")
                _desc.Text = Lang.T("背景裡有東西應該擋在人物前面時（例如欄杆、桌子、花草），把它塗起來，人物就會站到它後面。")
                RebuildOccluderViews()
            Case GuideStep.Color
                _title.Text = Lang.T("6. 光線與色彩")
                _desc.Text = Lang.T("讓人物的亮度、色溫、對比和背景接近，看起來像在同一個地方拍的。選「依背景自動融合」後可以再微調。")
                If _fig.Color.Mode = CompositeColorMode.Environment AndAlso _fig.Color.IsIdentity Then AutoColor()
                SyncColorSliders()
            Case GuideStep.Shadow
                _title.Text = Lang.T("7. 陰影")
                _desc.Text = Lang.T("人物腳下的接觸陰影和投在地上的影子，讓人物真的「站」在地面上。方向、長度、濃淡都可以調；半身像沒有踩在地上，不需要陰影。")
                If Not _shadowInit Then
                    _shadowInit = True
                    _fig.Shadow = CompositeShadow.Recommend(_scene, DisplayAnalysis())
                    RebuildLayers()
                End If
                If DisplayAnalysis().BottomCropped Then _status.Text = Lang.T("半身像沒有踩在地上，不加陰影。")
                SyncShadowControls()
            Case GuideStep.Finish
                _title.Text = Lang.T("8. 完成")
                If _bg Is Nothing Then
                    _desc.Text = Lang.T("沒有背景：去背好的人物可以存成透明 PNG、複製到剪貼簿（貼到其他程式），或按「開成新影像」在主畫面繼續編輯。")
                    _summary.Text = Lang.F("人物：{0}（{1} × {2}）", IO.Path.GetFileName(If(_sourceFile, Lang.T("貼上的圖片"))), _fw, _fh) & vbCrLf & vbCrLf &
                                    Lang.F("去背：{0}", If(_cut.AiMaskIsSource, Lang.T("使用原圖的透明背景"), CutoutModels.DisplayName(_fig.Model)))
                Else
                    _desc.Text = If(_bgFile IsNot Nothing OrElse _bgPasted IsNot Nothing,
                                    Lang.T("按「套用」，背景會開成新影像、人物（和陰影）加成上面的合成圖層。之後可以在主畫面移動、縮放、調整透明度，或刪除。"),
                                    Lang.T("按「套用」，人物（和陰影）會加成主畫面上的合成圖層，原本的照片不變。之後可以在主畫面移動、縮放、調整透明度，或刪除。"))
                    _summary.Text = SummaryText()
                End If
        End Select
        _canvas.Invalidate()
    End Sub

    Private Function SummaryText() As String
        Dim fa = DisplayAnalysis()
        Dim lines As New List(Of String) From {
            Lang.F("人物：{0}（{1} × {2}）", Path.GetFileName(If(_sourceFile, Lang.T("貼上的圖片"))), _fw, _fh),
            Lang.F("去背：{0}", If(_cut.AiMaskIsSource, Lang.T("使用原圖的透明背景"), CutoutModels.DisplayName(_fig.Model))) &
                If(_cut.StrokeCount > 0, Lang.F("，筆刷修正 {0} 筆", _cut.StrokeCount), ""),
            Lang.F("位置：高度約畫面 {0:P0}{1}", fa.Height * _scale / _bh, If(fa.BottomCropped, Lang.T("（半身像，貼齊畫面下緣）"), "")) &
                If(_fig.FlipHorizontal, Lang.T("，左右翻轉"), ""),
            Lang.F("方向與透視：旋轉 {0:0}°，仰角透視 {1:P0}", _fig.Rotation, _fig.Keystone),
            If(HasOcclusion(), Lang.T("前後遮擋：有（人物站在塗起來的東西後面）"), Lang.T("前後遮擋：沒有")),
            Lang.F("色彩：{0}", If(_fig.Color.IsIdentity, Lang.T("保持原色"), Lang.F("依背景融合（強度 {0}）", _fig.Color.Strength))) &
                Lang.F("，邊緣透光 {0}，顆粒 {1}", _fig.LightWrap, _fig.Grain),
            If(_fig.Shadow.Enabled AndAlso Not fa.BottomCropped, Lang.F("陰影：方向 {0:0}°，長度 {1:P0}，濃淡 {2}", _fig.Shadow.Direction, _fig.Shadow.Length, _fig.Shadow.Opacity), Lang.T("陰影：沒有"))}
        Return String.Join(vbCrLf & vbCrLf, lines)
    End Function

    '=====================================================================
    ' 1 選人物
    '=====================================================================

    Private Sub OpenFigureWithDialog()
        Using dlg As New OpenFileDialog With {.Title = Lang.T("選擇人物圖片"), .Filter = Lang.T("圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有檔案|*.*")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            LoadFigureFile(dlg.FileName)
        End Using
    End Sub

    Private Sub PasteFigure()
        Try
            Dim data = Clipboard.GetDataObject()
            Dim file = If(data Is Nothing, Nothing, frmEditor.ImageFile(data))
            If file IsNot Nothing Then
                LoadFigureFile(file)
                Return
            End If
            Dim bmp = If(data Is Nothing, Nothing, frmEditor.ImageBitmap(data))
            If bmp Is Nothing Then
                _status.Text = Lang.T("剪貼簿裡沒有圖片。")
                Return
            End If
            SetFigure(CompositeBaker.FitWorking(bmp), Nothing)
        Catch ex As Exception When TypeOf ex Is Runtime.InteropServices.ExternalException OrElse TypeOf ex Is IOException
            _status.Text = Lang.T("無法讀取剪貼簿：") & ex.Message
        End Try
    End Sub

    ''' <summary>載入人物圖（測試也用這個）。</summary>
    Friend Sub LoadFigureFile(file As String)
        Try
            SetFigure(CompositeBaker.LoadFigure(file), file)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is UnauthorizedAccessException
            _status.Text = Lang.T("無法開啟圖片：") & ex.Message
        End Try
    End Sub

    ''' <summary>
    ''' 人物由別的工具交過來（怪獸去背小幫手已經去好背的透明圖，由這裡複製一份）：
    ''' 透明背景會被當成「已經去背」，去背那一步可以略過；說明改成提示先在下面選背景。
    ''' </summary>
    Friend Sub PresetFigure(cutout As Bitmap, name As String, hint As String)
        SetFigure(CompositeBaker.FitWorking(DirectCast(cutout.Clone(), Bitmap)), Nothing)
        _figInfo.Text = Lang.F("{0}{1}{2} × {3} 像素", name, vbCrLf, _fw, _fh) & vbCrLf & vbCrLf & hint
        _status.Text = hint
    End Sub

    Private Sub SetFigure(bmp As Bitmap, file As String)
        _figure?.Dispose()
        _figure = bmp
        _fw = bmp.Width : _fh = bmp.Height
        _figPx = CompositeBaker.ReadPixels(bmp)
        _sourceFile = file
        _placed = False
        _shadowInit = False
        _candidates.Clear()
        _candList.Items.Clear()
        Dim srcAlpha = CompositeBaker.SourceAlpha(_figPx, _fw, _fh)
        _sourceHadAlpha = srcAlpha IsNot Nothing
        _cut.SetFigure(bmp, _figPx, srcAlpha, _sourceHadAlpha)
        _figAnalysis = Nothing
        _fig.Color = New CompositeColor With {.Mode = CompositeColorMode.Environment}
        _figInfo.Text = Lang.F("{0}{1}{2} × {3} 像素", If(file Is Nothing, Lang.T("貼上的圖片"), Path.GetFileName(file)), vbCrLf, _fw, _fh) & vbCrLf & vbCrLf &
                        If(_sourceHadAlpha, Lang.T("這張圖已經是透明背景，可以直接用（去背那一步可以略過）。"), Lang.T("下一步用 AI 去背。"))
        InvalidatePrepared(immediate:=True)
        ShowStep(_step)
    End Sub

    ''' <summary>
    ''' 回到引導視窗修改既有的合成圖層：讀回人物圖、遮罩、AI 遮罩、遮擋，還原所有參數。
    ''' 圖片物件在主畫面上被移動、縮放過時，錨點與大小跟著換算。從「位置與大小」開始。失敗時回傳 False（狀態列有原因）。
    ''' </summary>
    Friend Function LoadExisting(o As Overlay) As Boolean
        Dim c = o.Composite
        If c Is Nothing OrElse String.IsNullOrEmpty(c.SourcePath) OrElse Not File.Exists(c.SourcePath) Then
            _status.Text = Lang.T("找不到這個合成圖層的人物原圖，無法修改。")
            Return False
        End If
        Dim bmp = CompositeBaker.LoadFigure(c.SourcePath)
        _fig = c.Clone()
        _figure?.Dispose()
        _figure = bmp
        _fw = bmp.Width : _fh = bmp.Height
        _figPx = CompositeBaker.ReadPixels(bmp)
        _sourceFile = Nothing
        _figAnalysis = Nothing
        Dim ai = CompositeBaker.LoadMask(c.AiMaskPath, _fw, _fh)
        Dim final = CompositeBaker.LoadMask(c.MaskPath, _fw, _fh)
        If final Is Nothing Then
            _status.Text = Lang.T("找不到這個合成圖層的遮罩，無法修改。")
            Return False
        End If
        ' 沒有 AI 遮罩：原圖本來就透明（或用的是最後的遮罩），筆刷修正以最後的遮罩為準
        _sourceHadAlpha = ai Is Nothing
        _cut.SetOptions(New CutoutEditOptions With {.EdgeRefine = _fig.EdgeRefine, .Decontaminate = _fig.Decontaminate})
        _cut.SetFigure(bmp, _figPx, If(ai, DirectCast(final.Clone(), Byte())), _sourceHadAlpha, _fig.Model)
        _cut.SetFinalMask(final) ' 存好的遮罩（含當時的筆刷修正）原樣用
        _mask = final
        _figAnalysis = Nothing
        _occ = CompositeBaker.LoadMask(c.OcclusionMaskPath, _bw, _bh)
        Dim follow = CompositePlacement.FollowLayer(c.TargetX, c.TargetY, c.LayerX, c.LayerY, c.LayerSize, o.X, o.Y, o.Size)
        Dim fa = DisplayAnalysis()
        _tx = follow.TargetX * _bw : _ty = follow.TargetY * _bh
        _scale = c.HeightFraction * follow.Factor * _bh / Math.Max(1, fa.Height)
        _horizon = If(c.Horizon >= 0, c.Horizon * _bh, -1)
        _placed = True
        _shadowInit = True
        _syncing = True
        _flip.Checked = _fig.FlipHorizontal
        _useHorizon.Checked = _horizon >= 0
        _syncing = False
        _figInfo.Text = Lang.F("{0}{1}{2} × {3} 像素", _fig.Name, vbCrLf, _fw, _fh)
        Text = Lang.T("人物合成（修改）")
        RebuildOccluderViews()
        SyncHeightSlider()
        SyncPerspectiveControls()
        RebuildPrepared()
        ShowStep(GuideStep.Place)
        _status.Text = Lang.T("正在修改既有的合成圖層，按「套用」會取代原本的圖層。")
        Return True
    End Function

    '=====================================================================
    ' 2 去背
    '=====================================================================

    ''' <summary>AI 去背（處理中視窗）；完成或失敗後回傳。測試也用這個。</summary>
    Friend Function RunAutoCutout() As Task
        Return _cut.RunCutoutAsync(Me)
    End Function

    '=====================================================================
    ' 3 位置與大小
    '=====================================================================

    Private Sub EnsureScene()
        If _scene Is Nothing Then _scene = SceneAnalyzer.Analyze(_bgPx, _bw, _bh)
    End Sub

    ''' <summary>顯示方向（翻轉後）的人物分析。</summary>
    Private Function DisplayAnalysis() As FigureAnalysis
        If _mask Is Nothing Then Return New FigureAnalysis With {.Bounds = New Rectangle(0, 0, Math.Max(1, _fw), Math.Max(1, _fh)), .FeetX = _fw / 2.0, .FeetY = _fh}
        If _figAnalysis Is Nothing Then
            Try
                _figAnalysis = FigureAnalyzer.Analyze(_mask, _fw, _fh)
            Catch ex As InvalidOperationException
                _figAnalysis = New FigureAnalysis With {.Bounds = New Rectangle(0, 0, _fw, _fh), .FeetX = _fw / 2.0, .FeetY = _fh, .FeetLeft = 0, .FeetRight = _fw}
            End Try
        End If
        Return If(_fig.FlipHorizontal, _figAnalysis.Mirrored(_fw), _figAnalysis)
    End Function

    Private Function Geom() As CompositeGeometry
        Dim fa = DisplayAnalysis()
        Dim a = fa.Anchor
        Return New CompositeGeometry With {.AnchorX = a.X, .AnchorY = a.Y, .TX = _tx, .TY = _ty, .Scale = _scale,
                                           .Rotation = _fig.Rotation, .Keystone = _fig.Keystone, .FigureHeight = fa.Height}
    End Function

    ''' <summary>自動定位（測試也用這個）。</summary>
    Friend Sub AutoPlace()
        If _mask Is Nothing Then Return
        EnsureScene()
        Dim fa = DisplayAnalysis()
        Dim alpha = If(_fig.FlipHorizontal, CompositeBaker.MirrorAlpha(_mask, _fw, _fh), _mask)
        _candidates = CompositePlacement.Suggest(_scene, fa, alpha, _fw, _fh)
        _syncing = True
        _candList.Items.Clear()
        For Each c In _candidates.Take(8)
            _candList.Items.Add($"{c.Score:0.00}  {c.Description}")
        Next
        _syncing = False
        If _candidates.Count > 0 Then
            _candList.SelectedIndex = 0 ' 會套用第一名
            _status.Text = Lang.T("已套用推薦的位置：") & _candidates(0).Description
        End If
    End Sub

    Private Sub ApplyCandidate(c As PlacementCandidate)
        _tx = c.TargetX : _ty = c.TargetY : _scale = c.Scale
        _placed = True
        SyncHeightSlider()
        RebuildLayers()
    End Sub

    Private Sub SyncHeightSlider()
        Dim fa = DisplayAnalysis()
        _syncing = True
        _height.Value = Math.Clamp(CInt(Math.Round(fa.Height * _scale / _bh * 100)), _height.Minimum, _height.Maximum)
        _heightValue.Text = _height.Value & "%"
        _syncing = False
    End Sub

    '=====================================================================
    ' 4 方向與透視
    '=====================================================================

    Private Sub SyncPerspectiveControls()
        _syncing = True
        _rotation.Value = Math.Clamp(CInt(Math.Round(_fig.Rotation)), _rotation.Minimum, _rotation.Maximum)
        _rotationValue.Text = _rotation.Value & "°"
        _keystone.Value = Math.Clamp(CInt(Math.Round(_fig.Keystone * 100)), _keystone.Minimum, _keystone.Maximum)
        _keystoneValue.Text = _keystone.Value & "%"
        _syncing = False
    End Sub

    ''' <summary>依兩條參考線推估旋轉與仰角透視（測試也用這個）。</summary>
    Friend Sub ApplyGuides()
        If _guides.Count < 2 Then
            _status.Text = Lang.T("請先畫兩條參考線。")
            Return
        End If
        Dim fa = DisplayAnalysis()
        Dim r = CompositeGeometry.FromGuides(_guides(0).A, _guides(0).B, _guides(1).A, _guides(1).B, _ty, fa.Height * _scale)
        ' 太小的角度當成 0（顯示時才不會出現「-0.0°」）
        _fig.Rotation = If(Math.Abs(r.Rotation) < 0.05, 0, r.Rotation)
        _fig.Keystone = r.Keystone
        SyncPerspectiveControls()
        RebuildLayers()
        _status.Text = Lang.F("依參考線：旋轉 {0:0.0}°，仰角透視 {1:P0}。", _fig.Rotation, r.Keystone)
    End Sub

    ''' <summary>加一條參考線（背景像素；最多兩條，第三條換掉最舊的）。</summary>
    Friend Sub AddGuide(a As PointF, b As PointF)
        If _guides.Count >= 2 Then _guides.RemoveAt(0)
        _guides.Add((a, b))
        _canvas.Invalidate()
    End Sub

    '=====================================================================
    ' 5 前後遮擋
    '=====================================================================

    Private Function HasOcclusion() As Boolean
        Return _occ IsNot Nothing AndAlso _occ.Any(Function(v) v > 0)
    End Function

    ''' <summary>遮擋的兩張預覽圖：擋在人物前面的背景像素（畫在人物上面）、紅色範圍（遮擋那一步才顯示）。</summary>
    Private Sub RebuildOccluderViews()
        _occluderView?.Dispose() : _occluderView = Nothing
        _occTint?.Dispose() : _occTint = Nothing
        If HasOcclusion() Then
            Dim px = DirectCast(_bgPx.Clone(), Byte())
            Dim tint(_bw * _bh * 4 - 1) As Byte
            For i = 0 To _bw * _bh - 1
                px(i * 4 + 3) = _occ(i)
                tint(i * 4 + 2) = 255 : tint(i * 4 + 3) = CByte(_occ(i) * 110 \ 255)
            Next
            _occluderView = CompositeBaker.ToBitmap(px, _bw, _bh)
            _occTint = CompositeBaker.ToBitmap(tint, _bw, _bh)
        End If
        _canvas.Invalidate()
    End Sub

    ''' <summary>魔術棒：背景上點一下，相近顏色的相連範圍加進遮擋（測試也用這個）。</summary>
    Friend Sub OcclusionWand(bx As Integer, by As Integer)
        Dim sel = CompositeBaker.FloodSelect(_bgPx, _bw, _bh, bx, by, _occTol.Value)
        If _occ Is Nothing Then ReDim _occ(_bw * _bh - 1)
        For i = 0 To sel.Length - 1
            If sel(i) > _occ(i) Then _occ(i) = sel(i)
        Next
        RebuildOccluderViews()
    End Sub

    Private Sub OcclusionPaint(bx As Single, by As Single, r As Single, keep As Boolean)
        If _occ Is Nothing Then ReDim _occ(_bw * _bh - 1)
        CutoutEdit.PaintDab(_occ, _bw, _bh, bx, by, r, keep)
    End Sub

    '=====================================================================
    ' 6 光線與色彩
    '=====================================================================

    ''' <summary>依目前位置附近的背景推薦色彩（測試也用這個）。</summary>
    Friend Sub AutoColor()
        If _mask Is Nothing Then Return
        Dim r = Geom().BoundsOf(DisplayAnalysis().Bounds)
        Dim region = Rectangle.Inflate(Rectangle.Round(r), CInt(r.Width * 0.4), CInt(r.Height * 0.4))
        Dim strength = _fig.Color.Strength
        _fig.Color = CompositeColorMatch.Recommend(_bgPx, _bw, _bh, region, _figPx, _fw, _fh, _mask)
        _fig.Color.Strength = If(strength > 0, strength, 100)
        ' 顆粒：背景的雜訊比人物（縮放到背景上的大小）多多少就補多少；邊緣透光給一點
        Dim bgNoise = CompositeNoise.Estimate(_bgPx, _bw, _bh, region, Nothing)
        Dim figNoise = CompositeNoise.Estimate(_figPx, _fw, _fh, New Rectangle(0, 0, _fw, _fh), _mask) * Math.Min(1.0, _scale)
        _fig.Grain = Math.Clamp(CInt(Math.Round(Math.Sqrt(Math.Max(0, bgNoise * bgNoise - figNoise * figNoise)) * 10)), 0, 100)
        If _fig.LightWrap = 0 Then _fig.LightWrap = 30
        _syncing = True
        _matchColor.Checked = True
        _syncing = False
        SyncColorSliders()
        InvalidatePrepared()
        _status.Text = Lang.T("已依人物附近的背景調整亮度、對比、飽和度、色溫與顆粒。")
    End Sub

    Private Sub SyncColorSliders()
        Dim c = _fig.Color
        _syncing = True
        Dim setv = Sub(key As String, v As Integer)
                       Dim t = _colorSliders(key)
                       t.Slider.Value = Math.Clamp(v, t.Slider.Minimum, t.Slider.Maximum)
                       t.Value.Text = t.Slider.Value.ToString()
                       t.Slider.Enabled = c.Mode = CompositeColorMode.Environment
                   End Sub
        setv("strength", c.Strength)
        setv("bright", CInt(Math.Round(c.Brightness * 100)))
        setv("contrast", CInt(Math.Round((c.Contrast - 1) * 100)))
        setv("sat", CInt(Math.Round((c.Saturation - 1) * 100)))
        setv("temp", CInt(Math.Round(c.Temperature * 100)))
        setv("wrap", _fig.LightWrap)
        setv("grain", _fig.Grain)
        ' 邊緣透光、顆粒和色彩模式無關，一直可以調
        _colorSliders("wrap").Slider.Enabled = True
        _colorSliders("grain").Slider.Enabled = True
        _reanalyze.Enabled = c.Mode = CompositeColorMode.Environment
        _keepColor.Checked = c.Mode = CompositeColorMode.Original
        _matchColor.Checked = c.Mode = CompositeColorMode.Environment
        _syncing = False
    End Sub

    Private Sub ReadColorSlider(key As String, v As Integer)
        Dim c = _fig.Color
        Select Case key
            Case "strength" : c.Strength = v
            Case "bright" : c.Brightness = v / 100.0
            Case "contrast" : c.Contrast = 1 + v / 100.0
            Case "sat" : c.Saturation = 1 + v / 100.0
            Case "temp" : c.Temperature = v / 100.0
            Case "wrap" : _fig.LightWrap = v
            Case "grain" : _fig.Grain = v
        End Select
    End Sub

    '=====================================================================
    ' 7 陰影
    '=====================================================================

    Private Sub SyncShadowControls()
        Dim sh = _fig.Shadow
        Dim cropped = DisplayAnalysis().BottomCropped
        _syncing = True
        _shadowOn.Checked = sh.Enabled AndAlso Not cropped
        _shadowOn.Enabled = Not cropped
        _avoidSky.Checked = sh.AvoidSky
        Dim setv = Sub(key As String, v As Integer)
                       Dim t = _shadowSliders(key)
                       t.Slider.Value = Math.Clamp(v, t.Slider.Minimum, t.Slider.Maximum)
                       t.Value.Text = t.Slider.Value.ToString() & If(key = "dir", "°", "")
                       t.Slider.Enabled = sh.Enabled AndAlso Not cropped
                   End Sub
        Dim dir = sh.Direction
        While dir > 180 : dir -= 360 : End While
        While dir < -180 : dir += 360 : End While
        setv("dir", CInt(Math.Round(dir)))
        setv("len", CInt(Math.Round(sh.Length * 100)))
        setv("opacity", sh.Opacity)
        setv("soft", sh.Softness)
        setv("contact", sh.Contact)
        _avoidSky.Enabled = sh.Enabled AndAlso Not cropped
        _syncing = False
    End Sub

    ''' <summary>在預覽上點的位置當作頭的影子落點：換算成方向與長度。</summary>
    Private Sub AimShadow(bx As Double, by As Double)
        Dim sh = _fig.Shadow
        Dim figH = Math.Max(1, DisplayAnalysis().Height * _scale)
        Dim dx = bx - _tx, dy = (by - _ty) / Math.Clamp(sh.Flatten, 0.1, 1)
        sh.Direction = Math.Atan2(dy, dx) * 180 / Math.PI
        sh.Length = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / figH, 0, 1.5)
        sh.Enabled = True
        SyncShadowControls()
        RebuildLayers()
    End Sub

    '=====================================================================
    ' 預覽：準備好的人物 → 變形後的人物圖層與陰影（預覽解析度）
    '=====================================================================

    Private Sub InvalidatePrepared(Optional immediate As Boolean = False)
        _preparedTimer.Stop()
        If immediate Then
            RebuildPrepared()
        Else
            _preparedTimer.Start()
        End If
    End Sub

    Private Sub RebuildPrepared()
        If _figure Is Nothing Then Return
        Dim alpha = If(_mask, Enumerable.Repeat(CByte(255), _fw * _fh).ToArray())
        _prepared = CompositeBaker.Prepare(_figPx, _fw, _fh, alpha, _fig)
        RebuildLayers()
    End Sub

    ''' <summary>預覽的人物圖層與陰影（解析度＝畫面上的大小；遮擋另外疊在上面畫）。</summary>
    Private Sub RebuildLayers()
        If _figure Is Nothing OrElse _mask Is Nothing OrElse Not _placed Then
            _canvas.Invalidate()
            Return
        End If
        If _prepared Is Nothing Then
            Dim alpha = _mask
            _prepared = CompositeBaker.Prepare(_figPx, _fw, _fh, alpha, _fig)
        End If
        Dim r = ViewScale(_bw, _bh)
        Dim g = Geom()
        Dim fa = DisplayAnalysis()
        Dim layer = CompositeBaker.BakeLayer(_prepared, _fw, _fh, fa.Bounds, g, Nothing, _bw, _bh, r, BackgroundBlur(), _fig.LightWrap, _fig.Grain / 10.0)
        _layerPrev?.Dispose()
        _layerPrev = layer.Image
        _layerRect = layer.Rect
        _shadowPrev?.Dispose()
        _shadowPrev = Nothing
        If _step >= GuideStep.Shadow OrElse _shadowInit Then
            EnsureScene()
            Dim sh = CompositeBaker.BakeShadow(_prepared, _fw, _fh, fa, g, _fig.Shadow, _scene, Nothing, _bw, _bh, r)
            _shadowPrev = sh.Image
            _shadowRect = sh.Rect
        End If
        _bakedAt = New PointF(CSng(_tx), CSng(_ty))
        _canvas.Invalidate()
    End Sub

    Private Function BackgroundBlur() As Byte()
        If _bgBlur Is Nothing Then _bgBlur = CompositeBaker.BlurBackground(_bgPx, _bw, _bh, Math.Max(2.0, _bh * 0.012))
        Return _bgBlur
    End Function

    ''' <summary>地平線輔助：腳在 _ty 時，人物高度依地平線決定。</summary>
    Private Sub ApplyHorizon()
        If _horizon < 0 Then Return
        Dim fa = DisplayAnalysis()
        If fa.BottomCropped Then Return
        Dim h = CompositePlacement.HeightFromHorizon(_ty, _horizon)
        If h > 0 Then _scale = h / Math.Max(1, fa.Height)
        SyncHeightSlider()
    End Sub

    Private Function ViewScale(w As Integer, h As Integer) As Single
        Return CSng(Math.Min((_canvas.Width - 24) / CDbl(w), (_canvas.Height - 24) / CDbl(h)))
    End Function

    Private Function ViewOrigin(w As Integer, h As Integer, s As Single) As PointF
        Return New PointF((_canvas.Width - w * s) / 2, (_canvas.Height - h * s) / 2)
    End Function

    Private ReadOnly Property SceneMode As Boolean
        Get
            Return _step >= GuideStep.Place AndAlso _bg IsNot Nothing
        End Get
    End Property

    Private Sub PaintCanvas(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        g.InterpolationMode = InterpolationMode.HighQualityBilinear
        g.PixelOffsetMode = PixelOffsetMode.HighQuality
        If Not SceneMode Then
            If _figure Is Nothing Then
                Using f As New Font(Lang.UiFont, 13)
                    TextRenderer.DrawText(g, Lang.T("先選一張人物圖片"), f, _canvas.ClientRectangle, Color.FromArgb(180, 185, 195), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End Using
                Return
            End If
            Dim s = ViewScale(_fw, _fh)
            Dim o = ViewOrigin(_fw, _fh, s)
            _cut.DrawView(g, New RectangleF(o.X, o.Y, _fw * s, _fh * s), If(_step = GuideStep.Cutout, _hover, Nothing))
            Return
        End If
        Dim vs = ViewScale(_bw, _bh)
        Dim vo = ViewOrigin(_bw, _bh, vs)
        Dim bgRect As New RectangleF(vo.X, vo.Y, _bw * vs, _bh * vs)
        g.DrawImage(_bg, bgRect)
        If _placed AndAlso _layerPrev IsNot Nothing Then
            ' 拖曳中：圖層只位移，放開再重做
            Dim ox = CSng(_tx - _bakedAt.X), oy = CSng(_ty - _bakedAt.Y)
            g.SetClip(bgRect)
            If _shadowPrev IsNot Nothing Then
                g.DrawImage(_shadowPrev, vo.X + (_shadowRect.X + ox) * vs, vo.Y + (_shadowRect.Y + oy) * vs, _shadowRect.Width * vs, _shadowRect.Height * vs)
            End If
            Dim lr As New RectangleF(vo.X + (_layerRect.X + ox) * vs, vo.Y + (_layerRect.Y + oy) * vs, _layerRect.Width * vs, _layerRect.Height * vs)
            g.DrawImage(_layerPrev, lr)
            ' 擋在人物前面的東西（遮擋那一步只顯示紅色範圍，方便看到被擋住的人物）
            If _occluderView IsNot Nothing AndAlso _step <> GuideStep.Occlusion Then g.DrawImage(_occluderView, bgRect)
            g.ResetClip()
            If _step = GuideStep.Place OrElse _step = GuideStep.Perspective Then
                Using pen As New Pen(Color.FromArgb(160, 255, 255, 255), 1) With {.DashStyle = DashStyle.Dash}
                    g.DrawRectangle(pen, lr.X, lr.Y, lr.Width, lr.Height)
                End Using
            End If
            If _step = GuideStep.Place OrElse _step = GuideStep.Shadow Then
                Dim ax = vo.X + CSng(_tx) * vs, ay = vo.Y + CSng(_ty) * vs
                Using br As New SolidBrush(Color.FromArgb(230, 60, 160))
                    g.FillEllipse(br, ax - 5, ay - 5, 10, 10)
                End Using
            End If
        End If
        If _step = GuideStep.Occlusion AndAlso _occTint IsNot Nothing Then g.DrawImage(_occTint, bgRect)
        If _step = GuideStep.Place AndAlso _horizon >= 0 Then
            Dim hy = vo.Y + CSng(_horizon) * vs
            Using pen As New Pen(Color.FromArgb(230, 40, 220, 230), 2) With {.DashStyle = DashStyle.Dash}
                g.DrawLine(pen, vo.X, hy, vo.X + _bw * vs, hy)
            End Using
            TextRenderer.DrawText(g, Lang.T("地平線"), Font, New Point(CInt(vo.X + 6), CInt(hy - 20)), Color.FromArgb(40, 220, 230))
        End If
        If _step = GuideStep.Perspective Then
            Using pen As New Pen(Color.FromArgb(255, 210, 40), 2)
                For Each gl In _guides
                    g.DrawLine(pen, vo.X + gl.A.X * vs, vo.Y + gl.A.Y * vs, vo.X + gl.B.X * vs, vo.Y + gl.B.Y * vs)
                Next
                If _guideFrom.HasValue AndAlso _guideTo.HasValue Then
                    g.DrawLine(pen, vo.X + _guideFrom.Value.X * vs, vo.Y + _guideFrom.Value.Y * vs, vo.X + _guideTo.Value.X * vs, vo.Y + _guideTo.Value.Y * vs)
                End If
            End Using
        End If
    End Sub

    '=====================================================================
    ' 滑鼠
    '=====================================================================

    Private _dragFrom As PointF?
    Private _painting As Boolean
    Private _guideFrom As PointF?
    Private _guideTo As PointF?
    Private _draggingHorizon As Boolean
    Private _hover As Point? ' 去背那一步：筆刷圓圈的位置

    Private Function CanvasToFigure(p As Point) As PointF
        Dim s = ViewScale(_fw, _fh)
        Dim o = ViewOrigin(_fw, _fh, s)
        Return New PointF((p.X - o.X) / s, (p.Y - o.Y) / s)
    End Function

    Private Function CanvasToBackground(p As Point) As PointF
        Dim s = ViewScale(_bw, _bh)
        Dim o = ViewOrigin(_bw, _bh, s)
        Return New PointF((p.X - o.X) / s, (p.Y - o.Y) / s)
    End Function

    Private Sub CanvasMouseDown(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left OrElse _cut.Busy Then Return
        Select Case _step
            Case GuideStep.Cutout
                If _cut.BeginStroke(CanvasToFigure(e.Location), ViewScale(_fw, _fh)) Then _painting = True
            Case GuideStep.Perspective
                If _guideMode.Checked Then
                    _guideFrom = CanvasToBackground(e.Location)
                    _guideTo = _guideFrom
                ElseIf _placed Then
                    _dragFrom = e.Location
                End If
            Case GuideStep.Occlusion
                Dim b = CanvasToBackground(e.Location)
                If _occWand.Checked Then
                    OcclusionWand(CInt(b.X), CInt(b.Y))
                ElseIf _occPaint.Checked OrElse _occErase.Checked Then
                    _painting = True
                    OcclusionPaintAt(e.Location)
                End If
            Case GuideStep.Shadow
                If _placed AndAlso Not DisplayAnalysis().BottomCropped Then
                    _painting = True
                    Dim b = CanvasToBackground(e.Location)
                    AimShadow(b.X, b.Y)
                End If
            Case Else
                If _step = GuideStep.Place AndAlso _horizon >= 0 Then
                    Dim vs = ViewScale(_bw, _bh), vo = ViewOrigin(_bw, _bh, vs)
                    If Math.Abs(e.Y - (vo.Y + _horizon * vs)) <= 6 Then
                        _draggingHorizon = True
                        Return
                    End If
                End If
                If _step >= GuideStep.Place AndAlso _placed Then
                    _dragFrom = e.Location
                    _canvas.Cursor = Cursors.SizeAll
                End If
        End Select
    End Sub

    Private Sub CanvasMouseMove(sender As Object, e As MouseEventArgs)
        If _step = GuideStep.Cutout AndAlso _cut.BrushActive Then
            _hover = e.Location
            If Not _painting Then _canvas.Invalidate()
        End If
        If _draggingHorizon Then
            _horizon = Math.Clamp(CanvasToBackground(e.Location).Y, 0, _bh)
            ApplyHorizon()
            _canvas.Invalidate()
        ElseIf _guideFrom.HasValue Then
            _guideTo = CanvasToBackground(e.Location)
            _canvas.Invalidate()
        ElseIf _painting Then
            Select Case _step
                Case GuideStep.Cutout : _cut.ContinueStroke(CanvasToFigure(e.Location), ViewScale(_fw, _fh))
                Case GuideStep.Occlusion : OcclusionPaintAt(e.Location)
                Case GuideStep.Shadow
                    Dim b = CanvasToBackground(e.Location)
                    AimShadow(b.X, b.Y)
            End Select
        ElseIf _dragFrom.HasValue Then
            Dim vs = ViewScale(_bw, _bh)
            _tx += (e.X - _dragFrom.Value.X) / vs
            _ty += (e.Y - _dragFrom.Value.Y) / vs
            _dragFrom = e.Location
            If _horizon >= 0 AndAlso _step = GuideStep.Place Then
                ApplyHorizon()
                RebuildLayers() ' 大小變了，不能只位移
            Else
                _canvas.Invalidate()
            End If
        End If
    End Sub

    Private Sub CanvasMouseUp(sender As Object, e As MouseEventArgs)
        If _draggingHorizon Then
            _draggingHorizon = False
            RebuildLayers()
        End If
        If _guideFrom.HasValue Then
            Dim a = _guideFrom.Value, b = _guideTo.Value
            _guideFrom = Nothing : _guideTo = Nothing
            If Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) > 8 Then AddGuide(a, b) Else _canvas.Invalidate()
        End If
        If _painting Then
            _painting = False
            If _step = GuideStep.Cutout Then
                _cut.EndStroke()
            ElseIf _step = GuideStep.Occlusion Then
                RebuildOccluderViews()
            End If
        End If
        If _dragFrom.HasValue Then
            _dragFrom = Nothing
            _canvas.Cursor = Cursors.Default
            RebuildLayers()
        End If
    End Sub

    Private Sub OcclusionPaintAt(p As Point)
        Dim b = CanvasToBackground(p)
        OcclusionPaint(b.X, b.Y, CSng(_occSize.Value / 2.0 / ViewScale(_bw, _bh)), _occPaint.Checked)
        RebuildOccluderViews()
    End Sub

    Private Sub CanvasMouseWheel(sender As Object, e As MouseEventArgs)
        If _step < GuideStep.Place OrElse Not _placed Then Return
        ' 以錨點為中心縮放
        _scale *= Math.Pow(1.06, e.Delta / 120.0)
        _scale = Math.Clamp(_scale, 0.005, 50)
        SyncHeightSlider()
        RebuildLayers()
    End Sub

    '=====================================================================
    ' 背景（第一步可以換）
    '=====================================================================

    ''' <summary>
    ''' 換背景：working＝工作尺寸的背景（這裡接手，Nothing＝不用背景），full＝輸出的原尺寸；
    ''' file／pasted＝換成的那張（用目前的影像時兩個都是 Nothing）。位置、遮擋、地平線、陰影都重來。測試也用這個。
    ''' </summary>
    Friend Sub UseBackground(working As Bitmap, full As Size, file As String, pasted As Bitmap)
        _bg?.Dispose()
        _bgPasted?.Dispose()
        _bg = working
        _bgFile = file
        _bgPasted = pasted
        _outputSize = full
        If working IsNot Nothing Then
            _bw = working.Width : _bh = working.Height
            _bgPx = CompositeBaker.ReadPixels(working)
        Else
            _bw = 0 : _bh = 0
            _bgPx = Nothing
        End If
        _scene = Nothing
        _bgBlur = Nothing
        _occ = Nothing
        _occluderView?.Dispose() : _occluderView = Nothing
        _occTint?.Dispose() : _occTint = Nothing
        _layerPrev?.Dispose() : _layerPrev = Nothing
        _shadowPrev?.Dispose() : _shadowPrev = Nothing
        _placed = False
        _shadowInit = False
        _horizon = -1
        _guides.Clear()
        _candidates.Clear()
        _candList.Items.Clear()
        _bgCurrent.Enabled = _currentBg IsNot Nothing AndAlso (working Is Nothing OrElse file IsNot Nothing OrElse pasted IsNot Nothing)
        _bgNone.Enabled = working IsNot Nothing
        _bgInfo.Text = If(working Is Nothing,
                          Lang.T("不用背景：只做去背，完成時存成透明 PNG、複製到剪貼簿或開成新影像。"),
                          If(file IsNot Nothing, Lang.F("{0}（{1} × {2}）", IO.Path.GetFileName(file), full.Width, full.Height),
                             If(pasted IsNot Nothing, Lang.F("貼上的圖片（{0} × {1}）", full.Width, full.Height),
                                Lang.F("目前的影像（{0} × {1}）", full.Width, full.Height))))
        UpdateStepLabels()
        _canvas.Invalidate()
    End Sub

    Private Sub OpenBackgroundWithDialog()
        Using dlg As New OpenFileDialog With {.Title = Lang.T("選擇背景圖片"), .Filter = Lang.T("圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有檔案|*.*")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            LoadBackgroundFile(dlg.FileName)
        End Using
    End Sub

    ''' <summary>換成這個檔案當背景（測試也用這個）。</summary>
    Friend Function LoadBackgroundFile(file As String) As Boolean
        Try
            Using full = CompositeBaker.LoadFullImage(file)
                UseBackground(WorkingBackground(full), full.Size, file, Nothing)
            End Using
            Return True
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is UnauthorizedAccessException
            _status.Text = Lang.T("無法開啟圖片：") & ex.Message
            Return False
        End Try
    End Function

    Private Sub PasteBackground()
        Try
            Dim data = Clipboard.GetDataObject()
            Dim file = If(data Is Nothing, Nothing, frmEditor.ImageFile(data))
            If file IsNot Nothing Then
                LoadBackgroundFile(file)
                Return
            End If
            Dim bmp = If(data Is Nothing, Nothing, frmEditor.ImageBitmap(data))
            If bmp Is Nothing Then
                _status.Text = Lang.T("剪貼簿裡沒有圖片。")
                Return
            End If
            UseBackground(WorkingBackground(bmp), bmp.Size, Nothing, bmp)
        Catch ex As Exception When TypeOf ex Is Runtime.InteropServices.ExternalException OrElse TypeOf ex Is IO.IOException
            _status.Text = Lang.T("無法讀取剪貼簿：") & ex.Message
        End Try
    End Sub

    ''' <summary>背景的工作尺寸（和主畫面給的一樣，長邊 1600）。</summary>
    Private Shared Function WorkingBackground(full As Bitmap) As Bitmap
        Dim k = Math.Min(1.0, 1600 / CDbl(Math.Max(full.Width, full.Height)))
        Dim w = Math.Max(1, CInt(full.Width * k)), h = Math.Max(1, CInt(full.Height * k))
        Dim b As New Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(b)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.DrawImage(full, 0, 0, w, h)
        End Using
        Return CompositeBaker.FitWorking(b)
    End Function

    '=====================================================================
    ' 沒有背景：去背好的人物
    '=====================================================================

    ''' <summary>去背好的人物（去除殘色、裁到人的範圍、透明背景）；還沒去背時 Nothing。</summary>
    Friend Function BakeCutout() As Bitmap
        If _figure Is Nothing OrElse _mask Is Nothing Then
            _status.Text = Lang.T("請先去背（或使用原圖的透明背景）。")
            Return Nothing
        End If
        Return CompositeBaker.Bake(_figPx, _fw, _fh, _mask, _fig).Image
    End Function

    Private Sub SaveCutoutWithDialog()
        Dim cut = BakeCutout()
        If cut Is Nothing Then Return
        Using cut
            Dim name = If(_sourceFile Is Nothing, Lang.T("去背"), IO.Path.GetFileNameWithoutExtension(_sourceFile) & "_" & Lang.T("去背"))
            Using dlg As New SaveFileDialog With {.Title = Lang.T("存成透明 PNG"), .Filter = Lang.T("PNG 圖片|*.png"), .FileName = name & ".png"}
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                SaveCutout(dlg.FileName, cut)
            End Using
        End Using
    End Sub

    ''' <summary>存成透明 PNG（測試也用這個）。</summary>
    Friend Sub SaveCutout(file As String, Optional cut As Bitmap = Nothing)
        Dim own = cut Is Nothing
        If own Then cut = BakeCutout()
        If cut Is Nothing Then Return
        Try
            cut.Save(file, Imaging.ImageFormat.Png)
            _status.Text = Lang.F("已存成 {0}（{1} × {2}，透明背景）。", file, cut.Width, cut.Height)
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Runtime.InteropServices.ExternalException
            _status.Text = Lang.T("無法儲存：") & ex.Message
        Finally
            If own Then cut.Dispose()
        End Try
    End Sub

    ''' <summary>複製到剪貼簿（PNG 保留透明，另附白底給不支援透明的程式）。測試也用這個。</summary>
    Friend Sub CopyCutout()
        Using cut = BakeCutout()
            If cut Is Nothing Then Return
            Try
                frmEditor.PutOnClipboard(cut)
                _status.Text = Lang.F("已複製到剪貼簿（{0} × {1}，透明背景）。", cut.Width, cut.Height)
            Catch ex As Runtime.InteropServices.ExternalException
                _status.Text = Lang.T("剪貼簿正被其他程式使用，請稍後再試。")
            End Try
        End Using
    End Sub

    '=====================================================================
    ' 套用
    '=====================================================================

    ''' <summary>做出結果並關閉（測試也用這個）。</summary>
    Friend Sub Finish()
        If _bg Is Nothing Then
            Dim cut = BakeCutout()
            If cut Is Nothing Then Return
            Result = New GuideResult With {.NoBackground = True, .Cutout = cut, .SourceFile = _sourceFile}
            DialogResult = DialogResult.OK
            Return
        End If
        If _figure Is Nothing OrElse _mask Is Nothing OrElse Not _placed Then
            _status.Text = Lang.T("還沒完成前面的步驟。")
            Return
        End If
        Dim fa = DisplayAnalysis()
        Dim g = Geom()
        Dim prepared = CompositeBaker.Prepare(_figPx, _fw, _fh, _mask, _fig)
        ' 圖層解析度：不超過照片原尺寸、也不超過人物原圖本身的細節（每個人物像素至少一個圖層像素）
        Dim outScale = _outputSize.Width / CDbl(_bw)
        Dim r = Math.Min(outScale, Math.Max(1.0, 1 / Math.Max(0.0001, _scale)))
        Dim occ = If(HasOcclusion(), _occ, Nothing)
        Dim layer = CompositeBaker.BakeLayer(prepared, _fw, _fh, fa.Bounds, g, occ, _bw, _bh, r, BackgroundBlur(), _fig.LightWrap, _fig.Grain / 10.0)
        If layer.Image Is Nothing Then
            _status.Text = Lang.T("人物整個在畫面外面，請先移回畫面裡。")
            Return
        End If
        EnsureScene()
        Dim sh = CompositeBaker.BakeShadow(prepared, _fw, _fh, fa, g, _fig.Shadow, _scene, occ, _bw, _bh, Math.Min(outScale, 1.5))
        Dim f = _fig.Clone()
        Dim a = fa.Anchor
        f.AnchorX = If(_fig.FlipHorizontal, _fw - a.X, a.X) : f.AnchorY = a.Y ' 錨點存翻轉前的座標
        f.TargetX = _tx / _bw : f.TargetY = _ty / _bh : f.HeightFraction = fa.Height * _scale / _bh
        f.Horizon = If(_horizon >= 0, _horizon / _bh, -1)
        If fa.BottomCropped Then f.Shadow.Enabled = False
        Result = New GuideResult With {
            .Figure = f, .Baked = layer.Image, .ShadowImage = sh.Image, .FigureImage = DirectCast(_figure.Clone(), Bitmap),
            .Mask = DirectCast(_mask.Clone(), Byte()), .AiMask = If(_cut.AiMaskIsSource, Nothing, _cut.AiMask),
            .FigureWidth = _fw, .FigureHeight = _fh, .SourceFile = _sourceFile,
            .Occlusion = If(occ Is Nothing, Nothing, DirectCast(occ.Clone(), Byte())), .BackgroundWidth = _bw, .BackgroundHeight = _bh,
            .X = (layer.Rect.X + layer.Rect.Width / 2) / _bw, .Y = (layer.Rect.Y + layer.Rect.Height / 2) / _bh, .Size = layer.Rect.Height / _bh}
        If sh.Image IsNot Nothing Then
            Result.ShadowX = (sh.Rect.X + sh.Rect.Width / 2) / _bw
            Result.ShadowY = (sh.Rect.Y + sh.Rect.Height / 2) / _bh
            Result.ShadowSize = sh.Rect.Height / _bh
        End If
        Result.BackgroundFile = _bgFile
        Result.BackgroundImage = If(_bgPasted Is Nothing, Nothing, DirectCast(_bgPasted.Clone(), Bitmap))
        DialogResult = DialogResult.OK
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _preparedTimer.Dispose()
            _bg?.Dispose()
            _currentBg?.Dispose()
            _bgPasted?.Dispose()
            _figure?.Dispose()
            _layerPrev?.Dispose()
            _shadowPrev?.Dispose()
            _occluderView?.Dispose()
            _occTint?.Dispose()
            _font.Dispose()
            _boldFont.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>雙緩衝的預覽區。</summary>
    Private NotInheritable Class GuideCanvas
        Inherits Control
        Public Sub New()
            SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        End Sub
    End Class
End Class
