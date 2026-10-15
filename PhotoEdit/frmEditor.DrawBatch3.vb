Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 互動式陰影、互動式透明度、封套三個工具（左側向量區同一格展開）、調和（上方按鈕）、影像描圖（影像選單）。
''' 運算見 BrushEngine 的 AddShadow／ApplyFade、Envelope、Blend、ImageTrace。
''' </summary>
Partial Friend Class frmEditor

    Private Const TargetShadow As Integer = 4
    ' ---- 拖曳中 ----
    Private _fxDrag As Integer ' 1 陰影、2 透明度、3 封套控制點
    Private _fxIndex As Integer = -1
    Private _fxStart As PointF
    Private _fxStartLayer As DrawLayer
    ' ---- 封套 ----
    Private _envOrig As DrawLayer
    Private _envLayer As DrawLayer
    Private _envGrid As PointF()
    Private _envHandle As Integer = -1
    ' ---- 操作列 ----
    Private ReadOnly _fxBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                         .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0)}
    Private ReadOnly _fxControls As New Dictionary(Of String, Control)()
    Private ReadOnly _blendSteps As New TextBox With {.Width = 30, .Text = "5", .Margin = New Padding(0, 3, 4, 0), .TextAlign = HorizontalAlignment.Right}

    '---------------------------------------------------------------------
    ' 操作列
    '---------------------------------------------------------------------

    Private Function BuildFxBar() As Control
        Dim addLabel = Sub(key As String, text As String)
                           Dim l As New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(4, 7, 1, 0), .BackColor = Color.Transparent}
                           _fxControls(key) = l
                           _fxBar.Controls.Add(l)
                       End Sub
        Dim addBox = Sub(key As String, help As String)
                         Dim tb As New TextBox With {.Width = 36, .Margin = New Padding(0, 3, 4, 0), .TextAlign = HorizontalAlignment.Right}
                         _help.SetHelp(help, tb)
                         AddHandler tb.KeyDown, Sub(s, e)
                                                    If e.KeyCode = Keys.Enter Then FxBoxCommit(key, tb) : e.SuppressKeyPress = True
                                                End Sub
                         AddHandler tb.Leave, Sub() FxBoxCommit(key, tb)
                         _fxControls(key) = tb
                         _fxBar.Controls.Add(tb)
                     End Sub
        Dim addButton = Sub(key As String, text As String, help As String, click As Action)
                            Dim b = MakeButton(text, help)
                            b.Height = 28 : b.AutoSize = True : b.AutoSizeMode = AutoSizeMode.GrowAndShrink : b.Margin = New Padding(0, 0, 4, 0)
                            AddHandler b.Click, Sub() click()
                            _fxControls(key) = b
                            _fxBar.Controls.Add(b)
                        End Sub
        ' 陰影
        addLabel("shadowSoftL", Lang.T("柔邊")) : addBox("shadowSoft", "draw.shadow.soft")
        addLabel("shadowOpL", Lang.T("不透明")) : addBox("shadowOp", "draw.shadow.opacity")
        addButton("shadowColor", Lang.T("陰影色"), "draw.shadow.color", Sub() ShowColorWindow(TargetShadow))
        addButton("shadowOff", Lang.T("移除陰影"), "draw.shadow.remove", Sub() SetSelectedFx(Sub(d)
                                                                                        d.Shadow = False : d.ShadowOffsetX = Nothing : d.ShadowOffsetY = Nothing
                                                                                    End Sub))
        ' 透明度
        Dim kind As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 70, .Margin = New Padding(4, 3, 4, 0)}
        kind.Items.AddRange({Lang.T("線性"), Lang.T("放射")})
        kind.SelectedIndex = 0
        _help.SetHelp("draw.fade.kind", kind)
        AddHandler kind.SelectedIndexChanged, Sub()
                                                  If _syncing Then Return
                                                  Dim radial = kind.SelectedIndex = 1
                                                  SetSelectedFx(Sub(d) If d.FadeGradient IsNot Nothing Then d.FadeGradient.Kind = If(radial, GradientKind.Radial, GradientKind.Linear))
                                              End Sub
        _fxControls("fadeKind") = kind
        _fxBar.Controls.Add(kind)
        addButton("fadeOff", Lang.T("移除透明"), "draw.fade.remove", Sub() SetSelectedFx(Sub(d) d.FadeGradient = Nothing))
        ' 封套
        addButton("envReset", Lang.T("重設封套"), "draw.envelope.reset", Sub() ResetEnvelope())
        ' 調和（選取工具、選了兩個圖形）
        addButton("blend", Lang.T("調和"), "draw.blend", Sub() BlendSelected())
        addLabel("blendL", Lang.T("步數"))
        _help.SetHelp("draw.blend.steps", _blendSteps)
        _fxControls("blendSteps") = _blendSteps
        _fxBar.Controls.Add(_blendSteps)
        For Each c In _fxControls.Values
            c.Visible = False
        Next
        Return _fxBar
    End Function

    Private Sub UpdateFxBar()
        If _fxControls.Count = 0 Then Return
        Dim tool = _drawStrip.SelectedTool
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        Dim show = Sub(keys As String(), visible As Boolean)
                       For Each k In keys
                           _fxControls(k).Visible = visible
                       Next
                   End Sub
        show({"shadowSoftL", "shadowSoft", "shadowOpL", "shadowOp", "shadowColor", "shadowOff"}, tool = DrawIcons.ShadowTool AndAlso sel IsNot Nothing AndAlso sel.Shadow)
        show({"fadeKind", "fadeOff"}, tool = DrawIcons.TransparencyTool AndAlso sel IsNot Nothing AndAlso sel.FadeGradient IsNot Nothing)
        show({"envReset"}, tool = DrawIcons.EnvelopeTool AndAlso _envOrig IsNot Nothing)
        Dim canBlend = tool = DrawIcons.SelectTool AndAlso Not _builderOn AndAlso BlendCandidate().HasValue
        show({"blend", "blendL", "blendSteps"}, canBlend)
        If sel IsNot Nothing Then
            Dim s = CType(_fxControls("shadowSoft"), TextBox), o = CType(_fxControls("shadowOp"), TextBox)
            If Not s.Focused Then s.Text = If(sel.ShadowSoft > 0, sel.ShadowSoft.ToString(), Lang.T("自動"))
            If Not o.Focused Then o.Text = sel.ShadowOpacity & "%"
            If sel.FadeGradient IsNot Nothing Then CType(_fxControls("fadeKind"), ComboBox).SelectedIndex = If(sel.FadeGradient.Kind = GradientKind.Radial, 1, 0)
        End If
    End Sub

    Private Sub FxBoxCommit(key As String, tb As TextBox)
        Dim v As Integer
        If Not Integer.TryParse(tb.Text.Trim().TrimEnd("%"c), v) Then
            If key = "shadowSoft" AndAlso tb.Text.Trim() = Lang.T("自動") Then Return
            UpdateFxBar() : Return
        End If
        Select Case key
            Case "shadowSoft" : Dim vv = Math.Max(0, Math.Min(200, v)) : SetSelectedFx(Sub(d) d.ShadowSoft = vv)
            Case "shadowOp" : Dim vv = Math.Max(0, Math.Min(100, v)) : SetSelectedFx(Sub(d) d.ShadowOpacity = vv)
        End Select
    End Sub

    Private Sub SetSelectedFx(change As Action(Of DrawLayer))
        Dim index = _drawIndex
        If index < 0 OrElse index >= LayerCount() OrElse IsMultiSelected() Then Return
        ApplyChange(Sub(r) change(r.Drawings(index)))
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 滑鼠（frmEditor.DrawTools.vb 的 ToolMouseDown／Move／Up 呼叫）
    '---------------------------------------------------------------------

    ''' <summary>點到的圖層（目前選取的優先）。</summary>
    Private Function FxTarget(u As PointF) As Integer
        Dim sel = SelDraw(_recipe)
        If sel IsNot Nothing AndAlso Not IsMultiSelected() AndAlso LayerHit(sel, u, HitTolerance()) Then Return _drawIndex
        Return HitLayer(u)
    End Function

    Private Function FxMouseDown(e As MouseEventArgs, u As PointF) As Boolean
        Dim tool = _drawStrip.SelectedTool
        If tool <> DrawIcons.ShadowTool AndAlso tool <> DrawIcons.TransparencyTool AndAlso tool <> DrawIcons.EnvelopeTool Then Return False
        If e.Button <> MouseButtons.Left Then Return True
        If tool = DrawIcons.EnvelopeTool Then
            ' 封套：先看控制點
            If _envGrid IsNot Nothing AndAlso EnvelopeAlive() Then
                For i = 0 To 7
                    If ScreenDist(UnitToScreen(_envGrid(i)), e.Location) <= HandleRadius + 1 Then
                        _envHandle = i : _fxDrag = 3 : _ddSerial += 1
                        Return True
                    End If
                Next
            End If
            Dim hitE = FxTarget(u)
            If hitE < 0 Then EndEnvelope() : SelectDrawLayer(-1) : Return True
            Dim d = _recipe.Drawings(hitE)
            If Not Envelope.CanApply(d) OrElse d.Locked Then
                SetStatusMessage(Lang.T("封套只能套在向量圖形上（文字先轉成曲線；點陣圖層不行）。"))
                Return True
            End If
            SelectDrawLayer(hitE)
            _envOrig = d.Clone()
            _envLayer = d
            _envGrid = Envelope.DefaultGrid(DrawGeometry.Bounds(d))
            _ddSerial += 1
            UpdateDrawControls()
            _canvas.Invalidate()
            Return True
        End If
        Dim hit = FxTarget(u)
        If hit < 0 OrElse _recipe.Drawings(hit).Locked Then
            SetStatusMessage(Lang.T("先按在圖形上再拖曳。"))
            Return True
        End If
        If hit <> _drawIndex OrElse IsMultiSelected() Then SelectDrawLayer(hit)
        _fxIndex = hit
        _fxStart = u
        _fxStartLayer = _recipe.Drawings(hit).Clone()
        _fxDrag = If(tool = DrawIcons.ShadowTool, 1, 2)
        _ddSerial += 1
        Return True
    End Function

    Private Function FxMouseMove(e As MouseEventArgs, u As PointF) As Boolean
        Dim tool = _drawStrip.SelectedTool
        If _fxDrag = 0 Then
            If tool = DrawIcons.ShadowTool OrElse tool = DrawIcons.TransparencyTool OrElse tool = DrawIcons.EnvelopeTool Then
                Dim c = If(FxTarget(u) >= 0, Cursors.Cross, Cursors.Default)
                If tool = DrawIcons.EnvelopeTool AndAlso _envGrid IsNot Nothing AndAlso _envGrid.Any(Function(g) ScreenDist(UnitToScreen(g), e.Location) <= HandleRadius + 1) Then c = Cursors.Hand
                If _canvas.Cursor IsNot c Then _canvas.Cursor = c
                Return True
            End If
            Return False
        End If
        Select Case _fxDrag
            Case 1
                ' 陰影：拖曳的方向與距離就是陰影的位移
                Dim index = _fxIndex
                Dim dx = Math.Round(u.X - _fxStart.X, 5), dy = Math.Round(u.Y - _fxStart.Y, 5)
                ApplyChange(Sub(r)
                                Dim d = r.Drawings(index)
                                d.Shadow = True : d.ShadowOffsetX = dx : d.ShadowOffsetY = dy
                            End Sub, "fx-shadow-" & _ddSerial)
            Case 2
                ' 透明度：按下的地方不透明、拖到的地方透明
                Dim index = _fxIndex
                Dim s = _fxStartLayer
                Dim a = DrawGeometry.GradientFraction(s, _fxStart), b = DrawGeometry.GradientFraction(s, u)
                Dim radial = s.FadeGradient IsNot Nothing AndAlso s.FadeGradient.Kind = GradientKind.Radial
                ApplyChange(Sub(r) r.Drawings(index).FadeGradient = New GradientFill With {.Kind = If(radial, GradientKind.Radial, GradientKind.Linear),
                                .X1 = Math.Round(a.X, 4), .Y1 = Math.Round(a.Y, 4), .X2 = Math.Round(b.X, 4), .Y2 = Math.Round(b.Y, 4), .Dither = False}, "fx-fade-" & _ddSerial)
            Case 3
                _envGrid(_envHandle) = u
                ApplyEnvelope()
        End Select
        _canvas.Invalidate()
        Return True
    End Function

    Private Function FxMouseUp() As Boolean
        If _fxDrag = 0 Then Return False
        _fxDrag = 0
        _fxStartLayer = Nothing
        UpdateDrawControls()
        _canvas.Invalidate()
        Return True
    End Function

    '---------------------------------------------------------------------
    ' 封套
    '---------------------------------------------------------------------

    ''' <summary>封套的圖層還是剛剛那個（沒被復原、刪除、換掉）。</summary>
    Private Function EnvelopeAlive() As Boolean
        Return _envLayer IsNot Nothing AndAlso _recipe.Drawings IsNot Nothing AndAlso _recipe.Drawings.Contains(_envLayer)
    End Function

    Private Sub ApplyEnvelope()
        If Not EnvelopeAlive() Then EndEnvelope() : Return
        Dim index = _recipe.Drawings.IndexOf(_envLayer)
        Dim warped = Envelope.Apply(_envOrig, _envGrid)
        If warped Is Nothing Then Return
        ApplyChange(Sub(r) r.Drawings(index) = warped, "envelope-" & _ddSerial)
        _envLayer = warped
    End Sub

    Private Sub ResetEnvelope()
        If _envOrig Is Nothing OrElse Not EnvelopeAlive() Then Return
        Dim index = _recipe.Drawings.IndexOf(_envLayer)
        Dim orig = _envOrig.Clone()
        ApplyChange(Sub(r) r.Drawings(index) = orig)
        _envLayer = orig
        _envGrid = Envelope.DefaultGrid(DrawGeometry.Bounds(orig))
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    Private Sub EndEnvelope()
        _envOrig = Nothing : _envLayer = Nothing : _envGrid = Nothing : _envHandle = -1
    End Sub

    '---------------------------------------------------------------------
    ' 調和
    '---------------------------------------------------------------------

    ''' <summary>選了兩個可以調和的圖形（不在不同群組也可以）：回傳（下面的, 上面的）索引。</summary>
    Private Function BlendCandidate() As (A As Integer, B As Integer)?
        Dim idx = SelectedIndices()
        If idx.Count <> 2 Then Return Nothing
        If Not idx.All(Function(i) ShapeBlend.CanBlend(_recipe.Drawings(i)) AndAlso Not _recipe.Drawings(i).Locked) Then Return Nothing
        Return (idx.Min(), idx.Max())
    End Function

    Private Sub BlendSelected()
        Dim c = BlendCandidate()
        If Not c.HasValue Then Return
        Dim steps As Integer
        If Not Integer.TryParse(_blendSteps.Text, steps) Then steps = 5
        steps = Math.Max(1, Math.Min(50, steps))
        _blendSteps.Text = steps.ToString()
        Dim a = _recipe.Drawings(c.Value.A), b = _recipe.Drawings(c.Value.B)
        Dim mids = ShapeBlend.Make(a, b, steps)
        If mids.Count = 0 Then Return
        Dim entered = EnteredGroup()
        ApplyChange(Sub(r)
                        Dim at = r.Drawings.IndexOf(a) + 1
                        For Each m In mids
                            m.GroupId = a.GroupId
                        Next
                        r.Drawings.InsertRange(at, mids)
                        ' 堆疊順序：一個接一個疊在 a 上面
                        Dim prev = a
                        For Each m In mids
                            LayerStack.PlaceAbove(r, m, prev)
                            prev = m
                        Next
                        DrawGroups.Create(r, {a}.Concat(mids).Concat({b}), entered, Lang.T("調和"))
                    End Sub)
        Dim idx = {a}.Concat(mids).Concat({b}).Select(Function(d) _recipe.Drawings.IndexOf(d)).Where(Function(i) i >= 0).ToList()
        SetMultiSelection(idx, idx.Max())
        SetStatusMessage(Lang.F("已調和：中間加了 {0} 個圖形（和兩端組成群組）。改兩端不會自動更新，要重做時解散群組、刪掉中間的再調和一次。", mids.Count))
        UpdateDrawControls()
    End Sub

    '---------------------------------------------------------------------
    ' 影像描圖
    '---------------------------------------------------------------------

    Private Sub ShowTrace()
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        CommitCalloutEditor()
        Dim bgRecipe = _recipe.Clone()
        bgRecipe.Drawings = Nothing
        bgRecipe.DrawGroups = Nothing
        bgRecipe.PhotoClip = Nothing
        Using src = RenderMerged(bgRecipe), dlg As New frmTrace(src)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim opt = dlg.Options
            opt.Resolution = 1000
            Cursor = Cursors.WaitCursor
            Dim layers As List(Of DrawLayer)
            Try
                layers = ImageTrace.Trace(src, opt)
            Finally
                Cursor = Cursors.Default
            End Try
            AddTracedLayers(layers)
        End Using
    End Sub

    ''' <summary>描好的圖形加成繪圖圖層（兩個以上組成「描圖」群組），切到繪圖分頁並選取。</summary>
    Friend Sub AddTracedLayers(layers As List(Of DrawLayer))
            If layers.Count = 0 Then
                SetStatusMessage(Lang.T("沒有描出任何圖形。"))
                Return
            End If
            ApplyChange(Sub(r)
                            If r.Drawings Is Nothing Then r.Drawings = New List(Of DrawLayer)()
                            r.Drawings.AddRange(layers)
                            If layers.Count >= 2 Then DrawGroups.Create(r, layers, Nothing, Lang.T("描圖")) Else layers(0).Name = Lang.T("描圖")
                        End Sub)
            If _tabs.SelectedIndex <> TabDraw Then _tabs.SelectedIndex = TabDraw
            Dim idx = layers.Select(Function(d) _recipe.Drawings.IndexOf(d)).Where(Function(i) i >= 0).ToList()
            If idx.Count >= 2 Then SetMultiSelection(idx, idx.Max()) Else SelectDrawLayer(idx(0))
            SetStatusMessage(Lang.F("描圖完成：{0} 個向量圖形（組成「描圖」群組）。可以隱藏照片看效果，或匯出 SVG。", layers.Count))
            UpdateDrawControls()
    End Sub

    '---------------------------------------------------------------------
    ' 畫面
    '---------------------------------------------------------------------

    Private Sub PaintFx(g As Graphics)
        Dim tool = _drawStrip.SelectedTool
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        If tool = DrawIcons.ShadowTool AndAlso sel IsNot Nothing AndAlso sel.Shadow Then
            Dim b = DrawGeometry.Bounds(sel)
            Dim c = New PointF(b.X + b.Width / 2, b.Y + b.Height / 2)
            Dim sh = DrawingRenderer.ShadowGeometry(sel, CSng(Math.Max(0.6, sel.StrokeWidth * PhotoPixelSize().Height)), PhotoPixelSize().Height)
            Dim px = PhotoPixelSize().Height
            Dim a = UnitToScreen(c), e = UnitToScreen(New PointF(CSng(c.X + sh.DX / px), CSng(c.Y + sh.DY / px)))
            Using pen As New Pen(Color.FromArgb(220, 40, 44, 52), 1.4F) With {.DashStyle = DashStyle.Dash}
                g.DrawLine(pen, a, e)
            End Using
            g.FillRectangle(Brushes.White, e.X - 5, e.Y - 5, 10, 10)
            g.DrawRectangle(Pens.Black, e.X - 5, e.Y - 5, 10, 10)
        End If
        If tool = DrawIcons.TransparencyTool AndAlso sel IsNot Nothing AndAlso sel.FadeGradient IsNot Nothing Then
            Dim fg = DrawingRenderer.AbsoluteGradient(sel, sel.FadeGradient)
            Dim a = UnitToScreen(New PointF(CSng(fg.X1), CSng(fg.Y1))), b = UnitToScreen(New PointF(CSng(fg.X2), CSng(fg.Y2)))
            Using outer As New Pen(Color.FromArgb(160, 0, 0, 0), 3), line As New Pen(Color.White, 1.4F) With {.DashStyle = DashStyle.Dash}
                g.DrawLine(outer, a, b) : g.DrawLine(line, a, b)
            End Using
            g.FillEllipse(Brushes.White, a.X - 6, a.Y - 6, 12, 12) : g.DrawEllipse(Pens.Black, a.X - 6, a.Y - 6, 12, 12)
            g.FillRectangle(Brushes.Black, b.X - 5, b.Y - 5, 10, 10) : g.DrawRectangle(Pens.White, b.X - 5, b.Y - 5, 10, 10)
        End If
        If tool = DrawIcons.EnvelopeTool AndAlso _envGrid IsNot Nothing AndAlso EnvelopeAlive() Then
            Dim pts = Envelope.Outline(_envGrid).Select(Function(p) UnitToScreen(p)).ToArray()
            Using outer As New Pen(Color.FromArgb(160, 0, 0, 0), 3), pen As New Pen(Color.FromArgb(212, 83, 126), 1.4F) With {.DashStyle = DashStyle.Dash}
                g.DrawPolygon(outer, pts) : g.DrawPolygon(pen, pts)
            End Using
            For i = 0 To 7
                Dim p = UnitToScreen(_envGrid(i))
                Using br As New SolidBrush(If(i Mod 2 = 0, Color.White, Color.FromArgb(250, 220, 90)))
                    g.FillRectangle(br, p.X - 5, p.Y - 5, 10, 10)
                End Using
                g.DrawRectangle(Pens.Black, p.X - 5, p.Y - 5, 10, 10)
            Next
        End If
    End Sub

    ''' <summary>新工具的提示。</summary>
    Private Function FxHint(tool As Integer) As String
        Select Case tool
            Case DrawIcons.ShadowTool : Return Lang.T("陰影：在圖形上按住往外拖，拖的方向和距離就是陰影的位置；上方可以調柔邊、不透明度、顏色。")
            Case DrawIcons.TransparencyTool : Return Lang.T("透明度：在圖形上從要保持不透明的地方拖到要變透明的地方；上方可以換成放射、移除。")
            Case DrawIcons.EnvelopeTool : Return Lang.T("封套：點一個圖形，拖曳四角（白）和四邊中點（黃）把圖形彎曲變形；結果是鋼筆路徑。")
        End Select
        Return Nothing
    End Function
End Class
