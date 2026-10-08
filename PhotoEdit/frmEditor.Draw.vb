Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 「繪圖」分頁：左側工具列（選取、直接繪製＋18 種向量工具）、右側筆刷／顏色／粗細／圖說文字／圖層。
''' 直接繪製像 Photoshop 的筆刷：畫在目前的點陣圖層上（見 frmEditor.Raster.vb），畫完就是像素。
''' 每個形狀或每組筆畫是一個圖層，存在配方的 Drawings（非破壞性，可復原／重做）。
''' 畫好的形狀可以再拖曳：藍色方塊縮放、上方圓點旋轉、黃點調整形狀（圓角、星形胖瘦、箭頭、圖說指示），
''' 直線、貝茲曲線、多邊形可拖曳各點。座標一律以「照片高度 = 1」為單位（見 DrawLayer）。
''' </summary>
Partial Friend Class frmEditor
    Implements PreviewCanvas.IDrawHost

    Private Const TabDraw As Integer = 9
    Private Const HandleRadius As Single = 7
    Private Const RotateHandleOffset As Single = 26

    Private ReadOnly _drawStrip As New DrawToolStrip()
    Private ReadOnly _drawBar As New Panel()
    Private ReadOnly _drawHint As New Label()
    Private ReadOnly _brushGrid As New Panel()
    Private ReadOnly _brushTiles As New List(Of BrushTile)()
    Private ReadOnly _drawSub As New ComboBox()
    Private ReadOnly _drawSubCat As New ComboBox()
    Private ReadOnly _drawStrokeColor As New Button()
    Private ReadOnly _drawFillColor As New Button()
    Private ReadOnly _drawTextColor As New Button()
    Private ReadOnly _drawFill As New CheckBox()
    Private ReadOnly _drawStroke As New CheckBox()
    Private ReadOnly _drawShadow As New CheckBox()
    Private ReadOnly _drawFont As New ComboBox()
    Private ReadOnly _layerList As New ListBox()
    Private ReadOnly _layerButtons As New List(Of Button)()
    Private _layerTop As Integer
    Private _brushPreviewKey As String

    Private _drawIndex As Integer = -1
    ''' <summary>新圖形使用的筆觸設定（選取圖形時會換成該圖形的設定）。</summary>
    Private ReadOnly _drawStyle As New DrawLayer With {.Shape = DrawShape.Freehand}
    Private ReadOnly _drawRandom As New Random()

    '=====================================================================
    ' 版面
    '=====================================================================

    ''' <summary>畫布左側的工具列與上方的操作列（只在繪圖分頁顯示）。</summary>
    Private Sub BuildDrawChrome()
        _drawStrip.Dock = DockStyle.Left
        _drawStrip.Visible = False
        AddHandler _drawStrip.ToolSelected, Sub(t) OnDrawToolChanged()
        For Each b As DrawToolStrip.ToolButton In _drawStrip.Buttons
            _help.SetHelp(If(b.Tool = DrawIcons.SelectTool, "draw.tool.select", "draw.tool." & b.Tool), b)
        Next

        _drawBar.Dock = DockStyle.Top
        _drawBar.Height = 36
        _drawBar.Visible = False
        _drawBar.BackColor = Color.FromArgb(236, 238, 242)
        _drawBar.Padding = New Padding(6, 4, 6, 4)
        _drawBar.Font = _panelFont
        Dim undo = MakeButton("↶ 復原", "draw.undo")
        Dim redo = MakeButton("↷ 重做", "draw.redo")
        Dim done = MakeButton("完成", "draw.done")
        For Each b In {undo, redo}
            b.Dock = DockStyle.Left
            b.Width = 84
        Next
        done.Dock = DockStyle.Right
        done.Width = 80
        AddHandler undo.Click, Sub() RunCommand("undo")
        AddHandler redo.Click, Sub() RunCommand("redo")
        AddHandler done.Click, Sub() FinishDrawAction()
        BuildEraserToggle()
        _drawHint.Dock = DockStyle.Fill
        _drawHint.AutoEllipsis = True
        _drawHint.TextAlign = ContentAlignment.MiddleLeft
        _drawHint.Padding = New Padding(10, 0, 0, 0)
        _drawHint.ForeColor = Color.FromArgb(70, 76, 88)
        ' 停靠順序：最後加入的最先停靠。
        _drawBar.Controls.Add(_drawHint)
        _drawBar.Controls.Add(done)
        _drawBar.Controls.Add(_drawEraser)
        _drawBar.Controls.Add(redo)
        _drawBar.Controls.Add(undo)
        _canvas.DrawHost = Me
    End Sub

    Private Sub BuildDrawPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        AddHeading(L, "筆刷")
        Dim brushRows = (DrawGeometry.BrushNames.Length + 3) \ 4
        _brushGrid.SetBounds(8, L.Y, L.Width - 8, brushRows * 42)
        _brushGrid.BackColor = Color.Transparent
        For i = 0 To DrawGeometry.BrushNames.Length - 1
            Dim t As New BrushTile(CType(i, BrushKind))
            AddHandler t.Click, Sub(s, e) SetDrawBrush(DirectCast(s, BrushTile).Brush)
            _help.SetHelp("draw.brush." & i, t)
            _brushTiles.Add(t)
            _brushGrid.Controls.Add(t)
        Next
        AddHandler _brushGrid.Resize, Sub() LayoutBrushTiles()
        L.Add(_brushGrid)
        L.Y += brushRows * 42 + 2
        LayoutBrushTiles()

        ' 特效／材質：先選分類，再選項目（選項目時線條色換成建議顏色）。
        Dim subCaption = AddCaption(L, "效果", L.Y)
        _help.SetHelpLinked("draw.subcat", _drawSubCat, subCaption, _drawSubCat)
        _help.SetHelp("draw.sub", _drawSub)
        Dim catW = (L.Width - CaptionWidth - 8 - 6) * 2 \ 5
        _drawSubCat.DropDownStyle = ComboBoxStyle.DropDownList
        _drawSubCat.SetBounds(8 + CaptionWidth, L.Y + 3, catW, 24)
        _drawSub.DropDownStyle = ComboBoxStyle.DropDownList
        _drawSub.SetBounds(8 + CaptionWidth + catW + 6, L.Y + 3, L.Width - CaptionWidth - 8 - catW - 6, 24)
        _drawSub.MaxDropDownItems = 16
        AddHandler _drawSubCat.SelectedIndexChanged, Sub()
                                                          If _syncing OrElse _drawSubCat.SelectedIndex < 0 Then Return
                                                          Dim cats = CurrentEffectCategories()
                                                          If cats Is Nothing Then Return
                                                          ApplyEffectEntry(cats(_drawSubCat.SelectedIndex).Items(0))
                                                      End Sub
        AddHandler _drawSub.SelectedIndexChanged, Sub()
                                                       If _syncing OrElse _drawSub.SelectedIndex < 0 OrElse _drawSubCat.SelectedIndex < 0 Then Return
                                                       Dim cats = CurrentEffectCategories()
                                                       If cats Is Nothing Then Return
                                                       ApplyEffectEntry(cats(_drawSubCat.SelectedIndex).Items(_drawSub.SelectedIndex))
                                                   End Sub
        L.Add(_drawSubCat)
        L.Add(_drawSub)
        L.Y += RowHeight

        AddHeading(L, "顏色")
        Dim third = (L.Width - 8 - 12) \ 3
        _drawStrokeColor.Text = "線條色"
        _drawFillColor.Text = "填色"
        _drawTextColor.Text = "文字色"
        Dim colorButtons = {(_drawStrokeColor, "draw.strokecolor"), (_drawFillColor, "draw.fillcolor"), (_drawTextColor, "draw.textcolor")}
        For i = 0 To 2
            Dim b = colorButtons(i).Item1
            b.FlatStyle = FlatStyle.Flat
            b.FlatAppearance.BorderColor = Color.FromArgb(150, 160, 175)
            b.Cursor = Cursors.Hand
            b.SetBounds(8 + i * (third + 6), L.Y + 2, third, 30)
            _help.SetHelp(colorButtons(i).Item2, b)
            L.Add(b)
        Next
        AddHandler _drawStrokeColor.Click, Sub() PickDrawColor(Function(d) d.StrokeColorArgb, Sub(d, v) d.StrokeColorArgb = v)
        AddHandler _drawFillColor.Click, Sub() PickDrawColor(Function(d) d.FillColorArgb, Sub(d, v)
                                                                                           d.FillColorArgb = v
                                                                                           d.Filled = True
                                                                                       End Sub)
        AddHandler _drawTextColor.Click, Sub() PickDrawColor(Function(d) d.TextColorArgb, Sub(d, v) d.TextColorArgb = v)
        L.Y += 38
        Dim checks = {(_drawStroke, "外框", "draw.stroke"), (_drawFill, "填色", "draw.fill"), (_drawShadow, "陰影", "draw.shadow")}
        For i = 0 To 2
            Dim cb = checks(i).Item1
            cb.Text = checks(i).Item2
            cb.BackColor = Color.Transparent
            cb.SetBounds(8 + i * (third + 6), L.Y + 4, third, 24)
            _help.SetHelp(checks(i).Item3, cb)
            L.Add(cb)
        Next
        AddHandler _drawStroke.CheckedChanged, Sub() If Not _syncing Then SetDrawProp(Sub(d) d.Stroked = _drawStroke.Checked)
        AddHandler _drawFill.CheckedChanged, Sub() If Not _syncing Then SetDrawProp(Sub(d) d.Filled = _drawFill.Checked)
        AddHandler _drawShadow.CheckedChanged, Sub() If Not _syncing Then SetDrawProp(Sub(d) d.Shadow = _drawShadow.Checked)
        L.Y += 32

        Dim pct As Func(Of Integer, String) = Function(v) v & "%"
        AddRow(L, DrawRow("dw_width", "粗細", 1, 200, AddressOf Plain, Function(d) CInt(Math.Round(d.StrokeWidth * 1000)), Sub(d, v) d.StrokeWidth = v / 1000.0))
        AddRow(L, DrawRow("dw_soft", "柔邊", 0, 100, pct, Function(d) d.Softness, Sub(d, v) d.Softness = v))
        AddRow(L, DrawRow("dw_opacity", "不透明度", 0, 100, pct, Function(d) d.Opacity, Sub(d, v) d.Opacity = v))
        AddRow(L, DrawRow("dw_flow", "流量", 2, 100, pct, Function(d) d.Flow, Sub(d, v) d.Flow = v))
        BuildDrawAdvanced(L)

        _help.SetHelpLinked("draw.font", _drawFont, AddCaption(L, "圖說字型", L.Y), _drawFont)
        _drawFont.DropDownStyle = ComboBoxStyle.DropDownList
        _drawFont.Items.AddRange(TextFonts.Where(Function(f) f.Family <> TextRender.SevenSegmentFont).Select(Function(f) CObj(f.Name)).ToArray())
        _drawFont.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _drawFont.SelectedIndexChanged, Sub()
                                                        If _syncing OrElse _drawFont.SelectedIndex < 0 Then Return
                                                        Dim family = TextFonts(_drawFont.SelectedIndex).Family
                                                        SetDrawProp(Sub(d) d.FontName = family)
                                                    End Sub
        L.Add(_drawFont)
        L.Y += RowHeight
        AddRow(L, DrawRow("dw_textsize", "圖說字級", 10, 200, AddressOf Plain, Function(d) CInt(Math.Round(d.TextSize * 1000)), Sub(d, v) d.TextSize = v / 1000.0))

        AddHeading(L, "圖層")
        Dim names = {("新增", "draw.layer.add"), ("複製", "draw.layer.dup"), ("上移", "draw.layer.up"), ("下移", "draw.layer.down"), ("刪除", "draw.layer.delete")}
        Dim bw = (L.Width - 8 - 4 * 4) \ 5
        For i = 0 To names.Length - 1
            Dim b = MakeButton(names(i).Item1, names(i).Item2)
            b.SetBounds(8 + i * (bw + 4), L.Y + 2, bw, 28)
            Dim index = i
            AddHandler b.Click, Sub() LayerCommand(index)
            _layerButtons.Add(b)
            L.Add(b)
        Next
        L.Y += 36
        _layerTop = L.Y
        _layerList.SetBounds(8, L.Y, L.Width - 8, 150)
        _layerList.DrawMode = DrawMode.OwnerDrawFixed
        _layerList.ItemHeight = 28
        _layerList.IntegralHeight = False
        _layerList.BorderStyle = BorderStyle.FixedSingle
        AddHandler _layerList.DrawItem, AddressOf LayerList_DrawItem
        AddHandler _layerList.MouseDown, AddressOf LayerList_MouseDown
        AddHandler _layerList.SelectedIndexChanged, Sub()
                                                         If _syncing OrElse _layerList.SelectedIndex < 0 Then Return
                                                         SelectDrawLayer(LayerCount() - 1 - _layerList.SelectedIndex)
                                                     End Sub
        AddHandler _layerList.DoubleClick, Sub() RenameDrawLayer()
        _help.SetHelp("draw.layers", _layerList)
        L.Add(_layerList)
        AddHandler page.Resize, Sub() _layerList.Height = Math.Max(110, page.ClientSize.Height - _layerTop - 8)
    End Sub

    ''' <summary>筆刷格子固定 4 × 4，跟著面板寬度伸縮。</summary>
    Private Sub LayoutBrushTiles()
        Const GapPx = 4, TileH = 38
        Dim w = Math.Max(40, (_brushGrid.ClientSize.Width - GapPx * 3) \ 4)
        _brushGrid.SuspendLayout()
        For i = 0 To _brushTiles.Count - 1
            _brushTiles(i).SetBounds((i Mod 4) * (w + GapPx), (i \ 4) * (TileH + GapPx), w, TileH)
        Next
        _brushGrid.ResumeLayout()
    End Sub

    ''' <summary>綁定到「繪圖設定」的滑桿：有可調整的選取圖形時改它，同時改新圖形的設定。</summary>
    Private Function DrawRow(key As String, caption As String, min As Integer, max As Integer, format As Func(Of Integer, String),
                             getter As Func(Of DrawLayer, Integer), setter As Action(Of DrawLayer, Integer)) As SliderRow
        Return New SliderRow With {.Key = key, .Caption = caption, .Minimum = min, .Maximum = max, .Format = format,
            .GetValue = Function(r) getter(If(StyleTarget(r), _drawStyle)),
            .SetValue = Sub(r, v)
                            setter(_drawStyle, v)
                            Dim t = StyleTarget(r)
                            If t IsNot Nothing Then setter(t, v)
                        End Sub}
    End Function

    '=====================================================================
    ' 選取與設定
    '=====================================================================

    Private Function SelDraw(r As EditRecipe) As DrawLayer
        If r.Drawings Is Nothing OrElse _drawIndex < 0 OrElse _drawIndex >= r.Drawings.Count Then Return Nothing
        Return r.Drawings(_drawIndex)
    End Function

    Private Function LayerCount() As Integer
        Return If(_recipe.Drawings?.Count, 0)
    End Function

    ''' <summary>
    ''' 設定要改的圖層：選取的形狀（畫完後可再調整）；自由繪製圖層只在「選取」工具時才改，
    ''' 否則改的是下一筆的設定（換顏色接著畫會成為新圖層，不會把舊的筆畫一起改掉）。
    ''' </summary>
    Private Function StyleTarget(r As EditRecipe) As DrawLayer
        Dim sel = SelDraw(r)
        If sel Is Nothing Then Return Nothing
        ' 直接繪製時筆刷設定只給下一筆用；選取的圖層（不論種類）都不跟著改。
        If _drawStrip.SelectedTool = CInt(DrawShape.Raster) Then Return Nothing
        If (sel.Shape = DrawShape.Freehand OrElse sel.Shape = DrawShape.Raster) AndAlso _drawStrip.SelectedTool <> DrawIcons.SelectTool Then Return Nothing
        Return sel
    End Function

    Private Sub SetDrawProp(change As Action(Of DrawLayer))
        change(_drawStyle)
        If _photo IsNot Nothing Then
            ApplyChange(Sub(r)
                            Dim t = StyleTarget(r)
                            If t IsNot Nothing Then change(t)
                        End Sub)
        End If
        UpdateDrawControls()
    End Sub

    Private Sub SetDrawBrush(b As BrushKind)
        ' 混色、塗抹、仿製要讀取畫布，只能直接繪製在點陣圖層上。
        If DrawLayer.SamplesCanvas(b) AndAlso _drawStrip.SelectedTool <> CInt(DrawShape.Raster) Then
            _drawStrip.SelectedTool = CInt(DrawShape.Raster)
            SetStatusMessage($"「{DrawGeometry.BrushNames(CInt(b))}」會讀取照片的顏色，已切換到直接繪製（畫在點陣圖層上）。")
        End If
        SetDrawProp(Sub(d) d.Brush = b)
        ' 換到特效筆、紋理筆時，線條色換成目前效果／材質的建議顏色。
        Dim cats = CurrentEffectCategories()
        If cats IsNot Nothing Then
            Dim st = If(StyleTarget(_recipe), _drawStyle)
            Dim w = EffectCatalog.Locate(cats, EffectValue(st))
            ApplyEffectEntry(cats(w.Category).Items(w.Item))
        End If
    End Sub

    Private Sub PickDrawColor(getter As Func(Of DrawLayer, Integer), setter As Action(Of DrawLayer, Integer))
        Dim current = Color.FromArgb(getter(If(StyleTarget(_recipe), _drawStyle)))
        Using dlg As New ColorDialog With {.Color = current, .FullOpen = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim argb = dlg.Color.ToArgb()
            SetDrawProp(Sub(d) setter(d, argb))
        End Using
    End Sub

    Private Sub SelectDrawLayer(index As Integer)
        If index >= LayerCount() Then index = LayerCount() - 1
        _drawIndex = index
        Dim t = StyleTarget(_recipe)
        If t IsNot Nothing Then _drawStyle.CopyStyleFrom(t)
        SyncSliders()
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    Private Sub OnDrawToolChanged()
        _polyPoints = Nothing
        CommitCalloutEditor()
        ' 換成畫圖工具時放開選取的圖形（像小畫家），之後改筆刷、顏色只影響新畫的。
        ' 直接繪製例外：像 Photoshop 一樣畫在目前選取的圖層上。
        If _drawStrip.SelectedTool <> DrawIcons.SelectTool AndAlso _drawStrip.SelectedTool <> CInt(DrawShape.Raster) Then _drawIndex = -1
        _drawEraser.Visible = _drawStrip.SelectedTool = CInt(DrawShape.Raster)
        ' 換成畫圖工具時，選取中的自由繪製圖層不再是設定對象：同步面板。
        SyncSliders()
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    ''' <summary>配方或選取改變後更新繪圖分頁。</summary>
    Private Sub UpdateDrawControls()
        Dim n = LayerCount()
        If _drawIndex >= n Then _drawIndex = n - 1
        Dim wasSyncing = _syncing
        _syncing = True
        Try
            If _layerList.Items.Count <> n Then
                _layerList.Items.Clear()
                For i = 0 To n - 1
                    _layerList.Items.Add(i)
                Next
            End If
            _layerList.SelectedIndex = If(_drawIndex < 0, -1, n - 1 - _drawIndex)
            _layerList.Invalidate()
            Dim st = If(StyleTarget(_recipe), _drawStyle)
            For Each t In _brushTiles
                t.Selected = t.Brush = st.Brush
            Next
            UpdateBrushPreviews(st)
            Dim cats = CurrentEffectCategories(st)
            Dim where = If(cats Is Nothing, (Category:=-1, Item:=-1),
                           EffectCatalog.Locate(cats, EffectValue(st)))
            Dim catNames = If(cats Is Nothing, Array.Empty(Of String)(), cats.Select(Function(c) c.Name).ToArray())
            If Not catNames.SequenceEqual(_drawSubCat.Items.Cast(Of String)()) Then
                _drawSubCat.Items.Clear()
                _drawSubCat.Items.AddRange(catNames.Cast(Of Object)().ToArray())
            End If
            _drawSubCat.SelectedIndex = where.Category
            Dim itemNames = If(cats Is Nothing, Array.Empty(Of String)(), cats(where.Category).Items.Select(Function(i) i.Name).ToArray())
            If Not itemNames.SequenceEqual(_drawSub.Items.Cast(Of String)()) Then
                _drawSub.Items.Clear()
                _drawSub.Items.AddRange(itemNames.Cast(Of Object)().ToArray())
            End If
            _drawSub.SelectedIndex = where.Item
            _drawSubCat.Enabled = cats IsNot Nothing
            _drawSub.Enabled = cats IsNot Nothing
            SetSwatch(_drawStrokeColor, st.StrokeColorArgb)
            SetSwatch(_drawFillColor, st.FillColorArgb)
            SetSwatch(_drawTextColor, st.TextColorArgb)
            _drawStroke.Checked = st.Stroked
            _drawFill.Checked = st.Filled
            _drawShadow.Checked = st.Shadow
            UpdateDrawAdvanced(st)
            _drawFont.SelectedIndex = Math.Max(0, Array.FindIndex(TextFonts, Function(f) f.Family = st.FontName))
            Dim sel = SelDraw(_recipe)
            For i = 1 To _layerButtons.Count - 1
                _layerButtons(i).Enabled = sel IsNot Nothing
            Next
            UpdateDrawHint()
        Finally
            _syncing = wasSyncing
        End Try
    End Sub

    Private Shared Sub SetSwatch(b As Button, argb As Integer)
        Dim c = Color.FromArgb(255, Color.FromArgb(argb))
        b.BackColor = c
        b.Tag = ThemeManager.SkipTag ' 顏色樣本：深色配色時也保持本身的顏色
        b.ForeColor = If(c.GetBrightness() < 0.55, Color.White, Color.Black)
    End Sub

    ''' <summary>筆刷格子的預覽用目前的線條色（太淡時改用深灰）；特效與材質改變時重畫。</summary>
    Private Sub UpdateBrushPreviews(st As DrawLayer)
        Dim c = Color.FromArgb(255, Color.FromArgb(st.StrokeColorArgb))
        If ThemeManager.Dark Then
            If c.GetBrightness() < 0.25 Then c = Color.FromArgb(210, 214, 222) ' 深色格子上看得到
        ElseIf c.GetBrightness() > 0.85 Then
            c = Color.FromArgb(60, 64, 72)
        End If
        Dim key = $"{c.ToArgb()}|{st.Fx}|{st.Material}|{_brushGrid.Width}"
        If key = _brushPreviewKey OrElse _brushTiles.Count = 0 Then Return
        _brushPreviewKey = key
        Dim w = Math.Max(40, _brushTiles(0).Width - 4)
        ' 特效、紋理的格子用該效果／材質的建議顏色；光、火、動態、魔法類用深色底。
        Dim fxWhere = EffectCatalog.Locate(EffectCatalog.FxCategories, CInt(st.Fx))
        Dim fxEntry = EffectCatalog.FxCategories(fxWhere.Category).Items(fxWhere.Item)
        Dim matWhere = EffectCatalog.Locate(EffectCatalog.MaterialCategories, CInt(st.Material))
        Dim matEntry = EffectCatalog.MaterialCategories(matWhere.Category).Items(matWhere.Item)
        For Each t In _brushTiles
            Dim previewColor = c
            If t.Brush = BrushKind.FX Then previewColor = If(st.Brush = BrushKind.FX, Color.FromArgb(255, Color.FromArgb(st.StrokeColorArgb)), fxEntry.Color)
            If t.Brush = BrushKind.Texture Then previewColor = If(st.Brush = BrushKind.Texture, Color.FromArgb(255, Color.FromArgb(st.StrokeColorArgb)), matEntry.Color)
            t.Dark = t.Brush = BrushKind.FX AndAlso fxWhere.Category <> 2 AndAlso fxWhere.Category <> 5
            t.SetPreview(DrawingRenderer.BrushPreview(t.Brush, st.Fx, st.Material, w, 24, previewColor))
        Next
    End Sub

    ''' <summary>目前筆刷的分類目錄：特效筆、紋理筆各一份；其他筆刷回傳 Nothing。</summary>
    Private Function CurrentEffectCategories(Optional st As DrawLayer = Nothing) As EffectCatalog.Category()
        st = If(st, If(StyleTarget(_recipe), _drawStyle))
        If st.Brush = BrushKind.FX Then Return EffectCatalog.FxCategories
        If st.Brush = BrushKind.Texture Then Return EffectCatalog.MaterialCategories
        If st.Brush = BrushKind.Particle Then Return ParticleCategories
        If st.Brush = BrushKind.StickerHose Then
            ' 貼圖主題（內建＋貼圖資料夾），值為清單索引；選了主題不換線條色。
            Dim c = Color.FromArgb(st.StrokeColorArgb)
            Return {New EffectCatalog.Category With {.Name = "貼圖主題",
                .Items = HoseThemes().Select(Function(n, i) New EffectCatalog.Entry With {.Value = i, .Name = n, .Color = c, .Tinted = True}).ToArray()}}
        End If
        Return Nothing
    End Function

    Private Shared ReadOnly ParticleCategories As EffectCatalog.Category() = {
        New EffectCatalog.Category With {.Name = "粒子", .Items = {
            New EffectCatalog.Entry With {.Value = ParticleKind.Gravity, .Name = "重力（噴泉、毛髮、火花）", .Color = Color.FromArgb(60, 80, 160), .Tinted = True},
            New EffectCatalog.Entry With {.Value = ParticleKind.Flow, .Name = "流動（煙絲、水流、髮絲）", .Color = Color.FromArgb(70, 130, 190), .Tinted = True},
            New EffectCatalog.Entry With {.Value = ParticleKind.Spring, .Name = "彈簧（甩出一圈圈的線）", .Color = Color.FromArgb(40, 42, 52), .Tinted = True}}}}

    Private Shared Function HoseThemes() As List(Of String)
        Dim list As New List(Of String) From {StickerLibrary.BuiltInTheme}
        list.AddRange(StickerLibrary.Themes().Where(Function(t) t <> StickerLibrary.BuiltInTheme))
        Return list
    End Function

    ''' <summary>目前筆刷在「效果」清單裡的值：特效、材質、粒子種類，或貼圖主題的索引。</summary>
    Private Shared Function EffectValue(st As DrawLayer) As Integer
        Select Case st.Brush
            Case BrushKind.FX : Return CInt(st.Fx)
            Case BrushKind.Particle : Return CInt(st.Particle)
            Case BrushKind.StickerHose : Return Math.Max(0, HoseThemes().IndexOf(If(String.IsNullOrEmpty(st.HoseTheme), StickerLibrary.BuiltInTheme, st.HoseTheme)))
            Case Else : Return CInt(st.Material)
        End Select
    End Function

    ''' <summary>選了一個特效、材質或粒子種類：套用，線條色換成它的建議顏色；貼圖主題只換主題。</summary>
    Private Sub ApplyEffectEntry(entry As EffectCatalog.Entry)
        Dim argb = entry.Color.ToArgb()
        SetDrawProp(Sub(d)
                        Select Case d.Brush
                            Case BrushKind.FX : d.Fx = CType(entry.Value, FxKind)
                            Case BrushKind.Particle : d.Particle = CType(entry.Value, ParticleKind)
                            Case BrushKind.StickerHose
                                d.HoseTheme = entry.Name
                                Return
                            Case Else : d.Material = CType(entry.Value, MaterialKind)
                        End Select
                        d.StrokeColorArgb = argb
                    End Sub)
        If Not entry.Tinted Then SetStatusMessage($"「{entry.Name}」使用本身的顏色；線條色只會稍微影響或不影響。")
    End Sub

    Private Sub UpdateDrawHint()
        Dim tool = _drawStrip.SelectedTool
        Dim pen = If(_canvas.PenPressure.HasValue, "（繪圖筆：依筆壓改變粗細）", "")
        Dim text As String
        If _photo Is Nothing Then
            text = "先開啟一張照片。"
        ElseIf _polyPoints IsNot Nothing Then
            text = "點一下加一個頂點；點回第一點、按兩下或 Enter 完成，Esc 取消。"
        ElseIf tool = DrawIcons.SelectTool Then
            text = "點選圖形後可拖曳移動；藍色方塊縮放、圓點旋轉、黃點調整形狀。方向鍵微調，Del 刪除。"
        Else
            Dim shape = CType(tool, DrawShape)
            Select Case shape
                Case DrawShape.Raster
                    If _drawEraser.Checked Then
                        text = "橡皮擦：拖曳擦掉目前點陣圖層上畫過的地方（B 換回畫筆）。"
                    ElseIf _drawStyle.Brush = BrushKind.Clone Then
                        text = If(_cloneSource.HasValue, "仿製筆：拖曳把來源（十字準星）的照片畫過來；Alt＋點一下換來源。",
                                  "仿製筆：先按住 Alt 在照片上點一下，設定要仿製的來源。")
                    ElseIf _drawStyle.Brush = BrushKind.Smudge Then
                        text = "塗抹筆：拖曳把照片與下面圖層的顏色推開、抹勻（結果畫在點陣圖層，原圖不動）。"
                    ElseIf _drawStyle.Brush = BrushKind.Mixer Then
                        text = "混色筆：帶著線條色畫，同時沾起畫布上的顏色混在一起。"
                    Else
                        text = "直接畫在目前的點陣圖層上，畫完就是像素；沒有選圖層時自動新增。E 切換橡皮擦。"
                    End If
                    text &= pen
                Case DrawShape.Freehand : text = "在照片上拖曳畫線（向量，之後可以再調整）。換顏色或筆刷後再畫，會成為新的圖層。" & pen
                Case DrawShape.Polygon : text = "點一下開始，逐點畫出多邊形。"
                Case DrawShape.Line : text = "拖曳畫出直線（Shift：45° 角）；畫好後可拖曳兩端。"
                Case DrawShape.Bezier : text = "拖曳畫出曲線，再拖曳黃色控制點調整彎曲。"
                Case Else
                    text = $"拖曳畫出{DrawGeometry.ShapeNames(tool)}（Shift：等比例）；畫好後可縮放、旋轉"
                    If DrawGeometry.IsCallout(shape) Then
                        text &= "，拖曳黃點改變指向，按兩下輸入文字。"
                    ElseIf DrawGeometry.ParamHandles(New DrawLayer With {.Shape = shape, .Param1 = 0.3, .Param2 = 0.3}).Count > 0 Then
                        text &= "，拖曳黃點調整形狀。"
                    Else
                        text &= "。"
                    End If
            End Select
        End If
        _drawHint.Text = text
    End Sub

    '=====================================================================
    ' 圖層清單
    '=====================================================================

    Private Sub LayerList_DrawItem(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 OrElse e.Index >= LayerCount() Then Return
        Dim index = LayerCount() - 1 - e.Index
        Dim d = _recipe.Drawings(index)
        Dim g = e.Graphics
        Dim r = e.Bounds
        Dim selected = index = _drawIndex
        Using bg As New SolidBrush(ThemeManager.Back(If(selected, Color.FromArgb(210, 228, 250), Color.White)))
            g.FillRectangle(bg, r)
        End Using
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim fg = ThemeManager.Fore(If(d.Visible, Color.FromArgb(40, 44, 52), Color.FromArgb(160, 165, 172)))
        DrawEye(g, New RectangleF(r.X + 4, r.Y + 6, 18, 16), d.Visible)
        DrawLock(g, New RectangleF(r.X + 26, r.Y + 6, 16, 16), d.Locked)
        DrawIcons.DrawTool(g, CInt(d.Shape), New RectangleF(r.X + 48, r.Y + 4, 20, 20), fg)
        Dim name = If(String.IsNullOrEmpty(d.Name), DrawGeometry.ShapeNames(CInt(d.Shape)), d.Name)
        TextRenderer.DrawText(g, name, _layerList.Font, New Rectangle(r.X + 74, r.Y, r.Width - 74 - 46, r.Height), fg,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        TextRenderer.DrawText(g, d.Opacity & "%", _layerList.Font, New Rectangle(r.Right - 46, r.Y, 42, r.Height), ThemeManager.Fore(Color.FromArgb(130, 136, 146)),
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.Right)
        Using line As New Pen(ThemeManager.Line(Color.FromArgb(232, 235, 240)))
            g.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1)
        End Using
    End Sub

    Private Shared Sub DrawEye(g As Graphics, r As RectangleF, open As Boolean)
        Dim c = ThemeManager.Fore(If(open, Color.FromArgb(60, 66, 78), Color.FromArgb(175, 180, 188)))
        Using pen As New Pen(c, 1.4F), br As New SolidBrush(c), path As New GraphicsPath()
            Dim cy = r.Y + r.Height / 2
            path.AddBezier(r.Left, cy, r.Left + r.Width * 0.3F, r.Top + 1, r.Right - r.Width * 0.3F, r.Top + 1, r.Right, cy)
            path.AddBezier(r.Right, cy, r.Right - r.Width * 0.3F, r.Bottom - 1, r.Left + r.Width * 0.3F, r.Bottom - 1, r.Left, cy)
            g.DrawPath(pen, path)
            g.FillEllipse(br, r.X + r.Width / 2 - 2.6F, cy - 2.6F, 5.2F, 5.2F)
            If Not open Then g.DrawLine(pen, r.Left + 2, r.Bottom - 1, r.Right - 2, r.Top + 1)
        End Using
    End Sub

    Private Shared Sub DrawLock(g As Graphics, r As RectangleF, locked As Boolean)
        Dim c = If(locked, Color.FromArgb(178, 75, 18), Color.FromArgb(185, 190, 198))
        Using pen As New Pen(c, 1.4F)
            Dim body = New RectangleF(r.X + 2, r.Y + r.Height * 0.45F, r.Width - 4, r.Height * 0.52F)
            g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height)
            Dim sw = body.Width * 0.62F
            Dim sx = body.X + (body.Width - sw) / 2 + If(locked, 0, body.Width * 0.28F)
            g.DrawArc(pen, sx, r.Y + 1, sw, r.Height * 0.62F, 180, 180)
            If locked Then g.FillRectangle(New SolidBrush(c), body.X + body.Width / 2 - 1, body.Y + 2, 2, body.Height - 4)
        End Using
    End Sub

    ''' <summary>點眼睛切換顯示、點鎖頭切換鎖定。</summary>
    Private Sub LayerList_MouseDown(sender As Object, e As MouseEventArgs)
        Dim row = _layerList.IndexFromPoint(e.Location)
        If row < 0 OrElse row >= LayerCount() OrElse e.X > 46 Then Return
        Dim index = LayerCount() - 1 - row
        If e.X < 24 Then
            ApplyChange(Sub(r) r.Drawings(index).Visible = Not r.Drawings(index).Visible)
        Else
            ApplyChange(Sub(r) r.Drawings(index).Locked = Not r.Drawings(index).Locked)
        End If
        _canvas.Invalidate()
    End Sub

    Private Sub RenameDrawLayer()
        Dim d = SelDraw(_recipe)
        If d Is Nothing Then Return
        Dim name = Microsoft.VisualBasic.Interaction.InputBox("圖層名稱：", AppName, d.Name)
        If String.IsNullOrWhiteSpace(name) Then Return
        Dim index = _drawIndex
        ApplyChange(Sub(r) r.Drawings(index).Name = name.Trim())
    End Sub

    ''' <summary>0 新增、1 複製、2 上移、3 下移、4 刪除。</summary>
    Private Sub LayerCommand(command As Integer)
        If _photo Is Nothing Then Return
        Dim index = _drawIndex
        Select Case command
            Case 0
                ' 像 Photoshop：新增的是空白的點陣圖層，接著用直接繪製畫在上面。
                _drawStrip.SelectedTool = CInt(DrawShape.Raster)
                AddDrawLayer(NewRasterLayer())
            Case 1
                Dim original = SelDraw(_recipe)
                If original Is Nothing Then Return
                Dim d = original.Clone()
                DrawGeometry.Offset(d, 0.02, 0.02)
                d.Name &= " 複本"
                d.Id = Nothing
                ApplyChange(Sub(r)
                                r.Drawings.Add(d)
                                LayerStack.PlaceAbove(r, d, original) ' 複本放在原圖層正上方
                            End Sub)
                SelectDrawLayer(_recipe.Drawings.IndexOf(d))
            Case 2, 3
                ' 只在繪圖圖層之間上下移動；和文字貼圖交錯的順序在「圖層」分頁調整。
                Dim target = index + If(command = 2, 1, -1)
                If index < 0 OrElse target < 0 OrElse target >= LayerCount() Then Return
                ApplyChange(Sub(r) LayerStack.Swap(r, r.Drawings(index), r.Drawings(target)))
                SelectDrawLayer(target)
            Case 4
                DeleteDrawLayer()
        End Select
    End Sub

    Private Sub AddDrawLayer(d As DrawLayer)
        ApplyChange(Sub(r)
                        If r.Drawings Is Nothing Then r.Drawings = New List(Of DrawLayer)()
                        r.Drawings.Add(d)
                    End Sub)
        SelectDrawLayer(LayerCount() - 1)
    End Sub

    Private Sub DeleteDrawLayer()
        If SelDraw(_recipe) Is Nothing Then Return
        CommitCalloutEditor()
        Dim index = _drawIndex
        ApplyChange(Sub(r) r.Drawings.RemoveAt(index))
        SelectDrawLayer(Math.Min(index, LayerCount() - 1))
    End Sub

    '=====================================================================
    ' 座標換算：畫布 ↔ 照片高度單位
    '=====================================================================

    Private Function PhotoPixelSize() As Size
        If Not _framePhotoSize.IsEmpty Then Return _framePhotoSize
        Return If(_rendered Is Nothing, New Size(1, 1), _rendered.Size)
    End Function

    Private Function PhotoAspect() As Double
        Dim s = PhotoPixelSize()
        Return s.Width / CDbl(Math.Max(1, s.Height))
    End Function

    Private Function ScreenToUnit(p As Point) As PointF
        Dim q = DisplayToPhoto(_canvas.ClientToNormalized(p))
        Return New PointF(CSng(q.X * PhotoAspect()), q.Y)
    End Function

    Private Function UnitToScreen(u As PointF) As PointF
        Dim n = PhotoToDisplay(New PointF(CSng(u.X / PhotoAspect()), u.Y))
        Dim b = _canvas.ImageBounds()
        Return New PointF(b.X + n.X * b.Width, b.Y + n.Y * b.Height)
    End Function

    ''' <summary>畫面上一個單位（照片高度）有幾個螢幕像素。</summary>
    Private Function PxPerUnit() As Single
        Dim b = _canvas.ImageBounds()
        Dim ph = PhotoPixelSize().Height
        Dim framedH = If(_framePhotoSize.IsEmpty, ph, FramedSize.Height)
        Return CSng(b.Height * ph / Math.Max(1, framedH))
    End Function

    Private Function HitTolerance() As Double
        Return 5 / Math.Max(0.0001, PxPerUnit())
    End Function

    Private Shared Function ScreenDist(a As PointF, b As PointF) As Double
        Return Math.Sqrt((a.X - b.X) ^ 2 + (a.Y - b.Y) ^ 2)
    End Function

    '=====================================================================
    ' 控制點
    '=====================================================================

    Private Enum HandleKind
        Resize
        Rotate
        Param
        Vertex
        RectScale
    End Enum

    Private Structure DrawHandle
        Public Kind As HandleKind
        Public Index As Integer
        Public Pos As PointF
    End Structure

    ''' <summary>選取圖形的控制點（螢幕座標），點選時由後往前找：黃點優先。</summary>
    Private Function HandlesOf(d As DrawLayer) As List(Of DrawHandle)
        Dim list As New List(Of DrawHandle)()
        If d.Shape = DrawShape.Raster Then
            ' 點陣圖層只能整個移動，沒有控制點。
        ElseIf DrawGeometry.IsBox(d.Shape) Then
            For i = 0 To 7
                list.Add(New DrawHandle With {.Kind = HandleKind.Resize, .Index = i, .Pos = UnitToScreen(DrawGeometry.LocalToWorld(d, DrawGeometry.BoxHandleLocal(d, i)))})
            Next
            list.Add(New DrawHandle With {.Kind = HandleKind.Rotate, .Pos = RotateHandlePos(d)})
            Dim ps = DrawGeometry.ParamHandles(d)
            For i = 0 To ps.Count - 1
                list.Add(New DrawHandle With {.Kind = HandleKind.Param, .Index = i, .Pos = UnitToScreen(ps(i))})
            Next
        ElseIf DrawGeometry.IsPointShape(d.Shape) AndAlso d.Points IsNot Nothing Then
            For i = 0 To d.Points.Count - 1
                list.Add(New DrawHandle With {.Kind = HandleKind.Vertex, .Index = i, .Pos = UnitToScreen(d.Points(i).ToPointF())})
            Next
        Else
            Dim b = DrawGeometry.Bounds(d)
            If Not b.IsEmpty OrElse b.Width > 0 OrElse b.Height > 0 Then
                For i = 0 To 7
                    list.Add(New DrawHandle With {.Kind = HandleKind.RectScale, .Index = i, .Pos = UnitToScreen(DrawGeometry.RectHandle(b, i))})
                Next
            End If
        End If
        Return list
    End Function

    Private Function RotateHandlePos(d As DrawLayer) As PointF
        Dim top = UnitToScreen(DrawGeometry.LocalToWorld(d, New PointF(0, CSng(-Math.Abs(d.H) / 2))))
        Dim dir = DrawGeometry.Rotate(New PointF(0, -RotateHandleOffset), d.Rotation)
        Return New PointF(top.X + dir.X, top.Y + dir.Y)
    End Function

    Private Function HitHandle(d As DrawLayer, p As Point) As DrawHandle?
        Dim hs = HandlesOf(d)
        For i = hs.Count - 1 To 0 Step -1
            If ScreenDist(hs(i).Pos, p) <= HandleRadius + 1 Then Return hs(i)
        Next
        Return Nothing
    End Function

    ''' <summary>由上往下找點到的圖層（隱藏、鎖定的略過）；沒有時回傳 -1。</summary>
    Private Function HitLayer(u As PointF) As Integer
        If _recipe.Drawings Is Nothing Then Return -1
        Dim tol = HitTolerance()
        For i = _recipe.Drawings.Count - 1 To 0 Step -1
            Dim d = _recipe.Drawings(i)
            If d.Visible AndAlso Not d.Locked AndAlso DrawGeometry.HitTest(d, u, tol) Then Return i
        Next
        Return -1
    End Function

    '=====================================================================
    ' 滑鼠
    '=====================================================================

    Private Enum DrawDrag
        None
        Create
        Freehand
        Move
        Resize
        Rotate
        Param
        Vertex
        RectScale
    End Enum

    Private _dd As DrawDrag
    Private _ddStart As PointF
    Private _ddCur As PointF
    Private _ddLayer As DrawLayer
    Private _ddHandle As Integer
    Private _ddBounds As RectangleF
    Private _ddSerial As Integer
    Private _freePoints As List(Of DrawPoint)
    Private _freePressure As Single = 1
    Private _freeLastScreen As Point
    Private _freeLastTime As Integer
    Private _polyPoints As List(Of PointF)
    Private _polyHover As PointF

    Private Sub DrawMouseDown(e As MouseEventArgs) Implements PreviewCanvas.IDrawHost.DrawMouseDown
        If _photo Is Nothing Then Return
        If _tabs.SelectedIndex = TabSelect Then SelMouseDown(e) : Return
        CommitCalloutEditor()
        Dim u = ScreenToUnit(e.Location)
        If _polyPoints IsNot Nothing Then
            If _polyPoints.Count >= 3 AndAlso ScreenDist(UnitToScreen(_polyPoints(0)), e.Location) <= 9 Then
                FinishPolygon()
            Else
                _polyPoints.Add(u)
            End If
            UpdateDrawHint()
            _canvas.Invalidate()
            Return
        End If
        ' 對稱繪圖以照片中心為準（照片裁切後中心會變，每一筆都重新設定）。
        If _drawStyle.Symmetry <> SymmetryKind.None Then
            _drawStyle.SymX = Math.Round(PhotoAspect() / 2, 5) : _drawStyle.SymY = 0.5
        End If
        ' 混色、塗抹、仿製只能畫在點陣圖層：在向量的自由繪製裡選到時改成直接繪製。
        If _drawStrip.SelectedTool = CInt(DrawShape.Freehand) AndAlso DrawLayer.SamplesCanvas(_drawStyle.Brush) Then
            _drawStrip.SelectedTool = CInt(DrawShape.Raster)
        End If
        If _drawStrip.SelectedTool = CInt(DrawShape.Raster) Then
            If _drawStyle.Brush = BrushKind.Clone AndAlso Not _drawEraser.Checked Then
                If ModifierKeys.HasFlag(Keys.Alt) Then
                    SetCloneSource(u)
                    Return
                End If
                If Not _cloneSource.HasValue Then
                    SetStatusMessage("仿製筆：先按住 Alt 在照片上點一下，設定要仿製的來源。")
                    Return
                End If
            End If
            BeginRasterStroke(e, u)
            _canvas.Invalidate()
            Return
        End If

        Dim sel = SelDraw(_recipe)
        If sel IsNot Nothing AndAlso sel.Visible AndAlso Not sel.Locked Then
            Dim h = HitHandle(sel, e.Location)
            If h.HasValue Then
                StartHandleDrag(sel, h.Value, u)
                Return
            End If
        End If

        Dim tool = _drawStrip.SelectedTool
        Dim onSelected = sel IsNot Nothing AndAlso sel.Visible AndAlso Not sel.Locked AndAlso DrawGeometry.HitTest(sel, u, HitTolerance()) AndAlso
                         tool <> CInt(DrawShape.Freehand)
        If tool = DrawIcons.SelectTool OrElse onSelected Then
            Dim index = If(onSelected, _drawIndex, HitLayer(u))
            SelectDrawLayer(index)
            If index >= 0 Then
                _dd = DrawDrag.Move
                _ddStart = u
                _ddLayer = SelDraw(_recipe).Clone()
                _ddSerial += 1
            End If
            Return
        End If

        Select Case CType(tool, DrawShape)
            Case DrawShape.Freehand
                _dd = DrawDrag.Freehand
                _freeRaster = False
                _freePoints = New List(Of DrawPoint) From {StrokePoint(u, e.Location, True)}
            Case DrawShape.Polygon
                _polyPoints = New List(Of PointF) From {u}
                _polyHover = u
                UpdateDrawHint()
            Case Else
                _dd = DrawDrag.Create
                _ddStart = u
                _ddCur = u
        End Select
        _canvas.Invalidate()
    End Sub

    Private Sub StartHandleDrag(sel As DrawLayer, h As DrawHandle, u As PointF)
        _ddLayer = sel.Clone()
        _ddHandle = h.Index
        _ddStart = u
        _ddSerial += 1
        Select Case h.Kind
            Case HandleKind.Resize : _dd = DrawDrag.Resize
            Case HandleKind.Rotate : _dd = DrawDrag.Rotate
            Case HandleKind.Param : _dd = DrawDrag.Param
            Case HandleKind.Vertex : _dd = DrawDrag.Vertex
            Case HandleKind.RectScale
                _dd = DrawDrag.RectScale
                _ddBounds = DrawGeometry.Bounds(sel)
        End Select
    End Sub

    ''' <summary>筆壓：繪圖筆用真的筆壓；滑鼠在毛筆、墨水時依速度模擬（越快越細），其餘為 1。</summary>
    Private Function StrokePressure(p As Point, first As Boolean) As Single
        Dim pen = _canvas.PenPressure
        If pen.HasValue Then Return pen.Value
        If Not DrawingRenderer.SimulatesPressure(_drawStyle.Brush) Then Return 1
        Dim now = Environment.TickCount
        If first Then
            _freeLastTime = now
            _freeLastScreen = p
            _freePressure = 0.8F
            Return _freePressure
        End If
        Dim dt = Math.Max(1, now - _freeLastTime)
        Dim speed = ScreenDist(p, _freeLastScreen) / dt ' 像素 / 毫秒
        Dim target = CSng(Math.Max(0.25, Math.Min(1, 1.1 - speed * 0.35)))
        _freePressure = _freePressure * 0.75F + target * 0.25F
        _freeLastTime = now
        _freeLastScreen = p
        Return _freePressure
    End Function

    Private Sub DrawMouseMove(e As MouseEventArgs) Implements PreviewCanvas.IDrawHost.DrawMouseMove
        If _photo Is Nothing Then Return
        If _tabs.SelectedIndex = TabSelect Then SelMouseMove(e) : Return
        Dim u = ScreenToUnit(e.Location)
        Dim shift = ModifierKeys.HasFlag(Keys.Shift)
        Select Case _dd
            Case DrawDrag.None
                If _polyPoints IsNot Nothing Then
                    _polyHover = u
                    _canvas.Invalidate()
                End If
                UpdateDrawCursor(e.Location, u)
            Case DrawDrag.Create
                _ddCur = u
                _canvas.Invalidate()
            Case DrawDrag.Freehand
                Dim last = UnitToScreen(_freePoints(_freePoints.Count - 1).ToPointF())
                Dim minStep = Math.Max(1.5, _drawStyle.StrokeWidth * PxPerUnit() / 8)
                If ScreenDist(last, e.Location) >= minStep Then
                    _freePoints.Add(StrokePoint(u, e.Location, False))
                    _canvas.Invalidate()
                End If
            Case DrawDrag.Move
                Dim dx = u.X - _ddStart.X, dy = u.Y - _ddStart.Y
                ReplaceDragLayer(Sub(d) DrawGeometry.Offset(d, dx, dy))
            Case DrawDrag.Resize
                ReplaceDragLayer(Sub(d) DrawGeometry.ResizeBox(d, _ddLayer, _ddHandle, u, shift))
            Case DrawDrag.Rotate
                Dim deg = Math.Atan2(u.Y - _ddLayer.Y, u.X - _ddLayer.X) * 180 / Math.PI + 90
                If shift Then deg = Math.Round(deg / 15) * 15
                If deg > 180 Then deg -= 360
                ReplaceDragLayer(Sub(d) d.Rotation = Math.Round(deg, 1))
            Case DrawDrag.Param
                ReplaceDragLayer(Sub(d) DrawGeometry.SetParamHandle(d, _ddHandle, u))
            Case DrawDrag.Vertex
                ReplaceDragLayer(Sub(d) d.Points(_ddHandle) = New DrawPoint(u.X, u.Y))
            Case DrawDrag.RectScale
                Dim nb = DrawGeometry.DragRect(_ddBounds, _ddHandle, u, shift)
                ReplaceDragLayer(Sub(d) DrawGeometry.ScalePoints(d, _ddLayer, _ddBounds, nb))
        End Select
    End Sub

    ''' <summary>拖曳中：以按下時的圖層為準套用變化（整段拖曳在復原紀錄裡算一步）。</summary>
    Private Sub ReplaceDragLayer(change As Action(Of DrawLayer))
        Dim index = _drawIndex
        If index < 0 OrElse index >= LayerCount() Then Return
        Dim d = _ddLayer.Clone()
        change(d)
        ApplyChange(Sub(r) r.Drawings(index) = d, "draw-drag-" & _ddSerial)
        _canvas.Invalidate()
    End Sub

    Private Sub UpdateDrawCursor(p As Point, u As PointF)
        Dim sel = SelDraw(_recipe)
        Dim c = If(_drawStrip.SelectedTool = DrawIcons.SelectTool, Cursors.Default, Cursors.Cross)
        If sel IsNot Nothing AndAlso sel.Visible AndAlso Not sel.Locked AndAlso _drawStrip.SelectedTool <> CInt(DrawShape.Raster) Then
            Dim h = HitHandle(sel, p)
            If h.HasValue Then
                Select Case h.Value.Kind
                    Case HandleKind.Rotate : c = Cursors.Hand
                    Case HandleKind.Param, HandleKind.Vertex : c = Cursors.Hand
                    Case Else : c = If(h.Value.Index Mod 4 = 1, Cursors.SizeNS, If(h.Value.Index Mod 4 = 3, Cursors.SizeWE,
                                       If(h.Value.Index Mod 4 = 0, Cursors.SizeNWSE, Cursors.SizeNESW)))
                End Select
            ElseIf _drawStrip.SelectedTool <> CInt(DrawShape.Freehand) AndAlso DrawGeometry.HitTest(sel, u, HitTolerance()) Then
                c = Cursors.SizeAll
            End If
        End If
        If _canvas.Cursor IsNot c Then _canvas.Cursor = c
    End Sub

    Private Sub DrawMouseUp(e As MouseEventArgs) Implements PreviewCanvas.IDrawHost.DrawMouseUp
        If _tabs.SelectedIndex = TabSelect Then SelMouseUp(e) : Return
        Dim kind = _dd
        _dd = DrawDrag.None
        Select Case kind
            Case DrawDrag.Create
                Dim d = NewShape(CType(_drawStrip.SelectedTool, DrawShape), _ddStart, _ddCur, ModifierKeys.HasFlag(Keys.Shift), _drawRandom.Next(1, 100000))
                If d IsNot Nothing Then
                    d.Name = DrawGeometry.ShapeNames(CInt(d.Shape)) & " " & (LayerCount() + 1)
                    AddDrawLayer(d)
                    If DrawGeometry.IsCallout(d.Shape) Then OpenCalloutEditor()
                End If
            Case DrawDrag.Freehand
                CommitFreehand()
        End Select
        _canvas.Invalidate()
    End Sub

    Private Sub DrawDoubleClick(e As MouseEventArgs) Implements PreviewCanvas.IDrawHost.DrawDoubleClick
        If _tabs.SelectedIndex = TabSelect Then SelDoubleClick(e) : Return
        If _polyPoints IsNot Nothing Then
            FinishPolygon()
            Return
        End If
        Dim sel = SelDraw(_recipe)
        If sel IsNot Nothing AndAlso DrawGeometry.IsCallout(sel.Shape) AndAlso Not sel.Locked AndAlso
           DrawGeometry.HitTest(sel, ScreenToUnit(e.Location), HitTolerance()) Then OpenCalloutEditor()
    End Sub

    ''' <summary>拖曳範圍做出新形狀；拖曳太短時用預設大小（點一下就有一個）。直線與曲線太短則不建立。</summary>
    Private Function NewShape(shape As DrawShape, a As PointF, b As PointF, shift As Boolean, seed As Integer) As DrawLayer
        Dim dragPx = ScreenDist(a, b) * PxPerUnit()
        Dim d As New DrawLayer With {.Shape = shape, .Seed = seed}
        d.CopyStyleFrom(_drawStyle)
        Select Case shape
            Case DrawShape.Line, DrawShape.Bezier
                If dragPx < 4 Then Return Nothing
                If shift Then b = Snap45(a, b)
                If shape = DrawShape.Line Then
                    d.Points = New List(Of DrawPoint) From {New DrawPoint(a.X, a.Y), New DrawPoint(b.X, b.Y)}
                Else
                    ' 預設成 S 形，再拖黃點調整。
                    Dim nx = -(b.Y - a.Y) * 0.3F, ny = (b.X - a.X) * 0.3F
                    d.Points = New List(Of DrawPoint) From {
                        New DrawPoint(a.X, a.Y),
                        New DrawPoint(a.X + (b.X - a.X) / 3 + nx, a.Y + (b.Y - a.Y) / 3 + ny),
                        New DrawPoint(a.X + (b.X - a.X) * 2 / 3 - nx, a.Y + (b.Y - a.Y) * 2 / 3 - ny),
                        New DrawPoint(b.X, b.Y)}
                End If
            Case Else
                Dim w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y)
                Dim cx, cy As Single
                If dragPx < 6 Then
                    Dim square = shape = DrawShape.Heart OrElse shape = DrawShape.Star4 OrElse shape = DrawShape.Star5 OrElse shape = DrawShape.Star6
                    w = If(square, 0.18F, 0.24F) : h = If(square, 0.18F, 0.15F)
                    cx = a.X : cy = a.Y
                Else
                    If shift Then
                        Dim s = Math.Max(w, h)
                        w = s : h = s
                    End If
                    cx = a.X + Math.Sign(b.X - a.X) * w / 2
                    cy = a.Y + Math.Sign(b.Y - a.Y) * h / 2
                End If
                d.X = cx : d.Y = cy : d.W = Math.Max(0.004, w) : d.H = Math.Max(0.004, h)
                DrawGeometry.ApplyDefaults(d)
                If DrawGeometry.IsCallout(shape) Then
                    d.Filled = True
                    d.Stroked = True
                End If
        End Select
        Return d
    End Function

    Private Shared Function Snap45(a As PointF, b As PointF) As PointF
        Dim dx = b.X - a.X, dy = b.Y - a.Y
        Dim len = Math.Sqrt(dx * dx + dy * dy)
        Dim ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4)
        Return New PointF(CSng(a.X + len * Math.Cos(ang)), CSng(a.Y + len * Math.Sin(ang)))
    End Function

    ''' <summary>一筆畫完：接在選取的同設定自由繪製圖層上，否則成為新圖層。</summary>
    Private Sub CommitFreehand()
        Dim pts = _freePoints
        _freePoints = Nothing
        If pts Is Nothing OrElse pts.Count = 0 Then Return
        Dim stroke As New DrawStroke With {.Points = pts.Select(Function(p) New DrawPoint(CSng(Math.Round(p.X, 5)), CSng(Math.Round(p.Y, 5)), CSng(Math.Round(p.P, 3))) With {.Tx = CSng(Math.Round(p.Tx)), .Ty = CSng(Math.Round(p.Ty)), .R = CSng(Math.Round(p.R))}).ToList()}
        If _freeRaster Then
            CommitRasterStroke(stroke)
            Return
        End If
        Dim sel = SelDraw(_recipe)
        If sel IsNot Nothing AndAlso sel.Shape = DrawShape.Freehand AndAlso sel.Visible AndAlso Not sel.Locked AndAlso sel.SameStroke(_drawStyle) Then
            Dim index = _drawIndex
            ApplyChange(Sub(r)
                            If r.Drawings(index).Strokes Is Nothing Then r.Drawings(index).Strokes = New List(Of DrawStroke)()
                            r.Drawings(index).Strokes.Add(stroke)
                        End Sub)
            UpdateDrawControls()
        Else
            Dim d As New DrawLayer With {.Shape = DrawShape.Freehand, .Strokes = New List(Of DrawStroke) From {stroke}, .Seed = _drawRandom.Next(1, 100000)}
            d.CopyStyleFrom(_drawStyle)
            Dim kind = If(d.Brush = BrushKind.FX, EffectCatalog.FxNames(CInt(d.Fx)), If(d.Brush = BrushKind.Texture, EffectCatalog.MaterialNames(CInt(d.Material)), DrawGeometry.BrushNames(CInt(d.Brush))))
            d.Name = $"筆畫（{kind}）{LayerCount() + 1}"
            AddDrawLayer(d)
        End If
    End Sub

    Private Sub FinishPolygon()
        Dim pts = _polyPoints
        _polyPoints = Nothing
        If pts IsNot Nothing Then
            ' 按兩下會在同一處多加點：去掉相鄰太近的點。
            Dim clean As New List(Of PointF)()
            For Each p In pts
                If clean.Count = 0 OrElse ScreenDist(UnitToScreen(clean(clean.Count - 1)), UnitToScreen(p)) > 3 Then clean.Add(p)
            Next
            If clean.Count >= 3 AndAlso ScreenDist(UnitToScreen(clean(0)), UnitToScreen(clean(clean.Count - 1))) <= 3 Then clean.RemoveAt(clean.Count - 1)
            If clean.Count >= 3 Then
                Dim d As New DrawLayer With {.Shape = DrawShape.Polygon, .Seed = _drawRandom.Next(1, 100000),
                                             .Points = clean.Select(Function(p) New DrawPoint(p.X, p.Y)).ToList()}
                d.CopyStyleFrom(_drawStyle)
                d.Name = DrawGeometry.ShapeNames(CInt(DrawShape.Polygon)) & " " & (LayerCount() + 1)
                AddDrawLayer(d)
            End If
        End If
        UpdateDrawHint()
        _canvas.Invalidate()
    End Sub

    ''' <summary>「完成」／Enter：結束多邊形或文字輸入，否則取消選取。</summary>
    Private Sub FinishDrawAction()
        If _calloutEditor IsNot Nothing AndAlso _calloutEditor.Visible Then
            CommitCalloutEditor()
        ElseIf _polyPoints IsNot Nothing Then
            FinishPolygon()
        Else
            SelectDrawLayer(-1)
        End If
        _canvas.Focus()
    End Sub

    ''' <summary>繪圖分頁的快捷鍵：Del 刪除、Esc 取消、Enter 完成、方向鍵微調（Shift 10 倍）。</summary>
    Private Function HandleDrawKey(keyData As Keys) As Boolean
        If _tabs.SelectedIndex <> TabDraw OrElse _photo Is Nothing Then Return False
        Select Case keyData
            Case Keys.E, Keys.B
                ' 像 Photoshop：B 畫筆、E 橡皮擦（都是直接繪製）。
                _drawStrip.SelectedTool = CInt(DrawShape.Raster)
                _drawEraser.Checked = keyData = Keys.E
                Return True
            Case Keys.Delete
                If SelDraw(_recipe) Is Nothing Then Return False
                DeleteDrawLayer()
                Return True
            Case Keys.Escape
                If _calloutEditor IsNot Nothing AndAlso _calloutEditor.Visible Then
                    CommitCalloutEditor()
                    _canvas.Focus()
                ElseIf _polyPoints IsNot Nothing Then
                    _polyPoints = Nothing
                    UpdateDrawHint()
                    _canvas.Invalidate()
                ElseIf SelDraw(_recipe) IsNot Nothing Then
                    SelectDrawLayer(-1)
                Else
                    Return False
                End If
                Return True
            Case Keys.Enter
                If _polyPoints Is Nothing AndAlso SelDraw(_recipe) Is Nothing Then Return False
                FinishDrawAction()
                Return True
            Case Keys.Left, Keys.Right, Keys.Up, Keys.Down,
                 Keys.Shift Or Keys.Left, Keys.Shift Or Keys.Right, Keys.Shift Or Keys.Up, Keys.Shift Or Keys.Down
                Dim sel = SelDraw(_recipe)
                If sel Is Nothing OrElse sel.Locked Then Return False
                Dim stepPx = If(keyData.HasFlag(Keys.Shift), 10, 1) / Math.Max(0.0001, PxPerUnit())
                Dim k = keyData And Keys.KeyCode
                Dim dx = If(k = Keys.Left, -stepPx, If(k = Keys.Right, stepPx, 0))
                Dim dy = If(k = Keys.Up, -stepPx, If(k = Keys.Down, stepPx, 0))
                Dim index = _drawIndex
                ApplyChange(Sub(r) DrawGeometry.Offset(r.Drawings(index), dx, dy), "draw-nudge")
                Return True
        End Select
        Return False
    End Function

    '=====================================================================
    ' 圖說文字：在畫布上直接輸入
    '=====================================================================

    Private _calloutEditor As TextBox
    Private _calloutEditIndex As Integer = -1
    Private _calloutSyncing As Boolean

    Private Sub OpenCalloutEditor()
        Dim d = SelDraw(_recipe)
        If d Is Nothing OrElse Not DrawGeometry.IsCallout(d.Shape) Then Return
        If _calloutEditor Is Nothing Then
            _calloutEditor = New TextBox With {.Multiline = True, .AcceptsReturn = True, .BorderStyle = BorderStyle.FixedSingle,
                                               .TextAlign = HorizontalAlignment.Center, .Visible = False}
            AddHandler _calloutEditor.TextChanged, Sub()
                                                       If _calloutSyncing OrElse _calloutEditIndex < 0 OrElse _calloutEditIndex >= LayerCount() Then Return
                                                       Dim index = _calloutEditIndex, text = _calloutEditor.Text
                                                       ApplyChange(Sub(r) r.Drawings(index).Text = text, "callout-text-" & _ddSerial)
                                                   End Sub
            AddHandler _calloutEditor.Leave, Sub() CommitCalloutEditor()
            _canvas.Controls.Add(_calloutEditor)
        End If
        ' 文字範圍（未旋轉）換成畫面上的外接矩形。
        Dim box = DrawGeometry.TextBox(d)
        Dim corners = {New PointF(box.Left, box.Top), New PointF(box.Right, box.Top), New PointF(box.Right, box.Bottom), New PointF(box.Left, box.Bottom)}.
            Select(Function(p) UnitToScreen(DrawGeometry.LocalToWorld(d, p))).ToArray()
        Dim area = RectangleF.FromLTRB(corners.Min(Function(p) p.X), corners.Min(Function(p) p.Y), corners.Max(Function(p) p.X), corners.Max(Function(p) p.Y))
        Dim w = Math.Max(80, CInt(area.Width)), h = Math.Max(34, CInt(area.Height))
        _calloutEditor.SetBounds(CInt(area.X + area.Width / 2 - w / 2), CInt(area.Y + area.Height / 2 - h / 2), w, h)
        Dim old = _calloutEditor.Font
        _calloutEditor.Font = New Font(d.FontName, Math.Max(9, Math.Min(48, CSng(d.TextSize * PxPerUnit()))), If(d.TextBold, FontStyle.Bold, FontStyle.Regular), GraphicsUnit.Pixel)
        If old IsNot Nothing AndAlso old IsNot Font Then old.Dispose()
        _calloutSyncing = True
        _calloutEditor.Text = d.Text
        _calloutSyncing = False
        _calloutEditIndex = _drawIndex
        _ddSerial += 1
        _calloutEditor.Visible = True
        _calloutEditor.BringToFront()
        _calloutEditor.Focus()
        _calloutEditor.SelectAll()
    End Sub

    Private Sub CommitCalloutEditor()
        If _calloutEditor Is Nothing OrElse Not _calloutEditor.Visible Then Return
        _calloutEditIndex = -1
        _calloutEditor.Visible = False
    End Sub

    '=====================================================================
    ' 畫布上的選取框、控制點與繪製中的預覽
    '=====================================================================

    Private Sub DrawPaint(g As Graphics) Implements PreviewCanvas.IDrawHost.DrawPaint
        If _photo Is Nothing Then Return
        If _tabs.SelectedIndex = TabSelect Then SelPaint(g) : Return
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim px = PxPerUnit()
        Dim sel = SelDraw(_recipe)
        If sel IsNot Nothing AndAlso sel.Visible Then
            Using outer As New Pen(Color.FromArgb(150, 0, 0, 0), 3), inner As New Pen(Color.FromArgb(90, 170, 255), 1.3F) With {.DashStyle = DashStyle.Dash}
                For Each f In DrawGeometry.Figures(sel)
                    Dim pts = f.Points.Select(Function(p) UnitToScreen(p)).ToArray()
                    If pts.Length < 2 Then Continue For
                    If f.Closed Then
                        g.DrawPolygon(outer, pts) : g.DrawPolygon(inner, pts)
                    Else
                        g.DrawLines(outer, pts) : g.DrawLines(inner, pts)
                    End If
                Next
                If sel.Shape = DrawShape.Raster Then
                    ' 點陣圖層：畫過範圍的虛線框。
                    Dim rb = DrawGeometry.RasterBounds(sel)
                    If Not rb.IsEmpty Then
                        Dim a = UnitToScreen(rb.Location), b = UnitToScreen(New PointF(rb.Right, rb.Bottom))
                        Dim box = RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y)
                        g.DrawRectangle(outer, box.X, box.Y, box.Width, box.Height)
                        g.DrawRectangle(inner, box.X, box.Y, box.Width, box.Height)
                    End If
                ElseIf DrawGeometry.IsBox(sel.Shape) Then
                    ' 方框類另外畫出外框與旋轉把手的連線。
                    Dim box = Enumerable.Range(0, 8).Where(Function(i) i Mod 2 = 0).
                        Select(Function(i) UnitToScreen(DrawGeometry.LocalToWorld(sel, DrawGeometry.BoxHandleLocal(sel, i)))).ToArray()
                    Using frame As New Pen(Color.FromArgb(120, 90, 170, 255), 1) With {.DashStyle = DashStyle.Dot}
                        g.DrawPolygon(frame, box)
                        Dim top = UnitToScreen(DrawGeometry.LocalToWorld(sel, New PointF(0, CSng(-Math.Abs(sel.H) / 2))))
                        g.DrawLine(frame, top, RotateHandlePos(sel))
                    End Using
                ElseIf sel.Shape = DrawShape.Bezier AndAlso sel.Points?.Count >= 4 Then
                    Using dash As New Pen(Color.FromArgb(200, 255, 200, 60), 1) With {.DashStyle = DashStyle.Dash}
                        g.DrawLine(dash, UnitToScreen(sel.Points(0).ToPointF()), UnitToScreen(sel.Points(1).ToPointF()))
                        g.DrawLine(dash, UnitToScreen(sel.Points(3).ToPointF()), UnitToScreen(sel.Points(2).ToPointF()))
                    End Using
                End If
            End Using
            If Not sel.Locked Then
                For Each h In HandlesOf(sel)
                    DrawHandleMark(g, h, sel)
                Next
            End If
        End If

        ' 繪製中的預覽
        If _dd = DrawDrag.Create Then
            Dim tmp = NewShape(CType(_drawStrip.SelectedTool, DrawShape), _ddStart, _ddCur, ModifierKeys.HasFlag(Keys.Shift), 7)
            If tmp IsNot Nothing Then PreviewOutline(g, tmp, px)
        ElseIf _dd = DrawDrag.Freehand AndAlso _freePoints IsNot Nothing Then
            Dim c = Color.FromArgb(CInt(Math.Max(40, _drawStyle.Opacity * 2.55)), Color.FromArgb(_drawStyle.StrokeColorArgb))
            ' 橡皮擦：半透明白色，看得出擦過的路徑。
            If _freeRaster AndAlso _drawEraser.Checked Then c = Color.FromArgb(150, 255, 255, 255)
            Dim w = CSng(Math.Max(1, _drawStyle.StrokeWidth * px))
            Using pen As New Pen(c, w) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                Dim pts = _freePoints.Select(Function(p) UnitToScreen(p.ToPointF())).ToArray()
                If pts.Length = 1 Then
                    Using br As New SolidBrush(c)
                        g.FillEllipse(br, pts(0).X - w / 2, pts(0).Y - w / 2, w, w)
                    End Using
                Else
                    g.DrawLines(pen, pts)
                    PaintSymmetryPreview(g, pen, _freePoints)
                End If
            End Using
        End If
        If _polyPoints IsNot Nothing Then
            Dim pts = _polyPoints.Concat({_polyHover}).Select(Function(p) UnitToScreen(p)).ToArray()
            Using outer As New Pen(Color.FromArgb(150, 0, 0, 0), 3), pen As New Pen(Color.FromArgb(_drawStyle.StrokeColorArgb), 1.6F)
                If pts.Length >= 2 Then
                    g.DrawLines(outer, pts)
                    g.DrawLines(pen, pts)
                End If
            End Using
            Dim first = pts(0)
            g.FillEllipse(Brushes.White, first.X - 5, first.Y - 5, 10, 10)
            g.DrawEllipse(Pens.Black, first.X - 5, first.Y - 5, 10, 10)
        End If
        PaintCloneSource(g)
    End Sub

    ''' <summary>拖曳中的新形狀：用目前的線條色與粗細畫外形（實際筆刷效果放開後才算）。</summary>
    Private Sub PreviewOutline(g As Graphics, d As DrawLayer, px As Single)
        Dim c = Color.FromArgb(CInt(Math.Max(60, d.Opacity * 2.55)), Color.FromArgb(d.StrokeColorArgb))
        Using pen As New Pen(c, CSng(Math.Max(1, d.StrokeWidth * px))) With {.LineJoin = LineJoin.Round, .StartCap = LineCap.Round, .EndCap = LineCap.Round},
              fill As New SolidBrush(Color.FromArgb(110, Color.FromArgb(d.FillColorArgb)))
            For Each f In DrawGeometry.Figures(d)
                Dim pts = f.Points.Select(Function(p) UnitToScreen(p)).ToArray()
                If pts.Length < 2 Then Continue For
                If f.Closed Then
                    If d.Filled Then g.FillPolygon(fill, pts)
                    If d.Stroked Then g.DrawPolygon(pen, pts)
                Else
                    g.DrawLines(pen, pts)
                End If
            Next
        End Using
    End Sub

    Private Sub DrawHandleMark(g As Graphics, h As DrawHandle, sel As DrawLayer)
        Dim p = h.Pos
        Select Case h.Kind
            Case HandleKind.Resize, HandleKind.RectScale
                g.FillRectangle(Brushes.White, p.X - 4.5F, p.Y - 4.5F, 9, 9)
                Using pen As New Pen(Color.FromArgb(24, 95, 165), 1.4F)
                    g.DrawRectangle(pen, p.X - 4.5F, p.Y - 4.5F, 9, 9)
                End Using
            Case HandleKind.Rotate
                g.FillEllipse(Brushes.White, p.X - 6, p.Y - 6, 12, 12)
                Using pen As New Pen(Color.FromArgb(24, 95, 165), 1.6F)
                    g.DrawEllipse(pen, p.X - 6, p.Y - 6, 12, 12)
                    g.DrawArc(pen, p.X - 3, p.Y - 3, 6, 6, -60, 270)
                End Using
            Case HandleKind.Param
                Dim diamond = {New PointF(p.X, p.Y - 6), New PointF(p.X + 6, p.Y), New PointF(p.X, p.Y + 6), New PointF(p.X - 6, p.Y)}
                Using br As New SolidBrush(Color.FromArgb(239, 159, 39)), pen As New Pen(Color.FromArgb(133, 79, 11), 1.2F)
                    g.FillPolygon(br, diamond)
                    g.DrawPolygon(pen, diamond)
                End Using
            Case HandleKind.Vertex
                Dim control = sel.Shape = DrawShape.Bezier AndAlso (h.Index = 1 OrElse h.Index = 2)
                If control Then
                    Using br As New SolidBrush(Color.FromArgb(239, 159, 39)), pen As New Pen(Color.FromArgb(133, 79, 11), 1.2F)
                        g.FillEllipse(br, p.X - 5.5F, p.Y - 5.5F, 11, 11)
                        g.DrawEllipse(pen, p.X - 5.5F, p.Y - 5.5F, 11, 11)
                    End Using
                Else
                    g.FillRectangle(Brushes.White, p.X - 4.5F, p.Y - 4.5F, 9, 9)
                    Using pen As New Pen(Color.FromArgb(24, 95, 165), 1.4F)
                        g.DrawRectangle(pen, p.X - 4.5F, p.Y - 4.5F, 9, 9)
                    End Using
                End If
        End Select
    End Sub

    '=====================================================================
    ' 筆刷格子
    '=====================================================================

    Private Class BrushTile
        Inherits Control

        Public ReadOnly Brush As BrushKind
        Private _preview As Bitmap
        Private _selected As Boolean
        Private _hover As Boolean
        Private _dark As Boolean
        Private Shared ReadOnly NameFont As New Font("Microsoft JhengHei UI", 8.5F)

        Public Sub New(b As BrushKind)
            Brush = b
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Cursor = Cursors.Hand
            AccessibleName = DrawGeometry.BrushNames(CInt(b))
            AccessibleRole = AccessibleRole.RadioButton
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

        ''' <summary>預覽用深色底（火焰、煙霧、星光在白底上看不清楚）。</summary>
        Public Property Dark As Boolean
            Get
                Return _dark
            End Get
            Set(value As Boolean)
                _dark = value
                Invalidate()
            End Set
        End Property

        Public Sub SetPreview(bmp As Bitmap)
            _preview?.Dispose()
            _preview = bmp
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Dim r = New Rectangle(0, 0, Width - 1, Height - 1)
            g.Clear(ThemeManager.Back(Color.FromArgb(236, 238, 242)))
            Dim top = New Rectangle(2, 2, Width - 4, Height - 18)
            Using bg As New SolidBrush(If(_dark, Color.FromArgb(70, 72, 80), If(ThemeManager.Dark, Color.FromArgb(44, 48, 56), Color.White)))
                g.FillRectangle(bg, top)
            End Using
            If _preview IsNot Nothing Then
                g.DrawImage(_preview, top.X + (top.Width - _preview.Width) \ 2, top.Y + (top.Height - _preview.Height) \ 2)
            End If
            TextRenderer.DrawText(g, DrawGeometry.BrushNames(CInt(Brush)), NameFont, New Rectangle(0, Height - 17, Width, 16),
                                  ThemeManager.Fore(If(_selected, Color.FromArgb(24, 95, 165), Color.FromArgb(70, 76, 88))), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Using pen As New Pen(If(_selected, Color.FromArgb(55, 138, 221), If(_hover, Color.FromArgb(150, 170, 200), ThemeManager.Line(Color.FromArgb(205, 210, 218)))), If(_selected, 2, 1))
                g.DrawRectangle(pen, If(_selected, New Rectangle(1, 1, Width - 2, Height - 2), r))
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

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _preview?.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
