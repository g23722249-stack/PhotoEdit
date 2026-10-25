''' <summary>平面上的點（雙精度，背景或人物圖的像素座標）。</summary>
Public Structure PointD
    Public X As Double
    Public Y As Double

    Public Sub New(x As Double, y As Double)
        Me.X = x : Me.Y = y
    End Sub

    Public Shared Operator +(a As PointD, b As PointD) As PointD
        Return New PointD(a.X + b.X, a.Y + b.Y)
    End Operator

    Public Shared Operator -(a As PointD, b As PointD) As PointD
        Return New PointD(a.X - b.X, a.Y - b.Y)
    End Operator

    Public Shared Operator *(a As PointD, k As Double) As PointD
        Return New PointD(a.X * k, a.Y * k)
    End Operator

    Public Function Length() As Double
        Return Math.Sqrt(X * X + Y * Y)
    End Function

    Public Function DistanceTo(o As PointD) As Double
        Return (Me - o).Length()
    End Function

    Public Overrides Function ToString() As String
        Return $"({X:0.##}, {Y:0.##})"
    End Function
End Structure

''' <summary>
''' 3×3 齊次變換矩陣（列主序，點為行向量：p' = M·p）。仿射是最後一列為 (0,0,1) 的特例，
''' 透視（Homography）用完整 3×3。所有圖層變換最後都組成一個矩陣，從原圖一次重採樣，避免反覆縮放劣化。
''' </summary>
Public Structure Matrix3
    Public M11, M12, M13 As Double
    Public M21, M22, M23 As Double
    Public M31, M32, M33 As Double

    Public Shared ReadOnly Property Identity As Matrix3
        Get
            Return New Matrix3 With {.M11 = 1, .M22 = 1, .M33 = 1}
        End Get
    End Property

    Public Shared Function Translation(tx As Double, ty As Double) As Matrix3
        Return New Matrix3 With {.M11 = 1, .M13 = tx, .M22 = 1, .M23 = ty, .M33 = 1}
    End Function

    Public Shared Function Scaling(sx As Double, sy As Double) As Matrix3
        Return New Matrix3 With {.M11 = sx, .M22 = sy, .M33 = 1}
    End Function

    ''' <summary>順時針為正（影像座標 y 向下）。</summary>
    Public Shared Function Rotation(degrees As Double) As Matrix3
        Dim r = degrees * Math.PI / 180
        Dim c = Math.Cos(r), s = Math.Sin(r)
        Return New Matrix3 With {.M11 = c, .M12 = -s, .M21 = s, .M22 = c, .M33 = 1}
    End Function

    Public Shared Operator *(a As Matrix3, b As Matrix3) As Matrix3
        Return New Matrix3 With {
            .M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
            .M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
            .M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,
            .M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
            .M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
            .M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,
            .M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
            .M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
            .M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33}
    End Operator

    Public Function Transform(p As PointD) As PointD
        Dim w = M31 * p.X + M32 * p.Y + M33
        If Math.Abs(w) < 0.000000000001 Then w = If(w < 0, -0.000000000001, 0.000000000001)
        Return New PointD((M11 * p.X + M12 * p.Y + M13) / w, (M21 * p.X + M22 * p.Y + M23) / w)
    End Function

    Public ReadOnly Property IsAffine As Boolean
        Get
            Return Math.Abs(M31) < 0.000000000001 AndAlso Math.Abs(M32) < 0.000000000001 AndAlso Math.Abs(M33 - 1) < 0.000000000001
        End Get
    End Property

    Public Function Determinant() As Double
        Return M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31) + M13 * (M21 * M32 - M22 * M31)
    End Function

    Public Function Inverse() As Matrix3
        Dim d = Determinant()
        If Math.Abs(d) < 0.000000000001 Then Throw New InvalidOperationException("變換矩陣不可逆（縮放為 0 或透視退化）。")
        Dim inv = 1 / d
        Return New Matrix3 With {
            .M11 = (M22 * M33 - M23 * M32) * inv, .M12 = (M13 * M32 - M12 * M33) * inv, .M13 = (M12 * M23 - M13 * M22) * inv,
            .M21 = (M23 * M31 - M21 * M33) * inv, .M22 = (M11 * M33 - M13 * M31) * inv, .M23 = (M13 * M21 - M11 * M23) * inv,
            .M31 = (M21 * M32 - M22 * M31) * inv, .M32 = (M12 * M31 - M11 * M32) * inv, .M33 = (M11 * M22 - M12 * M21) * inv}
    End Function

    ''' <summary>
    ''' 由四組對應點求透視矩陣（src(i) → dst(i)），以 8×8 線性方程組（DLT，h33 = 1）求解。
    ''' 四點中有三點共線時無解，丟出例外。
    ''' </summary>
    Public Shared Function FromQuad(src As PointD(), dst As PointD()) As Matrix3
        If src.Length <> 4 OrElse dst.Length <> 4 Then Throw New ArgumentException("透視變換需要剛好四組控制點。")
        Dim a(7, 8) As Double
        For i = 0 To 3
            Dim x = src(i).X, y = src(i).Y, u = dst(i).X, v = dst(i).Y
            Dim r = i * 2
            a(r, 0) = x : a(r, 1) = y : a(r, 2) = 1 : a(r, 6) = -u * x : a(r, 7) = -u * y : a(r, 8) = u
            a(r + 1, 3) = x : a(r + 1, 4) = y : a(r + 1, 5) = 1 : a(r + 1, 6) = -v * x : a(r + 1, 7) = -v * y : a(r + 1, 8) = v
        Next
        ' 部分主元高斯消去法。
        For col = 0 To 7
            Dim piv = col
            For r = col + 1 To 7
                If Math.Abs(a(r, col)) > Math.Abs(a(piv, col)) Then piv = r
            Next
            If Math.Abs(a(piv, col)) < 0.000000001 Then Throw New InvalidOperationException("透視控制點退化（有三點共線或重疊）。")
            If piv <> col Then
                For k = 0 To 8
                    Dim t = a(col, k) : a(col, k) = a(piv, k) : a(piv, k) = t
                Next
            End If
            For r = 0 To 7
                If r = col Then Continue For
                Dim f = a(r, col) / a(col, col)
                If f = 0 Then Continue For
                For k = col To 8
                    a(r, k) -= f * a(col, k)
                Next
            Next
        Next
        Dim h(7) As Double
        For i = 0 To 7
            h(i) = a(i, 8) / a(i, i)
        Next
        Return New Matrix3 With {.M11 = h(0), .M12 = h(1), .M13 = h(2), .M21 = h(3), .M22 = h(4), .M23 = h(5), .M31 = h(6), .M32 = h(7), .M33 = 1}
    End Function

    ''' <summary>近似的局部等比縮放倍率（2×2 部分的行列式開根號，透視時以 (x,y) 處的雅可比估計）。</summary>
    Public Function LocalScale(p As PointD) As Double
        Const e = 0.5
        Dim a = Transform(New PointD(p.X - e, p.Y)), b = Transform(New PointD(p.X + e, p.Y))
        Dim c = Transform(New PointD(p.X, p.Y - e)), d = Transform(New PointD(p.X, p.Y + e))
        Dim jx = b - a, jy = d - c
        Return Math.Sqrt(Math.Abs(jx.X * jy.Y - jx.Y * jy.X))
    End Function

    Public Function ToArray() As Double()
        Return {M11, M12, M13, M21, M22, M23, M31, M32, M33}
    End Function

    Public Shared Function FromArray(v As Double()) As Matrix3
        If v Is Nothing OrElse v.Length <> 9 Then Return Identity
        Return New Matrix3 With {.M11 = v(0), .M12 = v(1), .M13 = v(2), .M21 = v(3), .M22 = v(4), .M23 = v(5), .M31 = v(6), .M32 = v(7), .M33 = v(8)}
    End Function
End Structure
