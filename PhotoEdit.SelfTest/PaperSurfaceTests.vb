Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>表面紋理作用在「紙張」：顏料蓋住的地方紋理變淡。</summary>
Module PaperSurfaceTests

    ''' <summary>畫面左半邊（x 0..0.45）與右半邊（x 0.55..1）的亮度起伏（標準差）。</summary>
    Private Function Ripple(b As Bitmap, x0 As Double, x1 As Double) As Double
        Dim vals As New List(Of Double)()
        For y = 20 To b.Height - 21 Step 2
            For x = CInt(b.Width * x0) To CInt(b.Width * x1) Step 2
                Dim c = b.GetPixel(x, y)
                vals.Add(0.3 * c.R + 0.59 * c.G + 0.11 * c.B)
            Next
        Next
        Dim m = vals.Average()
        Return Math.Sqrt(vals.Sum(Function(v) (v - m) ^ 2) / vals.Count)
    End Function

    Private Function RenderWith(target As SurfaceTarget, onPaint As Integer, paintOpacity As Integer, Optional raster As Boolean = False, Optional smudge As Boolean = False) As Bitmap
        Using src As New Bitmap(300, 200, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(src)
                g.Clear(Color.FromArgb(200, 200, 200))
            End Using
            ' 右半邊一塊顏料（同樣的灰，只看紋理）
            Dim paint As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 1.125, .Y = 0.5, .W = 0.75, .H = 1.2, .Filled = True, .Stroked = False, .Seed = 1,
                                             .FillColorArgb = Color.FromArgb(200, 200, 200).ToArgb(), .Opacity = paintOpacity}
            If raster Then paint = New DrawLayer With {.Shape = DrawShape.Raster, .Opacity = paintOpacity, .Ops = New List(Of DrawLayer) From {paint.Clone()}}
            If raster Then paint.Ops(0).Opacity = 100
            If smudge Then
                ' 塗抹筆從沒畫的紙上拖到顏料上（讀到的畫布不該有紋理）
                paint.Ops.Add(New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.Smudge, .StrokeWidth = 0.4, .Seed = 3, .Wet = 90,
                                                  .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = New List(Of DrawPoint) From {New DrawPoint(0.1F, 0.5F), New DrawPoint(1.4F, 0.5F)}}}})
            End If
            Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {paint},
                                          .Paper = New PaperSettings With {.Kind = PaperKind.Canvas, .SurfaceStrength = 70, .SurfaceTarget = target, .SurfaceOnPaint = onPaint}}
            DrawingRenderer.ClearCache()
            Return ImagePipeline.Render(src, r)
        End Using
    End Function

    Sub PaperSurfaceTestsRun()
        Console.WriteLine("— 表面紋理：紙張（顏料蓋住變淡） —")
        Using whole = RenderWith(SurfaceTarget.Photo, 20, 100)
            Dim bare = Ripple(whole, 0.02, 0.45), painted = Ripple(whole, 0.6, 0.98)
            Check("整張照片：顏料上的紋理和紙上一樣", bare > 3 AndAlso Math.Abs(painted - bare) < bare * 0.25, $"紙 {bare:0.00}、顏料 {painted:0.00}")
        End Using
        Using base = RenderWith(SurfaceTarget.Base, 20, 100)
            Dim bare = Ripple(base, 0.02, 0.45), painted = Ripple(base, 0.6, 0.98)
            Check("紙張：沒畫的地方紋理照舊、不透明顏料上只剩約 20%", bare > 3 AndAlso painted > bare * 0.1 AndAlso painted < bare * 0.32, $"紙 {bare:0.00}、顏料 {painted:0.00}")
        End Using
        Using base0 = RenderWith(SurfaceTarget.Base, 0, 100)
            Dim bare = Ripple(base0, 0.02, 0.45), painted = Ripple(base0, 0.6, 0.98)
            Check("紙張、顏料上的紋理 0：顏料完全蓋住紋理", painted < bare * 0.05, $"紙 {bare:0.00}、顏料 {painted:0.00}")
        End Using
        Using thin = RenderWith(SurfaceTarget.Base, 0, 50)
            Dim bare = Ripple(thin, 0.02, 0.45), painted = Ripple(thin, 0.6, 0.98)
            Check("紙張：半透明的顏料（50%）透出一半左右的紋理", painted > bare * 0.35 AndAlso painted < bare * 0.65, $"紙 {bare:0.00}、顏料 {painted:0.00}")
        End Using
        Using rs = RenderWith(SurfaceTarget.Base, 20, 100, raster:=True)
            Dim bare = Ripple(rs, 0.02, 0.45), painted = Ripple(rs, 0.6, 0.98)
            Check("紙張、點陣圖層的顏料：一樣只剩約 20% 的紋理", painted > bare * 0.1 AndAlso painted < bare * 0.32, $"紙 {bare:0.00}、顏料 {painted:0.00}")
        End Using
        Using sm = RenderWith(SurfaceTarget.Base, 20, 100, raster:=True, smudge:=True)
            ' 塗抹筆拖過的顏料區（右半、筆畫經過的那條）
            Dim bare = Ripple(sm, 0.02, 0.15), painted = Ripple(sm, 0.6, 0.98)
            Check("紙張：塗抹筆拖過，顏料上沒有被推進來的紋理（仍只剩約 20%）", painted < bare * 0.32, $"紙 {bare:0.00}、顏料 {painted:0.00}")
        End Using
        Dim p As New PaperSettings With {.SurfaceTarget = SurfaceTarget.Base, .SurfaceOnPaint = 35}
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of PaperSettings)(System.Text.Json.JsonSerializer.Serialize(p))
        Check("存檔讀回：作用在「紙張」、顏料上的紋理 35%", back.SurfaceTarget = SurfaceTarget.Base AndAlso back.SurfaceOnPaint = 35 AndAlso New PaperSettings().SurfaceOnPaint = 20)
    End Sub
End Module
