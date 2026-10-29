Imports System.IO
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports PixelFormat = System.Windows.Media.PixelFormat

''' <summary>
''' 以 Windows Imaging Component 解碼／編碼（透過 WPF 的 System.Windows.Media.Imaging，不用 WPF 介面）。
''' 支援 JPEG、PNG、BMP、TIFF、GIF、ICO、JPEG XR，以及系統安裝了擴充編解碼器時的 WebP、HEIC。
''' 讀取 EXIF Orientation 後轉正；不做色彩描述檔轉換（保留原始數值）。
''' </summary>
Public NotInheritable Class WicImageCodec
    Implements AutoComposite.IImageCodec

    Private Shared ReadOnly Extensions As String() = {".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".tif", ".tiff", ".gif", ".ico", ".jxr", ".wdp", ".webp", ".heic", ".heif", ".avif"}

    Public Shared ReadOnly Property OpenFileFilter As String
        Get
            Return "圖片|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.tif;*.tiff;*.gif;*.webp;*.heic;*.heif;*.avif;*.jxr|所有檔案|*.*"
        End Get
    End Property

    Public Function CanDecode(path As String) As Boolean Implements AutoComposite.IImageCodec.CanDecode
        Return Extensions.Contains(IO.Path.GetExtension(path).ToLowerInvariant())
    End Function

    Public Function Decode(path As String) As AutoComposite.DecodedImage Implements AutoComposite.IImageCodec.Decode
        Dim decoder As BitmapDecoder
        Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            Try
                decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat Or BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad)
            Catch ex As Exception When TypeOf ex Is NotSupportedException OrElse TypeOf ex Is FileFormatException OrElse TypeOf ex Is Runtime.InteropServices.COMException
                Dim ext = IO.Path.GetExtension(path).ToLowerInvariant()
                Dim hint = If(ext = ".webp" OrElse ext = ".heic" OrElse ext = ".heif" OrElse ext = ".avif",
                              "，請從 Microsoft Store 安裝對應的影像擴充功能（例如「WebP 影像擴充功能」、「HEIF 影像擴充功能」）", "")
                Throw New NotSupportedException($"Windows 無法解碼此圖片（{ex.Message}）{hint}。", ex)
            End Try
        End Using
        Dim frame = decoder.Frames(0)
        Dim w = frame.PixelWidth, h = frame.PixelHeight
        AutoComposite.ImageLimits.CheckSize(w, h, 4)
        Dim fmt = frame.Format
        Dim conv As New FormatConvertedBitmap(frame, PixelFormats.Bgra32, Nothing, 0)
        Dim stride = w * 4
        Dim px(stride * h - 1) As Byte
        conv.CopyPixels(px, stride, 0)
        ' BGRA → RGBA
        For i = 0 To px.Length - 1 Step 4
            Dim b = px(i)
            px(i) = px(i + 2)
            px(i + 2) = b
        Next
        Dim img As New AutoComposite.RgbaImage(w, h, px)
        Dim orientation = ReadOrientation(frame)
        img = AutoComposite.ExifOrientation.Apply(img, orientation)
        Dim hasAlpha = FormatHasAlpha(fmt, frame.Palette)
        Return New AutoComposite.DecodedImage With {
            .Image = img,
            .Info = New AutoComposite.ImageInfo With {
                .SourcePath = path, .Format = If(decoder.CodecInfo?.FriendlyName, IO.Path.GetExtension(path).TrimStart("."c).ToUpperInvariant()).Replace(" Decoder", "").Replace(" decoder", ""),
                .Width = img.Width, .Height = img.Height, .ColorMode = DescribeFormat(fmt, hasAlpha), .BitsPerChannel = BitsPerChannel(fmt),
                .HasAlphaChannel = hasAlpha, .HasTransparency = hasAlpha AndAlso img.HasTransparency(), .ExifOrientation = orientation}}
    End Function

    Public Sub Encode(image As AutoComposite.RgbaImage, path As String, format As AutoComposite.ExportFormat, jpegQuality As Integer) Implements AutoComposite.IImageCodec.Encode
        If format = AutoComposite.ExportFormat.Png Then
            Call New AutoComposite.PngCodec().Encode(image, path, format, jpegQuality)
            Return
        End If
        ' JPEG 沒有 Alpha：透明區域以白色墊底。
        Dim w = image.Width, h = image.Height
        Dim bgr(w * h * 3 - 1) As Byte
        Dim p = image.Pixels
        For i = 0 To w * h - 1
            Dim a = p(i * 4 + 3) / 255.0
            bgr(i * 3) = AutoComposite.RgbaImage.ToByte(CSng((p(i * 4 + 2) * a + 255 * (1 - a)) / 255))
            bgr(i * 3 + 1) = AutoComposite.RgbaImage.ToByte(CSng((p(i * 4 + 1) * a + 255 * (1 - a)) / 255))
            bgr(i * 3 + 2) = AutoComposite.RgbaImage.ToByte(CSng((p(i * 4) * a + 255 * (1 - a)) / 255))
        Next
        Dim src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, Nothing, bgr, w * 3)
        Dim enc As New JpegBitmapEncoder With {.QualityLevel = Math.Clamp(jpegQuality, 1, 100)}
        enc.Frames.Add(BitmapFrame.Create(src))
        Dim dir = IO.Path.GetDirectoryName(IO.Path.GetFullPath(path))
        If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
        Dim tmp = path & ".tmp"
        Using fs = File.Create(tmp)
            enc.Save(fs)
        End Using
        File.Move(tmp, path, True)
    End Sub

    Private Shared Function ReadOrientation(frame As BitmapFrame) As Integer
        Dim meta = TryCast(frame.Metadata, BitmapMetadata)
        If meta Is Nothing Then Return 1
        For Each q In {"System.Photo.Orientation", "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}"}
            Try
                Dim v = meta.GetQuery(q)
                If v IsNot Nothing Then
                    Dim o = Convert.ToInt32(v)
                    If o >= 1 AndAlso o <= 8 Then Return o
                End If
            Catch ex As Exception When TypeOf ex Is NotSupportedException OrElse TypeOf ex Is ArgumentException OrElse
                                       TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is Runtime.InteropServices.COMException OrElse TypeOf ex Is FormatException
            End Try
        Next
        Return 1
    End Function

    ''' <summary>格式帶 Alpha 通道，或索引色的調色盤裡有透明色。</summary>
    Private Shared Function FormatHasAlpha(f As PixelFormat, palette As BitmapPalette) As Boolean
        If f = PixelFormats.Bgra32 OrElse f = PixelFormats.Pbgra32 OrElse f = PixelFormats.Rgba64 OrElse f = PixelFormats.Prgba64 OrElse
           f = PixelFormats.Rgba128Float OrElse f = PixelFormats.Prgba128Float Then Return True
        If IsIndexed(f) AndAlso palette IsNot Nothing Then Return palette.Colors.Any(Function(c) c.A < 255)
        Return False
    End Function

    Private Shared Function IsIndexed(f As PixelFormat) As Boolean
        Return f = PixelFormats.Indexed1 OrElse f = PixelFormats.Indexed2 OrElse f = PixelFormats.Indexed4 OrElse f = PixelFormats.Indexed8
    End Function

    Private Shared Function DescribeFormat(f As PixelFormat, hasAlpha As Boolean) As String
        If IsIndexed(f) Then Return "Indexed"
        If f = PixelFormats.Gray8 OrElse f = PixelFormats.Gray16 OrElse f = PixelFormats.Gray32Float OrElse f = PixelFormats.Gray2 OrElse f = PixelFormats.Gray4 OrElse f = PixelFormats.BlackWhite Then Return "Gray"
        If f = PixelFormats.Cmyk32 Then Return "CMYK"
        If hasAlpha Then Return "RGBA"
        Return "RGB"
    End Function

    Private Shared Function BitsPerChannel(f As PixelFormat) As Integer
        If f = PixelFormats.Rgba64 OrElse f = PixelFormats.Prgba64 OrElse f = PixelFormats.Rgb48 OrElse f = PixelFormats.Gray16 Then Return 16
        If f = PixelFormats.Rgba128Float OrElse f = PixelFormats.Prgba128Float OrElse f = PixelFormats.Rgb128Float OrElse f = PixelFormats.Gray32Float Then Return 32
        Return 8
    End Function
End Class
