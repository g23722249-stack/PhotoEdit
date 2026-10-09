Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>
''' 液化：依筆觸逐點（每點一個「筆印」）變形，後面的筆印看到前面變形後的結果（和 Photoshop 液化一樣會累積）。
''' 座標以原圖比例記錄，所以預覽縮圖與全尺寸匯出的變形一致。
''' </summary>
Public NotInheritable Class Liquify
    Private Sub New()
    End Sub

    ''' <summary>單點（沒有拖曳）的膨脹、縮攏、旋轉要重複幾次：相當於按住不動一下子。</summary>
    Private Const ClickRepeats As Integer = 8

    ''' <summary>在 bmp（32bpp ARGB）上依序套用全部筆觸；回傳新圖。</summary>
    Public Shared Function Apply(source As Bitmap, strokes As IEnumerable(Of LiquifyStroke)) As Bitmap
        Dim result = source.Clone(New Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb)
        Dim w = result.Width, h = result.Height
        Dim data = result.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
        Try
            Dim px(data.Stride * h - 1) As Byte
            Marshal.Copy(data.Scan0, px, 0, px.Length)
            Dim longSide = Math.Max(w, h)
            For Each s In strokes
                Dim r = s.Radius * longSide
                If r < 1.5 Then Continue For
                Dim pts = s.Points().Select(Function(p) New PointF(p.X * w, p.Y * h)).ToList()
                If pts.Count = 0 Then Continue For
                Dim strength = Math.Max(1, Math.Min(100, s.Strength)) / 100.0
                For Each dab In Dabs(pts, r, s.Mode)
                    ApplyDab(px, data.Stride, w, h, dab.Center, dab.Move, r, strength, s.Mode)
                Next
            Next
            Marshal.Copy(px, 0, data.Scan0, px.Length)
        Finally
            result.UnlockBits(data)
        End Try
        Return result
    End Function

    ''' <summary>沿路徑每隔半徑的 1/6 放一個筆印；推移時附上這一段的移動量。</summary>
    Private Shared Iterator Function Dabs(pts As List(Of PointF), r As Double, mode As LiquifyMode) As IEnumerable(Of (Center As PointF, Move As PointF))
        If pts.Count = 1 Then
            If mode = LiquifyMode.Push Then Return ' 推移要有拖曳
            For i = 1 To ClickRepeats
                Yield (pts(0), PointF.Empty)
            Next
            Return
        End If
        Dim spacing = Math.Max(1.0, r / 6)
        Dim prev = pts(0)
        For i = 1 To pts.Count - 1
            Dim a = pts(i - 1), b = pts(i)
            Dim dx = b.X - a.X, dy = b.Y - a.Y
            Dim len = Math.Sqrt(dx * dx + dy * dy)
            Dim n = Math.Max(1, CInt(Math.Ceiling(len / spacing)))
            For k = 1 To n
                Dim c As New PointF(CSng(a.X + dx * k / n), CSng(a.Y + dy * k / n))
                Yield (c, New PointF(c.X - prev.X, c.Y - prev.Y))
                prev = c
            Next
        Next
    End Function

    Private Shared Sub ApplyDab(px As Byte(), stride As Integer, w As Integer, h As Integer, c As PointF, move As PointF,
                                r As Double, strength As Double, mode As LiquifyMode)
        Dim x0 = Math.Max(0, CInt(Math.Floor(c.X - r))), x1 = Math.Min(w - 1, CInt(Math.Ceiling(c.X + r)))
        Dim y0 = Math.Max(0, CInt(Math.Floor(c.Y - r))), y1 = Math.Min(h - 1, CInt(Math.Ceiling(c.Y + r)))
        If x1 < x0 OrElse y1 < y0 Then Return
        ' 取樣來源可能在筆印外（推移、縮攏），所以複製整張的參考：只複製會讀到的範圍
        Dim reach = CInt(Math.Ceiling(r * 0.5 + Math.Sqrt(move.X * move.X + move.Y * move.Y))) + 2
        Dim sx0 = Math.Max(0, x0 - reach), sx1 = Math.Min(w - 1, x1 + reach)
        Dim sy0 = Math.Max(0, y0 - reach), sy1 = Math.Min(h - 1, y1 + reach)
        Dim sw = sx1 - sx0 + 1, sh = sy1 - sy0 + 1
        Dim src(sw * sh * 4 - 1) As Byte
        For y = 0 To sh - 1
            Buffer.BlockCopy(px, (sy0 + y) * stride + sx0 * 4, src, y * sw * 4, sw * 4)
        Next
        Dim r2 = r * r
        Dim amount = strength * 0.1 ' 每個筆印的變形量
        For y = y0 To y1
            For x = x0 To x1
                Dim dx = x - c.X, dy = y - c.Y
                Dim d2 = dx * dx + dy * dy
                If d2 >= r2 Then Continue For
                Dim t = 1 - d2 / r2
                Dim falloff = t * t
                Dim fx As Double, fy As Double
                Select Case mode
                    Case LiquifyMode.Push
                        fx = x - move.X * falloff * strength * 1.4
                        fy = y - move.Y * falloff * strength * 1.4
                    Case LiquifyMode.Bloat
                        Dim s = 1 - amount * falloff
                        fx = c.X + dx * s : fy = c.Y + dy * s
                    Case LiquifyMode.Pucker
                        Dim s = 1 + amount * falloff
                        fx = c.X + dx * s : fy = c.Y + dy * s
                    Case Else
                        Dim ang = amount * 1.5 * falloff * If(mode = LiquifyMode.TwirlClockwise, -1, 1)
                        Dim cs = Math.Cos(ang), sn = Math.Sin(ang)
                        fx = c.X + dx * cs - dy * sn : fy = c.Y + dx * sn + dy * cs
                End Select
                Sample(src, sw, sh, fx - sx0, fy - sy0, px, y * stride + x * 4)
            Next
        Next
    End Sub

    ''' <summary>雙線性取樣（夾在來源範圍內）寫到 dst(di..di+3)。</summary>
    Private Shared Sub Sample(src As Byte(), sw As Integer, sh As Integer, fx As Double, fy As Double, dst As Byte(), di As Integer)
        fx = Math.Max(0, Math.Min(sw - 1.001, fx))
        fy = Math.Max(0, Math.Min(sh - 1.001, fy))
        Dim ix = CInt(Math.Floor(fx)), iy = CInt(Math.Floor(fy))
        Dim ax = fx - ix, ay = fy - iy
        Dim ix1 = Math.Min(sw - 1, ix + 1), iy1 = Math.Min(sh - 1, iy + 1)
        Dim i00 = (iy * sw + ix) * 4, i10 = (iy * sw + ix1) * 4, i01 = (iy1 * sw + ix) * 4, i11 = (iy1 * sw + ix1) * 4
        For ch = 0 To 3
            Dim v = (src(i00 + ch) * (1 - ax) + src(i10 + ch) * ax) * (1 - ay) + (src(i01 + ch) * (1 - ax) + src(i11 + ch) * ax) * ay
            dst(di + ch) = CByte(Math.Max(0, Math.Min(255, Math.Round(v))))
        Next
    End Sub
End Class
