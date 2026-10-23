Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>網格填色與漸層中間色標（互動式填色）。</summary>
Module MeshFillTests

    Private Function Render(d As DrawLayer) As Bitmap
        Dim b As New Bitmap(300, 200, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(b)
            g.Clear(Color.White)
        End Using
        DrawingRenderer.ClearCache()
        DrawingRenderer.DrawLayers(b, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {d}})
        Return b
    End Function

    Private Function Near(a As Color, b As Color, tol As Integer) As Boolean
        Return Math.Abs(CInt(a.R) - b.R) <= tol AndAlso Math.Abs(CInt(a.G) - b.G) <= tol AndAlso Math.Abs(CInt(a.B) - b.B) <= tol
    End Function

    Private Function Box() As DrawLayer
        ' 方塊：x 0.25..1.25、y 0..1（照片高度 200 像素 → 像素 50..250、0..200）
        Return New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.75, .Y = 0.5, .W = 1.0, .H = 1.0, .Filled = True, .Stroked = False, .Seed = 1,
                                   .FillColorArgb = Color.White.ToArgb()}
    End Function

    Sub MeshFillTestsRun()
        Console.WriteLine("— 網格填色、互動式填色 —")
        Dim m = MeshFill.Create(3, 3, Color.White.ToArgb())
        Check("3×3 網格：9 個節點、規則排列", m.Count = 9 AndAlso m.PointAt(4) = New PointF(0.5F, 0.5F) AndAlso m.PointAt(8) = New PointF(1, 1))
        m.Colors(0) = Color.Red.ToArgb() : m.Colors(2) = Color.Blue.ToArgb() : m.Colors(6) = Color.Lime.ToArgb() : m.Colors(8) = Color.Yellow.ToArgb()
        m.Colors(4) = Color.Black.ToArgb()
        Dim c00 = m.ColorAtParam(0, 0), c11 = m.ColorAtParam(1, 1)
        Check("平滑內插會經過節點本身的顏色", c00.R = 255 AndAlso c00.G = 0 AndAlso c11.R = 0 AndAlso c11.G = 0 AndAlso c11.B = 0)
        Dim d = Box()
        d.FillMesh = m
        Using bmp = Render(d)
            Dim tl = bmp.GetPixel(52, 2), tr = bmp.GetPixel(247, 2), mid = bmp.GetPixel(150, 100), bl = bmp.GetPixel(52, 197)
            Check("畫出來：四角是節點的顏色、中間黑", Near(tl, Color.Red, 30) AndAlso Near(tr, Color.Blue, 30) AndAlso Near(bl, Color.Lime, 40) AndAlso Near(mid, Color.Black, 25),
                  $"左上 {tl} 右上 {tr} 左下 {bl} 中 {mid}")
            Dim q = bmp.GetPixel(100, 50)
            Check("中間的過渡：介於紅與黑之間（平滑，不是一塊一塊）", q.R > 40 AndAlso q.R < 230, $"{q}")
        End Using
        ' 拖動中間節點往左：黑色跟著往左
        Dim moved = d.Clone()
        moved.FillMesh.SetPoint(4, New PointF(0.25F, 0.5F))
        Using bmp = Render(moved)
            Dim leftQ = bmp.GetPixel(100, 100), rightQ = bmp.GetPixel(200, 100)
            Check("拖中間的節點往左：左邊變暗、右邊變亮", Luma(leftQ) < 60 AndAlso Luma(rightQ) > Luma(leftQ) + 40, $"左 {leftQ} 右 {rightQ}")
        End Using
        ' 圖形移動：網格跟著走
        Dim shifted = d.Clone()
        shifted.X += 0.1
        Using bmp = Render(shifted)
            Check("圖形往右移：中間的黑色也跟著往右 20 像素", Near(bmp.GetPixel(170, 100), Color.Black, 25), $"{bmp.GetPixel(170, 100)}")
        End Using
        Check("複製圖層是深拷貝（改複本不影響原本的網格）", d.FillMesh.PointAt(4) = New PointF(0.5F, 0.5F))
        ' 改列數、欄數
        Dim rs = m.Resample(5, 5)
        Dim center = Color.FromArgb(rs.Colors(12)), corner = Color.FromArgb(rs.Colors(0))
        Check("改成 5×5：四角與中間的顏色保留", rs.Count = 25 AndAlso Near(corner, Color.Red, 2) AndAlso Near(center, Color.Black, 2) AndAlso rs.PointAt(12) = New PointF(0.5F, 0.5F), $"{rs.Count} 角 {corner} 中 {center} 點 {rs.PointAt(12)}")
        ' 網格沒蓋到的地方：鋪平均色，不透明
        Dim shrunk = d.Clone()
        For i = 0 To 8
            Dim p = shrunk.FillMesh.PointAt(i)
            shrunk.FillMesh.SetPoint(i, New PointF(0.3F + p.X * 0.4F, 0.3F + p.Y * 0.4F))
        Next
        Using bmp = Render(shrunk)
            Dim edge = bmp.GetPixel(55, 5)
            Check("網格縮到圖形裡面：外圍鋪平均色（不是透明露出白底）", Not Near(edge, Color.White, 10), $"{edge}")
        End Using

        ' 互動式填色：中間色標
        Dim gd = Box()
        gd.FillColorArgb = Color.Red.ToArgb() : gd.FillColor2Argb = Color.Blue.ToArgb()
        gd.FillGradient = New GradientFill With {.Kind = GradientKind.Linear, .X1 = 0, .Y1 = 0.5, .X2 = 1, .Y2 = 0.5, .Dither = False,
                                                 .Stops = New List(Of GradientStop) From {New GradientStop With {.Position = 0.5, .Argb = Color.Lime.ToArgb()}}}
        Using bmp = Render(gd)
            Dim mid = bmp.GetPixel(150, 100), quarter = bmp.GetPixel(100, 100)
            Check("色標：漸層中間變綠、1/4 處介於紅綠之間", Near(mid, Color.Lime, 12) AndAlso quarter.R > 80 AndAlso quarter.G > 80 AndAlso quarter.B < 40, $"中 {mid} 1/4 {quarter}")
        End Using
        Dim at = DrawingRenderer.GradientColorAt(gd, 0.75)
        Check("GradientColorAt：3/4 處介於綠藍之間", at.G > 80 AndAlso at.B > 80 AndAlso at.R < 40, $"{at}")
        Dim cl = gd.Clone()
        cl.FillGradient.Stops(0).Position = 0.2
        Check("複製圖層：色標也是深拷貝", gd.FillGradient.Stops(0).Position = 0.5)

        ' SVG 與存檔
        Dim svg = SvgExport.Export(New EditRecipe With {.Drawings = New List(Of DrawLayer) From {gd, d}}, Nothing, 300, 200)
        Dim stopCount = (svg.Length - svg.Replace("<stop ", "").Length) \ 6
        Check("SVG：色標變成 3 個 stop；網格填色用平均色", stopCount = 3 AndAlso svg.Contains(SvgHex(m.AverageColor())), $"{stopCount} 個 stop")
        Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {gd, d}}
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(r))
        Check("存檔讀回：色標與網格（節點、顏色）都在", back.Drawings(0).FillGradient.Stops.Count = 1 AndAlso back.Drawings(1).FillMesh IsNot Nothing AndAlso
              back.Drawings(1).FillMesh.Count = 9 AndAlso back.Drawings(1).FillMesh.Colors(4) = Color.Black.ToArgb())
    End Sub

    Private Function Luma(c As Color) As Double
        Return 0.3 * c.R + 0.59 * c.G + 0.11 * c.B
    End Function

    Private Function SvgHex(argb As Integer) As String
        Dim c = Color.FromArgb(argb)
        Return $"#{c.R:X2}{c.G:X2}{c.B:X2}"
    End Function
End Module
