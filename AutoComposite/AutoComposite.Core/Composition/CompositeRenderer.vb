Imports System.Diagnostics

''' <summary>預覽模式（讓使用者逐步檢視自動合成的每一步）。</summary>
Public Enum PreviewMode
    ''' <summary>原始背景。</summary>
    Background
    ''' <summary>去背後的人物（棋盤格底）。</summary>
    CutoutFigure
    ''' <summary>Alpha 遮罩灰階。</summary>
    AlphaMask
    ''' <summary>變換後的人物（棋盤格底，背景座標）。</summary>
    Transformed
    ''' <summary>背景＋陰影（不含人物）。</summary>
    Shadow
    ''' <summary>最終合成。</summary>
    Final
End Enum

Public NotInheritable Class RenderOptions
    ''' <summary>輸出長邊上限；0 = 背景原尺寸（正式輸出必須用 0）。</summary>
    Public Property MaxSide As Integer
    Public Property Quality As ResampleQuality = ResampleQuality.Bicubic
    Public Property Mode As PreviewMode = PreviewMode.Final
    Public Property IncludeShadows As Boolean = True
    ''' <summary>CutoutFigure／AlphaMask 模式顯示哪個圖層；空字串 = 最上層。</summary>
    Public Property LayerId As String = ""
    ''' <summary>合成前對人物邊緣去色污染（避免白邊、黑邊）。</summary>
    Public Property DecontaminateEdges As Boolean = True
End Class

Public NotInheritable Class RenderResult
    Public Property Image As RgbaImage
    ''' <summary>輸出相對背景原圖的縮放（x、y）。</summary>
    Public Property ScaleX As Double = 1
    Public Property ScaleY As Double = 1
    Public Property Timings As New Dictionary(Of String, Double)()
End Class

Public Interface ICompositeRenderer
    Function Render(scene As CompositeScene, options As RenderOptions, Optional cancel As Threading.CancellationToken = Nothing) As RenderResult
    Function GetFigureAnalysis(layer As CompositeLayer) As FigureAnalysis
    Sub InvalidateCaches()
End Interface

''' <summary>
''' 合成渲染：背景 → 每個圖層（陰影 → 變換 → 景深 → 遮擋 → 不透明度 → Alpha 合成）。
''' 圖層以預乘 Alpha 合成，人物 Alpha 為 0 的像素完全保留背景原值；陰影只動到陰影濃度大於 0 的像素。
''' 預覽用代理解析度（MaxSide），正式輸出以背景原尺寸從原圖重新計算，不放大預覽。
''' </summary>
Public NotInheritable Class CompositeRenderer
    Implements ICompositeRenderer

    Private ReadOnly _transform As ITransformService
    Private ReadOnly _color As IColorMatchingService
    Private ReadOnly _shadow As IShadowService
    Private ReadOnly _occlusion As IOcclusionService
    Private ReadOnly _refine As IMaskRefinementService

    Private ReadOnly _lock As New Object()
    Private ReadOnly _samplers As New Dictionary(Of String, LayerSampler)()
    Private ReadOnly _analyses As New Dictionary(Of String, FigureAnalysis)()
    Private _proxyKey As String = ""
    Private _proxy As RgbaImage
    Private _skyKey As String = ""
    Private _skyCanvas As FloatMask

    Public Sub New(transform As ITransformService, color As IColorMatchingService, shadow As IShadowService, occlusion As IOcclusionService, refine As IMaskRefinementService)
        _transform = transform : _color = color : _shadow = shadow : _occlusion = occlusion : _refine = refine
    End Sub

    Public Shared Function CreateDefault() As CompositeRenderer
        Return New CompositeRenderer(New TransformService(), New ColorMatchingService(), New ShadowService(), New OcclusionService(), New MaskRefinementService())
    End Function

    Public Sub InvalidateCaches() Implements ICompositeRenderer.InvalidateCaches
        SyncLock _lock
            _samplers.Clear() : _analyses.Clear() : _proxy = Nothing : _proxyKey = "" : _skyCanvas = Nothing : _skyKey = ""
        End SyncLock
        TryCast(_occlusion, OcclusionService)?.ClearCache()
    End Sub

    Public Function GetFigureAnalysis(layer As CompositeLayer) As FigureAnalysis Implements ICompositeRenderer.GetFigureAnalysis
        Dim key = $"{layer.Id}|{layer.ContentVersion}"
        SyncLock _lock
            Dim a As FigureAnalysis = Nothing
            If _analyses.TryGetValue(key, a) Then Return a
        End SyncLock
        Dim r = FigureAnalyzer.Analyze(layer.Alpha)
        SyncLock _lock
            _analyses(key) = r
        End SyncLock
        Return r
    End Function

    ''' <summary>色彩調整與去色污染後的取樣器（依內容版本與色彩參數快取）。</summary>
    Private Function GetSampler(layer As CompositeLayer, decontaminate As Boolean) As LayerSampler
        Dim c = layer.Color
        Dim key = $"{layer.Id}|{layer.ContentVersion}|{decontaminate}|{c.Mode}|{c.Brightness}|{c.Contrast}|{c.Saturation}|{c.Temperature}|{c.Tint}|{c.GainR}|{c.GainG}|{c.GainB}|{c.Gamma}|{c.Strength}"
        SyncLock _lock
            Dim s As LayerSampler = Nothing
            If _samplers.TryGetValue(key, s) Then Return s
        End SyncLock
        Dim rgb = layer.Source
        If decontaminate Then rgb = _refine.DecontaminateEdges(rgb, layer.Alpha)
        rgb = _color.Apply(rgb, c)
        Dim sampler As New LayerSampler(rgb, layer.Alpha)
        SyncLock _lock
            ' 只保留目前圖層集合最新的取樣器，避免調色時無限累積。
            For Each k In _samplers.Keys.Where(Function(x) x.StartsWith(layer.Id & "|")).ToList()
                _samplers.Remove(k)
            Next
            _samplers(key) = sampler
        End SyncLock
        Return sampler
    End Function

    Private Function GetCanvasBackground(scene As CompositeScene, maxSide As Integer) As RgbaImage
        If maxSide <= 0 OrElse Math.Max(scene.Width, scene.Height) <= maxSide Then Return scene.Background
        Dim key = $"{Runtime.CompilerServices.RuntimeHelpers.GetHashCode(scene.Background)}|{maxSide}"
        SyncLock _lock
            If _proxyKey = key AndAlso _proxy IsNot Nothing Then Return _proxy
        End SyncLock
        Dim s As Double
        Dim p = scene.Background.Downscale(maxSide, s)
        SyncLock _lock
            _proxy = p : _proxyKey = key
        End SyncLock
        Return p
    End Function

    Public Function Render(scene As CompositeScene, options As RenderOptions, Optional cancel As Threading.CancellationToken = Nothing) As RenderResult Implements ICompositeRenderer.Render
        If scene?.Background Is Nothing Then Throw New InvalidOperationException("尚未載入背景照片。")
        options = If(options, New RenderOptions())
        Dim result As New RenderResult()
        Dim sw = Stopwatch.StartNew()

        If options.Mode = PreviewMode.CutoutFigure OrElse options.Mode = PreviewMode.AlphaMask Then
            Dim layer = FindLayer(scene, options.LayerId)
            If layer Is Nothing OrElse layer.Source Is Nothing Then
                result.Image = scene.Background
                Return result
            End If
            result.Image = RenderFigureView(layer, options)
            result.Timings("figure") = sw.Elapsed.TotalMilliseconds
            Return result
        End If

        Dim bg = GetCanvasBackground(scene, options.MaxSide)
        Dim cw = bg.Width, ch = bg.Height
        Dim sx = cw / CDbl(scene.Width), sy = ch / CDbl(scene.Height)
        result.ScaleX = sx : result.ScaleY = sy
        Dim canvasM = Matrix3.Scaling(sx, sy)
        Dim renderScale = Math.Sqrt(sx * sy)
        Dim transformedOnly = options.Mode = PreviewMode.Transformed
        Dim canvas = If(transformedOnly, Checkerboard(cw, ch), bg.Clone())
        result.Timings("background") = sw.Elapsed.TotalMilliseconds
        If options.Mode = PreviewMode.Background Then
            result.Image = canvas
            Return result
        End If

        For Each layer In scene.Layers
            cancel.ThrowIfCancellationRequested()
            If Not layer.Visible OrElse layer.Source Is Nothing OrElse layer.Alpha Is Nothing Then Continue For
            Dim t0 = sw.Elapsed.TotalMilliseconds
            Dim layerM = _transform.BuildMatrix(layer)
            layer.MatrixSnapshot = layerM.ToArray()
            Dim m = canvasM * layerM
            Dim sampler = GetSampler(layer, options.DecontaminateEdges)
            Dim occ = If(transformedOnly, Nothing, _occlusion.EffectiveOcclusion(scene, layer, cw, ch))

            ' 陰影。
            If Not transformedOnly AndAlso options.IncludeShadows AndAlso layer.Shadow IsNot Nothing AndAlso layer.Shadow.Enabled Then
                Dim fig = GetFigureAnalysis(layer)
                Dim receiver = ShadowReceiverMask(scene, layer.Shadow.Receiver, cw, ch)
                Dim st = _shadow.Render(sampler, m, fig, layer.Anchor, layer.Shadow, cw, ch, renderScale, receiver, cancel)
                ApplyShadow(canvas, st, occ, layer.Shadow)
            End If
            result.Timings($"shadow:{layer.Name}") = sw.Elapsed.TotalMilliseconds - t0
            If options.Mode = PreviewMode.Shadow Then Continue For

            ' 圖層變換＋景深＋遮擋＋合成。
            Dim t1 = sw.Elapsed.TotalMilliseconds
            Dim tile = _transform.RenderLayer(sampler, m, cw, ch, options.Quality, cancel)
            Dim blurSigma = If(layer.Color.Mode = ColorMode.Original, 0, layer.Color.DepthBlur) * renderScale
            If blurSigma > 0.3 AndAlso Not tile.Bounds.IsEmpty Then tile = BlurTile(tile, blurSigma, cw, ch)
            CompositeTile(canvas, tile, occ, CSng(Math.Clamp(layer.Opacity, 0, 1)))
            result.Timings($"layer:{layer.Name}") = sw.Elapsed.TotalMilliseconds - t1
        Next
        result.Image = canvas
        result.Timings("total") = sw.Elapsed.TotalMilliseconds
        Return result
    End Function

    Private Shared Function FindLayer(scene As CompositeScene, id As String) As CompositeLayer
        If scene.Layers.Count = 0 Then Return Nothing
        If String.IsNullOrEmpty(id) Then Return scene.Layers(scene.Layers.Count - 1)
        Return If(scene.Layers.FirstOrDefault(Function(l) l.Id = id), scene.Layers(scene.Layers.Count - 1))
    End Function

    Private Function RenderFigureView(layer As CompositeLayer, options As RenderOptions) As RgbaImage
        Dim src = layer.Source, a = layer.Alpha
        Dim img As RgbaImage
        If options.Mode = PreviewMode.AlphaMask Then
            img = New RgbaImage(src.Width, src.Height)
            For i = 0 To a.Data.Length - 1
                Dim v = RgbaImage.ToByte(a.Data(i))
                img.Pixels(i * 4) = v : img.Pixels(i * 4 + 1) = v : img.Pixels(i * 4 + 2) = v : img.Pixels(i * 4 + 3) = 255
            Next
        Else
            Dim rgb = If(options.DecontaminateEdges, _refine.DecontaminateEdges(src, a), src)
            rgb = _color.Apply(rgb, layer.Color)
            img = Checkerboard(src.Width, src.Height)
            Dim p = img.Pixels, s = rgb.Pixels
            For i = 0 To a.Data.Length - 1
                Dim al = a.Data(i)
                If al <= 0 Then Continue For
                For c = 0 To 2
                    p(i * 4 + c) = RgbaImage.ClampByte(s(i * 4 + c) * al + p(i * 4 + c) * (1 - al))
                Next
            Next
        End If
        If options.MaxSide > 0 Then
            Dim sc As Double
            img = img.Downscale(options.MaxSide, sc)
        End If
        Return img
    End Function

    Private Function ShadowReceiverMask(scene As CompositeScene, mode As ShadowReceiver, cw As Integer, ch As Integer) As FloatMask
        Select Case mode
            Case ShadowReceiver.NonSky
                If scene.SkyMask Is Nothing Then Return Nothing
                Dim key = $"{Runtime.CompilerServices.RuntimeHelpers.GetHashCode(scene.SkyMask)}|{cw}x{ch}"
                SyncLock _lock
                    If _skyKey = key AndAlso _skyCanvas IsNot Nothing Then Return _skyCanvas
                End SyncLock
                Dim sky = If(scene.SkyMask.Width = cw AndAlso scene.SkyMask.Height = ch, scene.SkyMask.Clone(), scene.SkyMask.Resize(cw, ch))
                For i = 0 To sky.Data.Length - 1
                    sky.Data(i) = 1 - sky.Data(i)
                Next
                SyncLock _lock
                    _skyCanvas = sky : _skyKey = key
                End SyncLock
                Return sky
            Case ShadowReceiver.CustomMask
                Dim m = scene.ShadowReceiverMask
                If m Is Nothing Then Return Nothing
                Return If(m.Width = cw AndAlso m.Height = ch, m, m.Resize(cw, ch))
            Case Else
                Return Nothing
        End Select
    End Function

    ''' <summary>陰影以「乘上陰影色」方式壓暗背景，保留背景紋理；被前景物件擋住的部分不畫。</summary>
    Public Shared Sub ApplyShadow(canvas As RgbaImage, tile As ShadowTile, occ As FloatMask, p As ShadowParams)
        If tile.Bounds.IsEmpty Then Return
        Dim r = tile.Bounds, w = canvas.Width
        Dim cr = p.ColorR / 255.0F, cg = p.ColorG / 255.0F, cb = p.ColorB / 255.0F
        Dim px = canvas.Pixels, d = tile.Data
        For ty = 0 To r.Height - 1
            For tx = 0 To r.Width - 1
                Dim s = d(ty * r.Width + tx)
                If s <= 0.0005F Then Continue For
                Dim gx = r.X + tx, gy = r.Y + ty
                If occ IsNot Nothing Then s *= 1 - occ.Data(gy * occ.Width + gx)
                If s <= 0.0005F Then Continue For
                Dim i = (gy * w + gx) * 4
                px(i) = RgbaImage.ClampByte(px(i) * (1 - s * (1 - cr)))
                px(i + 1) = RgbaImage.ClampByte(px(i + 1) * (1 - s * (1 - cg)))
                px(i + 2) = RgbaImage.ClampByte(px(i + 2) * (1 - s * (1 - cb)))
            Next
        Next
    End Sub

    ''' <summary>
    ''' 預乘圖層「蓋在上面」（Porter-Duff over）。背景可以有透明度；圖層覆蓋量為 0 的像素完全不動。
    ''' </summary>
    Public Shared Sub CompositeTile(canvas As RgbaImage, tile As PremultipliedTile, occ As FloatMask, opacity As Single)
        If tile.Bounds.IsEmpty OrElse opacity <= 0 Then Return
        Dim r = tile.Bounds, w = canvas.Width
        Dim px = canvas.Pixels, d = tile.Data
        Threading.Tasks.Parallel.For(0, r.Height,
            Sub(ty)
                For tx = 0 To r.Width - 1
                    Dim o = (ty * r.Width + tx) * 4
                    Dim k = opacity
                    Dim gx = r.X + tx, gy = r.Y + ty
                    If occ IsNot Nothing Then k *= 1 - occ.Data(gy * occ.Width + gx)
                    Dim a = d(o + 3) * k
                    If a <= 0 Then Continue For
                    Dim i = (gy * w + gx) * 4
                    Dim ba = px(i + 3) / 255.0F
                    Dim outA = a + ba * (1 - a)
                    If outA <= 0 Then Continue For
                    Dim keep = ba * (1 - a)
                    For c = 0 To 2
                        Dim v = (d(o + c) * k * 255.0F + px(i + c) * keep) / outA
                        px(i + c) = RgbaImage.ClampByte(v)
                    Next
                    px(i + 3) = RgbaImage.ToByte(outA)
                Next
            End Sub)
    End Sub

    ' 景深模糊：先把區塊外擴（模糊會往外暈開），在預乘空間模糊。
    Private Shared Function BlurTile(tile As PremultipliedTile, sigma As Double, cw As Integer, ch As Integer) As PremultipliedTile
        Dim pad = CInt(Math.Ceiling(sigma * 3))
        Dim nb = tile.Bounds.Inflate(pad).Intersect(New PixelRect(0, 0, cw, ch))
        Dim big As New PremultipliedTile(nb)
        Dim ob = tile.Bounds
        For y = 0 To ob.Height - 1
            Array.Copy(tile.Data, y * ob.Width * 4, big.Data, ((ob.Y - nb.Y + y) * nb.Width + (ob.X - nb.X)) * 4, ob.Width * 4)
        Next
        Blur.GaussianInPlace(big.Data, nb.Width, nb.Height, 4, sigma)
        Return big
    End Function

    ''' <summary>透明區域的棋盤格底。</summary>
    Public Shared Function Checkerboard(w As Integer, h As Integer, Optional cell As Integer = 12) As RgbaImage
        Dim img As New RgbaImage(w, h)
        Dim p = img.Pixels
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim v As Byte = If(((x \ cell) + (y \ cell)) Mod 2 = 0, CByte(204), CByte(170))
                Dim i = (y * w + x) * 4
                p(i) = v : p(i + 1) = v : p(i + 2) = v : p(i + 3) = 255
            Next
        Next
        Return img
    End Function
End Class
