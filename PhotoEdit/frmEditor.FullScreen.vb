Imports System.Drawing.Drawing2D

''' <summary>
''' 全螢幕繪圖（繪圖分頁）：畫布填滿目前螢幕的工作區（保留 Windows 工作列），
''' 標題列、選單、右側面板、繪圖工具列與選項列、狀態列都收起；工具用 F9 快速面板、F10 輪盤、浮動選色視窗。
''' 切換：F11、檢視 → 全螢幕繪圖、繪圖選項列的「全螢幕」按鈕、輪盤中間。
''' 離開：Esc（有進行中的多邊形、圖說編輯、一次吸管時先取消那個）、F11、右上角的「離開全螢幕」按鈕、切到別的分頁。
''' </summary>
Partial Friend Class frmEditor

    Private _fullScreen As Boolean
    Private _fsBounds As Rectangle
    Private _fsState As FormWindowState
    Private _fsPadding As Padding
    Private _fsHidden As List(Of (Ctl As Control, Visible As Boolean))
    Private _fsExit As FullScreenExitButton

    Friend ReadOnly Property IsFullScreen As Boolean
        Get
            Return _fullScreen
        End Get
    End Property

    Friend Sub ToggleFullScreen()
        If _fullScreen Then ExitFullScreen() Else EnterFullScreen()
    End Sub

    Private Sub EnterFullScreen()
        If _fullScreen Then Return
        If _tabs.SelectedIndex <> TabDraw Then _tabs.SelectedIndex = TabDraw ' 只在繪圖分頁
        _fsState = WindowState
        If WindowState = FormWindowState.Minimized Then WindowState = FormWindowState.Normal
        _fsBounds = If(WindowState = FormWindowState.Normal, Bounds, RestoreBounds)
        _fsPadding = Padding
        Dim wa = Screen.FromControl(Me).WorkingArea
        SuspendLayout()
        _fsHidden = New List(Of (Ctl As Control, Visible As Boolean))()
        For Each c In New Control() {_sidePanel, _splitter, _statusLabel, _drawBar, _drawStrip, _presetStrip, _cropPanel}
            _fsHidden.Add((c, c.Visible))
            c.Visible = False
        Next
        Padding = Padding.Empty ' 標題列、選單列被畫布蓋住
        _fullScreen = True
        WindowState = FormWindowState.Normal
        Bounds = wa
        Region = Nothing ' 直角，四角不露出底下的桌面
        ResumeLayout(True)
        If _fsExit Is Nothing OrElse _fsExit.IsDisposed Then _fsExit = New FullScreenExitButton(Me)
        _fsExit.ShowAtCorner(wa)
        _canvas.Focus()
        SetStatusMessage("全螢幕繪圖：Esc 或 F11 離開。")
    End Sub

    Friend Sub ExitFullScreen()
        If Not _fullScreen Then Return
        _fullScreen = False
        _fsExit?.Hide()
        SuspendLayout()
        Padding = _fsPadding
        For Each h In _fsHidden
            h.Ctl.Visible = h.Visible
        Next
        _fsHidden = Nothing
        Bounds = _fsBounds
        If _fsState = FormWindowState.Maximized Then WindowState = FormWindowState.Maximized
        ResumeLayout(True)
        OnResize(EventArgs.Empty) ' 恢復圓角外形
        _canvas.Focus()
    End Sub

    ''' <summary>全螢幕時保持直角（AquaForm 每次改大小都會重設圓角外形）。</summary>
    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If _fullScreen Then Region = Nothing
    End Sub

    ''' <summary>全螢幕時的 Esc：沒有進行中的編輯要取消時才離開。</summary>
    Private Function HandleFullScreenEscape() As Boolean
        If Not _fullScreen Then Return False
        If _polyPoints IsNot Nothing OrElse (_calloutEditor IsNot Nothing AndAlso _calloutEditor.Visible) Then Return False
        ExitFullScreen()
        Return True
    End Function

    ''' <summary>全螢幕右上角的「離開全螢幕」：平常半透明，滑鼠或筆靠近時變清楚；不搶焦點。</summary>
    Friend NotInheritable Class FullScreenExitButton
        Inherits Form

        Private ReadOnly _ed As frmEditor
        Private ReadOnly _font As New Font("Microsoft JhengHei UI", 9.5F)
        Private _hot As Boolean
        ''' <summary>平常的不透明度（測試設成 0，不會出現在使用者螢幕上）。</summary>
        Friend IdleOpacity As Double = 0.45

        Public Sub New(ed As frmEditor)
            _ed = ed
            Owner = ed
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            DoubleBuffered = True
            ClientSize = New Size(156, 34)
            Opacity = IdleOpacity
            BackColor = Color.FromArgb(40, 44, 52)
            Cursor = Cursors.Hand
            Tag = ThemeManager.SkipTreeTag
            Using path = RoundRect(New Rectangle(0, 0, Width, Height), 9)
                Region = New Region(path)
            End Using
        End Sub

        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property

        Public Sub ShowAtCorner(wa As Rectangle)
            Location = New Point(wa.Right - Width - 14, wa.Top + 12)
            _hot = False
            Opacity = IdleOpacity
            If Not Visible Then Show(_ed)
        End Sub

        Private Shared Function RoundRect(r As Rectangle, radius As Integer) As GraphicsPath
            Dim p As New GraphicsPath()
            Dim d = radius * 2
            p.AddArc(r.X, r.Y, d, d, 180, 90)
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
            p.CloseFigure()
            Return p
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.Clear(If(_hot, Color.FromArgb(47, 128, 237), Color.FromArgb(40, 44, 52)))
            ' 縮小圖示：四個往內的角
            Using p As New Pen(Color.White, 2)
                Dim x = 12, y = 9, s = 16, k = 5
                g.DrawLines(p, {New Point(x + k, y), New Point(x + k, y + k), New Point(x, y + k)})
                g.DrawLines(p, {New Point(x + s - k, y), New Point(x + s - k, y + k), New Point(x + s, y + k)})
                g.DrawLines(p, {New Point(x, y + s - k), New Point(x + k, y + s - k), New Point(x + k, y + s)})
                g.DrawLines(p, {New Point(x + s, y + s - k), New Point(x + s - k, y + s - k), New Point(x + s - k, y + s)})
            End Using
            TextRenderer.DrawText(g, "離開全螢幕 (Esc)", _font, New Rectangle(34, 0, Width - 38, Height), Color.White,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.Left)
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            _hot = True
            Opacity = 0.95
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hot = False
            Opacity = IdleOpacity
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            If e.Button = MouseButtons.Left Then _ed.ExitFullScreen()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _font.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
