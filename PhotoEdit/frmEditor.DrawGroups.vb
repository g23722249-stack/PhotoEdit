Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 繪圖分頁的群組（同 CorelDRAW、Illustrator）：
''' 選取多個物件按 Ctrl+G（或上方「群組」）組成群組，Ctrl+Shift+G 解散；畫面上點群組裡的物件會選到整個群組，
''' 雙擊進入群組（上方顯示「群組 › 子群組」，可以選裡面的物件、畫的新圖形放進這個群組），Esc 離開；Ctrl＋點直接選群組裡的物件。
''' 圖層清單是樹狀：群組可以收合、隱藏、鎖定、改名，拖曳列可以移進移出群組。資料與規則見 DrawGroups。
''' </summary>
Partial Friend Class frmEditor

    ''' <summary>正在編輯的群組（雙擊進入）；Nothing＝最外層。</summary>
    Private _enteredGroup As String
    Private _layerRows As New List(Of DrawTreeRow)()
    Private ReadOnly _groupBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                            .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0)}
    Private _btnGroup As Button
    Private _btnUngroup As Button
    Private _btnExitGroup As Button
    Private ReadOnly _crumb As New Label With {.AutoSize = True, .BackColor = Color.Transparent, .Margin = New Padding(8, 7, 2, 0)}
    Private ReadOnly _addMenu As New ContextMenuStrip()
    Private ReadOnly _layerMenu As New ContextMenuStrip()
    ' 清單的點選與拖曳
    Private _listAnchorRow As Integer = -1
    Private _listDownRow As Integer = -1
    Private _listDownPoint As Point
    Private _listDragging As Boolean
    Private _listDropRow As Integer = -1

    Private Const IndentPx As Integer = 14

    '---------------------------------------------------------------------
    ' 版面
    '---------------------------------------------------------------------

    ''' <summary>上方操作列裡的「群組、解散群組、目前所在的群組、離開群組」。</summary>
    Private Function BuildGroupBar() As Control
        _btnGroup = MakeButton(Lang.T("群組"), "draw.group")
        _btnUngroup = MakeButton(Lang.T("解散群組"), "draw.ungroup")
        _btnExitGroup = MakeButton(Lang.T("離開群組"), "draw.exitgroup")
        For Each b In {_btnGroup, _btnUngroup, _btnExitGroup}
            b.Height = 28
            b.Width = If(b Is _btnGroup, 64, 92)
            b.Margin = New Padding(0, 0, 4, 0)
            b.Visible = False
        Next
        AddHandler _btnGroup.Click, Sub() GroupSelected()
        AddHandler _btnUngroup.Click, Sub() UngroupSelected()
        AddHandler _btnExitGroup.Click, Sub() ExitGroup()
        _crumb.Visible = False
        _crumb.ForeColor = Color.FromArgb(83, 74, 183)
        _groupBar.Controls.AddRange({_btnGroup, _btnUngroup, _crumb, _btnExitGroup})

        _addMenu.Items.Add(Lang.T("新增點陣圖層"), Nothing, Sub() LayerCommand(0))
        _addMenu.Items.Add(Lang.T("新增群組"), Nothing, Sub() NewEmptyGroup())
        _layerMenu.Items.Add(Lang.T("群組（Ctrl+G）"), Nothing, Sub() GroupSelected())
        _layerMenu.Items.Add(Lang.T("解散群組（Ctrl+Shift+G）"), Nothing, Sub() UngroupSelected())
        _layerMenu.Items.Add("-")
        _layerMenu.Items.Add(Lang.T("移到最上層"), Nothing, Sub() MoveSelectedItem(True, True))
        _layerMenu.Items.Add(Lang.T("上移"), Nothing, Sub() MoveSelectedItem(True, False))
        _layerMenu.Items.Add(Lang.T("下移"), Nothing, Sub() MoveSelectedItem(False, False))
        _layerMenu.Items.Add(Lang.T("移到最下層"), Nothing, Sub() MoveSelectedItem(False, True))
        _layerMenu.Items.Add("-")
        _layerMenu.Items.Add(Lang.T("重新命名"), Nothing, Sub() RenameDrawLayer())
        _layerMenu.Items.Add(Lang.T("刪除"), Nothing, Sub() DeleteSelectedLayers())
        AddHandler _layerMenu.Opening, Sub()
                                           _layerMenu.Items(0).Enabled = CanGroup()
                                           _layerMenu.Items(1).Enabled = SelectedTopGroups().Count > 0
                                           For i = 3 To 6
                                               _layerMenu.Items(i).Enabled = MovableBlock() IsNot Nothing
                                           Next
                                       End Sub
        Return _groupBar
    End Function

    ''' <summary>正在編輯的群組（不存在了就回到最外層）。</summary>
    Private Function EnteredGroup() As String
        If _enteredGroup IsNot Nothing AndAlso DrawGroups.Find(_recipe, _enteredGroup) Is Nothing Then _enteredGroup = Nothing
        Return _enteredGroup
    End Function

    ''' <summary>操作列上的群組按鈕、所在位置跟著選取更新。</summary>
    Private Sub UpdateGroupBar()
        If _btnGroup Is Nothing Then Return
        Dim picking = _drawStrip.SelectedTool = DrawIcons.SelectTool
        _btnGroup.Visible = picking AndAlso CanGroup()
        _btnUngroup.Visible = picking AndAlso SelectedTopGroups().Count > 0
        Dim entered = EnteredGroup()
        _crumb.Visible = entered IsNot Nothing
        _btnExitGroup.Visible = entered IsNot Nothing
        If entered IsNot Nothing Then
            Dim chain = DrawGroups.ChainFromGroup(_recipe, entered)
            chain.Reverse()
            _crumb.Text = Lang.T("群組：") & String.Join(" › ", chain.Select(Function(id) GroupName(DrawGroups.Find(_recipe, id))))
        End If
    End Sub

    Private Shared Function GroupName(g As DrawGroup) As String
        Return If(g Is Nothing, "", If(String.IsNullOrEmpty(g.Name), Lang.T("群組"), g.Name))
    End Function

    '---------------------------------------------------------------------
    ' 選取與群組的關係
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 畫面上點選、框選的單位：在目前所在的那一層，群組裡的圖層換成整個群組的成員；不在所在群組裡的圖層拿掉。
    ''' </summary>
    Private Function ExpandToGroups(indices As IEnumerable(Of Integer)) As List(Of Integer)
        Dim entered = EnteredGroup()
        Dim result As New SortedSet(Of Integer)()
        For Each i In indices
            Dim d = _recipe.Drawings(i)
            If Not DrawGroups.IsInside(_recipe, d, entered) Then Continue For
            Dim g = DrawGroups.ChildGroupAt(_recipe, d, entered)
            If g Is Nothing Then
                result.Add(i)
            Else
                For Each m In DrawGroups.MemberIndices(_recipe, g)
                    result.Add(m)
                Next
            End If
        Next
        Return result.ToList()
    End Function

    ''' <summary>整個被選到的群組（在目前所在的那一層）。</summary>
    Private Function SelectedTopGroups() As List(Of String)
        Dim sel = New HashSet(Of Integer)(SelectedIndices())
        If sel.Count = 0 OrElse _recipe.DrawGroups Is Nothing Then Return New List(Of String)()
        Dim entered = EnteredGroup()
        Return sel.Select(Function(i) DrawGroups.ChildGroupAt(_recipe, _recipe.Drawings(i), entered)).
                   Where(Function(g) g IsNot Nothing).Distinct().
                   Where(Function(g) DrawGroups.MemberIndices(_recipe, g).All(Function(m) sel.Contains(m))).ToList()
    End Function

    ''' <summary>可以組成群組：選到兩個以上的「項目」（群組算一個）。</summary>
    Private Function CanGroup() As Boolean
        Dim sel = SelectedIndices()
        If sel.Count < 2 Then Return False
        Dim entered = EnteredGroup()
        Dim items = sel.Where(Function(i) DrawGroups.IsInside(_recipe, _recipe.Drawings(i), entered)).
                        Select(Function(i) If(DrawGroups.ChildGroupAt(_recipe, _recipe.Drawings(i), entered), "L" & i)).Distinct().Count()
        Return items >= 2
    End Function

    Private Sub GroupSelected()
        If Not CanGroup() Then Return
        Dim layers = SelectedIndices().Select(Function(i) _recipe.Drawings(i)).ToList()
        Dim entered = EnteredGroup()
        Dim name = Lang.T("群組 ") & (If(_recipe.DrawGroups?.Count, 0) + 1)
        ApplyChange(Sub(r)
                        Dim mine = layers
                        DrawGroups.Create(r, mine, entered, name)
                    End Sub)
        Dim idx = layers.Select(Function(d) _recipe.Drawings.IndexOf(d)).ToList()
        SetMultiSelection(idx, idx.Max())
        SetStatusMessage(Lang.F("已組成「{0}」：點一下選整個群組，雙擊進去編輯裡面的物件；Ctrl+Shift+G 解散。", name))
    End Sub

    Private Sub UngroupSelected()
        Dim groups = SelectedTopGroups()
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
    End Sub

    ''' <summary>進入群組：之後點選的是裡面的物件，畫的新圖形放進這個群組。</summary>
    Private Sub EnterGroup(groupId As String)
        _enteredGroup = groupId
        SelectDrawLayer(-1)
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    ''' <summary>離開群組（回到上一層），並選取剛剛那個群組。</summary>
    Private Sub ExitGroup()
        Dim g = DrawGroups.Find(_recipe, EnteredGroup())
        If g Is Nothing Then Return
        _enteredGroup = g.ParentId
        DrawGroups.Prune(_recipe, _enteredGroup) ' 空的群組（新增後沒畫東西）離開時刪掉；不算一步修改
        Dim m = DrawGroups.MemberIndices(_recipe, g.Id)
        If m.Count > 0 Then SetMultiSelection(m, m.Max()) Else SelectDrawLayer(-1)
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    ''' <summary>新增空的群組並進入：接著畫的圖形都放進去。</summary>
    Private Sub NewEmptyGroup()
        If _photo Is Nothing Then Return
        Dim parent = EnteredGroup()
        Dim name = Lang.T("群組 ") & (If(_recipe.DrawGroups?.Count, 0) + 1)
        Dim created As DrawGroup = Nothing
        ApplyChange(Sub(r) created = DrawGroups.CreateEmpty(r, parent, name))
        If _drawStrip.SelectedTool = DrawIcons.SelectTool Then _drawStrip.SelectedTool = CInt(DrawShape.Rectangle)
        EnterGroup(created.Id)
        SetStatusMessage(Lang.F("已新增「{0}」並進入：接著畫的圖形都會放在這個群組裡；Esc 或「離開群組」回到上一層。", name))
    End Sub

    ''' <summary>新圖層加進正在編輯的群組：放在群組最上面的成員正上方（群組的成員保持相鄰）。</summary>
    Private Sub PlaceInEnteredGroup(r As EditRecipe, d As DrawLayer)
        Dim entered = EnteredGroup()
        If entered Is Nothing Then Return
        d.GroupId = entered
        Dim members = DrawGroups.MemberIndices(r, entered).Where(Function(i) r.Drawings(i) IsNot d).ToList()
        If members.Count > 0 Then LayerStack.PlaceAbove(r, d, r.Drawings(members.Max()))
    End Sub

    '---------------------------------------------------------------------
    ' 上下移動（同一層裡，群組整塊一起）
    '---------------------------------------------------------------------

    ''' <summary>可以上下移動的那一塊：一個圖層，或整個被選到的群組；其他情形 Nothing。回傳（成員索引, 所在的上一層）。</summary>
    Private Function MovableBlock() As (Indices As List(Of Integer), Parent As String)?
        Dim sel = SelectedIndices()
        If sel.Count = 0 Then Return Nothing
        If sel.Count = 1 Then Return (sel, _recipe.Drawings(sel(0)).GroupId)
        Dim groups = SelectedTopGroups()
        If groups.Count <> 1 Then Return Nothing
        Dim m = DrawGroups.MemberIndices(_recipe, groups(0))
        If m.Count <> sel.Count Then Return Nothing
        Return (m, DrawGroups.Find(_recipe, groups(0)).ParentId)
    End Function

    ''' <summary>上移／下移一格（toEnd＝移到這一層的最上面／最下面），不會移出所在的群組。</summary>
    Private Sub MoveSelectedItem(up As Boolean, toEnd As Boolean)
        Dim blk = MovableBlock()
        If Not blk.HasValue Then Return
        Dim layers = blk.Value.Indices.Select(Function(i) _recipe.Drawings(i)).ToList()
        Dim parent = blk.Value.Parent
        Dim moved = False
        ApplyChange(Sub(r)
                        Do
                            Dim cur = layers.Select(Function(d) r.Drawings.IndexOf(d)).ToList() ' r 就是 _recipe（ApplyChange 直接改）
                            If Not StepBlock(r, cur, parent, up) Then Exit Do
                            moved = True
                        Loop While toEnd
                    End Sub)
        If moved Then
            Dim idx = layers.Select(Function(d) _recipe.Drawings.IndexOf(d)).ToList()
            SetMultiSelection(idx, idx.Max())
        End If
    End Sub

    ''' <summary>把 block（同一層裡相鄰的一塊）和上面／下面那一個同層項目交換位置；到頂了回傳 False。</summary>
    Private Shared Function StepBlock(r As EditRecipe, block As List(Of Integer), parent As String, up As Boolean) As Boolean
        Dim n = r.Drawings.Count
        Dim nb = If(up, block.Max() + 1, block.Min() - 1)
        If nb < 0 OrElse nb >= n Then Return False
        Dim neighbor = r.Drawings(nb)
        If Not DrawGroups.IsInside(r, neighbor, parent) Then Return False
        Dim sg = DrawGroups.ChildGroupAt(r, neighbor, parent)
        Dim sibling = If(sg Is Nothing, New List(Of Integer) From {nb}, DrawGroups.MemberIndices(r, sg))
        Dim movingSet = New HashSet(Of Object)(block.Select(Function(i) CObj(r.Drawings(i))))
        Dim order = LayerStack.Order(r)
        Dim moving = order.Where(Function(x) movingSet.Contains(x.Item)).ToList()
        order.RemoveAll(Function(x) movingSet.Contains(x.Item))
        If up Then
            Dim anchor = r.Drawings(sibling.Max())
            Dim a = order.FindIndex(Function(x) x.Item Is anchor)
            order.InsertRange(a + 1, moving)
        Else
            Dim anchor = r.Drawings(sibling.Min())
            Dim a = order.FindIndex(Function(x) x.Item Is anchor)
            order.InsertRange(a, moving)
        End If
        LayerStack.SetOrder(r, order)
        Return True
    End Function

    '---------------------------------------------------------------------
    ' 樹狀圖層清單
    '---------------------------------------------------------------------

    ''' <summary>重新排出清單的列（配方或收合改變後）。</summary>
    Private Sub RefreshLayerRows()
        _layerRows = DrawGroups.Rows(_recipe)
        If _layerList.Items.Count <> _layerRows.Count Then
            _layerList.BeginUpdate()
            _layerList.Items.Clear()
            For i = 0 To _layerRows.Count - 1
                _layerList.Items.Add(i)
            Next
            _layerList.EndUpdate()
        End If
        _layerList.Invalidate()
    End Sub

    ''' <summary>這一列代表的圖層：圖層列一個，群組列是全部成員。</summary>
    Private Function RowIndices(row As Integer) As List(Of Integer)
        If row < 0 OrElse row >= _layerRows.Count Then Return New List(Of Integer)()
        Dim lr = _layerRows(row)
        If lr.Group IsNot Nothing Then Return DrawGroups.MemberIndices(_recipe, lr.Group.Id)
        Return If(lr.LayerIndex < LayerCount(), New List(Of Integer) From {lr.LayerIndex}, New List(Of Integer)())
    End Function

    Private Function RowSelected(row As Integer) As Boolean
        Dim idx = RowIndices(row)
        If idx.Count = 0 Then Return _layerRows(row).Group IsNot Nothing AndAlso _layerRows(row).Group.Id = EnteredGroup()
        Dim sel = New HashSet(Of Integer)(SelectedIndices())
        Return idx.All(Function(i) sel.Contains(i))
    End Function

    Private Sub LayerTree_DrawItem(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 OrElse e.Index >= _layerRows.Count Then Return
        Dim row = _layerRows(e.Index)
        Dim g = e.Graphics
        Dim r = e.Bounds
        Dim selected = RowSelected(e.Index)
        Using bg As New SolidBrush(ThemeManager.Back(If(selected, Color.FromArgb(210, 228, 250), Color.White)))
            g.FillRectangle(bg, r)
        End Using
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim x0 = r.X + 48 + row.Depth * IndentPx
        If row.Group IsNot Nothing Then
            Dim grp = row.Group
            Dim fg = ThemeManager.Fore(If(grp.Visible, Color.FromArgb(40, 44, 52), Color.FromArgb(160, 165, 172)))
            DrawEye(g, New RectangleF(r.X + 4, r.Y + 6, 18, 16), grp.Visible)
            DrawLock(g, New RectangleF(r.X + 26, r.Y + 6, 16, 16), grp.Locked)
            ' 收合箭頭
            Using br As New SolidBrush(fg)
                Dim cx = x0 + 6, cy = r.Y + r.Height / 2.0F
                Dim tri = If(grp.Collapsed, {New PointF(cx - 3, cy - 5), New PointF(cx + 3, cy), New PointF(cx - 3, cy + 5)},
                                            {New PointF(cx - 5, cy - 3), New PointF(cx + 5, cy - 3), New PointF(cx, cy + 3)})
                g.FillPolygon(br, tri)
            End Using
            ' 資料夾
            Using pen As New Pen(Color.FromArgb(127, 119, 221), 1.4F)
                Dim fx = x0 + 16, fy = r.Y + 8
                g.DrawLines(pen, {New PointF(fx, fy + 12), New PointF(fx, fy + 1), New PointF(fx + 6, fy + 1), New PointF(fx + 8, fy + 3), New PointF(fx + 18, fy + 3), New PointF(fx + 18, fy + 12), New PointF(fx, fy + 12)})
            End Using
            Dim n = DrawGroups.MemberIndices(_recipe, grp.Id).Count
            Dim label = GroupName(grp) & If(grp.Id = EnteredGroup(), Lang.T("（編輯中）"), "")
            TextRenderer.DrawText(g, label, _layerList.Font, New Rectangle(x0 + 40, r.Y, r.Right - x0 - 40 - 46, r.Height), fg,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
            TextRenderer.DrawText(g, n & Lang.T(" 個"), _layerList.Font, New Rectangle(r.Right - 46, r.Y, 42, r.Height), ThemeManager.Fore(Color.FromArgb(130, 136, 146)),
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.Right)
        ElseIf row.LayerIndex < LayerCount() Then
            Dim d = _recipe.Drawings(row.LayerIndex)
            Dim fg = ThemeManager.Fore(If(d.Visible, Color.FromArgb(40, 44, 52), Color.FromArgb(160, 165, 172)))
            DrawEye(g, New RectangleF(r.X + 4, r.Y + 6, 18, 16), d.Visible)
            DrawLock(g, New RectangleF(r.X + 26, r.Y + 6, 16, 16), d.Locked)
            DrawIcons.DrawTool(g, CInt(d.Shape), New RectangleF(x0, r.Y + 4, 20, 20), fg)
            Dim name = If(String.IsNullOrEmpty(d.Name), DrawGeometry.ShapeNames(CInt(d.Shape)), d.Name)
            TextRenderer.DrawText(g, name, _layerList.Font, New Rectangle(x0 + 26, r.Y, r.Right - x0 - 26 - 46, r.Height), fg,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
            TextRenderer.DrawText(g, d.Opacity & "%", _layerList.Font, New Rectangle(r.Right - 46, r.Y, 42, r.Height), ThemeManager.Fore(Color.FromArgb(130, 136, 146)),
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.Right)
        End If
        ' 拖曳的目標：群組列＝放進去（整列框起來），圖層列＝放在它上面（上緣一條線）
        If _listDragging AndAlso e.Index = _listDropRow Then
            Using pen As New Pen(Color.FromArgb(24, 95, 165), 2)
                If row.Group IsNot Nothing Then
                    g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3)
                Else
                    g.DrawLine(pen, x0, r.Top + 1, r.Right, r.Top + 1)
                End If
            End Using
        End If
        Using line As New Pen(ThemeManager.Line(Color.FromArgb(232, 235, 240)))
            g.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1)
        End Using
    End Sub

    ''' <summary>
    ''' 清單按下：眼睛、鎖頭、收合箭頭；其他地方選取（Ctrl 加選／取消、Shift 連續選取；群組列選整個群組），右鍵開選單。
    ''' 按住拖曳可以把這一列移到別的群組（放在群組列上）或別的位置（放在圖層列上＝放在它上面）。
    ''' </summary>
    Private Sub LayerTree_MouseDown(sender As Object, e As MouseEventArgs)
        Dim row = _layerList.IndexFromPoint(e.Location)
        If row < 0 OrElse row >= _layerRows.Count OrElse Not _layerList.GetItemRectangle(row).Contains(e.Location) Then
            If e.Button = MouseButtons.Left Then SelectDrawLayer(-1)
            Return
        End If
        Dim lr = _layerRows(row)
        If e.Button = MouseButtons.Left AndAlso e.X < 46 Then
            Dim eye = e.X < 24
            If lr.Group IsNot Nothing Then
                Dim gid = lr.Group.Id
                If eye Then
                    Dim v = Not lr.Group.Visible
                    ApplyChange(Sub(r) DrawGroups.SetVisible(r, gid, v))
                Else
                    Dim l = Not lr.Group.Locked
                    ApplyChange(Sub(r) DrawGroups.SetLocked(r, gid, l))
                End If
            Else
                Dim index = lr.LayerIndex
                If eye Then
                    ApplyChange(Sub(r)
                                    r.Drawings(index).Visible = Not r.Drawings(index).Visible
                                    r.Drawings(index).HiddenByGroup = False
                                End Sub)
                Else
                    ApplyChange(Sub(r)
                                    r.Drawings(index).Locked = Not r.Drawings(index).Locked
                                    r.Drawings(index).LockedByGroup = False
                                End Sub)
                End If
            End If
            _canvas.Invalidate()
            Return
        End If
        Dim x0 = 48 + lr.Depth * IndentPx
        If e.Button = MouseButtons.Left AndAlso lr.Group IsNot Nothing AndAlso e.X >= x0 AndAlso e.X < x0 + 14 Then
            lr.Group.Collapsed = Not lr.Group.Collapsed ' 只是顯示狀態（不進復原紀錄）
            RefreshLayerRows()
            UpdateLayersPanel() ' 「圖層」分頁的清單一起收合
            Return
        End If
        Dim ctrl = ModifierKeys.HasFlag(Keys.Control), shift = ModifierKeys.HasFlag(Keys.Shift)
        If e.Button = MouseButtons.Right Then
            If Not RowSelected(row) Then SelectRows({row})
            _layerMenu.Show(_layerList, e.Location)
            Return
        End If
        If e.Button <> MouseButtons.Left Then Return
        If shift AndAlso _listAnchorRow >= 0 AndAlso _listAnchorRow < _layerRows.Count Then
            Dim a = Math.Min(_listAnchorRow, row), b = Math.Max(_listAnchorRow, row)
            SelectRows(Enumerable.Range(a, b - a + 1))
        ElseIf ctrl Then
            Dim cur = SelectedIndices()
            Dim idx = RowIndices(row)
            If RowSelected(row) Then
                cur = cur.Except(idx).ToList()
            Else
                cur = cur.Union(idx).ToList()
            End If
            ApplyListSelection(cur)
            _listAnchorRow = row
        Else
            ' 沒有成員的群組（新增群組）：點一下進去
            If lr.Group IsNot Nothing AndAlso RowIndices(row).Count = 0 Then
                EnterGroup(lr.Group.Id)
                Return
            End If
            If Not RowSelected(row) Then SelectRows({row})
            _listAnchorRow = row
        End If
        _listDownRow = row
        _listDownPoint = e.Location
        _listDragging = False
    End Sub

    Private Sub SelectRows(rows As IEnumerable(Of Integer))
        ApplyListSelection(rows.SelectMany(Function(r) RowIndices(r)).Distinct().ToList())
    End Sub

    ''' <summary>清單選取：直接選到那些圖層（不展開成群組）；選了兩個以上時換成選取工具。</summary>
    Private Sub ApplyListSelection(indices As List(Of Integer))
        If indices.Count >= 2 AndAlso _drawStrip.SelectedTool <> DrawIcons.SelectTool Then _drawStrip.SelectedTool = DrawIcons.SelectTool
        If indices.Count = 0 Then
            SelectDrawLayer(-1)
        ElseIf indices.Count = 1 Then
            SelectDrawLayer(indices(0))
        Else
            SetMultiSelection(indices, If(indices.Contains(_drawIndex), _drawIndex, indices.Max()))
        End If
    End Sub

    Private Sub LayerTree_MouseMove(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left OrElse _listDownRow < 0 Then Return
        If Not _listDragging Then
            If Math.Abs(e.Y - _listDownPoint.Y) < 6 Then Return
            _listDragging = True
            _layerList.Capture = True
        End If
        Dim row = _layerList.IndexFromPoint(e.Location)
        If e.Y > _layerList.ClientSize.Height - 4 OrElse row < 0 Then row = -1
        If row <> _listDropRow Then
            _listDropRow = row
            _layerList.Invalidate()
        End If
        _layerList.Cursor = If(row >= 0 AndAlso row <> _listDownRow, Cursors.Hand, Cursors.No)
    End Sub

    Private Sub LayerTree_MouseUp(sender As Object, e As MouseEventArgs)
        Dim dragging = _listDragging, src = _listDownRow, dst = _listDropRow
        _listDragging = False
        _listDownRow = -1
        _listDropRow = -1
        _layerList.Capture = False
        _layerList.Cursor = Cursors.Default
        If dragging AndAlso src >= 0 AndAlso dst >= 0 AndAlso src <> dst Then DropRow(src, dst)
        _layerList.Invalidate()
    End Sub

    ''' <summary>
    ''' 把第 src 列（圖層或群組）移到第 dst 列：dst 是群組＝放進那個群組（最上面）；dst 是圖層＝放在它正上方、和它同一個群組。
    ''' 不能把群組放進它自己裡面。
    ''' </summary>
    Private Sub DropRow(src As Integer, dst As Integer)
        Dim s = _layerRows(src), t = _layerRows(dst)
        Dim movingLayers = RowIndices(src).Select(Function(i) _recipe.Drawings(i)).ToList()
        If movingLayers.Count = 0 Then Return
        Dim newParent As String, anchor As DrawLayer
        If t.Group IsNot Nothing Then
            newParent = t.Group.Id
            Dim m = DrawGroups.MemberIndices(_recipe, t.Group.Id)
            anchor = If(m.Count > 0, _recipe.Drawings(m.Max()), Nothing)
        Else
            anchor = _recipe.Drawings(t.LayerIndex)
            newParent = anchor.GroupId
        End If
        If s.Group IsNot Nothing AndAlso DrawGroups.ChainFromGroup(_recipe, newParent).Contains(s.Group.Id) Then
            SetStatusMessage(Lang.T("不能把群組放進它自己裡面。"))
            Return
        End If
        If anchor IsNot Nothing AndAlso movingLayers.Contains(anchor) Then Return
        Dim srcGroup = s.Group?.Id
        Dim srcLayer = If(s.Group Is Nothing, _recipe.Drawings(s.LayerIndex), Nothing)
        ApplyChange(Sub(r)
                        If srcGroup IsNot Nothing Then
                            DrawGroups.Find(r, srcGroup).ParentId = newParent
                        Else
                            srcLayer.GroupId = newParent
                        End If
                        Dim set1 = New HashSet(Of Object)(movingLayers)
                        Dim order = LayerStack.Order(r)
                        Dim moving = order.Where(Function(x) set1.Contains(x.Item)).ToList()
                        order.RemoveAll(Function(x) set1.Contains(x.Item))
                        Dim a = If(anchor Is Nothing, order.Count - 1, order.FindIndex(Function(x) x.Item Is anchor))
                        order.InsertRange(a + 1, moving)
                        LayerStack.SetOrder(r, order)
                        DrawGroups.Prune(r, EnteredGroup())
                    End Sub)
        Dim idx = movingLayers.Select(Function(d) _recipe.Drawings.IndexOf(d)).Where(Function(i) i >= 0).ToList()
        If idx.Count = 1 Then SelectDrawLayer(idx(0)) Else SetMultiSelection(idx, If(idx.Count > 0, idx.Max(), -1))
    End Sub

    '---------------------------------------------------------------------
    ' 畫面
    '---------------------------------------------------------------------

    ''' <summary>正在編輯的群組：外面一圈紫色虛線、左上角群組名稱。</summary>
    Private Sub PaintEnteredGroup(g As Graphics)
        Dim entered = EnteredGroup()
        If entered Is Nothing Then Return
        Dim m = DrawGroups.MemberIndices(_recipe, entered)
        Dim b = DrawGeometry.GroupBounds(m.Select(Function(i) _recipe.Drawings(i)).Where(Function(d) d.Visible))
        If b.IsEmpty AndAlso b.Width <= 0 Then
            ' 空的群組（剛新增）：照片整個變淡，左上角標出所在的群組
            Dim img0 = _canvas.ImageBounds()
            Using br As New SolidBrush(Color.FromArgb(150, 255, 255, 255))
                g.FillRectangle(br, img0)
            End Using
            Dim label0 = Lang.T("群組：") & GroupName(DrawGroups.Find(_recipe, entered)) & Lang.T("（空的，畫的圖形會放進來）")
            Using f As New Font(_panelFont.FontFamily, 9), bb As New SolidBrush(Color.FromArgb(230, 238, 237, 254)), tb As New SolidBrush(Color.FromArgb(60, 52, 137)),
                  pen As New Pen(Color.FromArgb(127, 119, 221), 1.4F) With {.DashStyle = DashStyle.Dash}
                g.DrawRectangle(pen, img0.X + 4, img0.Y + 4, img0.Width - 8, img0.Height - 8)
                Dim sz = g.MeasureString(label0, f)
                g.FillRectangle(bb, img0.X + 8, img0.Y + 8, sz.Width + 6, sz.Height + 2)
                g.DrawString(label0, f, tb, img0.X + 11, img0.Y + 9)
            End Using
            Return
        End If
        ' 群組以外的東西（照片、其他圖形）蓋一層半透明白色，群組裡的物件挖空（同 Illustrator 的隔離模式）
        Dim img = _canvas.ImageBounds()
        Using veil As New Region(img)
            Dim px = PxPerUnit()
            For Each i In m
                Dim d = _recipe.Drawings(i)
                If Not d.Visible Then Continue For
                Using path As New GraphicsPath()
                    If d.Shape = DrawShape.Raster Then
                        Dim rb = DrawingRenderer.RasterVisibleBounds(d, PhotoAspect())
                        If rb.IsEmpty Then Continue For
                        Dim p0 = UnitToScreen(rb.Location), p1 = UnitToScreen(New PointF(rb.Right, rb.Bottom))
                        path.AddRectangle(RectangleF.FromLTRB(p0.X, p0.Y, p1.X, p1.Y))
                    Else
                        Dim w = CSng(Math.Max(6, d.StrokeWidth * px + 4))
                        For Each fig In DrawGeometry.Figures(d)
                            Dim pts = fig.Points.Select(Function(q) UnitToScreen(q)).ToArray()
                            If pts.Length < 2 Then Continue For
                            Using line As New GraphicsPath(), pen As New Pen(Color.Black, w) With {.LineJoin = LineJoin.Round, .StartCap = LineCap.Round, .EndCap = LineCap.Round}
                                If fig.Closed Then line.AddPolygon(pts) Else line.AddLines(pts)
                                If fig.Closed Then path.AddPolygon(pts)
                                line.Widen(pen) ' 外框的寬度也挖空
                                path.AddPath(line, False)
                            End Using
                        Next
                    End If
                    veil.Exclude(path)
                End Using
            Next
            Using br As New SolidBrush(Color.FromArgb(150, 255, 255, 255))
                g.FillRegion(br, veil)
            End Using
        End Using
        Dim a = UnitToScreen(b.Location), c = UnitToScreen(New PointF(b.Right, b.Bottom))
        Dim rect = RectangleF.FromLTRB(a.X - 8, a.Y - 8, c.X + 8, c.Y + 8)
        Using pen As New Pen(Color.FromArgb(127, 119, 221), 1.4F) With {.DashStyle = DashStyle.Dash}
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height)
        End Using
        Dim label = Lang.T("群組：") & GroupName(DrawGroups.Find(_recipe, entered))
        Using f As New Font(_panelFont.FontFamily, 9), br As New SolidBrush(Color.FromArgb(220, 238, 237, 254)), tb As New SolidBrush(Color.FromArgb(60, 52, 137))
            Dim sz = g.MeasureString(label, f)
            g.FillRectangle(br, rect.X, rect.Y - sz.Height - 2, sz.Width + 6, sz.Height + 2)
            g.DrawString(label, f, tb, rect.X + 3, rect.Y - sz.Height - 1)
        End Using
    End Sub
End Class
