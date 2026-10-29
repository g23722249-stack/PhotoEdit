''' <summary>自動融合的選項。</summary>
Public NotInheritable Class AutoBlendOptions
    Public Property ColorMode As ColorMode = ColorMode.EnvironmentMatch
    ''' <summary>環境匹配強度 0..1。</summary>
    Public Property Strength As Double = 0.6
    Public Property EnableShadow As Boolean = True
End Class

''' <summary>
''' 合成流程協調：載入背景與人物、去背、遮罩精修、自動定位、自動融合、匯出、專案存取。
''' 每個步驟獨立，使用者可以逐步檢視並修改結果（沒有一鍵黑箱）。介面只呼叫這裡的方法。
''' </summary>
Public NotInheritable Class CompositeWorkflow
    Private ReadOnly _svc As CompositeServices

    Public ReadOnly Property Scene As New CompositeScene()
    Public ReadOnly Property History As New SceneHistory()
    Public Property SceneAnalysis As SceneAnalysis
    Public Property Performance As New PerformanceReport()
    Public Property ProjectPath As String = ""

    Public Sub New(services As CompositeServices)
        _svc = services
        History.Clear(Scene)
    End Sub

    Public ReadOnly Property Services As CompositeServices
        Get
            Return _svc
        End Get
    End Property

    ''' <summary>目前選取（或最上層）的人物圖層。</summary>
    Public Property SelectedLayerId As String = ""

    Public ReadOnly Property SelectedLayer As CompositeLayer
        Get
            If Scene.Layers.Count = 0 Then Return Nothing
            Return If(Scene.Layers.FirstOrDefault(Function(l) l.Id = SelectedLayerId), Scene.Layers(Scene.Layers.Count - 1))
        End Get
    End Property

    ' ---------------- 載入 ----------------

    Public Function LoadBackground(path As String) As ImageInfo
        Dim d = Performance.Measure("載入背景", Function() _svc.Images.Load(path))
        Scene.BackgroundPath = path
        Scene.Background = d.Image
        Scene.BackgroundInfo = d.Info
        Scene.BackgroundWidth = d.Image.Width : Scene.BackgroundHeight = d.Image.Height
        ' 背景換了：與舊背景尺寸相關的遮罩全部清掉。
        Scene.ForegroundMask = Nothing : Scene.ProtectMask = Nothing : Scene.ShadowReceiverMask = Nothing
        For Each l In Scene.Layers
            l.OcclusionMask = Nothing
        Next
        Scene.Target = New PlacementTarget()
        Scene.Guides.Clear()
        AnalyzeBackground()
        _svc.Renderer.InvalidateCaches()
        History.Record(Scene, "載入背景")
        AppLog.Info($"背景：{d.Info.Describe()}")
        Return d.Info
    End Function

    ''' <summary>重新分析背景（天空、推薦站立點）。</summary>
    Public Sub AnalyzeBackground()
        If Scene.Background Is Nothing Then Return
        SceneAnalysis = Performance.Measure("背景分析", Function() SceneAnalyzer.Analyze(Scene.Background))
        Scene.SkyMask = SceneAnalysis.SkyMask
    End Sub

    ''' <summary>加入人物圖層。原圖有透明區域時直接用其 Alpha（不需要再去背）。</summary>
    Public Function AddFigure(path As String) As CompositeLayer
        Dim d = Performance.Measure("載入人物", Function() _svc.Images.Load(path))
        Dim layer As New CompositeLayer With {.Name = IO.Path.GetFileNameWithoutExtension(path), .SourcePath = path, .Source = d.Image, .SourceInfo = d.Info, .SourceWidth = d.Image.Width, .SourceHeight = d.Image.Height}
        If SourceAlphaSegmentation.HasUsableAlpha(d.Image) Then
            layer.AiAlpha = d.Image.ExtractAlpha()
            layer.Alpha = layer.AiAlpha.Clone()
            layer.MaskModelId = SourceAlphaSegmentation.ModelId
        Else
            ' 尚未去背：暫時整張不透明，提示使用者執行自動去背。
            layer.Alpha = FloatMask.Filled(d.Image.Width, d.Image.Height, 1)
            layer.MaskModelId = ""
        End If
        layer.Touch()
        ResetAnchor(layer)
        PlaceDefault(layer)
        Scene.Layers.Add(layer)
        SelectedLayerId = layer.Id
        History.Record(Scene, "加入人物")
        AppLog.Info($"人物：{d.Info.Describe()}")
        Return layer
    End Function

    ''' <summary>替換人物原圖（保留位置參數）。</summary>
    Public Sub ReplaceFigureSource(layer As CompositeLayer, path As String)
        Dim d = _svc.Images.Load(path)
        layer.SourcePath = path
        layer.Source = d.Image
        layer.SourceInfo = d.Info
        layer.AiAlpha = If(SourceAlphaSegmentation.HasUsableAlpha(d.Image), d.Image.ExtractAlpha(), Nothing)
        layer.Alpha = If(layer.AiAlpha?.Clone(), FloatMask.Filled(d.Image.Width, d.Image.Height, 1))
        layer.MaskModelId = If(layer.AiAlpha IsNot Nothing, SourceAlphaSegmentation.ModelId, "")
        layer.Touch()
        ResetAnchor(layer)
        History.Record(Scene, "替換人物圖")
    End Sub

    Public Sub RemoveLayer(layer As CompositeLayer)
        Scene.Layers.Remove(layer)
        History.Record(Scene, "刪除圖層")
    End Sub

    Public Sub MoveLayer(layer As CompositeLayer, delta As Integer)
        Dim i = Scene.Layers.IndexOf(layer)
        Dim j = Math.Clamp(i + delta, 0, Scene.Layers.Count - 1)
        If i < 0 OrElse i = j Then Return
        Scene.Layers.RemoveAt(i)
        Scene.Layers.Insert(j, layer)
        History.Record(Scene, "調整圖層順序")
    End Sub

    ''' <summary>依錨點種類重新計算人物錨點（SingleFoot／Custom 保留使用者指定值）。</summary>
    Public Sub ResetAnchor(layer As CompositeLayer)
        If layer.AnchorKind = AnchorKind.SingleFoot OrElse layer.AnchorKind = AnchorKind.Custom Then Return
        Try
            Dim fig = FigureAnalyzer.Analyze(layer.Alpha)
            Dim a = fig.AnchorFor(layer.AnchorKind)
            layer.AnchorX = a.X : layer.AnchorY = a.Y
        Catch ex As InvalidOperationException
            layer.AnchorX = layer.Source.Width / 2.0 : layer.AnchorY = layer.Source.Height
        End Try
    End Sub

    ''' <summary>還沒自動定位前的預設位置：畫面下方中央，高度約畫面 40%。</summary>
    Private Sub PlaceDefault(layer As CompositeLayer)
        If Scene.Background Is Nothing Then Return
        Dim h = layer.Source.Height
        Try
            h = FigureAnalyzer.Analyze(layer.Alpha).Bounds.Height
        Catch ex As InvalidOperationException
        End Try
        layer.Transform = New LayerTransform With {.X = Scene.Width / 2.0, .Y = Scene.Height * 0.9, .Scale = Scene.Height * 0.4 / Math.Max(1, h)}
    End Sub

    ' ---------------- 去背 ----------------

    Public Async Function AutoCutoutAsync(layer As CompositeLayer, modelId As String, options As SegmentationOptions, refine As MaskRefineOptions,
                                          progress As IProgress(Of String), cancel As Threading.CancellationToken) As Task(Of SegmentationResult)
        Dim svc As IBackgroundRemovalService = Nothing
        If Not _svc.BackgroundRemovers.TryGetValue(modelId, svc) Then Throw New KeyNotFoundException($"沒有去背模型 {modelId}。")
        If Not svc.IsAvailable() Then Throw New IO.FileNotFoundException($"{svc.DisplayName} 尚未安裝，請到「模型管理」下載。")
        Dim sw = Stopwatch.StartNew()
        Dim r = Await svc.RemoveBackgroundAsync(layer.Source, options, progress, cancel).ConfigureAwait(True)
        Performance.Add("AI 去背", sw.Elapsed.TotalMilliseconds, $"{modelId}，{r.Provider}")
        layer.AiAlpha = r.Mask
        layer.Alpha = Await Task.Run(Function() _svc.MaskRefinement.Refine(r.Mask, refine), cancel).ConfigureAwait(True)
        layer.MaskModelId = r.ModelId
        layer.MaskModelRevision = r.ModelRevision
        layer.Touch()
        ResetAnchor(layer)
        History.Record(Scene, "自動去背")
        Return r
    End Function

    ''' <summary>以新的精修設定重新從 AI 原始遮罩計算。</summary>
    Public Sub ReRefine(layer As CompositeLayer, refine As MaskRefineOptions)
        If layer.AiAlpha Is Nothing Then Return
        layer.Alpha = _svc.MaskRefinement.Refine(layer.AiAlpha, refine)
        layer.Touch()
        ResetAnchor(layer)
        History.Record(Scene, "遮罩精修")
    End Sub

    ''' <summary>還原成 AI 原始遮罩。</summary>
    Public Sub RestoreAiMask(layer As CompositeLayer)
        If layer.AiAlpha Is Nothing Then Return
        layer.Alpha = layer.AiAlpha.Clone()
        layer.Touch()
        ResetAnchor(layer)
        History.Record(Scene, "還原遮罩")
    End Sub

    ''' <summary>人物遮罩筆刷修正（先複製再修改，讓復原可以取回舊遮罩）。</summary>
    Public Sub PaintFigureMask(layer As CompositeLayer, stroke As BrushStroke)
        Dim m = layer.Alpha.Clone()
        _svc.MaskRefinement.PaintCorrection(m, stroke)
        layer.Alpha = m
        layer.Touch()
        History.Record(Scene, If(stroke.Mode = BrushMode.Add, "筆刷保留", "筆刷移除"))
    End Sub

    Public Sub FeatherFigureMask(layer As CompositeLayer, sigma As Double)
        layer.Alpha = _svc.MaskRefinement.Feather(layer.Alpha, sigma)
        layer.Touch()
        History.Record(Scene, "邊緣羽化")
    End Sub

    ' ---------------- 遮擋／保護／陰影接收 ----------------

    Public Enum SceneMaskKind
        Foreground
        Protect
        ShadowReceiver
        LayerOcclusion
    End Enum

    Public Sub PaintSceneMask(kind As SceneMaskKind, stroke As BrushStroke)
        If Scene.Background Is Nothing Then Return
        Dim current As FloatMask
        Select Case kind
            Case SceneMaskKind.Foreground : current = Scene.ForegroundMask
            Case SceneMaskKind.Protect : current = Scene.ProtectMask
            Case SceneMaskKind.ShadowReceiver : current = Scene.ShadowReceiverMask
            Case Else : current = SelectedLayer?.OcclusionMask
        End Select
        Dim m = If(current?.Clone(), New FloatMask(Scene.Width, Scene.Height))
        _svc.Occlusion.Paint(m, stroke)
        Select Case kind
            Case SceneMaskKind.Foreground : Scene.ForegroundMask = m
            Case SceneMaskKind.Protect : Scene.ProtectMask = m
            Case SceneMaskKind.ShadowReceiver : Scene.ShadowReceiverMask = m
            Case Else
                If SelectedLayer Is Nothing Then Return
                SelectedLayer.OcclusionMask = m
        End Select
        Scene.TouchMasks()
        History.Record(Scene, "繪製遮罩")
    End Sub

    Public Sub ClearSceneMask(kind As SceneMaskKind)
        Select Case kind
            Case SceneMaskKind.Foreground : Scene.ForegroundMask = Nothing
            Case SceneMaskKind.Protect : Scene.ProtectMask = Nothing
            Case SceneMaskKind.ShadowReceiver : Scene.ShadowReceiverMask = Nothing
            Case Else
                If SelectedLayer IsNot Nothing Then SelectedLayer.OcclusionMask = Nothing
        End Select
        Scene.TouchMasks()
        History.Record(Scene, "清除遮罩")
    End Sub

    ' ---------------- 自動定位 ----------------

    Public Function SuggestPlacements(layer As CompositeLayer, Optional rules As PlacementRules = Nothing) As List(Of PlacementCandidate)
        If SceneAnalysis Is Nothing Then AnalyzeBackground()
        Return Performance.Measure("自動定位", Function() _svc.Placement.Suggest(Scene, layer, SceneAnalysis, rules))
    End Function

    Public Sub ApplyPlacement(layer As CompositeLayer, c As PlacementCandidate)
        layer.AnchorX = c.SourceAnchor.X : layer.AnchorY = c.SourceAnchor.Y
        layer.Transform = c.Transform.Clone()
        ' 從天際線推薦的位置（柱頂、屋頂）：陰影只落在非天空區域。
        Dim fromSky = TargetFromSkyline()
        If fromSky AndAlso layer.Shadow.Receiver = ShadowReceiver.Everywhere Then layer.Shadow.Receiver = ShadowReceiver.NonSky
        History.Record(Scene, "套用自動定位")
    End Sub

    ''' <summary>目前的放置目標來自天際線推薦（使用者沒有自行指定目標錨點）。</summary>
    Private Function TargetFromSkyline() As Boolean
        If Scene.Target.HasAnchor OrElse SceneAnalysis Is Nothing OrElse SceneAnalysis.Suggestions.Count = 0 Then Return False
        Return SceneAnalysis.Suggestions(0).FromSkyline
    End Function

    ''' <summary>一般的參數修改（拖曳、數值輸入）完成後記錄一筆復原點。</summary>
    Public Sub CommitEdit(label As String)
        History.Record(Scene, label)
    End Sub

    ' ---------------- 自動融合 ----------------

    ''' <summary>依放置處附近的背景推薦色彩與陰影參數並套用。</summary>
    Public Sub AutoBlend(layer As CompositeLayer, options As AutoBlendOptions)
        options = If(options, New AutoBlendOptions())
        Dim region = PlacementRegion(layer)
        If options.ColorMode = ColorMode.Original Then
            layer.Color = New ColorParams With {.Mode = ColorMode.Original}
        Else
            Dim rec = Performance.Measure("色彩分析", Function() _svc.ColorMatching.Recommend(Scene.Background, region, layer.Source, layer.Alpha, options.Strength))
            rec.Mode = options.ColorMode
            layer.Color = rec
        End If
        If options.EnableShadow Then
            Dim fromSky = layer.Shadow.Receiver = ShadowReceiver.NonSky OrElse TargetFromSkyline()
            Dim sp = _svc.Shadow.Recommend(SceneAnalysis, fromSky)
            ' 模糊依人物在畫面上的大小調整。
            Dim figH = 0.0
            Try
                figH = _svc.Renderer.GetFigureAnalysis(layer).Height * layer.Transform.Scale
            Catch ex As InvalidOperationException
            End Try
            If figH > 0 Then sp.BlurRadius = Math.Clamp(figH * 0.012, 1.5, 24)
            layer.Shadow = sp
        Else
            layer.Shadow.Enabled = False
        End If
        History.Record(Scene, "自動融合")
    End Sub

    ''' <summary>人物在背景上的外框，外擴 40%（色彩分析取樣範圍）。</summary>
    Public Function PlacementRegion(layer As CompositeLayer) As PixelRect
        Dim m = _svc.Transform.BuildMatrix(layer)
        Dim fig = _svc.Renderer.GetFigureAnalysis(layer)
        Dim b = fig.Bounds
        Dim pts = {m.Transform(New PointD(b.X, b.Y)), m.Transform(New PointD(b.Right, b.Y)), m.Transform(New PointD(b.Right, b.Bottom)), m.Transform(New PointD(b.X, b.Bottom))}
        Dim minX = pts.Min(Function(p) p.X), maxX = pts.Max(Function(p) p.X), minY = pts.Min(Function(p) p.Y), maxY = pts.Max(Function(p) p.Y)
        Dim padX = (maxX - minX) * 0.4, padY = (maxY - minY) * 0.4
        Return New PixelRect(CInt(minX - padX), CInt(minY - padY), CInt(maxX - minX + 2 * padX), CInt(maxY - minY + 2 * padY)).Intersect(New PixelRect(0, 0, Scene.Width, Scene.Height))
    End Function

    ' ---------------- 專案 ----------------

    Public Sub SaveProject(path As String)
        _svc.Projects.Save(Scene, path)
        ProjectPath = path
    End Sub

    ''' <summary>讀專案；有遺失檔案時回傳清單（場景尚未載入影像），呼叫端讓使用者重新指定後再呼叫 <see cref="FinishLoadProject"/>。</summary>
    Public Function OpenProject(path As String, ByRef pending As CompositeScene) As List(Of MissingFile)
        Dim missing As List(Of MissingFile) = Nothing
        pending = _svc.Projects.Load(path, missing)
        Return missing
    End Function

    Public Function FinishLoadProject(pending As CompositeScene, path As String) As List(Of String)
        Dim warnings As New List(Of String)()
        ProjectSerializer.LoadImages(pending, _svc.Images, warnings)
        CopyScene(pending, Scene)
        ProjectPath = path
        AnalyzeBackground()
        _svc.Renderer.InvalidateCaches()
        SelectedLayerId = If(Scene.Layers.LastOrDefault()?.Id, "")
        History.Clear(Scene, "開啟專案")
        Return warnings
    End Function

    Private Shared Sub CopyScene(src As CompositeScene, dst As CompositeScene)
        dst.BackgroundPath = src.BackgroundPath : dst.Background = src.Background : dst.BackgroundInfo = src.BackgroundInfo
        dst.Layers = src.Layers
        dst.ForegroundMaskPath = src.ForegroundMaskPath : dst.ForegroundMask = src.ForegroundMask
        dst.ProtectMaskPath = src.ProtectMaskPath : dst.ProtectMask = src.ProtectMask
        dst.ShadowReceiverMaskPath = src.ShadowReceiverMaskPath : dst.ShadowReceiverMask = src.ShadowReceiverMask
        dst.OcclusionFeather = src.OcclusionFeather
        dst.Target = src.Target : dst.Guides = src.Guides : dst.Export = src.Export
        dst.TouchMasks()
    End Sub

    ''' <summary>新專案：清空場景。</summary>
    Public Sub NewProject()
        CopyScene(New CompositeScene(), Scene)
        Scene.Background = Nothing
        SceneAnalysis = Nothing
        ProjectPath = ""
        SelectedLayerId = ""
        _svc.Renderer.InvalidateCaches()
        History.Clear(Scene)
    End Sub

    Public Function ExportImage(path As String, format As ExportFormat, jpegQuality As Integer, cancel As Threading.CancellationToken) As ExportReport
        Dim r = _svc.Export.Export(Scene, path, format, jpegQuality, _svc.Images.Primary, cancel)
        Performance.Add("正式輸出", r.Milliseconds, $"{r.Width}×{r.Height}")
        Return r
    End Function
End Class
