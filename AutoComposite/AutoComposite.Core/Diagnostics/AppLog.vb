Imports System.IO

Public Enum LogLevel
    Info
    Warn
    [Error]
End Enum

Public NotInheritable Class LogEntry
    Public Property Time As DateTime
    Public Property Level As LogLevel
    Public Property Message As String = ""
    Public Property Detail As String = ""

    Public Overrides Function ToString() As String
        Return $"{Time:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Message}" & If(String.IsNullOrEmpty(Detail), "", Environment.NewLine & Detail)
    End Function
End Class

''' <summary>
''' 程式日誌：保留最近的紀錄給介面顯示，並寫到日誌檔（預設 %LOCALAPPDATA%\AutoComposite\logs）。
''' 寫檔失敗不影響程式運作。
''' </summary>
Public NotInheritable Class AppLog
    Private Sub New()
    End Sub

    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly _recent As New LinkedList(Of LogEntry)()
    Private Const MaxRecent As Integer = 500

    ''' <summary>日誌檔路徑；Nothing 表示不寫檔（測試時）。</summary>
    Public Shared Property FilePath As String

    Public Shared Event EntryAdded(entry As LogEntry)

    Public Shared Sub Info(message As String)
        Add(LogLevel.Info, message, Nothing)
    End Sub

    Public Shared Sub Warn(message As String)
        Add(LogLevel.Warn, message, Nothing)
    End Sub

    Public Shared Sub [Error](message As String, Optional ex As Exception = Nothing)
        Add(LogLevel.Error, If(ex Is Nothing, message, $"{message}：{ex.Message}"), ex?.ToString())
    End Sub

    Public Shared Function Recent() As List(Of LogEntry)
        SyncLock _lock
            Return _recent.ToList()
        End SyncLock
    End Function

    Public Shared Function DefaultLogPath() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoComposite", "logs", $"log-{DateTime.Now:yyyyMMdd}.txt")
    End Function

    Private Shared Sub Add(level As LogLevel, message As String, detail As String)
        Dim e As New LogEntry With {.Time = DateTime.Now, .Level = level, .Message = message, .Detail = If(detail, "")}
        SyncLock _lock
            _recent.AddLast(e)
            While _recent.Count > MaxRecent
                _recent.RemoveFirst()
            End While
            If Not String.IsNullOrEmpty(FilePath) Then
                Try
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
                    File.AppendAllText(FilePath, e.ToString() & Environment.NewLine)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                End Try
            End If
        End SyncLock
        RaiseEvent EntryAdded(e)
    End Sub
End Class

''' <summary>效能量測：記錄各階段耗時，產生可讀報告（不預先承諾固定 FPS 或推論時間）。</summary>
Public NotInheritable Class PerformanceReport
    Private ReadOnly _items As New List(Of (Name As String, Milliseconds As Double, Detail As String))()
    Private ReadOnly _lock As New Object()

    Public Sub Add(name As String, milliseconds As Double, Optional detail As String = "")
        SyncLock _lock
            _items.Add((name, milliseconds, detail))
        End SyncLock
    End Sub

    Public Function Measure(Of T)(name As String, f As Func(Of T), Optional detail As String = "") As T
        Dim sw = Stopwatch.StartNew()
        Dim r = f()
        Add(name, sw.Elapsed.TotalMilliseconds, detail)
        Return r
    End Function

    Public Function ToText() As String
        Dim sb As New Text.StringBuilder()
        sb.AppendLine($"效能報告 {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
        sb.AppendLine($"處理器：{Environment.ProcessorCount} 執行緒，{If(Environment.Is64BitProcess, "64", "32")} 位元程序，.NET {Environment.Version}")
        sb.AppendLine($"目前受控記憶體：{GC.GetTotalMemory(False) / 1048576.0:0.0} MB，工作集：{Environment.WorkingSet / 1048576.0:0.0} MB")
        SyncLock _lock
            For Each it In _items
                sb.AppendLine($"  {it.Name,-28} {it.Milliseconds,10:0.0} ms  {it.Detail}")
            Next
        End SyncLock
        Return sb.ToString()
    End Function
End Class
