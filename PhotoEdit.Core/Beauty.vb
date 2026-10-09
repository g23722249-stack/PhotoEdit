Imports System.Drawing

''' <summary>光影（美顏）：以臉為準加亮、加暗，模擬打光。</summary>
Public Enum BeautyLight
    None = 0
    ''' <summary>柔光：臉部柔和提亮、降低對比，像柔光罩。</summary>
    Soft = 1
    ''' <summary>林布蘭光：一側亮、一側暗，暗側臉頰留一塊三角光。</summary>
    Rembrandt = 2
    ''' <summary>側光：單側打光，明暗對比強。</summary>
    Side = 3
End Enum

''' <summary>
''' 一組美顏參數（人像分頁）。全部的臉共用一組（EditRecipe.GlobalBeauty），也可以替某張臉另存一組（EditRecipe.FaceBeauty，用 FaceX/FaceY 認臉）。
''' 數值都是 0..100（Tone、Chin 為 -100..100），0 表示不套用。
''' </summary>
Public Class BeautySettings
    ' ---- 肌膚 ----
    ''' <summary>磨皮（雙邊濾波，保留部分紋理）。</summary>
    Public Property Smoothing As Integer
    ''' <summary>臉部提亮。</summary>
    Public Property Brighten As Integer
    ''' <summary>勻膚：頻率分離，只抹勻膚色的色塊與明暗不均，毛孔等細紋理保留。</summary>
    Public Property Even As Integer
    ''' <summary>去紅：淡化泛紅（痘疤紅、鼻翼紅、臉頰潮紅）。</summary>
    Public Property Redness As Integer
    ''' <summary>美白：只把皮膚調亮。</summary>
    Public Property Whiten As Integer
    ''' <summary>膚色：負值白皙偏冷（粉嫩），正值健康小麥色。</summary>
    Public Property Tone As Integer
    ''' <summary>去油光：壓低額頭、鼻頭的反光亮點。</summary>
    Public Property Shine As Integer
    ''' <summary>自動去痘：數值越高找到的小斑點越多。</summary>
    Public Property Blemish As Integer
    ''' <summary>黑眼圈淡化。</summary>
    Public Property DarkCircles As Integer
    ' ---- 五官 ----
    ''' <summary>亮眼。</summary>
    Public Property Eyes As Integer
    ''' <summary>大眼：以眼睛為中心局部放大。</summary>
    Public Property EyeEnlarge As Integer
    ''' <summary>牙齒美白：去黃提亮。</summary>
    Public Property Teeth As Integer
    ''' <summary>腮紅濃度；顏色見 BlushColorArgb。</summary>
    Public Property Blush As Integer
    ''' <summary>腮紅顏色；0 表示預設的柔粉色。</summary>
    Public Property BlushColorArgb As Integer
    ''' <summary>立體修容：鼻樑打亮、顴骨下方陰影。</summary>
    Public Property Contour As Integer
    ' ---- 臉型、唇、眉（需要 68 點特徵點模型）----
    ''' <summary>瘦臉：兩頰往臉的中線收。</summary>
    Public Property FaceSlim As Integer
    ''' <summary>V 臉：下顎兩側往內收，下巴變尖。</summary>
    Public Property VFace As Integer
    ''' <summary>下巴：-100..100，負值縮短、正值拉長。</summary>
    Public Property Chin As Integer
    ''' <summary>瘦鼻：鼻翼往內收。</summary>
    Public Property NoseSlim As Integer
    ''' <summary>唇色濃度；顏色見 LipColorArgb。</summary>
    Public Property Lips As Integer
    ''' <summary>唇色；0 表示預設的玫瑰色。</summary>
    Public Property LipColorArgb As Integer
    ''' <summary>眉毛加深：讓淡的眉毛更清楚。</summary>
    Public Property Brows As Integer
    ' ---- 妝容（需要 68 點）----
    ''' <summary>眼影濃度；顏色見 EyeShadowColorArgb。</summary>
    Public Property EyeShadow As Integer
    ''' <summary>眼影顏色；0 表示預設的大地色。</summary>
    Public Property EyeShadowColorArgb As Integer
    ''' <summary>眼線：沿上眼皮畫一條深色細線，眼尾微微上揚。</summary>
    Public Property EyeLiner As Integer
    ''' <summary>臥蠶：下眼皮下方一條提亮、再下面淡淡的陰影。</summary>
    Public Property EyeBag As Integer
    ' ---- 光影 ----
    Public Property LightKind As BeautyLight
    ''' <summary>光影強度 0..100。</summary>
    Public Property Light As Integer
    ''' <summary>光從右邊來（林布蘭光、側光）；False 為左邊。</summary>
    Public Property LightFromRight As Boolean
    ' ---- 一鍵美顏 ----
    ''' <summary>目前套用的一鍵美顏（PresetNames 的索引）；手動改過數值後為 Nothing（自訂）。</summary>
    Public Property PresetIndex As Integer?
    ''' <summary>一鍵美顏的強度 0..100（%）。</summary>
    Public Property PresetStrength As Integer = 100
    ' ---- 個別臉 ----
    ''' <summary>這組參數屬於哪張臉（臉框中心，已轉正原圖 0..1）；全部的臉共用時為 Nothing。</summary>
    Public Property FaceX As Double?
    Public Property FaceY As Double?

    Public Shared ReadOnly DefaultBlush As Color = Color.FromArgb(236, 120, 140)
    Public Shared ReadOnly DefaultLip As Color = Color.FromArgb(205, 60, 85)
    Public Shared ReadOnly DefaultEyeShadow As Color = Color.FromArgb(170, 110, 85)

    Public ReadOnly Property BlushColor As Color
        Get
            Return If(BlushColorArgb = 0, DefaultBlush, Color.FromArgb(255, Color.FromArgb(BlushColorArgb)))
        End Get
    End Property

    Public ReadOnly Property LipColor As Color
        Get
            Return If(LipColorArgb = 0, DefaultLip, Color.FromArgb(255, Color.FromArgb(LipColorArgb)))
        End Get
    End Property

    Public ReadOnly Property EyeShadowColor As Color
        Get
            Return If(EyeShadowColorArgb = 0, DefaultEyeShadow, Color.FromArgb(255, Color.FromArgb(EyeShadowColorArgb)))
        End Get
    End Property

    ''' <summary>有用到 68 點特徵點的效果（臉型、唇色、眉毛、妝容）。</summary>
    Public ReadOnly Property NeedsDense As Boolean
        Get
            Return FaceSlim <> 0 OrElse VFace <> 0 OrElse Chin <> 0 OrElse NoseSlim <> 0 OrElse Lips <> 0 OrElse Brows <> 0 OrElse
                   EyeShadow <> 0 OrElse EyeLiner <> 0 OrElse EyeBag <> 0
        End Get
    End Property

    Public ReadOnly Property HasLight As Boolean
        Get
            Return LightKind <> BeautyLight.None AndAlso Light > 0
        End Get
    End Property

    ''' <summary>所有效果都是 0（不含顏色、光影方向、一鍵美顏記錄與臉的位置）。</summary>
    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return Smoothing = 0 AndAlso Brighten = 0 AndAlso Even = 0 AndAlso Redness = 0 AndAlso Whiten = 0 AndAlso Tone = 0 AndAlso
                   Shine = 0 AndAlso Blemish = 0 AndAlso DarkCircles = 0 AndAlso Eyes = 0 AndAlso EyeEnlarge = 0 AndAlso Teeth = 0 AndAlso
                   Blush = 0 AndAlso Contour = 0 AndAlso
                   FaceSlim = 0 AndAlso VFace = 0 AndAlso Chin = 0 AndAlso NoseSlim = 0 AndAlso Lips = 0 AndAlso Brows = 0 AndAlso
                   EyeShadow = 0 AndAlso EyeLiner = 0 AndAlso EyeBag = 0 AndAlso Not HasLight
        End Get
    End Property

    ''' <summary>除了數值之外沒有任何要記的東西（顏色、光影種類、一鍵美顏記錄都是預設）。</summary>
    Public ReadOnly Property IsBlank As Boolean
        Get
            Return IsEmpty AndAlso BlushColorArgb = 0 AndAlso LipColorArgb = 0 AndAlso EyeShadowColorArgb = 0 AndAlso
                   LightKind = BeautyLight.None AndAlso Not LightFromRight AndAlso Not PresetIndex.HasValue AndAlso PresetStrength = 100
        End Get
    End Property

    Public Function Clone() As BeautySettings
        Return DirectCast(MemberwiseClone(), BeautySettings)
    End Function

    ''' <summary>複製效果數值、顏色、光影與一鍵美顏記錄（不動臉的位置）。</summary>
    Public Sub CopyValuesFrom(o As BeautySettings)
        Smoothing = o.Smoothing : Brighten = o.Brighten : Even = o.Even : Redness = o.Redness : Whiten = o.Whiten : Tone = o.Tone
        Shine = o.Shine : Blemish = o.Blemish : DarkCircles = o.DarkCircles : Eyes = o.Eyes : EyeEnlarge = o.EyeEnlarge
        Teeth = o.Teeth : Blush = o.Blush : BlushColorArgb = o.BlushColorArgb : Contour = o.Contour
        FaceSlim = o.FaceSlim : VFace = o.VFace : Chin = o.Chin : NoseSlim = o.NoseSlim : Lips = o.Lips : LipColorArgb = o.LipColorArgb : Brows = o.Brows
        EyeShadow = o.EyeShadow : EyeShadowColorArgb = o.EyeShadowColorArgb : EyeLiner = o.EyeLiner : EyeBag = o.EyeBag
        LightKind = o.LightKind : Light = o.Light : LightFromRight = o.LightFromRight
        PresetIndex = o.PresetIndex : PresetStrength = o.PresetStrength
    End Sub

    ''' <summary>全部數值乘上 percent%（顏色、光影種類與方向不變）。</summary>
    Public Function Scaled(percent As Integer) As BeautySettings
        Dim k = Math.Max(0, Math.Min(100, percent)) / 100.0
        Dim s = Function(v As Integer) CInt(Math.Round(v * k))
        Dim r = Clone()
        r.Smoothing = s(Smoothing) : r.Brighten = s(Brighten) : r.Even = s(Even) : r.Redness = s(Redness) : r.Whiten = s(Whiten) : r.Tone = s(Tone)
        r.Shine = s(Shine) : r.Blemish = s(Blemish) : r.DarkCircles = s(DarkCircles) : r.Eyes = s(Eyes) : r.EyeEnlarge = s(EyeEnlarge)
        r.Teeth = s(Teeth) : r.Blush = s(Blush) : r.Contour = s(Contour)
        r.FaceSlim = s(FaceSlim) : r.VFace = s(VFace) : r.Chin = s(Chin) : r.NoseSlim = s(NoseSlim) : r.Lips = s(Lips) : r.Brows = s(Brows)
        r.EyeShadow = s(EyeShadow) : r.EyeLiner = s(EyeLiner) : r.EyeBag = s(EyeBag) : r.Light = s(Light)
        Return r
    End Function

    ' ---- 一鍵美顏 ----
    Public Shared ReadOnly PresetNames As String() = {
        "自然", "甜美", "證件照", "男性",
        "清透", "好氣色", "嬰兒肌", "冷白皮", "小麥肌", "精緻小臉", "減齡", "自拍補光", "清爽男生",
        "韓系淡妝", "桃花妝", "歐美妝", "柔光", "林布蘭光", "側光"}

    Private Shared Function Argb(r As Integer, g As Integer, b As Integer) As Integer
        Return Color.FromArgb(r, g, b).ToArgb()
    End Function

    ''' <summary>第 index 組一鍵美顏（強度 100%），PresetIndex 已填好。</summary>
    Public Shared Function Preset(index As Integer) As BeautySettings
        Dim b As BeautySettings
        Select Case index
            Case 0 ' 自然：輕微，看不出修過
                b = New BeautySettings With {.Smoothing = 15, .Even = 25, .Redness = 20, .Blemish = 40, .DarkCircles = 25, .Eyes = 10, .Teeth = 20}
            Case 1 ' 甜美：白皙粉嫩、大眼、腮紅
                b = New BeautySettings With {.Smoothing = 25, .Brighten = 10, .Even = 30, .Redness = 20, .Whiten = 25, .Tone = -15, .Blemish = 50,
                                             .DarkCircles = 35, .Eyes = 20, .EyeEnlarge = 20, .Teeth = 30, .Blush = 25, .FaceSlim = 20, .Lips = 20}
            Case 2 ' 證件照：乾淨、均勻、不變形
                b = New BeautySettings With {.Smoothing = 20, .Brighten = 15, .Even = 30, .Redness = 25, .Shine = 35, .Blemish = 60, .DarkCircles = 35,
                                             .Teeth = 20}
            Case 3 ' 男性：保留質感、去油光、輪廓立體
                b = New BeautySettings With {.Even = 20, .Redness = 15, .Tone = 10, .Shine = 40, .Blemish = 45, .DarkCircles = 20, .Contour = 30}
            Case 4 ' 清透：比自然亮一點的裸妝感
                b = New BeautySettings With {.Even = 30, .Redness = 25, .Whiten = 15, .Blemish = 50, .DarkCircles = 30, .Eyes = 15, .Teeth = 20}
            Case 5 ' 好氣色：紅潤有精神
                b = New BeautySettings With {.Blush = 35, .Lips = 30, .Redness = 10, .Eyes = 20, .Whiten = 10, .Tone = -5, .Even = 15}
            Case 6 ' 嬰兒肌：最光滑細緻
                b = New BeautySettings With {.Smoothing = 40, .Even = 45, .Blemish = 70, .DarkCircles = 40, .Whiten = 20, .Redness = 20}
            Case 7 ' 冷白皮：白皙偏冷、玫瑰唇
                b = New BeautySettings With {.Whiten = 35, .Tone = -35, .Redness = 30, .Lips = 15, .Even = 25, .Blemish = 40}
            Case 8 ' 小麥肌：健康膚色、立體
                b = New BeautySettings With {.Tone = 35, .Shine = 30, .Contour = 35, .Even = 20, .Blemish = 40}
            Case 9 ' 精緻小臉：小臉大眼
                b = New BeautySettings With {.FaceSlim = 35, .VFace = 30, .Chin = 15, .EyeEnlarge = 30, .NoseSlim = 20, .Smoothing = 25, .Whiten = 20,
                                             .Lips = 25, .Even = 25, .Blemish = 50}
            Case 10 ' 減齡：看起來年輕一點
                b = New BeautySettings With {.Even = 40, .DarkCircles = 50, .Smoothing = 30, .EyeEnlarge = 20, .Brighten = 15, .Chin = -10, .Blemish = 50}
            Case 11 ' 自拍補光：逆光、暗處自拍救臉
                b = New BeautySettings With {.Brighten = 35, .Eyes = 25, .DarkCircles = 35, .Shine = 30, .Even = 20}
            Case 12 ' 清爽男生：乾淨、去油光、眉毛清楚
                b = New BeautySettings With {.Blemish = 50, .Shine = 50, .Even = 20, .Brows = 30, .Contour = 20, .Redness = 15}
            Case 13 ' 韓系淡妝：淡眼影、臥蠶、水潤珊瑚唇
                b = New BeautySettings With {.EyeShadow = 25, .EyeShadowColorArgb = Argb(225, 150, 130), .EyeBag = 40, .Lips = 30,
                                             .LipColorArgb = Argb(230, 100, 110), .Even = 25, .Whiten = 15, .Blush = 15, .Blemish = 40}
            Case 14 ' 桃花妝：粉色眼影、大範圍腮紅
                b = New BeautySettings With {.EyeShadow = 40, .EyeShadowColorArgb = Argb(232, 120, 150), .Blush = 45, .BlushColorArgb = Argb(240, 130, 150),
                                             .Lips = 30, .LipColorArgb = Argb(225, 90, 120), .Even = 20, .Whiten = 15, .Blemish = 40}
            Case 15 ' 歐美妝：深色眼影、眼線、修容、正紅唇
                b = New BeautySettings With {.EyeShadow = 45, .EyeShadowColorArgb = Argb(115, 72, 60), .EyeLiner = 60, .Contour = 45, .Lips = 35,
                                             .LipColorArgb = Argb(165, 30, 50), .Brows = 30, .Even = 25, .Blemish = 50}
            Case 16 ' 柔光
                b = New BeautySettings With {.LightKind = BeautyLight.Soft, .Light = 60, .Even = 20, .Smoothing = 15}
            Case 17 ' 林布蘭光
                b = New BeautySettings With {.LightKind = BeautyLight.Rembrandt, .Light = 70, .Even = 15}
            Case Else ' 側光
                b = New BeautySettings With {.LightKind = BeautyLight.Side, .Light = 70}
        End Select
        b.PresetIndex = Math.Max(0, Math.Min(PresetNames.Length - 1, index))
        Return b
    End Function

    ''' <summary>第 index 組一鍵美顏、強度 percent%。</summary>
    Public Shared Function Preset(index As Integer, percent As Integer) As BeautySettings
        Dim b = Preset(index).Scaled(percent)
        b.PresetStrength = Math.Max(0, Math.Min(100, percent))
        Return b
    End Function
End Class


''' <summary>液化筆刷的模式。</summary>
Public Enum LiquifyMode
    ''' <summary>推移：沿著拖曳方向推動像素。</summary>
    Push = 0
    ''' <summary>膨脹：筆刷中心往外撐開（放大）。</summary>
    Bloat = 1
    ''' <summary>縮攏：往筆刷中心收（縮小）。</summary>
    Pucker = 2
    ''' <summary>順時針旋轉。</summary>
    TwirlClockwise = 3
    ''' <summary>逆時針旋轉。</summary>
    TwirlCounterClockwise = 4
End Enum

''' <summary>一筆液化。座標同 SpotStroke（已轉正原圖 0..1，半徑以原圖長邊為 1）。</summary>
Public Class LiquifyStroke
    Inherits SpotStroke
    Public Property Mode As LiquifyMode
    ''' <summary>力道 1..100。</summary>
    Public Property Strength As Integer = 50

    Public Shadows Function Clone() As LiquifyStroke
        Return New LiquifyStroke With {.Radius = Radius, .Path = New List(Of Double)(Path), .Mode = Mode, .Strength = Strength}
    End Function
End Class
