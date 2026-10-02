Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>直方圖：RGB 三色半透明疊合，亮度以白線表示；縱軸取平方根讓暗淡區域也看得到。</summary>
Friend Class HistogramView
    Inherits Control

    Private _histogram As Histogram

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        BackColor = Color.FromArgb(30, 30, 32)
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

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        If _histogram Is Nothing OrElse _histogram.PixelCount = 0 Then Return

        Dim peak = 1.0
        For v = 1 To 254 ' 0 與 255 常因過曝/死黑暴衝，不列入縮放基準
            peak = Math.Max(peak, Math.Max(_histogram.Red(v), Math.Max(_histogram.Green(v), _histogram.Blue(v))))
        Next
        peak = Math.Sqrt(peak)

        g.SmoothingMode = SmoothingMode.AntiAlias
        g.CompositingMode = CompositingMode.SourceOver
        FillChannel(g, _histogram.Red, Color.FromArgb(110, 255, 70, 70), peak)
        FillChannel(g, _histogram.Green, Color.FromArgb(110, 70, 220, 70), peak)
        FillChannel(g, _histogram.Blue, Color.FromArgb(110, 80, 120, 255), peak)
        Using pen As New Pen(Color.FromArgb(200, 235, 235, 235), 1)
            g.DrawLines(pen, ChannelPoints(_histogram.Luminance, peak))
        End Using
    End Sub

    Private Sub FillChannel(g As Graphics, data As Integer(), color As Color, peak As Double)
        Dim pts = ChannelPoints(data, peak)
        Dim poly(pts.Length + 1) As PointF
        pts.CopyTo(poly, 0)
        poly(pts.Length) = New PointF(Width - 1, Height)
        poly(pts.Length + 1) = New PointF(0, Height)
        Using br As New SolidBrush(color)
            g.FillPolygon(br, poly)
        End Using
    End Sub

    Private Function ChannelPoints(data As Integer(), peak As Double) As PointF()
        Dim pts(255) As PointF
        Dim usableH = Height - 4
        For v = 0 To 255
            Dim y = Height - CSng(Math.Min(1.0, Math.Sqrt(data(v)) / peak) * usableH)
            pts(v) = New PointF(CSng(v * (Width - 1) / 255.0), y)
        Next
        Return pts
    End Function
End Class
