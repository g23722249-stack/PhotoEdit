Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors

''' <summary>
''' 宮崎風 AI 重繪：Stable Diffusion 1.5 img2img＋ControlNet（ONNX、DirectML）。
''' 用法：PhotoEdit.Redraw job.json；輸出一行行 "PROGRESS 目前 總數"，完成 "DONE 檔案"，失敗 "ERROR 訊息"。
''' job.json：model（模型資料夾：text_encoder、tokenizer）、vae（VAE 的資料夾，預設同 model）、unet、controlnet、useControlNet（false＝不用線稿控制，殘差給 0）、input、hint、output、prompt、negative、strength、seed、steps、cfg、controlnetScale、controlnetEnd、shortSide。
''' </summary>
Module Program
    Function Main(args As String()) As Integer
        Try
            Dim job = JsonDocument.Parse(File.ReadAllText(args(0))).RootElement
            Run(job)
            Return 0
        Catch ex As Exception
            Console.WriteLine("ERROR " & ex.Message.Replace(vbCr, " ").Replace(vbLf, " "))
            Return 1
        End Try
    End Function

    Private Function Str(j As JsonElement, name As String) As String
        Dim v As JsonElement
        Return If(j.TryGetProperty(name, v), v.GetString(), "")
    End Function
    Private Function Num(j As JsonElement, name As String, def As Double) As Double
        Dim v As JsonElement
        Return If(j.TryGetProperty(name, v), v.GetDouble(), def)
    End Function

    Private Sub Run(job As JsonElement)
        Dim dir = Str(job, "model")
        Dim strength = Num(job, "strength", 0.45), seed = CInt(Num(job, "seed", 11)), steps = CInt(Num(job, "steps", 26))
        Dim guidance = Num(job, "cfg", 6), cnScale = CSng(Num(job, "controlnetScale", 0.78)), cnEnd = Num(job, "controlnetEnd", 1.0)
        Dim shortSide = CInt(Num(job, "shortSide", 512))
        Dim vaeDir = If(String.IsNullOrEmpty(Str(job, "vae")), dir, Str(job, "vae"))
        Dim useCn As JsonElement, withCn = Not job.TryGetProperty("useControlNet", useCn) OrElse useCn.GetBoolean()

        Dim so As New SessionOptions With {.EnableMemoryPattern = False, .ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                                           .GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                                           .LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR}
        Try
            so.AppendExecutionProvider_DML(0)
        Catch
            ' 沒有 DirectML（沒有顯卡）就用 CPU，很慢但能動
        End Try
        Dim load = Function(p As String) New InferenceSession(p, so)

        Dim tok As New ClipTokenizer(Path.Combine(dir, "tokenizer", "vocab.json"), Path.Combine(dir, "tokenizer", "merges.txt"))
        Dim condEmb, uncondEmb As Single()
        Using te = load(Path.Combine(dir, "text_encoder", "model.onnx"))
            condEmb = Encode(te, tok.Encode(Str(job, "prompt")))
            uncondEmb = Encode(te, tok.Encode(Str(job, "negative")))
        End Using

        ' 尺寸：短邊 shortSide、都是 64 的倍數（UNet 縮三次再放大要對得上）
        Dim W, H As Integer
        Dim img As Single(), hint As Single()
        Using src0 As New Bitmap(Str(job, "input"))
            Dim k = shortSide / CDbl(Math.Min(src0.Width, src0.Height))
            W = Math.Max(64, CInt(Math.Round(src0.Width * k / 64)) * 64)
            H = Math.Max(64, CInt(Math.Round(src0.Height * k / 64)) * 64)
            img = ToTensor(src0, W, H, True)
        End Using
        Using h0 As New Bitmap(Str(job, "hint"))
            hint = ToTensor(h0, W, H, False)
        End Using
        Dim lw = W \ 8, lh = H \ 8

        Dim initLatent As Single()
        Using ve = load(Path.Combine(vaeDir, "vae_encoder", "model.onnx"))
            initLatent = RunOne(ve, img, {1, 3, H, W})
            For i = 0 To initLatent.Length - 1
                initLatent(i) *= 0.18215F
            Next
        End Using

        Using unet = load(Str(job, "unet")), cn = If(withCn, load(Str(job, "controlnet")), Nothing), vd = load(Path.Combine(vaeDir, "vae_decoder", "model.onnx"))
            Dim sched As New Ddim()
            Dim ts = sched.Timesteps(steps, strength)
            Dim rnd As New Random(seed)
            Dim lat(initLatent.Length - 1) As Single
            Dim a0 = sched.Acp(ts(0))
            For i = 0 To lat.Length - 1
                lat(i) = CSng(Math.Sqrt(a0) * initLatent(i) + Math.Sqrt(1 - a0) * Gauss(rnd))
            Next
            Dim zeros As List(Of (Data As Single(), Dims As Integer())) = Nothing
            Console.WriteLine($"PROGRESS 0 {ts.Length + 1}")
            For si = 0 To ts.Length - 1
                Dim t = ts(si)
                Dim cnOn = cn IsNot Nothing AndAlso si < Math.Ceiling(ts.Length * cnEnd)
                If cn Is Nothing AndAlso zeros Is Nothing Then zeros = ZeroResiduals(lh, lw)
                Dim resU = If(cnOn, CnRun(cn, lat, t, uncondEmb, hint, lh, lw, H, W, cnScale), zeros)
                Dim resC = If(cnOn, CnRun(cn, lat, t, condEmb, hint, lh, lw, H, W, cnScale), zeros)
                If cnOn AndAlso zeros Is Nothing Then zeros = resU.Select(Function(r) (New Single(r.Data.Length - 1) {}, r.Dims)).ToList()
                Dim epsU = UnetRun(unet, lat, t, uncondEmb, lh, lw, resU)
                Dim epsC = UnetRun(unet, lat, t, condEmb, lh, lw, resC)
                Dim eps(lat.Length - 1) As Single
                For i = 0 To eps.Length - 1
                    eps(i) = CSng(epsU(i) + guidance * (epsC(i) - epsU(i)))
                Next
                lat = sched.Stepped(lat, eps, t, steps)
                Console.WriteLine($"PROGRESS {si + 1} {ts.Length + 1}")
            Next
            For i = 0 To lat.Length - 1
                lat(i) /= 0.18215F
            Next
            Dim outImg = RunOne(vd, lat, {1, 4, lh, lw})
            Using bmp As New Bitmap(W, H, PixelFormat.Format24bppRgb)
                Dim data = bmp.LockBits(New Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb)
                Dim buf(data.Stride * H - 1) As Byte
                For y = 0 To H - 1
                    For x = 0 To W - 1
                        Dim p = y * data.Stride + x * 3
                        buf(p + 2) = ToByte(outImg((0 * H + y) * W + x)) : buf(p + 1) = ToByte(outImg((1 * H + y) * W + x)) : buf(p) = ToByte(outImg((2 * H + y) * W + x))
                    Next
                Next
                Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length)
                bmp.UnlockBits(data)
                bmp.Save(Str(job, "output"), ImageFormat.Png)
            End Using
            Console.WriteLine($"PROGRESS {ts.Length + 1} {ts.Length + 1}")
            Console.WriteLine("DONE " & Str(job, "output"))
        End Using
    End Sub

    Private Function ToByte(v As Single) As Byte
        Return CByte(Math.Max(0, Math.Min(255, Math.Round((v + 1) * 127.5))))
    End Function

    ''' <summary>圖片縮到 W×H，轉成 [1,3,H,W]：照片 -1..1，線稿 0..1。</summary>
    Private Function ToTensor(src As Bitmap, W As Integer, H As Integer, signed As Boolean) As Single()
        Using bmp As New Bitmap(W, H, PixelFormat.Format24bppRgb)
            Using g = Graphics.FromImage(bmp)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.DrawImage(src, 0, 0, W, H)
            End Using
            Dim data = bmp.LockBits(New Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb)
            Dim buf(data.Stride * H - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length)
            bmp.UnlockBits(data)
            Dim a(3 * H * W - 1) As Single
            For y = 0 To H - 1
                For x = 0 To W - 1
                    Dim p = y * data.Stride + x * 3
                    For c = 0 To 2
                        Dim v = buf(p + 2 - c) / 255.0F
                        a((c * H + y) * W + x) = If(signed, v * 2 - 1, v)
                    Next
                Next
            Next
            Return a
        End Using
    End Function

    Private Function Gauss(r As Random) As Double
        Dim u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble()
        Return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2)
    End Function

    ''' <summary>依模型的型別建輸入（float 或 float16）。</summary>
    Private Function Tensor(s As InferenceSession, name As String, data As Single(), dims As Integer()) As NamedOnnxValue
        If s.InputMetadata(name).ElementDataType = TensorElementType.Float16 Then
            Return NamedOnnxValue.CreateFromTensor(name, New DenseTensor(Of Float16)(data.Select(Function(v) CType(v, Float16)).ToArray(), dims))
        End If
        Return NamedOnnxValue.CreateFromTensor(name, New DenseTensor(Of Single)(data, dims))
    End Function

    Private Function Output(v As DisposableNamedOnnxValue) As Single()
        Dim h = TryCast(v.Value, DenseTensor(Of Float16))
        If h IsNot Nothing Then Return h.ToArray().Select(Function(x) CType(x, Single)).ToArray()
        Return v.AsTensor(Of Single)().ToArray()
    End Function

    Private Function Dims(v As DisposableNamedOnnxValue) As Integer()
        Dim h = TryCast(v.Value, DenseTensor(Of Float16))
        Return If(h IsNot Nothing, h.Dimensions.ToArray(), v.AsTensor(Of Single)().Dimensions.ToArray())
    End Function

    Private Function RunOne(s As InferenceSession, data As Single(), d As Integer()) As Single()
        Dim name = s.InputMetadata.Keys.First()
        Using r = s.Run({Tensor(s, name, data, d)})
            Return Output(r.First())
        End Using
    End Function

    Private Function Encode(te As InferenceSession, ids As Integer()) As Single()
        Dim name = te.InputMetadata.Keys.First()
        Dim inp = If(te.InputMetadata(name).ElementDataType = TensorElementType.Int64,
                     NamedOnnxValue.CreateFromTensor(name, New DenseTensor(Of Long)(ids.Select(Function(i) CLng(i)).ToArray(), {1, 77})),
                     NamedOnnxValue.CreateFromTensor(name, New DenseTensor(Of Integer)(ids, {1, 77})))
        Using r = te.Run({inp})
            Return Output(r.First()) ' last_hidden_state [1,77,768]
        End Using
    End Function

    Private Function Timestep(s As InferenceSession, n As String, t As Integer) As NamedOnnxValue
        Select Case s.InputMetadata(n).ElementDataType
            Case TensorElementType.Int64 : Return NamedOnnxValue.CreateFromTensor(n, New DenseTensor(Of Long)(New Long() {t}, {1}))
            Case TensorElementType.Int32 : Return NamedOnnxValue.CreateFromTensor(n, New DenseTensor(Of Integer)(New Integer() {t}, {1}))
            Case Else : Return Tensor(s, n, New Single() {t}, {1})
        End Select
    End Function

    ''' <summary>加過 ControlNet 輸入的 UNet：down_block_res_0..11、mid_block_res。</summary>
    Private Function UnetRun(unet As InferenceSession, lat As Single(), t As Integer, emb As Single(), lh As Integer, lw As Integer,
                             res As List(Of (Data As Single(), Dims As Integer()))) As Single()
        Dim inputs As New List(Of NamedOnnxValue)()
        For Each n In unet.InputMetadata.Keys
            If n.StartsWith("down_block_res_") Then
                Dim r0 = res(Integer.Parse(n.Substring(15)))
                inputs.Add(Tensor(unet, n, r0.Data, r0.Dims))
            ElseIf n = "mid_block_res" Then
                inputs.Add(Tensor(unet, n, res(12).Data, res(12).Dims))
            ElseIf n.Contains("sample") Then
                inputs.Add(Tensor(unet, n, lat, {1, 4, lh, lw}))
            ElseIf n.Contains("hidden") Then
                inputs.Add(Tensor(unet, n, emb, {1, 77, 768}))
            ElseIf n.Contains("timestep") Then
                inputs.Add(Timestep(unet, n, t))
            End If
        Next
        Using r = unet.Run(inputs)
            Return Output(r.First())
        End Using
    End Function

    ''' <summary>ControlNet：12 個下行殘差＋1 個中間殘差（輸出名稱不可靠，照順序：前 12 個下行、最後中間）。</summary>
    Private Function CnRun(cn As InferenceSession, lat As Single(), t As Integer, emb As Single(), hint As Single(),
                           lh As Integer, lw As Integer, H As Integer, W As Integer, scale As Single) As List(Of (Data As Single(), Dims As Integer()))
        Dim inputs As New List(Of NamedOnnxValue)()
        For Each kv In cn.InputMetadata
            Dim n = kv.Key
            If n.Contains("cond") Then
                inputs.Add(Tensor(cn, n, hint, {1, 3, H, W}))
            ElseIf n.Contains("scale") Then
                inputs.Add(Tensor(cn, n, {scale}, If(kv.Value.Dimensions.Length = 0, Array.Empty(Of Integer)(), {1})))
            ElseIf n.Contains("sample") Then
                inputs.Add(Tensor(cn, n, lat, {1, 4, lh, lw}))
            ElseIf n.Contains("hidden") Then
                inputs.Add(Tensor(cn, n, emb, {1, 77, 768}))
            ElseIf n.Contains("timestep") Then
                inputs.Add(Timestep(cn, n, t))
            End If
        Next
        Dim hasScale = cn.InputMetadata.Keys.Any(Function(k) k.Contains("scale"))
        Dim list As New List(Of (Data As Single(), Dims As Integer()))()
        Using r = cn.Run(inputs)
            For Each v In r
                Dim data = Output(v)
                If Not hasScale AndAlso scale <> 1.0F Then
                    For i = 0 To data.Length - 1
                        data(i) *= scale
                    Next
                End If
                list.Add((data, Dims(v)))
            Next
        End Using
        If list.Count <> 13 Then Throw New InvalidOperationException($"ControlNet 輸出數量不對：{list.Count}")
        Return list
    End Function

    ''' <summary>不用線稿控制時給 UNet 的 13 個全 0 殘差（SD 1.5 各層的通道數與縮小倍數）。</summary>
    Private Function ZeroResiduals(lh As Integer, lw As Integer) As List(Of (Data As Single(), Dims As Integer()))
        Dim spec = {(320, 1), (320, 1), (320, 1), (320, 2), (640, 2), (640, 2), (640, 4), (1280, 4), (1280, 4), (1280, 8), (1280, 8), (1280, 8), (1280, 8)}
        Return spec.Select(Function(s)
                               Dim h = lh \ s.Item2, w = lw \ s.Item2
                               Return (New Single(s.Item1 * h * w - 1) {}, New Integer() {1, s.Item1, h, w})
                           End Function).ToList()
    End Function
End Module

''' <summary>DDIM（eta 0）：SD1.5 的 scaled_linear β 0.00085～0.012、1000 步、steps_offset 1。</summary>
Friend NotInheritable Class Ddim
    Public ReadOnly Acp(999) As Double
    Public Sub New()
        Dim prod = 1.0
        For i = 0 To 999
            Dim b = Math.Pow(Math.Sqrt(0.00085) + (Math.Sqrt(0.012) - Math.Sqrt(0.00085)) * i / 999.0, 2)
            prod *= 1 - b
            Acp(i) = prod
        Next
    End Sub
    Public Function Timesteps(steps As Integer, strength As Double) As Integer()
        Dim ratio = 1000 \ steps
        Dim all = Enumerable.Range(0, steps).Select(Function(i) i * ratio + 1).Reverse().ToArray()
        Dim init = Math.Max(1, Math.Min(CInt(Math.Floor(steps * strength)), steps))
        Return all.Skip(steps - init).ToArray()
    End Function
    Public Function Stepped(x As Single(), eps As Single(), t As Integer, steps As Integer) As Single()
        Dim prev = t - 1000 \ steps
        Dim at = Acp(t), ap = If(prev >= 0, Acp(prev), Acp(0))
        Dim r(x.Length - 1) As Single
        For i = 0 To x.Length - 1
            Dim x0 = (x(i) - Math.Sqrt(1 - at) * eps(i)) / Math.Sqrt(at)
            r(i) = CSng(Math.Sqrt(ap) * x0 + Math.Sqrt(1 - ap) * eps(i))
        Next
        Return r
    End Function
End Class

''' <summary>CLIP 的 BPE 分詞（vocab.json＋merges.txt），補到 77 個（用 &lt;|endoftext|&gt;）。</summary>
Friend NotInheritable Class ClipTokenizer
    Private ReadOnly _vocab As Dictionary(Of String, Integer)
    Private ReadOnly _ranks As New Dictionary(Of (String, String), Integer)()
    Private ReadOnly _b2u As New Dictionary(Of Byte, Char)()
    Private Shared ReadOnly Pat As New Regex("<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+", RegexOptions.IgnoreCase)

    Public Sub New(vocabPath As String, mergesPath As String)
        _vocab = JsonSerializer.Deserialize(Of Dictionary(Of String, Integer))(File.ReadAllText(vocabPath))
        Dim lines = File.ReadAllLines(mergesPath).Skip(1).Where(Function(l) l.Length > 0).ToArray()
        For i = 0 To lines.Length - 1
            Dim p = lines(i).Split(" "c)
            If p.Length = 2 Then _ranks((p(0), p(1))) = i
        Next
        ' GPT-2 的位元組 → 可見字元對照
        Dim bs = Enumerable.Range(AscW("!"c), AscW("~"c) - AscW("!"c) + 1).Concat(Enumerable.Range(&HA1, &HAC - &HA1 + 1)).Concat(Enumerable.Range(&HAE, &HFF - &HAE + 1)).ToList()
        Dim cs = bs.ToList()
        Dim n = 0
        For b = 0 To 255
            If Not bs.Contains(b) Then bs.Add(b) : cs.Add(256 + n) : n += 1
        Next
        For i = 0 To bs.Count - 1
            _b2u(CByte(bs(i))) = ChrW(cs(i))
        Next
    End Sub

    Private Function Bpe(token As String) As List(Of String)
        Dim word = token.Select(Function(c) c.ToString()).ToList()
        word(word.Count - 1) &= "</w>"
        Do While word.Count > 1
            Dim best = Integer.MaxValue, bi = -1
            For i = 0 To word.Count - 2
                Dim r As Integer
                If _ranks.TryGetValue((word(i), word(i + 1)), r) AndAlso r < best Then best = r : bi = i
            Next
            If bi < 0 Then Exit Do
            Dim a = word(bi), b2 = word(bi + 1)
            Dim merged As New List(Of String)()
            Dim j = 0
            Do While j < word.Count
                If j < word.Count - 1 AndAlso word(j) = a AndAlso word(j + 1) = b2 Then
                    merged.Add(a & b2) : j += 2
                Else
                    merged.Add(word(j)) : j += 1
                End If
            Loop
            word = merged
        Loop
        Return word
    End Function

    Public Function Encode(text As String) As Integer()
        Dim ids As New List(Of Integer) From {49406}
        text = Regex.Replace(text.Trim(), "\s+", " ").ToLowerInvariant()
        For Each m As Match In Pat.Matches(text)
            Dim u = New String(Encoding.UTF8.GetBytes(m.Value).Select(Function(b) _b2u(b)).ToArray())
            For Each piece In Bpe(u)
                Dim id As Integer
                If _vocab.TryGetValue(piece, id) Then ids.Add(id)
            Next
        Next
        If ids.Count > 76 Then ids = ids.Take(76).ToList()
        ids.Add(49407)
        Do While ids.Count < 77
            ids.Add(49407)
        Loop
        Return ids.ToArray()
    End Function
End Class
