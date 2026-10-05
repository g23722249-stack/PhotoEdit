''' <summary>裁切範圍，以「拉直後影像」的寬高為 1 的相對座標表示，預覽圖與原圖共用同一組數值。</summary>
Public Class CropRect
    Public Property X As Double
    Public Property Y As Double
    Public Property Width As Double = 1
    Public Property Height As Double = 1

    Public Sub New()
    End Sub

    Public Sub New(x As Double, y As Double, width As Double, height As Double)
        Me.X = x : Me.Y = y : Me.Width = width : Me.Height = height
    End Sub

    Public Function Clone() As CropRect
        Return New CropRect(X, Y, Width, Height)
    End Function

    ''' <summary>夾回 0..1 範圍內，並保證至少 1% 寬高。</summary>
    Public Function Normalized() As CropRect
        Dim w = Math.Max(0.01, Math.Min(1, Width))
        Dim h = Math.Max(0.01, Math.Min(1, Height))
        Dim nx = Math.Max(0, Math.Min(1 - w, X))
        Dim ny = Math.Max(0, Math.Min(1 - h, Y))
        Return New CropRect(nx, ny, w, h)
    End Function

    ''' <summary>在 imageWidth × imageHeight 的影像中，置中且最大的指定寬高比（像素）裁切框。</summary>
    Public Shared Function CenteredForAspect(aspectRatio As Double, imageWidth As Integer, imageHeight As Integer) As CropRect
        If aspectRatio <= 0 OrElse imageWidth <= 0 OrElse imageHeight <= 0 Then Return New CropRect()
        Dim w As Double = 1, h As Double = 1
        If imageWidth / imageHeight > aspectRatio Then
            w = imageHeight * aspectRatio / imageWidth
        Else
            h = imageWidth / aspectRatio / imageHeight
        End If
        Return New CropRect((1 - w) / 2, (1 - h) / 2, w, h)
    End Function

    Public ReadOnly Property IsFull As Boolean
        Get
            Return X <= 0.0001 AndAlso Y <= 0.0001 AndAlso Width >= 0.9999 AndAlso Height >= 0.9999
        End Get
    End Property
End Class

''' <summary>
''' 非破壞性編輯配方：只記錄調整參數，原始照片永不覆寫。
''' 套用順序固定為 修補/降噪/人像（原圖座標）→ 旋轉/翻轉 → 透視 → 拉直 → 裁切 → 色調 → 暗角/顆粒 → 銳利化（見 ImagePipeline）。
''' </summary>
Public Class EditRecipe
    Public Const CurrentVersion As Integer = 1

    Public Property Version As Integer = CurrentVersion

    ' ---- 幾何 ----
    ''' <summary>順時針旋轉角度：0 / 90 / 180 / 270。旋轉先於翻轉套用。</summary>
    Public Property Rotation As Integer
    Public Property FlipHorizontal As Boolean
    Public Property FlipVertical As Boolean
    ''' <summary>拉直角度（度），-45..45，正值順時針。</summary>
    Public Property Straighten As Double
    ''' <summary>Nothing 表示不裁切。</summary>
    Public Property Crop As CropRect
    ''' <summary>裁切形狀：矩形以外的形狀，形狀外變透明（匯出 PNG 保留）。</summary>
    Public Property CropShape As CropShape
    ''' <summary>垂直透視，-100..100：正值放大上緣（修正建築物上窄下寬），負值放大下緣。</summary>
    Public Property PerspectiveVertical As Integer
    ''' <summary>水平透視，-100..100：正值放大左緣，負值放大右緣。</summary>
    Public Property PerspectiveHorizontal As Integer

    ' ---- 修補（在已轉正原圖上處理）----
    ''' <summary>污點／雜物移除筆觸；Nothing 或空清單表示沒有。</summary>
    Public Property Spots As List(Of SpotStroke)
    ''' <summary>明度降噪，0..100。</summary>
    Public Property Denoise As Integer
    ''' <summary>色彩降噪（彩色雜點），0..100。</summary>
    Public Property ColorNoise As Integer

    ' ---- 色調（「風格」：濾鏡預設集會整組替換這些值）----
    ''' <summary>曝光（EV），-3..3。</summary>
    Public Property Exposure As Double
    ''' <summary>以下皆為 -100..100。</summary>
    Public Property Contrast As Integer
    Public Property Highlights As Integer
    Public Property [Shadows] As Integer
    ''' <summary>正值偏暖（黃），負值偏冷（藍）。</summary>
    Public Property Temperature As Integer
    ''' <summary>正值偏洋紅，負值偏綠。</summary>
    Public Property Tint As Integer
    Public Property Saturation As Integer

    ' ---- 效果（也屬於「風格」）----
    ''' <summary>暗角，-100..100：負值四周變暗，正值四周變亮。</summary>
    Public Property Vignette As Integer
    ''' <summary>褪色（提亮黑色、壓低白色的霧面感），0..100。</summary>
    Public Property Fade As Integer
    ''' <summary>底片顆粒，0..100。</summary>
    Public Property Grain As Integer
    ''' <summary>色彩濾鏡色相（度），0..359；強度為 0 時無作用。</summary>
    Public Property ToningHue As Integer
    ''' <summary>色彩濾鏡強度，0..100。例如褐色老照片 = 飽和度 -100 + 色相 35。</summary>
    Public Property ToningStrength As Integer

    ' ---- 細節與人像（不屬於濾鏡預設集）----
    ''' <summary>0..100。</summary>
    Public Property Sharpness As Integer
    ''' <summary>磨皮，0..100。只作用在偵測到的臉部膚色區域。</summary>
    Public Property SkinSmoothing As Integer
    ''' <summary>臉部提亮，0..100。</summary>
    Public Property FaceBrighten As Integer
    ''' <summary>亮眼，0..100。</summary>
    Public Property EyeBrighten As Integer

    ' ---- 創意特效 ----
    ''' <summary>局部調整（漸層濾鏡、筆刷）；Nothing 或空清單表示沒有。</summary>
    Public Property LocalAdjustments As List(Of LocalAdjustment)
    ''' <summary>背景模糊（假景深），0..100。主體由偵測到的人臉推估，沒有臉時為畫面中央。</summary>
    Public Property BackgroundBlur As Integer
    ''' <summary>移軸模型效果，0..100。</summary>
    Public Property TiltShift As Integer
    ''' <summary>移軸清楚帶的位置（0 = 上緣，100 = 下緣）。</summary>
    Public Property TiltShiftPosition As Integer = 50
    Public Property Frame As PhotoFrameStyle
    ''' <summary>邊框寬度，0..100。</summary>
    Public Property FrameSize As Integer = 40
    ''' <summary>文字與貼圖；Nothing 或空清單表示沒有。</summary>
    Public Property Overlays As List(Of Overlay)

    ''' <summary>繪圖圖層（由下而上）；Nothing 或空清單表示沒有。</summary>
    Public Property Drawings As List(Of DrawLayer)

    ''' <summary>去背；Nothing 表示沒有去背。</summary>
    Public Property Cutout As CutoutSettings

    ''' <summary>
    ''' 文字、貼圖與繪圖圖層混在一起的上下順序（由下而上，存各圖層的 Id）。
    ''' Nothing 時照舊：文字貼圖在下、繪圖在上；沒有列在這裡的圖層（剛新增的）放在最上面。見 LayerStack。
    ''' </summary>
    Public Property LayerOrder As List(Of String)

    ''' <summary>選取區（選取分頁）；不影響算圖，只決定複製、剪下、填色、局部調整等操作的範圍。Nothing 表示沒有選取。</summary>
    Public Property Selection As SelectionSpec

    Public Function Clone() As EditRecipe
        Dim r = DirectCast(MemberwiseClone(), EditRecipe)
        r.Crop = Crop?.Clone()
        r.Spots = Spots?.Select(Function(s) s.Clone()).ToList()
        r.LocalAdjustments = LocalAdjustments?.Select(Function(a) a.Clone()).ToList()
        r.Overlays = Overlays?.Select(Function(o) o.Clone()).ToList()
        r.Drawings = Drawings?.Select(Function(d) d.Clone()).ToList()
        r.Cutout = Cutout?.Clone()
        r.LayerOrder = If(LayerOrder Is Nothing, Nothing, New List(Of String)(LayerOrder))
        r.Selection = Selection?.Clone()
        Return r
    End Function

    ''' <summary>局部調整、模糊特效、文字貼圖、繪圖、邊框。</summary>
    Public ReadOnly Property HasCreative As Boolean
        Get
            Return (LocalAdjustments IsNot Nothing AndAlso LocalAdjustments.Any(Function(a) a.HasEffect)) OrElse
                   BackgroundBlur > 0 OrElse TiltShift > 0 OrElse Frame <> PhotoFrameStyle.None OrElse
                   (Overlays IsNot Nothing AndAlso Overlays.Any(Function(o) o.Visible)) OrElse
                   (Drawings IsNot Nothing AndAlso Drawings.Any(Function(d) d.Visible))
        End Get
    End Property

    Public ReadOnly Property HasGeometry As Boolean
        Get
            Return Rotation <> 0 OrElse FlipHorizontal OrElse FlipVertical OrElse
                   Math.Abs(Straighten) > 0.001 OrElse (Crop IsNot Nothing AndAlso Not Crop.IsFull) OrElse CropShape <> CropShape.Rectangle OrElse
                   PerspectiveVertical <> 0 OrElse PerspectiveHorizontal <> 0
        End Get
    End Property

    ''' <summary>需要逐點處理的色調（含褪色與色彩濾鏡）。</summary>
    Public ReadOnly Property HasTone As Boolean
        Get
            Return Math.Abs(Exposure) > 0.001 OrElse Contrast <> 0 OrElse Highlights <> 0 OrElse
                   [Shadows] <> 0 OrElse Temperature <> 0 OrElse Tint <> 0 OrElse Saturation <> 0 OrElse
                   Fade <> 0 OrElse ToningStrength <> 0
        End Get
    End Property

    ''' <summary>和位置有關的效果（暗角、顆粒），在裁切之後套用。</summary>
    Public ReadOnly Property HasEffects As Boolean
        Get
            Return Vignette <> 0 OrElse Grain <> 0
        End Get
    End Property

    Public ReadOnly Property HasPortrait As Boolean
        Get
            Return SkinSmoothing <> 0 OrElse FaceBrighten <> 0 OrElse EyeBrighten <> 0
        End Get
    End Property

    Public ReadOnly Property HasSpots As Boolean
        Get
            Return Spots IsNot Nothing AndAlso Spots.Count > 0
        End Get
    End Property

    ''' <summary>需要在原圖上先處理的項目（修補、降噪、人像），由 PhotoEdit.Vision 執行。</summary>
    Public ReadOnly Property HasSourceFix As Boolean
        Get
            Return HasSpots OrElse Denoise <> 0 OrElse ColorNoise <> 0 OrElse HasPortrait OrElse
                   (Cutout IsNot Nothing AndAlso Cutout.ChangesImage)
        End Get
    End Property

    Public ReadOnly Property IsIdentity As Boolean
        Get
            Return Not HasGeometry AndAlso Not HasTone AndAlso Not HasEffects AndAlso Not HasSourceFix AndAlso
                   Not HasCreative AndAlso Sharpness = 0
        End Get
    End Property

    ''' <summary>
    ''' 畫面上看到的影像向右轉 90 度。已翻轉一次時，存的旋轉方向要反過來才會和畫面一致。
    ''' 透視跟著轉：原本放大上緣變成放大右緣，原本放大左緣變成放大上緣。
    ''' </summary>
    Public Sub RotateRight()
        Rotation = NormalizeAngle(Rotation + If(FlipHorizontal Xor FlipVertical, -90, 90))
        Dim v = PerspectiveVertical
        PerspectiveVertical = PerspectiveHorizontal
        PerspectiveHorizontal = -v
        Crop = Nothing
    End Sub

    Public Sub RotateLeft()
        Rotation = NormalizeAngle(Rotation + If(FlipHorizontal Xor FlipVertical, 90, -90))
        Dim v = PerspectiveVertical
        PerspectiveVertical = -PerspectiveHorizontal
        PerspectiveHorizontal = v
        Crop = Nothing
    End Sub

    ''' <summary>翻轉畫面上看到的影像。翻轉在旋轉之後、透視與拉直之前套用，所以拉直角度與對應的透視要反號，裁切框要鏡射。</summary>
    Public Sub ToggleFlipHorizontal()
        FlipHorizontal = Not FlipHorizontal
        Straighten = -Straighten
        PerspectiveHorizontal = -PerspectiveHorizontal
        If Crop IsNot Nothing Then Crop = New CropRect(1 - Crop.X - Crop.Width, Crop.Y, Crop.Width, Crop.Height)
    End Sub

    Public Sub ToggleFlipVertical()
        FlipVertical = Not FlipVertical
        Straighten = -Straighten
        PerspectiveVertical = -PerspectiveVertical
        If Crop IsNot Nothing Then Crop = New CropRect(Crop.X, 1 - Crop.Y - Crop.Height, Crop.Width, Crop.Height)
    End Sub

    ''' <summary>套用濾鏡預設集：整組替換色調與效果，不動幾何、銳利度與人像。</summary>
    Public Sub CopyLookFrom(other As EditRecipe)
        Exposure = other.Exposure
        Contrast = other.Contrast
        Highlights = other.Highlights
        [Shadows] = other.Shadows
        Temperature = other.Temperature
        Tint = other.Tint
        Saturation = other.Saturation
        Vignette = other.Vignette
        Fade = other.Fade
        Grain = other.Grain
        ToningHue = other.ToningHue
        ToningStrength = other.ToningStrength
    End Sub

    Public Function LookEquals(other As EditRecipe) As Boolean
        Dim a As New EditRecipe(), b As New EditRecipe()
        a.CopyLookFrom(Me)
        b.CopyLookFrom(other)
        Return a.Equals(b)
    End Function

    ''' <summary>
    ''' 「貼上調整」：複製色調、效果、銳利度、降噪與人像。不複製幾何、透視與污點修補（每張照片都不同）。
    ''' </summary>
    Public Sub CopyAdjustmentsFrom(other As EditRecipe)
        CopyLookFrom(other)
        Sharpness = other.Sharpness
        SkinSmoothing = other.SkinSmoothing
        FaceBrighten = other.FaceBrighten
        EyeBrighten = other.EyeBrighten
        Denoise = other.Denoise
        ColorNoise = other.ColorNoise
        BackgroundBlur = other.BackgroundBlur
        TiltShift = other.TiltShift
        TiltShiftPosition = other.TiltShiftPosition
        Frame = other.Frame
        FrameSize = other.FrameSize
    End Sub

    ''' <summary>重設色調、效果、細節、降噪、人像、局部調整與特效；幾何、透視、污點修補、文字貼圖保留。</summary>
    Public Sub ResetAdjustments()
        CopyAdjustmentsFrom(New EditRecipe())
        LocalAdjustments = Nothing
    End Sub

    ''' <summary>只保留幾何（算濾鏡縮圖、自動調整分析用）。</summary>
    Public Function GeometryOnly() As EditRecipe
        Dim r = Clone()
        r.ResetAdjustments()
        Return r
    End Function

    ''' <summary>以 JSON 比對所有欄位，新增欄位時不必再改這裡。全幅裁切視同不裁切。</summary>
    Public Overrides Function Equals(obj As Object) As Boolean
        Dim o = TryCast(obj, EditRecipe)
        If o Is Nothing Then Return False
        Return CanonicalJson() = o.CanonicalJson()
    End Function

    Public Overrides Function GetHashCode() As Integer
        Return CanonicalJson().GetHashCode()
    End Function

    Friend Function CanonicalJson() As String
        Dim r = DirectCast(MemberwiseClone(), EditRecipe)
        r.Version = CurrentVersion
        If r.Crop IsNot Nothing AndAlso r.Crop.IsFull Then r.Crop = Nothing
        If r.Spots IsNot Nothing AndAlso r.Spots.Count = 0 Then r.Spots = Nothing
        If r.LocalAdjustments IsNot Nothing AndAlso r.LocalAdjustments.Count = 0 Then r.LocalAdjustments = Nothing
        If r.Overlays IsNot Nothing AndAlso r.Overlays.Count = 0 Then r.Overlays = Nothing
        If r.Drawings IsNot Nothing AndAlso r.Drawings.Count = 0 Then r.Drawings = Nothing
        If r.LayerOrder IsNot Nothing AndAlso r.LayerOrder.Count = 0 Then r.LayerOrder = Nothing
        If r.Selection IsNot Nothing AndAlso r.Selection.IsEmpty Then r.Selection = Nothing
        Return System.Text.Json.JsonSerializer.Serialize(r)
    End Function

    Friend Shared Function NormalizeAngle(deg As Integer) As Integer
        Dim r = deg Mod 360
        If r < 0 Then r += 360
        Return (r \ 90) * 90
    End Function
End Class

''' <summary>裁切形狀（數值存進編輯檔，不可更動）。</summary>
Public Enum CropShape
    Rectangle = 0
    Ellipse = 1
    RoundRect = 2
    Heart = 3
    Star = 4
End Enum
