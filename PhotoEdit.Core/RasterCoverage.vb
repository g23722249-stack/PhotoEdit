Imports System.Drawing
Imports System.Drawing.Imaging

''' <summary>
''' 點陣圖層「實際有顏色」的範圍：用高度 300 像素的縮圖合成一次，記下哪些像素看得到。
''' 給點選（點在畫過的線上才算點到，框內空白不算）與選取框（貼著看得到的內容、不含畫布外與擦掉的地方）用。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    Private Const CoverageHeight As Integer = 300
    Private Const CoverageAlpha As Byte = 16

    Private NotInheritable Class CoverageMap
        Public Mask As Byte()
        Public W As Integer
        Public H As Integer
        ''' <summary>看得到的像素範圍（圖層座標＝照片高度單位，未加位移）；空的＝什麼都沒有。</summary>
        Public Bounds As RectangleF
    End Class

    Private Shared ReadOnly _coverage As New Dictionary(Of String, CoverageMap)()
    Private Shared ReadOnly _coverageOrder As New LinkedList(Of String)()

    ''' <summary>
    ''' 快速指紋：筆數、照片寬高比，加上第一筆與最後一筆的內容。
    ''' 拖曳時圖層每次都是複製出來的（物件不同），用內容比對才能沿用；位移不影響（在圖層座標算）。
    ''' </summary>
    Private Shared Function CoverageKey(layer As DrawLayer, aspect As Double) As String
        Dim ops = layer.Ops
        Dim first = System.Text.Json.JsonSerializer.Serialize(ops(0), _jsonOptions)
        Dim last = If(ops.Count > 1, System.Text.Json.JsonSerializer.Serialize(ops(ops.Count - 1), _jsonOptions), "")
        Return $"{aspect:0.00000}|{ops.Count}|{ChainHash(ChainHash(0UL, first), last)}"
    End Function

    Private Shared Function CoverageOf(layer As DrawLayer, aspect As Double) As CoverageMap
        If layer Is Nothing OrElse layer.Shape <> DrawShape.Raster OrElse layer.Ops Is Nothing OrElse layer.Ops.Count = 0 Then Return Nothing
        If aspect <= 0 Then aspect = 1
        Dim key = CoverageKey(layer, aspect)
        SyncLock _coverage
            Dim hit As CoverageMap = Nothing
            If _coverage.TryGetValue(key, hit) Then Return hit
        End SyncLock
        Dim h = CoverageHeight, w = Math.Max(1, CInt(Math.Round(aspect * CoverageHeight)))
        ' 混色、塗抹、油漆桶要讀下面的畫面：這裡給透明底（只是估範圍）
        Dim below(w * h * 4 - 1) As Byte
        Dim local As New DrawLayer With {.Shape = DrawShape.Raster, .Ops = layer.Ops}
        Dim bmp = RasterBitmap(local, w, h, below)
        Dim map As New CoverageMap With {.W = w, .H = h, .Mask = New Byte(w * h - 1) {}, .Bounds = RectangleF.Empty}
        If bmp IsNot Nothing Then
            Dim px As Byte()
            SyncLock bmp
                px = Perspective.ReadPixels(bmp)
            End SyncLock
            Dim l = w, t = h, r = -1, b = -1
            For y = 0 To h - 1
                For x = 0 To w - 1
                    If px((y * w + x) * 4 + 3) < CoverageAlpha Then Continue For
                    map.Mask(y * w + x) = 1
                    If x < l Then l = x
                    If x > r Then r = x
                    If y < t Then t = y
                    If y > b Then b = y
                Next
            Next
            If r >= 0 Then map.Bounds = RectangleF.FromLTRB(CSng(l / h), CSng(t / h), CSng((r + 1) / h), CSng((b + 1) / h))
        End If
        SyncLock _coverage
            If Not _coverage.ContainsKey(key) Then
                _coverage(key) = map
                _coverageOrder.AddFirst(key)
                While _coverageOrder.Count > 16
                    _coverage.Remove(_coverageOrder.Last.Value)
                    _coverageOrder.RemoveLast()
                End While
            End If
        End SyncLock
        Return map
    End Function

    ''' <summary>
    ''' 點陣圖層看得到的內容範圍（照片座標，含圖層位移，裁在畫布內）；aspect＝照片寬高比。
    ''' 沒有看得到的內容時回傳空的。
    ''' </summary>
    Public Shared Function RasterVisibleBounds(layer As DrawLayer, aspect As Double) As RectangleF
        Dim map = CoverageOf(layer, aspect)
        If map Is Nothing OrElse map.Bounds.IsEmpty Then Return RectangleF.Empty
        Dim b = map.Bounds
        Dim world = RectangleF.FromLTRB(CSng(b.Left + layer.X), CSng(b.Top + layer.Y), CSng(b.Right + layer.X), CSng(b.Bottom + layer.Y))
        Return RectangleF.Intersect(world, New RectangleF(0, 0, CSng(aspect), 1))
    End Function

    ''' <summary>
    ''' 點 p（照片座標）有沒有點到點陣圖層畫過的地方：p 附近 tolerance（照片高度單位）以內有看得到的像素才算。
    ''' 畫布外的點不算。
    ''' </summary>
    Public Shared Function RasterHit(layer As DrawLayer, aspect As Double, p As PointF, tolerance As Double) As Boolean
        Dim map = CoverageOf(layer, aspect)
        If map Is Nothing OrElse map.Bounds.IsEmpty Then Return False
        If p.X < 0 OrElse p.Y < 0 OrElse p.X > aspect OrElse p.Y > 1 Then Return False
        Dim cx = (p.X - layer.X) * map.H, cy = (p.Y - layer.Y) * map.H
        Dim rad = Math.Max(1, CInt(Math.Ceiling(tolerance * map.H)))
        Dim x0 = Math.Max(0, CInt(Math.Floor(cx)) - rad), x1 = Math.Min(map.W - 1, CInt(Math.Floor(cx)) + rad)
        Dim y0 = Math.Max(0, CInt(Math.Floor(cy)) - rad), y1 = Math.Min(map.H - 1, CInt(Math.Floor(cy)) + rad)
        For y = y0 To y1
            For x = x0 To x1
                If map.Mask(y * map.W + x) = 0 Then Continue For
                If (x + 0.5 - cx) ^ 2 + (y + 0.5 - cy) ^ 2 <= (rad + 0.5) ^ 2 Then Return True
            Next
        Next
        Return False
    End Function
End Class
