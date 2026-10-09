Imports System.Drawing
Imports System.IO
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

Public Enum AnimeModel
    ''' <summary>人像：把臉畫成動畫臉（animegan2-pytorch Face Portrait v2，MIT）。</summary>
    FacePaint = 0
    ''' <summary>風景：宮崎駿背景風（AnimeGANv3 Hayao，僅限非商業使用）。</summary>
    Hayao = 1
End Enum

''' <summary>
''' 宮崎風 AI 版：AnimeGAN 系列 ONNX 模型（Models\ 下，不放進 git）。
''' 輸入輸出都是 RGB、-1..1；版面（NCHW／NHWC）與尺寸（固定／可變）讀模型自己的描述決定。
''' 固定尺寸的模型：等比例縮進去、邊緣用鏡射補滿，算完再切回來放大。
''' </summary>
Public NotInheritable Class AnimeStyle
    Private Sub New()
    End Sub

    Private Shared ReadOnly Files As String() = {"face_paint_512_v2_0.onnx", "AnimeGANv3_Hayao_36.onnx"}
    Private Shared ReadOnly _sessions(1) As InferenceSession
    Private Shared ReadOnly _lock As New Object()

    Public Shared Function ModelPath(m As AnimeModel) As String
        Return Path.Combine(AppContext.BaseDirectory, "Models", Files(CInt(m)))
    End Function

    Public Shared Function ModelAvailable(m As AnimeModel) As Boolean
        Return File.Exists(ModelPath(m))
    End Function

    Private Shared Function Session(m As AnimeModel) As InferenceSession
        SyncLock _lock
            If _sessions(CInt(m)) Is Nothing AndAlso ModelAvailable(m) Then
                _sessions(CInt(m)) = New InferenceSession(File.ReadAllBytes(ModelPath(m)),
                                                          New SessionOptions With {.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL})
            End If
            Return _sessions(CInt(m))
        End SyncLock
    End Function

    ''' <summary>模型的輸入輸出名稱與維度（除錯用）。</summary>
    Public Shared Function Describe(m As AnimeModel) As String
        Dim s = Session(m)
        If s Is Nothing Then Return "（沒有模型）"
        Dim f = Function(md As IReadOnlyDictionary(Of String, NodeMetadata)) String.Join("; ", md.Select(Function(kv) $"{kv.Key} [{String.Join(",", kv.Value.Dimensions)}]"))
        Return $"in: {f(s.InputMetadata)} | out: {f(s.OutputMetadata)}"
    End Function

    ''' <param name="maxSide">可變尺寸模型的處理長邊（越大越細、越慢）。</param>
    Public Shared Function Apply(source As Bitmap, m As AnimeModel, Optional maxSide As Integer = 1024) As Bitmap
        Dim s = Session(m)
        If s Is Nothing Then Return Nothing
        Dim inName = s.InputMetadata.Keys.First()
        Dim dims = s.InputMetadata(inName).Dimensions
        Dim nchw = dims.Length = 4 AndAlso dims(1) = 3
        Dim fixedH = If(nchw, dims(2), dims(1)), fixedW = If(nchw, dims(3), dims(2))
        Dim W = source.Width, H = source.Height
        Using full = BitmapConverter.ToMat(source), rgb As New Mat()
            Cv2.CvtColor(full, rgb, If(full.Channels() = 4, ColorConversionCodes.BGRA2RGB, ColorConversionCodes.BGR2RGB))
            ' 要送進模型的尺寸與放法
            Dim tw, th, cw, ch As Integer ' 模型大小、內容大小（其餘補邊）
            If fixedW > 0 AndAlso fixedH > 0 Then
                tw = fixedW : th = fixedH
                Dim k = Math.Min(tw / CDbl(W), th / CDbl(H))
                cw = Math.Max(1, CInt(W * k)) : ch = Math.Max(1, CInt(H * k))
            Else
                Dim k = Math.Min(1.0, maxSide / CDbl(Math.Max(W, H)))
                cw = Math.Max(32, CInt(Math.Round(W * k / 32)) * 32) : ch = Math.Max(32, CInt(Math.Round(H * k / 32)) * 32)
                tw = cw : th = ch
            End If
            Using small As New Mat(), padded As New Mat()
                Cv2.Resize(rgb, small, New OpenCvSharp.Size(cw, ch), 0, 0, InterpolationFlags.Area)
                Dim px = (tw - cw) \ 2, py = (th - ch) \ 2
                Cv2.CopyMakeBorder(small, padded, py, th - ch - py, px, tw - cw - px, BorderTypes.Reflect101)
                Dim input = If(nchw, New DenseTensor(Of Single)({1, 3, th, tw}), New DenseTensor(Of Single)({1, th, tw, 3}))
                Dim idx = padded.GetGenericIndexer(Of Vec3b)()
                For y = 0 To th - 1
                    For x = 0 To tw - 1
                        Dim v = idx(y, x)
                        For c = 0 To 2
                            Dim f = v(c) / 127.5F - 1.0F
                            If nchw Then input(0, c, y, x) = f Else input(0, y, x, c) = f
                        Next
                    Next
                Next
                Dim outArr As Single(), outDims As Integer()
                Using results = s.Run({NamedOnnxValue.CreateFromTensor(inName, input)})
                    Dim t = results.First().AsTensor(Of Single)()
                    outDims = t.Dimensions.ToArray()
                    outArr = t.ToArray()
                End Using
                Dim onchw = outDims.Length = 4 AndAlso outDims(1) = 3
                Dim oh = If(onchw, outDims(2), outDims(1)), ow = If(onchw, outDims(3), outDims(2))
                Using outMat As New Mat(oh, ow, MatType.CV_8UC3), crop As New Mat(), big As New Mat(), bgr As New Mat()
                    Dim oi = outMat.GetGenericIndexer(Of Vec3b)()
                    For y = 0 To oh - 1
                        For x = 0 To ow - 1
                            Dim v As Vec3b
                            For c = 0 To 2
                                Dim f = If(onchw, outArr((c * oh + y) * ow + x), outArr((y * ow + x) * 3 + c))
                                Dim b = CByte(Math.Max(0, Math.Min(255, (f + 1) * 127.5)))
                                If c = 0 Then v.Item0 = b Else If c = 1 Then v.Item1 = b Else v.Item2 = b
                            Next
                            oi(y, x) = v
                        Next
                    Next
                    ' 切掉補的邊（輸出和輸入同比例）
                    Dim sx = ow / CDbl(tw), sy = oh / CDbl(th)
                    Dim r As New OpenCvSharp.Rect(CInt(px * sx), CInt(py * sy), Math.Max(1, CInt(cw * sx)), Math.Max(1, CInt(ch * sy)))
                    Using part As New Mat(outMat, r)
                        part.CopyTo(crop)
                    End Using
                    Cv2.Resize(crop, big, New OpenCvSharp.Size(W, H), 0, 0, InterpolationFlags.Cubic)
                    Cv2.CvtColor(big, bgr, ColorConversionCodes.RGB2BGR)
                    Return BitmapConverter.ToBitmap(bgr)
                End Using
            End Using
        End Using
    End Function
End Class
