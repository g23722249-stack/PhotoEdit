Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>拼貼版型：每格是整張畫布的 0..1 比例。</summary>
Public Class CollageLayout
    Public ReadOnly Property Name As String
    Public ReadOnly Property Cells As IReadOnlyList(Of RectangleF)

    Public Sub New(name As String, ParamArray cells As RectangleF())
        Me.Name = name
        Me.Cells = cells
    End Sub

    Public Shared ReadOnly BuiltIn As IReadOnlyList(Of CollageLayout) = New List(Of CollageLayout) From {
        New CollageLayout("2 張：左右", R(0, 0, 0.5, 1), R(0.5, 0, 0.5, 1)),
        New CollageLayout("2 張：上下", R(0, 0, 1, 0.5), R(0, 0.5, 1, 0.5)),
        New CollageLayout("3 張：左大右二", R(0, 0, 0.6, 1), R(0.6, 0, 0.4, 0.5), R(0.6, 0.5, 0.4, 0.5)),
        New CollageLayout("3 張：上大下二", R(0, 0, 1, 0.6), R(0, 0.6, 0.5, 0.4), R(0.5, 0.6, 0.5, 0.4)),
        New CollageLayout("3 張：直排", R(0, 0, 1 / 3.0, 1), R(1 / 3.0, 0, 1 / 3.0, 1), R(2 / 3.0, 0, 1 / 3.0, 1)),
        New CollageLayout("4 張：田字", R(0, 0, 0.5, 0.5), R(0.5, 0, 0.5, 0.5), R(0, 0.5, 0.5, 0.5), R(0.5, 0.5, 0.5, 0.5)),
        New CollageLayout("4 張：一大三小", R(0, 0, 1, 0.65), R(0, 0.65, 1 / 3.0, 0.35), R(1 / 3.0, 0.65, 1 / 3.0, 0.35), R(2 / 3.0, 0.65, 1 / 3.0, 0.35)),
        New CollageLayout("5 張：二上三下", R(0, 0, 0.5, 0.55), R(0.5, 0, 0.5, 0.55), R(0, 0.55, 1 / 3.0, 0.45), R(1 / 3.0, 0.55, 1 / 3.0, 0.45), R(2 / 3.0, 0.55, 1 / 3.0, 0.45)),
        New CollageLayout("6 張：3×2", Grid(3, 2)),
        New CollageLayout("9 張：3×3", Grid(3, 3))
    }

    Private Shared Function R(x As Double, y As Double, w As Double, h As Double) As RectangleF
        Return New RectangleF(CSng(x), CSng(y), CSng(w), CSng(h))
    End Function

    Private Shared Function Grid(cols As Integer, rows As Integer) As RectangleF()
        Dim cells As New List(Of RectangleF)()
        For y = 0 To rows - 1
            For x = 0 To cols - 1
                cells.Add(R(x / cols, y / rows, 1 / cols, 1 / rows))
            Next
        Next
        Return cells.ToArray()
    End Function
End Class

Public Class CollageSettings
    Public Property Layout As CollageLayout = CollageLayout.BuiltIn(0)
    ''' <summary>畫布寬高比。</summary>
    Public Property Aspect As Double = 3 / 2.0
    ''' <summary>輸出長邊（像素）。</summary>
    Public Property LongSide As Integer = 3000
    ''' <summary>間距，0..100。</summary>
    Public Property Spacing As Integer = 30
    ''' <summary>圓角，0..100。</summary>
    Public Property CornerRadius As Integer
    Public Property Background As Color = Color.White
End Class

''' <summary>依版型把照片排進畫布：每張照片以「填滿並置中裁切」放進格子。</summary>
Public NotInheritable Class CollageRenderer
    Private Sub New()
    End Sub

    Public Shared Function CanvasSize(settings As CollageSettings, Optional longSide As Integer = 0) As Size
        Dim ls = If(longSide > 0, longSide, settings.LongSide)
        If settings.Aspect >= 1 Then Return New Size(ls, Math.Max(1, CInt(Math.Round(ls / settings.Aspect))))
        Return New Size(Math.Max(1, CInt(Math.Round(ls * settings.Aspect))), ls)
    End Function

    ''' <summary>每格在畫布上的像素範圍（已扣除間距）。</summary>
    Public Shared Function CellRects(settings As CollageSettings, size As Size) As List(Of RectangleF)
        Dim sp = CSng(settings.Spacing / 100.0 * 0.04 * Math.Min(size.Width, size.Height))
        Dim innerW = size.Width - sp, innerH = size.Height - sp
        Return settings.Layout.Cells.Select(
            Function(c) New RectangleF(sp / 2 + c.X * innerW + sp / 2, sp / 2 + c.Y * innerH + sp / 2,
                                       Math.Max(1, c.Width * innerW - sp), Math.Max(1, c.Height * innerH - sp))).ToList()
    End Function

    ''' <param name="images">依格子順序的照片（已套用編輯）；Nothing 的格子畫成空白格。</param>
    Public Shared Function Render(images As IList(Of Bitmap), settings As CollageSettings, Optional longSide As Integer = 0,
                                  Optional drawPlaceholders As Boolean = False) As Bitmap
        Dim size = CanvasSize(settings, longSide)
        Dim result As New Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(result)
            g.Clear(settings.Background)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            Dim cells = CellRects(settings, size)
            Dim radius = CSng(settings.CornerRadius / 100.0 * 0.08 * Math.Min(size.Width, size.Height))
            For i = 0 To cells.Count - 1
                Dim cell = cells(i)
                Dim img = If(images IsNot Nothing AndAlso i < images.Count, images(i), Nothing)
                Using path = CellPath(cell, radius)
                    If img Is Nothing Then
                        If drawPlaceholders Then
                            Using br As New SolidBrush(Color.FromArgb(225, 228, 232))
                                g.FillPath(br, path)
                            End Using
                            Dim arm = Math.Min(cell.Width, cell.Height) / 8
                            Dim cx = cell.X + cell.Width / 2, cy = cell.Y + cell.Height / 2
                            Using pen As New Pen(Color.FromArgb(160, 165, 175), Math.Max(2, arm / 4))
                                g.DrawLine(pen, cx - arm, cy, cx + arm, cy)
                                g.DrawLine(pen, cx, cy - arm, cx, cy + arm)
                            End Using
                        End If
                        Continue For
                    End If
                    ' 填滿並置中裁切。
                    Dim s = Math.Max(cell.Width / img.Width, cell.Height / img.Height)
                    Dim sw = cell.Width / s, sh = cell.Height / s
                    Dim src As New RectangleF((img.Width - sw) / 2, (img.Height - sh) / 2, sw, sh)
                    Dim state = g.Save()
                    g.SetClip(path)
                    g.DrawImage(img, cell, src, GraphicsUnit.Pixel)
                    g.Restore(state)
                End Using
            Next
        End Using
        Return result
    End Function

    Private Shared Function CellPath(r As RectangleF, radius As Single) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height))
        If d < 1 Then
            p.AddRectangle(r)
            Return p
        End If
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function
End Class
