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
        ClientSize = New Size(520, 744)
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
        close.SetBounds(400, 702, 100, 30)
        AddHandler close.Click, Sub() Me.Close()
        CancelButton = close

        Controls.AddRange({heading, hint, _light, _dark, close})
        BuildDocumentMode()
        BuildPen()
        BuildQuickKeys()
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

    ''' <summary>繪圖筆：筆壓曲線（軟硬、最小筆壓、滿壓力道）與穩定器，左邊畫出曲線，拉滑桿立刻更新、立刻生效。</summary>
    Private Sub BuildPen()
        Dim heading As New Label With {.Text = "繪圖筆", .AutoSize = False, .Font = New Font(_font, FontStyle.Bold),
                                       .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
        heading.SetBounds(20, 410, 480, 22)
        Dim curve As New PenCurveView With {.Curve = _settings.PenCurve()}
        curve.SetBounds(20, 438, 150, 150)
        Controls.AddRange({heading, curve})
        Dim y = 438
        Dim addSlider = Sub(key As String, caption As String, min As Integer, max As Integer, value As Integer, format As Func(Of Integer, String), apply As Action(Of Integer))
                            Dim cap As New Label With {.Text = caption, .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
                            cap.SetBounds(186, y, 76, 26)
                            Dim s As New Aqua.Slider With {.Minimum = min, .Maximum = max, .Value = Math.Max(min, Math.Min(max, value)), .ShowTicks = False, .Name = key}
                            s.SetBounds(262, y + 1, 186, 24)
                            Dim v As New Label With {.Text = format(s.Value), .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight}
                            v.SetBounds(450, y, 52, 26)
                            AddHandler s.ValueChanged, Sub()
                                                           v.Text = format(s.Value)
                                                           apply(s.Value)
                                                           _settings.Save()
                                                           curve.Curve = _settings.PenCurve()
                                                       End Sub
                            Controls.AddRange({cap, s, v})
                            y += 32
                        End Sub
        addSlider("pen_soft", "軟硬", -100, 100, _settings.PenSoftness,
                  Function(v) If(v = 0, "線性", If(v > 0, "軟 " & v, "硬 " & -v)), Sub(v) _settings.PenSoftness = v)
        addSlider("pen_min", "最小筆壓", 0, 50, _settings.PenMinPressure, Function(v) v & "%", Sub(v) _settings.PenMinPressure = v)
        addSlider("pen_full", "滿壓力道", 40, 100, _settings.PenFullPressure, Function(v) v & "%", Sub(v) _settings.PenFullPressure = v)
        addSlider("pen_stab", "穩定器", 0, 100, _settings.Stabilizer, Function(v) If(v = 0, "關", v.ToString()), Sub(v) _settings.Stabilizer = v)
        Dim hint As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120),
                                    .Text = "軟：輕畫就夠粗夠濃；硬：要用力才會粗。手輕的人把「滿壓力道」調低。穩定器去掉手抖。"}
        hint.SetBounds(186, y + 2, 316, 44)
        Controls.Add(hint)
    End Sub

    ''' <summary>可以當快速工具熱鍵的鍵：A–Z（繪圖分頁已用的 B、E 除外）與 F1–F12。</summary>
    Private Shared Function QuickKeyChoices() As String()
        Dim letters = Enumerable.Range(AscW("A"c), 26).Select(Function(c) ChrW(c).ToString()).Where(Function(s) s <> "B" AndAlso s <> "E")
        Return letters.Concat(Enumerable.Range(1, 12).Select(Function(i) "F" & i)).ToArray()
    End Function

    ''' <summary>快速工具：繪圖分頁在滑鼠位置叫出快速面板、輪盤的熱鍵。</summary>
    Private Sub BuildQuickKeys()
        Dim heading As New Label With {.Text = "快速工具（繪圖分頁）", .AutoSize = False, .Font = New Font(_font, FontStyle.Bold),
                                       .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
        heading.SetBounds(20, 618, 480, 22)
        Controls.Add(heading)
        Dim keys = QuickKeyChoices()
        Dim x = 24
        For Each item In {("快速面板", _settings.QuickPanelHotkey, CType(Sub(v As String) _settings.QuickPanelHotkey = v, Action(Of String)), "settings.quickpanel"),
                          ("輪盤", _settings.QuickRadialHotkey, CType(Sub(v As String) _settings.QuickRadialHotkey = v, Action(Of String)), "settings.quickradial")}
            Dim cap As New Label With {.Text = item.Item1, .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(x, 650)}
            Dim combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 70, .Location = New Point(x + 70, 646)}
            combo.Items.AddRange(keys)
            combo.SelectedItem = If(keys.Contains(item.Item2), item.Item2, keys(0))
            Dim apply = item.Item3
            AddHandler combo.SelectedIndexChanged, Sub()
                                                       apply(CStr(combo.SelectedItem))
                                                       _settings.Save()
                                                   End Sub
            Controls.AddRange({cap, combo})
            x += 170
        Next
        Dim hint As New Label With {.Text = "在畫布上按一下就在滑鼠位置出現；再按一次或 Esc 收起。", .AutoSize = False, .BackColor = Color.Transparent,
                                    .ForeColor = Color.FromArgb(105, 110, 120)}
        hint.SetBounds(24, 676, 480, 22)
        Controls.Add(hint)
    End Sub

    ''' <summary>筆壓曲線圖：橫軸是繪圖板讀到的筆壓，縱軸是筆刷用的筆壓；虛線為線性。</summary>
    Private Class PenCurveView
        Inherits Control

        Private _curve As New PhotoEdit.PenCurve()

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            AccessibleName = "筆壓曲線"
        End Sub

        Public Property Curve As PhotoEdit.PenCurve
            Get
                Return _curve
            End Get
            Set(value As PhotoEdit.PenCurve)
                _curve = value
                Invalidate()
            End Set
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.Clear(ThemeManager.Back(Color.White))
            Dim box As New Rectangle(18, 6, Width - 26, Height - 26)
            Using grid As New Pen(ThemeManager.Line(Color.FromArgb(225, 228, 234)))
                For i = 1 To 3
                    g.DrawLine(grid, box.X + box.Width * i \ 4, box.Y, box.X + box.Width * i \ 4, box.Bottom)
                    g.DrawLine(grid, box.X, box.Y + box.Height * i \ 4, box.Right, box.Y + box.Height * i \ 4)
                Next
            End Using
            Using frame As New Pen(ThemeManager.Line(Color.FromArgb(190, 196, 206)))
                g.DrawRectangle(frame, box)
            End Using
            Using diag As New Pen(ThemeManager.Line(Color.FromArgb(180, 186, 196))) With {.DashStyle = Drawing2D.DashStyle.Dash}
                g.DrawLine(diag, box.X, box.Bottom, box.Right, box.Y)
            End Using
            Dim pts = Enumerable.Range(0, 61).Select(Function(i)
                                                         Dim x = i / 60.0F
                                                         Return New PointF(box.X + x * box.Width, box.Bottom - _curve.Map(x) * box.Height)
                                                     End Function).ToArray()
            Using p As New Pen(Color.FromArgb(47, 128, 237), 2.2F)
                g.DrawLines(p, pts)
            End Using
            Dim fore = ThemeManager.Fore(Color.FromArgb(105, 110, 120))
            Using f As New Font(Font.FontFamily, 7.5F)
                TextRenderer.DrawText(g, "筆的力道 →", f, New Rectangle(box.X, box.Bottom + 2, box.Width, 16), fore, TextFormatFlags.HorizontalCenter)
                g.TranslateTransform(0, box.Bottom)
                g.RotateTransform(-90)
                ' TextRenderer 不吃座標轉換，直的字用 DrawString
                Using br As New SolidBrush(fore), sf As New StringFormat With {.Alignment = StringAlignment.Center}
                    g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
                    g.DrawString("筆刷 →", f, br, New RectangleF(0, 1, box.Height, 16), sf)
                End Using
                g.ResetTransform()
            End Using
        End Sub
    End Class

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
