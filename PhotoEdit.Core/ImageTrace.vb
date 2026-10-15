Imports PhotoEdit
Imports System.Drawing

''' <summary>影像描圖的模式。</summary>
Public Enum TraceMode
    ''' <summary>黑白線稿：暗的地方變成黑色的向量圖形。</summary>
    LineArt = 0
    ''' <summary>彩色：照片減成幾種顏色，每種顏色一個向量圖形。</summary>
    Color = 1
End Enum

''' <summary>描圖的選項。</summary>
Public NotInheritable Class TraceOptions
    Public Property Mode As TraceMode = TraceMode.Color
    ''' <summary>顏色數（彩色模式，2..24）。</summary>
    Public Property Colors As Integer = 8
    ''' <summary>細節（1..10）：越高越貼近照片、節點越多。</summary>
    Public Property Detail As Integer = 6
    ''' <summary>去雜點：小於這麼多像素（描圖解析度）的小塊併掉。</summary>
    Public Property Speckle As Integer = 12
    ''' <summary>去掉背景：彩色時拿掉面積最大的顏色，黑白時本來就只有黑色。</summary>
    Public Property RemoveBackground As Boolean
    ''' <summary>描圖解析度（長邊的像素數）。</summary>
    Public Property Resolution As Integer = 800
End Class

''' <summary>
''' 影像描圖（同 CorelDRAW 的 PowerTRACE、Illustrator 的影像描圖）：照片變成向量圖形。
''' 先縮小、減色（黑白用 Otsu 門檻；彩色用 k-means），去雜點後每種顏色描出外框（含洞），擬合成鋼筆路徑。
''' 面積大的顏色在下面；第一層（最大的）鋪滿整張，其餘往外長一格蓋住接縫，不會有白縫。
''' 座標以照片高度為 1。
''' </summary>
Public NotInheritable Class ImageTrace
    Private Sub New()
    End Sub

    Public Shared Function Trace(source As Bitmap, opt As TraceOptions) As List(Of DrawLayer)
        Dim result As New List(Of DrawLayer)()
        Dim sc = Math.Min(1.0, opt.Resolution / Math.Max(source.Width, source.Height))
        Dim w = Math.Max(2, CInt(Math.Round(source.Width * sc))), h = Math.Max(2, CInt(Math.Round(source.Height * sc)))
        Dim px As Byte()
        Using small As New Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(small)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.Clear(Color.White)
                g.DrawImage(source, 0, 0, w, h)
            End Using
            px = Perspective.ReadPixels(small)
        End Using
        Dim n = w * h
        Dim labels(n - 1) As Integer
        Dim palette As List(Of Color)
        If opt.Mode = TraceMode.LineArt Then
            Dim lum(n - 1) As Integer
            For i = 0 To n - 1
                lum(i) = (px(i * 4 + 2) * 299 + px(i * 4 + 1) * 587 + px(i * 4) * 114) \ 1000
            Next
            Dim t = Otsu(lum)
            For i = 0 To n - 1
                labels(i) = If(lum(i) <= t, 1, 0)
            Next
            palette = New List(Of Color) From {Color.White, Color.Black}
        Else
            palette = KMeans(px, n, Math.Max(2, Math.Min(24, opt.Colors)), labels)
        End If
        ModeFilter(labels, w, h) ' 去掉孤立的單點
        RemoveSpeckles(labels, w, h, Math.Max(0, opt.Speckle))
        ' 各顏色的面積，大的在下面
        Dim area(palette.Count - 1) As Integer
        For Each l In labels
            area(l) += 1
        Next
        Dim order = Enumerable.Range(0, palette.Count).Where(Function(k) area(k) > 0).OrderByDescending(Function(k) area(k)).ToList()
        If opt.Mode = TraceMode.LineArt Then order = {0, 1}.Where(Function(k) area(k) > 0).ToList() ' 白色在下、黑色在上
        Dim first = True
        Dim tol = 0.35 + (10 - Math.Max(1, Math.Min(10, opt.Detail))) * 0.22
        Dim s = CSng(h)
        For Each k In order
            Dim skip = (opt.Mode = TraceMode.LineArt AndAlso k = 0 AndAlso opt.RemoveBackground) OrElse (opt.Mode = TraceMode.Color AndAlso first AndAlso opt.RemoveBackground)
            Dim isBase = first AndAlso Not opt.RemoveBackground
            first = False
            If skip Then Continue For
            Dim mask(n - 1) As Boolean
            If isBase Then
                For i = 0 To n - 1 : mask(i) = True : Next ' 最下面那層鋪滿：上面的顏色有縫也不會露白
            Else
                For i = 0 To n - 1 : mask(i) = labels(i) = k : Next
                mask = Grow(mask, w, h)
            End If
            Dim rings = SmartFill.Trace(mask, w, h).
                Select(Function(r) DrawGeometry.Simplify(r, tol, True).Select(Function(q) New PointF(q.X / s, q.Y / s)).ToArray()).
                Where(Function(r) r.Length >= 3 AndAlso Math.Abs(PathBoolean.SignedArea(r)) > 1.5 / (s * s)).ToList()
            If rings.Count = 0 Then Continue For
            Dim style As New DrawLayer With {.Filled = True, .Stroked = False, .FillColorArgb = palette(k).ToArgb(), .Brush = BrushKind.HardRound}
            Dim layer = PathBoolean.MakeLayer(style, rings)
            If layer Is Nothing Then Continue For
            layer.Name = Lang.F("描圖 #{0:X2}{1:X2}{2:X2}", palette(k).R, palette(k).G, palette(k).B)
            result.Add(layer)
        Next
        Return result
    End Function

    ''' <summary>Otsu 門檻（亮度 0..255）。</summary>
    Private Shared Function Otsu(lum As Integer()) As Integer
        Dim hist(255) As Long
        For Each v In lum : hist(v) += 1 : Next
        Dim total = lum.Length, sum = 0.0
        For i = 0 To 255 : sum += i * hist(i) : Next
        Dim sumB = 0.0, wB = 0L, best = 0.0, t = 128
        For i = 0 To 255
            wB += hist(i)
            If wB = 0 Then Continue For
            Dim wF = total - wB
            If wF = 0 Then Exit For
            sumB += i * hist(i)
            Dim mB = sumB / wB, mF = (sum - sumB) / wF
            Dim between = wB * wF * (mB - mF) ^ 2
            If between > best Then best = between : t = i
        Next
        Return t
    End Function

    ''' <summary>k-means 減色：回傳顏色表，labels 填每個像素的顏色編號。</summary>
    Private Shared Function KMeans(px As Byte(), n As Integer, k As Integer, labels As Integer()) As List(Of Color)
        ' 初始：最遠點優先（第一個取平均色，之後每次取離現有顏色最遠的像素），面積小但顏色特別的地方也有自己的顏色
        Dim sample = Enumerable.Range(0, n).Where(Function(i) i Mod 5 = 0).ToArray()
        Dim cr(k - 1) As Double, cg(k - 1) As Double, cb(k - 1) As Double
        For Each i In sample
            cr(0) += px(i * 4 + 2) : cg(0) += px(i * 4 + 1) : cb(0) += px(i * 4)
        Next
        cr(0) /= sample.Length : cg(0) /= sample.Length : cb(0) /= sample.Length
        Dim minD(sample.Length - 1) As Double
        For c = 1 To k - 1
            Dim far = sample(0), farD = -1.0
            For si = 0 To sample.Length - 1
                Dim i = sample(si)
                Dim d = (px(i * 4 + 2) - cr(c - 1)) ^ 2 * 0.3 + (px(i * 4 + 1) - cg(c - 1)) ^ 2 * 0.59 + (px(i * 4) - cb(c - 1)) ^ 2 * 0.11
                If c = 1 OrElse d < minD(si) Then minD(si) = d
                If minD(si) > farD Then farD = minD(si) : far = i
            Next
            cr(c) = px(far * 4 + 2) : cg(c) = px(far * 4 + 1) : cb(c) = px(far * 4)
        Next
        For iter = 1 To 10
            Dim sr(k - 1) As Double, sg(k - 1) As Double, sbb(k - 1) As Double, cnt(k - 1) As Integer
            For i = 0 To n - 1
                Dim r = px(i * 4 + 2), g = px(i * 4 + 1), b = px(i * 4)
                Dim best = 0, bd = Double.MaxValue
                For c = 0 To k - 1
                    Dim d = (r - cr(c)) ^ 2 * 0.3 + (g - cg(c)) ^ 2 * 0.59 + (b - cb(c)) ^ 2 * 0.11
                    If d < bd Then bd = d : best = c
                Next
                labels(i) = best
                sr(best) += r : sg(best) += g : sbb(best) += b : cnt(best) += 1
            Next
            For c = 0 To k - 1
                If cnt(c) > 0 Then cr(c) = sr(c) / cnt(c) : cg(c) = sg(c) / cnt(c) : cb(c) = sbb(c) / cnt(c)
            Next
        Next
        Return Enumerable.Range(0, k).Select(Function(c) Color.FromArgb(CInt(Math.Round(cr(c))), CInt(Math.Round(cg(c))), CInt(Math.Round(cb(c))))).ToList()
    End Function

    ''' <summary>3×3 眾數濾波：每個點換成周圍最多的顏色（去掉單點雜訊）。</summary>
    Private Shared Sub ModeFilter(labels As Integer(), w As Integer, h As Integer)
        Dim src = DirectCast(labels.Clone(), Integer())
        For y = 1 To h - 2
            For x = 1 To w - 2
                Dim i = y * w + x
                Dim me1 = src(i), same = 0
                For dy = -1 To 1
                    For dx = -1 To 1
                        If src(i + dy * w + dx) = me1 Then same += 1
                    Next
                Next
                If same >= 3 Then Continue For
                ' 換成上下左右最多的
                Dim cands = {src(i - 1), src(i + 1), src(i - w), src(i + w)}
                labels(i) = cands.GroupBy(Function(v) v).OrderByDescending(Function(gg) gg.Count()).First().Key
            Next
        Next
    End Sub

    ''' <summary>去雜點：小於 minSize 像素的連通塊，換成旁邊的顏色。</summary>
    Private Shared Sub RemoveSpeckles(labels As Integer(), w As Integer, h As Integer, minSize As Integer)
        If minSize <= 1 Then Return
        Dim seen(labels.Length - 1) As Boolean
        Dim stack As New Stack(Of Integer)()
        Dim comp As New List(Of Integer)()
        For start = 0 To labels.Length - 1
            If seen(start) Then Continue For
            Dim l = labels(start)
            comp.Clear()
            stack.Push(start) : seen(start) = True
            Dim neighbor = -1
            While stack.Count > 0
                Dim i = stack.Pop()
                comp.Add(i)
                Dim x = i Mod w, y = i \ w
                For Each j In {If(x > 0, i - 1, -1), If(x < w - 1, i + 1, -1), If(y > 0, i - w, -1), If(y < h - 1, i + w, -1)}
                    If j < 0 Then Continue For
                    If labels(j) = l Then
                        If Not seen(j) Then seen(j) = True : stack.Push(j)
                    ElseIf neighbor < 0 Then
                        neighbor = labels(j)
                    End If
                Next
            End While
            If comp.Count < minSize AndAlso neighbor >= 0 Then
                For Each i In comp : labels(i) = neighbor : Next
            End If
        Next
    End Sub

    ''' <summary>往外長一格（八方向）：蓋住相鄰顏色之間的接縫。</summary>
    Private Shared Function Grow(mask As Boolean(), w As Integer, h As Integer) As Boolean()
        Dim r = DirectCast(mask.Clone(), Boolean())
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * w + x
                If mask(i) Then Continue For
                For dy = -1 To 1
                    Dim yy = y + dy
                    If yy < 0 OrElse yy >= h Then Continue For
                    For dx = -1 To 1
                        Dim xx = x + dx
                        If xx >= 0 AndAlso xx < w AndAlso mask(yy * w + xx) Then r(i) = True : GoTo NextPixel
                    Next
                Next
NextPixel:
            Next
        Next
        Return r
    End Function
End Class
