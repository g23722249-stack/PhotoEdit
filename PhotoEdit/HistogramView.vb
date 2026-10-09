Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 直方圖：RGB 三色半透明疊合，亮度以白線表示；縱軸取平方根讓暗淡區域也看得到。
''' 上方有標題列，點一下收起／展開（收起時只剩標題列，右邊畫一條小小的亮度曲線）。
''' </summary>
Friend Class HistogramView
    Inherits Control

    ''' <summary>標題列高度；收起時整個控制項就是這麼高。</summary>
    Public Const HeaderHeight As Integer = 22
    ''' <summary>展開時圖表的高度（不含標題列）。</summary>
    Public Const GraphHeight As Integer = 84

    Private _histogram As Histogram
    Private _collapsed As Boolean = True
    Private _hotHeader As Boolean

    Public Event CollapsedChanged As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        BackColor = Color.FromArgb(30, 30, 32)
        Font = New Font("Microsoft JhengHei UI", 9.0F)
    End Sub

    Public Property Histogram As Histogram
        Get
            Return _histogram
        End Get
        Set(value As Histogram)
            _histogram = value
            Invalidate()
        End Set
    End Property

    ''' <summary>收起時只顯示標題列。</summary>
    Public Property Collapsed As Boolean
        Get
            Return _collapsed
        End Get
        Set(value As Boolean)
            If _collapsed = value Then Return
            _collapsed = value
            Invalidate()
            RaiseEvent CollapsedChanged(Me, EventArgs.Empty)
        End Set
    End Property

    ''' <summary>目前狀態需要的高度（標題列＋展開時的圖表）。</summary>
    Public ReadOnly Property PreferredHeight As Integer
        Get
            Return HeaderHeight + If(_collapsed, 0, GraphHeight)
        End Get
    End Property

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim hot = e.Y < HeaderHeight
        If hot <> _hotHeader Then
            _hotHeader = hot
            Cursor = If(hot, Cursors.Hand, Cursors.Default)
            Invalidate(New Rectangle(0, 0, Width, HeaderHeight))
        End If
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hotHeader Then _hotHeader = False : Invalidate(New Rectangle(0, 0, Width, HeaderHeight))
    End Sub

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        MyBase.OnMouseClick(e)
        If e.Button = MouseButtons.Left AndAlso (e.Y < HeaderHeight OrElse _collapsed) Then Collapsed = Not _collapsed
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        ' 標題列：▸／▾ 直方圖
        Dim head As New Rectangle(0, 0, Width, HeaderHeight)
        Using b As New SolidBrush(If(_hotHeader, Color.FromArgb(52, 54, 60), Color.FromArgb(40, 41, 45)))
            g.FillRectangle(b, head)
        End Using
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim fore = Color.FromArgb(215, 218, 224)
        Using b As New SolidBrush(fore)
            Dim cx = 11.0F, cy = HeaderHeight / 2.0F
            If _collapsed Then
                g.FillPolygon(b, {New PointF(cx - 3, cy - 5), New PointF(cx + 3, cy), New PointF(cx - 3, cy + 5)})
            Else
                g.FillPolygon(b, {New PointF(cx - 5, cy - 3), New PointF(cx + 5, cy - 3), New PointF(cx, cy + 3)})
            End If
        End Using
        TextRenderer.DrawText(g, "直方圖", Font, New Rectangle(20, 0, 80, HeaderHeight), fore, TextFormatFlags.VerticalCenter Or TextFormatFlags.Left)
        If _histogram Is Nothing OrElse _histogram.PixelCount = 0 Then Return
        Dim pk = PeakValue()
        If _collapsed Then
            ' 收起時：標題列右邊一條小亮度曲線
            Dim area As New RectangleF(84, 3, Math.Max(10, Width - 92), HeaderHeight - 5)
            Using pen As New Pen(Color.FromArgb(170, 235, 235, 235), 1)
                g.DrawLines(pen, ChannelPoints(_histogram.Luminance, pk, area))
            End Using
            Return
        End If
        Dim graph As New RectangleF(0, HeaderHeight, Width, Height - HeaderHeight)
        g.CompositingMode = CompositingMode.SourceOver
        FillChannel(g, _histogram.Red, Color.FromArgb(110, 255, 70, 70), pk, graph)
        FillChannel(g, _histogram.Green, Color.FromArgb(110, 70, 220, 70), pk, graph)
        FillChannel(g, _histogram.Blue, Color.FromArgb(110, 80, 120, 255), pk, graph)
        Using pen As New Pen(Color.FromArgb(200, 235, 235, 235), 1)
            g.DrawLines(pen, ChannelPoints(_histogram.Luminance, pk, graph))
        End Using
    End Sub

    Private Function PeakValue() As Double
        Dim p = 1.0
        For v = 1 To 254 ' 0 與 255 常因過曝/死黑暴衝，不列入縮放基準
            p = Math.Max(p, Math.Max(_histogram.Red(v), Math.Max(_histogram.Green(v), _histogram.Blue(v))))
        Next
        Return Math.Sqrt(p)
    End Function

    Private Shared Sub FillChannel(g As Graphics, data As Integer(), color As Color, peak As Double, area As RectangleF)
        Dim pts = ChannelPoints(data, peak, area)
        Dim poly(pts.Length + 1) As PointF
        pts.CopyTo(poly, 0)
        poly(pts.Length) = New PointF(area.Right - 1, area.Bottom)
        poly(pts.Length + 1) = New PointF(area.Left, area.Bottom)
        Using br As New SolidBrush(color)
            g.FillPolygon(br, poly)
        End Using
    End Sub

    Private Shared Function ChannelPoints(data As Integer(), peak As Double, area As RectangleF) As PointF()
        Dim pts(255) As PointF
        Dim usableH = area.Height - 4
        For v = 0 To 255
            Dim y = area.Bottom - CSng(Math.Min(1.0, Math.Sqrt(data(v)) / peak) * usableH)
            pts(v) = New PointF(area.Left + CSng(v * (area.Width - 1) / 255.0), y)
        Next
        Return pts
    End Function
End Class
