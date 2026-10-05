Imports PhotoEdit

''' <summary>圖層：文字貼圖與繪圖混排的順序、混合模式、隱藏、合併。</summary>
Module LayerTests

    ''' <summary>照片中央的實心方塊（繪圖圖層）。</summary>
    Private Function Box(c As Color) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.5, .Y = 0.5, .W = 0.4, .H = 0.4,
                                   .Filled = True, .Stroked = False, .FillColorArgb = c.ToArgb()}
    End Function

    ''' <summary>照片中央的大顆內建貼圖（星形）。</summary>
    Private Function StarSticker(c As Color) As Overlay
        Return New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = "star", .X = 0.5, .Y = 0.5, .Size = 0.9,
                                 .ColorArgb = c.ToArgb(), .Shadow = False}
    End Function

    Private Function CenterAfterLayers(recipe As EditRecipe, Optional bg As Color = Nothing) As Color
        Using bmp = Solid(100, 100, If(bg.IsEmpty, Color.White, bg))
            LayerStack.Draw(bmp, recipe)
            Return bmp.GetPixel(50, 50)
        End Using
    End Function

    Sub LayerTestsRun()
        Console.WriteLine("圖層")

        ' ---- 順序 ----
        Dim r As New EditRecipe With {
            .Overlays = New List(Of Overlay) From {StarSticker(Color.Blue)},
            .Drawings = New List(Of DrawLayer) From {Box(Color.Red)}}
        Dim o = LayerStack.Order(r)
        Check("沒排過：文字貼圖在下、繪圖在上（和舊版相同）", o.Count = 2 AndAlso o(0).IsOverlay AndAlso Not o(1).IsOverlay)
        Check("沒排過：繪圖蓋住貼圖", Near(CenterAfterLayers(r), Color.Red))

        LayerStack.Move(r, 0, 1)
        Dim o2 = LayerStack.Order(r)
        Check("貼圖移到最上：順序改變並記下 Id", o2(1).IsOverlay AndAlso r.LayerOrder.Count = 2 AndAlso
              r.Overlays(0).Id IsNot Nothing AndAlso r.Drawings(0).Id IsNot Nothing AndAlso r.Overlays(0).Id <> r.Drawings(0).Id)
        Check("貼圖移到最上：貼圖蓋住繪圖", Near(CenterAfterLayers(r), Color.Blue))

        Dim back = RecipeStore.FromJson(RecipeStore.ToJson(r))
        Check("圖層順序存檔再讀回不變", LayerStack.Order(back)(1).IsOverlay AndAlso back.Equals(r))
        Dim c = r.Clone()
        c.LayerOrder.Clear()
        Check("Clone 不共用 LayerOrder", r.LayerOrder.Count = 2)

        ' 新增的圖層放最上面；Id 重複（複製圖層）的第二個也當成新的。
        r.Drawings.Add(Box(Color.Lime))
        Check("排過之後新增的圖層在最上面", Not LayerStack.Order(r)(2).IsOverlay AndAlso Near(CenterAfterLayers(r), Color.Lime))
        Dim dup = r.Overlays(0).Clone()
        r.Overlays.Add(dup)
        Dim o3 = LayerStack.Order(r)
        Check("Id 重複時第二個當成新圖層（排在已排序的圖層上面）", o3.Count = 4 AndAlso o3(2).Item Is dup AndAlso o3(1).Item Is r.Overlays(0))
        LayerStack.SetOrder(r, o3)
        Check("SetOrder 會補上不重複的 Id", r.Overlays(0).Id <> r.Overlays(1).Id)
        Check("SetOrder 依堆疊順序重排兩個清單", r.Overlays(1) Is dup AndAlso r.Drawings(1).FillColorArgb = Color.Lime.ToArgb())

        ' 交換與放在上方
        Dim a = r.Drawings(0), b = r.Drawings(1)
        LayerStack.Swap(r, a, b)
        Check("交換兩個繪圖圖層", r.Drawings(0) Is b AndAlso r.Drawings(1) Is a)
        LayerStack.PlaceAbove(r, dup, r.Overlays(0))
        Check("放在指定圖層正上方", LayerStack.PositionOf(r, dup) = LayerStack.PositionOf(r, r.Overlays(0)) + 1)

        ' ---- 隱藏 ----
        Dim hid As New EditRecipe With {.Overlays = New List(Of Overlay) From {StarSticker(Color.Blue)}}
        hid.Overlays(0).Visible = False
        Check("隱藏的文字貼圖不畫", Near(CenterAfterLayers(hid), Color.White))
        Check("全部隱藏時不算有創意編輯", Not hid.HasCreative)

        ' ---- 混合模式 ----
        Check("色彩增值 0.5×0.5", Math.Abs(LayerBlend.Mix(BlendMode.Multiply, 0.5, 0.5) - 0.25) < 0.0001)
        Check("濾色 0.5、0.5", Math.Abs(LayerBlend.Mix(BlendMode.Screen, 0.5, 0.5) - 0.75) < 0.0001)
        Check("覆蓋：暗底變更暗、亮底變更亮", LayerBlend.Mix(BlendMode.Overlay, 0.2, 0.3) < 0.2 AndAlso LayerBlend.Mix(BlendMode.Overlay, 0.8, 0.7) > 0.8)
        Check("差異化與排除", Math.Abs(LayerBlend.Mix(BlendMode.Difference, 0.8, 0.3) - 0.5) < 0.0001 AndAlso
              Math.Abs(LayerBlend.Mix(BlendMode.Exclusion, 1, 1)) < 0.0001)
        Check("柔光 0.5 不改變底色", Math.Abs(LayerBlend.Mix(BlendMode.SoftLight, 0.3, 0.5) - 0.3) < 0.0001)
        Check("每個模式都有中文名稱", LayerBlend.Names.Length = [Enum].GetValues(GetType(BlendMode)).Length)

        Dim gray = Color.FromArgb(128, 128, 128)
        Dim white = Box(Color.White)
        white.Blend = BlendMode.Multiply
        Check("白色方塊色彩增值：底色不變", Near(CenterAfterLayers(New EditRecipe With {.Drawings = New List(Of DrawLayer) From {white}}, gray), gray, 3))
        white.Blend = BlendMode.Screen
        Check("白色方塊濾色：變白", Near(CenterAfterLayers(New EditRecipe With {.Drawings = New List(Of DrawLayer) From {white}}, gray), Color.White, 3))
        white.Blend = BlendMode.Difference
        Check("白色方塊差異化：反相", Near(CenterAfterLayers(New EditRecipe With {.Drawings = New List(Of DrawLayer) From {white}}, gray), Color.FromArgb(127, 127, 127), 3))
        Dim red = Box(Color.Red)
        red.Blend = BlendMode.Multiply : red.Opacity = 50
        Check("色彩增值 + 50% 不透明度", Near(CenterAfterLayers(New EditRecipe With {.Drawings = New List(Of DrawLayer) From {red}}), Color.FromArgb(255, 128, 128), 4))
        Dim star = StarSticker(Color.FromArgb(0, 0, 0))
        star.Blend = BlendMode.Screen
        Check("文字貼圖也套用混合模式（黑色濾色＝看不見）", Near(CenterAfterLayers(New EditRecipe With {.Overlays = New List(Of Overlay) From {star}}, gray), gray, 3))
        Using bmp = Solid(40, 40, Color.Transparent)
            Using src = Solid(20, 20, Color.Red)
                LayerBlend.Composite(bmp, src, New Rectangle(30, 30, 20, 20), 1, BlendMode.Multiply)
            End Using
            Check("透明底上的混合：顯示原色、超出邊界的部分略過", Near(bmp.GetPixel(35, 35), Color.Red) AndAlso bmp.GetPixel(35, 35).A = 255 AndAlso bmp.GetPixel(10, 10).A = 0)
        End Using

        ' ---- 合併 ----
        Dim m As New EditRecipe With {
            .Overlays = New List(Of Overlay) From {StarSticker(Color.Blue)},
            .Drawings = New List(Of DrawLayer) From {Box(Color.Red), Box(Color.Lime)}}
        m.Drawings(1).Opacity = 60
        m.Drawings(1).Blend = BlendMode.Multiply
        m.Drawings(1).W = 0.2
        LayerStack.Move(m, 0, 2) ' 星形移到最上：方塊、方塊、星形
        Dim before = RenderLayers(m)
        Dim reason As String = Nothing
        Dim merged = LayerStack.MergeDown(m, 1, 1.0, reason)
        Check("合併向下：兩個方塊變成一個點陣圖層", merged IsNot Nothing AndAlso m.Drawings.Count = 1 AndAlso merged.Shape = DrawShape.Raster AndAlso merged.Ops.Count = 2, reason)
        Check("合併向下：位置不變（在星形下面）", LayerStack.Order(m)(0).Item Is merged AndAlso LayerStack.Order(m)(1).IsOverlay)
        Dim after = RenderLayers(m)
        Check("合併向下：畫面不變（保留不透明度與混合模式）", SameImage(before, after), DiffInfo(before, after))

        Dim mergedAll = LayerStack.MergeDown(m, 1, 1.0, reason)
        Check("文字貼圖也能合併進點陣圖層", mergedAll IsNot Nothing AndAlso (m.Overlays Is Nothing OrElse m.Overlays.Count = 0) AndAlso m.Drawings.Count = 1, reason)
        Check("合併文字貼圖後畫面不變", SameImage(before, RenderLayers(m)), DiffInfo(before, RenderLayers(m)))
        Dim rb = DrawGeometry.RasterBounds(mergedAll)
        Check("合併後的範圍包含文字貼圖", rb.Width > 0.8 AndAlso rb.Height > 0.8, rb.ToString())
        Dim json = RecipeStore.FromJson(RecipeStore.ToJson(m))
        Check("合併圖層存檔再讀回畫面不變", SameImage(before, RenderLayers(json)))
        DrawingRenderer.ClearCache()
        Check("清空快取後重算一樣", SameImage(before, RenderLayers(m)))

        Dim v As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {Box(Color.Red), Box(Color.Lime), Box(Color.Blue)}}
        v.Drawings(1).Visible = False
        Dim mv = LayerStack.MergeVisible(v, 1.0, reason)
        Check("合併可見：隱藏的圖層保留", mv IsNot Nothing AndAlso v.Drawings.Count = 2 AndAlso v.Drawings.Any(Function(d) Not d.Visible AndAlso d.FillColorArgb = Color.Lime.ToArgb()), reason)
        Check("合併可見：合併結果在最上面", LayerStack.Order(v).Last().Item Is mv)

        Dim photoText As New EditRecipe With {
            .Overlays = New List(Of Overlay) From {New Overlay With {.Kind = OverlayKind.Text, .Text = "鏤空", .FillMode = TextFill.Photo}},
            .Drawings = New List(Of DrawLayer) From {Box(Color.Red)}}
        Check("「照片本身」填字不能合併", LayerStack.MergeDown(photoText, 1, 1, reason) Is Nothing AndAlso reason IsNot Nothing AndAlso photoText.Drawings.Count = 1)
        Dim locked As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {Box(Color.Red), Box(Color.Lime)}}
        locked.Drawings(0).Locked = True
        Check("鎖定的圖層不能合併", LayerStack.MergeDown(locked, 1, 1, reason) Is Nothing)
        Check("最下面的圖層不能合併向下", LayerStack.MergeDown(locked, 0, 1, reason) Is Nothing)

        ' ---- 點陣化保留位置與混合 ----
        Dim vec = Box(Color.Red)
        vec.Id = "abc" : vec.Blend = BlendMode.Screen
        Dim ras = DrawGeometry.Rasterize(vec)
        Check("點陣化保留 Id 與混合模式（位置不跳）", ras.Id = "abc" AndAlso ras.Blend = BlendMode.Screen AndAlso ras.Ops(0).Blend = BlendMode.Normal)

        ' ---- 文字樣式不動圖層屬性 ----
        Dim t As New Overlay With {.Kind = OverlayKind.Text, .Id = "t1", .Visible = False, .Blend = BlendMode.Overlay}
        t.CopyStyleFrom(New Overlay With {.Kind = OverlayKind.Text})
        Check("套用文字樣式不改 Id、顯示與混合模式", t.Id = "t1" AndAlso Not t.Visible AndAlso t.Blend = BlendMode.Overlay)
    End Sub

    Private Function RenderLayers(r As EditRecipe) As Bitmap
        Dim bmp = Solid(120, 100, Color.FromArgb(200, 180, 90))
        LayerStack.Draw(bmp, r)
        Return bmp
    End Function

    Private Function SameImage(a As Bitmap, b As Bitmap) As Boolean
        Return MaxDiff(a, b) <= 3
    End Function

    Private Function DiffInfo(a As Bitmap, b As Bitmap) As String
        Return "最大差異 " & MaxDiff(a, b)
    End Function

    Private Function MaxDiff(a As Bitmap, b As Bitmap) As Integer
        Dim m = 0
        For y = 0 To a.Height - 1 Step 2
            For x = 0 To a.Width - 1 Step 2
                Dim p = a.GetPixel(x, y), q = b.GetPixel(x, y)
                m = Math.Max(m, Math.Max(Math.Abs(CInt(p.R) - q.R), Math.Max(Math.Abs(CInt(p.G) - q.G), Math.Abs(CInt(p.B) - q.B))))
            Next
        Next
        Return m
    End Function
End Module
