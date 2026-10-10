Imports System.Drawing
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>變臉的換臉方式。</summary>
Public Enum FaceChangeStyle
    ''' <summary>扯臉：臉譜從一側被快速扯開。</summary>
    Pull = 0
    ''' <summary>抹臉：手從下巴往額頭一抹。</summary>
    Wipe = 1
    ''' <summary>吹臉：一陣煙霧閃過就換了。</summary>
    Blow = 2
    ''' <summary>三種輪流。</summary>
    Mixed = 3
End Enum

''' <summary>變臉的時間設定。</summary>
Public NotInheritable Class FaceChangeTiming
    ''' <summary>每張臉停留幾秒。</summary>
    Public Property Hold As Double = 1.0
    ''' <summary>換臉的動作幾秒。</summary>
    Public Property Transition As Double = 0.35
    Public Property Style As FaceChangeStyle = FaceChangeStyle.Mixed
    Public Property Fps As Integer = 30
End Class

''' <summary>
''' 川劇變臉影片：同一張照片依序換上幾張臉譜（0＝本人），每次換臉用扯臉、抹臉或吹臉的動作，頭會跟著一頓。
''' 每張臉都用 PortraitRetouch.Apply 畫（和手動調整的戲曲頁一樣：臉上的遮擋物不塗、頭髮不塗）。
''' 換臉只會改到臉附近，所以轉場直接在整張圖上做也不會動到背景。
''' </summary>
Public NotInheritable Class FaceChange
    Implements IDisposable

    Private ReadOnly _looks As New List(Of Mat)
    Private ReadOnly _upCoord As Mat ' 每點在「下巴→額頭」方向的位置（0＝下巴、1＝額頭頂）
    Private ReadOnly _sideCoord As Mat ' 每點在臉的左右方向的位置（0＝左邊臉外、1＝右邊臉外）
    Private ReadOnly _noise As Mat ' 吹臉用的煙霧雜訊（0..1）
    Private ReadOnly _haze As Mat ' 臉附近的柔和橢圓（0..1）
    Private ReadOnly _center As Point2f

    Public ReadOnly Property Width As Integer
    Public ReadOnly Property Height As Integer
    ''' <summary>找到臉沒有（沒有臉時每張都是原圖）。</summary>
    Public ReadOnly Property HasFace As Boolean

    Public ReadOnly Property Count As Integer
        Get
            Return _looks.Count
        End Get
    End Property

    ''' <param name="roleIds">依序要換的臉（OperaRoles 的編號；0＝本人）。</param>
    ''' <param name="maxSide">畫面長邊上限。</param>
    Public Sub New(source As Bitmap, roleIds As IList(Of Integer), Optional maxSide As Integer = 1080)
        Dim k = Math.Min(1.0, maxSide / Math.Max(source.Width, source.Height))
        Width = Math.Max(2, CInt(Math.Round(source.Width * k / 2)) * 2)
        Height = Math.Max(2, CInt(Math.Round(source.Height * k / 2)) * 2)
        Using src As New Bitmap(source, Width, Height)
            Dim faces = New FaceDetector().Detect(src)
            FaceMesh.Fit(src, faces)
            FaceLandmarks.Fit(src, faces.Where(Function(x) x.Dense Is Nothing).ToList())
            HasFace = faces.Count > 0
            Using baseMat = ToBgr(src)
                For Each id In roleIds
                    If id <= 0 OrElse Not HasFace Then
                        _looks.Add(baseMat.Clone())
                        Continue For
                    End If
                    Dim b As New BeautySettings()
                    b.SetOperaRole(id)
                    Dim r As New EditRecipe()
                    r.SetGlobalBeauty(b)
                    Using painted = PortraitRetouch.Apply(src, faces, r)
                        _looks.Add(If(painted Is Nothing, baseMat.Clone(), ToBgr(painted)))
                    End Using
                Next
            End Using
            ' 臉的方向與範圍（最大的那張臉；沒有臉時用畫面中央）
            Dim f = faces.OrderByDescending(Function(x) x.Box.Width * x.Box.Height).FirstOrDefault()
            Dim box = If(f Is Nothing, New RectangleF(0.3F, 0.25F, 0.4F, 0.4F), f.Box)
            Dim cx = (box.X + box.Width / 2) * Width, cy = (box.Y + box.Height / 2) * Height
            Dim fw = box.Width * Width, fh = box.Height * Height
            Dim ux = 0.0, uy = -1.0 ' 往額頭的方向
            If f?.Mesh IsNot Nothing AndAlso f.Mesh.Length > 152 Then
                Dim dx = (f.Mesh(10).X - f.Mesh(152).X) * Width, dy = (f.Mesh(10).Y - f.Mesh(152).Y) * Height
                Dim len = Math.Sqrt(dx * dx + dy * dy)
                If len > 1 Then ux = dx / len : uy = dy / len : fh = CSng(len * 1.25)
            End If
            _center = New Point2f(CSng(cx), CSng(cy))
            _upCoord = New Mat(Height, Width, MatType.CV_32FC1)
            _sideCoord = New Mat(Height, Width, MatType.CV_32FC1)
            _haze = New Mat(Height, Width, MatType.CV_32FC1)
            Dim upIdx = _upCoord.GetGenericIndexer(Of Single)(), sideIdx = _sideCoord.GetGenericIndexer(Of Single)(), hazeIdx = _haze.GetGenericIndexer(Of Single)()
            For y = 0 To Height - 1
                For x = 0 To Width - 1
                    Dim px = x - cx, py = y - cy
                    Dim along = px * ux + py * uy ' 往額頭為正
                    Dim across = px * -uy + py * ux
                    upIdx(y, x) = CSng(0.5 + along / (fh * 1.1))
                    sideIdx(y, x) = CSng(0.5 + across / (fw * 1.5))
                    Dim e = (across / (fw * 0.75)) ^ 2 + (along / (fh * 0.75)) ^ 2
                    hazeIdx(y, x) = CSng(Math.Exp(-e * 1.5))
                Next
            Next
            ' 煙霧：小張隨機雜訊放大再模糊（固定種子，每次輸出一樣）
            Using tiny As New Mat(24, CInt(24.0 * Width / Height) + 1, MatType.CV_32FC1)
                Dim rnd As New Random(7)
                Dim ti = tiny.GetGenericIndexer(Of Single)()
                For y = 0 To tiny.Rows - 1
                    For x = 0 To tiny.Cols - 1
                        ti(y, x) = CSng(rnd.NextDouble())
                    Next
                Next
                _noise = New Mat()
                Cv2.Resize(tiny, _noise, New OpenCvSharp.Size(Width, Height), 0, 0, InterpolationFlags.Cubic)
                Cv2.GaussianBlur(_noise, _noise, New OpenCvSharp.Size(0, 0), Math.Max(2, fw * 0.04))
                Cv2.Normalize(_noise, _noise, 0, 1, NormTypes.MinMax)
            End Using
        End Using
    End Sub

    Private Shared Function ToBgr(img As Bitmap) As Mat
        Using m = BitmapConverter.ToMat(img)
            Dim r As New Mat()
            If m.Channels() = 4 Then
                Cv2.CvtColor(m, r, ColorConversionCodes.BGRA2BGR)
            Else
                m.CopyTo(r)
            End If
            Return r
        End Using
    End Function

    ''' <summary>第 i 張臉（複本）。</summary>
    Public Function Look(i As Integer) As Mat
        Return _looks(i).Clone()
    End Function

    Public Function LookBitmap(i As Integer) As Bitmap
        Return BitmapConverter.ToBitmap(_looks(i))
    End Function

    ''' <summary>第 i 張換到第 i+1 張，進度 p（0..1）。</summary>
    Public Function Transition(i As Integer, p As Double, style As FaceChangeStyle) As Mat
        If style = FaceChangeStyle.Mixed Then style = CType(i Mod 3, FaceChangeStyle)
        Dim a = _looks(i), b = _looks(i + 1)
        p = Math.Max(0, Math.Min(1, p))
        Dim ease = p * p * (3 - 2 * p)
        Dim mixed As New Mat()
        Using wa As New Mat(Height, Width, MatType.CV_32FC1), wb As New Mat()
            Select Case style
                Case FaceChangeStyle.Wipe
                    ' 抹臉：邊界從下巴往額頭掃過，邊界附近的舊臉被抹糊
                    Dim edge = -0.15 + ease * 1.3
                    Using t As New Mat()
                        Cv2.Subtract(_upCoord, New Scalar(edge), t)
                        Cv2.Multiply(t, New Scalar(1 / 0.12), t)
                        Clamp01(t, wa) ' 邊界以上（還沒抹到）＝舊臉
                    End Using
                    Using smear As New Mat(), aBlur As New Mat()
                        Using kern As New Mat(15, 1, MatType.CV_32FC1, New Scalar(1.0 / 15))
                            Cv2.Filter2D(a, aBlur, -1, kern)
                        End Using
                        ' 邊界帶裡用抹糊的舊臉
                        Using bandW As New Mat()
                            Cv2.Multiply(wa, wa, bandW)
                            Cv2.Subtract(wa, bandW, bandW)
                            Cv2.Multiply(bandW, New Scalar(4), bandW) ' 4w(1-w)：邊界最強
                            Using a2 As New Mat()
                                BlendInto(aBlur, a, bandW, a2)
                                Cv2.Subtract(New Scalar(1), wa, wb)
                                Cv2.BlendLinear(a2, b, wa, wb, mixed)
                            End Using
                        End Using
                    End Using
                Case FaceChangeStyle.Pull
                    ' 扯臉：從左往右扯開，舊臉被扯的那一塊往右滑、邊緣有陰影
                    Dim edge = -0.1 + ease * 1.25
                    Using t As New Mat()
                        Cv2.Subtract(_sideCoord, New Scalar(edge), t)
                        Cv2.Multiply(t, New Scalar(1 / 0.05), t)
                        Clamp01(t, wa)
                    End Using
                    Using shifted As New Mat(), m As New Mat(2, 3, MatType.CV_64FC1)
                        Dim shift = ease * Width * 0.06
                        m.Set(0, 0, 1.0) : m.Set(0, 1, 0.0) : m.Set(0, 2, shift)
                        m.Set(1, 0, 0.0) : m.Set(1, 1, 1.0) : m.Set(1, 2, 0.0)
                        Cv2.WarpAffine(a, shifted, m, New OpenCvSharp.Size(Width, Height), InterpolationFlags.Linear, BorderTypes.Replicate)
                        ' 只有臉附近會滑（背景本來就一樣，不動）
                        Using a2 As New Mat()
                            BlendInto(shifted, a, _haze, a2)
                            Cv2.Subtract(New Scalar(1), wa, wb)
                            Cv2.BlendLinear(a2, b, wa, wb, mixed)
                        End Using
                    End Using
                    ' 邊緣陰影
                    Using sh As New Mat(), d As New Mat()
                        Cv2.Subtract(_sideCoord, New Scalar(edge), sh)
                        Cv2.Multiply(sh, sh, sh)
                        Cv2.Multiply(sh, New Scalar(-1 / (0.03 * 0.03)), sh)
                        Cv2.Exp(sh, sh)
                        Cv2.Multiply(sh, _haze, sh)
                        Cv2.Multiply(sh, New Scalar(0.45 * Math.Sin(Math.PI * p)), sh)
                        Darken(mixed, sh)
                    End Using
                Case Else
                    ' 吹臉：煙霧雜訊門檻往上升，換到新臉；中間臉附近一陣白霧
                    Using t As New Mat()
                        Cv2.Subtract(_noise, New Scalar(ease * 1.3 - 0.15), t)
                        Cv2.Multiply(t, New Scalar(1 / 0.12), t)
                        Clamp01(t, wa)
                    End Using
                    Cv2.Subtract(New Scalar(1), wa, wb)
                    Cv2.BlendLinear(a, b, wa, wb, mixed)
                    Using fog As New Mat()
                        Cv2.Multiply(_haze, New Scalar(0.6 * Math.Sin(Math.PI * p)), fog)
                        Using fogged As New Mat(), white As New Mat(Height, Width, MatType.CV_8UC3, New Scalar(238, 240, 242))
                            BlendInto(white, mixed, fog, fogged)
                            fogged.CopyTo(mixed)
                        End Using
                    End Using
            End Select
        End Using
        ' 頭一頓：以臉為中心微微放大再回來
        Dim s = 1 + 0.035 * Math.Sin(Math.PI * p)
        Dim r As New Mat()
        Using m = Cv2.GetRotationMatrix2D(_center, 0, s)
            Cv2.WarpAffine(mixed, r, m, New OpenCvSharp.Size(Width, Height), InterpolationFlags.Linear, BorderTypes.Replicate)
        End Using
        mixed.Dispose()
        Return r
    End Function

    Private Shared Sub Clamp01(src As Mat, dst As Mat)
        Cv2.Max(src, New Scalar(0), dst)
        Cv2.Min(dst, New Scalar(1), dst)
    End Sub

    ''' <summary>dst = top×w + bottom×(1−w)（w 是單通道 0..1）。</summary>
    Private Sub BlendInto(top As Mat, bottom As Mat, w As Mat, dst As Mat)
        Using inv As New Mat(), wc As New Mat()
            w.CopyTo(wc)
            Cv2.Subtract(New Scalar(1), wc, inv)
            Cv2.BlendLinear(top, bottom, wc, inv, dst)
        End Using
    End Sub

    ''' <summary>img ×= (1 − amount)。</summary>
    Private Shared Sub Darken(img As Mat, amount As Mat)
        Using f As New Mat(), k As New Mat(), k3 As New Mat()
            img.ConvertTo(f, MatType.CV_32FC3)
            Cv2.Subtract(New Scalar(1), amount, k)
            Cv2.Merge({k, k, k}, k3)
            Cv2.Multiply(f, k3, f)
            f.ConvertTo(img, MatType.CV_8UC3)
        End Using
    End Sub

    ''' <summary>整段影片的格子：每張停 Hold 秒，中間換臉 Transition 秒。</summary>
    Public Function Frames(timing As FaceChangeTiming, fps As Integer) As List(Of (Render As Func(Of Mat), Duration As Double))
        Dim list As New List(Of (Render As Func(Of Mat), Duration As Double))
        Dim one = 1.0 / fps
        Dim n = Math.Max(2, CInt(Math.Round(timing.Transition * fps)))
        For i = 0 To _looks.Count - 1
            Dim idx = i
            list.Add((Function() Look(idx), Math.Max(one, timing.Hold)))
            If i = _looks.Count - 1 Then Exit For
            For k = 1 To n ' n 格剛好是換臉時間（頭尾兩張不算在裡面）
                Dim p = k / CDbl(n + 1)
                list.Add((Function() Transition(idx, p, timing.Style), one))
            Next
        Next
        Return list
    End Function

    Public Function WriteMp4(path As String, timing As FaceChangeTiming, Optional progress As Action(Of Integer, Integer) = Nothing,
                             Optional ct As Threading.CancellationToken = Nothing) As String
        Return VideoExport.WriteMp4(path, Width, Height, timing.Fps, Frames(timing, timing.Fps), progress, ct)
    End Function

    Public Sub WriteGif(path As String, timing As FaceChangeTiming, Optional gifSide As Integer = 480, Optional gifFps As Integer = 15,
                        Optional progress As Action(Of Integer, Integer) = Nothing, Optional ct As Threading.CancellationToken = Nothing)
        Dim sz = VideoExport.GifSize(Width, Height, gifSide)
        VideoExport.WriteGif(path, sz.W, sz.H, Frames(timing, gifFps), progress, ct)
    End Sub

    ''' <summary>整段長度（秒）。</summary>
    Public Function TotalSeconds(timing As FaceChangeTiming) As Double
        Return Frames(timing, timing.Fps).Sum(Function(x) x.Duration)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each m In _looks
            m.Dispose()
        Next
        _upCoord?.Dispose() : _sideCoord?.Dispose() : _noise?.Dispose() : _haze?.Dispose()
    End Sub
End Class
