Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 繪圖分頁的進階筆刷設定（參考 Corel Painter）：間距、散佈、大小與顏色變化、筆壓濃淡、混色濕度、
''' 繪圖筆的傾斜與旋轉、對稱／萬花筒；以及仿製筆的來源（Alt 點選）。
''' 「進階」區平常收起來，點標題展開，下面的控制項跟著往下移。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _drawAdvToggle As New Label()
    Private ReadOnly _drawAdvPanel As New Panel()
    Private ReadOnly _drawPenTilt As New CheckBox()
    Private ReadOnly _drawPenRot As New CheckBox()
    Private ReadOnly _drawSym As New ComboBox()
    Private _drawAdvPage As Control

    ''' <summary>仿製筆的來源（照片高度單位）；Alt 點選設定。</summary>
    Private _cloneSource As PointF?
    ''' <summary>來源與第一筆起點的距離（對齊模式）；重新設定來源時清掉。</summary>
    Private _cloneOffset As PointF?

    Private Shared ReadOnly SymmetryNames As String() = {"不對稱", "左右對稱", "上下對稱", "上下左右", "旋轉 N 等分", "萬花筒 N 等分"}

    ''' <summary>筆畫上的一點：筆壓之外也記下繪圖筆的傾斜與旋轉。</summary>
    Private Function StrokePoint(u As PointF, p As Point, first As Boolean) As DrawPoint
        Dim d As New DrawPoint(u.X, u.Y, StrokePressure(p, first))
        Dim a = _canvas.PenAngles
        d.Tx = a.TiltX : d.Ty = a.TiltY : d.R = a.Rotation
        Return d
    End Function

    '=====================================================================
    ' 筆畫穩定器（拉線式）：自由繪製與直接繪製共用
    '=====================================================================

    Private _stabilizer As StrokeStabilizer

    ''' <summary>穩定器的線長（螢幕像素）：設定 0..100 → 0..48 px，縮放畫面時手感一樣。</summary>
    Private Function StabilizerRadius() As Single
        Return Math.Max(0, Math.Min(100, _appSettings.Stabilizer)) * 0.48F
    End Function

    Private Sub StartFreehand(u As PointF, p As Point)
        _freePoints = New List(Of DrawPoint) From {StrokePoint(u, p, True)}
        Dim radius = StabilizerRadius()
        _stabilizer = If(radius >= 1, New StrokeStabilizer(p, radius), Nothing)
    End Sub

    ''' <summary>拖曳中：有穩定器時畫的是被拉著走的筆尖，不是游標本身。</summary>
    Private Sub FreehandMove(cursor As Point)
        Dim target As PointF = cursor
        If _stabilizer IsNot Nothing Then
            _stabilizer.Pull(cursor)
            target = _stabilizer.Anchor
            _canvas.Invalidate() ' 拉線跟著游標
        End If
        AddFreehandPoint(target)
    End Sub

    Private Sub AddFreehandPoint(target As PointF)
        Dim last = UnitToScreen(_freePoints(_freePoints.Count - 1).ToPointF())
        Dim minStep = Math.Max(1.5, _drawStyle.StrokeWidth * PxPerUnit() / 8)
        If ScreenDist(last, target) >= minStep Then
            Dim pt = Point.Round(target)
            _freePoints.Add(StrokePoint(ScreenToUnit(pt), pt, False))
            _canvas.Invalidate()
        End If
    End Sub

    ''' <summary>放開：筆尖補到放開的位置，筆畫才不會比手畫的短一截。</summary>
    Private Sub FinishStabilizer(cursor As Point)
        Dim st = _stabilizer
        _stabilizer = Nothing
        If st Is Nothing OrElse _freePoints Is Nothing Then Return
        For Each p In st.Finish(cursor, CSng(Math.Max(1.5, _drawStyle.StrokeWidth * PxPerUnit() / 8)))
            AddFreehandPoint(p)
        Next
    End Sub

    ''' <summary>畫布上：穩定器的拉線（游標到筆尖）與線長範圍。</summary>
    Private Sub PaintStabilizer(g As Graphics)
        Dim st = _stabilizer
        If st Is Nothing OrElse _dd <> DrawDrag.Freehand Then Return
        Dim cur = _canvas.PointToClient(_canvas.PointerPosition) ' 用筆時系統游標不會跟著動，問畫布筆在哪
        Dim a = st.Anchor
        Using outer As New Pen(Color.FromArgb(120, 0, 0, 0), 3), inner As New Pen(Color.FromArgb(230, 255, 255, 255), 1.2F) With {.DashStyle = DashStyle.Dot}
            g.DrawLine(outer, a, cur)
            g.DrawLine(inner, a, cur)
            Dim r = st.Radius
            g.DrawEllipse(inner, a.X - r, a.Y - r, r * 2, r * 2)
        End Using
        g.FillEllipse(Brushes.White, a.X - 3, a.Y - 3, 6, 6)
        g.DrawEllipse(Pens.Black, a.X - 3, a.Y - 3, 6, 6)
    End Sub

    Private Sub SetCloneSource(u As PointF)
        _cloneSource = u
        _cloneOffset = Nothing
        SetStatusMessage("已設定仿製來源；接著在要畫的地方拖曳，第一筆的起點對準來源。")
        _canvas.Invalidate()
    End Sub

    '=====================================================================
    ' 版面
    '=====================================================================

    Private Sub BuildDrawAdvanced(L As PageLayout)
        _drawAdvPage = L.Page
        _drawAdvToggle.AutoSize = False
        _drawAdvToggle.Cursor = Cursors.Hand
        _drawAdvToggle.Font = New Font(_panelFont, FontStyle.Bold)
        _drawAdvToggle.ForeColor = Color.FromArgb(40, 70, 120)
        _drawAdvToggle.BackColor = Color.FromArgb(226, 231, 239)
        _drawAdvToggle.TextAlign = ContentAlignment.MiddleLeft
        _drawAdvToggle.SetBounds(6, L.Y + 2, L.Width - 6, 26)
        _help.SetHelp("draw.adv", _drawAdvToggle)
        AddHandler _drawAdvToggle.Click, Sub() ToggleDrawAdvanced()
        L.Add(_drawAdvToggle)
        L.Y += 32

        _drawAdvPanel.BackColor = Color.Transparent
        _drawAdvPanel.Visible = False
        Dim A As New PageLayout(_drawAdvPanel, L.Width, rightMargin:=0) With {.Y = 0}
        Dim pct As Func(Of Integer, String) = Function(v) v & "%"
        AddRow(A, DrawRow("dw_spacing", "間距", 0, 200, Function(v) If(v = 0, "自動", v & "%"), Function(d) d.SpacingPct, Sub(d, v) d.SpacingPct = v))
        AddRow(A, DrawRow("dw_scatter", "散佈", 0, 100, pct, Function(d) d.Scatter, Sub(d, v) d.Scatter = v))
        AddRow(A, DrawRow("dw_sizej", "大小變化", 0, 100, pct, Function(d) d.SizeJitter, Sub(d, v) d.SizeJitter = v))
        AddRow(A, DrawRow("dw_huej", "色相變化", 0, 100, pct, Function(d) d.HueJitter, Sub(d, v) d.HueJitter = v))
        AddRow(A, DrawRow("dw_lumj", "明暗變化", 0, 100, pct, Function(d) d.LumJitter, Sub(d, v) d.LumJitter = v))
        AddRow(A, DrawRow("dw_presop", "筆壓濃淡", 0, 100, pct, Function(d) d.PressureOpacity, Sub(d, v) d.PressureOpacity = v))
        AddRow(A, DrawRow("dw_wet", "濕度", 0, 100, pct, Function(d) d.Wet, Sub(d, v) d.Wet = v))
        AddRow(A, DrawRow("dw_papergrain", "紙紋吃色", 0, 100, pct, Function(d) d.PaperGrain, Sub(d, v) d.PaperGrain = v))
        ' 穩定器是手感設定，不存在圖層裡（記在 settings.json，換圖層、換文件都一樣）
        AddRow(A, New SliderRow With {.Key = "dw_stabilizer", .Caption = "穩定器", .Minimum = 0, .Maximum = 100,
                                      .Format = Function(v) If(v = 0, "關", v.ToString()),
                                      .GetValue = Function(r) _appSettings.Stabilizer,
                                      .SetValue = Sub(r, v)
                                                      If _appSettings.Stabilizer = v Then Return
                                                      _appSettings.Stabilizer = v
                                                      _appSettings.Save()
                                                  End Sub})

        Dim half = (A.Width - 8 - 6) \ 2
        _drawPenTilt.Text = "筆傾斜改變筆尖"
        _drawPenRot.Text = "筆旋轉改變筆尖"
        For Each chk In {(_drawPenTilt, 0, "draw.pentilt"), (_drawPenRot, 1, "draw.penrot")}
            Dim cb = chk.Item1
            cb.BackColor = Color.Transparent
            cb.SetBounds(8 + chk.Item2 * (half + 6), A.Y + 4, half, 24)
            _help.SetHelp(chk.Item3, cb)
            A.Add(cb)
        Next
        AddHandler _drawPenTilt.CheckedChanged, Sub() If Not _syncing Then SetDrawProp(Sub(d) d.PenTilt = _drawPenTilt.Checked)
        AddHandler _drawPenRot.CheckedChanged, Sub() If Not _syncing Then SetDrawProp(Sub(d) d.PenRotation = _drawPenRot.Checked)
        A.Y += 32

        _help.SetHelpLinked("draw.sym", _drawSym, AddCaption(A, "對稱", A.Y), _drawSym)
        _drawSym.DropDownStyle = ComboBoxStyle.DropDownList
        _drawSym.Items.AddRange(SymmetryNames.Cast(Of Object)().ToArray())
        _drawSym.SetBounds(8 + CaptionWidth, A.Y + 3, A.Width - CaptionWidth - 8, 24)
        AddHandler _drawSym.SelectedIndexChanged, Sub()
                                                       If _syncing OrElse _drawSym.SelectedIndex < 0 Then Return
                                                       Dim kind = CType(_drawSym.SelectedIndex, SymmetryKind)
                                                       Dim cx = Math.Round(PhotoAspect() / 2, 5)
                                                       SetDrawProp(Sub(d)
                                                                       d.Symmetry = kind
                                                                       d.SymX = cx : d.SymY = 0.5
                                                                   End Sub)
                                                   End Sub
        A.Add(_drawSym)
        A.Y += RowHeight
        AddRow(A, DrawRow("dw_symcount", "等分", 2, 16, AddressOf Plain, Function(d) d.SymCount, Sub(d, v) d.SymCount = v))

        _drawAdvPanel.SetBounds(0, L.Y, L.Width, A.Y + 4)
        L.Add(_drawAdvPanel)
        UpdateAdvToggleText()
    End Sub

    Private Sub UpdateAdvToggleText()
        _drawAdvToggle.Text = If(_drawAdvPanel.Visible, "▾ 進階", "▸ 進階") & "（間距、散佈、顏色變化、繪圖筆、對稱）"
    End Sub

    ''' <summary>展開／收起「進階」：下面的控制項（字型、圖層清單…）跟著上下移。</summary>
    Private Sub ToggleDrawAdvanced()
        Dim show = Not _drawAdvPanel.Visible
        Dim delta = If(show, _drawAdvPanel.Height, -_drawAdvPanel.Height)
        Dim page = _drawAdvPage
        page.SuspendLayout()
        For Each c As Control In page.Controls
            If c IsNot _drawAdvPanel AndAlso c.Top >= _drawAdvPanel.Top Then c.Top += delta
        Next
        _drawAdvPanel.Visible = show
        _layerTop += delta
        _layerList.Height = Math.Max(110, page.ClientSize.Height - _layerTop - 8)
        page.ResumeLayout()
        UpdateAdvToggleText()
    End Sub

    ''' <summary>同步進階區的勾選框與對稱選單（滑桿由 SyncSliders 同步）。</summary>
    Private Sub UpdateDrawAdvanced(st As DrawLayer)
        _drawPenTilt.Checked = st.PenTilt
        _drawPenRot.Checked = st.PenRotation
        _drawSym.SelectedIndex = Math.Max(0, Math.Min(SymmetryNames.Length - 1, CInt(st.Symmetry)))
    End Sub

    '=====================================================================
    ' 畫布上的提示：對稱的預覽線、仿製來源
    '=====================================================================

    ''' <summary>拖曳中的筆畫照目前的對稱設定畫出其他複本的預覽。</summary>
    Private Sub PaintSymmetryPreview(g As Graphics, pen As Pen, pts As List(Of DrawPoint))
        If _drawStyle.Symmetry = SymmetryKind.None OrElse pts Is Nothing OrElse pts.Count < 2 Then Return
        Dim fig As New DrawGeometry.Figure With {.Points = pts.Select(Function(p) p.ToPointF()).ToArray()}
        Dim copies = DrawGeometry.ApplySymmetry(New List(Of DrawGeometry.Figure) From {fig}, _drawStyle.Symmetry, _drawStyle.SymCount, _drawStyle.SymX, _drawStyle.SymY)
        For i = 1 To copies.Count - 1
            g.DrawLines(pen, copies(i).Points.Select(Function(p) UnitToScreen(p)).ToArray())
        Next
        ' 對稱中心
        Dim c = UnitToScreen(New PointF(CSng(_drawStyle.SymX), CSng(_drawStyle.SymY)))
        Using dash As New Pen(Color.FromArgb(160, 255, 255, 255), 1) With {.DashStyle = DashStyle.Dash}
            g.DrawEllipse(dash, c.X - 6, c.Y - 6, 12, 12)
        End Using
    End Sub

    ''' <summary>仿製筆：來源處畫十字準星；拖曳中準星跟著筆移動。</summary>
    Private Sub PaintCloneSource(g As Graphics)
        If _drawStrip.SelectedTool <> CInt(DrawShape.Raster) OrElse _drawStyle.Brush <> BrushKind.Clone OrElse Not _cloneSource.HasValue Then Return
        Dim src = _cloneSource.Value
        If _dd = DrawDrag.Freehand AndAlso _freePoints IsNot Nothing AndAlso _freePoints.Count > 0 Then
            Dim first = _freePoints(0), last = _freePoints(_freePoints.Count - 1)
            Dim off = If(_cloneOffset, New PointF(src.X - first.X, src.Y - first.Y))
            src = New PointF(last.X + off.X, last.Y + off.Y)
        End If
        Dim p = UnitToScreen(src)
        Using outer As New Pen(Color.FromArgb(170, 0, 0, 0), 3), inner As New Pen(Color.White, 1.4F)
            For Each pen In {outer, inner}
                g.DrawEllipse(pen, p.X - 9, p.Y - 9, 18, 18)
                g.DrawLine(pen, p.X - 14, p.Y, p.X - 4, p.Y) : g.DrawLine(pen, p.X + 4, p.Y, p.X + 14, p.Y)
                g.DrawLine(pen, p.X, p.Y - 14, p.X, p.Y - 4) : g.DrawLine(pen, p.X, p.Y + 4, p.X, p.Y + 14)
            Next
        End Using
    End Sub
End Class
