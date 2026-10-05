Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>選取區：形狀與加減、遮罩編碼、羽化、範圍、換算到原圖、局部調整、填色描邊、圖片物件。</summary>
Module SelectionTests

    Private Function Rect(mode As SelectionMode, x As Double, y As Double, w As Double, h As Double) As SelectionOp
        Return New SelectionOp With {.Mode = mode, .Shape = SelectionShape.Rectangle, .X = x, .Y = y, .W = w, .H = h}
    End Function

    Private Function At(m As Byte(), w As Integer, x As Integer, y As Integer) As Integer
        Return m(y * w + x)
    End Function

    Sub SelectionTestsRun(tempDir As String)
        Console.WriteLine("選取")

        ' ---- 形狀與組合 ----
        Dim s As New SelectionSpec()
        s.Apply(Rect(SelectionMode.Replace, 0.1, 0.1, 0.4, 0.4))
        Dim m = SelectionMask.Render(s, 100, 100)
        Check("矩形：裡面選到、外面沒有", At(m, 100, 30, 30) = 255 AndAlso At(m, 100, 70, 70) = 0)
        s.Apply(Rect(SelectionMode.Add, 0.5, 0.5, 0.4, 0.4))
        m = SelectionMask.Render(s, 100, 100)
        Check("加入：兩塊都選到", At(m, 100, 30, 30) = 255 AndAlso At(m, 100, 70, 70) = 255 AndAlso At(m, 100, 30, 70) = 0)
        s.Apply(Rect(SelectionMode.Subtract, 0.2, 0.2, 0.1, 0.1))
        m = SelectionMask.Render(s, 100, 100)
        Check("減去：挖掉一個洞", At(m, 100, 25, 25) = 0 AndAlso At(m, 100, 15, 15) = 255)
        s.Apply(Rect(SelectionMode.Intersect, 0, 0, 0.45, 1))
        m = SelectionMask.Render(s, 100, 100)
        Check("交集：只留左邊重疊的部分", At(m, 100, 15, 15) = 255 AndAlso At(m, 100, 70, 70) = 0)
        s.Apply(New SelectionOp With {.Mode = SelectionMode.Invert})
        m = SelectionMask.Render(s, 100, 100)
        Check("反轉", At(m, 100, 15, 15) = 0 AndAlso At(m, 100, 70, 70) = 255)
        s.Apply(Rect(SelectionMode.Replace, 0, 0, 1, 1))
        Check("新增：取代之前全部步驟", s.Ops.Count = 1)

        Dim ell As New SelectionSpec()
        ell.Apply(New SelectionOp With {.Shape = SelectionShape.Ellipse, .X = 0, .Y = 0, .W = 1, .H = 1})
        m = SelectionMask.Render(ell, 100, 100)
        Check("橢圓：中心選到、角落沒有", At(m, 100, 50, 50) = 255 AndAlso At(m, 100, 2, 2) = 0)
        Dim tri As New SelectionSpec()
        tri.Apply(New SelectionOp With {.Shape = SelectionShape.Polygon, .Points = New List(Of Double) From {0, 0, 1, 0, 0, 1}})
        m = SelectionMask.Render(tri, 100, 100)
        Check("多邊形（三角形）", At(m, 100, 20, 20) = 255 AndAlso At(m, 100, 80, 80) = 0)
        Dim all As New SelectionSpec()
        all.Apply(New SelectionOp With {.Shape = SelectionShape.All})
        Check("全選", SelectionMask.Render(all, 10, 10).All(Function(v) v = 255))

        ' ---- 遮罩編碼 ----
        Dim raw(40 * 20 - 1) As Byte
        For y = 0 To 19
            For x = 0 To 19
                raw(y * 40 + x) = 255
            Next
        Next
        Dim png = SelectionMask.EncodeMask(raw, 40, 20)
        Dim back = SelectionMask.DecodeMask(png, 40, 20)
        Check("遮罩存成 PNG 再讀回一樣", back.SequenceEqual(raw))
        Dim scaled = SelectionMask.DecodeMask(png, 80, 40)
        Check("遮罩放大到不同尺寸", At(scaled, 80, 10, 10) > 250 AndAlso At(scaled, 80, 70, 30) < 5)

        ' ---- 羽化與範圍 ----
        Dim f As New SelectionSpec With {.Feather = 100}
        f.Apply(Rect(SelectionMode.Replace, 0.25, 0.25, 0.5, 0.5))
        m = SelectionMask.Render(f, 200, 200)
        Dim edge = At(m, 200, 50, 100)
        Check("羽化：邊緣半透明、中心全選", edge > 40 AndAlso edge < 215 AndAlso At(m, 200, 100, 100) = 255, edge.ToString())
        Dim b = SelectionMask.Bounds(s)
        Check("範圍", Math.Abs(b.Width - 1) < 0.01 AndAlso Math.Abs(b.Height - 1) < 0.01)
        Dim b2 = SelectionMask.Bounds(tri, 400, 400)
        Check("三角形範圍", b2.X < 0.01 AndAlso Math.Abs(b2.Right - 1) < 0.01)
        Check("空的選取區沒有範圍", SelectionMask.Bounds(New SelectionSpec()).IsEmpty)

        ' ---- 描邊遮罩 ----
        Dim sq As New SelectionSpec()
        sq.Apply(Rect(SelectionMode.Replace, 0.25, 0.25, 0.5, 0.5))
        m = SelectionMask.Render(sq, 100, 100)
        Dim outside = SelectionMask.Outline(m, 100, 100, 4, 2)
        Check("外側描邊：在邊外、不在裡面", At(outside, 100, 23, 50) = 255 AndAlso At(outside, 100, 27, 50) = 0 AndAlso At(outside, 100, 50, 50) = 0)
        Dim inner = SelectionMask.Outline(m, 100, 100, 4, 0)
        Check("內側描邊：在邊內", At(inner, 100, 26, 50) = 255 AndAlso At(inner, 100, 23, 50) = 0)

        ' ---- 換算到原圖（去背用）----
        Dim left As New SelectionSpec()
        left.Apply(Rect(SelectionMode.Replace, 0, 0, 0.5, 1))
        Dim src = SelectionMask.ToSource(left, New EditRecipe(), 100, 80, 100, 80)
        m = SelectionMask.Render(src, 100, 80)
        Check("沒有幾何變換：原圖也是左半邊", At(m, 100, 20, 40) = 255 AndAlso At(m, 100, 80, 40) = 0)
        Dim rot = SelectionMask.ToSource(left, New EditRecipe With {.Rotation = 90}, 100, 80, 80, 100)
        m = SelectionMask.Render(rot, 100, 80)
        Check("向右轉 90 度：畫面左半邊＝原圖下半部", At(m, 100, 50, 70) = 255 AndAlso At(m, 100, 50, 10) = 0)
        Dim cropped = SelectionMask.ToSource(left, New EditRecipe With {.Crop = New CropRect(0.5, 0, 0.5, 1)}, 100, 80, 50, 80)
        m = SelectionMask.Render(cropped, 100, 80)
        Check("裁切後：畫面左半邊＝原圖中間那一條", At(m, 100, 60, 40) = 255 AndAlso At(m, 100, 30, 40) = 0 AndAlso At(m, 100, 90, 40) = 0)
        Dim inv = SelectionMask.Render(SelectionMask.Inverted(left), 100, 80)
        Check("Inverted 不改原本的選取區", left.Ops.Count = 1 AndAlso At(inv, 100, 80, 40) = 255)

        ' ---- 只調整選取區 ----
        Using photo = Solid(100, 100, Color.FromArgb(100, 100, 100))
            Dim r As New EditRecipe With {.LocalAdjustments = New List(Of LocalAdjustment) From {
                New LocalAdjustment With {.Kind = LocalKind.Selection, .Region = sq.Clone(), .Exposure = 1}}}
            Using out = ImagePipeline.Render(photo, r)
                Check("選取區調整：裡面變亮、外面不變", out.GetPixel(50, 50).R > 150 AndAlso Math.Abs(CInt(out.GetPixel(5, 5).R) - 100) <= 2,
                      out.GetPixel(50, 50).ToString() & " " & out.GetPixel(5, 5).ToString())
            End Using
            Dim json = RecipeStore.FromJson(RecipeStore.ToJson(r))
            Check("選取區調整存檔再讀回", json.LocalAdjustments(0).Kind = LocalKind.Selection AndAlso json.Equals(r))
        End Using

        ' ---- 填色與描邊圖層 ----
        Dim fill As New DrawLayer With {.Shape = DrawShape.Raster, .Name = "填色", .Ops = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Raster, .Region = sq.Clone(), .Filled = True, .Stroked = False,
                                .FillColorArgb = Color.Red.ToArgb(), .Opacity = 100, .Param1 = 1}}}
        Using bmp = Solid(100, 100, Color.White)
            LayerStack.Draw(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {fill}})
            Check("填色：選取區變紅、外面不變", Near(bmp.GetPixel(50, 50), Color.Red) AndAlso Near(bmp.GetPixel(5, 5), Color.White))
        End Using
        Dim rb = DrawGeometry.RasterBounds(fill)
        Check("填色圖層的範圍＝選取區", Math.Abs(rb.X - 0.25) < 0.02 AndAlso Math.Abs(rb.Width - 0.5) < 0.02, rb.ToString())
        Dim stroke As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Raster, .Region = sq.Clone(), .Filled = False, .Stroked = True,
                                .StrokeColorArgb = Color.Blue.ToArgb(), .StrokeWidth = 0.04, .Param2 = 1, .Opacity = 100, .Param1 = 1}}}
        Using bmp = Solid(100, 100, Color.White)
            LayerStack.Draw(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {stroke}})
            Check("描邊：邊上是藍色、中間不變", Near(bmp.GetPixel(25, 50), Color.Blue) AndAlso Near(bmp.GetPixel(50, 50), Color.White))
        End Using

        ' ---- 配方 ----
        Dim withSel As New EditRecipe With {.Selection = sq.Clone()}
        Check("選取區不算編輯照片", withSel.IsIdentity)
        Dim rt = RecipeStore.FromJson(RecipeStore.ToJson(withSel))
        Check("選取區存檔再讀回", rt.Selection IsNot Nothing AndAlso rt.Selection.Ops.Count = 1 AndAlso rt.Equals(withSel))
        Check("選取區改變算是修改", Not withSel.Equals(New EditRecipe()))
        Check("空的選取區等於沒有選取", New EditRecipe With {.Selection = New SelectionSpec()}.Equals(New EditRecipe()))
        Dim cl = withSel.Clone()
        cl.Selection.Ops.Clear()
        Check("Clone 不共用選取區", withSel.Selection.Ops.Count = 1)
        Dim cut As New CutoutSettings With {.Regions = New List(Of SelectionSpec) From {sq}}
        Check("去背範圍 Clone", cut.Clone().Regions(0) IsNot sq AndAlso cut.Clone().Regions.Count = 1)

        ' ---- 圖片物件（完整路徑）----
        Dim file = Path.Combine(tempDir, "物件.png")
        Using piece = Solid(20, 10, Color.Lime)
            piece.Save(file, ImageFormat.Png)
        End Using
        Check("完整路徑的圖片可以當貼圖", StickerLibrary.GetImage(file) IsNot Nothing)
        Using bmp = Solid(100, 100, Color.White)
            LayerStack.Draw(bmp, New EditRecipe With {.Overlays = New List(Of Overlay) From {
                New Overlay With {.Kind = OverlayKind.Image, .ImagePath = file, .X = 0.5, .Y = 0.5, .Size = 0.2, .Shadow = False}}})
            Check("圖片物件畫在指定位置與大小（高 20%、寬 40%）", Near(bmp.GetPixel(50, 50), Color.Lime) AndAlso Near(bmp.GetPixel(32, 50), Color.Lime) AndAlso
                  Near(bmp.GetPixel(50, 38), Color.White) AndAlso Near(bmp.GetPixel(25, 50), Color.White))
        End Using
        Check("專案檔會把物件圖片包進去", ProjectFile.ReferencedFiles(New EditRecipe With {.Overlays = New List(Of Overlay) From {
            New Overlay With {.Kind = OverlayKind.Image, .ImagePath = file}}}).Contains(file))
    End Sub
End Module
