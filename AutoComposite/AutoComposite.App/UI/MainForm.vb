Imports System.IO
Imports System.Threading
Imports AutoComposite

''' <summary>
''' 主視窗：左側素材、中央合成畫布、右側圖層／去背／定位／變換／色彩／陰影／遮擋／輸出設定、底部進度與狀態。
''' 介面只負責收集操作與顯示結果；去背、定位、合成等計算都交給 <see cref="CompositeWorkflow"/> 與核心服務。
''' </summary>
Partial Friend NotInheritable Class MainForm
    Inherits Form

    Private ReadOnly _settings As AppSettings = AppSettings.Load()
    Private ReadOnly _codec As New WicImageCodec()
    Private ReadOnly _models As ModelManager
    Private ReadOnly _services As CompositeServices
    Private ReadOnly _wf As CompositeWorkflow
    Private ReadOnly _scheduler As RenderScheduler

    Private ReadOnly _toolbar As New ToolStrip()
    Private ReadOnly _status As New StatusStrip()
    Private ReadOnly _statusText As New ToolStripStatusLabel() With {.Spring = True, .TextAlign = ContentAlignment.MiddleLeft}
    Private ReadOnly _progress As New ToolStripProgressBar() With {.Width = 160, .Visible = False}
    Private ReadOnly _cancelButton As New ToolStripButton("取消") With {.Visible = False}
    Private ReadOnly _cursorText As New ToolStripStatusLabel() With {.Width = 150}
    Private ReadOnly _zoomText As New ToolStripStatusLabel() With {.Width = 90}
    Private ReadOnly _canvas As New CompositeCanvas() With {.Dock = DockStyle.Fill}
    Private ReadOnly _previewMode As New ToolStripComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 120}
    Private ReadOnly _undoButton As New ToolStripButton("↶ 復原")
    Private ReadOnly _redoButton As New ToolStripButton("↷ 重做")

    Private ReadOnly _bgThumb As New PictureBox() With {.Dock = DockStyle.Top, .Height = 150, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(40, 40, 44)}
    Private ReadOnly _figThumb As New PictureBox() With {.Dock = DockStyle.Top, .Height = 200, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(40, 40, 44)}
    Private ReadOnly _bgInfo As New Label() With {.Dock = DockStyle.Top, .Height = 48, .Text = "尚未載入背景"}
    Private ReadOnly _figInfo As New Label() With {.Dock = DockStyle.Top, .Height = 64, .Text = "尚未載入人物"}

    Private _busyCts As CancellationTokenSource
    Private _busy As Boolean
    Private _suppressUi As Boolean
    Private ReadOnly _busyControls As New List(Of ToolStripItem)()

    Public Sub New(args As String())
        Text = "AI Auto Composite Studio"
        Width = 1500 : Height = 940
        StartPosition = FormStartPosition.CenterScreen
        KeyPreview = True
        Font = New Font("Microsoft JhengHei UI", 9)

        Dim shipped = Path.Combine(AppContext.BaseDirectory, "ModelManifests")
        _models = New ModelManager(ModelManager.DefaultRoot(), Nothing, shipped)
        _services = CompositeServices.Create(_codec, _models)
        _wf = New CompositeWorkflow(_services)
        _scheduler = New RenderScheduler(Me, _services.Renderer)
        _canvas.Attach(_wf)

        BuildToolbar()
        BuildStatus()
        BuildLayout()
        WireEvents()
        RefreshAll()
        If args IsNot Nothing AndAlso args.Length > 0 AndAlso File.Exists(args(0)) AndAlso args(0).EndsWith(ProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase) Then
            AddHandler Shown, Sub() OpenProjectFile(args(0))
        End If
    End Sub

    ' ---------------- 版面 ----------------

    Private Sub BuildToolbar()
        _toolbar.ImageScalingSize = New Size(20, 20)
        _toolbar.GripStyle = ToolStripGripStyle.Hidden
        AddToolButton("新專案", "清空目前的合成", AddressOf NewProject)
        AddToolButton("開啟專案", "開啟 .acsp.json 合成專案", AddressOf OpenProject)
        AddToolButton("儲存專案", "儲存合成參數與遮罩（Ctrl+S）", AddressOf SaveProject)
        _toolbar.Items.Add(New ToolStripSeparator())
        AddToolButton("🖼 開啟背景", "載入背景照片（保留原始解析度）", AddressOf OpenBackground)
        AddToolButton("🧍 開啟人物", "載入要合成的人物（透明 PNG 或一般圖片）", AddressOf OpenFigure)
        _toolbar.Items.Add(New ToolStripSeparator())
        AddToolButton("✂ 自動去背", "以 AI 模型分離人物與背景", Sub() RunCutout())
        AddToolButton("📍 自動定位", "依錨點、站立面與透視推薦擺放位置", AddressOf RunAutoPlace)
        AddToolButton("🎨 自動融合", "依放置處的背景匹配色彩並產生接觸陰影", AddressOf RunAutoBlend)
        _toolbar.Items.Add(New ToolStripSeparator())
        _undoButton.ToolTipText = "復原（Ctrl+Z）"
        _redoButton.ToolTipText = "重做（Ctrl+Y）"
        AddHandler _undoButton.Click, Sub() DoUndo()
        AddHandler _redoButton.Click, Sub() DoRedo()
        _toolbar.Items.Add(_undoButton)
        _toolbar.Items.Add(_redoButton)
        _busyControls.Add(_undoButton) : _busyControls.Add(_redoButton)
        AddToolButton("重設", "重設選取人物的變換、色彩與陰影", AddressOf ResetLayer)
        _toolbar.Items.Add(New ToolStripSeparator())
        AddToolButton("💾 匯出圖片", "以原始解析度輸出 PNG／JPEG", AddressOf ExportImage)
        _toolbar.Items.Add(New ToolStripSeparator())
        _toolbar.Items.Add(New ToolStripLabel("預覽："))
        _previewMode.Items.AddRange({"原始背景", "去背人物", "Alpha 遮罩", "變換結果", "陰影", "最終合成"})
        _previewMode.SelectedIndex = 5
        _toolbar.Items.Add(_previewMode)
        AddToolButton("符合視窗", "縮放到整張可見", Sub() _canvas.FitToWindow(), False)
        AddToolButton("100%", "實際像素", Sub() _canvas.ZoomActual(), False)
        _toolbar.Items.Add(New ToolStripSeparator())
        AddToolButton("模型管理", "下載、驗證與刪除 AI 模型", AddressOf ShowModelManager)
        Controls.Add(_toolbar)
    End Sub

    Private Function AddToolButton(text As String, tip As String, action As Action, Optional busyLock As Boolean = True) As ToolStripButton
        Dim b As New ToolStripButton(text) With {.ToolTipText = tip}
        AddHandler b.Click, Sub() action()
        _toolbar.Items.Add(b)
        If busyLock Then _busyControls.Add(b)
        Return b
    End Function

    Private Sub BuildStatus()
        AddHandler _cancelButton.Click, Sub() _busyCts?.Cancel()
        _status.Items.AddRange({CType(_statusText, ToolStripItem), _progress, _cancelButton, _cursorText, _zoomText})
        _statusText.Text = "就緒"
        Controls.Add(_status)
    End Sub

    Private Sub BuildLayout()
        Dim split As New SplitContainer() With {.Dock = DockStyle.Fill, .FixedPanel = FixedPanel.Panel1}
        Dim inner As New SplitContainer() With {.Dock = DockStyle.Fill, .FixedPanel = FixedPanel.Panel2}
        Controls.Add(split)
        split.BringToFront()
        split.Panel2.Controls.Add(inner)

        ' 左側素材
        Dim left As New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(6), .AutoScroll = True}
        Dim bgTitle As New Label() With {.Text = "背景照片", .Dock = DockStyle.Top, .Height = 22, .Font = New Font(Font, FontStyle.Bold)}
        Dim figTitle As New Label() With {.Text = "人物（選取的圖層）", .Dock = DockStyle.Top, .Height = 22, .Font = New Font(Font, FontStyle.Bold)}
        Dim editMask As New Button() With {.Text = "編輯遮罩／指定人物錨點…", .Dock = DockStyle.Top, .Height = 30}
        AddHandler editMask.Click, Sub() ShowMaskEditor()
        left.Controls.Add(editMask)
        left.Controls.Add(_figInfo)
        left.Controls.Add(_figThumb)
        left.Controls.Add(figTitle)
        left.Controls.Add(New Label() With {.Dock = DockStyle.Top, .Height = 10})
        left.Controls.Add(_bgInfo)
        left.Controls.Add(_bgThumb)
        left.Controls.Add(bgTitle)
        split.Panel1.Controls.Add(left)

        inner.Panel1.Controls.Add(_canvas)
        inner.Panel2.Controls.Add(BuildRightPanel())

        AddHandler Load, Sub()
                             split.SplitterDistance = 240
                             inner.SplitterDistance = Math.Max(300, inner.Width - 390)
                         End Sub
    End Sub

    Private Sub WireEvents()
        AddHandler _previewMode.SelectedIndexChanged, Sub() RequestRender(False)
        AddHandler _canvas.RenderRequested, Sub(interactive) RequestRender(interactive)
        AddHandler _canvas.LayerEdited, Sub(label, committed)
                                            RefreshTransformPanel()
                                            If committed Then _wf.CommitEdit(label)
                                        End Sub
        AddHandler _canvas.LayerSelected, Sub(id) RefreshAll()
        AddHandler _canvas.CursorMoved, Sub(p) _cursorText.Text = $"x {p.X:0}  y {p.Y:0}"
        AddHandler _canvas.TargetPicked, AddressOf OnTargetPicked
        AddHandler _canvas.SurfacePicked, AddressOf OnSurfacePicked
        AddHandler _canvas.GuidePicked, AddressOf OnGuidePicked
        AddHandler _canvas.StrokeCompleted, AddressOf OnMaskStroke
        AddHandler _scheduler.Rendered, Sub(img, r)
                                            _canvas.SetPreview(BitmapConvert.ToBitmap(img))
                                            _zoomText.Text = $"{_canvas.Zoom:P0}"
                                            Dim total As Double
                                            If r.Timings.TryGetValue("total", total) Then _wf.Performance.Add("預覽", total, $"{img.Width}×{img.Height}")
                                        End Sub
        AddHandler _scheduler.Failed, Sub(ex) SetStatus("預覽失敗：" & ex.Message)
        AddHandler _wf.History.Changed, Sub() UpdateUndoButtons()
        AddHandler AppLog.EntryAdded, Sub(e) If e.Level <> LogLevel.Info Then BeginInvokeSafe(Sub() AppendLog(e))
    End Sub

    Private Sub BeginInvokeSafe(a As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(a)
        Catch ex As InvalidOperationException
        End Try
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Control Or Keys.Z : DoUndo() : Return True
            Case Keys.Control Or Keys.Y, Keys.Control Or Keys.Shift Or Keys.Z : DoRedo() : Return True
            Case Keys.Control Or Keys.S : SaveProject() : Return True
            Case Keys.Control Or Keys.O : OpenProject() : Return True
            Case Keys.Control Or Keys.E : ExportImage() : Return True
            Case Keys.Escape : _canvas.Tool = CanvasTool.Move : UpdateToolButtons() : Return True
        End Select
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    ' ---------------- 預覽 ----------------

    Private Sub RequestRender(interactive As Boolean)
        If _wf.Scene.Background Is Nothing Then
            _canvas.SetPreview(Nothing)
            Return
        End If
        Dim maxSide = If(_settings.PreviewMaxSide > 0, _settings.PreviewMaxSide, Math.Clamp(_canvas.DisplayedLongSide, 512, 2400))
        If interactive Then maxSide = Math.Min(maxSide, 1000)
        _scheduler.Request(_wf.Scene, New RenderOptions With {
            .MaxSide = maxSide, .Quality = If(interactive, ResampleQuality.Bilinear, ResampleQuality.Bicubic),
            .Mode = CType(_previewMode.SelectedIndex, PreviewMode), .LayerId = _wf.SelectedLayerId, .IncludeShadows = True})
    End Sub

    ' ---------------- 長時間工作 ----------------

    ''' <summary>在背景執行工作：顯示進度、可取消、期間停用會改變場景的按鈕；錯誤以對話框顯示並寫入日誌。</summary>
    Private Async Function RunBusyAsync(label As String, work As Func(Of CancellationToken, IProgress(Of String), Task)) As Task(Of Boolean)
        If _busy Then
            SetStatus("請等目前的工作完成，或按「取消」。")
            Return False
        End If
        _busy = True
        _busyCts = New CancellationTokenSource()
        For Each c In _busyControls
            c.Enabled = False
        Next
        _progress.Visible = True : _progress.Style = ProgressBarStyle.Marquee
        _cancelButton.Visible = True
        SetStatus(label & "…")
        Dim progress As New Progress(Of String)(Sub(m) SetStatus($"{label}：{m}"))
        Try
            Await work(_busyCts.Token, progress)
            SetStatus(label & "完成")
            Return True
        Catch ex As OperationCanceledException
            SetStatus(label & "已取消")
            Return False
        Catch ex As Exception
            AppLog.Error(label & "失敗", ex)
            SetStatus(label & "失敗：" & ex.Message)
            MessageBox.Show(Me, ex.Message, label & "失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        Finally
            _busy = False
            _busyCts.Dispose()
            _busyCts = Nothing
            For Each c In _busyControls
                c.Enabled = True
            Next
            _progress.Visible = False
            _cancelButton.Visible = False
            UpdateUndoButtons()
        End Try
    End Function

    Private Sub SetStatus(text As String)
        _statusText.Text = text
    End Sub

    ' ---------------- 檔案 ----------------

    Private Function PickImage(title As String) As String
        Using dlg As New OpenFileDialog With {.Title = title, .Filter = WicImageCodec.OpenFileFilter, .InitialDirectory = _settings.LastImageFolder}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return Nothing
            _settings.LastImageFolder = Path.GetDirectoryName(dlg.FileName)
            _settings.Save()
            Return dlg.FileName
        End Using
    End Function

    Private Async Sub OpenBackground()
        Dim p = PickImage("開啟背景照片")
        If p Is Nothing Then Return
        Dim info As ImageInfo = Nothing
        If Await RunBusyAsync("載入背景", Async Function(ct, pr)
                                               info = Await Task.Run(Function() _wf.LoadBackground(p), ct)
                                           End Function) Then
            _canvas.RequestFit()
            RefreshAll()
            SetStatus($"背景：{info.Describe()}")
        End If
    End Sub

    Private Async Sub OpenFigure()
        If _wf.Scene.Background Is Nothing Then
            MessageBox.Show(Me, "請先開啟背景照片。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim p = PickImage("開啟人物圖片")
        If p Is Nothing Then Return
        Dim layer As CompositeLayer = Nothing
        If Await RunBusyAsync("載入人物", Async Function(ct, pr)
                                               layer = Await Task.Run(Function() _wf.AddFigure(p), ct)
                                           End Function) Then
            RefreshAll()
            If layer.MaskModelId = SourceAlphaSegmentation.ModelId Then
                SetStatus($"人物已有透明背景，直接使用原圖 Alpha。{layer.SourceInfo.Describe()}")
            Else
                SetStatus($"人物：{layer.SourceInfo.Describe()}。下一步：自動去背。")
            End If
        End If
    End Sub

    Private Sub NewProject()
        If MessageBox.Show(Me, "清空目前的合成？未儲存的修改會遺失。", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        _wf.NewProject()
        _canvas.SetPreview(Nothing)
        RefreshAll()
    End Sub

    Private Sub SaveProject()
        If _wf.Scene.Background Is Nothing Then Return
        Dim path = _wf.ProjectPath
        If String.IsNullOrEmpty(path) Then
            Using dlg As New SaveFileDialog With {.Title = "儲存合成專案", .Filter = "合成專案|*" & ProjectSerializer.Extension, .InitialDirectory = _settings.LastProjectFolder,
                                                  .FileName = IO.Path.GetFileNameWithoutExtension(_wf.Scene.BackgroundPath) & "_合成" & ProjectSerializer.Extension}
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                path = dlg.FileName
                If Not path.EndsWith(ProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase) Then path = IO.Path.ChangeExtension(path, Nothing) & ProjectSerializer.Extension
            End Using
        End If
        Try
            _wf.SaveProject(path)
            _settings.LastProjectFolder = IO.Path.GetDirectoryName(path)
            _settings.Save()
            SetStatus("已儲存 " & path)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppLog.Error("儲存專案失敗", ex)
            MessageBox.Show(Me, ex.Message, "儲存失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub OpenProject()
        Using dlg As New OpenFileDialog With {.Title = "開啟合成專案", .Filter = "合成專案|*" & ProjectSerializer.Extension & "|JSON|*.json", .InitialDirectory = _settings.LastProjectFolder}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            OpenProjectFile(dlg.FileName)
        End Using
    End Sub

    Private Async Sub OpenProjectFile(path As String)
        Dim pending As CompositeScene = Nothing
        Dim missing As List(Of MissingFile)
        Try
            missing = _wf.OpenProject(path, pending)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is Text.Json.JsonException OrElse TypeOf ex Is InvalidDataException
            MessageBox.Show(Me, ex.Message, "無法開啟專案", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        ' 遺失的檔案讓使用者重新指定位置。
        For Each m In missing
            Dim r = MessageBox.Show(Me, $"找不到{m.Description}：{Environment.NewLine}{m.Path}{Environment.NewLine}{Environment.NewLine}要重新指定檔案位置嗎？（選「否」略過）",
                                    "檔案遺失", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning)
            If r = DialogResult.Cancel Then Return
            If r = DialogResult.No Then
                If m.Kind = "background" OrElse m.Kind = "layer-source" Then
                    MessageBox.Show(Me, "背景與人物原圖是必要檔案，無法略過。", Text) : Return
                End If
                ProjectSerializer.Relocate(pending, m, "")
                Continue For
            End If
            Using dlg As New OpenFileDialog With {.Title = $"重新指定{m.Description}", .FileName = IO.Path.GetFileName(m.Path),
                                                  .Filter = If(m.Kind = "mask", "PNG|*.png", WicImageCodec.OpenFileFilter)}
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                ProjectSerializer.Relocate(pending, m, dlg.FileName)
            End Using
        Next
        Dim warnings As List(Of String) = Nothing
        If Await RunBusyAsync("開啟專案", Async Function(ct, pr)
                                               warnings = Await Task.Run(Function() _wf.FinishLoadProject(pending, path), ct)
                                           End Function) Then
            _canvas.RequestFit()
            RefreshAll()
            If warnings.Count > 0 Then MessageBox.Show(Me, String.Join(Environment.NewLine, warnings), "開啟專案的提醒", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    Private Async Sub ExportImage()
        If _wf.Scene.Background Is Nothing Then Return
        Dim fmt As ExportFormat, path As String
        Using dlg As New SaveFileDialog With {.Title = "匯出合成圖片", .Filter = "PNG（無損）|*.png|JPEG|*.jpg", .InitialDirectory = _settings.LastImageFolder,
                                              .FileName = IO.Path.GetFileNameWithoutExtension(_wf.Scene.BackgroundPath) & "_合成.png"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            path = dlg.FileName
            fmt = If(dlg.FilterIndex = 2 OrElse path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) OrElse path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase), ExportFormat.Jpeg, ExportFormat.Png)
        End Using
        Dim rep As ExportReport = Nothing
        If Await RunBusyAsync("匯出圖片", Async Function(ct, pr)
                                               pr.Report($"以原始解析度 {_wf.Scene.Width}×{_wf.Scene.Height} 合成中")
                                               Dim snapshot = _wf.Scene.CloneForRender()
                                               rep = Await Task.Run(Function() _services.Export.Export(snapshot, path, fmt, _settings.JpegQuality, _codec, ct), ct)
                                               _wf.Performance.Add("正式輸出", rep.Milliseconds, $"{rep.Width}×{rep.Height}")
                                           End Function) Then
            SetStatus($"已匯出 {path}（{rep.Width}×{rep.Height}，{rep.Milliseconds / 1000:0.0} 秒）")
            RefreshDiagnostics()
        End If
    End Sub

    ' ---------------- 自動流程 ----------------

    Private Async Sub RunCutout(Optional modelId As String = Nothing)
        Dim layer = _wf.SelectedLayer
        If layer Is Nothing Then
            MessageBox.Show(Me, "請先開啟人物圖片。", Text) : Return
        End If
        modelId = If(modelId, SelectedModelId())
        If modelId = SourceAlphaSegmentation.ModelId Then
            If Not SourceAlphaSegmentation.HasUsableAlpha(layer.Source) Then
                MessageBox.Show(Me, "這張人物圖沒有透明區域，請選擇 AI 去背模型。", Text) : Return
            End If
            layer.AiAlpha = layer.Source.ExtractAlpha()
            _wf.RestoreAiMask(layer)
            layer.MaskModelId = SourceAlphaSegmentation.ModelId
            RefreshAll()
            Return
        End If
        Dim remover As IBackgroundRemovalService = Nothing
        If Not _services.BackgroundRemovers.TryGetValue(modelId, remover) OrElse Not remover.IsAvailable() Then
            Dim name = If(remover?.DisplayName, modelId)
            If MessageBox.Show(Me, $"{name} 尚未安裝。要開啟「模型管理」下載嗎？", "需要模型", MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then ShowModelManager(modelId)
            Return
        End If
        Dim res As SegmentationResult = Nothing
        Dim opts As New SegmentationOptions With {.Preference = _settings.Provider, .Resize = If(_cutoutResize.SelectedIndex = 1, "stretch", "letterbox")}
        If Await RunBusyAsync("自動去背", Async Function(ct, pr)
                                               res = Await _wf.AutoCutoutAsync(layer, modelId, opts, CurrentRefineOptions(), pr, ct)
                                           End Function) Then
            Dim warn = If(res.Warnings.Count > 0, "；" & String.Join("；", res.Warnings.Distinct()), "")
            SetStatus($"去背完成（{res.Provider}，{res.Milliseconds / 1000:0.0} 秒{warn}）。可切換預覽到「Alpha 遮罩」檢查，或用「編輯遮罩」修正。")
            _models.RecordValidation(modelId, TryCast(remover, OnnxBackgroundRemovalService)?.CurrentModel?.Describe())
            RefreshAll()
        End If
    End Sub

    Private Sub RunAutoPlace()
        Dim layer = _wf.SelectedLayer
        If layer Is Nothing OrElse _wf.Scene.Background Is Nothing Then Return
        Try
            Dim cands = _wf.SuggestPlacements(layer, CurrentRules())
            _candidates.Items.Clear()
            For Each c In cands.Take(12)
                _candidates.Items.Add(New CandidateItem(c))
            Next
            If cands.Count > 0 Then
                _wf.ApplyPlacement(layer, cands(0))
                _suppressUi = True
                _candidates.SelectedIndex = 0
                _suppressUi = False
                SetStatus($"已套用推薦位置（評分 {cands(0).Score:0.00}）：{cands(0).Description}")
            End If
            _rightTabs.SelectedTab = _placementTab
            RefreshAll()
        Catch ex As InvalidOperationException
            MessageBox.Show(Me, ex.Message, "自動定位", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Try
    End Sub

    Private Sub RunAutoBlend()
        Dim layer = _wf.SelectedLayer
        If layer Is Nothing OrElse _wf.Scene.Background Is Nothing Then Return
        Dim mode = If(_colorMode.SelectedIndex = 0, ColorMode.EnvironmentMatch, CType(_colorMode.SelectedIndex, ColorMode))
        _wf.AutoBlend(layer, New AutoBlendOptions With {.ColorMode = mode, .Strength = _blendStrength.Value, .EnableShadow = True})
        RefreshAll()
        SetStatus("已依放置處附近的背景調整色溫、亮度、對比，並產生接觸陰影。所有參數都可以在「色彩」「陰影」分頁修改。")
    End Sub

    Private Sub ResetLayer()
        Dim layer = _wf.SelectedLayer
        If layer Is Nothing Then Return
        Dim h = layer.Transform.Clone()
        layer.Color = New ColorParams()
        layer.Shadow = New ShadowParams()
        layer.Opacity = 1
        layer.Transform = New LayerTransform With {.X = h.X, .Y = h.Y, .Scale = h.Scale}
        _wf.CommitEdit("重設人物")
        RefreshAll()
    End Sub

    Private Sub DoUndo()
        If _busy OrElse Not _wf.History.CanUndo Then Return
        Dim label = _wf.History.Undo(_wf.Scene)
        SetStatus("已復原：" & label)
        RefreshAll()
    End Sub

    Private Sub DoRedo()
        If _busy OrElse Not _wf.History.CanRedo Then Return
        Dim label = _wf.History.Redo(_wf.Scene)
        SetStatus("已重做：" & label)
        RefreshAll()
    End Sub

    Private Sub UpdateUndoButtons()
        _undoButton.Enabled = Not _busy AndAlso _wf.History.CanUndo
        _redoButton.Enabled = Not _busy AndAlso _wf.History.CanRedo
        _undoButton.ToolTipText = If(_wf.History.CanUndo, $"復原「{_wf.History.UndoLabel}」（Ctrl+Z）", "復原（Ctrl+Z）")
        _redoButton.ToolTipText = If(_wf.History.CanRedo, $"重做「{_wf.History.RedoLabel}」（Ctrl+Y）", "重做（Ctrl+Y）")
    End Sub

    ' ---------------- 畫布工具回呼 ----------------

    Private Sub OnTargetPicked(p As PointD)
        Dim t = _wf.Scene.Target
        t.HasAnchor = True : t.AnchorX = p.X : t.AnchorY = p.Y
        _wf.CommitEdit("指定目標錨點")
        _canvas.Tool = CanvasTool.Move
        UpdateToolButtons()
        RefreshPlacementPanel()
        SetStatus($"目標錨點 ({p.X:0}, {p.Y:0})。按「自動定位」依此計算擺放。")
        _canvas.Invalidate()
    End Sub

    Private Sub OnSurfacePicked(l As PointD, r As PointD)
        Dim t = _wf.Scene.Target
        t.SurfaceLeftX = l.X : t.SurfaceRightX = r.X : t.SurfaceY = l.Y
        If Not t.HasAnchor Then
            t.HasAnchor = True : t.AnchorX = (l.X + r.X) / 2 : t.AnchorY = l.Y
        End If
        _wf.CommitEdit("指定站立面")
        _canvas.Tool = CanvasTool.Move
        UpdateToolButtons()
        RefreshPlacementPanel()
        _canvas.Invalidate()
    End Sub

    Private Sub OnGuidePicked(g As GuideLine)
        _wf.Scene.Guides.Add(g)
        While _wf.Scene.Guides.Count > 2
            _wf.Scene.Guides.RemoveAt(0)
        End While
        _wf.CommitEdit("畫參考線")
        If _wf.Scene.Guides.Count >= 2 Then
            _canvas.Tool = CanvasTool.Move
            UpdateToolButtons()
            SetStatus("已有兩條參考線：自動定位會依它們推估旋轉與仰角透視。")
        Else
            SetStatus("再畫一條參考線（例如石柱另一側邊緣）。")
        End If
        RefreshPlacementPanel()
        _canvas.Invalidate()
    End Sub

    Private Sub OnMaskStroke(stroke As BrushStroke)
        _wf.PaintSceneMask(CurrentMaskKind(), stroke)
        UpdateMaskOverlay()
        RequestRender(False)
    End Sub

    Private Sub ShowModelManager()
        ShowModelManager(Nothing)
    End Sub

    Private Sub ShowModelManager(focusId As String)
        Using f As New ModelManagerForm(_models, _settings, focusId)
            f.ShowDialog(Me)
        End Using
        _services.UnloadModels()
        RefreshCutoutModels()
    End Sub

    Private Sub ShowMaskEditor()
        Dim layer = _wf.SelectedLayer
        If layer Is Nothing Then Return
        Using f As New MaskEditorForm(_wf, layer)
            f.ShowDialog(Me)
        End Using
        RefreshAll()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _busyCts?.Cancel()
        _scheduler.Dispose()
        _services.UnloadModels()
        _settings.Save()
        MyBase.OnFormClosing(e)
    End Sub
End Class

''' <summary>候選位置清單項目。</summary>
Friend NotInheritable Class CandidateItem
    Public ReadOnly Property Candidate As PlacementCandidate

    Public Sub New(c As PlacementCandidate)
        Candidate = c
    End Sub

    Public Overrides Function ToString() As String
        Dim pen = Candidate.PenaltySummary()
        Return $"{Candidate.Score:0.00}　×{Candidate.Transform.Scale:0.###}　({Candidate.Transform.X:0}, {Candidate.Transform.Y:0})" & If(pen.Length > 0, "　" & pen, "")
    End Function
End Class
