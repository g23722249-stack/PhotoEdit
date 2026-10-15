Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>工具列的新工具：修飾筆（模糊、銳利化、加亮、加深、海綿、修復）、文字圖形、刀具。</summary>
Module ToolBrushTests

    Private Function Stroke(brush As BrushKind, x0 As Single, x1 As Single, Optional setup As Action(Of DrawLayer) = Nothing) As DrawLayer
        Dim pts As New List(Of DrawPoint)()
        For i = 0 To 20
            pts.Add(New DrawPoint(x0 + (x1 - x0) * i / 20.0F, 0.5F))
        Next
        Dim d As New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = brush, .StrokeColorArgb = Color.White.ToArgb(), .StrokeWidth = 0.3, .Seed = 5,
                                     .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}}
        setup?.Invoke(d)
        Return New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {d}}
    End Function

    ''' <summary>200 × 100 的「照片」（左右兩半各一色）上畫圖層。</summary>
    Private Function Render(layers As List(Of DrawLayer), left As Color, right As Color) As Bitmap
        Dim bmp As New Bitmap(200, 100, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp), lb As New SolidBrush(left), rb As New SolidBrush(right)
            g.FillRectangle(lb, 0, 0, 100, 100)
            g.FillRectangle(rb, 100, 0, 100, 100)
        End Using
        DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = layers})
        Return bmp
    End Function

    Private Function Area(d As DrawLayer) As Double
        Return d.Contours.Sum(Function(c) Math.Abs(PathBoolean.SignedArea(DrawGeometry.SampleContour(c))))
    End Function

    Sub ToolBrushTestsRun()
        Console.WriteLine("工具列的新工具（修飾筆、文字、刀具）")
        DrawingRenderer.ClearCache()
        Check("修飾筆只從工具列選、會讀畫布；修復筆要設來源", DrawLayer.IsToolOnlyBrush(BrushKind.Blur) AndAlso DrawLayer.IsToolOnlyBrush(BrushKind.Heal) AndAlso
              Not DrawLayer.IsToolOnlyBrush(BrushKind.Smudge) AndAlso DrawLayer.SamplesCanvas(BrushKind.Dodge) AndAlso
              DrawLayer.UsesCloneSource(BrushKind.Heal) AndAlso DrawLayer.UsesCloneSource(BrushKind.Clone) AndAlso Not DrawLayer.UsesCloneSource(BrushKind.Blur))

        Dim gray = Color.FromArgb(128, 128, 128)
        Using bmp = Render(New List(Of DrawLayer) From {Stroke(BrushKind.Dodge, 0.2F, 1.8F)}, gray, gray)
            Dim c = bmp.GetPixel(100, 50)
            Check("加亮：灰色變亮、沒塗到的地方不變", c.R > 150 AndAlso bmp.GetPixel(100, 3) = Color.FromArgb(255, gray), c.ToString())
        End Using
        Using bmp = Render(New List(Of DrawLayer) From {Stroke(BrushKind.Burn, 0.2F, 1.8F)}, gray, gray)
            Dim c = bmp.GetPixel(100, 50)
            Check("加深：灰色變暗", c.R < 105, c.ToString())
        End Using
        Dim red = Color.FromArgb(220, 30, 30), blue = Color.FromArgb(30, 60, 220)
        Using bmp = Render(New List(Of DrawLayer) From {Stroke(BrushKind.SpongeDesaturate, 0.2F, 1.8F)}, red, red)
            Dim c = bmp.GetPixel(50, 50)
            Check("海綿（降低飽和）：紅色變淡", c.R - c.B < 170, c.ToString())
        End Using
        Dim muted = Color.FromArgb(160, 110, 110)
        Using bmp = Render(New List(Of DrawLayer) From {Stroke(BrushKind.SpongeSaturate, 0.2F, 1.8F)}, muted, muted)
            Dim c = bmp.GetPixel(50, 50)
            Check("海綿（增加飽和）：顏色更鮮豔", CInt(c.R) - c.B > 60, c.ToString())
        End Using
        Using bmp = Render(New List(Of DrawLayer) From {Stroke(BrushKind.Blur, 0.6F, 1.4F)}, red, blue)
            Dim c = bmp.GetPixel(98, 50)
            Check("模糊：紅藍交界變成混色、遠處不變", c.B > 45 AndAlso c.R < 215 AndAlso bmp.GetPixel(10, 50) = Color.FromArgb(255, red), c.ToString())
        End Using
        Dim dark = Color.FromArgb(100, 100, 100), light = Color.FromArgb(160, 160, 160)
        Using bmp = Render(New List(Of DrawLayer) From {Stroke(BrushKind.Sharpen, 0.6F, 1.4F)}, dark, light)
            Dim a = bmp.GetPixel(99, 50), b = bmp.GetPixel(100, 50)
            Check("銳利化：交界兩邊的對比變大", a.R < 100 AndAlso b.R > 160, $"{a.R} / {b.R}")
        End Using
        ' 修復筆：從左邊（紅）取樣畫到右邊（藍）：顏色要融入目的地（還是藍色），不像仿製會變紅
        Using bmp = Render(New List(Of DrawLayer) From {Stroke(BrushKind.Heal, 1.3F, 1.7F, Sub(d)
                                                                                                 d.CloneDX = -1 : d.Softness = 0
                                                                                             End Sub)}, red, blue)
            Dim c = bmp.GetPixel(150, 50)
            Check("修復筆：取來源的紋理，顏色融入目的地（仍是藍色）", c.B > 150 AndAlso c.R < 90, c.ToString())
        End Using

        ' ---- 文字 ----
        Dim t As New DrawLayer With {.Shape = DrawShape.Text, .X = 0.75, .Y = 0.5, .W = 1.2, .H = 0.6, .Text = "字字", .TextSize = 0.4,
                                     .TextColorArgb = Color.Black.ToArgb(), .Stroked = False, .Filled = False}
        Check("文字：有文字、不是圖說、外形是方框", DrawGeometry.HasText(DrawShape.Text) AndAlso Not DrawGeometry.IsCallout(DrawShape.Text) AndAlso
              DrawGeometry.Figures(t).Count = 1 AndAlso DrawGeometry.Figures(t)(0).Points.Length = 4)
        Check("文字不能轉曲線、焊接、切開", DrawGeometry.ToPath(t) Is Nothing AndAlso Not PathBoolean.CanCombine(t) AndAlso Not PathKnife.CanCut(t))
        Using bmp = Render(New List(Of DrawLayer) From {t}, Color.White, Color.White)
            Dim dark2 = 0
            For y = 0 To 99 Step 2
                For x = 0 To 199 Step 2
                    If bmp.GetPixel(x, y).R < 100 Then dark2 += 1
                Next
            Next
            Check("文字：畫出字、沒有勾外框時沒有框線", dark2 > 100 AndAlso bmp.GetPixel(31, 21).R > 240, $"{dark2} {bmp.GetPixel(31, 21)}")
        End Using

        ' ---- 刀具 ----
        Dim rect As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.5, .Y = 0.5, .W = 0.4, .H = 0.2, .Filled = True, .Name = "方"}
        Dim halves = PathKnife.Cut(rect, New PointF(0.5F, 0.2F), New PointF(0.5F, 0.8F))
        Check("刀具：矩形從中間切成兩塊、面積各一半、名稱不變", halves IsNot Nothing AndAlso halves.Count = 2 AndAlso
              halves.All(Function(h) Math.Abs(Area(h) - 0.04) < 0.0005 AndAlso h.Name = "方"), If(halves Is Nothing, "Nothing", String.Join(",", halves.Select(Function(h) Area(h).ToString("0.0000")))))
        Check("刀具：只切進一半（沒穿過）不算", PathKnife.Cut(rect, New PointF(0.5F, 0.2F), New PointF(0.5F, 0.5F)) Is Nothing)
        Check("刀具：刀線沒碰到不算", PathKnife.Cut(rect, New PointF(0.1F, 0.2F), New PointF(0.1F, 0.8F)) Is Nothing)
        Dim el As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.5, .Y = 0.5, .W = 0.3, .H = 0.3, .Filled = True}
        Dim ep = PathKnife.Cut(el, New PointF(0.2F, 0.3F), New PointF(0.8F, 0.7F))
        Check("刀具：斜切圓形成兩塊", ep IsNot Nothing AndAlso ep.Count = 2)
        Dim ln As New DrawLayer With {.Shape = DrawShape.Line, .StrokeWidth = 0.01, .Points = New List(Of DrawPoint) From {New DrawPoint(0.1, 0.5), New DrawPoint(0.9, 0.5)}}
        Dim lp = PathKnife.Cut(ln, New PointF(0.4F, 0.3F), New PointF(0.4F, 0.7F))
        Check("刀具：直線在交點斷成兩條", lp IsNot Nothing AndAlso lp.Count = 2 AndAlso
              lp.Any(Function(x) Math.Abs(DrawGeometry.Bounds(x).Right - 0.4) < 0.002) AndAlso lp.Any(Function(x) Math.Abs(DrawGeometry.Bounds(x).Left - 0.4) < 0.002),
              If(lp Is Nothing, "Nothing", String.Join(" ", lp.Select(Function(x) DrawGeometry.Bounds(x).ToString()))))
        Dim lp2 = PathKnife.Cut(ln, New PointF(0.3F, 0.3F), New PointF(0.6F, 0.7F))
        Dim lp3 = PathKnife.Cut(ln, New PointF(0.3F, 0.4F), New PointF(0.3F, 0.6F))
        Check("刀具：斜線也切得斷", lp2 IsNot Nothing AndAlso lp2.Count = 2 AndAlso lp3 IsNot Nothing AndAlso lp3.Count = 2)
        Check("刀具：圖說、點陣圖層不能切", Not PathKnife.CanCut(New DrawLayer With {.Shape = DrawShape.CalloutRect}) AndAlso Not PathKnife.CanCut(New DrawLayer With {.Shape = DrawShape.Raster}))
    End Sub
End Module
