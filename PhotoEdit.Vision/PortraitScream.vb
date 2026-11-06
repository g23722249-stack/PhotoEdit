Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Threading.Tasks
Imports OpenCvSharp

''' <summary>
''' 孟克《吶喊》（1893）：
''' 臉（Scream，需要 68 點）＝兩頰往內凹（像雙手捧著臉）、下巴與頭頂拉長、嘴張開（上唇和鼻子不動，只有下唇與下巴往下）、眼睛瞪大，
''' 膚色換成暖橘黃（像油畫裡被夕陽照到的臉）、眼窩一圈暗，再依本人的唇色畫出張開的嘴（上下唇、暗色口腔、上排牙齒、舌頭）；
''' 背景（ScreamScene）＝整張照片沿著波浪扭動，上半部換成橘紅黃的天空色帶、下半部深藍的峽灣，再加上順著波浪的筆觸，臉保留大部分。
''' 長度都以臉寬或照片長邊為準，預覽和全尺寸匯出的樣子一樣。
''' </summary>
Partial Public NotInheritable Class PortraitRetouch

    Private Shared Function Dist(a As PointF, b As PointF) As Double
        Return Math.Sqrt((a.X - b.X) ^ 2 + (a.Y - b.Y) ^ 2)
    End Function

    Private Shared Function Mid(a As PointF, b As PointF) As PointF
        Return New PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2)
    End Function

    ''' <summary>《吶喊》臉型變形的控制點（p 是 68 點的像素座標，amount 0..1）。位移都小於半徑，不會折疊。</summary>
    Friend Shared Function ScreamControls(p As PointF(), amount As Double, shape As ScreamShape) As List(Of (C As PointF, D As PointF, R As Double))
        Dim ctrls As New List(Of (C As PointF, D As PointF, R As Double))()
        Dim faceW = Dist(p(0), p(16))
        Dim top = p(27), chin = p(8)
        Dim ax As Double = chin.X - top.X, ay As Double = chin.Y - top.Y
        Dim faceH = Math.Max(1.0, Math.Sqrt(ax * ax + ay * ay))
        ax /= faceH : ay /= faceH
        Dim add = Sub(q As PointF, dx As Double, dy As Double, r As Double) ctrls.Add((q, New PointF(CSng(dx * amount), CSng(dy * amount)), r))
        ' 往臉的中線的向量
        Dim inward = Function(q As PointF) As PointF
                         Dim t = (q.X - top.X) * ax + (q.Y - top.Y) * ay
                         Return New PointF(CSng(top.X + ax * t - q.X), CSng(top.Y + ay * t - q.Y))
                     End Function
        ' 兩頰往內凹：顴骨下方收最多，下顎跟著收一點
        For Each pr In {(4, 0.3, 0.18), (12, 0.3, 0.18), (6, 0.16, 0.12), (10, 0.16, 0.12)}
            Dim d = inward(p(pr.Item1))
            add(p(pr.Item1), d.X * pr.Item2, d.Y * pr.Item2, faceW * pr.Item3)
        Next
        ' 頭頂往上拉（長長的骷髏臉）；下巴在下面和嘴一起處理
        add(Mid(p(19), p(24)), -ax * faceH * 0.1, -ay * faceH * 0.1, faceW * 0.3)
        PinMouth(p, amount, shape, ctrls)
        Return ctrls
    End Function

    ''' <summary>張開的嘴（變形後的位置）：上唇中央、下唇底、左右嘴角，往下的方向，原本的唇厚。</summary>
    Private Structure ScreamMouthShape
        Public Top, Bottom, Left, Right As PointF
        Public AxisX, AxisY, Drop, LipThickness, Round As Double
    End Structure

    ''' <summary>
    ''' 嘴張開後的形狀。圓度 100：接近圓、稍微高一點的橢圓；圓度 0：正常嘴型，和本人一樣寬、高度約六成。
    ''' 大小 50 是標準，等比例放大縮小。上唇幾乎不動，下唇往下，嘴角跟著移到嘴的中段。
    ''' </summary>
    Private Shared Function ScreamMouth(p As PointF(), amount As Double, shape As ScreamShape) As ScreamMouthShape
        Dim ax As Double = p(8).X - p(27).X, ay As Double = p(8).Y - p(27).Y
        Dim len = Math.Max(1.0, Math.Sqrt(ax * ax + ay * ay))
        ax /= len : ay /= len
        Dim mouthW = Math.Max(4.0, Dist(p(48), p(54)))
        Dim lipH = Math.Max(1.0, (p(57).X - p(51).X) * ax + (p(57).Y - p(51).Y) * ay)
        ' 大小以臉寬為上限：微笑時嘴比較寬，照嘴寬算會張得太大
        Dim round = shape.MouthRound / 100.0, sizeK = 0.6 + 0.8 * shape.MouthSize / 100.0
        Dim roundW = Math.Min(mouthW * 0.76, Dist(p(0), p(16)) * 0.29), normalW = Math.Min(mouthW, Dist(p(0), p(16)) * 0.36)
        Dim openW = (normalW + (roundW - normalW) * round) * sizeK
        Dim openH = openW * (0.6 + 0.52 * round)
        ' 嘴比本人寬時是負的，嘴角往外；往內收太多會把鼻子和臉頰一起拖變形（微笑的嘴本來就寬）
        Dim pinch = Math.Max(-0.6, Math.Min(0.45, 1 - openW / mouthW)) * amount
        Dim drop = Math.Max(0.0, openH - lipH) * amount
        Dim corner = Function(q As PointF)
                         ' 往嘴的中線收（沿著嘴角連線的方向），再往下到嘴的中段
                         Dim c = Mid(p(48), p(54))
                         Return New PointF(CSng(q.X + (c.X - q.X) * pinch + ax * drop * 0.45), CSng(q.Y + (c.Y - q.Y) * pinch + ay * drop * 0.45))
                     End Function
        Return New ScreamMouthShape With {
            .Top = New PointF(CSng(p(51).X - ax * mouthW * 0.03 * amount), CSng(p(51).Y - ay * mouthW * 0.03 * amount)),
            .Bottom = New PointF(CSng(p(57).X + ax * drop), CSng(p(57).Y + ay * drop)),
            .Left = corner(p(48)), .Right = corner(p(54)),
            .AxisX = ax, .AxisY = ay, .Drop = drop, .LipThickness = lipH * 0.34, .Round = round}
    End Function

    ''' <summary>
    ''' 嘴、下巴和眼睛的控制點：高斯相加會互相影響（以前下巴、下唇把上唇和鼻子一起往下拖，人中被拉得很長；
    ''' 臉頰內凹把眼角往下帶，眼睛變下垂），所以解一個小方程組，讓這些點剛好到指定的位置。
    ''' 眼睛：兩個眼角一定拉平（外眼角略高），不隨眼睛大小改變；shape.EyeSize 決定上下眼皮撐開多少（0＝本人原來的大小）。
    ''' 半徑至少要和位移一樣大，嘴張很大時才不會折疊。
    ''' 只往上下拉虹膜會變成細長的貓眼，所以虹膜左右也一起撐開，黑眼珠維持圓形。
    ''' 變形是 dst(q) = src(q − D(q))，所以釘點放在變形後的位置 q，要求 D(q) ＝ 位移。
    ''' </summary>
    Private Shared Sub PinMouth(p As PointF(), amount As Double, shape As ScreamShape, ctrls As List(Of (C As PointF, D As PointF, R As Double)))
        Dim m = ScreamMouth(p, amount, shape)
        Dim eyeSize = shape.EyeSize / 100.0
        Dim mouthW = Math.Max(4.0, Dist(p(48), p(54))), faceW = Dist(p(0), p(16))
        Dim faceH = Math.Max(1.0, Dist(p(27), p(8)))
        Dim chinDrop = m.Drop * 0.8 + faceH * 0.03 * amount
        Dim chin As New PointF(CSng(p(8).X + m.AxisX * chinDrop), CSng(p(8).Y + m.AxisY * chinDrop))
        ' 兩個嘴角靠得比半徑近時方程組的解會爆開（臉被撕裂），所以半徑跟著嘴寬縮小
        Dim cornerR = Math.Max(Math.Min(mouthW * 0.45, Dist(m.Left, m.Right) * 0.6), Math.Max(Dist(m.Left, p(48)), Dist(m.Right, p(54))))
        Dim pins = {(Q:=p(33), Src:=p(33), R:=mouthW * 0.4), (Q:=m.Top, Src:=p(51), R:=mouthW * 0.45),
                    (Q:=m.Left, Src:=p(48), R:=cornerR), (Q:=m.Right, Src:=p(54), R:=cornerR),
                    (Q:=m.Bottom, Src:=p(57), R:=Math.Max(mouthW * 0.5, m.Drop)), (Q:=chin, Src:=p(8), R:=Math.Max(faceW * 0.22, chinDrop))}.ToList()
        ' 眼睛（外眼角、內眼角、上眼皮中點、下眼皮中點；ux 從畫面左到右）
        Dim ux = m.AxisY, uy = -m.AxisX
        If ux < 0 Then ux = -ux : uy = -uy
        For Each e In {(Outer:=36, Inner:=39, Up1:=37, Up2:=38, Lo1:=40, Lo2:=41, Side:=-1.0), (Outer:=45, Inner:=42, Up1:=44, Up2:=43, Lo1:=47, Lo2:=46, Side:=1.0)}
            Dim eyeW = Math.Max(3.0, Dist(p(e.Outer), p(e.Inner)))
            Dim c = Mid(p(e.Outer), p(e.Inner))
            Dim half = eyeW * (0.5 - 0.03 * eyeSize), tall = eyeW * 0.3, lift = eyeW * 0.03
            Dim irisSrc = eyeW * 0.22, irisDst = eyeW * 0.29
            Dim toward = Function(src As PointF, tx As Double, ty As Double, k As Double) New PointF(CSng(src.X + (tx - src.X) * k), CSng(src.Y + (ty - src.Y) * k))
            Dim open = amount * eyeSize
            Dim up = Mid(p(e.Up1), p(e.Up2)), lo = Mid(p(e.Lo1), p(e.Lo2))
            Dim r = eyeW * 0.3
            pins.Add((toward(p(e.Outer), c.X + ux * half * e.Side - m.AxisX * lift, c.Y + uy * half * e.Side - m.AxisY * lift, amount), p(e.Outer), r))
            pins.Add((toward(p(e.Inner), c.X - ux * half * e.Side, c.Y - uy * half * e.Side, amount), p(e.Inner), r))
            pins.Add((toward(up, c.X - m.AxisX * tall, c.Y - m.AxisY * tall, open), up, r))
            pins.Add((toward(lo, c.X + m.AxisX * tall, c.Y + m.AxisY * tall, open), lo, r))
            For Each sgn In {-1.0, 1.0}
                Dim iris As New PointF(CSng(c.X + ux * irisSrc * sgn), CSng(c.Y + uy * irisSrc * sgn))
                pins.Add((toward(iris, c.X + ux * irisDst * sgn, c.Y + uy * irisDst * sgn, open), iris, r))
            Next
        Next
        Dim n = pins.Count
        Dim a(n - 1, n + 1) As Double
        For i = 0 To n - 1
            For j = 0 To n - 1
                a(i, j) = Math.Exp(-((pins(i).Q.X - pins(j).Q.X) ^ 2 + (pins(i).Q.Y - pins(j).Q.Y) ^ 2) / (2 * pins(j).R ^ 2))
            Next
            a(i, i) += 0.001 ' 釘點很靠近時不會解出很大的值
            Dim ox = 0.0, oy = 0.0
            For Each c In ctrls
                Dim g = Math.Exp(-((pins(i).Q.X - c.C.X) ^ 2 + (pins(i).Q.Y - c.C.Y) ^ 2) / (2 * c.R * c.R))
                ox += c.D.X * g : oy += c.D.Y * g
            Next
            a(i, n) = pins(i).Q.X - pins(i).Src.X - ox
            a(i, n + 1) = pins(i).Q.Y - pins(i).Src.Y - oy
        Next
        ' 高斯消去（部分選主元），兩個方向一起解
        For col = 0 To n - 1
            Dim best = col
            For r = col + 1 To n - 1
                If Math.Abs(a(r, col)) > Math.Abs(a(best, col)) Then best = r
            Next
            If Math.Abs(a(best, col)) < 0.000001 Then Return
            For k = 0 To n + 1
                Dim t = a(col, k) : a(col, k) = a(best, k) : a(best, k) = t
            Next
            For r = 0 To n - 1
                If r = col Then Continue For
                Dim f = a(r, col) / a(col, col)
                For k = col To n + 1
                    a(r, k) -= f * a(col, k)
                Next
            Next
        Next
        For i = 0 To n - 1
            ctrls.Add((pins(i).Q, New PointF(CSng(a(i, n) / a(i, i)), CSng(a(i, n + 1) / a(i, i))), pins(i).R))
        Next
    End Sub

    ''' <summary>《吶喊》的臉變形後，68 點在哪裡（像素座標）。</summary>
    Friend Shared Function ScreamLandmarks(face As FaceRegion, w As Integer, h As Integer, amount As Double, shape As ScreamShape) As PointF()
        Dim p0 = face.Dense.Select(Function(q) New PointF(q.X * w, q.Y * h)).ToArray()
        Dim ctrls = ScreamControls(p0, amount, shape)
        Return p0.Select(Function(q) Displaced(q, ctrls)).ToArray()
    End Function

    ''' <summary>變形後某一點移到哪裡：解 q − D(q) = p（dst(q) = src(q − D(q))），疊代幾次就夠準。</summary>
    Private Shared Function Displaced(p As PointF, ctrls As List(Of (C As PointF, D As PointF, R As Double))) As PointF
        Dim q = p
        For iter = 1 To 6
            Dim dx = 0.0, dy = 0.0
            For Each c In ctrls
                Dim g = Math.Exp(-((q.X - c.C.X) ^ 2 + (q.Y - c.C.Y) ^ 2) / (2 * c.R * c.R))
                dx += c.D.X * g : dy += c.D.Y * g
            Next
            q = New PointF(CSng(p.X + dx), CSng(p.Y + dy))
        Next
        Return q
    End Function

    ''' <summary>《吶喊》的臉（變形之後畫）：膚色換成暖橘黃、眼窩一圈暗（eyeDark 0..1 深淺），再畫張開的嘴。ctrls 是這張臉變形用的控制點（算五官移到哪裡）。</summary>
    Private Shared Sub PaintScreamFace(px As Byte(), stride As Integer, w As Integer, h As Integer, dense As PointF(),
                                       ctrls As List(Of (C As PointF, D As PointF, R As Double)), amount As Double, eyeDark As Double, shape As ScreamShape)
        Dim p0 = dense.Select(Function(q) New PointF(q.X * w, q.Y * h)).ToArray()
        Dim p = p0.Select(Function(q) Displaced(q, ctrls)).ToArray()
        Dim faceW = Dist(p0(0), p0(16))
        If faceW < 12 Then Return
        Dim top = p0(27), chin = p0(8)
        Dim ax As Double = chin.X - top.X, ay As Double = chin.Y - top.Y
        Dim faceH = Math.Max(1.0, Math.Sqrt(ax * ax + ay * ay))
        ax /= faceH : ay /= faceH

        ' 臉的範圍：變形後的下顎線＋眉毛往上推到額頭（頭頂也拉長了）
        Dim outline As New List(Of PointF)(p.Take(17))
        For i = 26 To 17 Step -1
            outline.Add(New PointF(CSng(p(i).X - ax * faceH * 0.55), CSng(p(i).Y - ay * faceH * 0.55)))
        Next
        Dim bx0 = Math.Max(0, CInt(outline.Min(Function(q) q.X) - faceW * 0.15)), by0 = Math.Max(0, CInt(outline.Min(Function(q) q.Y) - faceW * 0.15))
        Dim bx1 = Math.Min(w - 1, CInt(outline.Max(Function(q) q.X) + faceW * 0.15)), by1 = Math.Min(h - 1, CInt(outline.Max(Function(q) q.Y) + faceW * 0.15))
        If bx1 - bx0 < 4 OrElse by1 - by0 < 4 Then Return
        Dim rw = bx1 - bx0 + 1, rh = by1 - by0 + 1
        Dim mask(rw * rh - 1) As Byte
        Using m As New Mat(rh, rw, MatType.CV_8UC1, Scalar.All(0))
            Cv2.FillPoly(m, {outline.Select(Function(q) New OpenCvSharp.Point(CInt(q.X - bx0), CInt(q.Y - by0))).ToArray()}, Scalar.All(255), LineTypes.AntiAlias)
            Cv2.GaussianBlur(m, m, New OpenCvSharp.Size(0, 0), Math.Max(1.0, faceW * 0.06))
            Runtime.InteropServices.Marshal.Copy(m.Data, mask, 0, mask.Length)
        End Using

        ' 眼窩、嘴巴（變形後的位置）
        Dim eyes = {(C:=Avg(p, 36, 41), W:=Dist(p(36), p(39))), (C:=Avg(p, 42, 47), W:=Dist(p(42), p(45)))}
        ' 唇色在換膚色之前取（下唇中間，已經變形過）
        Dim lip = SampleLip(px, stride, w, h, Displaced(Mid(p0(57), p0(66)), ctrls), Math.Max(1, CInt(faceW * 0.015)))

        Parallel.For(by0, by1 + 1, Sub(y)
                                       For x = bx0 To bx1
                                           Dim mk = mask((y - by0) * rw + (x - bx0)) / 255.0
                                           Dim i = y * stride + x * 4
                                           Dim b As Double = px(i), g As Double = px(i + 1), r As Double = px(i + 2)
                                           ' 膚色：依亮度對到暖色的顏色表（暗＝赭褐、中＝橘黃、亮＝淡黃）
                                           If mk > 0.004 Then
                                               Dim lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255
                                               lum = Smooth(lum, 0.08, 0.92) * 0.85 + lum * 0.15 ' 對比強一點，像平塗的色塊
                                               Dim pr, pg, pb As Double
                                               ScreamFacePalette(lum, pr, pg, pb)
                                               ' 眼睛裡面保留本人的顏色（眼白、虹膜）
                                               Dim keep = 1.0
                                               For Each e In eyes
                                                   Dim qx = (x - e.C.X) / (e.W * 0.56), qy = (y - e.C.Y) / (e.W * 0.5)
                                                   keep = Math.Min(keep, Smooth(Math.Sqrt(qx * qx + qy * qy), 0.75, 1.1))
                                               Next
                                               Dim k = mk * amount * 0.85 * keep
                                               r += (pr - r) * k : g += (pg - g) * k : b += (pb - b) * k
                                           End If
                                           ' 眼窩：眼睛外面一圈暗（瞪大的黑眼圈）
                                           For Each e In eyes
                                               Dim ex = (x - e.C.X) / (e.W * 0.85), ey = (y - e.C.Y) / (e.W * 0.68)
                                               Dim re = Math.Sqrt(ex * ex + ey * ey)
                                               If re > 1.8 OrElse eyeDark <= 0 Then Continue For
                                               Dim ring = Math.Exp(-((re - 0.95) / 0.3) ^ 2) * amount * eyeDark * 0.85
                                               r += (78 - r) * ring : g += (46 - g) * ring : b += (30 - b) * ring
                                           Next
                                           px(i) = ImagePipeline.ClampByte(b) : px(i + 1) = ImagePipeline.ClampByte(g) : px(i + 2) = ImagePipeline.ClampByte(r)
                                       Next
                                   End Sub)
        PaintScreamMouth(px, stride, w, h, ScreamMouth(p0, amount, shape), amount, lip)
    End Sub

    ''' <summary>下唇的顏色（BGR 平均），偏向深紅（照片的唇色常常很淡，畫上去會和膚色混在一起）。</summary>
    Private Shared Function SampleLip(px As Byte(), stride As Integer, w As Integer, h As Integer, c As PointF, r As Integer) As (R As Double, G As Double, B As Double)
        Dim sr = 0.0, sg = 0.0, sb = 0.0, n = 0
        For y = Math.Max(0, CInt(c.Y) - r) To Math.Min(h - 1, CInt(c.Y) + r)
            For x = Math.Max(0, CInt(c.X) - r) To Math.Min(w - 1, CInt(c.X) + r)
                Dim i = y * stride + x * 4
                sb += px(i) : sg += px(i + 1) : sr += px(i + 2)
                n += 1
            Next
        Next
        If n = 0 Then Return (142, 46, 42)
        Return (sr / n * 0.3 + 142 * 0.7, sg / n * 0.3 + 46 * 0.7, sb / n * 0.3 + 42 * 0.7)
    End Function

    ''' <summary>
    ''' 畫張開的嘴（參考 PhotoSkrik 生成的嘴）：有唇峰的上唇、較厚有高光的下唇、暗色口腔、一排上牙、舌頭，嘴的外圍一圈淡淡的陰影。
    ''' 畫在變形後的位置，蓋住被拉開的原本嘴唇。
    ''' </summary>
    Private Shared Sub PaintScreamMouth(px As Byte(), stride As Integer, w As Integer, h As Integer, m As ScreamMouthShape, amount As Double, lip As (R As Double, G As Double, B As Double))
        Dim opacity = Math.Min(1.0, amount * 2.2)
        Dim halfW = Dist(m.Left, m.Right) * 0.56, halfH = Dist(m.Top, m.Bottom) * 0.53
        If halfW < 2 OrElse halfH < 2 Then Return
        Dim c = Mid(m.Top, m.Bottom)
        Dim ux = CDbl(m.Right.X - m.Left.X), uy = CDbl(m.Right.Y - m.Left.Y)
        Dim ul = Math.Max(1.0, Math.Sqrt(ux * ux + uy * uy))
        ux /= ul : uy /= ul
        Dim vx = m.AxisX, vy = m.AxisY
        ' 口腔：上唇薄、下唇厚一點；唇的厚度和本人一樣，嘴張越大口腔越大
        Dim rx = Math.Max(0.5, halfW - m.LipThickness * 0.3)
        Dim topLip = m.LipThickness * 0.85, bottomLip = m.LipThickness * 1.1
        Dim ry = Math.Max(0.5, halfH - (topLip + bottomLip) / 2)
        Dim shift = (topLip - bottomLip) / 2
        ' 邊緣放柔（和照片的銳利度差不多，不要像貼上去的）
        Dim blur = Math.Max(1.5, Math.Min(halfW, halfH) * 0.05)
        Dim soft = blur / Math.Min(halfW, halfH), softIn = blur / Math.Max(1.0, Math.Min(rx, ry))
        Dim reach = Math.Max(halfW, halfH) * 1.5
        ' 不圓的時候：兩端收窄、嘴角變尖（圓度 100 是原本的橢圓）
        Dim taper = 0.45 * (1 - m.Round), power = 1.8 + 0.6 * m.Round
        Dim x0 = Math.Max(0, CInt(c.X - reach)), x1 = Math.Min(w - 1, CInt(c.X + reach))
        Dim y0 = Math.Max(0, CInt(c.Y - reach)), y1 = Math.Min(h - 1, CInt(c.Y + reach))
        Parallel.For(y0, y1 + 1, Sub(y)
                                     For x = x0 To x1
                                         Dim dx = x - c.X, dy = y - c.Y
                                         Dim lu = dx * ux + dy * uy, lv = dx * vx + dy * vy
                                         Dim nu = lu / halfW, nv = lv / halfH
                                         ' 唇峰：上緣中央往下凹一點
                                         If nv < 0 Then nv /= 1 - 0.1 * Math.Exp(-(nu / 0.17) ^ 2)
                                         nv /= 1 - taper * Math.Min(1.0, nu * nu)
                                         Dim ro = (Math.Abs(nu) ^ power + Math.Abs(nv) ^ power) ^ (1 / power)
                                         If ro > 1.5 Then Continue For
                                         Dim i = y * stride + x * 4
                                         Dim b As Double = px(i), g As Double = px(i + 1), r As Double = px(i + 2)
                                         ' 嘴外圍的陰影（下唇下面比較深）
                                         Dim shade = Math.Exp(-((ro - 1.1) / 0.2) ^ 2) * (0.1 + 0.08 * Smooth(nv, -0.3, 0.6)) * opacity
                                         r *= 1 - shade : g *= 1 - shade : b *= 1 - shade
                                         ' 嘴唇
                                         Dim lipA = (1 - Smooth(ro, 1 - soft, 1 + soft)) * opacity
                                         If lipA > 0 Then
                                             Dim k = (0.78 + 0.2 * Smooth(nv, -0.35, 0.35)) * (1 - 0.3 * Math.Abs(nu) ^ 3)
                                             Dim shine = 12 * Math.Exp(-(((nv - 0.84) / 0.12) ^ 2 + (nu / 0.5) ^ 2))
                                             r += (lip.R * k + shine - r) * lipA
                                             g += (lip.G * k + shine - g) * lipA
                                             b += (lip.B * k + shine - b) * lipA
                                         End If
                                         ' 口腔
                                         Dim cu = lu / rx, cv = (lv - shift) / ry
                                         cv /= 1 - taper * Math.Min(1.0, cu * cu)
                                         Dim rc = Math.Sqrt(cu * cu + cv * cv)
                                         Dim inA = (1 - Smooth(rc, 1 - softIn, 1 + softIn)) * opacity
                                         If inA > 0 Then
                                             Dim depth = Math.Max(0, Math.Min(1, (cv + 1) / 2))
                                             Dim cr = 24 + 30 * depth, cg = 10 + 8 * depth, cb = 13 + 9 * depth
                                             ' 舌頭
                                             Dim tongue = 1 - Smooth(Math.Sqrt((cu / 0.66) ^ 2 + ((cv - 0.95) / 0.5) ^ 2), 0.5, 1.1)
                                             cr += (88 - cr) * tongue * 0.7 : cg += (30 - cg) * tongue * 0.7 : cb += (34 - cb) * tongue * 0.7
                                             ' 上排牙齒：貼著上唇的一排，下緣是往上彎的弧
                                             Dim edge = -0.6 - 0.25 * cu * cu
                                             Dim tooth = (1 - Smooth(cv, edge - 0.09, edge + 0.09)) * (1 - Smooth(Math.Abs(cu), 0.5, 0.75)) * 0.85
                                             Dim tk = 1 - 0.3 * cu * cu
                                             cr += (196 * tk - cr) * tooth : cg += (184 * tk - cg) * tooth : cb += (158 * tk - cb) * tooth
                                             r += (cr - r) * inA : g += (cg - g) * inA : b += (cb - b) * inA
                                         End If
                                         px(i) = ImagePipeline.ClampByte(b) : px(i + 1) = ImagePipeline.ClampByte(g) : px(i + 2) = ImagePipeline.ClampByte(r)
                                     Next
                                 End Sub)
    End Sub

    Private Shared Function Avg(p As PointF(), first As Integer, last As Integer) As PointF
        Dim n = last - first + 1
        Return New PointF(CSng(p.Skip(first).Take(n).Average(Function(q) q.X)), CSng(p.Skip(first).Take(n).Average(Function(q) q.Y)))
    End Function

    ''' <summary>皮膚的顏色表（RGB）：0 赭褐、0.45 橘黃、0.78 暖黃、1 淡黃。</summary>
    Friend Shared Sub ScreamFacePalette(t As Double, ByRef r As Double, ByRef g As Double, ByRef b As Double)
        Lerp4(t, {72, 42, 28}, {196, 124, 54}, {236, 176, 88}, {250, 224, 164}, {0, 0.45, 0.78, 1}, r, g, b)
    End Sub

    ''' <summary>四個色標之間線性內插（RGB）。</summary>
    Private Shared Sub Lerp4(t As Double, c0 As Double(), c1 As Double(), c2 As Double(), c3 As Double(), at As Double(), ByRef r As Double, ByRef g As Double, ByRef b As Double)
        Dim cs = {c0, c1, c2, c3}
        t = Math.Max(0, Math.Min(1, t))
        Dim k = 0
        While k < 2 AndAlso t > at(k + 1)
            k += 1
        End While
        Dim u = (t - at(k)) / Math.Max(0.0001, at(k + 1) - at(k))
        u = Math.Max(0, Math.Min(1, u))
        r = cs(k)(0) + (cs(k + 1)(0) - cs(k)(0)) * u
        g = cs(k)(1) + (cs(k + 1)(1) - cs(k)(1)) * u
        b = cs(k)(2) + (cs(k + 1)(2) - cs(k)(2)) * u
    End Sub

    Private Shared ReadOnly _backdropLock As New Object()
    Private Shared _backdrop As Mat

    ''' <summary>程式附的《吶喊》背景圖路徑（backdrops\scream.webp）。</summary>
    Public Shared ReadOnly Property ScreamBackdropPath As String
        Get
            Return IO.Path.Combine(AppContext.BaseDirectory, "backdrops", "scream.webp")
        End Get
    End Property

    ''' <summary>背景圖（BGR，讀一次就留著）；沒有檔案或讀不到時 Nothing。</summary>
    Friend Shared Function ScreamBackdropImage() As Mat
        SyncLock _backdropLock
            If _backdrop Is Nothing AndAlso IO.File.Exists(ScreamBackdropPath) Then
                Try
                    Dim m = Cv2.ImDecode(IO.File.ReadAllBytes(ScreamBackdropPath), ImreadModes.Color)
                    If m IsNot Nothing AndAlso Not m.Empty() Then _backdrop = m Else m?.Dispose()
                Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is OpenCVException
                End Try
            End If
            Return _backdrop
        End SyncLock
    End Function

    ''' <summary>
    ''' 《吶喊》的背景：整張照片沿著波浪扭動，依亮度和波浪色帶換成天空（橘紅黃）或峽灣（深藍）的顏色，再加上順著波浪的細筆觸。
    ''' 臉的附近只套一點點（臉另外由 PaintScreamFace 處理）。座標以照片長邊為 1，預覽和全尺寸一樣。
    ''' </summary>
    Private Shared Sub ApplyScreamScene(px As Byte(), stride As Integer, w As Integer, h As Integer, faces As IReadOnlyList(Of FaceRegion), amount As Double)
        Dim src = DirectCast(px.Clone(), Byte())
        Dim L As Double = Math.Max(w, h)
        Dim amp = 0.011 * L * amount
        Dim twoPi = 2 * Math.PI
        ' 只跟 x 或只跟 y 有關的部分先算好
        Dim colA(w - 1) As Double, colC(w - 1) As Double, colF(w - 1) As Double, rowB(h - 1) As Double
        For x = 0 To w - 1
            Dim u = x / L
            colA(x) = Math.Sin(twoPi * u * 1.7 + 0.6)
            colC(x) = Math.Sin(twoPi * u * 1.3 + 2.1)
            colF(x) = Math.Sin(twoPi * u * 7.0)
        Next
        For y = 0 To h - 1
            rowB(y) = Math.Cos(twoPi * (y / L) * 2.3 + 0.4)
        Next
        ' 臉：拉長的橢圓（頭頂也算），裡面只套 20%
        Dim heads = faces.Select(Function(f) (Cx:=(f.Box.X + f.Box.Width / 2) * w, Cy:=(f.Box.Y + f.Box.Height * 0.5) * h,
                                               Rx:=f.Box.Width * w * 0.72, Ry:=f.Box.Height * h * 1.0)).ToArray()

        Parallel.For(0, h, Sub(y)
                               Dim v = y / L, vy = y / CDbl(h)
                               For x = 0 To w - 1
                                   Dim u = x / L
                                   Dim prot = 0.0
                                   For Each hd In heads
                                       Dim ex = (x - hd.Cx) / hd.Rx, ey = (y - hd.Cy) / hd.Ry
                                       prot = Math.Max(prot, 1 - Smooth(Math.Sqrt(ex * ex + ey * ey), 0.75, 1.15))
                                   Next
                                   Dim k = amount * 0.82 * (1 - 0.8 * prot)
                                   Dim a = amp * (1 - 0.7 * prot)
                                   ' 波浪位移
                                   Dim dx = a * Math.Sin(twoPi * v * 5.0 + 2.5 * colA(x))
                                   Dim dy = a * 0.7 * Math.Sin(twoPi * u * 4.0 + 2.0 * rowB(y))
                                   Dim sx = Math.Max(0, Math.Min(w - 1.001, x - dx)), sy = Math.Max(0, Math.Min(h - 1.001, y - dy))
                                   Dim ix = CInt(Math.Floor(sx)), iy = CInt(Math.Floor(sy))
                                   Dim fx = sx - ix, fy = sy - iy
                                   Dim ix1 = Math.Min(w - 1, ix + 1), iy1 = Math.Min(h - 1, iy + 1)
                                   Dim c(2) As Double
                                   For ch = 0 To 2
                                       c(ch) = (src(iy * stride + ix * 4 + ch) * (1 - fx) + src(iy * stride + ix1 * 4 + ch) * fx) * (1 - fy) +
                                               (src(iy1 * stride + ix * 4 + ch) * (1 - fx) + src(iy1 * stride + ix1 * 4 + ch) * fx) * fy
                                   Next
                                   Dim lum = (0.114 * c(0) + 0.587 * c(1) + 0.299 * c(2)) / 255
                                   ' 波浪色帶（天空的紅黃條紋）與順著色帶的細筆觸
                                   Dim phase = v * 7.0 + 0.8 * colC(x) + 0.3 * Math.Sin(twoPi * (u * 2.5 - v * 1.5))
                                   Dim band = 0.5 + 0.5 * Math.Sin(twoPi * phase)
                                   Dim stroke = Math.Sin(twoPi * phase * 5 + 3 * colF(x))
                                   ' 上半部天空、下半部峽灣，交界也是波浪
                                   Dim skyW = 1 - Smooth(vy + 0.05 * colA(x), 0.42, 0.62)
                                   Dim sr, sg, sb, lr, lg, lb As Double
                                   Lerp4(0.55 * lum + 0.45 * band, {110, 25, 20}, {205, 62, 30}, {236, 132, 42}, {246, 204, 96}, {0, 0.35, 0.7, 1}, sr, sg, sb)
                                   Lerp4(0.65 * lum + 0.35 * band, {14, 24, 56}, {38, 66, 118}, {96, 112, 140}, {176, 150, 104}, {0, 0.4, 0.75, 1}, lr, lg, lb)
                                   Dim tex = 1 + 0.05 * stroke
                                   Dim pr = (lr + (sr - lr) * skyW) * tex, pg = (lg + (sg - lg) * skyW) * tex, pb = (lb + (sb - lb) * skyW) * tex
                                   Dim i = y * stride + x * 4
                                   px(i) = ImagePipeline.ClampByte(c(0) + (pb - c(0)) * k)
                                   px(i + 1) = ImagePipeline.ClampByte(c(1) + (pg - c(1)) * k)
                                   px(i + 2) = ImagePipeline.ClampByte(c(2) + (pr - c(2)) * k)
                               Next
                           End Sub)
    End Sub
    ''' <summary>
    ''' 遮罩（0..255，w×h）照《吶喊》的臉型變形（和 Apply 裡同一組控制點），去背後的輪廓才對得上變形過的臉。
    ''' 沒有 68 點的臉不動。回傳新的陣列。
    ''' </summary>
    Public Shared Function ScreamWarpMask(mask As Byte(), w As Integer, h As Integer, faces As IReadOnlyList(Of FaceRegion), amount As Double, shape As ScreamShape) As Byte()
        Dim px(w * h * 4 - 1) As Byte
        For i = 0 To w * h - 1
            Dim v = mask(i)
            px(i * 4) = v : px(i * 4 + 1) = v : px(i * 4 + 2) = v : px(i * 4 + 3) = 255
        Next
        For Each f In faces
            If f.Dense Is Nothing Then Continue For
            Dim p = f.Dense.Select(Function(q) New PointF(q.X * w, q.Y * h)).ToArray()
            If Dist(p(0), p(16)) < 12 Then Continue For
            WarpByControls(px, w * 4, w, h, ScreamControls(p, amount, shape))
        Next
        Dim out(w * h - 1) As Byte
        For i = 0 To w * h - 1
            out(i) = px(i * 4)
        Next
        Return out
    End Function

    ''' <summary>整張圖套一點《吶喊》的波浪色調與筆觸（amount 0..1，臉保留大部分）。直接改 bmp。</summary>
    Public Shared Sub ScreamTint(bmp As Bitmap, faces As IReadOnlyList(Of FaceRegion), amount As Double)
        Dim w = bmp.Width, h = bmp.Height
        Dim d = bmp.LockBits(New Rectangle(0, 0, w, h), Imaging.ImageLockMode.ReadWrite, Imaging.PixelFormat.Format32bppArgb)
        Try
            Dim px(d.Stride * h - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length)
            ApplyScreamScene(px, d.Stride, w, h, faces, amount)
            Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length)
        Finally
            bmp.UnlockBits(d)
        End Try
    End Sub
End Class

''' <summary>《吶喊》的臉可以調的形狀（都是 0..100）。</summary>
Public NotInheritable Class ScreamShape
    ''' <summary>眼睛撐開多少：0＝本人原來的大小。</summary>
    Public Property EyeSize As Integer = 40
    ''' <summary>嘴巴張多大：50 是標準。</summary>
    Public Property MouthSize As Integer = 50
    ''' <summary>嘴巴多圓：0＝正常嘴型，100＝圓形。</summary>
    Public Property MouthRound As Integer = 100

    Friend Shared Function FromBeauty(b As BeautySettings) As ScreamShape
        Return New ScreamShape With {.EyeSize = b.ScreamEyeSize, .MouthSize = b.ScreamMouthSize, .MouthRound = b.ScreamMouthRound}
    End Function
End Class
