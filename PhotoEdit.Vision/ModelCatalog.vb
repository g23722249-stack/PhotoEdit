Imports System.IO
Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading

''' <summary>模型清單的一個檔案（Models\ 下的相對路徑、大小、下載位址）。</summary>
Public NotInheritable Class ModelFile
    Public Property Path As String = ""
    Public Property Size As Long
    Public Property Url As String = ""
End Class

''' <summary>一個模型（可能有好幾個檔案）。</summary>
Public NotInheritable Class ModelEntry
    Public Property Id As String = ""
    Public Property Group As String = ""
    Public Property Name As String = ""
    Public Property Use As String = ""
    Public Property License As String = ""
    Public Property Source As String = ""
    Public Property Files As New List(Of ModelFile)()

    Public ReadOnly Property TotalSize As Long
        Get
            Return Files.Sum(Function(f) f.Size)
        End Get
    End Property
End Class

Public Enum ModelState
    ''' <summary>所有檔案都在、大小正確。</summary>
    Installed
    ''' <summary>有些檔案在、有些沒有（或大小不對）。</summary>
    Incomplete
    Missing
End Enum

''' <summary>
''' 設定 → 模型：發佈的程式不附 AI 模型，使用者逐個下載到程式資料夾的 Models\。
''' 清單在程式資料夾的 ModelCatalog.json（發佈者可以改，不用重新編譯）；下載先寫成 .download 暫存檔，
''' 大小核對無誤才換上，取消或失敗不會留下壞檔。來源可以是網址，也可以是資料夾或共用磁碟路徑（mirror）。
''' </summary>
Public NotInheritable Class ModelCatalog
    Public Property Mirror As String = ""
    Public Property Models As New List(Of ModelEntry)()
    ''' <summary>模型資料夾（預設是程式資料夾的 Models\）。</summary>
    Public Property ModelsDir As String = IO.Path.Combine(AppContext.BaseDirectory, "Models")

    Public Shared ReadOnly Property DefaultCatalogPath As String
        Get
            Return IO.Path.Combine(AppContext.BaseDirectory, "ModelCatalog.json")
        End Get
    End Property

    Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True, .ReadCommentHandling = JsonCommentHandling.Skip}

    ''' <summary>讀清單；檔案不在或格式錯時回傳空清單。</summary>
    Public Shared Function Load(Optional file As String = Nothing) As ModelCatalog
        file = If(file, DefaultCatalogPath)
        Try
            If IO.File.Exists(file) Then
                Dim c = JsonSerializer.Deserialize(Of ModelCatalog)(IO.File.ReadAllText(file), JsonOptions)
                If c IsNot Nothing Then
                    c.Models = If(c.Models, New List(Of ModelEntry)())
                    Return c
                End If
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
        Return New ModelCatalog()
    End Function

    Public Function FullPath(f As ModelFile) As String
        Return IO.Path.Combine(ModelsDir, f.Path.Replace("/"c, IO.Path.DirectorySeparatorChar))
    End Function

    ''' <summary>這個檔案在、而且大小對（清單沒寫大小時只看在不在）。</summary>
    Public Function FileOk(f As ModelFile) As Boolean
        Dim p = FullPath(f)
        If Not IO.File.Exists(p) Then Return False
        Return f.Size <= 0 OrElse New FileInfo(p).Length = f.Size
    End Function

    Public Function State(m As ModelEntry) As ModelState
        Dim ok = m.Files.Where(Function(f) FileOk(f)).Count()
        If ok = m.Files.Count Then Return ModelState.Installed
        Return If(ok = 0, ModelState.Missing, ModelState.Incomplete)
    End Function

    ''' <summary>這個檔案從哪裡下載（有 mirror 時一律從 mirror）；沒有位址時 Nothing。</summary>
    Public Function SourceOf(f As ModelFile) As String
        If Not String.IsNullOrWhiteSpace(Mirror) Then
            Dim m = Mirror.Trim()
            If IsWebAddress(m) Then Return m.TrimEnd("/"c) & "/" & f.Path.Replace("\"c, "/"c)
            Return IO.Path.Combine(m, f.Path.Replace("/"c, IO.Path.DirectorySeparatorChar))
        End If
        Return If(String.IsNullOrWhiteSpace(f.Url), Nothing, f.Url)
    End Function

    ''' <summary>每個缺少的檔案都有地方可以下載。</summary>
    Public Function CanDownload(m As ModelEntry) As Boolean
        Return m.Files.Where(Function(f) Not FileOk(f)).All(Function(f) SourceOf(f) IsNot Nothing)
    End Function

    ''' <summary>還要下載多少位元組。</summary>
    Public Function RemainingSize(m As ModelEntry) As Long
        Return m.Files.Where(Function(f) Not FileOk(f)).Sum(Function(f) f.Size)
    End Function

    Private Shared Function IsWebAddress(s As String) As Boolean
        Return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared ReadOnly Http As New HttpClient With {.Timeout = Timeout.InfiniteTimeSpan}

    Shared Sub New()
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoEdit-ModelDownloader/1.0")
    End Sub

    ''' <summary>
    ''' 下載這個模型缺少的檔案。progress(已下載, 總共) 在背景執行緒回報（位元組）。
    ''' 每個檔案先寫成 .download，大小核對正確才換上；取消或失敗會刪掉暫存檔並丟出例外。
    ''' </summary>
    Public Async Function DownloadAsync(m As ModelEntry, progress As Action(Of Long, Long), cancel As CancellationToken) As Task
        Dim todo = m.Files.Where(Function(f) Not FileOk(f)).ToList()
        Dim total = todo.Sum(Function(f) f.Size)
        Dim done As Long = 0
        For Each f In todo
            Dim src = SourceOf(f)
            If src Is Nothing Then Throw New InvalidOperationException(String.Format(Lang.T("「{0}」沒有下載位址。"), f.Path))
            Dim target = FullPath(f)
            Directory.CreateDirectory(IO.Path.GetDirectoryName(target))
            Dim tmp = target & ".download"
            Dim baseDone = done
            Try
                Using output As New FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync:=True)
                    If IsWebAddress(src) Then
                        Using resp = Await Http.GetAsync(src, HttpCompletionOption.ResponseHeadersRead, cancel)
                            resp.EnsureSuccessStatusCode()
                            Using input = Await resp.Content.ReadAsStreamAsync(cancel)
                                Await CopyAsync(input, output, Sub(n) progress?.Invoke(baseDone + n, total), cancel)
                            End Using
                        End Using
                    Else
                        Using input As New FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync:=True)
                            Await CopyAsync(input, output, Sub(n) progress?.Invoke(baseDone + n, total), cancel)
                        End Using
                    End If
                End Using
                Dim got = New FileInfo(tmp).Length
                If f.Size > 0 AndAlso got <> f.Size Then
                    Throw New IOException(String.Format(Lang.T("「{0}」大小不對（{1:N0} 位元組，應該是 {2:N0}），可能下載不完整。"), f.Path, got, f.Size))
                End If
                If IO.File.Exists(target) Then IO.File.Delete(target)
                IO.File.Move(tmp, target)
            Catch
                Try
                    If IO.File.Exists(tmp) Then IO.File.Delete(tmp)
                Catch ex As IOException
                End Try
                Throw
            End Try
            done += f.Size
            progress?.Invoke(done, total)
        Next
    End Function

    Private Shared Async Function CopyAsync(input As Stream, output As Stream, report As Action(Of Long), cancel As CancellationToken) As Task
        Dim buf(65535) As Byte
        Dim n As Long = 0
        Dim lastTick = 0
        Do
            Dim r = Await input.ReadAsync(buf, 0, buf.Length, cancel)
            If r <= 0 Then Exit Do
            Await output.WriteAsync(buf, 0, r, cancel)
            n += r
            ' 回報不要太頻繁（介面會一直重畫）
            If Environment.TickCount - lastTick > 100 Then
                lastTick = Environment.TickCount
                report(n)
            End If
        Loop
        report(n)
    End Function

    ''' <summary>刪除這個模型的檔案（騰出空間）。</summary>
    Public Sub Delete(m As ModelEntry)
        For Each f In m.Files
            Dim p = FullPath(f)
            If IO.File.Exists(p) Then IO.File.Delete(p)
        Next
    End Sub

    ''' <summary>大小的文字：KB／MB／GB。</summary>
    Public Shared Function SizeText(bytes As Long) As String
        If bytes >= 1L << 30 Then Return $"{bytes / (1L << 30):0.0} GB"
        If bytes >= 1L << 20 Then Return $"{bytes / (1L << 20):0.#} MB"
        Return $"{Math.Max(1, bytes \ 1024)} KB"
    End Function
End Class
