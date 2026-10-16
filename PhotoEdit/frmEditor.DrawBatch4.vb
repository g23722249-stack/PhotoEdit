Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 向量第一批（對照 CorelDRAW、Illustrator 補齊常用工具）：
''' 鏡射（屬性列的水平／垂直鏡射，多選、群組以共同中心為軸）；
''' 再製（Ctrl+J，同 CorelDRAW 的智慧再製：複本移動或旋轉後再按，下一份照同樣的距離與角度）與「步驟與重複」；
''' 屬性吸管（吸管工具的第二個：點一下吸取外觀，再點其他圖形套用；先選好物件時直接套用到選取的）；
''' 正多邊形、星形的邊數（角數）、螺旋的圈數、格紙的欄列、弧形的起點與角度（上方的圖形參數列：選了這種圖形時改它，
''' 選了這種工具時是之後畫的預設）；外框轉物件（操作列，見 DrawingRenderer.OutlineToObject）。
''' </summary>
Partial Friend Class frmEditor

    '---------------------------------------------------------------------
    ' 鏡射
    '---------------------------------------------------------------------

    ''' <summary>水平或垂直鏡射選取的物件（鎖定、點陣、填滿圖層不動）；多選時以共同外框的中心為軸。</summary>
    Private Sub MirrorSelected(horizontal As Boolean)
        Dim idx = SelectedIndices().Where(Function(i) Not _recipe.Drawings(i).Locked AndAlso DrawGeometry.CanMirror(_recipe.Drawings(i))).ToList()
        If idx.Count = 0 Then
            SetStatusMessage(Lang.T("先選取向量圖形（點陣圖層、填滿圖層不能鏡射）。"))
            Return
        End If
        Dim b = DrawGeometry.GroupBounds(idx.Select(Function(i) _recipe.Drawings(i)))
        Dim cx = b.X + b.Width / 2.0, cy = b.Y + b.Height / 2.0
        ApplyChange(Sub(r)
                        For Each i In idx
                            DrawGeometry.Mirror(r.Drawings(i), horizontal, cx, cy)
                        Next
                    End Sub)
        _canvas.Invalidate()
        UpdatePropBar(force:=True)
    End Sub

    ''' <summary>鏡射按鈕的圖示：中間一條虛線軸，兩邊各一個三角形（左邊實心、右邊空心）。</summary>
    Private Shared Sub PaintMirrorGlyph(g As Graphics, b As Button, horizontal As Boolean)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim col = If(b.Enabled, ThemeManager.Fore(Color.FromArgb(50, 56, 66)), Color.FromArgb(160, 164, 170))
        Dim cx = b.Width / 2.0F, cy = b.Height / 2.0F
        Dim t = Function(p As PointF) If(horizontal, p, New PointF(cx + (p.Y - cy), cy + (p.X - cx)))
        Using br As New SolidBrush(col), pen As New Pen(col, 1.3F), axis As New Pen(col, 1) With {.DashStyle = DashStyle.Dot}
            g.DrawLine(axis, t(New PointF(cx, cy - 8)), t(New PointF(cx, cy + 8)))
            g.FillPolygon(br, {t(New PointF(cx - 2, cy - 6)), t(New PointF(cx - 2, cy + 6)), t(New PointF(cx - 9, cy + 6))})
            g.DrawPolygon(pen, {t(New PointF(cx + 2, cy - 6)), t(New PointF(cx + 2, cy + 6)), t(New PointF(cx + 9, cy + 6))})
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 再製（智慧再製）、步驟與重複
    '---------------------------------------------------------------------

    ''' <summary>上一次再製：原本的與複本（圖層 Id）。複本被移動、旋轉後再按再製，照同樣的變化做下一份。</summary>
    Private _dupSources As List(Of String)
    Private _dupCopies As List(Of String)
    Private Const DefaultDupOffset As Double = 0.02

    Private Function LayerById(id As String) As DrawLayer
        If id Is Nothing OrElse _recipe.Drawings Is Nothing Then Return Nothing
        Return _recipe.Drawings.FirstOrDefault(Function(d) d.Id = id)
    End Function

    ''' <summary>
    ''' 再製（Ctrl+J）：選取的物件各做一個複本，選取換成複本。
    ''' 如果選的正是上一次再製出來的複本，就照「原本 → 複本」的位移與旋轉再做一份（像 CorelDRAW：再製一份、拖到想要的位置，
    ''' 之後一直按就等距排下去）；否則往右下錯開一點。
    ''' </summary>
    Private Sub DuplicateSmart()
        Dim idx = SelectedIndices().Where(Function(i) _recipe.Drawings(i).Shape <> DrawShape.FillLayer).ToList()
        If idx.Count = 0 Then SetStatusMessage(Lang.T("先選取要再製的物件。")) : Return
        Dim originals = idx.Select(Function(i) _recipe.Drawings(i)).ToList()
        Dim dx = DefaultDupOffset, dy = DefaultDupOffset, rot = 0.0
        Dim ids = originals.Select(Function(d) d.Id).ToList()
        If _dupCopies IsNot Nothing AndAlso _dupSources IsNot Nothing AndAlso ids.All(Function(x) x IsNot Nothing) AndAlso
           ids.Count = _dupCopies.Count AndAlso ids.All(Function(x) _dupCopies.Contains(x)) Then
            Dim srcs = _dupSources.Select(Function(x) LayerById(x)).ToList()
            Dim cps = _dupCopies.Select(Function(x) LayerById(x)).ToList()
            If srcs.All(Function(x) x IsNot Nothing) AndAlso cps.All(Function(x) x IsNot Nothing) Then
                If srcs.Count = 1 AndAlso DrawGeometry.IsBoxLayer(srcs(0)) AndAlso DrawGeometry.IsBoxLayer(cps(0)) Then
                    ' 單一個方框類圖形：用圖形本身的中心（旋轉會讓外框中心偏掉）與角度差
                    dx = cps(0).X - srcs(0).X : dy = cps(0).Y - srcs(0).Y
                    rot = cps(0).Rotation - srcs(0).Rotation
                Else
                    Dim a = DrawGeometry.GroupBounds(srcs), b = DrawGeometry.GroupBounds(cps)
                    dx = (b.X + b.Width / 2) - (a.X + a.Width / 2)
                    dy = (b.Y + b.Height / 2) - (a.Y + a.Height / 2)
                End If
            End If
        End If
        Dim copies = MakeCopies(originals, dx, dy, rot)
        ApplyChange(Sub(r)
                        For k = 0 To copies.Count - 1
                            r.Drawings.Add(copies(k))
                            LayerStack.PlaceAbove(r, copies(k), originals(k))
                        Next
                    End Sub)
        _dupSources = originals.Select(Function(d) d.Id).ToList()
        _dupCopies = copies.Select(Function(d) d.Id).ToList()
        Dim newIdx = copies.Select(Function(c) _recipe.Drawings.IndexOf(c)).Where(Function(i) i >= 0).ToList()
        If newIdx.Count > 0 Then SetMultiSelection(newIdx, newIdx.Max())
        SetStatusMessage(Lang.T("已再製。把複本移到想要的位置（或旋轉）後再按 Ctrl+J，會照同樣的距離與角度一直排下去。"))
    End Sub

    ''' <summary>
    ''' 複本：位移 (dx, dy)、再轉 rot 度（單一個方框類圖形在原地轉；其他以複本的共同中心轉）；名稱加「複本」。
    ''' </summary>
    Private Shared Function MakeCopies(originals As List(Of DrawLayer), dx As Double, dy As Double, rot As Double) As List(Of DrawLayer)
        Dim b = DrawGeometry.GroupBounds(originals)
        Dim c As New PointF(CSng(b.X + b.Width / 2 + dx), CSng(b.Y + b.Height / 2 + dy))
        Dim inPlace = originals.Count = 1 AndAlso DrawGeometry.IsBoxLayer(originals(0))
        Return originals.Select(Function(o)
                                    Dim d = o.Clone()
                                    DrawGeometry.Offset(d, dx, dy)
                                    If inPlace Then
                                        d.Rotation = ((d.Rotation + rot) Mod 360 + 540) Mod 360 - 180 ' −180～180
                                    ElseIf Math.Abs(rot) > 0.0001 Then
                                        Dim s = d.Clone()
                                        DrawGeometry.RotateInGroup(d, s, c, rot)
                                    End If
                                    If Not d.Name.EndsWith(Lang.T(" 複本")) Then d.Name &= Lang.T(" 複本")
                                    d.Id = Nothing
                                    Return d
                                End Function).ToList()
    End Function

    ''' <summary>步驟與重複：選取的物件依份數、水平與垂直距離（照片像素）、每份旋轉角度一次做好幾份。</summary>
    Private Sub StepAndRepeat()
        Dim idx = SelectedIndices().Where(Function(i) _recipe.Drawings(i).Shape <> DrawShape.FillLayer).ToList()
        If idx.Count = 0 Then SetStatusMessage(Lang.T("先選取要重複的物件。")) : Return
        Dim px = CDbl(PhotoPixelSize().Height)
        Dim b = DrawGeometry.GroupBounds(idx.Select(Function(i) _recipe.Drawings(i)))
        Using dlg As New Form With {.Text = Lang.T("步驟與重複"), .FormBorderStyle = FormBorderStyle.FixedDialog, .MaximizeBox = False, .MinimizeBox = False,
                                   .ShowInTaskbar = False, .StartPosition = FormStartPosition.CenterParent, .ClientSize = New Size(300, 196), .Font = _panelFont}
            Dim rows = {(Lang.T("份數"), 1, 200, 0, 5.0),
                        (Lang.T("水平距離（像素）"), -20000, 20000, 0, Math.Round(b.Width * px * 1.1)),
                        (Lang.T("垂直距離（像素）"), -20000, 20000, 0, 0.0),
                        (Lang.T("每份旋轉（度）"), -360, 360, 1, 0.0)}
            Dim boxes As New List(Of NumericUpDown)()
            Dim y = 14
            For Each r In rows
                Dim lbl As New Label With {.Text = r.Item1, .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
                lbl.SetBounds(14, y, 150, 26)
                Dim nb As New NumericUpDown With {.Minimum = r.Item2, .Maximum = r.Item3, .DecimalPlaces = r.Item4, .Value = CDec(Math.Max(r.Item2, Math.Min(r.Item3, r.Item5)))}
                nb.SetBounds(170, y, 110, 26)
                dlg.Controls.AddRange({lbl, nb})
                boxes.Add(nb)
                y += 32
            Next
            Dim ok As New Button With {.Text = Lang.T("確定"), .DialogResult = DialogResult.OK}
            Dim cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel}
            ok.SetBounds(100, y + 6, 88, 30) : cancel.SetBounds(196, y + 6, 88, 30)
            dlg.Controls.AddRange({ok, cancel})
            dlg.AcceptButton = ok : dlg.CancelButton = cancel
            ThemeManager.Attach(dlg)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            RepeatSelected(idx, CInt(boxes(0).Value), CDbl(boxes(1).Value) / px, CDbl(boxes(2).Value) / px, CDbl(boxes(3).Value))
        End Using
    End Sub

    ''' <summary>步驟與重複的實際動作：count 份，每份再位移 (dx, dy)（照片高度單位）、轉 rot 度。</summary>
    Private Sub RepeatSelected(idx As List(Of Integer), count As Integer, dx As Double, dy As Double, rot As Double)
        Dim originals = idx.Select(Function(i) _recipe.Drawings(i)).ToList()
        Dim all As New List(Of DrawLayer)()
        Dim prev = originals
        For k = 1 To count
            Dim copies = MakeCopies(prev, dx, dy, rot)
            all.AddRange(copies)
            prev = copies
        Next
        ApplyChange(Sub(r)
                        ' 依序疊上去：每一份都在前一份上面
                        Dim above = originals
                        For k = 0 To count - 1
                            Dim batch = all.Skip(k * originals.Count).Take(originals.Count).ToList()
                            For j = 0 To batch.Count - 1
                                r.Drawings.Add(batch(j))
                                LayerStack.PlaceAbove(r, batch(j), above(j))
                            Next
                            above = batch
                        Next
                    End Sub)
        Dim newIdx = all.Select(Function(c) _recipe.Drawings.IndexOf(c)).Where(Function(i) i >= 0).ToList()
        If newIdx.Count > 0 Then SetMultiSelection(newIdx, newIdx.Max())
        SetStatusMessage(Lang.F("已做 {0} 份。", count))
    End Sub

    '---------------------------------------------------------------------
    ' 屬性吸管
    '---------------------------------------------------------------------

    ''' <summary>屬性吸管吸到的外觀（圖層的複本）；Nothing＝還沒吸。</summary>
    Private _attrSource As DrawLayer

    ''' <summary>
    ''' 屬性吸管：先選了物件時，點來源圖形就把外觀套到選取的物件；沒有選取時，第一下吸取，之後點的圖形都套用
    ''' （Shift＋點或 Esc 重新吸取）。
    ''' </summary>
    Private Sub AttrDropperClick(e As MouseEventArgs, u As PointF)
        If e.Button <> MouseButtons.Left Then Return
        Dim hit = HitLayer(u)
        If hit < 0 OrElse Not DrawGeometry.HasAppearance(_recipe.Drawings(hit)) Then
            SetStatusMessage(Lang.T("點在向量圖形上（點陣圖層、填滿圖層沒有這些屬性）。"))
            Return
        End If
        Dim targets = SelectedIndices().Where(Function(i) i <> hit AndAlso Not _recipe.Drawings(i).Locked AndAlso DrawGeometry.HasAppearance(_recipe.Drawings(i))).ToList()
        If _attrSource Is Nothing OrElse ModifierKeys.HasFlag(Keys.Shift) Then
            _attrSource = _recipe.Drawings(hit).Clone()
            If targets.Count > 0 Then
                ApplyAppearance(targets, _attrSource)
                SetStatusMessage(Lang.F("已把「{0}」的外觀套到選取的 {1} 個物件。", _attrSource.Name, targets.Count))
            Else
                SetStatusMessage(Lang.F("已吸取「{0}」的外觀：點其他圖形套用；Shift＋點或 Esc 重新吸取。", _attrSource.Name))
            End If
        Else
            ApplyAppearance({hit}.ToList(), _attrSource)
            SetStatusMessage(Lang.F("已套用「{0}」的外觀。", _attrSource.Name))
        End If
        UpdateDrawHint()
    End Sub

    Private Sub ApplyAppearance(targets As List(Of Integer), source As DrawLayer)
        ApplyChange(Sub(r)
                        For Each i In targets
                            DrawGeometry.CopyAppearance(r.Drawings(i), source)
                        Next
                    End Sub)
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 外框轉物件
    '---------------------------------------------------------------------

    ''' <summary>選取中可以轉的物件。</summary>
    Private Function OutlineCandidates() As List(Of Integer)
        Return SelectedIndices().Where(Function(i) Not _recipe.Drawings(i).Locked AndAlso DrawingRenderer.CanOutlineStroke(_recipe.Drawings(i))).ToList()
    End Function

    ''' <summary>
    ''' 外框轉物件：線條變成填滿線條色的鋼筆路徑（放在原本的上面）；原本有填色的保留填色、拿掉外框，沒有填色的刪掉。
    ''' </summary>
    Private Sub OutlineSelected()
        Dim idx = OutlineCandidates()
        If idx.Count = 0 Then SetStatusMessage(Lang.T("先選取有線條的向量圖形（特效筆、粒子、點陣圖層不能轉）。")) : Return
        Cursor = Cursors.WaitCursor
        Dim results As New List(Of (Index As Integer, Outline As DrawLayer))()
        Try
            For Each i In idx
                Dim o = DrawingRenderer.OutlineToObject(_recipe.Drawings(i))
                If o IsNot Nothing Then results.Add((i, o))
            Next
        Finally
            Cursor = Cursors.Default
        End Try
        If results.Count = 0 Then SetStatusMessage(Lang.T("線條太細或是空的，沒有可以轉的。")) : Return
        Dim originals = results.Select(Function(x) _recipe.Drawings(x.Index)).ToList()
        ApplyChange(Sub(r)
                        For k = 0 To results.Count - 1
                            Dim src = originals(k)
                            Dim outline = results(k).Outline
                            r.Drawings.Add(outline)
                            LayerStack.PlaceAbove(r, outline, src)
                            Dim keepFill = src.Filled AndAlso DrawGeometry.IsClosedLayer(src)
                            If keepFill OrElse DrawGeometry.HasText(src.Shape) Then
                                src.Stroked = False ' 原本的填色（與文字）留著
                            Else
                                r.Drawings.Remove(src)
                            End If
                        Next
                    End Sub)
        Dim newIdx = results.Select(Function(x) _recipe.Drawings.IndexOf(x.Outline)).Where(Function(i) i >= 0).ToList()
        If newIdx.Count > 0 Then SetMultiSelection(newIdx, newIdx.Max())
        SetStatusMessage(Lang.T("已把外框轉成物件：可以填色、焊接、用節點工具修改。"))
    End Sub

    '---------------------------------------------------------------------
    ' 圖形參數列：邊數、角數、內徑、圈數、欄列、弧形
    '---------------------------------------------------------------------

    Private ReadOnly _shapeParamBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                                 .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0), .Visible = False}
    Private ReadOnly _paramSides As New NumericUpDown With {.Minimum = 3, .Maximum = 64, .Width = 52}
    Private ReadOnly _paramInner As New NumericUpDown With {.Minimum = 5, .Maximum = 95, .Width = 52}
    Private ReadOnly _paramTurns As New NumericUpDown With {.Minimum = 0.25D, .Maximum = 30, .DecimalPlaces = 2, .Increment = 0.25D, .Width = 60}
    Private ReadOnly _paramCols As New NumericUpDown With {.Minimum = 1, .Maximum = 50, .Width = 48}
    Private ReadOnly _paramRows As New NumericUpDown With {.Minimum = 1, .Maximum = 50, .Width = 48}
    Private ReadOnly _paramArcMode As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 76}
    Private ReadOnly _paramArcStart As New NumericUpDown With {.Minimum = -360, .Maximum = 360, .Width = 58}
    Private ReadOnly _paramArcSweep As New NumericUpDown With {.Minimum = 1, .Maximum = 360, .Width = 58}
    ' 尺寸線：方向、單位、每單位幾像素、小數位數；連接線：直線或直角
    Private ReadOnly _paramDimKind As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 56}
    Private ReadOnly _paramDimUnit As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 48}
    Private ReadOnly _paramDimPx As New NumericUpDown With {.Minimum = 0.01D, .Maximum = 100000, .DecimalPlaces = 2, .Width = 66}
    Private ReadOnly _paramDimDec As New NumericUpDown With {.Minimum = 0, .Maximum = 4, .Width = 40}
    Private ReadOnly _paramConnStyle As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 56}
    Private Shared ReadOnly DimUnits As String() = {"px", "mm", "cm", "m", "in"}
    ''' <summary>單位的預設：每單位幾像素（96 dpi）、小數位數。</summary>
    Private Shared Function DimUnitDefaults(unit As String) As (Px As Double, Decimals As Integer)
        Select Case unit
            Case "mm" : Return (3.78, 0)
            Case "cm" : Return (37.8, 1)
            Case "m" : Return (3780, 2)
            Case "in" : Return (96, 2)
            Case Else : Return (1, 0)
        End Select
    End Function
    Private ReadOnly _paramLabels As New Dictionary(Of Control, Label)()
    Private _paramSyncing As Boolean
    ''' <summary>之後畫的圖形用的參數（每種工具一份）。</summary>
    Private ReadOnly _shapeDefaults As New Dictionary(Of DrawShape, DrawLayer)()

    Private Shared Function HasShapeParams(s As DrawShape) As Boolean
        Return s = DrawShape.RegularPolygon OrElse s = DrawShape.Star OrElse s = DrawShape.Spiral OrElse s = DrawShape.Grid OrElse s = DrawShape.Arc OrElse
               s = DrawShape.Dimension OrElse s = DrawShape.Connector
    End Function

    Private Function BuildShapeParamBar() As Control
        _paramArcMode.Items.AddRange({Lang.T("扇形"), Lang.T("弧線"), Lang.T("弓形")})
        Dim add = Sub(caption As String, c As Control, help As String)
                      Dim lbl As New Label With {.Text = caption, .AutoSize = True, .Margin = New Padding(6, 7, 2, 0), .BackColor = Color.Transparent}
                      c.Margin = New Padding(0, 3, 2, 0)
                      _help.SetHelp(help, c)
                      _paramLabels(c) = lbl
                      _shapeParamBar.Controls.Add(lbl)
                      _shapeParamBar.Controls.Add(c)
                  End Sub
        add(Lang.T("邊數"), _paramSides, "draw.param.sides")
        add(Lang.T("內徑"), _paramInner, "draw.param.inner")
        add(Lang.T("圈數"), _paramTurns, "draw.param.turns")
        add(Lang.T("欄"), _paramCols, "draw.param.grid")
        add(Lang.T("列"), _paramRows, "draw.param.grid")
        add("", _paramArcMode, "draw.param.arcmode")
        add(Lang.T("起點"), _paramArcStart, "draw.param.arc")
        add(Lang.T("角度"), _paramArcSweep, "draw.param.arc")
        _paramDimKind.Items.AddRange({Lang.T("平行"), Lang.T("水平"), Lang.T("垂直")})
        _paramDimUnit.Items.AddRange(DimUnits)
        _paramConnStyle.Items.AddRange({Lang.T("直線"), Lang.T("直角")})
        add("", _paramDimKind, "draw.param.dimkind")
        add(Lang.T("單位"), _paramDimUnit, "draw.param.dimunit")
        add(Lang.T("每單位像素"), _paramDimPx, "draw.param.dimunit")
        add(Lang.T("小數"), _paramDimDec, "draw.param.dimdec")
        add("", _paramConnStyle, "draw.param.connstyle")
        For Each nb In {_paramSides, _paramInner, _paramTurns, _paramCols, _paramRows, _paramArcStart, _paramArcSweep, _paramDimPx, _paramDimDec}
            AddHandler nb.ValueChanged, Sub() OnShapeParamChanged()
        Next
        For Each cb In {_paramArcMode, _paramDimKind, _paramDimUnit, _paramConnStyle}
            AddHandler cb.SelectedIndexChanged, Sub() OnShapeParamChanged()
        Next
        Return _shapeParamBar
    End Function

    ''' <summary>參數列改的是誰：選取工具選了一個這種圖形 → 那個圖層；用這種工具 → 之後畫的預設。</summary>
    Private Function ShapeParamTarget() As (Layer As DrawLayer, Index As Integer, Shape As DrawShape)?
        Dim tool = _drawStrip.SelectedTool
        If tool = DrawIcons.SelectTool Then
            Dim idx = SelectedIndices()
            If idx.Count = 1 Then
                Dim d = _recipe.Drawings(idx(0))
                If HasShapeParams(d.Shape) AndAlso Not d.Locked Then Return (d, idx(0), d.Shape)
            End If
            Return Nothing
        End If
        If tool >= 0 AndAlso HasShapeParams(CType(tool, DrawShape)) Then
            Dim s = CType(tool, DrawShape)
            Return (ShapeDefaults(s), -1, s)
        End If
        Return Nothing
    End Function

    Private Function ShapeDefaults(s As DrawShape) As DrawLayer
        Dim d As DrawLayer = Nothing
        If Not _shapeDefaults.TryGetValue(s, d) Then
            d = New DrawLayer With {.Shape = s}
            DrawGeometry.ApplyDefaults(d)
            _shapeDefaults(s) = d
        End If
        Return d
    End Function

    ''' <summary>新畫的圖形套用參數列的設定（NewShape 呼叫）。</summary>
    Private Sub ApplyShapeParamDefaults(d As DrawLayer)
        If Not HasShapeParams(d.Shape) Then Return
        Dim t = ShapeDefaults(d.Shape)
        d.Sides = t.Sides : d.Rows = t.Rows : d.ArcMode = t.ArcMode
        d.DimKind = t.DimKind : d.DimUnit = t.DimUnit : d.DimScale = t.DimScale : d.DimDecimals = t.DimDecimals : d.ConnElbow = t.ConnElbow
        Select Case d.Shape
            Case DrawShape.Star, DrawShape.Spiral : d.Param1 = t.Param1
            Case DrawShape.Arc : d.Param1 = t.Param1 : d.Param2 = t.Param2
        End Select
    End Sub

    ''' <summary>依目前的選取或工具顯示對應的參數。</summary>
    Private Sub UpdateShapeParamBar()
        Dim t = ShapeParamTarget()
        _shapeParamBar.Visible = t.HasValue
        If Not t.HasValue Then Return
        Dim d = t.Value.Layer, s = t.Value.Shape
        Dim show = Sub(c As Control, visible As Boolean)
                       c.Visible = visible
                       _paramLabels(c).Visible = visible
                   End Sub
        show(_paramSides, s = DrawShape.RegularPolygon OrElse s = DrawShape.Star)
        _paramLabels(_paramSides).Text = If(s = DrawShape.Star, Lang.T("角數"), Lang.T("邊數"))
        show(_paramInner, s = DrawShape.Star)
        show(_paramTurns, s = DrawShape.Spiral)
        show(_paramCols, s = DrawShape.Grid) : show(_paramRows, s = DrawShape.Grid)
        show(_paramArcMode, s = DrawShape.Arc) : show(_paramArcStart, s = DrawShape.Arc) : show(_paramArcSweep, s = DrawShape.Arc)
        Dim isDim = s = DrawShape.Dimension
        show(_paramDimKind, isDim) : show(_paramDimUnit, isDim) : show(_paramDimDec, isDim)
        show(_paramDimPx, isDim AndAlso d.DimUnit <> "px")
        show(_paramConnStyle, s = DrawShape.Connector)
        _paramSyncing = True
        Try
            _paramSides.Value = Math.Max(3, Math.Min(64, d.Sides))
            _paramInner.Value = CDec(Math.Max(5, Math.Min(95, Math.Round(d.Param1 * 100))))
            If s = DrawShape.Spiral Then _paramTurns.Value = CDec(Math.Max(0.25, Math.Min(30, d.Param1)))
            _paramCols.Value = Math.Max(1, Math.Min(50, d.Sides))
            _paramRows.Value = Math.Max(1, Math.Min(50, d.Rows))
            _paramArcMode.SelectedIndex = Math.Max(0, Math.Min(2, CInt(d.ArcMode)))
            _paramDimKind.SelectedIndex = Math.Max(0, Math.Min(2, CInt(d.DimKind)))
            _paramDimUnit.SelectedIndex = Math.Max(0, Array.IndexOf(DimUnits, If(d.DimUnit, "px")))
            _paramDimPx.Value = CDec(Math.Max(0.01, Math.Min(100000, Math.Round(1 / Math.Max(0.000001, d.DimScale), 2))))
            _paramDimDec.Value = Math.Max(0, Math.Min(4, d.DimDecimals))
            _paramConnStyle.SelectedIndex = If(d.ConnElbow, 1, 0)
            If s = DrawShape.Arc Then
                Dim st = d.Param1 Mod 360
                If st > 180 Then st -= 360
                If st <= -180 Then st += 360
                _paramArcStart.Value = CDec(Math.Round(st))
                _paramArcSweep.Value = CDec(Math.Max(1, Math.Min(360, Math.Round(d.Param2))))
            End If
        Finally
            _paramSyncing = False
        End Try
    End Sub

    Private Sub OnShapeParamChanged()
        If _paramSyncing Then Return
        Dim t = ShapeParamTarget()
        If Not t.HasValue Then Return
        Dim apply = Sub(d As DrawLayer)
                        Select Case d.Shape
                            Case DrawShape.RegularPolygon : d.Sides = CInt(_paramSides.Value)
                            Case DrawShape.Star : d.Sides = CInt(_paramSides.Value) : d.Param1 = CDbl(_paramInner.Value) / 100
                            Case DrawShape.Spiral : d.Param1 = CDbl(_paramTurns.Value)
                            Case DrawShape.Grid : d.Sides = CInt(_paramCols.Value) : d.Rows = CInt(_paramRows.Value)
                            Case DrawShape.Arc
                                d.ArcMode = CType(Math.Max(0, _paramArcMode.SelectedIndex), ArcKind)
                                d.Param1 = CDbl(_paramArcStart.Value) : d.Param2 = CDbl(_paramArcSweep.Value)
                            Case DrawShape.Dimension
                                d.DimKind = CType(Math.Max(0, _paramDimKind.SelectedIndex), DimensionKind)
                                Dim unit = DimUnits(Math.Max(0, _paramDimUnit.SelectedIndex))
                                If unit <> d.DimUnit Then
                                    ' 換單位：每單位像素、小數位數換成那個單位的預設
                                    Dim def = DimUnitDefaults(unit)
                                    d.DimUnit = unit : d.DimScale = 1 / def.Px : d.DimDecimals = def.Decimals
                                Else
                                    d.DimScale = If(unit = "px", 1, 1 / Math.Max(0.01, CDbl(_paramDimPx.Value)))
                                    d.DimDecimals = CInt(_paramDimDec.Value)
                                End If
                            Case DrawShape.Connector
                                d.ConnElbow = _paramConnStyle.SelectedIndex = 1
                        End Select
                    End Sub
        If t.Value.Index < 0 Then
            apply(t.Value.Layer) ' 之後畫的預設
            UpdateShapeParamBar()
            Return
        End If
        Dim index = t.Value.Index
        ApplyChange(Sub(r) apply(r.Drawings(index)), "draw-shapeparam-" & index)
        UpdateShapeParamBar() ' 換單位時每單位像素、小數跟著換
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 指令、快捷鍵
    '---------------------------------------------------------------------

    Private Function RunBatch4Command(name As String) As Boolean
        Select Case name
            Case "duplicate", "steprepeat", "mirrorh", "mirrorv", "outline"
            Case Else : Return False
        End Select
        If _tabs.SelectedIndex <> TabDraw Then _tabs.SelectedIndex = TabDraw
        Select Case name
            Case "duplicate" : DuplicateSmart()
            Case "steprepeat" : StepAndRepeat()
            Case "mirrorh" : MirrorSelected(True)
            Case "mirrorv" : MirrorSelected(False)
            Case "outline" : OutlineSelected()
        End Select
        Return True
    End Function

    ''' <summary>繪圖分頁的快捷鍵：Ctrl+J 再製；屬性吸管時 Esc 重新吸取。</summary>
    Private Function Batch4Key(keyData As Keys) As Boolean
        If keyData = (Keys.Control Or Keys.J) Then DuplicateSmart() : Return True
        If keyData = Keys.Escape AndAlso _drawStrip.SelectedTool = DrawIcons.AttrDropperTool AndAlso _attrSource IsNot Nothing Then
            _attrSource = Nothing
            UpdateDrawHint()
            SetStatusMessage(Lang.T("屬性吸管：點一個圖形吸取它的外觀。"))
            Return True
        End If
        Return False
    End Function
End Class
