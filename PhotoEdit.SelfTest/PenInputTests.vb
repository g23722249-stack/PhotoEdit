Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>繪圖筆：筆壓曲線、筆畫穩定器、傾斜時用筆側塗（變寬、乾性筆變淡、油畫筆毛斷開）、點陣圖層多時的快取。</summary>
Module PenInputTests

    ''' <summary>一筆橫線，筆往垂直於筆畫的方向傾斜（筆側橫跨筆畫，筆觸變寬）；tilt = 0 為筆直立。</summary>
    Private Function TiltStroke(brush As BrushKind, tilt As Single) As DrawLayer
        Dim pts = Enumerable.Range(0, 21).Select(Function(i) New DrawPoint(0.15F + i * 0.06F, 0.5F, 0.8F) With {.Tx = 0, .Ty = tilt}).ToList()
        Return New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = brush, .StrokeColorArgb = Color.Black.ToArgb(), .StrokeWidth = 0.08,
                                   .Seed = 9, .PenTilt = True, .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}}
    End Function

    ''' <summary>畫在白底上：（上色的列數＝筆觸寬度, 筆觸中央那一段的平均濃度）。</summary>
    Private Function Measure(layer As DrawLayer) As (Rows As Integer, Ink As Double)
        Using bmp As New Bitmap(400, 200, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.White)
            End Using
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {layer}})
            Dim rows = 0
            For y = 0 To 199
                If bmp.GetPixel(200, y).R < 235 Then rows += 1
            Next
            Dim ink = 0.0, n = 0
            For x = 150 To 250
                For y = 96 To 104
                    ink += (255 - bmp.GetPixel(x, y).R) / 255.0
                    n += 1
                Next
            Next
            Return (rows, ink / n)
        End Using
    End Function

    Sub PenInputTestsRun()
        Console.WriteLine("繪圖筆")
        ' ---- 筆壓曲線 ----
        Dim linear As New PenCurve()
        Check("筆壓曲線預設為線性", linear.IsLinear AndAlso Math.Abs(linear.Map(0.3F) - 0.3F) < 0.001 AndAlso linear.Map(1) = 1 AndAlso linear.Map(0) = 0)
        Dim soft As New PenCurve With {.Softness = 60}, hard As New PenCurve With {.Softness = -60}
        Check("軟：輕畫比線性濃；硬：輕畫比線性淡", soft.Map(0.3F) > 0.45F AndAlso hard.Map(0.3F) < 0.18F, $"{soft.Map(0.3F):0.00} / {hard.Map(0.3F):0.00}")
        Dim mn As New PenCurve With {.MinPressure = 20}
        Check("最小筆壓：最輕也有 20%", Math.Abs(mn.Map(0) - 0.2F) < 0.001 AndAlso mn.Map(1) = 1)
        Dim full As New PenCurve With {.FullPressure = 70}
        Check("滿壓力道 70%：按到七成就是全壓", full.Map(0.7F) >= 0.999F AndAlso full.Map(0.9F) = 1 AndAlso Math.Abs(full.Map(0.35F) - 0.5F) < 0.01)
        Dim allPts = Enumerable.Range(0, 101).Select(Function(i) soft.Map(i / 100.0F)).ToList()
        Check("曲線遞增、不超出 0..1", Enumerable.Range(1, 100).All(Function(i) allPts(i) >= allPts(i - 1)) AndAlso allPts.All(Function(v) v >= 0 AndAlso v <= 1))

        ' ---- 穩定器 ----
        Dim st As New StrokeStabilizer(New PointF(100, 100), 20)
        Dim moved = False
        Dim jr As New Random(2)
        For k = 1 To 50
            moved = moved Or st.Pull(New PointF(100 + CSng(jr.NextDouble() * 24 - 12), 100 + CSng(jr.NextDouble() * 24 - 12)))
        Next
        Check("穩定器：線長內的抖動不會畫出來", Not moved AndAlso st.Anchor = New PointF(100, 100))
        st.Pull(New PointF(150, 100))
        Check("穩定器：游標超過線長時筆尖被拉到相距線長的地方", Math.Abs(st.Anchor.X - 130) < 0.01 AndAlso Math.Abs(st.Anchor.Y - 100) < 0.01, st.Anchor.ToString())
        Dim tail = st.Finish(New PointF(150, 100), 5)
        Check("穩定器：放開時補到游標（不會短一截）", tail.Count = 4 AndAlso tail.Last() = New PointF(150, 100) AndAlso st.Anchor = New PointF(150, 100))
        ' 鋸齒狀的手抖：穩定後的路徑比原始的平順（轉彎角度的總和小很多）
        Dim raw = Enumerable.Range(0, 200).Select(Function(i) New PointF(i * 3.0F, 200 + CSng(Math.Sin(i * 0.05) * 60) + If(i Mod 2 = 0, 4, -4))).ToList()
        Dim st2 As New StrokeStabilizer(raw(0), 15)
        Dim smooth As New List(Of PointF) From {raw(0)}
        For Each p In raw.Skip(1)
            If st2.Pull(p) Then smooth.Add(st2.Anchor)
        Next
        Dim turning = Function(path As List(Of PointF)) As Double
                          Dim sum = 0.0
                          For i = 2 To path.Count - 1
                              Dim a1 = Math.Atan2(path(i - 1).Y - path(i - 2).Y, path(i - 1).X - path(i - 2).X)
                              Dim a2 = Math.Atan2(path(i).Y - path(i - 1).Y, path(i).X - path(i - 1).X)
                              sum += Math.Abs(Math.IEEERemainder(a2 - a1, 2 * Math.PI))
                          Next
                          Return sum
                      End Function
        Check("穩定器：鋸齒狀的手抖變平順", turning(smooth) < turning(raw) * 0.2, $"{turning(smooth):0.0} / {turning(raw):0.0}")

        ' ---- 傾斜：用筆側塗 ----
        DrawingRenderer.ClearCache()
        Dim upright = Measure(TiltStroke(BrushKind.Pencil, 0))
        Dim side = Measure(TiltStroke(BrushKind.Pencil, 70))
        Check("鉛筆斜著畫：筆觸變寬（側塗的邊緣很淡，量到的寬度增加較少）", side.Rows > upright.Rows * 1.15, $"{upright.Rows} → {side.Rows}")
        Check("鉛筆斜著畫：顏色變淡（筆側塗）", side.Ink < upright.Ink * 0.8, $"{upright.Ink:0.00} → {side.Ink:0.00}")
        Dim oilUp = Measure(TiltStroke(BrushKind.OilPaint, 0))
        Dim oilSide = Measure(TiltStroke(BrushKind.OilPaint, 70))
        Check("油畫筆斜著畫：筆觸變寬、顏料變少（筆毛斷開）", oilSide.Rows > oilUp.Rows AndAlso oilSide.Ink < oilUp.Ink * 0.85, $"{oilUp.Rows}/{oilUp.Ink:0.00} → {oilSide.Rows}/{oilSide.Ink:0.00}")
        Dim noTilt = TiltStroke(BrushKind.Pencil, 70) : noTilt.PenTilt = False
        Check("沒勾「筆傾斜改變筆尖」時傾斜不影響", Measure(noTilt).Equals(upright))

        ' ---- 點陣圖層多時：每層的結果都留在快取，加一筆只重算那一筆 ----
        Dim rnd As New Random(4)
        Dim recipe As New EditRecipe With {.Drawings = New List(Of DrawLayer)()}
        For layer = 1 To 12
            Dim r As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer)()}
            For k = 1 To 25
                Dim y = CSng(rnd.NextDouble())
                r.Ops.Add(New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.OilPaint, .StrokeColorArgb = Color.Navy.ToArgb(), .StrokeWidth = 0.04,
                                              .Seed = k, .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = Enumerable.Range(0, 12).Select(Function(i) New DrawPoint(0.05F + i * 0.08F, y)).ToList()}}})
            Next
            recipe.Drawings.Add(r)
        Next
        Using bg As New Bitmap(600, 400)
            Dim sw = Diagnostics.Stopwatch.StartNew()
            ImagePipeline.Render(bg, recipe).Dispose()
            Dim first = sw.ElapsedMilliseconds
            recipe.Drawings(11).Ops.Add(recipe.Drawings(0).Ops(0).Clone())
            sw.Restart()
            ImagePipeline.Render(bg, recipe).Dispose()
            Dim second = sw.ElapsedMilliseconds
            Check("12 個點陣圖層加一筆：不必重畫所有圖層", second * 4 < first, $"{first} ms → {second} ms")
        End Using
    End Sub
End Module
