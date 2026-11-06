Imports System.Drawing
Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>影像 → 吶喊（孟克）…：人物圖層（裁切、翻轉、色彩、只調整臉）、建議位置與配色、畫布大小、遮罩變形、內建背景。</summary>
Module ScreamStudioTests

    ''' <summary>200×300 的「人」：灰底上，膚色頭（臉框 70..130 × 40..120）＋藍色身體延伸到最底列（半身像）。遮罩同形狀。</summary>
    Private Function Person(ByRef mask As Byte()) As Bitmap
        Dim bmp As New Bitmap(200, 300, PixelFormat.Format32bppArgb)
        Dim m(200 * 300 - 1) As Byte
        For y = 0 To 299
            For x = 0 To 199
                Dim head = ((x - 100) / 30.0) ^ 2 + ((y - 80) / 40.0) ^ 2 <= 1
                Dim body = Math.Abs(x - 100) <= 60 AndAlso y >= 130
                Dim c = If(head, Color.FromArgb(210, 165, 140), If(body, Color.FromArgb(40, 60, 140), Color.FromArgb(150, 150, 150)))
                bmp.SetPixel(x, y, c)
                If head OrElse body Then m(y * 200 + x) = 255
            Next
        Next
        mask = m
        Return bmp
    End Function

    Private Function Face() As FaceRegion
        Return New FaceRegion With {.Box = New RectangleF(70 / 200.0F, 40 / 300.0F, 60 / 200.0F, 80 / 300.0F)}
    End Function

    Sub ScreamStudioTestsRun()
        Console.WriteLine("吶喊（孟克）")
        Dim mask As Byte() = Nothing
        Using src = Person(mask)
            Dim faces = New List(Of FaceRegion) From {Face()}
            Using plain = ScreamStudio.BuildLayer(src, mask, faces, New ScreamPersonColor(), 0, False)
                Check("人物圖層：裁到人的範圍（含 2 像素邊）、外面透明、人不透明",
                      Math.Abs(plain.Width - 125) <= 2 AndAlso Math.Abs(plain.Height - 262) <= 2 AndAlso plain.GetPixel(2, 2).A = 0 AndAlso plain.GetPixel(plain.Width \ 2, plain.Height - 5).A = 255,
                      $"{plain.Width}×{plain.Height}")
                Using flipped = ScreamStudio.BuildLayer(src, mask, faces, New ScreamPersonColor(), 0, True)
                    Check("人物圖層：左右翻轉", flipped.GetPixel(10, plain.Height - 5) = plain.GetPixel(plain.Width - 11, plain.Height - 5))
                End Using
                Dim faceAt = New Point(100 - 38 + 2, 80 - 40 + 2) ' 臉中心在圖層裡的位置（圖層從 (38, 38) 開始）
                Dim shirtAt = New Point(plain.Width \ 2, plain.Height - 10)
                Using hued = ScreamStudio.BuildLayer(src, mask, faces, New ScreamPersonColor With {.Hue = 120}, 0, False)
                    Dim a = plain.GetPixel(faceAt.X, faceAt.Y), b = hued.GetPixel(faceAt.X, faceAt.Y)
                    Dim la = 0.299 * a.R + 0.587 * a.G + 0.114 * a.B, lb = 0.299 * b.R + 0.587 * b.G + 0.114 * b.B
                    Check("色相：顏色換了、亮度差不多", a <> b AndAlso Math.Abs(la - lb) < 12, $"{a} → {b}")
                End Using
                Using bright = ScreamStudio.BuildLayer(src, mask, faces, New ScreamPersonColor With {.Brightness = 50, .Contrast = 30}, 0, False),
                      faceOnly = ScreamStudio.BuildLayer(src, mask, faces, New ScreamPersonColor With {.Brightness = 50, .FaceOnly = True}, 0, False)
                    Check("亮度：人變亮", bright.GetPixel(shirtAt.X, shirtAt.Y).GetBrightness() > plain.GetPixel(shirtAt.X, shirtAt.Y).GetBrightness() + 0.08)
                    Check("只調整臉：臉變亮、衣服不變", faceOnly.GetPixel(faceAt.X, faceAt.Y).GetBrightness() > plain.GetPixel(faceAt.X, faceAt.Y).GetBrightness() + 0.08 AndAlso
                                                      faceOnly.GetPixel(shirtAt.X, shirtAt.Y) = plain.GetPixel(shirtAt.X, shirtAt.Y))
                End Using
                Using soft = ScreamStudio.BuildLayer(src, mask, faces, New ScreamPersonColor(), 100, False)
                    Dim edge = soft.GetPixel(soft.Width \ 2 - 60 + 1, soft.Height - 5).A ' 身體左緣附近
                    Check("邊緣柔和：邊緣變成半透明", edge > 0 AndAlso edge < 255 OrElse soft.Width > plain.Width, $"{edge}")
                End Using
            End Using
            Check("半身像：人碰到圖的下緣", ScreamStudio.CroppedAtBottom(mask, 200, 300))

            ' 身上的皮膚：下巴以下、顏色接近臉頰的地方變暖色；不是皮膚（藍衣服）、下巴以上不動
            Using bare = CType(src.Clone(), Bitmap)
                For y = 180 To 220
                    For x = 60 To 140
                        bare.SetPixel(x, y, Color.FromArgb(210, 165, 140))
                    Next
                Next
                Using warm = ScreamStudio.WarmSkin(bare, faces, 100)
                    Dim chest = warm.GetPixel(100, 200), shirt = warm.GetPixel(100, 280), head = warm.GetPixel(100, 80)
                    Check("身上的皮膚變暖色、衣服和臉不動", chest <> bare.GetPixel(100, 200) AndAlso CInt(chest.R) - chest.B > 90 AndAlso shirt = bare.GetPixel(100, 280) AndAlso head = bare.GetPixel(100, 80), $"{chest}")
                End Using
                Using same = ScreamStudio.WarmSkin(bare, faces, 0)
                    Check("臉的強度 0：皮膚不變", same.GetPixel(100, 200) = bare.GetPixel(100, 200))
                End Using
            End Using
            ' 身體扭曲：頭不動、身體變窄、往旁邊擺；圖和遮罩用同一個變形
            Dim rowWidth = Function(m As Byte(), y As Integer) Enumerable.Range(0, 200).Where(Function(x) m(y * 200 + x) >= 128).Count()
            Dim rowCenter = Function(m As Byte(), y As Integer) Enumerable.Range(0, 200).Where(Function(x) m(y * 200 + x) >= 128).DefaultIfEmpty(0).Average()
            Dim twisted = ScreamStudio.BodyWarpMask(mask, 200, 300, faces, 100)
            Check("身體扭曲：頭不動、身體變窄、有往旁邊擺", rowWidth(twisted, 80) = rowWidth(mask, 80) AndAlso rowWidth(twisted, 230) < rowWidth(mask, 230) * 0.8 AndAlso
                  Math.Abs(rowCenter(twisted, 200) - rowCenter(twisted, 285)) > 4, $"身體寬 {rowWidth(mask, 230)} → {rowWidth(twisted, 230)}，中心 {rowCenter(twisted, 200):0} / {rowCenter(twisted, 285):0}")
            Check("身體扭曲 0、沒有臉：遮罩不變", ScreamStudio.BodyWarpMask(mask, 200, 300, faces, 0) Is mask AndAlso ScreamStudio.BodyWarpMask(mask, 200, 300, New List(Of FaceRegion)(), 100) Is mask)
            Using bent = ScreamStudio.BodyWarp(src, faces, 100)
                Dim overlap = Enumerable.Range(0, 200).Count(Function(x) twisted(230 * 200 + x) >= 200 AndAlso bent.GetPixel(x, 230).B > bent.GetPixel(x, 230).R)
                Check("身體扭曲：圖和遮罩對得上（遮罩裡是藍衣服）", overlap >= rowWidth(twisted, 230) * 0.8, $"{overlap}/{rowWidth(twisted, 230)}")
            End Using
            ' 油畫：有筆觸（平塗的地方也會有明暗變化），0 不變
            Using flat = CType(src.Clone(), Bitmap), keep = CType(src.Clone(), Bitmap)
                ScreamStudio.OilPaint(flat, 100, faces)
                ScreamStudio.OilPaint(keep, 0, faces)
                Dim tones = Enumerable.Range(0, 60).Select(Function(i) flat.GetPixel(70 + i, 250).GetBrightness()).ToArray()
                Check("油畫感：平塗的衣服出現筆觸；0 不變", tones.Max() - tones.Min() > 0.02 AndAlso keep.GetPixel(100, 250) = src.GetPixel(100, 250), $"{tones.Max() - tones.Min():0.000}")
            End Using

            ' 建議配色：暗、暖的背景 → 人變暗、偏暖；有上限
            Using dark As New Bitmap(100, 100, PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(dark)
                    g.Clear(Color.FromArgb(120, 50, 20))
                End Using
                Dim c = ScreamStudio.SuggestColor(src, mask, faces, dark, False)
                Check("建議配色：暗而暖的背景 → 調暗、色溫偏暖，都在上限內", c.Brightness < 0 AndAlso c.Temperature > 0 AndAlso c.Brightness >= -30 AndAlso c.Temperature <= 40 AndAlso Math.Abs(c.Hue) <= 20,
                      $"亮度 {c.Brightness}、色溫 {c.Temperature}、色相 {c.Hue}")
            End Using
        End Using

        ' 建議位置
        Dim half = ScreamStudio.SuggestPlacements(120, 260, True, 1000, 1000)
        Check("建議位置：三個（中間、左、右），半身像貼齊下緣", half.Count = 3 AndAlso Math.Abs(half(0).X - 0.5) < 0.001 AndAlso Math.Abs(half(0).Y + half(0).Size / 2 - 1) < 0.001 AndAlso
              half(1).X < 0.5 AndAlso half(2).X > 0.5)
        Dim full = ScreamStudio.SuggestPlacements(80, 300, False, 1000, 1000)
        Check("建議位置：全身像腳在下緣上方一點", full(0).Y + full(0).Size / 2 < 0.99 AndAlso full(0).Y + full(0).Size / 2 > 0.9)
        Dim wide = ScreamStudio.SuggestPlacements(900, 300, True, 1000, 1000)
        Check("建議位置：很寬的人會縮小，不超出畫布", wide(0).Size * 1000 * 3 <= 905, $"{wide(0).Size:0.00}")

        ' 畫布大小
        Using small As New Bitmap(1254, 1254), big As New Bitmap(4000, 3000)
            Dim s1 = ScreamStudio.CanvasSize(small), s2 = ScreamStudio.CanvasSize(big)
            Check("畫布：小的背景放大到長邊 2048，大的照原樣", s1.Width = 2048 AndAlso s1.Height = 2048 AndAlso s2.Width = 4000 AndAlso s2.Height = 3000, $"{s1} {s2}")
        End Using

        ' 遮罩跟著臉變形：68 點的臉，吶喊時臉頰往內收 → 臉頰外緣的遮罩變少
        Dim fm(400 * 200 - 1) As Byte
        For y = 0 To 199
            For x = 0 To 399
                If ((x - 110) / 48.0) ^ 2 + ((y - 80) / 80.0) ^ 2 <= 1 Then fm(y * 400 + x) = 255
            Next
        Next
        Dim df = DenseTestFace()
        Dim warped = ScreamStudio.WarpMask(fm, 400, 200, {df}, 100, New ScreamShape())
        Dim before = fm.Count(Function(v) v >= 128), after = warped.Count(Function(v) v >= 128)
        Check("遮罩跟著吶喊的臉變形（臉頰內凹，輪廓變了）；強度 0 不變", Not warped.SequenceEqual(fm) AndAlso ScreamStudio.WarpMask(fm, 400, 200, {df}, 0, New ScreamShape()) Is fm, $"{before} → {after}")

        ' 接上原作的手和身體
        If ScreamFigure.Available Then
            Using pic As New Bitmap(400, 200, PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(pic)
                    g.Clear(Color.FromArgb(210, 165, 140))
                End Using
                Dim all(400 * 200 - 1) As Byte
                Array.Fill(all, CByte(255))
                Dim fig = ScreamFigure.Build(pic, all, {df}, 100, 40, New ScreamShape(), 0)
                Check("原作身體：做得出人物，遮罩和圖一樣大，角落透明、身體不透明",
                      fig IsNot Nothing AndAlso fig.Alpha.Length = fig.Image.Width * fig.Image.Height AndAlso fig.Alpha(0) = 0 AndAlso
                      fig.Alpha(CInt(fig.Image.Height * 0.65) * fig.Image.Width + fig.Image.Width \ 2) = 255 AndAlso fig.Faces.Count = 1,
                      If(fig Is Nothing, "沒有結果", $"{fig.Image.Width}×{fig.Image.Height}"))
                Check("原作身體：站回原畫的位置（畫面中間偏右、下方）", fig IsNot Nothing AndAlso Math.Abs(fig.Spot.X - 0.57) < 0.05 AndAlso fig.Spot.Y > 0.6 AndAlso fig.Spot.Size > 0.4,
                      If(fig Is Nothing, "", $"({fig.Spot.X:0.00}, {fig.Spot.Y:0.00}) 高 {fig.Spot.Size:0.00}"))
                fig?.Image.Dispose()
                Check("原作身體：沒有臉部特徵點時不做", ScreamFigure.Build(pic, all, {New FaceRegion With {.Box = New RectangleF(0.2F, 0.2F, 0.3F, 0.5F)}}, 100, 40, New ScreamShape(), 0) Is Nothing)
            End Using
        Else
            Console.WriteLine("  （略過原作身體：沒有 backdrops\scream_original.jpg）")
        End If

        ' 內建背景
        Using art = ScreamStudio.DefaultBackdrop()
            If art Is Nothing Then
                Console.WriteLine("  （略過內建背景：沒有 backdrops\scream.webp）")
            Else
                ' 天空是紅黃相間的條紋，看一段的平均
                Dim sky = Enumerable.Range(0, 40).Select(Function(i) art.GetPixel(art.Width \ 2, art.Height * (3 + i \ 2) \ 100)).ToArray()
                Dim warm = sky.Average(Function(c) CInt(c.R) - c.B)
                Check("內建《吶喊》背景讀得到，上方是橘紅的天空", warm > 80, $"{art.Width}×{art.Height} 紅比藍多 {warm:0}")
            End If
        End Using
    End Sub

    ''' <summary>合成的 68 點（和美顏測試同一張臉：400×200 的圖，臉在左邊）。</summary>
    Private Function DenseTestFace() As FaceRegion
        Dim pts As New List(Of PointF)()
        For i = 0 To 16
            Dim t = i / 16.0 * Math.PI
            pts.Add(New PointF(CSng(110 - 48 * Math.Cos(t)), CSng(80 + 78 * Math.Sin(t))))
        Next
        For i = 0 To 4 : pts.Add(New PointF(76 + i * 6, 64)) : Next
        For i = 0 To 4 : pts.Add(New PointF(120 + i * 6, 64)) : Next
        For i = 0 To 3 : pts.Add(New PointF(110, 74 + i * 8.6F)) : Next
        For i = 0 To 4 : pts.Add(New PointF(102 + i * 4, 104)) : Next
        For Each c In {New PointF(88, 76), New PointF(132, 76)}
            For i = 0 To 5
                Dim a = i / 6.0 * 2 * Math.PI
                pts.Add(New PointF(CSng(c.X - 7 * Math.Cos(a)), CSng(c.Y - 3 * Math.Sin(a))))
            Next
        Next
        For i = 0 To 11
            Dim a = i / 12.0 * 2 * Math.PI
            pts.Add(New PointF(CSng(110 - 18 * Math.Cos(a)), CSng(132 - 8 * Math.Sin(a))))
        Next
        For i = 0 To 7
            Dim a = i / 8.0 * 2 * Math.PI
            pts.Add(New PointF(CSng(110 - 12 * Math.Cos(a)), CSng(132 - 2.5 * Math.Sin(a))))
        Next
        Return New FaceRegion With {.Box = New RectangleF(62 / 400.0F, 2 / 200.0F, 96 / 400.0F, 156 / 200.0F),
                                    .Dense = pts.Select(Function(p) New PointF(p.X / 400, p.Y / 200)).ToArray()}
    End Function
End Module
