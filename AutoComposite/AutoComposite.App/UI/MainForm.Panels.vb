Imports AutoComposite

' 主視窗右側的設定分頁：建立控制項、把控制項的值寫回選取的圖層、以及從圖層更新控制項。
Partial Friend NotInheritable Class MainForm
    Private _rightTabs As TabControl
    Private _placementTab As TabPage

    ' 圖層
    Private ReadOnly _layersList As New ListBox() With {.Dock = DockStyle.Top, .Height = 130, .IntegralHeight = False}
    Private ReadOnly _chkVisible As New CheckBox() With {.Text = "顯示", .AutoSize = True}
    Private ReadOnly _chkLocked As New CheckBox() With {.Text = "鎖定", .AutoSize = True}
    Private ReadOnly _opacity As New ParamSlider("不透明度", 0, 1, 2, 1)
    Private ReadOnly _anchorKind As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}

    ' 去背
    Private ReadOnly _cutoutModel As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}
    Private ReadOnly _providerCombo As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}
    Private ReadOnly _cutoutResize As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}
    Private ReadOnly _refBlack As New ParamSlider("黑點", 0, 0.45, 2, 0.04, "低於此值視為背景（清掉模型留下的淡淡殘影）")
    Private ReadOnly _refWhite As New ParamSlider("白點", 0.55, 1, 2, 0.96, "高於此值視為完全前景")
    Private ReadOnly _refGrow As New ParamSlider("內縮／外擴", -10, 10, 0, 0, "負值內縮、正值外擴（像素）")
    Private ReadOnly _refFeather As New ParamSlider("邊緣羽化", 0, 6, 1, 0.6, "邊緣柔化（像素）")
    Private ReadOnly _refSpecks As New CheckBox() With {.Text = "移除孤立碎塊", .Checked = True, .Dock = DockStyle.Top}
    Private ReadOnly _maskInfo As New Label() With {.Dock = DockStyle.Top, .Height = 40}

    ' 定位
    Private ReadOnly _targetInfo As New Label() With {.Dock = DockStyle.Top, .Height = 54}
    Private ReadOnly _toolTarget As New CheckBox() With {.Text = "點選目標錨點", .Appearance = Appearance.Button, .Width = 110, .Height = 28}
    Private ReadOnly _toolSurface As New CheckBox() With {.Text = "拖曳站立面", .Appearance = Appearance.Button, .Width = 100, .Height = 28}
    Private ReadOnly _toolGuide As New CheckBox() With {.Text = "畫參考線", .Appearance = Appearance.Button, .Width = 90, .Height = 28}
    Private ReadOnly _targetHeight As New ParamSlider("目標高度 px", 0, 10000, 0, 0, "0 = 自動估算")
    Private ReadOnly _targetFraction As New ParamSlider("占畫面高", 0, 1, 2, 0, "人物高度占畫面高度的比例；0 = 自動")
    Private ReadOnly _suggestions As New ListBox() With {.Dock = DockStyle.Top, .Height = 70, .IntegralHeight = False}
    Private ReadOnly _candidates As New ListBox() With {.Dock = DockStyle.Fill, .IntegralHeight = False, .HorizontalScrollbar = True}

    ' 變換
    Private ReadOnly _tx As New ParamSlider("X", 0, 1000, 1)
    Private ReadOnly _ty As New ParamSlider("Y", 0, 1000, 1)
    Private ReadOnly _tScale As New ParamSlider("縮放", 0.01, 5, 3, 1)
    Private ReadOnly _tRotation As New ParamSlider("旋轉", -180, 180, 1)
    Private ReadOnly _tScaleX As New ParamSlider("水平比例", 0.7, 1.3, 2, 1, "自由縮放（選配）：只在必要時微調，避免改變人物比例")
    Private ReadOnly _tFlip As New CheckBox() With {.Text = "水平翻轉", .Dock = DockStyle.Top}
    Private ReadOnly _perspMode As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}
    Private ReadOnly _keystone As New ParamSlider("仰角收斂", TransformService.MinKeystone, TransformService.MaxKeystone, 2, 1, "上緣寬 ÷ 下緣寬；低角度仰拍時小於 1")
    Private ReadOnly _vertical As New ParamSlider("垂直壓縮", TransformService.MinVertical, TransformService.MaxVertical, 2, 1)
    Private ReadOnly _corners As New List(Of ParamSlider)()

    ' 色彩
    Private ReadOnly _colorMode As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}
    Private ReadOnly _blendStrength As New ParamSlider("自動強度", 0, 1, 2, 0.6, "自動融合時往背景靠攏的程度")
    Private ReadOnly _cBright As New ParamSlider("亮度", -0.5, 0.5, 3)
    Private ReadOnly _cContrast As New ParamSlider("對比", 0.5, 1.5, 3, 1)
    Private ReadOnly _cSat As New ParamSlider("飽和度", 0, 2, 3, 1)
    Private ReadOnly _cTemp As New ParamSlider("色溫", -1, 1, 3)
    Private ReadOnly _cTint As New ParamSlider("色調", -1, 1, 3)
    Private ReadOnly _cGainR As New ParamSlider("R 增益", 0.5, 1.5, 3, 1)
    Private ReadOnly _cGainG As New ParamSlider("G 增益", 0.5, 1.5, 3, 1)
    Private ReadOnly _cGainB As New ParamSlider("B 增益", 0.5, 1.5, 3, 1)
    Private ReadOnly _cGamma As New ParamSlider("Gamma", 0.5, 2, 3, 1)
    Private ReadOnly _cStrength As New ParamSlider("混合強度", 0, 1, 2, 1)
    Private ReadOnly _cBlur As New ParamSlider("景深模糊", 0, 10, 1, 0, "背景比人物模糊時，讓人物也稍微柔化（背景像素）")

    ' 陰影
    Private ReadOnly _sEnabled As New CheckBox() With {.Text = "產生陰影", .Dock = DockStyle.Top, .Checked = True}
    Private ReadOnly _sDir As New ParamSlider("方向", -180, 180, 0, 30, "0 = 向右，90 = 朝鏡頭，-90 = 遠離鏡頭")
    Private ReadOnly _sLen As New ParamSlider("投影長度", 0, 2, 2, 0.5)
    Private ReadOnly _sFlat As New ParamSlider("地面壓扁", 0.1, 1, 2, 0.35)
    Private ReadOnly _sOffX As New ParamSlider("偏移 X", -200, 200, 0)
    Private ReadOnly _sOffY As New ParamSlider("偏移 Y", -200, 200, 0)
    Private ReadOnly _sBlur As New ParamSlider("模糊", 0, 60, 1, 6)
    Private ReadOnly _sOpacity As New ParamSlider("不透明度", 0, 1, 2, 0.28)
    Private ReadOnly _sScale As New ParamSlider("陰影縮放", 0.2, 3, 2, 1)
    Private ReadOnly _sContact As New ParamSlider("接觸陰影", 0, 1, 2, 0.55)
    Private ReadOnly _sContactSoft As New ParamSlider("接觸柔和", 0.05, 1, 2, 0.25)
    Private ReadOnly _sAo As New ParamSlider("環境遮蔽", 0, 1, 2, 0.25)
    Private ReadOnly _sFalloff As New ParamSlider("距離衰減", 0, 1, 2, 0.55)
    Private ReadOnly _sColor As New Button() With {.Text = "陰影顏色…", .Dock = DockStyle.Top, .Height = 28}
    Private ReadOnly _sReceiver As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}

    ' 遮擋
    Private ReadOnly _maskKind As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}
    Private ReadOnly _toolPaint As New CheckBox() With {.Text = "🖌 繪製遮罩（左鍵加、右鍵減）", .Appearance = Appearance.Button, .Dock = DockStyle.Top, .Height = 30}
    Private ReadOnly _brushSize As New ParamSlider("筆刷大小", 2, 200, 0, 24)
    Private ReadOnly _brushSoft As New ParamSlider("筆刷柔邊", 0, 1, 2, 0.5)
    Private ReadOnly _occFeather As New ParamSlider("遮擋羽化", 0, 20, 1, 1.5)
    Private ReadOnly _showMask As New CheckBox() With {.Text = "顯示遮罩疊色", .Dock = DockStyle.Top, .Checked = True}

    ' 輸出／診斷
    Private ReadOnly _jpegQuality As New ParamSlider("JPEG 品質", 50, 100, 0, 95)
    Private ReadOnly _previewSize As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top}
    Private ReadOnly _perfText As New TextBox() With {.Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Both, .Dock = DockStyle.Top, .Height = 200, .WordWrap = False, .Font = New Font(FontFamily.GenericMonospace, 8.5F)}
    Private ReadOnly _logList As New ListBox() With {.Dock = DockStyle.Fill, .HorizontalScrollbar = True, .IntegralHeight = False}

    Private Function BuildRightPanel() As Control
        _rightTabs = New TabControl() With {.Dock = DockStyle.Fill, .Multiline = True}
        _rightTabs.TabPages.Add(BuildLayerTab())
        _rightTabs.TabPages.Add(BuildCutoutTab())
        _placementTab = BuildPlacementTab()
        _rightTabs.TabPages.Add(_placementTab)
        _rightTabs.TabPages.Add(BuildTransformTab())
        _rightTabs.TabPages.Add(BuildColorTab())
        _rightTabs.TabPages.Add(BuildShadowTab())
        _rightTabs.TabPages.Add(BuildOcclusionTab())
        _rightTabs.TabPages.Add(BuildOutputTab())
        AddHandler _rightTabs.SelectedIndexChanged, Sub() If _rightTabs.SelectedTab?.Text = "輸出" Then RefreshDiagnostics()
        Return _rightTabs
    End Function

    ''' <summary>分頁：控制項由上往下排（Dock = Top 需要反向加入）。</summary>
    Private Shared Function NewTab(title As String, ParamArray items As Control()) As TabPage
        Dim page As New TabPage(title) With {.Padding = New Padding(6)}
        Dim host As New Panel() With {.Dock = DockStyle.Fill, .AutoScroll = True}
        For i = items.Length - 1 To 0 Step -1
            Dim c = items(i)
            If c.Dock = DockStyle.None Then c.Dock = DockStyle.Top
            host.Controls.Add(c)
        Next
        page.Controls.Add(host)
        Return page
    End Function

    Private Shared Function Caption(text As String) As Label
        Return New Label() With {.Text = text, .Dock = DockStyle.Top, .Height = 24, .TextAlign = ContentAlignment.BottomLeft, .Font = New Font(SystemFonts.MessageBoxFont, FontStyle.Bold)}
    End Function

    Private Shared Function Hint(text As String, Optional height As Integer = 40) As Label
        Return New Label() With {.Text = text, .Dock = DockStyle.Top, .Height = height, .ForeColor = SystemColors.GrayText}
    End Function

    Private Shared Function ButtonRow(ParamArray buttons As Control()) As FlowLayoutPanel
        Dim f As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 36, .WrapContents = False, .AutoScroll = False}
        For Each b In buttons
            f.Controls.Add(b)
        Next
        Return f
    End Function

    Private Function Btn(text As String, action As Action, Optional width As Integer = 0) As Button
        Dim b As New Button() With {.Text = text, .AutoSize = width = 0, .Height = 28}
        If width > 0 Then b.Width = width
        AddHandler b.Click, Sub() action()
        Return b
    End Function

    ' ---------------- 圖層 ----------------

    Private Function BuildLayerTab() As TabPage
        _anchorKind.Items.AddRange({"兩腳之間（自動）", "單腳接觸點（自訂）", "底部中心", "人物重心", "頭部", "自訂點"})
        AddHandler _layersList.SelectedIndexChanged, Sub()
                                                         If _suppressUi OrElse _layersList.SelectedIndex < 0 Then Return
                                                         Dim idx = _wf.Scene.Layers.Count - 1 - _layersList.SelectedIndex
                                                         _wf.SelectedLayerId = _wf.Scene.Layers(idx).Id
                                                         RefreshAll()
                                                     End Sub
        AddHandler _chkVisible.CheckedChanged, Sub() EditLayer("顯示／隱藏", Sub(l) l.Visible = _chkVisible.Checked, True)
        AddHandler _chkLocked.CheckedChanged, Sub() EditLayer("鎖定", Sub(l) l.Locked = _chkLocked.Checked, True)
        BindSlider(_opacity, "不透明度", Sub(l, v) l.Opacity = v)
        AddHandler _anchorKind.SelectedIndexChanged, Sub()
                                                         If _suppressUi Then Return
                                                         Dim l = _wf.SelectedLayer
                                                         If l Is Nothing Then Return
                                                         l.AnchorKind = CType(_anchorKind.SelectedIndex, AnchorKind)
                                                         If l.AnchorKind = AnchorKind.SingleFoot OrElse l.AnchorKind = AnchorKind.Custom Then
                                                             SetStatus("請按左側「編輯遮罩／指定人物錨點」，在人物圖上點選錨點。")
                                                         Else
                                                             _wf.ResetAnchor(l)
                                                         End If
                                                         _wf.CommitEdit("變更人物錨點")
                                                         RequestRender(False)
                                                         _canvas.Invalidate()
                                                     End Sub
        Return NewTab("圖層",
            Caption("人物圖層（上面的在最上層）"), _layersList,
            ButtonRow(_chkVisible, _chkLocked,
                      Btn("上移", Sub() MoveSelected(1)), Btn("下移", Sub() MoveSelected(-1)),
                      Btn("刪除", AddressOf DeleteSelected)),
            ButtonRow(Btn("替換人物圖…", AddressOf ReplaceSelected), Btn("再加入一個人物…", AddressOf OpenFigure)),
            _opacity,
            Caption("人物錨點（與背景目標對齊的點）"), _anchorKind,
            Hint("選「單腳接觸點」或「自訂點」後，在「編輯遮罩／指定人物錨點」視窗中點選。", 44))
    End Function

    Private Sub MoveSelected(delta As Integer)
        Dim l = _wf.SelectedLayer
        If l Is Nothing Then Return
        _wf.MoveLayer(l, delta)
        RefreshAll()
    End Sub

    Private Sub DeleteSelected()
        Dim l = _wf.SelectedLayer
        If l Is Nothing Then Return
        If MessageBox.Show(Me, $"刪除圖層「{l.Name}」？", Text, MessageBoxButtons.OKCancel) <> DialogResult.OK Then Return
        _wf.RemoveLayer(l)
        RefreshAll()
    End Sub

    Private Sub ReplaceSelected()
        Dim l = _wf.SelectedLayer
        If l Is Nothing Then Return
        Dim p = PickImage("替換人物圖片")
        If p Is Nothing Then Return
        Try
            _wf.ReplaceFigureSource(l, p)
        Catch ex As Exception When TypeOf ex Is NotSupportedException OrElse TypeOf ex Is IO.IOException
            MessageBox.Show(Me, ex.Message, "無法載入", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
        RefreshAll()
    End Sub

    ' ---------------- 去背 ----------------

    Private Function BuildCutoutTab() As TabPage
        _providerCombo.Items.AddRange({"自動（DirectML → CPU）", "DirectML（顯示卡）", "CPU"})
        _providerCombo.SelectedIndex = CInt(_settings.Provider)
        AddHandler _providerCombo.SelectedIndexChanged, Sub()
                                                            _settings.Provider = CType(_providerCombo.SelectedIndex, ExecutionPreference)
                                                            _settings.Save()
                                                            _services.UnloadModels()
                                                        End Sub
        _cutoutResize.Items.AddRange({"保留比例補邊（建議）", "拉伸成正方形"})
        _cutoutResize.SelectedIndex = 0
        RefreshCutoutModels()
        AddHandler _cutoutModel.SelectedIndexChanged, Sub()
                                                          Dim id = SelectedModelId()
                                                          If id <> SourceAlphaSegmentation.ModelId Then _settings.DefaultSegmentationModel = id : _settings.Save()
                                                      End Sub
        Return NewTab("去背",
            Caption("AI 去背模型"), _cutoutModel,
            Caption("執行裝置"), _providerCombo,
            Caption("前處理"), _cutoutResize,
            ButtonRow(Btn("✂ 執行自動去背", Sub() RunCutout(), 140), Btn("模型管理…", AddressOf ShowModelManager)),
            _maskInfo,
            Caption("遮罩精修（不二值化，保留髮絲與武器邊緣）"),
            _refBlack, _refWhite, _refGrow, _refFeather, _refSpecks,
            ButtonRow(Btn("套用精修", AddressOf ApplyRefine), Btn("還原 AI 遮罩", AddressOf RestoreMask), Btn("編輯遮罩…", AddressOf ShowMaskEditor)),
            Hint("提示：預覽切到「Alpha 遮罩」或「去背人物」檢查頭髮、長矛等細節；遺漏的地方用「編輯遮罩」的保留筆刷補回。", 56))
    End Function

    Private Sub RefreshCutoutModels()
        Dim keep = SelectedModelId()
        _cutoutModel.Items.Clear()
        _cutoutModel.Items.Add(New ModelChoice(SourceAlphaSegmentation.ModelId, "使用原圖透明度（透明 PNG）"))
        For Each m In _models.Manifests.Where(Function(x) x.Purpose = "segmentation" AndAlso x.Installable).OrderBy(Function(x) If(x.Id = "birefnet-toonout", 0, 1))
            Dim st = _models.GetStatus(m.Id)
            _cutoutModel.Items.Add(New ModelChoice(m.Id, $"{m.Name}{If(st.IsInstalled, "", "（未安裝）")}"))
        Next
        Dim want = If(String.IsNullOrEmpty(keep), _settings.DefaultSegmentationModel, keep)
        Dim idx = 0
        For i = 0 To _cutoutModel.Items.Count - 1
            If DirectCast(_cutoutModel.Items(i), ModelChoice).Id = want Then idx = i
        Next
        If _cutoutModel.Items.Count > 0 Then _cutoutModel.SelectedIndex = If(String.IsNullOrEmpty(keep) AndAlso idx = 0 AndAlso _cutoutModel.Items.Count > 1, 1, idx)
    End Sub

    Private Function SelectedModelId() As String
        Return If(TryCast(_cutoutModel.SelectedItem, ModelChoice)?.Id, "")
    End Function

    Private Function CurrentRefineOptions() As MaskRefineOptions
        Return New MaskRefineOptions With {.BlackPoint = _refBlack.Value, .WhitePoint = _refWhite.Value, .GrowPixels = CInt(_refGrow.Value),
                                           .Feather = _refFeather.Value, .RemoveSpecksFraction = If(_refSpecks.Checked, 0.0005, 0)}
    End Function

    Private Sub ApplyRefine()
        Dim l = _wf.SelectedLayer
        If l Is Nothing Then Return
        If l.AiAlpha Is Nothing Then
            MessageBox.Show(Me, "這個人物還沒有 AI 遮罩，請先執行自動去背。", Text) : Return
        End If
        _wf.ReRefine(l, CurrentRefineOptions())
        RefreshAll()
    End Sub

    Private Sub RestoreMask()
        Dim l = _wf.SelectedLayer
        If l Is Nothing OrElse l.AiAlpha Is Nothing Then Return
        _wf.RestoreAiMask(l)
        RefreshAll()
    End Sub

    ' ---------------- 定位 ----------------

    Private Function BuildPlacementTab() As TabPage
        AddHandler _toolTarget.CheckedChanged, Sub() ToolToggled(_toolTarget, CanvasTool.SetTarget)
        AddHandler _toolSurface.CheckedChanged, Sub() ToolToggled(_toolSurface, CanvasTool.SetSurface)
        AddHandler _toolGuide.CheckedChanged, Sub() ToolToggled(_toolGuide, CanvasTool.DrawGuide)
        AddHandler _targetHeight.Committed, Sub()
                                                _wf.Scene.Target.TargetHeight = _targetHeight.Value
                                                _wf.CommitEdit("目標高度")
                                            End Sub
        AddHandler _targetFraction.Committed, Sub()
                                                  _wf.Scene.Target.TargetHeightFraction = _targetFraction.Value
                                                  _wf.CommitEdit("目標高度比例")
                                              End Sub
        AddHandler _suggestions.SelectedIndexChanged, Sub()
                                                          If _suppressUi OrElse _suggestions.SelectedIndex < 0 OrElse _wf.SceneAnalysis Is Nothing Then Return
                                                          Dim s = _wf.SceneAnalysis.Suggestions(_suggestions.SelectedIndex)
                                                          Dim t = _wf.Scene.Target
                                                          t.HasAnchor = True : t.AnchorX = s.Anchor.X : t.AnchorY = s.Anchor.Y
                                                          t.SurfaceLeftX = s.SurfaceLeftX : t.SurfaceRightX = s.SurfaceRightX : t.SurfaceY = s.SurfaceY
                                                          _wf.CommitEdit("採用推薦站立點")
                                                          RefreshPlacementPanel()
                                                          _canvas.Invalidate()
                                                      End Sub
        AddHandler _candidates.SelectedIndexChanged, Sub()
                                                         If _suppressUi Then Return
                                                         Dim c = TryCast(_candidates.SelectedItem, CandidateItem)
                                                         Dim l = _wf.SelectedLayer
                                                         If c Is Nothing OrElse l Is Nothing Then Return
                                                         _wf.ApplyPlacement(l, c.Candidate)
                                                         SetStatus(c.Candidate.Description)
                                                         RefreshTransformPanel()
                                                         RequestRender(False)
                                                         _canvas.Invalidate()
                                                     End Sub
        Dim tools As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 36, .WrapContents = False}
        tools.Controls.AddRange({_toolTarget, _toolSurface, _toolGuide})
        Dim candHost As New Panel() With {.Dock = DockStyle.Top, .Height = 220}
        candHost.Controls.Add(_candidates)
        Return NewTab("定位",
            Caption("背景目標"), _targetInfo, tools,
            ButtonRow(Btn("清除目標", AddressOf ClearTarget), Btn("清除參考線", AddressOf ClearGuides), Btn("重新分析背景", AddressOf ReanalyzeBackground)),
            Hint("站在柱頂：點「拖曳站立面」，沿柱頂平台左右邊緣拖一條線。兩條「參考線」沿石柱左右邊緣畫，可推估仰角透視。", 56),
            Caption("推薦站立點（天際線分析，可點選採用）"), _suggestions,
            Caption("人物大小"), _targetHeight, _targetFraction,
            ButtonRow(Btn("📍 自動定位", AddressOf RunAutoPlace, 120)),
            Caption("候選位置（評分高→低，點選套用）"), candHost)
    End Function

    Private Sub ToolToggled(box As CheckBox, tool As CanvasTool)
        If _suppressUi Then Return
        _canvas.Tool = If(box.Checked, tool, CanvasTool.Move)
        UpdateToolButtons()
        UpdateMaskOverlay()
        _canvas.Focus()
    End Sub

    Private Sub UpdateToolButtons()
        _suppressUi = True
        Try
            _toolTarget.Checked = _canvas.Tool = CanvasTool.SetTarget
            _toolSurface.Checked = _canvas.Tool = CanvasTool.SetSurface
            _toolGuide.Checked = _canvas.Tool = CanvasTool.DrawGuide
            _toolPaint.Checked = _canvas.Tool = CanvasTool.PaintMask
        Finally
            _suppressUi = False
        End Try
    End Sub

    Private Sub ClearTarget()
        _wf.Scene.Target = New PlacementTarget With {.TargetHeight = _wf.Scene.Target.TargetHeight, .TargetHeightFraction = _wf.Scene.Target.TargetHeightFraction}
        _wf.CommitEdit("清除目標")
        RefreshPlacementPanel()
        _canvas.Invalidate()
    End Sub

    Private Sub ClearGuides()
        _wf.Scene.Guides.Clear()
        _wf.CommitEdit("清除參考線")
        RefreshPlacementPanel()
        _canvas.Invalidate()
    End Sub

    Private Sub ReanalyzeBackground()
        If _wf.Scene.Background Is Nothing Then Return
        _wf.AnalyzeBackground()
        RefreshPlacementPanel()
        _canvas.Invalidate()
    End Sub

    Private Function CurrentRules() As PlacementRules
        Return New PlacementRules()
    End Function

    ' ---------------- 變換 ----------------

    Private Function BuildTransformTab() As TabPage
        BindSlider(_tx, "移動人物", Sub(l, v) l.Transform.X = v, False)
        BindSlider(_ty, "移動人物", Sub(l, v) l.Transform.Y = v, False)
        BindSlider(_tScale, "縮放人物", Sub(l, v) l.Transform.Scale = Math.Max(0.001, v), False)
        BindSlider(_tRotation, "旋轉人物", Sub(l, v) l.Transform.Rotation = v, False)
        BindSlider(_tScaleX, "自由縮放", Sub(l, v) l.Transform.ScaleXFactor = v, False)
        AddHandler _tFlip.CheckedChanged, Sub() EditLayer("水平翻轉", Sub(l) l.Transform.FlipHorizontal = _tFlip.Checked, True)
        _perspMode.Items.AddRange({"不變形（預設）", "仰角梯形收斂", "四角自由透視"})
        AddHandler _perspMode.SelectedIndexChanged, Sub() EditLayer("透視模式", Sub(l) l.Transform.Perspective.Mode = CType(_perspMode.SelectedIndex, PerspectiveMode), True)
        BindSlider(_keystone, "仰角收斂", Sub(l, v)
                                         l.Transform.Perspective.KeystoneRatio = v
                                         If l.Transform.Perspective.Mode = PerspectiveMode.None Then l.Transform.Perspective.Mode = PerspectiveMode.Keystone
                                     End Sub)
        BindSlider(_vertical, "垂直壓縮", Sub(l, v) l.Transform.Perspective.VerticalFactor = v)
        Dim names = {"左上", "右上", "右下", "左下"}
        Dim cornerControls As New List(Of Control)()
        For i = 0 To 3
            For Each axis In {"X", "Y"}
                Dim ci = i, ax = axis
                Dim s As New ParamSlider($"{names(i)} {axis}", -1000, 1000, 0)
                BindSlider(s, "四角透視", Sub(l, v)
                                          Dim o = l.Transform.Perspective.CornerOffsets
                                          If o Is Nothing OrElse o.Length <> 4 Then o = {New PointD(), New PointD(), New PointD(), New PointD()}
                                          o(ci) = If(ax = "X", New PointD(v, o(ci).Y), New PointD(o(ci).X, v))
                                          l.Transform.Perspective.CornerOffsets = o
                                          l.Transform.Perspective.Mode = PerspectiveMode.Corners
                                      End Sub)
                _corners.Add(s)
                cornerControls.Add(s)
            Next
        Next
        Dim items As New List(Of Control) From {
            Caption("位置（背景原圖像素，錨點所在處）"), _tx, _ty,
            Caption("大小與角度"), _tScale, _tRotation, _tScaleX, _tFlip,
            Hint("畫布上：拖曳移動、Shift+滾輪縮放、Ctrl+滾輪旋轉、方向鍵微調（Shift 加大）、[ ] 旋轉、+ − 縮放。", 44),
            Caption("透視（有足夠依據時才使用）"), _perspMode, _keystone, _vertical,
            ButtonRow(Btn("依參考線計算", AddressOf PerspectiveFromGuides), Btn("重設透視", AddressOf ResetPerspective)),
            Caption("四角自由透視（人物原圖像素位移）")}
        items.AddRange(cornerControls)
        Return NewTab("變換", items.ToArray())
    End Function

    Private Sub PerspectiveFromGuides()
        Dim l = _wf.SelectedLayer
        If l Is Nothing Then Return
        If _wf.Scene.Guides.Count < 2 Then
            MessageBox.Show(Me, "請先在「定位」分頁畫兩條參考線（例如石柱左右邊緣）。", Text) : Return
        End If
        Dim fig = _services.Renderer.GetFigureAnalysis(l)
        Dim k = TransformService.KeystoneFromGuides(_wf.Scene.Guides(0), _wf.Scene.Guides(1), l.Transform.Y, fig.Height * l.Transform.Scale)
        l.Transform.Perspective.Mode = If(Math.Abs(k - 1) > 0.005, PerspectiveMode.Keystone, PerspectiveMode.None)
        l.Transform.Perspective.KeystoneRatio = k
        l.Transform.Rotation = AutoPlacementService.LocalVerticalAngle(_wf.Scene.Guides(0), _wf.Scene.Guides(1))
        _wf.CommitEdit("依參考線計算透視")
        RefreshTransformPanel()
        RequestRender(False)
    End Sub

    Private Sub ResetPerspective()
        EditLayer("重設透視", Sub(l) l.Transform.Perspective = New PerspectiveParams(), True)
        RefreshTransformPanel()
    End Sub

    ' ---------------- 色彩 ----------------

    Private Function BuildColorTab() As TabPage
        _colorMode.Items.AddRange({"原始風格（不改變人物）", "環境匹配（輕度調整）", "自訂調色"})
        AddHandler _colorMode.SelectedIndexChanged, Sub() EditLayer("色彩模式", Sub(l) l.Color.Mode = CType(_colorMode.SelectedIndex, ColorMode), True)
        Dim colorBind = Sub(s As ParamSlider, label As String, apply As Action(Of ColorParams, Double))
                            BindSlider(s, label, Sub(l, v)
                                                     apply(l.Color, v)
                                                     If l.Color.Mode = ColorMode.Original Then
                                                         l.Color.Mode = ColorMode.Custom
                                                         _suppressUi = True : _colorMode.SelectedIndex = 2 : _suppressUi = False
                                                     End If
                                                 End Sub)
                        End Sub
        colorBind(_cBright, "亮度", Sub(c, v) c.Brightness = v)
        colorBind(_cContrast, "對比", Sub(c, v) c.Contrast = v)
        colorBind(_cSat, "飽和度", Sub(c, v) c.Saturation = v)
        colorBind(_cTemp, "色溫", Sub(c, v) c.Temperature = v)
        colorBind(_cTint, "色調", Sub(c, v) c.Tint = v)
        colorBind(_cGainR, "R 增益", Sub(c, v) c.GainR = v)
        colorBind(_cGainG, "G 增益", Sub(c, v) c.GainG = v)
        colorBind(_cGainB, "B 增益", Sub(c, v) c.GainB = v)
        colorBind(_cGamma, "Gamma", Sub(c, v) c.Gamma = v)
        colorBind(_cStrength, "混合強度", Sub(c, v) c.Strength = v)
        colorBind(_cBlur, "景深模糊", Sub(c, v) c.DepthBlur = v)
        Return NewTab("色彩",
            Caption("模式"), _colorMode,
            Hint("原始風格保留動漫線條與配色；環境匹配只把亮度、色溫、對比往放置處的背景輕度靠攏。", 44),
            _blendStrength,
            ButtonRow(Btn("推薦參數（依放置處背景）", AddressOf RecommendColor)),
            Caption("調整"), _cBright, _cContrast, _cSat, _cTemp, _cTint, _cGainR, _cGainG, _cGainB, _cGamma, _cStrength, _cBlur,
            ButtonRow(Btn("重設色彩", Sub() EditLayer("重設色彩", Sub(l) l.Color = New ColorParams(), True))))
    End Function

    Private Sub RecommendColor()
        Dim l = _wf.SelectedLayer
        If l Is Nothing OrElse _wf.Scene.Background Is Nothing Then Return
        Dim region = _wf.PlacementRegion(l)
        Dim rec = _services.ColorMatching.Recommend(_wf.Scene.Background, region, l.Source, l.Alpha, _blendStrength.Value)
        l.Color = rec
        _wf.CommitEdit("推薦色彩")
        RefreshAll()
    End Sub

    ' ---------------- 陰影 ----------------

    Private Function BuildShadowTab() As TabPage
        AddHandler _sEnabled.CheckedChanged, Sub() EditLayer("陰影開關", Sub(l) l.Shadow.Enabled = _sEnabled.Checked, True)
        BindSlider(_sDir, "陰影方向", Sub(l, v) l.Shadow.Direction = v)
        BindSlider(_sLen, "陰影長度", Sub(l, v) l.Shadow.Length = v)
        BindSlider(_sFlat, "地面壓扁", Sub(l, v) l.Shadow.GroundFlatten = v)
        BindSlider(_sOffX, "陰影偏移", Sub(l, v) l.Shadow.OffsetX = v)
        BindSlider(_sOffY, "陰影偏移", Sub(l, v) l.Shadow.OffsetY = v)
        BindSlider(_sBlur, "陰影模糊", Sub(l, v) l.Shadow.BlurRadius = v)
        BindSlider(_sOpacity, "陰影不透明度", Sub(l, v) l.Shadow.Opacity = v)
        BindSlider(_sScale, "陰影縮放", Sub(l, v) l.Shadow.Scale = v)
        BindSlider(_sContact, "接觸陰影", Sub(l, v) l.Shadow.ContactStrength = v)
        BindSlider(_sContactSoft, "接觸柔和", Sub(l, v) l.Shadow.ContactSoftness = v)
        BindSlider(_sAo, "環境遮蔽", Sub(l, v) l.Shadow.AmbientOcclusion = v)
        BindSlider(_sFalloff, "距離衰減", Sub(l, v) l.Shadow.Falloff = v)
        _sReceiver.Items.AddRange({"整張背景", "只落在非天空區域", "自訂接收遮罩（遮擋分頁繪製）"})
        AddHandler _sReceiver.SelectedIndexChanged, Sub() EditLayer("陰影接收", Sub(l) l.Shadow.Receiver = CType(_sReceiver.SelectedIndex, ShadowReceiver), True)
        AddHandler _sColor.Click, Sub()
                                      Dim l = _wf.SelectedLayer
                                      If l Is Nothing Then Return
                                      Using dlg As New ColorDialog With {.Color = Color.FromArgb(l.Shadow.ColorR, l.Shadow.ColorG, l.Shadow.ColorB), .FullOpen = True}
                                          If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                                          EditLayer("陰影顏色", Sub(x)
                                                                x.Shadow.ColorR = dlg.Color.R : x.Shadow.ColorG = dlg.Color.G : x.Shadow.ColorB = dlg.Color.B
                                                            End Sub, True)
                                      End Using
                                  End Sub
        Return NewTab("陰影",
            _sEnabled,
            Hint("光源方向無法從單張照片精準還原：「自動估計」只比較畫面左右亮度做粗略判斷，請依照片實際光線微調。", 44),
            ButtonRow(Btn("自動估計", AddressOf RecommendShadow)),
            Caption("投射陰影"), _sDir, _sLen, _sFlat, _sOffX, _sOffY, _sBlur, _sOpacity, _sScale, _sFalloff, _sColor,
            Caption("接觸與環境遮蔽"), _sContact, _sContactSoft, _sAo,
            Caption("陰影可落在"), _sReceiver)
    End Function

    Private Sub RecommendShadow()
        Dim l = _wf.SelectedLayer
        If l Is Nothing Then Return
        Dim fromSky = l.Shadow.Receiver = ShadowReceiver.NonSky
        Dim sp = _services.Shadow.Recommend(_wf.SceneAnalysis, fromSky)
        sp.BlurRadius = Math.Clamp(_services.Renderer.GetFigureAnalysis(l).Height * l.Transform.Scale * 0.012, 1.5, 24)
        l.Shadow = sp
        _wf.CommitEdit("自動估計陰影")
        RefreshAll()
    End Sub

    ' ---------------- 遮擋 ----------------

    Private Function BuildOcclusionTab() As TabPage
        _maskKind.Items.AddRange({"前景物件（擋在所有人物前面）", "只擋選取的人物", "保護區域（定位時避免遮住）", "陰影接收區域"})
        _maskKind.SelectedIndex = 0
        AddHandler _maskKind.SelectedIndexChanged, Sub() UpdateMaskOverlay()
        AddHandler _toolPaint.CheckedChanged, Sub() ToolToggled(_toolPaint, CanvasTool.PaintMask)
        AddHandler _brushSize.ValueChanged, Sub() _canvas.BrushRadiusScreen = _brushSize.Value
        AddHandler _brushSoft.ValueChanged, Sub() _canvas.BrushSoftness = _brushSoft.Value
        AddHandler _occFeather.ValueChanged, Sub()
                                                 If _suppressUi Then Return
                                                 _wf.Scene.OcclusionFeather = _occFeather.Value
                                                 _wf.Scene.TouchMasks()
                                                 RequestRender(True)
                                             End Sub
        AddHandler _occFeather.Committed, Sub() _wf.CommitEdit("遮擋羽化")
        AddHandler _showMask.CheckedChanged, Sub() UpdateMaskOverlay()
        Return NewTab("遮擋",
            Hint("程式不會假裝能自動判斷前後關係。人物站在柱頭裝飾後面時，用筆刷把裝飾塗成「前景物件」，它就會擋在人物前面。", 56),
            Caption("遮罩種類"), _maskKind, _toolPaint, _brushSize, _brushSoft, _occFeather, _showMask,
            ButtonRow(Btn("清除此遮罩", Sub()
                                        _wf.ClearSceneMask(CurrentMaskKind())
                                        UpdateMaskOverlay()
                                        RequestRender(False)
                                    End Sub)))
    End Function

    Private Function CurrentMaskKind() As CompositeWorkflow.SceneMaskKind
        Select Case _maskKind.SelectedIndex
            Case 1 : Return CompositeWorkflow.SceneMaskKind.LayerOcclusion
            Case 2 : Return CompositeWorkflow.SceneMaskKind.Protect
            Case 3 : Return CompositeWorkflow.SceneMaskKind.ShadowReceiver
            Case Else : Return CompositeWorkflow.SceneMaskKind.Foreground
        End Select
    End Function

    Private Sub UpdateMaskOverlay()
        Dim s = _wf.Scene
        Dim showIt = _showMask.Checked AndAlso (_canvas.Tool = CanvasTool.PaintMask OrElse _rightTabs?.SelectedTab?.Text = "遮擋")
        Dim m As FloatMask = Nothing
        Dim col = Color.FromArgb(120, 255, 40, 40)
        If showIt AndAlso s.Background IsNot Nothing Then
            Select Case CurrentMaskKind()
                Case CompositeWorkflow.SceneMaskKind.Foreground : m = s.ForegroundMask
                Case CompositeWorkflow.SceneMaskKind.LayerOcclusion : m = _wf.SelectedLayer?.OcclusionMask : col = Color.FromArgb(120, 255, 140, 0)
                Case CompositeWorkflow.SceneMaskKind.Protect : m = s.ProtectMask : col = Color.FromArgb(110, 40, 220, 80)
                Case CompositeWorkflow.SceneMaskKind.ShadowReceiver : m = s.ShadowReceiverMask : col = Color.FromArgb(110, 60, 120, 255)
            End Select
        End If
        _canvas.SetMaskOverlay(If(m Is Nothing, Nothing, BitmapConvert.MaskOverlay(m, col, 1600)))
    End Sub

    ' ---------------- 輸出／診斷 ----------------

    Private Function BuildOutputTab() As TabPage
        _jpegQuality.Value = _settings.JpegQuality
        AddHandler _jpegQuality.Committed, Sub()
                                               _settings.JpegQuality = CInt(_jpegQuality.Value)
                                               _settings.Save()
                                           End Sub
        _previewSize.Items.AddRange({"依畫布大小（建議）", "800", "1200", "1600", "2400"})
        _previewSize.SelectedIndex = Math.Max(0, Array.IndexOf({0, 800, 1200, 1600, 2400}, _settings.PreviewMaxSide))
        AddHandler _previewSize.SelectedIndexChanged, Sub()
                                                          _settings.PreviewMaxSide = {0, 800, 1200, 1600, 2400}(_previewSize.SelectedIndex)
                                                          _settings.Save()
                                                          RequestRender(False)
                                                      End Sub
        Dim inpaint As New Button() With {.Text = "AI 局部修補（第二階段）", .Dock = DockStyle.Top, .Height = 30, .Enabled = _services.Inpainting.IsAvailable}
        Dim logHost As New Panel() With {.Dock = DockStyle.Top, .Height = 220}
        logHost.Controls.Add(_logList)
        For Each e In AppLog.Recent().Where(Function(x) x.Level <> LogLevel.Info)
            AppendLog(e)
        Next
        Return NewTab("輸出",
            Caption("匯出"), _jpegQuality,
            Hint("匯出一律以背景原始解析度從原圖重新合成，不會放大預覽圖。PNG 無損；JPEG 適合分享。", 40),
            ButtonRow(Btn("💾 匯出圖片…", AddressOf ExportImage)),
            Caption("預覽解析度"), _previewSize,
            Caption("AI 局部修補"), inpaint, Hint(_services.Inpainting.StatusMessage, 56),
            Caption("效能報告"), _perfText, ButtonRow(Btn("重新整理", AddressOf RefreshDiagnostics), Btn("開啟日誌資料夾", AddressOf OpenLogFolder)),
            Caption("警告與錯誤"), logHost)
    End Function

    Private Sub RefreshDiagnostics()
        _perfText.Text = _wf.Performance.ToText().Replace(vbLf, Environment.NewLine)
    End Sub

    Private Sub AppendLog(e As LogEntry)
        _logList.Items.Insert(0, $"{e.Time:HH:mm:ss} [{e.Level}] {e.Message}")
        While _logList.Items.Count > 300
            _logList.Items.RemoveAt(_logList.Items.Count - 1)
        End While
    End Sub

    Private Sub OpenLogFolder()
        Dim dir = IO.Path.GetDirectoryName(AppLog.FilePath)
        If Not String.IsNullOrEmpty(dir) Then
            IO.Directory.CreateDirectory(dir)
            Process.Start(New ProcessStartInfo(dir) With {.UseShellExecute = True})
        End If
    End Sub

    ' ---------------- 繫結 ----------------

    ''' <summary>滑桿 → 選取圖層：拖曳時即時預覽，放開時記錄復原點。</summary>
    Private Sub BindSlider(s As ParamSlider, label As String, apply As Action(Of CompositeLayer, Double), Optional interactive As Boolean = True)
        AddHandler s.ValueChanged, Sub()
                                       If _suppressUi Then Return
                                       Dim l = _wf.SelectedLayer
                                       If l Is Nothing OrElse l.Locked Then Return
                                       apply(l, s.Value)
                                       RequestRender(interactive)
                                       _canvas.Invalidate()
                                   End Sub
        AddHandler s.Committed, Sub()
                                    If _suppressUi OrElse _wf.SelectedLayer Is Nothing Then Return
                                    _wf.CommitEdit(label)
                                    RequestRender(False)
                                End Sub
    End Sub

    ''' <summary>一般控制項（核取方塊、下拉選單）修改選取圖層。</summary>
    Private Sub EditLayer(label As String, apply As Action(Of CompositeLayer), commit As Boolean)
        If _suppressUi Then Return
        Dim l = _wf.SelectedLayer
        If l Is Nothing Then Return
        apply(l)
        If commit Then _wf.CommitEdit(label)
        RefreshLayerPanels()
        RequestRender(False)
        _canvas.Invalidate()
    End Sub

    ' ---------------- 由場景更新介面 ----------------

    Private Sub RefreshAll()
        _suppressUi = True
        Try
            Dim s = _wf.Scene
            ' 縮圖與資訊
            Dim oldBg = _bgThumb.Image
            _bgThumb.Image = If(s.Background Is Nothing, Nothing, BitmapConvert.ToBitmap(Thumb(s.Background)))
            oldBg?.Dispose()
            _bgInfo.Text = If(s.BackgroundInfo Is Nothing, "尚未載入背景", $"{IO.Path.GetFileName(s.BackgroundPath)}{Environment.NewLine}{s.BackgroundInfo.Describe()}")
            Dim l = _wf.SelectedLayer
            Dim oldFig = _figThumb.Image
            _figThumb.Image = If(l?.Source Is Nothing, Nothing, BitmapConvert.ToBitmap(FigureThumb(l)))
            oldFig?.Dispose()
            _figInfo.Text = If(l?.SourceInfo Is Nothing, "尚未載入人物", $"{IO.Path.GetFileName(l.SourcePath)}{Environment.NewLine}{l.SourceInfo.Describe()}{Environment.NewLine}遮罩：{MaskSourceText(l)}")
            ' 圖層清單（最上層在最上面）
            _layersList.Items.Clear()
            For i = s.Layers.Count - 1 To 0 Step -1
                Dim x = s.Layers(i)
                _layersList.Items.Add($"{If(x.Visible, "👁", "　")}{If(x.Locked, "🔒", "")} {x.Name}")
            Next
            If l IsNot Nothing Then _layersList.SelectedIndex = s.Layers.Count - 1 - s.Layers.IndexOf(l)
        Finally
            _suppressUi = False
        End Try
        RefreshLayerPanels()
        RefreshPlacementPanel()
        UpdateUndoButtons()
        UpdateMaskOverlay()
        RequestRender(False)
        _canvas.Invalidate()
    End Sub

    Private Shared Function Thumb(img As RgbaImage) As RgbaImage
        Dim s As Double
        Return img.Downscale(400, s)
    End Function

    Private Function FigureThumb(l As CompositeLayer) As RgbaImage
        Dim r = _services.Renderer.Render(New CompositeScene With {.Background = RgbaImage.Filled(1, 1, 0, 0, 0), .Layers = New List(Of CompositeLayer) From {l.CloneParameters()}},
                                         New RenderOptions With {.Mode = PreviewMode.CutoutFigure, .MaxSide = 400, .LayerId = l.Id})
        Return r.Image
    End Function

    Private Shared Function MaskSourceText(l As CompositeLayer) As String
        Select Case l.MaskModelId
            Case "" : Return "尚未去背（整張不透明）"
            Case SourceAlphaSegmentation.ModelId : Return "原圖 Alpha"
            Case Else : Return l.MaskModelId & If(String.IsNullOrEmpty(l.MaskModelRevision), "", "@" & l.MaskModelRevision.Substring(0, Math.Min(8, l.MaskModelRevision.Length)))
        End Select
    End Function

    Private Sub RefreshLayerPanels()
        Dim l = _wf.SelectedLayer
        Dim s = _wf.Scene
        _suppressUi = True
        Try
            Dim enabled = l IsNot Nothing
            For Each page As TabPage In _rightTabs.TabPages
                If page.Text = "變換" OrElse page.Text = "色彩" OrElse page.Text = "陰影" Then page.Enabled = enabled
            Next
            If l Is Nothing Then Return
            _chkVisible.Checked = l.Visible
            _chkLocked.Checked = l.Locked
            _opacity.Value = l.Opacity
            _anchorKind.SelectedIndex = CInt(l.AnchorKind)
            _maskInfo.Text = $"目前遮罩：{MaskSourceText(l)}"
            RefreshTransformPanel()
            Dim c = l.Color
            _colorMode.SelectedIndex = CInt(c.Mode)
            _cBright.Value = c.Brightness : _cContrast.Value = c.Contrast : _cSat.Value = c.Saturation
            _cTemp.Value = c.Temperature : _cTint.Value = c.Tint
            _cGainR.Value = c.GainR : _cGainG.Value = c.GainG : _cGainB.Value = c.GainB
            _cGamma.Value = c.Gamma : _cStrength.Value = c.Strength : _cBlur.Value = c.DepthBlur
            Dim sh = l.Shadow
            _sEnabled.Checked = sh.Enabled
            _sDir.Value = sh.Direction : _sLen.Value = sh.Length : _sFlat.Value = sh.GroundFlatten
            _sOffX.Value = sh.OffsetX : _sOffY.Value = sh.OffsetY : _sBlur.Value = sh.BlurRadius
            _sOpacity.Value = sh.Opacity : _sScale.Value = sh.Scale : _sContact.Value = sh.ContactStrength
            _sContactSoft.Value = sh.ContactSoftness : _sAo.Value = sh.AmbientOcclusion : _sFalloff.Value = sh.Falloff
            _sReceiver.SelectedIndex = CInt(sh.Receiver)
            _occFeather.Value = s.OcclusionFeather
        Finally
            _suppressUi = False
        End Try
    End Sub

    Private Sub RefreshTransformPanel()
        Dim l = _wf.SelectedLayer
        If l Is Nothing Then Return
        Dim wasSuppressed = _suppressUi
        _suppressUi = True
        Try
            Dim s = _wf.Scene
            If s.Background IsNot Nothing Then
                _tx.SetRange(-s.Width, s.Width * 2)
                _ty.SetRange(-s.Height, s.Height * 2)
            End If
            Dim t = l.Transform
            _tx.Value = t.X : _ty.Value = t.Y
            _tScale.SetRange(0.01, Math.Max(5, t.Scale * 2))
            _tScale.Value = t.Scale : _tRotation.Value = t.Rotation : _tScaleX.Value = t.ScaleXFactor
            _tFlip.Checked = t.FlipHorizontal
            _perspMode.SelectedIndex = CInt(t.Perspective.Mode)
            _keystone.Value = t.Perspective.KeystoneRatio
            _vertical.Value = t.Perspective.VerticalFactor
            Dim o = If(t.Perspective.CornerOffsets, {New PointD(), New PointD(), New PointD(), New PointD()})
            For i = 0 To Math.Min(3, o.Length - 1)
                _corners(i * 2).Value = o(i).X
                _corners(i * 2 + 1).Value = o(i).Y
            Next
        Finally
            _suppressUi = wasSuppressed
        End Try
    End Sub

    Private Sub RefreshPlacementPanel()
        Dim s = _wf.Scene
        Dim t = s.Target
        Dim lines As New List(Of String)()
        lines.Add(If(t.HasAnchor, $"目標錨點：({t.AnchorX:0}, {t.AnchorY:0})", "目標錨點：未指定（使用推薦站立點）"))
        If t.SurfaceWidth > 0 Then lines.Add($"站立面：x {t.SurfaceLeftX:0}～{t.SurfaceRightX:0}，寬 {t.SurfaceWidth:0} px")
        lines.Add($"參考線：{s.Guides.Count} 條" & If(s.Guides.Count >= 2, "（會推估旋轉與仰角透視）", ""))
        _targetInfo.Text = String.Join(Environment.NewLine, lines)
        _suppressUi = True
        Try
            _targetHeight.SetRange(0, Math.Max(100, s.Height * 2))
            _targetHeight.Value = t.TargetHeight
            _targetFraction.Value = t.TargetHeightFraction
            _suggestions.Items.Clear()
            If _wf.SceneAnalysis IsNot Nothing Then
                For Each sug In _wf.SceneAnalysis.Suggestions
                    _suggestions.Items.Add($"{sug.Score:0.00}　({sug.Anchor.X:0}, {sug.Anchor.Y:0})　{sug.Reason}")
                Next
            End If
        Finally
            _suppressUi = False
        End Try
    End Sub
End Class

Friend NotInheritable Class ModelChoice
    Public ReadOnly Property Id As String
    Public ReadOnly Property Label As String

    Public Sub New(id As String, label As String)
        Me.Id = id
        Me.Label = label
    End Sub

    Public Overrides Function ToString() As String
        Return Label
    End Function
End Class
