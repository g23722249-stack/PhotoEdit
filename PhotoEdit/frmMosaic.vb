Imports PhotoEdit

''' <summary>
''' 打馬賽克／平滑馬賽克（選取 → 打馬賽克…、平滑馬賽克…）：左邊原本、右邊結果（選取範圍附近）。
''' 打馬賽克調方塊大小；平滑馬賽克先自動偵測方塊大小與起點（可以手動改），再選放大方法。
''' 按「套用」後由主畫面加成一個點陣圖層（隨時可以隱藏、刪除，原照片不變）。
''' </summary>
Friend Class frmMosaic
    Inherits Aqua.AquaForm

    Private Const PreviewW As Integer = 360
    Private Const PreviewH As Integer = 360
    Private ReadOnly _source As Byte()
    Private ReadOnly _w, _h As Integer
    Private ReadOnly _mask As Byte()
    Private ReadOnly _area As Rectangle
    Private ReadOnly _smooth As Boolean
    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _before As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(30, 30, 32)}
    Private ReadOnly _after As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(30, 30, 32)}
    Private ReadOnly _size As New NumericUpDown With {.Minimum = 2, .Maximum = 256}
    Private ReadOnly _offX As New NumericUpDown With {.Minimum = 0, .Maximum = 255}
    Private ReadOnly _offY As New NumericUpDown With {.Minimum = 0, .Maximum = 255}
    Private ReadOnly _method As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _detect As New Button With {.Text = Lang.T("重新偵測")}
    Private ReadOnly _info As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _note As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
    Private _syncing As Boolean

    Public Shared ReadOnly MethodNames As String() = {Lang.T("平滑（雙三次）"), Lang.T("xBR（像素圖）"), Lang.T("HQx（像素圖）")}

    ''' <summary>方塊大小（像素）。</summary>
    Public ReadOnly Property BlockSize As Integer
        Get
            Return CInt(_size.Value)
        End Get
    End Property
    ''' <summary>格線起點（像素，0..大小−1）。</summary>
    Public ReadOnly Property OffsetX As Integer
        Get
            Return CInt(_offX.Value) Mod BlockSize
        End Get
    End Property
    Public ReadOnly Property OffsetY As Integer
        Get
            Return CInt(_offY.Value) Mod BlockSize
        End Get
    End Property
    Public ReadOnly Property Method As MosaicSmoothMethod
        Get
            Return CType(Math.Max(0, _method.SelectedIndex), MosaicSmoothMethod)
        End Get
    End Property

    ''' <param name="smooth">True＝平滑馬賽克；False＝打馬賽克。</param>
    ''' <param name="area">選取範圍的外框（像素）。</param>
    Public Sub New(source As Bitmap, mask As Byte(), area As Rectangle, smooth As Boolean, help As HelpTip)
        _w = source.Width : _h = source.Height
        _source = ReadPixels(source)
        _mask = mask
        _area = area
        _smooth = smooth
        Text = If(smooth, Lang.T("平滑馬賽克"), Lang.T("打馬賽克"))
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        RoundCorners = False
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        Dim top = 23 + 12
        Dim rightX = 16 + PreviewW * 2 + 24
        ClientSize = New Size(rightX + 260, top + PreviewH + 76)
        Dim capBefore As New Label With {.Text = Lang.T("原本"), .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16, top)}
        Dim capAfter As New Label With {.Text = If(smooth, Lang.T("平滑之後"), Lang.T("打馬賽克之後")), .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16 + PreviewW + 12, top)}
        _before.SetBounds(16, top + 24, PreviewW, PreviewH)
        _after.SetBounds(16 + PreviewW + 12, top + 24, PreviewW, PreviewH)
        Controls.AddRange({capBefore, capAfter, _before, _after})

        Dim y = top + 24
        Dim addRow = Sub(caption As String, c As Control, helpKey As String, width As Integer)
                         Dim l As New Label With {.Text = caption, .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(rightX, y + 4)}
                         c.SetBounds(rightX + 90, y, width, 26)
                         Controls.AddRange({l, c})
                         help.SetHelpLinked(helpKey, c, l, c)
                         y += 36
                     End Sub
        _size.Maximum = Math.Max(8, Math.Min(256, Math.Min(_w, _h) \ 2))
        addRow(Lang.T("方塊大小"), _size, If(smooth, "mosaic.detected", "mosaic.size"), 80)
        If smooth Then
            addRow(Lang.T("起點 X"), _offX, "mosaic.offset", 80)
            addRow(Lang.T("起點 Y"), _offY, "mosaic.offset", 80)
            _method.Items.AddRange(MethodNames.Cast(Of Object)().ToArray())
            _method.SelectedIndex = 0
            addRow(Lang.T("放大方法"), _method, "mosaic.method", 150)
            _detect.SetBounds(rightX + 90, y, 110, 28)
            help.SetHelp("mosaic.detect", _detect)
            AddHandler _detect.Click, Sub() RunDetect()
            Controls.Add(_detect)
            y += 38
        End If
        _info.SetBounds(rightX, y, 246, 44)
        _note.SetBounds(rightX, y + 48, 246, top + 24 + PreviewH - y - 48)
        _note.Text = If(smooth, Lang.T("每一格縮成一個像素再平滑放大，方塊感會消失。方塊裡原本的細節已經被平均掉了，不會回來——這不是還原。"),
                        Lang.T("提醒：馬賽克的方塊太小時，文字、車牌有機會被猜回來；要遮敏感資訊，用「安全遮蔽（實心色塊）」比較安全。"))
        Controls.AddRange({_info, _note})

        Dim ok As New Button With {.Text = Lang.T("套用")}
        Dim cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel}
        ok.SetBounds(ClientSize.Width - 232, ClientSize.Height - 46, 110, 32)
        cancel.SetBounds(ClientSize.Width - 112, ClientSize.Height - 46, 96, 32)
        AddHandler ok.Click, Sub() DialogResult = DialogResult.OK
        Controls.AddRange({ok, cancel})
        AcceptButton = ok : CancelButton = cancel

        _syncing = True
        If smooth Then
            RunDetect(updatePreview:=False)
        Else
            _size.Value = Math.Max(_size.Minimum, Math.Min(_size.Maximum, Math.Max(4, Math.Min(_w, _h) \ 60)))
        End If
        _syncing = False
        For Each c In New Control() {_size, _offX, _offY}
            AddHandler CType(c, NumericUpDown).ValueChanged, Sub()
                                                                 If Not _syncing Then UpdatePreview()
                                                             End Sub
        Next
        AddHandler _method.SelectedIndexChanged, Sub()
                                                     If Not _syncing Then UpdatePreview()
                                                 End Sub
        ThemeManager.Attach(Me)
        Using b = CropBitmap(_source)
            _before.Image = DirectCast(b.Clone(), Bitmap)
        End Using
        UpdatePreview()
    End Sub

    Private Shared Function ReadPixels(bmp As Bitmap) As Byte()
        Dim w = bmp.Width, h = bmp.Height
        Using copy = bmp.Clone(New Rectangle(0, 0, w, h), Imaging.PixelFormat.Format32bppArgb)
            Dim d = copy.LockBits(New Rectangle(0, 0, w, h), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb)
            Dim px(w * h * 4 - 1) As Byte
            For y = 0 To h - 1
                Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4)
            Next
            copy.UnlockBits(d)
            Return px
        End Using
    End Function

    ''' <summary>偵測選取範圍裡的方塊大小與起點，填進欄位。</summary>
    Private Sub RunDetect(Optional updatePreview As Boolean = True)
        Dim r = MosaicEffect.Detect(_source, _w, _h, _area)
        Dim was = _syncing
        _syncing = True
        If r.Size >= 2 Then
            _size.Value = Math.Max(_size.Minimum, Math.Min(_size.Maximum, r.Size))
            _offX.Value = Math.Min(_offX.Maximum, r.OffsetX) : _offY.Value = Math.Min(_offY.Maximum, r.OffsetY)
            _info.Text = Lang.F("偵測到方塊 {0} 像素，起點 ({1}, {2})；明顯程度 {3:P0}。", r.Size, r.OffsetX, r.OffsetY, r.Score)
            _info.ForeColor = If(r.Score < 0.6, Color.FromArgb(200, 120, 20), ForeColor)
        Else
            _info.Text = Lang.T("沒有偵測到馬賽克方塊，請手動輸入方塊大小。")
            _info.ForeColor = Color.FromArgb(200, 120, 20)
        End If
        _syncing = was
        If updatePreview Then Me.UpdatePreview()
    End Sub

    ''' <summary>預覽看選取範圍附近（外框加一圈），算在這一塊上。</summary>
    Private ReadOnly Property ViewRect As Rectangle
        Get
            Dim m = Math.Max(BlockSize * 2, 8)
            Dim r = Rectangle.Inflate(_area, m, m)
            r.Intersect(New Rectangle(0, 0, _w, _h))
            Return r
        End Get
    End Property

    Private Function CropBitmap(px As Byte()) As Bitmap
        Dim r = ViewRect
        Dim b As New Bitmap(r.Width, r.Height, Imaging.PixelFormat.Format32bppArgb)
        Dim d = b.LockBits(New Rectangle(0, 0, r.Width, r.Height), Imaging.ImageLockMode.WriteOnly, Imaging.PixelFormat.Format32bppArgb)
        For y = 0 To r.Height - 1
            Runtime.InteropServices.Marshal.Copy(px, ((r.Y + y) * _w + r.X) * 4, d.Scan0 + y * d.Stride, r.Width * 4)
        Next
        b.UnlockBits(d)
        Return b
    End Function

    Private Sub UpdatePreview()
        Dim patch = If(_smooth, MosaicEffect.Smooth(_source, _w, _h, _mask, BlockSize, OffsetX, OffsetY, Method),
                       MosaicEffect.Pixelate(_source, _w, _h, _mask, BlockSize, _area.X, _area.Y))
        ' 結果疊在原本上面
        Dim merged = DirectCast(_source.Clone(), Byte())
        For i = 0 To merged.Length - 1 Step 4
            Dim a = patch(i + 3)
            If a = 0 Then Continue For
            For c = 0 To 2
                merged(i + c) = CByte((CInt(patch(i + c)) * a + CInt(merged(i + c)) * (255 - a)) \ 255)
            Next
        Next
        Dim old = _after.Image
        _after.Image = CropBitmap(merged)
        old?.Dispose()
        If Not _smooth Then _info.Text = Lang.F("方塊 {0} 像素（選取範圍 {1} × {2}）。", BlockSize, _area.Width, _area.Height)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _before.Image?.Dispose() : _after.Image?.Dispose()
            _font.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
