Imports System.IO
Imports PhotoEdit

''' <summary>
''' 「貼圖」分頁：上方主題標籤（「內建」+ stick 資料夾下的各主題），下方該主題的所有貼圖，
''' 把貼圖拖到照片上即可貼上（雙擊則貼在照片中央）。
''' </summary>
Partial Friend Class frmEditor

    Friend Const StickerDataFormat As String = "PhotoEdit.Sticker"
    Private Const TileSize As Integer = 80

    Private ReadOnly _themeBar As New Panel()
    Private ReadOnly _themeCombo As New ComboBox()
    Private ReadOnly _themeNames As New List(Of String)()
    Private ReadOnly _stickerGrid As New FlowLayoutPanel()
    Private ReadOnly _stickerCount As New Label()
    Private ReadOnly _stickerEmpty As New Label()
    Private ReadOnly _createStick As New Button()
    Private ReadOnly _openStick As New LinkLabel()
    Private _stickerPage As Control
    Private ReadOnly _stickerSelPanel As New Panel()
    Private Const SelPanelHeight As Integer = 32 + 3 * RowHeight + 38
    Private _currentTheme As String = StickerLibrary.BuiltInTheme

    Private Sub BuildStickerPage(page As Aqua.TabPage)
        _stickerPage = page
        Dim L = NewLayout(page, autoScroll:=False)
        AddHeading(L, "貼圖", reserveRight:=ValueWidth + 40 + 122)
        Dim refresh = MakeButton("重新整理", "btn.stickrefresh")
        refresh.SetBounds(L.Width - ValueWidth - 30, 6, ValueWidth + 30, 26)
        AddHandler refresh.Click, Sub() ReloadStickers()
        L.Add(refresh)
        refresh.BringToFront() ' 標題標籤橫跨整列，不移到上層會蓋住按鈕文字
        Dim helper = MakeButton("貼圖小幫手…", "btn.stickhelper")
        helper.SetBounds(L.Width - ValueWidth - 30 - 6 - 116, 6, 116, 26)
        AddHandler helper.Click, Sub() OpenStickerHelper()
        L.Add(helper)
        helper.BringToFront()

        ' 主題：上一個｜下拉選單（含張數）｜下一個。主題再多也只佔一列。
        _themeBar.SetBounds(8, L.Y, L.Width - 8, 28)
        _themeBar.BackColor = Color.Transparent
        Dim prevTheme = MakeButton("◀", "sticker.themeprev"), nextTheme = MakeButton("▶", "sticker.themenext")
        prevTheme.Dock = DockStyle.Left : prevTheme.Width = 34
        nextTheme.Dock = DockStyle.Right : nextTheme.Width = 34
        AddHandler prevTheme.Click, Sub() StepTheme(-1)
        AddHandler nextTheme.Click, Sub() StepTheme(1)
        _themeCombo.Dock = DockStyle.Fill
        _themeCombo.DropDownStyle = ComboBoxStyle.DropDownList
        _themeCombo.MaxDropDownItems = 20
        AddHandler _themeCombo.SelectedIndexChanged, Sub()
                                                          If _syncing OrElse _themeCombo.SelectedIndex < 0 Then Return
                                                          ShowTheme(_themeNames(_themeCombo.SelectedIndex))
                                                      End Sub
        _help.SetHelp("sticker.theme", _themeCombo)
        Dim comboHost As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(4, 2, 4, 0)}
        comboHost.Controls.Add(_themeCombo)
        _themeBar.Controls.Add(comboHost)
        _themeBar.Controls.Add(nextTheme)
        _themeBar.Controls.Add(prevTheme)
        L.Add(_themeBar)

        _stickerGrid.SetBounds(8, L.Y + 40, L.Width - 8, 300)
        _stickerGrid.AutoScroll = True
        _stickerGrid.WrapContents = True
        _stickerGrid.BackColor = Color.FromArgb(250, 250, 252)
        _stickerGrid.BorderStyle = BorderStyle.FixedSingle
        _stickerGrid.Padding = New Padding(4)
        L.Add(_stickerGrid)

        _stickerEmpty.AutoSize = False
        _stickerEmpty.TextAlign = ContentAlignment.MiddleCenter
        _stickerEmpty.ForeColor = Color.FromArgb(105, 110, 120)
        _stickerEmpty.BackColor = Color.Transparent
        _stickerEmpty.SetBounds(8, L.Y + 60, L.Width - 8, 90)
        L.Add(_stickerEmpty)
        _createStick.Text = "建立 stick 資料夾"
        _createStick.UseVisualStyleBackColor = True
        _createStick.SetBounds(8 + CaptionWidth, L.Y + 160, L.Width - CaptionWidth * 2, 32)
        AddHandler _createStick.Click, Sub() CreateStickFolder()
        L.Add(_createStick)

        _stickerCount.AutoSize = False
        _stickerCount.BackColor = Color.Transparent
        _stickerCount.ForeColor = Color.FromArgb(105, 110, 120)
        _stickerCount.TextAlign = ContentAlignment.MiddleLeft
        _stickerCount.SetBounds(8, 0, CaptionWidth, 24)
        L.Add(_stickerCount)
        _openStick.Text = "開啟 stick 資料夾"
        _openStick.TextAlign = ContentAlignment.MiddleRight
        _openStick.BackColor = Color.Transparent
        _openStick.SetBounds(8 + CaptionWidth, 0, L.Width - CaptionWidth - 8, 24)
        AddHandler _openStick.LinkClicked, Sub() OpenStickFolder()
        L.Add(_openStick)

        ' 下方：選取的貼圖（大小、旋轉、透明度、顏色、陰影、刪除）。
        _stickerSelPanel.BackColor = Color.Transparent
        _stickerSelPanel.SetBounds(8, 400, L.Width - 8, SelPanelHeight)
        L.Add(_stickerSelPanel)
        Dim S As New PageLayout(_stickerSelPanel, L.Width - 8, rightMargin:=0) With {.Y = 0}
        _overlayDelete.Text = "刪除"
        _overlayDelete.UseVisualStyleBackColor = True
        _overlayDelete.SetBounds(S.Width - ValueWidth - 30, 2, ValueWidth + 30, 26)
        _help.SetHelp("sticker.delete", _overlayDelete)
        AddHandler _overlayDelete.Click, Sub() DeleteOverlay()
        AddHeading(S, "選取的貼圖", reserveRight:=ValueWidth + 40)
        S.Add(_overlayDelete)
        _overlayDelete.BringToFront()
        AddRow(S, OverlayRow("ov_size", "大小", 2, 50, Function(v) v & "%", Function(o) CInt(Math.Round(o.Size * 100)), Sub(o, v) o.Size = v / 100.0))
        AddRow(S, OverlayRow("ov_rot", "旋轉", -180, 180, Function(v) v & "°", Function(o) CInt(Math.Round(o.Rotation)), Sub(o, v) o.Rotation = v))
        AddRow(S, OverlayRow("ov_opacity", "透明度", 0, 100, Function(v) v & "%", Function(o) o.Opacity, Sub(o, v) o.Opacity = v))
        Dim half = (S.Width - 16) \ 2
        _overlayColor.Text = "顏色…"
        _overlayColor.UseVisualStyleBackColor = True
        _overlayColor.SetBounds(8, S.Y + 2, half, 28)
        _help.SetHelp("overlay.color", _overlayColor)
        AddHandler _overlayColor.Click, Sub() PickOverlayColor()
        _overlayShadow.Text = "陰影"
        _overlayShadow.BackColor = Color.Transparent
        _overlayShadow.SetBounds(8 + half + 16, S.Y + 6, half - 8, 22)
        _help.SetHelp("overlay.shadow", _overlayShadow)
        AddHandler _overlayShadow.CheckedChanged, Sub()
                                                      If _syncing Then Return
                                                      Dim v = _overlayShadow.Checked
                                                      ApplyChange(Sub(r)
                                                                      If SelOverlay(r) IsNot Nothing Then SelOverlay(r).Shadow = v
                                                                  End Sub)
                                                  End Sub
        S.Add(_overlayColor)
        S.Add(_overlayShadow)

        AddHandler page.Resize, Sub() LayoutStickerPage()
        _help.SetHelp("sticker.grid", _stickerGrid)
        _help.SetHelp("sticker.create", _createStick)
        _help.SetHelp("sticker.open", _openStick)
        ReloadStickers()
    End Sub

    ''' <summary>垂直排版：主題標籤高度會隨主題數與寬度改變，貼圖格填滿中間，底部是張數與資料夾連結。</summary>
    Private Sub LayoutStickerPage()
        If _stickerPage Is Nothing Then Return
        Dim footerTop = _stickerPage.ClientSize.Height - 30
        _stickerCount.Top = footerTop
        _openStick.Top = footerTop
        _stickerSelPanel.Top = footerTop - SelPanelHeight - 4
        _stickerGrid.Top = _themeBar.Bottom + 8
        _stickerGrid.Height = Math.Max(80, _stickerSelPanel.Top - 8 - _stickerGrid.Top)
        _stickerEmpty.Top = _stickerGrid.Top + 20
        _createStick.Top = _stickerEmpty.Bottom + 8
    End Sub

    '---------------------------------------------------------------------
    ' 主題與貼圖
    '---------------------------------------------------------------------

    Private Sub ReloadStickers()
        StickerLibrary.ClearCache()
        Dim themes As New List(Of String) From {StickerLibrary.BuiltInTheme}
        themes.AddRange(StickerLibrary.Themes())
        If Not themes.Contains(_currentTheme) Then _currentTheme = StickerLibrary.BuiltInTheme

        _themeNames.Clear()
        _themeNames.AddRange(themes)
        Dim items = themes.Select(Function(t) CObj(If(t = StickerLibrary.BuiltInTheme, $"{t}（{Creative.StickerNames.Count}）", $"{t}（{StickerLibrary.Stickers(t).Count}）"))).ToArray()
        Dim wasSyncing = _syncing
        _syncing = True
        _themeCombo.Items.Clear()
        _themeCombo.Items.AddRange(items)
        _themeCombo.SelectedIndex = themes.IndexOf(_currentTheme)
        _syncing = wasSyncing
        ShowTheme(_currentTheme)
        LayoutStickerPage()
        RequestRender() ' 貼圖檔可能換過
    End Sub

    ''' <summary>上一個／下一個主題（循環）。</summary>
    Private Sub StepTheme(delta As Integer)
        If _themeNames.Count = 0 Then Return
        Dim i = (_themeNames.IndexOf(_currentTheme) + delta + _themeNames.Count) Mod _themeNames.Count
        _themeCombo.SelectedIndex = i
    End Sub

    Private Sub ShowTheme(theme As String)
        _currentTheme = theme
        Cursor = Cursors.WaitCursor
        _stickerGrid.SuspendLayout()
        Try
            For Each c As Control In _stickerGrid.Controls.Cast(Of Control)().ToList()
                c.Dispose()
            Next
            Dim keys As List(Of String)
            If theme = StickerLibrary.BuiltInTheme Then
                keys = Creative.StickerNames.Select(Function(s) "builtin:" & s.Key).ToList()
            Else
                keys = StickerLibrary.Stickers(theme).Select(Function(p) "image:" & p).ToList()
            End If
            For Each key In keys
                Dim o = StickerOverlay(key)
                Dim tile As New StickerTile(key, Creative.StickerPreview(o, TileSize - 14))
                _help.SetHelp(tile, StickerHelp(o, tile.Preview))
                AddHandler tile.DoubleClick, Sub(s, e) AddStickerAt(DirectCast(s, StickerTile).Key, New PointF(0.5F, 0.5F))
                _stickerGrid.Controls.Add(tile)
            Next
            _stickerCount.Text = $"{keys.Count} 張"

            Dim message As String = Nothing
            If keys.Count = 0 Then
                message = If(StickerLibrary.RootExists,
                             $"「{theme}」裡還沒有貼圖。{vbCrLf}把 PNG 圖片放進{vbCrLf}stick\{theme}\ 後按「重新整理」。",
                             "找不到 stick 資料夾。")
            ElseIf theme = StickerLibrary.BuiltInTheme AndAlso Not StickerLibrary.RootExists Then
                message = Nothing
            End If
            _stickerEmpty.Text = message
            _stickerEmpty.Visible = message IsNot Nothing
            _createStick.Visible = Not StickerLibrary.RootExists AndAlso keys.Count = 0
        Finally
            _stickerGrid.ResumeLayout()
            Cursor = Cursors.Default
        End Try
    End Sub

    ''' <summary>貼圖鍵（builtin:heart、image:主題\檔名）轉成預設外觀的 Overlay。</summary>
    Private Shared Function StickerOverlay(key As String) As Overlay
        If key.StartsWith("image:") Then
            Return New Overlay With {.Kind = OverlayKind.Image, .ImagePath = key.Substring(6), .Size = 0.22, .Shadow = False}
        End If
        Dim name = key.Substring(If(key.StartsWith("builtin:"), 8, 0))
        Return New Overlay With {.Kind = OverlayKind.Sticker, .Sticker = name, .Size = 0.18, .ColorArgb = StickerColor(name).ToArgb()}
    End Function

    ''' <summary>在照片上的指定位置（0..1）貼上貼圖並選取，停留在貼圖分頁以便繼續貼。</summary>
    Private Sub AddStickerAt(key As String, position As PointF)
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        Dim o = StickerOverlay(key)
        o.X = Math.Max(0, Math.Min(1, position.X))
        o.Y = Math.Max(0, Math.Min(1, position.Y))
        AddOverlayObject(o, switchTab:=-1)
        SetStatusMessage("已貼上。拖曳可移動，角落可縮放，上方圓點可旋轉；大小、旋轉、陰影也可在「裝飾」分頁調整。")
    End Sub

    '---------------------------------------------------------------------
    ' stick 資料夾
    '---------------------------------------------------------------------

    Private Sub OpenStickFolder()
        If Not StickerLibrary.RootExists Then
            CreateStickFolder()
            If Not StickerLibrary.RootExists Then Return
        End If
        Process.Start(New ProcessStartInfo("explorer.exe", """" & StickerLibrary.Root & """") With {.UseShellExecute = True})
    End Sub

    Private Sub CreateStickFolder()
        Try
            Directory.CreateDirectory(StickerLibrary.Root)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            MessageBox.Show(Me, "無法建立 stick 資料夾：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        ReloadStickers()
        SetStatusMessage("已建立 " & StickerLibrary.Root & "：在裡面建立主題資料夾，再放入 PNG 貼圖。")
    End Sub

    ''' <summary>貼圖格的說明：名稱、種類，圖示用貼圖本身。</summary>
    Private Shared Function StickerHelp(o As Overlay, preview As Bitmap) As HelpTip.Entry
        Dim baseHelp = HelpTexts.Get("sticker.grid")
        Dim isImage = o.Kind = OverlayKind.Image
        Return New HelpTip.Entry With {
            .Title = If(isImage, Path.GetFileNameWithoutExtension(o.ImagePath), Creative.StickerNames.First(Function(s) s.Key = o.Sticker).Name),
            .Text = If(isImage, "圖片貼圖（stick\" & o.ImagePath & "），保留原本的顏色。", "內建向量貼圖，放大也很清楚，可以改顏色。"),
            .Hint = "拖到照片上貼在那個位置；按兩下貼在照片中央。",
            .Icon = preview, .Glyph = If(baseHelp?.Glyph, "")}
    End Function

    ''' <summary>貼圖格：顯示預覽，可拖曳（資料為貼圖鍵）或雙擊。</summary>
    Private Class StickerTile
        Inherits Control

        Public ReadOnly Key As String
        Private ReadOnly _preview As Bitmap

        Public ReadOnly Property Preview As Bitmap
            Get
                Return _preview
            End Get
        End Property
        Private _hover As Boolean
        Private _downAt As Point?

        Public Sub New(key As String, preview As Bitmap)
            Me.Key = key
            _preview = preview
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.StandardDoubleClick, True)
            Size = New Size(TileSize, TileSize)
            Margin = New Padding(3)
            Cursor = Cursors.Hand
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(If(_hover, Color.FromArgb(225, 236, 252), Color.White))
            Using pen As New Pen(If(_hover, Color.FromArgb(90, 140, 220), Color.FromArgb(215, 220, 228)))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1)
            End Using
            If _preview IsNot Nothing Then
                g.DrawImage(_preview, (Width - _preview.Width) \ 2, (Height - _preview.Height) \ 2, _preview.Width, _preview.Height)
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
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Button = MouseButtons.Left Then _downAt = e.Location
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If Not _downAt.HasValue OrElse e.Button <> MouseButtons.Left Then Return
            Dim d = SystemInformation.DragSize
            If Math.Abs(e.X - _downAt.Value.X) < d.Width AndAlso Math.Abs(e.Y - _downAt.Value.Y) < d.Height Then Return
            _downAt = Nothing
            ' 拖曳時游標換成貼圖本身。
            Using cursor As New DragCursor(_preview)
                _dragCursor = cursor
                Try
                    DoDragDrop(New DataObject(StickerDataFormat, Key), DragDropEffects.Copy)
                Finally
                    _dragCursor = Nothing
                End Try
            End Using
        End Sub

        Private _dragCursor As DragCursor

        Protected Overrides Sub OnGiveFeedback(e As GiveFeedbackEventArgs)
            MyBase.OnGiveFeedback(e)
            If _dragCursor Is Nothing Then Return
            e.UseDefaultCursors = False
            Cursor.Current = If(e.Effect = DragDropEffects.Copy, _dragCursor.CanDrop, _dragCursor.CannotDrop)
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            _downAt = Nothing
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _preview?.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class

''' <summary>貼圖小幫手（獨立視窗，同時只開一個）：存好後重新讀取貼圖並切到該主題。</summary>
Partial Friend Class frmEditor
    Private _stickerHelper As frmStickerHelper

    Private Sub OpenStickerHelper()
        If _stickerHelper Is Nothing OrElse _stickerHelper.IsDisposed Then
            _stickerHelper = New frmStickerHelper(Sub(theme)
                                                      _currentTheme = theme
                                                      ReloadStickers()
                                                      _tabs.SelectedIndex = TabSticker
                                                  End Sub)
            _stickerHelper.Show(Me)
        Else
            If _stickerHelper.WindowState = FormWindowState.Minimized Then _stickerHelper.WindowState = FormWindowState.Normal
            _stickerHelper.Activate()
        End If
    End Sub
End Class
