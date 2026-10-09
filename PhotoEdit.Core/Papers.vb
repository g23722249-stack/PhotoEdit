Imports System.Drawing
Imports System.Drawing.Imaging

''' <summary>紙張與材質（數值存進編輯檔，不可更動；新的往後加）。</summary>
Public Enum PaperKind
    Sketch = 0
    WatercolorFine = 1
    WatercolorRough = 2
    Canvas = 3
    Pastel = 4
    Laid = 5
    Rice = 6
    Kraft = 7
    Linen = 8
    Burlap = 9
    Crumpled = 10
    Sandpaper = 11
    Concrete = 12
    Brick = 13
    Wood = 14
    Leather = 15
    Custom = 16
End Enum

''' <summary>表面紋理作用的範圍。</summary>
Public Enum SurfaceTarget
    ''' <summary>整張照片（含文字、貼圖、繪圖）。</summary>
    Photo = 0
    ''' <summary>只有繪圖圖層（照片本身不變）。</summary>
    Drawings = 1
End Enum

''' <summary>
''' 文件的紙張（類似 Corel Painter 的 Paper）：一張高度圖，凸起的地方 1、凹下 0。
''' 用在三個地方：筆刷吃紙紋（輕畫只碰到凸起）、水彩顏料沉積在凹處、表面紋理（把紙紋壓印成光影）；
''' 藝術風格的紙紋也用它。紋路以照片高度為單位（大小 100% 時一個紋路單位 = 照片高度的 1/1000），
''' 預覽與匯出看起來一樣。
''' </summary>
Public Class PaperSettings
    Public Property Kind As PaperKind
    ''' <summary>紋路大小 %（25..400）。</summary>
    Public Property Scale As Integer = 100
    ''' <summary>紋路深淺 0..100（50 為標準）。</summary>
    Public Property Contrast As Integer = 50
    ''' <summary>旋轉角度（度）。</summary>
    Public Property Rotation As Integer
    ''' <summary>凹凸反過來。</summary>
    Public Property Invert As Boolean
    ''' <summary>自訂紙紋的圖片（完整路徑；灰階，亮 = 凸）。</summary>
    Public Property CustomPath As String
    ''' <summary>表面紋理強度 0..100；0 為不壓印。</summary>
    Public Property SurfaceStrength As Integer
    Public Property SurfaceTarget As SurfaceTarget
    ''' <summary>光源方向（度，0 = 從右、90 = 從上；預設 135 = 左上）。</summary>
    Public Property LightAngle As Integer = 135

    Public Function Clone() As PaperSettings
        Return DirectCast(MemberwiseClone(), PaperSettings)
    End Function

    ''' <summary>影響紙紋高度的部分（快取用）。</summary>
    Friend Function HeightKey() As String
        Return $"{CInt(Kind)}|{Scale}|{Contrast}|{Rotation}|{Invert}|{CustomPath}"
    End Function
End Class

Public NotInheritable Class Papers
    Private Sub New()
    End Sub

    Public Shared ReadOnly Names As String() = {
        "素描紙", "水彩紙（細紋）", "水彩紙（粗紋）", "畫布", "粉彩紙", "條紋紙", "宣紙", "牛皮紙",
        "亞麻布", "麻布", "皺紋紙", "砂紙", "混凝土", "磚牆", "木板", "皮革", "自訂"}

    ''' <summary>每種紙的底色（新影像用紙張當底時的顏色）。</summary>
    Public Shared ReadOnly BaseColors As Color() = {
        Color.FromArgb(250, 249, 245), Color.FromArgb(250, 248, 242), Color.FromArgb(248, 246, 238), Color.FromArgb(236, 230, 215),
        Color.FromArgb(232, 228, 220), Color.FromArgb(246, 242, 230), Color.FromArgb(244, 239, 226), Color.FromArgb(196, 160, 118),
        Color.FromArgb(230, 222, 204), Color.FromArgb(196, 170, 128), Color.FromArgb(244, 242, 236), Color.FromArgb(150, 140, 126),
        Color.FromArgb(178, 176, 170), Color.FromArgb(170, 88, 64), Color.FromArgb(186, 140, 96), Color.FromArgb(120, 76, 50),
        Color.FromArgb(240, 240, 240)}

    '=====================================================================
    ' 高度圖
    '=====================================================================

    Private Shared ReadOnly _cache As New Dictionary(Of String, Single())()
    Private Shared ReadOnly _order As New LinkedList(Of String)()
    Private Shared ReadOnly _sync As New Object()

    ''' <summary>
    ''' 整張影像的紙紋高度（w × h，0..1，結果快取，呼叫端不可修改）。
    ''' photoH 是照片高度的像素數（紋路大小以它為準；繪圖圖層算圖時就是影像高度）。
    ''' </summary>
    Public Shared Function HeightMap(p As PaperSettings, w As Integer, h As Integer) As Single()
        Dim key = p.HeightKey() & $"|{w}x{h}"
        SyncLock _sync
            Dim hit As Single() = Nothing
            If _cache.TryGetValue(key, hit) Then
                _order.Remove(key) : _order.AddFirst(key)
                Return hit
            End If
        End SyncLock
        Dim map = Compute(p, w, h)
        SyncLock _sync
            _cache(key) = map
            _order.AddFirst(key)
            While _order.Count > 4
                _cache.Remove(_order.Last.Value)
                _order.RemoveLast()
            End While
        End SyncLock
        Return map
    End Function

    Private Shared Function Compute(p As PaperSettings, w As Integer, h As Integer) As Single()
        Dim map(w * h - 1) As Single
        ' 一個紋路單位 = 照片高度的 1/1000 × 大小
        Dim unit = h / 1000.0 * Math.Max(25, Math.Min(400, p.Scale)) / 100.0
        Dim a = p.Rotation * Math.PI / 180
        Dim ca = Math.Cos(a), sa = Math.Sin(a)
        Dim custom As Single() = Nothing, cw = 0, ch = 0
        If p.Kind = PaperKind.Custom Then custom = LoadCustom(p.CustomPath, cw, ch)
        Dim k = 0.4F + Math.Max(0, Math.Min(100, p.Contrast)) / 100.0F * 1.2F
        System.Threading.Tasks.Parallel.For(0, h,
            Sub(y)
                For x = 0 To w - 1
                    Dim u = CSng((x * ca + y * sa) / unit), v = CSng((-x * sa + y * ca) / unit)
                    Dim value As Single
                    If custom IsNot Nothing Then
                        Dim cx = CInt(Math.Floor(u)) Mod cw, cy = CInt(Math.Floor(v)) Mod ch
                        If cx < 0 Then cx += cw
                        If cy < 0 Then cy += ch
                        value = custom(cy * cw + cx)
                    Else
                        value = Sample(If(p.Kind = PaperKind.Custom, PaperKind.Sketch, p.Kind), u, v)
                    End If
                    value = 0.5F + (value - 0.5F) * k
                    If p.Invert Then value = 1 - value
                    map(y * w + x) = Clamp01(value)
                Next
            End Sub)
        Return map
    End Function

    ''' <summary>單一點的紙紋高度（u、v 為紋路單位）。</summary>
    Public Shared Function Sample(kind As PaperKind, u As Single, v As Single) As Single
        Select Case kind
            Case PaperKind.Sketch
                Return Fbm(u / 1.6F, v / 1.6F, 1) * 0.6F + Hash(CInt(Math.Floor(u)), CInt(Math.Floor(v)), 2) * 0.4F
            Case PaperKind.WatercolorFine
                Return Fbm(u / 7, v / 7, 3) * 0.6F + Fbm(u / 2.2F, v / 2.2F, 4) * 0.4F
            Case PaperKind.WatercolorRough
                ' 大顆粒圓凸：細胞距離做成一顆顆凸起，再加起伏
                Dim c = Worley(u / 9, v / 9, 5)
                Return Clamp01(1 - c.F1 * 1.25F) * 0.6F + Fbm(u / 4, v / 4, 6) * 0.4F
            Case PaperKind.Canvas
                Return Weave(u, v, 4.2F, 0.18F, 7)
            Case PaperKind.Pastel
                ' 粉彩紙的蜂巢紋
                Dim c = Worley(u / 5.5F, v / 5.5F, 8, hexagonal:=True)
                Return Clamp01(c.F2 - c.F1) * 0.75F + Fbm(u / 2, v / 2, 9) * 0.25F
            Case PaperKind.Laid
                ' 細的水平簾紋＋稀疏的直鏈紋
                Dim lines = 0.5F + 0.5F * CSng(Math.Sin(v * Math.PI * 2 / 1.8F))
                Dim chain = Math.Max(0, 1 - Math.Abs((u Mod 28) - 14) / 0.9F)
                Return Clamp01(lines * 0.55F + chain * 0.3F + Fbm(u / 3, v / 3, 10) * 0.3F)
            Case PaperKind.Rice
                Return Fibers(u, v, 11) * 0.55F + Fbm(u / 6, v / 6, 12) * 0.45F
            Case PaperKind.Kraft
                Dim speck = If(Hash(CInt(Math.Floor(u / 1.5F)), CInt(Math.Floor(v / 1.5F)), 13) > 0.97F, 0.0F, 1.0F)
                Return Clamp01((Fbm(u / 3, v / 3, 14) * 0.5F + Fibers(u, v, 15) * 0.5F) * (0.7F + 0.3F * speck))
            Case PaperKind.Linen
                Return Weave(u, v, 1.9F, 0.35F, 16)
            Case PaperKind.Burlap
                Return Weave(u, v, 7.5F, 0.3F, 17) * (0.75F + 0.25F * Fbm(u / 20, v / 20, 18))
            Case PaperKind.Crumpled
                ' 皺摺：細胞邊界是摺痕
                Dim c = Worley(u / 40, v / 40, 19)
                Dim crease = Clamp01((c.F2 - c.F1) * 3)
                Return crease * 0.6F + Fbm(u / 12, v / 12, 20) * 0.4F
            Case PaperKind.Sandpaper
                Dim g = Hash(CInt(Math.Floor(u / 0.8F)), CInt(Math.Floor(v / 0.8F)), 21)
                Return Clamp01(g * g * 1.2F) * 0.7F + Fbm(u / 2, v / 2, 22) * 0.3F
            Case PaperKind.Concrete
                Dim pit = Worley(u / 6, v / 6, 23).F1
                Dim holes = If(pit < 0.12F AndAlso Hash(CInt(Math.Floor(u / 6)), CInt(Math.Floor(v / 6)), 24) > 0.6F, 0.15F, 1.0F)
                Return Clamp01(Fbm(u / 10, v / 10, 25) * 0.6F + Fbm(u / 2, v / 2, 26) * 0.4F) * holes
            Case PaperKind.Brick
                Dim bh = 22.0F, bw = 50.0F, mortar = 2.2F
                Dim row = CInt(Math.Floor(v / bh))
                Dim bu = u + If(row Mod 2 = 0, 0, bw / 2)
                Dim fx = bu - CSng(Math.Floor(bu / bw)) * bw, fy = v - row * bh
                Dim edge = Math.Min(Math.Min(fx, bw - fx), Math.Min(fy, bh - fy))
                Dim body = 0.7F + 0.3F * Fbm(u / 4, v / 4, 27)
                Return If(edge < mortar, 0.15F + 0.1F * Fbm(u, v, 28), body * SmoothStep(mortar, mortar + 1.5F, edge))
            Case PaperKind.Wood
                ' 木紋：沿 u 拉長的年輪線
                Dim warp = Fbm(u / 60, v / 12, 29) * 9
                Dim rings = 0.5F + 0.5F * CSng(Math.Sin((v + warp) * Math.PI * 2 / 5))
                Return Clamp01(rings * 0.6F + Fbm(u / 30, v / 1.5F, 30) * 0.4F)
            Case PaperKind.Leather
                Dim c = Worley(u / 4, v / 4, 31)
                Return Clamp01(c.F1 * 1.6F) * 0.7F + Fbm(u / 1.5F, v / 1.5F, 32) * 0.3F
            Case Else
                Return 0.5F
        End Select
    End Function

    ''' <summary>平紋織布：經線、緯線一上一下交錯。</summary>
    Private Shared Function Weave(u As Single, v As Single, period As Single, gap As Single, seed As Integer) As Single
        Dim cu = CInt(Math.Floor(u / period)), cv = CInt(Math.Floor(v / period))
        Dim fu = u / period - cu, fv = v / period - cv
        ' 線的截面（圓弧），線與線之間有縫
        Dim warp = Thread(fu, gap), weft = Thread(fv, gap)
        Dim over = (cu + cv) Mod 2 = 0
        Dim hgt = If(over, Math.Max(warp * (0.6F + 0.4F * Thread(fv, 0)), weft * 0.6F), Math.Max(weft * (0.6F + 0.4F * Thread(fu, 0)), warp * 0.6F))
        Return Clamp01(hgt * (0.85F + 0.15F * Hash(cu, cv, seed)) + (Fbm(u / 3, v / 3, seed + 1) - 0.5F) * 0.15F)
    End Function

    Private Shared Function Thread(f As Single, gap As Single) As Single
        Dim d = Math.Abs(f - 0.5F) * 2 ' 0 在線中央
        If d > 1 - gap Then Return 0
        Dim t = d / Math.Max(0.001F, 1 - gap)
        Return CSng(Math.Sqrt(Math.Max(0, 1 - t * t)))
    End Function

    ''' <summary>纖維：幾個方向的細長雜訊疊起來。</summary>
    Private Shared Function Fibers(u As Single, v As Single, seed As Integer) As Single
        Dim sum = 0.0F
        For k = 0 To 3
            Dim a = Hash(k, 0, seed) * Math.PI
            Dim ca = CSng(Math.Cos(a)), sa = CSng(Math.Sin(a))
            Dim x = (u * ca + v * sa) / 18, y = (-u * sa + v * ca) / 0.7F
            sum += Noise(x, y, seed + k)
        Next
        Return SmoothStep(0.25F, 0.75F, sum / 4)
    End Function

    ''' <summary>Worley（細胞）雜訊：到最近與第二近特徵點的距離。hexagonal 時特徵點排成蜂巢。</summary>
    Private Shared Function Worley(x As Single, y As Single, seed As Integer, Optional hexagonal As Boolean = False) As (F1 As Single, F2 As Single)
        Dim ix = CInt(Math.Floor(x)), iy = CInt(Math.Floor(y))
        Dim f1 = 9.0F, f2 = 9.0F
        For dy = -1 To 1
            For dx = -1 To 1
                Dim cx = ix + dx, cy = iy + dy
                Dim px, py As Single
                If hexagonal Then
                    px = cx + 0.5F + If(cy Mod 2 = 0, 0, 0.5F) + (Hash(cx, cy, seed) - 0.5F) * 0.15F
                    py = cy + 0.5F + (Hash(cx, cy, seed + 1) - 0.5F) * 0.15F
                Else
                    px = cx + Hash(cx, cy, seed) : py = cy + Hash(cx, cy, seed + 1)
                End If
                Dim d = CSng(Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y)))
                If d < f1 Then
                    f2 = f1 : f1 = d
                ElseIf d < f2 Then
                    f2 = d
                End If
            Next
        Next
        Return (f1, f2)
    End Function

    ' 自訂紙紋：載入一次，灰階，長邊縮到 512
    Private Shared _customPath As String
    Private Shared _customMap As Single()
    Private Shared _customW, _customH As Integer

    Private Shared Function LoadCustom(path As String, ByRef w As Integer, ByRef h As Integer) As Single()
        SyncLock _sync
            If path = _customPath AndAlso _customMap IsNot Nothing Then
                w = _customW : h = _customH
                Return _customMap
            End If
            Try
                If String.IsNullOrEmpty(path) OrElse Not IO.File.Exists(path) Then Return Nothing
                Using src As New Bitmap(path)
                    Dim k = Math.Min(1.0, 512.0 / Math.Max(src.Width, src.Height))
                    Using bmp As New Bitmap(src, Math.Max(1, CInt(src.Width * k)), Math.Max(1, CInt(src.Height * k)))
                        Dim px = Perspective.ReadPixels(bmp)
                        Dim m(bmp.Width * bmp.Height - 1) As Single
                        For i = 0 To m.Length - 1
                            m(i) = (px(i * 4) * 0.114F + px(i * 4 + 1) * 0.587F + px(i * 4 + 2) * 0.299F) / 255.0F
                        Next
                        ' 拉滿對比，各種圖片都有明顯起伏
                        Dim mn = m.Min(), mx = m.Max()
                        For i = 0 To m.Length - 1
                            m(i) = (m(i) - mn) / Math.Max(0.01F, mx - mn)
                        Next
                        _customPath = path : _customMap = m : _customW = bmp.Width : _customH = bmp.Height
                    End Using
                End Using
                w = _customW : h = _customH
                Return _customMap
            Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException
                Return Nothing
            End Try
        End SyncLock
    End Function

    '=====================================================================
    ' 表面紋理
    '=====================================================================

    ''' <summary>
    ''' 把紙紋壓印成光影：依高度的斜率與光源方向變亮變暗，凹處再暗一點。
    ''' bmp 的 (0,0) 對應高度圖的 (ox, oy)；高度圖大小 mapW × mapH。只改顏色，透明度不變。
    ''' </summary>
    Public Shared Sub ApplySurface(bmp As Bitmap, map As Single(), mapW As Integer, mapH As Integer, ox As Integer, oy As Integer,
                                   strength As Integer, lightAngle As Integer)
        If strength <= 0 Then Return
        Dim px = Perspective.ReadPixels(bmp)
        Dim w = bmp.Width, h = bmp.Height
        Dim la = lightAngle * Math.PI / 180
        Dim lx = CSng(Math.Cos(la)), ly = CSng(-Math.Sin(la))
        Dim k = strength / 100.0F
        Dim hAt = Function(x As Integer, y As Integer) map(Math.Max(0, Math.Min(mapH - 1, y)) * mapW + Math.Max(0, Math.Min(mapW - 1, x)))
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                If px(i + 3) = 0 Then Continue For
                Dim gx = ox + x, gy = oy + y
                Dim dx = hAt(gx + 1, gy) - hAt(gx - 1, gy), dy = hAt(gx, gy + 1) - hAt(gx, gy - 1)
                ' 朝光的斜面變亮、背光變暗；凹處（高度低）略暗
                Dim lit = -(dx * lx + dy * ly) * 2.2F
                Dim shade = 1 + k * (lit * 0.9F - (0.5F - hAt(gx, gy)) * 0.25F)
                For c = 0 To 2
                    Dim v = px(i + c) * shade
                    px(i + c) = CByte(Math.Max(0, Math.Min(255, Math.Round(v))))
                Next
            Next
        Next
        Perspective.WritePixels(bmp, px)
    End Sub

    '=====================================================================
    ' 雜訊
    '=====================================================================

    Private Shared Function Hash(x As Integer, y As Integer, seed As Integer) As Single
        Dim n = CLng(x) * 374761393L + CLng(y) * 668265263L + CLng(seed) * 144665L
        n = (n Xor (n >> 13)) And &HFFFFFFFFL
        n = (n * 1274126177L) And &HFFFFFFFFL
        n = n Xor (n >> 16)
        Return (n And &HFFFFL) / 65535.0F
    End Function

    Private Shared Function Noise(x As Single, y As Single, seed As Integer) As Single
        Dim ix = CInt(Math.Floor(x)), iy = CInt(Math.Floor(y))
        Dim fx = x - ix, fy = y - iy
        fx = fx * fx * (3 - 2 * fx) : fy = fy * fy * (3 - 2 * fy)
        Dim a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed)
        Return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy
    End Function

    Private Shared Function Fbm(x As Single, y As Single, seed As Integer) As Single
        Return Noise(x, y, seed) * 0.57F + Noise(x * 2.1F, y * 2.1F, seed + 1) * 0.29F + Noise(x * 4.3F, y * 4.3F, seed + 2) * 0.14F
    End Function

    Private Shared Function SmoothStep(e0 As Single, e1 As Single, v As Single) As Single
        Dim t = Clamp01((v - e0) / (e1 - e0))
        Return t * t * (3 - 2 * t)
    End Function

    Private Shared Function Clamp01(v As Single) As Single
        If Not (v > 0) Then Return 0
        If v > 1 Then Return 1
        Return v
    End Function
End Class
