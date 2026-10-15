Imports PhotoEdit
Imports System.Drawing

''' <summary>
''' 封套（同 CorelDRAW 的封套工具）：圖形放進一個可以拖曳變形的框，框的四個角與四邊中點拖到哪，圖形就跟著彎過去。
''' 框的每一邊是通過中點的二次曲線，裡面用 Coons 曲面內插；圖形先轉成鋼筆路徑並細分，再把每個節點與把手換算過去。
''' </summary>
Public NotInheritable Class Envelope
    Private Sub New()
    End Sub

    ''' <summary>能不能套封套：向量圖形（文字、圖說、點陣、填滿圖層不行；文字先轉成外框）。</summary>
    Public Shared Function CanApply(d As DrawLayer) As Boolean
        If d Is Nothing OrElse DrawGeometry.HasText(d.Shape) Then Return False
        Return DrawGeometry.ToPath(d) IsNot Nothing
    End Function

    ''' <summary>框的 8 個控制點（左上、上、右上、右、右下、下、左下、左），一開始是外接矩形。</summary>
    Public Shared Function DefaultGrid(b As RectangleF) As PointF()
        Dim cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2
        Return {New PointF(b.Left, b.Top), New PointF(cx, b.Top), New PointF(b.Right, b.Top), New PointF(b.Right, cy),
                New PointF(b.Right, b.Bottom), New PointF(cx, b.Bottom), New PointF(b.Left, b.Bottom), New PointF(b.Left, cy)}
    End Function

    ''' <summary>二次曲線（端點 a、b，通過中點 m）上 t 的位置。</summary>
    Private Shared Function Edge(a As PointF, m As PointF, b As PointF, t As Double) As PointF
        Dim cx = 2 * m.X - (a.X + b.X) / 2, cy = 2 * m.Y - (a.Y + b.Y) / 2
        Dim mt = 1 - t
        Return New PointF(CSng(mt * mt * a.X + 2 * mt * t * cx + t * t * b.X), CSng(mt * mt * a.Y + 2 * mt * t * cy + t * t * b.Y))
    End Function

    ''' <summary>框內 (u, v)（0..1）換算到變形後的位置（Coons 曲面）。</summary>
    Public Shared Function Map(g As PointF(), u As Double, v As Double) As PointF
        Dim top = Edge(g(0), g(1), g(2), u), bottom = Edge(g(6), g(5), g(4), u)
        Dim left = Edge(g(0), g(7), g(6), v), right = Edge(g(2), g(3), g(4), v)
        Dim x = (1 - v) * top.X + v * bottom.X + (1 - u) * left.X + u * right.X -
                ((1 - u) * (1 - v) * g(0).X + u * (1 - v) * g(2).X + (1 - u) * v * g(6).X + u * v * g(4).X)
        Dim y = (1 - v) * top.Y + v * bottom.Y + (1 - u) * left.Y + u * right.Y -
                ((1 - u) * (1 - v) * g(0).Y + u * (1 - v) * g(2).Y + (1 - u) * v * g(6).Y + u * v * g(4).Y)
        Return New PointF(CSng(x), CSng(y))
    End Function

    ''' <summary>框的外形線（畫面顯示用，照片座標）。</summary>
    Public Shared Function Outline(g As PointF()) As PointF()
        Dim pts As New List(Of PointF)()
        For k = 0 To 16 : pts.Add(Edge(g(0), g(1), g(2), k / 16.0)) : Next
        For k = 1 To 16 : pts.Add(Edge(g(2), g(3), g(4), k / 16.0)) : Next
        For k = 1 To 16 : pts.Add(Edge(g(4), g(5), g(6), k / 16.0)) : Next
        For k = 1 To 15 : pts.Add(Edge(g(6), g(7), g(0), k / 16.0)) : Next
        Return pts.ToArray()
    End Function

    ''' <summary>
    ''' 把 original 依框 grid 變形（original 的外接矩形對應到框）：回傳鋼筆路徑圖層。
    ''' 每段曲線先切成 6 段，變形後的曲線才貼得住框。
    ''' </summary>
    Public Shared Function Apply(original As DrawLayer, grid As PointF()) As DrawLayer
        Dim p = DrawGeometry.ToPath(original)
        If p Is Nothing OrElse p.Contours Is Nothing Then Return Nothing
        Dim b = DrawGeometry.Bounds(original)
        Dim w = Math.Max(0.000001, b.Width), h = Math.Max(0.000001, b.Height)
        For Each c In p.Contours
            For si = c.SegmentCount - 1 To 0 Step -1
                ' 從後面切：第 si 段切成 6 等分（t 依剩下的長度換算）
                For k = 5 To 1 Step -1
                    DrawGeometry.InsertNode(c, si, k / (k + 1.0))
                Next
            Next
            For Each n In c.Nodes
                n.Transform(Function(q) Map(grid, (q.X - b.X) / w, (q.Y - b.Y) / h))
                If n.Kind = PathNodeKind.Symmetric Then n.Kind = PathNodeKind.Smooth
            Next
        Next
        Return p
    End Function
End Class

''' <summary>
''' 調和（同 CorelDRAW 的調和、Illustrator 的漸變）：兩個圖形之間產生幾個中間的圖形，形狀、顏色、線寬、不透明度逐步變過去。
''' 兩個外形各取樣成同樣多的點（起點對齊、方向一致）再內插；結果是鋼筆路徑。
''' </summary>
Public NotInheritable Class ShapeBlend
    Private Sub New()
    End Sub

    Private Const Samples As Integer = 120

    Public Shared Function CanBlend(d As DrawLayer) As Boolean
        If d Is Nothing OrElse DrawGeometry.HasText(d.Shape) Then Return False
        Select Case d.Shape
            Case DrawShape.Raster, DrawShape.Bucket, DrawShape.Gradient, DrawShape.FillLayer : Return False
        End Select
        Return DrawGeometry.Figures(d).Any(Function(f) f.Points.Length >= 2)
    End Function

    ''' <summary>a 到 b 之間的 steps 個中間圖形（不含 a、b 本身）。</summary>
    Public Shared Function Make(a As DrawLayer, b As DrawLayer, steps As Integer) As List(Of DrawLayer)
        Dim result As New List(Of DrawLayer)()
        Dim fa = MainFigure(a), fb = MainFigure(b)
        If fa.Points Is Nothing OrElse fb.Points Is Nothing Then Return result
        Dim closed = fa.Closed AndAlso fb.Closed
        Dim pa = Resample(fa.Points, closed), pb = Resample(fb.Points, closed)
        If closed Then
            ' 方向一致、起點對齊（距離總和最小）
            If Math.Sign(PathBoolean.SignedArea(pa)) <> Math.Sign(PathBoolean.SignedArea(pb)) Then Array.Reverse(pb)
            Dim best = 0, bestD = Double.MaxValue
            For s = 0 To Samples - 1
                Dim d = 0.0
                For i = 0 To Samples - 1 Step 4
                    d += DrawGeometry.Dist(pa(i), pb((i + s) Mod Samples))
                Next
                If d < bestD Then bestD = d : best = s
            Next
            pb = Enumerable.Range(0, Samples).Select(Function(i) pb((i + best) Mod Samples)).ToArray()
        ElseIf DrawGeometry.Dist(pa(0), pb(0)) > DrawGeometry.Dist(pa(0), pb(pb.Length - 1)) Then
            Array.Reverse(pb)
        End If
        steps = Math.Max(1, Math.Min(100, steps))
        For k = 1 To steps
            Dim t = k / (steps + 1.0)
            Dim pts = Enumerable.Range(0, Samples).Select(Function(i) New PointF(CSng(pa(i).X + (pb(i).X - pa(i).X) * t), CSng(pa(i).Y + (pb(i).Y - pa(i).Y) * t))).ToArray()
            Dim d = a.Clone()
            d.Shape = DrawShape.Path
            d.Points = Nothing : d.Strokes = Nothing : d.Rotation = 0 : d.Symmetry = SymmetryKind.None
            d.Text = "" : d.Id = Nothing
            If closed Then
                Dim c = PathBoolean.FitRing(pts, 0.0006)
                If c Is Nothing Then Continue For
                d.Contours = New List(Of PathContour) From {c}
            Else
                d.Contours = New List(Of PathContour) From {DrawGeometry.SmoothContour(DrawGeometry.Simplify(pts, 0.0006, False), False)}
            End If
            d.StrokeColorArgb = Lerp(a.StrokeColorArgb, b.StrokeColorArgb, t)
            d.FillColorArgb = Lerp(a.FillColorArgb, b.FillColorArgb, t)
            d.FillColor2Argb = Lerp(a.FillColor2Argb, b.FillColor2Argb, t)
            d.StrokeWidth = a.StrokeWidth + (b.StrokeWidth - a.StrokeWidth) * t
            d.Opacity = CInt(Math.Round(a.Opacity + (b.Opacity - a.Opacity) * t))
            d.Filled = closed AndAlso (a.Filled OrElse b.Filled)
            d.Stroked = a.Stroked OrElse b.Stroked
            d.Name = Lang.F("調和 {0}", k)
            result.Add(d)
        Next
        Return result
    End Function

    ''' <summary>主要的外形（最長的那條）。</summary>
    Private Shared Function MainFigure(d As DrawLayer) As DrawGeometry.Figure
        Dim figs = DrawGeometry.Figures(d).Where(Function(f) f.Points.Length >= 2).ToList()
        If figs.Count = 0 Then Return Nothing
        Return figs.OrderByDescending(Function(f) Length(f.Points, f.Closed)).First()
    End Function

    Private Shared Function Length(p As PointF(), closed As Boolean) As Double
        Dim s = 0.0
        For i = 1 To p.Length - 1
            s += DrawGeometry.Dist(p(i - 1), p(i))
        Next
        If closed Then s += DrawGeometry.Dist(p(p.Length - 1), p(0))
        Return s
    End Function

    ''' <summary>依長度等距取 Samples 個點。</summary>
    Private Shared Function Resample(p As PointF(), closed As Boolean) As PointF()
        Dim pts = If(closed, p.Concat({p(0)}).ToArray(), p)
        Dim cum(pts.Length - 1) As Double
        For i = 1 To pts.Length - 1
            cum(i) = cum(i - 1) + DrawGeometry.Dist(pts(i - 1), pts(i))
        Next
        Dim total = cum(pts.Length - 1)
        Dim n = Samples
        Dim result(n - 1) As PointF
        Dim j = 1
        For k = 0 To n - 1
            Dim s = total * k / If(closed, n, n - 1)
            While j < pts.Length - 1 AndAlso cum(j) < s
                j += 1
            End While
            Dim seg = Math.Max(1.0E-12, cum(j) - cum(j - 1))
            Dim t = Math.Max(0, Math.Min(1, (s - cum(j - 1)) / seg))
            result(k) = New PointF(CSng(pts(j - 1).X + (pts(j).X - pts(j - 1).X) * t), CSng(pts(j - 1).Y + (pts(j).Y - pts(j - 1).Y) * t))
        Next
        Return result
    End Function

    Private Shared Function Lerp(a As Integer, b As Integer, t As Double) As Integer
        Dim ca = Color.FromArgb(a), cb = Color.FromArgb(b)
        Dim m = Function(x As Integer, y As Integer) CInt(Math.Round(x + (y - x) * t))
        Return Color.FromArgb(m(ca.A, cb.A), m(ca.R, cb.R), m(ca.G, cb.G), m(ca.B, cb.B)).ToArgb()
    End Function
End Class
