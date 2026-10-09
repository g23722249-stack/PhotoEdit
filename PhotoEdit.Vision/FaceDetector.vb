Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports OpenCvSharp
Imports OpenCvSharp.Dnn
Imports OpenCvSharp.Extensions

''' <summary>
''' YuNet 人臉偵測（OpenCV Zoo face_detection_yunet_2023mar.onnx），由 iPhoto.Net 的 Quartz.FaceEngine 精簡而來：
''' 只做偵測、不做辨識；輸入已轉正的 Bitmap，回傳 0..1 比例座標的 FaceRegion。
''' 同一個實例可跨執行緒使用（網路一次只跑一個呼叫）。
''' </summary>
Public NotInheritable Class FaceDetector
    Implements IDisposable

    Public Const ModelFile As String = "face_detection_yunet_2023mar.onnx"

    Private Shared ReadOnly Strides As Integer() = {8, 16, 32}
    Private Shared ReadOnly OutNames As String() = {
        "cls_8", "cls_16", "cls_32", "obj_8", "obj_16", "obj_32",
        "bbox_8", "bbox_16", "bbox_32", "kps_8", "kps_16", "kps_32"}

    Private ReadOnly _net As Net
    Private ReadOnly _lock As New Object()

    ''' <summary>偵測前把影像縮到長邊不超過此值。</summary>
    Public Property MaxSide As Integer = 1280
    Public Property ScoreThreshold As Single = 0.75F
    Public Property NmsThreshold As Single = 0.3F
    ''' <summary>縮小後寬度小於此值（像素）的臉略過，太小的臉修飾也看不出效果。</summary>
    Public Property MinFaceSize As Integer = 20

    Public Shared ReadOnly Property DefaultModelPath As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "Models", ModelFile)
        End Get
    End Property

    Public Shared Function ModelAvailable(Optional modelPath As String = Nothing) As Boolean
        Return File.Exists(If(modelPath, DefaultModelPath))
    End Function

    Public Sub New(Optional modelPath As String = Nothing)
        ' 從位元組載入：cv::dnn 在 Windows 上打不開含中文的路徑。
        _net = CvDnn.ReadNetFromOnnx(File.ReadAllBytes(If(modelPath, DefaultModelPath)))
        _net.SetPreferableBackend(Backend.OPENCV)
        _net.SetPreferableTarget(Target.CPU)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock _lock
            _net.Dispose()
        End SyncLock
    End Sub

    Public Function Detect(image As Bitmap) As List(Of FaceRegion)
        Using bgra = BitmapConverter.ToMat(image), bgr As New Mat()
            Cv2.CvtColor(bgra, bgr, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2BGR, ColorConversionCodes.RGB2BGR))
            Dim longSide = Math.Max(bgr.Cols, bgr.Rows)
            Dim k = If(longSide > MaxSide, MaxSide / CDbl(longSide), 1.0)
            Using small As New Mat()
                If k < 1 Then
                    Cv2.Resize(bgr, small, New OpenCvSharp.Size(Math.Max(1, CInt(bgr.Cols * k)), Math.Max(1, CInt(bgr.Rows * k))), 0, 0, InterpolationFlags.Area)
                Else
                    bgr.CopyTo(small)
                End If
                Dim w As Single = small.Cols, h As Single = small.Rows
                SyncLock _lock
                    Dim found = DetectPixels(small)
                    ' 大特寫（自拍、證件照）臉太大時 YuNet 會漏抓：縮小再找一次
                    For Each side In {640, 400}
                        If found.Count > 0 OrElse Math.Max(small.Cols, small.Rows) <= side Then Exit For
                        Dim s = side / CDbl(Math.Max(small.Cols, small.Rows))
                        Using tiny As New Mat()
                            Cv2.Resize(small, tiny, New OpenCvSharp.Size(Math.Max(1, CInt(small.Cols * s)), Math.Max(1, CInt(small.Rows * s))), 0, 0, InterpolationFlags.Area)
                            w = tiny.Cols : h = tiny.Rows
                            found = DetectPixels(tiny)
                        End Using
                    Next
                    Dim fw = w, fh = h
                    Return found.
                        Select(Function(f) ToFraction(f, fw, fh)).
                        OrderByDescending(Function(f) f.Box.Width * f.Box.Height).
                        ToList()
                End SyncLock
            End Using
        End Using
    End Function

    Private Function DetectPixels(bgr As Mat) As List(Of FaceRegion)
        Dim padW = ((bgr.Cols - 1) \ 32 + 1) * 32
        Dim padH = ((bgr.Rows - 1) \ 32 + 1) * 32
        Dim outs = OutNames.Select(Function(x) New Mat()).ToArray()
        Try
            Using padded As New Mat()
                Cv2.CopyMakeBorder(bgr, padded, 0, padH - bgr.Rows, 0, padW - bgr.Cols, BorderTypes.Constant, Scalar.All(0))
                Using blob = CvDnn.BlobFromImage(padded)
                    _net.SetInput(blob)
                    _net.Forward(outs, OutNames)
                End Using
            End Using

            Dim boxes As New List(Of Rect), scores As New List(Of Single), faces As New List(Of FaceRegion)
            For i = 0 To Strides.Length - 1
                Dim stride = Strides(i)
                Dim cols = padW \ stride, rows = padH \ stride
                Dim cls = ToArray(outs(i)), obj = ToArray(outs(i + 3))
                Dim bbox = ToArray(outs(i + 6)), kps = ToArray(outs(i + 9))
                For r = 0 To rows - 1
                    For c = 0 To cols - 1
                        Dim idx = r * cols + c
                        Dim score = CSng(Math.Sqrt(Clamp01(cls(idx)) * Clamp01(obj(idx))))
                        If score < ScoreThreshold Then Continue For
                        Dim cx = (c + bbox(idx * 4)) * stride
                        Dim cy = (r + bbox(idx * 4 + 1)) * stride
                        Dim bw = CSng(Math.Exp(bbox(idx * 4 + 2))) * stride
                        Dim bh = CSng(Math.Exp(bbox(idx * 4 + 3))) * stride
                        If bw < MinFaceSize Then Continue For
                        Dim f As New FaceRegion With {.Box = New RectangleF(cx - bw / 2, cy - bh / 2, bw, bh), .Score = score}
                        For n = 0 To 4
                            f.Landmarks(n) = New PointF((kps(idx * 10 + 2 * n) + c) * stride, (kps(idx * 10 + 2 * n + 1) + r) * stride)
                        Next
                        faces.Add(f)
                        boxes.Add(New Rect(CInt(f.Box.X), CInt(f.Box.Y), CInt(f.Box.Width), CInt(f.Box.Height)))
                        scores.Add(score)
                    Next
                Next
            Next

            Dim keep As Integer() = Nothing
            CvDnn.NMSBoxes(boxes, scores, ScoreThreshold, NmsThreshold, keep)
            Return keep.Select(Function(k) faces(k)).
                        Where(Function(f) f.Box.X < bgr.Cols AndAlso f.Box.Y < bgr.Rows).ToList()
        Finally
            For Each m In outs
                m.Dispose()
            Next
        End Try
    End Function

    Private Shared Function ToFraction(f As FaceRegion, w As Single, h As Single) As FaceRegion
        Dim r As New FaceRegion With {
            .Box = RectangleF.FromLTRB(Math.Max(0, f.Box.Left) / w, Math.Max(0, f.Box.Top) / h,
                                       Math.Min(w, f.Box.Right) / w, Math.Min(h, f.Box.Bottom) / h),
            .Score = f.Score}
        For n = 0 To 4
            r.Landmarks(n) = New PointF(f.Landmarks(n).X / w, f.Landmarks(n).Y / h)
        Next
        Return r
    End Function

    Friend Shared Function ToArray(m As Mat) As Single()
        Dim n = CInt(m.Total())
        Dim a(n - 1) As Single
        If m.IsContinuous() Then
            Marshal.Copy(m.Data, a, 0, n)
        Else
            Using c = m.Clone()
                Marshal.Copy(c.Data, a, 0, n)
            End Using
        End If
        Return a
    End Function

    Private Shared Function Clamp01(v As Single) As Single
        Return Math.Min(1.0F, Math.Max(0.0F, v))
    End Function
End Class
