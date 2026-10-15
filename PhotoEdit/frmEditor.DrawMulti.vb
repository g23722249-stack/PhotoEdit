Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 繪圖分頁的多選：選取工具 Shift＋點選加選／取消、拖曳空白處框選、圖層清單 Ctrl／Shift 多選。
''' 選了兩個以上時顯示共同的外框：拖曳任何一個一起移動，藍色方塊一起縮放、上方圓點一起旋轉；
''' 顏色、粗細、不透明度等設定一起改；方向鍵、Del 也是一起。
''' _drawIndex 仍是「主要」的那一個（最後點的），其他在 _drawMulti。
''' </summary>
Partial Friend Class frmEditor

    ''' <summary>多選時全部選取的圖層索引（含 _drawIndex）；單選或沒選時是空的。</summary>
    Private ReadOnly _drawMulti As New SortedSet(Of Integer)()
    ''' <summary>框選中：起點與目前位置（照片高度單位）。</summary>
    Private _marqueeStart As PointF
    Private _marqueeCur As PointF
    Private _marqueeAdd As Boolean
    ''' <summary>一起拖曳時，按下當時各圖層的複本與共同外框。</summary>
    Private _ddGroup As List(Of (Index As Integer, Layer As DrawLayer))
    Private _ddGroupBounds As RectangleF

    ''' <summary>選取的圖層索引（由下而上排序）：多選時全部，單選時一個，沒選時空的。</summary>
    Private Function SelectedIndices() As List(Of Integer)
        Dim n = LayerCount()
        If _drawMulti.Count >= 2 AndAlso _drawMulti.Contains(_drawIndex) Then ' 主要的那一個被別處換掉時多選失效
            Dim list = _drawMulti.Where(Function(i) i >= 0 AndAlso i < n).ToList()
            If list.Count >= 2 Then Return list
        End If
        If _drawIndex >= 0 AndAlso _drawIndex < n Then Return New List(Of Integer) From {_drawIndex}
        Return New List(Of Integer)()
    End Function

    Private Function IsMultiSelected() As Boolean
        Return SelectedIndices().Count >= 2
    End Function

    ''' <summary>設定多選（primary＝主要的那一個）；只有一個時等於單選。</summary>
    Private Sub SetMultiSelection(indices As IEnumerable(Of Integer), primary As Integer)
        Dim n = LayerCount()
        Dim valid = indices.Where(Function(i) i >= 0 AndAlso i < n).Distinct().ToList()
        If valid.Count = 0 Then
            SelectDrawLayer(-1)
            Return
        End If
        If Not valid.Contains(primary) Then primary = valid.Max()
        SelectDrawLayer(primary) ' 會清掉多選
        If valid.Count >= 2 Then
            For Each i In valid
                _drawMulti.Add(i)
            Next
            UpdateDrawControls()
            _canvas.Invalidate()
        End If
    End Sub

    ''' <summary>Shift＋點選：加選或取消。</summary>
    Private Sub ToggleInSelection(index As Integer)
        Dim cur = SelectedIndices()
        If cur.Contains(index) Then
            cur.Remove(index)
            SetMultiSelection(cur, If(cur.Count > 0, cur.Last(), -1))
        Else
            cur.Add(index)
            SetMultiSelection(cur, index)
        End If
    End Sub

    ''' <summary>可以一起改顏色、粗細等設定的圖層：多選時全部（填滿圖層除外），單選時同 StyleTarget。</summary>
    Private Function StyleTargets(r As EditRecipe) As List(Of DrawLayer)
        If IsMultiSelected() AndAlso _drawStrip.SelectedTool = DrawIcons.SelectTool Then
            Return SelectedIndices().Select(Function(i) r.Drawings(i)).Where(Function(d) d.Shape <> DrawShape.FillLayer).ToList()
        End If
        Dim t = StyleTarget(r)
        Return If(t Is Nothing, New List(Of DrawLayer)(), New List(Of DrawLayer) From {t})
    End Function

    '---------------------------------------------------------------------
    ' 共同外框的控制點
    '---------------------------------------------------------------------

    ''' <summary>多選的共同外框（照片高度單位）。</summary>
    Private Function MultiBounds() As RectangleF
        Return DrawGeometry.GroupBounds(SelectedIndices().Select(Function(i) _recipe.Drawings(i)).Where(Function(d) d.Visible))
    End Function

    ''' <summary>多選外框的 8 個縮放點與旋轉點（螢幕座標）；不能動（全部鎖定）時是空的。</summary>
    Private Function MultiHandles() As List(Of DrawHandle)
        Dim list As New List(Of DrawHandle)()
        If Not IsMultiSelected() OrElse SelectedIndices().All(Function(i) _recipe.Drawings(i).Locked) Then Return list
        Dim b = MultiBounds()
        If b.IsEmpty AndAlso b.Width <= 0 Then Return list
        For i = 0 To 7
            list.Add(New DrawHandle With {.Kind = HandleKind.RectScale, .Index = i, .Pos = UnitToScreen(DrawGeometry.RectHandle(b, i))})
        Next
        Dim top = UnitToScreen(New PointF(b.X + b.Width / 2, b.Top))
        list.Add(New DrawHandle With {.Kind = HandleKind.Rotate, .Pos = New PointF(top.X, top.Y - RotateHandleOffset)})
        Return list
    End Function

    Private Function HitMultiHandle(p As Point) As DrawHandle?
        Dim hs = MultiHandles()
        For i = hs.Count - 1 To 0 Step -1
            If ScreenDist(hs(i).Pos, p) <= HandleRadius + 1 Then Return hs(i)
        Next
        Return Nothing
    End Function

    '---------------------------------------------------------------------
    ' 滑鼠
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 選取工具按下（多選版）：多選的控制點 → 一起縮放／旋轉；Shift＋點圖層 → 加選或取消；
    ''' 點到已選的圖層 → 一起移動；點到別的圖層 → 單選；點空白 → 開始框選（Shift 時加選）。
    ''' 回傳 True＝已處理；False＝交給原本的單選流程（單選的控制點）。
    ''' </summary>
    Private Function MultiMouseDown(e As MouseEventArgs, u As PointF) As Boolean
        Dim shift = ModifierKeys.HasFlag(Keys.Shift)
        If IsMultiSelected() Then
            Dim h = HitMultiHandle(e.Location)
            If h.HasValue Then
                BeginGroupDrag(If(h.Value.Kind = HandleKind.Rotate, DrawDrag.GroupRotate, DrawDrag.GroupScale), h.Value.Index, u, e.Location)
                Return True
            End If
        ElseIf Not shift Then
            ' 單選：選取圖層自己的控制點優先（交給原本的流程）
            Dim sel = SelDraw(_recipe)
            If sel IsNot Nothing AndAlso sel.Visible AndAlso Not sel.Locked AndAlso HitHandle(sel, e.Location).HasValue Then Return False
        End If
        Dim selected = SelectedIndices()
        Dim tol = HitTolerance()
        ' 已選的圖層優先（上面蓋著別的圖層也能拖）
        Dim onSel = selected.Where(Function(i)
                                       Dim d = _recipe.Drawings(i)
                                       Return d.Visible AndAlso Not d.Locked AndAlso LayerHit(d, u, tol)
                                   End Function).DefaultIfEmpty(-1).Max()
        Dim hit = If(onSel >= 0, onSel, HitLayer(u))
        ' Ctrl＋點：直接選群組裡的那一個（不展開成整個群組）
        Dim ctrl = ModifierKeys.HasFlag(Keys.Control)
        ' 點到正在編輯的群組外面：離開群組
        If hit >= 0 AndAlso Not ctrl AndAlso EnteredGroup() IsNot Nothing AndAlso Not DrawGroups.IsInside(_recipe, _recipe.Drawings(hit), _enteredGroup) Then
            _enteredGroup = Nothing
        End If
        ' 點選的單位：群組裡的圖層換成整個群組（見 frmEditor.DrawGroups.vb）
        Dim unit = If(hit < 0, New List(Of Integer)(), If(ctrl, New List(Of Integer) From {hit}, ExpandToGroups({hit})))
        If shift Then
            If hit >= 0 Then
                Dim cur = SelectedIndices()
                If unit.All(Function(i) cur.Contains(i)) Then cur = cur.Except(unit).ToList() Else cur = cur.Union(unit).ToList()
                SetMultiSelection(cur, If(cur.Contains(hit), hit, If(cur.Count > 0, cur.Max(), -1)))
            Else
                BeginMarquee(u, True)
            End If
            _canvas.Invalidate()
            Return True
        End If
        If hit < 0 Then
            BeginMarquee(u, False)
            _canvas.Invalidate()
            Return True
        End If
        If ctrl Then
            If selected.Count <> 1 OrElse selected(0) <> hit Then SelectDrawLayer(hit)
            Return False ' 單選＋移動：原本的流程
        End If
        If onSel >= 0 AndAlso selected.Count >= 2 AndAlso unit.All(Function(i) selected.Contains(i)) Then
            _drawIndex = onSel ' 主要的換成點到的這個，多選不變
            BeginGroupDrag(DrawDrag.GroupMove, 0, u, e.Location)
            UpdateDrawControls()
            Return True
        End If
        If unit.Count >= 2 Then
            SetMultiSelection(unit, hit) ' 選到整個群組，可以直接拖
            BeginGroupDrag(DrawDrag.GroupMove, 0, u, e.Location)
            Return True
        End If
        Return False ' 單選＋移動：原本的流程
    End Function

    Private Sub BeginMarquee(u As PointF, add As Boolean)
        _dd = DrawDrag.Marquee
        _marqueeStart = u
        _marqueeCur = u
        _marqueeAdd = add
    End Sub

    Private Sub BeginGroupDrag(kind As DrawDrag, handle As Integer, u As PointF, screen As Point)
        _dd = kind
        _ddHandle = handle
        _ddStart = u
        _ddSerial += 1
        _ddDownScreen = screen
        _ddArmed = kind <> DrawDrag.GroupMove
        _ddPen = _canvas.PenPressure.HasValue
        _ddGroup = SelectedIndices().Where(Function(i) Not _recipe.Drawings(i).Locked).
                   Select(Function(i) (Index:=i, Layer:=_recipe.Drawings(i).Clone())).ToList()
        _ddGroupBounds = DrawGeometry.GroupBounds(_ddGroup.Select(Function(x) x.Layer).Where(Function(d) d.Visible))
    End Sub

    ''' <summary>拖曳中（多選、框選）；回傳 True＝已處理。</summary>
    Private Function MultiMouseMove(e As MouseEventArgs, u As PointF) As Boolean
        Dim shift = ModifierKeys.HasFlag(Keys.Shift)
        Select Case _dd
            Case DrawDrag.Marquee
                _marqueeCur = u
                _canvas.Invalidate()
            Case DrawDrag.GroupMove
                If Not _ddArmed Then
                    If ScreenDist(e.Location, _ddDownScreen) < If(_ddPen, 8, 5) Then Return True
                    _ddArmed = True
                End If
                Dim dx As Double = u.X - _ddStart.X, dy As Double = u.Y - _ddStart.Y
                SnapMove(_ddGroupBounds, _ddGroup.Select(Function(x) x.Index).ToList(), dx, dy) ' 智慧參考線
                ReplaceGroup(Sub(d, s) DrawGeometry.Offset(d, dx, dy))
            Case DrawDrag.GroupScale
                ' 角落預設等比例（多個圖形一起縮放不要變形），Shift 時自由縮放；邊上的點只縮一個方向
                Dim nb = DrawGeometry.DragRect(_ddGroupBounds, _ddHandle, u, Not shift)
                ReplaceGroup(Sub(d, s) DrawGeometry.ScaleInGroup(d, s, _ddGroupBounds, nb))
            Case DrawDrag.GroupRotate
                Dim c = New PointF(_ddGroupBounds.X + _ddGroupBounds.Width / 2, _ddGroupBounds.Y + _ddGroupBounds.Height / 2)
                Dim a0 = Math.Atan2(_ddStart.Y - c.Y, _ddStart.X - c.X), a1 = Math.Atan2(u.Y - c.Y, u.X - c.X)
                Dim deg = (a1 - a0) * 180 / Math.PI
                If shift Then deg = Math.Round(deg / 15) * 15
                ReplaceGroup(Sub(d, s) DrawGeometry.RotateInGroup(d, s, c, deg))
            Case Else
                Return False
        End Select
        Return True
    End Function

    ''' <summary>放開（多選、框選）；回傳 True＝已處理。</summary>
    Private Function MultiMouseUp(kind As DrawDrag) As Boolean
        Select Case kind
            Case DrawDrag.Marquee
                Dim a = UnitToScreen(_marqueeStart), b = UnitToScreen(_marqueeCur)
                If ScreenDist(a, b) < 4 Then
                    If Not _marqueeAdd Then SelectDrawLayer(-1) ' 點空白：取消選取
                Else
                    Dim r = RectangleF.FromLTRB(Math.Min(_marqueeStart.X, _marqueeCur.X), Math.Min(_marqueeStart.Y, _marqueeCur.Y),
                                                Math.Max(_marqueeStart.X, _marqueeCur.X), Math.Max(_marqueeStart.Y, _marqueeCur.Y))
                    Dim found = ExpandToGroups(MarqueeHits(r)) ' 群組裡的圖層換成整個群組
                    Dim list = If(_marqueeAdd, SelectedIndices().Union(found).ToList(), found)
                    SetMultiSelection(list, If(found.Count > 0, found.Max(), If(list.Count > 0, list.Max(), -1)))
                End If
            Case DrawDrag.GroupMove, DrawDrag.GroupScale, DrawDrag.GroupRotate
                _ddGroup = Nothing
                UpdateDrawControls()
            Case Else
                Return False
        End Select
        _canvas.Invalidate()
        Return True
    End Function

    ''' <summary>
    ''' 框選到的圖層：向量圖形碰到框就算；點陣圖層要整個在框裡（它常常很大，碰到就選會選到不想要的）；
    ''' 填滿圖層、隱藏、鎖定的不算。
    ''' </summary>
    Private Function MarqueeHits(r As RectangleF) As List(Of Integer)
        Dim list As New List(Of Integer)()
        For i = 0 To LayerCount() - 1
            Dim d = _recipe.Drawings(i)
            If Not d.Visible OrElse d.Locked OrElse d.Shape = DrawShape.FillLayer Then Continue For
            Dim b = DrawGeometry.Bounds(d)
            If b.Width <= 0 AndAlso b.Height <= 0 AndAlso b.X = 0 AndAlso b.Y = 0 Then Continue For
            Dim ok = If(d.Shape = DrawShape.Raster, r.Contains(b), r.IntersectsWith(RectangleF.Inflate(b, 0.0001F, 0.0001F)))
            If ok Then list.Add(i)
        Next
        Return list
    End Function

    ''' <summary>多選拖曳中：以按下時的複本為準套用變化（整段拖曳在復原紀錄裡算一步）。</summary>
    Private Sub ReplaceGroup(change As Action(Of DrawLayer, DrawLayer))
        If _ddGroup Is Nothing Then Return
        Dim items = _ddGroup.Where(Function(x) x.Index < LayerCount()).Select(Function(x)
                                                                                  Dim d = x.Layer.Clone()
                                                                                  change(d, x.Layer)
                                                                                  Return (x.Index, d)
                                                                              End Function).ToList()
        ApplyChange(Sub(r)
                        For Each it In items
                            r.Drawings(it.Index) = it.d
                        Next
                    End Sub, "draw-group-" & _ddSerial)
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 刪除、微調、複製
    '---------------------------------------------------------------------

    Private Sub DeleteSelectedLayers()
        Dim idx = SelectedIndices()
        If idx.Count = 0 Then Return
        CommitCalloutEditor()
        ApplyChange(Sub(r)
                        For Each i In idx.OrderByDescending(Function(x) x)
                            r.Drawings.RemoveAt(i)
                        Next
                        DrawGroups.Prune(r, EnteredGroup()) ' 刪光成員的群組一起刪掉
                    End Sub)
        SelectDrawLayer(Math.Min(idx.Min(), LayerCount() - 1))
    End Sub

    Private Sub NudgeSelected(dx As Double, dy As Double)
        Dim idx = SelectedIndices().Where(Function(i) Not _recipe.Drawings(i).Locked).ToList()
        If idx.Count = 0 Then Return
        ApplyChange(Sub(r)
                        For Each i In idx
                            DrawGeometry.Offset(r.Drawings(i), dx, dy)
                        Next
                    End Sub, "draw-nudge")
        _canvas.Invalidate()
    End Sub

    ''' <summary>多選複製：每個複本放在原圖層正上方、稍微錯開，選取換成複本。</summary>
    Private Sub DuplicateSelected()
        Dim idx = SelectedIndices()
        If idx.Count = 0 Then Return
        Dim originals = idx.Select(Function(i) _recipe.Drawings(i)).ToList()
        Dim copies = originals.Select(Function(o)
                                          Dim d = o.Clone()
                                          DrawGeometry.Offset(d, 0.02, 0.02)
                                          d.Name &= " 複本"
                                          d.Id = Nothing
                                          Return d
                                      End Function).ToList()
        ApplyChange(Sub(r)
                        For k = 0 To copies.Count - 1
                            r.Drawings.Add(copies(k))
                            LayerStack.PlaceAbove(r, copies(k), originals(k))
                        Next
                    End Sub)
        Dim newIdx = copies.Select(Function(c) _recipe.Drawings.IndexOf(c)).ToList()
        SetMultiSelection(newIdx, newIdx.Max())
    End Sub

    '---------------------------------------------------------------------
    ' 畫面
    '---------------------------------------------------------------------

    ''' <summary>多選：每個圖形的虛線外形、共同外框與控制點；框選中的虛線框。</summary>
    Private Sub PaintMulti(g As Graphics)
        If _dd = DrawDrag.Marquee Then
            Dim a = UnitToScreen(_marqueeStart), b = UnitToScreen(_marqueeCur)
            Dim r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))
            Using br As New SolidBrush(Color.FromArgb(30, 55, 138, 221)), pen As New Pen(Color.FromArgb(220, 55, 138, 221), 1) With {.DashStyle = DashStyle.Dash}
                g.FillRectangle(br, r)
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height)
            End Using
        End If
        If Not IsMultiSelected() Then Return
        Using outer As New Pen(Color.FromArgb(120, 0, 0, 0), 3), inner As New Pen(Color.FromArgb(90, 170, 255), 1.2F) With {.DashStyle = DashStyle.Dash}
            For Each i In SelectedIndices()
                Dim d = _recipe.Drawings(i)
                If Not d.Visible Then Continue For
                If d.Shape = DrawShape.Raster OrElse d.Shape = DrawShape.FillLayer Then
                    Dim rb = If(d.Shape = DrawShape.Raster, DrawingRenderer.RasterVisibleBounds(d, PhotoAspect()), RectangleF.Empty)
                    If rb.IsEmpty Then Continue For
                    Dim p0 = UnitToScreen(rb.Location), p1 = UnitToScreen(New PointF(rb.Right, rb.Bottom))
                    g.DrawRectangle(inner, p0.X, p0.Y, p1.X - p0.X, p1.Y - p0.Y)
                    Continue For
                End If
                For Each f In DrawGeometry.Figures(d)
                    Dim pts = f.Points.Select(Function(p) UnitToScreen(p)).ToArray()
                    If pts.Length < 2 Then Continue For
                    If f.Closed Then
                        g.DrawPolygon(outer, pts) : g.DrawPolygon(inner, pts)
                    Else
                        g.DrawLines(outer, pts) : g.DrawLines(inner, pts)
                    End If
                Next
            Next
        End Using
        Dim hs = MultiHandles()
        Dim b2 = MultiBounds()
        If Not b2.IsEmpty OrElse b2.Width > 0 Then
            Dim a = UnitToScreen(b2.Location), c = UnitToScreen(New PointF(b2.Right, b2.Bottom))
            Using frame As New Pen(Color.FromArgb(24, 95, 165), 1.2F)
                g.DrawRectangle(frame, a.X, a.Y, c.X - a.X, c.Y - a.Y)
                If hs.Count > 8 Then g.DrawLine(frame, New PointF((a.X + c.X) / 2, a.Y), hs(8).Pos)
            End Using
        End If
        For Each h In hs
            DrawHandleMark(g, h, Nothing)
        Next
    End Sub

    ''' <summary>多選時的游標：共同外框的控制點、已選的圖形（可移動）。回傳 Nothing＝交給單選的判斷。</summary>
    Private Function MultiCursor(p As Point, u As PointF) As Cursor
        If Not IsMultiSelected() Then Return Nothing
        Dim h = HitMultiHandle(p)
        If h.HasValue Then
            If h.Value.Kind = HandleKind.Rotate Then Return Cursors.Hand
            Dim i = h.Value.Index
            Return If(i Mod 4 = 1, Cursors.SizeNS, If(i Mod 4 = 3, Cursors.SizeWE, If(i Mod 4 = 0, Cursors.SizeNWSE, Cursors.SizeNESW)))
        End If
        Dim tol = HitTolerance()
        If SelectedIndices().Any(Function(i) _recipe.Drawings(i).Visible AndAlso Not _recipe.Drawings(i).Locked AndAlso LayerHit(_recipe.Drawings(i), u, tol)) Then Return Cursors.SizeAll
        Return If(HitLayer(u) >= 0, Cursors.SizeAll, Cursors.Default)
    End Function
End Class
