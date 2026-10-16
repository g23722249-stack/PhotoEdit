''' <summary>
''' 墨暈（暈開）：毛筆、墨水筆、水彩的墨沿著紙纖維往筆畫外滲開——
''' 邊緣一絲一絲、不規則（纖維凸起與毛細的地方滲得遠），滲出去的墨比較淡，最外圈墨停住的地方略深（水痕）；
''' 筆畫裡筆毛間的白縫也被濕墨填起來。
''' 量以 DrawLayer.Bleed（0..100）控制；寬度以筆寬為準，預覽與匯出一致。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    ''' <summary>會暈開的筆。</summary>
    Public Shared Function BleedsInk(b As BrushKind) As Boolean
        Return b = BrushKind.ChineseBrush OrElse b = BrushKind.Ink OrElse b = BrushKind.Watercolor
    End Function

    ''' <summary>暈開需要的外圍（像素）。</summary>
    Private Shared Function BleedPad(layer As DrawLayer, widthPx As Single) As Single
        If layer.Bleed <= 0 OrElse Not BleedsInk(layer.Brush) Then Return 0
        Return widthPx * (0.15F + 1.1F * Math.Min(100, layer.Bleed) / 100.0F) + 2
    End Function

    ''' <summary>
    ''' 由筆畫的覆蓋率算出暈開：Halo 是滲到筆畫外的墨（0..1，已含淡化），Core 是濕墨把筆毛間的白縫填起來後的覆蓋率。
    ''' </summary>
    Private Shared Function InkHalo(mask As Single(), cv As Canvas, widthPx As Single, bleed As Integer, seed As Integer) As (Halo As Single(), Core As Single())
        Dim amount = Math.Max(0, Math.Min(100, bleed)) / 100.0F
        Dim r = CInt(Math.Max(1, widthPx * (0.08F + 0.5F * amount)))
        ' 兩次方框模糊 ≈ 高斯：墨往四周擴散
        Dim spread = BoxBlur(BoxBlur(mask, cv.W, cv.H, r), cv.W, cv.H, r)
        ' 筆毛間的白縫：濕墨一下就滲滿（小範圍模糊）
        Dim core = BoxBlur(mask, cv.W, cv.H, CInt(Math.Max(1, widthPx * 0.05F * (0.5F + amount))))
        Dim result(mask.Length - 1) As Single
        ' 毛細的細紋：大小跟筆寬走，滲出的邊是一絲一絲、不規則的
        Dim fine = Math.Max(1.5F, widthPx * 0.22F)
        Dim hasPaper = _paperMap IsNot Nothing
        For y = 0 To cv.H - 1
            Dim gy = y + cv.OY
            For x = 0 To cv.W - 1
                Dim i = y * cv.W + x
                Dim s = Math.Min(1.0F, spread(i))
                If s < 0.004F Then Continue For
                Dim gx = x + cv.OX
                ' 紙纖維：有紙張時用紙紋高度（棉紙的長纖維會把墨帶得遠），再加上毛細的細雜訊
                Dim fiber = If(hasPaper, PaperAt(gx, gy), 0.5F)
                Dim capillary = Fbm(gx / fine, gy / fine, seed + 71, 4)
                Dim reach = s * (0.7F + 0.9F * (fiber - 0.5F) + 1.1F * (capillary - 0.5F))
                Dim v = SmoothStep(0.08F, 0.4F, reach)
                ' 水痕：墨停住的外緣一圈略深
                Dim rim = SmoothStep(0.1F, 0.15F, reach) * (1 - SmoothStep(0.15F, 0.26F, reach))
                result(i) = Math.Min(1.0F, v * (0.28F + 0.3F * amount) * (0.75F + 0.5F * capillary) + rim * 0.16F * amount)
            Next
        Next
        Return (result, core)
    End Function
End Class
