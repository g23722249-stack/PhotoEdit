Imports PhotoEdit

''' <summary>
''' 去背分頁的魔術棒（W）：點一下去掉和點擊處顏色相近的區域，Alt＋點一下（或「補回」模式）加回來。
''' 「相似程度」與「只選相連區域」改變時，會一起修改最後一次點擊，不必復原重點。
''' 不需要先自動去背；還沒有去背時點一下就會建立透明背景的去背。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _wandToggle As New CheckBox()
    Private ReadOnly _wandRemoveMode As New RadioButton()
    Private ReadOnly _wandRestoreMode As New RadioButton()
    Private ReadOnly _wandContiguous As New CheckBox()
    Private ReadOnly _wandDespeckle As New CheckBox()
    Private _wandTolerance As Integer = 25

    Private Sub BuildWandSection(L As PageLayout)
        AddHeading(L, "魔術棒")
        Dim half = (L.Width - 16) \ 2
        _wandToggle.Appearance = Appearance.Button
        _wandToggle.Text = "魔術棒 (W)"
        _wandToggle.TextAlign = ContentAlignment.MiddleCenter
        _wandToggle.FlatStyle = FlatStyle.Flat
        _wandToggle.BackColor = Color.White
        _wandToggle.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 195)
        _wandToggle.FlatAppearance.CheckedBackColor = Color.FromArgb(255, 238, 180)
        _wandToggle.Image = WandCursor.Picture(restore:=False)
        _wandToggle.ImageAlign = ContentAlignment.MiddleLeft
        _wandToggle.SetBounds(8, L.Y + 2, half, 32)
        AddHandler _wandToggle.CheckedChanged, Sub() OnWandToggled()
        _help.SetHelp("cutout.wand", _wandToggle)
        L.Add(_wandToggle)

        For Each rb In {_wandRemoveMode, _wandRestoreMode}
            rb.Appearance = Appearance.Button
            rb.FlatStyle = FlatStyle.Flat
            rb.TextAlign = ContentAlignment.MiddleCenter
            rb.BackColor = Color.White
            rb.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 195)
        Next
        _wandRemoveMode.Text = "去除"
        _wandRemoveMode.Checked = True
        _wandRemoveMode.FlatAppearance.CheckedBackColor = Color.FromArgb(250, 210, 210)
        _wandRestoreMode.Text = "補回"
        _wandRestoreMode.FlatAppearance.CheckedBackColor = Color.FromArgb(200, 240, 200)
        AddHandler _wandRestoreMode.CheckedChanged, Sub() _canvas.WandRestore = _wandRestoreMode.Checked
        _help.SetHelp("cutout.wandremove", _wandRemoveMode)
        _help.SetHelp("cutout.wandrestore", _wandRestoreMode)
        ' 自己的容器：才不會和其他選項按鈕互斥。
        L.Add(PairPanel(_wandRemoveMode, _wandRestoreMode, 8 + half + 8, L.Y + 2, half, 50))
        L.Y += 40

        AddRow(L, New SliderRow With {.Key = "co_wand", .Caption = "相似程度", .Minimum = 0, .Maximum = 100, .Format = AddressOf Plain,
            .GetValue = Function(r) If(LastWand(r) Is Nothing, _wandTolerance, LastWand(r).Tolerance),
            .SetValue = Sub(r, v)
                            _wandTolerance = v
                            Dim last = LastWand(r)
                            If last IsNot Nothing Then last.Tolerance = v
                        End Sub})

        _wandContiguous.Text = "只選相連區域"
        _wandContiguous.Checked = True
        _wandContiguous.BackColor = Color.Transparent
        _wandContiguous.SetBounds(8, L.Y + 4, half, 24)
        AddHandler _wandContiguous.CheckedChanged, Sub()
                                                       If _syncing Then Return
                                                       Dim v = _wandContiguous.Checked
                                                       ApplyChange(Sub(r)
                                                                       Dim last = LastWand(r)
                                                                       If last IsNot Nothing Then last.Contiguous = v
                                                                   End Sub)
                                                   End Sub
        _help.SetHelp("cutout.wandcontiguous", _wandContiguous)
        _wandDespeckle.Text = "清除雜點"
        _wandDespeckle.BackColor = Color.Transparent
        _wandDespeckle.SetBounds(8 + half + 8, L.Y + 4, half, 24)
        AddHandler _wandDespeckle.CheckedChanged, Sub()
                                                      If _syncing Then Return
                                                      Dim v = _wandDespeckle.Checked
                                                      ApplyChange(Sub(r)
                                                                      If r.Cutout IsNot Nothing Then r.Cutout.WandDespeckle = v
                                                                  End Sub)
                                                  End Sub
        _help.SetHelp("cutout.wanddespeckle", _wandDespeckle)
        L.Add(_wandContiguous)
        L.Add(_wandDespeckle)
        _cutoutControls.Add(_wandDespeckle)
        L.Y += 32
        AddHint(L, "點一下去掉相近的顏色，Alt＋點一下補回。拉「相似程度」會調整最後一次點擊。")
        AddHandler _canvas.WandClicked, AddressOf OnWandClicked
    End Sub

    Private Shared Function LastWand(r As EditRecipe) As WandClick
        If r.Cutout Is Nothing OrElse r.Cutout.Wand Is Nothing OrElse r.Cutout.Wand.Count = 0 Then Return Nothing
        Return r.Cutout.Wand(r.Cutout.Wand.Count - 1)
    End Function

    Private Sub OnWandToggled()
        If _syncing Then Return
        If _wandToggle.Checked AndAlso MaskBrushActive Then
            _syncing = True
            _keepBrush.Checked = False
            _eraseBrush.Checked = False
            _syncing = False
        End If
        _canvas.WandRestore = _wandRestoreMode.Checked
        If _wandToggle.Checked Then SetStatusMessage("魔術棒：在照片上點一下要去掉的顏色（Alt＋點一下補回）。")
        UpdateToolFromTab()
    End Sub

    ''' <summary>點一下：位置換回已轉正原圖座標，加一次魔術棒點擊（還沒有去背時建立一個透明背景的去背）。</summary>
    Private Sub OnWandClicked(p As PointF, alt As Boolean)
        If _photo Is Nothing OrElse _previewBase Is Nothing Then Return
        Dim src = GeometryMapper.UnmapPoint(DisplayToPhoto(p), _recipe, _previewBase.Width, _previewBase.Height, fromCropped:=True)
        If src.X < 0 OrElse src.Y < 0 OrElse src.X > 1 OrElse src.Y > 1 Then Return
        Dim restore = _wandRestoreMode.Checked Xor alt
        Dim click As New WandClick With {.X = Math.Round(src.X, 5), .Y = Math.Round(src.Y, 5), .Tolerance = _wandTolerance,
                                         .Contiguous = _wandContiguous.Checked, .Restore = restore}
        Dim model = If(_modelHuman.Checked, CutoutModel.Human, CutoutModel.General)
        ApplyChange(Sub(r)
                        If r.Cutout Is Nothing Then r.Cutout = New CutoutSettings With {.Model = model, .Background = CutoutBackground.Transparent}
                        r.Cutout.Wand.Add(click)
                    End Sub)
        SetStatusMessage(If(restore, "已補回相近顏色的區域。", "已去掉相近顏色的區域。") & "拉「相似程度」可以調整這一次的範圍。")
    End Sub
End Class
