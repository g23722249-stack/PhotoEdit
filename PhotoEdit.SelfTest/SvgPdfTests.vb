Imports System.Drawing.Imaging
Imports System.IO
Imports System.Text
Imports PhotoEdit

''' <summary>匯入 SVG（各種元素、path 指令、變形、顏色、round-trip）與匯出 PDF（結構、頁面大小、透明）。</summary>
Module SvgPdfTests

    Private Function Render(layers As List(Of DrawLayer), w As Integer, h As Integer) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(b)
            g.Clear(Color.White)
        End Using
        DrawingRenderer.ClearCache()
        DrawingRenderer.DrawLayers(b, New EditRecipe With {.Drawings = layers})
        Return b
    End Function

    Sub SvgPdfTestsRun()
        Console.WriteLine("— 匯入 SVG、匯出 PDF —")
        ' path 指令
        Dim c1 = SvgImport.PathData("M10 10 L 90 10 L 90 90 Z")
        Check("path M L Z：一條封閉路徑、3 個節點", c1.Count = 1 AndAlso c1(0).Closed AndAlso c1(0).Nodes.Count = 3)
        Dim c2 = SvgImport.PathData("m10,10 h80 v80 h-80 z m20,20 h40 v40 h-40 z")
        Check("相對座標 m h v z、兩條子路徑", c2.Count = 2 AndAlso c2(1).Nodes(0).X = 30 AndAlso c2(0).Nodes(2).X = 90 AndAlso c2(0).Nodes(2).Y = 90,
              $"{c2.Count} 條；{c2(1).Nodes(0).X},{c2(0).Nodes(2).X},{c2(0).Nodes(2).Y}")
        Dim c3 = SvgImport.PathData("M0 50 C 0 0 100 0 100 50 S 200 100 200 50 Q 250 0 300 50 T 400 50")
        Check("C S Q T：曲線的把手（S 對稱、Q 換成三次）", c3(0).Nodes.Count = 5 AndAlso c3(0).Nodes(0).OutY = 0 AndAlso
              Math.Abs(c3(0).Nodes(1).OutX - 100) < 0.01 AndAlso Math.Abs(c3(0).Nodes(1).OutY - 100) < 0.01 AndAlso
              Math.Abs(c3(0).Nodes(2).OutX - (200 + 2 / 3.0 * 50)) < 0.01, $"{c3(0).Nodes(1).OutX},{c3(0).Nodes(1).OutY}")
        Dim c4 = SvgImport.PathData("M 0 50 A 50 50 0 1 1 100 50 A 50 50 0 1 1 0 50 Z")
        Dim pts = DrawGeometry.SampleContour(c4(0))
        Dim maxErr = pts.Max(Function(p) Math.Abs(Math.Sqrt((p.X - 50) ^ 2 + (p.Y - 50) ^ 2) - 50))
        Check("A 圓弧換成貝茲：兩段半圓接成一個圓，誤差 < 0.1", c4(0).Closed AndAlso maxErr < 0.1, $"誤差 {maxErr:0.000}")

        ' 元素、樣式、變形
        Dim svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 100'>" &
                  "<defs><linearGradient id='g1'><stop offset='0' stop-color='#00ff00'/></linearGradient></defs>" &
                  "<rect x='10' y='10' width='60' height='40' fill='#ff0000'/>" &
                  "<g transform='translate(100,0)' style='fill:blue;stroke:black;stroke-width:2'>" &
                  "<circle cx='30' cy='30' r='20'/><ellipse cx='70' cy='70' rx='20' ry='10' fill='rgb(255,128,0)' opacity='0.5'/></g>" &
                  "<polygon points='10,90 30,60 50,90' fill='url(#g1)'/><polyline points='60,90 80,70 100,90' fill='none' stroke='purple'/>" &
                  "<line x1='0' y1='0' x2='200' y2='100' stroke='#333'/>" &
                  "<path d='M120 60 h50 v30 h-50 z M130 65 h30 v20 h-30 z' fill='navy' fill-rule='evenodd'/>" &
                  "<rect x='0' y='0' width='10' height='10' rx='3' fill='gray' transform='rotate(45 5 5)'/>" &
                  "<text x='10' y='10'>hi</text><image href='a.png'/></svg>"
        Dim res = SvgImport.ParseText(svg, 400, 200)
        Check("匯入 8 個圖形；text、image 記在沒匯入", res.Layers.Count = 8 AndAlso res.Skipped.ContainsKey("text") AndAlso res.Skipped.ContainsKey("image"),
              $"{res.Layers.Count} 個；{String.Join(",", res.Skipped.Keys)}")
        Dim ly = res.Layers
        Check("rect 紅色填色、沒有外框", ly(0).Filled AndAlso ly(0).FillColorArgb = Color.Red.ToArgb() AndAlso Not ly(0).Stroked)
        Check("g 的 style 繼承：圓是藍色填色＋黑色外框", ly(1).FillColorArgb = Color.Blue.ToArgb() AndAlso ly(1).Stroked AndAlso ly(1).StrokeColorArgb = Color.Black.ToArgb())
        Check("rgb() 顏色與 opacity 0.5 → 不透明度 50", ly(2).FillColorArgb = Color.FromArgb(255, 128, 0).ToArgb() AndAlso ly(2).Opacity = 50, $"{ly(2).Opacity}")
        Check("漸層 url(#g1) 用第一個色標（綠）", ly(3).FillColorArgb = Color.FromArgb(0, 255, 0).ToArgb())
        Check("polyline fill=none：只有線、紫色、開放路徑", Not ly(4).Filled AndAlso ly(4).Stroked AndAlso Not ly(4).Contours(0).Closed AndAlso
              ly(4).StrokeColorArgb = Color.Purple.ToArgb())
        Check("evenodd 的路徑：兩條路徑、奇偶填色", ly(6).Contours.Count = 2 AndAlso ly(6).EvenOdd)
        ' 位置：viewBox 200×100 放進 400×200 的 80%（每個 SVG 單位 = 1.6 像素，左上偏移 40,20）→ 圖層座標 = 像素 / 200
        Dim circleCenter = DrawGeometry.GroupBounds({ly(1)})
        Dim expX = ((130 * 1.6) + 40) / 200.0, expY = ((30 * 1.6) + 20) / 200.0
        Check("translate 與縮放：圓心位置正確", Math.Abs(circleCenter.X + circleCenter.Width / 2 - expX) < 0.02 AndAlso Math.Abs(circleCenter.Y + circleCenter.Height / 2 - expY) < 0.02,
              $"{circleCenter.X + circleCenter.Width / 2:0.000},{circleCenter.Y + circleCenter.Height / 2:0.000} 預期 {expX:0.000},{expY:0.000}")
        Check("線寬跟著縮放（stroke-width 2 × 1.6 像素 ÷ 200）", Math.Abs(ly(1).StrokeWidth - 2 * 1.6 / 200) < 0.0005, $"{ly(1).StrokeWidth:0.0000}")
        Using bmp = Render(ly, 400, 200)
            Dim red = bmp.GetPixel(CInt(40 * 1.6 + 40), CInt(30 * 1.6 + 20)), hole = bmp.GetPixel(CInt(145 * 1.6 + 40), CInt(75 * 1.6 + 20)), navy = bmp.GetPixel(CInt(125 * 1.6 + 40), CInt(75 * 1.6 + 20))
            Check("畫出來：方塊是紅的、evenodd 中間是洞、外圈深藍", red.R > 200 AndAlso red.G < 60 AndAlso hole.R > 240 AndAlso hole.B > 240 AndAlso navy.B > 90 AndAlso navy.R < 40,
                  $"紅 {red} 洞 {hole} 深藍 {navy}")
        End Using

        ' round-trip：PhotoEdit 匯出的 SVG 再匯入
        Dim a As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.5, .Y = 0.5, .W = 0.4, .H = 0.3, .Filled = True, .Stroked = False, .FillColorArgb = Color.FromArgb(200, 30, 60).ToArgb(), .Seed = 1}
        Dim e As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 1.1, .Y = 0.5, .W = 0.3, .H = 0.3, .Filled = True, .Stroked = True, .StrokeWidth = 0.01,
                                     .FillColorArgb = Color.FromArgb(30, 160, 90).ToArgb(), .StrokeColorArgb = Color.FromArgb(20, 20, 20).ToArgb(), .Seed = 2}
        Dim exported = SvgExport.Export(New EditRecipe With {.Drawings = New List(Of DrawLayer) From {a, e}}, Nothing, 300, 200)
        Dim back = SvgImport.ParseText(exported, 300, 200).Layers
        Check("PhotoEdit 匯出的 SVG 再匯入：兩個圖形、顏色相同", back.Count >= 2 AndAlso back.Any(Function(d) d.Filled AndAlso d.FillColorArgb = a.FillColorArgb) AndAlso
              back.Any(Function(d) d.Filled AndAlso d.FillColorArgb = e.FillColorArgb), $"{back.Count} 個")
        Dim rb = DrawGeometry.GroupBounds(back.Where(Function(d) d.FillColorArgb = a.FillColorArgb).Take(1))
        Check("再匯入的矩形維持寬高比 4:3", Math.Abs(rb.Width / rb.Height - 0.4 / 0.3) < 0.02, $"{rb.Width / rb.Height:0.000}")

        ' PDF
        Using bmp As New Bitmap(60, 40, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.FromArgb(255, 20, 120, 220))
                g.FillRectangle(New SolidBrush(Color.FromArgb(0, 0, 0, 0)), 0, 0, 10, 10)
            End Using
            bmp.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0))
            Using ms As New MemoryStream()
                PdfExport.WriteImage(ms, bmp, 300, "測試")
                Dim bytes = ms.ToArray()
                Dim text = Encoding.Latin1.GetString(bytes)
                Check("PDF 開頭 %PDF-1.4、結尾 %%EOF", text.StartsWith("%PDF-1.4") AndAlso text.TrimEnd().EndsWith("%%EOF"))
                Dim sx = CInt(text.Substring(text.LastIndexOf("startxref") + 9).Trim().Split(ChrW(10))(0))
                Check("startxref 指到 xref 表", text.Substring(sx, 4) = "xref", $"{sx}")
                Dim lines = text.Substring(sx).Split(ChrW(10))
                Dim count = CInt(lines(1).Split(" "c)(1))
                Dim allOk = True
                For k = 1 To count - 1
                    Dim off = CInt(lines(2 + k).Substring(0, 10))
                    If Not text.Substring(off).StartsWith($"{k} 0 obj") Then allOk = False
                Next
                Check($"xref 每個物件的位移都正確（{count - 1} 個）", allOk)
                Check("有透明：影像帶 SMask", text.Contains("/SMask") AndAlso text.Contains("/DeviceGray"))
                Check("頁面大小：60×40 像素在 300 DPI = 14.4×9.6 點", text.Contains("/MediaBox [0 0 14.4 9.6]"))
            End Using
        End Using
        Using bmp As New Bitmap(30, 30, PixelFormat.Format24bppRgb), ms As New MemoryStream()
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.Orange)
            End Using
            PdfExport.WriteImage(ms, bmp, 72)
            Dim text = Encoding.Latin1.GetString(ms.ToArray())
            Check("不透明的圖：沒有 SMask", Not text.Contains("/SMask"))
        End Using
    End Sub
End Module
