''' <summary>
''' 8 位元 RGBA 影像（未預乘 Alpha，sRGB 編碼），像素依 R、G、B、A 順序逐列存放。
''' 照片與人物原圖都用這個格式保存，避免整張大圖轉成浮點數占用四倍記憶體；
''' 需要高精度的運算（遮罩、重採樣、合成）在區域內轉成浮點數處理。
''' </summary>
Public NotInheritable Class RgbaImage
    Public ReadOnly Property Width As Integer
    Public ReadOnly Property Height As Integer
    ''' <summary>長度 = Width × Height × 4。</summary>
    Public ReadOnly Property Pixels As Byte()

    Public Sub New(width As Integer, height As Integer)
        Me.New(width, height, Nothing)
    End Sub

    Public Sub New(width As Integer, height As Integer, pixels As Byte())
        ImageLimits.CheckSize(width, height, 4)
        Me.Width = width
        Me.Height = height
        Dim n = CLng(width) * height * 4
        If pixels Is Nothing Then
            Me.Pixels = New Byte(CInt(n) - 1) {}
        Else
            If pixels.LongLength <> n Then Throw New ArgumentException($"像素長度 {pixels.LongLength} 與尺寸 {width}×{height} 不符。", NameOf(pixels))
            Me.Pixels = pixels
        End If
    End Sub

    Public ReadOnly Property PixelCount As Integer
        Get
            Return Width * Height
        End Get
    End Property

    ''' <summary>有任何像素不是完全不透明。</summary>
    Public Function HasTransparency() As Boolean
        Dim p = Pixels
        For i = 3 To p.Length - 1 Step 4
            If p(i) < 255 Then Return True
        Next
        Return False
    End Function

    ''' <summary>Alpha 通道轉成 0..1 的遮罩。</summary>
    Public Function ExtractAlpha() As FloatMask
        Dim m As New FloatMask(Width, Height)
        Dim d = m.Data, p = Pixels
        For i = 0 To d.Length - 1
            d(i) = p(i * 4 + 3) / 255.0F
        Next
        Return m
    End Function

    ''' <summary>用遮罩取代 Alpha（遮罩須同尺寸），回傳新影像。</summary>
    Public Function WithAlpha(mask As FloatMask) As RgbaImage
        If mask.Width <> Width OrElse mask.Height <> Height Then Throw New ArgumentException("遮罩尺寸與影像不同。", NameOf(mask))
        Dim r = Clone()
        Dim p = r.Pixels, d = mask.Data
        For i = 0 To d.Length - 1
            p(i * 4 + 3) = ToByte(d(i))
        Next
        Return r
    End Function

    Public Function Clone() As RgbaImage
        Return New RgbaImage(Width, Height, CType(Pixels.Clone(), Byte()))
    End Function

    Public Function GetPixel(x As Integer, y As Integer) As (R As Byte, G As Byte, B As Byte, A As Byte)
        Dim i = (y * Width + x) * 4
        Return (Pixels(i), Pixels(i + 1), Pixels(i + 2), Pixels(i + 3))
    End Function

    Public Sub SetPixel(x As Integer, y As Integer, r As Byte, g As Byte, b As Byte, a As Byte)
        Dim i = (y * Width + x) * 4
        Pixels(i) = r : Pixels(i + 1) = g : Pixels(i + 2) = b : Pixels(i + 3) = a
    End Sub

    Public Shared Function Filled(width As Integer, height As Integer, r As Byte, g As Byte, b As Byte, Optional a As Byte = 255) As RgbaImage
        Dim img As New RgbaImage(width, height)
        Dim p = img.Pixels
        For i = 0 To p.Length - 1 Step 4
            p(i) = r : p(i + 1) = g : p(i + 2) = b : p(i + 3) = a
        Next
        Return img
    End Function

    ''' <summary>0..1 轉 0..255（四捨五入、夾限）。</summary>
    Public Shared Function ToByte(v As Single) As Byte
        If Not (v > 0) Then Return 0
        If v >= 1 Then Return 255
        Return CByte(Math.Round(v * 255.0F))
    End Function

    ''' <summary>
    ''' 縮小到長邊不超過 maxSide（只縮不放）。以面積平均（box filter）縮小，Alpha 以預乘方式平均，
    ''' 避免透明像素的 RGB 殘值滲進邊緣。回傳的 scale 是新尺寸 ÷ 原尺寸。
    ''' </summary>
    Public Function Downscale(maxSide As Integer, ByRef scale As Double) As RgbaImage
        Dim longSide = Math.Max(Width, Height)
        If longSide <= maxSide Then
            scale = 1
            Return Me
        End If
        scale = maxSide / CDbl(longSide)
        Dim nw = Math.Max(1, CInt(Math.Round(Width * scale))), nh = Math.Max(1, CInt(Math.Round(Height * scale)))
        Return ResizeArea(nw, nh)
    End Function

    ''' <summary>面積平均縮放到指定尺寸（適合縮小；放大時等同最近鄰區塊）。</summary>
    Public Function ResizeArea(nw As Integer, nh As Integer) As RgbaImage
        Dim dst As New RgbaImage(nw, nh)
        Dim sx = Width / CDbl(nw), sy = Height / CDbl(nh)
        Dim src = Pixels, dp = dst.Pixels, w = Width
        Threading.Tasks.Parallel.For(0, nh,
            Sub(y)
                Dim y0 = y * sy, y1 = (y + 1) * sy
                Dim iy0 = CInt(Math.Floor(y0)), iy1 = Math.Min(Height - 1, CInt(Math.Ceiling(y1)) - 1)
                For x = 0 To nw - 1
                    Dim x0 = x * sx, x1 = (x + 1) * sx
                    Dim ix0 = CInt(Math.Floor(x0)), ix1 = Math.Min(w - 1, CInt(Math.Ceiling(x1)) - 1)
                    Dim r = 0.0, g = 0.0, b = 0.0, a = 0.0, wsum = 0.0
                    For yy = iy0 To iy1
                        Dim wy = Math.Min(yy + 1, y1) - Math.Max(yy, y0)
                        If wy <= 0 Then Continue For
                        Dim row = yy * w
                        For xx = ix0 To ix1
                            Dim wx = Math.Min(xx + 1, x1) - Math.Max(xx, x0)
                            If wx <= 0 Then Continue For
                            Dim wt = wx * wy
                            Dim i = (row + xx) * 4
                            Dim al = src(i + 3) * wt
                            r += src(i) * al : g += src(i + 1) * al : b += src(i + 2) * al
                            a += al : wsum += wt
                        Next
                    Next
                    Dim o = (y * nw + x) * 4
                    If a > 0 Then
                        dp(o) = ClampByte(r / a) : dp(o + 1) = ClampByte(g / a) : dp(o + 2) = ClampByte(b / a)
                    End If
                    dp(o + 3) = ClampByte(If(wsum > 0, a / wsum, 0))
                Next
            End Sub)
        Return dst
    End Function

    ''' <summary>0..255 的數值四捨五入並夾限成位元組。</summary>
    Public Shared Function ClampByte(v As Double) As Byte
        If Not (v > 0) Then Return 0
        If v >= 255 Then Return 255
        Return CByte(Math.Round(v))
    End Function
End Class

''' <summary>影像尺寸與記憶體需求檢查（大圖先算好需求，避免整數溢位或配置失敗才發現）。</summary>
Public NotInheritable Class ImageLimits
    Private Sub New()
    End Sub

    ''' <summary>單一緩衝區的最大元素數（.NET 陣列上限內，保守取值）。</summary>
    Public Const MaxElements As Long = 2_000_000_000L
    ''' <summary>單邊上限。</summary>
    Public Const MaxSide As Integer = 32768
    ''' <summary>
    ''' 預設的載入像素上限（6400 萬像素，RGBA 約 256 MB）。超過時解碼器應等比縮小載入，
    ''' 否則合成所需的多張全尺寸緩衝區會耗盡記憶體（例如 30000×23756 的博物館掃描檔光 RGBA 就要 2.85 GB）。
    ''' </summary>
    Public Const DefaultMaxLoadPixels As Long = 64_000_000L

    ''' <summary>超過 maxPixels 時的等比縮小尺寸（不超過時回傳原尺寸）。</summary>
    Public Shared Function FitPixels(width As Integer, height As Integer, maxPixels As Long) As (Width As Integer, Height As Integer)
        Dim n = CLng(width) * height
        If n <= maxPixels OrElse maxPixels <= 0 Then Return (width, height)
        Dim k = Math.Sqrt(maxPixels / CDbl(n))
        Return (Math.Max(1, CInt(Math.Floor(width * k))), Math.Max(1, CInt(Math.Floor(height * k))))
    End Function

    Public Shared Sub CheckSize(width As Integer, height As Integer, channels As Integer)
        If width <= 0 OrElse height <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(width), $"影像尺寸無效：{width}×{height}。")
        If width > MaxSide OrElse height > MaxSide Then Throw New ImageTooLargeException($"影像 {width}×{height} 超過單邊上限 {MaxSide}。")
        Dim n = CLng(width) * height * channels
        If n > MaxElements Then Throw New ImageTooLargeException($"影像 {width}×{height} 需要 {n:N0} 個元素，超過單一緩衝區上限。")
    End Sub

    ''' <summary>估計以指定尺寸合成時需要的位元組數（背景、輸出、圖層浮點區塊與遮罩）。</summary>
    Public Shared Function EstimateCompositeBytes(width As Integer, height As Integer, layerAreaPixels As Long) As Long
        Dim full = CLng(width) * height
        ' 背景 RGBA8 + 輸出 RGBA8 + 遮擋遮罩 float + 陰影遮罩 float + 圖層區塊（預乘 float RGBA）
        Return full * 4 + full * 4 + full * 4 + full * 4 + layerAreaPixels * 16
    End Function
End Class

Public Class ImageTooLargeException
    Inherits Exception
    Public Sub New(message As String)
        MyBase.New(message)
    End Sub
End Class
