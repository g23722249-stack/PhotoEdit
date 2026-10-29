Imports PhotoEdit

''' <summary>
''' 右側工作面板：上方固定直方圖，中間是「調整／效果／人像／修補／局部／裝飾／貼圖／文字／去背／繪圖／圖層」分頁（Aqua.TabControl），
''' 下方固定「重設調整／按住看原圖」。每個滑桿一行（名稱、滑桿、數值），各分頁不必捲動。
''' </summary>
Partial Friend Class frmEditor

    Private Const RowHeight As Integer = 34
    ''' <summary>名稱欄寬度；英文名稱比較長（Temperature、Saturation…），放寬一點。</summary>
    Private Shared ReadOnly CaptionWidth As Integer = If(Lang.Current = UiLanguage.English, 100, 84)
    Private Const ValueWidth As Integer = 62

    Private ReadOnly _tabs As New Aqua.TabControl()
    Private ReadOnly _healToggle As New CheckBox()
    Private ReadOnly _brushSize As New Aqua.Slider()
    Private ReadOnly _brushSizeLabel As New Label()
    Private ReadOnly _help As New HelpTip()
    Private ReadOnly _panelFont As New Font(Lang.UiFont, 10.5F)

    ''' <summary>
    ''' 在分頁上由上往下排列控制項，並在面板變寬時重新排版：
    ''' 左邊名稱欄寬度固定、右邊數值欄跟著右緣移動、中間區域（滑桿、按鈕）依比例拉寬。
    ''' </summary>
    Private Class PageLayout
        Public ReadOnly Page As Control
        Public ReadOnly Width As Integer
        Public Y As Integer = 10
        Private ReadOnly _design As New Dictionary(Of Control, Rectangle)()
        Private ReadOnly _rightMargin As Integer

        ''' <param name="rightMargin">右邊保留給捲軸的寬度（分頁 20；分頁內的面板 0）。</param>
        Public Sub New(page As Control, width As Integer, Optional rightMargin As Integer = 20)
            Me.Page = page
            Me.Width = width
            _rightMargin = rightMargin
            AddHandler page.Resize, Sub() Relayout()
        End Sub

        Public Sub Add(c As Control)
            _design(c) = c.Bounds
            Page.Controls.Add(c)
        End Sub

        Private Sub Relayout()
            Dim newWidth = Math.Max(Width, Page.ClientSize.Width - _rightMargin)
            Dim leftFixed = 8 + CaptionWidth, rightFixed = Width - ValueWidth
            Dim map = Function(x As Integer) As Integer
                          If x <= leftFixed Then Return x
                          If x >= rightFixed Then Return x + newWidth - Width
                          Return CInt(leftFixed + (x - leftFixed) * (newWidth - ValueWidth - leftFixed) / CDbl(rightFixed - leftFixed))
                      End Function
            Page.SuspendLayout()
            For Each kv In _design
                Dim r = kv.Value
                Dim l = map(r.Left), rt = map(r.Right)
                kv.Key.SetBounds(l, 0, Math.Max(1, rt - l), 0, BoundsSpecified.X Or BoundsSpecified.Width)
            Next
            Page.ResumeLayout()
        End Sub
    End Class


    ''' <summary>分頁標題：英文的完整單字放不下 12 個分頁，改用短名稱（簡體照一般翻譯）。</summary>
    Private Shared Function TabTitle(zh As String) As String
        If Lang.Current = UiLanguage.English Then
            Select Case zh
                Case "調整" : Return "Adj"
                Case "效果" : Return "FX"
                Case "人像" : Return "Face"
                Case "修補" : Return "Fix"
                Case "局部" : Return "Local"
                Case "裝飾" : Return "Deco"
                Case "貼圖" : Return "Stick"
                Case "文字" : Return "Text"
                Case "去背" : Return "Cut"
                Case "繪圖" : Return "Draw"
                Case "圖層" : Return "Layer"
                Case "選取" : Return "Select"
            End Select
        End If
        Return Lang.T(zh)
    End Function
    Private Sub BuildSidePanel()
        _sidePanel.Dock = DockStyle.Right
        _sidePanel.Width = SideWidth
        _sidePanel.MinimumSize = New Size(SideWidth, 0)
        _sidePanel.BackColor = Color.FromArgb(236, 238, 242)
        _sidePanel.Padding = New Padding(6, 6, 6, 4)
        _sidePanel.Font = _panelFont

        ' 上：直方圖（點標題列收起／展開，預設收起，記在設定）
        Dim top As New Panel With {.Dock = DockStyle.Top, .Padding = New Padding(2, 0, 2, 6)}
        _histogramView.Dock = DockStyle.Fill
        _histogramView.Collapsed = Not _appSettings.HistogramExpanded
        top.Height = _histogramView.PreferredHeight + top.Padding.Vertical
        AddHandler _histogramView.CollapsedChanged, Sub()
                                                        top.Height = _histogramView.PreferredHeight + top.Padding.Vertical
                                                        _appSettings.HistogramExpanded = Not _histogramView.Collapsed
                                                        _appSettings.Save()
                                                    End Sub
        top.Controls.Add(_histogramView)

        ' 下：重設與對照（兩顆按鈕平分寬度）
        Dim bottom As New TableLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 46, .ColumnCount = 2, .RowCount = 1,
                                                 .Padding = New Padding(0, 6, 0, 0)}
        bottom.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        bottom.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Dim reset = MakeButton(Lang.T("重設調整"), "btn.resetadj")
        reset.Dock = DockStyle.Fill
        AddHandler reset.Click, Sub() RunCommand("resetadj")
        Dim compare = MakeButton(Lang.T("按住看原圖"), "btn.compare")
        compare.Dock = DockStyle.Fill
        AddHandler compare.MouseDown, Sub() ShowOriginal(True)
        AddHandler compare.MouseUp, Sub() ShowOriginal(False)
        AddHandler compare.MouseLeave, Sub() ShowOriginal(False)
        bottom.Controls.Add(reset, 0, 0)
        bottom.Controls.Add(compare, 1, 0)

        ' 中：分頁
        _tabs.Dock = DockStyle.Fill
        ' 順序：修照片（調整、修補、人像、局部、效果）｜選取、去背｜繪圖、圖層｜文字、貼圖、裝飾
        ' 各分頁的常數（TabRepair、TabSelect…）要跟這裡的順序一致
        BuildAdjustPage(_tabs.AddTab(TabTitle("調整")))      ' 0
        BuildRepairPage(_tabs.AddTab(TabTitle("修補")))      ' 1 TabRepair
        BuildPortraitPage(_tabs.AddTab(TabTitle("人像")))    ' 2 TabPortrait
        BuildLocalPage(_tabs.AddTab(TabTitle("局部")))       ' 3 TabLocal
        BuildEffectsPage(_tabs.AddTab(TabTitle("效果")))     ' 4
        BuildSelectPage(_tabs.AddTab(TabTitle("選取")))      ' 5 TabSelect
        BuildCutoutPage(_tabs.AddTab(TabTitle("去背")))      ' 6 TabCutout
        BuildDrawPage(_tabs.AddTab(TabTitle("繪圖")))        ' 7 TabDraw
        BuildLayersPage(_tabs.AddTab(TabTitle("圖層")))      ' 8 TabLayers
        BuildTextPage(_tabs.AddTab(TabTitle("文字")))        ' 9 TabText
        BuildStickerPage(_tabs.AddTab(TabTitle("貼圖")))     ' 10 TabSticker
        BuildDecorPage(_tabs.AddTab(TabTitle("裝飾")))       ' 11 TabDecor
        If Lang.Current = UiLanguage.English Then FitButtonTexts(_tabs)
        _tabs.SelectedIndex = 0
        AddHandler _tabs.SelectedIndexChanged, Sub() OnSideTabChanged()
        _help.SetDynamicHelp(_tabs, Function(p) HelpTexts.Get("tab." & _tabs.TabIndexAt(p)))
        _help.SetHelp("histogram", _histogramView)

        ' 停靠順序：最後加入的最先停靠。
        _sidePanel.Controls.Add(_tabs)
        _sidePanel.Controls.Add(bottom)
        _sidePanel.Controls.Add(top)
    End Sub

    Private ReadOnly _appSettings As AppSettings = AppSettings.Load()

    ''' <summary>照片與右側面板之間的分隔線：拖曳可調整比例，寬度會記住。</summary>
    Private Sub SetupSplitter()
        _sidePanel.Width = Math.Max(SideWidth, _appSettings.SidePanelWidth)
        _splitter.Dock = DockStyle.Right
        _splitter.Width = 6
        _splitter.MinSize = SideWidth
        _splitter.MinExtra = 480
        _splitter.BackColor = Color.FromArgb(200, 205, 214)
        _help.SetHelp("splitter", _splitter)
        AddHandler _splitter.SplitterMoved, Sub()
                                                _appSettings.SidePanelWidth = _sidePanel.Width
                                                _appSettings.Save()
                                            End Sub
    End Sub

    ''' <summary>設計寬度 = 面板最小寬度扣掉邊距；分頁先設成這個寬度，之後變寬時由 PageLayout 重新排版。</summary>
    Private Function NewLayout(page As Aqua.TabPage, Optional autoScroll As Boolean = True) As PageLayout
        page.AutoScroll = autoScroll ' 螢幕太矮時的備援；一般解析度不會出現捲軸
        Dim designWidth = SideWidth - 12 - 26
        page.Size = New Size(designWidth + 20, 600)
        Return New PageLayout(page, designWidth)
    End Function

    ' ---- 調整頁的濾鏡區塊（原本在主視窗下方） ----
    Private ReadOnly _filterFold As New Button With {.FlatStyle = FlatStyle.Flat}
    Private ReadOnly _filterHint As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
    Private _filterPage As Control
    Private _filterBottom As Integer

    ''' <summary>調整頁：上方濾鏡（可以收起），下方手動調整；同人像頁「一鍵美顏在上、手動調整在下」的排法。</summary>
    Private Sub BuildAdjustPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        _filterPage = page
        ' 濾鏡（標題右邊的收起／展開）
        AddHeading(L, Lang.T("濾鏡"), reserveRight:=78)
        _filterFold.SetBounds(L.Width - 74, L.Y - 31, 74, 24)
        _filterFold.Cursor = Cursors.Hand
        _help.SetHelp("filters.fold", _filterFold)
        AddHandler _filterFold.Click, Sub() SetFiltersCollapsed(Not _appSettings.FiltersCollapsed)
        L.Add(_filterFold)
        _presetStrip.SetBounds(8, L.Y, L.Width - 8, _presetStrip.Height)
        L.Add(_presetStrip)
        L.Y += _presetStrip.Height + 2
        _filterHint.SetBounds(8, L.Y, L.Width - 10, 20)
        L.Add(_filterHint)
        L.Y += 24
        _filterBottom = L.Y
        AddHandler _presetStrip.CategoryChanged, Sub() UpdateFilterHint()
        UpdateFilterHint()
        ' 手動調整
        AddHeading(L, Lang.T("手動調整"))
        AddButtonPair(L, Lang.T("自動增強"), "btn.auto", Sub() RunCommand("auto"),
                         Lang.T("自動白平衡"), "btn.autowb", Sub() RunCommand("autowb"))
        Dim signed As Func(Of Integer, String) = Function(v) If(v > 0, "+" & v, v.ToString())
        AddSubHeading(L, Lang.T("光線與色彩"))
        AddRow(L, New SliderRow With {.Key = "exposure", .Caption = Lang.T("曝光"), .Minimum = -30, .Maximum = 30,
            .Format = Function(v) (v / 10.0).ToString("+0.0;-0.0;0.0") & " EV",
            .GetValue = Function(r) CInt(Math.Round(r.Exposure * 10)), .SetValue = Sub(r, v) r.Exposure = v / 10.0})
        AddRow(L, MakeRow("contrast", Lang.T("對比"), -100, 100, signed, Function(r) r.Contrast, Sub(r, v) r.Contrast = v))
        AddRow(L, MakeRow("highlights", Lang.T("亮部"), -100, 100, signed, Function(r) r.Highlights, Sub(r, v) r.Highlights = v))
        AddRow(L, MakeRow("shadows", Lang.T("暗部"), -100, 100, signed, Function(r) r.Shadows, Sub(r, v) r.Shadows = v))
        AddRow(L, MakeRow("temperature", Lang.T("色溫"), -100, 100, signed, Function(r) r.Temperature, Sub(r, v) r.Temperature = v))
        AddRow(L, MakeRow("tint", Lang.TC("tint", "色調"), -100, 100, signed, Function(r) r.Tint, Sub(r, v) r.Tint = v))
        AddRow(L, MakeRow("saturation", Lang.T("飽和度"), -100, 100, signed, Function(r) r.Saturation, Sub(r, v) r.Saturation = v))
        AddSubHeading(L, Lang.T("細節"))
        AddRow(L, MakeRow("sharpness", Lang.T("銳利度"), 0, 100, AddressOf Plain, Function(r) r.Sharpness, Sub(r, v) r.Sharpness = v))
        If _appSettings.FiltersCollapsed Then SetFiltersCollapsed(True, save:=False) Else UpdateFoldText()
    End Sub

    ''' <summary>繪畫、漫畫類濾鏡的筆觸與紙張在「效果」頁籤；其他類提示可以往下微調。</summary>
    Private Sub UpdateFilterHint()
        Dim c = _presetStrip.Category
        _filterHint.Text = If(c = Preset.PaintCategory OrElse c = Preset.ComicCategory,
                              Lang.T("筆觸、紙張在「效果」頁籤細調"), Lang.T("點一下套用，下面的滑桿可以再微調"))
    End Sub

    Private Sub UpdateFoldText()
        _filterFold.Text = If(_appSettings.FiltersCollapsed, Lang.T("展開 ▾"), Lang.T("收起 ▴"))
    End Sub

    ''' <summary>收起／展開濾鏡：下面的控制項跟著上下移（同繪圖分頁的「進階」）。</summary>
    Private Sub SetFiltersCollapsed(collapsed As Boolean, Optional save As Boolean = True)
        Dim wasVisible = _presetStrip.Visible
        If collapsed = Not wasVisible Then
            UpdateFoldText()
            Return
        End If
        Dim top = _presetStrip.Top, delta = _filterBottom - top
        _filterPage.SuspendLayout()
        For Each c As Control In _filterPage.Controls
            ' 收起：原本在濾鏡下面的往上移；展開：現在在濾鏡位置以下的往下移
            Dim from = If(collapsed, _filterBottom, top)
            If c IsNot _presetStrip AndAlso c IsNot _filterHint AndAlso c.Top >= from - 1 Then
                c.Top += If(collapsed, -delta, delta)
            End If
        Next
        _presetStrip.Visible = Not collapsed
        _filterHint.Visible = Not collapsed
        _filterPage.ResumeLayout()
        If save Then
            _appSettings.FiltersCollapsed = collapsed
            _appSettings.Save()
        Else
            _appSettings.FiltersCollapsed = collapsed
        End If
        UpdateFoldText()
    End Sub

    ''' <summary>小標題（區塊裡的分組）：灰字＋細線，比 AddHeading 輕。</summary>
    Private Sub AddSubHeading(L As PageLayout, title As String)
        Dim lbl As New Label With {.Text = title, .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(95, 102, 115)}
        lbl.SetBounds(8, L.Y + 2, L.Width - 8, 18)
        L.Add(lbl)
        Dim line As New Label With {.BackColor = Color.FromArgb(214, 220, 230), .AutoSize = False}
        line.SetBounds(8, L.Y + 21, L.Width - 8, 1)
        L.Add(line)
        L.Y += 26
    End Sub

    Private Sub BuildEffectsPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        Dim signed As Func(Of Integer, String) = Function(v) If(v > 0, "+" & v, v.ToString())
        AddHeading(L, Lang.T("效果"))
        AddRow(L, MakeRow("vignette", Lang.T("暗角"), -100, 100, signed, Function(r) r.Vignette, Sub(r, v) r.Vignette = v))
        AddRow(L, MakeRow("fade", Lang.T("褪色"), 0, 100, AddressOf Plain, Function(r) r.Fade, Sub(r, v) r.Fade = v))
        AddRow(L, MakeRow("grain", Lang.T("顆粒"), 0, 100, AddressOf Plain, Function(r) r.Grain, Sub(r, v) r.Grain = v))
        AddHeading(L, Lang.T("色彩濾鏡"))
        AddRow(L, MakeRow("toninghue", Lang.T("色相"), 0, 359, Function(v) v & "°", Function(r) r.ToningHue, Sub(r, v) r.ToningHue = v))
        AddRow(L, MakeRow("toningstrength", Lang.T("強度"), 0, 100, AddressOf Plain, Function(r) r.ToningStrength, Sub(r, v) r.ToningStrength = v))
        AddHint(L, Lang.T("例如：飽和度 -100 + 色相 35° = 褐色老照片。") & vbCrLf & Lang.T("「調整」頁籤上方的濾鏡可以一次套用整組風格。"))
        BuildArtSection(L)
        BuildPaperSection(L)
    End Sub

    Private Sub BuildRepairPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        AddHeading(L, Lang.T("污點／雜物移除"))
        _healToggle.Appearance = Appearance.Button
        _healToggle.Text = Lang.T("修補筆刷 (H)")
        _healToggle.TextAlign = ContentAlignment.MiddleCenter
        _healToggle.SetBounds(8, L.Y, (L.Width - 16) \ 2, 30)
        _help.SetHelp("heal", _healToggle)
        AddHandler _healToggle.CheckedChanged, Sub() SetHealMode(_healToggle.Checked)
        L.Add(_healToggle)
        Dim clear = MakeButton(Lang.T("清除全部修補"), "btn.clearspots")
        clear.SetBounds(8 + (L.Width - 16) \ 2 + 8, L.Y, (L.Width - 16) \ 2, 30)
        AddHandler clear.Click, Sub() RunCommand("clearspots")
        L.Add(clear)
        L.Y += 38

        ' 筆刷大小是工具設定，不存進配方。
        _help.SetHelp("brushsize", AddCaption(L, Lang.T("筆刷大小"), L.Y), _brushSize, _brushSizeLabel)
        _brushSize.Minimum = 4
        _brushSize.Maximum = 120
        _brushSize.Value = 18
        _brushSize.ShowTicks = False
        _brushSize.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - ValueWidth - 8, 24)
        _brushSizeLabel.AutoSize = False
        _brushSizeLabel.BackColor = Color.Transparent
        _brushSizeLabel.TextAlign = ContentAlignment.MiddleRight
        _brushSizeLabel.SetBounds(L.Width - ValueWidth, L.Y + 2, ValueWidth, 24)
        _brushSizeLabel.Text = _brushSize.Value & " px"
        AddHandler _brushSize.ValueChanged, Sub()
                                                _brushSizeLabel.Text = _brushSize.Value & " px"
                                                _canvas.BrushRadius = _brushSize.Value
                                            End Sub
        L.Add(_brushSize)
        L.Add(_brushSizeLabel)
        L.Y += RowHeight
        AddHint(L, Lang.T("[ ] 鍵調整筆刷大小，右鍵拖曳可平移。"))

        AddHeading(L, Lang.T("降噪"))
        AddRow(L, MakeRow("denoise", Lang.T("明度"), 0, 100, AddressOf Plain, Function(r) r.Denoise, Sub(r, v) r.Denoise = v))
        AddRow(L, MakeRow("colornoise", Lang.T("色彩"), 0, 100, AddressOf Plain, Function(r) r.ColorNoise, Sub(r, v) r.ColorNoise = v))
        ' JPEG 區塊雜訊：第一次調整時偵測原圖 8×8 格線的起點（見 frmEditor.Mosaic.vb）
        AddRow(L, MakeRow("deblock", Lang.T("JPEG 區塊"), 0, 100, AddressOf Plain, Function(r) r.Deblock,
                          Sub(r, v)
                              If v > 0 AndAlso r.Deblock = 0 Then
                                  Dim g = JpegGrid()
                                  r.DeblockGridX = g.X : r.DeblockGridY = g.Y
                              End If
                              r.Deblock = v
                          End Sub))

        AddHeading(L, Lang.T("透視校正"))
        Dim signed As Func(Of Integer, String) = Function(v) If(v > 0, "+" & v, v.ToString())
        AddRow(L, MakeRow("perspv", Lang.T("垂直"), -100, 100, signed, Function(r) r.PerspectiveVertical, Sub(r, v) r.PerspectiveVertical = v))
        AddRow(L, MakeRow("persph", Lang.T("水平"), -100, 100, signed, Function(r) r.PerspectiveHorizontal, Sub(r, v) r.PerspectiveHorizontal = v))

        AddHeading(L, Lang.T("拉直與裁切"))
        AddRow(L, New SliderRow With {.Key = "straighten", .Caption = Lang.T("拉直"), .Minimum = -90, .Maximum = 90,
            .Format = Function(v) (v / 2.0).ToString("+0.0;-0.0;0.0") & "°",
            .GetValue = Function(r) CInt(Math.Round(r.Straighten * 2)), .SetValue = Sub(r, v) r.Straighten = v / 2.0})
        AddButtonPair(L, Lang.T("自動拉直"), "btn.autostraighten", Sub() RunCommand("autostraighten"),
                         Lang.T("裁切… (Ctrl+K)"), "btn.crop", Sub() RunCommand("crop"))
    End Sub

    '---------------------------------------------------------------------
    ' 版面小工具
    '---------------------------------------------------------------------

    Private Shared Function Plain(v As Integer) As String
        Return v.ToString()
    End Function

    Private Sub AddHeading(L As PageLayout, title As String, Optional heading As Label = Nothing, Optional reserveRight As Integer = 0)
        heading = If(heading, New Label())
        heading.Text = title
        heading.Font = New Font(_panelFont, FontStyle.Bold)
        heading.ForeColor = Color.FromArgb(40, 70, 120)
        heading.BackColor = Color.Transparent
        heading.AutoSize = False
        heading.SetBounds(6, L.Y + 4, L.Width - 6 - reserveRight, 20) ' reserveRight：右邊留給同一列的按鈕，避免重疊
        L.Add(heading)
        Dim line As New Label With {.BackColor = Color.FromArgb(190, 200, 215), .AutoSize = False}
        line.SetBounds(6, L.Y + 25, L.Width - 6, 1)
        L.Add(line)
        L.Y += 32
    End Sub

    Private Function AddCaption(L As PageLayout, text As String, y As Integer) As Label
        Dim caption As New Label With {.Text = text, .AutoSize = False, .BackColor = Color.Transparent,
                                       .TextAlign = ContentAlignment.MiddleLeft}
        caption.SetBounds(8, y + 2, CaptionWidth, 24)
        L.Add(caption)
        Return caption
    End Function

    ''' <summary>一行：名稱｜滑桿｜數值（數值按兩下歸零）。</summary>
    Private Sub AddRow(L As PageLayout, row As SliderRow)
        Dim caption = AddCaption(L, row.Caption, L.Y)
        row.Slider = New Aqua.Slider With {.Maximum = row.Maximum, .Minimum = row.Minimum, .Value = 0, .ShowTicks = False}
        row.Slider.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - ValueWidth - 8, 24)
        row.ValueLabel = New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight,
                                         .Cursor = Cursors.Hand, .BackColor = Color.Transparent}
        row.ValueLabel.SetBounds(L.Width - ValueWidth, L.Y + 2, ValueWidth, 24)
        ' 說明在 HelpTexts 的「row.鍵」；名稱、滑桿、數值共用，停用與否看滑桿。
        _help.SetHelpLinked("row." & row.Key, row.Slider, caption, row.Slider, row.ValueLabel)

        Dim r = row
        AddHandler r.Slider.ValueChanged, Sub() OnSliderChanged(r)
        AddHandler r.ValueLabel.DoubleClick, Sub()
                                                 Dim zero = Math.Max(r.Minimum, 0)
                                                 If r.Slider.Value <> zero Then r.Slider.Value = zero
                                             End Sub
        L.Add(row.Slider)
        L.Add(row.ValueLabel)
        _rows.Add(row)
        L.Y += RowHeight
    End Sub

    Private Sub AddHint(L As PageLayout, text As String)
        Dim hint As New Label With {.Text = text, .AutoSize = False, .BackColor = Color.Transparent,
                                    .ForeColor = Color.FromArgb(105, 110, 120)}
        hint.Font = _panelFont
        Dim h = TextRenderer.MeasureText(text, _panelFont, New Size(L.Width - 10, 0), TextFormatFlags.WordBreak).Height + 4
        hint.SetBounds(8, L.Y + 2, L.Width - 10, h)
        L.Add(hint)
        L.Y += h + 6
    End Sub

    Private Sub AddButtonPair(L As PageLayout, text1 As String, help1 As String, click1 As Action,
                              text2 As String, help2 As String, click2 As Action)
        Dim w = (L.Width - 16) \ 2
        Dim b1 = MakeButton(text1, help1)
        b1.SetBounds(8, L.Y + 2, w, 30)
        AddHandler b1.Click, Sub() click1()
        Dim b2 = MakeButton(text2, help2)
        b2.SetBounds(8 + w + 8, L.Y + 2, w, 30)
        AddHandler b2.Click, Sub() click2()
        L.Add(b1)
        L.Add(b2)
        L.Y += 40
    End Sub

    ''' <param name="helpKey">HelpTexts 的鍵。</param>
    ''' <summary>英文比中文長：按鈕（含按鈕樣式的選項）放不下時把字縮小（最小 7.5pt），不改按鈕大小與排版。</summary>
    Friend Shared Sub FitButtonTexts(root As Control)
        For Each c As Control In root.Controls
            Dim b = TryCast(c, ButtonBase)
            If b IsNot Nothing AndAlso b.Width > 20 AndAlso Not String.IsNullOrEmpty(b.Text) Then
                Dim plain = (TypeOf b Is CheckBox AndAlso CType(b, CheckBox).Appearance = Appearance.Normal) OrElse
                            (TypeOf b Is RadioButton AndAlso CType(b, RadioButton).Appearance = Appearance.Normal)
                Dim avail = b.Width - If(plain, 22, 18) - If(b.Image IsNot Nothing, b.Image.Width + 4, 0)
                Dim f = b.Font, size = f.Size
                Do While size > 7.5F
                    Using test As New Font(f.FontFamily, size, f.Style)
                        If TextRenderer.MeasureText(b.Text, test).Width <= avail Then Exit Do
                    End Using
                    size -= 0.5F
                Loop
                If size < f.Size Then b.Font = New Font(f.FontFamily, size, f.Style)
            End If
            FitButtonTexts(c)
        Next
    End Sub

    Private Function MakeButton(text As String, helpKey As String) As Button
        Dim b As New Button With {.Text = text, .UseVisualStyleBackColor = True}
        If helpKey IsNot Nothing Then _help.SetHelp(helpKey, b)
        Return b
    End Function

    Private Shared Function MakeRow(key As String, caption As String, min As Integer, max As Integer,
                                    format As Func(Of Integer, String),
                                    getValue As Func(Of EditRecipe, Integer),
                                    setValue As Action(Of EditRecipe, Integer)) As SliderRow
        Return New SliderRow With {.Key = key, .Caption = caption, .Minimum = min, .Maximum = max,
                                   .Format = format, .GetValue = getValue, .SetValue = setValue}
    End Function

    ''' <summary>切換分頁時換畫布工具；離開「修補」分頁會關掉修補筆刷，避免在別的分頁誤塗。</summary>
    Private Sub OnSideTabChanged()
        If _fullScreen AndAlso _tabs.SelectedIndex <> TabDraw Then ExitFullScreen() ' 全螢幕只在繪圖分頁
        If _tabs.SelectedIndex = TabLayers AndAlso _lastTab <> TabLayers Then SyncStackSelectionFromTab(_lastTab)
        _lastTab = _tabs.SelectedIndex
        UpdateToolFromTab()
    End Sub
End Class
