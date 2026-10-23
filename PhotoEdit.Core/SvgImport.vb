Imports System.Drawing
Imports System.Globalization
Imports System.Text.RegularExpressions
Imports System.Xml.Linq

''' <summary>
''' 匯入 SVG：把 path、rect、circle、ellipse、line、polyline、polygon 變成可以用節點工具修改的鋼筆路徑圖層。
''' 支援 g 的巢狀與 transform（matrix、translate、scale、rotate、skewX、skewY），
''' 填色／線條色（#rgb、#rrggbb、rgb()、顏色名稱）、線寬、opacity、fill-opacity、fill-rule、style 屬性；
''' 漸層用第一個色標的顏色。文字、圖片、濾鏡、use 不匯入（會記在 Skipped）。
''' 整張 SVG 等比例縮放、置中放進影像的 80% 範圍。
''' </summary>
Public NotInheritable Class SvgImport
    Private Sub New()
    End Sub

    Public Class Result
        Public Layers As New List(Of DrawLayer)()
        ''' <summary>沒匯入的元素名稱與數量（text、image…）。</summary>
        Public Skipped As New Dictionary(Of String, Integer)()
    End Class

    Private Class Style
        Public Fill As Integer? = Color.Black.ToArgb() ' Nothing＝none
        Public Stroke As Integer? = Nothing
        Public StrokeWidth As Double = 1
        Public Opacity As Double = 1
        Public FillOpacity As Double = 1
        Public StrokeOpacity As Double = 1
        Public EvenOdd As Boolean
        Public Function Clone() As Style
            Return DirectCast(MemberwiseClone(), Style)
        End Function
    End Class

    ''' <summary>2D 仿射矩陣 [a c e; b d f]。</summary>
    Private Structure Affine
        Public A, B, C, D, E, F As Double
        Public Shared ReadOnly Identity As New Affine With {.A = 1, .D = 1}
        Public Function Apply(x As Double, y As Double) As PointF
            Return New PointF(CSng(A * x + C * y + E), CSng(B * x + D * y + F))
        End Function
        Public Function Times(o As Affine) As Affine ' this × o（先做 o 再做 this）
            Return New Affine With {.A = A * o.A + C * o.B, .B = B * o.A + D * o.B, .C = A * o.C + C * o.D, .D = B * o.C + D * o.D,
                               .E = A * o.E + C * o.F + E, .F = B * o.E + D * o.F + F}
        End Function
        Public ReadOnly Property Scale As Double
            Get
                Return Math.Sqrt(Math.Abs(A * D - B * C))
            End Get
        End Property
    End Structure

    ''' <summary>讀 SVG 檔，依影像大小（像素）換算成圖層座標（照片高度單位）。</summary>
    Public Shared Function Load(file As String, imageW As Integer, imageH As Integer) As Result
        Return Parse(XDocument.Load(file), imageW, imageH)
    End Function

    Public Shared Function ParseText(svg As String, imageW As Integer, imageH As Integer) As Result
        Return Parse(XDocument.Parse(svg), imageW, imageH)
    End Function

    Private Shared Function Parse(doc As XDocument, imageW As Integer, imageH As Integer) As Result
        Dim root = doc.Root
        Dim res As New Result()
        If root Is Nothing OrElse root.Name.LocalName <> "svg" Then Return res
        ' 漸層：id → 第一個色標的顏色
        Dim grads As New Dictionary(Of String, Integer)()
        For Each g In root.Descendants().Where(Function(e) e.Name.LocalName = "linearGradient" OrElse e.Name.LocalName = "radialGradient")
            Dim id = CStr(g.Attribute("id"))
            Dim stop0 = g.Elements().FirstOrDefault(Function(e) e.Name.LocalName = "stop")
            If id IsNot Nothing AndAlso stop0 IsNot Nothing Then
                Dim c = ParseColor(If(StyleProp(stop0, "stop-color"), "black"), Nothing)
                If c.HasValue Then grads(id) = c.Value
            End If
        Next
        ' 收集外形（SVG 座標）
        Dim shapes As New List(Of (Contours As List(Of PathContour), St As Style))()
        Walk(root, Affine.Identity, New Style(), grads, shapes, res)
        If shapes.Count = 0 Then Return res
        ' 範圍：viewBox，沒有時用所有外形的外框
        Dim vb = ViewBox(root)
        If Not vb.HasValue Then
            Dim pts = shapes.SelectMany(Function(s) s.Contours).SelectMany(Function(c) c.Nodes).Select(Function(n) n.Anchor).ToList()
            vb = RectangleF.FromLTRB(pts.Min(Function(p) p.X), pts.Min(Function(p) p.Y), pts.Max(Function(p) p.X), pts.Max(Function(p) p.Y))
        End If
        Dim box = vb.Value
        Dim bw = Math.Max(0.0001, box.Width), bh = Math.Max(0.0001, box.Height)
        Dim s0 = Math.Min(imageW * 0.8 / bw, imageH * 0.8 / bh) ' 像素／SVG 單位
        Dim ox = (imageW - bw * s0) / 2, oy = (imageH - bh * s0) / 2
        Dim toDoc = Function(p As PointF) New PointF(CSng(((p.X - box.X) * s0 + ox) / imageH), CSng(((p.Y - box.Y) * s0 + oy) / imageH))
        Dim k = 0
        For Each sh In shapes
            For Each c In sh.Contours
                For Each n In c.Nodes
                    Dim a = toDoc(New PointF(n.X, n.Y)), i = toDoc(New PointF(n.InX, n.InY)), o = toDoc(New PointF(n.OutX, n.OutY))
                    n.X = a.X : n.Y = a.Y : n.InX = i.X : n.InY = i.Y : n.OutX = o.X : n.OutY = o.Y
                Next
            Next
            k += 1
            Dim st = sh.St
            Dim layer As New DrawLayer With {.Shape = DrawShape.Path, .Contours = sh.Contours, .Seed = k, .Name = "SVG " & k,
                                             .Filled = st.Fill.HasValue, .Stroked = st.Stroke.HasValue AndAlso st.StrokeWidth > 0,
                                             .FillColorArgb = If(st.Fill, Color.Black.ToArgb()), .StrokeColorArgb = If(st.Stroke, Color.Black.ToArgb()),
                                             .StrokeWidth = Math.Max(0.0005, st.StrokeWidth * s0 / imageH), .EvenOdd = st.EvenOdd,
                                             .Opacity = CInt(Math.Round(Math.Max(0, Math.Min(1, st.Opacity * If(st.Fill.HasValue, st.FillOpacity, st.StrokeOpacity))) * 100))}
            res.Layers.Add(layer)
        Next
        Return res
    End Function

    Private Shared Sub Walk(el As XElement, m0 As Affine, st0 As Style, grads As Dictionary(Of String, Integer),
                            shapes As List(Of (Contours As List(Of PathContour), St As Style)), res As Result)
        For Each e In el.Elements()
            Dim name = e.Name.LocalName
            Select Case name
                Case "defs", "linearGradient", "radialGradient", "clipPath", "mask", "pattern", "symbol", "title", "desc", "metadata", "style", "marker"
                    Continue For
            End Select
            If StyleProp(e, "display") = "none" Then Continue For
            Dim m = m0.Times(ParseTransform(CStr(e.Attribute("transform"))))
            Dim st = ApplyStyle(e, st0, grads)
            Dim cs As List(Of PathContour) = Nothing
            Select Case name
                Case "g", "a", "switch" : Walk(e, m, st, grads, shapes, res) : Continue For
                Case "path" : cs = PathData(CStr(e.Attribute("d")))
                Case "rect" : cs = RectPath(Num(e, "x"), Num(e, "y"), Num(e, "width"), Num(e, "height"), Num(e, "rx", -1), Num(e, "ry", -1))
                Case "circle" : Dim r = Num(e, "r") : cs = EllipsePath(Num(e, "cx"), Num(e, "cy"), r, r)
                Case "ellipse" : cs = EllipsePath(Num(e, "cx"), Num(e, "cy"), Num(e, "rx"), Num(e, "ry"))
                Case "line" : cs = Poly({Num(e, "x1"), Num(e, "y1"), Num(e, "x2"), Num(e, "y2")}, False)
                Case "polyline" : cs = Poly(Numbers(CStr(e.Attribute("points"))), False)
                Case "polygon" : cs = Poly(Numbers(CStr(e.Attribute("points"))), True)
                Case Else
                    res.Skipped(name) = If(res.Skipped.ContainsKey(name), res.Skipped(name), 0) + 1
                    Continue For
            End Select
            If cs Is Nothing Then Continue For
            cs = cs.Where(Function(c) c.Nodes.Count >= 2).ToList()
            If cs.Count = 0 Then Continue For
            If Not st.Fill.HasValue AndAlso Not st.Stroke.HasValue Then Continue For
            For Each c In cs
                For Each n In c.Nodes
                    Dim a = m.Apply(n.X, n.Y), i = m.Apply(n.InX, n.InY), o = m.Apply(n.OutX, n.OutY)
                    n.X = a.X : n.Y = a.Y : n.InX = i.X : n.InY = i.Y : n.OutX = o.X : n.OutY = o.Y
                Next
            Next
            Dim st2 = st.Clone()
            st2.StrokeWidth *= m.Scale
            ' 開放的線不填色（SVG 會把開放路徑當成封閉來填，這裡維持線條就好）
            If cs.All(Function(c) Not c.Closed) Then st2.Fill = Nothing
            If Not st2.Fill.HasValue AndAlso Not st2.Stroke.HasValue Then st2.Stroke = Color.Black.ToArgb()
            shapes.Add((cs, st2))
        Next
    End Sub

    '---------------------------------------------------------------- 樣式

    Private Shared Function StyleProp(e As XElement, name As String) As String
        Dim styleAttr = CStr(e.Attribute("style"))
        If styleAttr IsNot Nothing Then
            For Each part In styleAttr.Split(";"c)
                Dim kv = part.Split(":"c)
                If kv.Length = 2 AndAlso kv(0).Trim() = name Then Return kv(1).Trim()
            Next
        End If
        Dim a = e.Attribute(name)
        Return If(a Is Nothing, Nothing, a.Value.Trim())
    End Function

    Private Shared Function ApplyStyle(e As XElement, st0 As Style, grads As Dictionary(Of String, Integer)) As Style
        Dim st = st0.Clone()
        Dim v = StyleProp(e, "fill") : If v IsNot Nothing Then st.Fill = ParseColor(v, grads)
        v = StyleProp(e, "stroke") : If v IsNot Nothing Then st.Stroke = ParseColor(v, grads)
        v = StyleProp(e, "stroke-width") : If v IsNot Nothing Then st.StrokeWidth = Length(v)
        v = StyleProp(e, "opacity") : If v IsNot Nothing Then st.Opacity *= Length(v)
        v = StyleProp(e, "fill-opacity") : If v IsNot Nothing Then st.FillOpacity = Length(v)
        v = StyleProp(e, "stroke-opacity") : If v IsNot Nothing Then st.StrokeOpacity = Length(v)
        v = StyleProp(e, "fill-rule") : If v IsNot Nothing Then st.EvenOdd = v = "evenodd"
        Return st
    End Function

    Private Shared Function ParseColor(v As String, grads As Dictionary(Of String, Integer)) As Integer?
        v = v.Trim()
        If v = "none" OrElse v = "transparent" Then Return Nothing
        If v = "currentColor" Then Return Color.Black.ToArgb()
        If v.StartsWith("url(") Then
            Dim id = Regex.Match(v, "#([^)\s]+)").Groups(1).Value
            If grads IsNot Nothing AndAlso grads.ContainsKey(id) Then Return grads(id)
            Return Color.Gray.ToArgb()
        End If
        If v.StartsWith("#") Then
            Dim h = v.Substring(1)
            If h.Length = 3 Then h = String.Concat(h.Select(Function(ch) ch & ch))
            If h.Length >= 6 Then
                Dim n As Integer
                If Integer.TryParse(h.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, n) Then Return Color.FromArgb(255, Color.FromArgb(n)).ToArgb()
            End If
            Return Color.Black.ToArgb()
        End If
        Dim rm = Regex.Match(v, "rgba?\(\s*([\d.]+)(%?)\s*,?\s*([\d.]+)(%?)\s*,?\s*([\d.]+)(%?)")
        If rm.Success Then
            Dim ch = Function(i As Integer) CInt(Math.Max(0, Math.Min(255, Double.Parse(rm.Groups(i).Value, CultureInfo.InvariantCulture) * If(rm.Groups(i + 1).Value = "%", 2.55, 1))))
            Return Color.FromArgb(ch(1), ch(3), ch(5)).ToArgb()
        End If
        Dim named = Color.FromName(v)
        If named.IsKnownColor OrElse named.A > 0 Then Return Color.FromArgb(255, named).ToArgb()
        Return Color.Black.ToArgb()
    End Function

    Private Shared Function Length(v As String) As Double
        Dim m = Regex.Match(v, "[-+]?[\d.]+(?:[eE][-+]?\d+)?")
        If Not m.Success Then Return 0
        Dim d = Double.Parse(m.Value, CultureInfo.InvariantCulture)
        If v.TrimEnd().EndsWith("%") Then d /= 100
        Return d
    End Function

    Private Shared Function Num(e As XElement, name As String, Optional def As Double = 0) As Double
        Dim a = e.Attribute(name)
        Return If(a Is Nothing, def, Length(a.Value))
    End Function

    Private Shared Function Numbers(s As String) As Double()
        If s Is Nothing Then Return {}
        Return Regex.Matches(s, "[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?").Cast(Of Match)().Select(Function(x) Double.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray()
    End Function

    Private Shared Function ViewBox(root As XElement) As RectangleF?
        Dim v = Numbers(CStr(root.Attribute("viewBox")))
        If v.Length = 4 AndAlso v(2) > 0 AndAlso v(3) > 0 Then Return New RectangleF(CSng(v(0)), CSng(v(1)), CSng(v(2)), CSng(v(3)))
        Dim w = Num(root, "width"), h = Num(root, "height")
        If w > 0 AndAlso h > 0 Then Return New RectangleF(0, 0, CSng(w), CSng(h))
        Return Nothing
    End Function

    Private Shared Function ParseTransform(s As String) As Affine
        Dim m = Affine.Identity
        If String.IsNullOrWhiteSpace(s) Then Return m
        For Each t As Match In Regex.Matches(s, "(\w+)\s*\(([^)]*)\)")
            Dim a = Numbers(t.Groups(2).Value)
            Dim n As Affine = Affine.Identity
            Select Case t.Groups(1).Value
                Case "matrix" : If a.Length = 6 Then n = New Affine With {.A = a(0), .B = a(1), .C = a(2), .D = a(3), .E = a(4), .F = a(5)}
                Case "translate" : n = New Affine With {.A = 1, .D = 1, .E = If(a.Length > 0, a(0), 0), .F = If(a.Length > 1, a(1), 0)}
                Case "scale" : Dim sx = If(a.Length > 0, a(0), 1) : n = New Affine With {.A = sx, .D = If(a.Length > 1, a(1), sx)}
                Case "rotate"
                    Dim r = If(a.Length > 0, a(0), 0) * Math.PI / 180
                    n = New Affine With {.A = Math.Cos(r), .B = Math.Sin(r), .C = -Math.Sin(r), .D = Math.Cos(r)}
                    If a.Length >= 3 Then
                        n = New Affine With {.A = 1, .D = 1, .E = a(1), .F = a(2)}.Times(n).Times(New Affine With {.A = 1, .D = 1, .E = -a(1), .F = -a(2)})
                    End If
                Case "skewX" : n = New Affine With {.A = 1, .D = 1, .C = Math.Tan(If(a.Length > 0, a(0), 0) * Math.PI / 180)}
                Case "skewY" : n = New Affine With {.A = 1, .D = 1, .B = Math.Tan(If(a.Length > 0, a(0), 0) * Math.PI / 180)}
            End Select
            m = m.Times(n)
        Next
        Return m
    End Function

    '---------------------------------------------------------------- 外形

    Private Shared Function Node(x As Double, y As Double) As PathNode
        Return New PathNode(CSng(x), CSng(y))
    End Function

    Private Shared Function Poly(v As Double(), closed As Boolean) As List(Of PathContour)
        Dim c As New PathContour With {.Closed = closed}
        For i = 0 To v.Length - 2 Step 2
            c.Nodes.Add(Node(v(i), v(i + 1)))
        Next
        Return New List(Of PathContour) From {c}
    End Function

    Private Shared Function EllipsePath(cx As Double, cy As Double, rx As Double, ry As Double) As List(Of PathContour)
        If rx <= 0 OrElse ry <= 0 Then Return Nothing
        Const K = 0.5523
        Dim c As New PathContour With {.Closed = True}
        For Each q In {(0.0, -ry, K * rx, 0.0), (rx, 0.0, 0.0, K * ry), (0.0, ry, -K * rx, 0.0), (-rx, 0.0, 0.0, -K * ry)}
            Dim n = Node(cx + q.Item1, cy + q.Item2)
            n.Kind = PathNodeKind.Symmetric
            n.OutX = CSng(cx + q.Item1 + q.Item3) : n.OutY = CSng(cy + q.Item2 + q.Item4)
            n.InX = CSng(cx + q.Item1 - q.Item3) : n.InY = CSng(cy + q.Item2 - q.Item4)
            c.Nodes.Add(n)
        Next
        Return New List(Of PathContour) From {c}
    End Function

    Private Shared Function RectPath(x As Double, y As Double, w As Double, h As Double, rx As Double, ry As Double) As List(Of PathContour)
        If w <= 0 OrElse h <= 0 Then Return Nothing
        If rx < 0 AndAlso ry < 0 Then rx = 0 : ry = 0
        If rx < 0 Then rx = ry
        If ry < 0 Then ry = rx
        rx = Math.Min(rx, w / 2) : ry = Math.Min(ry, h / 2)
        If rx <= 0 OrElse ry <= 0 Then Return Poly({x, y, x + w, y, x + w, y + h, x, y + h}, True)
        ' 圓角矩形：用路徑字串組（四段直線＋四段四分之一圓弧）
        Dim d = String.Format(CultureInfo.InvariantCulture, "M{0},{1} H{2} A{3},{4} 0 0 1 {5},{6} V{7} A{3},{4} 0 0 1 {8},{9} H{10} A{3},{4} 0 0 1 {11},{12} V{13} A{3},{4} 0 0 1 {0},{1} Z",
                              x + rx, y, x + w - rx, rx, ry, x + w, y + ry, y + h - ry, x + w - rx, y + h, x + rx, x, y + h - ry, y + ry)
        Return PathData(d)
    End Function

    ''' <summary>解析 path 的 d：M L H V C S Q T A Z（大小寫）。</summary>
    Public Shared Function PathData(d As String) As List(Of PathContour)
        Dim result As New List(Of PathContour)()
        If String.IsNullOrWhiteSpace(d) Then Return result
        Dim tokens = Regex.Matches(d, "[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?").Cast(Of Match)().Select(Function(t) t.Value).ToList()
        Dim pos = 0
        Dim cur As PathContour = Nothing
        Dim cx = 0.0, cy = 0.0, sx = 0.0, sy = 0.0
        Dim lastCtrlX = 0.0, lastCtrlY = 0.0, lastCmd = " "c
        Dim cmd = " "c
        Dim isNum = Function(i As Integer) i < tokens.Count AndAlso Not Char.IsLetter(tokens(i)(0))
        Dim nextNum = Function() As Double
                          Dim v = Double.Parse(tokens(pos), CultureInfo.InvariantCulture)
                          pos += 1
                          Return v
                      End Function
        Dim lineTo = Sub(x As Double, y As Double)
                         If cur Is Nothing Then cur = New PathContour() : result.Add(cur) : cur.Nodes.Add(Node(cx, cy))
                         cur.Nodes.Add(Node(x, y))
                         cx = x : cy = y
                     End Sub
        Dim cubicTo = Sub(x1 As Double, y1 As Double, x2 As Double, y2 As Double, x As Double, y As Double)
                          If cur Is Nothing Then cur = New PathContour() : result.Add(cur) : cur.Nodes.Add(Node(cx, cy))
                          Dim last = cur.Nodes(cur.Nodes.Count - 1)
                          last.OutX = CSng(x1) : last.OutY = CSng(y1)
                          Dim n = Node(x, y)
                          n.InX = CSng(x2) : n.InY = CSng(y2)
                          cur.Nodes.Add(n)
                          lastCtrlX = x2 : lastCtrlY = y2
                          cx = x : cy = y
                      End Sub
        Do While pos < tokens.Count
            If Char.IsLetter(tokens(pos)(0)) Then cmd = tokens(pos)(0) : pos += 1
            Dim rel = Char.IsLower(cmd)
            Dim bx = If(rel, cx, 0.0), by = If(rel, cy, 0.0)
            Select Case Char.ToUpperInvariant(cmd)
                Case "M"c
                    If Not isNum(pos) Then Exit Do
                    Dim x = nextNum() + bx, y = nextNum() + by
                    cur = New PathContour()
                    result.Add(cur)
                    cur.Nodes.Add(Node(x, y))
                    cx = x : cy = y : sx = x : sy = y
                    cmd = If(rel, "l"c, "L"c) ' 後面的座標是 lineto
                Case "L"c
                    If Not isNum(pos) Then Exit Do
                    Dim x = nextNum() + bx, y = nextNum() + by
                    lineTo(x, y)
                Case "H"c
                    If Not isNum(pos) Then Exit Do
                    lineTo(nextNum() + bx, cy)
                Case "V"c
                    If Not isNum(pos) Then Exit Do
                    lineTo(cx, nextNum() + by)
                Case "C"c
                    If Not isNum(pos) Then Exit Do
                    Dim x1 = nextNum() + bx, y1 = nextNum() + by, x2 = nextNum() + bx, y2 = nextNum() + by, x = nextNum() + bx, y = nextNum() + by
                    cubicTo(x1, y1, x2, y2, x, y)
                Case "S"c
                    If Not isNum(pos) Then Exit Do
                    Dim x1 = If("CcSs".Contains(lastCmd), 2 * cx - lastCtrlX, cx), y1 = If("CcSs".Contains(lastCmd), 2 * cy - lastCtrlY, cy)
                    Dim x2 = nextNum() + bx, y2 = nextNum() + by, x = nextNum() + bx, y = nextNum() + by
                    cubicTo(x1, y1, x2, y2, x, y)
                Case "Q"c
                    If Not isNum(pos) Then Exit Do
                    Dim qx = nextNum() + bx, qy = nextNum() + by, x = nextNum() + bx, y = nextNum() + by
                    Dim x0 = cx, y0 = cy
                    cubicTo(x0 + 2 / 3.0 * (qx - x0), y0 + 2 / 3.0 * (qy - y0), x + 2 / 3.0 * (qx - x), y + 2 / 3.0 * (qy - y), x, y)
                    lastCtrlX = qx : lastCtrlY = qy
                Case "T"c
                    If Not isNum(pos) Then Exit Do
                    Dim qx = If("QqTt".Contains(lastCmd), 2 * cx - lastCtrlX, cx), qy = If("QqTt".Contains(lastCmd), 2 * cy - lastCtrlY, cy)
                    Dim x = nextNum() + bx, y = nextNum() + by
                    Dim x0 = cx, y0 = cy
                    cubicTo(x0 + 2 / 3.0 * (qx - x0), y0 + 2 / 3.0 * (qy - y0), x + 2 / 3.0 * (qx - x), y + 2 / 3.0 * (qy - y), x, y)
                    lastCtrlX = qx : lastCtrlY = qy
                Case "A"c
                    If Not isNum(pos) Then Exit Do
                    Dim rx = nextNum(), ry = nextNum(), rot = nextNum(), large = nextNum() <> 0, sweep = nextNum() <> 0
                    Dim x = nextNum() + bx, y = nextNum() + by
                    For Each seg In ArcToCubics(cx, cy, rx, ry, rot, large, sweep, x, y)
                        cubicTo(seg(0), seg(1), seg(2), seg(3), seg(4), seg(5))
                    Next
                    If Math.Abs(rx) < 0.0000001 OrElse Math.Abs(ry) < 0.0000001 Then lineTo(x, y)
                Case "Z"c
                    If cur IsNot Nothing Then
                        ' 最後一點和起點重合時合併（把手留給起點）
                        If cur.Nodes.Count > 1 Then
                            Dim lastN = cur.Nodes(cur.Nodes.Count - 1), firstN = cur.Nodes(0)
                            If Math.Abs(lastN.X - firstN.X) < 0.000001 AndAlso Math.Abs(lastN.Y - firstN.Y) < 0.000001 Then
                                firstN.InX = lastN.InX : firstN.InY = lastN.InY
                                cur.Nodes.RemoveAt(cur.Nodes.Count - 1)
                            End If
                        End If
                        cur.Closed = True
                    End If
                    cx = sx : cy = sy
                    cur = Nothing
                Case Else
                    pos += 1
            End Select
            lastCmd = cmd
        Loop
        Return result
    End Function

    ''' <summary>SVG 圓弧換成幾段三次貝茲（每段不超過 90°）；回傳每段 {x1,y1,x2,y2,x,y}。</summary>
    Private Shared Function ArcToCubics(x0 As Double, y0 As Double, rx As Double, ry As Double, rotDeg As Double, large As Boolean, sweep As Boolean,
                                        x As Double, y As Double) As List(Of Double())
        Dim list As New List(Of Double())()
        rx = Math.Abs(rx) : ry = Math.Abs(ry)
        If rx < 0.0000001 OrElse ry < 0.0000001 OrElse (x0 = x AndAlso y0 = y) Then Return list
        Dim phi = rotDeg * Math.PI / 180, cp = Math.Cos(phi), sp = Math.Sin(phi)
        Dim dx = (x0 - x) / 2, dy = (y0 - y) / 2
        Dim x1p = cp * dx + sp * dy, y1p = -sp * dx + cp * dy
        Dim lam = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry)
        If lam > 1 Then rx *= Math.Sqrt(lam) : ry *= Math.Sqrt(lam)
        Dim num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
        Dim den = rx * rx * y1p * y1p + ry * ry * x1p * x1p
        Dim coef = If(den = 0, 0, Math.Sqrt(Math.Max(0, num / den))) * If(large = sweep, -1, 1)
        Dim cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx
        Dim ccx = cp * cxp - sp * cyp + (x0 + x) / 2, ccy = sp * cxp + cp * cyp + (y0 + y) / 2
        Dim ang = Function(ux As Double, uy As Double, vx As Double, vy As Double) As Double
                      Dim a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy)
                      Return a
                  End Function
        Dim th1 = ang(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry)
        Dim dth = ang((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry)
        If Not sweep AndAlso dth > 0 Then dth -= 2 * Math.PI
        If sweep AndAlso dth < 0 Then dth += 2 * Math.PI
        Dim n = CInt(Math.Ceiling(Math.Abs(dth) / (Math.PI / 2) - 0.000001))
        n = Math.Max(1, n)
        Dim d = dth / n
        Dim t = 4 / 3.0 * Math.Tan(d / 4)
        Dim pt = Function(a As Double) As (X As Double, Y As Double)
                     Dim ex = rx * Math.Cos(a), ey = ry * Math.Sin(a)
                     Return (cp * ex - sp * ey + ccx, sp * ex + cp * ey + ccy)
                 End Function
        Dim der = Function(a As Double) As (X As Double, Y As Double)
                      Dim ex = -rx * Math.Sin(a), ey = ry * Math.Cos(a)
                      Return (cp * ex - sp * ey, sp * ex + cp * ey)
                  End Function
        For i = 0 To n - 1
            Dim a1 = th1 + i * d, a2 = a1 + d
            Dim p1 = pt(a1), p2 = pt(a2), d1 = der(a1), d2 = der(a2)
            list.Add({p1.X + t * d1.X, p1.Y + t * d1.Y, p2.X - t * d2.X, p2.Y - t * d2.Y, p2.X, p2.Y})
        Next
        Return list
    End Function
End Class
