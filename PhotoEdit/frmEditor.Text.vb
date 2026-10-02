Imports System.IO
Imports PhotoEdit

''' <summary>
''' 「文字」分頁：加文字、日期戳記、插入照片資訊；文字內容、字型、粗體、直排、對齊；
''' 樣式預設集；以及用分段按鈕切換的特效組（排版、外框、底色、陰影、發光、立體、填色），一次只顯示一組，不必捲動。
''' </summary>
Partial Friend Class frmEditor

    Private Const TabText As Integer = 7

    Private ReadOnly _insertInfo As New ComboBox()
    Private ReadOnly _textVertical As New CheckBox()
    Private ReadOnly _alignButtons As New List(Of RadioButton)()
    Private ReadOnly _textPresetBar As New FlowLayoutPanel()
    Private ReadOnly _effectSelector As New FlowLayoutPanel()
    Private ReadOnly _effectGroups As New List(Of Panel)()
    Private ReadOnly _colorButtons As New List(Of (Button As Button, Getter As Func(Of Overlay, Integer)))()
    Private ReadOnly _bgStyleCombo As New ComboBox()
    Private ReadOnly _fillModeCombo As New ComboBox()
    Private ReadOnly _textShadowOn As New CheckBox()
    Private ReadOnly _textureButton As New Button()

    Private Shared ReadOnly TextFonts As (Name As String, Family As String)() = {
        ("微軟正黑體", "Microsoft JhengHei"), ("標楷體", "DFKai-SB"), ("新細明體", "PMingLiU"), ("微軟雅黑", "Microsoft YaHei"),
        ("Arial", "Arial"), ("Georgia", "Georgia"), ("Impact", "Impact"), ("Segoe Script", "Segoe Script"),
        ("Comic Sans MS", "Comic Sans MS"), ("七段數字（日期戳記）", TextRender.SevenSegmentFont)}
    Private Shared ReadOnly EffectNames As String() = {"排版", "外框", "底色", "陰影", "發光", "立體", "填色"}

    Private Sub BuildTextPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        AddHeading(L, "文字", reserveRight:=ValueWidth + 40)
        Dim del = MakeButton("刪除", "刪除選取的文字（Del）")
        del.SetBounds(L.Width - ValueWidth - 30, 6, ValueWidth + 30, 26)
        AddHandler del.Click, Sub()
                                  If SelText(_recipe) IsNot Nothing Then DeleteOverlay()
                              End Sub
        L.Add(del)
        del.BringToFront()

        ' ＋文字｜日期戳記｜插入資訊
        Dim third = (L.Width - 8 - 12) \ 3
        Dim add = MakeButton("＋ 文字", "加入一段文字，可拖曳移動、縮放、旋轉")
        add.SetBounds(8, L.Y + 2, third, 30)
        AddHandler add.Click, Sub() AddOverlay(OverlayKind.Text, Nothing)
        Dim stamp = MakeButton("日期戳記", "用照片的拍攝日期，做成傳統相機的橘色日期")
        stamp.SetBounds(8 + third + 6, L.Y + 2, third, 30)
        AddHandler stamp.Click, Sub() AddDateStamp()
        _insertInfo.DropDownStyle = ComboBoxStyle.DropDownList
        _insertInfo.Items.AddRange({"插入資訊…", "拍攝日期", "日期與時間", "相機型號", "檔名", "GPS 座標"})
        _insertInfo.SelectedIndex = 0
        _insertInfo.SetBounds(8 + (third + 6) * 2, L.Y + 5, L.Width - 8 - (third + 6) * 2, 24)
        AddHandler _insertInfo.SelectedIndexChanged, Sub()
                                                          If _insertInfo.SelectedIndex <= 0 Then Return
                                                          Dim index = _insertInfo.SelectedIndex
                                                          _insertInfo.SelectedIndex = 0
                                                          InsertPhotoInfo(index)
                                                      End Sub
        L.Add(add) : L.Add(stamp) : L.Add(_insertInfo)
        L.Y += 40

        ' 文字內容（可多行）
        _overlayText.Multiline = True
        _overlayText.AcceptsReturn = True
        _overlayText.ScrollBars = ScrollBars.Vertical
        _overlayText.SetBounds(8, L.Y + 2, L.Width - 8, 54)
        _tip.SetToolTip(_overlayText, "可以按 Enter 換行")
        AddHandler _overlayText.TextChanged, Sub()
                                                 If _syncing Then Return
                                                 Dim t = _overlayText.Text
                                                 ApplyChange(Sub(r)
                                                                 If SelText(r) IsNot Nothing Then SelText(r).Text = t
                                                             End Sub, "overlay-text")
                                             End Sub
        L.Add(_overlayText)
        L.Y += 62

        AddCaption(L, "字型", L.Y)
        _overlayFont.DropDownStyle = ComboBoxStyle.DropDownList
        _overlayFont.Items.AddRange(TextFonts.Select(Function(f) CObj(f.Name)).ToArray())
        _overlayFont.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _overlayFont.SelectedIndexChanged, Sub()
                                                           If _syncing OrElse _overlayFont.SelectedIndex < 0 Then Return
                                                           Dim family = TextFonts(_overlayFont.SelectedIndex).Family
                                                           ApplyChange(Sub(r)
                                                                           If SelText(r) IsNot Nothing Then SelText(r).FontName = family
                                                                       End Sub)
                                                       End Sub
        L.Add(_overlayFont)
        L.Y += RowHeight

        ' 粗體｜直排｜對齊
        _overlayBold.Text = "粗體"
        _overlayBold.BackColor = Color.Transparent
        _overlayBold.SetBounds(8, L.Y + 6, 64, 22)
        AddHandler _overlayBold.CheckedChanged, Sub() SetTextProp(Sub(o) o.Bold = _overlayBold.Checked)
        _textVertical.Text = "直排"
        _textVertical.BackColor = Color.Transparent
        _textVertical.SetBounds(76, L.Y + 6, 64, 22)
        AddHandler _textVertical.CheckedChanged, Sub() SetTextProp(Sub(o) o.Vertical = _textVertical.Checked)
        L.Add(_overlayBold) : L.Add(_textVertical)
        Dim alignX = 8 + CaptionWidth + 64
        Dim alignW = (L.Width - alignX - 4) \ 3
        For i = 0 To 2
            Dim index = i
            Dim rb As New RadioButton With {.Text = {"靠左", "置中", "靠右"}(i), .Appearance = Appearance.Button, .FlatStyle = FlatStyle.Flat,
                                            .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.White}
            rb.FlatAppearance.CheckedBackColor = Color.FromArgb(210, 228, 250)
            rb.SetBounds(alignX + i * (alignW + 2), L.Y + 3, alignW, 26)
            _tip.SetToolTip(rb, "多行文字的對齊方式（直排時為上、中、下）")
            AddHandler rb.CheckedChanged, Sub(s, e)
                                              If DirectCast(s, RadioButton).Checked Then SetTextProp(Sub(o) o.Align = index)
                                          End Sub
            _alignButtons.Add(rb)
            L.Add(rb)
        Next
        L.Y += RowHeight

        ' 樣式預設集
        AddHeading(L, "樣式")
        _textPresetBar.SetBounds(8, L.Y, L.Width - 8, 4 * 50)
        _textPresetBar.WrapContents = True
        _textPresetBar.BackColor = Color.Transparent
        Using bg = PresetBackground(76, 42)
            For Each p In TextPreset.BuiltIn
                Dim tile As New Button With {.Size = New Size(76, 44), .Margin = New Padding(0, 0, 4, 4), .FlatStyle = FlatStyle.Flat,
                                             .Text = "", .Tag = p, .Cursor = Cursors.Hand,
                                             .BackgroundImage = Creative.TextPreview(p.Style, If(p.Style.FontName = TextRender.SevenSegmentFont, "'05 7 12", "字Aa"), bg),
                                             .BackgroundImageLayout = ImageLayout.Stretch}
                tile.FlatAppearance.BorderColor = Color.FromArgb(190, 200, 215)
                _tip.SetToolTip(tile, p.Name & "（套用到選取的文字；沒有選取時新增一段）")
                AddHandler tile.Click, Sub(s, e) ApplyTextPreset(DirectCast(DirectCast(s, Button).Tag, TextPreset))
                _textPresetBar.Controls.Add(tile)
            Next
        End Using
        L.Add(_textPresetBar)
        L.Y += 4 * 50 + 4

        ' 特效組切換
        _effectSelector.SetBounds(8, L.Y, L.Width - 8, 32)
        _effectSelector.WrapContents = False
        _effectSelector.BackColor = Color.Transparent
        For i = 0 To EffectNames.Length - 1
            Dim index = i
            Dim rb As New RadioButton With {.Text = EffectNames(i), .Appearance = Appearance.Button, .FlatStyle = FlatStyle.Flat,
                                            .AutoSize = True, .Margin = New Padding(0, 0, 3, 0), .BackColor = Color.White, .Checked = i = 0}
            rb.FlatAppearance.CheckedBackColor = Color.FromArgb(210, 228, 250)
            rb.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 195)
            AddHandler rb.CheckedChanged, Sub(s, e)
                                              If DirectCast(s, RadioButton).Checked Then ShowEffectGroup(index)
                                          End Sub
            _effectSelector.Controls.Add(rb)
        Next
        L.Add(_effectSelector)
        L.Y += 38

        Dim groupTop = L.Y
        Dim groupHeight = 6 * RowHeight + 10
        For i = 0 To EffectNames.Length - 1
            Dim panel As New Panel With {.BackColor = Color.Transparent, .Visible = i = 0}
            panel.SetBounds(8, groupTop, L.Width - 8, groupHeight)
            L.Add(panel)
            _effectGroups.Add(panel)
            Dim G As New PageLayout(panel, L.Width - 8, rightMargin:=0)
            G.Y = 4
            BuildEffectGroup(i, G)
        Next
        L.Y += groupHeight
    End Sub

    Private Sub BuildEffectGroup(index As Integer, G As PageLayout)
        Dim num As Func(Of Integer, String) = AddressOf frmEditor.Plain
        Dim degrees As Func(Of Integer, String) = Function(v) v & "°"
        Select Case index
            Case 0 ' 排版（大小、旋轉、透明度與貼圖共用，其餘只限文字）
                AddRow(G, OverlayRow("ov_size", "大小", 2, 50, Function(v) v & "%", Function(o) CInt(Math.Round(o.Size * 100)), Sub(o, v) o.Size = v / 100.0),
                       "高度佔照片的百分比；也可以拖曳角落控制點，或 Ctrl+滾輪")
                AddRow(G, OverlayRow("ov_rot", "旋轉", -180, 180, degrees, Function(o) CInt(Math.Round(o.Rotation)), Sub(o, v) o.Rotation = v),
                       "也可以拖曳上方的圓形把手（Shift 每 15° 吸附），或 Shift+滾輪")
                AddRow(G, OverlayRow("ov_opacity", "透明度", 0, 100, Function(v) v & "%", Function(o) o.Opacity, Sub(o, v) o.Opacity = v),
                       "100% 為不透明；浮水印可用 30～50%")
                AddRow(G, OverlayRow("tx_spacing", "字距", -20, 100, num, Function(o) o.LetterSpacing, Sub(o, v) o.LetterSpacing = v))
                AddRow(G, OverlayRow("tx_line", "行距", 50, 300, Function(v) v & "%", Function(o) o.LineSpacing, Sub(o, v) o.LineSpacing = v))
                AddRow(G, OverlayRow("tx_arc", "弧形", -360, 360, degrees, Function(o) o.Arc, Sub(o, v) o.Arc = v),
                       "往右向上拱（彩虹形），往左向下彎（微笑形），360° 為一整圈；多行會合成一行，直排時不使用")
            Case 1 ' 外框
                AddColorRow(G, "外框色", Function(o) o.OutlineColorArgb, Sub(o, v) o.OutlineColorArgb = v)
                AddRow(G, OverlayRow("tx_outline", "粗細", 0, 100, num, Function(o) o.OutlineWidth, Sub(o, v) o.OutlineWidth = v))
                AddColorRow(G, "外層色", Function(o) o.Outline2ColorArgb, Sub(o, v) o.Outline2ColorArgb = v)
                AddRow(G, OverlayRow("tx_outline2", "外層粗細", 0, 100, num, Function(o) o.Outline2Width, Sub(o, v) o.Outline2Width = v),
                       "在外框之外再加一圈（例如可愛字的雙層框）")
            Case 2 ' 底色
                AddComboRow(G, "樣式", _bgStyleCombo, {"無", "圓角方塊", "字幕條（整列）", "壓暗照片", "印章框"},
                            Sub(i) SetTextProp(Sub(o) o.BackgroundStyle = CType(i, TextBackground)))
                AddColorRow(G, "顏色", Function(o) o.BackgroundColorArgb, Sub(o, v) o.BackgroundColorArgb = v)
                AddRow(G, OverlayRow("tx_bgopacity", "不透明度", 0, 100, Function(v) v & "%", Function(o) o.BackgroundOpacity, Sub(o, v) o.BackgroundOpacity = v))
                AddRow(G, OverlayRow("tx_bgradius", "圓角", 0, 100, num, Function(o) o.BackgroundRadius, Sub(o, v) o.BackgroundRadius = v))
                AddRow(G, OverlayRow("tx_bgpadding", "留白", 0, 100, num, Function(o) o.BackgroundPadding, Sub(o, v) o.BackgroundPadding = v))
            Case 3 ' 陰影
                Dim btn = AddColorRow(G, "顏色", Function(o) o.ShadowColorArgb, Sub(o, v) o.ShadowColorArgb = v)
                btn.Width -= ValueWidth + 4
                _textShadowOn.Text = "開啟"
                _textShadowOn.BackColor = Color.Transparent
                _textShadowOn.SetBounds(G.Width - ValueWidth, btn.Top + 3, ValueWidth, 22)
                AddHandler _textShadowOn.CheckedChanged, Sub() SetTextProp(Sub(o) o.Shadow = _textShadowOn.Checked)
                G.Add(_textShadowOn)
                AddRow(G, OverlayRow("tx_shdist", "距離", 0, 100, num, Function(o) o.ShadowDistance, Sub(o, v) o.ShadowDistance = v))
                AddRow(G, OverlayRow("tx_shangle", "方向", 0, 359, degrees, Function(o) o.ShadowAngle, Sub(o, v) o.ShadowAngle = v),
                       "0° 往右、90° 往下")
                AddRow(G, OverlayRow("tx_shblur", "模糊", 0, 100, num, Function(o) o.ShadowBlur, Sub(o, v) o.ShadowBlur = v))
                AddRow(G, OverlayRow("tx_shopacity", "不透明度", 0, 100, Function(v) v & "%", Function(o) o.ShadowOpacity, Sub(o, v) o.ShadowOpacity = v))
            Case 4 ' 發光
                AddColorRow(G, "顏色", Function(o) o.GlowColorArgb, Sub(o, v) o.GlowColorArgb = v)
                AddRow(G, OverlayRow("tx_glow", "大小", 0, 100, num, Function(o) o.GlowSize, Sub(o, v) o.GlowSize = v))
                AddHint(G, "搭配深色照片或「霓虹」樣式效果最好。貼圖也可以發光。")
            Case 5 ' 立體
                AddColorRow(G, "側面色", Function(o) o.ExtrudeColorArgb, Sub(o, v) o.ExtrudeColorArgb = v)
                AddRow(G, OverlayRow("tx_extrude", "厚度", 0, 100, num, Function(o) o.ExtrudeDepth, Sub(o, v) o.ExtrudeDepth = v))
                AddRow(G, OverlayRow("tx_exangle", "方向", 0, 359, degrees, Function(o) o.ExtrudeAngle, Sub(o, v) o.ExtrudeAngle = v))
            Case 6 ' 填色
                AddComboRow(G, "方式", _fillModeCombo, {"單色", "漸層", "彩虹", "圖片", "照片本身（鏤空）"},
                            Sub(i) SetTextProp(Sub(o) o.FillMode = CType(i, TextFill)))
                AddColorRow(G, "主色", Function(o) o.ColorArgb, Sub(o, v) o.ColorArgb = v)
                AddColorRow(G, "第二色", Function(o) o.Color2Argb, Sub(o, v) o.Color2Argb = v)
                AddRow(G, OverlayRow("tx_gradangle", "方向", 0, 359, degrees, Function(o) o.GradientAngle, Sub(o, v) o.GradientAngle = v),
                       "漸層與彩虹的方向：0° 由左到右、90° 由上到下")
                _textureButton.Text = "選擇填字圖片…"
                _textureButton.UseVisualStyleBackColor = True
                _textureButton.SetBounds(8 + CaptionWidth, G.Y + 2, G.Width - CaptionWidth - 8, 28)
                AddHandler _textureButton.Click, Sub() PickTexture()
                G.Add(_textureButton)
                G.Y += RowHeight
        End Select
    End Sub

    '---------------------------------------------------------------------
    ' 小工具
    '---------------------------------------------------------------------

    ''' <summary>選取中的物件是文字時回傳它。</summary>
    Private Function SelText(r As EditRecipe) As Overlay
        Dim o = SelOverlay(r)
        Return If(o IsNot Nothing AndAlso o.Kind = OverlayKind.Text, o, Nothing)
    End Function

    Private Sub SetTextProp(change As Action(Of Overlay))
        If _syncing Then Return
        ApplyChange(Sub(r)
                        Dim o = SelText(r)
                        If o IsNot Nothing Then change(o)
                    End Sub)
    End Sub

    ''' <summary>一列：名稱｜色塊按鈕（點了開調色盤）。回傳按鈕方便調整位置。</summary>
    Private Function AddColorRow(L As PageLayout, caption As String, getter As Func(Of Overlay, Integer), setter As Action(Of Overlay, Integer)) As Button
        AddCaption(L, caption, L.Y)
        Dim b As New Button With {.FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand}
        b.FlatAppearance.BorderColor = Color.FromArgb(150, 160, 175)
        b.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 26)
        AddHandler b.Click, Sub()
                                Dim o = SelOverlay(_recipe)
                                If o Is Nothing Then Return
                                Using dlg As New ColorDialog With {.Color = Color.FromArgb(getter(o)), .FullOpen = True}
                                    If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                                    Dim argb = dlg.Color.ToArgb()
                                    ApplyChange(Sub(r)
                                                    If SelOverlay(r) IsNot Nothing Then setter(SelOverlay(r), argb)
                                                End Sub)
                                End Using
                            End Sub
        L.Add(b)
        _colorButtons.Add((b, getter))
        L.Y += RowHeight
        Return b
    End Function

    Private Sub AddComboRow(L As PageLayout, caption As String, combo As ComboBox, items As String(), onChange As Action(Of Integer))
        AddCaption(L, caption, L.Y)
        combo.DropDownStyle = ComboBoxStyle.DropDownList
        combo.Items.AddRange(items.Cast(Of Object)().ToArray())
        combo.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler combo.SelectedIndexChanged, Sub()
                                                   If Not _syncing AndAlso combo.SelectedIndex >= 0 Then onChange(combo.SelectedIndex)
                                               End Sub
        L.Add(combo)
        L.Y += RowHeight
    End Sub

    Private Sub ShowEffectGroup(index As Integer)
        For i = 0 To _effectGroups.Count - 1
            _effectGroups(i).Visible = i = index
        Next
    End Sub

    ''' <summary>預設集縮圖的背景：像照片的漸層（鏤空樣式才看得出效果）。</summary>
    Private Shared Function PresetBackground(w As Integer, h As Integer) As Bitmap
        Dim bmp As New Bitmap(w, h)
        Using g = Graphics.FromImage(bmp),
              br As New Drawing2D.LinearGradientBrush(New Rectangle(0, 0, w, h), Color.FromArgb(70, 130, 190), Color.FromArgb(230, 170, 110), 35)
            g.FillRectangle(br, 0, 0, w, h)
            Using hill As New SolidBrush(Color.FromArgb(90, 140, 80))
                g.FillEllipse(hill, -w \ 4, h * 2 \ 3, w, h)
            End Using
        End Using
        Return bmp
    End Function

    '---------------------------------------------------------------------
    ' 動作
    '---------------------------------------------------------------------

    Private Sub ApplyTextPreset(p As TextPreset)
        If _photo Is Nothing Then Return
        If SelText(_recipe) Is Nothing Then
            AddOverlay(OverlayKind.Text, Nothing)
        End If
        Dim index = _overlayIndex
        ApplyChange(Sub(r) p.ApplyTo(r.Overlays(index)))
        SyncSliders()
        UpdateCreativeControls()
        SetStatusMessage($"已套用「{p.Name}」樣式。")
    End Sub

    ''' <summary>日期戳記：拍攝日期（沒有時用檔案修改日期），橘色七段數字放在右下角。</summary>
    Private Sub AddDateStamp()
        If _photo Is Nothing Then Return
        Dim info = _photo.Info()
        Dim d = If(info.TakenDate, File.GetLastWriteTime(_photo.Path))
        Dim o = TextPreset.DateStamp.Clone()
        o.Text = PhotoInfo.DateStampText(d)
        o.X = 0.86 : o.Y = 0.92 : o.Size = 0.05
        ExitCropMode(apply:=True)
        AddOverlayObject(o, switchTab:=TabText)
        Dim note = If(Not info.TakenDate.HasValue, "這張照片沒有拍攝日期，改用檔案日期。",
                      If(d > DateTime.Now.AddDays(1), $"相機記錄的日期是 {d:yyyy/MM/dd}，可能是相機時鐘設錯了。", "已加入拍攝日期。"))
        SetStatusMessage(note & "可以直接修改文字，或拖曳到其他位置。")
    End Sub

    Private Sub InsertPhotoInfo(index As Integer)
        If _photo Is Nothing Then Return
        Dim info = _photo.Info()
        Dim value As String = Nothing
        Select Case index
            Case 1 : value = info.TakenDate?.ToString("yyyy/MM/dd")
            Case 2 : value = info.TakenDate?.ToString("yyyy/MM/dd HH:mm")
            Case 3 : value = info.Camera
            Case 4 : value = Path.GetFileNameWithoutExtension(_photo.Path)
            Case 5 : value = info.Gps
        End Select
        If String.IsNullOrEmpty(value) Then
            SetStatusMessage("這張照片沒有這項資訊。")
            Return
        End If
        If SelText(_recipe) Is Nothing Then
            AddOverlay(OverlayKind.Text, Nothing)
            _overlayText.Text = value
        Else
            _overlayText.Focus()
            _overlayText.SelectedText = value
        End If
    End Sub

    Private Sub PickTexture()
        If SelText(_recipe) Is Nothing Then Return
        Using dlg As New OpenFileDialog With {.Title = "選擇填字圖片", .Filter = "圖片檔|*.png;*.jpg;*.jpeg;*.bmp;*.gif"}
            If StickerLibrary.RootExists Then dlg.InitialDirectory = StickerLibrary.Root
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ' stick 資料夾裡的圖存相對路徑，其他存完整路徑。
            Dim full = Path.GetFullPath(dlg.FileName)
            Dim root = Path.GetFullPath(StickerLibrary.Root).TrimEnd("\"c) & "\"
            Dim stored = If(full.StartsWith(root, StringComparison.OrdinalIgnoreCase), full.Substring(root.Length), full)
            SetTextProp(Sub(o)
                            o.TexturePath = stored
                            o.FillMode = TextFill.Texture
                        End Sub)
        End Using
    End Sub

    ''' <summary>選取改變或配方改變後，更新文字分頁的控制項。</summary>
    Private Sub UpdateTextControls()
        Dim o = SelOverlay(_recipe)
        Dim t = SelText(_recipe)
        Dim isText = t IsNot Nothing
        _overlayText.Enabled = isText
        If _overlayText.Text <> If(isText, t.Text, "") Then _overlayText.Text = If(isText, t.Text, "")
        _overlayFont.Enabled = isText
        _overlayFont.SelectedIndex = If(isText, Math.Max(0, Array.FindIndex(TextFonts, Function(f) f.Family = t.FontName)), -1)
        _overlayBold.Enabled = isText
        _overlayBold.Checked = isText AndAlso t.Bold
        _textVertical.Enabled = isText
        _textVertical.Checked = isText AndAlso t.Vertical
        For i = 0 To _alignButtons.Count - 1
            _alignButtons(i).Enabled = isText
            _alignButtons(i).Checked = isText AndAlso t.Align = i
        Next
        _bgStyleCombo.Enabled = isText
        _bgStyleCombo.SelectedIndex = If(isText, CInt(t.BackgroundStyle), -1)
        _fillModeCombo.Enabled = isText
        _fillModeCombo.SelectedIndex = If(isText, CInt(t.FillMode), -1)
        _textureButton.Enabled = isText
        _textureButton.Text = If(isText AndAlso Not String.IsNullOrEmpty(t.TexturePath), "填字圖片：" & Path.GetFileName(t.TexturePath), "選擇填字圖片…")
        ' 陰影、發光文字和貼圖都能用；其餘只限文字。
        _textShadowOn.Enabled = o IsNot Nothing
        _textShadowOn.Checked = o IsNot Nothing AndAlso o.Shadow
        For Each cb In _colorButtons
            cb.Button.Enabled = o IsNot Nothing
            If o IsNot Nothing Then
                Dim c = Color.FromArgb(cb.Getter(o))
                cb.Button.BackColor = Color.FromArgb(255, c)
                cb.Button.ForeColor = If(c.GetBrightness() < 0.5, Color.White, Color.Black)
                cb.Button.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            Else
                cb.Button.BackColor = SystemColors.Control
                cb.Button.Text = ""
            End If
        Next
        For Each row In _rows.Where(Function(r) r.Key.StartsWith("tx_"))
            Dim forAll = row.Key.StartsWith("tx_sh") OrElse row.Key = "tx_glow"
            row.Slider.Enabled = If(forAll, o IsNot Nothing, isText)
        Next
    End Sub
End Class
