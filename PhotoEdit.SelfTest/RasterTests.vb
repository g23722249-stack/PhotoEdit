Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>直接繪製：點陣圖層的合成、橡皮擦、位移、點陣化、快取與存檔。</summary>
Module RasterTests

    Private Function StrokeOp(x0 As Single, x1 As Single, y As Single, color As Color, Optional eraser As Boolean = False, Optional width As Double = 0.08) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.HardRound, .StrokeColorArgb = color.ToArgb(), .StrokeWidth = width,
                                   .Eraser = eraser, .Seed = 3,
                                   .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = New List(Of DrawPoint) From {New DrawPoint(x0, y), New DrawPoint(x1, y)}}}}
    End Function

    Private Function RenderOn(layers As List(Of DrawLayer), Optional w As Integer = 200, Optional h As Integer = 100) As Bitmap
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
        End Using
        DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = layers})
        Return bmp
    End Function

    Private Function IsRed(c As Color) As Boolean
        Return c.R > 200 AndAlso c.G < 60 AndAlso c.B < 60
    End Function

    Private Function IsWhite(c As Color) As Boolean
        Return c.R > 245 AndAlso c.G > 245 AndAlso c.B > 245
    End Function

    Sub RasterTestsRun()
        Console.WriteLine("直接繪製")
        DrawingRenderer.ClearCache()

        ' 一條紅線（y = 0.5，x 0.2..1.8，畫布 200 × 100 → 照片高度 = 100 像素）
        Dim layer As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {StrokeOp(0.2F, 1.8F, 0.5F, Color.Red)}}
        Using bmp = RenderOn(New List(Of DrawLayer) From {layer})
            Check("點陣圖層：畫上的紅線看得到", IsRed(bmp.GetPixel(50, 50)) AndAlso IsRed(bmp.GetPixel(150, 50)))
            Check("點陣圖層：沒畫到的地方不變", IsWhite(bmp.GetPixel(100, 10)))
        End Using

        ' 橡皮擦擦掉左半邊
        layer.Ops.Add(StrokeOp(0.1F, 1.0F, 0.5F, Color.Black, eraser:=True, width:=0.2))
        Using bmp = RenderOn(New List(Of DrawLayer) From {layer})
            Check("橡皮擦：擦過的地方露出底下的照片", IsWhite(bmp.GetPixel(50, 50)))
            Check("橡皮擦：沒擦到的地方還在", IsRed(bmp.GetPixel(150, 50)))
            Check("橡皮擦本身不會畫出顏色（黑色筆刷也不會留下黑）", bmp.GetPixel(60, 50).R > 200)
        End Using

        ' 擦完再畫：新的一筆蓋在上面
        layer.Ops.Add(StrokeOp(0.3F, 0.7F, 0.5F, Color.Blue, width:=0.04))
        Using bmp = RenderOn(New List(Of DrawLayer) From {layer})
            Dim p = bmp.GetPixel(50, 50)
            Check("擦完再畫：新筆畫畫得上去", p.B > 200 AndAlso p.R < 60)
        End Using

        ' 快取：接著畫的結果和從頭算一樣
        Dim incremental As Bitmap = RenderOn(New List(Of DrawLayer) From {layer})
        DrawingRenderer.ClearCache()
        Using fresh = RenderOn(New List(Of DrawLayer) From {layer})
            Dim same = True
            For y = 0 To 99 Step 3
                For x = 0 To 199 Step 3
                    If incremental.GetPixel(x, y) <> fresh.GetPixel(x, y) Then same = False
                Next
            Next
            Check("快取：一筆一筆接著畫，和從頭合成的結果相同", same)
        End Using
        incremental.Dispose()

        ' 復原（少一筆）：回到前一個結果
        Dim undone = layer.Clone()
        undone.Ops.RemoveAt(undone.Ops.Count - 1)
        Using bmp = RenderOn(New List(Of DrawLayer) From {undone})
            Check("復原最後一筆：藍線不見、擦過的地方仍是白的", IsWhite(bmp.GetPixel(50, 50)))
        End Using

        ' 位移：整層移動
        Dim moved = New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {StrokeOp(0.2F, 1.8F, 0.3F, Color.Red, width:=0.06)}}
        DrawGeometry.Offset(moved, 0, 0.4)
        Using bmp = RenderOn(New List(Of DrawLayer) From {moved})
            Check("移動點陣圖層：紅線跟著往下", IsRed(bmp.GetPixel(100, 70)) AndAlso IsWhite(bmp.GetPixel(100, 30)))
        End Using
        Dim rb = DrawGeometry.RasterBounds(moved)
        Check("點陣圖層範圍含線寬與位移", Math.Abs(rb.Top - 0.67) < 0.002 AndAlso Math.Abs(rb.Bottom - 0.73) < 0.002 AndAlso Math.Abs(rb.Left - 0.17) < 0.002,
              rb.ToString())
        Check("點選：範圍內點得到、範圍外點不到", DrawGeometry.HitTest(moved, New PointF(1.0F, 0.7F), 0) AndAlso Not DrawGeometry.HitTest(moved, New PointF(1.0F, 0.3F), 0))
        Check("點陣圖層沒有方框控制點", Not DrawGeometry.IsBox(DrawShape.Raster))
        Dim empty As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer)()}
        Check("空白點陣圖層：範圍為空、點不到", DrawGeometry.RasterBounds(empty).IsEmpty AndAlso Not DrawGeometry.HitTest(empty, New PointF(0.5F, 0.5F), 0.1))
        Using bmp = RenderOn(New List(Of DrawLayer) From {empty})
            Check("空白點陣圖層：照片不變", IsWhite(bmp.GetPixel(100, 50)))
        End Using

        ' 點陣化：外觀不變，之後可以擦
        Dim rect As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 1, .Y = 0.5, .W = 1, .H = 0.6, .Filled = True,
                                        .FillColorArgb = Color.Red.ToArgb(), .StrokeColorArgb = Color.Red.ToArgb(), .Opacity = 60, .Name = "矩形 1", .Seed = 4}
        Dim raster = DrawGeometry.Rasterize(rect)
        Check("點陣化：變成點陣圖層，名稱與不透明度保留", raster.Shape = DrawShape.Raster AndAlso raster.Name = "矩形 1" AndAlso raster.Opacity = 60 AndAlso
              raster.Ops.Count = 1 AndAlso raster.Ops(0).Opacity = 100)
        Using a = RenderOn(New List(Of DrawLayer) From {rect}), b = RenderOn(New List(Of DrawLayer) From {raster})
            Dim diff = 0
            For y = 0 To 99 Step 2
                For x = 0 To 199 Step 2
                    Dim p = a.GetPixel(x, y), q = b.GetPixel(x, y)
                    diff = Math.Max(diff, Math.Abs(CInt(p.R) - q.R) + Math.Abs(CInt(p.G) - q.G) + Math.Abs(CInt(p.B) - q.B))
                Next
            Next
            Check("點陣化前後看起來一樣", diff <= 6, diff.ToString())
        End Using
        raster.Ops.Add(StrokeOp(0.6F, 1.4F, 0.5F, Color.Black, eraser:=True, width:=0.2))
        Using bmp = RenderOn(New List(Of DrawLayer) From {raster})
            Check("點陣化後可以擦掉圖形的一部分", IsWhite(bmp.GetPixel(100, 50)) AndAlso Not IsWhite(bmp.GetPixel(100, 30)))
        End Using

        ' 存檔與複製
        Dim recipe As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {layer}}
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(recipe))
        Check("點陣圖層 JSON 來回轉換一致（含橡皮擦）", back.Equals(recipe) AndAlso back.Drawings(0).Ops.Count = 3 AndAlso back.Drawings(0).Ops(1).Eraser)
        Dim copy = recipe.Clone()
        copy.Drawings(0).Ops(0).Strokes(0).Points(0).X = 0.9F
        Check("Clone 為深複製（點陣圖層的筆畫不共用）", recipe.Drawings(0).Ops(0).Strokes(0).Points(0).X = 0.2F)

        ' 效能：60 筆的圖層，加一筆時只算新的那筆
        Dim many As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer)()}
        For i = 0 To 59
            many.Ops.Add(StrokeOp(0.1F + i * 0.02F, 0.5F + i * 0.02F, 0.1F + (i Mod 9) * 0.1F, Color.FromArgb(255, i * 4, 100, 200), width:=0.03))
        Next
        Using bmp = RenderOn(New List(Of DrawLayer) From {many}, 1200, 800)
        End Using
        many.Ops.Add(StrokeOp(0.2F, 1.2F, 0.5F, Color.Red, width:=0.03))
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Using bmp = RenderOn(New List(Of DrawLayer) From {many}, 1200, 800)
        End Using
        sw.Stop()
        Console.WriteLine($"    （1200×800、61 筆，加一筆的合成時間 {sw.ElapsedMilliseconds} ms）")
        Check("加一筆只算新的那筆（1200×800 不到 400 ms）", sw.ElapsedMilliseconds < 400, sw.ElapsedMilliseconds & " ms")
    End Sub
End Module
