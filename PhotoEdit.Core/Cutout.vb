Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO

Public Enum CutoutModel
    ''' <summary>IS-Net 通用：人像、動物、物品。</summary>
    General = 0
    ''' <summary>U²-Net 人像：人物輪廓與頭髮較準。</summary>
    Human = 1
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

''' <summary>
''' 去背設定。AI 算出的遮罩存在照片旁的附屬檔（MaskStore），配方只記模型、修正筆觸、邊緣與背景。
''' </summary>
Public Class CutoutSettings
    Public Property Model As CutoutModel
    Public Property Strokes As List(Of CutoutStroke) = New List(Of CutoutStroke)()
    ''' <summary>邊緣羽化，0..100。</summary>
    Public Property Feather As Integer = 15
    ''' <summary>內縮（負）／外擴（正），-50..50。</summary>
    Public Property Shift As Integer
    Public Property Background As CutoutBackground = CutoutBackground.Transparent
    Public Property BackgroundColorArgb As Integer = Drawing.Color.White.ToArgb()
    ''' <summary>背景模糊程度，0..100。</summary>
    Public Property BackgroundBlur As Integer = 60
    ''' <summary>換成圖片時的圖片路徑（完整路徑）。</summary>
    Public Property BackgroundImagePath As String

    Public Function Clone() As CutoutSettings
        Dim c = DirectCast(MemberwiseClone(), CutoutSettings)
        c.Strokes = Strokes?.Select(Function(s) s.Clone()).ToList()
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
        Return photoPath & ".pedit.mask-" & If(model = CutoutModel.Human, "human", "general") & ".png"
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
        For Each m In {CutoutModel.General, CutoutModel.Human}
            Dim p = MaskPath(photoPath, m)
            If File.Exists(p) Then File.Delete(p)
        Next
    End Sub
End Class
