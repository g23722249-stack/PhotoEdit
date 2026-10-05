Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>
''' 控制項的使用說明視窗（樣式沿用 iPhoto.Net 的 HelpTip）：滑鼠停在控制項上 0.5 秒後，
''' 在下方顯示一個小視窗：圖示、標題、說明，以及一行藍色的提示（快捷鍵、操作技巧）。
'''
'''   _help.SetHelp("btn.auto", button)                       ' 說明文字放在 HelpTexts
'''   _help.SetHelp("row.exposure", slider, caption, value)   ' 一組控制項共用一則說明
'''   _help.SetHelp(tile, New HelpTip.Entry With {...})       ' 動態產生的控制項
'''
''' 離開、按下滑鼠或 20 秒後隱藏。控制項停用時也會出現（停用的控制項收不到滑鼠事件，改看父容器的
''' MouseMove），並以橘色顯示 DisabledHint──現在為什麼不能用。視窗不搶焦點，且不會超出螢幕工作區。
''' </summary>
Friend Class HelpTip
    Implements IDisposable

    ''' <summary>一則說明。</summary>
    Public Class Entry
        Public Title As String = ""
        Public Text As String = ""
        ''' <summary>說明下方較小的一行（技巧、快捷鍵、條件）；可省略。</summary>
        Public Hint As String = ""
        ''' <summary>控制項停用時取代 Hint 顯示；可省略。</summary>
        Public DisabledHint As String = ""
        ''' <summary>圖示；沒有時改畫 Glyph 字樣的圓角徽章。</summary>
        Public Icon As Image
        ''' <summary>徽章上的字（通常是分頁名稱的第一個字）；空字串則不畫圖示。</summary>
        Public Glyph As String = ""

        Public Function Clone() As Entry
            Return DirectCast(MemberwiseClone(), Entry)
        End Function
    End Class

    Public Property InitialDelay As Integer = 500
    Public Property AutoPopDelay As Integer = 20000
    ''' <summary>False 時完全不顯示（「說明 → 顯示使用說明」可切換）。</summary>
    Public Property Active As Boolean = True

    Private ReadOnly _entries As New Dictionary(Of Control, Entry)()
    ''' <summary>依滑鼠位置決定說明的控制項（例如分頁標籤列）。</summary>
    Private ReadOnly _dynamic As New Dictionary(Of Control, Func(Of Point, Entry))()
    ''' <summary>控制項 → 判斷「是否停用」要看的控制項（滑桿列的名稱與數值跟著滑桿）。</summary>
    Private ReadOnly _enabledSource As New Dictionary(Of Control, Control)()
    ''' <summary>每個掛上事件的控制項 → 它顯示誰的說明（複合控制項的子控制項顯示外層的說明）。</summary>
    Private ReadOnly _owner As New Dictionary(Of Control, Control)()
    Private ReadOnly _parents As New HashSet(Of Control)()
    Private ReadOnly _window As New HelpWindow()
    Private WithEvents _showTimer As New Timer()
    Private WithEvents _hideTimer As New Timer()

    Private _hoverControl As Control
    Private _hoverEntry As Entry        ' 動態說明：目前滑鼠所在位置的那則
    Private _hoverAtCursor As Boolean    ' 動態說明：視窗放在游標下方，而非控制項下方
    Private _clickedControl As Control   ' 動態說明：按過之後，同一則說明不再出現，直到移到別處
    Private _clickedEntry As Entry

    '=====================================================================
    ' 註冊
    '=====================================================================

    Public Sub SetHelp(c As Control, e As Entry, Optional enabledSource As Control = Nothing)
        If c Is Nothing Then Return
        If e Is Nothing Then
            _entries.Remove(c)
            Return
        End If
        If Not _entries.ContainsKey(c) AndAlso Not _dynamic.ContainsKey(c) Then Register(c)
        _entries(c) = e
        If enabledSource IsNot Nothing Then _enabledSource(c) = enabledSource
    End Sub

    ''' <summary>HelpTexts 裡 <paramref name="key"/> 的說明套到每個控制項；找不到的鍵略過。</summary>
    Public Sub SetHelp(key As String, ParamArray controls As Control())
        SetHelpLinked(key, Nothing, controls)
    End Sub

    ''' <summary>同 SetHelp，但「停用」狀態看 <paramref name="enabledSource"/>（例如滑桿列的名稱跟著滑桿）。</summary>
    Public Sub SetHelpLinked(key As String, enabledSource As Control, ParamArray controls As Control())
        Dim e = HelpTexts.Get(key)
        If e Is Nothing Then Return
        For Each c In controls
            If c IsNot Nothing Then SetHelp(c, e, If(enabledSource Is c, Nothing, enabledSource))
        Next
    End Sub

    ''' <summary>依滑鼠位置（控制項座標）決定說明；回傳 Nothing 表示該處沒有說明。</summary>
    Public Sub SetDynamicHelp(c As Control, resolver As Func(Of Point, Entry))
        If c Is Nothing OrElse resolver Is Nothing Then Return
        If Not _entries.ContainsKey(c) AndAlso Not _dynamic.ContainsKey(c) Then Register(c, withChildren:=False)
        _dynamic(c) = resolver
        AddHandler c.MouseMove, AddressOf Dynamic_MouseMove
    End Sub

    ''' <param name="withChildren">False：子控制項不算這個控制項的（分頁控制項的子控制項是各分頁，有自己的說明）。</param>
    Private Sub Register(c As Control, Optional withChildren As Boolean = True)
        _registered.Add(c)
        Hook(c, c, withChildren)
        WatchParent(c.Parent)
        AddHandler c.ParentChanged, Sub(s, a) WatchParent(DirectCast(s, Control).Parent)
        AddHandler c.Disposed, Sub(s, a) Forget(DirectCast(s, Control))
    End Sub

    Private ReadOnly _registered As New HashSet(Of Control)()
    Private ReadOnly _hooked As New HashSet(Of Control)()

    ''' <summary>控制項被釋放（例如重新整理貼圖時的格子）就移除，避免一直累積。</summary>
    Private Sub Forget(c As Control)
        _registered.Remove(c)
        _entries.Remove(c)
        _dynamic.Remove(c)
        _enabledSource.Remove(c)
        For Each k In _owner.Where(Function(kv) kv.Value Is c).Select(Function(kv) kv.Key).ToList()
            _owner.Remove(k)
            _hooked.Remove(k)
        Next
        If _hoverControl Is c Then Unhover()
    End Sub

    ''' <summary><paramref name="ctrl"/> 與它的子控制項（含之後加入的）的滑鼠事件都算 <paramref name="owner"/> 的；
    ''' 自己有說明的子控制項除外。</summary>
    Private Sub Hook(ctrl As Control, owner As Control, withChildren As Boolean)
        If ctrl IsNot owner AndAlso _registered.Contains(ctrl) Then Return
        _owner(ctrl) = owner
        If _hooked.Add(ctrl) Then
            AddHandler ctrl.MouseEnter, AddressOf Control_MouseEnter
            AddHandler ctrl.MouseLeave, AddressOf Control_MouseLeave
            AddHandler ctrl.MouseDown, AddressOf Control_MouseDown
            If withChildren Then
                AddHandler ctrl.ControlAdded, Sub(s, a)
                                                  Dim o As Control = Nothing
                                                  If _owner.TryGetValue(ctrl, o) Then Hook(a.Control, o, True)
                                              End Sub
            End If
        End If
        If withChildren Then
            For Each child As Control In ctrl.Controls
                Hook(child, owner, True)
            Next
        End If
    End Sub

    ''' <summary>停用的控制項收不到滑鼠事件：由父容器的 MouseMove 找出來。</summary>
    Private Sub WatchParent(p As Control)
        If p Is Nothing OrElse _parents.Contains(p) Then Return
        _parents.Add(p)
        AddHandler p.MouseMove, AddressOf Parent_MouseMove
        AddHandler p.MouseLeave, AddressOf Parent_MouseLeave
    End Sub

    '=====================================================================
    ' 滑鼠
    '=====================================================================

    Private Sub Control_MouseEnter(sender As Object, e As EventArgs)
        Dim owner As Control = Nothing
        If Not _owner.TryGetValue(DirectCast(sender, Control), owner) Then Return
        If _hoverControl Is owner Then Return ' 從控制項的一部分移到另一部分
        If _dynamic.ContainsKey(owner) Then Return ' 由 MouseMove 依位置決定
        Hover(owner, Nothing, False)
    End Sub

    Private Sub Control_MouseLeave(sender As Object, e As EventArgs)
        Dim owner As Control = Nothing
        If Not _owner.TryGetValue(DirectCast(sender, Control), owner) Then Return
        If owner Is _clickedControl Then _clickedControl = Nothing
        If _hoverControl IsNot owner Then Return
        ' 滑鼠還在控制項範圍內（移進它的子控制項）：保留說明
        If owner.IsHandleCreated AndAlso owner.RectangleToScreen(owner.ClientRectangle).Contains(Cursor.Position) AndAlso
           Not _dynamic.ContainsKey(owner) Then Return
        Unhover()
    End Sub

    Private Sub Control_MouseDown(sender As Object, e As MouseEventArgs)
        Dim owner As Control = Nothing
        If _owner.TryGetValue(DirectCast(sender, Control), owner) AndAlso _dynamic.ContainsKey(owner) AndAlso owner Is _hoverControl Then
            _clickedControl = owner
            _clickedEntry = _hoverEntry
        End If
        Unhover()
    End Sub

    Private Sub Dynamic_MouseMove(sender As Object, e As MouseEventArgs)
        Dim c = DirectCast(sender, Control)
        Dim resolver As Func(Of Point, Entry) = Nothing
        If Not _dynamic.TryGetValue(c, resolver) Then Return
        Dim entry = resolver(e.Location)
        If c Is _clickedControl Then
            If entry Is _clickedEntry Then Return
            _clickedControl = Nothing
        End If
        If entry Is Nothing Then
            If _hoverControl Is c Then Unhover()
        ElseIf _hoverControl IsNot c OrElse _hoverEntry IsNot entry Then
            Hover(c, entry, True)
        End If
    End Sub

    Private Sub Parent_MouseMove(sender As Object, e As MouseEventArgs)
        Dim p = DirectCast(sender, Control)
        Dim child = p.GetChildAtPoint(e.Location, GetChildAtPointSkip.Invisible Or GetChildAtPointSkip.Transparent)
        If child IsNot Nothing AndAlso Not child.Enabled AndAlso _entries.ContainsKey(child) Then
            If _hoverControl IsNot child Then Hover(child, Nothing, False)
        ElseIf _hoverControl IsNot Nothing AndAlso Not _hoverControl.Enabled Then
            Unhover()
        End If
    End Sub

    Private Sub Parent_MouseLeave(sender As Object, e As EventArgs)
        If _hoverControl IsNot Nothing AndAlso Not _hoverControl.Enabled Then Unhover()
    End Sub

    Private Sub Hover(c As Control, dynamicEntry As Entry, atCursor As Boolean)
        Dim wasShowing = _window.Visible
        Unhover()
        If Not Active Then Return
        _hoverControl = c
        _hoverEntry = dynamicEntry
        _hoverAtCursor = atCursor
        ' 說明已經開著時（例如在分頁標籤之間移動）立刻換內容，不再等待。
        _showTimer.Interval = If(wasShowing AndAlso atCursor, 1, Math.Max(1, InitialDelay))
        _showTimer.Start()
    End Sub

    Private Sub Unhover()
        _showTimer.Stop()
        _hideTimer.Stop()
        _hoverControl = Nothing
        _hoverEntry = Nothing
        If _window.Visible Then _window.Hide()
    End Sub

    Private Sub ShowTimer_Tick(sender As Object, e As EventArgs) Handles _showTimer.Tick
        _showTimer.Stop()
        Dim c = _hoverControl
        If c Is Nothing OrElse Not c.Visible OrElse Not c.IsHandleCreated Then Return
        Dim entry = _hoverEntry
        If entry Is Nothing AndAlso Not _entries.TryGetValue(c, entry) Then Return
        Dim source As Control = Nothing
        Dim disabled = Not c.Enabled OrElse (_enabledSource.TryGetValue(c, source) AndAlso Not source.Enabled)
        Dim anchor As Rectangle
        If _hoverAtCursor Then
            Dim p = Cursor.Position
            anchor = New Rectangle(p.X - 8, p.Y - 8, 16, 24)
        Else
            anchor = c.RectangleToScreen(c.ClientRectangle)
        End If
        _window.ShowEntry(entry, disabled, anchor)
        _hideTimer.Interval = Math.Max(1000, AutoPopDelay)
        _hideTimer.Start()
    End Sub

    Private Sub HideTimer_Tick(sender As Object, e As EventArgs) Handles _hideTimer.Tick
        _hideTimer.Stop()
        _window.Hide()
    End Sub

    ''' <summary>測試用：不顯示視窗，把 <paramref name="c"/> 的說明畫成圖。</summary>
    Friend Function RenderPreview(c As Control) As Bitmap
        Dim entry As Entry = Nothing
        If Not _entries.TryGetValue(c, entry) Then Return Nothing
        Dim source As Control = Nothing
        Dim disabled = Not c.Enabled OrElse (_enabledSource.TryGetValue(c, source) AndAlso Not source.Enabled)
        Using w As New HelpWindow()
            w.Prepare(entry, disabled)
            Dim bmp As New Bitmap(w.Width, w.Height)
            w.DrawToBitmap(bmp, New Rectangle(Point.Empty, w.Size))
            Return bmp
        End Using
    End Function

    ''' <summary>測試用：<paramref name="c"/> 的說明。</summary>
    Friend Function EntryOf(c As Control) As Entry
        Dim entry As Entry = Nothing
        Return If(_entries.TryGetValue(c, entry), entry, Nothing)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _showTimer.Dispose()
        _hideTimer.Dispose()
        _window.Dispose()
    End Sub

    '=====================================================================
    ' 說明視窗
    '=====================================================================

    Private Class HelpWindow
        Inherits Form

        Private Const MaxWidth As Integer = 360
        Private Const MinWidth As Integer = 240
        Private Const Pad As Integer = 12
        Private Const IconSize As Integer = 32
        Private Const HeaderGap As Integer = 10

        Private Shared ReadOnly Border As Color = Color.FromArgb(158, 165, 175)
        Private Shared ReadOnly HeaderTop As Color = Color.FromArgb(246, 247, 249)
        Private Shared ReadOnly HeaderBottom As Color = Color.FromArgb(221, 227, 234)
        Private Shared ReadOnly TitleColor As Color = Color.FromArgb(27, 35, 48)
        Private Shared ReadOnly TextColor As Color = Color.FromArgb(60, 70, 86)
        Private Shared ReadOnly HintColor As Color = Color.FromArgb(42, 116, 208)
        Private Shared ReadOnly DisabledColor As Color = Color.FromArgb(178, 75, 18)
        Private Shared ReadOnly BadgeTop As Color = Color.FromArgb(96, 156, 232)
        Private Shared ReadOnly BadgeBottom As Color = Color.FromArgb(42, 104, 196)

        Private ReadOnly _titleFont As New Font("Microsoft JhengHei UI", 11.0F, FontStyle.Bold)
        Private ReadOnly _textFont As New Font("Microsoft JhengHei UI", 9.75F)
        Private ReadOnly _hintFont As New Font("Microsoft JhengHei UI", 9.0F)
        Private ReadOnly _glyphFont As New Font("Microsoft JhengHei UI", 13.0F, FontStyle.Bold)

        Private _entry As Entry
        Private _disabled As Boolean
        Private _headerHeight As Integer
        Private _titleRect, _textRect, _hintRect As Rectangle

        Public Sub New()
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            TopMost = True
            BackColor = Color.White
            DoubleBuffered = True
        End Sub

        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Const WS_EX_TOOLWINDOW As Integer = &H80
                Const WS_EX_TOPMOST As Integer = &H8
                Const WS_EX_NOACTIVATE As Integer = &H8000000
                Const CS_DROPSHADOW As Integer = &H20000
                Dim cp = MyBase.CreateParams
                cp.ExStyle = cp.ExStyle Or WS_EX_TOOLWINDOW Or WS_EX_TOPMOST Or WS_EX_NOACTIVATE
                cp.ClassStyle = cp.ClassStyle Or CS_DROPSHADOW
                Return cp
            End Get
        End Property

        Protected Overrides Sub WndProc(ByRef m As Message)
            Const WM_MOUSEACTIVATE As Integer = &H21
            Const MA_NOACTIVATE As Integer = 3
            If m.Msg = WM_MOUSEACTIVATE Then
                m.Result = New IntPtr(MA_NOACTIVATE)
                Return
            End If
            MyBase.WndProc(m)
        End Sub

        Public Sub Prepare(e As Entry, disabled As Boolean)
            _entry = e
            _disabled = disabled
            Arrange()
        End Sub

        Public Sub ShowEntry(e As Entry, disabled As Boolean, anchor As Rectangle)
            _entry = e
            _disabled = disabled
            Arrange()
            ' 放在控制項下方，放不下就放上方；一律留在該螢幕的工作區內
            Dim wa = Screen.FromRectangle(anchor).WorkingArea
            Dim x = Math.Max(wa.Left + 4, Math.Min(wa.Right - Width - 4, anchor.Left))
            Dim y = anchor.Bottom + 6
            If y + Height > wa.Bottom - 4 Then y = Math.Max(wa.Top + 4, anchor.Top - Height - 6)
            Location = New Point(x, y)
            Invalidate()
            If Not Visible Then Show() Else Refresh()
        End Sub

        Private ReadOnly Property HasIcon As Boolean
            Get
                Return _entry.Icon IsNot Nothing OrElse Not String.IsNullOrEmpty(_entry.Glyph)
            End Get
        End Property

        Private ReadOnly Property HintText As String
            Get
                If _disabled AndAlso _entry.DisabledHint <> "" Then Return "目前無法使用：" & _entry.DisabledHint
                Return _entry.Hint
            End Get
        End Property

        Private Sub Arrange()
            Dim titleX = Pad + If(HasIcon, IconSize + HeaderGap, 0)
            Dim flags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix
            ' 寬度取標題、說明、提示三者需要的最大值（介於 MinWidth 與 MaxWidth），再依寬度量高度
            Dim widest = MaxWidth - Pad * 2
            Dim titleW = TextRenderer.MeasureText(_entry.Title, _titleFont, New Size(MaxWidth - titleX - Pad, 0), flags).Width
            Dim textW = If(_entry.Text = "", 0, TextRenderer.MeasureText(_entry.Text, _textFont, New Size(widest, 0), flags).Width)
            Dim hintW = If(HintText = "", 0, TextRenderer.MeasureText(HintText, _hintFont, New Size(widest, 0), flags).Width)
            Dim w = Math.Max(MinWidth, Math.Min(MaxWidth, Math.Max(titleX + titleW + Pad, Math.Max(textW, hintW) + Pad * 2)))
            Dim bodyW = w - Pad * 2
            Dim titleSz = TextRenderer.MeasureText(_entry.Title, _titleFont, New Size(w - titleX - Pad, 0), flags)
            Dim textSz = If(_entry.Text = "", Size.Empty, TextRenderer.MeasureText(_entry.Text, _textFont, New Size(bodyW, 0), flags))
            Dim hintSz = If(HintText = "", Size.Empty, TextRenderer.MeasureText(HintText, _hintFont, New Size(bodyW, 0), flags))

            _headerHeight = Math.Max(If(HasIcon, IconSize, 0), titleSz.Height) + Pad * 2 - 4
            _titleRect = New Rectangle(titleX, (_headerHeight - titleSz.Height) \ 2, w - titleX - Pad, titleSz.Height)
            Dim y = _headerHeight + 8
            _textRect = New Rectangle(Pad, y, bodyW, textSz.Height)
            If textSz.Height > 0 Then y += textSz.Height + 6
            _hintRect = New Rectangle(Pad, y, bodyW, hintSz.Height)
            If hintSz.Height > 0 Then y += hintSz.Height + 4
            Size = New Size(w, y + Pad - 4)
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            If _entry Is Nothing Then Return
            Dim g = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            Dim header As New Rectangle(0, 0, Width, _headerHeight)
            Using b As New LinearGradientBrush(header, HeaderTop, HeaderBottom, LinearGradientMode.Vertical)
                g.FillRectangle(b, header)
            End Using
            Using p As New Pen(Color.FromArgb(206, 212, 220))
                g.DrawLine(p, 0, _headerHeight, Width, _headerHeight)
            End Using
            Dim iconRect As New Rectangle(Pad, (_headerHeight - IconSize) \ 2, IconSize, IconSize)
            If _entry.Icon IsNot Nothing Then
                DrawIcon(g, _entry.Icon, iconRect)
            ElseIf Not String.IsNullOrEmpty(_entry.Glyph) Then
                DrawBadge(g, _entry.Glyph, iconRect)
            End If

            Dim flags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix
            TextRenderer.DrawText(g, _entry.Title, _titleFont, _titleRect, TitleColor, flags Or TextFormatFlags.VerticalCenter)
            If _entry.Text <> "" Then TextRenderer.DrawText(g, _entry.Text, _textFont, _textRect, TextColor, flags)
            If HintText <> "" Then
                TextRenderer.DrawText(g, HintText, _hintFont, _hintRect, If(_disabled AndAlso _entry.DisabledHint <> "", DisabledColor, HintColor), flags)
            End If
            Using p As New Pen(Border)
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1)
            End Using
        End Sub

        Private Shared Sub DrawIcon(g As Graphics, img As Image, r As Rectangle)
            Dim k = Math.Min(r.Width / img.Width, r.Height / img.Height)
            Dim w = Math.Max(1, CInt(img.Width * k)), h = Math.Max(1, CInt(img.Height * k))
            Dim dest As New Rectangle(r.X + (r.Width - w) \ 2, r.Y + (r.Height - h) \ 2, w, h)
            Using ia As New ImageAttributes()
                ia.SetColorKey(Color.FromArgb(255, 0, 255), Color.FromArgb(255, 0, 255))
                g.DrawImage(img, dest, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia)
            End Using
        End Sub

        ''' <summary>藍色漸層圓角方塊加白字（PhotoEdit 的按鈕多半沒有圖示，用分頁的字代替）。</summary>
        Private Sub DrawBadge(g As Graphics, glyph As String, r As Rectangle)
            Using path = RoundRect(r, 8)
                Using b As New LinearGradientBrush(r, BadgeTop, BadgeBottom, LinearGradientMode.Vertical)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(Color.FromArgb(36, 88, 170))
                    g.DrawPath(p, path)
                End Using
            End Using
            Dim shine As New Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height \ 2 - 2)
            Using path = RoundRect(shine, 6), b As New SolidBrush(Color.FromArgb(50, 255, 255, 255))
                g.FillPath(b, path)
            End Using
            TextRenderer.DrawText(g, glyph, _glyphFont, r, Color.White,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)
        End Sub

        Private Shared Function RoundRect(r As Rectangle, radius As Integer) As GraphicsPath
            Dim d = radius * 2
            Dim path As New GraphicsPath()
            path.AddArc(r.X, r.Y, d, d, 180, 90)
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90)
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
            path.CloseFigure()
            Return path
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _titleFont.Dispose()
                _textFont.Dispose()
                _hintFont.Dispose()
                _glyphFont.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
