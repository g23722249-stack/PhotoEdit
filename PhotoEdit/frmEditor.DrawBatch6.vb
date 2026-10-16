Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 向量第三批（排版與精準，對照 CorelDRAW／Illustrator）：尺規、從尺規拖出的參考線、格線與貼齊（檢視選單）；
''' 尺寸線與連接線（工具在 frmEditor.Draw.vb 的 NewShape 建立，參數在上方圖形參數列）；可變寬度工具（寬度描述檔）；
''' 圖樣填色（右側「填色方式 → 圖樣」，上方圖樣列）；文件色票（左側色彩面板的「色票」，改色票時用到它的物件一起變）。
''' 幾何與上色見 PhotoEdit.Core 的 LayoutTools.vb。
''' </summary>
Partial Friend Class frmEditor

    Private Const RulerSize As Integer = 18

    '---------------------------------------------------------------------
    ' 設定（檢視選單）
    '---------------------------------------------------------------------

    Private ReadOnly _layoutMenu As New Dictionary(Of String, Aqua.MenuItem)()

    Private Sub AddLayoutMenu(viewMenu As Aqua.MenuItem)
        viewMenu.AddItem(New Aqua.MenuItem("-"))
        _layoutMenu("flipview") = viewMenu.AddItem(Item("flipview", Lang.T("左右翻轉檢視")))
        For Each m In {("rulers", Lang.T("顯示尺規 (Ctrl+Alt+R)")), ("grid", Lang.T("顯示格線 (Ctrl+')")), ("snapgrid", Lang.T("貼齊格線")),
                       ("snapguides", Lang.T("貼齊參考線")), ("gridsize", Lang.T("格線間距…")), ("clearguides", Lang.T("清除參考線"))}
            _layoutMenu(m.Item1) = viewMenu.AddItem(Item(m.Item1, m.Item2))
        Next
        UpdateLayoutMenu()
    End Sub

    Private Sub UpdateLayoutMenu()
        If _layoutMenu.Count = 0 Then Return
        _layoutMenu("rulers").Checked = _appSettings.ShowRulers
        _layoutMenu("grid").Checked = _appSettings.ShowGrid
        _layoutMenu("snapgrid").Checked = _appSettings.SnapGrid
        _layoutMenu("snapguides").Checked = _appSettings.SnapGuides
    End Sub

    Private Sub ToggleLayout(name As String)
        Select Case name
            Case "rulers" : _appSettings.ShowRulers = Not _appSettings.ShowRulers
            Case "grid" : _appSettings.ShowGrid = Not _appSettings.ShowGrid
            Case "snapgrid" : _appSettings.SnapGrid = Not _appSettings.SnapGrid
            Case "snapguides" : _appSettings.SnapGuides = Not _appSettings.SnapGuides
        End Select
        _appSettings.Save()
        UpdateLayoutMenu()
        _canvas.Invalidate()
    End Sub

    ''' <summary>格線間距（照片像素）。</summary>
    Private Sub SetGridSpacing(px As Integer)
        _appSettings.GridSpacing = Math.Max(2, Math.Min(5000, px))
        _appSettings.Save()
        _canvas.Invalidate()
    End Sub

    Private Sub ShowGridSpacing()
        Using dlg As New Form With {.Text = Lang.T("格線間距"), .FormBorderStyle = FormBorderStyle.FixedDialog, .MaximizeBox = False, .MinimizeBox = False,
                                   .ShowInTaskbar = False, .StartPosition = FormStartPosition.CenterParent, .ClientSize = New Size(280, 96), .Font = _panelFont}
            Dim lbl As New Label With {.Text = Lang.T("間距（照片像素）"), .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
            lbl.SetBounds(14, 14, 140, 26)
            Dim nb As New NumericUpDown With {.Minimum = 2, .Maximum = 5000, .Value = Math.Max(2, Math.Min(5000, _appSettings.GridSpacing))}
            nb.SetBounds(160, 14, 100, 26)
            Dim ok As New Button With {.Text = Lang.T("確定"), .DialogResult = DialogResult.OK}
            Dim cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel}
            ok.SetBounds(80, 54, 88, 30) : cancel.SetBounds(176, 54, 88, 30)
            dlg.Controls.AddRange({lbl, nb, ok, cancel})
            dlg.AcceptButton = ok : dlg.CancelButton = cancel
            ThemeManager.Attach(dlg)
            If dlg.ShowDialog(Me) = DialogResult.OK Then SetGridSpacing(CInt(nb.Value))
        End Using
    End Sub

    Private Function GridUnits() As Double
        Return PxToUnits(Math.Max(2, _appSettings.GridSpacing))
    End Function

    '---------------------------------------------------------------------
    ' 貼齊
    '---------------------------------------------------------------------

    ''' <summary>點靠近參考線（優先）或格線（螢幕 6 像素內）時吸過去；按住 Alt 不吸附。</summary>
    Private Function SnapUnit(u As PointF) As PointF
        If ModifierKeys.HasFlag(Keys.Alt) OrElse _recipe Is Nothing Then Return u
        Dim tol = 6 / Math.Max(0.0001, PxPerUnit())
        Dim x = CDbl(u.X), y = CDbl(u.Y)
        Dim bx = tol, by = tol
        Dim sx As Double? = Nothing, sy As Double? = Nothing
        If _appSettings.SnapGuides AndAlso _recipe.Guides IsNot Nothing Then
            For Each gd In _recipe.Guides
                If gd.Vertical AndAlso Math.Abs(gd.Pos - x) < bx Then bx = Math.Abs(gd.Pos - x) : sx = gd.Pos
                If Not gd.Vertical AndAlso Math.Abs(gd.Pos - y) < by Then by = Math.Abs(gd.Pos - y) : sy = gd.Pos
            Next
        End If
        If _appSettings.SnapGrid Then
            Dim gs = GridUnits()
            Dim gx = Math.Round(x / gs) * gs, gy = Math.Round(y / gs) * gs
            If Not sx.HasValue AndAlso Math.Abs(gx - x) < tol Then sx = gx
            If Not sy.HasValue AndAlso Math.Abs(gy - y) < tol Then sy = gy
        End If
        Return New PointF(CSng(If(sx, x)), CSng(If(sy, y)))
    End Function

    ''' <summary>智慧參考線（SnapMove）額外的對齊目標：參考線、靠近移動中外框的格線（寬或高為 0 的框）。</summary>
    Private Function LayoutSnapTargets(moving As RectangleF) As List(Of RectangleF)
        Dim list As New List(Of RectangleF)()
        If ModifierKeys.HasFlag(Keys.Alt) Then Return list
        Dim aspect = CSng(PhotoAspect())
        If _appSettings.SnapGuides AndAlso _recipe.Guides IsNot Nothing Then
            For Each gd In _recipe.Guides
                list.Add(If(gd.Vertical, New RectangleF(CSng(gd.Pos), 0, 0, 1), New RectangleF(0, CSng(gd.Pos), aspect, 0)))
            Next
        End If
        If _appSettings.SnapGrid Then
            Dim gs = GridUnits()
            For Each v In {moving.Left, moving.Left + moving.Width / 2, moving.Right}
                list.Add(New RectangleF(CSng(Math.Round(v / gs) * gs), 0, 0, 1))
            Next
            For Each v In {moving.Top, moving.Top + moving.Height / 2, moving.Bottom}
                list.Add(New RectangleF(0, CSng(Math.Round(v / gs) * gs), aspect, 0))
            Next
        End If
        Return list
    End Function

    '---------------------------------------------------------------------
    ' 參考線：從尺規拖出、拖曳、拖回尺規刪除
    '---------------------------------------------------------------------

    Private _guideDrag As Integer = -1
    Private _guideKey As String
    Private _guideSerial As Integer
    Private _rulerMouse As Point?

    Private Function InTopRuler(p As Point) As Boolean
        Return _appSettings.ShowRulers AndAlso p.Y < RulerSize AndAlso p.X >= RulerSize
    End Function

    Private Function InLeftRuler(p As Point) As Boolean
        Return _appSettings.ShowRulers AndAlso p.X < RulerSize AndAlso p.Y >= RulerSize
    End Function

    ''' <summary>點到的參考線（螢幕 4 像素內）；沒有時 −1。</summary>
    Private Function GuideAt(p As Point) As Integer
        If _recipe.Guides Is Nothing Then Return -1
        For i = _recipe.Guides.Count - 1 To 0 Step -1
            Dim gd = _recipe.Guides(i)
            Dim s = UnitToScreen(If(gd.Vertical, New PointF(CSng(gd.Pos), 0), New PointF(0, CSng(gd.Pos))))
            If If(gd.Vertical, Math.Abs(s.X - p.X), Math.Abs(s.Y - p.Y)) <= 4 Then Return i
        Next
        Return -1
    End Function

    ''' <summary>按下：在尺規上＝新增參考線；選取工具在參考線上（下面沒有物件、不是控制點）＝拖曳那條。</summary>
    Private Function GuideMouseDown(e As MouseEventArgs) As Boolean
        If e.Button <> MouseButtons.Left Then Return False
        If InTopRuler(e.Location) OrElse InLeftRuler(e.Location) Then
            Dim vertical = InLeftRuler(e.Location)
            Dim u = ScreenToUnit(e.Location)
            _guideSerial += 1
            _guideKey = "guide-" & _guideSerial
            ApplyChange(Sub(r)
                            If r.Guides Is Nothing Then r.Guides = New List(Of Guide)()
                            r.Guides.Add(New Guide With {.Vertical = vertical, .Pos = Math.Round(If(vertical, u.X, u.Y), 5)})
                        End Sub, _guideKey)
            _guideDrag = _recipe.Guides.Count - 1
            Return True
        End If
        If _appSettings.ShowRulers OrElse (_recipe.Guides IsNot Nothing AndAlso _recipe.Guides.Count > 0) Then
            If _drawStrip.SelectedTool <> DrawIcons.SelectTool Then Return False
            Dim gi = GuideAt(e.Location)
            If gi < 0 Then Return False
            Dim u = ScreenToUnit(e.Location)
            Dim sel = SelDraw(_recipe)
            If sel IsNot Nothing AndAlso HitHandle(sel, e.Location).HasValue Then Return False
            If HitLayer(u) >= 0 Then Return False
            _guideSerial += 1
            _guideKey = "guide-" & _guideSerial
            _guideDrag = gi
            Return True
        End If
        Return False
    End Function

    Private Function GuideMouseMove(e As MouseEventArgs) As Boolean
        If _appSettings.ShowRulers Then
            _rulerMouse = e.Location
            _canvas.Invalidate(New Rectangle(0, 0, _canvas.Width, RulerSize))
            _canvas.Invalidate(New Rectangle(0, 0, RulerSize, _canvas.Height))
        End If
        If _guideDrag < 0 Then
            If _drawStrip.SelectedTool = DrawIcons.SelectTool AndAlso e.Button = MouseButtons.None AndAlso _dd = DrawDrag.None AndAlso GuideAt(e.Location) >= 0 AndAlso
               HitLayer(ScreenToUnit(e.Location)) < 0 Then
                Dim gd = _recipe.Guides(GuideAt(e.Location))
                Dim c = If(gd.Vertical, Cursors.VSplit, Cursors.HSplit)
                If _canvas.Cursor IsNot c Then _canvas.Cursor = c
                Return True
            End If
            Return False
        End If
        Dim index = _guideDrag
        Dim u = ScreenToUnit(e.Location)
        If _appSettings.SnapGrid AndAlso Not ModifierKeys.HasFlag(Keys.Alt) Then
            Dim gs = GridUnits(), tol = 6 / Math.Max(0.0001, PxPerUnit())
            Dim gx = Math.Round(u.X / gs) * gs, gy = Math.Round(u.Y / gs) * gs
            If Math.Abs(gx - u.X) < tol Then u.X = CSng(gx)
            If Math.Abs(gy - u.Y) < tol Then u.Y = CSng(gy)
        End If
        ApplyChange(Sub(r)
                        If index < r.Guides.Count Then r.Guides(index).Pos = Math.Round(If(r.Guides(index).Vertical, u.X, u.Y), 5)
                    End Sub, _guideKey)
        Dim gdv = _recipe.Guides(index)
        SetStatusMessage(Lang.F("參考線：{0} 像素", CInt(Math.Round(gdv.Pos * PhotoPixelSize().Height))))
        _canvas.Invalidate()
        Return True
    End Function

    Private Function GuideMouseUp(e As MouseEventArgs) As Boolean
        If _guideDrag < 0 Then Return False
        Dim index = _guideDrag
        _guideDrag = -1
        ' 拖回尺規（或拖出畫面）：刪掉
        Dim gone = InTopRuler(e.Location) OrElse InLeftRuler(e.Location) OrElse Not _canvas.ClientRectangle.Contains(e.Location)
        If gone Then
            ApplyChange(Sub(r)
                            If index < r.Guides.Count Then r.Guides.RemoveAt(index)
                            If r.Guides.Count = 0 Then r.Guides = Nothing
                        End Sub, _guideKey)
        End If
        _canvas.Invalidate()
        Return True
    End Function

    Private Sub ClearGuides()
        ApplyChange(Sub(r) r.Guides = Nothing)
        _canvas.Invalidate()
    End Sub

    '---------------------------------------------------------------------
    ' 畫面：格線、參考線（DrawPaint 一開始）、尺規（最後）
    '---------------------------------------------------------------------

    Private Sub PaintGridAndGuides(g As Graphics)
        Dim aspect = CSng(PhotoAspect())
        Dim tl = UnitToScreen(New PointF(0, 0)), br = UnitToScreen(New PointF(aspect, 1))
        If _appSettings.ShowGrid Then
            Dim gs = GridUnits()
            Dim stepPx = gs * PxPerUnit()
            If stepPx >= 4 Then
                Using pen As New Pen(Color.FromArgb(60, 90, 120, 160), 1)
                    Dim k = 0
                    Dim x = 0.0
                    While x <= aspect + 0.000001
                        Dim sx = UnitToScreen(New PointF(CSng(x), 0)).X
                        g.DrawLine(pen, sx, tl.Y, sx, br.Y)
                        k += 1 : x = k * gs
                    End While
                    k = 0
                    Dim y = 0.0
                    While y <= 1.000001
                        Dim sy = UnitToScreen(New PointF(0, CSng(y))).Y
                        g.DrawLine(pen, tl.X, sy, br.X, sy)
                        k += 1 : y = k * gs
                    End While
                End Using
            End If
        End If
        If _recipe.Guides IsNot Nothing Then
            Using pen As New Pen(Color.FromArgb(220, 0, 170, 230), 1)
                For i = 0 To _recipe.Guides.Count - 1
                    Dim gd = _recipe.Guides(i)
                    pen.DashStyle = If(i = _guideDrag, DashStyle.Dash, DashStyle.Solid)
                    If gd.Vertical Then
                        Dim sx = UnitToScreen(New PointF(CSng(gd.Pos), 0)).X
                        g.DrawLine(pen, sx, 0, sx, _canvas.Height)
                    Else
                        Dim sy = UnitToScreen(New PointF(0, CSng(gd.Pos))).Y
                        g.DrawLine(pen, 0, sy, _canvas.Width, sy)
                    End If
                Next
            End Using
        End If
    End Sub

    ''' <summary>刻度間距（照片像素）：讓主刻度在螢幕上至少隔 60 像素（1、2、5 × 10ⁿ）。</summary>
    Private Function RulerStep(screenPerPhotoPx As Double) As Double
        Dim target = 60 / Math.Max(0.000001, screenPerPhotoPx)
        Dim p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(0.000001, target))))
        For Each m In {1.0, 2.0, 5.0, 10.0}
            If m * p >= target Then Return m * p
        Next
        Return 10 * p
    End Function

    ''' <summary>左右翻轉檢視時右上角標出來（免得忘了關）。</summary>
    Private Sub PaintMirrorBadge(g As Graphics)
        If Not _canvas.MirrorActive Then Return
        Dim text = Lang.T("↔ 左右翻轉檢視中")
        Using f As New Font(_panelFont.FontFamily, 9.5F, FontStyle.Bold), bb As New SolidBrush(Color.FromArgb(220, 47, 128, 237))
            Dim sz = g.MeasureString(text, f)
            Dim r As New RectangleF(_canvas.ClientSize.Width - sz.Width - 26, If(_appSettings.ShowRulers, RulerSize + 8, 8), sz.Width + 14, sz.Height + 6)
            g.FillRectangle(bb, r)
            g.DrawString(text, f, Brushes.White, r.X + 7, r.Y + 3)
        End Using
    End Sub

    Private Sub ToggleMirrorView()
        _canvas.MirrorView = Not _canvas.MirrorView
        If _layoutMenu.ContainsKey("flipview") Then _layoutMenu("flipview").Checked = _canvas.MirrorView
        SetStatusMessage(If(_canvas.MirrorView, Lang.T("左右翻轉檢視：只翻畫面，照片和圖形都不會變；再按一次恢復。"), Lang.T("已恢復正常檢視。")))
        _canvas.Invalidate()
    End Sub

    Private Sub PaintRulers(g As Graphics)
        If Not _appSettings.ShowRulers Then Return
        Dim photoH = PhotoPixelSize().Height
        Dim spp = PxPerUnit() / Math.Max(1, photoH) ' 螢幕像素／照片像素
        Dim stp = RulerStep(spp)
        Dim back = ThemeManager.Back(Color.FromArgb(238, 240, 244))
        Dim fore = ThemeManager.Fore(Color.FromArgb(90, 96, 106))
        Dim w = _canvas.ClientSize.Width, h = _canvas.ClientSize.Height
        g.SmoothingMode = SmoothingMode.None
        Using bb As New SolidBrush(back), pen As New Pen(fore), f As New Font("Segoe UI", 7.0F), tb As New SolidBrush(fore)
            g.FillRectangle(bb, 0, 0, w, RulerSize)
            g.FillRectangle(bb, 0, 0, RulerSize, h)
            g.DrawLine(pen, 0, RulerSize - 1, w, RulerSize - 1)
            g.DrawLine(pen, RulerSize - 1, 0, RulerSize - 1, h)
            ' 上：x
            Dim xa = ScreenToUnit(New Point(RulerSize, 0)).X * photoH, xb = ScreenToUnit(New Point(w, 0)).X * photoH
            Dim x0 = Math.Min(xa, xb), x1 = Math.Max(xa, xb) ' 翻轉檢視時數字由右往左
            Dim minor = stp / 5
            Dim k = Math.Floor(x0 / minor)
            While k * minor <= x1
                Dim v = k * minor
                Dim sx = UnitToScreen(New PointF(CSng(v / photoH), 0)).X
                If sx >= RulerSize Then
                    Dim major = Math.Abs(v / stp - Math.Round(v / stp)) < 0.0001
                    g.DrawLine(pen, sx, If(major, 2, RulerSize - 5), sx, RulerSize - 1)
                    If major Then
                        Dim txt = CInt(Math.Round(v)).ToString()
                        Dim tw = If(_canvas.MirrorActive, g.MeasureString(txt, f).Width + 2, -2) ' 翻轉時數字寫在刻度左邊
                        g.DrawString(txt, f, tb, sx - tw, 1)
                    End If
                End If
                k += 1
            End While
            ' 左：y
            Dim y0 = ScreenToUnit(New Point(0, RulerSize)).Y * photoH, y1 = ScreenToUnit(New Point(0, h)).Y * photoH
            k = Math.Floor(y0 / minor)
            While k * minor <= y1
                Dim v = k * minor
                Dim sy = UnitToScreen(New PointF(0, CSng(v / photoH))).Y
                If sy >= RulerSize Then
                    Dim major = Math.Abs(v / stp - Math.Round(v / stp)) < 0.0001
                    g.DrawLine(pen, If(major, 2, RulerSize - 5), sy, RulerSize - 1, sy)
                    If major Then
                        Dim st = g.Save()
                        g.TranslateTransform(1, sy + 2)
                        g.RotateTransform(90)
                        g.DrawString(CInt(Math.Round(v)).ToString(), f, tb, 0, -RulerSize + 3)
                        g.Restore(st)
                    End If
                End If
                k += 1
            End While
            ' 滑鼠位置
            If _rulerMouse.HasValue Then
                Using red As New Pen(Color.FromArgb(220, 210, 60, 60))
                    g.DrawLine(red, _rulerMouse.Value.X, 0, _rulerMouse.Value.X, RulerSize - 1)
                    g.DrawLine(red, 0, _rulerMouse.Value.Y, RulerSize - 1, _rulerMouse.Value.Y)
                End Using
            End If
            g.FillRectangle(bb, 0, 0, RulerSize - 1, RulerSize - 1)
        End Using
        g.SmoothingMode = SmoothingMode.AntiAlias
    End Sub

    '---------------------------------------------------------------------
    ' 連接線
    '---------------------------------------------------------------------

    ''' <summary>可以連上的物件（由上往下；跳過 exclude）；需要時給它 Id。</summary>
    Private Function ConnectTargetAt(u As PointF, Optional exclude As Integer = -1) As DrawLayer
        If _recipe.Drawings Is Nothing Then Return Nothing
        Dim order = LayerStack.Order(_recipe).Where(Function(x) Not x.IsOverlay).Select(Function(x) x.Drawing).Reverse().ToList()
        For Each d In order
            Dim i = _recipe.Drawings.IndexOf(d)
            If i = exclude OrElse Not d.Visible OrElse d.Locked OrElse Not DrawGeometry.CanConnect(d) Then Continue For
            If Not LayerHit(d, u, HitTolerance()) Then Continue For
            If d.Id Is Nothing Then d.Id = "c" & Guid.NewGuid().ToString("N").Substring(0, 11) ' 連接線要用 Id 找到它
            Return d
        Next
        Return Nothing
    End Function

    ''' <summary>拖曳點類圖形的頂點（DrawDrag.Vertex）：連接線的兩端放到物件上就連上，放在空白處就是自由的端點。</summary>
    Private Sub MoveVertex(d As DrawLayer, index As Integer, u As PointF)
        d.Points(index) = New DrawPoint(u.X, u.Y)
        If d.Shape <> DrawShape.Connector Then Return
        Dim t = ConnectTargetAt(u, _drawIndex)
        If index = 0 Then d.ConnStart = t?.Id Else If index = d.Points.Count - 1 Then d.ConnEnd = t?.Id
    End Sub

    '---------------------------------------------------------------------
    ' 可變寬度工具
    '---------------------------------------------------------------------

    Private ReadOnly _widthBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                            .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0), .Visible = False}
    Private ReadOnly _widthPreset As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 92}
    Private _widthIndex As Integer = -1
    Private _widthT As Double
    Private _widthPoint As PointF
    Private _widthKey As String
    Private _widthSerial As Integer
    Private _widthHover As PointF?

    Private Function BuildWidthBar() As Control
        For Each p In DrawGeometry.WidthPresets()
            _widthPreset.Items.Add(p.Name)
        Next
        _widthPreset.Margin = New Padding(0, 3, 2, 0)
        _help.SetHelp("draw.width.preset", _widthPreset)
        _widthBar.Controls.Add(New Label With {.Text = Lang.T("寬度"), .AutoSize = True, .Margin = New Padding(6, 7, 2, 0), .BackColor = Color.Transparent})
        _widthBar.Controls.Add(_widthPreset)
        AddHandler _widthPreset.SelectedIndexChanged, Sub()
                                                          If _syncing OrElse _widthPreset.SelectedIndex < 0 Then Return
                                                          ApplyWidthPreset(_widthPreset.SelectedIndex)
                                                      End Sub
        Return _widthBar
    End Function

    Private Sub UpdateWidthBar()
        Dim tool = If(_drawStrip Is Nothing, 0, _drawStrip.SelectedTool)
        _widthBar.Visible = tool = DrawIcons.WidthTool
        If Not _widthBar.Visible Then _widthHover = Nothing
    End Sub

    ''' <summary>寬度樣式套到選取的線條（沒有選取時給之後畫的線）。</summary>
    Private Sub ApplyWidthPreset(index As Integer)
        Dim stops = DrawGeometry.WidthPresets()(index).Stops
        Dim make = Function() If(stops.Length = 0, Nothing, stops.Select(Function(s) s.Clone()).ToList())
        Dim idx = SelectedIndices().Where(Function(i) Not _recipe.Drawings(i).Locked AndAlso DrawGeometry.CanVaryWidth(_recipe.Drawings(i))).ToList()
        If idx.Count = 0 Then
            _drawStyle.WidthProfile = make()
            SetStatusMessage(Lang.F("之後畫的線條用「{0}」寬度。", DrawGeometry.WidthPresets()(index).Name))
            Return
        End If
        ApplyChange(Sub(r)
                        For Each i In idx
                            r.Drawings(i).WidthProfile = make()
                        Next
                    End Sub)
        _canvas.Invalidate()
    End Sub

    ''' <summary>寬度工具要改的線：選取的優先，否則點到的；要在線附近（線寬加螢幕 8 像素內）。</summary>
    Private Function WidthTarget(u As PointF) As (Index As Integer, T As Double, Point As PointF)?
        Dim tol = 8 / Math.Max(0.0001, PxPerUnit())
        Dim candidates = SelectedIndices().ToList()
        Dim hit = HitLayer(u)
        If hit >= 0 AndAlso Not candidates.Contains(hit) Then candidates.Add(hit)
        For Each i In candidates
            Dim d = _recipe.Drawings(i)
            If d.Locked OrElse Not DrawGeometry.CanVaryWidth(d) Then Continue For
            Dim n = DrawGeometry.NearestOnStroke(d, u)
            If Not n.HasValue Then Continue For
            Dim maxW = If(d.WidthProfile Is Nothing OrElse d.WidthProfile.Count = 0, 1.0, Math.Max(1, d.WidthProfile.Max(Function(s) s.W)))
            If n.Value.Distance <= d.StrokeWidth * maxW / 2 + tol Then Return (i, n.Value.T, n.Value.Point)
        Next
        Return Nothing
    End Function

    ''' <summary>按下：在線上拖曳＝把那裡變粗變細（拖離線越遠越粗）；Alt＋點控制點＝刪掉。</summary>
    Private Sub WidthDown(e As MouseEventArgs, u As PointF)
        If e.Button <> MouseButtons.Left Then Return
        Dim t = WidthTarget(u)
        If Not t.HasValue Then SetStatusMessage(Lang.T("寬度工具：在線條上按住往外拖。")) : Return
        Dim d = _recipe.Drawings(t.Value.Index)
        If Not SelectedIndices().Contains(t.Value.Index) Then SetMultiSelection({t.Value.Index}, t.Value.Index)
        Dim nearStop = If(d.WidthProfile, New List(Of WidthStop)()).Select(Function(s, k) (k, s)).
            Where(Function(x) Math.Abs(x.s.T - t.Value.T) < 0.04).OrderBy(Function(x) Math.Abs(x.s.T - t.Value.T)).Select(Function(x) x.k).DefaultIfEmpty(-1).First()
        If ModifierKeys.HasFlag(Keys.Alt) Then
            If nearStop >= 0 Then
                Dim index = t.Value.Index
                ApplyChange(Sub(r)
                                r.Drawings(index).WidthProfile.RemoveAt(nearStop)
                                If r.Drawings(index).WidthProfile.Count = 0 Then r.Drawings(index).WidthProfile = Nothing
                            End Sub)
            End If
            Return
        End If
        _widthIndex = t.Value.Index
        _widthT = If(nearStop >= 0, d.WidthProfile(nearStop).T, Math.Round(t.Value.T, 4))
        _widthPoint = t.Value.Point
        _widthSerial += 1
        _widthKey = "width-" & _widthSerial
    End Sub

    Private Sub WidthMove(u As PointF)
        If _widthIndex < 0 Then
            Dim t = WidthTarget(u)
            _widthHover = If(t.HasValue, t.Value.Point, CType(Nothing, PointF?))
            _canvas.Invalidate()
            Return
        End If
        Dim index = _widthIndex, at = _widthT
        Dim d = _recipe.Drawings(index)
        Dim w = Math.Round(Math.Max(0, Math.Min(5, 2 * VectorEffects.Dist(u, _widthPoint) / Math.Max(0.0001, d.StrokeWidth))), 3)
        ApplyChange(Sub(r)
                        Dim layer = r.Drawings(index)
                        If layer.WidthProfile Is Nothing OrElse layer.WidthProfile.Count = 0 Then
                            layer.WidthProfile = New List(Of WidthStop) From {New WidthStop With {.T = 0, .W = 1}, New WidthStop With {.T = 1, .W = 1}}
                        End If
                        Dim s = layer.WidthProfile.FirstOrDefault(Function(x) Math.Abs(x.T - at) < 0.0001)
                        If s Is Nothing Then layer.WidthProfile.Add(New WidthStop With {.T = at, .W = w}) Else s.W = w
                        layer.WidthProfile = layer.WidthProfile.OrderBy(Function(x) x.T).ToList()
                    End Sub, _widthKey)
        SetStatusMessage(Lang.F("寬度：{0}%", CInt(w * 100)))
        _canvas.Invalidate()
    End Sub

    Private Sub WidthUp()
        _widthIndex = -1
    End Sub

    ''' <summary>線上 t（0..1）的位置（第一條線）。</summary>
    Private Shared Function PointOnStroke(d As DrawLayer, t As Double) As PointF?
        Dim f = DrawGeometry.Figures(d).FirstOrDefault()
        If f.Points Is Nothing OrElse f.Points.Length < 2 Then Return Nothing
        Dim pts = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
        Dim total = 0.0
        For i = 1 To pts.Length - 1 : total += VectorEffects.Dist(pts(i - 1), pts(i)) : Next
        Dim target = t * total, acc = 0.0
        For i = 1 To pts.Length - 1
            Dim l = VectorEffects.Dist(pts(i - 1), pts(i))
            If acc + l >= target AndAlso l > 0 Then
                Dim k = (target - acc) / l
                Return New PointF(CSng(pts(i - 1).X + (pts(i).X - pts(i - 1).X) * k), CSng(pts(i - 1).Y + (pts(i).Y - pts(i - 1).Y) * k))
            End If
            acc += l
        Next
        Return pts(pts.Length - 1)
    End Function

    ''' <summary>寬度工具：選取的線上畫控制點（菱形），滑鼠在線上時畫一個點。</summary>
    Private Sub PaintWidthStops(g As Graphics)
        If _drawStrip.SelectedTool <> DrawIcons.WidthTool Then Return
        Using br As New SolidBrush(Color.FromArgb(240, 255, 255, 255)), pen As New Pen(Color.FromArgb(230, 40, 110, 210), 1.4F)
            For Each i In SelectedIndices()
                Dim d = _recipe.Drawings(i)
                If d.WidthProfile Is Nothing Then Continue For
                For Each s In d.WidthProfile
                    Dim p = PointOnStroke(d, s.T)
                    If Not p.HasValue Then Continue For
                    Dim c = UnitToScreen(p.Value)
                    Dim pts = {New PointF(c.X, c.Y - 5), New PointF(c.X + 5, c.Y), New PointF(c.X, c.Y + 5), New PointF(c.X - 5, c.Y)}
                    g.FillPolygon(br, pts) : g.DrawPolygon(pen, pts)
                Next
            Next
            If _widthHover.HasValue Then
                Dim c = UnitToScreen(_widthHover.Value)
                g.DrawEllipse(pen, c.X - 4, c.Y - 4, 8, 8)
            End If
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 圖樣填色（上方圖樣列；「填色方式 → 圖樣」打開）
    '---------------------------------------------------------------------

    Private Shared ReadOnly PatternNames As String() = {Lang.T("條紋"), Lang.T("圓點"), Lang.T("棋盤"), Lang.T("格線"), Lang.T("斜格"), Lang.T("磚牆"),
                                                        Lang.T("波浪"), Lang.T("菱形"), Lang.T("材質")}
    Private ReadOnly _patternBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                              .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0), .Visible = False}
    Private ReadOnly _patternKind As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 64}
    Private ReadOnly _patternSize As New NumericUpDown With {.Minimum = 2, .Maximum = 2000, .Width = 56}
    Private ReadOnly _patternAngle As New NumericUpDown With {.Minimum = -180, .Maximum = 180, .Width = 52}
    Private ReadOnly _patternMaterial As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 90}
    Private _patternSyncing As Boolean

    Private Function BuildPatternBar() As Control
        _patternKind.Items.AddRange(PatternNames)
        _patternMaterial.Items.AddRange(DrawGeometry.MaterialNames)
        Dim add = Sub(caption As String, c As Control, help As String)
                      If caption <> "" Then _patternBar.Controls.Add(New Label With {.Text = caption, .AutoSize = True, .Margin = New Padding(6, 7, 2, 0), .BackColor = Color.Transparent})
                      c.Margin = New Padding(0, 3, 2, 0)
                      _help.SetHelp(help, c)
                      _patternBar.Controls.Add(c)
                  End Sub
        add(Lang.T("圖樣"), _patternKind, "draw.pattern.kind")
        add("", _patternMaterial, "draw.pattern.kind")
        add(Lang.T("大小"), _patternSize, "draw.pattern.size")
        add(Lang.T("角度"), _patternAngle, "draw.pattern.angle")
        AddHandler _patternKind.SelectedIndexChanged, Sub() OnPatternChanged()
        AddHandler _patternMaterial.SelectedIndexChanged, Sub() OnPatternChanged()
        AddHandler _patternSize.ValueChanged, Sub() OnPatternChanged()
        AddHandler _patternAngle.ValueChanged, Sub() OnPatternChanged()
        Return _patternBar
    End Function

    Private Sub UpdatePatternBar()
        Dim st = If(StyleTarget(_recipe), _drawStyle)
        Dim tool = If(_drawStrip Is Nothing, 0, _drawStrip.SelectedTool)
        _patternBar.Visible = st IsNot Nothing AndAlso st.FillPattern IsNot Nothing AndAlso st.Filled AndAlso
                              tool <> DrawIcons.TouchUpTool AndAlso tool <> DrawIcons.WidthTool
        If Not _patternBar.Visible Then Return
        _patternSyncing = True
        Try
            Dim pf = st.FillPattern
            _patternKind.SelectedIndex = Math.Max(0, Math.Min(PatternNames.Length - 1, CInt(pf.Kind)))
            _patternMaterial.Visible = pf.Kind = PatternKind.Material
            If _patternMaterial.Items.Count > 0 Then _patternMaterial.SelectedIndex = Math.Max(0, Math.Min(_patternMaterial.Items.Count - 1, CInt(pf.Material)))
            _patternSize.Value = CDec(Math.Max(2, Math.Min(2000, Math.Round(pf.Scale * PhotoPixelSize().Height))))
            _patternAngle.Value = CDec(Math.Max(-180, Math.Min(180, Math.Round(pf.Angle))))
            _patternSize.Enabled = pf.Kind <> PatternKind.Material
            _patternAngle.Enabled = pf.Kind <> PatternKind.Material
        Finally
            _patternSyncing = False
        End Try
    End Sub

    Private Sub OnPatternChanged()
        If _patternSyncing OrElse _syncing Then Return
        Dim kind = CType(Math.Max(0, _patternKind.SelectedIndex), PatternKind)
        Dim mat = CType(Math.Max(0, _patternMaterial.SelectedIndex), MaterialKind)
        Dim scale = Math.Round(PxToUnits(CDbl(_patternSize.Value)), 5), angle = CDbl(_patternAngle.Value)
        SetDrawProp(Sub(d)
                        If d.FillPattern Is Nothing Then Return
                        d.FillPattern.Kind = kind : d.FillPattern.Material = mat
                        d.FillPattern.Scale = scale : d.FillPattern.Angle = angle
                    End Sub, "pattern")
        _patternMaterial.Visible = kind = PatternKind.Material
    End Sub

    ''' <summary>填色方式選了「圖樣」：預設條紋，大小是照片高度的 4%。</summary>
    Private Shared Function NewPattern() As PatternFill
        Return New PatternFill With {.Kind = PatternKind.Stripes, .Scale = 0.04}
    End Function

    '---------------------------------------------------------------------
    ' 文件色票（全域色）
    '---------------------------------------------------------------------

    Private Sub RefreshSwatches()
        _drawStrip.SetSwatches(If(_recipe?.Swatches, New List(Of ColorSwatch)()))
    End Sub

    ''' <summary>點色票：左鍵線條色、右鍵填色，並連到這個色票（改色票時跟著變）。</summary>
    Private Sub PickSwatch(target As Integer, id As String)
        Dim sw = _recipe.Swatches?.FirstOrDefault(Function(x) x.Id = id)
        If sw Is Nothing Then Return
        Dim argb = sw.Argb
        If target = 1 Then
            SetDrawProp(Sub(d)
                            d.FillColorArgb = argb : d.Filled = True : d.FillSwatch = id
                        End Sub)
        Else
            SetDrawProp(Sub(d)
                            d.StrokeColorArgb = argb : d.StrokeSwatch = id
                        End Sub)
        End If
    End Sub

    ''' <summary>「＋」：目前的填色（右鍵）或線條色（左鍵）加成新色票，選取的物件也連上它。</summary>
    Private Sub AddSwatch(target As Integer)
        Dim st = If(StyleTarget(_recipe), _drawStyle)
        Dim argb = Color.FromArgb(255, Color.FromArgb(If(target = 1, st.FillColorArgb, st.StrokeColorArgb))).ToArgb()
        Dim id = "w" & Guid.NewGuid().ToString("N").Substring(0, 11)
        Dim n = If(_recipe.Swatches Is Nothing, 0, _recipe.Swatches.Count) + 1
        ApplyChange(Sub(r)
                        If r.Swatches Is Nothing Then r.Swatches = New List(Of ColorSwatch)()
                        r.Swatches.Add(New ColorSwatch With {.Id = id, .Name = Lang.F("色票 {0}", n), .Argb = argb})
                    End Sub)
        PickSwatch(target, id)
        RefreshSwatches()
        SetStatusMessage(Lang.T("已加入色票：改色票的顏色時，用到它的物件一起變（按兩下色票改顏色、名稱或刪除）。"))
    End Sub

    Private ReadOnly _swatchMenu As New ContextMenuStrip()

    ''' <summary>按兩下色票：改顏色、改名稱、刪除。</summary>
    Private Sub ShowSwatchMenu(id As String)
        Dim sw = _recipe.Swatches?.FirstOrDefault(Function(x) x.Id = id)
        If sw Is Nothing Then Return
        _swatchMenu.Items.Clear()
        _swatchMenu.Items.Add(Lang.F("「{0}」：{1} 個物件", sw.Name, DrawGeometry.SwatchUsage(_recipe, id))).Enabled = False
        _swatchMenu.Items.Add(Lang.T("改顏色…"), Nothing, Sub()
                                                            Using dlg As New ColorDialog With {.Color = Color.FromArgb(sw.Argb), .FullOpen = True}
                                                                If dlg.ShowDialog(Me) = DialogResult.OK Then EditSwatchColor(id, dlg.Color)
                                                            End Using
                                                        End Sub)
        _swatchMenu.Items.Add(Lang.T("改名稱…"), Nothing, Sub()
                                                            Dim name = AskText(Lang.T("色票名稱"), sw.Name)
                                                            If name IsNot Nothing Then RenameSwatch(id, name)
                                                        End Sub)
        _swatchMenu.Items.Add(Lang.T("刪除色票"), Nothing, Sub() DeleteSwatch(id))
        _swatchMenu.Show(Cursor.Position)
    End Sub

    Private Sub EditSwatchColor(id As String, c As Color)
        Dim argb = Color.FromArgb(255, c).ToArgb()
        ApplyChange(Sub(r) DrawGeometry.ChangeSwatch(r, id, argb))
        RefreshSwatches()
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    Private Sub RenameSwatch(id As String, name As String)
        ApplyChange(Sub(r)
                        Dim sw = r.Swatches?.FirstOrDefault(Function(x) x.Id = id)
                        If sw IsNot Nothing Then sw.Name = name.Trim()
                    End Sub)
        RefreshSwatches()
    End Sub

    Private Sub DeleteSwatch(id As String)
        ApplyChange(Sub(r) DrawGeometry.RemoveSwatch(r, id))
        _drawStyle.FillSwatch = If(_drawStyle.FillSwatch = id, Nothing, _drawStyle.FillSwatch)
        _drawStyle.StrokeSwatch = If(_drawStyle.StrokeSwatch = id, Nothing, _drawStyle.StrokeSwatch)
        RefreshSwatches()
    End Sub

    ''' <summary>簡單的輸入一行文字視窗；取消時 Nothing。</summary>
    Private Function AskText(title As String, value As String) As String
        Using dlg As New Form With {.Text = title, .FormBorderStyle = FormBorderStyle.FixedDialog, .MaximizeBox = False, .MinimizeBox = False,
                                   .ShowInTaskbar = False, .StartPosition = FormStartPosition.CenterParent, .ClientSize = New Size(300, 92), .Font = _panelFont}
            Dim tb As New TextBox With {.Text = value}
            tb.SetBounds(14, 14, 272, 26)
            Dim ok As New Button With {.Text = Lang.T("確定"), .DialogResult = DialogResult.OK}
            Dim cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel}
            ok.SetBounds(100, 50, 88, 30) : cancel.SetBounds(198, 50, 88, 30)
            dlg.Controls.AddRange({tb, ok, cancel})
            dlg.AcceptButton = ok : dlg.CancelButton = cancel
            ThemeManager.Attach(dlg)
            Return If(dlg.ShowDialog(Me) = DialogResult.OK AndAlso tb.Text.Trim() <> "", tb.Text.Trim(), Nothing)
        End Using
    End Function

    '---------------------------------------------------------------------
    ' 指令、快捷鍵
    '---------------------------------------------------------------------

    Private Function RunBatch6Command(name As String) As Boolean
        Select Case name
            Case "rulers", "grid", "snapgrid", "snapguides" : ToggleLayout(name)
            Case "gridsize" : ShowGridSpacing()
            Case "clearguides" : ClearGuides()
            Case "flipview" : ToggleMirrorView()
            Case Else : Return False
        End Select
        Return True
    End Function

    ''' <summary>Ctrl+Alt+R 尺規、Ctrl+' 格線。</summary>
    Private Function Batch6Key(keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Control Or Keys.Alt Or Keys.R : ToggleLayout("rulers") : Return True
            Case Keys.Control Or Keys.Oem7 : ToggleLayout("grid") : Return True
        End Select
        Return False
    End Function
End Class
