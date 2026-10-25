Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports Xunit

''' <summary>模擬 Hub 下載端點：可設定第一次連線在傳到一半時中斷。</summary>
Friend NotInheritable Class FakeDownloadHandler
    Inherits HttpMessageHandler

    Public Property Content As Byte()
    ''' <summary>前幾次請求在送出這麼多位元組後中斷（模擬網路斷線）。</summary>
    Public Property BreakAfterBytes As Integer = -1
    Public Property BreakTimes As Integer = 1
    Public Property SupportRange As Boolean = True
    Public ReadOnly Property Requests As New List(Of String)()

    Protected Overrides Function SendAsync(request As HttpRequestMessage, cancellationToken As Threading.CancellationToken) As Task(Of HttpResponseMessage)
        Dim range = request.Headers.Range
        Dim start = 0L
        If range IsNot Nothing AndAlso SupportRange Then start = range.Ranges.First().From.GetValueOrDefault()
        Requests.Add(If(range Is Nothing, "full", $"range:{start}"))
        Dim body = Content.Skip(CInt(start)).ToArray()
        Dim stream As Stream
        If BreakTimes > 0 AndAlso BreakAfterBytes >= 0 Then
            BreakTimes -= 1
            stream = New BreakingStream(body, BreakAfterBytes)
        Else
            stream = New MemoryStream(body)
        End If
        Dim resp As New HttpResponseMessage(If(start > 0, HttpStatusCode.PartialContent, HttpStatusCode.OK)) With {.Content = New StreamContent(stream)}
        resp.Content.Headers.ContentLength = body.Length
        Return Task.FromResult(resp)
    End Function

    Private NotInheritable Class BreakingStream
        Inherits MemoryStream
        Private ReadOnly _limit As Integer
        Public Sub New(data As Byte(), limit As Integer)
            MyBase.New(data)
            _limit = limit
        End Sub
        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            If Position >= _limit Then Throw New IOException("模擬連線中斷")
            Return MyBase.Read(buffer, offset, CInt(Math.Min(count, _limit - Position)))
        End Function
        Public Overrides Function ReadAsync(buffer As Memory(Of Byte), Optional cancellationToken As Threading.CancellationToken = Nothing) As ValueTask(Of Integer)
            If Position >= _limit Then Throw New IOException("模擬連線中斷")
            Dim n = CInt(Math.Min(buffer.Length, _limit - Position))
            Return New ValueTask(Of Integer)(MyBase.Read(buffer.Span.Slice(0, n)))
        End Function
    End Class
End Class

''' <summary>模擬 Hub API。</summary>
Friend NotInheritable Class FakeHub
    Implements IHubClient

    Public Property Info As HubModelInfo
    Public Property Handler As FakeDownloadHandler
    Private ReadOnly _http As HttpClient

    Public Sub New(handler As FakeDownloadHandler)
        Me.Handler = handler
        _http = New HttpClient(handler)
    End Sub

    Public ReadOnly Property Http As HttpClient Implements IHubClient.Http
        Get
            Return _http
        End Get
    End Property

    Public Function GetModelInfoAsync(repo As String, revision As String, cancel As Threading.CancellationToken) As Task(Of HubModelInfo) Implements IHubClient.GetModelInfoAsync
        Return Task.FromResult(Info)
    End Function

    Public Function ResolveUrl(repo As String, commit As String, path As String) As String Implements IHubClient.ResolveUrl
        Return $"https://example.invalid/{repo}/resolve/{commit}/{path}"
    End Function
End Class

Public Class ModelManagerTests
    Private Shared Function Sha(b As Byte()) As String
        Return Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant()
    End Function

    Private Shared Function WriteManifest(dir As String, Optional license As String = "mit", Optional commercial As String = "allowed") As String
        Dim m As New ModelManifest With {
            .Id = "toon", .Name = "ToonOut", .Purpose = "segmentation", .Repo = "owner/toon", .Revision = "main",
            .License = New ManifestLicense With {.Expected = license, .CommercialUse = commercial},
            .Variants = New List(Of ManifestVariant) From {
                New ManifestVariant With {.Id = "fp16", .Files = New List(Of ManifestFileRule) From {
                    New ManifestFileRule With {.Pattern = "*fp16*.onnx", .Role = "model"},
                    New ManifestFileRule With {.Pattern = "*.onnx", .Role = "model"},
                    New ManifestFileRule With {.Pattern = "config.json", .Role = "config", .Required = False}}}}}
        Directory.CreateDirectory(dir)
        Dim p = Path.Combine(dir, "toon.json")
        File.WriteAllText(p, Text.Json.JsonSerializer.Serialize(m, ModelManifest.JsonOptions))
        Return dir
    End Function

    <Fact>
    Public Sub Plan_UsesActualHubFileList_NotGuessedNames()
        Dim root = TestHelpers.TempDir()
        Dim shipped = WriteManifest(Path.Combine(root, "shipped"))
        Dim hub As New FakeHub(New FakeDownloadHandler()) With {.Info = New HubModelInfo With {
            .Commit = "abc123", .License = "mit", .Files = New List(Of HubFile) From {
                New HubFile With {.Path = "README.md", .Size = 10},
                New HubFile With {.Path = "onnx/birefnet_toonout_fp16.onnx", .Size = 100, .IsLfs = True, .Sha256 = "x"},
                New HubFile With {.Path = "onnx/birefnet_toonout_fp16.onnx_data", .Size = 900, .IsLfs = True, .Sha256 = "y"},
                New HubFile With {.Path = "onnx/birefnet_fp32.onnx", .Size = 200, .IsLfs = True, .Sha256 = "z"}}}}
        Dim mgr As New ModelManager(Path.Combine(root, "Models"), hub, shipped)
        Dim plan = mgr.PlanInstallAsync("toon", "fp16", IntendedUse.Commercial, Nothing).Result
        Assert.Equal("abc123", plan.Remote.Commit)
        TestHelpers.SameSeq({"onnx/birefnet_toonout_fp16.onnx", "onnx/birefnet_toonout_fp16.onnx_data"}, plan.Files.Select(Function(f) f.File.Path))
        Assert.Equal("external-weights", plan.Files(1).Role)
        Assert.True(plan.License.Allowed)
    End Sub

    <Fact>
    Public Sub Plan_NoMatchingModel_ListsWhatExists()
        Dim root = TestHelpers.TempDir()
        Dim shipped = WriteManifest(Path.Combine(root, "shipped"))
        Dim hub As New FakeHub(New FakeDownloadHandler()) With {.Info = New HubModelInfo With {.Commit = "c", .Files = New List(Of HubFile) From {New HubFile With {.Path = "weights.safetensors"}}}}
        Dim mgr As New ModelManager(Path.Combine(root, "Models"), hub, shipped)
        Dim ex = Assert.ThrowsAsync(Of InvalidDataException)(Function() mgr.PlanInstallAsync("toon", "", IntendedUse.NonCommercial, Nothing)).Result
        Assert.Contains("找不到", ex.Message)
    End Sub

    <Fact>
    Public Sub Install_InterruptedDownload_ResumesWithRange_AndVerifiesSha256()
        Dim root = TestHelpers.TempDir()
        Dim shipped = WriteManifest(Path.Combine(root, "shipped"))
        Dim content = New Byte(300_000 - 1) {}
        Call New Random(1).NextBytes(content)
        Dim handler As New FakeDownloadHandler With {.Content = content, .BreakAfterBytes = 120_000, .BreakTimes = 1}
        Dim hub As New FakeHub(handler) With {.Info = New HubModelInfo With {.Commit = "c0ffee", .License = "mit", .Files = New List(Of HubFile) From {
            New HubFile With {.Path = "model_fp16.onnx", .Size = content.Length, .IsLfs = True, .Sha256 = Sha(content)}}}}
        Dim mgr As New ModelManager(Path.Combine(root, "Models"), hub, shipped)
        mgr.Downloader.InitialRetryDelay = TimeSpan.Zero
        Dim plan = mgr.PlanInstallAsync("toon", "", IntendedUse.NonCommercial, Nothing).Result
        Dim reports As New List(Of DownloadProgress)()
        Dim st = mgr.InstallAsync(plan, IntendedUse.NonCommercial, False, New SyncProgress(Of DownloadProgress)(Sub(p) reports.Add(p)), Nothing).Result
        TestHelpers.SameSeq({"full", "range:120000"}, handler.Requests)
        Assert.Equal("c0ffee", st.Commit)
        Dim modelPath = mgr.PrimaryModelPath("toon")
        TestHelpers.SameSeq(content, File.ReadAllBytes(modelPath))
        Assert.False(File.Exists(modelPath & ".part"))
        Assert.True(mgr.GetStatus("toon").IsInstalled)
        Assert.Empty(mgr.VerifyAsync("toon", Nothing).Result)
        Assert.Contains(reports, Function(r) r.OverallDone = content.Length)
    End Sub

    <Fact>
    Public Sub Install_CorruptDownload_IsDeletedAndReported()
        Dim root = TestHelpers.TempDir()
        Dim shipped = WriteManifest(Path.Combine(root, "shipped"))
        Dim content = Text.Encoding.ASCII.GetBytes(New String("A"c, 5000))
        Dim handler As New FakeDownloadHandler With {.Content = content}
        Dim hub As New FakeHub(handler) With {.Info = New HubModelInfo With {.Commit = "c", .License = "mit", .Files = New List(Of HubFile) From {
            New HubFile With {.Path = "m.onnx", .Size = content.Length, .IsLfs = True, .Sha256 = New String("0"c, 64)}}}}
        Dim mgr As New ModelManager(Path.Combine(root, "Models"), hub, shipped)
        Dim plan = mgr.PlanInstallAsync("toon", "", IntendedUse.NonCommercial, Nothing).Result
        Dim ex = Assert.ThrowsAsync(Of InvalidDataException)(Function() mgr.InstallAsync(plan, IntendedUse.NonCommercial, False, Nothing, Nothing)).Result
        Assert.Contains("SHA-256", ex.Message)
        Assert.False(mgr.GetStatus("toon").IsInstalled)
        Assert.Null(mgr.PrimaryModelPath("toon"))
        Assert.Empty(Directory.GetFiles(mgr.ModelDirectory(mgr.GetManifest("toon")), "*", SearchOption.AllDirectories))
    End Sub

    <Fact>
    Public Sub LfsPointerFile_IsNotAcceptedAsWeights()
        Dim root = TestHelpers.TempDir()
        Dim shipped = WriteManifest(Path.Combine(root, "shipped"))
        Dim pointer = Text.Encoding.ASCII.GetBytes("version https://git-lfs.github.com/spec/v1" & vbLf & "oid sha256:abc" & vbLf & "size 123456789" & vbLf)
        Dim hub As New FakeHub(New FakeDownloadHandler With {.Content = pointer}) With {.Info = New HubModelInfo With {.Commit = "c", .License = "mit", .Files = New List(Of HubFile) From {
            New HubFile With {.Path = "m.onnx", .Size = 0, .IsLfs = False}}}}
        Dim mgr As New ModelManager(Path.Combine(root, "Models"), hub, shipped)
        Dim plan = mgr.PlanInstallAsync("toon", "", IntendedUse.NonCommercial, Nothing).Result
        Dim ex = Assert.ThrowsAsync(Of InvalidDataException)(Function() mgr.InstallAsync(plan, IntendedUse.NonCommercial, False, Nothing, Nothing)).Result
        Assert.Contains("LFS", ex.Message)
    End Sub

    <Fact>
    Public Sub NonCommercialLicense_BlocksCommercialUse()
        Dim root = TestHelpers.TempDir()
        Dim shipped = WriteManifest(Path.Combine(root, "shipped"), "cc-by-nc-4.0", "forbidden")
        Dim hub As New FakeHub(New FakeDownloadHandler With {.Content = {1, 2, 3}}) With {.Info = New HubModelInfo With {.Commit = "c", .License = "cc-by-nc-4.0", .Files = New List(Of HubFile) From {
            New HubFile With {.Path = "m.onnx", .Size = 3}}}}
        Dim mgr As New ModelManager(Path.Combine(root, "Models"), hub, shipped)
        Dim plan = mgr.PlanInstallAsync("toon", "", IntendedUse.Commercial, Nothing).Result
        Assert.False(plan.License.Allowed)
        Assert.ThrowsAsync(Of UnauthorizedAccessException)(Function() mgr.InstallAsync(plan, IntendedUse.Commercial, True, Nothing, Nothing)).Wait()
        ' 非商業用途：允許，但必須確認。
        Dim plan2 = mgr.PlanInstallAsync("toon", "", IntendedUse.NonCommercial, Nothing).Result
        Assert.True(plan2.License.Allowed)
        Assert.True(plan2.License.RequiresConfirmation)
        Assert.ThrowsAsync(Of UnauthorizedAccessException)(Function() mgr.InstallAsync(plan2, IntendedUse.NonCommercial, False, Nothing, Nothing)).Wait()
    End Sub

    <Fact>
    Public Sub LicensePolicy_HubLicenseMismatchRequiresConfirmation()
        Dim m As New ModelManifest With {.License = New ManifestLicense With {.Expected = "mit", .CommercialUse = "allowed"}}
        Assert.False(LicensePolicy.Evaluate(m, "mit", IntendedUse.Commercial).RequiresConfirmation)
        Dim d = LicensePolicy.Evaluate(m, "apache-2.0", IntendedUse.Commercial)
        Assert.True(d.RequiresConfirmation)
        Assert.Contains("不同", d.Message)
        Assert.True(LicensePolicy.Evaluate(m, "other", IntendedUse.Commercial).RequiresConfirmation)
    End Sub

    <Fact>
    Public Sub OfflineStatus_DetectsMissingFilesWithoutNetwork()
        Dim root = TestHelpers.TempDir()
        Dim shipped = WriteManifest(Path.Combine(root, "shipped"))
        Dim mgr As New ModelManager(Path.Combine(root, "Models"), New FakeHub(New FakeDownloadHandler()), shipped)
        Assert.Equal(ModelInstallStatus.NotInstalled, mgr.GetStatus("toon").Status)
        mgr.SaveState(New ModelInstallState With {.ModelId = "toon", .Commit = "c", .InstalledUtc = DateTime.UtcNow,
            .Files = New List(Of InstalledFile) From {New InstalledFile With {.LocalPath = "m.onnx", .Role = "model", .Size = 3}}})
        Dim s = mgr.GetStatus("toon")
        Assert.Equal(ModelInstallStatus.Broken, s.Status)
        Assert.Contains(s.Problems, Function(p) p.Contains("m.onnx"))
    End Sub

    <Fact>
    Public Sub GlobPattern_Matches()
        Assert.True(GlobPattern.IsMatch("onnx/model_fp16.onnx", "*.onnx"))
        Assert.True(GlobPattern.IsMatch("onnx/model_fp16.onnx", "onnx/*fp16*.onnx"))
        Assert.False(GlobPattern.IsMatch("onnx/model_fp16.onnx", "model.onnx"))
        Assert.True(GlobPattern.IsMatch("a/b/c.json", "**.json"))
    End Sub

    <Fact>
    Public Sub ShippedManifests_AreValid()
        Dim dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AutoComposite.App", "ModelStore", "manifests")
        Assert.True(Directory.Exists(dir), dir)
        Dim files = Directory.GetFiles(dir, "*.json")
        Assert.NotEmpty(files)
        For Each f In files
            Dim m = ModelManifest.Load(f)
            Assert.False(String.IsNullOrWhiteSpace(m.Repo), f)
            Assert.NotEmpty(m.Variants)
            Assert.Contains(m.Variants(0).Files, Function(r) r.Role = "model")
        Next
    End Sub
End Class

''' <summary>同步回報的 IProgress（測試用，避免 Progress(Of T) 的非同步回呼造成時序不定）。</summary>
Friend NotInheritable Class SyncProgress(Of T)
    Implements IProgress(Of T)
    Private ReadOnly _a As Action(Of T)
    Public Sub New(a As Action(Of T))
        _a = a
    End Sub
    Public Sub Report(value As T) Implements IProgress(Of T).Report
        _a(value)
    End Sub
End Class
