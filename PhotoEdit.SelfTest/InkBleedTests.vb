Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>暈開：毛筆的墨滲到筆畫外、比較淡；沒有暈開時不變；不會暈開的筆沒作用；存得回來。</summary>
Module InkBleedTests

    Private Function Paint(brush As BrushKind, bleed As Integer, Optional paper As PaperSettings = Nothing) As Bitmap
        Dim stroke As New DrawStroke()
        For i = 0 To 30
            stroke.Points.Add(New DrawPoint(0.2F + i * 0.04F, 0.5F, 0.9F))
        Next
        Dim layer As New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = brush, .StrokeColorArgb = Color.FromArgb(30, 30, 30).ToArgb(),
                                         .StrokeWidth = 0.06, .Seed = 5, .Bleed = bleed, .Strokes = New List(Of DrawStroke) From {stroke}}
        Dim b As New Bitmap(300, 200, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(b)
            g.Clear(Color.White)
        End Using
        DrawingRenderer.ClearCache()
        DrawingRenderer.DrawLayers(b, New EditRecipe With {.Paper = paper, .Drawings = New List(Of DrawLayer) From {layer}})
        Return b
    End Function

    ''' <summary>從筆畫中線往下，第一個接近白色的列（墨的下緣）。</summary>
    Private Function InkEdge(b As Bitmap) As Integer
        For y = 100 To b.Height - 1
            Dim c = b.GetPixel(150, y)
            If c.R > 248 AndAlso c.G > 248 Then Return y
        Next
        Return b.Height
    End Function

    Sub InkBleedTestsRun()
        Console.WriteLine("暈開")
        Using dry = Paint(BrushKind.ChineseBrush, 0), wet = Paint(BrushKind.ChineseBrush, 70)
            Dim e0 = InkEdge(dry), e1 = InkEdge(wet)
            Check("毛筆暈開 70：墨滲到筆畫外（下緣往外擴）", e1 >= e0 + 3, $"乾 {e0}、濕 {e1}")
            Dim halo = wet.GetPixel(150, e0 + 1), core = wet.GetPixel(150, 100)
            Check("滲出去的墨比筆畫中間淡", halo.R > core.R + 40, $"外 {halo} 內 {core}")
        End Using
        Using a = Paint(BrushKind.ChineseBrush, 0), b = Paint(BrushKind.ChineseBrush, 0)
            Check("沒有暈開：畫出來和原本一樣（同樣設定結果相同）", Enumerable.Range(0, 300).All(Function(x) a.GetPixel(x, 112).ToArgb() = b.GetPixel(x, 112).ToArgb()))
        End Using
        Using dry = Paint(BrushKind.HardRound, 0), wet = Paint(BrushKind.HardRound, 70)
            Check("硬筆不會暈開（暈開沒作用）", InkEdge(dry) = InkEdge(wet))
        End Using
        Using wet = Paint(BrushKind.ChineseBrush, 70, New PaperSettings With {.Kind = PaperKind.Washi})
            Check("棉紙上也畫得出暈開", InkEdge(wet) > 100)
        End Using
        Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.ChineseBrush, .Bleed = 55}}}
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(r))
        Check("暈開存得回來", back.Drawings(0).Bleed = 55)
        Dim style As New DrawLayer With {.Bleed = 40}
        Dim copy As New DrawLayer()
        copy.CopyStyleFrom(style)
        Check("複製筆觸設定時暈開跟著帶過去；設定不同就不是同一種筆觸", copy.Bleed = 40 AndAlso Not New DrawLayer().SameStroke(style))

        Console.WriteLine("文字樣式")
        Dim t As New DrawLayer With {.TextBold = True, .TextItalic = True}
        Check("粗體＋斜體 → 字型樣式兩個都有", t.TextFontStyle = (FontStyle.Bold Or FontStyle.Italic))
        Check("一般 → Regular", New DrawLayer With {.TextBold = False}.TextFontStyle = FontStyle.Regular)
        Dim tc As New DrawLayer()
        tc.CopyStyleFrom(t)
        Check("複製樣式時斜體跟著帶過去", tc.TextItalic)
        Dim tr As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {New DrawLayer With {.Shape = DrawShape.Text, .Text = "Ara", .TextItalic = True}}}
        Dim tb = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(tr))
        Check("斜體存得回來", tb.Drawings(0).TextItalic)
    End Sub
End Module
