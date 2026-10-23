Imports System.Drawing
Imports System.Globalization
Imports System.Text

''' <summary>
''' 匯出 SVG：繪圖圖層的向量圖形變成 SVG 的 path（填色、漸層、線條、虛線、端點、轉角、箭頭、不透明度），
''' 鋼筆路徑保留貝茲曲線；文字、圖說的字轉成外框；PowerClip 用 clipPath；點陣圖層、填滿圖層嵌成 PNG 圖片。
''' 照片（含文字效果與貼圖）可以嵌在最下面（照片放進形狀時一樣裁切）。
''' 水彩、炭筆這類筆刷的質感、陰影、混合模式沒辦法變成 SVG，會變成一般的實心線條。
''' 座標單位是照片像素。
''' </summary>
Public NotInheritable Class SvgExport
    Private Sub New()
    End Sub

    Private Shared ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture

    Private Shared Function N(v As Double) As String
        Return Math.Round(v, 2).ToString("0.##", Inv)
    End Function

    Private Shared Function Hex(argb As Integer) As String
        Dim c = Color.FromArgb(argb)
        Return $"#{c.R:X2}{c.G:X2}{c.B:X2}"
    End Function

    ''' <summary>
    ''' 產生 SVG 文字。background：照片（不含繪圖圖層）的點陣圖，Nothing 時不放照片；w、h：照片像素大小。
    ''' </summary>
    Public Shared Function Export(recipe As EditRecipe, background As Bitmap, w As Integer, h As Integer) As String
        Dim sb As New StringBuilder()
        Dim defs As New StringBuilder()
        Dim body As New StringBuilder()
        Dim nextId = 0
        Dim newId = Function(prefix As String)
                        nextId += 1
                        Return prefix & nextId
                    End Function
        ' 照片
        If background IsNot Nothing Then
            Dim clipAttr = ""
            If recipe.PhotoClip IsNot Nothing AndAlso recipe.Drawings IsNot Nothing Then
                Dim pcId = recipe.PhotoClip
                Dim frame = recipe.Drawings.FirstOrDefault(Function(x) x.Id = pcId)
                If frame IsNot Nothing Then
                    Dim cid = newId("photoclip")
                    defs.Append($"<clipPath id=""{cid}""><path d=""{RegionPath(frame, h)}""/></clipPath>")
                    clipAttr = $" clip-path=""url(#{cid})"""
                End If
            End If
            body.Append($"<image x=""0"" y=""0"" width=""{w}"" height=""{h}""{clipAttr} href=""data:image/png;base64,{Png(background)}""/>")
        End If
        ' 繪圖圖層（依堆疊順序）
        Dim layers = LayerStack.Order(recipe).Where(Function(r) Not r.IsOverlay AndAlso r.Visible).Select(Function(r) r.Drawing).ToList()
        Dim clipIds As New Dictionary(Of DrawLayer, String)()
        Dim lastOfFrame As New Dictionary(Of DrawLayer, Integer)()
        For k = 0 To layers.Count - 1
            Dim c = ClipRender.ClipOf(recipe, layers(k))
            If c.HasValue Then lastOfFrame(c.Value.Frame) = k
            If ClipRender.IsFrame(recipe, layers(k)) AndAlso Not lastOfFrame.ContainsKey(layers(k)) Then lastOfFrame(layers(k)) = k
        Next
        For k = 0 To layers.Count - 1
            Dim d = layers(k)
            Dim clip = ClipRender.ClipOf(recipe, d)
            Dim element As String
            If ClipRender.IsFrame(recipe, d) Then
                element = LayerElement(ClipRender.FramePart(d, fillOnly:=True), w, h, defs, newId)
            Else
                element = LayerElement(d, w, h, defs, newId)
            End If
            If clip.HasValue Then
                Dim cid As String = Nothing
                If Not clipIds.TryGetValue(clip.Value.Frame, cid) Then
                    cid = newId("clip")
                    clipIds(clip.Value.Frame) = cid
                    defs.Append($"<clipPath id=""{cid}""><path d=""{RegionPath(clip.Value.Frame, h)}""/></clipPath>")
                End If
                element = $"<g clip-path=""url(#{cid})"">{element}</g>"
            End If
            body.Append(element)
            For Each kv In lastOfFrame
                If kv.Value = k AndAlso kv.Key.Visible AndAlso kv.Key.Stroked Then body.Append(LayerElement(ClipRender.FramePart(kv.Key, fillOnly:=False), w, h, defs, newId))
            Next
        Next
        sb.Append("<?xml version=""1.0"" encoding=""UTF-8""?>")
        sb.Append($"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{w}"" height=""{h}"" viewBox=""0 0 {w} {h}"">")
        If defs.Length > 0 Then sb.Append("<defs>").Append(defs).Append("</defs>")
        sb.Append(body)
        sb.Append("</svg>")
        Return sb.ToString()
    End Function

    ''' <summary>一個圖層的 SVG（向量或嵌入的圖片）。</summary>
    Private Shared Function LayerElement(d As DrawLayer, w As Integer, h As Integer, defs As StringBuilder, newId As Func(Of String, String)) As String
        Select Case d.Shape
            Case DrawShape.Raster, DrawShape.FillLayer, DrawShape.Bucket, DrawShape.Gradient
                Return RasterElement(d, w, h)
        End Select
        Dim sb As New StringBuilder()
        Dim opacity = Math.Max(0, Math.Min(100, d.Opacity)) / 100.0
        sb.Append($"<g{If(opacity < 1, $" opacity=""{N(opacity)}""", "")}>")
        Dim scale = CSng(h)
        Dim figs = DrawGeometry.Figures(d).Select(Function(f) New DrawGeometry.Figure With {.Points = f.Points.Select(Function(p) New PointF(p.X * scale, p.Y * scale)).ToArray(), .Closed = f.Closed}).
                   Where(Function(f) f.Points.Length > 0).ToList()
        Dim closed = figs.Where(Function(f) f.Closed AndAlso f.Points.Length >= 3).ToList()
        ' 填色
        If d.Filled AndAlso closed.Count > 0 Then
            Dim fill = ColorAttr("fill", If(d.FillMesh IsNot Nothing, d.FillMesh.AverageColor(), d.FillColorArgb)) ' SVG 沒有網格填色：用平均色
            If d.FillGradient IsNot Nothing AndAlso d.FillMesh Is Nothing Then
                Dim gid = newId("grad")
                defs.Append(GradientDef(gid, DrawingRenderer.AbsoluteGradient(d), d.FillColorArgb, d.FillColor2Argb, h))
                fill = $"fill=""url(#{gid})"""
            End If
            Dim pathD = If(d.Shape = DrawShape.Path AndAlso d.Contours IsNot Nothing, ContourPath(d.Contours.Where(Function(c) c.Closed), scale), FigurePath(closed))
            sb.Append($"<path d=""{pathD}"" {fill}{If(d.EvenOdd, " fill-rule=""evenodd""", "")} stroke=""none""/>")
        End If
        ' 線條
        Dim stroked = d.Stroked OrElse Not DrawGeometry.IsClosedLayer(d)
        If stroked AndAlso Not (DrawGeometry.HasText(d.Shape) AndAlso Not d.Stroked) Then
            Dim widthPx = CSng(Math.Max(0.6, d.StrokeWidth * scale))
            Dim solid = d.Clone() : solid.Dash = LineDash.Solid
            Dim styled = DrawingRenderer.StyleStroke(figs, solid, widthPx)
            Dim hasArrows = d.StartArrow <> ArrowKind.None OrElse d.EndArrow <> ArrowKind.None
            Dim pathD = If(d.Shape = DrawShape.Path AndAlso d.Contours IsNot Nothing AndAlso Not hasArrows, ContourPath(d.Contours, scale), FigurePath(styled.Lines))
            Dim cap = If(d.Cap = LineCapKind.Flat, "butt", If(d.Cap = LineCapKind.Square, "square", "round"))
            Dim join = If(d.Join = LineJoinKind.Miter, "miter", If(d.Join = LineJoinKind.Bevel, "bevel", "round"))
            Dim dash = ""
            Dim pattern = DrawingRenderer.DashPattern(d.Dash)
            If pattern IsNot Nothing Then
                Dim capExtra = If(d.Cap = LineCapKind.Flat, 0.0F, 1.0F)
                dash = " stroke-dasharray=""" & String.Join(" ", pattern.Select(Function(v, i) N(If(i Mod 2 = 0, Math.Max(0.01F, v - capExtra), v + capExtra) * widthPx))) & """"
            End If
            sb.Append($"<path d=""{pathD}"" fill=""none"" {ColorAttr("stroke", d.StrokeColorArgb)} stroke-width=""{N(widthPx)}"" stroke-linecap=""{cap}"" stroke-linejoin=""{join}""{dash}/>")
            For Each head In styled.Heads
                sb.Append($"<path d=""{FigurePath({New DrawGeometry.Figure With {.Points = head, .Closed = True}})}"" {ColorAttr("fill", d.StrokeColorArgb)}/>")
            Next
        End If
        ' 文字：轉成外框
        If DrawGeometry.HasText(d.Shape) AndAlso Not String.IsNullOrWhiteSpace(d.Text) Then
            Dim outs = DrawGeometry.TextOutlines(d)
            For Each glyph In If(DrawGeometry.IsCallout(d.Shape), outs.Skip(1), outs) ' 圖說的框上面已經畫了
                sb.Append($"<path d=""{ContourPath(glyph.Contours, scale)}"" {ColorAttr("fill", d.TextColorArgb)}/>")
            Next
        End If
        sb.Append("</g>")
        Return sb.ToString()
    End Function

    Private Shared Function ColorAttr(name As String, argb As Integer) As String
        Dim a = Color.FromArgb(argb).A
        Return $"{name}=""{Hex(argb)}""" & If(a < 255, $" {name}-opacity=""{N(a / 255.0)}""", "")
    End Function

    ''' <summary>折線（像素）變成 path 的 d。</summary>
    Private Shared Function FigurePath(figs As IEnumerable(Of DrawGeometry.Figure)) As String
        Dim sb As New StringBuilder()
        For Each f In figs
            If f.Points Is Nothing OrElse f.Points.Length = 0 Then Continue For
            sb.Append($"M{N(f.Points(0).X)} {N(f.Points(0).Y)}")
            If f.Points.Length = 1 Then sb.Append($"L{N(f.Points(0).X + 0.01)} {N(f.Points(0).Y)}")
            For i = 1 To f.Points.Length - 1
                sb.Append($"L{N(f.Points(i).X)} {N(f.Points(i).Y)}")
            Next
            If f.Closed Then sb.Append("Z")
        Next
        Return sb.ToString()
    End Function

    ''' <summary>鋼筆路徑（照片高度單位）變成 path 的 d（保留貝茲曲線）。</summary>
    Private Shared Function ContourPath(contours As IEnumerable(Of PathContour), scale As Single) As String
        Dim sb As New StringBuilder()
        For Each c In contours
            If c.Nodes.Count = 0 Then Continue For
            sb.Append($"M{N(c.Nodes(0).X * scale)} {N(c.Nodes(0).Y * scale)}")
            For i = 0 To c.SegmentCount - 1
                Dim s = DrawGeometry.Segment(c, i)
                sb.Append($"C{N(s(1).X * scale)} {N(s(1).Y * scale)} {N(s(2).X * scale)} {N(s(2).Y * scale)} {N(s(3).X * scale)} {N(s(3).Y * scale)}")
            Next
            If c.Closed Then sb.Append("Z")
        Next
        Return sb.ToString()
    End Function

    ''' <summary>封閉圖形的範圍（clipPath 用）。</summary>
    Private Shared Function RegionPath(frame As DrawLayer, h As Integer) As String
        Dim scale = CSng(h)
        If frame.Shape = DrawShape.Path AndAlso frame.Contours IsNot Nothing Then Return ContourPath(frame.Contours.Where(Function(c) c.Closed), scale)
        Return FigurePath(PathBoolean.RegionOf(frame).Select(Function(r) New DrawGeometry.Figure With {.Points = r.Select(Function(p) New PointF(p.X * scale, p.Y * scale)).ToArray(), .Closed = True}))
    End Function

    ''' <summary>漸層的定義（座標：照片像素）。</summary>
    Private Shared Function GradientDef(id As String, g As GradientFill, c1 As Integer, c2 As Integer, h As Integer) As String
        Dim stops As New List(Of (Offset As Double, Argb As Integer))()
        Select Case g.Colors
            Case GradientColors.StrokeToTransparent
                stops.Add((0, c1)) : stops.Add((1, Color.FromArgb(0, Color.FromArgb(c1)).ToArgb()))
            Case GradientColors.Rainbow
                Dim rainbow = {Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta}
                For i = 0 To rainbow.Length - 1
                    stops.Add((i / 5.0, rainbow(i).ToArgb()))
                Next
            Case Else
                stops.Add((0, c1))
                If g.Stops IsNot Nothing Then
                    For Each st In g.Stops.OrderBy(Function(x) x.Position)
                        stops.Add((Math.Max(0, Math.Min(1, st.Position)), st.Argb)) ' 互動式填色加的中間色標
                    Next
                End If
                stops.Add((1, c2))
        End Select
        If g.Reverse Then stops = stops.Select(Function(s) (Offset:=1 - s.Offset, Argb:=s.Argb)).OrderBy(Function(s) s.Offset).ToList()
        Dim stopXml = String.Concat(stops.Select(Function(s) $"<stop offset=""{N(s.Offset)}"" stop-color=""{Hex(s.Argb)}""" &
                                                              If(Color.FromArgb(s.Argb).A < 255, $" stop-opacity=""{N(Color.FromArgb(s.Argb).A / 255.0)}""", "") & "/>"))
        Dim x1 = g.X1 * h, y1 = g.Y1 * h, x2 = g.X2 * h, y2 = g.Y2 * h
        If g.Kind = GradientKind.Radial Then
            Dim r = Math.Sqrt((x2 - x1) ^ 2 + (y2 - y1) ^ 2)
            Return $"<radialGradient id=""{id}"" gradientUnits=""userSpaceOnUse"" cx=""{N(x1)}"" cy=""{N(y1)}"" r=""{N(r)}"">{stopXml}</radialGradient>"
        End If
        Return $"<linearGradient id=""{id}"" gradientUnits=""userSpaceOnUse"" x1=""{N(x1)}"" y1=""{N(y1)}"" x2=""{N(x2)}"" y2=""{N(y2)}"">{stopXml}</linearGradient>"
    End Function

    ''' <summary>點陣圖層、填滿圖層：單獨畫在透明底上，裁到有內容的範圍，嵌成 PNG。</summary>
    Private Shared Function RasterElement(d As DrawLayer, w As Integer, h As Integer) As String
        Using bmp As New Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb)
            DrawingRenderer.DrawOne(bmp, d)
            Dim px = Perspective.ReadPixels(bmp)
            Dim l = w, t = h, r = -1, b = -1
            For y = 0 To h - 1
                For x = 0 To w - 1
                    If px((y * w + x) * 4 + 3) <> 0 Then
                        If x < l Then l = x
                        If x > r Then r = x
                        If y < t Then t = y
                        If y > b Then b = y
                    End If
                Next
            Next
            If r < 0 Then Return ""
            Using crop = bmp.Clone(Rectangle.FromLTRB(l, t, r + 1, b + 1), Imaging.PixelFormat.Format32bppArgb)
                Return $"<image x=""{l}"" y=""{t}"" width=""{crop.Width}"" height=""{crop.Height}"" href=""data:image/png;base64,{Png(crop)}""/>"
            End Using
        End Using
    End Function

    Private Shared Function Png(bmp As Bitmap) As String
        Using ms As New IO.MemoryStream()
            bmp.Save(ms, Imaging.ImageFormat.Png)
            Return Convert.ToBase64String(ms.ToArray())
        End Using
    End Function
End Class
