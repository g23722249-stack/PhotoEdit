Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>核心影像與 GDI+ Bitmap 之間的轉換（只用於畫面顯示，不參與合成計算）。</summary>
Friend NotInheritable Class BitmapConvert
    Private Sub New()
    End Sub

    ''' <summary>RGBA → 32bpp ARGB Bitmap（GDI+ 記憶體順序 BGRA）。</summary>
    Public Shared Function ToBitmap(img As AutoComposite.RgbaImage) As Bitmap
        Dim bmp As New Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb)
        Dim data = bmp.LockBits(New Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            Dim row(img.Width * 4 - 1) As Byte
            Dim p = img.Pixels
            For y = 0 To img.Height - 1
                Dim o = y * img.Width * 4
                For x = 0 To img.Width - 1
                    Dim i = o + x * 4
                    row(x * 4) = p(i + 2) : row(x * 4 + 1) = p(i + 1) : row(x * 4 + 2) = p(i) : row(x * 4 + 3) = p(i + 3)
                Next
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
        Return bmp
    End Function

    ''' <summary>遮罩 → 半透明著色疊圖（顯示遮擋、保護、陰影接收遮罩用）。</summary>
    Public Shared Function MaskOverlay(mask As AutoComposite.FloatMask, color As Color, maxSide As Integer) As Bitmap
        Dim m = mask
        Dim longSide = Math.Max(m.Width, m.Height)
        If longSide > maxSide Then
            Dim k = maxSide / CDbl(longSide)
            m = m.Resize(Math.Max(1, CInt(m.Width * k)), Math.Max(1, CInt(m.Height * k)))
        End If
        Dim bmp As New Bitmap(m.Width, m.Height, PixelFormat.Format32bppArgb)
        Dim data = bmp.LockBits(New Rectangle(0, 0, m.Width, m.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            Dim row(m.Width * 4 - 1) As Byte
            For y = 0 To m.Height - 1
                For x = 0 To m.Width - 1
                    Dim v = m.Data(y * m.Width + x)
                    row(x * 4) = color.B : row(x * 4 + 1) = color.G : row(x * 4 + 2) = color.R
                    row(x * 4 + 3) = CByte(Math.Clamp(v, 0.0F, 1.0F) * color.A)
                Next
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
        Return bmp
    End Function
End Class
