Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text.Json

''' <summary>Hub 上的一個檔案。</summary>
Public NotInheritable Class HubFile
    Public Property Path As String = ""
    Public Property Size As Long
    ''' <summary>Git blob SHA-1（非 LFS 檔案用來驗證）。</summary>
    Public Property GitOid As String = ""
    ''' <summary>LFS／Xet 大型檔案的 SHA-256（可信的預期雜湊值）。</summary>
    Public Property Sha256 As String = ""
    Public Property IsLfs As Boolean
End Class

''' <summary>解析後的遠端模型資訊。</summary>
Public NotInheritable Class HubModelInfo
    Public Property Repo As String = ""
    ''' <summary>解析後的 commit SHA。</summary>
    Public Property Commit As String = ""
    Public Property License As String = ""
    Public Property Gated As Boolean
    Public Property Files As New List(Of HubFile)()
End Class

Public Interface IHubClient
    Function GetModelInfoAsync(repo As String, revision As String, cancel As Threading.CancellationToken) As Task(Of HubModelInfo)
    Function ResolveUrl(repo As String, commit As String, path As String) As String
    ReadOnly Property Http As HttpClient
End Interface

''' <summary>
''' Hugging Face Hub API：用官方 REST 端點取得 commit、模型卡授權與完整檔案清單（含 LFS SHA-256），
''' 下載走 /resolve/{commit}/{path} 端點。環境變數 HF_TOKEN 有值時附上（存取需要同意條款的模型）。
''' </summary>
Public NotInheritable Class HubClient
    Implements IHubClient

    Public Const DefaultEndpoint As String = "https://huggingface.co"
    Private ReadOnly _endpoint As String

    Public ReadOnly Property Http As HttpClient Implements IHubClient.Http

    Public Sub New(Optional http As HttpClient = Nothing, Optional endpoint As String = Nothing)
        _endpoint = If(String.IsNullOrWhiteSpace(endpoint), If(Environment.GetEnvironmentVariable("HF_ENDPOINT"), DefaultEndpoint), endpoint).TrimEnd("/"c)
        Me.Http = If(http, CreateDefaultHttp())
    End Sub

    Public Shared Function CreateDefaultHttp() As HttpClient
        Dim handler As New SocketsHttpHandler With {.AutomaticDecompression = DecompressionMethods.None, .AllowAutoRedirect = True, .MaxAutomaticRedirections = 10,
                                                    .ConnectTimeout = TimeSpan.FromSeconds(30)}
        Dim c As New HttpClient(handler) With {.Timeout = Threading.Timeout.InfiniteTimeSpan}
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AutoCompositeStudio/1.0")
        Dim token = Environment.GetEnvironmentVariable("HF_TOKEN")
        If Not String.IsNullOrWhiteSpace(token) Then c.DefaultRequestHeaders.Authorization = New AuthenticationHeaderValue("Bearer", token)
        Return c
    End Function

    Public Function ResolveUrl(repo As String, commit As String, path As String) As String Implements IHubClient.ResolveUrl
        Dim encodedPath = String.Join("/", path.Split("/"c).Select(Function(s) Uri.EscapeDataString(s)))
        Return $"{_endpoint}/{repo}/resolve/{Uri.EscapeDataString(commit)}/{encodedPath}"
    End Function

    Public Async Function GetModelInfoAsync(repo As String, revision As String, cancel As Threading.CancellationToken) As Task(Of HubModelInfo) Implements IHubClient.GetModelInfoAsync
        If String.IsNullOrWhiteSpace(repo) OrElse Not repo.Contains("/"c) Then Throw New ArgumentException($"模型倉庫名稱無效：{repo}")
        Dim rev = If(String.IsNullOrWhiteSpace(revision), "main", revision)
        Dim info As New HubModelInfo With {.Repo = repo}

        Using doc = Await GetJsonAsync($"{_endpoint}/api/models/{repo}/revision/{Uri.EscapeDataString(rev)}", cancel).ConfigureAwait(False)
            Dim root = doc.RootElement
            info.Commit = GetString(root, "sha")
            Dim card As JsonElement
            If root.TryGetProperty("cardData", card) AndAlso card.ValueKind = JsonValueKind.Object Then
                Dim lic As JsonElement
                If card.TryGetProperty("license", lic) Then
                    info.License = If(lic.ValueKind = JsonValueKind.Array, String.Join(",", lic.EnumerateArray().Select(Function(e) e.ToString())), lic.ToString())
                End If
            End If
            Dim gated As JsonElement
            If root.TryGetProperty("gated", gated) Then info.Gated = gated.ValueKind = JsonValueKind.String OrElse gated.ValueKind = JsonValueKind.True
        End Using
        If String.IsNullOrEmpty(info.Commit) Then Throw New IO.InvalidDataException($"Hub 沒有回傳 {repo}@{rev} 的 commit。")

        ' 完整檔案清單（遞迴），有 Link: rel="next" 時繼續翻頁。
        Dim url = $"{_endpoint}/api/models/{repo}/tree/{info.Commit}?recursive=true&expand=false"
        While Not String.IsNullOrEmpty(url)
            Using resp = Await SendAsync(url, cancel).ConfigureAwait(False)
                Using s = Await resp.Content.ReadAsStreamAsync(cancel).ConfigureAwait(False)
                    Using doc = Await JsonDocument.ParseAsync(s, Nothing, cancel).ConfigureAwait(False)
                        For Each e In doc.RootElement.EnumerateArray()
                            If GetString(e, "type") <> "file" Then Continue For
                            Dim f As New HubFile With {.Path = GetString(e, "path"), .GitOid = GetString(e, "oid")}
                            Dim sz As JsonElement
                            If e.TryGetProperty("size", sz) AndAlso sz.ValueKind = JsonValueKind.Number Then f.Size = sz.GetInt64()
                            Dim lfs As JsonElement
                            If e.TryGetProperty("lfs", lfs) AndAlso lfs.ValueKind = JsonValueKind.Object Then
                                f.IsLfs = True
                                f.Sha256 = GetString(lfs, "oid")
                                Dim ls As JsonElement
                                If lfs.TryGetProperty("size", ls) AndAlso ls.ValueKind = JsonValueKind.Number Then f.Size = ls.GetInt64()
                            End If
                            info.Files.Add(f)
                        Next
                    End Using
                End Using
                url = NextLink(resp)
            End Using
        End While
        Return info
    End Function

    Private Async Function GetJsonAsync(url As String, cancel As Threading.CancellationToken) As Task(Of JsonDocument)
        Using resp = Await SendAsync(url, cancel).ConfigureAwait(False)
            Using s = Await resp.Content.ReadAsStreamAsync(cancel).ConfigureAwait(False)
                Return Await JsonDocument.ParseAsync(s, Nothing, cancel).ConfigureAwait(False)
            End Using
        End Using
    End Function

    Private Async Function SendAsync(url As String, cancel As Threading.CancellationToken) As Task(Of HttpResponseMessage)
        Dim resp = Await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(False)
        If Not resp.IsSuccessStatusCode Then
            Dim code = CInt(resp.StatusCode)
            resp.Dispose()
            Select Case code
                Case 401, 403
                    Throw New HttpRequestException($"Hub 拒絕存取（HTTP {code}）：模型可能需要登入並同意條款，請設定環境變數 HF_TOKEN。", Nothing, CType(code, HttpStatusCode))
                Case 404
                    Throw New HttpRequestException($"Hub 找不到資源（HTTP 404）：{url}", Nothing, HttpStatusCode.NotFound)
                Case Else
                    Throw New HttpRequestException($"Hub 回應 HTTP {code}：{url}", Nothing, CType(code, HttpStatusCode))
            End Select
        End If
        Return resp
    End Function

    Private Shared Function NextLink(resp As HttpResponseMessage) As String
        Dim values As IEnumerable(Of String) = Nothing
        If Not resp.Headers.TryGetValues("Link", values) Then Return Nothing
        For Each v In values
            For Each part In v.Split(","c)
                If part.Contains("rel=""next""") Then
                    Dim a = part.IndexOf("<"c), b = part.IndexOf(">"c)
                    If a >= 0 AndAlso b > a Then Return part.Substring(a + 1, b - a - 1)
                End If
            Next
        Next
        Return Nothing
    End Function

    Private Shared Function GetString(e As JsonElement, name As String) As String
        Dim v As JsonElement
        If e.ValueKind = JsonValueKind.Object AndAlso e.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return ""
    End Function
End Class
