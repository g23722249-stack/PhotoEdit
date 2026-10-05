Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>圖層混合模式（和 Photoshop 同名的可分離模式）。</summary>
Public Enum BlendMode
    Normal = 0
    Multiply = 1
    Screen = 2
    Overlay = 3
    Darken = 4
    Lighten = 5
    ColorDodge = 6
    ColorBurn = 7
    LinearDodge = 8
    LinearBurn = 9
    HardLight = 10
    SoftLight = 11
    Difference = 12
    Exclusion = 13
End Enum

''' <summary>
''' 把一個圖層（32bppArgb、未預乘）依不透明度與混合模式合成到底下的影像上。
''' 混合公式依 W3C Compositing and Blending：底下透明的地方照原色顯示，混合只發生在兩者重疊處。
''' </summary>
Public NotInheritable Class LayerBlend
    Private Sub New()
    End Sub

    ''' <summary>選單用的中文名稱（順序同 BlendMode）。</summary>
    Public Shared ReadOnly Names As String() = {
        "正常", "色彩增值", "濾色", "覆蓋", "變暗", "變亮", "加亮顏色", "加深顏色",
        "線性加亮（增加）", "線性加深", "實光", "柔光", "差異化", "排除"}

    Public Shared Function Name(mode As BlendMode) As String
        Dim i = CInt(mode)
        Return If(i >= 0 AndAlso i < Names.Length, Names(i), Names(0))
    End Function

    ''' <summary>
    ''' 把 src 整張以 1:1 畫到 dst 的 destRect（大小與 src 相同；超出 dst 的部分略過）。
    ''' 正常模式用 GDI+ 直接畫，其他模式逐像素混合。
    ''' </summary>
    Public Shared Sub Composite(dst As Bitmap, src As Bitmap, destRect As Rectangle, opacity As Single, mode As BlendMode)
        opacity = Math.Max(0, Math.Min(1, opacity))
        If opacity <= 0 Then Return
        If mode = BlendMode.Normal Then
            Using g = Graphics.FromImage(dst)
                If opacity >= 0.999F Then
                    g.DrawImage(src, destRect)
                Else
                    Using ia As New ImageAttributes()
                        ia.SetColorMatrix(New ColorMatrix With {.Matrix33 = opacity})
                        g.DrawImage(src, destRect, 0, 0, destRect.Width, destRect.Height, GraphicsUnit.Pixel, ia)
                    End Using
                End If
            End Using
            Return
        End If

        Dim clip = Rectangle.Intersect(destRect, New Rectangle(0, 0, dst.Width, dst.Height))
        clip = Rectangle.Intersect(clip, New Rectangle(destRect.X, destRect.Y, src.Width, src.Height))
        If clip.Width <= 0 OrElse clip.Height <= 0 Then Return
        Dim sx = clip.X - destRect.X, sy = clip.Y - destRect.Y
        ' 鎖整列（從 x = 0 開始）再自己算位移：部分 GDI+ 實作（libgdiplus）鎖不是從左邊開始的範圍時，寫回會不完整。
        ' 也先把圖層像素複製出來再鎖底圖，不同時鎖兩張圖。
        Dim rowBytes = clip.Width * 4
        Dim srcPx(rowBytes * clip.Height - 1) As Byte
        Dim sd = src.LockBits(New Rectangle(0, sy, src.Width, clip.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To clip.Height - 1
                Marshal.Copy(sd.Scan0 + y * sd.Stride + sx * 4, srcPx, y * rowBytes, rowBytes)
            Next
        Finally
            src.UnlockBits(sd)
        End Try
        Dim dd = dst.LockBits(New Rectangle(0, clip.Y, dst.Width, clip.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
        Try
            Dim d(rowBytes - 1) As Byte, s(rowBytes - 1) As Byte
            For y = 0 To clip.Height - 1
                Dim dp = dd.Scan0 + y * dd.Stride + clip.X * 4
                Marshal.Copy(dp, d, 0, d.Length)
                Buffer.BlockCopy(srcPx, y * rowBytes, s, 0, rowBytes)
                Dim changed = False
                For x = 0 To clip.Width - 1
                    Dim i = x * 4
                    Dim sa = s(i + 3) / 255.0 * opacity
                    If sa <= 0 Then Continue For
                    Dim ba = d(i + 3) / 255.0
                    Dim ao = sa + ba * (1 - sa)
                    For c = 0 To 2
                        Dim cs = s(i + c) / 255.0, cb = d(i + c) / 255.0
                        Dim mixed = (1 - ba) * cs + ba * Mix(mode, cb, cs)
                        Dim co = sa * mixed + ba * (1 - sa) * cb
                        d(i + c) = ToByte(co / ao)
                    Next
                    d(i + 3) = ToByte(ao)
                    changed = True
                Next
                If changed Then Marshal.Copy(d, 0, dp, d.Length)
            Next
        Finally
            dst.UnlockBits(dd)
        End Try
    End Sub

    Private Shared Function ToByte(v As Double) As Byte
        Return CByte(Math.Max(0, Math.Min(255, Math.Round(v * 255))))
    End Function

    ''' <summary>單一色版的混合結果（cb = 底下，cs = 圖層，0..1）。</summary>
    Public Shared Function Mix(mode As BlendMode, cb As Double, cs As Double) As Double
        Select Case mode
            Case BlendMode.Multiply : Return cb * cs
            Case BlendMode.Screen : Return cb + cs - cb * cs
            Case BlendMode.Overlay : Return HardLight(cs, cb) ' 覆蓋 = 實光把兩層對調
            Case BlendMode.Darken : Return Math.Min(cb, cs)
            Case BlendMode.Lighten : Return Math.Max(cb, cs)
            Case BlendMode.ColorDodge
                If cb <= 0 Then Return 0
                If cs >= 1 Then Return 1
                Return Math.Min(1, cb / (1 - cs))
            Case BlendMode.ColorBurn
                If cb >= 1 Then Return 1
                If cs <= 0 Then Return 0
                Return 1 - Math.Min(1, (1 - cb) / cs)
            Case BlendMode.LinearDodge : Return Math.Min(1, cb + cs)
            Case BlendMode.LinearBurn : Return Math.Max(0, cb + cs - 1)
            Case BlendMode.HardLight : Return HardLight(cb, cs)
            Case BlendMode.SoftLight
                If cs <= 0.5 Then Return cb - (1 - 2 * cs) * cb * (1 - cb)
                Dim dcb = If(cb <= 0.25, ((16 * cb - 12) * cb + 4) * cb, Math.Sqrt(cb))
                Return cb + (2 * cs - 1) * (dcb - cb)
            Case BlendMode.Difference : Return Math.Abs(cb - cs)
            Case BlendMode.Exclusion : Return cb + cs - 2 * cb * cs
            Case Else : Return cs
        End Select
    End Function

    Private Shared Function HardLight(cb As Double, cs As Double) As Double
        If cs <= 0.5 Then Return cb * 2 * cs
        Dim t = 2 * cs - 1
        Return cb + t - cb * t
    End Function
End Class
