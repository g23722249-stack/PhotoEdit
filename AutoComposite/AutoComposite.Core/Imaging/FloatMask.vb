''' <summary>
''' 單通道浮點遮罩（0 = 透明／不影響，1 = 完全保留／完全影響）。
''' 去背 Alpha、遮擋、陰影都用它，到輸出前才量化成 8 位元，不提早二值化。
''' </summary>
Public NotInheritable Class FloatMask
    Public ReadOnly Property Width As Integer
    Public ReadOnly Property Height As Integer
    Public ReadOnly Property Data As Single()

    Public Sub New(width As Integer, height As Integer)
        ImageLimits.CheckSize(width, height, 1)
        Me.Width = width
        Me.Height = height
        Data = New Single(width * height - 1) {}
    End Sub

    Public Sub New(width As Integer, height As Integer, data As Single())
        ImageLimits.CheckSize(width, height, 1)
        If data.Length <> width * height Then Throw New ArgumentException("遮罩資料長度與尺寸不符。", NameOf(data))
        Me.Width = width
        Me.Height = height
        Me.Data = data
    End Sub

    Default Public Property Item(x As Integer, y As Integer) As Single
        Get
            Return Data(y * Width + x)
        End Get
        Set(value As Single)
            Data(y * Width + x) = value
        End Set
    End Property

    Public Function Clone() As FloatMask
        Return New FloatMask(Width, Height, CType(Data.Clone(), Single()))
    End Function

    Public Shared Function Filled(width As Integer, height As Integer, value As Single) As FloatMask
        Dim m As New FloatMask(width, height)
        Array.Fill(m.Data, value)
        Return m
    End Function

    ''' <summary>雙線性取樣（座標為像素中心系統：像素 (0,0) 的中心在 0.5,0.5）；超出範圍回傳 0。</summary>
    Public Function SampleBilinear(x As Double, y As Double) As Single
        Dim fx = x - 0.5, fy = y - 0.5
        Dim x0 = CInt(Math.Floor(fx)), y0 = CInt(Math.Floor(fy))
        Dim tx = CSng(fx - x0), ty = CSng(fy - y0)
        Dim v00 = At(x0, y0), v10 = At(x0 + 1, y0), v01 = At(x0, y0 + 1), v11 = At(x0 + 1, y0 + 1)
        Return (v00 * (1 - tx) + v10 * tx) * (1 - ty) + (v01 * (1 - tx) + v11 * tx) * ty
    End Function

    ''' <summary>邊界外延（clamp）取值。</summary>
    Public Function AtClamped(x As Integer, y As Integer) As Single
        x = Math.Clamp(x, 0, Width - 1) : y = Math.Clamp(y, 0, Height - 1)
        Return Data(y * Width + x)
    End Function

    Private Function At(x As Integer, y As Integer) As Single
        If x < 0 OrElse y < 0 OrElse x >= Width OrElse y >= Height Then Return 0
        Return Data(y * Width + x)
    End Function

    ''' <summary>雙線性縮放到新尺寸（遮罩插值；縮小超過一半時先面積平均以免鋸齒）。</summary>
    Public Function Resize(nw As Integer, nh As Integer) As FloatMask
        If nw = Width AndAlso nh = Height Then Return Clone()
        Dim src = Me
        ' 大幅縮小時逐次減半（面積平均），再雙線性到目標。
        While src.Width >= nw * 2 AndAlso src.Height >= nh * 2
            src = src.HalveArea()
        End While
        Dim dst As New FloatMask(nw, nh)
        Dim sx = src.Width / CDbl(nw), sy = src.Height / CDbl(nh)
        Dim s = src
        Threading.Tasks.Parallel.For(0, nh,
            Sub(y)
                Dim yy = (y + 0.5) * sy
                For x = 0 To nw - 1
                    dst.Data(y * nw + x) = s.SampleBilinearClamped((x + 0.5) * sx, yy)
                Next
            End Sub)
        Return dst
    End Function

    Private Function SampleBilinearClamped(x As Double, y As Double) As Single
        Dim fx = x - 0.5, fy = y - 0.5
        Dim x0 = CInt(Math.Floor(fx)), y0 = CInt(Math.Floor(fy))
        Dim tx = CSng(fx - x0), ty = CSng(fy - y0)
        Dim v00 = AtClamped(x0, y0), v10 = AtClamped(x0 + 1, y0), v01 = AtClamped(x0, y0 + 1), v11 = AtClamped(x0 + 1, y0 + 1)
        Return (v00 * (1 - tx) + v10 * tx) * (1 - ty) + (v01 * (1 - tx) + v11 * tx) * ty
    End Function

    Private Function HalveArea() As FloatMask
        Dim nw = Math.Max(1, Width \ 2), nh = Math.Max(1, Height \ 2)
        Dim dst As New FloatMask(nw, nh)
        For y = 0 To nh - 1
            For x = 0 To nw - 1
                dst.Data(y * nw + x) = (AtClamped(2 * x, 2 * y) + AtClamped(2 * x + 1, 2 * y) + AtClamped(2 * x, 2 * y + 1) + AtClamped(2 * x + 1, 2 * y + 1)) * 0.25F
            Next
        Next
        Return dst
    End Function

    ''' <summary>大於門檻的像素外框（找不到時回傳 Nothing）。</summary>
    Public Function BoundsAbove(threshold As Single) As PixelRect?
        Dim minX = Width, minY = Height, maxX = -1, maxY = -1
        For y = 0 To Height - 1
            Dim row = y * Width
            For x = 0 To Width - 1
                If Data(row + x) > threshold Then
                    If x < minX Then minX = x
                    If x > maxX Then maxX = x
                    If y < minY Then minY = y
                    maxY = y
                End If
            Next
        Next
        If maxX < 0 Then Return Nothing
        Return New PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1)
    End Function

    Public Function Sum() As Double
        Dim s = 0.0
        For Each v In Data
            s += v
        Next
        Return s
    End Function

    Public Sub Clamp01()
        Dim d = Data
        For i = 0 To d.Length - 1
            Dim v = d(i)
            If Not (v > 0) Then
                d(i) = 0
            ElseIf v > 1 Then
                d(i) = 1
            End If
        Next
    End Sub
End Class

''' <summary>整數像素矩形。</summary>
Public Structure PixelRect
    Public X As Integer
    Public Y As Integer
    Public Width As Integer
    Public Height As Integer

    Public Sub New(x As Integer, y As Integer, width As Integer, height As Integer)
        Me.X = x : Me.Y = y : Me.Width = width : Me.Height = height
    End Sub

    Public ReadOnly Property Right As Integer
        Get
            Return X + Width
        End Get
    End Property

    Public ReadOnly Property Bottom As Integer
        Get
            Return Y + Height
        End Get
    End Property

    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return Width <= 0 OrElse Height <= 0
        End Get
    End Property

    Public Function Intersect(other As PixelRect) As PixelRect
        Dim x0 = Math.Max(X, other.X), y0 = Math.Max(Y, other.Y)
        Dim x1 = Math.Min(Right, other.Right), y1 = Math.Min(Bottom, other.Bottom)
        If x1 <= x0 OrElse y1 <= y0 Then Return New PixelRect(x0, y0, 0, 0)
        Return New PixelRect(x0, y0, x1 - x0, y1 - y0)
    End Function

    Public Function Inflate(d As Integer) As PixelRect
        Return New PixelRect(X - d, Y - d, Width + 2 * d, Height + 2 * d)
    End Function

    Public Overrides Function ToString() As String
        Return $"({X},{Y}) {Width}×{Height}"
    End Function
End Structure
