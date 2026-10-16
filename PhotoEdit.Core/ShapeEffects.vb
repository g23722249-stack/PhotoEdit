Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>變形效果的種類（同 Illustrator 的 Pucker &amp; Bloat、Roughen、Zig Zag、Twist）。</summary>
Public Enum DistortKind
    ''' <summary>收縮與膨脹：Amount −100（尖刺）～ 100（鼓起）。</summary>
    PuckerBloat = 0
    ''' <summary>粗糙：Amount＝起伏大小（外框長邊的 %），Detail＝每單位長度幾個點。</summary>
    Roughen = 1
    ''' <summary>鋸齒（拉鍊）：Amount＝起伏大小（外框長邊的 %），Detail＝每一段幾個起伏。</summary>
    ZigZag = 2
    ''' <summary>扭轉：Amount＝角度（中心轉最多，邊緣不轉）。</summary>
    Twist = 3
End Enum

Public NotInheritable Class DistortOptions
    Public Property Kind As DistortKind
    Public Property Amount As Double = 30
    Public Property Detail As Double = 4
    ''' <summary>平滑（曲線）或尖角（折線）；收縮膨脹、扭轉不看這個。</summary>
    Public Property Smooth As Boolean
    Public Property Seed As Integer = 1
End Class

''' <summary>向量修飾筆刷的模式（同 CorelDRAW 的塗抹、轉動、吸引、排斥、粗糙筆刷；Illustrator 的平滑工具）。</summary>
Public Enum TouchUpMode
    Smear = 0
    Twirl = 1
    Attract = 2
    Repel = 3
    Roughen = 4
    Smooth = 5
End Enum

''' <summary>
''' 向量效果（CorelDRAW 的輪廓圖、扭曲、立體化、減少節點；Illustrator 的 Offset Path、Distort &amp; Transform、3D Extrude、Simplify）。
''' 輪廓圖、立體化把圖形的範圍（填色＋線條）畫成高解析度的遮罩來算（距離場、掃掠），再描回外形線、擬合成貝茲曲線；
''' 變形效果與簡化直接改鋼筆路徑的節點。結果都是新的鋼筆路徑圖層，樣式沿用原本的。
''' </summary>
Public NotInheritable Class VectorEffects

    ''' <summary>遮罩長邊的像素數（夠細緻、算得快）。</summary>
    Private Const MaskResolution As Integer = 2000

    Private Sub New()
    End Sub

    '=====================================================================
    ' 共用：轉成路徑、遮罩
    '=====================================================================

    ''' <summary>
    ''' 效果用的鋼筆路徑：一般圖形用 ToPath；文字、圖說把每個字（和框）的外框併成一個路徑。不能轉時回傳 Nothing。
    ''' </summary>
    Public Shared Function EffectPath(layer As DrawLayer) As DrawLayer
        If layer Is Nothing OrElse Not DrawGeometry.CanMirror(layer) Then Return Nothing
        If DrawGeometry.HasText(layer.Shape) AndAlso Not String.IsNullOrWhiteSpace(layer.Text) Then
            Dim parts = DrawGeometry.TextOutlines(layer)
            If parts.Count = 0 Then Return Nothing
            Dim r = parts(parts.Count - 1).Clone()
            r.Contours = parts.SelectMany(Function(p) If(p.Contours, New List(Of PathContour)())).Select(Function(c) c.Clone()).ToList()
            Return If(r.Contours.Count = 0, Nothing, r)
        End If
        Dim path = DrawGeometry.ToPath(layer)
        If path Is Nothing OrElse path.Contours Is Nothing OrElse path.Contours.Count = 0 Then Return Nothing
        Return path
    End Function

    ''' <summary>能不能套效果（向量圖形、文字；點陣、填滿圖層不行）。</summary>
    Public Shared Function CanApply(layer As DrawLayer) As Boolean
        If layer Is Nothing OrElse Not DrawGeometry.CanMirror(layer) Then Return False
        If DrawGeometry.HasText(layer.Shape) Then Return Not String.IsNullOrWhiteSpace(layer.Text) OrElse DrawGeometry.IsCallout(layer.Shape)
        Return DrawGeometry.Figures(layer).Count > 0
    End Function

    ''' <summary>遮罩：世界座標 (Left, Top) 對到像素 (0, 0)，Scale 像素／單位。</summary>
    Private NotInheritable Class MaskGrid
        Public W As Integer, H As Integer
        Public Left As Single, Top As Single, Scale As Single
        Public Mask As Boolean()

        Public Function ToWorld(p As PointF) As PointF
            Return New PointF(Left + p.X / Scale, Top + p.Y / Scale)
        End Function
    End Class

    Private Shared Function MakeGrid(b As RectangleF, pad As Double) As MaskGrid
        Dim w0 = b.Width + 2 * pad, h0 = b.Height + 2 * pad
        Dim scale = CSng(Math.Min(40000, MaskResolution / Math.Max(0.0001, Math.Max(w0, h0))))
        Dim g As New MaskGrid With {.Scale = scale, .Left = CSng(b.Left - pad), .Top = CSng(b.Top - pad)}
        g.W = Math.Max(4, CInt(Math.Ceiling(w0 * scale)) + 2)
        g.H = Math.Max(4, CInt(Math.Ceiling(h0 * scale)) + 2)
        g.Mask = New Boolean(g.W * g.H - 1) {}
        Return g
    End Function

    ''' <summary>在遮罩上畫東西（世界座標），畫完併進 Mask。</summary>
    Private Shared Sub Paint(grid As MaskGrid, draw As Action(Of Graphics))
        Using bmp As New Bitmap(grid.W, grid.H, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.Transparent)
                g.SmoothingMode = SmoothingMode.None
                g.PixelOffsetMode = PixelOffsetMode.Half
                g.ScaleTransform(grid.Scale, grid.Scale)
                g.TranslateTransform(-grid.Left, -grid.Top)
                draw(g)
            End Using
            Dim data = bmp.LockBits(New Rectangle(0, 0, grid.W, grid.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Try
                Dim row(grid.W * 4 - 1) As Byte
                For y = 0 To grid.H - 1
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length)
                    For x = 0 To grid.W - 1
                        If row(x * 4 + 3) > 127 Then grid.Mask(y * grid.W + x) = True
                    Next
                Next
            Finally
                bmp.UnlockBits(data)
            End Try
        End Using
    End Sub

    Private Shared Sub FillRings(g As Graphics, rings As IEnumerable(Of PointF()))
        Using path As New GraphicsPath(FillMode.Winding)
            For Each r In rings
                If r.Length >= 3 Then path.AddPolygon(r)
            Next
            g.FillPath(Brushes.Black, path)
        End Using
    End Sub

    ''' <summary>遮罩描回外形線（世界座標，內部在左邊）。</summary>
    Private Shared Function TraceGrid(grid As MaskGrid, mask As Boolean()) As List(Of PointF())
        Dim result As New List(Of PointF())()
        For Each ring In SmartFill.Trace(mask, grid.W, grid.H)
            Dim simple = DrawGeometry.Simplify(ring, 0.6, True)
            If simple.Length < 3 Then Continue For
            Dim pts = simple.Select(Function(q) grid.ToWorld(q)).ToArray()
            If Math.Abs(PathBoolean.SignedArea(pts)) * grid.Scale * grid.Scale > 6 Then result.Add(pts)
        Next
        Return result
    End Function

    ''' <summary>看得到的範圍：封閉且有填色的區域＋線條。</summary>
    Private Shared Sub PaintArea(grid As MaskGrid, layer As DrawLayer)
        Dim path = If(DrawGeometry.HasText(layer.Shape), EffectPath(layer), Nothing)
        If path IsNot Nothing Then
            Paint(grid, Sub(g) FillRings(g, PathBoolean.RegionOf(path)))
            Return
        End If
        Dim closed = DrawGeometry.IsClosedLayer(layer)
        If closed AndAlso (layer.Filled OrElse Not layer.Stroked) Then Paint(grid, Sub(g) FillRings(g, PathBoolean.RegionOf(layer)))
        If DrawingRenderer.CanOutlineStroke(layer) Then
            Dim rings = DrawingRenderer.StrokeOutlineRings(layer)
            If rings.Count > 0 Then Paint(grid, Sub(g) FillRings(g, rings))
        End If
    End Sub

    Private Shared Function AreaBounds(layer As DrawLayer) As RectangleF
        Dim b = DrawGeometry.Bounds(layer)
        Dim pad = CSng(If(layer.Stroked OrElse Not DrawGeometry.IsClosedLayer(layer), layer.StrokeWidth * 3, 0))
        Return RectangleF.FromLTRB(b.Left - pad, b.Top - pad, b.Right + pad, b.Bottom + pad)
    End Function

    ''' <summary>看得到的範圍（填色＋線條）的外形線，世界座標。</summary>
    Public Shared Function AreaRings(layer As DrawLayer) As List(Of PointF())
        If Not CanApply(layer) Then Return New List(Of PointF())()
        ' 沒有線條的封閉圖形：直接用外形線（精確）
        If Not DrawGeometry.HasText(layer.Shape) AndAlso DrawGeometry.IsClosedLayer(layer) AndAlso Not layer.Stroked Then Return PathBoolean.RegionOf(layer)
        Dim grid = MakeGrid(AreaBounds(layer), 0.002)
        PaintArea(grid, layer)
        Return TraceGrid(grid, grid.Mask)
    End Function

    ''' <summary>
    ''' 平方歐氏距離場（Felzenszwalb）：每個像素到最近的 feature 像素的距離平方（像素）。
    ''' </summary>
    Private Shared Function SquaredDistance(feature As Boolean(), w As Integer, h As Integer) As Single()
        Const Inf As Double = 1.0E+20
        Dim g(w * h - 1) As Single
        For i = 0 To g.Length - 1
            g(i) = If(feature(i), 0F, CSng(Inf))
        Next
        Dim n = Math.Max(w, h)
        Dim f(n - 1) As Double, d(n - 1) As Double, v(n - 1) As Integer, z(n) As Double
        For x = 0 To w - 1
            For y = 0 To h - 1 : f(y) = g(y * w + x) : Next
            Dt1(f, h, d, v, z)
            For y = 0 To h - 1 : g(y * w + x) = CSng(d(y)) : Next
        Next
        For y = 0 To h - 1
            For x = 0 To w - 1 : f(x) = g(y * w + x) : Next
            Dt1(f, w, d, v, z)
            For x = 0 To w - 1 : g(y * w + x) = CSng(d(x)) : Next
        Next
        Return g
    End Function

    Private Shared Sub Dt1(f As Double(), n As Integer, d As Double(), v As Integer(), z As Double())
        Const Inf As Double = 1.0E+30
        Dim k = 0
        v(0) = 0 : z(0) = -Inf : z(1) = Inf
        For q = 1 To n - 1
            Dim s As Double
            Do
                Dim p = v(k)
                s = ((f(q) + CDbl(q) * q) - (f(p) + CDbl(p) * p)) / (2.0 * q - 2.0 * p)
                If s <= z(k) AndAlso k > 0 Then k -= 1 Else Exit Do
            Loop
            If s <= z(k) Then
                ' k = 0 且 s 不比 z(0) 大（只會在數值極端時發生）：直接取代
                v(0) = q : z(0) = -Inf : z(1) = Inf
                Continue For
            End If
            k += 1
            v(k) = q : z(k) = s : z(k + 1) = Inf
        Next
        k = 0
        For q = 0 To n - 1
            While z(k + 1) < q
                k += 1
            End While
            Dim dq = CDbl(q - v(k))
            d(q) = dq * dq + f(v(k))
        Next
    End Sub

    '=====================================================================
    ' 輪廓圖（內縮／外擴）
    '=====================================================================

    ''' <summary>
    ''' 外擴（distance &gt; 0）或內縮（&lt; 0）後的外形線（世界座標）；轉角是圓的（同 CorelDRAW 輪廓圖的圓角、Illustrator 的 Round join）。
    ''' </summary>
    Public Shared Function OffsetRings(layer As DrawLayer, distance As Double) As List(Of PointF())
        Return ContourRings(layer, {distance}).FirstOrDefault()
    End Function

    ''' <summary>一次算好幾個距離（共用同一個距離場）。內縮到沒有東西時那一個是空清單。</summary>
    Private Shared Function ContourRings(layer As DrawLayer, distances As IList(Of Double)) As List(Of List(Of PointF()))
        Dim result As New List(Of List(Of PointF()))()
        If Not CanApply(layer) OrElse distances.Count = 0 Then Return result
        Dim maxOut = Math.Max(0, distances.Max())
        Dim grid = MakeGrid(AreaBounds(layer), maxOut + 0.004)
        PaintArea(grid, layer)
        Dim outside As Single() = Nothing, inside As Single() = Nothing
        For Each dv In distances
            Dim px = dv * grid.Scale
            Dim m(grid.Mask.Length - 1) As Boolean
            If dv >= 0 Then
                If outside Is Nothing Then outside = SquaredDistance(grid.Mask, grid.W, grid.H)
                Dim t = px * px
                For i = 0 To m.Length - 1 : m(i) = outside(i) <= t : Next
            Else
                If inside Is Nothing Then inside = SquaredDistance(grid.Mask.Select(Function(b) Not b).ToArray(), grid.W, grid.H)
                Dim t = px * px
                For i = 0 To m.Length - 1 : m(i) = inside(i) > t : Next
            End If
            result.Add(TraceGrid(grid, m))
        Next
        Return result
    End Function

    ''' <summary>
    ''' 輪廓圖（同 CorelDRAW 的輪廓圖工具）：往外（outside）或往內一層層 steps 圈、每圈間隔 offset（照片高度單位），
    ''' 顏色由原本的填色（沒有填色時用線條色）漸變到 endColor。回傳的圖層由下而上排（外擴時最外圈在最下面，內縮時最內圈在最上面）；
    ''' 每圈是填滿、沒有外框的鋼筆路徑。算不出來（內縮到沒有）的圈省略。
    ''' </summary>
    Public Shared Function Contour(layer As DrawLayer, outside As Boolean, steps As Integer, offset As Double, endColor As Color) As List(Of DrawLayer)
        Dim result As New List(Of DrawLayer)()
        steps = Math.Max(1, Math.Min(100, steps))
        offset = Math.Abs(offset)
        If Not CanApply(layer) OrElse offset <= 0 Then Return result
        Dim dists = Enumerable.Range(1, steps).Select(Function(k) If(outside, k * offset, -k * offset)).ToList()
        Dim rings = ContourRings(layer, dists)
        Dim baseColor = Color.FromArgb(If(layer.Filled AndAlso DrawGeometry.IsClosedLayer(layer), layer.FillColorArgb, layer.StrokeColorArgb))
        If DrawGeometry.HasText(layer.Shape) AndAlso Not DrawGeometry.IsCallout(layer.Shape) Then baseColor = Color.FromArgb(layer.TextColorArgb)
        Dim style = layer.Clone()
        style.Shape = DrawShape.Path
        style.Text = "" : style.TextPathSource = Nothing
        For k = 1 To steps
            If rings(k - 1).Count = 0 Then Continue For
            Dim r = PathBoolean.MakeLayer(style, rings(k - 1))
            If r Is Nothing Then Continue For
            r.Filled = True : r.Stroked = False
            r.FillColorArgb = Lerp(baseColor, endColor, k / CDbl(steps)).ToArgb()
            r.FillGradient = Nothing : r.FadeGradient = Nothing : r.Shadow = False
            r.Brush = BrushKind.HardRound
            r.Strokes = Nothing : r.Points = Nothing
            r.X = 0 : r.Y = 0 : r.W = 0 : r.H = 0
            r.Id = Nothing : r.GroupId = Nothing
            r.Name = Lang.F("輪廓 {0}", k)
            result.Add(r)
        Next
        If outside Then result.Reverse() ' 外擴：最外圈畫在最下面
        Return result
    End Function

    Private Shared Function Lerp(a As Color, b As Color, t As Double) As Color
        t = Math.Max(0, Math.Min(1, t))
        Dim m = Function(x As Integer, y As Integer) CInt(Math.Round(x + (y - x) * t))
        Return Color.FromArgb(m(a.A, b.A), m(a.R, b.R), m(a.G, b.G), m(a.B, b.B))
    End Function

    '=====================================================================
    ' 變形效果
    '=====================================================================

    ''' <summary>套用變形效果，回傳新的鋼筆路徑（樣式不變）；不能套時回傳 Nothing。</summary>
    Public Shared Function Distort(layer As DrawLayer, opt As DistortOptions) As DrawLayer
        Dim path = EffectPath(layer)
        If path Is Nothing Then Return Nothing
        Dim b = DrawGeometry.Bounds(path)
        Dim c As New PointF(b.X + b.Width / 2, b.Y + b.Height / 2)
        Dim size = Math.Max(b.Width, b.Height)
        If size <= 0 Then Return Nothing
        Dim rnd As New Random(opt.Seed)
        Dim result As New List(Of PathContour)()
        For Each pc In path.Contours
            If pc.Nodes.Count < 2 Then Continue For
            Select Case opt.Kind
                Case DistortKind.PuckerBloat
                    result.Add(PuckerBloat(pc, c, opt.Amount / 100))
                Case DistortKind.Twist
                    Dim pts = Resample(DrawGeometry.SampleContour(pc), pc.Closed, size / 160)
                    Dim maxR = Math.Max(0.000001, path.Contours.SelectMany(Function(x) DrawGeometry.SampleContour(x)).Max(Function(p) Dist(p, c)))
                    Dim a = opt.Amount * Math.PI / 180
                    Dim tw = pts.Select(Function(p)
                                            Dim t = a * (1 - Math.Min(1, Dist(p, c) / maxR))
                                            Return Rotate(p, c, t)
                                        End Function).ToArray()
                    result.Add(Rebuild(tw, pc.Closed, size * 0.0015, smooth:=True))
                Case DistortKind.Roughen
                    Dim amp = opt.Amount / 100 * size
                    Dim spacing = 1 / Math.Max(0.5, opt.Detail)
                    Dim pts = Resample(DrawGeometry.SampleContour(pc), pc.Closed, spacing)
                    Dim moved = pts.Select(Function(p, i)
                                               If Not pc.Closed AndAlso (i = 0 OrElse i = pts.Count - 1) Then Return p
                                               Dim ang = rnd.NextDouble() * 2 * Math.PI, rr = Math.Sqrt(rnd.NextDouble()) * amp
                                               Return New PointF(CSng(p.X + rr * Math.Cos(ang)), CSng(p.Y + rr * Math.Sin(ang)))
                                           End Function).ToArray()
                    result.Add(Rebuild(moved, pc.Closed, 0, opt.Smooth))
                Case DistortKind.ZigZag
                    result.Add(ZigZag(pc, opt.Amount / 100 * size, Math.Max(1, CInt(Math.Round(opt.Detail))), opt.Smooth))
            End Select
        Next
        result = result.Where(Function(x) x IsNot Nothing AndAlso x.Nodes.Count >= 2).ToList()
        If result.Count = 0 Then Return Nothing
        Dim r = path.Clone()
        r.Contours = result
        Return r
    End Function

    ''' <summary>
    ''' 收縮與膨脹：錨點往中心（膨脹）或往外（收縮）移，把手往反方向移；a = −1～1。
    ''' 直線段先把把手放在三分點，才有曲線可以鼓起來。
    ''' </summary>
    Private Shared Function PuckerBloat(pc As PathContour, c As PointF, a As Double) As PathContour
        Dim n = pc.Nodes.Count
        Dim nodes = pc.Nodes.Select(Function(x) x.Clone()).ToList()
        For i = 0 To pc.SegmentCount - 1
            Dim s = DrawGeometry.Segment(pc, i)
            If DrawGeometry.IsStraight(s) Then
                Dim p = nodes(i), q = nodes((i + 1) Mod n)
                p.OutX = s(0).X + (s(3).X - s(0).X) / 3 : p.OutY = s(0).Y + (s(3).Y - s(0).Y) / 3
                q.InX = s(0).X + (s(3).X - s(0).X) * 2 / 3 : q.InY = s(0).Y + (s(3).Y - s(0).Y) * 2 / 3
            End If
        Next
        Dim k = CSng(0.5 * a)
        For Each nd In nodes
            nd.X += k * (c.X - nd.X) : nd.Y += k * (c.Y - nd.Y)
            nd.InX += k * (nd.InX - c.X) : nd.InY += k * (nd.InY - c.Y)
            nd.OutX += k * (nd.OutX - c.X) : nd.OutY += k * (nd.OutY - c.Y)
            nd.Kind = PathNodeKind.Corner
        Next
        ' 開放路徑的兩端沒有把手
        If Not pc.Closed Then
            nodes(0).InX = nodes(0).X : nodes(0).InY = nodes(0).Y
            nodes(n - 1).OutX = nodes(n - 1).X : nodes(n - 1).OutY = nodes(n - 1).Y
        End If
        Return New PathContour With {.Nodes = nodes, .Closed = pc.Closed}
    End Function

    ''' <summary>鋸齒：每一段分成 2×ridges 份，分點交錯往兩側（法線方向）移 amp。</summary>
    Private Shared Function ZigZag(pc As PathContour, amp As Double, ridges As Integer, smooth As Boolean) As PathContour
        Dim pts As New List(Of PointF)()
        Dim sign = 1
        Dim parts = 2 * ridges
        For i = 0 To pc.SegmentCount - 1
            Dim s = DrawGeometry.Segment(pc, i)
            For k = 0 To parts - 1
                Dim t = k / CDbl(parts)
                Dim p = DrawGeometry.BezierAt(s, t)
                Dim dt = 0.001
                Dim a = DrawGeometry.BezierAt(s, Math.Max(0, t - dt)), b = DrawGeometry.BezierAt(s, Math.Min(1, t + dt))
                Dim tx = b.X - a.X, ty = b.Y - a.Y
                Dim len = Math.Sqrt(tx * tx + ty * ty)
                If len < 0.0000001 Then
                    pts.Add(p)
                Else
                    pts.Add(New PointF(CSng(p.X - ty / len * amp * sign), CSng(p.Y + tx / len * amp * sign)))
                End If
                sign = -sign
            Next
        Next
        If Not pc.Closed Then pts.Add(pc.Nodes(pc.Nodes.Count - 1).Anchor)
        ' 開放路徑的起點留在原處
        If Not pc.Closed AndAlso pts.Count > 0 Then pts(0) = pc.Nodes(0).Anchor
        Return Rebuild(pts.ToArray(), pc.Closed, 0, smooth)
    End Function

    ''' <summary>一串點變回路徑：平滑（Catmull-Rom 把手）或尖角；simplifyTol &gt; 0 時先簡化。</summary>
    Private Shared Function Rebuild(pts As PointF(), closed As Boolean, simplifyTol As Double, smooth As Boolean) As PathContour
        If pts.Length < 2 Then Return Nothing
        If simplifyTol > 0 Then pts = DrawGeometry.Simplify(pts, simplifyTol, closed)
        If smooth Then
            If closed AndAlso pts.Length >= 3 AndAlso simplifyTol > 0 Then
                Dim fit = PathBoolean.FitRing(pts, simplifyTol)
                If fit IsNot Nothing Then Return fit
            End If
            Return DrawGeometry.SmoothContour(pts, closed)
        End If
        Return New PathContour With {.Nodes = pts.Select(Function(p) New PathNode(p.X, p.Y)).ToList(), .Closed = closed}
    End Function

    ''' <summary>折線加密：每一段切成不超過 spacing 的小段（原本的點都保留）。</summary>
    Public Shared Function Resample(pts As PointF(), closed As Boolean, spacing As Double) As List(Of PointF)
        Dim result As New List(Of PointF)()
        If pts.Length = 0 Then Return result
        spacing = Math.Max(0.00001, spacing)
        Dim n = pts.Length
        Dim segs = If(closed, n, n - 1)
        For i = 0 To segs - 1
            Dim a = pts(i), b = pts((i + 1) Mod n)
            result.Add(a)
            Dim parts = CInt(Math.Min(4000, Math.Ceiling(Dist(a, b) / spacing)))
            For k = 1 To parts - 1
                Dim t = k / CDbl(parts)
                result.Add(New PointF(CSng(a.X + (b.X - a.X) * t), CSng(a.Y + (b.Y - a.Y) * t)))
            Next
        Next
        If Not closed Then result.Add(pts(n - 1))
        Return result
    End Function

    Private Shared Function Rotate(p As PointF, c As PointF, a As Double) As PointF
        Dim cs = Math.Cos(a), sn = Math.Sin(a)
        Dim dx = p.X - c.X, dy = p.Y - c.Y
        Return New PointF(CSng(c.X + dx * cs - dy * sn), CSng(c.Y + dx * sn + dy * cs))
    End Function

    Public Shared Function Dist(a As PointF, b As PointF) As Double
        Dim dx = CDbl(a.X) - b.X, dy = CDbl(a.Y) - b.Y
        Return Math.Sqrt(dx * dx + dy * dy)
    End Function

    '=====================================================================
    ' 立體化
    '=====================================================================

    ''' <summary>
    ''' 立體化（同 CorelDRAW 的立體化工具，平行的）：圖形往 angleDeg（0＝右、90＝下）方向長出 depth（照片高度單位）的側面。
    ''' 回傳側面的鋼筆路徑（放在原本的下面）：填 sideColor，shaded 時由前往後漸暗。不能套時回傳 Nothing。
    ''' </summary>
    Public Shared Function Extrude(layer As DrawLayer, angleDeg As Double, depth As Double, sideColor As Color, shaded As Boolean) As DrawLayer
        If Not CanApply(layer) OrElse depth <= 0 Then Return Nothing
        Dim rings = AreaRings(layer)
        If rings.Count = 0 Then Return Nothing
        Dim a = angleDeg * Math.PI / 180
        Dim v As New PointF(CSng(Math.Cos(a) * depth), CSng(Math.Sin(a) * depth))
        Dim b = AreaBounds(layer)
        Dim swept = RectangleF.FromLTRB(Math.Min(b.Left, b.Left + v.X), Math.Min(b.Top, b.Top + v.Y), Math.Max(b.Right, b.Right + v.X), Math.Max(b.Bottom, b.Bottom + v.Y))
        Dim grid = MakeGrid(swept, 0.003)
        Paint(grid, Sub(g)
                        FillRings(g, rings)
                        FillRings(g, rings.Select(Function(r) r.Select(Function(p) New PointF(p.X + v.X, p.Y + v.Y)).ToArray()))
                        ' 每條邊掃過的平行四邊形
                        For Each r In rings
                            For i = 0 To r.Length - 1
                                Dim p = r(i), q = r((i + 1) Mod r.Length)
                                g.FillPolygon(Brushes.Black, {p, q, New PointF(q.X + v.X, q.Y + v.Y), New PointF(p.X + v.X, p.Y + v.Y)})
                            Next
                        Next
                    End Sub)
        Dim side = TraceGrid(grid, grid.Mask)
        If side.Count = 0 Then Return Nothing
        Dim style = layer.Clone()
        style.Shape = DrawShape.Path
        style.Text = "" : style.TextPathSource = Nothing
        Dim res = PathBoolean.MakeLayer(style, side)
        If res Is Nothing Then Return Nothing
        res.Filled = True
        res.FillColorArgb = sideColor.ToArgb()
        res.FadeGradient = Nothing : res.Shadow = False
        res.Strokes = Nothing : res.Points = Nothing
        res.X = 0 : res.Y = 0 : res.W = 0 : res.H = 0
        res.Id = Nothing : res.GroupId = Nothing
        res.Brush = If(layer.Stroked, layer.Brush, BrushKind.HardRound)
        If DrawGeometry.HasText(layer.Shape) Then res.Stroked = False
        If shaded Then
            ' 由前（原本的中心）往後（中心＋v）漸暗
            Dim fc As New PointF(b.X + b.Width / 2, b.Y + b.Height / 2)
            Dim f1 = DrawGeometry.GradientFraction(res, fc), f2 = DrawGeometry.GradientFraction(res, New PointF(fc.X + v.X, fc.Y + v.Y))
            res.FillColor2Argb = Lerp(sideColor, Color.FromArgb(sideColor.A, 0, 0, 0), 0.55).ToArgb()
            res.FillGradient = New GradientFill With {.Kind = GradientKind.Linear, .X1 = Math.Round(f1.X, 4), .Y1 = Math.Round(f1.Y, 4), .X2 = Math.Round(f2.X, 4), .Y2 = Math.Round(f2.Y, 4)}
        Else
            res.FillGradient = Nothing
        End If
        res.Name = If(String.IsNullOrEmpty(layer.Name), Lang.T("立體"), layer.Name & Lang.T(" 立體"))
        Return res
    End Function

    ''' <summary>側面的預設顏色：原本的填色（或線條色）調暗。</summary>
    Public Shared Function DefaultSideColor(layer As DrawLayer) As Color
        Dim c = Color.FromArgb(If(layer.Filled AndAlso DrawGeometry.IsClosedLayer(layer), layer.FillColorArgb, layer.StrokeColorArgb))
        If DrawGeometry.HasText(layer.Shape) AndAlso Not DrawGeometry.IsCallout(layer.Shape) Then c = Color.FromArgb(layer.TextColorArgb)
        Return Lerp(Color.FromArgb(255, c), Color.Black, 0.35)
    End Function

    '=====================================================================
    ' 簡化路徑
    '=====================================================================

    ''' <summary>節點數（鋼筆路徑；其他圖形先轉成路徑再算）。</summary>
    Public Shared Function NodeCount(layer As DrawLayer) As Integer
        Dim p = If(layer IsNot Nothing AndAlso layer.Shape = DrawShape.Path, layer, EffectPath(layer))
        If p Is Nothing OrElse p.Contours Is Nothing Then Return 0
        Return p.Contours.Sum(Function(c) c.Nodes.Count)
    End Function

    ''' <summary>
    ''' 簡化路徑（同 CorelDRAW 的減少節點、Illustrator 的 Simplify）：誤差 tolerance（照片高度單位）以內重新擬合成貝茲曲線；
    ''' 尖角保留。每條路徑只在節點變少時才換掉。不能轉時回傳 Nothing。
    ''' </summary>
    Public Shared Function SimplifyPath(layer As DrawLayer, tolerance As Double) As DrawLayer
        Dim path = EffectPath(layer)
        If path Is Nothing Then Return Nothing
        tolerance = Math.Max(0.00002, tolerance)
        Dim result As New List(Of PathContour)()
        For Each pc In path.Contours
            Dim pts = DrawGeometry.SampleContour(pc)
            Dim curved = Enumerable.Range(0, pc.SegmentCount).Any(Function(i) Not DrawGeometry.IsStraight(DrawGeometry.Segment(pc, i)))
            Dim nc As PathContour = Nothing
            If pc.Closed AndAlso pts.Length >= 3 Then
                nc = PathBoolean.FitRing(pts, tolerance)
            ElseIf pts.Length >= 2 Then
                Dim s = DrawGeometry.Simplify(pts, tolerance, False)
                nc = If(curved, DrawGeometry.SmoothContour(s, False), New PathContour With {.Nodes = s.Select(Function(p) New PathNode(p.X, p.Y)).ToList()})
            End If
            result.Add(If(nc IsNot Nothing AndAlso nc.Nodes.Count < pc.Nodes.Count, nc, pc.Clone()))
        Next
        Dim r = path.Clone()
        r.Contours = result
        Return r
    End Function
End Class

''' <summary>
''' 向量修飾筆刷的一次拖曳：圖形先轉成密集的折線，筆刷範圍內的點依模式移動（中心最強、邊緣漸弱），
''' 拉長的地方補點、擠在一起的地方減點；放開時再簡化擬合成貝茲曲線。
''' </summary>
Public NotInheritable Class TouchUpSession
    Private ReadOnly _style As DrawLayer
    Private ReadOnly _contours As New List(Of (Pts As List(Of PointF), Closed As Boolean))()
    Private ReadOnly _spacing As Double
    Private ReadOnly _rnd As New Random(7)

    ''' <summary>能不能用修飾筆刷（向量圖形、文字）。</summary>
    Public Shared Function CanTouch(layer As DrawLayer) As Boolean
        Return VectorEffects.CanApply(layer)
    End Function

    ''' <summary>spacing：折線的點距（照片高度單位），通常是筆刷半徑的 1/6。</summary>
    Public Sub New(layer As DrawLayer, spacing As Double)
        _spacing = Math.Max(0.0002, spacing)
        Dim path = VectorEffects.EffectPath(layer)
        _style = If(path, layer.Clone())
        If path Is Nothing Then Return
        For Each c In path.Contours
            Dim pts = DrawGeometry.SampleContour(c)
            If pts.Length < 2 Then Continue For
            _contours.Add((VectorEffects.Resample(pts, c.Closed, _spacing), c.Closed AndAlso pts.Length >= 3))
        Next
    End Sub

    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return _contours.Count = 0
        End Get
    End Property

    ''' <summary>
    ''' 筆刷作用一次：center、radius（照片高度單位），strength 0～1，delta＝這次滑鼠移動（塗抹用），reverse＝反方向（轉動逆時針）。
    ''' 回傳有沒有點被移動。
    ''' </summary>
    Public Function Apply(mode As TouchUpMode, center As PointF, radius As Double, strength As Double, delta As PointF, reverse As Boolean) As Boolean
        Dim moved = False
        radius = Math.Max(0.0005, radius)
        strength = Math.Max(0, Math.Min(1, strength))
        For Each c In _contours
            Dim pts = c.Pts
            Dim src = If(mode = TouchUpMode.Smooth, pts.ToArray(), Nothing)
            For i = 0 To pts.Count - 1
                Dim p = pts(i)
                Dim d = VectorEffects.Dist(p, center)
                If d >= radius Then Continue For
                Dim f = 1 - (d / radius) ^ 2
                Dim w = f * f
                Dim np = p
                Select Case mode
                    Case TouchUpMode.Smear
                        np = New PointF(CSng(p.X + delta.X * w * strength), CSng(p.Y + delta.Y * w * strength))
                    Case TouchUpMode.Attract
                        np = New PointF(CSng(p.X + (center.X - p.X) * w * strength * 0.15), CSng(p.Y + (center.Y - p.Y) * w * strength * 0.15))
                    Case TouchUpMode.Repel
                        If d > 0.0000001 Then
                            Dim k = radius * w * strength * 0.06 / d
                            np = New PointF(CSng(p.X + (p.X - center.X) * k), CSng(p.Y + (p.Y - center.Y) * k))
                        End If
                    Case TouchUpMode.Twirl
                        Dim a = If(reverse, -1, 1) * strength * w * 0.08
                        Dim cs = Math.Cos(a), sn = Math.Sin(a)
                        Dim dx = p.X - center.X, dy = p.Y - center.Y
                        np = New PointF(CSng(center.X + dx * cs - dy * sn), CSng(center.Y + dx * sn + dy * cs))
                    Case TouchUpMode.Roughen
                        Dim ang = _rnd.NextDouble() * 2 * Math.PI, rr = _rnd.NextDouble() * radius * 0.05 * strength * w
                        np = New PointF(CSng(p.X + rr * Math.Cos(ang)), CSng(p.Y + rr * Math.Sin(ang)))
                    Case TouchUpMode.Smooth
                        Dim n = src.Length
                        If Not c.Closed AndAlso (i = 0 OrElse i = n - 1) Then Continue For
                        Dim a = src((i - 1 + n) Mod n), b = src((i + 1) Mod n)
                        Dim mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2
                        np = New PointF(CSng(p.X + (mx - p.X) * w * strength * 0.5), CSng(p.Y + (my - p.Y) * w * strength * 0.5))
                End Select
                If np <> p Then pts(i) = np : moved = True
            Next
            If moved Then Respace(pts, c.Closed)
        Next
        Return moved
    End Function

    ''' <summary>拉長的段補點、太近的點刪掉（開放路徑的兩端不動）。</summary>
    Private Sub Respace(pts As List(Of PointF), closed As Boolean)
        Dim i = 0
        While i < pts.Count
            Dim j = i + 1
            If j >= pts.Count Then
                If Not closed Then Exit While
                j = 0
            End If
            Dim d = VectorEffects.Dist(pts(i), pts(j))
            If d > _spacing * 1.6 AndAlso pts.Count < 20000 Then
                pts.Insert(i + 1, New PointF((pts(i).X + pts(j).X) / 2, (pts(i).Y + pts(j).Y) / 2))
                Continue While
            End If
            If d < _spacing * 0.3 AndAlso pts.Count > If(closed, 3, 2) Then
                Dim remove = If(j = 0, i, j)
                If closed OrElse (remove > 0 AndAlso remove < pts.Count - 1) Then
                    pts.RemoveAt(remove)
                    If remove < i Then i -= 1
                    Continue While
                End If
            End If
            i += 1
        End While
    End Sub

    ''' <summary>目前的結果：拖曳中是密集的折線（畫得快）；final＝放開時，簡化並擬合成貝茲曲線。</summary>
    Public Function Result(final As Boolean) As DrawLayer
        Dim r = _style.Clone()
        r.Shape = DrawShape.Path
        r.Text = "" : r.TextPathSource = Nothing
        r.Points = Nothing : r.Strokes = Nothing
        r.X = 0 : r.Y = 0 : r.W = 0 : r.H = 0 : r.Rotation = 0
        r.Contours = New List(Of PathContour)()
        For Each c In _contours
            If c.Pts.Count < 2 Then Continue For
            Dim pts = c.Pts.ToArray()
            If Not final Then
                r.Contours.Add(New PathContour With {.Nodes = pts.Select(Function(p) New PathNode(p.X, p.Y)).ToList(), .Closed = c.Closed})
                Continue For
            End If
            Dim tol = _spacing * 0.2
            Dim fit = If(c.Closed AndAlso pts.Length >= 3, PathBoolean.FitRing(pts, tol), Nothing)
            If fit Is Nothing Then fit = DrawGeometry.SmoothContour(DrawGeometry.Simplify(pts, tol, c.Closed), c.Closed)
            r.Contours.Add(fit)
        Next
        Return r
    End Function
End Class
