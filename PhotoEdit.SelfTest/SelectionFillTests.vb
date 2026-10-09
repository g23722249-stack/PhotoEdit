Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>選取區填滿：內容感知（附近補滿、擴散備案）、材質、保留透明度，以及存檔。</summary>
Module SelectionFillTests

    Private Function Pixels(b As Bitmap) As Byte()
        Dim data = b.LockBits(New Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            Dim px(b.Width * b.Height * 4 - 1) As Byte
            For y = 0 To b.Height - 1
                Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * b.Width * 4, b.Width * 4)
            Next
            Return px
        Finally
            b.UnlockBits(data)
        End Try
    End Function

    ''' <summary>橫條紋（每 12 像素一個循環）的照片。</summary>
    Private Function Stripes(w As Integer, h As Integer) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        For y = 0 To h - 1
            Dim v = CInt(128 + 90 * Math.Sin(y * 2 * Math.PI / 12))
            For x = 0 To w - 1
                b.SetPixel(x, y, Color.FromArgb(v, CInt(v * 0.8), 60))
            Next
        Next
        Return b
    End Function

    Private Function RectSelection(x As Double, y As Double, w As Double, h As Double) As SelectionSpec
        Return New SelectionSpec With {.Ops = New List(Of SelectionOp) From {
            New SelectionOp With {.Mode = SelectionMode.Replace, .Shape = SelectionShape.Rectangle, .X = x, .Y = y, .W = w, .H = h}}}
    End Function

    Sub SelectionFillTestsRun()
        Console.WriteLine("選取區填滿")
        ' ---- 內容感知：橫條紋上的紅色方塊 ----
        Using clean = Stripes(240, 160), dirty = Stripes(240, 160)
            Using g = Graphics.FromImage(dirty)
                g.FillRectangle(Brushes.Red, 100, 60, 30, 30)
            End Using
            Dim mask(240 * 160 - 1) As Byte
            For y = 56 To 93
                For x = 96 To 133
                    mask(y * 240 + x) = 255
                Next
            Next
            Dim patch = ContentFill.Fill(Pixels(dirty), 240, 160, mask)
            Dim cleanPx = Pixels(clean)
            Dim diff = 0.0, cnt = 0, red = 0
            For y = 60 To 89
                For x = 100 To 129
                    Dim i = (y * 240 + x) * 4
                    diff += Math.Abs(CInt(patch(i + 2)) - cleanPx(i + 2)) : cnt += 1
                    If patch(i + 2) > 200 AndAlso patch(i + 1) < 60 Then red += 1
                Next
            Next
            Check("內容感知：紅色方塊不見了", red = 0 AndAlso patch((75 * 240 + 115) * 4 + 3) = 255)
            Check("內容感知：補上的地方接續條紋（和原本差很少）", diff / cnt < 12, (diff / cnt).ToString("0.0"))
            Check("內容感知：選取範圍外不動（透明）", patch((10 * 240 + 10) * 4 + 3) = 0)
        End Using
        ' 洞幾乎佔滿整張：找不到來源，改用擴散補色（不會留洞）
        Using flat As New Bitmap(60, 40, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(flat)
                g.Clear(Color.FromArgb(40, 160, 90))
            End Using
            Dim mask(60 * 40 - 1) As Byte
            For y = 2 To 37
                For x = 2 To 57
                    mask(y * 60 + x) = 255
                Next
            Next
            Dim patch = ContentFill.Fill(Pixels(flat), 60, 40, mask)
            Dim c = (30 * 60 + 20) * 4
            Check("大洞找不到來源：用周圍的顏色擴散補滿", patch(c + 3) = 255 AndAlso Math.Abs(CInt(patch(c + 1)) - 160) < 6)
        End Using
        Check("沒有選取：不補", ContentFill.Fill(New Byte(16 - 1) {}, 2, 2, New Byte(3) {}) Is Nothing)

        ' ---- 點陣圖層裡的選取區填滿 ----
        DrawingRenderer.ClearCache()
        Dim aware As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Raster, .Region = RectSelection(0.38, 0.33, 0.2, 0.3), .Filled = True, .Stroked = False,
                                .FillContent = FillContent.ContentAware, .Opacity = 100, .Param1 = 1.5}}}
        Using photo = Stripes(240, 160)
            Using g = Graphics.FromImage(photo)
                g.FillRectangle(Brushes.Red, 100, 60, 30, 30)
            End Using
            DrawingRenderer.DrawLayers(photo, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {aware}})
            Dim p = photo.GetPixel(115, 75)
            Check("內容感知填滿圖層：照片上的紅色方塊被蓋掉", Not (p.R > 200 AndAlso p.G < 60), p.ToString())
        End Using

        DrawingRenderer.ClearCache()
        Dim material As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Raster, .Region = RectSelection(0.25, 0.25, 0.5, 0.5), .Filled = True, .Stroked = False,
                                .FillContent = FillContent.Material, .Material = MaterialKind.Wood, .FillColorArgb = Color.SaddleBrown.ToArgb(), .Opacity = 100, .Param1 = 1.5}}}
        Using bmp As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.White)
            End Using
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {material}})
            Dim tones = Enumerable.Range(0, 60).Select(Function(k) bmp.GetPixel(90 + k, 80).R).Distinct().Count()
            Check("填材質：選取範圍裡是木紋、外面不變", tones > 8 AndAlso bmp.GetPixel(10, 10) = Color.FromArgb(255, 255, 255), tones.ToString())
        End Using

        ' ---- 保留透明度：圓形上面畫漸層，圓外仍透明 ----
        DrawingRenderer.ClearCache()
        Dim keep As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.75, .Y = 0.5, .W = 0.5, .H = 0.5, .Filled = True, .Stroked = False, .FillColorArgb = Color.Black.ToArgb(), .Seed = 1},
            New DrawLayer With {.Shape = DrawShape.Gradient, .StrokeColorArgb = Color.Red.ToArgb(), .FillColorArgb = Color.Yellow.ToArgb(), .KeepAlpha = True, .Param1 = 1.5,
                                .Gradient = New GradientFill With {.X1 = 0, .Y1 = 0.5, .X2 = 1.5, .Y2 = 0.5, .Dither = False}}}}
        Using bmp As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.White)
            End Using
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {keep}})
            Dim inside = bmp.GetPixel(120, 80)
            Check("保留透明度：圓裡換成漸層的顏色、圓外仍是原本（透明）", inside.R > 200 AndAlso inside.G > 60 AndAlso bmp.GetPixel(10, 80) = Color.FromArgb(255, 255, 255),
                  $"{inside} {bmp.GetPixel(10, 80)}")
        End Using

        ' ---- 填滿圖層（非破壞性）----
        DrawingRenderer.ClearCache()
        Dim solid As New DrawLayer With {.Shape = DrawShape.FillLayer, .FillColorArgb = Color.FromArgb(30, 160, 90).ToArgb(), .Opacity = 50,
                                         .Region = RectSelection(0.5, 0, 0.5, 1), .Param1 = 1.5}
        Using bmp As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.White)
            End Using
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {solid}})
            Dim right = bmp.GetPixel(200, 80)
            Check("填滿圖層（單色、50%、遮色片右半）：右半淡綠、左半不變", Math.Abs(CInt(right.R) - 142) < 4 AndAlso Math.Abs(CInt(right.G) - 207) < 4 AndAlso
                  bmp.GetPixel(40, 80) = Color.FromArgb(255, 255, 255), right.ToString())
        End Using
        solid.FillColorArgb = Color.FromArgb(200, 40, 40).ToArgb() : solid.Opacity = 100 : solid.Region = Nothing
        Using bmp As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {solid}})
            Check("改顏色、拿掉遮色片：整張變成新顏色（非破壞性，隨時可改）", bmp.GetPixel(40, 80) = Color.FromArgb(200, 40, 40) AndAlso bmp.GetPixel(200, 80) = Color.FromArgb(200, 40, 40))
        End Using
        Dim gradLayer As New DrawLayer With {.Shape = DrawShape.FillLayer, .FillColorArgb = Color.Black.ToArgb(), .StrokeColorArgb = Color.White.ToArgb(), .Opacity = 100,
                                             .Gradient = New GradientFill With {.X1 = 0, .Y1 = 0, .X2 = 0, .Y2 = 1, .Dither = False}}
        Using bmp As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {gradLayer}})
            Check("填滿圖層（漸層）：上黑下白", bmp.GetPixel(100, 1).R < 10 AndAlso bmp.GetPixel(100, 158).R > 245)
        End Using
        Dim matLayer As New DrawLayer With {.Shape = DrawShape.FillLayer, .FillContent = FillContent.Material, .Material = MaterialKind.Marble,
                                            .FillColorArgb = Color.Gray.ToArgb(), .Opacity = 100, .Seed = 3}
        Using bmp As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {matLayer}})
            Dim tones = Enumerable.Range(0, 100).Select(Function(k) bmp.GetPixel(20 + k, 80).R).Distinct().Count()
            Check("填滿圖層（材質）：整張大理石紋", tones > 8 AndAlso bmp.GetPixel(5, 5).A = 255, tones.ToString())
        End Using
        Check("工具名稱 22 個（含填滿圖層）", DrawGeometry.ShapeNames.Length = 22 AndAlso DrawGeometry.ShapeNames(21) = "填滿圖層")

        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of DrawLayer)(System.Text.Json.JsonSerializer.Serialize(keep))
        Check("填滿內容與保留透明度存得回來", back.Ops(1).KeepAlpha AndAlso
              System.Text.Json.JsonSerializer.Deserialize(Of DrawLayer)(System.Text.Json.JsonSerializer.Serialize(material)).Ops(0).FillContent = FillContent.Material)
    End Sub
End Module
