Imports System.Drawing

''' <summary>
''' 網格填色（同 CorelDRAW 的網格填色）：圖形上一張 Rows × Cols 個節點的網格，每個節點一個顏色、可以拖到別的位置，
''' 節點之間的顏色平滑過渡（Catmull-Rom），做出寫實的漸層（水果、布料、金屬的光澤）。
''' 節點位置是圖形外框的比例（同漸層的座標），圖形移動、縮放時跟著走。
''' </summary>
Public NotInheritable Class MeshFill
    ''' <summary>節點的列數、欄數（各 2..8）。</summary>
    Public Property Rows As Integer = 3
    Public Property Cols As Integer = 3
    ''' <summary>節點位置（圖形外框的比例），依列優先排：(r, c) 在索引 r × Cols + c；每個節點兩個數（X, Y）。</summary>
    Public Property Points As List(Of Double) = New List(Of Double)()
    ''' <summary>節點顏色（ARGB），依列優先排。</summary>
    Public Property Colors As List(Of Integer) = New List(Of Integer)()

    Public Function Clone() As MeshFill
        Return New MeshFill With {.Rows = Rows, .Cols = Cols, .Points = New List(Of Double)(Points), .Colors = New List(Of Integer)(Colors)}
    End Function

    ''' <summary>規則的網格，所有節點同一個顏色。</summary>
    Public Shared Function Create(rows As Integer, cols As Integer, argb As Integer) As MeshFill
        rows = Math.Max(2, Math.Min(8, rows)) : cols = Math.Max(2, Math.Min(8, cols))
        Dim m As New MeshFill With {.Rows = rows, .Cols = cols}
        For r = 0 To rows - 1
            For c = 0 To cols - 1
                m.Points.Add(c / (cols - 1.0)) : m.Points.Add(r / (rows - 1.0))
                m.Colors.Add(argb)
            Next
        Next
        Return m
    End Function

    Public ReadOnly Property Count As Integer
        Get
            Return Rows * Cols
        End Get
    End Property

    Public Function PointAt(i As Integer) As PointF
        Return New PointF(CSng(Points(i * 2)), CSng(Points(i * 2 + 1)))
    End Function

    Public Sub SetPoint(i As Integer, p As PointF)
        Points(i * 2) = Math.Round(p.X, 5) : Points(i * 2 + 1) = Math.Round(p.Y, 5)
    End Sub

    ''' <summary>格子參數 (s, t)（0..Cols−1、0..Rows−1）上的顏色：Catmull-Rom 平滑內插（各通道 0..255）。</summary>
    Public Function ColorAtParam(s As Double, t As Double) As (A As Double, R As Double, G As Double, B As Double)
        Dim ci = Math.Max(0, Math.Min(Cols - 2, CInt(Math.Floor(s)))), ri = Math.Max(0, Math.Min(Rows - 2, CInt(Math.Floor(t))))
        Dim fs = s - ci, ft = t - ri
        Dim acc(3) As Double
        For dr = -1 To 2
            Dim wr = CatmullWeight(dr, ft)
            Dim rr = Math.Max(0, Math.Min(Rows - 1, ri + dr))
            For dc = -1 To 2
                Dim w = wr * CatmullWeight(dc, fs)
                If w = 0 Then Continue For
                Dim cc = Math.Max(0, Math.Min(Cols - 1, ci + dc))
                Dim col = Color.FromArgb(Colors(rr * Cols + cc))
                acc(0) += w * col.A : acc(1) += w * col.R : acc(2) += w * col.G : acc(3) += w * col.B
            Next
        Next
        Return (Clamp255(acc(0)), Clamp255(acc(1)), Clamp255(acc(2)), Clamp255(acc(3)))
    End Function

    ''' <summary>格子參數 (s, t) 上的位置（四個角雙線性，圖形外框的比例）。</summary>
    Public Function PositionAtParam(s As Double, t As Double) As PointF
        Dim ci = Math.Max(0, Math.Min(Cols - 2, CInt(Math.Floor(s)))), ri = Math.Max(0, Math.Min(Rows - 2, CInt(Math.Floor(t))))
        Dim fs = s - ci, ft = t - ri
        Dim p00 = PointAt(ri * Cols + ci), p01 = PointAt(ri * Cols + ci + 1), p10 = PointAt((ri + 1) * Cols + ci), p11 = PointAt((ri + 1) * Cols + ci + 1)
        Dim x = (p00.X * (1 - fs) + p01.X * fs) * (1 - ft) + (p10.X * (1 - fs) + p11.X * fs) * ft
        Dim y = (p00.Y * (1 - fs) + p01.Y * fs) * (1 - ft) + (p10.Y * (1 - fs) + p11.Y * fs) * ft
        Return New PointF(CSng(x), CSng(y))
    End Function

    ''' <summary>改成 rows × cols：新節點的位置與顏色取自原本網格對應的地方。</summary>
    Public Function Resample(newRows As Integer, newCols As Integer) As MeshFill
        ' 參數不能叫 rows／cols：VB 不分大小寫，會蓋掉本身的 Rows／Cols
        newRows = Math.Max(2, Math.Min(8, newRows)) : newCols = Math.Max(2, Math.Min(8, newCols))
        Dim m As New MeshFill With {.Rows = newRows, .Cols = newCols}
        For r = 0 To newRows - 1
            For c = 0 To newCols - 1
                Dim s = c / (newCols - 1.0) * (Cols - 1), t = r / (newRows - 1.0) * (Rows - 1)
                Dim p = PositionAtParam(s, t)
                m.Points.Add(Math.Round(p.X, 5)) : m.Points.Add(Math.Round(p.Y, 5))
                Dim k = ColorAtParam(s, t)
                m.Colors.Add(Color.FromArgb(CInt(k.A), CInt(k.R), CInt(k.G), CInt(k.B)).ToArgb())
            Next
        Next
        Return m
    End Function

    ''' <summary>所有節點顏色的平均（SVG 不支援網格填色時用）。</summary>
    Public Function AverageColor() As Integer
        Dim a = 0.0, r = 0.0, g = 0.0, b = 0.0
        For Each v In Colors
            Dim c = Color.FromArgb(v)
            a += c.A : r += c.R : g += c.G : b += c.B
        Next
        Dim n = Math.Max(1, Colors.Count)
        Return Color.FromArgb(CInt(a / n), CInt(r / n), CInt(g / n), CInt(b / n)).ToArgb()
    End Function

    Private Shared Function CatmullWeight(d As Integer, t As Double) As Double
        Dim t2 = t * t, t3 = t2 * t
        Select Case d
            Case -1 : Return 0.5 * (-t3 + 2 * t2 - t)
            Case 0 : Return 0.5 * (3 * t3 - 5 * t2 + 2)
            Case 1 : Return 0.5 * (-3 * t3 + 4 * t2 + t)
            Case Else : Return 0.5 * (t3 - t2)
        End Select
    End Function

    Private Shared Function Clamp255(v As Double) As Double
        Return Math.Max(0, Math.Min(255, v))
    End Function
End Class

Partial Public NotInheritable Class DrawingRenderer

    ''' <summary>
    ''' 網格填色：每一格切成 Sub × Sub 個小四邊形、各分成兩個三角形，頂點顏色用網格的平滑內插，三角形裡線性內插，
    ''' 畫進 region 大小的顏色緩衝（BGRA 位元組），再由呼叫端用圖形的遮罩合成。h：照片高度（像素）。
    ''' </summary>
    Public Shared Function MeshRegion(layer As DrawLayer, region As Rectangle, h As Integer) As Byte()
        Dim m = layer.FillMesh
        Dim w = region.Width, hh = region.Height
        Dim px(w * hh * 4 - 1) As Byte
        If m Is Nothing OrElse m.Count < 4 OrElse m.Points.Count < m.Count * 2 OrElse m.Colors.Count < m.Count Then Return px
        ' 網格沒蓋到的地方（邊上的節點拖進圖形裡時）先鋪平均色，不會露出透明
        Dim avg = Color.FromArgb(m.AverageColor())
        For k = 0 To px.Length - 1 Step 4
            px(k) = avg.B : px(k + 1) = avg.G : px(k + 2) = avg.R : px(k + 3) = avg.A
        Next
        Const SubDiv As Integer = 12
        Dim nS = (m.Cols - 1) * SubDiv, nT = (m.Rows - 1) * SubDiv
        Dim vx(nS, nT), vy(nS, nT) As Single
        Dim vc(nS, nT) As (A As Double, R As Double, G As Double, B As Double)
        For j = 0 To nT
            For i = 0 To nS
                Dim s = i / CDbl(SubDiv), t = j / CDbl(SubDiv)
                Dim f = m.PositionAtParam(s, t)
                Dim p = DrawGeometry.GradientPoint(layer, f.X, f.Y)
                vx(i, j) = CSng(p.X * h - region.X) : vy(i, j) = CSng(p.Y * h - region.Y)
                vc(i, j) = m.ColorAtParam(s, t)
            Next
        Next
        For j = 0 To nT - 1
            For i = 0 To nS - 1
                Tri(px, w, hh, vx(i, j), vy(i, j), vc(i, j), vx(i + 1, j), vy(i + 1, j), vc(i + 1, j), vx(i + 1, j + 1), vy(i + 1, j + 1), vc(i + 1, j + 1))
                Tri(px, w, hh, vx(i, j), vy(i, j), vc(i, j), vx(i + 1, j + 1), vy(i + 1, j + 1), vc(i + 1, j + 1), vx(i, j + 1), vy(i, j + 1), vc(i, j + 1))
            Next
        Next
        Return px
    End Function

    ''' <summary>填一個三角形（重心座標內插顏色），像素中心落在三角形裡（含邊）才畫，相鄰三角形不留縫。</summary>
    Private Shared Sub Tri(px As Byte(), w As Integer, h As Integer,
                           x0 As Single, y0 As Single, c0 As (A As Double, R As Double, G As Double, B As Double),
                           x1 As Single, y1 As Single, c1 As (A As Double, R As Double, G As Double, B As Double),
                           x2 As Single, y2 As Single, c2 As (A As Double, R As Double, G As Double, B As Double))
        Dim area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        If Math.Abs(area) < 0.000001 Then Return
        Dim minX = Math.Max(0, CInt(Math.Floor(Math.Min(x0, Math.Min(x1, x2))))), maxX = Math.Min(w - 1, CInt(Math.Ceiling(Math.Max(x0, Math.Max(x1, x2)))))
        Dim minY = Math.Max(0, CInt(Math.Floor(Math.Min(y0, Math.Min(y1, y2))))), maxY = Math.Min(h - 1, CInt(Math.Ceiling(Math.Max(y0, Math.Max(y1, y2)))))
        Const Eps As Double = -0.0001
        For y = minY To maxY
            Dim py = y + 0.5
            For x = minX To maxX
                Dim pxX = x + 0.5
                Dim w0 = ((x1 - pxX) * (y2 - py) - (x2 - pxX) * (y1 - py)) / area
                Dim w1 = ((x2 - pxX) * (y0 - py) - (x0 - pxX) * (y2 - py)) / area
                Dim w2 = 1 - w0 - w1
                If w0 < Eps OrElse w1 < Eps OrElse w2 < Eps Then Continue For
                Dim k = (y * w + x) * 4
                px(k) = CByte(Math.Round(c0.B * w0 + c1.B * w1 + c2.B * w2))
                px(k + 1) = CByte(Math.Round(c0.G * w0 + c1.G * w1 + c2.G * w2))
                px(k + 2) = CByte(Math.Round(c0.R * w0 + c1.R * w1 + c2.R * w2))
                px(k + 3) = CByte(Math.Round(c0.A * w0 + c1.A * w1 + c2.A * w2))
            Next
        Next
    End Sub
End Class
