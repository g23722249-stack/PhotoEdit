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
Partial Public NotInheritable Class PortraitRetouch
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
                ' 曬黑：整張照片的皮膚一起（取曬黑最深的那張臉的膚色當準，只做一次）。
                ' 要最先做：之後的妝（白眼影、白鼻樑、淡唇）以曬黑後的膚色為準，才不會被曬黑壓暗
                Dim tanFace = faces.Select(Function(x) (F:=x, T:=recipe.BeautyFor(x.Box).Tan)).OrderByDescending(Function(x) x.T).FirstOrDefault()
                If tanFace.F IsNot Nothing AndAlso tanFace.T > 0 Then
                    ApplyTan(px, data.Stride, result.Width, result.Height, tanFace.F, faces, tanFace.T / 100.0)
                    Dim w = result.Width, h = result.Height
                    Dim tanned(w * h * 3 - 1) As Byte
                    For y = 0 To h - 1
                        For x = 0 To w - 1
                            Dim i = y * data.Stride + x * 4, j = (y * w + x) * 3
                            tanned(j) = px(i) : tanned(j + 1) = px(i + 1) : tanned(j + 2) = px(i + 2)
                        Next
                    Next
                    Marshal.Copy(tanned, 0, bgr.Data, tanned.Length)
                End If
                For Each f In faces
                    Dim b = ForOpera(recipe.BeautyFor(f.Box))
                    If b.IsEmpty Then Continue For
                    RetouchFace(bgr, px, data.Stride, f, b)
                Next
                ' 原圖解析度直接畫的：戲曲妝、美瞳、睫毛（在變形之前畫，之後跟著眼睛一起變形）
                For Each f In faces
                    Dim b = ForOpera(recipe.BeautyFor(f.Box))
                    If b.HasOpera Then
                        ApplyOpera(px, data.Stride, result.Width, result.Height, f, b)
                        ' 歌仔戲的俊扮有假睫毛（自己沒設睫毛時）
                        Dim role = OperaRoles.Get(b.OperaRole)
                        If role IsNot Nothing AndAlso role.Lashes AndAlso b.Lash = 0 Then
                            ApplyLashes(px, data.Stride, result.Width, result.Height, f,
                                        New BeautySettings With {.Lash = CInt(80 * b.Opera / 100.0), .LashStyle = LashStyle.Natural, .LashLength = 115, .LashCurl = 55})
                        End If
                    End If
                    If b.Iris > 0 Then ApplyIris(px, data.Stride, result.Width, result.Height, f, b)
                    If b.Lash > 0 Then ApplyLashes(px, data.Stride, result.Width, result.Height, f, b)
                    If b.Hair > 0 Then ApplyHair(px, data.Stride, result.Width, result.Height, source, f, b)
                Next
                ' 變形最後做（會移動像素，要在顏色修飾之後）：大眼、臉型、嘴角、豐唇、開眼角、眉形
                For Each f In faces
                    Dim b = ForOpera(recipe.BeautyFor(f.Box))
                    If b.EyeEnlarge > 0 Then EnlargeEyes(px, data.Stride, result.Width, result.Height, f, b.EyeEnlarge / 100.0)
                    If f.Dense IsNot Nothing AndAlso NeedsShapeWarp(b) Then
                        WarpFaceShape(px, data.Stride, result.Width, result.Height, f.Dense, f.Mesh, b)
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
        Dim meshSmall = f.Mesh?.Select(Function(p) toSmall(p.X * imgW, p.Y * imgH)).ToArray()
        Dim meshZSmall = f.MeshZ?.Select(Function(z) CSng(z * imgW * k)).ToArray() ' 深度換成縮小後的像素
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
                  delta = ColorDelta(small, soft, lm, eyeDist, eyeR, mouth, mouthHalf, b, denseSmall, center, fw * k, fh * k, meshSmall, meshZSmall), deltaBig As New Mat()
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
                                       faceCenter As Point2f, faceW As Double, faceH As Double, mesh As Point2f(), meshZ As Single()) As Mat
        If Not NeedsColorDelta(b, dense IsNot Nothing) Then Return Nothing
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

        ' 美妝共用的工作資料（和上面同一份陣列）
        Dim ctx As New MakeupCtx With {.W = w, .H = h, .N = n, .Src = src, .Lab = lab, .Skin = m, .Acc = acc, .DLab = dLab, .EyeDist = eyeDist,
                                       .Lm = lm, .Dense = dense, .Mesh = mesh, .ML = mL, .MA = mA, .MB = mB}
        If dense IsNot Nothing Then
            Dim ux As Double = dense(27).X - dense(8).X, uy As Double = dense(27).Y - dense(8).Y
            Dim ul = Math.Max(1.0, Math.Sqrt(ux * ux + uy * uy))
            ctx.UpX = ux / ul : ctx.UpY = uy / ul
        Else
            ctx.UpX = 0 : ctx.UpY = -1
        End If

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
                region = PolyMask(w, h, LipInner(dense, mesh), Nothing, eyeDist * 0.015) ' 嘴唇內側＝露出的牙齒
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

        ' 美妝（需要特徵點）：口紅、眉毛、眼影、雙眼皮、眼線、臥蠶、高光
        ApplyMakeupLab(ctx, b)

        If Not IsZero(dLab) Then AddLabDelta(lab, dLab, acc, n)

        ' 光影：以整張臉（含頭髮、不限皮膚）為範圍加亮加暗
        If b.HasLight Then
            Dim amt = b.Light / 100.0
            Dim face(n - 1) As Single
            FillSoftEllipse(face, w, h, faceCenter.X, faceCenter.Y, faceW * 0.72, faceH * 0.82)
            face = BlurArray(face, w, h, faceW * 0.1)
            ' 計算範圍（臉框的 1.7 倍）邊緣淡出，不要被截斷成一條直線
            For i = 0 To n - 1
                Dim ex = i Mod w, ey = i \ w
                face(i) *= CSng(Smooth(Math.Min(Math.Min(ex, w - 1 - ex), Math.Min(ey, h - 1 - ey)), 0, w * 0.1))
            Next
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
            ' 立體光影：有網格深度時，依臉的起伏打光（網格範圍內），範圍外（頭髮）用左右漸層
            Dim shade3d As (Gain As Single(), Cover As Single()) = Nothing
            If mesh IsNot Nothing AndAlso meshZ IsNot Nothing AndAlso b.LightKind <> BeautyLight.Soft Then
                shade3d = MeshShade(mesh, meshZ, w, h, eyeDist, b.LightKind, b.LightFromRight)
            End If
            For i = 0 To n - 1
                Dim fm = face(i)
                If fm < 0.003 Then Continue For
                Dim x = i Mod w
                Dim side = Math.Max(-1, Math.Min(1, (x - faceCenter.X) / (faceW * 0.6))) * dir ' 1＝朝光、-1＝背光
                Dim cover = If(shade3d.Cover IsNot Nothing, shade3d.Cover(i), 0.0F)
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
                            If cover > 0 Then gain = gain * (1 - cover) + shade3d.Gain(i) * cover
                            target = cur * gain
                        Case Else
                            Dim gain = 1 + 0.2 * Math.Max(0, side) - 0.6 * Math.Max(0, -side)
                            If cover > 0 Then gain = gain * (1 - cover) + shade3d.Gain(i) * cover
                            target = cur * gain + If(ch = 2, 5, If(ch = 0, -3, 0)) * Math.Max(0, gain - 1) * 5 ' 亮的地方稍暖
                    End Select
                    acc(j) += CSng((target - cur) * fm * amt)
                Next
            Next
        End If

        If b.Blush > 0 Then ApplyBlush(ctx, b) ' 腮紅（樣式、範圍、顏色；只在皮膚上）

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

    Private Shared Sub WarpFaceShape(px As Byte(), stride As Integer, w As Integer, h As Integer, dense As PointF(), meshN As PointF(), b As BeautySettings)
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
        ' 吊眉（戲曲的勒頭）：外眼角、眉尾往太陽穴上方拉
        If b.HasOpera AndAlso b.OperaLift > 0 Then
            Dim s = b.OperaLift / 100.0 * faceW * 0.045
            For Each i In {36, 45, 17, 26}
                Dim inw = inward(p(i))
                Dim il = Math.Max(1.0, Math.Sqrt(inw.X * inw.X + inw.Y * inw.Y))
                Dim dx = -ax * 0.8 - inw.X / il * 0.45, dy = -ay * 0.8 - inw.Y / il * 0.45
                Dim wgt = If(i = 17 OrElse i = 26, 1.0, 0.75)
                ctrls.Add((p(i), New PointF(CSng(dx * s * wgt), CSng(dy * s * wgt)), faceW * 0.13))
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
        ' 「上」方向（中線反方向）
        Dim upX = -ax, upY = -ay
        Dim eyeDist = Math.Sqrt((p(42).X - p(39).X) ^ 2 + (p(42).Y - p(39).Y) ^ 2) + Math.Sqrt((p(39).X - p(36).X) ^ 2 + (p(39).Y - p(36).Y) ^ 2)
        Dim mouthW = Math.Max(4.0, Math.Sqrt((p(54).X - p(48).X) ^ 2 + (p(54).Y - p(48).Y) ^ 2))
        Dim add = Sub(q As PointF, dx As Double, dy As Double, r As Double)
                      ctrls.Add((q, New PointF(CSng(dx), CSng(dy)), r))
                  End Sub
        ' 嘴角上揚：兩個嘴角往上、稍微往外
        If b.Smile <> 0 Then
            Dim s = b.Smile / 100.0 * mouthW * 0.07
            For Each i In {48, 54}
                Dim d = inward(p(i))
                Dim dl = Math.Max(1.0, Math.Sqrt(d.X * d.X + d.Y * d.Y))
                add(p(i), upX * s - d.X / dl * s * 0.3, upY * s - d.Y / dl * s * 0.3, mouthW * 0.22)
            Next
        End If
        ' 豐唇：上唇往上、下唇往下（負值往內收）
        If b.LipFull <> 0 Then
            ' 只推嘴唇外緣：以上下唇各自的厚度為準（張嘴時嘴裡的空隙不算），半徑小，內緣與牙齒幾乎不動
            Dim dist = Function(i As Integer, j As Integer) Math.Sqrt((p(i).X - p(j).X) ^ 2 + (p(i).Y - p(j).Y) ^ 2)
            Dim upperT = Math.Max(2.0, dist(51, 62)), lowerT = Math.Max(2.0, dist(57, 66))
            Dim f = b.LipFull / 100.0
            For Each i In {50, 51, 52}
                add(p(i), upX * upperT * 0.35 * f, upY * upperT * 0.35 * f, upperT * 0.55)
            Next
            For Each i In {56, 57, 58}
                add(p(i), -upX * lowerT * 0.35 * f, -upY * lowerT * 0.35 * f, lowerT * 0.55)
            Next
        End If
        ' 開眼角：內眼角往鼻樑、外眼角往外
        If b.EyeCorner <> 0 Then
            Dim s = b.EyeCorner / 100.0 * eyeDist * 0.05
            For Each pair In {(39, 1.0), (42, 1.0), (36, -0.6), (45, -0.6)}
                Dim d = inward(p(pair.Item1))
                Dim dl = Math.Max(1.0, Math.Sqrt(d.X * d.X + d.Y * d.Y))
                add(p(pair.Item1), d.X / dl * s * pair.Item2, d.Y / dl * s * pair.Item2, eyeDist * 0.16)
            Next
        End If
        ' 眉形（眉毛有開時）：挑眉＝眉峰拉高、平眉＝眉峰壓平、柳葉＝眉尾下彎、自然＝眉峰稍微提；粗細＝上緣往上、下緣往下
        If b.Brows > 0 Then
            Dim k = b.Brows / 100.0
            Dim peak = (0.4 + b.BrowPeak / 100.0) * eyeDist * 0.06 * k
            For Each brow In {(Outer:=17, Peak1:=18, Peak2:=19, Inner:=21), (Outer:=26, Peak1:=25, Peak2:=24, Inner:=22)}
                If b.BrowStyle = BrowStyle.Darken Then Exit For ' 原眉加深：形狀不變
                Select Case b.BrowStyle
                    Case BrowStyle.Arch
                        add(p(brow.Peak1), upX * peak, upY * peak, eyeDist * 0.12)
                        add(p(brow.Peak2), upX * peak * 0.7, upY * peak * 0.7, eyeDist * 0.12)
                        add(p(brow.Outer), -upX * peak * 0.4, -upY * peak * 0.4, eyeDist * 0.1)
                    Case BrowStyle.Flat
                        ' 眉峰往眉頭與眉尾的連線靠
                        For Each i In {brow.Peak1, brow.Peak2}
                            Dim a = p(brow.Inner), c = p(brow.Outer)
                            Dim t = ((p(i).X - a.X) * (c.X - a.X) + (p(i).Y - a.Y) * (c.Y - a.Y)) / Math.Max(1.0, (c.X - a.X) ^ 2 + (c.Y - a.Y) ^ 2)
                            Dim fx = a.X + (c.X - a.X) * t, fy = a.Y + (c.Y - a.Y) * t
                            add(p(i), (fx - p(i).X) * 0.8 * k, (fy - p(i).Y) * 0.8 * k, eyeDist * 0.12)
                        Next
                    Case BrowStyle.Willow
                        add(p(brow.Outer), -upX * peak, -upY * peak, eyeDist * 0.1)
                        add(p(brow.Peak1), upX * peak * 0.3, upY * peak * 0.3, eyeDist * 0.1)
                    Case Else ' 自然
                        add(p(brow.Peak2), upX * peak * 0.35, upY * peak * 0.35, eyeDist * 0.12)
                End Select
            Next
            ' 粗細（需要網格的眉毛上下緣）
            If meshN IsNot Nothing AndAlso b.BrowThick <> 50 Then
                Dim t = (b.BrowThick - 50) / 50.0 * eyeDist * 0.03 * k * If(b.BrowStyle = BrowStyle.Willow, 0.6, 1.0)
                Dim mp = Function(i As Integer) New PointF(meshN(i).X * w, meshN(i).Y * h)
                For s = 0 To 1
                    For Each i In MeshBrowUpper(s)
                        add(mp(i), upX * t, upY * t, eyeDist * 0.06)
                    Next
                    For Each i In MeshBrowLower(s)
                        add(mp(i), -upX * t, -upY * t, eyeDist * 0.06)
                    Next
                Next
            End If
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

    '---------------------------------------------------------------------
    ' 五官輪廓：有 478 點網格用網格（較細），否則用 68 點
    '---------------------------------------------------------------------

    Private Shared ReadOnly MeshLipOuter As Integer() = {61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185}
    Private Shared ReadOnly MeshLipInner As Integer() = {78, 95, 88, 178, 87, 14, 317, 402, 318, 324, 308, 415, 310, 311, 312, 13, 82, 81, 80, 191}
    ' 第一隻眼（68 點的 36–41，畫面左邊那隻）：上眼皮外→內、下眼皮內→外；第二隻眼：上眼皮內→外、下眼皮外→內（和 68 點的順序一致）
    Private Shared ReadOnly MeshUpper1 As Integer() = {33, 246, 161, 160, 159, 158, 157, 173, 133}
    Private Shared ReadOnly MeshLower1 As Integer() = {133, 155, 154, 153, 145, 144, 163, 7, 33}
    Private Shared ReadOnly MeshUpper2 As Integer() = {362, 398, 384, 385, 386, 387, 388, 466, 263}
    Private Shared ReadOnly MeshLower2 As Integer() = {263, 249, 390, 373, 374, 380, 381, 382, 362}

    Private Shared Function LipOuter(dense As Point2f(), mesh As Point2f()) As Point2f()
        Return If(mesh IsNot Nothing, MeshLipOuter.Select(Function(i) mesh(i)).ToArray(), dense.Skip(48).Take(12).ToArray())
    End Function

    Private Shared Function LipInner(dense As Point2f(), mesh As Point2f()) As Point2f()
        Return If(mesh IsNot Nothing, MeshLipInner.Select(Function(i) mesh(i)).ToArray(), dense.Skip(60).Take(8).ToArray())
    End Function

    ''' <summary>兩隻眼睛：上眼皮、下眼皮、外眼角、內眼角、眼睛輪廓多邊形；OuterFirst＝上眼皮的第一點是外眼角。</summary>
    Private Shared Function EyeShapes(dense As Point2f(), mesh As Point2f()) As (Upper As Point2f(), Lower As Point2f(), Outer As Point2f, Inner As Point2f, Poly As Point2f(), OuterFirst As Boolean)()
        Dim pick = Function(idx As Integer()) idx.Select(Function(i) mesh(i)).ToArray()
        If mesh IsNot Nothing Then
            Dim u1 = pick(MeshUpper1), l1 = pick(MeshLower1), u2 = pick(MeshUpper2), l2 = pick(MeshLower2)
            Return {(u1, l1, mesh(33), mesh(133), u1.Concat(l1.Skip(1).Take(l1.Length - 2)).ToArray(), True),
                    (u2, l2, mesh(263), mesh(362), u2.Concat(l2.Skip(1).Take(l2.Length - 2)).ToArray(), False)}
        End If
        Dim d = Function(idx As Integer()) idx.Select(Function(i) dense(i)).ToArray()
        Return {(d({36, 37, 38, 39}), d({39, 40, 41, 36}), dense(36), dense(39), d({36, 37, 38, 39, 40, 41}), True),
                (d({42, 43, 44, 45}), d({45, 46, 47, 42}), dense(45), dense(42), d({42, 43, 44, 45, 46, 47}), False)}
    End Function

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
