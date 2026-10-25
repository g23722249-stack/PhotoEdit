''' <summary>重採樣品質。預覽用雙線性，正式輸出用雙三次。</summary>
Public Enum ResampleQuality
    Bilinear
    Bicubic
End Enum

''' <summary>畫布上的一塊預乘浮點 RGBA 區域（圖層變換結果），只配置圖層實際覆蓋的範圍。</summary>
Public NotInheritable Class PremultipliedTile
    Public ReadOnly Property Bounds As PixelRect
    ''' <summary>長度 = 寬 × 高 × 4，預乘 RGBA，0..1。</summary>
    Public ReadOnly Property Data As Single()

    Public Sub New(bounds As PixelRect)
        Me.Bounds = bounds
        Data = New Single(Math.Max(0, bounds.Width * bounds.Height * 4) - 1) {}
    End Sub
End Class

Public Interface ITransformService
    Function BuildMatrix(layer As CompositeLayer) As Matrix3
    Function TransformedBounds(matrix As Matrix3, sourceWidth As Integer, sourceHeight As Integer) As (MinX As Double, MinY As Double, MaxX As Double, MaxY As Double)
    Function RenderLayer(sampler As LayerSampler, matrix As Matrix3, canvasWidth As Integer, canvasHeight As Integer, quality As ResampleQuality, Optional cancel As Threading.CancellationToken = Nothing) As PremultipliedTile
End Interface

''' <summary>
''' 平移、等比／自由縮放、旋轉、仿射與透視（Homography）變換。
''' 矩陣：M = T(錨點目標) · R(旋轉) · S(縮放) · P(透視，錨點局部座標) · T(-人物錨點)。
''' 預設只做等比縮放＋平移；透視只在模式啟用且參數合理時套用，並限制比例，避免拉長四肢或改變頭部比例。
''' </summary>
Public NotInheritable Class TransformService
    Implements ITransformService

    Public Const MinKeystone As Double = 0.6
    Public Const MaxKeystone As Double = 1.4
    Public Const MinVertical As Double = 0.8
    Public Const MaxVertical As Double = 1.2

    Public Function BuildMatrix(layer As CompositeLayer) As Matrix3 Implements ITransformService.BuildMatrix
        Dim t = layer.Transform
        Dim w = If(layer.Source Is Nothing, 1, layer.Source.Width), h = If(layer.Source Is Nothing, 1, layer.Source.Height)
        Return Compose(t, layer.Anchor, w, h)
    End Function

    ''' <summary>由變換參數、人物錨點與人物原圖尺寸組成矩陣。</summary>
    Public Shared Function Compose(t As LayerTransform, anchor As PointD, sourceWidth As Integer, sourceHeight As Integer) As Matrix3
        If t.Scale <= 0 OrElse Double.IsNaN(t.Scale) Then Throw New ArgumentOutOfRangeException(NameOf(t), "縮放倍率必須大於 0。")
        Dim m = Matrix3.Translation(-anchor.X, -anchor.Y)
        Dim p = PerspectiveMatrix(t.Perspective, anchor, sourceWidth, sourceHeight)
        m = p * m
        If t.FlipHorizontal Then m = Matrix3.Scaling(-1, 1) * m
        m = Matrix3.Scaling(t.Scale * t.ScaleXFactor, t.Scale) * m
        m = Matrix3.Rotation(t.Rotation) * m
        m = Matrix3.Translation(t.X, t.Y) * m
        Return m
    End Function

    ''' <summary>錨點局部座標下的透視矩陣（錨點保持不動）。</summary>
    Public Shared Function PerspectiveMatrix(pp As PerspectiveParams, anchor As PointD, w As Integer, h As Integer) As Matrix3
        If pp Is Nothing OrElse pp.Mode = PerspectiveMode.None Then Return Matrix3.Identity
        ' 人物原圖四角（錨點局部座標）。
        Dim l = -anchor.X, r = w - anchor.X, tp = -anchor.Y, bt = h - anchor.Y
        Dim src = {New PointD(l, tp), New PointD(r, tp), New PointD(r, bt), New PointD(l, bt)}
        Dim dst As PointD()
        Select Case pp.Mode
            Case PerspectiveMode.Keystone
                Dim k = Math.Clamp(pp.KeystoneRatio, MinKeystone, MaxKeystone)
                Dim v = Math.Clamp(pp.VerticalFactor, MinVertical, MaxVertical)
                If Math.Abs(k - 1) < 0.0001 AndAlso Math.Abs(v - 1) < 0.0001 Then Return Matrix3.Identity
                ' 寬度比例在錨點（y = 0）為 1、在頂緣（y = tp）為 k，沿 y 線性變化；
                ' 左右邊因此收斂到錨點正上方的同一消失點，人物只做透視收斂、不改變局部比例。
                If bt - tp <= 0 Then Return Matrix3.Identity
                Dim scaleAt = Function(y As Double) 1 + (k - 1) * (-y) / Math.Max(1, -tp)
                Dim kt = scaleAt(tp)
                Dim kb = Math.Max(0.05, scaleAt(bt))
                dst = {New PointD(l * kt, tp * v), New PointD(r * kt, tp * v), New PointD(r * kb, bt * v), New PointD(l * kb, bt * v)}
            Case Else
                Dim o = If(pp.CornerOffsets, {New PointD(), New PointD(), New PointD(), New PointD()})
                If o.Length <> 4 Then Return Matrix3.Identity
                dst = {src(0) + o(0), src(1) + o(1), src(2) + o(2), src(3) + o(3)}
        End Select
        Return Matrix3.FromQuad(src, dst)
    End Function

    Public Function TransformedBounds(matrix As Matrix3, sourceWidth As Integer, sourceHeight As Integer) As (MinX As Double, MinY As Double, MaxX As Double, MaxY As Double) Implements ITransformService.TransformedBounds
        Dim pts = {matrix.Transform(New PointD(0, 0)), matrix.Transform(New PointD(sourceWidth, 0)),
                   matrix.Transform(New PointD(sourceWidth, sourceHeight)), matrix.Transform(New PointD(0, sourceHeight))}
        Return (pts.Min(Function(p) p.X), pts.Min(Function(p) p.Y), pts.Max(Function(p) p.X), pts.Max(Function(p) p.Y))
    End Function

    ''' <summary>
    ''' 把圖層依矩陣重採樣到畫布（反向映射：每個輸出像素中心反算回人物原圖取樣）。
    ''' 只計算變換後外框與畫布的交集。透視時檢查四角都在相機前方（w &gt; 0），否則丟出例外。
    ''' </summary>
    Public Function RenderLayer(sampler As LayerSampler, matrix As Matrix3, canvasWidth As Integer, canvasHeight As Integer, quality As ResampleQuality, Optional cancel As Threading.CancellationToken = Nothing) As PremultipliedTile Implements ITransformService.RenderLayer
        CheckProjectable(matrix, sampler.Width, sampler.Height)
        Dim b = TransformedBounds(matrix, sampler.Width, sampler.Height)
        Dim rect = New PixelRect(CInt(Math.Floor(b.MinX)) - 1, CInt(Math.Floor(b.MinY)) - 1,
                                 CInt(Math.Ceiling(b.MaxX)) - CInt(Math.Floor(b.MinX)) + 2,
                                 CInt(Math.Ceiling(b.MaxY)) - CInt(Math.Floor(b.MinY)) + 2).Intersect(New PixelRect(0, 0, canvasWidth, canvasHeight))
        Dim tile As New PremultipliedTile(rect)
        If rect.IsEmpty Then Return tile
        Dim inv = matrix.Inverse()
        ' 依最小局部縮放挑金字塔層級（透視時頂部縮得較小，取最保守者）。
        Dim scale = Math.Min(Math.Min(matrix.LocalScale(New PointD(0, 0)), matrix.LocalScale(New PointD(sampler.Width, 0))),
                             Math.Min(matrix.LocalScale(New PointD(0, sampler.Height)), matrix.LocalScale(New PointD(sampler.Width, sampler.Height))))
        Dim view = sampler.View(sampler.LevelFor(scale))
        Dim d = tile.Data
        Dim tw = rect.Width
        Dim sw = sampler.Width, sh = sampler.Height
        Dim bicubic = quality = ResampleQuality.Bicubic
        Dim opts As New Threading.Tasks.ParallelOptions With {.CancellationToken = cancel}
        Threading.Tasks.Parallel.For(0, rect.Height, opts,
            Sub(ty)
                Dim y = rect.Y + ty + 0.5
                For tx = 0 To tw - 1
                    Dim s = inv.Transform(New PointD(rect.X + tx + 0.5, y))
                    If s.X < -2 OrElse s.Y < -2 OrElse s.X > sw + 2 OrElse s.Y > sh + 2 Then Continue For
                    Dim p = If(bicubic, view.Bicubic(s.X, s.Y), view.Bilinear(s.X, s.Y))
                    If p.A <= 0 Then Continue For
                    Dim o = (ty * tw + tx) * 4
                    d(o) = p.R : d(o + 1) = p.G : d(o + 2) = p.B : d(o + 3) = p.A
                Next
            End Sub)
        Return tile
    End Function

    ''' <summary>透視矩陣把人物任何一角投到相機後方（w ≤ 0）時，變換無效。</summary>
    Public Shared Sub CheckProjectable(m As Matrix3, w As Integer, h As Integer)
        For Each p In {New PointD(0, 0), New PointD(w, 0), New PointD(w, h), New PointD(0, h)}
            Dim ww = m.M31 * p.X + m.M32 * p.Y + m.M33
            If ww <= 0.000001 Then Throw New InvalidOperationException("透視參數使人物翻轉到視點後方，請減少透視強度。")
        Next
    End Sub

    ''' <summary>
    ''' 由兩條背景參考線（例如石柱左右邊緣）推估垂直消失點，再算出人物上緣相對下緣的寬度比例。
    ''' 參考線接近平行（消失點在無窮遠）時回傳 1。比例限制在 MinKeystone～MaxKeystone。
    ''' </summary>
    Public Shared Function KeystoneFromGuides(a As GuideLine, b As GuideLine, anchorY As Double, figureHeightOnCanvas As Double) As Double
        Dim vp = Intersect(a.P1, a.P2, b.P1, b.P2)
        If Not vp.HasValue OrElse figureHeightOnCanvas <= 0 Then Return 1
        Dim vy = vp.Value.Y
        ' 寬度與到消失點的垂直距離成正比。
        Dim dBottom = anchorY - vy
        Dim dTop = anchorY - figureHeightOnCanvas - vy
        If Math.Abs(dBottom) < 1 Then Return 1
        Dim k = dTop / dBottom
        If Double.IsNaN(k) OrElse k <= 0 Then Return MinKeystone
        Return Math.Clamp(k, MinKeystone, MaxKeystone)
    End Function

    ''' <summary>兩條直線（各兩點）的交點；平行時回傳 Nothing。</summary>
    Public Shared Function Intersect(p1 As PointD, p2 As PointD, p3 As PointD, p4 As PointD) As PointD?
        Dim d = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X)
        If Math.Abs(d) < 0.000001 Then Return Nothing
        Dim t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / d
        Dim pt = New PointD(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y))
        ' 消失點太遠（幾乎平行）視為沒有收斂。
        If Math.Abs(pt.Y) > 1000000 Then Return Nothing
        Return pt
    End Function
End Class
