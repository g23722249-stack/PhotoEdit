Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>RGB 與亮度直方圖，各 256 格。</summary>
Public Class Histogram
    Public ReadOnly Red(255) As Integer
    Public ReadOnly Green(255) As Integer
    Public ReadOnly Blue(255) As Integer
    Public ReadOnly Luminance(255) As Integer
    Public Property PixelCount As Integer

    ''' <summary>大圖只取樣（約 25 萬點），直方圖形狀不變但快很多。</summary>
    Public Shared Function Compute(bmp As Bitmap) As Histogram
        Dim hist As New Histogram()
        Dim w = bmp.Width, h = bmp.Height
        Dim stepPx = Math.Max(1, CInt(Math.Sqrt(CDbl(w) * h / 250000)))
        Dim data = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            Dim stride = data.Stride
            Dim px(stride * h - 1) As Byte
            Marshal.Copy(data.Scan0, px, 0, px.Length)
            For y = 0 To h - 1 Step stepPx
                For x = 0 To w - 1 Step stepPx
                    Dim i = y * stride + x * 4
                    Dim b = px(i), g = px(i + 1), r = px(i + 2)
                    hist.Blue(b) += 1
                    hist.Green(g) += 1
                    hist.Red(r) += 1
                    hist.Luminance(CInt((299 * r + 587 * g + 114 * b) \ 1000)) += 1
                    hist.PixelCount += 1
                Next
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
        Return hist
    End Function

    ''' <summary>亮度的百分位數（0..1），例如 Percentile(0.01) 為最暗 1% 的位置。</summary>
    Public Function Percentile(fraction As Double) As Double
        If PixelCount = 0 Then Return 0
        Dim target = fraction * PixelCount
        Dim acc = 0L
        For v = 0 To 255
            acc += Luminance(v)
            If acc >= target Then Return v / 255.0
        Next
        Return 1
    End Function

    Public Function MeanLuminance() As Double
        If PixelCount = 0 Then Return 0
        Dim sum = 0L
        For v = 0 To 255
            sum += CLng(v) * Luminance(v)
        Next
        Return sum / CDbl(PixelCount) / 255.0
    End Function

    Public Function MeanRgb() As (R As Double, G As Double, B As Double)
        If PixelCount = 0 Then Return (0, 0, 0)
        Dim r = 0L, g = 0L, b = 0L
        For v = 0 To 255
            r += CLng(v) * Red(v)
            g += CLng(v) * Green(v)
            b += CLng(v) * Blue(v)
        Next
        Return (r / CDbl(PixelCount), g / CDbl(PixelCount), b / CDbl(PixelCount))
    End Function
End Class
