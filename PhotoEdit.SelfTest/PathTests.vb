Imports PhotoEdit

''' <summary>鋼筆路徑：轉成曲線、加節點不變形、平滑／對稱把手、複製與比對、縮放與旋轉、存檔。</summary>
Module PathTests

    Private Function MaxGap(a As PointF(), b As PointF()) As Double
        ' a 每一點到 b 折線的最近距離的最大值
        Dim worst = 0.0
        For Each p In a
            Dim best = Double.MaxValue
            For i = 0 To b.Length - 2
                best = Math.Min(best, SegDist(p, b(i), b(i + 1)))
            Next
            worst = Math.Max(worst, best)
        Next
        Return worst
    End Function

    Private Function SegDist(p As PointF, a As PointF, b As PointF) As Double
        Dim vx = b.X - a.X, vy = b.Y - a.Y
        Dim l2 = vx * vx + vy * vy
        Dim t = If(l2 <= 0, 0, Math.Max(0, Math.Min(1, ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2)))
        Return Math.Sqrt((p.X - a.X - t * vx) ^ 2 + (p.Y - a.Y - t * vy) ^ 2)
    End Function

    Private Function AllPoints(d As DrawLayer) As PointF()
        Return DrawGeometry.Figures(d).SelectMany(Function(f) If(f.Closed, f.Points.Concat({f.Points(0)}), f.Points)).ToArray()
    End Function

    Sub PathTestsRun()
        Console.WriteLine("鋼筆路徑")
        Dim rect As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.5, .Y = 0.5, .W = 0.4, .H = 0.2, .Name = "方", .StrokeColorArgb = &HFF112233}
        Dim rp = DrawGeometry.ToPath(rect)
        Check("矩形轉曲線：4 個尖角節點、封閉、名稱顏色不變", rp.Shape = DrawShape.Path AndAlso rp.Contours.Count = 1 AndAlso rp.Contours(0).Nodes.Count = 4 AndAlso
              rp.Contours(0).Closed AndAlso rp.Contours(0).Nodes.All(Function(q) q.Kind = PathNodeKind.Corner) AndAlso rp.Name = "方" AndAlso rp.StrokeColorArgb = rect.StrokeColorArgb)
        Dim rb = DrawGeometry.Bounds(rect), pb = DrawGeometry.Bounds(rp)
        Check("矩形轉曲線：外框一樣", Math.Abs(rb.X - pb.X) < 0.0001 AndAlso Math.Abs(rb.Width - pb.Width) < 0.0001 AndAlso Math.Abs(rb.Height - pb.Height) < 0.0001, $"{rb} {pb}")
        Dim el As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.5, .Y = 0.5, .W = 0.4, .H = 0.4}
        Dim ep = DrawGeometry.ToPath(el)
        Dim gap = MaxGap(AllPoints(ep), AllPoints(el))
        Check("橢圓轉曲線：4 個對稱節點，形狀幾乎一樣", ep.Contours(0).Nodes.Count = 4 AndAlso ep.Contours(0).Nodes.All(Function(q) q.Kind = PathNodeKind.Symmetric) AndAlso gap < 0.002, gap.ToString("0.0000"))
        Dim star As New DrawLayer With {.Shape = DrawShape.Star5, .X = 0.5, .Y = 0.5, .W = 0.3, .H = 0.3}
        DrawGeometry.ApplyDefaults(star)
        Dim sp = DrawGeometry.ToPath(star)
        Check("五角星轉曲線：10 個節點", sp IsNot Nothing AndAlso sp.Contours(0).Nodes.Count = 10, If(sp Is Nothing, "Nothing", sp.Contours(0).Nodes.Count.ToString()))
        Check("文字、點陣、填滿圖層不能直接轉（文字用 TextOutlines）", DrawGeometry.ToPath(New DrawLayer With {.Shape = DrawShape.Text}) Is Nothing AndAlso
              DrawGeometry.ToPath(New DrawLayer With {.Shape = DrawShape.Raster}) Is Nothing AndAlso DrawGeometry.ToPath(New DrawLayer With {.Shape = DrawShape.FillLayer}) Is Nothing)

        ' 加節點：曲線形狀不變
        Dim before = AllPoints(ep)
        Dim c = ep.Contours(0)
        Dim idx = DrawGeometry.InsertNode(c, 1, 0.3)
        Dim after = AllPoints(ep)
        Dim g2 = Math.Max(MaxGap(after, before), MaxGap(before, after))
        Check("加節點：5 個、形狀不變", c.Nodes.Count = 5 AndAlso idx = 2 AndAlso g2 < 0.001, g2.ToString("0.0000"))
        Dim near = DrawGeometry.NearestOnPath(ep, c.Nodes(idx).Anchor)
        Check("最近點：剛加的節點就在路徑上", near.Distance < 0.0005, near.Distance.ToString())

        ' 把手：平滑保持一直線、對稱長度一樣、尖角另一邊不動
        Dim n As New PathNode(0.5F, 0.5F) With {.InX = 0.4F, .InY = 0.5F, .OutX = 0.7F, .OutY = 0.5F, .Kind = PathNodeKind.Smooth}
        DrawGeometry.MoveHandle(n, True, New PointF(0.5F, 0.8F))
        Check("平滑：另一邊轉到反方向、長度不變", Math.Abs(n.InX - 0.5F) < 0.0001 AndAlso Math.Abs(n.InY - 0.4F) < 0.0001, $"{n.InX},{n.InY}")
        n.Kind = PathNodeKind.Symmetric
        DrawGeometry.MoveHandle(n, False, New PointF(0.3F, 0.5F))
        Check("對稱：另一邊一樣長", Math.Abs(n.OutX - 0.7F) < 0.0001 AndAlso Math.Abs(n.OutY - 0.5F) < 0.0001, $"{n.OutX},{n.OutY}")
        n.Kind = PathNodeKind.Corner
        DrawGeometry.MoveHandle(n, True, New PointF(0.6F, 0.6F))
        Check("尖角：另一邊不動", Math.Abs(n.InX - 0.3F) < 0.0001 AndAlso Math.Abs(n.InY - 0.5F) < 0.0001)
        ' 尖角轉平滑：沒有把手時依前後節點拉出
        Dim tri As New PathContour With {.Closed = True}
        tri.Nodes.AddRange({New PathNode(0.2F, 0.2F), New PathNode(0.5F, 0.6F), New PathNode(0.8F, 0.2F)})
        DrawGeometry.SetNodeKind(tri, 1, PathNodeKind.Smooth)
        Dim m = tri.Nodes(1)
        Check("尖角轉平滑：拉出水平的把手", Math.Abs(m.InY - 0.6F) < 0.0001 AndAlso m.InX < 0.5F AndAlso m.OutX > 0.5F)

        ' 複製、比對（復原紀錄靠 CanonicalJson）
        Dim r1 As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {rp}}
        Dim r2 = r1.Clone()
        Check("複製後相等", r1.Equals(r2))
        r2.Drawings(0).Contours(0).Nodes(0).X += 0.01F
        Check("改節點後不相等、原本的沒被改到", Not r1.Equals(r2) AndAlso Math.Abs(r1.Drawings(0).Contours(0).Nodes(0).X - rp.Contours(0).Nodes(0).X) < 0.000001)

        ' 平移、縮放、旋轉
        Dim mv = rp.Clone()
        DrawGeometry.Offset(mv, 0.1F, 0)
        Check("平移：節點與把手一起動", Math.Abs(mv.Contours(0).Nodes(0).X - rp.Contours(0).Nodes(0).X - 0.1F) < 0.0001)
        Dim sc = rp.Clone()
        Dim ob = DrawGeometry.Bounds(rp)
        DrawGeometry.ScalePoints(sc, rp, ob, New RectangleF(ob.X, ob.Y, ob.Width * 2, ob.Height))
        Check("縮放：寬度變兩倍", Math.Abs(DrawGeometry.Bounds(sc).Width - ob.Width * 2) < 0.0005, DrawGeometry.Bounds(sc).ToString())
        Dim rt = rp.Clone()
        DrawGeometry.RotatePath(rt, rp, New PointF(ob.X + ob.Width / 2, ob.Y + ob.Height / 2), 90)
        Dim tb = DrawGeometry.Bounds(rt)
        Check("旋轉 90°：寬高對調", Math.Abs(tb.Width - ob.Height) < 0.0005 AndAlso Math.Abs(tb.Height - ob.Width) < 0.0005, tb.ToString())

        ' 開放路徑：外形是開放的線（一定有線條）
        Dim open As New DrawLayer With {.Shape = DrawShape.Path, .Contours = New List(Of PathContour) From {New PathContour()}}
        open.Contours(0).Nodes.AddRange({New PathNode(0.1F, 0.1F), New PathNode(0.3F, 0.4F)})
        Dim fs = DrawGeometry.Figures(open)
        Check("開放路徑：一條不封閉的線", fs.Count = 1 AndAlso Not fs(0).Closed AndAlso Not DrawGeometry.IsClosedLayer(open))

        ' 存檔再讀回來
        Dim json = System.Text.Json.JsonSerializer.Serialize(r1)
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(json)
        Check("存檔讀回：節點一樣", back.Equals(r1) AndAlso back.Drawings(0).Contours(0).Nodes.Count = 4)
    End Sub
End Module
