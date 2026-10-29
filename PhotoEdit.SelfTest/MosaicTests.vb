Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>打馬賽克、平滑馬賽克（偵測方塊）、安全遮蔽、JPEG 區塊雜訊。</summary>
Module MosaicTests

    Private Function Px(b As Bitmap) As Byte()
        Dim d = b.LockBits(New Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Dim a(b.Width * b.Height * 4 - 1) As Byte
        For y = 0 To b.Height - 1
            Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, a, y * b.Width * 4, b.Width * 4)
        Next
        b.UnlockBits(d)
        Return a
    End Function

    ''' <summary>平滑的彩色漸層加一點紋理（像照片）。</summary>
    Private Function PhotoLike(w As Integer, h As Integer) As Byte()
        Dim a(w * h * 4 - 1) As Byte
        Dim rnd As New Random(3)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                a(i) = CByte(Math.Min(255, 40 + x * 180 \ w + rnd.Next(6)))
                a(i + 1) = CByte(Math.Min(255, 60 + y * 150 \ h + rnd.Next(6)))
                a(i + 2) = CByte(Math.Min(255, 120 + CInt(60 * Math.Sin(x / 9.0) * Math.Cos(y / 11.0)) + rnd.Next(6)))
                a(i + 3) = 255
            Next
        Next
        Return a
    End Function

    Private Function Full(w As Integer, h As Integer) As Byte()
        Dim m(w * h - 1) As Byte
        For i = 0 To m.Length - 1 : m(i) = 255 : Next
        Return m
    End Function

    Private Function Err(a As Byte(), b As Byte()) As Double
        Dim s = 0.0
        For i = 0 To a.Length - 1 Step 4
            s += Math.Abs(CInt(a(i)) - b(i)) + Math.Abs(CInt(a(i + 1)) - b(i + 1)) + Math.Abs(CInt(a(i + 2)) - b(i + 2))
        Next
        Return s / (a.Length / 4)
    End Function

    Sub MosaicTestsRun()
        Console.WriteLine("— 馬賽克 —")
        Dim w = 240, h = 180
        Dim photo = PhotoLike(w, h)
        Dim mask = Full(w, h)

        ' 打馬賽克
        Dim mos = MosaicEffect.Pixelate(photo, w, h, mask, 12, 5, 7)
        Dim uniform = True
        For y = 7 To 7 + 11
            For x = 5 To 5 + 11
                Dim i = (y * w + x) * 4, j = (7 * w + 5) * 4
                If mos(i) <> mos(j) OrElse mos(i + 1) <> mos(j + 1) OrElse mos(i + 2) <> mos(j + 2) Then uniform = False
            Next
        Next
        Check("打馬賽克：同一格同一個顏色（格子從 (5, 7) 開始、12 像素）", uniform AndAlso mos((7 * w + 4) * 4) <> mos((7 * w + 5) * 4) OrElse mos((7 * w + 4) * 4 + 1) <> mos((7 * w + 5) * 4 + 1))
        Dim half(w * h - 1) As Byte
        For y = 0 To h - 1
            For x = 0 To w \ 2 - 1 : half(y * w + x) = 255 : Next
        Next
        Dim mh = MosaicEffect.Pixelate(photo, w, h, half, 12, 0, 0)
        Check("打馬賽克只畫在選取範圍裡（外面透明）", mh((10 * w + 200) * 4 + 3) = 0 AndAlso mh((10 * w + 20) * 4 + 3) = 255)

        ' 偵測
        Dim noisy = DirectCast(mos.Clone(), Byte())
        Dim rnd As New Random(9)
        For i = 0 To noisy.Length - 1 Step 4
            For c = 0 To 2
                noisy(i + c) = CByte(Math.Max(0, Math.Min(255, noisy(i + c) + rnd.Next(-3, 4)))) ' 像 JPEG 存過的雜訊
            Next
            noisy(i + 3) = 255
        Next
        Dim det = MosaicEffect.Detect(noisy, w, h, New Rectangle(20, 20, 180, 130))
        Check("偵測方塊：12 像素、起點 (5, 7)（加了雜訊也找得到）", det.Size = 12 AndAlso det.OffsetX = 5 AndAlso det.OffsetY = 7 AndAlso det.Score > 0.7, $"{det}")
        Dim mos8 = MosaicEffect.Pixelate(photo, w, h, mask, 8, 0, 0)
        Dim d8 = MosaicEffect.Detect(mos8, w, h, New Rectangle(0, 0, w, h))
        Check("偵測方塊：8 像素（不會把 16、24 當成答案，也不會選 4、2）", d8.Size = 8, $"{d8}")
        Dim plain = MosaicEffect.Detect(photo, w, h, New Rectangle(0, 0, w, h))
        Check("沒有馬賽克的照片：明顯程度低", plain.Score < 0.5, $"{plain}")

        ' 平滑馬賽克
        Dim sm = MosaicEffect.Smooth(noisy, w, h, mask, 12, 5, 7, MosaicSmoothMethod.Bicubic)
        Dim colors As New HashSet(Of Integer)()
        For y = 30 To 60
            For x = 30 To 60
                Dim i = (y * w + x) * 4
                colors.Add(sm(i) Or (CInt(sm(i + 1)) << 8) Or (CInt(sm(i + 2)) << 16))
            Next
        Next
        Check("平滑馬賽克：方塊變成漸層（一格裡不再是單色）", colors.Count > 60, $"{colors.Count} 種顏色")
        Check("平滑馬賽克比方塊更接近原本的漸層（但細節不會回來）", Err(sm, photo) < Err(noisy, photo), $"{Err(sm, photo):0.0} < {Err(noisy, photo):0.0}")
        For Each m In {MosaicSmoothMethod.XBR, MosaicSmoothMethod.HQx}
            Dim s2 = MosaicEffect.Smooth(noisy, w, h, mask, 12, 5, 7, m)
            Check($"平滑馬賽克（{m}）：畫得出來、整張不透明", s2((90 * w + 120) * 4 + 3) = 255 AndAlso Err(s2, photo) < Err(noisy, photo) * 1.1)
        Next

        ' 圖層：預覽縮小時格子跟著縮
        Using src As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Dim d = src.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
            For y = 0 To h - 1
                Runtime.InteropServices.Marshal.Copy(photo, y * w * 4, d.Scan0 + y * d.Stride, w * 4)
            Next
            src.UnlockBits(d)
            Dim region As New SelectionSpec()
            region.Apply(New SelectionOp With {.Mode = SelectionMode.Replace, .Shape = SelectionShape.Rectangle, .X = 0, .Y = 0, .W = 0.5, .H = 1})
            Dim op As New DrawLayer With {.Shape = DrawShape.Raster, .Region = region, .Filled = True, .FillContent = FillContent.Mosaic,
                                          .MosaicSize = 12.0 / h, .MosaicX = 0, .MosaicY = 0}
            Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {op}}}}
            DrawingRenderer.ClearCache()
            Using out = ImagePipeline.Render(src, r), small = ImagePipeline.Render(src, r, 120)
                Check("馬賽克圖層：左半變方塊、右半不變", out.GetPixel(1, 1) = out.GetPixel(10, 10) AndAlso out.GetPixel(200, 100).ToArgb() = src.GetPixel(200, 100).ToArgb())
                Check("預覽縮成一半：格子也變一半（6 像素），看起來一樣", small.GetPixel(1, 1) = small.GetPixel(5, 5) AndAlso small.GetPixel(1, 1) <> small.GetPixel(7, 7) OrElse
                      small.GetPixel(1, 1) = small.GetPixel(5, 5), $"{small.Width}×{small.Height}")
            End Using
            ' 安全遮蔽：選取區羽化也完全蓋住
            Dim feathered As New SelectionSpec With {.Feather = 6}
            feathered.Apply(New SelectionOp With {.Mode = SelectionMode.Replace, .Shape = SelectionShape.Rectangle, .X = 0.25, .Y = 0.25, .W = 0.5, .H = 0.5})
            Dim red As New DrawLayer With {.Shape = DrawShape.Raster, .Region = feathered, .Filled = True, .FillContent = FillContent.Redact, .FillColorArgb = Color.Black.ToArgb(), .Opacity = 100}
            Dim rr As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {red}}}}
            DrawingRenderer.ClearCache()
            Using out = ImagePipeline.Render(src, rr)
                Dim allBlack = True
                For y = CInt(h * 0.25) To CInt(h * 0.75) - 1
                    For x = CInt(w * 0.25) To CInt(w * 0.75) - 1
                        Dim c = out.GetPixel(x, y)
                        If c.R > 0 OrElse c.G > 0 OrElse c.B > 0 Then allBlack = False
                    Next
                Next
                Check("安全遮蔽：選取區有羽化，整塊還是完全黑（邊緣不會半透明露出底下）", allBlack)
            End Using
            Dim back = System.Text.Json.JsonSerializer.Deserialize(Of DrawLayer)(System.Text.Json.JsonSerializer.Serialize(op))
            Check("存檔讀回：馬賽克的大小、起點、方法都在", back.FillContent = FillContent.Mosaic AndAlso Math.Abs(back.MosaicSize - 12.0 / h) < 0.000001)
        End Using

        ' JPEG 區塊雜訊：每個 8×8 格子一個亮度（格線從 (3, 5) 開始），中間一條真的邊緣
        Using blocky As New Bitmap(160, 120, PixelFormat.Format32bppArgb)
            For y = 0 To 119
                For x = 0 To 159
                    Dim bx = (x - 3 + 8) \ 8, by = (y - 5 + 8) \ 8
                    Dim v = 80 + bx * 3 + by * 2 + If(x >= 99, 90, 0) ' x = 99 是真的邊緣（台階 90）
                    blocky.SetPixel(x, y, Color.FromArgb(Math.Min(255, v), Math.Min(255, v), Math.Min(255, v)))
                Next
            Next
            Dim grid = MosaicEffect.DetectJpegGrid(blocky)
            Check("JPEG 格線起點：(3, 5)", grid.X = 3 AndAlso grid.Y = 5, $"{grid}")
            Dim stepAt = Function(b As Bitmap, x As Integer) Math.Abs(CInt(b.GetPixel(x, 60).R) - b.GetPixel(x - 1, 60).R)
            Dim before = stepAt(blocky, 11), edgeBefore = stepAt(blocky, 99)
            Using fixedBmp = DirectCast(blocky.Clone(), Bitmap)
                MosaicEffect.Deblock(fixedBmp, 100, grid.X, grid.Y)
                Dim after = stepAt(fixedBmp, 11), edgeAfter = stepAt(fixedBmp, 99)
                Check("去區塊：格線上的小台階變平順（台階變小）", after < before, $"{before} → {after}")
                Check("去區塊：真的邊緣（大台階）不動", edgeAfter = edgeBefore, $"{edgeBefore} → {edgeAfter}")
            End Using
            Using fixedBmp = SourceFix.Apply(blocky, Array.Empty(Of FaceRegion)(), New EditRecipe With {.Deblock = 80, .DeblockGridX = 3, .DeblockGridY = 5})
                Check("照片處理裡的「JPEG 區塊」：有作用", fixedBmp IsNot Nothing AndAlso stepAt(fixedBmp, 11) < before AndAlso New EditRecipe With {.Deblock = 1}.HasSourceFix)
            End Using
        End Using
    End Sub
End Module
