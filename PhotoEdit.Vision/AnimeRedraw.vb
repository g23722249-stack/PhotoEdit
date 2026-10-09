Imports System.Drawing
Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>AI 重繪的一個模型（AnimeRedrawPresets.json 的 models）。</summary>
Public NotInheritable Class AnimeRedrawModel
    Public Property Id As String
    Public Property Name As String
    Public Property Note As String
    ''' <summary>Models\ 底下的資料夾（text_encoder、tokenizer、unet_controlnet）。</summary>
    Public Property Dir As String
    ''' <summary>VAE 所在的資料夾（Anything 共用 GhibliDiffusion 的）。</summary>
    Public Property VaeDir As String
End Class

''' <summary>一組宮崎風 AI 重繪的風格（AnimeRedrawPresets.json 的 presets）。</summary>
Public NotInheritable Class AnimeRedrawPreset
    Public Property Model As String
    Public Property Name As String
    Public Property Strength As Double
    Public Property Seed As Integer
    Public Property Note As String
    ''' <summary>這組風格在 JSON 裡的原始內容（覆寫 common 的欄位用）。</summary>
    Friend Property Raw As JsonElement
End Class

''' <summary>
''' 宮崎風 AI 重繪（本機顯卡）：
''' (1) 打底：磨皮、勻膚、暖色、去黑眼圈、眼睛略大；(2) 線稿：臉裡面用 478 點網格畫吉卜力式的簡化五官，臉外面用照片的 Canny；
''' (3) 交給 Redraw\PhotoEdit.Redraw.exe（Stable Diffusion＋ControlNet，DirectML）重畫；(4) 用打底照的網格把虹膜改深棕、膚色略白皙。
''' 模型在 Models\GhibliDiffusion、Models\AnimeAnything（共約 4.5 GB，不放進 git，另外下載）。
''' </summary>
Public NotInheritable Class AnimeRedraw
    Private Sub New()
    End Sub

    Public Shared ReadOnly Property ModelsRoot As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "Models")
        End Get
    End Property

    Public Shared ReadOnly Property HelperPath As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "Redraw", "PhotoEdit.Redraw.exe")
        End Get
    End Property

    ''' <summary>這個模型（與它要的生成程式、VAE、ControlNet）都在才能用；不能用時回傳原因。</summary>
    Public Shared Function Availability(model As AnimeRedrawModel) As String
        If Not File.Exists(HelperPath) Then Return "找不到生成程式（Redraw\PhotoEdit.Redraw.exe）。"
        Dim need = {Path.Combine(model.Dir, Common("unet").GetString()), Path.Combine(model.Dir, "text_encoder", "model.onnx"),
                    Path.Combine(model.Dir, "tokenizer", "vocab.json"), Path.Combine(model.VaeDir, "vae_encoder", "model.onnx"),
                    Path.Combine(model.VaeDir, "vae_decoder", "model.onnx"), Common("controlnet").GetString()}
        For Each f In need
            If Not File.Exists(Path.Combine(ModelsRoot, f)) Then Return $"找不到模型 Models\{f.Replace("/"c, "\"c)}。"
        Next
        Return Nothing
    End Function

    '---------------------------------------------------------------------
    ' 模型、風格與提示詞
    '---------------------------------------------------------------------

    Private Shared _settings As JsonElement?

    Private Shared Function Settings() As JsonElement
        If _settings Is Nothing Then
            Dim p = Path.Combine(AppContext.BaseDirectory, "AnimeRedrawPresets.json")
            _settings = JsonDocument.Parse(File.ReadAllText(p)).RootElement.Clone()
        End If
        Return _settings.Value
    End Function

    Public Shared Function Models() As IReadOnlyList(Of AnimeRedrawModel)
        Return Settings().GetProperty("models").EnumerateArray().Select(
            Function(e) New AnimeRedrawModel With {.Id = e.GetProperty("id").GetString(), .Name = e.GetProperty("name").GetString(),
                                                  .Note = e.GetProperty("note").GetString(), .Dir = e.GetProperty("dir").GetString(),
                                                  .VaeDir = e.GetProperty("vaeDir").GetString()}).ToList()
    End Function

    Public Shared Function Presets() As IReadOnlyList(Of AnimeRedrawPreset)
        Return Settings().GetProperty("presets").EnumerateArray().Select(
            Function(e) New AnimeRedrawPreset With {.Model = e.GetProperty("model").GetString(), .Name = e.GetProperty("name").GetString(),
                                                   .Strength = e.GetProperty("strength").GetDouble(), .Seed = e.GetProperty("seed").GetInt32(),
                                                   .Note = e.GetProperty("note").GetString(), .Raw = e.Clone()}).ToList()
    End Function

    Private Shared Function Common(name As String) As JsonElement
        Return Settings().GetProperty("common").GetProperty(name)
    End Function

    ''' <summary>風格有寫就用風格的，沒有就用 common 的。</summary>
    Private Shared Function Setting(p As AnimeRedrawPreset, name As String) As JsonElement
        Dim v As JsonElement
        If p IsNot Nothing AndAlso p.Raw.ValueKind = JsonValueKind.Object AndAlso p.Raw.TryGetProperty(name, v) Then Return v
        Return Common(name)
    End Function

    '---------------------------------------------------------------------
    ' 打底與線稿
    '---------------------------------------------------------------------

    Private Shared ReadOnly Oval As Integer() = {10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109}
    Private Shared ReadOnly Jaw As Integer() = {454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234}
    Private Shared ReadOnly UpperR As Integer() = {33, 246, 161, 160, 159, 158, 157, 173, 133}
    Private Shared ReadOnly LowerR As Integer() = {33, 7, 163, 144, 145, 153, 154, 155, 133}
    Private Shared ReadOnly UpperL As Integer() = {263, 466, 388, 387, 386, 385, 384, 398, 362}
    Private Shared ReadOnly LowerL As Integer() = {263, 249, 390, 373, 374, 380, 381, 382, 362}
    Private Shared ReadOnly BrowUp As Integer()() = {New Integer() {70, 63, 105, 66, 107}, New Integer() {300, 293, 334, 296, 336}}
    Private Shared ReadOnly BrowLo As Integer()() = {New Integer() {46, 53, 52, 65, 55}, New Integer() {276, 283, 282, 295, 285}}
    Private Shared ReadOnly MouthLine As Integer() = {78, 191, 80, 81, 82, 13, 312, 311, 310, 415, 308}
    Private Shared ReadOnly NoseBase As Integer() = {97, 2, 326}

    ''' <summary>打底後的照片、線稿、打底照上找到的臉（之後修虹膜用，0..1 座標）。</summary>
    Public Shared Function Prepare(source As Bitmap) As (Base As Bitmap, Hint As Bitmap, Faces As IReadOnlyList(Of FaceRegion))
        Dim faces = New FaceDetector().Detect(source)
        FaceMesh.Fit(source, faces)
        Dim b = Common("prep").GetProperty("beauty")
        Dim beauty As New BeautySettings()
        For Each p In b.EnumerateObject()
            GetType(BeautySettings).GetProperty(p.Name)?.SetValue(beauty, p.Value.GetInt32())
        Next
        Dim r As New EditRecipe()
        r.SetGlobalBeauty(beauty)
        Dim pre = If(faces.Count > 0, PortraitRetouch.Apply(source, faces, r), Nothing)
        If pre Is Nothing Then pre = CType(source.Clone(), Bitmap)
        ' 網格用打底後的位置（眼睛放大過）
        Dim faces2 = New FaceDetector().Detect(pre)
        FaceMesh.Fit(pre, faces2)
        Return (pre, DrawHint(pre, faces2), faces2)
    End Function

    ''' <summary>線稿（白線黑底）：臉外面 Canny，臉裡面清空後畫簡化五官（比例見 AnimeRedrawPresets.json 的 prep.hint）。</summary>
    Private Shared Function DrawHint(pre As Bitmap, faces As IReadOnlyList(Of FaceRegion)) As Bitmap
        Dim w = pre.Width, h = pre.Height
        Using mat = BitmapConverter.ToMat(pre), gray As New Mat(), edges As New Mat()
            Cv2.CvtColor(mat, gray, If(mat.Channels() = 4, ColorConversionCodes.BGRA2GRAY, ColorConversionCodes.BGR2GRAY))
            Cv2.GaussianBlur(gray, gray, New OpenCvSharp.Size(0, 0), 1.2)
            Cv2.Canny(gray, edges, 70, 160)
            For Each f In faces
                Dim m = f.Mesh
                If m Is Nothing OrElse m.Length < 468 Then Continue For
                Dim P = Function(i As Integer) New OpenCvSharp.Point(CInt(m(i).X * w), CInt(m(i).Y * h))
                Dim d = Math.Sqrt(((m(33).X - m(263).X) * w) ^ 2 + ((m(33).Y - m(263).Y) * h) ^ 2)
                Dim t = Math.Max(1, CInt(d * 0.012))
                Dim cx = Oval.Average(Function(i) m(i).X) * w, cy = Oval.Average(Function(i) m(i).Y) * h
                ' 臉裡面清空（往內縮 8%，外輪廓另外畫）
                Using inner As New Mat(h, w, MatType.CV_8UC1, Scalar.All(0))
                    Dim shrunk = Oval.Select(Function(i) New OpenCvSharp.Point(CInt(cx + (m(i).X * w - cx) * 0.92), CInt(cy + (m(i).Y * h - cy) * 0.92))).ToArray()
                    Cv2.FillPoly(inner, {shrunk}, Scalar.All(255))
                    edges.SetTo(Scalar.All(0), inner)
                End Using
                Dim lineS = Sub(idx As Integer(), th As Integer, ccx As Double, ccy As Double, sx As Double, sy As Double)
                                Dim pts = idx.Select(Function(i) New OpenCvSharp.Point(CInt(ccx + (m(i).X * w - ccx) * sx), CInt(ccy + (m(i).Y * h - ccy) * sy))).ToArray()
                                Cv2.Polylines(edges, {pts}, False, Scalar.All(255), th, LineTypes.AntiAlias)
                            End Sub
                ' 下顎：越靠下巴越往中間收 8%
                Dim jawPts = Jaw.Select(Function(i, j)
                                            Dim k = 1 - Math.Abs(j - 10) / 10.0
                                            Return New OpenCvSharp.Point(CInt(cx + (m(i).X * w - cx) * (1 - 0.08 * k)), CInt(m(i).Y * h))
                                        End Function).ToArray()
                Cv2.Polylines(edges, {jawPts}, False, Scalar.All(255), t, LineTypes.AntiAlias)
                ' 眉：上下緣的中線
                For k = 0 To 1
                    Dim kk = k
                    Dim mid = Enumerable.Range(0, 5).Select(Function(j) New OpenCvSharp.Point(CInt((m(BrowUp(kk)(j)).X + m(BrowLo(kk)(j)).X) / 2 * w),
                                                                                            CInt((m(BrowUp(kk)(j)).Y + m(BrowLo(kk)(j)).Y) / 2 * h))).ToArray()
                    Cv2.Polylines(edges, {mid}, False, Scalar.All(255), t, LineTypes.AntiAlias)
                Next
                ' 眼：以眼睛中心橫 ×1.08、縱 ×1.25；上眼皮粗、下眼皮中段細
                For Each pair In {(U:=UpperR, Lo:=LowerR), (U:=UpperL, Lo:=LowerL)}
                    Dim ecx = pair.U.Concat(pair.Lo).Average(Function(i) m(i).X) * w, ecy = pair.U.Concat(pair.Lo).Average(Function(i) m(i).Y) * h
                    lineS(pair.U, t * 2, ecx, ecy, 1.08, 1.25)
                    lineS(pair.Lo.Skip(2).Take(5).ToArray(), Math.Max(1, t \ 2), ecx, ecy, 1.08, 1.25)
                Next
                ' 虹膜：半徑 ×1.18 的圓，瞳孔實心
                If m.Length >= 478 Then
                    For Each c In {468, 473}
                        Dim rr = Math.Sqrt(((m(c + 1).X - m(c + 3).X) * w) ^ 2 + ((m(c + 1).Y - m(c + 3).Y) * h) ^ 2) / 2 * 1.18
                        Cv2.Circle(edges, P(c), CInt(rr), Scalar.All(255), t, LineTypes.AntiAlias)
                        Cv2.Circle(edges, P(c), Math.Max(1, CInt(rr * 0.45)), Scalar.All(255), -1, LineTypes.AntiAlias)
                    Next
                End If
                ' 鼻：只有鼻底一小段（橫 ×0.75）；嘴：內唇上緣一條（橫 ×0.95）
                lineS(NoseBase, t, m(2).X * w, m(2).Y * h, 0.75, 1.0)
                lineS(MouthLine, t, m(13).X * w, m(13).Y * h, 0.95, 1.0)
            Next
            Using rgb As New Mat()
                Cv2.CvtColor(edges, rgb, ColorConversionCodes.GRAY2BGR)
                Return BitmapConverter.ToBitmap(rgb)
            End Using
        End Using
    End Function

    ''' <summary>重畫完的修正：虹膜改深棕、膚色略白皙（用打底照的網格，位置相同）。</summary>
    Public Shared Function PostFix(img As Bitmap, faces As IReadOnlyList(Of FaceRegion)) As Bitmap
        If faces Is Nothing OrElse faces.Count = 0 Then Return CType(img.Clone(), Bitmap)
        Dim b As New BeautySettings With {.Iris = 85, .IrisStyle = IrisStyle.Natural, .IrisColorArgb = Color.FromArgb(78, 50, 34).ToArgb(), .Tone = -18, .Whiten = 12}
        Dim r As New EditRecipe()
        r.SetGlobalBeauty(b)
        Return If(PortraitRetouch.Apply(img, faces, r), CType(img.Clone(), Bitmap))
    End Function

    '---------------------------------------------------------------------
    ' 生成（外部程式）
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 打底 → 線稿 → 顯卡重畫 → 修正，回傳結果（和 source 同尺寸比例，短邊 512 放大回去）。
    ''' progress(目前, 總數)；subject＝畫面內容的英文描述（帽子、衣服、背景），可空白。
    ''' </summary>
    Public Shared Function Generate(source As Bitmap, preset As AnimeRedrawPreset, strength As Double, seed As Integer, subject As String,
                                    progress As Action(Of Integer, Integer), ct As CancellationToken) As Bitmap
        Dim model = Models().First(Function(m) m.Id = preset.Model)
        Dim why = Availability(model)
        If why IsNot Nothing Then Throw New InvalidOperationException(why)
        Dim work = Path.Combine(Path.GetTempPath(), "PhotoEdit", "redraw")
        Directory.CreateDirectory(work)
        Dim stamp = DateTime.Now.ToString("HHmmssfff")
        Dim baseFile = Path.Combine(work, $"base_{stamp}.png"), hintFile = Path.Combine(work, $"hint_{stamp}.png")
        Dim outFile = Path.Combine(work, $"out_{stamp}.png"), jobFile = Path.Combine(work, $"job_{stamp}.json")
        ' 處理尺寸：長邊 1024 夠用（生成是短邊 512）
        Dim k = Math.Min(1.0, 1024.0 / Math.Max(source.Width, source.Height))
        Dim prepFaces As IReadOnlyList(Of FaceRegion)
        Using small As New Bitmap(source, Math.Max(1, CInt(source.Width * k)), Math.Max(1, CInt(source.Height * k)))
            Dim prep = Prepare(small)
            prepFaces = prep.Faces
            prep.Base.Save(baseFile, Imaging.ImageFormat.Png)
            prep.Hint.Save(hintFile, Imaging.ImageFormat.Png)
            prep.Base.Dispose() : prep.Hint.Dispose()
        End Using
        ct.ThrowIfCancellationRequested()
        Dim defSubject = Setting(preset, "defaultSubject").GetString()
        Dim subj = If(String.IsNullOrWhiteSpace(subject), defSubject, subject.Trim())
        Dim prompt = Setting(preset, "prompt").GetString().Replace("{subject}", subj).Trim().TrimEnd(","c).Trim()
        Dim root = ModelsRoot
        Dim job As New Dictionary(Of String, Object) From {
            {"model", Path.Combine(root, model.Dir)}, {"vae", Path.Combine(root, model.VaeDir)},
            {"unet", Path.Combine(root, model.Dir, Setting(preset, "unet").GetString())},
            {"controlnet", Path.Combine(root, Setting(preset, "controlnet").GetString())},
            {"useControlNet", Setting(preset, "useControlNet").GetBoolean()},
            {"input", baseFile}, {"hint", hintFile}, {"output", outFile},
            {"prompt", prompt}, {"negative", Setting(preset, "negative").GetString()},
            {"strength", strength}, {"seed", seed}, {"steps", Setting(preset, "steps").GetInt32()}, {"cfg", Setting(preset, "cfg").GetDouble()},
            {"controlnetScale", Setting(preset, "controlnetScale").GetDouble()}, {"controlnetEnd", Setting(preset, "controlnetEnd").GetDouble()},
            {"shortSide", Setting(preset, "shortSide").GetInt32()}}
        File.WriteAllText(jobFile, JsonSerializer.Serialize(job))
        Dim err As String = Nothing
        Using p As New Process()
            p.StartInfo = New ProcessStartInfo(HelperPath, $"""{jobFile}""") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True,
                .WorkingDirectory = Path.GetDirectoryName(HelperPath)}
            AddHandler p.OutputDataReceived, Sub(s, e)
                                                 If e.Data Is Nothing Then Return
                                                 If e.Data.StartsWith("PROGRESS ") Then
                                                     Dim parts = e.Data.Split(" "c)
                                                     progress?.Invoke(Integer.Parse(parts(1)), Integer.Parse(parts(2)))
                                                 ElseIf e.Data.StartsWith("ERROR ") Then
                                                     err = e.Data.Substring(6)
                                                 End If
                                             End Sub
            AddHandler p.ErrorDataReceived, Sub(s, e)
                                            End Sub ' ONNX Runtime 的警告不用管
            p.Start()
            p.BeginOutputReadLine()
            p.BeginErrorReadLine()
            Do Until p.WaitForExit(200)
                If ct.IsCancellationRequested Then
                    Try
                        p.Kill(True)
                    Catch
                    End Try
                    ct.ThrowIfCancellationRequested()
                End If
            Loop
            p.WaitForExit()
            If p.ExitCode <> 0 OrElse Not File.Exists(outFile) Then Throw New InvalidOperationException(If(err, $"生成失敗（代碼 {p.ExitCode}）。"))
        End Using
        Try
            Using gen As New Bitmap(outFile)
                Using fixedImg = PostFix(gen, prepFaces)
                    ' 放大回原本的比例尺寸（生成時取 64 的倍數，比例可能差一點點）
                    Dim result As New Bitmap(source.Width, source.Height, Imaging.PixelFormat.Format32bppArgb)
                    Using g = Graphics.FromImage(result)
                        g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                        g.PixelOffsetMode = Drawing2D.PixelOffsetMode.Half
                        g.DrawImage(fixedImg, 0, 0, source.Width, source.Height)
                    End Using
                    Return result
                End Using
            End Using
        Finally
            For Each f In {baseFile, hintFile, outFile, jobFile}
                Try
                    File.Delete(f)
                Catch
                End Try
            Next
        End Try
    End Function
End Class
