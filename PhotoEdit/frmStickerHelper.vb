Imports System.Drawing.Drawing2D
Imports System.IO
Imports PhotoEdit

''' <summary>
''' 貼圖小幫手（獨立視窗）：開啟或貼上一張排了很多貼圖的圖，自動拆成一張張（範圍 = 有顏色的最大範圍），
''' 自動去背（預設「背景色填充」），可以逐張切換去背方式、用魔術棒與保留／擦除筆刷修正，最後存進 stick 資料夾。
''' 左邊原圖上的框：點一下選取、按兩下或點編號排除／加回、拖曳邊緣調整、框內拖曳移動、右鍵拖曳補畫一個框、Del 刪除。
''' </summary>
Friend Class frmStickerHelper
    Inherits Aqua.AquaForm

    Private Enum CutMode
        EdgeFill = 0
        AiGeneral = 1
        AiHuman = 2
        None = 3
    End Enum

    Private NotInheritable Class StickerItem
        Public Rect As Rectangle
        Public Included As Boolean = True
        Public Mode As CutMode = CutMode.EdgeFill
        Public Settings As New CutoutSettings With {.Feather = 5, .Background = CutoutBackground.Transparent, .WandDespeckle = True}
        Public Crop As Bitmap
        Public Result As Bitmap
        Public AiMasks As New Dictionary(Of CutoutModel, Bitmap)()
        Public ReadOnly History As New Stack(Of (Mode As CutMode, Settings As CutoutSettings))()

        Public Sub DisposeImages(Optional keepAi As Boolean = False)
            Crop?.Dispose() : Crop = Nothing
            Result?.Dispose() : Result = Nothing
            If Not keepAi Then
                For Each m In AiMasks.Values
                    m.Dispose()
                Next
                AiMasks.Clear()
            End If
        End Sub
    End Class

    Private ReadOnly _onSaved As Action(Of String)
    Private ReadOnly _help As New HelpTip()
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10.0F)
    Private _source As Bitmap
    Private _sourcePixels As Byte()
    Private _sourceName As String = "貼圖"
    Private _background As Color = Color.White
    Private _manualBackground As Boolean
    Private ReadOnly _items As New List(Of StickerItem)()
    Private _selected As Integer = -1

    ' 上方
    Private ReadOnly _bgCombo As New ComboBox()
    Private ReadOnly _bgSwatch As New Panel()
    Private ReadOnly _merge As New Aqua.Slider()
    Private ReadOnly _minSize As New Aqua.Slider()
    Private ReadOnly _found As New Label()
    ' 中間
    Private ReadOnly _sourceView As New SourceView()
    Private ReadOnly _tiles As New FlowLayoutPanel()
    Private ReadOnly _detail As New PreviewCanvas()
    Private ReadOnly _rightSplit As New SplitContainer()
    Private ReadOnly _modeButtons As New List(Of RadioButton)()
    Private ReadOnly _wandTool As New CheckBox()
    Private ReadOnly _keepTool As New CheckBox()
    Private ReadOnly _eraseTool As New CheckBox()
    Private ReadOnly _showMask As New CheckBox()
    Private ReadOnly _edgeTol As New Aqua.Slider()
    Private ReadOnly _wandTol As New Aqua.Slider()
    Private ReadOnly _brushSize As New Aqua.Slider()
    Private ReadOnly _feather As New Aqua.Slider()
    Private ReadOnly _shift As New Aqua.Slider()
    Private ReadOnly _detailTitle As New Label()
    ' 下方
    Private ReadOnly _themeBox As New ComboBox()
    Private ReadOnly _prefixBox As New TextBox()
    Private ReadOnly _trim As New CheckBox()
    Private ReadOnly _save As New Button()
    Private ReadOnly _status As New StatusLine()
    Private _syncing As Boolean

    Public Sub New(onSaved As Action(Of String))
        _onSaved = onSaved
        Text = "貼圖小幫手"
        Size = New Size(1320, 860)
        MinimumSize = New Size(1000, 680)
        StartPosition = FormStartPosition.CenterParent
        WindowBorderStyle = Aqua.FormBorderStyle.Sizable
        KeyPreview = True
        Font = _font
        Padding = New Padding(4, 25, 4, 16)

        Dim top = BuildTopBar()
        Dim bottom = BuildBottomBar()
        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .SplitterWidth = 6, .BackColor = Color.FromArgb(200, 205, 214)}
        split.Panel1.BackColor = Color.FromArgb(236, 238, 242)
        split.Panel2.BackColor = Color.FromArgb(236, 238, 242)
        BuildSourcePane(split.Panel1)
        BuildRightPane(split.Panel2)
        _status.Dock = DockStyle.Bottom
        Controls.Add(split)
        Controls.Add(bottom)
        Controls.Add(top)
        Controls.Add(_status)
        AddHandler Shown, Sub()
                              split.SplitterDistance = CInt(split.Width * 0.42)
                              _rightSplit.SplitterDistance = CInt(_rightSplit.Height * 0.36)
                          End Sub
        AddHandler FormClosed, Sub()
                                   For Each it In _items
                                       it.DisposeImages()
                                   Next
                                   _source?.Dispose()
                                   _help.Dispose()
                               End Sub
        UpdateDetail()
        _status.Text = "開啟或貼上一張排了很多貼圖的圖片（Ctrl+V）。"
        ' 開啟時直接最大化（還原時用上面的大小）；範圍限制在工作區，見 UpdateMaximizedBounds。
        WindowState = FormWindowState.Maximized
        ThemeManager.Attach(Me)
    End Sub

    ' 最大化：AquaForm 沒有系統邊框，最大化時預設會蓋住工作列，所以限制在目前螢幕的工作區（同 frmEditor）。
    Private Sub UpdateMaximizedBounds()
        Dim scr = Screen.FromControl(Me)
        MaximizedBounds = New Rectangle(scr.WorkingArea.X - scr.Bounds.X, scr.WorkingArea.Y - scr.Bounds.Y,
                                        scr.WorkingArea.Width, scr.WorkingArea.Height)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        UpdateMaximizedBounds()
        MyBase.OnHandleCreated(e)
    End Sub

    Protected Overrides Sub OnLocationChanged(e As EventArgs)
        MyBase.OnLocationChanged(e)
        If WindowState = FormWindowState.Normal AndAlso IsHandleCreated Then UpdateMaximizedBounds()
    End Sub

    '=====================================================================
    ' 版面
    '=====================================================================

    Private Function Bar(height As Integer) As FlowLayoutPanel
        Return New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = height, .WrapContents = False, .Padding = New Padding(6, 6, 6, 0),
                                         .BackColor = Color.FromArgb(230, 236, 244)}
    End Function

    Private Shared Function Caption(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(8, 6, 2, 0)}
    End Function

    Private Function MakeButton(text As String, width As Integer, helpKey As String) As Button
        Dim b As New Button With {.Text = text, .Width = width, .Height = 28, .UseVisualStyleBackColor = True, .Margin = New Padding(3, 0, 3, 0)}
        _help.SetHelp(helpKey, b)
        Return b
    End Function

    Private Shared Sub SetupSlider(s As Aqua.Slider, min As Integer, max As Integer, value As Integer, width As Integer)
        s.Minimum = min : s.Maximum = max : s.Value = value : s.ShowTicks = False
        s.Width = width : s.Height = 24 : s.Margin = New Padding(2, 2, 2, 0)
    End Sub

    Private Function BuildTopBar() As Control
        Dim p = Bar(40)
        Dim open = MakeButton("開啟圖片…", 100, "helper.open")
        Dim paste = MakeButton("貼上圖片 (Ctrl+V)", 150, "helper.paste")
        AddHandler open.Click, Sub() OpenImage()
        AddHandler paste.Click, Sub() PasteImage()
        _bgCombo.DropDownStyle = ComboBoxStyle.DropDownList
        _bgCombo.Items.AddRange({"自動", "指定顏色…"})
        _bgCombo.SelectedIndex = 0
        _bgCombo.Width = 110
        _bgCombo.Margin = New Padding(2, 3, 2, 0)
        AddHandler _bgCombo.SelectedIndexChanged, Sub()
                                                       If _syncing Then Return
                                                       If _bgCombo.SelectedIndex = 1 Then
                                                           Using dlg As New ColorDialog With {.Color = _background, .FullOpen = True}
                                                               If dlg.ShowDialog(Me) = DialogResult.OK Then
                                                                   _background = dlg.Color
                                                                   _manualBackground = True
                                                               Else
                                                                   _syncing = True : _bgCombo.SelectedIndex = If(_manualBackground, 1, 0) : _syncing = False
                                                                   Return
                                                               End If
                                                           End Using
                                                       Else
                                                           _manualBackground = False
                                                       End If
                                                       Resplit()
                                                   End Sub
        Dim bgLabel = Caption("背景")
        _help.SetHelp("helper.background", bgLabel, _bgCombo)
        _bgSwatch.Size = New Size(26, 22)
        _bgSwatch.Margin = New Padding(2, 4, 6, 0)
        _bgSwatch.BorderStyle = BorderStyle.FixedSingle
        _bgSwatch.BackColor = _background
        _help.SetHelp("helper.background", _bgSwatch)
        SetupSlider(_merge, 0, 100, 30, 110)
        SetupSlider(_minSize, 0, 100, 20, 110)
        Dim mergeLabel = Caption("合併距離")
        Dim minLabel = Caption("最小尺寸")
        _help.SetHelpLinked("helper.merge", _merge, mergeLabel, _merge)
        _help.SetHelpLinked("helper.minsize", _minSize, minLabel, _minSize)
        Dim resplitButton = MakeButton("重新拆分", 90, "helper.resplit")
        AddHandler resplitButton.Click, Sub() Resplit()
        _found.AutoSize = True
        _found.Margin = New Padding(12, 6, 2, 0)
        _found.ForeColor = Color.FromArgb(24, 95, 165)
        p.Controls.AddRange(New Control() {open, paste, bgLabel, _bgCombo, _bgSwatch, mergeLabel, _merge, minLabel, _minSize, resplitButton, _found})
        Return p
    End Function

    Private Sub BuildSourcePane(host As Control)
        Dim title As New Label With {.Dock = DockStyle.Top, .Height = 26, .Padding = New Padding(6, 6, 0, 0),
                                     .Text = "原圖：點框選取、按兩下排除／加回、拖曳邊緣調整、框上按右鍵拆開、右鍵拖曳補畫、Del 刪除",
                                     .ForeColor = Color.FromArgb(70, 76, 88), .AutoEllipsis = True}
        _sourceView.Dock = DockStyle.Fill
        AddHandler _sourceView.BoxSelected, Sub(i) SelectItem(i)
        AddHandler _sourceView.BoxToggled, Sub(i) ToggleInclude(i)
        AddHandler _sourceView.BoxChanged, Sub(i, r) ChangeBox(i, r)
        AddHandler _sourceView.BoxAdded, Sub(r) AddBox(r)
        AddHandler _sourceView.BoxMenuRequested, AddressOf ShowBoxMenu
        _help.SetHelp("helper.source", _sourceView)
        host.Controls.Add(_sourceView)
        host.Controls.Add(title)
    End Sub

    Private Sub BuildRightPane(host As Control)
        _rightSplit.Dock = DockStyle.Fill
        _rightSplit.Orientation = Orientation.Horizontal
        _rightSplit.SplitterWidth = 6
        _rightSplit.BackColor = Color.FromArgb(200, 205, 214)
        _rightSplit.Panel1.BackColor = Color.FromArgb(236, 238, 242)
        _rightSplit.Panel2.BackColor = Color.FromArgb(236, 238, 242)

        ' 上：拆分結果
        Dim tilesBar = Bar(36)
        tilesBar.BackColor = Color.FromArgb(236, 238, 242)
        Dim tilesTitle = Caption("拆分結果")
        tilesTitle.Font = New Font(_font, FontStyle.Bold)
        Dim allCut = MakeButton("全部去背", 84, "helper.allcut")
        Dim allRaw = MakeButton("全部原圖", 84, "helper.allraw")
        Dim selAll = MakeButton("全選", 60, "helper.selall")
        Dim selNone = MakeButton("全不選", 70, "helper.selnone")
        AddHandler allCut.Click, Sub() SetAllModes(CutMode.EdgeFill)
        AddHandler allRaw.Click, Sub() SetAllModes(CutMode.None)
        AddHandler selAll.Click, Sub() SetAllIncluded(True)
        AddHandler selNone.Click, Sub() SetAllIncluded(False)
        tilesBar.Controls.AddRange(New Control() {tilesTitle, allCut, allRaw, selAll, selNone})
        _tiles.Dock = DockStyle.Fill
        _tiles.AutoScroll = True
        _tiles.Padding = New Padding(4)
        _tiles.BackColor = Color.FromArgb(250, 250, 252)
        _rightSplit.Panel1.Controls.Add(_tiles)
        _rightSplit.Panel1.Controls.Add(tilesBar)

        ' 下：選取的貼圖（修正）
        _detailTitle.Dock = DockStyle.Top
        _detailTitle.Height = 26
        _detailTitle.Padding = New Padding(6, 6, 0, 0)
        _detailTitle.Font = New Font(_font, FontStyle.Bold)
        Dim modes = Bar(36)
        modes.BackColor = Color.FromArgb(236, 238, 242)
        modes.Controls.Add(Caption("去背方式"))
        Dim modeNames = {"背景色填充", "AI 一般", "AI 人像", "不去背"}
        Dim modeKeys = {"helper.mode.edge", "helper.mode.aigeneral", "helper.mode.aihuman", "helper.mode.none"}
        For i = 0 To 3
            Dim index = i
            Dim rb As New RadioButton With {.Text = modeNames(i), .Appearance = Appearance.Button, .FlatStyle = FlatStyle.Flat, .AutoSize = False, .TextAlign = ContentAlignment.MiddleCenter, .Size = New Size(TextRenderer.MeasureText(modeNames(i), _font).Width + 26, 28),
                                            .Margin = New Padding(2, 0, 2, 0), .BackColor = Color.White, .Padding = New Padding(6, 0, 6, 0)}
            rb.FlatAppearance.CheckedBackColor = Color.FromArgb(210, 228, 250)
            rb.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 195)
            AddHandler rb.CheckedChanged, Sub(s, e)
                                              If _syncing OrElse Not DirectCast(s, RadioButton).Checked Then Return
                                              SetMode(CType(index, CutMode))
                                          End Sub
            _help.SetHelp(modeKeys(i), rb)
            _modeButtons.Add(rb)
            modes.Controls.Add(rb)
        Next
        Dim tools = Bar(36)
        tools.BackColor = Color.FromArgb(236, 238, 242)
        Dim toolDefs = {(_wandTool, "魔術棒 (W)", "helper.wand"), (_keepTool, "保留筆刷", "helper.keep"), (_eraseTool, "擦除筆刷", "helper.erase"), (_showMask, "檢查遮罩", "helper.showmask")}
        For Each td In toolDefs
            Dim cb = td.Item1
            cb.Text = td.Item2
            cb.Appearance = Appearance.Button
            cb.FlatStyle = FlatStyle.Flat
            cb.AutoSize = False
            cb.TextAlign = ContentAlignment.MiddleCenter
            cb.Size = New Size(TextRenderer.MeasureText(td.Item2, _font).Width + 26, 28)
            cb.BackColor = Color.White
            cb.Padding = New Padding(6, 0, 6, 0)
            cb.Margin = New Padding(2, 0, 2, 0)
            cb.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 195)
            cb.FlatAppearance.CheckedBackColor = Color.FromArgb(255, 238, 180)
            _help.SetHelp(td.Item3, cb)
            tools.Controls.Add(cb)
        Next
        AddHandler _wandTool.CheckedChanged, Sub() OnToolToggled(_wandTool)
        AddHandler _keepTool.CheckedChanged, Sub() OnToolToggled(_keepTool)
        AddHandler _eraseTool.CheckedChanged, Sub() OnToolToggled(_eraseTool)
        AddHandler _showMask.CheckedChanged, Sub() RefreshDetailImage()
        Dim undo = MakeButton("復原 (Ctrl+Z)", 110, "helper.undo")
        AddHandler undo.Click, Sub() UndoItem()
        Dim zoom = MakeButton("放大修正", 90, "helper.zoom")
        AddHandler zoom.Click, Sub()
                                   _rightSplit.Panel1Collapsed = Not _rightSplit.Panel1Collapsed
                                   zoom.Text = If(_rightSplit.Panel1Collapsed, "顯示全部", "放大修正")
                               End Sub
        tools.Controls.Add(undo)
        tools.Controls.Add(zoom)

        Dim sliders = Bar(36)
        sliders.BackColor = Color.FromArgb(236, 238, 242)
        SetupSlider(_edgeTol, 0, 100, 15, 90)
        SetupSlider(_wandTol, 0, 100, 25, 90)
        SetupSlider(_brushSize, 2, 80, 14, 80)
        SetupSlider(_feather, 0, 100, 5, 80)
        SetupSlider(_shift, -50, 50, 0, 80)
        Dim sliderDefs = {("背景容許度", _edgeTol, "helper.edgetol"), ("魔術棒", _wandTol, "helper.wandtol"), ("筆刷", _brushSize, "helper.brush"),
                          ("羽化", _feather, "helper.feather"), ("內縮外擴", _shift, "helper.shift")}
        For Each sd In sliderDefs
            Dim cap = Caption(sd.Item1)
            _help.SetHelpLinked(sd.Item3, sd.Item2, cap, sd.Item2)
            sliders.Controls.Add(cap)
            sliders.Controls.Add(sd.Item2)
        Next
        AddHandler _edgeTol.ValueChanged, Sub() ChangeSettings(Sub(s) s.EdgeFillTolerance = _edgeTol.Value, "edge")
        AddHandler _wandTol.ValueChanged, Sub() ChangeSettings(Sub(s)
                                                                   If s.Wand.Count > 0 Then s.Wand(s.Wand.Count - 1).Tolerance = _wandTol.Value
                                                               End Sub, "wand")
        AddHandler _feather.ValueChanged, Sub() ChangeSettings(Sub(s) s.Feather = _feather.Value, "feather")
        AddHandler _shift.ValueChanged, Sub() ChangeSettings(Sub(s) s.Shift = _shift.Value, "shift")
        AddHandler _brushSize.ValueChanged, Sub() _detail.BrushRadius = _brushSize.Value

        _detail.Dock = DockStyle.Fill
        _detail.Checkerboard = True
        AddHandler _detail.WandClicked, AddressOf OnWandClicked
        AddHandler _detail.StrokeCompleted, AddressOf OnStroke
        _rightSplit.Panel2.Controls.Add(_detail)
        _rightSplit.Panel2.Controls.Add(sliders)
        _rightSplit.Panel2.Controls.Add(tools)
        _rightSplit.Panel2.Controls.Add(modes)
        _rightSplit.Panel2.Controls.Add(_detailTitle)
        host.Controls.Add(_rightSplit)
    End Sub

    Private Function BuildBottomBar() As Control
        Dim p = Bar(42)
        p.Dock = DockStyle.Bottom
        _themeBox.DropDownStyle = ComboBoxStyle.DropDown
        _themeBox.Width = 180
        _themeBox.Margin = New Padding(2, 3, 2, 0)
        _themeBox.Items.AddRange(StickerLibrary.Themes().Cast(Of Object)().ToArray())
        Dim themeLabel = Caption("存到主題")
        _help.SetHelp("helper.theme", themeLabel, _themeBox)
        _prefixBox.Width = 150
        _prefixBox.Margin = New Padding(2, 3, 2, 0)
        Dim prefixLabel = Caption("檔名")
        _help.SetHelp("helper.prefix", prefixLabel, _prefixBox)
        _trim.Text = "剪掉透明邊"
        _trim.Checked = True
        _trim.AutoSize = True
        _trim.Margin = New Padding(10, 6, 2, 0)
        _help.SetHelp("helper.trim", _trim)
        _save.Text = "存成貼圖"
        _save.Width = 150
        _save.Height = 30
        _save.Font = New Font(_font, FontStyle.Bold)
        _save.Margin = New Padding(20, 0, 3, 0)
        AddHandler _save.Click, Sub() SaveStickers()
        _help.SetHelp("helper.save", _save)
        Dim closeButton = MakeButton("關閉", 80, "helper.close")
        AddHandler closeButton.Click, Sub() Me.Close()
        p.Controls.AddRange(New Control() {themeLabel, _themeBox, prefixLabel, _prefixBox, _trim, _save, closeButton})
        Return p
    End Function

    '=====================================================================
    ' 取得圖片與拆分
    '=====================================================================

    Private Sub OpenImage()
        Using dlg As New OpenFileDialog With {.Title = "選擇貼圖圖片", .Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有檔案|*.*"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using fs As New FileStream(dlg.FileName, FileMode.Open, FileAccess.Read), img = Image.FromStream(fs)
                    LoadSource(New Bitmap(img), Path.GetFileNameWithoutExtension(dlg.FileName))
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException
                MessageBox.Show(Me, "無法開啟圖片：" & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    Private Sub PasteImage()
        Dim bmp As Bitmap = Nothing
        Try
            Dim data = Clipboard.GetDataObject()
            If data IsNot Nothing Then
                Dim file = frmEditor.ImageFile(data)
                If file IsNot Nothing Then
                    Using fs As New FileStream(file, FileMode.Open, FileAccess.Read), img = Image.FromStream(fs)
                        bmp = New Bitmap(img)
                    End Using
                Else
                    bmp = frmEditor.ImageBitmap(data)
                End If
            End If
        Catch ex As Exception When TypeOf ex Is Runtime.InteropServices.ExternalException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException
            bmp = Nothing
        End Try
        If bmp Is Nothing Then
            _status.Text = "剪貼簿裡沒有圖片。"
            Return
        End If
        LoadSource(bmp, "貼上的貼圖")
    End Sub

    ''' <summary>換一張原圖（bmp 由本視窗保管）。</summary>
    Friend Sub LoadSource(bmp As Bitmap, name As String)
        For Each it In _items
            it.DisposeImages()
        Next
        _items.Clear()
        _source?.Dispose()
        _source = New Bitmap(bmp.Width, bmp.Height, Imaging.PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(_source)
            g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height)
        End Using
        bmp.Dispose()
        _sourcePixels = StickerSplitter.ReadPixels(_source)
        _sourceName = name
        If String.IsNullOrWhiteSpace(_themeBox.Text) Then _themeBox.Text = name
        _prefixBox.Text = name
        _sourceView.Image = _source
        Resplit()
    End Sub

    Private Sub Resplit()
        If _source Is Nothing Then Return
        If _items.Any(Function(it) it.History.Count > 0) AndAlso
           MessageBox.Show(Me, "重新拆分會清除每張貼圖的修正，要繼續嗎？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        If Not _manualBackground Then _background = StickerSplitter.BorderColor(_sourcePixels, _source.Width, _source.Height)
        _bgSwatch.BackColor = If(_background.A = 0, Color.White, _background)
        Dim longSide = Math.Max(_source.Width, _source.Height)
        Dim merge = CInt(_merge.Value / 100.0 * 0.05 * longSide)
        Dim minSize = Math.Max(4, CInt(_minSize.Value / 100.0 * 0.1 * longSide))
        Dim boxes = StickerSplitter.Split(_sourcePixels, _source.Width, _source.Height, _background, merge, minSize)
        For Each it In _items
            it.DisposeImages()
        Next
        _items.Clear()
        For Each b In boxes
            Dim it As New StickerItem With {.Rect = b}
            it.Settings.EdgeFillColorArgb = If(_background.A = 0, 0, _background.ToArgb())
            _items.Add(it)
            Recompose(it)
        Next
        _selected = If(_items.Count > 0, 0, -1)
        RebuildTiles()
        UpdateDetail()
        _status.Text = If(_background.A = 0, "背景：透明。", $"背景：RGB({_background.R}, {_background.G}, {_background.B})。") &
                       $"找到 {_items.Count} 張；點左邊的框或右邊的縮圖可以逐張修正。"
    End Sub

    '=====================================================================
    ' 框的編輯
    '=====================================================================

    Private Sub ChangeBox(index As Integer, r As Rectangle)
        If index < 0 OrElse index >= _items.Count Then Return
        Dim it = _items(index)
        it.Rect = r
        it.DisposeImages()
        Recompose(it)
        If it.Mode = CutMode.AiGeneral OrElse it.Mode = CutMode.AiHuman Then ComputeAi(it, If(it.Mode = CutMode.AiHuman, CutoutModel.Human, CutoutModel.General))
        SelectItem(index)
        RefreshTiles()
    End Sub

    Private Sub AddBox(r As Rectangle)
        If _source Is Nothing Then Return
        r.Intersect(New Rectangle(0, 0, _source.Width, _source.Height))
        If r.Width < 4 OrElse r.Height < 4 Then Return
        Dim it As New StickerItem With {.Rect = r}
        it.Settings.EdgeFillColorArgb = If(_background.A = 0, 0, _background.ToArgb())
        Recompose(it)
        _items.Add(it)
        ' 依閱讀順序重排。
        Dim order = StickerSplitter.ReadingOrder(_items.Select(Function(x) x.Rect)).ToList()
        Dim sorted = order.Select(Function(rc) _items.First(Function(x) x.Rect = rc)).ToList()
        _items.Clear()
        _items.AddRange(sorted)
        _selected = _items.IndexOf(it)
        RebuildTiles()
        UpdateDetail()
        _status.Text = $"已補一個框，共 {_items.Count} 張。"
    End Sub

    ''' <summary>框的右鍵選單：平均拆成幾張（在最空的位置切）、排除／加回、刪除。</summary>
    Private Sub ShowBoxMenu(index As Integer, location As Point)
        If index < 0 OrElse index >= _items.Count Then Return
        SelectItem(index)
        Dim menu As New ContextMenuStrip With {.Font = _font}
        For Each n In {2, 3, 4}
            Dim parts = n
            menu.Items.Add($"左右拆成 {n} 張", Nothing, Sub() SplitBox(index, parts, horizontal:=True))
        Next
        For Each n In {2, 3}
            Dim parts = n
            menu.Items.Add($"上下拆成 {n} 張", Nothing, Sub() SplitBox(index, parts, horizontal:=False))
        Next
        menu.Items.Add(New ToolStripSeparator())
        menu.Items.Add(If(_items(index).Included, "排除這張", "加回這張"), Nothing, Sub() ToggleInclude(index))
        menu.Items.Add("刪除這個框", Nothing, Sub() DeleteSelected())
        AddHandler menu.Closed, Sub() BeginInvoke(Sub() menu.Dispose())
        menu.Show(_sourceView, location)
    End Sub

    ''' <summary>手動拆分一個框：換成 parts 個新框（各自重新去背），依閱讀順序重排。</summary>
    Private Sub SplitBox(index As Integer, parts As Integer, horizontal As Boolean)
        If index < 0 OrElse index >= _items.Count OrElse _source Is Nothing Then Return
        Dim old = _items(index)
        Dim rects = StickerSplitter.SplitRect(_sourcePixels, _source.Width, _source.Height, _background, old.Rect, parts, horizontal)
        If rects.Count < 2 Then
            _status.Text = "這個框找不到可以切開的地方。"
            Return
        End If
        old.DisposeImages()
        _items.RemoveAt(index)
        Dim added As New List(Of StickerItem)()
        For Each r In rects
            Dim it As New StickerItem With {.Rect = r, .Mode = old.Mode}
            it.Settings.EdgeFillColorArgb = old.Settings.EdgeFillColorArgb
            it.Settings.EdgeFillTolerance = old.Settings.EdgeFillTolerance
            If it.Mode = CutMode.AiGeneral OrElse it.Mode = CutMode.AiHuman Then it.Mode = CutMode.EdgeFill
            Recompose(it)
            _items.Add(it)
            added.Add(it)
        Next
        Dim order = StickerSplitter.ReadingOrder(_items.Select(Function(x) x.Rect)).ToList()
        Dim sorted = order.Select(Function(rc) _items.First(Function(x) x.Rect = rc)).ToList()
        _items.Clear()
        _items.AddRange(sorted)
        _selected = _items.IndexOf(added(0))
        RebuildTiles()
        UpdateDetail()
        _status.Text = $"已拆成 {rects.Count} 張，共 {_items.Count} 張。"
    End Sub

    Private Sub DeleteSelected()
        If _selected < 0 OrElse _selected >= _items.Count Then Return
        _items(_selected).DisposeImages()
        _items.RemoveAt(_selected)
        _selected = Math.Min(_selected, _items.Count - 1)
        RebuildTiles()
        UpdateDetail()
    End Sub

    Private Sub ToggleInclude(index As Integer)
        If index < 0 OrElse index >= _items.Count Then Return
        _items(index).Included = Not _items(index).Included
        RefreshTiles()
    End Sub

    Private Sub SetAllIncluded(value As Boolean)
        For Each it In _items
            it.Included = value
        Next
        RefreshTiles()
    End Sub

    Private Sub SetAllModes(mode As CutMode)
        For Each it In _items
            If it.Mode <> mode Then
                it.History.Push((it.Mode, it.Settings.Clone()))
                it.Mode = mode
                Recompose(it)
            End If
        Next
        RefreshTiles()
        UpdateDetail()
    End Sub

    '=====================================================================
    ' 去背與修正
    '=====================================================================

    Private ReadOnly Property Current As StickerItem
        Get
            Return If(_selected >= 0 AndAlso _selected < _items.Count, _items(_selected), Nothing)
        End Get
    End Property

    ''' <summary>重算一張的結果（去背時透明背景）。</summary>
    Private Sub Recompose(it As StickerItem)
        If it.Crop Is Nothing Then it.Crop = StickerSplitter.Crop(_source, it.Rect)
        it.Result?.Dispose()
        it.Result = Compose(it, CutoutBackground.Transparent)
    End Sub

    Private Function Compose(it As StickerItem, background As CutoutBackground) As Bitmap
        If it.Mode = CutMode.None Then Return DirectCast(it.Crop.Clone(), Bitmap)
        Dim s = it.Settings.Clone()
        s.Background = background
        s.EdgeFill = it.Mode = CutMode.EdgeFill
        Dim ai As Bitmap = Nothing
        If it.Mode = CutMode.AiGeneral Then it.AiMasks.TryGetValue(CutoutModel.General, ai)
        If it.Mode = CutMode.AiHuman Then it.AiMasks.TryGetValue(CutoutModel.Human, ai)
        Return CutoutCompositor.Compose(it.Crop, ai, s)
    End Function

    Private Sub SetMode(mode As CutMode)
        Dim it = Current
        If it Is Nothing OrElse it.Mode = mode Then Return
        it.History.Push((it.Mode, it.Settings.Clone()))
        it.Mode = mode
        Dim model = If(mode = CutMode.AiHuman, CutoutModel.Human, CutoutModel.General)
        If (mode = CutMode.AiGeneral OrElse mode = CutMode.AiHuman) AndAlso Not it.AiMasks.ContainsKey(model) Then
            ComputeAi(it, model)
            Return
        End If
        Recompose(it)
        RefreshTiles()
        RefreshDetailImage()
    End Sub

    ''' <summary>AI 去背在背景執行，算好後套用（這張貼圖還是 AI 模式時）。</summary>
    Private Sub ComputeAi(it As StickerItem, model As CutoutModel)
        If Not BackgroundRemover.ModelAvailable(model) Then
            MessageBox.Show(Me, "找不到去背模型：" & BackgroundRemover.ModelPath(model), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        _status.Text = "AI 去背計算中…（第一次載入模型會久一點）"
        Cursor = Cursors.AppStarting
        Dim copy = DirectCast(it.Crop.Clone(), Bitmap)
        Task.Run(Function() BackgroundRemover.ComputeMask(copy, model)).ContinueWith(
            Sub(t)
                copy.Dispose()
                Dim mask = If(t.Status = TaskStatus.RanToCompletion, t.Result, Nothing)
                Try
                    If IsDisposed Then mask?.Dispose() : Return
                    BeginInvoke(Sub()
                                    Cursor = Cursors.Default
                                    If mask Is Nothing OrElse Not _items.Contains(it) Then
                                        _status.Text = "AI 去背失敗。"
                                        mask?.Dispose()
                                        Return
                                    End If
                                    it.AiMasks(model) = mask
                                    _status.Text = "AI 去背完成。"
                                    Recompose(it)
                                    RefreshTiles()
                                    If it Is Current Then RefreshDetailImage()
                                End Sub)
                Catch ex As InvalidOperationException
                    mask?.Dispose()
                End Try
            End Sub)
    End Sub

    ''' <summary>改設定（滑桿）：同一個滑桿連續拖曳只記一筆復原。</summary>
    Private _lastSettingKey As String
    Private Sub ChangeSettings(change As Action(Of CutoutSettings), key As String)
        Dim it = Current
        If _syncing OrElse it Is Nothing Then Return
        If _lastSettingKey <> key Then it.History.Push((it.Mode, it.Settings.Clone()))
        _lastSettingKey = key
        change(it.Settings)
        Recompose(it)
        RefreshTiles()
        RefreshDetailImage()
    End Sub

    Private Sub OnToolToggled(source As CheckBox)
        If _syncing Then Return
        If source.Checked Then
            _syncing = True
            For Each cb In {_wandTool, _keepTool, _eraseTool}
                If cb IsNot source Then cb.Checked = False
            Next
            _syncing = False
        End If
        UpdateTool()
    End Sub

    Private Sub UpdateTool()
        If _wandTool.Checked Then
            _detail.Tool = PreviewCanvas.CanvasTool.Wand
        ElseIf _keepTool.Checked OrElse _eraseTool.Checked Then
            _detail.Tool = PreviewCanvas.CanvasTool.MaskBrush
            _detail.MaskKeep = _keepTool.Checked
            _detail.BrushRadius = _brushSize.Value
        Else
            _detail.Tool = PreviewCanvas.CanvasTool.None
        End If
    End Sub

    Private Sub OnWandClicked(p As PointF, alt As Boolean)
        Dim it = Current
        If it Is Nothing Then Return
        If it.Mode = CutMode.None Then SetModeSilently(it, CutMode.EdgeFill)
        it.History.Push((it.Mode, it.Settings.Clone()))
        _lastSettingKey = Nothing
        it.Settings.Wand.Add(New WandClick With {.X = Math.Round(p.X, 5), .Y = Math.Round(p.Y, 5), .Tolerance = _wandTol.Value, .Restore = alt})
        Recompose(it)
        RefreshTiles()
        RefreshDetailImage()
    End Sub

    Private Sub OnStroke(points As List(Of PointF), screenRadius As Single)
        Dim it = Current
        If it Is Nothing Then Return
        If it.Mode = CutMode.None Then SetModeSilently(it, CutMode.EdgeFill)
        it.History.Push((it.Mode, it.Settings.Clone()))
        _lastSettingKey = Nothing
        Dim stroke As New CutoutStroke With {.Keep = _keepTool.Checked,
            .Radius = screenRadius / Math.Max(0.0001, _detail.EffectiveZoom()) / Math.Max(it.Crop.Width, it.Crop.Height)}
        For Each p In points
            stroke.AddPoint(New PointF(p.X, p.Y))
        Next
        it.Settings.Strokes.Add(stroke)
        Recompose(it)
        RefreshTiles()
        RefreshDetailImage()
    End Sub

    ''' <summary>不去背的貼圖要修正時，先切回背景色填充（不另記復原）。</summary>
    Private Sub SetModeSilently(it As StickerItem, mode As CutMode)
        it.Mode = mode
        _syncing = True
        _modeButtons(CInt(mode)).Checked = True
        _syncing = False
    End Sub

    Private Sub UndoItem()
        Dim it = Current
        If it Is Nothing OrElse it.History.Count = 0 Then Return
        Dim prev = it.History.Pop()
        it.Mode = prev.Mode
        it.Settings = prev.Settings
        _lastSettingKey = Nothing
        Recompose(it)
        RefreshTiles()
        UpdateDetail()
    End Sub

    '=====================================================================
    ' 縮圖與修正區
    '=====================================================================

    Private Sub SelectItem(index As Integer)
        If index < 0 OrElse index >= _items.Count Then Return
        _selected = index
        _lastSettingKey = Nothing
        RefreshTiles()
        UpdateDetail()
    End Sub

    Private Sub RebuildTiles()
        _tiles.SuspendLayout()
        For Each c As Control In _tiles.Controls.Cast(Of Control)().ToList()
            c.Dispose()
        Next
        For i = 0 To _items.Count - 1
            Dim tile As New TileView With {.Index = i, .Size = New Size(118, 136), .Margin = New Padding(4)}
            AddHandler tile.Clicked, Sub(idx, onCheck)
                                         If onCheck Then ToggleInclude(idx) Else SelectItem(idx)
                                     End Sub
            _help.SetHelp("helper.tile", tile)
            _tiles.Controls.Add(tile)
        Next
        _tiles.ResumeLayout()
        RefreshTiles()
    End Sub

    Private Sub RefreshTiles()
        For Each tile As TileView In _tiles.Controls
            If tile.Index >= _items.Count Then Continue For
            Dim it = _items(tile.Index)
            tile.Picture = it.Result
            tile.Included = it.Included
            tile.Selected = tile.Index = _selected
            tile.Label = $"{tile.Index + 1}　{it.Rect.Width}×{it.Rect.Height}"
            tile.Invalidate()
        Next
        _sourceView.SetBoxes(_items.Select(Function(it) it.Rect).ToList(), _items.Select(Function(it) it.Included).ToList(), _selected)
        Dim n = _items.Where(Function(it) it.Included).Count()
        _found.Text = If(_items.Count = 0, "", $"找到 {_items.Count} 張，已選 {n} 張")
        _save.Text = $"存成貼圖（{n} 張）"
        _save.Enabled = n > 0
    End Sub

    Private Sub UpdateDetail()
        Dim it = Current
        _syncing = True
        Try
            Dim has = it IsNot Nothing
            For Each rb In _modeButtons
                rb.Enabled = has
            Next
            If has Then
                _modeButtons(CInt(it.Mode)).Checked = True
                _edgeTol.Value = it.Settings.EdgeFillTolerance
                _feather.Value = it.Settings.Feather
                _shift.Value = it.Settings.Shift
                If it.Settings.Wand.Count > 0 Then _wandTol.Value = it.Settings.Wand(it.Settings.Wand.Count - 1).Tolerance
                _detailTitle.Text = $"第 {_selected + 1} 張（{it.Rect.Width} × {it.Rect.Height}）：魔術棒點一下去掉、Alt＋點補回；滾輪縮放、右鍵拖曳平移"
            Else
                _detailTitle.Text = "選取的貼圖"
            End If
        Finally
            _syncing = False
        End Try
        RefreshDetailImage()
        UpdateTool()
    End Sub

    Private _detailImage As Bitmap
    Private Sub RefreshDetailImage()
        Dim it = Current
        Dim old = _detailImage
        If it Is Nothing Then
            _detailImage = Nothing
        ElseIf _showMask.Checked AndAlso it.Mode <> CutMode.None Then
            _detailImage = Compose(it, CutoutBackground.MaskPreview)
        Else
            _detailImage = DirectCast(it.Result.Clone(), Bitmap)
        End If
        Dim sameSize = old IsNot Nothing AndAlso _detailImage IsNot Nothing AndAlso old.Size = _detailImage.Size
        _detail.ImageScale = 1
        _detail.Image = _detailImage
        If Not sameSize Then _detail.ZoomToFit()
        old?.Dispose()
    End Sub

    '=====================================================================
    ' 存檔
    '=====================================================================

    Private Sub SaveStickers()
        Dim chosen = _items.Where(Function(it) it.Included).ToList()
        If chosen.Count = 0 Then Return
        Dim bad = Path.GetInvalidFileNameChars()
        Dim theme = New String(_themeBox.Text.Trim().Where(Function(ch) Not bad.Contains(ch)).ToArray())
        Dim prefix = New String(_prefixBox.Text.Trim().Where(Function(ch) Not bad.Contains(ch)).ToArray())
        If theme = "" OrElse theme = StickerLibrary.BuiltInTheme Then theme = "貼圖小幫手"
        If prefix = "" Then prefix = theme
        Dim dir = Path.Combine(StickerLibrary.Root, theme)
        Dim saved = 0
        Try
            Directory.CreateDirectory(dir)
            Dim n = 1
            For Each it In chosen
                Dim bmp = If(_trim.Checked, StickerSplitter.TrimTransparent(it.Result), DirectCast(it.Result.Clone(), Bitmap))
                If bmp Is Nothing Then Continue For
                Using bmp
                    Dim file As String
                    Do
                        file = Path.Combine(dir, $"{prefix}_{n:00}.png")
                        n += 1
                    Loop While IO.File.Exists(file)
                    bmp.Save(file, Imaging.ImageFormat.Png)
                    saved += 1
                End Using
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Runtime.InteropServices.ExternalException
            MessageBox.Show(Me, "存檔失敗：" & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        _status.Text = $"已存 {saved} 張到 stick\{theme}\。"
        If Not _themeBox.Items.Contains(theme) Then _themeBox.Items.Add(theme)
        _onSaved?.Invoke(theme)
    End Sub

    '=====================================================================
    ' 鍵盤
    '=====================================================================

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If TypeOf ActiveControl Is TextBoxBase OrElse TypeOf ActiveControl Is ComboBox Then Return MyBase.ProcessCmdKey(msg, keyData)
        Select Case keyData
            Case Keys.Control Or Keys.V : PasteImage() : Return True
            Case Keys.Control Or Keys.O : OpenImage() : Return True
            Case Keys.Control Or Keys.Z : UndoItem() : Return True
            Case Keys.Delete : DeleteSelected() : Return True
            Case Keys.W : _wandTool.Checked = Not _wandTool.Checked : Return True
        End Select
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    '=====================================================================
    ' 原圖檢視（框的點選、調整、補畫）
    '=====================================================================

    Private Class SourceView
        Inherits Control

        Private _image As Bitmap
        Private _boxes As New List(Of Rectangle)()
        Private _included As New List(Of Boolean)()
        Private _selected As Integer = -1
        Private _dragIndex As Integer = -1
        Private _dragHandle As Integer = -1   ' 0..7 邊角；8 = 移動
        Private _dragStart As Point
        Private _dragRect As Rectangle
        Private _newBox As Rectangle?
        Private Shared ReadOnly NumberFont As New Font("Arial", 9, FontStyle.Bold)

        Public Event BoxSelected(index As Integer)
        Public Event BoxToggled(index As Integer)
        Public Event BoxChanged(index As Integer, rect As Rectangle)
        Public Event BoxAdded(rect As Rectangle)
        ''' <summary>在框上按右鍵（沒有拖曳）：顯示拆分選單。</summary>
        Public Event BoxMenuRequested(index As Integer, location As Point)

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            BackColor = Color.FromArgb(52, 52, 56)
        End Sub

        Public Property Image As Bitmap
            Get
                Return _image
            End Get
            Set(value As Bitmap)
                _image = value
                Invalidate()
            End Set
        End Property

        Public Sub SetBoxes(boxes As List(Of Rectangle), included As List(Of Boolean), selected As Integer)
            _boxes = boxes : _included = included : _selected = selected
            Invalidate()
        End Sub

        Private Function Area() As RectangleF
            If _image Is Nothing Then Return RectangleF.Empty
            Dim s = Math.Min((Width - 20) / CDbl(_image.Width), (Height - 20) / CDbl(_image.Height))
            Dim w = CSng(_image.Width * s), h = CSng(_image.Height * s)
            Return New RectangleF((Width - w) / 2, (Height - h) / 2, w, h)
        End Function

        Private Function ScaleFactor() As Double
            Dim a = Area()
            Return If(_image Is Nothing, 1, a.Width / _image.Width)
        End Function

        Private Function ToScreen(r As Rectangle) As RectangleF
            Dim a = Area(), s = ScaleFactor()
            Return New RectangleF(CSng(a.X + r.X * s), CSng(a.Y + r.Y * s), CSng(r.Width * s), CSng(r.Height * s))
        End Function

        Private Function ToImage(p As Point) As Point
            Dim a = Area(), s = ScaleFactor()
            Return New Point(CInt(Math.Round((p.X - a.X) / s)), CInt(Math.Round((p.Y - a.Y) / s)))
        End Function

        ''' <summary>點到第幾個框的哪裡：handle 0..7 邊角、8 框內、9 編號（切換排除）。</summary>
        Private Function HitBox(p As Point, ByRef handle As Integer) As Integer
            For i = _boxes.Count - 1 To 0 Step -1
                Dim r = ToScreen(_boxes(i))
                If New RectangleF(r.X, r.Y, 22, 16).Contains(p) Then handle = 9 : Return i
                Dim nearL = Math.Abs(p.X - r.Left) <= 5, nearR = Math.Abs(p.X - r.Right) <= 5
                Dim nearT = Math.Abs(p.Y - r.Top) <= 5, nearB = Math.Abs(p.Y - r.Bottom) <= 5
                Dim inX = p.X >= r.Left - 5 AndAlso p.X <= r.Right + 5, inY = p.Y >= r.Top - 5 AndAlso p.Y <= r.Bottom + 5
                If inX AndAlso inY Then
                    If nearT AndAlso nearL Then handle = 0 : Return i
                    If nearT AndAlso nearR Then handle = 2 : Return i
                    If nearB AndAlso nearR Then handle = 4 : Return i
                    If nearB AndAlso nearL Then handle = 6 : Return i
                    If nearT Then handle = 1 : Return i
                    If nearR Then handle = 3 : Return i
                    If nearB Then handle = 5 : Return i
                    If nearL Then handle = 7 : Return i
                End If
                If r.Contains(p) Then handle = 8 : Return i
            Next
            handle = -1
            Return -1
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            If _image Is Nothing Then
                TextRenderer.DrawText(g, "開啟或貼上（Ctrl+V）一張排了很多貼圖的圖片", Font, ClientRectangle, Color.FromArgb(180, 180, 185),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                Return
            End If
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.DrawImage(_image, Area())
            g.SmoothingMode = SmoothingMode.AntiAlias
            For i = 0 To _boxes.Count - 1
                Dim r = If(i = _dragIndex, ToScreen(_dragRect), ToScreen(_boxes(i)))
                Dim inc = i >= _included.Count OrElse _included(i)
                Dim col = If(inc, Color.FromArgb(70, 170, 50), Color.FromArgb(225, 60, 60))
                Using pen As New Pen(col, If(i = _selected, 3, 1.6F)) With {.DashStyle = If(inc, DashStyle.Solid, DashStyle.Dash)}
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height)
                End Using
                If i = _selected Then
                    Using sel As New Pen(Color.FromArgb(90, 150, 230), 1)
                        g.DrawRectangle(sel, r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6)
                    End Using
                End If
                Using br As New SolidBrush(col)
                    g.FillRectangle(br, r.X, r.Y, 22, 16)
                End Using
                TextRenderer.DrawText(g, If(inc, "", "✕") & (i + 1).ToString(), NumberFont, New Rectangle(CInt(r.X), CInt(r.Y), 22, 16), Color.White,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Next
            If _newBox.HasValue Then
                Using pen As New Pen(Color.FromArgb(255, 200, 60), 2) With {.DashStyle = DashStyle.Dash}
                    Dim r = ToScreen(_newBox.Value)
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height)
                End Using
            End If
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
            If _image Is Nothing Then Return
            _dragStart = e.Location
            If e.Button = MouseButtons.Right Then
                _newBox = New Rectangle(ToImage(e.Location), Size.Empty)
                Capture = True
                Return
            End If
            If e.Button <> MouseButtons.Left Then Return
            Dim handle As Integer
            Dim i = HitBox(e.Location, handle)
            If i < 0 Then Return
            If handle = 9 Then
                RaiseEvent BoxToggled(i)
                Return
            End If
            RaiseEvent BoxSelected(i)
            _dragIndex = i
            _dragHandle = handle
            _dragRect = _boxes(i)
            Capture = True
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If _image Is Nothing Then Return
            If _newBox.HasValue Then
                Dim a = ToImage(_dragStart), b = ToImage(e.Location)
                _newBox = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))
                Invalidate()
                Return
            End If
            If _dragIndex >= 0 Then
                Dim s = ScaleFactor()
                Dim dx = CInt(Math.Round((e.X - _dragStart.X) / s)), dy = CInt(Math.Round((e.Y - _dragStart.Y) / s))
                Dim r = _boxes(_dragIndex)
                Dim l = r.Left, t = r.Top, rt = r.Right, bt = r.Bottom
                Select Case _dragHandle
                    Case 8 : l += dx : rt += dx : t += dy : bt += dy
                    Case Else
                        If _dragHandle = 0 OrElse _dragHandle = 6 OrElse _dragHandle = 7 Then l = Math.Min(rt - 4, l + dx)
                        If _dragHandle = 2 OrElse _dragHandle = 3 OrElse _dragHandle = 4 Then rt = Math.Max(l + 4, rt + dx)
                        If _dragHandle = 0 OrElse _dragHandle = 1 OrElse _dragHandle = 2 Then t = Math.Min(bt - 4, t + dy)
                        If _dragHandle = 4 OrElse _dragHandle = 5 OrElse _dragHandle = 6 Then bt = Math.Max(t + 4, bt + dy)
                End Select
                _dragRect = Rectangle.FromLTRB(l, t, rt, bt)
                _dragRect.Intersect(New Rectangle(0, 0, _image.Width, _image.Height))
                Invalidate()
                Return
            End If
            Dim handle As Integer
            HitBox(e.Location, handle)
            Cursor = If(handle = 0 OrElse handle = 4, Cursors.SizeNWSE, If(handle = 2 OrElse handle = 6, Cursors.SizeNESW,
                     If(handle = 1 OrElse handle = 5, Cursors.SizeNS, If(handle = 3 OrElse handle = 7, Cursors.SizeWE,
                     If(handle = 8, Cursors.SizeAll, If(handle = 9, Cursors.Hand, Cursors.Default))))))
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            Capture = False
            If _newBox.HasValue Then
                Dim r = _newBox.Value
                _newBox = Nothing
                Invalidate()
                If e.Button = MouseButtons.Right AndAlso Math.Abs(e.X - _dragStart.X) + Math.Abs(e.Y - _dragStart.Y) <= 4 Then
                    Dim handle As Integer
                    Dim hit = HitBox(e.Location, handle)
                    If hit >= 0 Then RaiseEvent BoxMenuRequested(hit, e.Location)
                    Return
                End If
                If r.Width >= 4 AndAlso r.Height >= 4 Then RaiseEvent BoxAdded(r)
                Return
            End If
            If _dragIndex >= 0 Then
                Dim i = _dragIndex
                _dragIndex = -1
                Dim moved = Math.Abs(e.X - _dragStart.X) + Math.Abs(e.Y - _dragStart.Y) > 2
                If moved AndAlso _dragRect <> _boxes(i) Then RaiseEvent BoxChanged(i, _dragRect)
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
            MyBase.OnMouseDoubleClick(e)
            If e.Button <> MouseButtons.Left Then Return
            Dim handle As Integer
            Dim i = HitBox(e.Location, handle)
            If i >= 0 AndAlso handle = 8 Then RaiseEvent BoxToggled(i)
        End Sub
    End Class

    '=====================================================================
    ' 拆分結果的縮圖
    '=====================================================================

    Private Class TileView
        Inherits Control

        Public Index As Integer
        Public Picture As Bitmap
        Public Included As Boolean = True
        Public Selected As Boolean
        Public Label As String = ""
        Public Event Clicked(index As Integer, onCheck As Boolean)

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
            Cursor = Cursors.Hand
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(ThemeManager.Back(If(Selected, Color.FromArgb(210, 228, 250), Color.White)))
            Dim box = New Rectangle(5, 5, Width - 10, Height - 30)
            For y = box.Top To box.Bottom - 1 Step 8
                For x = box.Left To box.Right - 1 Step 8
                    Using br As New SolidBrush(If(((x - box.Left) \ 8 + (y - box.Top) \ 8) Mod 2 = 0, Color.FromArgb(232, 232, 232), Color.White))
                        g.FillRectangle(br, Rectangle.Intersect(New Rectangle(x, y, 8, 8), box))
                    End Using
                Next
            Next
            If Picture IsNot Nothing Then
                Dim k = Math.Min(box.Width / CDbl(Picture.Width), box.Height / CDbl(Picture.Height))
                Dim w = CInt(Picture.Width * k), h = CInt(Picture.Height * k)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.DrawImage(Picture, box.X + (box.Width - w) \ 2, box.Y + (box.Height - h) \ 2, w, h)
            End If
            If Not Included Then
                Using veil As New SolidBrush(If(ThemeManager.Dark, Color.FromArgb(150, 30, 33, 39), Color.FromArgb(150, 255, 255, 255)))
                    g.FillRectangle(veil, box)
                End Using
            End If
            ' 勾選框
            Dim chk = New Rectangle(6, Height - 22, 16, 16)
            g.FillRectangle(Brushes.White, chk)
            g.DrawRectangle(Pens.Gray, chk)
            If Included Then
                Using pen As New Pen(Color.FromArgb(24, 120, 60), 2)
                    g.DrawLines(pen, {New Point(chk.X + 3, chk.Y + 8), New Point(chk.X + 7, chk.Y + 12), New Point(chk.X + 13, chk.Y + 4)})
                End Using
            End If
            TextRenderer.DrawText(g, Label, Font, New Rectangle(26, Height - 24, Width - 28, 20), ThemeManager.Fore(Color.FromArgb(60, 66, 78)), TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
            Using pen As New Pen(If(Selected, Color.FromArgb(55, 138, 221), ThemeManager.Line(Color.FromArgb(205, 210, 218))), If(Selected, 2, 1))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1)
            End Using
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            RaiseEvent Clicked(Index, e.X < 26 AndAlso e.Y > Height - 26)
        End Sub
    End Class
End Class
