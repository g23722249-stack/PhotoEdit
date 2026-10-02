Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks

''' <summary>
''' 透視校正（梯形修正）。分兩次一維取樣：先逐列左右縮放（垂直透視），再逐欄上下縮放（水平透視）。
''' 取樣範圍一律在原圖內（縮放係數 ≤ 1），所以四周不會出現空白，也不必另外裁切。
''' 座標皆為 0..1 比例。
''' </summary>
Public NotInheritable Class Perspective
    Private Sub New()
    End Sub

    ''' <summary>滑桿 100 時，被放大的那一邊取樣寬度縮成 1 - MaxZoom。</summary>
    Public Const MaxZoom As Double = 0.35

    ''' <summary>垂直透視：第 t 列（0 = 上緣）的取樣寬度比例。</summary>
    Public Shared Function RowFactor(vertical As Integer, t As Double) As Double
        If vertical = 0 Then Return 1
        Dim a = Math.Min(100, Math.Abs(vertical)) / 100.0 * MaxZoom
        Return If(vertical > 0, 1 - a * (1 - t), 1 - a * t)
    End Function

    ''' <summary>水平透視：第 u 欄（0 = 左緣）的取樣高度比例。</summary>
    Public Shared Function ColumnFactor(horizontal As Integer, u As Double) As Double
        If horizontal = 0 Then Return 1
        Dim a = Math.Min(100, Math.Abs(horizontal)) / 100.0 * MaxZoom
        Return If(horizontal > 0, 1 - a * (1 - u), 1 - a * u)
    End Function

    ''' <summary>輸出點 → 原圖點（取樣方向）。</summary>
    Public Shared Function Unwarp(p As PointF, vertical As Integer, horizontal As Integer) As PointF
        Dim yMid = 0.5 + (p.Y - 0.5) * ColumnFactor(horizontal, p.X)
        Dim x = 0.5 + (p.X - 0.5) * RowFactor(vertical, yMid)
        Return New PointF(CSng(x), CSng(yMid))
    End Function

    ''' <summary>原圖點 → 輸出點（Unwarp 的反函數）。</summary>
    Public Shared Function Warp(p As PointF, vertical As Integer, horizontal As Integer) As PointF
        Dim xMid = 0.5 + (p.X - 0.5) / RowFactor(vertical, p.Y)
        Dim y = 0.5 + (p.Y - 0.5) / ColumnFactor(horizontal, xMid)
        Return New PointF(CSng(xMid), CSng(y))
    End Function

    ''' <summary>回傳校正後的新圖（尺寸不變）；兩個值都是 0 時回傳 Nothing。</summary>
    Public Shared Function Apply(bmp As Bitmap, vertical As Integer, horizontal As Integer) As Bitmap
        If vertical = 0 AndAlso horizontal = 0 Then Return Nothing
        Dim w = bmp.Width, h = bmp.Height
        Dim src = ReadPixels(bmp)
        Dim stride = w * 4
        Dim midPx(src.Length - 1) As Byte

        ' 第一次：逐列左右取樣。
        Parallel.For(0, h,
            Sub(y)
                Dim f = RowFactor(vertical, (y + 0.5) / h)
                Dim row = y * stride
                For x = 0 To w - 1
                    Dim sx = (0.5 + ((x + 0.5) / w - 0.5) * f) * w - 0.5
                    Dim x0 = CInt(Math.Floor(sx)), t = sx - x0
                    Dim xa = Math.Max(0, Math.Min(w - 1, x0)), xb = Math.Max(0, Math.Min(w - 1, x0 + 1))
                    For ch = 0 To 3
                        midPx(row + x * 4 + ch) = ImagePipeline.ClampByte(src(row + xa * 4 + ch) * (1 - t) + src(row + xb * 4 + ch) * t)
                    Next
                Next
            End Sub)

        ' 第二次：逐欄上下取樣。
        Dim dst(src.Length - 1) As Byte
        Parallel.For(0, w,
            Sub(x)
                Dim g = ColumnFactor(horizontal, (x + 0.5) / w)
                For y = 0 To h - 1
                    Dim sy = (0.5 + ((y + 0.5) / h - 0.5) * g) * h - 0.5
                    Dim y0 = CInt(Math.Floor(sy)), t = sy - y0
                    Dim ya = Math.Max(0, Math.Min(h - 1, y0)), yb = Math.Max(0, Math.Min(h - 1, y0 + 1))
                    For ch = 0 To 3
                        dst(y * stride + x * 4 + ch) = ImagePipeline.ClampByte(midPx(ya * stride + x * 4 + ch) * (1 - t) + midPx(yb * stride + x * 4 + ch) * t)
                    Next
                Next
            End Sub)

        Dim result As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        WritePixels(result, dst)
        Return result
    End Function

    ''' <summary>讀成緊密排列（stride = 寬 × 4）的 BGRA 陣列。</summary>
    Friend Shared Function ReadPixels(bmp As Bitmap) As Byte()
        Dim w = bmp.Width, h = bmp.Height
        Dim data = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            Dim px(w * 4 * h - 1) As Byte
            For y = 0 To h - 1
                Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w * 4, w * 4)
            Next
            Return px
        Finally
            bmp.UnlockBits(data)
        End Try
    End Function

    Friend Shared Sub WritePixels(bmp As Bitmap, px As Byte())
        Dim w = bmp.Width, h = bmp.Height
        Dim data = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To h - 1
                Marshal.Copy(px, y * w * 4, data.Scan0 + y * data.Stride, w * 4)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
    End Sub
End Class
