Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>藝術風格（數值存進編輯檔，不可更動；新的往後加）。</summary>
Public Enum ArtStyle
    None = 0
    MangaBW = 1
    MangaColor = 2
    Watercolor = 3
    OilPainting = 4
    InkWash = 5
    Etching = 6
    Sketch = 7
    Pointillism = 8
    Ukiyoe = 9
    ColoredPencil = 10
    Woodcut = 11
    PopArt = 12
    PixelArt = 13
End Enum

''' <summary>
''' 藝術風格：把照片畫成漫畫、水彩、油畫、水墨、版畫等。算圖時放在色調、局部調整、模糊之後，
''' 暗角與文字貼圖之前（貼圖與繪圖不會被風格化）。
''' 尺寸都以「處理影像高度 / 900」為單位（再乘上「細節」），預覽與匯出看起來一致；
''' 很大的照片先縮到長邊 2400 處理再放大回去，速度與記憶體都可控。
''' 強度 = 與原圖混合的比例；線條 = 輪廓線的粗細；細節 = 筆觸、網點、色塊的大小（越大越粗獷）。
''' </summary>
Public NotInheritable Class ArtStyles
    Private Sub New()
    End Sub

    Public Const MaxWorkSize As Integer = 2400

    Public Shared ReadOnly Names As String() = {
        "無", "黑白漫畫", "彩色漫畫", "水彩畫", "油畫", "水墨畫", "銅版畫", "素描", "點描", "浮世繪", "彩色鉛筆", "木刻版畫", "普普藝術", "像素藝術"}

    ''' <summary>效果分頁選單的分類（依序列出）。</summary>
    Public Shared ReadOnly Categories As (Name As String, Styles As ArtStyle())() = {
        ("繪畫", {ArtStyle.Watercolor, ArtStyle.OilPainting, ArtStyle.InkWash, ArtStyle.Sketch, ArtStyle.ColoredPencil, ArtStyle.Pointillism}),
        ("漫畫／版畫", {ArtStyle.MangaBW, ArtStyle.MangaColor, ArtStyle.Ukiyoe, ArtStyle.Etching, ArtStyle.Woodcut, ArtStyle.PopArt, ArtStyle.PixelArt})}

    ''' <summary>處理中的影像：0..1 的 R、G、B。</summary>
    Private NotInheritable Class Img
        Public ReadOnly W As Integer, H As Integer
        Public ReadOnly R As Single(), G As Single(), B As Single()
        Public Sub New(w As Integer, h As Integer)
            Me.W = w : Me.H = h
            R = New Single(w * h - 1) {} : G = New Single(w * h - 1) {} : B = New Single(w * h - 1) {}
        End Sub
        Public Function Luma() As Single()
            Dim l(W * H - 1) As Single
            For i = 0 To l.Length - 1
                l(i) = 0.299F * R(i) + 0.587F * G(i) + 0.114F * B(i)
            Next
            Return l
        End Function
        Public Function Copy() As Img
            Dim c As New Img(W, H)
            Array.Copy(R, c.R, R.Length) : Array.Copy(G, c.G, G.Length) : Array.Copy(B, c.B, B.Length)
            Return c
        End Function
    End Class

    '=====================================================================
    ' 進入點
    '=====================================================================

    Public Shared Sub Apply(bmp As Bitmap, recipe As EditRecipe)
        If recipe.ArtStyle = ArtStyle.None OrElse recipe.ArtStrength <= 0 Then Return
        Apply(bmp, recipe.ArtStyle, recipe.ArtStrength, recipe.ArtLine, recipe.ArtDetail)
    End Sub

    ' 最近一次的結果：輸入畫面與參數都沒變時（例如只改了暗角、文字、繪圖）直接用，不必重算。
    Private Shared _cacheKey As String
    Private Shared _cachePixels As Byte()
    Private Shared ReadOnly _cacheLock As New Object()

    Public Shared Sub Apply(bmp As Bitmap, style As ArtStyle, strength As Integer, line As Integer, detail As Integer)
        If style = ArtStyle.None OrElse strength <= 0 OrElse bmp.Width < 2 OrElse bmp.Height < 2 Then Return
        ' 濾鏡縮圖（很小、很快）不進快取，免得把預覽的結果擠掉
        If CLng(bmp.Width) * bmp.Height < 120000 Then
            ApplyUncached(bmp, style, strength, line, detail)
            Return
        End If
        Dim input = Perspective.ReadPixels(bmp)
        Dim key = $"{CInt(style)}|{strength}|{line}|{detail}|{bmp.Width}x{bmp.Height}|" &
                  Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(input))
        SyncLock _cacheLock
            If key = _cacheKey AndAlso _cachePixels IsNot Nothing AndAlso _cachePixels.Length = input.Length Then
                Perspective.WritePixels(bmp, _cachePixels)
                Return
            End If
        End SyncLock
        ApplyUncached(bmp, style, strength, line, detail)
        SyncLock _cacheLock
            _cacheKey = key
            _cachePixels = Perspective.ReadPixels(bmp)
        End SyncLock
    End Sub

    Private Shared Sub ApplyUncached(bmp As Bitmap, style As ArtStyle, strength As Integer, line As Integer, detail As Integer)
        ' 太大的照片縮小處理
        Dim k = Math.Min(1.0, MaxWorkSize / Math.Max(bmp.Width, bmp.Height))
        Dim work As Bitmap = bmp
        If k < 1 Then
            work = New Bitmap(Math.Max(2, CInt(bmp.Width * k)), Math.Max(2, CInt(bmp.Height * k)), PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(work)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.DrawImage(bmp, New Rectangle(0, 0, work.Width, work.Height))
            End Using
        End If
        Dim px = Perspective.ReadPixels(work)
        Dim src As New Img(work.Width, work.Height)
        For i = 0 To src.R.Length - 1
            src.B(i) = px(i * 4) / 255.0F : src.G(i) = px(i * 4 + 1) / 255.0F : src.R(i) = px(i * 4 + 2) / 255.0F
        Next
        Dim unit = CSng(Math.Max(0.5, Math.Sqrt(CDbl(src.W) * src.H) / 1000.0) * (0.4 + Math.Max(0, Math.Min(100, detail)) / 100.0 * 1.2))
        Dim lineK = CSng(0.3 + Math.Max(0, Math.Min(100, line)) / 100.0 * 1.4)
        Dim art = Stylize(src, style, Math.Max(0.2F, unit), lineK)

        ' 寫回（保留原本的透明度）
        For i = 0 To art.R.Length - 1
            px(i * 4) = ToByte(art.B(i)) : px(i * 4 + 1) = ToByte(art.G(i)) : px(i * 4 + 2) = ToByte(art.R(i))
        Next
        Dim result As Bitmap
        If work Is bmp Then
            result = New Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb)
            Perspective.WritePixels(result, px)
        Else
            Perspective.WritePixels(work, px)
            result = New Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(result)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.DrawImage(work, New Rectangle(0, 0, bmp.Width, bmp.Height))
            End Using
            work.Dispose()
        End If
        ' 依強度與原圖混合，透明度用原圖的
        Dim orig = Perspective.ReadPixels(bmp)
        Dim res = Perspective.ReadPixels(result)
        result.Dispose()
        Dim t = Math.Max(0, Math.Min(100, strength)) / 100.0F
        For i = 0 To orig.Length - 1 Step 4
            For c = 0 To 2
                orig(i + c) = CByte(Math.Round(orig(i + c) + (res(i + c) - CInt(orig(i + c))) * t))
            Next
        Next
        Perspective.WritePixels(bmp, orig)
    End Sub

    Private Shared Function Stylize(src As Img, style As ArtStyle, s As Single, lineK As Single) As Img
        Select Case style
            Case ArtStyle.MangaBW : Return MangaBW(src, s, lineK)
            Case ArtStyle.MangaColor : Return MangaColor(src, s, lineK)
            Case ArtStyle.Watercolor : Return Watercolor(src, s, lineK)
            Case ArtStyle.OilPainting : Return OilPainting(src, s)
            Case ArtStyle.InkWash : Return InkWash(src, s, lineK)
            Case ArtStyle.Etching : Return Etching(src, s, lineK)
            Case ArtStyle.Sketch : Return Sketch(src, s, lineK)
            Case ArtStyle.Pointillism : Return Pointillism(src, s)
            Case ArtStyle.Ukiyoe : Return Ukiyoe(src, s, lineK)
            Case ArtStyle.ColoredPencil : Return ColoredPencil(src, s, lineK)
            Case ArtStyle.Woodcut : Return Woodcut(src, s, lineK)
            Case ArtStyle.PopArt : Return PopArt(src, s, lineK)
            Case ArtStyle.PixelArt : Return PixelArt(src, s)
            Case Else : Return src
        End Select
    End Function

    '=====================================================================
    ' 各種風格
    '=====================================================================

    ''' <summary>黑白漫畫：墨線＋黑、網點、白三階。</summary>
    Private Shared Function MangaBW(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim l = Gauss(LocalTone(src.Luma(), w, h, 24 * s), w, h, s * 0.7F)
        Dim ink = XDoG(src.Luma(), w, h, s * 0.8F * lineK)
        Dim period = Math.Max(3.0F, 4.2F * s)
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim v = SmoothStep(0.08F, 0.92F, l(i))
                Dim tone As Single
                If v < 0.1F Then
                    tone = 0
                ElseIf v < 0.6F Then
                    ' 45° 網點：越暗點越大
                    Dim darkness = 0.2F + (0.6F - v) / 0.5F * 0.6F
                    tone = 1 - Halftone(x, y, period, 45, darkness)
                Else
                    tone = 1
                End If
                Dim v2 = Math.Min(tone, 1 - ink(i))
                o.R(i) = v2 : o.G(i) = v2 : o.B(i) = v2
            Next
        Next
        Return o
    End Function

    ''' <summary>彩色漫畫：平塗色塊（明暗分 5 階、提高飽和）＋黑色輪廓線。</summary>
    Private Shared Function MangaColor(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim c = Kuwahara(src, Math.Max(1, CInt(2.5F * s)))
        Dim ink = XDoG(src.Luma(), w, h, s * 0.75F * lineK)
        Dim o As New Img(w, h)
        For i = 0 To w * h - 1
            Dim l = 0.299F * c.R(i) + 0.587F * c.G(i) + 0.114F * c.B(i)
            Dim q = CSng(Math.Round(l * 4) / 4)
            q = Math.Max(0.08F, q)
            Dim k = q / Math.Max(0.03F, l)
            Dim rr = c.R(i) * k, gg = c.G(i) * k, bb = c.B(i) * k
            Saturate(rr, gg, bb, 1.3F)
            Dim a = 1 - ink(i) * 0.95F
            o.R(i) = Clamp01(rr) * a + 0.08F * (1 - a) : o.G(i) = Clamp01(gg) * a + 0.08F * (1 - a) : o.B(i) = Clamp01(bb) * a + 0.12F * (1 - a)
        Next
        Return o
    End Function

    ''' <summary>水彩：平滑去細節、顏色微微暈開、色塊邊緣的水痕加深、顆粒與紙紋、整體透明提亮。</summary>
    Private Shared Function Watercolor(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim c = Kuwahara(Kuwahara(src, Math.Max(1, CInt(5 * s))), Math.Max(1, CInt(2.5F * s)))
        c = Wobble(c, s * 3.2F, s * 7, 11)
        Dim lum = c.Luma()
        Dim edge = GradientMagnitude(Gauss(lum, w, h, s * 0.8F), w, h)
        Dim pencil = XDoG(src.Luma(), w, h, s * 0.6F * lineK)
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim gran = 0.9F + 0.1F * Fbm(x / (1.6F * s), y / (1.6F * s), 21)
                Dim pap = Paper(x, y, s)
                Dim e = Clamp01(edge(i) * 9 / Math.Max(0.6F, s))
                Dim rr = c.R(i), gg = c.G(i), bb = c.B(i)
                Saturate(rr, gg, bb, 1.12F)
                ' 透明感：往白紙提亮；色塊邊緣顏料堆積變深；顆粒沉在紙紋裡
                Dim blot = 0.8F + 0.4F * Fbm(x / (18 * s), y / (18 * s), 25) ' 顏料濃淡不均的水漬
                rr = 1 - (1 - rr) * 0.78F * blot : gg = 1 - (1 - gg) * 0.78F * blot : bb = 1 - (1 - bb) * 0.78F * blot
                Dim dark = (1 - 0.3F * e) * gran
                Dim ln = 1 - pencil(i) * 0.25F
                o.R(i) = rr * dark * pap * ln : o.G(i) = gg * dark * pap * ln : o.B(i) = bb * dark * pap * 0.985F * ln
            Next
        Next
        Return o
    End Function

    ''' <summary>油畫：Kuwahara 色塊＋沿畫面紋理方向的筆觸（LIC）＋厚塗光影。</summary>
    Private Shared Function OilPainting(src As Img, s As Single) As Img
        Dim w = src.W, h = src.H
        Dim c = Kuwahara(Kuwahara(src, Math.Max(1, CInt(5 * s))), Math.Max(1, CInt(2.5F * s)))
        Dim lum = c.Luma()
        Dim angle = Orientation(Gauss(lum, w, h, s * 1.2F), w, h, s * 3)
        Dim noise = WhiteNoise(w, h, 5, Math.Max(1.2F, s * 2.2F))
        Dim strokes = Lic(noise, angle, w, h, Math.Max(4, CInt(9 * s)))
        Stretch(strokes)
        ' 厚度＝筆觸紋理＋一點亮度；光從左上
        Dim height(w * h - 1) As Single
        For i = 0 To height.Length - 1
            height(i) = strokes(i) * 0.7F + lum(i) * 0.3F
        Next
        Dim o As New Img(w, h)
        Dim bump = 1.1F / Math.Max(0.7F, s)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim dx = height(y * w + Math.Min(w - 1, x + 1)) - height(y * w + Math.Max(0, x - 1))
                Dim dy = height(Math.Min(h - 1, y + 1) * w + x) - height(Math.Max(0, y - 1) * w + x)
                Dim light = Clamp01(0.5F - (dx + dy) * bump)
                Dim shade = 0.82F + 0.36F * light
                Dim spec = CSng(Math.Pow(light, 6)) * 0.18F
                Dim rr = c.R(i), gg = c.G(i), bb = c.B(i)
                Saturate(rr, gg, bb, 1.15F)
                o.R(i) = Clamp01(rr * shade + spec) : o.G(i) = Clamp01(gg * shade + spec) : o.B(i) = Clamp01(bb * shade + spec)
            Next
        Next
        Return o
    End Function

    ''' <summary>水墨：灰階大幅平滑、墨色分濃淡數階並自然暈開、乾筆輪廓、宣紙底與留白。</summary>
    Private Shared Function InkWash(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim lumSrc = Levels(src.Luma())
        Dim gray As New Img(w, h)
        Array.Copy(lumSrc, gray.R, lumSrc.Length) : Array.Copy(lumSrc, gray.G, lumSrc.Length) : Array.Copy(lumSrc, gray.B, lumSrc.Length)
        Dim smooth = Kuwahara(gray, Math.Max(1, CInt(5 * s))).R
        Dim ink(w * h - 1) As Single
        For i = 0 To ink.Length - 1
            Dim d = CSng(Math.Pow(SmoothStep(0.22F, 0.95F, 1 - smooth(i)), 1.3))
            ' 濃、淡、淡淡三階（柔和的階梯）
            Dim q = CSng(Math.Round(d * 3) / 3)
            ink(i) = d * 0.35F + q * 0.65F
            If smooth(i) > 0.82F Then ink(i) *= 0.25F ' 亮處留白
        Next
        Dim bleed = Gauss(ink, w, h, s * 2.2F)
        Dim lines = XDoG(lumSrc, w, h, s * 0.9F * lineK)
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim a = Math.Max(ink(i), bleed(i) * 0.85F)
                ' 乾筆：輪廓線被紙紋打斷
                Dim dry = If(Fbm(x / (0.8F * s), y / (3 * s), 31) > 0.38F, 1.0F, 0.35F)
                a = Math.Max(a, lines(i) * dry)
                a = Clamp01(a * (0.92F + 0.08F * Fbm(x / (2 * s), y / (2 * s), 37)))
                Dim pr = 0.953F * Paper(x, y, s), pg = 0.933F * Paper(x, y, s), pb = 0.886F * Paper(x, y, s)
                o.R(i) = pr + (0.09F - pr) * a : o.G(i) = pg + (0.09F - pg) * a : o.B(i) = pb + (0.1F - pb) * a
            Next
        Next
        Return o
    End Function

    ''' <summary>銅版畫：依明暗畫平行排線（越暗越粗越密，最暗交叉排線）＋輪廓線，米黃紙、深褐墨。</summary>
    Private Shared Function Etching(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim l = Gauss(LocalTone(src.Luma(), w, h, 24 * s), w, h, s * 0.6F)
        Dim contour = XDoG(src.Luma(), w, h, s * 0.7F * lineK)
        Dim period = Math.Max(3.5F, 4.6F * s)
        Dim layers = {(T:=0.8F, A:=45.0), (T:=0.55F, A:=-45.0), (T:=0.34F, A:=0.0), (T:=0.16F, A:=90.0)}
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim v = SmoothStep(0.05F, 0.95F, l(i))
                Dim a = 0.0F
                For Each ly In layers
                    If v >= ly.T Then Continue For
                    Dim wobble = (Noise(x / (12 * s), y / (12 * s), 41) - 0.5F) * period * 0.6F
                    Dim cov = HatchLine(x, y, period, ly.A, wobble, Math.Min(0.7F, (ly.T - v) / ly.T * 1.3F))
                    a = Math.Max(a, cov)
                Next
                a = Math.Max(a, contour(i))
                Dim pr = 0.93F, pg = 0.9F, pb = 0.82F
                Dim p = Paper(x, y, s)
                o.R(i) = (pr + (0.15F - pr) * a) * p : o.G(i) = (pg + (0.12F - pg) * a) * p : o.B(i) = (pb + (0.1F - pb) * a) * p
            Next
        Next
        Return o
    End Function

    ''' <summary>素描：減淡混合得到線稿＋暗部斜向鉛筆排線＋紙紋，石墨灰。</summary>
    Private Shared Function Sketch(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim l = src.Luma()
        Dim lines = DodgeSketch(l, w, h, s * 2.2F * lineK)
        Dim hatch = DirectionalNoise(w, h, s, 45, 51)
        Dim hatch2 = DirectionalNoise(w, h, s, 135, 53)
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim dark = 1 - SmoothStep(0.1F, 0.95F, l(i))
                Dim shade = 1 - dark * (0.45F + 0.55F * hatch(i)) * 0.75F
                If dark > 0.55F Then shade -= (dark - 0.55F) * hatch2(i) * 0.6F
                Dim v = Math.Min(lines(i), shade) * (0.94F + 0.06F * Paper(x, y, s))
                v = Clamp01(v)
                o.R(i) = v * 0.98F : o.G(i) = v * 0.985F : o.B(i) = v
            Next
        Next
        Return o
    End Function

    ''' <summary>點描（秀拉）：在白畫布上點滿一顆顆小色點，顏色略加變化、提高飽和。</summary>
    Private Shared Function Pointillism(src As Img, s As Single) As Img
        Dim w = src.W, h = src.H
        Dim c = GaussImg(src, s * 1.2F)
        Dim o As New Img(w, h)
        For i = 0 To w * h - 1
            o.R(i) = 0.97F : o.G(i) = 0.96F : o.B(i) = 0.93F
        Next
        Dim spacing = Math.Max(3.0F, 5.2F * s)
        Dim rnd As New Random(7)
        For pass = 0 To 1
            Dim off = pass * spacing / 2
            Dim y0 = off
            While y0 < h + spacing
                Dim x0 = off
                While x0 < w + spacing
                    Dim cx = x0 + CSng(rnd.NextDouble() - 0.5) * spacing * 0.8F
                    Dim cy = y0 + CSng(rnd.NextDouble() - 0.5) * spacing * 0.8F
                    Dim sx = Math.Max(0, Math.Min(w - 1, CInt(cx))), sy = Math.Max(0, Math.Min(h - 1, CInt(cy)))
                    Dim i = sy * w + sx
                    Dim rr = c.R(i), gg = c.G(i), bb = c.B(i)
                    Saturate(rr, gg, bb, 1.35F)
                    Dim jl = CSng(rnd.NextDouble() - 0.5) * 0.14F
                    rr = Clamp01(rr + jl) : gg = Clamp01(gg + jl) : bb = Clamp01(bb + jl)
                    Dim radius = spacing * CSng(0.42 + rnd.NextDouble() * 0.2)
                    Disc(o, cx, cy, radius, rr, gg, bb, 0.95F)
                    x0 += spacing
                End While
                y0 += spacing
            End While
        Next
        Return o
    End Function

    ''' <summary>浮世繪：平塗色塊、顏色收斂到浮世繪常用色（普魯士藍、朱紅、黃土、米白…）、粗輪廓、和紙紋。</summary>
    Private Shared Function Ukiyoe(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim c = Kuwahara(src, Math.Max(1, CInt(3 * s)))
        Dim ink = XDoG(src.Luma(), w, h, s * 1.0F * lineK)
        Dim pal = {(38, 64, 104), (88, 124, 158), (160, 190, 196), (196, 72, 48), (214, 160, 84), (240, 228, 200),
                   (104, 140, 108), (60, 90, 70), (222, 176, 160), (44, 36, 34), (150, 120, 90)}
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim rr = c.R(i) * 255, gg = c.G(i) * 255, bb = c.B(i) * 255
                Dim best = 0, bestD = Single.MaxValue
                For p = 0 To pal.Length - 1
                    Dim dr = rr - pal(p).Item1, dg = gg - pal(p).Item2, db = bb - pal(p).Item3
                    Dim d = dr * dr * 0.3F + dg * dg * 0.59F + db * db * 0.11F + Math.Abs((rr - gg) - (pal(p).Item1 - pal(p).Item2)) * 8
                    If d < bestD Then bestD = d : best = p
                Next
                Dim k = 0.72F
                Dim fr = (rr + (pal(best).Item1 - rr) * k) / 255, fg = (gg + (pal(best).Item2 - gg) * k) / 255, fb = (bb + (pal(best).Item3 - bb) * k) / 255
                Dim p2 = Paper(x, y, s) * (0.96F + 0.04F * Fbm(x / (0.6F * s), y / (4 * s), 61))
                Dim a = ink(i) * 0.95F
                o.R(i) = (fr * (1 - a) + 0.16F * a) * p2 : o.G(i) = (fg * (1 - a) + 0.13F * a) * p2 : o.B(i) = (fb * (1 - a) + 0.12F * a) * p2
            Next
        Next
        Return o
    End Function

    ''' <summary>彩色鉛筆：淡彩＋兩個方向的鉛筆筆觸（暗處較密）＋鉛筆線稿、白紙紋。</summary>
    Private Shared Function ColoredPencil(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim c = GaussImg(src, s * 0.8F)
        Dim lines = DodgeSketch(src.Luma(), w, h, s * 1.8F * lineK)
        Dim h1 = DirectionalNoise(w, h, s, 30, 71)
        Dim h2 = DirectionalNoise(w, h, s, 120, 73)
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim l = 0.299F * c.R(i) + 0.587F * c.G(i) + 0.114F * c.B(i)
                Dim dark = 1 - l
                Dim cover = Clamp01(0.35F + 0.65F * h1(i) + If(dark > 0.45F, (dark - 0.45F) * h2(i) * 1.2F, 0))
                Dim grain = 0.93F + 0.07F * Paper(x, y, s)
                Dim rr = c.R(i), gg = c.G(i), bb = c.B(i)
                Saturate(rr, gg, bb, 1.15F)
                ' 顏色只塗在筆觸上，空隙露出白紙
                rr = 1 - (1 - rr) * cover * 0.9F : gg = 1 - (1 - gg) * cover * 0.9F : bb = 1 - (1 - bb) * cover * 0.9F
                Dim ln = 0.55F + 0.45F * lines(i)
                o.R(i) = rr * ln * grain : o.G(i) = gg * ln * grain : o.B(i) = bb * ln * grain
            Next
        Next
        Return o
    End Function

    ''' <summary>木刻版畫：黑白高反差，中間調刻出沿著明暗走的白色刀痕，木紋紙底。</summary>
    Private Shared Function Woodcut(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim l = Gauss(LocalTone(src.Luma(), w, h, 24 * s), w, h, s * 1.2F)
        Dim edges = XDoG(src.Luma(), w, h, s * 1.0F * lineK)
        Dim freq = 9.0F / Math.Max(0.6F, s * 0.5F + 0.5F)
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim v = SmoothStep(0.15F, 0.85F, l(i))
                Dim n = Noise(x / (6 * s), y / (6 * s), 81) - 0.5F
                ' 沿等亮線的刀痕：亮度加上週期波，中間調變成一條條黑白線
                Dim carve = CSng(Math.Sin((v * freq + n * 0.8) * Math.PI * 2)) * 0.26F
                Dim t = v + carve * Math.Max(0.25F, 1 - Math.Abs(v - 0.5F) * 1.4F)
                Dim ink = 1 - SmoothStep(0.46F, 0.54F, t)
                ink = Math.Max(ink, edges(i))
                Dim grain = 0.9F + 0.1F * Fbm(x / (0.7F * s), y / (10 * s), 83)
                Dim pr = 0.94F * grain, pg = 0.9F * grain, pb = 0.82F * grain
                o.R(i) = pr + (0.1F - pr) * ink : o.G(i) = pg + (0.08F - pg) * ink : o.B(i) = pb + (0.07F - pb) * ink
            Next
        Next
        Return o
    End Function

    ''' <summary>普普藝術（安迪沃荷風）：亮度分四階換成鮮豔的對比色，中間色加班戴網點，黑色輪廓。</summary>
    Private Shared Function PopArt(src As Img, s As Single, lineK As Single) As Img
        Dim w = src.W, h = src.H
        Dim l = Gauss(Levels(src.Luma()), w, h, s * 0.9F)
        Dim ink = XDoG(src.Luma(), w, h, s * 0.9F * lineK)
        Dim pal = {(24, 22, 60), (232, 36, 120), (255, 160, 20), (255, 236, 70)}
        Dim period = Math.Max(3.0F, 5 * s)
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim v = SmoothStep(0.1F, 0.9F, l(i))
                Dim lvl = Math.Min(3, CInt(Math.Floor(v * 4)))
                Dim c = pal(lvl)
                Dim rr = c.Item1 / 255.0F, gg = c.Item2 / 255.0F, bb = c.Item3 / 255.0F
                If lvl = 2 Then
                    ' 橘色區加洋紅網點
                    Dim d = Halftone(x, y, period, 15, 0.45F)
                    rr += (232 / 255.0F - rr) * d : gg += (36 / 255.0F - gg) * d : bb += (120 / 255.0F - bb) * d
                End If
                Dim a = ink(i)
                o.R(i) = rr * (1 - a) + 0.05F * a : o.G(i) = gg * (1 - a) + 0.05F * a : o.B(i) = bb * (1 - a) + 0.08F * a
            Next
        Next
        Return o
    End Function

    ''' <summary>像素藝術：縮成大方塊、每色限 6 階、提高飽和。</summary>
    Private Shared Function PixelArt(src As Img, s As Single) As Img
        Dim w = src.W, h = src.H
        Dim block = Math.Max(3, CInt(Math.Round(7 * s)))
        Dim o As New Img(w, h)
        For by = 0 To h - 1 Step block
            For bx = 0 To w - 1 Step block
                Dim sr = 0.0F, sg = 0.0F, sb = 0.0F, n = 0
                For y = by To Math.Min(h - 1, by + block - 1)
                    For x = bx To Math.Min(w - 1, bx + block - 1)
                        Dim i = y * w + x
                        sr += src.R(i) : sg += src.G(i) : sb += src.B(i) : n += 1
                    Next
                Next
                Dim rr = sr / n, gg = sg / n, bb = sb / n
                Saturate(rr, gg, bb, 1.25F)
                rr = CSng(Math.Round(Clamp01(rr) * 5) / 5) : gg = CSng(Math.Round(Clamp01(gg) * 5) / 5) : bb = CSng(Math.Round(Clamp01(bb) * 5) / 5)
                For y = by To Math.Min(h - 1, by + block - 1)
                    For x = bx To Math.Min(w - 1, bx + block - 1)
                        Dim i = y * w + x
                        o.R(i) = rr : o.G(i) = gg : o.B(i) = bb
                    Next
                Next
            Next
        Next
        Return o
    End Function

    '=====================================================================
    ' 工具
    '=====================================================================

    ''' <summary>分離式方框模糊（邊緣夾住），radius 0 時原樣複製。</summary>
    Private Shared Function BoxBlur(src As Single(), w As Integer, h As Integer, radius As Integer) As Single()
        Dim n = w * h
        If radius <= 0 Then
            Dim c(n - 1) As Single
            Array.Copy(src, c, n)
            Return c
        End If
        Dim tmp(n - 1) As Single, dst(n - 1) As Single
        Dim inv = 1.0F / (radius * 2 + 1)
        For y = 0 To h - 1
            Dim row = y * w
            Dim sum = 0.0F
            For k = -radius To radius
                sum += src(row + Math.Max(0, Math.Min(w - 1, k)))
            Next
            For x = 0 To w - 1
                tmp(row + x) = sum * inv
                sum += src(row + Math.Min(w - 1, x + radius + 1)) - src(row + Math.Max(0, x - radius))
            Next
        Next
        For x = 0 To w - 1
            Dim sum = 0.0F
            For k = -radius To radius
                sum += tmp(Math.Max(0, Math.Min(h - 1, k)) * w + x)
            Next
            For y = 0 To h - 1
                dst(y * w + x) = sum * inv
                sum += tmp(Math.Min(h - 1, y + radius + 1) * w + x) - tmp(Math.Max(0, y - radius) * w + x)
            Next
        Next
        Return dst
    End Function

    ''' <summary>近似高斯模糊（三次方框）。</summary>
    Private Shared Function Gauss(src As Single(), w As Integer, h As Integer, sigma As Single) As Single()
        Dim r = Math.Max(0, CInt(Math.Round(sigma * 0.95F)))
        If r = 0 Then Return BoxBlur(src, w, h, 0)
        Return BoxBlur(BoxBlur(BoxBlur(src, w, h, r), w, h, r), w, h, r)
    End Function

    Private Shared Function GaussImg(src As Img, sigma As Single) As Img
        Dim o As New Img(src.W, src.H)
        Dim r = Gauss(src.R, src.W, src.H, sigma), g = Gauss(src.G, src.W, src.H, sigma), b = Gauss(src.B, src.W, src.H, sigma)
        Array.Copy(r, o.R, r.Length) : Array.Copy(g, o.G, g.Length) : Array.Copy(b, o.B, b.Length)
        Return o
    End Function

    ''' <summary>
    ''' Kuwahara 濾鏡：每點看左上、右上、左下、右下四個方塊，取亮度變化最小那塊的平均色。
    ''' 細節變成一塊塊的平塗、邊緣保持清楚（繪畫感的基礎）。用方框平均算，速度和半徑無關。
    ''' </summary>
    Private Shared Function Kuwahara(src As Img, radius As Integer) As Img
        Dim w = src.W, h = src.H
        Dim m = Math.Max(1, radius \ 2)
        Dim l = src.Luma()
        Dim l2(l.Length - 1) As Single
        For i = 0 To l.Length - 1
            l2(i) = l(i) * l(i)
        Next
        Dim mr = BoxBlur(src.R, w, h, m), mg = BoxBlur(src.G, w, h, m), mb = BoxBlur(src.B, w, h, m)
        Dim ml = BoxBlur(l, w, h, m), ml2 = BoxBlur(l2, w, h, m)
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim best = -1, bestV = Single.MaxValue
                For q = 0 To 3
                    Dim qx = Math.Max(0, Math.Min(w - 1, x + If(q Mod 2 = 0, -m, m)))
                    Dim qy = Math.Max(0, Math.Min(h - 1, y + If(q < 2, -m, m)))
                    Dim j = qy * w + qx
                    Dim v = ml2(j) - ml(j) * ml(j)
                    If v < bestV Then bestV = v : best = j
                Next
                Dim i = y * w + x
                o.R(i) = mr(best) : o.G(i) = mg(best) : o.B(i) = mb(best)
            Next
        Next
        Return o
    End Function

    ''' <summary>XDoG 墨線（0 = 沒有線、1 = 實線）：兩個高斯的差，在暗的那側畫線。</summary>
    Private Shared Function XDoG(l As Single(), w As Integer, h As Integer, sigma As Single) As Single()
        sigma = Math.Max(0.6F, sigma)
        Dim g1 = Gauss(l, w, h, sigma), g2 = Gauss(l, w, h, sigma * 1.6F)
        Dim ink(l.Length - 1) As Single
        For i = 0 To ink.Length - 1
            Dim d = g2(i) - g1(i) ' 暗邊為正
            ink(i) = SmoothStep(0.012F, 0.045F, d)
        Next
        Return ink
    End Function

    ''' <summary>減淡混合線稿：灰階除以（1 − 反相模糊），平的地方變白、邊緣留下鉛筆線。</summary>
    Private Shared Function DodgeSketch(l As Single(), w As Integer, h As Integer, sigma As Single) As Single()
        Dim inv(l.Length - 1) As Single
        For i = 0 To l.Length - 1
            inv(i) = 1 - l(i)
        Next
        Dim blur = Gauss(inv, w, h, Math.Max(1, sigma))
        Dim o(l.Length - 1) As Single
        For i = 0 To o.Length - 1
            o(i) = Clamp01(l(i) / Math.Max(0.02F, 1 - blur(i)))
            o(i) = o(i) * o(i) ' 線條深一點
        Next
        Return o
    End Function

    ''' <summary>
    ''' 自動色階：以第 1 與第 99 百分位拉滿 0..1，再和直方圖均化各半混合。
    ''' 很暗或很亮的照片（例如黑色的模型）也能分出層次，漫畫網點、水墨濃淡、版畫刀痕才出得來。
    ''' </summary>
    Private Shared Function Levels(l As Single()) As Single()
        Dim hist(255) As Integer
        For Each v In l
            hist(Math.Max(0, Math.Min(255, CInt(v * 255)))) += 1
        Next
        Dim cdf(255) As Single
        Dim acc = 0
        For i = 0 To 255
            acc += hist(i)
            cdf(i) = acc / CSng(l.Length)
        Next
        Dim lo = Array.FindIndex(cdf, Function(c) c >= 0.01F), hi = Array.FindIndex(cdf, Function(c) c >= 0.99F)
        If hi <= lo Then hi = lo + 1
        Dim o(l.Length - 1) As Single
        For i = 0 To l.Length - 1
            Dim b = Math.Max(0, Math.Min(255, CInt(l(i) * 255)))
            Dim stretched = Clamp01((b - lo) / CSng(hi - lo))
            o(i) = stretched * 0.5F + cdf(b) * 0.5F
        Next
        Return o
    End Function

    ''' <summary>
    ''' 色階＋局部對比：每一點和周圍（半徑 radius）比亮暗，深色主體裡的紋理也分得出來
    ''' （黑白漫畫、銅版畫、木刻版畫用，否則深色物體會變成一片黑）。
    ''' </summary>
    Private Shared Function LocalTone(l As Single(), w As Integer, h As Integer, radius As Single) As Single()
        Dim g = Levels(l)
        Dim r = Math.Max(2, CInt(radius))
        Dim mean = BoxBlur(BoxBlur(g, w, h, r), w, h, r)
        Dim sq(g.Length - 1) As Single
        For i = 0 To g.Length - 1
            sq(i) = (g(i) - mean(i)) * (g(i) - mean(i))
        Next
        Dim varc = BoxBlur(BoxBlur(sq, w, h, r), w, h, r)
        Dim o(g.Length - 1) As Single
        For i = 0 To g.Length - 1
            Dim local = 0.5F + (g(i) - mean(i)) * 0.22F / (CSng(Math.Sqrt(Math.Max(0, varc(i)))) + 0.06F) ' 浮點誤差可能讓變異數略小於 0
            o(i) = Clamp01(g(i) * 0.55F + Clamp01(local) * 0.45F)
        Next
        Return o
    End Function

    ''' <summary>索貝爾梯度大小。</summary>
    Private Shared Function GradientMagnitude(l As Single(), w As Integer, h As Integer) As Single()
        Dim o(l.Length - 1) As Single
        For y = 1 To h - 2
            For x = 1 To w - 2
                Dim i = y * w + x
                Dim gx = l(i - w + 1) + 2 * l(i + 1) + l(i + w + 1) - l(i - w - 1) - 2 * l(i - 1) - l(i + w - 1)
                Dim gy = l(i + w - 1) + 2 * l(i + w) + l(i + w + 1) - l(i - w - 1) - 2 * l(i - w) - l(i - w + 1)
                o(i) = CSng(Math.Sqrt(gx * gx + gy * gy)) * 0.25F
            Next
        Next
        Return o
    End Function

    ''' <summary>每點的筆觸方向（弧度）：結構張量平滑後，與梯度垂直（順著邊緣走）。</summary>
    Private Shared Function Orientation(l As Single(), w As Integer, h As Integer, smooth As Single) As Single()
        Dim n = w * h
        Dim exx(n - 1) As Single, eyy(n - 1) As Single, exy(n - 1) As Single
        For y = 1 To h - 2
            For x = 1 To w - 2
                Dim i = y * w + x
                Dim gx = (l(i + 1) - l(i - 1)) * 0.5F, gy = (l(i + w) - l(i - w)) * 0.5F
                exx(i) = gx * gx : eyy(i) = gy * gy : exy(i) = gx * gy
            Next
        Next
        exx = Gauss(exx, w, h, smooth) : eyy = Gauss(eyy, w, h, smooth) : exy = Gauss(exy, w, h, smooth)
        Dim a(n - 1) As Single
        For i = 0 To n - 1
            ' 主方向（梯度）角度，再轉 90° 順著邊緣
            a(i) = CSng(0.5 * Math.Atan2(2 * exy(i), exx(i) - eyy(i)) + Math.PI / 2)
        Next
        Return a
    End Function

    ''' <summary>線積分卷積：沿每點的方向前後走 steps 步平均雜訊，得到順著方向的筆觸紋理。</summary>
    Private Shared Function Lic(noise As Single(), angle As Single(), w As Integer, h As Integer, steps As Integer) As Single()
        Dim o(noise.Length - 1) As Single
        ' 每點的方向向量先算好（省掉每一步的三角函數）
        Dim cs(angle.Length - 1) As Single, sn(angle.Length - 1) As Single
        For i = 0 To angle.Length - 1
            cs(i) = CSng(Math.Cos(angle(i))) : sn(i) = CSng(Math.Sin(angle(i)))
        Next
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim sum = noise(y * w + x), cnt = 1
                For dirSign = -1 To 1 Step 2
                    Dim px = CSng(x), py = CSng(y)
                    For k = 1 To steps
                        Dim ix = Math.Max(0, Math.Min(w - 1, CInt(px))), iy = Math.Max(0, Math.Min(h - 1, CInt(py)))
                        Dim j = iy * w + ix
                        px += cs(j) * dirSign : py += sn(j) * dirSign
                        If px < 0 OrElse py < 0 OrElse px >= w OrElse py >= h Then Exit For
                        sum += noise(Math.Min(h - 1, CInt(py)) * w + Math.Min(w - 1, CInt(px))) : cnt += 1
                    Next
                Next
                o(y * w + x) = sum / cnt
            Next
        Next
        Return o
    End Function

    ''' <summary>固定方向的筆觸雜訊（鉛筆排線用）：雜訊沿 angle 方向拉長。</summary>
    Private Shared Function DirectionalNoise(w As Integer, h As Integer, s As Single, angleDeg As Double, seed As Integer) As Single()
        Dim a = angleDeg * Math.PI / 180
        Dim ca = CSng(Math.Cos(a)), sa = CSng(Math.Sin(a))
        Dim o(w * h - 1) As Single
        Dim along = 9 * s, across = Math.Max(0.6F, 0.7F * s)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim u = (x * ca + y * sa) / along, v = (-x * sa + y * ca) / across
                o(y * w + x) = SmoothStep(0.35F, 0.75F, Noise(u, v, seed))
            Next
        Next
        Return o
    End Function

    Private Shared Function WhiteNoise(w As Integer, h As Integer, seed As Integer, blur As Single) As Single()
        Dim o(w * h - 1) As Single
        For y = 0 To h - 1
            For x = 0 To w - 1
                o(y * w + x) = Hash(x, y, seed)
            Next
        Next
        Return If(blur >= 1, Gauss(o, w, h, blur * 0.5F), o)
    End Function

    ''' <summary>沿雜訊方向小幅位移取樣（水彩的暈開、手繪的不規則）。</summary>
    Private Shared Function Wobble(src As Img, amount As Single, scale As Single, seed As Integer) As Img
        Dim w = src.W, h = src.H
        Dim o As New Img(w, h)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim dx = (Noise(x / scale, y / scale, seed) - 0.5F) * 2 * amount
                Dim dy = (Noise(x / scale, y / scale, seed + 5) - 0.5F) * 2 * amount
                Dim sx = Math.Max(0, Math.Min(w - 1, CInt(x + dx))), sy = Math.Max(0, Math.Min(h - 1, CInt(y + dy)))
                Dim i = y * w + x, j = sy * w + sx
                o.R(i) = src.R(j) : o.G(i) = src.G(j) : o.B(i) = src.B(j)
            Next
        Next
        Return o
    End Function

    ''' <summary>網點：以 angle 旋轉的格子，點的面積 ≈ darkness；回傳這一點的墨量（反鋸齒）。</summary>
    Private Shared Function Halftone(x As Integer, y As Integer, period As Single, angleDeg As Double, darkness As Single) As Single
        Dim a = angleDeg * Math.PI / 180
        Dim u = CSng((x * Math.Cos(a) + y * Math.Sin(a)) / period), v = CSng((-x * Math.Sin(a) + y * Math.Cos(a)) / period)
        Dim fu = u - CSng(Math.Floor(u)) - 0.5F, fv = v - CSng(Math.Floor(v)) - 0.5F
        Dim d = CSng(Math.Sqrt(fu * fu + fv * fv)) * period
        Dim r = CSng(Math.Sqrt(Clamp01(darkness) / Math.PI)) * period
        Return Clamp01(r - d + 0.5F)
    End Function

    ''' <summary>平行排線：週期 period、角度 angle、線寬佔週期的 width（0..1）；回傳墨量（反鋸齒）。</summary>
    Private Shared Function HatchLine(x As Integer, y As Integer, period As Single, angleDeg As Double, offset As Single, width As Single) As Single
        Dim a = angleDeg * Math.PI / 180
        Dim u = CSng(x * Math.Cos(a) + y * Math.Sin(a)) + offset
        Dim f = u / period - CSng(Math.Floor(u / period)) - 0.5F
        Dim d = Math.Abs(f) * period
        Return Clamp01(width * period / 2 - d + 0.5F)
    End Function

    ''' <summary>在影像上畫一顆反鋸齒的實心圓（點描用）。</summary>
    Private Shared Sub Disc(img As Img, cx As Single, cy As Single, radius As Single, r As Single, g As Single, b As Single, alpha As Single)
        Dim x0 = Math.Max(0, CInt(Math.Floor(cx - radius - 1))), x1 = Math.Min(img.W - 1, CInt(Math.Ceiling(cx + radius + 1)))
        Dim y0 = Math.Max(0, CInt(Math.Floor(cy - radius - 1))), y1 = Math.Min(img.H - 1, CInt(Math.Ceiling(cy + radius + 1)))
        For y = y0 To y1
            For x = x0 To x1
                Dim d = CSng(Math.Sqrt((x + 0.5F - cx) ^ 2 + (y + 0.5F - cy) ^ 2))
                Dim a = Clamp01(radius - d + 0.5F) * alpha
                If a <= 0 Then Continue For
                Dim i = y * img.W + x
                img.R(i) += (r - img.R(i)) * a : img.G(i) += (g - img.G(i)) * a : img.B(i) += (b - img.B(i)) * a
            Next
        Next
    End Sub

    ''' <summary>紙紋（0.9..1）：細顆粒＋淡淡的纖維。</summary>
    Private Shared Function Paper(x As Integer, y As Integer, s As Single) As Single
        Dim fine = Hash(x, y, 91)
        Dim fiber = Noise(x / (1.2F * s), y / (14 * s), 93)
        Return 0.93F + 0.04F * fine + 0.03F * fiber
    End Function

    Private Shared Sub Saturate(ByRef r As Single, ByRef g As Single, ByRef b As Single, k As Single)
        Dim l = 0.299F * r + 0.587F * g + 0.114F * b
        r = l + (r - l) * k : g = l + (g - l) * k : b = l + (b - l) * k
    End Sub

    Private Shared Sub Stretch(a As Single())
        Dim mn = a.Min(), mx = a.Max()
        Dim d = Math.Max(0.0001F, mx - mn)
        For i = 0 To a.Length - 1
            a(i) = (a(i) - mn) / d
        Next
    End Sub

    Private Shared Function Hash(x As Integer, y As Integer, seed As Integer) As Single
        Dim n = CLng(x) * 374761393L + CLng(y) * 668265263L + CLng(seed) * 144665L
        n = (n Xor (n >> 13)) And &HFFFFFFFFL
        n = (n * 1274126177L) And &HFFFFFFFFL
        n = n Xor (n >> 16)
        Return (n And &HFFFFL) / 65535.0F
    End Function

    ''' <summary>平滑的值雜訊（0..1）。</summary>
    Private Shared Function Noise(x As Single, y As Single, seed As Integer) As Single
        Dim ix = CInt(Math.Floor(x)), iy = CInt(Math.Floor(y))
        Dim fx = x - ix, fy = y - iy
        fx = fx * fx * (3 - 2 * fx) : fy = fy * fy * (3 - 2 * fy)
        Dim a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed)
        Return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy
    End Function

    Private Shared Function Fbm(x As Single, y As Single, seed As Integer) As Single
        Return Noise(x, y, seed) * 0.57F + Noise(x * 2.1F, y * 2.1F, seed + 1) * 0.29F + Noise(x * 4.3F, y * 4.3F, seed + 2) * 0.14F
    End Function

    Private Shared Function SmoothStep(e0 As Single, e1 As Single, v As Single) As Single
        Dim t = Clamp01((v - e0) / (e1 - e0))
        Return t * t * (3 - 2 * t)
    End Function

    Private Shared Function Clamp01(v As Single) As Single
        If v < 0 Then Return 0
        If v > 1 Then Return 1
        Return v
    End Function

    Private Shared Function ToByte(v As Single) As Byte
        If Not (v > 0) Then Return 0 ' 含 NaN
        If v >= 1 Then Return 255
        Return CByte(v * 255 + 0.5F)
    End Function
End Class
