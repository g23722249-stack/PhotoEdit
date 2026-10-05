Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks

''' <summary>
''' 依配方算出結果圖。順序：修補/降噪/人像（prepare）→ 旋轉/翻轉 → 透視 → 拉直 → 裁切 → 色調 → 暗角/顆粒 → 銳利化。
''' 一律回傳新的 32bppArgb 點陣圖，來源圖不會被修改。
''' </summary>
Public NotInheritable Class ImagePipeline
    Private Sub New()
    End Sub

    ''' <summary>
    ''' 完整算圖。maxDimension &gt; 0 時先把來源縮到長邊不超過此值（預覽用）。
    ''' prepare 會拿到縮放後、尚未轉向的來源複本（修補、降噪、人像），回傳新圖或 Nothing（不變）。
    ''' faces 供背景模糊判斷主體（已轉正原圖的 0..1 座標）。
    ''' 後段順序：色調 → 局部調整 → 背景模糊/移軸 → 暗角/顆粒 → 銳利化 → 文字貼圖 → 繪圖圖層 → 裁切形狀 → 邊框。
    ''' </summary>
    Public Shared Function Render(source As Bitmap, recipe As EditRecipe, Optional maxDimension As Integer = 0,
                                  Optional prepare As Func(Of Bitmap, Bitmap) = Nothing,
                                  Optional faces As IReadOnlyList(Of FaceRegion) = Nothing) As Bitmap
        Dim bmp = CopyScaled(source, maxDimension)
        Dim sw = bmp.Width, sh = bmp.Height
        If prepare IsNot Nothing Then
            Dim prepared = prepare(bmp)
            If prepared IsNot Nothing AndAlso prepared IsNot bmp Then
                bmp.Dispose()
                bmp = prepared
            End If
        End If
        bmp = ApplyGeometry(bmp, recipe, applyCrop:=True)
        ApplyTone(bmp, recipe)
        Creative.ApplyLocal(bmp, recipe, sw, sh)
        Creative.ApplyBlurs(bmp, recipe, faces, sw, sh)
        ApplyEffects(bmp, recipe)
        If recipe.Sharpness > 0 Then Sharpen(bmp, recipe.Sharpness / 100.0)
        Creative.DrawOverlays(bmp, recipe)
        DrawingRenderer.DrawLayers(bmp, recipe)
        ApplyCropShape(bmp, recipe.CropShape)
        Dim framed = Creative.ApplyFrame(bmp, recipe)
        If framed IsNot Nothing Then
            bmp.Dispose()
            bmp = framed
        End If
        Return bmp
    End Function

    ''' <summary>矩形以外的裁切形狀：形狀外變透明（反鋸齒）。</summary>
    Public Shared Sub ApplyCropShape(bmp As Bitmap, shape As CropShape)
        If shape = CropShape.Rectangle Then Return
        Dim w = bmp.Width, h = bmp.Height
        Dim mask(w * h - 1) As Byte
        Using m As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(m), path = CropShapePath(shape, New RectangleF(0, 0, w, h))
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
                g.FillPath(Brushes.White, path)
            End Using
            Dim mp = Perspective.ReadPixels(m)
            For i = 0 To mask.Length - 1
                mask(i) = mp(i * 4 + 3)
            Next
        End Using
        Dim px = Perspective.ReadPixels(bmp)
        For i = 0 To mask.Length - 1
            px(i * 4 + 3) = CByte(CInt(px(i * 4 + 3)) * mask(i) \ 255)
        Next
        Perspective.WritePixels(bmp, px)
    End Sub

    ''' <summary>裁切形狀填滿 rect 的路徑（畫布預覽與算圖共用）：圓形為內切橢圓、圓角半徑為短邊 12%、愛心與五角星撐滿。</summary>
    Public Shared Function CropShapePath(shape As CropShape, rect As RectangleF) As Drawing2D.GraphicsPath
        Dim path As New Drawing2D.GraphicsPath()
        Dim kind As DrawShape
        Select Case shape
            Case CropShape.Ellipse : kind = DrawShape.Ellipse
            Case CropShape.RoundRect : kind = DrawShape.RoundRect
            Case CropShape.Heart : kind = DrawShape.Heart
            Case CropShape.Star : kind = DrawShape.Star5
            Case Else
                path.AddRectangle(rect)
                Return path
        End Select
        Dim layer As New DrawLayer With {.Shape = kind, .X = rect.X + rect.Width / 2, .Y = rect.Y + rect.Height / 2, .W = rect.Width, .H = rect.Height}
        DrawGeometry.ApplyDefaults(layer)
        If kind = DrawShape.RoundRect Then layer.Param1 = 0.12
        If kind = DrawShape.Star5 Then layer.Param1 = 0.5
        For Each f In DrawGeometry.Figures(layer)
            path.AddPolygon(f.Points)
        Next
        Return path
    End Function

    ''' <summary>只做幾何（裁切模式要看未裁切的畫面時 applyCrop:=False）。</summary>
    Public Shared Function RenderGeometry(source As Bitmap, recipe As EditRecipe, maxDimension As Integer, applyCrop As Boolean) As Bitmap
        Return ApplyGeometry(CopyScaled(source, maxDimension), recipe, applyCrop)
    End Function

    ''' <summary>對 bmp 套用幾何並回傳結果；bmp 交給本函式處理（不是原物件時已被釋放）。</summary>
    Private Shared Function ApplyGeometry(bmp As Bitmap, recipe As EditRecipe, applyCrop As Boolean) As Bitmap
        Dim rft = ToRotateFlipType(recipe.Rotation, recipe.FlipHorizontal, recipe.FlipVertical)
        If rft <> RotateFlipType.RotateNoneFlipNone Then bmp.RotateFlip(rft)

        Dim warped = Perspective.Apply(bmp, recipe.PerspectiveVertical, recipe.PerspectiveHorizontal)
        If warped IsNot Nothing Then
            bmp.Dispose()
            bmp = warped
        End If

        If Math.Abs(recipe.Straighten) > 0.001 Then
            Dim straightened = StraightenImage(bmp, recipe.Straighten)
            bmp.Dispose()
            bmp = straightened
        End If

        If applyCrop AndAlso recipe.Crop IsNot Nothing AndAlso Not recipe.Crop.IsFull Then
            Dim cropped = CropImage(bmp, recipe.Crop.Normalized())
            bmp.Dispose()
            bmp = cropped
        End If
        Return bmp
    End Function

    ''' <summary>先旋轉再左右翻轉；上下翻轉 = 轉 180 度 + 左右翻轉。</summary>
    Public Shared Function ToRotateFlipType(rotation As Integer, flipH As Boolean, flipV As Boolean) As RotateFlipType
        Dim rot = EditRecipe.NormalizeAngle(rotation + If(flipV, 180, 0))
        Dim flipX = flipH Xor flipV
        Return CType(rot \ 90 + If(flipX, 4, 0), RotateFlipType)
    End Function

    Public Shared Function CopyScaled(source As Bitmap, maxDimension As Integer) As Bitmap
        Dim w = source.Width, h = source.Height
        If maxDimension > 0 AndAlso Math.Max(w, h) > maxDimension Then
            Dim s = maxDimension / Math.Max(w, h)
            w = Math.Max(1, CInt(Math.Round(w * s)))
            h = Math.Max(1, CInt(Math.Round(h * s)))
        End If
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.CompositingMode = CompositingMode.SourceCopy
            Using ia As New ImageAttributes()
                ia.SetWrapMode(WrapMode.TileFlipXY) ' 避免縮圖時邊緣出現半透明灰邊
                g.DrawImage(source, New Rectangle(0, 0, w, h), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, ia)
            End Using
        End Using
        Return bmp
    End Function

    ''' <summary>拉直所需的放大倍率：旋轉後放大到四角不露出空白，輸出尺寸與原圖相同。</summary>
    Public Shared Function StraightenScale(width As Integer, height As Integer, degrees As Double) As Double
        Dim a = Math.Abs(degrees) * Math.PI / 180
        Dim c = Math.Cos(a), s = Math.Sin(a)
        Return Math.Max(c + s * height / width, c + s * width / height)
    End Function

    Private Shared Function StraightenImage(bmp As Bitmap, degrees As Double) As Bitmap
        Dim w = bmp.Width, h = bmp.Height
        Dim scale = StraightenScale(w, h, degrees)
        Dim result As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(result)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.SmoothingMode = SmoothingMode.HighQuality
            g.TranslateTransform(w / 2.0F, h / 2.0F)
            g.RotateTransform(CSng(degrees))
            g.ScaleTransform(CSng(scale), CSng(scale))
            Using ia As New ImageAttributes()
                ia.SetWrapMode(WrapMode.TileFlipXY)
                g.DrawImage(bmp, New Rectangle(-w \ 2, -h \ 2, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia)
            End Using
        End Using
        Return result
    End Function

    Private Shared Function CropImage(bmp As Bitmap, crop As CropRect) As Bitmap
        Dim x = CInt(Math.Round(crop.X * bmp.Width))
        Dim y = CInt(Math.Round(crop.Y * bmp.Height))
        Dim w = Math.Max(1, Math.Min(bmp.Width - x, CInt(Math.Round(crop.Width * bmp.Width))))
        Dim h = Math.Max(1, Math.Min(bmp.Height - y, CInt(Math.Round(crop.Height * bmp.Height))))
        Return bmp.Clone(New Rectangle(x, y, w, h), PixelFormat.Format32bppArgb)
    End Function

    '=====================================================================
    ' 色調
    '=====================================================================

    ''' <summary>
    ''' 每個色版先做白平衡與曝光，再過色調曲線（對比、亮部、暗部、褪色），合成一張 LUT；
    ''' 飽和度與色彩濾鏡需要三色版一起算，所以在 LUT 之後逐點處理。
    ''' </summary>
    Public Shared Sub ApplyTone(bmp As Bitmap, recipe As EditRecipe)
        If Not recipe.HasTone Then Return

        Dim t = recipe.Temperature / 100.0 * 0.3
        Dim m = recipe.Tint / 100.0 * 0.2
        Dim ev = Math.Pow(2, recipe.Exposure)
        Dim lutR = BuildLut(recipe, (1 + t) * ev)
        Dim lutG = BuildLut(recipe, (1 - m) * ev)
        Dim lutB = BuildLut(recipe, (1 - t) * ev)
        Dim sat = 1 + recipe.Saturation / 100.0

        ' 色彩濾鏡：保持亮度、把顏色往指定色相拉。
        Dim toning = recipe.ToningStrength / 100.0 * 0.6
        Dim tone = HueToRgb(recipe.ToningHue)
        Dim toneLum = 0.299 * tone.R + 0.587 * tone.G + 0.114 * tone.B

        ProcessPixels(bmp,
            Sub(px As Byte(), rowStart As Integer, width As Integer, y As Integer)
                For i = rowStart To rowStart + width * 4 - 1 Step 4
                    Dim b As Double = lutB(px(i))
                    Dim g As Double = lutG(px(i + 1))
                    Dim r As Double = lutR(px(i + 2))
                    If sat <> 1 Then
                        Dim lum = 0.299 * r + 0.587 * g + 0.114 * b
                        r = lum + (r - lum) * sat
                        g = lum + (g - lum) * sat
                        b = lum + (b - lum) * sat
                    End If
                    If toning > 0 Then
                        Dim lum = Math.Max(0, 0.299 * r + 0.587 * g + 0.114 * b)
                        Dim k = lum / toneLum
                        r += (tone.R * k - r) * toning
                        g += (tone.G * k - g) * toning
                        b += (tone.B * k - b) * toning
                    End If
                    px(i) = ClampByte(b)
                    px(i + 1) = ClampByte(g)
                    px(i + 2) = ClampByte(r)
                Next
            End Sub)
    End Sub

    Private Shared Function BuildLut(recipe As EditRecipe, gain As Double) As Byte()
        Dim lut(255) As Byte
        Dim c = recipe.Contrast / 100.0
        Dim hl = recipe.Highlights / 100.0
        Dim sh = recipe.Shadows / 100.0
        Dim fade = recipe.Fade / 100.0
        For v = 0 To 255
            Dim x = Math.Min(1.0, v / 255.0 * gain)
            ' 對比：以 0.5 為中心拉伸；負值壓平。
            x = 0.5 + (x - 0.5) * If(c >= 0, 1 + c * 1.5, 1 + c * 0.7)
            x = Math.Max(0, Math.Min(1, x))
            ' 暗部：只作用在暗處，0 附近漸弱以免黑色變灰。
            Dim shadowW = (1 - x) * (1 - x) * Math.Min(1.0, x * 8)
            ' 亮部：只作用在亮處，1 附近漸弱以免白色變灰。
            Dim highlightW = x * x * Math.Min(1.0, (1 - x) * 8)
            x += sh * 0.35 * shadowW + hl * 0.35 * highlightW
            ' 褪色：黑色提亮到 fade*0.22，白色壓到 1 - fade*0.06。
            x = fade * 0.22 + x * (1 - fade * 0.28)
            lut(v) = ClampByte(x * 255)
        Next
        Return lut
    End Function

    ''' <summary>色相（度）轉成飽和、中亮度的 RGB（0..255）。</summary>
    Public Shared Function HueToRgb(hue As Integer) As (R As Double, G As Double, B As Double)
        Dim h = (((hue Mod 360) + 360) Mod 360) / 60.0
        Dim x = 1 - Math.Abs(h Mod 2 - 1)
        Dim r, g, b As Double
        Select Case CInt(Math.Floor(h))
            Case 0 : r = 1 : g = x : b = 0
            Case 1 : r = x : g = 1 : b = 0
            Case 2 : r = 0 : g = 1 : b = x
            Case 3 : r = 0 : g = x : b = 1
            Case 4 : r = x : g = 0 : b = 1
            Case Else : r = 1 : g = 0 : b = x
        End Select
        ' 混一半白色，避免純色太刺眼。
        Return ((r + 1) / 2 * 255, (g + 1) / 2 * 255, (b + 1) / 2 * 255)
    End Function

    '=====================================================================
    ' 暗角與顆粒（和位置有關，裁切後才套用）
    '=====================================================================

    Public Shared Sub ApplyEffects(bmp As Bitmap, recipe As EditRecipe)
        If Not recipe.HasEffects Then Return
        Dim w = bmp.Width, h = bmp.Height
        Dim vig = recipe.Vignette / 100.0
        Dim grain = recipe.Grain / 100.0 * 22

        ' 暗角權重：以畫面中心為 0、四角約為 1 的橢圓距離，0.45 以內不作用。
        Dim dx2(w - 1) As Double, dy2(h - 1) As Double
        For x = 0 To w - 1
            Dim d = (x + 0.5 - w / 2.0) / (w / 2.0)
            dx2(x) = d * d / 2
        Next
        For y = 0 To h - 1
            Dim d = (y + 0.5 - h / 2.0) / (h / 2.0)
            dy2(y) = d * d / 2
        Next
        ' 顆粒大小隨影像尺寸放大，預覽和匯出看起來一樣粗細。
        Dim cell = Math.Max(1, CInt(Math.Round(Math.Max(w, h) / 1600.0)))

        ProcessPixels(bmp,
            Sub(px As Byte(), rowStart As Integer, width As Integer, y As Integer)
                For x = 0 To width - 1
                    Dim i = rowStart + x * 4
                    Dim b As Double = px(i), g As Double = px(i + 1), r As Double = px(i + 2)
                    If vig <> 0 Then
                        Dim d = Math.Sqrt(dx2(x) + dy2(y))
                        Dim t = Math.Max(0, Math.Min(1, (d - 0.45) / 0.55))
                        Dim weight = t * t * (3 - 2 * t) ' smoothstep
                        If vig < 0 Then
                            Dim f = 1 + vig * 0.85 * weight
                            r *= f : g *= f : b *= f
                        Else
                            Dim f = vig * 0.85 * weight
                            r += (255 - r) * f : g += (255 - g) * f : b += (255 - b) * f
                        End If
                    End If
                    If grain > 0 Then
                        Dim n = Noise(x \ cell, y \ cell) * grain
                        r += n : g += n : b += n
                    End If
                    px(i) = ClampByte(b)
                    px(i + 1) = ClampByte(g)
                    px(i + 2) = ClampByte(r)
                Next
            End Sub)
    End Sub

    ''' <summary>固定的偽隨機雜訊（-1..1），同一位置每次結果相同，預覽不會閃動。</summary>
    Private Shared Function Noise(x As Integer, y As Integer) As Double
        ' 用 ULong 運算並自行截到 32 位元（VB 預設會檢查整數溢位）。
        Const Mask32 As ULong = &HFFFFFFFFUL
        Dim n = (CULng(x And &H7FFFFFFF) * 374761393UL + CULng(y And &H7FFFFFFF) * 668265263UL) And Mask32
        n = ((n Xor (n >> 13)) * 1274126177UL) And Mask32
        n = n Xor (n >> 16)
        Return (n And &HFFFFUL) / 32767.5 - 1
    End Function

    '=====================================================================
    ' 銳利化（3x3 平均模糊的反遮罩）
    '=====================================================================

    Public Shared Sub Sharpen(bmp As Bitmap, amount As Double)
        If amount <= 0 Then Return
        Dim k = amount * 1.5
        Dim w = bmp.Width, h = bmp.Height
        If w < 3 OrElse h < 3 Then Return

        Dim rect As New Rectangle(0, 0, w, h)
        Dim data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
        Try
            Dim stride = data.Stride
            Dim src(stride * h - 1) As Byte
            Marshal.Copy(data.Scan0, src, 0, src.Length)
            Dim dst = DirectCast(src.Clone(), Byte())
            Parallel.For(1, h - 1,
                Sub(y)
                    For x = 1 To w - 2
                        Dim i = y * stride + x * 4
                        For ch = 0 To 2
                            Dim sum = 0
                            For dy = -1 To 1
                                Dim rowI = i + dy * stride + ch
                                sum += CInt(src(rowI - 4)) + src(rowI) + src(rowI + 4)
                            Next
                            Dim orig As Double = src(i + ch)
                            dst(i + ch) = ClampByte(orig + k * (orig - sum / 9.0))
                        Next
                    Next
                End Sub)
            Marshal.Copy(dst, 0, data.Scan0, dst.Length)
        Finally
            bmp.UnlockBits(data)
        End Try
    End Sub

    '=====================================================================
    ' 共用
    '=====================================================================

    Public Delegate Sub RowProcessor(px As Byte(), rowStart As Integer, width As Integer, y As Integer)

    ''' <summary>把 32bppArgb 像素讀成 BGRA 位元組陣列，逐列平行處理後寫回。</summary>
    Public Shared Sub ProcessPixels(bmp As Bitmap, processRow As RowProcessor)
        Dim rect As New Rectangle(0, 0, bmp.Width, bmp.Height)
        Dim data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
        Try
            Dim stride = data.Stride
            Dim px(stride * bmp.Height - 1) As Byte
            Marshal.Copy(data.Scan0, px, 0, px.Length)
            Dim w = bmp.Width
            Parallel.For(0, bmp.Height, Sub(y) processRow(px, y * stride, w, y))
            Marshal.Copy(px, 0, data.Scan0, px.Length)
        Finally
            bmp.UnlockBits(data)
        End Try
    End Sub

    Public Shared Function ClampByte(v As Double) As Byte
        If v <= 0 Then Return 0
        If v >= 255 Then Return 255
        Return CByte(Math.Floor(v + 0.5)) ' 四捨五入，避免 CByte 的銀行家捨入
    End Function
End Class
