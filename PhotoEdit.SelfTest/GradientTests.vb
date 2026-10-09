Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>漸層工具：線性、放射、角度、反射、菱形、四色、透明、彩虹、反轉、防色階、選取範圍裁切、點陣圖層操作與存檔。</summary>
Module GradientTests

    Private Function At(px As Byte(), w As Integer, x As Integer, y As Integer) As Color
        Dim i = (y * w + x) * 4
        Return Color.FromArgb(px(i + 3), px(i + 2), px(i + 1), px(i))
    End Function

    Private Function Near(a As Color, b As Color, Optional tol As Integer = 6) As Boolean
        Return Math.Abs(CInt(a.R) - b.R) <= tol AndAlso Math.Abs(CInt(a.G) - b.G) <= tol AndAlso Math.Abs(CInt(a.B) - b.B) <= tol AndAlso Math.Abs(CInt(a.A) - b.A) <= tol
    End Function

    Sub GradientTestsRun()
        Console.WriteLine("漸層")
        Dim red = Color.FromArgb(255, 0, 0), blue = Color.FromArgb(0, 0, 255)
        Const W = 200, H = 100
        ' 照片高度單位：寬 2、高 1；從 x=0.5 拉到 x=1.5（中間一半）
        Dim g As New GradientFill With {.X1 = 0.5, .Y1 = 0.5, .X2 = 1.5, .Y2 = 0.5, .Dither = False}
        Dim px = DrawingRenderer.GradientPixels(g, W, H, red, blue)
        Check("線性：起點以前是起點色、終點以後是終點色", Near(At(px, W, 10, 50), red, 0) AndAlso Near(At(px, W, 190, 50), blue, 0))
        Check("線性：中間是一半一半", Near(At(px, W, 100, 50), Color.FromArgb(128, 0, 127), 3), At(px, W, 100, 50).ToString())
        Check("線性：和拖曳方向垂直的方向顏色不變", At(px, W, 80, 5) = At(px, W, 80, 95))

        g.Reverse = True
        px = DrawingRenderer.GradientPixels(g, W, H, red, blue)
        Check("反轉：起點變終點色", Near(At(px, W, 10, 50), blue, 0) AndAlso Near(At(px, W, 190, 50), red, 0))
        g.Reverse = False

        g.Kind = GradientKind.Reflected : g.X1 = 1 : g.X2 = 1.5
        px = DrawingRenderer.GradientPixels(g, W, H, red, blue)
        Check("反射：起點往兩邊對稱", Near(At(px, W, 70, 50), At(px, W, 129, 50), 3) AndAlso Near(At(px, W, 100, 50), red, 3))

        g.Kind = GradientKind.Radial : g.X1 = 1 : g.Y1 = 0.5 : g.X2 = 1.4 : g.Y2 = 0.5
        px = DrawingRenderer.GradientPixels(g, W, H, red, blue)
        Check("放射：中心是起點色、同距離同顏色", Near(At(px, W, 100, 50), red, 6) AndAlso Near(At(px, W, 120, 50), At(px, W, 100, 70), 3) AndAlso Near(At(px, W, 190, 50), blue, 0))

        g.Kind = GradientKind.Angle
        px = DrawingRenderer.GradientPixels(g, W, H, red, blue)
        Check("角度：沿起點轉一圈（拖曳方向是起點色、反方向是一半）", Near(At(px, W, 150, 50), red, 6) AndAlso Near(At(px, W, 50, 50), Color.FromArgb(128, 0, 127), 6), $"{At(px, W, 150, 50)} {At(px, W, 50, 50)}")

        g.Kind = GradientKind.Diamond
        px = DrawingRenderer.GradientPixels(g, W, H, red, blue)
        Check("菱形：上下左右對稱、斜角變化較快", Near(At(px, W, 120, 50), At(px, W, 100, 70), 3) AndAlso Near(At(px, W, 79, 50), At(px, W, 120, 50), 3) AndAlso
              At(px, W, 114, 64).B > At(px, W, 120, 50).B)

        g.Kind = GradientKind.Linear : g.X1 = 0.5 : g.X2 = 1.5 : g.Colors = GradientColors.StrokeToTransparent
        px = DrawingRenderer.GradientPixels(g, W, H, red, blue)
        Check("線條色 → 透明", At(px, W, 10, 50).A = 255 AndAlso At(px, W, 190, 50).A = 0 AndAlso At(px, W, 100, 50).R = 255)

        g.Colors = GradientColors.Rainbow
        px = DrawingRenderer.GradientPixels(g, W, H, red, blue)
        Dim mid = At(px, W, 100, 50)
        Check("彩虹：紅開始、中間是綠", Near(At(px, W, 10, 50), red, 0) AndAlso mid.G > 200 AndAlso mid.R < 60, mid.ToString())

        Dim four As New GradientFill With {.Kind = GradientKind.FourColor, .X1 = 0, .Y1 = 0, .X2 = 2, .Y2 = 1, .Dither = False}
        px = DrawingRenderer.GradientPixels(four, W, H, red, blue)
        Check("四色：四個角各是設定的顏色", Near(At(px, W, 0, 0), Color.FromArgb(four.Corner1), 4) AndAlso Near(At(px, W, 199, 0), Color.FromArgb(four.Corner2), 4) AndAlso
              Near(At(px, W, 0, 99), Color.FromArgb(four.Corner3), 4) AndAlso Near(At(px, W, 199, 99), Color.FromArgb(four.Corner4), 4))

        ' 防色階：深灰兩色差很小時，有雜點的版本相鄰像素不會整段一樣
        Dim dark As New GradientFill With {.X1 = 0, .Y1 = 0.5, .X2 = 2, .Y2 = 0.5, .Dither = True}
        px = DrawingRenderer.GradientPixels(dark, W, H, Color.FromArgb(40, 40, 40), Color.FromArgb(44, 44, 44))
        Dim runs = Enumerable.Range(1, 199).Count(Function(x) At(px, W, x, 50).R <> At(px, W, x - 1, 50).R)
        dark.Dither = False
        Dim px2 = DrawingRenderer.GradientPixels(dark, W, H, Color.FromArgb(40, 40, 40), Color.FromArgb(44, 44, 44))
        Dim runs2 = Enumerable.Range(1, 199).Count(Function(x) At(px2, W, x, 50).R <> At(px2, W, x - 1, 50).R)
        Check("防色階：色階邊界打散", runs > runs2 * 4, $"{runs2} → {runs}")

        ' 點陣圖層裡的漸層操作＋選取範圍裁切
        DrawingRenderer.ClearCache()
        Dim op As New DrawLayer With {.Shape = DrawShape.Gradient, .StrokeColorArgb = red.ToArgb(), .FillColorArgb = blue.ToArgb(), .Param1 = 2,
                                      .Gradient = New GradientFill With {.X1 = 0, .Y1 = 0.5, .X2 = 2, .Y2 = 0.5}}
        Dim layer As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {op}}
        Using bmp As New Bitmap(W, H, PixelFormat.Format32bppArgb)
            Using gr = Graphics.FromImage(bmp)
                gr.Clear(Color.White)
            End Using
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {layer}})
            Check("點陣圖層的漸層操作：整個圖層畫上漸層", Near(bmp.GetPixel(2, 50), red, 6) AndAlso Near(bmp.GetPixel(197, 50), blue, 6))
        End Using
        op.Region = New SelectionSpec With {.Ops = New List(Of SelectionOp) From {
            New SelectionOp With {.Mode = SelectionMode.Replace, .Shape = SelectionShape.Rectangle, .X = 0.25, .Y = 0.25, .W = 0.5, .H = 0.5}}}
        DrawingRenderer.ClearCache()
        Using bmp As New Bitmap(W, H, PixelFormat.Format32bppArgb)
            Using gr = Graphics.FromImage(bmp)
                gr.Clear(Color.White)
            End Using
            DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {layer}})
            Check("有選取範圍：只畫在範圍裡", bmp.GetPixel(10, 10) = Color.FromArgb(255, 255, 255) AndAlso bmp.GetPixel(100, 50).B > 80 AndAlso bmp.GetPixel(100, 50).R > 80,
                  $"{bmp.GetPixel(10, 10)} {bmp.GetPixel(100, 50)}")
        End Using
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of DrawLayer)(System.Text.Json.JsonSerializer.Serialize(layer))
        Check("漸層操作存得回來", back.Ops(0).Shape = DrawShape.Gradient AndAlso back.Ops(0).Gradient.X2 = 2 AndAlso back.Ops(0).Region IsNot Nothing)
        Dim cl = layer.Clone()
        cl.Ops(0).Gradient.X2 = 9
        Check("Clone 為深複製（漸層選項不共用）", layer.Ops(0).Gradient.X2 = 2)
    End Sub
End Module
