Imports PhotoEdit

''' <summary>
''' 影像選單的功能在沒有開啟影像時也能用：先選一張圖（frmPickSource：開啟、貼上、拖進來），開啟後功能照平常進行。
''' 人物合成例外：它自己可以分別選人物與背景，沒有背景時存成透明 PNG（見 frmCompositeGuide）。
''' </summary>
Partial Friend Class frmEditor

    ''' <summary>影像選單裡要先有影像的指令（人物合成、拼貼自己會選圖，不在這裡）。</summary>
    Private Shared ReadOnly ImageToolCommands As New HashSet(Of String) From {
        "rotl", "rotr", "fliph", "flipv", "crop", "smartcrop", "autostraighten", "auto", "autowb", "colorize",
        "pixelscale", "trace", "airedraw", "scream", "morph", "facechange", "monstercutout"}

    ''' <summary>
    ''' 沒有開啟影像時先選一張圖開啟；回傳 True＝現在有影像可以繼續。有影像時直接回傳 True。
    ''' </summary>
    Private Function EnsurePhotoForTool(command As String) As Boolean
        If _photo IsNot Nothing Then Return True
        Using dlg As New frmPickSource(MenuCaption(command))
            If PickSourceOverride IsNot Nothing Then
                PickSourceOverride.Invoke(dlg) ' 測試：不顯示對話框，直接指定
            ElseIf dlg.ShowDialog(Me) <> DialogResult.OK Then
                Return False
            End If
            If dlg.FileName IsNot Nothing Then
                OpenPhoto(dlg.FileName)
            ElseIf dlg.ClipboardData IsNot Nothing Then
                PasteAsNewImage(dlg.ClipboardData)
            End If
        End Using
        Return _photo IsNot Nothing
    End Function

    ''' <summary>測試用：設定後選圖視窗不顯示，改由這個動作決定選了什麼（例如呼叫 UseFile）。</summary>
    Friend Shared Property PickSourceOverride As Action(Of frmPickSource)

    ''' <summary>選單上這個指令的文字（去掉「…」與快捷鍵），當作選圖視窗的標題。</summary>
    Private Function MenuCaption(command As String) As String
        Dim found = FindMenuItem(_menuRoot, command)
        Dim text = If(found?.Text, command)
        Dim k = text.IndexOf(" (", StringComparison.Ordinal)
        If k > 0 Then text = text.Substring(0, k)
        Return text.TrimEnd("…"c, "."c)
    End Function

    Private Shared Function FindMenuItem(parent As Aqua.MenuItem, name As String) As Aqua.MenuItem
        If parent Is Nothing Then Return Nothing
        For i = 0 To parent.Count - 1
            Dim m = parent(i)
            If m.Name = name Then Return m
            Dim inner = FindMenuItem(m, name)
            If inner IsNot Nothing Then Return inner
        Next
        Return Nothing
    End Function
End Class
