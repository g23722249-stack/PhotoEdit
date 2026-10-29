Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO

Public Enum CutoutModel
    ''' <summary>IS-Net 通用：人像、動物、物品。</summary>
    General = 0
    ''' <summary>U²-Net 人像：人物輪廓與頭髮較準。</summary>
    Human = 1
    ''' <summary>BiRefNet lite（MIT）：新的一般模型，邊緣與細節比 IS-Net 準。</summary>
    BiRefNetLite = 2
    ''' <summary>MODNet（Apache-2.0）：新的人像模型，直接算出頭髮的半透明。</summary>
    ModNet = 3
    ''' <summary>BiRefNet HR Matting（MIT，fp16）：精細，2048 輸入，最慢。</summary>
    BiRefNetHR = 4
End Enum

Public Enum CutoutBackground
    ''' <summary>保留原背景（只用遮罩做貼圖或檢視）。</summary>
    Original = 0
    Transparent = 1
    Color = 2
    ''' <summary>模糊原背景（真正依主體範圍的景深）。</summary>
    Blur = 3
    Image = 4
    ''' <summary>檢視遮罩用：背景蓋上半透明紅色（不會存進配方）。</summary>
    MaskPreview = 99
End Enum

''' <summary>去背修正筆觸：Keep = 保留（補回主體），否則擦除（去掉背景）。座標同 SpotStroke（已轉正原圖 0..1）。</summary>
Public Class CutoutStroke
    Inherits SpotStroke
    Public Property Keep As Boolean

    Public Shadows Function Clone() As CutoutStroke
        Return New CutoutStroke With {.Radius = Radius, .Path = New List(Of Double)(Path), .Keep = Keep}
    End Function
End Class

''' <summary>魔術棒的一次點擊：位置為已轉正原圖的 0..1 座標。</summary>
Public Class WandClick
    Public Property X As Double
    Public Property Y As Double
    ''' <summary>相似程度 0..100（0 只選完全相同的顏色）。</summary>
    Public Property Tolerance As Integer = 25
    ''' <summary>True：只選和點擊處相連的區域；False：整張照片相近的顏色。</summary>
    Public Property Contiguous As Boolean = True
    ''' <summary>True：補回（加回主體）；False：去除。</summary>
    Public Property Restore As Boolean

    Public Function Clone() As WandClick
        Return DirectCast(MemberwiseClone(), WandClick)
    End Function
End Class

''' <summary>去背模型的資訊：檔名、輸入大小、選單上的名稱與順序。</summary>
Public NotInheritable Class CutoutModels
    Private Sub New()
    End Sub

    ''' <summary>選單上的順序：新的在前（精細最慢放第三），舊模型留著給已經用它去背的文件。</summary>
    Public Shared ReadOnly Order As CutoutModel() = {CutoutModel.BiRefNetLite, CutoutModel.ModNet, CutoutModel.BiRefNetHR, CutoutModel.General, CutoutModel.Human}

    ''' <summary>Models\ 下的檔名（和 ModelCatalog.json 的 path 一致）。</summary>
    Public Shared Function FileName(model As CutoutModel) As String
        Select Case model
            Case CutoutModel.Human : Return "u2net_human_seg.onnx"
            Case CutoutModel.BiRefNetLite : Return "birefnet-lite.onnx"
            Case CutoutModel.ModNet : Return "modnet.onnx"
            Case CutoutModel.BiRefNetHR : Return "birefnet-hr-matting-fp16.onnx"
            Case Else : Return "isnet-general-use.onnx"
        End Select
    End Function

    Public Shared Function DisplayName(model As CutoutModel) As String
        Select Case model
            Case CutoutModel.BiRefNetLite : Return Lang.T("一般（BiRefNet）")
            Case CutoutModel.ModNet : Return Lang.T("人像（MODNet）")
            Case CutoutModel.BiRefNetHR : Return Lang.T("精細（BiRefNet HR，較慢）")
            Case CutoutModel.Human : Return Lang.T("人像（舊：U²-Net）")
            Case Else : Return Lang.T("一般（舊：IS-Net）")
        End Select
    End Function

    ''' <summary>人像模型（只找人）。</summary>
    Public Shared Function IsPortrait(model As CutoutModel) As Boolean
        Return model = CutoutModel.Human OrElse model = CutoutModel.ModNet
    End Function

    ''' <summary>
    ''' 模型輸入的邊長（像素）：遮罩放大回原尺寸時，一個模型像素蓋住「長邊 ÷ 這個數」個像素——邊緣精修的半徑以這個為準。
    ''' MODNet 依照片比例（短邊 512），這裡用長邊大約的值。
    ''' </summary>
    Public Shared Function InputSize(model As CutoutModel) As Integer
        Select Case model
            Case CutoutModel.Human : Return 320
            Case CutoutModel.ModNet : Return 768
            Case CutoutModel.BiRefNetHR : Return 2048
            Case Else : Return 1024
        End Select
    End Function

    ''' <summary>有下載的模型裡，人像／一般各自最好的一個（都沒有時給新的那個，按下去會提示去下載）。</summary>
    Public Shared Function Preferred(portrait As Boolean, available As Func(Of CutoutModel, Boolean)) As CutoutModel
        Dim choices = If(portrait, {CutoutModel.ModNet, CutoutModel.Human, CutoutModel.BiRefNetLite, CutoutModel.General},
                                   {CutoutModel.BiRefNetLite, CutoutModel.General})
        For Each m In choices
            If available(m) Then Return m
        Next
        Return choices(0)
    End Function
End Class

''' <summary>
''' 去背設定。AI 算出的遮罩存在照片旁的附屬檔（MaskStore），配方只記模型、修正筆觸、邊緣與背景。
''' </summary>

Public Class CutoutSettings
    Public Property Model As CutoutModel
    Public Property Strokes As List(Of CutoutStroke) = New List(Of CutoutStroke)()
    ''' <summary>魔術棒點擊（依序套用，在 AI 遮罩之後、修正筆觸之前）。</summary>
    Public Property Wand As List(Of WandClick) = New List(Of WandClick)()
    ''' <summary>
    ''' 只保留這些範圍（原圖座標的選取區，全部取交集）：選取分頁的「當作去背範圍」與「刪除」。
    ''' </summary>
    Public Property Regions As List(Of SelectionSpec)
    ''' <summary>魔術棒選取後清除雜點（小斑點、小破洞）。</summary>
    Public Property WandDespeckle As Boolean = True
    ''' <summary>背景色填充：從圖片四邊往內，把和背景色相近、而且與邊緣相連的區域去掉（白底插圖去背用）。</summary>
    Public Property EdgeFill As Boolean
    ''' <summary>背景色填充的相似程度 0..100。</summary>
    Public Property EdgeFillTolerance As Integer = 15
    ''' <summary>背景色；0 表示自動（取四邊最常見的顏色）。</summary>
    Public Property EdgeFillColorArgb As Integer
    ''' <summary>邊緣羽化，0..100。</summary>
    Public Property Feather As Integer = 15
    ''' <summary>內縮（負）／外擴（正），-50..50。</summary>
    Public Property Shift As Integer
    ''' <summary>邊緣精修 0..100：用照片當導引把 AI 遮罩的邊緣貼齊真正的輪廓（髮絲、毛邊）。舊文件沒有這個值＝0（不變）。</summary>
    Public Property EdgeRefine As Integer
    ''' <summary>去除邊緣殘色：半透明的邊緣扣掉附近的背景色，換背景時不會留下一圈原背景色。</summary>
    Public Property EdgeDecontaminate As Boolean
    Public Property Background As CutoutBackground = CutoutBackground.Transparent
    Public Property BackgroundColorArgb As Integer = Drawing.Color.White.ToArgb()
    ''' <summary>背景模糊程度，0..100。</summary>
    Public Property BackgroundBlur As Integer = 60
    ''' <summary>換成圖片時的圖片路徑（完整路徑）。</summary>
    Public Property BackgroundImagePath As String

    Public Function Clone() As CutoutSettings
        Dim c = DirectCast(MemberwiseClone(), CutoutSettings)
        c.Strokes = Strokes?.Select(Function(s) s.Clone()).ToList()
        c.Wand = Wand?.Select(Function(w) w.Clone()).ToList()
        c.Regions = Regions?.Select(Function(r) r.Clone()).ToList()
        Return c
    End Function

    ''' <summary>會改變畫面（原背景且沒有其他處理時，去背只是準備好遮罩）。</summary>
    Public ReadOnly Property ChangesImage As Boolean
        Get
            Return Background <> CutoutBackground.Original
        End Get
    End Property
End Class

''' <summary>AI 去背遮罩的附屬檔：「照片檔名.pedit.mask-general.png」（灰階，白 = 主體），長邊最多 2048。</summary>
Public NotInheritable Class MaskStore
    Private Sub New()
    End Sub

    Public Const MaxSide As Integer = 2048

    Public Shared Function MaskPath(photoPath As String, model As CutoutModel) As String
        Dim name As String
        Select Case model
            Case CutoutModel.General : name = "general"
            Case CutoutModel.Human : name = "human"
            Case Else : name = model.ToString().ToLowerInvariant()
        End Select
        Return photoPath & ".pedit.mask-" & name & ".png"
    End Function

    ''' <summary>讀取遮罩（不鎖檔）；沒有或壞掉時回傳 Nothing。</summary>
    Public Shared Function Load(photoPath As String, model As CutoutModel) As Bitmap
        Dim p = MaskPath(photoPath, model)
        If Not File.Exists(p) Then Return Nothing
        Try
            Using ms As New MemoryStream(File.ReadAllBytes(p)), img = Image.FromStream(ms)
                Dim bmp As New Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(bmp)
                    g.DrawImage(img, 0, 0, img.Width, img.Height)
                End Using
                Return bmp
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException
            Return Nothing
        End Try
    End Function

    Public Shared Sub Save(photoPath As String, model As CutoutModel, mask As Bitmap)
        mask.Save(MaskPath(photoPath, model), ImageFormat.Png)
    End Sub

    Public Shared Sub DeleteAll(photoPath As String)
        For Each m In [Enum].GetValues(Of CutoutModel)()
            Dim p = MaskPath(photoPath, m)
            If File.Exists(p) Then File.Delete(p)
        Next
    End Sub
End Class
