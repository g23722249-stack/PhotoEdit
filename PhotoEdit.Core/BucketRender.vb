Imports System.Drawing
Imports System.Drawing.Imaging

''' <summary>
''' 點陣圖層裡的油漆桶操作：依選項取樣（目前圖層或連同下面的照片與圖層），用 FloodFill 算出範圍，
''' 再用目前筆刷上色（線條色＋筆刷質感，例如紋理筆填材質）或填回原照片的顏色，以本操作的不透明度與混合模式疊上去。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    ''' <summary>這一筆操作要讀下面的照片與圖層（取樣「所有圖層」或填入「原照片」）。</summary>
    Friend Shared Function BucketNeedsBelow(op As Global.PhotoEdit.DrawLayer) As Boolean
        Return op.Shape = DrawShape.Bucket AndAlso op.Bucket IsNot Nothing AndAlso
               (op.Bucket.Sample = BucketSample.AllLayers OrElse op.Bucket.Source = BucketSource.Photo)
    End Function

    ''' <summary>
    ''' bmp：圖層到目前為止的像素（w × h，圖層座標）；below：下面合成好的畫面（整張畫布座標，可能為 Nothing），
    ''' 圖層位移 (ox, oy) 像素。
    ''' </summary>
    ''' <summary>
    ''' 合成圖層時由 LayerStack.Draw／DrawLayers 設定：把目前圖層「上面」的所有圖層畫到指定的點陣圖上。
    ''' 油漆桶「取樣所有圖層」用（上色圖層放在線稿下面時，要看得到上面的線稿）。
    ''' </summary>
    <ThreadStatic> Friend Shared AboveDrawer As Action(Of Bitmap)

    Private Shared Sub ApplyBucketOp(bmp As Bitmap, below As Byte(), above As Byte(), w As Integer, h As Integer, ox As Integer, oy As Integer,
                                     op As Global.PhotoEdit.DrawLayer)
        Dim b = op.Bucket
        If b Is Nothing Then Return
        Dim opacity = Math.Max(0, Math.Min(100, op.Opacity)) / 100.0F
        If opacity <= 0 Then Return
        Dim layerPx = Perspective.ReadPixels(bmp)
        Dim sample = layerPx
        If b.Sample = BucketSample.AllLayers Then
            ' 下面的畫面 → 目前圖層 → 上面的圖層，疊成看到的樣子
            If below IsNot Nothing Then sample = Over(layerPx, ToLayer(below, w, h, ox, oy))
            If above IsNot Nothing Then sample = Over(ToLayer(above, w, h, ox, oy), sample)
        End If
        Dim sx = CInt(Math.Floor(b.X * h)), sy = CInt(Math.Floor(b.Y * h))
        Dim perMille = Function(v As Integer) If(v <= 0, 0, Math.Max(1, CInt(Math.Round(Math.Min(10, v) * h / 1000.0))))
        Dim mask = FloodFill.Region(sample, w, h, sx, sy, b.Tolerance, b.Contiguous, perMille(b.GapClose), perMille(b.Expand), b.AntiAlias)
        If mask Is Nothing Then Return

        ' 只處理有填到的範圍
        Dim l = w, t = h, r = -1, btm = -1
        For y = 0 To h - 1
            Dim row = y * w
            For x = 0 To w - 1
                If mask(row + x) <= 0 Then Continue For
                If x < l Then l = x
                If x > r Then r = x
                If y < t Then t = y
                If y > btm Then btm = y
            Next
        Next
        If r < 0 Then Return
        Dim region = Rectangle.FromLTRB(l, t, r + 1, btm + 1)

        Using fill = If(b.Source = BucketSource.Photo, PhotoFill(mask, below, w, h, ox, oy, region), BrushFill(mask, w, h, region, op))
            If fill IsNot Nothing Then LayerBlend.Composite(bmp, fill, region, opacity, op.Blend)
        End Using
    End Sub

    ''' <summary>選取區「內容感知」填滿：看下面的照片與圖層（連同本圖層已畫的），用附近相近的區域補滿選取範圍。</summary>
    Private Shared Sub ApplyContentFillOp(bmp As Bitmap, below As Byte(), w As Integer, h As Integer, ox As Integer, oy As Integer,
                                          op As Global.PhotoEdit.DrawLayer)
        Dim opacity = Math.Max(0, Math.Min(100, op.Opacity)) / 100.0F
        If opacity <= 0 Then Return
        Dim layerPx = Perspective.ReadPixels(bmp)
        Dim sample = If(below IsNot Nothing, Over(layerPx, ToLayer(below, w, h, ox, oy)), layerPx)
        Dim patch = ContentFill.Fill(sample, w, h, SelectionMask.Render(op.Region, w, h))
        If patch Is Nothing Then Return
        Using fill As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Perspective.WritePixels(fill, patch)
            LayerBlend.Composite(bmp, fill, New Rectangle(0, 0, w, h), opacity, op.Blend)
        End Using
    End Sub

    ''' <summary>選取區填材質：選取範圍當覆蓋率，用紋理筆的材質上色（顏色 = 填色）。</summary>
    Private Shared Sub ApplyMaterialRegionOp(bmp As Bitmap, w As Integer, h As Integer, op As Global.PhotoEdit.DrawLayer)
        Dim opacity = Math.Max(0, Math.Min(100, op.Opacity)) / 100.0F
        If opacity <= 0 Then Return
        Dim m = SelectionMask.Render(op.Region, w, h)
        Dim mask(m.Length - 1) As Single
        Dim l = w, t = h, r = -1, b = -1
        For i = 0 To m.Length - 1
            If m(i) = 0 Then Continue For
            mask(i) = m(i) / 255.0F
            Dim x = i Mod w, y = i \ w
            If x < l Then l = x
            If x > r Then r = x
            If y < t Then t = y
            If y > b Then b = y
        Next
        If r < 0 Then Return
        Dim region = Rectangle.FromLTRB(l, t, r + 1, b + 1)
        Dim style = op.Clone()
        style.Brush = BrushKind.Texture
        style.StrokeColorArgb = op.FillColorArgb
        Using fill = BrushFill(mask, w, h, region, style)
            LayerBlend.Composite(bmp, fill, region, opacity, op.Blend)
        End Using
    End Sub

    ''' <summary>整張畫布座標的像素（w × h）換成圖層座標（扣掉圖層位移 ox, oy；超出的地方透明）。</summary>
    Private Shared Function ToLayer(canvasPx As Byte(), w As Integer, h As Integer, ox As Integer, oy As Integer) As Byte()
        If ox = 0 AndAlso oy = 0 Then Return canvasPx
        Dim result(w * h * 4 - 1) As Byte
        For y = 0 To h - 1
            Dim gy = y + oy
            If gy < 0 OrElse gy >= h Then Continue For
            Dim x0 = Math.Max(0, -ox), x1 = Math.Min(w - 1, w - 1 - ox)
            If x1 < x0 Then Continue For
            Array.Copy(canvasPx, (gy * w + x0 + ox) * 4, result, (y * w + x0) * 4, (x1 - x0 + 1) * 4)
        Next
        Return result
    End Function

    ''' <summary>top 疊在 bottom 上（非預乘 BGRA，一般「正常」合成），取樣「所有圖層」用。</summary>
    Private Shared Function Over(top As Byte(), bottom As Byte()) As Byte()
        Dim result(top.Length - 1) As Byte
        For i = 0 To top.Length - 4 Step 4
            Dim sa = top(i + 3) / 255.0
            If sa >= 1 Then
                result(i) = top(i) : result(i + 1) = top(i + 1) : result(i + 2) = top(i + 2) : result(i + 3) = 255
                Continue For
            End If
            Dim belowA = bottom(i + 3) / 255.0
            Dim oa = sa + belowA * (1 - sa)
            If oa <= 0 Then Continue For
            result(i) = CByte(Math.Round((top(i) * sa + bottom(i) * belowA * (1 - sa)) / oa))
            result(i + 1) = CByte(Math.Round((top(i + 1) * sa + bottom(i + 1) * belowA * (1 - sa)) / oa))
            result(i + 2) = CByte(Math.Round((top(i + 2) * sa + bottom(i + 2) * belowA * (1 - sa)) / oa))
            result(i + 3) = CByte(Math.Round(oa * 255))
        Next
        Return result
    End Function

    ''' <summary>目前筆刷上色：覆蓋率交給 Shade（同形狀的填色），顏色用線條色；紋理筆是材質、水彩有水彩邊。</summary>
    Private Shared Function BrushFill(mask As Single(), w As Integer, h As Integer, region As Rectangle, op As Global.PhotoEdit.DrawLayer) As Bitmap
        Dim cv As New Canvas(region)
        Dim cov(region.Width * region.Height - 1) As Single
        For y = 0 To region.Height - 1
            Array.Copy(mask, (y + region.Y) * w + region.X, cov, y * region.Width, region.Width)
        Next
        ' 讀畫布、特效、粒子、貼圖這類筆刷沒有「填滿」的樣子：當成硬筆填單色
        Dim style = op
        Select Case op.Brush
            Case BrushKind.FX, BrushKind.Particle, BrushKind.StickerHose, BrushKind.Mixer, BrushKind.Smudge, BrushKind.Clone
                style = op.Clone()
                style.Brush = BrushKind.HardRound
        End Select
        Dim widthPx = CSng(Math.Max(0.6, style.StrokeWidth * h))
        Shade(cv, cov, Nothing, style, Color.FromArgb(style.StrokeColorArgb), widthPx, h, isFill:=True)
        Return ToBitmap(cv)
    End Function

    ''' <summary>填回原照片（照片＋下面圖層）的顏色。</summary>
    Private Shared Function PhotoFill(mask As Single(), below As Byte(), w As Integer, h As Integer, ox As Integer, oy As Integer, region As Rectangle) As Bitmap
        If below Is Nothing Then Return Nothing
        Dim bh = below.Length \ 4 \ Math.Max(1, w)
        Dim px(region.Width * region.Height * 4 - 1) As Byte
        For y = 0 To region.Height - 1
            Dim ly = y + region.Y, gy = ly + oy
            If gy < 0 OrElse gy >= bh Then Continue For
            For x = 0 To region.Width - 1
                Dim lx = x + region.X, gx = lx + ox
                If gx < 0 OrElse gx >= w Then Continue For
                Dim m = mask(ly * w + lx)
                If m <= 0 Then Continue For
                Dim j = (gy * w + gx) * 4, k = (y * region.Width + x) * 4
                px(k) = below(j) : px(k + 1) = below(j + 1) : px(k + 2) = below(j + 2)
                px(k + 3) = CByte(Math.Round(below(j + 3) * m))
            Next
        Next
        Dim bmp As New Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb)
        Perspective.WritePixels(bmp, px)
        Return bmp
    End Function
End Class
