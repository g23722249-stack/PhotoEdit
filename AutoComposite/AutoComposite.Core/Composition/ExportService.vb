Imports System.IO

''' <summary>匯出結果與處理紀錄。</summary>
Public NotInheritable Class ExportReport
    Public Property OutputPath As String = ""
    Public Property Width As Integer
    Public Property Height As Integer
    Public Property Milliseconds As Double
    Public Property Timings As New Dictionary(Of String, Double)()
    Public Property ModelsUsed As New List(Of String)()

    Public Function ToText() As String
        Dim sb As New Text.StringBuilder()
        sb.AppendLine($"匯出 {OutputPath}（{Width}×{Height}），共 {Milliseconds:0} ms")
        For Each kv In Timings
            sb.AppendLine($"  {kv.Key}: {kv.Value:0.0} ms")
        Next
        If ModelsUsed.Count > 0 Then sb.AppendLine("  使用模型：" & String.Join("、", ModelsUsed))
        Return sb.ToString()
    End Function
End Class

''' <summary>
''' 正式輸出：以背景原尺寸、雙三次重採樣從原圖重新合成（不使用預覽縮圖放大），存成 PNG 或 JPEG。
''' 背景有透明區域時 PNG 保留 Alpha；JPEG 由主程式的 WIC 編碼器處理。
''' </summary>
Public NotInheritable Class ExportService
    Private ReadOnly _renderer As ICompositeRenderer

    Public Sub New(renderer As ICompositeRenderer)
        _renderer = renderer
    End Sub

    Public Function RenderFinal(scene As CompositeScene, cancel As Threading.CancellationToken) As RenderResult
        Return _renderer.Render(scene, New RenderOptions With {.MaxSide = 0, .Quality = ResampleQuality.Bicubic, .Mode = PreviewMode.Final, .IncludeShadows = True}, cancel)
    End Function

    Public Function Export(scene As CompositeScene, outputPath As String, format As ExportFormat, jpegQuality As Integer, codec As IImageCodec, cancel As Threading.CancellationToken) As ExportReport
        Dim sw = Stopwatch.StartNew()
        Dim estimated = ImageLimits.EstimateCompositeBytes(scene.Width, scene.Height, scene.Layers.Sum(Function(l) If(l.Source Is Nothing, 0L, CLng(l.Source.Width) * l.Source.Height)))
        AppLog.Info($"開始匯出 {scene.Width}×{scene.Height}，估計需要 {estimated / 1048576.0:0} MB 記憶體。")
        Dim r = RenderFinal(scene, cancel)
        cancel.ThrowIfCancellationRequested()
        If format = ExportFormat.Png Then
            PngCodec.WriteAtomically(outputPath, Sub(s) PngCodec.EncodeRgba(r.Image, s, Not r.Image.HasTransparency()))
        Else
            If codec Is Nothing OrElse TypeOf codec Is PngCodec Then Throw New NotSupportedException("JPEG 匯出需要主程式的 WIC 編碼器。")
            codec.Encode(r.Image, outputPath, ExportFormat.Jpeg, Math.Clamp(jpegQuality, 1, 100))
        End If
        Dim rep As New ExportReport With {.OutputPath = outputPath, .Width = r.Image.Width, .Height = r.Image.Height, .Milliseconds = sw.Elapsed.TotalMilliseconds, .Timings = r.Timings}
        For Each l In scene.Layers
            If Not String.IsNullOrEmpty(l.MaskModelId) Then rep.ModelsUsed.Add($"{l.Name}: {l.MaskModelId}@{If(String.IsNullOrEmpty(l.MaskModelRevision), "-", l.MaskModelRevision)}")
        Next
        scene.Export.LastExportPath = outputPath
        AppLog.Info(rep.ToText())
        Return rep
    End Function
End Class
