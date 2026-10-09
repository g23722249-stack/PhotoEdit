Imports System.Drawing
Imports System.Runtime.InteropServices
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>
''' 宮崎風（手繪動畫背景＋賽璐珞人物）：演算法版，不用 AI 模型。
''' 人（人像去背遮罩）：強力保邊平滑 → 亮度分 3～4 階的柔和賽璐珞上色、色彩略飽和、細的深褐輪廓線；
''' 背景：更粗的平塗色塊（廣告顏料感）；整體：陰影偏藍紫、亮部偏暖、亮部微微泛光；
''' 天空（偏藍或過曝、在上方且連到上緣）：重畫天藍漸層＋程式畫的積雨雲。
''' 在長邊 1000 的縮圖上算，再放大回原尺寸。
''' </summary>
Public NotInheritable Class MiyazakiStyle
    Private Sub New()
    End Sub

    Public Const WorkSize As Integer = 1000

    Private Shared Function Smooth(v As Double, e0 As Double, e1 As Double) As Double
        Dim t = Math.Max(0, Math.Min(1, (v - e0) / Math.Max(0.0001, e1 - e0)))
        Return t * t * (3 - 2 * t)
    End Function

    ''' <summary>柔和的色階：n 階，階與階之間用寬 w 的平滑過渡（賽璐珞的明暗交界）。</summary>
    Private Shared Function SoftPosterize(x As Double, n As Integer, w As Double) As Double
        Dim v = Math.Max(0, Math.Min(0.9999, x)) * n
        Dim i = Math.Floor(v)
        Dim fr = v - i
        Return (i + Smooth(fr, 0.5 - w / 2, 0.5 + w / 2)) / n
    End Function

    Private Shared Function Floats(m As Mat) As Single()
        Dim n = CInt(m.Total() * m.Channels())
        Dim a(n - 1) As Single
        Using c = If(m.IsContinuous(), Nothing, m.Clone())
            Marshal.Copy(If(c, m).Data, a, 0, n)
        End Using
        Return a
    End Function

    Private Shared Function LabOf(bgr8 As Mat) As Single()
        Using f As New Mat(), lab As New Mat()
            bgr8.ConvertTo(f, MatType.CV_32FC3, 1 / 255.0)
            Cv2.CvtColor(f, lab, ColorConversionCodes.BGR2Lab)
            Return Floats(lab)
        End Using
    End Function

    ''' <param name="personMask">人像去背遮罩（灰階，白＝人）；Nothing＝全部當背景。</param>
    ''' <param name="clouds">天空要不要畫積雨雲。</param>
    Public Shared Function Apply(source As Bitmap, personMask As Bitmap, Optional clouds As Boolean = True) As Bitmap
        Dim W = source.Width, H = source.Height
        Dim k = Math.Min(1.0, WorkSize / CDbl(Math.Max(W, H)))
        Dim ww = Math.Max(8, CInt(W * k)), hh = Math.Max(8, CInt(H * k))
        Dim n = ww * hh
        Using full = BitmapConverter.ToMat(source), bgr0 As New Mat(), bgr As New Mat()
            Cv2.CvtColor(full, bgr0, If(full.Channels() = 4, ColorConversionCodes.BGRA2BGR, ColorConversionCodes.RGB2BGR))
            Cv2.Resize(bgr0, bgr, New OpenCvSharp.Size(ww, hh), 0, 0, InterpolationFlags.Area)

            ' 人的範圍
            Dim pm(n - 1) As Single
            If personMask IsNot Nothing Then
                Using mm = BitmapConverter.ToMat(personMask), g As New Mat(), gs As New Mat(), gf As New Mat()
                    If mm.Channels() = 1 Then
                        mm.CopyTo(g)
                    Else
                        Cv2.CvtColor(mm, g, If(mm.Channels() = 4, ColorConversionCodes.BGRA2GRAY, ColorConversionCodes.BGR2GRAY))
                    End If
                    Cv2.Resize(g, gs, New OpenCvSharp.Size(ww, hh), 0, 0, InterpolationFlags.Area)
                    gs.ConvertTo(gf, MatType.CV_32FC1, 1 / 255.0)
                    Cv2.GaussianBlur(gf, gf, New OpenCvSharp.Size(0, 0), 1.5)
                    pm = Floats(gf)
                End Using
            End If

            ' 平滑：人＝賽璐珞（保留五官），背景＝更粗的色塊
            Dim celLab, paintLab, celAB As Single(), lineMask As Single()
            Using cel As New Mat(), paint As New Mat(), t As New Mat()
                Cv2.PyrMeanShiftFiltering(bgr, cel, 9, 22, 1)
                Cv2.BilateralFilter(cel, t, 7, 28, 5)
                Cv2.BilateralFilter(t, cel, 7, 28, 5)
                Cv2.PyrMeanShiftFiltering(bgr, paint, 12, 28, 1)
                Cv2.BilateralFilter(paint, t, 9, 40, 9)
                t.CopyTo(paint)
                celLab = LabOf(cel)
                paintLab = LabOf(paint)
                ' 顏色（a、b）再抹平一點：賽璐珞的色塊
                Using cf As New Mat(), lab As New Mat(), blurred As New Mat()
                    cel.ConvertTo(cf, MatType.CV_32FC3, 1 / 255.0)
                    Cv2.CvtColor(cf, lab, ColorConversionCodes.BGR2Lab)
                    Cv2.GaussianBlur(lab, blurred, New OpenCvSharp.Size(0, 0), 4.0)
                    celAB = Floats(blurred)
                End Using
                ' 輪廓線：亮度的高斯差（比周圍暗的細線）
                Using lf As New Mat(), g1 As New Mat(), g2 As New Mat(), gray As New Mat()
                    Cv2.CvtColor(cel, gray, ColorConversionCodes.BGR2GRAY)
                    gray.ConvertTo(lf, MatType.CV_32FC1, 1 / 255.0)
                    Cv2.GaussianBlur(lf, g1, New OpenCvSharp.Size(0, 0), 0.8)
                    Cv2.GaussianBlur(lf, g2, New OpenCvSharp.Size(0, 0), 1.8)
                    Dim a1 = Floats(g1), a2 = Floats(g2)
                    lineMask = New Single(n - 1) {}
                    For i = 0 To n - 1
                        lineMask(i) = CSng(Smooth(a2(i) - a1(i), 0.014, 0.05))
                    Next
                End Using
            End Using

            ' 天空：偏藍或過曝、低彩度的亮區，在上方且連到上緣（不是人）
            Dim sky = If(clouds, FindSky(paintLab, pm, ww, hh), New Single(n - 1) {})

            Dim outLab(n * 3 - 1) As Single
            For i = 0 To n - 1
                Dim p = pm(i)
                ' 人：亮度分 3 階（柔和交界），顏色抹平、略飽和
                Dim lc = (celLab(i * 3) * 0.35 + celAB(i * 3) * 0.65) / 100.0 ' 亮度也抹平：臉上不要一塊塊的明暗
                Dim lcq = SoftPosterize(lc, 3, 0.35)
                Dim Lp = lc + (lcq - lc) * 0.55
                Dim ap = celAB(i * 3 + 1) * 1.12, bp = celAB(i * 3 + 2) * 1.12
                ' 背景：粗色塊，分 6 階較淡
                Dim lb = paintLab(i * 3) / 100.0
                Dim Lb2 = lb + (SoftPosterize(lb, 6, 0.3) - lb) * 0.45
                Dim ab = paintLab(i * 3 + 1) * 1.18, bb = paintLab(i * 3 + 2) * 1.18
                Dim L = Lb2 + (Lp - Lb2) * p, a = ab + (ap - ab) * p, b = bb + (bp - bb) * p
                ' 陰影偏藍紫、稍微提亮（不死黑）；亮部偏暖
                Dim sh = 1 - Smooth(L, 0.15, 0.55)
                L += 0.06 * sh
                a += 1.5 * sh : b -= 4.5 * sh
                Dim hl = Smooth(L, 0.62, 0.95)
                a += 1.5 * hl : b += 7 * hl
                ' 輪廓線：深褐、人身上清楚、背景淡
                Dim ln = lineMask(i) * (0.25 + 0.75 * p)
                L += (0.2 - L) * ln * 0.85
                a += (9 - a) * ln * 0.6 : b += (14 - b) * ln * 0.6
                outLab(i * 3) = CSng(Math.Max(0, Math.Min(100, L * 100)))
                outLab(i * 3 + 1) = CSng(a)
                outLab(i * 3 + 2) = CSng(b)
            Next
            If clouds Then PaintSky(outLab, sky, ww, hh)

            Using lab As New Mat(hh, ww, MatType.CV_32FC3), rgbF As New Mat(), bloom As New Mat(), out8 As New Mat(), big As New Mat()
                Marshal.Copy(outLab, 0, lab.Data, outLab.Length)
                Cv2.CvtColor(lab, rgbF, ColorConversionCodes.Lab2BGR)
                ' 亮部泛光（暖色）
                Dim px = Floats(rgbF)
                Dim glow(n * 3 - 1) As Single
                For i = 0 To n - 1
                    Dim y = 0.114 * px(i * 3) + 0.587 * px(i * 3 + 1) + 0.299 * px(i * 3 + 2)
                    Dim gk = CSng(Smooth(y, 0.7, 1.0))
                    glow(i * 3) = gk * 0.85F : glow(i * 3 + 1) = gk * 0.95F : glow(i * 3 + 2) = gk
                Next
                Using gm As New Mat(hh, ww, MatType.CV_32FC3)
                    Marshal.Copy(glow, 0, gm.Data, glow.Length)
                    Cv2.GaussianBlur(gm, bloom, New OpenCvSharp.Size(0, 0), Math.Max(ww, hh) * 0.015)
                End Using
                Dim bl = Floats(bloom)
                For i = 0 To px.Length - 1
                    Dim v = Math.Max(0F, Math.Min(1.0F, px(i)))
                    px(i) = 1 - (1 - v) * (1 - 0.3F * bl(i))
                Next
                Marshal.Copy(px, 0, rgbF.Data, px.Length)
                rgbF.ConvertTo(out8, MatType.CV_8UC3, 255)
                Cv2.Resize(out8, big, New OpenCvSharp.Size(W, H), 0, 0, InterpolationFlags.Cubic)
                Return BitmapConverter.ToBitmap(big)
            End Using
        End Using
    End Function

    ''' <summary>天空遮罩：亮、偏藍或低彩度（陰天、過曝），不是人，而且從上緣連過來。</summary>
    Private Shared Function FindSky(lab As Single(), pm As Single(), w As Integer, h As Integer) As Single()
        Dim n = w * h
        Dim cand(n - 1) As Boolean
        For i = 0 To n - 1
            Dim L = lab(i * 3), a = lab(i * 3 + 1), b = lab(i * 3 + 2)
            Dim blue = b < -6 AndAlso L > 45
            Dim pale = L > 70 AndAlso Math.Abs(a) < 8 AndAlso Math.Abs(b) < 12
            cand(i) = (blue OrElse pale) AndAlso pm(i) < 0.3
        Next
        ' 從上緣往下長（4 連通）
        Dim sky(n - 1) As Single
        Dim q As New Queue(Of Integer)()
        For x = 0 To w - 1
            If cand(x) Then sky(x) = 1 : q.Enqueue(x)
        Next
        While q.Count > 0
            Dim i = q.Dequeue()
            Dim x = i Mod w, y = i \ w
            For Each j In {If(x > 0, i - 1, -1), If(x < w - 1, i + 1, -1), If(y > 0, i - w, -1), If(y < h - 1, i + w, -1)}
                If j >= 0 AndAlso cand(j) AndAlso sky(j) = 0 Then sky(j) = 1 : q.Enqueue(j)
            Next
        End While
        ' 太小（不到 4%）就不算天空
        If sky.Sum(Function(v) CDbl(v)) < n * 0.04 Then Return New Single(n - 1) {}
        Using m As New Mat(h, w, MatType.CV_32FC1), bm As New Mat()
            Marshal.Copy(sky, 0, m.Data, n)
            Cv2.GaussianBlur(m, bm, New OpenCvSharp.Size(0, 0), Math.Max(1.5, w * 0.004))
            Return Floats(bm)
        End Using
    End Function

    ''' <summary>天空重畫：往上越深的天藍漸層＋程式畫的積雨雲（頂端亮白、底部淡紫灰）。</summary>
    Private Shared Sub PaintSky(lab As Single(), sky As Single(), w As Integer, h As Integer)
        If sky.All(Function(v) v < 0.01) Then Return
        ' 天空的範圍高度（雲放在天空下半部）
        Dim rows = Enumerable.Range(0, h).Where(Function(y) Enumerable.Range(0, w).Any(Function(x) sky(y * w + x) > 0.5)).ToList()
        Dim skyBottom = If(rows.Count > 0, rows.Max(), h \ 3)
        Dim rnd As New Random(11)
        ' 雲：幾團由許多圓疊成的積雲（底平、頂圓），用柔和的距離場
        Dim cloud(w * h - 1) As Single, shade(w * h - 1) As Single
        Dim nClouds = 3 + rnd.Next(3)
        For c = 0 To nClouds - 1
            Dim cx = w * (0.1 + 0.8 * rnd.NextDouble())
            Dim baseY = skyBottom * (0.55 + 0.35 * rnd.NextDouble())
            Dim size = w * (0.12 + 0.12 * rnd.NextDouble())
            For bIdx = 0 To 14
                Dim bx = cx + (rnd.NextDouble() - 0.5) * size * 1.8
                Dim r = size * (0.25 + 0.35 * rnd.NextDouble()) * (1 - Math.Abs(bx - cx) / (size * 1.4))
                If r <= 2 Then Continue For
                Dim by = baseY - r * (0.6 + 0.6 * rnd.NextDouble())
                Dim x0 = Math.Max(0, CInt(bx - r - 2)), x1 = Math.Min(w - 1, CInt(bx + r + 2))
                Dim y0 = Math.Max(0, CInt(by - r - 2)), y1 = Math.Min(h - 1, CInt(Math.Min(baseY + 2, by + r + 2)))
                For y = y0 To y1
                    For x = x0 To x1
                        Dim dd = Math.Sqrt((x - bx) ^ 2 + (y - by) ^ 2) / r
                        If dd > 1.05 OrElse y > baseY Then Continue For
                        Dim i = y * w + x
                        Dim v = CSng(1 - Smooth(dd, 0.85, 1.05))
                        If v > cloud(i) Then cloud(i) = v
                        ' 光從上方：每個球的下半部偏暗；越靠雲底越暗
                        Dim dark = CSng(Smooth((y - by) / r, -0.2, 0.9) * 0.6 + Smooth(y, baseY - r * 0.6, baseY) * 0.4)
                        If v > 0.3 Then shade(i) = Math.Max(shade(i) * 0.5F, dark)
                    Next
                Next
            Next
        Next
        For y = 0 To h - 1
            Dim ty = y / Math.Max(1.0, skyBottom)
            ' 天藍：上深下淺
            Dim sL = 62 + 22 * ty, sA = -4 + 2 * ty, sB = -38 + 20 * ty
            For x = 0 To w - 1
                Dim i = y * w + x
                Dim s = sky(i)
                If s < 0.01 Then Continue For
                Dim L = sL, a = sA, b = sB
                Dim cv = cloud(i)
                If cv > 0 Then
                    ' 雲：亮白 → 淡紫灰（分兩階，交界柔和）
                    Dim d = SoftPosterize(shade(i), 2, 0.4)
                    Dim cL = 97 - 22 * d, cA = 1 + 4 * d, cB = 2 - 12 * d
                    L += (cL - L) * cv : a += (cA - a) * cv : b += (cB - b) * cv
                End If
                lab(i * 3) += CSng((L - lab(i * 3)) * s)
                lab(i * 3 + 1) += CSng((a - lab(i * 3 + 1)) * s)
                lab(i * 3 + 2) += CSng((b - lab(i * 3 + 2)) * s)
            Next
        Next
    End Sub
End Class
