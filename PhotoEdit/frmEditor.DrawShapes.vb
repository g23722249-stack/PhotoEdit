Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 對齊、均分、焊接修剪（造形）、形狀建立、智慧參考線（同 Illustrator、CorelDRAW）：
''' 選取工具選了物件時，上方操作列出現對齊（靠左、水平置中、靠右、靠上、垂直置中、靠下；只選一個時對齊照片）、
''' 均分（三個以上，間距一樣）、焊接／修剪／相交／排除（兩個以上的封閉向量圖形，結果變成鋼筆路徑），
''' 以及「形狀建立」：重疊的圖形切成一塊一塊，拖過幾塊就合成一塊，Alt＋點（或拖）刪掉那幾塊。
''' 移動物件時邊緣、中心對齊其他物件或照片時會吸附並顯示洋紅色參考線（按住 Alt 暫時不吸附）。
''' 群組算一個單位（對齊、均分時整組一起動）。運算見 PathBoolean。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _shapeBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                            .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0)}
    Private ReadOnly _shapeButtons As New Dictionary(Of String, Button)()
    ' ---- 形狀建立 ----
    Private _builderOn As Boolean
    Private _builderSources As List(Of DrawLayer)
    Private _builderFaces As List(Of ShapeFace)
    Private _builderHover As Integer = -1
    Private ReadOnly _builderPicked As New List(Of Integer)()
    Private _builderDragging As Boolean
    Private _builderDelete As Boolean
    ' ---- 智慧參考線 ----
    Private ReadOnly _guides As New List(Of (Vertical As Boolean, Pos As Single, A As Single, B As Single))()

    Private Shared ReadOnly AlignKeys As String() = {"left", "hcenter", "right", "top", "vcenter", "bottom"}
    Private Shared ReadOnly OpKeys As String() = {"weld", "trim", "intersect", "exclude"}

    '---------------------------------------------------------------------
    ' 操作列
    '---------------------------------------------------------------------

    Private ReadOnly _alignMenu As New ContextMenuStrip()
    Private Shared ReadOnly GlyphNames As New Dictionary(Of String, String) From {
        {"left", Lang.T("靠左對齊")}, {"hcenter", Lang.T("水平置中")}, {"top", Lang.T("靠上對齊")}, {"more", Lang.T("更多對齊…")},
        {"weld", Lang.T("焊接")}, {"trim", Lang.T("修剪")}, {"intersect", Lang.T("相交")}, {"exclude", Lang.T("排除")}}

    Private Function BuildShapeBar() As Control
        ' 常用的三個對齊直接放，其餘（靠右、垂直置中、靠下、均分）在「⋯」選單；形狀建立在左側工具列
        Dim keys = {"left", "hcenter", "top", "more", "|"}.Concat(OpKeys).Concat({"|"})
        For Each k In keys
            If k = "|" Then
                _shapeBar.Controls.Add(New Panel With {.Width = 8, .Height = 28, .Margin = Padding.Empty, .BackColor = Color.Transparent})
                Continue For
            End If
            Dim key = k
            Dim help = If(AlignKeys.Contains(key), "draw.align." & key, If(OpKeys.Contains(key), "draw.op." & key, "draw.align." & key))
            Dim b = MakeButton("", help)
            b.Size = New Size(If(key = "builder", 30, 28), 28)
            b.Margin = New Padding(0, 0, 2, 0)
            b.Visible = False
            b.AccessibleName = GlyphNames(key) ' 收進「»」選單時顯示的名稱
            AddHandler b.Paint, Sub(s, e) PaintShapeGlyph(e.Graphics, key, DirectCast(s, Button))
            AddHandler b.Click, Sub() ShapeCommand(key)
            _shapeButtons(key) = b
            _shapeBar.Controls.Add(b)
        Next
        For Each t In {("combine", Lang.T("結合"), "draw.combine"), ("breakapart", Lang.T("打散"), "draw.breakapart"), ("clipin", Lang.T("放進形狀"), "draw.clip.in"), ("clipout", Lang.T("取出內容"), "draw.clip.out"), ("photoclip", Lang.T("照片放進形狀"), "draw.clip.photo"),
                       ("outline", Lang.T("外框轉物件"), "draw.outline"), ("effects", Lang.T("效果 ▾"), "draw.effects")}
            Dim key = t.Item1
            Dim b = MakeButton(t.Item2, t.Item3)
            b.Height = 28
            b.AutoSize = True
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink
            b.Margin = New Padding(0, 0, 4, 0)
            b.Visible = False
            AddHandler b.Click, Sub() ShapeCommand(key)
            _shapeButtons(key) = b
            _shapeBar.Controls.Add(b)
        Next
        For Each m In {("right", Lang.T("靠右對齊")), ("vcenter", Lang.T("垂直置中")), ("bottom", Lang.T("靠下對齊")), ("-", ""), ("disth", Lang.T("水平均分")), ("distv", Lang.T("垂直均分"))}
            Dim key = m.Item1
            If key = "-" Then _alignMenu.Items.Add(New ToolStripSeparator()) : Continue For
            _alignMenu.Items.Add(m.Item2, Nothing, Sub() ShapeCommand(key)).Tag = key
        Next
        AddHandler _alignMenu.Opening, Sub()
                                           Dim n = SelectionUnits().Count
                                           For Each it As ToolStripItem In _alignMenu.Items
                                               If TypeOf it.Tag Is String AndAlso CStr(it.Tag).StartsWith("dist") Then it.Enabled = n >= 3
                                           Next
                                       End Sub
        Return _shapeBar
    End Function

    ''' <summary>選取的單位：群組算一個（目前所在那一層的群組），其餘每個圖層一個；鎖定的不算。</summary>
    Private Function SelectionUnits() As List(Of List(Of Integer))
        Dim entered = EnteredGroup()
        Dim units As New List(Of List(Of Integer))()
        Dim byGroup As New Dictionary(Of String, List(Of Integer))()
        For Each i In SelectedIndices()
            Dim d = _recipe.Drawings(i)
            If d.Locked OrElse d.Shape = DrawShape.FillLayer Then Continue For
            Dim g = DrawGroups.ChildGroupAt(_recipe, d, entered)
            If g Is Nothing Then
                units.Add(New List(Of Integer) From {i})
            Else
                Dim lst As List(Of Integer) = Nothing
                If Not byGroup.TryGetValue(g, lst) Then lst = New List(Of Integer)() : byGroup(g) = lst : units.Add(lst)
                lst.Add(i)
            End If
        Next
        Return units.Where(Function(u) Not UnitBounds(u).IsEmpty).ToList()
    End Function

    Private Function UnitBounds(unit As IEnumerable(Of Integer)) As RectangleF
        Return DrawGeometry.GroupBounds(unit.Select(Function(i) _recipe.Drawings(i)))
    End Function

    ''' <summary>可以焊接修剪的選取（由下而上）；不能時回傳 Nothing。</summary>
    Private Function CombinableSelection() As List(Of Integer)
        Dim idx = SelectedIndices()
        If idx.Count < 2 Then Return Nothing
        If Not idx.All(Function(i) Not _recipe.Drawings(i).Locked AndAlso PathBoolean.CanCombine(_recipe.Drawings(i))) Then Return Nothing
        Return idx.OrderBy(Function(i) i).ToList()
    End Function

    Private Sub UpdateShapeBar()
        If _shapeButtons.Count = 0 Then Return
        If _builderOn AndAlso (_builderSources Is Nothing OrElse Not _builderSources.All(Function(d) _recipe.Drawings IsNot Nothing AndAlso _recipe.Drawings.Contains(d))) Then
            ExitBuilder() ' 復原、刪除之後原本的圖形不在了
        End If
        Dim picking = _drawStrip.SelectedTool = DrawIcons.SelectTool
        Dim units = If(picking, SelectionUnits(), New List(Of List(Of Integer))())
        For Each k In {"left", "hcenter", "top", "more"}
            _shapeButtons(k).Visible = picking AndAlso units.Count >= 1 AndAlso Not _builderOn
        Next
        Dim comb = picking AndAlso CombinableSelection() IsNot Nothing
        For Each k In OpKeys
            _shapeButtons(k).Visible = comb AndAlso Not _builderOn
        Next
        ' 結合、打散（CorelDRAW）
        _shapeButtons("combine").Visible = picking AndAlso Not _builderOn AndAlso CurveCombineSelection() IsNot Nothing
        _shapeButtons("breakapart").Visible = picking AndAlso Not _builderOn AndAlso BreakApartCandidate() >= 0
        ' PowerClip、照片放進形狀
        _shapeButtons("clipin").Visible = picking AndAlso Not _builderOn AndAlso ClipCandidate().HasValue
        _shapeButtons("clipout").Visible = picking AndAlso Not _builderOn AndAlso SelectedClipGroups().Count > 0
        Dim pc = If(picking AndAlso Not _builderOn, PhotoClipCandidate(), Nothing)
        _shapeButtons("photoclip").Visible = pc IsNot Nothing
        _shapeButtons("outline").Visible = picking AndAlso Not _builderOn AndAlso OutlineCandidates().Count > 0
        _shapeButtons("effects").Visible = picking AndAlso Not _builderOn AndAlso EffectTargets().Count > 0
        If pc IsNot Nothing Then _shapeButtons("photoclip").Text = If(pc.Id IsNot Nothing AndAlso _recipe.PhotoClip = pc.Id, Lang.T("取消照片裁切"), Lang.T("照片放進形狀"))
        ' 分隔
        Dim seps = _shapeBar.Controls.OfType(Of Panel)().ToList()
        If seps.Count >= 2 Then
            seps(0).Visible = _shapeButtons("left").Visible AndAlso _shapeButtons("weld").Visible
            seps(1).Visible = (_shapeButtons("left").Visible OrElse _shapeButtons("weld").Visible) AndAlso
                              (_shapeButtons("combine").Visible OrElse _shapeButtons("breakapart").Visible OrElse _shapeButtons("clipin").Visible OrElse _shapeButtons("clipout").Visible OrElse _shapeButtons("photoclip").Visible)
        End If
    End Sub

    Private Sub ShapeCommand(key As String)
        Select Case key
            Case "left", "hcenter", "right", "top", "vcenter", "bottom" : AlignSelected(key)
            Case "disth" : DistributeSelected(True)
            Case "distv" : DistributeSelected(False)
            Case "weld" : CombineSelected(ShapeOp.Weld)
            Case "trim" : CombineSelected(ShapeOp.Trim)
            Case "intersect" : CombineSelected(ShapeOp.Intersect)
            Case "exclude" : CombineSelected(ShapeOp.Exclude)
            Case "builder" : If _builderOn Then ExitBuilder() Else EnterBuilder()
            Case "more"
                If _shapeButtons("more").Visible Then _alignMenu.Show(_shapeButtons("more"), New Point(0, _shapeButtons("more").Height)) Else _alignMenu.Show(Cursor.Position)
            Case "combine" : CombineCurves()
            Case "breakapart" : BreakApartSelected()
            Case "clipin" : ClipIntoShape()
            Case "clipout" : ClipOut()
            Case "photoclip" : TogglePhotoClip()
            Case "outline" : OutlineSelected() ' 外框轉物件（見 frmEditor.DrawBatch4.vb）
            Case "effects" : ShowEffectsMenu() ' 輪廓圖、變形、立體化、簡化（見 frmEditor.DrawBatch5.vb）
        End Select
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 對齊、均分
    '---------------------------------------------------------------------

    ''' <summary>對齊：選兩個以上對齊彼此的外框；只選一個對齊照片。</summary>
    Private Sub AlignSelected(key As String)
        Dim units = SelectionUnits()
        If units.Count = 0 Then Return
        Dim target = If(units.Count = 1, New RectangleF(0, 0, CSng(PhotoAspect()), 1),
                        DrawGeometry.GroupBounds(units.SelectMany(Function(u) u).Select(Function(i) _recipe.Drawings(i))))
        Dim moves As New List(Of (Unit As List(Of Integer), Dx As Double, Dy As Double))()
        For Each u In units
            Dim b = UnitBounds(u)
            Dim dx = 0.0, dy = 0.0
            Select Case key
                Case "left" : dx = target.Left - b.Left
                Case "right" : dx = target.Right - b.Right
                Case "hcenter" : dx = (target.Left + target.Right) / 2 - (b.Left + b.Right) / 2
                Case "top" : dy = target.Top - b.Top
                Case "bottom" : dy = target.Bottom - b.Bottom
                Case "vcenter" : dy = (target.Top + target.Bottom) / 2 - (b.Top + b.Bottom) / 2
            End Select
            If Math.Abs(dx) > 0.0000001 OrElse Math.Abs(dy) > 0.0000001 Then moves.Add((u, dx, dy))
        Next
        If moves.Count = 0 Then Return
        ApplyChange(Sub(r)
                        For Each m In moves
                            For Each i In m.Unit
                                DrawGeometry.Offset(r.Drawings(i), m.Dx, m.Dy)
                            Next
                        Next
                    End Sub)
    End Sub

    ''' <summary>均分：最外面兩個不動，中間的排成間距一樣。</summary>
    Private Sub DistributeSelected(horizontal As Boolean)
        Dim units = SelectionUnits()
        If units.Count < 3 Then Return
        Dim items = units.Select(Function(u) (Unit:=u, B:=UnitBounds(u))).
                          OrderBy(Function(x) If(horizontal, x.B.Left + x.B.Width / 2, x.B.Top + x.B.Height / 2)).ToList()
        Dim first = items(0).B, last = items(items.Count - 1).B
        Dim span = If(horizontal, last.Right - first.Left, last.Bottom - first.Top)
        Dim total = items.Sum(Function(x) CDbl(If(horizontal, x.B.Width, x.B.Height)))
        Dim gap = (span - total) / (items.Count - 1)
        Dim pos = CDbl(If(horizontal, first.Right, first.Bottom)) + gap
        Dim moves As New List(Of (Unit As List(Of Integer), D As Double))()
        For k = 1 To items.Count - 2
            Dim b = items(k).B
            moves.Add((items(k).Unit, pos - If(horizontal, b.Left, b.Top)))
            pos += If(horizontal, b.Width, b.Height) + gap
        Next
        ApplyChange(Sub(r)
                        For Each m In moves
                            For Each i In m.Unit
                                DrawGeometry.Offset(r.Drawings(i), If(horizontal, m.D, 0), If(horizontal, 0, m.D))
                            Next
                        Next
                    End Sub)
    End Sub

    '---------------------------------------------------------------------
    ' 焊接、修剪、相交、排除
    '---------------------------------------------------------------------

    Private Sub CombineSelected(op As ShapeOp)
        Dim idx = CombinableSelection()
        If idx Is Nothing Then
            SetStatusMessage(Lang.T("焊接、修剪要選兩個以上的封閉向量圖形（圖說、點陣圖層、填滿圖層、開放的線不行）。"))
            Return
        End If
        Dim result = PathBoolean.Apply(idx.Select(Function(i) _recipe.Drawings(i)).ToList(), op)
        If result Is Nothing Then
            SetStatusMessage(If(op = ShapeOp.Intersect, Lang.T("這些圖形沒有全部重疊的地方。"), Lang.T("結果是空的（全部被減掉了）。")))
            Return
        End If
        Dim keep = If(op = ShapeOp.Trim, idx.Min(), idx.Max())
        ApplyChange(Sub(r)
                        r.Drawings(keep) = result
                        For Each i In idx.Where(Function(x) x <> keep).OrderByDescending(Function(x) x)
                            r.Drawings.RemoveAt(i)
                        Next
                        DrawGroups.Prune(r, EnteredGroup())
                    End Sub)
        SelectDrawLayer(_recipe.Drawings.IndexOf(result))
        SetStatusMessage({Lang.T("已焊接"), Lang.T("已修剪"), Lang.T("已相交"), Lang.T("已排除")}(CInt(op)) & Lang.T("：結果是鋼筆路徑，可以用節點工具再修改。"))
    End Sub

    '---------------------------------------------------------------------
    ' 形狀建立
    '---------------------------------------------------------------------

    Private Sub EnterBuilder()
        Dim idx = CombinableSelection()
        If idx Is Nothing Then Return
        _builderSources = idx.Select(Function(i) _recipe.Drawings(i)).ToList()
        _builderFaces = PathBoolean.Faces(_builderSources.Select(Function(d) PathBoolean.RegionOf(d)).ToList())
        _builderOn = True
        _builderHover = -1
        _builderPicked.Clear()
        UpdateDrawHint()
    End Sub

    Private Sub ExitBuilder()
        If Not _builderOn Then Return
        _builderOn = False
        _builderSources = Nothing
        _builderFaces = Nothing
        _builderPicked.Clear()
        _builderDragging = False
        _builderHover = -1
        UpdateDrawHint()
        _canvas.Invalidate()
    End Sub

    Private Function FaceAt(u As PointF) As Integer
        If _builderFaces Is Nothing Then Return -1
        For i = 0 To _builderFaces.Count - 1
            If _builderFaces(i).Bounds.Contains(u) AndAlso PathBoolean.Contains(_builderFaces(i).Rings, u) Then Return i
        Next
        Return -1
    End Function

    Private Function BuilderMouseDown(e As MouseEventArgs, u As PointF) As Boolean
        If Not _builderOn Then Return False
        If e.Button <> MouseButtons.Left Then Return True
        Dim f = FaceAt(u)
        If f < 0 Then
            ExitBuilder() ' 點外面：離開形狀建立
            UpdateDrawControls()
            Return True
        End If
        _builderDragging = True
        _builderDelete = ModifierKeys.HasFlag(Keys.Alt)
        _builderPicked.Clear()
        _builderPicked.Add(f)
        _canvas.Invalidate()
        Return True
    End Function

    Private Function BuilderMouseMove(u As PointF) As Boolean
        If Not _builderOn Then Return False
        Dim f = FaceAt(u)
        If _builderDragging AndAlso f >= 0 AndAlso Not _builderPicked.Contains(f) Then _builderPicked.Add(f)
        If f <> _builderHover OrElse _builderDragging Then
            _builderHover = f
            _canvas.Invalidate()
        End If
        Dim c = If(f >= 0, Cursors.Hand, Cursors.Default)
        If _canvas.Cursor IsNot c Then _canvas.Cursor = c
        Return True
    End Function

    Private Function BuilderMouseUp() As Boolean
        If Not _builderOn Then Return False
        If _builderDragging Then
            _builderDragging = False
            BuilderCommit(_builderPicked.ToList(), _builderDelete)
        End If
        Return True
    End Function

    ''' <summary>
    ''' 合併（或刪掉）選到的幾塊：原本的圖形換成切出來的每一塊（各用最上面那個圖形的樣式），合併的那塊用拖曳起點那塊的樣式。
    ''' 結果全部選取，繼續留在形狀建立。
    ''' </summary>
    Private Sub BuilderCommit(picked As List(Of Integer), delete As Boolean)
        If _builderFaces Is Nothing OrElse picked.Count = 0 Then Return
        Dim srcIdx = _builderSources.Select(Function(d) _recipe.Drawings.IndexOf(d)).ToList()
        If srcIdx.Any(Function(i) i < 0) Then ExitBuilder() : Return
        Dim styleOf = Function(mask As Integer) _builderSources(CInt(Math.Floor(Math.Log(mask, 2) + 0.0000001)))
        Dim pieces As New List(Of DrawLayer)()
        For i = 0 To _builderFaces.Count - 1
            If picked.Contains(i) Then Continue For
            Dim f = _builderFaces(i)
            Dim d = PathBoolean.MakeLayer(styleOf(f.Mask), f.Rings)
            If d IsNot Nothing Then pieces.Add(d)
        Next
        If Not delete Then
            Dim regions = picked.Select(Function(i) _builderFaces(i).Rings).ToList()
            Dim merged = If(regions.Count = 1, regions(0), PathBoolean.Combine(regions, Function(v) v.Any(Function(x) x)))
            Dim d = PathBoolean.MakeLayer(styleOf(_builderFaces(picked(0)).Mask), merged)
            If d IsNot Nothing Then pieces.Add(d)
        End If
        Dim top = srcIdx.Max()
        Dim baseName = _builderSources(_builderSources.Count - 1).Name
        For k = 0 To pieces.Count - 1
            pieces(k).Name = If(String.IsNullOrEmpty(pieces(k).Name), baseName, pieces(k).Name)
        Next
        Dim topId = _builderSources(_builderSources.Count - 1).Id
        ApplyChange(Sub(r)
                        Dim at = top
                        For Each i In srcIdx.OrderByDescending(Function(x) x)
                            r.Drawings.RemoveAt(i)
                            If i < top Then at -= 1
                        Next
                        InsertPieces(r, at, pieces, topId) ' 堆疊位置沿用最上面那個圖形
                        DrawGroups.Prune(r, EnteredGroup())
                    End Sub)
        Dim newIdx = pieces.Select(Function(p) _recipe.Drawings.IndexOf(p)).Where(Function(i) i >= 0).ToList()
        _builderOn = False ' 選取改變時不要自己離開
        If newIdx.Count >= 2 Then
            SetMultiSelection(newIdx, newIdx.Max())
        Else
            SelectDrawLayer(If(newIdx.Count = 1, newIdx(0), -1))
        End If
        _builderPicked.Clear()
        If newIdx.Count >= 2 AndAlso newIdx.Count <= 64 Then
            _builderSources = pieces
            _builderFaces = PathBoolean.Faces(pieces.Select(Function(d) PathBoolean.RegionOf(d)).ToList())
            _builderOn = True
        Else
            ExitBuilder()
        End If
        UpdateDrawControls()
        UpdateDrawHint()
        _canvas.Invalidate()
    End Sub

    Private Function BuilderKey(keyData As Keys) As Boolean
        If Not _builderOn Then Return False
        If keyData = Keys.Escape OrElse keyData = Keys.Enter Then
            ExitBuilder()
            UpdateDrawControls()
            Return True
        End If
        Return False
    End Function

    Private Sub PaintBuilder(g As Graphics)
        If Not _builderOn OrElse _builderFaces Is Nothing Then Return
        Dim toPath = Function(f As ShapeFace)
                         Dim p As New GraphicsPath(FillMode.Winding)
                         For Each r In f.Rings
                             p.AddPolygon(r.Select(Function(q) UnitToScreen(q)).ToArray())
                         Next
                         Return p
                     End Function
        For i = 0 To _builderFaces.Count - 1
            Using p = toPath(_builderFaces(i))
                Dim picked = _builderPicked.Contains(i) AndAlso _builderDragging
                If picked OrElse i = _builderHover Then
                    Dim c = If(_builderDelete AndAlso _builderDragging, Color.FromArgb(110, 226, 75, 74), Color.FromArgb(If(picked, 120, 80), 55, 138, 221))
                    Using hb As New HatchBrush(HatchStyle.WideUpwardDiagonal, c, Color.FromArgb(c.A \ 2, c))
                        g.FillPath(hb, p)
                    End Using
                End If
                Using pen As New Pen(Color.FromArgb(200, 90, 90, 90), 1) With {.DashStyle = DashStyle.Dash}
                    g.DrawPath(pen, p)
                End Using
            End Using
        Next
    End Sub

    '---------------------------------------------------------------------
    ' 智慧參考線
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 移動中的外框（已加上 dx、dy）靠近其他物件或照片的邊、中心（螢幕 6 像素內）時吸過去，記下要畫的參考線。
    ''' exclude＝正在移動的圖層。按住 Alt 不吸附。
    ''' </summary>
    Private Sub SnapMove(moving As RectangleF, exclude As ICollection(Of Integer), ByRef dx As Double, ByRef dy As Double)
        _guides.Clear()
        If ModifierKeys.HasFlag(Keys.Alt) OrElse moving.IsEmpty Then Return
        Dim tol = 6 / Math.Max(0.0001, PxPerUnit())
        Dim m = New RectangleF(CSng(moving.X + dx), CSng(moving.Y + dy), moving.Width, moving.Height)
        Dim targets As New List(Of RectangleF) From {New RectangleF(0, 0, CSng(PhotoAspect()), 1)}
        targets.AddRange(LayoutSnapTargets(m)) ' 參考線、格線（見 frmEditor.DrawBatch6.vb）
        For i = 0 To LayerCount() - 1
            Dim d = _recipe.Drawings(i)
            If exclude.Contains(i) OrElse Not d.Visible OrElse d.Shape = DrawShape.FillLayer OrElse d.Shape = DrawShape.Raster Then Continue For
            Dim b = DrawGeometry.Bounds(d)
            If Not b.IsEmpty Then targets.Add(b)
        Next
        Dim xs = {m.Left, m.Left + m.Width / 2, m.Right}, ys = {m.Top, m.Top + m.Height / 2, m.Bottom}
        Dim bestX = tol, bestY = tol, sx = 0.0, sy = 0.0
        Dim gx As Single = Single.NaN, gy As Single = Single.NaN
        For Each t In targets
            For Each tx In {t.Left, t.Left + t.Width / 2, t.Right}
                For Each x In xs
                    If Math.Abs(tx - x) < bestX Then bestX = Math.Abs(tx - x) : sx = tx - x : gx = tx
                Next
            Next
            For Each ty In {t.Top, t.Top + t.Height / 2, t.Bottom}
                For Each y In ys
                    If Math.Abs(ty - y) < bestY Then bestY = Math.Abs(ty - y) : sy = ty - y : gy = ty
                Next
            Next
        Next
        dx += sx : dy += sy
        Dim fm = New RectangleF(CSng(moving.X + dx), CSng(moving.Y + dy), moving.Width, moving.Height)
        ' 參考線畫到所有對齊這條線的物件
        If Not Single.IsNaN(gx) Then
            Dim lo = fm.Top, hi = fm.Bottom
            For Each t In targets
                If {t.Left, t.Left + t.Width / 2, t.Right}.Any(Function(v) Math.Abs(v - gx) < 0.00001) Then lo = Math.Min(lo, t.Top) : hi = Math.Max(hi, t.Bottom)
            Next
            _guides.Add((True, gx, lo, hi))
        End If
        If Not Single.IsNaN(gy) Then
            Dim lo = fm.Left, hi = fm.Right
            For Each t In targets
                If {t.Top, t.Top + t.Height / 2, t.Bottom}.Any(Function(v) Math.Abs(v - gy) < 0.00001) Then lo = Math.Min(lo, t.Left) : hi = Math.Max(hi, t.Right)
            Next
            _guides.Add((False, gy, lo, hi))
        End If
    End Sub

    Private Sub PaintGuides(g As Graphics)
        If _guides.Count = 0 OrElse (_dd <> DrawDrag.Move AndAlso _dd <> DrawDrag.GroupMove) Then Return
        Using pen As New Pen(Color.FromArgb(230, 212, 83, 126), 1)
            For Each gd In _guides
                Dim a = If(gd.Vertical, UnitToScreen(New PointF(gd.Pos, gd.A)), UnitToScreen(New PointF(gd.A, gd.Pos)))
                Dim b = If(gd.Vertical, UnitToScreen(New PointF(gd.Pos, gd.B)), UnitToScreen(New PointF(gd.B, gd.Pos)))
                g.DrawLine(pen, a, b)
            Next
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 按鈕圖示
    '---------------------------------------------------------------------

    Private Shared Sub PaintShapeGlyph(g As Graphics, key As String, b As Button)
        Dim col = If(b.Enabled, Color.FromArgb(50, 54, 62), Color.FromArgb(160, 160, 160))
        If key = "builder" AndAlso b.Tag IsNot Nothing AndAlso CBool(b.Tag) Then col = Color.FromArgb(24, 95, 165)
        Dim state = g.Save()
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim k = Math.Min(b.Width, b.Height) / 28.0F
        g.TranslateTransform((b.Width - 20 * k) / 2, (b.Height - 20 * k) / 2)
        g.ScaleTransform(k, k)
        Using pen As New Pen(col, 1.4F), br As New SolidBrush(col), soft As New SolidBrush(Color.FromArgb(120, col))
            Select Case key
                Case "left" : g.DrawLine(pen, 3, 2, 3, 18) : g.FillRectangle(br, 4, 5, 12, 3.5F) : g.FillRectangle(br, 4, 11.5F, 7, 3.5F)
                Case "hcenter" : g.DrawLine(pen, 10, 2, 10, 18) : g.FillRectangle(soft, 4, 5, 12, 3.5F) : g.FillRectangle(soft, 6.5F, 11.5F, 7, 3.5F)
                Case "right" : g.DrawLine(pen, 17, 2, 17, 18) : g.FillRectangle(br, 4, 5, 12, 3.5F) : g.FillRectangle(br, 9, 11.5F, 7, 3.5F)
                Case "top" : g.DrawLine(pen, 2, 3, 18, 3) : g.FillRectangle(br, 5, 4, 3.5F, 12) : g.FillRectangle(br, 11.5F, 4, 3.5F, 7)
                Case "vcenter" : g.DrawLine(pen, 2, 10, 18, 10) : g.FillRectangle(soft, 5, 4, 3.5F, 12) : g.FillRectangle(soft, 11.5F, 6.5F, 3.5F, 7)
                Case "bottom" : g.DrawLine(pen, 2, 17, 18, 17) : g.FillRectangle(br, 5, 4, 3.5F, 12) : g.FillRectangle(br, 11.5F, 9, 3.5F, 7)
                Case "more"
                    For Each x In {5.0F, 10.0F, 15.0F}
                        g.FillEllipse(br, x - 1.6F, 8.4F, 3.2F, 3.2F)
                    Next
                Case "disth"
                    g.FillRectangle(br, 2, 6, 3.5F, 8) : g.FillRectangle(br, 8.25F, 3, 3.5F, 14) : g.FillRectangle(br, 14.5F, 6, 3.5F, 8)
                    Using thin As New Pen(col, 1) : g.DrawLine(thin, 2, 19, 18, 19) : End Using
                Case "distv"
                    g.FillRectangle(br, 6, 2, 8, 3.5F) : g.FillRectangle(br, 3, 8.25F, 14, 3.5F) : g.FillRectangle(br, 6, 14.5F, 8, 3.5F)
                    Using thin As New Pen(col, 1) : g.DrawLine(thin, 19, 2, 19, 18) : End Using
                Case "weld", "trim", "intersect", "exclude", "builder"
                    Using pa As New GraphicsPath(), pb As New GraphicsPath()
                        pa.AddRectangle(New RectangleF(2, 2, 11, 11))
                        pb.AddEllipse(New RectangleF(7, 7, 11, 11))
                        Using ra As New Region(pa)
                            Select Case key
                                Case "weld" : ra.Union(pb)
                                Case "trim" : ra.Exclude(pb)
                                Case "intersect" : ra.Intersect(pb)
                                Case "exclude" : ra.Xor(pb)
                                Case "builder" : ra.Intersect(pb)
                            End Select
                            g.FillRegion(If(key = "builder", soft, br), ra)
                        End Using
                        Using thin As New Pen(col, 1) With {.DashStyle = If(key = "weld", DashStyle.Solid, DashStyle.Dot)}
                            If key <> "weld" Then g.DrawPath(thin, pa) : g.DrawPath(thin, pb)
                        End Using
                        If key = "builder" Then g.DrawLine(pen, 15, 2, 15, 8) : g.DrawLine(pen, 12, 5, 18, 5)
                    End Using
            End Select
        End Using
        g.Restore(state)
        If key = "builder" AndAlso b.Tag IsNot Nothing AndAlso CBool(b.Tag) Then
            Using frame As New Pen(Color.FromArgb(90, 140, 220), 2)
                g.DrawRectangle(frame, 1, 1, b.Width - 3, b.Height - 3)
            End Using
        End If
    End Sub
End Class
