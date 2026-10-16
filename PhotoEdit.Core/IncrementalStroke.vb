Imports System.Drawing

''' <summary>
''' 逐段筆印（Painter 式的即時預覽）：畫的時候只把新增的筆印疊上去，不必每次整筆重算，長筆畫也不會越畫越慢。
'''
''' 覆蓋率（與筆毛明暗、顏色變化）存在 128×128 的格子裡，用到才配置。每次更新：
''' 1. 已確定的筆印（平滑曲線不會再變的那一段）蓋進格子，之後不再重算；
''' 2. 最後一小段（還會隨下一個點改變的曲線）蓋在暫存的副本上，下次重畫；
''' 3. 只有改到的範圍（加上水彩暈染、厚塗光影需要的邊）重新上色，換成新的格子圖給畫面。
''' 筆印的位置、亂數都和整筆算的一樣，所以結果相同；只有「收尾變細、筆毛乾掉」這類要知道筆畫總長的效果，
''' 放開時（整筆重算）才出現。畫面上用的格子圖交出去後不再碰，背景執行緒不會和畫面搶同一張圖。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    ''' <summary>預覽的一塊：Bounds 是預覽解析度的照片座標；Bitmap 是 Nothing 表示這一塊已經空了。</summary>
    Public NotInheritable Class PreviewTile
        Public Bounds As Rectangle
        Public Bitmap As Bitmap
    End Class

    ''' <summary>一筆的逐段筆印。同一時間只能由一個執行緒呼叫 Update。</summary>
    Public NotInheritable Class StrokeSession
        Private Const TileSize As Integer = 128
        ''' <summary>「還沒畫完」的總長：收尾變細、筆毛乾掉都不會發生。</summary>
        Private Const Unfinished As Single = 1.0E+9F

        Private NotInheritable Class Tile
            Public Cov As Single()
            Public Streak As Single()
            Public Tint As Single()
            ''' <summary>上好色的像素（BGRA，非預乘），交給畫面前複製成 Bitmap。</summary>
            Public Px As Byte()
        End Class

        Private ReadOnly _w As Integer, _h As Integer
        Private ReadOnly _paper As PaperSettings
        Private ReadOnly _tiles As New Dictionary(Of Integer, Tile)()
        Private _sDone As Single = Single.NegativeInfinity
        Private _prevTail As Rectangle = Rectangle.Empty
        Private _streak As Boolean, _tint As Boolean

        ''' <param name="w">預覽畫布的寬（照片寬換算成預覽像素）。</param>
        ''' <param name="h">預覽畫布的高（= 一個照片單位的像素數）。</param>
        Public Sub New(w As Integer, h As Integer, paper As PaperSettings)
            _w = Math.Max(1, w) : _h = Math.Max(1, h) : _paper = paper
        End Sub

        ''' <summary>這一筆能不能逐段畫（其他的用「真實筆刷」整筆重算）。</summary>
        Public Shared Function Supports(layer As DrawLayer) As Boolean
            If layer Is Nothing OrElse layer.Shape <> DrawShape.Freehand Then Return False
            Select Case layer.Brush
                Case BrushKind.FX, BrushKind.Particle, BrushKind.StickerHose, BrushKind.DryBrush
                    Return False ' 特效沿整筆分佈；乾刷的飛白依整筆的比例
            End Select
            If Global.PhotoEdit.DrawLayer.SamplesCanvas(layer.Brush) Then Return False
            If layer.Brush = BrushKind.Texture AndAlso layer.Material = MaterialKind.Mercury Then Return False
            If layer.Shadow OrElse layer.FadeGradient IsNot Nothing Then Return False
            If NeedsGeometricStroke(layer) OrElse DashPattern(layer.Dash) IsNot Nothing Then Return False
            If layer.StartArrow <> ArrowKind.None OrElse layer.EndArrow <> ArrowKind.None Then Return False
            ' 對稱：好幾個外形共用同一串亂數，筆畫變長時後面外形的亂數會跟著變
            If layer.Symmetry <> SymmetryKind.None AndAlso (layer.SizeJitter > 0 OrElse layer.Scatter > 0 OrElse NeedsTint(layer)) Then Return False
            Return True
        End Function

        ''' <summary>
        ''' 筆畫更新了：layer 是目前整筆（和放開時建立的圖層同樣的樣式與種子），rawCount 是原始的點數。
        ''' 回傳改到的格子（新的圖，舊的由呼叫的人釋放）。
        ''' </summary>
        Public Function Update(layer As DrawLayer, rawCount As Integer) As List(Of PreviewTile)
            Dim result As New List(Of PreviewTile)()
            Dim saved = CurrentPaper
            CurrentPaper = _paper
            Try
                PreparePaper(_w, _h)
                UpdateCore(layer, rawCount, result)
            Finally
                CurrentPaper = saved
            End Try
            Return result
        End Function

        Private Sub UpdateCore(layer As DrawLayer, rawCount As Integer, result As List(Of PreviewTile))
            Dim scale = CSng(_h)
            Dim figs = DrawGeometry.Figures(layer).
                Select(Function(f) New DrawGeometry.Figure With {.Points = f.Points.Select(Function(p) New PointF(p.X * scale, p.Y * scale)).ToArray(),
                                                                 .Closed = f.Closed, .Pressure = f.Pressure, .Angle = f.Angle, .Flat = f.Flat}).
                Where(Function(f) f.Points.Length > 0).ToList()
            If figs.Count = 0 Then Return
            layer = StrokeStyleLayer(layer)
            _streak = NeedsStreak(layer) : _tint = NeedsTint(layer)
            Dim widthPx = CSng(Math.Max(0.6, layer.StrokeWidth * scale))

            ' 平滑曲線（Catmull-Rom）：原始第 i 段要等到第 i + 2 點出現才固定，每段 4 個點。
            Dim stable = If(rawCount >= 3, 4 * (rawCount - 2), 0)
            Dim cum = Cumulative(figs(0).Points)
            Dim sStable = If(stable <= 0, Single.NegativeInfinity, cum(Math.Min(stable, cum.Length) - 1))

            ' 筆印最遠伸到哪（同 Render 的邊）
            Dim reach = widthPx * 1.2F + 4
            Dim extra = 0.0F
            If layer.Scatter > 0 Then extra = Math.Max(extra, widthPx * 1.6F * layer.Scatter / 100.0F + widthPx * 0.5F)
            If layer.SizeJitter > 0 OrElse figs.Any(Function(f) f.Angle IsNot Nothing) Then extra = Math.Max(extra, widthPx * 0.8F)
            reach += extra

            ' 這次要動的範圍：新的確定段＋最後一小段（每個對稱外形同一段距離），再加上次的暫時段（要擦掉）
            Dim newPart = Rectangle.Empty, tail = Rectangle.Empty
            For Each f In figs
                Dim c = Cumulative(f.Points)
                newPart = Union(newPart, Bounds(f.Points, c, _sDone, sStable, reach))
                tail = Union(tail, Bounds(f.Points, c, sStable, Single.PositiveInfinity, reach))
            Next
            Dim dirty = Union(Union(newPart, tail), _prevTail)
            dirty.Intersect(New Rectangle(0, 0, _w, _h))
            If dirty.Width <= 0 OrElse dirty.Height <= 0 Then Return

            ' 上色要看鄰近像素：水彩的暈染半徑、厚塗與材質的光影
            Dim margin = 3
            If layer.Brush = BrushKind.Watercolor Then margin += CInt(Math.Max(1, widthPx * 0.3F))
            Dim win = Rectangle.Inflate(dirty, margin, margin)
            win.Intersect(New Rectangle(0, 0, _w, _h))

            Dim cv As New Canvas(win)
            Dim n = cv.W * cv.H
            Dim cov(n - 1) As Single
            Dim streak As Single() = If(_streak, New Single(n - 1) {}, Nothing)
            Dim tint As Single() = If(_tint, New Single(n * 3 - 1) {}, Nothing)
            Load(win, cov, streak, tint)

            ' 1. 確定的筆印：蓋上去並存回格子
            If sStable > _sDone Then
                StampFigures(cov, streak, tint, cv, figs, layer, widthPx, New DabRange With {.SFrom = _sDone, .STo = sStable, .TotalOverride = Unfinished})
                Store(win, cov, streak, tint)
                _sDone = sStable
            End If
            ' 2. 最後一小段：只畫在這次的副本上
            StampFigures(cov, streak, tint, cv, figs, layer, widthPx, New DabRange With {.SFrom = _sDone, .TotalOverride = Unfinished})
            _prevTail = tail

            ' 3. 上色（整個視窗，只取 dirty 的部分）
            Shade(cv, cov, streak, layer, Color.FromArgb(layer.StrokeColorArgb), widthPx, _h, isFill:=False, tint:=tint)
            WriteShaded(cv, dirty, result)
        End Sub

        Friend Shared Function Cumulative(pts As PointF()) As Single()
            Dim c(pts.Length - 1) As Single
            For i = 1 To pts.Length - 1
                c(i) = c(i - 1) + CSng(DrawGeometry.Dist(pts(i - 1), pts(i)))
            Next
            Return c
        End Function

        ''' <summary>已走距離在 [s0, s1) 的那段路徑（含前後的點）的外框，向外 reach。</summary>
        Friend Shared Function Bounds(pts As PointF(), cum As Single(), s0 As Single, s1 As Single, reach As Single) As Rectangle
            Dim minX = Single.MaxValue, minY = Single.MaxValue, maxX = Single.MinValue, maxY = Single.MinValue
            Dim any = False
            Dim add = Sub(p As PointF)
                          minX = Math.Min(minX, p.X) : minY = Math.Min(minY, p.Y) : maxX = Math.Max(maxX, p.X) : maxY = Math.Max(maxY, p.Y)
                          any = True
                      End Sub
            If pts.Length = 1 Then
                If s0 <= 0 AndAlso 0 < s1 Then add(pts(0))
            Else
                For i = 1 To pts.Length - 1
                    ' 線段 i-1 → i 走過的距離是 [cum(i-1), cum(i)]，和 [s0, s1) 有重疊就算進去
                    If cum(i) < s0 Then Continue For
                    If cum(i - 1) >= s1 Then Exit For
                    add(pts(i - 1)) : add(pts(i))
                Next
            End If
            If Not any Then Return Rectangle.Empty
            Return Rectangle.FromLTRB(CInt(Math.Floor(minX - reach)), CInt(Math.Floor(minY - reach)),
                                      CInt(Math.Ceiling(maxX + reach)) + 1, CInt(Math.Ceiling(maxY + reach)) + 1)
        End Function

        Private Shared Function Union(a As Rectangle, b As Rectangle) As Rectangle
            If a.Width <= 0 OrElse a.Height <= 0 Then Return b
            If b.Width <= 0 OrElse b.Height <= 0 Then Return a
            Return Rectangle.Union(a, b)
        End Function

        Private Function TileOf(tx As Integer, ty As Integer, create As Boolean) As Tile
            Dim key = ty * 65536 + tx
            Dim t As Tile = Nothing
            If _tiles.TryGetValue(key, t) OrElse Not create Then Return t
            t = New Tile With {.Cov = New Single(TileSize * TileSize - 1) {}, .Px = New Byte(TileSize * TileSize * 4 - 1) {}}
            If _streak Then t.Streak = New Single(TileSize * TileSize - 1) {}
            If _tint Then t.Tint = New Single(TileSize * TileSize * 3 - 1) {}
            _tiles(key) = t
            Return t
        End Function

        ''' <summary>格子裡已確定的覆蓋率 → 視窗。</summary>
        Private Sub Load(win As Rectangle, cov As Single(), streak As Single(), tint As Single())
            ForCells(win, False, Sub(t, ti, wi)
                                     cov(wi) = t.Cov(ti)
                                     If streak IsNot Nothing AndAlso t.Streak IsNot Nothing Then streak(wi) = t.Streak(ti)
                                     If tint IsNot Nothing AndAlso t.Tint IsNot Nothing Then
                                         tint(wi * 3) = t.Tint(ti * 3) : tint(wi * 3 + 1) = t.Tint(ti * 3 + 1) : tint(wi * 3 + 2) = t.Tint(ti * 3 + 2)
                                     End If
                                 End Sub)
        End Sub

        ''' <summary>視窗 → 格子（只存有覆蓋的格子）。</summary>
        Private Sub Store(win As Rectangle, cov As Single(), streak As Single(), tint As Single())
            ForCells(win, True, Sub(t, ti, wi)
                                    t.Cov(ti) = cov(wi)
                                    If streak IsNot Nothing Then t.Streak(ti) = streak(wi)
                                    If tint IsNot Nothing Then
                                        t.Tint(ti * 3) = tint(wi * 3) : t.Tint(ti * 3 + 1) = tint(wi * 3 + 1) : t.Tint(ti * 3 + 2) = tint(wi * 3 + 2)
                                    End If
                                End Sub, Function(wi) cov(wi) > 0)
        End Sub

        ''' <summary>
        ''' 走過 win 裡每個像素：visit(格子, 格子內索引, 視窗內索引)。create = True 時沒有的格子先建立
        ''' （needed 不是 Nothing 時，只有格子裡有像素 needed 才建立）。
        ''' </summary>
        Private Sub ForCells(win As Rectangle, create As Boolean, visit As Action(Of Tile, Integer, Integer), Optional needed As Func(Of Integer, Boolean) = Nothing)
            For ty = win.Top \ TileSize To (win.Bottom - 1) \ TileSize
                For tx = win.Left \ TileSize To (win.Right - 1) \ TileSize
                    Dim cell = Rectangle.Intersect(win, New Rectangle(tx * TileSize, ty * TileSize, TileSize, TileSize))
                    If cell.Width <= 0 OrElse cell.Height <= 0 Then Continue For
                    Dim t = TileOf(tx, ty, False)
                    If t Is Nothing Then
                        If Not create Then Continue For
                        If needed IsNot Nothing Then
                            Dim want = False
                            For y = cell.Top To cell.Bottom - 1
                                For x = cell.Left To cell.Right - 1
                                    If needed((y - win.Y) * win.Width + x - win.X) Then want = True : Exit For
                                Next
                                If want Then Exit For
                            Next
                            If Not want Then Continue For
                        End If
                        t = TileOf(tx, ty, True)
                    End If
                    For y = cell.Top To cell.Bottom - 1
                        Dim trow = (y - ty * TileSize) * TileSize - tx * TileSize
                        Dim wrow = (y - win.Y) * win.Width - win.X
                        For x = cell.Left To cell.Right - 1
                            visit(t, trow + x, wrow + x)
                        Next
                    Next
                Next
            Next
        End Sub

        ''' <summary>上好色的視窗 → 格子的像素，改到的格子做成新的圖。</summary>
        Private Sub WriteShaded(cv As Canvas, dirty As Rectangle, result As List(Of PreviewTile))
            For ty = dirty.Top \ TileSize To (dirty.Bottom - 1) \ TileSize
                For tx = dirty.Left \ TileSize To (dirty.Right - 1) \ TileSize
                    Dim tileRect = Rectangle.Intersect(New Rectangle(tx * TileSize, ty * TileSize, TileSize, TileSize), New Rectangle(0, 0, _w, _h))
                    Dim cell = Rectangle.Intersect(dirty, tileRect)
                    If cell.Width <= 0 OrElse cell.Height <= 0 Then Continue For
                    ' 暫時段也會畫到還沒有確定覆蓋的格子：沒有時建立（只為了放像素）
                    Dim t = TileOf(tx, ty, True)
                    Dim anyPixel = False
                    For y = cell.Top To cell.Bottom - 1
                        For x = cell.Left To cell.Right - 1
                            Dim ci = (y - cv.OY) * cv.W + x - cv.OX
                            Dim ti = ((y - ty * TileSize) * TileSize + x - tx * TileSize) * 4
                            Dim a = cv.A(ci)
                            If a <= 0.002F Then
                                t.Px(ti) = 0 : t.Px(ti + 1) = 0 : t.Px(ti + 2) = 0 : t.Px(ti + 3) = 0
                                Continue For
                            End If
                            If a > 1 Then a = 1
                            t.Px(ti) = ToByte(cv.B(ci) / a) : t.Px(ti + 1) = ToByte(cv.G(ci) / a)
                            t.Px(ti + 2) = ToByte(cv.R(ci) / a) : t.Px(ti + 3) = ToByte(a)
                        Next
                    Next
                    For k = 3 To t.Px.Length - 1 Step 4
                        If t.Px(k) <> 0 Then anyPixel = True : Exit For
                    Next
                    Dim tile As New PreviewTile With {.Bounds = tileRect}
                    If anyPixel Then
                        Dim bmp As New Bitmap(TileSize, TileSize, Imaging.PixelFormat.Format32bppArgb)
                        Perspective.WritePixels(bmp, t.Px)
                        If tileRect.Width < TileSize OrElse tileRect.Height < TileSize Then
                            Dim cut = bmp.Clone(New Rectangle(0, 0, tileRect.Width, tileRect.Height), Imaging.PixelFormat.Format32bppArgb)
                            bmp.Dispose()
                            bmp = cut
                        End If
                        tile.Bitmap = bmp
                    End If
                    result.Add(tile)
                Next
            Next
        End Sub
    End Class

    ''' <summary>
    ''' 讀畫布的筆（混色、塗抹、仿製、模糊、銳利化、加亮、加深、海綿、修復）的逐段筆印：
    ''' 畫布用開始畫時畫面上的樣子（快照），筆印依序蓋上去，筆尖帶著的顏料一路留著。
    ''' 這類筆會改變畫布，不能重畫最後一小段，所以只畫已確定的筆印（比筆尖慢一兩個點）。
    ''' 快照包含上面的圖層，和放開後（只讀下面）的結果可能有一點不同；放開時整筆重算。
    ''' </summary>
    Public NotInheritable Class SamplingSession
        Private Const TileSize As Integer = 128
        Private ReadOnly _w As Integer, _h As Integer
        Private ReadOnly _snapshot As Byte()
        Private _painter As SamplingPainter
        Private _sDone As Single = Single.NegativeInfinity

        ''' <param name="snapshot">畫面的樣子，w × h（BGRA，非預乘）。</param>
        Public Sub New(snapshot As Byte(), w As Integer, h As Integer)
            _snapshot = snapshot : _w = Math.Max(1, w) : _h = Math.Max(1, h)
        End Sub

        Public Shared Function Supports(op As DrawLayer) As Boolean
            Return op IsNot Nothing AndAlso op.Shape = DrawShape.Freehand AndAlso Global.PhotoEdit.DrawLayer.SamplesCanvas(op.Brush) AndAlso
                   op.Symmetry = SymmetryKind.None AndAlso Not op.Eraser
        End Function

        ''' <summary>筆畫更新了：只蓋新確定的筆印，回傳改到的格子（新的圖）。</summary>
        Public Function Update(op As DrawLayer, rawCount As Integer) As List(Of PreviewTile)
            Dim result As New List(Of PreviewTile)()
            Dim scale = CSng(_h)
            Dim figs = SamplingFigures(op, scale)
            If figs.Count = 0 Then Return result
            Dim stable = If(rawCount >= 3, 4 * (rawCount - 2), 0)
            If stable <= 0 Then Return result
            Dim pts = figs(0).Points
            Dim cum = StrokeSession.Cumulative(pts)
            Dim sStable = cum(Math.Min(stable, cum.Length) - 1)
            If sStable <= _sDone Then Return result
            If _painter Is Nothing Then
                Dim snap = _snapshot, w = _w, h = _h
                Dim compAt = Function(x As Integer, y As Integer) As (R As Single, G As Single, B As Single, A As Single)
                                 If x < 0 OrElse y < 0 OrElse x >= w OrElse y >= h Then Return (0, 0, 0, 0)
                                 Dim i = (y * w + x) * 4
                                 Dim a = snap(i + 3) / 255.0F
                                 Return (snap(i + 2) / 255.0F * a, snap(i + 1) / 255.0F * a, snap(i) / 255.0F * a, a)
                             End Function
                _painter = New SamplingPainter(op, scale, New Rectangle(0, 0, _w, _h), compAt)
            End If
            Dim radius = Math.Max(1.0F, CSng(Math.Max(1, op.StrokeWidth * scale)) / 2)
            Dim dirty = StrokeSession.Bounds(pts, cum, _sDone, sStable, CSng(Math.Ceiling(radius)) + 3)
            _painter.StampFigure(0, figs(0), New DabRange With {.SFrom = _sDone, .STo = sStable})
            _sDone = sStable
            dirty.Intersect(New Rectangle(0, 0, _w, _h))
            If dirty.Width <= 0 OrElse dirty.Height <= 0 Then Return result
            ' 改到的格子：被筆碰過的像素用畫布的新顏色，其他透明（看得到原本的畫面）
            Dim p = _painter
            For ty = dirty.Top \ TileSize To (dirty.Bottom - 1) \ TileSize
                For tx = dirty.Left \ TileSize To (dirty.Right - 1) \ TileSize
                    Dim r = Rectangle.Intersect(New Rectangle(tx * TileSize, ty * TileSize, TileSize, TileSize), New Rectangle(0, 0, _w, _h))
                    If r.Width <= 0 OrElse r.Height <= 0 Then Continue For
                    Dim px(r.Width * r.Height * 4 - 1) As Byte
                    Dim any = False
                    For y = r.Top To r.Bottom - 1
                        For x = r.Left To r.Right - 1
                            Dim i = y * _w + x
                            If p.Touched(i) <= 0 Then Continue For
                            Dim a = Clamp01(p.CA(i))
                            If a <= 0.002F Then Continue For
                            Dim k = ((y - r.Y) * r.Width + x - r.X) * 4
                            px(k) = ToByte(p.CB(i) / a) : px(k + 1) = ToByte(p.CG(i) / a) : px(k + 2) = ToByte(p.CR(i) / a) : px(k + 3) = ToByte(a)
                            any = True
                        Next
                    Next
                    Dim tile As New PreviewTile With {.Bounds = r}
                    If any Then
                        tile.Bitmap = New Bitmap(r.Width, r.Height, Imaging.PixelFormat.Format32bppArgb)
                        Perspective.WritePixels(tile.Bitmap, px)
                    End If
                    result.Add(tile)
                Next
            Next
            Return result
        End Function
    End Class
End Class
