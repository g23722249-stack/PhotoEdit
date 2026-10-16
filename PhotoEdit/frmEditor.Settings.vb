''' <summary>「設定」視窗（編輯 → 設定…）。</summary>
Partial Friend Class frmEditor

    Private Sub OpenSettings()
        Using f As New frmSettings(_appSettings, _mdi)
            f.ShowDialog(Me)
        End Using
        AfterSettings()
    End Sub

    ''' <summary>設定視窗關掉之後（沒開圖片時也會走到）。</summary>
    Private Sub AfterSettings()
        ' 配色換了：重畫自己畫的部分（筆刷預覽、直方圖、圖層清單…）
        _brushPreviewKey = Nothing
        SyncSliders() ' 穩定器在設定與繪圖分頁兩邊都能調
        UpdateDrawControls()
        Invalidate(True)
    End Sub
End Class
