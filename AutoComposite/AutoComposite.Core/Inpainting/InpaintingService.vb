''' <summary>局部修補請求（第二階段）。</summary>
Public NotInheritable Class InpaintRequest
    ''' <summary>目前的合成結果（修補基礎）。</summary>
    Public Property Composite As RgbaImage
    ''' <summary>只允許修改的區域（1 = 可修改）。</summary>
    Public Property Mask As FloatMask
    Public Property Prompt As String = ""
    Public Property NegativePrompt As String = ""
    ''' <summary>修補強度 0..1。</summary>
    Public Property Strength As Double = 0.6
    Public Property Seed As Integer = 1234
    Public Property Steps As Integer = 25
End Class

Public Interface IInpaintingService
    ReadOnly Property IsAvailable As Boolean
    ReadOnly Property StatusMessage As String
    Function InpaintAsync(request As InpaintRequest, progress As IProgress(Of String), cancel As Threading.CancellationToken) As Task(Of RgbaImage)
End Interface

''' <summary>
''' Stable Diffusion 1.5 Inpainting 的佔位實作：完整 ONNX 管線（Text Encoder、Tokenizer、VAE Encoder／Decoder、
''' Inpainting UNet、Scheduler、雜訊與種子管理）尚未完成，所以永遠回報不可用，不假裝能修補。
''' 介面與遮罩混合規則（<see cref="InpaintBlend"/>）已固定，之後接上管線時不需改動呼叫端。
''' </summary>
Public NotInheritable Class DisabledInpaintingService
    Implements IInpaintingService

    Public ReadOnly Property IsAvailable As Boolean Implements IInpaintingService.IsAvailable
        Get
            Return False
        End Get
    End Property

    Public ReadOnly Property StatusMessage As String Implements IInpaintingService.StatusMessage
        Get
            Return "AI 局部修補屬於第二階段功能：Stable Diffusion Inpainting 的 ONNX 推論管線尚未整合，目前無法使用。"
        End Get
    End Property

    Public Function InpaintAsync(request As InpaintRequest, progress As IProgress(Of String), cancel As Threading.CancellationToken) As Task(Of RgbaImage) Implements IInpaintingService.InpaintAsync
        Throw New NotSupportedException(StatusMessage)
    End Function
End Class

''' <summary>
''' 生成結果與原圖的混合：只在遮罩範圍內採用生成像素，遮罩為 0 的地方逐像素保留原圖，
''' 確保修補範圍外的背景完全不變。
''' </summary>
Public NotInheritable Class InpaintBlend
    Private Sub New()
    End Sub

    Public Shared Function Apply(original As RgbaImage, generated As RgbaImage, mask As FloatMask) As RgbaImage
        If generated.Width <> original.Width OrElse generated.Height <> original.Height Then Throw New ArgumentException("生成結果尺寸與原圖不同。")
        If mask.Width <> original.Width OrElse mask.Height <> original.Height Then Throw New ArgumentException("修補遮罩尺寸與原圖不同。")
        Dim r = original.Clone()
        Dim o = original.Pixels, g = generated.Pixels, d = r.Pixels
        For i = 0 To mask.Data.Length - 1
            Dim m = mask.Data(i)
            If Not (m > 0) Then Continue For
            If m > 1 Then m = 1
            For c = 0 To 3
                d(i * 4 + c) = RgbaImage.ClampByte(o(i * 4 + c) + (CInt(g(i * 4 + c)) - o(i * 4 + c)) * m)
            Next
        Next
        Return r
    End Function
End Class
