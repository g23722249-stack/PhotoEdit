Imports System.Drawing
Imports OpenCvSharp

''' <summary>
''' 戲曲妝（京劇、歌仔戲的俊扮、臉譜、丑角）：原圖解析度直接畫。
''' 圖樣是「標準臉」座標（OperaLayer），用兩眼中心與嘴的中心三點換算成這張臉的仿射轉換；
''' 眼睛、嘴唇相關的層直接用實際的眼睛、嘴唇輪廓。塗色時依原圖亮度保留明暗（像畫在臉上）。
''' </summary>
Partial Public NotInheritable Class PortraitRetouch

    ''' <summary>網格的臉輪廓（額頭頂端起順時針）。</summary>
    Private Shared ReadOnly MeshFaceOval As Integer() = {10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109}

    ''' <summary>臉譜、丑角會整張臉重畫：一般美妝（口紅、眼影、腮紅…）不套用，免得疊在下面弄髒。</summary>
    Private Shared Function ForOpera(b As BeautySettings) As BeautySettings
        If Not b.HasOpera Then Return b
        Dim role = OperaRoles.Get(b.OperaRole)
        If role Is Nothing OrElse role.Group = OperaGroup.Junban Then Return b
        Dim r = b.Clone()
        r.Lips = 0 : r.EyeShadow = 0 : r.EyeLiner = 0 : r.Blush = 0 : r.Highlight = 0 : r.WhiteNose = 0 : r.UnderEye = 0
        r.Brows = 0 : r.Fold = 0 : r.EyeBag = 0 : r.Contour = 0 : r.Lash = 0 : r.Iris = 0
        Return r
    End Function

    Private Shared Function Centroid(pts As IEnumerable(Of Point2f)) As Point2f
        Dim a = pts.ToArray()
        Return New Point2f(a.Average(Function(q) q.X), a.Average(Function(q) q.Y))
    End Function

    ''' <param name="occ">臉上的遮擋物（原圖大小，可為 Nothing）：臉譜的顏料不畫在上面；片子、髯口照畫（畫在頭髮上、垂到臉外）。</param>
    Private Shared Sub ApplyOpera(px As Byte(), stride As Integer, w As Integer, h As Integer, f As FaceRegion, b As BeautySettings, Optional occ As Single() = Nothing)
        Dim role = OperaRoles.Get(b.OperaRole)
        If role Is Nothing OrElse b.Opera <= 0 OrElse f.Dense Is Nothing Then Return
        Dim amt = b.Opera / 100.0
        Dim toPx0 = Function(q As PointF) New Point2f(q.X * w, q.Y * h)
        Dim dense = f.Dense.Select(toPx0).ToArray()
        Dim mesh = f.Mesh?.Select(toPx0).ToArray()
        Dim eyes = EyeShapes(dense, mesh)
        Dim ec0 = Centroid(eyes(0).Poly), ec1 = Centroid(eyes(1).Poly)
        Dim eL = If(ec0.X <= ec1.X, ec0, ec1), eR = If(ec0.X <= ec1.X, ec1, ec0)
        Dim lipO = LipOuter(dense, mesh), lipI = LipInner(dense, mesh)
        Dim mouth = Centroid(lipO)
        Dim d = Math.Sqrt((eR.X - eL.X) ^ 2 + (eR.Y - eL.Y) ^ 2)
        If d < 6 Then Return

        ' 標準臉 → 像素：兩眼中心 (∓0.5, 0)、嘴 (0, 1.05)
        Dim a00, a01, a02, a10, a11, a12 As Double
        Using m = Cv2.GetAffineTransform({New Point2f(-0.5F, 0), New Point2f(0.5F, 0), New Point2f(0, 1.05F)}, {eL, eR, mouth})
            a00 = m.At(Of Double)(0, 0) : a01 = m.At(Of Double)(0, 1) : a02 = m.At(Of Double)(0, 2)
            a10 = m.At(Of Double)(1, 0) : a11 = m.At(Of Double)(1, 1) : a12 = m.At(Of Double)(1, 2)
        End Using
        Dim det = a00 * a11 - a01 * a10
        If Math.Abs(det) < 0.000001 Then Return
        Dim toPx = Function(x As Double, y As Double) New Point2f(CSng(a00 * x + a01 * y + a02), CSng(a10 * x + a11 * y + a12))
        Dim toCan = Function(q As Point2f) As (X As Double, Y As Double)
                        Dim dx = q.X - a02, dy = q.Y - a12
                        Return ((a11 * dx - a01 * dy) / det, (-a10 * dx + a00 * dy) / det)
                    End Function

        ' 範圍：標準臉的方框（有髯口往下多留）
        Dim beard = b.OperaBeard
        Dim yMax = If(beard, 2.9, 1.9)
        Dim corners = {toPx(-1.6, -1.9), toPx(1.6, -1.9), toPx(-1.6, yMax), toPx(1.6, yMax)}
        Dim rx0 = Math.Max(0, CInt(Math.Floor(corners.Min(Function(q) q.X))))
        Dim ry0 = Math.Max(0, CInt(Math.Floor(corners.Min(Function(q) q.Y))))
        Dim rx1 = Math.Min(w - 1, CInt(Math.Ceiling(corners.Max(Function(q) q.X))))
        Dim ry1 = Math.Min(h - 1, CInt(Math.Ceiling(corners.Max(Function(q) q.Y))))
        Dim rw = rx1 - rx0 + 1, rh = ry1 - ry0 + 1
        If rw < 8 OrElse rh < 8 Then Return
        Dim n = rw * rh

        ' 目前顏色（浮點）與原本的亮度（保留明暗用）
        Dim cR(n - 1), cG(n - 1), cB(n - 1), lum(n - 1) As Single
        For y = 0 To rh - 1
            For x = 0 To rw - 1
                Dim pi = (ry0 + y) * stride + (rx0 + x) * 4, i = y * rw + x
                cB(i) = px(pi) : cG(i) = px(pi + 1) : cR(i) = px(pi + 2)
                lum(i) = 0.299F * cR(i) + 0.587F * cG(i) + 0.114F * cB(i)
            Next
        Next
        ' 皮膚的參考亮度：兩頰
        Dim sumL = 0.0, cntL = 0, sumRB = 0.0
        For Each side In {-1.0, 1.0}
            Dim c = toPx(side * 0.62, 0.45)
            Dim rad = Math.Max(2, CInt(d * 0.08))
            For yy = CInt(c.Y) - rad To CInt(c.Y) + rad
                For xx = CInt(c.X) - rad To CInt(c.X) + rad
                    Dim lx = xx - rx0, ly = yy - ry0
                    If lx < 0 OrElse ly < 0 OrElse lx >= rw OrElse ly >= rh Then Continue For
                    sumL += lum(ly * rw + lx) : cntL += 1
                    sumRB += cR(ly * rw + lx) - cB(ly * rw + lx)
                Next
            Next
        Next
        Dim lref = If(cntL > 0, sumL / cntL, 160.0)
        Dim refRB = If(cntL > 0, sumRB / cntL, 30.0) ' 膚色的紅減藍（暖的程度）
        Dim shadeK = b.OperaShade / 100.0

        ' ---- 畫遮罩的小工具 ----
        Const SH = 4 ' 次像素：座標 ×16
        Dim ip = Function(q As Point2f) New OpenCvSharp.Point(CInt((q.X - rx0) * 16), CInt((q.Y - ry0) * 16))
        Dim newMask = Function() New Mat(rh, rw, MatType.CV_8UC1, Scalar.All(0))
        Dim fillPoly = Sub(m As Mat, pts As IEnumerable(Of Point2f), v As Integer)
                           Dim a = pts.Select(ip).ToArray()
                           If a.Length >= 3 Then Cv2.FillPoly(m, {a}, Scalar.All(v), LineTypes.AntiAlias, SH)
                       End Sub
        Dim drawLine = Sub(m As Mat, p0 As Point2f, p1 As Point2f, thick As Double, v As Integer)
                           Cv2.Line(m, ip(p0), ip(p1), Scalar.All(v), Math.Max(1, CInt(Math.Round(thick))), LineTypes.AntiAlias, SH)
                       End Sub
        Dim toFloat = Function(m As Mat, sigma As Double) As Single()
                          Using f32 As New Mat()
                              m.ConvertTo(f32, MatType.CV_32FC1, 1 / 255.0)
                              If sigma > 0.4 Then Cv2.GaussianBlur(f32, f32, New OpenCvSharp.Size(0, 0), sigma)
                              Return GetFloats(f32)
                          End Using
                      End Function
        Dim can = Function(x As Double, y As Double, mirror As Boolean) toPx(If(mirror, -x, x), y)
        ' 折線（可漸細：頭稍細、尾巴收尖）
        Dim strokeLine = Sub(m As Mat, pts As Point2f(), width As Double, taper As Boolean)
                             Dim total = 0.0
                             For j = 0 To pts.Length - 2
                                 total += Math.Sqrt((pts(j + 1).X - pts(j).X) ^ 2 + (pts(j + 1).Y - pts(j).Y) ^ 2)
                             Next
                             If total < 0.5 Then Return
                             Dim acc = 0.0
                             For j = 0 To pts.Length - 2
                                 Dim segLen = Math.Sqrt((pts(j + 1).X - pts(j).X) ^ 2 + (pts(j + 1).Y - pts(j).Y) ^ 2)
                                 Dim pieces = Math.Max(1, CInt(segLen / 3))
                                 For k = 0 To pieces - 1
                                     Dim t0 = k / CDbl(pieces), t1 = (k + 1) / CDbl(pieces)
                                     Dim q0 = New Point2f(CSng(pts(j).X + (pts(j + 1).X - pts(j).X) * t0), CSng(pts(j).Y + (pts(j + 1).Y - pts(j).Y) * t0))
                                     Dim q1 = New Point2f(CSng(pts(j).X + (pts(j + 1).X - pts(j).X) * t1), CSng(pts(j).Y + (pts(j + 1).Y - pts(j).Y) * t1))
                                     Dim tt = (acc + segLen * t0) / total
                                     Dim tf = If(taper, Math.Min(1.0, 0.45 + tt * 4) * (1 - 0.8 * Math.Pow(tt, 1.5)), 1.0)
                                     drawLine(m, q0, q1, width * tf, 255)
                                 Next
                                 acc += segLen
                             Next
                         End Sub
        ' 眼睛輪廓換成標準座標後放大、外眼角上拉，再換回像素
        Dim eyeShape = Function(e As (Upper As Point2f(), Lower As Point2f(), Outer As Point2f, Inner As Point2f, Poly As Point2f(), OuterFirst As Boolean),
                                sx As Double, sy As Double, lift As Double) As Point2f()
                           Dim cp = e.Poly.Select(Function(q) toCan(q)).ToArray()
                           Dim cx = cp.Average(Function(q) q.X), cy = cp.Average(Function(q) q.Y)
                           Dim half = Math.Max(0.01, cp.Max(Function(q) Math.Abs(q.X - cx)))
                           Dim outerSign = Math.Sign(toCan(e.Outer).X - cx)
                           Return cp.Select(Function(q)
                                                Dim dx = (q.X - cx) * sx, dy = (q.Y - cy) * sy
                                                Dim o = Math.Max(0.0, Math.Min(1.0, (q.X - cx) * outerSign / half))
                                                Return toPx(cx + dx, cy + dy - lift * Math.Pow(o, 1.5))
                                            End Function).ToArray()
                       End Function

        ' ---- 共用範圍：臉（往上延伸到髮際、扣掉頭髮）、眼睛、嘴巴裡面 ----
        Dim ovalCan As List(Of (X As Double, Y As Double))
        If mesh IsNot Nothing Then
            ovalCan = MeshFaceOval.Select(Function(i) toCan(mesh(i))).ToList()
        Else
            ovalCan = Enumerable.Range(0, 17).Select(Function(i) toCan(dense(i))).ToList()
            Dim topY = Math.Min(ovalCan(0).Y, ovalCan(16).Y)
            Dim xL = ovalCan(0).X, xR = ovalCan(16).X
            For k = 1 To 11
                Dim ang = Math.PI * k / 12
                ovalCan.Add(((xL + xR) / 2 + (xR - xL) / 2 * Math.Cos(ang), topY - (topY + 1.2) * Math.Sin(ang)))
            Next
        End If
        ' 額頭往上拉（網格只到額頭中段）
        Dim ovalPx = ovalCan.Select(Function(q) toPx(q.X * 1.02, If(q.Y < -0.2, q.Y * 1.3 - 0.05, q.Y))).ToArray()
        ' 網格的輪廓在下顎、腮邊、太陽穴常比實際的臉內縮一點：往外擴（柔邊落在輪廓上，臉的邊緣才塗得滿）
        Dim ocx = ovalPx.Average(Function(q) q.X), ocy = ovalPx.Average(Function(q) q.Y)
        Dim grow = d * 0.08
        Dim expand = Function(pts As Point2f(), by As Double) pts.Select(Function(q)
                                                                            Dim dx = q.X - ocx, dy = q.Y - ocy
                                                                            Dim len = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy))
                                                                            Return New Point2f(CSng(q.X + dx / len * by), CSng(q.Y + dy / len * by))
                                                                        End Function).ToArray()
        Dim ovalOuter = expand(ovalPx, grow)
        Dim inner(n - 1) As Single
        Using m = newMask()
            fillPoly(m, ovalPx, 255)
            inner = toFloat(m, 0)
        End Using
        ' 頭髮：額頭上方、比皮膚暗很多的像素；往外擴的那一圈裡，暗的（兩側垂下的頭髮）也不塗
        Dim hair(n - 1) As Single
        For y = 0 To rh - 1
            For x = 0 To rw - 1
                Dim i = y * rw + x
                Dim dark = Smooth(lref * 0.62 - lum(i), 0, 25)
                Dim cq = toCan(New Point2f(rx0 + x, ry0 + y))
                Dim top = If(cq.Y > -0.45, 0.0, Smooth(-0.45 - cq.Y, 0, 0.12))
                Dim rim = 1 - inner(i)
                ' 外擴的那圈：顏色要像皮膚（比臉頰冷很多的是背景、牆）；臉色本身不暖（冷光）時分不出來，只排除頭髮
                Dim notSkin = If(refRB < 10, 0.0, 1 - Smooth(cR(i) - cB(i), refRB * 0.3, refRB * 0.65))
                hair(i) = CSng(Math.Max(dark * Math.Max(top, rim), rim * notSkin))
            Next
        Next
        Dim faceMask As Single()
        Using m = newMask()
            fillPoly(m, ovalOuter, 255)
            faceMask = toFloat(m, d * 0.025)
        End Using
        For i = 0 To n - 1
            faceMask(i) *= 1 - hair(i)
        Next
        Dim eyeHole As Single(), mouthHole As Single()
        Using m = newMask()
            For Each e In eyes
                fillPoly(m, e.Poly, 255)
            Next
            eyeHole = toFloat(m, d * 0.005)
        End Using
        Using m = newMask()
            fillPoly(m, lipI, 255)
            mouthHole = toFloat(m, d * 0.006)
        End Using

        ' 塗上一層
        Dim paint = Sub(mask As Single(), col As Color, op As Double, shade As Boolean, metal As Boolean)
                        Dim k0 = op * amt
                        For i = 0 To n - 1
                            Dim mv = mask(i) * k0
                            If mv < 0.002 Then Continue For
                            Dim tr As Double = col.R, tg As Double = col.G, tb As Double = col.B
                            If metal Then
                                ' 金屬：明暗對比加強，亮的地方往白色反光（臉的高光變成金屬的反光）
                                Dim rel = Math.Max(1.0, lum(i)) / lref
                                Dim s = Math.Max(0.35, Math.Min(1.6, Math.Pow(rel, 2.2)))
                                Dim spec = Math.Max(0.0, Math.Min(0.75, (rel - 1.0) * 2.5))
                                tr = Math.Min(255, tr * s) : tg = Math.Min(255, tg * s) : tb = Math.Min(255, tb * s)
                                tr += (255 - tr) * spec : tg += (250 - tg) * spec : tb += (235 - tb) * spec
                            ElseIf shade AndAlso shadeK > 0 Then
                                Dim s = Math.Max(0.55, Math.Min(1.35, Math.Pow(Math.Max(1.0, lum(i)) / lref, shadeK)))
                                tr = Math.Min(255, tr * s) : tg = Math.Min(255, tg * s) : tb = Math.Min(255, tb * s)
                            End If
                            cR(i) += CSng((tr - cR(i)) * mv) : cG(i) += CSng((tg - cG(i)) * mv) : cB(i) += CSng((tb - cB(i)) * mv)
                        Next
                    End Sub

        Dim rnd As New Random(17)
        For Each L In role.Layers
            Dim mask As Single()
            Using m = newMask()
                Dim sides = If(L.Mirror, {False, True}, {False})
                Select Case L.Kind
                    Case OperaLayerKind.FaceFill
                        ' 底色：再往外擴一個柔邊的寬度，柔邊落在臉的外面，最後由 faceMask 決定邊緣
                        fillPoly(m, expand(ovalOuter, L.Blur * d * 1.5), 255)
                    Case OperaLayerKind.Poly
                        For Each mir In sides
                            fillPoly(m, L.Pts.Select(Function(q) can(q.X, q.Y, mir)), 255)
                        Next
                    Case OperaLayerKind.Stroke
                        For Each mir In sides
                            strokeLine(m, L.Pts.Select(Function(q) can(q.X, q.Y, mir)).ToArray(), L.Width * d, L.Taper)
                        Next
                    Case OperaLayerKind.Ellipse
                        For Each mir In sides
                            Dim c0 = L.Pts(0)
                            Dim ang0 = L.Angle * Math.PI / 180 * If(mir, -1, 1)
                            Dim cx = If(mir, -c0.X, c0.X), cy = c0.Y
                            Dim ring = Enumerable.Range(0, 48).Select(Function(k)
                                                                          Dim t = 2 * Math.PI * k / 48
                                                                          Dim ex = L.Sx * Math.Cos(t), ey = L.Sy * Math.Sin(t)
                                                                          Return toPx(cx + ex * Math.Cos(ang0) - ey * Math.Sin(ang0), cy + ex * Math.Sin(ang0) + ey * Math.Cos(ang0))
                                                                      End Function)
                            fillPoly(m, ring, 255)
                        Next
                    Case OperaLayerKind.EyeSocket
                        For Each e In eyes
                            fillPoly(m, eyeShape(e, L.Sx, L.Sy, L.Lift), 255)
                        Next
                    Case OperaLayerKind.EyeOutline
                        For Each e In eyes
                            Dim ring = eyeShape(e, L.Sx, L.Sy, L.Lift)
                            strokeLine(m, ring.Concat({ring(0)}).ToArray(), L.Width * d, False)
                        Next
                    Case OperaLayerKind.EyeLine
                        For Each e In eyes
                            Dim lid = If(e.OuterFirst, e.Upper.Reverse().ToArray(), e.Upper) ' 內 → 外
                            For j = 0 To lid.Length - 2
                                Dim t = (j + 0.5) / (lid.Length - 1)
                                drawLine(m, lid(j), lid(j + 1), L.Width * d * (0.4 + 0.6 * t), 255)
                            Next
                            Dim o = toCan(e.Outer)
                            Dim tgt = toPx(If(o.X < 0, -L.Pts(0).X, L.Pts(0).X), L.Pts(0).Y)
                            Dim wing = Enumerable.Range(0, 9).Select(Function(k)
                                                                         Dim t = k / 8.0
                                                                         ' 先順著眼尾往外、再往上翹（二次曲線）
                                                                         Dim mid = New Point2f(CSng(e.Outer.X + (tgt.X - e.Outer.X) * 0.6), CSng(e.Outer.Y + (tgt.Y - e.Outer.Y) * 0.25))
                                                                         Dim xq = (1 - t) ^ 2 * e.Outer.X + 2 * (1 - t) * t * mid.X + t * t * tgt.X
                                                                         Dim yq = (1 - t) ^ 2 * e.Outer.Y + 2 * (1 - t) * t * mid.Y + t * t * tgt.Y
                                                                         Return New Point2f(CSng(xq), CSng(yq))
                                                                     End Function).ToArray()
                            For j = 0 To wing.Length - 2
                                drawLine(m, wing(j), wing(j + 1), L.Width * d * (1 - 0.8 * j / 8.0), 255)
                            Next
                        Next
                    Case OperaLayerKind.Lips
                        Dim cp = lipO.Select(Function(q) toCan(q)).ToArray()
                        Dim cx = cp.Average(Function(q) q.X), cy = cp.Average(Function(q) q.Y)
                        fillPoly(m, cp.Select(Function(q) toPx(cx + (q.X - cx) * L.Sx, cy + (q.Y - cy) * L.Sy)), 255)
                    Case OperaLayerKind.Glitter
                        For Each mir In sides
                            Dim poly = L.Pts.Select(Function(q) can(q.X, q.Y, mir)).ToArray()
                            Dim pxs = poly.Select(Function(q) New Point2f(q.X - rx0, q.Y - ry0)).ToArray()
                            Dim bx0 = pxs.Min(Function(q) q.X), bx1 = pxs.Max(Function(q) q.X), by0 = pxs.Min(Function(q) q.Y), by1 = pxs.Max(Function(q) q.Y)
                            Dim r = Math.Max(1, CInt(d * 0.006))
                            For k = 1 To 70
                                Dim q = New Point2f(CSng(bx0 + rnd.NextDouble() * (bx1 - bx0)), CSng(by0 + rnd.NextDouble() * (by1 - by0)))
                                If Cv2.PointPolygonTest(pxs, q, False) < 0 Then Continue For
                                Cv2.Circle(m, New OpenCvSharp.Point(CInt(q.X), CInt(q.Y)), r, Scalar.All(140 + rnd.Next(116)), -1, LineTypes.AntiAlias)
                            Next
                        Next
                End Select
                mask = toFloat(m, L.Blur * d)
            End Using
            Dim keepEyes = L.KeepEyes AndAlso L.Kind <> OperaLayerKind.EyeLine
            For i = 0 To n - 1
                Dim v = mask(i)
                If v <= 0 Then Continue For
                If L.ClipFace Then v *= faceMask(i)
                If keepEyes Then v *= 1 - eyeHole(i)
                If L.KeepMouth Then v *= 1 - mouthHole(i)
                If occ IsNot Nothing Then v *= 1 - occ((ry0 + i \ rw) * w + rx0 + i Mod rw)
                mask(i) = v
            Next
            paint(mask, L.Color, L.Opacity, L.Shade, L.Metal)
        Next

        ' ---- 片子：沿髮際一排小彎、臉的兩側各一條上寬下尖的長髮片（貼著臉的輪廓，把臉修成瓜子臉）----
        If b.OperaPian Then
            Dim ext = ovalCan.Select(Function(q) (X:=q.X * 1.02, Y:=If(q.Y < -0.2, q.Y * 1.3 - 0.05, q.Y))).ToList()
            ' 髮際：輪廓上方那段在 x 的高度（找最接近的兩點內插）
            Dim top = ext.Where(Function(q) q.Y < -0.6).OrderBy(Function(q) q.X).ToList()
            Dim hairlineY = Function(x As Double) As Double
                                If top.Count = 0 Then Return -1.2
                                If x <= top(0).X Then Return top(0).Y
                                For j = 0 To top.Count - 2
                                    If x <= top(j + 1).X Then
                                        Dim t = (x - top(j).X) / Math.Max(0.0001, top(j + 1).X - top(j).X)
                                        Return top(j).Y + (top(j + 1).Y - top(j).Y) * t
                                    End If
                                Next
                                Return top(top.Count - 1).Y
                            End Function
            Using m = newMask()
                ' 貼著髮際的一條帶子＋一排小彎
                Dim band As New List(Of Point2f)()
                For k = 0 To 20
                    Dim x = -0.72 + 1.44 * k / 20
                    band.Add(toPx(x, hairlineY(x) - 0.1))
                Next
                For k = 20 To 0 Step -1
                    Dim x = -0.72 + 1.44 * k / 20
                    band.Add(toPx(x, hairlineY(x) + 0.03))
                Next
                fillPoly(m, band, 255)
                For k = -3 To 3
                    Dim cx = k * 0.2, cy = hairlineY(k * 0.2) + 0.06
                    fillPoly(m, Enumerable.Range(0, 24).Select(Function(j)
                                                                   Dim t = 2 * Math.PI * j / 24
                                                                   Return toPx(cx + 0.105 * Math.Cos(t), cy + 0.085 * Math.Sin(t))
                                                               End Function), 255)
                Next
                ' 兩側長髮片：沿臉的輪廓，太陽穴到臉頰下方，中間最寬、兩端收尖
                For Each side In {-1, 1}
                    Dim edge = ext.Where(Function(q) Math.Sign(q.X) = side AndAlso q.Y > -0.8 AndAlso q.Y < 1.05).OrderBy(Function(q) q.Y).ToList()
                    If edge.Count < 3 Then Continue For
                    Dim outerSide As New List(Of Point2f)(), innerSide As New List(Of Point2f)()
                    For Each q In edge
                        Dim nx = q.X, ny = q.Y - 0.3
                        Dim nl = Math.Max(0.0001, Math.Sqrt(nx * nx + ny * ny))
                        nx /= nl : ny /= nl
                        Dim t = (q.Y + 0.8) / 1.85
                        Dim wdt = 0.03 + 0.15 * Math.Sin(Math.PI * Math.Min(1.0, t * 1.1)) * (1 - 0.5 * t)
                        outerSide.Add(toPx(q.X + nx * 0.06, q.Y + ny * 0.06))
                        innerSide.Add(toPx(q.X - nx * wdt, q.Y - ny * wdt))
                    Next
                    innerSide.Reverse()
                    fillPoly(m, outerSide.Concat(innerSide), 255)
                Next
                Dim pm = toFloat(m, d * 0.01)
                paint(pm, Color.FromArgb(18, 16, 18), 0.95, False, False)
            End Using
        End If

        ' ---- 髯口：一根根細鬚，尾端漸淡 ----
        If beard Then
            Dim col = If(role.BeardArgb <> 0, Color.FromArgb(role.BeardArgb), Color.FromArgb(26, 26, 26))
            Dim style = If(role.BeardArgb <> 0, role.BeardStyle, 0)
            Using m = newMask()
                Dim br As New Random(29)
                Dim thin = Math.Max(1.0, d * 0.007)
                Dim strand = Sub(x0 As Double, y0 As Double, x1 As Double, y1 As Double)
                                 Dim sway = (br.NextDouble() - 0.5) * 0.12
                                 Dim v = 120 + br.Next(136)
                                 Dim pts = Enumerable.Range(0, 9).Select(Function(k)
                                                                             Dim t = k / 8.0
                                                                             Return toPx(x0 + (x1 - x0) * t + Math.Sin(t * Math.PI) * sway, y0 + (y1 - y0) * t)
                                                                         End Function).ToArray()
                                 For j = 0 To pts.Length - 2
                                     drawLine(m, pts(j), pts(j + 1), thin, v)
                                 Next
                             End Sub
                Select Case style
                    Case 1 ' 滿髯：整片從上唇垂下，蓋住嘴
                        For k = 0 To 149
                            Dim u = br.NextDouble() * 2 - 1
                            Dim x0 = u * 0.48
                            strand(x0, 0.88 + Math.Abs(u) * 0.08, x0 * 1.3 + (br.NextDouble() - 0.5) * 0.12, 2.3 + br.NextDouble() * 0.45 - Math.Abs(u) * 0.25)
                        Next
                    Case 2 ' 紮髯：兩邊各一片，嘴露出來；下巴一片
                        For k = 0 To 99
                            Dim side = If(k Mod 2 = 0, -1, 1)
                            Dim u = 0.3 + br.NextDouble() * 0.3
                            strand(side * u, 0.95, side * (u + 0.12) + (br.NextDouble() - 0.5) * 0.12, 1.9 + br.NextDouble() * 0.45)
                        Next
                        For k = 0 To 49
                            Dim u = (br.NextDouble() * 2 - 1) * 0.28
                            strand(u, 1.35, u * 1.2 + (br.NextDouble() - 0.5) * 0.1, 2.0 + br.NextDouble() * 0.45)
                        Next
                    Case Else ' 三髯：中間一綹、兩邊嘴角各一綹，上窄下散開
                        For Each c0 In {(X:=0.0, Y:=0.9, Len:=2.6), (X:=-0.36, Y:=1.0, Len:=2.35), (X:=0.36, Y:=1.0, Len:=2.35)}
                            For k = 0 To 21
                                Dim u = (br.NextDouble() - 0.5) * 0.1
                                strand(c0.X + u, c0.Y, c0.X * 1.12 + u * 2.4, c0.Len + br.NextDouble() * 0.3)
                            Next
                        Next
                End Select
                Dim bm = toFloat(m, d * 0.003)
                ' 越往下越淡
                For y = 0 To rh - 1
                    For x = 0 To rw - 1
                        Dim i = y * rw + x
                        If bm(i) <= 0 Then Continue For
                        Dim cq = toCan(New Point2f(rx0 + x, ry0 + y))
                        bm(i) *= CSng(1 - 0.65 * Smooth(cq.Y, 1.5, 2.8))
                    Next
                Next
                paint(bm, col, 0.92, False, False)
            End Using
        End If

        ' 寫回
        For y = 0 To rh - 1
            For x = 0 To rw - 1
                Dim pi = (ry0 + y) * stride + (rx0 + x) * 4, i = y * rw + x
                px(pi) = ImagePipeline.ClampByte(cB(i)) : px(pi + 1) = ImagePipeline.ClampByte(cG(i)) : px(pi + 2) = ImagePipeline.ClampByte(cR(i))
            Next
        Next
    End Sub
End Class
