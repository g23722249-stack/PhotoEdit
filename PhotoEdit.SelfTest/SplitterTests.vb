Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>貼圖小幫手：背景色、拆分（合併距離、最小尺寸、閱讀順序）與背景色填充去背。</summary>
Module SplitterTests

    ''' <summary>白底上 2 列 × 2 張「貼圖」：紅圓（旁邊 4 像素處有一顆小愛心）、藍方塊、綠圓（中間白色眼睛）、黃方塊，外加一個 2 像素雜點。</summary>
    Private Function Sheet() As Bitmap
        Dim bmp As New Bitmap(200, 160, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
            g.FillEllipse(Brushes.Red, 10, 10, 50, 50)
            g.FillRectangle(Brushes.DeepPink, 64, 12, 6, 6)          ' 小裝飾，離紅圓 4 像素
            g.FillRectangle(Brushes.Blue, 120, 15, 60, 40)
            g.FillEllipse(Brushes.Green, 15, 90, 50, 50)
            g.FillEllipse(Brushes.White, 32, 107, 16, 16)            ' 被包住的白色（眼睛）
            g.FillRectangle(Brushes.Gold, 125, 95, 45, 45)
            g.FillRectangle(Brushes.Black, 100, 150, 2, 2)           ' 雜點
        End Using
        Return bmp
    End Function

    Sub SplitterTestsRun()
        Console.WriteLine("貼圖小幫手")
        Using bmp = Sheet()
            Dim px = StickerSplitter.ReadPixels(bmp)
            Dim w = bmp.Width, h = bmp.Height
            Dim bg = StickerSplitter.BorderColor(px, w, h)
            Check("背景色：取四邊最常見的白色", bg.R > 240 AndAlso bg.G > 240 AndAlso bg.B > 240)

            Dim boxes = StickerSplitter.Split(px, w, h, bg, mergeDistance:=6, minSize:=8)
            Check("拆成 4 張（雜點太小不算）", boxes.Count = 4, boxes.Count.ToString())
            If boxes.Count = 4 Then
                Check("閱讀順序：紅、藍、綠、黃", boxes(0).X < 20 AndAlso boxes(1).X > 100 AndAlso boxes(2).Y > 80 AndAlso boxes(2).X < 30 AndAlso boxes(3).X > 100)
                Check("範圍 = 有顏色的最大範圍（紅圓含旁邊的小裝飾）", boxes(0).Left >= 9 AndAlso boxes(0).Left <= 11 AndAlso boxes(0).Right >= 70 AndAlso boxes(0).Right <= 71)
                Check("藍方塊範圍剛好", boxes(1) = New Rectangle(120, 15, 60, 40), boxes(1).ToString())
            End If
            Dim apart = StickerSplitter.Split(px, w, h, bg, mergeDistance:=0, minSize:=4)
            Check("合併距離 0：小裝飾拆成獨立的一張", apart.Count = 5, apart.Count.ToString())

            ' 背景色填充：外面的白去掉、被綠圓包住的白眼睛保留
            Using crop = StickerSplitter.Crop(bmp, New Rectangle(10, 85, 60, 60))
                Dim cp = StickerSplitter.ReadPixels(crop)
                Dim sel = MagicWand.EdgeFill(cp, crop.Width, crop.Height, 15, 0)
                Check("背景色填充：四角的白色背景選到", sel(0) = 255 AndAlso sel(cp.Length \ 4 - 1) = 255)
                Check("背景色填充：被包住的白色眼睛不選", sel((30) * crop.Width + 30) = 0)
                Check("背景色填充：綠色主體不選", sel(10 * crop.Width + 30) = 0)
                Dim s As New CutoutSettings With {.EdgeFill = True, .Feather = 0, .Background = CutoutBackground.Transparent}
                Using cut = CutoutCompositor.Compose(crop, Nothing, s)
                    Check("去背結果：角落透明、眼睛不透明", cut.GetPixel(1, 1).A = 0 AndAlso cut.GetPixel(30, 30).A = 255 AndAlso cut.GetPixel(30, 30).R > 240)
                    Using trimmed = StickerSplitter.TrimTransparent(cut, padding:=0)
                        Check("剪掉透明邊：剩下綠圓的大小", trimmed IsNot Nothing AndAlso Math.Abs(trimmed.Width - 50) <= 2 AndAlso Math.Abs(trimmed.Height - 50) <= 2,
                              If(trimmed Is Nothing, "Nothing", trimmed.Size.ToString()))
                    End Using
                End Using
            End Using
        End Using

        ' 透明背景的 PNG：以透明當背景
        Using bmp As New Bitmap(80, 40, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.Transparent)
                g.FillRectangle(Brushes.White, 5, 5, 20, 20)
                g.FillRectangle(Brushes.Black, 50, 10, 20, 20)
            End Using
            Dim px = StickerSplitter.ReadPixels(bmp)
            Dim bg = StickerSplitter.BorderColor(px, 80, 40)
            Check("透明背景：背景判定為透明", bg.A = 0)
            Check("透明背景：白色貼圖也拆得出來", StickerSplitter.Split(px, 80, 40, bg, 2, 4).Count = 2)
        End Using

        ' 靠太近、中間被一串小點（速度線、音符）連起來的三張：自動在最空的地方切開
        Using bmp As New Bitmap(440, 240, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.White)
                For i = 0 To 3
                    g.FillEllipse(Brushes.Crimson, 20 + i * 105, 15, 80, 85)
                Next
                For i = 0 To 2
                    g.FillEllipse(Brushes.SteelBlue, 15 + i * 92, 130, 78, 90)
                Next
                For x = 92 To 290 Step 6
                    g.FillRectangle(Brushes.Black, x, 175, 3, 3)
                Next
                g.FillEllipse(Brushes.SeaGreen, 345, 130, 80, 90)
            End Using
            Dim px = StickerSplitter.ReadPixels(bmp)
            Dim bg = StickerSplitter.BorderColor(px, 440, 240)
            Dim boxes = StickerSplitter.Split(px, 440, 240, bg, 6, 8)
            Check("連在一起的三張自動切開（共 8 張）", boxes.Count = 8, boxes.Count.ToString())
            Check("切開的每張寬度接近一般貼圖", boxes.Where(Function(b) b.Y > 120).All(Function(b) b.Width >= 60 AndAlso b.Width <= 100),
                  String.Join(" ", boxes.Select(Function(b) b.Width)))
            Dim manual = StickerSplitter.SplitRect(px, 440, 240, bg, New Rectangle(10, 125, 290, 100), 3, horizontal:=True)
            Check("手動左右拆成 3 張", manual.Count = 3 AndAlso manual.All(Function(b) b.Width >= 60))
        End Using

        Dim order = StickerSplitter.ReadingOrder({New Rectangle(100, 5, 10, 10), New Rectangle(5, 60, 10, 10), New Rectangle(5, 0, 10, 20)})
        Check("閱讀順序：同一列由左而右，再換下一列", order(0).X = 5 AndAlso order(0).Y = 0 AndAlso order(1).X = 100 AndAlso order(2).Y = 60)
    End Sub
End Module
