Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>
''' 像素圖放大（影像 → 像素圖放大…）：用目前看到的樣子（含調整）放大，結果存到 圖片\PhotoEdit\像素放大 並開成新影像，原圖不變。
''' 演算法在 PhotoEdit.Core\PixelScaler.vb。
''' </summary>
Partial Friend Class frmEditor

    Private Shared _pixelFolder As String

    ''' <summary>結果存放的資料夾（測試時可以改到暫存區）。</summary>
    Friend Shared Property PixelScaleFolder As String
        Get
            Return If(_pixelFolder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", Lang.T("像素放大")))
        End Get
        Set(value As String)
            _pixelFolder = value
        End Set
    End Property

    Private Sub ShowPixelScale()
        If _photo Is Nothing Then Return
        Dim source As Bitmap
        Try
            source = RenderMerged(_recipe)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            MessageBox.Show(Me, Lang.T("無法取得目前的影像：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Dim baseName = Path.GetFileNameWithoutExtension(_photo.Path)
        Using source
            Using dlg As New frmPixelScale(source, _help)
                If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.Result Is Nothing Then Return
                Dim file As String
                Using result = dlg.Result
                    file = SavePixelScaled(result, $"{baseName}_{dlg.Algorithm}_{dlg.Factor}x")
                End Using
                OpenPhoto(file, remember:=False)
                SetStatusMessage(Lang.T("像素圖放大：已存成 ") & file)
            End Using
        End Using
    End Sub

    ''' <summary>存成 PNG（保留透明），同名時加序號。</summary>
    Friend Shared Function SavePixelScaled(bmp As Bitmap, name As String) As String
        Directory.CreateDirectory(PixelScaleFolder)
        For Each c In Path.GetInvalidFileNameChars()
            name = name.Replace(c, "_"c)
        Next
        Dim file = Path.Combine(PixelScaleFolder, name & ".png")
        Dim n = 2
        While IO.File.Exists(file)
            file = Path.Combine(PixelScaleFolder, $"{name}_{n}.png")
            n += 1
        End While
        bmp.Save(file, ImageFormat.Png)
        Return file
    End Function
End Class
