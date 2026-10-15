Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 繪圖分頁（同 CorelDRAW 的屬性列）：
''' 上方屬性列輸入位置、大小、角度（照片像素；Enter 套用、上下鍵加減 1，Shift 加減 10，鎖頭＝等比例）；
''' 右側面板的線條樣式（虛線、端點、轉角、起點與終點箭頭）；
''' PowerClip「放進形狀」「取出內容」與「照片放進形狀」（形狀外面的照片變透明）。運算見 ClipRender、LineStyle。
''' </summary>
Partial Friend Class frmEditor

    ' ---- 屬性列 ----
    Private ReadOnly _propBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                           .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0)}
    Private ReadOnly _propBoxes As New Dictionary(Of String, TextBox)()
    Private ReadOnly _propLock As New CheckBox With {.Appearance = Appearance.Button, .Text = "", .Checked = True, .Size = New Size(22, 24),
                                                     .Margin = New Padding(0, 2, 0, 0), .FlatStyle = FlatStyle.Flat}
    Private _propSyncing As Boolean
    ' ---- 線條樣式 ----
    Private ReadOnly _lineDash As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .DrawMode = DrawMode.OwnerDrawFixed, .ItemHeight = 18}
    Private ReadOnly _lineCap As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _lineJoin As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _arrowStart As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .DrawMode = DrawMode.OwnerDrawFixed, .ItemHeight = 18}
    Private ReadOnly _arrowEnd As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .DrawMode = DrawMode.OwnerDrawFixed, .ItemHeight = 18}

    Private Shared ReadOnly DashNames As String() = {"實線", "虛線", "點線", "點虛線", "長虛線"}
    Private Shared ReadOnly ArrowNames As String() = {"無", "三角", "開放", "圓點", "方塊", "菱形"}

    '---------------------------------------------------------------------
    ' 右側面板：線條樣式
    '---------------------------------------------------------------------

    Private Sub BuildLineStyleRows(L As PageLayout)
        Dim half = (L.Width - CaptionWidth - 8 - 6) \ 2
        ' 樣式
        _help.SetHelpLinked("draw.line.dash", _lineDash, AddCaption(L, "線條樣式", L.Y), _lineDash)
        _lineDash.Items.AddRange(DashNames.Cast(Of Object)().ToArray())
        _lineDash.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _lineDash.DrawItem, Sub(s, e) DrawLineItem(e, isArrow:=False, atStart:=False)
        AddHandler _lineDash.SelectedIndexChanged, Sub() If Not _syncing AndAlso _lineDash.SelectedIndex >= 0 Then SetDrawProp(Sub(d) d.Dash = CType(_lineDash.SelectedIndex, LineDash))
        L.Add(_lineDash)
        L.Y += RowHeight
        ' 端點、轉角
        _help.SetHelpLinked("draw.line.cap", _lineCap, AddCaption(L, "端點／轉角", L.Y), _lineCap)
        _help.SetHelp("draw.line.join", _lineJoin)
        _lineCap.Items.AddRange({"圓頭", "平頭", "方頭"})
        _lineJoin.Items.AddRange({"圓角", "尖角", "斜角"})
        _lineCap.SetBounds(8 + CaptionWidth, L.Y + 3, half, 24)
        _lineJoin.SetBounds(8 + CaptionWidth + half + 6, L.Y + 3, half, 24)
        AddHandler _lineCap.SelectedIndexChanged, Sub() If Not _syncing AndAlso _lineCap.SelectedIndex >= 0 Then SetDrawProp(Sub(d) d.Cap = CType(_lineCap.SelectedIndex, LineCapKind))
        AddHandler _lineJoin.SelectedIndexChanged, Sub() If Not _syncing AndAlso _lineJoin.SelectedIndex >= 0 Then SetDrawProp(Sub(d) d.Join = CType(_lineJoin.SelectedIndex, LineJoinKind))
        L.Add(_lineCap) : L.Add(_lineJoin)
        L.Y += RowHeight
        ' 箭頭
        _help.SetHelpLinked("draw.line.arrow", _arrowStart, AddCaption(L, "起點／終點", L.Y), _arrowStart)
        _help.SetHelp("draw.line.arrow", _arrowEnd)
        For Each cb In {_arrowStart, _arrowEnd}
            cb.Items.AddRange(ArrowNames.Cast(Of Object)().ToArray())
        Next
        _arrowStart.SetBounds(8 + CaptionWidth, L.Y + 3, half, 24)
        _arrowEnd.SetBounds(8 + CaptionWidth + half + 6, L.Y + 3, half, 24)
        AddHandler _arrowStart.DrawItem, Sub(s, e) DrawLineItem(e, isArrow:=True, atStart:=True)
        AddHandler _arrowEnd.DrawItem, Sub(s, e) DrawLineItem(e, isArrow:=True, atStart:=False)
        AddHandler _arrowStart.SelectedIndexChanged, Sub() If Not _syncing AndAlso _arrowStart.SelectedIndex >= 0 Then SetDrawProp(Sub(d) d.StartArrow = CType(_arrowStart.SelectedIndex, ArrowKind))
        AddHandler _arrowEnd.SelectedIndexChanged, Sub() If Not _syncing AndAlso _arrowEnd.SelectedIndex >= 0 Then SetDrawProp(Sub(d) d.EndArrow = CType(_arrowEnd.SelectedIndex, ArrowKind))
        L.Add(_arrowStart) : L.Add(_arrowEnd)
        L.Y += RowHeight
    End Sub

    ''' <summary>線條樣式、箭頭選單的項目：畫出樣子（樣式）或箭頭加名稱。</summary>
    Private Sub DrawLineItem(e As DrawItemEventArgs, isArrow As Boolean, atStart As Boolean)
        e.DrawBackground()
        If e.Index < 0 Then Return
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim fore = If((e.State And DrawItemState.Selected) <> 0, SystemColors.HighlightText, ThemeManager.Fore(Color.FromArgb(40, 44, 52)))
        Dim r = e.Bounds
        Dim cy = r.Y + r.Height / 2.0F
        Using pen As New Pen(fore, 2), br As New SolidBrush(fore)
            If Not isArrow Then
                Dim pattern = {Nothing, New Single() {3, 2}, New Single() {0.5F, 1.6F}, New Single() {3, 1.6F, 0.5F, 1.6F}, New Single() {7, 2.6F}}(e.Index)
                If pattern IsNot Nothing Then pen.DashPattern = pattern
                g.DrawLine(pen, r.X + 6, cy, r.X + 60, cy)
                TextRenderer.DrawText(g, DashNames(e.Index), e.Font, New Point(r.X + 68, r.Y + 1), fore)
            Else
                Dim x0 = r.X + 6, x1 = r.X + 34
                Dim tip = If(atStart, x0, x1), dir = If(atStart, -1, 1)
                g.DrawLine(pen, If(atStart, x0 + If(e.Index = 1, 5, 0), x0), cy, If(atStart, x1, x1 - If(e.Index = 1, 5, 0)), cy)
                Select Case CType(e.Index, ArrowKind)
                    Case ArrowKind.Triangle : g.FillPolygon(br, {New PointF(tip, cy), New PointF(tip - dir * 8, cy - 5), New PointF(tip - dir * 8, cy + 5)})
                    Case ArrowKind.Open : g.DrawLines(pen, {New PointF(tip - dir * 7, cy - 5), New PointF(tip, cy), New PointF(tip - dir * 7, cy + 5)})
                    Case ArrowKind.Circle : g.FillEllipse(br, tip - 4, cy - 4, 8, 8)
                    Case ArrowKind.Square : g.FillRectangle(br, tip - 4, cy - 4, 8, 8)
                    Case ArrowKind.Diamond : g.FillPolygon(br, {New PointF(tip, cy), New PointF(tip - dir * 5, cy - 4), New PointF(tip - dir * 10, cy), New PointF(tip - dir * 5, cy + 4)})
                End Select
                TextRenderer.DrawText(g, ArrowNames(e.Index), e.Font, New Point(r.X + 42, r.Y + 1), fore)
            End If
        End Using
    End Sub

    Private Sub SyncLineStyle(st As DrawLayer)
        _lineDash.SelectedIndex = CInt(st.Dash)
        _lineCap.SelectedIndex = CInt(st.Cap)
        _lineJoin.SelectedIndex = CInt(st.Join)
        _arrowStart.SelectedIndex = CInt(st.StartArrow)
        _arrowEnd.SelectedIndex = CInt(st.EndArrow)
    End Sub

    '---------------------------------------------------------------------
    ' 上方屬性列：位置、大小、角度
    '---------------------------------------------------------------------

    Private Function BuildPropBar() As Control
        For Each f In {("x", "X"), ("y", "Y"), ("w", "寬"), ("lock", ""), ("h", "高"), ("angle", "∠")}
            If f.Item1 = "lock" Then
                _propLock.FlatAppearance.BorderSize = 0
                _propLock.FlatAppearance.CheckedBackColor = Color.Transparent
                AddHandler _propLock.Paint, Sub(s, e) PaintLockGlyph(e.Graphics, _propLock)
                AddHandler _propLock.CheckedChanged, Sub() _propLock.Invalidate()
                _help.SetHelp("draw.prop.lock", _propLock)
                _propBar.Controls.Add(_propLock)
                Continue For
            End If
            Dim key = f.Item1
            Dim lbl As New Label With {.Text = f.Item2, .AutoSize = True, .Margin = New Padding(3, 7, 0, 0), .BackColor = Color.Transparent}
            Dim tb As New TextBox With {.Width = If(key = "angle", 38, 44), .Margin = New Padding(0, 3, 1, 0), .TextAlign = HorizontalAlignment.Right}
            _help.SetHelp("draw.prop." & key, tb)
            AddHandler tb.KeyDown, Sub(s, e) PropKeyDown(key, tb, e)
            AddHandler tb.Leave, Sub() ApplyProp(key, tb)
            _propBoxes(key) = tb
            _propBar.Controls.Add(lbl)
            _propBar.Controls.Add(tb)
        Next
        _propBar.Visible = False
        Return _propBar
    End Function

    ''' <summary>鎖定比例：按下時畫連在一起的鏈條（藍色），放開時畫斷開的。</summary>
    Private Shared Sub PaintLockGlyph(g As Graphics, cb As CheckBox)
        ' 背景：往上找第一個不透明的底色（屬性列本身是透明的）
        Dim back = SystemColors.Control
        Dim c As Control = cb.Parent
        While c IsNot Nothing
            If c.BackColor.A = 255 Then back = c.BackColor : Exit While
            c = c.Parent
        End While
        g.Clear(back)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim col = If(cb.Checked, Color.FromArgb(24, 95, 165), Color.FromArgb(140, 146, 156))
        Dim cx = cb.Width / 2.0F, cy = cb.Height / 2.0F
        Using pen As New Pen(col, 1.6F)
            Dim gap = If(cb.Checked, 0.0F, 2.5F)
            g.DrawArc(pen, cx - 3, cy - 9 - gap, 6, 8, 180, 180)
            g.DrawLine(pen, cx - 3, cy - 5 - gap, cx - 3, cy - 2 - gap)
            g.DrawLine(pen, cx + 3, cy - 5 - gap, cx + 3, cy - 2 - gap)
            g.DrawArc(pen, cx - 3, cy + 1 + gap, 6, 8, 0, 180)
            g.DrawLine(pen, cx - 3, cy + 2 + gap, cx - 3, cy + 5 + gap)
            g.DrawLine(pen, cx + 3, cy + 2 + gap, cx + 3, cy + 5 + gap)
            If cb.Checked Then g.DrawLine(pen, cx, cy - 3, cx, cy + 3)
        End Using
    End Sub

    Private Sub PropKeyDown(key As String, tb As TextBox, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            ApplyProp(key, tb)
            tb.SelectAll()
            e.SuppressKeyPress = True
        ElseIf e.KeyCode = Keys.Escape Then
            UpdatePropBar(force:=True)
            _canvas.Focus()
            e.SuppressKeyPress = True
        ElseIf e.KeyCode = Keys.Up OrElse e.KeyCode = Keys.Down Then
            Dim v As Double
            If Not Double.TryParse(tb.Text.TrimEnd("°"c), v) Then Return
            v += If(e.KeyCode = Keys.Up, 1, -1) * If(e.Shift, 10, 1)
            tb.Text = v.ToString("0")
            ApplyProp(key, tb)
            e.SuppressKeyPress = True
        End If
    End Sub

    ''' <summary>屬性列跟著選取：選取工具選了東西時顯示（提示文字讓出位置）。</summary>
    Private Sub UpdatePropBar(Optional force As Boolean = False)
        If _propBoxes.Count = 0 Then Return
        Dim idx = If(_drawStrip.SelectedTool = DrawIcons.SelectTool AndAlso Not _builderOn AndAlso _photo IsNot Nothing,
                     SelectedIndices().Where(Function(i) _recipe.Drawings(i).Shape <> DrawShape.FillLayer).ToList(), New List(Of Integer)())
        Dim b = If(idx.Count > 0, DrawGeometry.GroupBounds(idx.Select(Function(i) _recipe.Drawings(i))), RectangleF.Empty)
        Dim show = idx.Count > 0 AndAlso Not b.IsEmpty
        _propBar.Visible = show
        _drawHint.Visible = Not show
        If Not show Then Return
        Dim px = CDbl(PhotoPixelSize().Height)
        Dim single1 = If(idx.Count = 1, _recipe.Drawings(idx(0)), Nothing)
        Dim angle = If(single1 IsNot Nothing AndAlso HasOwnRotation(single1), single1.Rotation, 0.0)
        _propSyncing = True
        Try
            For Each kv In {("x", b.X * px), ("y", b.Y * px), ("w", b.Width * px), ("h", b.Height * px), ("angle", angle)}
                Dim tb = _propBoxes(kv.Item1)
                If tb.Focused AndAlso Not force Then Continue For
                tb.Text = If(kv.Item1 = "angle", Math.Round(kv.Item2, 1).ToString("0.#") & "°", Math.Round(kv.Item2).ToString("0"))
            Next
        Finally
            _propSyncing = False
        End Try
    End Sub

    ''' <summary>方框類、文字、圖說有自己的角度（屬性列顯示角度）；其他輸入的角度是「再轉幾度」。</summary>
    Private Shared Function HasOwnRotation(d As DrawLayer) As Boolean
        Return DrawGeometry.IsBoxLayer(d)
    End Function

    Private Sub ApplyProp(key As String, tb As TextBox)
        If _propSyncing Then Return
        Dim v As Double
        If Not Double.TryParse(tb.Text.Trim().TrimEnd("°"c), v) Then UpdatePropBar(force:=True) : Return
        Dim idx = SelectedIndices().Where(Function(i) Not _recipe.Drawings(i).Locked AndAlso _recipe.Drawings(i).Shape <> DrawShape.FillLayer).ToList()
        If idx.Count = 0 Then Return
        Dim px = CDbl(PhotoPixelSize().Height)
        Dim starts = idx.Select(Function(i) _recipe.Drawings(i).Clone()).ToList()
        Dim b = DrawGeometry.GroupBounds(starts)
        If b.IsEmpty Then Return
        Dim u = v / px
        Dim change As Action(Of DrawLayer, DrawLayer) = Nothing
        Select Case key
            Case "x"
                Dim dx = u - b.X
                If Math.Abs(dx) < 0.0000001 Then Return
                change = Sub(d, s) DrawGeometry.Offset(d, dx, 0)
            Case "y"
                Dim dy = u - b.Y
                If Math.Abs(dy) < 0.0000001 Then Return
                change = Sub(d, s) DrawGeometry.Offset(d, 0, dy)
            Case "w", "h"
                If u <= 0.0005 Then UpdatePropBar(force:=True) : Return
                Dim nw = If(key = "w", u, If(_propLock.Checked, b.Width * u / b.Height, b.Width))
                Dim nh = If(key = "h", u, If(_propLock.Checked, b.Height * u / b.Width, b.Height))
                If Math.Abs(nw - b.Width) < 0.0000001 AndAlso Math.Abs(nh - b.Height) < 0.0000001 Then Return
                Dim nb = New RectangleF(b.X, b.Y, CSng(nw), CSng(nh))
                change = Sub(d, s) DrawGeometry.ScaleInGroup(d, s, b, nb)
            Case "angle"
                Dim c = New PointF(b.X + b.Width / 2, b.Y + b.Height / 2)
                If idx.Count = 1 AndAlso HasOwnRotation(starts(0)) Then
                    Dim target = v
                    change = Sub(d, s) d.Rotation = Math.Round(target, 1)
                Else
                    Dim delta = v
                    If Math.Abs(delta) < 0.001 Then Return
                    change = Sub(d, s) DrawGeometry.RotateInGroup(d, s, c, delta)
                End If
        End Select
        Dim results = idx.Select(Function(i, k)
                                     Dim d = starts(k).Clone()
                                     change(d, starts(k))
                                     Return (i, d)
                                 End Function).ToList()
        ApplyChange(Sub(r)
                        For Each x In results
                            r.Drawings(x.i) = x.d
                        Next
                    End Sub)
        UpdateDrawControls()
        UpdatePropBar(force:=True)
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' PowerClip、照片放進形狀
    '---------------------------------------------------------------------

    ''' <summary>可以「放進形狀」：選了兩個以上的單位，最上面那個是封閉的向量圖形（框），其他是內容。回傳（框, 內容）。</summary>
    Private Function ClipCandidate() As (Frame As DrawLayer, Contents As List(Of DrawLayer))?
        Dim units = SelectionUnits()
        If units.Count < 2 Then Return Nothing
        Dim top = units.OrderBy(Function(u) u.Max()).Last()
        If top.Count <> 1 Then Return Nothing
        Dim frame = _recipe.Drawings(top(0))
        If Not PathBoolean.CanCombine(frame) Then Return Nothing
        Dim contents = units.Where(Function(u) u IsNot top).SelectMany(Function(u) u).Select(Function(i) _recipe.Drawings(i)).ToList()
        Return (frame, contents)
    End Function

    ''' <summary>選取裡整個被選到的 PowerClip 群組。</summary>
    Private Function SelectedClipGroups() As List(Of String)
        Return SelectedTopGroups().Where(Function(g) DrawGroups.Find(_recipe, g)?.ClipFrame IsNot Nothing).ToList()
    End Function

    ''' <summary>可以「照片放進形狀」：只選一個封閉的向量圖形。</summary>
    Private Function PhotoClipCandidate() As DrawLayer
        Dim idx = SelectedIndices()
        If idx.Count <> 1 Then Return Nothing
        Dim d = _recipe.Drawings(idx(0))
        Return If(PathBoolean.CanCombine(d), d, Nothing)
    End Function

    Private Sub ClipIntoShape()
        Dim c = ClipCandidate()
        If Not c.HasValue Then
            SetStatusMessage("放進形狀：選要放進去的東西，加上最上面的一個封閉圖形（當框）。")
            Return
        End If
        Dim frame = c.Value.Frame, contents = c.Value.Contents
        Dim entered = EnteredGroup()
        Dim name = If(String.IsNullOrEmpty(frame.Name), "PowerClip", frame.Name & "（PowerClip）")
        ApplyChange(Sub(r) DrawGroups.CreateClip(r, frame, contents, entered, name))
        Dim idx = {frame}.Concat(contents).Select(Function(d) _recipe.Drawings.IndexOf(d)).Where(Function(i) i >= 0).ToList()
        SetMultiSelection(idx, idx.Max())
        SetStatusMessage($"已放進「{frame.Name}」：移動、縮放框時內容跟著動；雙擊進去可以單獨移動內容，「取出內容」還原。")
        UpdateDrawControls()
    End Sub

    Private Sub ClipOut()
        Dim groups = SelectedClipGroups()
        If groups.Count = 0 Then Return
        Dim sel = SelectedIndices().Select(Function(i) _recipe.Drawings(i)).ToList()
        ApplyChange(Sub(r)
                        For Each g In groups
                            DrawGroups.Ungroup(r, g)
                        Next
                        DrawGroups.Prune(r, EnteredGroup())
                    End Sub)
        Dim idx = sel.Select(Function(d) _recipe.Drawings.IndexOf(d)).Where(Function(i) i >= 0).ToList()
        SetMultiSelection(idx, If(idx.Count > 0, idx.Max(), -1))
        SetStatusMessage("已取出內容：框和內容變回各自的圖層。")
        UpdateDrawControls()
    End Sub

    ''' <summary>照片放進形狀（再按一次取消）：形狀外面的照片變透明，存 PNG 就是去背。</summary>
    Private Sub TogglePhotoClip()
        Dim d = PhotoClipCandidate()
        If d Is Nothing Then Return
        Dim index = _recipe.Drawings.IndexOf(d)
        Dim turnOff = d.Id IsNot Nothing AndAlso _recipe.PhotoClip = d.Id
        ApplyChange(Sub(r)
                        If turnOff Then
                            r.PhotoClip = Nothing
                        Else
                            If r.Drawings(index).Id Is Nothing Then r.Drawings(index).Id = Guid.NewGuid().ToString("N")
                            r.PhotoClip = r.Drawings(index).Id
                        End If
                    End Sub)
        UpdateCutoutControls() ' 透明的地方顯示棋盤格
        SetStatusMessage(If(turnOff, "已取消照片放進形狀。", $"照片只留「{d.Name}」裡面，外面變透明（匯出時預設存 PNG）；再按一次取消。"))
        UpdateDrawControls()
    End Sub

    ''' <summary>文字、圖說轉成鋼筆路徑：每個字一個（圖說的框也是），兩個以上時組成群組。</summary>
    Private Sub ConvertTextToPaths(index As Integer)
        Dim src = _recipe.Drawings(index)
        Dim outs = DrawGeometry.TextOutlines(src)
        If outs.Count = 0 Then
            SetStatusMessage("沒有文字可以轉。")
            Return
        End If
        Dim entered = EnteredGroup()
        Dim name = If(String.IsNullOrEmpty(src.Name), "文字", src.Name) & "（外框）"
        ApplyChange(Sub(r)
                        Dim keepId = r.Drawings(index).Id
                        r.Drawings.RemoveAt(index)
                        InsertPieces(r, index, outs, keepId)
                        If outs.Count >= 2 Then DrawGroups.Create(r, outs, If(src.GroupId, entered), name)
                    End Sub)
        Dim idx = outs.Select(Function(o) _recipe.Drawings.IndexOf(o)).Where(Function(i) i >= 0).ToList()
        If idx.Count >= 2 Then SetMultiSelection(idx, idx.Max()) Else SelectDrawLayer(idx.FirstOrDefault(-1))
        SetStatusMessage("已轉成外框：每個字都是鋼筆路徑（不能再改字）；可以改節點、焊接、放進形狀當框。")
        UpdateDrawControls()
    End Sub
End Class
