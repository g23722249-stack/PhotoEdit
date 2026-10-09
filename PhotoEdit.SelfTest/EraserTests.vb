Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>橡皮擦的擦法：擦掉（透明）、漂白（往白）、加深（往黑）；漂白、加深不改透明度。</summary>
Module EraserTests

    Private Function Layer(mode As EraseMode, Optional strokes As Integer = 1) As DrawLayer
        Dim ops As New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.75, .Y = 0.5, .W = 1.2, .H = 0.8, .Filled = True, .Stroked = False,
                                .FillColorArgb = Color.FromArgb(200, 60, 60).ToArgb(), .Seed = 1}}
        For k = 1 To strokes
            ops.Add(New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.HardRound, .StrokeWidth = 0.3, .Eraser = True, .EraseMode = mode, .Seed = 2,
                                        .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = New List(Of DrawPoint) From {New DrawPoint(0.5F, 0.5F), New DrawPoint(1.0F, 0.5F)}}}})
        Next
        Return New DrawLayer With {.Shape = DrawShape.Raster, .Ops = ops}
    End Function

    Private Function Render(l As DrawLayer) As Bitmap
        DrawingRenderer.ClearCache()
        Dim bmp As New Bitmap(300, 200, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.FromArgb(0, 0, 0, 0))
        End Using
        DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {l}})
        Return bmp
    End Function

    Sub EraserTestsRun()
        Console.WriteLine("橡皮擦")
        Using clear = Render(Layer(EraseMode.Clear))
            Check("擦掉：擦過的地方變透明、旁邊不變", clear.GetPixel(112, 100).A = 0 AndAlso clear.GetPixel(30, 30).A = 255)
        End Using
        Using bleach = Render(Layer(EraseMode.Bleach)), bleach3 = Render(Layer(EraseMode.Bleach, 3))
            Dim p = bleach.GetPixel(112, 100), p3 = bleach3.GetPixel(112, 100)
            Check("漂白：變淡（往白色）、透明度不變", p.A = 255 AndAlso p.G > 100 AndAlso p.R > 200, p.ToString())
            Check("漂白：多擦幾次越來越淡", p3.G > p.G + 40, $"{p.G} → {p3.G}")
            Check("漂白：沒擦到的地方不變", bleach.GetPixel(30, 30) = Color.FromArgb(200, 60, 60))
        End Using
        Using darken = Render(Layer(EraseMode.Darken))
            Dim p = darken.GetPixel(112, 100)
            Check("加深：變暗、透明度不變", p.A = 255 AndAlso p.R < 130 AndAlso p.G < 40, p.ToString())
        End Using
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of DrawLayer)(System.Text.Json.JsonSerializer.Serialize(Layer(EraseMode.Darken)))
        Check("擦法存得回來", back.Ops(1).EraseMode = EraseMode.Darken)
    End Sub
End Module
