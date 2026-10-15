Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>互動式陰影、透明度漸層、封套、調和、影像描圖。</summary>
Module Batch3Tests

    Private Function Render(layers As List(Of DrawLayer)) As Bitmap
        Dim bmp As New Bitmap(200, 100, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
        End Using
        LayerStack.Draw(bmp, New EditRecipe With {.Drawings = layers})
        Return bmp
    End Function

    Private Function Sq(x As Double, y As Double, s As Double, c As Color) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Rectangle, .X = x, .Y = y, .W = s, .H = s, .Filled = True, .Stroked = False, .FillColorArgb = c.ToArgb()}
    End Function

    Sub Batch3TestsRun()
        Console.WriteLine("互動式陰影、透明度、封套、調和、影像描圖")
        DrawingRenderer.ClearCache()
        ' ---- 陰影 ----
        Dim box = Sq(1.0, 0.5, 0.3, Color.Red)
        box.Shadow = True : box.ShadowOffsetX = -0.2 : box.ShadowOffsetY = 0 : box.ShadowSoft = 1 : box.ShadowOpacity = 80 : box.ShadowColorArgb = Color.Blue.ToArgb()
        Using bmp = Render(New List(Of DrawLayer) From {box})
            Dim left = bmp.GetPixel(70, 50), right = bmp.GetPixel(125, 50)
            Check("自訂陰影：往左 0.2、藍色、80%", left.B > 120 AndAlso left.R < 120 AndAlso right.R > 240 AndAlso right.G > 240, $"{left} {right}")
        End Using
        Dim auto = Sq(1.0, 0.5, 0.3, Color.Red) : auto.Shadow = True
        Using bmp = Render(New List(Of DrawLayer) From {auto})
            Check("沒自訂時：陰影在右下（同以前）", bmp.GetPixel(117, 67).R < 230 AndAlso bmp.GetPixel(83, 33).R > 250, $"{bmp.GetPixel(117, 67)} {bmp.GetPixel(83, 33)}")
        End Using
        ' ---- 透明度漸層 ----
        Dim fade = Sq(1.0, 0.5, 0.8, Color.Black)
        fade.W = 1.6
        fade.FadeGradient = New GradientFill With {.X1 = 0, .Y1 = 0.5, .X2 = 1, .Y2 = 0.5}
        Using bmp = Render(New List(Of DrawLayer) From {fade})
            Dim l = bmp.GetPixel(25, 50).R, m = bmp.GetPixel(100, 50).R, r = bmp.GetPixel(175, 50).R
            Check("透明度漸層：左邊不透明、右邊透明", l < 40 AndAlso m > 90 AndAlso m < 170 AndAlso r > 220, $"{l} {m} {r}")
        End Using
        Dim fc As New DrawLayer()
        fc.CopyStyleFrom(box)
        Check("陰影設定是樣式的一部分", fc.ShadowOffsetX.GetValueOrDefault() = -0.2 AndAlso fc.ShadowOpacity = 80 AndAlso fc.ShadowColorArgb = Color.Blue.ToArgb())
        ' ---- 封套 ----
        Dim rect = Sq(0.5, 0.5, 0.4, Color.Green)
        Dim b = DrawGeometry.Bounds(rect)
        Dim grid = Envelope.DefaultGrid(b)
        Dim same = Envelope.Apply(rect, grid)
        Dim sb = DrawGeometry.Bounds(same)
        Check("封套沒變形：形狀一樣", Math.Abs(sb.Width - b.Width) < 0.001 AndAlso Math.Abs(sb.Height - b.Height) < 0.001 AndAlso same.Shape = DrawShape.Path)
        grid(1) = New PointF(grid(1).X, grid(1).Y - 0.1F) ' 上緣中點往上拉
        Dim arched = Envelope.Apply(rect, grid)
        Dim ab = DrawGeometry.Bounds(arched)
        Check("拉上緣中點：上面拱起來、下面不變", ab.Top < b.Top - 0.04 AndAlso Math.Abs(ab.Bottom - b.Bottom) < 0.001, ab.ToString())
        Check("拱起來的是曲線（中間比兩端高）", PathBoolean.Contains({DrawGeometry.SampleContour(arched.Contours(0))}, New PointF(0.5F, b.Top - 0.03F)) AndAlso
              Not PathBoolean.Contains({DrawGeometry.SampleContour(arched.Contours(0))}, New PointF(b.Left + 0.01F, b.Top - 0.03F)))
        Check("文字、點陣不能套封套", Not Envelope.CanApply(New DrawLayer With {.Shape = DrawShape.Text, .Text = "A"}) AndAlso Not Envelope.CanApply(New DrawLayer With {.Shape = DrawShape.Raster}))
        ' ---- 調和 ----
        Dim a1 = Sq(0.3, 0.5, 0.1, Color.Red), b1 As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 1.5, .Y = 0.5, .W = 0.3, .H = 0.3, .Filled = True, .FillColorArgb = Color.Blue.ToArgb()}
        Dim mids = ShapeBlend.Make(a1, b1, 3)
        Check("調和 3 步：3 個中間圖形", mids.Count = 3 AndAlso mids.All(Function(m) m.Shape = DrawShape.Path))
        Dim cx = mids.Select(Function(m) DrawGeometry.Bounds(m).X + DrawGeometry.Bounds(m).Width / 2).ToList()
        Check("位置從左到右均分", Math.Abs(cx(0) - 0.6) < 0.02 AndAlso Math.Abs(cx(1) - 0.9) < 0.02 AndAlso Math.Abs(cx(2) - 1.2) < 0.02, String.Join(" ", cx.Select(Function(v) v.ToString("0.00"))))
        Dim c1 = Color.FromArgb(mids(1).FillColorArgb)
        Check("顏色、大小也均分", c1.R > 100 AndAlso c1.R < 155 AndAlso c1.B > 100 AndAlso c1.B < 155 AndAlso Math.Abs(DrawGeometry.Bounds(mids(1)).Width - 0.2) < 0.02, $"{c1} {DrawGeometry.Bounds(mids(1)).Width:0.00}")
        ' ---- 影像描圖 ----
        Using photo As New Bitmap(300, 200)
            Using g = Graphics.FromImage(photo)
                g.Clear(Color.White)
                g.FillEllipse(Brushes.Black, 100, 50, 100, 100)
            End Using
            Dim line = ImageTrace.Trace(photo, New TraceOptions With {.Mode = TraceMode.LineArt, .RemoveBackground = True})
            Check("黑白線稿＋去背景：只有黑色那一層", line.Count = 1 AndAlso Color.FromArgb(line(0).FillColorArgb).R < 40, $"{line.Count}")
            Dim cb = DrawGeometry.Bounds(line(0))
            Check("描出的圓：位置大小對", Math.Abs(cb.X - 0.5) < 0.02 AndAlso Math.Abs(cb.Width - 0.5) < 0.03 AndAlso line(0).Contours(0).Nodes.Count <= 12,
                  $"{cb} 節點 {line(0).Contours(0).Nodes.Count}")
            Dim withBg = ImageTrace.Trace(photo, New TraceOptions With {.Mode = TraceMode.LineArt})
            Check("不去背景：白色鋪滿整張在下面", withBg.Count = 2 AndAlso Color.FromArgb(withBg(0).FillColorArgb).R > 200 AndAlso Math.Abs(DrawGeometry.Bounds(withBg(0)).Width - 1.5) < 0.01)
        End Using
        Using photo As New Bitmap(300, 200)
            Using g = Graphics.FromImage(photo)
                g.Clear(Color.FromArgb(240, 240, 240))
                g.FillRectangle(Brushes.Red, 20, 20, 120, 160)
                g.FillEllipse(Brushes.Blue, 170, 40, 110, 110)
            End Using
            Dim col = ImageTrace.Trace(photo, New TraceOptions With {.Mode = TraceMode.Color, .Colors = 3})
            Dim names = String.Join(",", col.Select(Function(l) l.Name))
            Check("彩色 3 色：三層、背景在最下面", col.Count = 3 AndAlso Color.FromArgb(col(0).FillColorArgb).G > 200, names)
            Dim red = col.FirstOrDefault(Function(l) Color.FromArgb(l.FillColorArgb).R > 200 AndAlso Color.FromArgb(l.FillColorArgb).G < 60)
            Check("紅色那塊是矩形（4 個角）", red IsNot Nothing AndAlso red.Contours.Count = 1 AndAlso red.Contours(0).Nodes.Count <= 6, If(red Is Nothing, "沒有", red.Contours(0).Nodes.Count.ToString()))
            Using bmp = Render(col)
                Check("描完畫回去：顏色在對的位置", bmp.GetPixel(53, 67).R > 200 AndAlso bmp.GetPixel(53, 67).G < 80 AndAlso bmp.GetPixel(150, 64).B > 200, $"{bmp.GetPixel(53, 67)} {bmp.GetPixel(150, 64)}")
            End Using
        End Using
    End Sub
End Module
