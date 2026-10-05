Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>
''' 「內建」主題的向量貼圖（愛心、星星等最早的 6 種之外的部分）。每款都是單色剪影，
''' 細節（眼睛、勾勾、鏡頭…）用挖空表現，所以可以任意改顏色、加陰影與發光，放大也清楚。
''' 設計座標為 -0.5..0.5（貼圖大小 = 1），外形順時針、挖空逆時針，以 Winding 規則填色：
''' 重疊的外形自動合併，挖空只挖掉它所在的外形。
''' </summary>
Public NotInheritable Class BuiltInStickers
    Private Sub New()
    End Sub

    ''' <summary>鍵、名稱、預設顏色（顯示在貼圖面板上的順序）。</summary>
    Public Shared ReadOnly Items As IReadOnlyList(Of (Key As String, Name As String, Color As Color)) = {
        ("smile", "笑臉", Color.FromArgb(255, 196, 30)),
        ("cat", "貓咪", Color.FromArgb(80, 82, 92)),
        ("paw", "貓掌", Color.FromArgb(150, 100, 80)),
        ("crown", "皇冠", Color.FromArgb(245, 178, 30)),
        ("bow", "蝴蝶結", Color.FromArgb(232, 69, 110)),
        ("gift", "禮物", Color.FromArgb(214, 52, 80)),
        ("balloon", "氣球", Color.FromArgb(235, 70, 90)),
        ("flower", "花朵", Color.FromArgb(240, 110, 165)),
        ("clover", "四葉草", Color.FromArgb(60, 165, 85)),
        ("sun", "太陽", Color.FromArgb(255, 165, 25)),
        ("moon", "月亮", Color.FromArgb(250, 205, 70)),
        ("cloud", "雲朵", Color.FromArgb(130, 185, 240)),
        ("bolt", "閃電", Color.FromArgb(255, 200, 30)),
        ("drop", "水滴", Color.FromArgb(60, 150, 230)),
        ("snow", "雪花", Color.FromArgb(110, 185, 240)),
        ("fire", "火焰", Color.FromArgb(255, 105, 25)),
        ("note", "音符", Color.FromArgb(70, 70, 95)),
        ("camera", "相機", Color.FromArgb(55, 60, 70)),
        ("coffee", "咖啡", Color.FromArgb(135, 85, 55)),
        ("plane", "紙飛機", Color.FromArgb(70, 150, 230)),
        ("pin", "定位", Color.FromArgb(232, 60, 70)),
        ("check", "打勾", Color.FromArgb(55, 175, 90)),
        ("cross", "叉叉", Color.FromArgb(228, 60, 60)),
        ("exclaim", "驚嘆號", Color.FromArgb(240, 90, 35)),
        ("question", "問號", Color.FromArgb(80, 115, 230)),
        ("thumb", "讚", Color.FromArgb(60, 135, 230)),
        ("sparkles", "閃閃", Color.FromArgb(255, 210, 50)),
        ("burst", "爆炸", Color.FromArgb(255, 85, 55))}

    ''' <summary>預設顏色；不是這裡的貼圖時回傳 Nothing。</summary>
    Public Shared Function DefaultColor(key As String) As Color?
        For Each it In Items
            If it.Key = key Then Return it.Color
        Next
        Return Nothing
    End Function

    ''' <summary>以 (cx, cy) 為中心、大小約 s 像素的路徑；不是這裡的貼圖時回傳 Nothing。</summary>
    Public Shared Function Build(key As String, cx As Single, cy As Single, s As Single) As GraphicsPath
        Dim f As New Figures()
        If Not Design(key, f) Then Return Nothing
        Dim path As New GraphicsPath(FillMode.Winding)
        For Each pts In f.List
            path.AddPolygon(pts.Select(Function(p) New PointF(cx + p.X * s, cy + p.Y * s)).ToArray())
        Next
        Return path
    End Function

    Private Shared Function Design(key As String, f As Figures) As Boolean
        Select Case key
            Case "smile"
                f.Add(Ell(0, 0, 0.5F, 0.5F))
                f.Hole(Ell(-0.16F, -0.1F, 0.06F, 0.1F))
                f.Hole(Ell(0.16F, -0.1F, 0.06F, 0.1F))
                Dim outer = Enumerable.Range(0, 25).Select(Function(i) Polar(0, 0.04F, 0.3F, 0.3F, 20 + i * 140 / 24.0))
                Dim inner = Enumerable.Range(0, 25).Select(Function(i) Polar(0, 0.04F, 0.22F, 0.17F, 160 - i * 140 / 24.0))
                f.Hole(outer.Concat(inner))
            Case "cat"
                f.Add(Ell(0, 0.06F, 0.42F, 0.36F))
                f.Add({P(-0.41F, -0.08F), P(-0.36F, -0.48F), P(-0.08F, -0.27F)})
                f.Add({P(0.41F, -0.08F), P(0.36F, -0.48F), P(0.08F, -0.27F)})
                f.Hole(Ell(-0.16F, 0, 0.055F, 0.085F))
                f.Hole(Ell(0.16F, 0, 0.055F, 0.085F))
                f.Hole({P(-0.05F, 0.11F), P(0.05F, 0.11F), P(0, 0.17F)})
                For Each side In {-1.0F, 1.0F}
                    f.Hole(Stroke({P(side * 0.34F, 0.13F), P(side * 0.14F, 0.16F)}, 0.022F))
                    f.Hole(Stroke({P(side * 0.33F, 0.25F), P(side * 0.14F, 0.21F)}, 0.022F))
                Next
            Case "paw"
                f.Add(Ell(0, 0.17F, 0.27F, 0.23F))
                f.Add(Ell(-0.31F, -0.1F, 0.1F, 0.135F, -22))
                f.Add(Ell(-0.11F, -0.31F, 0.1F, 0.135F, -6))
                f.Add(Ell(0.11F, -0.31F, 0.1F, 0.135F, 6))
                f.Add(Ell(0.31F, -0.1F, 0.1F, 0.135F, 22))
            Case "crown"
                f.Add({P(-0.45F, 0.28F), P(-0.45F, -0.14F), P(-0.22F, 0.05F), P(0, -0.3F), P(0.22F, 0.05F), P(0.45F, -0.14F), P(0.45F, 0.28F)})
                f.Add(Ell(-0.45F, -0.2F, 0.07F, 0.07F))
                f.Add(Ell(0, -0.37F, 0.075F, 0.075F))
                f.Add(Ell(0.45F, -0.2F, 0.07F, 0.07F))
                f.Add(RRect(-0.45F, 0.32F, 0.9F, 0.11F, 0.03F))
                f.Hole(Ell(-0.22F, 0.17F, 0.05F, 0.05F))
                f.Hole(Ell(0, 0.15F, 0.065F, 0.065F))
                f.Hole(Ell(0.22F, 0.17F, 0.05F, 0.05F))
            Case "bow"
                For Each side In {-1.0F, 1.0F}
                    f.Add(Ell(side * 0.25F, -0.06F, 0.25F, 0.17F, -side * 22))
                    f.Hole(Ell(side * 0.28F, -0.07F, 0.12F, 0.07F, -side * 22))
                    f.Add({P(side * 0.07F, 0.02F), P(side * 0.28F, 0.42F), P(side * 0.17F, 0.39F), P(side * 0.11F, 0.48F), P(side * 0.0F, 0.06F)})
                Next
                f.Add(RRect(-0.09F, -0.15F, 0.18F, 0.2F, 0.05F))
            Case "gift"
                f.Add(RRect(-0.43F, -0.12F, 0.37F, 0.13F, 0.02F))
                f.Add(RRect(0.06F, -0.12F, 0.37F, 0.13F, 0.02F))
                f.Add(RRect(-0.38F, 0.05F, 0.32F, 0.41F, 0.02F))
                f.Add(RRect(0.06F, 0.05F, 0.32F, 0.41F, 0.02F))
                f.Add(Ell(-0.14F, -0.25F, 0.14F, 0.085F, 20))
                f.Add(Ell(0.14F, -0.25F, 0.14F, 0.085F, -20))
                f.Hole(Ell(-0.15F, -0.26F, 0.065F, 0.035F, 20))
                f.Hole(Ell(0.15F, -0.26F, 0.065F, 0.035F, -20))
                f.Add(Ell(0, -0.21F, 0.05F, 0.05F))
            Case "balloon"
                f.Add(Ell(0, -0.13F, 0.31F, 0.36F))
                f.Add({P(0, 0.2F), P(0.065F, 0.3F), P(-0.065F, 0.3F)})
                f.Add(Stroke(Enumerable.Range(0, 13).Select(Function(i) P(CSng(0.035 * Math.Sin(i * 0.9)), 0.3F + i * 0.017F)), 0.022F))
                f.Hole(Ell(-0.13F, -0.27F, 0.05F, 0.1F, 30))
            Case "flower"
                For k = 0 To 4
                    Dim a = (-90 + k * 72) * Math.PI / 180
                    f.Add(Ell(CSng(0.32 * Math.Cos(a)), CSng(0.32 * Math.Sin(a)), 0.18F, 0.18F))
                Next
                f.Add(Ell(0, 0, 0.2F, 0.2F))
                f.Hole(Ell(0, 0, 0.13F, 0.13F))
                f.Add(Ell(0, 0, 0.075F, 0.075F))
            Case "clover"
                For k = 0 To 3
                    Dim a = (45 + k * 90) * Math.PI / 180
                    Dim dx = CSng(Math.Cos(a)), dy = CSng(Math.Sin(a))
                    Dim px = -dy, py = dx
                    f.Add(Ell(dx * 0.26F + px * 0.09F, dy * 0.26F + py * 0.09F, 0.13F, 0.13F))
                    f.Add(Ell(dx * 0.26F - px * 0.09F, dy * 0.26F - py * 0.09F, 0.13F, 0.13F))
                    f.Add({P(dx * 0.02F, dy * 0.02F), P(dx * 0.28F + px * 0.19F, dy * 0.28F + py * 0.19F), P(dx * 0.28F - px * 0.19F, dy * 0.28F - py * 0.19F)})
                Next
                f.Add(Stroke(Enumerable.Range(0, 9).Select(Function(i) P(CSng(i * 0.016 + 0.03 * Math.Sin(i / 3.0)), 0.05F + i * 0.055F)), 0.05F))
            Case "sun"
                f.Add(Ell(0, 0, 0.24F, 0.24F))
                For k = 0 To 11
                    Dim a = k * 30.0
                    f.Add({Polar(0, 0, 0.3F, 0.3F, a - 8), Polar(0, 0, 0.5F, 0.5F, a), Polar(0, 0, 0.3F, 0.3F, a + 8)})
                Next
            Case "moon"
                f.Add(Crescent(New PointF(0, 0), 0.45F, New PointF(0.2F, -0.12F), 0.38F))
            Case "cloud"
                f.Add(Ell(-0.24F, 0.08F, 0.2F, 0.18F))
                f.Add(Ell(0.0F, -0.08F, 0.25F, 0.25F))
                f.Add(Ell(0.25F, 0.04F, 0.2F, 0.2F))
                f.Add(RRect(-0.42F, 0.04F, 0.84F, 0.24F, 0.12F))
            Case "bolt"
                f.Add({P(-0.1F, -0.5F), P(0.28F, -0.5F), P(0.06F, -0.12F), P(0.32F, -0.12F), P(-0.22F, 0.5F), P(-0.04F, 0.02F), P(-0.3F, 0.02F)})
            Case "drop"
                f.Add(Teardrop(tipUp:=True))
                f.Hole(Ell(-0.12F, 0.14F, 0.05F, 0.1F, 20))
            Case "pin"
                f.Add(Teardrop(tipUp:=False))
                f.Hole(Ell(0, -0.14F, 0.13F, 0.13F))
            Case "snow"
                For k = 0 To 5
                    Dim a = -90 + k * 60.0
                    f.Add(Bar(P(0, 0), Polar(0, 0, 0.48F, 0.48F, a), 0.065F))
                    For Each d In {0.22F, 0.34F}
                        Dim b = Polar(0, 0, d, d, a)
                        Dim len = If(d < 0.3F, 0.15F, 0.11F)
                        f.Add(Bar(b, Polar(b.X, b.Y, len, len, a - 45), 0.05F))
                        f.Add(Bar(b, Polar(b.X, b.Y, len, len, a + 45), 0.05F))
                    Next
                Next
            Case "fire"
                f.Add(Smooth({P(0, 0.5F), P(-0.3F, 0.42F), P(-0.42F, 0.18F), P(-0.37F, -0.08F), P(-0.24F, -0.24F), P(-0.2F, -0.06F),
                              P(-0.1F, -0.3F), P(-0.02F, -0.5F), P(0.12F, -0.28F), P(0.2F, -0.38F), P(0.36F, -0.12F), P(0.42F, 0.15F), P(0.32F, 0.4F)}))
                f.Hole(Smooth({P(0, 0.42F), P(-0.15F, 0.36F), P(-0.2F, 0.2F), P(-0.12F, 0.03F), P(-0.05F, -0.1F), P(0.02F, 0.04F),
                               P(0.1F, -0.05F), P(0.18F, 0.12F), P(0.18F, 0.3F), P(0.1F, 0.39F)}))
            Case "note"
                f.Add(Ell(-0.24F, 0.3F, 0.14F, 0.1F, -20))
                f.Add(Ell(0.24F, 0.2F, 0.14F, 0.1F, -20))
                f.Add({P(-0.13F, -0.3F), P(-0.09F, -0.3F), P(-0.09F, 0.28F), P(-0.13F, 0.28F)})
                f.Add({P(0.35F, -0.4F), P(0.39F, -0.4F), P(0.39F, 0.18F), P(0.35F, 0.18F)})
                f.Add({P(-0.13F, -0.3F), P(0.39F, -0.42F), P(0.39F, -0.27F), P(-0.13F, -0.15F)})
            Case "camera"
                f.Add(RRect(-0.46F, -0.22F, 0.92F, 0.6F, 0.08F))
                f.Add(RRect(-0.2F, -0.34F, 0.3F, 0.16F, 0.04F))
                f.Hole(Ell(0, 0.08F, 0.2F, 0.2F))
                f.Add(Ell(0, 0.08F, 0.12F, 0.12F))
                f.Hole(RRect(0.24F, -0.14F, 0.13F, 0.07F, 0.02F))
            Case "coffee"
                f.Add({P(-0.38F, -0.1F), P(0.22F, -0.1F), P(0.17F, 0.36F), P(-0.33F, 0.36F)})
                f.Add(Ell(0.27F, 0.1F, 0.14F, 0.12F))
                f.Hole(Ell(0.28F, 0.1F, 0.065F, 0.055F))
                f.Add(RRect(-0.45F, 0.39F, 0.75F, 0.07F, 0.035F))
                For Each x In {-0.22F, -0.08F, 0.06F}
                    Dim x0 = x
                    f.Add(Stroke(Enumerable.Range(0, 9).Select(Function(i) P(x0 + CSng(0.035 * Math.Sin(i * 0.9)), -0.18F - i * 0.035F)), 0.035F))
                Next
            Case "plane"
                f.Add({P(-0.47F, -0.02F), P(0.47F, -0.38F), P(-0.1F, 0.14F)})
                f.Add({P(-0.04F, 0.18F), P(0.47F, -0.32F), P(0.08F, 0.45F)})
                f.Add({P(-0.1F, 0.21F), P(0.0F, 0.27F), P(-0.16F, 0.42F)})
            Case "check"
                f.Add(Ell(0, 0, 0.48F, 0.48F))
                f.Hole(Stroke({P(-0.22F, 0.0F), P(-0.06F, 0.17F), P(0.24F, -0.16F)}, 0.1F))
            Case "cross"
                f.Add(Ell(0, 0, 0.48F, 0.48F))
                Dim plus = {P(-0.06F, -0.26F), P(0.06F, -0.26F), P(0.06F, -0.06F), P(0.26F, -0.06F), P(0.26F, 0.06F), P(0.06F, 0.06F),
                            P(0.06F, 0.26F), P(-0.06F, 0.26F), P(-0.06F, 0.06F), P(-0.26F, 0.06F), P(-0.26F, -0.06F), P(-0.06F, -0.06F)}
                f.Hole(plus.Select(Function(p) Rot(p, 45)))
            Case "exclaim"
                f.Add(Stroke({P(0, -0.4F), P(0, 0.12F)}, 0.18F, taper:=0.55F))
                f.Add(Ell(0, 0.37F, 0.1F, 0.1F))
            Case "question"
                Dim hook = Enumerable.Range(0, 24).Select(Function(i) Polar(0, -0.17F, 0.22F, 0.22F, 195 + i * 225 / 23.0)).ToList()
                hook.Add(P(0.02F, 0.06F))
                hook.Add(P(0.0F, 0.16F))
                f.Add(Stroke(hook, 0.12F))
                f.Add(Ell(0, 0.38F, 0.08F, 0.08F))
            Case "thumb"
                f.Add(RRect(-0.18F, -0.08F, 0.5F, 0.5F, 0.1F))
                f.Add(Smooth({P(-0.18F, 0.04F), P(-0.12F, -0.22F), P(-0.08F, -0.42F), P(0.04F, -0.46F), P(0.1F, -0.36F), P(0.06F, -0.08F)}))
                For Each y In {0.08F, 0.2F, 0.31F}
                    f.Hole(RRect(0.0F, y, 0.3F, 0.025F, 0.012F))
                Next
                f.Add(RRect(-0.46F, -0.04F, 0.21F, 0.48F, 0.04F))
            Case "sparkles"
                f.Add(Star(-0.18F, 0.08F, 0.32F, 0.07F, 4))
                f.Add(Star(0.27F, -0.25F, 0.2F, 0.045F, 4))
                f.Add(Star(0.3F, 0.3F, 0.14F, 0.035F, 4))
            Case "burst"
                Dim pts As New List(Of PointF)()
                For i = 0 To 35
                    Dim r = If(i Mod 2 = 1, 0.33F, If(i Mod 4 = 0, 0.5F, 0.43F))
                    pts.Add(Polar(0, 0, r, r, -90 + i * 10.0))
                Next
                f.Add(pts)
            Case Else
                Return False
        End Select
        Return True
    End Function

    '=====================================================================
    ' 外形小工具（設計座標）
    '=====================================================================

    ''' <summary>外形清單：外形一律順時針、挖空一律逆時針（Winding 填色時挖空才會挖掉）。</summary>
    Private NotInheritable Class Figures
        Public ReadOnly List As New List(Of PointF())()

        Public Sub Add(pts As IEnumerable(Of PointF))
            List.Add(Oriented(pts.ToArray(), clockwise:=True))
        End Sub

        Public Sub Hole(pts As IEnumerable(Of PointF))
            List.Add(Oriented(pts.ToArray(), clockwise:=False))
        End Sub

        Private Shared Function Oriented(pts As PointF(), clockwise As Boolean) As PointF()
            Dim area = 0.0
            For i = 0 To pts.Length - 1
                Dim a = pts(i), b = pts((i + 1) Mod pts.Length)
                area += CDbl(a.X) * b.Y - CDbl(b.X) * a.Y
            Next
            ' y 軸向下：面積為正 = 畫面上順時針。
            If (area > 0) <> clockwise Then Array.Reverse(pts)
            Return pts
        End Function
    End Class

    Private Shared Function P(x As Single, y As Single) As PointF
        Return New PointF(x, y)
    End Function

    Private Shared Function Polar(cx As Single, cy As Single, rx As Single, ry As Single, degrees As Double) As PointF
        Dim a = degrees * Math.PI / 180
        Return New PointF(CSng(cx + rx * Math.Cos(a)), CSng(cy + ry * Math.Sin(a)))
    End Function

    Private Shared Function Rot(p As PointF, degrees As Double) As PointF
        Dim a = degrees * Math.PI / 180
        Return New PointF(CSng(p.X * Math.Cos(a) - p.Y * Math.Sin(a)), CSng(p.X * Math.Sin(a) + p.Y * Math.Cos(a)))
    End Function

    Private Shared Function Ell(cx As Single, cy As Single, rx As Single, ry As Single, Optional rotation As Double = 0) As PointF()
        Dim n = 48
        Dim pts(n - 1) As PointF
        For i = 0 To n - 1
            Dim a = i * 2 * Math.PI / n
            Dim r = Rot(New PointF(CSng(rx * Math.Cos(a)), CSng(ry * Math.Sin(a))), rotation)
            pts(i) = New PointF(cx + r.X, cy + r.Y)
        Next
        Return pts
    End Function

    Private Shared Function RRect(x As Single, y As Single, w As Single, h As Single, r As Single) As PointF()
        r = Math.Min(r, Math.Min(w, h) / 2)
        Dim pts As New List(Of PointF)()
        Dim corners = {(x + w - r, y + r, -90.0), (x + w - r, y + h - r, 0.0), (x + r, y + h - r, 90.0), (x + r, y + r, 180.0)}
        For Each c In corners
            For k = 0 To 6
                pts.Add(Polar(c.Item1, c.Item2, r, r, c.Item3 + k * 15))
            Next
        Next
        Return pts.ToArray()
    End Function

    Private Shared Function Star(cx As Single, cy As Single, outer As Single, inner As Single, points As Integer) As PointF()
        Return Enumerable.Range(0, points * 2).Select(Function(i) Polar(cx, cy, If(i Mod 2 = 0, outer, inner), If(i Mod 2 = 0, outer, inner), -90 + i * 180.0 / points)).ToArray()
    End Function

    ''' <summary>a 到 b 的粗線（長方形）。</summary>
    Private Shared Function Bar(a As PointF, b As PointF, w As Single) As PointF()
        Dim dx = b.X - a.X, dy = b.Y - a.Y
        Dim len = CSng(Math.Max(0.000001, Math.Sqrt(dx * dx + dy * dy)))
        Dim nx = -dy / len * w / 2, ny = dx / len * w / 2
        Return {P(a.X + nx, a.Y + ny), P(b.X + nx, b.Y + ny), P(b.X - nx, b.Y - ny), P(a.X - nx, a.Y - ny)}
    End Function

    ''' <summary>
    ''' 折線加粗成單一外形（轉角斜接、兩端圓頭），可當挖空使用（不會自己重疊）。
    ''' taper &lt; 1 時由起點的 w 漸細到終點的 w × taper（驚嘆號）。
    ''' </summary>
    Private Shared Function Stroke(points As IEnumerable(Of PointF), w As Single, Optional taper As Single = 1) As PointF()
        Dim pts = points.ToArray()
        Dim n = pts.Length
        Dim left As New List(Of PointF)(), right As New List(Of PointF)()
        Dim halfAt = Function(i As Integer) w / 2 * (1 - (1 - taper) * i / Math.Max(1, n - 1))
        Dim normals(n - 2) As PointF
        For i = 0 To n - 2
            Dim dx = pts(i + 1).X - pts(i).X, dy = pts(i + 1).Y - pts(i).Y
            Dim len = CSng(Math.Max(0.000001, Math.Sqrt(dx * dx + dy * dy)))
            normals(i) = P(-dy / len, dx / len)
        Next
        For i = 0 To n - 1
            Dim nrm As PointF
            Dim scale = 1.0F
            If i = 0 Then
                nrm = normals(0)
            ElseIf i = n - 1 Then
                nrm = normals(n - 2)
            Else
                Dim a = normals(i - 1), b = normals(i)
                Dim mx = a.X + b.X, my = a.Y + b.Y
                Dim ml = CSng(Math.Max(0.000001, Math.Sqrt(mx * mx + my * my)))
                nrm = P(mx / ml, my / ml)
                Dim dot = nrm.X * a.X + nrm.Y * a.Y
                scale = Math.Min(2.5F, 1 / Math.Max(0.2F, dot)) ' 斜接長度上限
            End If
            Dim hw = halfAt(i) * scale
            left.Add(P(pts(i).X + nrm.X * hw, pts(i).Y + nrm.Y * hw))
            right.Add(P(pts(i).X - nrm.X * hw, pts(i).Y - nrm.Y * hw))
        Next
        Dim result As New List(Of PointF)(left)
        ' 終點圓頭：從左側繞到右側。
        Dim endN = normals(n - 2), endHw = halfAt(n - 1)
        Dim endAngle = Math.Atan2(endN.Y, endN.X) * 180 / Math.PI
        For k = 1 To 7
            result.Add(Polar(pts(n - 1).X, pts(n - 1).Y, endHw, endHw, endAngle - k * 180 / 8.0))
        Next
        right.Reverse()
        result.AddRange(right)
        Dim startN = normals(0), startHw = halfAt(0)
        Dim startAngle = Math.Atan2(-startN.Y, -startN.X) * 180 / Math.PI
        For k = 1 To 7
            result.Add(Polar(pts(0).X, pts(0).Y, startHw, startHw, startAngle - k * 180 / 8.0))
        Next
        Return result.ToArray()
    End Function

    ''' <summary>封閉的 Catmull-Rom 平滑曲線。</summary>
    Private Shared Function Smooth(pts As PointF()) As PointF()
        Dim n = pts.Length
        Dim result As New List(Of PointF)()
        For i = 0 To n - 1
            Dim p0 = pts((i - 1 + n) Mod n), p1 = pts(i), p2 = pts((i + 1) Mod n), p3 = pts((i + 2) Mod n)
            For k = 0 To 5
                Dim t = k / 6.0F, t2 = t * t, t3 = t2 * t
                Dim f = Function(a As Single, b As Single, c As Single, d As Single) _
                    0.5F * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3)
                result.Add(P(f(p0.X, p1.X, p2.X, p3.X), f(p0.Y, p1.Y, p2.Y, p3.Y)))
            Next
        Next
        Return result.ToArray()
    End Function

    ''' <summary>水滴形：tipUp 時尖端朝上（水滴），否則朝下（定位圖釘）。</summary>
    Private Shared Function Teardrop(tipUp As Boolean) As PointF()
        Dim n = 72
        Dim pts(n - 1) As PointF
        For i = 0 To n - 1
            Dim t = i * 2 * Math.PI / n
            Dim x = 0.44 * Math.Sin(t) * Math.Sin(t / 2)
            Dim y = 0.5 * Math.Cos(t)
            pts(i) = New PointF(CSng(x), CSng(If(tipUp, -y, y)))
        Next
        Return pts
    End Function

    ''' <summary>月牙：圓 A 去掉圓 B 的部分（取兩段圓弧接成一個外形）。</summary>
    Private Shared Function Crescent(ca As PointF, ra As Single, cb As PointF, rb As Single) As PointF()
        Dim inB = Function(p As PointF) (p.X - cb.X) ^ 2 + (p.Y - cb.Y) ^ 2 < rb * rb
        Dim inA = Function(p As PointF) (p.X - ca.X) ^ 2 + (p.Y - ca.Y) ^ 2 < ra * ra
        Dim n = 360
        Dim a = Enumerable.Range(0, n).Select(Function(i) Polar(ca.X, ca.Y, ra, ra, i)).ToList()
        Dim b = Enumerable.Range(0, n).Select(Function(i) Polar(cb.X, cb.Y, rb, rb, i)).ToList()
        Dim arcA = Contiguous(a, Function(p) Not inB(p))
        Dim arcB = Contiguous(b, inA)
        If DrawGeometry.Dist(arcA(arcA.Count - 1), arcB(0)) > DrawGeometry.Dist(arcA(arcA.Count - 1), arcB(arcB.Count - 1)) Then arcB.Reverse()
        Return arcA.Concat(arcB).ToArray()
    End Function

    ''' <summary>圓周上符合條件的那一段（依順序，跨過起點也接得起來）。</summary>
    Private Shared Function Contiguous(circle As List(Of PointF), keep As Func(Of PointF, Boolean)) As List(Of PointF)
        Dim n = circle.Count
        Dim start = 0
        For i = 0 To n - 1
            If keep(circle(i)) AndAlso Not keep(circle((i - 1 + n) Mod n)) Then start = i : Exit For
        Next
        Dim result As New List(Of PointF)()
        For k = 0 To n - 1
            Dim p = circle((start + k) Mod n)
            If Not keep(p) Then Exit For
            result.Add(p)
        Next
        Return result
    End Function
End Class
