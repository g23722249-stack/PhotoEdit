Imports System.Drawing.Drawing2D
Imports System.IO
Imports PhotoEdit

''' <summary>
''' 智慧填色工具、向量圖形的漸層填色（右側「填色方式」與畫面上的漸層控制點）、匯出 SVG、路徑文字（文字沿路徑）。
''' 運算見 SmartFill、DrawingRenderer.AbsoluteGradient、SvgExport、DrawGeometry.PathTextLayout。
''' </summary>
Partial Friend Class frmEditor

    ' ---- 漸層填色 ----
    Private ReadOnly _fillMode As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _drawFill2Color As New Button()
    Private Const TargetFill2 As Integer = 3
    ''' <summary>拖曳中的漸層控制點：1＝起點、2＝終點。</summary>
    Private _gradDrag As Integer
    Private Shared ReadOnly FillModeNames As String() = {Lang.T("單色"), Lang.T("線性漸層"), Lang.T("放射漸層"), Lang.T("圖樣")}
    ' ---- 路徑文字 ----
    Private ReadOnly _pathTextBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                               .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0)}
    Private _btnAttachText As Button
    Private _btnFlipText As Button
    Private _btnDetachText As Button
    Private ReadOnly _textShift As New TextBox With {.Width = 36, .Margin = New Padding(0, 3, 4, 0), .TextAlign = HorizontalAlignment.Right}

    '---------------------------------------------------------------------
    ' 智慧填色
    '---------------------------------------------------------------------

    ''' <summary>點一下：線條圍起來的地方變成新的圖形（填色用目前的填色，可以是漸層）。</summary>
    Private Sub SmartFillAt(u As PointF)
        Dim walls = Enumerable.Range(0, LayerCount()).Where(Function(i) SmartFill.IsWall(_recipe.Drawings(i))).ToList()
        Dim rings As List(Of PointF()) = Nothing
        Cursor = Cursors.WaitCursor
        Try
            rings = SmartFill.Region(walls.Select(Function(i) _recipe.Drawings(i)), PhotoAspect(), u)
        Finally
            Cursor = Cursors.Default
        End Try
        If rings Is Nothing OrElse rings.Count = 0 Then
            SetStatusMessage(Lang.T("這裡沒有被線條或圖形圍起來（填到照片邊緣了）；線條要接起來才填得到。"))
            Return
        End If
        Dim layer = SmartFill.MakeLayer(rings, _drawStyle)
        If layer Is Nothing Then Return
        layer.Name = Lang.T("智慧填色 ") & (LayerCount() + 1)
        layer.GroupId = EnteredGroup()
        ' 放在圍住它的圖形最下面那個的下方（線條蓋在填色上面）
        Dim rb = DrawGeometry.Bounds(layer)
        Dim touching = walls.Where(Function(i)
                                       Dim wd = _recipe.Drawings(i)
                                       Dim pad = CSng(wd.StrokeWidth + 0.004) ' 填色的邊在線的內緣，比線的中心往內縮半個線寬
                                       Return RectangleF.Inflate(DrawGeometry.Bounds(wd), pad, pad).IntersectsWith(RectangleF.Inflate(rb, 0.002F, 0.002F))
                                   End Function).ToList()
        Dim at = If(touching.Count > 0, touching.Min(), LayerCount())
        Dim below = If(at < LayerCount(), _recipe.Drawings(at), Nothing)
        ApplyChange(Sub(r)
                        r.Drawings.Insert(at, layer)
                        If below Is Nothing Then Return
                        ' 堆疊順序：放在 below 的正下方
                        Dim order = LayerStack.Order(r)
                        Dim me1 = order.FindIndex(Function(x) x.Item Is layer)
                        Dim it = order(me1)
                        order.RemoveAt(me1)
                        order.Insert(Math.Max(0, order.FindIndex(Function(x) x.Item Is below)), it)
                        LayerStack.SetOrder(r, order)
                    End Sub)
        SelectDrawLayer(_recipe.Drawings.IndexOf(layer))
        SetStatusMessage(Lang.T("已填色：變成一個鋼筆路徑（放在線條下面），可以再改顏色、漸層或節點。"))
    End Sub

    '---------------------------------------------------------------------
    ' 漸層填色
    '---------------------------------------------------------------------

    Private Sub BuildFillStyleRow(L As PageLayout)
        Dim third = (L.Width - 8 - 12) \ 3
        _help.SetHelpLinked("draw.fillmode", _fillMode, AddCaption(L, Lang.T("填色方式"), L.Y), _fillMode)
        _fillMode.Items.AddRange(FillModeNames.Cast(Of Object)().ToArray())
        _fillMode.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8 - third - 6, 24)
        AddHandler _fillMode.SelectedIndexChanged, Sub()
                                                       If _syncing OrElse _fillMode.SelectedIndex < 0 Then Return
                                                       Dim mode = _fillMode.SelectedIndex
                                                       SetDrawProp(Sub(d)
                                                                       If mode = 0 Then
                                                                           d.FillGradient = Nothing : d.FillPattern = Nothing
                                                                       ElseIf mode = 3 Then
                                                                           ' 圖樣：填色是底、第二色是花樣（見 frmEditor.DrawBatch6.vb）
                                                                           d.FillGradient = Nothing
                                                                           If d.FillPattern Is Nothing Then d.FillPattern = NewPattern()
                                                                           d.Filled = True
                                                                       Else
                                                                           d.FillPattern = Nothing
                                                                           Dim radial = mode = 2
                                                                           d.FillGradient = New GradientFill With {.Kind = If(radial, GradientKind.Radial, GradientKind.Linear),
                                                                               .X1 = If(radial, 0.5, 0), .Y1 = 0.5, .X2 = 1, .Y2 = 0.5}
                                                                           d.Filled = True
                                                                       End If
                                                                   End Sub)
                                                   End Sub
        L.Add(_fillMode)
        _drawFill2Color.Text = Lang.T("第二色")
        _drawFill2Color.FlatStyle = FlatStyle.Flat
        _drawFill2Color.FlatAppearance.BorderColor = Color.FromArgb(150, 160, 175)
        _drawFill2Color.Cursor = Cursors.Hand
        _drawFill2Color.SetBounds(L.Width - third, L.Y + 1, third, 28)
        _help.SetHelp("draw.fillcolor2", _drawFill2Color)
        AddHandler _drawFill2Color.Click, Sub() ShowColorWindow(TargetFill2)
        L.Add(_drawFill2Color)
        L.Y += RowHeight + 4
    End Sub

    Private Sub SyncFillStyle(st As DrawLayer)
        Dim mode = If(st.FillPattern IsNot Nothing, 3, If(st.FillGradient Is Nothing, 0, If(st.FillGradient.Kind = GradientKind.Radial, 2, 1)))
        _fillMode.SelectedIndex = mode
        SetSwatch(_drawFill2Color, st.FillColor2Argb)
        _drawFill2Color.Enabled = mode <> 0
    End Sub

    ''' <summary>選取工具、只選一個有漸層的圖形：起點（圓）、終點（方塊）的螢幕位置。</summary>
    Private Function GradHandles() As (A As PointF, B As PointF, Layer As DrawLayer)?
        If _drawStrip.SelectedTool <> DrawIcons.SelectTool OrElse IsMultiSelected() Then Return Nothing
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing OrElse sel.FillGradient Is Nothing OrElse Not sel.Filled OrElse sel.Locked OrElse Not sel.Visible Then Return Nothing
        Dim g = DrawingRenderer.AbsoluteGradient(sel)
        Return (UnitToScreen(New PointF(CSng(g.X1), CSng(g.Y1))), UnitToScreen(New PointF(CSng(g.X2), CSng(g.Y2))), sel)
    End Function

    Private Function BeginGradHandle(e As MouseEventArgs, u As PointF) As Boolean
        Dim h = GradHandles()
        If Not h.HasValue Then Return False
        If ScreenDist(h.Value.B, e.Location) <= HandleRadius + 1 Then
            _gradDrag = 2
        ElseIf ScreenDist(h.Value.A, e.Location) <= HandleRadius + 1 Then
            _gradDrag = 1
        Else
            Return False
        End If
        _ddSerial += 1
        Return True
    End Function

    Private Sub GradHandleMove(u As PointF)
        Dim index = _drawIndex
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing OrElse sel.FillGradient Is Nothing Then Return
        Dim b = DrawGeometry.Bounds(sel)
        If b.Width < 0.0001 OrElse b.Height < 0.0001 Then Return
        Dim fr = DrawGeometry.GradientFraction(sel, u) ' 方框類用自己的方框（旋轉時漸層跟著轉）
        Dim fx = fr.X, fy = fr.Y
        Dim which = _gradDrag
        ApplyChange(Sub(r)
                        Dim g = r.Drawings(index).FillGradient
                        If which = 1 Then g.X1 = Math.Round(fx, 4) : g.Y1 = Math.Round(fy, 4) Else g.X2 = Math.Round(fx, 4) : g.Y2 = Math.Round(fy, 4)
                    End Sub, "grad-handle-" & _ddSerial)
        _canvas.Invalidate()
    End Sub

    Private Sub PaintGradHandles(g As Graphics)
        Dim h = GradHandles()
        If Not h.HasValue Then Return
        Dim a = h.Value.A, b = h.Value.B
        Using outer As New Pen(Color.FromArgb(160, 0, 0, 0), 3), line As New Pen(Color.White, 1.4F) With {.DashStyle = DashStyle.Dash}
            g.DrawLine(outer, a, b) : g.DrawLine(line, a, b)
        End Using
        Dim c1 = Color.FromArgb(255, Color.FromArgb(h.Value.Layer.FillColorArgb)), c2 = Color.FromArgb(255, Color.FromArgb(h.Value.Layer.FillColor2Argb))
        Using b1 As New SolidBrush(c1), b2 As New SolidBrush(c2), pen As New Pen(Color.FromArgb(40, 44, 52), 1.5F)
            g.FillEllipse(b1, a.X - 6, a.Y - 6, 12, 12) : g.DrawEllipse(pen, a.X - 6, a.Y - 6, 12, 12)
            g.FillRectangle(b2, b.X - 5.5F, b.Y - 5.5F, 11, 11) : g.DrawRectangle(pen, b.X - 5.5F, b.Y - 5.5F, 11, 11)
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 匯出 SVG
    '---------------------------------------------------------------------

    Private Sub ExportSvgWithDialog()
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        CommitCalloutEditor()
        If _recipe.Drawings Is Nothing OrElse _recipe.Drawings.Count = 0 Then
            MessageBox.Show(Me, Lang.T("還沒有繪圖圖層：SVG 匯出的是繪圖分頁畫的向量圖形。"), AppName, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim withPhoto As New TaskDialogButton(Lang.T("含照片")) With {.Tag = True}
        Dim vectorOnly As New TaskDialogButton(Lang.T("只有向量圖形")) With {.Tag = False}
        Dim page As New TaskDialogPage With {
            .Caption = AppName, .Heading = Lang.T("匯出 SVG"),
            .Text = Lang.T("繪圖分頁的圖形會變成可以無限放大的向量（鋼筆路徑保留曲線，文字轉成外框）；點陣圖層嵌成圖片。") & vbCrLf &
                    Lang.T("水彩、炭筆等筆刷的質感、陰影與混合模式沒辦法變成 SVG，會變成一般的實心線條。") & vbCrLf & vbCrLf &
                    Lang.T("要把照片（含文字效果、貼圖）也放進去嗎？"),
            .Icon = TaskDialogIcon.Information, .DefaultButton = vectorOnly}
        page.Buttons.Add(withPhoto)
        page.Buttons.Add(vectorOnly)
        page.Buttons.Add(TaskDialogButton.Cancel)
        Dim result = TaskDialog.ShowDialog(Me, page)
        If result Is TaskDialogButton.Cancel OrElse result Is Nothing OrElse result.Tag Is Nothing Then Return
        Using dlg As New SaveFileDialog()
            dlg.Title = Lang.T("匯出 SVG")
            dlg.Filter = Lang.T("SVG 向量圖 (*.svg)|*.svg")
            SetDialogDefaults(dlg, ".svg")
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ExportSvg(dlg.FileName, CBool(result.Tag))
        End Using
    End Sub

    ''' <summary>寫出 SVG（includePhoto：照片放在最下面）。</summary>
    Friend Function ExportSvg(file As String, includePhoto As Boolean) As Boolean
        Cursor = Cursors.WaitCursor
        Try
            ' 照片＝不含繪圖圖層（照片放進形狀在 SVG 裡用 clipPath 裁）
            Dim bgRecipe = _recipe.Clone()
            bgRecipe.Drawings = Nothing
            bgRecipe.DrawGroups = Nothing
            bgRecipe.PhotoClip = Nothing
            Using bg = RenderMerged(bgRecipe)
                Dim svg = SvgExport.Export(_recipe, If(includePhoto, bg, Nothing), bg.Width, bg.Height)
                IO.File.WriteAllText(file, svg, New Text.UTF8Encoding(False))
            End Using
            SetStatusMessage(Lang.T("已匯出 SVG：") & Path.GetFileName(file))
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            MessageBox.Show(Me, Lang.T("無法匯出：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        Finally
            Cursor = Cursors.Default
        End Try
    End Function

    '---------------------------------------------------------------------
    ' 路徑文字
    '---------------------------------------------------------------------

    Private Function BuildPathTextBar() As Control
        _btnAttachText = MakeButton(Lang.T("文字沿路徑"), "draw.pathtext.attach")
        _btnFlipText = MakeButton(Lang.T("換到另一邊"), "draw.pathtext.flip")
        _btnDetachText = MakeButton(Lang.T("離開路徑"), "draw.pathtext.detach")
        For Each b In {_btnAttachText, _btnFlipText, _btnDetachText}
            b.Height = 28
            b.AutoSize = True
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink
            b.Margin = New Padding(0, 0, 4, 0)
        Next
        AddHandler _btnAttachText.Click, Sub() AttachTextToPath()
        AddHandler _btnFlipText.Click, Sub() SetPathText(Sub(d) d.TextPathFlip = Not d.TextPathFlip)
        AddHandler _btnDetachText.Click, Sub() DetachTextFromPath()
        Dim lbl As New Label With {.Text = Lang.T("位置"), .AutoSize = True, .Margin = New Padding(2, 7, 1, 0), .BackColor = Color.Transparent}
        _help.SetHelp("draw.pathtext.shift", _textShift)
        AddHandler _textShift.KeyDown, Sub(s, e)
                                           Dim v As Integer
                                           If e.KeyCode = Keys.Enter AndAlso Integer.TryParse(_textShift.Text.TrimEnd("%"c), v) Then
                                               Dim vv = Math.Max(-50, Math.Min(50, v))
                                               SetPathText(Sub(d) d.TextPathShift = vv)
                                               e.SuppressKeyPress = True
                                           ElseIf e.KeyCode = Keys.Up OrElse e.KeyCode = Keys.Down Then
                                               Dim sel = SelDraw(_recipe)
                                               If sel Is Nothing Then Return
                                               Dim vv = Math.Max(-50, Math.Min(50, sel.TextPathShift + If(e.KeyCode = Keys.Up, 1, -1) * If(e.Shift, 5, 1)))
                                               SetPathText(Sub(d) d.TextPathShift = vv)
                                               e.SuppressKeyPress = True
                                           End If
                                       End Sub
        _pathTextBar.Controls.AddRange({_btnAttachText, lbl, _textShift, _btnFlipText, _btnDetachText})
        _pathTextBar.Visible = False
        Return _pathTextBar
    End Function

    ''' <summary>可以「文字沿路徑」：選了一個文字（還沒在路徑上）和一個可以轉成曲線的圖形。回傳（文字, 路徑圖形）的索引。</summary>
    Private Function AttachCandidate() As (Text As Integer, PathLayer As Integer)?
        Dim idx = SelectedIndices()
        If idx.Count <> 2 Then Return Nothing
        Dim t = idx.FirstOrDefault(Function(i) _recipe.Drawings(i).Shape = DrawShape.Text AndAlso Not DrawGeometry.IsPathText(_recipe.Drawings(i)), -1)
        If t < 0 Then Return Nothing
        Dim p = idx.First(Function(i) i <> t)
        Dim pl = _recipe.Drawings(p)
        If pl.Shape = DrawShape.Text OrElse DrawGeometry.ToPath(pl) Is Nothing Then Return Nothing
        Return (t, p)
    End Function

    Private Sub UpdatePathTextBar()
        If _btnAttachText Is Nothing Then Return
        Dim picking = _drawStrip.SelectedTool = DrawIcons.SelectTool AndAlso Not _builderOn
        Dim attach = picking AndAlso AttachCandidate().HasValue
        Dim sel = If(picking AndAlso Not IsMultiSelected(), SelDraw(_recipe), Nothing)
        Dim onPath = DrawGeometry.IsPathText(sel)
        _btnAttachText.Visible = attach
        For Each c As Control In _pathTextBar.Controls
            If c IsNot _btnAttachText Then c.Visible = onPath
        Next
        _pathTextBar.Visible = attach OrElse onPath
        If onPath AndAlso Not _textShift.Focused Then _textShift.Text = sel.TextPathShift & "%"
    End Sub

    Private Sub AttachTextToPath()
        Dim c = AttachCandidate()
        If Not c.HasValue Then Return
        Dim ti = c.Value.Text, pi = c.Value.PathLayer
        Dim path = DrawGeometry.ToPath(_recipe.Drawings(pi))
        Dim b = DrawGeometry.Bounds(path)
        Dim textLayer = _recipe.Drawings(ti), srcLayer = _recipe.Drawings(pi)
        Dim entered = EnteredGroup()
        ApplyChange(Sub(r)
                        Dim t = r.Drawings(ti)
                        If r.Drawings(pi).Id Is Nothing Then r.Drawings(pi).Id = Guid.NewGuid().ToString("N")
                        t.TextPathSource = r.Drawings(pi).Id ' 跟著這個圖形
                        ' 文字和圖形組成群組：一起移動、縮放
                        If DrawGroups.ChildGroupAt(r, t, entered) Is Nothing OrElse DrawGroups.ChildGroupAt(r, t, entered) <> DrawGroups.ChildGroupAt(r, r.Drawings(pi), entered) Then
                            DrawGroups.Create(r, {t, r.Drawings(pi)}, entered, Lang.T("路徑文字"))
                        End If
                        t.Contours = path.Contours.Select(Function(x) x.Clone()).ToList()
                        t.TextPathShift = 0
                        t.TextPathFlip = False
                        t.Rotation = 0
                        t.X = b.X + b.Width / 2 : t.Y = b.Y + b.Height / 2 ' 輸入框的位置
                        t.W = Math.Max(0.05, b.Width) : t.H = Math.Max(0.05, b.Height)
                    End Sub)
        SelectDrawLayer(_recipe.Drawings.IndexOf(textLayer))
        SetStatusMessage(Lang.T("文字已沿著路徑排，和原本的圖形組成群組（一起移動、縮放）：用節點工具改那個圖形，文字跟著走；上方可以調位置、換到另一邊。"))
    End Sub

    Private Sub DetachTextFromPath()
        Dim index = _drawIndex
        Dim sel = SelDraw(_recipe)
        If Not DrawGeometry.IsPathText(sel) Then Return
        ApplyChange(Sub(r)
                        r.Drawings(index).Contours = Nothing
                        r.Drawings(index).TextPathSource = Nothing
                        r.Drawings(index).TextPathShift = 0
                        r.Drawings(index).TextPathFlip = False
                    End Sub)
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    Private Sub SetPathText(change As Action(Of DrawLayer))
        Dim index = _drawIndex
        If Not DrawGeometry.IsPathText(SelDraw(_recipe)) Then Return
        ApplyChange(Sub(r) change(r.Drawings(index)))
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub
End Class
