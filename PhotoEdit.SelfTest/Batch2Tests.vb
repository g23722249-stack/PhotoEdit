Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>智慧填色、向量漸層、路徑文字、SVG 匯出。</summary>
Module Batch2Tests

    Private Function Ln(x1 As Single, y1 As Single, x2 As Single, y2 As Single) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Line, .StrokeWidth = 0.01, .StrokeColorArgb = Color.Black.ToArgb(),
                                   .Points = New List(Of DrawPoint) From {New DrawPoint(x1, y1), New DrawPoint(x2, y2)}}
    End Function

    Private Function NetArea(rings As List(Of PointF())) As Double
        Return rings.Sum(Function(r) PathBoolean.SignedArea(r))
    End Function

    Private Function Render(layers As List(Of DrawLayer), Optional w As Integer = 200, Optional h As Integer = 100) As Bitmap
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
        End Using
        LayerStack.Draw(bmp, New EditRecipe With {.Drawings = layers})
        Return bmp
    End Function

    Sub Batch2TestsRun()
        Console.WriteLine("智慧填色、向量漸層、路徑文字、SVG 匯出")
        DrawingRenderer.ClearCache()
        ' ---- 智慧填色 ----
        ' 四條線圍出 0.4 × 0.4 的方塊（線的兩端各多出一點，交叉）
        Dim walls = {Ln(0.28, 0.3, 0.72, 0.3), Ln(0.7, 0.28, 0.7, 0.72), Ln(0.72, 0.7, 0.28, 0.7), Ln(0.3, 0.72, 0.3, 0.28)}.ToList()
        Dim reg = SmartFill.Region(walls, 1.0, New PointF(0.5F, 0.5F))
        Check("四條線圍起來：填到一塊、面積約 0.4×0.4", reg IsNot Nothing AndAlso reg.Count = 1 AndAlso Math.Abs(NetArea(reg) - 0.16) < 0.01,
              If(reg Is Nothing, "Nothing", $"{reg.Count} {NetArea(reg):0.0000}"))
        Check("圍起來的外面：沒有圍住", SmartFill.Region(walls, 1.0, New PointF(0.1F, 0.1F)) Is Nothing)
        Dim open = walls.Take(3).ToList()
        Check("缺一邊：沒有圍住", SmartFill.Region(open, 1.0, New PointF(0.5F, 0.5F)) Is Nothing)
        Dim ring As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.5, .Y = 0.5, .W = 0.16, .H = 0.16, .StrokeWidth = 0.005}
        Dim holed = SmartFill.Region(walls.Concat({ring}).ToList(), 1.0, New PointF(0.35F, 0.35F))
        Check("方塊裡有圓：填到的那塊有洞（圓那裡是空的）", holed IsNot Nothing AndAlso holed.Count = 2 AndAlso
              Not PathBoolean.Contains(holed, New PointF(0.5F, 0.5F)) AndAlso PathBoolean.Contains(holed, New PointF(0.35F, 0.35F)),
              If(holed Is Nothing, "Nothing", $"{holed.Count}"))
        Dim inCircle = SmartFill.Region(walls.Concat({ring}).ToList(), 1.0, New PointF(0.5F, 0.5F))
        Check("點圓的裡面：填圓", inCircle IsNot Nothing AndAlso Math.Abs(NetArea(inCircle) - Math.PI * 0.08 * 0.08) < 0.004, If(inCircle Is Nothing, "Nothing", NetArea(inCircle).ToString("0.0000")))
        Dim style As New DrawLayer With {.FillColorArgb = Color.Orange.ToArgb()}
        Dim filled = SmartFill.MakeLayer(reg, style)
        Check("智慧填色的圖層：鋼筆路徑、填色、沒有線條、4 個尖角", filled.Shape = DrawShape.Path AndAlso filled.Filled AndAlso Not filled.Stroked AndAlso
              filled.FillColorArgb = Color.Orange.ToArgb() AndAlso filled.Contours(0).Nodes.Count = 4, $"{filled.Contours(0).Nodes.Count}")
        ' ---- 向量漸層 ----
        Dim rect As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 1, .Y = 0.5, .W = 1.6, .H = 0.6, .Filled = True, .Stroked = False,
                                        .FillColorArgb = Color.Red.ToArgb(), .FillColor2Argb = Color.Blue.ToArgb(),
                                        .FillGradient = New GradientFill With {.X1 = 0, .Y1 = 0.5, .X2 = 1, .Y2 = 0.5, .Dither = False}}
        Using bmp = Render(New List(Of DrawLayer) From {rect})
            Dim l = bmp.GetPixel(25, 50), r = bmp.GetPixel(175, 50)
            Check("線性漸層：左邊紅、右邊藍", l.R > 200 AndAlso l.B < 60 AndAlso r.B > 200 AndAlso r.R < 60, $"{l} {r}")
        End Using
        Dim moved = rect.Clone()
        moved.W = 0.8 : moved.X = 0.6
        Using bmp = Render(New List(Of DrawLayer) From {moved})
            Dim l = bmp.GetPixel(25, 50), r = bmp.GetPixel(95, 50)
            Check("圖形縮小移動：漸層跟著（左紅右藍）", l.R > 190 AndAlso r.B > 190, $"{l} {r}")
        End Using
        Dim radial = rect.Clone()
        radial.FillGradient = New GradientFill With {.Kind = GradientKind.Radial, .X1 = 0.5, .Y1 = 0.5, .X2 = 1, .Y2 = 0.5, .Dither = False}
        Using bmp = Render(New List(Of DrawLayer) From {radial})
            Check("放射漸層：中間紅、邊邊藍", bmp.GetPixel(100, 50).R > 200 AndAlso bmp.GetPixel(178, 50).B > 150, $"{bmp.GetPixel(100, 50)} {bmp.GetPixel(178, 50)}")
        End Using
        Dim copy As New DrawLayer()
        copy.CopyStyleFrom(rect)
        Check("漸層是樣式的一部分（複製、獨立）", copy.FillGradient IsNot Nothing AndAlso copy.FillGradient IsNot rect.FillGradient AndAlso copy.FillColor2Argb = Color.Blue.ToArgb())
        ' ---- 路徑文字 ----
        Dim pt As New DrawLayer With {.Shape = DrawShape.Text, .Text = "ABCD", .TextSize = 0.2, .FontName = "Arial", .TextColorArgb = Color.Black.ToArgb(),
                                      .Stroked = False, .Filled = False,
                                      .Contours = DrawGeometry.ToPath(Ln(0.2, 0.7, 1.8, 0.7)).Contours}
        Check("路徑文字：有節點、外形是路徑", DrawGeometry.IsPathText(pt) AndAlso DrawGeometry.HasNodes(pt) AndAlso Not DrawGeometry.IsBoxLayer(pt))
        Dim size As Single, fam As FontFamily = Nothing
        Dim places = DrawGeometry.PathTextLayout(pt, 100, size, fam)
        Check("排版：4 個字從左到右、角度 0、在中間", places.Count = 4 AndAlso places.All(Function(p) Math.Abs(p.Angle) < 0.5 AndAlso Math.Abs(p.Y - 70) < 0.5) AndAlso
              places(0).X < places(3).X AndAlso Math.Abs((places(0).X + places(3).X) / 2 - 100) < 6, String.Join(" ", places.Select(Function(p) $"{p.X:0}")))
        Using bmp = Render(New List(Of DrawLayer) From {pt})
            Dim above = Enumerable.Range(0, 200).Sum(Function(x) Enumerable.Range(45, 25).Count(Function(y) bmp.GetPixel(x, y).R < 100))
            Dim below = Enumerable.Range(0, 200).Sum(Function(x) Enumerable.Range(72, 25).Count(Function(y) bmp.GetPixel(x, y).R < 100))
            Check("畫出來：字在線的上面", above > 150 AndAlso below < above / 10, $"上 {above} 下 {below}")
        End Using
        pt.TextPathFlip = True
        places = DrawGeometry.PathTextLayout(pt, 100, size, fam)
        Check("放在另一邊：方向反過來（180°）", places.Count = 4 AndAlso places.All(Function(p) Math.Abs(Math.Abs(p.Angle) - 180) < 0.5) AndAlso places(0).X > places(3).X)
        pt.TextPathFlip = False
        Dim circle As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 1, .Y = 0.5, .W = 0.7, .H = 0.7}
        Dim ct = pt.Clone() : ct.Contours = DrawGeometry.ToPath(circle).Contours : ct.Text = "TOP"
        places = DrawGeometry.PathTextLayout(ct, 100, size, fam)
        Check("圓形路徑：字排在最上面", places.Count = 3 AndAlso places(1).Y < 20 AndAlso Math.Abs(places(1).X - 100) < 6, String.Join(" ", places.Select(Function(p) $"({p.X:0},{p.Y:0})")))
        ct.TextPathFlip = True
        places = DrawGeometry.PathTextLayout(ct, 100, size, fam)
        Check("圓形路徑放在另一邊：排在最下面、字是正的", places.Count = 3 AndAlso places(1).Y > 80 AndAlso Math.Abs(places(1).Angle) < 20, String.Join(" ", places.Select(Function(p) $"({p.Y:0},{p.Angle:0})")))
        ct.TextPathFlip = False : ct.TextPathShift = 25
        places = DrawGeometry.PathTextLayout(ct, 100, size, fam)
        Check("位置往後移 25%：從上面移到右邊", places.Count = 3 AndAlso places(1).X > 125, $"{places(1).X:0}")
        Dim outs = DrawGeometry.TextOutlines(pt)
        Check("路徑文字轉外框：每個字一個", outs.Count = 4 AndAlso outs.All(Function(o) o.Shape = DrawShape.Path) AndAlso outs(0).Name = "A")
        ' ---- 漸層跟著旋轉 ----
        Dim rot = rect.Clone() : rot.Rotation = 90
        Dim ag = DrawingRenderer.AbsoluteGradient(rot)
        Check("方框轉 90°：漸層從左右變成上下", Math.Abs(ag.X1 - ag.X2) < 0.001 AndAlso ag.Y2 - ag.Y1 > 1.5, $"({ag.X1:0.00},{ag.Y1:0.00})→({ag.X2:0.00},{ag.Y2:0.00})")
        Dim pathG = DrawGeometry.ToPath(rect) : pathG.FillGradient = rect.FillGradient.Clone()
        Dim pathR = pathG.Clone()
        Dim pb = DrawGeometry.Bounds(pathG)
        DrawGeometry.RotatePath(pathR, pathG, New PointF(pb.X + pb.Width / 2, pb.Y + pb.Height / 2), 90)
        Dim ag2 = DrawingRenderer.AbsoluteGradient(pathR)
        Check("鋼筆路徑轉 90°：漸層端點一起轉", Math.Abs(ag2.X1 - ag2.X2) < 0.01 AndAlso ag2.Y2 - ag2.Y1 > 1.5, $"({ag2.X1:0.00},{ag2.Y1:0.00})→({ag2.X2:0.00},{ag2.Y2:0.00})")
        Dim fr = DrawGeometry.GradientFraction(rot, DrawGeometry.GradientPoint(rot, 0.25, 0.75))
        Check("漸層比例與位置互相換算", Math.Abs(fr.X - 0.25) < 0.0001 AndAlso Math.Abs(fr.Y - 0.75) < 0.0001)
        ' ---- 路徑文字跟著圖形 ----
        Dim srcLine = Ln(0.2F, 0.5F, 1.2F, 0.5F) : srcLine.Id = "src-1"
        Dim linked = pt.Clone() : linked.TextPathSource = "src-1" : linked.Contours = DrawGeometry.ToPath(srcLine).Contours
        Dim lr As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {srcLine, linked}}
        Check("跟著圖形的路徑文字不能直接改節點", Not DrawGeometry.HasNodes(linked) AndAlso DrawGeometry.IsPathText(linked))
        DrawGeometry.Offset(srcLine, 0, 0.2)
        DrawGeometry.SyncPathText(lr)
        Check("圖形移動：文字的路徑跟著", Math.Abs(linked.Contours(0).Nodes(0).Y - 0.7) < 0.0001, $"{linked.Contours(0).Nodes(0).Y}")
        lr.Drawings.Remove(srcLine)
        DrawGeometry.SyncPathText(lr)
        Check("圖形刪掉：文字保留路徑、不再連動", linked.TextPathSource Is Nothing AndAlso DrawGeometry.IsPathText(linked) AndAlso DrawGeometry.HasNodes(linked))
        ' ---- SVG ----
        Dim layers As New List(Of DrawLayer) From {
            rect,
            New DrawLayer With {.Shape = DrawShape.Line, .StrokeWidth = 0.02, .Dash = LineDash.Dash, .EndArrow = ArrowKind.Triangle, .StrokeColorArgb = Color.Green.ToArgb(),
                                .Points = New List(Of DrawPoint) From {New DrawPoint(0.2, 0.2), New DrawPoint(1.5, 0.3)}},
            New DrawLayer With {.Shape = DrawShape.CalloutRect, .X = 1, .Y = 0.5, .W = 0.6, .H = 0.3, .Text = "Hi", .FontName = "Arial", .Filled = True, .Stroked = True,
                                .StrokeWidth = 0.005, .Param1 = 0.18, .TailX = -0.2, .TailY = 0.3},
            pt}
        Dim rr As New EditRecipe With {.Drawings = layers}
        Dim svg As String
        Using bg As New Bitmap(200, 100)
            svg = SvgExport.Export(rr, bg, 200, 100)
        End Using
        Dim doc As Xml.Linq.XDocument = Nothing
        Try
            doc = Xml.Linq.XDocument.Parse(svg)
        Catch ex As Exception
        End Try
        Check("SVG：合法的 XML、大小 200×100", doc IsNot Nothing AndAlso doc.Root.Name.LocalName = "svg" AndAlso doc.Root.Attribute("width").Value = "200")
        Check("SVG：漸層、虛線、箭頭、照片、文字外框", svg.Contains("<linearGradient") AndAlso svg.Contains("stroke-dasharray") AndAlso svg.Contains("<image") AndAlso
              doc.Descendants().Count(Function(e) e.Name.LocalName = "path") >= 10, $"path {If(doc Is Nothing, 0, doc.Descendants().Count(Function(e) e.Name.LocalName = "path"))}")
        ' PowerClip → clipPath
        Dim frame As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 1, .Y = 0.5, .W = 0.5, .H = 0.5, .Filled = False, .Stroked = True}
        Dim content As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 1, .Y = 0.5, .W = 1, .H = 0.8, .Filled = True}
        Dim cr As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {content, frame}}
        DrawGroups.CreateClip(cr, frame, {content}, Nothing, "clip")
        Dim svg2 = SvgExport.Export(cr, Nothing, 200, 100)
        Check("SVG：PowerClip 變成 clipPath、沒有照片", svg2.Contains("<clipPath") AndAlso svg2.Contains("clip-path=""url(#") AndAlso Not svg2.Contains("<image"))
    End Sub
End Module
