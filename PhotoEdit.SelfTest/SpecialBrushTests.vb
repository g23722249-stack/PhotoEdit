Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>Painter 式的筆刷：進階參數、繪圖筆傾斜、對稱、粒子筆、貼圖噴槍、混色／塗抹／仿製筆。</summary>
Module SpecialBrushTests

    Private Function Line(brush As BrushKind, x0 As Single, y0 As Single, x1 As Single, y1 As Single, color As Color, width As Double,
                          Optional setup As Action(Of DrawLayer) = Nothing) As DrawLayer
        Dim pts As New List(Of DrawPoint)()
        For i = 0 To 20
            pts.Add(New DrawPoint(x0 + (x1 - x0) * i / 20.0F, y0 + (y1 - y0) * i / 20.0F))
        Next
        Dim d As New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = brush, .StrokeColorArgb = color.ToArgb(), .StrokeWidth = width, .Seed = 9,
                                     .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}}
        setup?.Invoke(d)
        Return d
    End Function

    ''' <summary>左半紅、右半藍的「照片」上畫圖層。</summary>
    Private Function Render(layers As List(Of DrawLayer), Optional w As Integer = 200, Optional h As Integer = 100,
                            Optional left As Color? = Nothing, Optional right As Color? = Nothing) As Bitmap
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
            If left.HasValue Then g.FillRectangle(New SolidBrush(left.Value), 0, 0, w \ 2, h)
            If right.HasValue Then g.FillRectangle(New SolidBrush(right.Value), w \ 2, 0, w - w \ 2, h)
        End Using
        DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = layers})
        Return bmp
    End Function

    Private Function Painted(bmp As Bitmap, Optional background As Color? = Nothing) As Integer
        Dim bg = If(background, Color.White)
        Dim n = 0
        For y = 0 To bmp.Height - 1 Step 2
            For x = 0 To bmp.Width - 1 Step 2
                Dim p = bmp.GetPixel(x, y)
                If Math.Abs(CInt(p.R) - bg.R) + Math.Abs(CInt(p.G) - bg.G) + Math.Abs(CInt(p.B) - bg.B) > 30 Then n += 1
            Next
        Next
        Return n
    End Function

    Sub SpecialBrushTestsRun()
        Console.WriteLine("Painter 式筆刷")
        DrawingRenderer.ClearCache()

        ' ---- 模型 ----
        Check("筆刷名稱 21 個（含混色、塗抹、粒子、仿製、貼圖噴槍）", DrawGeometry.BrushNames.Length = 21 AndAlso DrawGeometry.BrushNames(20) = "貼圖噴槍")
        Check("只有混色、塗抹、仿製要讀畫布", DrawLayer.SamplesCanvas(BrushKind.Smudge) AndAlso DrawLayer.SamplesCanvas(BrushKind.Clone) AndAlso
              DrawLayer.SamplesCanvas(BrushKind.Mixer) AndAlso Not DrawLayer.SamplesCanvas(BrushKind.Particle))
        Dim p As New DrawPoint(0.1F, 0.2F, 0.5F)
        Dim json = System.Text.Json.JsonSerializer.Serialize(p)
        Check("沒有傾斜資料時不寫進編輯檔", Not json.Contains("Tx") AndAlso Not json.Contains("""R"""), json)
        p.Tx = 30 : p.Ty = -10 : p.R = 90
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of DrawPoint)(System.Text.Json.JsonSerializer.Serialize(p))
        Check("傾斜與旋轉存得回來", back.Tx = 30 AndAlso back.Ty = -10 AndAlso back.R = 90 AndAlso p.Clone().R = 90)
        Dim style As New DrawLayer With {.HueJitter = 40, .Scatter = 20, .Symmetry = SymmetryKind.Kaleido, .SymCount = 5, .Particle = ParticleKind.Spring, .HoseTheme = "x", .PenTilt = True}
        Dim copy As New DrawLayer()
        copy.CopyStyleFrom(style)
        Check("複製筆觸設定包含進階參數", copy.HueJitter = 40 AndAlso copy.Scatter = 20 AndAlso copy.Symmetry = SymmetryKind.Kaleido AndAlso
              copy.SymCount = 5 AndAlso copy.Particle = ParticleKind.Spring AndAlso copy.HoseTheme = "x" AndAlso copy.PenTilt AndAlso copy.SameStroke(style))
        copy.HueJitter = 0
        Check("進階參數不同就不是同一種筆觸", Not copy.SameStroke(style))

        ' ---- 對稱 ----
        Dim sym = Line(BrushKind.HardRound, 0.2F, 0.3F, 0.4F, 0.3F, Color.Black, 0.01, Sub(d)
                                                                                         d.SymX = 1 : d.SymY = 0.5
                                                                                     End Sub)
        Check("不對稱：一條線", DrawGeometry.Figures(sym).Count = 1)
        sym.Symmetry = SymmetryKind.MirrorX
        Dim figs = DrawGeometry.Figures(sym)
        Check("左右對稱：兩條，第二條鏡射到中心另一邊", figs.Count = 2 AndAlso Math.Abs(figs(1).Points(0).X - 1.8F) < 0.001 AndAlso Math.Abs(figs(1).Points(0).Y - 0.3F) < 0.001)
        sym.Symmetry = SymmetryKind.MirrorXY
        Check("上下左右：四條", DrawGeometry.Figures(sym).Count = 4)
        sym.Symmetry = SymmetryKind.Rotate : sym.SymCount = 6
        Check("旋轉 6 等分：六條", DrawGeometry.Figures(sym).Count = 6)
        sym.Symmetry = SymmetryKind.Kaleido
        figs = DrawGeometry.Figures(sym)
        Check("萬花筒 6 等分：十二條，都和中心等距", figs.Count = 12 AndAlso
              figs.All(Function(f) Math.Abs(DrawGeometry.Dist(f.Points(0), New PointF(1, 0.5F)) - DrawGeometry.Dist(New PointF(0.2F, 0.3F), New PointF(1, 0.5F))) < 0.001))
        Using bmp = Render(New List(Of DrawLayer) From {Line(BrushKind.HardRound, 0.2F, 0.3F, 0.6F, 0.3F, Color.Black, 0.03, Sub(d)
                                                                                                                                 d.Symmetry = SymmetryKind.MirrorX : d.SymX = 1 : d.SymY = 0.5
                                                                                                                             End Sub)})
            Check("對稱的複本真的畫出來", bmp.GetPixel(40, 30).R < 80 AndAlso bmp.GetPixel(160, 30).R < 80 AndAlso bmp.GetPixel(100, 30).R > 200)
        End Using

        ' ---- 進階參數 ----
        Dim plain = Line(BrushKind.SoftRound, 0.1F, 0.5F, 1.9F, 0.5F, Color.FromArgb(220, 40, 40), 0.1)
        Dim jitter = plain.Clone()
        jitter.HueJitter = 100
        Using a = Render(New List(Of DrawLayer) From {plain}), b = Render(New List(Of DrawLayer) From {jitter}), b2 = Render(New List(Of DrawLayer) From {jitter})
            Dim hues As New HashSet(Of Integer)()
            For x = 20 To 180 Step 4
                Dim c = b.GetPixel(x, 50)
                hues.Add(CInt(c.GetHue() / 30))
            Next
            Check("色相變化：一筆裡出現多種色相", hues.Count >= 4, hues.Count.ToString())
            Check("色相變化每次算出來都一樣（同一個種子）", b.GetPixel(77, 50) = b2.GetPixel(77, 50) AndAlso b.GetPixel(141, 50) = b2.GetPixel(141, 50))
            Check("沒有顏色變化時仍是原來的紅", a.GetPixel(100, 50).R > 180 AndAlso a.GetPixel(100, 50).G < 90)
        End Using
        Dim dotted = Line(BrushKind.HardRound, 0.1F, 0.5F, 1.9F, 0.5F, Color.Black, 0.06, Sub(d) d.SpacingPct = 200)
        Using bmp = Render(New List(Of DrawLayer) From {dotted})
            Dim gaps = 0
            For x = 20 To 180
                If bmp.GetPixel(x, 50).R > 200 Then gaps += 1
            Next
            Check("間距 200%：筆畫變成一顆顆的點（中間有空隙）", gaps > 40, gaps.ToString())
        End Using
        Dim scattered = Line(BrushKind.HardRound, 0.1F, 0.5F, 1.9F, 0.5F, Color.Black, 0.04, Sub(d)
                                                                                               d.Scatter = 100 : d.SpacingPct = 50
                                                                                           End Sub)
        Using bmp = Render(New List(Of DrawLayer) From {scattered}), ref = Render(New List(Of DrawLayer) From {Line(BrushKind.HardRound, 0.1F, 0.5F, 1.9F, 0.5F, Color.Black, 0.04)})
            Dim off = Function(b As Bitmap) Enumerable.Range(10, 180).Count(Function(x) b.GetPixel(x, 50 - 4).R < 128 OrElse b.GetPixel(x, 50 + 4).R < 128)
            Check("散佈：筆印偏離筆畫中線", off(bmp) > off(ref) + 10, off(bmp) & " / " & off(ref))
        End Using

        ' ---- 繪圖筆傾斜 ----
        Dim marker = Line(BrushKind.Marker, 0.3F, 0.5F, 1.7F, 0.5F, Color.Black, 0.12)
        Dim tilted = marker.Clone()
        tilted.PenTilt = True
        For Each pt In tilted.Strokes(0).Points
            pt.Tx = 0 : pt.Ty = 60 ' 往下傾：筆尖長軸轉成直的
        Next
        Using a = Render(New List(Of DrawLayer) From {marker}), b = Render(New List(Of DrawLayer) From {tilted})
            Dim height = Function(bm As Bitmap) Enumerable.Range(0, 100).Count(Function(y) bm.GetPixel(100, y).R < 128)
            Check("繪圖筆傾斜：麥克筆筆觸變寬（筆尖轉向、變扁的方向跟著筆）", height(b) > height(a) + 3, height(a) & " → " & height(b))
            Dim noTilt = tilted.Clone()
            noTilt.PenTilt = False
            Using c = Render(New List(Of DrawLayer) From {noTilt})
                Check("沒勾「筆傾斜改變筆尖」時忽略傾斜資料", height(c) = height(a))
            End Using
        End Using

        ' ---- 粒子筆 ----
        For Each kind In {ParticleKind.Gravity, ParticleKind.Flow, ParticleKind.Spring}
            Dim pk = kind
            Using bmp = Render(New List(Of DrawLayer) From {Line(BrushKind.Particle, 0.3F, 0.5F, 1.7F, 0.5F, Color.FromArgb(30, 60, 160), 0.05, Sub(d) d.Particle = pk)})
                Check($"粒子筆（{pk}）畫得出細絲", Painted(bmp) > 40, Painted(bmp).ToString())
            End Using
        Next

        ' ---- 貼圖噴槍 ----
        Using bmp = Render(New List(Of DrawLayer) From {Line(BrushKind.StickerHose, 0.2F, 0.5F, 1.8F, 0.5F, Color.Black, 0.06, Sub(d) d.HoseTheme = StickerLibrary.BuiltInTheme)})
            Check("貼圖噴槍（內建主題）沿筆畫噴出貼圖", Painted(bmp) > 150, Painted(bmp).ToString())
        End Using

        ' ---- 混色、塗抹、仿製（點陣圖層） ----
        Dim red = Color.FromArgb(220, 30, 30), blue = Color.FromArgb(30, 60, 220)
        Using bmp = Render(New List(Of DrawLayer) From {Line(BrushKind.Smudge, 0.4F, 0.5F, 1.6F, 0.5F, Color.White, 0.2)})
            Check("向量圖層裡的塗抹筆不畫任何東西（只能用在點陣圖層）", bmp.GetPixel(100, 50).R = 255 AndAlso bmp.GetPixel(100, 50).B = 255)
        End Using
        Dim smudge As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            Line(BrushKind.Smudge, 0.5F, 0.5F, 1.5F, 0.5F, Color.White, 0.3, Sub(d) d.Wet = 90)}}
        Using bmp = Render(New List(Of DrawLayer) From {smudge}, left:=red, right:=blue)
            Dim c = bmp.GetPixel(120, 50)
            Check("塗抹筆：把左邊的紅推進右邊的藍", c.R > 90, c.ToString())
            Check("塗抹筆：沒抹到的地方照片不變", bmp.GetPixel(180, 5) = Color.FromArgb(255, blue))
        End Using
        Dim mixer As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            Line(BrushKind.Mixer, 1.2F, 0.5F, 1.8F, 0.5F, Color.FromArgb(250, 230, 20), 0.3, Sub(d) d.Wet = 80)}}
        Using bmp = Render(New List(Of DrawLayer) From {mixer}, left:=red, right:=blue)
            Dim c = bmp.GetPixel(160, 50)
            Check("混色筆：黃色顏料混進底下的藍（不是純黃也不是純藍）", c.R > 60 AndAlso c.R < 245 AndAlso c.B > 40, c.ToString())
        End Using
        Dim clone As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {
            Line(BrushKind.Clone, 1.3F, 0.5F, 1.7F, 0.5F, Color.White, 0.3, Sub(d)
                                                                                d.CloneDX = -1 : d.Softness = 0
                                                                            End Sub)}}
        Using bmp = Render(New List(Of DrawLayer) From {clone}, left:=red, right:=blue)
            Dim c = bmp.GetPixel(150, 50)
            Check("仿製筆：把左邊（紅）畫到右邊", c.R > 180 AndAlso c.B < 90, c.ToString())
        End Using
        ' 照片改了（下面變綠），抹過的地方要跟著重算，不能用舊的快取。
        Using bmp = Render(New List(Of DrawLayer) From {clone}, left:=Color.FromArgb(30, 200, 60), right:=blue)
            Dim c = bmp.GetPixel(150, 50)
            Check("照片改了：仿製的結果跟著更新", c.G > 150 AndAlso c.R < 90, c.ToString())
        End Using
    End Sub
End Module
