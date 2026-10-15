Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 鋼筆工具與節點工具（同 CorelDRAW、Illustrator）：
''' 鋼筆：點一下加尖角節點、按住拖曳加平滑節點並拉出把手；點回第一個節點封閉路徑；Enter、雙擊或 Esc 結束。
''' 節點：點選鋼筆路徑後顯示節點，拖曳錨點移動（Shift 加選多個一起動）、拖曳把手改彎曲（Alt 拖把手只動一邊＝轉成尖角）；
''' 雙擊路徑加節點、雙擊節點刪除、Del 刪除選取的節點；上方操作列：尖角、平滑、對稱、刪節點、斷開、封閉、接合、轉成曲線。
''' 其他圖形可以「轉成曲線」變成鋼筆路徑再改。路徑資料見 DrawPath.vb（PathContour、PathNode）。
''' </summary>
Partial Friend Class frmEditor

    ' ---- 鋼筆 ----
    Private _penContour As PathContour
    Private _penDragging As Boolean
    Private _penDownScreen As Point
    Private _penHover As PointF
    Private _penClosing As Boolean
    ' ---- 節點 ----
    ''' <summary>選取的節點（第幾條路徑, 第幾個節點）。</summary>
    Private ReadOnly _nodeSel As New HashSet(Of (C As Integer, N As Integer))()
    Private Enum NodeDrag
        None
        Anchor
        HandleIn
        HandleOut
        ''' <summary>單選的鋼筆路徑：繞中心旋轉（選取工具）。</summary>
        PathRotate
    End Enum
    Private _nd As NodeDrag
    Private _ndContour As Integer
    Private _ndNode As Integer
    Private _ndStart As PointF
    Private _ndLayer As DrawLayer
    Private _ndCenter As PointF
    ' ---- 操作列 ----
    Private ReadOnly _nodeBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                           .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0)}
    Private ReadOnly _nodeButtons As New Dictionary(Of String, Button)()

    Private ReadOnly Property PenActive As Boolean
        Get
            Return _drawStrip.SelectedTool = CInt(DrawShape.Path)
        End Get
    End Property

    Private ReadOnly Property NodeToolActive As Boolean
        Get
            Return _drawStrip.SelectedTool = DrawIcons.NodeTool
        End Get
    End Property

    '---------------------------------------------------------------------
    ' 操作列
    '---------------------------------------------------------------------

    Private Function BuildNodeBar() As Control
        Dim defs = {("corner", "尖角"), ("smooth", "平滑"), ("symmetric", "對稱"), ("delete", "刪節點"), ("break", "斷開"),
                    ("close", "封閉"), ("join", "接合"), ("convert", "轉成曲線")}
        For Each d In defs
            Dim key = d.Item1
            Dim b = MakeButton(d.Item2, "draw.node." & key)
            b.Height = 28
            b.Width = If(key = "convert", 84, If(d.Item2.Length >= 3, 64, 50))
            b.Margin = New Padding(0, 0, 4, 0)
            b.Visible = False
            AddHandler b.Click, Sub() NodeCommand(key)
            _nodeButtons(key) = b
            _nodeBar.Controls.Add(b)
        Next
        Return _nodeBar
    End Function

    ''' <summary>操作列的節點按鈕：節點工具選著鋼筆路徑時顯示；「轉成曲線」在選到能轉的圖形時顯示。</summary>
    Private Sub UpdateNodeBar()
        If _nodeButtons.Count = 0 Then Return
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        Dim isPath = sel IsNot Nothing AndAlso sel.Shape = DrawShape.Path
        Dim editing = NodeToolActive AndAlso isPath
        Dim hasNodes = editing AndAlso ValidNodeSel().Count > 0
        For Each k In {"corner", "smooth", "symmetric", "delete", "break"}
            _nodeButtons(k).Visible = hasNodes
        Next
        _nodeButtons("close").Visible = editing
        If editing Then _nodeButtons("close").Text = If(sel.Contours.All(Function(c) c.Closed), "開放", "封閉")
        _nodeButtons("join").Visible = editing AndAlso JoinableEnds().HasValue
        _nodeButtons("convert").Visible = sel IsNot Nothing AndAlso Not isPath AndAlso
                                          (DrawGeometry.ToPath(sel) IsNot Nothing OrElse (DrawGeometry.HasText(sel.Shape) AndAlso Not String.IsNullOrWhiteSpace(sel.Text))) AndAlso
                                          (NodeToolActive OrElse _drawStrip.SelectedTool = DrawIcons.SelectTool)
    End Sub

    Private Function ValidNodeSel() As List(Of (C As Integer, N As Integer))
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing OrElse sel.Shape <> DrawShape.Path OrElse sel.Contours Is Nothing Then Return New List(Of (C As Integer, N As Integer))()
        Return _nodeSel.Where(Function(x) x.C < sel.Contours.Count AndAlso x.N < sel.Contours(x.C).Nodes.Count).ToList()
    End Function

    ''' <summary>選了兩個開放路徑的端點（可以接起來）：回傳兩個端點。</summary>
    Private Function JoinableEnds() As ((C As Integer, N As Integer), (C As Integer, N As Integer))?
        Dim sel = SelDraw(_recipe)
        Dim ns = ValidNodeSel()
        If ns.Count <> 2 Then Return Nothing
        Dim isEnd = Function(x As (C As Integer, N As Integer))
                        Dim c = sel.Contours(x.C)
                        Return Not c.Closed AndAlso (x.N = 0 OrElse x.N = c.Nodes.Count - 1)
                    End Function
        If Not isEnd(ns(0)) OrElse Not isEnd(ns(1)) Then Return Nothing
        Return (ns(0), ns(1))
    End Function

    Private Sub NodeCommand(key As String)
        Dim index = _drawIndex
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing Then Return
        If key = "convert" Then
            ConvertToPath(index)
            Return
        End If
        If sel.Shape <> DrawShape.Path Then Return
        Dim ns = ValidNodeSel()
        Select Case key
            Case "corner", "smooth", "symmetric"
                Dim kind = If(key = "corner", PathNodeKind.Corner, If(key = "smooth", PathNodeKind.Smooth, PathNodeKind.Symmetric))
                ApplyChange(Sub(r)
                                For Each x In ns
                                    DrawGeometry.SetNodeKind(r.Drawings(index).Contours(x.C), x.N, kind)
                                Next
                            End Sub)
            Case "delete"
                DeleteSelectedNodes()
            Case "break"
                ' 在選取的節點斷開：封閉路徑變成從這裡開始的開放路徑；開放路徑分成兩條
                If ns.Count = 0 Then Return
                Dim x = ns(0)
                ApplyChange(Sub(r)
                                Dim d = r.Drawings(index)
                                Dim c = d.Contours(x.C)
                                If c.Closed Then
                                    Dim nodes = c.Nodes.Skip(x.N).Concat(c.Nodes.Take(x.N)).Select(Function(n) n.Clone()).ToList()
                                    nodes.Add(nodes(0).Clone()) ' 起點複製一份當終點
                                    nodes(0).InX = nodes(0).X : nodes(0).InY = nodes(0).Y
                                    nodes(nodes.Count - 1).OutX = nodes(nodes.Count - 1).X : nodes(nodes.Count - 1).OutY = nodes(nodes.Count - 1).Y
                                    nodes(0).Kind = PathNodeKind.Corner : nodes(nodes.Count - 1).Kind = PathNodeKind.Corner
                                    d.Contours(x.C) = New PathContour With {.Nodes = nodes}
                                ElseIf x.N > 0 AndAlso x.N < c.Nodes.Count - 1 Then
                                    Dim a = c.Nodes.Take(x.N + 1).Select(Function(n) n.Clone()).ToList()
                                    Dim b = c.Nodes.Skip(x.N).Select(Function(n) n.Clone()).ToList()
                                    d.Contours(x.C) = New PathContour With {.Nodes = a}
                                    d.Contours.Insert(x.C + 1, New PathContour With {.Nodes = b})
                                End If
                            End Sub)
                _nodeSel.Clear()
            Case "close"
                Dim allClosed = sel.Contours.All(Function(c) c.Closed)
                ApplyChange(Sub(r)
                                For Each c In r.Drawings(index).Contours
                                    c.Closed = Not allClosed AndAlso c.Nodes.Count >= 2
                                Next
                            End Sub)
            Case "join"
                Dim ends = JoinableEnds()
                If Not ends.HasValue Then Return
                Dim e1 = ends.Value.Item1, e2 = ends.Value.Item2
                ApplyChange(Sub(r) JoinEnds(r.Drawings(index), e1, e2))
                _nodeSel.Clear()
        End Select
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    ''' <summary>接合兩個端點：同一條路徑的頭尾＝封閉；不同路徑＝接成一條（中間加一段直線）。</summary>
    Private Shared Sub JoinEnds(d As DrawLayer, e1 As (C As Integer, N As Integer), e2 As (C As Integer, N As Integer))
        If e1.C = e2.C Then
            d.Contours(e1.C).Closed = True
            Return
        End If
        Dim c1 = d.Contours(e1.C), c2 = d.Contours(e2.C)
        Dim a = c1.Nodes.Select(Function(n) n.Clone()).ToList(), b = c2.Nodes.Select(Function(n) n.Clone()).ToList()
        If e1.N = 0 Then a = Reversed(a) ' 讓 e1 在 a 的尾巴
        If e2.N <> 0 Then b = Reversed(b) ' 讓 e2 在 b 的開頭
        Dim joined As New PathContour With {.Nodes = a.Concat(b).ToList()}
        Dim hi = Math.Max(e1.C, e2.C), lo = Math.Min(e1.C, e2.C)
        d.Contours.RemoveAt(hi)
        d.Contours(lo) = joined
    End Sub

    ''' <summary>節點倒過來（把手也對調）。</summary>
    Private Shared Function Reversed(nodes As List(Of PathNode)) As List(Of PathNode)
        Dim r = nodes.AsEnumerable().Reverse().Select(Function(n) New PathNode With {.X = n.X, .Y = n.Y, .InX = n.OutX, .InY = n.OutY, .OutX = n.InX, .OutY = n.InY, .Kind = n.Kind}).ToList()
        Return r
    End Function

    ''' <summary>把選取的圖形轉成鋼筆路徑（之後用節點工具改）。</summary>
    Private Sub ConvertToPath(index As Integer)
        If index < 0 OrElse index >= LayerCount() Then Return
        If DrawGeometry.HasText(_recipe.Drawings(index).Shape) Then ConvertTextToPaths(index) : Return ' 文字、圖說：每個字變成外框
        Dim p = DrawGeometry.ToPath(_recipe.Drawings(index))
        If p Is Nothing Then
            SetStatusMessage("圖說、點陣圖層、填滿圖層不能轉成曲線。")
            Return
        End If
        ApplyChange(Sub(r) r.Drawings(index) = p)
        _drawStrip.SelectedTool = DrawIcons.NodeTool
        SelectDrawLayer(index)
        SetStatusMessage("已轉成曲線：用節點工具拖曳節點與把手改形狀（原本的圓角、角數等設定不能再調）。")
    End Sub

    Private Sub DeleteSelectedNodes()
        Dim index = _drawIndex
        Dim ns = ValidNodeSel()
        If ns.Count = 0 Then Return
        Dim removeLayer = False
        ApplyChange(Sub(r)
                        Dim d = r.Drawings(index)
                        For Each g In ns.GroupBy(Function(x) x.C).OrderByDescending(Function(x) x.Key)
                            Dim c = d.Contours(g.Key)
                            For Each n In g.Select(Function(x) x.N).OrderByDescending(Function(x) x)
                                c.Nodes.RemoveAt(n)
                            Next
                            If c.Nodes.Count < 2 Then d.Contours.RemoveAt(g.Key)
                        Next
                        If d.Contours.Count = 0 Then
                            r.Drawings.RemoveAt(index)
                            DrawGroups.Prune(r, EnteredGroup())
                            removeLayer = True
                        End If
                    End Sub)
        _nodeSel.Clear()
        If removeLayer Then SelectDrawLayer(-1)
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 鋼筆
    '---------------------------------------------------------------------

    Private Sub PenMouseDown(e As MouseEventArgs, u As PointF)
        If e.Button <> MouseButtons.Left Then Return
        If _penContour Is Nothing Then
            _penContour = New PathContour()
            _penContour.Nodes.Add(New PathNode(u.X, u.Y))
        ElseIf _penContour.Nodes.Count >= 2 AndAlso ScreenDist(UnitToScreen(_penContour.Nodes(0).Anchor), e.Location) <= 9 Then
            _penClosing = True ' 點回第一個節點：放開時封閉並完成
        Else
            _penContour.Nodes.Add(New PathNode(u.X, u.Y))
        End If
        _penDragging = True
        _penDownScreen = e.Location
        _penHover = u
        UpdateDrawHint()
        _canvas.Invalidate()
    End Sub

    Private Sub PenMouseMove(e As MouseEventArgs, u As PointF)
        _penHover = u
        If _penDragging AndAlso _penContour IsNot Nothing AndAlso Not _penClosing AndAlso ScreenDist(e.Location, _penDownScreen) > 3 Then
            ' 拖曳：拉出平滑節點的把手（Out 跟著游標，In 對稱）
            Dim n = _penContour.Nodes(_penContour.Nodes.Count - 1)
            n.Kind = PathNodeKind.Symmetric
            n.OutX = u.X : n.OutY = u.Y
            n.InX = 2 * n.X - u.X : n.InY = 2 * n.Y - u.Y
        End If
        _canvas.Invalidate()
    End Sub

    Private Sub PenMouseUp()
        _penDragging = False
        If _penClosing Then
            _penClosing = False
            _penContour.Closed = True
            CommitPen()
        End If
        _canvas.Invalidate()
    End Sub

    ''' <summary>完成目前的鋼筆路徑（兩個節點以上才建立圖層）。</summary>
    Private Sub CommitPen()
        Dim c = _penContour
        _penContour = Nothing
        _penDragging = False
        _penClosing = False
        If c Is Nothing Then Return
        ' 雙擊時第二下會多加一個疊在一起的節點：拿掉
        While c.Nodes.Count >= 2 AndAlso ScreenDist(UnitToScreen(c.Nodes(c.Nodes.Count - 1).Anchor), UnitToScreen(c.Nodes(c.Nodes.Count - 2).Anchor)) < 3
            c.Nodes.RemoveAt(c.Nodes.Count - 1)
        End While
        If c.Nodes.Count < 2 Then
            UpdateDrawHint()
            _canvas.Invalidate()
            Return
        End If
        Dim d As New DrawLayer With {.Shape = DrawShape.Path, .Seed = _drawRandom.Next(1, 100000), .Contours = New List(Of PathContour) From {c}}
        d.CopyStyleFrom(_drawStyle)
        d.Name = "鋼筆 " & (LayerCount() + 1)
        AddDrawLayer(d)
        UpdateDrawHint()
    End Sub

    '---------------------------------------------------------------------
    ' 節點工具
    '---------------------------------------------------------------------

    ''' <summary>選取的鋼筆路徑上，畫面座標的節點與把手位置。</summary>
    Private Function HitNode(d As DrawLayer, p As Point) As (Kind As NodeDrag, C As Integer, N As Integer)
        If d Is Nothing OrElse d.Shape <> DrawShape.Path OrElse d.Contours Is Nothing Then Return (NodeDrag.None, -1, -1)
        ' 先看選取節點的把手（畫在上面）
        For Each x In ValidNodeSel()
            Dim n = d.Contours(x.C).Nodes(x.N)
            If ScreenDist(UnitToScreen(n.OutHandle), p) <= HandleRadius AndAlso DrawGeometry.Dist(n.OutHandle, n.Anchor) > 0.000001 Then Return (NodeDrag.HandleOut, x.C, x.N)
            If ScreenDist(UnitToScreen(n.InHandle), p) <= HandleRadius AndAlso DrawGeometry.Dist(n.InHandle, n.Anchor) > 0.000001 Then Return (NodeDrag.HandleIn, x.C, x.N)
        Next
        For ci = 0 To d.Contours.Count - 1
            For ni = 0 To d.Contours(ci).Nodes.Count - 1
                If ScreenDist(UnitToScreen(d.Contours(ci).Nodes(ni).Anchor), p) <= HandleRadius Then Return (NodeDrag.Anchor, ci, ni)
            Next
        Next
        Return (NodeDrag.None, -1, -1)
    End Function

    Private Sub NodeMouseDown(e As MouseEventArgs, u As PointF)
        If e.Button <> MouseButtons.Left Then Return
        Dim shift = ModifierKeys.HasFlag(Keys.Shift)
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        Dim h = HitNode(sel, e.Location)
        If h.Kind <> NodeDrag.None Then
            If h.Kind = NodeDrag.Anchor Then
                Dim key = (h.C, h.N)
                If shift Then
                    If _nodeSel.Contains(key) Then _nodeSel.Remove(key) Else _nodeSel.Add(key)
                ElseIf Not _nodeSel.Contains(key) Then
                    _nodeSel.Clear()
                    _nodeSel.Add(key)
                End If
            ElseIf ModifierKeys.HasFlag(Keys.Alt) Then
                ' Alt 拖把手：轉成尖角，只動這一邊
                Dim cc = h.C, nn = h.N, idx = _drawIndex
                ApplyChange(Sub(r) r.Drawings(idx).Contours(cc).Nodes(nn).Kind = PathNodeKind.Corner)
                sel = SelDraw(_recipe)
            End If
            _nd = h.Kind
            _ndContour = h.C
            _ndNode = h.N
            _ndStart = u
            _ndLayer = sel.Clone()
            _ddSerial += 1
            UpdateDrawControls()
            _canvas.Invalidate()
            Return
        End If
        ' 點到圖層：選它（群組裡的也直接選，方便改節點）
        Dim hit = HitLayer(u)
        _nodeSel.Clear()
        If hit <> _drawIndex OrElse IsMultiSelected() Then SelectDrawLayer(hit)
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    Private Sub NodeMouseMove(e As MouseEventArgs, u As PointF)
        Dim index = _drawIndex
        If index < 0 OrElse _ndLayer Is Nothing Then Return
        Dim d = _ndLayer.Clone()
        Select Case _nd
            Case NodeDrag.Anchor
                Dim dx = u.X - _ndStart.X, dy = u.Y - _ndStart.Y
                For Each x In _nodeSel.Where(Function(k) k.C < d.Contours.Count AndAlso k.N < d.Contours(k.C).Nodes.Count)
                    d.Contours(x.C).Nodes(x.N).Offset(dx, dy)
                Next
            Case NodeDrag.HandleIn, NodeDrag.HandleOut
                DrawGeometry.MoveHandle(d.Contours(_ndContour).Nodes(_ndNode), _nd = NodeDrag.HandleOut, u)
            Case NodeDrag.PathRotate
                Dim a0 = Math.Atan2(_ndStart.Y - _ndCenter.Y, _ndStart.X - _ndCenter.X), a1 = Math.Atan2(u.Y - _ndCenter.Y, u.X - _ndCenter.X)
                Dim deg = (a1 - a0) * 180 / Math.PI
                If ModifierKeys.HasFlag(Keys.Shift) Then deg = Math.Round(deg / 15) * 15
                DrawGeometry.RotatePath(d, _ndLayer, _ndCenter, deg)
            Case Else
                Return
        End Select
        ApplyChange(Sub(r) r.Drawings(index) = d, "node-drag-" & _ddSerial)
        _canvas.Invalidate()
    End Sub

    ''' <summary>選取工具：單選鋼筆路徑時上方的旋轉圓點（方框類有自己的旋轉）。</summary>
    Private Function PathRotateHandle(d As DrawLayer) As PointF?
        If d Is Nothing OrElse d.Shape <> DrawShape.Path OrElse d.Locked Then Return Nothing
        Dim b = DrawGeometry.Bounds(d)
        Dim top = UnitToScreen(New PointF(b.X + b.Width / 2, b.Top))
        Return New PointF(top.X, top.Y - RotateHandleOffset)
    End Function

    Private Function BeginPathRotate(e As MouseEventArgs, u As PointF) As Boolean
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        Dim h = PathRotateHandle(sel)
        If Not h.HasValue OrElse ScreenDist(h.Value, e.Location) > HandleRadius + 1 Then Return False
        Dim b = DrawGeometry.Bounds(sel)
        _nd = NodeDrag.PathRotate
        _ndCenter = New PointF(b.X + b.Width / 2, b.Y + b.Height / 2)
        _ndStart = u
        _ndLayer = sel.Clone()
        _ddSerial += 1
        Return True
    End Function

    '---------------------------------------------------------------------
    ' 掛到繪圖分頁的滑鼠、鍵盤、畫面（frmEditor.Draw.vb 呼叫）
    '---------------------------------------------------------------------

    ''' <summary>鋼筆、節點工具的按下；回傳 True＝已處理。</summary>
    Private Function PathMouseDown(e As MouseEventArgs, u As PointF) As Boolean
        If ModifierKeys.HasFlag(Keys.Control) Then Return False ' Ctrl：暫時選取（原本的流程）
        If _drawStrip.SelectedTool = DrawIcons.SelectTool AndAlso e.Button = MouseButtons.Left Then Return BeginPathRotate(e, u)
        If PenActive Then PenMouseDown(e, u) : Return True
        If NodeToolActive Then NodeMouseDown(e, u) : Return True
        Return False
    End Function

    Private Function PathMouseMove(e As MouseEventArgs, u As PointF) As Boolean
        If _nd <> NodeDrag.None Then NodeMouseMove(e, u) : Return True
        If PenActive AndAlso _penContour IsNot Nothing Then PenMouseMove(e, u) : Return True
        If NodeToolActive AndAlso _dd = DrawDrag.None Then
            Dim h = HitNode(If(IsMultiSelected(), Nothing, SelDraw(_recipe)), e.Location)
            Dim c = If(h.Kind <> NodeDrag.None, Cursors.Hand, If(HitLayer(u) >= 0, Cursors.Default, Cursors.Default))
            If _canvas.Cursor IsNot c Then _canvas.Cursor = c
            Return True
        End If
        If PenActive Then
            If _canvas.Cursor IsNot Cursors.Cross Then _canvas.Cursor = Cursors.Cross
            Return True
        End If
        Return False
    End Function

    Private Function PathMouseUp() As Boolean
        If _nd <> NodeDrag.None Then
            _nd = NodeDrag.None
            _ndLayer = Nothing
            UpdateDrawControls()
            _canvas.Invalidate()
            Return True
        End If
        If PenActive AndAlso _penDragging Then PenMouseUp() : Return True
        Return False
    End Function

    ''' <summary>雙擊：鋼筆＝完成；節點工具＝路徑上加節點、節點上刪除。</summary>
    Private Function PathDoubleClick(e As MouseEventArgs) As Boolean
        If PenActive AndAlso _penContour IsNot Nothing Then
            CommitPen()
            Return True
        End If
        If Not NodeToolActive Then Return False
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        If sel Is Nothing OrElse sel.Shape <> DrawShape.Path Then Return False
        Dim index = _drawIndex
        Dim h = HitNode(sel, e.Location)
        If h.Kind = NodeDrag.Anchor Then
            _nodeSel.Clear()
            _nodeSel.Add((h.C, h.N))
            DeleteSelectedNodes()
            Return True
        End If
        Dim u = ScreenToUnit(e.Location)
        Dim near = DrawGeometry.NearestOnPath(sel, u)
        If near.Contour >= 0 AndAlso near.Distance <= HitTolerance() + sel.StrokeWidth / 2 Then
            Dim added = -1
            ApplyChange(Sub(r) added = DrawGeometry.InsertNode(r.Drawings(index).Contours(near.Contour), near.Seg, near.T))
            _nodeSel.Clear()
            _nodeSel.Add((near.Contour, added))
            UpdateDrawControls()
            _canvas.Invalidate()
            Return True
        End If
        Return False
    End Function

    ''' <summary>Enter／Esc 結束鋼筆；Del 刪節點。回傳 True＝已處理。</summary>
    Private Function PathKey(keyData As Keys) As Boolean
        If PenActive AndAlso _penContour IsNot Nothing AndAlso (keyData = Keys.Enter OrElse keyData = Keys.Escape) Then
            CommitPen()
            Return True
        End If
        If NodeToolActive AndAlso keyData = Keys.Delete AndAlso ValidNodeSel().Count > 0 Then
            DeleteSelectedNodes()
            Return True
        End If
        Return False
    End Function

    ''' <summary>換工具時：鋼筆畫到一半的完成、節點選取清掉。</summary>
    Private Sub PathToolChanged()
        If _penContour IsNot Nothing AndAlso Not PenActive Then CommitPen()
        If Not NodeToolActive Then _nodeSel.Clear()
    End Sub

    ''' <summary>畫面：鋼筆畫到一半的路徑與橡皮筋；節點工具的節點、把手；選取工具的路徑旋轉圓點。</summary>
    Private Sub PaintPath(g As Graphics)
        Dim blue = Color.FromArgb(24, 95, 165)
        ' 鋼筆畫到一半
        If _penContour IsNot Nothing AndAlso _penContour.Nodes.Count > 0 Then
            Dim pts = DrawGeometry.SampleContour(_penContour).Select(Function(q) UnitToScreen(q)).ToArray()
            Using outer As New Pen(Color.FromArgb(150, 0, 0, 0), 3), pen As New Pen(Color.FromArgb(_drawStyle.StrokeColorArgb), 1.6F)
                If pts.Length >= 2 Then g.DrawLines(outer, pts) : g.DrawLines(pen, pts)
                If Not _penDragging Then
                    ' 橡皮筋：最後一個節點到游標（照它的 Out 把手彎）
                    Dim last = _penContour.Nodes(_penContour.Nodes.Count - 1)
                    Dim seg = {last.Anchor, last.OutHandle, _penHover, _penHover}
                    Dim rubber = Enumerable.Range(0, 21).Select(Function(k) UnitToScreen(DrawGeometry.BezierAt(seg, k / 20.0))).ToArray()
                    Using dash As New Pen(Color.FromArgb(200, 55, 138, 221), 1.2F) With {.DashStyle = DashStyle.Dash}
                        g.DrawLines(dash, rubber)
                    End Using
                End If
            End Using
            For i = 0 To _penContour.Nodes.Count - 1
                Dim n = _penContour.Nodes(i)
                If i = _penContour.Nodes.Count - 1 Then PaintHandles(g, n)
                PaintAnchor(g, UnitToScreen(n.Anchor), i = 0, False)
            Next
            ' 游標移到第一個節點上：顯示可以封閉
            If _penContour.Nodes.Count >= 2 AndAlso ScreenDist(UnitToScreen(_penContour.Nodes(0).Anchor), UnitToScreen(_penHover)) <= 9 Then
                Dim f = UnitToScreen(_penContour.Nodes(0).Anchor)
                Using ring As New Pen(blue, 1.5F)
                    g.DrawEllipse(ring, f.X - 9, f.Y - 9, 18, 18)
                End Using
            End If
        End If
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        If sel Is Nothing OrElse sel.Shape <> DrawShape.Path OrElse sel.Contours Is Nothing OrElse Not sel.Visible Then Return
        If NodeToolActive Then
            Dim chosen = New HashSet(Of (C As Integer, N As Integer))(ValidNodeSel())
            For ci = 0 To sel.Contours.Count - 1
                For ni = 0 To sel.Contours(ci).Nodes.Count - 1
                    If chosen.Contains((ci, ni)) Then PaintHandles(g, sel.Contours(ci).Nodes(ni))
                Next
            Next
            For ci = 0 To sel.Contours.Count - 1
                For ni = 0 To sel.Contours(ci).Nodes.Count - 1
                    PaintAnchor(g, UnitToScreen(sel.Contours(ci).Nodes(ni).Anchor), chosen.Contains((ci, ni)), sel.Contours(ci).Nodes(ni).Kind <> PathNodeKind.Corner)
                Next
            Next
        ElseIf _drawStrip.SelectedTool = DrawIcons.SelectTool Then
            Dim h = PathRotateHandle(sel)
            If h.HasValue Then
                Dim b = DrawGeometry.Bounds(sel)
                Dim top = UnitToScreen(New PointF(b.X + b.Width / 2, b.Top))
                Using frame As New Pen(Color.FromArgb(120, 90, 170, 255), 1) With {.DashStyle = DashStyle.Dot}
                    g.DrawLine(frame, top, h.Value)
                End Using
                DrawHandleMark(g, New DrawHandle With {.Kind = HandleKind.Rotate, .Pos = h.Value}, sel)
            End If
        End If
    End Sub

    ''' <summary>錨點：尖角是方塊、平滑／對稱是圓；選取的實心藍色。</summary>
    Private Shared Sub PaintAnchor(g As Graphics, p As PointF, selected As Boolean, smooth As Boolean)
        Dim r = 4.5F
        Using br As New SolidBrush(If(selected, Color.FromArgb(24, 95, 165), Color.White)), pen As New Pen(Color.FromArgb(24, 95, 165), 1.4F)
            If smooth Then
                g.FillEllipse(br, p.X - r, p.Y - r, r * 2, r * 2) : g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2)
            Else
                g.FillRectangle(br, p.X - r, p.Y - r, r * 2, r * 2) : g.DrawRectangle(pen, p.X - r, p.Y - r, r * 2, r * 2)
            End If
        End Using
    End Sub

    ''' <summary>節點的兩個把手：細線＋實心小圓。</summary>
    Private Sub PaintHandles(g As Graphics, n As PathNode)
        Dim a = UnitToScreen(n.Anchor)
        Using pen As New Pen(Color.FromArgb(55, 138, 221), 1), br As New SolidBrush(Color.FromArgb(55, 138, 221))
            For Each hp In {n.InHandle, n.OutHandle}
                If DrawGeometry.Dist(hp, n.Anchor) < 0.000001 Then Continue For
                Dim s = UnitToScreen(hp)
                g.DrawLine(pen, a, s)
                g.FillEllipse(br, s.X - 3.5F, s.Y - 3.5F, 7, 7)
            Next
        End Using
    End Sub
End Class
