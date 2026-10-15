Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>線條樣式（虛線、箭頭、端點、轉角）、PowerClip、照片放進形狀、文字轉外框。</summary>
Module LineClipTests

    ''' <summary>200 × 100 白底上依圖層順序畫。</summary>
    Private Function Render(r As EditRecipe, Optional back As Color? = Nothing) As Bitmap
        Dim bmp As New Bitmap(200, 100, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(If(back, Color.White))
        End Using
        LayerStack.Draw(bmp, r)
        Return bmp
    End Function

    Private Function HLine(Optional setup As Action(Of DrawLayer) = Nothing) As DrawLayer
        Dim d As New DrawLayer With {.Shape = DrawShape.Line, .StrokeColorArgb = Color.Black.ToArgb(), .StrokeWidth = 0.08, .Brush = BrushKind.HardRound,
                                     .Points = New List(Of DrawPoint) From {New DrawPoint(0.3, 0.5), New DrawPoint(1.7, 0.5)}}
        setup?.Invoke(d)
        Return d
    End Function

    Private Function Dark(c As Color) As Boolean
        Return c.R < 128
    End Function

    ''' <summary>y = 50 這一列，x 從 a 到 b 有幾個黑點、黑白切換幾次。</summary>
    Private Function RowStats(bmp As Bitmap, y As Integer, a As Integer, b As Integer) As (Black As Integer, Switches As Integer)
        Dim n = 0, sw = 0, last = Dark(bmp.GetPixel(a, y))
        For x = a To b
            Dim d = Dark(bmp.GetPixel(x, y))
            If d Then n += 1
            If d <> last Then sw += 1
            last = d
        Next
        Return (n, sw)
    End Function

    Private Function Recipe(ParamArray layers As DrawLayer()) As EditRecipe
        Return New EditRecipe With {.Drawings = layers.ToList()}
    End Function

    Sub LineClipTestsRun()
        Console.WriteLine("線條樣式、PowerClip、照片放進形狀、文字轉外框")
        DrawingRenderer.ClearCache()
        ' ---- 虛線 ----
        Dim solid = RowStats(Render(Recipe(HLine())), 50, 40, 160)
        Dim dash = RowStats(Render(Recipe(HLine(Sub(d) d.Dash = LineDash.Dash))), 50, 40, 160)
        Check("實線：整條都是黑的", solid.Black >= 118 AndAlso solid.Switches = 0, $"{solid.Black} {solid.Switches}")
        Check("虛線：有空隙、切換好幾次", dash.Black < solid.Black - 20 AndAlso dash.Switches >= 6, $"{dash.Black} {dash.Switches}")
        Dim dot = RowStats(Render(Recipe(HLine(Sub(d) d.Dash = LineDash.Dot))), 50, 40, 160)
        Check("點線：點比虛線多", dot.Switches > dash.Switches, $"{dot.Switches} / {dash.Switches}")
        Dim wcDash = RowStats(Render(Recipe(HLine(Sub(d)
                                                       d.Dash = LineDash.Dash : d.Brush = BrushKind.Watercolor
                                                   End Sub))), 50, 40, 160)
        Check("水彩筆也能畫虛線", wcDash.Switches >= 6, $"{wcDash.Switches}")
        ' ---- 端點 ----
        ' 線的右端在 x = 170、線寬 8 像素：看最右邊的黑點與右上角
        Dim rightmost = Function(b As Bitmap, y As Integer) Enumerable.Range(0, 200).Where(Function(x) Dark(b.GetPixel(x, y))).DefaultIfEmpty(-1).Max()
        Dim rb = Render(Recipe(HLine())), fb = Render(Recipe(HLine(Sub(d) d.Cap = LineCapKind.Flat))), sb = Render(Recipe(HLine(Sub(d) d.Cap = LineCapKind.Square)))
        Dim rEnd = rightmost(rb, 50), fEnd = rightmost(fb, 50), sEnd = rightmost(sb, 50)
        Check("端點：圓頭、方頭超出端點，平頭剛好停在端點", fEnd >= 168 AndAlso fEnd <= 171 AndAlso rEnd > fEnd + 1 AndAlso sEnd > fEnd + 1, $"圓 {rEnd} 平 {fEnd} 方 {sEnd}")
        Check("方頭的角是方的（圓頭的角是圓的）", rightmost(sb, 46) >= sEnd - 1 AndAlso rightmost(rb, 46) < rEnd - 1, $"方 {rightmost(sb, 46)}/{sEnd} 圓 {rightmost(rb, 46)}/{rEnd}")
        ' ---- 轉角 ----
        Dim corner = Function(j As LineJoinKind)
                         Dim d As New DrawLayer With {.Shape = DrawShape.Polygon, .Filled = False, .Stroked = True, .StrokeWidth = 0.1, .Join = j,
                                                      .StrokeColorArgb = Color.Black.ToArgb(), .Brush = BrushKind.HardRound,
                                                      .Points = New List(Of DrawPoint) From {New DrawPoint(0.5, 0.3), New DrawPoint(1.5, 0.3), New DrawPoint(1.5, 0.8), New DrawPoint(0.5, 0.8)}}
                         Return Dark(Render(Recipe(d)).GetPixel(154, 26))
                     End Function
        Check("轉角：尖角填滿外角、圓角不填", corner(LineJoinKind.Miter) AndAlso Not corner(LineJoinKind.Round), $"{corner(LineJoinKind.Miter)} {corner(LineJoinKind.Round)}")
        ' ---- 箭頭 ----
        Dim arrowBmp = Render(Recipe(HLine(Sub(d)
                                                d.StrokeWidth = 0.03 : d.EndArrow = ArrowKind.Triangle
                                            End Sub)))
        Dim plainBmp = Render(Recipe(HLine(Sub(d) d.StrokeWidth = 0.03)))
        Dim colH = Function(b As Bitmap, x As Integer) Enumerable.Range(0, 100).Count(Function(y) Dark(b.GetPixel(x, y)))
        Check("終點三角箭頭：尾端變寬、起點不變", colH(arrowBmp, 160) > colH(plainBmp, 160) + 4 AndAlso colH(arrowBmp, 34) = colH(plainBmp, 34),
              $"{colH(arrowBmp, 160)} / {colH(plainBmp, 160)}")
        Dim openBmp = Render(Recipe(HLine(Sub(d)
                                               d.StrokeWidth = 0.03 : d.StartArrow = ArrowKind.Open
                                           End Sub)))
        Check("起點開放箭頭：起點那邊多兩撇", colH(openBmp, 36) > colH(plainBmp, 36) + 3, $"{colH(openBmp, 36)}")
        Dim rectArrow As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 1, .Y = 0.5, .W = 1, .H = 0.6, .Stroked = True, .StrokeWidth = 0.03,
                                             .EndArrow = ArrowKind.Triangle, .StrokeColorArgb = Color.Black.ToArgb()}
        Dim rectPlain = rectArrow.Clone() : rectPlain.EndArrow = ArrowKind.None
        Check("封閉圖形不加箭頭", RowStats(Render(Recipe(rectArrow)), 20, 0, 199).Black = RowStats(Render(Recipe(rectPlain)), 20, 0, 199).Black)
        Dim style As New DrawLayer With {.Dash = LineDash.DashDot, .Cap = LineCapKind.Square, .Join = LineJoinKind.Bevel, .EndArrow = ArrowKind.Diamond}
        Dim copy As New DrawLayer()
        copy.CopyStyleFrom(style)
        Check("線條樣式會複製、存檔讀回", copy.Dash = LineDash.DashDot AndAlso copy.Cap = LineCapKind.Square AndAlso copy.Join = LineJoinKind.Bevel AndAlso copy.EndArrow = ArrowKind.Diamond AndAlso
              System.Text.Json.JsonSerializer.Deserialize(Of DrawLayer)(System.Text.Json.JsonSerializer.Serialize(style)).EndArrow = ArrowKind.Diamond)

        ' ---- PowerClip ----
        Dim frame As New DrawLayer With {.Name = "圓框", .Shape = DrawShape.Ellipse, .X = 1, .Y = 0.5, .W = 0.6, .H = 0.6, .Filled = False, .Stroked = True,
                                         .StrokeWidth = 0.03, .StrokeColorArgb = Color.Blue.ToArgb()}
        Dim content As New DrawLayer With {.Name = "紅塊", .Shape = DrawShape.Rectangle, .X = 1, .Y = 0.5, .W = 1.6, .H = 0.9, .Filled = True, .Stroked = False,
                                           .FillColorArgb = Color.Red.ToArgb()}
        Dim r = Recipe(content, frame) ' 框在上面
        Dim g = DrawGroups.CreateClip(r, frame, {content}, Nothing, "PowerClip")
        Dim order = LayerStack.Order(r)
        Check("放進形狀：框排到群組最下面", order(0).Item Is frame AndAlso g.ClipFrame = frame.Id AndAlso content.GroupId = g.Id, String.Join(",", order.Select(Function(x) x.Drawing?.Name)))
        Check("ClipOf：內容找得到框、框本身不算", ClipRender.ClipOf(r, content).HasValue AndAlso ClipRender.ClipOf(r, frame) Is Nothing AndAlso ClipRender.IsFrame(r, frame))
        Using bmp = Render(r)
            Dim inside = bmp.GetPixel(100, 50), outside = bmp.GetPixel(40, 50), edge = bmp.GetPixel(130, 50)
            Check("內容只畫在框裡（框外是原本的白色）", inside.R > 200 AndAlso inside.G < 60 AndAlso outside = Color.FromArgb(255, 255, 255, 255), $"{inside} {outside}")
            Check("框的外框畫在內容上面", edge.B > 150 AndAlso edge.R < 120, edge.ToString())
        End Using
        DrawGroups.Ungroup(r, g.Id)
        Using bmp = Render(r)
            Check("取出內容：紅塊整塊畫出來", bmp.GetPixel(40, 50).R > 200 AndAlso bmp.GetPixel(40, 50).G < 60)
        End Using

        ' ---- 照片放進形狀 ----
        Dim heart As New DrawLayer With {.Name = "愛心", .Shape = DrawShape.Heart, .X = 1, .Y = 0.5, .W = 0.7, .H = 0.7, .Filled = False, .Stroked = False, .Id = "heart-1"}
        Dim pr = Recipe(heart)
        pr.PhotoClip = "heart-1"
        Using bmp = Render(pr, Color.FromArgb(40, 160, 90))
            Check("照片放進形狀：形狀裡保留、外面透明", bmp.GetPixel(100, 55).A = 255 AndAlso bmp.GetPixel(100, 55).G > 140 AndAlso bmp.GetPixel(10, 10).A = 0,
                  $"{bmp.GetPixel(100, 55)} {bmp.GetPixel(10, 10)}")
        End Using
        Check("照片放進形狀會存檔、比對得出差別", Not pr.Equals(Recipe(heart.Clone())) AndAlso pr.Clone().PhotoClip = "heart-1")

        ' ---- 文字轉外框 ----
        Dim t As New DrawLayer With {.Shape = DrawShape.Text, .X = 1, .Y = 0.5, .W = 1.2, .H = 0.5, .Text = "A B", .TextSize = 0.3, .FontName = "Arial",
                                     .TextColorArgb = Color.Navy.ToArgb(), .Rotation = 0}
        Dim outs = DrawGeometry.TextOutlines(t)
        Check("文字轉外框：兩個字（空白不算）、填文字色、沒有線條", outs.Count = 2 AndAlso outs.All(Function(o) o.Shape = DrawShape.Path AndAlso o.Filled AndAlso Not o.Stroked AndAlso
              o.FillColorArgb = Color.Navy.ToArgb()) AndAlso outs(0).Name = "A" AndAlso outs(1).Name = "B", $"{outs.Count} {String.Join(",", outs.Select(Function(o) o.Name))}")
        Dim ab = DrawGeometry.Bounds(outs(0)), bb = DrawGeometry.Bounds(outs(1))
        Check("字在框裡、A 在 B 左邊", ab.Right < bb.Left AndAlso ab.Left > 0.4 AndAlso bb.Right < 1.6 AndAlso ab.Top > 0.25 AndAlso ab.Bottom < 0.75, $"{ab} {bb}")
        Check("A 有洞（兩條外形線）", outs(0).Contours.Count = 2, $"{outs(0).Contours.Count}")
        Using bmp = Render(Recipe(outs.ToArray()))
            Dim n = 0
            For y = 0 To 99 Step 2
                For x = 0 To 199 Step 2
                    If bmp.GetPixel(x, y).B > 80 AndAlso bmp.GetPixel(x, y).R < 60 Then n += 1
                Next
            Next
            Check("轉好的外框畫得出來", n > 80, n.ToString())
        End Using
        Dim callout As New DrawLayer With {.Shape = DrawShape.CalloutRect, .X = 1, .Y = 0.5, .W = 1, .H = 0.6, .Text = "Hi", .TextSize = 0.2, .FontName = "Arial", .Param1 = 0.18, .TailX = -0.3, .TailY = 0.5}
        Dim co = DrawGeometry.TextOutlines(callout)
        Check("圖說轉外框：框＋每個字", co.Count = 3 AndAlso co.All(Function(o) o.Shape = DrawShape.Path), $"{co.Count}")
    End Sub
End Module
