''' <summary>
''' 人物圖層的取樣器：RGB（未預乘，8 位元）＋浮點 Alpha，以預乘方式做雙三次（Catmull-Rom）或雙線性取樣。
''' 大幅縮小時改用面積平均建立的縮圖金字塔，避免鋸齒；每次都從原圖（或其金字塔）取樣，不累積重採樣損失。
''' </summary>
Public NotInheritable Class LayerSampler
    Private ReadOnly _rgb As RgbaImage
    Private ReadOnly _alpha As FloatMask
    ' 金字塔：第 i 層（i ≥ 1）是寬高約為原圖 1/2^i 的預乘浮點 RGBA。
    Private ReadOnly _levels As New List(Of Single())()
    Private ReadOnly _levelSizes As New List(Of (W As Integer, H As Integer))()
    Private ReadOnly _lock As New Object()

    Public ReadOnly Property Width As Integer
    Public ReadOnly Property Height As Integer

    Public Sub New(rgb As RgbaImage, alpha As FloatMask)
        If alpha.Width <> rgb.Width OrElse alpha.Height <> rgb.Height Then Throw New ArgumentException("Alpha 與 RGB 尺寸不同。")
        _rgb = rgb
        _alpha = alpha
        Width = rgb.Width
        Height = rgb.Height
    End Sub

    ''' <summary>依縮放倍率選金字塔層級（讓該層的縮放倍率至少 0.5）。</summary>
    Public Function LevelFor(scale As Double) As Integer
        Dim lvl = 0
        Dim s = scale
        While s < 0.5 AndAlso (Width >> (lvl + 1)) >= 2 AndAlso (Height >> (lvl + 1)) >= 2
            s *= 2
            lvl += 1
        End While
        Return lvl
    End Function

    Private Function GetLevel(level As Integer) As (Data As Single(), W As Integer, H As Integer)
        SyncLock _lock
            While _levels.Count < level
                Dim i = _levels.Count + 1
                Dim prev As Single(), pw As Integer, ph As Integer
                If i = 1 Then
                    pw = Width : ph = Height
                    prev = Nothing
                Else
                    prev = _levels(i - 2) : pw = _levelSizes(i - 2).W : ph = _levelSizes(i - 2).H
                End If
                Dim nw = Math.Max(1, pw \ 2), nh = Math.Max(1, ph \ 2)
                Dim d(nw * nh * 4 - 1) As Single
                For y = 0 To nh - 1
                    For x = 0 To nw - 1
                        Dim acc0 = 0.0F, acc1 = 0.0F, acc2 = 0.0F, acc3 = 0.0F
                        For dy = 0 To 1
                            For dx = 0 To 1
                                Dim sx = Math.Min(pw - 1, 2 * x + dx), sy = Math.Min(ph - 1, 2 * y + dy)
                                If prev Is Nothing Then
                                    Dim p = Level0(sx, sy)
                                    acc0 += p.R : acc1 += p.G : acc2 += p.B : acc3 += p.A
                                Else
                                    Dim j = (sy * pw + sx) * 4
                                    acc0 += prev(j) : acc1 += prev(j + 1) : acc2 += prev(j + 2) : acc3 += prev(j + 3)
                                End If
                            Next
                        Next
                        Dim o = (y * nw + x) * 4
                        d(o) = acc0 * 0.25F : d(o + 1) = acc1 * 0.25F : d(o + 2) = acc2 * 0.25F : d(o + 3) = acc3 * 0.25F
                    Next
                Next
                _levels.Add(d)
                _levelSizes.Add((nw, nh))
            End While
            Return (_levels(level - 1), _levelSizes(level - 1).W, _levelSizes(level - 1).H)
        End SyncLock
    End Function

    ''' <summary>原圖像素（預乘、0..1）；超出範圍為全透明。</summary>
    Private Function Level0(x As Integer, y As Integer) As (R As Single, G As Single, B As Single, A As Single)
        If x < 0 OrElse y < 0 OrElse x >= Width OrElse y >= Height Then Return (0, 0, 0, 0)
        Dim i = y * Width + x
        Dim a = _alpha.Data(i)
        If a <= 0 Then Return (0, 0, 0, 0)
        Dim k = a / 255.0F
        Dim p = _rgb.Pixels
        Return (p(i * 4) * k, p(i * 4 + 1) * k, p(i * 4 + 2) * k, a)
    End Function

    ''' <summary>
    ''' 取樣器的一個檢視（固定層級）。座標以原圖像素計（像素中心在 +0.5），內部換算到該層級。
    ''' </summary>
    Public Function View(level As Integer) As SamplerView
        If level <= 0 Then Return New SamplerView(Me, Nothing, Width, Height, 1)
        Dim l = GetLevel(level)
        Return New SamplerView(Me, l.Data, l.W, l.H, l.W / CDbl(Width))
    End Function

    Public NotInheritable Class SamplerView
        Private ReadOnly _owner As LayerSampler
        Private ReadOnly _data As Single()
        Private ReadOnly _w As Integer
        Private ReadOnly _h As Integer
        Private ReadOnly _k As Double

        Friend Sub New(owner As LayerSampler, data As Single(), w As Integer, h As Integer, k As Double)
            _owner = owner : _data = data : _w = w : _h = h : _k = k
        End Sub

        Private Function Px(x As Integer, y As Integer) As (R As Single, G As Single, B As Single, A As Single)
            If _data Is Nothing Then Return _owner.Level0(x, y)
            If x < 0 OrElse y < 0 OrElse x >= _w OrElse y >= _h Then Return (0, 0, 0, 0)
            Dim j = (y * _w + x) * 4
            Return (_data(j), _data(j + 1), _data(j + 2), _data(j + 3))
        End Function

        ''' <summary>雙線性取樣（預乘）。</summary>
        Public Function Bilinear(x As Double, y As Double) As (R As Single, G As Single, B As Single, A As Single)
            Dim fx = x * _k - 0.5, fy = y * _k - 0.5
            Dim x0 = CInt(Math.Floor(fx)), y0 = CInt(Math.Floor(fy))
            Dim tx = CSng(fx - x0), ty = CSng(fy - y0)
            Dim a = Px(x0, y0), b = Px(x0 + 1, y0), c = Px(x0, y0 + 1), d = Px(x0 + 1, y0 + 1)
            Dim w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty), w01 = (1 - tx) * ty, w11 = tx * ty
            Return (a.R * w00 + b.R * w10 + c.R * w01 + d.R * w11,
                    a.G * w00 + b.G * w10 + c.G * w01 + d.G * w11,
                    a.B * w00 + b.B * w10 + c.B * w01 + d.B * w11,
                    a.A * w00 + b.A * w10 + c.A * w01 + d.A * w11)
        End Function

        ''' <summary>雙三次 Catmull-Rom 取樣（預乘），結果夾限避免振鈴造成負值或 RGB 超過 Alpha。</summary>
        Public Function Bicubic(x As Double, y As Double) As (R As Single, G As Single, B As Single, A As Single)
            Dim fx = x * _k - 0.5, fy = y * _k - 0.5
            Dim x0 = CInt(Math.Floor(fx)), y0 = CInt(Math.Floor(fy))
            Dim tx = fx - x0, ty = fy - y0
            Dim wx = CatmullRom(tx), wy = CatmullRom(ty)
            Dim r = 0.0F, g = 0.0F, b = 0.0F, a = 0.0F
            For j = 0 To 3
                Dim wyj = wy(j)
                If wyj = 0 Then Continue For
                For i = 0 To 3
                    Dim wt = wx(i) * wyj
                    If wt = 0 Then Continue For
                    Dim p = Px(x0 - 1 + i, y0 - 1 + j)
                    r += p.R * wt : g += p.G * wt : b += p.B * wt : a += p.A * wt
                Next
            Next
            a = Math.Clamp(a, 0.0F, 1.0F)
            Return (Math.Clamp(r, 0.0F, a), Math.Clamp(g, 0.0F, a), Math.Clamp(b, 0.0F, a), a)
        End Function

        ''' <summary>只取 Alpha（陰影用，雙線性）。</summary>
        Public Function AlphaBilinear(x As Double, y As Double) As Single
            Return Bilinear(x, y).A
        End Function

        Private Shared Function CatmullRom(t As Double) As Single()
            Dim t2 = t * t, t3 = t2 * t
            Return {CSng(-0.5 * t3 + t2 - 0.5 * t),
                    CSng(1.5 * t3 - 2.5 * t2 + 1),
                    CSng(-1.5 * t3 + 2 * t2 + 0.5 * t),
                    CSng(0.5 * t3 - 0.5 * t2)}
        End Function
    End Class
End Class
