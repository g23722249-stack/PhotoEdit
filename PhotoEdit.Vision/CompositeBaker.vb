Imports System.Drawing
Imports System.Drawing.Imaging
Imports OpenCvSharp

''' <summary>
''' 人物合成的影像處理：AI 去背、邊緣精修、做出圖層用的人物 PNG（去殘色 → 色彩 → 裁到前景外框 → 翻轉）。
''' 人物圖一律先縮到長邊 MaxSide（AI 遮罩也是這個大小），之後所有座標都以這張工作用的人物圖為準。
''' </summary>
Public NotInheritable Class CompositeBaker
    Private Sub New()
    End Sub

    ''' <summary>工作用人物圖的長邊上限（和去背遮罩的 MaskStore.MaxSide 一致）。</summary>
    Public Const MaxSide As Integer = MaskStore.MaxSide

    ''' <summary>讀人物圖（不鎖檔），縮到長邊 MaxSide，回傳 32bpp。GDI+ 讀不了的格式（WebP）改用 OpenCV。</summary>
    Public Shared Function LoadFigure(file As String) As Bitmap
        Dim bytes = IO.File.ReadAllBytes(file)
        Dim img As Bitmap
        Try
            Using ms As New IO.MemoryStream(bytes), src = Image.FromStream(ms)
                img = New Bitmap(src)
            End Using
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException
            Using m = Cv2.ImDecode(bytes, ImreadModes.Unchanged)
                If m.Empty() Then Throw New ArgumentException(ex.Message, ex)
                img = Extensions.BitmapConverter.ToBitmap(m)
            End Using
        End Try
        Return FitWorking(img)
    End Function

    ''' <summary>把圖縮到長邊 MaxSide 以內並轉成 32bpp（傳入的圖由這裡處理掉）。</summary>
    Public Shared Function FitWorking(img As Bitmap) As Bitmap
        Dim k = Math.Min(1.0, MaxSide / CDbl(Math.Max(img.Width, img.Height)))
        Dim w = Math.Max(1, CInt(img.Width * k)), h = Math.Max(1, CInt(img.Height * k))
        Dim r As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(r)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
            g.DrawImage(img, 0, 0, w, h)
        End Using
        img.Dispose()
        Return r
    End Function

    Public Shared Function ReadPixels(bmp As Bitmap) As Byte()
        Dim w = bmp.Width, h = bmp.Height
        Using copy = bmp.Clone(New Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb)
            Dim d = copy.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Dim px(w * h * 4 - 1) As Byte
            For y = 0 To h - 1
                Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4)
            Next
            copy.UnlockBits(d)
            Return px
        End Using
    End Function

    Public Shared Function ToBitmap(px As Byte(), w As Integer, h As Integer) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Dim d = b.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        For y = 0 To h - 1
            Runtime.InteropServices.Marshal.Copy(px, y * w * 4, d.Scan0 + y * d.Stride, w * 4)
        Next
        b.UnlockBits(d)
        Return b
    End Function

    ''' <summary>原圖本身的透明度（有可用的透明區域時，可以不用 AI 去背）；沒有時 Nothing。</summary>
    Public Shared Function SourceAlpha(px As Byte(), w As Integer, h As Integer) As Byte()
        Dim a(w * h - 1) As Byte
        Dim clear = 0
        For i = 0 To a.Length - 1
            a(i) = px(i * 4 + 3)
            If a(i) < 128 Then clear += 1
        Next
        ' 至少 2% 是透明的，而且不是整張透明，才當成「已經去背」。
        Return If(clear > a.Length * 0.02 AndAlso clear < a.Length * 0.98, a, Nothing)
    End Function

    ''' <summary>AI 去背：回傳和人物圖同尺寸的透明度（0..255）。</summary>
    Public Shared Function AiMask(figure As Bitmap, model As CutoutModel) As Byte()
        Using m = BackgroundRemover.ComputeMask(figure, model)
            Dim px = ReadPixels(m)
            Dim w = figure.Width, h = figure.Height
            If m.Width = w AndAlso m.Height = h Then
                Dim a(w * h - 1) As Byte
                For i = 0 To a.Length - 1
                    a(i) = px(i * 4 + 2)
                Next
                Return a
            End If
            ' 遮罩比人物圖小（不應該發生；人物圖已縮到 MaxSide）：放大回來。
            Using src As New Mat(m.Height, m.Width, MatType.CV_8UC4), gray As New Mat(), big As New Mat()
                Runtime.InteropServices.Marshal.Copy(px, 0, src.Data, px.Length)
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGRA2GRAY)
                Cv2.Resize(gray, big, New OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Linear)
                Dim a(w * h - 1) As Byte
                Runtime.InteropServices.Marshal.Copy(big.Data, a, 0, a.Length)
                Return a
            End Using
        End Using
    End Function

    ''' <summary>邊緣精修（見 CutoutCompositor.RefineEdges）：strength 0 時原樣傳回。回傳新的陣列。</summary>
    Public Shared Function RefineMask(mask As Byte(), figurePx As Byte(), w As Integer, h As Integer, strength As Integer, model As CutoutModel) As Byte()
        Dim r = DirectCast(mask.Clone(), Byte())
        If strength <= 0 Then Return r
        Using m As New Mat(h, w, MatType.CV_8UC1)
            Runtime.InteropServices.Marshal.Copy(r, 0, m.Data, r.Length)
            CutoutCompositor.RefineEdges(m, figurePx, w, h, strength, CutoutModels.InputSize(model))
            Runtime.InteropServices.Marshal.Copy(m.Data, r, 0, r.Length)
        End Using
        Return r
    End Function

    ''' <summary>前景外框（透明度 &gt; 0），外加 pad 像素；全透明時 Nothing。</summary>
    Public Shared Function ForegroundBounds(alpha As Byte(), w As Integer, h As Integer, Optional pad As Integer = 2) As Rectangle?
        Dim minX = w, minY = h, maxX = -1, maxY = -1
        For y = 0 To h - 1
            For x = 0 To w - 1
                If alpha(y * w + x) > 0 Then
                    If x < minX Then minX = x
                    If x > maxX Then maxX = x
                    If y < minY Then minY = y
                    maxY = y
                End If
            Next
        Next
        If maxX < 0 Then Return Nothing
        Return Rectangle.FromLTRB(Math.Max(0, minX - pad), Math.Max(0, minY - pad), Math.Min(w, maxX + 1 + pad), Math.Min(h, maxY + 1 + pad))
    End Function

    ''' <summary>
    ''' 做出圖層用的人物：去除邊緣殘色、套色彩、透明度換成遮罩、裁到前景外框、需要時左右翻轉。
    ''' 回傳的 Crop 是顯示方向（翻轉後）人物圖上的範圍，ToOverlay 用它換算位置。
    ''' </summary>
    Public Shared Function Bake(figurePx As Byte(), w As Integer, h As Integer, alpha As Byte(), fig As CompositeFigure) As (Image As Bitmap, Crop As Rectangle)
        Dim px = DirectCast(figurePx.Clone(), Byte())
        If fig.Decontaminate Then
            CutoutCompositor.Decontaminate(px, alpha, w, h, CutoutCompositor.RefineRadius(w, h, Math.Max(30, fig.EdgeRefine), CutoutModels.InputSize(fig.Model)))
        End If
        For i = 0 To alpha.Length - 1
            px(i * 4 + 3) = alpha(i)
        Next
        CompositeColorMatch.Apply(px, w, h, fig.Color)
        Dim box = If(ForegroundBounds(alpha, w, h), New Rectangle(0, 0, w, h))
        Dim cw = box.Width, ch = box.Height
        Dim outPx(cw * ch * 4 - 1) As Byte
        For y = 0 To ch - 1
            For x = 0 To cw - 1
                Dim sx = box.X + x
                Dim dx = If(fig.FlipHorizontal, cw - 1 - x, x)
                Buffer.BlockCopy(px, ((box.Y + y) * w + sx) * 4, outPx, (y * cw + dx) * 4, 4)
            Next
        Next
        Dim crop = If(fig.FlipHorizontal, New Rectangle(w - box.Right, box.Y, cw, ch), box)
        Return (ToBitmap(outPx, cw, ch), crop)
    End Function

    ''' <summary>左右翻轉透明度（顯示方向的分析、定位用）。</summary>
    Public Shared Function MirrorAlpha(alpha As Byte(), w As Integer, h As Integer) As Byte()
        Dim r(alpha.Length - 1) As Byte
        For y = 0 To h - 1
            Dim row = y * w
            For x = 0 To w - 1
                r(row + w - 1 - x) = alpha(row + x)
            Next
        Next
        Return r
    End Function

    ''' <summary>透明度存成 8 位元灰階 PNG。</summary>
    Public Shared Sub SaveMask(alpha As Byte(), w As Integer, h As Integer, file As String)
        Using m As New Mat(h, w, MatType.CV_8UC1)
            Runtime.InteropServices.Marshal.Copy(alpha, 0, m.Data, alpha.Length)
            IO.File.WriteAllBytes(file, m.ImEncode(".png"))
        End Using
    End Sub

    ''' <summary>讀回 SaveMask 存的遮罩；尺寸不符或讀不到時 Nothing。</summary>
    Public Shared Function LoadMask(file As String, w As Integer, h As Integer) As Byte()
        If String.IsNullOrEmpty(file) OrElse Not IO.File.Exists(file) Then Return Nothing
        Using m = Cv2.ImDecode(IO.File.ReadAllBytes(file), ImreadModes.Grayscale)
            If m.Empty() Then Return Nothing
            Using fit As New Mat()
                If m.Width <> w OrElse m.Height <> h Then
                    Cv2.Resize(m, fit, New OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Linear)
                Else
                    m.CopyTo(fit)
                End If
                Dim a(w * h - 1) As Byte
                Runtime.InteropServices.Marshal.Copy(fit.Data, a, 0, a.Length)
                Return a
            End Using
        End Using
    End Function
    '=====================================================================
    ' 階段 B：變形（旋轉、仰角透視）、遮擋、陰影都做進圖層 PNG；圖層對齊背景的座標軸
    '=====================================================================

    ''' <summary>
    ''' 準備人物：去除邊緣殘色、透明度換成遮罩、套色彩、需要時整張左右翻轉（之後的幾何都用顯示方向）。回傳 BGRA。
    ''' </summary>
    Public Shared Function Prepare(figurePx As Byte(), w As Integer, h As Integer, alpha As Byte(), fig As CompositeFigure) As Byte()
        Dim px = DirectCast(figurePx.Clone(), Byte())
        If fig.Decontaminate Then
            CutoutCompositor.Decontaminate(px, alpha, w, h, CutoutCompositor.RefineRadius(w, h, Math.Max(30, fig.EdgeRefine), CutoutModels.InputSize(fig.Model)))
        End If
        For i = 0 To alpha.Length - 1
            px(i * 4 + 3) = alpha(i)
        Next
        CompositeColorMatch.Apply(px, w, h, fig.Color)
        If Not fig.FlipHorizontal Then Return px
        Dim r(px.Length - 1) As Byte
        For y = 0 To h - 1
            For x = 0 To w - 1
                Buffer.BlockCopy(px, (y * w + x) * 4, r, (y * w + w - 1 - x) * 4, 4)
            Next
        Next
        Return r
    End Function

    ''' <summary>
    ''' 縮小倍率很大時先把來源縮小（避免取樣鋸齒）：回傳要用的來源 Mat（呼叫端負責釋放）與對應調整過的幾何。
    ''' </summary>
    Private Shared Function SourceFor(px As Byte(), w As Integer, h As Integer, channels As Integer, geom As CompositeGeometry, r As Double) As (Src As Mat, Geom As CompositeGeometry)
        Dim m As New Mat(h, w, If(channels = 4, MatType.CV_8UC4, MatType.CV_8UC1))
        Runtime.InteropServices.Marshal.Copy(px, 0, m.Data, px.Length)
        Dim f = geom.Scale * r ' 人物一個像素在圖層上的大小
        If f >= 0.6 Then Return (m, geom)
        Dim sw = Math.Max(1, CInt(w * f * 1.5)), sh = Math.Max(1, CInt(h * f * 1.5))
        Dim small As New Mat()
        Cv2.Resize(m, small, New OpenCvSharp.Size(sw, sh), 0, 0, InterpolationFlags.Area)
        m.Dispose()
        Dim kx = sw / CDbl(w), ky = sh / CDbl(h)
        Dim g = geom
        g.AnchorX *= kx : g.AnchorY *= ky : g.Scale /= kx : g.FigureHeight *= ky
        Return (small, g)
    End Function

    ''' <summary>
    ''' 變形後的人物圖層：prepared（Prepare 的結果、顯示方向）依 geom 變形到背景座標，解析度 r（圖層像素 / 背景像素）。
    ''' occlusion（背景尺寸 bw×bh，在人物前面的東西）擋住的部分變透明。回傳圖層與它在背景上的範圍；完全在畫面外時 Image 是 Nothing。
    ''' </summary>
    ''' <param name="bgBlur">模糊過的背景（BGRA，bw×bh），邊緣透光用；不用時 Nothing。</param>
    ''' <param name="lightWrap">邊緣透光 0..100。</param>
    ''' <param name="grain">顆粒：背景預覽尺寸下的灰階標準差（0 = 不加）；圖層解析度較高時依 r 換算。</param>
    Public Shared Function BakeLayer(prepared As Byte(), w As Integer, h As Integer, figBounds As Rectangle, geom As CompositeGeometry,
                                     occlusion As Byte(), bw As Integer, bh As Integer, r As Double,
                                     Optional bgBlur As Byte() = Nothing, Optional lightWrap As Integer = 0, Optional grain As Double = 0) As (Image As Bitmap, Rect As RectangleF)
        Dim box = RectangleF.Intersect(geom.BoundsOf(Rectangle.Inflate(figBounds, 2, 2)), New RectangleF(0, 0, bw, bh))
        If box.Width < 0.5 OrElse box.Height < 0.5 Then Return (Nothing, box)
        Dim lw = Math.Max(1, CInt(Math.Ceiling(box.Width * r))), lh = Math.Max(1, CInt(Math.Ceiling(box.Height * r)))
        ' 預乘透明度後再取樣，邊緣才不會帶進黑邊
        Dim pre = DirectCast(prepared.Clone(), Byte())
        For i = 0 To pre.Length - 1 Step 4
            Dim a = CInt(pre(i + 3))
            If a = 255 Then Continue For
            pre(i) = CByte(pre(i) * a \ 255) : pre(i + 1) = CByte(pre(i + 1) * a \ 255) : pre(i + 2) = CByte(pre(i + 2) * a \ 255)
        Next
        Dim srcInfo = SourceFor(pre, w, h, 4, geom, r)
        Dim g = srcInfo.Geom
        Dim outPx(lw * lh * 4 - 1) As Byte
        Using src = srcInfo.Src, mapX As New Mat(lh, lw, MatType.CV_32FC1), mapY As New Mat(lh, lw, MatType.CV_32FC1), dst As New Mat()
            Dim mx(lw * lh - 1) As Single, my(lw * lh - 1) As Single
            Threading.Tasks.Parallel.For(0, lh, Sub(v)
                                                    For u = 0 To lw - 1
                                                        Dim p = g.Inverse(box.X + (u + 0.5) / r, box.Y + (v + 0.5) / r)
                                                        mx(v * lw + u) = p.X - 0.5F : my(v * lw + u) = p.Y - 0.5F
                                                    Next
                                                End Sub)
            Runtime.InteropServices.Marshal.Copy(mx, 0, mapX.Data, mx.Length)
            Runtime.InteropServices.Marshal.Copy(my, 0, mapY.Data, my.Length)
            Cv2.Remap(src, dst, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0))
            Runtime.InteropServices.Marshal.Copy(dst.Data, outPx, 0, outPx.Length)
        End Using
        Threading.Tasks.Parallel.For(0, lh, Sub(v)
                                                For u = 0 To lw - 1
                                                    Dim i = (v * lw + u) * 4
                                                    Dim a = CInt(outPx(i + 3))
                                                    If a = 0 Then Continue For
                                                    If occlusion IsNot Nothing Then
                                                        Dim bx = CInt(box.X + (u + 0.5) / r), by = CInt(box.Y + (v + 0.5) / r)
                                                        If bx >= 0 AndAlso by >= 0 AndAlso bx < bw AndAlso by < bh Then
                                                            Dim o = CInt(occlusion(by * bw + bx))
                                                            If o > 0 Then
                                                                ' 預乘狀態下顏色和透明度一起縮
                                                                Dim keep = 255 - o
                                                                outPx(i) = CByte(outPx(i) * keep \ 255) : outPx(i + 1) = CByte(outPx(i + 1) * keep \ 255)
                                                                outPx(i + 2) = CByte(outPx(i + 2) * keep \ 255) : a = a * keep \ 255
                                                                outPx(i + 3) = CByte(a)
                                                                If a = 0 Then Continue For
                                                            End If
                                                        End If
                                                    End If
                                                    ' 還原成一般（非預乘）
                                                    outPx(i) = CByte(Math.Min(255, outPx(i) * 255 \ a))
                                                    outPx(i + 1) = CByte(Math.Min(255, outPx(i + 1) * 255 \ a))
                                                    outPx(i + 2) = CByte(Math.Min(255, outPx(i + 2) * 255 \ a))
                                                Next
                                            End Sub)
        If lightWrap > 0 AndAlso bgBlur IsNot Nothing Then ApplyLightWrap(outPx, lw, lh, box, r, bgBlur, bw, bh, lightWrap / 100.0, geom.FigureHeight * geom.Scale * r)
        If grain > 0 Then ApplyGrain(outPx, lw, lh, grain * r)
        Return (ToBitmap(outPx, lw, lh), box)
    End Function

    ''' <summary>
    ''' 陰影圖層：投射陰影（人物透明度以錨點為支點投到地面：高度 h 的點落到方向 θ、長度 h·L 處，再依透視壓扁）、
    ''' 接觸陰影與環境遮蔽（錨點下的扁橢圓），乘上「不落在天空」與遮擋。顏色是帶一點藍的深灰。
    ''' 陰影關掉、半身像時回傳 Nothing。
    ''' </summary>
    Public Shared Function BakeShadow(prepared As Byte(), w As Integer, h As Integer, fa As FigureAnalysis, geom As CompositeGeometry,
                                      shadow As CompositeShadow, scene As SceneAnalysis, occlusion As Byte(),
                                      bw As Integer, bh As Integer, r As Double) As (Image As Bitmap, Rect As RectangleF)
        If shadow Is Nothing OrElse Not shadow.Enabled OrElse fa.BottomCropped Then Return (Nothing, RectangleF.Empty)
        Dim figH = Math.Max(1, fa.Height * geom.Scale)
        Dim th = shadow.Direction * Math.PI / 180
        Dim len = Math.Max(0, shadow.Length)
        Dim flat = Math.Clamp(shadow.Flatten, 0.1, 1)
        Dim c = len * Math.Cos(th)
        Dim ky = len * Math.Sin(th) * flat
        If Math.Abs(ky) < 0.04 Then ky = If(ky < 0, -0.04, 0.04)
        Dim ax = geom.TX, ay = geom.TY
        ' 投射：背景上人物的點 (dx, dy)（相對錨點，dy < 0 在上方）→ (dx − c·dy, −ky·dy)
        Dim castOf = Function(p As PointF) New PointF(CSng(ax + (p.X - ax) - c * (p.Y - ay)), CSng(ay - ky * (p.Y - ay)))
        Dim fb = geom.BoundsOf(fa.Bounds)
        Dim corners = {New PointF(fb.Left, fb.Top), New PointF(fb.Right, fb.Top), New PointF(fb.Left, fb.Bottom), New PointF(fb.Right, fb.Bottom)}.Select(castOf).ToList()
        Dim feetW = Math.Max(2, fa.FeetWidth * geom.Scale)
        Dim rx = feetW * 0.75 + 2, ry = Math.Max(2, feetW * 0.18)
        Dim aoRx = feetW * 1.4 + 4, aoRy = Math.Max(3, feetW * 0.32)
        Dim sigma = Math.Max(0.5, shadow.Softness / 1000.0 * figH)
        Dim pad = sigma * 3 + feetW * 0.4 + 4
        Dim minX = Math.Min(corners.Min(Function(p) p.X), ax - aoRx) - pad, maxX = Math.Max(corners.Max(Function(p) p.X), ax + aoRx) + pad
        Dim minY = Math.Min(corners.Min(Function(p) p.Y), ay - aoRy) - pad, maxY = Math.Max(corners.Max(Function(p) p.Y), ay + aoRy) + pad
        Dim box = RectangleF.Intersect(RectangleF.FromLTRB(CSng(minX), CSng(minY), CSng(maxX), CSng(maxY)), New RectangleF(0, 0, bw, bh))
        If box.Width < 0.5 OrElse box.Height < 0.5 Then Return (Nothing, box)
        Dim lw = Math.Max(1, CInt(Math.Ceiling(box.Width * r))), lh = Math.Max(1, CInt(Math.Ceiling(box.Height * r)))
        Dim n = lw * lh

        ' 1. 投射陰影：反算背景點 → 人物圖上的點，取透明度，離腳越遠越淡
        Dim cast(n - 1) As Single
        If shadow.Opacity > 0 AndAlso len > 0.001 Then
            Dim alpha(w * h - 1) As Byte
            For i = 0 To alpha.Length - 1
                alpha(i) = prepared(i * 4 + 3)
            Next
            Dim srcInfo = SourceFor(alpha, w, h, 1, geom, r * Math.Max(0.3, Math.Abs(ky)))
            Dim g = srcInfo.Geom
            Dim falloff = shadow.Falloff / 100.0
            Using src = srcInfo.Src, mapX As New Mat(lh, lw, MatType.CV_32FC1), mapY As New Mat(lh, lw, MatType.CV_32FC1), dst As New Mat()
                Dim mx(n - 1) As Single, my(n - 1) As Single, hf(n - 1) As Single
                Threading.Tasks.Parallel.For(0, lh, Sub(v)
                                                        For u = 0 To lw - 1
                                                            Dim sx = box.X + (u + 0.5) / r - ax, sy = box.Y + (v + 0.5) / r - ay
                                                            Dim dy = -sy / ky
                                                            Dim dx = sx + c * dy
                                                            Dim p = g.Inverse(ax + dx, ay + dy)
                                                            Dim k = v * lw + u
                                                            mx(k) = p.X - 0.5F : my(k) = p.Y - 0.5F
                                                            hf(k) = CSng(Math.Clamp(-dy / figH, 0, 1))
                                                        Next
                                                    End Sub)
                Runtime.InteropServices.Marshal.Copy(mx, 0, mapX.Data, n)
                Runtime.InteropServices.Marshal.Copy(my, 0, mapY.Data, n)
                Cv2.Remap(src, dst, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0))
                Using f As New Mat()
                    dst.ConvertTo(f, MatType.CV_32FC1, 1 / 255.0)
                    Runtime.InteropServices.Marshal.Copy(f.Data, cast, 0, n)
                End Using
                For k = 0 To n - 1
                    cast(k) *= CSng(1 - falloff * hf(k))
                Next
            End Using
            BlurInPlace(cast, lw, lh, sigma * r)
        End If

        ' 2. 接觸陰影＋3. 環境遮蔽
        Dim contact(n - 1) As Single
        Dim cs = shadow.Contact / 100.0, ao = shadow.Ambient / 100.0 * 0.5
        If cs > 0 OrElse ao > 0 Then
            For v = 0 To lh - 1
                For u = 0 To lw - 1
                    Dim dx = box.X + (u + 0.5) / r - ax, dy = box.Y + (v + 0.5) / r - ay
                    Dim e1 = (dx / rx) ^ 2 + (dy / ry) ^ 2, e2 = (dx / aoRx) ^ 2 + (dy / aoRy) ^ 2
                    contact(v * lw + u) = CSng(1 - (1 - cs * Smooth(e1)) * (1 - ao * Smooth(e2)))
                Next
            Next
            BlurInPlace(contact, lw, lh, Math.Max(0.5, feetW * 0.12) * r)
        End If

        ' 合併、不落在天空、被前景擋住
        Dim op = shadow.Opacity / 100.0
        Dim outPx(n * 4 - 1) As Byte
        Threading.Tasks.Parallel.For(0, lh, Sub(v)
                                                For u = 0 To lw - 1
                                                    Dim k = v * lw + u
                                                    Dim val = 1 - (1 - cast(k) * op) * (1 - contact(k))
                                                    Dim bx = box.X + (u + 0.5) / r, by = box.Y + (v + 0.5) / r
                                                    If shadow.AvoidSky AndAlso scene IsNot Nothing Then val *= 1 - scene.SkyAt(bx, by)
                                                    If occlusion IsNot Nothing Then
                                                        Dim ix = CInt(bx), iy = CInt(by)
                                                        If ix >= 0 AndAlso iy >= 0 AndAlso ix < bw AndAlso iy < bh Then val *= 1 - occlusion(iy * bw + ix) / 255.0
                                                    End If
                                                    val = Math.Clamp(val, 0, 0.95)
                                                    outPx(k * 4) = 36 : outPx(k * 4 + 1) = 26 : outPx(k * 4 + 2) = 24
                                                    outPx(k * 4 + 3) = CByte(Math.Round(val * 255))
                                                Next
                                            End Sub)
        Return (ToBitmap(outPx, lw, lh), box)
    End Function

    ''' <summary>e = 橢圓正規化距離平方；中心 1，邊緣平滑降到 0。</summary>
    Private Shared Function Smooth(e As Double) As Double
        If e >= 1 Then Return 0
        Dim t = 1 - e
        Return t * t * (3 - 2 * t)
    End Function

    Private Shared Sub BlurInPlace(data As Single(), w As Integer, h As Integer, sigma As Double)
        If sigma < 0.3 Then Return
        Using m As New Mat(h, w, MatType.CV_32FC1), d As New Mat()
            Runtime.InteropServices.Marshal.Copy(data, 0, m.Data, data.Length)
            Cv2.GaussianBlur(m, d, New OpenCvSharp.Size(0, 0), sigma)
            Runtime.InteropServices.Marshal.Copy(d.Data, data, 0, data.Length)
        End Using
    End Sub

    ''' <summary>魔術棒：從 (x, y) 往外找和起點顏色相近（BGRA 距離 ≤ tolerance）的相連範圍，回傳 0/255 遮罩。</summary>
    Public Shared Function FloodSelect(px As Byte(), w As Integer, h As Integer, x As Integer, y As Integer, tolerance As Integer) As Byte()
        Dim m(w * h - 1) As Byte
        If x < 0 OrElse y < 0 OrElse x >= w OrElse y >= h Then Return m
        Dim s = (y * w + x) * 4
        Dim sb = CInt(px(s)), sg = CInt(px(s + 1)), sr = CInt(px(s + 2))
        Dim tol2 = tolerance * tolerance * 3
        Dim q As New Queue(Of Integer)()
        q.Enqueue(y * w + x) : m(y * w + x) = 255
        While q.Count > 0
            Dim i = q.Dequeue()
            Dim cx = i Mod w, cy = i \ w
            For Each nb In {If(cx > 0, i - 1, -1), If(cx < w - 1, i + 1, -1), If(cy > 0, i - w, -1), If(cy < h - 1, i + w, -1)}
                If nb < 0 OrElse m(nb) <> 0 Then Continue For
                Dim j = nb * 4
                Dim d = (px(j) - sb) ^ 2 + (px(j + 1) - sg) ^ 2 + (px(j + 2) - sr) ^ 2
                If d > tol2 Then Continue For
                m(nb) = 255
                q.Enqueue(nb)
            Next
        End While
        Return m
    End Function
    ''' <summary>
    ''' 邊緣透光：人物邊緣附近（透明度往外變淡的那一圈內側）混進模糊背景的光（濾色），越靠邊越多，
    ''' 人物看起來被環境光包住，不像剪貼上去的。figHeightPx 是人物在圖層上的高度（決定透光的寬度）。
    ''' </summary>
    Private Shared Sub ApplyLightWrap(px As Byte(), lw As Integer, lh As Integer, box As RectangleF, r As Double, bgBlur As Byte(), bw As Integer, bh As Integer,
                                      strength As Double, figHeightPx As Double)
        Dim n = lw * lh
        Dim a(n - 1) As Single
        For i = 0 To n - 1
            a(i) = px(i * 4 + 3) / 255.0F
        Next
        Dim blurA = DirectCast(a.Clone(), Single())
        BlurInPlace(blurA, lw, lh, Math.Max(1.0, figHeightPx * 0.012))
        Dim k = CSng(Math.Clamp(strength, 0, 1) * 0.85)
        Threading.Tasks.Parallel.For(0, lh, Sub(v)
                                                For u = 0 To lw - 1
                                                    Dim i = v * lw + u
                                                    If a(i) <= 0 Then Continue For
                                                    Dim edge = a(i) * Math.Clamp((1 - blurA(i)) * 2.2F, 0F, 1F) * k
                                                    If edge <= 0.002F Then Continue For
                                                    Dim bx = Math.Clamp(CInt(box.X + (u + 0.5) / r), 0, bw - 1), by = Math.Clamp(CInt(box.Y + (v + 0.5) / r), 0, bh - 1)
                                                    Dim j = (by * bw + bx) * 4
                                                    For c = 0 To 2
                                                        Dim f = px(i * 4 + c) / 255.0F, b = bgBlur(j + c) / 255.0F
                                                        Dim screen = 1 - (1 - f) * (1 - b)
                                                        px(i * 4 + c) = CByte(Math.Clamp(CInt(Math.Round((f + (screen - f) * edge) * 255)), 0, 255))
                                                    Next
                                                Next
                                            End Sub)
    End Sub

    ''' <summary>顆粒：在人物上加固定種子的亮度雜訊（標準差 sigma 灰階），讓乾淨的人物和有雜訊的照片質感接近。</summary>
    Private Shared Sub ApplyGrain(px As Byte(), lw As Integer, lh As Integer, sigma As Double)
        If sigma < 0.2 Then Return
        Dim rnd As New Random(20261030)
        For i = 0 To lw * lh - 1
            If px(i * 4 + 3) = 0 Then Continue For
            ' Box–Muller
            Dim u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble()
            Dim d = CInt(Math.Round(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2) * sigma))
            For c = 0 To 2
                px(i * 4 + c) = CByte(Math.Clamp(px(i * 4 + c) + d, 0, 255))
            Next
        Next
    End Sub

    ''' <summary>模糊過的背景（邊緣透光用）。</summary>
    Public Shared Function BlurBackground(px As Byte(), w As Integer, h As Integer, sigma As Double) As Byte()
        Using m As New Mat(h, w, MatType.CV_8UC4), d As New Mat()
            Runtime.InteropServices.Marshal.Copy(px, 0, m.Data, px.Length)
            Cv2.GaussianBlur(m, d, New OpenCvSharp.Size(0, 0), Math.Max(0.5, sigma))
            Dim r(px.Length - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(d.Data, r, 0, r.Length)
            Return r
        End Using
    End Function
End Class
