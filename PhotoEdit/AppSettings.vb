Imports System.IO
Imports System.Text.Json

''' <summary>使用者介面設定（例如右側面板寬度），存在 %AppData%\PhotoEdit\settings.json。</summary>
Friend Class AppSettings
    Public Property SidePanelWidth As Integer = 400
    ''' <summary>裁切輔助線（0 三分線、1 黃金比例、2 格線、3 對角線、4 無）。</summary>
    Public Property CropGuide As Integer
    ''' <summary>滑鼠停在控制項上時顯示使用說明視窗。</summary>
    Public Property ShowHelp As Boolean = True
    ''' <summary>深色配色（「設定」視窗切換）；預設淺色。</summary>
    Public Property DarkTheme As Boolean
    ''' <summary>多文件（MDI）：同時開多張畫布；False 為單一文件（SDI）。改了要重開程式才生效。</summary>
    Public Property Mdi As Boolean
    ''' <summary>多文件時用子視窗（可並排、重疊）顯示；False 為分頁。</summary>
    Public Property MdiWindows As Boolean

    Private Shared ReadOnly FilePath As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoEdit", "settings.json")

    ''' <summary>讀不到或內容損壞時用預設值。</summary>
    Public Shared Function Load() As AppSettings
        Try
            If File.Exists(FilePath) Then Return If(JsonSerializer.Deserialize(Of AppSettings)(File.ReadAllText(FilePath)), New AppSettings())
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
        Return New AppSettings()
    End Function

    ''' <summary>存不了就算了（不影響編輯）。</summary>
    Public Sub Save()
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Me))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub
End Class
