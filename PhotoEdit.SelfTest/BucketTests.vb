Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>油漆桶：相近顏色範圍、容許度、相鄰、封閉缺口、擴張、柔邊；點陣圖層裡的填色操作（取樣所有圖層、筆刷質感、原照片）。</summary>
Module BucketTests

    Private Function White(w As Integer, h As Integer) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(b)
            g.Clear(Color.White)
        End Using
        Return b
    End Function

    ''' <summary>白底上一個黑色圓圈（線寬 3）；gapDeg > 0 時右邊留一個缺口。</summary>
    Private Function Ring(gapPx As Integer) As Bitmap
        Dim b = White(200, 200)
        Using g = Graphics.FromImage(b), p As New Pen(Color.Black, 3)
            g.SmoothingMode = SmoothingMode.AntiAlias
            If gapPx <= 0 Then
                g.DrawEllipse(p, 40, 40, 120, 120)
            Else
                ' 缺口在右邊（0°）：弧長 = 半徑 × 角度
                Dim deg = CSng(gapPx / 60.0 * 180 / Math.PI)
                g.DrawArc(p, 40, 40, 120, 120, deg / 2, 360 - deg)
            End If
        End Using
        Return b
    End Function

    Private Function Fill(b As Bitmap, x As Integer, y As Integer, Optional tol As Integer = 32, Optional contiguous As Boolean = True,
                          Optional gap As Integer = 0, Optional expand As Integer = 0, Optional aa As Boolean = False) As Single()
        Return FloodFill.Region(Pixels(b), b.Width, b.Height, x, y, tol, contiguous, gap, expand, aa)
    End Function

    Private Function Pixels(b As Bitmap) As Byte()
        Dim data = b.LockBits(New Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            Dim px(b.Width * b.Height * 4 - 1) As Byte
            For y = 0 To b.Height - 1
                Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * b.Width * 4, b.Width * 4)
            Next
            Return px
        Finally
            b.UnlockBits(data)
        End Try
    End Function

    Sub BucketTestsRun()
        Console.WriteLine("油漆桶")
        Using closed = Ring(0)
            Dim m = Fill(closed, 100, 100)
            Check("點圓圈裡面：只填裡面", m(100 * 200 + 100) = 1 AndAlso m(10 * 200 + 10) = 0 AndAlso m(100 * 200 + 190) = 0)
            Dim all = Fill(closed, 100, 100, contiguous:=False)
            Check("不相鄰：圓圈外的白色也填", all(10 * 200 + 10) = 1 AndAlso all(100 * 200 + 100) = 1)
            Check("黑線本身不填", m(100 * 200 + 41) = 0 AndAlso all(100 * 200 + 41) = 0)
            ' 擴張：蓋住線稿內側反鋸齒的灰邊
            Dim inner = Enumerable.Range(0, 200).Where(Function(x) m(100 * 200 + x) > 0).Count()
            Dim expanded = Fill(closed, 100, 100, expand:=2)
            Dim innerX = Enumerable.Range(0, 200).Where(Function(x) expanded(100 * 200 + x) > 0).Count()
            Check("擴張 2 像素：每邊多 2 像素", innerX >= inner + 4 AndAlso expanded(10 * 200 + 10) = 0, $"{inner} → {innerX}")
            Dim soft = Fill(closed, 100, 100, aa:=True)
            Check("柔邊：邊緣有介於 0..1 的值，裡面仍是 1", soft.Any(Function(v) v > 0 AndAlso v < 1) AndAlso soft(100 * 200 + 100) = 1)
        End Using

        Using open = Ring(6)
            Dim leak = Fill(open, 100, 100)
            Check("圓圈有 6 像素缺口：不封閉時漏到外面", leak(10 * 200 + 10) = 1)
            Dim sealedFill = Fill(open, 100, 100, gap:=4)
            Check("封閉缺口 4：不漏到外面", sealedFill(10 * 200 + 10) = 0 AndAlso sealedFill(5 * 200 + 195) = 0)
            Check("封閉缺口後仍填滿到線邊（貼齊線稿）", sealedFill(100 * 200 + 100) = 1 AndAlso sealedFill(100 * 200 + 45) = 1 AndAlso sealedFill(100 * 200 + 155) = 1,
                  $"{sealedFill(100 * 200 + 45)} {sealedFill(100 * 200 + 155)}")
            Dim outsideCount = Enumerable.Range(0, 200 * 200).Count(Function(i) sealedFill(i) > 0 AndAlso Math.Sqrt((i Mod 200 - 100) ^ 2 + (i \ 200 - 100) ^ 2) > 64)
            Check("從缺口探出去的只有一點點", outsideCount < 60, outsideCount.ToString())
        End Using

        ' 容許度：左右兩半差 40
        Using two = White(100, 50)
            Using g = Graphics.FromImage(two)
                g.FillRectangle(New SolidBrush(Color.FromArgb(100, 100, 100)), 0, 0, 50, 50)
                g.FillRectangle(New SolidBrush(Color.FromArgb(140, 140, 140)), 50, 0, 50, 50)
            End Using
            Check("容許度 32：差 40 的另一半不填", Fill(two, 10, 10, tol:=32)(25 * 100 + 80) = 0)
            Check("容許度 50：差 40 的另一半也填", Fill(two, 10, 10, tol:=50)(25 * 100 + 80) = 1)
        End Using
        Using empty As New Bitmap(60, 40, PixelFormat.Format32bppArgb)
            Check("空白（透明）圖層：一次填滿", Fill(empty, 5, 5).All(Function(v) v = 1))
        End Using
        Check("點在影像外：不填", FloodFill.Region(New Byte(16 - 1) {}, 2, 2, 5, 5, 32, True, 0, 0, False) Is Nothing)

        ' ---- 點陣圖層裡的填色操作 ----
        DrawingRenderer.ClearCache()
        Dim lineArt As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.75, .Y = 0.5, .W = 0.6, .H = 0.6, .Stroked = True, .Filled = False,
                                .StrokeColorArgb = Color.Black.ToArgb(), .StrokeWidth = 0.015, .Seed = 3}}}
        Dim bucketOp As New DrawLayer With {.Shape = DrawShape.Bucket, .StrokeColorArgb = Color.FromArgb(230, 60, 40).ToArgb(), .Param1 = 1.5,
                                            .Bucket = New BucketFill With {.X = 0.75, .Y = 0.5, .Expand = 2}}
        Dim colorLayer As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {bucketOp}}
        Using bmp = White(300, 200)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {lineArt, colorLayer}})
            Dim inside = bmp.GetPixel(150, 100), outside = bmp.GetPixel(10, 10)
            Check("上色圖層在線稿上面、取樣所有圖層：填在線稿的圈圈裡", inside.R > 200 AndAlso inside.G < 90 AndAlso outside = Color.FromArgb(255, 255, 255), $"{inside} {outside}")
        End Using
        ' 漫畫上色的做法：上色圖層放在線稿下面，擴張蓋到線底下，線稿仍在最上面
        DrawingRenderer.ClearCache()
        Using bmp = White(300, 200)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {colorLayer, lineArt}})
            Dim inside = bmp.GetPixel(150, 100), outside = bmp.GetPixel(10, 10), onLine = bmp.GetPixel(150, 40)
            Check("上色圖層在線稿下面：取樣看得到上面的線稿，只填圈圈裡", inside.R > 200 AndAlso inside.G < 90 AndAlso outside = Color.FromArgb(255, 255, 255), $"{inside} {outside}")
            Check("線稿在上面完整看得到", onLine.R < 90, onLine.ToString())
        End Using
        ' 線稿改了：上色跟著重算
        lineArt.Ops(0).W = 0.4 : lineArt.Ops(0).H = 0.4
        Using bmp = White(300, 200)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {colorLayer, lineArt}})
            Check("上面的線稿縮小：填色範圍跟著變", bmp.GetPixel(150, 50) = Color.FromArgb(255, 255, 255) AndAlso bmp.GetPixel(150, 100).G < 90, bmp.GetPixel(150, 50).ToString())
        End Using
        lineArt.Ops(0).W = 0.6 : lineArt.Ops(0).H = 0.6
        bucketOp.Bucket.Sample = BucketSample.CurrentLayer
        DrawingRenderer.ClearCache()
        Using bmp = White(300, 200)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {lineArt, colorLayer}})
            Check("只取樣目前圖層（空的）：整個圖層填滿", bmp.GetPixel(10, 10).R > 200 AndAlso bmp.GetPixel(10, 10).G < 90)
        End Using

        ' 紋理筆：填材質（顏色有變化）
        bucketOp.Bucket.Sample = BucketSample.AllLayers
        bucketOp.Brush = BrushKind.Texture : bucketOp.Material = MaterialKind.Wood
        DrawingRenderer.ClearCache()
        Using bmp = White(300, 200)
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {lineArt, colorLayer}})
            Dim tones = Enumerable.Range(0, 60).Select(Function(k) bmp.GetPixel(120 + k, 100).R).Distinct().Count()
            Check("紋理筆填色：填進材質紋路", tones > 8 AndAlso bmp.GetPixel(10, 10) = Color.FromArgb(255, 255, 255), tones.ToString())
        End Using

        ' 原照片：上面整層塗白，再用油漆桶（原照片）把圈裡補回來
        bucketOp.Brush = BrushKind.HardRound
        Dim cover As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.75, .Y = 0.5, .W = 1.6, .H = 1.1, .Filled = True, .Stroked = False,
                                .FillColorArgb = Color.White.ToArgb(), .Seed = 1},
            New DrawLayer With {.Shape = DrawShape.Bucket, .Param1 = 1.5,
                                .Bucket = New BucketFill With {.X = 0.75, .Y = 0.5, .Sample = BucketSample.AllLayers, .Source = BucketSource.Photo, .Contiguous = True}}}}
        DrawingRenderer.ClearCache()
        Using photo = White(300, 200)
            Using g = Graphics.FromImage(photo)
                g.FillRectangle(Brushes.SteelBlue, 0, 0, 300, 200)
            End Using
            DrawingRenderer.DrawLayers(photo, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {cover}})
            Dim px = photo.GetPixel(150, 100)
            Check("填入原照片：把蓋掉的照片補回來", px.B > 150 AndAlso px.R < 100, px.ToString())
        End Using

        ' 存檔
        Dim saved = System.Text.Json.JsonSerializer.Serialize(colorLayer)
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of DrawLayer)(saved)
        Check("填色操作存得回來", back.Ops(0).Shape = DrawShape.Bucket AndAlso back.Ops(0).Bucket.Expand = 2 AndAlso back.Ops(0).Bucket.X = 0.75)
        Dim cl = colorLayer.Clone()
        cl.Ops(0).Bucket.Tolerance = 99
        Check("Clone 為深複製（填色選項不共用）", colorLayer.Ops(0).Bucket.Tolerance = 32)
    End Sub
End Module
