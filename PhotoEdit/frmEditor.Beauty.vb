Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 人像分頁（美顏）：套用到（全部的臉／某一張臉）、一鍵美顏小圖（這張照片的臉套上各組的樣子）＋強度、
''' 「手動調整…」視窗（frmBeautyAdjust）、液化筆刷、按住看未美顏、智慧構圖。
''' 美顏存在「目前對象」的那組 BeautySettings：全部的臉＝EditRecipe.GlobalBeauty，某張臉＝EditRecipe.FaceBeauty 裡那張臉的一組。
''' </summary>
Partial Friend Class frmEditor

    Private Const TabPortrait As Integer = 2

    ''' <summary>人像分頁裡要有臉才能用的滑桿（舊的單項滑桿已移到手動調整視窗，這裡留給其他程式碼判斷用）。</summary>
    Private Shared ReadOnly PortraitKeys As String() = {}

    Private ReadOnly _beautyTarget As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    ''' <summary>目前調整的臉（SortedFaces 的索引）；-1＝全部的臉。</summary>
    Private _beautyFace As Integer = -1
    Private ReadOnly _faceReset As New Button()
    Private ReadOnly _beautyGrid As New BeautyThumbGrid()
    Private ReadOnly _presetStrength As New Aqua.Slider With {.Minimum = 0, .Maximum = 100, .Value = 100, .ShowTicks = False}
    Private ReadOnly _presetStrengthLabel As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _beautyManual As New Button With {.Text = "手動調整…"}
    Private _beautyCompare As Boolean
    Private _syncingBeauty As Boolean
    ''' <summary>小圖：背景算圖的代號（照片、對象、色調變了就重算）。</summary>
    Private _beautyThumbKey As String
    Private _beautyThumbGeneration As Integer

    Private ReadOnly _liquifyToggle As New CheckBox With {.Appearance = Appearance.Button, .Text = "液化筆刷 (L)", .TextAlign = ContentAlignment.MiddleCenter}
    Private ReadOnly _liquifyMode As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _liquifySize As New Aqua.Slider With {.Minimum = 10, .Maximum = 300, .Value = 70, .ShowTicks = False}
    Private ReadOnly _liquifyStrength As New Aqua.Slider With {.Minimum = 5, .Maximum = 100, .Value = 50, .ShowTicks = False}
    Private ReadOnly _liquifySizeLabel As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _liquifyStrengthLabel As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}

    Private Sub BuildPortraitPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        AddHeading(L, "人像", _portraitHeading)

        ' 套用到：全部的臉／某一張臉
        _help.SetHelp("beauty.target", AddCaption(L, "套用到", L.Y), _beautyTarget)
        _beautyTarget.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - 8 - 86, 26)
        AddHandler _beautyTarget.SelectedIndexChanged, Sub() OnBeautyTargetChanged()
        L.Add(_beautyTarget)
        _faceReset.Text = "跟隨全部"
        _faceReset.SetBounds(L.Width - 82, L.Y + 1, 82, 28)
        _help.SetHelp("beauty.facereset", _faceReset)
        AddHandler _faceReset.Click, Sub() ResetFaceBeauty()
        L.Add(_faceReset)
        L.Y += 34

        ' 一鍵美顏：小圖（第一張是原圖）
        AddHeading(L, "一鍵美顏")
        LoadMyLooks()
        _beautyGrid.SetItems(BeautyGridNames())
        Dim gw = L.Width - 8
        _beautyGrid.SetBounds(6, L.Y, gw, _beautyGrid.PreferredHeightFor(gw))
        _help.SetHelp("beauty.preset", _beautyGrid)
        AddHandler _beautyGrid.ItemClicked, Sub(i) ApplyBeautyPreset(i - 1)
        L.Add(_beautyGrid)
        L.Y += _beautyGrid.Height + 8

        ' 強度
        _help.SetHelp("beauty.strength", AddCaption(L, "強度", L.Y), _presetStrength, _presetStrengthLabel)
        _presetStrength.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - ValueWidth - 8, 24)
        _presetStrengthLabel.SetBounds(L.Width - ValueWidth, L.Y + 2, ValueWidth, 24)
        _presetStrengthLabel.Text = "100%"
        AddHandler _presetStrength.ValueChanged, Sub() OnPresetStrengthChanged()
        L.Add(_presetStrength)
        L.Add(_presetStrengthLabel)
        L.Y += RowHeight

        _beautyManual.SetBounds(8, L.Y + 2, L.Width - 8, 32)
        _help.SetHelp("beauty.manual", _beautyManual)
        AddHandler _beautyManual.Click, Sub() ShowBeautyAdjust()
        L.Add(_beautyManual)
        L.Y += 42
        AddHint(L, "小圖是這張照片的臉套上各組的樣子，點一下套用，再用「強度」調濃淡。" & vbCrLf &
                   "單項數值、妝容與光影在「手動調整…」裡。")

        ' 液化
        AddHeading(L, "液化")
        _liquifyToggle.SetBounds(8, L.Y, (L.Width - 16) \ 2, 30)
        _help.SetHelp("liquify", _liquifyToggle)
        AddHandler _liquifyToggle.CheckedChanged, Sub() SetLiquifyMode(_liquifyToggle.Checked)
        L.Add(_liquifyToggle)
        _liquifyMode.Items.AddRange({"推移", "膨脹（放大）", "縮攏（縮小）", "順時針旋轉", "逆時針旋轉"})
        _liquifyMode.SelectedIndex = 0
        _liquifyMode.SetBounds(8 + (L.Width - 16) \ 2 + 8, L.Y + 2, (L.Width - 16) \ 2, 26)
        _help.SetHelp("liquify.mode", _liquifyMode)
        L.Add(_liquifyMode)
        L.Y += 38
        For Each entry In {(_liquifySize, _liquifySizeLabel, "筆刷大小", "liquify.size"), (_liquifyStrength, _liquifyStrengthLabel, "力道", "liquify.strength")}
            Dim s = entry.Item1, lbl = entry.Item2
            _help.SetHelp(entry.Item4, AddCaption(L, entry.Item3, L.Y), s, lbl)
            s.SetBounds(8 + CaptionWidth, L.Y + 2, L.Width - CaptionWidth - ValueWidth - 8, 24)
            lbl.SetBounds(L.Width - ValueWidth, L.Y + 2, ValueWidth, 24)
            lbl.Text = s.Value.ToString()
            AddHandler s.ValueChanged, Sub()
                                           lbl.Text = s.Value.ToString()
                                           If s Is _liquifySize AndAlso _canvas.Tool = PreviewCanvas.CanvasTool.Liquify Then _canvas.BrushRadius = s.Value
                                       End Sub
            L.Add(s)
            L.Add(lbl)
            L.Y += RowHeight
        Next
        Dim half = (L.Width - 16) \ 2
        Dim clearLiq = MakeButton("清除液化", "liquify.clear")
        clearLiq.SetBounds(8, L.Y + 2, half, 30)
        AddHandler clearLiq.Click, Sub() ClearLiquify()
        Dim compare = MakeButton("按住看未美顏", "beauty.compare")
        compare.SetBounds(8 + half + 8, L.Y + 2, half, 30)
        L.Add(clearLiq)
        L.Add(compare)
        L.Y += 40
        AddHandler compare.MouseDown, Sub() ShowBeautyCompare(True)
        AddHandler compare.MouseUp, Sub() ShowBeautyCompare(False)
        AddHandler compare.MouseLeave, Sub() ShowBeautyCompare(False)

        L.Y += 4
        Dim smart = MakeButton("智慧構圖", "btn.smartcrop")
        smart.SetBounds(8, L.Y, L.Width - 8, 30)
        AddHandler smart.Click, Sub() RunCommand("smartcrop")
        L.Add(smart)
        RefreshBeautyTargets()
    End Sub

    ''' <summary>從設定檔載入我的妝容（一鍵美顏小圖的最後面）。</summary>
    Private Sub LoadMyLooks()
        BeautySettings.CustomLooks.Clear()
        If _appSettings.MyLooks Is Nothing Then Return
        For Each l In _appSettings.MyLooks
            If l?.Look IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(l.Name) Then BeautySettings.CustomLooks.Add((l.Name, l.Look))
        Next
    End Sub

    ''' <summary>小圖名稱：原圖＋內建＋我的妝容。</summary>
    Private Shared Function BeautyGridNames() As String()
        Return {"原圖"}.Concat(Enumerable.Range(0, BeautySettings.PresetCount).Select(Function(i) BeautySettings.PresetName(i))).ToArray()
    End Function

    ''' <summary>手動調整視窗新增或刪除了我的妝容：存設定、重建小圖。</summary>
    Private Sub OnMyLooksChanged()
        _appSettings.MyLooks = BeautySettings.CustomLooks.Select(Function(l) New NamedLook With {.Name = l.Name, .Look = l.Look}).ToList()
        _appSettings.Save()
        _beautyGrid.SetItems(BeautyGridNames())
        _beautyThumbKey = Nothing
        RefreshBeautyThumbs()
    End Sub

    ''' <summary>人像標題後面的特徵點說明：用了 478 點網格、68 點，或都沒有（臉型與妝容停用）。</summary>
    Private Function LandmarkNote() As String
        If _faces Is Nothing OrElse _faces.Count = 0 Then Return ""
        Dim mesh = _faces.Where(Function(f) f.Mesh IsNot Nothing).Count()
        Dim dense = _faces.Where(Function(f) f.Mesh Is Nothing AndAlso f.Dense IsNot Nothing).Count()
        If mesh = _faces.Count Then Return "・478 點"
        If mesh + dense = _faces.Count Then Return If(mesh = 0, "・68 點", "・478／68 點")
        Return "・部分臉沒有特徵點"
    End Function

    ''' <summary>臉由左到右排序（「臉 1」是最左邊那張）。</summary>
    Private Function SortedFaces() As List(Of FaceRegion)
        If _faces Is Nothing Then Return New List(Of FaceRegion)()
        Return _faces.OrderBy(Function(f) f.Box.X).ToList()
    End Function

    Private Function TargetFace() As FaceRegion
        Dim faces = SortedFaces()
        Return If(_beautyFace >= 0 AndAlso _beautyFace < faces.Count, faces(_beautyFace), Nothing)
    End Function

    ''' <summary>小圖、全部的臉時用來預覽的臉：選了某張臉就是那張，否則最大的那張。</summary>
    Private Function PreviewFace() As FaceRegion
        If _faces Is Nothing OrElse _faces.Count = 0 Then Return Nothing
        Return If(TargetFace(), _faces.OrderByDescending(Function(f) f.Box.Width * f.Box.Height).First())
    End Function

    ''' <summary>目前對象的美顏值（某張臉還沒有個別設定時，顯示共用的值）。</summary>
    Private Function CurrentBeauty(r As EditRecipe) As BeautySettings
        Dim f = TargetFace()
        If f Is Nothing Then Return r.GlobalBeauty()
        Return r.BeautyFor(f.Box)
    End Function

    ''' <summary>改目前對象的美顏。</summary>
    Private Sub EditBeauty(r As EditRecipe, change As Action(Of BeautySettings))
        EditBeautyFor(r, TargetFace(), change)
    End Sub

    ''' <summary>改某個對象的美顏：face 為 Nothing 改全部的臉共用的一組；某張臉第一次調整時，從共用的值複製一份成為它的個別設定。</summary>
    Private Shared Sub EditBeautyFor(r As EditRecipe, face As FaceRegion, change As Action(Of BeautySettings))
        If face Is Nothing Then
            Dim g = r.GlobalBeauty()
            change(g)
            r.SetGlobalBeauty(g)
            Return
        End If
        Dim own = r.FaceBeautyFor(face.Box)
        If own Is Nothing Then
            own = r.GlobalBeauty()
            own.FaceX = Math.Round(face.Box.X + face.Box.Width / 2, 4)
            own.FaceY = Math.Round(face.Box.Y + face.Box.Height / 2, 4)
            If r.FaceBeauty Is Nothing Then r.FaceBeauty = New List(Of BeautySettings)()
            r.FaceBeauty.Add(own)
        End If
        change(own)
    End Sub

    ''' <summary>偵測到臉之後：重建「套用到」清單、可用與否、小圖。</summary>
    Private Sub RefreshBeautyTargets()
        Dim faces = SortedFaces()
        Dim keep = _beautyFace
        _beautyTarget.BeginUpdate()
        _beautyTarget.Items.Clear()
        _beautyTarget.Items.Add(If(faces.Count > 1, $"全部的臉（{faces.Count} 張）", "全部的臉"))
        For i = 1 To faces.Count
            _beautyTarget.Items.Add($"臉 {i}" & If(i = 1 AndAlso faces.Count > 1, "（最左邊）", ""))
        Next
        _beautyTarget.EndUpdate()
        _beautyFace = If(keep >= 0 AndAlso keep < faces.Count, keep, -1)
        _beautyTarget.SelectedIndex = _beautyFace + 1
        _beautyTarget.Enabled = faces.Count > 1
        UpdateBeautyExtras()
        RefreshBeautyThumbs()
    End Sub

    Private Sub OnBeautyTargetChanged()
        _beautyFace = _beautyTarget.SelectedIndex - 1
        UpdateBeautyExtras()
        RefreshBeautyThumbs()
        _canvas.Invalidate()
    End Sub

    ''' <summary>小圖的選取框、強度、「跟隨全部」、按鈕可用與否（配方或對象改變時呼叫）。</summary>
    Private Sub UpdateBeautyExtras()
        Dim usable = _faces IsNot Nothing AndAlso _faces.Count > 0
        Dim f = TargetFace()
        _faceReset.Enabled = f IsNot Nothing AndAlso _recipe.FaceBeautyFor(f.Box) IsNot Nothing
        Dim b = CurrentBeauty(_recipe)
        _beautyGrid.Enabled = usable
        _beautyGrid.SelectedIndex = If(b.PresetIndex.HasValue, b.PresetIndex.Value + 1, If(b.IsEmpty, 0, -1)) ' -1＝自訂
        _beautyManual.Enabled = usable
        _syncingBeauty = True
        _presetStrength.Value = If(b.PresetIndex.HasValue, b.PresetStrength, 100)
        _presetStrengthLabel.Text = _presetStrength.Value & "%"
        _syncingBeauty = False
        _presetStrength.Enabled = usable AndAlso b.PresetIndex.HasValue
        RefreshBeautyThumbs() ' 色調改了小圖跟著重算（代號沒變時不做事）
    End Sub

    ''' <summary>一鍵美顏（-1＝原圖、全部清除）：以目前的強度套到目前對象。</summary>
    Private Sub ApplyBeautyPreset(index As Integer)
        If _photo Is Nothing OrElse _faces Is Nothing OrElse _faces.Count = 0 Then Return
        Dim strength = If(CurrentBeauty(_recipe).PresetIndex.HasValue, _presetStrength.Value, 100)
        Dim preset = If(index >= 0, BeautySettings.Preset(index, strength), New BeautySettings())
        ApplyChange(Sub(r) EditBeauty(r, Sub(b) b.CopyValuesFrom(preset)))
        SetStatusMessage(If(index >= 0, $"一鍵美顏：{BeautySettings.PresetName(index)}（強度 {strength}%）", "已清除美顏") &
                         If(TargetFace() Is Nothing, "", $"（臉 {_beautyFace + 1}）") & "。")
    End Sub

    ''' <summary>強度：整組一鍵美顏的數值等比例縮放（同一次拖曳合併成一步復原）。</summary>
    Private Sub OnPresetStrengthChanged()
        _presetStrengthLabel.Text = _presetStrength.Value & "%"
        If _syncingBeauty OrElse _photo Is Nothing Then Return
        Dim b = CurrentBeauty(_recipe)
        If Not b.PresetIndex.HasValue Then Return
        Dim preset = BeautySettings.Preset(b.PresetIndex.Value, _presetStrength.Value)
        ApplyChange(Sub(r) EditBeauty(r, Sub(x) x.CopyValuesFrom(preset)), "beautystrength")
    End Sub

    ''' <summary>這張臉不再個別設定，改回跟隨「全部的臉」。</summary>
    Private Sub ResetFaceBeauty()
        Dim f = TargetFace()
        If f Is Nothing Then Return
        ApplyChange(Sub(r)
                        Dim own = r.FaceBeautyFor(f.Box)
                        If own IsNot Nothing Then r.FaceBeauty.Remove(own)
                        If r.FaceBeauty IsNot Nothing AndAlso r.FaceBeauty.Count = 0 Then r.FaceBeauty = Nothing
                    End Sub)
    End Sub

    '---------------------------------------------------------------------
    ' 一鍵美顏小圖
    '---------------------------------------------------------------------

    ''' <summary>在背景依序算每組的小圖（臉部裁切＋該組美顏＋照片色調），算好一張顯示一張；照片、對象或色調變了才重算。</summary>
    Private Sub RefreshBeautyThumbs()
        Dim face = PreviewFace()
        If face Is Nothing OrElse _previewBase Is Nothing Then
            _beautyThumbKey = Nothing
            _beautyGrid.ClearImages()
            Return
        End If
        Dim look As New EditRecipe()
        look.CopyLookFrom(_recipe)
        Dim key = $"{_previewBase.GetHashCode()}|{face.Box}|{RecipeStore.ToJson(look)}"
        If key = _beautyThumbKey Then Return
        _beautyThumbKey = key
        _beautyThumbGeneration += 1
        Dim gen = _beautyThumbGeneration
        _beautyGrid.ClearImages()
        Dim size = _beautyGrid.ThumbPixels
        Dim crop = BeautyPreviewRenderer.Crop(_previewBase, face, size)
        Dim count = BeautySettings.PresetCount
        Task.Run(Sub()
                     Try
                         For i = -1 To count - 1
                             If gen <> _beautyThumbGeneration Then Exit For
                             Dim settings = If(i < 0, New BeautySettings(), BeautySettings.Preset(i))
                             Dim img = BeautyPreviewRenderer.Render(crop.Image, crop.Face, settings, look)
                             Dim index = i + 1
                             PostToUi(Sub()
                                          If gen = _beautyThumbGeneration Then _beautyGrid.SetImage(index, img) Else img.Dispose()
                                      End Sub)
                         Next
                     Finally
                         crop.Image.Dispose()
                     End Try
                 End Sub)
    End Sub

    '---------------------------------------------------------------------
    ' 手動調整視窗
    '---------------------------------------------------------------------

    ''' <summary>開手動調整：對象＝全部的臉＋每一張臉（多張臉時），預覽用各自的臉部裁切；確定後一次寫回（一步復原）。</summary>
    Private Sub ShowBeautyAdjust()
        If _photo Is Nothing OrElse _faces Is Nothing OrElse _faces.Count = 0 Then Return
        Dim faces = SortedFaces()
        Dim targets As New List(Of BeautyTarget)()
        Dim previewFaces As New List(Of FaceRegion)()
        Dim biggest = _faces.OrderByDescending(Function(f) f.Box.Width * f.Box.Height).First()
        previewFaces.Add(If(faces.Count = 1, faces(0), biggest))
        targets.Add(New BeautyTarget With {.Name = If(faces.Count > 1, "全部的臉（預覽最大的那張）", "這張臉"), .Settings = _recipe.GlobalBeauty()})
        If faces.Count > 1 Then
            For i = 0 To faces.Count - 1
                previewFaces.Add(faces(i))
                targets.Add(New BeautyTarget With {.Name = $"臉 {i + 1}", .Settings = _recipe.BeautyFor(faces(i).Box).Clone()})
            Next
        End If
        For i = 0 To targets.Count - 1
            Dim c = BeautyPreviewRenderer.Crop(_previewBase, previewFaces(i), 720)
            targets(i).Crop = c.Image
            targets(i).Face = c.Face
        Next
        Dim look As New EditRecipe()
        look.CopyLookFrom(_recipe)
        Dim hasDense = _faces.Any(Function(f) f.Dense IsNot Nothing)
        Dim hasMesh = _faces.Any(Function(f) f.Mesh IsNot Nothing)
        Try
            Using dlg As New frmBeautyAdjust(targets, _beautyFace + 1, look, hasDense, hasMesh, _help, AddressOf OnMyLooksChanged, Sub(owner) ShowFaceChange(owner))
                If dlg.ShowDialog(Me) <> DialogResult.OK OrElse Not targets.Any(Function(t) t.Modified) Then Return
            End Using
            ApplyChange(Sub(r)
                            For i = 0 To targets.Count - 1
                                If Not targets(i).Modified Then Continue For
                                Dim face = If(i = 0, Nothing, faces(i - 1))
                                Dim s = targets(i).Settings
                                EditBeautyFor(r, face, Sub(b) b.CopyValuesFrom(s))
                            Next
                        End Sub)
            SetStatusMessage("已套用手動調整。Ctrl+Z 可復原。")
        Finally
            For Each t In targets
                t.Crop?.Dispose()
            Next
        End Try
    End Sub

    ''' <summary>按住看未美顏：暫時拿掉美顏與液化（其他調整保留）。</summary>
    Private Sub ShowBeautyCompare(show As Boolean)
        If _photo Is Nothing OrElse _beautyCompare = show Then Return
        _beautyCompare = show
        RequestRender()
        If show Then SetStatusMessage("未美顏") Else UpdateStatus()
    End Sub

    ''' <summary>按住看未美顏時，算圖用的配方拿掉美顏與液化。</summary>
    Private Shared Function WithoutBeauty(r As EditRecipe) As EditRecipe
        Dim c = r.Clone()
        c.SetGlobalBeauty(New BeautySettings())
        c.FaceBeauty = Nothing
        c.Liquify = Nothing
        Return c
    End Function

    '---------------------------------------------------------------------
    ' 液化
    '---------------------------------------------------------------------

    Private Sub SetLiquifyMode(enabled As Boolean)
        If enabled AndAlso _photo Is Nothing Then
            _liquifyToggle.Checked = False
            Return
        End If
        If enabled Then
            ExitCropMode(apply:=True)
            If _tabs.SelectedIndex <> TabPortrait Then _tabs.SelectedIndex = TabPortrait
        End If
        UpdateToolFromTab()
        SetStatusMessage(If(enabled, "液化：在照片上拖曳（推移）或按一下（膨脹、縮攏、旋轉），放開滑鼠就會套用。Esc 結束。", ""))
        If Not enabled Then UpdateStatus()
    End Sub

    Private Sub OnLiquifyStroke(points As List(Of PointF), screenRadius As Single)
        Dim s = MakeSourceStroke(points, screenRadius)
        Dim stroke As New LiquifyStroke With {.Radius = s.Radius, .Path = s.Path, .Mode = CType(Math.Max(0, _liquifyMode.SelectedIndex), LiquifyMode),
                                              .Strength = _liquifyStrength.Value}
        ApplyChange(Sub(r)
                        If r.Liquify Is Nothing Then r.Liquify = New List(Of LiquifyStroke)()
                        r.Liquify.Add(stroke)
                    End Sub)
        SetStatusMessage($"液化 {_recipe.Liquify.Count} 筆。Ctrl+Z 可復原，「清除液化」全部移除。")
    End Sub

    Private Sub ClearLiquify()
        If Not _recipe.HasLiquify Then Return
        ApplyChange(Sub(r) r.Liquify = Nothing)
        SetStatusMessage("已清除液化。")
    End Sub

    '---------------------------------------------------------------------
    ' 畫布上標出臉（選了某一張臉時）
    '---------------------------------------------------------------------

    Private Sub PaintBeautyFaces(g As Graphics)
        If _cropMode OrElse _tabs.SelectedIndex <> TabPortrait OrElse _beautyFace < 0 OrElse _previewBase Is Nothing Then Return
        Dim faces = SortedFaces()
        If faces.Count = 0 Then Return
        Dim rect = PhotoScreenRect()
        Dim crop = If(_recipe.Crop, New CropRect())
        Dim geo = _recipe.Clone()
        geo.Crop = Nothing
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using font As New Font("Microsoft JhengHei UI", 10, FontStyle.Bold)
            For i = 0 To faces.Count - 1
                Dim m = GeometryMapper.MapBox(faces(i).Box, geo, _previewBase.Width, _previewBase.Height)
                Dim x = rect.X + CSng((m.X - crop.X) / crop.Width) * rect.Width
                Dim y = rect.Y + CSng((m.Y - crop.Y) / crop.Height) * rect.Height
                Dim w = CSng(m.Width / crop.Width) * rect.Width, h = CSng(m.Height / crop.Height) * rect.Height
                Dim sel = i = _beautyFace
                Using p As New Pen(If(sel, Color.FromArgb(230, 255, 200, 60), Color.FromArgb(150, 255, 255, 255)), If(sel, 2.5F, 1.2F)) With {.DashStyle = If(sel, DashStyle.Solid, DashStyle.Dash)}
                    g.DrawEllipse(p, x, y - h * 0.05F, w, h * 1.1F)
                End Using
                Dim label = $"臉 {i + 1}"
                Dim sz = g.MeasureString(label, font)
                Using bg As New SolidBrush(If(sel, Color.FromArgb(220, 255, 200, 60), Color.FromArgb(160, 0, 0, 0))),
                      fg As New SolidBrush(If(sel, Color.Black, Color.White))
                    g.FillRectangle(bg, x + w / 2 - sz.Width / 2 - 3, y - h * 0.05F - sz.Height - 4, sz.Width + 6, sz.Height + 2)
                    g.DrawString(label, font, fg, x + w / 2 - sz.Width / 2, y - h * 0.05F - sz.Height - 3)
                End Using
            Next
        End Using
    End Sub
End Class
