''' <summary>
''' 魔術棒：以點擊處的顏色為基準，選出顏色相近的區域（0..255，邊緣有一圈漸層避免鋸齒）。
''' 顏色差距用「紅色加權」的 RGB 距離（接近人眼感受，比單純 RGB 距離自然）；
''' 相似程度 0..100 以 1.5 次方對應門檻，低數值時可以細調。
''' </summary>
Public NotInheritable Class MagicWand
    Private Sub New()
    End Sub

    ''' <summary>最大顏色距離（黑與白之間）。</summary>
    Public Const MaxDistance As Double = 765

    ''' <summary>相似程度 → 顏色距離門檻（0 → 只選相同顏色，100 → 全選）。</summary>
    Public Shared Function Threshold(tolerance As Integer) As Double
        Dim t = Math.Max(0, Math.Min(100, tolerance)) / 100.0
        Return MaxDistance * Math.Pow(t, 1.5) + 0.5
    End Function

    ''' <summary>紅色加權的 RGB 距離（redmean）。</summary>
    Public Shared Function Distance(r1 As Integer, g1 As Integer, b1 As Integer, r2 As Integer, g2 As Integer, b2 As Integer) As Double
        Dim rm = (r1 + r2) / 2.0
        Dim dr = r1 - r2, dg = g1 - g2, db = b1 - b2
        Return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db)
    End Function

    ''' <summary>
    ''' 選取範圍：bgra 為緊密排列的 BGRA 像素。相連模式從點擊處往四周擴散（只走顏色相近的像素）；
    ''' 全圖模式選所有相近的顏色。門檻外再一小段距離的像素給部分選取，邊緣才平順。
    ''' </summary>
    Public Shared Function SelectRegion(bgra As Byte(), w As Integer, h As Integer, click As WandClick) As Byte()
        Dim sel(w * h - 1) As Byte
        If w <= 0 OrElse h <= 0 Then Return sel
        Dim px = Math.Max(0, Math.Min(w - 1, CInt(Math.Floor(click.X * w))))
        Dim py = Math.Max(0, Math.Min(h - 1, CInt(Math.Floor(click.Y * h))))
        Dim seed = py * w + px
        Dim sb = bgra(seed * 4), sg = bgra(seed * 4 + 1), sr = bgra(seed * 4 + 2)
        Dim t = Threshold(click.Tolerance)
        Dim soft = t * 0.2 + 10
        Dim dist = Function(i As Integer) Distance(bgra(i * 4 + 2), bgra(i * 4 + 1), bgra(i * 4), sr, sg, sb)
        Dim edgeAlpha = Function(d As Double) CByte(Math.Max(0, Math.Min(255, 255 * (1 - (d - t) / soft))))

        If Not click.Contiguous Then
            For i = 0 To w * h - 1
                Dim d = dist(i)
                If d <= t Then
                    sel(i) = 255
                ElseIf d < t + soft Then
                    sel(i) = edgeAlpha(d)
                End If
            Next
            Return sel
        End If

        ' 相連：四方向擴散。visited 0 = 未看過，1 = 已選，2 = 看過但不選（只給邊緣漸層）。
        Dim visited(w * h - 1) As Byte
        Dim stack As New Stack(Of Integer)()
        stack.Push(seed)
        visited(seed) = 1
        sel(seed) = 255
        While stack.Count > 0
            Dim i = stack.Pop()
            Dim x = i Mod w, y = i \ w
            For k = 0 To 3
                Dim nx = x + If(k = 0, 1, If(k = 1, -1, 0)), ny = y + If(k = 2, 1, If(k = 3, -1, 0))
                If nx < 0 OrElse ny < 0 OrElse nx >= w OrElse ny >= h Then Continue For
                Dim j = ny * w + nx
                If visited(j) <> 0 Then Continue For
                Dim d = dist(j)
                If d <= t Then
                    visited(j) = 1
                    sel(j) = 255
                    stack.Push(j)
                Else
                    visited(j) = 2
                    If d < t + soft Then sel(j) = edgeAlpha(d)
                End If
            Next
        End While
        Return sel
    End Function

    ''' <summary>
    ''' 背景色填充：從四邊所有接近背景色的像素出發往內擴散，選出與邊緣相連的背景（255 = 背景）。
    ''' 被線條包起來的同色區域（眼白、白衣服）不會被選到。backgroundArgb = 0 時自動取四邊最常見的顏色。
    ''' </summary>
    Public Shared Function EdgeFill(bgra As Byte(), w As Integer, h As Integer, tolerance As Integer, backgroundArgb As Integer) As Byte()
        Dim sel(w * h - 1) As Byte
        If w <= 0 OrElse h <= 0 Then Return sel
        Dim bg = If(backgroundArgb = 0, StickerSplitter.BorderColor(bgra, w, h), Drawing.Color.FromArgb(backgroundArgb))
        Dim t = Threshold(tolerance)
        Dim soft = t * 0.2 + 10
        Dim dist = Function(i As Integer)
                       ' 透明像素一律當背景。
                       If bgra(i * 4 + 3) < 16 Then Return 0.0
                       Return Distance(bgra(i * 4 + 2), bgra(i * 4 + 1), bgra(i * 4), bg.R, bg.G, bg.B)
                   End Function
        Dim visited(w * h - 1) As Byte
        Dim stack As New Stack(Of Integer)()
        Dim seed = Sub(i As Integer)
                       If visited(i) <> 0 Then Return
                       Dim d = dist(i)
                       If d <= t Then
                           visited(i) = 1 : sel(i) = 255 : stack.Push(i)
                       End If
                   End Sub
        For x = 0 To w - 1
            seed(x) : seed((h - 1) * w + x)
        Next
        For y = 0 To h - 1
            seed(y * w) : seed(y * w + w - 1)
        Next
        While stack.Count > 0
            Dim i = stack.Pop()
            Dim x = i Mod w, y = i \ w
            For k = 0 To 3
                Dim nx = x + If(k = 0, 1, If(k = 1, -1, 0)), ny = y + If(k = 2, 1, If(k = 3, -1, 0))
                If nx < 0 OrElse ny < 0 OrElse nx >= w OrElse ny >= h Then Continue For
                Dim j = ny * w + nx
                If visited(j) <> 0 Then Continue For
                Dim d = dist(j)
                If d <= t Then
                    visited(j) = 1 : sel(j) = 255 : stack.Push(j)
                Else
                    visited(j) = 2
                    If d < t + soft Then sel(j) = CByte(Math.Max(0, Math.Min(255, 255 * (1 - (d - t) / soft))))
                End If
            Next
        End While
        Return sel
    End Function

    ''' <summary>
    ''' 清除雜點：選取範圍裡的小破洞補滿、零星的小選取點去掉（面積小於整張的 minFraction）。
    ''' 用 8 方向連通區域計算。
    ''' </summary>
    Public Shared Sub Despeckle(sel As Byte(), w As Integer, h As Integer, Optional minFraction As Double = 0.0005)
        Dim minArea = Math.Max(4, CInt(w * h * minFraction))
        FillSmall(sel, w, h, minArea, selected:=False)
        FillSmall(sel, w, h, Math.Max(2, minArea \ 2), selected:=True)
    End Sub

    ''' <summary>selected = False：找未選取的小區域（破洞）並選起來；True：找選取的小區域並取消。</summary>
    Private Shared Sub FillSmall(sel As Byte(), w As Integer, h As Integer, maxArea As Integer, selected As Boolean)
        Dim seen(w * h - 1) As Boolean
        Dim region As New List(Of Integer)()
        Dim stack As New Stack(Of Integer)()
        Dim inSet = Function(i As Integer) (sel(i) >= 128) = selected
        For start = 0 To w * h - 1
            If seen(start) OrElse Not inSet(start) Then Continue For
            region.Clear()
            stack.Push(start)
            seen(start) = True
            Dim tooBig = False
            While stack.Count > 0
                Dim i = stack.Pop()
                If Not tooBig Then region.Add(i)
                If region.Count > maxArea Then tooBig = True
                Dim x = i Mod w, y = i \ w
                For dy = -1 To 1
                    For dx = -1 To 1
                        If dx = 0 AndAlso dy = 0 Then Continue For
                        Dim nx = x + dx, ny = y + dy
                        If nx < 0 OrElse ny < 0 OrElse nx >= w OrElse ny >= h Then Continue For
                        Dim j = ny * w + nx
                        If seen(j) OrElse Not inSet(j) Then Continue For
                        seen(j) = True
                        stack.Push(j)
                    Next
                Next
            End While
            If Not tooBig Then
                Dim v = If(selected, CByte(0), CByte(255))
                For Each i In region
                    sel(i) = v
                Next
            End If
        Next
    End Sub

    ''' <summary>把一次點擊套到遮罩上（255 = 主體）：去除時遮罩 × (1 − 選取)，補回時取較大值。</summary>
    Public Shared Sub ApplyToMask(mask As Byte(), sel As Byte(), restore As Boolean)
        For i = 0 To mask.Length - 1
            Dim s = sel(i)
            If s = 0 Then Continue For
            If restore Then
                If s > mask(i) Then mask(i) = s
            Else
                mask(i) = CByte(CInt(mask(i)) * (255 - s) \ 255)
            End If
        Next
    End Sub
End Class
