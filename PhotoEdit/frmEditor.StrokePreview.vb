Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>
''' 畫筆畫時的即時預覽（設定 → 繪圖筆 → 筆畫預覽）：
''' 真實筆刷：每隔約 40 毫秒在背景用和放開時相同的筆刷引擎（筆壓、質感、水彩、特效、對稱、紙紋）把目前的整筆算一次，畫在畫布上；
''' 兩次之間最新的一小段先用依筆壓變粗細的線補上。放開後留著預覽，等畫面重算完才拿掉（不會閃一下）。
''' 快速線條：只畫一條依筆壓變粗細的線（舊電腦或很大的筆刷比較順）。
''' 會讀畫布的筆（混色、塗抹、仿製、模糊…）與橡皮擦一律用快速線條。
''' 逐段筆印（Painter 式，邊畫邊只算新增的筆印）保留在設定裡，之後提供。
''' </summary>
Partial Friend Class frmEditor

    Public Const PreviewEngine As Integer = 0, PreviewLine As Integer = 1, PreviewIncremental As Integer = 2

    Private _strokeSeed As Integer
    Private ReadOnly _pvTimer As New Timer With {.Interval = 40}
    Private _pvBmp As Bitmap
    Private _pvRegion As Rectangle
    Private _pvRenderH As Integer
    Private _pvCount As Integer
    Private _pvOpacity As Single = 1
    Private _pvBusy As Boolean
    Private _pvSerial As Integer
    ''' <summary>已經放開、等畫面重算完才拿掉的預覽。</summary>
    Private _pvHold As Boolean
    Private _pvTimerWired As Boolean

    ''' <summary>這一筆要不要用真實筆刷預覽。</summary>
    Private Function EnginePreviewOn() As Boolean
        If _appSettings.StrokePreview = PreviewLine Then Return False
        If DrawLayer.SamplesCanvas(_drawStyle.Brush) Then Return False
        If _freeRaster AndAlso IsErasing() Then Return False
        Return True
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
        If EnginePreviewOn() Then _pvTimer.Start()
    End Sub

    ''' <summary>放開（CommitFreehand 之後）：預覽留到畫面重算完。</summary>
    Private Sub EndStrokePreview()
        _pvTimer.Stop()
        _pvSerial += 1 ' 還在算的那次不要用了
        _pvBusy = False
        If _pvBmp IsNot Nothing Then _pvHold = True
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

    ''' <summary>每 40 毫秒：上一次算完、而且筆畫有變時，在背景再算一次。</summary>
    Private Sub StrokePreviewTick()
        If _dd <> DrawDrag.Freehand OrElse _freePoints Is Nothing OrElse Not EnginePreviewOn() Then _pvTimer.Stop() : Return
        If _pvBusy OrElse _freePoints.Count = _pvCount Then Return
        _pvBusy = True
        Dim layer = PreviewStrokeLayer()
        Dim count = _freePoints.Count
        Dim h = Math.Max(32, Math.Min(2400, CInt(Math.Round(PxPerUnit()))))
        Dim w = Math.Max(32, CInt(Math.Ceiling(PhotoAspect() * h)))
        Dim paper = _recipe.Paper
        Dim serial = _pvSerial
        Dim opacity = Math.Max(0, Math.Min(100, _drawStyle.Opacity)) / 100.0F
        Threading.Tasks.Task.Run(Function() DrawingRenderer.RenderPreview(layer, w, h, paper)).
            ContinueWith(Sub(t)
                             _pvBusy = False
                             If t.IsFaulted Then Return
                             Dim r = t.Result
                             If serial <> _pvSerial OrElse IsDisposed Then
                                 r.Bitmap?.Dispose()
                                 Return
                             End If
                             _pvBmp?.Dispose()
                             _pvBmp = r.Bitmap
                             _pvRegion = r.Region
                             _pvRenderH = h
                             _pvCount = count
                             _pvOpacity = opacity
                             _canvas.Invalidate()
                         End Sub, Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext())
    End Sub

    ''' <summary>畫面：真實筆刷的預覽圖，加上還沒算到的最新一段；沒有預覽圖時畫依筆壓變粗細的線。回傳 True＝已經畫了。</summary>
    Private Function PaintStrokePreview(g As Graphics, color As Color, widthPx As Single) As Boolean
        Dim engine = EnginePreviewOn()
        If _pvBmp IsNot Nothing AndAlso (engine OrElse _pvHold) Then
            ' 預覽圖是照片座標（高度 = _pvRenderH 像素）：換成畫面上的位置
            Dim k = 1.0F / _pvRenderH
            Dim a = UnitToScreen(New PointF(_pvRegion.X * k, _pvRegion.Y * k)), b = UnitToScreen(New PointF(_pvRegion.Right * k, _pvRegion.Bottom * k))
            Using ia As New ImageAttributes()
                If _pvOpacity < 0.999F Then ia.SetColorMatrix(New ColorMatrix With {.Matrix33 = _pvOpacity})
                Dim state = g.Save()
                g.InterpolationMode = InterpolationMode.HighQualityBilinear
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.DrawImage(_pvBmp, Rectangle.Round(RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y)), 0, 0, _pvBmp.Width, _pvBmp.Height, GraphicsUnit.Pixel, ia)
                g.Restore(state)
            End Using
            If _pvHold OrElse _freePoints Is Nothing Then Return True
            ' 還沒算到的最新一段（從預覽的最後一點接上去）
            If _freePoints.Count > _pvCount Then PaintPressureLine(g, color, widthPx, Math.Max(0, _pvCount - 1))
            Return True
        End If
        If _freePoints Is Nothing Then Return False
        PaintPressureLine(g, color, widthPx, 0)
        Return False ' 對稱的複本由原本的程式畫
    End Function

    ''' <summary>放開後、畫面重算完之前：畫留著的預覽。</summary>
    Private Sub PaintHeldPreview(g As Graphics)
        If _pvHold AndAlso _pvBmp IsNot Nothing Then PaintStrokePreview(g, Color.Black, 1)
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
