Imports PhotoEdit

''' <summary>繪圖圖層：模型、幾何、控制點、點選與筆刷算圖。</summary>
Module DrawingTests

    Private Function Box(shape As DrawShape, x As Double, y As Double, w As Double, h As Double) As DrawLayer
        Dim d As New DrawLayer With {.Shape = shape, .X = x, .Y = y, .W = w, .H = h, .Seed = 5}
        DrawGeometry.ApplyDefaults(d)
        Return d
    End Function

    Private Function Near(a As Double, b As Double, Optional tol As Double = 0.0005) As Boolean
        Return Math.Abs(a - b) <= tol
    End Function

    ''' <summary>bmp 裡和背景色不同的像素數。</summary>
    Private Function Painted(bmp As Bitmap, background As Color) As Integer
        Dim n = 0
        For y = 0 To bmp.Height - 1 Step 2
            For x = 0 To bmp.Width - 1 Step 2
                Dim p = bmp.GetPixel(x, y)
                If Math.Abs(CInt(p.R) - background.R) + Math.Abs(CInt(p.G) - background.G) + Math.Abs(CInt(p.B) - background.B) > 24 Then n += 1
            Next
        Next
        Return n
    End Function

    Sub DrawingTestsRun()
        Console.WriteLine("繪圖")

        ' ---- 模型 ----
        Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Freehand, .Strokes = New List(Of DrawStroke) From {
                New DrawStroke With {.Points = New List(Of DrawPoint) From {New DrawPoint(0.1F, 0.1F, 0.5F), New DrawPoint(0.3F, 0.2F)}}}}}}
        Check("有繪圖圖層時不是原圖", Not r.IsIdentity AndAlso r.HasCreative)
        Dim c = r.Clone()
        c.Drawings(0).Strokes(0).Points(0).X = 0.9F
        Check("Clone 為深複製（筆畫點不共用）", r.Drawings(0).Strokes(0).Points(0).X = 0.1F)
        Dim json = System.Text.Json.JsonSerializer.Serialize(r)
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(json)
        Check("繪圖圖層 JSON 來回轉換一致", back.Equals(r) AndAlso back.Drawings(0).Strokes(0).Points(0).P = 0.5F)
        Check("空的繪圖清單視同沒有", New EditRecipe With {.Drawings = New List(Of DrawLayer)()}.Equals(New EditRecipe()))
        Dim hidden = r.Clone()
        hidden.Drawings(0).Visible = False
        Check("全部隱藏時不算有創意特效", Not hidden.HasCreative)

        ' ---- 幾何 ----
        Dim allShapes = [Enum].GetValues(GetType(DrawShape)).Cast(Of DrawShape)().Where(Function(s) DrawGeometry.IsBox(s)).ToList()
        Check("每種方框形狀都有外形", allShapes.All(Function(s) DrawGeometry.Figures(Box(s, 0.5, 0.5, 0.3, 0.2)).Count > 0))
        Check("工具名稱 22 個（含油漆桶、漸層、填滿圖層）、筆刷名稱 21 個", DrawGeometry.ShapeNames.Length = 22 AndAlso DrawGeometry.BrushNames.Length = 21)
        Dim star = Box(DrawShape.Star5, 0.5, 0.5, 0.2, 0.2)
        Check("五角星 10 個頂點", DrawGeometry.Figures(star)(0).Points.Length = 10)
        Dim rect = Box(DrawShape.Rectangle, 0.5, 0.5, 0.4, 0.2)
        Dim b = DrawGeometry.Bounds(rect)
        Check("矩形範圍 = 中心 ± 寬高一半", Near(b.Left, 0.3) AndAlso Near(b.Right, 0.7) AndAlso Near(b.Top, 0.4) AndAlso Near(b.Bottom, 0.6))
        rect.Rotation = 90
        b = DrawGeometry.Bounds(rect)
        Check("轉 90 度後寬高互換", Near(b.Width, 0.2, 0.001) AndAlso Near(b.Height, 0.4, 0.001))

        ' 圖說：尖端在框外時外形多出指示，在框內時沒有
        Dim callout = Box(DrawShape.CalloutEllipse, 0.5, 0.5, 0.3, 0.2)
        Dim withTail = DrawGeometry.Figures(callout)(0).Points
        Dim tipWorld = DrawGeometry.LocalToWorld(callout, New PointF(CSng(callout.TailX), CSng(callout.TailY)))
        Check("橢圓圖說的外形包含指示尖端", withTail.Any(Function(p) DrawGeometry.Dist(p, tipWorld) < 0.0001))
        callout.TailX = 0.01 : callout.TailY = 0.01
        Dim noTail = DrawGeometry.Figures(callout)(0).Points
        Check("尖端在框內時不加指示", noTail.Length = 120)
        Dim cloud = Box(DrawShape.CalloutCloud, 0.5, 0.5, 0.3, 0.2)
        Check("雲朵圖說：本體＋三個小泡泡", DrawGeometry.Figures(cloud).Count = 4)
        Dim shout = Box(DrawShape.CalloutShout, 0.5, 0.5, 0.3, 0.2)
        Dim shoutTip = DrawGeometry.LocalToWorld(shout, New PointF(CSng(shout.TailX), CSng(shout.TailY)))
        Check("吶喊框有一根尖刺拉到指示點", DrawGeometry.Figures(shout)(0).Points.Any(Function(p) DrawGeometry.Dist(p, shoutTip) < 0.0001))
        For Each s In {DrawShape.CalloutRect, DrawShape.CalloutBubble}
            Dim co = Box(s, 0.5, 0.5, 0.3, 0.2)
            Dim tip = DrawGeometry.LocalToWorld(co, New PointF(CSng(co.TailX), CSng(co.TailY)))
            Check($"{DrawGeometry.ShapeNames(CInt(s))}的指示會到尖端", DrawGeometry.HitTest(co, tip, 0.002))
        Next

        ' ---- 點選 ----
        Dim filled = Box(DrawShape.Ellipse, 0.5, 0.5, 0.2, 0.2)
        Check("點橢圓中心選得到", DrawGeometry.HitTest(filled, New PointF(0.5F, 0.5F), 0.001))
        Check("點外面選不到", Not DrawGeometry.HitTest(filled, New PointF(0.8F, 0.8F), 0.001))
        Dim line As New DrawLayer With {.Shape = DrawShape.Line, .StrokeWidth = 0.01,
                                        .Points = New List(Of DrawPoint) From {New DrawPoint(0.1F, 0.1F), New DrawPoint(0.5F, 0.1F)}}
        Check("點在線上（線寬內）選得到", DrawGeometry.HitTest(line, New PointF(0.3F, 0.104F), 0))
        Check("離線太遠選不到", Not DrawGeometry.HitTest(line, New PointF(0.3F, 0.15F), 0))

        ' ---- 編輯 ----
        Dim start = Box(DrawShape.Rectangle, 0.5, 0.5, 0.2, 0.1)
        start.Rotation = 30
        Dim edited = start.Clone()
        Dim fixedCorner = DrawGeometry.LocalToWorld(start, DrawGeometry.BoxHandleLocal(start, 0))
        DrawGeometry.ResizeBox(edited, start, 4, DrawGeometry.LocalToWorld(start, New PointF(0.2F, 0.1F)), False)
        Dim corner = DrawGeometry.LocalToWorld(edited, DrawGeometry.BoxHandleLocal(edited, 0))
        Check("拖右下角縮放（旋轉中）：左上角固定", DrawGeometry.Dist(corner, fixedCorner) < 0.0001)
        Check("拖右下角縮放：新寬高", Near(edited.W, 0.3) AndAlso Near(edited.H, 0.15))
        Dim aspect = start.Clone()
        DrawGeometry.ResizeBox(aspect, start, 2, DrawGeometry.LocalToWorld(start, New PointF(0.3F, -0.06F)), True)
        Check("Shift 縮放保持比例", Near(aspect.W / aspect.H, 2, 0.001))
        Dim tiny = start.Clone()
        DrawGeometry.ResizeBox(tiny, start, 3, DrawGeometry.LocalToWorld(start, New PointF(-0.5F, 0)), False)
        Check("縮放不會變成負寬度", tiny.W > 0)

        Dim rr = Box(DrawShape.RoundRect, 0.5, 0.5, 0.4, 0.2)
        DrawGeometry.SetParamHandle(rr, 0, DrawGeometry.LocalToWorld(rr, New PointF(-0.2F + 0.05F, -0.1F)))
        Check("圓角黃點：圓角 = 0.05 ÷ 短邊", Near(rr.Param1, 0.25))
        DrawGeometry.SetParamHandle(star, 0, DrawGeometry.LocalToWorld(star, New PointF(0, 0.02F)))
        Check("星形黃點往中心拖變瘦", star.Param1 < 0.3)
        Dim arrow = Box(DrawShape.Arrow, 0.5, 0.5, 0.4, 0.2)
        DrawGeometry.SetParamHandle(arrow, 0, DrawGeometry.LocalToWorld(arrow, New PointF(0.1F, -0.02F)))
        Check("箭頭黃點：箭頭長度與箭身粗細", Near(arrow.Param1, 0.25) AndAlso Near(arrow.Param2, 0.2))
        Dim crect = Box(DrawShape.CalloutRect, 0.5, 0.5, 0.4, 0.2)
        Check("圓角圖說有兩個黃點（圓角、指示）", DrawGeometry.ParamHandles(crect).Count = 2)
        DrawGeometry.SetParamHandle(crect, 1, New PointF(0.9F, 0.9F))
        Dim local = DrawGeometry.WorldToLocal(crect, New PointF(0.9F, 0.9F))
        Check("拖指示黃點改變指向", Near(crect.TailX, local.X) AndAlso Near(crect.TailY, local.Y))

        Dim free As New DrawLayer With {.Shape = DrawShape.Freehand, .StrokeWidth = 0.01, .Strokes = New List(Of DrawStroke) From {
            New DrawStroke With {.Points = New List(Of DrawPoint) From {New DrawPoint(0.1F, 0.1F), New DrawPoint(0.3F, 0.3F)}}}}
        Dim scaled = free.Clone()
        DrawGeometry.ScalePoints(scaled, free, New RectangleF(0.1F, 0.1F, 0.2F, 0.2F), New RectangleF(0.1F, 0.1F, 0.4F, 0.4F))
        Check("自由繪製依外框放大兩倍（線寬跟著變粗）",
              Near(scaled.Strokes(0).Points(1).X, 0.5) AndAlso Near(scaled.StrokeWidth, 0.02))
        DrawGeometry.Offset(scaled, 0.1, 0)
        Check("移動圖層：所有點一起移動", Near(scaled.Strokes(0).Points(0).X, 0.2))

        ' ---- 算圖 ----
        Dim bg = Color.FromArgb(40, 80, 120)
        Dim results As New List(Of String)()
        For Each brush In [Enum].GetValues(GetType(BrushKind)).Cast(Of BrushKind)().Where(Function(k) Not DrawLayer.SamplesCanvas(k))
            Using bmp = Solid(300, 200, bg)
                Dim layer As New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = brush, .StrokeWidth = 0.08, .Seed = 3,
                                                 .StrokeColorArgb = Color.FromArgb(250, 230, 90).ToArgb(),
                                                 .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = New List(Of DrawPoint) From {
                                                     New DrawPoint(0.2F, 0.5F), New DrawPoint(0.7F, 0.4F), New DrawPoint(1.2F, 0.6F)}}}}
                DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {layer}})
                If Painted(bmp, bg) < 50 Then results.Add(DrawGeometry.BrushNames(CInt(brush)))
            End Using
        Next
        Check("18 種向量筆刷都畫得出東西（混色、塗抹、仿製只在點陣圖層，另外測）", results.Count = 0, String.Join("、", results))
        For Each fx In [Enum].GetValues(GetType(FxKind)).Cast(Of FxKind)()
            Using bmp = Solid(300, 200, bg)
                Dim layer As New DrawLayer With {.Shape = DrawShape.Line, .Brush = BrushKind.FX, .Fx = fx, .StrokeWidth = 0.08, .Seed = 3,
                                                 .StrokeColorArgb = Color.FromArgb(250, 230, 90).ToArgb(),
                                                 .Points = New List(Of DrawPoint) From {New DrawPoint(0.3F, 0.5F), New DrawPoint(1.2F, 0.5F)}}
                DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {layer}})
                Check($"特效筆「{DrawGeometry.FxNames(CInt(fx))}」畫得出東西", Painted(bmp, bg) > 50)
            End Using
        Next

        ' 目錄：每個效果／材質都在某個分類裡、名稱不重複
        Dim fxValues = [Enum].GetValues(GetType(FxKind)).Cast(Of Integer)().ToList()
        Dim fxInCatalog = EffectCatalog.FxCategories.SelectMany(Function(cat) cat.Items).Select(Function(i) i.Value).ToList()
        Check("68 種特效都在分類目錄裡（7 類）", fxValues.Count = 68 AndAlso fxInCatalog.Count = 68 AndAlso fxValues.All(Function(v) fxInCatalog.Contains(v)) AndAlso
              EffectCatalog.FxCategories.Length = 7)
        Dim matValues = [Enum].GetValues(GetType(MaterialKind)).Cast(Of Integer)().ToList()
        Dim matInCatalog = EffectCatalog.MaterialCategories.SelectMany(Function(cat) cat.Items).Select(Function(i) i.Value).ToList()
        Check("64 種材質都在分類目錄裡（6 類）", matValues.Count = 64 AndAlso matInCatalog.Count = 64 AndAlso matValues.All(Function(v) matInCatalog.Contains(v)) AndAlso
              EffectCatalog.MaterialCategories.Length = 6)
        Check("舊編輯檔的特效與材質數值不變", FxKind.Blood = 3 AndAlso MaterialKind.Skin = 3 AndAlso EffectCatalog.FxNames(0) = "火焰" AndAlso EffectCatalog.MaterialNames(1) = "木紋")

        ' 每種材質都畫得出有紋理（不是單一顏色）的填色
        Dim flat As New List(Of String)()
        For Each m In [Enum].GetValues(GetType(MaterialKind)).Cast(Of MaterialKind)()
            Using bmp = Solid(240, 160, Color.Black)
                Dim layer As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.75, .Y = 0.5, .W = 1.4, .H = 0.9, .Brush = BrushKind.Texture,
                                                 .Material = m, .Filled = True, .Stroked = False, .Seed = 4, .StrokeColorArgb = Color.SteelBlue.ToArgb()}
                DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {layer}})
                Dim lums As New List(Of Double)()
                For y = 20 To 140 Step 6
                    For x = 20 To 220 Step 6
                        Dim p = bmp.GetPixel(x, y)
                        lums.Add(p.R * 0.3 + p.G * 0.59 + p.B * 0.11)
                    Next
                Next
                Dim mean = lums.Average(), sd = Math.Sqrt(lums.Select(Function(l) (l - mean) ^ 2).Average())
                If mean < 15 OrElse sd < 1.5 Then flat.Add($"{EffectCatalog.MaterialNames(CInt(m))}({mean:0}/{sd:0.0})")
            End Using
        Next
        Check("64 種材質都畫得出紋理", flat.Count = 0, String.Join("、", flat))

        ' 同一圖層每次結果相同（紋理用固定亂數種子）
        Dim chalk As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.75, .Y = 0.5, .W = 0.6, .H = 0.5, .Brush = BrushKind.Chalk,
                                         .StrokeWidth = 0.05, .Filled = True, .Seed = 9}
        Dim recipe As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {chalk}}
        Using a = Solid(300, 200, bg), b2 = Solid(300, 200, bg)
            DrawingRenderer.DrawLayers(a, recipe)
            DrawingRenderer.ClearCache()
            DrawingRenderer.DrawLayers(b2, recipe)
            Dim same = True
            For y = 0 To 199 Step 3
                For x = 0 To 299 Step 3
                    If a.GetPixel(x, y) <> b2.GetPixel(x, y) Then same = False
                Next
            Next
            Check("同一圖層重算結果完全相同", same)
        End Using

        ' 預覽與全尺寸：覆蓋比例一致（尺寸以照片高度為準）
        Dim marker As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.6, .Y = 0.5, .W = 0.4, .H = 0.4, .Filled = True,
                                          .FillColorArgb = Color.White.ToArgb(), .Stroked = False}
        Dim mr As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {marker}}
        Using small = Solid(240, 160, bg), big = Solid(960, 640, bg)
            DrawingRenderer.DrawLayers(small, mr)
            DrawingRenderer.DrawLayers(big, mr)
            Dim fs = Painted(small, bg) / (240 * 160 / 4.0), fb = Painted(big, bg) / (960 * 640 / 4.0)
            Check("預覽與全尺寸的圖形比例相同", Math.Abs(fs - fb) < 0.01, $"{fs:0.000} vs {fb:0.000}")
            Check("矩形畫在正確位置（中心是白色）", small.GetPixel(96, 80).R > 240)
        End Using

        ' 不透明度與隱藏
        Using bmp = Solid(240, 160, Color.Black)
            marker.Opacity = 50
            DrawingRenderer.DrawLayers(bmp, mr)
            Dim v = bmp.GetPixel(96, 80).R
            Check("不透明度 50%：白色疊在黑色上變中灰", v > 110 AndAlso v < 145, v.ToString())
        End Using
        Using bmp = Solid(240, 160, Color.Black)
            marker.Visible = False
            DrawingRenderer.DrawLayers(bmp, mr)
            Check("隱藏的圖層不畫", bmp.GetPixel(96, 80).R = 0)
        End Using

        ' 圖說文字
        Dim talk = Box(DrawShape.CalloutRect, 0.75, 0.5, 0.9, 0.5)
        talk.Filled = True : talk.FillColorArgb = Color.White.ToArgb() : talk.Text = "到了！" : talk.TextColorArgb = Color.Black.ToArgb() : talk.TextSize = 0.12
        Using bmp = Solid(300, 200, bg)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {talk}})
            Dim dark = 0
            For y = 70 To 130
                For x = 80 To 220
                    If bmp.GetPixel(x, y).R < 80 Then dark += 1
                Next
            Next
            Check("圖說裡畫出文字", dark > 30, dark.ToString())
        End Using

        ' 完整算圖流程也會畫繪圖圖層
        Using src = Solid(200, 100, Color.Black)
            Dim full As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {
                New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 1, .Y = 0.5, .W = 0.5, .H = 0.5, .Filled = True, .FillColorArgb = Color.White.ToArgb()}}}
            Using out = ImagePipeline.Render(src, full)
                Check("算圖流程包含繪圖圖層", out.GetPixel(100, 50).R > 240)
            End Using
        End Using
    End Sub
End Module
