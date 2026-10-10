Imports System.Drawing
Imports System.Runtime.InteropServices
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>變形動畫的一個對應點：名稱（給人看）與在原圖上的位置（像素）。</summary>
Public NotInheritable Class MorphPoint
    Public Property Name As String
    Public Property X As Single
    Public Property Y As Single

    Public Sub New()
    End Sub

    Public Sub New(name As String, p As PointF)
        Me.Name = name
        X = p.X
        Y = p.Y
    End Sub

    Public ReadOnly Property Pt As PointF
        Get
            Return New PointF(X, Y)
        End Get
    End Property
End Class

''' <summary>變形動畫的時間軸與輸出設定。</summary>
Public NotInheritable Class MorphTiming
    ''' <summary>變形本身幾秒。</summary>
    Public Property Seconds As Double = 3
    Public Property Fps As Integer = 30
    ''' <summary>開頭、結尾各停幾秒。</summary>
    Public Property HoldStart As Double = 0.5
    Public Property HoldEnd As Double = 1
    ''' <summary>變過去再變回來。</summary>
    Public Property PingPong As Boolean
    ''' <summary>慢入慢出。</summary>
    Public Property Ease As Boolean = True
End Class

''' <summary>
''' 兩張圖的變形動畫（Morph）：兩組對應點 → 共同畫布 → Delaunay 三角形 → 每一格把兩張圖各自變形到中間位置再交叉淡化。
''' 解析度不同的處理：輸出畫布用第一張圖的比例（長邊不超過 maxSide，寬高取偶數，MP4 需要）；
''' 第二張圖依兩眼位置縮放、旋轉、平移到畫布上（align＝對齊程度：0＝整張填滿畫布，1＝兩眼完全重疊），超出的地方用邊緣延伸補。
''' 這樣兩張臉的大小、位置接近，動畫只剩五官的形狀變化，不會整張大幅晃動。
''' </summary>
Public NotInheritable Class FaceMorph
    Implements IDisposable

    ''' <summary>預設點裡用來對齊、自動推估的 5 個關鍵點。</summary>
    Public Shared ReadOnly KeyNames As String() = {"左眼", "右眼", "鼻尖", "左嘴角", "右嘴角"}

    Private ReadOnly _a As Mat, _b As Mat
    Private ReadOnly _pa As Point2f(), _pb As Point2f()
    Private ReadOnly _tris As List(Of Integer())

    Public ReadOnly Property Width As Integer
    Public ReadOnly Property Height As Integer
    ''' <summary>第二張圖在畫布上放大的倍數（大於 1 表示放大、會比較糊）。</summary>
    Public ReadOnly Property ScaleB As Double
    Public ReadOnly Property ScaleA As Double

    '---------------------------------------------------------------------
    ' 預設點
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 人臉的預設點（約 50 點，用 478 點網格挑出來，再推估頭頂、耳朵位置、髮側、脖子、肩膀）。
    ''' 「耳尖」在人身上落在頭髮上，對應動物的耳朵。沒有網格時回傳 Nothing。
    ''' </summary>
    Public Shared Function DefaultPoints(face As FaceRegion, w As Integer, h As Integer) As List(Of MorphPoint)
        Dim m = face?.Mesh
        If m Is Nothing OrElse m.Length < 468 Then Return Nothing
        Dim P = Function(i As Integer) New PointF(m(i).X * w, m(i).Y * h)
        Dim avg = Function(idx As Integer()) New PointF(idx.Average(Function(i) m(i).X) * w, idx.Average(Function(i) m(i).Y) * h)
        Dim list As New List(Of MorphPoint)
        Dim add = Sub(n As String, q As PointF) list.Add(New MorphPoint(n, q))
        ' 關鍵點（順序固定，對齊與自動推估用）
        Dim eyeL = If(m.Length >= 478, P(468), avg({33, 133})), eyeR = If(m.Length >= 478, P(473), avg({263, 362}))
        If eyeL.X > eyeR.X Then
            Dim tmp = eyeL : eyeL = eyeR : eyeR = tmp
        End If
        add("左眼", eyeL) : add("右眼", eyeR)
        add("鼻尖", P(1))
        Dim mL = P(61), mR = P(291)
        If mL.X > mR.X Then
            Dim tmp = mL : mL = mR : mR = tmp
        End If
        add("左嘴角", mL) : add("右嘴角", mR)
        ' 眼睛（畫面左邊那隻＝網格 33/133 那組時，依 x 判斷）
        Dim leftIsLow = m(33).X < m(263).X
        Dim eyeSets = {(Outer:=33, Upper:=159, Inner:=133, Lower:=145), (Outer:=263, Upper:=386, Inner:=362, Lower:=374)}
        For k = 0 To 1
            Dim e = eyeSets(If(leftIsLow, k, 1 - k))
            Dim side = If(k = 0, "左", "右")
            add(side & "眼外角", P(e.Outer)) : add(side & "眼上緣", P(e.Upper)) : add(side & "眼內角", P(e.Inner)) : add(side & "眼下緣", P(e.Lower))
        Next
        ' 眉毛
        Dim brows = {New Integer() {70, 105, 107}, New Integer() {300, 334, 336}}
        For k = 0 To 1
            Dim br = brows(If(leftIsLow, k, 1 - k))
            Dim side = If(k = 0, "左", "右")
            Dim pts = br.Select(Function(i) P(i)).OrderBy(Function(q) q.X).ToArray()
            add(side & "眉1", pts(0)) : add(side & "眉2", pts(1)) : add(side & "眉3", pts(2))
        Next
        ' 鼻
        add("鼻樑", P(6))
        Dim nL = P(98), nR = P(327)
        If nL.X > nR.X Then
            Dim tmp = nL : nL = nR : nR = tmp
        End If
        add("左鼻翼", nL) : add("右鼻翼", nR) : add("鼻底", P(2))
        ' 嘴
        add("上唇", P(0)) : add("嘴中上", P(13)) : add("嘴中下", P(14)) : add("下唇", P(17))
        ' 臉的輪廓（16 點，從額頭頂順時針）
        Dim oval = {10, 297, 284, 389, 454, 361, 397, 378, 152, 149, 172, 132, 234, 162, 54, 67}
        For k = 0 To oval.Length - 1
            add("輪廓" & (k + 1), P(oval(k)))
        Next
        ' 推估：頭頂、耳尖、髮側、脖子、肩膀
        Dim top = P(10), chin = P(152)
        Dim fh = Math.Sqrt((top.X - chin.X) ^ 2 + (top.Y - chin.Y) ^ 2)
        Dim ux = (top.X - chin.X) / fh, uy = (top.Y - chin.Y) / fh ' 往頭頂的方向
        Dim sx = -uy, sy = ux ' 往畫面右邊的方向（大致）
        If sx < 0 Then sx = -sx : sy = -sy
        Dim fw = Math.Sqrt((P(234).X - P(454).X) ^ 2 + (P(234).Y - P(454).Y) ^ 2)
        Dim at = Function(b As PointF, up As Double, right As Double) New PointF(CSng(b.X + ux * up + sx * right), CSng(b.Y + uy * up + sy * right))
        add("頭頂", at(top, fh * 0.3, 0))
        add("左耳尖", at(top, fh * 0.32, -fw * 0.42)) : add("右耳尖", at(top, fh * 0.32, fw * 0.42))
        Dim sideL = If(m(234).X < m(454).X, P(234), P(454)), sideR = If(m(234).X < m(454).X, P(454), P(234))
        add("左髮側", at(sideL, 0, -fw * 0.2)) : add("右髮側", at(sideR, 0, fw * 0.2))
        add("脖子", at(chin, -fh * 0.3, 0))
        add("左肩", at(chin, -fh * 0.55, -fw * 0.55)) : add("右肩", at(chin, -fh * 0.55, fw * 0.55))
        ' 超出照片的推估點收回照片內
        For Each q In list
            q.X = Math.Max(0, Math.Min(w - 1, q.X))
            q.Y = Math.Max(0, Math.Min(h - 1, q.Y))
        Next
        Return list
    End Function

    ''' <summary>
    ''' 第二張圖的點：使用者標好 5 個關鍵點（KeyNames 的順序）後，把第一張的全部點用最小平方仿射搬過去，之後再手動微調。
    ''' </summary>
    Public Shared Function EstimateFromKeys(template As IList(Of MorphPoint), keysB As PointF(), w As Integer, h As Integer) As List(Of MorphPoint)
        Dim keysA = KeyNames.Select(Function(n) template.First(Function(q) q.Name = n).Pt).ToArray()
        ' 解 x' = a x + b y + c、y' = d x + e y + f（最小平方）
        Dim A As New Mat(keysA.Length, 3, MatType.CV_64FC1), bx As New Mat(keysA.Length, 1, MatType.CV_64FC1), by As New Mat(keysA.Length, 1, MatType.CV_64FC1)
        Try
            For i = 0 To keysA.Length - 1
                A.Set(i, 0, CDbl(keysA(i).X)) : A.Set(i, 1, CDbl(keysA(i).Y)) : A.Set(i, 2, 1.0)
                bx.Set(i, 0, CDbl(keysB(i).X)) : by.Set(i, 0, CDbl(keysB(i).Y))
            Next
            Using cx As New Mat(), cy As New Mat()
                Cv2.Solve(A, bx, cx, DecompTypes.SVD)
                Cv2.Solve(A, by, cy, DecompTypes.SVD)
                Dim result = template.Select(Function(q) New MorphPoint(q.Name, New PointF(
                    CSng(cx.At(Of Double)(0) * q.X + cx.At(Of Double)(1) * q.Y + cx.At(Of Double)(2)),
                    CSng(cy.At(Of Double)(0) * q.X + cy.At(Of Double)(1) * q.Y + cy.At(Of Double)(2))))).ToList()
                For i = 0 To KeyNames.Length - 1
                    Dim n = KeyNames(i)
                    Dim q = result.First(Function(r) r.Name = n)
                    q.X = keysB(i).X : q.Y = keysB(i).Y
                Next
                For Each q In result
                    q.X = Math.Max(0, Math.Min(w - 1, q.X))
                    q.Y = Math.Max(0, Math.Min(h - 1, q.Y))
                Next
                Return result
            End Using
        Finally
            A.Dispose() : bx.Dispose() : by.Dispose()
        End Try
    End Function

    '---------------------------------------------------------------------
    ' 準備：共同畫布、三角形
    '---------------------------------------------------------------------

    ''' <param name="maxSide">畫布長邊上限（像素）。</param>
    ''' <param name="align">第二張圖依兩眼對齊的程度 0..1。</param>
    Public Sub New(imageA As Bitmap, imageB As Bitmap, pointsA As IList(Of MorphPoint), pointsB As IList(Of MorphPoint),
                   Optional maxSide As Integer = 1080, Optional align As Double = 0.5)
        If pointsA.Count <> pointsB.Count Then Throw New ArgumentException("兩張圖的點數不同。")
        ' 畫布：第一張的比例，長邊不超過 maxSide、也不超過第一張原本的大小；寬高取偶數
        Dim k = Math.Min(1.0, maxSide / Math.Max(imageA.Width, imageA.Height))
        Width = Math.Max(2, CInt(Math.Round(imageA.Width * k / 2)) * 2)
        Height = Math.Max(2, CInt(Math.Round(imageA.Height * k / 2)) * 2)
        Dim sxA = Width / CDbl(imageA.Width), syA = Height / CDbl(imageA.Height)
        ScaleA = (sxA + syA) / 2
        _a = ToBgr(imageA, Width, Height)
        _pa = pointsA.Select(Function(q) New Point2f(CSng(q.X * sxA), CSng(q.Y * syA))).ToArray()
        ' 第二張：填滿畫布（cover）與兩眼對齊之間取 align
        Dim cover = Math.Max(Width / CDbl(imageB.Width), Height / CDbl(imageB.Height))
        Dim coverTx = (Width - imageB.Width * cover) / 2, coverTy = (Height - imageB.Height * cover) / 2
        Dim s = cover, angle = 0.0, tx = coverTx, ty = coverTy
        Dim iL = IndexOfName(pointsA, "左眼"), iR = IndexOfName(pointsA, "右眼")
        If align > 0 AndAlso iL >= 0 AndAlso iR >= 0 AndAlso IndexOfName(pointsB, "左眼") = iL AndAlso IndexOfName(pointsB, "右眼") = iR Then
            Dim aL = _pa(iL), aR = _pa(iR), bL = pointsB(iL).Pt, bR = pointsB(iR).Pt
            Dim da = Math.Sqrt((aR.X - aL.X) ^ 2 + (aR.Y - aL.Y) ^ 2), db = Math.Sqrt((bR.X - bL.X) ^ 2 + (bR.Y - bL.Y) ^ 2)
            If da > 1 AndAlso db > 1 Then
                Dim eyeS = da / db
                Dim eyeAng = Math.Atan2(aR.Y - aL.Y, aR.X - aL.X) - Math.Atan2(bR.Y - bL.Y, bR.X - bL.X)
                ' 縮放用對數內插；旋轉照比例
                s = Math.Exp(Math.Log(cover) * (1 - align) + Math.Log(eyeS) * align)
                angle = eyeAng * align
                ' 平移：兩眼中點（照 cover 放上去的位置 → 第一張的兩眼中點）照比例
                Dim bm = New PointF((bL.X + bR.X) / 2, (bL.Y + bR.Y) / 2), am = New PointF((aL.X + aR.X) / 2, (aL.Y + aR.Y) / 2)
                Dim coverM = New PointF(CSng(bm.X * cover + coverTx), CSng(bm.Y * cover + coverTy))
                Dim target = New PointF(CSng(coverM.X + (am.X - coverM.X) * align), CSng(coverM.Y + (am.Y - coverM.Y) * align))
                Dim c = Math.Cos(angle) * s, sn = Math.Sin(angle) * s
                tx = target.X - (c * bm.X - sn * bm.Y)
                ty = target.Y - (sn * bm.X + c * bm.Y)
            End If
        End If
        ScaleB = s
        Dim cc = Math.Cos(angle) * s, ss = Math.Sin(angle) * s
        Using srcB = ToBgr(imageB, imageB.Width, imageB.Height), m As New Mat(2, 3, MatType.CV_64FC1)
            m.Set(0, 0, cc) : m.Set(0, 1, -ss) : m.Set(0, 2, tx)
            m.Set(1, 0, ss) : m.Set(1, 1, cc) : m.Set(1, 2, ty)
            _b = New Mat()
            Cv2.WarpAffine(srcB, _b, m, New OpenCvSharp.Size(Width, Height), If(s > 1, InterpolationFlags.Cubic, InterpolationFlags.Area), BorderTypes.Replicate)
        End Using
        _pb = pointsB.Select(Function(q) New Point2f(CSng(cc * q.X - ss * q.Y + tx), CSng(ss * q.X + cc * q.Y + ty))).ToArray()
        ' 畫布邊緣的固定點（四角、每邊三點），背景跟著一起變
        Dim border As New List(Of Point2f)
        For i = 0 To 4
            Dim fx = CSng((Width - 1) * i / 4.0), fy = CSng((Height - 1) * i / 4.0)
            border.Add(New Point2f(fx, 0)) : border.Add(New Point2f(fx, Height - 1))
            If i > 0 AndAlso i < 4 Then border.Add(New Point2f(0, fy)) : border.Add(New Point2f(Width - 1, fy))
        Next
        _pa = ClampAll(_pa.Concat(border).ToArray())
        _pb = ClampAll(_pb.Concat(border).ToArray())
        _tris = Triangulate()
    End Sub

    Private Shared Function IndexOfName(pts As IList(Of MorphPoint), name As String) As Integer
        For i = 0 To pts.Count - 1
            If pts(i).Name = name Then Return i
        Next
        Return -1
    End Function

    Private Function ClampAll(p As Point2f()) As Point2f()
        Return p.Select(Function(q) New Point2f(Math.Max(0, Math.Min(Width - 1, q.X)), Math.Max(0, Math.Min(Height - 1, q.Y)))).ToArray()
    End Function

    Private Shared Function ToBgr(img As Bitmap, w As Integer, h As Integer) As Mat
        Using src = BitmapConverter.ToMat(img), bgr As New Mat()
            Select Case src.Channels()
                Case 4 : Cv2.CvtColor(src, bgr, ColorConversionCodes.BGRA2BGR)
                Case 1 : Cv2.CvtColor(src, bgr, ColorConversionCodes.GRAY2BGR)
                Case Else : src.CopyTo(bgr)
            End Select
            Dim r As New Mat()
            If bgr.Width = w AndAlso bgr.Height = h Then
                bgr.CopyTo(r)
            Else
                Cv2.Resize(bgr, r, New OpenCvSharp.Size(w, h), 0, 0, If(w < bgr.Width, InterpolationFlags.Area, InterpolationFlags.Cubic))
            End If
            Return r
        End Using
    End Function

    ''' <summary>在兩組點的平均位置上做 Delaunay（同一組三角形用在每一格，中途不會換連法而跳動）。</summary>
    Private Function Triangulate() As List(Of Integer())
        Dim mid = Enumerable.Range(0, _pa.Length).Select(Function(i) New Point2f((_pa(i).X + _pb(i).X) / 2, (_pa(i).Y + _pb(i).Y) / 2)).ToArray()
        Dim result As New List(Of Integer())
        Using sub2 As New Subdiv2D(New Rect(-1, -1, Width + 2, Height + 2))
            Dim seen As New HashSet(Of Long)
            Dim used As New List(Of Point2f)
            For Each q In mid
                Dim key = CLng(Math.Round(q.X * 2)) * 100000 + CLng(Math.Round(q.Y * 2))
                If seen.Add(key) Then sub2.Insert(q) ' 重疊的點只插一次
            Next
            For Each t In sub2.GetTriangleList()
                Dim idx = {Nearest(mid, t.Item0, t.Item1), Nearest(mid, t.Item2, t.Item3), Nearest(mid, t.Item4, t.Item5)}
                If idx.Any(Function(i) i < 0) OrElse idx.Distinct().Count() < 3 Then Continue For
                result.Add(idx)
            Next
        End Using
        Return result
    End Function

    Private Shared Function Nearest(pts As Point2f(), x As Single, y As Single) As Integer
        Dim best = -1, bd = 4.0F
        For i = 0 To pts.Length - 1
            Dim d = (pts(i).X - x) ^ 2 + (pts(i).Y - y) ^ 2
            If d < bd Then bd = CSng(d) : best = i
        Next
        Return best
    End Function

    '---------------------------------------------------------------------
    ' 一格
    '---------------------------------------------------------------------

    ''' <summary>t＝0 第一張、1 第二張。形狀用 t，顏色在中段才交換（看起來比較像「變身」而不是兩張疊在一起）。</summary>
    Public Function Frame(t As Double) As Mat
        t = Math.Max(0, Math.Min(1, t))
        Dim pt = Enumerable.Range(0, _pa.Length).Select(Function(i) New Point2f(CSng(_pa(i).X + (_pb(i).X - _pa(i).X) * t), CSng(_pa(i).Y + (_pb(i).Y - _pa(i).Y) * t))).ToArray()
        Dim alpha = Smooth(t, 0.15, 0.85)
        Using wa = WarpTo(_a, _pa, pt), wb = WarpTo(_b, _pb, pt)
            Dim r As New Mat()
            Cv2.AddWeighted(wa, 1 - alpha, wb, alpha, 0, r)
            Return r
        End Using
    End Function

    Public Function FrameBitmap(t As Double) As Bitmap
        Using m = Frame(t)
            Return BitmapConverter.ToBitmap(m)
        End Using
    End Function

    Private Function WarpTo(src As Mat, from As Point2f(), [to] As Point2f()) As Mat
        Dim dst = src.Clone() ' 三角形沒蓋到的地方（理論上沒有）保留原圖
        Dim bounds As New Rect(0, 0, Width, Height)
        For Each tri In _tris
            Dim d = {[to](tri(0)), [to](tri(1)), [to](tri(2))}
            Dim s = {from(tri(0)), from(tri(1)), from(tri(2))}
            Dim area = (d(1).X - d(0).X) * (d(2).Y - d(0).Y) - (d(2).X - d(0).X) * (d(1).Y - d(0).Y)
            If Math.Abs(area) < 0.5 Then Continue For
            Dim r = Cv2.BoundingRect(d.Select(Function(q) New OpenCvSharp.Point(CInt(Math.Floor(q.X)), CInt(Math.Floor(q.Y)))).
                                     Concat(d.Select(Function(q) New OpenCvSharp.Point(CInt(Math.Ceiling(q.X)), CInt(Math.Ceiling(q.Y)))))).Intersect(bounds)
            If r.Width < 1 OrElse r.Height < 1 Then Continue For
            Dim local = d.Select(Function(q) New Point2f(q.X - r.X, q.Y - r.Y)).ToArray()
            Using m = Cv2.GetAffineTransform(s, local), patch As New Mat(), mask As New Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.All(0)), roi As New Mat(dst, r)
                Cv2.WarpAffine(src, patch, m, New OpenCvSharp.Size(r.Width, r.Height), InterpolationFlags.Linear, BorderTypes.Reflect101)
                Cv2.FillConvexPoly(mask, local.Select(Function(q) New OpenCvSharp.Point(CInt(Math.Round(q.X)), CInt(Math.Round(q.Y)))).ToArray(), Scalar.All(255), LineTypes.Link8)
                patch.CopyTo(roi, mask)
            End Using
        Next
        Return dst
    End Function

    Private Shared Function Smooth(v As Double, e0 As Double, e1 As Double) As Double
        Dim x = Math.Max(0, Math.Min(1, (v - e0) / (e1 - e0)))
        Return x * x * (3 - 2 * x)
    End Function

    '---------------------------------------------------------------------
    ' 時間軸與輸出
    '---------------------------------------------------------------------

    ''' <summary>每一格的 t 與這格要停多久（秒）；停留的部分合成一格（GIF 用延遲時間，MP4 重複寫）。</summary>
    Public Shared Function Timeline(timing As MorphTiming, fps As Integer) As List(Of (T As Double, Duration As Double))
        Dim list As New List(Of (T As Double, Duration As Double))
        Dim n = Math.Max(2, CInt(Math.Round(timing.Seconds * fps)))
        Dim one = 1.0 / fps
        Dim ease = Function(x As Double) If(timing.Ease, Smooth(x, 0, 1), x)
        list.Add((0, Math.Max(one, timing.HoldStart)))
        For i = 1 To n - 1
            list.Add((ease(i / CDbl(n)), one))
        Next
        list.Add((1, Math.Max(one, timing.HoldEnd)))
        If timing.PingPong Then
            For i = n - 1 To 1 Step -1
                list.Add((ease(i / CDbl(n)), one))
            Next
            list.Add((0, Math.Max(one, timing.HoldStart)))
        End If
        Return list
    End Function

    ''' <summary>時間軸換成 VideoExport 用的格子（每格現算）。</summary>
    Private Function Frames(timing As MorphTiming, fps As Integer) As List(Of (Render As Func(Of Mat), Duration As Double))
        Return Timeline(timing, fps).Select(Function(x) (Render:=CType(Function() Frame(x.T), Func(Of Mat)), Duration:=x.Duration)).ToList()
    End Function

    ''' <summary>寫 MP4（見 VideoExport）。回傳實際用的編碼。</summary>
    Public Function WriteMp4(path As String, timing As MorphTiming, Optional progress As Action(Of Integer, Integer) = Nothing,
                             Optional ct As Threading.CancellationToken = Nothing) As String
        Return VideoExport.WriteMp4(path, Width, Height, timing.Fps, Frames(timing, timing.Fps), progress, ct)
    End Function

    ''' <summary>寫 GIF（長邊縮到 gifSide、每秒 gifFps 格）。</summary>
    Public Sub WriteGif(path As String, timing As MorphTiming, Optional gifSide As Integer = 480, Optional gifFps As Integer = 20,
                        Optional progress As Action(Of Integer, Integer) = Nothing, Optional ct As Threading.CancellationToken = Nothing)
        Dim sz = VideoExport.GifSize(Width, Height, gifSide)
        VideoExport.WriteGif(path, sz.W, sz.H, Frames(timing, gifFps), progress, ct)
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        _a?.Dispose()
        _b?.Dispose()
    End Sub
End Class
