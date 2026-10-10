Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 變形動畫的點位編輯畫布：顯示一張圖與它的對應點（編號、顏色和另一張圖的配對點相同）。
''' 左鍵拖點移動、在空白處點一下新增（另一張圖自動放到對應位置）、右鍵點刪除；滾輪縮放、中鍵拖曳平移、雙擊空白處回到整張。
''' 「標關鍵點」模式：每點一下回報一個位置（給呼叫端依序收集左眼、右眼、鼻尖、嘴角）。
''' </summary>
Friend NotInheritable Class MorphEditor
    Inherits Control

    Private _image As Bitmap
    Private _points As List(Of MorphPoint)
    Private _zoom As Double = 1 ' 相對於「整張放得下」
    Private _center As PointF ' 畫面中央對到圖上的哪一點
    Private _drag As Integer = -1
    Private _panFrom As Point?
    Private _panCenter As PointF
    Private _hot As Integer = -1

    ''' <summary>選取的點（兩個畫布同步）。</summary>
    Public Property Selected As Integer = -1
    ''' <summary>標關鍵點模式（點一下回報位置，不新增、不拖曳）。</summary>
    Public Property KeyMode As Boolean
    ''' <summary>標關鍵點時已經點好的位置（畫成白色十字）。</summary>
    Public Property KeyMarks As New List(Of PointF)

    Public Event PointMoved(index As Integer)
    Public Event PointAddRequested(p As PointF)
    Public Event PointDeleteRequested(index As Integer)
    Public Event SelectionChanged(index As Integer)
    Public Event KeyClicked(p As PointF)
    ''' <summary>開始拖點之前（呼叫端存復原點）。</summary>
    Public Event EditStarting()

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        BackColor = Color.FromArgb(30, 30, 32)
        Tag = ThemeManager.SkipTag
    End Sub

    Public Sub SetImage(img As Bitmap, pts As List(Of MorphPoint))
        _image = img
        _points = pts
        _zoom = 1
        If img IsNot Nothing Then _center = New PointF(img.Width / 2.0F, img.Height / 2.0F)
        _drag = -1
        Invalidate()
    End Sub

    Public Sub SetPoints(pts As List(Of MorphPoint))
        _points = pts
        Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 座標換算
    '---------------------------------------------------------------------

    Private ReadOnly Property Scale As Double
        Get
            If _image Is Nothing Then Return 1
            Return Math.Min((Width - 8) / CDbl(_image.Width), (Height - 8) / CDbl(_image.Height)) * _zoom
        End Get
    End Property

    Private Function ToScreen(p As PointF) As PointF
        Dim s = Scale
        Return New PointF(CSng(Width / 2.0 + (p.X - _center.X) * s), CSng(Height / 2.0 + (p.Y - _center.Y) * s))
    End Function

    Private Function ToImage(p As Point) As PointF
        Dim s = Scale
        Return New PointF(CSng(_center.X + (p.X - Width / 2.0) / s), CSng(_center.Y + (p.Y - Height / 2.0) / s))
    End Function

    Private Function HitPoint(p As Point) As Integer
        If _points Is Nothing Then Return -1
        Dim best = -1, bd = 9.0 * 9.0
        For i = 0 To _points.Count - 1
            Dim q = ToScreen(_points(i).Pt)
            Dim d = (q.X - p.X) ^ 2 + (q.Y - p.Y) ^ 2
            If d < bd Then bd = d : best = i
        Next
        Return best
    End Function

    ''' <summary>第 i 點的顏色（兩個畫布相同，方便對照）。</summary>
    Public Shared Function PointColor(i As Integer) As Color
        Dim h = (i * 47) Mod 360
        Dim x = 1 - Math.Abs((h / 60.0) Mod 2 - 1)
        Dim rgb = If(h < 60, (1.0, x, 0.0), If(h < 120, (x, 1.0, 0.0), If(h < 180, (0.0, 1.0, x), If(h < 240, (0.0, x, 1.0), If(h < 300, (x, 0.0, 1.0), (1.0, 0.0, x))))))
        Return Color.FromArgb(CInt(rgb.Item1 * 255), CInt(rgb.Item2 * 255), CInt(rgb.Item3 * 255))
    End Function

    '---------------------------------------------------------------------
    ' 繪製
    '---------------------------------------------------------------------

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        If _image Is Nothing Then
            TextRenderer.DrawText(g, "（尚未選圖）", Font, ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Return
        End If
        Dim tl = ToScreen(New PointF(0, 0)), br = ToScreen(New PointF(_image.Width, _image.Height))
        g.InterpolationMode = If(Scale > 1.5, InterpolationMode.NearestNeighbor, InterpolationMode.HighQualityBilinear)
        g.PixelOffsetMode = PixelOffsetMode.Half
        g.DrawImage(_image, RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y))
        g.SmoothingMode = SmoothingMode.AntiAlias
        If _points IsNot Nothing Then
            Using fnt As New Font("Arial", 7.5F, FontStyle.Bold)
                For i = 0 To _points.Count - 1
                    Dim q = ToScreen(_points(i).Pt)
                    Dim r = If(i = Selected, 6.0F, If(i = _hot, 5.0F, 4.0F))
                    Using br2 As New SolidBrush(PointColor(i))
                        g.FillEllipse(br2, q.X - r, q.Y - r, r * 2, r * 2)
                    End Using
                    g.DrawEllipse(If(i = Selected, Pens.White, Pens.Black), q.X - r, q.Y - r, r * 2, r * 2)
                    If i = Selected Then
                        Using ring As New Pen(Color.White, 1.5F)
                            g.DrawEllipse(ring, q.X - r - 4, q.Y - r - 4, (r + 4) * 2, (r + 4) * 2)
                        End Using
                    End If
                    If _zoom >= 1.6 OrElse i = Selected OrElse i = _hot Then
                        Dim label = (i + 1).ToString() & If(i = Selected OrElse i = _hot, " " & _points(i).Name, "")
                        Dim sz = g.MeasureString(label, fnt)
                        Using bg As New SolidBrush(Color.FromArgb(160, 0, 0, 0))
                            g.FillRectangle(bg, q.X + r + 1, q.Y - sz.Height / 2, sz.Width, sz.Height)
                        End Using
                        g.DrawString(label, fnt, Brushes.Yellow, q.X + r + 1, q.Y - sz.Height / 2)
                    End If
                Next
            End Using
        End If
        For Each k In KeyMarks
            Dim q = ToScreen(k)
            Using p As New Pen(Color.White, 2)
                g.DrawLine(p, q.X - 7, q.Y, q.X + 7, q.Y) : g.DrawLine(p, q.X, q.Y - 7, q.X, q.Y + 7)
            End Using
        Next
    End Sub

    '---------------------------------------------------------------------
    ' 滑鼠
    '---------------------------------------------------------------------

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        If _image Is Nothing Then Return
        If e.Button = MouseButtons.Middle Then
            _panFrom = e.Location : _panCenter = _center
            Cursor = Cursors.SizeAll
            Return
        End If
        Dim ip = ToImage(e.Location)
        If KeyMode Then
            If e.Button = MouseButtons.Left AndAlso InImage(ip) Then RaiseEvent KeyClicked(ip)
            Return
        End If
        Dim hit = HitPoint(e.Location)
        If e.Button = MouseButtons.Right Then
            If hit >= 0 Then RaiseEvent PointDeleteRequested(hit)
            Return
        End If
        If e.Button <> MouseButtons.Left Then Return
        If hit >= 0 Then
            RaiseEvent EditStarting()
            _drag = hit
            Selected = hit
            RaiseEvent SelectionChanged(hit)
            Capture = True
        ElseIf InImage(ip) Then
            RaiseEvent PointAddRequested(ip)
        End If
        Invalidate()
    End Sub

    Private Function InImage(p As PointF) As Boolean
        Return p.X >= 0 AndAlso p.Y >= 0 AndAlso p.X < _image.Width AndAlso p.Y < _image.Height
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _image Is Nothing Then Return
        If _panFrom.HasValue Then
            Dim s = Scale
            _center = New PointF(CSng(_panCenter.X - (e.X - _panFrom.Value.X) / s), CSng(_panCenter.Y - (e.Y - _panFrom.Value.Y) / s))
            Invalidate()
            Return
        End If
        If _drag >= 0 Then
            Dim ip = ToImage(e.Location)
            _points(_drag).X = Math.Max(0, Math.Min(_image.Width - 1, ip.X))
            _points(_drag).Y = Math.Max(0, Math.Min(_image.Height - 1, ip.Y))
            RaiseEvent PointMoved(_drag)
            Invalidate()
            Return
        End If
        Dim h = If(KeyMode, -1, HitPoint(e.Location))
        If h <> _hot Then
            _hot = h
            Cursor = If(h >= 0, Cursors.Hand, Cursors.Cross)
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _panFrom.HasValue AndAlso e.Button = MouseButtons.Middle Then
            _panFrom = Nothing
            Cursor = Cursors.Cross
        End If
        If _drag >= 0 Then
            _drag = -1
            Capture = False
        End If
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        If _image Is Nothing OrElse HitPoint(e.Location) >= 0 OrElse e.Button <> MouseButtons.Middle Then Return
        ResetView()
    End Sub

    ''' <summary>回到整張放得下。</summary>
    Public Sub ResetView()
        If _image Is Nothing Then Return
        _zoom = 1
        _center = New PointF(_image.Width / 2.0F, _image.Height / 2.0F)
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        If _image Is Nothing Then Return
        Dim before = ToImage(e.Location)
        _zoom = Math.Max(1, Math.Min(12, _zoom * If(e.Delta > 0, 1.25, 0.8)))
        ' 縮放後游標下還是同一點
        Dim after = ToImage(e.Location)
        _center = New PointF(_center.X + before.X - after.X, _center.Y + before.Y - after.Y)
        If _zoom = 1 Then _center = New PointF(_image.Width / 2.0F, _image.Height / 2.0F)
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hot >= 0 Then _hot = -1 : Invalidate()
    End Sub
End Class
