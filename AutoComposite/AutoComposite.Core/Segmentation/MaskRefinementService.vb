''' <summary>遮罩精修設定。</summary>
Public NotInheritable Class MaskRefineOptions
    ''' <summary>低於此值視為背景（清掉模型留下的淡淡殘值）。</summary>
    Public Property BlackPoint As Double = 0.04
    ''' <summary>高於此值視為完全前景。</summary>
    Public Property WhitePoint As Double = 0.96
    ''' <summary>內縮（負）或外擴（正）像素。</summary>
    Public Property GrowPixels As Integer
    ''' <summary>邊緣羽化 sigma（像素）。</summary>
    Public Property Feather As Double = 0.6
    ''' <summary>移除面積小於前景總量此比例的孤立碎塊（0 = 不移除）。</summary>
    Public Property RemoveSpecksFraction As Double = 0.0005
End Class

Public Interface IMaskRefinementService
    Function Refine(aiMask As FloatMask, options As MaskRefineOptions) As FloatMask
    Sub PaintCorrection(mask As FloatMask, stroke As BrushStroke)
    Function Feather(mask As FloatMask, sigma As Double) As FloatMask
    Function Grow(mask As FloatMask, pixels As Integer) As FloatMask
    Function DecontaminateEdges(source As RgbaImage, alpha As FloatMask, Optional radius As Double = 3) As RgbaImage
End Interface

''' <summary>
''' 遮罩精修：黑白點拉伸、內縮外擴、孤立碎塊移除、羽化，以及使用者的保留／移除筆刷。
''' 全程使用浮點遮罩，不二值化，保留髮絲與細長武器的半透明邊緣。
''' 另提供邊緣去色污染：半透明像素的 RGB 以附近不透明前景色取代，避免合成時出現原背景的白邊或黑邊。
''' </summary>
Public NotInheritable Class MaskRefinementService
    Implements IMaskRefinementService

    Public Function Refine(aiMask As FloatMask, options As MaskRefineOptions) As FloatMask Implements IMaskRefinementService.Refine
        options = If(options, New MaskRefineOptions())
        Dim m = aiMask.Clone()
        Dim lo = CSng(Math.Clamp(options.BlackPoint, 0, 0.49)), hi = CSng(Math.Clamp(options.WhitePoint, 0.51, 1))
        Dim range = hi - lo
        For i = 0 To m.Data.Length - 1
            m.Data(i) = Math.Clamp((m.Data(i) - lo) / range, 0.0F, 1.0F)
        Next
        If options.RemoveSpecksFraction > 0 Then RemoveSpecks(m, options.RemoveSpecksFraction)
        If options.GrowPixels <> 0 Then m = Grow(m, options.GrowPixels)
        If options.Feather > 0.3 Then m = Feather(m, options.Feather)
        Return m
    End Function

    Public Sub PaintCorrection(mask As FloatMask, stroke As BrushStroke) Implements IMaskRefinementService.PaintCorrection
        BrushPainter.Paint(mask, stroke)
    End Sub

    Public Function Feather(mask As FloatMask, sigma As Double) As FloatMask Implements IMaskRefinementService.Feather
        Dim m = mask.Clone()
        Blur.GaussianInPlace(m, sigma)
        Return m
    End Function

    Public Function Grow(mask As FloatMask, pixels As Integer) As FloatMask Implements IMaskRefinementService.Grow
        Return Blur.Morph(mask, pixels)
    End Function

    ''' <summary>移除與主體不相連、面積很小的前景碎塊（以 0.5 為門檻的四連通區域）。</summary>
    Public Shared Sub RemoveSpecks(mask As FloatMask, fraction As Double)
        Dim w = mask.Width, h = mask.Height, d = mask.Data
        Dim label(w * h - 1) As Integer
        Dim areas As New List(Of Integer) From {0}
        Dim totalFg = 0
        Dim stack As New Stack(Of Integer)()
        For start = 0 To d.Length - 1
            If d(start) < 0.5F OrElse label(start) <> 0 Then Continue For
            Dim id = areas.Count
            Dim area = 0
            label(start) = id
            stack.Push(start)
            While stack.Count > 0
                Dim i = stack.Pop()
                area += 1
                Dim x = i Mod w, y = i \ w
                If x > 0 AndAlso label(i - 1) = 0 AndAlso d(i - 1) >= 0.5F Then label(i - 1) = id : stack.Push(i - 1)
                If x < w - 1 AndAlso label(i + 1) = 0 AndAlso d(i + 1) >= 0.5F Then label(i + 1) = id : stack.Push(i + 1)
                If y > 0 AndAlso label(i - w) = 0 AndAlso d(i - w) >= 0.5F Then label(i - w) = id : stack.Push(i - w)
                If y < h - 1 AndAlso label(i + w) = 0 AndAlso d(i + w) >= 0.5F Then label(i + w) = id : stack.Push(i + w)
            End While
            areas.Add(area)
            totalFg += area
        Next
        If areas.Count <= 2 Then Return
        Dim minArea = Math.Max(4, CInt(totalFg * fraction))
        Dim largest = 0
        For i = 1 To areas.Count - 1
            If areas(i) > areas(largest) Then largest = i
        Next
        For i = 0 To d.Length - 1
            Dim id = label(i)
            If id > 0 AndAlso id <> largest AndAlso areas(id) < minArea Then d(i) = 0
        Next
    End Sub

    ''' <summary>
    ''' 邊緣去色污染：以 Alpha 加權模糊估計每個像素附近的前景顏色，半透明像素的 RGB 依 Alpha 由估計色漸變到原色。
    ''' Alpha ≥ 0.98 的像素完全不變。
    ''' </summary>
    Public Function DecontaminateEdges(source As RgbaImage, alpha As FloatMask, Optional radius As Double = 3) As RgbaImage Implements IMaskRefinementService.DecontaminateEdges
        Dim w = source.Width, h = source.Height
        Dim acc(w * h * 4 - 1) As Single
        Dim p = source.Pixels
        ' 以 Alpha^2 加權，讓幾乎不透明的像素主導估計色。
        For i = 0 To w * h - 1
            Dim a = alpha.Data(i)
            Dim wt = a * a
            acc(i * 4) = p(i * 4) * wt : acc(i * 4 + 1) = p(i * 4 + 1) * wt : acc(i * 4 + 2) = p(i * 4 + 2) * wt : acc(i * 4 + 3) = wt
        Next
        Blur.GaussianInPlace(acc, w, h, 4, radius)
        Dim dst = source.Clone()
        Dim d = dst.Pixels
        For i = 0 To w * h - 1
            Dim a = alpha.Data(i)
            If a >= 0.98F OrElse acc(i * 4 + 3) < 0.0001F Then Continue For
            Dim inv = 1 / acc(i * 4 + 3)
            Dim t = a * a
            For c = 0 To 2
                Dim est = acc(i * 4 + c) * inv
                d(i * 4 + c) = RgbaImage.ClampByte(est + (p(i * 4 + c) - est) * t)
            Next
        Next
        Return dst
    End Function
End Class
