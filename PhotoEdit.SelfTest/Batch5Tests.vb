Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>輪廓圖、變形效果、立體化、簡化路徑、修飾筆刷。</summary>
Module Batch5Tests

    Private Function Render(layers As IEnumerable(Of DrawLayer)) As Bitmap
        Dim bmp As New Bitmap(200, 100, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
        End Using
        LayerStack.Draw(bmp, New EditRecipe With {.Drawings = layers.ToList()})
        Return bmp
    End Function

    Private Function MkSq(cx As Double, cy As Double, s As Double) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Rectangle, .X = cx, .Y = cy, .W = s, .H = s, .Filled = True, .Stroked = False,
                                   .FillColorArgb = Color.Black.ToArgb(), .StrokeColorArgb = Color.Black.ToArgb(), .Brush = BrushKind.HardRound, .StrokeWidth = 0.02}
    End Function

    Private Function RingBounds(rings As List(Of PointF())) As RectangleF
        Dim pts = rings.SelectMany(Function(r) r).ToList()
        If pts.Count = 0 Then Return RectangleF.Empty
        Return RectangleF.FromLTRB(pts.Min(Function(p) p.X), pts.Min(Function(p) p.Y), pts.Max(Function(p) p.X), pts.Max(Function(p) p.Y))
    End Function

    Private Function Area(rings As List(Of PointF())) As Double
        Return Math.Abs(rings.Sum(Function(r) PathBoolean.SignedArea(r)))
    End Function

    Private Function Near(a As Double, b As Double, tol As Double) As Boolean
        Return Math.Abs(a - b) <= tol
    End Function

    Private Function Pts(layer As DrawLayer) As List(Of PointF)
        Return layer.Contours.SelectMany(Function(c) DrawGeometry.SampleContour(c)).ToList()
    End Function

    Sub Batch5TestsRun()
        Console.WriteLine("輪廓圖、變形效果、立體化、簡化路徑、修飾筆刷")
        DrawingRenderer.ClearCache()
        ' ---- 輪廓圖 ----
        Dim sq = MkSq(0.5, 0.5, 0.4)
        Dim outR = VectorEffects.OffsetRings(sq, 0.05)
        Dim ob = RingBounds(outR)
        Check("外擴 0.05：0.4 的方塊變 0.5", Near(ob.Width, 0.5, 0.004) AndAlso Near(ob.Height, 0.5, 0.004), ob.ToString())
        Check("外擴的角是圓的（面積＝方塊＋四邊＋圓）", Near(Area(outR), 0.16 + 4 * 0.4 * 0.05 + Math.PI * 0.05 * 0.05, 0.003), Area(outR).ToString("0.0000"))
        Dim inR = VectorEffects.OffsetRings(sq, -0.05)
        Dim ib = RingBounds(inR)
        Check("內縮 0.05：變 0.3", Near(ib.Width, 0.3, 0.004) AndAlso Near(Area(inR), 0.09, 0.003), $"{ib.Width:0.000} {Area(inR):0.0000}")
        Check("內縮超過一半：沒有東西", VectorEffects.OffsetRings(sq, -0.25).Count = 0)
        Dim ring = New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.5, .Y = 0.5, .W = 0.4, .H = 0.4, .Filled = False, .Stroked = True, .StrokeWidth = 0.02,
                                       .StrokeColorArgb = Color.Black.ToArgb(), .Brush = BrushKind.HardRound}
        Dim ringOut = VectorEffects.OffsetRings(ring, 0.03)
        Check("沒填色的圓圈外擴：外圈直徑 0.42＋0.06、中間還是空的", Near(RingBounds(ringOut).Width, 0.48, 0.006) AndAlso ringOut.Count = 2 AndAlso
              Not PathBoolean.Contains(ringOut, New PointF(0.5F, 0.5F)), $"{RingBounds(ringOut).Width:0.000} {ringOut.Count}")
        Dim red = MkSq(0.5, 0.5, 0.4) : red.FillColorArgb = Color.Red.ToArgb()
        Dim cont = VectorEffects.Contour(red, True, 3, 0.03, Color.Yellow)
        Check("輪廓圖外擴 3 圈：最外圈在最下面、顏色漸變到黃色", cont.Count = 3 AndAlso
              DrawGeometry.Bounds(cont(0)).Width > DrawGeometry.Bounds(cont(1)).Width AndAlso DrawGeometry.Bounds(cont(1)).Width > DrawGeometry.Bounds(cont(2)).Width AndAlso
              cont(0).FillColorArgb = Color.Yellow.ToArgb() AndAlso Color.FromArgb(cont(2).FillColorArgb).G > 60 AndAlso Color.FromArgb(cont(2).FillColorArgb).G < 110 AndAlso
              cont.All(Function(c) c.Shape = DrawShape.Path AndAlso c.Filled AndAlso Not c.Stroked),
              String.Join(" ", cont.Select(Function(c) $"{DrawGeometry.Bounds(c).Width:0.000}/{Color.FromArgb(c.FillColorArgb).G}")))
        Dim contIn = VectorEffects.Contour(red, False, 10, 0.03, Color.White)
        Check("輪廓圖內縮 10 圈、每圈 0.03：只有 6 圈算得出來、最內圈在最上面", contIn.Count = 6 AndAlso
              DrawGeometry.Bounds(contIn.Last()).Width < DrawGeometry.Bounds(contIn(0)).Width, contIn.Count.ToString())
        Dim txt As New DrawLayer With {.Shape = DrawShape.Text, .Text = "AB", .X = 0.5, .Y = 0.5, .W = 0.6, .H = 0.3, .TextSize = 0.2, .TextColorArgb = Color.Navy.ToArgb()}
        Dim tc = VectorEffects.Contour(txt, True, 1, 0.02, Color.White)
        Check("文字也能做輪廓圖（貼紙白邊）", VectorEffects.CanApply(txt) AndAlso tc.Count = 1 AndAlso tc(0).FillColorArgb = Color.White.ToArgb())
        Check("點陣、填滿圖層不能套效果", Not VectorEffects.CanApply(New DrawLayer With {.Shape = DrawShape.Raster}) AndAlso
              Not VectorEffects.CanApply(New DrawLayer With {.Shape = DrawShape.FillLayer}))

        ' ---- 變形效果 ----
        Dim bloat = VectorEffects.Distort(sq, New DistortOptions With {.Kind = DistortKind.PuckerBloat, .Amount = 60})
        Dim pucker = VectorEffects.Distort(sq, New DistortOptions With {.Kind = DistortKind.PuckerBloat, .Amount = -60})
        Dim inSq = Function(l As DrawLayer, p As PointF) PathBoolean.Contains(PathBoolean.RegionOf(l), p)
        Check("膨脹：邊的中間鼓出去、角縮進來", inSq(bloat, New PointF(0.72F, 0.5F)) AndAlso Not inSq(bloat, New PointF(0.69F, 0.69F)))
        Check("收縮：邊的中間凹進來、角變尖往外", Not inSq(pucker, New PointF(0.68F, 0.5F)) AndAlso inSq(pucker, New PointF(0.73F, 0.73F)))
        Dim tw = VectorEffects.Distort(sq, New DistortOptions With {.Kind = DistortKind.Twist, .Amount = 90})
        Dim twMax = Pts(tw).Max(Function(p) VectorEffects.Dist(p, New PointF(0.5F, 0.5F)))
        ' 上邊中點離中心 0.2，轉 90°×(1 − 0.2/0.283)；角（最外面）不動
        Dim ang = Math.PI / 2 * (1 - 0.2 / (0.2 * Math.Sqrt(2)))
        Dim expMid As New PointF(CSng(0.5 + 0.2 * Math.Sin(ang)), CSng(0.5 - 0.2 * Math.Cos(ang)))
        Dim midErr = Pts(tw).Min(Function(p) VectorEffects.Dist(p, expMid))
        Check("扭轉：角（最外面）不動、邊的中間轉了 26°（彎成曲線）", Near(twMax, 0.2 * Math.Sqrt(2), 0.003) AndAlso midErr < 0.004, $"{twMax:0.000} {midErr:0.0000}")
        Dim zz = VectorEffects.Distort(sq, New DistortOptions With {.Kind = DistortKind.ZigZag, .Amount = 5, .Detail = 3})
        Dim zb = DrawGeometry.Bounds(zz)
        Check("鋸齒：每邊 3 個起伏（共 24 點），往外凸 0.02", zz.Contours(0).Nodes.Count = 24 AndAlso Near(zb.Width, 0.44, 0.006), $"{zz.Contours(0).Nodes.Count} {zb.Width:0.000}")
        Dim r1 = VectorEffects.Distort(sq, New DistortOptions With {.Kind = DistortKind.Roughen, .Amount = 3, .Detail = 40, .Seed = 5})
        Dim r2 = VectorEffects.Distort(sq, New DistortOptions With {.Kind = DistortKind.Roughen, .Amount = 3, .Detail = 40, .Seed = 5})
        Dim r3 = VectorEffects.Distort(sq, New DistortOptions With {.Kind = DistortKind.Roughen, .Amount = 3, .Detail = 40, .Seed = 6})
        Check("粗糙：點變多、同一個種子結果一樣、換種子不一樣", r1.Contours(0).Nodes.Count > 50 AndAlso
              Pts(r1).SequenceEqual(Pts(r2)) AndAlso Not Pts(r1).SequenceEqual(Pts(r3)), r1.Contours(0).Nodes.Count.ToString())
        Check("變形效果保留樣式（填色）", bloat.FillColorArgb = sq.FillColorArgb AndAlso bloat.Filled AndAlso bloat.Shape = DrawShape.Path)
        Dim tz = VectorEffects.Distort(txt, New DistortOptions With {.Kind = DistortKind.PuckerBloat, .Amount = 20})
        Check("文字可以套變形（先轉外框）", tz IsNot Nothing AndAlso tz.Contours.Count >= 2)

        ' ---- 立體化 ----
        Dim cube = MkSq(0.5, 0.5, 0.2)
        Dim side = VectorEffects.Extrude(cube, 45, 0.1, Color.Gray, False)
        Dim sb = DrawGeometry.Bounds(side)
        Dim dd = 0.1 / Math.Sqrt(2)
        Check("立體化 45°、深 0.1：側面範圍往右下延伸", side IsNot Nothing AndAlso Near(sb.Left, 0.4, 0.004) AndAlso Near(sb.Right, 0.6 + dd, 0.004) AndAlso Near(sb.Bottom, 0.6 + dd, 0.004),
              sb.ToString())
        Check("側面面積＝方塊＋兩個平行四邊形", Near(Area(PathBoolean.RegionOf(side)), 0.04 + 2 * 0.2 * dd, 0.002), Area(PathBoolean.RegionOf(side)).ToString("0.0000"))
        Dim shaded = VectorEffects.Extrude(cube, 45, 0.1, Color.Gray, True)
        Check("漸層側面：有漸層、後面比較暗", shaded.FillGradient IsNot Nothing AndAlso Color.FromArgb(shaded.FillColor2Argb).R < Color.Gray.R)
        Using bmp = Render({side, cube})
            Check("立體化畫出來：右下有側面、左上沒有", bmp.GetPixel(66, 66).R < 200 AndAlso bmp.GetPixel(37, 37).R > 240, $"{bmp.GetPixel(66, 66)} {bmp.GetPixel(37, 37)}")
        End Using

        ' ---- 簡化路徑 ----
        Dim circlePts = Enumerable.Range(0, 240).Select(Function(i) New PathNode(CSng(0.5 + 0.3 * Math.Cos(i * Math.PI / 120)), CSng(0.5 + 0.3 * Math.Sin(i * Math.PI / 120)))).ToList()
        Dim busy As New DrawLayer With {.Shape = DrawShape.Path, .Filled = True, .Stroked = False, .FillColorArgb = Color.Black.ToArgb(),
                                        .Contours = New List(Of PathContour) From {New PathContour With {.Nodes = circlePts, .Closed = True}}}
        Dim simple = VectorEffects.SimplifyPath(busy, 0.002)
        Check("簡化路徑：240 個節點的圓變成 12 個以下", VectorEffects.NodeCount(busy) = 240 AndAlso VectorEffects.NodeCount(simple) <= 12, VectorEffects.NodeCount(simple).ToString())
        Check("簡化後形狀不變（每點離圓心 0.3 ± 0.003）", Pts(simple).All(Function(p) Near(VectorEffects.Dist(p, New PointF(0.5F, 0.5F)), 0.3, 0.003)))
        Check("已經很簡單的方塊：節點不會變多", VectorEffects.NodeCount(VectorEffects.SimplifyPath(sq, 0.002)) = 4)

        ' ---- 修飾筆刷 ----
        Dim sess As New TouchUpSession(sq, 0.01)
        sess.Apply(TouchUpMode.Smear, New PointF(0.7F, 0.5F), 0.08, 1, New PointF(0.1F, 0), False)
        Dim sm = sess.Result(False)
        Check("塗抹：右邊中間被往右推 0.1、角不動", Near(DrawGeometry.Bounds(sm).Right, 0.8, 0.003) AndAlso inSq(sm, New PointF(0.78F, 0.5F)) AndAlso
              Not inSq(sm, New PointF(0.78F, 0.35F)), DrawGeometry.Bounds(sm).ToString())
        Dim fin = sess.Result(True)
        Check("放開：簡化成曲線（節點比拖曳中少很多）、形狀一樣", fin.Contours(0).Nodes.Count < sm.Contours(0).Nodes.Count / 3 AndAlso inSq(fin, New PointF(0.78F, 0.5F)),
              $"{sm.Contours(0).Nodes.Count} → {fin.Contours(0).Nodes.Count}")
        Dim s2 As New TouchUpSession(sq, 0.01)
        For k = 1 To 20
            s2.Apply(TouchUpMode.Attract, New PointF(0.5F, 0.36F), 0.1, 1, PointF.Empty, False)
        Next
        Check("吸引（筆刷在裡面）：上邊被拉向筆刷、凹進去", Not inSq(s2.Result(False), New PointF(0.5F, 0.32F)) AndAlso inSq(s2.Result(False), New PointF(0.35F, 0.32F)))
        Dim s3 As New TouchUpSession(sq, 0.01)
        For k = 1 To 20
            s3.Apply(TouchUpMode.Repel, New PointF(0.5F, 0.26F), 0.1, 1, PointF.Empty, False)
        Next
        Check("排斥（筆刷在外面）：上邊被推開、凹進去", Not inSq(s3.Result(False), New PointF(0.5F, 0.32F)) AndAlso inSq(s3.Result(False), New PointF(0.33F, 0.32F)))
        Dim s4 As New TouchUpSession(zz, 0.005)
        Dim before = Pts(s4.Result(False)).Count
        For k = 1 To 30
            s4.Apply(TouchUpMode.Smooth, New PointF(0.7F, 0.5F), 0.1, 1, PointF.Empty, False)
        Next
        Dim smX = Pts(s4.Result(False)).Where(Function(p) Math.Abs(p.Y - 0.5) < 0.05).Max(Function(p) p.X)
        Check("平滑：鋸齒被撫平（右邊最凸的點縮回去）", smX < 0.715, smX.ToString("0.000"))
        Dim s5 As New TouchUpSession(sq, 0.01)
        Dim moved = s5.Apply(TouchUpMode.Twirl, New PointF(1.5F, 0.5F), 0.05, 1, PointF.Empty, False)
        Check("筆刷沒碰到圖形：不動", Not moved)
        Dim rr As New DrawLayer With {.Shape = DrawShape.RoundRect, .X = 0.3, .Y = 0.3, .W = 0.3, .H = 0.25, .Param1 = 0.2, .Filled = True, .Stroked = False}
        For Each spacing In {0.01, 0.0125, 0.02}
            Dim s6 As New TouchUpSession(rr, spacing)
            Dim live = s6.Result(False), done = s6.Result(True)
            Dim ob6 = DrawGeometry.Bounds(rr), lb = DrawGeometry.Bounds(live), db = DrawGeometry.Bounds(done)
            Dim err = Function(x As RectangleF) Math.Max(Math.Max(Math.Abs(x.Left - ob6.Left), Math.Abs(x.Right - ob6.Right)), Math.Max(Math.Abs(x.Top - ob6.Top), Math.Abs(x.Bottom - ob6.Bottom)))
            Dim refPts = VectorEffects.Resample(DrawGeometry.Figures(rr)(0).Points, True, 0.0005)
            Dim far = Function(l As DrawLayer) Pts(l).Max(Function(p) refPts.Min(Function(q) VectorEffects.Dist(p, q)))
            Check($"修飾筆刷沒動：外形不變（點距 {spacing}，拖曳中、放開都一樣）", err(lb) < 0.002 AndAlso err(db) < 0.002 AndAlso far(live) < 0.002 AndAlso far(done) < 0.002,
                  $"外框 {err(lb):0.0000}/{err(db):0.0000} 最遠 {far(live):0.0000}/{far(done):0.0000}")
        Next
        Check("文字可以用修飾筆刷", TouchUpSession.CanTouch(txt) AndAlso Not New TouchUpSession(txt, 0.005).IsEmpty)
        DrawingRenderer.ClearCache()
    End Sub
End Module
