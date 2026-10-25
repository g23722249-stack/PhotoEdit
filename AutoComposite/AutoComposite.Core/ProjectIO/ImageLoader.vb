''' <summary>
''' 依副檔名挑選編解碼器載入影像：先試註冊的編解碼器（主程式的 WIC 支援 JPEG／WebP／EXIF），
''' 最後用內建 PNG。載入時檢查尺寸上限。
''' </summary>
Public NotInheritable Class ImageLoader
    Private ReadOnly _codecs As New List(Of IImageCodec)()
    Private ReadOnly _png As New PngCodec()

    Public Sub New(ParamArray codecs As IImageCodec())
        If codecs IsNot Nothing Then _codecs.AddRange(codecs.Where(Function(c) c IsNot Nothing))
        _codecs.Add(_png)
    End Sub

    ''' <summary>主要編解碼器（第一個註冊者），匯出 JPEG 時使用。</summary>
    Public ReadOnly Property Primary As IImageCodec
        Get
            Return _codecs(0)
        End Get
    End Property

    Public Function Load(path As String) As DecodedImage
        If String.IsNullOrEmpty(path) Then Throw New ArgumentException("沒有指定圖片路徑。")
        If Not IO.File.Exists(path) Then Throw New IO.FileNotFoundException("找不到圖片。", path)
        Dim errors As New List(Of String)()
        For Each c In _codecs
            If Not c.CanDecode(path) Then Continue For
            Try
                Dim r = c.Decode(path)
                r.Info.SourcePath = path
                Return r
            Catch ex As Exception When TypeOf ex Is NotSupportedException OrElse TypeOf ex Is IO.InvalidDataException OrElse TypeOf ex Is IO.EndOfStreamException
                errors.Add($"{c.GetType().Name}：{ex.Message}")
            End Try
        Next
        Throw New NotSupportedException($"無法讀取 {IO.Path.GetFileName(path)}。" & If(errors.Count > 0, String.Join("；", errors), "沒有支援此格式的解碼器。"))
    End Function
End Class
