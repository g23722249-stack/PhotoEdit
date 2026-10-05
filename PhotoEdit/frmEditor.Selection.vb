Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports PhotoEdit

''' <summary>
''' 「選取」分頁（像 PhotoImpact 的選取工具）：標準選取（矩形、橢圓）、套索（拖曳自由畫或點選多邊形）、魔術棒、貝茲選取，
''' 選取區可以新增、加入（Shift）、減去（Alt）、交集（Shift＋Alt）、反轉與羽化，畫面上以螞蟻線顯示。
''' 選好之後可以複製、剪下、貼成物件、複製成物件、只調整選取區、刪除（變透明）、填色、描邊、裁切、當作去背範圍。
''' 選取區存在配方裡（可以復原，也會存進專案檔），座標是畫面上的照片（裁切後、加邊框前）的 0..1。
''' </summary>
Partial Friend Class frmEditor

    Private Const TabSelect As Integer = 11

    Private Enum SelTool
        Rectangle = 0
        Ellipse = 1
        Lasso = 2
        Wand = 3
        Bezier = 4
    End Enum

    Private _selTool As SelTool = SelTool.Rectangle
    Private ReadOnly _selToolButtons As New List(Of RadioButton)()
    Private ReadOnly _selModeButtons As New List(Of RadioButton)()
    Private ReadOnly _selContiguous As New CheckBox()
    Private ReadOnly _selOpButtons As New List(Of Button)()
    Private _selWandTolerance As Integer = 32

    ' 拖曳中的形狀（照片 0..1 座標）。
    Private _selDragStart As PointF?
    Private _selDragNow As PointF
    Private _selDragMode As SelectionMode
    Private _selLasso As List(Of PointF)
    Private _selPolygon As Boolean
    Private _selHover As PointF
    Private _selBezier As List(Of (Anchor As PointF, Handle As PointF))
    Private _selBezierDragging As Boolean
    Private _selPressScreen As Point

    ' 魔術棒用的影像（照片本身，不含圖層與邊框），依配方快取。
    Private _selWandKey As String
    Private _selWandPixels As Byte()
    Private _selWandSize As Size

    ' 螞蟻線。
    Private ReadOnly _antsTimer As New Timer() With {.Interval = 150}
    Private _antsPhase As Integer
    Private _antsKey As String
    Private _antsPoints As Point()
    Private _antsSize As Size
    Private _antsScreen As RectangleF

    ''' <summary>最近一次「複製」的範圍與大小：貼成物件時放回原位。</summary>
    Private _lastCopy As (Bounds As RectangleF, Size As Size)?

    '=====================================================================
    ' 分頁
    '=====================================================================

    Private Sub BuildSelectPage(page As Aqua.TabPage)
        Dim L = NewLayout(page)
        AddHeading(L, "選取工具")
        Dim toolNames = {("矩形", "select.rect"), ("橢圓", "select.ellipse"), ("套索", "select.lasso"), ("魔術棒", "select.wand"), ("貝茲", "select.bezier")}
        Dim toolPanel As New Panel With {.BackColor = Color.Transparent}
        toolPanel.SetBounds(8, L.Y + 2, L.Width - 8, 32)
        Dim tw = (L.Width - 8 - 4 * 4) \ 5
        For i = 0 To toolNames.Length - 1
            Dim rb = ToggleButton(toolNames(i).Item1, Color.FromArgb(200, 225, 255))
            rb.SetBounds(i * (tw + 4), 0, tw, 30)
            Dim tool = CType(i, SelTool)
            AddHandler rb.CheckedChanged, Sub()
                                              If Not rb.Checked Then Return
                                              _selTool = tool
                                              CancelSelectionInProgress()
                                              UpdateSelectControls()
                                          End Sub
            _help.SetHelp(toolNames(i).Item2, rb)
            _selToolButtons.Add(rb)
            toolPanel.Controls.Add(rb)
        Next
        _selToolButtons(0).Checked = True
        L.Add(toolPanel)
        L.Y += 38

        AddHeading(L, "選取模式")
        Dim modeNames = {("新增", "select.new"), ("加入", "select.add"), ("減去", "select.subtract"), ("交集", "select.intersect")}
        Dim modePanel As New Panel With {.BackColor = Color.Transparent}
        modePanel.SetBounds(8, L.Y + 2, L.Width - 8, 32)
        Dim mw = (L.Width - 8 - 3 * 4) \ 4
        For i = 0 To modeNames.Length - 1
            Dim rb = ToggleButton(modeNames(i).Item1, Color.FromArgb(255, 238, 180))
            rb.SetBounds(i * (mw + 4), 0, mw, 30)
            _help.SetHelp(modeNames(i).Item2, rb)
            _selModeButtons.Add(rb)
            modePanel.Controls.Add(rb)
        Next
        _selModeButtons(0).Checked = True
        L.Add(modePanel)
        L.Y += 36
        AddHint(L, "按住 Shift 加入、Alt 減去、Shift＋Alt 交集；矩形與橢圓按住 Ctrl 為正方形／正圓。")

        AddRow(L, New SliderRow With {.Key = "sel_wand", .Caption = "相似程度", .Minimum = 0, .Maximum = 100, .Format = AddressOf Plain,
            .GetValue = Function(r) _selWandTolerance,
            .SetValue = Sub(r, v) _selWandTolerance = v})
        _selContiguous.Text = "魔術棒只選相連區域"
        _selContiguous.Checked = True
        _selContiguous.SetBounds(8, L.Y, L.Width - 8, 24)
        _help.SetHelp("select.contiguous", _selContiguous)
        L.Add(_selContiguous)
        L.Y += 30
        AddRow(L, New SliderRow With {.Key = "sel_feather", .Caption = "羽化", .Minimum = 0, .Maximum = 100, .Format = AddressOf Plain,
            .GetValue = Function(r) If(r.Selection?.Feather, 0),
            .SetValue = Sub(r, v)
                            If r.Selection IsNot Nothing Then r.Selection.Feather = v
                        End Sub})

        AddSelButtons(L, {("全選", "select.all", "selectall"), ("取消選取", "select.none", "deselect"), ("反轉", "select.invert", "invertsel")})
        AddHeading(L, "選取區操作")
        AddSelButtons(L, {("複製", "select.copy", "copy"), ("剪下", "select.cut", "cut"), ("貼成物件", "select.paste", "pasteobject")})
        AddSelButtons(L, {("複製成物件", "select.toobject", "toobject"), ("只調整選取區", "select.adjust", "seladjust")})
        AddSelButtons(L, {("刪除（透明）", "select.delete", "seldelete"), ("填色…", "select.fill", "selfill"), ("描邊…", "select.stroke", "selstroke")})
        AddSelButtons(L, {("裁切到選取區", "select.crop", "selcrop"), ("當作去背範圍", "select.cutout", "selcutout")})

        AddHandler _antsTimer.Tick, Sub() OnAntsTick()
        AddHandler FormClosed, Sub() _antsTimer.Dispose()
    End Sub

    Private Shared Function ToggleButton(text As String, checkedColor As Color) As RadioButton
        Dim rb As New RadioButton With {.Text = text, .Appearance = Appearance.Button, .FlatStyle = FlatStyle.Flat,
                                        .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.White}
        rb.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 195)
        rb.FlatAppearance.CheckedBackColor = checkedColor
        Return rb
    End Function

    Private Sub AddSelButtons(L As PageLayout, items As (Text As String, Help As String, Command As String)())
        Dim bw = (L.Width - 8 - (items.Length - 1) * 4) \ items.Length
        For i = 0 To items.Length - 1
            Dim b = MakeButton(items(i).Text, items(i).Help)
            b.SetBounds(8 + i * (bw + 4), L.Y + 2, bw, 30)
            Dim command = items(i).Command
            AddHandler b.Click, Sub() RunCommand(command)
            b.Tag = command
            _selOpButtons.Add(b)
            L.Add(b)
        Next
        L.Y += 36
    End Sub

    Private ReadOnly Property HasSelection As Boolean
        Get
            Return _photo IsNot Nothing AndAlso _recipe.Selection IsNot Nothing AndAlso Not _recipe.Selection.IsEmpty
        End Get
    End Property

    ''' <summary>配方或選取改變後更新選取分頁與螞蟻線。</summary>
    Private Sub UpdateSelectControls()
        If _selOpButtons.Count = 0 Then Return ' 分頁還沒建好（啟動中）
        Dim has = HasSelection
        For Each b In _selOpButtons
            Dim cmd = CStr(b.Tag)
            b.Enabled = _photo IsNot Nothing AndAlso (has OrElse cmd = "selectall" OrElse cmd = "copy" OrElse cmd = "pasteobject")
        Next
        _selContiguous.Enabled = _selTool = SelTool.Wand
        For Each row In _rows.Where(Function(r) r.Key = "sel_wand")
            row.Slider.Enabled = _selTool = SelTool.Wand
        Next
        For Each row In _rows.Where(Function(r) r.Key = "sel_feather")
            row.Slider.Enabled = has
        Next
        If has Then
            If Not _antsTimer.Enabled Then _antsTimer.Start()
        Else
            _antsTimer.Stop()
            _antsPoints = Nothing
            _antsKey = Nothing
        End If
        _canvas.Invalidate()
    End Sub

    '=====================================================================
    ' 指令
    '=====================================================================

    ''' <summary>選取相關的指令；處理了回傳 True。</summary>
    Private Function RunSelectCommand(name As String) As Boolean
        Select Case name
            Case "selectall"
                ApplyChange(Sub(r)
                                If r.Selection Is Nothing Then r.Selection = New SelectionSpec()
                                r.Selection.Apply(New SelectionOp With {.Mode = SelectionMode.Replace, .Shape = SelectionShape.All})
                            End Sub)
            Case "deselect"
                CancelSelectionInProgress()
                If HasSelection Then ApplyChange(Sub(r) r.Selection = Nothing)
            Case "invertsel"
                ApplyChange(Sub(r)
                                If r.Selection Is Nothing OrElse r.Selection.IsEmpty Then
                                    r.Selection = New SelectionSpec()
                                    r.Selection.Apply(New SelectionOp With {.Mode = SelectionMode.Replace, .Shape = SelectionShape.All})
                                Else
                                    r.Selection.Apply(New SelectionOp With {.Mode = SelectionMode.Invert})
                                End If
                            End Sub)
            Case "cut"
                If Not RequireSelection() Then Return True
                If CopySelection(includeLayers:=False) Then DeleteSelection(fromCut:=True)
            Case "pasteobject" : PasteAsObject()
            Case "toobject" : If RequireSelection() Then SelectionToObject()
            Case "seladjust" : If RequireSelection() Then AdjustSelection()
            Case "seldelete" : If RequireSelection() Then DeleteSelection(fromCut:=False)
            Case "selfill" : If RequireSelection() Then FillSelection()
            Case "selstroke" : If RequireSelection() Then StrokeSelection()
            Case "selcrop" : If RequireSelection() Then CropToSelection()
            Case "selcutout" : If RequireSelection() Then SelectionAsCutout()
            Case Else : Return False
        End Select
        Return True
    End Function

    Private Function RequireSelection() As Boolean
        If HasSelection Then Return True
        SetStatusMessage("先用選取分頁的工具選取一個範圍（或按 Ctrl+A 全選）。")
        Return False
    End Function

    '=====================================================================
    ' 座標
    '=====================================================================

    Private Function SelScreenToPhoto(p As Point) As PointF
        Return DisplayToPhoto(_canvas.ClientToNormalized(p))
    End Function

    Private Function SelPhotoToScreen(p As PointF) As PointF
        Dim n = PhotoToDisplay(p)
        Dim b = _canvas.ImageBounds()
        Return New PointF(b.X + n.X * b.Width, b.Y + n.Y * b.Height)
    End Function

    ''' <summary>照片在畫面上的範圍（不含邊框）。</summary>
    Private Function PhotoScreenRect() As RectangleF
        Dim a = SelPhotoToScreen(New PointF(0, 0)), b = SelPhotoToScreen(New PointF(1, 1))
        Return RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y)
    End Function

    Private Shared Function Clamp01(p As PointF) As PointF
        Return New PointF(Math.Max(0, Math.Min(1, p.X)), Math.Max(0, Math.Min(1, p.Y)))
    End Function

    ''' <summary>依面板模式與按住的鍵決定這一次的組合方式。</summary>
    Private Function CurrentSelectMode() As SelectionMode
        Dim shift = ModifierKeys.HasFlag(Keys.Shift), alt = ModifierKeys.HasFlag(Keys.Alt)
        If shift AndAlso alt Then Return SelectionMode.Intersect
        If shift Then Return SelectionMode.Add
        If alt Then Return SelectionMode.Subtract
        Dim i = _selModeButtons.FindIndex(Function(b) b.Checked)
        Return If(i < 0, SelectionMode.Replace, CType(i, SelectionMode))
    End Function

    '=====================================================================
    ' 滑鼠（畫布在「選取」分頁用繪圖工具的事件）
    '=====================================================================

    Private Sub SelMouseDown(e As MouseEventArgs)
        If _photo Is Nothing Then Return
        Dim p = SelScreenToPhoto(e.Location)
        _selPressScreen = e.Location
        Select Case _selTool
            Case SelTool.Rectangle, SelTool.Ellipse
                _selDragMode = CurrentSelectMode()
                _selDragStart = Clamp01(p)
                _selDragNow = _selDragStart.Value
            Case SelTool.Wand
                If p.X < 0 OrElse p.Y < 0 OrElse p.X > 1 OrElse p.Y > 1 Then Return
                WandSelect(p, CurrentSelectMode())
            Case SelTool.Lasso
                If _selPolygon AndAlso _selLasso IsNot Nothing Then
                    If _selLasso.Count >= 3 AndAlso Dist(SelPhotoToScreen(_selLasso(0)), e.Location) <= 9 Then
                        CommitPolygon(_selLasso)
                    Else
                        _selLasso.Add(Clamp01(p))
                    End If
                Else
                    _selDragMode = CurrentSelectMode()
                    _selLasso = New List(Of PointF) From {Clamp01(p)}
                    _selPolygon = False
                End If
            Case SelTool.Bezier
                If _selBezier Is Nothing Then
                    _selDragMode = CurrentSelectMode()
                    _selBezier = New List(Of (Anchor As PointF, Handle As PointF))()
                ElseIf _selBezier.Count >= 2 AndAlso Dist(SelPhotoToScreen(_selBezier(0).Anchor), e.Location) <= 9 Then
                    CommitBezier()
                    Return
                End If
                _selBezier.Add((Clamp01(p), New PointF(0, 0)))
                _selBezierDragging = True
        End Select
        _canvas.Invalidate()
    End Sub

    Private Sub SelMouseMove(e As MouseEventArgs)
        If _photo Is Nothing Then Return
        If _canvas.Cursor IsNot Cursors.Cross Then _canvas.Cursor = Cursors.Cross
        Dim p = SelScreenToPhoto(e.Location)
        _selHover = Clamp01(p)
        Dim down = e.Button = MouseButtons.Left
        Select Case _selTool
            Case SelTool.Rectangle, SelTool.Ellipse
                If down AndAlso _selDragStart.HasValue Then _selDragNow = Clamp01(p)
            Case SelTool.Lasso
                If down AndAlso _selLasso IsNot Nothing AndAlso Not _selPolygon Then
                    If Dist(SelPhotoToScreen(_selLasso.Last()), e.Location) >= 2 Then _selLasso.Add(Clamp01(p))
                End If
            Case SelTool.Bezier
                If down AndAlso _selBezierDragging AndAlso _selBezier IsNot Nothing AndAlso _selBezier.Count > 0 Then
                    Dim a = _selBezier.Last().Anchor
                    _selBezier(_selBezier.Count - 1) = (a, New PointF(_selHover.X - a.X, _selHover.Y - a.Y))
                End If
        End Select
        _canvas.Invalidate()
    End Sub

    Private Sub SelMouseUp(e As MouseEventArgs)
        If _photo Is Nothing Then Return
        Dim moved = Dist(New PointF(_selPressScreen.X, _selPressScreen.Y), e.Location) > 3
        Select Case _selTool
            Case SelTool.Rectangle, SelTool.Ellipse
                If Not _selDragStart.HasValue Then Return
                Dim start = _selDragStart.Value
                _selDragStart = Nothing
                If Not moved Then
                    ' 點一下（沒拖曳）：新增模式時取消選取，像 PhotoImpact。
                    If _selDragMode = SelectionMode.Replace AndAlso HasSelection Then RunCommand("deselect")
                    _canvas.Invalidate()
                    Return
                End If
                Dim r = DragRect(start, _selDragNow)
                CommitSelection(New SelectionOp With {.Mode = _selDragMode,
                    .Shape = If(_selTool = SelTool.Rectangle, SelectionShape.Rectangle, SelectionShape.Ellipse),
                    .X = r.X, .Y = r.Y, .W = r.Width, .H = r.Height})
            Case SelTool.Lasso
                If _selLasso Is Nothing OrElse _selPolygon Then Return
                If moved AndAlso _selLasso.Count >= 3 Then
                    CommitPolygon(_selLasso)
                Else
                    ' 點一下：改成點選多邊形，繼續點下一個頂點；按兩下、Enter 或點回第一點結束。
                    _selPolygon = True
                    SetStatusMessage("點選多邊形的頂點；按兩下、Enter 或點回第一點完成，Esc 取消。")
                End If
            Case SelTool.Bezier
                _selBezierDragging = False
                SetStatusMessage("貝茲選取：點一下加錨點、拖曳拉出曲線；按兩下、Enter 或點回第一點完成，Esc 取消。")
        End Select
        _canvas.Invalidate()
    End Sub

    Private Sub SelDoubleClick(e As MouseEventArgs)
        Select Case _selTool
            Case SelTool.Lasso
                If _selPolygon AndAlso _selLasso IsNot Nothing AndAlso _selLasso.Count >= 3 Then CommitPolygon(_selLasso)
            Case SelTool.Bezier
                If _selBezier IsNot Nothing AndAlso _selBezier.Count >= 3 Then
                    ' 按兩下會先多加一個（重疊的）錨點，去掉再完成。
                    _selBezier.RemoveAt(_selBezier.Count - 1)
                    CommitBezier()
                End If
        End Select
    End Sub

    ''' <summary>Enter 完成、Esc 取消進行中的多邊形或貝茲。處理了回傳 True。</summary>
    Private Function SelKey(keyData As Keys) As Boolean
        If _tabs.SelectedIndex <> TabSelect OrElse _photo Is Nothing Then Return False
        Select Case keyData
            Case Keys.Enter
                If _selPolygon AndAlso _selLasso IsNot Nothing AndAlso _selLasso.Count >= 3 Then CommitPolygon(_selLasso) : Return True
                If _selBezier IsNot Nothing AndAlso _selBezier.Count >= 2 Then CommitBezier() : Return True
            Case Keys.Escape
                If _selLasso IsNot Nothing OrElse _selBezier IsNot Nothing OrElse _selDragStart.HasValue Then
                    CancelSelectionInProgress()
                    Return True
                End If
                If HasSelection Then RunCommand("deselect") : Return True
            Case Keys.Delete
                If HasSelection Then DeleteSelection(fromCut:=False) : Return True
        End Select
        Return False
    End Function

    Private Sub CancelSelectionInProgress()
        _selDragStart = Nothing
        _selLasso = Nothing
        _selPolygon = False
        _selBezier = Nothing
        _selBezierDragging = False
        _canvas.Invalidate()
    End Sub

    Private Shared Function Dist(a As PointF, b As Point) As Double
        Return Math.Sqrt((a.X - b.X) ^ 2 + (a.Y - b.Y) ^ 2)
    End Function

    ''' <summary>拖曳的兩點換成矩形；按住 Ctrl 時依照片像素做成正方形。</summary>
    Private Function DragRect(a As PointF, b As PointF) As RectangleF
        Dim dx = b.X - a.X, dy = b.Y - a.Y
        If ModifierKeys.HasFlag(Keys.Control) Then
            Dim aspect = CSng(PhotoAspect())
            Dim side = Math.Max(Math.Abs(dx) * aspect, Math.Abs(dy))
            dx = Math.Sign(If(dx = 0, 1, dx)) * side / aspect
            dy = Math.Sign(If(dy = 0, 1, dy)) * side
        End If
        Return RectangleF.FromLTRB(Math.Min(a.X, a.X + dx), Math.Min(a.Y, a.Y + dy), Math.Max(a.X, a.X + dx), Math.Max(a.Y, a.Y + dy))
    End Function

    '=====================================================================
    ' 建立選取區
    '=====================================================================

    Private Sub CommitSelection(op As SelectionOp)
        CancelSelectionInProgress()
        ApplyChange(Sub(r)
                        If r.Selection Is Nothing Then r.Selection = New SelectionSpec()
                        r.Selection.Apply(op)
                    End Sub)
        If Not HasSelection Then SetStatusMessage("選取區是空的。") Else UpdateSelectionStatus()
    End Sub

    Private Sub CommitPolygon(points As List(Of PointF))
        Dim flat As New List(Of Double)()
        For Each p In points
            flat.Add(p.X) : flat.Add(p.Y)
        Next
        CommitSelection(New SelectionOp With {.Mode = _selDragMode, .Shape = SelectionShape.Polygon, .Points = flat})
    End Sub

    Private Sub CommitBezier()
        If _selBezier Is Nothing OrElse _selBezier.Count < 2 Then CancelSelectionInProgress() : Return
        CommitPolygon(BezierOutline(_selBezier, closed:=True))
    End Sub

    ''' <summary>貝茲錨點（含對稱控制把手）取樣成折線。</summary>
    Private Shared Function BezierOutline(anchors As List(Of (Anchor As PointF, Handle As PointF)), closed As Boolean) As List(Of PointF)
        Dim pts As New List(Of PointF)()
        Dim n = anchors.Count
        Dim last = If(closed, n, n - 1)
        For i = 0 To last - 1
            Dim a = anchors(i), b = anchors((i + 1) Mod n)
            Dim p0 = a.Anchor
            Dim p1 = New PointF(a.Anchor.X + a.Handle.X, a.Anchor.Y + a.Handle.Y)
            Dim p2 = New PointF(b.Anchor.X - b.Handle.X, b.Anchor.Y - b.Handle.Y)
            Dim p3 = b.Anchor
            For s = 0 To 23
                Dim t = s / 24.0F, u = 1 - t
                pts.Add(New PointF(u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                                   u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y))
            Next
        Next
        If Not closed AndAlso n > 0 Then pts.Add(anchors(n - 1).Anchor)
        Return pts
    End Function

    ''' <summary>魔術棒：在照片本身（不含圖層與邊框）上選出相近的顏色，結果存成遮罩。</summary>
    Private Sub WandSelect(p As PointF, mode As SelectionMode)
        Cursor = Cursors.WaitCursor
        Try
            EnsureWandPixels()
            Dim w = _selWandSize.Width, h = _selWandSize.Height
            Dim sel = MagicWand.SelectRegion(_selWandPixels, w, h, New WandClick With {
                .X = p.X, .Y = p.Y, .Tolerance = _selWandTolerance, .Contiguous = _selContiguous.Checked})
            CommitSelection(New SelectionOp With {.Mode = mode, .Shape = SelectionShape.Mask, .MaskPng = SelectionMask.EncodeMask(sel, w, h)})
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Function PhotoOnlyRecipe() As EditRecipe
        Dim r = DisplayRecipe().Clone()
        r.Overlays = Nothing : r.Drawings = Nothing : r.LayerOrder = Nothing : r.Selection = Nothing
        r.Frame = PhotoFrameStyle.None : r.CropShape = CropShape.Rectangle
        Return r
    End Function

    Private Sub EnsureWandPixels()
        Dim r = PhotoOnlyRecipe()
        Dim key = RecipeStore.ToJson(r)
        If key = _selWandKey AndAlso _selWandPixels IsNot Nothing Then Return
        Using bmp = ImagePipeline.Render(SourceBase(), r, faces:=_faces)
            _selWandPixels = StickerSplitter.ReadPixels(bmp)
            _selWandSize = bmp.Size
        End Using
        _selWandKey = key
    End Sub

    Private Sub UpdateSelectionStatus()
        Dim b = SelectionMask.Bounds(_recipe.Selection)
        If b.IsEmpty Then SetStatusMessage("選取區是空的。") : Return
        Dim full = FullOutputSize()
        SetStatusMessage($"選取範圍 {CInt(b.Width * full.Width)} × {CInt(b.Height * full.Height)}　｜　Shift 加入、Alt 減去；Ctrl+C 複製、Del 刪除、Ctrl+D 取消選取")
    End Sub

    ''' <summary>全尺寸輸出（不含邊框）的大小。</summary>
    Private Function FullOutputSize() As Size
        Dim s = PhotoPixelSize()
        Dim scale = If(_previewBase Is Nothing, 1.0, _photo.Image.Width / CDbl(_previewBase.Width))
        Return New Size(Math.Max(1, CInt(Math.Round(s.Width * scale))), Math.Max(1, CInt(Math.Round(s.Height * scale))))
    End Function

    '=====================================================================
    ' 畫面：拖曳中的形狀與螞蟻線
    '=====================================================================

    ''' <summary>拖曳中的形狀（只在選取分頁）。</summary>
    Private Sub SelPaint(g As Graphics)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using black As New Pen(Color.Black, 1), white As New Pen(Color.White, 1) With {.DashStyle = DashStyle.Dash}
            Dim drawBoth = Sub(draw As Action(Of Pen))
                               draw(black)
                               draw(white)
                           End Sub
            If _selDragStart.HasValue AndAlso (_selTool = SelTool.Rectangle OrElse _selTool = SelTool.Ellipse) Then
                Dim r = DragRect(_selDragStart.Value, _selDragNow)
                Dim a = SelPhotoToScreen(r.Location), b = SelPhotoToScreen(New PointF(r.Right, r.Bottom))
                Dim sr = RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y)
                drawBoth(Sub(pen)
                             If _selTool = SelTool.Rectangle Then g.DrawRectangle(pen, sr.X, sr.Y, sr.Width, sr.Height) Else g.DrawEllipse(pen, sr)
                         End Sub)
            End If
            If _selLasso IsNot Nothing AndAlso _selLasso.Count >= 1 Then
                Dim pts = _selLasso.Select(Function(p) SelPhotoToScreen(p)).ToList()
                If _selPolygon Then pts.Add(SelPhotoToScreen(_selHover))
                If pts.Count >= 2 Then drawBoth(Sub(pen) g.DrawLines(pen, pts.ToArray()))
                If _selPolygon Then DrawAnchor(g, pts(0))
            End If
            If _selBezier IsNot Nothing AndAlso _selBezier.Count >= 1 Then
                Dim preview = New List(Of (Anchor As PointF, Handle As PointF))(_selBezier)
                If Not _selBezierDragging Then preview.Add((_selHover, New PointF(0, 0)))
                Dim outline = BezierOutline(preview, closed:=False).Select(Function(p) SelPhotoToScreen(p)).ToArray()
                If outline.Length >= 2 Then drawBoth(Sub(pen) g.DrawLines(pen, outline))
                For Each a In _selBezier
                    Dim s = SelPhotoToScreen(a.Anchor)
                    If a.Handle.X <> 0 OrElse a.Handle.Y <> 0 Then
                        Dim h1 = SelPhotoToScreen(New PointF(a.Anchor.X + a.Handle.X, a.Anchor.Y + a.Handle.Y))
                        Dim h2 = SelPhotoToScreen(New PointF(a.Anchor.X - a.Handle.X, a.Anchor.Y - a.Handle.Y))
                        Using hp As New Pen(Color.FromArgb(40, 120, 230), 1)
                            g.DrawLine(hp, h1, h2)
                            g.FillEllipse(Brushes.White, h1.X - 3, h1.Y - 3, 6, 6) : g.DrawEllipse(hp, h1.X - 3, h1.Y - 3, 6, 6)
                            g.FillEllipse(Brushes.White, h2.X - 3, h2.Y - 3, 6, 6) : g.DrawEllipse(hp, h2.X - 3, h2.Y - 3, 6, 6)
                        End Using
                    End If
                    DrawAnchor(g, s)
                Next
            End If
        End Using
    End Sub

    Private Shared Sub DrawAnchor(g As Graphics, p As PointF)
        g.FillRectangle(Brushes.White, p.X - 3.5F, p.Y - 3.5F, 7, 7)
        g.DrawRectangle(Pens.Black, p.X - 3.5F, p.Y - 3.5F, 7, 7)
    End Sub

    ''' <summary>每個分頁都會畫：選取區的螞蟻線（黑白相間、會流動）。</summary>
    Private Sub PaintSelectionAnts(g As Graphics)
        If Not HasSelection OrElse _cropMode Then Return
        Dim rect = PhotoScreenRect()
        If rect.Width < 2 OrElse rect.Height < 2 Then Return
        ' 遮罩解析度跟畫面上的大小差不多（太大時限制在長邊 2400）。
        Dim scale = Math.Min(1.0F, 2400.0F / Math.Max(rect.Width, rect.Height))
        Dim mw = Math.Max(2, CInt(rect.Width * scale)), mh = Math.Max(2, CInt(rect.Height * scale))
        Dim key = RecipeStore.ToJson(New EditRecipe With {.Selection = _recipe.Selection}) & "|" & mw & "x" & mh
        If key <> _antsKey Then
            _antsKey = key
            _antsSize = New Size(mw, mh)
            _antsPoints = BoundaryPoints(SelectionMask.Render(_recipe.Selection, mw, mh), mw, mh)
        End If
        _antsScreen = rect
        If _antsPoints Is Nothing OrElse _antsPoints.Length = 0 Then Return
        Dim sx = rect.Width / _antsSize.Width, sy = rect.Height / _antsSize.Height
        Dim pw = Math.Max(1.0F, sx), ph = Math.Max(1.0F, sy)
        Dim dark As New List(Of RectangleF)(), light As New List(Of RectangleF)()
        Dim clip = g.VisibleClipBounds
        For Each p In _antsPoints
            Dim r As New RectangleF(rect.X + p.X * sx, rect.Y + p.Y * sy, pw, ph)
            If Not r.IntersectsWith(clip) Then Continue For
            If ((p.X + p.Y + _antsPhase) \ 4) Mod 2 = 0 Then dark.Add(r) Else light.Add(r)
        Next
        g.SmoothingMode = SmoothingMode.None
        If dark.Count > 0 Then g.FillRectangles(Brushes.Black, dark.ToArray())
        If light.Count > 0 Then g.FillRectangles(Brushes.White, light.ToArray())
    End Sub

    ''' <summary>選取區的邊界像素（選到、而四周有沒選到的或是照片邊緣）。</summary>
    Private Shared Function BoundaryPoints(m As Byte(), w As Integer, h As Integer) As Point()
        Dim list As New List(Of Point)()
        Dim inside = Function(x As Integer, y As Integer) x >= 0 AndAlso y >= 0 AndAlso x < w AndAlso y < h AndAlso m(y * w + x) >= 128
        For y = 0 To h - 1
            For x = 0 To w - 1
                If Not inside(x, y) Then Continue For
                If Not inside(x - 1, y) OrElse Not inside(x + 1, y) OrElse Not inside(x, y - 1) OrElse Not inside(x, y + 1) Then list.Add(New Point(x, y))
            Next
        Next
        Return list.ToArray()
    End Function

    Private Sub OnAntsTick()
        If Not HasSelection OrElse Not Visible OrElse WindowState = FormWindowState.Minimized Then Return
        _antsPhase = (_antsPhase + 7) Mod 8 ' 往前流動
        Dim r = Rectangle.Round(_antsScreen)
        r.Inflate(3, 3)
        _canvas.Invalidate(r)
    End Sub

    '=====================================================================
    ' 操作
    '=====================================================================

    ''' <summary>全尺寸、合併所有圖層（不含邊框）的影像，只留選取區（裁到選取範圍）。沒選到東西時回傳 Nothing。</summary>
    Private Function RenderSelection(includeLayers As Boolean, ByRef bounds As RectangleF) As Bitmap
        ExitCropMode(apply:=True)
        CommitCalloutEditor()
        Dim r = If(includeLayers, _recipe.Clone(), PhotoOnlyRecipe())
        r.Frame = PhotoFrameStyle.None
        r.Selection = Nothing
        Dim spec = _recipe.Selection
        Using full = RenderMerged(r)
            Dim w = full.Width, h = full.Height
            Dim mask = SelectionMask.Render(spec, w, h)
            Dim l = w, t = h, rr = -1, bb = -1
            For y = 0 To h - 1
                For x = 0 To w - 1
                    If mask(y * w + x) > 0 Then
                        If x < l Then l = x
                        If x > rr Then rr = x
                        If y < t Then t = y
                        If y > bb Then bb = y
                    End If
                Next
            Next
            If rr < 0 Then Return Nothing
            Dim cw = rr - l + 1, ch = bb - t + 1
            bounds = New RectangleF(CSng(l / w), CSng(t / h), CSng(cw / w), CSng(ch / h))
            Dim px = StickerSplitter.ReadPixels(full)
            Dim outPx(cw * ch * 4 - 1) As Byte
            For y = 0 To ch - 1
                For x = 0 To cw - 1
                    Dim si = ((t + y) * w + (l + x)), di = y * cw + x
                    outPx(di * 4) = px(si * 4) : outPx(di * 4 + 1) = px(si * 4 + 1) : outPx(di * 4 + 2) = px(si * 4 + 2)
                    outPx(di * 4 + 3) = CByte(CInt(px(si * 4 + 3)) * mask(si) \ 255)
                Next
            Next
            Dim result As New Bitmap(cw, ch, PixelFormat.Format32bppArgb)
            Dim bd = result.LockBits(New Rectangle(0, 0, cw, ch), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
            Try
                For y = 0 To ch - 1
                    Marshal.Copy(outPx, y * cw * 4, bd.Scan0 + y * bd.Stride, cw * 4)
                Next
            Finally
                result.UnlockBits(bd)
            End Try
            result.SetResolution(_photo.Resolution, _photo.Resolution)
            Return result
        End Using
    End Function

    ''' <summary>複製選取區到剪貼簿；PNG 保留透明。</summary>
    ''' <param name="includeLayers">複製看到的畫面（含文字、貼圖、繪圖）；剪下只拿照片本身（剪下只刪照片）。</param>
    Private Function CopySelection(Optional includeLayers As Boolean = True) As Boolean
        Dim bounds As RectangleF
        Cursor = Cursors.WaitCursor
        Try
            Using piece = RenderSelection(includeLayers, bounds)
                If piece Is Nothing Then SetStatusMessage("選取區是空的。") : Return False
                PutOnClipboard(piece)
                _lastCopy = (bounds, piece.Size)
                SetStatusMessage($"已複製選取區（{piece.Width} × {piece.Height}）到剪貼簿；「貼成物件」可以貼回照片上當成可移動的物件。")
                Return True
            End Using
        Catch ex As Exception When TypeOf ex Is ExternalException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is ArgumentException
            MessageBox.Show(Me, "無法複製到剪貼簿：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        Finally
            Cursor = Cursors.Default
        End Try
    End Function

    Private Shared Sub PutOnClipboard(image As Bitmap)
        Using opaque As New Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb), png As New MemoryStream()
            Using g = Graphics.FromImage(opaque)
                g.Clear(Color.White)
                g.DrawImage(image, 0, 0, image.Width, image.Height)
            End Using
            image.Save(png, ImageFormat.Png)
            Dim data As New DataObject()
            data.SetData("PNG", False, png)
            data.SetData(DataFormats.Bitmap, True, opaque)
            Clipboard.SetDataObject(data, True, 5, 100)
        End Using
    End Sub

    ''' <summary>貼成物件：剪貼簿的圖片變成照片上可移動、縮放、旋轉的圖片物件（剛複製的放回原位）。</summary>
    Private Sub PasteAsObject()
        If _photo Is Nothing Then Return
        Dim file As String
        Dim size As Size
        Using bmp = ReadClipboardBitmap()
            If bmp Is Nothing Then SetStatusMessage("剪貼簿裡沒有圖片。") : Return
            size = bmp.Size
            Try
                file = SaveClipboardImage(bmp)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ExternalException
                MessageBox.Show(Me, "無法貼上：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End Try
        End Using
        Dim b As RectangleF
        If _lastCopy.HasValue AndAlso _lastCopy.Value.Size = size Then
            b = _lastCopy.Value.Bounds
        Else
            ' 依原尺寸放在中央；比照片大時縮到 80%。
            Dim full = FullOutputSize()
            Dim fw = size.Width / CDbl(full.Width), fh = size.Height / CDbl(full.Height)
            Dim s = Math.Min(1.0, 0.8 / Math.Max(fw, fh))
            b = New RectangleF(CSng(0.5 - fw * s / 2), CSng(0.5 - fh * s / 2), CSng(fw * s), CSng(fh * s))
        End If
        AddImageObject(file, b, "已貼成物件，可以拖曳移動、縮放、旋轉。")
    End Sub

    ''' <summary>複製成物件：選取區（照片本身）變成可移動的圖片物件，放在原位，原本的照片不變。</summary>
    Private Sub SelectionToObject()
        Dim bounds As RectangleF
        Dim file As String
        Cursor = Cursors.WaitCursor
        Try
            Using piece = RenderSelection(includeLayers:=False, bounds)
                If piece Is Nothing Then SetStatusMessage("選取區是空的。") : Return
                file = SaveClipboardImage(piece)
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ExternalException
            MessageBox.Show(Me, "無法建立物件：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Finally
            Cursor = Cursors.Default
        End Try
        AddImageObject(file, bounds, "已把選取區複製成物件（在原位上面），拖曳就能移開；原本的照片不變。")
    End Sub

    ''' <summary>加入圖片物件：bounds 為照片 0..1 的範圍（物件高度＝範圍高度）。</summary>
    Private Sub AddImageObject(file As String, bounds As RectangleF, message As String)
        Dim o As New Overlay With {.Kind = OverlayKind.Image, .ImagePath = file, .Shadow = False,
                                   .X = bounds.X + bounds.Width / 2, .Y = bounds.Y + bounds.Height / 2,
                                   .Size = Math.Max(0.005, bounds.Height)}
        AddOverlayObject(o, switchTab:=TabSticker)
        SetStatusMessage(message)
    End Sub

    ''' <summary>只調整選取區：新增一個以選取區為範圍的局部調整，切到「局部」分頁調曝光、對比、飽和度、色溫。</summary>
    Private Sub AdjustSelection()
        Dim adj As New LocalAdjustment With {.Kind = LocalKind.Selection, .Region = _recipe.Selection.Clone(), .Exposure = 0.3}
        ApplyChange(Sub(r)
                        If r.LocalAdjustments Is Nothing Then r.LocalAdjustments = New List(Of LocalAdjustment)()
                        r.LocalAdjustments.Add(adj)
                    End Sub)
        _localIndex = _recipe.LocalAdjustments.Count - 1
        _tabs.SelectedIndex = TabLocal
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolFromTab()
        SetStatusMessage("已新增「選取區調整」（先提亮一點），用下面的滑桿調整；只會改選取區裡面。")
    End Sub

    ''' <summary>刪除：選取區變透明（以去背做，原圖不變；匯出 JPG 時透明處為白色）。</summary>
    Private Sub DeleteSelection(fromCut As Boolean)
        Dim region = SourceRegion(SelectionMask.Inverted(_recipe.Selection))
        AddCutoutRegion(region)
        SetStatusMessage(If(fromCut, "已剪下選取區（原處變透明）；「貼成物件」可以貼回。", "已刪除選取區（變透明）；按復原可以還原。") &
                         "在「去背」分頁可以把透明處換成顏色或其他背景。")
    End Sub

    Private Sub SelectionAsCutout()
        AddCutoutRegion(SourceRegion(_recipe.Selection))
        SetStatusMessage("已把選取區當作去背範圍：選取區以外變透明。可以到「去背」分頁換背景、或再用魔術棒與筆刷修正。")
    End Sub

    ''' <summary>換算到原圖座標（去背在原圖上算）。</summary>
    Private Function SourceRegion(spec As SelectionSpec) As SelectionSpec
        Dim out = PhotoPixelSize()
        Return SelectionMask.ToSource(spec, _recipe, _photo.Image.Width, _photo.Image.Height, out.Width, out.Height, MaskStore.MaxSide)
    End Function

    Private Sub AddCutoutRegion(region As SelectionSpec)
        Dim model = If(_modelHuman.Checked, CutoutModel.Human, CutoutModel.General)
        ApplyChange(Sub(r)
                        If r.Cutout Is Nothing Then r.Cutout = New CutoutSettings With {.Model = model, .Background = CutoutBackground.Transparent}
                        If r.Cutout.Background = CutoutBackground.Original Then r.Cutout.Background = CutoutBackground.Transparent
                        If r.Cutout.Regions Is Nothing Then r.Cutout.Regions = New List(Of SelectionSpec)()
                        r.Cutout.Regions.Add(region)
                    End Sub)
    End Sub

    ''' <summary>填色：新增一個點陣圖層，把選取區填滿顏色（之後可以改不透明度、混合模式或移動）。</summary>
    Private Sub FillSelection()
        Dim c As Color
        Using dlg As New ColorDialog With {.Color = Color.FromArgb(_drawStyle.FillColorArgb), .FullOpen = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            c = dlg.Color
        End Using
        AddRegionLayer("填色", New DrawLayer With {.Shape = DrawShape.Raster, .Region = _recipe.Selection.Clone(), .Filled = True, .Stroked = False,
                                                   .FillColorArgb = c.ToArgb(), .Opacity = 100, .Param1 = PhotoAspect()})
        SetStatusMessage("已把選取區填色（新的點陣圖層，可以在「圖層」分頁調不透明度與混合模式）。")
    End Sub

    ''' <summary>描邊：選顏色、寬度與位置，新增一個點陣圖層畫出選取區的邊。</summary>
    Private Sub StrokeSelection()
        Dim full = FullOutputSize()
        Dim c = Color.FromArgb(_drawStyle.StrokeColorArgb)
        Dim widthPx = Math.Max(2, full.Height \ 200)
        Dim position = 1
        Using dlg As New Form With {.Text = "描邊", .FormBorderStyle = FormBorderStyle.FixedDialog, .MaximizeBox = False, .MinimizeBox = False,
                                    .StartPosition = FormStartPosition.CenterParent, .ShowInTaskbar = False, .ClientSize = New Size(320, 168),
                                    .Font = New Font("Microsoft JhengHei UI", 9.5F)}
            Dim colorButton As New Button With {.BackColor = c, .FlatStyle = FlatStyle.Flat}
            Dim widthBox As New NumericUpDown With {.Minimum = 1, .Maximum = 2000, .Value = widthPx}
            Dim posBox As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
            posBox.Items.AddRange({"內側", "置中", "外側"})
            posBox.SelectedIndex = position
            Dim captions = {("顏色", 16), ("寬度（像素）", 52), ("位置", 88)}
            For Each cap In captions
                Dim lbl As New Label With {.Text = cap.Item1, .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
                lbl.SetBounds(16, cap.Item2, 100, 26)
                dlg.Controls.Add(lbl)
            Next
            colorButton.SetBounds(120, 16, 80, 26)
            widthBox.SetBounds(120, 52, 100, 26)
            posBox.SetBounds(120, 88, 100, 26)
            AddHandler colorButton.Click, Sub()
                                              Using cd As New ColorDialog With {.Color = colorButton.BackColor, .FullOpen = True}
                                                  If cd.ShowDialog(dlg) = DialogResult.OK Then colorButton.BackColor = cd.Color
                                              End Using
                                          End Sub
            Dim ok As New Button With {.Text = "確定", .DialogResult = DialogResult.OK}
            Dim cancel As New Button With {.Text = "取消", .DialogResult = DialogResult.Cancel}
            ok.SetBounds(140, 128, 80, 28)
            cancel.SetBounds(228, 128, 80, 28)
            dlg.Controls.AddRange(New Control() {colorButton, widthBox, posBox, ok, cancel})
            dlg.AcceptButton = ok
            dlg.CancelButton = cancel
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            c = colorButton.BackColor
            widthPx = CInt(widthBox.Value)
            position = posBox.SelectedIndex
        End Using
        AddRegionLayer("描邊", New DrawLayer With {.Shape = DrawShape.Raster, .Region = _recipe.Selection.Clone(), .Filled = False, .Stroked = True,
                                                   .StrokeColorArgb = c.ToArgb(), .StrokeWidth = widthPx / CDbl(full.Height),
                                                   .Param2 = position, .Opacity = 100, .Param1 = PhotoAspect()})
        SetStatusMessage("已描邊選取區（新的點陣圖層）。")
    End Sub

    Private Sub AddRegionLayer(name As String, op As DrawLayer)
        Dim layer As New DrawLayer With {.Shape = DrawShape.Raster, .Name = name, .Ops = New List(Of DrawLayer) From {op}}
        AddDrawLayer(layer)
    End Sub

    ''' <summary>裁切到選取區的外接矩形（裁切後選取區取消）。</summary>
    Private Sub CropToSelection()
        Dim b = SelectionMask.Bounds(_recipe.Selection, 1024, 1024)
        If b.IsEmpty Then SetStatusMessage("選取區是空的。") : Return
        ExitCropMode(apply:=True)
        ApplyChange(Sub(r)
                        Dim c = If(r.Crop Is Nothing, New CropRect(), r.Crop.Normalized())
                        r.Crop = New CropRect(c.X + b.X * c.Width, c.Y + b.Y * c.Height, b.Width * c.Width, b.Height * c.Height)
                        r.Selection = Nothing
                    End Sub)
        SetStatusMessage("已裁切到選取範圍；按復原可以還原。")
    End Sub
End Class
