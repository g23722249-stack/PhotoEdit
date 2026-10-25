Imports Xunit

Public Class SegmentationTests
    Private Shared Function TinyManifest(Optional activation As String = "auto", Optional inputType As String = "float32") As ModelManifest
        Return New ModelManifest With {
            .Id = "tiny", .Name = "測試模型", .Purpose = "segmentation",
            .Io = New ManifestIo With {.InputShape = {1, 3, 64, 64}, .InputType = inputType, .OutputShape = {1, 1, 64, 64}, .OutputType = inputType},
            .Preprocess = New ManifestPreprocess With {.Size = 64, .Mean = {0.5, 0.5, 0.5}, .Std = {0.5, 0.5, 0.5}, .OutputActivation = activation}}
    End Function

    <Fact>
    Public Sub OnnxHeader_ReadsOpsetWithoutLoadingGraph()
        Dim h = OnnxHeaderReader.Read(TestHelpers.Asset("tiny_sigmoid.onnx"))
        Assert.Equal(18L, h.DefaultOpset)
        Assert.Equal(8L, h.IrVersion)
        Assert.Equal("autocomposite-tests", h.ProducerName)
    End Sub

    <Fact>
    Public Sub OnnxHeader_RejectsGarbage()
        Dim p = IO.Path.Combine(TestHelpers.TempDir(), "bad.onnx")
        IO.File.WriteAllBytes(p, {&HFF, &HFF, &HFF})
        Assert.ThrowsAny(Of Exception)(Function() OnnxHeaderReader.Read(p))
    End Sub

    <Fact>
    Public Sub Loader_DirectMLUnavailable_FallsBackToCpu()
        Dim m = New OnnxModelLoader().Load(TestHelpers.Asset("tiny_sigmoid.onnx"), ExecutionPreference.DirectML, TinyManifest().Io)
        Try
            Assert.Equal("CPU", m.Provider)
            Assert.Contains(m.Warnings, Function(w) w.Contains("DirectML"))
            Assert.Equal("input", m.Inputs(0).Name)
            TestHelpers.SameSeq({1L, 3L, 64L, 64L}, m.Inputs(0).Shape)
            Assert.Equal("float32", m.Inputs(0).ElementType)
        Finally
            m.Dispose()
        End Try
    End Sub

    <Fact>
    Public Sub Loader_ShapeMismatch_IsRejected()
        Dim io As New ManifestIo With {.InputShape = {1, 3, 1024, 1024}, .InputType = "float32"}
        Dim ex = Assert.Throws(Of ModelValidationException)(Function() New OnnxModelLoader().Load(TestHelpers.Asset("tiny_sigmoid.onnx"), ExecutionPreference.Cpu, io))
        Assert.Contains("1024", ex.Message)
    End Sub

    <Fact>
    Public Sub MissingModel_IsReportedClearly()
        Dim svc As New OnnxBackgroundRemovalService(TinyManifest(), Function() IO.Path.Combine(TestHelpers.TempDir(), "nope.onnx"))
        Assert.False(svc.IsAvailable())
        Dim ex = Assert.ThrowsAsync(Of IO.FileNotFoundException)(Function() svc.RemoveBackgroundAsync(TestHelpers.StickFigure(10, 10), Nothing, Nothing, Nothing)).Result
        Assert.Contains("模型管理", ex.Message)
    End Sub

    <Theory>
    <InlineData("tiny_sigmoid.onnx", "float32", "none")>
    <InlineData("tiny_logits.onnx", "float32", "sigmoid")>
    <InlineData("tiny_fp16.onnx", "float16", "none")>
    Public Sub EndToEnd_MaskMatchesOriginalSizeAndShape(file As String, ioType As String, expectedActivation As String)
        ' 非正方形人物（寬 50、高 120）：letterbox 不能把它拉伸。
        Dim fig = TestHelpers.StickFigure(50, 120)
        Dim svc As New OnnxBackgroundRemovalService(TinyManifest(inputType:=ioType), Function() TestHelpers.Asset(file))
        Dim r = svc.RemoveBackgroundAsync(fig, New SegmentationOptions With {.Preference = ExecutionPreference.Cpu}, Nothing, Nothing).Result
        Try
            Assert.Equal(50, r.Mask.Width)
            Assert.Equal(120, r.Mask.Height)
            Assert.Equal(expectedActivation, r.Activation)
            Assert.Equal("CPU", r.Provider)
            ' 身體中心是前景、身體旁邊的黑底是背景（模型：亮 → 前景）。
            Assert.True(r.Mask(25, 70) > 0.9F, $"身體 {r.Mask(25, 70)}")
            Assert.True(r.Mask(3, 70) < 0.1F, $"背景 {r.Mask(3, 70)}")
            ' 頭頂上方與腳底以下也是背景（沒有因拉伸而錯位）。
            Assert.True(r.Mask(25, 2) < 0.1F)
            Assert.True(r.Mask(25, 117) < 0.1F)
            ' 遮罩保留中間值（不二值化）。
            Assert.Contains(r.Mask.Data, Function(v) v > 0.05F AndAlso v < 0.95F)
        Finally
            svc.Dispose()
        End Try
    End Sub

    <Theory>
    <InlineData(200, 100)>
    <InlineData(100, 300)>
    <InlineData(64, 64)>
    Public Sub Letterbox_PreservesAspectRatio(w As Integer, h As Integer)
        Dim img = TestHelpers.NoisyBackground(w, h)
        Dim info As LetterboxInfo
        Dim data = OnnxBackgroundRemovalService.BuildInput(img, 64, {0.485, 0.456, 0.406}, {0.229, 0.224, 0.225}, "letterbox", info)
        Assert.Equal(3 * 64 * 64, data.Length)
        Assert.Equal(64, Math.Max(info.ContentW, info.ContentH))
        Assert.InRange(info.ContentW / CDbl(info.ContentH), w / CDbl(h) * 0.95, w / CDbl(h) * 1.05)
        ' 補邊區域為 0（正規化後的平均色）。
        If info.ContentY > 0 Then Assert.Equal(0.0F, data(0))
        If info.ContentX > 0 Then Assert.Equal(0.0F, data(0))
    End Sub

    <Fact>
    Public Sub ToMask_AutoActivation_DoesNotDoubleApplySigmoid()
        Dim info As New LetterboxInfo With {.InputSize = 4, .ContentW = 4, .ContentH = 4}
        Dim probs = Enumerable.Repeat(0.9F, 16).ToArray()
        Dim act As String = Nothing
        Dim m = OnnxBackgroundRemovalService.ToMask(probs, 4, 4, info, "auto", False, 4, 4, act)
        Assert.Equal("none", act)
        Assert.InRange(m(1, 1), 0.89F, 0.91F) ' 若誤套 sigmoid 會變成 0.71
        Dim logits = Enumerable.Repeat(3.0F, 16).ToArray()
        m = OnnxBackgroundRemovalService.ToMask(logits, 4, 4, info, "auto", False, 4, 4, act)
        Assert.Equal("sigmoid", act)
        Assert.InRange(m(1, 1), 0.94F, 0.96F)
    End Sub

    <Fact>
    Public Sub SourceAlpha_IsUsedForTransparentPng()
        Dim fig = TestHelpers.StickFigure(30, 60, transparent:=True)
        Dim r = SourceAlphaSegmentation.FromSource(fig)
        Assert.Equal(SourceAlphaSegmentation.ModelId, r.ModelId)
        Assert.Equal(0.0F, r.Mask(1, 1))
        Assert.Equal(1.0F, r.Mask(15, 30))
    End Sub
End Class
