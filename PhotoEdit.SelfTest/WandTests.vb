Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>魔術棒：顏色門檻、相連／全圖、邊緣漸層、清除雜點、套用到遮罩與完整去背合成。</summary>
Module WandTests

    ''' <summary>測試圖：左半白、右半紅，右邊中間有一塊不相連的白色方塊，左邊有一點淡灰。</summary>
    Private Function TestImage() As Bitmap
        Dim bmp As New Bitmap(100, 60, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
            g.FillRectangle(Brushes.Red, 50, 0, 50, 60)
            g.FillRectangle(Brushes.White, 70, 20, 15, 15)
            g.FillRectangle(New SolidBrush(Color.FromArgb(235, 235, 235)), 10, 10, 6, 6)
        End Using
        Return bmp
    End Function

    Private Function Pixels(bmp As Bitmap) As Byte()
        Dim px(bmp.Width * bmp.Height * 4 - 1) As Byte
        Dim data = bmp.LockBits(New Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        For y = 0 To bmp.Height - 1
            Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bmp.Width * 4, bmp.Width * 4)
        Next
        bmp.UnlockBits(data)
        Return px
    End Function

    Sub WandTestsRun()
        Console.WriteLine("魔術棒")
        Check("相似程度 0 只容許幾乎相同的顏色、100 全選", MagicWand.Threshold(0) < 1 AndAlso MagicWand.Threshold(100) >= MagicWand.MaxDistance)
        Check("顏色距離：白與白為 0、黑白最大", MagicWand.Distance(255, 255, 255, 255, 255, 255) = 0 AndAlso
              Math.Abs(MagicWand.Distance(0, 0, 0, 255, 255, 255) - MagicWand.MaxDistance) < 1)

        Using bmp = TestImage()
            Dim px = Pixels(bmp)
            Dim w = bmp.Width, h = bmp.Height
            Dim at = Function(region As Byte(), x As Integer, y As Integer) region(y * w + x)

            Dim near = MagicWand.SelectRegion(px, w, h, New WandClick With {.X = 0.1, .Y = 0.8, .Tolerance = 20, .Contiguous = True})
            Check("相連：選到點擊處所在的白色區域", at(near, 5, 50) = 255 AndAlso at(near, 45, 5) = 255)
            Check("相連：不選紅色", at(near, 60, 5) = 0)
            Check("相連：不選不相連的白色方塊", at(near, 77, 27) = 0)
            Check("相似程度 20：淡灰也選進來", at(near, 12, 12) = 255)

            Dim exact = MagicWand.SelectRegion(px, w, h, New WandClick With {.X = 0.1, .Y = 0.8, .Tolerance = 0, .Contiguous = True})
            Check("相似程度 0：淡灰不選", at(exact, 12, 12) = 0 AndAlso at(exact, 5, 50) = 255)

            Dim all = MagicWand.SelectRegion(px, w, h, New WandClick With {.X = 0.1, .Y = 0.8, .Tolerance = 20, .Contiguous = False})
            Check("全圖：不相連的白色方塊也選到", at(all, 77, 27) = 255 AndAlso at(all, 60, 5) = 0)

            Dim red = MagicWand.SelectRegion(px, w, h, New WandClick With {.X = 0.9, .Y = 0.1, .Tolerance = 20})
            Check("點紅色：選紅色、不選中間的白塊與左邊", at(red, 95, 55) = 255 AndAlso at(red, 77, 27) = 0 AndAlso at(red, 5, 5) = 0)

            ' 清除雜點
            Dim sel(w * h - 1) As Byte
            For i = 0 To sel.Length - 1
                sel(i) = If(i Mod w < 50, CByte(255), CByte(0))
            Next
            sel(30 * w + 20) = 0 : sel(30 * w + 21) = 0             ' 選取裡的小破洞
            sel(10 * w + 80) = 255                                  ' 選取外的小雜點
            MagicWand.Despeckle(sel, w, h)
            Check("清除雜點：補滿小破洞", sel(30 * w + 20) = 255 AndAlso sel(30 * w + 21) = 255)
            Check("清除雜點：去掉零星小點", sel(10 * w + 80) = 0)
            Check("清除雜點：大區域不受影響", sel(5 * w + 10) = 255 AndAlso sel(5 * w + 90) = 0)

            ' 套用到遮罩
            Dim mask = Enumerable.Repeat(CByte(255), w * h).ToArray()
            MagicWand.ApplyToMask(mask, near, restore:=False)
            Check("去除：選到的地方遮罩變 0", mask(50 * w + 5) = 0 AndAlso mask(5 * w + 60) = 255)
            MagicWand.ApplyToMask(mask, near, restore:=True)
            Check("補回：選到的地方遮罩回到 255", mask(50 * w + 5) = 255)

            ' 完整合成：去背設定只有魔術棒（沒有 AI 遮罩）
            Dim settings As New CutoutSettings With {.Background = CutoutBackground.Transparent, .Feather = 0}
            settings.Wand.Add(New WandClick With {.X = 0.1, .Y = 0.8, .Tolerance = 20})
            Using out = CutoutCompositor.Compose(bmp, Nothing, settings)
                Check("沒有 AI 遮罩也能用魔術棒去背：白色變透明、紅色保留", out.GetPixel(5, 50).A = 0 AndAlso out.GetPixel(60, 5).A = 255)
                Check("不相連的白色方塊保留", out.GetPixel(77, 27).A = 255)
            End Using
            settings.Wand.Add(New WandClick With {.X = 0.1, .Y = 0.8, .Tolerance = 20, .Restore = True})
            Using out = CutoutCompositor.Compose(bmp, Nothing, settings)
                Check("第二次點擊補回：白色回來", out.GetPixel(5, 50).A = 255)
            End Using
        End Using

        Dim r As New EditRecipe With {.Cutout = New CutoutSettings()}
        r.Cutout.Wand.Add(New WandClick With {.X = 0.25, .Y = 0.5, .Tolerance = 33, .Contiguous = False, .Restore = True})
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(r))
        Check("魔術棒點擊 JSON 來回轉換一致", back.Equals(r) AndAlso back.Cutout.Wand(0).Tolerance = 33 AndAlso back.Cutout.Wand(0).Restore)
        Dim c = r.Clone()
        c.Cutout.Wand(0).Tolerance = 80
        Check("Clone 為深複製（魔術棒點擊不共用）", r.Cutout.Wand(0).Tolerance = 33)
    End Sub
End Module
