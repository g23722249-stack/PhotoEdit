Imports System.Drawing
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>
''' 在已轉正原圖上先做的處理，順序：污點／雜物移除 → 降噪 → 人像修飾 → 去背換背景。
''' ImagePipeline.Render 的 prepare 參數就是呼叫這裡；預覽（1600 縮圖）與全尺寸匯出共用同一套演算法。
''' </summary>
Public NotInheritable Class SourceFix
    Private Sub New()
    End Sub

    ''' <summary>回傳處理後的新圖；配方沒有任何原圖處理時回傳 Nothing（表示不變）。aiMask 為去背的 AI 遮罩（可為 Nothing）。</summary>
    Public Shared Function Apply(source As Bitmap, faces As IReadOnlyList(Of FaceRegion), recipe As EditRecipe,
                                 Optional aiMask As Bitmap = Nothing) As Bitmap
        Dim fixedImage = ApplyRetouch(source, faces, recipe)
        If recipe.Cutout Is Nothing OrElse Not recipe.Cutout.ChangesImage Then Return fixedImage
        Dim composed = CutoutCompositor.Compose(If(fixedImage, source), aiMask, recipe.Cutout)
        fixedImage?.Dispose()
        Return composed
    End Function

    Private Shared Function ApplyRetouch(source As Bitmap, faces As IReadOnlyList(Of FaceRegion), recipe As EditRecipe) As Bitmap
        If Not (recipe.HasSpots OrElse recipe.Denoise > 0 OrElse recipe.ColorNoise > 0 OrElse recipe.HasPortrait) Then Return Nothing

        Dim fixedImage As Bitmap = Nothing
        If recipe.HasSpots OrElse recipe.Denoise > 0 OrElse recipe.ColorNoise > 0 Then
            Using bgra = BitmapConverter.ToMat(source), bgr As New Mat()
                Cv2.CvtColor(bgra, bgr, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2BGR, ColorConversionCodes.RGB2BGR))
                If recipe.HasSpots Then HealSpots(bgr, recipe.Spots)
                If recipe.Denoise > 0 OrElse recipe.ColorNoise > 0 Then Denoise(bgr, recipe.Denoise / 100.0, recipe.ColorNoise / 100.0)
                Using outBgra As New Mat()
                    Cv2.CvtColor(bgr, outBgra, ColorConversionCodes.BGR2BGRA)
                    fixedImage = BitmapConverter.ToBitmap(outBgra)
                End Using
            End Using
        End If

        Dim portrait = PortraitRetouch.Apply(If(fixedImage, source), faces, recipe)
        If portrait IsNot Nothing Then
            fixedImage?.Dispose()
            Return portrait
        End If
        Return fixedImage
    End Function

    '=====================================================================
    ' 污點／雜物移除
    '=====================================================================

    ''' <summary>
    ''' 逐筆處理（後面的筆觸會看到前面修好的結果），每筆只處理筆觸附近的範圍。
    ''' 先找附近紋理相符的區域複製過來（像 Lightroom 的「修復」）：以筆觸外圍一圈像素的差異挑選來源，
    ''' 再校正平均顏色、羽化邊緣；找不到合適來源時才用 inpaint（Telea）從邊緣補。
    ''' </summary>
    Public Shared Sub HealSpots(bgr As Mat, spots As IEnumerable(Of SpotStroke))
        Dim w = bgr.Cols, h = bgr.Rows
        Dim longSide = Math.Max(w, h)
        For Each s In spots
            Dim pts = s.Points().Select(Function(p) New OpenCvSharp.Point(CInt(p.X * w), CInt(p.Y * h))).ToList()
            If pts.Count = 0 Then Continue For
            Dim r = Math.Max(1, CInt(Math.Round(s.Radius * longSide)))
            Dim margin = r * 9 + 4
            Dim roi = New Rect(pts.Min(Function(p) p.X) - margin, pts.Min(Function(p) p.Y) - margin, 0, 0)
            roi.Width = pts.Max(Function(p) p.X) + margin - roi.X
            roi.Height = pts.Max(Function(p) p.Y) + margin - roi.Y
            roi = roi.Intersect(New Rect(0, 0, w, h))
            If roi.Width < 4 OrElse roi.Height < 4 Then Continue For

            Using mask As New Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.All(0)), view As New Mat(bgr, roi)
                Dim local = pts.Select(Function(p) New OpenCvSharp.Point(p.X - roi.X, p.Y - roi.Y)).ToList()
                ' 稍微塗大一點（1.15 倍），把污點邊緣的暈也蓋進去。
                Dim thickness = Math.Max(2, CInt(r * 2 * 1.15))
                If local.Count = 1 Then
                    Cv2.Circle(mask, local(0), thickness \ 2, Scalar.All(255), -1)
                Else
                    For i = 1 To local.Count - 1
                        Cv2.Line(mask, local(i - 1), local(i), Scalar.All(255), thickness, LineTypes.Link8)
                    Next
                End If
                If Not CloneHeal(view, mask, r) Then
                    Using healed As New Mat()
                        Cv2.Inpaint(view, mask, healed, Math.Max(3.0, longSide / 300.0), InpaintMethod.Telea)
                        healed.CopyTo(view, mask)
                    End Using
                End If
            End Using
        Next
    End Sub

    ''' <summary>從附近位移 offset 的區域複製紋理補洞。找不到可用的來源時回傳 False。</summary>
    Private Shared Function CloneHeal(view As Mat, mask As Mat, r As Integer) As Boolean
        Dim w = view.Cols, h = view.Rows
        Dim ringWidth = Math.Max(2, CInt(r * 0.5))
        Using dilated As New Mat(), ring As New Mat(), feather As New Mat(), img As Mat = view.Clone()
            Using kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, New OpenCvSharp.Size(ringWidth * 2 + 1, ringWidth * 2 + 1))
                Cv2.Dilate(mask, dilated, kernel)
            End Using
            Cv2.Subtract(dilated, mask, ring)
            ' 羽化：洞內完全取代，外圍一圈漸變。
            Cv2.GaussianBlur(dilated, feather, New OpenCvSharp.Size(0, 0), Math.Max(1.0, ringWidth * 0.5))
            Cv2.Max(feather, mask, feather)

            Dim px = GetBytes(img), mk = GetBytes(mask), rg = GetBytes(ring), fe = GetBytes(feather)
            Dim holeIdx As New List(Of Integer)(), ringIdx As New List(Of Integer)()
            Dim minX = w, minY = h, maxX = 0, maxY = 0
            For y = 0 To h - 1
                For x = 0 To w - 1
                    Dim i = y * w + x
                    If fe(i) > 0 Then
                        holeIdx.Add(i)
                        minX = Math.Min(minX, x) : maxX = Math.Max(maxX, x)
                        minY = Math.Min(minY, y) : maxY = Math.Max(maxY, y)
                    End If
                    If rg(i) > 0 Then ringIdx.Add(i)
                Next
            Next
            If holeIdx.Count = 0 OrElse ringIdx.Count = 0 Then Return False

            ' 候選位移：24 個方向 × 幾種距離；來源區不能和洞重疊、也不能超出範圍。
            Dim bestScore = Double.MaxValue, bestDx = 0, bestDy = 0
            For Each dist In {2.4, 3.5, 5.0, 7.0}
                For a = 0 To 23
                    Dim ang = a * Math.PI / 12
                    Dim dx = CInt(Math.Round(Math.Cos(ang) * dist * r)), dy = CInt(Math.Round(Math.Sin(ang) * dist * r))
                    If minX + dx < 0 OrElse maxX + dx >= w OrElse minY + dy < 0 OrElse maxY + dy >= h Then Continue For
                    Dim shift = dy * w + dx
                    Dim overlap = 0
                    For Each i In holeIdx
                        If mk(i + shift) > 0 Then overlap += 1
                    Next
                    If overlap > holeIdx.Count \ 50 Then Continue For
                    Dim sum = 0.0
                    For Each i In ringIdx
                        For ch = 0 To 2
                            Dim d = CInt(px(i * 3 + ch)) - px((i + shift) * 3 + ch)
                            sum += d * d
                        Next
                    Next
                    Dim score = sum / ringIdx.Count * (1 + dist * 0.03) ' 同分時偏好近的來源
                    If score < bestScore Then bestScore = score : bestDx = dx : bestDy = dy
                Next
            Next
            If bestScore = Double.MaxValue Then Return False

            ' 色差校正：外圍一圈的平均差加回來源。
            Dim best = bestDy * w + bestDx
            Dim shiftColor(2) As Double
            For Each i In ringIdx
                For ch = 0 To 2
                    shiftColor(ch) += CInt(px(i * 3 + ch)) - px((i + best) * 3 + ch)
                Next
            Next
            For ch = 0 To 2
                shiftColor(ch) /= ringIdx.Count
            Next

            Dim result = DirectCast(px.Clone(), Byte())
            For Each i In holeIdx
                Dim a = fe(i) / 255.0
                For ch = 0 To 2
                    Dim patch = px((i + best) * 3 + ch) + shiftColor(ch)
                    result(i * 3 + ch) = ImagePipeline.ClampByte(px(i * 3 + ch) * (1 - a) + patch * a)
                Next
            Next
            Using healed As New Mat(h, w, MatType.CV_8UC3)
                Runtime.InteropServices.Marshal.Copy(result, 0, healed.Data, result.Length)
                healed.CopyTo(view)
            End Using
            Return True
        End Using
    End Function

    Private Shared Function GetBytes(m As Mat) As Byte()
        Using c = If(m.IsContinuous(), Nothing, m.Clone())
            Dim src = If(c, m)
            Dim n = CInt(src.Total() * src.ElemSize())
            Dim a(n - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(src.Data, a, 0, n)
            Return a
        End Using
    End Function

    '=====================================================================
    ' 降噪
    '=====================================================================

    ''' <summary>
    ''' 明度：小範圍雙邊濾波（保留邊緣），依強度混合；色彩：模糊色度通道去掉彩色雜點，
    ''' 色彩雜點通常比像素大，模糊範圍隨影像尺寸放大。
    ''' </summary>
    Public Shared Sub Denoise(bgr As Mat, luminance As Double, color As Double)
        Dim scale = Math.Max(1.0, Math.Max(bgr.Cols, bgr.Rows) / 1600.0)
        Using ycc As New Mat()
            Cv2.CvtColor(bgr, ycc, ColorConversionCodes.BGR2YCrCb)
            Dim ch = ycc.Split()
            Try
                If luminance > 0 Then
                    Using filtered As New Mat()
                        Cv2.BilateralFilter(ch(0), filtered, 5, 6 + 40 * luminance, 2)
                        Dim k = Math.Min(1.0, luminance * 1.3)
                        Cv2.AddWeighted(ch(0), 1 - k, filtered, k, 0, ch(0))
                    End Using
                End If
                If color > 0 Then
                    Dim sigma = (0.8 + 3.2 * color) * scale
                    Cv2.GaussianBlur(ch(1), ch(1), New OpenCvSharp.Size(0, 0), sigma)
                    Cv2.GaussianBlur(ch(2), ch(2), New OpenCvSharp.Size(0, 0), sigma)
                End If
                Cv2.Merge(ch, ycc)
            Finally
                For Each m In ch
                    m.Dispose()
                Next
            End Try
            Cv2.CvtColor(ycc, bgr, ColorConversionCodes.YCrCb2BGR)
        End Using
    End Sub
End Class

''' <summary>自動拉直：找出畫面中接近水平或垂直的長直線，取長度加權中位數的傾斜角。</summary>
Public NotInheritable Class AutoStraighten
    Private Sub New()
    End Sub

    Private Const AnalyzeSize As Integer = 1000
    Private Const MaxTilt As Double = 20

    ''' <summary>
    ''' 回傳建議的拉直角度（度，正值順時針，可直接設給 EditRecipe.Straighten）；
    ''' 找不到足夠的直線時回傳 Nothing。影像應是「已旋轉/透視、尚未拉直與裁切」的畫面。
    ''' </summary>
    Public Shared Function Suggest(image As Bitmap) As Double?
        Using bgra = BitmapConverter.ToMat(image), gray As New Mat(), small As New Mat(), edges As New Mat()
            Cv2.CvtColor(bgra, gray, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2GRAY, ColorConversionCodes.RGB2GRAY))
            Dim k = AnalyzeSize / CDbl(Math.Max(gray.Cols, gray.Rows))
            Cv2.Resize(gray, small, New OpenCvSharp.Size(Math.Max(1, CInt(gray.Cols * k)), Math.Max(1, CInt(gray.Rows * k))), 0, 0, InterpolationFlags.Area)
            Cv2.GaussianBlur(small, small, New OpenCvSharp.Size(3, 3), 0)
            Cv2.Canny(small, edges, 50, 150)
            Dim longSide = Math.Max(small.Cols, small.Rows)
            Dim lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 60, longSide * 0.08, longSide * 0.01)

            Dim samples As New List(Of (Dev As Double, Weight As Double))()
            For Each l In lines
                Dim dx = CDbl(l.P2.X - l.P1.X), dy = CDbl(l.P2.Y - l.P1.Y)
                Dim len = Math.Sqrt(dx * dx + dy * dy)
                Dim angle = Math.Atan2(dy, dx) * 180 / Math.PI
                If angle <= -90 Then angle += 180
                If angle > 90 Then angle -= 180
                Dim dev As Double
                If Math.Abs(angle) <= MaxTilt Then
                    dev = angle
                ElseIf Math.Abs(angle) >= 90 - MaxTilt Then
                    dev = If(angle > 0, angle - 90, angle + 90)
                Else
                    Continue For
                End If
                samples.Add((dev, len))
            Next
            Dim total = samples.Sum(Function(s) s.Weight)
            If samples.Count < 2 OrElse total < longSide * 0.3 Then Return Nothing

            ' 長度加權中位數：少數斜線（屋頂、樓梯）不會帶偏結果。
            Dim acc = 0.0
            For Each s In samples.OrderBy(Function(x) x.Dev)
                acc += s.Weight
                If acc >= total / 2 Then Return Math.Round(-s.Dev, 1)
            Next
            Return Nothing
        End Using
    End Function
End Class
