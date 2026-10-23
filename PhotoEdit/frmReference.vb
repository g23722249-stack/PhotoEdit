Imports PhotoEdit
Imports System.Drawing.Drawing2D

''' <summary>
''' 參考圖（檢視 → 參考圖…，Ctrl+Shift+R）：臨摹時把原作開在旁邊的浮動視窗，不必切換程式。
''' 左鍵吸色設成線條色、右鍵設成填色；滾輪縮放、拖曳移動、按兩下看全圖。
''' 「描圖」把參考圖半透明疊在畫布上對位用，只顯示在畫面上，不會存進文件或匯出。
''' </summary>
Friend Class frmReference
    Inherits Aqua.AquaForm

    Private ReadOnly _settings As AppSettings
    Private ReadOnly _view As New RefView()
    Private ReadOnly _open As New Button With {.Text = Lang.T("開啟圖片…"), .UseVisualStyleBackColor = True}
    Private ReadOnly _trace As New CheckBox With {.Text = Lang.T("描圖"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter, .UseVisualStyleBackColor = True}
    Private ReadOnly _inkTone As New CheckBox With {.Text = Lang.T("墨色"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter, .UseVisualStyleBackColor = True}
    Private ReadOnly _pick As New CheckBox With {.Text = Lang.T("畫筆取色"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter, .UseVisualStyleBackColor = True}
    Private ReadOnly _pickSize As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private Shared ReadOnly PickSizes As Integer() = {3, 7, 15}
    Private ReadOnly _tips As New ToolTip()
    Private ReadOnly _opacity As New Aqua.Slider With {.Minimum = 10, .Maximum = 80, .ShowTicks = False}
    Private ReadOnly _opacityLabel As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _status As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent,
                                               .ForeColor = Color.FromArgb(105, 110, 120)}
    Private ReadOnly _font As New Font(Lang.UiFont, 10)

    ''' <summary>點一下參考圖吸到的顏色。</summary>
    Public Event ColorPicked(c As Color)
    ''' <summary>按右鍵吸到的顏色（設成填色）。</summary>
    Public Event FillColorPicked(c As Color)
    ''' <summary>描圖開關、不透明度或圖片換了：畫布要重畫。</summary>
    Public Event TraceChanged()

    Public Sub New(settings As AppSettings)
        _settings = settings
        Text = Lang.T("參考圖")
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Sizable
        RoundCorners = False ' 小工具視窗：方角（圓角的描線會被裁掉一半）
        MinButton = False
        StartPosition = FormStartPosition.Manual
        ShowInTaskbar = False
        ClientSize = New Size(560, 420)
        MinimumSize = New Size(560, 260)

        AddHandler _open.Click, Sub() OpenWithDialog()
        AddHandler _trace.CheckedChanged, Sub()
                                              _trace.BackColor = If(_trace.Checked, Color.FromArgb(255, 214, 120), If(ThemeManager.Dark, ThemeManager.ButtonBack, SystemColors.Control))
                                              _trace.ForeColor = If(_trace.Checked OrElse Not ThemeManager.Dark, SystemColors.ControlText, Aqua.Theme.TextColor)
                                              RaiseEvent TraceChanged()
                                          End Sub
        _pick.Checked = _settings.ReferenceColorPick
        _inkTone.Checked = _settings.ReferenceInkTone
        _view.Grayscale = _inkTone.Checked
        StylePick()
        AddHandler _inkTone.CheckedChanged, Sub()
                                                StylePick()
                                                _view.Grayscale = _inkTone.Checked
                                                _view.Invalidate()
                                                _settings.ReferenceInkTone = _inkTone.Checked
                                                _settings.Save()
                                                _status.Text = If(_inkTone.Checked, Lang.T("墨色：吸到的顏色換成同樣深淺的墨色（水墨畫用）。"),
                                                                  Lang.T("左鍵吸線條色、右鍵吸填色；滾輪縮放、拖曳移動、按兩下看全圖。"))
                                            End Sub
        AddHandler _pick.CheckedChanged, Sub()
                                             StylePick()
                                             _settings.ReferenceColorPick = _pick.Checked
                                             _settings.Save()
                                             _status.Text = If(_pick.Checked, Lang.T("畫筆取色：每一筆自動用參考圖同一位置的顏色。"),
                                                               Lang.T("左鍵吸線條色、右鍵吸填色；滾輪縮放、拖曳移動、按兩下看全圖。"))
                                         End Sub
        ' 吸色的取樣範圍：老畫的龜裂紋、照片的雜點只有幾個像素寬，範圍大一點、去掉最暗最亮的再平均，就吸不到它們
        _pickSize.Items.AddRange(PickSizes.Select(Function(v) CObj($"{v}×{v}")).ToArray())
        Dim sizeIndex = Array.IndexOf(PickSizes, _settings.ReferencePickSize)
        _pickSize.SelectedIndex = If(sizeIndex < 0, 1, sizeIndex)
        _view.SampleSize = PickSizes(_pickSize.SelectedIndex)
        _tips.SetToolTip(_pickSize, Lang.T("吸色範圍（像素）：範圍內去掉最暗、最亮的一部分再平均，老畫的裂紋、照片的雜點吸不到。要吸很細的線或小點時選 3×3。"))
        AddHandler _pickSize.SelectedIndexChanged, Sub()
                                                       _view.SampleSize = PickSizes(_pickSize.SelectedIndex)
                                                       _settings.ReferencePickSize = _view.SampleSize
                                                       _settings.Save()
                                                       _status.Text = Lang.F("吸色範圍 {0}×{0}：去掉最暗、最亮的再平均，吸不到裂紋與雜點（3×3 吸細節）。", _view.SampleSize)
                                                   End Sub
        _opacity.Value = Math.Max(10, Math.Min(80, _settings.TraceOpacity))
        _opacityLabel.Text = _opacity.Value & "%"
        AddHandler _opacity.ValueChanged, Sub()
                                              _opacityLabel.Text = _opacity.Value & "%"
                                              _settings.TraceOpacity = _opacity.Value
                                              _settings.Save()
                                              If _trace.Checked Then RaiseEvent TraceChanged()
                                          End Sub
        AddHandler _view.PickedFill, Sub(c0)
                                         Dim c = ToInk(c0)
                                         _status.Text = Lang.F("已吸色 RGB({0}, {1}, {2})，設成填色。", c.R, c.G, c.B)
                                         RaiseEvent FillColorPicked(c)
                                     End Sub
        AddHandler _view.Picked, Sub(c0)
                                     Dim c = ToInk(c0)
                                     _status.Text = Lang.F("已吸色 RGB({0}, {1}, {2})，設成線條色。", c.R, c.G, c.B)
                                     RaiseEvent ColorPicked(c)
                                 End Sub
        _view.AllowDrop = True
        AddHandler _view.DragEnter, Sub(s, e) e.Effect = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
        AddHandler _view.DragDrop, Sub(s, e)
                                       Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
                                       If files IsNot Nothing AndAlso files.Length > 0 Then LoadImage(files(0))
                                   End Sub
        Controls.AddRange({_open, _trace, _pick, _pickSize, _inkTone, _opacity, _opacityLabel, _view, _status})
        _status.Text = Lang.T("左鍵吸線條色、右鍵吸填色；滾輪縮放、拖曳移動、按兩下看全圖。")
        LayoutBody()
        ThemeManager.Attach(Me)
        If Not String.IsNullOrEmpty(_settings.ReferencePath) AndAlso IO.File.Exists(_settings.ReferencePath) Then LoadImage(_settings.ReferencePath, quiet:=True)
    End Sub

    ''' <summary>參考圖（描圖用）；沒有開圖時為 Nothing。</summary>
    Public ReadOnly Property RefImage As Bitmap
        Get
            Return _view.Image
        End Get
    End Property

    Public ReadOnly Property Tracing As Boolean
        Get
            Return _trace.Checked AndAlso _view.Image IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property TraceOpacity As Single
        Get
            Return _opacity.Value / 100.0F
        End Get
    End Property

    ''' <summary>「畫筆取色」開著、而且有圖。</summary>
    Public ReadOnly Property PickWhileDrawing As Boolean
        Get
            Return _pick.Checked AndAlso _view.Image IsNot Nothing
        End Get
    End Property

    ''' <summary>參考圖上 (nx, ny)（0..1）附近的顏色（取樣範圍見「取樣」下拉，去掉最暗、最亮的再平均）；超出範圍時 Nothing。</summary>
    Public Function ColorAt(nx As Single, ny As Single) As Color?
        Dim img = _view.Image
        If img Is Nothing OrElse nx < 0 OrElse ny < 0 OrElse nx >= 1 OrElse ny >= 1 Then Return Nothing
        Return ToInk(_view.SampleAt(CInt(Math.Floor(nx * img.Width)), CInt(Math.Floor(ny * img.Height))))
    End Function

    ''' <summary>「墨色」開著時：換成同樣亮度的墨色（略帶暖，像墨在紙上）；關著時原樣。</summary>
    Private Function ToInk(c As Color) As Color
        If Not _inkTone.Checked Then Return c
        Dim l = 0.3 * c.R + 0.59 * c.G + 0.11 * c.B
        Return Color.FromArgb(CInt(Math.Min(255, l)), CInt(Math.Min(255, l * 0.985)), CInt(Math.Min(255, l * 0.95)))
    End Function

    Private Sub StylePick()
        _pick.BackColor = If(_pick.Checked, Color.FromArgb(255, 214, 120), If(ThemeManager.Dark, ThemeManager.ButtonBack, SystemColors.Control))
        _pick.ForeColor = If(_pick.Checked OrElse Not ThemeManager.Dark, SystemColors.ControlText, Aqua.Theme.TextColor)
        _inkTone.BackColor = If(_inkTone.Checked, Color.FromArgb(255, 214, 120), If(ThemeManager.Dark, ThemeManager.ButtonBack, SystemColors.Control))
        _inkTone.ForeColor = If(_inkTone.Checked OrElse Not ThemeManager.Dark, SystemColors.ControlText, Aqua.Theme.TextColor)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        LayoutBody()
    End Sub

    Private Sub LayoutBody()
        If _view Is Nothing Then Return ' AquaForm 的建構式會先觸發 OnResize，那時欄位還沒建立
        Dim top = 23 + 8, w = ClientSize.Width, h = ClientSize.Height
        _open.SetBounds(10, top, 104, 28)
        _trace.SetBounds(120, top, 64, 28)
        _pick.SetBounds(190, top, 84, 28)
        _pickSize.SetBounds(278, top + 3, 72, 24)
        _inkTone.SetBounds(356, top, 56, 28)
        _opacity.SetBounds(418, top + 2, Math.Max(40, w - 418 - 58), 24)
        _opacityLabel.SetBounds(w - 56, top, 46, 28)
        _view.SetBounds(10, top + 34, w - 20, Math.Max(40, h - top - 34 - 30))
        _status.SetBounds(10, h - 28, w - 20, 24)
    End Sub

    Private Sub OpenWithDialog()
        Using dlg As New OpenFileDialog With {.Title = Lang.T("開啟參考圖"), .Filter = Lang.T("圖片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有檔案|*.*")}
            If Not String.IsNullOrEmpty(_settings.ReferencePath) Then
                Try
                    dlg.InitialDirectory = IO.Path.GetDirectoryName(_settings.ReferencePath)
                Catch ex As ArgumentException
                End Try
            End If
            If dlg.ShowDialog(Me) = DialogResult.OK Then LoadImage(dlg.FileName)
        End Using
    End Sub

    Public Sub LoadImage(path As String, Optional quiet As Boolean = False)
        Dim bmp As Bitmap
        Try
            ' 讀進記憶體再關檔，圖片檔不會被鎖住。
            Using fs As New IO.FileStream(path, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite), img = Drawing.Image.FromStream(fs)
                bmp = New Bitmap(img)
            End Using
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is ArgumentException OrElse
                                   TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is OutOfMemoryException
            If Not quiet Then MessageBox.Show(Me, Lang.F("無法開啟「{0}」：{1}", IO.Path.GetFileName(path), ex.Message), Lang.T("參考圖"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        _view.Image = bmp
        _settings.ReferencePath = path
        _settings.Save()
        Text = Lang.T("參考圖 – ") & IO.Path.GetFileName(path)
        RaiseEvent TraceChanged()
    End Sub

    ''' <summary>關閉只是隱藏：圖片與位置保留，下次打開還在。</summary>
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            _trace.Checked = False
            Hide()
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _view.Image = Nothing
            _font.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>參考圖的檢視區：符合視窗或縮放、拖曳移動；點一下（沒有拖曳）回報那一點的顏色。</summary>
    Private Class RefView
        Inherits Control

        Private _image As Bitmap
        Private _zoom As Single ' 0 = 符合視窗
        Private _center As New PointF(0.5F, 0.5F)
        Private _down As Point?
        Private _dragged As Boolean
        Private _downCenter As PointF

        Public Event Picked(c As Color)
        Public Event PickedFill(c As Color)

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Cursor = Cursors.Cross
            AccessibleName = Lang.T("參考圖")
        End Sub

        Public Property Image As Bitmap
            Get
                Return _image
            End Get
            Set(value As Bitmap)
                If _image IsNot Nothing AndAlso _image IsNot value Then _image.Dispose()
                _image = value
                _zoom = 0
                _center = New PointF(0.5F, 0.5F)
                Invalidate()
            End Set
        End Property

        Private Function FitScale() As Single
            If _image Is Nothing Then Return 1
            Return Math.Min(Width / CSng(_image.Width), Height / CSng(_image.Height))
        End Function

        Private Function ImageRect() As RectangleF
            Dim s = If(_zoom = 0, FitScale(), _zoom)
            Dim w = _image.Width * s, h = _image.Height * s
            If _zoom = 0 Then Return New RectangleF((Width - w) / 2, (Height - h) / 2, w, h)
            Return New RectangleF(Width / 2.0F - _center.X * w, Height / 2.0F - _center.Y * h, w, h)
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(ThemeManager.Back(Color.FromArgb(52, 52, 56)))
            If _image Is Nothing Then
                TextRenderer.DrawText(g, Lang.T("按「開啟圖片…」或把圖片拖曳到這裡"), Font, ClientRectangle, Color.FromArgb(200, 204, 212),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
                Return
            End If
            g.InterpolationMode = If(If(_zoom = 0, FitScale(), _zoom) > 2, InterpolationMode.NearestNeighbor, InterpolationMode.HighQualityBilinear)
            g.PixelOffsetMode = PixelOffsetMode.Half
            If Grayscale Then
                ' 墨色：看濃淡用灰階顯示（吸色時才換算，原圖不變）
                Dim r = ImageRect()
                Using attr As New Imaging.ImageAttributes()
                    attr.SetColorMatrix(New Imaging.ColorMatrix(New Single()() {
                        New Single() {0.3F, 0.3F, 0.3F, 0, 0}, New Single() {0.59F, 0.59F, 0.59F, 0, 0}, New Single() {0.11F, 0.11F, 0.11F, 0, 0},
                        New Single() {0, 0, 0, 1, 0}, New Single() {0, 0, 0, 0, 1}}))
                    g.DrawImage(_image, New PointF() {r.Location, New PointF(r.Right, r.Top), New PointF(r.Left, r.Bottom)},
                                New RectangleF(0, 0, _image.Width, _image.Height), GraphicsUnit.Pixel, attr)
                End Using
            Else
                g.DrawImage(_image, ImageRect())
            End If
        End Sub

        ''' <summary>灰階顯示（「墨色」開著時）。</summary>
        Public Property Grayscale As Boolean

        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            MyBase.OnMouseWheel(e)
            If _image Is Nothing Then Return
            Dim b = ImageRect()
            ' 以游標所在的點為中心縮放
            Dim u = New PointF((e.X - b.X) / b.Width, (e.Y - b.Y) / b.Height)
            Dim cur = If(_zoom = 0, FitScale(), _zoom)
            Dim nz = CSng(Math.Max(FitScale() * 0.5, Math.Min(16, cur * If(e.Delta > 0, 1.25, 0.8))))
            Dim nw = _image.Width * nz, nh = _image.Height * nz
            _center = New PointF(u.X - (e.X - Width / 2.0F) / nw, u.Y - (e.Y - Height / 2.0F) / nh)
            _zoom = nz
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
            If _image IsNot Nothing AndAlso e.Button = MouseButtons.Right Then
                ' 右鍵：吸成填色（同工具列色盤：左鍵線條、右鍵填色）
                Dim ir = ImageRect()
                Dim px = CInt(Math.Floor((e.X - ir.X) / ir.Width * _image.Width)), py = CInt(Math.Floor((e.Y - ir.Y) / ir.Height * _image.Height))
                If px >= 0 AndAlso py >= 0 AndAlso px < _image.Width AndAlso py < _image.Height Then RaiseEvent PickedFill(SampleAt(px, py))
                Return
            End If
            If _image Is Nothing OrElse e.Button <> MouseButtons.Left Then Return
            _down = e.Location
            _dragged = False
            Dim b = ImageRect()
            _downCenter = If(_zoom = 0, New PointF((Width / 2.0F - b.X) / b.Width, (Height / 2.0F - b.Y) / b.Height), _center)
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If Not _down.HasValue Then Return
            Dim dx = e.X - _down.Value.X, dy = e.Y - _down.Value.Y
            If Not _dragged AndAlso Math.Abs(dx) + Math.Abs(dy) < 4 Then Return
            _dragged = True
            Cursor = Cursors.SizeAll
            Dim b = ImageRect()
            If _zoom = 0 Then _zoom = FitScale()
            _center = New PointF(_downCenter.X - dx / b.Width, _downCenter.Y - dy / b.Height)
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            If Not _down.HasValue Then Return
            _down = Nothing
            Cursor = Cursors.Cross
            If _dragged Then Return
            Dim b = ImageRect()
            Dim x = CInt(Math.Floor((e.X - b.X) / b.Width * _image.Width)), y = CInt(Math.Floor((e.Y - b.Y) / b.Height * _image.Height))
            If x < 0 OrElse y < 0 OrElse x >= _image.Width OrElse y >= _image.Height Then Return
            RaiseEvent Picked(SampleAt(x, y))
        End Sub

        ''' <summary>3×3 平均：避開單一雜點（油畫的筆觸、JPG 的雜訊）。</summary>
        ''' <summary>吸色的取樣範圍（邊長，像素）。</summary>
        Public Property SampleSize As Integer = 7

        Public Function SampleAt(x As Integer, y As Integer) As Color
            Return TrimmedMean(_image, x, y, SampleSize)
        End Function

        ''' <summary>
        ''' (x, y) 附近 size × size 的顏色：依亮度排序，去掉最暗的 30%、最亮的 10% 再平均（3×3 照舊直接平均，吸很小的細節用）。
        ''' 細的暗線（老畫的龜裂紋、髮絲）、反光、雜點落在範圍裡也不會把顏色拉走。
        ''' </summary>
        Public Shared Function TrimmedMean(img As Bitmap, x As Integer, y As Integer, size As Integer) As Color
            Dim h = Math.Max(0, size \ 2)
            Dim px As New List(Of Color)()
            For yy = Math.Max(0, y - h) To Math.Min(img.Height - 1, y + h)
                For xx = Math.Max(0, x - h) To Math.Min(img.Width - 1, x + h)
                    px.Add(img.GetPixel(xx, yy))
                Next
            Next
            If px.Count = 0 Then Return Color.Black
            Dim skipDark = 0, skipBright = 0
            If size > 3 AndAlso px.Count >= 9 Then
                px.Sort(Function(p, q) (0.3 * p.R + 0.59 * p.G + 0.11 * p.B).CompareTo(0.3 * q.R + 0.59 * q.G + 0.11 * q.B))
                skipDark = CInt(Math.Floor(px.Count * 0.3)) : skipBright = CInt(Math.Floor(px.Count * 0.1))
            End If
            Dim r = 0, g = 0, b = 0, n = 0
            For i = skipDark To px.Count - 1 - skipBright
                r += px(i).R : g += px(i).G : b += px(i).B : n += 1
            Next
            Return Color.FromArgb(r \ n, g \ n, b \ n)
        End Function

        Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
            MyBase.OnMouseDoubleClick(e)
            _zoom = 0
            _center = New PointF(0.5F, 0.5F)
            Invalidate()
        End Sub
    End Class
End Class
