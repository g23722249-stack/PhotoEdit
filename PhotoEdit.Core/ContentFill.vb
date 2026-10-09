''' <summary>
''' 內容感知填滿（選取區）：每個相連的區塊各自在附近找一塊「外圍一圈最像」的位移，把那裡的照片複製過來，
''' 加上外圍的平均色差校正，邊緣往外羽化一小段；找不到可用的來源時（區塊太大、貼著邊）改用由外往內的擴散補色。
''' 原理同修補分頁的污點移除（SourceFix.CloneHeal），但不需要 OpenCV。
''' </summary>
Public NotInheritable Class ContentFill
    Private Sub New()
    End Sub

    ''' <summary>
    ''' image：BGRA（非預乘）；mask：0..255（≥ 128 算要補的洞）。回傳要疊上去的 BGRA（洞與羽化邊有 alpha，其他透明）；沒有洞時 Nothing。
    ''' </summary>
    Public Shared Function Fill(image As Byte(), w As Integer, h As Integer, mask As Byte()) As Byte()
        Dim n = w * h
        Dim hole(n - 1) As Boolean
        Dim any = False
        For i = 0 To n - 1
            If mask(i) >= 128 Then hole(i) = True : any = True
        Next
        If Not any Then Return Nothing
        Dim result(n * 4 - 1) As Byte
        Dim label(n - 1) As Integer
        Dim nextLabel = 0
        For start = 0 To n - 1
            If Not hole(start) OrElse label(start) <> 0 Then Continue For
            nextLabel += 1
            Dim region = Component(hole, label, w, h, start, nextLabel)
            FillRegion(image, w, h, hole, region, result)
        Next
        Return result
    End Function

    ''' <summary>從 start 開始的相連區塊（4 連通），標上 id。</summary>
    Private Shared Function Component(hole As Boolean(), label As Integer(), w As Integer, h As Integer, start As Integer, id As Integer) As List(Of Integer)
        Dim list As New List(Of Integer)()
        Dim stack As New Stack(Of Integer)()
        stack.Push(start) : label(start) = id
        While stack.Count > 0
            Dim i = stack.Pop()
            list.Add(i)
            Dim x = i Mod w, y = i \ w
            For Each j In {If(x > 0, i - 1, -1), If(x < w - 1, i + 1, -1), If(y > 0, i - w, -1), If(y < h - 1, i + w, -1)}
                If j >= 0 AndAlso hole(j) AndAlso label(j) = 0 Then label(j) = id : stack.Push(j)
            Next
        End While
        Return list
    End Function

    Private Shared Sub FillRegion(img As Byte(), w As Integer, h As Integer, hole As Boolean(), region As List(Of Integer), result As Byte())
        Dim minX = w, minY = h, maxX = -1, maxY = -1
        For Each i In region
            Dim x = i Mod w, y = i \ w
            If x < minX Then minX = x
            If x > maxX Then maxX = x
            If y < minY Then minY = y
            If y > maxY Then maxY = y
        Next
        Dim size = Math.Max(maxX - minX + 1, maxY - minY + 1)
        Dim ringW = Math.Max(3, Math.Min(40, CInt(size * 0.12)))
        Dim featherW = Math.Max(2, ringW \ 2)

        ' 外圍一圈（ringW 寬）與羽化帶（featherW 寬）：只看這個區塊附近的方框
        Dim bx0 = Math.Max(0, minX - ringW), by0 = Math.Max(0, minY - ringW)
        Dim bx1 = Math.Min(w - 1, maxX + ringW), by1 = Math.Min(h - 1, maxY + ringW)
        Dim bw = bx1 - bx0 + 1, bh = by1 - by0 + 1
        Dim inRegion(bw * bh - 1) As Boolean
        For Each i In region
            inRegion((i \ w - by0) * bw + (i Mod w - bx0)) = True
        Next
        Dim dist = Distance(inRegion, bw, bh)
        Dim ring As New List(Of Integer)(), band As New List(Of (Index As Integer, Alpha As Single))()
        For y = 0 To bh - 1
            For x = 0 To bw - 1
                Dim k = y * bw + x
                Dim gi = (y + by0) * w + (x + bx0)
                If inRegion(k) Then
                    band.Add((gi, 1.0F))
                ElseIf Not hole(gi) Then
                    If dist(k) <= ringW Then ring.Add(gi)
                    If dist(k) <= featherW Then band.Add((gi, CSng(1 - dist(k) / (featherW + 1))))
                End If
            Next
        Next

        ' 外圍取樣（最多約 3000 點）比對候選位移
        Dim stepRing = Math.Max(1, ring.Count \ 3000)
        Dim sampleRing = Enumerable.Range(0, (ring.Count + stepRing - 1) \ stepRing).Select(Function(k) ring(k * stepRing)).ToList()
        Dim bestScore = Double.MaxValue, bestDx = 0, bestDy = 0
        If sampleRing.Count > 0 Then
            For Each factor In {1.15, 1.5, 2.0, 2.7, 3.6}
                For a = 0 To 23
                    Dim ang = a * Math.PI / 12
                    Dim dx = CInt(Math.Round(Math.Cos(ang) * factor * size)), dy = CInt(Math.Round(Math.Sin(ang) * factor * size))
                    ' 外圍一圈（ringW，比羽化帶寬）位移後也要在影像裡
                    If minX - ringW + dx < 0 OrElse maxX + ringW + dx >= w OrElse minY - ringW + dy < 0 OrElse maxY + ringW + dy >= h Then Continue For
                    Dim shift = dy * w + dx
                    ' 來源不能落在任何要補的洞上
                    Dim overlap = 0
                    For k = 0 To region.Count - 1 Step Math.Max(1, region.Count \ 2000)
                        If hole(region(k) + shift) Then overlap += 1
                    Next
                    If overlap > 0 Then Continue For
                    Dim sum = 0.0
                    For Each i In sampleRing
                        Dim j = i + shift
                        If hole(j) Then sum += 3 * 255 * 255 : Continue For
                        For ch = 0 To 2
                            Dim d = CInt(img(i * 4 + ch)) - img(j * 4 + ch)
                            sum += d * d
                        Next
                    Next
                    Dim score = sum / sampleRing.Count * (1 + factor * 0.04) ' 同分時偏好近的來源
                    If score < bestScore Then bestScore = score : bestDx = dx : bestDy = dy
                Next
            Next
        End If

        If bestScore < Double.MaxValue Then
            Dim best = bestDy * w + bestDx
            ' 色差校正：外圍的平均差
            Dim corr(2) As Double
            For Each i In sampleRing
                For ch = 0 To 2
                    corr(ch) += CInt(img(i * 4 + ch)) - img((i + best) * 4 + ch)
                Next
            Next
            For ch = 0 To 2
                corr(ch) /= sampleRing.Count
            Next
            For Each b In band
                Dim i = b.Index, j = i + best
                For ch = 0 To 2
                    result(i * 4 + ch) = ImagePipeline.ClampByte(img(j * 4 + ch) + corr(ch))
                Next
                result(i * 4 + 3) = CByte(Math.Max(result(i * 4 + 3), CInt(Math.Round(b.Alpha * 255))))
            Next
        Else
            Diffuse(img, w, h, hole, region, ring, result)
        End If
    End Sub

    ''' <summary>找不到來源：洞先填外圍的平均色，再反覆取四鄰平均（由外往內擴散），平滑接上周圍。</summary>
    Private Shared Sub Diffuse(img As Byte(), w As Integer, h As Integer, hole As Boolean(), region As List(Of Integer), ring As List(Of Integer), result As Byte())
        Dim avg(2) As Double
        For Each i In ring
            For ch = 0 To 2
                avg(ch) += img(i * 4 + ch)
            Next
        Next
        Dim cnt = Math.Max(1, ring.Count)
        Dim cur(w * h * 3 - 1) As Single
        For i = 0 To w * h - 1
            For ch = 0 To 2
                cur(i * 3 + ch) = If(hole(i), CSng(avg(ch) / cnt), img(i * 4 + ch))
            Next
        Next
        For iter = 1 To 120
            For Each i In region
                Dim x = i Mod w, y = i \ w
                For ch = 0 To 2
                    Dim s = 0.0F, c = 0
                    If x > 0 Then s += cur((i - 1) * 3 + ch) : c += 1
                    If x < w - 1 Then s += cur((i + 1) * 3 + ch) : c += 1
                    If y > 0 Then s += cur((i - w) * 3 + ch) : c += 1
                    If y < h - 1 Then s += cur((i + w) * 3 + ch) : c += 1
                    cur(i * 3 + ch) = s / c
                Next
            Next
        Next
        For Each i In region
            For ch = 0 To 2
                result(i * 4 + ch) = ImagePipeline.ClampByte(cur(i * 3 + ch))
            Next
            result(i * 4 + 3) = 255
        Next
    End Sub

    ''' <summary>到最近的區塊像素的距離（兩趟掃描的近似歐氏距離）。</summary>
    Private Shared Function Distance(inside As Boolean(), w As Integer, h As Integer) As Single()
        Const Far As Single = 1.0E+9F
        Dim d(w * h - 1) As Single
        For i = 0 To d.Length - 1
            d(i) = If(inside(i), 0, Far)
        Next
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                If d(i) = 0 Then Continue For
                Dim v = d(i)
                If x > 0 Then v = Math.Min(v, d(i - 1) + 1)
                If y > 0 Then
                    v = Math.Min(v, d(i - w) + 1)
                    If x > 0 Then v = Math.Min(v, d(i - w - 1) + 1.4142F)
                    If x < w - 1 Then v = Math.Min(v, d(i - w + 1) + 1.4142F)
                End If
                d(i) = v
            Next
        Next
        For y = h - 1 To 0 Step -1
            For x = w - 1 To 0 Step -1
                Dim i = y * w + x
                If d(i) = 0 Then Continue For
                Dim v = d(i)
                If x < w - 1 Then v = Math.Min(v, d(i + 1) + 1)
                If y < h - 1 Then
                    v = Math.Min(v, d(i + w) + 1)
                    If x < w - 1 Then v = Math.Min(v, d(i + w + 1) + 1.4142F)
                    If x > 0 Then v = Math.Min(v, d(i + w - 1) + 1.4142F)
                End If
                d(i) = v
            Next
        Next
        Return d
    End Function
End Class
