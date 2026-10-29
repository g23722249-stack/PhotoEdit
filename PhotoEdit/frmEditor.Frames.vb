Imports PhotoEdit

' 「裝飾」頁的邊框選項：主色（自選／取照片主色／預設）、下方加寬、陰影、內框線、照片圓角、輸出比例、邊框文字。
' 算圖在 PhotoEdit.Core\PhotoFrames.vb。
Partial Class frmEditor

    Private ReadOnly _frameColor As New Button With {.FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand}
    Private ReadOnly _frameAuto As New CheckBox With {.Text = Lang.T("取照片主色"), .AutoSize = True, .BackColor = Color.Transparent}
    Private _frameColorReset As Button
    Private ReadOnly _frameAspect As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _frameText As New TextBox With {.Multiline = True, .ScrollBars = ScrollBars.Vertical, .AcceptsReturn = True}
    Private _frameExif As Button
    Private ReadOnly FrameOptionKeys As String() = {"framebottom", "frameshadow", "frameline", "framecorner"}

    Private Sub BuildFrameControls(L As PageLayout)
        AddRow(L, MakeRow("framebottom", Lang.T("下方加寬"), 0, 200, Function(v) v & "%", Function(r) r.FrameBottom, Sub(r, v) r.FrameBottom = v))
        AddRow(L, MakeRow("frameshadow", Lang.T("陰影"), 0, 100, AddressOf Plain, Function(r) r.FrameShadow, Sub(r, v) r.FrameShadow = v))
        AddRow(L, MakeRow("frameline", Lang.T("內框線"), 0, 100, AddressOf Plain, Function(r) r.FrameInnerLine, Sub(r, v) r.FrameInnerLine = v))
        AddRow(L, MakeRow("framecorner", Lang.T("照片圓角"), 0, 100, AddressOf Plain, Function(r) r.FrameCorner, Sub(r, v) r.FrameCorner = v))

        ' 主色：色塊按鈕（點了開選色）、取照片主色、預設
        Dim caption = AddCaption(L, Lang.T("顏色"), L.Y)
        _help.SetHelpLinked("frame.color", _frameColor, caption, _frameColor)
        _frameColor.FlatAppearance.BorderColor = Color.FromArgb(150, 160, 175)
        _frameColor.SetBounds(8 + CaptionWidth, L.Y + 3, 52, 26)
        AddHandler _frameColor.Click, Sub() PickFrameColor()
        L.Add(_frameColor)
        _frameAuto.Location = New Point(8 + CaptionWidth + 60, L.Y + 7)
        _help.SetHelp("frame.auto", _frameAuto)
        AddHandler _frameAuto.CheckedChanged, Sub()
                                                  If _syncing OrElse _photo Is Nothing Then Return
                                                  Dim isAuto = _frameAuto.Checked
                                                  ApplyChange(Sub(r) r.FrameAutoColor = isAuto)
                                              End Sub
        L.Add(_frameAuto)
        L.Y += RowHeight
        _frameColorReset = MakeButton(Lang.T("預設顏色"), "frame.colorreset")
        _frameColorReset.SetBounds(8 + CaptionWidth, L.Y + 1, 96, 28)
        AddHandler _frameColorReset.Click, Sub() ApplyChange(Sub(r)
                                                                 r.FrameColorArgb = Nothing
                                                                 r.FrameAutoColor = False
                                                             End Sub)
        L.Add(_frameColorReset)
        L.Y += RowHeight

        ' 模糊延伸背景的比例
        _help.SetHelpLinked("frame.aspect", _frameAspect, AddCaption(L, Lang.T("比例"), L.Y), _frameAspect)
        _frameAspect.Items.AddRange(PhotoFrames.AspectNames.Cast(Of Object)().ToArray())
        _frameAspect.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _frameAspect.SelectedIndexChanged, Sub()
                                                          If _syncing OrElse _photo Is Nothing OrElse _frameAspect.SelectedIndex < 0 Then Return
                                                          Dim a = _frameAspect.SelectedIndex
                                                          ApplyChange(Sub(r) r.FrameAspect = a)
                                                      End Sub
        L.Add(_frameAspect)
        L.Y += RowHeight

        ' 邊框文字（相機資訊、拍立得、郵票、底片邊…）
        _help.SetHelpLinked("frame.text", _frameText, AddCaption(L, Lang.T("文字"), L.Y), _frameText)
        _frameText.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 84)
        AddHandler _frameText.TextChanged, Sub()
                                               If _syncing OrElse _photo Is Nothing Then Return
                                               Dim t = _frameText.Text.Replace(vbCrLf, vbLf)
                                               ApplyChange(Sub(r) r.FrameText = If(t = "", Nothing, t), "frametext")
                                           End Sub
        L.Add(_frameText)
        L.Y += 90
        _frameExif = MakeButton(Lang.T("帶入相機資訊"), "frame.exif")
        _frameExif.SetBounds(8 + CaptionWidth, L.Y + 1, 130, 28)
        AddHandler _frameExif.Click, Sub()
                                         Dim t = CameraInfoText()
                                         ApplyChange(Sub(r) r.FrameText = t)
                                     End Sub
        L.Add(_frameExif)
        L.Y += RowHeight
    End Sub

    Private Sub PickFrameColor()
        If _photo Is Nothing Then Return
        Dim cur = If(_recipe.FrameColorArgb.HasValue, Color.FromArgb(_recipe.FrameColorArgb.Value), PhotoFrames.DefaultColor(_recipe.Frame))
        Using dlg As New ColorDialog With {.Color = cur, .FullOpen = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            SetFrameColor(dlg.Color)
        End Using
    End Sub

    Friend Sub SetFrameColor(c As Color)
        Dim argb = Color.FromArgb(255, c).ToArgb()
        ApplyChange(Sub(r)
                        r.FrameColorArgb = argb
                        r.FrameAutoColor = False
                    End Sub)
    End Sub

    ''' <summary>相機資訊邊框的文字：相機、鏡頭、拍攝參數、日期（各一行；照片沒有 EXIF 時用檔名和今天）。</summary>
    Private Function CameraInfoText() As String
        Dim info = If(_photo?.Info(), New PhotoInfo())
        Dim camera = If(String.IsNullOrWhiteSpace(info.Camera), IO.Path.GetFileNameWithoutExtension(If(_photo?.Path, "")), info.Camera)
        Dim lens = If(info.Lens, "")
        Dim settings = info.SettingsText()
        Dim d = If(info.TakenDate, DateTime.Now)
        Return String.Join(vbLf, {camera, lens, settings, d.ToString("yyyy.MM.dd  HH:mm")})
    End Function

    ''' <summary>選了邊框樣式：相機資訊沒有文字時先帶入相機資訊。</summary>
    Private Sub OnFrameStyleChosen(style As PhotoFrameStyle)
        Dim needText = style = PhotoFrameStyle.CameraInfo AndAlso String.IsNullOrWhiteSpace(_recipe.FrameText)
        Dim t = If(needText, CameraInfoText(), Nothing)
        ApplyChange(Sub(r)
                        r.Frame = style
                        If needText Then r.FrameText = t
                    End Sub)
    End Sub

    Private Sub SyncFrameControls()
        Dim style = _recipe.Frame
        Dim has = style <> PhotoFrameStyle.None
        For Each row In _rows.Where(Function(r) FrameOptionKeys.Contains(r.Key) OrElse r.Key = "framesize")
            row.Slider.Enabled = has
        Next
        Dim col = If(_recipe.FrameColorArgb.HasValue, Color.FromArgb(_recipe.FrameColorArgb.Value), PhotoFrames.DefaultColor(style))
        _frameColor.BackColor = If(has, col, SystemColors.Control)
        _frameColor.Enabled = has AndAlso Not _recipe.FrameAutoColor
        _frameAuto.Enabled = has
        _frameAuto.Checked = _recipe.FrameAutoColor
        _frameColorReset.Enabled = has AndAlso (_recipe.FrameColorArgb.HasValue OrElse _recipe.FrameAutoColor)
        _frameAspect.Enabled = style = PhotoFrameStyle.BlurExtend
        _frameAspect.SelectedIndex = Math.Max(0, Math.Min(PhotoFrames.AspectNames.Length - 1, _recipe.FrameAspect))
        Dim usesText = PhotoFrames.UsesText(style)
        _frameText.Enabled = usesText
        Dim txt = If(_recipe.FrameText, "").Replace(vbLf, vbCrLf)
        If _frameText.Text <> txt AndAlso Not _frameText.Focused Then _frameText.Text = txt
        _frameExif.Enabled = usesText
    End Sub
End Class
