Imports PhotoEdit
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Drawing.Text
Imports System.Threading.Tasks

''' <summary>
''' 照片邊框：照片原封不動放在 (Left, Top)、四周加上邊框（畫布座標換算靠這一點，所有樣式都遵守）。
''' 共用選項：主色（或取照片主色）、下方加寬、陰影、內框細線、照片圓角、邊框文字。
''' 古典金框、木框、畫廊襯紙的外框用同一個「線腳」算法：描述橫切面的高低，光從左上來算出斜面的明暗。
''' 撕紙邊、郵票、花邊、卷軸外面是透明的（存 PNG 會保留）。
''' </summary>
Public NotInheritable Class PhotoFrames
    Private Sub New()
    End Sub

    ''' <summary>模糊延伸背景的輸出比例（寬:高）。</summary>
    Public Shared ReadOnly AspectNames As String() = {"1:1", "4:5", "9:16", "16:9", "3:2", "2:3"}
    Private Shared ReadOnly AspectValues As Double() = {1, 4 / 5.0, 9 / 16.0, 16 / 9.0, 3 / 2.0, 2 / 3.0}

    ''' <summary>會用到「邊框文字」的樣式。</summary>
    Public Shared Function UsesText(style As PhotoFrameStyle) As Boolean
        Select Case style
            Case PhotoFrameStyle.CameraInfo, PhotoFrameStyle.Polaroid, PhotoFrameStyle.GalleryMat, PhotoFrameStyle.Stamp,
                 PhotoFrameStyle.SlideMount, PhotoFrameStyle.MediumFormat, PhotoFrameStyle.Film
                Return True
        End Select
        Return False
    End Function

    ''' <summary>外面是透明的樣式（撕紙邊、郵票、花邊、卷軸）。</summary>
    Public Shared Function HasTransparentEdge(style As PhotoFrameStyle) As Boolean
        Return style = PhotoFrameStyle.TornPaper OrElse style = PhotoFrameStyle.Stamp OrElse style = PhotoFrameStyle.Deckle OrElse style = PhotoFrameStyle.Scroll
    End Function

    ''' <summary>照片的邊緣被筆觸、暈邊取代的樣式（不加圓角、細線）。</summary>
    Private Shared Function MasksPhoto(style As PhotoFrameStyle) As Boolean
        Return style = PhotoFrameStyle.Watercolor OrElse style = PhotoFrameStyle.SoftFade OrElse style = PhotoFrameStyle.InkBrush
    End Function

    ''' <summary>各樣式的預設主色。</summary>
    Public Shared Function DefaultColor(style As PhotoFrameStyle) As Color
        Select Case style
            Case PhotoFrameStyle.Black, PhotoFrameStyle.Film, PhotoFrameStyle.MediumFormat : Return Color.FromArgb(18, 18, 18)
            Case PhotoFrameStyle.Polaroid : Return Color.FromArgb(250, 249, 245)
            Case PhotoFrameStyle.GalleryMat : Return Color.FromArgb(246, 243, 236)
            Case PhotoFrameStyle.GoldFrame : Return Color.FromArgb(205, 165, 72)
            Case PhotoFrameStyle.WoodFrame : Return Color.FromArgb(128, 84, 48)
            Case PhotoFrameStyle.Scroll : Return Color.FromArgb(214, 205, 180)
            Case PhotoFrameStyle.Deckle : Return Color.FromArgb(244, 238, 222)
            Case PhotoFrameStyle.Tape : Return Color.FromArgb(232, 228, 218)
            Case PhotoFrameStyle.PhotoCorners : Return Color.FromArgb(226, 216, 194)
            Case PhotoFrameStyle.SlideMount : Return Color.FromArgb(240, 240, 236)
            Case PhotoFrameStyle.Watercolor, PhotoFrameStyle.SoftFade, PhotoFrameStyle.Pencil : Return Color.FromArgb(250, 248, 243)
            Case PhotoFrameStyle.InkBrush : Return Color.FromArgb(24, 22, 20)
            Case Else : Return Color.White
        End Select
    End Function

    Private Shared Function Base(recipe As EditRecipe, w As Integer, h As Integer) As Integer
        Return Math.Max(2, CInt(Math.Min(w, h) * (0.01 + recipe.FrameSize / 100.0 * 0.07)))
    End Function

    '=====================================================================
    ' 邊框寬度
    '=====================================================================

    Public Shared Function Margins(recipe As EditRecipe, w As Integer, h As Integer) As (Left As Integer, Top As Integer, Right As Integer, Bottom As Integer)
        If recipe.Frame = PhotoFrameStyle.None Then Return (0, 0, 0, 0)
        Dim bw = Base(recipe, w, h)
        Dim m As (Left As Integer, Top As Integer, Right As Integer, Bottom As Integer)
        Dim sc = Function(k As Double) Math.Max(2, CInt(bw * k))
        Select Case recipe.Frame
            Case PhotoFrameStyle.Polaroid : m = (bw, bw, bw, bw * 4)
            Case PhotoFrameStyle.Rounded : m = (bw \ 2, bw \ 2, bw \ 2, bw \ 2)
            Case PhotoFrameStyle.Film
                Dim side = Math.Max(2, bw \ 3), tb = CInt(bw * 1.8)
                m = (side, tb, side, tb)
            Case PhotoFrameStyle.CameraInfo
                Dim side = sc(0.45)
                m = (side, side, side, Math.Max(sc(3.2), CInt(Math.Min(w, h) * 0.13)))
            Case PhotoFrameStyle.BlurExtend
                Dim a = AspectValues(Math.Max(0, Math.Min(AspectValues.Length - 1, recipe.FrameAspect)))
                Dim cw = w + 2 * bw, ch = h + 2 * bw
                Dim tw = cw, th = ch
                If cw / CDbl(ch) < a Then tw = CInt(Math.Ceiling(ch * a)) Else th = CInt(Math.Ceiling(cw / a))
                Dim l = (tw - w) \ 2, t = (th - h) \ 2
                Return (l, t, tw - w - l, th - h - t) ' 比例固定：不加下方加寬
            Case PhotoFrameStyle.GalleryMat : m = (sc(1.9), sc(1.9), sc(1.9), sc(2.25))
            Case PhotoFrameStyle.GoldFrame : m = (sc(2.6), sc(2.6), sc(2.6), sc(2.6))
            Case PhotoFrameStyle.WoodFrame : m = (sc(2.0), sc(2.0), sc(2.0), sc(2.0))
            Case PhotoFrameStyle.Scroll
                Dim s = ScrollParts(bw)
                m = (s.Side + s.Knob, s.Cord + s.Bar + s.Head, s.Side + s.Knob, s.Foot + s.Roller)
            Case PhotoFrameStyle.Deckle : m = (sc(1.15), sc(1.15), sc(1.15), sc(1.15))
            Case PhotoFrameStyle.TornPaper : m = (sc(1.1), sc(1.1), sc(1.1), sc(1.1))
            Case PhotoFrameStyle.Stamp : m = (sc(0.95), sc(0.95), sc(0.95), sc(0.95))
            Case PhotoFrameStyle.Tape
                Dim t = Math.Max(sc(1.6), CInt(Math.Min(w, h) * 0.085))
                m = (t, t, t, t)
            Case PhotoFrameStyle.PhotoCorners : m = (sc(1.3), sc(1.3), sc(1.3), sc(1.3))
            Case PhotoFrameStyle.SlideMount : m = (sc(1.7), sc(1.7), sc(1.7), sc(1.7))
            Case PhotoFrameStyle.MediumFormat : m = (sc(0.7), sc(1.1), sc(0.7), sc(1.1))
            Case PhotoFrameStyle.Watercolor : m = (sc(1.3), sc(1.3), sc(1.3), sc(1.3))
            Case PhotoFrameStyle.InkBrush : m = (sc(1.6), sc(1.6), sc(1.6), sc(1.6))
            Case PhotoFrameStyle.Pencil, PhotoFrameStyle.SoftFade : m = (sc(1.0), sc(1.0), sc(1.0), sc(1.0))
            Case Else : m = (bw, bw, bw, bw)
        End Select
        If recipe.FrameBottom > 0 Then m.Bottom += CInt(bw * Math.Min(200, recipe.FrameBottom) / 100.0)
        Return m
    End Function

    ''' <summary>卷軸各部分的高度、寬度：天杆上的掛繩、天杆、天頭、地頭、軸、兩側綾邊、軸頭伸出。</summary>
    Private Shared Function ScrollParts(bw As Integer) As (Cord As Integer, Bar As Integer, Head As Integer, Foot As Integer, Roller As Integer, Side As Integer, Knob As Integer)
        Dim k = Function(f As Double) Math.Max(2, CInt(bw * f))
        Return (k(1.2), k(0.55), k(4.2), k(2.7), k(1.05), k(1.6), k(0.65))
    End Function

    '=====================================================================
    ' 合成
    '=====================================================================

    Public Shared Function Apply(bmp As Bitmap, recipe As EditRecipe) As Bitmap
        Dim style = recipe.Frame
        If style = PhotoFrameStyle.None Then Return Nothing
        Dim w = bmp.Width, h = bmp.Height
        Dim m = Margins(recipe, w, h)
        Dim bw = Base(recipe, w, h)
        Dim fullW = w + m.Left + m.Right, fullH = h + m.Top + m.Bottom
        Dim main = If(recipe.FrameAutoColor, DominantColor(bmp), If(recipe.FrameColorArgb.HasValue, Color.FromArgb(255, Color.FromArgb(recipe.FrameColorArgb.Value)), DefaultColor(style)))
        Dim photoRect As New Rectangle(m.Left, m.Top, w, h)
        Dim result As New Bitmap(fullW, fullH, PixelFormat.Format32bppArgb)
        Dim ctx As New FrameContext With {.Recipe = recipe, .Photo = bmp, .Result = result, .PhotoRect = photoRect, .Bw = bw, .Main = main, .Margins = m}

        ' 1. 底：邊框的面
        Using g = Graphics.FromImage(result)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            Select Case style
                Case PhotoFrameStyle.BlurExtend : PaintBlurBackground(g, ctx)
                Case PhotoFrameStyle.Scroll, PhotoFrameStyle.TornPaper, PhotoFrameStyle.Stamp, PhotoFrameStyle.Deckle
                    ' 外面透明，邊框形狀之後再畫
                Case PhotoFrameStyle.InkBrush : g.Clear(Color.FromArgb(250, 248, 243)) ' 宣紙；主色是墨
                Case Else : g.Clear(main)
            End Select
        End Using
        Select Case style
            Case PhotoFrameStyle.GoldFrame : Moulding(result, New Rectangle(0, 0, fullW, fullH), photoRect, AddressOf GoldProfile, main, 0.55F)
            Case PhotoFrameStyle.WoodFrame : Moulding(result, New Rectangle(0, 0, fullW, fullH), photoRect, AddressOf WoodProfile, main, 0.4F, wood:=True)
            Case PhotoFrameStyle.GalleryMat
                Dim edge = Math.Max(2, CInt(bw * 0.28))
                Moulding(result, New Rectangle(0, 0, fullW, fullH), New Rectangle(edge, edge, fullW - 2 * edge, fullH - 2 * edge), AddressOf ThinFrameProfile, Color.FromArgb(34, 32, 30), 0.35F)
            Case PhotoFrameStyle.Scroll : PaintScroll(ctx)
            Case PhotoFrameStyle.Deckle : PaintDeckle(ctx)
            Case PhotoFrameStyle.TornPaper : PaintTorn(ctx)
            Case PhotoFrameStyle.Stamp : PaintStamp(ctx)
            Case PhotoFrameStyle.Watercolor, PhotoFrameStyle.Pencil, PhotoFrameStyle.SoftFade, PhotoFrameStyle.InkBrush : PaperGrain(result, 6)
            Case PhotoFrameStyle.Tape, PhotoFrameStyle.PhotoCorners : PaperGrain(result, 5)
        End Select

        ' 2. 照片（陰影、圓角、暈邊）
        Using g = Graphics.FromImage(result)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            Dim shadow = recipe.FrameShadow
            If style = PhotoFrameStyle.Tape OrElse style = PhotoFrameStyle.PhotoCorners OrElse style = PhotoFrameStyle.BlurExtend Then shadow = Math.Max(shadow, 45)
            Dim cornerR = CornerRadius(ctx)
            Dim tapeBorder = If(style = PhotoFrameStyle.Tape, Math.Max(2, CInt(bw * 0.28)), 0)
            If shadow > 0 AndAlso Not MasksPhoto(style) Then DrawShadow(g, ctx, Rectangle.Inflate(photoRect, tapeBorder, tapeBorder), cornerR, shadow)
            If tapeBorder > 0 Then
                Using wb As New SolidBrush(Color.FromArgb(252, 251, 248))
                    g.FillRectangle(wb, Rectangle.Inflate(photoRect, tapeBorder, tapeBorder))
                End Using
            End If
            Select Case style
                Case PhotoFrameStyle.Watercolor : DrawMasked(g, ctx, AddressOf WatercolorMask, edgeDarken:=0.18F)
                Case PhotoFrameStyle.SoftFade : DrawMasked(g, ctx, AddressOf SoftFadeMask, edgeDarken:=0)
                Case PhotoFrameStyle.Deckle
                    Using aged = Sepia(bmp, 0.14F)
                        DrawPhoto(g, aged, photoRect, cornerR)
                    End Using
                Case Else : DrawPhoto(g, bmp, photoRect, cornerR)
            End Select
        End Using

        ' 3. 上面的東西：內框、片孔、相角、膠帶、文字、筆觸
        Using g = Graphics.FromImage(result)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit
            Select Case style
                Case PhotoFrameStyle.Film : PaintFilmHoles(g, ctx)
                Case PhotoFrameStyle.MediumFormat : PaintMediumFormat(g, ctx)
                Case PhotoFrameStyle.GalleryMat : PaintMatBevel(g, ctx)
                Case PhotoFrameStyle.GoldFrame, PhotoFrameStyle.WoodFrame : PaintRebate(g, ctx)
                Case PhotoFrameStyle.PhotoCorners : PaintCorners(g, ctx)
                Case PhotoFrameStyle.Tape : PaintTape(g, ctx)
                Case PhotoFrameStyle.SlideMount : PaintSlide(g, ctx)
                Case PhotoFrameStyle.CameraInfo : PaintCameraInfo(g, ctx)
                Case PhotoFrameStyle.Polaroid : PaintCaption(g, ctx)
                Case PhotoFrameStyle.Stamp : PaintStampText(g, ctx)
            End Select
            If style = PhotoFrameStyle.GalleryMat Then PaintCaption(g, ctx)
            If recipe.FrameInnerLine > 0 AndAlso Not MasksPhoto(style) Then PaintInnerLine(g, ctx)
        End Using
        Select Case style
            Case PhotoFrameStyle.InkBrush : PaintBrushFrame(ctx, BrushKind.ChineseBrush)
            Case PhotoFrameStyle.Pencil : PaintBrushFrame(ctx, BrushKind.Pencil)
        End Select
        Return result
    End Function

    Private NotInheritable Class FrameContext
        Public Recipe As EditRecipe
        Public Photo As Bitmap
        Public Result As Bitmap
        Public PhotoRect As Rectangle
        Public Bw As Integer
        Public Main As Color
        Public Margins As (Left As Integer, Top As Integer, Right As Integer, Bottom As Integer)
    End Class

    Private Shared Function CornerRadius(ctx As FrameContext) As Single
        Dim r = ctx.Recipe
        Dim w = ctx.PhotoRect.Width, h = ctx.PhotoRect.Height
        If r.Frame = PhotoFrameStyle.Rounded AndAlso r.FrameCorner = 0 Then Return Math.Max(4, CInt(Math.Min(w, h) * (0.02 + r.FrameSize / 100.0 * 0.06)))
        If MasksPhoto(r.Frame) OrElse r.Frame = PhotoFrameStyle.Film OrElse r.Frame = PhotoFrameStyle.MediumFormat Then Return 0
        If r.Frame = PhotoFrameStyle.SlideMount AndAlso r.FrameCorner = 0 Then Return Math.Max(2, ctx.Bw * 0.18F)
        Return CSng(Math.Min(w, h) * r.FrameCorner / 100.0 * 0.18)
    End Function

    Private Shared Sub DrawPhoto(g As Graphics, photo As Bitmap, rect As Rectangle, radius As Single)
        If radius < 0.5F Then
            g.DrawImageUnscaled(photo, rect.X, rect.Y)
            Return
        End If
        Using path = RoundedRect(New RectangleF(rect.X, rect.Y, rect.Width, rect.Height), radius), tb As New TextureBrush(photo)
            tb.TranslateTransform(rect.X, rect.Y)
            g.FillPath(tb, path)
        End Using
    End Sub

    Friend Shared Function RoundedRect(r As RectangleF, radius As Single) As GraphicsPath
        Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height))
        Dim p As New GraphicsPath()
        If d < 1 Then
            p.AddRectangle(r)
            Return p
        End If
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    ''' <summary>柔和的陰影：在 1/4 大小的圖上畫好再放大（雙線性放大就是模糊）。</summary>
    Private Shared Sub DrawShadow(g As Graphics, ctx As FrameContext, rect As Rectangle, radius As Single, strength As Integer)
        Dim blur = Math.Max(3, ctx.Bw * 0.45F)
        Dim off = Math.Max(1, ctx.Bw * 0.18F)
        Dim sc = 4.0F
        Dim full = ctx.Result
        Using small As New Bitmap(Math.Max(1, CInt(full.Width / sc)), Math.Max(1, CInt(full.Height / sc)), PixelFormat.Format32bppArgb)
            Using sg = Graphics.FromImage(small)
                sg.SmoothingMode = SmoothingMode.AntiAlias
                Dim a = CInt(Math.Min(255, 40 + strength * 1.6))
                Using br As New SolidBrush(Color.FromArgb(a, 0, 0, 0)), path = RoundedRect(New RectangleF((rect.X + off) / sc, (rect.Y + off * 1.4F) / sc, rect.Width / sc, rect.Height / sc), radius / sc)
                    sg.FillPath(br, path)
                End Using
            End Using
            Dim soft = BoxBlurBitmap(small, Math.Max(1, CInt(blur / sc)))
            Using soft
                Dim old = g.InterpolationMode
                g.InterpolationMode = InterpolationMode.HighQualityBilinear
                g.DrawImage(soft, New Rectangle(0, 0, full.Width, full.Height))
                g.InterpolationMode = old
            End Using
        End Using
    End Sub

    ''' <summary>預乘前的方框模糊（兩次），整張小圖。</summary>
    Private Shared Function BoxBlurBitmap(src As Bitmap, r As Integer) As Bitmap
        Dim w = src.Width, h = src.Height
        Dim px = Perspective.ReadPixels(src)
        For pass = 1 To 2
            Dim tmp(px.Length - 1) As Byte
            ' 橫
            Parallel.For(0, h, Sub(y)
                                   For c = 0 To 3
                                       Dim sum = 0, n = 0
                                       For x = -r To w - 1 + r
                                           If x + r < w Then sum += px(((y * w) + x + r) * 4 + c) : n += 1
                                           If x - r - 1 >= 0 Then sum -= px(((y * w) + x - r - 1) * 4 + c) : n -= 1
                                           If x >= 0 AndAlso x < w Then tmp((y * w + x) * 4 + c) = CByte(sum \ Math.Max(1, n))
                                       Next
                                   Next
                               End Sub)
            ' 直
            Parallel.For(0, w, Sub(x)
                                   For c = 0 To 3
                                       Dim sum = 0, n = 0
                                       For y = -r To h - 1 + r
                                           If y + r < h Then sum += tmp(((y + r) * w + x) * 4 + c) : n += 1
                                           If y - r - 1 >= 0 Then sum -= tmp(((y - r - 1) * w + x) * 4 + c) : n -= 1
                                           If y >= 0 AndAlso y < h Then px((y * w + x) * 4 + c) = CByte(sum \ Math.Max(1, n))
                                       Next
                                   Next
                               End Sub)
        Next
        Dim result As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Perspective.WritePixels(result, px)
        Return result
    End Function

    ''' <summary>照片的主色：縮小後依顏色分組（每色 4 位元），鮮豔的顏色權重高，取最多的那組的平均。</summary>
    Public Shared Function DominantColor(bmp As Bitmap) As Color
        Using small As New Bitmap(48, 48, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(small)
                g.InterpolationMode = InterpolationMode.HighQualityBilinear
                g.DrawImage(bmp, New Rectangle(0, 0, 48, 48))
            End Using
            Dim px = Perspective.ReadPixels(small)
            Dim weight As New Dictionary(Of Integer, Double)(), sums As New Dictionary(Of Integer, (R As Double, G As Double, B As Double, W As Double))()
            For i = 0 To px.Length - 1 Step 4
                If px(i + 3) < 128 Then Continue For
                Dim b0 = px(i), g0 = px(i + 1), r0 = px(i + 2)
                Dim mx = Math.Max(r0, Math.Max(g0, b0)), mn = Math.Min(r0, Math.Min(g0, b0))
                Dim sat = If(mx = 0, 0, (mx - mn) / CDbl(mx))
                Dim wgt = 1 + sat * 2.5
                Dim key = (r0 >> 4) << 8 Or (g0 >> 4) << 4 Or (b0 >> 4)
                Dim s As (R As Double, G As Double, B As Double, W As Double) = Nothing
                sums.TryGetValue(key, s)
                sums(key) = (s.R + r0 * wgt, s.G + g0 * wgt, s.B + b0 * wgt, s.W + wgt)
            Next
            If sums.Count = 0 Then Return Color.White
            Dim best = sums.OrderByDescending(Function(kv) kv.Value.W).First().Value
            Return Color.FromArgb(CInt(best.R / best.W), CInt(best.G / best.W), CInt(best.B / best.W))
        End Using
    End Function

    '=====================================================================
    ' 雜訊（紙紋、撕邊、水彩邊、木紋）
    '=====================================================================

    Private Shared Function Hash(x As Integer, y As Integer, seed As Integer) As Single
        Dim n = CLng(x) * 374761393L + CLng(y) * 668265263L + CLng(seed) * 144665L
        n = (n Xor (n >> 13)) And &HFFFFFFFFL
        n = (n * 1274126177L) And &HFFFFFFFFL
        n = n Xor (n >> 16)
        Return (n And &HFFFFL) / 65535.0F
    End Function

    Private Shared Function Noise(x As Single, y As Single, seed As Integer) As Single
        Dim ix = CInt(Math.Floor(x)), iy = CInt(Math.Floor(y))
        Dim fx = x - ix, fy = y - iy
        fx = fx * fx * (3 - 2 * fx) : fy = fy * fy * (3 - 2 * fy)
        Dim a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed)
        Return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy
    End Function

    Private Shared Function Fbm(x As Single, y As Single, seed As Integer, octaves As Integer) As Single
        Dim sum = 0.0F, amp = 0.5F, f = 1.0F, norm = 0.0F
        For o = 0 To octaves - 1
            sum += Noise(x * f, y * f, seed + o * 101) * amp
            norm += amp : amp *= 0.5F : f *= 2.03F
        Next
        Return sum / norm
    End Function

    ''' <summary>淡淡的紙紋（只在不透明的地方，亮度 ±amount）。</summary>
    Private Shared Sub PaperGrain(bmp As Bitmap, amount As Integer)
        Dim w = bmp.Width, h = bmp.Height
        Dim px = Perspective.ReadPixels(bmp)
        Dim sc = Math.Max(1.0F, Math.Min(w, h) / 700.0F)
        Parallel.For(0, h, Sub(y)
                               For x = 0 To w - 1
                                   Dim i = (y * w + x) * 4
                                   If px(i + 3) = 0 Then Continue For
                                   Dim n = (Fbm(x / (2.2F * sc), y / (2.2F * sc), 77, 3) - 0.5F) * 2 * amount + (Hash(x, y, 5) - 0.5F) * amount * 0.6F
                                   For c = 0 To 2
                                       px(i + c) = CByte(Math.Max(0, Math.Min(255, px(i + c) + n)))
                                   Next
                               Next
                           End Sub)
        Perspective.WritePixels(bmp, px)
    End Sub

    '=====================================================================
    ' 線腳（金框、木框、襯紙外的細框）：橫切面的高度 → 斜面的明暗
    '=====================================================================

    ''' <summary>橫切面：u 從外緣 0 到內緣 1，along 是沿著邊的位置（像素）、thick 是框的寬度；傳回高度（0..1 左右）。</summary>
    Private Delegate Function Profile(u As Single, along As Single, thick As Single) As Single

    Private Shared Function GoldProfile(u As Single, along As Single, thick As Single) As Single
        ' 外緣小圓珠 → 大的凸弧 → 一排珠子 → 平台 → 內緣往下的斜口
        Dim hgt As Single
        If u < 0.08F Then
            hgt = 0.35F + 0.25F * CSng(Math.Sin(u / 0.08F * Math.PI))
        ElseIf u < 0.5F Then
            Dim t = (u - 0.08F) / 0.42F
            hgt = 0.4F + 0.55F * CSng(Math.Sin(t * Math.PI))
        ElseIf u < 0.66F Then
            Dim t = (u - 0.5F) / 0.16F
            Dim beadLen = Math.Max(4.0F, thick * 0.16F)
            Dim pos = (along / beadLen) - CSng(Math.Floor(along / beadLen))
            Dim bead = CSng(Math.Sqrt(Math.Max(0, 1 - ((pos - 0.5F) * 2) ^ 2 - ((t - 0.5F) * 2) ^ 2)))
            hgt = 0.35F + 0.3F * bead
        ElseIf u < 0.86F Then
            hgt = 0.38F + 0.04F * CSng(Math.Sin((u - 0.66F) / 0.2F * Math.PI))
        Else
            Dim t = (u - 0.86F) / 0.14F
            hgt = 0.38F - 0.3F * t
        End If
        Return hgt
    End Function

    Private Shared Function WoodProfile(u As Single, along As Single, thick As Single) As Single
        ' 外緣圓角 → 平面 → 內緣斜面往下
        If u < 0.18F Then Return 0.25F + 0.45F * CSng(Math.Sin(u / 0.18F * Math.PI / 2))
        If u < 0.78F Then Return 0.7F
        Return 0.7F - 0.55F * (u - 0.78F) / 0.22F
    End Function

    Private Shared Function ThinFrameProfile(u As Single, along As Single, thick As Single) As Single
        Return 0.3F + 0.4F * CSng(Math.Sin(u * Math.PI))
    End Function

    ''' <summary>
    ''' 在 outer 與 inner 之間畫一圈框：每個像素看它離哪一邊近（四個角斜切），
    ''' 沿那一邊算橫切面的斜率，光從左上來：朝光的斜面亮、背光暗，再加一點高光。
    ''' </summary>
    Private Shared Sub Moulding(bmp As Bitmap, outer As Rectangle, inner As Rectangle, prof As Profile, baseColor As Color, gloss As Single, Optional wood As Boolean = False)
        Dim w = bmp.Width, h = bmp.Height
        Dim px = Perspective.ReadPixels(bmp)
        Dim lx = -0.70710677F, ly = -0.70710677F
        Dim br = baseColor.R / 255.0F, bg = baseColor.G / 255.0F, bb = baseColor.B / 255.0F
        Parallel.For(Math.Max(0, outer.Top), Math.Min(h, outer.Bottom), Sub(y)
                                                                             For x = Math.Max(0, outer.Left) To Math.Min(w, outer.Right) - 1
                                                                                 If inner.Contains(x, y) Then Continue For
                                                                                 ' 離四個外緣的距離，用各邊的厚度正規化
                                                                                 Dim tT = inner.Top - outer.Top, tB = outer.Bottom - inner.Bottom, tL = inner.Left - outer.Left, tR = outer.Right - inner.Right
                                                                                 Dim dT = (y - outer.Top) / CSng(Math.Max(1, tT)), dB = (outer.Bottom - 1 - y) / CSng(Math.Max(1, tB))
                                                                                 Dim dL = (x - outer.Left) / CSng(Math.Max(1, tL)), dR = (outer.Right - 1 - x) / CSng(Math.Max(1, tR))
                                                                                 Dim side = 0, u = dT, along = CSng(x), thick = CSng(tT), ax = 0.0F, ay = 1.0F
                                                                                 If dB < u Then side = 1 : u = dB : along = x : thick = tB : ax = 0 : ay = -1
                                                                                 If dL < u Then side = 2 : u = dL : along = y : thick = tL : ax = 1 : ay = 0
                                                                                 If dR < u Then side = 3 : u = dR : along = y : thick = tR : ax = -1 : ay = 0
                                                                                 u = Math.Max(0, Math.Min(0.999F, u))
                                                                                 Dim du = 1.0F / Math.Max(2.0F, thick)
                                                                                 Dim h0 = prof(Math.Max(0, u - du), along, thick), h1 = prof(Math.Min(0.999F, u + du), along, thick)
                                                                                 ' 高度變化換成表面的傾斜（橫切面高度約是框寬的 0.35 倍），再化成單位法線，明暗才不會爆掉
                                                                                 Dim slope = (h1 - h0) / (2 * du) * 0.35F
                                                                                 Dim tilt = slope / CSng(Math.Sqrt(1 + slope * slope))
                                                                                 ' 表面朝向（往內是 +u）：-tilt × 往內方向
                                                                                 Dim nx = -tilt * ax, ny = -tilt * ay
                                                                                 Dim lit = -(nx * lx + ny * ly)
                                                                                 Dim shade = 0.86F + 0.75F * lit
                                                                                 Dim spec = If(lit > 0, CSng(Math.Pow(Math.Min(1, lit), 6)) * gloss, 0)
                                                                                 Dim cr = br, cg = bg, cb = bb
                                                                                 If wood Then
                                                                                     ' 木紋順著邊走：年輪線＋細紋
                                                                                     Dim across = u * thick
                                                                                     Dim grain = CSng(Math.Sin((across * 0.55F + Fbm(along / 90.0F, across / 9.0F, side * 31 + 7, 3) * 14) * 1.3F))
                                                                                     Dim fine = Fbm(along / 4.0F, across / 0.8F, side * 13 + 3, 2)
                                                                                     Dim k = 0.78F + 0.16F * grain + 0.12F * (fine - 0.5F)
                                                                                     cr *= k : cg *= k : cb *= k
                                                                                 Else
                                                                                     Dim k = 0.96F + 0.08F * (Fbm(along / 30.0F, u * 6, side + 1, 2) - 0.5F)
                                                                                     cr *= k : cg *= k : cb *= k
                                                                                 End If
                                                                                 ' 外緣、斜切的角線稍暗
                                                                                 Dim miter = Math.Abs(Math.Min(dT, dB) - Math.Min(dL, dR))
                                                                                 If miter < 0.012F Then shade *= 0.82F
                                                                                 Dim i = (y * w + x) * 4
                                                                                 px(i + 2) = ToByte((cr * shade + spec) * 255)
                                                                                 px(i + 1) = ToByte((cg * shade + spec * 0.92F) * 255)
                                                                                 px(i) = ToByte((cb * shade + spec * 0.75F) * 255)
                                                                                 px(i + 3) = 255
                                                                             Next
                                                                         End Sub)
        Perspective.WritePixels(bmp, px)
    End Sub

    Private Shared Function ToByte(v As Single) As Byte
        Return CByte(Math.Max(0, Math.Min(255, Math.Round(v))))
    End Function

    ''' <summary>金框、木框內緣：照片四周一圈很窄的暗口（框和畫之間的縫）。</summary>
    Private Shared Sub PaintRebate(g As Graphics, ctx As FrameContext)
        Dim r = ctx.PhotoRect
        Dim t = Math.Max(1.0F, ctx.Bw * 0.06F)
        Using p As New Pen(Color.FromArgb(150, 20, 14, 8), t)
            g.DrawRectangle(p, r.X - t / 2, r.Y - t / 2, r.Width + t, r.Height + t)
        End Using
        ' 上、左兩邊的內側落下陰影（框比畫高）
        Dim sh = Math.Max(2, CInt(ctx.Bw * 0.18F))
        Using lg As New LinearGradientBrush(New Rectangle(r.X, r.Y, r.Width, sh), Color.FromArgb(90, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical)
            g.FillRectangle(lg, r.X, r.Y, r.Width, sh)
        End Using
        Using lg As New LinearGradientBrush(New Rectangle(r.X, r.Y, sh, r.Height), Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Horizontal)
            g.FillRectangle(lg, r.X, r.Y, sh, r.Height)
        End Using
    End Sub

    '=====================================================================
    ' 各樣式
    '=====================================================================

    Private Shared Sub PaintFilmHoles(g As Graphics, ctx As FrameContext)
        Dim t = ctx.Margins.Top, btm = ctx.Margins.Bottom, full = ctx.Result
        Dim h = ctx.PhotoRect.Height
        Dim holeH = t * 0.42F, holeW = holeH * 1.35F, gap = holeW * 0.9F
        Using br As New SolidBrush(Color.FromArgb(225, 225, 220))
            Dim x = gap / 2
            While x + holeW < full.Width
                For Each y In {(t - holeH) / 2, h + t + (btm - holeH) / 2}
                    Using hole = RoundedRect(New RectangleF(x, y, holeW, holeH), holeH * 0.25F)
                        g.FillPath(br, hole)
                    End Using
                Next
                x += holeW + gap
            End While
        End Using
        EdgeText(g, ctx, ctx.Recipe.FrameText, New RectangleF(0, h + t + btm * 0.02F, full.Width, btm * 0.3F), Color.FromArgb(230, 160, 60))
    End Sub

    ''' <summary>底片邊的字（橘色細字，靠左）。</summary>
    Private Shared Sub EdgeText(g As Graphics, ctx As FrameContext, text As String, area As RectangleF, col As Color)
        If String.IsNullOrWhiteSpace(text) OrElse area.Height < 5 Then Return
        Using f As New Font(MonoFont(), Math.Max(4.0F, area.Height * 0.62F), FontStyle.Bold, GraphicsUnit.Pixel), br As New SolidBrush(col)
            g.DrawString(text.Replace(vbCr, "").Replace(vbLf, "    "), f, br, area.X + ctx.Bw * 0.8F, area.Y)
        End Using
    End Sub

    Private Shared Function MonoFont() As String
        Return If(FontInstalled("Consolas"), "Consolas", "Courier New")
    End Function

    Private Shared Function FontInstalled(name As String) As Boolean
        Using f As New Font(name, 10)
            Return String.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)
        End Using
    End Function

    Private Shared Sub PaintMediumFormat(g As Graphics, ctx As FrameContext)
        Dim r = ctx.PhotoRect, full = ctx.Result, bw = ctx.Bw
        ' 片框邊緣不規則的亮邊（光從片框漏進來的一點點）與左右的缺口
        Using p As New Pen(Color.FromArgb(60, 255, 250, 235), Math.Max(1.0F, bw * 0.05F))
            g.DrawRectangle(p, r)
        End Using
        Dim notchW = Math.Max(3, CInt(bw * 0.35)), notchH = Math.Max(3, CInt(bw * 0.22))
        Using br As New SolidBrush(Color.FromArgb(70, 70, 66))
            For Each y In {r.Y + r.Height * 0.22F, r.Y + r.Height * 0.62F}
                g.FillRectangle(br, r.X - notchW, y, notchW, notchH)
            Next
            g.FillPolygon(br, {New PointF(r.Right, r.Y + r.Height * 0.4F), New PointF(r.Right + notchW, r.Y + r.Height * 0.4F - notchH), New PointF(r.Right + notchW, r.Y + r.Height * 0.4F + notchH)})
        End Using
        Dim text = If(String.IsNullOrWhiteSpace(ctx.Recipe.FrameText), "120", ctx.Recipe.FrameText)
        EdgeText(g, ctx, text, New RectangleF(0, ctx.Margins.Top * 0.18F, full.Width, ctx.Margins.Top * 0.45F), Color.FromArgb(220, 140, 50))
    End Sub

    Private Shared Sub PaintBlurBackground(g As Graphics, ctx As FrameContext)
        Dim full = ctx.Result, photo = ctx.Photo
        ' 照片放大到蓋滿整張，縮得很小再放大＝很糊
        ' 先鋪照片主色、背景畫得比整張大一點：放大時邊上的內插會混到透明，角落不會變半透明
        g.Clear(DominantColor(photo))
        Dim sc = Math.Max(full.Width / CDbl(photo.Width), full.Height / CDbl(photo.Height)) * 1.06
        Dim cw = CInt(photo.Width * sc), ch = CInt(photo.Height * sc)
        Dim tiny = Math.Max(24, Math.Min(full.Width, full.Height) \ 12)
        Using small As New Bitmap(Math.Max(2, cw * tiny \ Math.Max(cw, ch)), Math.Max(2, ch * tiny \ Math.Max(cw, ch)), PixelFormat.Format32bppArgb)
            Using sg = Graphics.FromImage(small)
                sg.InterpolationMode = InterpolationMode.HighQualityBilinear
                sg.DrawImage(photo, New Rectangle(0, 0, small.Width, small.Height))
            End Using
            Using soft = BoxBlurBitmap(small, 2)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                Using ia As New ImageAttributes()
                    ia.SetWrapMode(WrapMode.TileFlipXY)
                    g.DrawImage(soft, New Rectangle((full.Width - cw) \ 2, (full.Height - ch) \ 2, cw, ch), 0, 0, soft.Width, soft.Height, GraphicsUnit.Pixel, ia)
                End Using
            End Using
        End Using
        ' 稍微壓暗，照片才跳出來
        Using br As New SolidBrush(Color.FromArgb(60, 0, 0, 0))
            g.FillRectangle(br, 0, 0, full.Width, full.Height)
        End Using
    End Sub

    Private Shared Sub PaintMatBevel(g As Graphics, ctx As FrameContext)
        ' 襯紙開口的 45° 斜切面：白色的紙芯，上、左受光亮、下、右暗一點
        Dim r = ctx.PhotoRect
        Dim t = Math.Max(2, CInt(ctx.Bw * 0.16F))
        Dim outer = Rectangle.Inflate(r, t, t)
        Dim pts = Function(a As PointF, b As PointF, c As PointF, d As PointF) New PointF() {a, b, c, d}
        Dim tl = New PointF(outer.Left, outer.Top), tr = New PointF(outer.Right, outer.Top), bl = New PointF(outer.Left, outer.Bottom), brr = New PointF(outer.Right, outer.Bottom)
        Dim itl = New PointF(r.Left, r.Top), itr = New PointF(r.Right, r.Top), ibl = New PointF(r.Left, r.Bottom), ibr = New PointF(r.Right, r.Bottom)
        Using lit As New SolidBrush(Color.FromArgb(253, 252, 249)), mid As New SolidBrush(Color.FromArgb(238, 236, 230)), dark As New SolidBrush(Color.FromArgb(222, 219, 211))
            g.FillPolygon(lit, pts(tl, tr, itr, itl))
            g.FillPolygon(mid, pts(tl, itl, ibl, bl))
            g.FillPolygon(dark, pts(bl, ibl, ibr, brr))
            g.FillPolygon(mid, pts(tr, brr, ibr, itr))
        End Using
        Using p As New Pen(Color.FromArgb(70, 0, 0, 0), 1)
            g.DrawRectangle(p, outer)
        End Using
    End Sub

    Private Shared Sub PaintCorners(g As Graphics, ctx As FrameContext)
        ' 相簿的黑色相角：三角形的紙套住照片四個角，上面一點點反光
        Dim r = ctx.PhotoRect
        Dim leg = Math.Max(8.0F, Math.Min(r.Width, r.Height) * 0.16F)
        Dim out = Math.Max(1.5F, ctx.Bw * 0.08F)
        Dim corners = {(New PointF(r.Left - out, r.Top - out), 1, 1), (New PointF(r.Right + out, r.Top - out), -1, 1),
                       (New PointF(r.Left - out, r.Bottom + out), 1, -1), (New PointF(r.Right + out, r.Bottom + out), -1, -1)}
        For Each c In corners
            Dim p0 = c.Item1, sx = c.Item2, sy = c.Item3
            Dim tri = {p0, New PointF(p0.X + sx * leg, p0.Y), New PointF(p0.X, p0.Y + sy * leg)}
            Using sh As New SolidBrush(Color.FromArgb(70, 0, 0, 0))
                g.FillPolygon(sh, tri.Select(Function(p) New PointF(p.X + out, p.Y + out * 1.4F)).ToArray())
            End Using
            Using lg As New LinearGradientBrush(New RectangleF(Math.Min(p0.X, p0.X + sx * leg) - 1, Math.Min(p0.Y, p0.Y + sy * leg) - 1, leg + 2, leg + 2),
                                                Color.FromArgb(70, 70, 72), Color.FromArgb(16, 16, 18), 45.0F)
                g.FillPolygon(lg, tri)
            End Using
            Using hl As New Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1.0F, out * 0.6F))
                g.DrawLine(hl, New PointF(p0.X + sx * leg * 0.92F, p0.Y + sy * out * 0.3F), New PointF(p0.X + sx * out * 0.3F, p0.Y + sy * leg * 0.92F))
            End Using
        Next
    End Sub

    Private Shared Sub PaintTape(g As Graphics, ctx As FrameContext)
        ' 兩段半透明的紙膠帶斜斜貼住上面兩個角，兩端是撕開的鋸齒
        Dim r = ctx.PhotoRect
        Dim len = Math.Max(20.0F, Math.Min(r.Width, r.Height) * 0.22F), wid = Math.Max(8.0F, len * 0.32F)
        Dim col = Color.FromArgb(165, 236, 222, 178)
        For Each c In {(New PointF(r.Left, r.Top), -45.0F), (New PointF(r.Right, r.Top), 45.0F)}
            Dim st = g.Save()
            g.TranslateTransform(c.Item1.X, c.Item1.Y)
            g.RotateTransform(c.Item2)
            Dim path As New GraphicsPath()
            Dim teeth = 7
            Dim pts As New List(Of PointF)()
            For k = 0 To teeth
                pts.Add(New PointF(-len / 2 + If(k Mod 2 = 0, 0, wid * 0.06F), -wid / 2 + k * wid / teeth))
            Next
            For k = teeth To 0 Step -1
                pts.Add(New PointF(len / 2 - If(k Mod 2 = 0, 0, wid * 0.06F), -wid / 2 + k * wid / teeth))
            Next
            path.AddPolygon(pts.ToArray())
            Using br As New SolidBrush(col)
                g.FillPath(br, path)
            End Using
            Using p As New Pen(Color.FromArgb(40, 120, 100, 60), 1)
                g.DrawPath(p, path)
            End Using
            path.Dispose()
            g.Restore(st)
        Next
    End Sub

    Private Shared Sub PaintSlide(g As Graphics, ctx As FrameContext)
        ' 幻燈片的紙框：外圍圓角（外面透明）、開口一圈壓痕、下方印字
        Dim full = ctx.Result, r = ctx.PhotoRect, bw = ctx.Bw
        Dim radius = Math.Max(3.0F, bw * 0.55F)
        Using outer = RoundedRect(New RectangleF(0, 0, full.Width, full.Height), radius)
            Using rg As New Region(New Rectangle(0, 0, full.Width, full.Height))
                rg.Exclude(outer)
                Dim old = g.CompositingMode
                g.CompositingMode = CompositingMode.SourceCopy
                Using clear As New SolidBrush(Color.FromArgb(0, 0, 0, 0))
                    g.FillRegion(clear, rg)
                End Using
                g.CompositingMode = old
            End Using
        End Using
        Dim t = Math.Max(1.5F, bw * 0.12F)
        Using p As New Pen(Color.FromArgb(60, 0, 0, 0), t), hl As New Pen(Color.FromArgb(160, 255, 255, 255), t * 0.6F)
            Using path = RoundedRect(New RectangleF(r.X - t, r.Y - t, r.Width + 2 * t, r.Height + 2 * t), CornerRadius(ctx) + t)
                g.DrawPath(p, path)
            End Using
            Using path = RoundedRect(New RectangleF(r.X - t * 2.2F, r.Y - t * 2.2F, r.Width + 4.4F * t, r.Height + 4.4F * t), CornerRadius(ctx) + t * 2)
                g.DrawPath(hl, path)
            End Using
        End Using
        Dim text = If(String.IsNullOrWhiteSpace(ctx.Recipe.FrameText), "24×36", ctx.Recipe.FrameText)
        Using f As New Font(MonoFont(), Math.Max(5.0F, bw * 0.38F), FontStyle.Regular, GraphicsUnit.Pixel), br As New SolidBrush(Color.FromArgb(120, 110, 100))
            Dim sz = g.MeasureString(text, f)
            g.DrawString(text, f, br, (full.Width - sz.Width) / 2, r.Bottom + (ctx.Margins.Bottom - sz.Height) / 2)
        End Using
    End Sub

    ''' <summary>相機資訊：下方白條，左邊相機與鏡頭、右邊拍攝參數與日期（第 1 行相機、第 2 行鏡頭、第 3 行參數、第 4 行日期）。</summary>
    Private Shared Sub PaintCameraInfo(g As Graphics, ctx As FrameContext)
        Dim full = ctx.Result, r = ctx.PhotoRect
        Dim bar = New RectangleF(0, r.Bottom, full.Width, full.Height - r.Bottom)
        Dim lines = If(ctx.Recipe.FrameText, "").Replace(vbCr, "").Split(ChrW(10))
        Dim line = Function(k As Integer) If(k < lines.Length, lines(k).Trim(), "")
        Dim ink = If(Luma(ctx.Main) > 128, Color.FromArgb(30, 30, 32), Color.FromArgb(240, 240, 238))
        Dim soft = If(Luma(ctx.Main) > 128, Color.FromArgb(120, 120, 124), Color.FromArgb(170, 170, 172))
        Dim pad = ctx.Margins.Left * 2.0F + bar.Height * 0.25F
        Dim bigSize = Math.Max(5.0F, bar.Height * 0.22F), smallSize = Math.Max(4.0F, bar.Height * 0.15F)
        Dim family = Lang.UiFont
        Using big As New Font(family, bigSize, FontStyle.Bold, GraphicsUnit.Pixel), small As New Font(family, smallSize, FontStyle.Regular, GraphicsUnit.Pixel),
              inkBr As New SolidBrush(ink), softBr As New SolidBrush(soft)
            Dim cy = bar.Y + bar.Height / 2
            ' 左：相機（粗）、鏡頭（細）
            Dim l1 = line(0), l2 = line(1)
            g.DrawString(l1, big, inkBr, pad, cy - bigSize * 1.1F)
            g.DrawString(l2, small, softBr, pad, cy + smallSize * 0.15F)
            ' 右：參數（粗）、日期（細），靠右；中間一條細直線
            Dim r1 = line(2), r2 = line(3)
            Dim w1 = g.MeasureString(r1, big).Width, w2 = g.MeasureString(r2, small).Width
            Dim rightEdge = full.Width - pad
            Dim textLeft = rightEdge - Math.Max(w1, w2)
            g.DrawString(r1, big, inkBr, rightEdge - w1, cy - bigSize * 1.1F)
            g.DrawString(r2, small, softBr, rightEdge - w2, cy + smallSize * 0.15F)
            If r1 <> "" OrElse r2 <> "" Then
                Using p As New Pen(Color.FromArgb(80, soft), Math.Max(1.0F, bar.Height * 0.012F))
                    g.DrawLine(p, textLeft - bar.Height * 0.2F, cy - bigSize, textLeft - bar.Height * 0.2F, cy + smallSize * 1.2F)
                End Using
            End If
        End Using
    End Sub

    Private Shared Function Luma(c As Color) As Double
        Return 0.3 * c.R + 0.59 * c.G + 0.11 * c.B
    End Function

    ''' <summary>拍立得、畫廊襯紙下方的手寫字（拍立得置中、襯紙靠右用鉛筆灰）。</summary>
    Private Shared Sub PaintCaption(g As Graphics, ctx As FrameContext)
        Dim text = If(ctx.Recipe.FrameText, "").Trim()
        If text = "" Then Return
        Dim full = ctx.Result, r = ctx.PhotoRect
        Dim area = New RectangleF(0, r.Bottom, full.Width, full.Height - r.Bottom)
        Dim gallery = ctx.Recipe.Frame = PhotoFrameStyle.GalleryMat
        Dim family = If(FontInstalled("Segoe Print"), "Segoe Print", Lang.UiFont)
        Dim size = Math.Max(5.0F, If(gallery, ctx.Bw * 0.32F, area.Height * 0.22F))
        Dim col = If(gallery, Color.FromArgb(130, 125, 118), If(Luma(ctx.Main) > 128, Color.FromArgb(40, 40, 70), Color.FromArgb(235, 235, 235)))
        Using f As New Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel), br As New SolidBrush(col)
            Dim one = text.Replace(vbCr, "").Replace(vbLf, "  ")
            Dim sz = g.MeasureString(one, f)
            If gallery Then
                g.DrawString(one, f, br, r.Right - sz.Width, r.Bottom + ctx.Bw * 0.45F)
            Else
                g.DrawString(one, f, br, (full.Width - sz.Width) / 2, area.Y + (area.Height - sz.Height) / 2)
            End If
        End Using
    End Sub

    Private Shared Sub PaintInnerLine(g As Graphics, ctx As FrameContext)
        Dim r = ctx.PhotoRect
        Dim t = Math.Max(1.0F, ctx.Bw * ctx.Recipe.FrameInnerLine / 100.0F * 0.22F)
        Dim gap = t * 1.6F
        Dim col = If(Luma(ctx.Main) > 128, Color.FromArgb(70, 68, 64), Color.FromArgb(225, 222, 214))
        Using p As New Pen(col, t)
            Using path = RoundedRect(New RectangleF(r.X - gap - t / 2, r.Y - gap - t / 2, r.Width + 2 * gap + t, r.Height + 2 * gap + t), CornerRadius(ctx) + gap)
                g.DrawPath(p, path)
            End Using
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 卷軸裱褙
    '---------------------------------------------------------------------

    Private Shared Sub PaintScroll(ctx As FrameContext)
        Dim full = ctx.Result, r = ctx.PhotoRect, s = ScrollParts(ctx.Bw)
        Dim silk = ctx.Main
        Dim brocade = Color.FromArgb(Math.Min(255, CInt(silk.R * 0.86)), Math.Min(255, CInt(silk.G * 0.74)), Math.Min(255, CInt(silk.B * 0.5)))
        Dim silkRect = New Rectangle(s.Knob, s.Cord + s.Bar, full.Width - 2 * s.Knob, full.Height - s.Cord - s.Bar - s.Roller)
        Using g = Graphics.FromImage(full)
            g.SmoothingMode = SmoothingMode.AntiAlias
            ' 綾（整片）、天頭與地頭稍深
            Using br As New SolidBrush(silk)
                g.FillRectangle(br, silkRect)
            End Using
            Dim headCol = Color.FromArgb(CInt(silk.R * 0.9), CInt(silk.G * 0.92), CInt(silk.B * 0.97))
            Using br As New SolidBrush(headCol)
                g.FillRectangle(br, silkRect.X, silkRect.Y, silkRect.Width, r.Top - silkRect.Y - s.Side \ 3)
                g.FillRectangle(br, silkRect.X, r.Bottom + s.Side \ 3, silkRect.Width, silkRect.Bottom - r.Bottom - s.Side \ 3)
            End Using
            ' 畫心外一圈錦（隔水）
            Dim rim = Math.Max(2, s.Side \ 3)
            Using br As New SolidBrush(brocade)
                g.FillRectangle(br, Rectangle.Inflate(r, rim, rim))
            End Using
            ' 天頭兩條驚燕
            Dim strip = Math.Max(2, CInt(ctx.Bw * 0.16))
            Using br As New SolidBrush(brocade)
                For Each fx In {0.32, 0.68}
                    g.FillRectangle(br, CInt(silkRect.X + silkRect.Width * fx - strip / 2.0), silkRect.Y, strip, r.Top - rim - silkRect.Y - s.Side \ 3)
                Next
            End Using
            ' 天杆
            Dim barRect = New RectangleF(silkRect.X - s.Knob * 0.2F, s.Cord, silkRect.Width + s.Knob * 0.4F, s.Bar)
            Using lg As New LinearGradientBrush(barRect, Color.FromArgb(120, 82, 50), Color.FromArgb(52, 32, 18), LinearGradientMode.Vertical)
                g.FillRectangle(lg, barRect)
            End Using
            ' 掛繩
            Using p As New Pen(Color.FromArgb(150, 120, 70), Math.Max(1.0F, ctx.Bw * 0.06F))
                Dim cx = full.Width / 2.0F
                g.DrawLines(p, {New PointF(silkRect.X + silkRect.Width * 0.3F, s.Cord), New PointF(cx, s.Cord * 0.25F), New PointF(silkRect.X + silkRect.Width * 0.7F, s.Cord)})
                g.DrawEllipse(p, cx - s.Cord * 0.12F, s.Cord * 0.02F, s.Cord * 0.24F, s.Cord * 0.24F)
            End Using
            ' 地軸：圓柱的明暗，兩端軸頭伸出來
            Dim rollerY = silkRect.Bottom
            Dim roll = New RectangleF(silkRect.X, rollerY, silkRect.Width, s.Roller)
            Using lg As New LinearGradientBrush(roll, Color.FromArgb(70, 44, 26), Color.FromArgb(70, 44, 26), LinearGradientMode.Vertical)
                lg.InterpolationColors = New ColorBlend With {.Colors = {Color.FromArgb(60, 38, 22), Color.FromArgb(150, 108, 70), Color.FromArgb(40, 24, 14)}, .Positions = {0, 0.35F, 1}}
                g.FillRectangle(lg, roll)
            End Using
            For Each kx In {0.0F, CSng(full.Width - s.Knob)}
                Dim knob = New RectangleF(kx, rollerY - s.Roller * 0.08F, s.Knob, s.Roller * 1.16F)
                Using lg As New LinearGradientBrush(knob, Color.White, Color.White, LinearGradientMode.Vertical)
                    lg.InterpolationColors = New ColorBlend With {.Colors = {Color.FromArgb(150, 160, 140), Color.FromArgb(225, 230, 215), Color.FromArgb(110, 118, 100)}, .Positions = {0, 0.4F, 1}}
                    Using path = RoundedRect(knob, s.Knob * 0.25F)
                        g.FillPath(lg, path)
                    End Using
                End Using
            Next
        End Using
        PaperGrain(full, 4)
    End Sub

    '---------------------------------------------------------------------
    ' 外面透明的邊：花邊、撕紙、郵票
    '---------------------------------------------------------------------

    ''' <summary>老照片的花邊：一圈米白的紙，外緣是一個個小圓弧（外面透明），紙色略帶泛黃。</summary>
    Private Shared Sub PaintDeckle(ctx As FrameContext)
        Dim full = ctx.Result
        Dim rad = Math.Max(3.0F, ctx.Bw * 0.34F)
        Using g = Graphics.FromImage(full), path = ScallopPath(New RectangleF(rad, rad, full.Width - 2 * rad, full.Height - 2 * rad), rad)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using br As New SolidBrush(ctx.Main)
                g.FillPath(br, path)
            End Using
            ' 邊緣稍微泛黃
            Using pb As New PathGradientBrush(New PointF() {New PointF(0, 0), New PointF(full.Width, 0), New PointF(full.Width, full.Height), New PointF(0, full.Height)})
                pb.CenterColor = Color.FromArgb(0, 210, 180, 120)
                pb.SurroundColors = {Color.FromArgb(70, 200, 165, 100)}
                g.SetClip(path)
                g.FillRectangle(pb, 0, 0, full.Width, full.Height)
                g.ResetClip()
            End Using
        End Using
        PaperGrain(full, 4)
    End Sub

    ''' <summary>矩形外緣換成一個個向外凸的半圓（花邊剪刀剪出來的樣子）。</summary>
    Private Shared Function ScallopPath(r As RectangleF, rad As Single) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim nx = Math.Max(2, CInt(r.Width / (rad * 2))), ny = Math.Max(2, CInt(r.Height / (rad * 2)))
        Dim sx = r.Width / nx, sy = r.Height / ny
        For i = 0 To nx - 1
            p.AddArc(r.X + i * sx, r.Y - sy * 0 - rad, sx, rad * 2, 180, 180)
        Next
        For i = 0 To ny - 1
            p.AddArc(r.Right - rad, r.Y + i * sy, rad * 2, sy, 270, 180)
        Next
        For i = nx - 1 To 0 Step -1
            p.AddArc(r.X + i * sx, r.Bottom - rad, sx, rad * 2, 0, 180)
        Next
        For i = ny - 1 To 0 Step -1
            p.AddArc(r.X - rad, r.Y + i * sy, rad * 2, sy, 90, 180)
        Next
        p.CloseFigure()
        Return p
    End Function

    ''' <summary>撕紙：紙的外緣沿著雜訊忽進忽出，邊上一圈較亮的纖維，下方一點陰影；外面透明。</summary>
    Private Shared Sub PaintTorn(ctx As FrameContext)
        Dim full = ctx.Result, bw = ctx.Bw
        Dim inset = bw * 0.45F, amp = bw * 0.28F
        Dim pts As New List(Of PointF)()
        Dim w = full.Width, h = full.Height
        Dim stepPx = Math.Max(1.5F, bw * 0.05F)
        Dim per = 2 * (w + h)
        Dim walk = Function(t As Single) As (P As PointF, Nx As Single, Ny As Single)
                       ' 繞矩形一圈（順時針），回傳點與往外的方向
                       Dim iw = w - 2 * inset, ih = h - 2 * inset
                       Dim d = t * (2 * (iw + ih))
                       If d < iw Then Return (New PointF(inset + d, inset), 0, -1)
                       d -= iw
                       If d < ih Then Return (New PointF(w - inset, inset + d), 1, 0)
                       d -= ih
                       If d < iw Then Return (New PointF(w - inset - d, h - inset), 0, 1)
                       d -= iw
                       Return (New PointF(inset, h - inset - d), -1, 0)
                   End Function
        Dim count = CInt(per / stepPx)
        For k = 0 To count - 1
            Dim t = k / CSng(count)
            Dim q = walk(t)
            Dim n = (Fbm(k * stepPx / (bw * 0.9F), 0.5F, 11, 4) - 0.5F) * 2 * amp + (Hash(k, 3, 9) - 0.5F) * amp * 0.25F
            pts.Add(New PointF(q.P.X + q.Nx * n, q.P.Y + q.Ny * n))
        Next
        Using g = Graphics.FromImage(full)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using path As New GraphicsPath()
                path.AddPolygon(pts.ToArray())
                ' 陰影
                Dim st = g.Save()
                g.TranslateTransform(bw * 0.08F, bw * 0.14F)
                Using sh As New SolidBrush(Color.FromArgb(60, 0, 0, 0))
                    g.FillPath(sh, path)
                End Using
                g.Restore(st)
                ' 纖維：亮一點的毛邊
                Using fib As New Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1.5F, bw * 0.09F))
                    g.DrawPath(fib, path)
                End Using
                Using br As New SolidBrush(ctx.Main)
                    g.FillPath(br, path)
                End Using
            End Using
        End Using
        PaperGrain(full, 4)
    End Sub

    ''' <summary>郵票：白邊外緣一排半圓的齒孔（透明），裡面一條細線。</summary>
    Private Shared Sub PaintStamp(ctx As FrameContext)
        Dim full = ctx.Result, bw = ctx.Bw
        Dim hole = Math.Max(2.0F, bw * 0.17F)
        Using g = Graphics.FromImage(full)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using br As New SolidBrush(ctx.Main)
                g.FillRectangle(br, 0, 0, full.Width, full.Height)
            End Using
            g.CompositingMode = CompositingMode.SourceCopy
            Using clear As New SolidBrush(Color.FromArgb(0, 0, 0, 0))
                Dim gapX = hole * 3.1F
                Dim nx = Math.Max(2, CInt(full.Width / gapX)), ny = Math.Max(2, CInt(full.Height / gapX))
                For i = 0 To nx
                    Dim x = i * full.Width / CSng(nx)
                    g.FillEllipse(clear, x - hole, -hole, hole * 2, hole * 2)
                    g.FillEllipse(clear, x - hole, full.Height - hole, hole * 2, hole * 2)
                Next
                For i = 0 To ny
                    Dim y = i * full.Height / CSng(ny)
                    g.FillEllipse(clear, -hole, y - hole, hole * 2, hole * 2)
                    g.FillEllipse(clear, full.Width - hole, y - hole, hole * 2, hole * 2)
                Next
            End Using
            g.CompositingMode = CompositingMode.SourceOver
        End Using
        PaperGrain(full, 3)
    End Sub

    Private Shared Sub PaintStampText(g As Graphics, ctx As FrameContext)
        Dim r = ctx.PhotoRect
        Using p As New Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(1.0F, ctx.Bw * 0.04F))
            g.DrawRectangle(p, r)
        End Using
        Dim value = If(String.IsNullOrWhiteSpace(ctx.Recipe.FrameText), "5", ctx.Recipe.FrameText.Replace(vbCr, "").Split(ChrW(10))(0).Trim())
        Dim size = Math.Max(6.0F, Math.Min(r.Width, r.Height) * 0.16F)
        Using f As New Font("Georgia", size, FontStyle.Bold, GraphicsUnit.Pixel), small As New Font("Georgia", size * 0.32F, FontStyle.Bold, GraphicsUnit.Pixel),
              path As New GraphicsPath()
            Dim sz = g.MeasureString(value, f)
            Dim x = r.Right - sz.Width - size * 0.15F, y = r.Bottom - sz.Height - size * 0.05F
            path.AddString(value, f.FontFamily, CInt(FontStyle.Bold), size, New PointF(x, y), StringFormat.GenericDefault)
            path.AddString("POSTAGE", small.FontFamily, CInt(FontStyle.Bold), size * 0.32F, New PointF(r.X + size * 0.25F, r.Y + size * 0.2F), StringFormat.GenericDefault)
            Using outline As New Pen(Color.FromArgb(180, 0, 0, 0), Math.Max(1.0F, size * 0.08F)) With {.LineJoin = LineJoin.Round}
                g.DrawPath(outline, path)
            End Using
            Using br As New SolidBrush(Color.White)
                g.FillPath(br, path)
            End Using
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 照片的邊緣：水彩、柔邊
    '---------------------------------------------------------------------

    ''' <summary>照片每個像素的不透明度（0..1）與離邊緣多近（0 = 邊上，1 = 遠）。</summary>
    Private Delegate Function PhotoMask(x As Integer, y As Integer, w As Integer, h As Integer, bw As Integer) As (Alpha As Single, Edge As Single)

    Private Shared Function WatercolorMask(x As Integer, y As Integer, w As Integer, h As Integer, bw As Integer) As (Alpha As Single, Edge As Single)
        Dim d = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y))
        Dim sc = Math.Max(6.0F, bw * 1.6F)
        Dim wob = (Fbm(x / sc, y / sc, 21, 3) - 0.5F) * 2 * bw * 1.1F + (Fbm(x / (sc * 0.3F), y / (sc * 0.3F), 23, 2) - 0.5F) * bw * 0.12F
        Dim v = d - bw * 0.7F + wob
        Dim feather = Math.Max(2.0F, bw * 0.16F)
        Dim core = Math.Max(0, Math.Min(1, v / feather))
        ' 外面淡淡滲出去的一圈（顏料在濕紙上暈開）
        Dim bleed = Math.Max(0, Math.Min(1, (v + bw * 0.45F) / (bw * 0.45F))) * 0.28F
        Dim edge = Math.Max(0, Math.Min(1, v / (bw * 0.18F)))
        Return (Math.Max(core, bleed), If(core > bleed, edge, 1))
    End Function

    Private Shared Function SoftFadeMask(x As Integer, y As Integer, w As Integer, h As Integer, bw As Integer) As (Alpha As Single, Edge As Single)
        Dim d = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y))
        Dim fadeW = Math.Max(4.0F, bw * 1.6F)
        Dim t = Math.Max(0, Math.Min(1, d / fadeW))
        Return (t * t * (3 - 2 * t), 1)
    End Function

    ''' <summary>照片依遮罩畫上去；水彩邊的顏料積在邊緣（邊緣稍暗、略帶顆粒）。</summary>
    Private Shared Sub DrawMasked(g As Graphics, ctx As FrameContext, mask As PhotoMask, edgeDarken As Single)
        Dim photo = ctx.Photo
        Dim w = photo.Width, h = photo.Height, bw = ctx.Bw
        Dim px = Perspective.ReadPixels(photo)
        Parallel.For(0, h, Sub(y)
                               For x = 0 To w - 1
                                   Dim i = (y * w + x) * 4
                                   Dim mk = mask(x, y, w, h, bw)
                                   If edgeDarken > 0 AndAlso mk.Alpha > 0 Then
                                       Dim k = 1 - edgeDarken * (1 - mk.Edge) * (0.75F + 0.5F * Hash(x \ 2, y \ 2, 41))
                                       For c = 0 To 2
                                           px(i + c) = CByte(Math.Max(0, Math.Min(255, px(i + c) * k)))
                                       Next
                                   End If
                                   px(i + 3) = CByte(Math.Round(px(i + 3) * mk.Alpha))
                               Next
                           End Sub)
        Using masked As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Perspective.WritePixels(masked, px)
            g.DrawImageUnscaled(masked, ctx.PhotoRect.X, ctx.PhotoRect.Y)
        End Using
    End Sub

    Private Shared Function Sepia(bmp As Bitmap, amount As Single) As Bitmap
        Dim result = bmp.Clone(New Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format32bppArgb)
        Dim px = Perspective.ReadPixels(result)
        For i = 0 To px.Length - 1 Step 4
            Dim l = 0.3F * px(i + 2) + 0.59F * px(i + 1) + 0.11F * px(i)
            px(i + 2) = ToByte(px(i + 2) * (1 - amount) + Math.Min(255, l * 1.08F + 12) * amount)
            px(i + 1) = ToByte(px(i + 1) * (1 - amount) + Math.Min(255, l * 0.98F + 6) * amount)
            px(i) = ToByte(px(i) * (1 - amount) + l * 0.8F * amount)
        Next
        Perspective.WritePixels(result, px)
        Return result
    End Function

    '---------------------------------------------------------------------
    ' 筆刷畫的框：毛筆墨刷、鉛筆手繪
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 用 PhotoEdit 自己的筆刷沿著照片四邊畫：毛筆是一圈有飛白的粗筆（壓在照片邊上），
    ''' 鉛筆是兩圈微微抖動、角落出頭的細線。
    ''' </summary>
    Private Shared Sub PaintBrushFrame(ctx As FrameContext, brush As BrushKind)
        Dim full = ctx.Result, r = ctx.PhotoRect, bw = ctx.Bw
        Dim hh = CSng(full.Height)
        Dim ink = If(brush = BrushKind.Pencil, Color.FromArgb(70, 68, 66), ctx.Main)
        If brush = BrushKind.Pencil AndAlso ctx.Recipe.FrameColorArgb.HasValue AndAlso Not ctx.Recipe.FrameAutoColor Then ink = Color.FromArgb(70, 68, 66)
        Dim layers As New List(Of DrawLayer)()
        Dim rnd As New Random(17)
        Dim stroke = Function(ax As Single, ay As Single, bx As Single, by As Single, width As Single, seed As Integer)
                         Dim pts As New List(Of DrawPoint)()
                         Dim n = 24
                         For k = 0 To n
                             Dim t = k / CSng(n)
                             Dim wob = If(brush = BrushKind.Pencil, (Noise(t * 6, seed, 3) - 0.5F) * bw * 0.12F, 0)
                             Dim dx = bx - ax, dy = by - ay, len = CSng(Math.Sqrt(dx * dx + dy * dy))
                             Dim nx = -dy / Math.Max(1, len), ny = dx / Math.Max(1, len)
                             Dim pr = If(brush = BrushKind.Pencil, 0.95F, 0.35F + 0.65F * CSng(Math.Sin(Math.Min(1, t * 1.2F) * Math.PI)))
                             pts.Add(New DrawPoint((ax + dx * t + nx * wob) / hh, (ay + dy * t + ny * wob) / hh, pr))
                         Next
                         ' 鉛筆框用硬筆半透明灰線（鉛筆筆刷在細線時顆粒太重，像一串珠子）
                         Return New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = If(brush = BrushKind.Pencil, BrushKind.HardRound, brush), .StrokeColorArgb = ink.ToArgb(),
                                                    .Opacity = If(brush = BrushKind.Pencil, 70, 100), .StrokeWidth = width / hh, .Seed = seed,
                                                    .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}}
                     End Function
        If brush = BrushKind.ChineseBrush Then
            ' 四邊各兩筆，壓在照片邊緣上，起筆收筆超出一點
            Dim wd = bw * 2.1F
            Dim o = bw * 0.35F
            For pass = 0 To 1
                Dim j = pass * bw * 0.25F
                layers.Add(stroke(r.Left - wd * 0.6F, r.Top + o + j, r.Right + wd * 0.5F, r.Top + o - j, wd, 31 + pass))
                layers.Add(stroke(r.Right - o - j, r.Top - wd * 0.5F, r.Right - o + j, r.Bottom + wd * 0.6F, wd, 41 + pass))
                layers.Add(stroke(r.Right + wd * 0.6F, r.Bottom - o - j, r.Left - wd * 0.5F, r.Bottom - o + j, wd, 51 + pass))
                layers.Add(stroke(r.Left + o + j, r.Bottom + wd * 0.5F, r.Left + o - j, r.Top - wd * 0.6F, wd, 61 + pass))
            Next
        Else
            ' 兩圈細線，角落出頭
            Dim wd = Math.Max(1.5F, bw * 0.07F)
            For Each gap In {bw * 0.22F, bw * 0.42F}
                Dim ov = bw * 0.3F
                layers.Add(stroke(r.Left - gap - ov, r.Top - gap, r.Right + gap + ov, r.Top - gap, wd, 71 + CInt(gap)))
                layers.Add(stroke(r.Right + gap, r.Top - gap - ov, r.Right + gap, r.Bottom + gap + ov, wd, 81 + CInt(gap)))
                layers.Add(stroke(r.Right + gap + ov, r.Bottom + gap, r.Left - gap - ov, r.Bottom + gap, wd, 91 + CInt(gap)))
                layers.Add(stroke(r.Left - gap, r.Bottom + gap + ov, r.Left - gap, r.Top - gap - ov, wd, 101 + CInt(gap)))
            Next
        End If
        DrawingRenderer.DrawLayers(full, New EditRecipe With {.Drawings = layers})
    End Sub
End Class
