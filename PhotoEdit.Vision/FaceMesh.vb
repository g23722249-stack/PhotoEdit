Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>
''' 478 點臉部網格（Google MediaPipe Face Mesh V2，Models\face_landmarks.onnx，Apache-2.0）：468 個臉部點＋兩眼各 5 個虹膜點。
''' 輸入：依 YuNet 兩眼轉正、以臉為中心的正方形區域，縮成 256×256 RGB（0..1）；輸出 478×(x,y,z)（256 座標）與「有沒有臉」分數。
''' 先用偵測框算一次，再用第一次的點重新框一次（比較準）。結果存在 FaceRegion.Mesh，
''' 並換算成 68 點（FaceRegion.Dense），原本用 68 點的效果自動變準。
''' </summary>
Public NotInheritable Class FaceMesh
    Private Sub New()
    End Sub

    Public Const ModelFile As String = "face_landmarks.onnx"
    Private Const InputSize As Integer = 256
    Private Shared ReadOnly _lock As New Object()
    Private Shared _session As InferenceSession
    Private Shared _loadFailed As Boolean
    ''' <summary>最後一次 RunOnce 的深度（原圖像素）；只在鎖裡用。</summary>
    Private Shared _lastZ As Single()

    ''' <summary>MediaPipe 網格點 → dlib 68 點（下顎 0–16、眉 17–26、鼻 27–35、眼 36–47、嘴唇 48–67）。</summary>
    Private Shared ReadOnly To68 As Integer() = {
        162, 234, 93, 58, 172, 136, 149, 148, 152, 377, 378, 365, 397, 288, 323, 454, 389,
        71, 63, 105, 66, 107, 336, 296, 334, 293, 301,
        168, 197, 5, 4, 75, 97, 2, 326, 305,
        33, 160, 158, 133, 153, 144, 362, 385, 387, 263, 373, 380,
        61, 39, 37, 0, 267, 269, 291, 405, 314, 17, 84, 181,
        78, 82, 13, 312, 308, 317, 14, 87}

    Public Shared ReadOnly Property DefaultModelPath As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "Models", ModelFile)
        End Get
    End Property

    Public Shared Function ModelAvailable() As Boolean
        Return File.Exists(DefaultModelPath)
    End Function

    Private Shared Function Session() As InferenceSession
        If _session IsNot Nothing OrElse _loadFailed Then Return _session
        If Not ModelAvailable() Then Return Nothing
        Try
            ' 從位元組載入：不受中文路徑影響
            _session = New InferenceSession(File.ReadAllBytes(DefaultModelPath),
                                            New SessionOptions With {.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL})
        Catch
            _loadFailed = True
        End Try
        Return _session
    End Function

    ''' <summary>替每張臉算 478 點（成功時填 Mesh 與換算的 Dense，並回傳 True）；沒有模型或失敗的臉不動。</summary>
    Public Shared Function Fit(image As Bitmap, faces As IList(Of FaceRegion)) As Boolean
        If faces Is Nothing OrElse faces.Count = 0 Then Return False
        Dim any = False
        SyncLock _lock
            Dim s = Session()
            If s Is Nothing Then Return False
            Using bgra = BitmapConverter.ToMat(image), rgb As New Mat()
                Cv2.CvtColor(bgra, rgb, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2RGB, ColorConversionCodes.BGR2RGB))
                Dim w = rgb.Cols, h = rgb.Rows
                For Each f In faces
                    Dim mesh = FitOne(s, rgb, f, w, h)
                    If mesh Is Nothing Then Continue For
                    f.Mesh = mesh.Select(Function(p) New PointF(p.X / w, p.Y / h)).ToArray()
                    f.MeshZ = _lastZ.Select(Function(z) z / w).ToArray()
                    f.Dense = To68.Select(Function(i) f.Mesh(i)).ToArray()
                    any = True
                Next
            End Using
        End SyncLock
        Return any
    End Function

    Private Shared Function FitOne(s As InferenceSession, rgb As Mat, f As FaceRegion, w As Integer, h As Integer) As Point2f()
        Dim yu = f.Landmarks.Select(Function(p) New Point2f(p.X * w, p.Y * h)).ToArray()
        Dim eyeDist = Dist(yu(0), yu(1))
        If eyeDist < 6 Then Return Nothing
        Dim angle = Math.Atan2(yu(1).Y - yu(0).Y, yu(1).X - yu(0).X)
        Dim bw = f.Box.Width * w, bh = f.Box.Height * h
        ' 第一次：偵測框中心、邊長 1.5 倍
        Dim center As New Point2f(f.Box.X * w + bw / 2, f.Box.Y * h + bh / 2)
        Dim side = Math.Max(bw, bh) * 1.5
        Dim pts = RunOnce(s, rgb, center, side, angle)
        If pts Is Nothing Then Return Nothing
        ' 第二次：角度改用網格自己的兩眼（偵測的五點有時會偏），框改用網格點的外接框
        Dim e1 = Avg(pts, {33, 133}), e2 = Avg(pts, {362, 263})
        Dim angle2 = Math.Atan2(e2.Y - e1.Y, e2.X - e1.X)
        Dim meshEye = Dist(e1, e2)
        If meshEye < 4 Then Return Nothing
        Dim cs = Math.Cos(-angle2), sn = Math.Sin(-angle2)
        Dim rx = pts.Take(468).Select(Function(p) p.X * cs - p.Y * sn).ToArray()
        Dim ry = pts.Take(468).Select(Function(p) p.X * sn + p.Y * cs).ToArray()
        Dim mx = (rx.Min() + rx.Max()) / 2, my = (ry.Min() + ry.Max()) / 2
        Dim center2 As New Point2f(CSng(mx * Math.Cos(angle2) - my * Math.Sin(angle2)), CSng(mx * Math.Sin(angle2) + my * Math.Cos(angle2)))
        Dim side2 = Math.Max(rx.Max() - rx.Min(), ry.Max() - ry.Min()) * 1.5
        Dim pts2 = RunOnce(s, rgb, center2, side2, angle2)
        If pts2 Is Nothing Then Return Nothing
        ' 檢查一：兩次結果要一致（對不準時重新框一次通常會跑掉很多）
        Dim drift = Enumerable.Range(0, 468).Average(Function(i) Dist(pts(i), pts2(i))) / meshEye
        If drift > 0.06 Then Return Nothing
        ' 檢查二：鼻子要在偵測到的臉附近（寬鬆：偵測的五點有時本身就偏，不拿它當準）
        If Dist(pts2(4), yu(2)) > eyeDist * 0.7 Then Return Nothing
        Return pts2
    End Function

    ''' <summary>以 center 為中心、邊長 side、轉 angle（弧度，讓兩眼水平）切出 256×256 跑一次模型；回傳原圖座標的 478 點，或 Nothing（沒有臉）。</summary>
    Private Shared Function RunOnce(s As InferenceSession, rgb As Mat, center As Point2f, side As Double, angle As Double) As Point2f()
        If side < 8 Then Return Nothing
        Dim k = InputSize / side
        Dim cs = Math.Cos(angle), sn = Math.Sin(angle)
        ' 原圖 → 輸入：先平移到中心、反轉 angle、縮放，再移到輸入中心
        Dim a = k * cs, b = k * sn
        Dim affine As New Mat(2, 3, MatType.CV_64FC1)
        affine.Set(0, 0, a) : affine.Set(0, 1, b) : affine.Set(0, 2, InputSize / 2.0 - a * center.X - b * center.Y)
        affine.Set(1, 0, -b) : affine.Set(1, 1, a) : affine.Set(1, 2, InputSize / 2.0 + b * center.X - a * center.Y)
        Using crop As New Mat(), affineOwner = affine
            Cv2.WarpAffine(rgb, crop, affine, New OpenCvSharp.Size(InputSize, InputSize), InterpolationFlags.Linear, BorderTypes.Replicate)
            Dim px(InputSize * InputSize * 3 - 1) As Byte
            Marshal.Copy(crop.Data, px, 0, px.Length)
            Dim input As New DenseTensor(Of Single)({1, InputSize, InputSize, 3})
            Dim buf = input.Buffer.Span
            For i = 0 To px.Length - 1
                buf(i) = px(i) / 255.0F
            Next
            Dim name = s.InputMetadata.Keys.First()
            Using results = s.Run({NamedOnnxValue.CreateFromTensor(name, input)})
                ' 輸出：478×(x,y,z) 的那個（另一個「有沒有臉」的分數在這個轉換版本不可靠，不用；改用 FitOne 的兩次一致檢查）
                Dim coords As Single() = Nothing
                For Each r In results
                    Dim t = r.AsTensor(Of Single)()
                    If t.Length >= 1434 Then coords = t.ToArray()
                Next
                If coords Is Nothing Then Return Nothing
                ' 輸入 → 原圖（反矩陣）
                Dim inv = 1 / k
                Dim result(477) As Point2f
                Dim zs(477) As Single
                For i = 0 To 477
                    Dim x = coords(i * 3) - InputSize / 2.0, y = coords(i * 3 + 1) - InputSize / 2.0
                    result(i) = New Point2f(CSng(center.X + (x * cs - y * sn) * inv), CSng(center.Y + (x * sn + y * cs) * inv))
                    zs(i) = CSng(coords(i * 3 + 2) * inv)
                Next
                _lastZ = zs ' 深度（原圖像素）；呼叫端在鎖裡讀
                Return result
            End Using
        End Using
    End Function

    Private Shared Function Avg(p As Point2f(), idx As Integer()) As Point2f
        Return New Point2f(CSng(idx.Average(Function(i) p(i).X)), CSng(idx.Average(Function(i) p(i).Y)))
    End Function

    Private Shared Function Dist(a As Point2f, b As Point2f) As Double
        Return Math.Sqrt((a.X - b.X) ^ 2 + (a.Y - b.Y) ^ 2)
    End Function
End Class
