Imports System.Drawing.Drawing2D

''' <summary>
''' 參考圖（檢視 → 參考圖…，Ctrl+Shift+R）：臨摹時把原作開在旁邊的浮動視窗，不必切換程式。
''' 點一下吸色（設成繪圖的線條色）；滾輪縮放、拖曳移動、按兩下看全圖。
''' 「描圖」把參考圖半透明疊在畫布上對位用，只顯示在畫面上，不會存進文件或匯出。
''' </summary>
Friend Class frmReference
    Inherits Aqua.AquaForm

    Private ReadOnly _settings As AppSettings
    Private ReadOnly _view As New RefView()
    Private ReadOnly _open As New Button With {.Text = "開啟圖片…", .UseVisualStyleBackColor = True}
    Private ReadOnly _trace As New CheckBox With {.Text = "描圖", .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter, .UseVisualStyleBackColor = True}
    Private ReadOnly _opacity As New Aqua.Slider With {.Minimum = 10, .Maximum = 80, .ShowTicks = False}
    Private ReadOnly _opacityLabel As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _status As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent,
                                               .ForeColor = Color.FromArgb(105, 110, 120)}
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10)

    ''' <summary>點一下參考圖吸到的顏色。</summary>
    Public Event ColorPicked(c As Color)
    ''' <summary>描圖開關、不透明度或圖片換了：畫布要重畫。</summary>
    Public Event TraceChanged()

    Public Sub New(settings As AppSettings)
        _settings = settings
        Text = "參考圖"
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Sizable
        MinButton = False
        StartPosition = FormStartPosition.Manual
        ShowInTaskbar = False
        ClientSize = New Size(460, 420)
        MinimumSize = New Size(320, 260)

        AddHandler _open.Click, Sub() OpenWithDialog()
        AddHandler _trace.CheckedChanged, Sub()
                                              _trace.BackColor = If(_trace.Checked, Color.FromArgb(255, 214, 120), If(ThemeManager.Dark, ThemeManager.ButtonBack, SystemColors.Control))
                                              _trace.ForeColor = If(_trace.Checked OrElse Not ThemeManager.Dark, SystemColors.ControlText, Aqua.Theme.TextColor)
                                              RaiseEvent TraceChanged()
                                          End Sub
        _opacity.Value = Math.Max(10, Math.Min(80, _settings.TraceOpacity))
        _opacityLabel.Text = _opacity.Value & "%"
        AddHandler _opacity.ValueChanged, Sub()
                                              _opacityLabel.Text = _opacity.Value & "%"
                                              _settings.TraceOpacity = _opacity.Value
                                              _settings.Save()
                                              If _trace.Checked Then RaiseEvent TraceChanged()
                                          End Sub
        AddHandler _view.Picked, Sub(c)
                                     _status.Text = $"已吸色 RGB({c.R}, {c.G}, {c.B})，設成線條色。"
                                     RaiseEvent ColorPicked(c)
                                 End Sub
        _view.AllowDrop = True
        AddHandler _view.DragEnter, Sub(s, e) e.Effect = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
        AddHandler _view.DragDrop, Sub(s, e)
                                       Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
                                       If files IsNot Nothing AndAlso files.Length > 0 Then LoadImage(files(0))
                                   End Sub
        Controls.AddRange({_open, _trace, _opacity, _opacityLabel, _view, _status})
        _status.Text = "點一下吸色；滾輪縮放、拖曳移動、按兩下看全圖。"
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

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        LayoutBody()
    End Sub

    Private Sub LayoutBody()
        If _view Is Nothing Then Return ' AquaForm 的建構式會先觸發 OnResize，那時欄位還沒建立
        Dim top = 23 + 8, w = ClientSize.Width, h = ClientSize.Height
        _open.SetBounds(10, top, 104, 28)
        _trace.SetBounds(120, top, 64, 28)
        _opacity.SetBounds(190, top + 2, Math.Max(60, w - 190 - 58), 24)
        _opacityLabel.SetBounds(w - 56, top, 46, 28)
        _view.SetBounds(10, top + 34, w - 20, Math.Max(40, h - top - 34 - 30))
        _status.SetBounds(10, h - 28, w - 20, 24)
    End Sub

    Private Sub OpenWithDialog()
        Using dlg As New OpenFileDialog With {.Title = "開啟參考圖", .Filter = "圖片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有檔案|*.*"}
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
            If Not quiet Then MessageBox.Show(Me, $"無法開啟「{IO.Path.GetFileName(path)}」：{ex.Message}", "參考圖", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        _view.Image = bmp
        _settings.ReferencePath = path
        _settings.Save()
        Text = "參考圖 – " & IO.Path.GetFileName(path)
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

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Cursor = Cursors.Cross
            AccessibleName = "參考圖"
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
                TextRenderer.DrawText(g, "按「開啟圖片…」或把圖片拖曳到這裡", Font, ClientRectangle, Color.FromArgb(200, 204, 212),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
                Return
            End If
            g.InterpolationMode = If(If(_zoom = 0, FitScale(), _zoom) > 2, InterpolationMode.NearestNeighbor, InterpolationMode.HighQualityBilinear)
            g.PixelOffsetMode = PixelOffsetMode.Half
            g.DrawImage(_image, ImageRect())
        End Sub

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
            RaiseEvent Picked(AverageAt(x, y))
        End Sub

        ''' <summary>3×3 平均：避開單一雜點（油畫的筆觸、JPG 的雜訊）。</summary>
        Private Function AverageAt(x As Integer, y As Integer) As Color
            Dim r = 0, g = 0, b = 0, n = 0
            For yy = Math.Max(0, y - 1) To Math.Min(_image.Height - 1, y + 1)
                For xx = Math.Max(0, x - 1) To Math.Min(_image.Width - 1, x + 1)
                    Dim c = _image.GetPixel(xx, yy)
                    r += c.R : g += c.G : b += c.B : n += 1
                Next
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
