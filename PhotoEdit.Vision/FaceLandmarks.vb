Imports System.Drawing
Imports System.IO
Imports OpenCvSharp
Imports OpenCvSharp.Extensions
Imports OpenCvSharp.Face

''' <summary>
''' 68 點臉部特徵點（OpenCV FacemarkLBF，模型 Models\lbfmodel.yaml）：下顎 0–16、眉 17–26、鼻 27–35、眼 36–47、嘴唇外 48–59、內 60–67。
''' 用 YuNet 找到的臉框去對齊，結果存在 FaceRegion.Dense（0..1）。沒有模型時不做事，美顏的臉型類效果會停用。
''' </summary>
Public NotInheritable Class FaceLandmarks
    Private Sub New()
    End Sub

    Public Const ModelFile As String = "lbfmodel.yaml"
    Private Shared ReadOnly _lock As New Object()
    Private Shared _facemark As FacemarkLBF
    Private Shared _loadFailed As Boolean

    Public Shared ReadOnly Property DefaultModelPath As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "Models", ModelFile)
        End Get
    End Property

    Public Shared Function ModelAvailable() As Boolean
        Return File.Exists(DefaultModelPath)
    End Function

    ''' <summary>載入模型（只載一次）。OpenCV 打不開含中文的路徑，所以先複製到暫存資料夾（使用者名稱為英文時路徑全是 ASCII）。</summary>
    Private Shared Function Model() As FacemarkLBF
        If _facemark IsNot Nothing OrElse _loadFailed Then Return _facemark
        If Not ModelAvailable() Then Return Nothing
        Try
            Dim path = DefaultModelPath
            If path.Any(Function(c) AscW(c) > 127) Then
                Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "PhotoEdit")
                Directory.CreateDirectory(dir)
                Dim copy = IO.Path.Combine(dir, ModelFile)
                If Not File.Exists(copy) OrElse New FileInfo(copy).Length <> New FileInfo(path).Length Then File.Copy(path, copy, True)
                path = copy
            End If
            Dim fm = FacemarkLBF.Create()
            fm.LoadModel(path)
            _facemark = fm
        Catch ex As Exception
            _loadFailed = True
        End Try
        Return _facemark
    End Function

    ''' <summary>替每張臉算 68 點，存進 FaceRegion.Dense；失敗或沒有模型時不動。</summary>
    Public Shared Sub Fit(image As Bitmap, faces As IList(Of FaceRegion))
        If faces Is Nothing OrElse faces.Count = 0 Then Return
        SyncLock _lock
            Dim fm = Model()
            If fm Is Nothing Then Return
            Using bgra = BitmapConverter.ToMat(image), gray As New Mat()
                Cv2.CvtColor(bgra, gray, If(bgra.Channels() = 4, ColorConversionCodes.BGRA2GRAY, ColorConversionCodes.BGR2GRAY))
                Dim w = gray.Cols, h = gray.Rows
                For Each f In faces
                    f.Dense = FitOne(fm, gray, f, w, h)
                Next
            End Using
        End SyncLock
    End Sub


    ''' <summary>
    ''' 一張臉：LBF 只認得正的臉，所以先依 YuNet 的兩眼把臉轉正再對齊、轉回原角度（也試不轉的版本，取和 YuNet 五點最吻合的）。
    ''' 兩眼或嘴角和 YuNet 差太多時視為失敗（回傳 Nothing），寧可不套用也不要畫錯地方。
    ''' </summary>
    Private Shared Function FitOne(fm As FacemarkLBF, gray As Mat, f As FaceRegion, w As Integer, h As Integer) As PointF()
        Dim yu = f.Landmarks.Select(Function(p) New Point2f(p.X * w, p.Y * h)).ToArray()
        Dim eyeDist = Dist(yu(0), yu(1))
        If eyeDist < 6 Then Return Nothing
        Dim angle = Math.Atan2(yu(1).Y - yu(0).Y, yu(1).X - yu(0).X) * 180 / Math.PI ' 兩眼連線的角度（0＝水平）
        Dim best As Point2f() = Nothing, bestErr = Double.MaxValue
        For Each a In If(Math.Abs(angle) < 3, {0.0}, {angle, 0.0})
            Dim pts = TryFit(fm, gray, f, w, h, a)
            If pts Is Nothing Then Continue For
            Dim err = FitError(pts, yu)
            If err < bestErr Then bestErr = err : best = pts
        Next
        If best Is Nothing OrElse bestErr > eyeDist * 0.22 Then Return Nothing
        Return best.Select(Function(p) New PointF(p.X / w, p.Y / h)).ToArray()
    End Function

    ''' <summary>68 點和 YuNet 五點（兩眼中心、兩嘴角）最大的距離。</summary>
    Private Shared Function FitError(pts As Point2f(), yu As Point2f()) As Double
        Dim near = Function(p As Point2f, a As Point2f, b As Point2f) Math.Min(Dist(p, a), Dist(p, b))
        Return {near(Center(pts, 36), yu(0), yu(1)), near(Center(pts, 42), yu(0), yu(1)),
                near(pts(48), yu(3), yu(4)), near(pts(54), yu(3), yu(4))}.Max()
    End Function

    ''' <summary>以臉中心把一塊區域轉 angleDeg 度（讓兩眼水平）後對齊，結果轉回原圖座標。</summary>
    Private Shared Function TryFit(fm As FacemarkLBF, gray As Mat, f As FaceRegion, w As Integer, h As Integer, angleDeg As Double) As Point2f()
        Dim bw = f.Box.Width * w, bh = f.Box.Height * h
        Dim c As New Point2f(f.Box.X * w + bw / 2, f.Box.Y * h + bh / 2)
        Dim half = CInt(Math.Max(bw, bh) * 1.2)
        Dim crop = New Rect(CInt(c.X) - half, CInt(c.Y) - half, half * 2, half * 2).Intersect(New Rect(0, 0, w, h))
        If crop.Width < 16 OrElse crop.Height < 16 Then Return Nothing
        Using piece As New Mat(gray, crop), upright As New Mat()
            Dim lc As New Point2f(c.X - crop.X, c.Y - crop.Y)
            Using rot = Cv2.GetRotationMatrix2D(lc, angleDeg, 1.0)
                Cv2.WarpAffine(piece, upright, rot, piece.Size(), InterpolationFlags.Linear, BorderTypes.Replicate)
                ' LBF 是用比較寬鬆的臉框訓練的，往外放一點
                Dim r = New Rect(CInt(lc.X - bw * 0.55), CInt(lc.Y - bh * 0.48), CInt(bw * 1.1), CInt(bh * 1.05))
                Dim points As Point2f()() = Nothing
                Using arr = InputArray.Create({r})
                    If Not fm.Fit(upright, arr, points) OrElse points Is Nothing OrElse points.Length = 0 OrElse
                       points(0) Is Nothing OrElse points(0).Length <> 68 Then Return Nothing
                End Using
                ' 反向轉回：旋轉矩陣 [cos sin; -sin cos] 的反矩陣
                Dim t = angleDeg * Math.PI / 180
                Dim cs = Math.Cos(t), sn = Math.Sin(t)
                Return points(0).Select(Function(p)
                                            Dim dx = p.X - lc.X, dy = p.Y - lc.Y
                                            Return New Point2f(CSng(lc.X + dx * cs - dy * sn + crop.X), CSng(lc.Y + dx * sn + dy * cs + crop.Y))
                                        End Function).ToArray()
            End Using
        End Using
    End Function

    Private Shared Function Center(p As Point2f(), start As Integer) As Point2f
        Return New Point2f(CSng(Enumerable.Range(start, 6).Average(Function(i) p(i).X)), CSng(Enumerable.Range(start, 6).Average(Function(i) p(i).Y)))
    End Function

    Private Shared Function Dist(a As Point2f, b As Point2f) As Double
        Return Math.Sqrt((a.X - b.X) ^ 2 + (a.Y - b.Y) ^ 2)
    End Function
End Class
