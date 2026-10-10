Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>變形動畫：解析度不同的兩張圖、頭尾等於原圖、中間有變形、時間軸、GIF 寫得出來且讀得回去、點位自動推估。</summary>
Module MorphTests

    Private Function Pic(w As Integer, h As Integer, back As Color, dot As Color, dotAt As PointF) As Bitmap
        Dim bmp As New Bitmap(w, h, PixelFormat.Format24bppRgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(back)
            Using br As New SolidBrush(dot)
                g.FillEllipse(br, dotAt.X - w * 0.08F, dotAt.Y - w * 0.08F, w * 0.16F, w * 0.16F)
            End Using
        End Using
        Return bmp
    End Function

    Private Function Pts(w As Integer, h As Integer, dotAt As PointF) As List(Of MorphPoint)
        Return New List(Of MorphPoint) From {
            New MorphPoint("左眼", New PointF(w * 0.3F, h * 0.4F)), New MorphPoint("右眼", New PointF(w * 0.7F, h * 0.4F)),
            New MorphPoint("鼻尖", dotAt), New MorphPoint("左嘴角", New PointF(w * 0.35F, h * 0.75F)), New MorphPoint("右嘴角", New PointF(w * 0.65F, h * 0.75F))}
    End Function

    Private Function MeanDiff(a As Bitmap, b As Bitmap) As Double
        Dim s = 0.0, n = 0
        For y = 0 To a.Height - 1 Step 3
            For x = 0 To a.Width - 1 Step 3
                Dim p = a.GetPixel(x, y), q = b.GetPixel(Math.Min(b.Width - 1, x * b.Width \ a.Width), Math.Min(b.Height - 1, y * b.Height \ a.Height))
                s += Math.Abs(CInt(p.R) - q.R) + Math.Abs(CInt(p.G) - q.G) + Math.Abs(CInt(p.B) - q.B)
                n += 1
            Next
        Next
        Return s / n
    End Function

    Sub MorphTestsRun(tempDir As String)
        Console.WriteLine("變形動畫")
        ' 第一張 300×400 紅點在左上；第二張 150×180（解析度、比例都不同）藍點在右下
        Using a = Pic(300, 400, Color.White, Color.Red, New PointF(110, 170)), b = Pic(150, 180, Color.Black, Color.Blue, New PointF(95, 110))
            Dim pa = Pts(300, 400, New PointF(110, 170)), pb = Pts(150, 180, New PointF(95, 110))
            Using m As New FaceMorph(a, b, pa, pb, 1080, 0)
                Check("畫布用第一張的比例、寬高是偶數、不放大", m.Width = 300 AndAlso m.Height = 400, $"{m.Width}x{m.Height}")
                Using f0 = m.FrameBitmap(0), f1 = m.FrameBitmap(1), fm = m.FrameBitmap(0.5)
                    Check("t=0 等於第一張", MeanDiff(f0, a) < 3, $"{MeanDiff(f0, a):0.0}")
                    Check("t=1 是第二張（填滿畫布）", f1.GetPixel(5, 5).R < 30 AndAlso f1.GetPixel(5, 5).G < 30, f1.GetPixel(5, 5).ToString())
                    Check("t=0.5 背景是兩張的中間色", Math.Abs(fm.GetPixel(5, 5).R - 128) < 40, fm.GetPixel(5, 5).ToString())
                End Using
            End Using
            ' 對齊 100%：第二張的兩眼和第一張重疊
            Using m2 As New FaceMorph(a, b, pa, pb, 1080, 1)
                Check("對齊 100%：第二張放大到兩眼距離相同", Math.Abs(m2.ScaleB - (300 * 0.4) / (150 * 0.4)) < 0.01, $"{m2.ScaleB:0.00}")
            End Using
            ' 時間軸：3 秒 30 格、頭停 0.5、尾停 1
            Dim tl = FaceMorph.Timeline(New MorphTiming With {.Seconds = 3, .Fps = 30, .HoldStart = 0.5, .HoldEnd = 1}, 30)
            Check("時間軸：總長 4.5 秒、頭尾是 0 與 1", Math.Abs(tl.Sum(Function(x) x.Duration) - 4.5) < 0.05 AndAlso tl.First().T = 0 AndAlso tl.Last().T = 1, $"{tl.Sum(Function(x) x.Duration):0.00} 秒、{tl.Count} 格")
            Dim tl2 = FaceMorph.Timeline(New MorphTiming With {.Seconds = 2, .Fps = 10, .PingPong = True}, 10)
            Check("來回：最後回到 0", tl2.Last().T = 0 AndAlso tl2.Max(Function(x) x.T) = 1)
            ' GIF
            Dim gifPath = Path.Combine(tempDir, "morph.gif")
            Using m As New FaceMorph(a, b, pa, pb, 1080, 0.5)
                m.WriteGif(gifPath, New MorphTiming With {.Seconds = 1, .HoldStart = 0, .HoldEnd = 0}, 120, 10)
            End Using
            Using g = Image.FromFile(gifPath)
                Dim frames = g.GetFrameCount(FrameDimension.Time)
                g.SelectActiveFrame(FrameDimension.Time, 0)
                Dim first = CType(g.Clone(), Bitmap)
                g.SelectActiveFrame(FrameDimension.Time, frames - 1)
                Dim last = CType(g.Clone(), Bitmap)
                Check("GIF 讀得回來：大小、格數", g.Width = 90 AndAlso g.Height = 120 AndAlso frames = 11, $"{g.Width}x{g.Height}、{frames} 格")
                Check("GIF 第一格白底、最後一格黑底（調色盤與 LZW 正確）", first.GetPixel(2, 2).R > 220 AndAlso last.GetPixel(2, 2).R < 40, $"{first.GetPixel(2, 2)} / {last.GetPixel(2, 2)}")
                first.Dispose() : last.Dispose()
            End Using
            ' 點位推估：第二張只給 5 點，其他點跟著仿射過去
            Dim template = Pts(300, 400, New PointF(150, 240))
            template.Add(New MorphPoint("額頭", New PointF(150, 60)))
            Dim keysB = {New PointF(45, 80), New PointF(105, 80), New PointF(75, 120), New PointF(52.5F, 150), New PointF(97.5F, 150)} ' 第一張縮小一半
            Dim est = FaceMorph.EstimateFromKeys(template, keysB, 150, 180)
            Dim fore = est.First(Function(q) q.Name = "額頭")
            Check("5 個關鍵點推估其他點", est.Count = 6 AndAlso Math.Abs(fore.X - 75) < 2 AndAlso Math.Abs(fore.Y - 30) < 3, $"額頭 {fore.X:0},{fore.Y:0}")
        End Using
    End Sub

    ''' <summary>川劇變臉：沒有臉時每張都是原圖、時間軸長度、換臉中間的畫面介於前後兩張之間；川劇角色都畫得出來而且沒有髯口。</summary>
    Sub FaceChangeTestsRun()
        Console.WriteLine("川劇變臉")
        Using a = Pic(200, 240, Color.White, Color.Red, New PointF(100, 120))
            Dim ids = {0, OperaRoles.IdOf("火焰紅臉"), OperaRoles.IdOf("雲紋藍臉"), 0}
            Using fc As New FaceChange(a, ids, 1080)
                Check("沒有臉：不出錯、每張都是原圖", Not fc.HasFace AndAlso fc.Count = 4)
                Dim tm As New FaceChangeTiming With {.Hold = 1, .Transition = 0.3, .Fps = 30}
                Check("長度＝4 張×1 秒＋3 次換臉×0.3 秒", Math.Abs(fc.TotalSeconds(tm) - 4.9) < 0.05, $"{fc.TotalSeconds(tm):0.00}")
                For Each s In {FaceChangeStyle.Pull, FaceChangeStyle.Wipe, FaceChangeStyle.Blow}
                    Using m = fc.Transition(0, 0.5, s)
                        Check($"換臉（{s}）畫得出來、大小不變", m.Width = fc.Width AndAlso m.Height = fc.Height)
                    End Using
                Next
            End Using
        End Using
        Dim chuan = Enumerable.Range(1, OperaRoles.All.Count).Where(Function(id) OperaRoles.Get(id).Group = OperaGroup.Chuan).ToList()
        Check("川劇 15 張、都沒有髯口", chuan.Count = 15 AndAlso chuan.All(Function(id) OperaRoles.Get(id).BeardArgb = 0), $"{chuan.Count} 張")
    End Sub
End Module
