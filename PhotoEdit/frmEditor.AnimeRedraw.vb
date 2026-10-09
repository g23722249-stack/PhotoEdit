Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>
''' 宮崎風 AI 重繪（影像 → 宮崎風 AI 重繪…，或效果分頁藝術風格區的按鈕）：
''' 用目前看到的樣子（含調整）重畫，結果存到 圖片\PhotoEdit\AI重繪 並開成新影像，原照片不變。
''' </summary>
Partial Friend Class frmEditor

    Private Shared _redrawFolder As String

    ''' <summary>結果存放的資料夾（測試時可以改到暫存區）。</summary>
    Friend Shared Property RedrawFolder As String
        Get
            Return If(_redrawFolder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", "AI重繪"))
        End Get
        Set(value As String)
            _redrawFolder = value
        End Set
    End Property

    Private Sub ShowAnimeRedraw()
        If _photo Is Nothing Then Return
        Dim source As Bitmap
        Try
            source = RenderMerged(_recipe)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            MessageBox.Show(Me, "無法取得目前的影像：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Dim baseName = Path.GetFileNameWithoutExtension(_photo.Path)
        Using source
            Using dlg As New frmAnimeRedraw(source, _help)
                If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.Result Is Nothing Then Return
                Dim file As String
                Using result = dlg.Result
                    file = SaveRedraw(result, $"{baseName}_{dlg.ResultName}")
                End Using
                OpenPhoto(file, remember:=False)
                SetStatusMessage("宮崎風 AI 重繪：已存成 " & file)
            End Using
        End Using
    End Sub

    ''' <summary>存成 PNG，同名時加序號。</summary>
    Private Shared Function SaveRedraw(bmp As Bitmap, name As String) As String
        Directory.CreateDirectory(RedrawFolder)
        For Each c In Path.GetInvalidFileNameChars()
            name = name.Replace(c, "_"c)
        Next
        Dim file = Path.Combine(RedrawFolder, name & ".png")
        Dim n = 2
        While IO.File.Exists(file)
            file = Path.Combine(RedrawFolder, $"{name}_{n}.png")
            n += 1
        End While
        bmp.Save(file, ImageFormat.Png)
        Return file
    End Function
End Class
