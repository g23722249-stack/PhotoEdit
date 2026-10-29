Imports System.Drawing.Imaging
Imports OpenCvSharp
Imports PhotoEdit

''' <summary>去背的邊緣精修與去除邊緣殘色：合成一張知道正確透明度的圖來量。</summary>
Module CutoutRefineTests

    Sub CutoutRefineTestsRun()
        Console.WriteLine("— 去背邊緣精修 —")
        Dim w = 320, h = 240
        ' 正確的透明度：一個圓（頭）加上往外的細線（髮絲）
        Dim gt(w * h - 1) As Single
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim d = Math.Sqrt((x - 160) ^ 2 + (y - 130) ^ 2)
                Dim a = Math.Max(0, Math.Min(1, (70 - d) / 1.5))
                ' 髮絲：12 條 1.5 像素寬的放射線，從圓往外 40 像素
                Dim ang = Math.Atan2(y - 130, x - 160)
                Dim k = Math.Round(ang / (Math.PI / 6)) * (Math.PI / 6)
                Dim off = Math.Abs(Math.Sin(ang - k)) * d
                If d >= 60 AndAlso d < 110 AndAlso off < 1.2 Then a = Math.Max(a, 1 - off / 1.2)
                gt(y * w + x) = CSng(a)
            Next
        Next
        Dim fg = (B:=40.0F, G:=60.0F, R:=90.0F), bg = (B:=230.0F, G:=170.0F, R:=60.0F) ' 深棕主體、亮藍背景
        Dim src(w * h * 4 - 1) As Byte
        For i = 0 To w * h - 1
            Dim a = gt(i)
            src(i * 4) = CByte(fg.B * a + bg.B * (1 - a)) : src(i * 4 + 1) = CByte(fg.G * a + bg.G * (1 - a)) : src(i * 4 + 2) = CByte(fg.R * a + bg.R * (1 - a)) : src(i * 4 + 3) = 255
        Next
        ' 粗遮罩：正確答案縮到 1/8 再放大（像 320 像素輸入的模型）
        Dim coarse As New Mat()
        Using gtM As New Mat(h, w, MatType.CV_32FC1), small As New Mat()
            Runtime.InteropServices.Marshal.Copy(gt, 0, gtM.Data, gt.Length)
            Cv2.Resize(gtM, small, New OpenCvSharp.Size(w \ 8, h \ 8), 0, 0, InterpolationFlags.Area)
            Cv2.Resize(small, coarse, New OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Linear)
            coarse.ConvertTo(coarse, MatType.CV_8UC1, 255)
        End Using
        Dim errOf = Function(m As Mat) As Double
                        Dim px(w * h - 1) As Byte
                        Runtime.InteropServices.Marshal.Copy(m.Data, px, 0, px.Length)
                        Dim s = 0.0, n = 0
                        For i = 0 To w * h - 1
                            Dim x = i Mod w, y = i \ w
                            Dim d = Math.Sqrt((x - 160) ^ 2 + (y - 130) ^ 2)
                            If d < 55 OrElse d > 115 Then Continue For ' 只量邊緣與髮絲那一圈
                            s += Math.Abs(px(i) / 255.0 - gt(i)) : n += 1
                        Next
                        Return s / Math.Max(1, n)
                    End Function
        Dim before = errOf(coarse)
        Using refined = coarse.Clone()
            CutoutCompositor.RefineEdges(refined, src, w, h, 80, 40) ' 粗遮罩是 1/8 → 模型輸入 40
            Dim after = errOf(refined)
            Check("邊緣精修：邊緣與髮絲那一圈的透明度更接近正確答案", after < before * 0.8, $"誤差 {before:0.000} → {after:0.000}")
            ' 圓的正中間不動、遠處背景不動
            Dim c0 = refined.At(Of Byte)(130, 160), far = refined.At(Of Byte)(5, 5)
            Check("邊緣精修：主體裡面、遠處背景不動", c0 >= 250 AndAlso far <= 5, $"中 {c0}、遠 {far}")

            ' 去除邊緣殘色：髮絲上的半透明像素，顏色裡的藍要變少
            Dim alpha(w * h - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(refined.Data, alpha, 0, alpha.Length)
            Dim fixedPx = DirectCast(src.Clone(), Byte())
            CutoutCompositor.Decontaminate(fixedPx, alpha, w, h, 6)
            Dim blueBefore = 0.0, blueAfter = 0.0, n = 0
            For i = 0 To w * h - 1
                If alpha(i) < 40 OrElse alpha(i) > 215 Then Continue For
                blueBefore += src(i * 4) - CInt(src(i * 4 + 2))
                blueAfter += fixedPx(i * 4) - CInt(fixedPx(i * 4 + 2))
                n += 1
            Next
            Check("去除邊緣殘色：半透明邊緣的顏色往主體靠（藍色殘留變少）", n > 50 AndAlso blueAfter < blueBefore * 0.6, $"{n} 點；藍減紅 {blueBefore / Math.Max(1, n):0} → {blueAfter / Math.Max(1, n):0}")
            Check("去除邊緣殘色：完全不透明、完全透明的像素不動", fixedPx((130 * w + 160) * 4) = src((130 * w + 160) * 4) AndAlso fixedPx((5 * w + 5) * 4) = src((5 * w + 5) * 4))
        End Using
        coarse.Dispose()

        ' 設定：舊文件沒有這兩個值 → 不精修、不去殘色
        Dim old = System.Text.Json.JsonSerializer.Deserialize(Of CutoutSettings)("{""Feather"":15}")
        Check("舊文件讀進來：邊緣精修 0、去除殘色關（維持原本的樣子）", old.EdgeRefine = 0 AndAlso Not old.EdgeDecontaminate)
        Dim cs As New CutoutSettings With {.EdgeRefine = 60, .EdgeDecontaminate = True}
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of CutoutSettings)(System.Text.Json.JsonSerializer.Serialize(cs))
        Check("存檔讀回：邊緣精修、去除殘色", back.EdgeRefine = 60 AndAlso back.EdgeDecontaminate)

        ' 去背模型：選單列出全部、舊文件的附屬檔名不變、新模型各自一個檔名
        Console.WriteLine("— 去背模型 —")
        Dim all = [Enum].GetValues(Of CutoutModel)()
        Check("模型選單列出全部模型、沒有重複", CutoutModels.Order.Length = all.Length AndAlso CutoutModels.Order.Distinct().Count() = all.Length)
        Check("舊模型的遮罩附屬檔名不變（舊文件讀得回來）",
              MaskStore.MaskPath("a.jpg", CutoutModel.General) = "a.jpg.pedit.mask-general.png" AndAlso MaskStore.MaskPath("a.jpg", CutoutModel.Human) = "a.jpg.pedit.mask-human.png")
        Check("每個模型的遮罩附屬檔、模型檔名都不同",
              all.Select(Function(m) MaskStore.MaskPath("a.jpg", m)).Distinct().Count() = all.Length AndAlso all.Select(Function(m) CutoutModels.FileName(m)).Distinct().Count() = all.Length)
        Dim parsed As CutoutModel
        Check("ToonOut 的遮罩附屬檔名", MaskStore.MaskPath("a.jpg", CutoutModel.ToonOut) = "a.jpg.pedit.mask-toonout.png")
        Check("專案檔裡的遮罩名稱讀得回新模型", [Enum].TryParse("birefnetlite", True, parsed) AndAlso parsed = CutoutModel.BiRefNetLite)
        Dim ok1 = New OpenCvSharp.Size() {BackgroundRemover.ModNetInputSize(4000, 3000), BackgroundRemover.ModNetInputSize(300, 1000), BackgroundRemover.ModNetInputSize(10000, 500)}
        Check("MODNet 輸入：短邊約 512、長邊最多 1536、都是 32 的倍數",
              ok1.All(Function(s) s.Width Mod 32 = 0 AndAlso s.Height Mod 32 = 0 AndAlso Math.Max(s.Width, s.Height) <= 1536) AndAlso
              ok1(0).Height = 512 AndAlso ok1(1).Height = 1536 AndAlso ok1(2).Width = 1536,String.Join(" ", ok1.Select(Function(s) $"{s.Width}×{s.Height}")))
        Check("有臉的照片優先用 MODNet，沒下載時退回 U²-Net",
              CutoutModels.Preferred(True, Function(m) True) = CutoutModel.ModNet AndAlso
              CutoutModels.Preferred(True, Function(m) m = CutoutModel.Human OrElse m = CutoutModel.General) = CutoutModel.Human)
        Check("一般優先用 BiRefNet，只有 IS-Net 時用 IS-Net、都沒有時給 BiRefNet（按下去提示下載）",
              CutoutModels.Preferred(False, Function(m) True) = CutoutModel.BiRefNetLite AndAlso
              CutoutModels.Preferred(False, Function(m) m = CutoutModel.General) = CutoutModel.General AndAlso
              CutoutModels.Preferred(False, Function(m) False) = CutoutModel.BiRefNetLite)
        ' 實際推論（有模型才跑；HR 太慢不在這裡跑）：深色圓在淺色背景上，中間要是主體、角落是背景
        Using pic As New Bitmap(400, 300)
            Using g = Graphics.FromImage(pic)
                g.Clear(Color.FromArgb(235, 235, 230))
                g.FillEllipse(New SolidBrush(Color.FromArgb(150, 40, 30)), 120, 70, 160, 160)
            End Using
            For Each m In {CutoutModel.BiRefNetLite, CutoutModel.ModNet, CutoutModel.ToonOut}
                If Not BackgroundRemover.ModelAvailable(m) Then
                    Console.WriteLine($"  （略過 {m}：沒有模型檔）")
                    Continue For
                End If
                Using mk = BackgroundRemover.ComputeMask(pic, m)
                    Dim c = mk.GetPixel(200, 150).R, corner = mk.GetPixel(5, 5).R
                    Check($"{m} 推論：遮罩和照片一樣大、背景角落是透明", mk.Width = 400 AndAlso mk.Height = 300 AndAlso corner < 30, $"中 {c}、角 {corner}")
                End Using
            Next
        End Using
    End Sub
End Module
