Imports PhotoEdit

''' <summary>
''' 「局部」分頁（漸層濾鏡、局部筆刷、背景模糊、移軸）與「裝飾」分頁（邊框、文字、貼圖）。
''' 畫布工具依分頁與選取自動切換；使用工具時暫時不畫邊框，畫面座標才會和照片一致。
''' </summary>
Partial Friend Class frmEditor

    Private Const TabRepair As Integer = 3
    Private Const TabLocal As Integer = 4
    Private Const TabDecor As Integer = 5
    Private Const TabSticker As Integer = 6

    Private _localIndex As Integer = -1
    Private _overlayIndex As Integer = -1
    Private _overlayDragStart As (X As Double, Y As Double, Size As Double, Rotation As Double)

    Private ReadOnly _localList As New ComboBox()
    Private ReadOnly _localDelete As New Button()
    Private ReadOnly _localBrushSize As New Aqua.Slider()
    Private ReadOnly _localHint As New Label()
    Private ReadOnly _frameCombo As New ComboBox()
    Private ReadOnly _overlayText As New TextBox()
    Private ReadOnly _overlayFont As New ComboBox()
    Private ReadOnly _overlayColor As New Button()
    Private ReadOnly _overlayShadow As New CheckBox()
    Private ReadOnly _overlayBold As New CheckBox()
    Private ReadOnly _overlayDelete As New Button()

    Private Shared ReadOnly LocalKeys As String() = {"l_exposure", "l_contrast", "l_saturation", "l_temperature"}

    '=====================================================================
    ' 版面
    '=====================================================================

    Private Sub BuildLocalPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        AddHeading(L, "局部調整")
        AddButtonPair(L, "＋ 漸層濾鏡", "btn.addgradient", Sub() AddLocal(LocalKind.Gradient),
                         "＋ 局部筆刷", "btn.addbrush", Sub() AddLocal(LocalKind.Brush))
        _localList.DropDownStyle = ComboBoxStyle.DropDownList
        _localList.SetBounds(8, L.Y + 2, L.Width - 8 - 70, 24)
        AddHandler _localList.SelectedIndexChanged, Sub()
                                                         If _syncing Then Return
                                                         _localIndex = _localList.SelectedIndex
                                                         SyncSliders()
                                                         UpdateCreativeControls()
                                                         UpdateToolFromTab()
                                                     End Sub
        _localDelete.Text = "刪除"
        _localDelete.UseVisualStyleBackColor = True
        _localDelete.SetBounds(L.Width - 62, L.Y, 62, 28)
        AddHandler _localDelete.Click, Sub() DeleteLocal()
        _help.SetHelp("local.list", _localList)
        _help.SetHelp("local.delete", _localDelete)
        L.Add(_localList)
        L.Add(_localDelete)
        L.Y += 36

        Dim signed As Func(Of Integer, String) = Function(v) If(v > 0, "+" & v, v.ToString())
        AddRow(L, New SliderRow With {.Key = "l_exposure", .Caption = "曝光", .Minimum = -30, .Maximum = 30,
            .Format = Function(v) (v / 10.0).ToString("+0.0;-0.0;0.0") & " EV",
            .GetValue = Function(r) If(SelLocal(r) Is Nothing, 0, CInt(Math.Round(SelLocal(r).Exposure * 10))),
            .SetValue = Sub(r, v)
                            If SelLocal(r) IsNot Nothing Then SelLocal(r).Exposure = v / 10.0
                        End Sub})
        AddRow(L, MakeRow("l_contrast", "對比", -100, 100, signed, Function(r) If(SelLocal(r) Is Nothing, 0, SelLocal(r).Contrast),
                          Sub(r, v)
                              If SelLocal(r) IsNot Nothing Then SelLocal(r).Contrast = v
                          End Sub))
        AddRow(L, MakeRow("l_saturation", "飽和度", -100, 100, signed, Function(r) If(SelLocal(r) Is Nothing, 0, SelLocal(r).Saturation),
                          Sub(r, v)
                              If SelLocal(r) IsNot Nothing Then SelLocal(r).Saturation = v
                          End Sub))
        AddRow(L, MakeRow("l_temperature", "色溫", -100, 100, signed, Function(r) If(SelLocal(r) Is Nothing, 0, SelLocal(r).Temperature),
                          Sub(r, v)
                              If SelLocal(r) IsNot Nothing Then SelLocal(r).Temperature = v
                          End Sub))

        _help.SetHelpLinked("local.brushsize", _localBrushSize, AddCaption(L, "筆刷大小", L.Y), _localBrushSize)
        _localBrushSize.Minimum = 6
        _localBrushSize.Maximum = 200
        _localBrushSize.Value = 40
        _localBrushSize.ShowTicks = False
        _localBrushSize.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - 8, 24)
        AddHandler _localBrushSize.ValueChanged, Sub()
                                                     If _canvas.Tool = PreviewCanvas.CanvasTool.LocalBrush Then _canvas.BrushRadius = _localBrushSize.Value
                                                 End Sub
        L.Add(_localBrushSize)
        L.Y += RowHeight
        _localHint.AutoSize = False
        _localHint.BackColor = Color.Transparent
        _localHint.ForeColor = Color.FromArgb(105, 110, 120)
        _localHint.SetBounds(8, L.Y + 2, L.Width - 10, 34)
        L.Add(_localHint)
        L.Y += 40

        AddHeading(L, "模糊特效")
        AddRow(L, MakeRow("bgblur", "背景模糊", 0, 100, AddressOf Plain, Function(r) r.BackgroundBlur, Sub(r, v) r.BackgroundBlur = v))
        AddRow(L, MakeRow("tiltshift", "移軸", 0, 100, AddressOf Plain, Function(r) r.TiltShift, Sub(r, v) r.TiltShift = v))
        AddRow(L, MakeRow("tiltpos", "清楚帶位置", 0, 100, Function(v) v & "%", Function(r) r.TiltShiftPosition, Sub(r, v) r.TiltShiftPosition = v))
    End Sub

    ''' <summary>「裝飾」分頁：邊框。</summary>
    Private Sub BuildDecorPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        AddHeading(L, "邊框")
        _help.SetHelpLinked("frame.style", _frameCombo, AddCaption(L, "樣式", L.Y), _frameCombo)
        _frameCombo.DropDownStyle = ComboBoxStyle.DropDownList
        _frameCombo.Items.AddRange(Creative.FrameNames.Cast(Of Object)().ToArray())
        _frameCombo.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - 8, 24)
        AddHandler _frameCombo.SelectedIndexChanged, Sub()
                                                          If _syncing OrElse _photo Is Nothing Then Return
                                                          Dim style = CType(_frameCombo.SelectedIndex, PhotoFrameStyle)
                                                          ApplyChange(Sub(r) r.Frame = style)
                                                      End Sub
        L.Add(_frameCombo)
        L.Y += RowHeight + 4
        AddRow(L, MakeRow("framesize", "寬度", 0, 100, AddressOf Plain, Function(r) r.FrameSize, Sub(r, v) r.FrameSize = v))
        AddHint(L, "文字請到「文字」分頁，貼圖請到「貼圖」分頁；選取後的大小、旋轉、透明度也在各自的分頁調整。")
    End Sub

    ''' <summary>綁定到「選取中的文字／貼圖」某個整數屬性的滑桿列。</summary>
    Private Function OverlayRow(key As String, caption As String, min As Integer, max As Integer, format As Func(Of Integer, String),
                                getter As Func(Of Overlay, Integer), setter As Action(Of Overlay, Integer)) As SliderRow
        Return New SliderRow With {.Key = key, .Caption = caption, .Minimum = min, .Maximum = max, .Format = format,
            .GetValue = Function(r) If(SelOverlay(r) Is Nothing, Math.Max(min, Math.Min(max, getter(New Overlay()))), getter(SelOverlay(r))),
            .SetValue = Sub(r, v)
                            If SelOverlay(r) IsNot Nothing Then setter(SelOverlay(r), v)
                        End Sub}
    End Function

    '=====================================================================
    ' 選取與工具
    '=====================================================================

    Private Function SelLocal(r As EditRecipe) As LocalAdjustment
        If r.LocalAdjustments Is Nothing OrElse _localIndex < 0 OrElse _localIndex >= r.LocalAdjustments.Count Then Return Nothing
        Return r.LocalAdjustments(_localIndex)
    End Function

    Private Function SelOverlay(r As EditRecipe) As Overlay
        If r.Overlays Is Nothing OrElse _overlayIndex < 0 OrElse _overlayIndex >= r.Overlays.Count Then Return Nothing
        Return r.Overlays(_overlayIndex)
    End Function

    ''' <summary>依目前分頁與選取決定畫布工具；工具改變時重算畫面（邊框顯示與否會變）。</summary>
    Private Sub UpdateToolFromTab()
        Dim tool = PreviewCanvas.CanvasTool.None
        If _photo IsNot Nothing AndAlso Not _cropMode Then
            Select Case _tabs.SelectedIndex
                Case TabRepair
                    If _healToggle.Checked Then tool = PreviewCanvas.CanvasTool.Heal
                Case TabLocal
                    Dim sel = SelLocal(_recipe)
                    If sel IsNot Nothing AndAlso sel.Kind <> LocalKind.Selection Then tool = If(sel.Kind = LocalKind.Gradient, PreviewCanvas.CanvasTool.Gradient, PreviewCanvas.CanvasTool.LocalBrush)
                Case TabSticker, TabText
                    tool = PreviewCanvas.CanvasTool.Overlay
                Case TabCutout
                    If _wandToggle.Checked Then
                        tool = PreviewCanvas.CanvasTool.Wand
                    ElseIf MaskBrushActive AndAlso _recipe.Cutout IsNot Nothing Then
                        tool = PreviewCanvas.CanvasTool.MaskBrush
                    End If
                Case TabDraw, TabSelect
                    tool = PreviewCanvas.CanvasTool.Draw
            End Select
        End If
        If _tabs.SelectedIndex <> TabRepair AndAlso _healToggle.Checked Then _healToggle.Checked = False
        Dim drawing = _tabs.SelectedIndex = TabDraw
        If _drawStrip.Visible <> drawing Then
            _drawStrip.Visible = drawing
            _drawBar.Visible = drawing
            AutoShowColorWindow(drawing) ' 選色視窗跟著繪圖分頁出現、收起
        End If
        If Not drawing Then
            CommitCalloutEditor()
            _polyPoints = Nothing
        End If

        Select Case tool
            Case PreviewCanvas.CanvasTool.Heal : _canvas.BrushRadius = _brushSize.Value
            Case PreviewCanvas.CanvasTool.LocalBrush : _canvas.BrushRadius = _localBrushSize.Value
            Case PreviewCanvas.CanvasTool.MaskBrush
                _canvas.BrushRadius = _maskBrushSize.Value
                _canvas.MaskKeep = _keepBrush.Checked
        End Select
        If _tabs.SelectedIndex <> TabCutout AndAlso _cutoutShowMask Then _viewResult.Checked = True
        If _canvas.Tool <> tool Then
            _canvas.Tool = tool
            RequestRender()
        End If
        UpdateToolOverlays()
    End Sub

    ''' <summary>畫面用的配方：檢查遮罩時，去背背景換成半透明紅色（不影響存檔與匯出）。</summary>
    Private Function DisplayRecipe() As EditRecipe
        If Not _cutoutShowMask OrElse _recipe.Cutout Is Nothing Then Return _recipe
        Dim r = _recipe.Clone()
        r.Cutout.Background = CutoutBackground.MaskPreview
        Return r
    End Function

    '---------------------------------------------------------------------
    ' 邊框換算：畫布顯示的是「照片＋邊框」，工具座標要換回照片本身的 0..1。
    '---------------------------------------------------------------------

    Private _framePhotoSize As Size
    Private _frameMargins As (Left As Integer, Top As Integer, Right As Integer, Bottom As Integer)

    Private ReadOnly Property FramedSize As SizeF
        Get
            Return New SizeF(_framePhotoSize.Width + _frameMargins.Left + _frameMargins.Right,
                             _framePhotoSize.Height + _frameMargins.Top + _frameMargins.Bottom)
        End Get
    End Property

    Private Function DisplayToPhoto(p As PointF) As PointF
        If _framePhotoSize.IsEmpty Then Return p
        Dim f = FramedSize
        Return New PointF((p.X * f.Width - _frameMargins.Left) / _framePhotoSize.Width,
                          (p.Y * f.Height - _frameMargins.Top) / _framePhotoSize.Height)
    End Function

    Private Function PhotoToDisplay(p As PointF) As PointF
        If _framePhotoSize.IsEmpty Then Return p
        Dim f = FramedSize
        Return New PointF((_frameMargins.Left + p.X * _framePhotoSize.Width) / f.Width,
                          (_frameMargins.Top + p.Y * _framePhotoSize.Height) / f.Height)
    End Function

    ''' <summary>選取框（照片座標）換成畫面座標。</summary>
    Private Function FrameToDisplay(f As OverlayFrameInfo) As OverlayFrameInfo
        If _framePhotoSize.IsEmpty Then Return f
        Dim pivot = PhotoToDisplay(New PointF(f.PivotX, f.PivotY))
        Dim sx = _framePhotoSize.Width / FramedSize.Width, sy = _framePhotoSize.Height / FramedSize.Height
        Return New OverlayFrameInfo With {.PivotX = pivot.X, .PivotY = pivot.Y, .Rotation = f.Rotation,
                                          .Left = f.Left * sx, .Right = f.Right * sx, .Top = f.Top * sy, .Bottom = f.Bottom * sy}
    End Function

    ''' <summary>畫布上的漸層線與文字/貼圖範圍。</summary>
    Private Sub UpdateToolOverlays()
        If _previewBase Is Nothing Then Return
        Dim sel = SelLocal(_recipe)
        If _canvas.Tool = PreviewCanvas.CanvasTool.Gradient AndAlso sel IsNot Nothing Then
            Dim s = GeometryMapper.MapPoint(New PointF(CSng(sel.StartX), CSng(sel.StartY)), _recipe, _previewBase.Width, _previewBase.Height, True)
            Dim e = GeometryMapper.MapPoint(New PointF(CSng(sel.EndX), CSng(sel.EndY)), _recipe, _previewBase.Width, _previewBase.Height, True)
            _canvas.GradientLine = (PhotoToDisplay(s), PhotoToDisplay(e))
        Else
            _canvas.GradientLine = Nothing
        End If

        If _canvas.Tool = PreviewCanvas.CanvasTool.Overlay AndAlso _rendered IsNot Nothing AndAlso _recipe.Overlays IsNot Nothing Then
            Dim pw = If(_framePhotoSize.IsEmpty, _rendered.Width, _framePhotoSize.Width)
            Dim ph = If(_framePhotoSize.IsEmpty, _rendered.Height, _framePhotoSize.Height)
            _canvas.OverlayFrames = _recipe.Overlays.Select(Function(o) FrameToDisplay(Creative.OverlayFrame(o, pw, ph))).ToList()
        Else
            _canvas.OverlayFrames = Nothing
        End If
        _canvas.SelectedOverlay = If(SelOverlay(_recipe) Is Nothing, -1, _overlayIndex)
    End Sub

    ''' <summary>配方改變（含復原）後：修正選取索引、更新清單與控制項可用狀態。</summary>
    Private Sub UpdateCreativeControls()
        Dim locals = If(_recipe.LocalAdjustments, New List(Of LocalAdjustment)())
        If _localIndex >= locals.Count Then _localIndex = locals.Count - 1
        Dim overlays = If(_recipe.Overlays, New List(Of Overlay)())
        If _overlayIndex >= overlays.Count Then _overlayIndex = overlays.Count - 1

        Dim wasSyncing = _syncing
        _syncing = True
        Try
            Dim names = locals.Select(Function(a, i) $"{i + 1}. {If(a.Kind = LocalKind.Gradient, "漸層濾鏡", If(a.Kind = LocalKind.Selection, "選取區調整", "局部筆刷"))}").ToArray()
            If Not names.SequenceEqual(_localList.Items.Cast(Of String)()) Then
                _localList.Items.Clear()
                _localList.Items.AddRange(names.Cast(Of Object)().ToArray())
            End If
            _localList.SelectedIndex = _localIndex
            Dim sel = SelLocal(_recipe)
            _localList.Enabled = locals.Count > 0
            _localDelete.Enabled = sel IsNot Nothing
            _localBrushSize.Enabled = sel IsNot Nothing AndAlso sel.Kind = LocalKind.Brush
            _localHint.Text = If(sel Is Nothing, "按上方按鈕新增漸層濾鏡或局部筆刷。",
                              If(sel.Kind = LocalKind.Selection, "範圍是建立時的選取區；用下面的滑桿調整選取區裡面的曝光、對比、飽和度與色溫。",
                              If(sel.Kind = LocalKind.Gradient, "在照片上從效果最強處拖曳到效果消失處。",
                                 "在照片上塗抹要調整的區域（藍色）。右鍵拖曳可平移。")))
            For Each row In _rows.Where(Function(r) LocalKeys.Contains(r.Key))
                row.Slider.Enabled = sel IsNot Nothing
            Next

            _frameCombo.SelectedIndex = CInt(_recipe.Frame)
            Dim ov = SelOverlay(_recipe)
            _overlayShadow.Enabled = ov IsNot Nothing
            _overlayShadow.Checked = ov IsNot Nothing AndAlso ov.Shadow
            _overlayColor.Enabled = ov IsNot Nothing AndAlso ov.Kind <> OverlayKind.Image
            _overlayColor.BackColor = If(ov Is Nothing, SystemColors.Control, Color.FromArgb(ov.ColorArgb))
            _overlayColor.ForeColor = If(ov IsNot Nothing AndAlso Color.FromArgb(ov.ColorArgb).GetBrightness() < 0.5, Color.White, Color.Black)
            _overlayDelete.Enabled = ov IsNot Nothing
            For Each row In _rows.Where(Function(r) r.Key.StartsWith("ov_"))
                row.Slider.Enabled = ov IsNot Nothing
            Next
            UpdateTextControls()
            UpdateCutoutControls()
            UpdateDrawControls()
            UpdateLayersPanel()
            UpdateSelectControls()
        Finally
            _syncing = wasSyncing
        End Try
    End Sub

    '=====================================================================
    ' 局部調整
    '=====================================================================

    ''' <summary>新增局部調整。漸層預設為從畫面上緣到中間、曝光 -0.7（壓暗天空）；筆刷預設提亮 0.5。</summary>
    Private Sub AddLocal(kind As LocalKind)
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        Dim adj As New LocalAdjustment With {.Kind = kind}
        If kind = LocalKind.Gradient Then
            adj.Exposure = -0.7
            SetGradientEnds(adj, New PointF(0.5F, 0), New PointF(0.5F, 0.5F))
        Else
            adj.Exposure = 0.5
        End If
        ApplyChange(Sub(r)
                        If r.LocalAdjustments Is Nothing Then r.LocalAdjustments = New List(Of LocalAdjustment)()
                        r.LocalAdjustments.Add(adj)
                    End Sub)
        _localIndex = _recipe.LocalAdjustments.Count - 1
        _tabs.SelectedIndex = TabLocal
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolFromTab()
    End Sub

    ''' <summary>把畫面座標的起終點換成原圖座標存起來。</summary>
    Private Sub SetGradientEnds(adj As LocalAdjustment, start As PointF, [end] As PointF)
        Dim s = GeometryMapper.UnmapPoint(start, _recipe, _previewBase.Width, _previewBase.Height, fromCropped:=True)
        Dim e = GeometryMapper.UnmapPoint([end], _recipe, _previewBase.Width, _previewBase.Height, fromCropped:=True)
        adj.StartX = s.X : adj.StartY = s.Y
        adj.EndX = e.X : adj.EndY = e.Y
    End Sub

    Private Sub DeleteLocal()
        If SelLocal(_recipe) Is Nothing Then Return
        Dim index = _localIndex
        ApplyChange(Sub(r) r.LocalAdjustments.RemoveAt(index))
        _localIndex = Math.Min(index, If(_recipe.LocalAdjustments?.Count, 0) - 1)
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolFromTab()
    End Sub

    Private Sub OnGradientDefined(start As PointF, [end] As PointF)
        If SelLocal(_recipe) Is Nothing Then Return
        Dim index = _localIndex
        Dim s = DisplayToPhoto(start), e = DisplayToPhoto([end])
        ApplyChange(Sub(r) SetGradientEnds(r.LocalAdjustments(index), s, e))
    End Sub

    ''' <summary>局部筆刷的一筆：同修補筆刷換回原圖座標，加進選取的局部調整。</summary>
    Private Sub OnLocalStroke(points As List(Of PointF), screenRadius As Single)
        Dim sel = SelLocal(_recipe)
        If sel Is Nothing OrElse sel.Kind <> LocalKind.Brush Then Return
        Dim stroke = MakeSourceStroke(points, screenRadius)
        Dim index = _localIndex
        ApplyChange(Sub(r) r.LocalAdjustments(index).Strokes.Add(stroke))
    End Sub

    '=====================================================================
    ' 文字與貼圖
    '=====================================================================

    Private Sub AddOverlay(kind As OverlayKind, sticker As String)
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        Dim o As New Overlay With {.Kind = kind}
        If kind = OverlayKind.Text Then
            o.Text = "在這裡輸入文字"
            o.X = 0.5 : o.Y = 0.85 : o.Size = 0.08
        Else
            o.Sticker = sticker
            o.X = 0.5 : o.Y = 0.5 : o.Size = 0.18
            o.ColorArgb = StickerColor(sticker).ToArgb()
        End If
        AddOverlayObject(o, switchTab:=If(kind = OverlayKind.Text, TabText, TabDecor))
        If kind = OverlayKind.Text Then
            _overlayText.Focus()
            _overlayText.SelectAll()
        End If
    End Sub

    ''' <summary>加入一個文字/貼圖並選取它；switchTab 為要切換到的分頁（-1 不切換）。</summary>
    Private Sub AddOverlayObject(o As Overlay, switchTab As Integer)
        ApplyChange(Sub(r)
                        If r.Overlays Is Nothing Then r.Overlays = New List(Of Overlay)()
                        r.Overlays.Add(o)
                    End Sub)
        _overlayIndex = _recipe.Overlays.Count - 1
        If switchTab >= 0 Then _tabs.SelectedIndex = switchTab
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolFromTab()
    End Sub

    Private Shared Function StickerColor(sticker As String) As Color
        Select Case sticker
            Case "heart" : Return Color.FromArgb(232, 69, 90)
            Case "star", "sparkle" : Return Color.FromArgb(255, 200, 40)
            Case "bubble" : Return Color.FromArgb(60, 60, 70)
            Case "arrow" : Return Color.FromArgb(255, 110, 30)
            Case Else : Return If(BuiltInStickers.DefaultColor(sticker), Color.FromArgb(240, 60, 60))
        End Select
    End Function

    Private Sub DeleteOverlay()
        If SelOverlay(_recipe) Is Nothing Then Return
        Dim index = _overlayIndex
        ApplyChange(Sub(r) r.Overlays.RemoveAt(index))
        _overlayIndex = -1
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolOverlays()
    End Sub

    Private Sub OnOverlayPressed(index As Integer)
        _overlayIndex = index
        Dim o = SelOverlay(_recipe)
        If o IsNot Nothing Then _overlayDragStart = (o.X, o.Y, o.Size, o.Rotation)
        SyncSliders()
        UpdateCreativeControls()
    End Sub

    Private Sub OnOverlayDragged(index As Integer, dx As Single, dy As Single)
        If index <> _overlayIndex OrElse SelOverlay(_recipe) Is Nothing Then Return
        ' 畫面上的位移換成照片上的位移（有邊框時照片只佔畫面的一部分）。
        Dim sx = If(_framePhotoSize.IsEmpty, 1.0, FramedSize.Width / _framePhotoSize.Width)
        Dim sy = If(_framePhotoSize.IsEmpty, 1.0, FramedSize.Height / _framePhotoSize.Height)
        Dim nx = Math.Max(0, Math.Min(1, _overlayDragStart.X + dx * sx))
        Dim ny = Math.Max(0, Math.Min(1, _overlayDragStart.Y + dy * sy))
        ApplyChange(Sub(r)
                        r.Overlays(index).X = nx
                        r.Overlays(index).Y = ny
                    End Sub, "overlay-move")
    End Sub

    ''' <summary>拖曳角落：依按下時的大小等比例縮放。</summary>
    Private Sub OnOverlayScaled(index As Integer, factor As Single)
        If index <> _overlayIndex OrElse SelOverlay(_recipe) Is Nothing Then Return
        Dim size = Math.Round(Math.Max(0.02, Math.Min(0.9, _overlayDragStart.Size * factor)), 4)
        ApplyChange(Sub(r) r.Overlays(index).Size = size, "overlay-scale")
        SyncSliders()
    End Sub

    Private Sub OnOverlayRotated(index As Integer, degrees As Single)
        If index <> _overlayIndex OrElse SelOverlay(_recipe) Is Nothing Then Return
        Dim deg = Math.Round(degrees, 1)
        ApplyChange(Sub(r) r.Overlays(index).Rotation = deg, "overlay-rotate")
        SyncSliders()
    End Sub

    ''' <summary>Ctrl+滾輪每格放大/縮小 10%，Shift+滾輪每格旋轉 5 度。</summary>
    Private Sub OnOverlayWheel(index As Integer, up As Boolean, rotate As Boolean)
        If index <> _overlayIndex OrElse SelOverlay(_recipe) Is Nothing Then Return
        If rotate Then
            ApplyChange(Sub(r) r.Overlays(index).Rotation = NormalizeDegrees(r.Overlays(index).Rotation + If(up, -5, 5)), "overlay-rotate")
        Else
            ApplyChange(Sub(r) r.Overlays(index).Size = Math.Round(Math.Max(0.02, Math.Min(0.9, r.Overlays(index).Size * If(up, 1.1, 1 / 1.1))), 4), "overlay-scale")
        End If
        SyncSliders()
    End Sub

    Private Shared Function NormalizeDegrees(d As Double) As Double
        d = d Mod 360
        If d > 180 Then d -= 360
        If d <= -180 Then d += 360
        Return d
    End Function

    Private Sub PickOverlayColor()
        Dim o = SelOverlay(_recipe)
        If o Is Nothing Then Return
        Using dlg As New ColorDialog With {.Color = Color.FromArgb(o.ColorArgb), .FullOpen = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim argb = dlg.Color.ToArgb()
            ApplyChange(Sub(r)
                            If SelOverlay(r) IsNot Nothing Then SelOverlay(r).ColorArgb = argb
                        End Sub)
        End Using
    End Sub
End Class
