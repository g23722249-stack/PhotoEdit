''' <summary>
''' 「設定」視窗（編輯 → 設定…，Ctrl+,）：外觀配色選淺色或深色，點選後立刻套用到所有視窗並記住。
''' </summary>
Friend Class frmSettings
    Inherits Aqua.AquaForm

    Private ReadOnly _settings As AppSettings
    Private ReadOnly _light As New ThemeCard(dark:=False)
    Private ReadOnly _dark As New ThemeCard(dark:=True)
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10.5F)
    Private ReadOnly _runningMdi As Boolean
    Private ReadOnly _sdi As New RadioButton()
    Private ReadOnly _mdiRadio As New RadioButton()

    Public Sub New(settings As AppSettings, runningMdi As Boolean)
        _settings = settings
        _runningMdi = runningMdi
        Text = "設定"
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        KeyPreview = True
        ClientSize = New Size(520, 470)
        Padding = New Padding(16, 23 + 12, 16, 14)

        Dim heading As New Label With {.Text = "外觀", .AutoSize = False, .Font = New Font(_font, FontStyle.Bold),
                                       .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
        heading.SetBounds(20, 40, 480, 22)
        Dim hint As New Label With {.Text = "選擇視窗與面板的配色，點一下立刻套用。", .AutoSize = False, .BackColor = Color.Transparent,
                                    .ForeColor = Color.FromArgb(105, 110, 120)}
        hint.SetBounds(20, 64, 480, 22)
        _light.SetBounds(20, 94, 230, 180)
        _dark.SetBounds(270, 94, 230, 180)
        AddHandler _light.Click, Sub() Choose(False)
        AddHandler _dark.Click, Sub() Choose(True)

        Dim close As New Button With {.Text = "關閉", .UseVisualStyleBackColor = True}
        close.SetBounds(400, 428, 100, 30)
        AddHandler close.Click, Sub() Me.Close()
        CancelButton = close

        Controls.AddRange({heading, hint, _light, _dark, close})
        BuildDocumentMode()
        UpdateCards()
        ThemeManager.Attach(Me)
    End Sub

    ''' <summary>文件模式：單一文件（SDI）或多文件（MDI）。改了要重開程式才生效，關閉設定時提示。</summary>
    Private Sub BuildDocumentMode()
        Dim heading As New Label With {.Text = "文件模式", .AutoSize = False, .Font = New Font(_font, FontStyle.Bold),
                                       .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
        heading.SetBounds(20, 290, 480, 22)
        _sdi.Text = "單一文件（SDI）：一次編輯一張照片，開新照片會取代目前的"
        _mdiRadio.Text = "多文件（MDI）：同時開多張畫布，分頁或子視窗顯示"
        For Each rb In {_sdi, _mdiRadio}
            rb.AutoSize = False
            rb.BackColor = Color.Transparent
        Next
        _sdi.SetBounds(24, 316, 480, 26)
        _mdiRadio.SetBounds(24, 344, 480, 26)
        _sdi.Checked = Not _settings.Mdi
        _mdiRadio.Checked = _settings.Mdi
        AddHandler _mdiRadio.CheckedChanged, Sub()
                                                 _settings.Mdi = _mdiRadio.Checked
                                                 _settings.Save()
                                             End Sub
        Dim hint As New Label With {.Text = "改了文件模式要重新開啟 PhotoEdit 才會生效。", .AutoSize = False, .BackColor = Color.Transparent,
                                    .ForeColor = Color.FromArgb(105, 110, 120)}
        hint.SetBounds(24, 376, 480, 22)
        Controls.AddRange({heading, _sdi, _mdiRadio, hint})
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        MyBase.OnFormClosed(e)
        If _settings.Mdi <> _runningMdi Then
            MessageBox.Show(Owner, $"文件模式已改成「{If(_settings.Mdi, "多文件（MDI）", "單一文件（SDI）")}」，" & vbCrLf &
                            "要重新開啟 PhotoEdit 才會生效。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    Private Sub Choose(dark As Boolean)
        If ThemeManager.Dark = dark Then Return
        _settings.DarkTheme = dark
        _settings.Save()
        ThemeManager.SetDark(dark)
        UpdateCards()
    End Sub

    Private Sub UpdateCards()
        _light.Selected = Not ThemeManager.Dark
        _dark.Selected = ThemeManager.Dark
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.KeyCode = Keys.Escape Then Close()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _font.Dispose()
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>配色選項：畫一個縮小的編輯器示意（標題列、工具列、照片、右側面板），下面是名稱。</summary>
    Private Class ThemeCard
        Inherits Control

        Private ReadOnly _darkCard As Boolean
        Private _selected As Boolean
        Private _hover As Boolean

        Public Sub New(dark As Boolean)
            _darkCard = dark
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Cursor = Cursors.Hand
            AccessibleRole = AccessibleRole.RadioButton
            AccessibleName = If(dark, "深色", "淺色")
        End Sub

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
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.Clear(Parent.BackColor)
            Dim pv As New Rectangle(8, 8, Width - 16, Height - 46)
            ' 示意圖的配色
            Dim win = If(_darkCard, Color.FromArgb(27, 30, 36), Color.FromArgb(236, 238, 242))
            Dim title = If(_darkCard, Color.FromArgb(40, 44, 52), Color.FromArgb(214, 216, 220))
            Dim panel = If(_darkCard, Color.FromArgb(35, 39, 46), Color.FromArgb(246, 247, 249))
            Dim canvas = If(_darkCard, Color.FromArgb(18, 20, 24), Color.FromArgb(52, 52, 56))
            Dim line = If(_darkCard, Color.FromArgb(70, 76, 88), Color.FromArgb(200, 205, 214))
            Dim text = If(_darkCard, Color.FromArgb(200, 204, 212), Color.FromArgb(70, 76, 88))
            Dim accent = Color.FromArgb(47, 128, 237)
            Using b As New SolidBrush(win) : g.FillRectangle(b, pv) : End Using
            Using b As New SolidBrush(title) : g.FillRectangle(b, pv.X, pv.Y, pv.Width, 14) : End Using
            For i = 0 To 2
                Dim c = {Color.FromArgb(255, 95, 87), Color.FromArgb(255, 189, 46), Color.FromArgb(40, 200, 64)}(i)
                Using b As New SolidBrush(c) : g.FillEllipse(b, pv.X + 5 + i * 9, pv.Y + 4, 6, 6) : End Using
            Next
            ' 左邊工具列、中間照片、右邊面板
            Using b As New SolidBrush(panel) : g.FillRectangle(b, pv.X, pv.Y + 14, 14, pv.Height - 14) : End Using
            Dim photo As New Rectangle(pv.X + 18, pv.Y + 20, pv.Width - 18 - 70, pv.Height - 26)
            Using b As New SolidBrush(canvas) : g.FillRectangle(b, photo) : End Using
            Dim pic As New Rectangle(photo.X + 10, photo.Y + 8, photo.Width - 20, photo.Height - 16)
            Using b As New Drawing2D.LinearGradientBrush(pic, Color.FromArgb(120, 180, 235), Color.FromArgb(250, 210, 140), 90.0F)
                g.FillRectangle(b, pic)
            End Using
            Using b As New SolidBrush(Color.FromArgb(70, 140, 80)) : g.FillRectangle(b, pic.X, pic.Bottom - pic.Height \ 3, pic.Width, pic.Height \ 3) : End Using
            Dim side As New Rectangle(pv.Right - 66, pv.Y + 18, 62, pv.Height - 22)
            Using b As New SolidBrush(panel) : g.FillRectangle(b, side) : End Using
            Using b As New SolidBrush(accent) : g.FillRectangle(b, side.X + 4, side.Y + 4, 26, 8) : End Using
            Using p As New Pen(line)
                For i = 0 To 4
                    Dim y = side.Y + 22 + i * 13
                    Using tb As New SolidBrush(text) : g.FillRectangle(tb, side.X + 4, y - 2, 14, 3) : End Using
                    g.DrawLine(p, side.X + 22, y, side.Right - 6, y)
                    Using kb As New SolidBrush(accent) : g.FillEllipse(kb, side.X + 22 + (i * 9) Mod 30, y - 3, 6, 6) : End Using
                Next
            End Using
            ' 名稱與選取框
            Dim nameRect As New Rectangle(0, Height - 36, Width, 28)
            Dim fore = ThemeManager.Fore(Color.FromArgb(40, 44, 52))
            Using f As New Font(Font, If(_selected, FontStyle.Bold, FontStyle.Regular))
                TextRenderer.DrawText(g, If(_selected, "● ", "○ ") & If(_darkCard, "深色", "淺色"), f, nameRect, If(_selected, accent, fore),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Using
            Dim border = If(_selected, accent, If(_hover, Color.FromArgb(120, 150, 200), ThemeManager.Line(Color.FromArgb(200, 205, 214))))
            Using p As New Pen(border, If(_selected, 3, 1))
                g.DrawRectangle(p, 1, 1, Width - 3, Height - 3)
            End Using
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            _hover = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = False
            Invalidate()
        End Sub
    End Class
End Class
