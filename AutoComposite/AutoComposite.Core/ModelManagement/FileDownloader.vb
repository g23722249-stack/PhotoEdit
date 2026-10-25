Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography

''' <summary>下載進度。</summary>
Public Structure DownloadProgress
    Public FileName As String
    Public FileIndex As Integer
    Public FileCount As Integer
    Public BytesDone As Long
    Public BytesTotal As Long
    Public OverallDone As Long
    Public OverallTotal As Long
    Public Message As String
End Structure

''' <summary>
''' 大型檔案下載：寫到 .part，支援續傳（Range）、取消、指數退避重試；完成後核對大小、
''' 排除 Git LFS 指標檔，並以可信的 SHA-256／Git blob SHA-1 驗證，通過才換成正式檔名。
''' </summary>
Public NotInheritable Class FileDownloader
    Private ReadOnly _http As HttpClient

    Public Property MaxRetries As Integer = 4
    ''' <summary>第一次重試前的等待（之後倍增）；測試可設為 0。</summary>
    Public Property InitialRetryDelay As TimeSpan = TimeSpan.FromSeconds(2)
    Public Property BufferSize As Integer = 1 << 20

    Public Sub New(http As HttpClient)
        _http = http
    End Sub

    ''' <summary>下載單一檔案到 destination。expectedSha256／gitOid 為空時不驗證該項。</summary>
    Public Async Function DownloadAsync(url As String, destination As String, expectedSize As Long, expectedSha256 As String, gitOid As String,
                                        progress As Action(Of Long, Long), cancel As Threading.CancellationToken) As Task
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)))
        Dim part = destination & ".part"
        Dim attempt = 0
        Dim delay = InitialRetryDelay
        Do
            cancel.ThrowIfCancellationRequested()
            Dim retry = False
            Try
                Await DownloadOnceAsync(url, part, expectedSize, progress, cancel).ConfigureAwait(False)
            Catch ex As Exception When IsTransient(ex) AndAlso Not cancel.IsCancellationRequested AndAlso attempt < MaxRetries
                AppLog.Warn($"下載中斷，第 {attempt + 1} 次重試（保留已下載部分）：{ex.Message}")
                retry = True
            End Try
            If Not retry Then Exit Do
            attempt += 1
            If delay > TimeSpan.Zero Then Await Task.Delay(delay, cancel).ConfigureAwait(False)
            delay = TimeSpan.FromTicks(delay.Ticks * 2)
        Loop

        ' 驗證：大小、LFS 指標檔、雜湊。
        Dim len = New FileInfo(part).Length
        If expectedSize > 0 AndAlso len <> expectedSize Then
            File.Delete(part)
            Throw New InvalidDataException($"{Path.GetFileName(destination)} 大小不符：預期 {expectedSize:N0}，實際 {len:N0} 位元組。")
        End If
        If IsLfsPointer(part) AndAlso (expectedSize <= 0 OrElse expectedSize > 1024) Then
            File.Delete(part)
            Throw New InvalidDataException($"{Path.GetFileName(destination)} 下載到的是 Git LFS 指標檔而不是模型權重。")
        End If
        If Not String.IsNullOrEmpty(expectedSha256) Then
            Dim actual = Await ComputeSha256Async(part, cancel).ConfigureAwait(False)
            If Not String.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase) Then
                File.Delete(part)
                Throw New InvalidDataException($"{Path.GetFileName(destination)} 的 SHA-256 不符（預期 {expectedSha256}，實際 {actual}），已刪除損壞檔。")
            End If
        ElseIf Not String.IsNullOrEmpty(gitOid) Then
            Dim actual = Await ComputeGitBlobSha1Async(part, cancel).ConfigureAwait(False)
            If Not String.Equals(actual, gitOid, StringComparison.OrdinalIgnoreCase) Then
                File.Delete(part)
                Throw New InvalidDataException($"{Path.GetFileName(destination)} 的 Git 雜湊不符，已刪除損壞檔。")
            End If
        End If
        File.Move(part, destination, True)
    End Function

    Private Async Function DownloadOnceAsync(url As String, part As String, expectedSize As Long, progress As Action(Of Long, Long), cancel As Threading.CancellationToken) As Task
        Dim existing = If(File.Exists(part), New FileInfo(part).Length, 0L)
        If expectedSize > 0 AndAlso existing > expectedSize Then
            File.Delete(part) : existing = 0
        End If
        If expectedSize > 0 AndAlso existing = expectedSize Then
            progress?.Invoke(existing, expectedSize)
            Return
        End If
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            If existing > 0 Then req.Headers.Range = New RangeHeaderValue(existing, Nothing)
            Using resp = Await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(False)
                If resp.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable Then
                    ' 伺服器說範圍無效：可能 .part 已完整，交給後續大小驗證；否則重新開始。
                    If expectedSize > 0 AndAlso existing = expectedSize Then Return
                    File.Delete(part)
                    Throw New IOException("續傳位置無效，重新下載。")
                End If
                If Not resp.IsSuccessStatusCode Then
                    Throw New HttpRequestException($"下載失敗（HTTP {CInt(resp.StatusCode)}）：{url}", Nothing, resp.StatusCode)
                End If
                Dim append = existing > 0 AndAlso resp.StatusCode = HttpStatusCode.PartialContent
                If Not append Then existing = 0
                Dim total = If(expectedSize > 0, expectedSize, If(resp.Content.Headers.ContentLength.HasValue, resp.Content.Headers.ContentLength.Value + existing, -1))
                Using src = Await resp.Content.ReadAsStreamAsync(cancel).ConfigureAwait(False)
                    Using dst As New FileStream(part, If(append, FileMode.Append, FileMode.Create), FileAccess.Write, FileShare.None, BufferSize, True)
                        Dim buf(BufferSize - 1) As Byte
                        Dim done = existing
                        Dim lastReport = 0L
                        Do
                            Dim n = Await src.ReadAsync(buf.AsMemory(0, buf.Length), cancel).ConfigureAwait(False)
                            If n <= 0 Then Exit Do
                            Await dst.WriteAsync(buf.AsMemory(0, n), cancel).ConfigureAwait(False)
                            done += n
                            If done - lastReport >= 256 * 1024 OrElse done = total Then
                                progress?.Invoke(done, total)
                                lastReport = done
                            End If
                        Loop
                        progress?.Invoke(done, total)
                        If expectedSize > 0 AndAlso done < expectedSize Then Throw New IOException($"連線提早結束（{done:N0}/{expectedSize:N0}）。")
                    End Using
                End Using
            End Using
        End Using
    End Function

    Private Shared Function IsTransient(ex As Exception) As Boolean
        If TypeOf ex Is OperationCanceledException Then Return TypeOf ex.InnerException Is TimeoutException
        If TypeOf ex Is IOException Then Return True
        Dim h = TryCast(ex, HttpRequestException)
        If h IsNot Nothing Then
            If Not h.StatusCode.HasValue Then Return True
            Dim c = CInt(h.StatusCode.Value)
            Return c = 408 OrElse c = 429 OrElse c >= 500
        End If
        Return False
    End Function

    ''' <summary>Git LFS 指標檔（小文字檔，開頭是 version https://git-lfs.github.com/spec/v1）。</summary>
    Public Shared Function IsLfsPointer(path As String) As Boolean
        Dim fi As New FileInfo(path)
        If Not fi.Exists OrElse fi.Length > 1024 Then Return False
        Dim head = File.ReadAllText(path)
        Return head.StartsWith("version https://git-lfs.github.com/spec/", StringComparison.Ordinal)
    End Function

    Public Shared Async Function ComputeSha256Async(path As String, cancel As Threading.CancellationToken) As Task(Of String)
        Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, True)
            Using sha = SHA256.Create()
                Dim h = Await sha.ComputeHashAsync(fs, cancel).ConfigureAwait(False)
                Return Convert.ToHexString(h).ToLowerInvariant()
            End Using
        End Using
    End Function

    ''' <summary>Git blob 雜湊：SHA-1("blob {size}\0" + 內容)。</summary>
    Public Shared Async Function ComputeGitBlobSha1Async(path As String, cancel As Threading.CancellationToken) As Task(Of String)
        Dim len = New FileInfo(path).Length
        Using sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1)
            sha.AppendData(Text.Encoding.ASCII.GetBytes($"blob {len}" & ChrW(0)))
            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, True)
                Dim buf((1 << 20) - 1) As Byte
                Do
                    Dim n = Await fs.ReadAsync(buf.AsMemory(0, buf.Length), cancel).ConfigureAwait(False)
                    If n <= 0 Then Exit Do
                    sha.AppendData(buf, 0, n)
                Loop
            End Using
            Return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant()
        End Using
    End Function
End Class
