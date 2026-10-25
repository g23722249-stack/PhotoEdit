Imports System.IO
Imports System.Text.Json

''' <summary>已安裝模型的一個檔案。</summary>
Public NotInheritable Class InstalledFile
    Public Property HubPath As String = ""
    ''' <summary>相對於模型資料夾的路徑。</summary>
    Public Property LocalPath As String = ""
    Public Property Role As String = ""
    Public Property Size As Long
    Public Property Sha256 As String = ""
    Public Property GitOid As String = ""
    Public Property Verified As Boolean
End Class

Public Enum ModelInstallStatus
    NotInstalled
    Installed
    ''' <summary>檔案遺失或大小不符。</summary>
    Broken
End Enum

''' <summary>模型安裝狀態（Models/manifests/{id}.state.json）。</summary>
Public NotInheritable Class ModelInstallState
    Public Property ModelId As String = ""
    Public Property Repo As String = ""
    ''' <summary>安裝時鎖定的 commit；之後不會自動更新。</summary>
    Public Property Commit As String = ""
    Public Property VariantId As String = ""
    Public Property HubLicense As String = ""
    Public Property LicenseAcceptedUtc As DateTime?
    Public Property IntendedUse As IntendedUse = IntendedUse.NonCommercial
    Public Property InstalledUtc As DateTime?
    Public Property Files As New List(Of InstalledFile)()
    Public Property LastError As String = ""
    ''' <summary>最近一次載入驗證的結果（輸入輸出、opset、執行提供者）。</summary>
    Public Property LastValidation As String = ""
End Class

''' <summary>模型清單＋本機狀態（不需網路）。</summary>
Public NotInheritable Class ModelStatus
    Public Property Manifest As ModelManifest
    Public Property State As ModelInstallState
    Public Property Status As ModelInstallStatus
    Public Property Problems As New List(Of String)()

    Public ReadOnly Property IsInstalled As Boolean
        Get
            Return Status = ModelInstallStatus.Installed
        End Get
    End Property
End Class

''' <summary>安裝計畫：遠端資訊與實際要下載的檔案。</summary>
Public NotInheritable Class InstallPlan
    Public Property Manifest As ModelManifest
    Public Property VariantId As String = ""
    Public Property Remote As HubModelInfo
    Public Property Files As New List(Of (File As HubFile, Role As String))()
    Public Property License As LicenseDecision

    Public ReadOnly Property TotalBytes As Long
        Get
            Return Files.Sum(Function(f) f.File.Size)
        End Get
    End Property
End Class

''' <summary>
''' 模型管理：讀取模型清單、查詢本機狀態（離線可用）、解析 Hub 檔案清單、下載與驗證、刪除。
''' 資料夾結構：
'''   Models/manifests/   清單（*.json）與安裝狀態（*.state.json）
'''   Models/segmentation/{id}/、Models/inpainting/{id}/、Models/depth/{id}/   模型檔
'''   Models/cache/       下載暫存
''' 只下載使用者啟用的模型，不在啟動時連網或自動升級。
''' </summary>
Public NotInheritable Class ModelManager
    Private ReadOnly _hub As IHubClient
    Private ReadOnly _downloader As FileDownloader
    Private ReadOnly _manifests As New Dictionary(Of String, ModelManifest)(StringComparer.OrdinalIgnoreCase)

    Public ReadOnly Property RootDirectory As String
    Public ReadOnly Property ManifestsDirectory As String
    Public ReadOnly Property CacheDirectory As String

    Public Sub New(rootDirectory As String, Optional hub As IHubClient = Nothing, Optional shippedManifestsDirectory As String = Nothing)
        Me.RootDirectory = Path.GetFullPath(rootDirectory)
        ManifestsDirectory = Path.Combine(Me.RootDirectory, "manifests")
        CacheDirectory = Path.Combine(Me.RootDirectory, "cache")
        For Each d In {ManifestsDirectory, CacheDirectory, Path.Combine(Me.RootDirectory, "segmentation"), Path.Combine(Me.RootDirectory, "inpainting")}
            Directory.CreateDirectory(d)
        Next
        _hub = If(hub, New HubClient())
        _downloader = New FileDownloader(_hub.Http)
        LoadManifests(shippedManifestsDirectory)
    End Sub

    Public ReadOnly Property Downloader As FileDownloader
        Get
            Return _downloader
        End Get
    End Property

    ''' <summary>
    ''' 預設模型資料夾：程式資料夾的 Models\（可寫入時，和 PhotoEdit 慣例一致），否則 %LOCALAPPDATA%\AutoComposite\Models。
    ''' </summary>
    Public Shared Function DefaultRoot() As String
        Dim appModels = Path.Combine(AppContext.BaseDirectory, "Models")
        Try
            Directory.CreateDirectory(appModels)
            Dim probe = Path.Combine(appModels, ".write-test")
            File.WriteAllText(probe, "")
            File.Delete(probe)
            Return appModels
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoComposite", "Models")
        End Try
    End Function

    Public ReadOnly Property Manifests As IReadOnlyCollection(Of ModelManifest)
        Get
            Return _manifests.Values
        End Get
    End Property

    Public Function GetManifest(id As String) As ModelManifest
        Dim m As ModelManifest = Nothing
        If _manifests.TryGetValue(id, m) Then Return m
        Throw New KeyNotFoundException($"找不到模型清單：{id}")
    End Function

    ''' <summary>讀清單：先讀程式附帶的清單，再讀模型資料夾內的（同 id 時後者覆蓋）。格式錯誤的清單略過並記錄。</summary>
    Public Sub LoadManifests(Optional shippedDirectory As String = Nothing)
        _manifests.Clear()
        For Each folder In {shippedDirectory, ManifestsDirectory}
            If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then Continue For
            For Each f In Directory.GetFiles(folder, "*.json").Where(Function(x) Not x.EndsWith(".state.json", StringComparison.OrdinalIgnoreCase)).OrderBy(Function(x) x)
                Try
                    Dim m = ModelManifest.Load(f)
                    _manifests(m.Id) = m
                Catch ex As Exception When TypeOf ex Is JsonException OrElse TypeOf ex Is InvalidDataException OrElse TypeOf ex Is IOException
                    AppLog.Warn($"略過格式錯誤的模型清單 {f}：{ex.Message}")
                End Try
            Next
        Next
    End Sub

    Public Function ModelDirectory(m As ModelManifest) As String
        Dim purpose = If(String.IsNullOrWhiteSpace(m.Purpose), "other", m.Purpose)
        Return Path.Combine(RootDirectory, purpose, SafeName(m.Id))
    End Function

    Private Function StatePath(id As String) As String
        Return Path.Combine(ManifestsDirectory, SafeName(id) & ".state.json")
    End Function

    Private Shared Function SafeName(s As String) As String
        Dim invalid = Path.GetInvalidFileNameChars()
        Return New String(s.Select(Function(c) If(invalid.Contains(c) OrElse c = "/"c OrElse c = "\"c, "_"c, c)).ToArray())
    End Function

    Public Function LoadState(id As String) As ModelInstallState
        Dim p = StatePath(id)
        If Not File.Exists(p) Then Return Nothing
        Try
            Return JsonSerializer.Deserialize(Of ModelInstallState)(File.ReadAllText(p), ModelManifest.JsonOptions)
        Catch ex As JsonException
            AppLog.Warn($"模型狀態檔 {p} 損壞：{ex.Message}")
            Return Nothing
        End Try
    End Function

    Public Sub SaveState(state As ModelInstallState)
        Dim p = StatePath(state.ModelId)
        File.WriteAllText(p & ".tmp", JsonSerializer.Serialize(state, ModelManifest.JsonOptions))
        File.Move(p & ".tmp", p, True)
    End Sub

    ''' <summary>本機狀態（只看檔案是否存在、大小是否正確，不連網）。</summary>
    Public Function GetStatus(id As String) As ModelStatus
        Dim m = GetManifest(id)
        Dim st = LoadState(id)
        Dim r As New ModelStatus With {.Manifest = m, .State = st, .Status = ModelInstallStatus.NotInstalled}
        If st Is Nothing OrElse st.Files.Count = 0 OrElse Not st.InstalledUtc.HasValue Then Return r
        Dim dir = ModelDirectory(m)
        For Each f In st.Files
            Dim full = Path.Combine(dir, f.LocalPath)
            If Not File.Exists(full) Then
                r.Problems.Add($"缺少檔案 {f.LocalPath}")
            ElseIf f.Size > 0 AndAlso New FileInfo(full).Length <> f.Size Then
                r.Problems.Add($"{f.LocalPath} 大小不符")
            End If
        Next
        r.Status = If(r.Problems.Count = 0, ModelInstallStatus.Installed, ModelInstallStatus.Broken)
        Return r
    End Function

    ''' <summary>主模型（.onnx）的完整路徑；未安裝時回傳 Nothing。</summary>
    Public Function PrimaryModelPath(id As String) As String
        Dim s = GetStatus(id)
        If Not s.IsInstalled Then Return Nothing
        Dim f = s.State.Files.FirstOrDefault(Function(x) x.Role = "model")
        If f Is Nothing Then Return Nothing
        Return Path.Combine(ModelDirectory(s.Manifest), f.LocalPath)
    End Function

    ''' <summary>
    ''' 向 Hub 取得 commit 與檔案清單，依變體規則挑出實際要下載的檔案，並評估授權。
    ''' 已安裝過的模型沿用鎖定的 commit（除非 forceRevision 指定）。
    ''' </summary>
    Public Async Function PlanInstallAsync(id As String, variantId As String, use As IntendedUse, cancel As Threading.CancellationToken, Optional forceRevision As String = Nothing) As Task(Of InstallPlan)
        Dim m = GetManifest(id)
        If Not m.Installable Then Throw New InvalidOperationException($"{m.Name} 目前只提供說明，推論管線尚未完成，無法安裝。")
        Dim st = LoadState(id)
        Dim rev = If(Not String.IsNullOrEmpty(forceRevision), forceRevision, If(Not String.IsNullOrEmpty(st?.Commit), st.Commit, m.Revision))
        Dim remote = Await _hub.GetModelInfoAsync(m.Repo, rev, cancel).ConfigureAwait(False)
        Dim v = m.GetVariant(variantId)
        Dim plan As New InstallPlan With {.Manifest = m, .VariantId = v.Id, .Remote = remote}
        plan.License = LicensePolicy.Evaluate(m, remote.License, use)

        Dim paths = remote.Files.OrderBy(Function(f) f.Path, StringComparer.Ordinal).ToList()
        Dim primary As HubFile = Nothing
        For Each rule In v.Files
            If rule.Role = "model" Then
                ' 主模型：取第一條符合任何檔案的規則（規則依優先順序排列）。
                If primary IsNot Nothing Then Continue For
                primary = paths.FirstOrDefault(Function(f) GlobPattern.IsMatch(f.Path, rule.Pattern))
                If primary IsNot Nothing Then plan.Files.Add((primary, "model"))
            Else
                Dim matches = paths.Where(Function(f) GlobPattern.IsMatch(f.Path, rule.Pattern)).ToList()
                If matches.Count = 0 AndAlso rule.Required Then Throw New InvalidDataException($"{m.Repo}@{remote.Commit.Substring(0, Math.Min(8, remote.Commit.Length))} 找不到必要檔案（{rule.Pattern}）。")
                For Each f In matches
                    If Not plan.Files.Any(Function(x) x.File.Path = f.Path) Then plan.Files.Add((f, rule.Role))
                Next
            End If
        Next
        If primary Is Nothing Then
            Throw New InvalidDataException($"{m.Repo} 的檔案清單中找不到符合規則的主模型檔：{String.Join("、", v.Files.Where(Function(r) r.Role = "model").Select(Function(r) r.Pattern))}。" &
                                           $"倉庫內的 .onnx 檔：{String.Join("、", paths.Where(Function(f) f.Path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)).Select(Function(f) f.Path))}")
        End If
        ' 主模型的外部權重（與主模型同名、加 .data／_data／.pb 等後綴，或同資料夾的 *.onnx_data）一併下載。
        Dim dirPrefix = If(primary.Path.Contains("/"c), primary.Path.Substring(0, primary.Path.LastIndexOf("/"c) + 1), "")
        For Each f In paths
            If f.Path = primary.Path OrElse plan.Files.Any(Function(x) x.File.Path = f.Path) Then Continue For
            Dim isExternal = f.Path.StartsWith(primary.Path, StringComparison.Ordinal) AndAlso f.Path.Length > primary.Path.Length
            isExternal = isExternal OrElse (f.Path.StartsWith(dirPrefix, StringComparison.Ordinal) AndAlso Not f.Path.Substring(dirPrefix.Length).Contains("/"c) AndAlso
                                            Path.GetFileNameWithoutExtension(f.Path) = Path.GetFileNameWithoutExtension(primary.Path) AndAlso
                                            (f.Path.EndsWith(".onnx_data", StringComparison.OrdinalIgnoreCase) OrElse f.Path.EndsWith(".data", StringComparison.OrdinalIgnoreCase)))
            If isExternal Then plan.Files.Add((f, "external-weights"))
        Next
        Return plan
    End Function

    ''' <summary>
    ''' 依計畫下載與驗證，全部成功才寫入安裝狀態。授權不允許或需要確認但未確認時拒絕安裝。
    ''' </summary>
    Public Async Function InstallAsync(plan As InstallPlan, use As IntendedUse, licenseConfirmed As Boolean, progress As IProgress(Of DownloadProgress), cancel As Threading.CancellationToken) As Task(Of ModelInstallState)
        Dim m = plan.Manifest
        If Not plan.License.Allowed Then Throw New UnauthorizedAccessException(plan.License.Message)
        If plan.License.RequiresConfirmation AndAlso Not licenseConfirmed Then Throw New UnauthorizedAccessException("需要先閱讀並確認授權才能安裝：" & plan.License.Message)
        Dim dir = ModelDirectory(m)
        Directory.CreateDirectory(dir)
        Dim st As New ModelInstallState With {.ModelId = m.Id, .Repo = m.Repo, .Commit = plan.Remote.Commit, .VariantId = plan.VariantId,
            .HubLicense = plan.Remote.License, .IntendedUse = use, .LicenseAcceptedUtc = If(licenseConfirmed OrElse Not plan.License.RequiresConfirmation, DateTime.UtcNow, CType(Nothing, DateTime?))}
        Dim overallTotal = plan.TotalBytes
        Dim overallDone = 0L
        Try
            For i = 0 To plan.Files.Count - 1
                Dim f = plan.Files(i).File
                Dim local = f.Path.Replace("/"c, Path.DirectorySeparatorChar)
                Dim dest = Path.Combine(dir, local)
                Dim baseDone = overallDone
                Dim idx = i
                Await _downloader.DownloadAsync(_hub.ResolveUrl(m.Repo, plan.Remote.Commit, f.Path), dest, f.Size,
                    If(f.IsLfs, f.Sha256, ""), If(f.IsLfs, "", f.GitOid),
                    Sub(done, total) progress?.Report(New DownloadProgress With {
                        .FileName = f.Path, .FileIndex = idx, .FileCount = plan.Files.Count, .BytesDone = done, .BytesTotal = total,
                        .OverallDone = baseDone + done, .OverallTotal = overallTotal, .Message = $"下載 {f.Path}"}),
                    cancel).ConfigureAwait(False)
                overallDone += f.Size
                st.Files.Add(New InstalledFile With {.HubPath = f.Path, .LocalPath = local, .Role = plan.Files(i).Role, .Size = f.Size,
                                                     .Sha256 = f.Sha256, .GitOid = f.GitOid, .Verified = True})
            Next
            st.InstalledUtc = DateTime.UtcNow
            SaveState(st)
            AppLog.Info($"模型 {m.Id} 安裝完成：{m.Repo}@{plan.Remote.Commit}，{st.Files.Count} 個檔案，{overallTotal:N0} 位元組。")
            Return st
        Catch ex As OperationCanceledException
            AppLog.Info($"模型 {m.Id} 下載已取消（已下載部分保留為 .part，下次可續傳）。")
            Throw
        Catch ex As Exception
            Dim failed = If(LoadState(m.Id), st)
            failed.LastError = ex.Message
            If failed.InstalledUtc.HasValue OrElse failed Is st Then SaveState(failed)
            AppLog.Error($"模型 {m.Id} 安裝失敗", ex)
            Throw
        End Try
    End Function

    ''' <summary>重新計算本機檔案雜湊並與安裝時的預期值比對。</summary>
    Public Async Function VerifyAsync(id As String, cancel As Threading.CancellationToken) As Task(Of List(Of String))
        Dim s = GetStatus(id)
        Dim problems = New List(Of String)(s.Problems)
        If s.State Is Nothing Then
            problems.Add("尚未安裝。")
            Return problems
        End If
        Dim dir = ModelDirectory(s.Manifest)
        For Each f In s.State.Files
            Dim full = Path.Combine(dir, f.LocalPath)
            If Not File.Exists(full) Then Continue For
            If FileDownloader.IsLfsPointer(full) AndAlso f.Size > 1024 Then problems.Add($"{f.LocalPath} 是 Git LFS 指標檔")
            If Not String.IsNullOrEmpty(f.Sha256) Then
                Dim h = Await FileDownloader.ComputeSha256Async(full, cancel).ConfigureAwait(False)
                If Not String.Equals(h, f.Sha256, StringComparison.OrdinalIgnoreCase) Then problems.Add($"{f.LocalPath} SHA-256 不符")
            ElseIf Not String.IsNullOrEmpty(f.GitOid) Then
                Dim h = Await FileDownloader.ComputeGitBlobSha1Async(full, cancel).ConfigureAwait(False)
                If Not String.Equals(h, f.GitOid, StringComparison.OrdinalIgnoreCase) Then problems.Add($"{f.LocalPath} Git 雜湊不符")
            End If
        Next
        Return problems
    End Function

    ''' <summary>記錄最近一次載入驗證結果。</summary>
    Public Sub RecordValidation(id As String, text As String)
        Dim st = LoadState(id)
        If st Is Nothing Then Return
        st.LastValidation = text
        SaveState(st)
    End Sub

    ''' <summary>刪除模型檔與狀態（清單保留）。</summary>
    Public Sub Delete(id As String)
        Dim m = GetManifest(id)
        Dim dir = ModelDirectory(m)
        If Directory.Exists(dir) Then Directory.Delete(dir, True)
        Dim sp = StatePath(id)
        If File.Exists(sp) Then File.Delete(sp)
        AppLog.Info($"已刪除模型 {id}。")
    End Sub
End Class
