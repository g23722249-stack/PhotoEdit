''' <summary>浮點陣列的高斯近似模糊（三次盒狀模糊）與形態學運算。</summary>
Public NotInheritable Class Blur
    Private Sub New()
    End Sub

    ''' <summary>對遮罩做高斯近似模糊（sigma 以像素計），原地修改。</summary>
    Public Shared Sub GaussianInPlace(mask As FloatMask, sigma As Double)
        GaussianInPlace(mask.Data, mask.Width, mask.Height, 1, sigma)
    End Sub

    ''' <summary>對交錯 channels 個通道的浮點緩衝區做高斯近似模糊，原地修改。</summary>
    Public Shared Sub GaussianInPlace(data As Single(), width As Integer, height As Integer, channels As Integer, sigma As Double)
        If sigma < 0.3 Then Return
        For Each r In BoxRadii(sigma, 3)
            If r < 1 Then Continue For
            BoxHorizontal(data, width, height, channels, r)
            BoxVertical(data, width, height, channels, r)
        Next
    End Sub

    ' 三次盒狀模糊逼近高斯的半徑（Ivan Kutskir 方法）。
    Private Shared Function BoxRadii(sigma As Double, n As Integer) As Integer()
        Dim wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1)
        Dim wl = CInt(Math.Floor(wIdeal))
        If wl Mod 2 = 0 Then wl -= 1
        Dim wu = wl + 2
        Dim mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4)
        Dim m = CInt(Math.Round(mIdeal))
        Dim r(n - 1) As Integer
        For i = 0 To n - 1
            r(i) = If(i < m, wl, wu) \ 2
        Next
        Return r
    End Function

    Private Shared Sub BoxHorizontal(data As Single(), w As Integer, h As Integer, ch As Integer, r As Integer)
        Dim inv = 1.0F / (2 * r + 1)
        Threading.Tasks.Parallel.For(0, h,
            Sub(y)
                Dim line(w * ch - 1) As Single
                Dim row = y * w * ch
                Array.Copy(data, row, line, 0, w * ch)
                For c = 0 To ch - 1
                    ' 邊界外延：視為重複端點像素。
                    Dim acc = 0.0F
                    For k = -r To r
                        acc += line(Math.Clamp(k, 0, w - 1) * ch + c)
                    Next
                    For x = 0 To w - 1
                        data(row + x * ch + c) = acc * inv
                        Dim addX = Math.Min(x + r + 1, w - 1), subX = Math.Max(x - r, 0)
                        acc += line(addX * ch + c) - line(subX * ch + c)
                    Next
                Next
            End Sub)
    End Sub

    Private Shared Sub BoxVertical(data As Single(), w As Integer, h As Integer, ch As Integer, r As Integer)
        Dim inv = 1.0F / (2 * r + 1)
        Dim rowLen = w * ch
        Threading.Tasks.Parallel.For(0, rowLen,
            Sub(col)
                Dim line(h - 1) As Single
                For y = 0 To h - 1
                    line(y) = data(y * rowLen + col)
                Next
                Dim acc = 0.0F
                For k = -r To r
                    acc += line(Math.Clamp(k, 0, h - 1))
                Next
                For y = 0 To h - 1
                    data(y * rowLen + col) = acc * inv
                    acc += line(Math.Min(y + r + 1, h - 1)) - line(Math.Max(y - r, 0))
                Next
            End Sub)
    End Sub

    ''' <summary>灰階膨脹（radius &gt; 0）或侵蝕（radius &lt; 0），以正方形結構元素分離計算。</summary>
    Public Shared Function Morph(mask As FloatMask, radius As Integer) As FloatMask
        If radius = 0 Then Return mask.Clone()
        Dim dilate = radius > 0
        Dim r = Math.Abs(radius)
        Dim w = mask.Width, h = mask.Height
        Dim tmp(w * h - 1) As Single, dst As New FloatMask(w, h)
        Dim src = mask.Data
        Threading.Tasks.Parallel.For(0, h,
            Sub(y)
                For x = 0 To w - 1
                    Dim v = If(dilate, Single.MinValue, Single.MaxValue)
                    For k = Math.Max(0, x - r) To Math.Min(w - 1, x + r)
                        Dim s = src(y * w + k)
                        v = If(dilate, Math.Max(v, s), Math.Min(v, s))
                    Next
                    tmp(y * w + x) = v
                Next
            End Sub)
        Threading.Tasks.Parallel.For(0, w,
            Sub(x)
                For y = 0 To h - 1
                    Dim v = If(dilate, Single.MinValue, Single.MaxValue)
                    For k = Math.Max(0, y - r) To Math.Min(h - 1, y + r)
                        Dim s = tmp(k * w + x)
                        v = If(dilate, Math.Max(v, s), Math.Min(v, s))
                    Next
                    dst.Data(y * w + x) = v
                Next
            End Sub)
        Return dst
    End Function
End Class
