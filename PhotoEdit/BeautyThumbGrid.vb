Imports System.Drawing.Drawing2D

''' <summary>
''' 一鍵美顏小圖（像手機相機的美顏風格列）：格子排列（面板窄時 4 欄，拉寬時自動增加欄數），每格是這張照片的臉套上該組的樣子＋名稱；
''' 目前套用的那張有藍框，滑過有淺框。小圖由外面在背景算好再 SetImage，還沒好時顯示灰底。背景透明（看得到頁面的底）。
''' </summary>
Friend NotInheritable Class BeautyThumbGrid
    Inherits Control

    Private Const MinColumns As Integer = 4
    Private Const MaxColumns As Integer = 7
    Private Const Gap As Integer = 6
    Private Const NameH As Integer = 20
    Private _names As String() = {}
    Private _images As Bitmap() = {}
    Private _selected As Integer = -1
    Private _hot As Integer = -1

    Public Event ItemClicked(index As Integer)

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
        BackColor = Color.Transparent
        Font = New Font("Microsoft JhengHei UI", 9.0F)
        Tag = ThemeManager.SkipTag
    End Sub

    Public Sub SetItems(names As String())
        ClearImages()
        _names = names
        _images = New Bitmap(names.Length - 1) {}
        Invalidate()
    End Sub

    Public Sub SetImage(index As Integer, img As Bitmap)
        If index < 0 OrElse index >= _images.Length Then img?.Dispose() : Return
        _images(index)?.Dispose()
        _images(index) = img
        Invalidate(TileRect(index))
    End Sub

    Public Sub ClearImages()
        For i = 0 To _images.Length - 1
            _images(i)?.Dispose()
            _images(i) = Nothing
        Next
        Invalidate()
    End Sub

    ''' <summary>目前套用的那組（-1：都不是，例如手動調整過）。</summary>
    Public Property SelectedIndex As Integer
        Get
            Return _selected
        End Get
        Set(value As Integer)
            If _selected = value Then Return
            _selected = value
            Invalidate()
        End Set
    End Property

    ''' <summary>
    ''' 版面：試 4～7 欄，取每格最大的那種（寬和高都要放得下）。
    ''' 面板拉寬時版面只會拉寬、不會加高，所以靠增加欄數用掉多出來的寬度，最後一列也不會被切掉。
    ''' </summary>
    Private Function Layout(width As Integer, height As Integer) As (Columns As Integer, Cell As Integer)
        Dim n = Math.Max(1, _names.Length)
        Dim best = (Columns:=MinColumns, Cell:=0)
        For cols = MinColumns To MaxColumns
            Dim rowCount = (n + cols - 1) \ cols
            Dim byWidth = (width - Gap * (cols - 1)) \ cols
            Dim byHeight = (height + Gap) \ rowCount - NameH - Gap
            Dim c = Math.Min(byWidth, byHeight)
            If c > best.Cell Then best = (cols, c)
        Next
        Return (best.Columns, Math.Max(24, best.Cell))
    End Function

    Private ReadOnly Property Columns As Integer
        Get
            Return Layout(Width, Height).Columns
        End Get
    End Property

    Private ReadOnly Property Cell As Integer
        Get
            Return Layout(Width, Height).Cell
        End Get
    End Property

    ''' <summary>格子比寬度窄時，整組水平置中。</summary>
    Private ReadOnly Property OffsetX As Integer
        Get
            Dim l = Layout(Width, Height)
            Return Math.Max(0, (Width - (l.Cell * l.Columns + Gap * (l.Columns - 1))) \ 2)
        End Get
    End Property

    ''' <summary>小圖要算多大（像素，正方形）：比格子大一點，面板拉寬或高解析度螢幕也清楚。</summary>
    Public ReadOnly Property ThumbPixels As Integer
        Get
            Return Math.Max(96, CInt(Cell * 1.6))
        End Get
    End Property

    ''' <summary>寬度 width 時，4 欄排下全部需要的高度（版面設計時用）。</summary>
    Public Function PreferredHeightFor(width As Integer) As Integer
        Dim c = Math.Max(24, (width - Gap * (MinColumns - 1)) \ MinColumns)
        Dim rowCount = (Math.Max(1, _names.Length) + MinColumns - 1) \ MinColumns
        Return rowCount * (c + NameH + Gap) - Gap
    End Function

    Private Function TileRect(i As Integer) As Rectangle
        Dim l = Layout(Width, Height)
        Dim ox = Math.Max(0, (Width - (l.Cell * l.Columns + Gap * (l.Columns - 1))) \ 2)
        Return New Rectangle(ox + (i Mod l.Columns) * (l.Cell + Gap), (i \ l.Columns) * (l.Cell + NameH + Gap), l.Cell, l.Cell + NameH)
    End Function

    Private Function HitTest(p As Point) As Integer
        For i = 0 To _names.Length - 1
            If TileRect(i).Contains(p) Then Return i
        Next
        Return -1
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        Dim back = ThemeManager.Back(Color.FromArgb(236, 238, 242))
        Dim fore = ThemeManager.Fore(Color.FromArgb(40, 44, 52))
        Dim accent = Color.FromArgb(47, 128, 237)
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.InterpolationMode = InterpolationMode.HighQualityBicubic
        g.PixelOffsetMode = PixelOffsetMode.Half
        Dim c = Cell
        For i = 0 To _names.Length - 1
            Dim r = TileRect(i)
            If Not r.IntersectsWith(e.ClipRectangle) Then Continue For
            Dim img As New Rectangle(r.X, r.Y, c, c)
            Using path = Rounded(img, 8)
                If _images(i) IsNot Nothing Then
                    Dim st = g.Save()
                    g.SetClip(path)
                    g.DrawImage(_images(i), img)
                    g.Restore(st)
                Else
                    Using b As New SolidBrush(If(ThemeManager.Dark, Color.FromArgb(52, 56, 64), Color.FromArgb(214, 218, 226)))
                        g.FillPath(b, path)
                    End Using
                End If
                Dim sel = i = _selected, hot = i = _hot AndAlso Enabled
                If sel OrElse hot Then
                    Using p As New Pen(If(sel, accent, Color.FromArgb(150, accent)), If(sel, 3, 1.5F))
                        g.DrawPath(p, path)
                    End Using
                End If
            End Using
            Dim textColor = If(i = _selected, accent, If(Enabled, fore, Color.FromArgb(150, 150, 150)))
            Using f As New Font(Font, If(i = _selected, FontStyle.Bold, FontStyle.Regular))
                TextRenderer.DrawText(g, _names(i), f, New Rectangle(r.X - 4, r.Y + c + 1, c + 8, NameH), textColor,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
            End Using
        Next
        If Not Enabled Then
            Using b As New SolidBrush(Color.FromArgb(120, back))
                g.FillRectangle(b, ClientRectangle)
            End Using
        End If
    End Sub

    Private Shared Function Rounded(r As Rectangle, radius As Integer) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim d = radius * 2
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim h = HitTest(e.Location)
        If h <> _hot Then
            _hot = h
            Cursor = If(h >= 0, Cursors.Hand, Cursors.Default)
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hot >= 0 Then _hot = -1 : Invalidate()
    End Sub

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        MyBase.OnMouseClick(e)
        If e.Button <> MouseButtons.Left OrElse Not Enabled Then Return
        Dim i = HitTest(e.Location)
        If i >= 0 Then RaiseEvent ItemClicked(i)
    End Sub

    Protected Overrides Sub OnEnabledChanged(e As EventArgs)
        MyBase.OnEnabledChanged(e)
        Invalidate()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then ClearImages()
        MyBase.Dispose(disposing)
    End Sub
End Class
