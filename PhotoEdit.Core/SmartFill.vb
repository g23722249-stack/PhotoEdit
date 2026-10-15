Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>
''' 智慧填色（同 CorelDRAW）：在線條、圖形圍起來的地方點一下，那塊區域變成新的向量圖形。
''' 做法：把所有向量圖形的線（封閉圖形用外形線）畫成牆，從點的位置往外填，碰到牆就停；
''' 填到的範圍描出外框（洞也算），再擬合成鋼筆路徑。沒有被圍起來（填到照片邊緣）時回傳 Nothing。
''' </summary>
Public NotInheritable Class SmartFill
    Private Sub New()
    End Sub

    ''' <summary>能當牆的圖層：看得到的向量圖形（點陣、填滿、圖說文字以外）。</summary>
    Public Shared Function IsWall(d As DrawLayer) As Boolean
        If Not d.Visible Then Return False
        Select Case d.Shape
            Case DrawShape.Raster, DrawShape.Bucket, DrawShape.Gradient, DrawShape.FillLayer : Return False
            Case DrawShape.Text : Return d.Stroked
        End Select
        Return True
    End Function

    ''' <summary>
    ''' 點 p（照片高度單位）所在、被 walls 圍起來的區域外形（內部在左邊：外框正、洞負）；沒圍起來時回傳 Nothing。
    ''' aspect＝照片寬高比；resolution＝牆的格子數（照片高度方向）。
    ''' </summary>
    Public Shared Function Region(walls As IEnumerable(Of DrawLayer), aspect As Double, p As PointF, Optional resolution As Integer = 1200) As List(Of PointF())
        Dim h = resolution, w = Math.Max(1, CInt(Math.Ceiling(aspect * resolution)))
        Dim s = CSng(resolution)
        Dim wall(w * h - 1) As Boolean
        Using bmp As New Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = SmoothingMode.None
                For Each d In walls
                    Dim width = Math.Max(1.6F, CSng(d.StrokeWidth * s))
                    Using pen As New Pen(Color.White, width) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                        For Each f In DrawGeometry.Figures(d)
                            Dim pts = f.Points.Select(Function(q) New PointF(q.X * s, q.Y * s)).ToArray()
                            If pts.Length = 1 Then
                                g.FillEllipse(Brushes.White, pts(0).X - width / 2, pts(0).Y - width / 2, width, width)
                            ElseIf pts.Length >= 2 Then
                                If f.Closed Then g.DrawPolygon(pen, pts) Else g.DrawLines(pen, pts)
                            End If
                        Next
                    End Using
                Next
            End Using
            Dim px = Perspective.ReadPixels(bmp)
            For i = 0 To wall.Length - 1
                wall(i) = px(i * 4 + 3) > 0
            Next
        End Using
        Dim sx = CInt(Math.Floor(p.X * s)), sy = CInt(Math.Floor(p.Y * s))
        If sx < 0 OrElse sy < 0 OrElse sx >= w OrElse sy >= h OrElse wall(sy * w + sx) Then Return Nothing
        ' 往外填（四方向）；碰到邊緣＝沒有圍起來
        Dim inside(w * h - 1) As Boolean
        Dim stack As New Stack(Of Integer)()
        stack.Push(sy * w + sx)
        inside(sy * w + sx) = True
        While stack.Count > 0
            Dim i = stack.Pop()
            Dim x = i Mod w, y = i \ w
            If x = 0 OrElse y = 0 OrElse x = w - 1 OrElse y = h - 1 Then Return Nothing
            For Each j In {i - 1, i + 1, i - w, i + w}
                If Not inside(j) AndAlso Not wall(j) Then inside(j) = True : stack.Push(j)
            Next
        End While
        ' 往外長一格：塞到線的下面，不留縫
        Dim grown = DirectCast(inside.Clone(), Boolean())
        For y = 1 To h - 2
            For x = 1 To w - 2
                Dim i = y * w + x
                If inside(i) Then Continue For
                If inside(i - 1) OrElse inside(i + 1) OrElse inside(i - w) OrElse inside(i + w) OrElse
                   inside(i - w - 1) OrElse inside(i - w + 1) OrElse inside(i + w - 1) OrElse inside(i + w + 1) Then grown(i) = True ' 八方向：方角保持方的
            Next
        Next
        Dim rings = Trace(grown, w, h)
        Return rings.Select(Function(r) DrawGeometry.Simplify(r, 0.8, True).Select(Function(q) New PointF(q.X / s, q.Y / s)).ToArray()).
                     Where(Function(r) r.Length >= 3 AndAlso Math.Abs(PathBoolean.SignedArea(r)) > 0.0000004).ToList()
    End Function

    ''' <summary>格子的範圍描成外形線（格線上的頂點；內部在左邊）。</summary>
    Private Shared Function Trace(mask As Boolean(), w As Integer, h As Integer) As List(Of PointF())
        Dim at = Function(x As Integer, y As Integer) x >= 0 AndAlso y >= 0 AndAlso x < w AndAlso y < h AndAlso mask(y * w + x)
        Dim key = Function(x As Integer, y As Integer) CLng(y) * (w + 1) + x
        ' 每一條邊界邊：由 (ax, ay) 到 (bx, by)
        Dim edges As New List(Of (AX As Integer, AY As Integer, BX As Integer, BY As Integer))()
        For y = 0 To h - 1
            For x = 0 To w - 1
                If Not mask(y * w + x) Then Continue For
                If Not at(x, y - 1) Then edges.Add((x, y, x + 1, y))
                If Not at(x + 1, y) Then edges.Add((x + 1, y, x + 1, y + 1))
                If Not at(x, y + 1) Then edges.Add((x + 1, y + 1, x, y + 1))
                If Not at(x - 1, y) Then edges.Add((x, y + 1, x, y))
            Next
        Next
        Dim outgoing As New Dictionary(Of Long, List(Of Integer))()
        For i = 0 To edges.Count - 1
            Dim k = key(edges(i).AX, edges(i).AY)
            Dim lst As List(Of Integer) = Nothing
            If Not outgoing.TryGetValue(k, lst) Then lst = New List(Of Integer)() : outgoing(k) = lst
            lst.Add(i)
        Next
        Dim used(edges.Count - 1) As Boolean
        Dim rings As New List(Of PointF())()
        For start = 0 To edges.Count - 1
            If used(start) Then Continue For
            Dim pts As New List(Of PointF)()
            Dim cur = start
            Do
                used(cur) = True
                Dim e = edges(cur)
                pts.Add(New PointF(e.AX, e.AY))
                Dim lst As List(Of Integer) = Nothing
                If Not outgoing.TryGetValue(key(e.BX, e.BY), lst) Then Exit Do
                ' 兩條可以走時（只碰到角的兩格）：往左轉，兩塊分開描
                Dim dx = e.BX - e.AX, dy = e.BY - e.AY
                Dim nxt = -1, best = Integer.MinValue
                For Each c In lst
                    If used(c) Then Continue For
                    Dim o = edges(c)
                    Dim ox = o.BX - o.AX, oy = o.BY - o.AY
                    Dim turn = dx * oy - dy * ox ' 左轉為正
                    Dim score = If(turn > 0, 2, If(turn = 0, 1, 0))
                    If score > best Then best = score : nxt = c
                Next
                If nxt < 0 Then Exit Do
                cur = nxt
            Loop
            ' 去掉一直線上的中間點
            Dim clean As New List(Of PointF)()
            For i = 0 To pts.Count - 1
                Dim a = pts((i - 1 + pts.Count) Mod pts.Count), b = pts(i), c = pts((i + 1) Mod pts.Count)
                If (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) <> 0 Then clean.Add(b)
            Next
            If clean.Count >= 3 Then rings.Add(clean.ToArray())
        Next
        Return rings
    End Function

    ''' <summary>區域變成圖層：用 style 的填色（漸層也一起）、沒有線條。</summary>
    Public Shared Function MakeLayer(rings As List(Of PointF()), style As DrawLayer) As DrawLayer
        Dim src As New DrawLayer()
        src.CopyStyleFrom(style)
        src.Filled = True
        src.Stroked = False
        src.Shadow = False
        src.Symmetry = SymmetryKind.None
        src.Brush = BrushKind.HardRound
        Return PathBoolean.MakeLayer(src, rings)
    End Function
End Class
