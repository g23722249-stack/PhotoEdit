Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>人物圖層的色彩調整（影像 → 吶喊…）。數值 0 表示不變。</summary>
Public Class ScreamPersonColor
    ''' <summary>色相 -180..180（度）。</summary>
    Public Property Hue As Integer
    ''' <summary>飽和度 -100..100。</summary>
    Public Property Saturation As Integer
    ''' <summary>亮度 -100..100。</summary>
    Public Property Brightness As Integer
    ''' <summary>對比 -100..100。</summary>
    Public Property Contrast As Integer
    ''' <summary>色溫 -100..100（負值偏藍、正值偏橘）。</summary>
    Public Property Temperature As Integer
    ''' <summary>只調整臉（臉框附近，邊緣淡出）；False 時整個人一起調。</summary>
    Public Property FaceOnly As Boolean

    Public ReadOnly Property IsIdentity As Boolean
        Get
            Return Hue = 0 AndAlso Saturation = 0 AndAlso Brightness = 0 AndAlso Contrast = 0 AndAlso Temperature = 0
        End Get
    End Property

    Public Function Clone() As ScreamPersonColor
        Return DirectCast(MemberwiseClone(), ScreamPersonColor)
    End Function
End Class

''' <summary>建議的位置：人物圖層中心（畫布 0..1）與高度（畫布高的比例）。</summary>
Public Structure ScreamPlacement
    Public Name As String
    Public X, Y, Size As Double
End Structure

''' <summary>
''' 影像 → 吶喊（孟克）…的計算：人臉變成《吶喊》的臉（PortraitRetouch）、AI 去背（遮罩跟著臉一起變形）、
''' 人物圖層（色相、飽和度、亮度、對比、色溫、波浪筆觸、邊緣柔和、翻轉）、建議的配色與位置、合成預覽。
''' 影像都是 32bppArgb；遮罩是和人物圖同大小的 0..255 位元組。
''' </summary>
Public NotInheritable Class ScreamStudio
    Private Sub New()
    End Sub

    ''' <summary>內建的《吶喊》背景圖（backdrops\scream.webp）；沒有時 Nothing。</summary>
    Public Shared Function DefaultBackdrop() As Bitmap
        Dim art = PortraitRetouch.ScreamBackdropImage()
        If art Is Nothing Then Return Nothing
        SyncLock art
            Return BitmapConverter.ToBitmap(art)
        End SyncLock
    End Function

    ''' <summary>讀背景圖（OpenCV 讀，WebP 也可以）；讀不到時 Nothing。</summary>
    Public Shared Function LoadBackdrop(path As String) As Bitmap
        Try
            Using m = Cv2.ImDecode(IO.File.ReadAllBytes(path), ImreadModes.Color)
                If m Is Nothing OrElse m.Empty() Then Return Nothing
                Return BitmapConverter.ToBitmap(m)
            End Using
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is OpenCVException OrElse TypeOf ex Is ArgumentException
            Return Nothing
        End Try
    End Function

    ''' <summary>臉變成《吶喊》的臉（強度、眼圈 0..100，形狀看 shape）；沒有臉或強度 0 時回傳複本。</summary>
    Public Shared Function FaceEffect(source As Bitmap, faces As IReadOnlyList(Of FaceRegion), scream As Integer, eyes As Integer, shape As ScreamShape) As Bitmap
        If scream > 0 AndAlso faces IsNot Nothing AndAlso faces.Count > 0 Then
            Dim r As New EditRecipe()
            r.SetGlobalBeauty(New BeautySettings With {.Scream = scream, .ScreamEyes = eyes, .ScreamEyeSize = shape.EyeSize, .ScreamMouthSize = shape.MouthSize, .ScreamMouthRound = shape.MouthRound})
            Dim out = PortraitRetouch.Apply(source, faces, r)
            If out IsNot Nothing Then Return out
        End If
        Return source.Clone(New Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb)
    End Function

    ''' <summary>
    ''' 脖子、胸口、手臂的皮膚換成和臉一樣的暖色（amount 0..100），不然臉是油畫的顏色、身上還是照片的膚色。
    ''' 皮膚＝Lab 的顏色接近臉頰的地方，只看下巴以下（下巴以上旁邊是頭髮，金髮的顏色很接近皮膚）。回傳新圖。
    ''' </summary>
    Public Shared Function WarmSkin(image As Bitmap, faces As IReadOnlyList(Of FaceRegion), amount As Integer) As Bitmap
        Dim w = image.Width, h = image.Height
        Dim out = image.Clone(New Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb)
        If amount <= 0 OrElse faces Is Nothing OrElse faces.Count = 0 Then Return out
        Dim f = faces.OrderByDescending(Function(x) x.Box.Width * x.Box.Height).First()
        Using bgra = BitmapConverter.ToMat(out), bgr As New Mat(), fl As New Mat(), lab As New Mat()
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR)
            bgr.ConvertTo(fl, MatType.CV_32FC3, 1 / 255.0)
            Cv2.CvtColor(fl, lab, ColorConversionCodes.BGR2Lab)
            Dim px(w * h * 3 - 1) As Single
            Marshal.Copy(lab.Data, px, 0, px.Length)
            ' 臉頰的顏色：臉框中間偏下的一塊，取中位數（避開鼻孔、嘴）
            Dim aList As New List(Of Single)(), bList As New List(Of Single)()
            For Each cx In {0.27, 0.73}
                Dim x0 = CInt((f.Box.X + f.Box.Width * (cx - 0.08)) * w), x1 = CInt((f.Box.X + f.Box.Width * (cx + 0.08)) * w)
                Dim y0 = CInt((f.Box.Y + f.Box.Height * 0.52) * h), y1 = CInt((f.Box.Y + f.Box.Height * 0.68) * h)
                For y = Math.Max(0, y0) To Math.Min(h - 1, y1)
                    For x = Math.Max(0, x0) To Math.Min(w - 1, x1)
                        aList.Add(px((y * w + x) * 3 + 1)) : bList.Add(px((y * w + x) * 3 + 2))
                    Next
                Next
            Next
            If aList.Count = 0 Then Return out
            aList.Sort() : bList.Sort()
            Dim sa = aList(aList.Count \ 2), sb = bList(bList.Count \ 2)
            Dim chin = (f.Box.Y + f.Box.Height) * h, fade = f.Box.Height * h * 0.12
            Dim weight(w * h - 1) As Byte
            Parallel.For(0, h, Sub(y)
                                   Dim gate = Math.Max(0.0, Math.Min(1.0, (y - chin + fade) / fade))
                                   If gate <= 0 Then Return
                                   For x = 0 To w - 1
                                       Dim i = (y * w + x) * 3
                                       Dim da = px(i + 1) - sa, db = px(i + 2) - sb
                                       Dim k = Math.Exp(-(da * da + db * db) / (2 * 9.0 * 9.0))
                                       If px(i) < 18 Then k *= px(i) / 18.0
                                       weight(y * w + x) = CByte(255 * k * gate)
                                   Next
                               End Sub)
            Using m As New Mat(h, w, MatType.CV_8UC1)
                Marshal.Copy(weight, 0, m.Data, weight.Length)
                Cv2.GaussianBlur(m, m, New OpenCvSharp.Size(0, 0), Math.Max(1.0, Math.Max(w, h) * 0.003))
                Marshal.Copy(m.Data, weight, 0, weight.Length)
            End Using
            Dim amt = amount / 100.0 * 0.85
            Dim d = out.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
            Try
                Dim buf(d.Stride * h - 1) As Byte
                Marshal.Copy(d.Scan0, buf, 0, buf.Length)
                Dim stride = d.Stride
                Parallel.For(0, h, Sub(y)
                                       For x = 0 To w - 1
                                           Dim k = weight(y * w + x) / 255.0 * amt
                                           If k <= 0.004 Then Continue For
                                           Dim i = y * stride + x * 4
                                           Dim pr, pg, pb As Double
                                           PortraitRetouch.ScreamFacePalette(px((y * w + x) * 3) / 100.0 * 0.88, pr, pg, pb)
                                           buf(i) = CByte(Math.Round(buf(i) + (pb - buf(i)) * k))
                                           buf(i + 1) = CByte(Math.Round(buf(i + 1) + (pg - buf(i + 1)) * k))
                                           buf(i + 2) = CByte(Math.Round(buf(i + 2) + (pr - buf(i + 2)) * k))
                                       Next
                                   End Sub)
                Marshal.Copy(buf, 0, d.Scan0, buf.Length)
            Finally
                out.UnlockBits(d)
            End Try
        End Using
        Return out
    End Function

    ''' <summary>
    ''' 人物一筆一筆畫成油畫（amount 0..100，見 ScreamOilPainter）。image 直接改。
    ''' </summary>
    Public Shared Sub OilPaint(image As Bitmap, amount As Integer, faces As IReadOnlyList(Of FaceRegion))
        ScreamOilPainter.Paint(image, amount, faces)
    End Sub

    ''' <summary>
    ''' 身體扭曲的對應表（dst → src）：原畫的人沒有肩膀、身體細長、像 S 形順著背景流動。
    ''' 下巴以上不動；往下身體左右擺動成 S 形、整體收窄、肩膀往下垮，輪廓再帶一點波浪。長度都以臉為準。沒有臉時 Nothing。
    ''' </summary>
    Private Shared Function BodyMaps(w As Integer, h As Integer, faces As IReadOnlyList(Of FaceRegion), amount As Integer) As (X As Mat, Y As Mat)?
        If amount <= 0 OrElse faces Is Nothing OrElse faces.Count = 0 Then Return Nothing
        Dim f = faces.OrderByDescending(Function(q) q.Box.Width * q.Box.Height).First()
        Dim faceW = f.Box.Width * w, faceH = f.Box.Height * h
        Dim c = (f.Box.X + f.Box.Width / 2) * w
        Dim start = (f.Box.Y + f.Box.Height) * h + faceH * 0.05 ' 下巴會被臉的變形往下拉，從下巴再下面一點開始
        Dim length = Math.Max(h - start, faceH * 1.5)
        Dim a = amount / 100.0
        ' 大頭照看得到的身體很短：幅度和波數跟著身體長度縮小，衣服才不會被扯開
        Dim swayAmp = Math.Min(faceW * 0.62, length * 0.2), rippleAmp = Math.Min(faceW * 0.1, length * 0.03), droopAmp = Math.Min(faceH * 0.5, length * 0.2)
        Dim waves = 2.1 * Math.Max(0.45, Math.Min(1.0, length / (faceH * 2.8)))
        Dim step01 = Function(v As Double, e0 As Double, e1 As Double)
                         Dim t = Math.Max(0.0, Math.Min(1.0, (v - e0) / (e1 - e0)))
                         Return t * t * (3 - 2 * t)
                     End Function
        Dim mx(w * h - 1) As Single, my(w * h - 1) As Single
        Parallel.For(0, h, Sub(y)
                               Dim gate = step01(y, start, start + faceH * 0.4)
                               Dim t = Math.Max(0.0, Math.Min(1.0, (y - start) / length))
                               Dim sway = a * gate * swayAmp * Math.Sin(Math.PI * waves * t + 0.35) * (0.45 + 0.55 * t)
                               Dim narrow = 1 - a * 0.45 * gate * step01(t, 0, 0.3)
                               For x = 0 To w - 1
                                   Dim rel = x - (c + sway)
                                   Dim side = Math.Abs(rel) / faceW
                                   Dim ripple = a * gate * rippleAmp * Math.Sin(y / faceW * 5 + side * 1.5) * Math.Min(1.0, side)
                                   Dim droop = a * gate * droopAmp * step01(side, 0.4, 1.5)
                                   mx(y * w + x) = CSng(c + rel / narrow + ripple)
                                   my(y * w + x) = CSng(y - droop)
                               Next
                           End Sub)
        Dim matX As New Mat(h, w, MatType.CV_32FC1), matY As New Mat(h, w, MatType.CV_32FC1)
        Marshal.Copy(mx, 0, matX.Data, mx.Length)
        Marshal.Copy(my, 0, matY.Data, my.Length)
        Return (matX, matY)
    End Function

    ''' <summary>身體扭曲（amount 0..100）。回傳新圖；沒有臉或 0 時是複本。</summary>
    Public Shared Function BodyWarp(image As Bitmap, faces As IReadOnlyList(Of FaceRegion), amount As Integer) As Bitmap
        Dim w = image.Width, h = image.Height
        Dim maps = BodyMaps(w, h, faces, amount)
        If maps Is Nothing Then Return image.Clone(New Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb)
        Using mapX = maps.Value.X, mapY = maps.Value.Y, src = BitmapConverter.ToMat(image), dst As New Mat()
            Cv2.Remap(src, dst, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Replicate)
            Return BitmapConverter.ToBitmap(dst)
        End Using
    End Function

    ''' <summary>遮罩做同樣的身體扭曲（圖外面當透明）；沒有臉或 0 時原樣傳回。</summary>
    Public Shared Function BodyWarpMask(mask As Byte(), w As Integer, h As Integer, faces As IReadOnlyList(Of FaceRegion), amount As Integer) As Byte()
        Dim maps = BodyMaps(w, h, faces, amount)
        If maps Is Nothing Then Return mask
        Using mapX = maps.Value.X, mapY = maps.Value.Y, src As New Mat(h, w, MatType.CV_8UC1), dst As New Mat()
            Marshal.Copy(mask, 0, src.Data, mask.Length)
            Cv2.Remap(src, dst, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0))
            Dim out(mask.Length - 1) As Byte
            Marshal.Copy(dst.Data, out, 0, out.Length)
            Return out
        End Using
    End Function

    ''' <summary>遮罩跟著《吶喊》的臉一起變形（臉頰內凹、下巴拉長後，輪廓才對得上）。</summary>
    Public Shared Function WarpMask(mask As Byte(), w As Integer, h As Integer, faces As IReadOnlyList(Of FaceRegion), scream As Integer, shape As ScreamShape) As Byte()
        If scream <= 0 OrElse faces Is Nothing Then Return mask
        Return PortraitRetouch.ScreamWarpMask(mask, w, h, faces, scream / 100.0, shape)
    End Function

    ''' <summary>人身上套一點《吶喊》的波浪色調與筆觸（0..100；臉保留大部分）。回傳新圖。</summary>
    Public Shared Function StrokeTint(image As Bitmap, faces As IReadOnlyList(Of FaceRegion), amount As Integer) As Bitmap
        Dim out = image.Clone(New Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb)
        If amount > 0 Then PortraitRetouch.ScreamTint(out, If(faces, New List(Of FaceRegion)()), amount / 100.0)
        Return out
    End Function

    ''' <summary>人的範圍（遮罩 ≥ 50%）；沒有人時 Rectangle.Empty。</summary>
    Public Shared Function MaskBounds(mask As Byte(), w As Integer, h As Integer) As Rectangle
        Dim x0 = w, y0 = h, x1 = -1, y1 = -1
        For y = 0 To h - 1
            Dim row = y * w
            For x = 0 To w - 1
                If mask(row + x) < 128 Then Continue For
                If x < x0 Then x0 = x
                If x > x1 Then x1 = x
                If y < y0 Then y0 = y
                y1 = y
            Next
        Next
        Return If(x1 < 0, Rectangle.Empty, Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1))
    End Function

    ''' <summary>
    ''' 做出人物圖層：image 依 mask 去背、裁到人的範圍，套上色彩調整與邊緣柔和（0..100），需要時左右翻轉。
    ''' faces 用來決定「只調整臉」的範圍。沒有人時回傳 Nothing。
    ''' </summary>
    Public Shared Function BuildLayer(image As Bitmap, mask As Byte(), faces As IReadOnlyList(Of FaceRegion), color As ScreamPersonColor,
                                      feather As Integer, flip As Boolean) As Bitmap
        Dim w = image.Width, h = image.Height
        Dim alpha = mask
        If feather > 0 Then
            Using m As New Mat(h, w, MatType.CV_8UC1)
                Marshal.Copy(mask, 0, m.Data, mask.Length)
                Cv2.GaussianBlur(m, m, New OpenCvSharp.Size(0, 0), Math.Max(0.5, feather / 100.0 * Math.Max(w, h) * 0.006))
                alpha = New Byte(mask.Length - 1) {}
                Marshal.Copy(m.Data, alpha, 0, alpha.Length)
            End Using
        End If
        Dim b = MaskBounds(alpha, w, h)
        If b.IsEmpty Then Return Nothing
        b.Inflate(2, 2)
        b.Intersect(New Rectangle(0, 0, w, h))

        ' 只調整臉：臉框放大一點的橢圓（含額頭與下巴），邊緣淡出
        Dim faceEll = If(color.FaceOnly AndAlso faces IsNot Nothing,
                         faces.Select(Function(f) (Cx:=(f.Box.X + f.Box.Width / 2) * w, Cy:=(f.Box.Y + f.Box.Height * 0.55) * h,
                                                   Rx:=f.Box.Width * w * 0.7, Ry:=f.Box.Height * h * 0.85)).ToArray(),
                         Nothing)

        Dim src(w * h * 4 - 1) As Byte
        Dim sd = image.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To h - 1
                Marshal.Copy(sd.Scan0 + y * sd.Stride, src, y * w * 4, w * 4)
            Next
        Finally
            image.UnlockBits(sd)
        End Try

        Dim bw = b.Width, bh = b.Height
        Dim dst(bw * bh * 4 - 1) As Byte
        Dim hueRad = color.Hue * Math.PI / 180, cosH = Math.Cos(hueRad), sinH = Math.Sin(hueRad)
        Dim sat = 1 + color.Saturation / 100.0, con = 1 + color.Contrast / 100.0 * If(color.Contrast > 0, 1.0, 0.8)
        Dim bri = color.Brightness / 100.0 * 90, temp = color.Temperature / 100.0 * 40
        Dim identity = color.IsIdentity
        Parallel.For(0, bh, Sub(yy)
                                Dim y = b.Y + yy
                                For xx = 0 To bw - 1
                                    Dim x = b.X + xx
                                    Dim si = (y * w + x) * 4
                                    Dim di = (yy * bw + If(flip, bw - 1 - xx, xx)) * 4
                                    Dim a = alpha(y * w + x)
                                    dst(di + 3) = a
                                    Dim bb As Double = src(si), gg As Double = src(si + 1), rr As Double = src(si + 2)
                                    If a > 0 AndAlso Not identity Then
                                        Dim k = 1.0
                                        If faceEll IsNot Nothing Then
                                            k = 0
                                            For Each e In faceEll
                                                Dim ex = (x - e.Cx) / e.Rx, ey = (y - e.Cy) / e.Ry
                                                Dim d = Math.Sqrt(ex * ex + ey * ey)
                                                k = Math.Max(k, 1 - Math.Max(0, Math.Min(1, (d - 0.75) / 0.4)))
                                            Next
                                        End If
                                        If k > 0.002 Then
                                            Dim nr = rr, ng = gg, nb = bb
                                            AdjustColor(nr, ng, nb, cosH, sinH, sat, con, bri, temp)
                                            rr += (nr - rr) * k : gg += (ng - gg) * k : bb += (nb - bb) * k
                                        End If
                                    End If
                                    dst(di) = ClampB(bb) : dst(di + 1) = ClampB(gg) : dst(di + 2) = ClampB(rr)
                                Next
                            End Sub)
        Dim layer As New Bitmap(bw, bh, PixelFormat.Format32bppArgb)
        Dim ld = layer.LockBits(New Rectangle(0, 0, bw, bh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To bh - 1
                Marshal.Copy(dst, y * bw * 4, ld.Scan0 + y * ld.Stride, bw * 4)
            Next
        Finally
            layer.UnlockBits(ld)
        End Try
        Return layer
    End Function

    ''' <summary>
    ''' 一個像素的色彩調整：色相在 YIQ 的色度平面旋轉（亮度不變）、飽和度＝色度乘倍數、
    ''' 對比以 128 為中心拉開、亮度加減、色溫＝紅加藍減。
    ''' </summary>
    Private Shared Sub AdjustColor(ByRef r As Double, ByRef g As Double, ByRef b As Double, cosH As Double, sinH As Double,
                                   sat As Double, con As Double, bri As Double, temp As Double)
        Dim y = 0.299 * r + 0.587 * g + 0.114 * b
        Dim i = 0.596 * r - 0.274 * g - 0.322 * b
        Dim q = 0.211 * r - 0.523 * g + 0.312 * b
        Dim i2 = (i * cosH - q * sinH) * sat, q2 = (i * sinH + q * cosH) * sat
        y = (y - 128) * con + 128 + bri
        r = y + 0.956 * i2 + 0.621 * q2 + temp
        g = y - 0.272 * i2 - 0.647 * q2 + temp * 0.15
        b = y - 1.106 * i2 + 1.703 * q2 - temp
    End Sub

    Private Shared Function ClampB(v As Double) As Byte
        Return CByte(Math.Max(0, Math.Min(255, Math.Round(v))))
    End Function

    ''' <summary>平均亮度、亮度標準差、平均的 I、Q 色度（YIQ）、平均色溫（R−B）。</summary>
    Private Shared Function Stats(px As Byte(), w As Integer, h As Integer, weight As Func(Of Integer, Integer, Double)) As (L As Double, S As Double, I As Double, Q As Double, Warm As Double, N As Double)
        Dim sw = 0.0, sl = 0.0, sll = 0.0, si = 0.0, sq = 0.0, swarm = 0.0
        Dim stepPx = Math.Max(1, CInt(Math.Sqrt(w * h / 200000.0)))
        For y = 0 To h - 1 Step stepPx
            For x = 0 To w - 1 Step stepPx
                Dim k = weight(x, y)
                If k <= 0 Then Continue For
                Dim o = (y * w + x) * 4
                Dim b As Double = px(o), g As Double = px(o + 1), r As Double = px(o + 2)
                Dim l = 0.299 * r + 0.587 * g + 0.114 * b
                sw += k : sl += l * k : sll += l * l * k
                si += (0.596 * r - 0.274 * g - 0.322 * b) * k
                sq += (0.211 * r - 0.523 * g + 0.312 * b) * k
                swarm += (r - b) * k
            Next
        Next
        If sw <= 0 Then Return (128, 50, 0, 0, 0, 0)
        Dim mean = sl / sw
        Return (mean, Math.Sqrt(Math.Max(1, sll / sw - mean * mean)), si / sw, sq / sw, swarm / sw, sw)
    End Function

    Private Shared Function Pixels(bmp As Bitmap) As Byte()
        Dim w = bmp.Width, h = bmp.Height
        Dim px(w * h * 4 - 1) As Byte
        Dim d = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To h - 1
                Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4)
            Next
        Finally
            bmp.UnlockBits(d)
        End Try
        Return px
    End Function

    ''' <summary>
    ''' 建議的色彩調整：讓人物（或只有臉）的亮度、對比、色溫、飽和度往背景靠一半（都有上限，不會改得面目全非），
    ''' 色相只往背景的主色轉一點點。背景是名畫時，人會帶一點暖色、對比稍強，比較像畫進去的。
    ''' </summary>
    Public Shared Function SuggestColor(image As Bitmap, mask As Byte(), faces As IReadOnlyList(Of FaceRegion), backdrop As Bitmap, faceOnly As Boolean) As ScreamPersonColor
        Dim w = image.Width, h = image.Height
        Dim px = Pixels(image), bp = Pixels(backdrop)
        Dim faceEll = If(faces, New List(Of FaceRegion)()).Select(Function(f) (Cx:=(f.Box.X + f.Box.Width / 2) * w, Cy:=(f.Box.Y + f.Box.Height * 0.55) * h,
                                                                             Rx:=f.Box.Width * w * 0.55, Ry:=f.Box.Height * h * 0.7)).ToArray()
        Dim inFace = Function(x As Integer, y As Integer) faceEll.Any(Function(e) ((x - e.Cx) / e.Rx) ^ 2 + ((y - e.Cy) / e.Ry) ^ 2 < 1)
        Dim p = Stats(px, w, h, Function(x, y) If(mask(y * w + x) < 128 OrElse (faceOnly AndAlso faceEll.Length > 0 AndAlso Not inFace(x, y)), 0.0, 1.0))
        Dim q = Stats(bp, backdrop.Width, backdrop.Height, Function(x, y) 1.0)
        Dim clampI = Function(v As Double, lim As Integer) CInt(Math.Round(Math.Max(-lim, Math.Min(lim, v))))
        Dim c As New ScreamPersonColor With {.FaceOnly = faceOnly}
        c.Brightness = clampI((q.L - p.L) * 0.5 / 0.9, 30)
        c.Contrast = clampI((q.S / p.S - 1) * 100 * 0.5, 35)
        c.Temperature = clampI((q.Warm - p.Warm) * 0.4 / 0.4, 40)
        Dim pc = Math.Sqrt(p.I * p.I + p.Q * p.Q), qc = Math.Sqrt(q.I * q.I + q.Q * q.Q)
        c.Saturation = clampI((qc / Math.Max(4, pc) - 1) * 100 * 0.35, 35)
        ' 色相：人和背景的平均色度方向差多少，轉 20%（最多 ±20 度）
        If pc > 3 AndAlso qc > 3 Then
            Dim d = (Math.Atan2(q.Q, q.I) - Math.Atan2(p.Q, p.I)) * 180 / Math.PI
            While d > 180 : d -= 360 : End While
            While d < -180 : d += 360 : End While
            c.Hue = clampI(d * 0.2, 20)
        End If
        Return c
    End Function

    ''' <summary>
    ''' 建議的位置（畫布 0..1）：像原畫一樣站在畫面下方中間，也可以靠左、靠右三分之一。
    ''' 半身像（人碰到圖的下緣）貼齊畫布下緣、高約 62%；全身像腳踩在下緣上方一點、高約 70%。
    ''' </summary>
    Public Shared Function SuggestPlacements(layerW As Integer, layerH As Integer, croppedAtBottom As Boolean, canvasW As Integer, canvasH As Integer) As List(Of ScreamPlacement)
        Dim size = If(croppedAtBottom, 0.62, 0.7)
        Dim ly = If(croppedAtBottom, 1 - size / 2, 0.97 - size / 2)
        ' 太寬時縮小，左右不要超出畫布太多
        Dim aspect = layerW / CDbl(Math.Max(1, layerH))
        Dim widthFrac = size * canvasH * aspect / canvasW
        If widthFrac > 0.9 Then
            size *= 0.9 / widthFrac
            ly = If(croppedAtBottom, 1 - size / 2, 0.97 - size / 2)
            widthFrac = 0.9
        End If
        Dim side = Math.Max(widthFrac / 2 + 0.02, 1 / 3.0)
        Return New List(Of ScreamPlacement) From {
            New ScreamPlacement With {.Name = Lang.T("中間（像原畫）"), .X = 0.5, .Y = ly, .Size = size},
            New ScreamPlacement With {.Name = Lang.T("靠左三分之一"), .X = Math.Min(0.5, side), .Y = ly, .Size = size},
            New ScreamPlacement With {.Name = Lang.T("靠右三分之一"), .X = Math.Max(0.5, 1 - side), .Y = ly, .Size = size}}
    End Function

    ''' <summary>人碰到圖的下緣（半身像）：最下面一列有人。</summary>
    Public Shared Function CroppedAtBottom(mask As Byte(), w As Integer, h As Integer) As Boolean
        Dim n = 0
        Dim row = (h - 1) * w
        For x = 0 To w - 1
            If mask(row + x) >= 128 Then n += 1
        Next
        Return n >= Math.Max(3, w \ 50)
    End Function

    ''' <summary>合成：背景（縮放成 canvas 大小）上放人物圖層，中心 (x, y)、高 size（都是畫布比例）。</summary>
    Public Shared Function Compose(backdrop As Bitmap, layer As Bitmap, canvas As System.Drawing.Size, x As Double, y As Double, size As Double) As Bitmap
        Dim out As New Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(out)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
            g.DrawImage(backdrop, 0, 0, canvas.Width, canvas.Height)
            If layer IsNot Nothing Then g.DrawImage(layer, LayerRect(layer, canvas, x, y, size))
        End Using
        Return out
    End Function

    ''' <summary>人物圖層在畫布上的範圍。</summary>
    Public Shared Function LayerRect(layer As Bitmap, canvas As System.Drawing.Size, x As Double, y As Double, size As Double) As RectangleF
        Dim lh = size * canvas.Height
        Dim lw = lh * layer.Width / Math.Max(1.0, layer.Height)
        Return New RectangleF(CSng(x * canvas.Width - lw / 2), CSng(y * canvas.Height - lh / 2), CSng(lw), CSng(lh))
    End Function

    ''' <summary>畫布大小：背景圖的比例，長邊至少 2048（小的名畫放大，大的照片照原樣，最多 6000）。</summary>
    Public Shared Function CanvasSize(backdrop As Bitmap) As System.Drawing.Size
        Dim longSide = Math.Max(backdrop.Width, backdrop.Height)
        Dim k = Math.Min(6000, Math.Max(2048, longSide)) / CDbl(longSide)
        Return New System.Drawing.Size(Math.Max(1, CInt(Math.Round(backdrop.Width * k))), Math.Max(1, CInt(Math.Round(backdrop.Height * k))))
    End Function
End Class
