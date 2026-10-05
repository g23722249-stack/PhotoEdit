Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>
''' 特效筆：62 種效果，分「光／能量、火／煙／爆炸、液體／自然、動態效果、魔法／科幻、漫畫效果」六類。
''' 用幾種共用的做法組合：加亮光點（光、火）、半透明煙團、沿線發光（光線、雷射、閃電）、
''' GDI+ 畫形狀再加光暈（魔法陣、HUD、碎片、雨、漫畫線）、覆蓋率上色（燃燒邊緣、泥漿、網點、排線）。
''' 「集中線、放射線、衝擊波、黑洞、爆炸光、HUD」以每一筆的中心為準，一筆一個；其餘沿筆畫分布。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    Private Structure Rgb
        Public R As Double, G As Double, B As Double
        Public Sub New(r As Double, g As Double, b As Double)
            Me.R = r : Me.G = g : Me.B = b
        End Sub
        Public Shared Function FromColor(c As Color) As Rgb
            Return New Rgb(c.R / 255.0, c.G / 255.0, c.B / 255.0)
        End Function
        Public Shared Function Lerp(a As Rgb, b As Rgb, t As Double) As Rgb
            t = Math.Max(0, Math.Min(1, t))
            Return New Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t)
        End Function
        Public Function Times(k As Double) As Rgb
            Return New Rgb(R * k, G * k, B * k)
        End Function
        Public Function ToColor(alpha As Double) As Color
            Return Color.FromArgb(B255(alpha), B255(R), B255(G), B255(B))
        End Function
        Private Shared Function B255(v As Double) As Integer
            Return CInt(Math.Max(0, Math.Min(255, v * 255)))
        End Function
    End Structure

    Private Shared ReadOnly White As New Rgb(1, 1, 1)

    Private Structure FxSample
        Public X As Double, Y As Double, P As Double, Dx As Double, Dy As Double, S As Double, Total As Double
        Public ReadOnly Property Along As Double
            Get
                Return If(Total > 0, S / Total, 0)
            End Get
        End Property
    End Structure

    ''' <summary>一次特效算圖的工具箱：取樣、亂數、加亮光點、煙團、發光線、GDI+ 圖形。</summary>
    Private NotInheritable Class FxCtx
        Public Cv As Canvas
        Public Light As Single()
        Public W As Double
        Public Tint As Rgb
        Public Flow As Double
        Public Rnd As Random
        Public Paths As New List(Of PointF())()
        Public Pres As New List(Of Single())()
        Public ImageH As Integer
        Public Seed As Integer

        Public Function R() As Double
            Return Rnd.NextDouble()
        End Function

        ''' <summary>-1..1</summary>
        Public Function RS() As Double
            Return Rnd.NextDouble() * 2 - 1
        End Function

        Public Function Mix(t As Double) As Rgb
            Return Rgb.Lerp(Tint, White, t)
        End Function

        ''' <summary>沿所有筆畫每隔 spacing 取一點。</summary>
        Public Sub Every(spacing As Double, visit As Action(Of FxSample))
            For k = 0 To Paths.Count - 1
                DrawingRenderer.Walk(Paths(k), Pres(k), CSng(Math.Max(0.35, spacing)),
                    Sub(x, y, p, dx, dy, s, t) visit(New FxSample With {.X = x, .Y = y, .P = p, .Dx = dx, .Dy = dy, .S = s, .Total = t}))
            Next
        End Sub

        ''' <summary>每一筆的中心與大小（外接矩形對角線的一半）。</summary>
        Public Function Centers() As List(Of (X As Double, Y As Double, Radius As Double))
            Dim list As New List(Of (Double, Double, Double))()
            For Each p In Paths
                Dim minX = p.Min(Function(q) q.X), maxX = p.Max(Function(q) q.X), minY = p.Min(Function(q) q.Y), maxY = p.Max(Function(q) q.Y)
                list.Add(((minX + maxX) / 2.0, (minY + maxY) / 2.0, Math.Sqrt((maxX - minX) ^ 2 + (maxY - minY) ^ 2) / 2))
            Next
            Return list
        End Function

        ''' <summary>沿筆畫等距放置（短筆畫只放中心一個），回傳位置。</summary>
        Public Function Stamps(spacing As Double) As List(Of PointF)
            Dim list As New List(Of PointF)()
            Dim cs = Centers()
            For k = 0 To Paths.Count - 1
                Dim len = 0.0
                For i = 1 To Paths(k).Length - 1
                    len += DrawGeometry.Dist(Paths(k)(i - 1), Paths(k)(i))
                Next
                If len < spacing Then
                    list.Add(New PointF(CSng(cs(k).X), CSng(cs(k).Y)))
                Else
                    DrawingRenderer.Walk(Paths(k), Nothing, CSng(spacing), Sub(x, y, p, dx, dy, s, t) list.Add(New PointF(x, y)))
                End If
            Next
            Return list
        End Function

        ''' <summary>加亮光點（最後統一換算成透明度）。</summary>
        Public Sub Glow(x As Double, y As Double, radius As Double, c As Rgb, amount As Double, Optional stretchY As Double = 1)
            If radius <= 0.3 OrElse amount <= 0 Then Return
            Dim cr = CSng(c.R), cg = CSng(c.G), cb = CSng(c.B), a0 = CSng(amount)
            DrawingRenderer.ForDisc(Cv, CSng(x), CSng(y), CSng(radius), CSng(stretchY),
                Sub(i, t)
                    Dim a = (1 - t * t) * (1 - t * t) * a0
                    Light(i * 3) += cr * a : Light(i * 3 + 1) += cg * a : Light(i * 3 + 2) += cb * a
                End Sub)
        End Sub

        ''' <summary>半透明的一團（煙、塵、水氣）。</summary>
        Public Sub Puff(x As Double, y As Double, radius As Double, c As Rgb, amount As Double)
            If radius <= 0.3 OrElse amount <= 0 Then Return
            Dim cr = CSng(c.R), cg = CSng(c.G), cb = CSng(c.B), a0 = CSng(amount)
            DrawingRenderer.ForDisc(Cv, CSng(x), CSng(y), CSng(radius), 1, Sub(i, t) Cv.Over(i, cr, cg, cb, (1 - t * t) * (1 - t * t) * a0))
        End Sub

        ''' <summary>散景光點：邊緣稍亮、平坦的圓。</summary>
        Public Sub Bokeh(x As Double, y As Double, radius As Double, c As Rgb, amount As Double)
            Dim cr = CSng(c.R), cg = CSng(c.G), cb = CSng(c.B), a0 = CSng(amount)
            DrawingRenderer.ForDisc(Cv, CSng(x), CSng(y), CSng(radius), 1,
                Sub(i, t)
                    Dim a = If(t < 0.82F, 0.55F + 0.45F * t * t * t * t, Math.Max(0, (1 - t) / 0.18F))
                    Cv.Over(i, cr, cg, cb, a * a0)
                End Sub)
        End Sub

        ''' <summary>沿折線的發光線：外層柔光＋內層亮芯（強度與取樣間距無關）。</summary>
        Public Sub GlowLine(pts As IList(Of PointF), coreR As Double, glowR As Double, c As Rgb, coreAmount As Double, glowAmount As Double)
            If pts.Count = 0 Then Return
            Dim spacing = Math.Max(0.5, coreR * 0.6)
            Dim core = Rgb.Lerp(c, White, 0.75)
            Dim arr = pts.ToArray()
            DrawingRenderer.Walk(arr, Nothing, CSng(spacing),
                Sub(x, y, p, dx, dy, s, t)
                    Glow(x, y, glowR, c, glowAmount * spacing / Math.Max(1, glowR) * 1.6)
                    Glow(x, y, coreR, core, coreAmount * spacing / Math.Max(0.6, coreR) * 1.4)
                End Sub)
        End Sub

        ''' <summary>用 GDI+ 畫（座標為照片像素），glowRadius &gt; 0 時在圖形下方加上同色光暈。</summary>
        Public Sub Paint(draw As Action(Of Graphics), Optional glowRadius As Double = 0, Optional glowColor As Rgb? = Nothing,
                         Optional glowAmount As Double = 0.85, Optional opacity As Double = 1)
            Using bmp As New Bitmap(Cv.W, Cv.H, PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(bmp)
                    g.SmoothingMode = SmoothingMode.AntiAlias
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality
                    g.TranslateTransform(-Cv.OX, -Cv.OY)
                    draw(g)
                End Using
                Dim px = Perspective.ReadPixels(bmp)
                Dim n = Cv.W * Cv.H
                If glowRadius >= 1 Then
                    Dim a(n - 1) As Single
                    For i = 0 To n - 1
                        a(i) = px(i * 4 + 3) / 255.0F
                    Next
                    Dim blur = DrawingRenderer.BoxBlur(DrawingRenderer.BoxBlur(a, Cv.W, Cv.H, CInt(glowRadius)), Cv.W, Cv.H, CInt(glowRadius))
                    Dim gc = If(glowColor, Tint)
                    For i = 0 To n - 1
                        Dim ga = Math.Min(1, blur(i) * 2.2F) * CSng(glowAmount)
                        If ga > 0.004F Then
                            Light(i * 3) += CSng(gc.R) * ga : Light(i * 3 + 1) += CSng(gc.G) * ga : Light(i * 3 + 2) += CSng(gc.B) * ga
                        End If
                    Next
                End If
                Dim op = CSng(opacity)
                For i = 0 To n - 1
                    Dim al = px(i * 4 + 3)
                    If al = 0 Then Continue For
                    Cv.Over(i, px(i * 4 + 2) / 255.0F, px(i * 4 + 1) / 255.0F, px(i * 4) / 255.0F, al / 255.0F * op)
                Next
            End Using
        End Sub

        ''' <summary>把目前的加亮光點蓋上去並清空（之後畫的東西會在光的上面）。</summary>
        Public Sub FlushLight()
            DrawingRenderer.FlushLight(Cv, Light)
            Array.Clear(Light, 0, Light.Length)
        End Sub

        Public Function Pen(c As Rgb, alpha As Double, width As Double) As Pen
            Return New Pen(c.ToColor(alpha), CSng(Math.Max(0.6, width))) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
        End Function
    End Class

    Private Shared Function Pt(x As Double, y As Double) As PointF
        Return New PointF(CSng(x), CSng(y))
    End Function

    Private Shared Function PolarPt(cx As Double, cy As Double, r As Double, angle As Double) As PointF
        Return New PointF(CSng(cx + r * Math.Cos(angle)), CSng(cy + r * Math.Sin(angle)))
    End Function

    ''' <summary>加亮緩衝 → 圖層（越亮越不透明）。</summary>
    Private Shared Sub FlushLight(cv As Canvas, light As Single())
        For i = 0 To cv.W * cv.H - 1
            Dim r = light(i * 3), g = light(i * 3 + 1), b = light(i * 3 + 2)
            Dim m = Math.Max(r, Math.Max(g, b))
            If m <= 0.003F Then Continue For
            Dim a = Math.Min(1, m)
            Dim k = 1 / Math.Max(1, m)
            cv.Over(i, Math.Min(1, r / a * k), Math.Min(1, g / a * k), Math.Min(1, b / a * k), a)
        Next
    End Sub

    ''' <summary>加亮緩衝 → 圖層，逐色用 1 − e^(−v) 曝光：很亮的地方各色都接近 1，變成白熱的顏色。</summary>
    Private Shared Sub FlushLightHot(cv As Canvas, light As Single())
        For i = 0 To cv.W * cv.H - 1
            Dim r = light(i * 3), g = light(i * 3 + 1), b = light(i * 3 + 2)
            If Math.Max(r, Math.Max(g, b)) <= 0.003F Then Continue For
            Dim er = 1 - CSng(Math.Exp(-1.2 * r)), eg = 1 - CSng(Math.Exp(-1.2 * g)), eb = 1 - CSng(Math.Exp(-1.2 * b))
            Dim a = Math.Max(er, Math.Max(eg, eb))
            cv.Over(i, er / a, eg / a, eb / a, a)
        Next
    End Sub

    ''' <summary>
    ''' 沿筆畫的鋸齒閃電路徑：每隔 0.8～2.4 倍筆寬放一個折點，往旁邊偏移（緩慢的蜿蜒＋隨機的折角），
    ''' 再把每段細分出小鋸齒。起點（嘴部）不偏移，離開起點後才漸漸抖動。amp 為偏移幅度倍數。
    ''' </summary>
    Private Shared Function JaggedPath(p As PointF(), w As Double, seed As Integer, rnd As Random, amp As Double) As List(Of PointF)
        Dim knots As New List(Of PointF) From {p(0)}
        Dim nextKnot = w * (0.8 + rnd.NextDouble() * 1.6)
        Walk(p, Nothing, CSng(Math.Max(0.5, w * 0.2)), Sub(px, py, pp, dx, dy, s, tot)
                                                           If s < nextKnot Then Return
                                                           nextKnot = s + w * (0.8 + rnd.NextDouble() * 1.6)
                                                           Dim ease = Math.Min(1, s / (w * 2))
                                                           Dim wander = (Noise1(CSng(s / (w * 5)), seed) - 0.5) * w * 2.2
                                                           Dim off = (wander + (rnd.NextDouble() * 2 - 1) * w * 0.6) * amp * ease
                                                           knots.Add(Pt(px - dy * off, py + dx * off))
                                                       End Sub)
        Dim last = p(p.Length - 1)
        If knots.Count = 1 OrElse DrawGeometry.Dist(knots(knots.Count - 1), last) > w * 0.3 Then knots.Add(last)
        Dim pts As New List(Of PointF) From {knots(0)}
        For i = 1 To knots.Count - 1
            Dim seg = Bolt(knots(i - 1), knots(i), 0.18 * amp, 2, rnd)
            pts.AddRange(seg.Skip(1))
        Next
        Return pts
    End Function

    ''' <summary>折線閃電：中點隨機位移（depth 次）。</summary>
    Private Shared Function Bolt(a As PointF, b As PointF, rough As Double, depth As Integer, rnd As Random) As List(Of PointF)
        Dim pts As New List(Of PointF) From {a, b}
        For d = 1 To depth
            Dim nextPts As New List(Of PointF) From {pts(0)}
            For i = 1 To pts.Count - 1
                Dim p = pts(i - 1), q = pts(i)
                Dim len = DrawGeometry.Dist(p, q)
                Dim nx = -(q.Y - p.Y) / Math.Max(0.0001, len), ny = (q.X - p.X) / Math.Max(0.0001, len)
                Dim off = (rnd.NextDouble() * 2 - 1) * len * rough
                nextPts.Add(Pt((p.X + q.X) / 2 + nx * off, (p.Y + q.Y) / 2 + ny * off))
                nextPts.Add(q)
            Next
            pts = nextPts
        Next
        Return pts
    End Function

    ''' <summary>隨機符文（3×4 格點上連 3～5 筆）。</summary>
    Private Shared Sub Glyph(g As Graphics, pen As Pen, cx As Double, cy As Double, size As Double, rnd As Random)
        Dim gx = Function(i As Integer) cx + (i Mod 3 - 1) * size * 0.35
        Dim gy = Function(i As Integer) cy + (i \ 3 - 1.5) * size * 0.3
        Dim strokes = 3 + rnd.Next(3)
        Dim last = rnd.Next(12)
        For s = 1 To strokes
            Dim nxt = rnd.Next(12)
            If nxt = last Then nxt = (nxt + 4) Mod 12
            g.DrawLine(pen, Pt(gx(last), gy(last)), Pt(gx(nxt), gy(nxt)))
            last = If(rnd.NextDouble() < 0.6, nxt, rnd.Next(12))
        Next
        If rnd.NextDouble() < 0.4 Then g.DrawEllipse(pen, CSng(cx - size * 0.1), CSng(gy(0) - size * 0.22), CSng(size * 0.2), CSng(size * 0.2))
    End Sub

    ''' <summary>不規則多邊形（碎片、血跡、泥團）。</summary>
    Private Shared Function Blob(cx As Double, cy As Double, r As Double, points As Integer, roughness As Double, rnd As Random, Optional rotation As Double = 0) As PointF()
        Dim pts(points - 1) As PointF
        For i = 0 To points - 1
            Dim a = rotation + i * 2 * Math.PI / points
            pts(i) = PolarPt(cx, cy, r * (1 - roughness / 2 + rnd.NextDouble() * roughness), a)
        Next
        Return pts
    End Function

    ''' <summary>需要多留的邊（左、上、右、下），以筆寬與筆畫大小為準。</summary>
    Private Shared Function FxPad(fx As FxKind, w As Single, extent As Single) As (L As Single, T As Single, R As Single, B As Single)
        Dim all = Function(k As Single) (k, k, k, k)
        Select Case fx
            Case FxKind.Fire : Return (w * 0.6F, w * 2.6F, w * 0.6F, 0)
            Case FxKind.Smoke, FxKind.BlackSmoke, FxKind.HeatHaze : Return (w * 2.5F, w * 4.5F, w * 2.5F, w * 0.5F)
            Case FxKind.Embers, FxKind.DustScatter : Return (w * 2, w * 4, w * 2, w)
            Case FxKind.Blood : Return (0, 0, 0, w * 7)
            Case FxKind.SparkSpray : Return all(w * 4.5F)
            Case FxKind.Explosion : Return all(w * 3)
            Case FxKind.FocusLines : Return all(100000)
            Case FxKind.RadialLines, FxKind.ShockWave : Return all(extent * 1.2F + w * 8)
            Case FxKind.BlackHole, FxKind.ExplosionLight, FxKind.Hud, FxKind.LensFlare : Return all(extent * 1.2F + w * 6)
            Case FxKind.ComicBurst, FxKind.MagicCircle, FxKind.Vortex : Return all(Math.Max(w * 3, extent * 0.7F))
            Case FxKind.Debris, FxKind.BloodSplatter, FxKind.Splash, FxKind.Flash : Return all(w * 3.2F)
            Case FxKind.SpeedLines, FxKind.ComicSpeed, FxKind.Trail : Return all(w * 5)
            Case FxKind.AtomicBreath : Return all(w * 7)
            Case FxKind.GravityBeam : Return all(w * 10)
            Case FxKind.SpiralHeatRay : Return all(w * 7)
            Case FxKind.SpeciumRay : Return all(w * 5)
            Case FxKind.ZeperionRay : Return all(w * 7)
            Case FxKind.M87Ray : Return all(w * 4)
            Case Else : Return all(w * 2)
        End Select
    End Function

    '=====================================================================
    ' 效果
    '=====================================================================

    Private Shared Sub RenderFx(cv As Canvas, figs As List(Of DrawGeometry.Figure), layer As DrawLayer, w As Single, rnd As Random, imageH As Integer)
        Dim x As New FxCtx With {.Cv = cv, .Light = New Single(cv.W * cv.H * 3 - 1) {}, .W = w, .Flow = Math.Max(0.05, layer.Flow / 100.0),
                                 .Rnd = rnd, .ImageH = imageH, .Seed = layer.Seed, .Tint = Rgb.FromColor(Color.FromArgb(layer.StrokeColorArgb))}
        For Each f In figs
            x.Paths.Add(If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points))
            x.Pres.Add(If(f.Closed AndAlso f.Pressure IsNot Nothing, f.Pressure.Concat({f.Pressure(0)}).ToArray(), f.Pressure))
        Next
        Dim t = x.Tint
        Dim fl = x.Flow
        Select Case layer.Fx
            '---------------- 光／能量 ----------------
            Case FxKind.Stars
                x.Every(w * 0.5, Sub(s)
                                     If x.R() < 0.5 Then Sparkle(cv, CSng(s.X + x.RS() * w * 0.6), CSng(s.Y + x.RS() * w * 0.6), CSng(w * (0.35 + x.R() * 1.1)),
                                                                CSng(t.R), CSng(t.G), CSng(t.B), CSng(x.R() * 0.6), CSng(fl))
                                     If x.R() < 0.6 Then x.Puff(s.X + x.RS() * w * 0.8, s.Y + x.RS() * w * 0.8, Math.Max(0.8, w * 0.09), White, 0.9 * fl)
                                 End Sub)
            Case FxKind.Flash
                x.Paint(Sub(g)
                            x.Every(w * 2.5, Sub(s)
                                                 Dim cx = s.X + x.RS() * w * 0.3, cy = s.Y + x.RS() * w * 0.3
                                                 x.Glow(cx, cy, w * 1.7, x.Mix(0.4), 0.55 * fl)
                                                 x.Glow(cx, cy, w * 0.45, White, fl)
                                                 For k = 0 To 5 + x.Rnd.Next(4)
                                                     Dim a = x.R() * Math.PI * 2, len = w * (1.2 + x.R() * 2.6), th = w * 0.05
                                                     Dim nx = -Math.Sin(a) * th, ny = Math.Cos(a) * th
                                                     Using br As New SolidBrush(x.Mix(0.7).ToColor(0.85))
                                                         g.FillPolygon(br, {Pt(cx + nx, cy + ny), PolarPt(cx, cy, len, a), Pt(cx - nx, cy - ny)})
                                                     End Using
                                                 Next
                                             End Sub)
                        End Sub, glowRadius:=w * 0.25, glowAmount:=0.6)
            Case FxKind.LightDots
                x.Every(w * 0.4, Sub(s)
                                     If x.R() > 0.7 Then Return
                                     Dim c = Rgb.Lerp(t, White, x.R() * 0.45)
                                     x.Bokeh(s.X + x.RS() * w, s.Y + x.RS() * w, w * (0.22 + x.R() * 0.6), c, (0.22 + x.R() * 0.25) * fl)
                                 End Sub)
            Case FxKind.Halo
                x.Every(w * 0.25, Sub(s)
                                      x.Glow(s.X, s.Y, w * 1.3, t, 0.06 * fl)
                                      x.Glow(s.X, s.Y, w * 0.45, x.Mix(0.5), 0.09 * fl)
                                  End Sub)
            Case FxKind.LightRays
                For Each p In x.Paths
                    x.GlowLine(p, w * 0.12, w * 0.8, t, fl, 0.5 * fl)
                Next
            Case FxKind.LensFlare
                For k = 0 To x.Paths.Count - 1
                    Dim p = x.Paths(k)
                    Dim src = p(0), dst = p(p.Length - 1)
                    If DrawGeometry.Dist(src, dst) < w Then dst = Pt(src.X + w * 6, src.Y + w * 3)
                    x.Glow(src.X, src.Y, w * 2.4, x.Mix(0.3), 0.6 * fl)
                    x.Glow(src.X, src.Y, w * 0.6, White, fl)
                    Dim srcPt = src, dstPt = dst
                    x.Paint(Sub(g)
                                For r = 0 To 11
                                    Dim a = r * Math.PI / 6 + 0.2, len = If(r Mod 3 = 0, w * 6, w * 2.2)
                                    Using pen = x.Pen(x.Mix(0.6), 0.7, w * 0.04)
                                        g.DrawLine(pen, srcPt, PolarPt(srcPt.X, srcPt.Y, len, a))
                                    End Using
                                Next
                                Dim ghosts = {(0.25, 0.25, New Rgb(0.4, 1, 0.6)), (0.42, 0.55, New Rgb(1, 0.6, 0.3)), (0.58, 0.18, New Rgb(0.6, 0.5, 1)),
                                              (0.78, 0.8, New Rgb(0.4, 0.8, 1)), (1.0, 0.35, New Rgb(1, 0.4, 0.8)), (1.32, 1.1, New Rgb(1, 0.8, 0.4))}
                                For Each gh In ghosts
                                    Dim cx = srcPt.X + (dstPt.X - srcPt.X) * gh.Item1, cy = srcPt.Y + (dstPt.Y - srcPt.Y) * gh.Item1
                                    Dim r = w * gh.Item2
                                    Using br As New SolidBrush(Rgb.Lerp(gh.Item3, t, 0.3).ToColor(0.16)), pen = x.Pen(gh.Item3, 0.32, Math.Max(1, w * 0.04))
                                        If gh.Item2 > 0.7 Then
                                            Dim hex = Enumerable.Range(0, 6).Select(Function(i) PolarPt(cx, cy, r, i * Math.PI / 3 + 0.3)).ToArray()
                                            g.FillPolygon(br, hex)
                                            g.DrawPolygon(pen, hex)
                                        Else
                                            g.FillEllipse(br, CSng(cx - r), CSng(cy - r), CSng(r * 2), CSng(r * 2))
                                            g.DrawEllipse(pen, CSng(cx - r), CSng(cy - r), CSng(r * 2), CSng(r * 2))
                                        End If
                                    End Using
                                Next
                                Using ring = x.Pen(x.Mix(0.5), 0.18, Math.Max(1, w * 0.06))
                                    g.DrawEllipse(ring, CSng(srcPt.X - w * 4), CSng(srcPt.Y - w * 4), CSng(w * 8), CSng(w * 8))
                                End Using
                            End Sub, glowRadius:=w * 0.15, glowAmount:=0.4)
                Next
            Case FxKind.Beam
                x.Every(w * 0.2, Sub(s)
                                     x.Glow(s.X, s.Y, w * 1.05, t, 0.07 * fl)
                                     x.Glow(s.X, s.Y, w * 0.35, x.Mix(0.6), 0.12 * fl)
                                     If x.R() < 0.25 Then x.Glow(s.X + x.RS() * w * 0.9, s.Y + x.RS() * w * 0.9, Math.Max(0.8, w * 0.05), White, 0.8)
                                 End Sub)
            Case FxKind.EnergyParticles
                x.Every(w * 0.15, Sub(s)
                                      x.Glow(s.X, s.Y, w * 0.5, t, 0.025 * fl)
                                      If x.R() > 0.55 Then Return
                                      Dim off = x.RS() * w * 0.8, px = s.X - s.Dy * off, py = s.Y + s.Dx * off
                                      Dim r = w * (0.05 + x.R() * 0.07)
                                      For k = 0 To 4
                                          x.Glow(px - s.Dx * r * k * 1.6, py - s.Dy * r * k * 1.6, r * (1.8 - k * 0.3), If(k = 0, x.Mix(0.6), t), (0.8 - k * 0.15) * fl)
                                      Next
                                  End Sub)
            Case FxKind.MagicAura
                x.Every(w * 0.1, Sub(s)
                                     Dim phase = s.S / (w * 1.5) * Math.PI * 2
                                     For j = 0 To 2
                                         Dim off = Math.Sin(phase + j * 2 * Math.PI / 3) * w * 0.6
                                         Dim bright = 0.5 + 0.5 * Math.Cos(phase + j * 2 * Math.PI / 3)
                                         x.Glow(s.X - s.Dy * off, s.Y + s.Dx * off, w * 0.09, x.Mix(0.6), 0.4 * bright * fl)
                                         x.Glow(s.X - s.Dy * off, s.Y + s.Dx * off, w * 0.32, t, 0.05 * bright * fl)
                                     Next
                                     If x.R() < 0.05 Then Sparkle(cv, CSng(s.X + x.RS() * w), CSng(s.Y + x.RS() * w), CSng(w * 0.35), CSng(t.R), CSng(t.G), CSng(t.B), 0, CSng(fl))
                                 End Sub)
            Case FxKind.Arc, FxKind.Thunder
                Dim big = layer.Fx = FxKind.Thunder
                For Each p In x.Paths
                    Dim nodes As New List(Of PointF)()
                    Walk(p, Nothing, CSng(w * 2.2), Sub(px, py, pp, dx, dy, s, tt) nodes.Add(New PointF(px, py)))
                    If nodes.Count < 2 Then nodes = {p(0), Pt(p(0).X + w * 3, p(0).Y + w * 2)}.ToList()
                    Dim main As New List(Of PointF)()
                    For i = 1 To nodes.Count - 1
                        Dim seg = Bolt(nodes(i - 1), nodes(i), If(big, 0.42, 0.32), 4, rnd)
                        If main.Count > 0 Then seg.RemoveAt(0)
                        main.AddRange(seg)
                    Next
                    x.GlowLine(main, w * If(big, 0.16, 0.08), w * If(big, 1.4, 0.6), t, 1.1 * fl, 0.55 * fl)
                    Dim branches = If(big, 5 + rnd.Next(4), 1 + rnd.Next(2))
                    For bIdx = 1 To branches
                        Dim i0 = rnd.Next(Math.Max(1, main.Count - 1))
                        Dim a0 = main(i0)
                        Dim ang = Math.Atan2(main(Math.Min(main.Count - 1, i0 + 1)).Y - a0.Y, main(Math.Min(main.Count - 1, i0 + 1)).X - a0.X) +
                                  If(rnd.NextDouble() < 0.5, -1, 1) * (0.4 + rnd.NextDouble() * 0.6)
                        Dim len = w * (If(big, 2, 1.2) + rnd.NextDouble() * If(big, 3.5, 1.5))
                        Dim br = Bolt(a0, PolarPt(a0.X, a0.Y, len, ang), 0.35, 3, rnd)
                        x.GlowLine(br, w * If(big, 0.07, 0.04), w * If(big, 0.7, 0.3), t, 0.9 * fl, 0.45 * fl)
                    Next
                Next
            Case FxKind.Sparks
                Dim fire = Rgb.Lerp(New Rgb(1, 0.72, 0.28), t, 0.3)
                x.Every(w * 0.3, Sub(s)
                                     If x.R() > 0.6 Then Return
                                     For k = 0 To 1
                                         Dim a = x.R() * Math.PI * 2, len = w * (0.3 + x.R() * 1.0)
                                         Dim pts = Enumerable.Range(0, 6).Select(Function(i) Pt(s.X + Math.Cos(a) * len * i / 5, s.Y + Math.Sin(a) * len * i / 5 + len * 0.35 * (i / 5.0) ^ 2)).ToList()
                                         x.GlowLine(pts, w * 0.035, w * 0.16, fire, fl, 0.45 * fl)
                                     Next
                                 End Sub)
            Case FxKind.ExplosionLight
                For Each c In x.Centers()
                    Dim r0 = Math.Max(w * 2, c.Radius * 0.7)
                    x.Glow(c.X, c.Y, r0 * 1.7, Rgb.Lerp(New Rgb(1, 0.55, 0.15), t, 0.3), 0.55 * fl)
                    x.Glow(c.X, c.Y, r0 * 0.7, New Rgb(1, 0.92, 0.75), 0.9 * fl)
                    x.Glow(c.X, c.Y, r0 * 0.28, White, fl)
                    Dim cc = c
                    x.Paint(Sub(g)
                                For k = 0 To 17
                                    Dim a = k * Math.PI / 9 + x.RS() * 0.12, len = r0 * (1.4 + x.R() * 1.3), th = r0 * 0.05
                                    Using br As New SolidBrush(New Rgb(1, 0.88, 0.6).ToColor(0.55))
                                        g.FillPolygon(br, {PolarPt(cc.X, cc.Y, r0 * 0.3, a - 0.05), PolarPt(cc.X, cc.Y, len, a), PolarPt(cc.X, cc.Y, r0 * 0.3, a + 0.05)})
                                    End Using
                                Next
                            End Sub, glowRadius:=w * 0.3, glowColor:=New Rgb(1, 0.6, 0.2), glowAmount:=0.5)
                Next

            '---------------- 火／煙／爆炸 ----------------
            Case FxKind.Fire
                x.Every(w * 0.12, Sub(s)
                                      For k = 0 To 3
                                          Dim tt = Math.Pow(x.R(), 1.6)
                                          Dim px = s.X + x.RS() * 0.35 * w * (1 - tt * 0.5)
                                          Dim py = s.Y - tt * w * 2.4
                                          Dim r = w * (0.42 - 0.26 * tt) * (0.75 + x.R() * 0.5) * (0.6 + 0.4 * s.P)
                                          Dim fc = FireColor(CSng(tt))
                                          Dim c = Rgb.Lerp(New Rgb(fc.R, fc.G, fc.B), t, 0.15)
                                          x.Glow(px, py, r, c, (0.3 - 0.2 * tt) * fl, 2.2)
                                      Next
                                  End Sub)
            Case FxKind.Embers
                x.Every(w * 0.3, Sub(s)
                                     For k = 0 To 1
                                         Dim tt = Math.Pow(x.R(), 1.2)
                                         Dim px = s.X + x.RS() * w * (0.5 + tt * 1.5), py = s.Y - tt * w * 3.5
                                         Dim r = w * (0.04 + x.R() * 0.06)
                                         Dim c = New Rgb(1, 0.5 + x.R() * 0.3, 0.12)
                                         x.Glow(px, py, r * 3, c, 0.35 * (1 - tt * 0.6) * fl)
                                         x.Glow(px, py, r, New Rgb(1, 0.9, 0.6), fl)
                                         x.Glow(px, py + r * 2, r * 0.7, c, 0.4 * fl)
                                     Next
                                 End Sub)
            Case FxKind.Smoke, FxKind.BlackSmoke
                Dim dark = layer.Fx = FxKind.BlackSmoke
                Dim sc = If(dark, Rgb.Lerp(New Rgb(0.11, 0.1, 0.1), t, 0.2), Rgb.Lerp(t, New Rgb(0.72, 0.72, 0.74), 0.55))
                x.Every(w * 0.3, Sub(s)
                                     For k = 0 To 1
                                         Dim tt = x.R()
                                         Dim px = s.X + x.RS() * w * 0.5 * (1 + tt * 2.2), py = s.Y - tt * w * If(dark, 4, 3.2)
                                         Dim r = w * (0.5 + tt * If(dark, 1.6, 1.3)) * (0.7 + x.R() * 0.6)
                                         x.Puff(px, py, r, sc, If(dark, 0.13, 0.08) * (1 - tt * 0.5) * fl)
                                     Next
                                 End Sub)
            Case FxKind.Explosion
                For Each st In x.Stamps(w * 4)
                    For k = 0 To 17
                        Dim a = x.R() * Math.PI * 2, d = w * (1.3 + x.R() * 1.0)
                        x.Puff(st.X + Math.Cos(a) * d, st.Y + Math.Sin(a) * d * 0.8 - w * 0.4, w * (0.5 + x.R() * 0.5), New Rgb(0.18, 0.15, 0.13), 0.12 * fl)
                    Next
                    For k = 0 To 44
                        Dim a = x.R() * Math.PI * 2, d = Math.Pow(x.R(), 0.7) * w * 1.6
                        Dim fc = FireColor(CSng(Math.Min(1, d / (w * 1.8))))
                        x.Glow(st.X + Math.Cos(a) * d, st.Y + Math.Sin(a) * d, w * (0.35 + x.R() * 0.4) * (1 - d / (w * 3.2)),
                               Rgb.Lerp(New Rgb(fc.R, fc.G, fc.B), t, 0.12), 0.35 * fl)
                    Next
                    x.Glow(st.X, st.Y, w * 0.7, New Rgb(1, 0.95, 0.8), 0.8 * fl)
                    For k = 0 To 9
                        Dim a = x.R() * Math.PI * 2, d = w * (1.8 + x.R() * 1.0)
                        x.Puff(st.X + Math.Cos(a) * d, st.Y + Math.Sin(a) * d, Math.Max(0.8, w * 0.07), New Rgb(0.12, 0.1, 0.09), 0.9)
                    Next
                Next
            Case FxKind.Debris
                Dim baseC = Rgb.Lerp(New Rgb(0.26, 0.23, 0.2), t, 0.35)
                x.Paint(Sub(g)
                            x.Every(w * 1.2, Sub(s)
                                                 For k = 0 To 2 + x.Rnd.Next(3)
                                                     Dim a = x.R() * Math.PI * 2, d = w * (0.5 + x.R() * 2.5), size = w * (0.08 + x.R() * 0.28)
                                                     Dim cx = s.X + Math.Cos(a) * d, cy = s.Y + Math.Sin(a) * d
                                                     Using streak = x.Pen(New Rgb(0.4, 0.38, 0.35), 0.25, size * 0.5)
                                                         g.DrawLine(streak, Pt(cx, cy), Pt(cx - Math.Cos(a) * size * 3, cy - Math.Sin(a) * size * 3))
                                                     End Using
                                                     Dim shard = Blob(cx, cy, size, 3 + x.Rnd.Next(3), 0.9, x.Rnd, x.R() * 6)
                                                     Using br As New SolidBrush(baseC.Times(0.8 + x.R() * 0.5).ToColor(1)), edge = x.Pen(baseC.Times(1.6), 0.8, Math.Max(0.6, size * 0.12))
                                                         g.FillPolygon(br, shard)
                                                         g.DrawLine(edge, shard(0), shard(1))
                                                     End Using
                                                 Next
                                             End Sub)
                        End Sub)
            Case FxKind.Ash
                x.Paint(Sub(g)
                            x.Every(w * 0.1, Sub(s)
                                                 Dim cx = s.X + x.RS() * w * 1.5, cy = s.Y + x.RS() * w * 1.5 - x.R() * w
                                                 Dim size = w * (0.05 + x.R() * 0.13)
                                                 Dim gray = 0.5 + x.R() * 0.35
                                                 Using br As New SolidBrush(Rgb.Lerp(New Rgb(gray, gray, gray * 0.97), t, 0.2).ToColor(0.75))
                                                     g.FillPolygon(br, Blob(cx, cy, size, 4, 0.8, x.Rnd, x.R() * 6))
                                                 End Using
                                                 If x.R() < 0.12 Then x.Glow(cx, cy, size * 1.6, New Rgb(1, 0.45, 0.1), 0.6 * fl)
                                             End Sub)
                        End Sub)
            Case FxKind.SparkSpray
                x.Every(w * 0.8, Sub(s)
                                     For k = 0 To 5 + x.Rnd.Next(5)
                                         Dim a = -Math.PI / 2 + x.RS() * 1.25, v = w * (1.5 + x.R() * 2.5)
                                         Dim pts = Enumerable.Range(0, 9).Select(Function(i) Pt(s.X + Math.Cos(a) * v * i / 8, s.Y + Math.Sin(a) * v * i / 8 + w * 2.4 * (i / 8.0) ^ 2)).ToList()
                                         Dim fc = Rgb.Lerp(New Rgb(1, 0.65 + x.R() * 0.3, 0.25), t, 0.15)
                                         x.GlowLine(pts, w * 0.035, w * 0.14, fc, fl, 0.4 * fl)
                                         x.Glow(pts.Last().X, pts.Last().Y, w * 0.08, White, fl)
                                     Next
                                 End Sub)
            Case FxKind.HeatHaze
                x.Paint(Sub(g)
                            x.Every(w * 0.6, Sub(s)
                                                 Dim phase = x.R() * 6
                                                 Dim pts = Enumerable.Range(0, 12).Select(Function(k) Pt(s.X + Math.Sin(k * 0.9 + phase) * w * 0.15 * (1 + k / 6.0), s.Y - k * w * 0.28)).ToArray()
                                                 Using pen = x.Pen(x.Mix(0.6), 0.14, w * 0.12)
                                                     g.DrawCurve(pen, pts)
                                                 End Using
                                                 x.Glow(s.X, s.Y, w * 0.5, New Rgb(1, 0.6, 0.3), 0.025 * fl)
                                             End Sub)
                        End Sub, opacity:=fl)
            Case FxKind.BurnEdge
                Dim cov = Coverage(x, w * 1.2F, 0.9F)
                For i = 0 To cov.Length - 1
                    Dim a = cov(i)
                    If a <= 0.01F Then Continue For
                    Dim px = i Mod cv.W + cv.OX, py = i \ cv.W + cv.OY
                    Dim v = a + (Fbm(px / CSng(w * 0.18), py / CSng(w * 0.18), layer.Seed, 3) - 0.5F) * 0.7F
                    If v > 0.72F Then
                        cv.Over(i, 0.07F, 0.05F, 0.04F, 1)
                    ElseIf v > 0.55F Then
                        cv.Over(i, 0.3F, 0.16F, 0.07F, 1)
                    ElseIf v > 0.42F Then
                        Dim gl = (v - 0.42F) / 0.13F
                        x.Light(i * 3) += 1.0F * (1 - gl * 0.5F) : x.Light(i * 3 + 1) += 0.45F * (1 - gl * 0.5F) : x.Light(i * 3 + 2) += 0.08F
                    End If
                Next

            '---------------- 液體／自然 ----------------
            Case FxKind.Splash
                x.Paint(Sub(g)
                            x.Every(w * 0.8, Sub(s)
                                                 For k = 0 To 7 + x.Rnd.Next(7)
                                                     Dim a = -Math.PI / 2 + x.RS() * 1.9, d = w * (0.3 + x.R() * 1.5), r = w * (0.05 + x.R() * 0.17)
                                                     Dim cx = s.X + Math.Cos(a) * d, cy = s.Y + Math.Sin(a) * d
                                                     Dim st = g.Save()
                                                     g.TranslateTransform(CSng(cx), CSng(cy))
                                                     g.RotateTransform(CSng(a * 180 / Math.PI))
                                                     Using br As New SolidBrush(t.ToColor(0.5)), rim = x.Pen(t.Times(0.6), 0.45, Math.Max(0.6, r * 0.15)),
                                                          hl As New SolidBrush(White.ToColor(0.85))
                                                         g.FillEllipse(br, CSng(-r * 1.5), CSng(-r), CSng(r * 3), CSng(r * 2))
                                                         g.DrawEllipse(rim, CSng(-r * 1.5), CSng(-r), CSng(r * 3), CSng(r * 2))
                                                         g.FillEllipse(hl, CSng(-r * 0.6), CSng(-r * 0.6), CSng(r * 0.6), CSng(r * 0.45))
                                                     End Using
                                                     g.Restore(st)
                                                 Next
                                                 For k = 0 To 4
                                                     Dim a = -Math.PI / 2 + x.RS() * 0.9, len = w * (0.4 + x.R() * 0.6), th = w * 0.06
                                                     Using br As New SolidBrush(t.ToColor(0.45))
                                                         g.FillPolygon(br, {Pt(s.X - th, s.Y), PolarPt(s.X, s.Y, len, a), Pt(s.X + th, s.Y)})
                                                     End Using
                                                 Next
                                             End Sub)
                        End Sub)
            Case FxKind.WaterDrops
                x.Paint(Sub(g)
                            x.Every(w * 0.7, Sub(s)
                                                 If x.R() > 0.8 Then Return
                                                 Dim r = w * (0.1 + x.R() * 0.35)
                                                 Dim cx = s.X + x.RS() * w * 0.6, cy = s.Y + x.RS() * w * 0.6
                                                 Using shadow As New SolidBrush(Color.FromArgb(55, 0, 0, 0)), body As New SolidBrush(x.Mix(0.3).ToColor(0.28)),
                                                      rim = x.Pen(t.Times(0.45), 0.55, Math.Max(0.7, r * 0.12)), hl As New SolidBrush(White.ToColor(0.9)),
                                                      glint As New SolidBrush(White.ToColor(0.45))
                                                     g.FillEllipse(shadow, CSng(cx - r + r * 0.15), CSng(cy - r * 1.1 + r * 0.2), CSng(r * 2), CSng(r * 2.2))
                                                     g.FillEllipse(body, CSng(cx - r), CSng(cy - r * 1.1), CSng(r * 2), CSng(r * 2.2))
                                                     g.DrawArc(rim, CSng(cx - r), CSng(cy - r * 1.1), CSng(r * 2), CSng(r * 2.2), 200, 140)
                                                     g.FillEllipse(hl, CSng(cx - r * 0.55), CSng(cy - r * 0.75), CSng(r * 0.45), CSng(r * 0.32))
                                                     g.FillEllipse(glint, CSng(cx + r * 0.2), CSng(cy + r * 0.4), CSng(r * 0.4), CSng(r * 0.25))
                                                 End Using
                                             End Sub)
                        End Sub)
            Case FxKind.Rain
                x.Paint(Sub(g)
                            x.Every(w * 0.15, Sub(s)
                                                  For k = 0 To 1
                                                      Dim cx = s.X + x.RS() * w * 1.5, cy = s.Y + x.RS() * w * 1.5, len = w * (0.6 + x.R() * 1.0)
                                                      Using pen = x.Pen(x.Mix(0.4), 0.35 + x.R() * 0.3, w * (0.025 + x.R() * 0.035))
                                                          g.DrawLine(pen, Pt(cx, cy), Pt(cx - len * 0.25, cy + len))
                                                      End Using
                                                  Next
                                              End Sub)
                        End Sub, opacity:=fl)
            Case FxKind.BloodSplatter
                x.Paint(Sub(g)
                            x.Every(w * 1.6, Sub(s)
                                                 Using br As New SolidBrush(t.ToColor(0.95)), gloss As New SolidBrush(White.ToColor(0.22))
                                                     Dim r0 = w * (0.4 + x.R() * 0.3)
                                                     g.FillClosedCurve(br, Blob(s.X, s.Y, r0, 18, 0.55, x.Rnd))
                                                     g.FillEllipse(gloss, CSng(s.X - r0 * 0.4), CSng(s.Y - r0 * 0.45), CSng(r0 * 0.5), CSng(r0 * 0.3))
                                                     For k = 0 To 14 + x.Rnd.Next(10)
                                                         Dim a = x.R() * Math.PI * 2, d = w * (0.55 + Math.Pow(x.R(), 0.7) * 2.2)
                                                         Dim r = w * 0.16 * (1.1 - d / (w * 3)) * (0.4 + x.R() * 0.8)
                                                         If r < 0.4 Then Continue For
                                                         Dim st = g.Save()
                                                         g.TranslateTransform(CSng(s.X + Math.Cos(a) * d), CSng(s.Y + Math.Sin(a) * d))
                                                         g.RotateTransform(CSng(a * 180 / Math.PI))
                                                         g.FillEllipse(br, CSng(-r * 1.8), CSng(-r), CSng(r * 2.6), CSng(r * 2))
                                                         g.Restore(st)
                                                     Next
                                                 End Using
                                             End Sub)
                        End Sub)
            Case FxKind.Blood
                Dim sp = Spec(BrushKind.FX, FxKind.Blood)
                Dim cov(cv.W * cv.H - 1) As Single
                For k = 0 To x.Paths.Count - 1
                    Stamp(cov, cv, x.Paths(k), x.Pres(k), w, sp, CSng(fl), Math.Max(sp.Edge, layer.Softness / 100.0F))
                Next
                Drips(cov, cv, figs, w, rnd)
                Shade(cv, cov, Nothing, layer, Color.FromArgb(layer.StrokeColorArgb), w, imageH, isFill:=False)
            Case FxKind.Mud, FxKind.OilStain, FxKind.DirtyWater
                Dim cov(cv.W * cv.H - 1) As Single
                x.Every(w * 0.18, Sub(s)
                                      Dab(cov, Nothing, 0, cv, CSng(s.X + x.RS() * w * 0.12), CSng(s.Y + x.RS() * w * 0.12), CSng(w * 0.5 * (0.6 + x.R() * 0.7)), 0.35F, 1, False, TipKind.Round)
                                      If layer.Fx = FxKind.Mud AndAlso x.R() < 0.08 Then
                                          Dim a = x.R() * Math.PI * 2, d = w * (0.6 + x.R())
                                          Dab(cov, Nothing, 0, cv, CSng(s.X + Math.Cos(a) * d), CSng(s.Y + Math.Sin(a) * d), CSng(w * (0.06 + x.R() * 0.12)), 0.1F, 1, False, TipKind.Round)
                                      End If
                                  End Sub)
                ' 模糊後取門檻：邊緣圓滑、不規則，看不出一個個筆印。
                Dim soft = BoxBlur(BoxBlur(cov, cv.W, cv.H, CInt(Math.Max(1, w * 0.2))), cv.W, cv.H, CInt(Math.Max(1, w * 0.2)))
                For i = 0 To cov.Length - 1
                    Dim gx0 = i Mod cv.W + cv.OX, gy0 = i \ cv.W + cv.OY
                    cov(i) = SmoothStep(0.3F, 0.5F, soft(i) + (Fbm(gx0 / CSng(w * 0.3), gy0 / CSng(w * 0.3), layer.Seed + 5, 3) - 0.5F) * 0.3F)
                Next
                Dim blurred = BoxBlur(cov, cv.W, cv.H, CInt(Math.Max(1, w * 0.15)))
                For i = 0 To cov.Length - 1
                    Dim a = cov(i)
                    If a <= 0.01F Then Continue For
                    Dim px = i Mod cv.W, py = i \ cv.W
                    Dim gx = px + cv.OX, gy = py + cv.OY
                    Dim n = Fbm(gx / CSng(w * 0.35), gy / CSng(w * 0.35), layer.Seed, 3)
                    Dim e = Sample(cov, cv, px - 1, py - 1) - Sample(cov, cv, px + 1, py + 1)
                    Select Case layer.Fx
                        Case FxKind.Mud
                            Dim c = Rgb.Lerp(New Rgb(0.2, 0.13, 0.07), Rgb.Lerp(New Rgb(0.42, 0.3, 0.17), t, 0.35), n)
                            Dim lum = 1 + e * 1.6 + If(e > 0.12F, (e - 0.12F) * 3, 0)
                            cv.Over(i, CSng(Math.Min(1, c.R * lum)), CSng(Math.Min(1, c.G * lum)), CSng(Math.Min(1, c.B * lum)), a)
                        Case FxKind.OilStain
                            Dim hue = (n * 2.5 + a * 1.5) Mod 1
                            Dim rb = HueColor(hue)
                            Dim c = Rgb.Lerp(Rgb.Lerp(New Rgb(0.06, 0.05, 0.05), t, 0.3), rb, 0.14 * SmoothStep(0.2F, 0.9F, CSng(blurred(i))) * (0.5 + n))
                            cv.Over(i, CSng(c.R), CSng(c.G), CSng(c.B), a * 0.68F)
                        Case Else
                            Dim edge = Clamp01((a - blurred(i)) * 3)
                            Dim c = Rgb.Lerp(New Rgb(0.32, 0.31, 0.18), t, 0.4).Times(0.85 + n * 0.3)
                            cv.Over(i, CSng(c.R), CSng(c.G), CSng(c.B), Clamp01(a * 0.42F + edge * 0.35F))
                            If Hash(gx \ 3, gy \ 3, layer.Seed) > 0.985F Then cv.Over(i, 0.12F, 0.1F, 0.06F, 0.8F)
                    End Select
                Next
            Case FxKind.Snow
                x.Paint(Sub(g)
                            x.Every(w * 0.2, Sub(s)
                                                 If x.R() > 0.6 Then Return
                                                 Dim cx = s.X + x.RS() * w * 1.5, cy = s.Y + x.RS() * w * 1.5
                                                 If x.R() < 0.25 Then
                                                     Dim size = w * (0.15 + x.R() * 0.2), rot = x.R()
                                                     Using pen = x.Pen(x.Mix(0.85), 0.92, Math.Max(0.8, size * 0.12))
                                                         For k = 0 To 5
                                                             Dim a = rot + k * Math.PI / 3
                                                             Dim tip = PolarPt(cx, cy, size, a)
                                                             g.DrawLine(pen, Pt(cx, cy), tip)
                                                             Dim mid = PolarPt(cx, cy, size * 0.55, a)
                                                             g.DrawLine(pen, mid, PolarPt(mid.X, mid.Y, size * 0.3, a + 0.7))
                                                             g.DrawLine(pen, mid, PolarPt(mid.X, mid.Y, size * 0.3, a - 0.7))
                                                         Next
                                                     End Using
                                                 Else
                                                     Dim r = w * (0.03 + x.R() * 0.07)
                                                     Using br As New SolidBrush(x.Mix(0.9).ToColor(0.85))
                                                         g.FillEllipse(br, CSng(cx - r), CSng(cy - r), CSng(r * 2), CSng(r * 2))
                                                     End Using
                                                 End If
                                             End Sub)
                        End Sub, glowRadius:=Math.Max(1, w * 0.06), glowColor:=White, glowAmount:=0.35, opacity:=fl)
            Case FxKind.IceCrystal
                x.Paint(Sub(g)
                            x.Every(w * 0.9, Sub(s)
                                                 For k = 0 To 2 + x.Rnd.Next(3)
                                                     Dim a = Math.Atan2(s.Dx, -s.Dy) + x.RS() * 1.2 + If(x.R() < 0.5, Math.PI, 0)
                                                     Dim len = w * (0.6 + x.R() * 1.2), wid = w * (0.1 + x.R() * 0.14)
                                                     Dim ux = Math.Cos(a), uy = Math.Sin(a), nx = -uy * wid, ny = ux * wid
                                                     Dim p0 = Pt(s.X + nx * 0.6, s.Y + ny * 0.6), p5 = Pt(s.X - nx * 0.6, s.Y - ny * 0.6)
                                                     Dim p1 = Pt(s.X + ux * len * 0.15 + nx, s.Y + uy * len * 0.15 + ny)
                                                     Dim p2 = Pt(s.X + ux * len * 0.75 + nx, s.Y + uy * len * 0.75 + ny)
                                                     Dim p3 = Pt(s.X + ux * len, s.Y + uy * len)
                                                     Dim p4 = Pt(s.X + ux * len * 0.75 - nx, s.Y + uy * len * 0.75 - ny)
                                                     Dim p6 = Pt(s.X + ux * len * 0.15 - nx, s.Y + uy * len * 0.15 - ny)
                                                     Dim poly = {p0, p1, p2, p3, p4, p6, p5}
                                                     Using br As New SolidBrush(x.Mix(0.45).ToColor(0.45)), edge = x.Pen(White, 0.85, Math.Max(0.7, wid * 0.12)),
                                                          facet = x.Pen(White, 0.4, Math.Max(0.5, wid * 0.08))
                                                         g.FillPolygon(br, poly)
                                                         g.DrawPolygon(edge, poly)
                                                         g.DrawLine(facet, Pt(s.X, s.Y), p3)
                                                     End Using
                                                 Next
                                             End Sub)
                        End Sub, glowRadius:=w * 0.25, glowAmount:=0.45)

            '---------------- 動態效果 ----------------
            Case FxKind.SpeedLines, FxKind.ComicSpeed
                Dim comic = layer.Fx = FxKind.ComicSpeed
                x.Paint(Sub(g)
                            x.Every(w * If(comic, 0.18, 0.25), Sub(s)
                                                                   Dim off = x.RS() * w * If(comic, 1.6, 1.2)
                                                                   Dim hx = s.X - s.Dy * off, hy = s.Y + s.Dx * off
                                                                   Dim len = w * (1.5 + x.R() * If(comic, 4.5, 3.5)), th = w * If(comic, 0.05 + x.R() * 0.05, 0.03 + x.R() * 0.03)
                                                                   Dim px = -s.Dy * th, py = s.Dx * th
                                                                   Dim poly As PointF()
                                                                   If comic Then
                                                                       poly = {Pt(hx + s.Dx * len / 2, hy + s.Dy * len / 2), Pt(hx + px, hy + py), Pt(hx - s.Dx * len / 2, hy - s.Dy * len / 2), Pt(hx - px, hy - py)}
                                                                   Else
                                                                       poly = {Pt(hx + px, hy + py), Pt(hx - s.Dx * len, hy - s.Dy * len), Pt(hx - px, hy - py)}
                                                                   End If
                                                                   Using br As New SolidBrush(If(comic, t.ToColor(0.95), x.Mix(0.3).ToColor(0.55)))
                                                                       g.FillPolygon(br, poly)
                                                                   End Using
                                                               End Sub)
                        End Sub, opacity:=fl)
            Case FxKind.MotionLines
                x.Paint(Sub(g)
                            For Each p In x.Paths
                                For j = -2 To 2
                                    Dim off = j * w * 0.55
                                    Dim pts As New List(Of (P As PointF, A As Double))()
                                    Walk(p, Nothing, CSng(w * 0.2), Sub(px, py, pp, dx, dy, s, tt) pts.Add((Pt(px - dy * off, py + dx * off), If(tt > 0, s / tt, 1))))
                                    For i = 1 To pts.Count - 1
                                        Dim al = pts(i).A
                                        Using pen = x.Pen(t, 0.75 * al, w * 0.09 * (0.3 + 0.7 * al) * (1 - Math.Abs(j) * 0.2))
                                            g.DrawLine(pen, pts(i - 1).P, pts(i).P)
                                        End Using
                                    Next
                                Next
                            Next
                        End Sub, opacity:=fl)
            Case FxKind.Whirlwind
                x.Paint(Sub(g)
                            x.Every(w * 0.35, Sub(s)
                                                  Dim rx = w * (0.35 + s.Along * 1.6) * (0.9 + x.R() * 0.2), ry = rx * 0.3
                                                  Dim st = g.Save()
                                                  g.TranslateTransform(CSng(s.X), CSng(s.Y))
                                                  g.RotateTransform(CSng(Math.Atan2(s.Dy, s.Dx) * 180 / Math.PI + 90))
                                                  Using back = x.Pen(t, 0.18, w * 0.05), front = x.Pen(t, 0.45, w * 0.07)
                                                      Dim off = CSng(x.RS() * w * 0.15)
                                                      g.DrawArc(back, CSng(-rx) + off, CSng(-ry), CSng(rx * 2), CSng(ry * 2), 180, 180)
                                                      g.DrawArc(front, CSng(-rx) + off, CSng(-ry), CSng(rx * 2), CSng(ry * 2), 10, 160)
                                                  End Using
                                                  g.Restore(st)
                                                  If x.R() < 0.4 Then x.Puff(s.X + x.RS() * w * 1.5, s.Y + x.RS() * w * 1.5, w * 0.6, t.Times(0.9), 0.05 * fl)
                                                  If x.R() < 0.5 Then x.Puff(s.X + x.RS() * w * 2, s.Y + x.RS() * w * 2, Math.Max(0.8, w * 0.05), t.Times(0.5), 0.9)
                                              End Sub)
                        End Sub, opacity:=fl)
            Case FxKind.Vortex
                For Each st In x.Stamps(w * 4.5)
                    For arm = 0 To 2
                        Dim pts As New List(Of PointF)()
                        For k = 0 To 60
                            Dim th = k * 0.16 + arm * 2 * Math.PI / 3
                            Dim r = w * 0.1 * Math.Exp(k * 0.05)
                            pts.Add(PolarPt(st.X, st.Y, Math.Min(r, w * 2.2), th))
                        Next
                        x.GlowLine(pts, w * 0.06, w * 0.35, t, 0.8 * fl, 0.4 * fl)
                    Next
                    x.Glow(st.X, st.Y, w * 0.6, x.Mix(0.5), 0.5 * fl)
                Next
            Case FxKind.Airflow
                x.Paint(Sub(g)
                            For Each p In x.Paths
                                For j = 0 To 4
                                    Dim baseOff = (j - 2) * w * 0.45, phase = x.R() * 6
                                    Dim pts As New List(Of (P As PointF, A As Double))()
                                    Walk(p, Nothing, CSng(w * 0.15), Sub(px, py, pp, dx, dy, s, tt)
                                                                         Dim off = baseOff + Math.Sin(s / (w * 1.2) + phase) * w * 0.25
                                                                         Dim al = If(tt > 0, Math.Sin(Math.PI * s / tt), 1)
                                                                         pts.Add((Pt(px - dy * off, py + dx * off), al))
                                                                     End Sub)
                                    For i = 1 To pts.Count - 1
                                        Using pen = x.Pen(x.Mix(0.5), 0.45 * pts(i).A, w * 0.05)
                                            g.DrawLine(pen, pts(i - 1).P, pts(i).P)
                                        End Using
                                    Next
                                Next
                            Next
                        End Sub, opacity:=fl)
            Case FxKind.DustScatter
                x.Every(w * 0.4, Sub(s)
                                     x.Puff(s.X + x.RS() * w * 0.8, s.Y - x.R() * w * 1.2, w * (0.4 + x.R() * 0.6), t, 0.14 * fl)
                                     For k = 0 To 5
                                         Dim a = -Math.PI / 2 + x.RS() * 1.6, d = x.R() * w * 2
                                         x.Puff(s.X + Math.Cos(a) * d, s.Y + Math.Sin(a) * d, Math.Max(0.8, w * (0.03 + x.R() * 0.04)), t.Times(0.6), 0.85)
                                     Next
                                 End Sub)
            Case FxKind.Sandstorm
                x.Paint(Sub(g)
                            x.Every(w * 0.1, Sub(s)
                                                 If x.R() < 0.5 Then x.Puff(s.X + x.RS() * w, s.Y + x.RS() * w, w * 0.8, t, 0.03 * fl)
                                                 For k = 0 To 2
                                                     Dim off = x.RS() * w * 1.5, along = x.RS() * w * 0.3
                                                     Dim cx = s.X - s.Dy * off + s.Dx * along, cy = s.Y + s.Dx * off + s.Dy * along, len = w * (0.08 + x.R() * 0.22)
                                                     Using pen = x.Pen(t.Times(0.8 + x.R() * 0.4), 0.6, w * 0.025)
                                                         g.DrawLine(pen, Pt(cx, cy), Pt(cx - s.Dx * len, cy - s.Dy * len))
                                                     End Using
                                                 Next
                                             End Sub)
                        End Sub, opacity:=fl)
            Case FxKind.ParticleFlow
                x.Every(w * 0.08, Sub(s)
                                      For k = 0 To 1
                                          Dim off = Math.Pow(x.RS(), 3) * w, len = w * (0.1 + x.R() * 0.3)
                                          Dim px = s.X - s.Dy * off, py = s.Y + s.Dx * off
                                          x.GlowLine({Pt(px, py), Pt(px - s.Dx * len, py - s.Dy * len)}, w * 0.03, w * 0.11, t, 0.8 * fl, 0.3 * fl)
                                      Next
                                  End Sub)
            Case FxKind.Trail
                Dim cov(cv.W * cv.H - 1) As Single
                x.Every(w * 0.08, Sub(s) Dab(cov, Nothing, 0, cv, CSng(s.X), CSng(s.Y), CSng(w * 0.5), 0.5F, CSng(Math.Pow(s.Along, 1.6)), False, TipKind.Round))
                For i = 0 To cov.Length - 1
                    If cov(i) > 0.003F Then cv.Over(i, CSng(t.R), CSng(t.G), CSng(t.B), cov(i) * 0.85F * CSng(fl))
                Next
                x.Paint(Sub(g)
                            For Each p In x.Paths
                                For j = -3 To 3
                                    Dim off = j * w * 0.14
                                    Dim pts As New List(Of (P As PointF, A As Double))()
                                    Walk(p, Nothing, CSng(w * 0.25), Sub(px, py, pp, dx, dy, s, tt) pts.Add((Pt(px - dy * off, py + dx * off), If(tt > 0, s / tt, 1))))
                                    For i = 1 To pts.Count - 1
                                        Using pen = x.Pen(x.Mix(0.4), 0.35 * pts(i).A, w * 0.03)
                                            g.DrawLine(pen, pts(i - 1).P, pts(i).P)
                                        End Using
                                    Next
                                Next
                            Next
                        End Sub, opacity:=fl)

            '---------------- 魔法／科幻 ----------------
            Case FxKind.MagicCircle
                Dim stamps = x.Stamps(w * 5.5)
                Dim cs = x.Centers()
                x.Paint(Sub(g)
                            For Each st In stamps
                                Dim r = If(stamps.Count = x.Paths.Count, Math.Max(w * 2.2, cs.Max(Function(c) c.Radius)), w * 2.2)
                                Using pen = x.Pen(x.Mix(0.45), 0.92, Math.Max(1, w * 0.05)), thin = x.Pen(x.Mix(0.45), 0.8, Math.Max(0.8, w * 0.03))
                                    For Each k In {1.0, 0.9, 0.62, 0.55}
                                        g.DrawEllipse(If(k > 0.8, pen, thin), CSng(st.X - r * k), CSng(st.Y - r * k), CSng(r * k * 2), CSng(r * k * 2))
                                    Next
                                    Dim tri1 = Enumerable.Range(0, 3).Select(Function(i) PolarPt(st.X, st.Y, r * 0.62, -Math.PI / 2 + i * 2 * Math.PI / 3)).ToArray()
                                    Dim tri2 = Enumerable.Range(0, 3).Select(Function(i) PolarPt(st.X, st.Y, r * 0.62, Math.PI / 2 + i * 2 * Math.PI / 3)).ToArray()
                                    g.DrawPolygon(thin, tri1)
                                    g.DrawPolygon(thin, tri2)
                                    For i = 0 To 5
                                        Dim v = PolarPt(st.X, st.Y, r * 0.62, -Math.PI / 2 + i * Math.PI / 3)
                                        g.DrawEllipse(thin, v.X - CSng(r * 0.06), v.Y - CSng(r * 0.06), CSng(r * 0.12), CSng(r * 0.12))
                                    Next
                                    For i = 0 To 15
                                        Dim a = i * Math.PI / 8 + Math.PI / 16
                                        Dim gp = PolarPt(st.X, st.Y, r * 0.76, a)
                                        Glyph(g, thin, gp.X, gp.Y, r * 0.12, x.Rnd)
                                    Next
                                End Using
                            Next
                        End Sub, glowRadius:=w * 0.25, glowAmount:=0.8, opacity:=fl)
            Case FxKind.Runes
                x.Paint(Sub(g)
                            Using pen = x.Pen(x.Mix(0.35), 0.95, Math.Max(1, w * 0.07))
                                x.Every(w * 1.3, Sub(s) Glyph(g, pen, s.X, s.Y, w * 0.9, x.Rnd))
                            End Using
                        End Sub, glowRadius:=w * 0.22, glowAmount:=0.85, opacity:=fl)
            Case FxKind.HaloRing
                x.Paint(Sub(g)
                            Using pen = x.Pen(x.Mix(0.55), 0.95, Math.Max(1, w * 0.08))
                                x.Every(w * 2, Sub(s) g.DrawEllipse(pen, CSng(s.X - w * 0.9), CSng(s.Y - w * 0.3), CSng(w * 1.8), CSng(w * 0.6)))
                            End Using
                        End Sub, glowRadius:=w * 0.3, glowAmount:=0.9, opacity:=fl)
                x.Every(w * 0.7, Sub(s)
                                     If x.R() < 0.3 Then Sparkle(cv, CSng(s.X + x.RS() * w), CSng(s.Y + x.RS() * w * 0.5), CSng(w * 0.3), CSng(t.R), CSng(t.G), CSng(t.B), 0, CSng(fl))
                                 End Sub)
            Case FxKind.Stardust
                x.Every(w * 0.05, Sub(s)
                                      For k = 0 To 1
                                          Dim d = Math.Pow(x.R(), 1.5) * w * 1.3, a = x.R() * Math.PI * 2
                                          x.Glow(s.X + Math.Cos(a) * d, s.Y + Math.Sin(a) * d, Math.Max(0.7, w * (0.012 + x.R() * 0.03)), Rgb.Lerp(t, White, x.R() * 0.6), 0.9 * fl)
                                      Next
                                      If x.R() < 0.02 Then Sparkle(cv, CSng(s.X + x.RS() * w), CSng(s.Y + x.RS() * w), CSng(w * 0.3), CSng(t.R), CSng(t.G), CSng(t.B), 0.3F, CSng(fl))
                                  End Sub)
            Case FxKind.Cosmic
                Dim palette = {New Rgb(0.55, 0.3, 0.9), New Rgb(0.2, 0.45, 1), New Rgb(1, 0.35, 0.7)}
                x.Every(w * 0.5, Sub(s)
                                     x.Glow(s.X + x.RS() * w * 0.6, s.Y + x.RS() * w * 0.6, w * (0.8 + x.R() * 0.8), Rgb.Lerp(palette(x.Rnd.Next(3)), t, 0.3), 0.13 * fl)
                                     For k = 0 To 3
                                         x.Glow(s.X + x.RS() * w * 1.4, s.Y + x.RS() * w * 1.4, Math.Max(0.7, w * 0.025), White, 0.9 * fl)
                                     Next
                                     If x.R() < 0.08 Then Sparkle(cv, CSng(s.X + x.RS() * w), CSng(s.Y + x.RS() * w), CSng(w * 0.35), 1, 1, 1, 0, CSng(fl))
                                 End Sub)
            Case FxKind.BlackHole
                For Each c In x.Centers()
                    Dim r0 = Math.Max(w * 1.2, c.Radius * 0.35)
                    Dim tilt = -0.25
                    Dim ring = Sub(front As Boolean)
                                   For k = 0 To 900
                                       Dim a = x.R() * Math.PI * 2
                                       If (Math.Sin(a) > 0) <> front Then Continue For
                                       Dim rr = r0 * (1.25 + Math.Pow(x.R(), 1.5) * 1.3)
                                       Dim ex = Math.Cos(a) * rr, ey = Math.Sin(a) * rr * 0.3
                                       Dim heat = 1 - (rr - r0 * 1.25) / (r0 * 1.3)
                                       Dim col = Rgb.Lerp(Rgb.Lerp(New Rgb(0.9, 0.35, 0.1), t, 0.3), New Rgb(1, 0.95, 0.85), heat)
                                       x.Glow(c.X + ex * Math.Cos(tilt) - ey * Math.Sin(tilt), c.Y + ex * Math.Sin(tilt) + ey * Math.Cos(tilt), r0 * 0.12, col, 0.12 * fl)
                                   Next
                               End Sub
                    ring(False)
                    x.Glow(c.X, c.Y, r0 * 1.25, Rgb.Lerp(New Rgb(1, 0.6, 0.3), t, 0.3), 0.35 * fl)
                    x.FlushLight()
                    Dim cc = c
                    x.Paint(Sub(g)
                                Using br As New SolidBrush(Color.Black), pen = x.Pen(New Rgb(1, 0.85, 0.6), 0.85, Math.Max(1, r0 * 0.04))
                                    g.FillEllipse(br, CSng(cc.X - r0), CSng(cc.Y - r0), CSng(r0 * 2), CSng(r0 * 2))
                                    g.DrawEllipse(pen, CSng(cc.X - r0 * 1.04), CSng(cc.Y - r0 * 1.04), CSng(r0 * 2.08), CSng(r0 * 2.08))
                                End Using
                            End Sub)
                    ring(True)
                Next
            Case FxKind.EnergyWave
                For Each p In x.Paths
                    For j = 0 To 2
                        Dim phase = j * 2 * Math.PI / 3
                        Dim pts As New List(Of PointF)()
                        Walk(p, Nothing, CSng(w * 0.12), Sub(px, py, pp, dx, dy, s, tt)
                                                             Dim off = Math.Sin(s / (w * 2) * 2 * Math.PI + phase) * w * 0.5
                                                             pts.Add(Pt(px - dy * off, py + dx * off))
                                                         End Sub)
                        x.GlowLine(pts, w * 0.05, w * 0.3, t, 0.9 * fl, 0.4 * fl)
                    Next
                Next
            Case FxKind.Laser
                For Each p In x.Paths
                    x.GlowLine(p, w * 0.08, w * 0.55, t, 1.3 * fl, 0.9 * fl)
                    Dim e = p(p.Length - 1)
                    x.Glow(e.X, e.Y, w * 1.3, t, 0.6 * fl)
                    x.Glow(e.X, e.Y, w * 0.4, White, fl)
                    Sparkle(cv, e.X, e.Y, CSng(w * 0.9), CSng(t.R), CSng(t.G), CSng(t.B), 0.2F, CSng(fl))
                    x.Glow(p(0).X, p(0).Y, w * 0.5, t, 0.5 * fl)
                Next
            Case FxKind.Plasma
                For Each p In x.Paths
                    x.GlowLine(p, w * 0.2, w * 1.0, t, 0.25 * fl, 0.3 * fl)
                    For j = 0 To 3
                        Dim jj = j
                        Dim pts As New List(Of PointF)()
                        Walk(p, Nothing, CSng(w * 0.1), Sub(px, py, pp, dx, dy, s, tt)
                                                            Dim off = (Noise1(CSng(s / (w * 0.5) + jj * 13), layer.Seed + jj) - 0.5) * w * 0.9
                                                            pts.Add(Pt(px - dy * off, py + dx * off))
                                                        End Sub)
                        x.GlowLine(pts, w * 0.03, w * 0.25, x.Mix(0.2), 0.8 * fl, 0.25 * fl)
                    Next
                Next
            Case FxKind.Hud
                Dim cs = x.Centers()
                x.Paint(Sub(g)
                            For Each c In cs
                                Dim s0 = Math.Max(w * 3, c.Radius * 2)
                                Dim r = s0 / 2
                                Using pen = x.Pen(t, 0.95, Math.Max(1, w * 0.035)), thick = x.Pen(t, 0.85, Math.Max(1.5, w * 0.09)), faint = x.Pen(t, 0.45, Math.Max(0.8, w * 0.025))
                                    g.DrawEllipse(pen, CSng(c.X - r), CSng(c.Y - r), CSng(r * 2), CSng(r * 2))
                                    For k = 0 To 71
                                        Dim a = k * Math.PI / 36
                                        Dim inner = If(k Mod 6 = 0, 0.88, 0.94)
                                        g.DrawLine(If(k Mod 6 = 0, pen, faint), PolarPt(c.X, c.Y, r * inner, a), PolarPt(c.X, c.Y, r, a))
                                    Next
                                    For Each rr In {0.75, 0.6, 0.45}
                                        Dim start = x.R() * 360, sweep = 60 + x.R() * 160
                                        g.DrawArc(If(rr = 0.6, thick, pen), CSng(c.X - r * rr), CSng(c.Y - r * rr), CSng(r * rr * 2), CSng(r * rr * 2), CSng(start), CSng(sweep))
                                    Next
                                    g.DrawLine(faint, CSng(c.X - r * 1.2), CSng(c.Y), CSng(c.X - r * 0.3), CSng(c.Y))
                                    g.DrawLine(faint, CSng(c.X + r * 0.3), CSng(c.Y), CSng(c.X + r * 1.2), CSng(c.Y))
                                    g.DrawLine(faint, CSng(c.X), CSng(c.Y - r * 1.2), CSng(c.X), CSng(c.Y - r * 0.3))
                                    g.DrawLine(faint, CSng(c.X), CSng(c.Y + r * 0.3), CSng(c.X), CSng(c.Y + r * 1.2))
                                    Dim b = r * 1.3, bl = r * 0.25
                                    For Each sx In {-1, 1}
                                        For Each sy In {-1, 1}
                                            g.DrawLines(pen, {Pt(c.X + sx * b, c.Y + sy * (b - bl)), Pt(c.X + sx * b, c.Y + sy * b), Pt(c.X + sx * (b - bl), c.Y + sy * b)})
                                        Next
                                    Next
                                    For k = 0 To 3
                                        Dim bw = r * (0.2 + x.R() * 0.4)
                                        g.DrawLine(thick, CSng(c.X + r * 1.45), CSng(c.Y - r * 0.4 + k * r * 0.18), CSng(c.X + r * 1.45 + bw), CSng(c.Y - r * 0.4 + k * r * 0.18))
                                    Next
                                    g.DrawEllipse(pen, CSng(c.X - r * 0.06), CSng(c.Y - r * 0.06), CSng(r * 0.12), CSng(r * 0.12))
                                End Using
                            Next
                        End Sub, glowRadius:=w * 0.15, glowAmount:=0.7, opacity:=fl)
            Case FxKind.Glitch
                Dim cols = {New Rgb(1, 0.1, 0.35), New Rgb(0, 1, 1), New Rgb(1, 1, 1), New Rgb(0.15, 0.15, 0.18), t}
                x.Paint(Sub(g)
                            x.Every(w * 0.35, Sub(s)
                                                  For k = 0 To 1
                                                      Dim cx = s.X + x.RS() * w * 1.2, cy = s.Y + x.RS() * w * 1.2
                                                      Dim rw = w * (0.4 + x.R() * 2.2), rh = w * (0.04 + x.R() * 0.22)
                                                      Using b1 As New SolidBrush(cols(x.Rnd.Next(cols.Length)).ToColor(0.55 + x.R() * 0.3))
                                                          g.FillRectangle(b1, CSng(cx - rw / 2), CSng(cy - rh / 2), CSng(rw), CSng(rh))
                                                      End Using
                                                  Next
                                                  If x.R() < 0.4 Then
                                                      Dim cx = s.X + x.RS() * w, cy = s.Y + x.RS() * w, rw = w * (0.6 + x.R()), rh = w * 0.15, o = w * 0.08
                                                      Using red As New SolidBrush(Color.FromArgb(150, 255, 0, 60)), cyan As New SolidBrush(Color.FromArgb(150, 0, 255, 255))
                                                          g.FillRectangle(red, CSng(cx - rw / 2 - o), CSng(cy), CSng(rw), CSng(rh))
                                                          g.FillRectangle(cyan, CSng(cx - rw / 2 + o), CSng(cy), CSng(rw), CSng(rh))
                                                      End Using
                                                  End If
                                                  Using scan = x.Pen(White, 0.3, Math.Max(0.6, w * 0.015))
                                                      g.DrawLine(scan, CSng(s.X - w * 1.5), CSng(s.Y + x.RS() * w), CSng(s.X + w * 1.5), CSng(s.Y + x.RS() * w))
                                                  End Using
                                              End Sub)
                        End Sub, opacity:=fl)

            '---------------- 漫畫效果 ----------------
            Case FxKind.ComicBurst
                Dim stamps = x.Stamps(w * 4.5)
                Dim cs = x.Centers()
                x.Paint(Sub(g)
                            For Each st In stamps
                                Dim r = If(stamps.Count = x.Paths.Count, Math.Max(w * 1.8, cs.Max(Function(c) c.Radius)), w * 1.8)
                                Dim outer = Enumerable.Range(0, 36).Select(Function(i) PolarPt(st.X, st.Y, r * If(i Mod 2 = 0, 0.82 + x.R() * 0.33, 0.6), i * Math.PI / 18)).ToArray()
                                Dim inner = Enumerable.Range(0, 28).Select(Function(i) PolarPt(st.X, st.Y, r * If(i Mod 2 = 0, 0.52 + x.R() * 0.12, 0.38), i * Math.PI / 14 + 0.1)).ToArray()
                                Using fill As New SolidBrush(t.ToColor(1)), white As New SolidBrush(Color.White), ink = x.Pen(New Rgb(0.08, 0.08, 0.08), 1, Math.Max(1.5, w * 0.07))
                                    g.FillPolygon(fill, outer)
                                    g.DrawPolygon(ink, outer)
                                    g.FillPolygon(white, inner)
                                End Using
                            Next
                        End Sub, opacity:=fl)
            Case FxKind.FocusLines, FxKind.RadialLines
                Dim focus = layer.Fx = FxKind.FocusLines
                Dim cs = x.Centers()
                x.Paint(Sub(g)
                            For Each c In cs
                                Dim r0 = Math.Max(w * 2, c.Radius)
                                Dim far = If(focus, Math.Sqrt(CDbl(cv.W) ^ 2 + CDbl(cv.H) ^ 2) + r0, r0 + w * 8)
                                Using br As New SolidBrush(t.ToColor(1))
                                    For k = 0 To If(focus, 170, 90)
                                        Dim a = x.R() * Math.PI * 2
                                        If focus Then
                                            Dim ri = r0 * (1 + x.R() * 0.45)
                                            Dim half = Math.PI / 170 * (0.25 + x.R() * 0.75)
                                            g.FillPolygon(br, {PolarPt(c.X, c.Y, ri, a), PolarPt(c.X, c.Y, far, a - half), PolarPt(c.X, c.Y, far, a + half)})
                                        Else
                                            Dim ro = r0 * 0.4 + (far - r0 * 0.4) * (0.45 + x.R() * 0.55)
                                            Dim half = Math.PI / 90 * (0.2 + x.R() * 0.6)
                                            g.FillPolygon(br, {PolarPt(c.X, c.Y, r0 * 0.35, a), PolarPt(c.X, c.Y, ro, a - half), PolarPt(c.X, c.Y, ro, a + half)})
                                        End If
                                    Next
                                End Using
                            Next
                        End Sub, opacity:=fl)
            Case FxKind.Halftone, FxKind.ComicShadow
                Dim cov = Coverage(x, w, 0.55F)
                Dim spacing = Math.Max(3.0, w * If(layer.Fx = FxKind.Halftone, 0.22, 0.16))
                Const Inv2 = 0.70710678
                For i = 0 To cov.Length - 1
                    Dim a = cov(i)
                    If a <= 0.02F Then Continue For
                    Dim gx = i Mod cv.W + cv.OX + 0.5, gy = i \ cv.W + cv.OY + 0.5
                    Dim u = (gx + gy) * Inv2 / spacing, v = (gx - gy) * Inv2 / spacing
                    Dim alpha As Double
                    If layer.Fx = FxKind.Halftone Then
                        Dim du = (u - Math.Floor(u) - 0.5) * spacing, dv = (v - Math.Floor(v) - 0.5) * spacing
                        Dim d = Math.Sqrt(du * du + dv * dv)
                        Dim rr = spacing * 0.55 * Math.Sqrt(a)
                        alpha = Math.Max(0, Math.Min(1, rr - d + 0.5))
                    Else
                        Dim lw = spacing * 0.42 * a
                        Dim d1 = Math.Abs(u - Math.Floor(u) - 0.5) * spacing
                        alpha = Math.Max(0, Math.Min(1, lw / 2 - d1 + 0.5))
                        If a > 0.55F Then
                            Dim d2 = Math.Abs(v - Math.Floor(v) - 0.5) * spacing
                            alpha = Math.Max(alpha, Math.Max(0, Math.Min(1, spacing * 0.42 * (a - 0.55) / 2 - d2 + 0.5)))
                        End If
                    End If
                    If alpha > 0.01 Then cv.Over(i, CSng(t.R), CSng(t.G), CSng(t.B), CSng(alpha * fl))
                Next
            Case FxKind.ShakeLines
                x.Paint(Sub(g)
                            Using pen = x.Pen(t, 1, w * 0.06)
                                x.Every(w * 1.3, Sub(s)
                                                     Dim baseAngle = Math.Atan2(s.Dx, -s.Dy) * 180 / Math.PI
                                                     For Each side In {-1, 1}
                                                         Dim cx = s.X - s.Dy * side * w * 0.5, cy = s.Y + s.Dx * side * w * 0.5
                                                         For k = 0 To 2
                                                             Dim r = w * (0.35 + k * 0.2)
                                                             g.DrawArc(pen, CSng(cx - r), CSng(cy - r), CSng(r * 2), CSng(r * 2), CSng(baseAngle + If(side > 0, 0, 180) - 90 - 30), 60)
                                                         Next
                                                     Next
                                                 End Sub)
                            End Using
                        End Sub, opacity:=fl)
            Case FxKind.ShockWave
                Dim cs = x.Centers()
                x.Paint(Sub(g)
                            For Each c In cs
                                Dim r0 = Math.Max(w * 1.5, c.Radius)
                                For Each ringK In {1.0, 1.35, 1.75}
                                    Using pen = x.Pen(t, 1, w * 0.1 / ringK)
                                        Dim a = x.R() * 0.3
                                        While a < Math.PI * 2
                                            Dim sweep = 0.25 + x.R() * 0.6
                                            Dim pts = Enumerable.Range(0, 6).Select(Function(i) PolarPt(c.X, c.Y, r0 * ringK * (0.96 + x.R() * 0.08), a + sweep * i / 5)).ToArray()
                                            g.DrawLines(pen, pts)
                                            a += sweep + 0.08 + x.R() * 0.15
                                        End While
                                    End Using
                                Next
                                Using pen = x.Pen(t, 1, w * 0.05)
                                    For k = 0 To 15
                                        Dim a = x.R() * Math.PI * 2
                                        g.DrawLine(pen, PolarPt(c.X, c.Y, r0 * 1.9, a), PolarPt(c.X, c.Y, r0 * (2.1 + x.R() * 0.4), a))
                                    Next
                                End Using
                            Next
                        End Sub, opacity:=fl)
            Case FxKind.ComicDryBrush
                Dim dry = layer.Clone()
                dry.Brush = BrushKind.DryBrush
                Dim sp = New BrushSpec With {.Tip = TipKind.Bristle, .SizePressure = 0.8F, .Amount = 1, .Taper = True}
                Dim cov(cv.W * cv.H - 1) As Single
                Dim streak(cv.W * cv.H - 1) As Single
                For k = 0 To x.Paths.Count - 1
                    Bristles(cov, streak, cv, x.Paths(k), x.Pres(k), w, dry, sp, CSng(fl), 0)
                Next
                For i = 0 To cov.Length - 1
                    If cov(i) > 0.003F Then cv.Over(i, CSng(t.R), CSng(t.G), CSng(t.B), Math.Min(1, cov(i)))
                Next

            '---------------- 動畫／特攝／電影 ----------------
            Case FxKind.AtomicBreath
                ' 傳奇哥吉拉的原子吐息：白熱核心＋藍色光身＋翻騰的電漿雲邊；從起點（嘴）往終點越噴越粗，終點爆開成一團。
                ' 各層的強度都除以「半徑 / 取樣間距」，粗細與筆畫長短改變時亮度一致。
                Dim core = Rgb.Lerp(t, White, 0.9)
                Dim radiusAt = Function(a As Double) w * (0.3 + 2.6 * Math.Pow(a, 0.7))
                ' 最後一小段漸淡，像能量散開，而不是突然停住。
                Dim fadeAt = Function(a As Double) Math.Min(1, (1 - a) / 0.15 * 0.7 + 0.3)
                For k = 0 To x.Paths.Count - 1
                    Dim p = x.Paths(k)
                    Dim seed = layer.Seed + k * 31
                    ' 外圍光暈（間距大一點，省時間）
                    Dim hs = Math.Max(0.8, w * 0.25)
                    Walk(p, Nothing, CSng(hs), Sub(px, py, pp, dx, dy, s, tot)
                                                   Dim a = If(tot > 0, s / tot, 0)
                                                   Dim rr = radiusAt(a) * 2.2
                                                   x.Glow(px, py, rr, t, 0.45 * fl * fadeAt(a) * hs / rr)
                                               End Sub)
                    ' 光身、翻騰的雲邊（沿方向往後拖的火舌）與白熱核心
                    Dim sp = Math.Max(0.6, w * 0.06)
                    Walk(p, Nothing, CSng(sp), Sub(px, py, pp, dx, dy, s, tot)
                                                   Dim a = If(tot > 0, s / tot, 0)
                                                   Dim rr = radiusAt(a)
                                                   Dim fade = fadeAt(a)
                                                   x.Glow(px, py, rr * 1.05, t, 0.8 * fl * fade * sp / rr)
                                                   ' 光身裡白亮的漩渦（體積感）
                                                   If x.R() < 0.5 Then
                                                       Dim ioff = x.RS() * rr * 0.5
                                                       Dim ib = rr * (0.2 + 0.2 * x.R())
                                                       Dim ia = 0.9 * fl * fade * sp / ib
                                                       Dim ic = x.Mix(0.7 + 0.25 * x.R())
                                                       For q = 0 To 3
                                                           x.Glow(px - dy * ioff - dx * ib * 0.6 * q, py + dx * ioff - dy * ib * 0.6 * q, ib * (1 - 0.15 * q), ic, ia * (1 - 0.2 * q))
                                                       Next
                                                   End If
                                                   ' 往外翻出的淡雲團
                                                   If x.R() < 0.12 Then
                                                       Dim side = If(x.R() < 0.5, -1, 1)
                                                       Dim ooff = side * rr * (0.85 + 0.45 * x.R())
                                                       Dim ob = rr * (0.25 + 0.2 * x.R())
                                                       Dim oa = 0.6 * fl * fade * sp / ob
                                                       Dim oc = x.Mix(0.1 * x.R())
                                                       For q = 0 To 3
                                                           x.Glow(px - dy * ooff - dx * ob * 0.5 * q, py + dx * ooff - dy * ob * 0.5 * q, ob * (1 - 0.15 * q), oc, oa * (1 - 0.2 * q))
                                                       Next
                                                   End If
                                                   For j = 0 To 2
                                                       If x.R() > 0.6 Then Continue For
                                                       ' 雜訊決定這一段往外鼓多少，兩側各自起伏
                                                       Dim side = If(x.R() < 0.5, -1, 1)
                                                       Dim bulge = 0.5 + 0.75 * Noise1(CSng(s / (w * 1.1) + j * 7.3), seed + j * 5 + If(side < 0, 0, 101))
                                                       Dim off = side * rr * bulge * (0.35 + 0.65 * x.R())
                                                       Dim back = x.RS() * rr * 0.4
                                                       Dim br = rr * (0.3 + 0.45 * x.R())
                                                       Dim cx = px - dy * off + dx * back, cy = py + dx * off + dy * back
                                                       Dim c = x.Mix(0.05 + 0.25 * x.R())
                                                       Dim a0 = 0.55 * fl * fade * sp / br
                                                       For q = 0 To 3
                                                           x.Glow(cx - dx * br * 0.55 * q, cy - dy * br * 0.55 * q, br * (1 - 0.17 * q), c, a0 * (1 - 0.2 * q))
                                                       Next
                                                   Next
                                                   Dim cr = rr * 0.72
                                                   x.Glow(px, py, cr, core, 1.6 * fl * fade * sp / cr)
                                                   Dim hot = rr * 0.36
                                                   x.Glow(px, py, hot, White, 1.4 * fl * fade * sp / hot)
                                               End Sub)
                    ' 周圍飛散的火花
                    Walk(p, Nothing, CSng(Math.Max(1, w * 0.4)), Sub(px, py, pp, dx, dy, s, tot)
                                                                     If x.R() > 0.35 Then Return
                                                                     Dim rr = radiusAt(If(tot > 0, s / tot, 0))
                                                                     Dim off = x.RS() * rr * 1.7
                                                                     Dim qx = px - dy * off + dx * x.RS() * rr * 0.5, qy = py + dx * off + dy * x.RS() * rr * 0.5
                                                                     Dim r = Math.Max(0.7, w * (0.03 + x.R() * 0.05))
                                                                     x.Glow(qx, qy, r * 3, t, 0.35 * fl)
                                                                     x.Glow(qx, qy, r, White, 0.9 * fl)
                                                                 End Sub)
                    ' 嘴部：集中的亮點
                    x.Glow(p(0).X, p(0).Y, w * 0.9, t, 0.6 * fl)
                    x.Glow(p(0).X, p(0).Y, w * 0.35, White, fl)
                    ' 終點：往前散開、漸淡的電漿雲
                    Dim e = p(p.Length - 1)
                    Dim b = p(Math.Max(0, p.Length - 4))
                    Dim len = DrawGeometry.Dist(b, e)
                    Dim ux = If(len > 0.01, (e.X - b.X) / len, 1.0), uy = If(len > 0.01, (e.Y - b.Y) / len, 0.0)
                    Dim re = radiusAt(1)
                    For j = 0 To 39
                        Dim fwd = Math.Pow(x.R(), 1.5) * re * 1.2, lat = x.RS() * re * (0.6 + fwd / re * 0.5)
                        Dim br = re * (0.25 + x.R() * 0.4)
                        x.Glow(e.X + ux * fwd - uy * lat, e.Y + uy * fwd + ux * lat, br, x.Mix(0.1 + 0.4 * x.R()), 0.12 * fl * (1 - fwd / (re * 1.4)))
                    Next
                Next
                ' 逐色曝光：亮到過曝就變白（核心青白、外圍藍），而不是一直保持同一個藍。
                FlushLightHot(cv, x.Light)
                Array.Clear(x.Light, 0, x.Light.Length)

            Case FxKind.GravityBeam
                ' 傳奇基多拉的引力光線：沿筆畫的鋸齒閃電光束（白熱核心＋金色光身），兩股互相纏繞、偶爾分岔，
                ' 外面罩一大片金黃色的霧光；從嘴部往外稍微變粗，終點有命中的亮光。
                Dim hotCore = Rgb.Lerp(t, White, 0.85)
                Dim haze = Rgb.Lerp(t, New Rgb(1, 0.55, 0.12), 0.45)
                Dim amber = Rgb.Lerp(t, New Rgb(1, 0.6, 0.15), 0.3)
                ' 一股光束：寬度 = w × widthScale，隨位置由 0.55 變到 1 倍。
                Dim strand = Sub(pts As List(Of PointF), widthScale As Double, amount As Double)
                                 If pts.Count < 2 Then Return
                                 Dim sp = Math.Max(0.5, w * 0.05)
                                 Walk(pts.ToArray(), Nothing, CSng(sp), Sub(px, py, pp, dx, dy, s, tot)
                                                                            Dim a = If(tot > 0, s / tot, 0)
                                                                            Dim rr = w * (0.85 + 0.6 * a) * widthScale
                                                                            x.Glow(px, py, rr * 1.6, t, 0.9 * amount * fl * sp / (rr * 1.6))
                                                                            x.Glow(px, py, rr * 0.55, hotCore, 1.3 * amount * fl * sp / (rr * 0.55))
                                                                            x.Glow(px, py, rr * 0.16, White, 1.1 * amount * fl * sp / (rr * 0.16))
                                                                        End Sub)
                             End Sub
                For k = 0 To x.Paths.Count - 1
                    Dim p = x.Paths(k)
                    Dim seed = layer.Seed + k * 37
                    ' 大片霧光（沿原本的筆畫，越往外越大）
                    Dim hs = Math.Max(0.8, w * 0.4)
                    Walk(p, Nothing, CSng(hs), Sub(px, py, pp, dx, dy, s, tot)
                                                   Dim a = If(tot > 0, s / tot, 0)
                                                   Dim hr = w * (4.5 + 4 * a)
                                                   x.Glow(px, py, hr, haze, 0.6 * fl * hs / hr)
                                               End Sub)
                    ' 主幹與纏繞的第二股
                    Dim main = JaggedPath(p, w, seed, x.Rnd, 1)
                    strand(main, 1, 1)
                    ' 鋸齒周圍的中層光暈（光打進霧裡）
                    Dim ms = Math.Max(0.8, w * 0.2)
                    Walk(main.ToArray(), Nothing, CSng(ms), Sub(px, py, pp, dx, dy, s, tot)
                                                                Dim mr = w * (1.8 + 1.2 * If(tot > 0, s / tot, 0))
                                                                x.Glow(px, py, mr, amber, 0.5 * fl * ms / mr)
                                                            End Sub)
                    strand(JaggedPath(p, w, seed + 977, x.Rnd, 0.75), 0.55, 0.6)
                    ' 分岔：沿主幹偶爾斜斜竄出一小段鋸齒
                    Dim mainArr = main.ToArray()
                    Walk(mainArr, Nothing, CSng(Math.Max(2, w * 2.5)), Sub(px, py, pp, dx, dy, s, tot)
                                                                           If s < w * 2 OrElse x.R() > 0.35 Then Return
                                                                           Dim ang = Math.Atan2(dy, dx) + If(x.R() < 0.5, -1, 1) * (0.4 + x.R() * 0.6)
                                                                           Dim len = w * (1.5 + x.R() * 2.5)
                                                                           Dim tip = PolarPt(px, py, len, ang)
                                                                           strand(Bolt(Pt(px, py), tip, 0.3, 3, x.Rnd), 0.3, 0.55)
                                                                       End Sub)
                    ' 嘴部的亮點、終點命中的亮光
                    x.Glow(p(0).X, p(0).Y, w * 1.6, t, 0.55 * fl)
                    x.Glow(p(0).X, p(0).Y, w * 0.5, White, 0.9 * fl)
                    Dim e = main(main.Count - 1)
                    x.Glow(e.X, e.Y, w * 3.2, haze, 0.35 * fl)
                    x.Glow(e.X, e.Y, w * 1.1, hotCore, 0.8 * fl)
                Next
                FlushLightHot(cv, x.Light)
                Array.Clear(x.Light, 0, x.Light.Length)

            Case FxKind.SpiralHeatRay
                ' 1995 紅蓮哥吉拉的放射熱線：紅橘色光束＋白熱核心，外面纏著像閃電一樣不規則的白色螺旋（兩股），
                ' 邊緣是往後拖的破碎火焰舌；從嘴部往外越來越粗，終點命中處爆出白光與飛散的火花。
                Dim hotCore = Rgb.Lerp(t, New Rgb(1, 0.95, 0.8), 0.85)
                Dim flameHot = New Rgb(1, 0.78, 0.3)
                Dim ringColor = Rgb.Lerp(White, flameHot, 0.3)
                Dim sizeAt = Function(a As Double) 0.3 + 1.5 * Math.Pow(a, 0.8)
                ' 沿一條折線畫發亮的細線，粗細跟著光束（a 依折線上的位置由 a0 變到 a1）。
                Dim zap = Sub(pts As List(Of PointF), thick As Double, amount As Double, a0 As Double, a1 As Double)
                              If pts.Count < 2 Then Return
                              Dim zs = Math.Max(0.5, w * 0.05)
                              Walk(pts.ToArray(), Nothing, CSng(zs), Sub(px, py, pp, dx, dy, s, tot)
                                                                         Dim a = a0 + (a1 - a0) * If(tot > 0, s / tot, 0)
                                                                         Dim rw = Math.Max(0.6, w * thick * sizeAt(a))
                                                                         x.Glow(px, py, rw * 3, t, 0.45 * amount * fl * zs / (rw * 3))
                                                                         x.Glow(px, py, rw, ringColor, 1.8 * amount * fl * zs / rw)
                                                                     End Sub)
                          End Sub
                For k = 0 To x.Paths.Count - 1
                    Dim p = x.Paths(k)
                    Dim seed = layer.Seed + k * 41
                    ' 紅色光暈
                    Dim hs = Math.Max(0.8, w * 0.3)
                    Walk(p, Nothing, CSng(hs), Sub(px, py, pp, dx, dy, s, tot)
                                                   Dim hr = w * 2.6 * sizeAt(If(tot > 0, s / tot, 0))
                                                   x.Glow(px, py, hr, t, 0.85 * fl * hs / hr)
                                               End Sub)
                    Dim sp = Math.Max(0.6, w * 0.05)
                    Walk(p, Nothing, CSng(sp), Sub(px, py, pp, dx, dy, s, tot)
                                                   Dim a = If(tot > 0, s / tot, 0)
                                                   ' 起點細、終點粗，再加上緩慢的起伏
                                                   Dim rr = w * sizeAt(a) * (0.85 + 0.3 * Noise1(CSng(s / (w * 3)), seed))
                                                   x.Glow(px, py, rr * 1.1, t, 1.5 * fl * sp / rr)
                                                   ' 破碎的火焰舌：兩側往後拖，顏色在紅橘與黃之間
                                                   For j = 0 To 1
                                                       If x.R() > 0.75 Then Continue For
                                                       Dim side = If(x.R() < 0.5, -1, 1)
                                                       Dim bulge = 0.5 + 0.7 * Noise1(CSng(s / (w * 0.7) + j * 5.1), seed + j * 7 + If(side < 0, 0, 59))
                                                       Dim off = side * rr * bulge * (0.5 + 0.5 * x.R())
                                                       Dim br = rr * (0.25 + 0.4 * x.R())
                                                       Dim c = Rgb.Lerp(t, flameHot, x.R())
                                                       Dim a0 = 0.7 * fl * sp / br
                                                       For q = 0 To 4
                                                           x.Glow(px - dy * off * (1 + q * 0.06) - dx * br * 0.6 * q, py + dx * off * (1 + q * 0.06) - dy * br * 0.6 * q,
                                                                  br * (1 - 0.15 * q), c, a0 * (1 - 0.18 * q))
                                                       Next
                                                   Next
                                                   Dim cr = rr * 0.75
                                                   x.Glow(px, py, cr, hotCore, 1.9 * fl * sp / cr)
                                                   Dim hot = rr * 0.38
                                                   x.Glow(px, py, hot, White, 1.2 * fl * sp / hot)
                                               End Sub)
                    ' 不規則的螺旋：兩股，各自的相位、半徑、圈距都隨雜訊亂飄；
                    ' 每隔一小段取一個折點再加上隨機偏移，折點之間細分出鋸齒，看起來像纏在光束上的閃電。
                    For strand = 0 To 1
                        Dim st = strand
                        Dim knots As New List(Of PointF)()
                        Dim knotA As New List(Of Double)()
                        Dim nextKnot = 0.0
                        Dim phase = st * Math.PI + x.R() * 0.8
                        Dim lastS = 0.0
                        Walk(p, Nothing, CSng(Math.Max(0.5, w * 0.08)), Sub(px, py, pp, dx, dy, s, tot)
                                                                            Dim a = If(tot > 0, s / tot, 0)
                                                                            Dim sz = sizeAt(a)
                                                                            ' 圈距跟著變粗的光束放大，相位用雜訊推進，圈圈有疏有密
                                                                            phase += (s - lastS) / (w * (2.6 + 2.4 * a)) * 2 * Math.PI * (0.6 + 0.8 * Noise1(CSng(s / (w * 4)), seed + 300 + st * 17))
                                                                            lastS = s
                                                                            If s < nextKnot Then Return
                                                                            nextKnot = s + w * sz * (0.22 + x.R() * 0.3)
                                                                            Dim hr = w * sz * 1.05 * (0.7 + 0.6 * Noise1(CSng(s / (w * 2)), seed + 500 + st * 23))
                                                                            Dim along = hr * 0.3 * Math.Cos(phase)
                                                                            Dim side = hr * Math.Sin(phase)
                                                                            Dim jit = x.RS() * w * sz * 0.22
                                                                            knots.Add(Pt(px + dx * along - dy * (side + jit), py + dy * along + dx * (side + jit)))
                                                                            knotA.Add(a)
                                                                        End Sub)
                        If knots.Count < 2 Then Continue For
                        Dim zig As New List(Of PointF) From {knots(0)}
                        For i = 1 To knots.Count - 1
                            zig.AddRange(Bolt(knots(i - 1), knots(i), 0.22, 2, x.Rnd).Skip(1))
                        Next
                        zap(zig, If(st = 0, 0.16, 0.11), If(st = 0, 1, 0.75), knotA(0), knotA(knotA.Count - 1))
                        ' 偶爾從螺旋上竄出一小段分岔
                        For i = 1 To knots.Count - 2
                            If x.R() > 0.12 Then Continue For
                            Dim ka = knotA(i)
                            Dim ang = x.R() * 2 * Math.PI
                            Dim tip = PolarPt(knots(i).X, knots(i).Y, w * sizeAt(ka) * (0.6 + x.R() * 0.9), ang)
                            zap(Bolt(knots(i), tip, 0.3, 3, x.Rnd), 0.07, 0.6, ka, ka)
                        Next
                    Next
                    ' 嘴部的強光
                    x.Glow(p(0).X, p(0).Y, w * 1.2, Rgb.Lerp(t, flameHot, 0.5), 0.6 * fl)
                    x.Glow(p(0).X, p(0).Y, w * 0.5, White, fl)
                    ' 終點命中：白色爆光＋往四周飛散的火花
                    Dim e = p(p.Length - 1)
                    Dim re = w * sizeAt(1)
                    x.Glow(e.X, e.Y, re * 3.2, Rgb.Lerp(t, flameHot, 0.3), 0.6 * fl)
                    x.Glow(e.X, e.Y, re * 1.6, New Rgb(0.85, 0.85, 1), 0.9 * fl)
                    x.Glow(e.X, e.Y, re * 0.8, White, 1.3 * fl)
                    For j = 0 To 59
                        Dim d = re * (0.8 + Math.Pow(x.R(), 0.7) * 3.5)
                        Dim ang = x.R() * 2 * Math.PI
                        Dim q = PolarPt(e.X, e.Y, d, ang)
                        Dim r = Math.Max(0.8, w * (0.035 + x.R() * 0.05))
                        x.Glow(q.X, q.Y, r * 3, flameHot, 0.5 * fl)
                        x.Glow(q.X, q.Y, r, White, 0.9 * fl)
                    Next
                Next
                FlushLightHot(cv, x.Light)
                Array.Clear(x.Light, 0, x.Light.Length)

            Case FxKind.SpeciumRay
                ' 奧特曼的斯派修姆光線：由許多平行、長短不一的藍白光條組成（像速度線），中間較密較亮；
                ' 從手掌往外慢慢散開變粗，起點（手掌）有強烈的星芒與一道垂直於光束的光斑。
                Dim streakCore = Rgb.Lerp(t, White, 0.8)
                Dim sizeAt = Function(a As Double) 0.45 + 0.9 * Math.Pow(a, 0.7)
                For k = 0 To x.Paths.Count - 1
                    Dim p = x.Paths(k)
                    ' 先把筆畫取樣成等距的點，光條沿著這些點畫
                    Dim step0 = Math.Max(0.4, w * 0.035)
                    Dim sx As New List(Of Single)(), sy As New List(Of Single)(), sdx As New List(Of Single)(), sdy As New List(Of Single)()
                    Walk(p, Nothing, CSng(step0), Sub(px, py, pp, dx, dy, s, tot)
                                                      sx.Add(px) : sy.Add(py) : sdx.Add(dx) : sdy.Add(dy)
                                                  End Sub)
                    Dim n = sx.Count
                    If n < 2 Then
                        x.Glow(p(0).X, p(0).Y, w * 1.5, t, 0.8 * fl)
                        x.Glow(p(0).X, p(0).Y, w * 0.6, White, fl)
                        Continue For
                    End If
                    ' 淡淡的藍色光暈
                    For i = 0 To n - 1 Step 3
                        Dim hr = w * 1.6 * sizeAt(i / (n - 1))
                        x.Glow(sx(i), sy(i), hr, t, 0.55 * fl * step0 * 3 / hr)
                    Next
                    ' 平行光條：位置偏向中間，越往外越長、越分散
                    Dim count = CInt(Math.Min(4000, n * step0 / w * 9))
                    For j = 0 To count - 1
                        Dim i0 = x.Rnd.Next(n)
                        Dim a = i0 / (n - 1)
                        Dim rr = w * sizeAt(a)
                        Dim u = x.RS()
                        u = Math.Sign(u) * Math.Pow(Math.Abs(u), 1.4)
                        Dim lenSteps = CInt(rr * (1 + x.R() * 3) / step0)
                        Dim thick = Math.Max(0.6, rr * (0.04 + x.R() * 0.07) * (1 - Math.Abs(u) * 0.4))
                        Dim bright = (0.5 + 0.5 * x.R()) * (1 - Math.Abs(u) * 0.35)
                        Dim c = If(x.R() < 0.6, streakCore, Rgb.Lerp(t, White, 0.4))
                        Dim i1 = Math.Min(n - 1, i0 + lenSteps)
                        For i = i0 To i1
                            ' 兩端漸細漸淡
                            Dim f = (i - i0) / Math.Max(1.0, i1 - i0)
                            Dim taper = Math.Sin(f * Math.PI) * 0.8 + 0.2
                            Dim aa = i / (n - 1)
                            Dim off = u * w * sizeAt(aa)
                            Dim qx = sx(i) - sdy(i) * off, qy = sy(i) + sdx(i) * off
                            x.Glow(qx, qy, thick * 2.5, t, 0.55 * bright * taper * fl * step0 / (thick * 2.5))
                            x.Glow(qx, qy, thick, c, 1.2 * bright * taper * fl * step0 / thick)
                        Next
                    Next
                    ' 中心一條較細的亮芯
                    For i = 0 To n - 1
                        Dim cr = w * 0.12 * sizeAt(i / (n - 1))
                        x.Glow(sx(i), sy(i), cr, streakCore, 0.9 * fl * step0 / cr)
                    Next
                    ' 手掌的星芒：白色大光點＋沿光束與垂直光束的光芒，垂直那道特別長
                    Dim ox = sx(0), oy = sy(0), odx = sdx(0), ody = sdy(0)
                    x.Glow(ox, oy, w * 2.2, t, 0.7 * fl)
                    x.Glow(ox, oy, w * 0.9, White, 1.3 * fl)
                    Dim ray = Sub(vx As Double, vy As Double, len As Double, wid As Double)
                                  Dim m = CInt(len / step0)
                                  For q = -m To m
                                      Dim f = Math.Abs(q) / Math.Max(1.0, m)
                                      Dim fall = (1 - f) * (1 - f)
                                      x.Glow(ox + vx * q * step0, oy + vy * q * step0, wid * (1 - f * 0.7), streakCore, 0.9 * fl * fall * step0 / wid)
                                  Next
                              End Sub
                    ray(-ody, odx, w * 4.5, w * 0.12)
                    ray(odx, ody, w * 2.2, w * 0.08)
                    ray((odx - ody) * 0.7071, (ody + odx) * 0.7071, w * 1.2, w * 0.05)
                    ray((odx + ody) * 0.7071, (ody - odx) * 0.7071, w * 1.2, w * 0.05)
                Next
                FlushLightHot(cv, x.Light)
                Array.Clear(x.Light, 0, x.Light.Length)

            Case FxKind.ZeperionRay
                ' 迪迦奧特曼的哉佩利敖光線：從雙手往外大幅散開的粗金色光柱，中間白熱、邊緣柔和，
                ' 光柱裡有淡淡沿長邊流動的能量紋理；手部有一團強光，最後一小段漸淡。
                Dim inner = Rgb.Lerp(t, White, 0.45)
                Dim streakC = Rgb.Lerp(t, White, 0.75)
                Dim sizeAt = Function(a As Double) 0.35 + 2.2 * Math.Pow(a, 0.75)
                Dim fadeAt = Function(a As Double) Math.Min(1, (1 - a) / 0.12 * 0.7 + 0.3)
                For k = 0 To x.Paths.Count - 1
                    Dim p = x.Paths(k)
                    Dim step0 = Math.Max(0.4, w * 0.04)
                    Dim sx As New List(Of Single)(), sy As New List(Of Single)(), sdx As New List(Of Single)(), sdy As New List(Of Single)()
                    Walk(p, Nothing, CSng(step0), Sub(px, py, pp, dx, dy, s, tot)
                                                      sx.Add(px) : sy.Add(py) : sdx.Add(dx) : sdy.Add(dy)
                                                  End Sub)
                    Dim n = sx.Count
                    If n >= 2 Then
                        ' 外圍金色光暈
                        For i = 0 To n - 1 Step 4
                            Dim a = i / (n - 1)
                            Dim hr = w * 1.9 * sizeAt(a)
                            x.Glow(sx(i), sy(i), hr, t, 0.85 * fl * fadeAt(a) * step0 * 4 / hr)
                        Next
                        ' 光身、內層與白熱核心
                        For i = 0 To n - 1 Step 2
                            Dim a = i / (n - 1)
                            Dim rr = w * sizeAt(a)
                            Dim fade = fadeAt(a)
                            x.Glow(sx(i), sy(i), rr, t, 1.3 * fl * fade * step0 * 2 / rr)
                            x.Glow(sx(i), sy(i), rr * 0.65, inner, 1.4 * fl * fade * step0 * 2 / (rr * 0.65))
                            x.Glow(sx(i), sy(i), rr * 0.3, White, 1.2 * fl * fade * step0 * 2 / (rr * 0.3))
                        Next
                        ' 沿長邊流動的能量紋理：柔和的長光條，比斯派修姆光線少、寬、淡
                        Dim count = CInt(Math.Min(1500, n * step0 / w * 3))
                        For j = 0 To count - 1
                            Dim i0 = x.Rnd.Next(n)
                            Dim u = x.RS() * 0.85
                            Dim rr0 = w * sizeAt(i0 / (n - 1))
                            Dim i1 = Math.Min(n - 1, i0 + CInt(rr0 * (1.5 + x.R() * 3) / step0))
                            Dim thick = Math.Max(0.7, rr0 * (0.05 + x.R() * 0.08))
                            Dim bright = 0.3 + 0.4 * x.R()
                            For i = i0 To i1
                                Dim f = (i - i0) / Math.Max(1.0, i1 - i0)
                                Dim taper = Math.Sin(f * Math.PI)
                                Dim a = i / (n - 1)
                                Dim off = u * w * sizeAt(a)
                                x.Glow(sx(i) - sdy(i) * off, sy(i) + sdx(i) * off, thick, streakC, bright * taper * fadeAt(a) * fl * step0 / thick)
                            Next
                        Next
                    End If
                    ' 手部的強光
                    x.Glow(p(0).X, p(0).Y, w * 2.8, t, 0.8 * fl)
                    x.Glow(p(0).X, p(0).Y, w * 1.0, White, 1.3 * fl)
                Next
                FlushLightHot(cv, x.Light)
                Array.Clear(x.Light, 0, x.Light.Length)

            Case FxKind.M87Ray
                ' 佐菲的 M87 光線：寬而整齊的光帶，由一行行水平的彩虹色短條（紅、黃、綠、藍、紫、粉）組成，
                ' 像掃描線、帶點數位雜訊；手掌處有一道垂直的亮邊。顏色固定是彩虹色，線條色只當作淡淡的底光。
                For k = 0 To x.Paths.Count - 1
                    Dim p = x.Paths(k)
                    Dim step0 = Math.Max(0.4, w * 0.035)
                    Dim sx As New List(Of Single)(), sy As New List(Of Single)(), sdx As New List(Of Single)(), sdy As New List(Of Single)()
                    Walk(p, Nothing, CSng(step0), Sub(px, py, pp, dx, dy, s, tot)
                                                      sx.Add(px) : sy.Add(py) : sdx.Add(dx) : sdy.Add(dy)
                                                  End Sub)
                    Dim n = sx.Count
                    If n < 2 Then
                        x.Glow(p(0).X, p(0).Y, w * 1.2, White, fl)
                        Continue For
                    End If
                    Dim halfW = w * 1.5
                    Dim widthAt = Function(a As Double) halfW * (1 + 0.15 * a)
                    ' 淡淡的底光
                    For i = 0 To n - 1 Step 4
                        Dim hr = widthAt(i / (n - 1)) * 1.3
                        x.Glow(sx(i), sy(i), hr, t, 0.3 * fl * step0 * 4 / hr)
                    Next
                    ' 掃描線：寬度分成一行一行，每行上放長短不一的彩色短條
                    Dim rows = Math.Max(6, CInt(halfW * 2 / Math.Max(2, w * 0.22)))
                    Dim count = CInt(Math.Min(6000, n * step0 / w * 26))
                    For j = 0 To count - 1
                        Dim i0 = x.Rnd.Next(n)
                        Dim row = x.Rnd.Next(rows)
                        Dim u = (row + 0.5) / rows * 2 - 1
                        Dim lenSteps = CInt(w * (0.3 + Math.Pow(x.R(), 2) * 3.5) / step0)
                        Dim i1 = Math.Min(n - 1, i0 + lenSteps)
                        Dim thick = Math.Max(0.8, halfW * 2 / rows * (0.32 + 0.12 * x.R()))
                        ' 彩虹色：偏紅、粉、藍、紫，少數白色
                        Dim c = If(x.R() < 0.1, White, Rgb.Lerp(HueColor(x.R()), White, 0.05 + 0.12 * x.R()))
                        ' 靠近手掌、靠近中間較亮
                        Dim bright = (0.55 + 0.45 * x.R()) * (1 - 0.3 * Math.Abs(u))
                        For i = i0 To i1
                            Dim a = i / (n - 1)
                            Dim off = u * widthAt(a)
                            Dim qx = sx(i) - sdy(i) * off, qy = sy(i) + sdx(i) * off
                            Dim amt = bright * (1.25 - 0.55 * a) * fl
                            x.Glow(qx, qy, thick * 2.2, c, 0.5 * amt * step0 / (thick * 2.2))
                            x.Glow(qx, qy, thick, c, 1.1 * amt * step0 / thick)
                        Next
                    Next
                    ' 手掌處垂直的亮邊（彩色鑲邊）
                    Dim ox = sx(0), oy = sy(0), nx = -sdy(0), ny = sdx(0)
                    Dim m = CInt(widthAt(0) * 1.1 / step0)
                    For q = -m To m
                        Dim f = Math.Abs(q) / Math.Max(1.0, m)
                        Dim ex = ox + nx * q * step0, ey = oy + ny * q * step0
                        x.Glow(ex, ey, w * 0.45, HueColor((q + m) / (2.0 * m)), 0.5 * fl * (1 - f * f) * step0 / (w * 0.45))
                        x.Glow(ex, ey, w * 0.15, White, 1.4 * fl * (1 - f * f) * step0 / (w * 0.15))
                    Next
                    x.Glow(ox, oy, w * 1.4, White, 0.5 * fl)
                Next
                FlushLightHot(cv, x.Light)
                Array.Clear(x.Light, 0, x.Light.Length)
        End Select
        FlushLight(cv, x.Light)
    End Sub

    ''' <summary>沿筆畫的柔邊覆蓋率（燃燒邊緣、網點、排線用）。</summary>
    Private Shared Function Coverage(x As FxCtx, width As Single, edge As Single) As Single()
        Dim cov(x.Cv.W * x.Cv.H - 1) As Single
        Dim sp As New BrushSpec With {.Tip = TipKind.Round, .Edge = edge, .Spacing = 0.08F, .SizePressure = 0.3F, .Amount = 1}
        For k = 0 To x.Paths.Count - 1
            Stamp(cov, x.Cv, x.Paths(k), x.Pres(k), width, sp, 1, edge)
        Next
        Return cov
    End Function

    ''' <summary>色相 0..1 → 飽和的彩虹色（油漬的虹彩）。</summary>
    Private Shared Function HueColor(h As Double) As Rgb
        Dim k = Function(n As Double) Math.Max(0, Math.Min(1, Math.Abs((h * 6 + n) Mod 6 - 3) - 1))
        Return New Rgb(k(0), k(4), k(2))
    End Function
End Class
