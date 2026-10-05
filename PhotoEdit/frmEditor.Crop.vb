Imports PhotoEdit

''' <summary>
''' 裁切模式（Ctrl+K）：畫布顯示未裁切的整張照片，下方兩列工具：
''' 第一列 比例（含直橫互換）、輔助線、形狀、精確尺寸、智慧構圖；第二列 拉直、旋轉、翻轉、套用／取消。
''' 裁切框與形狀在「套用」時寫入配方；拉直、旋轉、翻轉立即生效，「取消」時連同這些一起還原。
''' </summary>
Partial Friend Class frmEditor

    Private Shared ReadOnly CropRatios As (Name As String, Ratio As Double)() = {
        ("自由", 0), ("原始比例", -1), ("1:1 正方形", 1), ("4:3", 4 / 3), ("3:4", 3 / 4), ("3:2", 3 / 2), ("2:3", 2 / 3),
        ("16:9", 16 / 9), ("9:16", 9 / 16), ("5:4", 5 / 4), ("4:5（IG 直式）", 4 / 5), ("1.91:1（IG 橫式）", 1.91),
        ("A4 直式", 210 / 297), ("A4 橫式", 297 / 210), ("證件照 3.5×4.5 cm", 3.5 / 4.5)}
    Private Shared ReadOnly CropShapeNames As String() = {"矩形", "圓形", "圓角", "愛心", "星形"}
    Private Shared ReadOnly CropGuideNames As String() = {"三分線", "黃金比例", "格線", "對角線", "無"}

    Private ReadOnly _cropGuide As New ComboBox()
    Private ReadOnly _cropShape As New ComboBox()
    Private ReadOnly _cropWidth As New NumericUpDown()
    Private ReadOnly _cropHeight As New NumericUpDown()
    Private ReadOnly _cropStraighten As New Aqua.Slider()
    Private ReadOnly _cropStraightenLabel As New Label()
    Private _cropSwapped As Boolean
    Private _cropStart As EditRecipe
    Private _cropHistoryStart As Integer
    Private _cropSyncing As Boolean

    Private Sub BuildCropPanel()
        _cropPanel.Dock = DockStyle.Bottom
        _cropPanel.Height = 78
        _cropPanel.Visible = False
        _cropPanel.BackColor = Color.FromArgb(230, 236, 244)

        Dim row1 As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 38, .WrapContents = False, .Padding = New Padding(6, 5, 6, 0)}
        Dim row2 As New Panel With {.Dock = DockStyle.Fill}
        Dim tools As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .WrapContents = False, .Padding = New Padding(6, 3, 6, 0)}
        Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Right, .Width = 232, .WrapContents = False, .FlowDirection = FlowDirection.RightToLeft,
                                                 .Padding = New Padding(0, 3, 8, 0)}

        Dim caption = Function(text As String) New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(6, 6, 2, 0)}
        Dim button = Function(text As String, width As Integer) New Button With {.Text = text, .Width = width, .Height = 27, .UseVisualStyleBackColor = True,
                                                                                 .Margin = New Padding(3, 0, 3, 0)}
        ' 第一列
        Dim ratioLabel = caption("比例")
        _cropRatio.DropDownStyle = ComboBoxStyle.DropDownList
        _cropRatio.Items.AddRange(CropRatios.Select(Function(r) CObj(r.Name)).ToArray())
        _cropRatio.Width = 150
        AddHandler _cropRatio.SelectedIndexChanged, Sub()
                                                         If _syncing Then Return
                                                         _cropSwapped = False
                                                         ApplyRatioCrop()
                                                     End Sub
        _help.SetHelp("crop.ratio", ratioLabel, _cropRatio)
        Dim swap = button("⇄ 直橫", 70)
        AddHandler swap.Click, Sub() SwapCropOrientation()
        _help.SetHelp("crop.swap", swap)

        Dim guideLabel = caption("輔助線")
        _cropGuide.DropDownStyle = ComboBoxStyle.DropDownList
        _cropGuide.Items.AddRange(CropGuideNames)
        _cropGuide.Width = 92
        _cropGuide.SelectedIndex = Math.Max(0, Math.Min(CropGuideNames.Length - 1, _appSettings.CropGuide))
        _canvas.CropGuideKind = CType(_cropGuide.SelectedIndex, PreviewCanvas.CropGuide)
        AddHandler _cropGuide.SelectedIndexChanged, Sub()
                                                         _canvas.CropGuideKind = CType(_cropGuide.SelectedIndex, PreviewCanvas.CropGuide)
                                                         _appSettings.CropGuide = _cropGuide.SelectedIndex
                                                         _appSettings.Save()
                                                         _canvas.Focus()
                                                     End Sub
        _help.SetHelp("crop.guide", guideLabel, _cropGuide)

        Dim shapeLabel = caption("形狀")
        _cropShape.DropDownStyle = ComboBoxStyle.DropDownList
        _cropShape.Items.AddRange(CropShapeNames)
        _cropShape.Width = 76
        AddHandler _cropShape.SelectedIndexChanged, Sub()
                                                         If _syncing OrElse _cropShape.SelectedIndex < 0 Then Return
                                                         _canvas.CropShapeKind = CType(_cropShape.SelectedIndex, CropShape)
                                                         If _cropShape.SelectedIndex > 0 Then SetStatusMessage("形狀外會變透明；匯出時請存成 PNG（JPG 會變成白色）。")
                                                         _canvas.Focus()
                                                     End Sub
        _help.SetHelp("crop.shape", shapeLabel, _cropShape)

        Dim sizeLabel = caption("尺寸")
        For Each n In {_cropWidth, _cropHeight}
            n.Minimum = 1
            n.Maximum = 100000
            n.Width = 72
            n.Margin = New Padding(2, 2, 2, 0)
            n.TextAlign = HorizontalAlignment.Right
        Next
        AddHandler _cropWidth.ValueChanged, Sub() OnCropSizeTyped(True)
        AddHandler _cropHeight.ValueChanged, Sub() OnCropSizeTyped(False)
        Dim times = caption("×")
        Dim px = caption("px")
        _help.SetHelp("crop.size", sizeLabel, _cropWidth, times, _cropHeight, px)

        Dim smart = button("智慧構圖", 86)
        AddHandler smart.Click, Sub() ApplySmartCrop()
        _help.SetHelp("crop.smart", smart)
        row1.Controls.AddRange(New Control() {ratioLabel, _cropRatio, swap, guideLabel, _cropGuide, shapeLabel, _cropShape,
                                              sizeLabel, _cropWidth, times, _cropHeight, px, smart})

        ' 第二列
        Dim straightenLabel = caption("拉直")
        _cropStraighten.Minimum = -90
        _cropStraighten.Maximum = 90
        _cropStraighten.ShowTicks = False
        _cropStraighten.Width = 200
        _cropStraighten.Height = 24
        _cropStraighten.Margin = New Padding(2, 2, 2, 0)
        AddHandler _cropStraighten.ValueChanged, Sub() OnCropStraighten()
        _cropStraightenLabel.AutoSize = False
        _cropStraightenLabel.Width = 52
        _cropStraightenLabel.Height = 24
        _cropStraightenLabel.TextAlign = ContentAlignment.MiddleRight
        _cropStraightenLabel.Margin = New Padding(0, 2, 8, 0)
        _cropStraightenLabel.Cursor = Cursors.Hand
        AddHandler _cropStraightenLabel.DoubleClick, Sub() _cropStraighten.Value = 0
        _help.SetHelpLinked("crop.straighten", _cropStraighten, straightenLabel, _cropStraighten, _cropStraightenLabel)
        Dim rotl = button("⟲ 左轉", 70), rotr = button("⟳ 右轉", 70)
        Dim fliph = button("⇋ 水平翻轉", 96), flipv = button("⇵ 垂直翻轉", 96)
        AddHandler rotl.Click, Sub() CropRotate(False)
        AddHandler rotr.Click, Sub() CropRotate(True)
        AddHandler fliph.Click, Sub() CropFlip(True)
        AddHandler flipv.Click, Sub() CropFlip(False)
        _help.SetHelp("crop.rotl", rotl)
        _help.SetHelp("crop.rotr", rotr)
        _help.SetHelp("crop.fliph", fliph)
        _help.SetHelp("crop.flipv", flipv)
        tools.Controls.AddRange(New Control() {straightenLabel, _cropStraighten, _cropStraightenLabel, rotl, rotr, fliph, flipv})

        Dim apply = button("套用 (Enter)", 104), cancel = button("取消 (Esc)", 104)
        AddHandler apply.Click, Sub() ExitCropMode(apply:=True)
        AddHandler cancel.Click, Sub() ExitCropMode(apply:=False)
        _help.SetHelp("crop.apply", apply)
        _help.SetHelp("crop.cancel", cancel)
        actions.Controls.AddRange(New Control() {cancel, apply})

        ' 停靠順序：最後加入的最先停靠。
        row2.Controls.Add(tools)
        row2.Controls.Add(actions)
        _cropPanel.Controls.Add(row2)
        _cropPanel.Controls.Add(row1)
        AddHandler _canvas.CropChanged, Sub() SyncCropSize()
    End Sub

    '=====================================================================
    ' 進入／離開
    '=====================================================================

    Private Sub EnterCropMode()
        If _photo Is Nothing OrElse _cropMode Then Return
        _healToggle.Checked = False
        _cropMode = True
        _cropStart = _recipe.Clone()
        _cropHistoryStart = _history.UndoCount
        _cropSwapped = False
        _syncing = True
        _cropRatio.SelectedIndex = 0
        _cropShape.SelectedIndex = CInt(_recipe.CropShape)
        _syncing = False
        _canvas.AspectRatio = 0
        _canvas.CropShapeKind = _recipe.CropShape
        _canvas.Crop = If(_recipe.Crop, New CropRect())
        _canvas.CropMode = True
        _cropPanel.Visible = True
        UpdateToolFromTab()
        RenderNow()
        _canvas.CropPixelSize = FullCropSize()
        SyncCropBar()
        SyncCropSize()
        SetStatusMessage("拖曳畫出裁切範圍；拖曳四角或四邊調整大小、框內拖曳移動。Enter 套用、Esc 取消。")
    End Sub

    Private Sub ExitCropMode(apply As Boolean)
        If Not _cropMode Then Return
        Dim chosen = _canvas.Crop
        Dim shape = CType(Math.Max(0, _cropShape.SelectedIndex), CropShape)
        _cropMode = False
        _canvas.CropMode = False
        _canvas.FaceMarks = Nothing
        _cropPanel.Visible = False
        If apply Then
            Dim newCrop = If(chosen.IsFull, Nothing, chosen)
            ApplyChange(Sub(r)
                            r.Crop = newCrop
                            r.CropShape = shape
                        End Sub)
        ElseIf _cropStart IsNot Nothing AndAlso Not _recipe.Equals(_cropStart) Then
            ' 取消：裁切過程中的拉直、旋轉、翻轉一起撤掉，復原紀錄也回到進入裁切前。
            _recipe = _cropStart.Clone()
            _history.TruncateTo(_cropHistoryStart)
            OnRecipeChanged()
        End If
        _cropStart = Nothing
        UpdateToolFromTab()
        RenderNow()
    End Sub

    ''' <summary>裁切中按復原：只退回裁切過程中的拉直、旋轉、翻轉，不離開裁切模式。</summary>
    Private Function UndoInCropMode() As Boolean
        If Not _cropMode OrElse _history.UndoCount <= _cropHistoryStart Then Return False
        Dim rotation = _recipe.Rotation
        _recipe = _history.Undo(_recipe)
        OnRecipeChanged()
        AfterCropGeometry(resetFrame:=_recipe.Rotation <> rotation)
        Return True
    End Function

    '=====================================================================
    ' 比例、尺寸
    '=====================================================================

    ''' <summary>未裁切（已轉向）照片的原圖像素尺寸。</summary>
    Private Function FullCropSize() As Size
        If _photo Is Nothing Then Return Size.Empty
        Dim w = _photo.Image.Width, h = _photo.Image.Height
        Return If(_recipe.Rotation Mod 180 <> 0, New Size(h, w), New Size(w, h))
    End Function

    ''' <summary>目前比例（像素寬 ÷ 高），已套用直橫互換；0 為自由。</summary>
    Private Function SelectedCropRatio() As Double
        If _cropRatio.SelectedIndex < 0 OrElse _rendered Is Nothing Then Return 0
        Dim r = CropRatios(_cropRatio.SelectedIndex).Ratio
        If r < 0 Then r = _rendered.Width / CDbl(_rendered.Height)
        If r > 0 AndAlso _cropSwapped Then r = 1 / r
        Return r
    End Function

    ''' <summary>套用比例：有臉時依臉構圖，否則置中最大。</summary>
    Private Sub ApplyRatioCrop()
        If Not _cropMode OrElse _rendered Is Nothing Then Return
        Dim ratio = SelectedCropRatio()
        _canvas.AspectRatio = ratio
        If ratio > 0 Then
            _canvas.Crop = If(_canvas.FaceMarks.Count > 0,
                              SmartCrop.Suggest(_canvas.FaceMarks, ratio, _rendered.Width, _rendered.Height),
                              CropRect.CenteredForAspect(ratio, _rendered.Width, _rendered.Height))
        End If
        SyncCropSize()
        _canvas.Focus() ' 讓 Enter/Esc 不被下拉選單吃掉
    End Sub

    ''' <summary>直橫互換：固定比例時換成倒數；自由比例時把目前的框轉 90 度（以中心為準）。</summary>
    Private Sub SwapCropOrientation()
        If Not _cropMode OrElse _rendered Is Nothing Then Return
        If SelectedCropRatio() > 0 Then
            _cropSwapped = Not _cropSwapped
            ApplyRatioCrop()
            Return
        End If
        Dim c = _canvas.Crop
        Dim iw = CDbl(_rendered.Width), ih = CDbl(_rendered.Height)
        Dim w = c.Height * ih / iw, h = c.Width * iw / ih
        Dim scale = Math.Min(1, Math.Min(1 / w, 1 / h))
        w *= scale : h *= scale
        Dim cx = c.X + c.Width / 2, cy = c.Y + c.Height / 2
        _canvas.Crop = New CropRect(cx - w / 2, cy - h / 2, w, h).Normalized()
        SyncCropSize()
        _canvas.Focus()
    End Sub

    ''' <summary>依臉的位置建議裁切框；自由/原始比例時稍微收緊（80%），否則用最大尺寸。</summary>
    Private Sub ApplySmartCrop()
        If Not _cropMode OrElse _rendered Is Nothing Then Return
        Dim ratio = SelectedCropRatio()
        Dim factor = If(_cropRatio.SelectedIndex <= 1, 0.8, 1.0)
        _canvas.Crop = SmartCrop.Suggest(_canvas.FaceMarks, ratio, _rendered.Width, _rendered.Height, factor)
        SyncCropSize()
        SetStatusMessage(If(_canvas.FaceMarks.Count > 0, "已依人臉位置構圖：眼睛在上方三分線。", "沒有偵測到臉，改用置中構圖。"))
        _canvas.Focus()
    End Sub

    ''' <summary>輸入寬或高（原圖像素）：以目前框的中心為準；固定比例時另一邊跟著算，超出照片就等比例縮小。</summary>
    Private Sub OnCropSizeTyped(isWidth As Boolean)
        If _cropSyncing OrElse Not _cropMode Then Return
        Dim full = FullCropSize()
        If full.IsEmpty Then Return
        Dim w = CDbl(_cropWidth.Value), h = CDbl(_cropHeight.Value)
        Dim ratio = SelectedCropRatio()
        If ratio > 0 Then
            If isWidth Then h = w / ratio Else w = h * ratio
            Dim k = Math.Min(1, Math.Min(full.Width / w, full.Height / h))
            w *= k : h *= k
        Else
            w = Math.Min(w, full.Width) : h = Math.Min(h, full.Height)
        End If
        Dim c = _canvas.Crop
        Dim nw = w / full.Width, nh = h / full.Height
        Dim cx = c.X + c.Width / 2, cy = c.Y + c.Height / 2
        _canvas.Crop = New CropRect(cx - nw / 2, cy - nh / 2, nw, nh).Normalized()
        SyncCropSize()
    End Sub

    ''' <summary>畫布上的裁切框改變後，更新寬高欄位。</summary>
    Private Sub SyncCropSize()
        If Not _cropMode Then Return
        Dim full = FullCropSize()
        If full.IsEmpty Then Return
        Dim c = _canvas.Crop
        _cropSyncing = True
        Try
            _cropWidth.Maximum = full.Width
            _cropHeight.Maximum = full.Height
            _cropWidth.Value = Math.Max(1, Math.Min(full.Width, CInt(Math.Round(c.Width * full.Width))))
            _cropHeight.Value = Math.Max(1, Math.Min(full.Height, CInt(Math.Round(c.Height * full.Height))))
        Finally
            _cropSyncing = False
        End Try
    End Sub

    '=====================================================================
    ' 拉直、旋轉、翻轉
    '=====================================================================

    Private Sub SyncCropBar()
        _cropSyncing = True
        Try
            Dim v = CInt(Math.Round(Math.Max(-45, Math.Min(45, _recipe.Straighten)) * 2))
            If _cropStraighten.Value <> v Then _cropStraighten.Value = v
            _cropStraightenLabel.Text = (v / 2.0).ToString("+0.0;-0.0;0.0") & "°"
        Finally
            _cropSyncing = False
        End Try
    End Sub

    Private Sub OnCropStraighten()
        _cropStraightenLabel.Text = (_cropStraighten.Value / 2.0).ToString("+0.0;-0.0;0.0") & "°"
        If _cropSyncing OrElse Not _cropMode Then Return
        Dim deg = _cropStraighten.Value / 2.0
        ApplyChange(Sub(r) r.Straighten = deg, "crop-straighten")
    End Sub

    Private Sub CropRotate(right As Boolean)
        If Not _cropMode Then Return
        ApplyChange(Sub(r)
                        If right Then r.RotateRight() Else r.RotateLeft()
                    End Sub)
        AfterCropGeometry(resetFrame:=True)
    End Sub

    ''' <summary>翻轉照片，裁切框跟著鏡射（框住的仍是同一塊）。</summary>
    Private Sub CropFlip(horizontal As Boolean)
        If Not _cropMode Then Return
        Dim c = _canvas.Crop
        ApplyChange(Sub(r)
                        If horizontal Then r.ToggleFlipHorizontal() Else r.ToggleFlipVertical()
                    End Sub)
        _canvas.Crop = If(horizontal, New CropRect(1 - c.X - c.Width, c.Y, c.Width, c.Height), New CropRect(c.X, 1 - c.Y - c.Height, c.Width, c.Height))
        AfterCropGeometry(resetFrame:=False)
    End Sub

    ''' <summary>裁切中改了幾何：重算畫面；轉向後照片寬高互換，裁切框重設（有比例時依比例置中）。</summary>
    Private Sub AfterCropGeometry(resetFrame As Boolean)
        RenderNow()
        _canvas.CropPixelSize = FullCropSize()
        If resetFrame Then
            _canvas.Crop = New CropRect()
            If SelectedCropRatio() > 0 Then ApplyRatioCrop()
        End If
        SyncCropBar()
        SyncCropSize()
        _canvas.Focus()
    End Sub
End Class
