Imports System.IO
Imports System.Runtime.InteropServices
Imports PhotoEdit

''' <summary>
''' 檔案 → 新增：選底色（白、黑、自訂、透明）、底圖（檔案／使用中影像／剪貼簿，填滿／符合／延展／置中／並排）、
''' 影像大小（標準版面、使用中影像、剪貼簿影像、底圖、自訂寬高與單位）與解析度，右邊即時預覽。
''' 版面類似小畫家的「影像屬性」與 Photoshop 的「新增文件」；上次的選擇會記住。
''' 按「確定」後由呼叫端在對話框關閉前呼叫 CreateImage() 取得畫布。
''' </summary>
Friend Class frmNewImage
    Inherits Form

    Private Enum SizeMode
        Standard
        Current
        Clipboard
        Picture
        Custom
    End Enum

    ' 上次的選擇（同一次執行期間記住）。
    Private Shared _lastFill As NewImageFill = NewImageFill.White
    Private Shared _lastColor As Color = Color.FromArgb(255, 240, 230)
    Private Shared _lastPreset As Integer = 0
    Private Shared _lastMode As SizeMode = SizeMode.Standard
    Private Shared _lastDpi As Double = 300
    Private Shared _lastDpiPerCm As Boolean
    Private Shared _lastUnit As SizeUnit = SizeUnit.Pixels
    Private Shared _lastCustom As Size = New Size(1920, 1080)
    Private Shared _lastFit As PictureFit = PictureFit.Cover

    Private Shared ReadOnly DpiPresets As (Name As String, Dpi As Double)() = {
        ("使用者定義", 0), ("72（網頁）", 72), ("96（螢幕）", 96), ("150（一般列印）", 150),
        ("300（相片列印）", 300), ("350（印刷）", 350), ("600（高品質）", 600)}
    Private Shared ReadOnly FitNames As String() = {"填滿（裁掉多餘）", "完整顯示（留邊）", "延展（可能變形）", "原尺寸置中", "原尺寸並排"}

    Private ReadOnly _currentSize As Size?
    Private ReadOnly _clipboardSize As Size?
    Private ReadOnly _currentPicture As Func(Of Bitmap)
    Private ReadOnly _clipboardPicture As Func(Of Bitmap)

    Private ReadOnly _spec As New NewImageSpec()
    Private _picture As Bitmap
    Private _pictureName As String
    Private _syncing As Boolean
    Private _ratio As Double = 1
    Private _landscape As Boolean
    Private _preview As Bitmap
    Private _boldFont As Font

    ' 底色
    Private ReadOnly _rbWhite As New RadioButton With {.Text = "白色(&W)"}
    Private ReadOnly _rbBlack As New RadioButton With {.Text = "黑色(&K)"}
    Private ReadOnly _rbColor As New RadioButton With {.Text = "自訂色彩(&M)"}
    Private ReadOnly _rbTransparent As New RadioButton With {.Text = "透明(&T)"}
    Private ReadOnly _colorButton As New Button With {.FlatStyle = FlatStyle.Flat}
    ' 底圖
    Private ReadOnly _pictureLabel As New Label With {.AutoEllipsis = True}
    Private ReadOnly _pickFile As New Button With {.Text = "選擇檔案…"}
    Private ReadOnly _pickCurrent As New Button With {.Text = "使用中影像"}
    Private ReadOnly _pickClipboard As New Button With {.Text = "剪貼簿"}
    Private ReadOnly _clearPicture As New Button With {.Text = "移除"}
    Private ReadOnly _fit As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    ' 影像大小
    Private ReadOnly _sizeGroup As New GroupBox()
    Private ReadOnly _rbStandard As New RadioButton With {.Text = "標準(&S)"}
    Private ReadOnly _category As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _presetBox As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _rbCurrent As New RadioButton()
    Private ReadOnly _rbClipboard As New RadioButton()
    Private ReadOnly _rbPicture As New RadioButton()
    Private ReadOnly _rbCustom As New RadioButton With {.Text = "使用者自訂(&U)"}
    Private ReadOnly _width As New NumericUpDown With {.Maximum = 1000000, .TextAlign = HorizontalAlignment.Right}
    Private ReadOnly _height As New NumericUpDown With {.Maximum = 1000000, .TextAlign = HorizontalAlignment.Right}
    Private ReadOnly _unit As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _portrait As New Button With {.Text = "直向"}
    Private ReadOnly _landscapeButton As New Button With {.Text = "橫向"}
    Private ReadOnly _lockRatio As New CheckBox With {.Text = "鎖定比例"}
    ' 解析度
    Private ReadOnly _dpiPreset As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _dpi As New NumericUpDown With {.Minimum = 1, .Maximum = 10000, .DecimalPlaces = 0, .TextAlign = HorizontalAlignment.Right}
    Private ReadOnly _dpiUnit As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _fileSize As New Label()
    Private ReadOnly _previewBox As New Panel()
    Private ReadOnly _ok As New Button With {.Text = "確定", .DialogResult = DialogResult.OK}

    ''' <param name="currentSize">使用中影像的輸出大小；沒有開啟影像時為 Nothing。</param>
    ''' <param name="currentPicture">取得使用中影像（含編輯）當底圖；呼叫端產生新點陣圖，由本視窗釋放。</param>
    ''' <param name="clipboardPicture">讀剪貼簿圖片；沒有時回傳 Nothing。</param>
    Public Sub New(currentSize As Size?, currentPicture As Func(Of Bitmap), clipboardSize As Size?, clipboardPicture As Func(Of Bitmap))
        _currentSize = currentSize
        _currentPicture = currentPicture
        _clipboardSize = clipboardSize
        _clipboardPicture = clipboardPicture

        Text = "新增影像"
        Font = New Font("Microsoft JhengHei UI", 9.0F)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Dpi
        _boldFont = New Font(Font, FontStyle.Bold)
        ClientSize = New Size(700, 548)

        BuildLayout()
        LoadSettings()
        AddHandler FormClosed, Sub()
                                   _picture?.Dispose()
                                   _preview?.Dispose()
                                   _boldFont.Dispose()
                               End Sub
        ThemeManager.Attach(Me)
    End Sub

    '---------------------------------------------------------------------
    ' 版面
    '---------------------------------------------------------------------

    Private Sub BuildLayout()
        Const L = 12, W = 440
        Dim y = 10

        ' 底色
        Dim fillGroup As New GroupBox With {.Text = "底色"}
        fillGroup.SetBounds(L, y, W, 78)
        _rbWhite.SetBounds(14, 22, 150, 22)
        _rbBlack.SetBounds(220, 22, 150, 22)
        _rbColor.SetBounds(14, 48, 120, 22)
        _colorButton.SetBounds(140, 47, 36, 22)
        _rbTransparent.SetBounds(220, 48, 150, 22)
        fillGroup.Controls.AddRange(New Control() {_rbWhite, _rbBlack, _rbColor, _colorButton, _rbTransparent})
        For Each rb In {_rbWhite, _rbBlack, _rbColor, _rbTransparent}
            AddHandler rb.CheckedChanged, Sub(s, e) If DirectCast(s, RadioButton).Checked Then OnFillChanged()
        Next
        AddHandler _colorButton.Click, Sub() PickColor()
        y += 86

        ' 底圖
        Dim pictureGroup As New GroupBox With {.Text = "底圖（可直接帶入一張圖當背景）"}
        pictureGroup.SetBounds(L, y, W, 106)
        _pictureLabel.SetBounds(14, 22, W - 28, 20)
        _pickFile.SetBounds(14, 44, 100, 26)
        _pickCurrent.SetBounds(120, 44, 100, 26)
        _pickClipboard.SetBounds(226, 44, 100, 26)
        _clearPicture.SetBounds(332, 44, 94, 26)
        Dim fitCaption As New Label With {.Text = "放置方式", .TextAlign = ContentAlignment.MiddleLeft}
        fitCaption.SetBounds(14, 76, 70, 22)
        _fit.Items.AddRange(FitNames)
        _fit.SetBounds(88, 76, 200, 24)
        pictureGroup.Controls.AddRange(New Control() {_pictureLabel, _pickFile, _pickCurrent, _pickClipboard, _clearPicture, fitCaption, _fit})
        _pickCurrent.Enabled = _currentPicture IsNot Nothing
        _pickClipboard.Enabled = _clipboardPicture IsNot Nothing
        AddHandler _pickFile.Click, Sub() PickPictureFile()
        AddHandler _pickCurrent.Click, Sub() SetPicture(_currentPicture(), "使用中影像")
        AddHandler _pickClipboard.Click, Sub() SetPicture(_clipboardPicture(), "剪貼簿")
        AddHandler _clearPicture.Click, Sub() SetPicture(Nothing, Nothing)
        AddHandler _fit.SelectedIndexChanged, Sub() If Not _syncing Then UpdateAll()
        y += 114

        ' 影像大小
        _sizeGroup.SetBounds(L, y, W, 222)
        Dim gy = 22
        _rbStandard.SetBounds(14, gy, 90, 22)
        _category.SetBounds(106, gy, 82, 24)
        _presetBox.SetBounds(192, gy, W - 206, 24)
        _presetBox.DropDownWidth = 300
        _category.Items.AddRange(CanvasPreset.Categories.Select(Function(c) CObj(c)).ToArray())
        gy += 28
        _rbCurrent.SetBounds(14, gy, W - 28, 22) : gy += 24
        _rbClipboard.SetBounds(14, gy, W - 28, 22) : gy += 24
        _rbPicture.SetBounds(14, gy, W - 28, 22) : gy += 26
        _rbCustom.SetBounds(14, gy, 120, 22) : gy += 26
        Dim wCaption As New Label With {.Text = "寬度(&W)", .TextAlign = ContentAlignment.MiddleLeft}
        wCaption.SetBounds(34, gy, 64, 22)
        _width.SetBounds(100, gy, 96, 24)
        _unit.SetBounds(206, gy, 80, 24)
        For Each u In [Enum].GetValues(Of SizeUnit)()
            _unit.Items.Add(NewImage.UnitName(u))
        Next
        _lockRatio.SetBounds(298, gy, 100, 22)
        gy += 28
        Dim hCaption As New Label With {.Text = "高度(&E)", .TextAlign = ContentAlignment.MiddleLeft}
        hCaption.SetBounds(34, gy, 64, 22)
        _height.SetBounds(100, gy, 96, 24)
        _portrait.SetBounds(206, gy - 1, 60, 26)
        _landscapeButton.SetBounds(270, gy - 1, 60, 26)
        _sizeGroup.Controls.AddRange(New Control() {_rbStandard, _category, _presetBox, _rbCurrent, _rbClipboard, _rbPicture, _rbCustom,
                                                    wCaption, _width, _unit, _lockRatio, hCaption, _height, _portrait, _landscapeButton})
        For Each rb In {_rbStandard, _rbCurrent, _rbClipboard, _rbPicture, _rbCustom}
            AddHandler rb.CheckedChanged, Sub(s, e) If DirectCast(s, RadioButton).Checked Then OnModeChanged()
        Next
        AddHandler _category.SelectedIndexChanged, Sub() OnCategoryChanged()
        AddHandler _presetBox.SelectedIndexChanged, Sub() OnPresetChanged()
        AddHandler _width.ValueChanged, Sub() OnCustomValueChanged(True)
        AddHandler _height.ValueChanged, Sub() OnCustomValueChanged(False)
        AddHandler _unit.SelectedIndexChanged, Sub() OnUnitChanged()
        AddHandler _lockRatio.CheckedChanged, Sub() If _spec.Height > 0 Then _ratio = _spec.Width / CDbl(_spec.Height)
        AddHandler _portrait.Click, Sub() SetOrientation(False)
        AddHandler _landscapeButton.Click, Sub() SetOrientation(True)
        ' 點到停用的自訂欄位時切換到自訂。
        AddHandler _width.Enter, Sub() _rbCustom.Checked = True
        AddHandler _height.Enter, Sub() _rbCustom.Checked = True
        AddHandler _presetBox.Enter, Sub() _rbStandard.Checked = True
        AddHandler _category.Enter, Sub() _rbStandard.Checked = True
        y += 230

        ' 解析度
        Dim dpiCaption As New Label With {.Text = "解析度(&R)", .TextAlign = ContentAlignment.MiddleLeft}
        dpiCaption.SetBounds(L, y, 70, 24)
        _dpiPreset.Items.AddRange(DpiPresets.Select(Function(d) CObj(d.Name)).ToArray())
        _dpiPreset.SetBounds(L + 72, y, 136, 24)
        _dpi.SetBounds(L + 214, y, 80, 24)
        _dpiUnit.Items.AddRange({"像素/英吋", "像素/公分"})
        _dpiUnit.SetBounds(L + 300, y, 100, 24)
        AddHandler _dpiPreset.SelectedIndexChanged, Sub()
                                                        If _syncing OrElse _dpiPreset.SelectedIndex <= 0 Then Return
                                                        SetDpi(DpiPresets(_dpiPreset.SelectedIndex).Dpi)
                                                    End Sub
        AddHandler _dpi.ValueChanged, Sub()
                                          If _syncing Then Return
                                          SetDpi(If(_dpiUnit.SelectedIndex = 1, CDbl(_dpi.Value) * 2.54, CDbl(_dpi.Value)))
                                      End Sub
        AddHandler _dpiUnit.SelectedIndexChanged, Sub() If Not _syncing Then UpdateAll()
        y += 32
        _fileSize.SetBounds(L, y, W, 22)
        y += 30

        ' 預覽
        Dim previewCaption As New Label With {.Text = "預覽", .ForeColor = Color.FromArgb(90, 95, 105)}
        previewCaption.SetBounds(468, 14, 220, 18)
        _previewBox.SetBounds(468, 34, 220, 220)
        _previewBox.BorderStyle = BorderStyle.FixedSingle
        _previewBox.BackColor = Color.FromArgb(200, 203, 208)
        EnableDoubleBuffer(_previewBox)
        AddHandler _previewBox.Paint, AddressOf PaintPreview
        Dim hint As New Label With {.ForeColor = Color.FromArgb(90, 95, 105),
            .Text = "新影像會存成 PNG（圖片\PhotoEdit\新影像），之後就和一般照片一樣編輯、加文字貼圖、匯出。" & vbCrLf & vbCrLf &
                    "列印用的版面（相片、證件照、紙張）依解析度換算像素；解析度也會寫進匯出的檔案。"}
        hint.SetBounds(468, 264, 220, 150)

        Dim cancel As New Button With {.Text = "取消", .DialogResult = DialogResult.Cancel}
        _ok.SetBounds(ClientSize.Width - 200, ClientSize.Height - 40, 90, 28)
        cancel.SetBounds(ClientSize.Width - 102, ClientSize.Height - 40, 90, 28)
        AddHandler _ok.Click, AddressOf OnOk
        AcceptButton = _ok
        CancelButton = cancel

        Controls.AddRange(New Control() {fillGroup, pictureGroup, _sizeGroup, dpiCaption, _dpiPreset, _dpi, _dpiUnit, _fileSize,
                                         previewCaption, _previewBox, hint, _ok, cancel})
    End Sub

    Private Shared Sub EnableDoubleBuffer(c As Control)
        GetType(Control).GetProperty("DoubleBuffered", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).SetValue(c, True)
    End Sub

    Private Sub LoadSettings()
        _syncing = True
        Try
            _spec.Dpi = _lastDpi
            _spec.CustomColor = _lastColor
            _colorButton.BackColor = _lastColor
            Select Case _lastFill
                Case NewImageFill.Black : _rbBlack.Checked = True
                Case NewImageFill.Custom : _rbColor.Checked = True
                Case NewImageFill.Transparent : _rbTransparent.Checked = True
                Case Else : _rbWhite.Checked = True
            End Select
            _spec.Fill = _lastFill
            _fit.SelectedIndex = CInt(_lastFit)
            _unit.SelectedIndex = CInt(_lastUnit)
            _dpiUnit.SelectedIndex = If(_lastDpiPerCm, 1, 0)
            Dim preset = CanvasPreset.BuiltIn(Math.Max(0, Math.Min(CanvasPreset.BuiltIn.Count - 1, _lastPreset)))
            _category.SelectedItem = preset.Category
            FillPresetList(preset.Category)
            _presetBox.SelectedItem = preset
            _landscape = preset.Width > preset.Height
            _spec.Width = _lastCustom.Width
            _spec.Height = _lastCustom.Height
        Finally
            _syncing = False
        End Try

        Dim startMode = _lastMode
        If startMode = SizeMode.Current AndAlso Not _currentSize.HasValue Then startMode = SizeMode.Standard
        If startMode = SizeMode.Clipboard AndAlso Not _clipboardSize.HasValue Then startMode = SizeMode.Standard
        If startMode = SizeMode.Picture Then startMode = SizeMode.Standard
        RadioFor(startMode).Checked = True
        OnModeChanged()
    End Sub

    Private Function RadioFor(mode As SizeMode) As RadioButton
        Select Case mode
            Case SizeMode.Current : Return _rbCurrent
            Case SizeMode.Clipboard : Return _rbClipboard
            Case SizeMode.Picture : Return _rbPicture
            Case SizeMode.Custom : Return _rbCustom
            Case Else : Return _rbStandard
        End Select
    End Function

    Private ReadOnly Property Mode As SizeMode
        Get
            If _rbCurrent.Checked Then Return SizeMode.Current
            If _rbClipboard.Checked Then Return SizeMode.Clipboard
            If _rbPicture.Checked Then Return SizeMode.Picture
            If _rbCustom.Checked Then Return SizeMode.Custom
            Return SizeMode.Standard
        End Get
    End Property

    Private ReadOnly Property Unit As SizeUnit
        Get
            Return CType(Math.Max(0, _unit.SelectedIndex), SizeUnit)
        End Get
    End Property

    '---------------------------------------------------------------------
    ' 事件
    '---------------------------------------------------------------------

    Private Sub OnFillChanged()
        If _syncing Then Return
        _spec.Fill = If(_rbBlack.Checked, NewImageFill.Black, If(_rbColor.Checked, NewImageFill.Custom,
                     If(_rbTransparent.Checked, NewImageFill.Transparent, NewImageFill.White)))
        UpdateAll()
    End Sub

    Private Sub PickColor()
        Using dlg As New ColorDialog With {.Color = _spec.CustomColor, .FullOpen = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _spec.CustomColor = dlg.Color
            _colorButton.BackColor = dlg.Color
        End Using
        If _rbColor.Checked Then UpdateAll() Else _rbColor.Checked = True
    End Sub

    Private Sub OnCategoryChanged()
        If _syncing OrElse _category.SelectedItem Is Nothing Then Return
        FillPresetList(CStr(_category.SelectedItem))
        _presetBox.SelectedIndex = 0
    End Sub

    Private Sub FillPresetList(category As String)
        Dim was = _syncing
        _syncing = True
        Try
            _presetBox.Items.Clear()
            _presetBox.Items.AddRange(CanvasPreset.BuiltIn.Where(Function(p) p.Category = category).Cast(Of Object)().ToArray())
        Finally
            _syncing = was
        End Try
    End Sub

    Private Sub OnPresetChanged()
        If _syncing Then Return
        Dim p = TryCast(_presetBox.SelectedItem, CanvasPreset)
        If p Is Nothing Then Return
        _landscape = p.Width > p.Height
        _rbStandard.Checked = True
        UpdateAll()
    End Sub

    Private Sub OnModeChanged()
        If _syncing Then Return
        ' 進入自訂時，從目前大小開始改。
        If Mode = SizeMode.Custom Then _ratio = _spec.Width / CDbl(Math.Max(1, _spec.Height))
        UpdateAll()
    End Sub

    Private Sub OnCustomValueChanged(widthChanged As Boolean)
        If _syncing Then Return
        If Not _rbCustom.Checked Then
            _syncing = True ' 不要讓切換模式把剛輸入的數字蓋回去
            _rbCustom.Checked = True
            _syncing = False
        End If
        If _lockRatio.Checked AndAlso _ratio > 0 Then
            _syncing = True
            Try
                If widthChanged Then
                    _height.Value = Clamp(_height, CDec(Math.Round(CDbl(_width.Value) / _ratio, _height.DecimalPlaces)))
                Else
                    _width.Value = Clamp(_width, CDec(Math.Round(CDbl(_height.Value) * _ratio, _width.DecimalPlaces)))
                End If
            Finally
                _syncing = False
            End Try
        End If
        _spec.Width = NewImage.ToPixels(CDbl(_width.Value), Unit, _spec.Dpi)
        _spec.Height = NewImage.ToPixels(CDbl(_height.Value), Unit, _spec.Dpi)
        If Not _lockRatio.Checked Then _ratio = _spec.Width / CDbl(_spec.Height)
        UpdateAll()
    End Sub

    Private Sub OnUnitChanged()
        If _syncing Then Return
        UpdateAll() ' 像素不變，自訂欄位換成新單位顯示
    End Sub

    Private Sub SetDpi(dpi As Double)
        _spec.Dpi = Math.Max(1, Math.Min(30000, dpi))
        ' 自訂且用實體單位時，保持實體尺寸、重算像素（和 Photoshop 相同）。
        If Mode = SizeMode.Custom AndAlso Unit <> SizeUnit.Pixels Then
            _spec.Width = NewImage.ToPixels(CDbl(_width.Value), Unit, _spec.Dpi)
            _spec.Height = NewImage.ToPixels(CDbl(_height.Value), Unit, _spec.Dpi)
        End If
        UpdateAll()
    End Sub

    Private Sub SetOrientation(landscape As Boolean)
        Select Case Mode
            Case SizeMode.Standard
                _landscape = landscape
            Case SizeMode.Custom
                If (_spec.Width > _spec.Height) <> landscape AndAlso _spec.Width <> _spec.Height Then
                    Dim t = _spec.Width : _spec.Width = _spec.Height : _spec.Height = t
                    _ratio = _spec.Width / CDbl(_spec.Height)
                End If
            Case Else
                ' 固定大小的選項：改成自訂並轉向。
                Dim w = _spec.Width, h = _spec.Height
                _syncing = True
                _rbCustom.Checked = True
                _syncing = False
                If (w > h) <> landscape Then _spec.Width = h : _spec.Height = w
                _ratio = _spec.Width / CDbl(_spec.Height)
        End Select
        UpdateAll()
    End Sub

    Private Sub PickPictureFile()
        Using dlg As New OpenFileDialog With {.Title = "選擇底圖",
            .Filter = "圖片檔|" & String.Join(";", PhotoFile.SupportedExtensions.Select(Function(x) "*" & x)) & "|所有檔案|*.*"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using photo = PhotoFile.Open(dlg.FileName) ' 依 EXIF 轉正
                    SetPicture(DirectCast(photo.Image.Clone(), Bitmap), Path.GetFileName(dlg.FileName))
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                       TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse
                                       TypeOf ex Is ExternalException
                MessageBox.Show(Me, $"無法開啟「{Path.GetFileName(dlg.FileName)}」：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    ''' <summary>換底圖（picture 交給本視窗管理）。第一次放底圖時，大小改成和底圖相同，比較符合「直接帶入」的用法。</summary>
    Private Sub SetPicture(picture As Bitmap, name As String)
        Dim hadPicture = _picture IsNot Nothing
        _picture?.Dispose()
        _picture = picture
        _pictureName = If(picture Is Nothing, Nothing, name)
        _spec.Picture = picture
        If picture Is Nothing Then
            If Mode = SizeMode.Picture Then _rbStandard.Checked = True
        ElseIf Not hadPicture Then
            _rbPicture.Checked = True
        End If
        UpdateAll()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        Dim problem = NewImage.Validate(_spec)
        If problem IsNot Nothing Then
            MessageBox.Show(Me, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End If
        _lastFill = _spec.Fill
        _lastColor = _spec.CustomColor
        _lastDpi = _spec.Dpi
        _lastDpiPerCm = _dpiUnit.SelectedIndex = 1
        _lastUnit = Unit
        _lastFit = _spec.Fit
        _lastMode = Mode
        Dim p = TryCast(_presetBox.SelectedItem, CanvasPreset)
        If p IsNot Nothing Then _lastPreset = CanvasPreset.BuiltIn.ToList().IndexOf(p)
        If Mode = SizeMode.Custom Then _lastCustom = New Size(_spec.Width, _spec.Height)
    End Sub

    ''' <summary>依設定建立全尺寸畫布（呼叫端負責釋放）。</summary>
    Public Function CreateImage() As Bitmap
        Return NewImage.Create(_spec)
    End Function

    '---------------------------------------------------------------------
    ' 更新畫面
    '---------------------------------------------------------------------

    ''' <summary>依目前選項算出像素大小，同步所有欄位與預覽。</summary>
    Private Sub UpdateAll()
        _syncing = True
        Try
            _spec.Fit = CType(Math.Max(0, _fit.SelectedIndex), PictureFit)
            Select Case Mode
                Case SizeMode.Standard
                    Dim p = TryCast(_presetBox.SelectedItem, CanvasPreset)
                    If p IsNot Nothing Then
                        Dim s = p.PixelSize(_spec.Dpi)
                        Dim longSide = Math.Max(s.Width, s.Height), shortSide = Math.Min(s.Width, s.Height)
                        _spec.Width = If(_landscape, longSide, shortSide)
                        _spec.Height = If(_landscape, shortSide, longSide)
                    End If
                Case SizeMode.Current
                    _spec.Width = _currentSize.Value.Width : _spec.Height = _currentSize.Value.Height
                Case SizeMode.Clipboard
                    _spec.Width = _clipboardSize.Value.Width : _spec.Height = _clipboardSize.Value.Height
                Case SizeMode.Picture
                    If _picture IsNot Nothing Then _spec.Width = _picture.Width : _spec.Height = _picture.Height
            End Select

            ' 自訂欄位：顯示目前大小（換算成選擇的單位）。
            Dim decimals = If(Unit = SizeUnit.Pixels, 0, If(Unit = SizeUnit.Millimeters, 1, 2))
            _width.DecimalPlaces = decimals : _height.DecimalPlaces = decimals
            _width.Increment = If(decimals = 2, 0.1D, 1D)
            _height.Increment = _width.Increment
            _width.Value = Clamp(_width, CDec(Math.Round(NewImage.FromPixels(_spec.Width, Unit, _spec.Dpi), decimals)))
            _height.Value = Clamp(_height, CDec(Math.Round(NewImage.FromPixels(_spec.Height, Unit, _spec.Dpi), decimals)))

            Dim custom = Mode = SizeMode.Custom
            _width.ForeColor = If(custom, SystemColors.WindowText, SystemColors.GrayText)
            _height.ForeColor = _width.ForeColor
            _category.Enabled = True : _presetBox.Enabled = True
            Dim canOrient = Mode = SizeMode.Standard OrElse custom OrElse _spec.Width <> _spec.Height
            _portrait.Enabled = canOrient : _landscapeButton.Enabled = canOrient
            Dim isLandscape = _spec.Width > _spec.Height
            _portrait.Font = If(Not isLandscape AndAlso _spec.Width <> _spec.Height, _boldFont, Font)
            _landscapeButton.Font = If(isLandscape, _boldFont, Font)

            _rbCurrent.Text = "使用中影像(&A)" & If(_currentSize.HasValue, $"（{_currentSize.Value.Width} x {_currentSize.Value.Height}）", "")
            _rbCurrent.Enabled = _currentSize.HasValue
            _rbClipboard.Text = "和剪貼簿內的影像相同(&C)" & If(_clipboardSize.HasValue, $"（{_clipboardSize.Value.Width} x {_clipboardSize.Value.Height}）", "")
            _rbClipboard.Enabled = _clipboardSize.HasValue
            _rbPicture.Text = "和底圖相同(&P)" & If(_picture IsNot Nothing, $"（{_picture.Width} x {_picture.Height}）", "")
            _rbPicture.Enabled = _picture IsNot Nothing

            _pictureLabel.Text = If(_picture Is Nothing, "（無）— 只用底色", $"{_pictureName}　{_picture.Width} x {_picture.Height}")
            _clearPicture.Enabled = _picture IsNot Nothing
            _fit.Enabled = _picture IsNot Nothing

            ' 解析度
            Dim perCm = _dpiUnit.SelectedIndex = 1
            _dpi.DecimalPlaces = If(perCm, 2, 0)
            _dpi.Value = Clamp(_dpi, CDec(Math.Round(If(perCm, _spec.Dpi / 2.54, _spec.Dpi), _dpi.DecimalPlaces)))
            Dim match = Array.FindIndex(DpiPresets, Function(d) d.Dpi > 0 AndAlso Math.Abs(d.Dpi - _spec.Dpi) < 0.01)
            _dpiPreset.SelectedIndex = Math.Max(0, match)

            _sizeGroup.Text = $"影像大小 {_spec.Width} x {_spec.Height} 像素"
            Dim inchW = _spec.Width / _spec.Dpi, inchH = _spec.Height / _spec.Dpi
            Dim problem = NewImage.Validate(_spec)
            _fileSize.ForeColor = If(problem Is Nothing, SystemColors.ControlText, Color.Firebrick)
            _fileSize.Text = If(problem, $"檔案大小：{NewImage.FormatFileSize(_spec.UncompressedBytes)}　｜　列印 {inchW * 2.54:0.0} x {inchH * 2.54:0.0} 公分（{inchW:0.##} x {inchH:0.##} 英吋）")
            _ok.Enabled = problem Is Nothing
        Finally
            _syncing = False
        End Try
        RenderPreview()
    End Sub

    Private Shared Function Clamp(box As NumericUpDown, value As Decimal) As Decimal
        Return Math.Max(box.Minimum, Math.Min(box.Maximum, value))
    End Function

    Private Sub RenderPreview()
        _preview?.Dispose()
        _preview = Nothing
        If NewImage.Validate(_spec) Is Nothing Then _preview = NewImage.Create(_spec, _previewBox.ClientSize.Width - 16)
        _previewBox.Invalidate()
    End Sub

    Private Sub PaintPreview(sender As Object, e As PaintEventArgs)
        If _preview Is Nothing Then Return
        Dim box = _previewBox.ClientRectangle
        Dim r As New Rectangle((box.Width - _preview.Width) \ 2, (box.Height - _preview.Height) \ 2, _preview.Width, _preview.Height)
        ' 棋盤格表示透明。
        Using light As New SolidBrush(Color.White), dark As New SolidBrush(Color.FromArgb(214, 214, 214))
            e.Graphics.FillRectangle(light, r)
            Const cell = 8
            For y = r.Top To r.Bottom - 1 Step cell
                For x = r.Left To r.Right - 1 Step cell
                    If ((x - r.Left) \ cell + (y - r.Top) \ cell) Mod 2 = 1 Then
                        e.Graphics.FillRectangle(dark, x, y, Math.Min(cell, r.Right - x), Math.Min(cell, r.Bottom - y))
                    End If
                Next
            Next
        End Using
        e.Graphics.DrawImage(_preview, r)
        e.Graphics.DrawRectangle(Pens.DimGray, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1)
    End Sub
End Class
