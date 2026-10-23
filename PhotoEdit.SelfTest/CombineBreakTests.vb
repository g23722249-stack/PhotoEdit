Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>CorelDRAW 的結合與打散：重疊處變洞、套疊的洞、節點原封不動、打散拆回來、SVG 與存檔。</summary>
Module CombineBreakTests

    Private Function Box(x As Double, y As Double, w As Double, h As Double, argb As Integer) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Rectangle, .X = x, .Y = y, .W = w, .H = h, .Filled = True, .Stroked = False,
                                   .FillColorArgb = argb, .Seed = 1, .Name = "方塊"}
    End Function

    Private Function Render(ParamArray layers As DrawLayer()) As Bitmap
        Dim b As New Bitmap(300, 200, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(b)
            g.Clear(Color.White)
        End Using
        DrawingRenderer.ClearCache()
        DrawingRenderer.DrawLayers(b, New EditRecipe With {.Drawings = layers.ToList()})
        Return b
    End Function

    Private Function IsWhite(c As Color) As Boolean
        Return c.R > 245 AndAlso c.G > 245 AndAlso c.B > 245
    End Function

    Sub CombineBreakTestsRun()
        Console.WriteLine("— 結合與打散 —")
        Dim red = Color.FromArgb(220, 40, 40).ToArgb(), blue = Color.FromArgb(40, 60, 220).ToArgb()
        ' 兩個部分重疊的方塊（座標以照片高度 200 為 1）：左 x 0.3..0.9、右 x 0.6..1.2，重疊 0.6..0.9
        Dim a = Box(0.6, 0.5, 0.6, 0.6, red), b = Box(0.9, 0.5, 0.6, 0.6, blue)
        Check("兩個方塊都可以結合；文字不行", CombineBreak.CanCombine(a) AndAlso CombineBreak.CanCombine(b) AndAlso
              Not CombineBreak.CanCombine(New DrawLayer With {.Shape = DrawShape.Text, .Text = "A"}))
        Dim c = CombineBreak.Combine({a, b})
        Check("結合：一個鋼筆路徑、兩條路徑、奇偶填色、樣式用最下面的（紅）", c IsNot Nothing AndAlso c.Shape = DrawShape.Path AndAlso c.Contours.Count = 2 AndAlso
              c.EvenOdd AndAlso c.FillColorArgb = red)
        Using bmp = Render(c)
            Dim left = bmp.GetPixel(CInt(0.45 * 200), 100), mid = bmp.GetPixel(CInt(0.75 * 200), 100), right = bmp.GetPixel(CInt(1.05 * 200), 100)
            Check("結合後：兩邊塗紅、重疊的地方是洞（白）", left.R > 180 AndAlso left.G < 90 AndAlso right.R > 180 AndAlso right.G < 90 AndAlso IsWhite(mid),
                  $"左 {left} 中 {mid} 右 {right}")
        End Using
        ' 套疊：大方塊裡有小方塊 → 甜甜圈；方向依套疊深度相反，非零環繞的判斷（點選、焊接）也看得到洞
        Dim outer = Box(0.75, 0.5, 0.8, 0.8, red), inner = Box(0.75, 0.5, 0.3, 0.3, red)
        Dim donut = CombineBreak.Combine({outer, inner})
        Using bmp = Render(donut)
            Dim ring = bmp.GetPixel(CInt(0.42 * 200), 100), hole = bmp.GetPixel(CInt(0.75 * 200), 100)
            Check("大方塊＋小方塊結合成甜甜圈：中間是洞", ring.R > 180 AndAlso ring.G < 90 AndAlso IsWhite(hole), $"環 {ring} 洞 {hole}")
        End Using
        Dim areas = donut.Contours.Select(Function(k) PathBoolean.SignedArea(DrawGeometry.SampleContour(k))).ToList()
        Check("外框與洞的方向相反（非零環繞數也是洞）", Math.Sign(areas(0)) <> Math.Sign(areas(1)) AndAlso
              Not PathBoolean.Contains(donut.Contours.Select(Function(k) DrawGeometry.SampleContour(k)), New PointF(0.75F, 0.5F)) AndAlso
              PathBoolean.Contains(donut.Contours.Select(Function(k) DrawGeometry.SampleContour(k)), New PointF(0.42F, 0.5F)))
        ' 節點原封不動（不是像焊接那樣重新擬合外形）：橢圓轉曲線還是 4 個節點
        Dim ell As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.5, .Y = 0.5, .W = 0.4, .H = 0.4, .Filled = True, .FillColorArgb = blue, .Seed = 2}
        Dim ce = CombineBreak.Combine({ell, Box(1.2, 0.5, 0.3, 0.3, red)})
        Check("結合保留原本的節點：橢圓 4 個節點、方塊 4 個節點", ce.Contours(0).Nodes.Count = 4 AndAlso ce.Contours(1).Nodes.Count = 4,
              String.Join(",", ce.Contours.Select(Function(k) k.Nodes.Count)))

        ' 打散
        Check("結合後的圖形可以打散；單一方塊不行", CombineBreak.CanBreakApart(c) AndAlso Not CombineBreak.CanBreakApart(a))
        Dim parts = CombineBreak.BreakApart(c)
        Check("打散：拆成 2 個圖形、各一條路徑、填色恢復一般、樣式不變", parts IsNot Nothing AndAlso parts.Count = 2 AndAlso
              parts.All(Function(p) p.Contours.Count = 1 AndAlso Not p.EvenOdd AndAlso p.FillColorArgb = red))
        Check("打散：第一塊沿用原本的 Id、其他是新的；名稱加編號", parts(0).Id = c.Id AndAlso parts(1).Id Is Nothing AndAlso parts(0).Name = "方塊 1" AndAlso parts(1).Name = "方塊 2",
              $"{parts(0).Name}、{parts(1).Name}")
        Using bmp = Render(parts.ToArray())
            Dim mid = bmp.GetPixel(CInt(0.75 * 200), 100)
            Check("打散後重疊的地方又塗滿了（洞不見）", mid.R > 180 AndAlso mid.G < 90, $"{mid}")
        End Using
        Dim dparts = CombineBreak.BreakApart(donut)
        Using bmp = Render(dparts.ToArray())
            Dim hole = bmp.GetPixel(CInt(0.75 * 200), 100)
            Check("甜甜圈打散：洞變成實心的一塊（同 CorelDRAW）", dparts.Count = 2 AndAlso hole.R > 180 AndAlso hole.G < 90, $"{hole}")
        End Using
        ' 轉成曲線後有兩條路徑的圖形（例如格紙）也能打散
        Dim parts2 = CombineBreak.BreakApart(CombineBreak.Combine(parts.Cast(Of DrawLayer)().ToArray()))
        Check("打散 → 再結合 → 再打散，路徑數不變", parts2 IsNot Nothing AndAlso parts2.Count = 2)

        ' SVG 與存檔
        Dim svg = SvgExport.Export(New EditRecipe With {.Drawings = New List(Of DrawLayer) From {c}}, Nothing, 300, 200)
        Check("匯出 SVG：結合的圖形有 fill-rule=""evenodd""", svg.Contains("fill-rule=""evenodd"""))
        Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {c}}
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(r))
        Check("存檔讀回：奇偶填色與兩條路徑都在", back.Drawings(0).EvenOdd AndAlso back.Drawings(0).Contours.Count = 2)
        Check("複製圖層也複製奇偶填色", c.Clone().EvenOdd)
    End Sub
End Module
