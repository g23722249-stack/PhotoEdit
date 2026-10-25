''' <summary>一個候選擺放位置與其評分明細。</summary>
Public NotInheritable Class PlacementCandidate
    Public Property Transform As LayerTransform
    ''' <summary>人物上的錨點（人物原圖像素）。</summary>
    Public Property SourceAnchor As PointD
    ''' <summary>總分 0..1（越高越好）。</summary>
    Public Property Score As Double
    ''' <summary>各評分項目的扣分（0 = 完美）。</summary>
    Public Property Penalties As New Dictionary(Of String, Double)()
    Public Property Description As String = ""

    Public Function PenaltySummary() As String
        Return String.Join("，", Penalties.Where(Function(kv) kv.Value > 0.005).Select(Function(kv) $"{kv.Key} -{kv.Value:0.00}"))
    End Function
End Class

''' <summary>定位評分權重（可調整）。</summary>
Public NotInheritable Class PlacementRules
    Public Property AnchorError As Double = 1.0
    Public Property OutOfBounds As Double = 1.5
    Public Property SurfaceOverhang As Double = 0.8
    Public Property StandingOnSky As Double = 1.2
    Public Property ProtectOverlap As Double = 1.0
    Public Property HeavyOcclusion As Double = 0.6
    Public Property Perspective As Double = 0.5
    Public Property ScaleDeviation As Double = 0.25
    ''' <summary>候選縮放倍率（乘在基準縮放上）。</summary>
    Public Property ScaleMultipliers As Double() = {0.75, 0.85, 1.0, 1.15, 1.3}
    ''' <summary>候選水平位移（相對可站立寬度或腳寬）。</summary>
    Public Property OffsetFractions As Double() = {-0.15, 0, 0.15}
End Class

Public Interface IAutoPlacementService
    Function Suggest(scene As CompositeScene, layer As CompositeLayer, analysis As SceneAnalysis, Optional rules As PlacementRules = Nothing) As List(Of PlacementCandidate)
End Interface

''' <summary>
''' 自動定位：人物錨點對齊背景目標錨點，計算縮放、必要的旋轉與透視，再以可設定的規則評估多個候選位置。
''' 單張照片無法精準重建 3D 空間，這裡給的是推薦；使用者可以改錨點、目標高度或直接拖曳微調。
''' </summary>
Public NotInheritable Class AutoPlacementService
    Implements IAutoPlacementService

    Private Const GridSamples As Integer = 40

    Public Function Suggest(scene As CompositeScene, layer As CompositeLayer, analysis As SceneAnalysis, Optional rules As PlacementRules = Nothing) As List(Of PlacementCandidate) Implements IAutoPlacementService.Suggest
        If scene.Background Is Nothing Then Throw New InvalidOperationException("尚未載入背景。")
        If layer.Alpha Is Nothing Then Throw New InvalidOperationException("人物尚未去背（沒有 Alpha 遮罩）。")
        rules = If(rules, New PlacementRules())
        Dim fig = FigureAnalyzer.Analyze(layer.Alpha)
        Dim bw = scene.Width, bh = scene.Height

        ' 人物錨點。
        Dim srcAnchor As PointD
        Select Case layer.AnchorKind
            Case AnchorKind.SingleFoot, AnchorKind.Custom
                srcAnchor = layer.Anchor
            Case Else
                srcAnchor = fig.AnchorFor(layer.AnchorKind)
        End Select

        ' 背景目標：使用者指定優先，否則用分析推薦的第一名。
        Dim t = scene.Target
        Dim target As PointD, surfL As Double, surfR As Double, reason As String
        If t.HasAnchor Then
            target = New PointD(t.AnchorX, t.AnchorY)
            surfL = t.SurfaceLeftX : surfR = t.SurfaceRightX
            reason = "使用者指定的目標錨點"
        Else
            Dim s = If(analysis?.Suggestions.FirstOrDefault(), New SurfaceSuggestion With {.Anchor = New PointD(bw / 2.0, bh * 0.9)})
            target = s.Anchor
            surfL = s.SurfaceLeftX : surfR = s.SurfaceRightX
            reason = If(String.IsNullOrEmpty(s.Reason), "畫面下方中央", s.Reason)
        End If
        Dim surfaceWidth = Math.Max(0, surfR - surfL)

        ' 基準縮放。
        Dim baseScale As Double
        Dim scaleReason As String
        If t.TargetHeight > 0 Then
            baseScale = t.TargetHeight / fig.Height : scaleReason = $"指定高度 {t.TargetHeight:0} px"
        ElseIf t.TargetHeightFraction > 0 Then
            baseScale = t.TargetHeightFraction * bh / fig.Height : scaleReason = $"畫面高度的 {t.TargetHeightFraction:P0}"
        ElseIf surfaceWidth > 0 Then
            ' 站在有限寬度的平台上：腳寬約占平台寬度 55%，但人物高度不超過錨點上方空間的 85%。
            baseScale = surfaceWidth * 0.55 / fig.FeetWidth
            scaleReason = "依可站立寬度估算"
        Else
            baseScale = bh * 0.4 / fig.Height : scaleReason = "預設占畫面高度 40%"
        End If
        Dim maxByHeadroom = Math.Max(0.01, target.Y * 0.85) / fig.Height
        Dim minScale = bh * 0.06 / fig.Height
        If t.TargetHeight <= 0 AndAlso t.TargetHeightFraction <= 0 Then
            If baseScale > maxByHeadroom Then baseScale = maxByHeadroom : scaleReason &= "（受上方空間限制）"
            If baseScale < minScale Then baseScale = minScale : scaleReason &= "（最小可見尺寸）"
        End If

        ' 旋轉：兩條參考線時，以參考線在錨點附近的平均方向作為局部垂直；人物本身的姿勢傾斜不修正。
        Dim rotation = 0.0
        Dim keystone = 1.0
        Dim guideA As GuideLine = Nothing, guideB As GuideLine = Nothing
        If scene.Guides IsNot Nothing AndAlso scene.Guides.Count >= 2 Then
            guideA = scene.Guides(0) : guideB = scene.Guides(1)
            rotation = LocalVerticalAngle(guideA, guideB)
        End If

        Dim candidates As New List(Of PlacementCandidate)()
        Dim offsetUnit = If(surfaceWidth > 0, surfaceWidth, fig.FeetWidth * baseScale)
        For Each mult In rules.ScaleMultipliers
            Dim scale = baseScale * mult
            If guideA IsNot Nothing Then keystone = TransformService.KeystoneFromGuides(guideA, guideB, target.Y, fig.Height * scale)
            For Each off In rules.OffsetFractions
                Dim tr As New LayerTransform With {.X = target.X + off * offsetUnit, .Y = target.Y, .Scale = scale, .Rotation = rotation}
                If Math.Abs(keystone - 1) > 0.01 Then
                    tr.Perspective = New PerspectiveParams With {.Mode = PerspectiveMode.Keystone, .KeystoneRatio = keystone}
                End If
                Dim c = Evaluate(scene, layer, fig, srcAnchor, tr, target, surfL, surfR, mult, analysis, rules)
                c.Description = $"{reason}；{scaleReason}，倍率 ×{mult:0.00}" & If(off <> 0, $"，水平位移 {off:+0%;-0%}", "") &
                    If(tr.Perspective.Mode = PerspectiveMode.Keystone, $"，仰角透視 {keystone:0.00}", "")
                candidates.Add(c)
            Next
        Next
        candidates.Sort(Function(a, b) b.Score.CompareTo(a.Score))
        Return candidates
    End Function

    ''' <summary>評估一個候選變換。</summary>
    Public Shared Function Evaluate(scene As CompositeScene, layer As CompositeLayer, fig As FigureAnalysis, srcAnchor As PointD, tr As LayerTransform,
                                    target As PointD, surfL As Double, surfR As Double, mult As Double, analysis As SceneAnalysis, rules As PlacementRules) As PlacementCandidate
        Dim bw = scene.Width, bh = scene.Height
        Dim m = TransformService.Compose(tr, srcAnchor, layer.Alpha.Width, layer.Alpha.Height)
        Dim p As New Dictionary(Of String, Double)()

        ' 1. 錨點對齊誤差（相對人物高度）。
        Dim placed = m.Transform(srcAnchor)
        Dim figH = fig.Height * tr.Scale
        p("錨點誤差") = rules.AnchorError * Math.Min(1, placed.DistanceTo(target) / Math.Max(1, figH))

        ' 以格點取樣人物前景，估計超出畫面、保護區重疊、被前景遮住的比例。
        Dim total = 0.0, outside = 0.0, protect = 0.0, hidden = 0.0
        Dim b = fig.Bounds
        For gy = 0 To GridSamples - 1
            For gx = 0 To GridSamples - 1
                Dim sx = b.X + (gx + 0.5) * b.Width / GridSamples
                Dim sy = b.Y + (gy + 0.5) * b.Height / GridSamples
                Dim a = layer.Alpha.AtClamped(CInt(sx), CInt(sy))
                If a < 0.5F Then Continue For
                total += 1
                Dim q = m.Transform(New PointD(sx, sy))
                If q.X < 0 OrElse q.Y < 0 OrElse q.X >= bw OrElse q.Y >= bh Then
                    outside += 1
                    Continue For
                End If
                If scene.ProtectMask IsNot Nothing Then protect += scene.ProtectMask(CInt(q.X), CInt(q.Y))
                If scene.ForegroundMask IsNot Nothing Then hidden += scene.ForegroundMask(CInt(q.X), CInt(q.Y))
            Next
        Next
        If total > 0 Then
            p("超出畫面") = rules.OutOfBounds * outside / total
            p("遮住保護區") = rules.ProtectOverlap * protect / total
            p("被前景遮住過多") = rules.HeavyOcclusion * Math.Max(0, hidden / total - 0.4)
        End If

        ' 2. 接觸面相容性：腳寬超出可站立寬度、腳點落在平台外。
        Dim feetL = m.Transform(New PointD(fig.FeetLeft, fig.FeetCenter.Y)).X
        Dim feetR = m.Transform(New PointD(fig.FeetRight, fig.FeetCenter.Y)).X
        If feetR < feetL Then
            Dim tmp = feetL : feetL = feetR : feetR = tmp
        End If
        Dim surfW = surfR - surfL
        If surfW > 0 Then
            Dim over = Math.Max(0, surfL - feetL) + Math.Max(0, feetR - surfR)
            p("超出站立面") = rules.SurfaceOverhang * Math.Min(1, over / surfW)
        End If

        ' 3. 腳下是天空（懸空）。
        Dim sky = If(scene.SkyMask, analysis?.SkyMask)
        If sky IsNot Nothing Then
            Dim skyUnder = 0.0, n = 0
            For k = 0 To 8
                Dim x = CInt(feetL + (feetR - feetL) * k / 8.0)
                Dim y = CInt(placed.Y + Math.Max(2, figH * 0.01))
                If x < 0 OrElse x >= bw OrElse y < 0 OrElse y >= bh Then Continue For
                skyUnder += sky(x, y) : n += 1
            Next
            If n > 0 Then p("腳下懸空") = rules.StandingOnSky * skyUnder / n
        End If

        ' 4. 透視合理性與偏離基準縮放。
        If tr.Perspective IsNot Nothing AndAlso tr.Perspective.Mode = PerspectiveMode.Keystone Then
            p("透視過強") = rules.Perspective * Math.Max(0, Math.Abs(tr.Perspective.KeystoneRatio - 1) - 0.25)
        End If
        p("偏離建議尺寸") = rules.ScaleDeviation * Math.Abs(Math.Log(mult))

        Dim score = 1 - p.Values.Sum()
        Return New PlacementCandidate With {.Transform = tr, .SourceAnchor = srcAnchor, .Score = Math.Max(0, score), .Penalties = p}
    End Function

    ''' <summary>兩條參考線平均方向相對垂直線的角度（度，順時針為正）。</summary>
    Public Shared Function LocalVerticalAngle(a As GuideLine, b As GuideLine) As Double
        Dim angA = VerticalAngle(a), angB = VerticalAngle(b)
        Return Math.Clamp((angA + angB) / 2, -20, 20)
    End Function

    Private Shared Function VerticalAngle(g As GuideLine) As Double
        ' 讓向量朝上（y 遞減），再量相對 (0,-1) 的角度。
        Dim dx = g.X2 - g.X1, dy = g.Y2 - g.Y1
        If dy > 0 Then dx = -dx : dy = -dy
        Return Math.Atan2(dx, -dy) * 180 / Math.PI
    End Function
End Class
