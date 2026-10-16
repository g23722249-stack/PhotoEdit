Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>鏡射、正多邊形／星形／螺旋／格紙／弧形、屬性吸管、外框轉物件。</summary>
Module Batch4Tests

    Private Function Render(layers As IEnumerable(Of DrawLayer)) As Bitmap
        Dim bmp As New Bitmap(200, 100, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
        End Using
        LayerStack.Draw(bmp, New EditRecipe With {.Drawings = layers.ToList()})
        Return bmp
    End Function

    Private Function Box(shape As DrawShape, rot As Double, Optional setup As Action(Of DrawLayer) = Nothing) As DrawLayer
        Dim d As New DrawLayer With {.Shape = shape, .X = 0.75, .Y = 0.45, .W = 0.5, .H = 0.6, .Rotation = rot,
                                     .Filled = True, .Stroked = False, .FillColorArgb = Color.Black.ToArgb(), .StrokeColorArgb = Color.Black.ToArgb(),
                                     .Brush = BrushKind.HardRound, .StrokeWidth = 0.025}
        DrawGeometry.ApplyDefaults(d)
        d.X = 0.75 : d.Y = 0.45 : d.W = 0.5 : d.H = 0.6 : d.Rotation = rot
        setup?.Invoke(d)
        Return d
    End Function

    ''' <summary>黑點不一致的比例（相對於黑點數）。</summary>
    Private Function Mismatch(a As Bitmap, b As Bitmap) As Double
        Dim diff = 0, dark = 0
        For y = 0 To a.Height - 1
            For x = 0 To a.Width - 1
                Dim da = a.GetPixel(x, y).R < 128, db = b.GetPixel(x, y).R < 128
                If da Then dark += 1
                If da <> db Then diff += 1
            Next
        Next
        Return diff / Math.Max(1, dark)
    End Function

    ''' <summary>鏡射後的畫面＝原本的畫面翻過來。</summary>
    Private Function MirrorMatches(d As DrawLayer, horizontal As Boolean) As Double
        Using before = Render({d})
            before.RotateFlip(If(horizontal, RotateFlipType.RotateNoneFlipX, RotateFlipType.RotateNoneFlipY))
            Dim m = d.Clone()
            DrawGeometry.Mirror(m, horizontal, 1.0, 0.5)
            DrawingRenderer.ClearCache()
            Using after = Render({m})
                Return Mismatch(before, after)
            End Using
        End Using
    End Function

    Sub Batch4TestsRun()
        Console.WriteLine("鏡射、新圖形、屬性吸管、外框轉物件")
        DrawingRenderer.ClearCache()
        ' ---- 鏡射：每種圖形旋轉 25° 後左右、上下鏡射，畫面要剛好翻過來 ----
        Dim cases As New List(Of (String, DrawLayer)) From {
            ("矩形", Box(DrawShape.Rectangle, 25)),
            ("箭頭", Box(DrawShape.Arrow, 25)),
            ("愛心", Box(DrawShape.Heart, 25)),
            ("五角星", Box(DrawShape.Star5, 25)),
            ("七角星", Box(DrawShape.Star, 25, Sub(d) d.Sides = 7)),
            ("六角星", Box(DrawShape.Star, 25, Sub(d) d.Sides = 6)),
            ("正五邊形", Box(DrawShape.RegularPolygon, 25, Sub(d) d.Sides = 5)),
            ("閃電", Box(DrawShape.Lightning, 25)),
            ("螺旋", Box(DrawShape.Spiral, 25, Sub(d)
                     d.Filled = False
                     d.Stroked = True
                 End Sub)),
            ("扇形", Box(DrawShape.Arc, 25, Sub(d)
                     d.Param1 = -60
                     d.Param2 = 200
                 End Sub)),
            ("弧線", Box(DrawShape.Arc, 25, Sub(d)
                     d.Param1 = 10
                     d.Param2 = 120
                     d.ArcMode = ArcKind.Arc
                     d.Filled = False
                     d.Stroked = True
                 End Sub)),
            ("圖說", Box(DrawShape.CalloutRect, 25, Sub(d)
                     d.TailX = 0.3
                     d.TailY = 0.5
                 End Sub)),
            ("格紙", Box(DrawShape.Grid, 25, Sub(d)
                     d.Filled = False
                     d.Stroked = True
                     d.Sides = 3
                     d.Rows = 2
                 End Sub))}
        Dim path = DrawGeometry.ToPath(Box(DrawShape.Star5, 25))
        cases.Add(("鋼筆路徑", path))
        cases.Add(("直線", New DrawLayer With {.Shape = DrawShape.Line, .Stroked = True, .StrokeColorArgb = Color.Black.ToArgb(), .StrokeWidth = 0.04,
                                               .Brush = BrushKind.HardRound, .EndArrow = ArrowKind.Triangle,
                                               .Points = New List(Of DrawPoint) From {New DrawPoint With {.X = 0.3, .Y = 0.2}, New DrawPoint With {.X = 0.9, .Y = 0.7}}}))
        For Each c In cases
            Dim h = MirrorMatches(c.Item2, True), v = MirrorMatches(c.Item2, False)
            Check($"鏡射：{c.Item1}左右、上下都剛好翻過來", h < 0.04 AndAlso v < 0.04, $"左右 {h:P1} 上下 {v:P1}")
        Next
        Dim twice = Box(DrawShape.Lightning, 30)
        DrawGeometry.Mirror(twice, True, 1.0, 0.5) : DrawGeometry.Mirror(twice, True, 1.0, 0.5)
        Check("鏡射兩次回到原樣", Not twice.MirrorX AndAlso Math.Abs(twice.Rotation - 30) < 0.001 AndAlso Math.Abs(twice.X - 0.75) < 0.0001)
        ' 漸層跟著翻：左紅右藍 → 左藍右紅
        Dim grad = New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 1.0, .Y = 0.5, .W = 1.6, .H = 0.6, .Filled = True, .Stroked = False,
                                       .FillColorArgb = Color.Red.ToArgb(), .FillColor2Argb = Color.Blue.ToArgb(),
                                       .FillGradient = New GradientFill With {.X1 = 0, .Y1 = 0.5, .X2 = 1, .Y2 = 0.5}}
        DrawGeometry.Mirror(grad, True, 1.0, 0.5)
        Using bmp = Render({grad})
            Dim l = bmp.GetPixel(30, 50), r = bmp.GetPixel(170, 50)
            Check("鏡射：漸層方向跟著翻", l.B > 200 AndAlso l.R < 60 AndAlso r.R > 200 AndAlso r.B < 60, $"{l} {r}")
        End Using
        Check("點陣、填滿圖層不能鏡射", Not DrawGeometry.CanMirror(New DrawLayer With {.Shape = DrawShape.Raster}) AndAlso
              Not DrawGeometry.CanMirror(New DrawLayer With {.Shape = DrawShape.FillLayer}))

        ' ---- 新圖形 ----
        Dim fig = Function(d As DrawLayer) DrawGeometry.LocalFigures(d)
        Check("正多邊形：邊數 8 → 8 個頂點、封閉", fig(Box(DrawShape.RegularPolygon, 0, Sub(d) d.Sides = 8))(0).Points.Length = 8 AndAlso
              fig(Box(DrawShape.RegularPolygon, 0, Sub(d) d.Sides = 8))(0).Closed)
        Check("星形：9 角 → 18 個點", fig(Box(DrawShape.Star, 0, Sub(d) d.Sides = 9))(0).Points.Length = 18, fig(Box(DrawShape.Star, 0, Sub(d) d.Sides = 9))(0).Points.Length.ToString())
        Check("舊星形的角數不變", DrawGeometry.StarPointCount(Box(DrawShape.Star5, 0, Sub(d) d.Sides = 9)) = 5 AndAlso DrawGeometry.StarPointCount(Box(DrawShape.Star4, 0)) = 4)
        Dim sp = Box(DrawShape.Spiral, 0, Sub(d) d.Param1 = 3)
        Check("螺旋是開放的線、繞 3 圈", Not fig(sp)(0).Closed AndAlso Not DrawGeometry.IsClosedLayer(sp) AndAlso fig(sp)(0).Points.Length > 200)
        Check("格紙：4 欄 3 列 → 12 格", fig(Box(DrawShape.Grid, 0, Sub(d)
                     d.Sides = 4
                     d.Rows = 3
                 End Sub)).Count = 12)
        Dim pie = Box(DrawShape.Arc, 0), arcL = Box(DrawShape.Arc, 0, Sub(d) d.ArcMode = ArcKind.Arc), chord = Box(DrawShape.Arc, 0, Sub(d) d.ArcMode = ArcKind.Chord)
        Check("弧形：扇形、弓形封閉，弧線開放", DrawGeometry.IsClosedLayer(pie) AndAlso DrawGeometry.IsClosedLayer(chord) AndAlso Not DrawGeometry.IsClosedLayer(arcL))
        Using bmp = Render({Box(DrawShape.Arc, 0, Sub(d)
                     d.Param1 = 0
                     d.Param2 = 90
                 End Sub)})
            ' 0°～90°（右下四分之一）：右下有、左上沒有
            Check("弧形：扇形 0°～90° 畫在右下", bmp.GetPixel(85, 55).R < 128 AndAlso bmp.GetPixel(65, 35).R > 200, $"{bmp.GetPixel(85, 55)} {bmp.GetPixel(65, 35)}")
        End Using
        ' 黃點：拖到原本的位置參數不變；星形的黃點拉近中心尖角變尖
        Dim arcH = Box(DrawShape.Arc, 20, Sub(d)
                     d.Param1 = -30
                     d.Param2 = 150
                 End Sub)
        Dim hs = DrawGeometry.ParamHandles(arcH)
        For i = 0 To hs.Count - 1
            DrawGeometry.SetParamHandle(arcH, i, hs(i))
        Next
        Check("弧形的兩個黃點：拖回原位角度不變", hs.Count = 2 AndAlso Math.Abs(arcH.Param1 - -30) < 0.5 AndAlso Math.Abs(arcH.Param2 - 150) < 0.5, $"{arcH.Param1} {arcH.Param2}")
        Dim st = Box(DrawShape.Star, 0, Sub(d) d.Param1 = 0.5)
        Dim sh = DrawGeometry.ParamHandles(st)
        DrawGeometry.SetParamHandle(st, 0, New PointF((sh(0).X + 0.75F) / 2, (sh(0).Y + 0.45F) / 2))
        Check("星形的黃點往中心拉：內徑變小", sh.Count = 1 AndAlso st.Param1 < 0.4 AndAlso st.Param1 > 0.1, st.Param1.ToString())
        ' 新屬性存檔再讀回
        Dim rec As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {Box(DrawShape.Grid, 0, Sub(d)
                     d.Sides = 7
                     d.Rows = 9
                 End Sub),
                                                                                 Box(DrawShape.Arc, 0, Sub(d) d.ArcMode = ArcKind.Chord),
                                                                                 Box(DrawShape.Lightning, 0, Sub(d) d.MirrorX = True)}}
        Dim back = RecipeStore.FromJson(RecipeStore.ToJson(rec))
        Check("邊數、列數、弧形樣式、左右翻存檔讀回", back.Drawings(0).Sides = 7 AndAlso back.Drawings(0).Rows = 9 AndAlso
              back.Drawings(1).ArcMode = ArcKind.Chord AndAlso back.Drawings(2).MirrorX)
        Check("新圖形可以轉成鋼筆路徑", {DrawShape.RegularPolygon, DrawShape.Star, DrawShape.Spiral, DrawShape.Grid, DrawShape.Arc}.
              All(Function(s) DrawGeometry.ToPath(Box(s, 10)) IsNot Nothing))

        ' ---- 屬性吸管 ----
        Dim src = Box(DrawShape.Ellipse, 0, Sub(d)
                                                 d.FillColorArgb = Color.Orange.ToArgb() : d.Stroked = True : d.StrokeColorArgb = Color.Navy.ToArgb()
                                                 d.StrokeWidth = 0.031 : d.Shadow = True : d.ShadowOpacity = 66 : d.Dash = LineDash.Dash
                                                 d.FadeGradient = New GradientFill With {.X1 = 0, .Y1 = 0, .X2 = 1, .Y2 = 1}
                                             End Sub)
        Dim tgt = Box(DrawShape.Star5, 15)
        tgt.Filled = False : tgt.Name = "星"
        DrawGeometry.CopyAppearance(tgt, src)
        Check("屬性吸管：填色、線條、陰影、虛線、透明度都套上", tgt.Filled AndAlso tgt.FillColorArgb = Color.Orange.ToArgb() AndAlso tgt.Stroked AndAlso
              tgt.StrokeColorArgb = Color.Navy.ToArgb() AndAlso Math.Abs(tgt.StrokeWidth - 0.031) < 0.00001 AndAlso tgt.Shadow AndAlso tgt.ShadowOpacity = 66 AndAlso
              tgt.Dash = LineDash.Dash AndAlso tgt.FadeGradient IsNot Nothing AndAlso tgt.FadeGradient IsNot src.FadeGradient)
        Check("屬性吸管：形狀、位置、角度、名稱不變", tgt.Shape = DrawShape.Star5 AndAlso tgt.Rotation = 15 AndAlso tgt.X = 0.75 AndAlso tgt.Name = "星")
        Dim openLine As New DrawLayer With {.Shape = DrawShape.Line, .Filled = False, .Points = New List(Of DrawPoint) From {New DrawPoint(), New DrawPoint With {.X = 1}}}
        DrawGeometry.CopyAppearance(openLine, src)
        Check("屬性吸管：開放的線不會變成有填色", Not openLine.Filled AndAlso openLine.StrokeColorArgb = Color.Navy.ToArgb())

        ' ---- 外框轉物件 ----
        Dim line As New DrawLayer With {.Shape = DrawShape.Line, .Stroked = True, .StrokeColorArgb = Color.Black.ToArgb(), .StrokeWidth = 0.1,
                                        .Brush = BrushKind.HardRound, .Cap = LineCapKind.Flat,
                                        .Points = New List(Of DrawPoint) From {New DrawPoint With {.X = 0.4, .Y = 0.5}, New DrawPoint With {.X = 1.6, .Y = 0.5}}}
        Dim ol = DrawingRenderer.OutlineToObject(line)
        Check("外框轉物件：直線變成封閉、填黑、沒有外框的路徑", ol IsNot Nothing AndAlso ol.Shape = DrawShape.Path AndAlso ol.Filled AndAlso Not ol.Stroked AndAlso
              ol.FillColorArgb = Color.Black.ToArgb())
        If ol IsNot Nothing Then
            Dim ob = DrawGeometry.Bounds(ol)
            Check("外框轉物件：大小＝線的範圍（長 1.2、粗 0.1）", Math.Abs(ob.Width - 1.2) < 0.01 AndAlso Math.Abs(ob.Height - 0.1) < 0.01, ob.ToString())
        End If
        For Each c In {("粗線條的星形", Box(DrawShape.Star5, 20, Sub(d)
                     d.Filled = False
                     d.Stroked = True
                     d.StrokeWidth = 0.05
                 End Sub)),
                       ("虛線圓", Box(DrawShape.Ellipse, 0, Sub(d)
                     d.Filled = False
                     d.Stroked = True
                     d.StrokeWidth = 0.04
                     d.Dash = LineDash.Dash
                 End Sub)),
                       ("箭頭線", cases.Last().Item2),
                       ("螺旋", Box(DrawShape.Spiral, 0, Sub(d)
                     d.Filled = False
                     d.Stroked = True
                     d.StrokeWidth = 0.045
                 End Sub))}
            DrawingRenderer.ClearCache()
            Dim o = DrawingRenderer.OutlineToObject(c.Item2)
            If o Is Nothing Then
                Check($"外框轉物件：{c.Item1}", False, "Nothing")
                Continue For
            End If
            Using a = Render({c.Item2}), b = Render({o})
                Dim mm = Mismatch(a, b)
                Check($"外框轉物件：{c.Item1}轉出來和原本看起來一樣", mm < 0.06, mm.ToString("P1"))
            End Using
        Next
        Dim ring = DrawingRenderer.OutlineToObject(Box(DrawShape.Ellipse, 0, Sub(d)
                     d.Filled = False
                     d.Stroked = True
                     d.StrokeWidth = 0.05
                 End Sub))
        Using bmp = Render({ring})
            Check("外框轉物件：圓的中間是空的", bmp.GetPixel(75, 45).R > 240, bmp.GetPixel(75, 45).ToString())
        End Using
        Check("沒有線條的圖形、點陣不能轉", Not DrawingRenderer.CanOutlineStroke(Box(DrawShape.Rectangle, 0)) AndAlso
              Not DrawingRenderer.CanOutlineStroke(New DrawLayer With {.Shape = DrawShape.Raster}))
        DrawingRenderer.ClearCache()
    End Sub
End Module
