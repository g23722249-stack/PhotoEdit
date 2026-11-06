Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>黑白照片上色：色調判斷、合起來（亮度不變、強度、保留原色調、色溫、修復褪色）、模型（有下載時）。</summary>
Module ColorizerTests

    ''' <summary>左右漸層（亮度 40..215），整張乘上 tint（R、G、B 倍率）。</summary>
    Private Function Ramp(r As Double, g As Double, b As Double, Optional lo As Integer = 40, Optional hi As Integer = 215) As Bitmap
        Dim bmp As New Bitmap(120, 80, PixelFormat.Format24bppRgb)
        For y = 0 To 79
            For x = 0 To 119
                Dim l = lo + (hi - lo) * x / 119.0
                bmp.SetPixel(x, y, Color.FromArgb(CInt(Math.Min(255, l * r)), CInt(Math.Min(255, l * g)), CInt(Math.Min(255, l * b))))
            Next
        Next
        Return bmp
    End Function

    Private Function Lum(c As Color) As Double
        Return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B
    End Function

    Private Function Chroma(c As Color) As Integer
        Return Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B))
    End Function

    ''' <summary>模型解析度的假 ab：整張偏紅（a=30、b=20）。</summary>
    Private Function FakeAb() As OpenCvSharp.Mat
        Return New OpenCvSharp.Mat(32, 32, OpenCvSharp.MatType.CV_32FC2, New OpenCvSharp.Scalar(30, 20))
    End Function

    Sub ColorizerTestsRun()
        Console.WriteLine("黑白照片上色")
        Using gray = Ramp(1, 1, 1), sepia = Ramp(1.15, 1.0, 0.75), colorful As New Bitmap(120, 80)
            Using g = Graphics.FromImage(colorful)
                g.Clear(Color.FromArgb(200, 60, 60))
                g.FillRectangle(Brushes.SeaGreen, 0, 0, 60, 80)
                g.FillRectangle(Brushes.RoyalBlue, 60, 40, 60, 40)
            End Using
            Check("判斷：黑白、泛黃（單色調）、彩色",
                  Colorizer.Analyze(gray).Tone = PhotoTone.Gray AndAlso Colorizer.Analyze(sepia).Tone = PhotoTone.Toned AndAlso Colorizer.Analyze(colorful).Tone = PhotoTone.Color,
                  $"{Colorizer.Analyze(gray)} {Colorizer.Analyze(sepia)} {Colorizer.Analyze(colorful)}")

            Using ab = FakeAb()
                Dim plain As New ColorizeSettings With {.FixFading = False}
                Using res = Colorizer.Compose(gray, ab, plain)
                    Dim p = res.GetPixel(60, 40), q = gray.GetPixel(60, 40)
                    Check("合起來：有顏色（偏紅）、亮度和原圖差不多、大小一樣",
                          res.Width = gray.Width AndAlso p.R > p.B + 20 AndAlso Math.Abs(Lum(p) - Lum(q)) < 12, $"{q} → {p}")
                End Using
                Using none = Colorizer.Compose(gray, ab, New ColorizeSettings With {.Strength = 0, .FixFading = False})
                    Check("強度 0：還是黑白", Chroma(none.GetPixel(60, 40)) <= 3, none.GetPixel(60, 40).ToString())
                End Using
                Using strong = Colorizer.Compose(gray, ab, New ColorizeSettings With {.Strength = 200, .FixFading = False}),
                      normal = Colorizer.Compose(gray, ab, plain)
                    Check("強度 200：比 100 更鮮豔", Chroma(strong.GetPixel(60, 40)) > Chroma(normal.GetPixel(60, 40)) + 10)
                End Using
                Using kept = Colorizer.Compose(sepia, ab, New ColorizeSettings With {.KeepTone = 100, .FixFading = False})
                    Dim a = kept.GetPixel(60, 40), b = sepia.GetPixel(60, 40)
                    Check("保留原本色調 100：回到原本的泛黃", Math.Abs(CInt(a.R) - b.R) < 6 AndAlso Math.Abs(CInt(a.B) - b.B) < 6, $"{b} → {a}")
                End Using
                Using warm = Colorizer.Compose(gray, ab, New ColorizeSettings With {.Temperature = 100, .FixFading = False}),
                      cool = Colorizer.Compose(gray, ab, New ColorizeSettings With {.Temperature = -100, .FixFading = False})
                    Dim w = warm.GetPixel(60, 40), c = cool.GetPixel(60, 40)
                    Check("色溫：偏暖比偏冷黃（藍少）", CInt(w.B) < c.B - 10, $"{w} / {c}")
                End Using
                Using faded = Ramp(1, 1, 1, 90, 160), fixedImg = Colorizer.Compose(faded, Nothing, New ColorizeSettings With {.FixFading = True})
                    Check("修復褪色：灰灰的照片拉開成接近全黑到全白",
                          Lum(fixedImg.GetPixel(0, 40)) < 25 AndAlso Lum(fixedImg.GetPixel(119, 40)) > 230, $"{Lum(fixedImg.GetPixel(0, 40)):0} .. {Lum(fixedImg.GetPixel(119, 40)):0}")
                End Using
            End Using

            ' 模型（有下載時才測）
            If ColorizeModels.Available(ColorizeModel.DDColorTiny) Then
                Using ab = Colorizer.PredictAb(gray, ColorizeModel.DDColorTiny)
                    Check("模型（快速）：輸出 512×512 的 a、b", ab.Width = 512 AndAlso ab.Height = 512 AndAlso ab.Channels() = 2, $"{ab.Width}×{ab.Height}×{ab.Channels()}")
                End Using
            Else
                Console.WriteLine("  （略過模型：沒有 Models\ddcolor-tiny-fp16.onnx）")
            End If
        End Using
        Check("模型清單：三個、檔名不重複", ColorizeModels.Order.Length = 3 AndAlso ColorizeModels.Order.Select(Function(m) ColorizeModels.FileName(m)).Distinct().Count() = 3)
    End Sub
End Module
