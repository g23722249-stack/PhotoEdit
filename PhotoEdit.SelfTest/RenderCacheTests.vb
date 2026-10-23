Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>
''' 算圖快取（圖層多、筆畫多時的效能修正）：「底圖＋下面幾層」的快取、塗抹筆只看筆畫附近、檢查點。
''' 每個情境都比「接著上一次快取畫」和「清空快取從頭畫」逐像素一樣。
''' </summary>
Module RenderCacheTests
    Private ReadOnly rnd As New Random(11)

    Private Function Stroke(brush As BrushKind, Optional width As Double = 0.02, Optional x As Double = -1, Optional y As Double = -1) As DrawLayer
        Dim pts As New List(Of DrawPoint)()
        Dim px = If(x < 0, 0.1 + rnd.NextDouble() * 1.1, x), py = If(y < 0, 0.1 + rnd.NextDouble() * 0.8, y)
        For i = 0 To 12
            pts.Add(New DrawPoint(CSng(px + i * 0.012), CSng(py + Math.Sin(i * 0.5) * 0.02), 0.8F))
        Next
        Return New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = brush, .StrokeWidth = width, .Seed = rnd.Next(1, 100000), .Wet = 70,
                                   .StrokeColorArgb = Color.FromArgb(255, rnd.Next(256), rnd.Next(256), rnd.Next(256)).ToArgb(),
                                   .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}}
    End Function

    Private Function Layer(name As String, normal As Integer, Optional smudge As Integer = 0) As DrawLayer
        Dim ops As New List(Of DrawLayer)()
        For k = 1 To normal : ops.Add(Stroke(BrushKind.HardRound)) : Next
        For k = 1 To smudge : ops.Add(Stroke(If(k Mod 2 = 0, BrushKind.Smudge, BrushKind.Blur), 0.04)) : Next
        Return New DrawLayer With {.Shape = DrawShape.Raster, .Name = name, .Ops = ops}
    End Function

    Private _src As Bitmap

    Private Function Pixels(r As EditRecipe) As Byte()
        Using b = ImagePipeline.Render(_src, r.Snapshot())
            Dim d = b.LockBits(New Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Dim px(d.Stride * b.Height - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length)
            b.UnlockBits(d)
            Return px
        End Using
    End Function

    ''' <summary>接著快取畫的結果和清空快取從頭畫一樣（回傳不同的位元組數）。</summary>
    Private Function Diff(r As EditRecipe) As Integer
        Dim cached = Pixels(r)
        DrawingRenderer.ClearCache()
        Dim fresh = Pixels(r)
        Dim n = 0
        For i = 0 To cached.Length - 1
            If cached(i) <> fresh(i) Then n += 1
        Next
        Return n
    End Function

    Sub RenderCacheTestsRun()
        Console.WriteLine("— 算圖快取（圖層多、筆畫多） —")
        _src = New Bitmap(420, 300, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(_src)
            g.Clear(Color.FromArgb(230, 220, 200))
            g.FillEllipse(Brushes.SteelBlue, 60, 40, 200, 160)
        End Using
        DrawingRenderer.ClearCache()
        Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {
            Layer("L0", 40), Layer("L1", 40), Layer("L2", 30, smudge:=20), Layer("L3", 40), Layer("L4", 30, smudge:=20), Layer("L5", 40)}}
        Pixels(r) : Pixels(r) ' 暖快取（第二次會存下面幾層）

        r.Drawings(5).Ops.Add(Stroke(BrushKind.HardRound))
        Pixels(r) ' 上一次記下「最上層的下面」
        r.Drawings(5).Ops.Add(Stroke(BrushKind.HardRound))
        Check("在最上層畫：接著快取畫＝從頭畫", Diff(r) = 0)

        Pixels(r) : Pixels(r)
        r.Drawings(1).Ops.Add(Stroke(BrushKind.HardRound, 0.05, 0.3, 0.4))
        Pixels(r)
        r.Drawings(1).Ops.Add(Stroke(BrushKind.HardRound, 0.05, 0.5, 0.5))
        Check("在下面的層畫（上面有塗抹、模糊的層）：一樣", Diff(r) = 0)

        Pixels(r) : Pixels(r)
        r.Drawings(4).Ops.Add(Stroke(BrushKind.Smudge, 0.05))
        Pixels(r)
        r.Drawings(4).Ops.Add(Stroke(BrushKind.Blur, 0.05))
        Check("在有塗抹的層繼續塗抹、模糊：一樣", Diff(r) = 0)

        Pixels(r) : Pixels(r)
        r.Exposure = 0.4
        Check("改了底圖（曝光）：下面幾層的快取不能用，一樣", Diff(r) = 0)

        Pixels(r) : Pixels(r)
        r.Drawings(2).Opacity = 55
        Check("改下面一層的不透明度：一樣", Diff(r) = 0)

        Pixels(r) : Pixels(r)
        r.Drawings(1).Visible = False
        Check("隱藏下面一層：一樣", Diff(r) = 0)
        r.Drawings(1).Visible = True

        Pixels(r) : Pixels(r)
        Dim moved = r.Drawings(0) : r.Drawings.RemoveAt(0) : r.Drawings.Insert(3, moved)
        Check("調換圖層順序：一樣", Diff(r) = 0)

        Pixels(r) : Pixels(r)
        r.Drawings(3).X = 0.05
        Check("移動下面一層（點陣圖層位移）：一樣", Diff(r) = 0)

        ' 油漆桶「取樣所有圖層」看得到上面的圖層：改上面時下面那層要重算（不用下面幾層的快取）
        Dim bucket As New DrawLayer With {.Shape = DrawShape.Raster, .Name = "B", .Ops = New List(Of DrawLayer) From {
            New DrawLayer With {.Shape = DrawShape.Bucket, .FillColorArgb = Color.Gold.ToArgb(), .StrokeColorArgb = Color.Gold.ToArgb(), .Seed = 3,
                                .Bucket = New BucketFill With {.X = 0.2, .Y = 0.2, .Sample = BucketSample.AllLayers, .Tolerance = 20}}}}
        r.Drawings.Insert(0, bucket)
        Pixels(r) : Pixels(r)
        r.Drawings(6).Ops.Add(Stroke(BrushKind.HardRound, 0.08, 0.1, 0.15))
        Check("油漆桶取樣所有圖層＋改上面的層：一樣", Diff(r) = 0)

        ' 很大的種子（編輯檔被改過）：不溢位
        Dim big = Stroke(BrushKind.OilPaint) : big.Seed = Integer.MaxValue - 3
        Dim ok = True
        Try
            Using b = ImagePipeline.Render(_src, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {big}}}})
            End Using
        Catch ex As OverflowException
            ok = False
        End Try
        Check("種子很大的筆（編輯檔被改過）：畫得出來，不會溢位", ok)
        _src.Dispose()
        DrawingRenderer.ClearCache()
    End Sub
End Module
