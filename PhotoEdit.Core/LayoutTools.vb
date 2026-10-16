Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>尺寸線的方向（同 CorelDRAW 的平行、水平、垂直尺度工具）。</summary>
Public Enum DimensionKind
    ''' <summary>沿兩點的方向量距離。</summary>
    Aligned = 0
    ''' <summary>只量水平距離。</summary>
    Horizontal = 1
    ''' <summary>只量垂直距離。</summary>
    Vertical = 2
End Enum

''' <summary>圖樣填色的花樣（同 CorelDRAW 的雙色圖樣、Illustrator 的圖樣色票）。</summary>
Public Enum PatternKind
    Stripes = 0
    Dots = 1
    Checker = 2
    Grid = 3
    Crosshatch = 4
    Bricks = 5
    Waves = 6
    Diamonds = 7
    ''' <summary>紋理筆的材質（Material），顏色用填色。</summary>
    Material = 8
End Enum

''' <summary>圖樣填色：花樣、一格的大小（照片高度單位）、角度；顏色＝填色（底）＋第二色（花樣）。</summary>
Public NotInheritable Class PatternFill
    Public Property Kind As PatternKind
    Public Property Scale As Double = 0.04
    Public Property Angle As Double
    Public Property Material As MaterialKind

    Public Function Clone() As PatternFill
        Return DirectCast(MemberwiseClone(), PatternFill)
    End Function
End Class

''' <summary>可變寬度線條的一個控制點：T＝在線上的位置（0 起點～1 終點），W＝粗細倍數（1＝原本的線寬）。</summary>
Public NotInheritable Class WidthStop
    Public Property T As Double
    Public Property W As Double = 1

    Public Function Clone() As WidthStop
        Return DirectCast(MemberwiseClone(), WidthStop)
    End Function
End Class

''' <summary>文件色票（全域色）：圖層的 FillSwatch／StrokeSwatch 指向它，改色票時一起改。</summary>
Public NotInheritable Class ColorSwatch
    Public Property Id As String
    Public Property Name As String = ""
    Public Property Argb As Integer

    Public Function Clone() As ColorSwatch
        Return DirectCast(MemberwiseClone(), ColorSwatch)
    End Function
End Class

''' <summary>參考線：Vertical＝直的（Pos 是 x），否則橫的（Pos 是 y）；照片高度單位。</summary>
Public NotInheritable Class Guide
    Public Property Vertical As Boolean
    Public Property Pos As Double

    Public Function Clone() As Guide
        Return DirectCast(MemberwiseClone(), Guide)
    End Function
End Class

''' <summary>
''' 排版工具的幾何：尺寸線（延伸線、尺寸線、標籤）、連接線（跟著兩端的物件走）、可變寬度、色票同步。
''' </summary>
Partial Public NotInheritable Class DrawGeometry

    '=====================================================================
    ' 尺寸線
    '=====================================================================

    ''' <summary>尺寸線的幾何：兩個量測點 A、B，尺寸線兩端 A2、B2，往外的方向 N（標籤放這邊），量到的長度（照片高度單位）。</summary>
    Public Structure DimensionGeometry
        Public A As PointF, B As PointF, A2 As PointF, B2 As PointF
        Public N As PointF
        Public Length As Double
    End Structure

    Public Shared Function Dimension(layer As DrawLayer) As DimensionGeometry?
        If layer.Points Is Nothing OrElse layer.Points.Count < 2 Then Return Nothing
        Dim a = layer.Points(0).ToPointF(), b = layer.Points(1).ToPointF()
        Dim off = CSng(layer.Param1)
        Dim g As New DimensionGeometry With {.A = a, .B = b}
        Select Case layer.DimKind
            Case DimensionKind.Horizontal
                Dim y = Math.Min(a.Y, b.Y) - off
                g.A2 = New PointF(a.X, y) : g.B2 = New PointF(b.X, y)
                g.N = New PointF(0, If(off >= 0, -1, 1))
                g.Length = Math.Abs(b.X - a.X)
            Case DimensionKind.Vertical
                Dim x = Math.Max(a.X, b.X) + off
                g.A2 = New PointF(x, a.Y) : g.B2 = New PointF(x, b.Y)
                g.N = New PointF(If(off >= 0, 1, -1), 0)
                g.Length = Math.Abs(b.Y - a.Y)
            Case Else
                Dim len = Dist(a, b)
                Dim ux = If(len > 0, (b.X - a.X) / len, 1.0), uy = If(len > 0, (b.Y - a.Y) / len, 0.0)
                Dim nx = CSng(uy), ny = CSng(-ux) ' A→B 往右時，正的位移在上面
                g.A2 = New PointF(a.X + nx * off, a.Y + ny * off) : g.B2 = New PointF(b.X + nx * off, b.Y + ny * off)
                g.N = If(off >= 0, New PointF(nx, ny), New PointF(-nx, -ny))
                g.Length = len
        End Select
        Return g
    End Function

    ''' <summary>尺寸線的外形線：兩條延伸線（離量測點留一點空隙、超出尺寸線一點）＋尺寸線（最後一條，箭頭畫在它的兩端）。</summary>
    Private Shared Function DimensionFigures(layer As DrawLayer) As List(Of Figure)
        Dim list As New List(Of Figure)()
        Dim gq = Dimension(layer)
        If Not gq.HasValue Then Return list
        Dim g = gq.Value
        Dim gap = CSng(Math.Max(0.002, layer.TextSize * 0.15)), over = CSng(Math.Max(0.004, layer.TextSize * 0.3))
        For Each pr In {(g.A, g.A2), (g.B, g.B2)}
            Dim d = Dist(pr.Item1, pr.Item2)
            If d <= gap Then Continue For
            Dim ux = (pr.Item2.X - pr.Item1.X) / CSng(d), uy = (pr.Item2.Y - pr.Item1.Y) / CSng(d)
            list.Add(New Figure With {.Points = {New PointF(pr.Item1.X + ux * gap, pr.Item1.Y + uy * gap), New PointF(pr.Item2.X + ux * over, pr.Item2.Y + uy * over)}})
        Next
        list.Add(New Figure With {.Points = {g.A2, g.B2}})
        Return list
    End Function

    ''' <summary>標籤文字：Text 有內容時用它（{0} 換成數字），否則「數字 單位」。</summary>
    Public Shared Function DimensionLabel(layer As DrawLayer) As String
        Dim gq = Dimension(layer)
        If Not gq.HasValue Then Return ""
        Dim px = If(layer.DimPhotoPx > 0, layer.DimPhotoPx, 1000)
        Dim v = gq.Value.Length * px * If(layer.DimScale > 0, layer.DimScale, 1)
        Dim num = v.ToString("F" & Math.Max(0, Math.Min(4, layer.DimDecimals)), Globalization.CultureInfo.InvariantCulture)
        If Not String.IsNullOrWhiteSpace(layer.Text) Then Return layer.Text.Replace("{0}", num)
        Return If(String.IsNullOrEmpty(layer.DimUnit), num, num & " " & layer.DimUnit)
    End Function

    ''' <summary>標籤（畫的時候用的文字圖層）：放在尺寸線中間的外側，字順著線、保持正的。</summary>
    Public Shared Function DimensionLabelLayer(layer As DrawLayer) As DrawLayer
        Dim gq = Dimension(layer)
        If Not gq.HasValue Then Return Nothing
        Dim g = gq.Value
        Dim text = DimensionLabel(layer)
        Dim size = Math.Max(0.006, layer.TextSize)
        Dim mx = (g.A2.X + g.B2.X) / 2, my = (g.A2.Y + g.B2.Y) / 2
        Dim ang = Math.Atan2(g.B2.Y - g.A2.Y, g.B2.X - g.A2.X) * 180 / Math.PI
        If ang > 90 Then ang -= 180
        If ang <= -90 Then ang += 180
        Dim lift = size * 0.75
        Return New DrawLayer With {.Shape = DrawShape.Text, .Text = text, .X = mx + g.N.X * lift, .Y = my + g.N.Y * lift,
                                   .W = size * (text.Length * 0.75 + 2), .H = size * 1.7, .Rotation = ang, .TextSize = size,
                                   .FontName = layer.FontName, .TextColorArgb = layer.TextColorArgb, .TextBold = layer.TextBold,
                                   .Filled = False, .Stroked = False}
    End Function

    ''' <summary>把尺寸線拖到 world：依方向算新的位移。</summary>
    Public Shared Sub SetDimensionOffset(layer As DrawLayer, world As PointF)
        If layer.Points Is Nothing OrElse layer.Points.Count < 2 Then Return
        Dim a = layer.Points(0).ToPointF(), b = layer.Points(1).ToPointF()
        Select Case layer.DimKind
            Case DimensionKind.Horizontal : layer.Param1 = Math.Round(Math.Min(a.Y, b.Y) - world.Y, 5)
            Case DimensionKind.Vertical : layer.Param1 = Math.Round(world.X - Math.Max(a.X, b.X), 5)
            Case Else
                Dim len = Dist(a, b)
                If len <= 0 Then Return
                Dim nx = (b.Y - a.Y) / len, ny = -(b.X - a.X) / len
                layer.Param1 = Math.Round((world.X - a.X) * nx + (world.Y - a.Y) * ny, 5)
        End Select
    End Sub

    '=====================================================================
    ' 連接線
    '=====================================================================

    ''' <summary>能不能當連接線的端點物件（向量圖形、文字；連接線、點陣、填滿圖層不行）。</summary>
    Public Shared Function CanConnect(d As DrawLayer) As Boolean
        If d Is Nothing OrElse d.Shape = DrawShape.Connector OrElse Not CanMirror(d) Then Return False
        Return Not Bounds(d).IsEmpty
    End Function

    ''' <summary>
    ''' 連接線跟著兩端的物件：ApplyChange 之後呼叫（同一步復原）。端點物件被刪掉時改成自由的端點（留在原處）。
    ''' </summary>
    Public Shared Sub SyncConnectors(r As EditRecipe)
        If r.Drawings Is Nothing OrElse Not r.Drawings.Any(Function(d) d.Shape = DrawShape.Connector) Then Return
        Dim byId = r.Drawings.Where(Function(d) d.Id IsNot Nothing).GroupBy(Function(d) d.Id).ToDictionary(Function(g) g.Key, Function(g) g.First())
        For Each c In r.Drawings
            If c.Shape <> DrawShape.Connector OrElse c.Points Is Nothing OrElse c.Points.Count < 2 Then Continue For
            Dim s As DrawLayer = Nothing, e As DrawLayer = Nothing
            If c.ConnStart IsNot Nothing AndAlso Not byId.TryGetValue(c.ConnStart, s) Then c.ConnStart = Nothing
            If c.ConnEnd IsNot Nothing AndAlso Not byId.TryGetValue(c.ConnEnd, e) Then c.ConnEnd = Nothing
            Dim pts = RouteConnector(c.Points(0).ToPointF(), c.Points(c.Points.Count - 1).ToPointF(), s, e, c.ConnElbow)
            Dim same = pts.Count = c.Points.Count AndAlso pts.Select(Function(p, i) Dist(p, c.Points(i).ToPointF()) < 0.000001).All(Function(x) x)
            If Not same Then c.Points = pts.Select(Function(p) New DrawPoint(p.X, p.Y)).ToList()
        Next
    End Sub

    ''' <summary>
    ''' 連接線的點：直線＝兩個物件中心的連線，切在物件的外框上；直角＝從物件側邊的中點出去，水平、垂直、水平（或垂直、水平、垂直）。
    ''' s、e 是 Nothing 時那一端是自由的點（free0、free1）。
    ''' </summary>
    Public Shared Function RouteConnector(free0 As PointF, free1 As PointF, s As DrawLayer, e As DrawLayer, elbow As Boolean) As List(Of PointF)
        Dim sb = If(s Is Nothing, RectangleF.Empty, Bounds(s)), eb = If(e Is Nothing, RectangleF.Empty, Bounds(e))
        Dim c0 = If(s Is Nothing, free0, New PointF(sb.X + sb.Width / 2, sb.Y + sb.Height / 2))
        Dim c1 = If(e Is Nothing, free1, New PointF(eb.X + eb.Width / 2, eb.Y + eb.Height / 2))
        If Not elbow Then
            Dim p0 = If(s Is Nothing, c0, EdgePoint(s, c0, c1))
            Dim p1 = If(e Is Nothing, c1, EdgePoint(e, c1, c0))
            Return New List(Of PointF) From {p0, p1}
        End If
        Dim horizontal = Math.Abs(c1.X - c0.X) >= Math.Abs(c1.Y - c0.Y)
        Dim side = Function(b As RectangleF, c As PointF, toward As PointF) As PointF
                       If b.IsEmpty Then Return c
                       If horizontal Then Return New PointF(If(toward.X >= c.X, b.Right, b.Left), c.Y)
                       Return New PointF(c.X, If(toward.Y >= c.Y, b.Bottom, b.Top))
                   End Function
        Dim q0 = side(sb, c0, c1), q1 = side(eb, c1, c0)
        Dim pts As New List(Of PointF) From {q0}
        If horizontal Then
            Dim mx = (q0.X + q1.X) / 2
            If Math.Abs(q0.Y - q1.Y) > 0.000001 Then pts.Add(New PointF(mx, q0.Y)) : pts.Add(New PointF(mx, q1.Y))
        Else
            Dim my = (q0.Y + q1.Y) / 2
            If Math.Abs(q0.X - q1.X) > 0.000001 Then pts.Add(New PointF(q0.X, my)) : pts.Add(New PointF(q1.X, my))
        End If
        pts.Add(q1)
        Return pts
    End Function

    ''' <summary>從物件中心 c 往 toward 的射線，離開物件外形的地方（封閉外形；沒有時用外框）。</summary>
    Private Shared Function EdgePoint(d As DrawLayer, c As PointF, toward As PointF) As PointF
        Dim bestT = -1.0
        Dim polys = Figures(d).Where(Function(f) f.Points.Length >= 2).ToList()
        If polys.Count = 0 Then Return c
        For Each f In polys
            Dim n = f.Points.Length
            Dim segs = If(f.Closed, n, n - 1)
            For i = 0 To segs - 1
                Dim t = SegmentHit(c, toward, f.Points(i), f.Points((i + 1) Mod n))
                If t > bestT AndAlso t <= 1 Then bestT = t
            Next
        Next
        If bestT < 0 Then Return c
        Return New PointF(CSng(c.X + (toward.X - c.X) * bestT), CSng(c.Y + (toward.Y - c.Y) * bestT))
    End Function

    ''' <summary>線段 p→q 和 a→b 的交點在 p→q 上的比例（0..1）；沒有交點時 −1。</summary>
    Private Shared Function SegmentHit(p As PointF, q As PointF, a As PointF, b As PointF) As Double
        Dim rx = CDbl(q.X) - p.X, ry = CDbl(q.Y) - p.Y, sx = CDbl(b.X) - a.X, sy = CDbl(b.Y) - a.Y
        Dim den = rx * sy - ry * sx
        If Math.Abs(den) < 1.0E-12 Then Return -1
        Dim t = ((a.X - p.X) * sy - (a.Y - p.Y) * sx) / den
        Dim u = ((a.X - p.X) * ry - (a.Y - p.Y) * rx) / den
        If t < 0 OrElse t > 1 OrElse u < 0 OrElse u > 1 Then Return -1
        Return t
    End Function

    '=====================================================================
    ' 可變寬度
    '=====================================================================

    ''' <summary>粗細倍數在 t（0..1）的值：控制點之間平滑內插（餘弦），兩端之外用最近的控制點。</summary>
    Public Shared Function WidthAt(profile As IList(Of WidthStop), t As Double) As Double
        If profile Is Nothing OrElse profile.Count = 0 Then Return 1
        Dim s = profile.OrderBy(Function(x) x.T).ToList()
        If t <= s(0).T Then Return Math.Max(0, s(0).W)
        If t >= s(s.Count - 1).T Then Return Math.Max(0, s(s.Count - 1).W)
        For i = 0 To s.Count - 2
            If t <= s(i + 1).T Then
                Dim span = Math.Max(0.000001, s(i + 1).T - s(i).T)
                Dim k = (t - s(i).T) / span
                k = (1 - Math.Cos(k * Math.PI)) / 2
                Return Math.Max(0, s(i).W + (s(i + 1).W - s(i).W) * k)
            End If
        Next
        Return 1
    End Function

    ''' <summary>可變寬度的預設樣式（名稱、控制點）。第一個是均勻（沒有控制點）。</summary>
    Public Shared Function WidthPresets() As List(Of (Name As String, Stops As WidthStop()))
        Dim S = Function(t As Double, w As Double) New WidthStop With {.T = t, .W = w}
        Return New List(Of (String, WidthStop())) From {
            (Lang.T("均勻"), New WidthStop() {}),
            (Lang.T("尾端變尖"), {S(0, 1), S(1, 0)}),
            (Lang.T("起點變尖"), {S(0, 0), S(1, 1)}),
            (Lang.T("兩端尖"), {S(0, 0), S(0.5, 1), S(1, 0)}),
            (Lang.T("中間粗"), {S(0, 0.3), S(0.5, 1.8), S(1, 0.3)}),
            (Lang.T("中間細"), {S(0, 1.4), S(0.5, 0.25), S(1, 1.4)}),
            (Lang.T("毛筆"), {S(0, 0.35), S(0.25, 1.3), S(0.7, 0.9), S(1, 0)}),
            (Lang.T("水滴"), {S(0, 0.15), S(0.8, 1.6), S(1, 0.6)})}
    End Function

    ''' <summary>線上離 p 最近的點：(在線上的比例 t, 那個點, 距離)。沒有線時 Nothing。</summary>
    Public Shared Function NearestOnStroke(layer As DrawLayer, p As PointF) As (T As Double, Point As PointF, Distance As Double)?
        Dim best As (T As Double, Point As PointF, Distance As Double)? = Nothing
        For Each f In Figures(layer)
            Dim pts = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
            If pts.Length < 2 Then Continue For
            Dim total = 0.0
            For i = 1 To pts.Length - 1 : total += Dist(pts(i - 1), pts(i)) : Next
            If total <= 0 Then Continue For
            Dim acc = 0.0
            For i = 1 To pts.Length - 1
                Dim a = pts(i - 1), b = pts(i)
                Dim l = Dist(a, b)
                Dim k = 0.0
                If l > 0 Then k = Math.Max(0, Math.Min(1, ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / (l * l)))
                Dim q As New PointF(CSng(a.X + (b.X - a.X) * k), CSng(a.Y + (b.Y - a.Y) * k))
                Dim dd = Dist(p, q)
                If Not best.HasValue OrElse dd < best.Value.Distance Then best = ((acc + l * k) / total, q, dd)
                acc += l
            Next
            Exit For ' 只看第一條線（可變寬度以第一條為準）
        Next
        Return best
    End Function

    ''' <summary>能不能設可變寬度：有線條的向量圖形（特效筆、粒子、貼圖噴槍、點陣不行）。</summary>
    Public Shared Function CanVaryWidth(d As DrawLayer) As Boolean
        If d Is Nothing OrElse Not CanMirror(d) OrElse HasText(d.Shape) Then Return False
        Select Case d.Brush
            Case BrushKind.FX, BrushKind.Particle, BrushKind.StickerHose : Return False
        End Select
        If DrawLayer.SamplesCanvas(d.Brush) Then Return False
        Return (d.Stroked OrElse Not IsClosedLayer(d)) AndAlso Figures(d).Count > 0
    End Function

    '=====================================================================
    ' 色票（全域色）
    '=====================================================================

    ''' <summary>
    ''' 改色票的顏色：用到它、而且顏色還是舊色的圖層（填色、線條色）一起換成新色；
    ''' 自己改過顏色的圖層（已經不是舊色）不動，順便解除連結。
    ''' </summary>
    Public Shared Sub ChangeSwatch(r As EditRecipe, id As String, newArgb As Integer)
        Dim sw = r.Swatches?.FirstOrDefault(Function(x) x.Id = id)
        If sw Is Nothing Then Return
        Dim old = sw.Argb
        sw.Argb = newArgb
        For Each d In If(r.Drawings, New List(Of DrawLayer)())
            If d.FillSwatch = id Then
                If d.FillColorArgb = old Then d.FillColorArgb = newArgb Else d.FillSwatch = Nothing
            End If
            If d.StrokeSwatch = id Then
                If d.StrokeColorArgb = old Then d.StrokeColorArgb = newArgb Else d.StrokeSwatch = Nothing
            End If
        Next
    End Sub

    ''' <summary>刪掉色票：圖層的顏色留著，只解除連結。</summary>
    Public Shared Sub RemoveSwatch(r As EditRecipe, id As String)
        If r.Swatches Is Nothing Then Return
        r.Swatches.RemoveAll(Function(x) x.Id = id)
        If r.Swatches.Count = 0 Then r.Swatches = Nothing
        For Each d In If(r.Drawings, New List(Of DrawLayer)())
            If d.FillSwatch = id Then d.FillSwatch = Nothing
            If d.StrokeSwatch = id Then d.StrokeSwatch = Nothing
        Next
    End Sub

    ''' <summary>用到這個色票的圖層數。</summary>
    Public Shared Function SwatchUsage(r As EditRecipe, id As String) As Integer
        Return If(r.Drawings, New List(Of DrawLayer)()).Where(Function(d) d.FillSwatch = id OrElse d.StrokeSwatch = id).Count()
    End Function
End Class

''' <summary>圖樣填色、可變寬度的上色。</summary>
Partial Public NotInheritable Class DrawingRenderer

    ''' <summary>圖樣填色：每個像素依花樣（反鋸齒）混合填色（底）與第二色（花樣）；材質用紋理筆的材質（顏色＝填色）。</summary>
    Private Shared Sub ShadePattern(cv As Canvas, mask As Single(), layer As DrawLayer, imageH As Integer)
        Dim pf = layer.FillPattern
        If pf.Kind = PatternKind.Material Then
            Dim m = layer.Clone()
            m.Material = pf.Material
            m.StrokeColorArgb = layer.FillColorArgb
            ShadeMaterial(cv, mask, m, imageH)
            Return
        End If
        Dim c1 = Color.FromArgb(layer.FillColorArgb), c2 = Color.FromArgb(layer.FillColor2Argb)
        ' Color 的 R、G、B 是 Byte：先換成整數再相減（Byte 相減會溢位）
        Dim ca = (A:=CInt(c1.A), R:=CInt(c1.R), G:=CInt(c1.G), B:=CInt(c1.B)), cb = (A:=CInt(c2.A), R:=CInt(c2.R), G:=CInt(c2.G), B:=CInt(c2.B))
        Dim b = DrawGeometry.Bounds(layer)
        Dim cell = Math.Max(0.002, pf.Scale) * imageH ' 一格幾個像素
        Dim ox = b.Left * imageH, oy = b.Top * imageH
        Dim ang = pf.Angle * Math.PI / 180
        Dim cs = Math.Cos(ang), sn = Math.Sin(ang)
        Dim fw = 1.0 / cell ' 一個像素在花樣座標裡多大（反鋸齒用）
        For y = 0 To cv.H - 1
            For x = 0 To cv.W - 1
                Dim i = y * cv.W + x
                Dim m = mask(i)
                If m <= 0 Then Continue For
                Dim px = x + cv.OX + 0.5 - ox, py = y + cv.OY + 0.5 - oy
                Dim u = (px * cs + py * sn) / cell, v = (-px * sn + py * cs) / cell
                Dim k = PatternCoverage(pf.Kind, u, v, fw)
                Dim r = (ca.R + (cb.R - ca.R) * k) / 255.0, g = (ca.G + (cb.G - ca.G) * k) / 255.0, bl = (ca.B + (cb.B - ca.B) * k) / 255.0
                Dim a = (ca.A + (cb.A - ca.A) * k) / 255.0 * m ' 圖層不透明度在疊圖層時才套
                If a > 0 Then cv.Over(i, CSng(r), CSng(g), CSng(bl), CSng(a))
            Next
        Next
    End Sub

    ''' <summary>花樣在 (u, v)（以格為單位）的第二色比例 0..1，邊緣依 fw 反鋸齒。</summary>
    Friend Shared Function PatternCoverage(kind As PatternKind, u As Double, v As Double, fw As Double) As Double
        Dim fr = Function(t As Double) t - Math.Floor(t)
        ' 帶狀：離帶子中心 d（格），半寬 hw → 覆蓋率
        Dim band = Function(d As Double, hw As Double) Math.Max(0, Math.Min(1, (hw - d) / fw + 0.5))
        Select Case kind
            Case PatternKind.Stripes
                Return band(Math.Abs(fr(v) - 0.5), 0.25)
            Case PatternKind.Dots
                Dim du = fr(u) - 0.5, dv = fr(v) - 0.5
                Return band(Math.Sqrt(du * du + dv * dv), 0.3)
            Case PatternKind.Checker
                ' 一格一個顏色；靠近格線時往 0.5 漸變（反鋸齒）
                Dim par = ((CLng(Math.Floor(u)) + CLng(Math.Floor(v))) Mod 2 + 2) Mod 2
                Dim hard = If(par = 0, 1.0, 0.0)
                Dim e = Math.Min(Math.Min(fr(u), 1 - fr(u)), Math.Min(fr(v), 1 - fr(v)))
                Dim w = Math.Max(0, Math.Min(1, e / fw * 2))
                Return 0.5 + (hard - 0.5) * w
            Case PatternKind.Grid
                Return Math.Max(band(Math.Abs(fr(u + 0.5) - 0.5), 0.06), band(Math.Abs(fr(v + 0.5) - 0.5), 0.06))
            Case PatternKind.Crosshatch
                Dim a = (u + v) * 0.7071, b2 = (u - v) * 0.7071
                Return Math.Max(band(Math.Abs(fr(a) - 0.5), 0.1), band(Math.Abs(fr(b2) - 0.5), 0.1))
            Case PatternKind.Bricks
                Dim row = Math.Floor(v * 2)
                Dim uu = u + If((CLng(row) Mod 2 + 2) Mod 2 = 0, 0, 0.5)
                Dim mortarV = band(Math.Abs(fr(v * 2 + 0.5) - 0.5) / 2, 0.06)
                Dim mortarU = band(Math.Abs(fr(uu + 0.5) - 0.5), 0.06)
                Return Math.Max(mortarU, mortarV)
            Case PatternKind.Waves
                Return band(Math.Abs(fr(v + 0.18 * Math.Sin(u * 2 * Math.PI)) - 0.5), 0.22)
            Case PatternKind.Diamonds
                Dim d = Math.Abs(fr(u) - 0.5) + Math.Abs(fr(v) - 0.5)
                Return band(d, 0.32)
        End Select
        Return 0
    End Function

    ''' <summary>
    ''' 可變寬度線條的範圍：每一段是兩端寬度不同的梯形，每個點一個圓（轉角、端點圓滑），聯集起來（同方向繞，Winding 填滿）。
    ''' t 是在每條線上的位置（0..1）。
    ''' </summary>
    Private Shared Function VariableWidthMask(figs As List(Of DrawGeometry.Figure), layer As DrawLayer, widthPx As Single, region As Rectangle) As Single()
        Dim polys As New List(Of DrawGeometry.Figure)()
        For Each f In figs
            Dim pts = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
            If pts.Length = 0 Then Continue For
            ' 先加密：粗細只在點上算，直線只有兩端兩個點時中間會整段一樣粗（兩端是 0 就整條不見）
            pts = VectorEffects.Resample(pts, False, Math.Max(1.5, widthPx / 4)).ToArray()
            Dim acc(pts.Length - 1) As Double
            For i = 1 To pts.Length - 1 : acc(i) = acc(i - 1) + DrawGeometry.Dist(pts(i - 1), pts(i)) : Next
            Dim total = Math.Max(0.000001, acc(pts.Length - 1))
            Dim hw = acc.Select(Function(s) CSng(widthPx / 2 * DrawGeometry.WidthAt(layer.WidthProfile, s / total))).ToArray()
            For i = 0 To pts.Length - 1
                If hw(i) > 0.25F Then polys.Add(New DrawGeometry.Figure With {.Points = CirclePoly(pts(i), hw(i)), .Closed = True})
                If i = 0 Then Continue For
                Dim a = pts(i - 1), b = pts(i)
                Dim l = CSng(DrawGeometry.Dist(a, b))
                If l < 0.001F Then Continue For
                Dim nx = -(b.Y - a.Y) / l, ny = (b.X - a.X) / l
                Dim q = {New PointF(a.X + nx * hw(i - 1), a.Y + ny * hw(i - 1)), New PointF(b.X + nx * hw(i), b.Y + ny * hw(i)),
                         New PointF(b.X - nx * hw(i), b.Y - ny * hw(i)), New PointF(a.X - nx * hw(i - 1), a.Y - ny * hw(i - 1))}
                polys.Add(New DrawGeometry.Figure With {.Points = Orient(q), .Closed = True})
            Next
        Next
        If polys.Count = 0 Then Return New Single(region.Width * region.Height - 1) {}
        Return FillMask(polys, region)
    End Function

    ''' <summary>圓（逆時針，和梯形同方向，聯集時不會互相抵消）。</summary>
    Private Shared Function CirclePoly(c As PointF, r As Single) As PointF()
        Dim n = Math.Max(10, Math.Min(48, CInt(r * 1.2F)))
        Dim pts(n - 1) As PointF
        For k = 0 To n - 1
            Dim a = k * 2 * Math.PI / n
            pts(k) = New PointF(CSng(c.X + r * Math.Cos(a)), CSng(c.Y + r * Math.Sin(a)))
        Next
        Return Orient(pts)
    End Function

    ''' <summary>統一成面積為正的方向。</summary>
    Private Shared Function Orient(pts As PointF()) As PointF()
        Return If(PathBoolean.SignedArea(pts) < 0, pts.Reverse().ToArray(), pts)
    End Function
End Class
