Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 預覽畫布：符合視窗或指定倍率顯示影像，可滾輪縮放、拖曳平移、雙擊切換「符合視窗 / 100%」。
''' 倍率以「原圖像素」為單位：Image 可能是預覽縮圖或全尺寸圖，換圖時由 ImageScale 告知比例，畫面位置不會跳動。
''' 裁切模式下固定為符合視窗，可拖曳畫出裁切框、或在框內拖曳移動，並以虛線標出偵測到的臉。
''' Image 由表單管理生命週期，畫布不負責釋放。
''' </summary>
Partial Friend Class PreviewCanvas
    Inherits Control

    Public Const MaxZoom As Double = 4.0
    Private Const WheelStep As Double = 1.25

    Private _image As Image
    Private _imageScale As Double = 1
    Private _zoom As Double            ' 0 = 符合視窗；否則為「螢幕像素 / 原圖像素」
    Private _center As New PointF(0.5F, 0.5F)  ' 檢視中心，影像的 0..1 座標

    Private _cropMode As Boolean
    Private _crop As New CropRect()
    Private _aspectRatio As Double
    Private _faceMarks As IReadOnlyList(Of RectangleF) = Array.Empty(Of RectangleF)()

    Public Enum CanvasTool
        None
        ''' <summary>修補筆刷：左鍵塗抹，放開時送出 StrokeCompleted。</summary>
        Heal
        ''' <summary>局部調整筆刷：同修補筆刷，筆觸顯示為藍色。</summary>
        LocalBrush
        ''' <summary>漸層濾鏡：拖曳拉出起點（效果 100%）到終點（0%）。</summary>
        Gradient
        ''' <summary>文字與貼圖：點選、拖曳移動。</summary>
        Overlay
        ''' <summary>去背修正筆刷：保留（綠）或擦除（紅），見 MaskKeep。</summary>
        MaskBrush
        ''' <summary>繪圖：滑鼠左鍵交給 DrawHost 處理（形狀、筆刷、控制點），右鍵仍可平移。</summary>
        Draw
        ''' <summary>魔術棒：左鍵點一下送出 WandClicked（Alt 為補回）。</summary>
        Wand
    End Enum

    Private _maskKeep As Boolean = True
    Private _checkerboard As Boolean

    ''' <summary>去背筆刷是「保留」（綠）還是「擦除」（紅）。</summary>
    Public Property MaskKeep As Boolean
        Get
            Return _maskKeep
        End Get
        Set(value As Boolean)
            _maskKeep = value
            Invalidate()
        End Set
    End Property

    ''' <summary>影像後面畫棋盤格（透明背景時）。</summary>
    Public Property Checkerboard As Boolean
        Get
            Return _checkerboard
        End Get
        Set(value As Boolean)
            If _checkerboard = value Then Return
            _checkerboard = value
            Invalidate()
        End Set
    End Property

    Private _gradientLine As (Start As PointF, [End] As PointF)?
    Private _overlayFrames As IReadOnlyList(Of OverlayFrameInfo) = Array.Empty(Of OverlayFrameInfo)()
    Private _selectedOverlay As Integer = -1
    Private _dragOverlay As Integer = -1
    Private _dragStartNorm As PointF
    Private _dragStartDistance As Double

    Private Const HandleSize As Single = 9
    Private Const RotateHandleGap As Single = 28

    ''' <summary>拉完一條漸層：起點與終點為目前顯示影像的 0..1 座標。</summary>
    Public Event GradientDefined(start As PointF, [end] As PointF)
    ''' <summary>按下文字/貼圖（-1 表示點在空白處）。</summary>
    Public Event OverlayPressed(index As Integer)
    ''' <summary>拖曳中：位移量為影像的 0..1 比例（從按下時算起）。</summary>
    Public Event OverlayDragged(index As Integer, dx As Single, dy As Single)
    ''' <summary>拖曳角落控制點：相對於按下時的放大倍數。</summary>
    Public Event OverlayScaled(index As Integer, factor As Single)
    ''' <summary>拖曳旋轉把手：絕對角度（度，順時針）。</summary>
    Public Event OverlayRotated(index As Integer, degrees As Single)
    ''' <summary>Ctrl+滾輪（縮放）或 Shift+滾輪（旋轉）。</summary>
    Public Event OverlayWheel(index As Integer, up As Boolean, rotate As Boolean)

    Private Enum OverlayHit
        None
        Body
        Corner
        Rotate
    End Enum

    ''' <summary>漸層工具顯示的線（0..1 座標）；Nothing 不顯示。</summary>
    Public Property GradientLine As (Start As PointF, [End] As PointF)?
        Get
            Return _gradientLine
        End Get
        Set(value As (Start As PointF, [End] As PointF)?)
            _gradientLine = value
            Invalidate()
        End Set
    End Property

    ''' <summary>各文字/貼圖的選取框（見 Creative.OverlayFrame），供點選與控制點。</summary>
    Public Property OverlayFrames As IReadOnlyList(Of OverlayFrameInfo)
        Get
            Return _overlayFrames
        End Get
        Set(value As IReadOnlyList(Of OverlayFrameInfo))
            _overlayFrames = If(value, Array.Empty(Of OverlayFrameInfo)())
            Invalidate()
        End Set
    End Property

    '---------------------------------------------------------------------
    ' 文字/貼圖選取框的幾何（螢幕座標）
    '---------------------------------------------------------------------

    Private Function PivotOnScreen(f As OverlayFrameInfo) As PointF
        Dim b = ImageBounds()
        Return New PointF(b.X + f.PivotX * b.Width, b.Y + f.PivotY * b.Height)
    End Function

    ''' <summary>選取框在螢幕上的未旋轉範圍（相對旋轉中心的像素）。</summary>
    Private Function LocalBox(f As OverlayFrameInfo) As RectangleF
        Dim b = ImageBounds()
        Return RectangleF.FromLTRB(f.Left * b.Width, f.Top * b.Height, f.Right * b.Width, f.Bottom * b.Height)
    End Function

    Private Shared Function Rotate(p As PointF, degrees As Single) As PointF
        Dim a = degrees * Math.PI / 180
        Return New PointF(CSng(p.X * Math.Cos(a) - p.Y * Math.Sin(a)), CSng(p.X * Math.Sin(a) + p.Y * Math.Cos(a)))
    End Function

    Private Function LocalToScreen(f As OverlayFrameInfo, local As PointF) As PointF
        Dim c = PivotOnScreen(f)
        Dim r = Rotate(local, f.Rotation)
        Return New PointF(c.X + r.X, c.Y + r.Y)
    End Function

    Private Function ScreenToLocal(f As OverlayFrameInfo, p As Point) As PointF
        Dim c = PivotOnScreen(f)
        Return Rotate(New PointF(p.X - c.X, p.Y - c.Y), -f.Rotation)
    End Function

    Private Function Corners(f As OverlayFrameInfo) As PointF()
        Dim r = LocalBox(f)
        Return {New PointF(r.Left, r.Top), New PointF(r.Right, r.Top), New PointF(r.Right, r.Bottom), New PointF(r.Left, r.Bottom)}.
            Select(Function(p) LocalToScreen(f, p)).ToArray()
    End Function

    Private Function RotateHandle(f As OverlayFrameInfo) As PointF
        Dim r = LocalBox(f)
        Return LocalToScreen(f, New PointF((r.Left + r.Right) / 2, r.Top - RotateHandleGap))
    End Function

    ''' <summary>點到哪個文字/貼圖的哪個部位：選取中的控制點優先，再來由上而下找本體。</summary>
    Private Function HitOverlay(p As Point, ByRef index As Integer) As OverlayHit
        If _selectedOverlay >= 0 AndAlso _selectedOverlay < _overlayFrames.Count Then
            Dim f = _overlayFrames(_selectedOverlay)
            index = _selectedOverlay
            Dim h = RotateHandle(f)
            If Math.Abs(p.X - h.X) <= HandleSize AndAlso Math.Abs(p.Y - h.Y) <= HandleSize Then Return OverlayHit.Rotate
            For Each c In Corners(f)
                If Math.Abs(p.X - c.X) <= HandleSize AndAlso Math.Abs(p.Y - c.Y) <= HandleSize Then Return OverlayHit.Corner
            Next
        End If
        For i = _overlayFrames.Count - 1 To 0 Step -1 ' 後畫的在上面
            Dim local = ScreenToLocal(_overlayFrames(i), p)
            If LocalBox(_overlayFrames(i)).Contains(local) Then
                index = i
                Return OverlayHit.Body
            End If
        Next
        index = -1
        Return OverlayHit.None
    End Function

    Public Property SelectedOverlay As Integer
        Get
            Return _selectedOverlay
        End Get
        Set(value As Integer)
            _selectedOverlay = value
            Invalidate()
        End Set
    End Property

    Private ReadOnly Property IsPaintTool As Boolean
        Get
            Return _tool = CanvasTool.Heal OrElse _tool = CanvasTool.LocalBrush OrElse _tool = CanvasTool.MaskBrush
        End Get
    End Property

    Private _tool As CanvasTool = CanvasTool.None
    Private _brushRadius As Single = 18
    Private _stroke As List(Of PointF)      ' 塗抹中的點（影像 0..1 座標）
    Private _mousePos As Point?

    ''' <summary>一筆塗抹完成：點為目前顯示影像的 0..1 座標，半徑為螢幕像素。</summary>
    Public Event StrokeCompleted(points As List(Of PointF), screenRadius As Single)

    Private Enum DragMode
        None
        NewRect
        ResizeCrop
        Move
        Pan
        Paint
        Gradient
        MoveOverlay
        ScaleOverlay
        RotateOverlay
        Draw
    End Enum

    Private _drag As DragMode = DragMode.None
    Private _dragAnchor As PointF      ' 裁切：影像像素座標；平移：螢幕座標
    Private _dragStartCrop As CropRect
    Private _dragStartCenter As PointF

    ''' <summary>繪圖工具的滑鼠與繪製交給這個物件（frmEditor）。</summary>
    Friend Interface IDrawHost
        Sub DrawMouseDown(e As MouseEventArgs)
        Sub DrawMouseMove(e As MouseEventArgs)
        Sub DrawMouseUp(e As MouseEventArgs)
        Sub DrawDoubleClick(e As MouseEventArgs)
        Sub DrawPaint(g As Graphics)
    End Interface

    Private _drawHost As IDrawHost

    Friend Property DrawHost As IDrawHost
        Get
            Return _drawHost
        End Get
        Set(value As IDrawHost)
            _drawHost = value
        End Set
    End Property

    ''' <summary>魔術棒點一下：位置為目前顯示影像的 0..1 座標，alt 表示按住 Alt（補回）。</summary>
    Public Event WandClicked(point As PointF, alt As Boolean)

    Private _wandRestore As Boolean

    ''' <summary>魔術棒目前是「補回」模式（游標顯示 ＋）；按住 Alt 會暫時反過來。</summary>
    Public Property WandRestore As Boolean
        Get
            Return _wandRestore
        End Get
        Set(value As Boolean)
            _wandRestore = value
            If _tool = CanvasTool.Wand Then Cursor = WandCursorFor(ModifierKeys.HasFlag(Keys.Alt))
        End Set
    End Property

    Private Function WandCursorFor(alt As Boolean) As Cursor
        Return If(_wandRestore Xor alt, WandCursor.Restore, WandCursor.Remove)
    End Function

    Public Event CropChanged As EventHandler
    Public Event ZoomChanged As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        BackColor = Color.FromArgb(38, 38, 40)
        ' 畫布不打字：停用輸入法，中文（注音）模式下按 E、B 等快捷鍵才不會被拿去組字
        ImeMode = ImeMode.Disable
        InitScrollBars()
        ForeColor = Color.FromArgb(170, 170, 175)
    End Sub

    Public Property Image As Image
        Get
            Return _image
        End Get
        Set(value As Image)
            _image = value
            UpdateScrollBars()
            Invalidate()
        End Set
    End Property

    ''' <summary>
    ''' 吸色：畫布上這一點看到的顏色（照片＋所有可見圖層的合成結果），取 3×3 平均避開單一雜點；
    ''' 不在影像上或沒有影像時為 Nothing。半透明處（去背）以 alpha 加權，忽略全透明。
    ''' </summary>
    Public Function SampleColor(p As Point) As Color?
        Dim bmp = TryCast(_image, Bitmap)
        If bmp Is Nothing Then Return Nothing
        Dim b = ImageBounds()
        If b.Width <= 0 OrElse Not b.Contains(p) Then Return Nothing
        Dim x = CInt(Math.Floor((p.X - b.X) / b.Width * bmp.Width)), y = CInt(Math.Floor((p.Y - b.Y) / b.Height * bmp.Height))
        Dim r = 0.0, g = 0.0, bl = 0.0, w = 0.0
        For yy = Math.Max(0, y - 1) To Math.Min(bmp.Height - 1, y + 1)
            For xx = Math.Max(0, x - 1) To Math.Min(bmp.Width - 1, x + 1)
                Dim c = bmp.GetPixel(xx, yy)
                Dim a = c.A / 255.0
                r += c.R * a : g += c.G * a : bl += c.B * a : w += a
            Next
        Next
        If w <= 0 Then Return Nothing
        Return Color.FromArgb(CInt(Math.Round(r / w)), CInt(Math.Round(g / w)), CInt(Math.Round(bl / w)))
    End Function

    ''' <summary>Image 一個像素等於幾個原圖像素（預覽縮圖 &gt; 1，全尺寸 = 1）。</summary>
    Public Property ImageScale As Double
        Get
            Return _imageScale
        End Get
        Set(value As Double)
            _imageScale = Math.Max(0.0001, value)
            Invalidate()
        End Set
    End Property

    Public Property Tool As CanvasTool
        Get
            Return _tool
        End Get
        Set(value As CanvasTool)
            _tool = value
            _stroke = Nothing
            _drag = DragMode.None
            Cursor = If(value = CanvasTool.None, If(IsFit, Cursors.Default, Cursors.Hand),
                        If(value = CanvasTool.Wand, WandCursorFor(ModifierKeys.HasFlag(Keys.Alt)),
                           If(value = CanvasTool.Overlay OrElse value = CanvasTool.Draw, Cursors.Default, Cursors.Cross)))
            Invalidate()
        End Set
    End Property

    ''' <summary>筆刷半徑（螢幕像素）。</summary>
    Public Property BrushRadius As Single
        Get
            Return _brushRadius
        End Get
        Set(value As Single)
            _brushRadius = Math.Max(2, value)
            Invalidate()
        End Set
    End Property

    '---------------------------------------------------------------------
    ' 縮放
    '---------------------------------------------------------------------

    Public ReadOnly Property IsFit As Boolean
        Get
            Return _zoom = 0
        End Get
    End Property

    ''' <summary>檢視狀態（倍率、中心）：多文件切換時各自記住。倍率 0 表示符合視窗。</summary>
    Public Property ViewState As (Zoom As Double, Center As PointF)
        Get
            Return (_zoom, _center)
        End Get
        Set(value As (Zoom As Double, Center As PointF))
            _zoom = value.Zoom
            _center = value.Center
            ClampCenter()
            UpdateScrollBars()
            Invalidate()
            RaiseEvent ZoomChanged(Me, EventArgs.Empty)
        End Set
    End Property

    ''' <summary>目前實際倍率（螢幕像素 / 原圖像素）。</summary>
    Public Function EffectiveZoom() As Double
        If _image Is Nothing Then Return 0
        Return If(_zoom = 0, FitZoom(), _zoom)
    End Function

    Private Function FitZoom() As Double
        If _image Is Nothing Then Return 1
        Dim margin = 12
        Dim availW = Math.Max(1, ViewSize.Width - margin * 2)
        Dim availH = Math.Max(1, ViewSize.Height - margin * 2)
        Return Math.Min(availW / (_image.Width * _imageScale), availH / (_image.Height * _imageScale))
    End Function

    Public Sub ZoomToFit()
        If _zoom = 0 Then Return
        _zoom = 0
        _center = New PointF(0.5F, 0.5F)
        UpdateScrollBars()
        Invalidate()
        RaiseEvent ZoomChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>以畫面上某點為中心縮放（anchor 為 Nothing 時以畫面中心）。</summary>
    Public Sub SetZoom(zoom As Double, Optional anchor As Point? = Nothing)
        If _image Is Nothing OrElse _cropMode Then Return
        Dim fit = FitZoom()
        If zoom <= fit * 1.001 Then
            ZoomToFit()
            Return
        End If
        zoom = Math.Min(MaxZoom, zoom)
        Dim a = If(anchor, New Point(ViewSize.Width \ 2, ViewSize.Height \ 2))
        Dim before = ScreenToNormalized(a)
        _zoom = zoom
        ' 讓 anchor 下的影像位置縮放後仍在 anchor 下。
        Dim dispW = _image.Width * _imageScale * _zoom, dispH = _image.Height * _imageScale * _zoom
        _center = New PointF(CSng(before.X - (a.X - ViewSize.Width / 2.0) / dispW),
                             CSng(before.Y - (a.Y - ViewSize.Height / 2.0) / dispH))
        ClampCenter()
        Invalidate()
        RaiseEvent ZoomChanged(Me, EventArgs.Empty)
    End Sub

    Public Sub ZoomIn()
        SetZoom(EffectiveZoom() * WheelStep)
    End Sub

    Public Sub ZoomOut()
        SetZoom(EffectiveZoom() / WheelStep)
    End Sub

    Private Sub ClampCenter()
        If _image Is Nothing OrElse _zoom = 0 Then Return
        Dim dispW = _image.Width * _imageScale * _zoom, dispH = _image.Height * _imageScale * _zoom
        Dim halfW = ViewSize.Width / 2.0 / dispW, halfH = ViewSize.Height / 2.0 / dispH
        _center = New PointF(CSng(If(halfW >= 0.5, 0.5, Math.Max(halfW, Math.Min(1 - halfW, _center.X)))),
                             CSng(If(halfH >= 0.5, 0.5, Math.Max(halfH, Math.Min(1 - halfH, _center.Y)))))
        UpdateScrollBars()
    End Sub

    ''' <summary>影像在畫布上的顯示位置（可能大於畫布）。</summary>
    Public Function ImageBounds() As RectangleF
        If _image Is Nothing Then Return RectangleF.Empty
        Dim z = EffectiveZoom()
        Dim w = CSng(_image.Width * _imageScale * z), h = CSng(_image.Height * _imageScale * z)
        If _zoom = 0 Then Return New RectangleF((ViewSize.Width - w) / 2, (ViewSize.Height - h) / 2, w, h)
        Return New RectangleF(ViewSize.Width / 2.0F - _center.X * w, ViewSize.Height / 2.0F - _center.Y * h, w, h)
    End Function

    ''' <summary>畫布上的點換成影像 0..1 座標；不在影像範圍內時回傳 Nothing（拖放貼圖用）。</summary>
    Public Function ClientToImage(p As Point) As PointF?
        If _image Is Nothing Then Return Nothing
        Dim n = ScreenToNormalized(p)
        If n.X < 0 OrElse n.Y < 0 OrElse n.X > 1 OrElse n.Y > 1 Then Return Nothing
        Return n
    End Function

    ''' <summary>畫布上的點換成影像 0..1 座標（不限制在影像內）。</summary>
    Public Function ClientToNormalized(p As Point) As PointF
        If _image Is Nothing Then Return PointF.Empty
        Return ScreenToNormalized(p)
    End Function

    Private Function ScreenToNormalized(p As Point) As PointF
        Dim b = ImageBounds()
        Return New PointF((p.X - b.X) / b.Width, (p.Y - b.Y) / b.Height)
    End Function

    '---------------------------------------------------------------------
    ' 裁切
    '---------------------------------------------------------------------

    Public Property CropMode As Boolean
        Get
            Return _cropMode
        End Get
        Set(value As Boolean)
            _cropMode = value
            _drag = DragMode.None
            If value Then ZoomToFit()
            UpdateScrollBars()
            Cursor = If(value, Cursors.Cross, Cursors.Default)
            Invalidate()
        End Set
    End Property

    ''' <summary>相對於目前顯示影像（未裁切）的 0..1 座標。</summary>
    Public Property Crop As CropRect
        Get
            Return _crop.Clone()
        End Get
        Set(value As CropRect)
            _crop = If(value, New CropRect()).Normalized()
            Invalidate()
        End Set
    End Property

    ''' <summary>裁切框寬高比（像素），0 表示自由比例。</summary>
    Public Property AspectRatio As Double
        Get
            Return _aspectRatio
        End Get
        Set(value As Double)
            _aspectRatio = Math.Max(0, value)
        End Set
    End Property

    ''' <summary>裁切模式下以虛線標示的臉（目前顯示影像的 0..1 座標）。</summary>
    Public Property FaceMarks As IReadOnlyList(Of RectangleF)
        Get
            Return _faceMarks
        End Get
        Set(value As IReadOnlyList(Of RectangleF))
            _faceMarks = If(value, Array.Empty(Of RectangleF)())
            Invalidate()
        End Set
    End Property

    '---------------------------------------------------------------------
    ' 繪製
    '---------------------------------------------------------------------

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        If _image Is Nothing Then
            TextRenderer.DrawText(g, "把照片拖曳到這裡，或從「檔案 → 載入／新增」", Font, ClientRectangle, ForeColor,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Return
        End If

        Dim b = ImageBounds()
        ' 只畫看得到的部分：放大檢視時不必每次處理整張大圖。
        Dim visible = RectangleF.Intersect(b, ClientRectangle)
        If _checkerboard AndAlso visible.Width > 0 Then
            ' 透明處顯示棋盤格。
            Using light As New SolidBrush(Color.FromArgb(235, 235, 235)), dark As New SolidBrush(Color.FromArgb(200, 200, 200))
                g.FillRectangle(light, visible)
                Const cell = 12
                For y = CInt(visible.Top) To CInt(visible.Bottom) Step cell
                    For x = CInt(visible.Left) To CInt(visible.Right) Step cell
                        If ((x - CInt(b.X)) \ cell + (y - CInt(b.Y)) \ cell) Mod 2 = 0 Then
                            g.FillRectangle(dark, RectangleF.Intersect(New RectangleF(x, y, cell, cell), visible))
                        End If
                    Next
                Next
            End Using
        End If
        If visible.Width > 0 AndAlso visible.Height > 0 Then
            Dim sx = _image.Width / b.Width, sy = _image.Height / b.Height
            Dim src As New RectangleF((visible.X - b.X) * sx, (visible.Y - b.Y) * sy, visible.Width * sx, visible.Height * sy)
            g.InterpolationMode = If(EffectiveZoom() / (1 / _imageScale) > 2, InterpolationMode.NearestNeighbor, InterpolationMode.HighQualityBilinear)
            g.PixelOffsetMode = PixelOffsetMode.Half
            g.DrawImage(_image, visible, src, GraphicsUnit.Pixel)
        End If

        DrawTrace(g, b, visible)
        If _cropMode Then DrawCropOverlay(g, b)
        If IsPaintTool Then DrawBrush(g, b)
        If _tool = CanvasTool.Gradient Then DrawGradient(g, b)
        If _tool = CanvasTool.Overlay Then DrawOverlaySelection(g, b)
        If _tool = CanvasTool.Draw Then _drawHost?.DrawPaint(g)
        AfterPaint?.Invoke(g)
    End Sub

    '---------------------------------------------------------------------
    ' 描圖：參考圖半透明疊在照片上（只顯示，不存進文件也不匯出）
    '---------------------------------------------------------------------

    Private _traceSource As Bitmap
    Private _traceScaled As Bitmap
    Private _traceOpacity As Single = 0.35F

    ''' <summary>描圖用的參考圖（Nothing = 不描圖）；畫布另存一份縮小的複本，重畫才快。</summary>
    Friend Sub SetTrace(image As Bitmap, opacity As Single)
        If image IsNot _traceSource Then
            _traceScaled?.Dispose()
            _traceScaled = Nothing
            _traceSource = image
            If image IsNot Nothing Then
                Dim s = Math.Min(1.0, 2048.0 / Math.Max(image.Width, image.Height))
                _traceScaled = New Bitmap(image, Math.Max(1, CInt(image.Width * s)), Math.Max(1, CInt(image.Height * s)))
            End If
        End If
        _traceOpacity = opacity
        Invalidate()
    End Sub

    ''' <summary>參考圖等比例放進照片範圍、置中。</summary>
    Private Sub DrawTrace(g As Graphics, b As RectangleF, visible As RectangleF)
        If _traceScaled Is Nothing OrElse visible.Width <= 0 OrElse visible.Height <= 0 Then Return
        Dim s = Math.Min(b.Width / _traceScaled.Width, b.Height / _traceScaled.Height)
        Dim w = _traceScaled.Width * s, h = _traceScaled.Height * s
        Dim dest As New RectangleF(b.X + (b.Width - w) / 2, b.Y + (b.Height - h) / 2, w, h)
        Dim state = g.Save()
        g.SetClip(visible)
        g.InterpolationMode = InterpolationMode.Bilinear
        Using attr As New Imaging.ImageAttributes()
            attr.SetColorMatrix(New Imaging.ColorMatrix With {.Matrix33 = _traceOpacity})
            g.DrawImage(_traceScaled, Rectangle.Round(dest), 0, 0, _traceScaled.Width, _traceScaled.Height, GraphicsUnit.Pixel, attr)
        End Using
        g.Restore(state)
    End Sub

    ''' <summary>不論目前工具，最後再畫的東西（選取區的螞蟻線）。</summary>
    Friend Property AfterPaint As Action(Of Graphics)

    Private Sub DrawGradient(g As Graphics, b As RectangleF)
        If Not _gradientLine.HasValue Then Return
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim s = New PointF(b.X + _gradientLine.Value.Start.X * b.Width, b.Y + _gradientLine.Value.Start.Y * b.Height)
        Dim e = New PointF(b.X + _gradientLine.Value.End.X * b.Width, b.Y + _gradientLine.Value.End.Y * b.Height)
        Dim dx = e.X - s.X, dy = e.Y - s.Y
        Dim len = CSng(Math.Max(1, Math.Sqrt(dx * dx + dy * dy)))
        ' 起點、終點各畫一條垂直於拉線方向的長線，表示 100% 與 0% 的位置。
        Dim nx = -dy / len * 2000, ny = dx / len * 2000
        Using shadow As New Pen(Color.FromArgb(150, 0, 0, 0), 3), solid As New Pen(Color.White, 1.5F),
              dashed As New Pen(Color.White, 1.5F) With {.DashStyle = DashStyle.Dash}
            For Each p In {(s, solid), (e, dashed)}
                g.DrawLine(shadow, p.Item1.X - nx, p.Item1.Y - ny, p.Item1.X + nx, p.Item1.Y + ny)
                g.DrawLine(p.Item2, p.Item1.X - nx, p.Item1.Y - ny, p.Item1.X + nx, p.Item1.Y + ny)
            Next
            g.DrawLine(shadow, s, e)
            g.DrawLine(solid, s, e)
        End Using
        g.FillEllipse(Brushes.White, s.X - 6, s.Y - 6, 12, 12)
        g.DrawEllipse(Pens.Black, s.X - 6, s.Y - 6, 12, 12)
        g.FillEllipse(Brushes.Black, e.X - 5, e.Y - 5, 10, 10)
        g.DrawEllipse(Pens.White, e.X - 5, e.Y - 5, 10, 10)
    End Sub

    Private Sub DrawOverlaySelection(g As Graphics, b As RectangleF)
        If _selectedOverlay < 0 OrElse _selectedOverlay >= _overlayFrames.Count Then Return
        Dim f = _overlayFrames(_selectedOverlay)
        Dim pts = Corners(f)
        Dim r = LocalBox(f)
        Dim topMid = LocalToScreen(f, New PointF((r.Left + r.Right) / 2, r.Top))
        Dim handle = RotateHandle(f)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using outer As New Pen(Color.FromArgb(160, 0, 0, 0), 3), inner As New Pen(Color.FromArgb(255, 200, 60), 1.5F) With {.DashStyle = DashStyle.Dash}
            g.DrawPolygon(outer, pts)
            g.DrawPolygon(inner, pts)
            g.DrawLine(outer, topMid, handle)
            g.DrawLine(inner, topMid, handle)
        End Using
        ' 角落：縮放；上方圓點：旋轉。
        For Each c In pts
            g.FillRectangle(Brushes.White, c.X - 5, c.Y - 5, 10, 10)
            g.DrawRectangle(Pens.Black, c.X - 5, c.Y - 5, 10, 10)
        Next
        Using br As New SolidBrush(Color.FromArgb(255, 200, 60))
            g.FillEllipse(br, handle.X - 6, handle.Y - 6, 12, 12)
        End Using
        g.DrawEllipse(Pens.Black, handle.X - 6, handle.Y - 6, 12, 12)
    End Sub

    ''' <summary>塗抹中的軌跡（半透明紅色）與筆刷圓圈。</summary>
    Private Sub DrawBrush(g As Graphics, b As RectangleF)
        g.SmoothingMode = SmoothingMode.AntiAlias
        If _stroke IsNot Nothing AndAlso _stroke.Count > 0 Then
            Dim pts = _stroke.Select(Function(p) New PointF(b.X + p.X * b.Width, b.Y + p.Y * b.Height)).ToArray()
            Dim strokeColor = If(_tool = CanvasTool.Heal, Color.FromArgb(110, 255, 60, 60),
                                 If(_tool = CanvasTool.MaskBrush, If(_maskKeep, Color.FromArgb(120, 40, 200, 70), Color.FromArgb(120, 230, 40, 40)),
                                    Color.FromArgb(110, 60, 140, 255)))
            Using br As New SolidBrush(strokeColor)
                If pts.Length = 1 Then
                    g.FillEllipse(br, pts(0).X - _brushRadius, pts(0).Y - _brushRadius, _brushRadius * 2, _brushRadius * 2)
                Else
                    Using pen As New Pen(br, _brushRadius * 2) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                        g.DrawLines(pen, pts)
                    End Using
                End If
            End Using
        End If
        If _mousePos.HasValue Then
            Dim m = _mousePos.Value
            Using outer As New Pen(Color.FromArgb(200, 0, 0, 0), 3), inner As New Pen(Color.White, 1.2F)
                g.DrawEllipse(outer, m.X - _brushRadius, m.Y - _brushRadius, _brushRadius * 2, _brushRadius * 2)
                g.DrawEllipse(inner, m.X - _brushRadius, m.Y - _brushRadius, _brushRadius * 2, _brushRadius * 2)
            End Using
        End If
    End Sub

    Private Function ClampedNormalized(p As Point) As PointF
        Dim n = ScreenToNormalized(p)
        Return New PointF(Math.Max(0, Math.Min(1, n.X)), Math.Max(0, Math.Min(1, n.Y)))
    End Function

    Private Sub DrawCropOverlay(g As Graphics, b As RectangleF)
        Dim r = CropToScreen(b)
        Using shade As New SolidBrush(Color.FromArgb(150, 0, 0, 0))
            Using region As New Region(b)
                region.Exclude(r)
                g.FillRegion(shade, region)
            End Using
        End Using
        g.SmoothingMode = SmoothingMode.None
        DrawCropShape(g, r)
        DrawCropGuides(g, r)
        Using facePen As New Pen(Color.FromArgb(200, 255, 210, 80), 1.5F) With {.DashStyle = DashStyle.Dash}
            For Each f In _faceMarks
                g.DrawRectangle(facePen, b.X + f.X * b.Width, b.Y + f.Y * b.Height, f.Width * b.Width, f.Height * b.Height)
            Next
        End Using
        Using border As New Pen(Color.White, 2)
            g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height)
        End Using
        DrawCropHandles(g, r)
        DrawCropSize(g, r)
    End Sub

    Private Function CropToScreen(b As RectangleF) As RectangleF
        Return New RectangleF(CSng(b.X + _crop.X * b.Width), CSng(b.Y + _crop.Y * b.Height),
                              CSng(_crop.Width * b.Width), CSng(_crop.Height * b.Height))
    End Function

    ''' <summary>畫面座標轉成影像像素座標，並夾在影像範圍內。</summary>
    Private Function ScreenToImage(p As Point) As PointF
        Dim n = ScreenToNormalized(p)
        Dim x = Math.Max(0, Math.Min(_image.Width, n.X * _image.Width))
        Dim y = Math.Max(0, Math.Min(_image.Height, n.Y * _image.Height))
        Return New PointF(x, y)
    End Function

    '---------------------------------------------------------------------
    ' 滑鼠
    '---------------------------------------------------------------------

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        If _image Is Nothing OrElse _cropMode Then Return
        ' 文字/貼圖工具：Ctrl+滾輪縮放、Shift+滾輪旋轉選取中的物件。
        If _tool = CanvasTool.Overlay AndAlso _selectedOverlay >= 0 AndAlso
           (ModifierKeys = Keys.Control OrElse ModifierKeys = Keys.Shift) Then
            RaiseEvent OverlayWheel(_selectedOverlay, e.Delta > 0, ModifierKeys = Keys.Shift)
            Return
        End If
        SetZoom(EffectiveZoom() * If(e.Delta > 0, WheelStep, 1 / WheelStep), e.Location)
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
        ' 讓滾輪事件送到畫布，而不是右側面板。
        If _image IsNot Nothing AndAlso Not Focused AndAlso Form.ActiveForm Is FindForm() Then Focus()
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        If _tool = CanvasTool.Draw AndAlso e.Button = MouseButtons.Left AndAlso _image IsNot Nothing Then
            _drawHost?.DrawDoubleClick(e)
            Return
        End If
        If _image Is Nothing OrElse _cropMode OrElse _tool <> CanvasTool.None OrElse e.Button <> MouseButtons.Left Then Return
        If IsFit Then SetZoom(1.0, e.Location) Else ZoomToFit()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If _image Is Nothing Then Return
        Dim panButton = e.Button = MouseButtons.Right OrElse e.Button = MouseButtons.Middle OrElse
                        (e.Button = MouseButtons.Left AndAlso _tool = CanvasTool.None)
        If _cropMode Then
            If e.Button <> MouseButtons.Left Then Return
            _dragAnchor = ScreenToImage(e.Location)
            _dragStartCrop = _crop.Clone()
            _cropHandle = CropHandleAt(e.Location)
            _drag = If(_cropHandle >= 0, DragMode.ResizeCrop, If(CropToScreen(ImageBounds()).Contains(e.Location), DragMode.Move, DragMode.NewRect))
        ElseIf _tool = CanvasTool.Wand AndAlso e.Button = MouseButtons.Left Then
            Dim n = ScreenToNormalized(e.Location)
            If n.X >= 0 AndAlso n.Y >= 0 AndAlso n.X <= 1 AndAlso n.Y <= 1 Then RaiseEvent WandClicked(n, ModifierKeys.HasFlag(Keys.Alt))
            Return
        ElseIf _tool = CanvasTool.Draw AndAlso (e.Button = MouseButtons.Left OrElse e.Button = MouseButtons.Right) Then ' 繪圖：右鍵按住是暫時的橡皮擦（中鍵仍可拖曳畫面）
            _drag = DragMode.Draw
            _drawHost?.DrawMouseDown(e)
        ElseIf IsPaintTool AndAlso e.Button = MouseButtons.Left Then
            _drag = DragMode.Paint
            _stroke = New List(Of PointF) From {ClampedNormalized(e.Location)}
            Invalidate()
        ElseIf _tool = CanvasTool.Gradient AndAlso e.Button = MouseButtons.Left Then
            _drag = DragMode.Gradient
            _dragStartNorm = ClampedNormalized(e.Location)
            _gradientLine = (_dragStartNorm, _dragStartNorm)
            Invalidate()
        ElseIf _tool = CanvasTool.Overlay AndAlso e.Button = MouseButtons.Left Then
            Dim index As Integer
            Dim hit = HitOverlay(e.Location, index)
            _dragOverlay = index
            _selectedOverlay = index
            Invalidate()
            RaiseEvent OverlayPressed(index)
            If index < 0 Then Return
            Dim pivot = PivotOnScreen(_overlayFrames(index))
            _dragStartDistance = Math.Max(1, Math.Sqrt((e.X - pivot.X) ^ 2 + (e.Y - pivot.Y) ^ 2))
            _dragStartNorm = ScreenToNormalized(e.Location)
            _drag = If(hit = OverlayHit.Corner, DragMode.ScaleOverlay, If(hit = OverlayHit.Rotate, DragMode.RotateOverlay, DragMode.MoveOverlay))
        ElseIf panButton AndAlso Not IsFit Then
            _drag = DragMode.Pan
            _dragAnchor = e.Location
            _dragStartCenter = _center
            Cursor = Cursors.SizeAll
        Else
            Return
        End If
        Capture = True
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _image Is Nothing Then Return
        If _tool = CanvasTool.Wand Then
            Dim c = WandCursorFor(ModifierKeys.HasFlag(Keys.Alt))
            If Cursor IsNot c Then Cursor = c
        End If

        If IsPaintTool Then
            _mousePos = e.Location
            Invalidate()
        End If

        If _tool = CanvasTool.Draw AndAlso (_drag = DragMode.Draw OrElse _drag = DragMode.None) Then
            _drawHost?.DrawMouseMove(e)
            Return
        End If

        If _drag = DragMode.Gradient Then
            _gradientLine = (_dragStartNorm, ClampedNormalized(e.Location))
            Invalidate()
            Return
        End If

        If _drag = DragMode.MoveOverlay Then
            Dim n = ScreenToNormalized(e.Location)
            RaiseEvent OverlayDragged(_dragOverlay, n.X - _dragStartNorm.X, n.Y - _dragStartNorm.Y)
            Return
        End If

        If _drag = DragMode.ScaleOverlay OrElse _drag = DragMode.RotateOverlay Then
            If _dragOverlay >= _overlayFrames.Count Then Return
            Dim pivot = PivotOnScreen(_overlayFrames(_dragOverlay))
            Dim dx = e.X - pivot.X, dy = e.Y - pivot.Y
            If _drag = DragMode.ScaleOverlay Then
                ' 角落離中心的距離變化 = 放大倍數（等比例）。
                RaiseEvent OverlayScaled(_dragOverlay, CSng(Math.Sqrt(dx * dx + dy * dy) / _dragStartDistance))
            Else
                ' 把手在物件「上方」，所以滑鼠方向 + 90° 就是旋轉角度；Shift 每 15° 吸附。
                Dim deg = Math.Atan2(dy, dx) * 180 / Math.PI + 90
                If ModifierKeys.HasFlag(Keys.Shift) Then deg = Math.Round(deg / 15) * 15
                If deg > 180 Then deg -= 360
                RaiseEvent OverlayRotated(_dragOverlay, CSng(deg))
            End If
            Return
        End If

        If _tool = CanvasTool.Overlay AndAlso _drag = DragMode.None Then
            Dim index As Integer
            Select Case HitOverlay(e.Location, index)
                Case OverlayHit.Corner : Cursor = Cursors.SizeNWSE
                Case OverlayHit.Rotate : Cursor = Cursors.Hand
                Case OverlayHit.Body : Cursor = Cursors.SizeAll
                Case Else : Cursor = Cursors.Default
            End Select
        End If

        If _drag = DragMode.Pan Then
            Dim b = ImageBounds()
            _center = New PointF(_dragStartCenter.X - (e.X - _dragAnchor.X) / b.Width,
                                 _dragStartCenter.Y - (e.Y - _dragAnchor.Y) / b.Height)
            ClampCenter()
            Invalidate()
            Return
        End If

        If _drag = DragMode.Paint Then
            ' 移動超過半徑的 1/3 才加點，筆觸平滑又不會累積太多點。
            Dim b = ImageBounds()
            Dim last = _stroke(_stroke.Count - 1)
            Dim dx = (ClampedNormalized(e.Location).X - last.X) * b.Width, dy = (ClampedNormalized(e.Location).Y - last.Y) * b.Height
            If dx * dx + dy * dy >= (_brushRadius / 3) ^ 2 Then _stroke.Add(ClampedNormalized(e.Location))
            Return
        End If

        If Not _cropMode Then
            If _tool = CanvasTool.None Then Cursor = If(IsFit, Cursors.Default, Cursors.Hand)
            Return
        End If
        If _drag = DragMode.None Then
            Dim handle = CropHandleAt(e.Location)
            Cursor = If(handle >= 0, CropHandleCursor(handle), If(CropToScreen(ImageBounds()).Contains(e.Location), Cursors.SizeAll, Cursors.Cross))
            Return
        End If

        Dim p = ScreenToImage(e.Location)
        Dim iw = CDbl(_image.Width), ih = CDbl(_image.Height)
        If _drag = DragMode.ResizeCrop Then
            _crop = ResizedCrop(p, iw, ih)
            Invalidate()
            RaiseEvent CropChanged(Me, EventArgs.Empty)
            Return
        End If
        If _drag = DragMode.Move Then
            Dim nx = _dragStartCrop.X + (p.X - _dragAnchor.X) / iw
            Dim ny = _dragStartCrop.Y + (p.Y - _dragAnchor.Y) / ih
            _crop = New CropRect(nx, ny, _dragStartCrop.Width, _dragStartCrop.Height).Normalized()
        Else
            Dim dx = CDbl(p.X - _dragAnchor.X), dy = CDbl(p.Y - _dragAnchor.Y)
            Dim w = Math.Abs(dx), h = Math.Abs(dy)
            If w < 4 OrElse h < 4 Then Return
            If _aspectRatio > 0 Then
                ' 終點已夾在影像內，只縮不放，所以結果仍在影像範圍內。
                If w / h > _aspectRatio Then w = h * _aspectRatio Else h = w / _aspectRatio
            End If
            Dim x = If(dx >= 0, _dragAnchor.X, _dragAnchor.X - w)
            Dim y = If(dy >= 0, _dragAnchor.Y, _dragAnchor.Y - h)
            _crop = New CropRect(x / iw, y / ih, w / iw, h / ih).Normalized()
        End If
        Invalidate()
        RaiseEvent CropChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _drag = DragMode.Draw Then
            _drag = DragMode.None
            Capture = False
            _drawHost?.DrawMouseUp(e)
            Return
        End If
        If _drag = DragMode.Paint AndAlso _stroke IsNot Nothing Then
            Dim pts = _stroke
            _stroke = Nothing
            _drag = DragMode.None
            Capture = False
            Invalidate()
            RaiseEvent StrokeCompleted(pts, _brushRadius)
            Return
        End If
        If _drag = DragMode.Gradient AndAlso _gradientLine.HasValue Then
            _drag = DragMode.None
            Capture = False
            Dim line = _gradientLine.Value
            Dim dx = (line.End.X - line.Start.X) * ImageBounds().Width, dy = (line.End.Y - line.Start.Y) * ImageBounds().Height
            ' 只是點一下（沒拉開）不算。
            If dx * dx + dy * dy >= 100 Then RaiseEvent GradientDefined(line.Start, line.End)
            Return
        End If
        If _drag = DragMode.Pan Then Cursor = If(_tool = CanvasTool.None, Cursors.Hand, Cursors.Cross)
        _drag = DragMode.None
        Capture = False
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _mousePos.HasValue Then
            _mousePos = Nothing
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        ClampCenter()
    End Sub
End Class

''' <summary>
''' 繪圖板的筆壓：Windows 8 之後筆的輸入會先送 WM_POINTER 訊息，沒處理時系統再轉成滑鼠訊息。
''' 畫布在訊息往下傳之前讀出筆壓，滑鼠事件裡就能用 PenPressure 取得（不是筆時為 Nothing）。
''' </summary>
Partial Friend Class PreviewCanvas
    Private Const WM_POINTERUPDATE As Integer = &H245
    Private Const WM_POINTERDOWN As Integer = &H246
    Private Const WM_POINTERUP As Integer = &H247
    Private Const PT_PEN As Integer = 3
    Private Const PEN_MASK_PRESSURE As UInteger = 1

    Private Const PEN_MASK_ROTATION As UInteger = 2
    Private Const PEN_MASK_TILT_X As UInteger = 4
    Private Const PEN_MASK_TILT_Y As UInteger = 8

    Private _penPressure As Single?
    Private _penTime As Integer
    ''' <summary>繪圖筆倒過來用（橡皮擦端）：PEN_FLAG_INVERTED 或 PEN_FLAG_ERASER。</summary>
    Private _penEraser As Boolean
    Private _penTiltX As Single, _penTiltY As Single, _penRotation As Single

    ''' <summary>繪圖筆的傾斜（度）與筆身旋轉（度）；不是用筆時全部為 0。</summary>
    Public ReadOnly Property PenAngles As (TiltX As Single, TiltY As Single, Rotation As Single)
        Get
            If Not PenPressure.HasValue Then Return (0, 0, 0)
            Return (_penTiltX, _penTiltY, _penRotation)
        End Get
    End Property

    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure NativePoint
        Public X As Integer
        Public Y As Integer
    End Structure

    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure POINTER_INFO
        Public pointerType As Integer
        Public pointerId As UInteger
        Public frameId As UInteger
        Public pointerFlags As Integer
        Public sourceDevice As IntPtr
        Public hwndTarget As IntPtr
        Public ptPixelLocation As NativePoint
        Public ptHimetricLocation As NativePoint
        Public ptPixelLocationRaw As NativePoint
        Public ptHimetricLocationRaw As NativePoint
        Public dwTime As UInteger
        Public historyCount As UInteger
        Public InputData As Integer
        Public dwKeyStates As UInteger
        Public PerformanceCount As ULong
        Public ButtonChangeType As Integer
    End Structure

    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure POINTER_PEN_INFO
        Public pointerInfo As POINTER_INFO
        Public penFlags As UInteger
        Public penMask As UInteger
        Public pressure As UInteger
        Public rotation As UInteger
        Public tiltX As Integer
        Public tiltY As Integer
    End Structure

    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetPointerType(pointerId As UInteger, ByRef pointerType As Integer) As Boolean
    End Function

    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetPointerPenInfo(pointerId As UInteger, ByRef penInfo As POINTER_PEN_INFO) As Boolean
    End Function

    ''' <summary>目前的筆壓（0..1）；不是用繪圖筆、或筆已離開時為 Nothing。</summary>
    Public ReadOnly Property PenPressure As Single?
        Get
            If Not _penPressure.HasValue OrElse Environment.TickCount - _penTime > 400 Then Return Nothing
            Return _penPressure
        End Get
    End Property

    ''' <summary>現在用的是繪圖筆的橡皮擦端（筆倒過來）。</summary>
    Public ReadOnly Property PenEraser As Boolean
        Get
            Return _penEraser AndAlso Environment.TickCount - _penTime <= 400
        End Get
    End Property

    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_POINTERDOWN OrElse m.Msg = WM_POINTERUPDATE OrElse m.Msg = WM_POINTERUP Then ReadPen(m)
        MyBase.WndProc(m) ' 交給系統轉成滑鼠訊息
    End Sub

    Private Sub ReadPen(m As Message)
        Try
            Dim id = CUInt(m.WParam.ToInt64() And &HFFFFL)
            Dim type As Integer
            If Not GetPointerType(id, type) OrElse type <> PT_PEN Then Return
            Dim info As POINTER_PEN_INFO
            If Not GetPointerPenInfo(id, info) Then Return
            ' 傾斜（度，−90..90）與筆身旋轉（度，0..359）；筆不回報時為 0。
            _penTiltX = If((info.penMask And PEN_MASK_TILT_X) <> 0, CSng(info.tiltX), 0)
            _penTiltY = If((info.penMask And PEN_MASK_TILT_Y) <> 0, CSng(info.tiltY), 0)
            _penRotation = If((info.penMask And PEN_MASK_ROTATION) <> 0, CSng(info.rotation), 0)
            _penEraser = (info.penFlags And &H6UI) <> 0 ' 0x2 倒過來、0x4 按著橡皮擦端
            If m.Msg = WM_POINTERUP Then
                _penPressure = Nothing
            ElseIf (info.penMask And PEN_MASK_PRESSURE) = 0 Then
                _penPressure = 1 ' 這支筆不回報筆壓
            Else
                _penPressure = CSng(Math.Max(0.02, Math.Min(1, info.pressure / 1024.0)))
            End If
            _penTime = Environment.TickCount
        Catch ex As EntryPointNotFoundException
            ' Windows 7 沒有這些函式：當作滑鼠。
        End Try
    End Sub
End Class
