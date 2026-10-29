''' <summary>解碼後的影像與原始檔案資訊。</summary>
Public NotInheritable Class DecodedImage
    ''' <summary>已依 EXIF Orientation 轉正的 RGBA。</summary>
    Public Property Image As RgbaImage
    Public Property Info As ImageInfo
End Class

''' <summary>載入時顯示給使用者的影像資訊。</summary>
Public NotInheritable Class ImageInfo
    Public Property SourcePath As String = ""
    Public Property Format As String = ""
    ''' <summary>轉正後的寬高。</summary>
    Public Property Width As Integer
    Public Property Height As Integer
    ''' <summary>例如 "RGB"、"RGBA"、"Gray"、"Indexed"。</summary>
    Public Property ColorMode As String = ""
    Public Property BitsPerChannel As Integer = 8
    ''' <summary>檔案本身帶 Alpha 通道。</summary>
    Public Property HasAlphaChannel As Boolean
    ''' <summary>Alpha 實際有非 255 的值（有透明區域）。</summary>
    Public Property HasTransparency As Boolean
    ''' <summary>原始 EXIF Orientation（1..8，沒有時為 1）。</summary>
    Public Property ExifOrientation As Integer = 1
    ''' <summary>原始檔案的寬高（轉正後）。超大圖縮小載入時與 Width／Height 不同。</summary>
    Public Property OriginalWidth As Integer
    Public Property OriginalHeight As Integer

    ''' <summary>載入時是否因超過像素上限而縮小。</summary>
    Public ReadOnly Property WasDownscaled As Boolean
        Get
            Return OriginalWidth > 0 AndAlso (OriginalWidth <> Width OrElse OriginalHeight <> Height)
        End Get
    End Property

    Public Function Describe() As String
        Dim alpha = If(HasTransparency, "有透明區域", If(HasAlphaChannel, "有 Alpha（全不透明）", "無 Alpha"))
        Dim orient = If(ExifOrientation <> 1, $"，EXIF 方向 {ExifOrientation} 已轉正", "")
        Dim scaled = If(WasDownscaled, $"（原圖 {OriginalWidth}×{OriginalHeight} 超過載入上限，已等比縮小）", "")
        Return $"{Width}×{Height}{scaled}，{Format} {ColorMode} {BitsPerChannel}-bit，{alpha}{orient}"
    End Function
End Class

Public Enum ExportFormat
    Png
    Jpeg
End Enum

''' <summary>
''' 影像編解碼。核心內建無損 PNG（<see cref="PngCodec"/>，遮罩與測試用）；
''' 主程式另外提供以 Windows Imaging Component 實作、支援 JPEG／WebP／EXIF 的版本。
''' </summary>
Public Interface IImageCodec
    Function CanDecode(path As String) As Boolean
    Function Decode(path As String) As DecodedImage
    Sub Encode(image As RgbaImage, path As String, format As ExportFormat, jpegQuality As Integer)
End Interface

''' <summary>EXIF Orientation（1..8）的像素轉正。</summary>
Public NotInheritable Class ExifOrientation
    Private Sub New()
    End Sub

    ''' <summary>依 Orientation 把影像轉成正確方向；1 或無效值原樣回傳。</summary>
    Public Shared Function Apply(img As RgbaImage, orientation As Integer) As RgbaImage
        If orientation < 2 OrElse orientation > 8 Then Return img
        Dim w = img.Width, h = img.Height
        Dim swap = orientation >= 5
        Dim nw = If(swap, h, w), nh = If(swap, w, h)
        Dim dst As New RgbaImage(nw, nh)
        Dim s = img.Pixels, d = dst.Pixels
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim dx As Integer, dy As Integer
                Select Case orientation
                    Case 2 : dx = w - 1 - x : dy = y                 ' 水平翻轉
                    Case 3 : dx = w - 1 - x : dy = h - 1 - y         ' 旋轉 180°
                    Case 4 : dx = x : dy = h - 1 - y                 ' 垂直翻轉
                    Case 5 : dx = y : dy = x                         ' 轉置
                    Case 6 : dx = h - 1 - y : dy = x                 ' 順時針 90°
                    Case 7 : dx = h - 1 - y : dy = w - 1 - x         ' 反轉置
                    Case Else : dx = y : dy = w - 1 - x              ' 8：逆時針 90°
                End Select
                Dim si = (y * w + x) * 4, di = (dy * nw + dx) * 4
                d(di) = s(si) : d(di + 1) = s(si + 1) : d(di + 2) = s(si + 2) : d(di + 3) = s(si + 3)
            Next
        Next
        Return dst
    End Function
End Class
