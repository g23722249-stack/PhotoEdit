Imports System.IO
Imports System.Threading.Tasks
Imports PhotoEdit

''' <summary>
''' 「去背」分頁：AI 自動去背（一般／人像）、保留與擦除筆刷、邊緣、換背景、檢視遮罩、存成貼圖。
''' AI 遮罩先放在記憶體（_aiMask），「儲存編輯」時才寫成照片旁的附屬檔（MaskStore），和 .pedit.json 一致。
''' </summary>
Partial Friend Class frmEditor

    Private Const TabCutout As Integer = 8

    Private _aiMask As Bitmap
    Private _aiMaskModel As CutoutModel = CutoutModel.General
    Private _aiMaskVersion As Integer
    Private _aiMaskDirty As Boolean
    Private _cutoutBusy As Boolean
    Private _cutoutShowMask As Boolean

    Private ReadOnly _modelGeneral As New RadioButton()
    Private ReadOnly _modelHuman As New RadioButton()
    Private ReadOnly _autoCutout As New Button()
    Private ReadOnly _cutoutStatus As New Label()
    Private ReadOnly _keepBrush As New CheckBox()
    Private ReadOnly _eraseBrush As New CheckBox()
    Private ReadOnly _maskBrushSize As New Aqua.Slider()
    Private ReadOnly _cutoutBgCombo As New ComboBox()
    Private ReadOnly _cutoutBgColor As New Button()
    Private ReadOnly _cutoutBgImage As New Button()
    Private ReadOnly _viewResult As New RadioButton()
    Private ReadOnly _viewMask As New RadioButton()
    Private ReadOnly _cutoutControls As New List(Of Control)()

    Private Shared ReadOnly CutoutKeys As String() = {"co_feather", "co_shift", "co_blur"}

    Private Sub BuildCutoutPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        AddHeading(L, "自動去背")
        Dim half = (L.Width - 16) \ 2
        For Each rb In {_modelGeneral, _modelHuman}
            rb.Appearance = Appearance.Button
            rb.FlatStyle = FlatStyle.Flat
            rb.TextAlign = ContentAlignment.MiddleCenter
            rb.BackColor = Color.White
            rb.FlatAppearance.CheckedBackColor = Color.FromArgb(210, 228, 250)
            rb.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 195)
        Next
        _modelGeneral.Text = "一般（人像／動物／物品）"
        _modelGeneral.Checked = True
        _modelHuman.Text = "人像"
        _tip.SetToolTip(_modelGeneral, "IS-Net 通用模型：寵物、物品；照片有人臉時預設改用人像模型")
        _tip.SetToolTip(_modelHuman, "U²-Net 人像模型：人物的身體輪廓與頭髮較準")
        ' 兩組選項各放一個容器，才不會和下面「合成結果／檢查遮罩」互斥。
        L.Add(PairPanel(_modelGeneral, _modelHuman, 8, L.Y + 2, L.Width - 8, 60))
        L.Y += 36
        _autoCutout.Text = "自動去背"
        _autoCutout.UseVisualStyleBackColor = True
        _autoCutout.Font = New Font(_panelFont, FontStyle.Bold)
        _autoCutout.SetBounds(8, L.Y + 2, L.Width - 8, 34)
        AddHandler _autoCutout.Click, Sub() RunAutoCutout()
        L.Add(_autoCutout)
        L.Y += 40
        _cutoutStatus.AutoSize = False
        _cutoutStatus.BackColor = Color.Transparent
        _cutoutStatus.ForeColor = Color.FromArgb(105, 110, 120)
        _cutoutStatus.SetBounds(8, L.Y, L.Width - 8, 22)
        L.Add(_cutoutStatus)
        L.Y += 26

        AddHeading(L, "修正")
        For Each cb In {_keepBrush, _eraseBrush}
            cb.Appearance = Appearance.Button
            cb.TextAlign = ContentAlignment.MiddleCenter
            cb.FlatStyle = FlatStyle.Flat
            cb.BackColor = Color.White
            cb.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 195)
        Next
        _keepBrush.Text = "保留筆刷（綠）"
        _keepBrush.FlatAppearance.CheckedBackColor = Color.FromArgb(200, 240, 200)
        _keepBrush.SetBounds(8, L.Y + 2, half, 30)
        _eraseBrush.Text = "擦除筆刷（紅）"
        _eraseBrush.FlatAppearance.CheckedBackColor = Color.FromArgb(250, 210, 210)
        _eraseBrush.SetBounds(8 + half + 8, L.Y + 2, half, 30)
        _tip.SetToolTip(_keepBrush, "塗抹被誤去掉的主體，把它補回來")
        _tip.SetToolTip(_eraseBrush, "塗抹沒去乾淨的背景，把它擦掉")
        AddHandler _keepBrush.CheckedChanged, Sub() OnMaskBrushToggled(_keepBrush)
        AddHandler _eraseBrush.CheckedChanged, Sub() OnMaskBrushToggled(_eraseBrush)
        L.Add(_keepBrush) : L.Add(_eraseBrush)
        L.Y += 38
        AddCaption(L, "筆刷大小", L.Y)
        _maskBrushSize.Minimum = 4 : _maskBrushSize.Maximum = 150 : _maskBrushSize.Value = 24 : _maskBrushSize.ShowTicks = False
        _maskBrushSize.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - 8, 24)
        AddHandler _maskBrushSize.ValueChanged, Sub()
                                                    If _canvas.Tool = PreviewCanvas.CanvasTool.MaskBrush Then _canvas.BrushRadius = _maskBrushSize.Value
                                                End Sub
        L.Add(_maskBrushSize)
        L.Y += RowHeight
        AddButtonPair(L, "清除修正", "移除所有保留／擦除筆觸", Sub() ApplyChange(Sub(r)
                                                                                 If r.Cutout IsNot Nothing Then r.Cutout.Strokes.Clear()
                                                                             End Sub),
                         "取消去背", "回到原本的照片（AI 遮罩會保留，可再套用）", Sub() ApplyChange(Sub(r) r.Cutout = Nothing))

        AddHeading(L, "邊緣")
        AddRow(L, CutoutRow("co_feather", "羽化", 0, 100, Function(c) c.Feather, Sub(c, v) c.Feather = v), "讓邊緣柔和，避免鋸齒")
        AddRow(L, CutoutRow("co_shift", "內縮外擴", -50, 50, Function(c) c.Shift, Sub(c, v) c.Shift = v), "往左內縮（去掉殘留的背景邊），往右外擴")

        AddHeading(L, "背景")
        AddCaption(L, "換成", L.Y)
        _cutoutBgCombo.DropDownStyle = ComboBoxStyle.DropDownList
        _cutoutBgCombo.Items.AddRange({"原背景", "透明", "純色", "模糊（景深）", "圖片"})
        _cutoutBgCombo.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _cutoutBgCombo.SelectedIndexChanged, Sub()
                                                             If _syncing OrElse _cutoutBgCombo.SelectedIndex < 0 Then Return
                                                             Dim bg = CType(_cutoutBgCombo.SelectedIndex, CutoutBackground)
                                                             If bg = CutoutBackground.Image AndAlso String.IsNullOrEmpty(_recipe.Cutout?.BackgroundImagePath) Then
                                                                 PickCutoutBackgroundImage()
                                                                 Return
                                                             End If
                                                             ApplyChange(Sub(r)
                                                                             If r.Cutout IsNot Nothing Then r.Cutout.Background = bg
                                                                         End Sub)
                                                         End Sub
        L.Add(_cutoutBgCombo)
        L.Y += RowHeight
        AddCaption(L, "顏色", L.Y)
        _cutoutBgColor.FlatStyle = FlatStyle.Flat
        _cutoutBgColor.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 26)
        AddHandler _cutoutBgColor.Click, Sub() PickCutoutBackgroundColor()
        L.Add(_cutoutBgColor)
        L.Y += RowHeight
        AddRow(L, CutoutRow("co_blur", "模糊", 0, 100, Function(c) c.BackgroundBlur, Sub(c, v) c.BackgroundBlur = v))
        _cutoutBgImage.Text = "選擇背景圖片…"
        _cutoutBgImage.UseVisualStyleBackColor = True
        _cutoutBgImage.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - 8, 28)
        AddHandler _cutoutBgImage.Click, Sub() PickCutoutBackgroundImage()
        L.Add(_cutoutBgImage)
        L.Y += RowHeight + 2

        AddHeading(L, "檢視與輸出")
        For Each rb In {_viewResult, _viewMask}
            rb.Appearance = Appearance.Button
            rb.FlatStyle = FlatStyle.Flat
            rb.TextAlign = ContentAlignment.MiddleCenter
            rb.BackColor = Color.White
            rb.FlatAppearance.CheckedBackColor = Color.FromArgb(210, 228, 250)
        Next
        _viewResult.Text = "合成結果"
        _viewResult.Checked = True
        _viewMask.Text = "檢查遮罩（紅＝去掉）"
        AddHandler _viewMask.CheckedChanged, Sub()
                                                 _cutoutShowMask = _viewMask.Checked
                                                 UpdateCutoutControls()
                                                 RequestRender()
                                             End Sub
        L.Add(PairPanel(_viewResult, _viewMask, 8, L.Y + 2, L.Width - 8, 50))
        L.Y += 36
        Dim sticker = MakeButton("存成貼圖…", "把去背後的主體存成 PNG 放進貼圖資料夾，之後可以拖到其他照片上")
        sticker.Font = New Font(_panelFont, FontStyle.Bold)
        sticker.SetBounds(8, L.Y + 2, L.Width - 8, 34)
        AddHandler sticker.Click, Sub() SaveCutoutAsSticker()
        L.Add(sticker)
        L.Y += 40
        AddHint(L, "透明背景可以在「匯出」時存成 PNG。")

        _cutoutControls.AddRange({_keepBrush, _eraseBrush, _maskBrushSize, _cutoutBgCombo, _cutoutBgColor, _cutoutBgImage, _viewResult, _viewMask, sticker})
    End Sub

    ''' <summary>兩個選項按鈕放進自己的容器（互斥範圍只在容器內），依比例分寬度、跟著面板伸縮。</summary>
    Private Shared Function PairPanel(a As Control, b As Control, x As Integer, y As Integer, width As Integer, firstPercent As Single) As Control
        Dim t As New TableLayoutPanel With {.ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent, .Margin = Padding.Empty}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, firstPercent))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100 - firstPercent))
        a.Dock = DockStyle.Fill : b.Dock = DockStyle.Fill
        a.Margin = New Padding(0, 0, 4, 0) : b.Margin = New Padding(4, 0, 0, 0)
        t.Controls.Add(a, 0, 0)
        t.Controls.Add(b, 1, 0)
        t.SetBounds(x, y, width, 28)
        Return t
    End Function

    Private Function CutoutRow(key As String, caption As String, min As Integer, max As Integer,
                               getter As Func(Of CutoutSettings, Integer), setter As Action(Of CutoutSettings, Integer)) As SliderRow
        Return New SliderRow With {.Key = key, .Caption = caption, .Minimum = min, .Maximum = max,
            .Format = Function(v) If(min < 0 AndAlso v > 0, "+" & v, v.ToString()),
            .GetValue = Function(r) If(r.Cutout Is Nothing, getter(New CutoutSettings()), getter(r.Cutout)),
            .SetValue = Sub(r, v)
                            If r.Cutout IsNot Nothing Then setter(r.Cutout, v)
                        End Sub}
    End Function

    '---------------------------------------------------------------------
    ' AI 去背
    '---------------------------------------------------------------------

    Private ReadOnly Property SelectedCutoutModel As CutoutModel
        Get
            Return If(_modelHuman.Checked, CutoutModel.Human, CutoutModel.General)
        End Get
    End Property

    Private Sub RunAutoCutout()
        If _photo Is Nothing OrElse _cutoutBusy Then Return
        Dim model = SelectedCutoutModel
        If Not BackgroundRemover.ModelAvailable(model) Then
            MessageBox.Show(Me, "找不到去背模型：" & BackgroundRemover.ModelPath(model), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        ExitCropMode(apply:=True)
        _cutoutBusy = True
        _autoCutout.Enabled = False
        _cutoutStatus.Text = "去背計算中…（第一次載入模型會久一點）"
        Cursor = Cursors.AppStarting
        Dim copy = DirectCast(_previewBase.Clone(), Bitmap)
        Dim photo = _photo
        Dim watch = Diagnostics.Stopwatch.StartNew()
        Task.Run(Function() BackgroundRemover.ComputeMask(copy, model)).ContinueWith(
            Sub(t)
                copy.Dispose()
                Dim mask = If(t.Status = TaskStatus.RanToCompletion, t.Result, Nothing)
                Dim err = t.Exception?.GetBaseException().Message
                If Not PostToUi(Sub()
                                    _cutoutBusy = False
                                    _autoCutout.Enabled = True
                                    Cursor = Cursors.Default
                                    If photo IsNot _photo Then
                                        mask?.Dispose()
                                        Return
                                    End If
                                    If mask Is Nothing Then
                                        _cutoutStatus.Text = "去背失敗：" & err
                                        Return
                                    End If
                                    SetAiMask(mask, model, dirty:=True)
                                    _cutoutStatus.Text = $"完成（{watch.Elapsed.TotalSeconds:0.0} 秒，{If(model = CutoutModel.Human, "人像", "一般")}模型）"
                                    ApplyChange(Sub(r)
                                                    If r.Cutout Is Nothing Then r.Cutout = New CutoutSettings()
                                                    r.Cutout.Model = model
                                                    If r.Cutout.Background = CutoutBackground.Original Then r.Cutout.Background = CutoutBackground.Transparent
                                                End Sub)
                                End Sub) Then mask?.Dispose()
            End Sub)
    End Sub

    Private Sub SetAiMask(mask As Bitmap, model As CutoutModel, dirty As Boolean)
        _aiMask?.Dispose()
        _aiMask = mask
        _aiMaskModel = model
        _aiMaskDirty = dirty
        _aiMaskVersion += 1
    End Sub

    ''' <summary>開照片時：配方有去背就讀回附屬檔的遮罩。</summary>
    Private Sub LoadCutoutMask()
        _aiMask?.Dispose()
        _aiMask = Nothing
        _aiMaskDirty = False
        _aiMaskVersion += 1
        _cutoutShowMask = False
        _viewResult.Checked = True
        If _recipe.Cutout Is Nothing Then
            _cutoutStatus.Text = "按「自動去背」開始。"
            Return
        End If
        _modelHuman.Checked = _recipe.Cutout.Model = CutoutModel.Human
        _modelGeneral.Checked = Not _modelHuman.Checked
        Dim mask = MaskStore.Load(_photo.Path, _recipe.Cutout.Model)
        If mask IsNot Nothing Then
            SetAiMask(mask, _recipe.Cutout.Model, dirty:=False)
            _cutoutStatus.Text = "已載入先前的去背結果。"
        Else
            _cutoutStatus.Text = "找不到先前的去背遮罩，請再按一次「自動去背」。"
        End If
    End Sub

    ''' <summary>儲存編輯時一併寫入遮罩附屬檔（去背已取消時刪除）。</summary>
    Private Sub SaveCutoutMask()
        If _photo Is Nothing Then Return
        If _recipe.Cutout Is Nothing Then
            Return
        End If
        If _aiMask IsNot Nothing AndAlso _aiMaskDirty Then
            SyncLock _aiMask
                MaskStore.Save(_photo.Path, _aiMaskModel, _aiMask)
            End SyncLock
            _aiMaskDirty = False
        End If
    End Sub

    ''' <summary>匯出、全尺寸預覽、拼貼用：目前的 AI 遮罩（模型要和配方一致）。</summary>
    Private Function CurrentAiMask(recipe As EditRecipe) As Bitmap
        If recipe.Cutout Is Nothing OrElse _aiMask Is Nothing OrElse recipe.Cutout.Model <> _aiMaskModel Then Return Nothing
        Return _aiMask
    End Function

    '---------------------------------------------------------------------
    ' 筆刷、背景
    '---------------------------------------------------------------------

    Private Sub OnMaskBrushToggled(source As CheckBox)
        If _syncing Then Return
        If source.Checked Then
            Dim other = If(source Is _keepBrush, _eraseBrush, _keepBrush)
            _syncing = True
            other.Checked = False
            _syncing = False
            If _recipe.Cutout Is Nothing Then
                SetStatusMessage("請先按「自動去背」。")
            End If
        End If
        UpdateToolFromTab()
    End Sub

    Private ReadOnly Property MaskBrushActive As Boolean
        Get
            Return _keepBrush.Checked OrElse _eraseBrush.Checked
        End Get
    End Property

    Private Sub OnMaskStroke(points As List(Of PointF), screenRadius As Single)
        If _recipe.Cutout Is Nothing Then Return
        Dim s = MakeSourceStroke(points, screenRadius)
        Dim stroke As New CutoutStroke With {.Radius = s.Radius, .Path = s.Path, .Keep = _keepBrush.Checked}
        ApplyChange(Sub(r) r.Cutout.Strokes.Add(stroke))
    End Sub

    Private Sub PickCutoutBackgroundColor()
        If _recipe.Cutout Is Nothing Then Return
        Using dlg As New ColorDialog With {.Color = Color.FromArgb(_recipe.Cutout.BackgroundColorArgb), .FullOpen = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim argb = dlg.Color.ToArgb()
            ApplyChange(Sub(r)
                            r.Cutout.BackgroundColorArgb = argb
                            r.Cutout.Background = CutoutBackground.Color
                        End Sub)
        End Using
    End Sub

    Private Sub PickCutoutBackgroundImage()
        If _recipe.Cutout Is Nothing Then Return
        Using dlg As New OpenFileDialog With {.Title = "選擇背景圖片", .Filter = "圖片檔|*.jpg;*.jpeg;*.png;*.bmp;*.gif"}
            If _photo IsNot Nothing Then dlg.InitialDirectory = Path.GetDirectoryName(_photo.Path)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then
                UpdateCutoutControls()
                Return
            End If
            Dim file = dlg.FileName
            ApplyChange(Sub(r)
                            r.Cutout.BackgroundImagePath = file
                            r.Cutout.Background = CutoutBackground.Image
                        End Sub)
        End Using
    End Sub

    Private Sub UpdateCutoutControls()
        Dim c = _recipe.Cutout
        Dim has = c IsNot Nothing
        For Each ctl In _cutoutControls
            ctl.Enabled = has
        Next
        _cutoutBgCombo.SelectedIndex = If(has, CInt(c.Background), -1)
        _cutoutBgColor.BackColor = If(has, Color.FromArgb(255, Color.FromArgb(c.BackgroundColorArgb)), SystemColors.Control)
        _cutoutBgImage.Text = If(has AndAlso Not String.IsNullOrEmpty(c.BackgroundImagePath), "背景：" & Path.GetFileName(c.BackgroundImagePath), "選擇背景圖片…")
        For Each row In _rows.Where(Function(r) CutoutKeys.Contains(r.Key))
            row.Slider.Enabled = has
        Next
        If Not has AndAlso MaskBrushActive Then
            _keepBrush.Checked = False
            _eraseBrush.Checked = False
        End If
        _canvas.Checkerboard = has AndAlso c.Background = CutoutBackground.Transparent AndAlso Not _cutoutShowMask
    End Sub

    '---------------------------------------------------------------------
    ' 存成貼圖
    '---------------------------------------------------------------------

    Private Sub SaveCutoutAsSticker()
        If _photo Is Nothing OrElse _recipe.Cutout Is Nothing Then Return
        Dim theme As String = Nothing, name As String = Nothing
        If Not AskStickerName(theme, name) Then Return

        ' 只要主體：透明背景、不加邊框、文字貼圖與位置相關的效果。
        Dim r = _recipe.Clone()
        r.Cutout.Background = CutoutBackground.Transparent
        r.Frame = PhotoFrameStyle.None
        r.Overlays = Nothing
        r.Vignette = 0 : r.Grain = 0 : r.BackgroundBlur = 0 : r.TiltShift = 0
        Cursor = Cursors.WaitCursor
        Try
            Dim mask = CurrentAiMask(r)
            Dim faces As IReadOnlyList(Of FaceRegion) = If(_faces, Array.Empty(Of FaceRegion)())
            Using rendered = ImagePipeline.Render(_photo.Image, r, 1400, Function(b) SourceFix.Apply(b, faces, r, mask)),
                  trimmed = TrimTransparent(rendered)
                If trimmed Is Nothing Then
                    SetStatusMessage("去背結果是空的，無法存成貼圖。")
                    Return
                End If
                Dim dir = Path.Combine(StickerLibrary.Root, theme)
                Directory.CreateDirectory(dir)
                Dim file = Path.Combine(dir, name & ".png")
                Dim n = 2
                While IO.File.Exists(file)
                    file = Path.Combine(dir, $"{name}-{n}.png")
                    n += 1
                End While
                trimmed.Save(file, Drawing.Imaging.ImageFormat.Png)
                _currentTheme = theme
                ReloadStickers()
                SetStatusMessage($"已存成貼圖：stick\{theme}\{Path.GetFileName(file)}（在「貼圖」分頁可以拖到其他照片上）")
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            MessageBox.Show(Me, "存成貼圖失敗：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    ''' <summary>裁掉四周完全透明的部分（留一點邊）；全透明時回傳 Nothing。</summary>
    Private Shared Function TrimTransparent(bmp As Bitmap) As Bitmap
        Dim w = bmp.Width, h = bmp.Height
        Dim data = bmp.LockBits(New Rectangle(0, 0, w, h), Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb)
        Dim px(data.Stride * h - 1) As Byte
        Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length)
        Dim stride = data.Stride
        bmp.UnlockBits(data)
        Dim l = w, t = h, r = -1, b = -1
        For y = 0 To h - 1
            For x = 0 To w - 1
                If px(y * stride + x * 4 + 3) > 10 Then
                    If x < l Then l = x
                    If x > r Then r = x
                    If y < t Then t = y
                    If y > b Then b = y
                End If
            Next
        Next
        If r < 0 Then Return Nothing
        Dim pad = Math.Max(2, CInt(Math.Max(r - l, b - t) * 0.02))
        Dim rect = Rectangle.FromLTRB(Math.Max(0, l - pad), Math.Max(0, t - pad), Math.Min(w, r + pad + 1), Math.Min(h, b + pad + 1))
        Return bmp.Clone(rect, Drawing.Imaging.PixelFormat.Format32bppArgb)
    End Function

    ''' <summary>選主題（可輸入新主題）與檔名的小對話框。</summary>
    Private Function AskStickerName(ByRef theme As String, ByRef name As String) As Boolean
        Using dlg As New Form With {.Text = "存成貼圖", .FormBorderStyle = FormBorderStyle.FixedDialog, .StartPosition = FormStartPosition.CenterParent,
                                    .MinimizeBox = False, .MaximizeBox = False, .ClientSize = New Size(380, 150), .Font = _panelFont, .ShowInTaskbar = False}
            Dim l1 As New Label With {.Text = "主題", .Location = New Point(16, 20), .AutoSize = True}
            Dim themeBox As New ComboBox With {.Location = New Point(90, 16), .Width = 270, .DropDownStyle = ComboBoxStyle.DropDown}
            themeBox.Items.AddRange(StickerLibrary.Themes().Cast(Of Object)().ToArray())
            themeBox.Text = If(_currentTheme <> StickerLibrary.BuiltInTheme, _currentTheme, "我的去背")
            Dim l2 As New Label With {.Text = "名稱", .Location = New Point(16, 60), .AutoSize = True}
            Dim nameBox As New TextBox With {.Location = New Point(90, 56), .Width = 270, .Text = Path.GetFileNameWithoutExtension(_photo.Path)}
            Dim ok As New Button With {.Text = "儲存", .DialogResult = DialogResult.OK, .Location = New Point(190, 102), .Size = New Size(80, 32)}
            Dim cancel As New Button With {.Text = "取消", .DialogResult = DialogResult.Cancel, .Location = New Point(280, 102), .Size = New Size(80, 32)}
            dlg.Controls.AddRange(New Control() {l1, themeBox, l2, nameBox, ok, cancel})
            dlg.AcceptButton = ok
            dlg.CancelButton = cancel
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return False
            Dim bad = Path.GetInvalidFileNameChars()
            theme = New String(themeBox.Text.Trim().Where(Function(ch) Not bad.Contains(ch)).ToArray())
            name = New String(nameBox.Text.Trim().Where(Function(ch) Not bad.Contains(ch)).ToArray())
            If theme = "" OrElse theme = StickerLibrary.BuiltInTheme Then theme = "我的去背"
            If name = "" Then name = "貼圖"
            Return True
        End Using
    End Function
End Class
