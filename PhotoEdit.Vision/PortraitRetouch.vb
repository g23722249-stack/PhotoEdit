Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>
''' 人像修飾（美顏），只作用在偵測到的臉附近：
''' 肌膚＝磨皮、臉部提亮、勻膚、去紅、美白、膚色、去油光、去痘、黑眼圈；五官＝亮眼、大眼、牙齒美白、腮紅、修容。
''' 每張臉先縮放到固定大小（臉寬 WorkFaceSize）算遮罩與顏色修正量，再放大套回原圖，所以預覽（1600 縮圖）與全尺寸匯出的效果一致。
''' 臉的位置只有 YuNet 的 5 個點（兩眼、鼻尖、兩嘴角），臉頰、眼下、鼻樑等位置由這 5 點推估。
''' </summary>
Public NotInheritable Class PortraitRetouch
    Private Sub New()
    End Sub

    Private Const WorkFaceSize As Double = 256

    ''' <summary>回傳修飾後的新圖；沒有臉或沒有人像參數時回傳 Nothing（表示不變）。</summary>
    Public Shared Function Apply(source As Bitmap, faces As IReadOnlyList(Of FaceRegion), recipe As EditRecipe) As Bitmap
        If faces Is Nothing OrElse faces.Count = 0 OrElse Not recipe.HasPortrait Then Return Nothing

        Dim result = source.Clone(New Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb)
        Using bgra = BitmapConverter.ToMat(source), bgr As New Mat()
            Cv2.CvtColor(bgra, bgr, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2BGR, ColorConversionCodes.RGB2BGR))
            Dim data = result.LockBits(New Rectangle(0, 0, result.Width, result.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
            Try
                Dim px(data.Stride * result.Height - 1) As Byte
                Marshal.Copy(data.Scan0, px, 0, px.Length)
                For Each f In faces
                    Dim b = recipe.BeautyFor(f.Box)
                    If b.IsEmpty Then Continue For
                    RetouchFace(bgr, px, data.Stride, f, b)
                Next
                ' 大眼最後做（變形會移動像素，要在顏色修飾之後）
                For Each f In faces
                    Dim b = recipe.BeautyFor(f.Box)
                    If b.EyeEnlarge > 0 Then EnlargeEyes(px, data.Stride, result.Width, result.Height, f, b.EyeEnlarge / 100.0)
                    If f.Dense IsNot Nothing AndAlso (b.FaceSlim <> 0 OrElse b.VFace <> 0 OrElse b.Chin <> 0 OrElse b.NoseSlim <> 0) Then
                        WarpFaceShape(px, data.Stride, result.Width, result.Height, f.Dense, b)
                    End If
                Next
                Marshal.Copy(px, 0, data.Scan0, px.Length)
            Finally
                result.UnlockBits(data)
            End Try
        End Using
        Return result
    End Function

    Private Shared Sub RetouchFace(bgr As Mat, px As Byte(), stride As Integer, f As FaceRegion, b As BeautySettings)
        Dim smooth = b.Smoothing / 100.0, bright = b.Brighten / 100.0, eyes = b.Eyes / 100.0
        Dim imgW = bgr.Cols, imgH = bgr.Rows
        Dim fx = f.Box.X * imgW, fy = f.Box.Y * imgH
        Dim fw = f.Box.Width * imgW, fh = f.Box.Height * imgH
        If fw < 8 OrElse fh < 8 Then Return

        Dim roi = New Rect(CInt(fx - fw * 0.35), CInt(fy - fh * 0.35), CInt(fw * 1.7), CInt(fh * 1.7)).
                  Intersect(New Rect(0, 0, imgW, imgH))
        If roi.Width < 8 OrElse roi.Height < 8 Then Return

        Dim k = WorkFaceSize / fw
        Dim sw = Math.Max(8, CInt(roi.Width * k)), sh = Math.Max(8, CInt(roi.Height * k))
        Dim roiSize As New OpenCvSharp.Size(roi.Width, roi.Height)
        ' 原圖座標換到縮放後的 ROI 座標。
        Dim toSmall = Function(x As Double, y As Double) New Point2f(CSng((x - roi.X) * k), CSng((y - roi.Y) * k))
        Dim eyeR = CInt(fw * 0.11 * k)
        Dim lm = f.Landmarks.Select(Function(p) toSmall(p.X * imgW, p.Y * imgH)).ToArray()
        Dim denseSmall = f.Dense?.Select(Function(p) toSmall(p.X * imgW, p.Y * imgH)).ToArray()
        ' 臉的參考長度：兩眼距離（縮放後）
        Dim eyeDist = Math.Max(8.0, Math.Sqrt((lm(1).X - lm(0).X) ^ 2 + (lm(1).Y - lm(0).Y) ^ 2))

        Using roiMat As New Mat(bgr, roi), small As New Mat(), pass1 As New Mat(), smoothSmall As New Mat(),
              ycc As New Mat(), skin As New Mat(), shape As New Mat(sh, sw, MatType.CV_8UC1, Scalar.All(0)),
              mask As New Mat(), eyeMask As New Mat(sh, sw, MatType.CV_8UC1, Scalar.All(0)),
              smoothBig As New Mat(), maskBig As New Mat(), eyeBig As New Mat(), roiBlur As New Mat()

            Cv2.Resize(roiMat, small, New OpenCvSharp.Size(sw, sh), 0, 0, InterpolationFlags.Area)
            ' 雙邊濾波兩次：抹平膚質細紋，保留五官邊緣。
            Cv2.BilateralFilter(small, pass1, 9, 55, 9)
            Cv2.BilateralFilter(pass1, smoothSmall, 7, 30, 7)

            ' 膚色（YCrCb 範圍）∩ 臉部橢圓，扣掉眼睛與嘴巴，邊緣羽化。
            Cv2.CvtColor(small, ycc, ColorConversionCodes.BGR2YCrCb)
            Cv2.InRange(ycc, New Scalar(0, 133, 77), New Scalar(255, 178, 132), skin)
            Dim center = toSmall(fx + fw / 2, fy + fh * 0.52)
            Cv2.Ellipse(shape, New RotatedRect(center, New Size2f(fw * 1.1 * k, fh * 1.35 * k), 0), Scalar.All(255), -1)
            For n = 0 To 1
                Cv2.Circle(shape, ToPoint(lm(n)), eyeR, Scalar.All(0), -1)
                Cv2.Circle(eyeMask, ToPoint(lm(n)), CInt(eyeR * 0.85), Scalar.All(255), -1)
            Next
            ' 眉毛不算皮膚（否則磨皮、勻膚、美白、去痘會把眉毛抹淡）：有 68 點用眉毛的點，否則用眼睛上方推估
            If denseSmall IsNot Nothing Then
                For Each start In {17, 22}
                    Dim brow = denseSmall.Skip(start).Take(5).Select(Function(p) ToPoint(p)).ToArray()
                    Cv2.Polylines(shape, {brow}, False, Scalar.All(0), Math.Max(3, CInt(eyeDist * 0.16)), LineTypes.AntiAlias)
                Next
            Else
                For n = 0 To 1
                    Cv2.Ellipse(shape, New RotatedRect(New Point2f(lm(n).X, CSng(lm(n).Y - eyeDist * 0.36)),
                                                       New Size2f(CSng(eyeDist * 0.62), CSng(eyeDist * 0.24)), 0), Scalar.All(0), -1)
                Next
            End If
            Dim mouth As New Point2f((lm(3).X + lm(4).X) / 2, (lm(3).Y + lm(4).Y) / 2)
            Dim mouthHalf = Math.Max(4.0, Math.Abs(lm(4).X - lm(3).X) * 0.65)
            Cv2.Ellipse(shape, New RotatedRect(mouth, New Size2f(CSng(mouthHalf * 2), CSng(eyeR * 1.6)), 0), Scalar.All(0), -1)
            Cv2.BitwiseAnd(skin, shape, mask)
            Cv2.GaussianBlur(mask, mask, New OpenCvSharp.Size(0, 0), Math.Max(1.0, WorkFaceSize * 0.035))
            Cv2.GaussianBlur(eyeMask, eyeMask, New OpenCvSharp.Size(0, 0), Math.Max(1.0, eyeR * 0.4))

            ' 其他美顏：在縮小的臉上算出每個像素要加減多少（BGR，float），放大後加回原圖。
            Using soft = SoftSkinMask(ycc, skin, shape, center, fw * k, fh * k),
                  delta = ColorDelta(small, soft, lm, eyeDist, eyeR, mouth, mouthHalf, b, denseSmall, center, fw * k, fh * k), deltaBig As New Mat()
                Cv2.Resize(smoothSmall, smoothBig, roiSize, 0, 0, InterpolationFlags.Linear)
                Cv2.Resize(mask, maskBig, roiSize, 0, 0, InterpolationFlags.Linear)
                Cv2.Resize(eyeMask, eyeBig, roiSize, 0, 0, InterpolationFlags.Linear)
                Cv2.GaussianBlur(roiMat, roiBlur, New OpenCvSharp.Size(0, 0), Math.Max(0.8, fw * 0.008))
                Dim dl As Single() = Nothing
                If delta IsNot Nothing Then
                    Cv2.Resize(delta, deltaBig, roiSize, 0, 0, InterpolationFlags.Linear)
                    dl = GetFloats(deltaBig)
                End If

                Dim sm = GetBytes(smoothBig), mk = GetBytes(maskBig), ey = GetBytes(eyeBig), bl = GetBytes(roiBlur)
                Dim sb As Byte() = Nothing
                If bright > 0 Then
                    Using softBig As New Mat()
                        Cv2.Resize(soft, softBig, roiSize, 0, 0, InterpolationFlags.Linear)
                        sb = GetBytes(softBig)
                    End Using
                End If
                For y = 0 To roi.Height - 1
                    For x = 0 To roi.Width - 1
                        Dim mi = y * roi.Width + x
                        Dim m = mk(mi) / 255.0, e = ey(mi) / 255.0 * eyes, sf = If(sb Is Nothing, m, sb(mi) / 255.0)
                        Dim ci = mi * 3
                        If m < 0.004 AndAlso sf < 0.004 AndAlso e < 0.004 AndAlso (dl Is Nothing OrElse (dl(ci) = 0 AndAlso dl(ci + 1) = 0 AndAlso dl(ci + 2) = 0)) Then Continue For
                        Dim pi = (roi.Y + y) * stride + (roi.X + x) * 4
                        For ch = 0 To 2
                            Dim o As Double = px(pi + ch)
                            ' 最多只混入 55% 的平滑結果，保留膚質紋理，避免塑膠感。
                            Dim v = o + (sm(ci + ch) - o) * m * smooth * 0.55
                            v += (255 - v) * sf * bright * 0.2 ' 提亮用柔和遮罩：頭髮不會跟著變亮
                            If e > 0 Then
                                v += (o - bl(ci + ch)) * e * 1.2      ' 眼睛細節銳利一點
                                v += (255 - v) * e * 0.12             ' 並稍微提亮
                            End If
                            If dl IsNot Nothing Then v += dl(ci + ch)
                            px(pi + ch) = ImagePipeline.ClampByte(v)
                        Next
                    Next
                Next
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' 顏色類美顏用的柔和皮膚遮罩：先量這張臉中央的平均膚色（Cr、Cb、亮度），每個像素依「和平均膚色多接近」給 0..1，
    ''' 太暗的（頭髮、鼻孔）降低，再乘上羽化過的臉部橢圓（已挖掉眼睛、嘴巴）。
    ''' 比固定範圍的膚色判斷平順：陰影裡的皮膚不會被漏掉，美白、膚色不會一塊一塊。
    ''' </summary>
    Private Shared Function SoftSkinMask(ycc As Mat, skin As Mat, shape As Mat, center As Point2f, faceW As Double, faceH As Double) As Mat
        Dim w = ycc.Cols, h = ycc.Rows, n = w * h
        Dim y = GetBytes(ycc), sk = GetBytes(skin)
        ' 臉中央（0.55 倍的橢圓）裡的膚色像素當樣本
        Dim sY = 0.0, sCr = 0.0, sCb = 0.0, cnt = 0
        For pass = 0 To 1
            For i = 0 To n - 1
                If sk(i) = 0 Then Continue For
                Dim px = i Mod w, py = i \ w
                If pass = 0 AndAlso ((px - center.X) / (faceW * 0.3)) ^ 2 + ((py - center.Y) / (faceH * 0.35)) ^ 2 > 1 Then Continue For
                sY += y(i * 3) : sCr += y(i * 3 + 1) : sCb += y(i * 3 + 2) : cnt += 1
            Next
            If cnt >= 30 Then Exit For
            sY = 0 : sCr = 0 : sCb = 0 : cnt = 0
        Next
        Dim mY = If(cnt > 0, sY / cnt, 150.0), mCr = If(cnt > 0, sCr / cnt, 150.0), mCb = If(cnt > 0, sCb / cnt, 110.0)
        ' 顏色像不像皮膚：大範圍模糊，皮膚中間的小塊異色（泛紅、油光亮點、痘痘）也算在內
        Dim member(n - 1) As Single
        For i = 0 To n - 1
            Dim d = Math.Sqrt((y(i * 3 + 1) - mCr) ^ 2 + (y(i * 3 + 2) - mCb) ^ 2)
            member(i) = CSng(1 - Smooth(d, 10, 28))
        Next
        member = BlurArray(member, w, h, WorkFaceSize * 0.035)
        ' 亮度不模糊：頭髮、鼻孔等很暗的地方邊界要清楚
        For i = 0 To n - 1
            member(i) = CSng(Math.Min(1, member(i) * 1.25) * Smooth(y(i * 3), mY * 0.3, mY * 0.55))
        Next
        Using shapeF As New Mat()
            shape.ConvertTo(shapeF, MatType.CV_32FC1, 1 / 255.0)
            Cv2.GaussianBlur(shapeF, shapeF, New OpenCvSharp.Size(0, 0), Math.Max(1.0, WorkFaceSize * 0.04))
            Dim sh = GetFloats(shapeF)
            Dim outBytes(n - 1) As Byte
            For i = 0 To n - 1
                outBytes(i) = ImagePipeline.ClampByte(member(i) * sh(i) * 255)
            Next
            Dim result As New Mat(h, w, MatType.CV_8UC1)
            Marshal.Copy(outBytes, 0, result.Data, n)
            Return result
        End Using
    End Function

    '=====================================================================
    ' 顏色類的美顏：在縮小的臉（small，8 位元 BGR）上算修正量
    '=====================================================================

    ''' <summary>回傳 CV_32FC3 的修正量（加到原圖上）；沒有任何顏色類效果時 Nothing。</summary>
    Private Shared Function ColorDelta(small As Mat, mask As Mat, lm As Point2f(), eyeDist As Double, eyeR As Integer,
                                       mouth As Point2f, mouthHalf As Double, b As BeautySettings, dense As Point2f(),
                                       faceCenter As Point2f, faceW As Double, faceH As Double) As Mat
        If b.Even = 0 AndAlso b.Redness = 0 AndAlso b.Whiten = 0 AndAlso b.Tone = 0 AndAlso b.Shine = 0 AndAlso b.Blemish = 0 AndAlso
           b.DarkCircles = 0 AndAlso b.Teeth = 0 AndAlso b.Blush = 0 AndAlso b.Contour = 0 AndAlso
           (dense Is Nothing OrElse (b.Lips = 0 AndAlso b.Brows = 0 AndAlso b.EyeShadow = 0 AndAlso b.EyeLiner = 0 AndAlso b.EyeBag = 0)) AndAlso
           Not b.HasLight Then Return Nothing
        Dim w = small.Cols, h = small.Rows, n = w * h
        Dim src = GetBytes(small)                       ' BGR
        Dim m = GetBytes(mask)                          ' 膚色遮罩 0..255
        Dim acc(n * 3 - 1) As Single                    ' 累積的修正量
        Dim lab = ToLab(small)                          ' L, a, b（8 位元，a/b 以 128 為中心）

        ' 皮膚的平均顏色（遮罩加權）
        Dim sumW = 0.0, mL = 0.0, mA = 0.0, mB = 0.0
        For i = 0 To n - 1
            Dim wgt = m(i) / 255.0
            If wgt < 0.5 Then Continue For
            sumW += wgt : mL += lab(i * 3) * wgt : mA += lab(i * 3 + 1) * wgt : mB += lab(i * 3 + 2) * wgt
        Next
        If sumW < 20 Then
            mL = 160 : mA = 140 : mB = 145
        Else
            mL /= sumW : mA /= sumW : mB /= sumW
        End If

        ' 每個像素的 Lab 修正（ΔL、Δa、Δb），最後一次換回 BGR
        Dim dLab(n * 3 - 1) As Single
        Dim addLab = Sub(i As Integer, dLv As Double, dAv As Double, dBv As Double)
                         dLab(i * 3) += CSng(dLv) : dLab(i * 3 + 1) += CSng(dAv) : dLab(i * 3 + 2) += CSng(dBv)
                     End Sub

        ' 去痘先做：斑點補成周圍原本的膚色，之後的美白、膚色等再一起套上（後做的話補上的顏色會比旁邊暗）
        If b.Blemish > 0 Then RemoveBlemishes(small, lab, m, acc, w, h, b.Blemish / 100.0, lm, eyeDist)

        ' 勻膚：頻率分離。低頻（σ2.5）換成只在皮膚裡模糊得更開的版本（σ8），高頻紋理不動；修正量有上限，不抹掉立體感。
        If b.Even > 0 Then
            Using f32 As New Mat(), low As New Mat(), mf As New Mat(), weighted As New Mat(), wideNum As New Mat(), wideDen As New Mat()
                small.ConvertTo(f32, MatType.CV_32FC3)
                Cv2.GaussianBlur(f32, low, New OpenCvSharp.Size(0, 0), 2.5)
                mask.ConvertTo(mf, MatType.CV_32FC1, 1 / 255.0)
                Using mf3 As New Mat()
                    Cv2.Merge({mf, mf, mf}, mf3)
                    Cv2.Multiply(f32, mf3, weighted)
                    Cv2.GaussianBlur(weighted, wideNum, New OpenCvSharp.Size(0, 0), 8)
                    Cv2.GaussianBlur(mf3, wideDen, New OpenCvSharp.Size(0, 0), 8)
                End Using
                Dim lo = GetFloats(low), num = GetFloats(wideNum), den = GetFloats(wideDen)
                Dim amt = b.Even / 100.0
                For i = 0 To n - 1
                    Dim wgt = m(i) / 255.0
                    If wgt < 0.01 Then Continue For
                    For ch = 0 To 2
                        Dim j = i * 3 + ch
                        Dim wide = If(den(j) > 0.05F, num(j) / den(j), lo(j))
                        ' 只抹小色塊：修正量限制在 ±10，鼻子、臉頰的立體明暗不會被抹平
                        acc(j) += CSng(Math.Max(-10, Math.Min(10, wide - lo(j))) * wgt * amt)
                    Next
                Next
            End Using
        End If

        ' 去紅：比皮膚平均更紅的地方往平均拉；整體偏紅時也稍微收
        If b.Redness > 0 Then
            Dim amt = b.Redness / 100.0
            For i = 0 To n - 1
                Dim wgt = m(i) / 255.0
                If wgt < 0.01 Then Continue For
                Dim a = lab(i * 3 + 1)
                Dim extra = Math.Min(14, Math.Max(0, a - mA) * 0.8 + Math.Max(0, mA - 145) * 0.1) ' 只收多出來的紅，保留血色
                addLab(i, Math.Max(0, a - mA) * 0.15 * amt * wgt, -extra * amt * wgt, 0)
            Next
        End If

        ' 美白：皮膚變亮、稍微降低飽和
        If b.Whiten > 0 Then
            Dim amt = b.Whiten / 100.0
            For i = 0 To n - 1
                Dim wgt = m(i) / 255.0
                If wgt < 0.01 Then Continue For
                Dim L = lab(i * 3)
                addLab(i, (255 - L) * 0.25 * amt * wgt, -(lab(i * 3 + 1) - 128) * 0.04 * amt * wgt, -(lab(i * 3 + 2) - 128) * 0.08 * amt * wgt) ' 幾乎不降飽和，避免死白
            Next
        End If

        ' 膚色：負＝白皙偏冷（粉嫩）、正＝健康小麥色
        If b.Tone <> 0 Then
            Dim amt = b.Tone / 100.0
            For i = 0 To n - 1
                Dim wgt = m(i) / 255.0
                If wgt < 0.01 Then Continue For
                Dim L = lab(i * 3)
                If amt > 0 Then
                    addLab(i, -L * 0.07 * amt * wgt, 3 * amt * wgt, 8 * amt * wgt)
                Else
                    addLab(i, (255 - L) * 0.08 * -amt * wgt, 2 * -amt * wgt, -7 * -amt * wgt)
                End If
            Next
        End If

        ' 去油光：只壓「比周圍亮很多」的小反光點（照到光的整片額頭不算），顏色往周圍拉
        If b.Shine > 0 Then
            Dim amt = b.Shine / 100.0
            Dim lArr(n - 1) As Single
            For i = 0 To n - 1
                lArr(i) = lab(i * 3)
            Next
            Dim localL = BlurArray(lArr, w, h, eyeDist * 0.35)
            Dim shine(n - 1) As Single
            For i = 0 To n - 1
                If m(i) < 3 Then Continue For
                Dim L = lab(i * 3)
                Dim chroma = Math.Sqrt((lab(i * 3 + 1) - 128) ^ 2 + (lab(i * 3 + 2) - 128) ^ 2)
                Dim over = (L - localL(i) - 6) / 18.0
                If over <= 0 OrElse L < mL Then Continue For
                shine(i) = CSng(Math.Min(1, over) * Math.Max(0.3, 1 - chroma / 40))
            Next
            Dim sm = BlurArray(shine, w, h, 1.5)
            For i = 0 To n - 1
                Dim s = sm(i) * amt * (m(i) / 255.0)
                If s < 0.002 Then Continue For
                Dim L = lab(i * 3)
                addLab(i, -Math.Max(0, L - localL(i) - 3) * 0.85 * s, (mA - lab(i * 3 + 1)) * 0.5 * s, (mB - lab(i * 3 + 2)) * 0.5 * s)
            Next
        End If

        ' 黑眼圈：兩眼下方的橢圓，亮度補到接近臉頰、去青紫
        If b.DarkCircles > 0 Then
            Dim amt = b.DarkCircles / 100.0
            Dim region(n - 1) As Single
            For e = 0 To 1
                Dim cx = lm(e).X, cy = lm(e).Y + eyeDist * 0.3
                Dim rx = eyeDist * 0.3, ry = eyeDist * 0.13
                FillSoftEllipse(region, w, h, cx, cy, rx, ry)
            Next
            ' 眼睛本身不動
            Dim eyeHole(n - 1) As Single
            For e = 0 To 1
                FillSoftEllipse(eyeHole, w, h, lm(e).X, lm(e).Y, eyeR * 1.05, eyeR * 0.7)
            Next
            region = BlurArray(region, w, h, eyeDist * 0.06)
            For i = 0 To n - 1
                Dim r = region(i) * (1 - Math.Min(1, eyeHole(i) * 1.5)) * amt
                If r < 0.002 Then Continue For
                Dim L = lab(i * 3)
                addLab(i, Math.Max(0, mL - L) * 0.85 * r, (mA - lab(i * 3 + 1)) * 0.5 * r, Math.Max(0, mB - lab(i * 3 + 2)) * 0.7 * r)
            Next
        End If

        ' 牙齒美白：兩嘴角之間偏亮、不紅的像素去黃提亮
        If b.Teeth > 0 Then
            Dim amt = b.Teeth / 100.0
            Dim region(n - 1) As Single
            If dense IsNot Nothing Then
                region = PolyMask(w, h, dense.Skip(60).Take(8).ToArray(), Nothing, eyeDist * 0.015) ' 68 點：嘴唇內側＝露出的牙齒
            Else
                FillSoftEllipse(region, w, h, mouth.X, mouth.Y + eyeDist * 0.02, mouthHalf * 0.85, Math.Max(3, eyeDist * 0.16))
            End If
            ' 嘴裡的亮度參考：區域內的中位數附近
            For i = 0 To n - 1
                If region(i) < 0.01 Then Continue For
                Dim L = lab(i * 3), a = lab(i * 3 + 1)
                Dim bright = Smooth(L, mL * 0.75, mL * 0.95)
                Dim notRed = 1 - Smooth(a, 140, 152)
                Dim t = region(i) * bright * notRed * amt
                If t < 0.002 Then Continue For
                addLab(i, (255 - L) * 0.22 * t, -Math.Max(0, a - 128) * 0.4 * t, -Math.Max(0, lab(i * 3 + 2) - 128) * 0.8 * t)
            Next
        End If

        ' 唇色（68 點）：嘴唇外框扣掉內側（牙齒、嘴裡不上色），色相換成唇色、保留原本的明暗紋路
        If dense IsNot Nothing AndAlso b.Lips > 0 Then
            Dim amt = b.Lips / 100.0
            Dim region = PolyMask(w, h, dense.Skip(48).Take(12).ToArray(), dense.Skip(60).Take(8).ToArray(), eyeDist * 0.02)
            Dim t = ColorToLab(b.LipColor)
            For i = 0 To n - 1
                Dim r = region(i) * amt
                If r < 0.002 Then Continue For
                Dim L = lab(i * 3)
                addLab(i, (t.L - L) * 0.25 * r, (t.A - lab(i * 3 + 1)) * 0.7 * r, (t.B - lab(i * 3 + 2)) * 0.7 * r)
            Next
        End If

        ' 眉毛加深（68 點）：沿兩道眉毛的粗線，變暗並稍微偏深棕（保留毛流紋路）
        If dense IsNot Nothing AndAlso b.Brows > 0 Then
            Dim amt = b.Brows / 100.0
            Dim region(n - 1) As Single
            For Each start In {17, 22}
                For j = start To start + 3
                    DrawSoftLine(region, w, h, dense(j).X, dense(j).Y, dense(j + 1).X, dense(j + 1).Y, eyeDist * 0.045)
                Next
            Next
            region = BlurArray(region, w, h, eyeDist * 0.02)
            For i = 0 To n - 1
                Dim r = region(i) * amt
                If r < 0.002 Then Continue For
                Dim L = lab(i * 3)
                addLab(i, -L * 0.3 * r, (134 - lab(i * 3 + 1)) * 0.3 * r, (140 - lab(i * 3 + 2)) * 0.3 * r)
            Next
        End If

        ' 妝容（68 點）：眼影、眼線、臥蠶。眼睛點：36 外眼角、37–38 上緣、39 內眼角、40–41 下緣（另一眼 42 內、43–44 上、45 外、46–47 下）
        If dense IsNot Nothing AndAlso (b.EyeShadow > 0 OrElse b.EyeLiner > 0 OrElse b.EyeBag > 0) Then
            ' 臉的「上」方向：下巴 → 鼻樑頂
            Dim ux As Double = dense(27).X - dense(8).X, uy As Double = dense(27).Y - dense(8).Y
            Dim ul = Math.Max(1.0, Math.Sqrt(ux * ux + uy * uy))
            ux /= ul : uy /= ul
            Dim up = Function(p As Point2f, d As Double) New Point2f(CSng(p.X + ux * d), CSng(p.Y + uy * d))
            Dim eyesPts = {(Upper:={36, 37, 38, 39}, Lower:={39, 40, 41, 36}, Outer:=36, Inner:=39, All:=36),
                           (Upper:={42, 43, 44, 45}, Lower:={45, 46, 47, 42}, Outer:=45, Inner:=42, All:=42)}
            If b.EyeShadow > 0 Then
                Dim amt = b.EyeShadow / 100.0
                Dim t = ColorToLab(b.EyeShadowColor)
                Dim region(n - 1) As Single
                For Each e In eyesPts
                    ' 上眼皮往上一條帶狀（中間最高、外眼角往外延伸一點），扣掉眼睛本身
                    Dim lid = e.Upper.Select(Function(i) dense(i)).ToArray()
                    Dim lift = {0.12, 0.2, 0.2, 0.1}
                    Dim outerDir = If(dense(e.Outer).X < dense(e.Inner).X, -1, 1)
                    Dim top = lid.Select(Function(p, j) up(p, eyeDist * lift(j))).ToArray()
                    If e.Outer = 36 Then top(0) = New Point2f(CSng(top(0).X + outerDir * eyeDist * 0.06), top(0).Y) Else top(3) = New Point2f(CSng(top(3).X + outerDir * eyeDist * 0.06), top(3).Y)
                    Dim poly = lid.Concat(top.Reverse()).ToArray()
                    Dim eyeHole = Enumerable.Range(e.All, 6).Select(Function(i) dense(i)).ToArray()
                    Dim m1 = PolyMask(w, h, poly, eyeHole, eyeDist * 0.05)
                    For i = 0 To n - 1
                        If m1(i) > region(i) Then region(i) = m1(i)
                    Next
                Next
                For i = 0 To n - 1
                    Dim r = region(i) * amt
                    If r < 0.002 Then Continue For
                    addLab(i, (t.L - lab(i * 3)) * 0.3 * r, (t.A - lab(i * 3 + 1)) * 0.6 * r, (t.B - lab(i * 3 + 2)) * 0.6 * r)
                Next
            End If
            If b.EyeLiner > 0 Then
                Dim amt = b.EyeLiner / 100.0
                Dim region(n - 1) As Single
                For Each e In eyesPts
                    Dim lid = e.Upper.Select(Function(i) dense(i)).ToArray()
                    For j = 0 To lid.Length - 2
                        DrawSoftLine(region, w, h, lid(j).X, lid(j).Y, lid(j + 1).X, lid(j + 1).Y, eyeDist * 0.018)
                    Next
                    ' 眼尾微微上揚
                    Dim o = dense(e.Outer)
                    Dim outerDir = If(dense(e.Outer).X < dense(e.Inner).X, -1, 1)
                    Dim tip = up(New Point2f(CSng(o.X + outerDir * eyeDist * 0.09), o.Y), eyeDist * 0.04)
                    DrawSoftLine(region, w, h, o.X, o.Y, tip.X, tip.Y, eyeDist * 0.014)
                Next
                For i = 0 To n - 1
                    Dim r = Math.Min(1, region(i) * 1.3) * amt
                    If r < 0.002 Then Continue For
                    addLab(i, -lab(i * 3) * 0.75 * r, (128 - lab(i * 3 + 1)) * 0.5 * r, (128 - lab(i * 3 + 2)) * 0.5 * r)
                Next
            End If
            If b.EyeBag > 0 Then
                Dim amt = b.EyeBag / 100.0
                Dim hi(n - 1) As Single, lo(n - 1) As Single
                For Each e In eyesPts
                    Dim lid = e.Lower.Select(Function(i) dense(i)).ToArray()
                    For j = 0 To lid.Length - 2
                        Dim a1 = up(lid(j), -eyeDist * 0.07), a2 = up(lid(j + 1), -eyeDist * 0.07)
                        DrawSoftLine(hi, w, h, a1.X, a1.Y, a2.X, a2.Y, eyeDist * 0.04)
                        Dim c1 = up(lid(j), -eyeDist * 0.15), c2 = up(lid(j + 1), -eyeDist * 0.15)
                        DrawSoftLine(lo, w, h, c1.X, c1.Y, c2.X, c2.Y, eyeDist * 0.025)
                    Next
                Next
                For i = 0 To n - 1
                    If hi(i) < 0.002 AndAlso lo(i) < 0.002 Then Continue For
                    Dim L = lab(i * 3)
                    addLab(i, ((255 - L) * 0.16 * hi(i) - L * 0.08 * lo(i)) * amt, 0, 0)
                Next
            End If
        End If

        If Not IsZero(dLab) Then AddLabDelta(lab, dLab, acc, n)

        ' 光影：以整張臉（含頭髮、不限皮膚）為範圍加亮加暗
        If b.HasLight Then
            Dim amt = b.Light / 100.0
            Dim face(n - 1) As Single
            FillSoftEllipse(face, w, h, faceCenter.X, faceCenter.Y, faceW * 0.78, faceH * 0.88)
            face = BlurArray(face, w, h, faceW * 0.1)
            Dim dir = If(b.LightFromRight, 1, -1)
            Dim blurred As Byte() = Nothing
            If b.LightKind = BeautyLight.Soft Then
                Using bl As New Mat()
                    Cv2.GaussianBlur(small, bl, New OpenCvSharp.Size(0, 0), 4)
                    blurred = GetBytes(bl)
                End Using
            End If
            ' 林布蘭光：暗側那隻眼睛下方的三角光
            Dim tri(n - 1) As Single
            If b.LightKind = BeautyLight.Rembrandt Then
                Dim darkEye = If((lm(0).X - lm(1).X) * dir < 0, lm(0), lm(1))
                FillGaussian(tri, w, h, darkEye.X, darkEye.Y + eyeDist * 0.55, eyeDist * 0.17)
            End If
            For i = 0 To n - 1
                Dim fm = face(i)
                If fm < 0.003 Then Continue For
                Dim x = i Mod w
                Dim side = Math.Max(-1, Math.Min(1, (x - faceCenter.X) / (faceW * 0.6))) * dir ' 1＝朝光、-1＝背光
                For ch = 0 To 2
                    Dim j = i * 3 + ch
                    Dim cur = src(j) + acc(j)
                    Dim target As Double
                    Select Case b.LightKind
                        Case BeautyLight.Soft
                            target = cur + (blurred(j) - cur) * 0.3 + (255 - cur) * 0.14
                        Case BeautyLight.Rembrandt
                            Dim gain = 1 + 0.12 * Math.Max(0, side) - 0.45 * Math.Max(0, -side)
                            gain += (1.05 - gain) * tri(i)
                            target = cur * gain
                        Case Else
                            Dim gain = 1 + 0.2 * Math.Max(0, side) - 0.6 * Math.Max(0, -side)
                            target = cur * gain + If(ch = 2, 5, If(ch = 0, -3, 0)) * Math.Max(0, side) ' 亮側稍暖
                    End Select
                    acc(j) += CSng((target - cur) * fm * amt)
                Next
            Next
        End If

        ' 腮紅：兩眼下方偏外側的臉頰，柔和地混入腮紅色（只在皮膚上）
        If b.Blush > 0 Then
            Dim amt = b.Blush / 100.0
            Dim region(n - 1) As Single
            Dim midX = (lm(0).X + lm(1).X) / 2
            For e = 0 To 1
                Dim outward = Math.Sign(lm(e).X - midX)
                FillGaussian(region, w, h, lm(e).X + outward * eyeDist * 0.12, lm(e).Y + eyeDist * 0.62, eyeDist * 0.34)
            Next
            Dim c = b.BlushColor
            For i = 0 To n - 1
                Dim r = region(i) * (m(i) / 255.0) * amt * 0.32
                If r < 0.002 Then Continue For
                ' 色彩「乘上」再混：深膚色也自然
                Dim tb = src(i * 3) * (c.B / 255.0), tg = src(i * 3 + 1) * (c.G / 255.0), tr = src(i * 3 + 2) * (c.R / 255.0)
                acc(i * 3) += CSng((tb * 0.5 + c.B * 0.5 - src(i * 3)) * r)
                acc(i * 3 + 1) += CSng((tg * 0.5 + c.G * 0.5 - src(i * 3 + 1)) * r)
                acc(i * 3 + 2) += CSng((tr * 0.5 + c.R * 0.5 - src(i * 3 + 2)) * r)
            Next
        End If

        ' 立體修容：鼻樑打亮、顴骨下方與下顎兩側加陰影（只在皮膚上）
        If b.Contour > 0 Then
            Dim amt = b.Contour / 100.0
            Dim hi(n - 1) As Single, lo(n - 1) As Single
            Dim between As New Point2f((lm(0).X + lm(1).X) / 2, (lm(0).Y + lm(1).Y) / 2)
            DrawSoftLine(hi, w, h, between.X, between.Y + eyeDist * 0.05, lm(2).X, lm(2).Y - eyeDist * 0.12, eyeDist * 0.07)
            FillGaussian(hi, w, h, between.X, between.Y - eyeDist * 0.55, eyeDist * 0.22) ' 額頭中央
            Dim midX = (lm(0).X + lm(1).X) / 2
            For e = 0 To 1
                Dim outward = Math.Sign(lm(e).X - midX)
                ' 顴骨下方：從耳前往嘴角斜下
                DrawSoftLine(lo, w, h, lm(e).X + outward * eyeDist * 0.62, lm(e).Y + eyeDist * 0.45,
                             lm(3 + e).X + outward * eyeDist * 0.28, lm(3 + e).Y - eyeDist * 0.15, eyeDist * 0.11)
                ' 下顎兩側
                DrawSoftLine(lo, w, h, lm(e).X + outward * eyeDist * 0.7, lm(e).Y + eyeDist * 0.85,
                             lm(3 + e).X + outward * eyeDist * 0.25, lm(3 + e).Y + eyeDist * 0.55, eyeDist * 0.1)
            Next
            hi = BlurArray(hi, w, h, eyeDist * 0.08)
            lo = BlurArray(lo, w, h, eyeDist * 0.12)
            For i = 0 To n - 1
                Dim sk = m(i) / 255.0
                If sk < 0.01 Then Continue For
                For ch = 0 To 2
                    Dim v = src(i * 3 + ch) + acc(i * 3 + ch)
                    acc(i * 3 + ch) += CSng(((255 - v) * 0.2 * hi(i) - v * 0.14 * lo(i) * If(ch = 0, 1.1, 1.0)) * amt * sk)
                Next
            Next
        End If


        Dim result As New Mat(h, w, MatType.CV_32FC3)
        Marshal.Copy(acc, 0, result.Data, acc.Length)
        Return result
    End Function

    Private Shared Sub RemoveBlemishes(small As Mat, lab As Byte(), m As Byte(), acc As Single(), w As Integer, h As Integer,
                                       amt As Double, lm As Point2f(), eyeDist As Double)
        Dim n = w * h
        Dim k = Math.Max(5, CInt(eyeDist * 0.22)) Or 1 ' 中位數視窗（奇數），比痘痘大
        Dim L(n - 1) As Byte, A(n - 1) As Byte
        For i = 0 To n - 1
            L(i) = lab(i * 3) : A(i) = lab(i * 3 + 1)
        Next
        Using lMat As New Mat(h, w, MatType.CV_8UC1), aMat As New Mat(h, w, MatType.CV_8UC1), lMed As New Mat(), aMed As New Mat(), colorMed As New Mat()
            Marshal.Copy(L, 0, lMat.Data, n)
            Marshal.Copy(A, 0, aMat.Data, n)
            Cv2.MedianBlur(lMat, lMed, k)
            Cv2.MedianBlur(aMat, aMed, k)
            Cv2.MedianBlur(small, colorMed, k)
            Dim lm2 = GetBytes(lMed), am2 = GetBytes(aMed), cm = GetBytes(colorMed), src = GetBytes(small)
            ' 靈敏度：數值越高門檻越低
            Dim thr = 16 - 10 * amt
            Dim spot(n - 1) As Single
            Dim nose = lm(2)
            For i = 0 To n - 1
                If m(i) < 128 Then Continue For
                Dim x = i Mod w, y = i \ w
                ' 鼻孔附近不算（本來就暗）
                If Math.Abs(x - nose.X) < eyeDist * 0.32 AndAlso y > nose.Y - eyeDist * 0.05 AndAlso y < nose.Y + eyeDist * 0.25 Then Continue For
                Dim score = (CInt(lm2(i)) - L(i)) * 1.0 + Math.Max(0, CInt(A(i)) - am2(i)) * 1.6
                If score > thr Then spot(i) = CSng(Math.Min(1, (score - thr) / 6 + 0.5))
            Next
            ' 太大的區塊（眉毛、陰影）不是痘痘：用連通區域面積過濾
            Using sm As New Mat(h, w, MatType.CV_8UC1), labels As New Mat(), stats As New Mat(), cents As New Mat()
                Dim sb(n - 1) As Byte
                For i = 0 To n - 1
                    sb(i) = If(spot(i) > 0, CByte(255), CByte(0))
                Next
                Marshal.Copy(sb, 0, sm.Data, n)
                Dim count = Cv2.ConnectedComponentsWithStats(sm, labels, stats, cents)
                Dim maxArea = (eyeDist * 0.2) ^ 2
                Dim lab32(n - 1) As Integer
                Marshal.Copy(labels.Data, lab32, 0, n)
                For i = 0 To n - 1
                    If lab32(i) > 0 AndAlso stats.At(Of Integer)(lab32(i), CInt(ConnectedComponentsTypes.Area)) > maxArea Then spot(i) = 0
                Next
            End Using
            ' 稍微長大、羽化
            spot = BlurArray(spot, w, h, 1.2)
            For i = 0 To n - 1
                Dim s = Math.Min(1, spot(i) * 1.8)
                If s < 0.01 Then Continue For
                For ch = 0 To 2
                    Dim j = i * 3 + ch
                    Dim cur = src(j) + acc(j)
                    acc(j) += CSng((cm(j) - cur) * s)
                Next
            Next
        End Using
    End Sub

    '=====================================================================
    ' 大眼（變形）
    '=====================================================================

    Private Shared Sub EnlargeEyes(px As Byte(), stride As Integer, w As Integer, h As Integer, f As FaceRegion, amount As Double)
        Dim e0 As New PointF(f.Landmarks(0).X * w, f.Landmarks(0).Y * h), e1 As New PointF(f.Landmarks(1).X * w, f.Landmarks(1).Y * h)
        Dim d = Math.Sqrt((e1.X - e0.X) ^ 2 + (e1.Y - e0.Y) ^ 2)
        If d < 6 Then Return
        Dim r = d * 0.42
        Dim strength = amount * 0.32
        For Each c In {e0, e1}
            Dim x0 = Math.Max(0, CInt(c.X - r)), x1 = Math.Min(w - 1, CInt(c.X + r))
            Dim y0 = Math.Max(0, CInt(c.Y - r)), y1 = Math.Min(h - 1, CInt(c.Y + r))
            If x1 <= x0 OrElse y1 <= y0 Then Continue For
            Dim sw = x1 - x0 + 1, sh = y1 - y0 + 1
            Dim src(sw * sh * 4 - 1) As Byte
            For y = 0 To sh - 1
                Buffer.BlockCopy(px, (y0 + y) * stride + x0 * 4, src, y * sw * 4, sw * 4)
            Next
            For y = y0 To y1
                For x = x0 To x1
                    Dim dx = x - c.X, dy = y - c.Y
                    Dim t = (dx * dx + dy * dy) / (r * r)
                    If t >= 1 Then Continue For
                    ' 中心放大、往外平滑過渡到不變
                    Dim s = 1 - strength * (1 - t) * (1 - t)
                    Dim sx = c.X + dx * s - x0, sy = c.Y + dy * s - y0
                    SampleInto(src, sw, sh, sx, sy, px, y * stride + x * 4)
                Next
            Next
        Next
    End Sub

    '=====================================================================
    ' 臉型（68 點）：在控制點放位移，用高斯權重內插成位移場，再反向取樣
    '=====================================================================

    Private Shared Sub WarpFaceShape(px As Byte(), stride As Integer, w As Integer, h As Integer, dense As PointF(), b As BeautySettings)
        Dim p = dense.Select(Function(q) New PointF(q.X * w, q.Y * h)).ToArray()
        Dim faceW = Math.Sqrt((p(16).X - p(0).X) ^ 2 + (p(16).Y - p(0).Y) ^ 2)
        If faceW < 12 Then Return
        ' 臉的中線：鼻樑頂（27）往下巴（8），臉歪也跟著歪
        Dim top = p(27), chin = p(8)
        Dim ax As Double = chin.X - top.X, ay As Double = chin.Y - top.Y
        Dim alen = Math.Max(1.0, Math.Sqrt(ax * ax + ay * ay))
        ax /= alen : ay /= alen
        Dim faceH = alen
        ' 點到中線的垂足方向（往內）
        Dim inward = Function(q As PointF) As PointF
                         Dim t = (q.X - top.X) * ax + (q.Y - top.Y) * ay
                         Return New PointF(CSng(top.X + ax * t - q.X), CSng(top.Y + ay * t - q.Y))
                     End Function
        Dim ctrls As New List(Of (C As PointF, D As PointF, R As Double))()
        If b.FaceSlim <> 0 Then
            Dim s = b.FaceSlim / 100.0 * 0.11
            For i = 2 To 14
                If i = 8 Then Continue For
                Dim wgt = If(i <= 3 OrElse i >= 13, 0.5, 1.0)
                Dim d = inward(p(i))
                ctrls.Add((p(i), New PointF(CSng(d.X * s * wgt), CSng(d.Y * s * wgt)), faceW * 0.16))
            Next
        End If
        If b.VFace <> 0 Then
            Dim s = b.VFace / 100.0 * 0.16
            For i = 5 To 11
                If i = 8 Then Continue For
                Dim wgt = If(i = 7 OrElse i = 9, 0.6, 1.0)
                Dim d = inward(p(i))
                ctrls.Add((p(i), New PointF(CSng(d.X * s * wgt), CSng(d.Y * s * wgt)), faceW * 0.12))
            Next
        End If
        If b.Chin <> 0 Then
            Dim s = b.Chin / 100.0 * faceH * 0.07
            For Each i In {7, 8, 9}
                Dim wgt = If(i = 8, 1.0, 0.6)
                ctrls.Add((p(i), New PointF(CSng(ax * s * wgt), CSng(ay * s * wgt)), faceW * 0.14))
            Next
        End If
        If b.NoseSlim <> 0 Then
            Dim s = b.NoseSlim / 100.0 * 0.3
            Dim noseW = Math.Max(4.0, Math.Sqrt((p(35).X - p(31).X) ^ 2 + (p(35).Y - p(31).Y) ^ 2))
            For Each i In {31, 32, 34, 35}
                Dim d = inward(p(i))
                Dim wgt = If(i = 31 OrElse i = 35, 1.0, 0.5)
                ctrls.Add((p(i), New PointF(CSng(d.X * s * wgt), CSng(d.Y * s * wgt)), noseW * 0.45))
            Next
        End If
        If ctrls.Count > 0 Then WarpByControls(px, stride, w, h, ctrls)
    End Sub

    ''' <summary>
    ''' 位移場 D(p) = Σ dᵢ·exp(−|p−cᵢ|²/2rᵢ²)，輸出 dst(p) = src(p − D(p))：控制點附近的內容大約移動 dᵢ，往外平滑變回不動。
    ''' </summary>
    Private Shared Sub WarpByControls(px As Byte(), stride As Integer, w As Integer, h As Integer, ctrls As List(Of (C As PointF, D As PointF, R As Double)))
        Dim x0 = w, y0 = h, x1 = 0, y1 = 0
        Dim maxD = 0.0
        For Each c In ctrls
            Dim reach = c.R * 3
            x0 = Math.Min(x0, CInt(c.C.X - reach)) : y0 = Math.Min(y0, CInt(c.C.Y - reach))
            x1 = Math.Max(x1, CInt(c.C.X + reach)) : y1 = Math.Max(y1, CInt(c.C.Y + reach))
            maxD = Math.Max(maxD, Math.Sqrt(c.D.X * c.D.X + c.D.Y * c.D.Y))
        Next
        x0 = Math.Max(0, x0) : y0 = Math.Max(0, y0) : x1 = Math.Min(w - 1, x1) : y1 = Math.Min(h - 1, y1)
        If x1 <= x0 OrElse y1 <= y0 OrElse maxD < 0.05 Then Return
        Dim rw = x1 - x0 + 1, rh = y1 - y0 + 1
        Dim dx(rw * rh - 1) As Single, dy(rw * rh - 1) As Single
        For Each c In ctrls
            Dim reach = c.R * 3, inv = 1 / (2 * c.R * c.R)
            For y = Math.Max(y0, CInt(c.C.Y - reach)) To Math.Min(y1, CInt(c.C.Y + reach))
                For x = Math.Max(x0, CInt(c.C.X - reach)) To Math.Min(x1, CInt(c.C.X + reach))
                    Dim d2 = (x - c.C.X) ^ 2 + (y - c.C.Y) ^ 2
                    Dim g = Math.Exp(-d2 * inv)
                    If g < 0.003 Then Continue For
                    Dim i = (y - y0) * rw + (x - x0)
                    dx(i) += CSng(c.D.X * g) : dy(i) += CSng(c.D.Y * g)
                Next
            Next
        Next
        ' 來源範圍要多留位移量
        Dim m = CInt(Math.Ceiling(maxD * ctrls.Count)) + 2
        Dim sx0 = Math.Max(0, x0 - m), sy0 = Math.Max(0, y0 - m), sx1 = Math.Min(w - 1, x1 + m), sy1 = Math.Min(h - 1, y1 + m)
        Dim sw = sx1 - sx0 + 1, sh = sy1 - sy0 + 1
        Dim src(sw * sh * 4 - 1) As Byte
        For y = 0 To sh - 1
            Buffer.BlockCopy(px, (sy0 + y) * stride + sx0 * 4, src, y * sw * 4, sw * 4)
        Next
        For y = y0 To y1
            For x = x0 To x1
                Dim i = (y - y0) * rw + (x - x0)
                If Math.Abs(dx(i)) < 0.02 AndAlso Math.Abs(dy(i)) < 0.02 Then Continue For
                SampleInto(src, sw, sh, x - dx(i) - sx0, y - dy(i) - sy0, px, y * stride + x * 4)
            Next
        Next
    End Sub

    Private Shared Sub SampleInto(src As Byte(), sw As Integer, sh As Integer, fx As Double, fy As Double, dst As Byte(), di As Integer)
        fx = Math.Max(0, Math.Min(sw - 1.001, fx))
        fy = Math.Max(0, Math.Min(sh - 1.001, fy))
        Dim ix = CInt(Math.Floor(fx)), iy = CInt(Math.Floor(fy))
        Dim ax = fx - ix, ay = fy - iy
        Dim ix1 = Math.Min(sw - 1, ix + 1), iy1 = Math.Min(sh - 1, iy + 1)
        For ch = 0 To 3
            Dim v = (src((iy * sw + ix) * 4 + ch) * (1 - ax) + src((iy * sw + ix1) * 4 + ch) * ax) * (1 - ay) +
                    (src((iy1 * sw + ix) * 4 + ch) * (1 - ax) + src((iy1 * sw + ix1) * 4 + ch) * ax) * ay
            dst(di + ch) = CByte(Math.Max(0, Math.Min(255, Math.Round(v))))
        Next
    End Sub

    '=====================================================================
    ' 小工具
    '=====================================================================

    Private Shared Function ToPoint(p As Point2f) As OpenCvSharp.Point
        Return New OpenCvSharp.Point(CInt(p.X), CInt(p.Y))
    End Function

    Private Shared Function Smooth(v As Double, e0 As Double, e1 As Double) As Double
        Dim t = Math.Max(0, Math.Min(1, (v - e0) / Math.Max(0.0001, e1 - e0)))
        Return t * t * (3 - 2 * t)
    End Function

    Private Shared Function ToLab(bgr As Mat) As Byte()
        Using lab As New Mat()
            Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab)
            Return GetBytes(lab)
        End Using
    End Function

    ''' <summary>Lab 修正量換成 BGR 修正量加進 acc。</summary>
    Private Shared Sub AddLabDelta(lab As Byte(), dLab As Single(), acc As Single(), n As Integer)
        Dim before(n * 3 - 1) As Byte, after(n * 3 - 1) As Byte
        For i = 0 To n * 3 - 1
            after(i) = ImagePipeline.ClampByte(lab(i) + dLab(i))
        Next
        Using a As New Mat(1, n, MatType.CV_8UC3), o As New Mat(1, n, MatType.CV_8UC3), ab As New Mat(), ob As New Mat()
            Marshal.Copy(after, 0, a.Data, after.Length)
            Marshal.Copy(lab, 0, o.Data, lab.Length)
            Cv2.CvtColor(a, ab, ColorConversionCodes.Lab2BGR)
            Cv2.CvtColor(o, ob, ColorConversionCodes.Lab2BGR)
            Dim x = GetBytes(ab), y = GetBytes(ob)
            For i = 0 To n * 3 - 1
                If dLab(i - i Mod 3) <> 0 OrElse dLab(i - i Mod 3 + 1) <> 0 OrElse dLab(i - i Mod 3 + 2) <> 0 Then acc(i) += CSng(x(i)) - y(i)
            Next
        End Using
    End Sub

    ''' <summary>多邊形遮罩（0..1）：outer 填滿、hole 挖掉，邊緣羽化 feather。</summary>
    Private Shared Function PolyMask(w As Integer, h As Integer, outer As Point2f(), hole As Point2f(), feather As Double) As Single()
        Using m As New Mat(h, w, MatType.CV_8UC1, Scalar.All(0))
            Cv2.FillPoly(m, {outer.Select(Function(p) ToPoint(p)).ToArray()}, Scalar.All(255), LineTypes.AntiAlias)
            If hole IsNot Nothing Then Cv2.FillPoly(m, {hole.Select(Function(p) ToPoint(p)).ToArray()}, Scalar.All(0), LineTypes.AntiAlias)
            Using f As New Mat()
                m.ConvertTo(f, MatType.CV_32FC1, 1 / 255.0)
                If feather >= 0.3 Then Cv2.GaussianBlur(f, f, New OpenCvSharp.Size(0, 0), feather)
                Return GetFloats(f)
            End Using
        End Using
    End Function

    ''' <summary>顏色換成 OpenCV 8 位元 Lab（a、b 以 128 為中心）。</summary>
    Private Shared Function ColorToLab(c As Color) As (L As Double, A As Double, B As Double)
        Using m As New Mat(1, 1, MatType.CV_8UC3, New Scalar(c.B, c.G, c.R)), o As New Mat()
            Cv2.CvtColor(m, o, ColorConversionCodes.BGR2Lab)
            Dim v = o.At(Of Vec3b)(0, 0)
            Return (v.Item0, v.Item1, v.Item2)
        End Using
    End Function

    Private Shared Function IsZero(a As Single()) As Boolean
        For Each v In a
            If v <> 0 Then Return False
        Next
        Return True
    End Function

    ''' <summary>柔邊橢圓（中心 1、邊緣 0），取最大值疊上。</summary>
    Private Shared Sub FillSoftEllipse(a As Single(), w As Integer, h As Integer, cx As Double, cy As Double, rx As Double, ry As Double)
        If rx < 1 OrElse ry < 1 Then Return
        For y = Math.Max(0, CInt(cy - ry)) To Math.Min(h - 1, CInt(cy + ry))
            For x = Math.Max(0, CInt(cx - rx)) To Math.Min(w - 1, CInt(cx + rx))
                Dim t = ((x - cx) / rx) ^ 2 + ((y - cy) / ry) ^ 2
                If t >= 1 Then Continue For
                Dim v = CSng(Smooth(1 - t, 0, 0.6))
                Dim i = y * w + x
                If v > a(i) Then a(i) = v
            Next
        Next
    End Sub

    Private Shared Sub FillGaussian(a As Single(), w As Integer, h As Integer, cx As Double, cy As Double, sigma As Double)
        If sigma < 0.5 Then Return
        Dim r = sigma * 3
        For y = Math.Max(0, CInt(cy - r)) To Math.Min(h - 1, CInt(cy + r))
            For x = Math.Max(0, CInt(cx - r)) To Math.Min(w - 1, CInt(cx + r))
                Dim v = CSng(Math.Exp(-(((x - cx) ^ 2 + (y - cy) ^ 2) / (2 * sigma * sigma))))
                Dim i = y * w + x
                If v > a(i) Then a(i) = v
            Next
        Next
    End Sub

    ''' <summary>粗細 thick 的柔邊線段（中線 1）。</summary>
    Private Shared Sub DrawSoftLine(a As Single(), w As Integer, h As Integer, x0 As Double, y0 As Double, x1 As Double, y1 As Double, thick As Double)
        Dim dx = x1 - x0, dy = y1 - y0
        Dim len2 = Math.Max(0.0001, dx * dx + dy * dy)
        Dim pad = thick * 2
        For y = Math.Max(0, CInt(Math.Min(y0, y1) - pad)) To Math.Min(h - 1, CInt(Math.Max(y0, y1) + pad))
            For x = Math.Max(0, CInt(Math.Min(x0, x1) - pad)) To Math.Min(w - 1, CInt(Math.Max(x0, x1) + pad))
                Dim t = Math.Max(0, Math.Min(1, ((x - x0) * dx + (y - y0) * dy) / len2))
                Dim px = x0 + dx * t, py = y0 + dy * t
                Dim d = Math.Sqrt((x - px) ^ 2 + (y - py) ^ 2)
                If d > thick * 2 Then Continue For
                Dim v = CSng(Math.Exp(-(d * d) / (2 * thick * thick)))
                Dim i = y * w + x
                If v > a(i) Then a(i) = v
            Next
        Next
    End Sub

    Private Shared Function BlurArray(a As Single(), w As Integer, h As Integer, sigma As Double) As Single()
        If sigma < 0.3 Then Return a
        Using m As New Mat(h, w, MatType.CV_32FC1), o As New Mat()
            Marshal.Copy(a, 0, m.Data, a.Length)
            Cv2.GaussianBlur(m, o, New OpenCvSharp.Size(0, 0), sigma)
            Return GetFloats(o)
        End Using
    End Function

    Private Shared Function GetBytes(m As Mat) As Byte()
        Dim n = CInt(m.Total() * m.ElemSize())
        Dim a(n - 1) As Byte
        If m.IsContinuous() Then
            Marshal.Copy(m.Data, a, 0, n)
        Else
            Using c = m.Clone()
                Marshal.Copy(c.Data, a, 0, n)
            End Using
        End If
        Return a
    End Function

    Private Shared Function GetFloats(m As Mat) As Single()
        Dim n = CInt(m.Total() * m.Channels())
        Dim a(n - 1) As Single
        If m.IsContinuous() Then
            Marshal.Copy(m.Data, a, 0, n)
        Else
            Using c = m.Clone()
                Marshal.Copy(c.Data, a, 0, n)
            End Using
        End If
        Return a
    End Function
End Class
