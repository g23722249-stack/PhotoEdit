Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>尺寸線、連接線、圖樣填色、可變寬度、色票、參考線存檔。</summary>
Module Batch6Tests

    Private Function Render(layers As IEnumerable(Of DrawLayer)) As Bitmap
        Dim bmp As New Bitmap(200, 100, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
        End Using
        LayerStack.Draw(bmp, New EditRecipe With {.Drawings = layers.ToList()})
        Return bmp
    End Function

    Private Function Near(a As Double, b As Double, tol As Double) As Boolean
        Return Math.Abs(a - b) <= tol
    End Function

    Private Function Dark(c As Color) As Boolean
        Return c.R < 128
    End Function

    Private Function DimLayer(ax As Single, ay As Single, bx As Single, by As Single, Optional kind As DimensionKind = DimensionKind.Aligned) As DrawLayer
        Dim d As New DrawLayer With {.Shape = DrawShape.Dimension, .DimKind = kind, .DimPhotoPx = 1000, .StrokeColorArgb = Color.Black.ToArgb(),
                                     .TextColorArgb = Color.Black.ToArgb(), .StrokeWidth = 0.004, .Brush = BrushKind.HardRound,
                                     .Points = New List(Of DrawPoint) From {New DrawPoint(ax, ay), New DrawPoint(bx, by)}}
        DrawGeometry.ApplyDefaults(d)
        Return d
    End Function

    Private Function Rect(cx As Double, cy As Double, w As Double, h As Double, name As String) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Rectangle, .X = cx, .Y = cy, .W = w, .H = h, .Filled = True, .Stroked = False, .Name = name,
                                   .Id = name, .FillColorArgb = Color.Black.ToArgb()}
    End Function

    Sub Batch6TestsRun()
        Console.WriteLine("尺寸線、連接線、圖樣填色、可變寬度、色票")
        DrawingRenderer.ClearCache()
        ' ---- 尺寸線 ----
        Dim dm = DimLayer(0.2F, 0.5F, 0.8F, 0.5F)
        Check("尺寸線：量 0.6 → 「600 px」", DrawGeometry.DimensionLabel(dm) = "600 px", DrawGeometry.DimensionLabel(dm))
        Dim figs = DrawGeometry.Figures(dm)
        Check("尺寸線：兩條延伸線＋尺寸線，尺寸線在上面 0.04", figs.Count = 3 AndAlso Near(figs(2).Points(0).Y, 0.46, 0.0001) AndAlso Near(figs(2).Points(1).X, 0.8, 0.0001))
        Check("尺寸線不是方框類、不是封閉的", Not DrawGeometry.IsBoxLayer(dm) AndAlso Not DrawGeometry.IsClosedLayer(dm) AndAlso DrawGeometry.IsPointShape(DrawShape.Dimension))
        Dim hz = DimLayer(0.2F, 0.5F, 0.6F, 0.7F, DimensionKind.Horizontal), vt = DimLayer(0.2F, 0.5F, 0.6F, 0.7F, DimensionKind.Vertical)
        Check("水平、垂直尺寸線只量一個方向", DrawGeometry.DimensionLabel(hz) = "400 px" AndAlso DrawGeometry.DimensionLabel(vt) = "200 px",
              DrawGeometry.DimensionLabel(hz) & " / " & DrawGeometry.DimensionLabel(vt))
        Dim cm = DimLayer(0.2F, 0.5F, 0.8F, 0.5F) : cm.DimUnit = "cm" : cm.DimScale = 0.025 : cm.DimDecimals = 1
        Check("換算單位：600 像素、1 cm = 40 像素 → 15.0 cm", DrawGeometry.DimensionLabel(cm) = "15.0 cm", DrawGeometry.DimensionLabel(cm))
        Dim custom = DimLayer(0.2F, 0.5F, 0.8F, 0.5F) : custom.Text = "寬 {0}"
        Check("自訂標籤：{0} 換成數字", DrawGeometry.DimensionLabel(custom) = "寬 600")
        Dim off = DimLayer(0.2F, 0.5F, 0.8F, 0.5F)
        DrawGeometry.SetParamHandle(off, 0, New PointF(0.5F, 0.6F))
        Check("拖黃點：尺寸線移到下面 0.1", Near(off.Param1, -0.1, 0.0001) AndAlso Near(DrawGeometry.Figures(off)(2).Points(0).Y, 0.6, 0.0001), off.Param1.ToString())
        Dim big = DimLayer(0.2F, 0.5F, 0.8F, 0.5F) : big.StrokeWidth = 0.02 : big.TextSize = 0.12 ' 測試圖只有 100 像素高：線、字放大
        Using bmp = Render({big})
            Dim labelDark = 0
            For y = 28 To 42
                For x = 30 To 70
                    If Dark(bmp.GetPixel(x, y)) Then labelDark += 1
                Next
            Next
            Dim lineY = 46
            Check("尺寸線畫出來：尺寸線、標籤在線的上面", Dark(bmp.GetPixel(50, lineY)) AndAlso labelDark > 15, $"{bmp.GetPixel(100, lineY)} 標籤 {labelDark}")
            ' 箭頭在尺寸線兩端，延伸線上沒有
            Dim colH = Function(x As Integer) Enumerable.Range(40, 14).Where(Function(y) Dark(bmp.GetPixel(x, y))).Count()
            Check("箭頭在尺寸線兩端（比線粗）", Enumerable.Range(20, 10).Max(Function(x) colH(x)) > colH(50) + 2 AndAlso Enumerable.Range(71, 10).Max(Function(x) colH(x)) > colH(50) + 2, $"{Enumerable.Range(20, 10).Max(Function(x) colH(x))} {colH(50)} {Enumerable.Range(71, 10).Max(Function(x) colH(x))}")
        End Using
        Dim mir = DimLayer(0.2F, 0.5F, 0.8F, 0.5F)
        DrawGeometry.Mirror(mir, True, 1.0, 0.5)
        Check("左右鏡射：尺寸線還在上面", Near(DrawGeometry.Figures(mir)(2).Points(0).Y, 0.46, 0.0001) AndAlso DrawGeometry.DimensionLabel(mir) = "600 px")
        Dim mirV = DimLayer(0.2F, 0.5F, 0.8F, 0.5F)
        DrawGeometry.Mirror(mirV, False, 1.0, 0.5)
        Check("上下鏡射：尺寸線翻到下面", Near(DrawGeometry.Figures(mirV)(2).Points(0).Y, 0.54, 0.0001), DrawGeometry.Figures(mirV)(2).Points(0).Y.ToString())

        ' ---- 連接線 ----
        Dim a = Rect(0.4, 0.5, 0.2, 0.2, "A"), b = Rect(1.4, 0.5, 0.2, 0.2, "B")
        Dim cn As New DrawLayer With {.Shape = DrawShape.Connector, .ConnStart = "A", .ConnEnd = "B", .Stroked = True, .StrokeWidth = 0.006,
                                      .StrokeColorArgb = Color.Black.ToArgb(), .Brush = BrushKind.HardRound,
                                      .Points = New List(Of DrawPoint) From {New DrawPoint(0, 0), New DrawPoint(1, 1)}}
        DrawGeometry.ApplyDefaults(cn)
        Dim rec As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {a, b, cn}}
        DrawGeometry.SyncConnectors(rec)
        Check("連接線：從 A 的右邊連到 B 的左邊", cn.Points.Count = 2 AndAlso Near(cn.Points(0).X, 0.5, 0.0001) AndAlso Near(cn.Points(1).X, 1.3, 0.0001) AndAlso Near(cn.Points(0).Y, 0.5, 0.0001),
              String.Join(" ", cn.Points.Select(Function(p) $"({p.X:0.00},{p.Y:0.00})")))
        b.Y = 0.8
        DrawGeometry.SyncConnectors(rec)
        Check("B 移下去：連接線跟著（切在兩個方塊的邊上）", Near(cn.Points(1).X, 1.3, 0.0001) AndAlso cn.Points(1).Y > 0.6 AndAlso cn.Points(0).X >= 0.5 - 0.0001,
              String.Join(" ", cn.Points.Select(Function(p) $"({p.X:0.00},{p.Y:0.00})")))
        cn.ConnElbow = True
        DrawGeometry.SyncConnectors(rec)
        Dim axis = Enumerable.Range(1, cn.Points.Count - 1).All(Function(i) Near(cn.Points(i).X, cn.Points(i - 1).X, 0.00001) OrElse Near(cn.Points(i).Y, cn.Points(i - 1).Y, 0.00001))
        Check("直角連接線：4 點、每段水平或垂直", cn.Points.Count = 4 AndAlso axis AndAlso Near(cn.Points(0).X, 0.5, 0.0001) AndAlso Near(cn.Points(3).X, 1.3, 0.0001))
        Check("連接線有終點箭頭、不是封閉的", cn.EndArrow = ArrowKind.Triangle AndAlso Not DrawGeometry.IsClosedLayer(cn))
        rec.Drawings.Remove(b)
        Dim lastPt = cn.Points(cn.Points.Count - 1).ToPointF()
        DrawGeometry.SyncConnectors(rec)
        Check("B 刪掉：終點變成自由的點、留在原處", cn.ConnEnd Is Nothing AndAlso VectorEffects.Dist(cn.Points(cn.Points.Count - 1).ToPointF(), lastPt) < 0.0001)
        Check("連接線、點陣不能當端點；方塊可以", DrawGeometry.CanConnect(a) AndAlso Not DrawGeometry.CanConnect(cn) AndAlso Not DrawGeometry.CanConnect(New DrawLayer With {.Shape = DrawShape.Raster}))

        ' ---- 圖樣填色 ----
        Dim frac = Function(kind As PatternKind) As Double
                       Dim r = Rect(1.0, 0.5, 1.6, 0.8, "P")
                       r.FillColorArgb = Color.White.ToArgb() : r.FillColor2Argb = Color.Black.ToArgb()
                       r.FillPattern = New PatternFill With {.Kind = kind, .Scale = 0.1}
                       DrawingRenderer.ClearCache()
                       Using bmp = Render({r})
                           ' 平均深淺（反鋸齒的邊算一半）
                           Dim n = 0, dk = 0.0
                           For y = 15 To 84
                               For x = 25 To 174
                                   n += 1
                                   dk += (255 - bmp.GetPixel(x, y).R) / 255.0
                               Next
                           Next
                           Return dk / n
                       End Using
                   End Function
        Dim st = frac(PatternKind.Stripes), dt = frac(PatternKind.Dots), ck = frac(PatternKind.Checker), gr = frac(PatternKind.Grid)
        Check("圖樣：條紋一半、圓點約 28%、棋盤一半、格線少", Near(st, 0.5, 0.06) AndAlso Near(dt, 0.28, 0.06) AndAlso Near(ck, 0.5, 0.06) AndAlso gr > 0.15 AndAlso gr < 0.3,
              $"{st:P0} {dt:P0} {ck:P0} {gr:P0}")
        For Each k In {PatternKind.Crosshatch, PatternKind.Bricks, PatternKind.Waves, PatternKind.Diamonds}
            Dim f = frac(k)
            Check($"圖樣 {k}：有花樣（不是全白或全黑）", f > 0.03 AndAlso f < 0.97, f.ToString("P0"))
        Next
        Dim matR = Rect(1.0, 0.5, 1.6, 0.8, "M")
        matR.FillPattern = New PatternFill With {.Kind = PatternKind.Material, .Material = MaterialKind.Granite}
        Using bmp = Render({matR})
            Dim cols = Enumerable.Range(0, 20).Select(Function(i) bmp.GetPixel(30 + i * 7, 40 + (i Mod 5) * 5).ToArgb()).Distinct().Count()
            Check("圖樣：材質有紋理（顏色有變化）", cols > 5, cols.ToString())
        End Using
        Dim cl = Rect(0.5, 0.5, 0.2, 0.2, "C") : cl.FillPattern = New PatternFill With {.Kind = PatternKind.Dots}
        Dim cl2 = cl.Clone() : cl2.FillPattern.Scale = 0.2
        Check("複製圖層：圖樣是各自的", cl.FillPattern.Scale = 0.04)

        ' ---- 可變寬度 ----
        Dim ln As New DrawLayer With {.Shape = DrawShape.Line, .Stroked = True, .StrokeWidth = 0.12, .StrokeColorArgb = Color.Black.ToArgb(), .Brush = BrushKind.HardRound,
                                      .Points = New List(Of DrawPoint) From {New DrawPoint(0.2, 0.5), New DrawPoint(1.8, 0.5)}}
        ln.WidthProfile = DrawGeometry.WidthPresets().First(Function(p) p.Name = "兩端尖").Stops.Select(Function(s) s.Clone()).ToList()
        Using bmp = Render({ln})
            Dim colH = Function(x As Integer) Enumerable.Range(0, 100).Where(Function(y) Dark(bmp.GetPixel(x, y))).Count()
            Check("兩端尖：中間 12 像素粗、兩端很細", Near(colH(100), 12, 1.5) AndAlso colH(25) < 4 AndAlso colH(60) > colH(25) AndAlso colH(60) < colH(100),
                  $"{colH(25)} {colH(60)} {colH(100)}")
        End Using
        Check("粗細內插：兩端 0、中間 1、1/4 處 0.5", Near(DrawGeometry.WidthAt(ln.WidthProfile, 0), 0, 0.001) AndAlso Near(DrawGeometry.WidthAt(ln.WidthProfile, 0.5), 1, 0.001) AndAlso
              Near(DrawGeometry.WidthAt(ln.WidthProfile, 0.25), 0.5, 0.001))
        Dim nst = DrawGeometry.NearestOnStroke(ln, New PointF(1.0F, 0.55F))
        Check("線上最近的點：中間、t = 0.5", nst.HasValue AndAlso Near(nst.Value.T, 0.5, 0.001) AndAlso Near(nst.Value.Distance, 0.05, 0.0001))
        Check("文字、特效筆不能設可變寬度；直線可以", DrawGeometry.CanVaryWidth(ln) AndAlso Not DrawGeometry.CanVaryWidth(New DrawLayer With {.Shape = DrawShape.Text, .Text = "A"}))
        Dim cs As New DrawLayer : cs.CopyStyleFrom(ln)
        Check("可變寬度是樣式的一部分（複製樣式時跟著、各自一份）", cs.WidthProfile IsNot Nothing AndAlso cs.WidthProfile.Count = 3 AndAlso cs.WidthProfile IsNot ln.WidthProfile)

        ' ---- 色票 ----
        Dim sr As New EditRecipe With {.Swatches = New List(Of ColorSwatch) From {New ColorSwatch With {.Id = "s1", .Name = "品牌紅", .Argb = Color.Red.ToArgb()}},
                                       .Drawings = New List(Of DrawLayer) From {
                                           New DrawLayer With {.Name = "x", .FillColorArgb = Color.Red.ToArgb(), .FillSwatch = "s1", .StrokeColorArgb = Color.Red.ToArgb(), .StrokeSwatch = "s1"},
                                           New DrawLayer With {.Name = "y", .FillColorArgb = Color.Blue.ToArgb(), .FillSwatch = "s1"}}}
        DrawGeometry.ChangeSwatch(sr, "s1", Color.Green.ToArgb())
        Check("改色票：連結的填色、線條色一起變綠", sr.Drawings(0).FillColorArgb = Color.Green.ToArgb() AndAlso sr.Drawings(0).StrokeColorArgb = Color.Green.ToArgb() AndAlso
              sr.Swatches(0).Argb = Color.Green.ToArgb())
        Check("自己改過顏色的不跟著變、解除連結", sr.Drawings(1).FillColorArgb = Color.Blue.ToArgb() AndAlso sr.Drawings(1).FillSwatch Is Nothing)
        Check("用到色票的圖層數", DrawGeometry.SwatchUsage(sr, "s1") = 1)
        DrawGeometry.RemoveSwatch(sr, "s1")
        Check("刪色票：顏色留著、連結拿掉", sr.Swatches Is Nothing AndAlso sr.Drawings(0).FillColorArgb = Color.Green.ToArgb() AndAlso sr.Drawings(0).FillSwatch Is Nothing)

        ' ---- 存檔讀回 ----
        Dim full As New EditRecipe With {.Guides = New List(Of Guide) From {New Guide With {.Vertical = True, .Pos = 0.3}, New Guide With {.Pos = 0.7}},
                                         .Swatches = New List(Of ColorSwatch) From {New ColorSwatch With {.Id = "k", .Name = "藍", .Argb = Color.Blue.ToArgb()}},
                                         .Drawings = New List(Of DrawLayer) From {dm, cn, ln, cl}}
        Dim back = RecipeStore.FromJson(RecipeStore.ToJson(full))
        Check("參考線、色票、尺寸線、連接線、可變寬度、圖樣存檔讀回", back.Guides.Count = 2 AndAlso back.Guides(0).Vertical AndAlso Near(back.Guides(1).Pos, 0.7, 0.000001) AndAlso
              back.Swatches(0).Name = "藍" AndAlso back.Drawings(0).Shape = DrawShape.Dimension AndAlso back.Drawings(0).DimPhotoPx = 1000 AndAlso
              back.Drawings(1).ConnElbow AndAlso back.Drawings(2).WidthProfile.Count = 3 AndAlso back.Drawings(3).FillPattern.Kind = PatternKind.Dots)
        Dim cr = full.Clone()
        cr.Guides(0).Pos = 0.9 : cr.Swatches(0).Argb = 0
        Check("複製文件：參考線、色票是各自的", full.Guides(0).Pos = 0.3 AndAlso full.Swatches(0).Argb = Color.Blue.ToArgb())
        DrawingRenderer.ClearCache()
    End Sub
End Module
