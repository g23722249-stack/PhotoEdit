''' <summary>區域色彩統計（sRGB 0..1）。</summary>
Public NotInheritable Class ColorStats
    Public Property MeanLuma As Double
    Public Property StdLuma As Double
    Public Property MeanSaturation As Double
    ''' <summary>亮部（亮度前 30%）的 R − B，代表色溫偏暖程度。</summary>
    Public Property Warmth As Double
    ''' <summary>亮部的 G −（R+B）/2，代表色調偏綠程度。</summary>
    Public Property GreenShift As Double
    Public Property MeanR As Double
    Public Property MeanG As Double
    Public Property MeanB As Double
    ''' <summary>拉普拉斯能量，粗略代表清晰度（景深比較用）。</summary>
    Public Property Sharpness As Double
    Public Property SampleCount As Integer
End Class

Public Interface IColorMatchingService
    Function Apply(source As RgbaImage, p As ColorParams) As RgbaImage
    Function Recommend(background As RgbaImage, region As PixelRect, figure As RgbaImage, alpha As FloatMask, Optional strength As Double = 1) As ColorParams
End Interface

''' <summary>
''' 色彩與光線融合。預設（Original）不改變人物風格；EnvironmentMatch 依放置處附近的背景做輕度調整，
''' 保留動漫線條與配色，只把亮度、色溫、對比往環境靠攏；Custom 由使用者全權設定。
''' </summary>
Public NotInheritable Class ColorMatchingService
    Implements IColorMatchingService

    ''' <summary>套用色彩參數，回傳新影像（Alpha 不變）。Original 模式或參數等同不變時回傳原影像。</summary>
    Public Function Apply(source As RgbaImage, p As ColorParams) As RgbaImage Implements IColorMatchingService.Apply
        If p Is Nothing OrElse p.IsIdentity Then Return source
        ' 建 256 階查表不夠（飽和度、色溫跨通道），逐像素計算但以平行處理。
        Dim dst As New RgbaImage(source.Width, source.Height)
        Dim s = source.Pixels, d = dst.Pixels
        Dim tR = 1 + 0.12 * p.Temperature, tB = 1 - 0.12 * p.Temperature
        Dim tG = 1 - 0.08 * p.Tint
        Dim gr = CSng(p.GainR * tR), gg = CSng(p.GainG * tG), gb = CSng(p.GainB * tB)
        Dim br = CSng(p.Brightness), ct = CSng(p.Contrast), sat = CSng(p.Saturation)
        Dim invGamma = CSng(1 / Math.Max(0.1, p.Gamma))
        Dim useGamma = Math.Abs(p.Gamma - 1) > 0.0001
        Dim k = CSng(Math.Clamp(p.Strength, 0, 1))
        Threading.Tasks.Parallel.For(0, source.Height,
            Sub(y)
                Dim row = y * source.Width * 4
                For x = 0 To source.Width - 1
                    Dim i = row + x * 4
                    Dim r0 = s(i) / 255.0F, g0 = s(i + 1) / 255.0F, b0 = s(i + 2) / 255.0F
                    Dim r = r0 * gr, g = g0 * gg, b = b0 * gb
                    r += br : g += br : b += br
                    r = (r - 0.5F) * ct + 0.5F : g = (g - 0.5F) * ct + 0.5F : b = (b - 0.5F) * ct + 0.5F
                    Dim l = 0.2126F * r + 0.7152F * g + 0.0722F * b
                    r = l + (r - l) * sat : g = l + (g - l) * sat : b = l + (b - l) * sat
                    If useGamma Then
                        r = CSng(Math.Pow(Math.Max(0, r), invGamma)) : g = CSng(Math.Pow(Math.Max(0, g), invGamma)) : b = CSng(Math.Pow(Math.Max(0, b), invGamma))
                    End If
                    d(i) = RgbaImage.ToByte(r0 + (r - r0) * k)
                    d(i + 1) = RgbaImage.ToByte(g0 + (g - g0) * k)
                    d(i + 2) = RgbaImage.ToByte(b0 + (b - b0) * k)
                    d(i + 3) = s(i + 3)
                Next
            End Sub)
        Return dst
    End Function

    ''' <summary>
    ''' 推薦環境匹配參數：比較放置區域（region，背景像素）與人物前景（Alpha 加權）的統計，
    ''' 各項只往背景移動一小部分並夾限，避免把動漫人物強制轉成照片質感。strength 0..1 控制整體幅度。
    ''' </summary>
    Public Function Recommend(background As RgbaImage, region As PixelRect, figure As RgbaImage, alpha As FloatMask, Optional strength As Double = 1) As ColorParams Implements IColorMatchingService.Recommend
        Dim bg = Measure(background, region, Nothing)
        Dim fg = Measure(figure, New PixelRect(0, 0, figure.Width, figure.Height), alpha)
        Dim p As New ColorParams With {.Mode = ColorMode.EnvironmentMatch}
        If bg.SampleCount = 0 OrElse fg.SampleCount = 0 Then Return p
        Dim k = Math.Clamp(strength, 0, 1)
        p.Brightness = Math.Clamp((bg.MeanLuma - fg.MeanLuma) * 0.25 * k, -0.12, 0.12)
        p.Contrast = Math.Clamp(1 + (SafeRatio(bg.StdLuma, fg.StdLuma) - 1) * 0.3 * k, 0.85, 1.15)
        p.Saturation = Math.Clamp(1 + (SafeRatio(bg.MeanSaturation, fg.MeanSaturation) - 1) * 0.3 * k, 0.8, 1.1)
        p.Temperature = Math.Clamp((bg.Warmth - fg.Warmth) * 1.5 * k, -0.4, 0.4)
        p.Tint = Math.Clamp(-(bg.GreenShift - fg.GreenShift) * 1.5 * k, -0.3, 0.3)
        ' 環境色偏：背景平均色相對灰色的偏差，取 15%。
        Dim bm = (bg.MeanR + bg.MeanG + bg.MeanB) / 3
        If bm > 0.02 Then
            p.GainR = Math.Clamp(1 + (bg.MeanR / bm - 1) * 0.15 * k, 0.92, 1.08)
            p.GainG = Math.Clamp(1 + (bg.MeanG / bm - 1) * 0.15 * k, 0.92, 1.08)
            p.GainB = Math.Clamp(1 + (bg.MeanB / bm - 1) * 0.15 * k, 0.92, 1.08)
        End If
        ' 景深：背景明顯比人物模糊時建議輕微模糊（背景像素 sigma）。
        Dim ratio = SafeRatio(bg.Sharpness, fg.Sharpness)
        p.DepthBlur = If(ratio < 0.5, Math.Clamp((1 / Math.Max(0.05, ratio) - 2) * 0.6, 0, 3), 0)
        p.Strength = 1
        Return p
    End Function

    Private Shared Function SafeRatio(a As Double, b As Double) As Double
        If b < 0.0001 Then Return 1
        Return Math.Clamp(a / b, 0.2, 5)
    End Function

    ''' <summary>統計區域色彩；alpha 不為 Nothing 時以 Alpha 加權（只看前景）。大區域以跳點取樣。</summary>
    Public Shared Function Measure(img As RgbaImage, region As PixelRect, alpha As FloatMask) As ColorStats
        Dim r = region.Intersect(New PixelRect(0, 0, img.Width, img.Height))
        Dim st As New ColorStats()
        If r.IsEmpty Then Return st
        Dim stepPx = Math.Max(1, CInt(Math.Sqrt(CDbl(r.Width) * r.Height / 40000)))
        Dim p = img.Pixels, w = img.Width
        Dim lumas As New List(Of (L As Double, R As Double, G As Double, B As Double, W As Double))()
        Dim sw = 0.0, sl = 0.0, sl2 = 0.0, ss = 0.0, sr = 0.0, sg = 0.0, sb = 0.0, lap = 0.0, lapN = 0.0
        For y = r.Y To r.Bottom - 1 Step stepPx
            For x = r.X To r.Right - 1 Step stepPx
                Dim wt = If(alpha Is Nothing, 1.0, CDbl(alpha(x, y)))
                If wt < 0.5 Then Continue For
                Dim i = (y * w + x) * 4
                Dim rr = p(i) / 255.0, gg = p(i + 1) / 255.0, bb = p(i + 2) / 255.0
                Dim l = 0.2126 * rr + 0.7152 * gg + 0.0722 * bb
                Dim mx = Math.Max(rr, Math.Max(gg, bb)), mn = Math.Min(rr, Math.Min(gg, bb))
                Dim sat = If(mx > 0.0001, (mx - mn) / mx, 0)
                sw += wt : sl += l * wt : sl2 += l * l * wt : ss += sat * wt
                sr += rr * wt : sg += gg * wt : sb += bb * wt
                lumas.Add((l, rr, gg, bb, wt))
                ' 拉普拉斯（四鄰），只在內部點。
                If x > 0 AndAlso y > 0 AndAlso x < img.Width - 1 AndAlso y < img.Height - 1 Then
                    Dim c = Luma(p, i)
                    Dim lv = 4 * c - Luma(p, i - 4) - Luma(p, i + 4) - Luma(p, i - w * 4) - Luma(p, i + w * 4)
                    ' 前景內部才算（避開去背邊緣造成的假邊）。
                    If alpha Is Nothing OrElse (alpha(x - 1, y) > 0.9F AndAlso alpha(x + 1, y) > 0.9F AndAlso alpha(x, y - 1) > 0.9F AndAlso alpha(x, y + 1) > 0.9F) Then
                        lap += lv * lv : lapN += 1
                    End If
                End If
            Next
        Next
        If sw <= 0 Then Return st
        st.SampleCount = lumas.Count
        st.MeanLuma = sl / sw
        st.StdLuma = Math.Sqrt(Math.Max(0, sl2 / sw - st.MeanLuma * st.MeanLuma))
        st.MeanSaturation = ss / sw
        st.MeanR = sr / sw : st.MeanG = sg / sw : st.MeanB = sb / sw
        st.Sharpness = If(lapN > 0, Math.Sqrt(lap / lapN), 0)
        ' 亮部色溫：亮度前 30%。
        lumas.Sort(Function(a, b) b.L.CompareTo(a.L))
        Dim top = Math.Max(1, CInt(lumas.Count * 0.3))
        Dim wr = 0.0, wg = 0.0, wb = 0.0, ww = 0.0
        For i = 0 To top - 1
            Dim e = lumas(i)
            wr += e.R * e.W : wg += e.G * e.W : wb += e.B * e.W : ww += e.W
        Next
        If ww > 0 Then
            st.Warmth = (wr - wb) / ww
            st.GreenShift = (wg - (wr + wb) / 2) / ww
        End If
        Return st
    End Function

    Private Shared Function Luma(p As Byte(), i As Integer) As Double
        Return (0.2126 * p(i) + 0.7152 * p(i + 1) + 0.0722 * p(i + 2)) / 255.0
    End Function
End Class
