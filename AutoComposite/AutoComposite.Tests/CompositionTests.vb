Imports Xunit

Public Class CompositionTests
    Private Shared Function SceneWith(bg As RgbaImage, layer As CompositeLayer) As CompositeScene
        Dim s As New CompositeScene With {.Background = bg}
        s.Layers.Add(layer)
        Return s
    End Function

    <Fact>
    Public Sub AlphaCompositing_HalfTransparentRedOverBlue_IsExact()
        Dim canvas = RgbaImage.Filled(1, 1, 0, 0, 255)
        Dim tile As New PremultipliedTile(New PixelRect(0, 0, 1, 1))
        ' 預乘：紅色 50%。
        tile.Data(0) = 0.5F : tile.Data(1) = 0 : tile.Data(2) = 0 : tile.Data(3) = 0.5F
        CompositeRenderer.CompositeTile(canvas, tile, Nothing, 1)
        Dim p = canvas.GetPixel(0, 0)
        Assert.Equal(128, CInt(p.R)) ' 0.5·255 = 127.5 → 128
        Assert.Equal(0, CInt(p.G))
        Assert.Equal(128, CInt(p.B))
        Assert.Equal(255, CInt(p.A))
    End Sub

    <Fact>
    Public Sub AlphaCompositing_OverTransparentBackground_KeepsStraightColor()
        Dim canvas = RgbaImage.Filled(1, 1, 0, 0, 0, 0)
        Dim tile As New PremultipliedTile(New PixelRect(0, 0, 1, 1))
        tile.Data(0) = 0.25F : tile.Data(1) = 0.25F : tile.Data(2) = 0 : tile.Data(3) = 0.25F
        CompositeRenderer.CompositeTile(canvas, tile, Nothing, 1)
        Dim p = canvas.GetPixel(0, 0)
        ' 未預乘的顏色應為 (255,255,0)，Alpha 25%：不會因預乘而變暗（黑邊）。
        Assert.Equal(255, CInt(p.R)) : Assert.Equal(255, CInt(p.G)) : Assert.Equal(64, CInt(p.A))
    End Sub

    <Fact>
    Public Sub Background_OutsideLayer_IsUnchanged_WhenShadowsAndInpaintOff()
        Dim bg = TestHelpers.NoisyBackground(320, 240)
        Dim fig = TestHelpers.StickFigure(60, 120)
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        layer.Transform = New LayerTransform With {.X = 150, .Y = 200, .Scale = 1.3, .Rotation = 12}
        layer.Color = New ColorParams With {.Mode = ColorMode.Custom, .Brightness = 0.1, .Temperature = 0.3}
        Dim scene = SceneWith(bg, layer)
        Dim r = CompositeRenderer.CreateDefault().Render(scene, New RenderOptions With {.MaxSide = 0, .IncludeShadows = False})
        Assert.Equal(bg.Width, r.Image.Width)
        ' 逐像素：人物變換後 Alpha 為 0 的地方，與背景原值完全相同。
        Dim sampler As New LayerSampler(fig, layer.Alpha)
        Dim tile = New TransformService().RenderLayer(sampler, New TransformService().BuildMatrix(layer), bg.Width, bg.Height, ResampleQuality.Bicubic)
        Dim changedOutside = 0, changedInside = 0
        For y = 0 To bg.Height - 1
            For x = 0 To bg.Width - 1
                Dim covered = False
                If x >= tile.Bounds.X AndAlso y >= tile.Bounds.Y AndAlso x < tile.Bounds.Right AndAlso y < tile.Bounds.Bottom Then
                    covered = tile.Data(((y - tile.Bounds.Y) * tile.Bounds.Width + (x - tile.Bounds.X)) * 4 + 3) > 0
                End If
                Dim i = (y * bg.Width + x) * 4
                Dim same = bg.Pixels(i) = r.Image.Pixels(i) AndAlso bg.Pixels(i + 1) = r.Image.Pixels(i + 1) AndAlso bg.Pixels(i + 2) = r.Image.Pixels(i + 2) AndAlso bg.Pixels(i + 3) = r.Image.Pixels(i + 3)
                If Not covered AndAlso Not same Then changedOutside += 1
                If covered AndAlso Not same Then changedInside += 1
            Next
        Next
        Assert.Equal(0, changedOutside)
        Assert.True(changedInside > 1000)
        ' 背景物件本身也沒有被改寫。
        Assert.NotSame(bg, r.Image)
    End Sub

    <Fact>
    Public Sub Occlusion_HidesFigureWhereForegroundMaskIsSet()
        Dim bg = RgbaImage.Filled(100, 100, 0, 0, 255)
        Dim fig = RgbaImage.Filled(40, 40, 255, 0, 0)
        Dim layer = TestHelpers.NewLayer(fig, FloatMask.Filled(40, 40, 1))
        layer.AnchorX = 20 : layer.AnchorY = 40
        layer.Transform = New LayerTransform With {.X = 50, .Y = 70, .Scale = 1}
        Dim scene = SceneWith(bg, layer)
        scene.OcclusionFeather = 0
        scene.ForegroundMask = New FloatMask(100, 100)
        For y = 0 To 99
            For x = 50 To 99
                scene.ForegroundMask(x, y) = 1
            Next
        Next
        Dim r = CompositeRenderer.CreateDefault().Render(scene, New RenderOptions With {.IncludeShadows = False})
        Assert.Equal(255, CInt(r.Image.GetPixel(40, 50).R)) ' 左半：人物
        Dim right = r.Image.GetPixel(60, 50)                 ' 右半：前景物件擋住，保留背景
        Assert.Equal(0, CInt(right.R)) : Assert.Equal(255, CInt(right.B))
    End Sub

    <Fact>
    Public Sub BrushPainter_SoftEdgeAndRemove()
        Dim m As New FloatMask(50, 50)
        BrushPainter.Paint(m, New BrushStroke With {.Mode = BrushMode.Add, .Radius = 10, .Softness = 0.5, .Points = {New PointD(25, 25)}.ToList()})
        Assert.Equal(1.0F, m(25, 25))
        Assert.InRange(m(25, 33), 0.01F, 0.99F) ' 柔邊
        Assert.Equal(0.0F, m(25, 40))
        BrushPainter.Paint(m, New BrushStroke With {.Mode = BrushMode.Remove, .Radius = 4, .Softness = 0, .Points = {New PointD(25, 25)}.ToList()})
        Assert.Equal(0.0F, m(25, 25))
    End Sub

    <Fact>
    Public Sub ColorAdjust_IdentityAndDirections()
        Dim img = TestHelpers.NoisyBackground(16, 16)
        Dim svc As New ColorMatchingService()
        Assert.Same(img, svc.Apply(img, New ColorParams()))
        Assert.Same(img, svc.Apply(img, New ColorParams With {.Mode = ColorMode.Original, .Brightness = 0.3}))
        Dim brighter = svc.Apply(img, New ColorParams With {.Mode = ColorMode.Custom, .Brightness = 0.1})
        Assert.True(brighter.Pixels.Where(Function(b, i) i Mod 4 <> 3).Select(Function(b) CInt(b)).Average() > img.Pixels.Where(Function(b, i) i Mod 4 <> 3).Select(Function(b) CInt(b)).Average())
        Dim warm = svc.Apply(img, New ColorParams With {.Mode = ColorMode.Custom, .Temperature = 0.5})
        Assert.True(warm.GetPixel(3, 3).R >= img.GetPixel(3, 3).R AndAlso warm.GetPixel(3, 3).B <= img.GetPixel(3, 3).B)
        Dim gray = svc.Apply(img, New ColorParams With {.Mode = ColorMode.Custom, .Saturation = 0})
        Dim g = gray.GetPixel(5, 5)
        Assert.InRange(CInt(g.R) - CInt(g.G), -1, 1)
        Dim none = svc.Apply(img, New ColorParams With {.Mode = ColorMode.Custom, .Brightness = 0.3, .Strength = 0})
        TestHelpers.SameSeq(img.Pixels, none.Pixels)
    End Sub

    <Fact>
    Public Sub ColorRecommend_IsLocalAndBounded()
        ' 背景左半暗、右半亮；人物放在亮的那一側，推薦應變亮。
        Dim bg As New RgbaImage(200, 100)
        For y = 0 To 99
            For x = 0 To 199
                Dim v = If(x < 100, CByte(30), CByte(230))
                bg.SetPixel(x, y, v, v, v, 255)
            Next
        Next
        Dim fig = RgbaImage.Filled(20, 20, 120, 120, 120)
        Dim alpha = FloatMask.Filled(20, 20, 1)
        Dim svc As New ColorMatchingService()
        Dim right = svc.Recommend(bg, New PixelRect(120, 10, 60, 80), fig, alpha)
        Dim left = svc.Recommend(bg, New PixelRect(10, 10, 60, 80), fig, alpha)
        Assert.True(right.Brightness > 0)
        Assert.True(left.Brightness < 0)
        Assert.InRange(right.Brightness, -0.12, 0.12)
        Assert.Equal(ColorMode.EnvironmentMatch, right.Mode)
    End Sub

    <Fact>
    Public Sub Shadow_DarkensNearFeet_NotAboveHead_AndRespectsReceiver()
        Dim bg = RgbaImage.Filled(300, 300, 200, 200, 200)
        Dim fig = TestHelpers.StickFigure(60, 120)
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        layer.Transform = New LayerTransform With {.X = 150, .Y = 250, .Scale = 1}
        layer.Shadow = New ShadowParams With {.Enabled = True, .Direction = 30, .Length = 0.6, .Opacity = 0.5, .ContactStrength = 0.6, .BlurRadius = 2}
        Dim scene = SceneWith(bg, layer)
        Dim renderer = CompositeRenderer.CreateDefault()
        Dim r = renderer.Render(scene, New RenderOptions With {.Mode = PreviewMode.Shadow})
        ' 腳邊（接觸陰影）變暗。
        Assert.True(r.Image.GetPixel(150, 251).R < 190)
        ' 投射方向（30°，地面壓扁 0.35）：高度 h 的點落在 (150 + 0.52h, 250 + 0.105h)，h ≈ 57 → (179.6, 256)。
        Assert.True(r.Image.GetPixel(179, 256).R < 195, $"投射陰影 {r.Image.GetPixel(179, 256)}")
        ' 反方向（左邊）沒有投射陰影。
        Assert.Equal(200, CInt(r.Image.GetPixel(100, 256).R))
        ' 頭頂上方遠處沒有陰影。
        Assert.Equal(200, CInt(r.Image.GetPixel(150, 50).R))
        ' 接收遮罩全 0 → 沒有任何陰影。
        scene.ShadowReceiverMask = New FloatMask(300, 300)
        layer.Shadow.Receiver = ShadowReceiver.CustomMask
        Dim r2 = renderer.Render(scene, New RenderOptions With {.Mode = PreviewMode.Shadow})
        TestHelpers.SameSeq(bg.Pixels, r2.Image.Pixels)
    End Sub

    <Fact>
    Public Sub ProxyPreview_IsSmaller_FinalIsFullResolution()
        Dim bg = TestHelpers.NoisyBackground(1200, 800)
        Dim fig = TestHelpers.StickFigure(60, 120)
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        layer.Transform = New LayerTransform With {.X = 600, .Y = 700, .Scale = 3}
        Dim scene = SceneWith(bg, layer)
        Dim renderer = CompositeRenderer.CreateDefault()
        Dim preview = renderer.Render(scene, New RenderOptions With {.MaxSide = 300, .Quality = ResampleQuality.Bilinear})
        Assert.Equal(300, preview.Image.Width)
        Assert.Equal(200, preview.Image.Height)
        Dim final = New ExportService(renderer).RenderFinal(scene, Nothing)
        Assert.Equal(1200, final.Image.Width)
        Assert.Equal(800, final.Image.Height)
        ' 人物中心在預覽與正式輸出的對應位置都是人物顏色。
        Assert.Equal(240, CInt(final.Image.GetPixel(600, 500).R))
        Assert.InRange(CInt(preview.Image.GetPixel(150, 125).R), 230, 250)
    End Sub

    <Fact>
    Public Sub LargeImage_RendersWithLayerBoundedWork()
        Dim bg = RgbaImage.Filled(6000, 4000, 90, 120, 150)
        Dim fig = TestHelpers.StickFigure(300, 600)
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        layer.Transform = New LayerTransform With {.X = 3000, .Y = 3500, .Scale = 2}
        layer.Shadow = New ShadowParams With {.Enabled = True}
        Dim scene = SceneWith(bg, layer)
        Dim sw = Stopwatch.StartNew()
        Dim r = CompositeRenderer.CreateDefault().Render(scene, New RenderOptions With {.MaxSide = 0})
        Assert.Equal(6000, r.Image.Width)
        Assert.Equal(4000, r.Image.Height)
        Assert.Equal(240, CInt(r.Image.GetPixel(3000, 2600).R))
        Assert.Equal(90, CInt(r.Image.GetPixel(10, 10).R))
        Assert.True(sw.Elapsed.TotalSeconds < 60, $"大圖合成耗時 {sw.Elapsed.TotalSeconds:0.0} 秒")
    End Sub

    <Fact>
    Public Sub Decontaminate_RemovesWhiteFringeFromSemiTransparentEdge()
        ' 紅色人物、邊緣半透明像素帶著原背景的白色。
        Dim img = RgbaImage.Filled(20, 20, 255, 255, 255)
        Dim alpha As New FloatMask(20, 20)
        For y = 0 To 19
            For x = 0 To 9
                img.SetPixel(x, y, 200, 20, 20, 255)
                alpha(x, y) = 1
            Next
            alpha(10, y) = 0.4F ' 邊緣，RGB 仍是白色
        Next
        Dim fixedImg = New MaskRefinementService().DecontaminateEdges(img, alpha, 2)
        Dim edge = fixedImg.GetPixel(10, 10)
        Assert.True(edge.R > edge.G + 80, $"邊緣應偏向人物紅色，實際 {edge}")
        Assert.Equal(img.GetPixel(3, 3), fixedImg.GetPixel(3, 3)) ' 不透明像素不變
    End Sub

    <Fact>
    Public Sub MaskRefine_KeepsSoftEdges_AndRemovesSpecks()
        Dim m As New FloatMask(60, 60)
        For y = 10 To 49
            For x = 10 To 49
                m(x, y) = 1
            Next
        Next
        For x = 10 To 49
            m(x, 50) = 0.5F ' 半透明邊（例如髮絲）
        Next
        m(2, 2) = 1 ' 孤立雜點
        Dim r = New MaskRefinementService().Refine(m, New MaskRefineOptions With {.Feather = 0})
        Assert.InRange(r(30, 50), 0.4F, 0.6F)
        Assert.Equal(0.0F, r(2, 2))
        Assert.Equal(1.0F, r(30, 30))
    End Sub

    <Fact>
    Public Sub History_UndoRedo_RestoresParametersAndMasks()
        Dim bg = TestHelpers.NoisyBackground(50, 50)
        Dim fig = TestHelpers.StickFigure(20, 40)
        Dim scene As New CompositeScene With {.Background = bg}
        Dim h As New SceneHistory()
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        scene.Layers.Add(layer)
        h.Clear(scene)
        scene.Layers(0).Transform.X = 33
        h.Record(scene, "移動")
        Dim oldMask = scene.Layers(0).Alpha
        scene.Layers(0).Alpha = FloatMask.Filled(20, 40, 0.5F)
        h.Record(scene, "改遮罩")
        Assert.Equal("改遮罩", h.Undo(scene))
        Assert.Same(oldMask, scene.Layers(0).Alpha)
        Assert.Equal(33.0, scene.Layers(0).Transform.X)
        h.Undo(scene)
        Assert.Equal(0.0, scene.Layers(0).Transform.X)
        Assert.False(h.CanUndo)
        h.Redo(scene) : h.Redo(scene)
        Assert.Equal(0.5F, scene.Layers(0).Alpha(0, 0))
        Assert.False(h.CanRedo)
    End Sub
End Class
