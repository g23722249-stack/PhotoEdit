Imports System.Drawing.Drawing2D
Imports PhotoEdit

' 互動式填色與網格填色（同 CorelDRAW 的互動式填色工具、網格填色工具）。核心在 PhotoEdit.Core\MeshFill.vb、GradientRender.vb。
' 互動式填色：在圖形上拖一條線＝線性漸層（填色 → 第二色）；拖起點、終點、色標改位置；在漸層線上按兩下加色標；
'   點選一個控制點後點色盤換它的顏色；Del 刪掉選取的色標。
' 網格填色：點一個圖形加上 3×3 的網格；點節點（Shift 多選）、拖曳移動；選好節點點色盤換顏色；上方改列數、欄數或移除。
Partial Class frmEditor

    ' ---- 互動式填色 ----
    Private _ifSel As Integer = -1   ' 選取的控制點：0 起點、1 終點、2 起是色標
    Private _ifDrag As Integer        ' 0 沒有、1 拖控制點、2 拖出新漸層
    Private _ifIndex As Integer = -1
    Private _ifStart As PointF
    Private _ifStartPx As Point
    ' ---- 網格填色 ----
    Private ReadOnly _meshSel As New HashSet(Of Integer)()
    Private _meshDrag As Boolean
    Private _meshLast As PointF
    Private ReadOnly _meshBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                           .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0)}
    Private ReadOnly _meshRows As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 44, .Margin = New Padding(0, 3, 4, 0)}
    Private ReadOnly _meshCols As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 44, .Margin = New Padding(0, 3, 4, 0)}
    Private _meshRemove As Button
    Private _meshSyncing As Boolean

    Private ReadOnly Property InteractiveFillActive As Boolean
        Get
            Return _drawStrip.SelectedTool = DrawIcons.InteractiveFillTool
        End Get
    End Property

    Private ReadOnly Property MeshToolActive As Boolean
        Get
            Return _drawStrip.SelectedTool = DrawIcons.MeshFillTool
        End Get
    End Property

    Private Function BuildMeshBar() As Control
        For Each cb In {_meshRows, _meshCols}
            cb.Items.AddRange(Enumerable.Range(2, 7).Select(Function(v) CObj(v.ToString())).ToArray())
        Next
        _help.SetHelp("draw.mesh.rows", _meshRows)
        _help.SetHelp("draw.mesh.cols", _meshCols)
        _meshRemove = MakeButton(Lang.T("移除網格"), "draw.mesh.remove")
        _meshRemove.Height = 28 : _meshRemove.AutoSize = True : _meshRemove.AutoSizeMode = AutoSizeMode.GrowAndShrink : _meshRemove.Margin = New Padding(0, 0, 4, 0)
        AddHandler _meshRemove.Click, Sub() SetMesh(Sub(d) d.FillMesh = Nothing)
        Dim resample = Sub()
                           If _meshSyncing OrElse _meshRows.SelectedIndex < 0 OrElse _meshCols.SelectedIndex < 0 Then Return
                           Dim rows = _meshRows.SelectedIndex + 2, cols = _meshCols.SelectedIndex + 2
                           _meshSel.Clear()
                           SetMesh(Sub(d) d.FillMesh = d.FillMesh.Resample(rows, cols))
                       End Sub
        AddHandler _meshRows.SelectedIndexChanged, Sub() resample()
        AddHandler _meshCols.SelectedIndexChanged, Sub() resample()
        Dim lbl = Function(t As String) New Label With {.Text = t, .AutoSize = True, .Margin = New Padding(2, 7, 1, 0), .BackColor = Color.Transparent}
        _meshBar.Controls.AddRange({lbl(Lang.T("網格 列")), _meshRows, lbl(Lang.T("欄")), _meshCols, _meshRemove})
        _meshBar.Visible = False
        Return _meshBar
    End Function

    Private Sub UpdateMeshBar()
        Dim sel = If(MeshToolActive AndAlso Not IsMultiSelected(), SelDraw(_recipe), Nothing)
        _meshBar.Visible = sel IsNot Nothing AndAlso sel.FillMesh IsNot Nothing
        If Not _meshBar.Visible Then Return
        _meshSyncing = True
        Try
            _meshRows.SelectedIndex = Math.Max(0, Math.Min(6, sel.FillMesh.Rows - 2))
            _meshCols.SelectedIndex = Math.Max(0, Math.Min(6, sel.FillMesh.Cols - 2))
        Finally
            _meshSyncing = False
        End Try
    End Sub

    Private Sub SetMesh(change As Action(Of DrawLayer))
        Dim i = _drawIndex
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing OrElse sel.FillMesh Is Nothing Then Return
        ApplyChange(Sub(r) change(r.Drawings(i)))
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    ''' <summary>能不能填色（封閉的向量圖形，不是文字、點陣、填滿圖層）。</summary>
    Private Shared Function Fillable(d As DrawLayer) As Boolean
        If d Is Nothing OrElse DrawGeometry.HasText(d.Shape) Then Return False
        Select Case d.Shape
            Case DrawShape.Raster, DrawShape.Bucket, DrawShape.Gradient, DrawShape.FillLayer, DrawShape.Freehand : Return False
        End Select
        Return DrawGeometry.IsClosedLayer(d)
    End Function

    '---------------------------------------------------------------- 互動式填色

    ''' <summary>漸層的控制點（螢幕座標）：起點、終點、各色標（依位置排序後的原索引）。</summary>
    Private Function IfHandles() As (A As PointF, B As PointF, Stops As List(Of (Index As Integer, P As PointF)), Layer As DrawLayer)?
        If IsMultiSelected() Then Return Nothing
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing OrElse sel.FillGradient Is Nothing OrElse sel.FillMesh IsNot Nothing OrElse Not sel.Filled OrElse Not sel.Visible Then Return Nothing
        If sel.FillGradient.Kind = GradientKind.FourColor Then Return Nothing
        Dim g = DrawingRenderer.AbsoluteGradient(sel)
        Dim a = UnitToScreen(New PointF(CSng(g.X1), CSng(g.Y1))), b = UnitToScreen(New PointF(CSng(g.X2), CSng(g.Y2)))
        Dim stops As New List(Of (Index As Integer, P As PointF))()
        If sel.FillGradient.Stops IsNot Nothing Then
            For k = 0 To sel.FillGradient.Stops.Count - 1
                Dim t = sel.FillGradient.Stops(k).Position
                stops.Add((k, New PointF(CSng(a.X + (b.X - a.X) * t), CSng(a.Y + (b.Y - a.Y) * t))))
            Next
        End If
        Return (a, b, stops, sel)
    End Function

    Private Function IfMouseDown(e As MouseEventArgs, u As PointF) As Boolean
        If e.Button <> MouseButtons.Left Then Return True
        Dim h = IfHandles()
        If h.HasValue AndAlso Not h.Value.Layer.Locked Then
            Dim hv = h.Value
            For Each s In hv.Stops
                If ScreenDist(s.P, e.Location) <= HandleRadius Then _ifSel = 2 + s.Index : _ifDrag = 1 : _ddSerial += 1 : _canvas.Invalidate() : Return True
            Next
            If ScreenDist(hv.B, e.Location) <= HandleRadius + 1 Then _ifSel = 1 : _ifDrag = 1 : _ddSerial += 1 : _canvas.Invalidate() : Return True
            If ScreenDist(hv.A, e.Location) <= HandleRadius + 1 Then _ifSel = 0 : _ifDrag = 1 : _ddSerial += 1 : _canvas.Invalidate() : Return True
        End If
        Dim hit = FxTarget(u)
        If hit < 0 Then
            _ifSel = -1
            SelectDrawLayer(-1)
            Return True
        End If
        Dim d = _recipe.Drawings(hit)
        If Not Fillable(d) OrElse d.Locked Then
            SetStatusMessage(Lang.T("互動式填色只能用在封閉的向量圖形上（文字、點陣圖層、線條不行）。"))
            Return True
        End If
        If hit <> _drawIndex OrElse IsMultiSelected() Then SelectDrawLayer(hit)
        _ifSel = -1
        _ifDrag = 2
        _ifIndex = hit
        _ifStart = u
        _ifStartPx = e.Location
        _ddSerial += 1
        Return True
    End Function

    Private Function IfMouseMove(e As MouseEventArgs, u As PointF) As Boolean
        If _ifDrag = 0 Then
            Dim h = IfHandles()
            Dim onHandle = h.HasValue AndAlso (ScreenDist(h.Value.A, e.Location) <= HandleRadius + 1 OrElse ScreenDist(h.Value.B, e.Location) <= HandleRadius + 1 OrElse
                                               h.Value.Stops.Any(Function(s) ScreenDist(s.P, e.Location) <= HandleRadius))
            Dim c = If(onHandle, Cursors.Hand, If(FxTarget(u) >= 0, Cursors.Cross, Cursors.Default))
            If _canvas.Cursor IsNot c Then _canvas.Cursor = c
            Return True
        End If
        Dim index = If(_ifDrag = 2, _ifIndex, _drawIndex)
        If index < 0 OrElse index >= LayerCount() Then Return True
        Dim d = _recipe.Drawings(index)
        If _ifDrag = 2 Then
            If ScreenDist(_ifStartPx, e.Location) < 4 Then Return True
            Dim fa = DrawGeometry.GradientFraction(d, _ifStart), fb = DrawGeometry.GradientFraction(d, u)
            ApplyChange(Sub(r)
                            Dim x = r.Drawings(index)
                            Dim g = If(x.FillGradient, New GradientFill With {.Kind = GradientKind.Linear})
                            If g.Kind = GradientKind.FourColor Then g.Kind = GradientKind.Linear
                            g.Colors = GradientColors.StrokeToFill
                            g.X1 = Math.Round(fa.X, 4) : g.Y1 = Math.Round(fa.Y, 4) : g.X2 = Math.Round(fb.X, 4) : g.Y2 = Math.Round(fb.Y, 4)
                            x.FillGradient = g
                            x.FillMesh = Nothing
                            x.FillPattern = Nothing
                            x.Filled = True
                        End Sub, "ifill-" & _ddSerial)
        ElseIf _ifSel <= 1 Then
            Dim fr = DrawGeometry.GradientFraction(d, u)
            Dim which = _ifSel
            ApplyChange(Sub(r)
                            Dim g = r.Drawings(index).FillGradient
                            If which = 0 Then g.X1 = Math.Round(fr.X, 4) : g.Y1 = Math.Round(fr.Y, 4) Else g.X2 = Math.Round(fr.X, 4) : g.Y2 = Math.Round(fr.Y, 4)
                        End Sub, "ifill-" & _ddSerial)
        Else
            ' 色標沿著漸層線滑動
            Dim h = IfHandles()
            If Not h.HasValue Then Return True
            Dim t = ProjectT(h.Value.A, h.Value.B, e.Location)
            Dim k = _ifSel - 2
            ApplyChange(Sub(r)
                            Dim st = r.Drawings(index).FillGradient.Stops
                            If k < st.Count Then st(k).Position = Math.Round(Math.Max(0.01, Math.Min(0.99, t)), 4)
                        End Sub, "ifill-" & _ddSerial)
        End If
        _canvas.Invalidate()
        Return True
    End Function

    Private Shared Function ProjectT(a As PointF, b As PointF, p As Point) As Double
        Dim dx = b.X - a.X, dy = b.Y - a.Y
        Dim len2 = Math.Max(0.0001, dx * dx + dy * dy)
        Return ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2
    End Function

    Private Function IfMouseUp() As Boolean
        If _ifDrag = 0 Then Return False
        _ifDrag = 0
        UpdateDrawControls()
        _canvas.Invalidate()
        Return True
    End Function

    ''' <summary>在漸層線上按兩下：加一個色標（顏色取那個位置現在的顏色），並選取它。</summary>
    Private Function IfDoubleClick(e As MouseEventArgs) As Boolean
        Dim h = IfHandles()
        If Not h.HasValue OrElse h.Value.Layer.Locked Then Return False
        Dim a = h.Value.A, b = h.Value.B
        Dim t = ProjectT(a, b, e.Location)
        If t <= 0.02 OrElse t >= 0.98 Then Return False
        Dim q = New PointF(CSng(a.X + (b.X - a.X) * t), CSng(a.Y + (b.Y - a.Y) * t))
        If ScreenDist(q, e.Location) > 6 Then Return False
        Dim index = _drawIndex
        Dim argb = DrawingRenderer.GradientColorAt(h.Value.Layer, t).ToArgb()
        Dim count = 0
        ApplyChange(Sub(r)
                        Dim g = r.Drawings(index).FillGradient
                        If g.Stops Is Nothing Then g.Stops = New List(Of GradientStop)()
                        g.Stops.Add(New GradientStop With {.Position = Math.Round(t, 4), .Argb = argb})
                        count = g.Stops.Count
                    End Sub)
        _ifSel = 2 + count - 1
        SetStatusMessage(Lang.T("加了一個色標：點色盤換它的顏色、拖曳移動，Del 刪除。"))
        UpdateDrawControls()
        _canvas.Invalidate()
        Return True
    End Function

    Private Sub PaintInteractiveFill(g As Graphics)
        Dim h = IfHandles()
        If Not h.HasValue Then Return
        Dim hv = h.Value
        Using outer As New Pen(Color.FromArgb(160, 0, 0, 0), 3), line As New Pen(Color.White, 1.4F) With {.DashStyle = DashStyle.Dash}
            g.DrawLine(outer, hv.A, hv.B) : g.DrawLine(line, hv.A, hv.B)
        End Using
        Dim gr = hv.Layer.FillGradient
        Using pen As New Pen(Color.FromArgb(40, 44, 52), 1.5F), selPen As New Pen(Color.FromArgb(255, 140, 0), 2.5F)
            Using b1 As New SolidBrush(Color.FromArgb(255, Color.FromArgb(hv.Layer.FillColorArgb)))
                g.FillEllipse(b1, hv.A.X - 6, hv.A.Y - 6, 12, 12) : g.DrawEllipse(If(_ifSel = 0, selPen, pen), hv.A.X - 6, hv.A.Y - 6, 12, 12)
            End Using
            Using b2 As New SolidBrush(Color.FromArgb(255, Color.FromArgb(hv.Layer.FillColor2Argb)))
                g.FillRectangle(b2, hv.B.X - 5.5F, hv.B.Y - 5.5F, 11, 11) : g.DrawRectangle(If(_ifSel = 1, selPen, pen), hv.B.X - 5.5F, hv.B.Y - 5.5F, 11, 11)
            End Using
            For Each s In hv.Stops
                Dim p = s.P
                Dim dia = {New PointF(p.X, p.Y - 6), New PointF(p.X + 6, p.Y), New PointF(p.X, p.Y + 6), New PointF(p.X - 6, p.Y)}
                Using bs As New SolidBrush(Color.FromArgb(255, Color.FromArgb(gr.Stops(s.Index).Argb)))
                    g.FillPolygon(bs, dia)
                End Using
                g.DrawPolygon(If(_ifSel = 2 + s.Index, selPen, pen), dia)
            Next
        End Using
    End Sub

    '---------------------------------------------------------------- 網格填色

    ''' <summary>網格節點的螢幕位置（選取的圖形有網格時）。</summary>
    Private Function MeshScreenPoints() As (Pts As PointF(), Layer As DrawLayer)?
        If IsMultiSelected() Then Return Nothing
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing OrElse sel.FillMesh Is Nothing OrElse Not sel.Visible Then Return Nothing
        Dim m = sel.FillMesh
        Dim pts(m.Count - 1) As PointF
        For i = 0 To m.Count - 1
            Dim f = m.PointAt(i)
            pts(i) = UnitToScreen(DrawGeometry.GradientPoint(sel, f.X, f.Y))
        Next
        Return (pts, sel)
    End Function

    Private Function MeshMouseDown(e As MouseEventArgs, u As PointF) As Boolean
        If e.Button <> MouseButtons.Left Then Return True
        Dim mp = MeshScreenPoints()
        If mp.HasValue AndAlso Not mp.Value.Layer.Locked Then
            Dim best = -1, bestD = Double.MaxValue
            For i = 0 To mp.Value.Pts.Length - 1
                Dim dd = ScreenDist(mp.Value.Pts(i), e.Location)
                If dd < bestD Then bestD = dd : best = i
            Next
            If best >= 0 AndAlso bestD <= HandleRadius + 1 Then
                If ModifierKeys.HasFlag(Keys.Shift) Then
                    If Not _meshSel.Remove(best) Then _meshSel.Add(best)
                ElseIf Not _meshSel.Contains(best) Then
                    _meshSel.Clear() : _meshSel.Add(best)
                End If
                _meshDrag = _meshSel.Contains(best)
                _meshLast = DrawGeometry.GradientFraction(mp.Value.Layer, u)
                _ddSerial += 1
                _canvas.Invalidate()
                Return True
            End If
        End If
        Dim hit = FxTarget(u)
        If hit < 0 Then
            _meshSel.Clear()
            SelectDrawLayer(-1)
            Return True
        End If
        Dim d = _recipe.Drawings(hit)
        If Not Fillable(d) OrElse d.Locked Then
            SetStatusMessage(Lang.T("網格填色只能用在封閉的向量圖形上（文字、點陣圖層、線條不行）。"))
            Return True
        End If
        If hit <> _drawIndex OrElse IsMultiSelected() Then SelectDrawLayer(hit)
        _meshSel.Clear()
        If d.FillMesh Is Nothing Then
            ApplyChange(Sub(r)
                            Dim x = r.Drawings(hit)
                            x.FillMesh = MeshFill.Create(3, 3, If(x.Filled, x.FillColorArgb, Color.White.ToArgb()))
                            x.Filled = True
                        End Sub)
            SetStatusMessage(Lang.T("加上了 3×3 的網格：點節點（Shift 多選）再點色盤換顏色，拖曳節點移動；上方可以改列數、欄數。"))
        End If
        UpdateDrawControls()
        _canvas.Invalidate()
        Return True
    End Function

    Private Function MeshMouseMove(e As MouseEventArgs, u As PointF) As Boolean
        If Not _meshDrag Then
            Dim mp = MeshScreenPoints()
            Dim onNode = mp.HasValue AndAlso mp.Value.Pts.Any(Function(p) ScreenDist(p, e.Location) <= HandleRadius + 1)
            Dim c = If(onNode, Cursors.Hand, If(FxTarget(u) >= 0, Cursors.Cross, Cursors.Default))
            If _canvas.Cursor IsNot c Then _canvas.Cursor = c
            Return True
        End If
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing OrElse sel.FillMesh Is Nothing Then Return True
        Dim f = DrawGeometry.GradientFraction(sel, u)
        Dim dx = f.X - _meshLast.X, dy = f.Y - _meshLast.Y
        _meshLast = f
        Dim index = _drawIndex
        Dim nodes = _meshSel.ToList()
        ApplyChange(Sub(r)
                        Dim m = r.Drawings(index).FillMesh
                        For Each i In nodes
                            If i < m.Count Then
                                Dim p = m.PointAt(i)
                                m.SetPoint(i, New PointF(p.X + dx, p.Y + dy))
                            End If
                        Next
                    End Sub, "mesh-" & _ddSerial)
        _canvas.Invalidate()
        Return True
    End Function

    Private Function MeshMouseUp() As Boolean
        If Not _meshDrag Then Return False
        _meshDrag = False
        UpdateDrawControls()
        _canvas.Invalidate()
        Return True
    End Function

    Private Sub PaintMesh(g As Graphics)
        Dim mp = MeshScreenPoints()
        If Not mp.HasValue Then Return
        Dim pts = mp.Value.Pts, m = mp.Value.Layer.FillMesh
        Using outer As New Pen(Color.FromArgb(150, 0, 0, 0), 2.6F), line As New Pen(Color.FromArgb(230, 255, 255, 255), 1.1F)
            For r = 0 To m.Rows - 1
                For c = 0 To m.Cols - 1
                    Dim i = r * m.Cols + c
                    If c < m.Cols - 1 Then g.DrawLine(outer, pts(i), pts(i + 1)) : g.DrawLine(line, pts(i), pts(i + 1))
                    If r < m.Rows - 1 Then g.DrawLine(outer, pts(i), pts(i + m.Cols)) : g.DrawLine(line, pts(i), pts(i + m.Cols))
                Next
            Next
        End Using
        Using pen As New Pen(Color.FromArgb(40, 44, 52), 1.4F), selPen As New Pen(Color.FromArgb(255, 140, 0), 2.6F)
            For i = 0 To pts.Length - 1
                Using br As New SolidBrush(Color.FromArgb(255, Color.FromArgb(m.Colors(i))))
                    g.FillRectangle(br, pts(i).X - 5, pts(i).Y - 5, 10, 10)
                End Using
                g.DrawRectangle(If(_meshSel.Contains(i), selPen, pen), pts(i).X - 5, pts(i).Y - 5, 10, 10)
            Next
        End Using
    End Sub

    '---------------------------------------------------------------- 接線

    Private Function FillToolMouseDown(e As MouseEventArgs, u As PointF) As Boolean
        If InteractiveFillActive Then Return IfMouseDown(e, u)
        If MeshToolActive Then Return MeshMouseDown(e, u)
        Return False
    End Function

    Private Function FillToolMouseMove(e As MouseEventArgs, u As PointF) As Boolean
        If InteractiveFillActive Then Return IfMouseMove(e, u)
        If MeshToolActive Then Return MeshMouseMove(e, u)
        Return False
    End Function

    Private Function FillToolMouseUp() As Boolean
        Return IfMouseUp() OrElse MeshMouseUp()
    End Function

    Private Function FillToolDoubleClick(e As MouseEventArgs) As Boolean
        Return InteractiveFillActive AndAlso IfDoubleClick(e)
    End Function

    ''' <summary>色盤點顏色：互動式填色選了控制點、網格選了節點時，換那些點的顏色。回傳 True＝已處理。</summary>
    Private Function FillToolColor(argb As Integer) As Boolean
        Dim index = _drawIndex
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        If sel Is Nothing OrElse sel.Locked Then Return False
        If InteractiveFillActive AndAlso sel.FillGradient IsNot Nothing AndAlso _ifSel >= 0 Then
            Dim which = _ifSel
            ApplyChange(Sub(r)
                            Dim d = r.Drawings(index)
                            If which = 0 Then
                                d.FillColorArgb = argb
                            ElseIf which = 1 Then
                                d.FillColor2Argb = argb
                            ElseIf d.FillGradient.Stops IsNot Nothing AndAlso which - 2 < d.FillGradient.Stops.Count Then
                                d.FillGradient.Stops(which - 2).Argb = argb
                            End If
                        End Sub)
        ElseIf MeshToolActive AndAlso sel.FillMesh IsNot Nothing AndAlso _meshSel.Count > 0 Then
            Dim nodes = _meshSel.ToList()
            ApplyChange(Sub(r)
                            Dim m = r.Drawings(index).FillMesh
                            For Each i In nodes
                                If i < m.Count Then m.Colors(i) = argb
                            Next
                        End Sub)
        Else
            Return False
        End If
        UpdateDrawControls()
        _canvas.Invalidate()
        Return True
    End Function

    ''' <summary>Del：刪掉選取的色標。</summary>
    Private Function FillToolKey(keyData As Keys) As Boolean
        If keyData <> Keys.Delete OrElse Not InteractiveFillActive OrElse _ifSel < 2 Then Return False
        Dim sel = SelDraw(_recipe)
        If sel?.FillGradient?.Stops Is Nothing OrElse _ifSel - 2 >= sel.FillGradient.Stops.Count Then Return False
        Dim index = _drawIndex, k = _ifSel - 2
        ApplyChange(Sub(r) r.Drawings(index).FillGradient.Stops.RemoveAt(k))
        _ifSel = -1
        UpdateDrawControls()
        _canvas.Invalidate()
        Return True
    End Function

    Private Sub PaintFillTools(g As Graphics)
        If InteractiveFillActive Then PaintInteractiveFill(g)
        If MeshToolActive Then PaintMesh(g)
    End Sub

    ''' <summary>填色方式選「網格」：選取的圖形加上 3×3 網格（已經有的保留），換成網格填色工具。</summary>
    Private Sub UseMeshFill()
        Dim idx = SelectedIndices().Where(Function(i) Fillable(_recipe.Drawings(i)) AndAlso Not _recipe.Drawings(i).Locked).ToList()
        If idx.Count = 0 Then
            SetStatusMessage(Lang.T("先選一個封閉的向量圖形，或用左側的「網格填色」工具點一個圖形。"))
            UpdateDrawControls()
            Return
        End If
        ApplyChange(Sub(r)
                        For Each i In idx
                            Dim x = r.Drawings(i)
                            If x.FillMesh Is Nothing Then x.FillMesh = MeshFill.Create(3, 3, If(x.Filled, x.FillColorArgb, Color.White.ToArgb()))
                            x.Filled = True
                        Next
                    End Sub)
        If idx.Count = 1 Then
            _drawStrip.SelectedTool = DrawIcons.MeshFillTool
            SelectDrawLayer(idx(0))
        End If
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    Private Function FillToolHint(tool As Integer) As String
        Select Case tool
            Case DrawIcons.InteractiveFillTool
                Return Lang.T("互動式填色：在圖形上拖一條線做漸層（填色 → 第二色）；拖起點、終點、色標改位置，在漸層線上按兩下加色標，選了控制點再點色盤換顏色，Del 刪色標。")
            Case DrawIcons.MeshFillTool
                Return Lang.T("網格填色：點一個圖形加上網格；點節點（Shift 多選）再點色盤換顏色，拖曳節點移動；上方改列數、欄數或移除網格。")
        End Select
        Return Nothing
    End Function
End Class
