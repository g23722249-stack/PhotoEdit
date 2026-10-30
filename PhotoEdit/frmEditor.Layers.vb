Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 「圖層」分頁：文字、貼圖與繪圖圖層在同一張清單（上面的蓋住下面的），可以任意調整上下順序、
''' 設定混合模式與不透明度、顯示／隱藏、合併向下、合併可見圖層。按兩下（或「編輯」）跳到該圖層的分頁編輯。
''' 照片本身與調色不是圖層，永遠在最底下。
''' </summary>
Partial Friend Class frmEditor

    Private Const TabLayers As Integer = 8

    Private ReadOnly _stackList As New ListBox()
    Private ReadOnly _stackBlend As New ComboBox()
    Private ReadOnly _stackButtons As New List(Of Button)()
    Private _stackTop As Integer
    ''' <summary>選取的圖層在堆疊中的位置（由下而上）；-1 表示沒有選取。</summary>
    Private _stackIndex As Integer = -1
    ''' <summary>切換到「圖層」分頁之前的分頁，用來帶入那邊選取的圖層。</summary>
    Private _lastTab As Integer

    Private Sub BuildLayersPage(page As Aqua.TabPage)
        Dim L = NewLayout(page, autoScroll:=False)
        AddHint(L, Lang.T("文字、貼圖與繪圖圖層都在這裡，上面的蓋住下面的。點眼睛顯示／隱藏、按兩下到該分頁編輯。"))

        _help.SetHelpLinked("layers.blend", _stackBlend, AddCaption(L, Lang.T("混合模式"), L.Y), _stackBlend)
        _stackBlend.DropDownStyle = ComboBoxStyle.DropDownList
        _stackBlend.Items.AddRange(LayerBlend.Names.Cast(Of Object)().ToArray())
        _stackBlend.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _stackBlend.SelectedIndexChanged, Sub()
                                                         If _syncing OrElse _stackBlend.SelectedIndex < 0 Then Return
                                                         Dim mode = CType(_stackBlend.SelectedIndex, BlendMode)
                                                         ApplyChange(Sub(r)
                                                                         Dim s = StackSel(r)
                                                                         If s IsNot Nothing Then s.Blend = mode
                                                                     End Sub)
                                                     End Sub
        L.Add(_stackBlend)
        L.Y += RowHeight
        AddRow(L, MakeRow("ly_opacity", Lang.T("不透明度"), 0, 100, Function(v) v & "%",
                          Function(r) If(StackSel(r)?.Opacity, 100),
                          Sub(r, v)
                              Dim s = StackSel(r)
                              If s IsNot Nothing Then s.Opacity = v
                          End Sub))

        Dim names = {(Lang.T("移到最上"), "layers.top"), (Lang.T("上移"), "layers.up"), (Lang.T("下移"), "layers.down"), (Lang.T("移到最下"), "layers.bottom"),
                     (Lang.T("合併向下"), "layers.mergedown"), (Lang.T("合併可見"), "layers.mergevisible"), (Lang.T("編輯"), "layers.edit"), (Lang.T("刪除"), "layers.delete")}
        Dim bw = (L.Width - 8 - 3 * 4) \ 4
        For i = 0 To names.Length - 1
            Dim b = MakeButton(names(i).Item1, names(i).Item2)
            b.SetBounds(8 + (i Mod 4) * (bw + 4), L.Y + 2 + (i \ 4) * 32, bw, 28)
            Dim index = i
            AddHandler b.Click, Sub() StackCommand(index)
            _stackButtons.Add(b)
            L.Add(b)
        Next
        L.Y += 70

        _stackTop = L.Y
        _stackList.SetBounds(8, L.Y, L.Width - 8, 220)
        _stackList.DrawMode = DrawMode.OwnerDrawFixed
        _stackList.ItemHeight = 30
        _stackList.IntegralHeight = False
        _stackList.BorderStyle = BorderStyle.FixedSingle
        AddHandler _stackList.DrawItem, AddressOf StackList_DrawItem
        AddHandler _stackList.MouseDown, AddressOf StackList_MouseDown
        AddHandler _stackList.SelectedIndexChanged, Sub()
                                                         Dim row = _stackList.SelectedIndex
                                                         If _syncing OrElse row < 0 OrElse row >= _stackRows.Count Then Return
                                                         If _stackRows(row).Position >= 0 Then
                                                             SelectStack(_stackRows(row).Position)
                                                         Else
                                                             UpdateLayersPanel() ' 群組標題不是圖層：選取留在原本的圖層
                                                         End If
                                                     End Sub
        AddHandler _stackList.DoubleClick, Sub()
                                               ' 群組標題按兩下：到繪圖分頁選取整個群組
                                               Dim row = _stackList.IndexFromPoint(_stackList.PointToClient(Cursor.Position))
                                               If row >= 0 AndAlso row < _stackRows.Count AndAlso _stackRows(row).GroupId IsNot Nothing Then
                                                   Dim members = DrawGroups.MemberIndices(_recipe, _stackRows(row).GroupId)
                                                   _tabs.SelectedIndex = TabDraw
                                                   _drawStrip.SelectedTool = DrawIcons.SelectTool
                                                   If members.Count > 0 Then SetMultiSelection(members, members.Max())
                                                   Return
                                               End If
                                               EditStackLayer()
                                           End Sub
        _help.SetHelp("layers.list", _stackList)
        L.Add(_stackList)
        AddHandler page.Resize, Sub() _stackList.Height = Math.Max(120, page.ClientSize.Height - _stackTop - 8)
    End Sub

    Private Function StackCount() As Integer
        Return If(_recipe.Overlays?.Count, 0) + If(_recipe.Drawings?.Count, 0)
    End Function

    ''' <summary>清單的一列：群組標題（GroupId）或圖層（Position＝堆疊位置），Depth＝縮排。</summary>
    Private _stackRows As New List(Of (GroupId As String, Position As Integer, Depth As Integer))()

    ''' <summary>
    ''' 由上而下排出清單的列：繪圖群組的成員前面加一列群組標題、縮排；收合的群組不列成員。
    ''' 群組的成員在堆疊裡一定相鄰（見 DrawGroups），所以照順序走一遍就好。
    ''' </summary>
    Private Function BuildStackRows() As List(Of (GroupId As String, Position As Integer, Depth As Integer))
        Dim rows As New List(Of (GroupId As String, Position As Integer, Depth As Integer))()
        Dim o = LayerStack.Order(_recipe)
        Dim open As New List(Of String)() ' 目前所在的群組（由外而內）
        For pos = o.Count - 1 To 0 Step -1
            Dim chain = If(o(pos).IsOverlay, New List(Of String)(), DrawGroups.ChainOf(_recipe, o(pos).Drawing))
            chain.Reverse() ' 由外而內
            Dim common = 0
            While common < open.Count AndAlso common < chain.Count AndAlso open(common) = chain(common)
                common += 1
            End While
            open = open.Take(common).ToList()
            For k = common To chain.Count - 1
                If Not open.Any(Function(id) DrawGroups.Find(_recipe, id).Collapsed) Then rows.Add((chain(k), -1, k))
                open.Add(chain(k))
            Next
            If Not open.Any(Function(id) DrawGroups.Find(_recipe, id).Collapsed) Then rows.Add((Nothing, pos, open.Count))
        Next
        Return rows
    End Function

    ''' <summary>配方 r 裡目前選取的圖層（依堆疊位置）。</summary>
    Private Function StackSel(r As EditRecipe) As LayerRef
        If _stackIndex < 0 Then Return Nothing
        Dim o = LayerStack.Order(r)
        Return If(_stackIndex < o.Count, o(_stackIndex), Nothing)
    End Function

    ''' <summary>選取堆疊中的圖層，並同步各分頁的選取（文字貼圖 _overlayIndex、繪圖 _drawIndex）。</summary>
    Private Sub SelectStack(position As Integer)
        Dim o = LayerStack.Order(_recipe)
        _stackIndex = If(position >= 0 AndAlso position < o.Count, position, -1)
        Dim s = StackSel(_recipe)
        If s IsNot Nothing Then
            If s.IsOverlay Then
                _overlayIndex = _recipe.Overlays.IndexOf(s.Overlay)
            Else
                _drawIndex = _recipe.Drawings.IndexOf(s.Drawing)
            End If
        End If
        SyncSliders()
        UpdateCreativeControls()
        UpdateDrawControls()
        UpdateLayersPanel()
        _canvas.Invalidate()
    End Sub

    ''' <summary>從別的分頁切過來時，選取那邊正在編輯的圖層。</summary>
    Private Sub SyncStackSelectionFromTab(fromTab As Integer)
        Dim item As Object = Nothing
        Select Case fromTab
            Case TabText, TabSticker, TabDecor : item = SelOverlay(_recipe)
            Case TabDraw : item = SelDraw(_recipe)
        End Select
        If item IsNot Nothing Then _stackIndex = LayerStack.PositionOf(_recipe, item)
        SelectStack(_stackIndex)
    End Sub

    ''' <summary>配方或選取改變後更新「圖層」分頁。</summary>
    Private Sub UpdateLayersPanel()
        If _stackButtons.Count = 0 OrElse _stackList.IsDisposed Then Return ' 分頁還沒建好（啟動中）
        Dim n = StackCount()
        If _stackIndex >= n Then _stackIndex = n - 1
        Dim wasSyncing = _syncing
        _syncing = True
        Try
            _stackRows = BuildStackRows()
            If _stackList.Items.Count <> _stackRows.Count Then
                _stackList.Items.Clear()
                For i = 0 To _stackRows.Count - 1
                    _stackList.Items.Add(i)
                Next
            End If
            _stackList.SelectedIndex = If(_stackIndex < 0, -1, _stackRows.FindIndex(Function(x) x.Position = _stackIndex))
            _stackList.Invalidate()
            Dim s = StackSel(_recipe)
            _stackBlend.Enabled = s IsNot Nothing
            _stackBlend.SelectedIndex = If(s Is Nothing, -1, CInt(s.Blend))
            For Each row In _rows.Where(Function(r) r.Key = "ly_opacity")
                row.Slider.Enabled = s IsNot Nothing
            Next
            Dim has = s IsNot Nothing
            _stackButtons(0).Enabled = has AndAlso _stackIndex < n - 1
            _stackButtons(1).Enabled = has AndAlso _stackIndex < n - 1
            _stackButtons(2).Enabled = has AndAlso _stackIndex > 0
            _stackButtons(3).Enabled = has AndAlso _stackIndex > 0
            _stackButtons(4).Enabled = has AndAlso _stackIndex > 0
            _stackButtons(5).Enabled = n >= 2
            _stackButtons(6).Enabled = has
            _stackButtons(7).Enabled = has
        Finally
            _syncing = wasSyncing
        End Try
    End Sub

    Private Sub StackList_DrawItem(sender As Object, e As DrawItemEventArgs)
        Dim o = LayerStack.Order(_recipe)
        If e.Index < 0 OrElse e.Index >= _stackRows.Count Then Return
        Dim rowInfo = _stackRows(e.Index)
        Dim g = e.Graphics
        Dim r = e.Bounds
        Dim indent = rowInfo.Depth * 14
        If rowInfo.GroupId IsNot Nothing Then
            StackGroupRow(g, r, DrawGroups.Find(_recipe, rowInfo.GroupId), indent)
            Return
        End If
        Dim position = rowInfo.Position
        If position < 0 OrElse position >= o.Count Then Return
        Dim s = o(position)
        Using bg As New SolidBrush(ThemeManager.Back(If(position = _stackIndex, Color.FromArgb(210, 228, 250), Color.White)))
            g.FillRectangle(bg, r)
        End Using
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim fg = ThemeManager.Fore(If(s.Visible, Color.FromArgb(40, 44, 52), Color.FromArgb(160, 165, 172)))
        DrawEye(g, New RectangleF(r.X + 4, r.Y + 7, 18, 16), s.Visible)
        If Not s.IsOverlay Then DrawLock(g, New RectangleF(r.X + 26, r.Y + 7, 16, 16), s.Locked)
        r = New Rectangle(r.X + indent, r.Y, r.Width - indent, r.Height) ' 群組裡的圖層往右縮排（眼睛、鎖頭不動）
        Dim icon = New RectangleF(r.X + 48, r.Y + 5, 20, 20)
        If s.IsOverlay Then
            Dim glyph = If(s.Overlay.Kind = OverlayKind.Text, "T", "★")
            Using f As New Font(_stackList.Font, FontStyle.Bold)
                TextRenderer.DrawText(g, glyph, f, Rectangle.Round(icon), If(s.Overlay.Kind = OverlayKind.Text, ThemeManager.Fore(Color.FromArgb(40, 90, 170)), Color.FromArgb(220, 120, 20)),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Using
        Else
            DrawIcons.DrawTool(g, CInt(s.Drawing.Shape), icon, fg)
        End If
        Dim right = If(s.Blend = BlendMode.Normal, "", LayerBlend.Name(s.Blend) & " ") & s.Opacity & "%"
        Dim rightWidth = TextRenderer.MeasureText(right, _stackList.Font).Width + 6
        TextRenderer.DrawText(g, s.DisplayName, _stackList.Font, New Rectangle(r.X + 74, r.Y, r.Width - 74 - rightWidth, r.Height), fg,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        TextRenderer.DrawText(g, right, _stackList.Font, New Rectangle(r.Right - rightWidth, r.Y, rightWidth - 4, r.Height), ThemeManager.Fore(Color.FromArgb(130, 136, 146)),
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.Right Or TextFormatFlags.NoPrefix)
        Using line As New Pen(ThemeManager.Line(Color.FromArgb(232, 235, 240)))
            g.DrawLine(line, e.Bounds.Left, r.Bottom - 1, r.Right, r.Bottom - 1)
        End Using
    End Sub

    ''' <summary>群組標題列：眼睛、鎖頭、收合箭頭、資料夾、名稱與成員數。</summary>
    Private Sub StackGroupRow(g As Graphics, r As Rectangle, grp As DrawGroup, indent As Integer)
        Using bg As New SolidBrush(ThemeManager.Back(Color.FromArgb(244, 244, 252)))
            g.FillRectangle(bg, r)
        End Using
        If grp Is Nothing Then Return
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim fg = ThemeManager.Fore(If(grp.Visible, Color.FromArgb(40, 44, 52), Color.FromArgb(160, 165, 172)))
        DrawEye(g, New RectangleF(r.X + 4, r.Y + 7, 18, 16), grp.Visible)
        DrawLock(g, New RectangleF(r.X + 26, r.Y + 7, 16, 16), grp.Locked)
        Dim x0 = r.X + 48 + indent
        Using br As New SolidBrush(fg)
            Dim cx = x0 + 6, cy = r.Y + r.Height / 2.0F
            g.FillPolygon(br, If(grp.Collapsed, {New PointF(cx - 3, cy - 5), New PointF(cx + 3, cy), New PointF(cx - 3, cy + 5)},
                                               {New PointF(cx - 5, cy - 3), New PointF(cx + 5, cy - 3), New PointF(cx, cy + 3)}))
        End Using
        Using pen As New Pen(Color.FromArgb(127, 119, 221), 1.4F)
            Dim fx = x0 + 16, fy = r.Y + 9
            g.DrawLines(pen, {New PointF(fx, fy + 12), New PointF(fx, fy + 1), New PointF(fx + 6, fy + 1), New PointF(fx + 8, fy + 3), New PointF(fx + 18, fy + 3), New PointF(fx + 18, fy + 12), New PointF(fx, fy + 12)})
        End Using
        Dim n = DrawGroups.MemberIndices(_recipe, grp.Id).Count
        TextRenderer.DrawText(g, GroupName(grp), _stackList.Font, New Rectangle(x0 + 40, r.Y, r.Right - x0 - 40 - 50, r.Height), fg,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        TextRenderer.DrawText(g, n & Lang.T(" 個"), _stackList.Font, New Rectangle(r.Right - 50, r.Y, 46, r.Height), ThemeManager.Fore(Color.FromArgb(130, 136, 146)),
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.Right)
        Using line As New Pen(ThemeManager.Line(Color.FromArgb(232, 235, 240)))
            g.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1)
        End Using
    End Sub

    ''' <summary>點眼睛切換顯示、點鎖頭切換鎖定（只有繪圖圖層能鎖定）；群組標題的眼睛、鎖頭管整個群組，其他地方收合／展開。</summary>
    Private Sub StackList_MouseDown(sender As Object, e As MouseEventArgs)
        Dim row = _stackList.IndexFromPoint(e.Location)
        If row < 0 OrElse row >= _stackRows.Count Then Return
        Dim gid = _stackRows(row).GroupId
        If gid IsNot Nothing Then
            Dim grp = DrawGroups.Find(_recipe, gid)
            If grp Is Nothing Then Return
            If e.X < 24 Then
                Dim v = Not grp.Visible
                ApplyChange(Sub(r) DrawGroups.SetVisible(r, gid, v))
            ElseIf e.X < 46 Then
                Dim l = Not grp.Locked
                ApplyChange(Sub(r) DrawGroups.SetLocked(r, gid, l))
            ElseIf e.Clicks = 1 Then
                grp.Collapsed = Not grp.Collapsed ' 只是顯示狀態（不進復原紀錄）
                UpdateLayersPanel()
                UpdateDrawControls()
            End If
            _canvas.Invalidate()
            Return
        End If
        If e.X > 46 Then Return
        Dim position = _stackRows(row).Position
        If e.X < 24 Then
            ApplyChange(Sub(r)
                            Dim s = LayerStack.Order(r)(position)
                            s.Visible = Not s.Visible
                            If Not s.IsOverlay Then s.Drawing.HiddenByGroup = False ' 自己按的：群組再顯示時不要動它
                        End Sub)
        Else
            ApplyChange(Sub(r)
                            Dim s = LayerStack.Order(r)(position)
                            If Not s.IsOverlay Then
                                s.Drawing.Locked = Not s.Drawing.Locked
                                s.Drawing.LockedByGroup = False
                            End If
                        End Sub)
        End If
        _canvas.Invalidate()
    End Sub

    ''' <summary>0 移到最上、1 上移、2 下移、3 移到最下、4 合併向下、5 合併可見、6 編輯、7 刪除。</summary>
    Private Sub StackCommand(command As Integer)
        If _photo Is Nothing Then Return
        Dim n = StackCount()
        Dim from = _stackIndex
        Select Case command
            Case 0, 1, 2, 3
                If from < 0 Then Return
                Dim target = Select4(command, from, n)
                If target = from Then Return
                Dim moving = LayerStack.Order(_recipe)(from).Item
                ApplyChange(Sub(r)
                                LayerStack.Move(r, from, target)
                                DrawGroups.Prune(r) ' 群組的成員保持相鄰（移出群組範圍時會被排回去）
                            End Sub)
                SelectStack(LayerStack.PositionOf(_recipe, moving))
            Case 4
                Dim reason As String = Nothing
                Dim merged As DrawLayer = Nothing
                Dim aspect = PhotoAspect()
                Dim test = _recipe.Clone()
                If LayerStack.MergeDown(test, from, aspect, reason) Is Nothing Then
                    SetStatusMessage(reason) : Return
                End If
                ApplyChange(Sub(r)
                                merged = LayerStack.MergeDown(r, from, aspect, reason)
                                DrawGroups.Prune(r)
                            End Sub)
                SelectStack(LayerStack.PositionOf(_recipe, merged))
                SetStatusMessage(Lang.T("已合併成點陣圖層「") & merged.Name & Lang.T("」；可以整體移動、直接繪製或擦除，按復原可以還原。"))
            Case 5
                Dim reason As String = Nothing
                Dim merged As DrawLayer = Nothing
                Dim aspect = PhotoAspect()
                Dim test = _recipe.Clone()
                If LayerStack.MergeVisible(test, aspect, reason) Is Nothing Then
                    SetStatusMessage(reason) : Return
                End If
                ApplyChange(Sub(r)
                                merged = LayerStack.MergeVisible(r, aspect, reason)
                                DrawGroups.Prune(r)
                            End Sub)
                SelectStack(LayerStack.PositionOf(_recipe, merged))
                SetStatusMessage(Lang.T("已把所有顯示中的圖層合併成「合併圖層」；隱藏的圖層保留不動，按復原可以還原。"))
            Case 6 : EditStackLayer()
            Case 7 : DeleteStackLayer()
        End Select
    End Sub

    Private Shared Function Select4(command As Integer, from As Integer, n As Integer) As Integer
        Select Case command
            Case 0 : Return n - 1
            Case 1 : Return Math.Min(n - 1, from + 1)
            Case 2 : Return Math.Max(0, from - 1)
            Case Else : Return 0
        End Select
    End Function

    ''' <summary>跳到圖層所屬的分頁並選取它：文字 → 文字、內建貼圖 → 裝飾、圖片貼圖 → 貼圖、繪圖 → 繪圖。</summary>
    Private Sub EditStackLayer()
        Dim s = StackSel(_recipe)
        If s Is Nothing Then Return
        If s.IsOverlay AndAlso EditCompositeLayer(s.Overlay) Then Return ' 人物合成：回到引導視窗修改
        If s.IsOverlay Then
            _overlayIndex = _recipe.Overlays.IndexOf(s.Overlay)
            _tabs.SelectedIndex = If(s.Overlay.Kind = OverlayKind.Text, TabText, If(s.Overlay.Kind = OverlayKind.Image, TabSticker, TabDecor))
        Else
            _tabs.SelectedIndex = TabDraw
            SelectDrawLayer(_recipe.Drawings.IndexOf(s.Drawing))
        End If
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolFromTab()
        If Not s.IsOverlay AndAlso s.Drawing.Shape = DrawShape.FillLayer Then EditFillLayer(_drawIndex) ' 填滿圖層：直接開對話框修改
    End Sub

    Private Sub DeleteStackLayer()
        Dim s = StackSel(_recipe)
        If s Is Nothing Then Return
        CommitCalloutEditor()
        Dim position = _stackIndex
        ApplyChange(Sub(r)
                        Dim t = LayerStack.Order(r)(position)
                        If t.IsOverlay Then r.Overlays.Remove(t.Overlay) Else r.Drawings.Remove(t.Drawing)
                        DrawGroups.Prune(r)
                    End Sub)
        _overlayIndex = -1
        _drawIndex = -1
        SelectStack(Math.Min(position, StackCount() - 1))
    End Sub
End Class
