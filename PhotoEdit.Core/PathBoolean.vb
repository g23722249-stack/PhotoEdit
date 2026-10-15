Imports System.Drawing

''' <summary>焊接、修剪、相交、排除（同 Illustrator 的路徑管理員、CorelDRAW 的造形）。</summary>
Public Enum ShapeOp
    ''' <summary>焊接：全部合成一個外形。</summary>
    Weld
    ''' <summary>修剪：最下面的圖形減掉上面所有的圖形。</summary>
    Trim
    ''' <summary>相交：只留下全部重疊的地方。</summary>
    Intersect
    ''' <summary>排除：重疊奇數次的地方留下（重疊兩層的挖空）。</summary>
    Exclude
End Enum

''' <summary>形狀建立工具的一塊區域：外框與裡面的洞（外框逆時針、洞順時針，以「內部在左邊」為準）、屬於哪些圖形。</summary>
Public NotInheritable Class ShapeFace
    Public Property Rings As List(Of PointF())
    ''' <summary>這一塊在第幾個輸入圖形裡面（位元）。</summary>
    Public Property Mask As Integer
    Public Property Bounds As RectangleF
End Class

''' <summary>
''' 多邊形布林運算：把所有圖形的外形線（取樣後的折線）在交點切開，每一小段看左右兩側在不在結果裡，
''' 兩側不同的就是結果的邊界，再接成一圈一圈；最後用貝茲曲線擬合回鋼筆路徑（直線保持直線、尖角保持尖角）。
''' 判斷「在圖形裡」用非零環繞數，和算圖的 FillMode.Winding 一樣。
''' </summary>
Public NotInheritable Class PathBoolean

    Private Structure P2
        Public X As Double
        Public Y As Double
        Public Sub New(x As Double, y As Double)
            Me.X = x : Me.Y = y
        End Sub
    End Structure

    Private Const SideOffset As Double = 0.000002
    Private Const MergeEps As Double = 0.00000005

    '=====================================================================
    ' 對外
    '=====================================================================

    ''' <summary>能不能參加焊接修剪：封閉的向量圖形（圖說、點陣、填滿圖層、開放的線不行）。</summary>
    Public Shared Function CanCombine(d As DrawLayer) As Boolean
        If d Is Nothing OrElse DrawGeometry.HasText(d.Shape) Then Return False
        Select Case d.Shape
            Case DrawShape.Raster, DrawShape.Bucket, DrawShape.Gradient, DrawShape.FillLayer : Return False
        End Select
        If Not DrawGeometry.IsClosedLayer(d) Then Return False
        Return RegionOf(d).Count > 0
    End Function

    ''' <summary>圖形的區域（封閉的外形線）。</summary>
    Public Shared Function RegionOf(d As DrawLayer) As List(Of PointF())
        Return DrawGeometry.Figures(d).Where(Function(f) f.Closed AndAlso f.Points.Length >= 3).Select(Function(f) f.Points).ToList()
    End Function

    ''' <summary>
    ''' 套用造形（layers 由下而上）：回傳新的鋼筆路徑圖層；結果是空的時回傳 Nothing。
    ''' 樣式：修剪用最下面的圖形，其他用最上面的（同 Illustrator）。
    ''' </summary>
    Public Shared Function Apply(layers As IList(Of DrawLayer), op As ShapeOp) As DrawLayer
        If layers.Count < 2 Then Return Nothing
        Dim regions = layers.Select(Function(d) RegionOf(d)).ToList()
        Dim pred As Func(Of Boolean(), Boolean)
        Select Case op
            Case ShapeOp.Weld : pred = Function(v) v.Any(Function(x) x)
            Case ShapeOp.Trim : pred = Function(v) v(0) AndAlso Not v.Skip(1).Any(Function(x) x)
            Case ShapeOp.Intersect : pred = Function(v) v.All(Function(x) x)
            Case Else : pred = Function(v) v.Count(Function(x) x) Mod 2 = 1
        End Select
        Dim rings = Combine(regions, pred)
        If rings.Count = 0 Then Return Nothing
        Dim source = If(op = ShapeOp.Trim, layers(0), layers(layers.Count - 1))
        Return MakeLayer(source, rings)
    End Function

    ''' <summary>用 source 的樣式建立鋼筆路徑圖層（外形是 rings，世界座標）。</summary>
    Public Shared Function MakeLayer(source As DrawLayer, rings As IEnumerable(Of PointF())) As DrawLayer
        Dim r = source.Clone()
        r.Shape = DrawShape.Path
        r.Symmetry = SymmetryKind.None
        r.Rotation = 0
        r.Points = Nothing
        r.Contours = rings.Select(Function(x) FitRing(x, 0.0007)).Where(Function(c) c IsNot Nothing).ToList()
        If r.Contours.Count = 0 Then Return Nothing
        Return r
    End Function

    ''' <summary>
    ''' 一般化的布林運算：regions(i) 是第 i 個圖形的外形線；inside(v) 決定某點（v(i)＝在不在第 i 個圖形裡）在不在結果裡。
    ''' 回傳結果的外形線（內部在左邊：外框逆時針、洞順時針，面積正負相反）。
    ''' </summary>
    Public Shared Function Combine(regions As IList(Of List(Of PointF())), inside As Func(Of Boolean(), Boolean)) As List(Of PointF())
        Dim g = BuildGraph(regions)
        Dim kept As New List(Of (A As Integer, B As Integer))()
        For Each e In g.Edges
            Dim l = inside(e.Left), rr = inside(e.Right)
            If l = rr Then Continue For
            kept.Add(If(l, (e.A, e.B), (e.B, e.A)))
        Next
        Return Link(kept, g.Verts)
    End Function

    ''' <summary>形狀建立：所有圖形互相切出來的每一塊（相連的同一種重疊組合算一塊）。</summary>
    Public Shared Function Faces(regions As IList(Of List(Of PointF()))) As List(Of ShapeFace)
        Dim g = BuildGraph(regions)
        Dim toMask = Function(v As Boolean()) As Integer
                         Dim m = 0
                         For i = 0 To v.Length - 1
                             If v(i) Then m = m Or (1 << i)
                         Next
                         Return m
                     End Function
        Dim masks As New HashSet(Of Integer)()
        Dim sides = g.Edges.Select(Function(e) (e.A, e.B, L:=toMask(e.Left), R:=toMask(e.Right))).ToList()
        For Each s In sides
            masks.Add(s.L) : masks.Add(s.R)
        Next
        masks.Remove(0)
        Dim result As New List(Of ShapeFace)()
        For Each m In masks.OrderBy(Function(x) x)
            Dim kept As New List(Of (A As Integer, B As Integer))()
            For Each s In sides
                Dim l = s.L = m, r = s.R = m
                If l = r Then Continue For
                kept.Add(If(l, (s.A, s.B), (s.B, s.A)))
            Next
            Dim rings = Link(kept, g.Verts)
            For Each grp In SplitFaces(rings)
                ' 細條碎片（相鄰的圖形擬合後邊界差一點點）不算一塊：平均寬度＝2×面積÷周長
                Dim area = grp.Sum(Function(x) SignedArea(x))
                Dim perim = grp.Sum(Function(x) Enumerable.Range(0, x.Length).Sum(Function(i) DrawGeometry.Dist(x(i), x((i + 1) Mod x.Length))))
                If perim <= 0 OrElse 2 * area / perim < 0.0015 Then Continue For
                Dim pts = grp.SelectMany(Function(x) x)
                Dim b = RectangleF.FromLTRB(pts.Min(Function(p) p.X), pts.Min(Function(p) p.Y), pts.Max(Function(p) p.X), pts.Max(Function(p) p.Y))
                result.Add(New ShapeFace With {.Rings = grp, .Mask = m, .Bounds = b})
            Next
        Next
        Return result
    End Function

    ''' <summary>把外形線分成一塊一塊（外框加上它裡面的洞）。</summary>
    Public Shared Function Pieces(rings As List(Of PointF())) As List(Of List(Of PointF()))
        Return SplitFaces(rings)
    End Function

    ''' <summary>點在不在這些外形線裡（非零環繞數）。</summary>
    Public Shared Function Contains(rings As IEnumerable(Of PointF()), p As PointF) As Boolean
        Dim w = 0
        For Each r In rings
            w += Winding(r, p.X, p.Y)
        Next
        Return w <> 0
    End Function

    ''' <summary>有號面積（內部在左邊為正）。</summary>
    Public Shared Function SignedArea(r As PointF()) As Double
        Dim s = 0.0
        For i = 0 To r.Length - 1
            Dim a = r(i), b = r((i + 1) Mod r.Length)
            s += CDbl(a.X) * b.Y - CDbl(b.X) * a.Y
        Next
        Return s / 2
    End Function

    '=====================================================================
    ' 切開與分類
    '=====================================================================

    Private Class Graph
        Public Verts As New List(Of P2)()
        Public Edges As New List(Of SubEdge)()
    End Class

    Private Class SubEdge
        Public A As Integer
        Public B As Integer
        Public Left As Boolean()
        Public Right As Boolean()
    End Class

    Private Structure RawEdge
        Public A As P2
        Public B As P2
        Public Src As Integer
        Public MinX As Double
        Public MaxX As Double
        Public MinY As Double
        Public MaxY As Double
    End Structure

    Private Shared Function BuildGraph(regions As IList(Of List(Of PointF()))) As Graph
        ' 所有邊
        Dim edges As New List(Of RawEdge)()
        For si = 0 To regions.Count - 1
            For Each ring In regions(si)
                For i = 0 To ring.Length - 1
                    Dim a = New P2(ring(i).X, ring(i).Y), b = New P2(ring((i + 1) Mod ring.Length).X, ring((i + 1) Mod ring.Length).Y)
                    If Math.Abs(a.X - b.X) < 0.0000000001 AndAlso Math.Abs(a.Y - b.Y) < 0.0000000001 Then Continue For
                    edges.Add(New RawEdge With {.A = a, .B = b, .Src = si, .MinX = Math.Min(a.X, b.X), .MaxX = Math.Max(a.X, b.X),
                                                .MinY = Math.Min(a.Y, b.Y), .MaxY = Math.Max(a.Y, b.Y)})
                Next
            Next
        Next
        ' 交點（依 MinX 排序後掃描）
        Dim cuts = Enumerable.Range(0, edges.Count).Select(Function(x) New List(Of (T As Double, P As P2))()).ToArray()
        Dim order = Enumerable.Range(0, edges.Count).OrderBy(Function(i) edges(i).MinX).ToArray()
        For oi = 0 To order.Length - 1
            Dim i = order(oi)
            Dim ei = edges(i)
            For oj = oi + 1 To order.Length - 1
                Dim j = order(oj)
                Dim ej = edges(j)
                If ej.MinX > ei.MaxX + MergeEps Then Exit For
                If ej.MinY > ei.MaxY + MergeEps OrElse ej.MaxY < ei.MinY - MergeEps Then Continue For
                Intersect(ei, ej, cuts(i), cuts(j))
            Next
        Next
        ' 切開、合併很近的頂點
        Dim g As New Graph()
        Dim grid As New Dictionary(Of (Long, Long), List(Of Integer))()
        Dim vid = Function(p As P2) As Integer
                      Dim kx = CLng(Math.Floor(p.X / (MergeEps * 4))), ky = CLng(Math.Floor(p.Y / (MergeEps * 4)))
                      For dx = -1L To 1L
                          For dy = -1L To 1L
                              Dim lst As List(Of Integer) = Nothing
                              If grid.TryGetValue((kx + dx, ky + dy), lst) Then
                                  For Each v In lst
                                      If Math.Abs(g.Verts(v).X - p.X) <= MergeEps AndAlso Math.Abs(g.Verts(v).Y - p.Y) <= MergeEps Then Return v
                                  Next
                              End If
                          Next
                      Next
                      g.Verts.Add(p)
                      Dim id = g.Verts.Count - 1
                      Dim cell As List(Of Integer) = Nothing
                      If Not grid.TryGetValue((kx, ky), cell) Then cell = New List(Of Integer)() : grid((kx, ky)) = cell
                      cell.Add(id)
                      Return id
                  End Function
        Dim seen As New HashSet(Of (Integer, Integer))()
        Dim subs As New List(Of (A As Integer, B As Integer))()
        For i = 0 To edges.Count - 1
            Dim e = edges(i)
            Dim pts As New List(Of (T As Double, P As P2)) From {(0.0, e.A)}
            pts.AddRange(cuts(i).Where(Function(c) c.T > 0 AndAlso c.T < 1))
            pts.Add((1.0, e.B))
            pts.Sort(Function(x, y) x.T.CompareTo(y.T))
            Dim prev = vid(pts(0).P)
            For k = 1 To pts.Count - 1
                Dim cur = vid(pts(k).P)
                If cur <> prev Then
                    Dim key = (Math.Min(prev, cur), Math.Max(prev, cur))
                    If seen.Add(key) Then subs.Add((prev, cur)) ' 重疊的邊只留一條
                End If
                prev = cur
            Next
        Next
        ' 每一小段左右兩側在不在各個圖形裡
        Dim allEdges = edges ' 用原始的邊算環繞數
        For Each s In subs
            Dim a = g.Verts(s.A), b = g.Verts(s.B)
            Dim dx = b.X - a.X, dy = b.Y - a.Y
            Dim len = Math.Sqrt(dx * dx + dy * dy)
            Dim mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2
            Dim off = Math.Min(SideOffset, len / 4)
            Dim nx = -dy / len * off, ny = dx / len * off
            g.Edges.Add(New SubEdge With {.A = s.A, .B = s.B,
                                          .Left = InsideVector(allEdges, regions.Count, mx + nx, my + ny),
                                          .Right = InsideVector(allEdges, regions.Count, mx - nx, my - ny)})
        Next
        Return g
    End Function

    Private Shared Function InsideVector(edges As List(Of RawEdge), n As Integer, x As Double, y As Double) As Boolean()
        Dim w(n - 1) As Integer
        For Each e In edges
            If e.MinY > y OrElse e.MaxY < y Then Continue For
            If e.A.Y <= y Then
                If e.B.Y > y AndAlso Cross(e.A, e.B, x, y) > 0 Then w(e.Src) += 1
            ElseIf e.B.Y <= y AndAlso Cross(e.A, e.B, x, y) < 0 Then
                w(e.Src) -= 1
            End If
        Next
        Return w.Select(Function(k) k <> 0).ToArray()
    End Function

    Private Shared Function Cross(a As P2, b As P2, x As Double, y As Double) As Double
        Return (b.X - a.X) * (y - a.Y) - (x - a.X) * (b.Y - a.Y)
    End Function

    Private Shared Function Winding(r As PointF(), x As Double, y As Double) As Integer
        Dim w = 0
        For i = 0 To r.Length - 1
            Dim a = New P2(r(i).X, r(i).Y), b = New P2(r((i + 1) Mod r.Length).X, r((i + 1) Mod r.Length).Y)
            If a.Y <= y Then
                If b.Y > y AndAlso Cross(a, b, x, y) > 0 Then w += 1
            ElseIf b.Y <= y AndAlso Cross(a, b, x, y) < 0 Then
                w -= 1
            End If
        Next
        Return w
    End Function

    ''' <summary>兩條邊的交點（含端點碰到、平行重疊）加到兩邊的切點清單。</summary>
    Private Shared Sub Intersect(e1 As RawEdge, e2 As RawEdge, c1 As List(Of (T As Double, P As P2)), c2 As List(Of (T As Double, P As P2)))
        Dim rx = e1.B.X - e1.A.X, ry = e1.B.Y - e1.A.Y
        Dim sx = e2.B.X - e2.A.X, sy = e2.B.Y - e2.A.Y
        Dim den = rx * sy - ry * sx
        Dim qx = e2.A.X - e1.A.X, qy = e2.A.Y - e1.A.Y
        Dim l1 = Math.Sqrt(rx * rx + ry * ry), l2 = Math.Sqrt(sx * sx + sy * sy)
        If Math.Abs(den) <= 0.000000001 * l1 * l2 Then
            ' 平行：在同一條線上時，把彼此的端點加進去
            If Math.Abs(qx * ry - qy * rx) > MergeEps * l1 Then Return
            Dim proj = Sub(pt As P2, ax As Double, ay As Double, vx As Double, vy As Double, ll As Double, list As List(Of (T As Double, P As P2)))
                           Dim t = ((pt.X - ax) * vx + (pt.Y - ay) * vy) / (ll * ll)
                           If t > 0.0000001 AndAlso t < 0.9999999 Then list.Add((t, pt))
                       End Sub
            proj(e2.A, e1.A.X, e1.A.Y, rx, ry, l1, c1) : proj(e2.B, e1.A.X, e1.A.Y, rx, ry, l1, c1)
            proj(e1.A, e2.A.X, e2.A.Y, sx, sy, l2, c2) : proj(e1.B, e2.A.X, e2.A.Y, sx, sy, l2, c2)
            Return
        End If
        Dim t1 = (qx * sy - qy * sx) / den
        Dim t2 = (qx * ry - qy * rx) / den
        Dim tol1 = MergeEps / l1, tol2 = MergeEps / l2
        If t1 < -tol1 OrElse t1 > 1 + tol1 OrElse t2 < -tol2 OrElse t2 > 1 + tol2 Then Return
        ' 碰到端點時用端點本身的座標（頂點才接得起來）
        Dim p As P2
        If t2 <= tol2 Then
            p = e2.A
        ElseIf t2 >= 1 - tol2 Then
            p = e2.B
        ElseIf t1 <= tol1 Then
            p = e1.A
        ElseIf t1 >= 1 - tol1 Then
            p = e1.B
        Else
            p = New P2(e1.A.X + rx * t1, e1.A.Y + ry * t1)
        End If
        c1.Add((Math.Max(0, Math.Min(1, t1)), p))
        c2.Add((Math.Max(0, Math.Min(1, t2)), p))
    End Sub

    '=====================================================================
    ' 接成一圈一圈
    '=====================================================================

    Private Shared Function Link(kept As List(Of (A As Integer, B As Integer)), verts As List(Of P2)) As List(Of PointF())
        Dim outgoing As New Dictionary(Of Integer, List(Of Integer))()
        For i = 0 To kept.Count - 1
            Dim lst As List(Of Integer) = Nothing
            If Not outgoing.TryGetValue(kept(i).A, lst) Then lst = New List(Of Integer)() : outgoing(kept(i).A) = lst
            lst.Add(i)
        Next
        Dim used(kept.Count - 1) As Boolean
        Dim rings As New List(Of PointF())()
        For start = 0 To kept.Count - 1
            If used(start) Then Continue For
            Dim loopIds As New List(Of Integer)()
            Dim cur = start
            Dim guard = 0
            Do
                used(cur) = True
                loopIds.Add(kept(cur).A)
                Dim v = kept(cur).B
                If v = kept(start).A Then Exit Do
                Dim lst As List(Of Integer) = Nothing
                If Not outgoing.TryGetValue(v, lst) Then Exit Do
                ' 有好幾條可以走時，往左轉最多的（相碰的兩塊分開成兩圈）
                Dim a = verts(kept(cur).A), b = verts(v)
                Dim ix = b.X - a.X, iy = b.Y - a.Y
                Dim best = -1, bestAng = Double.NegativeInfinity
                For Each k In lst
                    If used(k) Then Continue For
                    Dim c = verts(kept(k).B)
                    Dim ox = c.X - b.X, oy = c.Y - b.Y
                    Dim ang = Math.Atan2(ix * oy - iy * ox, ix * ox + iy * oy)
                    If ang > bestAng Then bestAng = ang : best = k
                Next
                If best < 0 Then Exit Do
                cur = best
                guard += 1
            Loop While guard <= kept.Count
            If loopIds.Count >= 3 Then
                Dim pts = CleanRing(loopIds.Select(Function(i) New PointF(CSng(verts(i).X), CSng(verts(i).Y))).ToList())
                If pts.Length >= 3 AndAlso Math.Abs(SignedArea(pts)) > 0.0000001 Then rings.Add(pts)
            End If
        Next
        Return rings
    End Function

    ''' <summary>拿掉重複點與一直線上的中間點。</summary>
    Private Shared Function CleanRing(pts As List(Of PointF)) As PointF()
        Dim changed = True
        While changed AndAlso pts.Count >= 3
            changed = False
            For i = pts.Count - 1 To 0 Step -1
                If pts.Count < 3 Then Exit For
                Dim a = pts((i - 1 + pts.Count) Mod pts.Count), b = pts(i), c = pts((i + 1) Mod pts.Count)
                Dim cr = CDbl(b.X - a.X) * (c.Y - a.Y) - CDbl(c.X - a.X) * (b.Y - a.Y)
                Dim l = DrawGeometry.Dist(a, c)
                If DrawGeometry.Dist(a, b) < 0.00000001 OrElse (Math.Abs(cr) < 0.000000001 * Math.Max(l, 0.0001) AndAlso
                   (CDbl(b.X - a.X) * (c.X - b.X) + CDbl(b.Y - a.Y) * (c.Y - b.Y)) >= 0) Then
                    pts.RemoveAt(i)
                    changed = True
                End If
            Next
        End While
        Return pts.ToArray()
    End Function

    ''' <summary>把一圈一圈分成一塊一塊：每個外框（面積正）加上直接在它裡面的洞（面積負）。</summary>
    Private Shared Function SplitFaces(rings As List(Of PointF())) As List(Of List(Of PointF()))
        Dim outers = rings.Where(Function(r) SignedArea(r) > 0).OrderBy(Function(r) Math.Abs(SignedArea(r))).ToList()
        Dim groups = outers.Select(Function(o) New List(Of PointF()) From {o}).ToList()
        For Each h In rings.Where(Function(r) SignedArea(r) < 0)
            ' 洞上一點（第一段中間往洞的外側＝右邊偏一點，那裡在外框的區域裡）
            Dim a = h(0), b = h(1)
            Dim dx = b.X - a.X, dy = b.Y - a.Y
            Dim len = Math.Max(0.0000001, Math.Sqrt(dx * dx + dy * dy))
            Dim p = New PointF(CSng((a.X + b.X) / 2 + dy / len * SideOffset * 4), CSng((a.Y + b.Y) / 2 - dx / len * SideOffset * 4))
            For gi = 0 To outers.Count - 1
                If Winding(outers(gi), p.X, p.Y) <> 0 Then groups(gi).Add(h) : Exit For
            Next
        Next
        Return groups
    End Function

    '=====================================================================
    ' 擬合成貝茲曲線（Schneider 演算法）
    '=====================================================================

    ''' <summary>封閉折線擬合成鋼筆路徑：轉角大於 32° 的點是尖角，其餘用平滑的三次貝茲曲線（誤差在 tol 以內）。</summary>
    Public Shared Function FitRing(ring As PointF(), tol As Double) As PathContour
        Dim d = CleanRing(ring.ToList()).Select(Function(p) New P2(p.X, p.Y)).ToList()
        Dim n = d.Count
        If n < 3 Then Return Nothing
        Dim corner(n - 1) As Boolean
        For i = 0 To n - 1
            Dim a = d((i - 1 + n) Mod n), b = d(i), c = d((i + 1) Mod n)
            Dim ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y
            Dim ang = Math.Abs(Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy))
            ' 長直邊的兩端也當尖角（直線和曲線接起來的地方保持直線）
            Dim lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy)
            corner(i) = ang > 32 * Math.PI / 180 OrElse (ang > 4 * Math.PI / 180 AndAlso Math.Max(lu, lv) > 6 * Math.Max(Math.Min(lu, lv), 0.000001))
        Next
        Dim corners = Enumerable.Range(0, n).Where(Function(i) corner(i)).ToList()
        Dim beziers As New List(Of P2())()
        Dim kinds As New List(Of PathNodeKind)()
        If corners.Count = 0 Then
            ' 沒有尖角：從第 0 點繞一圈，起點平滑
            Dim pts = d.Concat({d(0)}).ToList()
            Dim t1 = Unit(Sub2(d(1), d(n - 1)))
            Dim segs As New List(Of P2())()
            FitCubic(pts, 0, pts.Count - 1, t1, Neg(t1), tol, segs)
            beziers.AddRange(segs)
            For k = 0 To segs.Count - 1
                kinds.Add(PathNodeKind.Smooth)
            Next
        Else
            For ci = 0 To corners.Count - 1
                Dim s = corners(ci), e = corners((ci + 1) Mod corners.Count)
                Dim run As New List(Of P2)()
                Dim k = s
                Do
                    run.Add(d(k))
                    If k = e AndAlso run.Count > 1 Then Exit Do
                    k = (k + 1) Mod n
                Loop
                Dim segs As New List(Of P2())()
                If run.Count = 2 OrElse MaxChordDist(run) <= tol * 0.5 Then
                    Dim a = run(0), b = run(run.Count - 1)
                    segs.Add({a, a, b, b})
                Else
                    FitCubic(run, 0, run.Count - 1, Unit(Sub2(run(1), run(0))), Unit(Sub2(run(run.Count - 2), run(run.Count - 1))), tol, segs)
                End If
                For j = 0 To segs.Count - 1
                    beziers.Add(segs(j))
                    kinds.Add(If(j = 0, PathNodeKind.Corner, PathNodeKind.Smooth))
                Next
            Next
        End If
        ' 一段一段變成節點：第 k 段的起點是第 k 個節點
        Dim c2 As New PathContour With {.Closed = True}
        For k = 0 To beziers.Count - 1
            Dim bz = beziers(k)
            Dim prev = beziers((k - 1 + beziers.Count) Mod beziers.Count)
            c2.Nodes.Add(New PathNode With {.X = CSng(bz(0).X), .Y = CSng(bz(0).Y), .OutX = CSng(bz(1).X), .OutY = CSng(bz(1).Y),
                                            .InX = CSng(prev(2).X), .InY = CSng(prev(2).Y), .Kind = kinds(k)})
        Next
        Return If(c2.Nodes.Count >= 2, c2, Nothing)
    End Function

    Private Shared Function MaxChordDist(run As List(Of P2)) As Double
        Dim a = run(0), b = run(run.Count - 1)
        Dim m = 0.0
        For i = 1 To run.Count - 2
            m = Math.Max(m, SegDist(run(i), a, b))
        Next
        Return m
    End Function

    Private Shared Sub FitCubic(d As List(Of P2), first As Integer, last As Integer, t1 As P2, t2 As P2, tol As Double, out As List(Of P2()))
        Dim nPts = last - first + 1
        If nPts = 2 Then
            Dim dist = Len(Sub2(d(last), d(first))) / 3
            out.Add({d(first), Add2(d(first), Mul(t1, dist)), Add2(d(last), Mul(t2, dist)), d(last)})
            Return
        End If
        Dim u = ChordParams(d, first, last)
        Dim bez = GenerateBezier(d, first, last, u, t1, t2)
        Dim split As Integer
        Dim maxErr = MaxError(d, first, last, bez, u, split)
        If maxErr < tol Then out.Add(bez) : Return
        If maxErr < tol * 4 Then
            For iter = 1 To 6
                u = Reparameterize(d, first, last, u, bez)
                bez = GenerateBezier(d, first, last, u, t1, t2)
                maxErr = MaxError(d, first, last, bez, u, split)
                If maxErr < tol Then out.Add(bez) : Return
            Next
        End If
        split = Math.Max(first + 1, Math.Min(last - 1, split))
        Dim tc = Unit(Sub2(d(split - 1), d(split + 1)))
        FitCubic(d, first, split, t1, tc, tol, out)
        FitCubic(d, split, last, Neg(tc), t2, tol, out)
    End Sub

    Private Shared Function ChordParams(d As List(Of P2), first As Integer, last As Integer) As Double()
        Dim u(last - first) As Double
        For i = first + 1 To last
            u(i - first) = u(i - first - 1) + Len(Sub2(d(i), d(i - 1)))
        Next
        Dim total = u(last - first)
        If total > 0 Then
            For i = 0 To u.Length - 1
                u(i) /= total
            Next
        End If
        Return u
    End Function

    Private Shared Function GenerateBezier(d As List(Of P2), first As Integer, last As Integer, u As Double(), t1 As P2, t2 As P2) As P2()
        Dim p0 = d(first), p3 = d(last)
        Dim c00, c01, c11, x0, x1 As Double
        For i = 0 To u.Length - 1
            Dim t = u(i), mt = 1 - t
            Dim b0 = mt * mt * mt, b1 = 3 * t * mt * mt, b2 = 3 * t * t * mt, b3 = t * t * t
            Dim a1 = Mul(t1, b1), a2 = Mul(t2, b2)
            c00 += Dot(a1, a1) : c01 += Dot(a1, a2) : c11 += Dot(a2, a2)
            Dim tmp = Sub2(d(first + i), Add2(Mul(p0, b0 + b1), Mul(p3, b2 + b3)))
            x0 += Dot(a1, tmp) : x1 += Dot(a2, tmp)
        Next
        Dim det = c00 * c11 - c01 * c01
        Dim al = 0.0, ar = 0.0
        If Math.Abs(det) > 1.0E-18 Then
            al = (x0 * c11 - x1 * c01) / det
            ar = (c00 * x1 - c01 * x0) / det
        End If
        Dim segLen = Len(Sub2(p3, p0))
        If al < 0.000001 * segLen OrElse ar < 0.000001 * segLen Then
            al = segLen / 3 : ar = segLen / 3
        End If
        Return {p0, Add2(p0, Mul(t1, al)), Add2(p3, Mul(t2, ar)), p3}
    End Function

    Private Shared Function MaxError(d As List(Of P2), first As Integer, last As Integer, bez As P2(), u As Double(), ByRef split As Integer) As Double
        Dim m = 0.0
        split = (first + last) \ 2
        For i = first + 1 To last - 1
            Dim p = BezierPt(bez, u(i - first))
            Dim e = Len(Sub2(p, d(i)))
            If e > m Then m = e : split = i
        Next
        Return m
    End Function

    Private Shared Function Reparameterize(d As List(Of P2), first As Integer, last As Integer, u As Double(), bez As P2()) As Double()
        Dim r(u.Length - 1) As Double
        For i = 0 To u.Length - 1
            r(i) = Newton(bez, d(first + i), u(i))
        Next
        Return r
    End Function

    Private Shared Function Newton(q As P2(), p As P2, u As Double) As Double
        Dim q1 = {Mul(Sub2(q(1), q(0)), 3), Mul(Sub2(q(2), q(1)), 3), Mul(Sub2(q(3), q(2)), 3)}
        Dim q2 = {Mul(Sub2(q1(1), q1(0)), 2), Mul(Sub2(q1(2), q1(1)), 2)}
        Dim qu = BezierPt(q, u)
        Dim mt = 1 - u
        Dim q1u = Add2(Add2(Mul(q1(0), mt * mt), Mul(q1(1), 2 * u * mt)), Mul(q1(2), u * u))
        Dim q2u = Add2(Mul(q2(0), mt), Mul(q2(1), u))
        Dim num = Dot(Sub2(qu, p), q1u)
        Dim den = Dot(q1u, q1u) + Dot(Sub2(qu, p), q2u)
        If Math.Abs(den) < 1.0E-18 Then Return u
        Return Math.Max(0, Math.Min(1, u - num / den))
    End Function

    Private Shared Function BezierPt(b As P2(), t As Double) As P2
        Dim mt = 1 - t
        Return New P2(mt * mt * mt * b(0).X + 3 * mt * mt * t * b(1).X + 3 * mt * t * t * b(2).X + t * t * t * b(3).X,
                      mt * mt * mt * b(0).Y + 3 * mt * mt * t * b(1).Y + 3 * mt * t * t * b(2).Y + t * t * t * b(3).Y)
    End Function

    Private Shared Function SegDist(p As P2, a As P2, b As P2) As Double
        Dim vx = b.X - a.X, vy = b.Y - a.Y
        Dim l2 = vx * vx + vy * vy
        Dim t = If(l2 <= 0, 0, Math.Max(0, Math.Min(1, ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2)))
        Return Math.Sqrt((p.X - a.X - t * vx) ^ 2 + (p.Y - a.Y - t * vy) ^ 2)
    End Function

    Private Shared Function Sub2(a As P2, b As P2) As P2
        Return New P2(a.X - b.X, a.Y - b.Y)
    End Function
    Private Shared Function Add2(a As P2, b As P2) As P2
        Return New P2(a.X + b.X, a.Y + b.Y)
    End Function
    Private Shared Function Mul(a As P2, k As Double) As P2
        Return New P2(a.X * k, a.Y * k)
    End Function
    Private Shared Function Neg(a As P2) As P2
        Return New P2(-a.X, -a.Y)
    End Function
    Private Shared Function Dot(a As P2, b As P2) As Double
        Return a.X * b.X + a.Y * b.Y
    End Function
    Private Shared Function Len(a As P2) As Double
        Return Math.Sqrt(a.X * a.X + a.Y * a.Y)
    End Function
    Private Shared Function Unit(a As P2) As P2
        Dim l = Len(a)
        Return If(l < 1.0E-15, New P2(1, 0), New P2(a.X / l, a.Y / l))
    End Function
End Class
