Imports System.Drawing.Drawing2D
Imports AutoComposite

''' <summary>畫布工具。</summary>
Friend Enum CanvasTool
    ''' <summary>拖曳人物；滾輪縮放檢視，Shift+滾輪縮放人物，Ctrl+滾輪旋轉人物。</summary>
    Move
    ''' <summary>點選背景目標錨點（例如柱頂中心）。</summary>
    SetTarget
    ''' <summary>拖曳出可站立表面的左右端（例如柱頂平台邊緣）。</summary>
    SetSurface
    ''' <summary>拖曳畫一條參考線（例如石柱邊緣），兩條用來推估透視。</summary>
    DrawGuide
    ''' <summary>遮罩筆刷（左鍵增加、右鍵或 Alt 減少）。</summary>
    PaintMask
    Pan
End Enum

''' <summary>
''' 合成畫布：顯示預覽、縮放平移、人物拖曳與鍵盤微調、錨點／參考線／遮罩筆刷等工具與疊圖。
''' 所有輸出座標都換算成背景原圖像素，畫面縮放與 DPI 不影響合成結果。
''' </summary>
Friend NotInheritable Class CompositeCanvas
    Inherits Control

    Private _workflow As CompositeWorkflow
    Private _preview As Bitmap
    Private _maskOverlay As Bitmap
    Private _zoom As Double = 1
    Private _offset As New PointF(0, 0)
    Private _fitPending As Boolean = True

    Private _dragStart As Point
    Private _dragging As Boolean
    Private _dragButton As MouseButtons
    Private _layerStart As PointD
    Private _panStart As PointF
    Private _lineStart As PointD
    Private _lineEnd As PointD
    Private _stroke As BrushStroke
    Private _mouseBg As PointD
    Private _spaceDown As Boolean

    Public Property Tool As CanvasTool = CanvasTool.Move
    ''' <summary>筆刷半徑（螢幕像素）。</summary>
    Public Property BrushRadiusScreen As Double = 24
    Public Property BrushSoftness As Double = 0.5
    Public Property ShowOverlays As Boolean = True
    Public Property MaskOverlayColor As Color = Color.FromArgb(120, 255, 40, 40)

    ''' <summary>預覽需要重算：interactive = True 表示正在拖曳（用較低解析度）。</summary>
    Public Event RenderRequested(interactive As Boolean)
    Public Event LayerEdited(label As String, committed As Boolean)
    Public Event LayerSelected(id As String)
    Public Event TargetPicked(p As PointD)
    Public Event SurfacePicked(left As PointD, right As PointD)
    Public Event GuidePicked(g As GuideLine)
    Public Event StrokeCompleted(stroke As BrushStroke)
    Public Event CursorMoved(p As PointD)

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        BackColor = Color.FromArgb(48, 48, 52)
        TabStop = True
    End Sub

    Public Sub Attach(wf As CompositeWorkflow)
        _workflow = wf
    End Sub

    ''' <summary>換上新的預覽圖（舊的會被釋放）。</summary>
    Public Sub SetPreview(bmp As Bitmap)
        Dim old = _preview
        _preview = bmp
        old?.Dispose()
        If _fitPending AndAlso _workflow?.Scene.Background IsNot Nothing Then FitToWindow()
        Invalidate()
    End Sub

    Public Sub SetMaskOverlay(bmp As Bitmap)
        Dim old = _maskOverlay
        _maskOverlay = bmp
        old?.Dispose()
        Invalidate()
    End Sub

    Public Sub RequestFit()
        _fitPending = True
        If _workflow?.Scene.Background IsNot Nothing Then FitToWindow()
    End Sub

    Public Sub FitToWindow()
        Dim s = _workflow?.Scene
        If s?.Background Is Nothing OrElse Width < 10 OrElse Height < 10 Then Return
        _zoom = Math.Min((Width - 20) / CDbl(s.Width), (Height - 20) / CDbl(s.Height))
        _offset = New PointF(CSng((Width - s.Width * _zoom) / 2), CSng((Height - s.Height * _zoom) / 2))
        _fitPending = False
        Invalidate()
    End Sub

    Public Sub ZoomActual()
        Dim s = _workflow?.Scene
        If s?.Background Is Nothing Then Return
        ZoomAt(New PointF(Width / 2.0F, Height / 2.0F), 1 / _zoom)
    End Sub

    ''' <summary>畫面上看得到的背景最大邊長（像素），用來決定預覽解析度。</summary>
    Public ReadOnly Property DisplayedLongSide As Integer
        Get
            Dim s = _workflow?.Scene
            If s?.Background Is Nothing Then Return 1024
            Return CInt(Math.Max(s.Width, s.Height) * Math.Min(1, _zoom) * DeviceDpi / 96.0)
        End Get
    End Property

    Public ReadOnly Property Zoom As Double
        Get
            Return _zoom
        End Get
    End Property

    Public Function ScreenToBg(p As PointF) As PointD
        Return New PointD((p.X - _offset.X) / _zoom, (p.Y - _offset.Y) / _zoom)
    End Function

    Public Function BgToScreen(p As PointD) As PointF
        Return New PointF(CSng(p.X * _zoom + _offset.X), CSng(p.Y * _zoom + _offset.Y))
    End Function

    Private Sub ZoomAt(screen As PointF, factor As Double)
        Dim bg = ScreenToBg(screen)
        _zoom = Math.Clamp(_zoom * factor, 0.02, 32)
        _offset = New PointF(CSng(screen.X - bg.X * _zoom), CSng(screen.Y - bg.Y * _zoom))
        Invalidate()
        RaiseEvent RenderRequested(False)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If _fitPending Then FitToWindow()
    End Sub

    ' ---------------- 繪製 ----------------

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        Dim s = _workflow?.Scene
        If s?.Background Is Nothing OrElse _preview Is Nothing Then
            Using f As New Font(Font.FontFamily, 12)
                TextRenderer.DrawText(g, "1. 開啟背景照片　2. 開啟人物圖片　3. 自動去背 → 自動定位 → 自動融合", f, ClientRectangle, Color.Gainsboro,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
            End Using
            Return
        End If
        Dim dest = New RectangleF(_offset.X, _offset.Y, CSng(s.Width * _zoom), CSng(s.Height * _zoom))
        g.InterpolationMode = If(_zoom * s.Width / _preview.Width >= 2, InterpolationMode.NearestNeighbor, InterpolationMode.HighQualityBilinear)
        g.PixelOffsetMode = PixelOffsetMode.Half
        g.DrawImage(_preview, dest)
        If _maskOverlay IsNot Nothing Then g.DrawImage(_maskOverlay, dest)
        g.PixelOffsetMode = PixelOffsetMode.Default
        g.SmoothingMode = SmoothingMode.AntiAlias
        If ShowOverlays Then DrawOverlays(g, s)
        DrawToolFeedback(g)
    End Sub

    Private Sub DrawOverlays(g As Graphics, s As CompositeScene)
        ' 參考線
        Using pen As New Pen(Color.FromArgb(220, 0, 200, 255), 1.5F) With {.DashStyle = DashStyle.Dash}
            For Each gl In s.Guides
                g.DrawLine(pen, BgToScreen(gl.P1), BgToScreen(gl.P2))
            Next
        End Using
        ' 目標錨點與站立面
        If s.Target.HasAnchor Then
            Dim p = BgToScreen(New PointD(s.Target.AnchorX, s.Target.AnchorY))
            Using pen As New Pen(Color.Lime, 2)
                g.DrawEllipse(pen, p.X - 7, p.Y - 7, 14, 14)
                g.DrawLine(pen, p.X - 11, p.Y, p.X + 11, p.Y)
                g.DrawLine(pen, p.X, p.Y - 11, p.X, p.Y + 11)
            End Using
        End If
        If s.Target.SurfaceWidth > 0 Then
            Using pen As New Pen(Color.Lime, 3)
                g.DrawLine(pen, BgToScreen(New PointD(s.Target.SurfaceLeftX, s.Target.SurfaceY)), BgToScreen(New PointD(s.Target.SurfaceRightX, s.Target.SurfaceY)))
            End Using
        End If
        ' 推薦站立點（尚未指定目標時）
        Dim an = _workflow.SceneAnalysis
        If an IsNot Nothing AndAlso Not s.Target.HasAnchor AndAlso an.Suggestions.Count > 0 Then
            Dim sug = an.Suggestions(0)
            Using pen As New Pen(Color.FromArgb(200, 255, 220, 0), 2) With {.DashStyle = DashStyle.Dot}
                g.DrawLine(pen, BgToScreen(New PointD(sug.SurfaceLeftX, sug.SurfaceY)), BgToScreen(New PointD(sug.SurfaceRightX, sug.SurfaceY)))
            End Using
        End If
        ' 人物外框與錨點
        Dim selected = _workflow.SelectedLayer
        For Each l In s.Layers
            If Not l.Visible OrElse l.Alpha Is Nothing Then Continue For
            Dim quad = LayerQuad(l)
            If quad Is Nothing Then Continue For
            Dim isSel = l Is selected
            Using pen As New Pen(If(isSel, Color.FromArgb(230, 255, 255, 255), Color.FromArgb(120, 200, 200, 200)), If(isSel, 1.5F, 1.0F)) With {.DashStyle = If(isSel, DashStyle.Solid, DashStyle.Dash)}
                g.DrawPolygon(pen, quad)
            End Using
            If isSel Then
                Dim a = BgToScreen(New PointD(l.Transform.X, l.Transform.Y))
                Using b As New SolidBrush(Color.FromArgb(230, 255, 80, 200))
                    g.FillEllipse(b, a.X - 5, a.Y - 5, 10, 10)
                End Using
                g.DrawEllipse(Pens.White, a.X - 5, a.Y - 5, 10, 10)
                If l.Locked Then TextRenderer.DrawText(g, "🔒", Font, Point.Round(quad(0)), Color.White)
            End If
        Next
    End Sub

    ''' <summary>人物有效外框變換到畫面上的四邊形。</summary>
    Private Function LayerQuad(l As CompositeLayer) As PointF()
        Try
            Dim fig = _workflow.Services.Renderer.GetFigureAnalysis(l)
            Dim m = _workflow.Services.Transform.BuildMatrix(l)
            Dim b = fig.Bounds
            Return {BgToScreen(m.Transform(New PointD(b.X, b.Y))), BgToScreen(m.Transform(New PointD(b.Right, b.Y))),
                    BgToScreen(m.Transform(New PointD(b.Right, b.Bottom))), BgToScreen(m.Transform(New PointD(b.X, b.Bottom)))}
        Catch ex As InvalidOperationException
            Return Nothing
        End Try
    End Function

    Private Sub DrawToolFeedback(g As Graphics)
        If _dragging AndAlso (Tool = CanvasTool.SetSurface OrElse Tool = CanvasTool.DrawGuide) Then
            Using pen As New Pen(If(Tool = CanvasTool.SetSurface, Color.Lime, Color.Cyan), 2)
                g.DrawLine(pen, BgToScreen(_lineStart), BgToScreen(_lineEnd))
            End Using
        End If
        If _dragging AndAlso Tool = CanvasTool.PaintMask AndAlso _stroke IsNot Nothing AndAlso _stroke.Points.Count > 1 Then
            Using pen As New Pen(If(_stroke.Mode = BrushMode.Add, Color.FromArgb(150, 255, 60, 60), Color.FromArgb(150, 60, 160, 255)), CSng(_stroke.Radius * 2 * _zoom)) With {
                .StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                g.DrawLines(pen, _stroke.Points.Select(Function(p) BgToScreen(p)).ToArray())
            End Using
        End If
        If Tool = CanvasTool.PaintMask AndAlso ClientRectangle.Contains(PointToClient(MousePosition)) Then
            Dim c = PointToClient(MousePosition)
            Dim r = CSng(BrushRadiusScreen)
            g.DrawEllipse(Pens.White, c.X - r, c.Y - r, 2 * r, 2 * r)
            g.DrawEllipse(Pens.Black, c.X - r - 1, c.Y - r - 1, 2 * r + 2, 2 * r + 2)
        End If
    End Sub

    ' ---------------- 滑鼠 ----------------

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        If _workflow?.Scene.Background Is Nothing Then Return
        _dragStart = e.Location
        _dragButton = e.Button
        Dim bg = ScreenToBg(e.Location)
        If e.Button = MouseButtons.Middle OrElse _spaceDown OrElse Tool = CanvasTool.Pan Then
            _dragging = True
            _panStart = _offset
            Tool = If(Tool = CanvasTool.Pan, CanvasTool.Pan, Tool)
            _dragButton = MouseButtons.Middle
            Return
        End If
        Select Case Tool
            Case CanvasTool.Move
                If e.Button <> MouseButtons.Left Then Return
                Dim hit = HitLayer(e.Location)
                If hit IsNot Nothing AndAlso hit.Id <> _workflow.SelectedLayer?.Id Then
                    _workflow.SelectedLayerId = hit.Id
                    RaiseEvent LayerSelected(hit.Id)
                End If
                Dim sel = _workflow.SelectedLayer
                If sel Is Nothing OrElse sel.Locked Then Return
                _dragging = True
                _layerStart = New PointD(sel.Transform.X, sel.Transform.Y)
            Case CanvasTool.SetTarget
                If e.Button = MouseButtons.Left Then RaiseEvent TargetPicked(bg)
            Case CanvasTool.SetSurface, CanvasTool.DrawGuide
                If e.Button <> MouseButtons.Left Then Return
                _dragging = True
                _lineStart = bg : _lineEnd = bg
            Case CanvasTool.PaintMask
                _dragging = True
                Dim remove = e.Button = MouseButtons.Right OrElse (ModifierKeys And Keys.Alt) = Keys.Alt
                _stroke = New BrushStroke With {.Mode = If(remove, BrushMode.Remove, BrushMode.Add), .Radius = Math.Max(0.5, BrushRadiusScreen / _zoom), .Softness = BrushSoftness}
                _stroke.Points.Add(bg)
        End Select
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _workflow?.Scene.Background Is Nothing Then Return
        _mouseBg = ScreenToBg(e.Location)
        RaiseEvent CursorMoved(_mouseBg)
        If Not _dragging Then
            If Tool = CanvasTool.PaintMask Then Invalidate()
            Cursor = If(Tool = CanvasTool.Move AndAlso HitLayer(e.Location) IsNot Nothing, Cursors.SizeAll, If(Tool = CanvasTool.Move, Cursors.Default, Cursors.Cross))
            Return
        End If
        If _dragButton = MouseButtons.Middle Then
            _offset = New PointF(_panStart.X + e.X - _dragStart.X, _panStart.Y + e.Y - _dragStart.Y)
            Invalidate()
            Return
        End If
        Select Case Tool
            Case CanvasTool.Move
                Dim sel = _workflow.SelectedLayer
                If sel Is Nothing Then Return
                sel.Transform.X = _layerStart.X + (e.X - _dragStart.X) / _zoom
                sel.Transform.Y = _layerStart.Y + (e.Y - _dragStart.Y) / _zoom
                RaiseEvent LayerEdited("移動人物", False)
                RaiseEvent RenderRequested(True)
            Case CanvasTool.SetSurface, CanvasTool.DrawGuide
                _lineEnd = _mouseBg
                If Tool = CanvasTool.SetSurface Then _lineEnd = New PointD(_mouseBg.X, _lineStart.Y)
            Case CanvasTool.PaintMask
                _stroke?.Points.Add(_mouseBg)
        End Select
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If Not _dragging Then Return
        _dragging = False
        If _dragButton = MouseButtons.Middle Then
            RaiseEvent RenderRequested(False)
            Return
        End If
        Select Case Tool
            Case CanvasTool.Move
                Dim sel = _workflow.SelectedLayer
                If sel IsNot Nothing AndAlso (Math.Abs(e.X - _dragStart.X) > 0 OrElse Math.Abs(e.Y - _dragStart.Y) > 0) Then
                    RaiseEvent LayerEdited("移動人物", True)
                End If
                RaiseEvent RenderRequested(False)
            Case CanvasTool.SetSurface
                If Math.Abs(_lineEnd.X - _lineStart.X) * _zoom > 3 Then
                    Dim l = If(_lineStart.X < _lineEnd.X, _lineStart, _lineEnd), r = If(_lineStart.X < _lineEnd.X, _lineEnd, _lineStart)
                    RaiseEvent SurfacePicked(l, r)
                End If
            Case CanvasTool.DrawGuide
                If _lineStart.DistanceTo(_lineEnd) * _zoom > 5 Then
                    RaiseEvent GuidePicked(New GuideLine With {.X1 = _lineStart.X, .Y1 = _lineStart.Y, .X2 = _lineEnd.X, .Y2 = _lineEnd.Y})
                End If
            Case CanvasTool.PaintMask
                If _stroke IsNot Nothing Then RaiseEvent StrokeCompleted(_stroke)
                _stroke = Nothing
        End Select
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        If _workflow?.Scene.Background Is Nothing Then Return
        Dim steps = e.Delta / 120.0
        Dim sel = _workflow.SelectedLayer
        If (ModifierKeys And Keys.Shift) = Keys.Shift AndAlso sel IsNot Nothing AndAlso Not sel.Locked Then
            sel.Transform.Scale = Math.Clamp(sel.Transform.Scale * Math.Pow(1.04, steps), 0.001, 100)
            RaiseEvent LayerEdited("縮放人物", True)
            RaiseEvent RenderRequested(False)
        ElseIf (ModifierKeys And Keys.Control) = Keys.Control AndAlso sel IsNot Nothing AndAlso Not sel.Locked Then
            sel.Transform.Rotation = Math.Round(sel.Transform.Rotation + steps, 2)
            RaiseEvent LayerEdited("旋轉人物", True)
            RaiseEvent RenderRequested(False)
        Else
            ZoomAt(e.Location, Math.Pow(1.15, steps))
        End If
    End Sub

    Private Function HitLayer(p As Point) As CompositeLayer
        If _workflow Is Nothing Then Return Nothing
        For i = _workflow.Scene.Layers.Count - 1 To 0 Step -1
            Dim l = _workflow.Scene.Layers(i)
            If Not l.Visible Then Continue For
            Dim q = LayerQuad(l)
            If q Is Nothing Then Continue For
            Using path As New GraphicsPath()
                path.AddPolygon(q)
                If path.IsVisible(p) Then Return l
            End Using
        Next
        Return Nothing
    End Function

    ' ---------------- 鍵盤 ----------------

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Select Case keyData And Keys.KeyCode
            Case Keys.Left, Keys.Right, Keys.Up, Keys.Down : Return True
        End Select
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.KeyCode = Keys.Space Then _spaceDown = True : Return
        Dim sel = _workflow?.SelectedLayer
        If sel Is Nothing OrElse sel.Locked Then Return
        Dim stepPx = If(e.Shift, 10.0, 1.0)
        Dim label As String = Nothing
        Select Case e.KeyCode
            Case Keys.Left : sel.Transform.X -= stepPx : label = "微調位置"
            Case Keys.Right : sel.Transform.X += stepPx : label = "微調位置"
            Case Keys.Up : sel.Transform.Y -= stepPx : label = "微調位置"
            Case Keys.Down : sel.Transform.Y += stepPx : label = "微調位置"
            Case Keys.Oemplus, Keys.Add : sel.Transform.Scale *= If(e.Shift, 1.1, 1.01) : label = "微調縮放"
            Case Keys.OemMinus, Keys.Subtract : sel.Transform.Scale /= If(e.Shift, 1.1, 1.01) : label = "微調縮放"
            Case Keys.OemOpenBrackets : sel.Transform.Rotation -= If(e.Shift, 5, 0.5) : label = "微調旋轉"
            Case Keys.OemCloseBrackets : sel.Transform.Rotation += If(e.Shift, 5, 0.5) : label = "微調旋轉"
        End Select
        If label IsNot Nothing Then
            e.Handled = True
            RaiseEvent LayerEdited(label, True)
            RaiseEvent RenderRequested(False)
        End If
    End Sub

    Protected Overrides Sub OnKeyUp(e As KeyEventArgs)
        MyBase.OnKeyUp(e)
        If e.KeyCode = Keys.Space Then _spaceDown = False
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _preview?.Dispose()
            _maskOverlay?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
