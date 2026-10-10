Imports System.Drawing

''' <summary>
''' 川劇臉譜：比京劇多了「臉上畫畫」的具象圖形（蝴蝶、蝙蝠、火焰、雲紋、太極、銅錢、葫蘆）、金銀色（金屬光澤）、粗黑勾邊、陰陽臉。
''' 各劇團畫法不同，這裡是依川劇臉譜特色重新設計的風格化版本。座標同 OperaLayer 的標準臉（兩眼 (∓0.5, 0)、嘴 (0, 1.05)）。
''' </summary>
Partial Public NotInheritable Class OperaRoles

    '-----------------------------------------------------------------
    ' 圖形產生器（標準臉座標）
    '-----------------------------------------------------------------

    ''' <summary>封閉的 Catmull-Rom 曲線：用幾個控制點畫出圓滑的形狀。</summary>
    Private Shared Function Curve(closed As Boolean, ParamArray v As Double()) As PointF()
        Dim c = P(v)
        Dim n = c.Length
        Dim r As New List(Of PointF)
        Dim segs = If(closed, n, n - 1)
        For i = 0 To segs - 1
            Dim p0 = c(If(closed, (i - 1 + n) Mod n, Math.Max(0, i - 1)))
            Dim p1 = c(i)
            Dim p2 = c((i + 1) Mod n)
            Dim p3 = c(If(closed, (i + 2) Mod n, Math.Min(n - 1, i + 2)))
            For k = 0 To 7
                Dim t = k / 8.0, t2 = t * t, t3 = t2 * t
                Dim x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3)
                Dim y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3)
                r.Add(New PointF(CSng(x), CSng(y)))
            Next
        Next
        If Not closed Then r.Add(c(n - 1))
        Return r.ToArray()
    End Function

    ''' <summary>圓或橢圓弧上的點（角度：0＝右、90＝下）。</summary>
    Private Shared Function Arc(cx As Double, cy As Double, rx As Double, ry As Double, a0 As Double, a1 As Double, Optional n As Integer = 32) As PointF()
        Return Enumerable.Range(0, n + 1).Select(Function(i)
                                                     Dim a = (a0 + (a1 - a0) * i / n) * Math.PI / 180
                                                     Return New PointF(CSng(cx + rx * Math.Cos(a)), CSng(cy + ry * Math.Sin(a)))
                                                 End Function).ToArray()
    End Function

    ''' <summary>漩渦（雲紋的捲）：從外往內捲 turns 圈。</summary>
    Private Shared Function Spiral(cx As Double, cy As Double, r0 As Double, r1 As Double, turns As Double, startDeg As Double, Optional clockwise As Boolean = True) As PointF()
        Dim n = CInt(40 * turns)
        Return Enumerable.Range(0, n + 1).Select(Function(i)
                                                     Dim t = i / CDbl(n)
                                                     Dim a = (startDeg + If(clockwise, 1, -1) * 360 * turns * t) * Math.PI / 180
                                                     Dim r = r0 + (r1 - r0) * t
                                                     Return New PointF(CSng(cx + r * Math.Cos(a)), CSng(cy + r * Math.Sin(a)))
                                                 End Function).ToArray()
    End Function

    Private Shared Function Move(pts As PointF(), cx As Double, cy As Double, sx As Double, sy As Double) As PointF()
        Return pts.Select(Function(q) New PointF(CSng(cx + q.X * sx), CSng(cy + q.Y * sy))).ToArray()
    End Function

    Private Shared Function Closed(pts As PointF()) As PointF()
        Return pts.Concat({pts(0)}).ToArray()
    End Function

    ''' <summary>（函式而不是靜態欄位：All 在另一個檔案初始化，欄位的順序不保證。）蝙蝠（右半邊，中心在 x=0；mirror 後成整隻）：單位大小，用 Move 放大。</summary>
    Private Shared Function BatHalf() As PointF()
        Return P(0, -0.42, 0.07, -0.62, 0.12, -0.36, 0.3, -0.42, 0.62, -0.52, 1.0, -0.22,
                                                    0.86, 0.06, 0.72, -0.04, 0.58, 0.2, 0.44, 0.04, 0.28, 0.28, 0.12, 0.14, 0, 0.42)
    End Function

    ''' <summary>火焰（右半邊，底在 (0,0)、往上 1）。</summary>
    Private Shared Function FlameHalf() As PointF()
        Return P(0, 0, 0.42, -0.06, 0.56, -0.3, 0.4, -0.5, 0.66, -0.78, 0.3, -0.7, 0.26, -1.0, 0.08, -0.82, 0, -1.25)
    End Function

    ''' <summary>加金屬光澤。</summary>
    Private Shared Function Metal(l As OperaLayer) As OperaLayer
        l.Metal = True
        Return l
    End Function

    ''' <summary>封閉形狀的黑邊（川劇的粗黑勾邊）。</summary>
    Private Shared Function Edge(pts As PointF(), width As Double, Optional mirror As Boolean = True, Optional hex As Integer = &H121010) As OperaLayer
        Return Stroke(hex, width, Closed(pts), mirror:=mirror)
    End Function

    '-----------------------------------------------------------------
    ' 華麗元件（川劇變臉臉譜常見的構圖）：印堂、眼睛周圍、臉頰、鼻子、下巴各有幾種樣式，每張臉譜挑不同的組合
    '-----------------------------------------------------------------

    ''' <summary>印堂紋樣式。</summary>
    Private Enum Column
        None
        ''' <summary>火焰柱（外層、內層兩色）。</summary>
        Flame
        ''' <summary>水滴（上尖下圓）。</summary>
        Drop
        ''' <summary>額頭一排圓點。</summary>
        Dots
    End Enum

    ''' <summary>眼睛周圍。</summary>
    Private Enum EyeStyle
        None
        ''' <summary>白色葉形眼斑＋黑眼圈＋眼尾黑勾。</summary>
        Leaf
        ''' <summary>黑色大淚形眼窩往外下方拉到臉頰，描白邊。</summary>
        Teardrop
        ''' <summary>粗白眼圈，外眼角一個色三角。</summary>
        WhiteRim
        ''' <summary>黑眼窩，外眼角三道火焰尖竄向太陽穴。</summary>
        Fire
        ''' <summary>金屬金眼眶＋銀色眼斑。</summary>
        GoldRing
        ''' <summary>往下垂的黑淚滴，裡面一圈色。</summary>
        Droop
    End Enum

    ''' <summary>臉頰。</summary>
    Private Enum CheekStyle
        None
        ''' <summary>細黑捲紋＋小漩渦。</summary>
        Curls
        ''' <summary>臉頰外側紋：從眼尾沿外側臉頰往下巴、末端捲起的粗弧線，中間夾一道細色線。</summary>
        Folds
        ''' <summary>鼻子以下到下顎整塊換色（下巴另外留白）。</summary>
        LowerBlock
        ''' <summary>臉頰一塊水滴形色斑，描黑邊。</summary>
        Patch
        ''' <summary>白色雲捲描黑邊。</summary>
        Clouds
    End Enum

    Private Enum NoseStyle
        ''' <summary>白鼻樑＋黑鼻翼。</summary>
        WhiteBridge
        ''' <summary>紅鼻頭＋黑鼻翼。</summary>
        RedTip
        ''' <summary>金屬金鼻樑＋黑鼻翼。</summary>
        GoldBridge
        ''' <summary>鼻樑兩側往上捲的黑紋。</summary>
        Curls
        ''' <summary>只有黑鼻翼。</summary>
        Plain
    End Enum

    Private Enum ChinStyle
        None
        ''' <summary>白橢圓描黑邊。</summary>
        WhiteOval
        ''' <summary>黑三角描白邊。</summary>
        BlackTriangle
        ''' <summary>紅元寶描黑邊。</summary>
        RedYuanbao
        ''' <summary>金色火焰往下。</summary>
        GoldFlame
    End Enum

    ''' <summary>華麗臉譜的配色與要畫哪些元件。</summary>
    Private NotInheritable Class Ornate
        Public Base As Integer
        Public Brow As Integer = &H121010 ' 掃帚眉
        Public BrowEdge As Integer = &HF6F2EC ' 眉的勾邊
        Public BrowScale As Double = 1.0
        Public Col As Column = Column.Flame
        Public ColOuter As Integer = &HC8202A
        Public ColInner As Integer = &HF2C430
        Public ColMetal As Boolean
        Public Forehead As Integer ' 額頭上半另一個顏色（0＝沒有）
        Public Eye As EyeStyle = EyeStyle.Leaf
        Public EyeColor As Integer = &HF6F2EC ' 眼斑（Leaf）、眼窩（Teardrop/Fire/Droop 用黑）
        Public EyeAccent As Integer = &HC8202A ' 外眼角三角、淚滴裡面那圈
        Public Cheek As CheekStyle = CheekStyle.Curls
        Public CheekColor As Integer = &H121010
        Public CheekAccent As Integer = &HD82A2A
        Public Nose As NoseStyle = NoseStyle.WhiteBridge
        Public Chin As ChinStyle = ChinStyle.WhiteOval
        Public Lip As Integer = &HC8202A
    End Class

    ''' <summary>掃帚眉（右半邊）：從鼻樑往上掃過額頭，在太陽穴捲下來。</summary>
    Private Shared Function SweepBrow(k As Double) As PointF()
        Dim pts = Curve(True, 0.06, -0.16, 0.14, -0.48, 0.36, -0.76, 0.68, -0.9, 0.98, -0.84, 1.12, -0.6, 1.06, -0.36, 0.96, -0.3,
                        0.98, -0.44, 0.88, -0.5, 0.7, -0.5, 0.46, -0.42, 0.26, -0.28, 0.14, -0.1)
        Return pts.Select(Function(q) New PointF(q.X, CSng(-0.1 + (q.Y + 0.1) * k))).ToArray()
    End Function

    ''' <summary>白色葉形眼斑（右眼）。</summary>
    Private Shared Function EyePatch() As PointF()
        Return Curve(True, 0.14, -0.1, 0.32, -0.26, 0.62, -0.32, 0.9, -0.26, 1.0, -0.08, 0.88, 0.14, 0.6, 0.22, 0.32, 0.18, 0.16, 0.06)
    End Function

    Private Shared Sub AddOrnate(r As OperaRole, o As Ornate)
        Const Ink = &H121010, Paper = &HF6F2EC, Gold = &HE2B23A, Silver = &HD6DCE6
        r.Layers.Add(Fill(o.Base))
        If o.Forehead <> 0 Then
            r.Layers.Add(Poly(o.Forehead, Curve(True, 0, -1.6, 0.9, -1.4, 1.1, -0.9, 0.8, -0.82, 0.45, -0.9, 0.18, -0.86, 0, -0.9), op:=0.95, blur:=0.02))
        End If

        ' ---- 臉頰（先畫，眼睛、鼻子疊在上面）----
        Select Case o.Cheek
            Case CheekStyle.Curls
                r.Layers.Add(Stroke(o.CheekColor, 0.022, Spiral(0.98, -0.12, 0.09, 0.01, 1.3, 270)))
                r.Layers.Add(Stroke(o.CheekColor, 0.022, Curve(False, 0.42, 0.34, 0.6, 0.4, 0.74, 0.56, 0.7, 0.72)))
                r.Layers.Add(Stroke(o.CheekColor, 0.022, Spiral(0.66, 0.74, 0.08, 0.01, 1.2, 0)))
                If o.CheekAccent <> 0 Then r.Layers.Add(Stroke(o.CheekAccent, 0.03, Spiral(0.86, 0.56, 0.07, 0.01, 1.2, 180)))
            Case CheekStyle.Folds
                ' 從眼尾沿外側臉頰往下巴，末端捲起（不繞過嘴角，免得像鬍子）
                Dim fold = Curve(False, 0.66, 0.24, 0.8, 0.5, 0.82, 0.82, 0.72, 1.08, 0.6, 1.16, 0.56, 1.06, 0.64, 1.02)
                r.Layers.Add(Stroke(o.CheekColor, 0.1, fold, taper:=True))
                r.Layers.Add(Stroke(o.CheekAccent, 0.025, fold.Select(Function(q) New PointF(q.X + 0.005F, q.Y)).ToArray(), taper:=True))
                r.Layers.Add(Stroke(o.CheekColor, 0.025, Spiral(0.5, 0.42, 0.07, 0.01, 1.2, 200)))
            Case CheekStyle.LowerBlock
                Dim block = Curve(True, 0, 0.74, 0.22, 0.7, 0.5, 0.74, 0.86, 0.92, 0.92, 1.2, 0.7, 1.55, 0.3, 1.72, 0, 1.74)
                r.Layers.Add(Poly(o.CheekColor, block, blur:=0.008))
                r.Layers.Add(Stroke(o.CheekAccent, 0.022, Curve(False, 0.22, 0.7, 0.5, 0.74, 0.86, 0.92, 0.92, 1.2)))
                r.Layers.Add(Stroke(Paper, 0.02, Spiral(0.62, 0.5, 0.08, 0.01, 1.2, 160)))
            Case CheekStyle.Patch
                Dim patch = Curve(True, 0.56, 0.28, 0.78, 0.26, 0.92, 0.42, 0.86, 0.66, 0.7, 0.86, 0.6, 0.62)
                r.Layers.Add(Poly(o.CheekAccent, patch, blur:=0.008))
                r.Layers.Add(Edge(patch, 0.02, hex:=o.CheekColor))
                r.Layers.Add(Stroke(o.CheekColor, 0.018, Spiral(0.75, 0.5, 0.08, 0.01, 1.3, 90)))
            Case CheekStyle.Clouds
                For Each c In {(X:=0.66, Y:=0.56, R:=0.12, S:=150.0), (X:=0.9, Y:=-0.1, R:=0.08, S:=250.0)}
                    Dim sp = Spiral(c.X, c.Y, c.R, 0.02, 1.6, c.S)
                    r.Layers.Add(Stroke(Ink, 0.06, sp))
                    r.Layers.Add(Stroke(o.CheekAccent, 0.032, sp))
                Next
        End Select

        ' ---- 眼睛周圍 ----
        Select Case o.Eye
            Case EyeStyle.Leaf
                Dim patch = EyePatch()
                r.Layers.Add(Poly(o.EyeColor, patch, blur:=0.008))
                r.Layers.Add(Edge(patch, 0.02, hex:=Ink))
            Case EyeStyle.Teardrop
                Dim tear = Curve(True, 0.14, -0.08, 0.34, -0.22, 0.64, -0.26, 0.92, -0.18, 1.02, 0.0, 0.88, 0.22, 0.7, 0.5, 0.52, 0.24, 0.3, 0.14)
                r.Layers.Add(Poly(Ink, tear, blur:=0.008))
                r.Layers.Add(Edge(tear, 0.03, hex:=Paper))
            Case EyeStyle.WhiteRim
                r.Layers.Add(Socket(Paper, 1.45, 1.9, 0.12))
                r.Layers.Add(Poly(o.EyeAccent, P(0.76, -0.04, 1.02, -0.2, 0.94, 0.1), blur:=0.006))
                r.Layers.Add(Outline(Ink, 0.02, 1.45, 0.12))
            Case EyeStyle.Fire
                r.Layers.Add(Socket(Ink, 1.5, 1.8, 0.3))
                For Each v In {P(0.74, -0.08, 0.9, -0.26, 1.0, -0.5), P(0.76, -0.02, 0.96, -0.12, 1.1, -0.28), P(0.76, 0.04, 0.98, 0.02, 1.12, -0.06)}
                    r.Layers.Add(Stroke(Ink, 0.07, v, taper:=True))
                Next
                r.Layers.Add(Outline(o.EyeAccent, 0.022, 1.2, 0.08))
            Case EyeStyle.GoldRing
                r.Layers.Add(Socket(Silver, 1.5, 1.8, 0.1, 0.9))
                r.Layers.Add(Metal(Outline(Gold, 0.05, 1.42, 0.1)))
                r.Layers.Add(Outline(Ink, 0.016, 1.15, 0.05))
            Case EyeStyle.Droop
                Dim drop = Curve(True, 0.2, -0.1, 0.5, -0.22, 0.86, -0.12, 0.9, 0.06, 0.72, 0.2, 0.56, 0.52, 0.44, 0.22, 0.22, 0.08)
                r.Layers.Add(Poly(Ink, drop, blur:=0.008))
                r.Layers.Add(Edge(drop, 0.025, hex:=Paper))
                r.Layers.Add(Socket(o.EyeAccent, 1.18, 1.4, 0.0))
        End Select
        ' 掃帚眉（白邊在下、眉在上）
        Dim brow = SweepBrow(o.BrowScale)
        r.Layers.Add(Poly(o.BrowEdge, brow, blur:=0.006))
        r.Layers.Add(Stroke(o.BrowEdge, 0.07, Closed(brow)))
        r.Layers.Add(Poly(o.Brow, brow, blur:=0.006))
        If o.Eye = EyeStyle.Leaf Then
            r.Layers.Add(Socket(Ink, 1.3, 1.6, 0.18))
            r.Layers.Add(Stroke(Ink, 0.085, Curve(False, 0.72, 0.04, 0.92, 0.18, 0.97, 0.42, 0.86, 0.52, 0.78, 0.42), taper:=True))
        End If
        If o.Eye <> EyeStyle.None Then r.Layers.Add(Outline(Paper, 0.022, 1.12, 0.05))

        ' ---- 印堂 ----
        Select Case o.Col
            Case Column.Flame
                Dim outer = Curve(True, 0, -0.08, 0.09, -0.3, 0.14, -0.58, 0.08, -0.82, 0.12, -1.0, 0, -1.12)
                Dim inner = Curve(True, 0, -0.2, 0.05, -0.38, 0.08, -0.6, 0.04, -0.82, 0, -0.92)
                Dim lo = Poly(o.ColOuter, outer, blur:=0.006)
                lo.Metal = o.ColMetal
                r.Layers.Add(lo)
                r.Layers.Add(Stroke(Paper, 0.03, Closed(outer)))
                r.Layers.Add(Poly(o.ColInner, inner, blur:=0.006))
            Case Column.Drop
                Dim drop = Curve(True, 0, -1.02, 0.07, -0.72, 0.13, -0.5, 0.1, -0.36, 0, -0.32)
                r.Layers.Add(Poly(o.ColOuter, drop, blur:=0.006))
                r.Layers.Add(Stroke(Paper, 0.03, Closed(drop)))
                r.Layers.Add(Oval(o.ColInner, 0, -0.47, 0.05, 0.07, False))
            Case Column.Dots
                For Each d In {(X:=0.0, Y:=-0.92), (X:=0.16, Y:=-0.88), (X:=0.3, Y:=-0.8), (X:=0.08, Y:=-0.76), (X:=0.22, Y:=-0.7)}
                    r.Layers.Add(Oval(o.ColOuter, d.X, d.Y, 0.045, 0.045, d.X <> 0, 1, 0.006))
                    r.Layers.Add(Stroke(Paper, 0.014, Arc(d.X, d.Y, 0.045, 0.045, 0, 360), mirror:=d.X <> 0))
                Next
        End Select

        ' ---- 鼻子 ----
        Dim wings = Curve(True, 0, 0.56, 0.12, 0.5, 0.22, 0.58, 0.2, 0.7, 0.06, 0.72)
        Select Case o.Nose
            Case NoseStyle.WhiteBridge
                r.Layers.Add(Poly(Paper, P(0, -0.12, 0.05, -0.06, 0.06, 0.42, 0, 0.5), blur:=0.008))
            Case NoseStyle.GoldBridge
                r.Layers.Add(Metal(Poly(Gold, P(0, -0.16, 0.07, -0.08, 0.08, 0.44, 0, 0.52), blur:=0.008)))
                r.Layers.Add(Stroke(Ink, 0.014, P(0.07, -0.08, 0.08, 0.44)))
            Case NoseStyle.Curls
                ' 鼻樑兩側往上捲（不放在鼻孔旁邊，免得像捲鬍子）
                r.Layers.Add(Stroke(Ink, 0.02, Curve(False, 0.1, 0.46, 0.16, 0.3, 0.2, 0.16)))
                r.Layers.Add(Stroke(Ink, 0.02, Spiral(0.26, 0.1, 0.07, 0.01, 1.3, 90)))
        End Select
        r.Layers.Add(Poly(Ink, wings, blur:=0.006))
        If o.Nose = NoseStyle.RedTip Then r.Layers.Add(Oval(&HD8202A, 0, 0.56, 0.09, 0.07, False, 1, 0.008))

        ' ---- 下巴（川劇這裡不畫鬍子）----
        Select Case o.Chin
            Case ChinStyle.WhiteOval
                r.Layers.Add(Oval(Paper, 0, 1.38, 0.24, 0.13, False, 1, 0.008))
                r.Layers.Add(Stroke(Ink, 0.016, Arc(0, 1.38, 0.24, 0.13, 0, 360), mirror:=False))
            Case ChinStyle.BlackTriangle
                Dim tri = P(-0.3, 1.24, 0.3, 1.24, 0, 1.62)
                r.Layers.Add(Poly(Ink, tri, mirror:=False, blur:=0.006))
                r.Layers.Add(Edge(tri, 0.022, False, Paper))
            Case ChinStyle.RedYuanbao
                Dim yb = Curve(True, -0.28, 1.24, -0.12, 1.3, 0, 1.26, 0.12, 1.3, 0.28, 1.24, 0.2, 1.46, 0, 1.54, -0.2, 1.46)
                r.Layers.Add(Poly(&HC8202A, yb, mirror:=False, blur:=0.006))
                r.Layers.Add(Edge(yb, 0.018, False))
            Case ChinStyle.GoldFlame
                Dim fl = Move(FlameHalf, 0, 1.22, 0.24, -0.32)
                r.Layers.Add(Metal(Poly(Gold, fl, blur:=0.006)))
                r.Layers.Add(Edge(fl, 0.018))
        End Select
        r.Layers.Add(Lips(o.Lip, 1, 1, 0.95))
    End Sub

    '-----------------------------------------------------------------
    ' 角色（每張的眼睛、臉頰、鼻子、下巴挑不同的組合）
    '-----------------------------------------------------------------

    Private Shared Sub BuildChuan(list As List(Of OperaRole))
        Dim r As OperaRole
        Const Gold = &HE2B23A, Silver = &HD6DCE6, Ink = &H121010, Paper = &HF4F0E8

        ' 1 張飛・蝴蝶臉：蝴蝶眼窩＋臉頰外側紋＋紅鼻頭
        r = New OperaRole With {.Name = "張飛（蝴蝶臉）", .Group = OperaGroup.Chuan, .Lift = 40,
                                .Note = "川劇張飛：眉眼畫成一對黑蝴蝶（白翅脈、觸鬚捲），額頭紅菱描金，臉頰外側黑紋夾紅線、紅鼻頭、白下巴。勇猛又憨直。"}
        AddOrnate(r, New Ornate With {.Base = &HF0E6E0, .Col = Column.None, .Eye = EyeStyle.None, .Cheek = CheekStyle.Folds, .CheekAccent = &HD82A2A,
                                      .Nose = NoseStyle.RedTip})
        Dim wingUp = Curve(True, 0.14, -0.08, 0.2, -0.5, 0.45, -0.84, 0.8, -0.88, 1.02, -0.6, 0.96, -0.24, 0.7, -0.08)
        Dim wingLo = Curve(True, 0.2, 0.06, 0.6, 0.08, 0.96, 0.2, 0.92, 0.52, 0.64, 0.62, 0.34, 0.42)
        r.Layers.Add(Poly(Ink, wingUp))
        r.Layers.Add(Poly(Ink, wingLo))
        For Each v In {P(0.24, -0.2, 0.5, -0.5, 0.8, -0.72), P(0.32, -0.16, 0.66, -0.34, 0.92, -0.4), P(0.34, 0.16, 0.58, 0.32, 0.8, 0.44)}
            r.Layers.Add(Stroke(Paper, 0.026, v, taper:=True))
        Next
        r.Layers.Add(Oval(&HD82A2A, 0.62, -0.62, 0.05, 0.05))
        r.Layers.Add(Oval(&HD82A2A, 0.7, 0.36, 0.04, 0.04))
        Dim lozenge = P(0, -1.12, 0.15, -0.84, 0, -0.56, -0.15, -0.84)
        r.Layers.Add(Poly(&HC8242C, lozenge, mirror:=False))
        r.Layers.Add(Metal(Edge(lozenge, 0.02, False, Gold)))
        r.Layers.Add(Stroke(Ink, 0.1, P(0, -0.5, 0, 0.42), mirror:=False))
        r.Layers.Add(Stroke(Ink, 0.03, Curve(False, 0.03, -0.5, 0.12, -0.7, 0.26, -0.76, 0.3, -0.68, 0.24, -0.64)))
        r.Layers.Add(Outline(Paper, 0.035, 1.3, 0.05))
        r.Layers.Add(Oval(&HD8202A, 0, 0.56, 0.09, 0.07, False, 1, 0.008))
        list.Add(r)

        ' 2 包拯・月牙：白眼圈金角、白雲紋
        r = New OperaRole With {.Name = "包拯（月牙）", .Group = OperaGroup.Chuan, .Lift = 25,
                                .Note = "黑整臉：白色掃帚眉鑲金、粗白眼圈外角描金、額頭描金邊的白月牙，臉頰白雲捲。鐵面無私。"}
        AddOrnate(r, New Ornate With {.Base = &H1A1818, .Brow = Paper, .BrowEdge = Gold, .Col = Column.None, .Eye = EyeStyle.WhiteRim, .EyeAccent = Gold,
                                      .Cheek = CheekStyle.Clouds, .CheekAccent = Paper, .Nose = NoseStyle.Plain, .Chin = ChinStyle.None, .Lip = &H5A1010})
        Dim moon = Arc(0, -0.86, 0.24, 0.24, 15, 165).Concat(Arc(0, -0.94, 0.22, 0.22, 160, 20)).ToArray()
        r.Layers.Add(Poly(Paper, moon, mirror:=False))
        r.Layers.Add(Metal(Edge(moon, 0.02, False, Gold)))
        list.Add(r)

        ' 3 鍾馗・紅臉蝙蝠：火焰眼、下半臉白、鼻樑捲紋
        r = New OperaRole With {.Name = "鍾馗（蝙蝠）", .Group = OperaGroup.Chuan, .Lift = 55,
                                .Note = "紅臉：火焰眼竄向太陽穴、黑掃帚眉，額頭展翅黑蝙蝠（取「福」），鼻子以下整塊白、紅元寶下巴，鼻樑捲紋。捉鬼的判官。"}
        AddOrnate(r, New Ornate With {.Base = &HC0242A, .Col = Column.None, .Eye = EyeStyle.Fire, .EyeAccent = Paper,
                                      .Cheek = CheekStyle.LowerBlock, .CheekColor = Paper, .CheekAccent = Ink, .Nose = NoseStyle.Curls, .Chin = ChinStyle.RedYuanbao})
        r.Layers.Add(Poly(Paper, Move(BatHalf, 0, -0.74, 0.6, 0.42)))
        r.Layers.Add(Poly(Ink, Move(BatHalf, 0, -0.74, 0.54, 0.36)))
        list.Add(r)

        ' 4 火焰紅臉：黑淚形眼窩、頰上金斑、金鼻樑、金火焰下巴
        r = New OperaRole With {.Name = "火焰紅臉", .Group = OperaGroup.Chuan, .Lift = 60,
                                .Note = "紅臉、額頭金黃：金色火焰柱（藍焰心）、黑淚形眼窩描白、頰上金斑、金鼻樑、下巴金火焰。性情剛烈的武將。"}
        AddOrnate(r, New Ornate With {.Base = &HC21E24, .Forehead = &HF0B828, .ColOuter = Gold, .ColMetal = True, .ColInner = &H2A5CC8,
                                      .Eye = EyeStyle.Teardrop, .Cheek = CheekStyle.Patch, .CheekAccent = &HF0B828, .Nose = NoseStyle.GoldBridge, .Chin = ChinStyle.GoldFlame})
        list.Add(r)

        ' 5 雲紋藍臉：白葉眼斑、白雲捲
        r = New OperaRole With {.Name = "雲紋藍臉", .Group = OperaGroup.Chuan, .Lift = 45,
                                .Note = "藍臉：紅色水滴印堂描白邊、黑掃帚眉、白葉眼斑，太陽穴與臉頰捲起白雲紋、白鼻樑、白下巴。勇猛剛直、有神通的角色。"}
        AddOrnate(r, New Ornate With {.Base = &H1E50A8, .Col = Column.Drop, .ColOuter = &HC8202A, .ColInner = &HF2C430,
                                      .Cheek = CheekStyle.Clouds, .CheekAccent = Paper})
        Dim sp = Spiral(0.84, -0.62, 0.13, 0.02, 1.6, 200)
        r.Layers.Add(Stroke(Ink, 0.06, sp))
        r.Layers.Add(Stroke(Paper, 0.032, sp))
        list.Add(r)

        ' 6 金臉神佛：金眼眶、紅鼻頭、紅元寶下巴
        r = New OperaRole With {.Name = "金臉神佛", .Group = OperaGroup.Chuan, .Lift = 50,
                                .Note = "金臉（金屬光澤）：額頭一排紅點、紅火焰印堂，金眼眶配銀眼斑，臉頰乾淨只點紅，紅鼻頭、紅元寶下巴。天神、佛一類的角色。"}
        AddOrnate(r, New Ornate With {.Base = &HDCAA3C, .Col = Column.Dots, .ColOuter = &HC8202A, .Eye = EyeStyle.GoldRing,
                                      .Cheek = CheekStyle.None, .Nose = NoseStyle.RedTip, .Chin = ChinStyle.RedYuanbao, .Lip = &HB01828})
        r.Layers(0).Metal = True
        r.Layers.Add(Poly(&HC8202A, Curve(True, 0, -0.1, 0.07, -0.3, 0.1, -0.52, 0.04, -0.62, 0, -0.66), blur:=0.006))
        r.Layers.Add(Oval(&HC8202A, 0.74, 0.5, 0.05, 0.05, True, 1, 0.006))
        list.Add(r)

        ' 7 綠臉精怪：下垂淚眼、臉頰外側紅紋、黑三角下巴
        r = New OperaRole With {.Name = "綠臉精怪", .Group = OperaGroup.Chuan, .Lift = 50,
                                .Note = "綠臉：黃色火焰印堂、下垂的黑淚眼（裡面一圈黃）、臉頰外側紅紋夾黑線、鼻樑捲紋、黑三角下巴、嘴角獠牙，額頭水波紋。山精水怪一類的角色。"}
        AddOrnate(r, New Ornate With {.Base = &H2C8A48, .ColOuter = &HF2C430, .ColInner = &HC8202A, .Eye = EyeStyle.Droop, .EyeAccent = &HF2C430,
                                      .Cheek = CheekStyle.Folds, .CheekColor = &HC8202A, .CheekAccent = Ink, .Nose = NoseStyle.Curls,
                                      .Chin = ChinStyle.BlackTriangle, .Lip = &H141010})
        For Each yy In {-0.9, -1.04}
            Dim y0 = yy
            r.Layers.Add(Stroke(Ink, 0.026, Enumerable.Range(0, 13).Select(Function(i) New PointF(CSng(0.22 + 0.5 * i / 12), CSng(y0 + 0.035 * Math.Sin(i * Math.PI / 3)))).ToArray()))
        Next
        r.Layers.Add(Poly(Paper, P(0.26, 1.04, 0.36, 1.04, 0.31, 1.22)))
        list.Add(r)

        ' 8 太極臉：黑淚形眼窩、頰上白斑、白鼻樑、紅元寶下巴
        r = New OperaRole With {.Name = "太極臉", .Group = OperaGroup.Chuan, .Lift = 45,
                                .Note = "紅臉、額頭黑白太極描金，黑掃帚眉、黑淚形眼窩、頰上白斑捲紋、白鼻樑。亦正亦邪、有道行的角色。"}
        AddOrnate(r, New Ornate With {.Base = &HC8222A, .Col = Column.None, .Eye = EyeStyle.Teardrop, .Cheek = CheekStyle.Patch, .CheekAccent = Paper,
                                      .Chin = ChinStyle.None})
        Const TR = 0.17, TY = -0.76
        r.Layers.Add(Poly(Paper, Arc(0, TY, TR, TR, 0, 360), mirror:=False))
        r.Layers.Add(Poly(&H161414, Arc(0, TY, TR, TR, 90, 270).Concat(Arc(0, TY - TR / 2, TR / 2, TR / 2, 270, 450)).Concat(Arc(0, TY + TR / 2, TR / 2, TR / 2, 270, 90)).ToArray(), mirror:=False))
        r.Layers.Add(Oval(&H161414, 0, TY - TR / 2, TR / 7, TR / 7, False, 1, 0.006))
        r.Layers.Add(Oval(Paper, 0, TY + TR / 2, TR / 7, TR / 7, False, 1, 0.006))
        r.Layers.Add(Metal(Stroke(Gold, 0.022, Arc(0, TY, TR, TR, 0, 360), mirror:=False)))
        list.Add(r)

        ' 9 陰陽臉（左黑右白）
        r = New OperaRole With {.Name = "陰陽臉", .Group = OperaGroup.Chuan, .Lift = 45,
                                .Note = "左黑右白的陰陽臉：兩邊的眉、眼窩、捲紋用相反的顏色，鼻樑紅火焰。川劇不對稱臉譜的代表畫法。"}
        r.Layers.Add(Fill(&HF0ECE6, 0.93))
        r.Layers.Add(Poly(&H161414, P(-2, -2, 0, -2, 0, 2.5, -2, 2.5), mirror:=False, op:=0.95, blur:=0.01))
        For Each side In {1.0, -1.0}
            Dim s = side
            Dim fg = If(s > 0, Ink, Paper)
            Dim m = Function(pts As PointF()) pts.Select(Function(q) New PointF(CSng(q.X * s), q.Y)).ToArray()
            r.Layers.Add(Poly(fg, m(SweepBrow(1.0)), mirror:=False, blur:=0.006))
            r.Layers.Add(Stroke(&HC8202A, 0.022, m(Closed(SweepBrow(1.0))), mirror:=False))
            r.Layers.Add(Stroke(fg, 0.022, m(Spiral(0.98, -0.12, 0.09, 0.01, 1.3, 270)), mirror:=False))
            r.Layers.Add(Stroke(fg, 0.07, m(Curve(False, 0.66, 0.24, 0.8, 0.5, 0.82, 0.82, 0.72, 1.08, 0.6, 1.16, 0.56, 1.06, 0.64, 1.02)), mirror:=False, taper:=True))
        Next
        Dim fl = Curve(True, 0, -0.08, 0.09, -0.3, 0.14, -0.58, 0.08, -0.82, 0.12, -1.0, 0, -1.12)
        r.Layers.Add(Poly(&HC8202A, fl, blur:=0.006))
        r.Layers.Add(Metal(Stroke(Gold, 0.022, Closed(fl))))
        r.Layers.Add(Outline(&HC8202A, 0.03, 1.25, 0.05))
        r.Layers.Add(Lips(&HC0202A, 1, 1, 0.95))
        list.Add(r)

        ' 10 黃臉：火焰眼、臉頰外側黑紋夾紅、紅鼻頭、紅元寶
        r = New OperaRole With {.Name = "黃臉", .Group = OperaGroup.Chuan, .Lift = 50,
                                .Note = "黃臉：額頭一排紅點、紅火焰印堂，火焰眼、臉頰外側黑紋夾紅線、紅鼻頭、紅元寶下巴。驍勇、性格暴躁的角色。"}
        AddOrnate(r, New Ornate With {.Base = &HF0C428, .Col = Column.Dots, .ColOuter = &HD8202A, .Eye = EyeStyle.Fire, .EyeAccent = &HD8202A,
                                      .Cheek = CheekStyle.Folds, .CheekAccent = &HD8202A, .Nose = NoseStyle.RedTip, .Chin = ChinStyle.RedYuanbao})
        r.Layers.Add(Poly(&HD8202A, Curve(True, 0, -0.1, 0.07, -0.3, 0.1, -0.52, 0.04, -0.62, 0, -0.66), blur:=0.006))
        list.Add(r)

        ' 11 粉臉：白葉眼斑、下半臉紅、白下巴
        r = New OperaRole With {.Name = "粉臉", .Group = OperaGroup.Chuan, .Lift = 45,
                                .Note = "粉紅臉：紅色水滴印堂、黑掃帚眉、白葉眼斑、白鼻樑，鼻子以下整塊紅、白下巴。年老或帶喜氣的武將。"}
        AddOrnate(r, New Ornate With {.Base = &HEE9AAE, .Col = Column.Drop, .ColOuter = &HC8202A, .ColInner = Paper,
                                      .Cheek = CheekStyle.LowerBlock, .CheekColor = &HC8202A, .CheekAccent = Ink})
        list.Add(r)

        ' 12～14 川丑：共用的華麗小花臉（捲曲小眉、額頭小花、雙圈腮紅加捲紋、嘴角上翹的小捲），各自的鼻樑白塊不同
        Dim chou = Sub(rr As OperaRole, patch As PointF(), mirrorPatch As Boolean)
                       rr.Layers.Add(Fill(&HF0DCCC, 0.45, 0.1))
                       ' 腮紅：大圓紅暈＋細黑捲紋圍一圈
                       rr.Layers.Add(Rouge(&HE84050, 0.7, 0.52, 0.17, 0.17, 0.7))
                       rr.Layers.Add(Stroke(Ink, 0.016, Spiral(0.7, 0.52, 0.2, 0.05, 1.3, 200)))
                       rr.Layers.Add(Oval(&HD82A2A, 0.86, 0.3, 0.03, 0.03, True, 1, 0.006))
                       ' 額頭：紅色三瓣小花＋兩側黑捲
                       For Each pt In {(X:=0.0, Y:=-0.82), (X:=0.07, Y:=-0.74), (X:=-0.07, Y:=-0.74)}
                           rr.Layers.Add(Oval(&HD82A2A, pt.X, pt.Y, 0.05, 0.05, False, 1, 0.006))
                       Next
                       rr.Layers.Add(Oval(&HF2C430, 0, -0.77, 0.025, 0.025, False, 1, 0.004))
                       rr.Layers.Add(Stroke(Ink, 0.016, Spiral(0.24, -0.72, 0.08, 0.01, 1.2, 180)))
                       ' 鼻樑白塊（黑邊＋內圈紅細線）
                       rr.Layers.Add(Poly(&HFBFAF7, patch, mirror:=mirrorPatch, op:=0.97, blur:=0.008))
                       rr.Layers.Add(Stroke(Ink, 0.022, Closed(patch), mirror:=mirrorPatch))
                       ' 眼尾往太陽穴的細捲、上眼皮細線
                       rr.Layers.Add(Stroke(Ink, 0.018, Curve(False, 0.72, -0.04, 0.88, -0.1, 0.96, -0.02, 0.92, 0.06, 0.86, 0.02)))
                       rr.Layers.Add(Outline(Ink, 0.012, 1.1))
                       ' 嘴角上翹的小捲（笑臉，不是鬍子）
                       rr.Layers.Add(Stroke(Ink, 0.018, Curve(False, 0.3, 1.04, 0.38, 0.98, 0.42, 0.9, 0.38, 0.86, 0.34, 0.9)))
                       rr.Layers.Add(Lips(&HD8283A, 0.9, 0.95, 0.85))
                   End Sub

        ' 12 川丑・豆腐乾
        r = New OperaRole With {.Name = "川丑・豆腐乾", .Group = OperaGroup.Chuan, .Lift = 0,
                                .Note = "川丑最典型的小花臉：鼻樑一塊花邊白豆腐乾，裡面畫捲曲小眉、小眼、紅鼻頭，額頭紅花、雙頰紅暈加捲紋。"}
        Dim tofu = Curve(True, 0, -0.42, 0.12, -0.36, 0.24, -0.4, 0.22, -0.24, 0.26, -0.08, 0.2, 0.08, 0.24, 0.26, 0.2, 0.44, 0.24, 0.58, 0.1, 0.56, 0, 0.62,
                         -0.1, 0.56, -0.24, 0.58, -0.2, 0.44, -0.24, 0.26, -0.2, 0.08, -0.26, -0.08, -0.22, -0.24, -0.24, -0.4, -0.12, -0.36)
        chou(r, tofu, False)
        r.Layers.Add(Stroke(Ink, 0.02, Spiral(0.11, -0.22, 0.06, 0.01, 1.2, 180)))
        r.Layers.Add(Stroke(Ink, 0.016, Curve(False, 0.05, 0.04, 0.1, 0.0, 0.16, 0.04)))
        r.Layers.Add(Oval(&HD82A2A, 0, 0.48, 0.05, 0.04, False, 1, 0.006))
        list.Add(r)

        ' 13 川丑・銅錢
        r = New OperaRole With {.Name = "川丑・銅錢", .Group = OperaGroup.Chuan, .Lift = 0,
                                .Note = "白鼻塊中間一枚金銅錢（外圈花邊、方孔），上下各一個捲紋：貪財、勢利的市井小人物。"}
        Dim coinPatch = Arc(0, 0.12, 0.26, 0.44, 0, 360, 48).Select(Function(q, i) New PointF(CSng(q.X * (1 + 0.06 * Math.Cos(i * Math.PI / 3))), CSng(0.12 + (q.Y - 0.12) * (1 + 0.04 * Math.Cos(i * Math.PI / 3))))).ToArray()
        chou(r, coinPatch, False)
        r.Layers.Add(Metal(Oval(Gold, 0, 0.14, 0.15, 0.15, False, 1, 0.006)))
        r.Layers.Add(Stroke(Ink, 0.018, Arc(0, 0.14, 0.15, 0.15, 0, 360), mirror:=False))
        r.Layers.Add(Stroke(Ink, 0.01, Arc(0, 0.14, 0.12, 0.12, 0, 360), mirror:=False))
        r.Layers.Add(Poly(Ink, P(-0.045, 0.095, 0.045, 0.095, 0.045, 0.185, -0.045, 0.185), mirror:=False))
        r.Layers.Add(Stroke(Ink, 0.016, Spiral(0.0, -0.18, 0.06, 0.01, 1.2, 90, False), mirror:=False))
        r.Layers.Add(Stroke(Ink, 0.016, Spiral(0.0, 0.42, 0.05, 0.01, 1.2, 270), mirror:=False))
        list.Add(r)

        ' 14 川丑・蝙蝠
        r = New OperaRole With {.Name = "川丑・蝙蝠", .Group = OperaGroup.Chuan, .Lift = 0,
                                .Note = "鼻樑畫成展翅的白蝙蝠（描黑邊、翅膀有骨紋），翅膀伸到眼下，紅鼻頭：機靈、會鑽營的小人物。"}
        Dim bat = Move(BatHalf, 0, 0.16, 0.62, 0.5)
        chou(r, bat, True)
        For Each v In {P(0.06, 0.08, 0.3, -0.02, 0.5, -0.06), P(0.08, 0.14, 0.26, 0.12, 0.42, 0.18)}
            r.Layers.Add(Stroke(Ink, 0.012, v))
        Next
        r.Layers.Add(Oval(&HD82A2A, 0, 0.5, 0.045, 0.035, False, 1, 0.006))
        list.Add(r)

        ' 15 青白鬼臉
        r = New OperaRole With {.Name = "青白鬼臉", .Group = OperaGroup.Chuan, .Lift = -10,
                                .Note = "青白的臉、發黑下垂的眼眶、臉頰凹陷、暗紫唇，額前幾縷亂髮。冤魂、鬼魂一類的角色。"}
        r.Layers.Add(Fill(&HD6E4E2, 0.9))
        r.Layers.Add(Socket(&H3A2A48, 1.6, 1.8, -0.12, 0.85, 0.06))
        r.Layers.Add(Rouge(&H7A8AA4, 0.72, 0.5, 0.24, 0.34, 0.45))
        r.Layers.Add(Stroke(&H2A2A30, 0.035, Curve(False, 0.12, -0.3, 0.5, -0.36, 0.9, -0.24), taper:=True))
        r.Layers.Add(Stroke(Ink, 0.035, Curve(False, 0.22, -1.3, 0.28, -0.95, 0.18, -0.65, 0.3, -0.36), mirror:=False, taper:=True))
        r.Layers.Add(Stroke(Ink, 0.03, Curve(False, -0.5, -1.25, -0.6, -0.9, -0.52, -0.6, -0.66, -0.38), mirror:=False, taper:=True))
        r.Layers.Add(Stroke(Ink, 0.025, Curve(False, 0.62, -1.2, 0.7, -0.92, 0.64, -0.66), mirror:=False, taper:=True))
        r.Layers.Add(Lips(&H4A2A48, 1, 1, 0.85))
        list.Add(r)
    End Sub
End Class
