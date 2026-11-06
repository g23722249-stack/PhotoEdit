Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>黑白照片上色的模型（檔名見 ColorizeModels.FileName）。</summary>
Public Enum ColorizeModel
    ''' <summary>DDColor tiny（fp16 權重、512×512，Apache-2.0）：快。</summary>
    DDColorTiny = 0
    ''' <summary>DDColor 完整版（Apache-2.0）：顏色最準、比較慢。</summary>
    DDColor = 1
    ''' <summary>DeOldify 藝術版（MIT）：顏色比較淡、柔和自然。</summary>
    DeOldify = 2
End Enum

Public NotInheritable Class ColorizeModels
    Private Sub New()
    End Sub

    ''' <summary>下拉選單的順序。</summary>
    Public Shared ReadOnly Order As ColorizeModel() = {ColorizeModel.DDColorTiny, ColorizeModel.DDColor, ColorizeModel.DeOldify}

    Public Shared Function FileName(model As ColorizeModel) As String
        Select Case model
            Case ColorizeModel.DDColor : Return "ddcolor.onnx"
            Case ColorizeModel.DeOldify : Return "deoldify_artistic.onnx"
            Case Else : Return "ddcolor-tiny-fp16.onnx"
        End Select
    End Function

    Public Shared Function DisplayName(model As ColorizeModel) As String
        Select Case model
            Case ColorizeModel.DDColor : Return Lang.T("精細（DDColor）")
            Case ColorizeModel.DeOldify : Return Lang.T("柔和（DeOldify）")
            Case Else : Return Lang.T("快速（DDColor 小）")
        End Select
    End Function

    ''' <summary>DDColor 類（輸入 0..1 的灰階 RGB、輸出 Lab 的 ab）；否則是 DeOldify（輸入 0..255、輸出 RGB 0..255）。</summary>
    Public Shared Function IsDDColor(model As ColorizeModel) As Boolean
        Return model <> ColorizeModel.DeOldify
    End Function

    ''' <summary>模型輸入大小不固定時用的大小（DeOldify 越大細節越多、但顏色越淡）。</summary>
    Public Shared Function DefaultSize(model As ColorizeModel) As Integer
        Return If(IsDDColor(model), 512, 384)
    End Function

    Public Shared Function ModelPath(model As ColorizeModel) As String
        Return Path.Combine(AppContext.BaseDirectory, "Models", FileName(model))
    End Function

    Public Shared Function Available(model As ColorizeModel) As Boolean
        Return File.Exists(ModelPath(model))
    End Function
End Class

''' <summary>照片目前的色調（判斷要不要上色）。</summary>
Public Enum PhotoTone
    ''' <summary>黑白（幾乎沒有彩度）。</summary>
    Gray
    ''' <summary>單色調：泛黃、褐色、藍調這種整張同一個色相的舊照片。</summary>
    Toned
    ''' <summary>本來就是彩色。</summary>
    Color
End Enum

''' <summary>上色之後的調整（不用重跑模型）。</summary>
Public Class ColorizeSettings
    ''' <summary>色彩強度 0..200（100＝模型原本的顏色）。</summary>
    Public Property Strength As Integer = 100
    ''' <summary>保留原本的色調 0..100（0＝全新的彩色，100＝原本的泛黃）。</summary>
    Public Property KeepTone As Integer
    ''' <summary>色溫 -100..100（正＝偏暖）。</summary>
    Public Property Temperature As Integer
    ''' <summary>修復褪色：亮度自動拉開（舊照片常常灰灰的）。</summary>
    Public Property FixFading As Boolean = True

    Public Function Clone() As ColorizeSettings
        Return DirectCast(MemberwiseClone(), ColorizeSettings)
    End Function
End Class

''' <summary>
''' 黑白、泛黃照片上色：模型只算顏色（Lab 的 a、b，模型的解析度），亮度一律用原照片的，所以放大回原尺寸時細節不會糊。
''' 流程：原圖 → 亮度 L（泛黃的色調一起去掉）→ 模型 → ab → 放大到原尺寸 → 和 L 合起來；
''' 強度、保留原色調、色溫在合起來時調（ColorizeSettings），換這些不用重跑模型。
''' </summary>
Public NotInheritable Class Colorizer
    Private Sub New()
    End Sub

    Private Shared ReadOnly SessionLock As New Object()
    Private Shared ReadOnly Sessions As New Dictionary(Of ColorizeModel, InferenceSession)()

    '---------------------------------------------------------------------
    ' 色調判斷
    '---------------------------------------------------------------------

    ''' <summary>平均彩度與色相集中度判斷：黑白、單色調（泛黃等）、彩色。也回傳平均彩度（Lab 單位）。</summary>
    Public Shared Function Analyze(bmp As Bitmap) As (Tone As PhotoTone, Chroma As Double)
        Using lab = SmallLab(bmp, 256)
            Dim n = 0, sumC = 0.0, sumA = 0.0, sumB = 0.0
            For y = 0 To lab.Rows - 1
                For x = 0 To lab.Cols - 1
                    Dim v = lab.At(Of Vec3f)(y, x)
                    If v.Item0 < 8 OrElse v.Item0 > 97 Then Continue For ' 太暗、太亮的地方顏色不可靠
                    sumC += Math.Sqrt(v.Item1 * v.Item1 + v.Item2 * v.Item2)
                    sumA += v.Item1 : sumB += v.Item2
                    n += 1
                Next
            Next
            If n = 0 Then Return (PhotoTone.Gray, 0)
            Dim chroma = sumC / n
            ' 色相集中：平均的 (a, b) 長度和平均彩度差不多 → 整張同一個色相
            Dim mean = Math.Sqrt((sumA / n) ^ 2 + (sumB / n) ^ 2)
            If chroma < 3 Then Return (PhotoTone.Gray, chroma)
            If mean > chroma * 0.75 Then Return (PhotoTone.Toned, chroma)
            Return (PhotoTone.Color, chroma)
        End Using
    End Function

    ''' <summary>縮到長邊 side 的 Lab（CV_32FC3：L 0..100、a/b 約 -127..127）。</summary>
    Private Shared Function SmallLab(bmp As Bitmap, side As Integer) As Mat
        Using bgr = ToBgr(bmp), small As New Mat(), f As New Mat()
            Dim k = Math.Min(1.0, side / CDbl(Math.Max(bgr.Width, bgr.Height)))
            Cv2.Resize(bgr, small, New OpenCvSharp.Size(Math.Max(1, CInt(bgr.Width * k)), Math.Max(1, CInt(bgr.Height * k))), 0, 0, InterpolationFlags.Area)
            small.ConvertTo(f, MatType.CV_32FC3, 1 / 255.0)
            Dim lab As New Mat()
            Cv2.CvtColor(f, lab, ColorConversionCodes.BGR2Lab)
            Return lab
        End Using
    End Function

    Private Shared Function ToBgr(bmp As Bitmap) As Mat
        Using src = BitmapConverter.ToMat(bmp)
            Dim bgr As New Mat()
            Select Case src.Channels()
                Case 4 : Cv2.CvtColor(src, bgr, ColorConversionCodes.BGRA2BGR)
                Case 1 : Cv2.CvtColor(src, bgr, ColorConversionCodes.GRAY2BGR)
                Case Else : src.CopyTo(bgr)
            End Select
            Return bgr
        End Using
    End Function

    '---------------------------------------------------------------------
    ' 模型
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 用模型算顏色：回傳模型解析度的 ab（CV_32FC2，Lab 單位）。很花時間，在背景執行緒呼叫。
    ''' 記憶體不足時放掉所有模型再試一次。
    ''' </summary>
    Public Shared Function PredictAb(bmp As Bitmap, model As ColorizeModel) As Mat
        If Not ColorizeModels.Available(model) Then Throw New FileNotFoundException(Lang.F("「{0}」模型還沒下載：請到 設定 → 模型 下載。", ColorizeModels.DisplayName(model)), ColorizeModels.ModelPath(model))
        For attempt = 1 To 2
            Try
                SyncLock SessionLock
                    Return Predict(bmp, model)
                End SyncLock
            Catch ex As OnnxRuntimeException When IsAllocationFailure(ex)
                ReleaseSessions()
                BackgroundRemover.ReleaseSessions()
                GC.Collect()
                GC.WaitForPendingFinalizers()
                If attempt = 2 Then Throw New InsufficientMemoryException(Lang.F("記憶體不足，「{0}」模型無法執行。請關掉其他程式再試，或改用「快速（DDColor 小）」。", ColorizeModels.DisplayName(model)), ex)
            End Try
        Next
        Return Nothing
    End Function

    Private Shared Function Predict(bmp As Bitmap, model As ColorizeModel) As Mat
        Dim session = GetSession(model)
        Dim name = session.InputMetadata.Keys.First()
        Dim meta = session.InputMetadata(name)
        Dim dims = meta.Dimensions
        Dim def = ColorizeModels.DefaultSize(model)
        Dim h = If(dims.Length = 4 AndAlso dims(2) > 0, dims(2), def), w = If(dims.Length = 4 AndAlso dims(3) > 0, dims(3), def)
        Dim dd = ColorizeModels.IsDDColor(model)
        ' 輸入：只有亮度的 RGB（L、a=b=0 → RGB；DDColor 0..1、DeOldify 0..255）
        Dim data(3 * w * h - 1) As Single
        Using bgr = ToBgr(bmp), small As New Mat(), f As New Mat(), lab As New Mat(), rgb As New Mat()
            Cv2.Resize(bgr, small, New OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Area)
            small.ConvertTo(f, MatType.CV_32FC3, 1 / 255.0)
            Cv2.CvtColor(f, lab, ColorConversionCodes.BGR2Lab)
            Dim ch = lab.Split()
            Try
                ch(1).SetTo(Scalar.All(0)) : ch(2).SetTo(Scalar.All(0))
                Using merged As New Mat()
                    Cv2.Merge(ch, merged)
                    Cv2.CvtColor(merged, rgb, ColorConversionCodes.Lab2RGB)
                End Using
            Finally
                For Each c In ch
                    c.Dispose()
                Next
            End Try
            Dim scale = If(dd, 1.0F, 255.0F)
            Dim px(w * h * 3 - 1) As Single
            Marshal.Copy(rgb.Data, px, 0, px.Length)
            For i = 0 To w * h - 1
                data(i) = Math.Clamp(px(i * 3), 0F, 1.0F) * scale
                data(w * h + i) = Math.Clamp(px(i * 3 + 1), 0F, 1.0F) * scale
                data(2 * w * h + i) = Math.Clamp(px(i * 3 + 2), 0F, 1.0F) * scale
            Next
        End Using
        Dim shape = {1, 3, h, w}
        Dim input = If(meta.ElementDataType = TensorElementType.Float16,
                       NamedOnnxValue.CreateFromTensor(name, New DenseTensor(Of Float16)(data.Select(Function(v) CType(v, Float16)).ToArray(), shape)),
                       NamedOnnxValue.CreateFromTensor(name, New DenseTensor(Of Single)(data, shape)))
        Dim outData As Single()
        Dim outDims As Integer()
        Using results = session.Run({input})
            Dim first = results.First()
            Dim half = TryCast(first.Value, DenseTensor(Of Float16))
            If half IsNot Nothing Then
                outData = half.ToArray().Select(Function(x) CType(x, Single)).ToArray()
                outDims = half.Dimensions.ToArray()
            Else
                Dim t = first.AsTensor(Of Single)()
                outData = t.ToArray()
                outDims = t.Dimensions.ToArray()
            End If
        End Using
        Dim oc = outDims(1), oh = outDims(2), ow = outDims(3)
        Dim ab As New Mat(oh, ow, MatType.CV_32FC2)
        Dim abPx(oh * ow * 2 - 1) As Single
        If dd AndAlso oc = 2 Then
            For i = 0 To oh * ow - 1
                abPx(i * 2) = outData(i)
                abPx(i * 2 + 1) = outData(oh * ow + i)
            Next
        Else
            ' DeOldify：輸出 RGB 0..255 → Lab 取 a、b
            Using rgbOut As New Mat(oh, ow, MatType.CV_32FC3), labOut As New Mat()
                Dim rgbPx(oh * ow * 3 - 1) As Single
                Dim k = If(dd, 1.0F, 1 / 255.0F)
                For i = 0 To oh * ow - 1
                    For c = 0 To 2
                        rgbPx(i * 3 + c) = Math.Clamp(outData(c * oh * ow + i) * k, 0F, 1.0F)
                    Next
                Next
                Marshal.Copy(rgbPx, 0, rgbOut.Data, rgbPx.Length)
                Cv2.CvtColor(rgbOut, labOut, ColorConversionCodes.RGB2Lab)
                Dim labPx(oh * ow * 3 - 1) As Single
                Marshal.Copy(labOut.Data, labPx, 0, labPx.Length)
                For i = 0 To oh * ow - 1
                    abPx(i * 2) = labPx(i * 3 + 1)
                    abPx(i * 2 + 1) = labPx(i * 3 + 2)
                Next
            End Using
        End If
        Marshal.Copy(abPx, 0, ab.Data, abPx.Length)
        Return ab
    End Function

    ''' <summary>模型的輸入大小與型別（測試、說明用）。</summary>
    Public Shared Function Describe(model As ColorizeModel) As String
        SyncLock SessionLock
            Dim s = GetSession(model)
            Dim i = s.InputMetadata.First(), o = s.OutputMetadata.First()
            Return $"{i.Key} [{String.Join(",", i.Value.Dimensions)}] {i.Value.ElementDataType} → {o.Key} [{String.Join(",", o.Value.Dimensions)}] {o.Value.ElementDataType}"
        End SyncLock
    End Function

    Private Shared Function IsAllocationFailure(ex As OnnxRuntimeException) As Boolean
        Dim m = If(ex.Message, "")
        Return m.IndexOf("allocate", StringComparison.OrdinalIgnoreCase) >= 0 OrElse m.IndexOf("bad_alloc", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>關掉所有快取的模型（釋放記憶體）。</summary>
    Public Shared Sub ReleaseSessions()
        SyncLock SessionLock
            For Each s In Sessions.Values
                s.Dispose()
            Next
            Sessions.Clear()
        End SyncLock
    End Sub

    Private Shared Function GetSession(model As ColorizeModel) As InferenceSession
        Dim s As InferenceSession = Nothing
        If Sessions.TryGetValue(model, s) Then Return s
        ' 不用 CPU 記憶體池（見 BackgroundRemover.GetSession）
        ' 記錄只留錯誤（大模型載入時會印一大堆「無法常數折疊」的警告）
        Dim options As New SessionOptions With {.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, .EnableCpuMemArena = False,
                                                .LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR}
        s = New InferenceSession(ColorizeModels.ModelPath(model), options)
        Sessions(model) = s
        Return s
    End Function

    '---------------------------------------------------------------------
    ' 合起來
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 原圖的亮度＋模型的顏色（放大到原尺寸）→ 彩色照片。ab 為 Nothing 時只做「保留原色調、修復褪色」（預覽還沒算好時用）。
    ''' </summary>
    Public Shared Function Compose(bmp As Bitmap, ab As Mat, settings As ColorizeSettings) As Bitmap
        Using bgr = ToBgr(bmp), f As New Mat(), lab As New Mat()
            bgr.ConvertTo(f, MatType.CV_32FC3, 1 / 255.0)
            Cv2.CvtColor(f, lab, ColorConversionCodes.BGR2Lab)
            Dim w = lab.Width, h = lab.Height
            Dim px(w * h * 3 - 1) As Single
            Marshal.Copy(lab.Data, px, 0, px.Length)
            ' 修復褪色：L 的 0.5%、99.5% 拉到 2、98
            If settings.FixFading Then
                Dim hist(1000) As Integer
                For i = 0 To w * h - 1
                    hist(CInt(Math.Clamp(px(i * 3) * 10, 0, 1000))) += 1
                Next
                Dim lo = Percentile(hist, w * h, 0.005) / 10.0, hi = Percentile(hist, w * h, 0.995) / 10.0
                If hi - lo > 10 Then
                    Dim k = (98 - 2) / (hi - lo)
                    For i = 0 To w * h - 1
                        px(i * 3) = CSng(Math.Clamp(2 + (px(i * 3) - lo) * k, 0, 100))
                    Next
                End If
            End If
            ' 顏色：模型的 ab（放大到原尺寸）× 強度，混一點原本的色調，再加色溫
            Dim abPx As Single() = Nothing
            If ab IsNot Nothing Then
                Using big As New Mat()
                    Cv2.Resize(ab, big, New OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Cubic)
                    ReDim abPx(w * h * 2 - 1)
                    Marshal.Copy(big.Data, abPx, 0, abPx.Length)
                End Using
            End If
            Dim s = settings.Strength / 100.0, keep = settings.KeepTone / 100.0
            Dim warmB = settings.Temperature * 0.15, warmA = settings.Temperature * 0.03
            For i = 0 To w * h - 1
                Dim a0 = px(i * 3 + 1), b0 = px(i * 3 + 2)
                Dim a = If(abPx Is Nothing, a0, abPx(i * 2) * s), b = If(abPx Is Nothing, b0, abPx(i * 2 + 1) * s)
                If abPx IsNot Nothing Then
                    a = a * (1 - keep) + a0 * keep
                    b = b * (1 - keep) + b0 * keep
                End If
                ' 亮部、暗部的顏色收一點（模型在純白、純黑的地方常亂塗）
                Dim l = px(i * 3)
                Dim edge = Math.Min(1.0, Math.Min(l / 12.0, (100 - l) / 8.0))
                px(i * 3 + 1) = CSng((a + warmA) * Math.Max(0, edge))
                px(i * 3 + 2) = CSng((b + warmB) * Math.Max(0, edge))
            Next
            Marshal.Copy(px, 0, lab.Data, px.Length)
            Using back As New Mat(), u8 As New Mat()
                Cv2.CvtColor(lab, back, ColorConversionCodes.Lab2BGR)
                back.ConvertTo(u8, MatType.CV_8UC3, 255)
                Return BitmapConverter.ToBitmap(u8)
            End Using
        End Using
    End Function

    Private Shared Function Percentile(hist As Integer(), total As Integer, p As Double) As Integer
        Dim target = total * p, acc = 0
        For i = 0 To hist.Length - 1
            acc += hist(i)
            If acc >= target Then Return i
        Next
        Return hist.Length - 1
    End Function
End Class
