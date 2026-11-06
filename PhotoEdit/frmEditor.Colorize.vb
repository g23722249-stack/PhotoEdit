Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>
''' 影像 → 黑白照片上色…：用目前看到的樣子（含調整）上色，結果存成新影像（圖片\PhotoEdit\上色）並打開，原照片不變。
''' </summary>
Partial Friend Class frmEditor

    Private Shared _colorizeFolder As String

    ''' <summary>結果存放的資料夾（測試時可以改到暫存區）。</summary>
    Friend Shared Property ColorizeFolder As String
        Get
            Return If(_colorizeFolder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", Lang.T("上色")))
        End Get
        Set(value As String)
            _colorizeFolder = value
        End Set
    End Property

    Private Sub ShowColorize()
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        Dim source As Bitmap
        Try
            source = RenderMerged(_recipe)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            MessageBox.Show(Me, Lang.T("無法取得目前的影像：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Dim baseName = Path.GetFileNameWithoutExtension(_photo.Path)
        Using source
            Using dlg As New frmColorize(source, _help)
                If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.Result Is Nothing Then Return
                OpenColorizeResult(dlg.Result, baseName)
            End Using
        End Using
    End Sub

    ''' <summary>上色的結果存檔並開成新影像（測試也用這個）；釋放 result。</summary>
    Friend Sub OpenColorizeResult(result As Bitmap, baseName As String)
        Dim file As String
        Try
            Directory.CreateDirectory(ColorizeFolder)
            Dim name = baseName & "_" & Lang.T("上色")
            For Each c In Path.GetInvalidFileNameChars()
                name = name.Replace(c, "_"c)
            Next
            file = Path.Combine(ColorizeFolder, name & ".jpg")
            Dim n = 2
            While IO.File.Exists(file)
                file = Path.Combine(ColorizeFolder, $"{name}_{n}.jpg")
                n += 1
            End While
            Dim codec = ImageCodecInfo.GetImageEncoders().First(Function(e) e.FormatID = ImageFormat.Jpeg.Guid)
            Using ps As New EncoderParameters(1)
                ps.Param(0) = New EncoderParameter(Encoder.Quality, 95L)
                result.Save(file, codec, ps)
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Runtime.InteropServices.ExternalException
            MessageBox.Show(Me, Lang.T("無法儲存上色的結果：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Finally
            result.Dispose()
        End Try
        OpenPhoto(file, remember:=False)
        SetStatusMessage(Lang.T("上色完成，已存成 ") & file)
    End Sub
End Class
