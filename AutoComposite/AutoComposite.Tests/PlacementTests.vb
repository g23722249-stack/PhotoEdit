Imports Xunit

Public Class PlacementTests
    <Fact>
    Public Sub FigureAnalyzer_FindsFeetHeadAndBounds()
        Dim fig = TestHelpers.StickFigure(100, 200)
        Dim a = FigureAnalyzer.Analyze(TestHelpers.StickMask(fig))
        Assert.InRange(a.FeetCenter.X, 48, 52)
        Assert.InRange(a.FeetCenter.Y, 182, 186) ' 身體底部 0.92·200 = 184
        Assert.InRange(a.FeetWidth, 25, 32)
        Assert.True(a.Head.Y < 40)
        Assert.InRange(a.AxisTiltDegrees, -2, 2)
    End Sub

    <Fact>
    Public Sub FigureAnalyzer_IgnoresThinSpearTipBelowFeet()
        Dim fig = TestHelpers.StickFigure(100, 200)
        Dim m = TestHelpers.StickMask(fig)
        ' 長矛尖端伸到腳底以下（細長、像素很少）。
        For y = 184 To 199
            m(80, y) = 1
        Next
        Dim a = FigureAnalyzer.Analyze(m)
        Assert.InRange(a.FeetCenter.Y, 182, 186)
        Assert.InRange(a.FeetCenter.X, 46, 54)
    End Sub

    <Fact>
    Public Sub FigureAnalyzer_EmptyMaskThrows()
        Assert.Throws(Of InvalidOperationException)(Function() FigureAnalyzer.Analyze(New FloatMask(10, 10)))
    End Sub

    <Fact>
    Public Sub SceneAnalyzer_FindsColumnTopAgainstSky()
        Dim bg = TestHelpers.ColumnScene(600, 800, 260, 340, 350)
        Dim a = SceneAnalyzer.Analyze(bg)
        Assert.True(a.SkyFraction > 0.5)
        Dim best = a.Suggestions(0)
        Assert.True(best.FromSkyline)
        Assert.InRange(best.Anchor.X, 285, 315)
        Assert.InRange(best.Anchor.Y, 340, 362)
        Assert.InRange(best.SurfaceLeftX, 250, 275)
        Assert.InRange(best.SurfaceRightX, 325, 350)
        ' 天空遮罩：柱頂上方是天空、柱身不是。
        Assert.True(a.SkyMask(300, 200) > 0.9F)
        Assert.True(a.SkyMask(300, 500) < 0.1F)
    End Sub

    <Fact>
    Public Sub SceneAnalyzer_NoSky_FallsBackToGround()
        Dim bg = TestHelpers.NoisyBackground(300, 200)
        Dim a = SceneAnalyzer.Analyze(bg)
        Assert.Single(a.Suggestions)
        Assert.False(a.Suggestions(0).FromSkyline)
        Assert.InRange(a.Suggestions(0).Anchor.Y, 170, 190)
    End Sub

    <Fact>
    Public Sub AutoPlacement_StandsFigureOnColumnTop()
        Dim bg = TestHelpers.ColumnScene(600, 800, 260, 340, 350)
        Dim fig = TestHelpers.StickFigure(200, 400, transparent:=True)
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        Dim scene As New CompositeScene With {.Background = bg}
        scene.Layers.Add(layer)
        Dim analysis = SceneAnalyzer.Analyze(bg)
        scene.SkyMask = analysis.SkyMask
        Dim cands = New AutoPlacementService().Suggest(scene, layer, analysis)
        Assert.NotEmpty(cands)
        Dim best = cands(0)
        Dim m = TransformService.Compose(best.Transform, best.SourceAnchor, fig.Width, fig.Height)
        Dim feet = m.Transform(best.SourceAnchor)
        Assert.InRange(feet.X, 280, 320)
        Assert.InRange(feet.Y, 340, 362)
        ' 腳寬不超出柱頂、人物不超出畫面。
        Dim fa = FigureAnalyzer.Analyze(layer.Alpha)
        Assert.True(fa.FeetWidth * best.Transform.Scale <= 80)
        Assert.True(feet.Y - fa.Height * best.Transform.Scale >= 0)
        Assert.True(best.Score > 0.5, best.PenaltySummary())
        Assert.Contains("天際線", best.Description)
    End Sub

    <Fact>
    Public Sub AutoPlacement_UserAnchorAndHeightWin()
        Dim bg = TestHelpers.NoisyBackground(500, 400)
        Dim fig = TestHelpers.StickFigure(100, 200)
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        Dim scene As New CompositeScene With {.Background = bg}
        scene.Layers.Add(layer)
        scene.Target = New PlacementTarget With {.HasAnchor = True, .AnchorX = 123, .AnchorY = 321, .TargetHeight = 150}
        Dim cands = New AutoPlacementService().Suggest(scene, layer, SceneAnalyzer.Analyze(bg))
        Dim best = cands(0)
        Assert.Equal(123.0, best.Transform.X, 3)
        Assert.Equal(321.0, best.Transform.Y, 3)
        Dim fa = FigureAnalyzer.Analyze(layer.Alpha)
        Assert.InRange(fa.Height * best.Transform.Scale, 149, 151)
    End Sub

    <Fact>
    Public Sub AutoPlacement_PenalizesCoveringProtectedObject()
        Dim bg = TestHelpers.NoisyBackground(500, 400)
        Dim fig = TestHelpers.StickFigure(100, 200)
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        Dim scene As New CompositeScene With {.Background = bg}
        scene.Layers.Add(layer)
        scene.Target = New PlacementTarget With {.HasAnchor = True, .AnchorX = 250, .AnchorY = 380, .TargetHeight = 200}
        scene.ProtectMask = New FloatMask(500, 400)
        For y = 0 To 399
            For x = 200 To 300
                scene.ProtectMask(x, y) = 1
            Next
        Next
        Dim c = New AutoPlacementService().Suggest(scene, layer, SceneAnalyzer.Analyze(bg))
        Dim centered = c.First(Function(x) Math.Abs(x.Transform.X - 250) < 0.01 AndAlso Math.Abs(x.Transform.Scale * 184 - 200) < 40)
        Assert.True(centered.Penalties("遮住保護區") > 0.5)
    End Sub

    <Fact>
    Public Sub AutoPlacement_GuidesProduceRotationAndKeystone()
        Dim bg = TestHelpers.NoisyBackground(400, 1000)
        Dim fig = TestHelpers.StickFigure(100, 200)
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        Dim scene As New CompositeScene With {.Background = bg}
        scene.Layers.Add(layer)
        scene.Target = New PlacementTarget With {.HasAnchor = True, .AnchorX = 200, .AnchorY = 600, .TargetHeight = 300}
        scene.Guides.Add(New GuideLine With {.X1 = 150, .Y1 = 1000, .X2 = 175, .Y2 = 50})
        scene.Guides.Add(New GuideLine With {.X1 = 250, .Y1 = 1000, .X2 = 225, .Y2 = 50})
        Dim best = New AutoPlacementService().Suggest(scene, layer, Nothing)(0)
        Assert.InRange(best.Transform.Rotation, -0.5, 0.5) ' 對稱收斂 → 不需旋轉
        Assert.Equal(PerspectiveMode.Keystone, best.Transform.Perspective.Mode)
        Assert.True(best.Transform.Perspective.KeystoneRatio < 1)
    End Sub
End Class
