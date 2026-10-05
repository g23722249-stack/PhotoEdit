Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>檔案 → 新增：單位換算、預設版面、底色、底圖放置與解析度。</summary>
Module NewImageTests

    Sub NewImageTestsRun(tempDir As String)
        Console.WriteLine("新增影像")
        Dim photo35 = CanvasPreset.BuiltIn.First(Function(p) p.Name.StartsWith("相片大小 3.5 x 5"))
        Check("3.5×5 英吋 300 dpi = 1050×1500（和小畫家相同）", photo35.PixelSize(300) = New Size(1050, 1500))
        Dim a4 = CanvasPreset.BuiltIn.First(Function(p) p.Name.StartsWith("A4"))
        Check("A4 300 dpi = 2480×3508", a4.PixelSize(300) = New Size(2480, 3508), a4.PixelSize(300).ToString())
        Check("像素版面與解析度無關", CanvasPreset.BuiltIn.First(Function(p) p.Name.StartsWith("Full HD")).PixelSize(72) = New Size(1920, 1080))
        Check("2.54 公分 = 1 英吋", NewImage.ToPixels(2.54, SizeUnit.Centimeters, 300) = 300)
        Check("像素換回公釐", Math.Abs(NewImage.FromPixels(2480, SizeUnit.Millimeters, 300) - 210) < 0.1)
        Check("每個類別都有版面", CanvasPreset.Categories.All(Function(c) CanvasPreset.BuiltIn.Any(Function(p) p.Category = c)))

        Dim spec As New NewImageSpec With {.Width = 1050, .Height = 1500}
        Check("檔案大小 24 位元：4,614 KB", NewImage.FormatFileSize(spec.UncompressedBytes) = $"{4614:N0} KB", NewImage.FormatFileSize(spec.UncompressedBytes))
        Check("尺寸合理時可以建立", NewImage.Validate(spec) Is Nothing)
        Check("太大時不能建立", NewImage.Validate(New NewImageSpec With {.Width = 30000, .Height = 10}) IsNot Nothing)

        Using bmp = NewImage.Create(New NewImageSpec With {.Width = 40, .Height = 30, .Dpi = 300})
            Check("白色底、大小正確、記錄解析度", bmp.Width = 40 AndAlso bmp.Height = 30 AndAlso bmp.GetPixel(5, 5).ToArgb() = Color.White.ToArgb() AndAlso
                  Math.Abs(bmp.HorizontalResolution - 300) < 0.5)
        End Using
        Using bmp = NewImage.Create(New NewImageSpec With {.Width = 40, .Height = 30, .Fill = NewImageFill.Transparent})
            Check("透明底", bmp.GetPixel(5, 5).A = 0)
        End Using
        Using bmp = NewImage.Create(New NewImageSpec With {.Width = 40, .Height = 30, .Fill = NewImageFill.Custom, .CustomColor = Color.FromArgb(10, 200, 30, 40)})
            Check("自訂色彩一律不透明", bmp.GetPixel(5, 5) = Color.FromArgb(255, 200, 30, 40))
        End Using

        ' 底圖：200×100 的四色圖放進 100×100 畫布。
        Using pic = QuadImage(200, 100)
            Dim cover = NewImage.PictureBounds(pic.Size, 100, 100, PictureFit.Cover)
            Check("填滿：等比放大蓋滿、左右裁掉", Math.Abs(cover.Height - 100) < 0.01 AndAlso Math.Abs(cover.Width - 200) < 0.01 AndAlso Math.Abs(cover.X + 50) < 0.01)
            Dim contain = NewImage.PictureBounds(pic.Size, 100, 100, PictureFit.Contain)
            Check("完整顯示：上下留邊", Math.Abs(contain.Width - 100) < 0.01 AndAlso Math.Abs(contain.Y - 25) < 0.01)
            Using bmp = NewImage.Create(New NewImageSpec With {.Width = 100, .Height = 100, .Fill = NewImageFill.Black, .Picture = pic, .Fit = PictureFit.Contain})
                Check("完整顯示：上方露出黑底、中間是底圖", Near(bmp.GetPixel(50, 5), Color.Black) AndAlso Near(bmp.GetPixel(25, 35), Color.Red))
            End Using
            Using bmp = NewImage.Create(New NewImageSpec With {.Width = 100, .Height = 100, .Picture = pic, .Fit = PictureFit.Stretch})
                Check("延展：四個象限都對", Near(QuadColor(bmp, 0, 0), Color.Red) AndAlso Near(QuadColor(bmp, 1, 1), Color.White) AndAlso Near(QuadColor(bmp, 0, 1), Color.Blue))
            End Using
            Using bmp = NewImage.Create(New NewImageSpec With {.Width = 400, .Height = 200, .Fill = NewImageFill.Transparent, .Picture = pic, .Fit = PictureFit.Tile})
                Check("並排：第二塊從 200 開始", Near(bmp.GetPixel(210, 10), Color.Red) AndAlso Near(bmp.GetPixel(210, 110), Color.Red))
            End Using
            Using small = NewImage.Create(New NewImageSpec With {.Width = 1000, .Height = 500, .Picture = pic, .Fit = PictureFit.Center}, 100)
                Check("預覽縮小：原尺寸置中的底圖跟著縮", small.Width = 100 AndAlso small.Height = 50 AndAlso Near(small.GetPixel(42, 28), Color.Blue) AndAlso
                      Near(small.GetPixel(42, 22), Color.Red) AndAlso Near(small.GetPixel(5, 5), Color.White))
            End Using
        End Using

        ' 新影像存成 PNG 再開啟：解析度保留，匯出也沿用。
        Dim file = Path.Combine(tempDir, "新影像.png")
        Using bmp = NewImage.Create(New NewImageSpec With {.Width = 60, .Height = 40, .Dpi = 300})
            bmp.Save(file, ImageFormat.Png)
        End Using
        Using pf = PhotoFile.Open(file)
            Check("開啟後讀到解析度", Math.Abs(pf.Resolution - 300) < 0.5, pf.Resolution.ToString())
            Dim outFile = Path.Combine(tempDir, "新影像_out.jpg")
            pf.Export(New EditRecipe(), outFile)
            Using img = Image.FromFile(outFile)
                Check("匯出 JPG 沿用解析度", Math.Abs(img.HorizontalResolution - 300) < 0.5, img.HorizontalResolution.ToString())
            End Using
        End Using
    End Sub
End Module
