Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>像素圖放大：最近鄰、Scale2x／3x、HQx、xBR。</summary>
Module PixelScalerTests

    Private Function Grid(w As Integer, h As Integer, f As Func(Of Integer, Integer, Color)) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        For y = 0 To h - 1
            For x = 0 To w - 1
                b.SetPixel(x, y, f(x, y))
            Next
        Next
        Return b
    End Function

    Private Function Colors(b As Bitmap) As HashSet(Of Integer)
        Dim s As New HashSet(Of Integer)()
        For y = 0 To b.Height - 1
            For x = 0 To b.Width - 1
                s.Add(b.GetPixel(x, y).ToArgb())
            Next
        Next
        Return s
    End Function

    Sub PixelScalerTestsRun()
        Console.WriteLine("— 像素圖放大 —")
        Dim red = Color.FromArgb(220, 30, 30), blue = Color.FromArgb(30, 60, 220), white = Color.White
        ' 45° 的實心三角形（左下紅、右上藍）
        Using tri = Grid(16, 16, Function(x, y) If(x <= y, red, blue))
            Dim sizesOk = True
            For Each a In [Enum].GetValues(GetType(PixelScaleAlgorithm)).Cast(Of PixelScaleAlgorithm)()
                For Each n In PixelScaler.Factors(a)
                    Using o = PixelScaler.Upscale(tri, a, n)
                        If o.Width <> 16 * n OrElse o.Height <> 16 * n Then sizesOk = False
                    End Using
                Next
            Next
            Check("每種演算法、每個倍數：輸出是原圖的 N 倍大", sizesOk)
            Using o = PixelScaler.Upscale(tri, PixelScaleAlgorithm.Nearest, 3)
                Check("最近鄰：每個像素變成 3×3 的同色方塊", o.GetPixel(5, 2).ToArgb() = blue.ToArgb() AndAlso o.GetPixel(3, 4).ToArgb() = red.ToArgb() AndAlso Colors(o).Count = 2)
            End Using
            For Each n In {2, 3, 4}
                Using o = PixelScaler.Upscale(tri, PixelScaleAlgorithm.Scale2x, n)
                    If Not Colors(o).IsSubsetOf({red.ToArgb(), blue.ToArgb()}) Then sizesOk = False
                End Using
            Next
            Check("Scale2x／3x／4x：不產生新顏色", sizesOk)
            Using s2 = PixelScaler.Upscale(tri, PixelScaleAlgorithm.Scale2x, 2), nn = PixelScaler.Upscale(tri, PixelScaleAlgorithm.Nearest, 2)
                Dim diffs = 0
                For y = 0 To 31
                    For x = 0 To 31
                        If s2.GetPixel(x, y) <> nn.GetPixel(x, y) Then diffs += 1
                    Next
                Next
                Check("Scale2x：斜邊上的鋸齒被補平（和最近鄰不同的地方在斜邊上）", diffs > 10, $"{diffs} 格不同")
            End Using
            For Each a In {PixelScaleAlgorithm.HQx, PixelScaleAlgorithm.XBR}
                Using o = PixelScaler.Upscale(tri, a, 4)
                    Dim mixed = Colors(o).Count - 2
                    ' 斜邊附近有混色；遠離斜邊的地方純色
                    Check($"{a}：45° 斜邊有混色（不再是階梯），遠離斜邊仍是純色", mixed >= 2 AndAlso o.GetPixel(60, 2).ToArgb() = blue.ToArgb() AndAlso o.GetPixel(2, 60).ToArgb() = red.ToArgb(), $"{mixed} 種混色")
                End Using
            Next
        End Using

        ' 直的、橫的邊：不混色
        Using half = Grid(12, 12, Function(x, y) If(x < 6 OrElse y < 3, red, blue))
            For Each a In {PixelScaleAlgorithm.HQx, PixelScaleAlgorithm.XBR}
                Using o = PixelScaler.Upscale(half, a, 4)
                    Dim edgeOk = Enumerable.Range(16, 30).All(Function(y) o.GetPixel(23, y).ToArgb() = red.ToArgb() AndAlso o.GetPixel(24, y).ToArgb() = blue.ToArgb())
                    Check($"{a}：直的邊放大後仍然銳利（不混色）", edgeOk)
                End Using
            Next
        End Using

        ' 一像素寬的斜線：放大後仍連續
        Using line = Grid(20, 20, Function(x, y) If(x = y, white, blue))
            For Each a In {PixelScaleAlgorithm.HQx, PixelScaleAlgorithm.XBR}
                Using o = PixelScaler.Upscale(line, a, 4)
                    ' 沿著斜線每一點都偏白（中心線上）
                    Dim gaps = Enumerable.Range(6, 68).Count(Function(t) o.GetPixel(t, t).R < 150)
                    Check($"{a}：一像素寬的斜線放大後連續", gaps = 0, $"{gaps} 個斷點")
                End Using
            Next
        End Using

        ' 透明背景的圖示
        Using sprite = Grid(16, 16, Function(x, y) If((x - 8) ^ 2 + (y - 8) ^ 2 < 25, red, Color.FromArgb(0, 0, 0, 0)))
            For Each a In {PixelScaleAlgorithm.HQx, PixelScaleAlgorithm.XBR}
                Using o = PixelScaler.Upscale(sprite, a, 4)
                    Dim edge = o.GetPixel(32 + 18, 32) ' 圓的右緣附近
                    Check($"{a}：透明背景保持透明、圓的中間不透明、邊緣不混到底色", o.GetPixel(2, 2).A = 0 AndAlso o.GetPixel(32, 32).A = 255 AndAlso
                          (edge.A = 0 OrElse (edge.R > 150 AndAlso edge.G < 80)), $"{edge}")
                End Using
            Next
        End Using

        Dim threw = False
        Try
            Using big As New Bitmap(6000, 6000)
                PixelScaler.Upscale(big, PixelScaleAlgorithm.XBR, 4).Dispose()
            End Using
        Catch ex As ArgumentException
            threw = True
        End Try
        Check("放大後超過 1 億像素：擋下來", threw)
    End Sub
End Module
