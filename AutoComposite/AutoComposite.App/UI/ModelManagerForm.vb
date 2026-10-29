Imports System.Threading
Imports AutoComposite

''' <summary>
''' 模型管理：列出模型清單與本機狀態（離線可看），顯示來源、鎖定的版本、授權與驗證結果；
''' 下載（進度、取消、續傳）、驗證檔案雜湊、測試載入（顯示輸入輸出、opset、實際執行提供者）、刪除。
''' 下載前先向 Hub 取得實際檔案清單與授權，授權不允許預定用途時拒絕安裝。
''' </summary>
Friend NotInheritable Class ModelManagerForm
    Inherits Form

    Private ReadOnly _models As ModelManager
    Private ReadOnly _settings As AppSettings
    Private ReadOnly _list As New ListView() With {.Dock = DockStyle.Top, .Height = 200, .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False}
    Private ReadOnly _details As New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly _variant As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 220}
    Private ReadOnly _use As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 170}
    Private ReadOnly _progress As New ProgressBar() With {.Dock = DockStyle.Bottom, .Height = 18}
    Private ReadOnly _progressText As New Label() With {.Dock = DockStyle.Bottom, .Height = 22}
    Private ReadOnly _buttons As New List(Of Button)()
    Private _cts As CancellationTokenSource

    Public Sub New(models As ModelManager, settings As AppSettings, focusId As String)
        _models = models
        _settings = settings
        Text = "模型管理"
        Width = 980 : Height = 660
        StartPosition = FormStartPosition.CenterParent
        Font = New Font("Microsoft JhengHei UI", 9)

        _list.Columns.Add("模型", 300)
        _list.Columns.Add("用途", 90)
        _list.Columns.Add("狀態", 110)
        _list.Columns.Add("鎖定版本", 100)
        _list.Columns.Add("授權", 140)
        _list.Columns.Add("大小", 100)
        _use.Items.AddRange({"非商業用途", "商業用途"})
        _use.SelectedIndex = CInt(settings.IntendedUse)
        AddHandler _use.SelectedIndexChanged, Sub()
                                                  _settings.IntendedUse = CType(_use.SelectedIndex, IntendedUse)
                                                  _settings.Save()
                                              End Sub

        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 38, .WrapContents = False}
        Dim lbl = Function(t As String) New Label() With {.Text = t, .AutoSize = True, .Padding = New Padding(0, 8, 0, 0)}
        bar.Controls.AddRange({lbl("版本"), _variant, lbl("預定用途"), _use,
                               MakeButton("下載安裝", AddressOf Install), MakeButton("驗證檔案", AddressOf Verify),
                               MakeButton("測試載入", AddressOf TestLoad), MakeButton("刪除", AddressOf DeleteModel),
                               MakeButton("開啟資料夾", AddressOf OpenFolder)})
        Dim cancel As New Button() With {.Text = "取消下載", .AutoSize = True, .Height = 28}
        AddHandler cancel.Click, Sub() _cts?.Cancel()
        bar.Controls.Add(cancel)

        Controls.Add(_details)
        Controls.Add(bar)
        Controls.Add(_list)
        Controls.Add(_progressText)
        Controls.Add(_progress)
        AddHandler _list.SelectedIndexChanged, Sub() ShowDetails()
        RefreshList(focusId)
        AddHandler FormClosing, Sub(s, e)
                                    If _cts IsNot Nothing Then
                                        If MessageBox.Show(Me, "下載進行中。關閉會取消下載（已下載的部分保留，下次可續傳）。", Text, MessageBoxButtons.OKCancel) <> DialogResult.OK Then
                                            e.Cancel = True
                                        Else
                                            _cts.Cancel()
                                        End If
                                    End If
                                End Sub
    End Sub

    Private Function MakeButton(text As String, action As Func(Of Task)) As Button
        Dim b As New Button() With {.Text = text, .AutoSize = True, .Height = 28}
        AddHandler b.Click, Async Sub() Await action()
        _buttons.Add(b)
        Return b
    End Function

    Private Function MakeButton(text As String, action As Action) As Button
        Return MakeButton(text, Function()
                                    action()
                                    Return Task.CompletedTask
                                End Function)
    End Function

    Private ReadOnly Property Selected As ModelManifest
        Get
            If _list.SelectedItems.Count = 0 Then Return Nothing
            Return DirectCast(_list.SelectedItems(0).Tag, ModelManifest)
        End Get
    End Property

    Private Sub RefreshList(Optional focusId As String = Nothing)
        Dim keep = If(focusId, Selected?.Id)
        _list.Items.Clear()
        For Each m In _models.Manifests.OrderBy(Function(x) If(x.Purpose = "segmentation", 0, 1)).ThenBy(Function(x) x.Name)
            Dim st = _models.GetStatus(m.Id)
            Dim status = If(Not m.Installable, "未整合（說明）", If(st.IsInstalled, "已安裝", If(st.Status = ModelInstallStatus.Broken, "檔案不完整", "未安裝")))
            Dim commit = If(st.State?.Commit, "")
            Dim size = If(st.State IsNot Nothing AndAlso st.State.Files.Count > 0, st.State.Files.Sum(Function(f) f.Size), m.Variants.FirstOrDefault()?.EstimatedBytes)
            Dim it As New ListViewItem({m.Name, m.Purpose, status, If(commit.Length > 8, commit.Substring(0, 8), commit),
                                        If(String.IsNullOrEmpty(st.State?.HubLicense), m.License.Expected & "（預期）", st.State.HubLicense),
                                        If(size.GetValueOrDefault() > 0, $"{size.GetValueOrDefault() / 1048576.0:0} MB", "")}) With {.Tag = m}
            _list.Items.Add(it)
            If m.Id = keep Then it.Selected = True
        Next
        If _list.SelectedItems.Count = 0 AndAlso _list.Items.Count > 0 Then _list.Items(0).Selected = True
        ShowDetails()
    End Sub

    Private Sub ShowDetails()
        Dim m = Selected
        If m Is Nothing Then Return
        Dim st = _models.GetStatus(m.Id)
        _variant.Items.Clear()
        For Each v In m.Variants
            _variant.Items.Add(New ModelChoice(v.Id, If(String.IsNullOrEmpty(v.Label), v.Id, v.Label)))
        Next
        If _variant.Items.Count > 0 Then _variant.SelectedIndex = Math.Max(0, m.Variants.FindIndex(Function(v) v.Id = st.State?.VariantId))
        Dim nl = Environment.NewLine
        Dim sb As New Text.StringBuilder()
        sb.Append($"{m.Name}{nl}{m.Description}{nl}{nl}")
        sb.Append($"來源：{m.SourceUrl}{nl}倉庫：{m.Repo}　清單版本：{m.Revision}{nl}")
        sb.Append($"授權（清單預期）：{m.License.Expected}，商用：{m.License.CommercialUse}　{m.License.Url}{nl}")
        If Not String.IsNullOrEmpty(m.License.Notes) Then sb.Append($"授權備註：{m.License.Notes}{nl}")
        sb.Append($"執行提供者：{String.Join("、", m.Providers.Select(Function(kv) $"{kv.Key} = {kv.Value}"))}{nl}")
        If m.Io.InputShape?.Length > 0 Then sb.Append($"輸入規格：[{String.Join(",", m.Io.InputShape)}] {m.Io.InputType}{nl}")
        sb.Append($"模型資料夾：{_models.ModelDirectory(m)}{nl}{nl}")
        If st.State IsNot Nothing Then
            sb.Append($"已鎖定 commit：{st.State.Commit}（不會自動更新）{nl}")
            sb.Append($"Hub 授權：{st.State.HubLicense}　同意時間：{st.State.LicenseAcceptedUtc}　用途：{st.State.IntendedUse}{nl}")
            For Each f In st.State.Files
                sb.Append($"  {f.Role,-16} {f.LocalPath}　{f.Size / 1048576.0:0.0} MB　{If(String.IsNullOrEmpty(f.Sha256), "Git " & f.GitOid, "SHA-256 " & f.Sha256)}{nl}")
            Next
            If Not String.IsNullOrEmpty(st.State.LastValidation) Then sb.Append($"最近載入驗證：{st.State.LastValidation}{nl}")
            If Not String.IsNullOrEmpty(st.State.LastError) Then sb.Append($"最近錯誤：{st.State.LastError}{nl}")
        End If
        For Each p In st.Problems
            sb.Append($"⚠ {p}{nl}")
        Next
        _details.Text = sb.ToString()
    End Sub

    Private Sub SetBusy(busy As Boolean)
        For Each b In _buttons
            b.Enabled = Not busy
        Next
        _list.Enabled = Not busy
    End Sub

    Private Async Function Install() As Task
        Dim m = Selected
        If m Is Nothing Then Return
        If Not m.Installable Then
            MessageBox.Show(Me, m.Description, "尚未整合", MessageBoxButtons.OK, MessageBoxIcon.Information) : Return
        End If
        Dim use = CType(_use.SelectedIndex, IntendedUse)
        Dim variantId = If(TryCast(_variant.SelectedItem, ModelChoice)?.Id, "")
        _cts = New CancellationTokenSource()
        SetBusy(True)
        Try
            _progressText.Text = "向 Hugging Face Hub 取得檔案清單與授權…"
            Dim plan = Await _models.PlanInstallAsync(m.Id, variantId, use, _cts.Token)
            Dim nl = Environment.NewLine
            Dim files = String.Join(nl, plan.Files.Select(Function(f) $"  {f.File.Path}（{f.File.Size / 1048576.0:0.0} MB）"))
            If Not plan.License.Allowed Then
                MessageBox.Show(Me, plan.License.Message, "授權不允許", MessageBoxButtons.OK, MessageBoxIcon.Stop) : Return
            End If
            Dim msg = $"{m.Name}{nl}commit {plan.Remote.Commit}{nl}{nl}將下載：{nl}{files}{nl}合計 {plan.TotalBytes / 1048576.0:0} MB{nl}{nl}{plan.License.Message}"
            Dim confirmed = False
            If plan.License.RequiresConfirmation Then
                If MessageBox.Show(Me, msg & nl & nl & "我已閱讀授權並確認符合我的用途。", "確認授權", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
                confirmed = True
            ElseIf MessageBox.Show(Me, msg, "下載模型", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) <> DialogResult.OK Then
                Return
            End If
            Dim prog As New Progress(Of DownloadProgress)(
                Sub(p)
                    If p.OverallTotal > 0 Then _progress.Value = CInt(Math.Clamp(p.OverallDone * 1000 \ p.OverallTotal, 0, 1000)) : _progress.Maximum = 1000
                    _progressText.Text = $"({p.FileIndex + 1}/{p.FileCount}) {p.FileName}　{p.OverallDone / 1048576.0:0.0} / {p.OverallTotal / 1048576.0:0.0} MB"
                End Sub)
            Await _models.InstallAsync(plan, use, confirmed, prog, _cts.Token)
            _progressText.Text = "安裝完成，正在測試載入…"
            Await TestLoad()
        Catch ex As OperationCanceledException
            _progressText.Text = "已取消。已下載的部分會保留，下次可續傳。"
        Catch ex As Exception
            AppLog.Error("模型安裝失敗", ex)
            _progressText.Text = "失敗：" & ex.Message
            MessageBox.Show(Me, ex.Message, "安裝失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            _cts?.Dispose()
            _cts = Nothing
            SetBusy(False)
            RefreshList(m.Id)
        End Try
    End Function

    Private Async Function Verify() As Task
        Dim m = Selected
        If m Is Nothing Then Return
        SetBusy(True)
        Try
            _progressText.Text = "計算雜湊中（大檔案需要一點時間）…"
            Dim problems = Await _models.VerifyAsync(m.Id, CancellationToken.None)
            _progressText.Text = If(problems.Count = 0, "驗證通過：所有檔案大小與雜湊正確。", "驗證發現問題：" & String.Join("；", problems))
        Finally
            SetBusy(False)
        End Try
    End Function

    Private Async Function TestLoad() As Task
        Dim m = Selected
        If m Is Nothing Then Return
        Dim path = _models.PrimaryModelPath(m.Id)
        If path Is Nothing Then
            _progressText.Text = "尚未安裝。" : Return
        End If
        SetBusy(True)
        Try
            Dim pref = _settings.Provider
            Dim text = Await Task.Run(Function()
                                          Using lm = New OnnxModelLoader().Load(path, pref, m.Io)
                                              Return lm.Describe()
                                          End Using
                                      End Function)
            _models.RecordValidation(m.Id, text)
            _progressText.Text = "載入成功：" & text
        Catch ex As Exception
            AppLog.Error("模型測試載入失敗", ex)
            _models.RecordValidation(m.Id, "載入失敗：" & ex.Message)
            _progressText.Text = "載入失敗：" & ex.Message
        Finally
            SetBusy(False)
            ShowDetails()
        End Try
    End Function

    Private Sub DeleteModel()
        Dim m = Selected
        If m Is Nothing Then Return
        If MessageBox.Show(Me, $"刪除 {m.Name} 的模型檔？", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        Try
            _models.Delete(m.Id)
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException
            MessageBox.Show(Me, "無法刪除（檔案可能正在使用）：" & ex.Message, Text)
        End Try
        RefreshList(m.Id)
    End Sub

    Private Sub OpenFolder()
        Dim m = Selected
        If m Is Nothing Then Return
        Dim dir = _models.ModelDirectory(m)
        IO.Directory.CreateDirectory(dir)
        Process.Start(New ProcessStartInfo(dir) With {.UseShellExecute = True})
    End Sub
End Class
