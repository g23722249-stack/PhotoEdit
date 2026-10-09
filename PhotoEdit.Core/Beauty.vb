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
    ''' <summary>曬黑 0..100：臉和露出的皮膚一起變成深色（比膚色的小麥更深）。</summary>
    Public Property Tan As Integer
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
    ' ---- 美妝細項（樣式、顏色、參數）----
    ''' <summary>口紅樣式；濃度用 Lips、顏色用 LipColorArgb。</summary>
    Public Property LipStyle As LipStyle
    ''' <summary>口紅光澤 0..100（下唇中間的亮光）。</summary>
    Public Property LipGloss As Integer
    Public Property ShadowStyle As ShadowStyle
    ''' <summary>眼影範圍 0..100（往上延伸多高）。</summary>
    Public Property ShadowSpread As Integer = 50
    ''' <summary>眼影閃粉 0..100。</summary>
    Public Property ShadowGlitter As Integer
    Public Property LinerStyle As LinerStyle
    ''' <summary>眼線粗細 0..100。</summary>
    Public Property LinerWidth As Integer = 35
    ''' <summary>眼尾上揚的長度 0..100。</summary>
    Public Property LinerWing As Integer = 40
    ''' <summary>眼線顏色；0 為近黑。</summary>
    Public Property LinerColorArgb As Integer
    ''' <summary>睫毛濃度 0..100。</summary>
    Public Property Lash As Integer
    Public Property LashStyle As LashStyle
    Public Property LashLength As Integer = 50
    Public Property LashCurl As Integer = 45
    Public Property LashColorArgb As Integer
    ''' <summary>美瞳濃度 0..100（需要 478 點網格的虹膜點）。</summary>
    Public Property Iris As Integer
    Public Property IrisStyle As IrisStyle
    Public Property IrisColorArgb As Integer
    ''' <summary>美瞳放大 0..100。</summary>
    Public Property IrisEnlarge As Integer = 30
    ''' <summary>美瞳外圈（深色邊）0..100。</summary>
    Public Property IrisRing As Integer = 40
    ''' <summary>雙眼皮濃度 0..100。</summary>
    Public Property Fold As Integer
    Public Property FoldStyle As FoldStyle
    ''' <summary>雙眼皮寬度 0..100。</summary>
    Public Property FoldWidth As Integer = 40
    ''' <summary>眉形（濃度用 Brows）。</summary>
    Public Property BrowStyle As BrowStyle
    Public Property BrowColorArgb As Integer
    ''' <summary>眉毛粗細 0..100（50 不變）。</summary>
    Public Property BrowThick As Integer = 50
    ''' <summary>眉峰高低 0..100。</summary>
    Public Property BrowPeak As Integer = 30
    Public Property BlushStyle As BlushStyle
    ''' <summary>腮紅範圍 0..100。</summary>
    Public Property BlushSpread As Integer = 50
    ''' <summary>高光濃度 0..100。</summary>
    Public Property Highlight As Integer
    Public Property HighlightStyle As HighlightStyle
    Public Property HighlightColorArgb As Integer
    ''' <summary>眼下打亮 0..100：下眼皮下方一大片往白色提亮（辣妹妝的眼下白）。</summary>
    Public Property UnderEye As Integer
    ''' <summary>白鼻樑 0..100：兩眼中間一筆白色畫到鼻尖（黑辣妹的誇張立體鼻）。</summary>
    Public Property WhiteNose As Integer
    ''' <summary>戲曲角色（OperaRoles 的索引＋1，0＝沒有）。</summary>
    Public Property OperaRole As Integer
    ''' <summary>戲曲妝濃度 0..100。</summary>
    Public Property Opera As Integer
    ''' <summary>吊眉 0..100：眼尾、眉尾往太陽穴上拉（勒頭的效果）。</summary>
    Public Property OperaLift As Integer
    ''' <summary>保留明暗 0..100：0＝平塗（像貼紙），越高越像畫在臉上（鼻樑、顴骨的立體感還在）。</summary>
    Public Property OperaShade As Integer = 70
    ''' <summary>貼片子（旦角額頭與兩頰的黑色髮片）。</summary>
    Public Property OperaPian As Boolean
    ''' <summary>戴髯口（假鬍子）。</summary>
    Public Property OperaBeard As Boolean

    ''' <summary>選了戲曲角色而且有濃度。</summary>
    Public ReadOnly Property HasOpera As Boolean
        Get
            Return OperaRole > 0 AndAlso Opera > 0
        End Get
    End Property

    ''' <summary>換成某個戲曲角色：套用該角色預設的吊眉、片子、髯口；濃度是 0 時設成 100。</summary>
    Public Sub SetOperaRole(roleId As Integer)
        OperaRole = roleId
        Dim r = PhotoEdit.OperaRoles.Get(roleId)
        If r Is Nothing Then Return
        OperaLift = r.Lift
        OperaPian = r.Pian
        OperaBeard = r.BeardArgb <> 0
        If Opera = 0 Then Opera = 100
    End Sub
    ''' <summary>髮色濃度 0..100（用人像去背模型找出頭髮）。</summary>
    Public Property Hair As Integer
    Public Property HairColorArgb As Integer
    ' ---- 臉型（變形）----
    ''' <summary>嘴角上揚 0..100。</summary>
    Public Property Smile As Integer
    ''' <summary>豐唇 -100..100（負值變薄）。</summary>
    Public Property LipFull As Integer
    ''' <summary>開眼角 0..100。</summary>
    Public Property EyeCorner As Integer
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

    Public Shared ReadOnly DefaultLiner As Color = Color.FromArgb(28, 24, 24)
    Public Shared ReadOnly DefaultLash As Color = Color.FromArgb(20, 16, 16)
    Public Shared ReadOnly DefaultIris As Color = Color.FromArgb(120, 82, 52)
    Public Shared ReadOnly DefaultBrow As Color = Color.FromArgb(92, 64, 48)
    Public Shared ReadOnly DefaultHighlight As Color = Color.FromArgb(255, 246, 234)
    Public Shared ReadOnly DefaultHair As Color = Color.FromArgb(120, 70, 40)

    Private Shared Function Pick(argb As Integer, fallback As Color) As Color
        Return If(argb = 0, fallback, Color.FromArgb(255, Color.FromArgb(argb)))
    End Function

    Public ReadOnly Property LinerColor As Color
        Get
            Return Pick(LinerColorArgb, DefaultLiner)
        End Get
    End Property
    Public ReadOnly Property LashColor As Color
        Get
            Return Pick(LashColorArgb, DefaultLash)
        End Get
    End Property
    Public ReadOnly Property IrisColor As Color
        Get
            Return Pick(IrisColorArgb, DefaultIris)
        End Get
    End Property
    Public ReadOnly Property BrowColor As Color
        Get
            Return Pick(BrowColorArgb, DefaultBrow)
        End Get
    End Property
    Public ReadOnly Property HighlightColor As Color
        Get
            Return Pick(HighlightColorArgb, DefaultHighlight)
        End Get
    End Property
    Public ReadOnly Property HairColor As Color
        Get
            Return Pick(HairColorArgb, DefaultHair)
        End Get
    End Property

    ''' <summary>效果強度類的欄位（0 表示不套用；強度縮放、IsEmpty 用）。樣式、顏色、形狀參數不在這裡。</summary>
    Private Shared ReadOnly AmountNames As String() = {
        "Smoothing", "Brighten", "Even", "Redness", "Whiten", "Tone", "Shine", "Blemish", "DarkCircles",
        "Eyes", "EyeEnlarge", "Teeth", "Blush", "Contour", "FaceSlim", "VFace", "Chin", "NoseSlim", "Lips", "Brows",
        "EyeShadow", "EyeLiner", "EyeBag", "Light", "LipGloss", "ShadowGlitter", "Lash", "Iris", "Fold", "Highlight", "Hair",
        "Smile", "LipFull", "EyeCorner", "Tan", "UnderEye", "WhiteNose", "Opera", "OperaLift"}
    Private Shared ReadOnly AmountProps As Reflection.PropertyInfo() =
        AmountNames.Select(Function(n) GetType(BeautySettings).GetProperty(n)).ToArray()
    ''' <summary>可以寫入的欄位（複製用；臉的位置除外）。</summary>
    Private Shared ReadOnly CopyProps As Reflection.PropertyInfo() =
        GetType(BeautySettings).GetProperties().Where(Function(p) p.CanWrite AndAlso p.Name <> "FaceX" AndAlso p.Name <> "FaceY").ToArray()

    ''' <summary>有用到臉部特徵點（68 點或網格）的效果。</summary>
    Public ReadOnly Property NeedsDense As Boolean
        Get
            Return FaceSlim <> 0 OrElse VFace <> 0 OrElse Chin <> 0 OrElse NoseSlim <> 0 OrElse Lips <> 0 OrElse Brows <> 0 OrElse
                   EyeShadow <> 0 OrElse EyeLiner <> 0 OrElse EyeBag <> 0 OrElse Lash <> 0 OrElse Fold <> 0 OrElse Highlight <> 0 OrElse
                   Smile <> 0 OrElse LipFull <> 0 OrElse EyeCorner <> 0 OrElse Iris <> 0 OrElse UnderEye <> 0 OrElse WhiteNose <> 0 OrElse Opera <> 0
        End Get
    End Property

    Public ReadOnly Property HasLight As Boolean
        Get
            Return LightKind <> BeautyLight.None AndAlso Light > 0
        End Get
    End Property

    ''' <summary>所有效果強度都是 0（不含顏色、樣式、形狀參數、一鍵美顏記錄與臉的位置）。</summary>
    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return AmountProps.All(Function(p) CInt(p.GetValue(Me)) = 0)
        End Get
    End Property

    ''' <summary>和預設完全一樣（除了臉的位置）：沒有任何要記的東西。</summary>
    Public ReadOnly Property IsBlank As Boolean
        Get
            Dim fresh As New BeautySettings()
            Return CopyProps.All(Function(p) Equals(p.GetValue(Me), p.GetValue(fresh)))
        End Get
    End Property

    Public Function Clone() As BeautySettings
        Return DirectCast(MemberwiseClone(), BeautySettings)
    End Function

    ''' <summary>複製全部設定（效果、樣式、顏色、參數、一鍵美顏記錄），不動臉的位置。</summary>
    Public Sub CopyValuesFrom(o As BeautySettings)
        For Each p In CopyProps
            p.SetValue(Me, p.GetValue(o))
        Next
    End Sub

    ''' <summary>全部效果強度乘上 percent%（樣式、顏色、形狀參數不變）。</summary>
    Public Function Scaled(percent As Integer) As BeautySettings
        Dim k = Math.Max(0, Math.Min(100, percent)) / 100.0
        Dim r = Clone()
        For Each p In AmountProps
            p.SetValue(r, CInt(Math.Round(CInt(p.GetValue(Me)) * k)))
        Next
        Return r
    End Function


    ' ---- 一鍵美顏 ----
    Public Shared ReadOnly PresetNames As String() = {
        "自然", "甜美", "證件照", "男性",
        "清透", "好氣色", "嬰兒肌", "冷白皮", "小麥肌", "精緻小臉", "減齡", "自拍補光", "清爽男生",
        "韓系淡妝", "桃花妝", "歐美妝", "柔光", "林布蘭光", "側光",
        "109 白辣妹", "109 黑辣妹", "Y2K 辣妹",
        "戲曲・京劇青衣", "戲曲・京劇小生", "戲曲・關公", "戲曲・包公", "戲曲・京劇文丑"}

    Private Shared Function Argb(r As Integer, g As Integer, b As Integer) As Integer
        Return Color.FromArgb(r, g, b).ToArgb()
    End Function

    ''' <summary>
    ''' 我的妝容：使用者存的組合（名稱＋設定），由程式啟動時從設定檔載入。
    ''' 一鍵美顏的索引接在內建的後面（PresetNames.Length 起）。
    ''' </summary>
    Public Shared Property CustomLooks As New List(Of (Name As String, Look As BeautySettings))()

    ''' <summary>內建＋我的妝容的總數。</summary>
    Public Shared ReadOnly Property PresetCount As Integer
        Get
            Return PresetNames.Length + CustomLooks.Count
        End Get
    End Property

    Public Shared Function PresetName(index As Integer) As String
        If index < PresetNames.Length Then Return PresetNames(Math.Max(0, index))
        Dim c = index - PresetNames.Length
        Return If(c < CustomLooks.Count, CustomLooks(c).Name, "（已刪除）")
    End Function

    ''' <summary>第 index 組一鍵美顏（強度 100%），PresetIndex 已填好；我的妝容被刪掉時回傳空的。</summary>
    Public Shared Function Preset(index As Integer) As BeautySettings
        If index >= PresetNames.Length Then
            Dim c = index - PresetNames.Length
            Dim look = If(c < CustomLooks.Count, CustomLooks(c).Look.Clone(), New BeautySettings())
            look.FaceX = Nothing : look.FaceY = Nothing
            look.PresetIndex = If(c < CustomLooks.Count, index, CType(Nothing, Integer?))
            look.PresetStrength = 100
            Return look
        End If
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
                b = New BeautySettings With {.EyeShadow = 25, .EyeShadowColorArgb = Argb(225, 150, 130), .ShadowStyle = ShadowStyle.Gradient, .EyeBag = 40,
                                             .Lips = 30, .LipColorArgb = Argb(230, 100, 110), .LipStyle = LipStyle.Gradient, .LipGloss = 45,
                                             .Lash = 30, .LashStyle = LashStyle.Natural, .Fold = 25, .FoldStyle = FoldStyle.Fan, .Highlight = 25, .HighlightStyle = HighlightStyle.Nose,
                                             .Even = 25, .Whiten = 15, .Blush = 15, .Blemish = 40}
            Case 14 ' 桃花妝：粉色眼影、大範圍腮紅
                b = New BeautySettings With {.EyeShadow = 40, .EyeShadowColorArgb = Argb(232, 120, 150), .ShadowStyle = ShadowStyle.Peach, .ShadowGlitter = 20,
                                             .Blush = 45, .BlushColorArgb = Argb(240, 130, 150), .BlushStyle = BlushStyle.Tipsy,
                                             .Lips = 30, .LipColorArgb = Argb(225, 90, 120), .LipStyle = LipStyle.Bitten, .LipGloss = 30,
                                             .Lash = 35, .LashStyle = LashStyle.Curl, .Even = 20, .Whiten = 15, .Blemish = 40}
            Case 15 ' 歐美妝：深色眼影、眼線、修容、正紅唇
                b = New BeautySettings With {.EyeShadow = 45, .EyeShadowColorArgb = Argb(115, 72, 60), .ShadowStyle = ShadowStyle.Smoky,
                                             .EyeLiner = 60, .LinerStyle = LinerStyle.Cat, .Lash = 55, .LashStyle = LashStyle.Thick, .Fold = 35, .FoldStyle = FoldStyle.Euro,
                                             .Contour = 45, .Highlight = 35, .HighlightStyle = HighlightStyle.All,
                                             .Lips = 35, .LipColorArgb = Argb(165, 30, 50), .LipStyle = LipStyle.Matte, .Brows = 30, .BrowStyle = BrowStyle.Arch,
                                             .Even = 25, .Blemish = 50}
            Case 16 ' 柔光
                b = New BeautySettings With {.LightKind = BeautyLight.Soft, .Light = 60, .Even = 20, .Smoothing = 15}
            Case 17 ' 林布蘭光
                b = New BeautySettings With {.LightKind = BeautyLight.Rembrandt, .Light = 70, .Even = 15}
            Case 18 ' 側光
                b = New BeautySettings With {.LightKind = BeautyLight.Side, .Light = 70}
            Case 19 ' 109 白辣妹：白皙粉嫩、粗黑貓眼線、長假睫毛、淺色放大片、誇張臥蠶、橫過鼻樑的粉色腮紅、水潤淡粉唇、亞麻金髮
                b = New BeautySettings With {.Whiten = 25, .Tone = -10, .Even = 35, .Smoothing = 25, .Blemish = 50, .EyeEnlarge = 30, .EyeCorner = 40,
                                             .EyeLiner = 85, .LinerStyle = LinerStyle.Cat, .LinerWidth = 60, .LinerWing = 70,
                                             .Lash = 85, .LashStyle = LashStyle.Thick, .LashLength = 130, .LashCurl = 60,
                                             .Iris = 70, .IrisStyle = IrisStyle.Enlarge, .IrisColorArgb = Argb(154, 122, 90), .IrisEnlarge = 60, .IrisRing = 60,
                                             .EyeBag = 70, .UnderEye = 45, .EyeShadow = 45, .ShadowStyle = ShadowStyle.Gradient, .EyeShadowColorArgb = Argb(192, 138, 128),
                                             .ShadowGlitter = 30, .Blush = 50, .BlushStyle = BlushStyle.Sunburn, .BlushColorArgb = Argb(240, 138, 160),
                                             .Lips = 55, .LipStyle = LipStyle.Glossy, .LipColorArgb = Argb(232, 160, 160), .LipGloss = 70,
                                             .Highlight = 40, .HighlightStyle = HighlightStyle.All, .Contour = 35,
                                             .Brows = 50, .BrowStyle = BrowStyle.Arch, .BrowThick = 35, .BrowColorArgb = Argb(160, 122, 90),
                                             .Hair = 70, .HairColorArgb = Argb(200, 160, 112)}
            Case 20 ' 109 黑辣妹：深色曬黑、眼周與唇用白色打亮、極粗眼線、上下長睫毛、混血放大片、金髮
                b = New BeautySettings With {.Tan = 85, .Even = 30, .Blemish = 40, .EyeEnlarge = 35, .EyeCorner = 45,
                                             .EyeLiner = 95, .LinerStyle = LinerStyle.Cat, .LinerWidth = 75, .LinerWing = 80,
                                             .Lash = 95, .LashStyle = LashStyle.Lower, .LashLength = 145, .LashCurl = 60,
                                             .Iris = 80, .IrisStyle = IrisStyle.Mixed, .IrisColorArgb = Argb(176, 154, 128), .IrisEnlarge = 70, .IrisRing = 70,
                                             .EyeShadow = 90, .ShadowStyle = ShadowStyle.Panda, .ShadowSpread = 60, .EyeShadowColorArgb = Argb(250, 250, 248),
                                             .ShadowGlitter = 25, .Lips = 75, .LipStyle = LipStyle.Glossy, .LipColorArgb = Argb(244, 226, 220), .LipGloss = 80,
                                             .WhiteNose = 85, .Highlight = 45, .HighlightStyle = HighlightStyle.All, .HighlightColorArgb = Argb(255, 255, 255), .Contour = 30,
                                             .Blush = 40, .BlushStyle = BlushStyle.Slant, .BlushColorArgb = Argb(240, 144, 112),
                                             .Brows = 45, .BrowStyle = BrowStyle.Arch, .BrowThick = 30, .BrowColorArgb = Argb(176, 136, 96),
                                             .Hair = 80, .HairColorArgb = Argb(224, 192, 144)}
            Case 21 ' Y2K 辣妹：微曬、上揚眼線、一簇一簇的睫毛、自然放大片、亮片眼影、水潤唇、微醺腮紅
                b = New BeautySettings With {.Tone = 20, .Even = 30, .Smoothing = 20, .Blemish = 50, .EyeEnlarge = 20,
                                             .EyeLiner = 70, .LinerStyle = LinerStyle.Winged, .LinerWidth = 45, .LinerWing = 60,
                                             .Lash = 70, .LashStyle = LashStyle.Separated, .LashLength = 115,
                                             .Iris = 55, .IrisStyle = IrisStyle.Natural, .IrisColorArgb = Argb(138, 106, 80), .IrisEnlarge = 40,
                                             .EyeBag = 55, .UnderEye = 25, .EyeShadow = 45, .ShadowStyle = ShadowStyle.Glitter, .EyeShadowColorArgb = Argb(216, 168, 144),
                                             .ShadowGlitter = 45, .Lips = 50, .LipStyle = LipStyle.Glossy, .LipColorArgb = Argb(216, 128, 128), .LipGloss = 70,
                                             .Highlight = 40, .HighlightStyle = HighlightStyle.All, .Blush = 40, .BlushStyle = BlushStyle.Tipsy, .BlushColorArgb = Argb(240, 160, 160),
                                             .Brows = 40, .BrowStyle = BrowStyle.Arch, .BrowThick = 40, .BrowColorArgb = Argb(138, 106, 80),
                                             .Hair = 50, .HairColorArgb = Argb(168, 120, 80)}
            Case Else ' 戲曲：名稱「戲曲・角色」對應 OperaRoles
                b = New BeautySettings()
                Dim roleName = PresetNames(Math.Max(0, Math.Min(PresetNames.Length - 1, index))).Replace("戲曲・", "")
                b.SetOperaRole(OperaRoles.IdOf(roleName))
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

''' <summary>口紅樣式。</summary>
Public Enum LipStyle
    ''' <summary>霧面：均勻、不反光。</summary>
    Matte = 0
    ''' <summary>水潤：下唇中間有亮光。</summary>
    Glossy = 1
    ''' <summary>咬唇：中間深、往外淡。</summary>
    Bitten = 2
    ''' <summary>漸層：內側深、外緣淡。</summary>
    Gradient = 3
    ''' <summary>唇線：外緣一圈較深。</summary>
    Liner = 4
End Enum

Public Enum ShadowStyle
    ''' <summary>單色。</summary>
    [Single] = 0
    ''' <summary>大地漸層：貼眼皮深、往上淡。</summary>
    Gradient = 1
    ''' <summary>煙燻：深色、範圍大，下眼皮也暈開。</summary>
    Smoky = 2
    ''' <summary>桃花：粉色，延伸到下眼尾。</summary>
    Peach = 3
    ''' <summary>亮片：加閃粉。</summary>
    Glitter = 4
    ''' <summary>熊貓白：白色大面積塗滿眼窩與眼周（上到眉下、下到眼袋），黑辣妹的黑白對比。</summary>
    Panda = 5
End Enum

Public Enum LinerStyle
    ''' <summary>自然：沿上眼皮，眼尾不拉長。</summary>
    Natural = 0
    ''' <summary>上揚：眼尾微微上揚。</summary>
    Winged = 1
    ''' <summary>貓眼：粗、眼尾拉長上揚。</summary>
    Cat = 2
    ''' <summary>內眼線：細、貼睫毛根部。</summary>
    Inner = 3
    ''' <summary>下眼線：沿下眼皮。</summary>
    Lower = 4
End Enum

Public Enum LashStyle
    Natural = 0
    ''' <summary>濃密：根數多、粗。</summary>
    Thick = 1
    ''' <summary>捲翹：比較彎。</summary>
    Curl = 2
    ''' <summary>根根分明：一簇一簇。</summary>
    Separated = 3
    ''' <summary>下睫毛：上下都有。</summary>
    Lower = 4
End Enum

Public Enum IrisStyle
    ''' <summary>自然：只換顏色。</summary>
    Natural = 0
    ''' <summary>放大：黑眼球變大。</summary>
    Enlarge = 1
    ''' <summary>外圈：深色邊框。</summary>
    Ring = 2
    ''' <summary>混血：內淺外深。</summary>
    Mixed = 3
    ''' <summary>亮眼：加眼神光。</summary>
    Bright = 4
End Enum

Public Enum FoldStyle
    ''' <summary>平行：整條摺痕和眼皮平行。</summary>
    Parallel = 0
    ''' <summary>開扇：內眼角窄、往眼尾變寬。</summary>
    Fan = 1
    ''' <summary>內雙：很窄、很淡。</summary>
    Inner = 2
    ''' <summary>歐式：寬、深。</summary>
    Euro = 3
End Enum

Public Enum BrowStyle
    ''' <summary>原眉加深：形狀不變。</summary>
    Darken = 0
    ''' <summary>自然：稍微修順。</summary>
    Natural = 1
    ''' <summary>平眉：眉峰壓平。</summary>
    Flat = 2
    ''' <summary>挑眉：眉峰拉高。</summary>
    Arch = 3
    ''' <summary>柳葉眉：細、眉尾下彎。</summary>
    Willow = 4
End Enum

Public Enum BlushStyle
    ''' <summary>蘋果肌：圓形在笑肌。</summary>
    Apple = 0
    ''' <summary>斜刷：從顴骨往太陽穴斜上。</summary>
    Slant = 1
    ''' <summary>曬傷妝：橫過鼻樑與兩頰。</summary>
    Sunburn = 2
    ''' <summary>微醺：眼下、範圍大、偏粉。</summary>
    Tipsy = 3
End Enum

Public Enum HighlightStyle
    ''' <summary>鼻樑。</summary>
    Nose = 0
    ''' <summary>顴骨。</summary>
    Cheek = 1
    ''' <summary>眉骨。</summary>
    BrowBone = 2
    ''' <summary>唇峰。</summary>
    LipPeak = 3
    ''' <summary>全臉（以上都有）。</summary>
    All = 4
End Enum
