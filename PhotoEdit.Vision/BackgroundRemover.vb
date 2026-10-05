Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>
''' AI 去背：IS-Net（isnet-general-use.onnx，通用）與 U²-Net 人像（u2net_human_seg.onnx），模型來自 rembg（Apache-2.0）。
''' 前處理與後處理照 rembg：縮放到模型輸入大小、正規化、輸出做最小最大值正規化後放大回原尺寸。
''' 同一個模型的工作階段會共用；一次只跑一個推論。
''' </summary>
Public NotInheritable Class BackgroundRemover
    Private Sub New()
    End Sub

    Public Const GeneralModelFile As String = "isnet-general-use.onnx"
    Public Const HumanModelFile As String = "u2net_human_seg.onnx"

    Private Shared ReadOnly Sessions As New Dictionary(Of CutoutModel, InferenceSession)()
    Private Shared ReadOnly SessionLock As New Object()

    Public Shared Function ModelPath(model As CutoutModel) As String
        Return Path.Combine(AppContext.BaseDirectory, "Models", If(model = CutoutModel.Human, HumanModelFile, GeneralModelFile))
    End Function

    Public Shared Function ModelAvailable(model As CutoutModel) As Boolean
        Return File.Exists(ModelPath(model))
    End Function

    ''' <summary>算出主體遮罩（灰階 32bpp，白 = 主體），長邊不超過 MaskStore.MaxSide。</summary>
    Public Shared Function ComputeMask(image As Bitmap, model As CutoutModel) As Bitmap
        Dim size = If(model = CutoutModel.Human, 320, 1024)
        Dim mean = If(model = CutoutModel.Human, {0.485F, 0.456F, 0.406F}, {0.5F, 0.5F, 0.5F})
        Dim std = If(model = CutoutModel.Human, {0.229F, 0.224F, 0.225F}, {1.0F, 1.0F, 1.0F})

        ' 前處理：縮成正方形輸入（不保持比例，同 rembg），RGB、0..1、減平均除標準差。
        Dim input As New DenseTensor(Of Single)({1, 3, size, size})
        Using bgra = BitmapConverter.ToMat(image), rgb As New Mat(), small As New Mat()
            Cv2.CvtColor(bgra, rgb, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2RGB, ColorConversionCodes.BGR2RGB))
            Cv2.Resize(rgb, small, New OpenCvSharp.Size(size, size), 0, 0, InterpolationFlags.Lanczos4)
            Dim px(size * size * 3 - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(small.Data, px, 0, px.Length)
            ' U²-Net 先除以影像最大值；IS-Net 直接除以 255。
            Dim maxV = If(model = CutoutModel.Human, Math.Max(1, CInt(px.Max())), 255)
            For y = 0 To size - 1
                For x = 0 To size - 1
                    Dim i = (y * size + x) * 3
                    For c = 0 To 2
                        input(0, c, y, x) = (px(i + c) / CSng(maxV) - mean(c)) / std(c)
                    Next
                Next
            Next
        End Using

        Dim output As Single()
        SyncLock SessionLock
            Dim session = GetSession(model)
            Dim name = session.InputMetadata.Keys.First()
            Using results = session.Run({NamedOnnxValue.CreateFromTensor(name, input)})
                output = results.First().AsTensor(Of Single)().ToArray()
            End Using
        End SyncLock

        ' 後處理：第一個輸出的前 size×size 個值，最小最大值正規化到 0..255。
        Dim n = size * size
        Dim mn = Single.MaxValue, mx = Single.MinValue
        For i = 0 To n - 1
            mn = Math.Min(mn, output(i)) : mx = Math.Max(mx, output(i))
        Next
        Dim range = Math.Max(0.00001F, mx - mn)
        Dim gray(n - 1) As Byte
        For i = 0 To n - 1
            gray(i) = CByte(Math.Round((output(i) - mn) / range * 255))
        Next

        Dim longSide = Math.Max(image.Width, image.Height)
        Dim k = Math.Min(1.0, MaskStore.MaxSide / CDbl(longSide))
        Dim mw = Math.Max(1, CInt(image.Width * k)), mh = Math.Max(1, CInt(image.Height * k))
        Using m As New Mat(size, size, MatType.CV_8UC1), big As New Mat(), bgraOut As New Mat()
            Runtime.InteropServices.Marshal.Copy(gray, 0, m.Data, n)
            Cv2.Resize(m, big, New OpenCvSharp.Size(mw, mh), 0, 0, InterpolationFlags.Cubic)
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
        Return mask
    End Function

    ''' <summary>依背景設定合成，回傳新的 32bpp 圖（透明背景時 alpha = 遮罩）。</summary>
    Public Shared Function Compose(source As Bitmap, aiMask As Bitmap, settings As CutoutSettings) As Bitmap
        Dim w = source.Width, h = source.Height
        Dim src = ReadBgra(source)
        Using mask = BuildMask(aiMask, settings, w, h, src)
            Dim m(w * h - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(mask.Data, m, 0, m.Length)
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
