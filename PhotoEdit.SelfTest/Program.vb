Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>PhotoEdit.Core 自我測試。所有檔案只寫在暫存資料夾，不碰任何真實照片。</summary>
Module Program
    Private _passed, _failed As Integer

    Function Main() As Integer
        Dim tempDir = Path.Combine(Path.GetTempPath(), "PhotoEditSelfTest_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
        Try
            RecipeTests()
            GeometryTests()
            ToneTests()
            HistoryTests()
            AutoTests()
            EffectTests()
            PresetTests()
            MapperTests()
            SmartCropTests()
            PortraitTests()
            RepairTests()
            CreativeTests()
            CollageTests()
            StickerTests(tempDir)
            TextEffectTests(tempDir)
            CutoutTests(tempDir)
            DrawingTestsRun()
            CropTestsRun()
            WandTestsRun()
            SplitterTestsRun()
            RasterTestsRun()
            FileTests(tempDir)
        Finally
            Directory.Delete(tempDir, recursive:=True)
        End Try
        Console.WriteLine()
        Console.WriteLine($"通過 {_passed}，失敗 {_failed}")
        Return If(_failed = 0, 0, 1)
    End Function

    Sub Check(name As String, condition As Boolean, Optional detail As String = "")
        If condition Then
            _passed += 1
            Console.WriteLine("  ✓ " & name)
        Else
            _failed += 1
            Console.WriteLine("  ✗ " & name & If(detail = "", "", "  — " & detail))
        End If
    End Sub

    ''' <summary>四個象限不同顏色的測試圖：左上紅、右上綠、左下藍、右下白。</summary>
    Function QuadImage(w As Integer, h As Integer) As Bitmap
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.FillRectangle(Brushes.Red, 0, 0, w \ 2, h \ 2)
            g.FillRectangle(Brushes.Lime, w \ 2, 0, w - w \ 2, h \ 2)
            g.FillRectangle(Brushes.Blue, 0, h \ 2, w \ 2, h - h \ 2)
            g.FillRectangle(Brushes.White, w \ 2, h \ 2, w - w \ 2, h - h \ 2)
        End Using
        Return bmp
    End Function

    Function Solid(w As Integer, h As Integer, c As Color) As Bitmap
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(c)
        End Using
        Return bmp
    End Function

    Function Near(a As Color, b As Color, Optional tolerance As Integer = 12) As Boolean
        Return Math.Abs(CInt(a.R) - b.R) <= tolerance AndAlso Math.Abs(CInt(a.G) - b.G) <= tolerance AndAlso Math.Abs(CInt(a.B) - b.B) <= tolerance
    End Function

    ''' <summary>取某象限中心的顏色（qx, qy = 0 或 1）。</summary>
    Function QuadColor(bmp As Bitmap, qx As Integer, qy As Integer) As Color
        Return bmp.GetPixel(bmp.Width * (1 + 2 * qx) \ 4, bmp.Height * (1 + 2 * qy) \ 4)
    End Function

    Sub RecipeTests()
        Console.WriteLine("配方")
        Dim r As New EditRecipe()
        Check("新配方等於原圖", r.IsIdentity)
        r.Exposure = 0.5
        Check("調整後不再是原圖", Not r.IsIdentity AndAlso r.HasTone)

        r.Crop = New CropRect(0.1, 0.2, 0.5, 0.5)
        Dim c = r.Clone()
        c.Crop.X = 0.3
        Check("Clone 為深複製（裁切框不共用）", r.Crop.X = 0.1)

        Dim full As New EditRecipe With {.Crop = New CropRect()}
        Check("全幅裁切視同不裁切", full.Equals(New EditRecipe()) AndAlso full.IsIdentity)

        Dim rot As New EditRecipe()
        rot.RotateRight() : rot.RotateRight() : rot.RotateRight() : rot.RotateRight()
        Check("轉四次回到 0 度", rot.Rotation = 0)
        rot.RotateLeft()
        Check("向左轉為 270 度", rot.Rotation = 270)

        Dim src As New EditRecipe With {.Exposure = 1, .Saturation = 20, .Rotation = 90, .Sharpness = 30}
        Dim dst As New EditRecipe With {.Rotation = 180}
        dst.CopyAdjustmentsFrom(src)
        Check("貼上調整只複製色調、不動幾何", dst.Exposure = 1 AndAlso dst.Saturation = 20 AndAlso dst.Sharpness = 30 AndAlso dst.Rotation = 180)

        Dim n = New CropRect(0.9, -0.2, 0.5, 2).Normalized()
        Check("裁切框夾回影像範圍", n.X = 0.5 AndAlso n.Y = 0 AndAlso n.Width = 0.5 AndAlso n.Height = 1)

        Dim sq = CropRect.CenteredForAspect(1, 400, 200)
        Check("置中 1:1 裁切框（橫圖）", Math.Abs(sq.Width - 0.5) < 0.0001 AndAlso Math.Abs(sq.X - 0.25) < 0.0001 AndAlso sq.Height = 1)
        Dim wide = CropRect.CenteredForAspect(16 / 9, 300, 300)
        Check("置中 16:9 裁切框（方圖）", wide.Width = 1 AndAlso Math.Abs(wide.Height - 0.5625) < 0.0001)

        Dim json = RecipeStore.ToJson(New EditRecipe With {.Exposure = -0.7, .Shadows = 25, .Crop = New CropRect(0.1, 0.1, 0.8, 0.6), .FlipVertical = True})
        Dim back = RecipeStore.FromJson(json)
        Check("JSON 來回轉換一致", back.Exposure = -0.7 AndAlso back.Shadows = 25 AndAlso back.FlipVertical AndAlso back.Crop.Height = 0.6)
    End Sub

    Sub GeometryTests()
        Console.WriteLine("幾何")
        Using src = QuadImage(200, 100)
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Rotation = 90})
                Check("向右轉 90 度：寬高互換", out.Width = 100 AndAlso out.Height = 200)
                Check("向右轉 90 度：左上變成藍色", Near(QuadColor(out, 0, 0), Color.Blue), QuadColor(out, 0, 0).ToString())
                Check("向右轉 90 度：右上變成紅色", Near(QuadColor(out, 1, 0), Color.Red), QuadColor(out, 1, 0).ToString())
            End Using

            Using out = ImagePipeline.Render(src, New EditRecipe With {.FlipHorizontal = True})
                Check("水平翻轉：左上變成綠色", Near(QuadColor(out, 0, 0), Color.Lime))
            End Using
            Using out = ImagePipeline.Render(src, New EditRecipe With {.FlipVertical = True})
                Check("垂直翻轉：左上變成藍色", Near(QuadColor(out, 0, 0), Color.Blue))
            End Using

            ' 先水平翻轉再向右轉，畫面上應等於「翻轉後的圖」再轉 90 度。
            Dim r As New EditRecipe()
            r.ToggleFlipHorizontal()
            r.RotateRight()
            Using flipped = ImagePipeline.Render(src, New EditRecipe With {.FlipHorizontal = True})
                Using expected = ImagePipeline.Render(flipped, New EditRecipe With {.Rotation = 90})
                    Using out = ImagePipeline.Render(src, r)
                        Dim same = True
                        For Each q In {(0, 0), (1, 0), (0, 1), (1, 1)}
                            If Not Near(QuadColor(out, q.Item1, q.Item2), QuadColor(expected, q.Item1, q.Item2)) Then same = False
                        Next
                        Check("翻轉後再轉向，結果與畫面操作一致", same)
                    End Using
                End Using
            End Using

            Using out = ImagePipeline.Render(src, New EditRecipe With {.Crop = New CropRect(0.5, 0, 0.5, 0.5)})
                Check("裁切右上 1/4：尺寸 100×50", out.Width = 100 AndAlso out.Height = 50)
                Check("裁切右上 1/4：全是綠色", Near(out.GetPixel(10, 10), Color.Lime) AndAlso Near(out.GetPixel(90, 40), Color.Lime))
            End Using

            Using out = ImagePipeline.Render(src, New EditRecipe With {.Straighten = 10})
                Check("拉直：尺寸不變", out.Width = 200 AndAlso out.Height = 100)
                Dim corners = {out.GetPixel(0, 0), out.GetPixel(199, 0), out.GetPixel(0, 99), out.GetPixel(199, 99)}
                Check("拉直：四角沒有透明空白", corners.All(Function(p) p.A = 255))
            End Using
            Check("拉直 0 度不需放大", Math.Abs(ImagePipeline.StraightenScale(200, 100, 0) - 1) < 0.000001)

            Using small = ImagePipeline.Render(src, New EditRecipe(), maxDimension:=50)
                Check("預覽縮圖長邊 50", small.Width = 50 AndAlso small.Height = 25)
            End Using
        End Using

        Check("EXIF 方向 6 = 向右轉 90", PhotoFile.OrientationToRotateFlip(6) = RotateFlipType.Rotate90FlipNone)
        Check("EXIF 方向 1 = 不轉", PhotoFile.OrientationToRotateFlip(1) = RotateFlipType.RotateNoneFlipNone)
    End Sub

    Sub ToneTests()
        Console.WriteLine("色調")
        Using gray = Solid(40, 40, Color.FromArgb(100, 100, 100))
            Using out = ImagePipeline.Render(gray, New EditRecipe())
                Check("原圖配方不改變像素", out.GetPixel(20, 20) = Color.FromArgb(255, 100, 100, 100))
            End Using
            Using out = ImagePipeline.Render(gray, New EditRecipe With {.Exposure = 1})
                Check("曝光 +1 EV 亮度加倍", Near(out.GetPixel(20, 20), Color.FromArgb(200, 200, 200), 3), out.GetPixel(20, 20).ToString())
            End Using
            Using out = ImagePipeline.Render(gray, New EditRecipe With {.Temperature = 50})
                Dim p = out.GetPixel(20, 20)
                Check("色溫偏暖：紅 > 藍", p.R > p.B, p.ToString())
            End Using
            Using out = ImagePipeline.Render(gray, New EditRecipe With {.Tint = 50})
                Dim p = out.GetPixel(20, 20)
                Check("色調偏洋紅：綠降低", p.G < 100 AndAlso p.R = 100, p.ToString())
            End Using
            Using out = ImagePipeline.Render(gray, New EditRecipe With {.Shadows = 100})
                Check("暗部 +100 拉亮暗灰", out.GetPixel(20, 20).R > 100)
            End Using
        End Using

        Using black = Solid(20, 20, Color.Black), white = Solid(20, 20, Color.White)
            Dim r As New EditRecipe With {.Shadows = 100, .Highlights = -100}
            Using b = ImagePipeline.Render(black, r), w = ImagePipeline.Render(white, r)
                Check("暗部/亮部調整不讓純黑、純白變灰", b.GetPixel(5, 5).R = 0 AndAlso w.GetPixel(5, 5).R = 255)
            End Using
        End Using

        Using colorful = Solid(20, 20, Color.FromArgb(200, 100, 50))
            Using out = ImagePipeline.Render(colorful, New EditRecipe With {.Saturation = -100})
                Dim p = out.GetPixel(5, 5)
                Check("飽和度 -100 變成灰階", p.R = p.G AndAlso p.G = p.B, p.ToString())
            End Using
            Using out = ImagePipeline.Render(colorful, New EditRecipe With {.Contrast = 50})
                Dim p = out.GetPixel(5, 5)
                Check("對比 +50 拉開亮暗差", p.R > 200 AndAlso p.B < 50, p.ToString())
            End Using
        End Using

        Using src = QuadImage(60, 60)
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Sharpness = 100})
                Check("銳利化不改變尺寸、色塊中心不變", out.Width = 60 AndAlso Near(QuadColor(out, 0, 0), Color.Red, 2))
            End Using
        End Using
    End Sub

    Sub HistoryTests()
        Console.WriteLine("復原紀錄")
        Dim h As New EditHistory()
        Dim t0 = New DateTime(2026, 10, 1, 12, 0, 0)
        Dim r As New EditRecipe()

        ' 模擬拖曳滑桿：連續 3 次變更，同一組。
        For i = 1 To 3
            h.Record(r, "slider:exposure", t0.AddMilliseconds(i * 100))
            r = r.Clone() : r.Exposure = i * 0.1
        Next
        ' 隔 5 秒後另一個操作。
        h.Record(r, Nothing, t0.AddSeconds(5))
        r = r.Clone() : r.Rotation = 90

        r = h.Undo(r)
        Check("復原一次：取消旋轉、保留曝光", r.Rotation = 0 AndAlso Math.Abs(r.Exposure - 0.3) < 0.0001)
        r = h.Undo(r)
        Check("再復原一次：整段拖曳一起退回", r.Exposure = 0)
        Check("沒有更多可復原", Not h.CanUndo)
        r = h.Redo(r)
        Check("重做：回到拖曳後", Math.Abs(r.Exposure - 0.3) < 0.0001)
        h.Record(r)
        Check("新操作後清空重做", Not h.CanRedo)
    End Sub

    Sub AutoTests()
        Console.WriteLine("自動調整")
        Using bluish = Solid(64, 64, Color.FromArgb(90, 110, 160))
            Dim wb = AutoAdjust.WhiteBalance(bluish, New EditRecipe())
            Check("偏藍照片：自動白平衡偏暖", wb.Temperature > 0, wb.Temperature.ToString())
            Using out = ImagePipeline.Render(bluish, wb)
                Dim p = out.GetPixel(10, 10)
                Check("白平衡後藍紅差距明顯縮小", Math.Abs(CInt(p.R) - p.B) < (160 - 90) \ 2, p.ToString())
            End Using
        End Using

        Using grass = Solid(64, 64, Color.FromArgb(40, 160, 40))
            Dim wb = AutoAdjust.WhiteBalance(grass, New EditRecipe())
            Check("整片綠色時白平衡有上限", Math.Abs(wb.Temperature) <= 50 AndAlso Math.Abs(wb.Tint) <= 30, $"{wb.Temperature}/{wb.Tint}")
        End Using

        Using dark = Solid(64, 64, Color.FromArgb(40, 40, 40))
            Dim e = AutoAdjust.Enhance(dark, New EditRecipe With {.Rotation = 90})
            Check("偏暗照片：自動增強提高曝光", e.Exposure > 0, e.Exposure.ToString())
            Check("自動增強不改幾何", e.Rotation = 90)
        End Using
    End Sub

    Sub EffectTests()
        Console.WriteLine("效果")
        Using gray = Solid(200, 100, Color.FromArgb(150, 150, 150))
            Using out = ImagePipeline.Render(gray, New EditRecipe With {.Vignette = -80})
                Dim center = out.GetPixel(100, 50), corner = out.GetPixel(1, 1)
                Check("暗角 -80：中心不變、四角變暗", center.R = 150 AndAlso corner.R < 80, $"{center.R}/{corner.R}")
            End Using
            Using out = ImagePipeline.Render(gray, New EditRecipe With {.Vignette = 80})
                Check("暗角 +80：四角變亮", out.GetPixel(1, 1).R > 200)
            End Using
            Using out = ImagePipeline.Render(gray, New EditRecipe With {.Grain = 80})
                Dim values = Enumerable.Range(0, 100).Select(Function(x) CInt(out.GetPixel(x, 40).R)).ToList()
                Check("顆粒：像素有起伏、平均不變", values.Max() - values.Min() > 10 AndAlso Math.Abs(values.Average() - 150) < 6,
                      $"{values.Min()}..{values.Max()} 平均 {values.Average():0.0}")
                Using again = ImagePipeline.Render(gray, New EditRecipe With {.Grain = 80})
                    Check("顆粒每次相同（預覽不閃動）", again.GetPixel(37, 40) = out.GetPixel(37, 40))
                End Using
            End Using
            Using out = ImagePipeline.Render(gray, New EditRecipe With {.ToningHue = 30, .ToningStrength = 80})
                Dim p = out.GetPixel(50, 50)
                Check("色彩濾鏡色相 30：偏暖、亮度大致不變", p.R > p.G AndAlso p.G > p.B AndAlso Math.Abs(0.299 * p.R + 0.587 * p.G + 0.114 * p.B - 150) < 10, p.ToString())
            End Using
        End Using
        Using black = Solid(20, 20, Color.Black)
            Using out = ImagePipeline.Render(black, New EditRecipe With {.Fade = 100})
                Check("褪色 100：黑色提亮成灰", out.GetPixel(5, 5).R > 40)
            End Using
        End Using
    End Sub

    Sub PresetTests()
        Console.WriteLine("濾鏡預設集")
        Check("內建預設集至少 10 個、第一個是原色", Preset.BuiltIn.Count >= 10 AndAlso Preset.BuiltIn(0).Look.IsIdentity)
        Check("預設集名稱不重複", Preset.BuiltIn.Select(Function(p) p.Name).Distinct().Count() = Preset.BuiltIn.Count)
        ' 用漸層圖：純色容易被截斷，不同預設集可能算出一樣的值。
        Using src As New Bitmap(80, 60, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(src),
                  br As New Drawing2D.LinearGradientBrush(New Rectangle(0, 0, 80, 60), Color.FromArgb(40, 70, 120), Color.FromArgb(230, 180, 110), 30)
                g.FillRectangle(br, 0, 0, 80, 60)
            End Using
            Dim signatures As New HashSet(Of String)()
            For Each p In Preset.BuiltIn
                Dim r As New EditRecipe With {.Rotation = 90, .Sharpness = 40}
                p.ApplyTo(r)
                Using out = ImagePipeline.Render(src, r)
                    signatures.Add(String.Join(",", {(0, 0), (1, 0), (0, 1), (1, 1)}.Select(Function(q) QuadColor(out, q.Item1, q.Item2).ToArgb())) &
                                   out.GetPixel(1, 1).ToArgb())
                End Using
                If Not (r.Rotation = 90 AndAlso r.Sharpness = 40 AndAlso p.Matches(r)) Then
                    Check($"預設集「{p.Name}」不動幾何與銳利度", False)
                End If
            Next
            Check("每個預設集的效果都不一樣", signatures.Count = Preset.BuiltIn.Count, $"{signatures.Count}/{Preset.BuiltIn.Count}")
        End Using
        Dim edited As New EditRecipe With {.SkinSmoothing = 50}
        Preset.BuiltIn(7).ApplyTo(edited)
        Check("套用預設集保留人像參數", edited.SkinSmoothing = 50 AndAlso edited.Saturation = -100)
    End Sub

    ''' <summary>在影像上點一個紅點，套用幾何後找紅點位置，與 GeometryMapper 算出來的比較。</summary>
    Sub MapperTests()
        Console.WriteLine("幾何換算")
        Dim marker As New PointF(0.2F, 0.3F)
        Using src = Solid(300, 200, Color.Gray)
            Using g = Graphics.FromImage(src)
                g.FillEllipse(Brushes.Red, 300 * 0.2F - 4, 200 * 0.3F - 4, 8, 8)
            End Using
            Dim cases = {
                ("不轉", New EditRecipe()),
                ("向右轉", New EditRecipe With {.Rotation = 90}),
                ("向左轉", New EditRecipe With {.Rotation = 270}),
                ("水平翻轉", New EditRecipe With {.FlipHorizontal = True}),
                ("垂直翻轉 + 向右轉", New EditRecipe With {.FlipVertical = True, .Rotation = 90}),
                ("拉直 12 度", New EditRecipe With {.Straighten = 12}),
                ("向右轉 + 拉直 -8 度", New EditRecipe With {.Rotation = 90, .Straighten = -8}),
                ("垂直透視 +60", New EditRecipe With {.PerspectiveVertical = 60}),
                ("水平透視 -50 + 向左轉", New EditRecipe With {.PerspectiveHorizontal = -50, .Rotation = 270}),
                ("雙向透視 + 拉直 5 度", New EditRecipe With {.PerspectiveVertical = -40, .PerspectiveHorizontal = 70, .Straighten = 5})}
            For Each c In cases
                Using out = ImagePipeline.RenderGeometry(src, c.Item2, 0, applyCrop:=False)
                    Dim found = RedCentroid(out)
                    Dim mapped = GeometryMapper.MapPoint(marker, c.Item2, 300, 200)
                    Dim dx = found.X - mapped.X * out.Width, dy = found.Y - mapped.Y * out.Height
                    Check($"換算位置正確：{c.Item1}", Math.Sqrt(dx * dx + dy * dy) < 2.5, $"實際 {found} 換算 ({mapped.X * out.Width:0.0}, {mapped.Y * out.Height:0.0})")
                End Using
            Next
        End Using
    End Sub

    Sub RepairTests()
        Console.WriteLine("修補")
        ' 座標反算：畫面 → 原圖 → 畫面要回到原點。
        Dim recipes = {
            New EditRecipe With {.Rotation = 90, .FlipHorizontal = True, .Straighten = 7, .PerspectiveVertical = 50,
                                 .Crop = New CropRect(0.1, 0.2, 0.6, 0.5)},
            New EditRecipe With {.Rotation = 270, .PerspectiveHorizontal = -80, .Straighten = -12},
            New EditRecipe With {.FlipVertical = True, .Rotation = 180}}
        Dim worst = 0.0
        For Each r In recipes
            For Each p In {New PointF(0.1F, 0.1F), New PointF(0.5F, 0.5F), New PointF(0.9F, 0.3F), New PointF(0.25F, 0.85F)}
                Dim src = GeometryMapper.UnmapPoint(p, r, 300, 200, fromCropped:=True)
                Dim back = GeometryMapper.MapPoint(src, r, 300, 200, applyCrop:=True)
                worst = Math.Max(worst, Math.Max(Math.Abs(back.X - p.X), Math.Abs(back.Y - p.Y)))
            Next
        Next
        Check("畫面座標 → 原圖 → 畫面，來回一致", worst < 0.0005, $"最大誤差 {worst:0.000000}")
        Check("透視四周沒有空白", Perspective.RowFactor(100, 0) > 0 AndAlso Perspective.RowFactor(100, 1) = 1 AndAlso Perspective.RowFactor(-100, 1) < 1)

        ' 透視：垂直 +100 讓上緣放大，原本上窄下寬的梯形變成接近矩形。
        Using src = Solid(200, 200, Color.Black)
            Using g = Graphics.FromImage(src)
                g.FillPolygon(Brushes.White, {New PointF(70, 0), New PointF(130, 0), New PointF(170, 199), New PointF(30, 199)})
            End Using
            Using out = ImagePipeline.Render(src, New EditRecipe With {.PerspectiveVertical = 100})
                Dim topW = WhiteWidth(out, 2), bottomW = WhiteWidth(out, 197)
                Check("垂直透視：上窄下寬的梯形被拉正", WhiteWidth(src, 2) < 70 AndAlso topW > WhiteWidth(src, 2) * 1.3 AndAlso Math.Abs(bottomW - WhiteWidth(src, 197)) <= 2,
                      $"上 {WhiteWidth(src, 2)}→{topW}，下 {WhiteWidth(src, 197)}→{bottomW}")
            End Using
        End Using

        Dim recipe As New EditRecipe With {.Denoise = 30, .PerspectiveVertical = 20,
                                           .Spots = New List(Of SpotStroke) From {New SpotStroke With {.Radius = 0.01, .Path = New List(Of Double) From {0.5, 0.5}}}}
        Check("有修補時不是原圖", Not recipe.IsIdentity AndAlso recipe.HasSourceFix)
        Dim pasted As New EditRecipe()
        pasted.CopyAdjustmentsFrom(recipe)
        Check("貼上調整帶降噪、不帶污點與透視", pasted.Denoise = 30 AndAlso Not pasted.HasSpots AndAlso pasted.PerspectiveVertical = 0)
        Dim reset = recipe.Clone()
        reset.ResetAdjustments()
        Check("重設調整保留污點與透視", reset.HasSpots AndAlso reset.PerspectiveVertical = 20 AndAlso reset.Denoise = 0)
        Dim c = recipe.Clone()
        c.Spots(0).Path(0) = 0.9
        Check("Clone 深複製污點", recipe.Spots(0).Path(0) = 0.5)
        Check("JSON 保存污點", RecipeStore.FromJson(RecipeStore.ToJson(recipe)).Equals(recipe))
        Check("空的污點清單等於沒有", New EditRecipe With {.Spots = New List(Of SpotStroke)()}.Equals(New EditRecipe()))

        Dim rot As New EditRecipe With {.PerspectiveVertical = 40, .PerspectiveHorizontal = -10}
        rot.RotateRight()
        Check("向右轉時透視跟著轉", rot.PerspectiveVertical = -10 AndAlso rot.PerspectiveHorizontal = -40)
        rot.RotateLeft()
        Check("再向左轉回到原本的透視", rot.PerspectiveVertical = 40 AndAlso rot.PerspectiveHorizontal = -10)

        ' 污點移除：灰色底上的黑點被補掉，其他地方不變。
        Using src = Solid(300, 200, Color.FromArgb(150, 150, 150))
            Using g = Graphics.FromImage(src)
                g.FillEllipse(Brushes.Black, 145, 95, 10, 10)
                g.FillRectangle(Brushes.Red, 20, 20, 10, 10)
            End Using
            Dim spot As New SpotStroke With {.Radius = 8 / 300.0}
            spot.AddPoint(New PointF(0.5F, 0.5F))
            Using out = SourceFix.Apply(src, Array.Empty(Of FaceRegion)(), New EditRecipe With {.Spots = New List(Of SpotStroke) From {spot}})
                Dim p = out.GetPixel(150, 100)
                Check("污點移除：黑點被補成周圍的灰色", Math.Abs(CInt(p.R) - 150) < 15, p.ToString())
                Check("污點移除：其他地方不變", out.GetPixel(25, 25) = src.GetPixel(25, 25) AndAlso out.GetPixel(250, 150) = src.GetPixel(250, 150))
            End Using

            ' 畫一條斜線電線，用多點筆觸移除。
            Using g = Graphics.FromImage(src)
                g.DrawLine(New Pen(Color.Black, 3), 60, 160, 240, 170)
            End Using
            Dim wire As New SpotStroke With {.Radius = 4 / 300.0}
            For x = 60 To 240 Step 10
                wire.AddPoint(New PointF(x / 300.0F, CSng((160 + (x - 60) / 180.0 * 10) / 200.0)))
            Next
            Using out = SourceFix.Apply(src, Array.Empty(Of FaceRegion)(), New EditRecipe With {.Spots = New List(Of SpotStroke) From {wire}})
                Dim darkest = Enumerable.Range(70, 160).Min(Function(x) CInt(out.GetPixel(x, CInt(160 + (x - 60) / 180.0 * 10)).R))
                Check("污點移除：多點筆觸移除整條電線", darkest > 120, $"線上最暗 {darkest}")
            End Using
        End Using

        ' 紋理上的污點：條紋要接回去，而不是糊成一片。
        Using clean As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
            For y = 0 To 159
                For x = 0 To 239
                    clean.SetPixel(x, y, If((x \ 4) Mod 2 = 0, Color.FromArgb(220, 210, 190), Color.FromArgb(70, 60, 50)))
                Next
            Next
            Using dirty = DirectCast(clean.Clone(), Bitmap)
                Using g = Graphics.FromImage(dirty)
                    g.FillEllipse(Brushes.Red, 112, 72, 16, 16)
                End Using
                Dim spot As New SpotStroke With {.Radius = 8 / 240.0}
                spot.AddPoint(New PointF(0.5F, 0.5F))
                Using out = SourceFix.Apply(dirty, Array.Empty(Of FaceRegion)(), New EditRecipe With {.Spots = New List(Of SpotStroke) From {spot}})
                    Dim err = (From y In Enumerable.Range(74, 12) From x In Enumerable.Range(114, 12)
                               Select Math.Abs(CInt(out.GetPixel(x, y).G) - clean.GetPixel(x, y).G)).Average()
                    Check("污點移除：條紋紋理接得回去", err < 20, $"平均誤差 {err:0.0}")
                End Using
            End Using
        End Using

        ' 降噪
        Dim rnd As New Random(3)
        Using noisy As New Bitmap(200, 200, PixelFormat.Format32bppArgb)
            For y = 0 To 199
                For x = 0 To 199
                    Dim n = rnd.Next(-20, 21)
                    Dim cr = rnd.Next(-25, 26)
                    noisy.SetPixel(x, y, Color.FromArgb(Clamp(128 + n + cr), Clamp(128 + n), Clamp(128 + n - cr)))
                Next
            Next
            Using lum = SourceFix.Apply(noisy, Array.Empty(Of FaceRegion)(), New EditRecipe With {.Denoise = 100})
                Check("明度降噪：雜訊明顯減少", StdDev(lum, 100, 100, 40) < StdDev(noisy, 100, 100, 40) * 0.7,
                      $"{StdDev(noisy, 100, 100, 40):0.0} → {StdDev(lum, 100, 100, 40):0.0}")
            End Using
            Using col = SourceFix.Apply(noisy, Array.Empty(Of FaceRegion)(), New EditRecipe With {.ColorNoise = 100})
                Check("色彩降噪：紅藍差異（彩色雜點）大幅減少", ChromaSpread(col) < ChromaSpread(noisy) * 0.4,
                      $"{ChromaSpread(noisy):0.0} → {ChromaSpread(col):0.0}")
            End Using
        End Using
        Using plain = Solid(50, 50, Color.Gray)
            Check("沒有修補參數時不處理", SourceFix.Apply(plain, Array.Empty(Of FaceRegion)(), New EditRecipe With {.Exposure = 1}) Is Nothing)
        End Using

        ' 自動拉直：水平線傾斜 4 度，建議值應約為 -4。
        Using lines = Solid(800, 600, Color.FromArgb(200, 200, 200))
            Using g = Graphics.FromImage(lines)
                For y = 100 To 500 Step 100
                    g.FillRectangle(Brushes.Black, 50, y, 700, 4)
                Next
                g.FillRectangle(Brushes.Black, 600, 80, 4, 440)
            End Using
            Using tilted = ImagePipeline.Render(lines, New EditRecipe With {.Straighten = 4})
                Dim s = AutoStraighten.Suggest(tilted)
                Check("自動拉直：偵測到 4 度傾斜", s.HasValue AndAlso Math.Abs(s.Value + 4) < 0.6, If(s.HasValue, s.Value.ToString("0.0"), "無"))
            End Using
            Using tilted = ImagePipeline.Render(lines, New EditRecipe With {.Straighten = -2.5})
                Dim s = AutoStraighten.Suggest(tilted)
                Check("自動拉直：偵測到 -2.5 度傾斜", s.HasValue AndAlso Math.Abs(s.Value - 2.5) < 0.6, If(s.HasValue, s.Value.ToString("0.0"), "無"))
            End Using
        End Using
        Using blank = Solid(400, 300, Color.Gray)
            Check("自動拉直：沒有直線時不建議", Not AutoStraighten.Suggest(blank).HasValue)
        End Using
    End Sub

    Sub CreativeTests()
        Console.WriteLine("創意特效")
        Using gray = Solid(300, 200, Color.FromArgb(128, 128, 128))
            ' 漸層濾鏡：上緣壓暗、下半部不變。
            Dim grad As New EditRecipe With {.LocalAdjustments = New List(Of LocalAdjustment) From {
                New LocalAdjustment With {.Kind = LocalKind.Gradient, .StartX = 0.5, .StartY = 0, .EndX = 0.5, .EndY = 0.5, .Exposure = -1}}}
            Using out = ImagePipeline.Render(gray, grad)
                Check("漸層濾鏡：上緣變暗、下半部不變", out.GetPixel(150, 2).R < 75 AndAlso out.GetPixel(150, 150).R = 128,
                      $"{out.GetPixel(150, 2).R}/{out.GetPixel(150, 150).R}")
            End Using
            ' 位置記在原圖：向右轉後，壓暗的那一邊跑到右邊。
            grad.Rotation = 90
            Using out = ImagePipeline.Render(gray, grad)
                Check("漸層濾鏡跟著照片旋轉", out.GetPixel(out.Width - 2, 150).R < 75 AndAlso out.GetPixel(2, 150).R = 128)
            End Using

            ' 局部筆刷：中央提亮，角落不變。
            Dim stroke As New SpotStroke With {.Radius = 0.05}
            stroke.AddPoint(New PointF(0.5F, 0.5F))
            Dim brush As New EditRecipe With {.LocalAdjustments = New List(Of LocalAdjustment) From {
                New LocalAdjustment With {.Kind = LocalKind.Brush, .Exposure = 1, .Strokes = New List(Of SpotStroke) From {stroke}}}}
            Using out = ImagePipeline.Render(gray, brush)
                Check("局部筆刷：塗到的地方變亮、其他不變", out.GetPixel(150, 100).R > 200 AndAlso out.GetPixel(10, 10).R = 128,
                      $"{out.GetPixel(150, 100).R}/{out.GetPixel(10, 10).R}")
            End Using
            Using small = ImagePipeline.Render(gray, brush, 150)
                Check("局部筆刷：縮圖時範圍等比例", small.GetPixel(75, 50).R > 200 AndAlso small.GetPixel(75 + 20, 50).R = 128)
            End Using
        End Using

        ' 背景模糊：條紋圖，臉附近保持清楚、遠處被模糊。
        Using stripes As New Bitmap(400, 300, PixelFormat.Format32bppArgb)
            For y = 0 To 299
                For x = 0 To 399
                    stripes.SetPixel(x, y, If((x \ 3) Mod 2 = 0, Color.White, Color.Black))
                Next
            Next
            Dim face As New FaceRegion With {.Box = New RectangleF(0.45F, 0.2F, 0.1F, 0.15F)}
            Using out = ImagePipeline.Render(stripes, New EditRecipe With {.BackgroundBlur = 100}, faces:={face})
                Check("背景模糊：臉部清楚", StdDev(out, 200, 80, 6) > 100, $"{StdDev(out, 200, 80, 6):0}")
                Check("背景模糊：遠處變模糊", StdDev(out, 30, 270, 6) < 40, $"{StdDev(out, 30, 270, 6):0}")
            End Using
            Using out = ImagePipeline.Render(stripes, New EditRecipe With {.BackgroundBlur = 100})
                Check("背景模糊：沒有臉時中央清楚、角落模糊", StdDev(out, 200, 150, 6) > 100 AndAlso StdDev(out, 15, 15, 6) < 40)
            End Using
            Using out = ImagePipeline.Render(stripes, New EditRecipe With {.TiltShift = 100, .TiltShiftPosition = 50})
                Check("移軸：中間清楚、上緣模糊", StdDev(out, 200, 150, 6) > 90 AndAlso StdDev(out, 200, 10, 6) < 40,
                      $"{StdDev(out, 200, 150, 6):0}/{StdDev(out, 200, 10, 6):0}")
            End Using
        End Using

        ' 邊框
        Using src = Solid(200, 100, Color.Red)
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 50})
                Check("白邊：尺寸變大、四周是白色", out.Width > 200 AndAlso out.Height > 100 AndAlso out.GetPixel(1, 1).ToArgb() = Color.White.ToArgb() AndAlso
                      out.GetPixel(out.Width \ 2, out.Height \ 2).R = 255 AndAlso out.GetPixel(out.Width \ 2, out.Height \ 2).G = 0)
            End Using
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.Polaroid, .FrameSize = 50})
                Dim top = Enumerable.Range(0, out.Height).First(Function(y) out.GetPixel(out.Width \ 2, y).G < 100)
                Dim bottom = out.Height - 1 - Enumerable.Range(0, out.Height).Last(Function(y) out.GetPixel(out.Width \ 2, y).G < 100)
                Check("拍立得：下緣比上緣寬", bottom > top * 3, $"上 {top} 下 {bottom}")
            End Using
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.Rounded, .FrameSize = 80})
                Dim m = Enumerable.Range(0, out.Width).First(Function(x) out.GetPixel(x, out.Height \ 2).G < 100)
                Check("圓角：照片角落被修圓", out.GetPixel(m + 1, m + 1).G > 200 AndAlso out.GetPixel(out.Width \ 2, out.Height \ 2).G = 0)
            End Using
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.Film})
                Check("底片：上下黑邊有齒孔", out.Height > out.Width / 2 + 4 AndAlso
                      Enumerable.Range(0, out.Width).Any(Function(x) out.GetPixel(x, 4).R > 200) AndAlso out.GetPixel(0, 0).R < 40)
            End Using
        End Using

        ' 文字與貼圖
        Using src = Solid(400, 300, Color.FromArgb(40, 40, 40))
            Dim text As New Overlay With {.Kind = OverlayKind.Text, .Text = "測試ABC", .X = 0.5, .Y = 0.5, .Size = 0.15, .Shadow = False}
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Overlays = New List(Of Overlay) From {text}})
                Dim lit = (From y In Enumerable.Range(120, 60) From x In Enumerable.Range(100, 200) Where out.GetPixel(x, y).R > 200).Count()
                Check("文字：畫在指定位置", lit > 300 AndAlso out.GetPixel(10, 10).R = 40, $"亮點 {lit}")
                Dim b = Creative.OverlayBounds(text, 400, 300)
                Check("文字範圍包含中心點", b.Contains(0.5F, 0.5F) AndAlso b.Width < 0.9)
            End Using
            Dim heart As New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = "heart", .X = 0.25, .Y = 0.5, .Size = 0.3, .ColorArgb = Color.Red.ToArgb()}
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Overlays = New List(Of Overlay) From {heart}})
                Dim p = out.GetPixel(100, 160)
                Check("貼圖：愛心畫成指定顏色", p.R > 200 AndAlso p.G < 60, p.ToString())
            End Using
            ' 旋轉：箭頭朝右，轉 90 度後朝下。
            Dim arrow As New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = "arrow", .X = 0.5, .Y = 0.5, .Size = 0.4, .ColorArgb = Color.Lime.ToArgb(), .Shadow = False}
            Using flat = ImagePipeline.Render(src, New EditRecipe With {.Overlays = New List(Of Overlay) From {arrow}})
                arrow.Rotation = 90
                Using turned = ImagePipeline.Render(src, New EditRecipe With {.Overlays = New List(Of Overlay) From {arrow}})
                    ' 箭頭尖端：未轉時在右邊 (x≈255)，轉 90 度後在下方 (y≈205)。
                    Check("貼圖旋轉 90 度：箭頭由朝右變朝下",
                          flat.GetPixel(250, 150).G > 200 AndAlso flat.GetPixel(200, 200).G < 100 AndAlso
                          turned.GetPixel(200, 200).G > 200 AndAlso turned.GetPixel(250, 150).G < 100)
                End Using
            End Using
            Dim f1 = Creative.OverlayFrame(New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = "star", .Size = 0.2}, 400, 300)
            Dim f2 = Creative.OverlayFrame(New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = "star", .Size = 0.4, .Rotation = 30}, 400, 300)
            Check("選取框：大小加倍框也加倍、角度照記", Math.Abs((f2.Right - f2.Left) / (f1.Right - f1.Left) - 2) < 0.05 AndAlso f2.Rotation = 30 AndAlso
                  f1.Left < 0 AndAlso f1.Right > 0 AndAlso f1.Top < 0 AndAlso f1.Bottom > 0)

            For Each s In Creative.StickerNames
                Using out = ImagePipeline.Render(src, New EditRecipe With {.Overlays = New List(Of Overlay) From {
                    New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = s.Key, .Size = 0.3, .ColorArgb = Color.Yellow.ToArgb()}}})
                    Dim changed = (From y In Enumerable.Range(100, 100) From x In Enumerable.Range(150, 100) Where out.GetPixel(x, y).R > 150).Any()
                    If Not changed Then Check($"貼圖「{s.Name}」有畫出來", False)
                End Using
            Next
            Check("所有貼圖都畫得出來", True)
        End Using

        ' 配方
        Dim full As New EditRecipe With {.BackgroundBlur = 30, .Frame = PhotoFrameStyle.Polaroid,
            .LocalAdjustments = New List(Of LocalAdjustment) From {New LocalAdjustment With {.Exposure = 0.5}},
            .Overlays = New List(Of Overlay) From {New Overlay With {.Text = "hi"}}}
        Check("創意特效不是原圖", Not full.IsIdentity AndAlso full.HasCreative)
        Check("JSON 保存局部調整與文字", RecipeStore.FromJson(RecipeStore.ToJson(full)).Equals(full))
        Dim reset = full.Clone()
        reset.ResetAdjustments()
        Check("重設調整清除局部與特效、保留文字", reset.LocalAdjustments Is Nothing AndAlso reset.Frame = PhotoFrameStyle.None AndAlso reset.Overlays.Count = 1)
        Dim pasted As New EditRecipe()
        pasted.CopyAdjustmentsFrom(full)
        Check("貼上調整帶邊框與模糊、不帶文字和局部", pasted.Frame = PhotoFrameStyle.Polaroid AndAlso pasted.BackgroundBlur = 30 AndAlso
              pasted.Overlays Is Nothing AndAlso pasted.LocalAdjustments Is Nothing)
        Dim c = full.Clone()
        c.Overlays(0).Text = "changed"
        c.LocalAdjustments(0).Exposure = 2
        Check("Clone 深複製文字與局部調整", full.Overlays(0).Text = "hi" AndAlso full.LocalAdjustments(0).Exposure = 0.5)
    End Sub

    Sub CollageTests()
        Console.WriteLine("拼貼")
        Check("內建版型至少 8 種，每格都在畫布內", CollageLayout.BuiltIn.Count >= 8 AndAlso
              CollageLayout.BuiltIn.All(Function(l) l.Cells.All(Function(c) c.Left >= 0 AndAlso c.Top >= 0 AndAlso c.Right <= 1.0001 AndAlso c.Bottom <= 1.0001)))
        Dim settings As New CollageSettings With {.Layout = CollageLayout.BuiltIn(0), .Aspect = 2, .Spacing = 50, .Background = Color.White}
        Dim size = CollageRenderer.CanvasSize(settings, 400)
        Check("畫布尺寸依比例", size.Width = 400 AndAlso size.Height = 200)
        Using a = Solid(100, 100, Color.Red), b = Solid(50, 80, Color.Blue)
            Using out = CollageRenderer.Render({a, b}, settings, 400)
                Check("拼貼：左格紅、右格藍、中間是背景色",
                      out.GetPixel(100, 100).R = 255 AndAlso out.GetPixel(100, 100).B = 0 AndAlso
                      out.GetPixel(300, 100).B = 255 AndAlso out.GetPixel(300, 100).R = 0 AndAlso
                      out.GetPixel(200, 100).ToArgb() = Color.White.ToArgb())
            End Using
            settings.Layout = CollageLayout.BuiltIn.First(Function(l) l.Cells.Count = 9)
            Using out = CollageRenderer.Render({a}, settings, 400, drawPlaceholders:=True)
                ' 取格子內、避開中央「＋」號的位置。
                Check("空白格畫成灰色預留格", out.GetPixel(160, 80).R < 240 AndAlso out.GetPixel(160, 80).R > 180, out.GetPixel(160, 80).ToString())
            End Using
        End Using
    End Sub

    Sub StickerTests(tempDir As String)
        Console.WriteLine("貼圖庫")
        Dim root = Path.Combine(tempDir, "stick")
        Directory.CreateDirectory(Path.Combine(root, "動物"))
        Directory.CreateDirectory(Path.Combine(root, "生日"))
        ' 寬 2 高 1 的紅色貼圖（透明底，中間紅色）
        Using bmp As New Bitmap(100, 50, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.Transparent)
                g.FillRectangle(Brushes.Red, 10, 10, 80, 30)
            End Using
            bmp.Save(Path.Combine(root, "生日", "box.png"), ImageFormat.Png)
            bmp.Save(Path.Combine(root, "生日", "_cover.png"), ImageFormat.Png)
            bmp.Save(Path.Combine(root, "生日", "cover-1.thumb256.png"), ImageFormat.Png)
            bmp.Save(Path.Combine(root, "生日", "a.png"), ImageFormat.Png)
        End Using
        File.WriteAllText(Path.Combine(root, "生日", "readme.txt"), "x")
        Dim oldRoot = StickerLibrary.Root
        StickerLibrary.Root = root
        Try
            Check("主題依名稱排序", StickerLibrary.Themes().SequenceEqual({"動物", "生日"}.OrderBy(Function(s) s, StringComparer.CurrentCulture)))
            Dim list = StickerLibrary.Stickers("生日")
            Check("貼圖清單排除封面（_ 或 cover 開頭）與非圖片", list.SequenceEqual({"生日\a.png", "生日\box.png"}), String.Join(",", list))
            Check("空主題沒有貼圖", StickerLibrary.Stickers("動物").Count = 0)
            Check("不允許跑出 stick 資料夾", StickerLibrary.FullPath("..\..\secret.png") Is Nothing AndAlso StickerLibrary.FullPath("生日\box.png") IsNot Nothing)
            Dim loaded = StickerLibrary.GetImage("生日\box.png")
            Check("讀取貼圖", loaded IsNot Nothing AndAlso loaded.Width = 100)
            Check("讀取後不鎖住檔案", CanOpenExclusive(Path.Combine(root, "生日", "box.png")))
            Check("找不到的貼圖回傳 Nothing", StickerLibrary.GetImage("生日\none.png") Is Nothing)

            Using src = Solid(400, 300, Color.FromArgb(40, 40, 40))
                Dim o As New Overlay With {.Kind = OverlayKind.Image, .ImagePath = "生日\box.png", .X = 0.5, .Y = 0.5, .Size = 0.4, .Shadow = False}
                Using out = ImagePipeline.Render(src, New EditRecipe With {.Overlays = New List(Of Overlay) From {o}})
                    ' 高 120、寬 240，紅色區域約 x 104..296、y 102..198，透明邊不蓋住底色。
                    Check("圖片貼圖：依比例畫在指定位置", out.GetPixel(200, 150).R > 200 AndAlso out.GetPixel(285, 150).R > 200 AndAlso
                          out.GetPixel(200, 95).R = 40 AndAlso out.GetPixel(330, 150).R = 40, $"{out.GetPixel(285, 150)}")
                End Using
                o.Rotation = 90
                Using out = ImagePipeline.Render(src, New EditRecipe With {.Overlays = New List(Of Overlay) From {o}})
                    Check("圖片貼圖：旋轉 90 度變成直的", out.GetPixel(200, 235).R > 200 AndAlso out.GetPixel(285, 150).R = 40)
                End Using
                Dim f = Creative.OverlayFrame(New Overlay With {.Kind = OverlayKind.Image, .ImagePath = "生日\box.png", .Size = 0.4}, 400, 300)
                Check("圖片貼圖選取框依圖片比例", (f.Right - f.Left) * 400 > (f.Bottom - f.Top) * 300 * 1.6)
                Dim missing As New Overlay With {.Kind = OverlayKind.Image, .ImagePath = "生日\gone.png", .Size = 0.4}
                Using out = ImagePipeline.Render(src, New EditRecipe With {.Overlays = New List(Of Overlay) From {missing}})
                    Check("圖片貼圖檔不見時不畫、不出錯", out.GetPixel(200, 150).R = 40)
                End Using
                Using p = Creative.StickerPreview(New Overlay With {.Kind = OverlayKind.Image, .ImagePath = "生日\box.png"}, 64)
                    Check("貼圖預覽圖：透明底、內容置中", p.GetPixel(0, 0).A = 0 AndAlso p.GetPixel(32, 32).R > 200)
                End Using
                Using p = Creative.StickerPreview(New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = "star", .ColorArgb = Color.Gold.ToArgb()}, 64)
                    Check("內建貼圖預覽圖", p.GetPixel(32, 32).A > 200)
                End Using
            End Using
            Dim r As New EditRecipe With {.Overlays = New List(Of Overlay) From {New Overlay With {.Kind = OverlayKind.Image, .ImagePath = "生日\box.png"}}}
            Check("JSON 保存圖片貼圖路徑", RecipeStore.FromJson(RecipeStore.ToJson(r)).Overlays(0).ImagePath = "生日\box.png")
        Finally
            StickerLibrary.Root = oldRoot
        End Try
    End Sub

    ''' <summary>在 w×h、底色 bg 的圖上畫一個文字物件。</summary>
    Function RenderText(o As Overlay, bg As Color, Optional w As Integer = 400, Optional h As Integer = 200) As Bitmap
        Dim bmp = Solid(w, h, bg)
        Creative.DrawOverlays(bmp, New EditRecipe With {.Overlays = New List(Of Overlay) From {o}})
        Return bmp
    End Function

    Function CountPixels(bmp As Bitmap, rect As Rectangle, test As Func(Of Color, Boolean)) As Integer
        Dim n = 0
        For y = Math.Max(0, rect.Top) To Math.Min(bmp.Height - 1, rect.Bottom - 1)
            For x = Math.Max(0, rect.Left) To Math.Min(bmp.Width - 1, rect.Right - 1)
                If test(bmp.GetPixel(x, y)) Then n += 1
            Next
        Next
        Return n
    End Function

    Function TextOv(text As String, Optional size As Double = 0.3) As Overlay
        Return New Overlay With {.Kind = OverlayKind.Text, .Text = text, .X = 0.5, .Y = 0.5, .Size = size, .Shadow = False,
                                 .ColorArgb = Color.White.ToArgb(), .FontName = "Arial"}
    End Function

    Sub TextEffectTests(tempDir As String)
        Console.WriteLine("文字特效")
        Dim gray = Color.FromArgb(128, 128, 128)
        Dim all As New Rectangle(0, 0, 400, 200)
        Dim isBlack = Function(c As Color) c.R < 40 AndAlso c.G < 40 AndAlso c.B < 40
        Dim isWhite = Function(c As Color) c.R > 230 AndAlso c.G > 230 AndAlso c.B > 230

        ' 外框
        Using plain = RenderText(TextOv("Text"), gray)
            Dim o = TextOv("Text") : o.OutlineWidth = 60
            Using outlined = RenderText(o, gray)
                Check("外框：加了黑色外框", CountPixels(outlined, all, isBlack) > 300 AndAlso CountPixels(plain, all, isBlack) = 0)
            End Using
            o.Outline2Width = 60 : o.Outline2ColorArgb = Color.Red.ToArgb()
            Using two = RenderText(o, gray)
                Check("外框：第二層在最外圍", CountPixels(two, all, Function(c) c.R > 200 AndAlso c.G < 50) > 300)
            End Using
        End Using

        ' 底色
        Dim box = TextOv("Hi") : box.BackgroundStyle = TextBackground.Box : box.BackgroundColorArgb = Color.Red.ToArgb() : box.BackgroundOpacity = 100
        Using b = RenderText(box, gray)
            Dim f = Creative.OverlayFrame(box, 400, 200)
            Dim corner = New Point(CInt((0.5 + f.Left) * 400) + 4, CInt((0.5 + f.Top) * 200) + 8)
            Check("底色方塊：文字周圍是紅色、遠處不變", b.GetPixel(corner.X + 6, corner.Y + 6).R > 200 AndAlso b.GetPixel(corner.X + 6, corner.Y + 6).G < 60 AndAlso b.GetPixel(2, 2) = gray,
                  b.GetPixel(corner.X + 6, corner.Y + 6).ToString())
        End Using
        Dim bar = TextOv("Hi", 0.15) : bar.BackgroundStyle = TextBackground.Bar : bar.BackgroundColorArgb = Color.Black.ToArgb() : bar.BackgroundOpacity = 100
        Using b = RenderText(bar, gray)
            Check("字幕條：橫跨整張照片", isBlack(b.GetPixel(2, 100)) AndAlso isBlack(b.GetPixel(397, 100)) AndAlso b.GetPixel(200, 5) = gray)
        End Using
        Dim dimmed = TextOv("Hi") : dimmed.BackgroundStyle = TextBackground.DimPhoto : dimmed.BackgroundOpacity = 50
        Using b = RenderText(dimmed, Color.FromArgb(200, 200, 200))
            Check("壓暗照片：遠處變暗", b.GetPixel(5, 5).R < 120 AndAlso b.GetPixel(5, 5).R > 80, b.GetPixel(5, 5).ToString())
        End Using

        ' 鏤空：照片本身填字 + 壓暗。
        Using photo As New Bitmap(400, 200, PixelFormat.Format32bppArgb)
            For y = 0 To 199
                For x = 0 To 399
                    photo.SetPixel(x, y, Color.FromArgb(255, (x * 255) \ 399, 200, (y * 255) \ 199))
                Next
            Next
            Dim knock = TextOv("MM", 0.6) : knock.Bold = True : knock.FillMode = TextFill.Photo
            knock.BackgroundStyle = TextBackground.DimPhoto : knock.BackgroundOpacity = 80
            Using out = DirectCast(photo.Clone(), Bitmap)
                Creative.DrawOverlays(out, New EditRecipe With {.Overlays = New List(Of Overlay) From {knock}})
                Dim same = CountPixels(out, New Rectangle(80, 40, 240, 120), Function(c) True) ' 先算總數
                Dim original = 0
                For y = 40 To 159
                    For x = 80 To 319
                        If Math.Abs(CInt(out.GetPixel(x, y).G) - 200) < 6 Then original += 1
                    Next
                Next
                Check("鏤空：字裡是原本的照片、字外被壓暗", original > same * 0.15 AndAlso out.GetPixel(5, 5).G < 60, $"{original}/{same}，外 {out.GetPixel(5, 5)}")
            End Using
        End Using

        ' 陰影方向與模糊
        Dim sh = TextOv("I", 0.5) : sh.Shadow = True : sh.ShadowOpacity = 100 : sh.ShadowDistance = 100 : sh.ShadowAngle = 0
        Using b = RenderText(sh, gray)
            Dim f = Creative.OverlayFrame(sh, 400, 200)
            Dim cx = 200
            Dim right = CountPixels(b, New Rectangle(cx, 0, 200, 200), isBlack), left = CountPixels(b, New Rectangle(0, 0, cx - 10, 200), isBlack)
            Check("陰影：往指定方向（右）偏移", right > 100 AndAlso left = 0, $"右 {right} 左 {left}")
        End Using
        sh.ShadowBlur = 100
        Using b = RenderText(sh, gray)
            Dim soft = CountPixels(b, all, Function(c) c.R < 120 AndAlso c.R > 20 AndAlso c.G = c.R)
            Check("陰影模糊：邊緣變柔和", soft > 300, soft.ToString())
        End Using

        ' 發光
        Dim glow = TextOv("O", 0.5) : glow.GlowSize = 80 : glow.GlowColorArgb = Color.Lime.ToArgb()
        Using b = RenderText(glow, Color.Black)
            Check("發光：文字外圍出現光暈", CountPixels(b, all, Function(c) c.G > 120 AndAlso c.R < 100) > 500)
        End Using

        ' 立體（往下）
        Dim ex = TextOv("H", 0.4) : ex.ExtrudeDepth = 100 : ex.ExtrudeAngle = 90 : ex.ExtrudeColorArgb = Color.Blue.ToArgb()
        Using b = RenderText(ex, gray)
            Dim f = Creative.OverlayFrame(TextOv("H", 0.4), 400, 200)
            Dim below = CountPixels(b, New Rectangle(150, CInt((0.5 + f.Bottom) * 200) - 4, 100, 30), Function(c) c.B > 100 AndAlso c.R < 60)
            Check("立體：下方出現側面", below > 50, below.ToString())
        End Using

        ' 漸層（左紅右藍）與彩虹
        Dim grad = TextOv("WWWW", 0.4) : grad.Bold = True : grad.FillMode = TextFill.Gradient : grad.ColorArgb = Color.Red.ToArgb()
        grad.Color2Argb = Color.Blue.ToArgb() : grad.GradientAngle = 0
        Using b = RenderText(grad, Color.Black)
            Dim leftRed = CountPixels(b, New Rectangle(0, 0, 160, 200), Function(c) c.R > 150 AndAlso c.B < 100)
            Dim rightBlue = CountPixels(b, New Rectangle(240, 0, 160, 200), Function(c) c.B > 150 AndAlso c.R < 100)
            Check("漸層：左邊紅、右邊藍", leftRed > 100 AndAlso rightBlue > 100, $"{leftRed}/{rightBlue}")
        End Using
        grad.FillMode = TextFill.Rainbow
        Using b = RenderText(grad, Color.Black)
            Dim hues = (From y In Enumerable.Range(60, 80) From x In Enumerable.Range(40, 320) Let c = b.GetPixel(x, y) Where c.GetSaturation() > 0.5 AndAlso c.GetBrightness() > 0.3
                        Select CInt(c.GetHue() / 60)).Distinct().Count()
            Check("彩虹：出現多種顏色", hues >= 4, hues.ToString())
        End Using

        ' 圖片填字
        Dim texPath = Path.Combine(tempDir, "tex.png")
        Using tex = Solid(32, 32, Color.Lime)
            tex.Save(texPath, ImageFormat.Png)
        End Using
        Dim tf = TextOv("WW", 0.5) : tf.Bold = True : tf.FillMode = TextFill.Texture : tf.TexturePath = texPath
        Using b = RenderText(tf, Color.Black)
            Check("圖片填字：用圖片填滿文字", CountPixels(b, all, Function(c) c.G > 200 AndAlso c.R < 60) > 500)
        End Using

        ' 透明度
        Dim half = TextOv("WW", 0.6) : half.Bold = True : half.Opacity = 50
        Using b = RenderText(half, Color.Black)
            Dim brightest = (From y In Enumerable.Range(0, 200) From x In Enumerable.Range(0, 400) Select CInt(b.GetPixel(x, y).R)).Max()
            Check("透明度 50%：白字變成半亮", brightest > 110 AndAlso brightest < 145, brightest.ToString())
        End Using
        Dim heart As New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = "heart", .Size = 0.5, .Opacity = 50, .Shadow = False, .ColorArgb = Color.White.ToArgb()}
        Using b = RenderText(heart, Color.Black)
            Check("貼圖也能半透明", Math.Abs(b.GetPixel(200, 100).R - 128) < 15, b.GetPixel(200, 100).ToString())
        End Using

        ' 排版
        Dim horiz = Creative.OverlayFrame(TextOv("直排文字", 0.15), 400, 200)
        Dim vertO = TextOv("直排文字", 0.15) : vertO.FontName = "Microsoft JhengHei" : vertO.Vertical = True
        Dim vert = Creative.OverlayFrame(vertO, 400, 200)
        Check("直排：變成高大於寬", (vert.Bottom - vert.Top) * 200 > (vert.Right - vert.Left) * 400 * 2 AndAlso
              (horiz.Right - horiz.Left) * 400 > (horiz.Bottom - horiz.Top) * 200)
        Dim twoLines = Creative.OverlayFrame(TextOv("AB" & vbLf & "CD", 0.15), 400, 200)
        Dim oneLine = Creative.OverlayFrame(TextOv("AB", 0.15), 400, 200)
        Check("多行：兩行比一行高", (twoLines.Bottom - twoLines.Top) > (oneLine.Bottom - oneLine.Top) * 1.8)
        Dim leftA = TextOv("WWWWWW" & vbLf & "I", 0.2) : leftA.Align = 0
        Dim rightA = leftA.Clone() : rightA.Align = 2
        Using l = RenderText(leftA, Color.Black), r = RenderText(rightA, Color.Black)
            Dim line2 = Function(b As Bitmap) (From x In Enumerable.Range(0, 400) Where Enumerable.Range(110, 30).Any(Function(y) b.GetPixel(x, y).R > 200) Select x).DefaultIfEmpty(-1).Average()
            Check("對齊：短行靠左／靠右", line2(l) < 170 AndAlso line2(r) > 230, $"{line2(l):0}/{line2(r):0}")
        End Using
        Dim spaced = TextOv("ABC", 0.15) : spaced.LetterSpacing = 60
        Check("字距加大：變寬", Creative.OverlayFrame(spaced, 400, 200).Right > Creative.OverlayFrame(TextOv("ABC", 0.15), 400, 200).Right * 1.3)
        Dim arcO = TextOv("ARCHED TEXT", 0.1) : arcO.Arc = 180
        Dim straight = Creative.OverlayFrame(TextOv("ARCHED TEXT", 0.1), 400, 200)
        Dim arced = Creative.OverlayFrame(arcO, 400, 200)
        Check("弧形 180°：變成拱形（較高、較窄）", (arced.Bottom - arced.Top) > (straight.Bottom - straight.Top) * 2 AndAlso
              (arced.Right - arced.Left) < (straight.Right - straight.Left))

        ' 七段數字
        Dim seven = TextOv("'05 7 12", 0.2) : seven.FontName = TextRender.SevenSegmentFont : seven.ColorArgb = Color.Orange.ToArgb()
        Using b = RenderText(seven, Color.Black)
            Check("七段數字：畫出日期", CountPixels(b, all, Function(c) c.R > 200 AndAlso c.G > 100 AndAlso c.B < 50) > 300)
        End Using
        Check("日期戳記格式", PhotoInfo.DateStampText(New DateTime(2005, 7, 12)) = "'05 7 12")
        Dim parsed = PhotoInfo.ParseExifDate(System.Text.Encoding.ASCII.GetBytes("2005:07:12 14:03:21" & ChrW(0)))
        Check("EXIF 日期解析", parsed.HasValue AndAlso parsed.Value = New DateTime(2005, 7, 12, 14, 3, 21))
        Dim gps(23) As Byte
        BitConverter.GetBytes(25UI).CopyTo(gps, 0) : BitConverter.GetBytes(1UI).CopyTo(gps, 4)
        BitConverter.GetBytes(30UI).CopyTo(gps, 8) : BitConverter.GetBytes(1UI).CopyTo(gps, 12)
        BitConverter.GetBytes(36UI).CopyTo(gps, 16) : BitConverter.GetBytes(1UI).CopyTo(gps, 20)
        Check("GPS 度分秒解析", Math.Abs(PhotoInfo.ParseGpsCoordinate(gps).Value - 25.51) < 0.0001)

        ' 預設集
        Dim sigs As New HashSet(Of String)()
        Dim allOk = True
        For Each p In TextPreset.BuiltIn
            Dim o = TextOv("測試", 0.25) : o.X = 0.4 : o.Rotation = 10 : o.Text = "測試"
            p.ApplyTo(o)
            If Not (o.Text = "測試" AndAlso o.X = 0.4 AndAlso o.Rotation = 10 AndAlso o.Size = 0.25 AndAlso o.Kind = OverlayKind.Text) Then allOk = False
            Using b = RenderText(o, Color.FromArgb(90, 120, 160))
                sigs.Add(String.Join(",", Enumerable.Range(0, 20).Select(Function(i) b.GetPixel(60 + i * 14, 70 + (i Mod 5) * 12).ToArgb())))
            End Using
        Next
        Check("預設集：保留文字、位置、大小、角度", allOk)
        Check("預設集：每個樣式看起來都不一樣", sigs.Count = TextPreset.BuiltIn.Count, $"{sigs.Count}/{TextPreset.BuiltIn.Count}")
        Dim withStyle = TextOv("x") : withStyle.OutlineWidth = 30 : withStyle.GlowSize = 20
        Check("JSON 保存文字特效", RecipeStore.FromJson(RecipeStore.ToJson(New EditRecipe With {.Overlays = New List(Of Overlay) From {withStyle}})).Overlays(0).GlowSize = 20)
    End Sub

    Sub CutoutTests(tempDir As String)
        Console.WriteLine("去背")
        ' 照片：左半紅（主體）、右半藍（背景）；遮罩：左半白。
        Using photo As New Bitmap(200, 100, PixelFormat.Format32bppArgb), mask As New Bitmap(100, 50, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(photo)
                g.Clear(Color.Blue)
                g.FillRectangle(Brushes.Red, 0, 0, 100, 100)
            End Using
            Using g = Graphics.FromImage(mask)
                g.Clear(Color.Black)
                g.FillRectangle(Brushes.White, 0, 0, 50, 50)
            End Using
            Dim c As New CutoutSettings With {.Feather = 0, .Background = CutoutBackground.Transparent}
            Using out = CutoutCompositor.Compose(photo, mask, c)
                Check("去背透明：主體不透明、背景透明", out.GetPixel(30, 50).A = 255 AndAlso out.GetPixel(30, 50).R = 255 AndAlso out.GetPixel(170, 50).A = 0)
            End Using
            c.Background = CutoutBackground.Color : c.BackgroundColorArgb = Color.Lime.ToArgb()
            Using out = CutoutCompositor.Compose(photo, mask, c)
                Check("換成純色：背景變綠、主體不變", out.GetPixel(170, 50).G = 255 AndAlso out.GetPixel(170, 50).B = 0 AndAlso out.GetPixel(30, 50).R = 255)
            End Using
            c.Background = CutoutBackground.Blur : c.BackgroundBlur = 100
            Using out = CutoutCompositor.Compose(photo, mask, c)
                Dim nearEdge = out.GetPixel(115, 50)
                Check("模糊背景：主體顏色不會暈到背景", nearEdge.R < 40 AndAlso nearEdge.B > 200, nearEdge.ToString())
            End Using
            Dim bgPath = Path.Combine(tempDir, "bg.png")
            Using bgImg = Solid(40, 40, Color.Yellow)
                bgImg.Save(bgPath, ImageFormat.Png)
            End Using
            c.Background = CutoutBackground.Image : c.BackgroundImagePath = bgPath
            Using out = CutoutCompositor.Compose(photo, mask, c)
                Check("換成圖片：背景是指定的圖片", out.GetPixel(170, 50).R > 240 AndAlso out.GetPixel(170, 50).G > 240 AndAlso out.GetPixel(170, 50).B < 20)
            End Using

            ' 修正筆觸：在背景上「保留」、在主體上「擦除」。
            c.Background = CutoutBackground.Transparent
            Dim keep As New CutoutStroke With {.Radius = 0.05, .Keep = True}
            keep.AddPoint(New PointF(0.85F, 0.5F))
            Dim erase1 As New CutoutStroke With {.Radius = 0.05, .Keep = False}
            erase1.AddPoint(New PointF(0.15F, 0.5F))
            c.Strokes = New List(Of CutoutStroke) From {keep, erase1}
            Using out = CutoutCompositor.Compose(photo, mask, c)
                Check("保留筆刷補回、擦除筆刷去掉", out.GetPixel(170, 50).A = 255 AndAlso out.GetPixel(30, 50).A = 0 AndAlso out.GetPixel(30, 10).A = 255)
            End Using
            c.Strokes.Clear()
            Dim opaqueWidth = Function(b As Bitmap) Enumerable.Range(0, 200).Count(Function(x) b.GetPixel(x, 50).A > 128)
            Dim baseWidth As Integer
            Using out = CutoutCompositor.Compose(photo, mask, c)
                baseWidth = opaqueWidth(out)
            End Using
            c.Shift = 50
            Using out = CutoutCompositor.Compose(photo, mask, c)
                Check("外擴：主體範圍變大", opaqueWidth(out) >= baseWidth + 2, $"{baseWidth} → {opaqueWidth(out)}")
            End Using
            c.Shift = -50
            Using out = CutoutCompositor.Compose(photo, mask, c)
                Check("內縮：主體範圍變小", opaqueWidth(out) <= baseWidth - 2, $"{baseWidth} → {opaqueWidth(out)}")
            End Using
            c.Shift = 0 : c.Feather = 100
            Using out = CutoutCompositor.Compose(photo, mask, c)
                Dim a = out.GetPixel(100, 50).A
                Check("羽化：邊緣半透明", a > 30 AndAlso a < 225, a.ToString())
            End Using

            ' 整條流程：SourceFix → 透明一路保留到輸出。
            Dim recipe As New EditRecipe With {.Cutout = New CutoutSettings With {.Feather = 0}, .Exposure = 0.5, .Rotation = 90}
            Check("去背會改變畫面、原背景則不會", recipe.HasSourceFix AndAlso Not New EditRecipe With {.Cutout = New CutoutSettings With {.Background = CutoutBackground.Original}}.HasSourceFix)
            Using out = ImagePipeline.Render(photo, recipe, prepare:=Function(b) SourceFix.Apply(b, Array.Empty(Of FaceRegion)(), recipe, mask))
                Check("透明經過旋轉與調色仍保留", out.Width = 100 AndAlso out.GetPixel(50, 20).A = 255 AndAlso out.GetPixel(50, 180).A = 0)
            End Using
            Check("JSON 保存去背設定", RecipeStore.FromJson(RecipeStore.ToJson(recipe)).Cutout.Feather = 0)
            Dim cloned = recipe.Clone()
            cloned.Cutout.Strokes.Add(New CutoutStroke())
            Check("Clone 深複製去背設定", recipe.Cutout.Strokes.Count = 0)

            ' 遮罩附屬檔與匯出
            Dim photoPath = Path.Combine(tempDir, "cut.png")
            photo.Save(photoPath, ImageFormat.Png)
            MaskStore.Save(photoPath, CutoutModel.General, mask)
            Using loaded = MaskStore.Load(photoPath, CutoutModel.General)
                Check("遮罩附屬檔存取", loaded IsNot Nothing AndAlso loaded.GetPixel(10, 10).R = 255 AndAlso loaded.GetPixel(90, 10).R = 0)
            End Using
            Check("沒有人像遮罩時回傳 Nothing", MaskStore.Load(photoPath, CutoutModel.Human) Is Nothing)
            Using pf = PhotoFile.Open(photoPath)
                Dim r2 As New EditRecipe With {.Cutout = New CutoutSettings With {.Feather = 0}}
                Dim prep As Func(Of Bitmap, Bitmap) = Function(b) SourceFix.Apply(b, Array.Empty(Of FaceRegion)(), r2, mask)
                Dim pngOut = Path.Combine(tempDir, "cut_out.png"), jpgOut = Path.Combine(tempDir, "cut_out.jpg")
                pf.Export(r2, pngOut, prepare:=prep)
                pf.Export(r2, jpgOut, prepare:=prep)
                Using png = New Bitmap(pngOut), jpg = New Bitmap(jpgOut)
                    Check("匯出 PNG 保留透明", png.GetPixel(170, 50).A = 0 AndAlso png.GetPixel(30, 50).A = 255)
                    Check("匯出 JPG 時透明處變白", jpg.GetPixel(170, 50).R > 240 AndAlso jpg.GetPixel(170, 50).B > 240 AndAlso jpg.GetPixel(30, 50).G < 30)
                End Using
            End Using
            MaskStore.DeleteAll(photoPath)
            Check("刪除遮罩附屬檔", Not File.Exists(MaskStore.MaskPath(photoPath, CutoutModel.General)))
        End Using
    End Sub

    Function WhiteWidth(bmp As Bitmap, y As Integer) As Integer
        Return Enumerable.Range(0, bmp.Width).Count(Function(x) bmp.GetPixel(x, y).R > 128)
    End Function

    Function ChromaSpread(bmp As Bitmap) As Double
        Return Enumerable.Range(20, 160).Average(Function(i) Math.Abs(CInt(bmp.GetPixel(i, i).R) - bmp.GetPixel(i, i).B))
    End Function

    Function RedCentroid(bmp As Bitmap) As PointF
        Dim sx = 0.0, sy = 0.0, n = 0
        For y = 0 To bmp.Height - 1
            For x = 0 To bmp.Width - 1
                Dim p = bmp.GetPixel(x, y)
                If p.R > 180 AndAlso p.G < 90 Then sx += x + 0.5 : sy += y + 0.5 : n += 1
            Next
        Next
        Return If(n = 0, New PointF(-100, -100), New PointF(CSng(sx / n), CSng(sy / n)))
    End Function

    Sub SmartCropTests()
        Console.WriteLine("智慧構圖")
        Dim face As New RectangleF(0.65F, 0.15F, 0.1F, 0.15F)
        Dim c = SmartCrop.Suggest({face}, 1, 400, 300)
        Dim inside = c.X <= face.Left AndAlso c.Y <= face.Top AndAlso c.X + c.Width >= face.Right AndAlso c.Y + c.Height >= face.Bottom
        Check("1:1 構圖：臉在框內", inside, $"{c.X:0.00},{c.Y:0.00} {c.Width:0.00}×{c.Height:0.00}")
        Check("1:1 構圖：比例正確且用最大尺寸", Math.Abs(c.Width * 400 - c.Height * 300) < 1 AndAlso Math.Abs(c.Height - 1) < 0.001)
        Check("1:1 構圖：臉群水平置中（受邊界限制時靠邊）", Math.Abs((c.X + c.Width / 2) - 0.7) < 0.01 OrElse Math.Abs(c.X + c.Width - 1) < 0.001)

        Dim lowFace As New RectangleF(0.45F, 0.35F, 0.1F, 0.12F)
        Dim tight = SmartCrop.Suggest({lowFace}, 0, 400, 300, 0.6)
        Dim eyeY = lowFace.Top + lowFace.Height * 0.4
        Check("收緊構圖：眼睛在上方三分線", Math.Abs((eyeY - tight.Y) / tight.Height - 1 / 3.0) < 0.02, $"{(eyeY - tight.Y) / tight.Height:0.000}")

        Dim faces = {New RectangleF(0.05F, 0.4F, 0.1F, 0.12F), New RectangleF(0.85F, 0.4F, 0.1F, 0.12F)}
        Dim wide = SmartCrop.Suggest(faces, 1, 400, 300, 0.5)
        Check("臉群放不下時自動放大框", wide.Width > 0.5 * 0.75 + 0.001)

        Dim none = SmartCrop.Suggest(Array.Empty(Of RectangleF)(), 16 / 9, 400, 300)
        Check("沒有臉時置中", Math.Abs(none.X) < 0.001 AndAlso Math.Abs(none.Y + none.Height / 2 - 0.5) < 0.001)
    End Sub

    Sub PortraitTests()
        Console.WriteLine("人像修飾")
        ' 膚色底加上雜訊，模擬粗糙膚質；臉在左半邊。
        Dim rnd As New Random(1)
        Using src As New Bitmap(400, 200, PixelFormat.Format32bppArgb)
            For y = 0 To 199
                For x = 0 To 399
                    Dim n = rnd.Next(-25, 26)
                    src.SetPixel(x, y, Color.FromArgb(Clamp(205 + n), Clamp(160 + n), Clamp(135 + n)))
                Next
            Next
            Dim face As New FaceRegion With {.Box = New RectangleF(0.15F, 0.2F, 0.25F, 0.6F)}
            face.Landmarks = {New PointF(0.22F, 0.38F), New PointF(0.33F, 0.38F), New PointF(0.275F, 0.5F),
                              New PointF(0.23F, 0.65F), New PointF(0.32F, 0.65F)}
            Dim faces = {face}

            Check("沒有人像參數時不處理", PortraitRetouch.Apply(src, faces, New EditRecipe()) Is Nothing)
            Check("沒有臉時不處理", PortraitRetouch.Apply(src, Array.Empty(Of FaceRegion)(), New EditRecipe With {.SkinSmoothing = 80}) Is Nothing)

            Using out = PortraitRetouch.Apply(src, faces, New EditRecipe With {.SkinSmoothing = 100})
                Dim cheekBefore = StdDev(src, 95, 100, 10), cheekAfter = StdDev(out, 95, 100, 10)
                ' 刻意保留部分膚質紋理，所以 100 也只降到約六成。
                Check("磨皮：臉頰雜訊明顯減少", cheekAfter < cheekBefore * 0.75, $"{cheekBefore:0.0} → {cheekAfter:0.0}")
                Check("磨皮：臉外的區域不變", StdDev(out, 330, 100, 10) = StdDev(src, 330, 100, 10))
                Check("磨皮：尺寸不變", out.Width = 400 AndAlso out.Height = 200)
            End Using
            Using out = PortraitRetouch.Apply(src, faces, New EditRecipe With {.FaceBrighten = 100})
                Check("臉部提亮：臉頰變亮", MeanR(out, 95, 100, 10) > MeanR(src, 95, 100, 10) + 5)
            End Using
        End Using

        ' 預覽縮圖與全尺寸的磨皮程度要接近（臉先縮放到固定大小再處理）。
        Check("偵測模型存在於輸出資料夾", FaceDetector.ModelAvailable())
    End Sub

    Function Clamp(v As Integer) As Integer
        Return Math.Max(0, Math.Min(255, v))
    End Function

    Function StdDev(bmp As Bitmap, cx As Integer, cy As Integer, r As Integer) As Double
        Dim values = (From y In Enumerable.Range(cy - r, 2 * r) From x In Enumerable.Range(cx - r, 2 * r) Select CDbl(bmp.GetPixel(x, y).G)).ToList()
        Dim avg = values.Average()
        Return Math.Sqrt(values.Average(Function(v) (v - avg) * (v - avg)))
    End Function

    Function MeanR(bmp As Bitmap, cx As Integer, cy As Integer, r As Integer) As Double
        Return (From y In Enumerable.Range(cy - r, 2 * r) From x In Enumerable.Range(cx - r, 2 * r) Select CDbl(bmp.GetPixel(x, y).R)).Average()
    End Function

    Sub FileTests(tempDir As String)
        Console.WriteLine("檔案")
        Dim photoPath = Path.Combine(tempDir, "測試照片.jpg")
        Using src = QuadImage(320, 240)
            src.Save(photoPath, ImageFormat.Jpeg)
        End Using
        Dim originalBytes = File.ReadAllBytes(photoPath)

        Dim recipe As New EditRecipe With {.Rotation = 90, .Exposure = 0.3, .Crop = New CropRect(0, 0, 1, 0.5)}
        RecipeStore.Save(photoPath, recipe)
        Check("儲存附屬檔", File.Exists(RecipeStore.SidecarPath(photoPath)))
        Check("讀回配方一致", recipe.Equals(RecipeStore.Load(photoPath)))

        Using photo = PhotoFile.Open(photoPath)
            Check("開檔後不鎖住檔案", CanOpenExclusive(photoPath))
            Dim exportPath = PhotoFile.SuggestExportPath(photoPath)
            Check("建議匯出檔名", Path.GetFileName(exportPath) = "測試照片_edited.jpg")
            photo.Export(recipe, exportPath)
            Using exported = Image.FromFile(exportPath)
                Check("匯出尺寸正確（轉 90 度後裁上半）", exported.Width = 240 AndAlso exported.Height = 160, $"{exported.Width}×{exported.Height}")
            End Using
            Check("已存在時建議加序號", Path.GetFileName(PhotoFile.SuggestExportPath(photoPath)) = "測試照片_edited2.jpg")

            Dim refused = False
            Try
                photo.Export(recipe, photoPath)
            Catch ex As InvalidOperationException
                refused = True
            End Try
            Check("拒絕覆寫原始照片", refused)
        End Using
        Check("原始照片內容完全沒變", originalBytes.SequenceEqual(File.ReadAllBytes(photoPath)))

        RecipeStore.Save(photoPath, New EditRecipe())
        Check("回復原圖後刪除附屬檔", Not File.Exists(RecipeStore.SidecarPath(photoPath)))

        File.WriteAllText(RecipeStore.SidecarPath(photoPath), "{ 壞掉的內容")
        Check("附屬檔損壞時當作沒有編輯", RecipeStore.Load(photoPath) Is Nothing)
    End Sub

    Function CanOpenExclusive(path As String) As Boolean
        Try
            Using fs As New FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                Return True
            End Using
        Catch ex As IOException
            Return False
        End Try
    End Function
End Module
