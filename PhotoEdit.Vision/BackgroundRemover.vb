Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>
''' AI 去背，五個模型（檔名見 CutoutModels.FileName）：
''' IS-Net 一般與 U²-Net 人像（rembg，Apache-2.0）、BiRefNet lite 一般（MIT）、MODNet 人像（Apache-2.0）、BiRefNet HR Matting 精細（MIT，fp16）、
''' BiRefNet ToonOut 動漫（MIT，fp16 權重、fp32 輸入輸出）。
''' 前處理：縮放到模型輸入大小（MODNet 保持比例，短邊 512、邊長 32 的倍數；其他縮成正方形）、正規化；
''' 後處理：rembg 的兩個做最小最大值正規化，BiRefNet lite 做 sigmoid，HR、MODNet、ToonOut 已經是 0..1 的透明度。最後放大回原尺寸。
''' 同一個模型的工作階段會共用；一次只跑一個推論。
''' </summary>
Public NotInheritable Class BackgroundRemover
    Private Sub New()
    End Sub

    Private Shared ReadOnly Sessions As New Dictionary(Of CutoutModel, InferenceSession)()
    Private Shared ReadOnly SessionLock As New Object()
    Private Shared ReadOnly ImageNetMean As Single() = {0.485F, 0.456F, 0.406F}
    Private Shared ReadOnly ImageNetStd As Single() = {0.229F, 0.224F, 0.225F}

    Public Shared Function ModelPath(model As CutoutModel) As String
        Return Path.Combine(AppContext.BaseDirectory, "Models", CutoutModels.FileName(model))
    End Function

    Public Shared Function ModelAvailable(model As CutoutModel) As Boolean
        Return File.Exists(ModelPath(model))
    End Function

    ''' <summary>MODNet 的輸入大小：短邊 512（長邊最多 1536），兩邊都是 32 的倍數。</summary>
    Public Shared Function ModNetInputSize(w As Integer, h As Integer) As OpenCvSharp.Size
        Dim k = 512.0 / Math.Min(w, h)
        k = Math.Min(k, 1536.0 / Math.Max(w, h))
        Dim fit = Function(v As Double) Math.Max(32, CInt(Math.Round(v / 32)) * 32)
        Return New OpenCvSharp.Size(fit(w * k), fit(h * k))
    End Function

    ''' <summary>算出主體遮罩（灰階 32bpp，白 = 主體），長邊不超過 MaskStore.MaxSide。</summary>
    Public Shared Function ComputeMask(image As Bitmap, model As CutoutModel) As Bitmap
        Dim inSize As OpenCvSharp.Size
        Select Case model
            Case CutoutModel.ModNet : inSize = ModNetInputSize(image.Width, image.Height)
            Case Else
                Dim s = CutoutModels.InputSize(model)
                inSize = New OpenCvSharp.Size(s, s)
        End Select
        Dim mean As Single(), std As Single()
        Select Case model
            Case CutoutModel.General : mean = {0.5F, 0.5F, 0.5F} : std = {1.0F, 1.0F, 1.0F}
            Case CutoutModel.ModNet : mean = {0.5F, 0.5F, 0.5F} : std = {0.5F, 0.5F, 0.5F}
            Case Else : mean = ImageNetMean : std = ImageNetStd
        End Select
        Dim iw = inSize.Width, ih = inSize.Height

        ' 前處理：RGB、0..1、減平均除標準差，排成 1×3×高×寬。
        Dim data(3 * iw * ih - 1) As Single
        Using bgra = BitmapConverter.ToMat(image), rgb As New Mat(), small As New Mat()
            Cv2.CvtColor(bgra, rgb, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2RGB, ColorConversionCodes.BGR2RGB))
            ' rembg 的兩個用 Lanczos（同 rembg）；其他照它們的參考實作用雙線性（縮小時用面積平均才不會鋸齒）
            Dim interp = If(model = CutoutModel.General OrElse model = CutoutModel.Human, InterpolationFlags.Lanczos4,
                            If(iw < image.Width, InterpolationFlags.Area, InterpolationFlags.Linear))
            Cv2.Resize(rgb, small, inSize, 0, 0, interp)
            Dim px(iw * ih * 3 - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(small.Data, px, 0, px.Length)
            ' U²-Net 先除以影像最大值；其他直接除以 255。
            Dim maxV = If(model = CutoutModel.Human, Math.Max(1, CInt(px.Max())), 255)
            Dim plane = iw * ih
            For i = 0 To plane - 1
                For c = 0 To 2
                    data(c * plane + i) = (px(i * 3 + c) / CSng(maxV) - mean(c)) / std(c)
                Next
            Next
        End Using

        Dim output As Single()
        SyncLock SessionLock
            Dim session = GetSession(model)
            Dim name = session.InputMetadata.Keys.First()
            Dim dims = {1, 3, ih, iw}
            Dim input = If(session.InputMetadata(name).ElementDataType = TensorElementType.Float16,
                           NamedOnnxValue.CreateFromTensor(name, New DenseTensor(Of Float16)(data.Select(Function(v) CType(v, Float16)).ToArray(), dims)),
                           NamedOnnxValue.CreateFromTensor(name, New DenseTensor(Of Single)(data, dims)))
            Using results = session.Run({input})
                Dim first = results.First()
                Dim half = TryCast(first.Value, DenseTensor(Of Float16))
                output = If(half IsNot Nothing, half.ToArray().Select(Function(x) CType(x, Single)).ToArray(), first.AsTensor(Of Single)().ToArray())
            End Using
        End SyncLock

        ' 後處理：第一個輸出的前 寬×高 個值轉成 0..255。
        Dim n = iw * ih
        Dim gray(n - 1) As Byte
        Select Case model
            Case CutoutModel.General, CutoutModel.Human
                Dim mn = Single.MaxValue, mx = Single.MinValue
                For i = 0 To n - 1
                    mn = Math.Min(mn, output(i)) : mx = Math.Max(mx, output(i))
                Next
                Dim range = Math.Max(0.00001F, mx - mn)
                For i = 0 To n - 1
                    gray(i) = CByte(Math.Round((output(i) - mn) / range * 255))
                Next
            Case CutoutModel.BiRefNetLite
                For i = 0 To n - 1
                    gray(i) = CByte(Math.Round(255 / (1 + Math.Exp(-output(i)))))
                Next
            Case Else
                For i = 0 To n - 1
                    gray(i) = CByte(Math.Round(Math.Max(0, Math.Min(1, output(i))) * 255))
                Next
        End Select

        Dim longSide = Math.Max(image.Width, image.Height)
        Dim k = Math.Min(1.0, MaskStore.MaxSide / CDbl(longSide))
        Dim mw = Math.Max(1, CInt(image.Width * k)), mh = Math.Max(1, CInt(image.Height * k))
        Using m As New Mat(ih, iw, MatType.CV_8UC1), big As New Mat(), bgraOut As New Mat()
            Runtime.InteropServices.Marshal.Copy(gray, 0, m.Data, n)
            Cv2.Resize(m, big, New OpenCvSharp.Size(mw, mh), 0, 0, If(mw < iw, InterpolationFlags.Area, InterpolationFlags.Cubic))
            Cv2.CvtColor(big, bgraOut, ColorConversionCodes.GRAY2BGRA)
            Return BitmapConverter.ToBitmap(bgraOut)
        End Using
    End Function

    Private Shared Function GetSession(model As CutoutModel) As InferenceSession
        Dim s As InferenceSession = Nothing
        If Sessions.TryGetValue(model, s) Then Return s
        Dim options As New SessionOptions With {.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL}
        s = New InferenceSession(File.ReadAllBytes(ModelPath(model)), options)
        Sessions(model) = s
        Return s
    End Function
End Class

''' <summary>依 AI 遮罩、修正筆觸、羽化與內縮外擴做出最終遮罩，再合成新背景。</summary>
Public NotInheritable Class CutoutCompositor
    Private Sub New()
    End Sub

    ''' <summary>最終遮罩（CV_8UC1，與 bgr 同大小）。aiMask 為 Nothing 時以全白開始（只靠筆觸）。</summary>
    ''' <param name="sourcePixels">來源的 BGRA 像素（魔術棒用；Nothing 時略過魔術棒）。</param>
    Public Shared Function BuildMask(aiMask As Bitmap, settings As CutoutSettings, w As Integer, h As Integer,
                                     Optional sourcePixels As Byte() = Nothing) As Mat
        Dim mask As New Mat(h, w, MatType.CV_8UC1, Scalar.All(255))
        If aiMask IsNot Nothing Then
            SyncLock aiMask
                Using bgra = BitmapConverter.ToMat(aiMask), g As New Mat()
                    Cv2.CvtColor(bgra, g, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2GRAY, ColorConversionCodes.BGR2GRAY))
                    Cv2.Resize(g, mask, New OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Linear)
                End Using
            End SyncLock
        End If
        Dim longSide = Math.Max(w, h)

        ' 清掉微弱殘值：10% 以下當背景、90% 以上當主體，中間線性拉開（模型常在背景留下淡淡一層）。
        If aiMask IsNot Nothing Then mask.ConvertTo(mask, -1, 1 / 0.8, -0.1 * 255 / 0.8)

        ' 邊緣精修：依照片附近的主體色、背景色重算透明度，只改邊緣附近（主體裡面、背景遠處不動）。
        If aiMask IsNot Nothing AndAlso sourcePixels IsNot Nothing AndAlso settings.EdgeRefine > 0 Then RefineEdges(mask, sourcePixels, w, h, settings.EdgeRefine, CutoutModels.InputSize(settings.Model))

        ' 內縮／外擴：形態學侵蝕或膨脹。
        If settings.Shift <> 0 Then
            Dim r = Math.Max(1, CInt(Math.Abs(settings.Shift) / 50.0 * longSide * 0.015))
            Using k = Cv2.GetStructuringElement(MorphShapes.Ellipse, New OpenCvSharp.Size(r * 2 + 1, r * 2 + 1))
                If settings.Shift > 0 Then Cv2.Dilate(mask, mask, k) Else Cv2.Erode(mask, mask, k)
            End Using
        End If

        ' 背景色填充：從四邊往內去掉背景色（白底插圖）。
        If sourcePixels IsNot Nothing AndAlso settings.EdgeFill Then
            Dim m(w * h - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(mask.Data, m, 0, m.Length)
            Dim sel = MagicWand.EdgeFill(sourcePixels, w, h, settings.EdgeFillTolerance, settings.EdgeFillColorArgb)
            If settings.WandDespeckle Then MagicWand.Despeckle(sel, w, h)
            MagicWand.ApplyToMask(m, sel, restore:=False)
            Runtime.InteropServices.Marshal.Copy(m, 0, mask.Data, m.Length)
        End If

        ' 魔術棒：依點擊順序去除或補回相近顏色的區域。
        If sourcePixels IsNot Nothing AndAlso settings.Wand IsNot Nothing AndAlso settings.Wand.Count > 0 Then
            Dim m(w * h - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(mask.Data, m, 0, m.Length)
            For Each click In settings.Wand
                Dim sel = MagicWand.SelectRegion(sourcePixels, w, h, click)
                If settings.WandDespeckle Then MagicWand.Despeckle(sel, w, h)
                MagicWand.ApplyToMask(m, sel, click.Restore)
            Next
            Runtime.InteropServices.Marshal.Copy(m, 0, mask.Data, m.Length)
        End If

        ' 修正筆觸：保留畫白、擦除畫黑（邊緣稍微柔和）。
        If settings.Strokes IsNot Nothing AndAlso settings.Strokes.Count > 0 Then
            For Each s In settings.Strokes
                Dim pts = s.Points().Select(Function(p) New OpenCvSharp.Point(CInt(p.X * w), CInt(p.Y * h))).ToList()
                Dim r = Math.Max(1, CInt(s.Radius * longSide))
                Dim color = If(s.Keep, Scalar.All(255), Scalar.All(0))
                If pts.Count = 1 Then
                    Cv2.Circle(mask, pts(0), r, color, -1, LineTypes.AntiAlias)
                Else
                    For i = 1 To pts.Count - 1
                        Cv2.Line(mask, pts(i - 1), pts(i), color, r * 2, LineTypes.AntiAlias)
                    Next
                End If
            Next
        End If

        ' 羽化。
        If settings.Feather > 0 Then
            Dim sigma = settings.Feather / 100.0 * longSide * 0.006
            If sigma >= 0.3 Then Cv2.GaussianBlur(mask, mask, New OpenCvSharp.Size(0, 0), sigma)
        End If

        ' 選取分頁的「當作去背範圍」與「刪除」：只保留選取區（各範圍取交集；選取區自己的羽化已算在內）。
        If settings.Regions IsNot Nothing AndAlso settings.Regions.Count > 0 Then
            Dim m(w * h - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(mask.Data, m, 0, m.Length)
            For Each region In settings.Regions
                Dim keep = SelectionMask.Render(region, w, h)
                For i = 0 To m.Length - 1
                    m(i) = CByte(CInt(m(i)) * keep(i) \ 255)
                Next
            Next
            Runtime.InteropServices.Marshal.Copy(m, 0, mask.Data, m.Length)
        End If
        Return mask
    End Function

    ''' <summary>
    ''' 邊緣精修的半徑（像素）：模型一個像素在這張圖上有多大（長邊 ÷ 模型輸入邊長）乘 1～3（強度越高看得越遠）。
    ''' 遮罩的邊緣模糊大約就是這麼寬，半徑比它小時看不到真正的邊。
    ''' </summary>
    Private Shared Function RefineRadius(w As Integer, h As Integer, strength As Integer, modelInput As Integer) As Integer
        Dim cell = Math.Max(w, h) / CDbl(Math.Max(64, modelInput))
        Return Math.Max(2, CInt(Math.Ceiling(cell * (1 + 2 * strength / 100.0))))
    End Function

    ''' <summary>
    ''' 邊緣精修：在原本邊緣附近的一圈（內外各一個半徑）裡，從遮罩確定的地方取附近的主體色 F、背景色 B，
    ''' 把每個像素的顏色投影到 B→F 這條線上，得到它的透明度（I = a·F + (1−a)·B）。頭髮、毛邊這些模型畫不細的地方
    ''' 會依照片真正的顏色變成半透明的細節。主體和背景顏色太接近、或像素顏色不在這條線上（第三種顏色）時不可信，
    ''' 退回模型的結果。其他地方完全不動。
    ''' （導向濾波在模型遮罩很模糊時斜率會被拉低，反而更糊，所以不用。）
    ''' </summary>
    Public Shared Sub RefineEdges(mask As Mat, sourcePixels As Byte(), w As Integer, h As Integer, strength As Integer, Optional modelInput As Integer = 1024)
        Dim r = RefineRadius(w, h, strength, modelInput)
        Dim n = w * h
        Dim pm(n - 1) As Byte
        Runtime.InteropServices.Marshal.Copy(mask.Data, pm, 0, n)
        Dim bandPx(n - 1) As Byte
        Using k = Cv2.GetStructuringElement(MorphShapes.Ellipse, New OpenCvSharp.Size(r * 2 + 1, r * 2 + 1)),
              dil As New Mat(), ero As New Mat(), band As New Mat()
            ' 邊緣帶：原遮罩膨脹與侵蝕差很多的地方，邊上再柔一點才不會看出接縫
            Cv2.Dilate(mask, dil, k) : Cv2.Erode(mask, ero, k)
            Cv2.Subtract(dil, ero, band)
            Cv2.Threshold(band, band, 24, 255, ThresholdTypes.Binary)
            Cv2.GaussianBlur(band, band, New OpenCvSharp.Size(0, 0), Math.Max(1.0, r / 3.0))
            Runtime.InteropServices.Marshal.Copy(band.Data, bandPx, 0, n)
        End Using
        ' 附近確定的主體色、背景色：權重只給遮罩很確定的像素，再做加權平均（權重也一起模糊，相除）
        Dim fm(n * 3 - 1) As Single, bm(n * 3 - 1) As Single, fd(n - 1) As Single, bd(n - 1) As Single
        Using bgra As New Mat(h, w, MatType.CV_8UC4), bgr As New Mat(), img As New Mat(), pf As New Mat(),
              wf As New Mat(), wb As New Mat(), wf3 As New Mat(), wb3 As New Mat(), fw As New Mat(), bw As New Mat(),
              fSum As New Mat(), bSum As New Mat(), fDen As New Mat(), bDen As New Mat(), fDen3 As New Mat(), bDen3 As New Mat(), fMean As New Mat(), bMean As New Mat()
            Runtime.InteropServices.Marshal.Copy(sourcePixels, 0, bgra.Data, sourcePixels.Length)
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR)
            bgr.ConvertTo(img, MatType.CV_32FC3, 1 / 255.0)
            mask.ConvertTo(pf, MatType.CV_32FC1, 1 / 255.0)
            ' 主體：p 0.9→1 權重 0→1；背景：p 0.1→0 權重 0→1
            Cv2.Subtract(pf, Scalar.All(0.9), wf) : Cv2.Multiply(wf, Scalar.All(10.0), wf) : Cv2.Max(wf, 0.0, wf)
            Cv2.Subtract(Scalar.All(0.1), pf, wb) : Cv2.Multiply(wb, Scalar.All(10.0), wb) : Cv2.Max(wb, 0.0, wb)
            Cv2.Merge({wf, wf, wf}, wf3) : Cv2.Merge({wb, wb, wb}, wb3)
            Cv2.Multiply(img, wf3, fw) : Cv2.Multiply(img, wb3, bw)
            Dim sigma = Math.Max(3.0, r * 1.5)
            Dim ks = New OpenCvSharp.Size(0, 0)
            Cv2.GaussianBlur(fw, fSum, ks, sigma) : Cv2.GaussianBlur(wf, fDen, ks, sigma)
            Cv2.GaussianBlur(bw, bSum, ks, sigma) : Cv2.GaussianBlur(wb, bDen, ks, sigma)
            Cv2.Max(fDen, 0.000001, fDen) : Cv2.Max(bDen, 0.000001, bDen)
            Cv2.Merge({fDen, fDen, fDen}, fDen3) : Cv2.Merge({bDen, bDen, bDen}, bDen3)
            Cv2.Divide(fSum, fDen3, fMean) : Cv2.Divide(bSum, bDen3, bMean)
            Runtime.InteropServices.Marshal.Copy(fMean.Data, fm, 0, fm.Length)
            Runtime.InteropServices.Marshal.Copy(bMean.Data, bm, 0, bm.Length)
            Runtime.InteropServices.Marshal.Copy(fDen.Data, fd, 0, n)
            Runtime.InteropServices.Marshal.Copy(bDen.Data, bd, 0, n)
        End Using
        Dim mix = 0.5F + 0.5F * Math.Max(0, Math.Min(100, strength)) / 100.0F
        Threading.Tasks.Parallel.For(0, h, Sub(y)
                                               For x = 0 To w - 1
                                                   Dim i = y * w + x
                                                   If bandPx(i) = 0 Then Continue For
                                                   ' F−B 與 I−B
                                                   Dim dd = 0.0F, dot = 0.0F
                                                   Dim dv(2) As Single, iv(2) As Single
                                                   For c = 0 To 2
                                                       dv(c) = fm(i * 3 + c) - bm(i * 3 + c)
                                                       iv(c) = sourcePixels(i * 4 + c) / 255.0F - bm(i * 3 + c)
                                                       dd += dv(c) * dv(c) : dot += iv(c) * dv(c)
                                                   Next
                                                   Dim dist = CSng(Math.Sqrt(dd))
                                                   If dist < 0.06F Then Continue For
                                                   Dim a = Math.Max(0.0F, Math.Min(1.0F, dot / dd))
                                                   ' 像素離 B→F 線多遠（第三種顏色）
                                                   Dim res = 0.0F
                                                   For c = 0 To 2
                                                       Dim e = iv(c) - a * dv(c)
                                                       res += e * e
                                                   Next
                                                   res = CSng(Math.Sqrt(res))
                                                   Dim conf = Math.Min(1.0F, (dist - 0.06F) / 0.15F) *
                                                              Math.Max(0.0F, 1 - res / (0.5F * dist)) *
                                                              Math.Min(1.0F, fd(i) / 0.02F) * Math.Min(1.0F, bd(i) / 0.02F)
                                                   Dim kk = bandPx(i) / 255.0F * conf * mix
                                                   Dim v = kk * a + (1 - kk) * pm(i) / 255.0F
                                                   pm(i) = CByte(Math.Round(Math.Max(0, Math.Min(1, v)) * 255))
                                               Next
                                           End Sub)
        Runtime.InteropServices.Marshal.Copy(pm, 0, mask.Data, n)
    End Sub

    ''' <summary>
    ''' 去除邊緣殘色：半透明的像素 I = a·F + (1−a)·B（F 主體、B 背景）。B 用附近背景像素的平均、F 從這個式子解回來，
    ''' 太透明（a 小）的地方解出來不穩，改用附近主體的平均色。只改 0.02 &lt; a &lt; 0.98 的像素。
    ''' </summary>
    Public Shared Sub Decontaminate(src As Byte(), alpha As Byte(), w As Integer, h As Integer, radius As Integer)
        Dim n = w * h
        Using bgra As New Mat(h, w, MatType.CV_8UC4), bgr As New Mat(), f As New Mat(), aM As New Mat(h, w, MatType.CV_8UC1), af As New Mat(),
              wf As New Mat(), wb As New Mat(), one As New Mat(), fw As New Mat(), bw As New Mat(), wf3 As New Mat(), wb3 As New Mat(),
              fSum As New Mat(), bSum As New Mat(), fDen As New Mat(), bDen As New Mat(), fMean As New Mat(), bMean As New Mat()
            Runtime.InteropServices.Marshal.Copy(src, 0, bgra.Data, src.Length)
            Runtime.InteropServices.Marshal.Copy(alpha, 0, aM.Data, n)
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR)
            bgr.ConvertTo(f, MatType.CV_32FC3, 1 / 255.0)
            aM.ConvertTo(af, MatType.CV_32FC1, 1 / 255.0)
            ' 權重：主體 a²、背景 (1−a)²（越確定越算數）
            Cv2.Multiply(af, af, wf)
            Cv2.Subtract(Scalar.All(1.0), af, one) : Cv2.Multiply(one, one, wb)
            Cv2.Merge({wf, wf, wf}, wf3) : Cv2.Merge({wb, wb, wb}, wb3)
            Cv2.Multiply(f, wf3, fw) : Cv2.Multiply(f, wb3, bw)
            Dim sigma = Math.Max(2.0, radius * 1.5)
            Cv2.GaussianBlur(fw, fSum, New OpenCvSharp.Size(0, 0), sigma) : Cv2.GaussianBlur(wf3, fDen, New OpenCvSharp.Size(0, 0), sigma)
            Cv2.GaussianBlur(bw, bSum, New OpenCvSharp.Size(0, 0), sigma) : Cv2.GaussianBlur(wb3, bDen, New OpenCvSharp.Size(0, 0), sigma)
            Cv2.Max(fDen, Scalar.All(0.0001), fDen) : Cv2.Max(bDen, Scalar.All(0.0001), bDen)
            Cv2.Divide(fSum, fDen, fMean) : Cv2.Divide(bSum, bDen, bMean)
            Dim fm(n * 3 - 1) As Single, bm(n * 3 - 1) As Single
            Runtime.InteropServices.Marshal.Copy(fMean.Data, fm, 0, fm.Length)
            Runtime.InteropServices.Marshal.Copy(bMean.Data, bm, 0, bm.Length)
            Threading.Tasks.Parallel.For(0, h, Sub(y)
                                                   For x = 0 To w - 1
                                                       Dim k = y * w + x
                                                       Dim a = alpha(k) / 255.0F
                                                       If a <= 0.02F OrElse a >= 0.98F Then Continue For
                                                       ' a 小時解出來的 F 不穩，往附近主體的平均色靠
                                                       Dim trust = Math.Max(0, Math.Min(1, (a - 0.1F) / 0.5F))
                                                       For c = 0 To 2
                                                           Dim i = src(k * 4 + c) / 255.0F
                                                           Dim est = (i - (1 - a) * bm(k * 3 + c)) / Math.Max(a, 0.05F)
                                                           est = Math.Max(0, Math.Min(1, est))
                                                           Dim v = fm(k * 3 + c) + (est - fm(k * 3 + c)) * trust
                                                           src(k * 4 + c) = CByte(Math.Round(Math.Max(0, Math.Min(1, v)) * 255))
                                                       Next
                                                   Next
                                               End Sub)
        End Using
    End Sub

    ''' <summary>依背景設定合成，回傳新的 32bpp 圖（透明背景時 alpha = 遮罩）。</summary>
    Public Shared Function Compose(source As Bitmap, aiMask As Bitmap, settings As CutoutSettings) As Bitmap
        Dim w = source.Width, h = source.Height
        Dim src = ReadBgra(source)
        Using mask = BuildMask(aiMask, settings, w, h, src)
            Dim m(w * h - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(mask.Data, m, 0, m.Length)
            ' 換背景、透明時才需要（檢查遮罩時看原本的顏色）
            If settings.EdgeDecontaminate AndAlso settings.Background <> CutoutBackground.MaskPreview AndAlso settings.Background <> CutoutBackground.Original Then
                src = DirectCast(src.Clone(), Byte())
                Decontaminate(src, m, w, h, RefineRadius(w, h, Math.Max(30, settings.EdgeRefine), CutoutModels.InputSize(settings.Model)))
            End If
            Dim bg As Byte() = Nothing
            Select Case settings.Background
                Case CutoutBackground.Color
                    Dim c = Drawing.Color.FromArgb(settings.BackgroundColorArgb)
                    bg = New Byte(src.Length - 1) {}
                    For i = 0 To w * h - 1
                        bg(i * 4) = c.B : bg(i * 4 + 1) = c.G : bg(i * 4 + 2) = c.R : bg(i * 4 + 3) = 255
                    Next
                Case CutoutBackground.Blur
                    bg = BlurredBackground(source, mask, settings.BackgroundBlur)
                Case CutoutBackground.Image
                    bg = CoverImage(StickerLibrary.GetImageFile(settings.BackgroundImagePath), w, h)
                Case CutoutBackground.MaskPreview
                    ' 背景蓋上 55% 紅色，方便檢查遮罩。
                    bg = DirectCast(src.Clone(), Byte())
                    For i = 0 To w * h - 1
                        bg(i * 4) = CByte(bg(i * 4) * 0.45)
                        bg(i * 4 + 1) = CByte(bg(i * 4 + 1) * 0.45)
                        bg(i * 4 + 2) = CByte(bg(i * 4 + 2) * 0.45 + 255 * 0.55)
                    Next
            End Select

            Dim outPx(src.Length - 1) As Byte
            For i = 0 To w * h - 1
                Dim a = m(i) / 255.0
                If bg Is Nothing Then
                    ' 透明（或圖片讀不到時也當透明）。
                    outPx(i * 4) = src(i * 4) : outPx(i * 4 + 1) = src(i * 4 + 1) : outPx(i * 4 + 2) = src(i * 4 + 2)
                    outPx(i * 4 + 3) = CByte(Math.Round(src(i * 4 + 3) * a))
                Else
                    For c = 0 To 2
                        outPx(i * 4 + c) = CByte(Math.Round(src(i * 4 + c) * a + bg(i * 4 + c) * (1 - a)))
                    Next
                    outPx(i * 4 + 3) = 255
                End If
            Next
            Dim result As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            WriteBgra(result, outPx)
            Return result
        End Using
    End Function

    ''' <summary>只用背景像素做模糊（遮罩加權），主體的顏色不會暈到背景上。</summary>
    Private Shared Function BlurredBackground(source As Bitmap, mask As Mat, amount As Integer) As Byte()
        Dim w = source.Width, h = source.Height
        Dim sigma = Math.Max(1.0, amount / 100.0 * Math.Max(w, h) * 0.02)
        Using bgra = BitmapConverter.ToMat(source), bgr As New Mat(), f As New Mat(), weight As New Mat(), inv As New Mat()
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR)
            bgr.ConvertTo(f, MatType.CV_32FC3, 1 / 255.0)
            ' 背景權重 = 1 - 遮罩（稍微內縮主體，避免邊緣殘影）。
            Using eroded As New Mat()
                Using k = Cv2.GetStructuringElement(MorphShapes.Ellipse, New OpenCvSharp.Size(7, 7))
                    Cv2.Dilate(mask, eroded, k)
                End Using
                Cv2.Subtract(Scalar.All(255), eroded, inv)
            End Using
            inv.ConvertTo(weight, MatType.CV_32FC1, 1 / 255.0)
            Using w3 As New Mat(), num As New Mat(), den As New Mat(), outF As New Mat(), out8 As New Mat(), outBgra As New Mat()
                Cv2.Merge({weight, weight, weight}, w3)
                Cv2.Multiply(f, w3, num)
                Cv2.GaussianBlur(num, num, New OpenCvSharp.Size(0, 0), sigma)
                Cv2.GaussianBlur(w3, den, New OpenCvSharp.Size(0, 0), sigma)
                Cv2.Max(den, Scalar.All(0.001), den)
                Cv2.Divide(num, den, outF)
                outF.ConvertTo(out8, MatType.CV_8UC3, 255)
                Cv2.CvtColor(out8, outBgra, ColorConversionCodes.BGR2BGRA)
                Dim px(w * h * 4 - 1) As Byte
                Using cont = outBgra.Clone()
                    Runtime.InteropServices.Marshal.Copy(cont.Data, px, 0, px.Length)
                End Using
                Return px
            End Using
        End Using
    End Function

    ''' <summary>背景圖片以「填滿並置中裁切」縮放到 w×h。</summary>
    Private Shared Function CoverImage(img As Bitmap, w As Integer, h As Integer) As Byte()
        If img Is Nothing Then Return Nothing
        Using bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb), g = Graphics.FromImage(bmp)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            SyncLock img
                Dim s = Math.Max(w / CDbl(img.Width), h / CDbl(img.Height))
                Dim sw = w / s, sh = h / s
                g.DrawImage(img, New Rectangle(0, 0, w, h), New RectangleF(CSng((img.Width - sw) / 2), CSng((img.Height - sh) / 2), CSng(sw), CSng(sh)), GraphicsUnit.Pixel)
            End SyncLock
            Return ReadBgra(bmp)
        End Using
    End Function

    Private Shared Function ReadBgra(bmp As Bitmap) As Byte()
        Dim w = bmp.Width, h = bmp.Height
        Dim data = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            Dim px(w * 4 * h - 1) As Byte
            For y = 0 To h - 1
                Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w * 4, w * 4)
            Next
            Return px
        Finally
            bmp.UnlockBits(data)
        End Try
    End Function

    Private Shared Sub WriteBgra(bmp As Bitmap, px As Byte())
        Dim w = bmp.Width, h = bmp.Height
        Dim data = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To h - 1
                Runtime.InteropServices.Marshal.Copy(px, y * w * 4, data.Scan0 + y * data.Stride, w * 4)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
    End Sub
End Class
