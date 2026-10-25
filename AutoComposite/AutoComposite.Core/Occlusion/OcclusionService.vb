''' <summary>遮罩筆刷模式。</summary>
Public Enum BrushMode
    ''' <summary>增加（保留前景／加入遮擋）。</summary>
    Add
    ''' <summary>減少（移除前景／取消遮擋）。</summary>
    Remove
End Enum

''' <summary>圓形柔邊筆刷（座標為該遮罩的像素座標）。</summary>
Public NotInheritable Class BrushStroke
    Public Property Mode As BrushMode = BrushMode.Add
    Public Property Radius As Double = 20
    ''' <summary>0 = 硬邊，1 = 從中心到邊緣全程柔化。</summary>
    Public Property Softness As Double = 0.5
    ''' <summary>每一點的強度（0..1）。</summary>
    Public Property Strength As Double = 1
    Public Property Points As New List(Of PointD)()
End Class

Public Interface IOcclusionService
    Sub Paint(mask As FloatMask, stroke As BrushStroke)
    Function EffectiveOcclusion(scene As CompositeScene, layer As CompositeLayer, canvasWidth As Integer, canvasHeight As Integer) As FloatMask
End Interface

''' <summary>
''' 前後景遮擋：場景的前景物件遮罩（擋在所有人物前面）與圖層專屬遮擋遮罩，以使用者筆刷繪製並可羽化。
''' 不自動判斷前後關係——單張照片的深度估計不可靠，交給使用者明確指定。
''' </summary>
Public NotInheritable Class OcclusionService
    Implements IOcclusionService

    ' 快取：同一組遮罩版本與尺寸只算一次羽化與縮放。
    Private ReadOnly _cache As New Dictionary(Of String, FloatMask)()
    Private ReadOnly _lock As New Object()

    ''' <summary>沿筆畫逐點蓋上柔邊圓形（點距不足時內插，避免斷續）。</summary>
    Public Sub Paint(mask As FloatMask, stroke As BrushStroke) Implements IOcclusionService.Paint
        BrushPainter.Paint(mask, stroke)
    End Sub

    ''' <summary>
    ''' 圖層實際的遮擋量（畫布尺寸，1 = 完全被擋住）：場景前景遮罩與圖層遮罩取最大值，羽化後縮放到畫布。
    ''' 都沒有時回傳 Nothing。
    ''' </summary>
    Public Function EffectiveOcclusion(scene As CompositeScene, layer As CompositeLayer, canvasWidth As Integer, canvasHeight As Integer) As FloatMask Implements IOcclusionService.EffectiveOcclusion
        Dim fg = scene.ForegroundMask, own = layer?.OcclusionMask
        If fg Is Nothing AndAlso own Is Nothing Then Return Nothing
        Dim key = $"{scene.MaskVersion}|{If(layer?.Id, "")}|{If(own Is Nothing, 0, 1)}|{If(fg Is Nothing, 0, 1)}|{canvasWidth}x{canvasHeight}|{scene.OcclusionFeather}"
        SyncLock _lock
            Dim cached As FloatMask = Nothing
            If _cache.TryGetValue(key, cached) Then Return cached
        End SyncLock
        Dim combined As FloatMask
        If fg IsNot Nothing AndAlso own IsNot Nothing Then
            combined = fg.Clone()
            Dim o = If(own.Width = fg.Width AndAlso own.Height = fg.Height, own, own.Resize(fg.Width, fg.Height))
            For i = 0 To combined.Data.Length - 1
                combined.Data(i) = Math.Max(combined.Data(i), o.Data(i))
            Next
        Else
            combined = If(fg, own).Clone()
        End If
        If scene.OcclusionFeather > 0.3 Then Blur.GaussianInPlace(combined, scene.OcclusionFeather)
        Dim result = If(combined.Width = canvasWidth AndAlso combined.Height = canvasHeight, combined, combined.Resize(canvasWidth, canvasHeight))
        SyncLock _lock
            If _cache.Count > 16 Then _cache.Clear()
            _cache(key) = result
        End SyncLock
        Return result
    End Function

    Public Sub ClearCache()
        SyncLock _lock
            _cache.Clear()
        End SyncLock
    End Sub
End Class

''' <summary>遮罩筆刷繪製（遮擋、保護、陰影接收、去背修正共用）。</summary>
Public NotInheritable Class BrushPainter
    Private Sub New()
    End Sub

    Public Shared Sub Paint(mask As FloatMask, stroke As BrushStroke)
        If stroke Is Nothing OrElse stroke.Points.Count = 0 OrElse stroke.Radius <= 0 Then Return
        Dim pts As New List(Of PointD) From {stroke.Points(0)}
        Dim spacing = Math.Max(0.5, stroke.Radius * 0.25)
        For i = 1 To stroke.Points.Count - 1
            Dim a = stroke.Points(i - 1), b = stroke.Points(i)
            Dim dist = a.DistanceTo(b)
            Dim n = CInt(Math.Ceiling(dist / spacing))
            For k = 1 To n
                pts.Add(a + (b - a) * (k / CDbl(n)))
            Next
        Next
        ' 同一筆畫內每個像素只取最大覆蓋量，避免重疊點把柔邊疊成硬邊。
        Dim r = stroke.Radius
        Dim minX = Math.Max(0, CInt(Math.Floor(pts.Min(Function(p) p.X) - r))), maxX = Math.Min(mask.Width - 1, CInt(Math.Ceiling(pts.Max(Function(p) p.X) + r)))
        Dim minY = Math.Max(0, CInt(Math.Floor(pts.Min(Function(p) p.Y) - r))), maxY = Math.Min(mask.Height - 1, CInt(Math.Ceiling(pts.Max(Function(p) p.Y) + r)))
        If maxX < minX OrElse maxY < minY Then Return
        Dim bw = maxX - minX + 1, bh = maxY - minY + 1
        Dim cover(bw * bh - 1) As Single
        Dim inner = r * (1 - Math.Clamp(stroke.Softness, 0, 1))
        Dim strength = CSng(Math.Clamp(stroke.Strength, 0, 1))
        For Each p In pts
            Dim x0 = Math.Max(minX, CInt(Math.Floor(p.X - r))), x1 = Math.Min(maxX, CInt(Math.Ceiling(p.X + r)))
            Dim y0 = Math.Max(minY, CInt(Math.Floor(p.Y - r))), y1 = Math.Min(maxY, CInt(Math.Ceiling(p.Y + r)))
            For y = y0 To y1
                For x = x0 To x1
                    Dim d = Math.Sqrt((x + 0.5 - p.X) ^ 2 + (y + 0.5 - p.Y) ^ 2)
                    If d >= r Then Continue For
                    Dim v = If(d <= inner, 1.0, 1 - (d - inner) / Math.Max(0.0001, r - inner))
                    v = v * v * (3 - 2 * v)
                    Dim i = (y - minY) * bw + (x - minX)
                    If v > cover(i) Then cover(i) = CSng(v)
                Next
            Next
        Next
        Dim add = stroke.Mode = BrushMode.Add
        For y = 0 To bh - 1
            For x = 0 To bw - 1
                Dim c = cover(y * bw + x) * strength
                If c <= 0 Then Continue For
                Dim i = (minY + y) * mask.Width + minX + x
                Dim v = mask.Data(i)
                mask.Data(i) = If(add, v + (1 - v) * c, v * (1 - c))
            Next
        Next
    End Sub
End Class
