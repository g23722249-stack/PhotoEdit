Imports System.Drawing.Imaging
Imports System.IO
Imports System.IO.Compression
Imports PhotoEdit

''' <summary>專案檔（.pedx）：存檔再載入要完整還原原圖、配方、圖層、去背遮罩與外部圖片；另存 BMP。</summary>
Module ProjectTests

    Sub ProjectTestsRun(tempDir As String)
        Console.WriteLine("專案檔")
        Dim dir = Path.Combine(tempDir, "project")
        Directory.CreateDirectory(dir)

        ' 原圖、貼圖庫裡的一張貼圖、一張完整路徑的材質圖。
        Dim photoPath = Path.Combine(dir, "IMG_0001.jpg")
        Using bmp = QuadImage(80, 60)
            bmp.SetResolution(300, 300)
            bmp.Save(photoPath, ImageFormat.Jpeg)
        End Using
        Dim oldRoot = StickerLibrary.Root
        Dim stickRoot = Path.Combine(dir, "stick")
        Directory.CreateDirectory(Path.Combine(stickRoot, "測試"))
        Using s = Solid(10, 10, Color.Lime)
            s.Save(Path.Combine(stickRoot, "測試", "綠.png"), ImageFormat.Png)
        End Using
        Dim texture = Path.Combine(dir, "texture.png")
        Using t = Solid(8, 8, Color.Blue)
            t.Save(texture, ImageFormat.Png)
        End Using
        StickerLibrary.Root = stickRoot

        Try
            Dim recipe As New EditRecipe With {.Exposure = 0.5, .Rotation = 90, .Crop = New CropRect(0.1, 0.1, 0.8, 0.8)}
            recipe.Overlays = New List(Of Overlay) From {
                New Overlay With {.Kind = OverlayKind.Text, .Text = "你好", .TexturePath = texture},
                New Overlay With {.Kind = OverlayKind.Image, .ImagePath = "測試\綠.png"}}
            recipe.Drawings = New List(Of DrawLayer) From {New DrawLayer With {.Shape = DrawShape.Ellipse, .Name = "圓"}}
            recipe.Cutout = New CutoutSettings With {.Model = CutoutModel.Human, .BackgroundImagePath = texture}

            Dim projectPath = Path.Combine(dir, "作品.pedx")
            Dim originalBytes As Byte()
            Using pf = PhotoFile.Open(photoPath)
                originalBytes = pf.OriginalBytes
                Check("PhotoFile 保留原檔位元組", originalBytes.SequenceEqual(File.ReadAllBytes(photoPath)))
                Using mask = Solid(40, 30, Color.White)
                    ProjectFile.Save(projectPath, Path.GetFileName(photoPath), pf.OriginalBytes, recipe,
                                     New Dictionary(Of CutoutModel, Bitmap) From {{CutoutModel.Human, mask}})
                End Using

                ' BMP：透明處變白、24 位元。
                Dim bmpFile = Path.Combine(dir, "flat.bmp")
                pf.Export(New EditRecipe With {.CropShape = CropShape.Ellipse}, bmpFile)
                Using img = New Bitmap(bmpFile)
                    Check("另存 BMP：24 位元、透明角落變白", img.PixelFormat = PixelFormat.Format24bppRgb AndAlso Near(img.GetPixel(0, 0), Color.White))
                End Using
            End Using
            Check("存檔後沒有留下暫存檔", File.Exists(projectPath) AndAlso Not File.Exists(projectPath & ".saving"))
            Check("ReferencedFiles：材質、貼圖不重複", ProjectFile.ReferencedFiles(recipe).Count = 2)

            ' 原圖、貼圖、材質都刪掉，模擬搬到別台電腦。
            File.Delete(photoPath)
            File.Delete(texture)
            File.Delete(Path.Combine(stickRoot, "測試", "綠.png"))
            StickerLibrary.ClearCache()

            Dim work = Path.Combine(dir, "work")
            Dim loaded = ProjectFile.Load(projectPath, work)
            Check("原圖用原檔名解開、內容完全相同", Path.GetFileName(loaded.PhotoPath) = "IMG_0001.jpg" AndAlso
                  File.ReadAllBytes(loaded.PhotoPath).SequenceEqual(originalBytes))
            Dim r = loaded.Recipe
            Check("配方還原：調整、旋轉、裁切", r.Exposure = 0.5 AndAlso r.Rotation = 90 AndAlso r.Crop IsNot Nothing AndAlso Math.Abs(r.Crop.Width - 0.8) < 0.0001)
            Check("圖層還原：文字、貼圖、繪圖", r.Overlays.Count = 2 AndAlso r.Overlays(0).Text = "你好" AndAlso r.Drawings.Count = 1 AndAlso r.Drawings(0).Name = "圓")
            Check("去背遮罩解開到 MaskStore 位置", MaskStore.Load(loaded.PhotoPath, CutoutModel.Human) IsNot Nothing)
            Check("完整路徑的圖片換成專案裡的副本", r.Overlays(0).TexturePath.StartsWith(work) AndAlso File.Exists(r.Overlays(0).TexturePath) AndAlso
                  r.Cutout.BackgroundImagePath = r.Overlays(0).TexturePath)
            Check("貼圖庫的相對路徑不變", r.Overlays(1).ImagePath = "測試\綠.png")
            Dim sticker = StickerLibrary.GetImage("測試\綠.png")
            Check("本機沒有的貼圖改用專案裡的副本", sticker IsNot Nothing AndAlso Near(sticker.GetPixel(5, 5), Color.Lime))
            Using pf = PhotoFile.Open(loaded.PhotoPath)
                Check("載入的原圖解析度保留", Math.Abs(pf.Resolution - 300) < 1)
            End Using

            ' 存檔再存一次（覆寫）也正常。
            ProjectFile.Save(projectPath, "IMG_0001.jpg", originalBytes, r)
            Check("覆寫既有專案檔", ProjectFile.Load(projectPath, Path.Combine(dir, "work2")).Recipe.Overlays.Count = 2)

            ' 惡意的項目名稱不能寫到工作資料夾以外。
            Dim evil = Path.Combine(dir, "evil.pedx")
            Using fs As New FileStream(evil, FileMode.Create), zip As New ZipArchive(fs, ZipArchiveMode.Create)
                Dim m As New ProjectManifest With {.OriginalName = "..\..\escape.jpg", .OriginalEntry = "original.jpg"}
                Using w As New StreamWriter(zip.CreateEntry("manifest.json").Open())
                    w.Write(Text.Json.JsonSerializer.Serialize(m))
                End Using
                Using s = zip.CreateEntry("original.jpg").Open()
                    s.Write(originalBytes, 0, originalBytes.Length)
                End Using
            End Using
            Dim evilWork = Path.Combine(dir, "evilwork")
            Dim e = ProjectFile.Load(evil, evilWork)
            Check("檔名含 ..\ 時只留檔名", Path.GetDirectoryName(e.PhotoPath) = evilWork AndAlso Not File.Exists(Path.Combine(dir, "escape.jpg")))

            Dim notProject = Path.Combine(dir, "bad.pedx")
            File.WriteAllText(notProject, "hello")
            Dim threw = False
            Try
                ProjectFile.Load(notProject, Path.Combine(dir, "badwork"))
            Catch ex As InvalidDataException
                threw = True
            End Try
            Check("不是專案檔時丟出 InvalidDataException", threw)
            Check("副檔名判斷", ProjectFile.IsProject("a.PEDX") AndAlso Not ProjectFile.IsProject("a.png"))
        Finally
            StickerLibrary.Root = oldRoot
        End Try
    End Sub
End Module
