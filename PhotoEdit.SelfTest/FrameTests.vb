Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>邊框：22 種樣式、共用選項（顏色、取照片主色、下方加寬、陰影、內框線、圓角、比例、文字）。</summary>
Module FrameTests

    Private Function Photo(w As Integer, h As Integer, c As Color) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(b)
            g.Clear(c)
        End Using
        Return b
    End Function

    Private Function Near(a As Color, b As Color, tol As Integer) As Boolean
        Return Math.Abs(CInt(a.R) - b.R) <= tol AndAlso Math.Abs(CInt(a.G) - b.G) <= tol AndAlso Math.Abs(CInt(a.B) - b.B) <= tol
    End Function

    Sub FrameTestsRun()
        Console.WriteLine("— 邊框 —")
        Dim src = Photo(240, 160, Color.FromArgb(40, 120, 200))
        Dim bad As New List(Of String)()
        Dim transparentOk = True, opaqueOk = True
        For Each st In [Enum].GetValues(GetType(PhotoFrameStyle)).Cast(Of PhotoFrameStyle)().Where(Function(s) s <> PhotoFrameStyle.None)
            Dim r As New EditRecipe With {.Frame = st, .FrameSize = 50, .FrameText = "Camera" & vbLf & "Lens" & vbLf & "50mm  f/2" & vbLf & "2026.10.24"}
            DrawingRenderer.ClearCache()
            Using out = ImagePipeline.Render(src, r)
                Dim m = Creative.FrameMargins(r, 240, 160)
                Dim sizeOk = out.Width = 240 + m.Left + m.Right AndAlso out.Height = 160 + m.Top + m.Bottom
                Dim center = out.GetPixel(m.Left + 120, m.Top + 80)
                Dim tol = If(st = PhotoFrameStyle.Deckle, 45, 6)
                If Not sizeOk OrElse Not Near(center, Color.FromArgb(40, 120, 200), tol) Then bad.Add($"{Creative.FrameNames(CInt(st))}（{out.Width}×{out.Height}、中央 {center}）")
                Dim cornerA = out.GetPixel(0, 0).A
                If PhotoFrames.HasTransparentEdge(st) Then
                    If cornerA > 10 Then transparentOk = False : bad.Add($"{Creative.FrameNames(CInt(st))} 外角不透明")
                ElseIf st <> PhotoFrameStyle.SlideMount Then
                    If cornerA < 250 Then opaqueOk = False : bad.Add($"{Creative.FrameNames(CInt(st))} 外角透明")
                End If
            End Using
        Next
        Check("22 種邊框：尺寸＝照片＋四邊、照片原封不動在 (左, 上)（畫布座標換算靠這一點）", bad.Count = 0, String.Join("；", bad))
        Check("撕紙邊、郵票、花邊、卷軸的外角透明；其他不透明", transparentOk AndAlso opaqueOk)

        ' 主色
        Using out = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameColorArgb = Color.FromArgb(200, 30, 60).ToArgb()})
            Check("自選顏色：白邊變成選的顏色", Near(out.GetPixel(2, 2), Color.FromArgb(200, 30, 60), 2), $"{out.GetPixel(2, 2)}")
        End Using
        Using out = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameAutoColor = True, .FrameColorArgb = Color.Red.ToArgb()})
            Check("取照片主色：邊框是照片的藍（優先於自選色）", Near(out.GetPixel(2, 2), Color.FromArgb(40, 120, 200), 12), $"{out.GetPixel(2, 2)}")
        End Using
        Using two As New Bitmap(200, 100)
            Using g = Graphics.FromImage(two)
                g.Clear(Color.FromArgb(128, 128, 128))
                g.FillRectangle(New SolidBrush(Color.FromArgb(230, 180, 20)), 0, 0, 80, 100) ' 少一點但鮮豔
            End Using
            Dim dom = PhotoFrames.DominantColor(two)
            Check("主色偏好鮮豔的顏色（40% 的黃勝過 60% 的灰）", dom.R > 200 AndAlso dom.B < 60, $"{dom}")
        End Using

        ' 下方加寬
        Dim m0 = Creative.FrameMargins(New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 50}, 240, 160)
        Dim m1 = Creative.FrameMargins(New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 50, .FrameBottom = 100}, 240, 160)
        Check("下方加寬 100%：下邊多一倍、其他三邊不變", m1.Bottom = m0.Bottom * 2 AndAlso m1.Top = m0.Top AndAlso m1.Left = m0.Left, $"{m0} → {m1}")

        ' 陰影、內框線、圓角
        Using plain = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 80}),
              shadow = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 80, .FrameShadow = 80})
            Dim m = Creative.FrameMargins(New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 80}, 240, 160)
            Dim p = New Point(m.Left + 120, m.Top + 160 + 3)
            Check("陰影：照片下方變暗", shadow.GetPixel(p.X, p.Y).R < plain.GetPixel(p.X, p.Y).R - 20, $"{plain.GetPixel(p.X, p.Y)} → {shadow.GetPixel(p.X, p.Y)}")
        End Using
        Using lined = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 80, .FrameInnerLine = 100})
            Dim m = Creative.FrameMargins(New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 80}, 240, 160)
            Dim darkest = Enumerable.Range(1, m.Top - 1).Min(Function(d) lined.GetPixel(m.Left + 120, m.Top - d).R)
            Check("內框線：照片上方的白邊裡有一條深色細線", darkest < 150, $"{darkest}")
        End Using
        Using round = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 50, .FrameCorner = 100})
            Dim m = Creative.FrameMargins(New EditRecipe With {.Frame = PhotoFrameStyle.White, .FrameSize = 50}, 240, 160)
            Check("照片圓角：照片的角落變成邊框色、中間不變", Near(round.GetPixel(m.Left + 1, m.Top + 1), Color.White, 20) AndAlso
                  Near(round.GetPixel(m.Left + 120, m.Top + 80), Color.FromArgb(40, 120, 200), 2))
        End Using

        ' 模糊延伸背景：比例
        For Each a In {(0, 1.0), (1, 0.8), (2, 9 / 16.0), (3, 16 / 9.0)}
            Using out = ImagePipeline.Render(src, New EditRecipe With {.Frame = PhotoFrameStyle.BlurExtend, .FrameAspect = a.Item1})
                Dim ratio = out.Width / CDbl(out.Height)
                If Math.Abs(ratio - a.Item2) > 0.01 Then bad.Add($"比例 {PhotoFrames.AspectNames(a.Item1)} 得到 {ratio:0.000}")
            End Using
        Next
        Check("模糊延伸背景：1:1、4:5、9:16、16:9 的整張比例都對", Not bad.Any(Function(b) b.StartsWith("比例")), String.Join("；", bad.Where(Function(b) b.StartsWith("比例"))))

        ' 相機資訊：EXIF 參數的寫法
        Dim info As New PhotoInfo With {.FocalLength = 50, .FNumber = 1.8, .ExposureTime = 1 / 250.0, .Iso = 100}
        Check("拍攝參數：50mm  f/1.8  1/250s  ISO 100", info.SettingsText() = "50mm  f/1.8  1/250s  ISO 100", info.SettingsText())
        Check("慢快門寫秒數、沒記錄的不寫", New PhotoInfo With {.ExposureTime = 2}.SettingsText() = "2s" AndAlso New PhotoInfo().SettingsText() = "")
        Check("EXIF rational：分子／分母，分母 0 時沒有", PhotoInfo.ParseRational({18, 0, 0, 0, 10, 0, 0, 0}).GetValueOrDefault() = 1.8 AndAlso Not PhotoInfo.ParseRational({1, 0, 0, 0, 0, 0, 0, 0}).HasValue)

        ' 存檔、貼上調整
        Dim full As New EditRecipe With {.Frame = PhotoFrameStyle.GoldFrame, .FrameColorArgb = Color.Teal.ToArgb(), .FrameAutoColor = True, .FrameBottom = 40,
                                         .FrameShadow = 30, .FrameInnerLine = 20, .FrameCorner = 10, .FrameText = "a" & vbLf & "b", .FrameAspect = 2}
        Dim back = RecipeStore.FromJson(RecipeStore.ToJson(full))
        Check("存檔讀回：邊框的顏色、加寬、陰影、細線、圓角、文字、比例都在", back.Equals(full) AndAlso back.FrameText = "a" & vbLf & "b" AndAlso back.FrameAspect = 2)
        Dim pasted As New EditRecipe()
        pasted.CopyAdjustmentsFrom(full)
        Check("貼上調整：邊框選項一起帶過去", pasted.FrameColorArgb.GetValueOrDefault() = full.FrameColorArgb.GetValueOrDefault() AndAlso pasted.FrameShadow = 30 AndAlso pasted.FrameText = full.FrameText)
        src.Dispose()
        DrawingRenderer.ClearCache()
    End Sub
End Module
