Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>
''' 人像修飾：磨皮、臉部提亮、亮眼，只作用在偵測到的臉附近。
''' 每張臉先縮放到固定大小（臉寬 WorkFaceSize）再計算，所以預覽（1600 縮圖）與全尺寸匯出的效果一致。
''' </summary>
Public NotInheritable Class PortraitRetouch
    Private Sub New()
    End Sub

    Private Const WorkFaceSize As Double = 256

    ''' <summary>回傳修飾後的新圖；沒有臉或沒有人像參數時回傳 Nothing（表示不變）。</summary>
    Public Shared Function Apply(source As Bitmap, faces As IReadOnlyList(Of FaceRegion), recipe As EditRecipe) As Bitmap
        If faces Is Nothing OrElse faces.Count = 0 OrElse Not recipe.HasPortrait Then Return Nothing
        Dim smooth = recipe.SkinSmoothing / 100.0
        Dim bright = recipe.FaceBrighten / 100.0
        Dim eyes = recipe.EyeBrighten / 100.0

        Dim result = source.Clone(New Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb)
        Using bgra = BitmapConverter.ToMat(source), bgr As New Mat()
            Cv2.CvtColor(bgra, bgr, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2BGR, ColorConversionCodes.RGB2BGR))
            Dim data = result.LockBits(New Rectangle(0, 0, result.Width, result.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
            Try
                Dim px(data.Stride * result.Height - 1) As Byte
                Marshal.Copy(data.Scan0, px, 0, px.Length)
                For Each f In faces
                    RetouchFace(bgr, px, data.Stride, f, smooth, bright, eyes)
                Next
                Marshal.Copy(px, 0, data.Scan0, px.Length)
            Finally
                result.UnlockBits(data)
            End Try
        End Using
        Return result
    End Function

    Private Shared Sub RetouchFace(bgr As Mat, px As Byte(), stride As Integer, f As FaceRegion,
                                   smooth As Double, bright As Double, eyes As Double)
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
        ' 把原圖座標換到縮放後的 ROI 座標。
        Dim toSmall = Function(x As Double, y As Double) New OpenCvSharp.Point(CInt((x - roi.X) * k), CInt((y - roi.Y) * k))
        Dim eyeR = CInt(fw * 0.11 * k)
        Dim lm = f.Landmarks.Select(Function(p) (X:=p.X * imgW, Y:=p.Y * imgH)).ToArray()

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
            Cv2.Ellipse(shape, New RotatedRect(New Point2f(center.X, center.Y), New Size2f(fw * 1.1 * k, fh * 1.35 * k), 0),
                        Scalar.All(255), -1)
            For n = 0 To 1
                Cv2.Circle(shape, toSmall(lm(n).X, lm(n).Y), eyeR, Scalar.All(0), -1)
                Cv2.Circle(eyeMask, toSmall(lm(n).X, lm(n).Y), CInt(eyeR * 0.85), Scalar.All(255), -1)
            Next
            Dim mouth = toSmall((lm(3).X + lm(4).X) / 2, (lm(3).Y + lm(4).Y) / 2)
            Dim mouthHalf = Math.Max(4.0, Math.Abs(lm(4).X - lm(3).X) * k * 0.65)
            Cv2.Ellipse(shape, New RotatedRect(New Point2f(mouth.X, mouth.Y), New Size2f(CSng(mouthHalf * 2), CSng(eyeR * 1.6)), 0),
                        Scalar.All(0), -1)
            Cv2.BitwiseAnd(skin, shape, mask)
            Cv2.GaussianBlur(mask, mask, New OpenCvSharp.Size(0, 0), Math.Max(1.0, WorkFaceSize * 0.035))
            Cv2.GaussianBlur(eyeMask, eyeMask, New OpenCvSharp.Size(0, 0), Math.Max(1.0, eyeR * 0.4))

            Cv2.Resize(smoothSmall, smoothBig, roiSize, 0, 0, InterpolationFlags.Linear)
            Cv2.Resize(mask, maskBig, roiSize, 0, 0, InterpolationFlags.Linear)
            Cv2.Resize(eyeMask, eyeBig, roiSize, 0, 0, InterpolationFlags.Linear)
            Cv2.GaussianBlur(roiMat, roiBlur, New OpenCvSharp.Size(0, 0), Math.Max(0.8, fw * 0.008))

            Dim sm = GetBytes(smoothBig), mk = GetBytes(maskBig), ey = GetBytes(eyeBig), bl = GetBytes(roiBlur)
            For y = 0 To roi.Height - 1
                For x = 0 To roi.Width - 1
                    Dim mi = y * roi.Width + x
                    Dim m = mk(mi) / 255.0, e = ey(mi) / 255.0 * eyes
                    If m < 0.004 AndAlso e < 0.004 Then Continue For
                    Dim pi = (roi.Y + y) * stride + (roi.X + x) * 4
                    Dim ci = mi * 3
                    For ch = 0 To 2
                        Dim o As Double = px(pi + ch)
                        ' 最多只混入 55% 的平滑結果，保留膚質紋理，避免塑膠感。
                        Dim v = o + (sm(ci + ch) - o) * m * smooth * 0.55
                        v += (255 - v) * m * bright * 0.2
                        If e > 0 Then
                            v += (o - bl(ci + ch)) * e * 1.2      ' 眼睛細節銳利一點
                            v += (255 - v) * e * 0.12             ' 並稍微提亮
                        End If
                        px(pi + ch) = ImagePipeline.ClampByte(v)
                    Next
                Next
            Next
        End Using
    End Sub

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
End Class
