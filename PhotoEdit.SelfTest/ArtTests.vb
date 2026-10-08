Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>藝術風格：每種都算得出來、強度、透明度、配方與濾鏡列分類。</summary>
Module ArtTests

    ''' <summary>測試圖：天空漸層、深色的山、圓形太陽、左上角透明。</summary>
    Private Function Scene() As Bitmap
        Dim bmp As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            Using br As New Drawing2D.LinearGradientBrush(New Rectangle(0, 0, 240, 160), Color.FromArgb(90, 150, 230), Color.FromArgb(250, 220, 170), 90.0F)
                g.FillRectangle(br, 0, 0, 240, 160)
            End Using
            g.FillPolygon(New SolidBrush(Color.FromArgb(40, 60, 45)), {New Point(0, 160), New Point(80, 70), New Point(150, 120), New Point(240, 60), New Point(240, 160)})
            g.FillEllipse(Brushes.OrangeRed, 160, 20, 40, 40)
        End Using
        bmp.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0))
        Return bmp
    End Function

    Private Function Diff(a As Bitmap, b As Bitmap) As Double
        Dim pa = StickerSplitter.ReadPixels(a), pb = StickerSplitter.ReadPixels(b)
        Dim sum = 0.0
        For i = 0 To pa.Length - 1
            If i Mod 4 = 3 Then Continue For
            sum += Math.Abs(CInt(pa(i)) - pb(i))
        Next
        Return sum / (pa.Length * 0.75)
    End Function

    Sub ArtTestsRun()
        Console.WriteLine("藝術風格")
        Dim styles = [Enum].GetValues(GetType(ArtStyle)).Cast(Of ArtStyle)().Where(Function(s) s <> ArtStyle.None).ToList()
        Check("13 種風格都有名稱與分類", styles.Count = 13 AndAlso ArtStyles.Names.Length = 14 AndAlso
              ArtStyles.Categories.SelectMany(Function(c) c.Styles).OrderBy(Function(s) s).SequenceEqual(styles))

        Using orig = Scene()
            Dim failed As New List(Of String)()
            For Each st In styles
                Using b = DirectCast(orig.Clone(), Bitmap)
                    ArtStyles.Apply(b, st, 100, 50, 50)
                    If Diff(orig, b) < 6 Then failed.Add(ArtStyles.Names(CInt(st)))
                    If b.GetPixel(0, 0).A <> 0 OrElse b.GetPixel(100, 100).A <> 255 Then failed.Add(ArtStyles.Names(CInt(st)) & "（透明度）")
                End Using
            Next
            Check("每種風格都明顯改變畫面、保留透明度", failed.Count = 0, String.Join("、", failed))

            Using a = DirectCast(orig.Clone(), Bitmap), b = DirectCast(orig.Clone(), Bitmap)
                ArtStyles.Apply(a, ArtStyle.MangaBW, 0, 50, 50)
                Check("強度 0：完全不變", Diff(orig, a) = 0)
                ArtStyles.Apply(b, ArtStyle.MangaBW, 50, 50, 50)
                Using full = DirectCast(orig.Clone(), Bitmap)
                    ArtStyles.Apply(full, ArtStyle.MangaBW, 100, 50, 50)
                    Dim half = Diff(orig, b), all = Diff(orig, full)
                    Check("強度 50：大約是一半的變化", half > all * 0.35 AndAlso half < all * 0.65, $"{half:0.0} / {all:0.0}")
                End Using
            End Using

            ' 雜訊圖（每點顏色都不同）：方塊越大，一列裡顏色改變的次數越少
            Dim noise As New Bitmap(240, 160, PixelFormat.Format32bppArgb)
            Dim rnd As New Random(3)
            For y = 0 To 159
                For x = 0 To 239
                    noise.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)))
                Next
            Next
            Using a = DirectCast(noise.Clone(), Bitmap), b = DirectCast(noise.Clone(), Bitmap)
                ArtStyles.Apply(a, ArtStyle.PixelArt, 100, 50, 10)
                ArtStyles.Apply(b, ArtStyle.PixelArt, 100, 50, 90)
                Dim changes = Function(bm As Bitmap) Enumerable.Range(0, 239).Count(Function(x) bm.GetPixel(x, 80) <> bm.GetPixel(x + 1, 80))
                Check("筆觸大小：像素藝術的方塊變大", changes(b) < changes(a), $"{changes(a)} → {changes(b)}")
            End Using
            noise.Dispose()
        End Using

        ' 大圖會先縮小處理再放大，結果大小不變
        Using big As New Bitmap(3000, 200, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(big)
                g.Clear(Color.SkyBlue)
                g.FillEllipse(Brushes.DarkGreen, 1000, 20, 800, 160)
            End Using
            ArtStyles.Apply(big, ArtStyle.Woodcut, 100, 50, 50)
            Check("超過 2400 的大圖也能處理（尺寸不變）", big.Width = 3000 AndAlso big.Height = 200)
        End Using

        ' 配方
        Dim r As New EditRecipe With {.ArtStyle = ArtStyle.Watercolor, .ArtStrength = 80}
        Check("有藝術風格時不是原圖", Not r.IsIdentity AndAlso r.HasArt)
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(r))
        Check("藝術風格存得回來", back.Equals(r) AndAlso back.ArtStyle = ArtStyle.Watercolor AndAlso back.ArtStrength = 80)
        Dim target As New EditRecipe With {.Exposure = 1}
        Dim water = Preset.BuiltIn.First(Function(p) p.Name = "水彩畫")
        water.ApplyTo(target)
        Check("套用水彩畫濾鏡：設定風格、只換風格不動曝光以外的幾何", target.ArtStyle = ArtStyle.Watercolor AndAlso water.Matches(target))
        Preset.BuiltIn(0).ApplyTo(target)
        Check("套用原色：清掉藝術風格", target.ArtStyle = ArtStyle.None AndAlso target.ArtStrength = 100)

        ' 濾鏡列分類
        Check("濾鏡列 4 個分類，每個濾鏡（原色除外）都有分類", Preset.Categories.Length = 4 AndAlso
              Preset.BuiltIn.Skip(1).All(Function(p) Preset.Categories.Contains(p.Category)) AndAlso Preset.BuiltIn(0).Category = "")
        Check("繪畫 6 個、漫畫／版畫 7 個", Preset.BuiltIn.Where(Function(p) p.Category = Preset.PaintCategory).Count() = 6 AndAlso
              Preset.BuiltIn.Where(Function(p) p.Category = Preset.ComicCategory).Count() = 7)

        ' 速度：預覽大小（1600×1067）
        Using pv As New Bitmap(1600, 1067, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(pv)
                Using br As New Drawing2D.LinearGradientBrush(New Rectangle(0, 0, 1600, 1067), Color.SteelBlue, Color.Wheat, 45.0F)
                    g.FillRectangle(br, 0, 0, 1600, 1067)
                End Using
            End Using
            Dim sw = Diagnostics.Stopwatch.StartNew()
            ArtStyles.Apply(pv, ArtStyle.MangaColor, 100, 50, 50)
            Dim first = sw.ElapsedMilliseconds
            sw.Restart()
            Using again As New Bitmap(1600, 1067, PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(again)
                    Using br As New Drawing2D.LinearGradientBrush(New Rectangle(0, 0, 1600, 1067), Color.SteelBlue, Color.Wheat, 45.0F)
                        g.FillRectangle(br, 0, 0, 1600, 1067)
                    End Using
                End Using
                ArtStyles.Apply(again, ArtStyle.MangaColor, 100, 50, 50)
                Dim second = sw.ElapsedMilliseconds
                Console.WriteLine($"    （1600×1067 彩色漫畫：第一次 {first} ms、同樣的畫面再算 {second} ms）")
                Check("同樣的畫面與參數再算一次直接用快取", second < first \ 3 + 20, $"{first} / {second}")
                Check("快取的結果和第一次相同", Diff(pv, again) = 0)
            End Using
        End Using
    End Sub
End Module
