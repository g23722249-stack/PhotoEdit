Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>
''' 線條樣式：虛線、箭頭、端點、轉角（座標都是像素）。
''' 虛線把線切成一段一段再交給筆刷畫，所以每種筆刷都能畫虛線；
''' 端點、轉角不是圓的時候，筆印（圓形）做不到，改用幾何外框（GraphicsPath.Widen）當線條的範圍。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    ''' <summary>虛線的樣子（線寬的倍數，畫、空、畫、空…；畫的長度是看起來的長度，含圓頭）。</summary>
    Friend Shared Function DashPattern(d As LineDash) As Single()
        Select Case d
            Case LineDash.Dash : Return {3, 2}
            Case LineDash.Dot : Return {1, 1.6F}
            Case LineDash.DashDot : Return {3, 1.6F, 1, 1.6F}
            Case LineDash.LongDash : Return {7, 2.6F}
        End Select
        Return Nothing
    End Function

    ''' <summary>箭頭的長度與半寬（像素）。</summary>
    Private Shared Function ArrowSize(widthPx As Single) As (Length As Single, Half As Single)
        Return (Math.Max(7, widthPx * 4.2F), Math.Max(4, widthPx * 2.2F))
    End Function

    ''' <summary>
    ''' 套用虛線與箭頭：回傳要描的線（已切段、為箭頭縮短）、箭頭的實心外形，以及開放箭頭要描的線。
    ''' 箭頭只加在開放的線條上。
    ''' </summary>
    Friend Shared Function StyleStroke(figs As List(Of DrawGeometry.Figure), layer As DrawLayer, widthPx As Single) As (Lines As List(Of DrawGeometry.Figure), Heads As List(Of PointF()))
        Dim lines As New List(Of DrawGeometry.Figure)()
        Dim heads As New List(Of PointF())()
        Dim open = Not DrawGeometry.IsClosedLayer(layer)
        Dim sz = ArrowSize(widthPx)
        For Each f In figs
            Dim cur = f
            If open AndAlso Not f.Closed AndAlso f.Points.Length >= 2 Then
                If layer.EndArrow <> ArrowKind.None Then cur = AddArrow(cur, layer.EndArrow, sz, atStart:=False, heads, lines)
                If layer.StartArrow <> ArrowKind.None AndAlso cur.Points IsNot Nothing Then cur = AddArrow(cur, layer.StartArrow, sz, atStart:=True, heads, lines)
            End If
            If cur.Points IsNot Nothing Then lines.Add(cur)
        Next
        Dim pattern = DashPattern(layer.Dash)
        If pattern Is Nothing Then Return (lines, heads)
        ' 圓頭、方頭會在每段兩端各多出半個線寬：畫的長度扣掉、空的長度加上
        Dim capExtra = If(layer.Cap = LineCapKind.Flat, 0.0F, 1.0F)
        Dim px = pattern.Select(Function(v, i) If(i Mod 2 = 0, Math.Max(0, v - capExtra), v + capExtra) * widthPx).ToArray()
        Dim dashed As New List(Of DrawGeometry.Figure)()
        For Each f In lines
            dashed.AddRange(DashFigure(f, px))
        Next
        Return (dashed, heads)
    End Function

    ''' <summary>一條線切成虛線的各段（封閉的線從起點繞一圈）。</summary>
    Private Shared Function DashFigure(f As DrawGeometry.Figure, pattern As Single()) As List(Of DrawGeometry.Figure)
        Dim result As New List(Of DrawGeometry.Figure)()
        Dim pts = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
        Dim pres = f.Pressure
        If pres IsNot Nothing AndAlso f.Closed Then pres = pres.Concat({pres(0)}).ToArray()
        If pts.Length < 2 Then result.Add(f) : Return result
        Dim k = 0, remain = pattern(0), drawing = True
        Dim curPts As New List(Of PointF) From {pts(0)}
        Dim curPres As New List(Of Single)()
        If pres IsNot Nothing Then curPres.Add(pres(0))
        Dim flush = Sub()
                        If curPts.Count >= 1 Then
                            result.Add(New DrawGeometry.Figure With {.Points = curPts.ToArray(), .Closed = False,
                                                                     .Pressure = If(pres Is Nothing, Nothing, curPres.ToArray())})
                        End If
                    End Sub
        For i = 1 To pts.Length - 1
            Dim a = pts(i - 1), b = pts(i)
            Dim segLen = CSng(DrawGeometry.Dist(a, b))
            Dim pa = If(pres Is Nothing, 1.0F, pres(i - 1)), pb = If(pres Is Nothing, 1.0F, pres(i))
            Dim pos = 0.0F
            While segLen - pos > remain
                pos += remain
                Dim t = pos / segLen
                Dim p = New PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t)
                Dim pp = pa + (pb - pa) * t
                If drawing Then
                    curPts.Add(p) : If pres IsNot Nothing Then curPres.Add(pp)
                    flush()
                End If
                curPts = New List(Of PointF) From {p}
                curPres = New List(Of Single)()
                If pres IsNot Nothing Then curPres.Add(pp)
                drawing = Not drawing
                k = (k + 1) Mod pattern.Length
                remain = Math.Max(0.01F, pattern(k))
            End While
            remain -= segLen - pos
            If drawing Then
                curPts.Add(b) : If pres IsNot Nothing Then curPres.Add(pb)
            Else
                curPts = New List(Of PointF) From {b}
                curPres = New List(Of Single)()
                If pres IsNot Nothing Then curPres.Add(pb)
            End If
        Next
        If drawing Then flush()
        Return result
    End Function

    ''' <summary>在線的一端加箭頭：實心的外形加到 heads、開放箭頭的兩撇加到 lines；回傳縮短後的線。</summary>
    Private Shared Function AddArrow(f As DrawGeometry.Figure, kind As ArrowKind, sz As (Length As Single, Half As Single), atStart As Boolean,
                                     heads As List(Of PointF()), lines As List(Of DrawGeometry.Figure)) As DrawGeometry.Figure
        Dim pts = If(atStart, f.Points.Reverse().ToArray(), f.Points)
        Dim pres = If(f.Pressure Is Nothing, Nothing, If(atStart, f.Pressure.Reverse().ToArray(), f.Pressure))
        Dim tip = pts(pts.Length - 1)
        ' 方向：從尾端往回走一個箭頭長度的地方指向尖端（曲線末端比較穩）
        Dim back = PointBack(pts, sz.Length)
        Dim dx = tip.X - back.X, dy = tip.Y - back.Y
        Dim len = CSng(Math.Sqrt(dx * dx + dy * dy))
        If len < 0.001F Then Return f
        dx /= len : dy /= len
        Dim nx = -dy, ny = dx
        Dim at = Function(along As Single, side As Single) New PointF(tip.X - dx * along + nx * side, tip.Y - dy * along + ny * side)
        Dim trim = 0.0F
        Select Case kind
            Case ArrowKind.Triangle
                heads.Add({tip, at(sz.Length, sz.Half), at(sz.Length, -sz.Half)})
                trim = sz.Length * 0.8F
            Case ArrowKind.Open
                lines.Add(New DrawGeometry.Figure With {.Points = {at(sz.Length * 0.9F, sz.Half), tip, at(sz.Length * 0.9F, -sz.Half)}})
            Case ArrowKind.Circle
                Dim r = sz.Half * 0.8F
                heads.Add(Enumerable.Range(0, 24).Select(Function(i) New PointF(CSng(tip.X + r * Math.Cos(i * Math.PI / 12)), CSng(tip.Y + r * Math.Sin(i * Math.PI / 12)))).ToArray())
            Case ArrowKind.Square
                Dim r = sz.Half * 0.75F
                heads.Add({at(-r, r), at(-r, -r), at(r, -r), at(r, r)})
            Case ArrowKind.Diamond
                heads.Add({tip, at(sz.Length * 0.5F, sz.Half * 0.8F), at(sz.Length, 0), at(sz.Length * 0.5F, -sz.Half * 0.8F)})
                trim = sz.Length * 0.5F
        End Select
        If trim <= 0 Then Return f
        ' 縮短：從尾端往回切掉 trim 的長度
        Dim keep As New List(Of PointF)(), keepP As New List(Of Single)()
        Dim total = 0.0F
        For i = 1 To pts.Length - 1
            total += CSng(DrawGeometry.Dist(pts(i - 1), pts(i)))
        Next
        If total <= trim + 0.5F Then Return New DrawGeometry.Figure() ' 線比箭頭短：只留箭頭（Points 是 Nothing）
        Dim target = total - trim, walked = 0.0F
        keep.Add(pts(0)) : If pres IsNot Nothing Then keepP.Add(pres(0))
        For i = 1 To pts.Length - 1
            Dim d = CSng(DrawGeometry.Dist(pts(i - 1), pts(i)))
            If walked + d >= target Then
                Dim t = If(d <= 0, 0, (target - walked) / d)
                keep.Add(New PointF(pts(i - 1).X + (pts(i).X - pts(i - 1).X) * t, pts(i - 1).Y + (pts(i).Y - pts(i - 1).Y) * t))
                If pres IsNot Nothing Then keepP.Add(pres(i - 1) + (pres(i) - pres(i - 1)) * t)
                Exit For
            End If
            walked += d
            keep.Add(pts(i)) : If pres IsNot Nothing Then keepP.Add(pres(i))
        Next
        Dim outPts = keep.ToArray(), outP = If(pres Is Nothing, Nothing, keepP.ToArray())
        If atStart Then
            Array.Reverse(outPts)
            If outP IsNot Nothing Then Array.Reverse(outP)
        End If
        Return New DrawGeometry.Figure With {.Points = outPts, .Closed = False, .Pressure = outP}
    End Function

    ''' <summary>從線的尾端往回走 dist 的位置。</summary>
    Private Shared Function PointBack(pts As PointF(), dist As Single) As PointF
        Dim walked = 0.0F
        For i = pts.Length - 1 To 1 Step -1
            Dim d = CSng(DrawGeometry.Dist(pts(i - 1), pts(i)))
            If walked + d >= dist Then
                Dim t = (dist - walked) / Math.Max(0.0001F, d)
                Return New PointF(pts(i).X + (pts(i - 1).X - pts(i).X) * t, pts(i).Y + (pts(i - 1).Y - pts(i).Y) * t)
            End If
            walked += d
        Next
        Return pts(0)
    End Function

    ''' <summary>端點或轉角不是圓的：用幾何外框算線條的範圍（0..1，region 內）。</summary>
    Private Shared Function GeometricStrokeMask(lines As List(Of DrawGeometry.Figure), layer As DrawLayer, widthPx As Single, region As Rectangle) As Single()
        Using bmp As New Bitmap(region.Width, region.Height, Imaging.PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp), pen As New Pen(Color.White, widthPx)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.TranslateTransform(-region.X, -region.Y)
                Dim cap = If(layer.Cap = LineCapKind.Flat, LineCap.Flat, If(layer.Cap = LineCapKind.Square, LineCap.Square, LineCap.Round))
                pen.StartCap = cap : pen.EndCap = cap
                pen.LineJoin = If(layer.Join = LineJoinKind.Miter, LineJoin.Miter, If(layer.Join = LineJoinKind.Bevel, LineJoin.Bevel, LineJoin.Round))
                pen.MiterLimit = 4
                For Each f In lines
                    If f.Points.Length = 1 Then
                        ' 點線的點：方頭畫方塊，平頭不畫
                        If layer.Cap = LineCapKind.Square Then g.FillRectangle(Brushes.White, f.Points(0).X - widthPx / 2, f.Points(0).Y - widthPx / 2, widthPx, widthPx)
                        Continue For
                    End If
                    If f.Closed Then g.DrawPolygon(pen, f.Points) Else g.DrawLines(pen, f.Points)
                Next
            End Using
            Dim px = Perspective.ReadPixels(bmp)
            Dim mask(region.Width * region.Height - 1) As Single
            For i = 0 To mask.Length - 1
                mask(i) = px(i * 4 + 3) / 255.0F
            Next
            Return mask
        End Using
    End Function

    Private Shared Function NeedsGeometricStroke(layer As DrawLayer) As Boolean
        Return layer.Cap <> LineCapKind.Round OrElse layer.Join <> LineJoinKind.Round
    End Function
End Class
