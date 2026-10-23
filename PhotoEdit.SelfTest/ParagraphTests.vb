Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>段落文字框：自動換行、行首禁則、對齊、分欄、溢出、旋轉、畫出來、轉曲線、存檔。</summary>
Module ParagraphTests

    Private Function Frame(text As String, Optional w As Double = 0.6, Optional h As Double = 0.6, Optional align As TextAlignKind = TextAlignKind.Left,
                           Optional cols As Integer = 1, Optional size As Double = 0.04) As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Text, .Paragraph = True, .Text = text, .X = 0.5, .Y = 0.5, .W = w, .H = h, .TextAlign = align,
                                   .TextColumns = cols, .TextSize = size, .FontName = "Microsoft JhengHei", .TextBold = False, .Seed = 1}
    End Function

    Private Function Layout(d As DrawLayer, ByRef overflow As Boolean) As List(Of DrawGeometry.GlyphPlace)
        Dim size As Single, fam As FontFamily = Nothing
        Return DrawGeometry.ParagraphLayout(d, 1000, size, fam, overflow)
    End Function

    Private Function Lines(g As List(Of DrawGeometry.GlyphPlace)) As List(Of List(Of DrawGeometry.GlyphPlace))
        Return g.GroupBy(Function(p) CInt(Math.Round(p.Y))).OrderBy(Function(k) k.Key).Select(Function(k) k.OrderBy(Function(p) p.X).ToList()).ToList()
    End Function

    Sub ParagraphTestsRun()
        Console.WriteLine("— 段落文字框 —")
        Dim ov As Boolean
        Dim box = DrawGeometry.TextBox(Frame("x"))
        Dim left = (0.5 + box.X) * 1000, right = (0.5 + box.X + box.Width) * 1000
        Dim en = Frame("The quick brown fox jumps over the lazy dog and keeps running far away from the river bank.")
        Dim g1 = Layout(en, ov)
        Dim l1 = Lines(g1)
        Check("英文自動換行：好幾行、每個字都在框裡、沒有溢出", l1.Count >= 3 AndAlso g1.All(Function(p) p.X - p.Width / 2 >= left - 1 AndAlso p.X + p.Width / 2 <= right + 1) AndAlso Not ov,
              $"{l1.Count} 行")
        Dim words = en.Text.Split(" "c)
        ' 在空白處斷：每行的最後一個字母後面原本就是空白或結尾
        Dim lineTexts = l1.Select(Function(l) String.Concat(l.Select(Function(p) p.Ch))).ToList()
        Check("英文在空白處斷，不會把單字切開", lineTexts.All(Function(t) words.Any(Function(wd) t.EndsWith(wd.Replace(" ", "")))), String.Join(" / ", lineTexts))
        Dim zh = Frame(New String("字"c, 13) & "，" & New String("字"c, 30))
        Dim l2 = Lines(Layout(zh, ov))
        Check("中文每個字都能斷；「，」不會在行首", l2.Count >= 2 AndAlso l2.All(Function(l) l(0).Ch <> "，"), String.Join(" / ", l2.Select(Function(l) String.Concat(l.Select(Function(p) p.Ch)))))
        ' 對齊
        Dim rightL = Lines(Layout(Frame("短句" & vbLf & "第二行", align:=TextAlignKind.Right), ov))
        Check("靠右：每行的最後一個字貼著右邊", rightL.All(Function(l) Math.Abs(l.Last().X + l.Last().Width / 2 - right) < 2), $"{rightL(0).Last().X + rightL(0).Last().Width / 2:0} vs {right:0}")
        Dim centerL = Lines(Layout(Frame("置中", align:=TextAlignKind.Center), ov))
        Dim mid = (centerL(0).First().X - centerL(0).First().Width / 2 + centerL(0).Last().X + centerL(0).Last().Width / 2) / 2
        Check("置中：行的中點在框的中間", Math.Abs(mid - 500) < 2, $"{mid:0}")
        Dim jl = Lines(Layout(Frame(en.Text, align:=TextAlignKind.Justify), ov))
        Check("左右對齊：除了最後一行，每行都撐到右邊", jl.Take(jl.Count - 1).All(Function(l) Math.Abs(l.Last().X + l.Last().Width / 2 - right) < 2) AndAlso
              jl.Last().Last().X + jl.Last().Last().Width / 2 < right - 10, $"{jl.Count} 行")
        ' 分欄
        Dim two = Layout(Frame(New String("文"c, 200), cols:=2), ov)
        Check("兩欄：文字分在左右兩半", two.Any(Function(p) p.X < 470) AndAlso two.Any(Function(p) p.X > 530) AndAlso Not two.Any(Function(p) Math.Abs(p.X - 500) < 8), $"{two.Count} 字")
        ' 溢出
        Dim small = Frame(New String("溢"c, 400), w:=0.3, h:=0.15)
        Dim gs = Layout(small, ov)
        Check("放不下：overflow、只排得進一部分", ov AndAlso gs.Count < 400 AndAlso DrawGeometry.ParagraphOverflows(small), $"{gs.Count} 字")
        Check("放得下：沒有溢出", Not DrawGeometry.ParagraphOverflows(Frame("一點點字")))
        ' 旋轉 90°：字往下排
        Dim rot = Frame("ABCDEFG")
        rot.Rotation = 90
        Dim gr = Layout(rot, ov)
        Check("旋轉 90°：字沿著直的方向排、角度 90", gr.Count = 7 AndAlso gr.All(Function(p) Math.Abs(p.Angle - 90) < 0.01) AndAlso gr.Last().Y > gr.First().Y + 50 AndAlso
              Math.Abs(gr.Last().X - gr.First().X) < 1, $"{gr.First().Y:0}→{gr.Last().Y:0}")
        ' 行距
        Dim tall = Frame(New String("行"c, 60), align:=TextAlignKind.Left)
        tall.LineSpacing = 200
        Dim lsN = Lines(Layout(Frame(New String("行"c, 60)), ov)), lsT = Lines(Layout(tall, ov))
        Check("行距 200%：行與行的距離變成兩倍", lsT.Count >= 2 AndAlso Math.Abs((lsT(1)(0).Y - lsT(0)(0).Y) / (lsN(1)(0).Y - lsN(0)(0).Y) - 2) < 0.02)
        ' 畫出來與轉曲線
        Using bmp As New Bitmap(400, 200, PixelFormat.Format32bppArgb)
            Using gr2 = Graphics.FromImage(bmp)
                gr2.Clear(Color.White)
            End Using
            Dim d = Frame("段落文字框會自動換行。", w:=0.8, h:=0.5, size:=0.09)
            d.TextColorArgb = Color.Black.ToArgb()
            DrawingRenderer.ClearCache()
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {d}})
            Dim inked = 0
            For y = 0 To 199 Step 2
                For x = 0 To 399 Step 2
                    If bmp.GetPixel(x, y).R < 200 Then inked += 1
                Next
            Next
            Check("畫得出來（有字的像素）", inked > 150, $"{inked}")
            Dim outl = DrawGeometry.TextOutlines(d)
            Check("轉成曲線：每個字一個鋼筆路徑（10 個字＋句號）", outl.Count = d.Text.Length AndAlso outl.All(Function(o) o.Shape = DrawShape.Path), $"{outl.Count}")
        End Using
        Dim r As New EditRecipe With {.Drawings = New List(Of DrawLayer) From {Frame("存檔", align:=TextAlignKind.Justify, cols:=3)}}
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(r))
        Check("存檔讀回：段落、對齊、欄數都在", back.Drawings(0).Paragraph AndAlso back.Drawings(0).TextAlign = TextAlignKind.Justify AndAlso back.Drawings(0).TextColumns = 3)
    End Sub
End Module
