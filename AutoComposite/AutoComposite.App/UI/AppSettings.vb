Imports System.IO
Imports System.Text.Json
Imports AutoComposite

''' <summary>使用者設定（%LOCALAPPDATA%\AutoComposite\settings.json），讀寫失敗時用預設值。</summary>
Friend NotInheritable Class AppSettings
    Public Property LastImageFolder As String = ""
    Public Property LastProjectFolder As String = ""
    Public Property Provider As ExecutionPreference = ExecutionPreference.Auto
    Public Property IntendedUse As IntendedUse = IntendedUse.NonCommercial
    Public Property DefaultSegmentationModel As String = "birefnet-toonout"
    ''' <summary>預覽長邊上限（0 = 依畫布大小）。</summary>
    Public Property PreviewMaxSide As Integer = 0
    Public Property JpegQuality As Integer = 95

    Private Shared ReadOnly FilePath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoComposite", "settings.json")

    Public Shared Function Load() As AppSettings
        Try
            If File.Exists(FilePath) Then Return If(JsonSerializer.Deserialize(Of AppSettings)(File.ReadAllText(FilePath)), New AppSettings())
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is UnauthorizedAccessException
            AppLog.Warn($"設定檔讀取失敗，使用預設值：{ex.Message}")
        End Try
        Return New AppSettings()
    End Function

    Public Sub Save()
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True}))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppLog.Warn($"設定檔寫入失敗：{ex.Message}")
        End Try
    End Sub
End Class
