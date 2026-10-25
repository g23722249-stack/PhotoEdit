Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors

''' <summary>去背選項。</summary>
Public NotInheritable Class SegmentationOptions
    ''' <summary>覆寫清單的縮放方式："letterbox"（保留比例補邊）或 "stretch"；空字串 = 照清單。</summary>
    Public Property Resize As String = ""
    Public Property Preference As ExecutionPreference = ExecutionPreference.Auto
End Class

''' <summary>去背結果：與原圖同尺寸的浮點 Alpha（尚未精修）。</summary>
Public NotInheritable Class SegmentationResult
    Public Property Mask As FloatMask
    Public Property ModelId As String = ""
    Public Property ModelRevision As String = ""
    Public Property Provider As String = ""
    Public Property Milliseconds As Double
    ''' <summary>實際套用的輸出啟動函數（"sigmoid" 或 "none"）。</summary>
    Public Property Activation As String = ""
    Public Property Warnings As New List(Of String)()
End Class

''' <summary>去背服務介面（模型可替換）。</summary>
Public Interface IBackgroundRemovalService
    Inherits IDisposable
    ReadOnly Property ModelId As String
    ReadOnly Property DisplayName As String
    Function IsAvailable() As Boolean
    Function RemoveBackgroundAsync(image As RgbaImage, options As SegmentationOptions, progress As IProgress(Of String), cancel As Threading.CancellationToken) As Task(Of SegmentationResult)
    ''' <summary>釋放 ONNX 工作階段（記憶體／顯示記憶體）。</summary>
    Sub Unload()
End Interface

''' <summary>前處理時的補邊資訊（把模型輸出對回原圖用）。</summary>
Public Structure LetterboxInfo
    Public InputSize As Integer
    ''' <summary>原圖在模型輸入中的位置與大小（像素）。</summary>
    Public ContentX As Integer
    Public ContentY As Integer
    Public ContentW As Integer
    Public ContentH As Integer
End Structure

''' <summary>
''' 以 ONNX 模型（BiRefNet／ToonOut 等）去背。前處理依模型清單：RGB、0..1、ImageNet 平均與標準差、
''' 預設保留比例縮放後置中補邊（補邊值為平均色，正規化後為 0），不把非正方形人物拉伸變形；
''' 後處理：取出內容區域、依實際數值決定是否套 sigmoid（已是機率時不重複套用）、雙線性放大回原尺寸。
''' 工作階段建立一次後重複使用；DirectML 推論失敗時自動改用 CPU 重跑一次。
''' </summary>
Public NotInheritable Class OnnxBackgroundRemovalService
    Implements IBackgroundRemovalService

    Private ReadOnly _manifest As ModelManifest
    Private ReadOnly _modelPath As Func(Of String)
    Private ReadOnly _revision As Func(Of String)
    Private ReadOnly _loader As New OnnxModelLoader()
    Private ReadOnly _gate As New Threading.SemaphoreSlim(1, 1)
    Private _model As LoadedModel
    Private _loadedPreference As ExecutionPreference

    Public Event ModelLoaded(model As LoadedModel)

    Public Sub New(manifest As ModelManifest, modelPath As Func(Of String), Optional revision As Func(Of String) = Nothing)
        _manifest = manifest
        _modelPath = modelPath
        _revision = If(revision, Function() "")
    End Sub

    Public ReadOnly Property ModelId As String Implements IBackgroundRemovalService.ModelId
        Get
            Return _manifest.Id
        End Get
    End Property

    Public ReadOnly Property DisplayName As String Implements IBackgroundRemovalService.DisplayName
        Get
            Return _manifest.Name
        End Get
    End Property

    Public ReadOnly Property CurrentModel As LoadedModel
        Get
            Return _model
        End Get
    End Property

    Public Function IsAvailable() As Boolean Implements IBackgroundRemovalService.IsAvailable
        Dim p = _modelPath()
        Return Not String.IsNullOrEmpty(p) AndAlso IO.File.Exists(p)
    End Function

    Public Async Function RemoveBackgroundAsync(image As RgbaImage, options As SegmentationOptions, progress As IProgress(Of String), cancel As Threading.CancellationToken) As Task(Of SegmentationResult) Implements IBackgroundRemovalService.RemoveBackgroundAsync
        options = If(options, New SegmentationOptions())
        Await _gate.WaitAsync(cancel).ConfigureAwait(False)
        Try
            Return Await Task.Run(Function() RunCore(image, options, progress, cancel), cancel).ConfigureAwait(False)
        Finally
            _gate.Release()
        End Try
    End Function

    Private Function RunCore(image As RgbaImage, options As SegmentationOptions, progress As IProgress(Of String), cancel As Threading.CancellationToken) As SegmentationResult
        Dim sw = Stopwatch.StartNew()
        Dim result As New SegmentationResult With {.ModelId = _manifest.Id, .ModelRevision = _revision()}
        EnsureLoaded(options.Preference, progress)
        result.Warnings.AddRange(_model.Warnings)
        Dim pp = _manifest.Preprocess
        Dim mode = If(String.IsNullOrEmpty(options.Resize), pp.Resize, options.Resize)

        progress?.Report("前處理…")
        Dim size = ResolveInputSize(_model, pp)
        Dim info As LetterboxInfo
        Dim input = BuildInput(image, size, pp.Mean, pp.Std, mode, info)
        cancel.ThrowIfCancellationRequested()

        progress?.Report($"推論中（{_model.Provider}）…")
        Dim output As Single(), outW As Integer, outH As Integer
        Try
            output = Infer(input, size, outW, outH)
        Catch ex As OnnxRuntimeException When _model.Provider = "DirectML"
            ' 有些算子在 DirectML 載入成功但執行失敗：改用 CPU 重跑一次。
            AppLog.Warn($"DirectML 推論失敗，改用 CPU 重試：{ex.Message}")
            result.Warnings.Add("DirectML 推論失敗，已改用 CPU。")
            Unload()
            EnsureLoaded(ExecutionPreference.Cpu, progress)
            output = Infer(input, size, outW, outH)
        End Try
        cancel.ThrowIfCancellationRequested()

        progress?.Report("後處理…")
        Dim activation As String = Nothing
        result.Mask = ToMask(output, outW, outH, info, pp.OutputActivation, pp.MinMaxNormalize, image.Width, image.Height, activation)
        result.Activation = activation
        result.Provider = _model.Provider
        result.Milliseconds = sw.Elapsed.TotalMilliseconds
        AppLog.Info($"去背完成：{_manifest.Id}，{image.Width}×{image.Height}，{result.Provider}，{result.Milliseconds:0} ms，輸出啟動 {activation}")
        Return result
    End Function

    Private Sub EnsureLoaded(pref As ExecutionPreference, progress As IProgress(Of String))
        If _model IsNot Nothing AndAlso (_loadedPreference = pref OrElse _model.Provider = "CPU" AndAlso pref = ExecutionPreference.Cpu) Then Return
        Unload()
        Dim path = _modelPath()
        If String.IsNullOrEmpty(path) OrElse Not IO.File.Exists(path) Then
            Throw New IO.FileNotFoundException($"{_manifest.Name} 尚未安裝，請到「模型管理」下載。", If(path, _manifest.Id))
        End If
        progress?.Report($"載入模型 {_manifest.Name}…")
        _model = _loader.Load(path, pref, _manifest.Io)
        _loadedPreference = pref
        RaiseEvent ModelLoaded(_model)
    End Sub

    Private Shared Function ResolveInputSize(model As LoadedModel, pp As ManifestPreprocess) As Integer
        Dim inp = model.Inputs.First()
        If inp.Shape.Length = 4 AndAlso inp.Shape(2) > 0 AndAlso inp.Shape(3) > 0 Then
            If inp.Shape(2) <> inp.Shape(3) Then Throw New ModelValidationException($"模型輸入不是正方形（{inp}），目前只支援正方形輸入。")
            Return CInt(inp.Shape(2))
        End If
        Return pp.Size
    End Function

    Private Function Infer(input As Single(), size As Integer, ByRef outW As Integer, ByRef outH As Integer) As Single()
        Dim inSpec = If(String.IsNullOrEmpty(_manifest.Io.InputName), _model.Inputs.First(), _model.Inputs.First(Function(i) i.Name = _manifest.Io.InputName))
        Dim dims = {1, 3, size, size}
        Dim value As NamedOnnxValue
        If inSpec.ElementType = "float16" Then
            Dim t As New DenseTensor(Of Float16)(dims)
            Dim span = t.Buffer.Span
            For i = 0 To input.Length - 1
                span(i) = CType(input(i), Float16)
            Next
            value = NamedOnnxValue.CreateFromTensor(inSpec.Name, t)
        Else
            value = NamedOnnxValue.CreateFromTensor(inSpec.Name, New DenseTensor(Of Single)(input, dims))
        End If
        Using results = _model.Session.Run({value})
            Dim chosen = PickOutput(results, size)
            Dim tdims As Integer()
            Dim data As Single()
            If chosen.ElementType = TensorElementType.Float16 Then
                Dim t = chosen.AsTensor(Of Float16)()
                tdims = t.Dimensions.ToArray()
                data = t.ToArray().Select(Function(h) CType(h, Single)).ToArray()
            Else
                Dim t = chosen.AsTensor(Of Single)()
                tdims = t.Dimensions.ToArray()
                data = t.ToArray()
            End If
            If tdims.Length < 2 Then Throw New ModelValidationException($"模型輸出維度不足：[{String.Join(",", tdims)}]")
            outH = tdims(tdims.Length - 2) : outW = tdims(tdims.Length - 1)
            ' 多通道輸出（例如 [1,2,H,W]）時取最後一個通道（前景）。
            Dim plane = outW * outH
            If data.Length > plane Then data = data.Skip(data.Length - plane).ToArray()
            Return data
        End Using
    End Function

    ' 指定輸出名稱優先；否則挑最後兩維最大（最高解析度）的輸出。
    Private Function PickOutput(results As IDisposableReadOnlyCollection(Of DisposableNamedOnnxValue), size As Integer) As DisposableNamedOnnxValue
        If Not String.IsNullOrEmpty(_manifest.Io.OutputName) Then
            Dim named = results.FirstOrDefault(Function(r) r.Name = _manifest.Io.OutputName)
            If named IsNot Nothing Then Return named
        End If
        Dim best As DisposableNamedOnnxValue = Nothing
        Dim bestArea = -1L
        For Each r In results
            If r.ValueType <> OnnxValueType.ONNX_TYPE_TENSOR Then Continue For
            Dim dims As Integer()
            If r.ElementType = TensorElementType.Float16 Then
                dims = r.AsTensor(Of Float16)().Dimensions.ToArray()
            ElseIf r.ElementType = TensorElementType.Float Then
                dims = r.AsTensor(Of Single)().Dimensions.ToArray()
            Else
                Continue For
            End If
            If dims.Length < 2 Then Continue For
            Dim area = CLng(dims(dims.Length - 1)) * dims(dims.Length - 2)
            If area > bestArea Then best = r : bestArea = area
        Next
        If best Is Nothing Then Throw New ModelValidationException("模型沒有可用的浮點遮罩輸出。")
        Return best
    End Function

    Public Sub Unload() Implements IBackgroundRemovalService.Unload
        _model?.Dispose()
        _model = Nothing
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Unload()
        _gate.Dispose()
    End Sub

    ' ---------------- 前後處理（獨立的共用函式，方便測試） ----------------

    ''' <summary>
    ''' 產生 NCHW 輸入（RGB、(v/255 - mean)/std）。letterbox：等比縮放到長邊 = size，置中，補邊為平均色（正規化後 0）。
    ''' 原圖有透明區域時，透明像素以平均色代替，避免模型把殘留 RGB 當成前景。
    ''' </summary>
    Public Shared Function BuildInput(image As RgbaImage, size As Integer, mean As Double(), std As Double(), mode As String, ByRef info As LetterboxInfo) As Single()
        Dim letterbox = Not String.Equals(mode, "stretch", StringComparison.OrdinalIgnoreCase)
        Dim cw As Integer, ch As Integer
        If letterbox Then
            Dim s = size / CDbl(Math.Max(image.Width, image.Height))
            cw = Math.Max(1, Math.Min(size, CInt(Math.Round(image.Width * s))))
            ch = Math.Max(1, Math.Min(size, CInt(Math.Round(image.Height * s))))
        Else
            cw = size : ch = size
        End If
        info = New LetterboxInfo With {.InputSize = size, .ContentW = cw, .ContentH = ch, .ContentX = (size - cw) \ 2, .ContentY = (size - ch) \ 2}
        Dim resized = ResizeForModel(image, cw, ch)
        Dim plane = size * size
        Dim data(plane * 3 - 1) As Single ' 預設 0 = 正規化後的平均色
        Dim m0 = CSng(mean(0)), m1 = CSng(mean(1)), m2 = CSng(mean(2))
        Dim s0 = CSng(std(0)), s1 = CSng(std(1)), s2 = CSng(std(2))
        Dim p = resized.Pixels
        Dim ox = info.ContentX, oy = info.ContentY
        For y = 0 To ch - 1
            For x = 0 To cw - 1
                Dim i = (y * cw + x) * 4
                Dim a = p(i + 3) / 255.0F
                Dim r = p(i) / 255.0F * a + m0 * (1 - a)
                Dim g = p(i + 1) / 255.0F * a + m1 * (1 - a)
                Dim b = p(i + 2) / 255.0F * a + m2 * (1 - a)
                Dim o = (oy + y) * size + ox + x
                data(o) = (r - m0) / s0
                data(plane + o) = (g - m1) / s1
                data(2 * plane + o) = (b - m2) / s2
            Next
        Next
        Return data
    End Function

    ' 縮小用面積平均、放大用雙線性（RGB 依 Alpha 預乘處理）。
    Private Shared Function ResizeForModel(image As RgbaImage, w As Integer, h As Integer) As RgbaImage
        If w = image.Width AndAlso h = image.Height Then Return image
        If w <= image.Width AndAlso h <= image.Height Then Return image.ResizeArea(w, h)
        Dim alpha = image.ExtractAlpha()
        Dim sampler As New LayerSampler(image, alpha)
        Dim view = sampler.View(0)
        Dim dst As New RgbaImage(w, h)
        Dim sx = image.Width / CDbl(w), sy = image.Height / CDbl(h)
        Dim d = dst.Pixels
        For y = 0 To h - 1
            For x = 0 To w - 1
                ' 邊緣夾在影像內，避免外圍透明拉低邊界像素。
                Dim px = Math.Clamp((x + 0.5) * sx, 0.5, image.Width - 0.5), py = Math.Clamp((y + 0.5) * sy, 0.5, image.Height - 0.5)
                Dim v = view.Bilinear(px, py)
                Dim o = (y * w + x) * 4
                If v.A > 0 Then
                    d(o) = RgbaImage.ToByte(v.R / v.A) : d(o + 1) = RgbaImage.ToByte(v.G / v.A) : d(o + 2) = RgbaImage.ToByte(v.B / v.A)
                End If
                d(o + 3) = RgbaImage.ToByte(v.A)
            Next
        Next
        Return dst
    End Function

    ''' <summary>
    ''' 模型輸出 → 原圖尺寸遮罩。activation："auto" 時只有數值明顯超出 0..1（logits）才套 sigmoid。
    ''' 輸出解析度與輸入不同時，依比例換算內容區域。
    ''' </summary>
    Public Shared Function ToMask(output As Single(), outW As Integer, outH As Integer, info As LetterboxInfo, activation As String, minMax As Boolean,
                                  origW As Integer, origH As Integer, ByRef appliedActivation As String) As FloatMask
        Dim mn = Single.MaxValue, mx = Single.MinValue
        For Each v In output
            If v < mn Then mn = v
            If v > mx Then mx = v
        Next
        Dim act = If(activation, "auto").ToLowerInvariant()
        Dim useSigmoid = act = "sigmoid" OrElse (act = "auto" AndAlso (mn < -0.01F OrElse mx > 1.01F))
        appliedActivation = If(useSigmoid, "sigmoid", "none")
        Dim plane(output.Length - 1) As Single
        For i = 0 To output.Length - 1
            Dim v = output(i)
            plane(i) = If(useSigmoid, CSng(1 / (1 + Math.Exp(-v))), v)
        Next
        If minMax Then
            Dim lo = plane.Min(), hi = plane.Max()
            Dim range = Math.Max(0.00001F, hi - lo)
            For i = 0 To plane.Length - 1
                plane(i) = (plane(i) - lo) / range
            Next
        End If
        Dim full As New FloatMask(outW, outH, plane)
        full.Clamp01()
        ' 內容區域（輸出解析度下）。
        Dim kx = outW / CDbl(info.InputSize), ky = outH / CDbl(info.InputSize)
        Dim cx = CInt(Math.Round(info.ContentX * kx)), cy = CInt(Math.Round(info.ContentY * ky))
        Dim cw = Math.Max(1, Math.Min(outW - cx, CInt(Math.Round(info.ContentW * kx))))
        Dim chh = Math.Max(1, Math.Min(outH - cy, CInt(Math.Round(info.ContentH * ky))))
        Dim crop As New FloatMask(cw, chh)
        For y = 0 To chh - 1
            Array.Copy(full.Data, (cy + y) * outW + cx, crop.Data, y * cw, cw)
        Next
        Return crop.Resize(origW, origH)
    End Function
End Class

''' <summary>直接使用原圖 Alpha 當遮罩（透明 PNG 人物不需要再跑模型）。</summary>
Public NotInheritable Class SourceAlphaSegmentation
    Private Sub New()
    End Sub

    Public Const ModelId As String = "source-alpha"

    ''' <summary>原圖有足夠的透明區域（至少 1% 像素 Alpha &lt; 0.5）才視為已去背。</summary>
    Public Shared Function HasUsableAlpha(image As RgbaImage) As Boolean
        Dim p = image.Pixels
        Dim n = 0
        For i = 3 To p.Length - 1 Step 4
            If p(i) < 128 Then n += 1
        Next
        Return n >= image.PixelCount * 0.01
    End Function

    Public Shared Function FromSource(image As RgbaImage) As SegmentationResult
        Return New SegmentationResult With {.Mask = image.ExtractAlpha(), .ModelId = ModelId, .Provider = "none", .Activation = "none"}
    End Function
End Class
