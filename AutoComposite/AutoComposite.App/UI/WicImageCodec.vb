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

    ''' <summary>
    ''' 載入像素上限：超過時在解碼階段就等比縮小（JPEG 由 WIC 以 DCT 縮放，不需要先解出整張原圖）。
    ''' 合成要同時保留背景、輸出與多張遮罩的全尺寸緩衝區，所以不能無上限地載入博物館等級的超大掃描檔。
    ''' </summary>
    Public Property MaxPixels As Long = AutoComposite.ImageLimits.DefaultMaxLoadPixels

    Public Function Decode(path As String) As AutoComposite.DecodedImage Implements AutoComposite.IImageCodec.Decode
        Try
            Return DecodeCore(path)
        Catch ex As Exception When TypeOf ex Is OverflowException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is InsufficientMemoryException
            Throw New AutoComposite.ImageTooLargeException($"圖片太大，記憶體不足以解碼（{ex.Message}）。請先用其他軟體縮小，或關閉其他程式後再試。")
        End Try
    End Function

    Private Function DecodeCore(path As String) As AutoComposite.DecodedImage
        Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            ' 先以延遲模式開啟，只讀尺寸與中繼資料（OnLoad 會立刻解碼整張圖，超大圖會在 WPF 內部溢位）。
            Dim decoder As BitmapDecoder
            Try
                decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat Or BitmapCreateOptions.IgnoreColorProfile Or BitmapCreateOptions.DelayCreation, BitmapCacheOption.None)
            Catch ex As Exception When TypeOf ex Is NotSupportedException OrElse TypeOf ex Is FileFormatException OrElse TypeOf ex Is Runtime.InteropServices.COMException
                Dim ext = IO.Path.GetExtension(path).ToLowerInvariant()
                Dim hint = If(ext = ".webp" OrElse ext = ".heic" OrElse ext = ".heif" OrElse ext = ".avif",
                              "，請從 Microsoft Store 安裝對應的影像擴充功能（例如「WebP 影像擴充功能」、「HEIF 影像擴充功能」）", "")
                Throw New NotSupportedException($"Windows 無法解碼此圖片（{ex.Message}）{hint}。", ex)
            End Try
            Dim frame = decoder.Frames(0)
            Dim origW = frame.PixelWidth, origH = frame.PixelHeight
            Dim fmt = frame.Format
            Dim orientation = ReadOrientation(frame)
            Dim hasAlpha = FormatHasAlpha(fmt, frame.Palette)
            Dim formatName = If(decoder.CodecInfo?.FriendlyName, IO.Path.GetExtension(path).TrimStart("."c).ToUpperInvariant()).Replace(" Decoder", "").Replace(" decoder", "")
            Dim target = AutoComposite.ImageLimits.FitPixels(origW, origH, MaxPixels)

            Dim source As BitmapSource
            If target.Width = origW AndAlso target.Height = origH Then
                source = frame
            Else
                ' 解碼時縮小：BitmapImage 的 DecodePixelWidth 會用 WIC 的 IWICBitmapSourceTransform（JPEG 為 DCT 縮放），
                ' 不支援的格式則由 WIC 縮放器逐列處理。尺寸以檔案儲存方向計算，EXIF 轉正在後面做。
                fs.Position = 0
                Dim bi As New BitmapImage()
                bi.BeginInit()
                bi.StreamSource = fs
                bi.CacheOption = BitmapCacheOption.OnLoad
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile
                bi.DecodePixelWidth = target.Width
                bi.DecodePixelHeight = target.Height
                bi.EndInit()
                source = bi
                AutoComposite.AppLog.Info($"{IO.Path.GetFileName(path)} 原圖 {origW}×{origH} 超過 {MaxPixels / 1000000.0:0} MP 上限，縮小載入為 {bi.PixelWidth}×{bi.PixelHeight}。")
            End If

            Dim w = source.PixelWidth, h = source.PixelHeight
            AutoComposite.ImageLimits.CheckSize(w, h, 4)
            Dim conv As New FormatConvertedBitmap(source, PixelFormats.Bgra32, Nothing, 0)
            Dim stride = w * 4
            Dim px(CInt(CLng(stride) * h) - 1) As Byte
            conv.CopyPixels(px, stride, 0)
            ' BGRA → RGBA
            For i = 0 To px.Length - 1 Step 4
                Dim b = px(i)
                px(i) = px(i + 2)
                px(i + 2) = b
            Next
            Dim img As New AutoComposite.RgbaImage(w, h, px)
            img = AutoComposite.ExifOrientation.Apply(img, orientation)
            Dim swap = orientation >= 5
            Return New AutoComposite.DecodedImage With {
                .Image = img,
                .Info = New AutoComposite.ImageInfo With {
                    .SourcePath = path, .Format = formatName,
                    .Width = img.Width, .Height = img.Height, .OriginalWidth = If(swap, origH, origW), .OriginalHeight = If(swap, origW, origH),
                    .ColorMode = DescribeFormat(fmt, hasAlpha), .BitsPerChannel = BitsPerChannel(fmt),
                    .HasAlphaChannel = hasAlpha, .HasTransparency = hasAlpha AndAlso img.HasTransparency(), .ExifOrientation = orientation}}
        End Using
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
