Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>
''' 魔術棒游標（程式畫的 32 × 32）：斜放的棒子、頂端一顆四芒星，熱點在星星中心；
''' 右下角的小圓標示「去除（−）」或「補回（＋）」。游標建立一次重複使用。
''' </summary>
Friend NotInheritable Class WandCursor
    Private Sub New()
    End Sub

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

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(handle As IntPtr) As Boolean
    End Function

    Private Const Size As Integer = 32
    Private Const HotX As Integer = 7, HotY As Integer = 7

    Private Shared _remove As Cursor
    Private Shared _restore As Cursor

    ''' <summary>去除模式的游標。</summary>
    Public Shared ReadOnly Property Remove As Cursor
        Get
            If _remove Is Nothing Then _remove = Create(restore:=False)
            Return _remove
        End Get
    End Property

    ''' <summary>補回模式的游標。</summary>
    Public Shared ReadOnly Property Restore As Cursor
        Get
            If _restore Is Nothing Then _restore = Create(restore:=True)
            Return _restore
        End Get
    End Property

    ''' <summary>游標的圖（說明視窗與測試也用）。</summary>
    Public Shared Function Picture(restore As Boolean) As Bitmap
        Dim bmp As New Bitmap(Size, Size, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.Transparent)
            g.SmoothingMode = SmoothingMode.AntiAlias
            ' 棒子：黑色外框、深色棒身、靠星星那端有一段白色。
            Using outline As New Pen(Color.Black, 5.5F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round},
                  body As New Pen(Color.FromArgb(60, 45, 90), 3.2F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round},
                  tip As New Pen(Color.White, 3.2F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Flat}
                g.DrawLine(outline, 11, 11, 25, 25)
                g.DrawLine(body, 11, 11, 25, 25)
                g.DrawLine(tip, 11, 11, 15, 15)
            End Using
            ' 星星：四芒星，熱點在中心。
            Dim star = Enumerable.Range(0, 8).Select(Function(i)
                                                         Dim r = If(i Mod 2 = 0, 6.8F, 2.1F)
                                                         Dim a = -Math.PI / 2 + i * Math.PI / 4
                                                         Return New PointF(HotX + CSng(r * Math.Cos(a)), HotY + CSng(r * Math.Sin(a)))
                                                     End Function).ToArray()
            Using fill As New SolidBrush(Color.FromArgb(255, 215, 40)), pen As New Pen(Color.Black, 1.2F)
                g.FillPolygon(fill, star)
                g.DrawPolygon(pen, star)
            End Using
            ' 小亮點
            Using sp As New SolidBrush(Color.FromArgb(255, 235, 120)), pen As New Pen(Color.Black, 0.8F)
                g.FillEllipse(sp, 15, 2, 4, 4) : g.DrawEllipse(pen, 15, 2, 4, 4)
                g.FillEllipse(sp, 2, 15, 3, 3) : g.DrawEllipse(pen, 2, 15, 3, 3)
            End Using
            ' 模式標記
            Dim badge As New Rectangle(19, 19, 12, 12)
            Using bg As New SolidBrush(If(restore, Color.FromArgb(40, 170, 80), Color.FromArgb(220, 60, 60))), pen As New Pen(Color.White, 1.8F)
                g.FillEllipse(bg, badge)
                g.DrawEllipse(Pens.White, badge)
                g.DrawLine(pen, 22, 25, 28, 25)
                If restore Then g.DrawLine(pen, 25, 22, 25, 28)
            End Using
        End Using
        Return bmp
    End Function

    Private Shared Function Create(restore As Boolean) As Cursor
        Using bmp = Picture(restore)
            Dim colorBits = bmp.GetHbitmap(Color.FromArgb(0))
            Dim mask As IntPtr
            Using maskBmp As New Bitmap(Size, Size)
                mask = maskBmp.GetHbitmap(Color.Black)
            End Using
            Try
                Dim info As New ICONINFO With {.fIcon = False, .xHotspot = HotX, .yHotspot = HotY, .hbmMask = mask, .hbmColor = colorBits}
                Dim icon = CreateIconIndirect(info)
                Return If(icon = IntPtr.Zero, Cursors.Cross, New Cursor(icon))
            Finally
                DeleteObject(colorBits)
                DeleteObject(mask)
            End Try
        End Using
    End Function
End Class
