Imports PhotoEdit

''' <summary>焊接、修剪、相交、排除、形狀建立的區塊：面積、尖角、洞、共用邊、樣式。</summary>
Module ShapeOpTests

    Private Function Sq(x As Double, y As Double, s As Double, Optional color As Integer = &HFF336699) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Rectangle, .X = x, .Y = y, .W = s, .H = s, .Filled = True, .FillColorArgb = color, .Stroked = True}
    End Function

    Private Function Circle(x As Double, y As Double, r As Double) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Ellipse, .X = x, .Y = y, .W = r * 2, .H = r * 2, .Filled = True}
    End Function

    ''' <summary>淨面積（洞是負的）。</summary>
    Private Function Area(d As DrawLayer) As Double
        If d Is Nothing Then Return 0
        Return d.Contours.Sum(Function(c) PathBoolean.SignedArea(DrawGeometry.SampleContour(c)))
    End Function

    Private Function Near(a As Double, b As Double, tol As Double) As Boolean
        Return Math.Abs(a - b) <= tol
    End Function

    Sub ShapeOpTestsRun()
        Console.WriteLine("造形（焊接、修剪、相交、排除）")
        ' 兩個 0.2 的正方形，右下錯開一半
        Dim a = Sq(0.5, 0.5, 0.2, &HFF112233), b = Sq(0.6, 0.6, 0.2, &HFF445566)
        Dim weld = PathBoolean.Apply({a, b}, ShapeOp.Weld)
        Check("焊接：面積 0.07、8 個尖角", Near(Area(weld), 0.07, 0.0002) AndAlso weld.Contours.Count = 1 AndAlso weld.Contours(0).Nodes.Count = 8 AndAlso
              weld.Contours(0).Nodes.All(Function(n) n.Kind = PathNodeKind.Corner), $"{Area(weld):0.00000} {weld?.Contours(0).Nodes.Count}")
        Check("焊接：樣式用最上面的、變成鋼筆路徑", weld.Shape = DrawShape.Path AndAlso weld.FillColorArgb = &HFF445566)
        Dim trim = PathBoolean.Apply({a, b}, ShapeOp.Trim)
        Check("修剪：下面的減掉上面的，面積 0.03、6 個角、樣式用下面的", Near(Area(trim), 0.03, 0.0002) AndAlso trim.Contours(0).Nodes.Count = 6 AndAlso trim.FillColorArgb = &HFF112233,
              $"{Area(trim):0.00000} {trim?.Contours(0).Nodes.Count}")
        Dim inter = PathBoolean.Apply({a, b}, ShapeOp.Intersect)
        Check("相交：面積 0.01、4 個角", Near(Area(inter), 0.01, 0.0001) AndAlso inter.Contours(0).Nodes.Count = 4, $"{Area(inter):0.00000}")
        Dim xr = PathBoolean.Apply({a, b}, ShapeOp.Exclude)
        Check("排除：面積 0.06", Near(Area(xr), 0.06, 0.0002), $"{Area(xr):0.00000}")
        Check("排除：重疊的地方挖空", Not PathBoolean.Contains(xr.Contours.Select(Function(c) DrawGeometry.SampleContour(c)), New PointF(0.5F, 0.5F)) AndAlso
              PathBoolean.Contains(xr.Contours.Select(Function(c) DrawGeometry.SampleContour(c)), New PointF(0.45F, 0.45F)))
        Check("不重疊的相交：沒有結果", PathBoolean.Apply({Sq(0.2, 0.2, 0.1), Sq(0.8, 0.8, 0.1)}, ShapeOp.Intersect) Is Nothing)
        Dim apart = PathBoolean.Apply({Sq(0.2, 0.2, 0.1), Sq(0.8, 0.8, 0.1)}, ShapeOp.Weld)
        Check("不相連的焊接：兩條路徑", apart.Contours.Count = 2)
        ' 共用一條邊：焊接成一個長方形（中間的點拿掉）
        Dim side = PathBoolean.Apply({Sq(0.5, 0.5, 0.2), Sq(0.7, 0.5, 0.2)}, ShapeOp.Weld)
        Check("共用邊的焊接：一個 4 角的長方形", side.Contours.Count = 1 AndAlso side.Contours(0).Nodes.Count = 4 AndAlso Near(Area(side), 0.08, 0.0002),
              $"{side.Contours.Count} {side.Contours(0).Nodes.Count} {Area(side):0.00000}")
        ' 圓：結果是平滑曲線、節點不多
        Dim c1 = Circle(0.5, 0.5, 0.1), c2 = Circle(0.6, 0.5, 0.1)
        Dim cw = PathBoolean.Apply({c1, c2}, ShapeOp.Weld)
        Dim lens = 2 * (0.01 * Math.Acos(0.5) - 0.05 * Math.Sqrt(0.01 - 0.0025))
        Dim expect = 2 * Math.PI * 0.01 - lens
        Check("兩個圓焊接：面積對、節點少、兩個交點是尖角", Near(Area(cw), expect, 0.0004) AndAlso cw.Contours(0).Nodes.Count <= 16 AndAlso
              cw.Contours(0).Nodes.Where(Function(n) n.Kind = PathNodeKind.Corner).Count() = 2,
              $"{Area(cw):0.00000}/{expect:0.00000} 節點 {cw.Contours(0).Nodes.Count} 尖角 {cw.Contours(0).Nodes.Where(Function(n) n.Kind = PathNodeKind.Corner).Count()}")
        ' 方塊挖圓洞
        Dim hole = PathBoolean.Apply({Sq(0.5, 0.5, 0.4), Circle(0.5, 0.5, 0.1)}, ShapeOp.Trim)
        Dim rings = hole.Contours.Select(Function(c) DrawGeometry.SampleContour(c)).ToList()
        Check("方塊減圓：兩條路徑、中間是洞", hole.Contours.Count = 2 AndAlso Not PathBoolean.Contains(rings, New PointF(0.5F, 0.5F)) AndAlso
              PathBoolean.Contains(rings, New PointF(0.35F, 0.35F)) AndAlso Near(Area(hole), 0.16 - Math.PI * 0.01, 0.0004), $"{hole.Contours.Count} {Area(hole):0.00000}")
        ' 旋轉的矩形（外形線是世界座標）
        Dim rot = Sq(0.5, 0.5, 0.2) : rot.Rotation = 45
        Dim rw = PathBoolean.Apply({rot, Sq(0.5, 0.5, 0.2)}, ShapeOp.Weld)
        Check("旋轉 45° 的方塊與方塊焊接：八角星、16 個角", rw.Contours(0).Nodes.Count = 16, $"{rw.Contours(0).Nodes.Count}")
        Check("能不能參加：圖說、點陣、直線不行", Not PathBoolean.CanCombine(New DrawLayer With {.Shape = DrawShape.CalloutRect, .W = 0.1, .H = 0.1}) AndAlso
              Not PathBoolean.CanCombine(New DrawLayer With {.Shape = DrawShape.Raster}) AndAlso PathBoolean.CanCombine(a) AndAlso
              Not PathBoolean.CanCombine(New DrawLayer With {.Shape = DrawShape.Line, .Points = New List(Of DrawPoint) From {New DrawPoint(0.1, 0.1), New DrawPoint(0.3, 0.3)}}))
        ' 形狀建立的區塊
        Dim faces = PathBoolean.Faces({PathBoolean.RegionOf(c1), PathBoolean.RegionOf(c2)})
        Check("兩個圓切成三塊（左、中、右）", faces.Count = 3 AndAlso faces.Select(Function(f) f.Mask).OrderBy(Function(m) m).SequenceEqual({1, 2, 3}),
              String.Join(",", faces.Select(Function(f) f.Mask)))
        Dim mid = faces.First(Function(f) f.Mask = 3)
        Check("中間那塊包含 (0.55, 0.5)", PathBoolean.Contains(mid.Rings, New PointF(0.55F, 0.5F)) AndAlso Not PathBoolean.Contains(mid.Rings, New PointF(0.45F, 0.5F)))
        Dim f2 = PathBoolean.Faces({PathBoolean.RegionOf(Sq(0.5, 0.5, 0.4)), PathBoolean.RegionOf(Sq(0.5, 0.5, 0.1))})
        Dim outer = f2.FirstOrDefault(Function(f) f.Mask = 1)
        Check("大方塊裡的小方塊：外圈那塊有洞", f2.Count = 2 AndAlso outer IsNot Nothing AndAlso outer.Rings.Count = 2, $"{f2.Count} {outer?.Rings.Count}")
        ' 擬合：圓形的折線擬合成少量平滑節點，誤差小
        Dim ring = Enumerable.Range(0, 120).Select(Function(k) New PointF(CSng(0.5 + 0.2 * Math.Cos(k * Math.PI / 60)), CSng(0.5 + 0.2 * Math.Sin(k * Math.PI / 60)))).ToArray()
        Dim fit = PathBoolean.FitRing(ring, 0.0005)
        Dim worst = DrawGeometry.SampleContour(fit).Max(Function(p) Math.Abs(DrawGeometry.Dist(p, New PointF(0.5F, 0.5F)) - 0.2))
        Check("圓擬合：節點 ≤ 8、全部平滑、誤差 < 0.001", fit.Nodes.Count <= 8 AndAlso fit.Nodes.All(Function(n) n.Kind = PathNodeKind.Smooth) AndAlso worst < 0.001,
              $"{fit.Nodes.Count} {worst:0.00000}")
    End Sub
End Module
