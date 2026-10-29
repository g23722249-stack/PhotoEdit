Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Threading.Tasks

''' <summary>像素圖放大的演算法。</summary>
Public Enum PixelScaleAlgorithm
    ''' <summary>最近鄰：每個像素變成 N×N 的方塊。</summary>
    Nearest = 0
    ''' <summary>Scale2x／Scale3x（EPX，AdvMAME）：只在對角相同的地方換成鄰點的顏色，不產生新顏色。</summary>
    Scale2x = 1
    ''' <summary>HQx（依 Maxim Stepin 的原理重新實作）：YUV 門檻比較 8 個鄰點，相近的顏色之間平滑、不同的顏色之間保持銳利，斜線磨圓。</summary>
    HQx = 2
    ''' <summary>xBR（Hyllian）：在 5×5 的鄰域比較兩個方向的邊緣強度，沿 45°、約 27°、約 63° 的邊把角落混成鄰點的顏色。</summary>
    XBR = 3
End Enum

''' <summary>
''' 像素圖（點陣小圖、遊戲畫面、圖示）放大：不像一般縮放那樣糊掉，也不像最近鄰那樣一格一格，斜線與曲線變平滑、邊緣保持銳利。
''' HQx 與 xBR 的混色依「輸出的每個小格被邊線蓋住多少」計算（每格 8×8 取樣），所以 2×、3×、4×……都用同一套規則。
''' 顏色比較用 YUV（亮度權重大），透明也算在差異裡，透明背景的精靈圖放大後邊緣不會混到底色。
''' </summary>
Public NotInheritable Class PixelScaler
    Private Sub New()
    End Sub

    ''' <summary>每種演算法可以選的倍數。</summary>
    Public Shared Function Factors(algo As PixelScaleAlgorithm) As Integer()
        Select Case algo
            Case PixelScaleAlgorithm.Nearest : Return {2, 3, 4, 5, 6, 8}
            Case PixelScaleAlgorithm.Scale2x : Return {2, 3, 4}
            Case PixelScaleAlgorithm.HQx : Return {2, 3, 4}
            Case Else : Return {2, 3, 4, 5, 6}
        End Select
    End Function

    ''' <summary>放大後的像素數上限（約 1 億）。</summary>
    Public Const MaxOutputPixels As Long = 100_000_000L

    Public Shared Function Upscale(src As Bitmap, algo As PixelScaleAlgorithm, factor As Integer) As Bitmap
        If Not Factors(algo).Contains(factor) Then Throw New ArgumentOutOfRangeException(NameOf(factor))
        Dim w = src.Width, h = src.Height
        If CLng(w) * h * factor * factor > MaxOutputPixels Then Throw New ArgumentException("output too large")
        Dim px = ReadArgb(src)
        Dim result As Integer()
        Select Case algo
            Case PixelScaleAlgorithm.Nearest : result = Nearest(px, w, h, factor)
            Case PixelScaleAlgorithm.Scale2x
                If factor = 3 Then
                    result = Scale3x(px, w, h)
                ElseIf factor = 4 Then
                    result = Scale2xCore(Scale2xCore(px, w, h), w * 2, h * 2)
                Else
                    result = Scale2xCore(px, w, h)
                End If
            Case PixelScaleAlgorithm.HQx : result = HQ(px, w, h, factor)
            Case Else : result = XBR(px, w, h, factor)
        End Select
        Return WriteArgb(result, w * factor, h * factor)
    End Function

    '=====================================================================
    ' 像素存取
    '=====================================================================

    Private Shared Function ReadArgb(bmp As Bitmap) As Integer()
        Dim w = bmp.Width, h = bmp.Height
        Dim px(w * h - 1) As Integer
        Using copy = bmp.Clone(New Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb)
            Dim d = copy.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            For y = 0 To h - 1
                Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w, w)
            Next
            copy.UnlockBits(d)
        End Using
        Return px
    End Function

    Private Shared Function WriteArgb(px As Integer(), w As Integer, h As Integer) As Bitmap
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Dim d = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        For y = 0 To h - 1
            Runtime.InteropServices.Marshal.Copy(px, y * w, d.Scan0 + y * d.Stride, w)
        Next
        bmp.UnlockBits(d)
        Return bmp
    End Function

    '=====================================================================
    ' 最近鄰、Scale2x／3x
    '=====================================================================

    Private Shared Function Nearest(px As Integer(), w As Integer, h As Integer, n As Integer) As Integer()
        Dim ow = w * n
        Dim o(ow * h * n - 1) As Integer
        Parallel.For(0, h * n, Sub(oy)
                                   Dim sy = oy \ n
                                   For ox = 0 To ow - 1
                                       o(oy * ow + ox) = px(sy * w + ox \ n)
                                   Next
                               End Sub)
        Return o
    End Function

    ''' <summary>
    ''' Scale2x（AdvMAME2x／EPX）：   B
    '''                              D E F   → E0 E1
    '''                                H       E2 E3
    ''' 例如 E0 = (D = B 且 B ≠ F 且 D ≠ H) ? D : E。
    ''' </summary>
    Private Shared Function Scale2xCore(px As Integer(), w As Integer, h As Integer) As Integer()
        Dim ow = w * 2
        Dim o(ow * h * 2 - 1) As Integer
        Parallel.For(0, h, Sub(y)
                               For x = 0 To w - 1
                                   Dim e = px(y * w + x)
                                   Dim b = px(Math.Max(0, y - 1) * w + x), hh = px(Math.Min(h - 1, y + 1) * w + x)
                                   Dim d = px(y * w + Math.Max(0, x - 1)), f = px(y * w + Math.Min(w - 1, x + 1))
                                   Dim e0 = e, e1 = e, e2 = e, e3 = e
                                   If b <> hh AndAlso d <> f Then
                                       If d = b Then e0 = d
                                       If b = f Then e1 = f
                                       If d = hh Then e2 = d
                                       If hh = f Then e3 = f
                                   End If
                                   Dim o0 = (y * 2) * ow + x * 2
                                   o(o0) = e0 : o(o0 + 1) = e1 : o(o0 + ow) = e2 : o(o0 + ow + 1) = e3
                               Next
                           End Sub)
        Return o
    End Function

    ''' <summary>Scale3x（AdvMAME3x）：同樣的想法，3×3 的輸出，邊的中間格看兩個角的條件。</summary>
    Private Shared Function Scale3x(px As Integer(), w As Integer, h As Integer) As Integer()
        Dim ow = w * 3
        Dim o(ow * h * 3 - 1) As Integer
        Dim at = Function(x As Integer, y As Integer) px(Math.Max(0, Math.Min(h - 1, y)) * w + Math.Max(0, Math.Min(w - 1, x)))
        Parallel.For(0, h, Sub(y)
                               For x = 0 To w - 1
                                   Dim a = at(x - 1, y - 1), b = at(x, y - 1), c = at(x + 1, y - 1)
                                   Dim d = at(x - 1, y), e = at(x, y), f = at(x + 1, y)
                                   Dim g = at(x - 1, y + 1), hh = at(x, y + 1), i = at(x + 1, y + 1)
                                   Dim r(8) As Integer
                                   For k = 0 To 8 : r(k) = e : Next
                                   If b <> hh AndAlso d <> f Then
                                       If d = b Then r(0) = d
                                       If (d = b AndAlso e <> c) OrElse (b = f AndAlso e <> a) Then r(1) = b
                                       If b = f Then r(2) = f
                                       If (d = b AndAlso e <> g) OrElse (d = hh AndAlso e <> a) Then r(3) = d
                                       If (b = f AndAlso e <> i) OrElse (hh = f AndAlso e <> c) Then r(5) = f
                                       If d = hh Then r(6) = d
                                       If (d = hh AndAlso e <> i) OrElse (hh = f AndAlso e <> g) Then r(7) = hh
                                       If hh = f Then r(8) = f
                                   End If
                                   For k = 0 To 8
                                       o((y * 3 + k \ 3) * ow + x * 3 + k Mod 3) = r(k)
                                   Next
                               Next
                           End Sub)
        Return o
    End Function

    '=====================================================================
    ' 顏色比較（YUV）與混色
    '=====================================================================

    Private Structure Yuva
        Public Y As Single, U As Single, V As Single, A As Single
    End Structure

    Private Shared Function ToYuv(c As Integer) As Yuva
        Dim a = (c >> 24) And 255, r = (c >> 16) And 255, g = (c >> 8) And 255, b = c And 255
        Return New Yuva With {.Y = 0.299F * r + 0.587F * g + 0.114F * b,
                              .U = -0.169F * r - 0.331F * g + 0.5F * b + 128,
                              .V = 0.5F * r - 0.419F * g - 0.081F * b + 128,
                              .A = a}
    End Function

    ''' <summary>HQx 的門檻：Y 差 48、U 差 7、V 差 6 以內（或兩個都幾乎透明）算同一種顏色。</summary>
    Private Shared Function Differ(p As Yuva, q As Yuva) As Boolean
        If p.A < 16 AndAlso q.A < 16 Then Return False
        If Math.Abs(p.A - q.A) > 32 Then Return True
        Return Math.Abs(p.Y - q.Y) > 48 OrElse Math.Abs(p.U - q.U) > 7 OrElse Math.Abs(p.V - q.V) > 6
    End Function

    ''' <summary>xBR 的距離：48·|ΔY| + 7·|ΔU| + 6·|ΔV|（透明差異也算進亮度）。</summary>
    Private Shared Function Dist(p As Yuva, q As Yuva) As Single
        If p.A < 16 AndAlso q.A < 16 Then Return 0
        Return 48 * (Math.Abs(p.Y - q.Y) + Math.Abs(p.A - q.A)) + 7 * Math.Abs(p.U - q.U) + 6 * Math.Abs(p.V - q.V)
    End Function

    ''' <summary>預乘透明度的混色：a、b 各佔 (1 − t)、t。</summary>
    Private Shared Function Mix(a As Integer, b As Integer, t As Single) As Integer
        If t <= 0.001F Then Return a
        If t >= 0.999F Then Return b
        Dim aa = ((a >> 24) And 255) * (1 - t), ba = ((b >> 24) And 255) * t
        Dim al = aa + ba
        If al < 0.5F Then Return 0
        Dim ch = Function(shift As Integer) CInt(Math.Round((((a >> shift) And 255) * aa + ((b >> shift) And 255) * ba) / al))
        Return (CInt(Math.Round(al)) << 24) Or (ch(16) << 16) Or (ch(8) << 8) Or ch(0)
    End Function

    ''' <summary>兩個顏色的平均（預乘透明度）。</summary>
    Private Shared Function Avg(a As Integer, b As Integer) As Integer
        Return Mix(a, b, 0.5F)
    End Function

    '=====================================================================
    ' 覆蓋率表：輸出的每個小格被某條邊線的「另一側」蓋住多少
    '=====================================================================

    ''' <summary>
    ''' 每個倍數算一次：cover(rule)(rotation)(小格) = 0..1。座標是像素內 (u, v)（0..1，右下角 = (1,1)），
    ''' 規則寫在「右下角」的方向，其他三個角把取樣點轉 90° 回來對照。
    ''' </summary>
    Private Shared ReadOnly _covers As New Dictionary(Of String, Single()())()

    Private Shared Function Cover(key As String, n As Integer, inside As Func(Of Single, Single, Boolean)) As Single()()
        Dim k = $"{key}|{n}"
        SyncLock _covers
            Dim hit As Single()() = Nothing
            If _covers.TryGetValue(k, hit) Then Return hit
        End SyncLock
        Const S As Integer = 8
        Dim table(3)() As Single
        For turn = 0 To 3
            Dim t(n * n - 1) As Single
            For sy = 0 To n - 1
                For sx = 0 To n - 1
                    Dim cnt = 0
                    For j = 0 To S - 1
                        For i = 0 To S - 1
                            ' 小格內的取樣點，以像素中心為原點
                            Dim px = (sx + (i + 0.5F) / S) / n - 0.5F, py = (sy + (j + 0.5F) / S) / n - 0.5F
                            ' 轉回右下角的方向：旋轉 (x, y) → (y, −x) 共 rot 次（rot 次 (x, y) → (−y, x) 的反向）
                            For q = 1 To turn
                                Dim tx = px
                                px = py : py = -tx
                            Next
                            If inside(px + 0.5F, py + 0.5F) Then cnt += 1
                        Next
                    Next
                    t(sy * n + sx) = cnt / CSng(S * S)
                Next
            Next
            table(turn) = t
        Next
        SyncLock _covers
            _covers(k) = table
        End SyncLock
        Return table
    End Function

    ''' <summary>鄰點位移依角落旋轉：(dx, dy) → (−dy, dx) 一次轉 90°（右下 → 左下 → 左上 → 右上）。</summary>
    Private Shared Function Rot(dx As Integer, dy As Integer, r As Integer) As (X As Integer, Y As Integer)
        For q = 1 To r
            Dim t = dx
            dx = -dy : dy = t
        Next
        Return (dx, dy)
    End Function

    '=====================================================================
    ' xBR
    '=====================================================================

    ''' <summary>
    ''' xBR（Hyllian）。右下角的鄰域（其他角旋轉）：
    '''        A1 B1 C1
    '''     A0 A  B  C  C4
    '''     D0 D  E  F  F4
    '''     G0 G  H  I  I4
    '''        G5 H5 I5
    ''' e = d(E,C)+d(E,G)+d(I,F4)+d(I,H5)+4·d(H,F)；i = d(H,D)+d(H,I5)+d(F,I4)+d(F,B)+4·d(E,I)。
    ''' e &lt; i 表示 F–H 方向是邊；再看 F–G、H–C 決定是 45° 的邊（切角）還是較平、較陡的邊（多切一格），
    ''' 角落混成 F 或 H 中和 E 比較接近的那個。
    ''' </summary>
    Private Shared Function XBR(px As Integer(), w As Integer, h As Integer, n As Integer) As Integer()
        Dim yuv(px.Length - 1) As Yuva
        For k = 0 To px.Length - 1 : yuv(k) = ToYuv(px(k)) : Next
        Dim dia = Cover("xbr-dia", n, Function(u, v) u + v > 1.5F)
        Dim left = Cover("xbr-left", n, Function(u, v) v > 1 - 0.5F * u)
        Dim up = Cover("xbr-up", n, Function(u, v) u > 1 - 0.5F * v)
        Dim both = Cover("xbr-both", n, Function(u, v) v > 1 - 0.5F * u OrElse u > 1 - 0.5F * v)
        Dim ow = w * n
        Dim o(ow * h * n - 1) As Integer
        Parallel.For(0, h, Sub(y)
                               Dim sub1(n * n - 1) As Integer
                               For x = 0 To w - 1
                                   Dim ex = x, ey = y
                                   Dim e0 = px(y * w + x)
                                   For k = 0 To sub1.Length - 1 : sub1(k) = e0 : Next
                                   For r = 0 To 3
                                       Dim rr = r
                                       Dim gy = Function(dx As Integer, dy As Integer) As Yuva
                                                    Dim p = Rot(dx, dy, rr)
                                                    Return yuv(Math.Max(0, Math.Min(h - 1, ey + p.Y)) * w + Math.Max(0, Math.Min(w - 1, ex + p.X)))
                                                End Function
                                       Dim gc = Function(dx As Integer, dy As Integer) As Integer
                                                    Dim p = Rot(dx, dy, rr)
                                                    Return px(Math.Max(0, Math.Min(h - 1, ey + p.Y)) * w + Math.Max(0, Math.Min(w - 1, ex + p.X)))
                                                End Function
                                       Dim E = gy(0, 0), B = gy(0, -1), C = gy(1, -1), D = gy(-1, 0), F = gy(1, 0), G = gy(-1, 1), Hn = gy(0, 1), I = gy(1, 1)
                                       Dim F4 = gy(2, 0), I4 = gy(2, 1), H5 = gy(0, 2), I5 = gy(1, 2)
                                       Dim eq = Function(p As Yuva, q As Yuva) Dist(p, q) < 155
                                       If eq(E, F) OrElse eq(E, Hn) Then Continue For ' 角落和兩側之一同色：沒有邊
                                       Dim ev = Dist(E, C) + Dist(E, G) + Dist(I, F4) + Dist(I, H5) + 4 * Dist(Hn, F)
                                       Dim iv = Dist(Hn, D) + Dist(Hn, I5) + Dist(F, I4) + Dist(F, B) + 4 * Dist(E, I)
                                       If ev >= iv Then Continue For
                                       Dim restriction = (Not eq(F, B) AndAlso Not eq(Hn, D)) OrElse (eq(E, I) AndAlso Not eq(F, I4) AndAlso Not eq(Hn, I5)) OrElse eq(E, G) OrElse eq(E, C)
                                       If Not restriction Then Continue For
                                       Dim ke = Dist(F, G), ki = Dist(Hn, C)
                                       Dim leftOk = (2 * ke <= ki) AndAlso Not eq(E, G) AndAlso Not eq(D, G)
                                       Dim upOk = (ke >= 2 * ki) AndAlso Not eq(E, C) AndAlso Not eq(B, C)
                                       Dim pxc = If(Dist(E, F) <= Dist(E, Hn), gc(1, 0), gc(0, 1))
                                       Dim tab = If(leftOk AndAlso upOk, both, If(leftOk, left, If(upOk, up, dia)))(rr)
                                       For k = 0 To sub1.Length - 1
                                           If tab(k) > 0 Then sub1(k) = Mix(sub1(k), pxc, tab(k))
                                       Next
                                   Next
                                   For sy = 0 To n - 1
                                       Array.Copy(sub1, sy * n, o, (y * n + sy) * ow + x * n, n)
                                   Next
                               Next
                           End Sub)
        Return o
    End Function

    '=====================================================================
    ' HQx（依原理重新實作）
    '=====================================================================

    ''' <summary>
    ''' HQx 的原理：中心點 E 和 8 個鄰點用 YUV 門檻比較，得到哪些「不同」；
    ''' 每個角落看它的兩個側邊鄰點（例如左上看 上、左）與角落鄰點：
    '''   兩側都和 E 不同、但彼此相同 → 斜線經過這個角：角落混成兩側的顏色（角落鄰點也相同時切得多一點）；
    '''   和 E 相同的鄰點 → 往它平滑過渡（漸層不會一格一格）；
    '''   和 E 不同的鄰點 → 不混，邊緣保持銳利。
    ''' 原版用手寫的規則表，這裡用同樣的判斷搭配覆蓋率計算，結果相近但不保證逐像素相同。
    ''' </summary>
    Private Shared Function HQ(px As Integer(), w As Integer, h As Integer, n As Integer) As Integer()
        Dim yuv(px.Length - 1) As Yuva
        For k = 0 To px.Length - 1 : yuv(k) = ToYuv(px(k)) : Next
        ' 右下角方向的兩種切角（斜線離角落近／遠）與兩側的平滑權重
        ' 2× 時右下小格：切大 ≈ 75%（同原版 HQ 斜線經過時的 2:3:3），切小 ≈ 60%（一像素寬的斜線也連得起來）
        Dim cutSmall = Cover("hq-cut-small", n, Function(u, v) u + v > 1.45F)
        Dim cutBig = Cover("hq-cut-big", n, Function(u, v) u + v > 1.35F)
        Dim nearRight = Cover("hq-near-right", n, Function(u, v) u > 0.75F AndAlso v > 0.25F AndAlso v < 0.75F)
        Dim nearBottom = Cover("hq-near-bottom", n, Function(u, v) v > 0.75F AndAlso u > 0.25F AndAlso u < 0.75F)
        Dim ow = w * n
        Dim o(ow * h * n - 1) As Integer
        Parallel.For(0, h, Sub(y)
                               Dim sub1(n * n - 1) As Integer
                               For x = 0 To w - 1
                                   Dim ex = x, ey = y
                                   Dim e0 = px(y * w + x)
                                   For k = 0 To sub1.Length - 1 : sub1(k) = e0 : Next
                                   Dim E = yuv(y * w + x)
                                   ' 先平滑：和 E 相同的上下左右鄰點，靠近那一邊的小格往它混一點（右、下兩個方向各轉四次就涵蓋四邊）
                                   For r = 0 To 3
                                       Dim rr = r
                                       Dim gi = Function(dx As Integer, dy As Integer) As Integer
                                                    Dim p = Rot(dx, dy, rr)
                                                    Return Math.Max(0, Math.Min(h - 1, ey + p.Y)) * w + Math.Max(0, Math.Min(w - 1, ex + p.X))
                                                End Function
                                       Dim fi = gi(1, 0)
                                       If Not Differ(E, yuv(fi)) AndAlso px(fi) <> e0 Then
                                           Dim tab = nearRight(rr)
                                           For k = 0 To sub1.Length - 1
                                               If tab(k) > 0 Then sub1(k) = Mix(sub1(k), px(fi), tab(k) * 0.25F)
                                           Next
                                       End If
                                   Next
                                   ' 再切角：右下角的兩側 F、H 都和 E 不同、彼此相同 → 斜線
                                   For r = 0 To 3
                                       Dim rr = r
                                       Dim gi = Function(dx As Integer, dy As Integer) As Integer
                                                    Dim p = Rot(dx, dy, rr)
                                                    Return Math.Max(0, Math.Min(h - 1, ey + p.Y)) * w + Math.Max(0, Math.Min(w - 1, ex + p.X))
                                                End Function
                                       Dim fi = gi(1, 0), hi = gi(0, 1), ii = gi(1, 1)
                                       Dim F = yuv(fi), Hn = yuv(hi), I = yuv(ii)
                                       If Not (Differ(E, F) AndAlso Differ(E, Hn) AndAlso Not Differ(F, Hn)) Then Continue For
                                       ' 斜對角也和 F、H 相同：E 是突出的角（例如階梯的一格），切多一點；否則切一點點
                                       Dim big = Not Differ(I, F)
                                       ' 斜對角和 E 同色：這是一條斜線延續的方向，不切（接縫由兩旁的背景格補上），斜線才會粗細均勻
                                       If Not Differ(I, E) Then Continue For
                                       Dim col = Avg(px(fi), px(hi))
                                       Dim tab = If(big, cutBig, cutSmall)(rr)
                                       For k = 0 To sub1.Length - 1
                                           If tab(k) > 0 Then sub1(k) = Mix(sub1(k), col, tab(k))
                                       Next
                                   Next
                                   For sy = 0 To n - 1
                                       Array.Copy(sub1, sy * n, o, (y * n + sy) * ow + x * n, n)
                                   Next
                               Next
                           End Sub)
        Return o
    End Function
End Class
