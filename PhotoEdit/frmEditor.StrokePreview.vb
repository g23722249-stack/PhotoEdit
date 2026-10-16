Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>
''' 畫筆畫時的即時預覽（設定 → 筆畫預覽），依負載由輕到重：
''' 快速線條：只畫一條依筆壓變粗細的線（舊電腦或很大的筆刷比較順）。
''' 逐段筆印：像 Painter，邊畫邊只把新增的筆印疊上去（DrawingRenderer.StrokeSession），長筆畫也不會越來越慢；
'''   會讀畫布的筆（混色、塗抹、仿製、模糊…）也看得到真實的效果（SamplingSession）。要知道筆畫總長的效果
'''   （收尾變細、筆毛乾掉）放開時才出現；特效筆、粒子、乾刷、陰影等不能逐段畫的，自動改用真實筆刷。
''' 真實筆刷：每次在背景用和放開時相同的引擎把目前的整筆算一次，看到的就是放開後的樣子；筆畫越長越吃力。
''' 背景同一時間只算一次，算完有新的點就接著算；計算由筆的移動帶動（WM_TIMER 在繪圖筆輸入時常常輪不到），
''' 計時器只當備援。放開後留著預覽，等畫面重算完才拿掉（不會閃一下）。橡皮擦一律用快速線條。
''' </summary>
Partial Friend Class frmEditor

    Public Const PreviewEngine As Integer = 0, PreviewLine As Integer = 1, PreviewIncremental As Integer = 2

    ''' <summary>這一筆實際用的預覽方式。</summary>
    Private Enum StrokePreviewKind
        Line
        Engine
        Incremental
        Sampling
    End Enum

    Private _strokeSeed As Integer
    Private ReadOnly _pvTimer As New Timer With {.Interval = 40}
    Private _pvKind As StrokePreviewKind = StrokePreviewKind.Line
    ' 真實筆刷：整筆一張圖
    Private _pvBmp As Bitmap
    Private _pvRegion As Rectangle
    ' 逐段筆印：一格一格的圖（照片座標，預覽解析度）
    Private ReadOnly _pvTiles As New Dictionary(Of Rectangle, Bitmap)()
    Private _pvStroke As DrawingRenderer.StrokeSession
    Private _pvSampling As DrawingRenderer.SamplingSession
    Private _pvRenderH As Integer
    Private _pvCount As Integer
    Private _pvOpacity As Single = 1
    ''' <summary>背景正在算的那次（同一時間只算一個，跨筆畫也是：兩個同時算會搶共用的快取）。</summary>
    Private _pvTask As Threading.Tasks.Task
    Private _pvSerial As Integer
    ''' <summary>已經放開、等畫面重算完才拿掉的預覽。</summary>
    Private _pvHold As Boolean
    Private _pvTimerWired As Boolean

    ''' <summary>這一筆有沒有真實的預覽（真實筆刷或逐段筆印）。</summary>
    Private Function EnginePreviewOn() As Boolean
        Return _pvKind <> StrokePreviewKind.Line
    End Function

    ''' <summary>依設定與筆刷決定這一筆怎麼預覽。</summary>
    Private Function ChoosePreviewKind() As StrokePreviewKind
        Dim mode = _appSettings.StrokePreview
        If mode = PreviewLine Then Return StrokePreviewKind.Line
        If _freeRaster AndAlso IsErasing() Then Return StrokePreviewKind.Line
        If DrawLayer.SamplesCanvas(_drawStyle.Brush) Then
            ' 讀畫布的筆：只有逐段筆印做得到（真實筆刷要整張重算）
            If mode = PreviewIncremental AndAlso _freeRaster AndAlso DrawingRenderer.SamplingSession.Supports(PreviewSamplingOp()) Then Return StrokePreviewKind.Sampling
            Return StrokePreviewKind.Line
        End If
        If mode = PreviewIncremental AndAlso DrawingRenderer.StrokeSession.Supports(PreviewStrokeLayer()) Then Return StrokePreviewKind.Incremental
        Return StrokePreviewKind.Engine
    End Function

    ''' <summary>開始一筆（StartFreehand 呼叫）：決定這一筆的亂數種子，預覽和放開後的結果才會一模一樣。</summary>
    Private Sub BeginStrokePreview()
        ClearStrokePreview()
        _strokeSeed = _drawRandom.Next(1, 100000)
        _pvSerial += 1
        If Not _pvTimerWired Then
            AddHandler _pvTimer.Tick, Sub() StrokePreviewTick()
            _pvTimerWired = True
        End If
        _pvKind = ChoosePreviewKind()
        Dim size = PreviewCanvasSize(_pvKind = StrokePreviewKind.Sampling)
        _pvRenderH = size.Height
        Select Case _pvKind
            Case StrokePreviewKind.Incremental
                _pvStroke = New DrawingRenderer.StrokeSession(size.Width, size.Height, _recipe.Paper)
            Case StrokePreviewKind.Sampling
                Dim snap = CanvasSnapshot(size)
                If snap Is Nothing Then
                    _pvKind = StrokePreviewKind.Line
                Else
                    _pvSampling = New DrawingRenderer.SamplingSession(snap, size.Width, size.Height)
                End If
        End Select
        If EnginePreviewOn() Then _pvTimer.Start()
    End Sub

    ''' <summary>預覽畫布的大小：照片高度 = 畫面上的像素數（讀畫布的筆最多 1200，整張畫布都要放在記憶體裡）。</summary>
    Private Function PreviewCanvasSize(sampling As Boolean) As Size
        Dim h = Math.Max(32, Math.Min(If(sampling, 1200, 2400), CInt(Math.Round(PxPerUnit()))))
        Return New Size(Math.Max(32, CInt(Math.Ceiling(PhotoAspect() * h))), h)
    End Function

    ''' <summary>畫面上照片（不含邊框）的樣子縮成 size（BGRA）：讀畫布的筆的起點。</summary>
    Private Function CanvasSnapshot(size As Size) As Byte()
        If _rendered Is Nothing Then Return Nothing
        Dim src = If(_framePhotoSize.IsEmpty, New Rectangle(Point.Empty, _rendered.Size),
                     New Rectangle(_frameMargins.Left, _frameMargins.Top, _framePhotoSize.Width, _framePhotoSize.Height))
        Using bmp As New Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.InterpolationMode = InterpolationMode.HighQualityBilinear
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.DrawImage(_rendered, New Rectangle(Point.Empty, size), src, GraphicsUnit.Pixel)
            End Using
            Dim data = bmp.LockBits(New Rectangle(Point.Empty, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Try
                Dim px(size.Width * size.Height * 4 - 1) As Byte
                For y = 0 To size.Height - 1
                    Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * size.Width * 4, size.Width * 4)
                Next
                Return px
            Finally
                bmp.UnlockBits(data)
            End Try
        End Using
    End Function

    ''' <summary>放開（CommitFreehand 之後）：預覽留到畫面重算完。</summary>
    Private Sub EndStrokePreview()
        _pvTimer.Stop()
        _pvKind = StrokePreviewKind.Line
        _pvStroke = Nothing : _pvSampling = Nothing
        _pvSerial += 1 ' 還在算的那次不要用了（算完會丟掉，下一筆等它算完才開始算）
        If _pvBmp IsNot Nothing OrElse _pvTiles.Count > 0 Then _pvHold = True
    End Sub

    ''' <summary>畫面重算完（RenderNow 結尾）：拿掉放開前留著的預覽。</summary>
    Private Sub StrokePreviewRendered()
        If _pvHold Then
            ClearStrokePreview()
            _canvas.Invalidate()
        End If
    End Sub

    Private Sub ClearStrokePreview()
        _pvHold = False
        _pvBmp?.Dispose()
        _pvBmp = Nothing
        For Each b In _pvTiles.Values
            b?.Dispose()
        Next
        _pvTiles.Clear()
        _pvCount = 0
    End Sub

    ''' <summary>目前的筆畫（和放開時建立的圖層同樣的樣式與種子）。</summary>
    Private Function PreviewStrokeLayer() As DrawLayer
        Dim pts = _freePoints.Select(Function(p) p.Clone()).ToList()
        Dim seed = _strokeSeed
        If Not _freeRaster Then
            ' 接著畫在同一個自由繪製圖層時，用那個圖層的種子
            Dim sel = SelDraw(_recipe)
            If sel IsNot Nothing AndAlso sel.Shape = DrawShape.Freehand AndAlso sel.Visible AndAlso Not sel.Locked AndAlso sel.SameStroke(_drawStyle) Then seed = sel.Seed
        End If
        Dim d As New DrawLayer With {.Shape = DrawShape.Freehand, .Seed = seed, .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}}
        d.CopyStyleFrom(_drawStyle)
        d.Opacity = 100 ' 不透明度在畫的時候套用
        Return d
    End Function

    ''' <summary>讀畫布的筆這一筆（同 CommitRasterStroke：不透明度是筆的強度、仿製的來源距離）。</summary>
    Private Function PreviewSamplingOp() As DrawLayer
        Dim pts = If(_freePoints, New List(Of DrawPoint)()).Select(Function(p) p.Clone()).ToList()
        Dim op As New DrawLayer With {.Shape = DrawShape.Freehand, .Seed = _strokeSeed, .Strokes = New List(Of DrawStroke) From {New DrawStroke With {.Points = pts}}}
        op.CopyStyleFrom(_drawStyle)
        If DrawLayer.UsesCloneSource(op.Brush) AndAlso pts.Count > 0 Then
            Dim offset = _cloneOffset
            If Not offset.HasValue AndAlso _cloneSource.HasValue Then offset = New PointF(_cloneSource.Value.X - pts(0).X, _cloneSource.Value.Y - pts(0).Y)
            If offset.HasValue Then op.CloneDX = Math.Round(offset.Value.X, 5) : op.CloneDY = Math.Round(offset.Value.Y, 5)
        End If
        Return op
    End Function

    ''' <summary>上一次算完、而且筆畫有變時，在背景再算一次（筆移動時與計時器都會呼叫）。</summary>
    Private Sub StrokePreviewTick()
        If _dd <> DrawDrag.Freehand OrElse _freePoints Is Nothing OrElse Not EnginePreviewOn() Then _pvTimer.Stop() : Return
        If PreviewBusy() OrElse _freePoints.Count = _pvCount Then Return
        Dim count = _freePoints.Count
        Dim serial = _pvSerial
        Select Case _pvKind
            Case StrokePreviewKind.Engine
                Dim layer = PreviewStrokeLayer()
                Dim size = PreviewCanvasSize(False)
                Dim h = size.Height
                Dim paper = _recipe.Paper
                Dim opacity = Math.Max(0, Math.Min(100, _drawStyle.Opacity)) / 100.0F
                Dim render = Threading.Tasks.Task.Run(Function() DrawingRenderer.RenderPreview(layer, size.Width, h, paper))
                _pvTask = render
                ' 算完明確交回 UI 執行緒（不靠 SynchronizationContext：沒有 WinForms 的同步內容時接續會跑在背景執行緒，
                ' 換掉正在畫的預覽圖 → GDI+「Object is currently in use elsewhere」）
                render.ContinueWith(Sub(t) PostToUi(Sub() ApplyStrokePreview(t, serial, count, h, opacity),
                                                    Sub() If Not t.IsFaulted Then t.Result.Bitmap?.Dispose()))
            Case StrokePreviewKind.Incremental, StrokePreviewKind.Sampling
                Dim session = _pvStroke, sampling = _pvSampling
                Dim layer = If(_pvKind = StrokePreviewKind.Sampling, PreviewSamplingOp(), PreviewStrokeLayer())
                Dim opacity = If(_pvKind = StrokePreviewKind.Sampling, 1.0F, Math.Max(0, Math.Min(100, _drawStyle.Opacity)) / 100.0F)
                Dim render = Threading.Tasks.Task.Run(Function() If(session IsNot Nothing, session.Update(layer, count), sampling.Update(layer, count)))
                _pvTask = render
                render.ContinueWith(Sub(t) PostToUi(Sub() ApplyTilePreview(t, serial, count, opacity),
                                                    Sub() If Not t.IsFaulted Then t.Result.ForEach(Sub(tile) tile.Bitmap?.Dispose())))
        End Select
    End Sub

    ''' <summary>（背景執行緒）把結果交回 UI 執行緒；視窗已經關了就丟掉。</summary>
    Private Sub PostToUi(apply As Action, discard As Action)
        Try
            If IsDisposed OrElse Not IsHandleCreated Then Throw New ObjectDisposedException(Name)
            BeginInvoke(apply)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
            discard()
        End Try
    End Sub

    ''' <summary>（UI 執行緒）真實筆刷：背景算好的整張預覽換上畫面。</summary>
    Private Sub ApplyStrokePreview(t As Threading.Tasks.Task(Of (Bitmap As Bitmap, Region As Rectangle)), serial As Integer, count As Integer, h As Integer, opacity As Single)
        If t.IsFaulted Then Return
        Dim r = t.Result
        If serial <> _pvSerial OrElse IsDisposed Then
            r.Bitmap?.Dispose()
            ' 上一筆留下的計算剛算完：這一筆已經在畫了就接著算
            If Not IsDisposed AndAlso EnginePreviewOn() Then StrokePreviewTick()
            Return
        End If
        _pvBmp?.Dispose()
        _pvBmp = r.Bitmap
        _pvRegion = r.Region
        _pvRenderH = h
        _pvCount = count
        _pvOpacity = opacity
        _canvas.Invalidate()
        ' 算的時候又畫了新的點：馬上接著算下一次（不等計時器）
        If _freePoints IsNot Nothing AndAlso _freePoints.Count > count Then StrokePreviewTick()
    End Sub

    ''' <summary>（UI 執行緒）逐段筆印：改到的格子換上新的圖。</summary>
    Private Sub ApplyTilePreview(t As Threading.Tasks.Task(Of List(Of DrawingRenderer.PreviewTile)), serial As Integer, count As Integer, opacity As Single)
        If t.IsFaulted Then Return
        If serial <> _pvSerial OrElse IsDisposed Then
            t.Result.ForEach(Sub(tile) tile.Bitmap?.Dispose())
            If Not IsDisposed AndAlso EnginePreviewOn() Then StrokePreviewTick()
            Return
        End If
        For Each tile In t.Result
            Dim old As Bitmap = Nothing
            If _pvTiles.TryGetValue(tile.Bounds, old) Then old?.Dispose()
            If tile.Bitmap Is Nothing Then _pvTiles.Remove(tile.Bounds) Else _pvTiles(tile.Bounds) = tile.Bitmap
        Next
        _pvCount = count
        _pvOpacity = opacity
        _canvas.Invalidate()
        If _freePoints IsNot Nothing AndAlso _freePoints.Count > count Then StrokePreviewTick()
    End Sub

    Private Function PreviewBusy() As Boolean
        Return _pvTask IsNot Nothing AndAlso Not _pvTask.IsCompleted
    End Function

    ''' <summary>
    ''' 筆移動時呼叫：背景沒有在算就馬上算（一次只算一個，算完有新的點會接著算，等於一張接一張）。
    ''' 只靠 WinForms 計時器不行：WM_TIMER 的優先順序最低，繪圖筆高頻率的輸入加上一直重畫畫布時計時器幾乎輪不到，
    ''' 看起來就像真實筆刷沒有作用（只看得到快速線條）。計時器留著當備援。
    ''' </summary>
    Private Sub KickStrokePreview()
        If Not EnginePreviewOn() OrElse PreviewBusy() Then Return
        StrokePreviewTick()
    End Sub

    ''' <summary>畫面：預覽圖（整張或一格一格），加上還沒算到的最新一段；沒有預覽圖時畫依筆壓變粗細的線。回傳 True＝已經畫了。</summary>
    Private Function PaintStrokePreview(g As Graphics, color As Color, widthPx As Single) As Boolean
        Dim live = EnginePreviewOn()
        Dim hasImage = _pvBmp IsNot Nothing OrElse _pvTiles.Count > 0
        If hasImage AndAlso (live OrElse _pvHold) Then
            Using ia As New ImageAttributes()
                If _pvOpacity < 0.999F Then ia.SetColorMatrix(New ColorMatrix With {.Matrix33 = _pvOpacity})
                ia.SetWrapMode(WrapMode.TileFlipXY) ' 縮放時邊緣不要混到透明（格子之間才不會有細縫）
                Dim state = g.Save()
                g.InterpolationMode = InterpolationMode.HighQualityBilinear
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                If _pvBmp IsNot Nothing Then DrawPreviewImage(g, _pvBmp, _pvRegion, ia)
                For Each kv In _pvTiles
                    DrawPreviewImage(g, kv.Value, kv.Key, ia)
                Next
                g.Restore(state)
            End Using
            If _pvHold OrElse _freePoints Is Nothing Then Return True
            ' 還沒算到的最新一段（從預覽的最後一點接上去）；讀畫布的筆畫線沒有意義，等算好再看
            If _freePoints.Count > _pvCount AndAlso _pvKind <> StrokePreviewKind.Sampling Then PaintPressureLine(g, color, widthPx, Math.Max(0, _pvCount - 1))
            Return True
        End If
        If _freePoints Is Nothing Then Return False
        If _pvKind = StrokePreviewKind.Sampling Then Return True ' 第一段還沒算好
        PaintPressureLine(g, color, widthPx, 0)
        Return False ' 對稱的複本由原本的程式畫
    End Function

    ''' <summary>預覽圖（照片座標，高度 = _pvRenderH 像素）畫到畫面上的位置。</summary>
    Private Sub DrawPreviewImage(g As Graphics, img As Bitmap, r As Rectangle, ia As ImageAttributes)
        Dim k = 1.0F / _pvRenderH
        Dim a = UnitToScreen(New PointF(r.X * k, r.Y * k)), b = UnitToScreen(New PointF(r.Right * k, r.Bottom * k))
        ' 格子之間不能有縫：位置取整後相鄰的格子剛好接上
        Dim ax = CInt(Math.Round(a.X)), ay = CInt(Math.Round(a.Y)), bx = CInt(Math.Round(b.X)), by = CInt(Math.Round(b.Y))
        If ax = bx OrElse by <= ay Then Return
        ' 左上、右上、左下三點：左右翻轉檢視時右上在左邊，圖跟著翻
        g.DrawImage(img, {New Point(ax, ay), New Point(bx, ay), New Point(ax, by)}, New Rectangle(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, ia)
    End Sub

    ''' <summary>放開後、畫面重算完之前：畫留著的預覽。</summary>
    Private Sub PaintHeldPreview(g As Graphics)
        If _pvHold AndAlso (_pvBmp IsNot Nothing OrElse _pvTiles.Count > 0) Then PaintStrokePreview(g, Color.Black, 1)
    End Sub

    ''' <summary>依筆壓變粗細的線（從第 from 個點開始）。</summary>
    Private Sub PaintPressureLine(g As Graphics, color As Color, widthPx As Single, from As Integer)
        Dim pts = _freePoints
        If pts.Count - from < 1 Then Return
        Dim hasPressure = _canvas.PenPressure.HasValue
        Dim widthAt = Function(p As Single) If(hasPressure, Math.Max(1.0F, widthPx * (0.25F + 0.75F * Math.Max(0, Math.Min(1, p)))), widthPx)
        If Not hasPressure AndAlso pts.Count - from >= 2 Then
            ' 滑鼠：粗細固定，一次畫完（半透明時接縫不會變深）
            Using pen As New Pen(color, widthPx) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                g.DrawLines(pen, pts.Skip(from).Select(Function(p) UnitToScreen(p.ToPointF())).ToArray())
            End Using
            Return
        End If
        Using br As New SolidBrush(color)
            If pts.Count - from = 1 Then
                Dim s = UnitToScreen(pts(from).ToPointF())
                Dim w = widthAt(pts(from).P)
                g.FillEllipse(br, s.X - w / 2, s.Y - w / 2, w, w)
                Return
            End If
            For i = from + 1 To pts.Count - 1
                Dim a = UnitToScreen(pts(i - 1).ToPointF()), b = UnitToScreen(pts(i).ToPointF())
                Using pen As New Pen(color, widthAt((pts(i - 1).P + pts(i).P) / 2)) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                    g.DrawLine(pen, a, b)
                End Using
            Next
        End Using
    End Sub
End Class
