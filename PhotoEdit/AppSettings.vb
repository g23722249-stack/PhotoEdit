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
    ''' <summary>繪圖筆的筆壓曲線（「設定」視窗）：軟硬 -100..100、最小筆壓 0..50%、滿壓力道 40..100%。</summary>
    Public Property PenSoftness As Integer
    Public Property PenMinPressure As Integer
    Public Property PenFullPressure As Integer = 100
    ''' <summary>筆畫穩定器 0..100（0 = 關）：自由繪製與直接繪製的拉線長度。</summary>
    Public Property Stabilizer As Integer
    ''' <summary>參考圖：最近開過的圖片路徑、描圖底稿的不透明度（%）。</summary>
    Public Property ReferencePath As String
    Public Property TraceOpacity As Integer = 35
    ''' <summary>浮動選色視窗的位置（螢幕座標）；沒有記錄時放在側邊面板左邊。</summary>
    Public Property ColorWindowX As Integer?
    Public Property ColorWindowY As Integer?
    ''' <summary>油漆桶的選項（容許度、相鄰、取樣、補缺口、擴張、柔邊、填入）；種子點不用。</summary>
    Public Property Bucket As PhotoEdit.BucketFill
    ''' <summary>漸層工具的選項（類型、顏色、反轉、防色階、四色的四個角）；起點終點不用。</summary>
    Public Property Gradient As PhotoEdit.GradientFill
    ''' <summary>繪圖分頁的快速工具熱鍵（Keys 名稱）：快速面板、輪盤（預設 F9、F10，功能鍵不會被中文輸入法攔下）。</summary>
    Public Property QuickPanelHotkey As String = "F9"
    Public Property QuickRadialHotkey As String = "F10"
    ''' <summary>橡皮擦的擦法：0 擦掉、1 漂白、2 加深。</summary>
    Public Property EraseMode As Integer
    ''' <summary>快速面板的常用筆刷（BrushKind，4 個；右鍵可換）。</summary>
    Public Property QuickBrushes As List(Of Integer)
    ''' <summary>右側面板上方的直方圖展開（預設收起，只剩標題列）。</summary>
    Public Property HistogramExpanded As Boolean
    ''' <summary>最近開啟的檔案（照片與專案，新的在前，最多 10 個）。</summary>
    Public Property RecentFiles As List(Of String)

    Public Function PenCurve() As PhotoEdit.PenCurve
        Return New PhotoEdit.PenCurve With {.Softness = PenSoftness, .MinPressure = PenMinPressure, .FullPressure = PenFullPressure}
    End Function

    ''' <summary>設定檔位置；測試程式可以改到暫存資料夾（要在建立編輯器之前），才不會動到使用者的設定。</summary>
    Friend Shared Property FilePath As String =
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
