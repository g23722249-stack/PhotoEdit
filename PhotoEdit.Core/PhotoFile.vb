Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO

''' <summary>
''' 讀取照片（依 EXIF 方向轉正、不鎖住檔案）與匯出 JPG（保留拍攝日期等 EXIF）。
''' </summary>
Public Class PhotoFile
    Implements IDisposable

    Private Const TagOrientation As Integer = &H112
    ' 匯出時不複製的標籤：方向（已轉正）、尺寸、內嵌縮圖。
    Private Shared ReadOnly SkipTags As New HashSet(Of Integer) From {
        TagOrientation, &HA002, &HA003, &H100, &H101,
        &H201, &H202, &H501B, &H5090, &H5091, &H502D, &H5013}

    Public ReadOnly Property Path As String
    ''' <summary>已轉正的 32bppArgb 原圖。</summary>
    Public ReadOnly Property Image As Bitmap
    Private ReadOnly _properties As PropertyItem()

    ''' <summary>拍攝日期、相機、GPS（從 EXIF 讀；沒有的欄位為 Nothing）。</summary>
    Public Function Info() As PhotoInfo
        Dim raw = Function(id As Integer) _properties.FirstOrDefault(Function(p) p.Id = id)?.Value
        Dim asText = Function(id As Integer) As String
                       Dim v = raw(id)
                       Return If(v Is Nothing, Nothing, System.Text.Encoding.ASCII.GetString(v).TrimEnd(ChrW(0), " "c).Trim())
                   End Function
        Dim result As New PhotoInfo With {.TakenDate = If(PhotoInfo.ParseExifDate(raw(&H9003)), PhotoInfo.ParseExifDate(raw(&H132)))}
        Dim make = asText(&H10F), model = asText(&H110)
        If Not String.IsNullOrEmpty(model) Then
            result.Camera = If(Not String.IsNullOrEmpty(make) AndAlso Not model.StartsWith(make, StringComparison.OrdinalIgnoreCase), make & " " & model, model)
        End If
        Dim lat = PhotoInfo.ParseGpsCoordinate(raw(&H2)), lon = PhotoInfo.ParseGpsCoordinate(raw(&H4))
        If lat.HasValue AndAlso lon.HasValue Then
            result.Gps = $"{lat.Value:0.0000}°{If(asText(&H1) = "S", "S", "N")} {lon.Value:0.0000}°{If(asText(&H3) = "W", "W", "E")}"
        End If
        Return result
    End Function

    Private Sub New(path As String, image As Bitmap, properties As PropertyItem())
        Me.Path = path
        Me.Image = image
        _properties = properties
    End Sub

    Public Shared ReadOnly SupportedExtensions As String() = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif"}

    Public Shared Function IsSupported(path As String) As Boolean
        Return SupportedExtensions.Contains(IO.Path.GetExtension(path).ToLowerInvariant())
    End Function

    Public Shared Function Open(path As String) As PhotoFile
        Using ms As New MemoryStream(File.ReadAllBytes(path))
            Using original = System.Drawing.Image.FromStream(ms, useEmbeddedColorManagement:=True, validateImageData:=True)
                Dim props = original.PropertyItems
                Dim upright As New Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(upright)
                    g.DrawImage(original, 0, 0, original.Width, original.Height)
                End Using
                Dim rft = OrientationToRotateFlip(ReadOrientation(props))
                If rft <> RotateFlipType.RotateNoneFlipNone Then upright.RotateFlip(rft)
                Return New PhotoFile(path, upright, props)
            End Using
        End Using
    End Function

    Private Shared Function ReadOrientation(props As PropertyItem()) As Integer
        For Each p In props
            If p.Id = TagOrientation AndAlso p.Value IsNot Nothing AndAlso p.Value.Length >= 2 Then
                Return BitConverter.ToUInt16(p.Value, 0)
            End If
        Next
        Return 1
    End Function

    ''' <summary>EXIF 方向值 1..8 轉成讓影像轉正所需的 RotateFlipType。</summary>
    Public Shared Function OrientationToRotateFlip(orientation As Integer) As RotateFlipType
        Select Case orientation
            Case 2 : Return RotateFlipType.RotateNoneFlipX
            Case 3 : Return RotateFlipType.Rotate180FlipNone
            Case 4 : Return RotateFlipType.Rotate180FlipX
            Case 5 : Return RotateFlipType.Rotate90FlipX
            Case 6 : Return RotateFlipType.Rotate90FlipNone
            Case 7 : Return RotateFlipType.Rotate270FlipX
            Case 8 : Return RotateFlipType.Rotate270FlipNone
            Case Else : Return RotateFlipType.RotateNoneFlipNone
        End Select
    End Function

    ''' <summary>以原圖全尺寸算圖並另存 JPG。不允許覆寫原始照片。prepare 見 ImagePipeline.Render（人像修飾）。</summary>
    Public Sub Export(recipe As EditRecipe, targetPath As String, Optional quality As Long = 92,
                      Optional prepare As Func(Of Bitmap, Bitmap) = Nothing,
                      Optional faces As IReadOnlyList(Of FaceRegion) = Nothing)
        If String.Equals(IO.Path.GetFullPath(targetPath), IO.Path.GetFullPath(Path), StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException("不能覆寫原始照片，請換一個檔名。")
        End If
        Using rendered = ImagePipeline.Render(Image, recipe, prepare:=prepare, faces:=faces)
            If String.Equals(IO.Path.GetExtension(targetPath), ".png", StringComparison.OrdinalIgnoreCase) Then
                ' PNG：保留透明（去背）。
                rendered.Save(targetPath, ImageFormat.Png)
                Return
            End If
            Using output As New Bitmap(rendered.Width, rendered.Height, PixelFormat.Format24bppRgb)
                Using g = Graphics.FromImage(output)
                    g.Clear(Color.White) ' JPG 不能透明：透明處變白色
                    g.DrawImage(rendered, 0, 0, rendered.Width, rendered.Height)
                End Using
                For Each p In _properties
                    If SkipTags.Contains(p.Id) Then Continue For
                    Try
                        output.SetPropertyItem(p)
                    Catch ex As ArgumentException
                        ' GDI+ 不接受的標籤略過即可。
                    End Try
                Next
                Dim codec = ImageCodecInfo.GetImageEncoders().First(Function(c) c.FormatID = ImageFormat.Jpeg.Guid)
                Using ep As New EncoderParameters(1)
                    ep.Param(0) = New EncoderParameter(Encoder.Quality, quality)
                    output.Save(targetPath, codec, ep)
                End Using
            End Using
        End Using
    End Sub

    ''' <summary>預設匯出檔名：同資料夾下「原檔名_edited.jpg」（或 .png），已存在就加序號。</summary>
    Public Shared Function SuggestExportPath(photoPath As String, Optional extension As String = ".jpg") As String
        Dim dir = IO.Path.GetDirectoryName(photoPath)
        Dim name = IO.Path.GetFileNameWithoutExtension(photoPath)
        Dim candidate = IO.Path.Combine(dir, name & "_edited" & extension)
        Dim n = 2
        While File.Exists(candidate)
            candidate = IO.Path.Combine(dir, $"{name}_edited{n}{extension}")
            n += 1
        End While
        Return candidate
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Image.Dispose()
    End Sub
End Class
