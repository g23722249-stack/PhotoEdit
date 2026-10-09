Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>美顏（勻膚、去紅、美白、膚色、去油光、去痘、黑眼圈、大眼、牙齒、腮紅、修容）、個別臉設定、液化。</summary>
Module BeautyTests

    ' 臉在左半邊：臉框 x 60..160、y 40..160；兩眼 (88,76)(132,76)、鼻尖 (110,100)、嘴角 (92,130)(128,130)
    Private Function MakeFace(Optional offsetX As Single = 0) As FaceRegion
        Dim f As New FaceRegion With {.Box = New RectangleF(0.15F + offsetX, 0.2F, 0.25F, 0.6F)}
        f.Landmarks = {New PointF(0.22F + offsetX, 0.38F), New PointF(0.33F + offsetX, 0.38F), New PointF(0.275F + offsetX, 0.5F),
                       New PointF(0.23F + offsetX, 0.65F), New PointF(0.32F + offsetX, 0.65F)}
        Return f
    End Function

    ''' <summary>平滑膚色底（少量雜訊），paint 可以再畫上測試用的斑點。</summary>
    Private Function Skin(Optional paint As Action(Of Graphics) = Nothing, Optional noise As Integer = 3) As Bitmap
        Dim rnd As New Random(7)
        Dim bmp As New Bitmap(400, 200, PixelFormat.Format32bppArgb)
        For y = 0 To 199
            For x = 0 To 399
                Dim n = rnd.Next(-noise, noise + 1)
                bmp.SetPixel(x, y, Color.FromArgb(Clamp(205 + n), Clamp(160 + n), Clamp(135 + n)))
            Next
        Next
        If paint IsNot Nothing Then
            Using g = Graphics.FromImage(bmp)
                paint(g)
            End Using
        End If
        Return bmp
    End Function

    Private Function Run(src As Bitmap, b As BeautySettings, Optional faces As FaceRegion() = Nothing) As Bitmap
        Dim r As New EditRecipe()
        r.SetGlobalBeauty(b)
        Return If(PortraitRetouch.Apply(src, If(faces, {MakeFace()}), r), CType(src.Clone(), Bitmap))
    End Function

    Private Function Mean(bmp As Bitmap, cx As Integer, cy As Integer, r As Integer, ch As Func(Of Color, Double)) As Double
        Dim s = 0.0, n = 0
        For y = cy - r To cy + r
            For x = cx - r To cx + r
                s += ch(bmp.GetPixel(x, y)) : n += 1
            Next
        Next
        Return s / n
    End Function

    Private Function Luma(c As Color) As Double
        Return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B
    End Function

    Sub BeautyTestsRun()
        Console.WriteLine("美顏")
        ' 設定：全部臉共用的存取（磨皮、提亮、亮眼寫回原本的欄位）
        Dim rr As New EditRecipe()
        rr.SetGlobalBeauty(New BeautySettings With {.Smoothing = 30, .Whiten = 40})
        Check("共用美顏：磨皮寫回原欄位、其他存在 Beauty", rr.SkinSmoothing = 30 AndAlso rr.Beauty.Whiten = 40 AndAlso rr.Beauty.Smoothing = 0 AndAlso
              rr.GlobalBeauty().Smoothing = 30 AndAlso rr.HasPortrait)
        rr.SetGlobalBeauty(New BeautySettings())
        Check("全部歸零：沒有人像參數、Beauty 清掉", Not rr.HasPortrait AndAlso rr.Beauty Is Nothing AndAlso rr.Equals(New EditRecipe()))
        Dim json = System.Text.Json.JsonSerializer.Serialize(New EditRecipe With {.Beauty = New BeautySettings With {.Teeth = 30},
            .Liquify = New List(Of LiquifyStroke) From {New LiquifyStroke With {.Mode = LiquifyMode.Bloat, .Radius = 0.05, .Strength = 70}}})
        Dim back = System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(json)
        Check("美顏與液化存得回來", back.Beauty.Teeth = 30 AndAlso back.Liquify(0).Mode = LiquifyMode.Bloat AndAlso back.Liquify(0).Strength = 70 AndAlso back.HasSourceFix)

        Using src = Skin()
            Using out = Run(src, New BeautySettings With {.Whiten = 100})
                Check("美白：臉頰變亮", Mean(out, 80, 105, 6, AddressOf Luma) > Mean(src, 80, 105, 6, AddressOf Luma) + 8,
                      $"{Mean(src, 80, 105, 6, AddressOf Luma):0} → {Mean(out, 80, 105, 6, AddressOf Luma):0}")
                Check("美白：臉外不變", Mean(out, 330, 100, 8, AddressOf Luma) = Mean(src, 330, 100, 8, AddressOf Luma))
            End Using
            Using warm = Run(src, New BeautySettings With {.Tone = 100}), cool = Run(src, New BeautySettings With {.Tone = -100})
                Dim rb = Function(c As Color) CDbl(c.R) - c.B
                Check("膚色：正值偏暖（紅-藍變大）、負值偏冷", Mean(warm, 80, 105, 6, rb) > Mean(src, 80, 105, 6, rb) + 4 AndAlso
                      Mean(cool, 80, 105, 6, rb) < Mean(src, 80, 105, 6, rb) - 4)
            End Using
        End Using

        ' 去紅：臉頰上一塊泛紅
        Using src = Skin(Sub(g)
                             Using b As New SolidBrush(Color.FromArgb(225, 135, 125))
                                 g.FillEllipse(b, 70, 95, 22, 18)
                             End Using
                         End Sub)
            Using out = Run(src, New BeautySettings With {.Redness = 100})
                Dim red = Function(c As Color) CDbl(c.R) - c.G
                Check("去紅：泛紅的地方紅色變淡", Mean(out, 81, 104, 4, red) < Mean(src, 81, 104, 4, red) - 12,
                      $"{Mean(src, 81, 104, 4, red):0} → {Mean(out, 81, 104, 4, red):0}")
            End Using
        End Using

        ' 勻膚：大範圍的明暗不均（低頻）變平，細雜訊（高頻紋理）保留
        Using src = Skin(Sub(g)
                             Using b As New SolidBrush(Color.FromArgb(35, 120, 60, 40))
                                 g.FillEllipse(b, 76, 102, 8, 8)
                             End Using
                         End Sub, noise:=12)
            Using out = Run(src, New BeautySettings With {.Even = 100})
                Dim blotchBefore = Mean(src, 80, 106, 1, AddressOf Luma) - Mean(src, 96, 118, 3, AddressOf Luma)
                Dim blotchAfter = Mean(out, 80, 106, 1, AddressOf Luma) - Mean(out, 96, 118, 3, AddressOf Luma)
                Check("勻膚：色塊和旁邊的差距變小", Math.Abs(blotchAfter) < Math.Abs(blotchBefore) * 0.7, $"{blotchBefore:0.0} → {blotchAfter:0.0}")
                Check("勻膚：膚質紋理保留（雜訊不會被抹平）", StdDev(out, 120, 110, 6) > StdDev(src, 120, 110, 6) * 0.8,
                      $"{StdDev(src, 120, 110, 6):0.0} → {StdDev(out, 120, 110, 6):0.0}")
            End Using
        End Using

        ' 眉毛不算皮膚：美白、勻膚、磨皮、去痘都不會把眉毛抹淡
        Using src = Skin(Sub(g)
                             Using p As New Pen(Color.FromArgb(150, 110, 95), 3)
                                 g.DrawLine(p, 78, 60, 98, 60)
                                 g.DrawLine(p, 122, 60, 142, 60)
                             End Using
                         End Sub)
            Using out = Run(src, New BeautySettings With {.Whiten = 100, .Even = 100, .Smoothing = 100, .Blemish = 100})
                Check("眉毛：美白、勻膚、磨皮、去痘後眉毛不變淡", Math.Abs(Mean(out, 88, 60, 1, AddressOf Luma) - Mean(src, 88, 60, 1, AddressOf Luma)) < 6,
                      $"{Mean(src, 88, 60, 1, AddressOf Luma):0} → {Mean(out, 88, 60, 1, AddressOf Luma):0}")
            End Using
        End Using

        ' 去痘：臉頰幾顆小暗點
        Using src = Skin(Sub(g)
                             Using b As New SolidBrush(Color.FromArgb(160, 95, 80))
                                 g.FillEllipse(b, 76, 102, 4, 4)
                                 g.FillEllipse(b, 136, 106, 4, 4)
                             End Using
                         End Sub)
            Using out = Run(src, New BeautySettings With {.Blemish = 80})
                Check("去痘：小暗點補成周圍的膚色", Luma(out.GetPixel(78, 104)) > Luma(src.GetPixel(78, 104)) + 25 AndAlso
                      Luma(out.GetPixel(138, 108)) > Luma(src.GetPixel(138, 108)) + 25,
                      $"{Luma(src.GetPixel(78, 104)):0} → {Luma(out.GetPixel(78, 104)):0}")
            End Using
            Using out = Run(src, New BeautySettings With {.Blemish = 0, .Whiten = 1})
                Check("去痘 0：暗點不動", Math.Abs(Luma(out.GetPixel(78, 104)) - Luma(src.GetPixel(78, 104))) < 4)
            End Using
        End Using

        ' 黑眼圈：兩眼下方偏暗偏紫
        Using src = Skin(Sub(g)
                             Using b As New SolidBrush(Color.FromArgb(150, 115, 120))
                                 g.FillEllipse(b, 78, 84, 20, 8)
                                 g.FillEllipse(b, 122, 84, 20, 8)
                             End Using
                         End Sub)
            Using out = Run(src, New BeautySettings With {.DarkCircles = 100})
                Check("黑眼圈：眼下變亮", Mean(out, 88, 88, 2, AddressOf Luma) > Mean(src, 88, 88, 2, AddressOf Luma) + 10,
                      $"{Mean(src, 88, 88, 2, AddressOf Luma):0} → {Mean(out, 88, 88, 2, AddressOf Luma):0}")
            End Using
        End Using

        ' 去油光：臉頰一個亮點
        Using src = Skin(Sub(g)
                             Using b As New SolidBrush(Color.FromArgb(250, 240, 232))
                                 g.FillEllipse(b, 120, 100, 10, 8)
                             End Using
                         End Sub)
            Using out = Run(src, New BeautySettings With {.Shine = 100})
                Check("去油光：亮點變暗", Mean(out, 125, 104, 2, AddressOf Luma) < Mean(src, 125, 104, 2, AddressOf Luma) - 15,
                      $"{Mean(src, 125, 104, 2, AddressOf Luma):0} → {Mean(out, 125, 104, 2, AddressOf Luma):0}")
            End Using
        End Using

        ' 牙齒：兩嘴角之間偏黃的白色
        Using src = Skin(Sub(g)
                             Using b As New SolidBrush(Color.FromArgb(225, 210, 160))
                                 g.FillRectangle(b, 98, 127, 24, 7)
                             End Using
                         End Sub)
            Using out = Run(src, New BeautySettings With {.Teeth = 100})
                Dim yellow = Function(c As Color) (CDbl(c.R) + c.G) / 2 - c.B
                Check("牙齒美白：黃色變少", Mean(out, 110, 130, 2, yellow) < Mean(src, 110, 130, 2, yellow) - 12,
                      $"{Mean(src, 110, 130, 2, yellow):0} → {Mean(out, 110, 130, 2, yellow):0}")
            End Using
        End Using

        Using src = Skin()
            Using out = Run(src, New BeautySettings With {.Blush = 100})
                Dim pink = Function(c As Color) CDbl(c.R) - c.G
                Check("腮紅：臉頰變紅潤", Mean(out, 83, 103, 3, pink) > Mean(src, 83, 103, 3, pink) + 8,
                      $"{Mean(src, 83, 103, 3, pink):0} → {Mean(out, 83, 103, 3, pink):0}")
                Check("腮紅：額頭不變", Math.Abs(Mean(out, 110, 55, 3, pink) - Mean(src, 110, 55, 3, pink)) < 2)
            End Using
            Using out = Run(src, New BeautySettings With {.Contour = 100})
                Check("修容：鼻樑變亮", Mean(out, 110, 86, 2, AddressOf Luma) > Mean(src, 110, 86, 2, AddressOf Luma) + 4,
                      $"{Mean(src, 110, 86, 2, AddressOf Luma):0} → {Mean(out, 110, 86, 2, AddressOf Luma):0}")
            End Using
        End Using

        ' 大眼：眼睛是一個深色小圓，放大後半徑變大
        Using src = Skin(Sub(g)
                             g.FillEllipse(Brushes.Black, 84, 72, 8, 8)
                             g.FillEllipse(Brushes.Black, 128, 72, 8, 8)
                         End Sub)
            Using out = Run(src, New BeautySettings With {.EyeEnlarge = 100})
                Dim dark = Function(bmp As Bitmap) Enumerable.Range(70, 30).Sum(Function(x) Enumerable.Range(64, 24).Count(Function(y) Luma(bmp.GetPixel(x, y)) < 100))
                Check("大眼：眼睛變大", dark(out) > dark(src) * 1.2, $"{dark(src)} → {dark(out)}")
                Check("大眼：遠處不變", out.GetPixel(330, 100) = src.GetPixel(330, 100))
            End Using
        End Using

        ' 個別臉：右邊另一張臉單獨設定
        Using src = Skin()
            Dim left = MakeFace(), right = MakeFace(0.5F)
            Dim r As New EditRecipe()
            r.FaceBeauty = New List(Of BeautySettings) From {New BeautySettings With {.Whiten = 100, .FaceX = 0.775, .FaceY = 0.5}}
            Dim rightOnly As Double = 0
            Using out = PortraitRetouch.Apply(src, {left, right}, r)
                Check("個別臉：只有右邊那張臉美白", out IsNot Nothing AndAlso Mean(out, 280, 105, 6, AddressOf Luma) > Mean(src, 280, 105, 6, AddressOf Luma) + 8 AndAlso
                      Mean(out, 80, 105, 6, AddressOf Luma) = Mean(src, 80, 105, 6, AddressOf Luma))
                rightOnly = Mean(out, 280, 105, 6, AddressOf Luma)
            End Using
            r.SetGlobalBeauty(New BeautySettings With {.Tone = 100})
            Using out = PortraitRetouch.Apply(src, {left, right}, r)
                Dim rb = Function(c As Color) CDbl(c.R) - c.B
                Check("個別臉：共用設定只套到沒有個別設定的臉", Mean(out, 80, 105, 6, rb) > Mean(src, 80, 105, 6, rb) + 4 AndAlso
                      Math.Abs(Mean(out, 280, 105, 6, AddressOf Luma) - rightOnly) < 0.5)
            End Using
        End Using

        ' 一鍵美顏
        Check("一鍵美顏：19 組預設都有效果", BeautySettings.PresetNames.Length = 19 AndAlso Enumerable.Range(0, 19).All(Function(i) Not BeautySettings.Preset(i).IsEmpty))

        DenseTests()
        LightTests()
        LiquifyTests()
    End Sub

    ''' <summary>合成的 68 點（同 MakeFace 的臉）：下顎半圓、眉、鼻、眼、嘴唇。</summary>
    Private Function DenseFace() As FaceRegion
        Dim f = MakeFace()
        Dim pts As New List(Of PointF)()
        For i = 0 To 16
            Dim t = i / 16.0 * Math.PI
            pts.Add(New PointF(CSng(110 - 48 * Math.Cos(t)), CSng(80 + 78 * Math.Sin(t))))
        Next
        For i = 0 To 4 : pts.Add(New PointF(76 + i * 6, 64)) : Next
        For i = 0 To 4 : pts.Add(New PointF(120 + i * 6, 64)) : Next
        For i = 0 To 3 : pts.Add(New PointF(110, 74 + i * 8.6F)) : Next
        For i = 0 To 4 : pts.Add(New PointF(102 + i * 4, 104)) : Next
        For Each c In {New PointF(88, 76), New PointF(132, 76)}
            For i = 0 To 5
                Dim a = i / 6.0 * 2 * Math.PI
                pts.Add(New PointF(CSng(c.X - 7 * Math.Cos(a)), CSng(c.Y - 3 * Math.Sin(a))))
            Next
        Next
        For i = 0 To 11
            Dim a = i / 12.0 * 2 * Math.PI
            pts.Add(New PointF(CSng(110 - 18 * Math.Cos(a)), CSng(132 - 8 * Math.Sin(a))))
        Next
        For i = 0 To 7
            Dim a = i / 8.0 * 2 * Math.PI
            pts.Add(New PointF(CSng(110 - 12 * Math.Cos(a)), CSng(132 - 2.5 * Math.Sin(a))))
        Next
        f.Dense = pts.Select(Function(p) New PointF(p.X / 400, p.Y / 200)).ToArray()
        Return f
    End Function

    Private Sub DenseTests()
        Console.WriteLine("美顏（68 點）")
        Dim face = DenseFace()
        Check("合成特徵點 68 個", face.Dense.Length = 68)
        ' 唇色：嘴唇（外框內、內側外）換成唇色，嘴裡不動
        Using src = Skin(Sub(g)
                             Using b As New SolidBrush(Color.FromArgb(200, 140, 135))
                                 g.FillEllipse(b, 92, 124, 36, 16)
                             End Using
                             g.FillEllipse(Brushes.White, 98, 130, 24, 4)
                         End Sub)
            Using out = Run(src, New BeautySettings With {.Lips = 100, .LipColorArgb = Color.FromArgb(40, 60, 220).ToArgb()}, {face})
                Dim blue = Function(c As Color) CDbl(c.B) - c.R
                Check("唇色：嘴唇變成選的顏色（藍）", Mean(out, 110, 127, 1, blue) > Mean(src, 110, 127, 1, blue) + 25,
                      $"{Mean(src, 110, 127, 1, blue):0} → {Mean(out, 110, 127, 1, blue):0}")
                Check("唇色：臉頰不動", Mean(out, 80, 105, 4, AddressOf Luma) = Mean(src, 80, 105, 4, AddressOf Luma))
            End Using
            Using out = Run(src, New BeautySettings With {.Lips = 100}, {MakeFace()})
                Check("唇色：沒有 68 點時不上色", Mean(out, 110, 127, 1, AddressOf Luma) = Mean(src, 110, 127, 1, AddressOf Luma))
            End Using
        End Using
        ' 眉毛加深
        Using src = Skin(Sub(g)
                             Using p As New Pen(Color.FromArgb(150, 110, 90), 4)
                                 g.DrawLine(p, 76, 64, 100, 64)
                             End Using
                         End Sub)
            Using out = Run(src, New BeautySettings With {.Brows = 100}, {face})
                Check("眉毛：眉毛變深", Mean(out, 88, 64, 1, AddressOf Luma) < Mean(src, 88, 64, 1, AddressOf Luma) - 15,
                      $"{Mean(src, 88, 64, 1, AddressOf Luma):0} → {Mean(out, 88, 64, 1, AddressOf Luma):0}")
            End Using
        End Using
        ' 瘦臉：臉頰外緣一條直線往內移
        Using src = Skin(Sub(g)
                             g.FillRectangle(Brushes.Black, 0, 0, 68, 200)
                         End Sub)
            Using out = Run(src, New BeautySettings With {.FaceSlim = 100}, {face})
                ' 黑色在左邊：找最右邊的黑點
                Dim lastDark = Function(bmp As Bitmap, y As Integer)
                                   Dim r = -1
                                   For x = 40 To 110
                                       If Luma(bmp.GetPixel(x, y)) < 60 Then r = x
                                   Next
                                   Return r
                               End Function
                Check("瘦臉：臉頰外緣往內移", lastDark(out, 120) > lastDark(src, 120) + 1, $"{lastDark(src, 120)} → {lastDark(out, 120)}")
                Check("瘦臉：遠處不變", out.GetPixel(330, 100) = src.GetPixel(330, 100))
            End Using
        End Using
        ' 下巴：下巴下方一條橫線，拉長時往下移
        Using src = Skin(Sub(g)
                             g.FillRectangle(Brushes.Black, 95, 160, 30, 40)
                         End Sub)
            Dim topDark = Function(bmp As Bitmap)
                              For y = 140 To 199
                                  If Luma(bmp.GetPixel(110, y)) < 60 Then Return y
                              Next
                              Return -1
                          End Function
            Using longer = Run(src, New BeautySettings With {.Chin = 100}, {face}), shorter = Run(src, New BeautySettings With {.Chin = -100}, {face})
                Check("下巴：拉長往下、縮短往上", topDark(longer) > topDark(src) AndAlso topDark(shorter) < topDark(src),
                      $"{topDark(shorter)} / {topDark(src)} / {topDark(longer)}")
            End Using
        End Using
        ' 妝容：眼影（上眼皮上方換色）、眼線（上眼皮變深）、臥蠶（下眼皮下方變亮）
        Using src = Skin()
            Using out = Run(src, New BeautySettings With {.EyeShadow = 100, .EyeShadowColorArgb = Color.FromArgb(60, 90, 220).ToArgb()}, {face})
                Dim blue = Function(c As Color) CDbl(c.B) - c.R
                Check("眼影：上眼皮上方變成選的顏色", Mean(out, 88, 70, 1, blue) > Mean(src, 88, 70, 1, blue) + 15,
                      $"{Mean(src, 88, 70, 1, blue):0} → {Mean(out, 88, 70, 1, blue):0}")
                Check("眼影：臉頰不動", Mean(out, 80, 110, 3, AddressOf Luma) = Mean(src, 80, 110, 3, AddressOf Luma))
            End Using
            Using out = Run(src, New BeautySettings With {.EyeLiner = 100}, {face})
                Check("眼線：上眼皮那條線變深", Mean(out, 88, 73, 0, AddressOf Luma) < Mean(src, 88, 73, 0, AddressOf Luma) - 30,
                      $"{Mean(src, 88, 73, 0, AddressOf Luma):0} → {Mean(out, 88, 73, 0, AddressOf Luma):0}")
            End Using
            Using out = Run(src, New BeautySettings With {.EyeBag = 100}, {face})
                Check("臥蠶：下眼皮下方變亮", Mean(out, 88, 81, 0, AddressOf Luma) > Mean(src, 88, 81, 0, AddressOf Luma) + 3,
                      $"{Mean(src, 88, 81, 0, AddressOf Luma):0} → {Mean(out, 88, 81, 0, AddressOf Luma):0}")
            End Using
            Using out = Run(src, New BeautySettings With {.EyeShadow = 100, .EyeLiner = 100, .EyeBag = 100}, {MakeFace()})
                Check("妝容：沒有 68 點時不作用", Mean(out, 88, 70, 2, AddressOf Luma) = Mean(src, 88, 70, 2, AddressOf Luma))
            End Using
        End Using
    End Sub

    Private Sub LightTests()
        Console.WriteLine("光影、強度")
        Using src = Skin()
            Using remb = Run(src, New BeautySettings With {.LightKind = BeautyLight.Rembrandt, .Light = 100, .LightFromRight = True})
                Check("林布蘭光（光從右）：左臉變暗、右臉不變暗", Mean(remb, 75, 100, 3, AddressOf Luma) < Mean(src, 75, 100, 3, AddressOf Luma) - 15 AndAlso
                      Mean(remb, 145, 100, 3, AddressOf Luma) >= Mean(src, 145, 100, 3, AddressOf Luma) - 1,
                      $"{Mean(src, 75, 100, 3, AddressOf Luma):0} → {Mean(remb, 75, 100, 3, AddressOf Luma):0}")
            End Using
            Using side = Run(src, New BeautySettings With {.LightKind = BeautyLight.Side, .Light = 100})
                Check("側光（光從左）：右臉變暗", Mean(side, 145, 100, 3, AddressOf Luma) < Mean(src, 145, 100, 3, AddressOf Luma) - 20)
            End Using
            Using soft = Run(src, New BeautySettings With {.LightKind = BeautyLight.Soft, .Light = 100})
                Check("柔光：臉變亮", Mean(soft, 110, 110, 4, AddressOf Luma) > Mean(src, 110, 110, 4, AddressOf Luma) + 5)
            End Using
            Using none = Run(src, New BeautySettings With {.LightKind = BeautyLight.Rembrandt, .Light = 0, .Whiten = 1})
                Check("光影強度 0：不打光", Math.Abs(Mean(none, 75, 100, 3, AddressOf Luma) - Mean(src, 75, 100, 3, AddressOf Luma)) < 2)
            End Using
        End Using
        Dim p = BeautySettings.Preset(1, 50)
        Check("強度 50%：數值減半、記下強度與第幾組", Math.Abs(p.Whiten - 12.5) < 1 AndAlso Math.Abs(p.Smoothing - 12.5) < 1 AndAlso p.PresetStrength = 50 AndAlso p.PresetIndex.GetValueOrDefault(-1) = 1)
        Dim p2 = BeautySettings.Preset(15, 0)
        Check("強度 0%：沒有效果，但顏色還在", p2.IsEmpty AndAlso p2.LipColorArgb <> 0)
        Dim r As New EditRecipe()
        r.SetGlobalBeauty(BeautySettings.Preset(16, 100))
        Check("光影預設存得回來", System.Text.Json.JsonSerializer.Deserialize(Of EditRecipe)(System.Text.Json.JsonSerializer.Serialize(r)).GlobalBeauty().LightKind = BeautyLight.Soft)
        r.SetGlobalBeauty(New BeautySettings())
        Check("清除：配方回到空白", r.Equals(New EditRecipe()))
    End Sub

    Private Sub LiquifyTests()
        Console.WriteLine("液化")
        Using src As New Bitmap(200, 200, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(src)
                g.Clear(Color.White)
                g.FillEllipse(Brushes.Black, 90, 90, 20, 20)
            End Using
            Dim dark = Function(bmp As Bitmap) Enumerable.Range(60, 80).Sum(Function(x) Enumerable.Range(60, 80).Count(Function(y) bmp.GetPixel(x, y).R < 128))
            Dim stroke = Function(mode As LiquifyMode, pts As PointF()) As LiquifyStroke
                             Dim s As New LiquifyStroke With {.Mode = mode, .Radius = 0.15, .Strength = 80}
                             For Each p In pts
                                 s.AddPoint(p)
                             Next
                             Return s
                         End Function
            Using big = Liquify.Apply(src, {stroke(LiquifyMode.Bloat, {New PointF(0.5F, 0.5F)})}),
                  small = Liquify.Apply(src, {stroke(LiquifyMode.Pucker, {New PointF(0.5F, 0.5F)})})
                Check("膨脹：圓變大；縮攏：圓變小", dark(big) > dark(src) * 1.15 AndAlso dark(small) < dark(src) * 0.87, $"{dark(src)} → {dark(big)} / {dark(small)}")
                Check("液化：筆刷外不變", big.GetPixel(10, 10) = src.GetPixel(10, 10) AndAlso big.GetPixel(190, 100) = src.GetPixel(190, 100))
            End Using
            Using pushed = Liquify.Apply(src, {stroke(LiquifyMode.Push, {New PointF(0.5F, 0.5F), New PointF(0.6F, 0.5F)})})
                Dim cx = Function(bmp As Bitmap)
                             Dim sx = 0.0, n = 0
                             For x = 60 To 160
                                 For y = 60 To 140
                                     If bmp.GetPixel(x, y).R < 128 Then sx += x : n += 1
                                 Next
                             Next
                             Return sx / Math.Max(1, n)
                         End Function
                Check("推移：圓往拖曳方向（右）移動", cx(pushed) > cx(src) + 3, $"{cx(src):0.0} → {cx(pushed):0.0}")
            End Using
            Using twirl = Liquify.Apply(src, {stroke(LiquifyMode.TwirlClockwise, {New PointF(0.5F, 0.5F)})})
                Check("旋轉：圓心不動、外面不變", twirl.GetPixel(100, 100).R < 128 AndAlso twirl.GetPixel(10, 10) = src.GetPixel(10, 10))
            End Using
        End Using
    End Sub
End Module
