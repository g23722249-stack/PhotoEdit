Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 畫布左側的繪圖工具列（只在「繪圖」分頁顯示），兩欄排列、分四區：
''' 共用（選取、平移、縮放、吸管）、點陣（筆刷、橡皮擦、油漆桶、漸層、仿製／修復、塗抹／混色、模糊／銳利化、加亮／加深／海綿）、
''' 向量（節點、鋼筆、自由繪製、線條、矩形、橢圓、多邊形與星形、造型、圖說、文字、形狀建立、刀具）、
''' 色彩（線條色／填色方塊、交換、無填色、預設色、快速色盤、最近用過的顏色）。
''' 右下角有小三角的按鈕可以展開同類的工具（按住、右鍵或點小三角），選了之後按鈕換成那個工具。
''' 工具編號見 DrawIcons（負數是選取、節點、平移…；其餘是 DrawShape）。點陣的變化實際上都是「直接繪製」。
''' </summary>
Friend Class DrawToolStrip
    Inherits Panel

    Private Const ButtonSize As Integer = 30
    Private Const Gap As Integer = 3
    Private Const HeaderH As Integer = 15

    ''' <summary>一個按鈕位置：可以有好幾個同類工具（展開選）。</summary>
    Friend Class ToolSlot
        Public Variants As Integer()
        Public Current As Integer
        Public Button As ToolButton
        Public ReadOnly Property Tool As Integer
            Get
                Return Variants(Current)
            End Get
        End Property
    End Class

    Private ReadOnly _groups As New List(Of (Title As String, Slots As List(Of ToolSlot), Top As Integer))()
    Private ReadOnly _slots As New List(Of ToolSlot)()
    Private _selected As Integer = DrawIcons.SelectTool
    Private ReadOnly _flyout As New ContextMenuStrip With {.ShowImageMargin = True, .ShowCheckMargin = False}
    Private ReadOnly _colors As New ColorWell()
    Private _colorTop As Integer

    ''' <summary>選了工具（按鈕上的工具編號，點陣的變化是負數；引擎工具用 SelectedTool）。</summary>
    Public Event ToolSelected(tool As Integer)
    ''' <summary>色盤的顏色：target 0＝線條色（左鍵）、1＝填色（右鍵）。</summary>
    Public Event ColorPicked(target As Integer, argb As Integer)
    ''' <summary>點了線條色／填色方塊（開完整的色彩視窗）。</summary>
    Public Event ColorBoxClicked(target As Integer)
    Public Event SwapColors()
    Public Event NoFill()
    Public Event ResetColors()

    ''' <summary>按鈕換工具時更新說明文字（frmEditor 指定）。</summary>
    Public Property HelpSetter As Action(Of Control, String)

    Public Sub New()
        BackColor = Color.FromArgb(236, 238, 242)
        Width = Gap * 3 + ButtonSize * 2 + 8
        Padding = New Padding(4, 6, 4, 6)
        AutoScroll = True
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Dim S = Function(x As DrawShape) CInt(x)
        AddGroup(Lang.T("共用"), {V(DrawIcons.SelectTool), V(DrawIcons.PanTool), V(DrawIcons.ZoomTool), V(DrawIcons.EyedropperTool)})
        AddGroup(Lang.T("點陣"), {V(S(DrawShape.Raster)), V(DrawIcons.EraserTool), V(S(DrawShape.Bucket)), V(S(DrawShape.Gradient)),
                          V(DrawIcons.CloneTool, DrawIcons.HealTool), V(DrawIcons.SmudgeTool, DrawIcons.MixerTool),
                          V(DrawIcons.BlurTool, DrawIcons.SharpenTool),
                          V(DrawIcons.DodgeTool, DrawIcons.BurnTool, DrawIcons.SpongeMinusTool, DrawIcons.SpongePlusTool)})
        AddGroup(Lang.T("向量"), {V(DrawIcons.NodeTool), V(S(DrawShape.Path)), V(S(DrawShape.Freehand)), V(S(DrawShape.Line), S(DrawShape.Bezier)),
                          V(S(DrawShape.Rectangle), S(DrawShape.RoundRect)), V(S(DrawShape.Ellipse)),
                          V(S(DrawShape.Polygon), S(DrawShape.Star4), S(DrawShape.Star5), S(DrawShape.Star6)),
                          V(S(DrawShape.Arrow), S(DrawShape.Heart), S(DrawShape.Lightning)),
                          V(S(DrawShape.CalloutRect), S(DrawShape.CalloutEllipse), S(DrawShape.CalloutCloud), S(DrawShape.CalloutBubble), S(DrawShape.CalloutShout)),
                          V(S(DrawShape.Text)), V(DrawIcons.ShapeBuilderTool, DrawIcons.SmartFillTool), V(DrawIcons.ShadowTool, DrawIcons.TransparencyTool, DrawIcons.EnvelopeTool), V(DrawIcons.KnifeTool)})
        Dim y = 4
        For gi = 0 To _groups.Count - 1
            Dim grp = _groups(gi)
            _groups(gi) = (grp.Title, grp.Slots, y)
            y += HeaderH
            For i = 0 To grp.Slots.Count - 1
                grp.Slots(i).Button.SetBounds(4 + Gap + (i Mod 2) * (ButtonSize + Gap), y + (i \ 2) * (ButtonSize + Gap), ButtonSize, ButtonSize)
            Next
            y += ((grp.Slots.Count + 1) \ 2) * (ButtonSize + Gap) + 2
        Next
        _colorTop = y
        _colors.SetBounds(2, y + HeaderH, Width - 4, _colors.PreferredHeight)
        AddHandler _colors.Picked, Sub(t, c) RaiseEvent ColorPicked(t, c)
        AddHandler _colors.BoxClicked, Sub(t) RaiseEvent ColorBoxClicked(t)
        AddHandler _colors.Swap, Sub() RaiseEvent SwapColors()
        AddHandler _colors.NoFill, Sub() RaiseEvent NoFill()
        AddHandler _colors.Reset, Sub() RaiseEvent ResetColors()
        Controls.Add(_colors)
        UpdateButtons()
    End Sub

    Private Shared Function V(ParamArray tools As Integer()) As Integer()
        Return tools
    End Function

    Private Sub AddGroup(title As String, slots As IEnumerable(Of Integer()))
        Dim list As New List(Of ToolSlot)()
        For Each vs In slots
            Dim slot As New ToolSlot With {.Variants = vs}
            slot.Button = New ToolButton(slot)
            AddHandler slot.Button.Click, Sub(s, e) ChooseTool(DirectCast(s, ToolButton).Slot.Tool)
            AddHandler slot.Button.FlyoutRequested, Sub(b) ShowFlyout(b)
            list.Add(slot)
            _slots.Add(slot)
            Controls.Add(slot.Button)
        Next
        _groups.Add((title, list, 0))
    End Sub

    Public ReadOnly Property Buttons As IReadOnlyList(Of Control)
        Get
            Return _slots.Select(Function(s) CType(s.Button, Control)).ToList()
        End Get
    End Property

    ''' <summary>目前的引擎工具（點陣的變化都是直接繪製；-1 選取，其餘見 DrawIcons、DrawShape）。設定時可以給按鈕的工具編號。</summary>
    Public Property SelectedTool As Integer
        Get
            Return DrawIcons.EngineTool(_selected)
        End Get
        Set(value As Integer)
            If _selected = value Then Return
            ' 已經是點陣的某個變化時，程式要求「直接繪製」不改變化（例如換筆刷時）
            If value = CInt(DrawShape.Raster) AndAlso DrawIcons.IsRasterVariant(_selected) Then Return
            Highlight(value)
            RaiseEvent ToolSelected(value)
        End Set
    End Property

    ''' <summary>使用者點了按鈕（或展開選單）：一定換成這個工具（例如從橡皮擦換回筆刷）。</summary>
    Public Sub ChooseTool(tool As Integer)
        If _selected = tool Then Return
        Highlight(tool)
        RaiseEvent ToolSelected(tool)
    End Sub

    ''' <summary>按鈕上的工具編號（點陣的變化是負數，例如橡皮擦）。</summary>
    Public ReadOnly Property SelectedId As Integer
        Get
            Return _selected
        End Get
    End Property

    ''' <summary>只改反白的按鈕（不發出事件），例如按 E 切換橡皮擦、在筆刷面板選了仿製筆時同步。</summary>
    Public Sub Highlight(tool As Integer)
        _selected = tool
        For Each s In _slots
            Dim i = Array.IndexOf(s.Variants, tool)
            If i >= 0 AndAlso i <> s.Current Then
                s.Current = i
                HelpSetter?.Invoke(s.Button, DrawIcons.HelpKey(tool))
                s.Button.UpdateName()
            End If
        Next
        UpdateButtons()
    End Sub

    Private Sub UpdateButtons()
        For Each s In _slots
            s.Button.Selected = s.Tool = _selected
        Next
    End Sub

    Private Sub ShowFlyout(b As ToolButton)
        If b.Slot.Variants.Length < 2 Then Return
        For Each it As ToolStripItem In _flyout.Items
            it.Image?.Dispose()
        Next
        _flyout.Items.Clear()
        For Each tv In b.Slot.Variants
            Dim tool = tv
            Dim img As New Bitmap(20, 20)
            Using g = Graphics.FromImage(img)
                DrawIcons.DrawTool(g, tool, New RectangleF(0, 0, 20, 20), Color.FromArgb(50, 54, 62))
            End Using
            Dim mi = New ToolStripMenuItem(DrawIcons.ToolName(tool), img, Sub() ChooseTool(tool))
            If tool = b.Slot.Tool Then mi.Font = New Font(mi.Font, FontStyle.Bold)
            _flyout.Items.Add(mi)
        Next
        _flyout.Show(b, New Point(b.Width, 0))
    End Sub

    ''' <summary>色彩面板顯示的顏色（跟右側面板的線條色、填色同步）。</summary>
    Public Sub SetColors(stroke As Integer, fill As Integer, filled As Boolean)
        _colors.SetColors(stroke, fill, filled)
    End Sub

    Public Sub SetRecent(colors As IEnumerable(Of Color))
        _colors.SetRecent(colors)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y)
        Using pen As New Pen(ThemeManager.Line(Color.FromArgb(200, 205, 214))), f As New Font(Font.FontFamily, 7.5F)
            Dim fore = ThemeManager.Fore(Color.FromArgb(110, 116, 126))
            For Each grp In _groups.Select(Function(x) (x.Title, x.Top)).Concat({(Lang.T("色彩"), _colorTop)})
                Dim sz = TextRenderer.MeasureText(grp.Title, f)
                TextRenderer.DrawText(g, grp.Title, f, New Point(5, grp.Top), fore)
                g.DrawLine(pen, 7 + sz.Width - 4, grp.Top + 7, Width - 6, grp.Top + 7)
            Next
        End Using
        g.ResetTransform()
        Using pen As New Pen(ThemeManager.Line(Color.FromArgb(200, 205, 214)))
            g.DrawLine(pen, Width - 1, 0, Width - 1, Height)
        End Using
    End Sub

    Protected Overrides Sub OnScroll(se As ScrollEventArgs)
        MyBase.OnScroll(se)
        Invalidate()
    End Sub

    '=====================================================================
    ' 工具按鈕
    '=====================================================================

    Friend Class ToolButton
        Inherits Control

        Public ReadOnly Slot As ToolSlot
        Private _selected As Boolean
        Private _hover As Boolean
        Private ReadOnly _hold As New Timer With {.Interval = 380}
        Private _flyoutShown As Boolean

        ''' <summary>按住、右鍵或點右下角小三角：展開同類工具。</summary>
        Public Event FlyoutRequested(b As ToolButton)

        Public Sub New(slot As ToolSlot)
            Me.Slot = slot
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
            Cursor = Cursors.Hand
            AccessibleRole = AccessibleRole.RadioButton
            UpdateName()
            AddHandler _hold.Tick, Sub()
                                       _hold.Stop()
                                       _flyoutShown = True
                                       RaiseEvent FlyoutRequested(Me)
                                   End Sub
        End Sub

        ''' <summary>相容舊的寫法：按鈕目前的工具。</summary>
        Public ReadOnly Property Tool As Integer
            Get
                Return Slot.Tool
            End Get
        End Property

        Public Sub UpdateName()
            AccessibleName = DrawIcons.ToolName(Slot.Tool)
            Invalidate()
        End Sub

        Public Property Selected As Boolean
            Get
                Return _selected
            End Get
            Set(value As Boolean)
                If _selected = value Then Return
                _selected = value
                Invalidate()
            End Set
        End Property

        Private ReadOnly Property HasFlyout As Boolean
            Get
                Return Slot.Variants.Length > 1
            End Get
        End Property

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            _flyoutShown = False
            If Not HasFlyout Then Return
            If e.Button = MouseButtons.Right OrElse (e.Button = MouseButtons.Left AndAlso e.X >= Width - 10 AndAlso e.Y >= Height - 10) Then
                _flyoutShown = True
                RaiseEvent FlyoutRequested(Me)
            ElseIf e.Button = MouseButtons.Left Then
                _hold.Start()
            End If
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            _hold.Stop()
            MyBase.OnMouseUp(e)
        End Sub

        Protected Overrides Sub OnClick(e As EventArgs)
            If _flyoutShown Then Return ' 已經展開選單：不算點選
            If TypeOf e Is MouseEventArgs AndAlso DirectCast(e, MouseEventArgs).Button <> MouseButtons.Left Then Return
            MyBase.OnClick(e)
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Parent.BackColor)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim r = New Rectangle(0, 0, Width - 1, Height - 1)
            If _selected OrElse _hover Then
                Using br As New SolidBrush(ThemeManager.Back(If(_selected, Color.FromArgb(210, 228, 250), Color.FromArgb(226, 231, 238)))),
                      pen As New Pen(If(_selected, Color.FromArgb(90, 140, 220), ThemeManager.Line(Color.FromArgb(190, 198, 210))))
                    g.FillRectangle(br, r)
                    g.DrawRectangle(pen, r)
                End Using
            End If
            Dim iconColor = ThemeManager.Fore(If(_selected, Color.FromArgb(24, 95, 165), Color.FromArgb(50, 54, 62)))
            DrawIcons.DrawTool(g, Slot.Tool, New RectangleF(4, 4, Width - 8, Height - 8), iconColor)
            If HasFlyout Then
                Using br As New SolidBrush(ThemeManager.Fore(Color.FromArgb(110, 116, 126)))
                    g.FillPolygon(br, {New Point(Width - 2, Height - 7), New Point(Width - 2, Height - 2), New Point(Width - 7, Height - 2)})
                End Using
            End If
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            _hover = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = False
            _hold.Stop()
            Invalidate()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _hold.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

    '=====================================================================
    ' 色彩面板
    '=====================================================================

    ''' <summary>
    ''' 傳統的色彩面板：前面方塊＝線條色、後面方塊＝填色（沒有填色時畫紅色斜線）；右上交換、左下無填色、右下預設黑白；
    ''' 下面 16 色快速色盤與最近用過的 8 色：左鍵設線條色、右鍵設填色。
    ''' </summary>
    Friend Class ColorWell
        Inherits Control

        Private Shared ReadOnly Palette As Integer() = {
            &HFF000000, &HFFFFFFFF, &HFF7F7F7F, &HFFC3C3C3,
            &HFFE8455A, &HFFFF7F27, &HFFFFC90E, &HFF22B14C,
            &HFF00A2E8, &HFF3F48CC, &HFFA349A4, &HFFB97A57,
            &HFFFFAEC9, &HFFB5E61D, &HFF99D9EA, &HFF7092BE}
        Private Const Cell As Integer = 14
        Private Const CellGap As Integer = 2
        Private Const Cols As Integer = 4

        Private _stroke As Integer = Color.Black.ToArgb()
        Private _fill As Integer = Color.White.ToArgb()
        Private _filled As Boolean = True
        Private _recent As Integer() = {}

        Public Event Picked(target As Integer, argb As Integer)
        Public Event BoxClicked(target As Integer)
        Public Event Swap()
        Public Event NoFill()
        Public Event Reset()

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Cursor = Cursors.Hand
            AccessibleName = Lang.T("色彩")
        End Sub

        Public ReadOnly Property PreferredHeight As Integer
            Get
                Return 48 + 4 * (Cell + CellGap) + 16 + 2 * (Cell + CellGap) + 2
            End Get
        End Property

        Public Sub SetColors(stroke As Integer, fill As Integer, filled As Boolean)
            If _stroke = stroke AndAlso _fill = fill AndAlso _filled = filled Then Return
            _stroke = stroke : _fill = fill : _filled = filled
            Invalidate()
        End Sub

        Public Sub SetRecent(colors As IEnumerable(Of Color))
            _recent = colors.Take(8).Select(Function(c) c.ToArgb()).ToArray()
            Invalidate()
        End Sub

        ' 版面（相對這個控制項）
        Private ReadOnly Property StrokeBox As Rectangle
            Get
                Return New Rectangle(6, 2, 26, 26)
            End Get
        End Property
        Private ReadOnly Property FillBox As Rectangle
            Get
                Return New Rectangle(20, 16, 26, 26)
            End Get
        End Property
        Private ReadOnly Property SwapBox As Rectangle
            Get
                Return New Rectangle(Width - 20, 1, 16, 14)
            End Get
        End Property
        Private ReadOnly Property NoFillBox As Rectangle
            Get
                Return New Rectangle(4, 31, 12, 12)
            End Get
        End Property
        Private ReadOnly Property ResetBox As Rectangle
            Get
                Return New Rectangle(Width - 18, 30, 14, 14)
            End Get
        End Property
        Private ReadOnly Property GridLeft As Integer
            Get
                Return (Width - (Cols * Cell + (Cols - 1) * CellGap)) \ 2
            End Get
        End Property
        Private Function CellRect(index As Integer, top As Integer) As Rectangle
            Return New Rectangle(GridLeft + (index Mod Cols) * (Cell + CellGap), top + (index \ Cols) * (Cell + CellGap), Cell, Cell)
        End Function
        Private ReadOnly Property PaletteTop As Integer
            Get
                Return 48
            End Get
        End Property
        Private ReadOnly Property RecentTop As Integer
            Get
                Return PaletteTop + 4 * (Cell + CellGap) + 16
            End Get
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Parent.BackColor)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim line = ThemeManager.Line(Color.FromArgb(120, 126, 136))
            Using pen As New Pen(line), white As New Pen(Color.White, 2)
                ' 填色（後面）
                Dim fb = FillBox
                If _filled Then
                    Using br As New SolidBrush(Color.FromArgb(255, Color.FromArgb(_fill)))
                        g.FillRectangle(br, fb)
                    End Using
                Else
                    g.FillRectangle(Brushes.White, fb)
                    Using red As New Pen(Color.FromArgb(220, 50, 50), 2)
                        g.DrawLine(red, fb.Left + 2, fb.Bottom - 2, fb.Right - 2, fb.Top + 2)
                    End Using
                End If
                g.DrawRectangle(pen, fb)
                ' 線條色（前面）
                Dim sb = StrokeBox
                Using br As New SolidBrush(Color.FromArgb(255, Color.FromArgb(_stroke)))
                    g.FillRectangle(br, sb)
                End Using
                g.DrawRectangle(white, sb.X + 1, sb.Y + 1, sb.Width - 2, sb.Height - 2)
                g.DrawRectangle(pen, sb)
                ' 交換箭頭
                Dim sw = SwapBox
                Using arrow As New Pen(line, 1.3F), cap As New AdjustableArrowCap(3, 3)
                    arrow.CustomStartCap = cap
                    arrow.CustomEndCap = cap
                    g.DrawBezier(arrow, sw.X + 2, sw.Bottom - 1, sw.X + 2, sw.Y + 4, sw.X + 5, sw.Y + 2, sw.Right - 1, sw.Y + 2)
                End Using
                ' 無填色
                Dim nf = NoFillBox
                g.FillRectangle(Brushes.White, nf)
                g.DrawRectangle(pen, nf)
                Using red As New Pen(Color.FromArgb(220, 50, 50), 1.5F)
                    g.DrawLine(red, nf.Left + 1, nf.Bottom - 1, nf.Right - 1, nf.Top + 1)
                End Using
                ' 預設黑白
                Dim rb = ResetBox
                g.FillRectangle(Brushes.White, rb.X + 5, rb.Y + 5, 9, 9) : g.DrawRectangle(pen, rb.X + 5, rb.Y + 5, 9, 9)
                g.FillRectangle(Brushes.Black, rb.X, rb.Y, 9, 9) : g.DrawRectangle(pen, rb.X, rb.Y, 9, 9)
                ' 色盤
                For i = 0 To Palette.Length - 1
                    DrawCell(g, pen, CellRect(i, PaletteTop), Palette(i))
                Next
                Using f As New Font(Font.FontFamily, 7.5F)
                    TextRenderer.DrawText(g, Lang.T("最近"), f, New Point(GridLeft - 2, RecentTop - 15), ThemeManager.Fore(Color.FromArgb(110, 116, 126)))
                End Using
                For i = 0 To 7
                    Dim r = CellRect(i, RecentTop)
                    If i < _recent.Length Then
                        DrawCell(g, pen, r, _recent(i))
                    Else
                        Using dash As New Pen(ThemeManager.Line(Color.FromArgb(200, 205, 214))) With {.DashStyle = DashStyle.Dot}
                            g.DrawRectangle(dash, r)
                        End Using
                    End If
                Next
            End Using
        End Sub

        Private Shared Sub DrawCell(g As Graphics, pen As Pen, r As Rectangle, argb As Integer)
            Using br As New SolidBrush(Color.FromArgb(255, Color.FromArgb(argb)))
                g.FillRectangle(br, r)
            End Using
            g.DrawRectangle(pen, r)
        End Sub

        ''' <summary>點到的色塊顏色；沒有時 Nothing。</summary>
        Private Function CellAt(p As Point) As Integer?
            For i = 0 To Palette.Length - 1
                If CellRect(i, PaletteTop).Contains(p) Then Return Palette(i)
            Next
            For i = 0 To _recent.Length - 1
                If CellRect(i, RecentTop).Contains(p) Then Return _recent(i)
            Next
            Return Nothing
        End Function

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Dim c = CellAt(e.Location)
            If c.HasValue Then
                RaiseEvent Picked(If(e.Button = MouseButtons.Right, 1, 0), c.Value)
            ElseIf SwapBox.Contains(e.Location) Then
                RaiseEvent Swap()
            ElseIf NoFillBox.Contains(e.Location) Then
                RaiseEvent NoFill()
            ElseIf ResetBox.Contains(e.Location) Then
                RaiseEvent Reset()
            ElseIf StrokeBox.Contains(e.Location) Then
                RaiseEvent BoxClicked(0)
            ElseIf FillBox.Contains(e.Location) Then
                RaiseEvent BoxClicked(1)
            End If
        End Sub
    End Class
End Class
