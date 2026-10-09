Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 繪圖分頁的快速工具（不必把手移到工具列、面板）：按熱鍵在滑鼠位置叫出
''' 「快速面板」（前景／背景色、小色環、近期與常用色票、筆刷大小、不透明度、常用筆刷、吸管／橡皮擦／復原／重做、快速圖層）
''' 或「輪盤」（筆刷、橡皮擦、吸管、填色、漸層、選取、文字、色彩，點一下就選）。熱鍵在設定視窗改（預設 F9、F10）。
''' 前景色＝線條色、背景色＝填色。
''' </summary>
Partial Friend Class frmEditor

    Friend Enum QuickTool
        Brush
        Eraser
        Eyedropper
        Bucket
        Gradient
        SelectMove
        Text
        Color
    End Enum

    Private _quickPanel As QuickPanel
    Private _radial As RadialMenu
    ''' <summary>輪盤或面板選了「吸管」：下一次在畫布上點一下就吸色（不必按住 Alt）。</summary>
    Private _pickOnce As Boolean

    ''' <summary>常用筆刷（快速面板兩列、輪盤外圈共用）。</summary>
    Friend Const QuickBrushCount As Integer = 8
    Private Shared ReadOnly DefaultQuickBrushes As BrushKind() = {BrushKind.HardRound, BrushKind.SoftRound, BrushKind.Pencil, BrushKind.Airbrush,
                                                                   BrushKind.Watercolor, BrushKind.OilPaint, BrushKind.ChineseBrush, BrushKind.Marker}

    Private Shared Function ParseKey(name As String, fallback As Keys) As Keys
        Dim k As Keys
        If Not String.IsNullOrEmpty(name) AndAlso [Enum].TryParse(name, True, k) Then Return k
        Return fallback
    End Function

    ''' <summary>繪圖分頁的熱鍵：快速面板、輪盤（再按一次收起）。</summary>
    Private Function HandleQuickKey(keyData As Keys) As Boolean
        If _tabs Is Nothing OrElse _tabs.SelectedIndex <> TabDraw Then Return False
        If keyData = Keys.Escape AndAlso _pickOnce Then
            _pickOnce = False
            SetStatusMessage("已取消吸管。")
            Dim p = _canvas.PointToClient(_canvas.PointerPosition)
            If _canvas.ClientRectangle.Contains(p) Then UpdateDrawCursor(p, ScreenToUnit(p))
            Return True
        End If
        If keyData = Keys.Escape AndAlso HandleFullScreenEscape() Then Return True
        If keyData = Keys.F11 Then ToggleFullScreen() : Return True ' 選色視窗有焦點時也能切換
        If keyData = ParseKey(_appSettings.QuickPanelHotkey, Keys.F9) Then
            ToggleQuickPanel(_canvas.PointerPosition)
            Return True
        End If
        If keyData = ParseKey(_appSettings.QuickRadialHotkey, Keys.F10) Then
            ToggleRadial(_canvas.PointerPosition)
            Return True
        End If
        Return False
    End Function

    Private Sub ToggleQuickPanel(at As Point)
        _radial?.Hide()
        If _quickPanel Is Nothing OrElse _quickPanel.IsDisposed Then _quickPanel = New QuickPanel(Me)
        If _quickPanel.Visible Then _quickPanel.Hide() : Return
        _quickPanel.SyncFromEditor()
        _quickPanel.ShowAt(at)
    End Sub

    Private Sub ToggleRadial(at As Point)
        _quickPanel?.Hide()
        If _radial Is Nothing OrElse _radial.IsDisposed Then _radial = New RadialMenu(Me)
        If _radial.Visible Then _radial.Hide() : Return
        _radial.ShowAt(at)
    End Sub

    ''' <summary>目前的快速工具（輪盤中間顯示、亮起來的那一格）。</summary>
    Friend Function CurrentQuickTool() As QuickTool
        If _pickOnce Then Return QuickTool.Eyedropper
        Select Case _drawStrip.SelectedTool
            Case CInt(DrawShape.Raster) : Return If(_drawEraser.Checked, QuickTool.Eraser, QuickTool.Brush)
            Case CInt(DrawShape.Bucket) : Return QuickTool.Bucket
            Case CInt(DrawShape.Gradient) : Return QuickTool.Gradient
            Case DrawIcons.SelectTool : Return QuickTool.SelectMove
            Case CInt(DrawShape.CalloutRect), CInt(DrawShape.CalloutEllipse), CInt(DrawShape.CalloutCloud), CInt(DrawShape.CalloutBubble), CInt(DrawShape.CalloutShout)
                Return QuickTool.Text
        End Select
        Return QuickTool.Brush
    End Function

    ''' <summary>輪盤或面板選了一個工具。</summary>
    Friend Sub SelectQuickTool(tool As QuickTool, Optional near As Point? = Nothing)
        _pickOnce = False
        Select Case tool
            Case QuickTool.Brush
                _drawStrip.SelectedTool = CInt(DrawShape.Raster)
                _drawEraser.Checked = False
            Case QuickTool.Eraser
                _drawStrip.SelectedTool = CInt(DrawShape.Raster)
                _drawEraser.Checked = True
            Case QuickTool.Eyedropper
                _pickOnce = True
                SetStatusMessage("吸管：在畫布上點一下吸色（設成前景色）；按 Esc 取消。")
            Case QuickTool.Bucket : _drawStrip.SelectedTool = CInt(DrawShape.Bucket)
            Case QuickTool.Gradient : _drawStrip.SelectedTool = CInt(DrawShape.Gradient)
            Case QuickTool.SelectMove : _drawStrip.SelectedTool = DrawIcons.SelectTool
            Case QuickTool.Text : _drawStrip.SelectedTool = CInt(DrawShape.CalloutRect)
            Case QuickTool.Color
                ShowColorWindow(TargetStroke)
                If near.HasValue AndAlso _colorWin IsNot Nothing Then
                    ' 色彩面板開在輪盤旁邊
                    Dim wa = Screen.FromPoint(near.Value).WorkingArea
                    Dim x = Math.Min(wa.Right - _colorWin.Width, near.Value.X + 160), y = Math.Max(wa.Top, Math.Min(wa.Bottom - _colorWin.Height, near.Value.Y - 150))
                    _colorWin.Location = New Point(Math.Max(wa.Left, x), y)
                End If
        End Select
        UpdateDrawHint()
        Dim p = _canvas.PointToClient(_canvas.PointerPosition)
        If _canvas.ClientRectangle.Contains(p) Then UpdateDrawCursor(p, ScreenToUnit(p))
    End Sub

    ''' <summary>前景色（線條色）、背景色（填色）。</summary>
    Friend ReadOnly Property ForegroundColor As Color
        Get
            Return Color.FromArgb(255, Color.FromArgb(If(StyleTarget(_recipe), _drawStyle).StrokeColorArgb))
        End Get
    End Property

    Friend ReadOnly Property BackgroundColor As Color
        Get
            Return Color.FromArgb(255, Color.FromArgb(If(StyleTarget(_recipe), _drawStyle).FillColorArgb))
        End Get
    End Property

    ''' <summary>快速面板改顏色：拖曳色環時合併成每 30 ms 套用一次（同選色視窗）。</summary>
    Friend Sub QuickSetColor(c As Color, background As Boolean, Optional remember As Boolean = False)
        _pendingPick = (c, If(background, TargetFill, TargetStroke))
        _lastPickTime = Environment.TickCount
        If remember Then
            _applyTimer.Stop()
            ApplyPickedColor(c, If(background, TargetFill, TargetStroke))
            _pendingPick = Nothing
            RememberColor(c)
        ElseIf Not _applyTimer.Enabled Then
            _applyTimer.Start()
        End If
    End Sub

    ''' <summary>記進最近使用色（存檔共用；開著的選色器跟著更新）。</summary>
    Friend Sub RememberColor(c As Color)
        Aqua.ColorPicker.RememberRecentColor(c)
        If _colorWin IsNot Nothing AndAlso Not _colorWin.IsDisposed Then _colorWin.Picker.RecentColors = Aqua.ColorPicker.SavedRecentColors()
    End Sub

    Friend Sub SwapColors()
        Dim fg = ForegroundColor.ToArgb(), bg = BackgroundColor.ToArgb()
        SetDrawProp(Sub(d)
                        d.StrokeColorArgb = bg
                        d.FillColorArgb = fg
                    End Sub)
    End Sub

    Friend Function QuickBrushes() As BrushKind()
        Dim list = _appSettings.QuickBrushes
        If list Is Nothing OrElse (list.Count <> 4 AndAlso list.Count <> QuickBrushCount) Then Return DefaultQuickBrushes
        Dim result = list.Select(Function(i) CType(Math.Max(0, Math.Min(DrawGeometry.BrushNames.Length - 1, i)), BrushKind)).ToList()
        ' 舊設定只有 4 個：後 4 個用預設
        For i = result.Count To QuickBrushCount - 1
            result.Add(DefaultQuickBrushes(i))
        Next
        Return result.ToArray()
    End Function

    ''' <summary>目前的筆刷（選取的圖層或繪圖設定）。</summary>
    Friend ReadOnly Property CurrentBrush As BrushKind
        Get
            Return If(StyleTarget(_recipe), _drawStyle).Brush
        End Get
    End Property

    ''' <summary>筆刷 b 的效果清單（特效、材質、粒子、貼圖主題）；沒有時 Nothing。</summary>
    Friend Function QuickEffectCategories(b As BrushKind) As EffectCatalog.Category()
        Return CurrentEffectCategories(New DrawLayer With {.Brush = b, .StrokeColorArgb = If(StyleTarget(_recipe), _drawStyle).StrokeColorArgb, .HoseTheme = If(StyleTarget(_recipe), _drawStyle).HoseTheme})
    End Function

    ''' <summary>目前筆刷選中的效果值（同繪圖分頁「效果」列）。</summary>
    Friend Function QuickEffectValue() As Integer
        Return EffectValue(If(StyleTarget(_recipe), _drawStyle))
    End Function

    Friend Sub QuickApplyEffect(entry As EffectCatalog.Entry)
        ApplyEffectEntry(entry)
        UpdateDrawControls()
    End Sub

    ''' <summary>右鍵換常用筆刷的選單（快速面板、輪盤共用）。</summary>
    Friend Sub ShowQuickBrushMenu(slot As Integer, owner As Control, at As Point, changed As Action, Optional closed As Action = Nothing)
        Dim current = QuickBrushes()(slot)
        Dim menu As New ContextMenuStrip()
        For i = 0 To DrawGeometry.BrushNames.Length - 1
            Dim b = CType(i, BrushKind)
            Dim mi = CType(menu.Items.Add("換成「" & DrawGeometry.BrushNames(i) & "」"), ToolStripMenuItem)
            mi.Checked = b = current
            AddHandler mi.Click, Sub()
                                       SetQuickBrush(slot, b)
                                       changed()
                                   End Sub
        Next
        AddHandler menu.Closed, Sub()
                                    closed?.Invoke()
                                    menu.BeginInvoke(Sub() menu.Dispose())
                                End Sub
        menu.Show(owner, at)
    End Sub

    Private _brushBrowser As BrushBrowser

    ''' <summary>輪盤的「更多…」：全部筆刷分類清單（紋理、特效、粒子、貼圖噴槍再選效果）。</summary>
    Friend Sub ShowBrushBrowser(at As Point)
        If _brushBrowser Is Nothing OrElse _brushBrowser.IsDisposed Then _brushBrowser = New BrushBrowser(Me)
        _brushBrowser.ShowAt(at)
    End Sub

    Friend Sub SetQuickBrush(slot As Integer, b As BrushKind)
        Dim list = QuickBrushes().Select(Function(x) CInt(x)).ToList()
        list(slot) = CInt(b)
        _appSettings.QuickBrushes = list
        _appSettings.Save()
    End Sub

    ''' <summary>快速面板選了常用筆刷：換筆刷，工具換成直接繪製（不是畫筆工具時）。</summary>
    Friend Sub UseQuickBrush(b As BrushKind)
        _pickOnce = False
        If _drawStrip.SelectedTool <> CInt(DrawShape.Raster) AndAlso _drawStrip.SelectedTool <> CInt(DrawShape.Freehand) Then _drawStrip.SelectedTool = CInt(DrawShape.Raster)
        _drawEraser.Checked = False
        SetDrawBrush(b)
    End Sub

    ''' <summary>筆刷大小、不透明度：直接改繪圖分頁的滑桿（同一個設定、同一套復原）。</summary>
    Friend Function DrawRowSlider(key As String) As Aqua.Slider
        Return _rows.FirstOrDefault(Function(r) r.Key = key)?.Slider
    End Function

    ''' <summary>快速圖層：繪圖圖層（上面的在前）。</summary>
    Friend Function QuickLayers() As List(Of (Index As Integer, Name As String))
        Dim result As New List(Of (Index As Integer, Name As String))()
        For i = LayerCount() - 1 To 0 Step -1
            Dim d = _recipe.Drawings(i)
            result.Add((i, If(String.IsNullOrEmpty(d.Name), DrawGeometry.ShapeNames(CInt(d.Shape)), d.Name)))
        Next
        Return result
    End Function

    Friend ReadOnly Property SelectedDrawIndex As Integer
        Get
            Return _drawIndex
        End Get
    End Property

    Friend Sub QuickSelectLayer(index As Integer)
        SelectDrawLayer(index)
    End Sub

    Friend Sub QuickCommand(name As String)
        RunCommand(name)
    End Sub

    ''' <summary>繪圖面板更新時，開著的快速面板跟著同步。</summary>
    Private Sub SyncQuickPanel()
        If _quickPanel IsNot Nothing AndAlso Not _quickPanel.IsDisposed AndAlso _quickPanel.Visible Then _quickPanel.SyncFromEditor()
    End Sub

    '=====================================================================
    ' 快速面板
    '=====================================================================

    Friend NotInheritable Class QuickPanel
        Inherits Form

        Private Const HeaderH As Integer = 34
        Private ReadOnly _ed As frmEditor
        Private ReadOnly _font As New Font("Microsoft JhengHei UI", 9.5F)
        Private ReadOnly _wheel As New Aqua.ColorWheel()
        Private ReadOnly _fg As New ColorChip()
        Private ReadOnly _bg As New ColorChip()
        Private ReadOnly _swap As New Button With {.Text = "⇄", .FlatStyle = FlatStyle.Flat}
        Private ReadOnly _recent As New SwatchRow()
        Private ReadOnly _custom As New SwatchRow()
        Private ReadOnly _size As New Aqua.Slider With {.Minimum = 1, .Maximum = 200, .ShowTicks = False}
        Private ReadOnly _opacity As New Aqua.Slider With {.Minimum = 0, .Maximum = 100, .ShowTicks = False}
        Private ReadOnly _sizeValue As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight}
        Private ReadOnly _opacityValue As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight}
        Private ReadOnly _brushTiles As New List(Of BrushChip)()
        Private ReadOnly _layers As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        Private _editBackground As Boolean
        Private _syncing As Boolean
        Private _modal As Boolean
        Private _drag As Point?

        Public Sub New(ed As frmEditor)
            _ed = ed
            Owner = ed
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            KeyPreview = True
            Font = _font
            DoubleBuffered = True
            ClientSize = New Size(452, 488)
            Tag = ThemeManager.SkipTreeTag ' 自己用 ThemeManager 的顏色畫

            ' 顏色：前景、背景（點一下選要改哪一個，按兩下開完整選色器）、對調；小色環；近期與常用色票
            _fg.SetBounds(16, HeaderH + 14, 56, 56)
            _bg.SetBounds(98, HeaderH + 30, 40, 40)
            _swap.SetBounds(74, HeaderH + 14, 24, 22)
            _swap.FlatAppearance.BorderSize = 0
            AddHandler _fg.Click, Sub() SetEditTarget(False)
            AddHandler _bg.Click, Sub() SetEditTarget(True)
            AddHandler _fg.DoubleClick, Sub() PickFull(False)
            AddHandler _bg.DoubleClick, Sub() PickFull(True)
            AddHandler _swap.Click, Sub()
                                        _ed.SwapColors()
                                        SyncFromEditor()
                                    End Sub
            _wheel.SetBounds(148, HeaderH + 6, 128, 128)
            AddHandler _wheel.ColorChanged, Sub()
                                                If _syncing Then Return
                                                CurrentChip().Color = _wheel.Color
                                                _ed.QuickSetColor(_wheel.Color, _editBackground)
                                            End Sub
            AddHandler _wheel.MouseUp, Sub() _ed.RememberColor(_wheel.Color)
            _recent.SetBounds(290, HeaderH + 26, 150, 22)
            _custom.SetBounds(290, HeaderH + 72, 150, 66)
            For Each row In {_recent, _custom}
                AddHandler row.Picked, Sub(c)
                                           CurrentChip().Color = c
                                           _syncing = True : _wheel.Color = c : _syncing = False
                                           _ed.QuickSetColor(c, _editBackground, remember:=True)
                                       End Sub
            Next
            Controls.AddRange({_fg, _bg, _swap, _wheel, _recent, _custom})

            ' 筆刷大小、不透明度（就是繪圖分頁的「粗細」「不透明度」）
            Dim y = HeaderH + 150
            For Each entry In {(_size, _sizeValue, "dw_width"), (_opacity, _opacityValue, "dw_opacity")}
                Dim s = entry.Item1, v = entry.Item2, key = entry.Item3
                s.SetBounds(110, y + 2, 270, 24)
                v.SetBounds(384, y, 52, 28)
                AddHandler s.ValueChanged, Sub()
                                               v.Text = If(key = "dw_width", s.Value.ToString(), s.Value & "%")
                                               If _syncing Then Return
                                               Dim target = _ed.DrawRowSlider(key)
                                               If target IsNot Nothing AndAlso target.Value <> s.Value Then target.Value = s.Value
                                           End Sub
                Controls.AddRange({s, v})
                y += 32
            Next

            ' 常用筆刷（右鍵換成別的筆刷）
            y += 30
            For i = 0 To QuickBrushCount - 1
                Dim slot = i
                Dim t As New BrushChip()
                t.SetBounds(16 + (i Mod 4) * 106, y + (i \ 4) * 56, 100, 52)
                AddHandler t.MouseUp, Sub(s, e)
                                          If e.Button = MouseButtons.Right Then
                                              _ed.ShowQuickBrushMenu(slot, t, New Point(0, t.Height), AddressOf SyncFromEditor)
                                          ElseIf e.Button = MouseButtons.Left Then
                                              _ed.UseQuickBrush(t.Brush)
                                              SyncFromEditor()
                                          End If
                                      End Sub
                _brushTiles.Add(t)
                Controls.Add(t)
            Next
            y += 116

            ' 吸管、橡皮擦、復原、重做
            Dim actions = {("吸管取色", Sub() _ed.SelectQuickTool(QuickTool.Eyedropper)), ("橡皮擦", Sub() _ed.SelectQuickTool(QuickTool.Eraser)),
                           ("↶ 復原", Sub() _ed.QuickCommand("undo")), ("↷ 重做", Sub() _ed.QuickCommand("redo"))}
            For i = 0 To actions.Length - 1
                Dim act = actions(i).Item2
                Dim b As New Button With {.Text = actions(i).Item1, .FlatStyle = FlatStyle.Flat}
                b.SetBounds(16 + i * 106, y, 100, 32)
                AddHandler b.Click, Sub()
                                        act()
                                        If b.Text.StartsWith("吸管") OrElse b.Text = "橡皮擦" Then Hide() Else SyncFromEditor()
                                    End Sub
                Controls.Add(b)
            Next
            y += 42

            ' 快速圖層
            _layers.SetBounds(110, y + 2, 326, 26)
            AddHandler _layers.SelectedIndexChanged, Sub()
                                                         If _syncing OrElse _layers.SelectedIndex < 0 Then Return
                                                         Dim items = _ed.QuickLayers()
                                                         _ed.QuickSelectLayer(items(_layers.SelectedIndex).Index)
                                                     End Sub
            Controls.Add(_layers)
            ApplyColors()
        End Sub

        Private Function CurrentChip() As ColorChip
            Return If(_editBackground, _bg, _fg)
        End Function

        Private Sub SetEditTarget(background As Boolean)
            _editBackground = background
            _fg.Selected = Not background
            _bg.Selected = background
            _syncing = True : _wheel.Color = CurrentChip().Color : _syncing = False
        End Sub

        ''' <summary>按兩下前景／背景：開完整的選色器。</summary>
        Private Sub PickFull(background As Boolean)
            SetEditTarget(background)
            _modal = True
            Try
                Using dlg As New Aqua.ColorPickerDialog With {.Color = CurrentChip().Color}
                    If dlg.ShowDialog(Me) = DialogResult.OK Then
                        _ed.QuickSetColor(dlg.Color, background, remember:=True)
                        SyncFromEditor()
                    End If
                End Using
            Finally
                _modal = False
            End Try
        End Sub

        ''' <summary>顯示在滑鼠位置（左上角對著游標稍微偏一點），不超出螢幕。</summary>
        Public Sub ShowAt(p As Point)
            Dim wa = Screen.FromPoint(p).WorkingArea
            Location = New Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, p.X - 40)), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, p.Y - 20)))
            Show(_ed)
            Activate()
        End Sub

        ''' <summary>和編輯器同步：前景／背景色、色票、筆刷大小、不透明度、常用筆刷、圖層清單。</summary>
        Public Sub SyncFromEditor()
            _syncing = True
            Try
                _fg.Color = _ed.ForegroundColor
                _bg.Color = _ed.BackgroundColor
                _wheel.Color = CurrentChip().Color
                _fg.Selected = Not _editBackground
                _bg.Selected = _editBackground
                _recent.Colors = Aqua.ColorPicker.SavedRecentColors()
                _custom.Colors = Aqua.ColorPicker.SavedCustomColors()
                For Each entry In {(_size, "dw_width"), (_opacity, "dw_opacity")}
                    Dim src = _ed.DrawRowSlider(entry.Item2)
                    If src IsNot Nothing Then entry.Item1.Value = src.Value
                Next
                _sizeValue.Text = _size.Value.ToString()
                _opacityValue.Text = _opacity.Value & "%"
                Dim brushes = _ed.QuickBrushes()
                Dim current = _ed._drawStyle.Brush
                For i = 0 To QuickBrushCount - 1
                    _brushTiles(i).SetBrush(brushes(i), brushes(i) = current AndAlso _ed.CurrentQuickTool() = QuickTool.Brush, _ed.ForegroundColor)
                Next
                Dim layers = _ed.QuickLayers()
                _layers.Items.Clear()
                _layers.Items.AddRange(layers.Select(Function(l) CObj(l.Name)).ToArray())
                _layers.SelectedIndex = layers.FindIndex(Function(l) l.Index = _ed.SelectedDrawIndex)
            Finally
                _syncing = False
            End Try
            Invalidate()
        End Sub

        Private Sub ApplyColors()
            BackColor = ThemeManager.Back(Color.FromArgb(246, 247, 249))
            ForeColor = ThemeManager.Fore(Color.FromArgb(40, 44, 52))
            For Each c As Control In Controls
                If TypeOf c Is Button Then
                    Dim b = DirectCast(c, Button)
                    b.BackColor = If(ThemeManager.Dark, ThemeManager.ButtonBack, Color.White)
                    b.ForeColor = ForeColor
                    b.FlatAppearance.BorderColor = ThemeManager.Line(Color.FromArgb(200, 205, 214))
                ElseIf TypeOf c Is Label Then
                    c.ForeColor = ForeColor
                    c.BackColor = BackColor
                ElseIf TypeOf c Is ComboBox Then
                    ThemeManager.Apply(c) ' 深色時改成自繪的深色下拉選單（同其他面板）
                End If
            Next
            _wheel.BackColor = BackColor
        End Sub

        Protected Overrides Sub OnVisibleChanged(e As EventArgs)
            MyBase.OnVisibleChanged(e)
            If Visible Then ApplyColors()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim line = ThemeManager.Line(Color.FromArgb(190, 196, 206))
            Dim sub2 = ThemeManager.Fore(Color.FromArgb(105, 110, 120))
            Dim accent = Color.FromArgb(47, 128, 237)
            Using head As New SolidBrush(ThemeManager.Back(Color.FromArgb(232, 235, 241)))
                g.FillRectangle(head, 0, 0, Width, HeaderH)
            End Using
            Using bold As New Font(_font, FontStyle.Bold), fg As New SolidBrush(ForeColor), s2 As New SolidBrush(sub2), small As New Font(_font.FontFamily, 8.5F)
                g.DrawString("快速操作", bold, fg, 12, 8)
                ' 關閉 ×
                Using p As New Pen(ForeColor, 1.6F)
                    g.DrawLine(p, Width - 24, 12, Width - 14, 22)
                    g.DrawLine(p, Width - 14, 12, Width - 24, 22)
                End Using
                g.DrawString("前景色", small, s2, 18, HeaderH + 74)
                g.DrawString("背景色", small, s2, 94, HeaderH + 74)
                g.DrawString("近期色票", small, s2, 288, HeaderH + 6)
                g.DrawString("常用色票", small, s2, 288, HeaderH + 52)
                g.DrawString("筆刷大小", _font, fg, 16, HeaderH + 154)
                g.DrawString("不透明度", _font, fg, 16, HeaderH + 186)
                g.DrawString("常用筆刷（右鍵可換）", _font, fg, 16, HeaderH + 220)
                g.DrawString("快速圖層", _font, fg, 16, _layers.Top + 4)
                Dim hint = $"{_ed._appSettings.QuickPanelHotkey} 開關　｜　{_ed._appSettings.QuickRadialHotkey} 輪盤　｜　Esc 關閉　｜　按兩下色塊開完整選色器"
                Using sf As New StringFormat With {.Alignment = StringAlignment.Center}
                    g.DrawString(hint, small, s2, New RectangleF(0, Height - 22, Width, 18), sf)
                End Using
            End Using
            Using p As New Pen(line)
                g.DrawLine(p, 12, HeaderH + 144, Width - 12, HeaderH + 144)
                g.DrawLine(p, 12, Height - 28, Width - 12, Height - 28)
            End Using
            Using p As New Pen(If(ThemeManager.Dark, Color.FromArgb(150, 160, 178), Color.FromArgb(130, 136, 150)), 2)
                g.DrawRectangle(p, 1, 1, Width - 2, Height - 2)
            End Using
        End Sub

        ' 標題列拖曳移動、× 關閉
        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Y > HeaderH Then Return
            If e.X > Width - 32 Then Hide() : Return
            _drag = e.Location
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If _drag.HasValue Then Location = New Point(Location.X + e.X - _drag.Value.X, Location.Y + e.Y - _drag.Value.Y)
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            _drag = Nothing
        End Sub

        ''' <summary>點到外面就收起（開選色器時例外）。</summary>
        Protected Overrides Sub OnDeactivate(e As EventArgs)
            MyBase.OnDeactivate(e)
            If Not _modal Then BeginInvoke(Sub() If Not _modal AndAlso Not ContainsFocus Then Hide())
        End Sub

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            If keyData = Keys.Escape OrElse keyData = ParseKey(_ed._appSettings.QuickPanelHotkey, Keys.F9) Then Hide() : Return True
            If keyData = ParseKey(_ed._appSettings.QuickRadialHotkey, Keys.F10) Then
                Hide()
                _ed.ToggleRadial(_ed._canvas.PointerPosition)
                Return True
            End If
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _font.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

    ''' <summary>前景／背景色塊；選中（色環改的是它）時有藍色外框。</summary>
    Friend NotInheritable Class ColorChip
        Inherits Control
        Private _color As Color = Color.Black
        Private _selected As Boolean
        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.StandardDoubleClick, True)
            Cursor = Cursors.Hand
        End Sub
        Public Property Color As Color
            Get
                Return _color
            End Get
            Set(value As Color)
                _color = value
                Invalidate()
            End Set
        End Property
        Public Property Selected As Boolean
            Get
                Return _selected
            End Get
            Set(value As Boolean)
                _selected = value
                Invalidate()
            End Set
        End Property
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Parent.BackColor)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim r As New Rectangle(3, 3, Width - 7, Height - 7)
            Using b As New SolidBrush(_color)
                g.FillRectangle(b, r)
            End Using
            Using p As New Pen(If(_selected, Color.FromArgb(47, 128, 237), Color.FromArgb(140, 146, 158)), If(_selected, 3, 1))
                g.DrawRectangle(p, r)
            End Using
        End Sub
    End Class

    ''' <summary>一排圓點色票（每列 7 格），點一下回報顏色。</summary>
    Friend NotInheritable Class SwatchRow
        Inherits Control
        Private _colors As Color() = {}
        Private _hot As Integer = -1
        Public Event Picked(c As Color)
        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        End Sub
        Public Property Colors As Color()
            Get
                Return _colors
            End Get
            Set(value As Color())
                _colors = If(value, {})
                Invalidate()
            End Set
        End Property
        Private ReadOnly Property Cell As Single
            Get
                Return Width / 7.0F
            End Get
        End Property
        Private Function DotRect(i As Integer) As RectangleF
            Dim c = Cell
            Return New RectangleF((i Mod 7) * c + 2, (i \ 7) * c + 2, c - 4, c - 4)
        End Function
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Parent.BackColor)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim count = Math.Max(_colors.Length, If(Height > Cell * 1.5F, 21, 7))
            For i = 0 To count - 1
                Dim r = DotRect(i)
                If i < _colors.Length AndAlso Not _colors(i).IsEmpty Then
                    Using b As New SolidBrush(_colors(i))
                        g.FillEllipse(b, r)
                    End Using
                    Using p As New Pen(If(i = _hot, Color.FromArgb(47, 128, 237), Color.FromArgb(120, 126, 138)), If(i = _hot, 2, 1))
                        g.DrawEllipse(p, r)
                    End Using
                Else
                    Using p As New Pen(Color.FromArgb(150, 156, 168)) With {.DashStyle = DashStyle.Dot}
                        g.DrawEllipse(p, r)
                    End Using
                End If
            Next
        End Sub
        Private Function HitTest(p As Point) As Integer
            For i = 0 To _colors.Length - 1
                If DotRect(i).Contains(p) AndAlso Not _colors(i).IsEmpty Then Return i
            Next
            Return -1
        End Function
        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim h = HitTest(e.Location)
            If h <> _hot Then _hot = h : Cursor = If(h >= 0, Cursors.Hand, Cursors.Default) : Invalidate()
        End Sub
        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hot = -1 : Invalidate()
        End Sub
        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            Dim i = HitTest(e.Location)
            If i >= 0 AndAlso e.Button = MouseButtons.Left Then RaiseEvent Picked(_colors(i))
        End Sub
    End Class

    ''' <summary>常用筆刷格：筆刷預覽＋名稱；目前用的那個有藍色外框。</summary>
    Friend NotInheritable Class BrushChip
        Inherits Control
        Private _preview As Bitmap
        Private _active As Boolean
        Public Property Brush As BrushKind
        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
            Cursor = Cursors.Hand
        End Sub
        Public Sub SetBrush(b As BrushKind, active As Boolean, color As Color)
            Brush = b
            _active = active
            If ThemeManager.Dark AndAlso color.GetBrightness() < 0.25 Then color = Color.FromArgb(210, 214, 222)
            _preview?.Dispose()
            _preview = DrawingRenderer.BrushPreview(b, FxKind.Fire, MaterialKind.Wood, Math.Max(20, Width - 12), 22, color)
            AccessibleName = DrawGeometry.BrushNames(CInt(b))
            Invalidate()
        End Sub
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(ThemeManager.Back(Color.White))
            If _preview IsNot Nothing Then g.DrawImage(_preview, 6, 4)
            TextRenderer.DrawText(g, DrawGeometry.BrushNames(CInt(Brush)), Font, New Rectangle(0, Height - 22, Width, 20), ThemeManager.Fore(Color.FromArgb(40, 44, 52)),
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Using p As New Pen(If(_active, Color.FromArgb(47, 128, 237), ThemeManager.Line(Color.FromArgb(200, 205, 214))), If(_active, 2, 1))
                g.DrawRectangle(p, If(_active, 1, 0), If(_active, 1, 0), Width - If(_active, 2, 1), Height - If(_active, 2, 1))
            End Using
        End Sub
        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _preview?.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

    '=====================================================================
    ' 輪盤
    '=====================================================================

    ''' <summary>
    ''' 圓形放射選單：8 格（筆刷、橡皮擦、吸管、填色、漸層、選取、文字、色彩），中間是目前的工具。
    ''' 移到格子上亮起來，點一下就選定並收起；點中間切換全螢幕繪圖；Esc 或再按熱鍵收起。
    ''' 移到「筆刷」格時外面展開一圈常用筆刷（8 個＋「更多…」），點一下換筆刷，右鍵換成別的筆刷；
    ''' 在輪盤上轉滾輪＝在常用筆刷間循環。
    ''' </summary>
    Friend NotInheritable Class RadialMenu
        Inherits Form

        ''' <summary>視窗大小（展開外圈時的直徑）；收起時只顯示中間直徑 300 的圓。</summary>
        Private Const Big As Integer = 480
        Private Const Small As Integer = 300
        Private Const Outer As Single = 146, Inner As Single = 54
        Private Const RingIn As Single = 152, RingOut As Single = 236
        Private Const RingSlots As Integer = QuickBrushCount + 1 ' 最後一格「更多…」
        Private Shared ReadOnly Items As (Tool As QuickTool, Name As String)() = {
            (QuickTool.Brush, "筆刷"), (QuickTool.Eraser, "橡皮擦"), (QuickTool.Eyedropper, "吸管"), (QuickTool.Bucket, "填色"),
            (QuickTool.Gradient, "漸層"), (QuickTool.SelectMove, "選取"), (QuickTool.Text, "文字"), (QuickTool.Color, "色彩")}
        Private ReadOnly _ed As frmEditor
        Private ReadOnly _font As New Font("Microsoft JhengHei UI", 9.5F)
        Private ReadOnly _small As New Font("Microsoft JhengHei UI", 8.5F)
        Private _hot As Integer = -1
        Private _hotRing As Integer = -1
        Private _hotCenter As Boolean
        Private _expanded As Boolean
        Private _menuOpen As Boolean
        Private _center As Point
        Private _brushes As BrushKind() = {}
        Private ReadOnly _previews As New List(Of Bitmap)()
        Private _previewColor As Color

        Public Sub New(ed As frmEditor)
            _ed = ed
            Owner = ed
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            KeyPreview = True
            DoubleBuffered = True
            ClientSize = New Size(Big, Big)
            Tag = ThemeManager.SkipTreeTag
            SetExpanded(False, force:=True)
        End Sub

        ''' <summary>輪盤中心在視窗裡的座標。</summary>
        Public ReadOnly Property CenterOffset As Integer
            Get
                Return Big \ 2
            End Get
        End Property

        Public ReadOnly Property Expanded As Boolean
            Get
                Return _expanded
            End Get
        End Property

        Public Sub ShowAt(p As Point)
            _center = p
            Dim wa = Screen.FromPoint(p).WorkingArea
            Location = New Point(Math.Max(wa.Left - (Big - Small) \ 2, Math.Min(wa.Right - (Big + Small) \ 2, p.X - Big \ 2)),
                                 Math.Max(wa.Top - (Big - Small) \ 2, Math.Min(wa.Bottom - (Big + Small) \ 2, p.Y - Big \ 2)))
            _hot = -1 : _hotRing = -1 : _hotCenter = False
            SetExpanded(False)
            RefreshBrushes()
            Show(_ed)
            Activate()
        End Sub

        ''' <summary>展開／收起外圈：視窗形狀跟著改，外圈以外的地方點得到底下的畫布。</summary>
        Private Sub SetExpanded(value As Boolean, Optional force As Boolean = False)
            If value = _expanded AndAlso Not force Then Return
            _expanded = value
            Dim d = If(value, Big, Small)
            Using path As New GraphicsPath()
                path.AddEllipse((Big - d) \ 2, (Big - d) \ 2, d, d)
                Dim old = Region
                Region = New Region(path)
                old?.Dispose()
            End Using
            Invalidate()
        End Sub

        ''' <summary>重新讀常用筆刷、畫筆觸樣張（用前景色）。</summary>
        Private Sub RefreshBrushes()
            _brushes = _ed.QuickBrushes()
            Dim col = _ed.ForegroundColor
            If ThemeManager.Dark AndAlso col.GetBrightness() < 0.25 Then col = Color.FromArgb(210, 214, 222)
            If Not ThemeManager.Dark AndAlso col.GetBrightness() > 0.92 Then col = Color.FromArgb(90, 96, 108)
            _previewColor = col
            For Each b In _previews
                b.Dispose()
            Next
            _previews.Clear()
            For Each b In _brushes
                _previews.Add(DrawingRenderer.BrushPreview(b, FxKind.Fire, MaterialKind.Wood, 70, 18, col))
            Next
        End Sub

        ''' <summary>格子 i 的角度：第 0 格（筆刷）在正左邊偏上，順時針排列。</summary>
        Private Shared Function SectorStart(i As Integer) As Single
            Return 180.0F - 22.5F + i * 45.0F
        End Function

        ''' <summary>外圈第 j 格的角度：第 0 格對著「筆刷」格，順時針排列。</summary>
        Private Shared Function SlotStart(j As Integer) As Single
            Return 180.0F - 180.0F / RingSlots + j * 360.0F / RingSlots
        End Function

        Private Function Polar(p As Point) As (R As Double, A As Double)
            Dim dx = p.X - Big / 2.0, dy = p.Y - Big / 2.0
            Return (Math.Sqrt(dx * dx + dy * dy), Math.Atan2(dy, dx) * 180 / Math.PI)
        End Function

        ''' <summary>滑鼠所在的格子；在中間、外圈或外面時 -1。</summary>
        Public Function HitSector(p As Point) As Integer
            Dim q = Polar(p)
            If q.R < Inner OrElse q.R > Outer + 4 Then Return -1
            Dim rel = (q.A - SectorStart(0) + 720) Mod 360
            Return CInt(Math.Floor(rel / 45)) Mod Items.Length
        End Function

        ''' <summary>外圈（展開時）滑鼠所在的格子：0..7 常用筆刷、8「更多…」；不在外圈時 -1。</summary>
        Public Function HitRing(p As Point) As Integer
            If Not _expanded Then Return -1
            Dim q = Polar(p)
            If q.R < Outer + 4 OrElse q.R > RingOut + 4 Then Return -1
            Dim rel = (q.A - SlotStart(0) + 720) Mod 360
            Return CInt(Math.Floor(rel / (360.0 / RingSlots))) Mod RingSlots
        End Function

        Private Function BrushIsCurrent(b As BrushKind) As Boolean
            Return _ed.CurrentQuickTool() = QuickTool.Brush AndAlso _ed.CurrentBrush = b
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim dark = ThemeManager.Dark
            Dim baseC = If(dark, Color.FromArgb(34, 38, 46), Color.FromArgb(246, 247, 249))
            Dim ringC = If(dark, Color.FromArgb(44, 49, 59), Color.FromArgb(232, 235, 241))
            Dim ring2C = If(dark, Color.FromArgb(38, 43, 52), Color.FromArgb(240, 242, 246))
            Dim activeC = If(dark, Color.FromArgb(40, 66, 110), Color.FromArgb(214, 228, 248))
            Dim fore = If(dark, Color.FromArgb(225, 229, 236), Color.FromArgb(40, 44, 52))
            Dim lineC = If(dark, Color.FromArgb(70, 76, 88), Color.FromArgb(200, 205, 214))
            Dim accent = Color.FromArgb(47, 128, 237)
            g.Clear(baseC)
            Dim c = Big / 2.0F
            Dim current = _ed.CurrentQuickTool()

            ' 外圈：常用筆刷＋更多
            If _expanded Then
                For j = 0 To RingSlots - 1
                    Using path = Sector(c, RingIn, RingOut, SlotStart(j), 360.0F / RingSlots)
                        Dim isCur = j < _brushes.Length AndAlso BrushIsCurrent(_brushes(j))
                        Using b As New SolidBrush(If(j = _hotRing, accent, If(isCur, activeC, ring2C)))
                            g.FillPath(b, path)
                        End Using
                        Using p As New Pen(lineC)
                            g.DrawPath(p, path)
                        End Using
                    End Using
                    Dim mid = (SlotStart(j) + 180.0 / RingSlots) * Math.PI / 180
                    Dim rr = (RingIn + RingOut) / 2
                    Dim ix = CSng(c + Math.Cos(mid) * rr), iy = CSng(c + Math.Sin(mid) * rr)
                    Dim col = If(j = _hotRing, Color.White, fore)
                    If j < _brushes.Length Then
                        If j = _hotRing Then
                            ' 藍底上看得清楚：樣張底下墊一塊淡色
                            Using b As New SolidBrush(Color.FromArgb(235, 245, 247, 250))
                                g.FillRectangle(b, ix - 38, iy - 22, 76, 22)
                            End Using
                        End If
                        g.DrawImage(_previews(j), ix - 35, iy - 20)
                        TextRenderer.DrawText(g, DrawGeometry.BrushNames(CInt(_brushes(j))), _small, New Rectangle(CInt(ix) - 45, CInt(iy) + 3, 90, 18), col,
                                              TextFormatFlags.HorizontalCenter)
                    Else
                        ' 「更多…」：九宮格圖示
                        Using b As New SolidBrush(col)
                            For k = 0 To 8
                                g.FillRectangle(b, ix - 10 + (k Mod 3) * 7, iy - 22 + (k \ 3) * 7, 5, 5)
                            Next
                        End Using
                        TextRenderer.DrawText(g, "更多…", _small, New Rectangle(CInt(ix) - 45, CInt(iy) + 3, 90, 18), col, TextFormatFlags.HorizontalCenter)
                    End If
                Next
            End If

            ' 內圈：工具
            For i = 0 To Items.Length - 1
                Using path = Sector(c, Inner, Outer, SectorStart(i), 45)
                    Dim open = i = 0 AndAlso _expanded
                    Dim fill = If(i = _hot OrElse (open AndAlso _hotRing >= 0), accent, If(Items(i).Tool = current, activeC, ringC))
                    Using b As New SolidBrush(fill)
                        g.FillPath(b, path)
                    End Using
                    Using p As New Pen(lineC)
                        g.DrawPath(p, path)
                    End Using
                End Using
                Dim mid = (SectorStart(i) + 22.5) * Math.PI / 180
                Dim rr = (Outer + Inner) / 2
                Dim ix = CSng(c + Math.Cos(mid) * rr), iy = CSng(c + Math.Sin(mid) * rr)
                Dim col = If(i = _hot OrElse (i = 0 AndAlso _expanded AndAlso _hotRing >= 0), Color.White, fore)
                DrawItemIcon(g, Items(i).Tool, New RectangleF(ix - 13, iy - 22, 26, 26), col)
                TextRenderer.DrawText(g, Items(i).Name, _font, New Rectangle(CInt(ix) - 40, CInt(iy) + 5, 80, 20), col, TextFormatFlags.HorizontalCenter)
                If i = 0 AndAlso Not _expanded Then
                    ' 「筆刷」格外緣的小箭頭：移過去會展開常用筆刷
                    Dim ax = CSng(c + Math.Cos(mid) * (Outer - 9)), ay = CSng(c + Math.Sin(mid) * (Outer - 9))
                    Using b As New SolidBrush(col)
                        g.FillPolygon(b, {New PointF(ax - 3, ay - 5), New PointF(ax - 3, ay + 5), New PointF(ax - 8, ay)})
                    End Using
                End If
            Next

            ' 中間：目前的工具；是筆刷時顯示筆刷樣張與名稱
            Using b As New SolidBrush(If(dark, Color.FromArgb(28, 31, 38), Color.White)), p As New Pen(accent, 2)
                g.FillEllipse(b, c - Inner + 4, c - Inner + 4, (Inner - 4) * 2, (Inner - 4) * 2)
                g.DrawEllipse(p, c - Inner + 4, c - Inner + 4, (Inner - 4) * 2, (Inner - 4) * 2)
            End Using
            If current = QuickTool.Brush Then
                Dim k = Array.IndexOf(_brushes, _ed.CurrentBrush)
                If k >= 0 Then
                    g.DrawImage(_previews(k), c - 35, c - 24)
                Else
                    DrawItemIcon(g, QuickTool.Brush, New RectangleF(c - 14, c - 26, 28, 28), accent)
                End If
                TextRenderer.DrawText(g, DrawGeometry.BrushNames(CInt(_ed.CurrentBrush)), _font, New Rectangle(CInt(c) - 44, CInt(c) + 2, 88, 20), fore, TextFormatFlags.HorizontalCenter)
                TextRenderer.DrawText(g, "滾輪換筆刷", _small, New Rectangle(CInt(c) - 44, CInt(c) + 22, 88, 16), If(dark, Color.FromArgb(150, 158, 172), Color.FromArgb(120, 126, 138)),
                                      TextFormatFlags.HorizontalCenter)
            Else
                Dim cur = Items.First(Function(x) x.Tool = current)
                DrawItemIcon(g, cur.Tool, New RectangleF(c - 14, c - 26, 28, 28), accent)
                TextRenderer.DrawText(g, cur.Name, _font, New Rectangle(CInt(c) - 40, CInt(c) + 4, 80, 20), fore, TextFormatFlags.HorizontalCenter)
            End If
            ' 中間上方的全螢幕圖示：點中間切換全螢幕（滑鼠在中間時變藍）
            Using p As New Pen(If(_hotCenter, accent, If(dark, Color.FromArgb(130, 138, 152), Color.FromArgb(150, 156, 168))), 1.6F)
                Dim x = c - 7, y = c - 45, s = 14.0F, k = 4.0F
                Dim inward = _ed.IsFullScreen ' 全螢幕中：往內的角（離開）；平常：往外的角（進入）
                For Each corner In {(x, y, 1, 1), (x + s, y, -1, 1), (x, y + s, 1, -1), (x + s, y + s, -1, -1)}
                    Dim cx = corner.Item1, cy = corner.Item2, dx = corner.Item3, dy = corner.Item4
                    If inward Then
                        g.DrawLines(p, {New PointF(cx + dx * k, cy), New PointF(cx + dx * k, cy + dy * k), New PointF(cx, cy + dy * k)})
                    Else
                        g.DrawLines(p, {New PointF(cx + dx * k, cy), New PointF(cx, cy), New PointF(cx, cy + dy * k)})
                    End If
                Next
            End Using
            Dim d = If(_expanded, Big, Small)
            Using p As New Pen(lineC, 2)
                g.DrawEllipse(p, (Big - d) / 2.0F + 1, (Big - d) / 2.0F + 1, d - 3, d - 3)
            End Using
        End Sub

        ''' <summary>環形扇區（中心 c、半徑 r1..r2、從 start 起 sweep 度）。</summary>
        Private Shared Function Sector(c As Single, r1 As Single, r2 As Single, start As Single, sweep As Single) As GraphicsPath
            Dim path As New GraphicsPath()
            path.AddArc(c - r2, c - r2, r2 * 2, r2 * 2, start, sweep)
            path.AddArc(c - r1, c - r1, r1 * 2, r1 * 2, start + sweep, -sweep)
            path.CloseFigure()
            Return path
        End Function

        ''' <summary>各工具的圖示：繪圖工具列有的用同一個圖示，其他自己畫。</summary>
        Private Shared Sub DrawItemIcon(g As Graphics, tool As QuickTool, r As RectangleF, col As Color)
            Select Case tool
                Case QuickTool.Brush : DrawIcons.DrawTool(g, CInt(DrawShape.Raster), r, col)
                Case QuickTool.Bucket : DrawIcons.DrawTool(g, CInt(DrawShape.Bucket), r, col)
                Case QuickTool.Gradient : DrawIcons.DrawTool(g, CInt(DrawShape.Gradient), r, col)
                Case QuickTool.SelectMove : DrawIcons.DrawTool(g, DrawIcons.SelectTool, r, col)
                Case QuickTool.Text
                    Using f As New Font("Times New Roman", r.Height * 0.62F, FontStyle.Bold, GraphicsUnit.Pixel), b As New SolidBrush(col),
                          sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                        g.DrawString("T", f, b, r, sf)
                    End Using
                Case QuickTool.Eraser
                    Dim st = g.Save()
                    g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2)
                    g.RotateTransform(-40)
                    Using p As New Pen(col, 1.8F), b As New SolidBrush(col)
                        g.DrawRectangle(p, -r.Width * 0.38F, -r.Height * 0.2F, r.Width * 0.76F, r.Height * 0.4F)
                        g.FillRectangle(b, -r.Width * 0.38F, -r.Height * 0.2F, r.Width * 0.3F, r.Height * 0.4F)
                    End Using
                    g.Restore(st)
                Case QuickTool.Eyedropper
                    Using p As New Pen(col, 2.2F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}, b As New SolidBrush(col)
                        g.DrawLine(p, r.X + r.Width * 0.18F, r.Bottom - r.Height * 0.18F, r.X + r.Width * 0.62F, r.Y + r.Height * 0.38F)
                        g.FillEllipse(b, r.X + r.Width * 0.56F, r.Y + r.Height * 0.08F, r.Width * 0.36F, r.Height * 0.36F)
                    End Using
                Case QuickTool.Color
                    Dim cols = {Color.FromArgb(235, 70, 60), Color.FromArgb(250, 200, 50), Color.FromArgb(70, 190, 110), Color.FromArgb(60, 130, 230)}
                    For k = 0 To 3
                        Dim a = k * Math.PI / 2 - Math.PI / 4
                        Using b As New SolidBrush(cols(k))
                            g.FillEllipse(b, CSng(r.X + r.Width / 2 + Math.Cos(a) * r.Width * 0.25 - r.Width * 0.17), CSng(r.Y + r.Height / 2 + Math.Sin(a) * r.Height * 0.25 - r.Height * 0.17),
                                          r.Width * 0.34F, r.Height * 0.34F)
                        End Using
                    Next
            End Select
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim h = HitSector(e.Location)
            ' 移到「筆刷」格展開外圈；移到其他工具格收起（在外圈、中間時維持原狀）
            If h = 0 Then
                SetExpanded(True)
            ElseIf h > 0 Then
                SetExpanded(False)
            End If
            Dim hr = HitRing(e.Location)
            Dim hc = Polar(e.Location).R < Inner
            If h <> _hot OrElse hr <> _hotRing OrElse hc <> _hotCenter Then _hot = h : _hotRing = hr : _hotCenter = hc : Invalidate()
            Cursor = If(h >= 0 OrElse hr >= 0 OrElse hc, Cursors.Hand, Cursors.Default)
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            If _hot >= 0 OrElse _hotRing >= 0 OrElse _hotCenter Then _hot = -1 : _hotRing = -1 : _hotCenter = False : Invalidate()
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            Dim hr = HitRing(e.Location)
            If e.Button = MouseButtons.Right AndAlso hr >= 0 AndAlso hr < _brushes.Length Then
                ' 右鍵：把這格換成別的筆刷
                _menuOpen = True
                _ed.ShowQuickBrushMenu(hr, Me, e.Location, Sub()
                                                               RefreshBrushes()
                                                               Invalidate()
                                                           End Sub,
                                       Sub()
                                           _menuOpen = False
                                           BeginInvoke(Sub() If Visible AndAlso Not ContainsFocus AndAlso Form.ActiveForm IsNot Me Then Activate())
                                       End Sub)
                Return
            End If
            If e.Button <> MouseButtons.Left Then Hide() : Return
            If hr >= 0 Then
                Hide()
                If hr < _brushes.Length Then _ed.UseQuickBrush(_brushes(hr)) Else _ed.ShowBrushBrowser(_center)
                Return
            End If
            Dim h = HitSector(e.Location)
            Hide()
            If h >= 0 Then
                _ed.SelectQuickTool(Items(h).Tool, _center)
            ElseIf Polar(e.Location).R < Inner Then
                _ed.ToggleFullScreen() ' 點中間：切換全螢幕
            End If
        End Sub

        ''' <summary>滾輪：在常用筆刷間循環（往下下一個、往上上一個），外圈跟著展開顯示。</summary>
        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            MyBase.OnMouseWheel(e)
            CycleBrush(If(e.Delta < 0, 1, -1))
        End Sub

        Public Sub CycleBrush(direction As Integer)
            If _brushes.Length = 0 Then Return
            Dim k = If(_ed.CurrentQuickTool() = QuickTool.Brush, Array.IndexOf(_brushes, _ed.CurrentBrush), -1)
            k = If(k < 0, If(direction > 0, 0, _brushes.Length - 1), (k + direction + _brushes.Length) Mod _brushes.Length)
            _ed.UseQuickBrush(_brushes(k))
            SetExpanded(True)
            Invalidate()
        End Sub

        Protected Overrides Sub OnDeactivate(e As EventArgs)
            MyBase.OnDeactivate(e)
            If Not _menuOpen Then Hide()
        End Sub

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            If keyData = Keys.Escape OrElse keyData = ParseKey(_ed._appSettings.QuickRadialHotkey, Keys.F10) Then Hide() : Return True
            If keyData = ParseKey(_ed._appSettings.QuickPanelHotkey, Keys.F9) Then
                Hide()
                _ed.ToggleQuickPanel(_ed._canvas.PointerPosition)
                Return True
            End If
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _font.Dispose()
                _small.Dispose()
                For Each b In _previews
                    b.Dispose()
                Next
            End If
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
