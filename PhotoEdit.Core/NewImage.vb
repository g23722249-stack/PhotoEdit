Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>新增影像的尺寸單位。</summary>
Public Enum SizeUnit
    Pixels = 0
    Inches = 1
    Centimeters = 2
    Millimeters = 3
End Enum

''' <summary>新增影像的底色。</summary>
Public Enum NewImageFill
    White = 0
    Black = 1
    Custom = 2
    Transparent = 3
End Enum

''' <summary>底圖放進畫布的方式。</summary>
Public Enum PictureFit
    ''' <summary>等比放大到蓋滿畫布，超出的部分裁掉。</summary>
    Cover = 0
    ''' <summary>等比縮放到整張放得下，四周露出底色。</summary>
    Contain = 1
    ''' <summary>拉成畫布大小（可能變形）。</summary>
    Stretch = 2
    ''' <summary>原尺寸置中。</summary>
    Center = 3
    ''' <summary>原尺寸重複並排。</summary>
    Tile = 4
End Enum

''' <summary>預設版面（相片、證件照、紙張、螢幕、社群）。尺寸是直向或原本的方向；Unit 為像素時與解析度無關。</summary>
Public Class CanvasPreset
    Public ReadOnly Property Category As String
    Public ReadOnly Property Name As String
    Public ReadOnly Property Width As Double
    Public ReadOnly Property Height As Double
    Public ReadOnly Property Unit As SizeUnit

    Public Sub New(category As String, name As String, width As Double, height As Double, unit As SizeUnit)
        Me.Category = category
        Me.Name = name
        Me.Width = width
        Me.Height = height
        Me.Unit = unit
    End Sub

    Public Overrides Function ToString() As String
        Return Name
    End Function

    Public Shared ReadOnly BuiltIn As IReadOnlyList(Of CanvasPreset) = New List(Of CanvasPreset) From {
        New CanvasPreset("相片", "相片大小 3.5 x 5 英吋（3×5）", 3.5, 5, SizeUnit.Inches),
        New CanvasPreset("相片", "相片大小 4 x 6 英吋（4×6）", 4, 6, SizeUnit.Inches),
        New CanvasPreset("相片", "相片大小 5 x 7 英吋", 5, 7, SizeUnit.Inches),
        New CanvasPreset("相片", "相片大小 6 x 8 英吋", 6, 8, SizeUnit.Inches),
        New CanvasPreset("相片", "相片大小 8 x 10 英吋", 8, 10, SizeUnit.Inches),
        New CanvasPreset("相片", "相片大小 8 x 12 英吋", 8, 12, SizeUnit.Inches),
        New CanvasPreset("證件照", "1 吋大頭照 2.8 x 3.5 公分", 2.8, 3.5, SizeUnit.Centimeters),
        New CanvasPreset("證件照", "2 吋大頭照 3.5 x 4.5 公分（身分證、護照）", 3.5, 4.5, SizeUnit.Centimeters),
        New CanvasPreset("證件照", "美國簽證 2 x 2 英吋", 2, 2, SizeUnit.Inches),
        New CanvasPreset("紙張", "A3 297 x 420 公釐", 297, 420, SizeUnit.Millimeters),
        New CanvasPreset("紙張", "A4 210 x 297 公釐", 210, 297, SizeUnit.Millimeters),
        New CanvasPreset("紙張", "A5 148 x 210 公釐", 148, 210, SizeUnit.Millimeters),
        New CanvasPreset("紙張", "B5 182 x 257 公釐", 182, 257, SizeUnit.Millimeters),
        New CanvasPreset("紙張", "Letter 8.5 x 11 英吋", 8.5, 11, SizeUnit.Inches),
        New CanvasPreset("紙張", "Legal 8.5 x 14 英吋", 8.5, 14, SizeUnit.Inches),
        New CanvasPreset("紙張", "明信片 100 x 148 公釐", 100, 148, SizeUnit.Millimeters),
        New CanvasPreset("紙張", "名片 90 x 54 公釐", 90, 54, SizeUnit.Millimeters),
        New CanvasPreset("螢幕", "640 x 480", 640, 480, SizeUnit.Pixels),
        New CanvasPreset("螢幕", "800 x 600", 800, 600, SizeUnit.Pixels),
        New CanvasPreset("螢幕", "1024 x 768", 1024, 768, SizeUnit.Pixels),
        New CanvasPreset("螢幕", "HD 1280 x 720", 1280, 720, SizeUnit.Pixels),
        New CanvasPreset("螢幕", "Full HD 1920 x 1080", 1920, 1080, SizeUnit.Pixels),
        New CanvasPreset("螢幕", "2K 2560 x 1440", 2560, 1440, SizeUnit.Pixels),
        New CanvasPreset("螢幕", "4K 3840 x 2160", 3840, 2160, SizeUnit.Pixels),
        New CanvasPreset("社群", "IG 貼文 1080 x 1080", 1080, 1080, SizeUnit.Pixels),
        New CanvasPreset("社群", "IG 直式貼文 1080 x 1350", 1080, 1350, SizeUnit.Pixels),
        New CanvasPreset("社群", "限時動態／短影音 1080 x 1920", 1080, 1920, SizeUnit.Pixels),
        New CanvasPreset("社群", "FB 封面 851 x 315", 851, 315, SizeUnit.Pixels),
        New CanvasPreset("社群", "FB 分享圖 1200 x 630", 1200, 630, SizeUnit.Pixels),
        New CanvasPreset("社群", "YouTube 縮圖 1280 x 720", 1280, 720, SizeUnit.Pixels),
        New CanvasPreset("圖示", "圖示 32 x 32", 32, 32, SizeUnit.Pixels),
        New CanvasPreset("圖示", "圖示 256 x 256", 256, 256, SizeUnit.Pixels),
        New CanvasPreset("圖示", "圖示 512 x 512", 512, 512, SizeUnit.Pixels)
    }

    Public Shared ReadOnly Property Categories As IReadOnlyList(Of String)
        Get
            Return BuiltIn.Select(Function(p) p.Category).Distinct().ToList()
        End Get
    End Property

    ''' <summary>依目前解析度換算成像素。</summary>
    Public Function PixelSize(dpi As Double) As Size
        Return New Size(NewImage.ToPixels(Width, Unit, dpi), NewImage.ToPixels(Height, Unit, dpi))
    End Function
End Class

''' <summary>新增影像的設定。</summary>
Public Class NewImageSpec
    Public Property Width As Integer = 1050
    Public Property Height As Integer = 1500
    ''' <summary>解析度（像素／英吋），存進檔案供列印時使用。</summary>
    Public Property Dpi As Double = 300
    Public Property Fill As NewImageFill = NewImageFill.White
    Public Property CustomColor As Color = Color.White
    ''' <summary>底圖（Nothing 表示不放底圖）。由呼叫端負責釋放。</summary>
    Public Property Picture As Image
    Public Property Fit As PictureFit = PictureFit.Cover

    ''' <summary>實際的底色（透明時為 Color.Transparent）。</summary>
    Public ReadOnly Property FillColor As Color
        Get
            Select Case Fill
                Case NewImageFill.Black : Return Color.Black
                Case NewImageFill.Custom : Return Color.FromArgb(255, CustomColor)
                Case NewImageFill.Transparent : Return Color.Transparent
                Case Else : Return Color.White
            End Select
        End Get
    End Property

    ''' <summary>未壓縮的影像大小（透明 32 位元、其他 24 位元），和小畫家「檔案大小」的算法相同。</summary>
    Public ReadOnly Property UncompressedBytes As Long
        Get
            Return CLng(Width) * Height * If(Fill = NewImageFill.Transparent, 4, 3)
        End Get
    End Property
End Class

''' <summary>檔案 → 新增：建立空白（或帶底圖）的畫布。</summary>
Public Module NewImage
    Public Const MaxSide As Integer = 20000
    ''' <summary>總像素上限（約 2.5 億，32 位元時約 1 GB 記憶體）。</summary>
    Public Const MaxPixels As Long = 250_000_000L

    Public Function UnitName(unit As SizeUnit) As String
        Select Case unit
            Case SizeUnit.Inches : Return "英吋"
            Case SizeUnit.Centimeters : Return "公分"
            Case SizeUnit.Millimeters : Return "公釐"
            Case Else : Return "像素"
        End Select
    End Function

    ''' <summary>每個單位等於幾英吋。</summary>
    Private Function InchesPer(unit As SizeUnit) As Double
        Select Case unit
            Case SizeUnit.Inches : Return 1
            Case SizeUnit.Centimeters : Return 1 / 2.54
            Case SizeUnit.Millimeters : Return 1 / 25.4
            Case Else : Return 0
        End Select
    End Function

    ''' <summary>長度換算成像素（四捨五入，至少 1）。</summary>
    Public Function ToPixels(value As Double, unit As SizeUnit, dpi As Double) As Integer
        Dim px = If(unit = SizeUnit.Pixels, value, value * InchesPer(unit) * dpi)
        Return CInt(Math.Max(1, Math.Min(Integer.MaxValue, Math.Round(px))))
    End Function

    ''' <summary>像素換算成指定單位的長度。</summary>
    Public Function FromPixels(pixels As Integer, unit As SizeUnit, dpi As Double) As Double
        If unit = SizeUnit.Pixels OrElse dpi <= 0 Then Return pixels
        Return pixels / dpi / InchesPer(unit)
    End Function

    ''' <summary>尺寸不合理時回傳說明，可以建立時回傳 Nothing。</summary>
    Public Function Validate(spec As NewImageSpec) As String
        If spec.Width < 1 OrElse spec.Height < 1 Then Return "寬度與高度至少要 1 像素。"
        If spec.Width > MaxSide OrElse spec.Height > MaxSide Then Return $"寬度與高度最多 {MaxSide:N0} 像素。"
        If CLng(spec.Width) * spec.Height > MaxPixels Then Return $"影像太大（{CLng(spec.Width) * spec.Height:N0} 像素），最多 {MaxPixels:N0} 像素。"
        Return Nothing
    End Function

    ''' <summary>「4,615 KB」或「12.3 MB」。</summary>
    Public Function FormatFileSize(bytes As Long) As String
        Dim kb = bytes / 1024.0
        If kb < 10240 Then Return $"{Math.Round(kb):N0} KB"
        Return $"{kb / 1024:N1} MB"
    End Function

    ''' <summary>
    ''' 建立畫布：先塗底色，再依 Fit 放上底圖。maxDimension &gt; 0 時等比縮小（給預覽用）。
    ''' 傳回 32bppArgb 並記錄解析度。
    ''' </summary>
    Public Function Create(spec As NewImageSpec, Optional maxDimension As Integer = 0) As Bitmap
        Dim scale = 1.0
        If maxDimension > 0 Then scale = Math.Min(1.0, maxDimension / CDbl(Math.Max(spec.Width, spec.Height)))
        Dim w = Math.Max(1, CInt(Math.Round(spec.Width * scale)))
        Dim h = Math.Max(1, CInt(Math.Round(spec.Height * scale)))
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        If spec.Dpi > 0 Then bmp.SetResolution(CSng(spec.Dpi * scale), CSng(spec.Dpi * scale))
        Using g = Graphics.FromImage(bmp)
            g.Clear(spec.FillColor)
            If spec.Picture IsNot Nothing Then
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.CompositingQuality = CompositingQuality.HighQuality
                DrawPicture(g, spec.Picture, w, h, spec.Fit, scale)
            End If
        End Using
        Return bmp
    End Function

    ''' <summary>底圖在畫布上的位置（Tile 時為第一塊的位置）。scale 是預覽縮小比例，原尺寸類的模式要跟著縮。</summary>
    Public Function PictureBounds(pictureSize As Size, canvasWidth As Integer, canvasHeight As Integer, fit As PictureFit, Optional scale As Double = 1) As RectangleF
        Dim pw = CDbl(pictureSize.Width), ph = CDbl(pictureSize.Height)
        Select Case fit
            Case PictureFit.Stretch
                Return New RectangleF(0, 0, canvasWidth, canvasHeight)
            Case PictureFit.Cover, PictureFit.Contain
                Dim sx = canvasWidth / pw, sy = canvasHeight / ph
                Dim s = If(fit = PictureFit.Cover, Math.Max(sx, sy), Math.Min(sx, sy))
                Dim dw = pw * s, dh = ph * s
                Return New RectangleF(CSng((canvasWidth - dw) / 2), CSng((canvasHeight - dh) / 2), CSng(dw), CSng(dh))
            Case PictureFit.Tile
                Return New RectangleF(0, 0, CSng(pw * scale), CSng(ph * scale))
            Case Else ' Center
                Dim dw = pw * scale, dh = ph * scale
                Return New RectangleF(CSng((canvasWidth - dw) / 2), CSng((canvasHeight - dh) / 2), CSng(dw), CSng(dh))
        End Select
    End Function

    Private Sub DrawPicture(g As Graphics, picture As Image, w As Integer, h As Integer, fit As PictureFit, scale As Double)
        Dim r = PictureBounds(picture.Size, w, h, fit, scale)
        Using attrs As New ImageAttributes()
            attrs.SetWrapMode(WrapMode.TileFlipXY) ' 邊緣不會被內插出半透明的線
            If fit = PictureFit.Tile Then
                Dim tw = Math.Max(1.0F, r.Width), th = Math.Max(1.0F, r.Height)
                Dim y = 0.0F
                While y < h
                    Dim x = 0.0F
                    While x < w
                        g.DrawImage(picture, Rectangle.Round(New RectangleF(x, y, tw, th)), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, attrs)
                        x += tw
                    End While
                    y += th
                End While
            Else
                g.DrawImage(picture, Rectangle.Round(r), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, attrs)
            End If
        End Using
    End Sub
End Module
