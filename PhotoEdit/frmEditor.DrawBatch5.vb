Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 向量第二批（對照 CorelDRAW 的效果工具）：輪廓圖、變形效果（收縮膨脹、粗糙、鋸齒、扭轉）、立體化、簡化路徑
''' （上方「效果 ▾」選單與編輯選單，設定視窗見 frmVectorEffect），以及向量修飾筆刷（塗抹、轉動、吸引、排斥、粗糙、平滑）。
''' 運算見 PhotoEdit.Core 的 VectorEffects（ShapeEffects.vb）與 TouchUpSession。
''' </summary>
Partial Friend Class frmEditor

    '---------------------------------------------------------------------
    ' 共用
    '---------------------------------------------------------------------

    ''' <summary>選取中可以套效果的物件（沒鎖定的向量圖形、文字）。</summary>
    Private Function EffectTargets() As List(Of Integer)
        Return SelectedIndices().Where(Function(i) Not _recipe.Drawings(i).Locked AndAlso VectorEffects.CanApply(_recipe.Drawings(i))).ToList()
    End Function

    ''' <summary>照片像素換成照片高度單位。</summary>
    Private Function PxToUnits(px As Double) As Double
        Return px / Math.Max(1, PhotoPixelSize().Height)
    End Function

    ''' <summary>效果結果換掉原本的圖層時，保留身分（Id、群組、名稱、顯示、鎖定）。</summary>
    Private Shared Function KeepIdentity(result As DrawLayer, old As DrawLayer) As DrawLayer
        result.Id = old.Id : result.GroupId = old.GroupId : result.Name = old.Name
        result.Visible = old.Visible : result.Locked = old.Locked
        Return result
    End Function

    Private Function NewEffectDialog(title As String) As frmVectorEffect
        Dim dlg As New frmVectorEffect(title, PhotoAspect(), _panelFont)
        ThemeManager.Attach(dlg)
        Return dlg
    End Function

    Private Function NoEffectTargets() As Boolean
        If EffectTargets().Count > 0 Then Return False
        SetStatusMessage(Lang.T("先選取向量圖形或文字（點陣圖層、填滿圖層不能套效果）。"))
        Return True
    End Function

    ''' <summary>新圖層放在 anchor 正下方。</summary>
    Private Shared Sub PlaceBelow(r As EditRecipe, item As DrawLayer, anchor As DrawLayer)
        LayerStack.PlaceAbove(r, item, anchor)
        LayerStack.Swap(r, item, anchor)
    End Sub

    '---------------------------------------------------------------------
    ' 輪廓圖
    '---------------------------------------------------------------------

    Private Sub ShowContour()
        If NoEffectTargets() Then Return
        Dim idx = EffectTargets()
        Dim photoH = PhotoPixelSize().Height
        Using dlg = NewEffectDialog(Lang.T("輪廓圖"))
            dlg.AddChoice("dir", Lang.T("方向"), {Lang.T("外擴"), Lang.T("內縮")}, 0)
            dlg.AddNumber("steps", Lang.T("圈數"), 1, 50, 3)
            dlg.AddNumber("offset", Lang.T("每圈間隔（像素）"), 1, 2000, CDec(Math.Max(2, Math.Round(photoH * 0.02))))
            dlg.AddColor("color", Lang.T("最後一圈的顏色"), Color.White)
            Dim layers = idx.Select(Function(i) _recipe.Drawings(i)).ToList()
            dlg.Compute = Function() ContourPreview(layers, dlg.Choice("dir") = 0, CInt(dlg.Number("steps")), PxToUnits(dlg.Number("offset")), dlg.ColorOf("color"))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ApplyContour(idx, dlg.Choice("dir") = 0, CInt(dlg.Number("steps")), PxToUnits(dlg.Number("offset")), dlg.ColorOf("color"))
        End Using
    End Sub

    Private Shared Function ContourPreview(layers As List(Of DrawLayer), outside As Boolean, steps As Integer, offset As Double, endColor As Color) As List(Of DrawLayer)
        Dim all As New List(Of DrawLayer)()
        For Each d In layers
            Dim c = VectorEffects.Contour(d, outside, steps, offset, endColor)
            If outside Then all.AddRange(c) : all.Add(d) Else all.Add(d) : all.AddRange(c)
        Next
        Return all
    End Function

    ''' <summary>輪廓圖：每個物件和它的輪廓組成「輪廓圖」群組（外擴的在下面、內縮的在上面）。</summary>
    Private Sub ApplyContour(idx As List(Of Integer), outside As Boolean, steps As Integer, offset As Double, endColor As Color)
        Cursor = Cursors.WaitCursor
        Dim made As New List(Of (Source As DrawLayer, Rings As List(Of DrawLayer)))()
        Try
            For Each i In idx
                Dim c = VectorEffects.Contour(_recipe.Drawings(i), outside, steps, offset, endColor)
                If c.Count > 0 Then made.Add((_recipe.Drawings(i), c))
            Next
        Finally
            Cursor = Cursors.Default
        End Try
        If made.Count = 0 Then SetStatusMessage(Lang.T("算不出輪廓（內縮得太多？）。")) : Return
        Dim entered = EnteredGroup()
        ApplyChange(Sub(r)
                        For Each m In made
                            Dim prev As DrawLayer = Nothing
                            For Each c In m.Rings
                                r.Drawings.Add(c)
                                If outside Then
                                    PlaceBelow(r, c, m.Source) ' 依序放在原本的下面（最外圈最下面）
                                Else
                                    LayerStack.PlaceAbove(r, c, If(prev, m.Source))
                                End If
                                prev = c
                            Next
                            DrawGroups.Create(r, m.Rings.Concat({m.Source}), entered, Lang.T("輪廓圖"))
                        Next
                    End Sub)
        Dim sel = made.SelectMany(Function(m) m.Rings.Concat({m.Source})).Select(Function(d) _recipe.Drawings.IndexOf(d)).Where(Function(i) i >= 0).ToList()
        If sel.Count > 0 Then SetMultiSelection(sel, sel.Max())
        SetStatusMessage(Lang.F("已做輪廓圖：{0} 圈，和原本的組成群組。", made.Max(Function(m) m.Rings.Count)))
        UpdateDrawControls()
    End Sub

    '---------------------------------------------------------------------
    ' 變形效果
    '---------------------------------------------------------------------

    Private Sub ShowDistort()
        If NoEffectTargets() Then Return
        Dim idx = EffectTargets()
        Using dlg = NewEffectDialog(Lang.T("變形效果"))
            dlg.AddChoice("kind", Lang.T("效果"), {Lang.T("收縮與膨脹"), Lang.T("粗糙"), Lang.T("鋸齒"), Lang.T("扭轉")}, 0)
            dlg.AddNumber("pb", Lang.T("收縮 − ／膨脹 +（%）"), -100, 100, 40)
            dlg.AddNumber("size", Lang.T("起伏大小（%）"), 0, 50, 3, 1)
            dlg.AddNumber("detail", Lang.T("細節（每 100 像素幾點）"), 1, 100, 10)
            dlg.AddNumber("ridges", Lang.T("每段起伏數"), 1, 50, 4)
            dlg.AddNumber("angle", Lang.T("角度（度）"), -720, 720, 90)
            dlg.AddCheck("smooth", Lang.T("平滑（曲線）"), False)
            dlg.AddNumber("seed", Lang.T("隨機種子"), 1, 9999, 1)
            Dim sync = Sub()
                           Dim k = dlg.Choice("kind")
                           dlg.SetRowVisible("pb", k = 0)
                           dlg.SetRowVisible("size", k = 1 OrElse k = 2)
                           dlg.SetRowVisible("detail", k = 1)
                           dlg.SetRowVisible("seed", k = 1)
                           dlg.SetRowVisible("ridges", k = 2)
                           dlg.SetRowVisible("smooth", k = 1 OrElse k = 2)
                           dlg.SetRowVisible("angle", k = 3)
                       End Sub
            sync()
            AddHandler dlg.ParamsChanged, Sub() sync()
            Dim layers = idx.Select(Function(i) _recipe.Drawings(i)).ToList()
            dlg.Compute = Function() layers.Select(Function(d) VectorEffects.Distort(d, DistortFrom(dlg))).Where(Function(d) d IsNot Nothing).ToList()
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ApplyDistort(idx, DistortFrom(dlg))
        End Using
    End Sub

    Private Function DistortFrom(dlg As frmVectorEffect) As DistortOptions
        Dim o As New DistortOptions With {.Kind = CType(dlg.Choice("kind"), DistortKind), .Smooth = dlg.Checked("smooth"), .Seed = CInt(dlg.Number("seed"))}
        Select Case o.Kind
            Case DistortKind.PuckerBloat : o.Amount = dlg.Number("pb")
            Case DistortKind.Roughen
                o.Amount = dlg.Number("size")
                o.Detail = dlg.Number("detail") * PhotoPixelSize().Height / 100 ' 每 100 像素 → 每單位
            Case DistortKind.ZigZag : o.Amount = dlg.Number("size") : o.Detail = dlg.Number("ridges")
            Case DistortKind.Twist : o.Amount = dlg.Number("angle")
        End Select
        Return o
    End Function

    ''' <summary>變形效果：每個物件換成變形後的鋼筆路徑（文字會先轉成外框）。</summary>
    Private Sub ApplyDistort(idx As List(Of Integer), opt As DistortOptions)
        Dim results = idx.Select(Function(i) (Index:=i, Layer:=VectorEffects.Distort(_recipe.Drawings(i), opt))).Where(Function(x) x.Layer IsNot Nothing).ToList()
        If results.Count = 0 Then Return
        ApplyChange(Sub(r)
                        For Each x In results
                            r.Drawings(x.Index) = KeepIdentity(x.Layer, r.Drawings(x.Index))
                        Next
                    End Sub)
        SetStatusMessage(Lang.F("已套用變形效果（{0} 個物件，變成鋼筆路徑）。", results.Count))
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 立體化
    '---------------------------------------------------------------------

    Private Sub ShowExtrude()
        If NoEffectTargets() Then Return
        Dim idx = EffectTargets()
        Dim photoH = PhotoPixelSize().Height
        Using dlg = NewEffectDialog(Lang.T("立體化"))
            dlg.AddNumber("angle", Lang.T("方向（度，0＝右、90＝下）"), -180, 180, 45)
            dlg.AddNumber("depth", Lang.T("深度（像素）"), 1, 5000, CDec(Math.Max(2, Math.Round(photoH * 0.05))))
            dlg.AddColor("color", Lang.T("側面顏色"), VectorEffects.DefaultSideColor(_recipe.Drawings(idx(0))))
            dlg.AddCheck("shaded", Lang.T("由前往後漸暗"), True)
            Dim layers = idx.Select(Function(i) _recipe.Drawings(i)).ToList()
            dlg.Compute = Function()
                              Dim all As New List(Of DrawLayer)()
                              For Each d In layers
                                  Dim s = VectorEffects.Extrude(d, dlg.Number("angle"), PxToUnits(dlg.Number("depth")), dlg.ColorOf("color"), dlg.Checked("shaded"))
                                  If s IsNot Nothing Then all.Add(s)
                                  all.Add(d)
                              Next
                              Return all
                          End Function
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ApplyExtrude(idx, dlg.Number("angle"), PxToUnits(dlg.Number("depth")), dlg.ColorOf("color"), dlg.Checked("shaded"))
        End Using
    End Sub

    ''' <summary>立體化：側面放在原本的下面，兩個組成「立體化」群組。</summary>
    Private Sub ApplyExtrude(idx As List(Of Integer), angle As Double, depth As Double, sideColor As Color, shaded As Boolean)
        Dim made = idx.Select(Function(i) (Source:=_recipe.Drawings(i), Side:=VectorEffects.Extrude(_recipe.Drawings(i), angle, depth, sideColor, shaded))).
            Where(Function(x) x.Side IsNot Nothing).ToList()
        If made.Count = 0 Then Return
        Dim entered = EnteredGroup()
        ApplyChange(Sub(r)
                        For Each m In made
                            r.Drawings.Add(m.Side)
                            PlaceBelow(r, m.Side, m.Source)
                            DrawGroups.Create(r, {m.Side, m.Source}, entered, Lang.T("立體化"))
                        Next
                    End Sub)
        Dim sel = made.SelectMany(Function(m) {m.Side, m.Source}).Select(Function(d) _recipe.Drawings.IndexOf(d)).Where(Function(i) i >= 0).ToList()
        If sel.Count > 0 Then SetMultiSelection(sel, sel.Max())
        SetStatusMessage(Lang.T("已立體化：側面在原本的下面，兩個組成群組。"))
        UpdateDrawControls()
    End Sub

    '---------------------------------------------------------------------
    ' 簡化路徑
    '---------------------------------------------------------------------

    Private Sub ShowSimplify()
        If NoEffectTargets() Then Return
        Dim idx = EffectTargets()
        Using dlg = NewEffectDialog(Lang.T("簡化路徑"))
            dlg.AddNumber("tol", Lang.T("容許誤差（像素）"), 0.1D, 50, 1.5D, 1)
            Dim layers = idx.Select(Function(i) _recipe.Drawings(i)).ToList()
            Dim before = layers.Sum(Function(d) VectorEffects.NodeCount(d))
            Dim after = before
            dlg.Compute = Function()
                              Dim res = layers.Select(Function(d) VectorEffects.SimplifyPath(d, PxToUnits(dlg.Number("tol")))).Where(Function(d) d IsNot Nothing).ToList()
                              after = res.Sum(Function(d) VectorEffects.NodeCount(d))
                              Return res
                          End Function
            dlg.Describe = Function() Lang.F("節點：{0} → {1}", before, after)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ApplySimplify(idx, PxToUnits(dlg.Number("tol")))
        End Using
    End Sub

    Private Sub ApplySimplify(idx As List(Of Integer), tolerance As Double)
        Dim before = idx.Sum(Function(i) VectorEffects.NodeCount(_recipe.Drawings(i)))
        Dim results = idx.Select(Function(i) (Index:=i, Layer:=VectorEffects.SimplifyPath(_recipe.Drawings(i), tolerance))).Where(Function(x) x.Layer IsNot Nothing).ToList()
        If results.Count = 0 Then Return
        ApplyChange(Sub(r)
                        For Each x In results
                            r.Drawings(x.Index) = KeepIdentity(x.Layer, r.Drawings(x.Index))
                        Next
                    End Sub)
        SetStatusMessage(Lang.F("已簡化路徑：節點 {0} → {1}。", before, results.Sum(Function(x) VectorEffects.NodeCount(x.Layer))))
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 「效果 ▾」選單
    '---------------------------------------------------------------------

    Private ReadOnly _effectsMenu As New ContextMenuStrip()

    Private Sub BuildEffectsMenu()
        If _effectsMenu.Items.Count > 0 Then Return
        _effectsMenu.Items.Add(Lang.T("輪廓圖…"), Nothing, Sub() ShowContour())
        _effectsMenu.Items.Add(Lang.T("變形效果…"), Nothing, Sub() ShowDistort())
        _effectsMenu.Items.Add(Lang.T("立體化…"), Nothing, Sub() ShowExtrude())
        _effectsMenu.Items.Add(Lang.T("簡化路徑…"), Nothing, Sub() ShowSimplify())
    End Sub

    Private Sub ShowEffectsMenu()
        BuildEffectsMenu()
        Dim b As Button = Nothing
        If _shapeButtons.TryGetValue("effects", b) AndAlso b.Visible Then
            _effectsMenu.Show(b, New Point(0, b.Height))
        Else
            _effectsMenu.Show(Cursor.Position)
        End If
    End Sub

    '---------------------------------------------------------------------
    ' 修飾筆刷
    '---------------------------------------------------------------------

    Private ReadOnly _touchBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                            .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0), .Visible = False}
    Private ReadOnly _touchMode As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 70}
    Private ReadOnly _touchSize As New NumericUpDown With {.Minimum = 4, .Maximum = 3000, .Value = 60, .Width = 60}
    Private ReadOnly _touchStrength As New NumericUpDown With {.Minimum = 1, .Maximum = 100, .Value = 50, .Width = 52}
    Private ReadOnly _touchTimer As New Timer With {.Interval = 50}
    Private _touchSessions As Dictionary(Of Integer, TouchUpSession)
    Private _touchLast As PointF
    Private _touchCursor As PointF?
    Private _touchKey As String
    Private _touchSerial As Integer
    Private _touchMoved As Boolean ' 這次拖曳有沒有真的推到邊（沒有的話放開時不要把圖形換成路徑）

    Private Function BuildTouchBar() As Control
        _touchMode.Items.AddRange({Lang.T("塗抹"), Lang.T("轉動"), Lang.T("吸引"), Lang.T("排斥"), Lang.T("粗糙"), Lang.T("平滑")})
        _touchMode.SelectedIndex = 0
        Dim add = Sub(caption As String, c As Control, help As String)
                      If caption <> "" Then _touchBar.Controls.Add(New Label With {.Text = caption, .AutoSize = True, .Margin = New Padding(6, 7, 2, 0), .BackColor = Color.Transparent})
                      c.Margin = New Padding(0, 3, 2, 0)
                      _help.SetHelp(help, c)
                      _touchBar.Controls.Add(c)
                  End Sub
        add("", _touchMode, "draw.touch.mode")
        add(Lang.T("大小"), _touchSize, "draw.touch.size")
        add(Lang.T("力道"), _touchStrength, "draw.touch.strength")
        AddHandler _touchMode.SelectedIndexChanged, Sub() UpdateDrawHint()
        AddHandler _touchTimer.Tick, Sub() TouchTick()
        Return _touchBar
    End Function

    Private Sub UpdateTouchBar()
        _touchBar.Visible = _drawStrip IsNot Nothing AndAlso _drawStrip.SelectedTool = DrawIcons.TouchUpTool
        If Not _touchBar.Visible Then _touchCursor = Nothing
    End Sub

    Private ReadOnly Property TouchMode As TouchUpMode
        Get
            Return CType(Math.Max(0, _touchMode.SelectedIndex), TouchUpMode)
        End Get
    End Property

    ''' <summary>筆刷半徑（照片高度單位）：「大小」是直徑（照片像素）。</summary>
    Private Function TouchRadius() As Double
        Return PxToUnits(CDbl(_touchSize.Value) / 2)
    End Function

    Private Sub TouchDown(e As MouseEventArgs, u As PointF)
        If e.Button <> MouseButtons.Left Then Return
        Dim idx = EffectTargets()
        If idx.Count = 0 Then
            Dim hit = HitLayer(u)
            If hit < 0 OrElse _recipe.Drawings(hit).Locked OrElse Not TouchUpSession.CanTouch(_recipe.Drawings(hit)) Then
                SetStatusMessage(Lang.T("修飾筆刷：先選取（或在上面按下）要修飾的向量圖形。"))
                Return
            End If
            SetMultiSelection({hit}, hit)
            idx = {hit}.ToList()
        End If
        Dim spacing = TouchRadius() / 6
        _touchSessions = New Dictionary(Of Integer, TouchUpSession)()
        For Each i In idx
            Dim s As New TouchUpSession(_recipe.Drawings(i), spacing)
            If Not s.IsEmpty Then _touchSessions(i) = s
        Next
        If _touchSessions.Count = 0 Then _touchSessions = Nothing : Return
        _touchSerial += 1
        _touchKey = "touchup-" & _touchSerial
        _touchMoved = False
        _touchLast = u
        _touchCursor = u
        TouchApply(u, PointF.Empty)
        If TouchMode <> TouchUpMode.Smear Then _touchTimer.Start()
    End Sub

    Private Sub TouchMove(u As PointF)
        _touchCursor = u
        If _touchSessions IsNot Nothing Then
            TouchApply(u, New PointF(u.X - _touchLast.X, u.Y - _touchLast.Y))
            _touchLast = u
        End If
        _canvas.Invalidate()
    End Sub

    ''' <summary>按住不動時，轉動、吸引、排斥、粗糙、平滑繼續作用（同 CorelDRAW）。</summary>
    Private Sub TouchTick()
        If _touchSessions Is Nothing Then _touchTimer.Stop() : Return
        TouchApply(_touchLast, PointF.Empty)
    End Sub

    Private Sub TouchApply(u As PointF, delta As PointF)
        Dim moved = False
        Dim reverse = ModifierKeys.HasFlag(Keys.Alt)
        For Each s In _touchSessions.Values
            If s.Apply(TouchMode, u, TouchRadius(), CDbl(_touchStrength.Value) / 100, delta, reverse) Then moved = True
        Next
        If moved Then _touchMoved = True : TouchPush(final:=False)
    End Sub

    Private Sub TouchPush(final As Boolean)
        Dim sessions = _touchSessions
        ApplyChange(Sub(r)
                        For Each kv In sessions
                            If kv.Key < r.Drawings.Count Then r.Drawings(kv.Key) = KeepIdentity(kv.Value.Result(final), r.Drawings(kv.Key))
                        Next
                    End Sub, _touchKey)
        _canvas.Invalidate()
    End Sub

    Private Sub TouchUp()
        _touchTimer.Stop()
        If _touchSessions Is Nothing Then Return
        If _touchMoved Then TouchPush(final:=True) ' 放開：擬合成貝茲曲線（同一步復原）
        _touchSessions = Nothing
        UpdateDrawControls()
    End Sub

    ''' <summary>筆刷範圍的圓（frmEditor.DrawTools.vb 的 PaintTools 呼叫）。</summary>
    Private Sub PaintTouchCursor(g As Graphics)
        If Not _touchCursor.HasValue OrElse _drawStrip.SelectedTool <> DrawIcons.TouchUpTool Then Return
        Dim c = UnitToScreen(_touchCursor.Value)
        Dim edge = UnitToScreen(New PointF(CSng(_touchCursor.Value.X + TouchRadius()), _touchCursor.Value.Y))
        Dim rr = Math.Max(2.0F, edge.X - c.X)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using outer As New Pen(Color.FromArgb(170, 255, 255, 255), 3), pen As New Pen(Color.FromArgb(220, 55, 138, 221), 1.3F)
            g.DrawEllipse(outer, c.X - rr, c.Y - rr, rr * 2, rr * 2)
            g.DrawEllipse(pen, c.X - rr, c.Y - rr, rr * 2, rr * 2)
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 指令、快捷鍵
    '---------------------------------------------------------------------

    Private Function RunBatch5Command(name As String) As Boolean
        Select Case name
            Case "contour", "distort", "extrude", "simplify"
            Case Else : Return False
        End Select
        If _tabs.SelectedIndex <> TabDraw Then _tabs.SelectedIndex = TabDraw
        Select Case name
            Case "contour" : ShowContour()
            Case "distort" : ShowDistort()
            Case "extrude" : ShowExtrude()
            Case "simplify" : ShowSimplify()
        End Select
        Return True
    End Function

    ''' <summary>修飾筆刷時 [ ] 調大小（同其他筆刷）。</summary>
    Private Function Batch5Key(keyData As Keys) As Boolean
        If _drawStrip.SelectedTool <> DrawIcons.TouchUpTool Then Return False
        Select Case keyData
            Case Keys.OemOpenBrackets
                _touchSize.Value = Math.Max(_touchSize.Minimum, Math.Round(_touchSize.Value / 1.2D))
            Case Keys.OemCloseBrackets
                _touchSize.Value = Math.Min(_touchSize.Maximum, Math.Round(_touchSize.Value * 1.2D) + 1)
            Case Else : Return False
        End Select
        _canvas.Invalidate()
        Return True
    End Function
End Class
