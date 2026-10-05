Imports System.Drawing
Imports System.Drawing.Imaging

''' <summary>
''' 貼圖小幫手的拆分：一張排了很多貼圖的圖（例如 LINE 貼圖的預覽圖），依背景色找出每一張貼圖的範圍。
''' 1. 背景色：四邊最常見的顏色（PNG 透明時以透明為背景）。
''' 2. 和背景差距夠大的像素算「有顏色」。
''' 3. 有顏色的區域向外膨脹 mergeDistance 再找連通區域：靠得近的小東西（愛心、放射線）併進同一張。
''' 4. 每張的範圍 = 該區域內有顏色像素的最大範圍；太小的丟掉；依閱讀順序（一列一列、由左而右）排列。
''' </summary>
Public NotInheritable Class StickerSplitter
    Private Sub New()
    End Sub

    ''' <summary>和背景的顏色距離超過這個值才算有顏色（redmean 距離）。</summary>
    Public Const ForegroundDistance As Double = 45

    ''' <summary>讀成緊密排列的 BGRA。</summary>
    Public Shared Function ReadPixels(bmp As Bitmap) As Byte()
        Using copy As New Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(copy)
                g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height)
            End Using
            Return Perspective.ReadPixels(copy)
        End Using
    End Function

    ''' <summary>四邊最常見的顏色（每個色版量化到 16 階再投票，回傳該組的平均色）；四邊大多透明時回傳透明。</summary>
    Public Shared Function BorderColor(bgra As Byte(), w As Integer, h As Integer) As Color
        Dim votes As New Dictionary(Of Integer, (Count As Integer, R As Long, G As Long, B As Long))()
        Dim transparent = 0, total = 0
        Dim add = Sub(i As Integer)
                      total += 1
                      If bgra(i * 4 + 3) < 16 Then transparent += 1 : Return
                      Dim r = bgra(i * 4 + 2), g = bgra(i * 4 + 1), b = bgra(i * 4)
                      Dim key = (r \ 16) << 8 Or (g \ 16) << 4 Or (b \ 16)
                      Dim v As (Count As Integer, R As Long, G As Long, B As Long) = Nothing
                      votes.TryGetValue(key, v)
                      votes(key) = (v.Count + 1, v.R + r, v.G + g, v.B + b)
                  End Sub
        For x = 0 To w - 1
            add(x) : add((h - 1) * w + x)
        Next
        For y = 1 To h - 2
            add(y * w) : add(y * w + w - 1)
        Next
        If total = 0 OrElse transparent * 2 > total OrElse votes.Count = 0 Then Return Color.Transparent
        Dim best = votes.Values.OrderByDescending(Function(v) v.Count).First()
        Return Color.FromArgb(CInt(best.R \ best.Count), CInt(best.G \ best.Count), CInt(best.B \ best.Count))
    End Function

    ''' <summary>有顏色的像素（和背景不同、且不透明）。</summary>
    Public Shared Function Foreground(bgra As Byte(), w As Integer, h As Integer, background As Color) As Boolean()
        Dim fg(w * h - 1) As Boolean
        Dim transparentBg = background.A = 0
        For i = 0 To w * h - 1
            If bgra(i * 4 + 3) < 16 Then Continue For
            fg(i) = transparentBg OrElse
                    MagicWand.Distance(bgra(i * 4 + 2), bgra(i * 4 + 1), bgra(i * 4), background.R, background.G, background.B) > ForegroundDistance
        Next
        Return fg
    End Function

    ''' <summary>拆出每張貼圖的範圍（原圖像素）。</summary>
    Public Shared Function Split(bgra As Byte(), w As Integer, h As Integer, background As Color, mergeDistance As Integer, minSize As Integer) As List(Of Rectangle)
        Dim fg = Foreground(bgra, w, h, background)
        Dim grown = Dilate(fg, w, h, Math.Max(0, mergeDistance))
        ' 連通區域（8 方向）：同一區域內有顏色像素的範圍。
        Dim label(w * h - 1) As Integer
        Dim boxes As New List(Of Rectangle)()
        Dim stack As New Stack(Of Integer)()
        Dim nextLabel = 0
        For start = 0 To w * h - 1
            If Not grown(start) OrElse label(start) <> 0 Then Continue For
            nextLabel += 1
            Dim l = w, t = h, r = -1, b = -1, count = 0
            stack.Push(start)
            label(start) = nextLabel
            While stack.Count > 0
                Dim i = stack.Pop()
                Dim x = i Mod w, y = i \ w
                If fg(i) Then
                    count += 1
                    If x < l Then l = x
                    If x > r Then r = x
                    If y < t Then t = y
                    If y > b Then b = y
                End If
                For dy = -1 To 1
                    For dx = -1 To 1
                        If dx = 0 AndAlso dy = 0 Then Continue For
                        Dim nx = x + dx, ny = y + dy
                        If nx < 0 OrElse ny < 0 OrElse nx >= w OrElse ny >= h Then Continue For
                        Dim j = ny * w + nx
                        If Not grown(j) OrElse label(j) <> 0 Then Continue For
                        label(j) = nextLabel
                        stack.Push(j)
                    Next
                Next
            End While
            If r < 0 Then Continue For
            Dim box = Rectangle.FromLTRB(l, t, r + 1, b + 1)
            If box.Width < minSize OrElse box.Height < minSize OrElse count < minSize Then Continue For
            boxes.Add(box)
        Next
        Return ReadingOrder(SplitOversized(boxes, fg, w, h))
    End Function

    ''' <summary>
    ''' 貼圖預覽圖多半排成格子、每張差不多大：比一般貼圖（中位數）寬或高 1.6 倍以上的框，
    ''' 多半是幾張靠太近被速度線、音符之類連在一起，依「應該是幾張」在最空的直欄／橫列切開。
    ''' </summary>
    Private Shared Function SplitOversized(boxes As List(Of Rectangle), fg As Boolean(), w As Integer, h As Integer) As List(Of Rectangle)
        If boxes.Count < 3 Then Return boxes
        Dim medW = Median(boxes.Select(Function(b) CDbl(b.Width))), medH = Median(boxes.Select(Function(b) CDbl(b.Height)))
        Dim result As New List(Of Rectangle)()
        For Each b In boxes
            Dim parts As New List(Of Rectangle) From {b}
            If b.Width > medW * 1.6 Then parts = BestSplit(fg, w, b, b.Width / medW, medW, horizontal:=True)
            Dim rows As New List(Of Rectangle)()
            For Each p In parts
                If p.Height > medH * 1.6 Then rows.AddRange(BestSplit(fg, w, p, p.Height / medH, medH, horizontal:=False)) Else rows.Add(p)
            Next
            result.AddRange(rows)
        Next
        Return result
    End Function

    ''' <summary>
    ''' 張數取「比例無條件捨去」與「無條件進位」兩種試切：切點越空越好，切出比一般貼圖一半還窄的碎片則不採用。
    ''' </summary>
    Private Shared Function BestSplit(fg As Boolean(), w As Integer, rect As Rectangle, ratio As Double, typical As Double, horizontal As Boolean) As List(Of Rectangle)
        Dim best As List(Of Rectangle) = Nothing
        Dim bestCost = Double.MaxValue
        For Each n In {CInt(Math.Floor(ratio)), CInt(Math.Ceiling(ratio))}.Distinct()
            If n < 2 Then Continue For
            Dim cost As Double
            Dim parts = SplitAlong(fg, w, rect, n, horizontal, cost)
            Dim smallest = parts.Select(Function(p) If(horizontal, p.Width, p.Height)).DefaultIfEmpty(0).Min()
            If parts.Count < n OrElse smallest < typical * 0.5 Then cost += 1000000
            If cost < bestCost Then bestCost = cost : best = parts
        Next
        Return If(best Is Nothing OrElse bestCost >= 1000000, New List(Of Rectangle) From {rect}, best)
    End Function

    Private Shared Function Median(values As IEnumerable(Of Double)) As Double
        Dim v = values.OrderBy(Function(x) x).ToList()
        Return If(v.Count = 0, 1, Math.Max(1, v(v.Count \ 2)))
    End Function

    ''' <summary>
    ''' 把一個框平均拆成 parts 份（horizontal = 左右排列）：每個切點在預期位置 ±30% 內找有顏色像素最少的直欄（或橫列），
    ''' 每份再縮到有顏色的範圍；空的份丟掉。
    ''' </summary>
    Private Shared Function SplitAlong(fg As Boolean(), w As Integer, rect As Rectangle, parts As Integer, horizontal As Boolean,
                                       Optional ByRef cost As Double = 0) As List(Of Rectangle)
        Dim length = If(horizontal, rect.Width, rect.Height)
        Dim counts(length - 1) As Integer
        For y = rect.Top To rect.Bottom - 1
            For x = rect.Left To rect.Right - 1
                If fg(y * w + x) Then counts(If(horizontal, x - rect.Left, y - rect.Top)) += 1
            Next
        Next
        cost = 0
        Dim cuts As New List(Of Integer) From {0}
        Dim cell = length / CDbl(parts)
        For k = 1 To parts - 1
            Dim expected = CInt(k * cell)
            Dim lo = Math.Max(cuts.Last() + 1, CInt(expected - cell * 0.3)), hi = Math.Min(length - 2, CInt(expected + cell * 0.3))
            Dim best = expected, bestCount = Integer.MaxValue
            For i = lo To hi
                Dim c = counts(i)
                If c < bestCount OrElse (c = bestCount AndAlso Math.Abs(i - expected) < Math.Abs(best - expected)) Then best = i : bestCount = c
            Next
            cost += bestCount
            cuts.Add(best)
        Next
        cuts.Add(length)
        Dim result As New List(Of Rectangle)()
        For k = 0 To cuts.Count - 2
            Dim seg = If(horizontal, Rectangle.FromLTRB(rect.Left + cuts(k), rect.Top, rect.Left + cuts(k + 1), rect.Bottom),
                                     Rectangle.FromLTRB(rect.Left, rect.Top + cuts(k), rect.Right, rect.Top + cuts(k + 1)))
            Dim tight = Tighten(fg, w, seg)
            If Not tight.IsEmpty Then result.Add(tight)
        Next
        Return result
    End Function

    ''' <summary>框內有顏色像素的最大範圍；沒有時回傳 Empty。</summary>
    Private Shared Function Tighten(fg As Boolean(), w As Integer, rect As Rectangle) As Rectangle
        Dim l = Integer.MaxValue, t = Integer.MaxValue, r = -1, b = -1
        For y = rect.Top To rect.Bottom - 1
            For x = rect.Left To rect.Right - 1
                If fg(y * w + x) Then
                    If x < l Then l = x
                    If x > r Then r = x
                    If y < t Then t = y
                    If y > b Then b = y
                End If
            Next
        Next
        Return If(r < 0, Rectangle.Empty, Rectangle.FromLTRB(l, t, r + 1, b + 1))
    End Function

    ''' <summary>手動拆分：把 rect 平均拆成 parts 份（在最空的位置切），每份縮到有顏色的範圍。</summary>
    Public Shared Function SplitRect(bgra As Byte(), w As Integer, h As Integer, background As Color, rect As Rectangle,
                                     parts As Integer, horizontal As Boolean) As List(Of Rectangle)
        rect.Intersect(New Rectangle(0, 0, w, h))
        If parts < 2 OrElse rect.Width < parts * 2 OrElse rect.Height < parts * 2 Then Return New List(Of Rectangle) From {rect}
        Return SplitAlong(Foreground(bgra, w, h, background), w, rect, parts, horizontal)
    End Function

    ''' <summary>一列一列、由左而右：中心高度落在同一列範圍內的歸為同一列。</summary>
    Public Shared Function ReadingOrder(boxes As IEnumerable(Of Rectangle)) As List(Of Rectangle)
        Dim rows As New List(Of List(Of Rectangle))()
        For Each b In boxes.OrderBy(Function(r) r.Top + r.Height / 2.0)
            Dim cy = b.Top + b.Height / 2.0
            Dim row = rows.FirstOrDefault(Function(rw)
                                              Dim top = rw.Min(Function(r) r.Top), bottom = rw.Max(Function(r) r.Bottom)
                                              Return cy >= top AndAlso cy <= bottom
                                          End Function)
            If row Is Nothing Then
                row = New List(Of Rectangle)()
                rows.Add(row)
            End If
            row.Add(b)
        Next
        Return rows.OrderBy(Function(rw) rw.Min(Function(r) r.Top)).SelectMany(Function(rw) rw.OrderBy(Function(r) r.Left)).ToList()
    End Function

    ''' <summary>方形膨脹（先橫向再縱向，用累加和）。</summary>
    Private Shared Function Dilate(src As Boolean(), w As Integer, h As Integer, r As Integer) As Boolean()
        If r <= 0 Then Return DirectCast(src.Clone(), Boolean())
        Dim tmp(w * h - 1) As Boolean, dst(w * h - 1) As Boolean
        Dim prefix(Math.Max(w, h)) As Integer
        For y = 0 To h - 1
            prefix(0) = 0
            For x = 0 To w - 1
                prefix(x + 1) = prefix(x) + If(src(y * w + x), 1, 0)
            Next
            For x = 0 To w - 1
                tmp(y * w + x) = prefix(Math.Min(w, x + r + 1)) - prefix(Math.Max(0, x - r)) > 0
            Next
        Next
        For x = 0 To w - 1
            prefix(0) = 0
            For y = 0 To h - 1
                prefix(y + 1) = prefix(y) + If(tmp(y * w + x), 1, 0)
            Next
            For y = 0 To h - 1
                dst(y * w + x) = prefix(Math.Min(h, y + r + 1)) - prefix(Math.Max(0, y - r)) > 0
            Next
        Next
        Return dst
    End Function

    ''' <summary>剪下一塊（32 位元）。</summary>
    Public Shared Function Crop(source As Bitmap, rect As Rectangle) As Bitmap
        rect.Intersect(New Rectangle(0, 0, source.Width, source.Height))
        Dim bmp As New Bitmap(Math.Max(1, rect.Width), Math.Max(1, rect.Height), PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.DrawImage(source, New Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel)
        End Using
        Return bmp
    End Function

    ''' <summary>剪掉四周透明的部分（保留少許邊）；全透明時回傳 Nothing。</summary>
    Public Shared Function TrimTransparent(bmp As Bitmap, Optional padding As Integer = 2) As Bitmap
        Dim w = bmp.Width, h = bmp.Height
        Dim px = ReadPixels(bmp)
        Dim l = w, t = h, r = -1, b = -1
        For y = 0 To h - 1
            For x = 0 To w - 1
                If px((y * w + x) * 4 + 3) > 10 Then
                    If x < l Then l = x
                    If x > r Then r = x
                    If y < t Then t = y
                    If y > b Then b = y
                End If
            Next
        Next
        If r < 0 Then Return Nothing
        Dim rect = Rectangle.FromLTRB(Math.Max(0, l - padding), Math.Max(0, t - padding), Math.Min(w, r + padding + 1), Math.Min(h, b + padding + 1))
        Return Crop(bmp, rect)
    End Function
End Class
