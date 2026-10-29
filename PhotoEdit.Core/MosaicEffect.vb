Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Threading.Tasks

''' <summary>平滑馬賽克用的放大方法。</summary>
Public Enum MosaicSmoothMethod
    ''' <summary>雙三次：最柔和，像柔焦。</summary>
    Bicubic = 0
    ''' <summary>xBR：邊緣、斜線比較清楚（適合像素圖、文字少的畫面）。</summary>
    XBR = 1
    ''' <summary>HQx：介於兩者之間。</summary>
    HQx = 2
End Enum

''' <summary>
''' 馬賽克相關：打馬賽克、平滑馬賽克（把方塊變平滑；方塊裡原本的細節不會回來）、偵測方塊大小與起點、
''' JPEG 區塊雜訊（8×8 格線）的去除。像素都是 BGRA 位元組（w × h）。
''' </summary>
Public NotInheritable Class MosaicEffect
    Private Sub New()
    End Sub

    Private Shared Function Lum(px As Byte(), i As Integer) As Single
        Return 0.114F * px(i) + 0.587F * px(i + 1) + 0.299F * px(i + 2)
    End Function

    '=====================================================================
    ' 偵測方塊
    '=====================================================================

    ''' <summary>
    ''' 找馬賽克方塊的大小與起點：方塊裡相鄰像素幾乎一樣，差異都集中在格線上。
    ''' 對每個候選大小 s（2..64）與起點 o，算「格線上的平均差異 ÷ 其他位置的平均差異」（壓縮雜訊到處都有，用比值才不會被它蓋過）；
    ''' 真正的 s 比值最高，它的因數只有一半的格線是真的（比值低），倍數一樣高——取比值夠高裡最小的 s。橫、直各算一次。
    ''' Score：明顯程度 0..1（比值 r 換成 1 − 1/r）。
    ''' </summary>
    Public Shared Function Detect(px As Byte(), w As Integer, h As Integer, area As Rectangle) As (Size As Integer, OffsetX As Integer, OffsetY As Integer, Score As Double)
        area.Intersect(New Rectangle(0, 0, w, h))
        If area.Width < 6 OrElse area.Height < 6 Then Return (0, 0, 0, 0)
        Dim colDiff(area.Width - 1) As Double, rowDiff(area.Height - 1) As Double
        For y = area.Top To area.Bottom - 1
            For x = area.Left + 1 To area.Right - 1
                colDiff(x - area.Left) += Math.Abs(Lum(px, (y * w + x) * 4) - Lum(px, (y * w + x - 1) * 4))
            Next
        Next
        For y = area.Top + 1 To area.Bottom - 1
            For x = area.Left To area.Right - 1
                rowDiff(y - area.Top) += Math.Abs(Lum(px, (y * w + x) * 4) - Lum(px, ((y - 1) * w + x) * 4))
            Next
        Next
        Dim best = Function(diff As Double(), s As Integer) As (Score As Double, Offset As Integer)
                       Dim total = diff.Sum()
                       If total <= 0 Then Return (0, 0)
                       Dim top = 0.0, at = 0
                       For o = 0 To s - 1
                           Dim hit = 0.0, n = 0
                           For i = o To diff.Length - 1 Step s
                               hit += diff(i) : n += 1
                           Next
                           Dim offN = diff.Length - n
                           If n = 0 OrElse offN <= 0 Then Continue For
                           Dim ratio = (hit / n) / Math.Max(0.05, (total - hit) / offN)
                           If ratio > top Then top = ratio : at = o
                       Next
                       Return (top, at)
                   End Function
        Dim maxS = Math.Min(64, Math.Min(area.Width, area.Height) \ 2)
        Dim scores As New List(Of (S As Integer, Score As Double, Ox As Integer, Oy As Integer))()
        For s = 2 To maxS
            Dim bx = best(colDiff, s), by = best(rowDiff, s)
            scores.Add((s, (bx.Score + by.Score) / 2, bx.Offset, by.Offset))
        Next
        If scores.Count = 0 Then Return (0, 0, 0, 0)
        Dim maxScore = scores.Max(Function(t) t.Score)
        ' 比值夠高（接近最好的）裡面最小的：真正的方塊大小（倍數一樣高但比較大，因數比較低）
        Dim pick = scores.Where(Function(t) t.Score >= maxScore * 0.85).OrderBy(Function(t) t.S).First()
        ' 起點換回整張圖的座標（取 0..s-1）
        Dim ox = ((area.Left + pick.Ox) Mod pick.S + pick.S) Mod pick.S, oy = ((area.Top + pick.Oy) Mod pick.S + pick.S) Mod pick.S
        Return (pick.S, ox, oy, Math.Max(0, 1 - 1 / Math.Max(1, pick.Score)))
    End Function

    '=====================================================================
    ' 打馬賽克
    '=====================================================================

    ''' <summary>
    ''' 打馬賽克：格子大小 size（像素，可以有小數，預覽縮小時跟著縮），格線從 (originX, originY) 開始；
    ''' 每格取整格的平均色，只畫在 mask 蓋到的地方（透明度 = mask）。回傳一張 BGRA（其他地方透明）。
    ''' </summary>
    Public Shared Function Pixelate(px As Byte(), w As Integer, h As Integer, mask As Byte(), size As Double, originX As Double, originY As Double) As Byte()
        Dim result(w * h * 4 - 1) As Byte
        size = Math.Max(1.0, size)
        Dim b = MaskBounds(mask, w, h)
        If b.IsEmpty Then Return result
        Dim gx0 = CInt(Math.Floor((b.Left - originX) / size)), gx1 = CInt(Math.Floor((b.Right - 1 - originX) / size))
        Dim gy0 = CInt(Math.Floor((b.Top - originY) / size)), gy1 = CInt(Math.Floor((b.Bottom - 1 - originY) / size))
        Parallel.For(gy0, gy1 + 1, Sub(gy)
                                       Dim y0 = Math.Max(0, CInt(Math.Floor(originY + gy * size))), y1 = Math.Min(h, CInt(Math.Floor(originY + (gy + 1) * size)))
                                       For gx = gx0 To gx1
                                           Dim x0 = Math.Max(0, CInt(Math.Floor(originX + gx * size))), x1 = Math.Min(w, CInt(Math.Floor(originX + (gx + 1) * size)))
                                           If x1 <= x0 OrElse y1 <= y0 Then Continue For
                                           Dim sb = 0.0, sg = 0.0, sr = 0.0, sa = 0.0, n = 0
                                           For y = y0 To y1 - 1
                                               For x = x0 To x1 - 1
                                                   Dim i = (y * w + x) * 4
                                                   Dim a = px(i + 3) / 255.0
                                                   sb += px(i) * a : sg += px(i + 1) * a : sr += px(i + 2) * a : sa += a : n += 1
                                               Next
                                           Next
                                           If n = 0 Then Continue For
                                           Dim cb = If(sa > 0, sb / sa, 0), cg = If(sa > 0, sg / sa, 0), cr = If(sa > 0, sr / sa, 0), ca = sa / n
                                           For y = y0 To y1 - 1
                                               For x = x0 To x1 - 1
                                                   Dim k = y * w + x
                                                   If mask(k) = 0 Then Continue For
                                                   Dim i = k * 4
                                                   result(i) = CByte(Math.Round(cb)) : result(i + 1) = CByte(Math.Round(cg)) : result(i + 2) = CByte(Math.Round(cr))
                                                   result(i + 3) = CByte(Math.Round(ca * mask(k)))
                                               Next
                                           Next
                                       Next
                                   End Sub)
        Return result
    End Function

    Private Shared Function MaskBounds(mask As Byte(), w As Integer, h As Integer) As Rectangle
        Dim l = w, t = h, r = -1, b = -1
        For y = 0 To h - 1
            For x = 0 To w - 1
                If mask(y * w + x) = 0 Then Continue For
                If x < l Then l = x
                If x > r Then r = x
                If y < t Then t = y
                If y > b Then b = y
            Next
        Next
        If r < 0 Then Return Rectangle.Empty
        Return Rectangle.FromLTRB(l, t, r + 1, b + 1)
    End Function

    '=====================================================================
    ' 平滑馬賽克
    '=====================================================================

    ''' <summary>
    ''' 平滑馬賽克：每一格取中心的顏色縮成一張小圖（每格一個像素），用 method 放大回原本的大小，
    ''' 方塊感變成平滑的漸層。原本被方塊平均掉的細節不會回來——這不是「還原」。
    ''' 只畫在 mask 蓋到的地方；回傳一張 BGRA（其他地方透明）。
    ''' </summary>
    Public Shared Function Smooth(px As Byte(), w As Integer, h As Integer, mask As Byte(), size As Double, originX As Double, originY As Double,
                                  method As MosaicSmoothMethod) As Byte()
        Dim result(w * h * 4 - 1) As Byte
        size = Math.Max(1.0, size)
        Dim b = MaskBounds(mask, w, h)
        If b.IsEmpty Then Return result
        ' 多算一圈格子，邊上才平滑
        Dim gx0 = CInt(Math.Floor((b.Left - originX) / size)) - 1, gx1 = CInt(Math.Floor((b.Right - 1 - originX) / size)) + 1
        Dim gy0 = CInt(Math.Floor((b.Top - originY) / size)) - 1, gy1 = CInt(Math.Floor((b.Bottom - 1 - originY) / size)) + 1
        Dim cols = gx1 - gx0 + 1, rows = gy1 - gy0 + 1
        If cols < 1 OrElse rows < 1 Then Return result
        Dim cells(cols * rows - 1) As Integer
        For gy = 0 To rows - 1
            For gx = 0 To cols - 1
                ' 格子中心附近的平均（避開格線上的壓縮雜訊）
                Dim cx = originX + (gx0 + gx + 0.5) * size, cy = originY + (gy0 + gy + 0.5) * size
                Dim rr = Math.Max(0, CInt(size * 0.25))
                Dim sb = 0.0, sg = 0.0, sr = 0.0, sa = 0.0, n = 0
                For y = CInt(Math.Floor(cy)) - rr To CInt(Math.Floor(cy)) + rr
                    For x = CInt(Math.Floor(cx)) - rr To CInt(Math.Floor(cx)) + rr
                        Dim xx = Math.Max(0, Math.Min(w - 1, x)), yy = Math.Max(0, Math.Min(h - 1, y))
                        Dim i = (yy * w + xx) * 4
                        Dim a = px(i + 3) / 255.0
                        sb += px(i) * a : sg += px(i + 1) * a : sr += px(i + 2) * a : sa += a : n += 1
                    Next
                Next
                Dim ca = sa / Math.Max(1, n)
                Dim cb = If(sa > 0, sb / sa, 0), cg = If(sa > 0, sg / sa, 0), cr = If(sa > 0, sr / sa, 0)
                cells(gy * cols + gx) = (CInt(Math.Round(ca * 255)) << 24) Or (CInt(Math.Round(cr)) << 16) Or (CInt(Math.Round(cg)) << 8) Or CInt(Math.Round(cb))
            Next
        Next
        Using small As New Bitmap(cols, rows, PixelFormat.Format32bppArgb)
            Dim d = small.LockBits(New Rectangle(0, 0, cols, rows), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
            For y = 0 To rows - 1
                Runtime.InteropServices.Marshal.Copy(cells, y * cols, d.Scan0 + y * d.Stride, cols)
            Next
            small.UnlockBits(d)
            ' xBR／HQx 先放大整數倍，剩下的用雙三次補到剛好的大小
            Dim stage As Bitmap = small
            If method <> MosaicSmoothMethod.Bicubic AndAlso size >= 2 Then
                Dim algo = If(method = MosaicSmoothMethod.XBR, PixelScaleAlgorithm.XBR, PixelScaleAlgorithm.HQx)
                Dim fs = PixelScaler.Factors(algo)
                Dim f = fs.Where(Function(v) v <= size).DefaultIfEmpty(fs(0)).Max()
                stage = PixelScaler.Upscale(small, algo, f)
            End If
            Try
                Dim outX = originX + gx0 * size, outY = originY + gy0 * size
                Dim spanX = cols * size, spanY = rows * size
                Dim region = Rectangle.FromLTRB(Math.Max(0, CInt(Math.Floor(outX))), Math.Max(0, CInt(Math.Floor(outY))),
                                                Math.Min(w, CInt(Math.Ceiling(outX + spanX))), Math.Min(h, CInt(Math.Ceiling(outY + spanY))))
                If region.Width <= 0 OrElse region.Height <= 0 Then Return result
                Using big As New Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb)
                    Using g = Graphics.FromImage(big), ia As New ImageAttributes()
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality
                        g.CompositingMode = CompositingMode.SourceCopy
                        ia.SetWrapMode(WrapMode.TileFlipXY)
                        Dim dest As New RectangleF(CSng(outX - region.X), CSng(outY - region.Y), CSng(spanX), CSng(spanY))
                        g.DrawImage(stage, {dest.Location, New PointF(dest.Right, dest.Top), New PointF(dest.Left, dest.Bottom)},
                                    New RectangleF(0, 0, stage.Width, stage.Height), GraphicsUnit.Pixel, ia)
                    End Using
                    Dim bp = Perspective.ReadPixels(big)
                    Dim bw = region.Width
                    Parallel.For(region.Top, region.Bottom, Sub(y)
                                                                For x = region.Left To region.Right - 1
                                                                    Dim k = y * w + x
                                                                    If mask(k) = 0 Then Continue For
                                                                    Dim si = ((y - region.Top) * bw + (x - region.Left)) * 4, di = k * 4
                                                                    result(di) = bp(si) : result(di + 1) = bp(si + 1) : result(di + 2) = bp(si + 2)
                                                                    result(di + 3) = CByte(CInt(bp(si + 3)) * mask(k) \ 255) ' Byte×Byte 在 VB 是 Short，255×255 會溢位
                                                                Next
                                                            End Sub)
                End Using
            Finally
                If stage IsNot small Then stage.Dispose()
            End Try
        End Using
        Return result
    End Function

    '=====================================================================
    ' JPEG 區塊雜訊
    '=====================================================================

    ''' <summary>
    ''' JPEG 8×8 格線的起點（0..7）：沿格線的差異比格子裡大。照片轉正（EXIF 方向）或裁切過，格線不一定從 0 開始。
    ''' 回傳 (起點 X, 起點 Y, 明顯程度：格線上的平均差異 ÷ 其他位置的平均差異)。
    ''' </summary>
    Public Shared Function DetectJpegGrid(bmp As Bitmap) As (X As Integer, Y As Integer, Strength As Double)
        Dim w = bmp.Width, h = bmp.Height
        If w < 16 OrElse h < 16 Then Return (0, 0, 1)
        Dim px = Perspective.ReadPixels(bmp)
        Dim colDiff(7) As Double, rowDiff(7) As Double
        Dim stepY = Math.Max(1, h \ 400), stepX = Math.Max(1, w \ 400)
        For y = 0 To h - 1 Step stepY
            For x = 1 To w - 1
                colDiff(x Mod 8) += Math.Abs(Lum(px, (y * w + x) * 4) - Lum(px, (y * w + x - 1) * 4))
            Next
        Next
        For y = 1 To h - 1
            For x = 0 To w - 1 Step stepX
                rowDiff(y Mod 8) += Math.Abs(Lum(px, (y * w + x) * 4) - Lum(px, ((y - 1) * w + x) * 4))
            Next
        Next
        Dim gx = Array.IndexOf(colDiff, colDiff.Max()), gy = Array.IndexOf(rowDiff, rowDiff.Max())
        Dim others = (colDiff.Sum() - colDiff(gx)) / 7 + (rowDiff.Sum() - rowDiff(gy)) / 7
        Dim strength = (colDiff(gx) + rowDiff(gy)) / Math.Max(0.001, others)
        Return (gx, gy, strength)
    End Function

    ''' <summary>
    ''' 去 JPEG 區塊雜訊：在格線兩側各看 3 個像素，兩邊都平（|p1−p0|、|q1−q0| 小）而跨過格線有一個小台階（|p0−q0| 小於門檻）時，
    ''' 這個台階是壓縮造成的，不是真的邊緣：把兩側 6 個像素改成平順的斜坡。大的台階（真的邊緣）不動。
    ''' scale：bmp 相對原圖的比例（預覽縮小時格子跟著變小）；gridX、gridY：原圖上格線的起點。
    ''' </summary>
    Public Shared Sub Deblock(bmp As Bitmap, strength As Integer, gridX As Integer, gridY As Integer, Optional scale As Double = 1)
        If strength <= 0 Then Return
        Dim w = bmp.Width, h = bmp.Height
        Dim px = Perspective.ReadPixels(bmp)
        Dim k = strength / 100.0F
        Dim alpha = 4 + 46 * k, beta = 2 + 12 * k
        Dim cell = 8 * scale
        Dim taps = Math.Max(1, Math.Min(3, CInt(Math.Floor(cell / 2))))
        Dim lines = Function(len As Integer, start As Integer) As List(Of Integer)
                        Dim list As New List(Of Integer)()
                        Dim pos = start * scale
                        While pos < len
                            Dim p = CInt(Math.Round(pos))
                            If p >= taps AndAlso p <= len - taps Then list.Add(p)
                            pos += cell
                        End While
                        Return list
                    End Function
        ' 直的格線（處理左右）、橫的格線（處理上下）
        For Each vertical In {True, False}
            Dim cuts = If(vertical, lines(w, gridX), lines(h, gridY))
            Dim span = If(vertical, h, w)
            Dim copy = DirectCast(px.Clone(), Byte())
            Parallel.For(0, span, Sub(t)
                                      Dim idx = Function(c As Integer) As Integer
                                                    Return If(vertical, (t * w + c) * 4, (c * w + t) * 4)
                                                End Function
                                      For Each c In cuts
                                          Dim p0 = Lum(copy, idx(c - 1)), q0 = Lum(copy, idx(c))
                                          Dim stepAbs = Math.Abs(q0 - p0)
                                          If stepAbs >= alpha OrElse stepAbs < 0.5F Then Continue For
                                          Dim flatP = 0.0F, flatQ = 0.0F
                                          For i = 1 To taps - 1
                                              flatP = Math.Max(flatP, Math.Abs(Lum(copy, idx(c - 1 - i)) - p0))
                                              flatQ = Math.Max(flatQ, Math.Abs(Lum(copy, idx(c + i)) - q0))
                                          Next
                                          If flatP > beta OrElse flatQ > beta Then Continue For
                                          ' 兩側最外面的像素之間拉一條直線，裡面的像素往直線靠（強度 k）
                                          Dim a0 = idx(c - taps), a1 = idx(c + taps - 1)
                                          For ch = 0 To 2
                                              Dim left = CSng(copy(a0 + ch)), right = CSng(copy(a1 + ch))
                                              For i = -taps To taps - 1
                                                  Dim pos = (i + taps) / CSng(2 * taps - 1)
                                                  Dim target = left + (right - left) * pos
                                                  Dim j = idx(c + i) + ch
                                                  px(j) = CByte(Math.Max(0, Math.Min(255, Math.Round(copy(j) + (target - copy(j)) * k))))
                                              Next
                                          Next
                                      Next
                                  End Sub)
        Next
        Perspective.WritePixels(bmp, px)
    End Sub
End Class
