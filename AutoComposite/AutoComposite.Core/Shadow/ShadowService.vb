''' <summary>陰影遮罩（畫布上的一塊區域，0..1 = 陰影濃度）。</summary>
Public NotInheritable Class ShadowTile
    Public ReadOnly Property Bounds As PixelRect
    Public ReadOnly Property Data As Single()

    Public Sub New(bounds As PixelRect)
        Me.Bounds = bounds
        Data = New Single(Math.Max(0, bounds.Width * bounds.Height) - 1) {}
    End Sub
End Class

Public Interface IShadowService
    Function Render(sampler As LayerSampler, layerMatrix As Matrix3, fig As FigureAnalysis, sourceAnchor As PointD, p As ShadowParams,
                    canvasWidth As Integer, canvasHeight As Integer, renderScale As Double, receiver As FloatMask,
                    Optional cancel As Threading.CancellationToken = Nothing) As ShadowTile
    Function Recommend(analysis As SceneAnalysis, fromSkyline As Boolean) As ShadowParams
End Interface

''' <summary>
''' 接觸陰影：
''' 1. 投射陰影：人物 Alpha 以腳點為支點投影到地面（剪切＋壓扁），離腳越遠越淡、越模糊；
''' 2. 接觸陰影：腳寬範圍的扁橢圓，緊貼接觸點；
''' 3. 環境遮蔽近似：更寬、更柔的淡陰影。
''' 光源方向無法從單張照片精準還原，只提供粗略估計與手動調整；預設濃度保守，避免過黑或懸浮。
''' </summary>
Public NotInheritable Class ShadowService
    Implements IShadowService

    ''' <summary>投射陰影在背景座標的仿射（以腳點 a 為原點）：高度 h 的點落到地面方向 θ、長度 h·L 處。</summary>
    Public Shared Function CastMatrix(p As ShadowParams, anchor As PointD) As Matrix3
        Dim th = p.Direction * Math.PI / 180
        Dim len = Math.Max(0, p.Length * p.Scale)
        Dim flat = Math.Clamp(p.GroundFlatten, 0.1, 1)
        ' 局部座標 (dx, dy)，dy < 0 為腳點上方。高度 h = -dy。
        ' x' = dx + h·L·cosθ；y' = h·L·sinθ·flat。sinθ ≈ 0 時保留最小厚度，避免矩陣退化。
        Dim ky = len * Math.Sin(th) * flat
        If Math.Abs(ky) < 0.04 Then ky = If(ky < 0, -0.04, 0.04)
        Dim local = New Matrix3 With {.M11 = 1, .M12 = -len * Math.Cos(th), .M13 = 0, .M21 = 0, .M22 = -ky, .M23 = 0, .M33 = 1}
        Return Matrix3.Translation(anchor.X + p.OffsetX, anchor.Y + p.OffsetY) * local * Matrix3.Translation(-anchor.X, -anchor.Y)
    End Function

    Public Function Render(sampler As LayerSampler, layerMatrix As Matrix3, fig As FigureAnalysis, sourceAnchor As PointD, p As ShadowParams,
                           canvasWidth As Integer, canvasHeight As Integer, renderScale As Double, receiver As FloatMask,
                           Optional cancel As Threading.CancellationToken = Nothing) As ShadowTile Implements IShadowService.Render
        Dim canvas = New PixelRect(0, 0, canvasWidth, canvasHeight)
        If p Is Nothing OrElse Not p.Enabled Then Return New ShadowTile(New PixelRect(0, 0, 0, 0))
        Dim anchor = layerMatrix.Transform(sourceAnchor)
        Dim figH = fig.Height * layerMatrix.LocalScale(sourceAnchor)
        Dim feetL = layerMatrix.Transform(New PointD(fig.FeetLeft, fig.FeetCenter.Y))
        Dim feetR = layerMatrix.Transform(New PointD(fig.FeetRight, fig.FeetCenter.Y))
        Dim feetW = Math.Max(2, Math.Abs(feetR.X - feetL.X))
        Dim cast = CastMatrix(p, anchor)
        Dim full = cast * layerMatrix
        Dim blurCast = Math.Max(0, p.BlurRadius * renderScale)

        ' 範圍：投射陰影外框 ∪ 接觸橢圓，外擴模糊半徑。
        Dim tsvc As New TransformService()
        Dim bb = tsvc.TransformedBounds(full, sampler.Width, sampler.Height)
        Dim rx = feetW * 0.75 + 2, ry = Math.Max(2, feetW * 0.18)
        Dim aoRx = feetW * 1.4 + 4, aoRy = Math.Max(3, feetW * 0.32)
        Dim pad = CInt(Math.Ceiling(blurCast * 3 + feetW * Math.Max(0.1, p.ContactSoftness) * 3)) + 4
        Dim minX = Math.Min(bb.MinX, anchor.X - aoRx), maxX = Math.Max(bb.MaxX, anchor.X + aoRx)
        Dim minY = Math.Min(bb.MinY, anchor.Y - aoRy), maxY = Math.Max(bb.MaxY, anchor.Y + aoRy)
        Dim rect = New PixelRect(CInt(Math.Floor(minX)) - pad, CInt(Math.Floor(minY)) - pad,
                                 CInt(Math.Ceiling(maxX - minX)) + 2 * pad, CInt(Math.Ceiling(maxY - minY)) + 2 * pad).Intersect(canvas)
        Dim tile As New ShadowTile(rect)
        If rect.IsEmpty Then Return tile
        Dim tw = rect.Width, th = rect.Height

        ' 1. 投射陰影（帶距離衰減），先畫在獨立緩衝區再模糊。
        Dim castBuf(tw * th - 1) As Single
        If p.Opacity > 0 AndAlso p.Length * p.Scale > 0.001 Then
            Dim det = full.Determinant()
            If Math.Abs(det) > 0.0000001 Then
                Dim inv = full.Inverse()
                Dim view = sampler.View(sampler.LevelFor(Math.Max(0.01, full.LocalScale(sourceAnchor))))
                Dim srcTop = CDbl(fig.Bounds.Y), srcSpan = Math.Max(1, sourceAnchor.Y - srcTop)
                Dim falloff = Math.Clamp(p.Falloff, 0, 1)
                Dim opts As New Threading.Tasks.ParallelOptions With {.CancellationToken = cancel}
                Threading.Tasks.Parallel.For(0, th, opts,
                    Sub(ty)
                        For tx = 0 To tw - 1
                            Dim s = inv.Transform(New PointD(rect.X + tx + 0.5, rect.Y + ty + 0.5))
                            If s.X < -1 OrElse s.Y < -1 OrElse s.X > sampler.Width + 1 OrElse s.Y > sampler.Height + 1 Then Continue For
                            Dim a = view.AlphaBilinear(s.X, s.Y)
                            If a <= 0 Then Continue For
                            Dim hfrac = Math.Clamp((sourceAnchor.Y - s.Y) / srcSpan, 0, 1)
                            castBuf(ty * tw + tx) = CSng(a * (1 - falloff * hfrac))
                        Next
                    End Sub)
                Blur.GaussianInPlace(castBuf, tw, th, 1, blurCast)
            End If
        End If

        ' 2. 接觸陰影與 3. 環境遮蔽：以腳點為中心的柔和橢圓。
        Dim contactSigma = Math.Max(0.5, feetW * Math.Max(0.05, p.ContactSoftness) * 0.5)
        Dim contactBuf(tw * th - 1) As Single
        If p.ContactStrength > 0 OrElse p.AmbientOcclusion > 0 Then
            Dim cxl = anchor.X - rect.X, cyl = anchor.Y - rect.Y
            For ty = 0 To th - 1
                For tx = 0 To tw - 1
                    Dim dx = tx + 0.5 - cxl, dy = ty + 0.5 - cyl
                    Dim e1 = (dx / rx) ^ 2 + (dy / ry) ^ 2
                    Dim e2 = (dx / aoRx) ^ 2 + (dy / aoRy) ^ 2
                    Dim c = p.ContactStrength * SmoothFalloff(e1)
                    Dim ao = p.AmbientOcclusion * 0.5 * SmoothFalloff(e2)
                    contactBuf(ty * tw + tx) = CSng(1 - (1 - c) * (1 - ao))
                Next
            Next
            Blur.GaussianInPlace(contactBuf, tw, th, 1, contactSigma)
        End If

        ' 合併（螢幕混合），乘上接收遮罩。
        Dim op = CSng(Math.Clamp(p.Opacity, 0, 1))
        Dim d = tile.Data
        For ty = 0 To th - 1
            For tx = 0 To tw - 1
                Dim i = ty * tw + tx
                Dim v = 1 - (1 - castBuf(i) * op) * (1 - contactBuf(i))
                If receiver IsNot Nothing Then
                    Dim gx = rect.X + tx, gy = rect.Y + ty
                    If gx < receiver.Width AndAlso gy < receiver.Height Then v *= receiver(gx, gy)
                End If
                d(i) = Math.Clamp(v, 0.0F, 0.95F)
            Next
        Next
        Return tile
    End Function

    ' e = 橢圓正規化距離平方；中心 1，邊緣平滑降到 0。
    Private Shared Function SmoothFalloff(e As Double) As Double
        If e >= 1 Then Return 0
        Dim t = 1 - e
        Return t * t * (3 - 2 * t)
    End Function

    ''' <summary>依背景分析推薦陰影參數（粗略）：光從哪側來，影子就往另一側；判斷不出時往右前方、濃度更淡。</summary>
    Public Function Recommend(analysis As SceneAnalysis, fromSkyline As Boolean) As ShadowParams Implements IShadowService.Recommend
        Dim p As New ShadowParams()
        If analysis IsNot Nothing AndAlso analysis.LightSide <> 0 Then
            p.Direction = If(analysis.LightSide > 0, 160, 20)
            p.Opacity = 0.22 + 0.1 * Math.Abs(analysis.LightSide)
        Else
            p.Direction = 30
            p.Opacity = 0.18
        End If
        ' 站在柱頂等狹窄平台：投影大多落在平台外的天空，縮短投射、加強接觸陰影，並限制只落在非天空區域。
        If fromSkyline Then
            p.Length = 0.25
            p.ContactStrength = 0.6
            p.Receiver = ShadowReceiver.NonSky
        End If
        Return p
    End Function
End Class
