Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>紙張：紙紋高度圖、筆刷吃紙紋、表面紋理（整張／只有繪圖）、藝術風格、新影像紙張底色、配方存檔。</summary>
Module PaperTests

    Private Function Variance(a As Single()) As Double
        Dim m = a.Average(Function(v) CDbl(v))
        Return a.Average(Function(v) (v - m) * (v - m))
    End Function

    Private Function Solid(w As Integer, h As Integer, c As Color) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(b)
            g.Clear(c)
        End Using
        Return b
    End Function

    ''' <summary>畫面上不是白色的像素比例。</summary>
    Private Function Inked(b As Bitmap, y0 As Integer, y1 As Integer) As Double
        Dim n = 0, total = 0
        For y = y0 To y1
            For x = 20 To b.Width - 21
                total += 1
                If b.GetPixel(x, y).R < 200 Then n += 1
            Next
        Next
        Return n / CDbl(total)
    End Function

    Private Function Pencil(y As Single, pressure As Single) As DrawLayer
        Dim pts = Enumerable.Range(0, 21).Select(Function(i) New DrawPoint(0.1F + i * 0.09F, y, pressure)).ToList()
        Return New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.Charcoal, .StrokeColorArgb = Color.Black.ToArgb(), .StrokeWidth = 0.08,
                                   .Seed = 4, .PaperGrain = 100, .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}}
    End Function

    Sub PaperTestsRun()
        Console.WriteLine("紙張")
        Check("17 種紙張都有名稱與底色", Papers.Names.Length = 17 AndAlso Papers.BaseColors.Length = 17 AndAlso
              [Enum].GetValues(GetType(PaperKind)).Length = 17)

        ' ---- 高度圖 ----
        Dim flat As New List(Of String)()
        For Each kind In [Enum].GetValues(GetType(PaperKind)).Cast(Of PaperKind)()
            If kind = PaperKind.Custom Then Continue For
            Dim map = Papers.HeightMap(New PaperSettings With {.Kind = kind}, 160, 160)
            If map.Any(Function(v) v < 0 OrElse v > 1 OrElse Single.IsNaN(v)) OrElse Variance(map) < 0.002 Then flat.Add(Papers.Names(CInt(kind)))
        Next
        Check("每種紙紋都有起伏、數值在 0..1", flat.Count = 0, String.Join("、", flat))
        Dim a = Papers.HeightMap(New PaperSettings With {.Kind = PaperKind.Canvas}, 120, 120)
        Dim b = Papers.HeightMap(New PaperSettings With {.Kind = PaperKind.Canvas}, 120, 120)
        Check("同樣設定算出一樣的紙紋", a.SequenceEqual(b))
        Dim inv = Papers.HeightMap(New PaperSettings With {.Kind = PaperKind.Canvas, .Invert = True}, 120, 120)
        Check("凹凸反轉 = 1 − 高度", Enumerable.Range(0, a.Length).All(Function(i) Math.Abs(inv(i) - (1 - a(i))) < 0.0001))
        Dim lowC = Papers.HeightMap(New PaperSettings With {.Kind = PaperKind.Sketch, .Contrast = 0}, 120, 120)
        Dim highC = Papers.HeightMap(New PaperSettings With {.Kind = PaperKind.Sketch, .Contrast = 100}, 120, 120)
        Check("紙紋深淺：越深起伏越大", Variance(highC) > Variance(lowC) * 2)
        ' 紋路以照片高度為單位：同一張紙在 2 倍大的影像上，紋路也是 2 倍大（對應位置相同）
        Dim small = Papers.HeightMap(New PaperSettings With {.Kind = PaperKind.Brick}, 300, 200)
        Dim big = Papers.HeightMap(New PaperSettings With {.Kind = PaperKind.Brick}, 600, 400)
        Dim same = Enumerable.Range(0, 40).Count(Function(k) Math.Abs(small((k * 4) * 300 + k * 7) - big((k * 8) * 600 + k * 14)) < 0.05)
        Check("預覽與匯出的紙紋大小一致（以照片高度為單位）", same >= 30, same.ToString())

        ' 自訂紙紋
        Dim tmp = IO.Path.Combine(IO.Path.GetTempPath(), "photoedit_paper_test.png")
        Using t As New Bitmap(16, 16)
            For y = 0 To 15
                For x = 0 To 15
                    t.SetPixel(x, y, If((x \ 4 + y \ 4) Mod 2 = 0, Color.White, Color.Black))
                Next
            Next
            t.Save(tmp)
        End Using
        Dim custom = Papers.HeightMap(New PaperSettings With {.Kind = PaperKind.Custom, .CustomPath = tmp, .Contrast = 50}, 200, 1000)
        Check("自訂紙紋：用匯入的圖片（平鋪）", custom(0) > 0.6F AndAlso custom(4) < 0.4F AndAlso custom(16) > 0.6F, $"{custom(0):0.00} {custom(4):0.00} {custom(16):0.00}")
        IO.File.Delete(tmp)

        ' ---- 筆刷吃紙紋 ----
        DrawingRenderer.ClearCache()
        Dim rough As New PaperSettings With {.Kind = PaperKind.WatercolorRough, .Contrast = 80}
        Using noPaper = Solid(400, 200, Color.White), withPaper = Solid(400, 200, Color.White)
            DrawingRenderer.DrawLayers(noPaper, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {Pencil(0.25F, 0.3F), Pencil(0.75F, 1)}})
            DrawingRenderer.DrawLayers(withPaper, New EditRecipe With {.Paper = rough, .Drawings = New List(Of DrawLayer) From {Pencil(0.25F, 0.3F), Pencil(0.75F, 1)}})
            Dim lightPaper = Inked(withPaper, 45, 55), heavyPaper = Inked(withPaper, 145, 155)
            Check("有紙張時：輕畫只碰到紙紋凸起，用力畫填得比較滿", heavyPaper > lightPaper + 0.1, $"{lightPaper:0.00} / {heavyPaper:0.00}")
            Check("有紙張時輕畫比沒有紙張時稀疏", lightPaper < Inked(noPaper, 45, 55), $"{lightPaper:0.00} / {Inked(noPaper, 45, 55):0.00}")
        End Using
        Using a1 = Solid(400, 200, Color.White), a2 = Solid(400, 200, Color.White)
            Dim zero = Pencil(0.5F, 0.3F) : zero.PaperGrain = 0
            DrawingRenderer.DrawLayers(a1, New EditRecipe With {.Paper = rough, .Drawings = New List(Of DrawLayer) From {zero}})
            DrawingRenderer.DrawLayers(a2, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {zero}})
            Check("紙紋吃色 0：不受紙張影響", Math.Abs(Inked(a1, 95, 105) - Inked(a2, 95, 105)) < 0.02)
        End Using

        ' ---- 表面紋理 ----
        Dim canvasSurface As New PaperSettings With {.Kind = PaperKind.Canvas, .SurfaceStrength = 80}
        Using src = Solid(300, 200, Color.FromArgb(200, 180, 150))
            Dim line As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.75, .Y = 0.5, .W = 0.4, .H = 0.4, .Filled = True, .Stroked = False,
                                            .FillColorArgb = Color.SteelBlue.ToArgb(), .Seed = 2}
            Dim r As New EditRecipe With {.Paper = canvasSurface, .Drawings = New List(Of DrawLayer) From {line}}
            Using whole = ImagePipeline.Render(src, r)
                Dim lum = Enumerable.Range(0, 60).Select(Function(k) CSng(whole.GetPixel(10 + k, 40).R)).ToArray()
                Check("表面紋理（整張照片）：平的照片上出現畫布紋", Variance(lum) > 4, Variance(lum).ToString("0.0"))
            End Using
            canvasSurface.SurfaceTarget = SurfaceTarget.Drawings
            Using drawingsOnly = ImagePipeline.Render(src, r)
                Dim photoPart = Enumerable.Range(0, 60).All(Function(k) drawingsOnly.GetPixel(10 + k, 40) = Color.FromArgb(200, 180, 150))
                Dim lum = Enumerable.Range(0, 60).Select(Function(k) CSng(drawingsOnly.GetPixel(170 + k, 100).B)).ToArray()
                Check("表面紋理（只有繪圖）：照片不變", photoPart)
                Check("表面紋理（只有繪圖）：繪圖上有畫布紋", Variance(lum) > 4, Variance(lum).ToString("0.0"))
            End Using
            Check("有表面紋理時不是原圖", Not New EditRecipe With {.Paper = New PaperSettings With {.SurfaceStrength = 10}}.IsIdentity AndAlso
                  New EditRecipe With {.Paper = New PaperSettings()}.IsIdentity)
        End Using

        ' ---- 藝術風格用文件的紙 ----
        Using s1 = Solid(300, 200, Color.FromArgb(120, 160, 200)), s2 = Solid(300, 200, Color.FromArgb(120, 160, 200))
            ImagePipeline.Render(s1, New EditRecipe With {.ArtStyle = ArtStyle.Watercolor}).Dispose()
            Using r1 = ImagePipeline.Render(s1, New EditRecipe With {.ArtStyle = ArtStyle.Watercolor}),
                  r2 = ImagePipeline.Render(s2, New EditRecipe With {.ArtStyle = ArtStyle.Watercolor, .Paper = New PaperSettings With {.Kind = PaperKind.Burlap}})
                Dim diff = Enumerable.Range(0, 100).Count(Function(k) r1.GetPixel(50 + k, 100) <> r2.GetPixel(50 + k, 100))
                Check("藝術風格（水彩）改用文件的紙紋", diff > 30, diff.ToString())
            End Using
        End Using

        ' ---- 新影像與配方 ----
        Dim spec As New NewImageSpec With {.Width = 40, .Height = 30, .Fill = NewImageFill.Paper, .PaperKind = PaperKind.Kraft}
        Using img = NewImage.Create(spec)
            Check("新影像用紙張當底：牛皮紙底色", img.GetPixel(20, 15).ToArgb() = Papers.BaseColors(CInt(PaperKind.Kraft)).ToArgb())
        End Using
        Dim recipe As New EditRecipe With {.Paper = New PaperSettings With {.Kind = PaperKind.Rice, .Scale = 150, .SurfaceStrength = 30, .SurfaceTarget = SurfaceTarget.Drawings}}
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(recipe))
        Check("紙張存得回來", back.Equals(recipe) AndAlso back.Paper.Kind = PaperKind.Rice AndAlso back.Paper.SurfaceTarget = SurfaceTarget.Drawings)
        Dim copy = recipe.Clone()
        copy.Paper.Scale = 50
        Check("Clone 為深複製（紙張不共用）", recipe.Paper.Scale = 150)
    End Sub
End Module
