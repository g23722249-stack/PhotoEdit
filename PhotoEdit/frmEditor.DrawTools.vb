Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 左側工具列的新工具與色彩面板（工具列本身見 DrawToolStrip）：
''' 平移、縮放、吸管；點陣的橡皮擦、仿製／修復、塗抹／混色、模糊／銳利化、加亮／加深／海綿（都是直接繪製換筆或橡皮擦）；
''' 文字、形狀建立、刀具；以及工具列底部的色彩面板（和右側面板的線條色、填色同步，都改同一個畫筆設定）。
''' 任何工具按住滑鼠中鍵拖曳都是平移。
''' </summary>
Partial Friend Class frmEditor

    ''' <summary>上次用的一般筆刷：從仿製、模糊這些工具換回「筆刷」時恢復。</summary>
    Private _lastPaintBrush As BrushKind = BrushKind.HardRound
    Private _applyingTool As Boolean
    Private _panLast As Point?
    Private _zoomStart As Point?
    Private _zoomCur As Point
    Private _zoomOut As Boolean
    Private _knifeA As PointF?
    Private _knifeB As PointF
    Private _recentTick As Integer = Integer.MinValue

    Private Shared Function KeepsSelection(tool As Integer) As Boolean
        Return tool = DrawIcons.SelectTool OrElse tool = DrawIcons.PanTool OrElse tool = DrawIcons.ZoomTool OrElse
               tool = DrawIcons.EyedropperTool OrElse tool = DrawIcons.ShapeBuilderTool OrElse tool = DrawIcons.KnifeTool OrElse
               tool = DrawIcons.ShadowTool OrElse tool = DrawIcons.TransparencyTool OrElse tool = DrawIcons.EnvelopeTool
    End Function

    '---------------------------------------------------------------------
    ' 工具列與色彩面板的連接
    '---------------------------------------------------------------------

    Private Sub WireToolStrip()
        _drawStrip.HelpSetter = Sub(c, k) _help.SetHelp(k, c)
        For Each b As DrawToolStrip.ToolButton In _drawStrip.Buttons
            _help.SetHelp(DrawIcons.HelpKey(b.Tool), b)
        Next
        AddHandler _drawStrip.ColorPicked, Sub(t, argb) PickStripColor(t, argb)
        AddHandler _drawStrip.ColorBoxClicked, Sub(t) ShowColorWindow(If(t = 0, TargetStroke, TargetFill))
        AddHandler _drawStrip.SwapColors, Sub() SetDrawProp(Sub(d)
                                                                Dim s = d.StrokeColorArgb
                                                                d.StrokeColorArgb = d.FillColorArgb
                                                                d.FillColorArgb = s
                                                            End Sub)
        AddHandler _drawStrip.NoFill, Sub() SetDrawProp(Sub(d) d.Filled = False)
        AddHandler _drawStrip.ResetColors, Sub() SetDrawProp(Sub(d)
                                                                 d.StrokeColorArgb = Color.Black.ToArgb()
                                                                 d.FillColorArgb = Color.White.ToArgb()
                                                             End Sub)
        If Not DrawLayer.SamplesCanvas(_drawStyle.Brush) Then _lastPaintBrush = _drawStyle.Brush
        RefreshStripRecent(force:=True)
    End Sub

    ''' <summary>色盤：左鍵線條色、右鍵填色（同時打開填色）；記進最近用過的顏色。</summary>
    Private Sub PickStripColor(target As Integer, argb As Integer)
        If target = 1 Then
            SetDrawProp(Sub(d)
                            d.FillColorArgb = argb
                            d.Filled = True
                        End Sub)
        Else
            SetDrawProp(Sub(d) d.StrokeColorArgb = argb)
        End If
        Dim c = Color.FromArgb(255, Color.FromArgb(argb))
        Aqua.ColorPicker.RememberRecentColor(c)
        If _colorWin IsNot Nothing AndAlso Not _colorWin.IsDisposed Then _colorWin.Picker.RecentColors = Aqua.ColorPicker.SavedRecentColors()
        RefreshStripRecent(force:=True)
    End Sub

    Private Sub RefreshStripRecent(Optional force As Boolean = False)
        Dim now = Environment.TickCount
        If Not force AndAlso now - _recentTick < 1500 Then Return
        _recentTick = now
        _drawStrip.SetRecent(Aqua.ColorPicker.SavedRecentColors())
    End Sub

    ''' <summary>畫筆設定改了（UpdateDrawControls）：色彩面板跟著右側面板顯示同樣的顏色，點陣工具的按鈕跟著筆刷、橡皮擦反白。</summary>
    Private Sub SyncToolStrip(st As DrawLayer)
        _drawStrip.SetColors(st.StrokeColorArgb, st.FillColorArgb, st.Filled)
        RefreshStripRecent()
        SyncToolHighlight()
    End Sub

    Private Sub SyncToolHighlight()
        If _applyingTool OrElse _drawStrip.SelectedTool <> CInt(DrawShape.Raster) Then Return
        Dim id = If(_drawEraser.Checked, DrawIcons.EraserTool, DrawIcons.ToolOfBrush(_drawStyle.Brush))
        If id <> _drawStrip.SelectedId Then _drawStrip.Highlight(id)
    End Sub

    ''' <summary>選了點陣工具：橡皮擦開關、換成對應的筆（換回「筆刷」時恢復上次的一般筆刷）。</summary>
    Private Sub ApplyToolVariant()
        Dim id = _drawStrip.SelectedId
        If DrawIcons.EngineTool(id) <> CInt(DrawShape.Raster) Then Return
        _applyingTool = True
        Try
            _drawEraser.Checked = id = DrawIcons.EraserTool
            Dim b = DrawIcons.BrushOf(id)
            If b.HasValue Then
                If _drawStyle.Brush <> b.Value Then SetDrawBrush(b.Value)
            ElseIf id = CInt(DrawShape.Raster) AndAlso DrawLayer.SamplesCanvas(_drawStyle.Brush) Then
                SetDrawBrush(_lastPaintBrush)
            End If
        Finally
            _applyingTool = False
        End Try
    End Sub

    ''' <summary>換工具之後：形狀建立工具在已經選好圖形時直接開始。</summary>
    Private Sub AfterToolChanged()
        If _drawStrip.SelectedTool = DrawIcons.ShapeBuilderTool AndAlso Not _builderOn AndAlso CombinableSelection() IsNot Nothing Then EnterBuilder()
    End Sub

    '---------------------------------------------------------------------
    ' 滑鼠（frmEditor.Draw.vb 呼叫）
    '---------------------------------------------------------------------

    ''' <summary>平移、縮放、吸管、刀具、形狀建立（還沒開始時當選取用）；中鍵拖曳任何工具都是平移。回傳 True＝已處理。</summary>
    Private Function ToolMouseDown(e As MouseEventArgs, u As PointF) As Boolean
        If e.Button = MouseButtons.Middle Then
            _panLast = e.Location
            Return True
        End If
        If FxMouseDown(e, u) Then Return True ' 陰影、透明度、封套（見 frmEditor.DrawBatch3.vb）
        Select Case _drawStrip.SelectedTool
            Case DrawIcons.PanTool
                _panLast = e.Location
                _canvas.Cursor = Cursors.SizeAll
                Return True
            Case DrawIcons.ZoomTool
                _zoomStart = e.Location
                _zoomCur = e.Location
                _zoomOut = e.Button = MouseButtons.Right OrElse ModifierKeys.HasFlag(Keys.Alt)
                Return True
            Case DrawIcons.EyedropperTool
                BeginAltPick(e.Location)
                If e.Button = MouseButtons.Right Then
                    _altPickTarget = TargetFill
                    AltPickAt(e.Location)
                End If
                Return True
            Case DrawIcons.SmartFillTool
                If e.Button = MouseButtons.Left Then SmartFillAt(u)
                Return True
            Case DrawIcons.KnifeTool
                If e.Button = MouseButtons.Left Then
                    _knifeA = u
                    _knifeB = u
                End If
                Return True
            Case DrawIcons.ShapeBuilderTool
                If Not _builderOn AndAlso e.Button = MouseButtons.Left Then PickOrMove(e, u) ' 先選圖形（點選、框選），選好就開始
                Return Not _builderOn
        End Select
        Return False
    End Function

    Private Function ToolMouseMove(e As MouseEventArgs, u As PointF) As Boolean
        If _panLast.HasValue Then
            _canvas.PanBy(e.X - _panLast.Value.X, e.Y - _panLast.Value.Y)
            _panLast = e.Location
            Return True
        End If
        If FxMouseMove(e, u) Then Return True
        Select Case _drawStrip.SelectedTool
            Case DrawIcons.PanTool
                If _canvas.Cursor IsNot Cursors.Hand Then _canvas.Cursor = Cursors.Hand
                Return True
            Case DrawIcons.ZoomTool
                If _zoomStart.HasValue Then
                    _zoomCur = e.Location
                    _canvas.Invalidate()
                End If
                If _canvas.Cursor IsNot Cursors.Cross Then _canvas.Cursor = Cursors.Cross
                Return True
            Case DrawIcons.EyedropperTool
                If _canvas.Cursor IsNot DropperCursor() Then _canvas.Cursor = DropperCursor()
                Return Not _altPicking
            Case DrawIcons.SmartFillTool
                If _canvas.Cursor IsNot Cursors.Cross Then _canvas.Cursor = Cursors.Cross
                Return True
            Case DrawIcons.KnifeTool
                If _knifeA.HasValue Then
                    Dim b = u
                    If ModifierKeys.HasFlag(Keys.Shift) Then
                        ' Shift：45° 的倍數
                        Dim a = _knifeA.Value
                        Dim ang = Math.Round(Math.Atan2(b.Y - a.Y, b.X - a.X) / (Math.PI / 4)) * (Math.PI / 4)
                        Dim len = DrawGeometry.Dist(a, b)
                        b = New PointF(CSng(a.X + Math.Cos(ang) * len), CSng(a.Y + Math.Sin(ang) * len))
                    End If
                    _knifeB = b
                    _canvas.Invalidate()
                End If
                If _canvas.Cursor IsNot Cursors.Cross Then _canvas.Cursor = Cursors.Cross
                Return True
        End Select
        Return False
    End Function

    Private Function ToolMouseUp(e As MouseEventArgs) As Boolean
        If _panLast.HasValue Then
            _panLast = Nothing
            Return True
        End If
        If FxMouseUp() Then Return True
        Select Case _drawStrip.SelectedTool
            Case DrawIcons.ZoomTool
                If Not _zoomStart.HasValue Then Return True
                Dim a = _zoomStart.Value, b = e.Location
                _zoomStart = Nothing
                Dim r = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))
                If r.Width > 8 AndAlso r.Height > 8 AndAlso Not _zoomOut Then
                    ' 拖一個框：放大到框住的範圍
                    Dim k = Math.Min(_canvas.ClientSize.Width / CDbl(r.Width), _canvas.ClientSize.Height / CDbl(r.Height))
                    Dim c = New Point(r.X + r.Width \ 2, r.Y + r.Height \ 2)
                    _canvas.SetZoom(_canvas.EffectiveZoom() * k, c)
                    _canvas.PanBy(_canvas.ClientSize.Width \ 2 - c.X, _canvas.ClientSize.Height \ 2 - c.Y)
                Else
                    _canvas.SetZoom(_canvas.EffectiveZoom() * If(_zoomOut, 1 / 1.5, 1.5), b)
                End If
                _canvas.Invalidate()
                Return True
            Case DrawIcons.KnifeTool
                If _knifeA.HasValue Then
                    Dim a = _knifeA.Value
                    _knifeA = Nothing
                    KnifeCut(a, _knifeB)
                End If
                _canvas.Invalidate()
                Return True
        End Select
        Return False
    End Function

    ''' <summary>形狀建立工具：選好兩個以上的封閉圖形就開始切塊。</summary>
    Private Sub AfterBuilderPick()
        If _drawStrip.SelectedTool = DrawIcons.ShapeBuilderTool AndAlso Not _builderOn AndAlso CombinableSelection() IsNot Nothing Then
            EnterBuilder()
            UpdateDrawControls()
        End If
    End Sub

    '---------------------------------------------------------------------
    ' 刀具
    '---------------------------------------------------------------------

    ''' <summary>沿 a→b 切開：有選取時只切選取的，沒有時切刀線穿過的所有向量圖形。</summary>
    Private Sub KnifeCut(a As PointF, b As PointF)
        If ScreenDist(UnitToScreen(a), UnitToScreen(b)) < 4 Then Return
        Dim entered = EnteredGroup()
        Dim sel = SelectedIndices()
        Dim candidates = If(sel.Count > 0, sel, Enumerable.Range(0, LayerCount()).ToList()).
            Where(Function(i)
                      Dim d = _recipe.Drawings(i)
                      Return d.Visible AndAlso Not d.Locked AndAlso PathKnife.CanCut(d) AndAlso DrawGroups.IsInside(_recipe, d, entered)
                  End Function).ToList()
        Dim results As New List(Of (Index As Integer, Pieces As List(Of DrawLayer)))()
        For Each i In candidates
            Dim pieces = PathKnife.Cut(_recipe.Drawings(i), a, b)
            If pieces IsNot Nothing AndAlso pieces.Count >= 2 Then results.Add((i, pieces))
        Next
        If results.Count = 0 Then
            SetStatusMessage(Lang.T("刀線要整個穿過圖形才切得開（圖說、文字、點陣圖層不能切）。"))
            Return
        End If
        ApplyChange(Sub(r)
                        For Each x In results.OrderByDescending(Function(y) y.Index)
                            Dim keepId = r.Drawings(x.Index).Id
                            r.Drawings.RemoveAt(x.Index)
                            InsertPieces(r, x.Index, x.Pieces, keepId)
                        Next
                    End Sub)
        Dim all = results.SelectMany(Function(x) x.Pieces).ToList()
        Dim idx = all.Select(Function(p) _recipe.Drawings.IndexOf(p)).Where(Function(i) i >= 0).ToList()
        If idx.Count > 0 Then SetMultiSelection(idx, idx.Max())
        SetStatusMessage(Lang.F("已切開：{0} 個圖形變成 {1} 個（都是鋼筆路徑）。", results.Count, all.Count))
        UpdateDrawControls()
    End Sub

    ''' <summary>把切出來的幾塊放進 Drawings 的 index（原圖層已移除）：第一塊沿用原圖層在堆疊裡的位置，其餘依序疊在上面。</summary>
    Private Shared Sub InsertPieces(r As EditRecipe, index As Integer, pieces As IList(Of DrawLayer), keepId As String)
        For k = 0 To pieces.Count - 1
            pieces(k).Id = If(k = 0, keepId, Nothing)
        Next
        r.Drawings.InsertRange(index, pieces)
        For k = 1 To pieces.Count - 1
            LayerStack.PlaceAbove(r, pieces(k), pieces(k - 1))
        Next
    End Sub

    '---------------------------------------------------------------------
    ' 畫面
    '---------------------------------------------------------------------

    Private Sub PaintTools(g As Graphics)
        If _knifeA.HasValue Then
            Dim a = UnitToScreen(_knifeA.Value), b = UnitToScreen(_knifeB)
            Using outer As New Pen(Color.FromArgb(160, 255, 255, 255), 3), pen As New Pen(Color.FromArgb(226, 75, 74), 1.5F) With {.DashStyle = DashStyle.Dash}
                g.DrawLine(outer, a, b) : g.DrawLine(pen, a, b)
            End Using
        End If
        If _zoomStart.HasValue AndAlso Not _zoomOut Then
            Dim a = _zoomStart.Value
            Dim r = Rectangle.FromLTRB(Math.Min(a.X, _zoomCur.X), Math.Min(a.Y, _zoomCur.Y), Math.Max(a.X, _zoomCur.X), Math.Max(a.Y, _zoomCur.Y))
            If r.Width > 8 AndAlso r.Height > 8 Then
                Using pen As New Pen(Color.FromArgb(220, 55, 138, 221), 1) With {.DashStyle = DashStyle.Dash}
                    g.DrawRectangle(pen, r)
                End Using
            End If
        End If
    End Sub

    ''' <summary>新工具的操作提示；不是新工具時回傳 Nothing。</summary>
    Private Function ToolHint(tool As Integer) As String
        If FxHint(tool) IsNot Nothing Then Return FxHint(tool)
        Select Case tool
            Case DrawIcons.PanTool : Return Lang.T("平移：拖曳移動畫面（放大後才需要）。任何工具按住滑鼠中鍵拖曳也可以平移。")
            Case DrawIcons.ZoomTool : Return Lang.T("縮放：點一下放大、Alt＋點（或右鍵）縮小，拖一個框放大到那個範圍。")
            Case DrawIcons.EyedropperTool : Return Lang.T("吸管：點一下吸成線條色，右鍵（或 Shift＋點）吸成填色；可以按住拖曳看顏色。")
            Case DrawIcons.SmartFillTool : Return Lang.T("智慧填色：在線條、圖形圍起來的地方點一下，那塊變成新的圖形（用目前的填色，可以先在右側選漸層）。線條要接起來才填得到。")
            Case DrawIcons.KnifeTool : Return Lang.T("刀具：拖一條線穿過圖形把它切開（Shift：45°）；有選取時只切選取的。結果是鋼筆路徑。")
            Case DrawIcons.ShapeBuilderTool
                Return If(_builderOn, Nothing, Lang.T("形狀建立：先點選或框選兩個以上重疊的封閉圖形，選好就切成一塊一塊。"))
            Case CInt(DrawShape.Text) : Return Lang.T("文字：拖一個框（或點一下）輸入文字；字型、字級、文字色在右側面板。勾「外框」「填色」就是文字方塊。")
        End Select
        Return Nothing
    End Function

    ''' <summary>修飾筆的提示（直接繪製時）；不是時回傳 Nothing。</summary>
    Private Function RetouchHint(b As BrushKind) As String
        Select Case b
            Case BrushKind.Heal : Return If(_cloneSource.HasValue, Lang.T("修復筆：拖曳把來源（十字準星）的紋理畫過來，顏色亮度自動融入周圍；Alt＋點一下換來源。"),
                                            Lang.T("修復筆：先按住 Alt 在照片上點一下，設定要取樣的來源（像仿製筆）。"))
            Case BrushKind.Blur : Return Lang.T("模糊：在照片上塗抹讓那裡變柔和（越塗越糊；不透明度控制強度）。結果畫在點陣圖層，原圖不動。")
            Case BrushKind.Sharpen : Return Lang.T("銳利化：塗抹讓細節更清楚（別塗太多次會出現雜點）。結果畫在點陣圖層。")
            Case BrushKind.Dodge : Return Lang.T("加亮：塗抹讓那裡變亮（同暗房的遮光）。結果畫在點陣圖層。")
            Case BrushKind.Burn : Return Lang.T("加深：塗抹讓那裡變暗（同暗房的加光）。結果畫在點陣圖層。")
            Case BrushKind.SpongeDesaturate : Return Lang.T("海綿（降低飽和）：塗抹讓顏色變淡、接近灰色。結果畫在點陣圖層。")
            Case BrushKind.SpongeSaturate : Return Lang.T("海綿（增加飽和）：塗抹讓顏色更鮮豔。結果畫在點陣圖層。")
        End Select
        Return Nothing
    End Function
End Class
