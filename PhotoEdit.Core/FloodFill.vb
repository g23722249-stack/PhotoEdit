''' <summary>
''' 油漆桶的填色範圍：從種子點找出顏色相近的區域（相連或整張圖），可以封閉線稿上的小缺口、往外擴張、柔邊。
'''
''' 封閉缺口的做法：先把「擋住的像素」（顏色差太多，例如線稿）往外加粗 gap 像素，寬度不到 2×gap 的缺口就被補起來，
''' 在剩下的空間裡填色；之後再只在「可以填」的像素裡往外長回 gap+1 像素，填色就會貼齊線稿，
''' 但只會從缺口探出去一點點，不會漏到外面。
''' </summary>
Public NotInheritable Class FloodFill

    ''' <summary>
    ''' sample：BGRA（非預乘）像素；回傳每個像素的填色覆蓋率 0..1。種子點在影像外時回傳 Nothing。
    ''' tolerance 0..255；gap、expand 以像素為單位。
    ''' </summary>
    Public Shared Function Region(sample As Byte(), w As Integer, h As Integer, seedX As Integer, seedY As Integer,
                           tolerance As Integer, contiguous As Boolean, gap As Integer, expand As Integer, antiAlias As Boolean) As Single()
        If seedX < 0 OrElse seedY < 0 OrElse seedX >= w OrElse seedY >= h Then Return Nothing
        Dim n = w * h
        Dim passable = Similar(sample, n, seedY * w + seedX, Math.Max(0, Math.Min(255, tolerance)))

        ' 1. 封閉缺口：離「擋住的像素」不到 gap 的地方先不能走
        Dim allowed = passable
        If gap > 0 Then
            Dim dist = DistanceTo(passable, w, h, target:=False)
            Dim narrowed(n - 1) As Boolean
            For i = 0 To n - 1
                narrowed(i) = passable(i) AndAlso dist(i) > gap
            Next
            ' 點在很窄的地方（比缺口還窄的縫）：不封閉缺口，照一般方式填
            If narrowed(seedY * w + seedX) Then allowed = narrowed
        End If

        ' 2. 填色
        Dim filled As Boolean()
        If contiguous Then
            filled = Grow(allowed, w, h, seedY * w + seedX)
        Else
            filled = DirectCast(allowed.Clone(), Boolean())
        End If

        ' 3. 封閉缺口後長回去：只在可以填的像素裡往外長 gap+1，貼齊線稿
        If gap > 0 AndAlso allowed IsNot passable Then filled = GeodesicGrow(filled, passable, w, h, gap + 1)

        ' 4. 擴張：不受顏色限制往外長，蓋住線稿邊緣半透明的像素
        If expand > 0 Then
            Dim d = DistanceTo(filled, w, h, target:=True)
            For i = 0 To n - 1
                If Not filled(i) AndAlso d(i) <= expand Then filled(i) = True
            Next
        End If

        Dim mask(n - 1) As Single
        For i = 0 To n - 1
            If filled(i) Then mask(i) = 1
        Next
        If antiAlias Then mask = SoftenEdges(mask, filled, w, h)
        Return mask
    End Function

    ''' <summary>
    ''' 和種子點顏色相近的像素：以預乘 alpha 的 RGBA 比較，最大通道差 ≤ tolerance。
    ''' 預乘讓「全透明」彼此相同（空白圖層可以一次填滿），半透明的邊緣則和兩邊都有差。
    ''' </summary>
    Private Shared Function Similar(px As Byte(), n As Integer, seed As Integer, tolerance As Integer) As Boolean()
        Dim sa = CInt(px(seed * 4 + 3))
        Dim sb = px(seed * 4) * sa \ 255, sg = px(seed * 4 + 1) * sa \ 255, sr = px(seed * 4 + 2) * sa \ 255
        Dim result(n - 1) As Boolean
        For i = 0 To n - 1
            Dim a = CInt(px(i * 4 + 3))
            Dim d = Math.Abs(a - sa)
            If d > tolerance Then Continue For
            d = Math.Max(d, Math.Abs(px(i * 4) * a \ 255 - sb))
            d = Math.Max(d, Math.Abs(px(i * 4 + 1) * a \ 255 - sg))
            d = Math.Max(d, Math.Abs(px(i * 4 + 2) * a \ 255 - sr))
            result(i) = d <= tolerance
        Next
        Return result
    End Function

    ''' <summary>從種子點往上下左右擴散（4 連通，填色不會從對角的細縫鑽出去）。</summary>
    Private Shared Function Grow(allowed As Boolean(), w As Integer, h As Integer, seed As Integer) As Boolean()
        Dim filled(w * h - 1) As Boolean
        If Not allowed(seed) Then Return filled
        Dim stack As New Stack(Of Integer)()
        stack.Push(seed)
        filled(seed) = True
        While stack.Count > 0
            Dim i = stack.Pop()
            Dim x = i Mod w, y = i \ w
            If x > 0 AndAlso allowed(i - 1) AndAlso Not filled(i - 1) Then filled(i - 1) = True : stack.Push(i - 1)
            If x < w - 1 AndAlso allowed(i + 1) AndAlso Not filled(i + 1) Then filled(i + 1) = True : stack.Push(i + 1)
            If y > 0 AndAlso allowed(i - w) AndAlso Not filled(i - w) Then filled(i - w) = True : stack.Push(i - w)
            If y < h - 1 AndAlso allowed(i + w) AndAlso Not filled(i + w) Then filled(i + w) = True : stack.Push(i + w)
        End While
        Return filled
    End Function

    ''' <summary>在 passable 裡從已填的範圍往外長 steps 步（8 連通）。</summary>
    Private Shared Function GeodesicGrow(filled As Boolean(), passable As Boolean(), w As Integer, h As Integer, steps As Integer) As Boolean()
        Dim result = DirectCast(filled.Clone(), Boolean())
        Dim frontier As New List(Of Integer)()
        For i = 0 To filled.Length - 1
            If filled(i) Then frontier.Add(i)
        Next
        For s = 1 To steps
            Dim nextFront As New List(Of Integer)()
            For Each i In frontier
                Dim x = i Mod w, y = i \ w
                For dy = -1 To 1
                    Dim yy = y + dy
                    If yy < 0 OrElse yy >= h Then Continue For
                    For dx = -1 To 1
                        Dim xx = x + dx
                        If xx < 0 OrElse xx >= w Then Continue For
                        Dim j = yy * w + xx
                        If passable(j) AndAlso Not result(j) Then result(j) = True : nextFront.Add(j)
                    Next
                Next
            Next
            If nextFront.Count = 0 Then Exit For
            frontier = nextFront
        Next
        Return result
    End Function

    ''' <summary>每個像素到最近的「值為 target」像素的距離（近似歐氏，兩趟掃描；沒有時為很大的數）。</summary>
    Private Shared Function DistanceTo(mask As Boolean(), w As Integer, h As Integer, target As Boolean) As Single()
        Const Far As Single = 1.0E+9F
        Const D1 As Single = 1.0F, D2 As Single = 1.4142F
        Dim d(w * h - 1) As Single
        For i = 0 To d.Length - 1
            d(i) = If(mask(i) = target, 0, Far)
        Next
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                If d(i) = 0 Then Continue For
                Dim v = d(i)
                If x > 0 Then v = Math.Min(v, d(i - 1) + D1)
                If y > 0 Then
                    v = Math.Min(v, d(i - w) + D1)
                    If x > 0 Then v = Math.Min(v, d(i - w - 1) + D2)
                    If x < w - 1 Then v = Math.Min(v, d(i - w + 1) + D2)
                End If
                d(i) = v
            Next
        Next
        For y = h - 1 To 0 Step -1
            For x = w - 1 To 0 Step -1
                Dim i = y * w + x
                If d(i) = 0 Then Continue For
                Dim v = d(i)
                If x < w - 1 Then v = Math.Min(v, d(i + 1) + D1)
                If y < h - 1 Then
                    v = Math.Min(v, d(i + w) + D1)
                    If x < w - 1 Then v = Math.Min(v, d(i + w + 1) + D2)
                    If x > 0 Then v = Math.Min(v, d(i + w - 1) + D2)
                End If
                d(i) = v
            Next
        Next
        Return d
    End Function

    ''' <summary>柔邊：只在填色的邊界做 3×3 平均，裡面維持 1。</summary>
    Private Shared Function SoftenEdges(mask As Single(), filled As Boolean(), w As Integer, h As Integer) As Single()
        Dim result = DirectCast(mask.Clone(), Single())
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                ' 只看邊界：自己和四鄰不一樣
                Dim edge = (x > 0 AndAlso filled(i - 1) <> filled(i)) OrElse (x < w - 1 AndAlso filled(i + 1) <> filled(i)) OrElse
                           (y > 0 AndAlso filled(i - w) <> filled(i)) OrElse (y < h - 1 AndAlso filled(i + w) <> filled(i))
                If Not edge Then Continue For
                Dim sum = 0.0F, cnt = 0
                For dy = -1 To 1
                    Dim yy = y + dy
                    If yy < 0 OrElse yy >= h Then Continue For
                    For dx = -1 To 1
                        Dim xx = x + dx
                        If xx < 0 OrElse xx >= w Then Continue For
                        sum += mask(yy * w + xx) : cnt += 1
                    Next
                Next
                result(i) = sum / cnt
            Next
        Next
        Return result
    End Function
End Class
