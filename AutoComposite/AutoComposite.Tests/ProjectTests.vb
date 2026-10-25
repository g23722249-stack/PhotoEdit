Imports System.IO
Imports Xunit

Public Class ProjectTests
    <Fact>
    Public Sub SaveAndLoad_RoundTripsParametersMasksAndRelativePaths()
        Dim dir = TestHelpers.TempDir()
        Dim codec As New PngCodec()
        Dim bgPath = Path.Combine(dir, "bg.png"), figPath = Path.Combine(dir, "fig.png")
        Dim bg = TestHelpers.NoisyBackground(120, 90)
        Dim fig = TestHelpers.StickFigure(30, 60)
        codec.Encode(bg, bgPath, ExportFormat.Png, 0)
        codec.Encode(fig, figPath, ExportFormat.Png, 0)

        Dim scene As New CompositeScene With {.BackgroundPath = bgPath, .Background = bg}
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        layer.SourcePath = figPath
        layer.AiAlpha = layer.Alpha.Clone()
        layer.Alpha.Data(5) = 0.123456F
        layer.MaskModelId = "birefnet-toonout" : layer.MaskModelRevision = "abc"
        layer.Transform = New LayerTransform With {.X = 60, .Y = 80, .Scale = 0.75, .Rotation = -5,
            .Perspective = New PerspectiveParams With {.Mode = PerspectiveMode.Keystone, .KeystoneRatio = 0.9}}
        layer.Color = New ColorParams With {.Mode = ColorMode.EnvironmentMatch, .Temperature = 0.2}
        layer.Shadow = New ShadowParams With {.Direction = 140, .Receiver = ShadowReceiver.NonSky}
        scene.Layers.Add(layer)
        scene.ForegroundMask = New FloatMask(120, 90)
        scene.ForegroundMask(10, 10) = 0.5F
        scene.Guides.Add(New GuideLine With {.X1 = 1, .Y1 = 2, .X2 = 3, .Y2 = 4})
        scene.Target = New PlacementTarget With {.HasAnchor = True, .AnchorX = 61, .AnchorY = 79, .TargetHeight = 50}

        Dim projPath = Path.Combine(dir, "test" & ProjectSerializer.Extension)
        Dim ser As New ProjectSerializer()
        ser.Save(scene, projPath)
        Dim json = File.ReadAllText(projPath)
        Assert.DoesNotContain(bgPath, json) ' 專案資料夾內 → 相對路徑
        Assert.Contains("""bg.png""", json)
        Assert.True(json.Length < 20000, "JSON 不應內嵌影像")
        Assert.True(File.Exists(Path.Combine(dir, "test_files", $"layer-{layer.Id}-mask.png")))

        Dim missing As List(Of MissingFile) = Nothing
        Dim loaded = ser.Load(projPath, missing)
        Assert.Empty(missing)
        Dim warnings As New List(Of String)()
        ProjectSerializer.LoadImages(loaded, New ImageLoader(), warnings)
        Assert.Empty(warnings)
        Dim l2 = loaded.Layers(0)
        Assert.Equal(layer.Id, l2.Id)
        Assert.Equal(0.75, l2.Transform.Scale)
        Assert.Equal(PerspectiveMode.Keystone, l2.Transform.Perspective.Mode)
        Assert.Equal(0.9, l2.Transform.Perspective.KeystoneRatio)
        Assert.Equal(ColorMode.EnvironmentMatch, l2.Color.Mode)
        Assert.Equal(ShadowReceiver.NonSky, l2.Shadow.Receiver)
        Assert.Equal("abc", l2.MaskModelRevision)
        Assert.InRange(l2.Alpha.Data(5), 0.1234F, 0.1235F)
        Assert.NotNull(l2.AiAlpha)
        Assert.Equal(0.5F, loaded.ForegroundMask(10, 10), 3)
        TestHelpers.SameSeq(bg.Pixels, loaded.Background.Pixels)
        Assert.Single(loaded.Guides)
        Assert.Equal(50.0, loaded.Target.TargetHeight)
        Assert.Contains("keystone", json.ToLowerInvariant())
    End Sub

    <Fact>
    Public Sub Load_ReportsMissingFiles_AndRelocateFixesThem()
        Dim dir = TestHelpers.TempDir()
        Dim codec As New PngCodec()
        Dim bgPath = Path.Combine(dir, "bg.png")
        codec.Encode(TestHelpers.NoisyBackground(20, 20), bgPath, ExportFormat.Png, 0)
        Dim scene As New CompositeScene With {.BackgroundPath = bgPath, .Background = TestHelpers.NoisyBackground(20, 20)}
        Dim projPath = Path.Combine(dir, "p" & ProjectSerializer.Extension)
        Dim ser As New ProjectSerializer()
        ser.Save(scene, projPath)
        Dim moved = Path.Combine(dir, "moved.png")
        File.Move(bgPath, moved)
        Dim missing As List(Of MissingFile) = Nothing
        Dim loaded = ser.Load(projPath, missing)
        Assert.Single(missing)
        Assert.Equal("background", missing(0).Kind)
        ProjectSerializer.Relocate(loaded, missing(0), moved)
        Assert.Empty(ProjectSerializer.FindMissing(loaded))
        ProjectSerializer.LoadImages(loaded, New ImageLoader(), New List(Of String)())
        Assert.Equal(20, loaded.Width)
    End Sub

    <Fact>
    Public Sub NewerProjectVersion_IsRejected()
        Dim dir = TestHelpers.TempDir()
        Dim p = Path.Combine(dir, "x" & ProjectSerializer.Extension)
        File.WriteAllText(p, "{""version"": 99, ""scene"": {}}")
        Dim missing As List(Of MissingFile) = Nothing
        Assert.Throws(Of InvalidDataException)(Function() New ProjectSerializer().Load(p, missing))
    End Sub

    <Fact>
    Public Sub Export_Png_WritesFullResolution()
        Dim dir = TestHelpers.TempDir()
        Dim bg = TestHelpers.NoisyBackground(200, 150)
        Dim fig = TestHelpers.StickFigure(30, 60)
        Dim scene As New CompositeScene With {.Background = bg}
        Dim layer = TestHelpers.NewLayer(fig, TestHelpers.StickMask(fig))
        layer.Transform = New LayerTransform With {.X = 100, .Y = 140, .Scale = 1.5}
        scene.Layers.Add(layer)
        Dim out = Path.Combine(dir, "out.png")
        Dim rep = New ExportService(CompositeRenderer.CreateDefault()).Export(scene, out, ExportFormat.Png, 90, New PngCodec(), Nothing)
        Dim d = New PngCodec().Decode(out)
        Assert.Equal(200, d.Image.Width)
        Assert.Equal(150, d.Image.Height)
        Assert.Equal("RGB", d.Info.ColorMode) ' 背景不透明 → RGB
        Assert.Equal(200, rep.Width)
        ' JPEG 需要 WIC 編碼器，內建 PNG 編碼器明確拒絕。
        Assert.Throws(Of NotSupportedException)(Function() New ExportService(CompositeRenderer.CreateDefault()).Export(scene, Path.Combine(dir, "o.jpg"), ExportFormat.Jpeg, 90, New PngCodec(), Nothing))
    End Sub

    <Fact>
    Public Sub Inpainting_IsHonestlyDisabled_AndBlendProtectsOutsideMask()
        Dim svc As New DisabledInpaintingService()
        Assert.False(svc.IsAvailable)
        Assert.Throws(Of NotSupportedException)(Sub() svc.InpaintAsync(New InpaintRequest(), Nothing, Nothing))
        Dim original = TestHelpers.NoisyBackground(30, 30, 1)
        Dim generated = TestHelpers.NoisyBackground(30, 30, 2)
        Dim mask As New FloatMask(30, 30)
        For y = 10 To 19
            For x = 10 To 19
                mask(x, y) = 1
            Next
        Next
        Dim r = InpaintBlend.Apply(original, generated, mask)
        For y = 0 To 29
            For x = 0 To 29
                If mask(x, y) = 0 Then
                    Assert.Equal(original.GetPixel(x, y), r.GetPixel(x, y))
                Else
                    Assert.Equal(generated.GetPixel(x, y), r.GetPixel(x, y))
                End If
            Next
        Next
    End Sub

    <Fact>
    Public Sub Workflow_EndToEnd_WithTransparentFigure()
        Dim dir = TestHelpers.TempDir()
        Dim codec As New PngCodec()
        Dim bgPath = Path.Combine(dir, "athena.png"), figPath = Path.Combine(dir, "warrior.png")
        codec.Encode(TestHelpers.ColumnScene(600, 800, 260, 340, 350), bgPath, ExportFormat.Png, 0)
        codec.Encode(TestHelpers.StickFigure(200, 400, transparent:=True), figPath, ExportFormat.Png, 0)
        Dim svc = CompositeServices.Create(Nothing, Nothing)
        Dim wf As New CompositeWorkflow(svc)
        wf.LoadBackground(bgPath)
        Dim layer = wf.AddFigure(figPath)
        Assert.Equal(SourceAlphaSegmentation.ModelId, layer.MaskModelId)
        Dim cands = wf.SuggestPlacements(layer)
        wf.ApplyPlacement(layer, cands(0))
        Assert.Equal(ShadowReceiver.NonSky, layer.Shadow.Receiver)
        wf.AutoBlend(layer, New AutoBlendOptions())
        Assert.Equal(ColorMode.EnvironmentMatch, layer.Color.Mode)
        Dim out = Path.Combine(dir, "result.png")
        wf.ExportImage(out, ExportFormat.Png, 95, Nothing)
        Assert.True(File.Exists(out))
        wf.SaveProject(Path.Combine(dir, "proj" & ProjectSerializer.Extension))
        Assert.True(wf.History.CanUndo)
        wf.History.Undo(wf.Scene)
        Assert.Equal(ColorMode.Original, wf.Scene.Layers(0).Color.Mode)
        Assert.Contains("背景分析", wf.Performance.ToText())
    End Sub
End Class
