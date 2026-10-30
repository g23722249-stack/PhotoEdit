Imports PhotoEdit

''' <summary>人物合成：人物分析、背景分析、自動定位、位置換算、色彩匹配、做出圖層、存檔。用合成的圖，知道正確答案。</summary>
Module CompositeTests

    ''' <summary>全身「人物」的透明度：身體長條＋頭，腳在 92% 高度（圖內）。</summary>
    Private Function StickAlpha(w As Integer, h As Integer) As Byte()
        Dim a(w * h - 1) As Byte
        Dim cx = w / 2.0
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim body = Math.Abs(x + 0.5 - cx) <= w * 0.15 AndAlso y + 0.5 >= h * 0.25 AndAlso y + 0.5 <= h * 0.92
                Dim head = (x + 0.5 - cx) ^ 2 + (y + 0.5 - h * 0.15) ^ 2 <= (w * 0.15) ^ 2
                If body OrElse head Then a(y * w + x) = 255
            Next
        Next
        Return a
    End Function

    ''' <summary>半身像：身體一直延伸到最底列。</summary>
    Private Function HalfAlpha(w As Integer, h As Integer) As Byte()
        Dim a(w * h - 1) As Byte
        Dim cx = w / 2.0
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim body = Math.Abs(x + 0.5 - cx) <= w * 0.3 AndAlso y + 0.5 >= h * 0.35
                Dim head = (x + 0.5 - cx) ^ 2 + (y + 0.5 - h * 0.2) ^ 2 <= (w * 0.16) ^ 2
                If body OrElse head Then a(y * w + x) = 255
            Next
        Next
        Return a
    End Function

    ''' <summary>藍天＋灰色石柱（柱頂在 columnTop，左右 columnLeft..columnRight）＋底下灰色地面，BGRA。</summary>
    Private Function ColumnScene(w As Integer, h As Integer, cl As Integer, cr As Integer, ct As Integer) As Byte()
        Dim px(w * h * 4 - 1) As Byte
        Dim r As New Random(3)
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                Dim column = x >= cl AndAlso x < cr AndAlso y >= ct
                Dim ground = y >= h * 0.85
                If column OrElse ground Then
                    Dim v = 120 + r.Next(-12, 12)
                    px(i) = CByte(v) : px(i + 1) = CByte(v) : px(i + 2) = CByte(v)
                Else
                    ' 天空：往下稍微變亮
                    px(i) = CByte(230) : px(i + 1) = CByte(170 + y * 30 \ h) : px(i + 2) = CByte(90 + y * 40 \ h)
                End If
                px(i + 3) = 255
            Next
        Next
        Return px
    End Function

    Private Function Noisy(w As Integer, h As Integer, Optional seed As Integer = 7) As Byte()
        Dim r As New Random(seed)
        Dim px(w * h * 4 - 1) As Byte
        For i = 0 To w * h - 1
            px(i * 4) = CByte(r.Next(40, 200)) : px(i * 4 + 1) = CByte(r.Next(40, 200)) : px(i * 4 + 2) = CByte(r.Next(40, 200)) : px(i * 4 + 3) = 255
        Next
        Return px
    End Function

    Sub CompositeTestsRun()
        Console.WriteLine("— 人物合成 —")

        ' 人物分析
        Dim fullA = StickAlpha(100, 200)
        Dim fa = FigureAnalyzer.Analyze(fullA, 100, 200)
        Check("人物分析：腳點、腳寬、頭、外框", Math.Abs(fa.FeetX - 50) <= 2 AndAlso Math.Abs(fa.FeetY - 184) <= 2 AndAlso fa.FeetWidth >= 25 AndAlso fa.FeetWidth <= 32 AndAlso fa.HeadY < 40 AndAlso Not fa.BottomCropped,
              $"腳 ({fa.FeetX:0}, {fa.FeetY:0}) 寬 {fa.FeetWidth:0}，頭 y {fa.HeadY:0}")
        Dim ha = FigureAnalyzer.Analyze(HalfAlpha(200, 240), 200, 240)
        Check("人物分析：下緣被裁切的是半身像，錨點在裁切邊中點", ha.BottomCropped AndAlso Math.Abs(ha.Anchor.X - 100) <= 2 AndAlso ha.Anchor.Y = 240, $"{ha.Anchor}")
        Dim touching = DirectCast(fullA.Clone(), Byte())
        For y = 184 To 199
            For x = 47 To 52
                touching(y * 100 + x) = 255
            Next
        Next
        Check("人物分析：腳剛好碰到底邊的全身像不算半身像", Not FigureAnalyzer.Analyze(touching, 100, 200).BottomCropped)
        Dim mir = fa.Mirrored(100)
        Check("人物分析：左右翻轉後腳點、外框跟著鏡射", Math.Abs(mir.FeetX - (100 - fa.FeetX)) < 0.01 AndAlso mir.Bounds.X = 100 - fa.Bounds.Right)
        Dim threw = False
        Try
            FigureAnalyzer.Analyze(New Byte(99) {}, 10, 10)
        Catch ex As InvalidOperationException
            threw = True
        End Try
        Check("人物分析：全透明時告知無法分析", threw)

        ' 背景分析
        Dim colPx = ColumnScene(600, 800, 260, 340, 350)
        Dim sc = SceneAnalyzer.Analyze(colPx, 600, 800)
        Dim best = sc.Suggestions(0)
        Check("背景分析：找到柱頂（頂部對著天空）", sc.SkyFraction > 0.4 AndAlso best.FromSkyline AndAlso Math.Abs(best.AnchorX - 300) <= 15 AndAlso Math.Abs(best.AnchorY - 350) <= 15 AndAlso
              best.SurfaceLeft >= 245 AndAlso best.SurfaceRight <= 355, $"天空 {sc.SkyFraction:P0}，({best.AnchorX:0}, {best.AnchorY:0}) {best.SurfaceLeft:0}～{best.SurfaceRight:0}")
        Check("背景分析：天空遮罩（柱頂上方是天空、柱身不是）", sc.SkyAt(300, 200) > 0.9F AndAlso sc.SkyAt(300, 500) < 0.1F)
        Dim noSky = SceneAnalyzer.Analyze(Noisy(300, 200), 300, 200)
        Check("背景分析：沒有天空時放在畫面下方中央的地面", noSky.Suggestions.Count = 1 AndAlso Not noSky.Suggestions(0).FromSkyline AndAlso Math.Abs(noSky.Suggestions(0).AnchorY - 180) <= 2)

        ' 自動定位
        Dim figA = StickAlpha(200, 400)
        Dim figAn = FigureAnalyzer.Analyze(figA, 200, 400)
        Dim cands = CompositePlacement.Suggest(sc, figAn, figA, 200, 400)
        Dim c0 = cands(0)
        Dim feetW = figAn.FeetWidth * c0.Scale
        Check("自動定位：站上柱頂、腳寬不超出柱頂、頭不超出畫面", Math.Abs(c0.TargetX - 300) <= 20 AndAlso Math.Abs(c0.TargetY - 350) <= 15 AndAlso feetW <= 80 AndAlso
              c0.TargetY - figAn.Height * c0.Scale >= 0 AndAlso c0.Score > 0.5, $"({c0.TargetX:0}, {c0.TargetY:0}) 腳寬 {feetW:0}，{c0.PenaltySummary()}")
        Dim halfA = HalfAlpha(200, 240)
        Dim nsc = SceneAnalyzer.Analyze(Noisy(600, 400), 600, 400)
        Dim hc = CompositePlacement.Suggest(nsc, FigureAnalyzer.Analyze(halfA, 200, 240), halfA, 200, 240)(0)
        Dim hh = FigureAnalyzer.Analyze(halfA, 200, 240).Height * hc.Scale
        Check("自動定位：半身像貼齊畫面下緣、置中、高度約 60%", Math.Abs(hc.TargetY - 400) < 0.5 AndAlso Math.Abs(hc.TargetX - 300) < 0.5 AndAlso Math.Abs(hh - 240) <= 3,
              $"({hc.TargetX:0}, {hc.TargetY:0}) 高 {hh:0}")

        ' 位置換算（圖片物件的中心與高度）
        Dim crop As New Rectangle(20, 10, 160, 380)
        Dim ov = CompositePlacement.ToOverlay(crop, 100, 390, 300, 350, 0.5, 600, 800)
        Dim back = CompositePlacement.FromOverlay(crop, 100, 390, ov.X, ov.Y, ov.Size, 600, 800)
        Check("位置換算：物件中心、高度；反算回錨點位置與縮放", Math.Abs(ov.X - 0.5) < 0.0001 AndAlso Math.Abs(ov.Y - (350 + (200 - 390) * 0.5) / 800) < 0.0001 AndAlso Math.Abs(ov.Size - 190 / 800.0) < 0.0001 AndAlso
              Math.Abs(back.TargetX - 300) < 0.001 AndAlso Math.Abs(back.TargetY - 350) < 0.001 AndAlso Math.Abs(back.Scale - 0.5) < 0.0001, $"{ov}")

        ' 色彩
        Dim fig = Noisy(60, 80, 11)
        Dim alpha = Enumerable.Repeat(CByte(255), 60 * 80).ToArray()
        Dim same = DirectCast(fig.Clone(), Byte())
        CompositeColorMatch.Apply(same, 60, 80, New CompositeColor())
        Check("色彩：保持原色時像素完全不變", same.SequenceEqual(fig))
        Dim dark(400 * 300 * 4 - 1) As Byte
        For i = 0 To 400 * 300 - 1
            dark(i * 4) = 40 : dark(i * 4 + 1) = 35 : dark(i * 4 + 2) = 30 : dark(i * 4 + 3) = 255
        Next
        Dim rec = CompositeColorMatch.Recommend(dark, 400, 300, New Rectangle(100, 100, 200, 150), fig, 60, 80, alpha)
        Check("色彩：暗的背景 → 人物調暗，但都在上限內", rec.Mode = CompositeColorMode.Environment AndAlso rec.Brightness < 0 AndAlso rec.Brightness >= -0.12 AndAlso
              rec.Contrast >= 0.85 AndAlso rec.Contrast <= 1.15 AndAlso rec.Saturation >= 0.8 AndAlso rec.Saturation <= 1.1 AndAlso Math.Abs(rec.Temperature) <= 0.4,
              $"亮度 {rec.Brightness:0.000} 對比 {rec.Contrast:0.00} 飽和 {rec.Saturation:0.00} 色溫 {rec.Temperature:0.00}")
        Dim applied = DirectCast(fig.Clone(), Byte())
        CompositeColorMatch.Apply(applied, 60, 80, rec)
        Dim lumBefore = Enumerable.Range(0, 60 * 80).Average(Function(i) CInt(fig(i * 4 + 1)))
        Dim lumAfter = Enumerable.Range(0, 60 * 80).Average(Function(i) CInt(applied(i * 4 + 1)))
        Dim half = rec.Clone() : half.Strength = 0
        Dim zero = DirectCast(fig.Clone(), Byte())
        CompositeColorMatch.Apply(zero, 60, 80, half)
        Check("色彩：套用後變暗；強度 0 等於不變", lumAfter < lumBefore AndAlso zero.SequenceEqual(fig), $"{lumBefore:0} → {lumAfter:0}")

        ' 做出圖層：裁到前景、透明度＝遮罩、翻轉
        Dim w = 100, h = 200
        Dim px(w * h * 4 - 1) As Byte
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                px(i) = CByte(x) : px(i + 1) = CByte(y) : px(i + 2) = 50 : px(i + 3) = 255
            Next
        Next
        Dim cf As New CompositeFigure With {.Decontaminate = False}
        Dim baked = CompositeBaker.Bake(px, w, h, fullA, cf)
        Dim fb = FigureAnalyzer.Analyze(fullA, w, h).Bounds
        Using baked.Image
            Dim inside = baked.Image.GetPixel(baked.Image.Width \ 2, baked.Image.Height \ 2)
            Check("做出圖層：裁到前景外框（外加 2 像素）、人物內部不透明、角落透明",
                  baked.Crop = Rectangle.Inflate(fb, 2, 2) AndAlso inside.A = 255 AndAlso baked.Image.GetPixel(0, baked.Image.Height - 1).A = 0, $"{baked.Crop}，外框 {fb}")
        End Using
        cf.FlipHorizontal = True
        Dim flipped = CompositeBaker.Bake(px, w, h, fullA, cf)
        Using flipped.Image
            ' 原圖藍色通道 = x：翻轉後最左邊那一欄是原本最右邊
            Dim leftCol = flipped.Image.GetPixel(2, flipped.Image.Height \ 2)
            Dim r0 = baked.Crop
            Check("做出圖層：左右翻轉（像素與裁切範圍都鏡射）", flipped.Crop.X = w - r0.Right AndAlso leftCol.B = r0.Right - 1 - 2, $"最左欄 B={leftCol.B}，預期 {r0.Right - 3}")
        End Using

        ' 圖層與存檔
        Dim o As New Overlay With {.Kind = OverlayKind.Image, .ImagePath = "C:\x\layer.png",
                                   .Composite = New CompositeFigure With {.Name = "科幻美女", .SourcePath = "C:\x\fig.png", .MaskPath = "C:\x\mask.png", .AiMaskPath = "C:\x\ai.png"}}
        Dim copy = o.Clone()
        copy.Composite.Color.Brightness = 0.3
        Check("圖層：複製時合成資料也是新的一份", o.Composite.Color.Brightness = 0 AndAlso copy.Composite IsNot o.Composite)
        Dim recipe As New EditRecipe With {.Overlays = New List(Of Overlay) From {o}}
        Dim rt = RecipeStore.FromJson(RecipeStore.ToJson(recipe))
        Dim ro = rt.Overlays(0)
        Check("存檔讀回：合成資料（名稱、原圖、遮罩、色彩）", ro.Composite IsNot Nothing AndAlso ro.Composite.Name = "科幻美女" AndAlso ro.Composite.MaskPath = "C:\x\mask.png" AndAlso
              ro.Composite.Color.Mode = CompositeColorMode.Original)
        Dim refs = ProjectFile.ReferencedFiles(recipe)
        Check("專案檔：人物原圖、遮罩、AI 遮罩、圖層 PNG 都會打包", {"C:\x\layer.png", "C:\x\fig.png", "C:\x\mask.png", "C:\x\ai.png"}.All(Function(p) refs.Contains(p)), String.Join("、", refs))
        Dim names = LayerStack.Order(recipe).Select(Function(l) l.DisplayName).ToList()
        Check("圖層清單：合成圖層顯示「合成：人物名稱」", names.Contains("合成：科幻美女"), String.Join("、", names))

        ' ---- 階段 B：方向與透視、遮擋、陰影 ----
        Dim gm As New CompositeGeometry With {.AnchorX = 50, .AnchorY = 184, .TX = 300, .TY = 350, .Scale = 0.5, .Rotation = 12, .Keystone = 0.8, .FigureHeight = 160}
        Dim pts = {New PointF(10, 30), New PointF(90, 60), New PointF(50, 184), New PointF(70, 120)}
        Dim maxErr = pts.Max(Function(p)
                                 Dim b = gm.Forward(p.X, p.Y)
                                 Dim q = gm.Inverse(b.X, b.Y)
                                 Return Math.Max(Math.Abs(q.X - p.X), Math.Abs(q.Y - p.Y))
                             End Function)
        Dim anchorBg = gm.Forward(50, 184)
        Check("幾何：人物 → 背景 → 人物 回到原點；錨點落在目標", maxErr < 0.01 AndAlso Math.Abs(anchorBg.X - 300) < 0.01 AndAlso Math.Abs(anchorBg.Y - 350) < 0.01, $"最大誤差 {maxErr:0.0000}")
        Dim flatG As New CompositeGeometry With {.AnchorX = 50, .AnchorY = 184, .TX = 300, .TY = 350, .Scale = 1, .Keystone = 0.7, .FigureHeight = 160}
        Dim bottomW = flatG.Forward(90, 184).X - flatG.Forward(10, 184).X
        Dim topW = flatG.Forward(90, 24).X - flatG.Forward(10, 24).X
        Check("幾何：仰角透視 70% → 頂部寬度是錨點處的 70%", Math.Abs(topW / bottomW - 0.7) < 0.01, $"{topW:0.0} / {bottomW:0.0}")
        Dim conv = CompositeGeometry.FromGuides(New PointF(200, 400), New PointF(220, 100), New PointF(400, 400), New PointF(380, 100), 400, 300)
        Dim par = CompositeGeometry.FromGuides(New PointF(200, 400), New PointF(200, 100), New PointF(400, 400), New PointF(400, 100), 400, 300)
        Dim tilt = CompositeGeometry.FromGuides(New PointF(200, 400), New PointF(250, 100), New PointF(400, 400), New PointF(450, 100), 400, 300)
        Check("參考線：往上收斂 → 上窄；平行 → 不變；一起往右斜 → 旋轉", conv.Keystone < 0.95 AndAlso Math.Abs(conv.Rotation) < 0.5 AndAlso Math.Abs(par.Keystone - 1) < 0.001 AndAlso
              tilt.Rotation > 5 AndAlso Math.Abs(tilt.Keystone - 1) < 0.001, $"收斂 {conv.Keystone:0.00}，平行 {par.Keystone:0.00}，斜 {tilt.Rotation:0.0}°")

        ' 做出變形圖層：遮擋的地方透明、旁邊不受影響
        Dim prep(100 * 200 * 4 - 1) As Byte
        For i = 0 To 100 * 200 - 1
            prep(i * 4) = 60 : prep(i * 4 + 1) = 120 : prep(i * 4 + 2) = 200 : prep(i * 4 + 3) = fullA(i)
        Next
        Dim lg As New CompositeGeometry With {.AnchorX = fa.FeetX, .AnchorY = fa.FeetY, .TX = 300, .TY = 350, .Scale = 1, .Keystone = 1, .FigureHeight = fa.Height}
        Dim occ(600 * 400 - 1) As Byte
        For y = 270 To 289
            For x = 0 To 599
                occ(y * 600 + x) = 255
            Next
        Next
        Dim lay = CompositeBaker.BakeLayer(prep, 100, 200, fa.Bounds, lg, occ, 600, 400, 1)
        Using lay.Image
            Dim at = Function(bx As Integer, by As Integer) lay.Image.GetPixel(CInt(bx - lay.Rect.X), CInt(by - lay.Rect.Y))
            Dim hidden = at(300, 280), shown = at(300, 240), colorOk = at(300, 240)
            Check("變形圖層：遮擋帶裡透明、上面的身體不透明而且顏色不變", hidden.A < 10 AndAlso shown.A > 245 AndAlso Math.Abs(colorOk.R - 200) <= 2 AndAlso Math.Abs(colorOk.B - 60) <= 2,
                  $"遮擋 α={hidden.A}，身體 α={shown.A} RGB({colorOk.R},{colorOk.G},{colorOk.B})")
        End Using

        ' 陰影：落在腳邊、不落在天空；半身像不推薦陰影
        Dim colG As New CompositeGeometry With {.AnchorX = figAn.FeetX, .AnchorY = figAn.FeetY, .TX = c0.TargetX, .TY = c0.TargetY, .Scale = c0.Scale, .Keystone = 1, .FigureHeight = figAn.Height}
        Dim prep2(200 * 400 * 4 - 1) As Byte
        For i = 0 To 200 * 400 - 1
            prep2(i * 4 + 3) = figA(i)
        Next
        Dim sh As New CompositeShadow With {.Direction = 0, .Length = 0.8, .Opacity = 60, .AvoidSky = True}
        Dim shOut = CompositeBaker.BakeShadow(prep2, 200, 400, figAn, colG, sh, sc, Nothing, 600, 800, 1)
        Using shOut.Image
            Dim sa = Function(bx As Double, by As Double) shOut.Image.GetPixel(Math.Clamp(CInt(bx - shOut.Rect.X), 0, shOut.Image.Width - 1), Math.Clamp(CInt(by - shOut.Rect.Y), 0, shOut.Image.Height - 1)).A
            Dim underFeet = sa(c0.TargetX, c0.TargetY + 1)
            ' 影子往右投：柱頂右邊外面是天空（柱子只到 340），那裡不能有影子
            Dim overSky = sa(380, c0.TargetY + 1)
            Check("陰影：腳下有接觸陰影；投到柱子外面的天空不畫", underFeet > 60 AndAlso overSky < 5, $"腳下 α={underFeet}，天空 α={overSky}")
        End Using
        sh.AvoidSky = False
        Dim shSky = CompositeBaker.BakeShadow(prep2, 200, 400, figAn, colG, sh, sc, Nothing, 600, 800, 1)
        Using shSky.Image
            Dim a2 = shSky.Image.GetPixel(Math.Clamp(CInt(380 - shSky.Rect.X), 0, shSky.Image.Width - 1), Math.Clamp(CInt(c0.TargetY + 1 - shSky.Rect.Y), 0, shSky.Image.Height - 1)).A
            Check("陰影：關掉「不落在天空」時天空上也有影子", a2 > 5, $"α={a2}")
        End Using
        Dim halfFa = FigureAnalyzer.Analyze(halfA, 200, 240)
        Check("陰影：半身像不推薦陰影、也做不出陰影圖層", Not CompositeShadow.Recommend(nsc, halfFa).Enabled AndAlso
              CompositeBaker.BakeShadow(New Byte(200 * 240 * 4 - 1) {}, 200, 240, halfFa, colG, New CompositeShadow(), nsc, Nothing, 600, 400, 1).Image Is Nothing)

        ' 魔術棒
        Dim wandPx(50 * 20 * 4 - 1) As Byte
        For y = 0 To 19
            For x = 0 To 49
                Dim i = (y * 50 + x) * 4
                Dim v = If(x < 25, 40, 220)
                wandPx(i) = CByte(v) : wandPx(i + 1) = CByte(v) : wandPx(i + 2) = CByte(v) : wandPx(i + 3) = 255
            Next
        Next
        Dim wand = CompositeBaker.FloodSelect(wandPx, 50, 20, 5, 5, 20)
        Check("魔術棒：只選到相連、顏色相近的那一半", wand(5 * 50 + 10) = 255 AndAlso wand(5 * 50 + 40) = 0 AndAlso wand.Count(Function(v) v = 255) = 25 * 20)

        ' 新欄位存檔讀回
        o.Composite.Rotation = 7.5 : o.Composite.Keystone = 0.85 : o.Composite.OcclusionMaskPath = "C:\x\occ.png"
        o.Composite.Shadow.Direction = 140 : o.Composite.Shadow.Opacity = 33
        Dim shadowO As New Overlay With {.Kind = OverlayKind.Image, .ImagePath = "C:\x\shadow.png", .CompositeShadowName = "科幻美女"}
        Dim recipe2 As New EditRecipe With {.Overlays = New List(Of Overlay) From {shadowO, o}}
        Dim rt2 = RecipeStore.FromJson(RecipeStore.ToJson(recipe2))
        Dim c2 = rt2.Overlays(1).Composite
        Check("存檔讀回：旋轉、透視、遮擋遮罩、陰影參數、陰影圖層", c2.Rotation = 7.5 AndAlso c2.Keystone = 0.85 AndAlso c2.OcclusionMaskPath = "C:\x\occ.png" AndAlso
              c2.Shadow.Direction = 140 AndAlso c2.Shadow.Opacity = 33 AndAlso rt2.Overlays(0).CompositeShadowName = "科幻美女")
        Dim refs2 = ProjectFile.ReferencedFiles(recipe2)
        Dim names2 = LayerStack.Order(recipe2).Select(Function(l) l.DisplayName).ToList()
        Check("專案檔打包遮擋遮罩與陰影圖層；圖層清單顯示「合成陰影：」", refs2.Contains("C:\x\occ.png") AndAlso refs2.Contains("C:\x\shadow.png") AndAlso names2.Contains("合成陰影：科幻美女"),
              String.Join("、", names2))
        Dim cl = o.Clone()
        cl.Composite.Shadow.Opacity = 90
        Check("複製圖層：陰影參數也是新的一份", o.Composite.Shadow.Opacity = 33)

        ' ---- 階段 C：再編輯、地平線、邊緣透光、顆粒 ----
        Dim fl = CompositePlacement.FollowLayer(0.5, 0.8, 0.5, 0.6, 0.4, 0.6, 0.6, 0.48)
        Check("再編輯：主畫面把圖層往右移 0.1、放大 1.2 倍 → 錨點跟著移、倍率 1.2", Math.Abs(fl.TargetX - 0.6) < 0.0001 AndAlso Math.Abs(fl.TargetY - (0.6 + 0.2 * 1.2)) < 0.0001 AndAlso Math.Abs(fl.Factor - 1.2) < 0.0001,
              $"({fl.TargetX:0.000}, {fl.TargetY:0.000}) ×{fl.Factor:0.00}")
        Dim same0 = CompositePlacement.FollowLayer(0.3, 0.7, 0.4, 0.5, 0.3, 0.4, 0.5, 0.3)
        Check("再編輯：圖層沒動過 → 位置與大小不變", Math.Abs(same0.TargetX - 0.3) < 0.0001 AndAlso Math.Abs(same0.TargetY - 0.7) < 0.0001 AndAlso Math.Abs(same0.Factor - 1) < 0.0001)
        Dim hNear = CompositePlacement.HeightFromHorizon(500, 300), hFar = CompositePlacement.HeightFromHorizon(350, 300)
        Check("地平線：腳離地平線越遠（越近鏡頭）人越高；眼睛落在地平線；腳在地平線上方不適用",
              hNear > hFar AndAlso Math.Abs(500 - hNear * CompositePlacement.EyeFraction - 300) < 0.001 AndAlso CompositePlacement.HeightFromHorizon(290, 300) = 0, $"近 {hNear:0}，遠 {hFar:0}")

        ' 雜訊估計：平的圖 ≈ 0、加了標準差 8 的雜訊 ≈ 8
        Dim flatPx(200 * 200 * 4 - 1) As Byte
        Dim noisyPx(200 * 200 * 4 - 1) As Byte
        Dim rg As New Random(5)
        For i = 0 To 200 * 200 - 1
            Dim u1 = 1.0 - rg.NextDouble(), u2 = rg.NextDouble()
            Dim nz = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2) * 8
            For c = 0 To 2
                flatPx(i * 4 + c) = 128
                noisyPx(i * 4 + c) = CByte(Math.Clamp(CInt(Math.Round(128 + nz)), 0, 255))
            Next
            flatPx(i * 4 + 3) = 255 : noisyPx(i * 4 + 3) = 255
        Next
        Dim nFlat = CompositeNoise.Estimate(flatPx, 200, 200, New Rectangle(0, 0, 200, 200), Nothing)
        Dim nNoisy = CompositeNoise.Estimate(noisyPx, 200, 200, New Rectangle(0, 0, 200, 200), Nothing)
        Check("雜訊估計：平的圖約 0、標準差 8 的雜訊約 8", nFlat < 0.5 AndAlso nNoisy > 6 AndAlso nNoisy < 10, $"平 {nFlat:0.0}，雜訊 {nNoisy:0.0}")

        ' 邊緣透光：邊緣往背景的顏色靠，人物內部不變；顆粒：人物內部出現變化
        Dim bgRed(600 * 400 * 4 - 1) As Byte
        For i = 0 To 600 * 400 - 1
            bgRed(i * 4 + 2) = 255 : bgRed(i * 4 + 3) = 255 ' 紅色背景
        Next
        Dim plain = CompositeBaker.BakeLayer(prep, 100, 200, fa.Bounds, lg, Nothing, 600, 400, 1)
        Dim wrapped = CompositeBaker.BakeLayer(prep, 100, 200, fa.Bounds, lg, Nothing, 600, 400, 1, bgRed, 100, 0)
        Dim grainy = CompositeBaker.BakeLayer(prep, 100, 200, fa.Bounds, lg, Nothing, 600, 400, 1, Nothing, 0, 6)
        Using pImg = plain.Image, wImg = wrapped.Image, gImg = grainy.Image
            ' 身體長條的右邊緣（x 寬 30%：35..65）往內 1 像素，與身體正中間
            Dim at = Function(b As Bitmap, rect As RectangleF, bx As Integer, by As Integer) b.GetPixel(CInt(bx - rect.X), CInt(by - rect.Y))
            Dim edgeX = CInt(300 + 14), midX = 300, rowY = 250
            Dim pe = at(plain.Image, plain.Rect, edgeX, rowY), we = at(wrapped.Image, wrapped.Rect, edgeX, rowY)
            Dim pm = at(plain.Image, plain.Rect, midX, rowY), wm = at(wrapped.Image, wrapped.Rect, midX, rowY)
            Check("邊緣透光：邊緣混進背景的紅色，人物中間不變", we.R > pe.R + 10 AndAlso Math.Abs(CInt(wm.R) - pm.R) <= 1, $"邊緣 R {pe.R}→{we.R}，中間 R {pm.R}→{wm.R}")
            Dim vals As New List(Of Integer)()
            For yy = 230 To 270
                vals.Add(at(grainy.Image, grainy.Rect, midX, yy).G)
            Next
            Dim mean = vals.Average()
            Dim sd = Math.Sqrt(vals.Average(Function(v) (v - mean) ^ 2))
            Check("顆粒：人物上出現雜訊（標準差約 6）", sd > 3 AndAlso sd < 10, $"標準差 {sd:0.0}")
        End Using

        ' 新欄位存檔讀回
        o.Composite.LayerX = 0.42 : o.Composite.LayerSize = 0.5 : o.Composite.LightWrap = 40 : o.Composite.Grain = 25 : o.Composite.Horizon = 0.45
        Dim rt3 = RecipeStore.FromJson(RecipeStore.ToJson(New EditRecipe With {.Overlays = New List(Of Overlay) From {o}})).Overlays(0).Composite
        Check("存檔讀回：套用時的圖層位置、邊緣透光、顆粒、地平線", rt3.LayerX = 0.42 AndAlso rt3.LayerSize = 0.5 AndAlso rt3.LightWrap = 40 AndAlso rt3.Grain = 25 AndAlso rt3.Horizon = 0.45)
        Check("舊的合成資料沒有地平線：讀回是 -1（不用地平線）", RecipeStore.FromJson("{""Overlays"":[{""Kind"":2,""Composite"":{""Name"":""x""}}]}").Overlays(0).Composite.Horizon = -1)
    End Sub
End Module
