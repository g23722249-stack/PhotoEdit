Imports System.Drawing

''' <summary>戲曲角色的分組。</summary>
Public Enum OperaGroup
    ''' <summary>俊扮（生、旦）：美化的妝。</summary>
    Junban = 0
    ''' <summary>臉譜（淨）：整張臉畫成另一張臉。</summary>
    Lianpu = 1
    ''' <summary>丑角。</summary>
    Chou = 2
    ''' <summary>川劇：臉上畫具象圖形（蝴蝶、蝙蝠、火焰、雲紋…）、金銀色、粗黑勾邊、陰陽臉。</summary>
    Chuan = 3
End Enum

Public Enum OperaLayerKind
    ''' <summary>整張臉（臉的輪廓往上延伸到髮際）。</summary>
    FaceFill
    ''' <summary>標準臉座標的多邊形。</summary>
    Poly
    ''' <summary>標準臉座標的折線（Width 粗細，Taper 尾端漸細）。</summary>
    Stroke
    ''' <summary>橢圓：Pts(0)＝中心，Sx/Sy＝半徑，Angle＝角度（度，往外轉為正）。</summary>
    Ellipse
    ''' <summary>依實際眼睛輪廓放大（Sx 橫、Sy 直；Lift＝外眼角往上拉，負的往下垂）。</summary>
    EyeSocket
    ''' <summary>實際眼睛輪廓放大 Sx 倍後描一圈線（Width）。</summary>
    EyeOutline
    ''' <summary>沿實際上眼皮的眼線，眼尾拉到 Pts(0)（標準座標，右眼；左眼自動鏡像）。</summary>
    EyeLine
    ''' <summary>實際嘴唇（以嘴的中心縮放 Sx、Sy）。</summary>
    Lips
    ''' <summary>亮片：在 Pts 多邊形範圍內撒亮點。</summary>
    Glitter
End Enum

''' <summary>
''' 戲曲妝的一層。座標是「標準臉」：兩眼中心的中點為原點，兩眼中心距離為 1，x 往畫面右、y 往下；
''' 嘴的中心在 (0, 1.05)。畫的時候用兩眼與嘴三點對應成仿射轉換，臉歪、臉長臉短都會跟著走。
''' Mirror＝定義在右半邊（x &gt; 0），左邊自動鏡像。
''' </summary>
Public NotInheritable Class OperaLayer
    Public Kind As OperaLayerKind
    Public ColorArgb As Integer
    Public Opacity As Double = 1.0
    ''' <summary>邊緣柔化（眼距的倍數）。</summary>
    Public Blur As Double = 0.012
    Public Width As Double = 0.04
    Public Pts As PointF() = {}
    Public Mirror As Boolean = True
    Public Taper As Boolean
    Public Sx As Double = 1.0
    Public Sy As Double = 1.0
    Public Lift As Double
    Public Angle As Double
    ''' <summary>保留原圖明暗（像畫在臉上）；頭髮、片子、髯口不用。</summary>
    Public Shade As Boolean = True
    ''' <summary>只畫在臉的範圍內（片子、髯口不限）。</summary>
    Public ClipFace As Boolean = True
    ''' <summary>眼睛本身（眼白、黑眼珠）不塗。</summary>
    Public KeepEyes As Boolean = True
    ''' <summary>嘴巴裡面（牙齒）不塗。</summary>
    Public KeepMouth As Boolean = True
    ''' <summary>金屬光澤（金、銀）：依原圖明暗加強對比，亮的地方反白光。</summary>
    Public Metal As Boolean

    Public ReadOnly Property Color As Color
        Get
            Return Color.FromArgb(ColorArgb)
        End Get
    End Property
End Class

''' <summary>一個戲曲角色：名稱、分組、說明、各層的妝，以及預設要不要片子、髯口、吊眉。</summary>
Public NotInheritable Class OperaRole
    Public Name As String
    Public Group As OperaGroup
    Public Note As String
    Public Layers As New List(Of OperaLayer)()
    Public Pian As Boolean
    ''' <summary>髯口顏色（0＝預設沒有髯口）。</summary>
    Public BeardArgb As Integer
    ''' <summary>髯口樣式：0 三綹（三髯）、1 滿髯（整片蓋住嘴）、2 紮髯（分兩片、嘴露出來）。</summary>
    Public BeardStyle As Integer
    Public Lift As Integer = 50
    ''' <summary>俊扮加假睫毛（歌仔戲）。</summary>
    Public Lashes As Boolean
End Class

''' <summary>
''' 京劇、歌仔戲、川劇的角色妝（俊扮、臉譜、丑角、川劇）。圖樣是依傳統臉譜的構圖重新畫的簡化版。
''' BeautySettings.OperaRole 是這裡的索引＋1（0＝沒有）。
''' </summary>
Partial Public NotInheritable Class OperaRoles
    Private Sub New()
    End Sub

    Public Shared ReadOnly All As IReadOnlyList(Of OperaRole) = Build()

    Public Shared Function [Get](roleId As Integer) As OperaRole
        Return If(roleId >= 1 AndAlso roleId <= All.Count, All(roleId - 1), Nothing)
    End Function

    Public Shared Function IdOf(name As String) As Integer
        For i = 0 To All.Count - 1
            If All(i).Name = name Then Return i + 1
        Next
        Return 0
    End Function

    '-----------------------------------------------------------------
    ' 建構用的小工具
    '-----------------------------------------------------------------

    Private Shared Function Rgb(hex As Integer) As Integer
        Return Color.FromArgb(255, (hex >> 16) And 255, (hex >> 8) And 255, hex And 255).ToArgb()
    End Function

    Private Shared Function P(ParamArray v As Double()) As PointF()
        Dim r(v.Length \ 2 - 1) As PointF
        For i = 0 To r.Length - 1
            r(i) = New PointF(CSng(v(2 * i)), CSng(v(2 * i + 1)))
        Next
        Return r
    End Function

    Private Shared Function Fill(hex As Integer, Optional op As Double = 0.92, Optional blur As Double = 0.06) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.FaceFill, .ColorArgb = Rgb(hex), .Opacity = op, .Blur = blur, .Mirror = False}
    End Function

    Private Shared Function Poly(hex As Integer, pts As PointF(), Optional mirror As Boolean = True, Optional op As Double = 1.0, Optional blur As Double = 0.012) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.Poly, .ColorArgb = Rgb(hex), .Pts = pts, .Mirror = mirror, .Opacity = op, .Blur = blur}
    End Function

    Private Shared Function Stroke(hex As Integer, width As Double, pts As PointF(), Optional mirror As Boolean = True, Optional taper As Boolean = False,
                                   Optional op As Double = 1.0) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.Stroke, .ColorArgb = Rgb(hex), .Width = width, .Pts = pts, .Mirror = mirror, .Taper = taper,
                                    .Opacity = op, .Blur = 0.008}
    End Function

    Private Shared Function Oval(hex As Integer, cx As Double, cy As Double, rx As Double, ry As Double, Optional mirror As Boolean = True,
                                 Optional op As Double = 1.0, Optional blur As Double = 0.012, Optional angle As Double = 0) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.Ellipse, .ColorArgb = Rgb(hex), .Pts = P(cx, cy), .Sx = rx, .Sy = ry, .Mirror = mirror,
                                    .Opacity = op, .Blur = blur, .Angle = angle}
    End Function

    ''' <summary>胭脂：很柔的橢圓團。</summary>
    Private Shared Function Rouge(hex As Integer, cx As Double, cy As Double, rx As Double, ry As Double, op As Double, Optional angle As Double = 0) As OperaLayer
        Return Oval(hex, cx, cy, rx, ry, True, op, Math.Max(rx, ry) * 0.55, angle)
    End Function

    Private Shared Function Socket(hex As Integer, sx As Double, sy As Double, Optional lift As Double = 0, Optional op As Double = 1.0, Optional blur As Double = 0.012) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.EyeSocket, .ColorArgb = Rgb(hex), .Sx = sx, .Sy = sy, .Lift = lift, .Opacity = op, .Blur = blur}
    End Function

    Private Shared Function Outline(hex As Integer, width As Double, scale As Double, Optional lift As Double = 0) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.EyeOutline, .ColorArgb = Rgb(hex), .Width = width, .Sx = scale, .Sy = scale, .Lift = lift, .Blur = 0.006}
    End Function

    Private Shared Function EyeLine(hex As Integer, width As Double, toX As Double, toY As Double) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.EyeLine, .ColorArgb = Rgb(hex), .Width = width, .Pts = P(toX, toY), .Blur = 0.006}
    End Function

    Private Shared Function Lips(hex As Integer, Optional sx As Double = 1.0, Optional sy As Double = 1.0, Optional op As Double = 0.92) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.Lips, .ColorArgb = Rgb(hex), .Sx = sx, .Sy = sy, .Opacity = op, .Mirror = False, .Blur = 0.01}
    End Function

    Private Shared Function Glitter(pts As PointF()) As OperaLayer
        Return New OperaLayer With {.Kind = OperaLayerKind.Glitter, .ColorArgb = Rgb(&HFFFFFF), .Pts = pts, .Opacity = 0.9, .Shade = False}
    End Function

    ' 常用形狀
    Private Shared ReadOnly Forehead As PointF() = P(0, -1.35, 0.55, -1.25, 0.85, -0.95, 0.95, -0.55, 0.9, -0.38, 0.55, -0.42, 0.18, -0.32, 0, -0.3)

    ''' <summary>俊扮共用：白粉底、眼周胭脂（眼皮最深，暈到太陽穴與臉頰）、蓋眉、鼻樑提白。</summary>
    Private Shared Sub JunbanBase(r As OperaRole, baseHex As Integer, rougeHex As Integer, rougeOp As Double, Optional baseOp As Double = 0.72)
        r.Layers.Add(Fill(baseHex, baseOp, 0.1))
        ' 蓋掉原本的眉毛（之後重畫）
        r.Layers.Add(Poly(baseHex, P(0.12, -0.24, 0.5, -0.36, 0.92, -0.32, 0.95, -0.2, 0.5, -0.22, 0.14, -0.15), op:=0.75, blur:=0.04))
        r.Layers.Add(Rouge(rougeHex, 0.6, 0.18, 0.42, 0.55, rougeOp, 12))
        r.Layers.Add(Rouge(rougeHex, 0.62, -0.08, 0.32, 0.2, rougeOp * 0.8, 10))
        r.Layers.Add(Rouge(rougeHex, 0.92, -0.18, 0.2, 0.3, rougeOp * 0.6, 20))
        r.Layers.Add(Oval(&HFFFFFF, 0, 0.25, 0.06, 0.35, False, 0.35, 0.05))
    End Sub

    Private Shared Sub WillowBrows(r As OperaRole, hex As Integer)
        r.Layers.Add(Stroke(hex, 0.045, P(0.14, -0.28, 0.42, -0.4, 0.72, -0.44, 0.98, -0.52), taper:=True))
    End Sub

    Private Shared Sub SwordBrows(r As OperaRole, hex As Integer, Optional width As Double = 0.07)
        r.Layers.Add(Stroke(hex, width, P(0.12, -0.26, 0.5, -0.4, 0.98, -0.56), taper:=True))
    End Sub

    Private Shared Sub PhoenixEyes(r As OperaRole, Optional wingY As Double = -0.24)
        r.Layers.Add(EyeLine(&H141010, 0.035, 1.06, wingY))
    End Sub

    Private Shared Sub Yintang(r As OperaRole, Optional len As Double = 0.32)
        r.Layers.Add(Stroke(&HC8202E, 0.055, P(0, -0.26, 0, -0.26 - len), mirror:=False, taper:=True, op:=0.85))
    End Sub

    '-----------------------------------------------------------------
    ' 角色
    '-----------------------------------------------------------------

    Private Shared Function Build() As IReadOnlyList(Of OperaRole)
        Dim list As New List(Of OperaRole)()
        Dim r As OperaRole

        ' ===== 俊扮 =====
        r = New OperaRole With {.Name = "京劇青衣", .Group = OperaGroup.Junban, .Pian = True, .Lift = 65,
                                .Note = "白底、眼周桃紅胭脂往太陽穴暈開、鳳眼、柳葉眉、櫻桃小口、貼片子。端莊的正旦。"}
        JunbanBase(r, &HF7EEE9, &HE0567A, 0.6)
        WillowBrows(r, &H1A1414)
        PhoenixEyes(r)
        r.Layers.Add(Lips(&HEADBD3, 1.05, 1.05, 0.7))
        r.Layers.Add(Lips(&HC4122E, 0.5, 0.85))
        list.Add(r)

        r = New OperaRole With {.Name = "京劇花旦", .Group = OperaGroup.Junban, .Pian = True, .Lift = 60,
                                .Note = "活潑的少女：胭脂更粉更大片、眼睛更亮、唇色較豔。"}
        JunbanBase(r, &HF8EDEA, &HEE5C8C, 0.7)
        WillowBrows(r, &H1A1414)
        PhoenixEyes(r, -0.2)
        r.Layers.Add(Lips(&HEADBD3, 1.05, 1.05, 0.7))
        r.Layers.Add(Lips(&HD8183A, 0.58, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "刀馬旦（穆桂英）", .Group = OperaGroup.Junban, .Pian = True, .Lift = 75,
                                .Note = "武將女角：胭脂偏紅、眉眼更上挑、劍眉帶英氣。"}
        JunbanBase(r, &HF5ECE6, &HD8364A, 0.72)
        SwordBrows(r, &H151111, 0.055)
        PhoenixEyes(r, -0.32)
        r.Layers.Add(Lips(&HEADBD3, 1.05, 1.05, 0.7))
        r.Layers.Add(Lips(&HC0102A, 0.6, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "京劇老旦", .Group = OperaGroup.Junban, .Lift = 0,
                                .Note = "老婦人：幾乎不上胭脂、不吊眉，眉眼平順，略畫皺紋。"}
        r.Layers.Add(Fill(&HEBDACB, 0.4, 0.1))
        r.Layers.Add(Stroke(&H5A4A40, 0.03, P(0.15, -0.3, 0.5, -0.36, 0.88, -0.3), taper:=True, op:=0.8))
        r.Layers.Add(Stroke(&H8A6E60, 0.012, P(0.25, 0.68, 0.42, 0.95), op:=0.45))
        r.Layers.Add(Lips(&HA85A5A, 1, 1, 0.45))
        list.Add(r)

        r = New OperaRole With {.Name = "京劇小生", .Group = OperaGroup.Junban, .Lift = 60,
                                .Note = "年輕書生：胭脂偏紅較淡、劍眉上挑、印堂一道紅，嘴唇正常大小。"}
        JunbanBase(r, &HF4EBE4, &HD9484A, 0.55)
        SwordBrows(r, &H141010)
        PhoenixEyes(r, -0.28)
        Yintang(r)
        r.Layers.Add(Lips(&HB8303A, 0.9, 0.95, 0.8))
        list.Add(r)

        r = New OperaRole With {.Name = "京劇武生", .Group = OperaGroup.Junban, .Lift = 75,
                                .Note = "武將：劍眉更粗更挑、眼尾拉高，印堂紅較長，英武。"}
        JunbanBase(r, &HF2E6DD, &HCF3A3A, 0.6)
        SwordBrows(r, &H101010, 0.085)
        PhoenixEyes(r, -0.36)
        Yintang(r, 0.42)
        r.Layers.Add(Lips(&HA82A30, 0.95, 0.95, 0.8))
        list.Add(r)

        r = New OperaRole With {.Name = "京劇老生", .Group = OperaGroup.Junban, .Lift = 35, .BeardArgb = Rgb(&H202020), .BeardStyle = 0,
                                .Note = "中老年男角：淡胭脂偏膚色、印堂一點紅、戴三綹髯口（黑三）。"}
        JunbanBase(r, &HEED9C6, &HD98B70, 0.4, 0.5)
        SwordBrows(r, &H201A16, 0.055)
        PhoenixEyes(r, -0.2)
        Yintang(r, 0.18)
        list.Add(r)

        r = New OperaRole With {.Name = "歌仔戲小生", .Group = OperaGroup.Junban, .Lift = 75, .Lashes = True,
                                .Note = "多由女演員反串：眼周大片豔紅、劍眉誇張上挑、假睫毛、亮片、鼻影修容，舞台感強。"}
        JunbanBase(r, &HF8E8E9, &HE8467E, 0.78)
        r.Layers.Add(Oval(&H8A5A50, 0.13, 0.25, 0.05, 0.32, True, 0.4, 0.05))
        SwordBrows(r, &H0E0A0A, 0.08)
        PhoenixEyes(r, -0.38)
        Yintang(r, 0.25)
        r.Layers.Add(Glitter(P(0.3, -0.12, 0.85, -0.2, 0.85, 0.02, 0.3, 0.0)))
        r.Layers.Add(Lips(&HC0263C, 0.95, 0.95, 0.85))
        list.Add(r)

        r = New OperaRole With {.Name = "歌仔戲小旦", .Group = OperaGroup.Junban, .Lift = 60, .Lashes = True,
                                .Note = "粉嫩豔麗：粉紅胭脂、亮眼影、假睫毛，唇色比京劇亮。"}
        JunbanBase(r, &HF9ECEC, &HF06A9A, 0.72)
        WillowBrows(r, &H1A1414)
        PhoenixEyes(r, -0.22)
        r.Layers.Add(Glitter(P(0.3, -0.12, 0.85, -0.2, 0.85, 0.02, 0.3, 0.0)))
        r.Layers.Add(Lips(&HE0284E, 0.75, 0.95, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "歌仔戲苦旦", .Group = OperaGroup.Junban, .Lift = 50, .Lashes = True,
                                .Note = "悲情女角：胭脂偏淡粉、眉尾微垂、唇色淡，楚楚可憐。"}
        JunbanBase(r, &HF8F0EE, &HE68AA2, 0.5)
        r.Layers.Add(Stroke(&H2A2222, 0.04, P(0.14, -0.32, 0.45, -0.4, 0.75, -0.38, 0.98, -0.3), taper:=True))
        PhoenixEyes(r, -0.12)
        r.Layers.Add(Lips(&HC8586A, 0.7, 0.9, 0.85))
        list.Add(r)

        ' ===== 臉譜 =====
        r = New OperaRole With {.Name = "關公", .Group = OperaGroup.Lianpu, .Lift = 40, .BeardArgb = Rgb(&H151515), .BeardStyle = 1,
                                .Note = "紅整臉：忠義勇武。丹鳳眼、臥蠶眉，額上細紋，五綹長髯。"}
        r.Layers.Add(Fill(&HB3242B))
        r.Layers.Add(Stroke(&H141010, 0.13, P(0.1, -0.3, 0.45, -0.44, 0.8, -0.44, 1.0, -0.56), taper:=True))
        r.Layers.Add(EyeLine(&H141010, 0.05, 1.08, -0.3))
        r.Layers.Add(Stroke(&H5A0E12, 0.025, P(-0.4, -0.78, 0, -0.84, 0.4, -0.78), mirror:=False))
        r.Layers.Add(Stroke(&H5A0E12, 0.025, P(-0.32, -0.92, 0, -0.98, 0.32, -0.92), mirror:=False))
        r.Layers.Add(Stroke(&H5A0E12, 0.03, P(0.17, 0.6, 0.38, 0.92), op:=0.7))
        r.Layers.Add(Lips(&H5A0E12, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "包公", .Group = OperaGroup.Lianpu, .Lift = 20, .BeardArgb = Rgb(&H151515), .BeardStyle = 1,
                                .Note = "黑整臉：剛正不阿、鐵面無私。額頭白月牙，白色笑眉。"}
        r.Layers.Add(Fill(&H1E1C1C, 0.95))
        r.Layers.Add(Poly(&HF4F0E8, P(-0.2, -0.86, -0.08, -0.72, 0.08, -0.72, 0.2, -0.86, 0.1, -0.8, -0.1, -0.8), mirror:=False))
        r.Layers.Add(Stroke(&HF4F0E8, 0.045, P(0.12, -0.24, 0.45, -0.42, 0.95, -0.3), taper:=True))
        r.Layers.Add(Outline(&HF4F0E8, 0.03, 1.35, 0.05))
        r.Layers.Add(Stroke(&HF4F0E8, 0.025, P(0.18, 0.62, 0.42, 0.9), op:=0.8))
        r.Layers.Add(Lips(&H3A0D0D, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "曹操", .Group = OperaGroup.Lianpu, .Lift = 55, .BeardArgb = Rgb(&H2A2A2A), .BeardStyle = 0,
                                .Note = "水白整臉：奸詐多疑。細長眉、三角眼、細密皺紋。"}
        r.Layers.Add(Fill(&HF2EFEA, 0.93))
        r.Layers.Add(Stroke(&H141414, 0.028, P(0.1, -0.26, 0.5, -0.4, 1.0, -0.56), taper:=True))
        r.Layers.Add(EyeLine(&H141414, 0.035, 1.05, -0.22))
        r.Layers.Add(Stroke(&H141414, 0.015, P(0.04, -0.26, 0.07, -0.58), op:=0.9))
        r.Layers.Add(Stroke(&H555555, 0.012, P(0.85, -0.05, 0.98, 0.08), op:=0.8))
        r.Layers.Add(Stroke(&H555555, 0.012, P(0.85, 0.02, 0.96, 0.16), op:=0.8))
        r.Layers.Add(Stroke(&H333333, 0.015, P(0.2, 0.6, 0.42, 0.98), op:=0.85))
        r.Layers.Add(Stroke(&H777777, 0.012, P(-0.35, -0.8, 0, -0.86, 0.35, -0.8), mirror:=False, op:=0.7))
        r.Layers.Add(Lips(&H8A1E22, 1, 1, 0.85))
        list.Add(r)

        r = New OperaRole With {.Name = "張飛", .Group = OperaGroup.Lianpu, .Lift = 40, .BeardArgb = Rgb(&H111111), .BeardStyle = 2,
                                .Note = "十字門黑白臉：勇猛豪爽。黑色蝴蝶眼窩、額到鼻一道黑，笑口。"}
        r.Layers.Add(Fill(&HEDE9E2, 0.92))
        r.Layers.Add(Poly(&H151515, P(-0.11, -1.3, 0.11, -1.3, 0.09, 0.6, -0.09, 0.6), mirror:=False))
        r.Layers.Add(Poly(&H151515, P(0.13, 0.12, 0.12, -0.2, 0.3, -0.62, 0.58, -0.7, 0.86, -0.5, 1.0, -0.15, 0.98, 0.18, 0.72, 0.36, 0.42, 0.34)))
        r.Layers.Add(Outline(&HEDE9E2, 0.045, 1.3, 0.05))
        r.Layers.Add(Stroke(&HEDE9E2, 0.03, P(0.3, -0.45, 0.58, -0.56, 0.84, -0.42), taper:=True))
        r.Layers.Add(Oval(&H151515, 0, 1.08, 0.55, 0.32, False))
        r.Layers.Add(Lips(&HB82228, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "竇爾敦", .Group = OperaGroup.Lianpu, .Lift = 45, .BeardArgb = Rgb(&H1A1A1A), .BeardStyle = 1,
                                .Note = "藍臉：剛烈桀驁的綠林好漢。額上紅色虎頭雙鉤、黑色大眼窩。"}
        r.Layers.Add(Fill(&H2C5DA8))
        r.Layers.Add(Socket(&H151515, 1.75, 2.3, 0.18))
        r.Layers.Add(Outline(&HF4F0E8, 0.03, 1.2))
        r.Layers.Add(Stroke(&H151515, 0.1, P(0.1, -0.3, 0.45, -0.52, 0.95, -0.46), taper:=True))
        r.Layers.Add(Stroke(&HC8202A, 0.06, P(0.05, -0.55, 0.22, -0.82, 0.12, -1.02, -0.02, -0.92)))
        r.Layers.Add(Oval(&H151515, 0, 0.62, 0.16, 0.1, False))
        r.Layers.Add(Oval(&H151515, 0, 1.08, 0.5, 0.28, False))
        r.Layers.Add(Lips(&H8A1E22, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "典韋", .Group = OperaGroup.Lianpu, .Lift = 45, .BeardArgb = Rgb(&H1A1A1A), .BeardStyle = 2,
                                .Note = "黃臉：兇猛彪悍。黑色火焰眉、大眼窩、額頭黑紋。"}
        r.Layers.Add(Fill(&HD9A92A))
        r.Layers.Add(Socket(&H151515, 1.6, 2.0, 0.12))
        r.Layers.Add(Outline(&HF4F0E8, 0.03, 1.15))
        r.Layers.Add(Poly(&H151515, P(0.08, -0.32, 0.3, -0.62, 0.42, -0.5, 0.55, -0.74, 0.66, -0.56, 0.86, -0.7, 1.0, -0.42, 0.6, -0.4, 0.2, -0.24)))
        r.Layers.Add(Stroke(&H151515, 0.05, P(0, -0.7, 0, -1.15), mirror:=False, taper:=True))
        r.Layers.Add(Oval(&H151515, 0, 0.62, 0.15, 0.09, False))
        r.Layers.Add(Oval(&H151515, 0, 1.08, 0.5, 0.28, False))
        r.Layers.Add(Lips(&HA8202A, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "孫悟空", .Group = OperaGroup.Lianpu, .Lift = 30,
                                .Note = "象形猴臉：金色底，紅色桃心形包住眼鼻，白色猴臉，金睛火眼。"}
        r.Layers.Add(Fill(&HD9A63A))
        r.Layers.Add(Poly(&HC8302A, P(0, -0.9, 0.42, -0.86, 0.8, -0.55, 0.92, -0.05, 0.75, 0.55, 0.4, 0.9, 0, 0.98), mirror:=True))
        r.Layers.Add(Poly(&HF2E2BE, P(0, -0.42, 0.25, -0.36, 0.62, -0.28, 0.72, 0.1, 0.6, 0.45, 0.3, 0.8, 0, 0.86), mirror:=True))
        r.Layers.Add(Outline(&H151515, 0.04, 1.35))
        r.Layers.Add(Socket(&HE8B428, 1.25, 1.6, 0, 0.6))
        r.Layers.Add(Stroke(&H151515, 0.03, P(0.15, -0.5, 0.5, -0.62, 0.8, -0.5), taper:=True))
        r.Layers.Add(Stroke(&H151515, 0.025, P(0.12, 0.6, 0.3, 0.72, 0.45, 0.94)))
        r.Layers.Add(Lips(&HC8302A, 1, 1, 0.85))
        list.Add(r)

        r = New OperaRole With {.Name = "鍾馗", .Group = OperaGroup.Lianpu, .Lift = 50, .BeardArgb = Rgb(&H151515), .BeardStyle = 2,
                                .Note = "紅黑碎臉：捉鬼的判官。額頭黑蝙蝠、黑色火焰眼窩。"}
        r.Layers.Add(Fill(&HB52A2A))
        r.Layers.Add(Socket(&H151515, 1.8, 2.2, 0.25))
        r.Layers.Add(Outline(&HF4F0E8, 0.03, 1.2, 0.05))
        r.Layers.Add(Poly(&H151515, P(0, -0.62, 0.15, -0.75, 0.3, -0.7, 0.48, -0.92, 0.42, -0.62, 0.62, -0.68, 0.38, -0.5, 0.12, -0.55)))
        r.Layers.Add(Stroke(&HF4F0E8, 0.04, P(0.18, -0.38, 0.6, -0.5, 0.95, -0.4), taper:=True))
        r.Layers.Add(Oval(&H151515, 0, 0.62, 0.17, 0.1, False))
        r.Layers.Add(Oval(&H151515, 0, 1.08, 0.55, 0.3, False))
        r.Layers.Add(Lips(&HC8202A, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "項羽", .Group = OperaGroup.Lianpu, .Lift = 0, .BeardArgb = Rgb(&H151515), .BeardStyle = 1,
                                .Note = "黑白哭臉（霸王）：眼窩往下垂、壽字眉，悲壯的英雄。"}
        r.Layers.Add(Fill(&HEEEAE4, 0.92))
        r.Layers.Add(Socket(&H151515, 1.6, 2.3, -0.3))
        r.Layers.Add(Outline(&HEEEAE4, 0.035, 1.2, -0.08))
        r.Layers.Add(Stroke(&H151515, 0.09, P(0.08, -0.36, 0.5, -0.56, 1.0, -0.26), taper:=True))
        r.Layers.Add(Stroke(&H151515, 0.07, P(0, -0.32, 0, -1.1), mirror:=False))
        r.Layers.Add(Stroke(&H151515, 0.05, P(-0.18, -0.6, 0.18, -0.6), mirror:=False))
        r.Layers.Add(Stroke(&H151515, 0.05, P(-0.14, -0.84, 0.14, -0.84), mirror:=False))
        r.Layers.Add(Oval(&H151515, 0, 0.62, 0.15, 0.09, False))
        r.Layers.Add(Oval(&H151515, 0, 1.1, 0.48, 0.28, False))
        r.Layers.Add(Lips(&HA01E24, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "姜維", .Group = OperaGroup.Lianpu, .Lift = 45, .BeardArgb = Rgb(&H151515), .BeardStyle = 0,
                                .Note = "紅三塊瓦：智勇雙全。額頭太極圖（諸葛亮的傳人）。"}
        r.Layers.Add(Fill(&HB8282E))
        r.Layers.Add(Socket(&H151515, 1.45, 1.9, 0.15))
        r.Layers.Add(Outline(&HF4F0E8, 0.03, 1.15))
        r.Layers.Add(Stroke(&H151515, 0.08, P(0.08, -0.32, 0.5, -0.5, 0.98, -0.46), taper:=True))
        r.Layers.Add(Oval(&HF4F0E8, 0, -0.78, 0.17, 0.17, False))
        r.Layers.Add(Poly(&H151515, P(0, -0.95, 0.12, -0.9, 0.17, -0.78, 0.12, -0.66, 0, -0.61, 0.06, -0.7, 0.06, -0.86), mirror:=False))
        r.Layers.Add(Poly(&H151515, P(0, -0.95, -0.06, -0.86, -0.06, -0.78, 0, -0.78), mirror:=False))
        r.Layers.Add(Stroke(&H151515, 0.025, P(0.2, 0.6, 0.42, 0.95)))
        r.Layers.Add(Lips(&H5A0E12, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "楊七郎", .Group = OperaGroup.Lianpu, .Lift = 40, .BeardArgb = Rgb(&H111111), .BeardStyle = 2,
                                .Note = "黑十字門：楊家將的猛將。額頭白底紅心一道，白色眼框。"}
        r.Layers.Add(Fill(&H1C1A1A, 0.95))
        r.Layers.Add(Poly(&HF2EEE6, P(-0.12, -1.25, 0.12, -1.25, 0.08, -0.25, -0.08, -0.25), mirror:=False))
        r.Layers.Add(Stroke(&HC8202A, 0.05, P(0, -1.15, 0, -0.35), mirror:=False, taper:=True))
        r.Layers.Add(Socket(&HF2EEE6, 1.3, 1.6, 0.15, 0.95))
        r.Layers.Add(Outline(&H151515, 0.03, 1.08))
        r.Layers.Add(Stroke(&HF2EEE6, 0.05, P(0.18, -0.36, 0.55, -0.55, 0.95, -0.45), taper:=True))
        r.Layers.Add(Oval(&HF2EEE6, 0, 1.08, 0.5, 0.3, False, 0.95))
        r.Layers.Add(Lips(&HA01E24, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "單雄信", .Group = OperaGroup.Lianpu, .Lift = 45, .BeardArgb = Rgb(&H151515), .BeardStyle = 1,
                                .Note = "藍花臉：瓦崗寨好漢、性情剛烈。紅色火焰紋、白眼窩。"}
        r.Layers.Add(Fill(&H3F7FC1))
        r.Layers.Add(Socket(&HF2EEE6, 1.35, 1.7, 0.15, 0.95))
        r.Layers.Add(Outline(&H151515, 0.035, 1.1))
        r.Layers.Add(Stroke(&H151515, 0.09, P(0.08, -0.3, 0.5, -0.56, 0.98, -0.48), taper:=True))
        r.Layers.Add(Poly(&HC8202A, P(0, -0.55, 0.1, -0.7, 0.05, -0.9, 0.15, -1.1, 0, -1.0, -0.15, -1.1, -0.05, -0.9, -0.1, -0.7), mirror:=False))
        r.Layers.Add(Oval(&H151515, 0, 0.62, 0.15, 0.09, False))
        r.Layers.Add(Oval(&H151515, 0, 1.08, 0.5, 0.28, False))
        r.Layers.Add(Lips(&H8A1E22, 1, 1, 0.9))
        list.Add(r)

        r = New OperaRole With {.Name = "豬八戒", .Group = OperaGroup.Lianpu, .Lift = 0,
                                .Note = "象形豬臉：粉色底、大鼻頭兩個鼻孔，眼睛小、眉毛短。"}
        r.Layers.Add(Fill(&HE6AFAA))
        r.Layers.Add(Oval(&HF4D2CC, 0, 0.72, 0.4, 0.3, False))
        r.Layers.Add(Oval(&H2A1414, 0.13, 0.72, 0.06, 0.09))
        r.Layers.Add(Outline(&H151515, 0.03, 1.1))
        r.Layers.Add(Stroke(&H151515, 0.06, P(0.2, -0.32, 0.45, -0.42, 0.7, -0.34)))
        r.Layers.Add(Stroke(&H151515, 0.02, P(-0.25, -0.8, 0, -0.88, 0.25, -0.8), mirror:=False))
        r.Layers.Add(Lips(&HB04048, 1, 1, 0.8))
        list.Add(r)

        r = New OperaRole With {.Name = "二郎神", .Group = OperaGroup.Lianpu, .Lift = 55,
                                .Note = "粉金臉：額頭第三隻眼（天眼），眉眼英挺。"}
        r.Layers.Add(Fill(&HE9B98F, 0.85))
        r.Layers.Add(Socket(&HD8A23A, 1.35, 1.6, 0.18, 0.7))
        r.Layers.Add(Outline(&H151515, 0.035, 1.15, 0.12))
        r.Layers.Add(Stroke(&H151515, 0.07, P(0.12, -0.32, 0.55, -0.52, 1.0, -0.6), taper:=True))
        r.Layers.Add(Oval(&HC8202A, 0, -0.68, 0.09, 0.2, False))
        r.Layers.Add(Oval(&HF6F2EA, 0, -0.68, 0.055, 0.15, False))
        r.Layers.Add(Oval(&H151515, 0, -0.68, 0.035, 0.05, False))
        r.Layers.Add(Lips(&HC0202E, 1, 1, 0.9))
        list.Add(r)

        ' ===== 丑角 =====
        r = New OperaRole With {.Name = "京劇文丑", .Group = OperaGroup.Chou, .Lift = 0,
                                .Note = "小花臉：鼻樑到兩眼之間一塊白色豆腐塊，裡面畫小眉小眼。"}
        r.Layers.Add(Fill(&HEBD7C6, 0.3, 0.1))
        r.Layers.Add(Poly(&HFBFAF7, P(-0.3, -0.22, 0.3, -0.22, 0.27, 0.56, -0.27, 0.56), mirror:=False, op:=0.96, blur:=0.008))
        r.Layers.Add(Stroke(&H141414, 0.025, P(0.08, -0.12, 0.24, -0.16)))
        r.Layers.Add(Stroke(&H141414, 0.02, P(0.02, 0.5, 0.12, 0.42)))
        list.Add(r)

        r = New OperaRole With {.Name = "京劇武丑", .Group = OperaGroup.Chou, .Lift = 0,
                                .Note = "元寶形白塊，加上細密的黑紋，機靈矯健（如時遷）。"}
        r.Layers.Add(Fill(&HE8D3C2, 0.3, 0.1))
        r.Layers.Add(Poly(&HFBFAF7, P(-0.42, -0.3, -0.18, -0.18, 0.18, -0.18, 0.42, -0.3, 0.3, 0.2, 0.16, 0.6, -0.16, 0.6, -0.3, 0.2), mirror:=False, op:=0.96, blur:=0.008))
        r.Layers.Add(Stroke(&H141414, 0.02, P(0.1, -0.12, 0.3, -0.22)))
        r.Layers.Add(Stroke(&H141414, 0.015, P(0.2, 0.0, 0.35, 0.12)))
        r.Layers.Add(Stroke(&H141414, 0.015, P(0.16, 0.25, 0.3, 0.34)))
        r.Layers.Add(Oval(&H141414, 0, 0.6, 0.05, 0.03, False))
        list.Add(r)

        r = New OperaRole With {.Name = "歌仔戲三花", .Group = OperaGroup.Chou, .Lift = 0,
                                .Note = "鼻樑一塊白、紅鼻頭、臉頰黑痣、八字鬍，生活化的逗趣。"}
        r.Layers.Add(Fill(&HEBD7C6, 0.25, 0.1))
        r.Layers.Add(Oval(&HFBFAF7, 0, 0.18, 0.24, 0.45, False, 0.95, 0.01))
        r.Layers.Add(Oval(&HD8323A, 0, 0.6, 0.11, 0.09, False, 0.95))
        r.Layers.Add(Oval(&H141414, 0.5, 0.78, 0.035, 0.035, False))
        r.Layers.Add(Stroke(&H141414, 0.03, P(0.04, 0.88, 0.2, 0.86, 0.34, 0.94), taper:=True))
        list.Add(r)

        r = New OperaRole With {.Name = "彩旦", .Group = OperaGroup.Chou, .Pian = True, .Lift = 30,
                                .Note = "丑化的女角（媒婆）：兩頰大圓紅腮、嘴邊大黑痣、紅唇。"}
        r.Layers.Add(Fill(&HF2E4DA, 0.55, 0.1))
        r.Layers.Add(Oval(&HE03A3A, 0.68, 0.55, 0.22, 0.22, True, 0.75, 0.06))
        r.Layers.Add(Stroke(&H1A1414, 0.03, P(0.15, -0.45, 0.5, -0.6, 0.85, -0.45), taper:=True))
        r.Layers.Add(Oval(&H141414, 0.42, 1.12, 0.05, 0.05, False))
        r.Layers.Add(Lips(&HD8182E, 1.1, 1.1, 0.95))
        list.Add(r)

        BuildChuan(list)
        Return list
    End Function
End Class
