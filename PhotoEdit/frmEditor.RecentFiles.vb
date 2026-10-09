Imports System.IO

''' <summary>
''' 檔案 → 最近開啟的檔案：最近載入或存過的照片與專案（最多 10 個，新的在上），記在 settings.json。
''' 貼上的剪貼簿圖片與「新增」產生的暫存影像不列入。檔案不見了點下去會說明並從清單移除。
''' </summary>
Partial Friend Class frmEditor

    Private Const MaxRecentFiles As Integer = 10
    Private _recentMenu As Aqua.MenuItem

    Private Function RecentFiles() As List(Of String)
        If _appSettings.RecentFiles Is Nothing Then _appSettings.RecentFiles = New List(Of String)()
        Return _appSettings.RecentFiles
    End Function

    ''' <summary>放到清單最上面（重複的移上來），超過 10 個刪掉最舊的。</summary>
    Private Sub AddRecentFile(filePath As String)
        If String.IsNullOrEmpty(filePath) Then Return
        Dim full As String
        Try
            full = Path.GetFullPath(filePath)
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
            Return
        End Try
        Dim list = RecentFiles()
        list.RemoveAll(Function(p) String.Equals(p, full, StringComparison.OrdinalIgnoreCase))
        list.Insert(0, full)
        If list.Count > MaxRecentFiles Then list.RemoveRange(MaxRecentFiles, list.Count - MaxRecentFiles)
        _appSettings.Save()
        RebuildRecentMenu()
    End Sub

    ''' <summary>子選單：「1. 檔名　資料夾」…，最後是「清除清單」；沒有紀錄時一個停用的「（沒有）」。</summary>
    Private Sub RebuildRecentMenu()
        If _recentMenu Is Nothing Then Return
        _recentMenu.Clear()
        Dim list = RecentFiles()
        If list.Count = 0 Then
            _recentMenu.AddItem(New Aqua.MenuItem("（沒有）") With {.Enabled = False})
            Return
        End If
        For i = 0 To list.Count - 1
            Dim folder = Path.GetDirectoryName(list(i))
            If folder IsNot Nothing AndAlso folder.Length > 40 Then folder = "…" & folder.Substring(folder.Length - 39)
            Dim number = If(i < 9, (i + 1).ToString(), "0")
            _recentMenu.AddItem(Item("recent:" & i, $"{number}. {Path.GetFileName(list(i))}　　{folder}"))
        Next
        _recentMenu.AddItem(New Aqua.MenuItem("-"))
        _recentMenu.AddItem(Item("recentclear", "清除清單"))
    End Sub

    ''' <summary>處理「recent:N」與「recentclear」；不是這兩個指令時回傳 False。</summary>
    Private Function HandleRecentCommand(name As String) As Boolean
        If name = "recentclear" Then
            RecentFiles().Clear()
            _appSettings.Save()
            RebuildRecentMenu()
            SetStatusMessage("已清除最近開啟的檔案清單。")
            Return True
        End If
        If Not name.StartsWith("recent:", StringComparison.Ordinal) Then Return False
        Dim index As Integer
        If Not Integer.TryParse(name.Substring(7), index) Then Return True
        Dim list = RecentFiles()
        If index < 0 OrElse index >= list.Count Then Return True
        Dim file = list(index)
        If Not IO.File.Exists(file) Then
            MessageBox.Show(Me, $"找不到「{Path.GetFileName(file)}」，可能已經移動或刪除：" & vbCrLf & file & vbCrLf & vbCrLf & "已從最近開啟的檔案移除。",
                            AppName, MessageBoxButtons.OK, MessageBoxIcon.Information)
            list.RemoveAt(index)
            _appSettings.Save()
            RebuildRecentMenu()
            Return True
        End If
        OpenPhoto(file)
        Return True
    End Function
End Class
