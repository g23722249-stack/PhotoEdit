Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>路徑節點的種類（同 CorelDRAW）。</summary>
Public Enum PathNodeKind
    ''' <summary>尖角：兩個把手各自獨立。</summary>
    Corner = 0
    ''' <summary>平滑：兩個把手在同一條線上，長度可以不同。</summary>
    Smooth = 1
    ''' <summary>對稱：兩個把手在同一條線上、長度相同。</summary>
    Symmetric = 2
End Enum

''' <summary>
''' 鋼筆路徑的一個節點：錨點與前後兩個把手（世界座標，照片高度單位）。
''' 把手和錨點重疊表示那一側是直線；兩段之間的曲線是三次貝茲：前一個節點的 Out → 這個節點的 In。
''' </summary>
Public NotInheritable Class PathNode
    Public Property X As Single
    Public Property Y As Single
    Public Property InX As Single
    Public Property InY As Single
    Public Property OutX As Single
    Public Property OutY As Single
    Public Property Kind As PathNodeKind

    Public Sub New()
    End Sub

    ''' <summary>沒有把手的尖角節點。</summary>
    Public Sub New(x As Single, y As Single)
        Me.X = x : Me.Y = y
        InX = x : InY = y : OutX = x : OutY = y
    End Sub

    Public ReadOnly Property Anchor As PointF
        Get
            Return New PointF(X, Y)
        End Get
    End Property

    Public ReadOnly Property InHandle As PointF
        Get
            Return New PointF(InX, InY)
        End Get
    End Property

    Public ReadOnly Property OutHandle As PointF
        Get
            Return New PointF(OutX, OutY)
        End Get
    End Property

    Public Function Clone() As PathNode
        Return DirectCast(MemberwiseClone(), PathNode)
    End Function

    ''' <summary>整個節點（含把手）平移。</summary>
    Public Sub Offset(dx As Single, dy As Single)
        X += dx : Y += dy : InX += dx : InY += dy : OutX += dx : OutY += dy
    End Sub

    ''' <summary>每個座標套用同一個轉換（縮放、旋轉）。</summary>
    Public Sub Transform(map As Func(Of PointF, PointF))
        Dim a = map(Anchor), i = map(InHandle), o = map(OutHandle)
        X = a.X : Y = a.Y : InX = i.X : InY = i.Y : OutX = o.X : OutY = o.Y
    End Sub
End Class

''' <summary>一條路徑（一串節點）；Closed＝最後一個節點連回第一個。</summary>
Public NotInheritable Class PathContour
    Public Property Nodes As List(Of PathNode) = New List(Of PathNode)()
    Public Property Closed As Boolean

    Public Function Clone() As PathContour
        Return New PathContour With {.Nodes = Nodes.Select(Function(n) n.Clone()).ToList(), .Closed = Closed}
    End Function

    ''' <summary>段數：開放路徑 n−1，封閉路徑 n。</summary>
    Public ReadOnly Property SegmentCount As Integer
        Get
            If Nodes.Count < 2 Then Return 0
            Return If(Closed, Nodes.Count, Nodes.Count - 1)
        End Get
    End Property
End Class

Partial Public NotInheritable Class DrawGeometry

    '=====================================================================
    ' 鋼筆路徑
    '=====================================================================

    ''' <summary>第 i 段的四個控制點（前一個節點、它的 Out 把手、下一個節點的 In 把手、下一個節點）。</summary>
    Public Shared Function Segment(c As PathContour, i As Integer) As PointF()
        Dim a = c.Nodes(i), b = c.Nodes((i + 1) Mod c.Nodes.Count)
        Return {a.Anchor, a.OutHandle, b.InHandle, b.Anchor}
    End Function

    ''' <summary>貝茲曲線上 t 的位置。</summary>
    Public Shared Function BezierAt(p As PointF(), t As Double) As PointF
        Dim u = 1 - t
        Dim x = u * u * u * p(0).X + 3 * u * u * t * p(1).X + 3 * u * t * t * p(2).X + t * t * t * p(3).X
        Dim y = u * u * u * p(0).Y + 3 * u * u * t * p(1).Y + 3 * u * t * t * p(2).Y + t * t * t * p(3).Y
        Return New PointF(CSng(x), CSng(y))
    End Function

    ''' <summary>一段是不是直線（兩個把手都貼在錨點上）。</summary>
    Public Shared Function IsStraight(p As PointF()) As Boolean
        Return Dist(p(0), p(1)) < 0.000001 AndAlso Dist(p(2), p(3)) < 0.000001
    End Function

    ''' <summary>路徑取樣成折線（直線段只取兩端，曲線段依長度取 8～48 點）。</summary>
    Public Shared Function SampleContour(c As PathContour) As PointF()
        Dim pts As New List(Of PointF)()
        If c.Nodes.Count = 0 Then Return pts.ToArray()
        pts.Add(c.Nodes(0).Anchor)
        For i = 0 To c.SegmentCount - 1
            Dim s = Segment(c, i)
            If IsStraight(s) Then
                pts.Add(s(3))
            Else
                Dim len = Dist(s(0), s(1)) + Dist(s(1), s(2)) + Dist(s(2), s(3))
                Dim n = CInt(Math.Max(8, Math.Min(48, len * 400)))
                For k = 1 To n
                    pts.Add(BezierAt(s, k / CDbl(n)))
                Next
            End If
        Next
        If c.Closed AndAlso pts.Count > 1 AndAlso Dist(pts(0), pts(pts.Count - 1)) < 0.000001 Then pts.RemoveAt(pts.Count - 1)
        Return pts.ToArray()
    End Function

    ''' <summary>鋼筆路徑的外形線。</summary>
    Private Shared Function PathFigures(layer As DrawLayer) As List(Of Figure)
        Dim list As New List(Of Figure)()
        If layer.Contours Is Nothing Then Return list
        For Each c In layer.Contours
            Dim pts = SampleContour(c)
            If pts.Length = 0 Then Continue For
            list.Add(New Figure With {.Points = pts, .Closed = c.Closed AndAlso pts.Length >= 3})
        Next
        Return list
    End Function

    ''' <summary>圖層算不算封閉形狀（決定沒有勾「外框」時線條要不要畫）：鋼筆路徑要每條都封閉。</summary>
    Public Shared Function IsClosedLayer(layer As DrawLayer) As Boolean
        If layer.Shape = DrawShape.Path Then Return layer.Contours IsNot Nothing AndAlso layer.Contours.Count > 0 AndAlso layer.Contours.All(Function(c) c.Closed)
        If layer.Shape = DrawShape.Arc Then Return layer.ArcMode <> ArcKind.Arc
        Return IsClosed(layer.Shape)
    End Function

    ''' <summary>
    ''' 拖曳把手：節點 kind 是平滑時另一個把手轉到同一條線上（長度不變），對稱時長度也一樣；尖角時另一個不動。
    ''' isOut＝拖的是 Out 把手。
    ''' </summary>
    Public Shared Sub MoveHandle(n As PathNode, isOut As Boolean, p As PointF)
        If isOut Then n.OutX = p.X : n.OutY = p.Y Else n.InX = p.X : n.InY = p.Y
        If n.Kind = PathNodeKind.Corner Then Return
        Dim dx = p.X - n.X, dy = p.Y - n.Y
        Dim len = Math.Sqrt(dx * dx + dy * dy)
        If len < 0.000001 Then Return
        Dim other = If(isOut, New PointF(n.InX, n.InY), New PointF(n.OutX, n.OutY))
        Dim olen = If(n.Kind = PathNodeKind.Symmetric, len, Dist(other, n.Anchor))
        Dim ox = CSng(n.X - dx / len * olen), oy = CSng(n.Y - dy / len * olen)
        If isOut Then n.InX = ox : n.InY = oy Else n.OutX = ox : n.OutY = oy
    End Sub

    ''' <summary>改節點種類：平滑／對稱時把兩個把手調到同一條線上（兩個都貼著錨點時依前後節點自動拉出把手）。</summary>
    Public Shared Sub SetNodeKind(c As PathContour, index As Integer, kind As PathNodeKind)
        Dim n = c.Nodes(index)
        n.Kind = kind
        If kind = PathNodeKind.Corner Then Return
        Dim hasIn = Dist(n.InHandle, n.Anchor) > 0.000001, hasOut = Dist(n.OutHandle, n.Anchor) > 0.000001
        If Not hasIn AndAlso Not hasOut Then
            ' 依前後兩個節點的方向拉出把手（長度各為到鄰點距離的 1/3）
            Dim cnt = c.Nodes.Count
            Dim prev = If(index > 0, c.Nodes(index - 1), If(c.Closed, c.Nodes(cnt - 1), Nothing))
            Dim nxt = If(index < cnt - 1, c.Nodes(index + 1), If(c.Closed, c.Nodes(0), Nothing))
            Dim a = If(prev Is Nothing, n.Anchor, prev.Anchor), b = If(nxt Is Nothing, n.Anchor, nxt.Anchor)
            Dim dx = b.X - a.X, dy = b.Y - a.Y
            Dim len = Math.Sqrt(dx * dx + dy * dy)
            If len < 0.000001 Then Return
            Dim lin = If(prev Is Nothing, 0, Dist(prev.Anchor, n.Anchor) / 3), lout = If(nxt Is Nothing, 0, Dist(nxt.Anchor, n.Anchor) / 3)
            If kind = PathNodeKind.Symmetric Then
                Dim m = (lin + lout) / 2 : lin = m : lout = m
            End If
            n.InX = CSng(n.X - dx / len * lin) : n.InY = CSng(n.Y - dy / len * lin)
            n.OutX = CSng(n.X + dx / len * lout) : n.OutY = CSng(n.Y + dy / len * lout)
        ElseIf hasOut Then
            MoveHandle(n, True, n.OutHandle)
        Else
            MoveHandle(n, False, n.InHandle)
        End If
    End Sub

    ''' <summary>在第 seg 段的 t 處加一個節點（de Casteljau 分割，曲線形狀不變）；回傳新節點的索引。</summary>
    Public Shared Function InsertNode(c As PathContour, seg As Integer, t As Double) As Integer
        Dim s = Segment(c, seg)
        Dim a = c.Nodes(seg), b = c.Nodes((seg + 1) Mod c.Nodes.Count)
        Dim lerp = Function(p As PointF, q As PointF) New PointF(CSng(p.X + (q.X - p.X) * t), CSng(p.Y + (q.Y - p.Y) * t))
        Dim p01 = lerp(s(0), s(1)), p12 = lerp(s(1), s(2)), p23 = lerp(s(2), s(3))
        Dim p012 = lerp(p01, p12), p123 = lerp(p12, p23)
        Dim m = lerp(p012, p123)
        Dim straight = IsStraight(s)
        Dim n As New PathNode With {.X = m.X, .Y = m.Y, .Kind = If(straight, PathNodeKind.Corner, PathNodeKind.Smooth)}
        If straight Then
            n.InX = m.X : n.InY = m.Y : n.OutX = m.X : n.OutY = m.Y
        Else
            a.OutX = p01.X : a.OutY = p01.Y
            n.InX = p012.X : n.InY = p012.Y : n.OutX = p123.X : n.OutY = p123.Y
            b.InX = p23.X : b.InY = p23.Y
        End If
        c.Nodes.Insert(seg + 1, n)
        Return seg + 1
    End Function

    ''' <summary>點 p 最近的路徑位置：（第幾條、第幾段、t、距離）；沒有路徑時距離為無限大。</summary>
    Public Shared Function NearestOnPath(layer As DrawLayer, p As PointF) As (Contour As Integer, Seg As Integer, T As Double, Distance As Double)
        Dim best = (Contour:=-1, Seg:=-1, T:=0.0, Distance:=Double.MaxValue)
        If layer.Contours Is Nothing Then Return best
        For ci = 0 To layer.Contours.Count - 1
            Dim c = layer.Contours(ci)
            For si = 0 To c.SegmentCount - 1
                Dim s = Segment(c, si)
                Const N = 40
                Dim prev = s(0)
                For k = 1 To N
                    Dim q = BezierAt(s, k / CDbl(N))
                    Dim d = DistToSegment(p, prev, q)
                    If d < best.Distance Then
                        ' 這一小段上的 t
                        Dim vx = q.X - prev.X, vy = q.Y - prev.Y
                        Dim l2 = vx * vx + vy * vy
                        Dim f = If(l2 <= 0, 0, Math.Max(0, Math.Min(1, ((p.X - prev.X) * vx + (p.Y - prev.Y) * vy) / l2)))
                        best = (ci, si, (k - 1 + f) / N, d)
                    End If
                    prev = q
                Next
            Next
        Next
        Return best
    End Function

    ''' <summary>
    ''' 轉成鋼筆路徑（之後可以用節點工具改形狀）：矩形、多邊形、直線保留角點；橢圓用 4 個對稱節點；貝茲曲線保留把手；
    ''' 其他形狀（星形、愛心、箭頭、自由繪製…）把外形線簡化成節點。圖說、點陣圖層、填滿圖層不能轉，回傳 Nothing。
    ''' 顏色、筆刷、名稱等設定不變。
    ''' </summary>
    Public Shared Function ToPath(layer As DrawLayer) As DrawLayer
        If layer.Shape = DrawShape.Path Then Return layer.Clone()
        If layer.Shape = DrawShape.Text OrElse layer.Shape = DrawShape.Raster OrElse layer.Shape = DrawShape.Bucket OrElse
           layer.Shape = DrawShape.Gradient OrElse layer.Shape = DrawShape.FillLayer Then Return Nothing
        Dim r = layer.Clone()
        r.Shape = DrawShape.Path
        r.Contours = New List(Of PathContour)()
        Select Case layer.Shape
            Case DrawShape.Ellipse
                ' 4 個對稱節點，把手長度 0.5523（標準的圓逼近）
                Const K = 0.5523F
                Dim hw = CSng(Math.Abs(layer.W) / 2), hh = CSng(Math.Abs(layer.H) / 2)
                Dim c As New PathContour With {.Closed = True}
                For Each q In {(0F, -hh, K * hw, 0F), (hw, 0F, 0F, K * hh), (0F, hh, -K * hw, 0F), (-hw, 0F, 0F, -K * hh)}
                    Dim n As New PathNode With {.Kind = PathNodeKind.Symmetric}
                    Dim a = LocalToWorld(layer, New PointF(q.Item1, q.Item2))
                    Dim o = LocalToWorld(layer, New PointF(q.Item1 + q.Item3, q.Item2 + q.Item4))
                    Dim i = LocalToWorld(layer, New PointF(q.Item1 - q.Item3, q.Item2 - q.Item4))
                    n.X = a.X : n.Y = a.Y : n.OutX = o.X : n.OutY = o.Y : n.InX = i.X : n.InY = i.Y
                    c.Nodes.Add(n)
                Next
                r.Contours.Add(c)
            Case DrawShape.Bezier
                If layer.Points Is Nothing OrElse layer.Points.Count < 4 Then Return Nothing
                Dim p = layer.Points
                Dim a As New PathNode With {.X = p(0).X, .Y = p(0).Y, .InX = p(0).X, .InY = p(0).Y, .OutX = p(1).X, .OutY = p(1).Y}
                Dim b As New PathNode With {.X = p(3).X, .Y = p(3).Y, .InX = p(2).X, .InY = p(2).Y, .OutX = p(3).X, .OutY = p(3).Y}
                r.Contours.Add(New PathContour With {.Nodes = New List(Of PathNode) From {a, b}})
            Case DrawShape.Rectangle, DrawShape.Polygon, DrawShape.Line, DrawShape.Arrow, DrawShape.Star4, DrawShape.Star5, DrawShape.Star6, DrawShape.Lightning,
                 DrawShape.RegularPolygon, DrawShape.Star, DrawShape.Grid
                For Each f In BaseFigures(layer)
                    r.Contours.Add(New PathContour With {.Nodes = f.Points.Select(Function(q) New PathNode(q.X, q.Y)).ToList(), .Closed = f.Closed})
                Next
            Case Else
                ' 圓角矩形、愛心、自由繪製：外形線簡化後用平滑節點接起來（Catmull-Rom 換成貝茲把手）
                For Each f In BaseFigures(layer)
                    Dim tol = Math.Max(0.0015, Math.Max(Bounds(layer).Width, Bounds(layer).Height) * 0.006)
                    ' 封閉的外形用擬合（直邊接圓弧的地方不會凸出去；Catmull-Rom 在長直邊旁邊的把手太長，會冒出一根刺）
                    If f.Closed AndAlso f.Points.Length >= 3 Then
                        Dim fit = PathBoolean.FitRing(f.Points, Math.Max(0.0004, tol * 0.3))
                        If fit IsNot Nothing Then r.Contours.Add(fit) : Continue For
                    End If
                    Dim simple = Simplify(f.Points, tol, f.Closed)
                    If simple.Length < 2 Then Continue For
                    r.Contours.Add(SmoothContour(simple, f.Closed))
                Next
        End Select
        r.Points = Nothing : r.Strokes = Nothing
        r.X = 0 : r.Y = 0 : r.W = 0 : r.H = 0 : r.Rotation = 0
        r.Symmetry = SymmetryKind.None ' 對稱的複本已經算進外形線（自由繪製）
        Return r
    End Function

    ''' <summary>一串點用平滑節點接起來（把手由前後點的方向算，Catmull-Rom）。</summary>
    Public Shared Function SmoothContour(pts As PointF(), closed As Boolean) As PathContour
        Dim c As New PathContour With {.Closed = closed}
        Dim n = pts.Length
        For i = 0 To n - 1
            Dim p = pts(i)
            Dim prev = If(i > 0, pts(i - 1), If(closed, pts(n - 1), p))
            Dim nxt = If(i < n - 1, pts(i + 1), If(closed, pts(0), p))
            Dim tx = (nxt.X - prev.X) / 6.0F, ty = (nxt.Y - prev.Y) / 6.0F
            Dim isEnd = Not closed AndAlso (i = 0 OrElse i = n - 1)
            c.Nodes.Add(New PathNode With {.X = p.X, .Y = p.Y, .InX = p.X - If(i = 0 AndAlso Not closed, 0, tx), .InY = p.Y - If(i = 0 AndAlso Not closed, 0, ty),
                                           .OutX = p.X + If(i = n - 1 AndAlso Not closed, 0, tx), .OutY = p.Y + If(i = n - 1 AndAlso Not closed, 0, ty),
                                           .Kind = If(isEnd, PathNodeKind.Corner, PathNodeKind.Smooth)})
        Next
        Return c
    End Function

    ''' <summary>Douglas–Peucker 簡化折線（封閉時從最遠的兩點切開再各自簡化）。</summary>
    Public Shared Function Simplify(pts As PointF(), tol As Double, closed As Boolean) As PointF()
        If pts.Length <= 3 Then Return pts
        If closed Then
            Dim far = 0, fd = 0.0
            For i = 1 To pts.Length - 1
                Dim d = Dist(pts(0), pts(i))
                If d > fd Then fd = d : far = i
            Next
            Dim a = Simplify(pts.Take(far + 1).ToArray(), tol, False)
            Dim b = Simplify(pts.Skip(far).Concat({pts(0)}).ToArray(), tol, False)
            Return a.Concat(b.Skip(1).Take(b.Length - 2)).ToArray()
        End If
        Dim keep(pts.Length - 1) As Boolean
        keep(0) = True : keep(pts.Length - 1) = True
        Dim stack As New Stack(Of (A As Integer, B As Integer))()
        stack.Push((0, pts.Length - 1))
        While stack.Count > 0
            Dim s = stack.Pop()
            Dim idx = -1, md = tol
            For i = s.A + 1 To s.B - 1
                Dim d = DistToSegment(pts(i), pts(s.A), pts(s.B))
                If d > md Then md = d : idx = i
            Next
            If idx >= 0 Then
                keep(idx) = True
                stack.Push((s.A, idx)) : stack.Push((idx, s.B))
            End If
        End While
        Return pts.Where(Function(p, i) keep(i)).ToArray()
    End Function
End Class
