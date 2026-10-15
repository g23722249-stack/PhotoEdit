Imports System.Drawing

''' <summary>
''' 刀具（同 CorelDRAW 的刀具、Illustrator 的美工刀）：沿著一條線把圖形切開。
''' 封閉圖形：減掉沿刀線的一條極細切縫，切成幾塊就變成幾個鋼筆路徑；開放的線：在和刀線相交的地方斷開。
''' 刀線要真的穿過圖形才會切（只碰到一邊不算）。
''' </summary>
Public NotInheritable Class PathKnife
    Private Sub New()
    End Sub

    ''' <summary>切縫寬度（照片高度單位，約 0.15 像素）。</summary>
    Private Const Gap As Double = 0.00015

    ''' <summary>能不能切：向量圖形（圖說、文字、點陣、填滿圖層不行）。</summary>
    Public Shared Function CanCut(d As DrawLayer) As Boolean
        If d Is Nothing OrElse DrawGeometry.HasText(d.Shape) Then Return False
        Select Case d.Shape
            Case DrawShape.Raster, DrawShape.Bucket, DrawShape.Gradient, DrawShape.FillLayer : Return False
        End Select
        Return True
    End Function

    ''' <summary>沿 a→b 切開：回傳切出來的圖層（樣式同原圖層）；沒切到時回傳 Nothing。</summary>
    Public Shared Function Cut(d As DrawLayer, a As PointF, b As PointF) As List(Of DrawLayer)
        If Not CanCut(d) OrElse DrawGeometry.Dist(a, b) < 0.000001 Then Return Nothing
        If PathBoolean.CanCombine(d) Then Return CutClosed(d, a, b)
        Return CutOpen(d, a, b)
    End Function

    Private Shared Function CutClosed(d As DrawLayer, a As PointF, b As PointF) As List(Of DrawLayer)
        Dim region = PathBoolean.RegionOf(d)
        Dim dx = b.X - a.X, dy = b.Y - a.Y
        Dim len = Math.Sqrt(dx * dx + dy * dy)
        Dim nx = CSng(-dy / len * Gap / 2), ny = CSng(dx / len * Gap / 2)
        Dim strip = {New PointF(a.X + nx, a.Y + ny), New PointF(b.X + nx, b.Y + ny), New PointF(b.X - nx, b.Y - ny), New PointF(a.X - nx, a.Y - ny)}
        Dim rings = PathBoolean.Combine({region, New List(Of PointF()) From {strip}}, Function(v) v(0) AndAlso Not v(1))
        Dim groups = PathBoolean.Pieces(rings)
        If groups.Count < 2 Then Return Nothing
        Dim result = groups.Select(Function(g) PathBoolean.MakeLayer(d, g)).Where(Function(x) x IsNot Nothing).ToList()
        Return If(result.Count >= 2, result, Nothing)
    End Function

    Private Shared Function CutOpen(d As DrawLayer, a As PointF, b As PointF) As List(Of DrawLayer)
        Dim p = If(d.Shape = DrawShape.Path, d.Clone(), DrawGeometry.ToPath(d))
        If p Is Nothing OrElse p.Contours Is Nothing Then Return Nothing
        Dim pieces As New List(Of PathContour)()
        Dim any = False
        For Each c In p.Contours
            If c.Closed OrElse c.Nodes.Count < 2 Then pieces.Add(c) : Continue For
            ' 找交點（第幾段、t）
            Dim cuts As New List(Of (Seg As Integer, T As Double))()
            For si = 0 To c.SegmentCount - 1
                Dim s = DrawGeometry.Segment(c, si)
                Const N = 48
                Dim prev = s(0)
                For k = 1 To N
                    Dim q = DrawGeometry.BezierAt(s, k / CDbl(N))
                    Dim u = SegHit(prev, q, a, b)
                    If u >= 0 Then
                        Dim t = (k - 1 + u) / N
                        If t > 0.001 AndAlso t < 0.999 AndAlso Not cuts.Any(Function(x) x.Seg = si AndAlso Math.Abs(x.T - t) < 0.002) Then cuts.Add((si, t))
                    End If
                    prev = q
                Next
            Next
            If cuts.Count = 0 Then pieces.Add(c) : Continue For
            any = True
            ' 從後面插入節點（同一段裡後面的 t 先插，前面的 t 依剩下的長度換算）
            Dim cutNodes As New HashSet(Of PathNode)()
            For Each grp In cuts.GroupBy(Function(x) x.Seg).OrderByDescending(Function(g) g.Key)
                Dim tHigh = 1.0
                For Each x In grp.OrderByDescending(Function(y) y.T)
                    Dim idx = DrawGeometry.InsertNode(c, x.Seg, x.T / tHigh)
                    c.Nodes(idx).Kind = PathNodeKind.Corner
                    cutNodes.Add(c.Nodes(idx))
                    tHigh = x.T
                Next
            Next
            ' 在切點斷開
            Dim cur As New List(Of PathNode)()
            For Each n In c.Nodes
                cur.Add(n)
                If cutNodes.Contains(n) Then
                    If cur.Count >= 2 Then pieces.Add(New PathContour With {.Nodes = cur})
                    cur = New List(Of PathNode) From {n.Clone()}
                End If
            Next
            If cur.Count >= 2 Then pieces.Add(New PathContour With {.Nodes = cur})
        Next
        If Not any Then Return Nothing
        ' 每一段變成一個圖層
        Return pieces.Select(Function(pc)
                                 Dim l = p.Clone()
                                 l.Contours = New List(Of PathContour) From {pc.Clone()}
                                 Return l
                             End Function).ToList()
    End Function

    ''' <summary>線段 p→q 和 a→b 相交時回傳 p→q 上的參數（0..1），否則 −1。</summary>
    Private Shared Function SegHit(p As PointF, q As PointF, a As PointF, b As PointF) As Double
        Dim rx = CDbl(q.X) - p.X, ry = CDbl(q.Y) - p.Y, sx = CDbl(b.X) - a.X, sy = CDbl(b.Y) - a.Y
        Dim den = rx * sy - ry * sx
        If Math.Abs(den) < 1.0E-15 Then Return -1
        Dim qx = CDbl(a.X) - p.X, qy = CDbl(a.Y) - p.Y
        Dim t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den
        If t < 0 OrElse t > 1 OrElse u < 0 OrElse u > 1 Then Return -1
        Return t
    End Function
End Class
