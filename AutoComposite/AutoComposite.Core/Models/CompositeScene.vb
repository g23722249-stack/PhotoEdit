Imports System.Text.Json.Serialization

''' <summary>
''' 一個人物圖層。可序列化的參數（路徑、變換、色彩、陰影、錨點）存進專案 JSON；
''' 影像與遮罩（<see cref="JsonIgnoreAttribute"/> 標記的屬性）只在記憶體，遮罩另存 16 位元 PNG。
''' </summary>
Public NotInheritable Class CompositeLayer
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property Name As String = "人物"
    ''' <summary>人物原圖路徑。</summary>
    Public Property SourcePath As String = ""
    ''' <summary>精修後的 Alpha 遮罩檔（16 位元 PNG，存專案時寫入）。</summary>
    Public Property MaskPath As String = ""
    ''' <summary>AI 原始遮罩檔（用於「還原遮罩」）。</summary>
    Public Property AiMaskPath As String = ""
    ''' <summary>產生遮罩的模型 ID 與鎖定的 commit；手動或原圖 Alpha 時為 "manual"／"source-alpha"。</summary>
    Public Property MaskModelId As String = ""
    Public Property MaskModelRevision As String = ""

    ''' <summary>人物原圖上的錨點（像素座標）。</summary>
    Public Property AnchorKind As AnchorKind = AnchorKind.FeetCenter
    Public Property AnchorX As Double
    Public Property AnchorY As Double

    Public Property Transform As New LayerTransform()
    ''' <summary>人物在背景上的實際變換矩陣（人物像素 → 背景像素），由 Transform 計算後一併保存供外部工具讀取。</summary>
    Public Property MatrixSnapshot As Double()
    Public Property Opacity As Double = 1
    Public Property Color As New ColorParams()
    Public Property Shadow As New ShadowParams()
    ''' <summary>只遮這個人物的遮擋遮罩檔（背景座標，16 位元 PNG）。</summary>
    Public Property OcclusionMaskPath As String = ""
    Public Property Locked As Boolean
    Public Property Visible As Boolean = True

    ' ---- 執行期資料 ----

    ''' <summary>人物原圖（已轉正）。</summary>
    <JsonIgnore> Public Property Source As RgbaImage
    ''' <summary>精修後 Alpha（與 Source 同尺寸）。</summary>
    <JsonIgnore> Public Property Alpha As FloatMask
    ''' <summary>AI 原始 Alpha（還原用）。</summary>
    <JsonIgnore> Public Property AiAlpha As FloatMask
    ''' <summary>此圖層專屬的遮擋遮罩（背景尺寸），Nothing 表示沒有。</summary>
    <JsonIgnore> Public Property OcclusionMask As FloatMask
    <JsonIgnore> Public Property SourceInfo As ImageInfo
    ''' <summary>每次修改 Alpha 或 Source 時遞增，讓快取知道要重算。</summary>
    <JsonIgnore> Public Property ContentVersion As Integer

    Public Sub Touch()
        ContentVersion += 1
    End Sub

    <JsonIgnore>
    Public ReadOnly Property Anchor As PointD
        Get
            Return New PointD(AnchorX, AnchorY)
        End Get
    End Property

    ''' <summary>複製可序列化參數（不含影像；影像參考共用）。</summary>
    Public Function CloneParameters() As CompositeLayer
        Dim c = DirectCast(MemberwiseClone(), CompositeLayer)
        c.Transform = Transform.Clone()
        c.Color = Color.Clone()
        c.Shadow = Shadow.Clone()
        c.MatrixSnapshot = If(MatrixSnapshot Is Nothing, Nothing, CType(MatrixSnapshot.Clone(), Double()))
        Return c
    End Function
End Class

''' <summary>背景上的參考線（兩點），例如石柱左右邊緣，用來推估垂直消失點。</summary>
Public NotInheritable Class GuideLine
    Public Property X1 As Double
    Public Property Y1 As Double
    Public Property X2 As Double
    Public Property Y2 As Double

    <JsonIgnore>
    Public ReadOnly Property P1 As PointD
        Get
            Return New PointD(X1, Y1)
        End Get
    End Property

    <JsonIgnore>
    Public ReadOnly Property P2 As PointD
        Get
            Return New PointD(X2, Y2)
        End Get
    End Property
End Class

''' <summary>背景上的目標放置資訊（使用者指定或自動推薦後確認）。</summary>
Public NotInheritable Class PlacementTarget
    ''' <summary>目標錨點（例如柱頂中心）。</summary>
    Public Property HasAnchor As Boolean
    Public Property AnchorX As Double
    Public Property AnchorY As Double
    ''' <summary>可站立表面的左右端點（例如柱頂平台邊緣），沒有時寬度為 0。</summary>
    Public Property SurfaceLeftX As Double
    Public Property SurfaceRightX As Double
    Public Property SurfaceY As Double
    ''' <summary>人物目標高度（背景像素），0 表示未指定。</summary>
    Public Property TargetHeight As Double
    ''' <summary>人物高度占畫面高度比例（0～1），0 表示未指定；TargetHeight 優先。</summary>
    Public Property TargetHeightFraction As Double

    <JsonIgnore>
    Public ReadOnly Property SurfaceWidth As Double
        Get
            Return Math.Max(0, SurfaceRightX - SurfaceLeftX)
        End Get
    End Property

    Public Function Clone() As PlacementTarget
        Return DirectCast(MemberwiseClone(), PlacementTarget)
    End Function
End Class

''' <summary>匯出設定。</summary>
Public NotInheritable Class ExportSettings
    Public Property Format As ExportFormat = ExportFormat.Png
    Public Property JpegQuality As Integer = 95
    Public Property LastExportPath As String = ""
End Class

''' <summary>
''' 合成場景：一張背景與多個人物圖層（索引 0 在最下層）。所有座標都是背景原圖像素，與 UI 縮放、DPI 無關。
''' </summary>
Public NotInheritable Class CompositeScene
    Public Property BackgroundPath As String = ""
    Public Property Layers As New List(Of CompositeLayer)()
    ''' <summary>前景物件遮罩檔（背景中應該擋在所有人物前面的部分）。</summary>
    Public Property ForegroundMaskPath As String = ""
    ''' <summary>保護遮罩檔（不希望被人物遮住的物體，定位評分用）。</summary>
    Public Property ProtectMaskPath As String = ""
    ''' <summary>陰影接收遮罩檔（Receiver = CustomMask 時使用）。</summary>
    Public Property ShadowReceiverMaskPath As String = ""
    ''' <summary>遮擋遮罩的羽化（背景像素 sigma）。</summary>
    Public Property OcclusionFeather As Double = 1.5
    Public Property Target As New PlacementTarget()
    Public Property Guides As New List(Of GuideLine)()
    Public Property Export As New ExportSettings()

    ' ---- 執行期資料 ----
    <JsonIgnore> Public Property Background As RgbaImage
    <JsonIgnore> Public Property BackgroundInfo As ImageInfo
    <JsonIgnore> Public Property ForegroundMask As FloatMask
    <JsonIgnore> Public Property ProtectMask As FloatMask
    <JsonIgnore> Public Property ShadowReceiverMask As FloatMask
    ''' <summary>自動估計的天空遮罩（1 = 天空），由場景分析產生，不存檔。</summary>
    <JsonIgnore> Public Property SkyMask As FloatMask
    ''' <summary>遮擋相關遮罩修改時遞增。</summary>
    <JsonIgnore> Public Property MaskVersion As Integer

    Public Sub TouchMasks()
        MaskVersion += 1
    End Sub

    ''' <summary>
    ''' 給背景執行緒算圖用的快照：參數複製一份，影像與遮罩共用參考（修改遮罩一律「先複製再改」，所以共用是安全的）。
    ''' UI 執行緒可以繼續修改原場景，不會和算圖互相干擾。
    ''' </summary>
    Public Function CloneForRender() As CompositeScene
        Dim c = DirectCast(MemberwiseClone(), CompositeScene)
        c.Layers = Layers.Select(Function(l) l.CloneParameters()).ToList()
        c.Target = Target.Clone()
        c.Guides = Guides.ToList()
        Return c
    End Function

    <JsonIgnore>
    Public ReadOnly Property Width As Integer
        Get
            Return If(Background Is Nothing, 0, Background.Width)
        End Get
    End Property

    <JsonIgnore>
    Public ReadOnly Property Height As Integer
        Get
            Return If(Background Is Nothing, 0, Background.Height)
        End Get
    End Property
End Class
