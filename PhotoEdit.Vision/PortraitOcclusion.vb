Imports System.Runtime.InteropServices
Imports OpenCvSharp

''' <summary>
''' 臉上的遮擋物（劍、手、杯子、麥克風……）：妝與戲曲臉譜不能畫在上面。
''' 以臉頰、額頭、下巴取樣的膚色為準，臉的範圍裡顏色差很多、又不是暗部（鬍子、陰影、髮絲）、也不是五官的地方，算遮擋物；
''' 小塊的（油光、痣、雜點）用形態學去掉，只留成片的。人像去背模型會把手上拿的東西算進人，所以不用它。
''' </summary>
Partial Public NotInheritable Class PortraitRetouch
    Private Shared ReadOnly OccOval As Integer() = {10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109}
    Private Shared ReadOnly OccLips As Integer() = {61, 185, 40, 39, 37, 0, 267, 269, 270, 409, 291, 375, 321, 405, 314, 17, 84, 181, 91, 146}
    Private Shared ReadOnly OccBrows As Integer()() = {
        New Integer() {70, 63, 105, 66, 107, 55, 65, 52, 53, 46},
        New Integer() {300, 293, 334, 296, 336, 285, 295, 282, 283, 276}}
    ''' <summary>取樣膚色的點：兩頰、額頭、下巴、鼻樑。</summary>
    Private Shared ReadOnly OccSkinPoints As Integer() = {50, 280, 151, 199, 6, 117, 346}

    ''' <summary>
    ''' 遮擋程度（0..1，原圖大小 w×h，只填臉附近；沒有網格的臉不算）。source 用原圖（還沒上妝）的顏色判斷。
    ''' 回傳 Nothing＝沒有遮擋。
    ''' </summary>
    Private Shared Function FaceOcclusion(src As Mat, faces As IEnumerable(Of FaceRegion)) As Single()
        Dim w = src.Cols, h = src.Rows
        Dim result As Single() = Nothing
        For Each f In faces
            Dim m = f.Mesh
            If m Is Nothing OrElse m.Length < 468 Then Continue For
            Dim P = Function(i As Integer) New Point2f(m(i).X * w, m(i).Y * h)
            Dim fw = Math.Sqrt((P(234).X - P(454).X) ^ 2 + (P(234).Y - P(454).Y) ^ 2)
            If fw < 24 Then Continue For
            ' 範圍：臉的輪廓放大，額頭往上多一點（戲曲臉譜會畫到額頭上面）
            Dim cx = OccOval.Average(Function(i) P(i).X), cy = OccOval.Average(Function(i) P(i).Y)
            Dim region = OccOval.Select(Function(i)
                                            Dim dx = P(i).X - cx, dy = P(i).Y - cy
                                            Return New OpenCvSharp.Point(CInt(cx + dx * 1.1), CInt(cy + dy * If(dy < 0, 1.35, 1.08)))
                                        End Function).ToArray()
            Dim box = Cv2.BoundingRect(region).Intersect(New Rect(0, 0, w, h))
            If box.Width < 8 OrElse box.Height < 8 Then Continue For
            Dim bw = box.Width, bh = box.Height
            Dim lab(bw * bh * 3 - 1) As Byte
            Using roi As New Mat(src, box), roiC As New Mat(), labM As New Mat()
                Cv2.CvtColor(roi, labM, ColorConversionCodes.BGR2Lab)
                Using cont = labM.Clone()
                    Marshal.Copy(cont.Data, lab, 0, lab.Length)
                End Using
            End Using
            ' 膚色：取樣點附近的中位數與離散程度（遮擋物蓋到其中幾點也不影響中位數）
            Dim r = Math.Max(2, CInt(fw * 0.05))
            Dim sL As New List(Of Double), sA As New List(Of Double), sB As New List(Of Double)
            For Each pi In OccSkinPoints
                Dim c = P(pi)
                For y = CInt(c.Y) - r To CInt(c.Y) + r
                    For x = CInt(c.X) - r To CInt(c.X) + r
                        If x < box.X OrElse y < box.Y OrElse x >= box.Right OrElse y >= box.Bottom Then Continue For
                        If (x - c.X) ^ 2 + (y - c.Y) ^ 2 > r * r Then Continue For
                        Dim j = ((y - box.Y) * bw + (x - box.X)) * 3
                        sL.Add(lab(j)) : sA.Add(lab(j + 1)) : sB.Add(lab(j + 2))
                    Next
                Next
            Next
            If sA.Count < 20 Then Continue For
            Dim med = Function(v As List(Of Double)) As Double
                          Dim s = v.OrderBy(Function(q) q).ToList()
                          Return s(s.Count \ 2)
                      End Function
            Dim mL = med(sL), mA = med(sA), mB = med(sB)
            ' 每點：比膚色灰很多（金屬、布）或色相差很多（藍、綠、金）才算；比膚色更紅、更飽和（腮紅、潮紅、唇）還是皮膚。很暗的地方（頭髮、鬍子、深陰影）不算：顏色不可靠，戲曲的片子也畫在頭髮上。
            Dim va = mA - 128, vb = mB - 128, cv = Math.Sqrt(va * va + vb * vb)
            If cv < 6 Then Continue For ' 臉色本身接近灰色（冷光、黑白、暗場）：分不出遮擋物，不處理
            Using occ As New Mat(bh, bw, MatType.CV_8UC1, Scalar.All(0)), inside As New Mat(bh, bw, MatType.CV_8UC1, Scalar.All(0)),
                  keep As New Mat(bh, bw, MatType.CV_8UC1, Scalar.All(0))
                Dim ob(bw * bh - 1) As Byte, wb(bw * bh - 1) As Byte
                For i = 0 To bw * bh - 1
                    Dim L As Double = lab(i * 3), a As Double = lab(i * 3 + 1), b As Double = lab(i * 3 + 2)
                    ' 暗的皮膚（陰影）彩度也低：依亮度把彩度放大回來再比；灰色的金屬、布放大後還是灰的
                    Dim s = Math.Min(2.5, Math.Max(1.0, mL / Math.Max(1.0, L)))
                    Dim pa = (a - 128) * s, pb = (b - 128) * s
                    Dim pr = Math.Sqrt(pa * pa + pb * pb)
                    Dim gray = 1 - Smooth(pr / cv, 0.35, 0.6)
                    Dim ang = If(pr < 0.5, 180.0, Math.Acos(Math.Max(-1.0, Math.Min(1.0, (pa * va + pb * vb) / (pr * cv)))) * 180 / Math.PI)
                    Dim colorV = Math.Max(gray, Smooth(ang, 30, 45))
                    ob(i) = CByte(colorV * Smooth(L, mL * 0.35, mL * 0.5) * 255)
                    wb(i) = CByte(colorV * Smooth(L, mL * 0.08, mL * 0.18) * 255) ' 寬鬆版：暗一點也算（只用來把確定的遮擋物往外長）
                Next
                Marshal.Copy(ob, 0, occ.Data, ob.Length)
                ' 只在臉的範圍裡；五官（眼、眉、唇、鼻孔）不算
                Dim shift = Function(q As Point2f) New OpenCvSharp.Point(CInt(q.X - box.X), CInt(q.Y - box.Y))
                Cv2.FillPoly(inside, {region.Select(Function(q) New OpenCvSharp.Point(q.X - box.X, q.Y - box.Y)).ToArray()}, Scalar.All(255))
                Dim featW = Math.Max(2, CInt(fw * 0.03))
                For Each ring In MeshEyeRing
                    Dim pts = ring.Select(Function(i) shift(P(i))).ToArray()
                    Cv2.FillPoly(keep, {pts}, Scalar.All(255))
                    Cv2.Polylines(keep, {pts}, True, Scalar.All(255), featW)
                Next
                For Each brow In OccBrows
                    Dim pts = brow.Select(Function(i) shift(P(i))).ToArray()
                    Cv2.FillPoly(keep, {pts}, Scalar.All(255))
                    Cv2.Polylines(keep, {pts}, True, Scalar.All(255), featW)
                Next
                Dim lips = OccLips.Select(Function(i) shift(P(i))).ToArray()
                Cv2.FillPoly(keep, {lips}, Scalar.All(255))
                Cv2.Polylines(keep, {lips}, True, Scalar.All(255), featW)
                For Each n In {98, 327, 2}
                    Cv2.Circle(keep, shift(P(n)), Math.Max(2, CInt(fw * 0.045)), Scalar.All(255), -1)
                Next
                Cv2.Threshold(occ, occ, 127, 255, ThresholdTypes.Binary)
                Cv2.BitwiseAnd(occ, inside, occ)
                Using notKeep As New Mat()
                    Cv2.BitwiseNot(keep, notKeep)
                    Cv2.BitwiseAnd(occ, notKeep, occ)
                End Using
                ' 小塊去掉（油光、痣、雜點），留下成片的遮擋物；再補平邊緣
                Dim k = Math.Max(3, CInt(fw * 0.04)) Or 1
                Using kern = Cv2.GetStructuringElement(MorphShapes.Ellipse, New OpenCvSharp.Size(k, k))
                    Cv2.MorphologyEx(occ, occ, MorphTypes.Open, kern)
                    Cv2.MorphologyEx(occ, occ, MorphTypes.Close, kern)
                End Using
                ' 遮擋物是從臉外伸進來的：只留碰到範圍邊緣的區塊（臉中間孤立的一塊多半是陰影、法令紋）
                Using inner As New Mat(), band As New Mat(), labels As New Mat()
                    Dim bwid = Math.Max(3, CInt(fw * 0.06)) Or 1
                    Using kern = Cv2.GetStructuringElement(MorphShapes.Ellipse, New OpenCvSharp.Size(bwid, bwid))
                        Cv2.Erode(inside, inner, kern)
                    End Using
                    Cv2.Subtract(inside, inner, band)
                    Dim count = Cv2.ConnectedComponents(occ, labels, PixelConnectivity.Connectivity8, MatType.CV_32S)
                    If count > 1 Then
                        Dim lb(bw * bh - 1) As Integer, bb(bw * bh - 1) As Byte, oc(bw * bh - 1) As Byte
                        Marshal.Copy(labels.Data, lb, 0, lb.Length)
                        Marshal.Copy(band.Data, bb, 0, bb.Length)
                        Dim touches(count - 1) As Boolean
                        For i = 0 To lb.Length - 1
                            If lb(i) > 0 AndAlso bb(i) <> 0 Then touches(lb(i)) = True
                        Next
                        For i = 0 To lb.Length - 1
                            oc(i) = If(lb(i) > 0 AndAlso touches(lb(i)), CByte(255), CByte(0))
                        Next
                        Marshal.Copy(oc, 0, occ.Data, oc.Length)
                    End If
                End Using
                ' 兩段門檻：顏色一樣但比較暗、而且和確定的遮擋物連在一起的（劍身下緣的陰影、手指之間）也算；
                ' 單獨的深色區塊（頭髮、鬍子）不算
                If Cv2.CountNonZero(occ) > 0 Then
                    Using weak As New Mat(bh, bw, MatType.CV_8UC1), wl As New Mat(), notKeep As New Mat()
                        Marshal.Copy(wb, 0, weak.Data, wb.Length)
                        Cv2.Threshold(weak, weak, 127, 255, ThresholdTypes.Binary)
                        Cv2.BitwiseAnd(weak, inside, weak)
                        Cv2.BitwiseNot(keep, notKeep)
                        Cv2.BitwiseAnd(weak, notKeep, weak)
                        Cv2.BitwiseOr(weak, occ, weak)
                        Dim wn = Cv2.ConnectedComponents(weak, wl, PixelConnectivity.Connectivity8, MatType.CV_32S)
                        Dim wlb(bw * bh - 1) As Integer, seedB(bw * bh - 1) As Byte, res(bw * bh - 1) As Byte
                        Marshal.Copy(wl.Data, wlb, 0, wlb.Length)
                        Marshal.Copy(occ.Data, seedB, 0, seedB.Length)
                        Dim seeded(wn - 1) As Boolean
                        For i = 0 To wlb.Length - 1
                            If seedB(i) <> 0 AndAlso wlb(i) > 0 Then seeded(wlb(i)) = True
                        Next
                        For i = 0 To wlb.Length - 1
                            res(i) = If(wlb(i) > 0 AndAlso seeded(wlb(i)), CByte(255), CByte(0))
                        Next
                        Marshal.Copy(res, 0, occ.Data, res.Length)
                    End Using
                    Using kern = Cv2.GetStructuringElement(MorphShapes.Ellipse, New OpenCvSharp.Size(k, k))
                        Cv2.MorphologyEx(occ, occ, MorphTypes.Close, kern)
                    End Using
                    Cv2.BitwiseAnd(occ, inside, occ)
                End If
                If Cv2.CountNonZero(occ) = 0 Then Continue For
                Using occF As New Mat()
                    occ.ConvertTo(occF, MatType.CV_32FC1, 1 / 255.0)
                    Cv2.GaussianBlur(occF, occF, New OpenCvSharp.Size(0, 0), Math.Max(0.8, fw * 0.006))
                    Dim fv = GetFloats(occF)
                    If result Is Nothing Then ReDim result(w * h - 1)
                    For y = 0 To bh - 1
                        For x = 0 To bw - 1
                            Dim gi = (box.Y + y) * w + box.X + x
                            result(gi) = Math.Max(result(gi), fv(y * bw + x))
                        Next
                    Next
                End Using
            End Using
        Next
        Return result
    End Function
End Class
