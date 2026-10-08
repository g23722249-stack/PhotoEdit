Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>
''' 特殊筆（參考 Corel Painter）：
''' 粒子筆（重力、流場、彈簧）、貼圖噴槍（Image Hose），
''' 以及會讀取畫布顏色、只能畫在點陣圖層的混色筆、塗抹筆、仿製筆。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    '=====================================================================
    ' GDI+ 畫到 Canvas（粒子、貼圖用）
    '=====================================================================

    ''' <summary>用 GDI+ 畫（座標為照片像素），結果疊到 cv 上。</summary>
    Private Shared Sub PaintOnCanvas(cv As Canvas, draw As Action(Of Graphics))
        Using bmp As New Bitmap(cv.W, cv.H, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.TranslateTransform(-cv.OX, -cv.OY)
                draw(g)
            End Using
            Dim px = Perspective.ReadPixels(bmp)
            For i = 0 To cv.W * cv.H - 1
                Dim al = px(i * 4 + 3)
                If al = 0 Then Continue For
                cv.Over(i, px(i * 4 + 2) / 255.0F, px(i * 4 + 1) / 255.0F, px(i * 4) / 255.0F, al / 255.0F)
            Next
        End Using
    End Sub

    '=====================================================================
    ' 粒子筆
    '=====================================================================

    ''' <summary>
    ''' 粒子筆：沿筆畫放出粒子，記下每顆粒子的軌跡畫成細絲。
    ''' 重力：往外噴、往下墜（毛髮、噴泉、火花）；流動：順著雜訊流場彎曲（煙絲、水流、髮絲）；
    ''' 彈簧：粒子被筆尖用彈簧拉著走，甩出一圈圈的線（能量線、鉛筆速寫感）。
    ''' </summary>
    Private Shared Sub RenderParticles(cv As Canvas, figs As List(Of DrawGeometry.Figure), layer As DrawLayer, w As Single, rnd As Random, imageH As Integer)
        Dim baseC = Color.FromArgb(layer.StrokeColorArgb)
        Dim flow = Math.Max(0.05, layer.Flow / 100.0)
        Dim hueJ = layer.HueJitter, lumJ = Math.Max(8, layer.LumJitter)
        Dim strands As New List(Of (Pts As PointF(), Col As Color, Width As Single))()
        Dim colorOf = Function() As Color
                          Dim c = JitterColor(baseC.R / 255.0F, baseC.G / 255.0F, baseC.B / 255.0F, hueJ, lumJ, rnd)
                          Return Color.FromArgb(CInt(Clamp01(CSng(0.55 * flow + 0.25)) * 255), CInt(c.R * 255), CInt(c.G * 255), CInt(c.B * 255))
                      End Function
        For Each f In figs
            Dim path = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
            Select Case layer.Particle
                Case ParticleKind.Spring
                    ' 筆尖沿路移動，每顆粒子用不同的彈性跟著跑。
                    Dim anchors As New List(Of (X As Single, Y As Single, P As Single))()
                    Walk(path, f.Pressure, Math.Max(0.6F, w * 0.12F), Sub(x, y, p, dx, dy, s, t) anchors.Add((x, y, p)))
                    Dim count = 6 + CInt(flow * 8)
                    For k = 0 To count - 1
                        Dim stiff = 0.04 + rnd.NextDouble() * 0.14, damp = 0.06 + rnd.NextDouble() * 0.1
                        Dim px = anchors(0).X + (rnd.NextDouble() * 2 - 1) * w, py = anchors(0).Y + (rnd.NextDouble() * 2 - 1) * w
                        Dim vx = (rnd.NextDouble() * 2 - 1) * w * 0.3, vy = (rnd.NextDouble() * 2 - 1) * w * 0.3
                        Dim pts As New List(Of PointF)()
                        For Each a In anchors
                            vx += (a.X - px) * stiff - vx * damp
                            vy += (a.Y - py) * stiff - vy * damp
                            px += vx : py += vy
                            pts.Add(New PointF(CSng(px), CSng(py)))
                        Next
                        strands.Add((pts.ToArray(), colorOf(), Math.Max(0.6F, w * CSng(0.03 + rnd.NextDouble() * 0.04))))
                    Next
                Case Else
                    Dim gravity = layer.Particle = ParticleKind.Gravity
                    Walk(path, f.Pressure, Math.Max(0.6F, w * 0.22F),
                         Sub(x, y, p, dx, dy, s, t)
                             If rnd.NextDouble() > 0.35 + 0.65 * flow * p Then Return
                             Dim ang = rnd.NextDouble() * 2 * Math.PI, rr = Math.Sqrt(rnd.NextDouble()) * w * 0.5
                             Dim px = x + Math.Cos(ang) * rr, py = y + Math.Sin(ang) * rr
                             Dim speed = w * (0.06 + rnd.NextDouble() * 0.1)
                             Dim vx = (dx * (0.4 + rnd.NextDouble()) + (rnd.NextDouble() * 2 - 1) * 0.8) * speed
                             Dim vy = (dy * (0.4 + rnd.NextDouble()) + (rnd.NextDouble() * 2 - 1) * 0.8) * speed
                             If gravity Then vy -= speed * 0.8 ' 先往上噴一點再落下
                             Dim steps = If(gravity, 16 + rnd.Next(18), 28 + rnd.Next(30))
                             Dim pts As New List(Of PointF) From {New PointF(CSng(px), CSng(py))}
                             For k = 1 To steps
                                 If gravity Then
                                     vy += w * 0.006
                                     vx *= 0.985 : vy *= 0.985
                                 Else
                                     ' 流場：方向由雜訊決定，粒子慢慢轉向
                                     Dim fa = Noise2(CSng(px / (w * 3.5)), CSng(py / (w * 3.5)), layer.Seed) * 4 * Math.PI
                                     Dim sp = w * 0.09
                                     vx += (Math.Cos(fa) * sp - vx) * 0.22
                                     vy += (Math.Sin(fa) * sp - vy) * 0.22
                                 End If
                                 px += vx : py += vy
                                 pts.Add(New PointF(CSng(px), CSng(py)))
                             Next
                             strands.Add((pts.ToArray(), colorOf(), Math.Max(0.6F, w * CSng(0.025 + rnd.NextDouble() * 0.035) * (0.5F + 0.5F * p))))
                         End Sub)
            End Select
        Next
        If strands.Count = 0 Then Return
        PaintOnCanvas(cv, Sub(g)
                              For Each st In strands
                                  If st.Pts.Length < 2 Then Continue For
                                  ' 細絲尾端漸淡：分三段，越後面越透明越細
                                  Dim n = st.Pts.Length
                                  For seg = 0 To 2
                                      Dim a0 = seg * (n - 1) \ 3, a1 = Math.Min(n - 1, (seg + 1) * (n - 1) \ 3 + 1)
                                      If a1 - a0 < 1 Then Continue For
                                      Dim fade = 1 - seg * 0.3
                                      Using pen As New Pen(Color.FromArgb(CInt(st.Col.A * fade), st.Col), CSng(st.Width * (1 - seg * 0.2))) With {
                                          .StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                                          g.DrawLines(pen, st.Pts.Skip(a0).Take(a1 - a0 + 1).ToArray())
                                      End Using
                                  Next
                              Next
                          End Sub)
    End Sub

    '=====================================================================
    ' 貼圖噴槍
    '=====================================================================

    ''' <summary>
    ''' 貼圖噴槍（Painter 的 Image Hose）：沿筆畫每隔一段噴一張貼圖，隨機挑圖、旋轉、大小。
    ''' 貼圖大小約為筆寬的 3 倍（筆壓、大小變化會改變），間距、散佈用進階參數調整。
    ''' 「內建」主題用程式畫的向量貼圖（各自的預設顏色）。
    ''' </summary>
    Private Shared Sub RenderHose(cv As Canvas, figs As List(Of DrawGeometry.Figure), layer As DrawLayer, w As Single, rnd As Random)
        Dim theme = If(String.IsNullOrEmpty(layer.HoseTheme), StickerLibrary.BuiltInTheme, layer.HoseTheme)
        Dim builtIn = theme = StickerLibrary.BuiltInTheme
        Dim files As List(Of String) = If(builtIn, Nothing, StickerLibrary.Stickers(theme))
        Dim count = If(builtIn, BuiltInStickers.Items.Count, files.Count)
        If count = 0 Then Return
        Dim baseSize = w * 3
        Dim spacing = baseSize * If(layer.SpacingPct > 0, layer.SpacingPct / 100.0F, 0.8F)
        Dim scatter = layer.Scatter / 100.0F
        Dim sizeJ = Math.Max(0.25F, layer.SizeJitter / 100.0F)
        Dim drops As New List(Of (X As Single, Y As Single, Size As Single, Angle As Single, Index As Integer))()
        For Each f In figs
            Dim path = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
            Walk(path, f.Pressure, Math.Max(2, spacing),
                 Sub(x, y, p, dx, dy, s, t)
                     Dim size = baseSize * (0.5F + 0.5F * p) * Math.Max(0.2F, 1 + CSng(rnd.NextDouble() * 2 - 1) * sizeJ * 0.7F)
                     Dim off = CSng(rnd.NextDouble() * 2 - 1) * scatter * baseSize
                     Dim ang = CSng((rnd.NextDouble() * 2 - 1) * 35)
                     drops.Add((x - dy * off, y + dx * off, size, ang, rnd.Next(count)))
                 End Sub)
        Next
        PaintOnCanvas(cv, Sub(g)
                              For Each d In drops
                                  Dim state = g.Save()
                                  g.TranslateTransform(d.X, d.Y)
                                  g.RotateTransform(d.Angle)
                                  If builtIn Then
                                      Dim it = BuiltInStickers.Items(d.Index)
                                      Using path = BuiltInStickers.Build(it.Key, 0, 0, d.Size), br As New SolidBrush(it.Color)
                                          If path IsNot Nothing Then g.FillPath(br, path)
                                      End Using
                                  Else
                                      Dim img = StickerLibrary.GetImage(files(d.Index))
                                      If img IsNot Nothing Then
                                          SyncLock img
                                              Dim k = d.Size / Math.Max(img.Width, img.Height)
                                              Dim iw = img.Width * k, ih = img.Height * k
                                              g.DrawImage(img, New RectangleF(-iw / 2, -ih / 2, iw, ih))
                                          End SyncLock
                                      End If
                                  End If
                                  g.Restore(state)
                              Next
                          End Sub)
    End Sub

    '=====================================================================
    ' 混色筆、塗抹筆、仿製筆（讀取畫布，只在點陣圖層裡）
    '=====================================================================

    ''' <summary>
    ''' 在點陣圖層上套用一筆會讀取畫布的筆刷。
    ''' layerPx：圖層本身（BGRA，w × h，會被修改）；belowPx：圖層下面已經合成好的照片與圖層（BGRA，w × h）；
    ''' (ox, oy)：圖層的位移（像素），圖層的 (x, y) 疊在下面的 (x + ox, y + oy)。
    ''' 先算出「畫布」= 圖層疊在下面之上的顏色，筆刷在畫布上混色／推開／仿製，
    ''' 最後把結果換算回圖層的像素（下面的照片不動）。
    ''' </summary>
    Friend Shared Sub ApplySamplingOp(layerPx As Byte(), belowPx As Byte(), w As Integer, h As Integer, op As DrawLayer, ox As Integer, oy As Integer)
        Dim scale = CSng(h)
        Dim figs = DrawGeometry.Figures(op).
            Select(Function(f) New DrawGeometry.Figure With {.Points = f.Points.Select(Function(p) New PointF(p.X * scale, p.Y * scale)).ToArray(),
                                                             .Closed = f.Closed, .Pressure = f.Pressure}).
            Where(Function(f) f.Points.Length > 0).ToList()
        If figs.Count = 0 Then Return
        Dim widthPx = CSng(Math.Max(1, op.StrokeWidth * scale))
        Dim radius = Math.Max(1.0F, widthPx / 2)
        Dim reach = CInt(Math.Ceiling(radius)) + 2
        Dim pts = figs.SelectMany(Function(f) f.Points).ToList()
        Dim region = Rectangle.FromLTRB(CInt(Math.Floor(pts.Min(Function(p) p.X))) - reach, CInt(Math.Floor(pts.Min(Function(p) p.Y))) - reach,
                                        CInt(Math.Ceiling(pts.Max(Function(p) p.X))) + reach, CInt(Math.Ceiling(pts.Max(Function(p) p.Y))) + reach)
        region.Intersect(New Rectangle(0, 0, w, h))
        If region.Width <= 0 OrElse region.Height <= 0 Then Return
        Dim rw = region.Width, rh = region.Height

        ' 畫布顏色（預乘 alpha，0..1）：圖層 over 下面。
        Dim compAt = Function(x As Integer, y As Integer) As (R As Single, G As Single, B As Single, A As Single)
                         Dim lr = 0.0F, lg = 0.0F, lb = 0.0F, la = 0.0F
                         If x >= 0 AndAlso y >= 0 AndAlso x < w AndAlso y < h Then
                             Dim i = (y * w + x) * 4
                             la = layerPx(i + 3) / 255.0F
                             lr = layerPx(i + 2) / 255.0F * la : lg = layerPx(i + 1) / 255.0F * la : lb = layerPx(i) / 255.0F * la
                         End If
                         Dim bx = x + ox, by = y + oy
                         Dim br = 0.0F, bg = 0.0F, bb = 0.0F, ba = 0.0F
                         If belowPx IsNot Nothing AndAlso bx >= 0 AndAlso by >= 0 AndAlso bx < w AndAlso by < h Then
                             Dim j = (by * w + bx) * 4
                             ba = belowPx(j + 3) / 255.0F
                             br = belowPx(j + 2) / 255.0F * ba : bg = belowPx(j + 1) / 255.0F * ba : bb = belowPx(j) / 255.0F * ba
                         End If
                         Dim k = 1 - la
                         Return (lr + br * k, lg + bg * k, lb + bb * k, la + ba * k)
                     End Function
        Dim n = rw * rh
        Dim cr(n - 1) As Single, cg(n - 1) As Single, cb(n - 1) As Single, ca(n - 1) As Single, touched(n - 1) As Single
        For y = 0 To rh - 1
            For x = 0 To rw - 1
                Dim c = compAt(region.X + x, region.Y + y)
                Dim i = y * rw + x
                cr(i) = c.R : cg(i) = c.G : cb(i) = c.B : ca(i) = c.A
            Next
        Next

        Dim strength = Math.Max(0, Math.Min(100, op.Opacity)) / 100.0F
        Dim edge = Math.Max(0.35F, op.Softness / 100.0F)
        Dim wet = op.Wet / 100.0F
        Dim paint = Color.FromArgb(op.StrokeColorArgb)
        Dim pr = paint.R / 255.0F, pg = paint.G / 255.0F, pb = paint.B / 255.0F
        Dim cdx = CInt(Math.Round(op.CloneDX * scale)), cdy = CInt(Math.Round(op.CloneDY * scale))
        Dim r0 = CInt(Math.Ceiling(radius))
        Dim side = r0 * 2 + 1
        ' 筆尖帶著的顏料（預乘）：塗抹筆從畫布沾起，混色筆從線條色開始。
        Dim carry(side * side * 4 - 1) As Single
        Dim mask(side * side - 1) As Single
        Dim softW = radius * edge, inner = radius - softW
        For yy = -r0 To r0
            For xx = -r0 To r0
                Dim d = CSng(Math.Sqrt(xx * xx + yy * yy))
                Dim m As Single
                If d >= radius + 0.5F Then
                    m = 0
                ElseIf d <= inner Then
                    m = 1
                Else
                    m = CSng(0.5 + 0.5 * Math.Cos(Math.Min(1, (d - inner) / Math.Max(0.5F, softW)) * Math.PI))
                End If
                mask((yy + r0) * side + xx + r0) = m
            Next
        Next

        For Each f In figs
            Dim path = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
            Dim first = True
            Dim spacing = Math.Max(0.5F, radius * If(op.Brush = BrushKind.Clone, 0.25F, 0.12F))
            Walk(path, f.Pressure, spacing,
                 Sub(fx, fy, p, dx, dy, s, total)
                     Dim cx = CInt(Math.Round(fx)) - region.X, cy = CInt(Math.Round(fy)) - region.Y
                     If first Then
                         ' 第一個筆印：塗抹筆沾起底下的顏色，混色筆裝滿線條色。
                         For yy = -r0 To r0
                             For xx = -r0 To r0
                                 Dim k = ((yy + r0) * side + xx + r0) * 4
                                 Dim px = cx + xx, py = cy + yy
                                 If op.Brush = BrushKind.Mixer OrElse px < 0 OrElse py < 0 OrElse px >= rw OrElse py >= rh Then
                                     carry(k) = pr : carry(k + 1) = pg : carry(k + 2) = pb : carry(k + 3) = 1
                                     If op.Brush = BrushKind.Smudge AndAlso (px < 0 OrElse py < 0 OrElse px >= rw OrElse py >= rh) Then carry(k + 3) = 0
                                 Else
                                     Dim i = py * rw + px
                                     carry(k) = cr(i) : carry(k + 1) = cg(i) : carry(k + 2) = cb(i) : carry(k + 3) = ca(i)
                                 End If
                             Next
                         Next
                         first = False
                     End If
                     Dim amt = strength * (0.35F + 0.65F * p)
                     For yy = -r0 To r0
                         Dim py = cy + yy
                         If py < 0 OrElse py >= rh Then Continue For
                         For xx = -r0 To r0
                             Dim px = cx + xx
                             If px < 0 OrElse px >= rw Then Continue For
                             Dim m = mask((yy + r0) * side + xx + r0)
                             If m <= 0 Then Continue For
                             Dim i = py * rw + px
                             Dim a = m * amt
                             Dim curR = cr(i), curG = cg(i), curB = cb(i), curA = ca(i)
                             Select Case op.Brush
                                 Case BrushKind.Clone
                                     Dim src = compAt(region.X + px + cdx, region.Y + py + cdy)
                                     cr(i) += (src.R - curR) * a : cg(i) += (src.G - curG) * a : cb(i) += (src.B - curB) * a : ca(i) += (src.A - curA) * a
                                 Case Else
                                     Dim k = ((yy + r0) * side + xx + r0) * 4
                                     ' 把筆尖上的顏料抹上去
                                     cr(i) += (carry(k) - curR) * a : cg(i) += (carry(k + 1) - curG) * a
                                     cb(i) += (carry(k + 2) - curB) * a : ca(i) += (carry(k + 3) - curA) * a
                                     ' 筆尖沾起畫布原本的顏色：塗抹筆越濕帶得越遠；混色筆越濕混得越多。
                                     Dim pick = If(op.Brush = BrushKind.Smudge, 1 - (0.35F + 0.6F * wet), 0.15F + 0.6F * wet) * m
                                     carry(k) += (curR - carry(k)) * pick : carry(k + 1) += (curG - carry(k + 1)) * pick
                                     carry(k + 2) += (curB - carry(k + 2)) * pick : carry(k + 3) += (curA - carry(k + 3)) * pick
                                     If op.Brush = BrushKind.Mixer Then
                                         ' 一邊畫一邊補顏料（流量越大補越多）
                                         Dim reload = 0.01F + 0.06F * op.Flow / 100.0F
                                         carry(k) += (pr - carry(k)) * reload : carry(k + 1) += (pg - carry(k + 1)) * reload
                                         carry(k + 2) += (pb - carry(k + 2)) * reload : carry(k + 3) += (1 - carry(k + 3)) * reload
                                     End If
                             End Select
                             If a > touched(i) Then touched(i) = a
                         Next
                     Next
                 End Sub)
        Next

        ' 換算回圖層：讓「新圖層 over 下面」等於畫布的新顏色。
        For y = 0 To rh - 1
            For x = 0 To rw - 1
                Dim i = y * rw + x
                If touched(i) <= 0 Then Continue For
                Dim gx = region.X + x, gy = region.Y + y
                Dim li = (gy * w + gx) * 4
                Dim la = layerPx(li + 3) / 255.0F
                Dim bx = gx + ox, by = gy + oy
                Dim br = 0.0F, bgc = 0.0F, bb = 0.0F, ba = 0.0F
                If belowPx IsNot Nothing AndAlso bx >= 0 AndAlso by >= 0 AndAlso bx < w AndAlso by < h Then
                    Dim j = (by * w + bx) * 4
                    ba = belowPx(j + 3) / 255.0F
                    br = belowPx(j + 2) / 255.0F * ba : bgc = belowPx(j + 1) / 255.0F * ba : bb = belowPx(j) / 255.0F * ba
                End If
                ' 圖層要多不透明：至少能蓋過這次改動的比例；底下透明時就照畫布的不透明度。
                Dim na = Math.Max(la, Math.Min(1, touched(i) * 1.15F))
                If ba < 0.999F Then na = Math.Max(na, Clamp01((ca(i) - ba) / Math.Max(0.001F, 1 - ba)))
                If na <= 0.002F Then Continue For
                Dim k2 = 1 - na
                Dim nr = Clamp01((cr(i) - br * k2) / na), ng = Clamp01((cg(i) - bgc * k2) / na), nb = Clamp01((cb(i) - bb * k2) / na)
                layerPx(li) = CByte(Math.Round(nb * 255)) : layerPx(li + 1) = CByte(Math.Round(ng * 255))
                layerPx(li + 2) = CByte(Math.Round(nr * 255)) : layerPx(li + 3) = CByte(Math.Round(Clamp01(na) * 255))
            Next
        Next
    End Sub
End Class
