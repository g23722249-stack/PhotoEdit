''' <summary>「設定」視窗（編輯 → 設定…）。</summary>
Partial Friend Class frmEditor

    Private Sub OpenSettings()
        Using f As New frmSettings(_appSettings, _mdi)
            f.ShowDialog(Me)
        End Using
        ' 配色換了：重畫自己畫的部分（筆刷預覽、直方圖、圖層清單…）
        _brushPreviewKey = Nothing
        UpdateDrawControls()
        Invalidate(True)
    End Sub
End Class
