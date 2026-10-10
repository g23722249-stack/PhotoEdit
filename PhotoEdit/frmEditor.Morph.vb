Imports System.IO
Imports PhotoEdit

''' <summary>變形動畫（影像 → 變形動畫…）：用目前看到的樣子（含調整）當第一張，另外選第二張，輸出 MP4、GIF。</summary>
Partial Friend Class frmEditor

    Private Sub ShowMorph()
        If _photo Is Nothing Then Return
        Dim source As Bitmap
        Try
            source = RenderMerged(_recipe)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            MessageBox.Show(Me, "無法取得目前的影像：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Using source
            Using dlg As New frmMorph(source, Path.GetFileNameWithoutExtension(_photo.Path), _help)
                dlg.ShowDialog(Me)
            End Using
        End Using
    End Sub

    ''' <summary>川劇變臉（影像 → 川劇變臉…，或手動調整戲曲頁的「變臉影片…」）：用目前看到的樣子當本人。</summary>
    Private Sub ShowFaceChange(Optional owner As Form = Nothing)
        If _photo Is Nothing Then Return
        Dim source As Bitmap
        Try
            source = RenderMerged(_recipe)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            MessageBox.Show(If(owner, Me), "無法取得目前的影像：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Using source
            Using dlg As New frmFaceChange(source, Path.GetFileNameWithoutExtension(_photo.Path), _help)
                dlg.ShowDialog(If(owner, Me))
            End Using
        End Using
    End Sub
End Class