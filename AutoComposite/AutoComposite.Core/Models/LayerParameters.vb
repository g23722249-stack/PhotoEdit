Imports System.Text.Json.Serialization

''' <summary>人物圖上的錨點種類（與背景目標錨點對齊用）。</summary>
Public Enum AnchorKind
    ''' <summary>兩腳之間的地面接觸點（由輪廓自動估算）。</summary>
    FeetCenter
    ''' <summary>單腳接觸點（使用者在人物圖上點選）。</summary>
    SingleFoot
    ''' <summary>前景外框底部中心。</summary>
    BottomCenter
    ''' <summary>人物重心。</summary>
    Center
    ''' <summary>頭頂附近。</summary>
    Head
    ''' <summary>使用者自訂的任意點。</summary>
    Custom
End Enum

''' <summary>透視模式：預設不變形；有足夠控制資訊時才啟用。</summary>
Public Enum PerspectiveMode
    None
    ''' <summary>仰角／俯角梯形：上緣寬度相對下緣的比例（垂直線收斂）。</summary>
    Keystone
    ''' <summary>四角控制點自由透視。</summary>
    Corners
End Enum

''' <summary>透視參數（在人物錨點為原點的局部座標中套用，之後才縮放、旋轉、平移）。</summary>
Public NotInheritable Class PerspectiveParams
    Public Property Mode As PerspectiveMode = PerspectiveMode.None
    ''' <summary>上緣寬度 ÷ 下緣寬度（1 = 不收斂；仰角拍攝時小於 1）。限制在 0.6～1.4。</summary>
    Public Property KeystoneRatio As Double = 1
    ''' <summary>垂直方向的壓縮（1 = 不壓縮）。限制在 0.8～1.2，避免拉長四肢。</summary>
    Public Property VerticalFactor As Double = 1
    ''' <summary>Corners 模式：左上、右上、右下、左下四角在人物原圖像素座標的位移量。</summary>
    Public Property CornerOffsets As PointD() = {New PointD(), New PointD(), New PointD(), New PointD()}

    Public Function Clone() As PerspectiveParams
        Return New PerspectiveParams With {.Mode = Mode, .KeystoneRatio = KeystoneRatio, .VerticalFactor = VerticalFactor,
            .CornerOffsets = If(CornerOffsets, {New PointD(), New PointD(), New PointD(), New PointD()}).ToArray()}
    End Function
End Class

''' <summary>人物圖層的幾何變換（座標一律是背景原圖像素）。</summary>
Public NotInheritable Class LayerTransform
    ''' <summary>人物錨點在背景上的位置。</summary>
    Public Property X As Double
    Public Property Y As Double
    ''' <summary>等比縮放倍率（人物原圖像素 → 背景像素）。</summary>
    Public Property Scale As Double = 1
    ''' <summary>非等比時的額外水平倍率（預設 1，選配）。</summary>
    Public Property ScaleXFactor As Double = 1
    ''' <summary>繞錨點旋轉角度（度，順時針為正）。</summary>
    Public Property Rotation As Double
    Public Property FlipHorizontal As Boolean
    Public Property Perspective As New PerspectiveParams()

    Public Function Clone() As LayerTransform
        Return New LayerTransform With {.X = X, .Y = Y, .Scale = Scale, .ScaleXFactor = ScaleXFactor, .Rotation = Rotation,
            .FlipHorizontal = FlipHorizontal, .Perspective = If(Perspective, New PerspectiveParams()).Clone()}
    End Function
End Class

''' <summary>色彩融合模式。</summary>
Public Enum ColorMode
    ''' <summary>不改變人物風格。</summary>
    Original
    ''' <summary>輕度調整色溫、亮度與對比以匹配放置處的背景。</summary>
    EnvironmentMatch
    ''' <summary>使用者自行設定全部參數。</summary>
    Custom
End Enum

''' <summary>人物色彩調整參數（在 sRGB 0..1 空間套用）。</summary>
Public NotInheritable Class ColorParams
    Public Property Mode As ColorMode = ColorMode.Original
    ''' <summary>亮度位移（-0.5～0.5）。</summary>
    Public Property Brightness As Double
    ''' <summary>對比倍率（1 = 不變）。</summary>
    Public Property Contrast As Double = 1
    ''' <summary>飽和度倍率（1 = 不變）。</summary>
    Public Property Saturation As Double = 1
    ''' <summary>色溫（-1 冷 ～ 1 暖）。</summary>
    Public Property Temperature As Double
    ''' <summary>色調（-1 綠 ～ 1 洋紅）。</summary>
    Public Property Tint As Double
    Public Property GainR As Double = 1
    Public Property GainG As Double = 1
    Public Property GainB As Double = 1
    ''' <summary>Gamma（1 = 不變，大於 1 變亮）。</summary>
    Public Property Gamma As Double = 1
    ''' <summary>調整後與原色的混合強度（0 = 原色，1 = 完全套用）。</summary>
    Public Property Strength As Double = 1
    ''' <summary>景深模糊半徑（背景原圖像素的 sigma，0 = 不模糊）。</summary>
    Public Property DepthBlur As Double

    Public Function Clone() As ColorParams
        Return DirectCast(MemberwiseClone(), ColorParams)
    End Function

    ''' <summary>色彩運算是否等同不變（Original 模式或所有參數都是預設值）。</summary>
    <JsonIgnore>
    Public ReadOnly Property IsIdentity As Boolean
        Get
            If Mode = ColorMode.Original Then Return True
            Return Brightness = 0 AndAlso Contrast = 1 AndAlso Saturation = 1 AndAlso Temperature = 0 AndAlso Tint = 0 AndAlso
                GainR = 1 AndAlso GainG = 1 AndAlso GainB = 1 AndAlso Gamma = 1
        End Get
    End Property
End Class

''' <summary>陰影可以落在哪裡。</summary>
Public Enum ShadowReceiver
    ''' <summary>整張背景。</summary>
    Everywhere
    ''' <summary>只落在非天空區域（站在柱頂、屋頂時避免陰影浮在天空）。</summary>
    NonSky
    ''' <summary>使用者繪製的接收遮罩。</summary>
    CustomMask
End Enum

''' <summary>陰影參數。投射陰影依人物 Alpha 在接觸點投影到地面，接觸陰影與環境遮蔽另外生成。</summary>
Public NotInheritable Class ShadowParams
    Public Property Enabled As Boolean = True
    ''' <summary>投射陰影在地面上的方向（度；0 = 向右，90 = 朝鏡頭／畫面下方，-90 = 遠離鏡頭）。</summary>
    Public Property Direction As Double = 30
    ''' <summary>投影長度（相對人物高度）。</summary>
    Public Property Length As Double = 0.5
    ''' <summary>地面透視壓扁比例（0.1～1）。</summary>
    Public Property GroundFlatten As Double = 0.35
    Public Property OffsetX As Double
    Public Property OffsetY As Double
    ''' <summary>投射陰影模糊（背景原圖像素的 sigma）。</summary>
    Public Property BlurRadius As Double = 6
    ''' <summary>投射陰影不透明度（0～1）。</summary>
    Public Property Opacity As Double = 0.28
    Public Property ColorR As Byte = 24
    Public Property ColorG As Byte = 26
    Public Property ColorB As Byte = 36
    ''' <summary>投射陰影整體縮放。</summary>
    Public Property Scale As Double = 1
    ''' <summary>接觸陰影強度（0～1）。</summary>
    Public Property ContactStrength As Double = 0.55
    ''' <summary>接觸陰影模糊（相對腳寬）。</summary>
    Public Property ContactSoftness As Double = 0.25
    ''' <summary>環境遮蔽近似強度（0～1）。</summary>
    Public Property AmbientOcclusion As Double = 0.25
    ''' <summary>離腳越遠陰影越淡（0 = 不衰減，1 = 頭部影子完全消失）。</summary>
    Public Property Falloff As Double = 0.55
    Public Property Receiver As ShadowReceiver = ShadowReceiver.Everywhere

    Public Function Clone() As ShadowParams
        Return DirectCast(MemberwiseClone(), ShadowParams)
    End Function
End Class
