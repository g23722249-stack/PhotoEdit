''' <summary>
''' 服務容器（簡易依賴注入）：所有服務以介面持有，UI 只透過這裡與 <see cref="CompositeWorkflow"/> 取得功能，
''' 不直接執行模型推論或合成計算。測試可以替換任一服務。
''' </summary>
Public NotInheritable Class CompositeServices
    Public Property Transform As ITransformService = New TransformService()
    Public Property ColorMatching As IColorMatchingService = New ColorMatchingService()
    Public Property Shadow As IShadowService = New ShadowService()
    Public Property Occlusion As IOcclusionService = New OcclusionService()
    Public Property MaskRefinement As IMaskRefinementService = New MaskRefinementService()
    Public Property Placement As IAutoPlacementService = New AutoPlacementService()
    Public Property Inpainting As IInpaintingService = New DisabledInpaintingService()
    Public Property Renderer As ICompositeRenderer
    Public Property Export As ExportService
    Public Property Projects As New ProjectSerializer()
    Public Property Images As ImageLoader
    Public Property Models As ModelManager
    ''' <summary>可用的去背服務（依模型 ID）。</summary>
    Public Property BackgroundRemovers As New Dictionary(Of String, IBackgroundRemovalService)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>建立預設服務。codec 為主程式的影像編解碼器（Nothing = 只有內建 PNG）。</summary>
    Public Shared Function Create(codec As IImageCodec, models As ModelManager) As CompositeServices
        Dim s As New CompositeServices With {.Images = New ImageLoader(codec), .Models = models}
        s.Renderer = New CompositeRenderer(s.Transform, s.ColorMatching, s.Shadow, s.Occlusion, s.MaskRefinement)
        s.Export = New ExportService(s.Renderer)
        If models IsNot Nothing Then
            For Each m In models.Manifests.Where(Function(x) x.Purpose = "segmentation")
                Dim id = m.Id
                s.BackgroundRemovers(id) = New OnnxBackgroundRemovalService(m,
                    Function() models.PrimaryModelPath(id),
                    Function() If(models.LoadState(id)?.Commit, ""))
            Next
        End If
        Return s
    End Function

    ''' <summary>釋放所有已載入的模型。</summary>
    Public Sub UnloadModels()
        For Each r In BackgroundRemovers.Values
            r.Unload()
        Next
    End Sub
End Class
