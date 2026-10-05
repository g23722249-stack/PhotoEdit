Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports PhotoEdit

''' <summary>
''' 照片編輯器主視窗。預覽用長邊 1600 的縮圖即時算圖；放大檢視超過縮圖解析度時，在背景算全尺寸預覽；
''' 匯出時以原圖全尺寸計算。編輯參數存在照片旁的 .pedit.json，原始照片永不覆寫。
''' 人臉在開檔後於背景偵測（需要 Models\face_detection_yunet_2023mar.onnx），供人像修飾與智慧構圖使用。
''' </summary>
Partial Friend Class frmEditor
    Inherits Aqua.AquaForm

    Private Const AppName As String = "PhotoEdit"
    Private Const PreviewMaxSize As Integer = 1600
    ''' <summary>右側面板的最小（也是設計）寬度；實際寬度可拖曳分隔線調整並會記住。</summary>
    Private Const SideWidth As Integer = 340
    Private ReadOnly _splitter As New Splitter()

    ''' <summary>「複製調整」的內容，切換照片後仍保留，方便套用到下一張。</summary>
    Private Shared _copiedAdjustments As EditRecipe
    Private Shared ReadOnly Detector As New Lazy(Of FaceDetector)(Function() New FaceDetector())

    Private ReadOnly _canvas As New PreviewCanvas()
    Private ReadOnly _histogramView As New HistogramView()
    Private ReadOnly _sidePanel As New Panel()
    Private ReadOnly _presetStrip As New PresetStrip()
    Private _showHelpItem As Aqua.MenuItem
    Private ReadOnly _cropPanel As New Panel()
    Private ReadOnly _cropRatio As New ComboBox()
    Private ReadOnly _statusLabel As New StatusLine()
    Private ReadOnly _portraitHeading As New Label()
    Private ReadOnly _renderTimer As New Timer() With {.Interval = 15}
    Private ReadOnly _hiResTimer As New Timer() With {.Interval = 300}
    Private ReadOnly _rows As New List(Of SliderRow)()
    Private ReadOnly _history As New EditHistory()
    Private ReadOnly _startupPath As String
    ''' <summary>背景算全尺寸預覽、匯出、換照片時都要先拿到這把鎖，才能使用或釋放 _photo.Image。</summary>
    Private ReadOnly _sourceLock As New Object()

    Private _photo As PhotoFile
    Private _previewBase As Bitmap
    Private _rendered As Bitmap
    Private _recipe As New EditRecipe()
    Private _savedRecipe As New EditRecipe()
    Private _syncing As Boolean
    Private _showingOriginal As Boolean
    Private _cropMode As Boolean

    ' 人臉：Nothing 表示偵測中或無法偵測。
    Private _faces As IReadOnlyList(Of FaceRegion)
    Private _faceState As String = ""
    Private _faceGeneration As Integer
    Private _retouched As Bitmap
    Private _retouchKey As String

    ' 全尺寸預覽（放大檢視用）。
    Private _hiRes As Bitmap
    Private _hiResKey As String
    Private _hiResGeneration As Integer
    Private _hiResBusy As Boolean

    Private _thumbKey As String

    Private Class SliderRow
        Public Key As String
        Public Caption As String
        Public Minimum As Integer
        Public Maximum As Integer
        Public Format As Func(Of Integer, String)
        Public GetValue As Func(Of EditRecipe, Integer)
        Public SetValue As Action(Of EditRecipe, Integer)
        Public ValueLabel As Label
        Public Slider As Aqua.Slider
    End Class

    Private Shared ReadOnly PortraitKeys As String() = {"skin", "facebright", "eyebright"}

    Public Sub New(Optional startupPath As String = Nothing)
        _startupPath = startupPath
        Text = AppName
        Size = New Size(1320, 900)
        MinimumSize = New Size(860, 620)
        StartPosition = FormStartPosition.CenterScreen
        WindowBorderStyle = Aqua.FormBorderStyle.Sizable
        KeyPreview = True
        AllowDrop = True

        BuildMenu()
        Dim menuHeight = TextRenderer.MeasureText("Ag", MenuFont).Height + 6
        Padding = New Padding(4, 23 + menuHeight + 2, 4, 16) ' 標題列 23 + 選單列；底部留給縮放把手

        BuildSidePanel()
        SetupSplitter()
        BuildCropPanel()
        BuildDrawChrome()

        _statusLabel.Dock = DockStyle.Bottom

        _presetStrip.Dock = DockStyle.Bottom
        _presetStrip.AttachHelp(_help)
        _help.Active = _appSettings.ShowHelp
        AddHandler FormClosed, Sub() _help.Dispose()
        _canvas.Dock = DockStyle.Fill
        _canvas.AllowDrop = True

        ' 加入順序決定停靠順序（最後加入的最先停靠）：狀態列最底、右側面板全高，濾鏡列與裁切列只在畫布下方。
        Controls.Add(_canvas)
        Controls.Add(_drawBar)
        Controls.Add(_drawStrip)
        Controls.Add(_presetStrip)
        Controls.Add(_cropPanel)
        Controls.Add(_splitter)
        Controls.Add(_sidePanel)
        Controls.Add(_statusLabel)

        AddHandler MenuSelected, Sub(s, item) RunCommand(item.Name)
        ' 剪貼簿沒有圖片時，「貼成新影像」停用。
        AddHandler MenuOpen, Sub() _pasteImageItem.Enabled = ClipboardHasImage()
        AddHandler _renderTimer.Tick, Sub() RenderNow()
        AddHandler _hiResTimer.Tick, Sub() StartHiResRender()
        AddHandler _canvas.ZoomChanged, Sub() OnZoomChanged()
        AddHandler _canvas.StrokeCompleted, AddressOf OnCanvasStroke
        AddHandler _canvas.GradientDefined, AddressOf OnGradientDefined
        AddHandler _canvas.OverlayPressed, AddressOf OnOverlayPressed
        AddHandler _canvas.OverlayDragged, AddressOf OnOverlayDragged
        AddHandler _canvas.OverlayScaled, AddressOf OnOverlayScaled
        AddHandler _canvas.OverlayRotated, AddressOf OnOverlayRotated
        AddHandler _canvas.OverlayWheel, AddressOf OnOverlayWheel
        AddHandler _canvas.Resize, Sub() UpdateStatus()
        AddHandler _canvas.DragEnter, AddressOf OnFileDragEnter
        AddHandler _canvas.DragDrop, AddressOf OnFileDragDrop
        AddHandler _canvas.DragOver, AddressOf OnFileDragEnter
        AddHandler _presetStrip.PresetClicked, Sub(p) If _photo IsNot Nothing Then ApplyChange(Sub(r) p.ApplyTo(r))
        AddHandler DragEnter, AddressOf OnFileDragEnter
        AddHandler DragDrop, AddressOf OnFileDragDrop

        SyncSliders()
        UpdatePortraitControls()
        UpdateCreativeControls()
        UpdateTitle()
        UpdateStatus()

        Me.WindowState = FormWindowState.Maximized
    End Sub

    '=====================================================================
    ' 版面
    '=====================================================================

    Private Sub BuildMenu()
        Dim root As New Aqua.MenuItem()

        Dim fileMenu = root.AddItem(New Aqua.MenuItem("檔案"))
        fileMenu.AddItem(Item("open", "開啟照片… (Ctrl+O)"))
        fileMenu.AddItem(Item("save", "儲存編輯 (Ctrl+S)"))
        fileMenu.AddItem(Item("export", "匯出 JPG… (Ctrl+E)"))
        fileMenu.AddItem(Item("collage", "拼貼…"))
        fileMenu.AddItem(New Aqua.MenuItem("-"))
        fileMenu.AddItem(Item("revert", "回復原圖"))
        fileMenu.AddItem(New Aqua.MenuItem("-"))
        fileMenu.AddItem(Item("exit", "結束"))

        Dim editMenu = root.AddItem(New Aqua.MenuItem("編輯"))
        editMenu.AddItem(Item("undo", "復原 (Ctrl+Z)"))
        editMenu.AddItem(Item("redo", "重做 (Ctrl+Y)"))
        editMenu.AddItem(New Aqua.MenuItem("-"))
        _pasteImageItem = editMenu.AddItem(Item("pasteimage", "貼成新影像 (Ctrl+V)"))
        editMenu.AddItem(New Aqua.MenuItem("-"))
        editMenu.AddItem(Item("copyadj", "複製調整 (Ctrl+Shift+C)"))
        editMenu.AddItem(Item("pasteadj", "貼上調整 (Ctrl+Shift+V)"))
        editMenu.AddItem(Item("resetadj", "重設調整"))

        Dim imageMenu = root.AddItem(New Aqua.MenuItem("影像"))
        imageMenu.AddItem(Item("rotl", "向左轉 (Ctrl+L)"))
        imageMenu.AddItem(Item("rotr", "向右轉 (Ctrl+R)"))
        imageMenu.AddItem(Item("fliph", "水平翻轉"))
        imageMenu.AddItem(Item("flipv", "垂直翻轉"))
        imageMenu.AddItem(New Aqua.MenuItem("-"))
        imageMenu.AddItem(Item("crop", "裁切… (Ctrl+K)"))
        imageMenu.AddItem(Item("smartcrop", "智慧構圖"))
        imageMenu.AddItem(Item("autostraighten", "自動拉直"))
        imageMenu.AddItem(Item("heal", "修補筆刷 (H)"))
        imageMenu.AddItem(New Aqua.MenuItem("-"))
        imageMenu.AddItem(Item("auto", "自動增強"))
        imageMenu.AddItem(Item("autowb", "自動白平衡"))

        Dim viewMenu = root.AddItem(New Aqua.MenuItem("檢視"))
        viewMenu.AddItem(Item("fit", "符合視窗 (Ctrl+0)"))
        viewMenu.AddItem(Item("actual", "100% (Ctrl+1)"))
        viewMenu.AddItem(Item("zoomin", "放大 (Ctrl++)"))
        viewMenu.AddItem(Item("zoomout", "縮小 (Ctrl+-)"))

        Dim helpMenu = root.AddItem(New Aqua.MenuItem("說明"))
        _showHelpItem = helpMenu.AddItem(Item("togglehelp", "顯示使用說明（滑鼠停在按鈕上）"))
        _showHelpItem.Checked = _appSettings.ShowHelp
        helpMenu.AddItem(New Aqua.MenuItem("-"))
        helpMenu.AddItem(Item("about", "關於 PhotoEdit"))

        AddMenu(root)
    End Sub

    Private Shared Function Item(name As String, text As String) As Aqua.MenuItem
        Return New Aqua.MenuItem(text) With {.Name = name}
    End Function

    '=====================================================================
    ' 指令
    '=====================================================================

    Private Sub RunCommand(name As String)
        Select Case name
            Case "open" : OpenWithDialog()
            Case "collage" : OpenCollage()
            Case "pasteimage" : PasteAsNewImage()
            Case "exit" : Close()
            Case "togglehelp"
                _appSettings.ShowHelp = Not _appSettings.ShowHelp
                _appSettings.Save()
                _help.Active = _appSettings.ShowHelp
                _showHelpItem.Checked = _appSettings.ShowHelp
                SetStatusMessage(If(_appSettings.ShowHelp, "已開啟使用說明：滑鼠停在按鈕或滑桿上就會顯示。", "已關閉使用說明。"))
            Case "about"
                MessageBox.Show(Me, "PhotoEdit 0.2 — 非破壞性照片編輯器" & vbCrLf & vbCrLf &
                                "編輯參數存在「照片檔名.pedit.json」，原始照片不會被修改。" & vbCrLf &
                                "要得到編輯後的圖檔，請用「檔案 → 匯出 JPG」。" & vbCrLf & vbCrLf &
                                "人臉偵測：YuNet（OpenCV Zoo，Apache-2.0）",
                                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Select
        If _photo Is Nothing Then Return

        Select Case name
            Case "save" : SaveRecipe()
            Case "export" : ExportWithDialog()
            Case "revert"
                ExitCropMode(apply:=False)
                ReplaceRecipe(New EditRecipe())
            Case "undo"
                If UndoInCropMode() Then Return
                ExitCropMode(apply:=False)
                If _history.CanUndo Then _recipe = _history.Undo(_recipe) : OnRecipeChanged()
            Case "redo"
                ExitCropMode(apply:=False)
                If _history.CanRedo Then _recipe = _history.Redo(_recipe) : OnRecipeChanged()
            Case "copyadj"
                _copiedAdjustments = _recipe.Clone()
                SetStatusMessage("已複製調整，可到其他照片「貼上調整」。")
            Case "pasteadj"
                If _copiedAdjustments IsNot Nothing Then ApplyChange(Sub(r) r.CopyAdjustmentsFrom(_copiedAdjustments))
            Case "resetadj" : ApplyChange(Sub(r) r.ResetAdjustments())
            Case "rotl"
                If _cropMode Then CropRotate(False) : Return
                ApplyChange(Sub(r) r.RotateLeft())
            Case "rotr"
                If _cropMode Then CropRotate(True) : Return
                ApplyChange(Sub(r) r.RotateRight())
            Case "fliph"
                If _cropMode Then CropFlip(True) : Return
                ApplyChange(Sub(r) r.ToggleFlipHorizontal())
            Case "flipv"
                If _cropMode Then CropFlip(False) : Return
                ApplyChange(Sub(r) r.ToggleFlipVertical())
            Case "crop"
                If _cropMode Then ExitCropMode(apply:=True) Else EnterCropMode()
            Case "smartcrop"
                EnterCropMode()
                ApplySmartCrop()
            Case "auto"
                Dim suggested = AutoAdjust.Enhance(_previewBase, _recipe)
                ApplyChange(Sub(r)
                                r.Exposure = suggested.Exposure
                                r.Contrast = suggested.Contrast
                                r.Highlights = suggested.Highlights
                                r.Shadows = suggested.Shadows
                                r.Temperature = suggested.Temperature
                                r.Tint = suggested.Tint
                                r.Saturation = suggested.Saturation
                            End Sub)
            Case "autowb"
                Dim suggested = AutoAdjust.WhiteBalance(_previewBase, _recipe)
                ApplyChange(Sub(r)
                                r.Temperature = suggested.Temperature
                                r.Tint = suggested.Tint
                            End Sub)
            Case "fit" : _canvas.ZoomToFit()
            Case "actual" : _canvas.SetZoom(1.0)
            Case "zoomin" : _canvas.ZoomIn()
            Case "zoomout" : _canvas.ZoomOut()
            Case "heal"
                _tabs.SelectedIndex = 3
                _healToggle.Checked = Not _healToggle.Checked
            Case "clearspots" : ApplyChange(Sub(r) r.Spots = Nothing)
            Case "autostraighten" : RunAutoStraighten()
        End Select
    End Sub

    ''' <summary>開拼貼視窗；目前照片（含尚未儲存的編輯）會先放進第一格。</summary>
    Private Sub OpenCollage()
        Dim initial As New List(Of (Path As String, Recipe As EditRecipe))()
        If _photo IsNot Nothing Then initial.Add((_photo.Path, _recipe.Clone()))
        Dim f As New frmCollage(initial)
        f.Show(Me)
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        ' 在文字框打字時，H、[、]、Enter、Delete 等單鍵要留給文字框，不當快捷鍵。
        If (keyData And (Keys.Control Or Keys.Alt)) = Keys.None AndAlso keyData <> Keys.Escape AndAlso
           TypeOf ActiveControl Is TextBoxBase Then
            Return MyBase.ProcessCmdKey(msg, keyData)
        End If
        ' 文字框裡的 Ctrl+V／C／X／A 是編輯文字。
        If TypeOf ActiveControl Is TextBoxBase AndAlso
           {Keys.Control Or Keys.V, Keys.Control Or Keys.C, Keys.Control Or Keys.X, Keys.Control Or Keys.A}.Contains(keyData) Then
            Return MyBase.ProcessCmdKey(msg, keyData)
        End If
        If HandleDrawKey(keyData) Then Return True
        Dim cmd As String = Nothing
        Select Case keyData
            Case Keys.Control Or Keys.O : cmd = "open"
            Case Keys.Control Or Keys.S : cmd = "save"
            Case Keys.Control Or Keys.E : cmd = "export"
            Case Keys.Control Or Keys.Z : cmd = "undo"
            Case Keys.Control Or Keys.Y, Keys.Control Or Keys.Shift Or Keys.Z : cmd = "redo"
            Case Keys.Control Or Keys.Shift Or Keys.C : cmd = "copyadj"
            Case Keys.Control Or Keys.Shift Or Keys.V : cmd = "pasteadj"
            Case Keys.Control Or Keys.V : cmd = "pasteimage"
            Case Keys.Control Or Keys.L : cmd = "rotl"
            Case Keys.Control Or Keys.R : cmd = "rotr"
            Case Keys.Control Or Keys.K : cmd = "crop"
            Case Keys.Control Or Keys.D0, Keys.Control Or Keys.NumPad0 : cmd = "fit"
            Case Keys.Control Or Keys.D1, Keys.Control Or Keys.NumPad1 : cmd = "actual"
            Case Keys.Control Or Keys.Oemplus, Keys.Control Or Keys.Add : cmd = "zoomin"
            Case Keys.Control Or Keys.OemMinus, Keys.Control Or Keys.Subtract : cmd = "zoomout"
            Case Keys.W
                If _tabs.SelectedIndex = TabCutout AndAlso _photo IsNot Nothing Then _wandToggle.Checked = Not _wandToggle.Checked : Return True
            Case Keys.H : cmd = "heal"
            Case Keys.Delete
                If _canvas.Tool = PreviewCanvas.CanvasTool.Overlay AndAlso Not _overlayText.Focused AndAlso SelOverlay(_recipe) IsNot Nothing Then DeleteOverlay() : Return True
            Case Keys.OemOpenBrackets, Keys.OemCloseBrackets
                ' 目前使用中的筆刷（修補、局部、去背修正）調整大小。
                Dim brush = If(_healToggle.Checked, _brushSize,
                               If(_canvas.Tool = PreviewCanvas.CanvasTool.LocalBrush, _localBrushSize,
                                  If(_canvas.Tool = PreviewCanvas.CanvasTool.MaskBrush, _maskBrushSize, Nothing)))
                If brush IsNot Nothing AndAlso brush.Enabled Then
                    Dim stepSize = If(keyData = Keys.OemOpenBrackets, -4, 4)
                    brush.Value = Math.Max(brush.Minimum, Math.Min(brush.Maximum, brush.Value + stepSize))
                    Return True
                End If
            Case Keys.Enter
                ' 在尺寸欄位按 Enter：只確認數值，不套用裁切。
                If _cropMode AndAlso TypeOf ActiveControl Is NumericUpDown Then _canvas.Focus() : Return True
                If _cropMode Then ExitCropMode(apply:=True) : Return True
            Case Keys.Escape
                If _cropMode Then ExitCropMode(apply:=False) : Return True
                If _healToggle.Checked Then _healToggle.Checked = False : Return True
        End Select
        If cmd Is Nothing Then Return MyBase.ProcessCmdKey(msg, keyData)
        RunCommand(cmd)
        Return True
    End Function

    '=====================================================================
    ' 配方變更
    '=====================================================================

    Private Sub ApplyChange(mutate As Action(Of EditRecipe), Optional groupKey As String = Nothing)
        Dim before = _recipe.Clone()
        mutate(_recipe)
        If _recipe.Equals(before) Then Return
        _history.Record(before, groupKey)
        OnRecipeChanged()
    End Sub

    Private Sub ReplaceRecipe(recipe As EditRecipe)
        If _recipe.Equals(recipe) Then Return
        _history.Record(_recipe)
        _recipe = recipe.Clone()
        OnRecipeChanged()
    End Sub

    Private Sub OnRecipeChanged()
        SyncSliders()
        UpdateTitle()
        _presetStrip.UpdateSelection(_recipe)
        UpdateCreativeControls()
        RequestRender()
    End Sub

    Private Sub OnSliderChanged(row As SliderRow)
        row.ValueLabel.Text = row.Format(row.Slider.Value)
        If _syncing OrElse _photo Is Nothing Then Return
        ApplyChange(Sub(r) row.SetValue(r, row.Slider.Value), "slider:" & row.Key)
    End Sub

    Private Sub SyncSliders()
        _syncing = True
        Try
            For Each row In _rows
                Dim v = Math.Max(row.Minimum, Math.Min(row.Maximum, row.GetValue(_recipe)))
                If row.Slider.Value <> v Then row.Slider.Value = v
                row.ValueLabel.Text = row.Format(v)
            Next
        Finally
            _syncing = False
        End Try
    End Sub

    '=====================================================================
    ' 算圖與顯示
    '=====================================================================

    Private Sub RequestRender()
        _renderTimer.Stop()
        _renderTimer.Start()
    End Sub

    Private Sub RenderNow()
        _renderTimer.Stop()
        If _previewBase Is Nothing Then Return

        Dim source = SourceBase()
        Dim bmp As Bitmap
        If _cropMode Then
            ' 裁切模式看整張（含拉直與調色），裁切框畫在上面。
            Dim uncropped = _recipe.Clone()
            uncropped.Crop = Nothing
            bmp = ImagePipeline.RenderGeometry(source, uncropped, 0, applyCrop:=False)
            ImagePipeline.ApplyTone(bmp, uncropped)
            ImagePipeline.ApplyEffects(bmp, uncropped)
            _canvas.FaceMarks = MappedFaces()
            _framePhotoSize = bmp.Size
            _frameMargins = (0, 0, 0, 0)
        Else
            ' 先算不含邊框的照片，記下大小與邊框寬度，畫布座標才能換算回照片座標。
            Dim noFrame = DisplayRecipe().Clone()
            noFrame.Frame = PhotoFrameStyle.None
            bmp = ImagePipeline.Render(source, noFrame, faces:=_faces)
            _framePhotoSize = bmp.Size
            _frameMargins = Creative.FrameMargins(_recipe, bmp.Width, bmp.Height)
            Dim framed = Creative.ApplyFrame(bmp, _recipe)
            If framed IsNot Nothing Then
                bmp.Dispose()
                bmp = framed
            End If
        End If

        Dim old = _rendered
        _rendered = bmp
        If _hiRes IsNot Nothing AndAlso _hiResKey <> CurrentKey() Then
            _hiRes.Dispose()
            _hiRes = Nothing
        End If
        ShowCurrentImage()
        old?.Dispose()
        UpdateToolOverlays()
        _histogramView.Histogram = Histogram.Compute(_rendered)
        UpdatePresetThumbnails()
        ScheduleHiRes()
        UpdateStatus()
    End Sub

    ''' <summary>畫布顯示：原圖對照 &gt; 全尺寸預覽（若與目前配方相符）&gt; 一般預覽。</summary>
    Private Sub ShowCurrentImage()
        If _previewBase Is Nothing Then Return
        Dim previewScale = _photo.Image.Width / CDbl(_previewBase.Width)
        If _showingOriginal Then
            _canvas.ImageScale = previewScale
            _canvas.Image = _previewBase
        ElseIf _hiRes IsNot Nothing AndAlso Not _cropMode Then
            _canvas.ImageScale = 1
            _canvas.Image = _hiRes
        Else
            _canvas.ImageScale = previewScale
            _canvas.Image = _rendered
        End If
    End Sub

    Private Sub ShowOriginal(show As Boolean)
        If _previewBase Is Nothing OrElse _showingOriginal = show Then Return
        _showingOriginal = show
        ShowCurrentImage()
        If show Then SetStatusMessage("原圖") Else UpdateStatus()
    End Sub

    Private Function CurrentKey() As String
        Return RecipeStore.ToJson(DisplayRecipe())
    End Function

    Private Sub UpdateTitle()
        If _photo Is Nothing Then
            Text = AppName
        Else
            Text = $"{AppName} — {Path.GetFileName(_photo.Path)}{If(IsDirty, " *", "")}"
        End If
    End Sub

    Private ReadOnly Property IsDirty As Boolean
        Get
            Return _photo IsNot Nothing AndAlso Not _recipe.Equals(_savedRecipe)
        End Get
    End Property

    Private Sub UpdateStatus()
        If _photo Is Nothing OrElse _rendered Is Nothing Then
            _statusLabel.Text = "開啟照片：Ctrl+O 或拖曳檔案到視窗"
            Return
        End If
        Dim scale = _photo.Image.Width / CDbl(_previewBase.Width)
        Dim outW = CInt(Math.Round(_rendered.Width * scale))
        Dim outH = CInt(Math.Round(_rendered.Height * scale))
        Dim zoom = If(_canvas.IsFit, $"符合視窗 {_canvas.EffectiveZoom():P0}", $"{_canvas.EffectiveZoom():P0}")
        If _hiResBusy Then zoom &= "（產生高解析度中…）"
        Dim state = If(_recipe.IsIdentity, "未編輯", "已編輯")
        Dim faces = If(_faceState = "", "", "　｜　" & _faceState)
        _statusLabel.Text = $"{_photo.Image.Width} × {_photo.Image.Height}  →  輸出 {outW} × {outH}　｜　顯示 {zoom}　｜　{state}{faces}"
    End Sub

    Private Sub SetStatusMessage(message As String)
        _statusLabel.Text = message
    End Sub

    '=====================================================================
    ' 濾鏡縮圖
    '=====================================================================

    ''' <summary>幾何或人像改變時，重算各預設集的縮圖（約 150px，十幾張也很快）。</summary>
    Private Sub UpdatePresetThumbnails()
        Dim geometry = _recipe.GeometryOnly()
        geometry.Overlays = Nothing ' 縮圖只看濾鏡風格，不畫文字貼圖
        Dim key = RecipeStore.ToJson(geometry) & "|" & _retouchKey
        If key = _thumbKey Then Return
        _thumbKey = key

        Dim thumbs As New List(Of Bitmap)()
        Using baseThumb = ImagePipeline.Render(SourceBase(), geometry, PresetStrip.ThumbSize)
            For Each p In Preset.BuiltIn
                Dim t = DirectCast(baseThumb.Clone(), Bitmap)
                Dim look As New EditRecipe()
                p.ApplyTo(look)
                ImagePipeline.ApplyTone(t, look)
                ImagePipeline.ApplyEffects(t, look)
                thumbs.Add(t)
            Next
        End Using
        _presetStrip.SetThumbnails(thumbs)
        _presetStrip.UpdateSelection(_recipe)
    End Sub

    '=====================================================================
    ' 人像
    '=====================================================================

    ''' <summary>
    ''' 預覽算圖的來源：有修補、降噪或人像參數時，用 SourceFix 處理過的縮圖（依參數快取），否則用原縮圖。
    ''' </summary>
    Private Function SourceBase() As Bitmap
        Dim key As String = Nothing
        Dim display = DisplayRecipe()
        If display.HasSourceFix Then
            key = String.Join("|", System.Text.Json.JsonSerializer.Serialize(display.Spots), display.Denoise, display.ColorNoise,
                              display.SkinSmoothing, display.FaceBrighten, display.EyeBrighten, If(_faces?.Count, -1),
                              System.Text.Json.JsonSerializer.Serialize(display.Cutout), _aiMaskVersion)
        End If
        If key <> _retouchKey Then
            _retouched?.Dispose()
            _retouched = If(key Is Nothing, Nothing,
                            SourceFix.Apply(_previewBase, If(_faces, Array.Empty(Of FaceRegion)()), display, CurrentAiMask(display)))
            _retouchKey = key
        End If
        Return If(_retouched, _previewBase)
    End Function

    Private Sub StartFaceDetection()
        _faces = Nothing
        _faceGeneration += 1
        If Not FaceDetector.ModelAvailable() Then
            _faceState = "人像功能停用：缺少 Models\" & FaceDetector.ModelFile
            UpdatePortraitControls()
            Return
        End If
        Dim generation = _faceGeneration
        Dim copy = DirectCast(_previewBase.Clone(), Bitmap)
        _faceState = "偵測人臉中…"
        UpdatePortraitControls()
        Task.Run(Function() Detector.Value.Detect(copy)).ContinueWith(
            Sub(t)
                copy.Dispose()
                Dim faces As List(Of FaceRegion) = If(t.Status = TaskStatus.RanToCompletion, t.Result, Nothing)
                Dim err = t.Exception?.GetBaseException().Message
                PostToUi(Sub()
                             If generation <> _faceGeneration Then Return
                             _faces = If(faces, New List(Of FaceRegion)())
                             _faceState = If(err IsNot Nothing, "人臉偵測失敗：" & err,
                                          If(_faces.Count = 0, "沒有偵測到臉", $"找到 {_faces.Count} 張臉"))
                             UpdatePortraitControls()
                             If _recipe.HasPortrait OrElse _cropMode Then RequestRender()
                             ' 有人臉的照片，去背預設用人像模型（一般模型容易把旁邊顯眼的物體也當主體）。
                             If _recipe.Cutout Is Nothing Then
                                 _modelHuman.Checked = _faces.Count > 0
                                 _modelGeneral.Checked = _faces.Count = 0
                             End If
                             UpdateStatus()
                         End Sub)
            End Sub)
    End Sub

    Private Sub UpdatePortraitControls()
        Dim usable = _faces IsNot Nothing AndAlso _faces.Count > 0
        _portraitHeading.Text = "人像" & If(_faceState = "" OrElse _photo Is Nothing, "", "（" & _faceState & "）")
        For Each row In _rows.Where(Function(r) PortraitKeys.Contains(r.Key))
            row.Slider.Enabled = usable
        Next
    End Sub

    ''' <summary>臉框換算到目前幾何（未裁切）的 0..1 座標。</summary>
    Private Function MappedFaces() As List(Of RectangleF)
        If _faces Is Nothing OrElse _previewBase Is Nothing Then Return New List(Of RectangleF)()
        Dim g = _recipe.Clone()
        g.Crop = Nothing
        Return _faces.Select(Function(f) GeometryMapper.MapBox(f.Box, g, _previewBase.Width, _previewBase.Height)).ToList()
    End Function

    ''' <summary>匯出與全尺寸預覽用的原圖處理（修補、降噪、人像）。</summary>
    Private Function SourcePrepare(recipe As EditRecipe) As Func(Of Bitmap, Bitmap)
        If Not recipe.HasSourceFix Then Return Nothing
        Dim faces As IReadOnlyList(Of FaceRegion) = If(_faces, Array.Empty(Of FaceRegion)())
        Dim mask = CurrentAiMask(recipe)
        Return Function(b) SourceFix.Apply(b, faces, recipe, mask)
    End Function

    '=====================================================================
    ' 放大檢視：全尺寸預覽
    '=====================================================================

    Private Sub OnZoomChanged()
        ScheduleHiRes()
        UpdateStatus()
    End Sub

    ''' <summary>放大到超過預覽縮圖的解析度時，停止操作 0.3 秒後在背景算全尺寸。</summary>
    Private Sub ScheduleHiRes()
        _hiResTimer.Stop()
        If _photo Is Nothing OrElse _cropMode OrElse _canvas.IsFit Then Return
        Dim previewResolution = _previewBase.Width / CDbl(_photo.Image.Width)
        If _canvas.EffectiveZoom() <= previewResolution * 1.05 Then Return
        If _hiRes IsNot Nothing AndAlso _hiResKey = CurrentKey() Then Return
        _hiResTimer.Start()
    End Sub

    Private Sub StartHiResRender()
        _hiResTimer.Stop()
        If _photo Is Nothing Then Return
        _hiResGeneration += 1
        Dim generation = _hiResGeneration
        Dim recipe = DisplayRecipe().Clone()
        Dim faces = _faces
        Dim key = CurrentKey()
        Dim prepare = SourcePrepare(recipe)
        Dim photo = _photo
        _hiResBusy = True
        UpdateStatus()
        Task.Run(Function()
                     SyncLock _sourceLock
                         If photo IsNot _photo Then Return Nothing ' 已換照片
                         Return ImagePipeline.Render(photo.Image, recipe, prepare:=prepare, faces:=faces)
                     End SyncLock
                 End Function).ContinueWith(
            Sub(t)
                Dim bmp = If(t.Status = TaskStatus.RanToCompletion, t.Result, Nothing)
                If Not PostToUi(Sub()
                                    If generation <> _hiResGeneration OrElse key <> CurrentKey() OrElse photo IsNot _photo Then
                                        bmp?.Dispose()
                                    Else
                                        _hiRes?.Dispose()
                                        _hiRes = bmp
                                        _hiResKey = key
                                        ShowCurrentImage()
                                    End If
                                    If generation = _hiResGeneration Then _hiResBusy = False
                                    UpdateStatus()
                                End Sub) Then bmp?.Dispose()
            End Sub)
    End Sub

    ''' <summary>從背景執行緒回到 UI；視窗已關閉時回傳 False。</summary>
    Private Function PostToUi(action As Action) As Boolean
        Try
            If IsDisposed OrElse Not IsHandleCreated Then Return False
            BeginInvoke(action)
            Return True
        Catch ex As InvalidOperationException
            Return False
        End Try
    End Function

    '=====================================================================
    ' 檔案
    '=====================================================================

    Private Sub OpenWithDialog()
        Using dlg As New OpenFileDialog()
            dlg.Title = "開啟照片"
            dlg.Filter = "圖片檔|" & String.Join(";", PhotoFile.SupportedExtensions.Select(Function(x) "*" & x)) & "|所有檔案|*.*"
            If _photo IsNot Nothing Then dlg.InitialDirectory = Path.GetDirectoryName(_photo.Path)
            If dlg.ShowDialog(Me) = DialogResult.OK Then OpenPhoto(dlg.FileName)
        End Using
    End Sub

    Private Sub OpenPhoto(photoPath As String)
        If Not ConfirmDiscard() Then Return

        Dim photo As PhotoFile
        Cursor = Cursors.WaitCursor
        Try
            photo = PhotoFile.Open(photoPath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse
                                   TypeOf ex Is ExternalException
            ' GDI+ 遇到不支援或損壞的圖檔會丟 OutOfMemoryException / ArgumentException。
            MessageBox.Show(Me, $"無法開啟「{Path.GetFileName(photoPath)}」：{ex.Message}", AppName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Finally
            Cursor = Cursors.Default
        End Try

        ExitCropMode(apply:=False)
        _canvas.Image = Nothing
        _canvas.ZoomToFit()
        DisposeImages()
        SyncLock _sourceLock ' 等背景全尺寸算圖結束才釋放舊照片
            _photo?.Dispose()
            _photo = photo
        End SyncLock

        _previewBase = ImagePipeline.CopyScaled(photo.Image, PreviewMaxSize)
        _recipe = If(RecipeStore.Load(photoPath), New EditRecipe())
        _savedRecipe = _recipe.Clone()
        LoadCutoutMask()
        _history.Clear()
        _localIndex = -1
        _overlayIndex = -1
        _drawIndex = -1
        _polyPoints = Nothing
        CommitCalloutEditor()
        SyncSliders()
        UpdateCreativeControls() ' 文字、貼圖、去背分頁的選取與可用狀態
        UpdateToolFromTab()
        UpdateTitle()
        StartFaceDetection()
        RenderNow()
    End Sub

    Private Sub DisposeImages()
        _hiResGeneration += 1
        _hiResBusy = False
        _hiRes?.Dispose() : _hiRes = Nothing : _hiResKey = Nothing
        _retouched?.Dispose() : _retouched = Nothing : _retouchKey = Nothing
        _rendered?.Dispose() : _rendered = Nothing
        _previewBase?.Dispose() : _previewBase = Nothing
        _thumbKey = Nothing
    End Sub

    ''' <summary>有未儲存的編輯時詢問。回傳 False 表示使用者取消。</summary>
    Private Function ConfirmDiscard() As Boolean
        If Not IsDirty Then Return True
        Select Case MessageBox.Show(Me, $"要儲存對「{Path.GetFileName(_photo.Path)}」的編輯嗎？", AppName,
                                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            Case DialogResult.Yes : Return SaveRecipe()
            Case DialogResult.No : Return True
            Case Else : Return False
        End Select
    End Function

    Private Function SaveRecipe() As Boolean
        Try
            RecipeStore.Save(_photo.Path, _recipe)
            SaveCutoutMask()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            MessageBox.Show(Me, "無法儲存編輯：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        End Try
        _savedRecipe = _recipe.Clone()
        UpdateTitle()
        SetStatusMessage(If(_recipe.IsIdentity, "已回復原圖，移除編輯檔。", "已儲存編輯：" & Path.GetFileName(RecipeStore.SidecarPath(_photo.Path))))
        Return True
    End Function

    Private Sub ExportWithDialog()
        ExitCropMode(apply:=True)
        ' 去背成透明背景、或裁成圓形等形狀時預設存 PNG（JPG 不能透明）。
        Dim transparent = (_recipe.Cutout IsNot Nothing AndAlso _recipe.Cutout.Background = CutoutBackground.Transparent) OrElse
                          _recipe.CropShape <> CropShape.Rectangle
        Dim suggested = PhotoFile.SuggestExportPath(_photo.Path, If(transparent, ".png", ".jpg"))
        Using dlg As New SaveFileDialog()
            dlg.Title = "匯出"
            dlg.Filter = "JPEG 圖片|*.jpg|PNG 圖片（保留透明）|*.png"
            dlg.FilterIndex = If(transparent, 2, 1)
            dlg.InitialDirectory = Path.GetDirectoryName(suggested)
            dlg.FileName = Path.GetFileName(suggested)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

            Cursor = Cursors.WaitCursor
            Try
                SyncLock _sourceLock
                    _photo.Export(_recipe, dlg.FileName, prepare:=SourcePrepare(_recipe), faces:=_faces)
                End SyncLock
                SetStatusMessage("已匯出：" & dlg.FileName)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                       TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ExternalException
                MessageBox.Show(Me, "匯出失敗：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Finally
                Cursor = Cursors.Default
            End Try
        End Using
    End Sub

    Private Sub OnFileDragEnter(sender As Object, e As DragEventArgs)
        If e.Data?.GetDataPresent(StickerDataFormat) Then
            ' 從貼圖分頁拖過來：只能放在照片上。
            e.Effect = If(_photo IsNot Nothing AndAlso sender Is _canvas, DragDropEffects.Copy, DragDropEffects.None)
            Return
        End If
        e.Effect = If(DroppedPhoto(e) IsNot Nothing, DragDropEffects.Copy, DragDropEffects.None)
    End Sub

    Private Sub OnFileDragDrop(sender As Object, e As DragEventArgs)
        If e.Data?.GetDataPresent(StickerDataFormat) Then
            If sender IsNot _canvas Then Return
            Dim key = CStr(e.Data.GetData(StickerDataFormat))
            Dim at = _canvas.ClientToImage(_canvas.PointToClient(New Point(e.X, e.Y)))
            AddStickerAt(key, If(at.HasValue, DisplayToPhoto(at.Value), New PointF(0.5F, 0.5F)))
            Return
        End If
        Dim photoPath = DroppedPhoto(e)
        If photoPath IsNot Nothing Then BeginInvoke(Sub() OpenPhoto(photoPath)) ' 讓拖曳來源先結束，對話框才不會卡住檔案總管
    End Sub

    Private Shared Function DroppedPhoto(e As DragEventArgs) As String
        Dim files = TryCast(e.Data?.GetData(DataFormats.FileDrop), String())
        If files Is Nothing OrElse files.Length = 0 Then Return Nothing
        Return If(PhotoFile.IsSupported(files(0)), files(0), Nothing)
    End Function

    '=====================================================================
    ' 生命週期
    '=====================================================================

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If Not String.IsNullOrEmpty(_startupPath) AndAlso File.Exists(_startupPath) Then OpenPhoto(_startupPath)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        MyBase.OnFormClosing(e)
        If Not e.Cancel AndAlso Not ConfirmDiscard() Then e.Cancel = True
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        _renderTimer.Dispose()
        _hiResTimer.Dispose()
        _canvas.Image = Nothing
        DisposeImages()
        _aiMask?.Dispose()
        SyncLock _sourceLock
            _photo?.Dispose()
            _photo = Nothing
        End SyncLock
        MyBase.OnFormClosed(e)
    End Sub
End Class
