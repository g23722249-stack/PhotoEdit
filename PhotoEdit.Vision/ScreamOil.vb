Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>
''' 把人物一筆一筆畫成油畫（和《吶喊》背景一樣是長條、順著形狀流動的筆觸）：
''' 方向＝影像的結構張量（順著輪廓走），平坦的地方用緩慢起伏的波浪方向；
''' 粗筆鋪底 → 中筆 → 細筆只補和原圖差很多的地方 → 臉用最細的筆（五官才留得住）；
''' 每一筆的顏色取自原圖、略有深淺變化，碰到顏色差很多的地方就停筆（不會畫過輪廓）；
''' 最後依筆觸的厚度打光。長度都以長邊 1600 為準換算。亂數固定，同一張圖結果一樣。
''' </summary>
Public NotInheritable Class ScreamOilPainter
    Private Sub New()
    End Sub

    ''' <summary>畫成油畫，amount 0..100 和原圖混合；透明度不動。image 直接改。</summary>
    Public Shared Sub Paint(image As Bitmap, amount As Integer, faces As IReadOnlyList(Of FaceRegion))
        If amount <= 0 Then Return
        Dim w = image.Width, h = image.Height
        Dim unit = Math.Max(0.4, Math.Max(w, h) / 1600.0)
        Using bgra = BitmapConverter.ToMat(image), bgr As New Mat()
            If bgra.Channels() = 4 Then Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR) Else bgra.CopyTo(bgr)
            Dim field = Orientation(bgr, w, h, unit)
            Dim rnd As New Random(20261106)
            Using canvas As New Mat(), height As New Mat(h, w, MatType.CV_8UC1, Scalar.All(128))
                Cv2.GaussianBlur(bgr, canvas, New OpenCvSharp.Size(0, 0), 8 * unit)
                ' 粗、中：全面鋪；細：只補差很多的地方
                Layer(canvas, height, bgr, field, w, h, unit, rnd, radius:=6, spacing:=6, length:=46, blur:=5, minError:=0, area:=Nothing)
                Layer(canvas, height, bgr, field, w, h, unit, rnd, radius:=3, spacing:=3.6, length:=30, blur:=2.2, minError:=14, area:=Nothing)
                Layer(canvas, height, bgr, field, w, h, unit, rnd, radius:=1.6, spacing:=2.4, length:=16, blur:=0.9, minError:=26, area:=Nothing)
                If faces IsNot Nothing Then
                    For Each f In faces
                        Dim box = New Rect(CInt((f.Box.X - f.Box.Width * 0.1) * w), CInt((f.Box.Y - f.Box.Height * 0.05) * h), CInt(f.Box.Width * 1.2 * w), CInt(f.Box.Height * 1.35 * h))
                        Layer(canvas, height, bgr, field, w, h, unit, rnd, radius:=1.1, spacing:=1.8, length:=9, blur:=0.5, minError:=10, area:=box)
                    Next
                End If
                Using lit = Impasto(canvas, height, unit)
                    WriteBack(image, lit, amount / 100.0, w, h)
                End Using
            End Using
        End Using
    End Sub

    ''' <summary>每一點的筆觸方向（弧度）。</summary>
    Private Shared Function Orientation(bgr As Mat, w As Integer, h As Integer, unit As Double) As Single()
        Dim result(w * h - 1) As Single
        Using gray As New Mat(), f As New Mat(), gx As New Mat(), gy As New Mat(), jxx As New Mat(), jyy As New Mat(), jxy As New Mat()
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY)
            gray.ConvertTo(f, MatType.CV_32FC1, 1 / 255.0)
            Cv2.GaussianBlur(f, f, New OpenCvSharp.Size(0, 0), 2 * unit)
            Cv2.Sobel(f, gx, MatType.CV_32FC1, 1, 0)
            Cv2.Sobel(f, gy, MatType.CV_32FC1, 0, 1)
            Cv2.Multiply(gx, gx, jxx) : Cv2.Multiply(gy, gy, jyy) : Cv2.Multiply(gx, gy, jxy)
            Dim sigma = 9 * unit
            Cv2.GaussianBlur(jxx, jxx, New OpenCvSharp.Size(0, 0), sigma)
            Cv2.GaussianBlur(jyy, jyy, New OpenCvSharp.Size(0, 0), sigma)
            Cv2.GaussianBlur(jxy, jxy, New OpenCvSharp.Size(0, 0), sigma)
            Dim a(w * h - 1) As Single, b(w * h - 1) As Single, c(w * h - 1) As Single
            Marshal.Copy(jxx.Data, a, 0, a.Length) : Marshal.Copy(jyy.Data, b, 0, b.Length) : Marshal.Copy(jxy.Data, c, 0, c.Length)
            Dim L = Math.Max(w, h) * 1.0
            Threading.Tasks.Parallel.For(0, h, Sub(y)
                                                   For x = 0 To w - 1
                                                       Dim i = y * w + x
                                                       Dim dxy = a(i) - b(i), two = 2 * c(i)
                                                       Dim strength = Math.Sqrt(dxy * dxy + two * two)
                                                       ' 張量的主方向是亮度變化最大的方向，筆觸走垂直它的方向（兩倍角才能混合）
                                                       Dim ex = -dxy, ey = -two
                                                       ' 平坦的地方：緩慢起伏的波浪（大致直向，像《吶喊》流動的線）
                                                       Dim wave = Math.PI / 2 + 0.7 * Math.Sin(x / L * 9 + y / L * 5) + 0.35 * Math.Sin(y / L * 17 - x / L * 6)
                                                       Dim k = Math.Min(1.0, strength / 0.0006)
                                                       Dim vx = ex / Math.Max(1.0E-9, strength) * k + Math.Cos(2 * wave) * (1 - k)
                                                       Dim vy = ey / Math.Max(1.0E-9, strength) * k + Math.Sin(2 * wave) * (1 - k)
                                                       result(i) = CSng(0.5 * Math.Atan2(vy, vx))
                                                   Next
                                               End Sub)
        End Using
        Return result
    End Function

    ''' <summary>畫一層：格點加抖動、打亂順序，每點畫一筆。</summary>
    Private Shared Sub Layer(canvas As Mat, height As Mat, bgr As Mat, field As Single(), w As Integer, h As Integer, unit As Double, rnd As Random,
                             radius As Double, spacing As Double, length As Double, blur As Double, minError As Integer, area As Rect?)
        Dim r = Math.Max(0.6, radius * unit), gap = Math.Max(1.0, spacing * unit), reach = length * unit
        Dim thick = Math.Max(1, CInt(Math.Round(r * 2)))
        Dim stepLen = Math.Max(1.5, 3 * unit)
        Dim px(w * h * 3 - 1) As Byte
        Dim err As Byte() = Nothing
        Using src As New Mat()
            Cv2.GaussianBlur(bgr, src, New OpenCvSharp.Size(0, 0), Math.Max(0.3, blur * unit))
            Marshal.Copy(src.Data, px, 0, px.Length)
            If minError > 0 Then
                Using diff As New Mat(), g As New Mat()
                    Cv2.Absdiff(canvas, src, diff)
                    Cv2.CvtColor(diff, g, ColorConversionCodes.BGR2GRAY)
                    Cv2.GaussianBlur(g, g, New OpenCvSharp.Size(0, 0), Math.Max(0.5, r))
                    err = New Byte(w * h - 1) {}
                    Marshal.Copy(g.Data, err, 0, err.Length)
                End Using
            End If
        End Using
        Dim x0 = 0, y0 = 0, x1 = w - 1, y1 = h - 1
        If area.HasValue Then
            x0 = Math.Max(0, area.Value.Left) : y0 = Math.Max(0, area.Value.Top)
            x1 = Math.Min(w - 1, area.Value.Right) : y1 = Math.Min(h - 1, area.Value.Bottom)
        End If
        Dim points As New List(Of (X As Integer, Y As Integer))()
        Dim gy = CDbl(y0)
        While gy <= y1
            Dim gx = CDbl(x0)
            While gx <= x1
                Dim x = Math.Max(0, Math.Min(w - 1, CInt(gx + (rnd.NextDouble() - 0.5) * gap)))
                Dim y = Math.Max(0, Math.Min(h - 1, CInt(gy + (rnd.NextDouble() - 0.5) * gap)))
                If err Is Nothing OrElse err(y * w + x) >= minError Then points.Add((x, y))
                gx += gap
            End While
            gy += gap
        End While
        For i = points.Count - 1 To 1 Step -1
            Dim j = rnd.Next(i + 1)
            Dim t = points(i) : points(i) = points(j) : points(j) = t
        Next
        Dim path As New List(Of OpenCvSharp.Point)(32)
        Dim forward As New List(Of OpenCvSharp.Point)(16)
        For Each p In points
            Dim i0 = (p.Y * w + p.X) * 3
            Dim cb As Integer = px(i0), cg As Integer = px(i0 + 1), cr As Integer = px(i0 + 2)
            Dim steps = CInt(reach * (0.6 + rnd.NextDouble() * 0.8) / stepLen / 2)
            path.Clear()
            Walk(p.X, p.Y, field(p.Y * w + p.X) + Math.PI, steps, stepLen, cb, cg, cr, px, field, w, h, path)
            path.Reverse()
            path.Add(New OpenCvSharp.Point(p.X, p.Y))
            forward.Clear()
            Walk(p.X, p.Y, field(p.Y * w + p.X), steps, stepLen, cb, cg, cr, px, field, w, h, forward)
            path.AddRange(forward)
            If path.Count < 2 Then path.Add(New OpenCvSharp.Point(p.X + 1, p.Y))
            ' 同一筆的顏色略有深淺、偏暖或偏冷，看起來才是一條條的顏料
            Dim tone = 0.9 + rnd.NextDouble() * 0.2, warm = (rnd.NextDouble() - 0.5) * 10
            Dim color As New Scalar(Clamp(cb * tone - warm), Clamp(cg * tone), Clamp(cr * tone + warm))
            Dim line = {path.ToArray()}
            Cv2.Polylines(canvas, line, False, color, thick, LineTypes.AntiAlias)
            Cv2.Polylines(height, line, False, Scalar.All(70 + rnd.Next(120)), thick, LineTypes.Link8)
        Next
    End Sub

    ''' <summary>從 (x, y) 順著方向走，顏色差太多就停（不畫過輪廓）。</summary>
    Private Shared Sub Walk(x As Integer, y As Integer, startAngle As Double, steps As Integer, stepLen As Double, cb As Integer, cg As Integer, cr As Integer,
                            px As Byte(), field As Single(), w As Integer, h As Integer, path As List(Of OpenCvSharp.Point))
        Dim fx = CDbl(x), fy = CDbl(y)
        Dim dx = Math.Cos(startAngle), dy = Math.Sin(startAngle)
        For s = 1 To steps
            Dim ix = CInt(fx), iy = CInt(fy)
            Dim a = field(iy * w + ix)
            Dim nx = Math.Cos(a), ny = Math.Sin(a)
            If nx * dx + ny * dy < 0 Then nx = -nx : ny = -ny ' 方向沒有正負：跟著上一步走
            dx = dx * 0.6 + nx * 0.4 : dy = dy * 0.6 + ny * 0.4
            Dim len = Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy))
            dx /= len : dy /= len
            fx += dx * stepLen : fy += dy * stepLen
            If fx < 0 OrElse fy < 0 OrElse fx > w - 1 OrElse fy > h - 1 Then Return
            Dim i = (CInt(fy) * w + CInt(fx)) * 3
            If Math.Abs(px(i) - cb) + Math.Abs(px(i + 1) - cg) + Math.Abs(px(i + 2) - cr) > 70 Then Return
            path.Add(New OpenCvSharp.Point(CInt(fx), CInt(fy)))
        Next
    End Sub

    Private Shared Function Clamp(v As Double) As Double
        Return Math.Max(0, Math.Min(255, v))
    End Function

    ''' <summary>依筆觸的厚度打光（光從左上）：筆的邊緣一邊亮一邊暗，看起來是厚厚的顏料。</summary>
    Private Shared Function Impasto(canvas As Mat, height As Mat, unit As Double) As Mat
        Using hf As New Mat(), dx As New Mat(), dy As New Mat(), light As New Mat(), f As New Mat()
            height.ConvertTo(hf, MatType.CV_32FC1, 1 / 255.0)
            Cv2.GaussianBlur(hf, hf, New OpenCvSharp.Size(0, 0), Math.Max(0.6, 0.9 * unit))
            Cv2.Sobel(hf, dx, MatType.CV_32FC1, 1, 0)
            Cv2.Sobel(hf, dy, MatType.CV_32FC1, 0, 1)
            Cv2.AddWeighted(dx, -0.16, dy, -0.16, 1.0, light)
            canvas.ConvertTo(f, MatType.CV_32FC3)
            Using l3 As New Mat()
                Cv2.Merge({light, light, light}, l3)
                Cv2.Multiply(f, l3, f)
            End Using
            Dim result As New Mat()
            f.ConvertTo(result, MatType.CV_8UC3)
            Return result
        End Using
    End Function

    Private Shared Sub WriteBack(image As Bitmap, lit As Mat, mix As Double, w As Integer, h As Integer)
        Dim px(w * h * 3 - 1) As Byte
        Marshal.Copy(lit.Data, px, 0, px.Length)
        Dim d = image.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
        Try
            Dim buf(d.Stride * h - 1) As Byte
            Marshal.Copy(d.Scan0, buf, 0, buf.Length)
            Dim stride = d.Stride
            Threading.Tasks.Parallel.For(0, h, Sub(y)
                                                   For x = 0 To w - 1
                                                       Dim i = y * stride + x * 4, j = (y * w + x) * 3
                                                       For c = 0 To 2
                                                           buf(i + c) = CByte(Math.Round(buf(i + c) + (px(j + c) - CInt(buf(i + c))) * mix))
                                                       Next
                                                   Next
                                               End Sub)
            Marshal.Copy(buf, 0, d.Scan0, buf.Length)
        Finally
            image.UnlockBits(d)
        End Try
    End Sub
End Class
