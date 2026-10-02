Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>
''' 拖曳貼圖時的游標：用貼圖預覽圖做成游標（熱點在中心）。可以放下時完整顯示，
''' 不能放下時半透明並加上禁止符號。用完要 Dispose（會釋放 Win32 圖示）。
''' </summary>
Friend NotInheritable Class DragCursor
    Implements IDisposable

    <StructLayout(LayoutKind.Sequential)>
    Private Structure ICONINFO
        Public fIcon As Boolean
        Public xHotspot As Integer
        Public yHotspot As Integer
        Public hbmMask As IntPtr
        Public hbmColor As IntPtr
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function CreateIconIndirect(ByRef icon As ICONINFO) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function DestroyIcon(handle As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(handle As IntPtr) As Boolean
    End Function

    Public ReadOnly Property CanDrop As Cursor
    Public ReadOnly Property CannotDrop As Cursor
    Private ReadOnly _handles As New List(Of IntPtr)()

    Public Sub New(preview As Bitmap)
        CanDrop = MakeCursor(preview, allowed:=True)
        CannotDrop = MakeCursor(preview, allowed:=False)
    End Sub

    Private Function MakeCursor(preview As Bitmap, allowed As Boolean) As Cursor
        Dim size = Math.Max(preview.Width, preview.Height) + 8
        Using bmp As New Bitmap(size, size, PixelFormat.Format32bppArgb), g = Graphics.FromImage(bmp)
            g.Clear(Color.Transparent)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim x = (size - preview.Width) \ 2, y = (size - preview.Height) \ 2
            If allowed Then
                g.DrawImage(preview, x, y, preview.Width, preview.Height)
                ' 右下角小小的「+」表示複製到照片上。
                Dim r As New Rectangle(size - 22, size - 22, 18, 18)
                g.FillEllipse(Brushes.White, r)
                Using pen As New Pen(Color.FromArgb(40, 120, 220), 2.5F)
                    g.DrawEllipse(pen, r)
                    g.DrawLine(pen, r.X + 5, r.Y + 9, r.Right - 5, r.Y + 9)
                    g.DrawLine(pen, r.X + 9, r.Y + 5, r.X + 9, r.Bottom - 5)
                End Using
            Else
                ' 不能放下：變成淡灰色（不用半透明——游標點陣圖的半透明在部分系統會變黑）。
                Using ia As New ImageAttributes()
                    ia.SetColorMatrix(New ColorMatrix(New Single()() {
                        New Single() {0.2F, 0.2F, 0.2F, 0, 0}, New Single() {0.2F, 0.2F, 0.2F, 0, 0}, New Single() {0.2F, 0.2F, 0.2F, 0, 0},
                        New Single() {0, 0, 0, 1, 0}, New Single() {0.45F, 0.45F, 0.45F, 0, 1}}))
                    g.DrawImage(preview, New Rectangle(x, y, preview.Width, preview.Height), 0, 0, preview.Width, preview.Height, GraphicsUnit.Pixel, ia)
                End Using
                Dim r As New Rectangle(size - 22, size - 22, 18, 18)
                g.FillEllipse(Brushes.White, r)
                Using pen As New Pen(Color.FromArgb(220, 50, 50), 2.5F)
                    g.DrawEllipse(pen, r)
                    g.DrawLine(pen, r.X + 4, r.Bottom - 4, r.Right - 4, r.Y + 4)
                End Using
            End If

            ' 32 位元含 alpha 的彩色點陣圖決定外觀，遮罩全黑即可。
            Dim colorBits = bmp.GetHbitmap(Drawing.Color.FromArgb(0))
            Dim mask As IntPtr
            Using maskBmp As New Bitmap(size, size)
                mask = maskBmp.GetHbitmap(Drawing.Color.Black)
            End Using
            Try
                Dim info As New ICONINFO With {.fIcon = False, .xHotspot = size \ 2, .yHotspot = size \ 2, .hbmMask = mask, .hbmColor = colorBits}
                Dim icon = CreateIconIndirect(info)
                If icon = IntPtr.Zero Then Return Cursors.Default
                _handles.Add(icon)
                Return New Cursor(icon)
            Finally
                DeleteObject(colorBits)
                DeleteObject(mask)
            End Try
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each h In _handles
            DestroyIcon(h)
        Next
        _handles.Clear()
    End Sub
End Class
