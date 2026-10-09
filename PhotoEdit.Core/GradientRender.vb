Imports System.Drawing
Imports System.Drawing.Imaging

''' <summary>
''' 點陣圖層裡的漸層操作：依形狀算出每個像素在漸層上的位置 t（0..1），換成顏色後以本操作的不透明度疊上去；
''' 有選取範圍（op.Region）時只畫在範圍裡。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    Private Shared Sub ApplyGradientOp(bmp As Bitmap, w As Integer, h As Integer, op As Global.PhotoEdit.DrawLayer)
        Dim g = op.Gradient
        If g Is Nothing Then Return
        Dim opacity = Math.Max(0, Math.Min(100, op.Opacity)) / 100.0F
        If opacity <= 0 Then Return
        Dim px = GradientPixels(g, w, h, Color.FromArgb(op.StrokeColorArgb), Color.FromArgb(op.FillColorArgb))
        If op.Region IsNot Nothing Then
            Dim mask = SelectionMask.Render(op.Region, w, h)
            For i = 0 To mask.Length - 1
                px(i * 4 + 3) = CByte(CInt(px(i * 4 + 3)) * mask(i) \ 255)
            Next
        End If
        Using fill As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Perspective.WritePixels(fill, px)
            LayerBlend.Composite(bmp, fill, New Rectangle(0, 0, w, h), opacity, op.Blend)
        End Using
    End Sub

    ''' <summary>
    ''' 填滿圖層：整張單色、漸層（FillColorArgb → StrokeColorArgb）或材質（FillContent = Material），有遮色片（Region）時只畫在裡面。
    ''' 不透明度與混合模式由一般圖層的合成處理（DrawLayer 的快取與 LayerBlend）。
    ''' </summary>
    Private Shared Function RenderFillLayer(layer As Global.PhotoEdit.DrawLayer, w As Integer, h As Integer) As Rendered
        Dim mask As Byte() = If(layer.Region IsNot Nothing, SelectionMask.Render(layer.Region, w, h), Nothing)
        Dim bmp As Bitmap
        If layer.Gradient IsNot Nothing Then
            Dim px = GradientPixels(layer.Gradient, w, h, Color.FromArgb(layer.FillColorArgb), Color.FromArgb(layer.StrokeColorArgb))
            If mask IsNot Nothing Then
                For i = 0 To mask.Length - 1
                    px(i * 4 + 3) = CByte(CInt(px(i * 4 + 3)) * mask(i) \ 255)
                Next
            End If
            bmp = New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Perspective.WritePixels(bmp, px)
        ElseIf layer.FillContent = FillContent.Material Then
            Dim cov(w * h - 1) As Single
            For i = 0 To cov.Length - 1
                cov(i) = If(mask Is Nothing, 1.0F, mask(i) / 255.0F)
            Next
            Dim style = layer.Clone()
            style.Brush = BrushKind.Texture
            style.StrokeColorArgb = layer.FillColorArgb
            style.Opacity = 100
            bmp = BrushFill(cov, w, h, New Rectangle(0, 0, w, h), style)
        Else
            Dim c = Color.FromArgb(layer.FillColorArgb)
            Dim px(w * h * 4 - 1) As Byte
            For i = 0 To w * h - 1
                px(i * 4) = c.B : px(i * 4 + 1) = c.G : px(i * 4 + 2) = c.R
                px(i * 4 + 3) = If(mask Is Nothing, c.A, CByte(CInt(c.A) * mask(i) \ 255))
            Next
            bmp = New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Perspective.WritePixels(bmp, px)
        End If
        Return New Rendered With {.Bitmap = bmp, .Region = New Rectangle(0, 0, w, h)}
    End Function

    ''' <summary>整張 w × h 的漸層（BGRA，非預乘）。公開給測試與預覽用。</summary>
    Public Shared Function GradientPixels(g As GradientFill, w As Integer, h As Integer, c1 As Color, c2 As Color) As Byte()
        Dim px(w * h * 4 - 1) As Byte
        Dim ax = g.X1 * h, ay = g.Y1 * h, bx = g.X2 * h, by = g.Y2 * h
        Dim dx = bx - ax, dy = by - ay
        Dim len2 = Math.Max(1.0E-06, dx * dx + dy * dy), len = Math.Sqrt(len2)
        Dim baseAngle = Math.Atan2(dy, dx)
        ' 四色：拖曳範圍當方框（起點 = 左上角的位置，終點 = 右下角）
        Dim left = Math.Min(ax, bx), right = Math.Max(ax, bx), top = Math.Min(ay, by), bottom = Math.Max(ay, by)
        Dim corners = {Color.FromArgb(g.Corner1), Color.FromArgb(g.Corner2), Color.FromArgb(g.Corner3), Color.FromArgb(g.Corner4)}
        Threading.Tasks.Parallel.For(0, h,
            Sub(y)
                Dim py = y + 0.5
                For x = 0 To w - 1
                    Dim pxX = x + 0.5
                    Dim vx = pxX - ax, vy = py - ay
                    Dim r, gg, b, a As Double
                    If g.Kind = GradientKind.FourColor Then
                        Dim u = If(right - left < 1, 0.5, Clamp((pxX - left) / (right - left)))
                        Dim v = If(bottom - top < 1, 0.5, Clamp((py - top) / (bottom - top)))
                        If g.Reverse Then u = 1 - u : v = 1 - v
                        Dim topC = Mix(corners(0), corners(1), u), botC = Mix(corners(2), corners(3), u)
                        r = topC.R + (botC.R - topC.R) * v : gg = topC.G + (botC.G - topC.G) * v
                        b = topC.B + (botC.B - topC.B) * v : a = topC.A + (botC.A - topC.A) * v
                    Else
                        Dim t As Double
                        Select Case g.Kind
                            Case GradientKind.Radial
                                t = Math.Sqrt(vx * vx + vy * vy) / len
                            Case GradientKind.Angle
                                t = (Math.Atan2(vy, vx) - baseAngle) / (2 * Math.PI)
                                t -= Math.Floor(t)
                            Case GradientKind.Reflected
                                t = Math.Abs((vx * dx + vy * dy) / len2)
                            Case GradientKind.Diamond
                                Dim along = Math.Abs((vx * dx + vy * dy) / len), across = Math.Abs((vy * dx - vx * dy) / len)
                                t = (along + across) / len
                            Case Else
                                t = (vx * dx + vy * dy) / len2
                        End Select
                        t = Clamp(t)
                        If g.Reverse Then t = 1 - t
                        Dim c = ColorAt(g.Colors, t, c1, c2)
                        r = c.R : gg = c.G : b = c.B : a = c.A
                    End If
                    If g.Dither Then
                        ' ±0.5 階的雜點：大片漸層不會出現色階條紋（固定的雜湊，每次一樣）
                        Dim n = (Hash(x, y, 77) - 0.5) * 0.9
                        r += n : gg += n : b += n
                    End If
                    Dim i = (y * w + x) * 4
                    px(i) = ToByte255(b) : px(i + 1) = ToByte255(gg) : px(i + 2) = ToByte255(r) : px(i + 3) = ToByte255(a)
                Next
            End Sub)
        Return px
    End Function

    ''' <summary>t（0..1）的顏色（0..255 的 RGBA）。</summary>
    Private Shared Function ColorAt(kind As GradientColors, t As Double, c1 As Color, c2 As Color) As (R As Double, G As Double, B As Double, A As Double)
        Select Case kind
            Case GradientColors.StrokeToTransparent
                Return (c1.R, c1.G, c1.B, c1.A * (1 - t))
            Case GradientColors.Rainbow
                ' 紅 → 黃 → 綠 → 青 → 藍 → 紫（色相 0..300°）
                Dim hh = t * 5
                Dim k = Math.Min(4, CInt(Math.Floor(hh))), f = hh - k
                Dim rr, g2, bb As Double
                Select Case k
                    Case 0 : rr = 1 : g2 = f : bb = 0
                    Case 1 : rr = 1 - f : g2 = 1 : bb = 0
                    Case 2 : rr = 0 : g2 = 1 : bb = f
                    Case 3 : rr = 0 : g2 = 1 - f : bb = 1
                    Case Else : rr = f : g2 = 0 : bb = 1
                End Select
                Return (rr * 255, g2 * 255, bb * 255, 255)
            Case Else
                Dim m = Mix(c1, c2, t)
                Return (m.R, m.G, m.B, m.A)
        End Select
    End Function

    Private Shared Function Mix(a As Color, b As Color, t As Double) As (R As Double, G As Double, B As Double, A As Double)
        Return (a.R + (CInt(b.R) - a.R) * t, a.G + (CInt(b.G) - a.G) * t, a.B + (CInt(b.B) - a.B) * t, a.A + (CInt(b.A) - a.A) * t)
    End Function

    Private Shared Function Mix(a As (R As Double, G As Double, B As Double, A As Double), b As (R As Double, G As Double, B As Double, A As Double), t As Double) As (R As Double, G As Double, B As Double, A As Double)
        Return (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t)
    End Function

    Private Shared Function Clamp(v As Double) As Double
        Return If(v < 0, 0, If(v > 1, 1, v))
    End Function

    Private Shared Function ToByte255(v As Double) As Byte
        If v <= 0 Then Return 0
        If v >= 255 Then Return 255
        Return CByte(Math.Round(v))
    End Function
End Class
