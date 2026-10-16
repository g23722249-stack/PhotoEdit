Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>逐段筆印：一點一點畫出來的預覽要和整筆算的一樣；讀畫布的筆也要有預覽。</summary>
Module IncrementalTests

    Private Const W As Integer = 600, H As Integer = 400

    Private Function ReadPx(bmp As Bitmap) As Byte()
        Dim data = bmp.LockBits(New Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        Try
            Dim px(bmp.Width * bmp.Height * 4 - 1) As Byte
            For y = 0 To bmp.Height - 1
                Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bmp.Width * 4, bmp.Width * 4)
            Next
            Return px
        Finally
            bmp.UnlockBits(data)
        End Try
    End Function

    Private Function Points(n As Integer) As List(Of DrawPoint)
        Dim list As New List(Of DrawPoint)()
        For i = 0 To n - 1
            Dim t = i / 39.0
            list.Add(New DrawPoint(CSng(0.2 + 1.1 * t), CSng(0.5 + 0.22 * Math.Sin(t * 7)), CSng(0.5 + 0.5 * Math.Sin(t * 3))))
        Next
        Return list
    End Function

    Private Function StrokeLayer(brush As BrushKind) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Freehand, .Seed = 4321, .Brush = brush, .StrokeWidth = 0.04,
                                   .StrokeColorArgb = Color.FromArgb(40, 90, 200).ToArgb(), .Opacity = 100, .Flow = 100}
    End Function

    Private Function WithPoints(layer As DrawLayer, n As Integer) As DrawLayer
        Dim d = layer.Clone()
        d.Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = Points(n)}}
        Return d
    End Function

    ''' <summary>一點一點畫，把交回的格子拼成整張（w × h）。</summary>
    Private Function DrawIncrementally(layer As DrawLayer, total As Integer) As Bitmap
        Dim session As New DrawingRenderer.StrokeSession(W, H, Nothing)
        Dim tiles As New Dictionary(Of Rectangle, Bitmap)()
        For n = 1 To total
            For Each t In session.Update(WithPoints(layer, n), n)
                Dim old As Bitmap = Nothing
                If tiles.TryGetValue(t.Bounds, old) Then old?.Dispose()
                tiles(t.Bounds) = t.Bitmap
            Next
        Next
        ' 直接複製像素（用 Graphics 畫會先換成預乘再換回來，半透明的顏色會差一點）
        Dim all(W * H * 4 - 1) As Byte
        For Each kv In tiles
            If kv.Value Is Nothing Then Continue For
            Dim px = ReadPx(kv.Value)
            For y = 0 To kv.Key.Height - 1
                Array.Copy(px, y * kv.Key.Width * 4, all, ((kv.Key.Y + y) * W + kv.Key.X) * 4, kv.Key.Width * 4)
            Next
            kv.Value.Dispose()
        Next
        Dim result As New Bitmap(W, H, PixelFormat.Format32bppArgb)
        Dim data = result.LockBits(New Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        For y = 0 To H - 1
            Runtime.InteropServices.Marshal.Copy(all, y * W * 4, data.Scan0 + y * data.Stride, W * 4)
        Next
        result.UnlockBits(data)
        Return result
    End Function

    ''' <summary>和整筆算的比：不同（任一通道差超過 2）的像素比例。</summary>
    Private Function DiffRatio(layer As DrawLayer, total As Integer, ByRef detail As String) As Double
        Dim full = DrawingRenderer.RenderPreview(WithPoints(layer, total), W, H, Nothing)
        Using inc = DrawIncrementally(layer, total)
            Dim diff = 0, count = 0
            Dim incPx = ReadPx(inc)
            Dim fullPx As Byte() = If(full.Bitmap Is Nothing, Nothing, ReadPx(full.Bitmap))
            For y = 0 To H - 1
                For x = 0 To W - 1
                    Dim a(3) As Integer, b(3) As Integer
                    Dim i = (y * W + x) * 4
                    For k = 0 To 3
                        a(k) = incPx(i + k)
                    Next
                    If full.Region.Contains(x, y) AndAlso fullPx IsNot Nothing Then
                        Dim j = ((y - full.Region.Y) * full.Region.Width + x - full.Region.X) * 4
                        For k = 0 To 3
                            b(k) = fullPx(j + k)
                        Next
                    End If
                    If a(3) = 0 AndAlso b(3) = 0 Then Continue For
                    count += 1
                    ' 幾乎透明的像素顏色沒有意義，只比 alpha
                    Dim worst = Math.Abs(a(3) - b(3))
                    If Math.Min(a(3), b(3)) > 8 Then
                        For k = 0 To 2
                            worst = Math.Max(worst, Math.Abs(a(k) - b(k)))
                        Next
                    End If
                    If worst > 2 Then diff += 1
                Next
            Next
            full.Bitmap?.Dispose()
            detail = $"{diff}/{count}"
            Return If(count = 0, 1.0, diff / CDbl(count))
        End Using
    End Function

    Sub IncrementalTestsRun()
        Console.WriteLine("逐段筆印")
        Dim detail = ""
        For Each b In {BrushKind.HardRound, BrushKind.SoftRound, BrushKind.Pencil, BrushKind.Charcoal, BrushKind.Chalk, BrushKind.Crayon,
                       BrushKind.Watercolor, BrushKind.Marker, BrushKind.Airbrush, BrushKind.Acrylic, BrushKind.OilPaint, BrushKind.Texture,
                       BrushKind.Ink, BrushKind.ChineseBrush}
            Dim r = DiffRatio(StrokeLayer(b), 40, detail)
            ' 壓克力、油畫：筆畫最後一段的筆毛乾掉；墨水、毛筆：收尾變細、毛筆尾端飛白——都要知道總長，放開才出現
            Dim limit = If(b = BrushKind.Acrylic OrElse b = BrushKind.OilPaint, 0.08, If(b = BrushKind.Ink OrElse b = BrushKind.ChineseBrush, 0.3, 0.0))
            Check($"逐段畫完和整筆算的一樣：{b}", r <= limit, detail)
        Next
        Dim jitter = StrokeLayer(BrushKind.HardRound)
        jitter.SizeJitter = 60 : jitter.Scatter = 40 : jitter.HueJitter = 30
        Check("逐段：大小、散佈、顏色變化（亂數）也一樣", DiffRatio(jitter, 40, detail) = 0, detail)
        Dim sym = StrokeLayer(BrushKind.Charcoal)
        sym.Symmetry = SymmetryKind.MirrorX : sym.SymX = 0.75 : sym.SymY = 0.5
        Check("逐段：左右對稱", DiffRatio(sym, 40, detail) = 0, detail)
        Check("逐段：不支援的筆（特效、粒子、乾刷、陰影）改用真實筆刷",
              Not DrawingRenderer.StrokeSession.Supports(StrokeLayer(BrushKind.FX)) AndAlso
              Not DrawingRenderer.StrokeSession.Supports(StrokeLayer(BrushKind.Particle)) AndAlso
              Not DrawingRenderer.StrokeSession.Supports(StrokeLayer(BrushKind.DryBrush)) AndAlso
              Not DrawingRenderer.StrokeSession.Supports(New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.HardRound, .Shadow = True}) AndAlso
              DrawingRenderer.StrokeSession.Supports(StrokeLayer(BrushKind.Watercolor)))
        Dim symJitter = StrokeLayer(BrushKind.HardRound)
        symJitter.Symmetry = SymmetryKind.MirrorX : symJitter.Scatter = 30
        Check("逐段：對稱＋亂數不支援（亂數順序會變）", Not DrawingRenderer.StrokeSession.Supports(symJitter))

        ' 效能：長筆畫（160 點、每 2 點更新一次）逐段筆印要比每次整筆重算快很多
        Dim longLayer = StrokeLayer(BrushKind.Watercolor)
        Dim longPts = Function(n As Integer)
                          Dim d = longLayer.Clone()
                          Dim pts As New List(Of DrawPoint)()
                          For i = 0 To n - 1
                              Dim t = i / 159.0
                              pts.Add(New DrawPoint(CSng(0.1 + 1.3 * t), CSng(0.5 + 0.3 * Math.Sin(t * 19)), 0.8F))
                          Next
                          d.Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}
                          Return d
                      End Function
        Dim swEngine = Stopwatch.StartNew()
        For n = 2 To 160 Step 2
            DrawingRenderer.RenderPreview(longPts(n), W, H, Nothing).Bitmap?.Dispose()
        Next
        swEngine.Stop()
        Dim swInc = Stopwatch.StartNew()
        Dim longSession As New DrawingRenderer.StrokeSession(W, H, Nothing)
        For n = 2 To 160 Step 2
            longSession.Update(longPts(n), n).ForEach(Sub(t) t.Bitmap?.Dispose())
        Next
        swInc.Stop()
        Check("逐段筆印：長筆畫比每次整筆重算快", swInc.ElapsedMilliseconds * 2 < swEngine.ElapsedMilliseconds,
              $"逐段 {swInc.ElapsedMilliseconds} ms、整筆 {swEngine.ElapsedMilliseconds} ms")
        Console.WriteLine($"    （長筆畫 80 次更新：逐段 {swInc.ElapsedMilliseconds} ms、整筆重算 {swEngine.ElapsedMilliseconds} ms）")

        ' 多條執行緒同時算圖（畫面重算＋高解析度＋預覽）：快取一直擠掉舊圖也不能出錯
        ' （曾經：算快取總量時讀到別的執行緒正在合成的圖 →「Object is currently in use elsewhere」）
        Dim ink = StrokeLayer(BrushKind.Ink)
        Dim raster As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer)()}
        For k = 0 To 3
            Dim op = WithPoints(StrokeLayer(If(k Mod 2 = 0, BrushKind.Ink, BrushKind.Charcoal)), 12 + k * 6)
            raster.Ops.Add(op)
        Next
        Dim recipe As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {raster, WithPoints(ink, 30)}}
        Dim errors = 0, firstError = ""
        Threading.Tasks.Parallel.For(0, 160, New Threading.Tasks.ParallelOptions With {.MaxDegreeOfParallelism = 8},
            Sub(i)
                Try
                    ' 每次不同大小：快取一直塞新的、擠掉舊的
                    Dim hh = 60 + (i Mod 80) * 3
                    Using bmp As New Bitmap(CInt(hh * 1.5), hh, PixelFormat.Format32bppArgb)
                        LayerStack.Draw(bmp, recipe)
                    End Using
                Catch ex As Exception
                    Threading.Interlocked.Increment(errors)
                    If firstError = "" Then firstError = ex.GetType().Name & ": " & ex.Message
                End Try
            End Sub)
        Check("多條執行緒同時算點陣與向量圖層、快取不斷擠掉舊圖：不會出錯", errors = 0, $"{errors} 次：{firstError}")

        ' 讀畫布的筆：畫布是左紅右藍的快照，塗抹筆一點一點畫下去，會把紅色帶到藍色那邊
        Dim snap(W * H * 4 - 1) As Byte
        For y = 0 To H - 1
            For x = 0 To W - 1
                Dim i = (y * W + x) * 4
                If x < W \ 2 Then snap(i + 2) = 220 Else snap(i) = 220
                snap(i + 3) = 255
            Next
        Next
        Dim smudge = New DrawLayer With {.Shape = DrawShape.Freehand, .Seed = 1, .Brush = BrushKind.Smudge, .StrokeWidth = 0.08, .Opacity = 100, .Wet = 60}
        Check("讀畫布的筆：塗抹、混色、仿製、模糊支援逐段", DrawingRenderer.SamplingSession.Supports(smudge) AndAlso
              DrawingRenderer.SamplingSession.Supports(New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.Blur}))
        Dim ss As New DrawingRenderer.SamplingSession(snap, W, H)
        Dim straight = Function(n As Integer)
                           Dim d = smudge.Clone()
                           Dim pts As New List(Of DrawPoint)()
                           For i = 0 To n - 1
                               pts.Add(New DrawPoint(CSng(0.4 + i * 0.02), 0.5F, 1))
                           Next
                           d.Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}
                           Return d
                       End Function
        Dim last As DrawingRenderer.PreviewTile = Nothing
        Dim tileCount = 0
        For n = 1 To 30
            For Each t In ss.Update(straight(n), n)
                tileCount += 1
                If t.Bitmap IsNot Nothing AndAlso t.Bounds.Contains(CInt(0.85 * H), CInt(0.5 * H)) Then last?.Bitmap?.Dispose() : last = t Else t.Bitmap?.Dispose()
            Next
        Next
        Dim redOnBlue = False
        If last IsNot Nothing Then
            Dim c = last.Bitmap.GetPixel(CInt(0.85 * H) - last.Bounds.X, CInt(0.5 * H) - last.Bounds.Y)
            redOnBlue = c.A > 200 AndAlso c.R > 40
            last.Bitmap.Dispose()
        End If
        Check("塗抹筆逐段預覽：紅色被推到藍色那邊", tileCount > 0 AndAlso redOnBlue)
    End Sub
End Module
