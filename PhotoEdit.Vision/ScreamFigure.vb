Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports OpenCvSharp
Imports OpenCvSharp.Extensions

''' <summary>接上原作身體的人物：Image 不透明，Alpha 是人的範圍，Faces 是臉在這張圖上的位置。</summary>
Public NotInheritable Class ScreamFigureResult
    Public Property Image As Bitmap
    Public Property Alpha As Byte()
    Public Property Faces As List(Of FaceRegion)
    ''' <summary>人物站在原畫裡的位置（畫布比例；背景是內建的原畫時用）。</summary>
    Public Property Spot As ScreamPlacement
End Class

''' <summary>
''' 把人頭接到孟克《吶喊》原作的手和身體上：手與身體從原作描下來（backdrops\scream_original.jpg），
''' 臉的下顎線用薄板樣條貼到手掌內緣，疊圖順序＝身體 → 頭 → 手，手的色調往臉靠，最後整個人畫一次油畫統一質感。
''' 座標都是原作的參考座標（832×1216）；原作檔案只要畫面範圍相同，解析度高低都會自動換算。
''' 內建背景 scream.webp 是同一張畫（人物拿掉），畫面範圍也相同，人物可以站回原畫的位置。
''' </summary>
Public NotInheritable Class ScreamFigure
    Private Sub New()
    End Sub

    Private Const RefW As Integer = 832, RefH As Integer = 1216
    ' 人物的範圍（參考座標；上面留空間給頭髮）
    Private Const X0 As Integer = 340, Y0 As Integer = 530, CW As Integer = 270, CH As Integer = 665

    Private Shared ReadOnly LeftHand As Single(,) = {{414, 645}, {405, 655}, {402, 680}, {404, 702}, {411, 722}, {419, 745}, {427, 770}, {431, 800}, {431, 825}, {429, 846}, {436, 860},
                                                     {445, 846}, {451, 826}, {452, 800}, {452, 772}, {451, 752}, {443, 736}, {434, 718}, {428, 700}, {425, 680}, {426, 655}, {421, 645}}
    Private Shared ReadOnly RightHand As Single(,) = {{562, 644}, {571, 658}, {575, 690}, {572, 712}, {561, 735}, {553, 755}, {552, 778}, {556, 800}, {563, 816}, {562, 836}, {553, 838},
                                                      {545, 812}, {536, 790}, {529, 770}, {529, 748}, {536, 730}, {545, 712}, {552, 692}, {556, 670}, {556, 650}}
    ' 身體上緣墊到頭的後面，新的下巴比原作短也不會有空隙
    Private Shared ReadOnly Body As Single(,) = {{452, 748}, {462, 742}, {491, 735}, {520, 742}, {530, 750}, {537, 792}, {548, 812}, {564, 818}, {578, 850}, {588, 890}, {591, 930},
                                                 {586, 960}, {570, 985}, {546, 996}, {530, 1012}, {512, 1058}, {498, 1108}, {491, 1142}, {496, 1170}, {500, 1190}, {470, 1194}, {430, 1190}, {385, 1192},
                                                 {372, 1160}, {367, 1110}, {377, 1060}, {391, 1016}, {385, 1004}, {367, 1000}, {356, 984}, {354, 950}, {358, 900}, {365, 850}, {372, 815}, {382, 790},
                                                 {398, 770}, {418, 771}, {432, 775}}
    ' 下顎 17 點貼到手掌內緣（左耳高度 → 下巴 → 右耳高度）
    Private Shared ReadOnly Jaw As Single(,) = {{421, 664}, {422, 684}, {426, 702}, {433, 718}, {442, 733}, {450, 749}, {458, 765}, {470, 782}, {492, 802},
                                                {514, 784}, {526, 767}, {533, 751}, {540, 735}, {549, 718}, {556, 702}, {560, 684}, {561, 664}}
    ' 頭的上半部（頭髮）的範圍：手的上方圓頂，往下收進手後面，不會有一條切平的線
    Private Shared ReadOnly Dome As Single(,) = {{418, 700}, {408, 660}, {404, 620}, {412, 585}, {432, 555}, {462, 538}, {492, 533}, {522, 538}, {552, 555}, {572, 585}, {580, 620}, {576, 660}, {566, 700}, {492, 815}}

    ''' <summary>程式附的原作（backdrops\scream_original.jpg，奧斯陸國家博物館 NG.M.00939，公有領域）。</summary>
    Public Shared ReadOnly Property ArtPath As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "backdrops", "scream_original.jpg")
        End Get
    End Property

    Public Shared ReadOnly Property Available As Boolean
        Get
            Return File.Exists(ArtPath)
        End Get
    End Property

    ''' <summary>
    ''' 做出接上原作身體的人物。source＝人物照片，mask＝去背遮罩（和 source 同大小）。
    ''' 沒有原作檔、沒有臉或沒有臉部特徵點時回傳 Nothing。
    ''' </summary>
    Public Shared Function Build(source As Bitmap, mask As Byte(), faces As IReadOnlyList(Of FaceRegion), scream As Integer, eyes As Integer, shape As ScreamShape, oil As Integer) As ScreamFigureResult
        If Not Available OrElse faces Is Nothing Then Return Nothing
        Dim face = faces.Where(Function(f) f.Dense IsNot Nothing).OrderByDescending(Function(f) f.Box.Width * f.Box.Height).FirstOrDefault()
        If face Is Nothing Then Return Nothing
        Dim w = source.Width, h = source.Height
        Dim one As IReadOnlyList(Of FaceRegion) = New List(Of FaceRegion) From {face}
        Dim p = PortraitRetouch.ScreamLandmarks(face, w, h, scream / 100.0, shape)
        Dim standard = PortraitRetouch.ScreamLandmarks(face, w, h, scream / 100.0, New ScreamShape())
        Dim warped = ScreamStudio.WarpMask(mask, w, h, one, scream, shape)
        Using art = Cv2.ImDecode(File.ReadAllBytes(ArtPath), ImreadModes.Color)
            Dim k = art.Width / CDbl(RefW)
            ' 輸出比原作的人物大一點（低解析度時靠油畫筆觸補），高解析度時照原作
            Dim s = Math.Max(3, Math.Min(6, CInt(Math.Round(k))))
            Dim ow = CW * s, oh = CH * s
            Using crop As New Mat(art, New Rect(CInt(X0 * k), CInt(Y0 * k), CInt(CW * k), CInt(CH * k))), big As New Mat(),
                  bodyM = MaskOf(Poly(Body, s), ow, oh, s), leftM = MaskOf(Poly(LeftHand, s), ow, oh, s), rightM = MaskOf(Poly(RightHand, s), ow, oh, s), handsM As New Mat(),
                  domeM = MaskOf(Poly(Dome, s), ow, oh, s), fx = ScreamStudio.FaceEffect(source, one, scream, eyes, shape), bgra = BitmapConverter.ToMat(fx), head As New Mat(),
                  region As New Mat(h, w, MatType.CV_8UC1, Scalar.All(0)), headA As New Mat(h, w, MatType.CV_8UC1), warpedC As New Mat(), warpedA As New Mat()
                Cv2.Resize(crop, big, New OpenCvSharp.Size(ow, oh), 0, 0, If(s < k, InterpolationFlags.Area, InterpolationFlags.Lanczos4))
                Cv2.Max(leftM, rightM, handsM)
                ' 頭的範圍：下顎線以內＋耳朵高度以上（下面的長髮、脖子不要）
                Dim contour = Enumerable.Range(0, 17).Select(Function(i) New OpenCvSharp.Point(CInt(p(i).X), CInt(p(i).Y))).ToList()
                contour.Add(New OpenCvSharp.Point(w - 1, CInt(p(16).Y))) : contour.Add(New OpenCvSharp.Point(w - 1, 0))
                contour.Add(New OpenCvSharp.Point(0, 0)) : contour.Add(New OpenCvSharp.Point(0, CInt(p(0).Y)))
                Cv2.FillPoly(region, {contour.ToArray()}, Scalar.All(255), LineTypes.AntiAlias)
                Cv2.GaussianBlur(region, region, New OpenCvSharp.Size(0, 0), Math.Max(1.0, w * 0.002))
                Marshal.Copy(warped, 0, headA.Data, warped.Length)
                Cv2.Min(headA, region, headA)
                ' 控制點：下顎、眼角、鼻、嘴、頭頂
                Dim dst As New List(Of PointF), src As New List(Of PointF)
                Dim add = Sub(q As PointF, x As Single, y As Single)
                              src.Add(q) : dst.Add(New PointF((x - X0) * s, (y - Y0) * s))
                          End Sub
                For i = 0 To 16
                    add(p(i), Jaw(i, 0), Jaw(i, 1))
                Next
                ' 眼睛：眼角拉平；眼皮和虹膜照變形後的比例放進原作的眼睛（比例寫死的話，眼睛大小的滑桿會沒有作用）
                add(p(36), 444, 673) : add(p(39), 474, 674) : add(p(42), 508, 674) : add(p(45), 538, 673)
                For Each e In {(A:=36, B:=39, U1:=37, U2:=38, L1:=40, L2:=41, X:=459.0F), (A:=42, B:=45, U1:=43, U2:=44, L1:=46, L2:=47, X:=523.0F)}
                    Dim dx = (p(e.B).X - p(e.A).X) * 0.22F, dy = (p(e.B).Y - p(e.A).Y) * 0.22F
                    Dim cx = (p(e.A).X + p(e.B).X) / 2, cy = (p(e.A).Y + p(e.B).Y) / 2
                    Dim up As New PointF((p(e.U1).X + p(e.U2).X) / 2, (p(e.U1).Y + p(e.U2).Y) / 2)
                    Dim lo As New PointF((p(e.L1).X + p(e.L2).X) / 2, (p(e.L1).Y + p(e.L2).Y) / 2)
                    Dim eyeW = Math.Max(1.0, Math.Sqrt((p(e.B).X - p(e.A).X) ^ 2 + (p(e.B).Y - p(e.A).Y) ^ 2))
                    Dim halfH = CSng(15 * Math.Sqrt((lo.X - up.X) ^ 2 + (lo.Y - up.Y) ^ 2) / eyeW)
                    add(up, e.X, 674 - halfH) : add(lo, e.X, 674 + halfH)
                    add(New PointF(cx - dx, cy - dy), e.X - 6.6F, 674)
                    add(New PointF(cx + dx, cy + dy), e.X + 6.6F, 674)
                Next
                ' 嘴：標準大小對到原作的嘴（722～770），其他大小照比例，不超出下巴；寬度跟著臉走（指定嘴角會和手的位置打架，嘴變菱形）
                Dim gap = Function(q As PointF(), a As Integer, b As Integer) Math.Max(1.0, Math.Sqrt((q(b).X - q(a).X) ^ 2 + (q(b).Y - q(a).Y) ^ 2))
                Dim mouthBottom = CSng(722 + Math.Min(60.0, 48 * gap(p, 51, 57) / gap(standard, 51, 57)))
                add(p(30), 491, 702) : add(p(51), 491, 722) : add(p(57), 491, mouthBottom)
                add(New PointF(p(27).X + (p(27).X - p(8).X) * 0.62F, p(27).Y + (p(27).Y - p(8).Y) * 0.62F), 491, 600)
                Dim maps = Tps(dst.ToArray(), src.ToArray(), ow, oh)
                Using mapX = maps.X, mapY = maps.Y
                    Cv2.CvtColor(bgra, head, ColorConversionCodes.BGRA2BGR)
                    Cv2.Remap(head, warpedC, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Replicate)
                    Cv2.Remap(headA, warpedA, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0))
                End Using
                Cv2.Min(warpedA, domeM, warpedA)
                ' 手偏綠、臉偏暖：手的色調往臉靠
                Using warmHands = big.Clone(), canvas = big.Clone(), alpha As New Mat()
                    MatchTone(warmHands, handsM, warpedC, warpedA, 0.75)
                    ' 畫作當底：透明的地方顏色也有定義，油畫的筆才不會吃到黑邊
                    Blend(canvas, warpedC, warpedA)
                    Blend(canvas, warmHands, handsM)
                    Cv2.Max(bodyM, warpedA, alpha)
                    Cv2.Max(alpha, handsM, alpha)
                    Dim headBox As New FaceRegion With {.Box = New RectangleF((425 - X0) / CSng(CW), (640 - Y0) / CSng(CH), 135 / CSng(CW), 165 / CSng(CH))}
                    Dim faceList As New List(Of FaceRegion) From {headBox}
                    Dim image = ToArgb(canvas, ow, oh)
                    ScreamOilPainter.Paint(image, oil, faceList)
                    Dim a(ow * oh - 1) As Byte
                    Marshal.Copy(alpha.Data, a, 0, a.Length)
                    Dim b = ScreamStudio.MaskBounds(a, ow, oh)
                    Dim spot As New ScreamPlacement With {.Name = Lang.T("原畫的位置"), .X = (X0 + (b.X + b.Width / 2.0) / s) / RefW,
                                                          .Y = (Y0 + (b.Y + b.Height / 2.0) / s) / RefH, .Size = b.Height / CDbl(s) / RefH}
                    Return New ScreamFigureResult With {.Image = image, .Alpha = a, .Faces = faceList, .Spot = spot}
                End Using
            End Using
        End Using
    End Function

    ''' <summary>參考座標的多邊形換成輸出像素，角切圓兩次（Chaikin），輪廓才不會有折角。</summary>
    Private Shared Function Poly(a As Single(,), s As Integer) As OpenCvSharp.Point()
        Dim pts = Enumerable.Range(0, a.GetLength(0)).Select(Function(i) New PointF(a(i, 0), a(i, 1))).ToList()
        For pass = 1 To 2
            Dim nxt As New List(Of PointF)
            For i = 0 To pts.Count - 1
                Dim p = pts(i), q = pts((i + 1) Mod pts.Count)
                nxt.Add(New PointF(p.X * 0.75F + q.X * 0.25F, p.Y * 0.75F + q.Y * 0.25F))
                nxt.Add(New PointF(p.X * 0.25F + q.X * 0.75F, p.Y * 0.25F + q.Y * 0.75F))
            Next
            pts = nxt
        Next
        Return pts.Select(Function(p) New OpenCvSharp.Point(CInt((p.X - X0) * s), CInt((p.Y - Y0) * s))).ToArray()
    End Function

    Private Shared Function MaskOf(p As OpenCvSharp.Point(), w As Integer, h As Integer, s As Integer) As Mat
        Dim m As New Mat(h, w, MatType.CV_8UC1, Scalar.All(0))
        Cv2.FillPoly(m, {p}, Scalar.All(255), LineTypes.AntiAlias)
        Cv2.GaussianBlur(m, m, New OpenCvSharp.Size(0, 0), 0.75 * s)
        Return m
    End Function

    ''' <summary>薄板樣條：dst 點 → src 點；回傳每個輸出像素的來源座標（粗格子算完再放大）。對角線加一點，不用剛好穿過每一點，五官才不會皺。</summary>
    Private Shared Function Tps(dst As PointF(), src As PointF(), w As Integer, h As Integer) As (X As Mat, Y As Mat)
        Dim n = dst.Length, m = n + 3
        Dim u = Function(d2 As Double) If(d2 < 0.0000001, 0.0, d2 * Math.Log(d2))
        Dim a(m - 1, m + 1) As Double
        For i = 0 To n - 1
            For j = 0 To n - 1
                a(i, j) = u((dst(i).X - dst(j).X) ^ 2 + (dst(i).Y - dst(j).Y) ^ 2)
            Next
            a(i, i) += 20
            a(i, n) = 1 : a(i, n + 1) = dst(i).X : a(i, n + 2) = dst(i).Y
            a(n, i) = 1 : a(n + 1, i) = dst(i).X : a(n + 2, i) = dst(i).Y
            a(i, m) = src(i).X : a(i, m + 1) = src(i).Y
        Next
        For col = 0 To m - 1
            Dim best = col
            For r = col + 1 To m - 1
                If Math.Abs(a(r, col)) > Math.Abs(a(best, col)) Then best = r
            Next
            For k = 0 To m + 1
                Dim t = a(col, k) : a(col, k) = a(best, k) : a(best, k) = t
            Next
            For r = 0 To m - 1
                If r = col Then Continue For
                Dim f = a(r, col) / a(col, col)
                For k = col To m + 1
                    a(r, k) -= f * a(col, k)
                Next
            Next
        Next
        Dim wx(m - 1) As Double, wy(m - 1) As Double
        For i = 0 To m - 1
            wx(i) = a(i, m) / a(i, i) : wy(i) = a(i, m + 1) / a(i, i)
        Next
        Const G = 6
        Dim gw = w \ G + 2, gh = h \ G + 2
        Dim bx(gw * gh - 1) As Single, by(gw * gh - 1) As Single
        Threading.Tasks.Parallel.For(0, gh, Sub(gy)
                                                For gx = 0 To gw - 1
                                                    Dim x = gx * G, y = gy * G
                                                    Dim vx = wx(n) + wx(n + 1) * x + wx(n + 2) * y, vy = wy(n) + wy(n + 1) * x + wy(n + 2) * y
                                                    For i = 0 To n - 1
                                                        Dim k = u((x - dst(i).X) ^ 2 + (y - dst(i).Y) ^ 2)
                                                        vx += wx(i) * k : vy += wy(i) * k
                                                    Next
                                                    bx(gy * gw + gx) = CSng(vx) : by(gy * gw + gx) = CSng(vy)
                                                Next
                                            End Sub)
        Dim fx As New Mat(), fy As New Mat()
        Using sx As New Mat(gh, gw, MatType.CV_32FC1), sy As New Mat(gh, gw, MatType.CV_32FC1), rx As New Mat(), ry As New Mat()
            Marshal.Copy(bx, 0, sx.Data, bx.Length) : Marshal.Copy(by, 0, sy.Data, by.Length)
            Cv2.Resize(sx, rx, New OpenCvSharp.Size(gw * G, gh * G), 0, 0, InterpolationFlags.Linear)
            Cv2.Resize(sy, ry, New OpenCvSharp.Size(gw * G, gh * G), 0, 0, InterpolationFlags.Linear)
            Using cx As New Mat(rx, New Rect(0, 0, w, h)), cy As New Mat(ry, New Rect(0, 0, w, h))
                cx.CopyTo(fx) : cy.CopyTo(fy)
            End Using
        End Using
        Return (fx, fy)
    End Function

    Private Shared Function ToArgb(bgr As Mat, w As Integer, h As Integer) As Bitmap
        Using a As New Mat()
            Cv2.CvtColor(bgr, a, ColorConversionCodes.BGR2BGRA)
            Using b = BitmapConverter.ToBitmap(a)
                Dim c As New Bitmap(w, h, PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(c)
                    g.DrawImageUnscaled(b, 0, 0)
                End Using
                Return c
            End Using
        End Using
    End Function

    ''' <summary>dst ＝ dst ＋ (src − dst) × alpha。</summary>
    Private Shared Sub Blend(dst As Mat, src As Mat, alpha As Mat)
        Dim n = dst.Rows * dst.Cols
        Dim d(n * 3 - 1) As Byte, s2(n * 3 - 1) As Byte, a(n - 1) As Byte
        Marshal.Copy(dst.Data, d, 0, d.Length) : Marshal.Copy(src.Data, s2, 0, s2.Length) : Marshal.Copy(alpha.Data, a, 0, a.Length)
        Threading.Tasks.Parallel.For(0, n, Sub(i)
                                               Dim k = a(i) / 255.0
                                               For c = 0 To 2
                                                   d(i * 3 + c) = CByte(d(i * 3 + c) + (s2(i * 3 + c) - CInt(d(i * 3 + c))) * k)
                                               Next
                                           End Sub)
        Marshal.Copy(d, 0, dst.Data, d.Length)
    End Sub

    ''' <summary>image（imageA 範圍）的 a、b 平均往 target（targetA 範圍）移 amount。</summary>
    Private Shared Sub MatchTone(image As Mat, imageA As Mat, target As Mat, targetA As Mat, amount As Double)
        Using fi As New Mat(), li As New Mat(), ft As New Mat(), lt As New Mat(), solid As New Mat(), back As New Mat()
            image.ConvertTo(fi, MatType.CV_32FC3, 1 / 255.0) : Cv2.CvtColor(fi, li, ColorConversionCodes.BGR2Lab)
            target.ConvertTo(ft, MatType.CV_32FC3, 1 / 255.0) : Cv2.CvtColor(ft, lt, ColorConversionCodes.BGR2Lab)
            Cv2.Threshold(imageA, solid, 200, 255, ThresholdTypes.Binary)
            Dim mi = Cv2.Mean(li, solid), mt = Cv2.Mean(lt, targetA)
            Dim da = CSng((mt.Val1 - mi.Val1) * amount), db = CSng((mt.Val2 - mi.Val2) * amount)
            Dim px(li.Rows * li.Cols * 3 - 1) As Single
            Marshal.Copy(li.Data, px, 0, px.Length)
            For i = 0 To px.Length - 3 Step 3
                px(i + 1) += da : px(i + 2) += db
            Next
            Marshal.Copy(px, 0, li.Data, px.Length)
            Cv2.CvtColor(li, back, ColorConversionCodes.Lab2BGR)
            back.ConvertTo(image, MatType.CV_8UC3, 255)
        End Using
    End Sub
End Class
