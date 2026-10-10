Imports System.Drawing
Imports System.Runtime.InteropServices
Imports OpenCvSharp

''' <summary>顏色類美顏共用的工作資料：縮小的臉（BGR、Lab）、皮膚遮罩、修正量、五官點。</summary>
Friend NotInheritable Class MakeupCtx
    Public W, H, N As Integer
    Public Src As Byte()       ' BGR
    Public Lab As Byte()       ' L, a, b（8 位元）
    Public Skin As Byte()      ' 柔和皮膚遮罩 0..255
    Public Acc As Single()     ' BGR 修正量
    Public DLab As Single()    ' Lab 修正量
    Public EyeDist As Double
    Public Lm As Point2f()
    Public Dense As Point2f()
    Public Mesh As Point2f()
    Public UpX, UpY As Double  ' 臉的「上」方向（單位向量）
    Public ML, MA, MB As Double ' 皮膚平均 Lab

    Public Sub AddLab(i As Integer, dl As Double, da As Double, db As Double)
        DLab(i * 3) += CSng(dl) : DLab(i * 3 + 1) += CSng(da) : DLab(i * 3 + 2) += CSng(db)
    End Sub

    ''' <summary>點 p 往臉的上方移 d（負值往下）。</summary>
    Public Function Up(p As Point2f, d As Double) As Point2f
        Return New Point2f(CSng(p.X + UpX * d), CSng(p.Y + UpY * d))
    End Function

    ' 目前的顏色（原色＋前面已經上的妝）：後上的妝要疊在前面的妝上（白眼影上的黑眼線才會是黑的）
    Public Function L(i As Integer) As Double
        Return Lab(i * 3) + DLab(i * 3)
    End Function
    Public Function A(i As Integer) As Double
        Return Lab(i * 3 + 1) + DLab(i * 3 + 1)
    End Function
    Public Function B(i As Integer) As Double
        Return Lab(i * 3 + 2) + DLab(i * 3 + 2)
    End Function
End Class

''' <summary>美妝：口紅、眉、眼影、眼線、雙眼皮、臥蠶、高光、腮紅（縮小的臉上算），睫毛、美瞳（原圖解析度上直接畫）。</summary>
Partial Public NotInheritable Class PortraitRetouch

    ''' <summary>網格的眉毛：上緣、下緣（由內往外），兩道眉。</summary>
    Private Shared ReadOnly MeshBrowUpper As Integer()() = {New Integer() {107, 66, 105, 63, 70}, New Integer() {336, 296, 334, 293, 300}}
    Private Shared ReadOnly MeshBrowLower As Integer()() = {New Integer() {55, 65, 52, 53, 46}, New Integer() {285, 295, 282, 283, 276}}

    ''' <summary>有沒有要變形的（臉型、嘴角、豐唇、開眼角、眉形）。</summary>
    Private Shared Function NeedsShapeWarp(b As BeautySettings) As Boolean
        Return b.FaceSlim <> 0 OrElse b.VFace <> 0 OrElse b.Chin <> 0 OrElse b.NoseSlim <> 0 OrElse b.Smile <> 0 OrElse b.LipFull <> 0 OrElse
               b.EyeCorner <> 0 OrElse (b.Brows > 0 AndAlso (b.BrowStyle <> BrowStyle.Darken OrElse b.BrowThick <> 50)) OrElse
               (b.HasOpera AndAlso b.OperaLift > 0)
    End Function

    ''' <summary>有沒有任何要在縮小的臉上算的顏色類效果。</summary>
    Private Shared Function NeedsColorDelta(b As BeautySettings, hasDense As Boolean) As Boolean
        If b.Even <> 0 OrElse b.Redness <> 0 OrElse b.Whiten <> 0 OrElse b.Tone <> 0 OrElse b.Shine <> 0 OrElse b.Blemish <> 0 OrElse
           b.DarkCircles <> 0 OrElse b.Teeth <> 0 OrElse b.Blush <> 0 OrElse b.Contour <> 0 OrElse b.HasLight Then Return True
        Return hasDense AndAlso (b.Lips <> 0 OrElse b.Brows <> 0 OrElse b.EyeShadow <> 0 OrElse b.EyeLiner <> 0 OrElse b.EyeBag <> 0 OrElse
                                 b.Fold <> 0 OrElse b.Highlight <> 0 OrElse b.UnderEye <> 0 OrElse b.WhiteNose <> 0)
    End Function

    ''' <summary>Lab 階段的美妝（需要特徵點）：口紅、眉毛、眼影、眼線、臥蠶、雙眼皮、高光。</summary>
    Private Shared Sub ApplyMakeupLab(c As MakeupCtx, b As BeautySettings)
        If c.Dense Is Nothing Then Return
        If b.Lips > 0 Then LipMakeup(c, b)
        If b.Brows > 0 Then BrowMakeup(c, b)
        Dim eyes = EyeShapes(c.Dense, c.Mesh)
        If b.EyeShadow > 0 Then ShadowMakeup(c, b, eyes)
        If b.Fold > 0 Then FoldMakeup(c, b, eyes)
        If b.EyeLiner > 0 Then LinerMakeup(c, b, eyes)
        If b.EyeBag > 0 Then EyeBagMakeup(c, b, eyes)
        If b.UnderEye > 0 Then UnderEyeMakeup(c, b, eyes)
        If b.Highlight > 0 Then HighlightMakeup(c, b)
        If b.WhiteNose > 0 Then WhiteNoseMakeup(c, b)
    End Sub

    '---------------------------------------------------------------------
    ' 口紅
    '---------------------------------------------------------------------

    Private Shared Sub LipMakeup(c As MakeupCtx, b As BeautySettings)
        Dim amt = b.Lips / 100.0
        Dim outer = LipOuter(c.Dense, c.Mesh), inner = LipInner(c.Dense, c.Mesh)
        Dim region = PolyMask(c.W, c.H, outer, inner, c.EyeDist * 0.02)
        Dim t = ColorToLab(b.LipColor)
        ' 嘴巴中心與大小（咬唇、漸層用）
        Dim cx = outer.Average(Function(p) p.X), cy = outer.Average(Function(p) p.Y)
        Dim halfW = Math.Max(2.0, (outer.Max(Function(p) p.X) - outer.Min(Function(p) p.X)) / 2)
        Dim halfH = Math.Max(2.0, (outer.Max(Function(p) p.Y) - outer.Min(Function(p) p.Y)) / 2)
        Dim innerGlow As Single() = Nothing
        If b.LipStyle = LipStyle.Gradient Then
            innerGlow = BlurArray(PolyMask(c.W, c.H, inner, Nothing, 0), c.W, c.H, c.EyeDist * 0.07)
            Dim mx = Math.Max(0.0001F, innerGlow.Max())
            For i = 0 To innerGlow.Length - 1
                innerGlow(i) = Math.Min(1.0F, innerGlow(i) / mx * 1.6F)
            Next
        End If
        Dim lineMask As Single() = Nothing
        If b.LipStyle = LipStyle.Liner Then
            lineMask = New Single(c.N - 1) {}
            For j = 0 To outer.Length - 1
                Dim p = outer(j), q = outer((j + 1) Mod outer.Length)
                DrawSoftLine(lineMask, c.W, c.H, p.X, p.Y, q.X, q.Y, c.EyeDist * 0.014)
            Next
        End If
        ' 唇色平均亮度：嘴角、齒縫的暗處不是唇，淡色唇套上去會變灰綠的黑影
        Dim sumL = 0.0, sumW = 0.0
        For i = 0 To c.N - 1
            If region(i) > 0.5 Then sumL += c.L(i) * region(i) : sumW += region(i)
        Next
        Dim lipL = If(sumW > 0, sumL / sumW, 50.0)
        For i = 0 To c.N - 1
            Dim r = region(i)
            If r < 0.002 AndAlso (lineMask Is Nothing OrElse lineMask(i) < 0.002) Then Continue For
            Dim darkGate = Math.Min(1.0, Math.Max(0.0, (c.L(i) - lipL * 0.45) / (lipL * 0.3)))
            r = CSng(r * darkGate)
            Dim wgt = 1.0
            Select Case b.LipStyle
                Case LipStyle.Bitten
                    Dim dx = (i Mod c.W - cx) / (halfW * 0.55), dy = (i \ c.W - cy) / (halfH * 1.1)
                    wgt = 0.2 + 0.8 * Math.Exp(-(dx * dx + dy * dy))
                Case LipStyle.Gradient
                    wgt = 0.3 + 0.7 * innerGlow(i)
                Case LipStyle.Liner
                    wgt = 0.55
            End Select
            Dim k = r * wgt * amt
            Dim L = c.L(i)
            Dim matte = If(b.LipStyle = LipStyle.Matte, 0.08, 0.0) ' 霧面：壓一點反光
            ' 淡色（粉白、裸色）唇：亮度也要往目標拉多一點，才蓋得過原本的紅
            Dim lw = If(t.L > L, 0.6, 0.25), cw = If(t.L > L, 0.85, 0.7)
            c.AddLab(i, (t.L - L) * lw * k - Math.Max(0, L - c.ML) * matte * k, (t.A - c.A(i)) * cw * k, (t.B - c.B(i)) * cw * k)
            If lineMask IsNot Nothing AndAlso lineMask(i) > 0.002 Then
                Dim lk = lineMask(i) * amt
                c.AddLab(i, (t.L * 0.7 - L) * 0.45 * lk, (t.A - c.A(i)) * 0.6 * lk, (t.B - c.B(i)) * 0.6 * lk)
            End If
        Next
        ' 光澤：下唇中間（水潤一定有）
        Dim gloss = Math.Max(b.LipGloss, If(b.LipStyle = LipStyle.Glossy, 55, 0)) / 100.0
        If gloss > 0 AndAlso b.LipStyle <> LipStyle.Matte Then
            Dim lowIn = If(c.Mesh IsNot Nothing, c.Mesh(14), c.Dense(66)), lowOut = If(c.Mesh IsNot Nothing, c.Mesh(17), c.Dense(57))
            Dim gx = (lowIn.X + lowOut.X) / 2, gy = lowIn.Y + (lowOut.Y - lowIn.Y) * 0.45
            Dim hl(c.N - 1) As Single
            FillSoftEllipse(hl, c.W, c.H, gx, gy, halfW * 0.32, Math.Max(1.5, Math.Abs(lowOut.Y - lowIn.Y) * 0.22))
            Dim upC = If(c.Mesh IsNot Nothing, c.Mesh(0), c.Dense(51))
            FillSoftEllipse(hl, c.W, c.H, upC.X, upC.Y + halfH * 0.18, halfW * 0.14, Math.Max(1.0, halfH * 0.1))
            hl = BlurArray(hl, c.W, c.H, c.EyeDist * 0.012)
            For i = 0 To c.N - 1
                Dim k = hl(i) * region(i) * gloss * amt
                If k < 0.002 Then Continue For
                c.AddLab(i, (255 - c.L(i)) * 0.45 * k, -(c.A(i) - 128) * 0.15 * k, -(c.B(i) - 128) * 0.15 * k)
            Next
        End If
    End Sub

    '---------------------------------------------------------------------
    ' 眉毛（顏色、加深；形狀在 WarpFaceShape）
    '---------------------------------------------------------------------

    Private Shared Sub BrowMakeup(c As MakeupCtx, b As BeautySettings)
        Dim amt = b.Brows / 100.0
        Dim region(c.N - 1) As Single
        If c.Mesh IsNot Nothing Then
            For k = 0 To 1
                Dim poly = MeshBrowUpper(k).Select(Function(i) c.Mesh(i)).Concat(MeshBrowLower(k).Reverse().Select(Function(i) c.Mesh(i))).ToArray()
                Dim m1 = PolyMask(c.W, c.H, poly, Nothing, c.EyeDist * 0.02)
                For i = 0 To c.N - 1
                    If m1(i) > region(i) Then region(i) = m1(i)
                Next
            Next
        Else
            For Each start In {17, 22}
                For j = start To start + 3
                    DrawSoftLine(region, c.W, c.H, c.Dense(j).X, c.Dense(j).Y, c.Dense(j + 1).X, c.Dense(j + 1).Y, c.EyeDist * 0.045)
                Next
            Next
            region = BlurArray(region, c.W, c.H, c.EyeDist * 0.02)
        End If
        Dim t = ColorToLab(b.BrowColor)
        For i = 0 To c.N - 1
            Dim r = region(i) * amt
            If r < 0.002 Then Continue For
            Dim L = c.L(i)
            ' 本來就暗的（眉毛）加深多一點，眉毛之間的皮膚補淡淡的顏色（稀疏的眉毛變完整）
            Dim hairy = 0.35 + 0.65 * Smooth(c.ML - L, 4, 40)
            Dim k = r * hairy
            c.AddLab(i, -L * 0.3 * k + (t.L - L) * 0.1 * k, (t.A - c.A(i)) * 0.45 * k, (t.B - c.B(i)) * 0.45 * k)
        Next
    End Sub

    '---------------------------------------------------------------------
    ' 眼妝
    '---------------------------------------------------------------------

    Private Shared Sub ShadowMakeup(c As MakeupCtx, b As BeautySettings, eyes As (Upper As Point2f(), Lower As Point2f(), Outer As Point2f, Inner As Point2f, Poly As Point2f(), OuterFirst As Boolean)())
        Dim amt = b.EyeShadow / 100.0
        Dim style = b.ShadowStyle
        If style = ShadowStyle.Panda Then
            PandaShadow(c, b, eyes, amt)
            Return
        End If
        Dim spread = (0.5 + b.ShadowSpread / 100.0) * If(style = ShadowStyle.Smoky, 1.35, 1.0)
        Dim t = ColorToLab(b.EyeShadowColor)
        Dim deep = (L:=t.L * If(style = ShadowStyle.Smoky, 0.55, 0.78), A:=t.A, B:=t.B) ' 貼眼皮那層較深
        Dim lowBand(c.N - 1) As Single, highBand(c.N - 1) As Single, lowerLid(c.N - 1) As Single
        For Each e In eyes
            Dim lid = e.Upper
            Dim last = lid.Length - 1
            Dim lift = Function(j As Integer, f As Double) c.EyeDist * (0.1 + 0.1 * Math.Sin(Math.PI * j / last)) * spread * f
            Dim oi = If(e.OuterFirst, 0, last)
            Dim outerDir = If(e.Outer.X < e.Inner.X, -1, 1)
            Dim widen = Function(pts As Point2f()) As Point2f()
                            pts(oi) = New Point2f(CSng(pts(oi).X + outerDir * c.EyeDist * 0.06 * spread), pts(oi).Y)
                            Return pts
                        End Function
            Dim top = widen(lid.Select(Function(p, j) c.Up(p, lift(j, 1.0))).ToArray())
            Dim mid = widen(lid.Select(Function(p, j) c.Up(p, lift(j, 0.5))).ToArray())
            Dim hi = PolyMask(c.W, c.H, lid.Concat(top.Reverse()).ToArray(), e.Poly, c.EyeDist * 0.05)
            Dim lo = PolyMask(c.W, c.H, lid.Concat(mid.Reverse()).ToArray(), e.Poly, c.EyeDist * 0.035)
            For i = 0 To c.N - 1
                If hi(i) > highBand(i) Then highBand(i) = hi(i)
                If lo(i) > lowBand(i) Then lowBand(i) = lo(i)
            Next
            ' 煙燻：下眼皮也暈開；桃花：延伸到下眼尾
            If style = ShadowStyle.Smoky OrElse style = ShadowStyle.Peach Then
                Dim low = e.Lower
                Dim n = low.Length - 1
                For j = 0 To n - 1
                    Dim tPos = j / CDbl(n)
                    Dim fromOuter = If(e.OuterFirst, 1 - tPos, tPos) ' Lower 是內→外（第一眼）或外→內（第二眼）
                    If style = ShadowStyle.Peach AndAlso fromOuter < 0.45 Then Continue For
                    Dim a1 = c.Up(low(j), -c.EyeDist * 0.05), a2 = c.Up(low(j + 1), -c.EyeDist * 0.05)
                    DrawSoftLine(lowerLid, c.W, c.H, a1.X, a1.Y, a2.X, a2.Y, c.EyeDist * 0.04)
                Next
            End If
        Next
        lowerLid = BlurArray(lowerLid, c.W, c.H, c.EyeDist * 0.02)
        Dim twoTone = style = ShadowStyle.Gradient OrElse style = ShadowStyle.Smoky
        For i = 0 To c.N - 1
            Dim r = Math.Max(highBand(i), lowerLid(i) * 0.7) * amt
            If r < 0.002 Then Continue For
            Dim L = c.L(i)
            Dim tl = t.L, ta = t.A, tb = t.B
            If twoTone AndAlso lowBand(i) > 0 Then
                Dim d = lowBand(i)
                tl = tl + (deep.L - tl) * d : ta = ta + (deep.A - ta) * d : tb = tb + (deep.B - tb) * d
            End If
            c.AddLab(i, (tl - L) * 0.32 * r, (ta - c.A(i)) * 0.6 * r, (tb - c.B(i)) * 0.6 * r)
        Next
        ' 閃粉：眼影範圍裡一些亮點（固定亂數，預覽與匯出一樣）
        Dim glitter = Math.Max(b.ShadowGlitter, If(style = ShadowStyle.Glitter, 55, 0)) / 100.0
        If glitter > 0 Then
            Dim rnd As New Random(1234)
            Dim spots(c.N - 1) As Single
            Dim count = CInt(c.EyeDist * c.EyeDist * 0.25 * glitter)
            For k = 1 To count
                Dim i = rnd.Next(c.N)
                If highBand(i) < 0.3 Then Continue For
                spots(i) = CSng(0.5 + rnd.NextDouble() * 0.5)
            Next
            spots = BlurArray(spots, c.W, c.H, 0.6)
            For i = 0 To c.N - 1
                Dim s = Math.Min(1.0, spots(i) * 3) * amt
                If s < 0.01 Then Continue For
                c.AddLab(i, (255 - c.L(i)) * 0.7 * s, 0, 0)
            Next
        End If
    End Sub

    Private Shared Sub LinerMakeup(c As MakeupCtx, b As BeautySettings, eyes As (Upper As Point2f(), Lower As Point2f(), Outer As Point2f, Inner As Point2f, Poly As Point2f(), OuterFirst As Boolean)())
        Dim amt = b.EyeLiner / 100.0
        Dim style = b.LinerStyle
        Dim width = c.EyeDist * (0.008 + 0.022 * b.LinerWidth / 100.0) * If(style = LinerStyle.Cat, 1.4, If(style = LinerStyle.Inner, 0.6, 1.0))
        Dim wing = c.EyeDist * 0.18 * b.LinerWing / 100.0 * If(style = LinerStyle.Cat, 1.3, If(style = LinerStyle.Winged, 1.0, 0.0))
        Dim region(c.N - 1) As Single
        For Each e In eyes
            If style = LinerStyle.Lower Then
                ' 下眼線：從眼尾畫到下眼皮約三分之二
                Dim low = e.Lower
                Dim n = low.Length - 1
                For j = 0 To n - 1
                    Dim tPos = j / CDbl(n)
                    Dim fromOuter = If(e.OuterFirst, 1 - tPos, tPos)
                    If fromOuter < 0.3 Then Continue For
                    DrawSoftLine(region, c.W, c.H, low(j).X, low(j).Y, low(j + 1).X, low(j + 1).Y, width * 0.8)
                Next
                Continue For
            End If
            Dim lid = e.Upper
            Dim shift = If(style = LinerStyle.Inner, -width * 0.6, 0.0) ' 內眼線貼睫毛根部（稍微往下）
            For j = 0 To lid.Length - 2
                Dim p = c.Up(lid(j), shift), q = c.Up(lid(j + 1), shift)
                DrawSoftLine(region, c.W, c.H, p.X, p.Y, q.X, q.Y, width)
            Next
            If wing > 0 Then
                Dim o = e.Outer
                Dim outerDir = If(e.Outer.X < e.Inner.X, -1, 1)
                ' 眼尾上揚、由粗漸細，接在眼線末端（不會像一截分開的線）
                Dim start = c.Up(o, width * 0.3)
                Dim tip = c.Up(New Point2f(CSng(o.X + outerDir * wing), o.Y), wing * 0.5)
                Const segs = 6
                For s = 0 To segs - 1
                    Dim t0 = s / CDbl(segs), t1 = (s + 1) / CDbl(segs)
                    DrawSoftLine(region, c.W, c.H, start.X + (tip.X - start.X) * t0, start.Y + (tip.Y - start.Y) * t0,
                                 start.X + (tip.X - start.X) * t1, start.Y + (tip.Y - start.Y) * t1, width * (1.0 - 0.7 * t0))
                Next
            End If
        Next
        Dim t = ColorToLab(b.LinerColor)
        For i = 0 To c.N - 1
            Dim r = Math.Min(1, region(i) * 1.3) * amt
            If r < 0.002 Then Continue For
            c.AddLab(i, (t.L - c.L(i)) * 0.8 * r, (t.A - c.A(i)) * 0.6 * r, (t.B - c.B(i)) * 0.6 * r)
        Next
    End Sub

    Private Shared Sub EyeBagMakeup(c As MakeupCtx, b As BeautySettings, eyes As (Upper As Point2f(), Lower As Point2f(), Outer As Point2f, Inner As Point2f, Poly As Point2f(), OuterFirst As Boolean)())
        Dim amt = b.EyeBag / 100.0
        Dim hi(c.N - 1) As Single, lo(c.N - 1) As Single
        For Each e In eyes
            Dim lid = e.Lower
            For j = 0 To lid.Length - 2
                Dim a1 = c.Up(lid(j), -c.EyeDist * 0.07), a2 = c.Up(lid(j + 1), -c.EyeDist * 0.07)
                DrawSoftLine(hi, c.W, c.H, a1.X, a1.Y, a2.X, a2.Y, c.EyeDist * 0.04)
                Dim c1 = c.Up(lid(j), -c.EyeDist * 0.15), c2 = c.Up(lid(j + 1), -c.EyeDist * 0.15)
                DrawSoftLine(lo, c.W, c.H, c1.X, c1.Y, c2.X, c2.Y, c.EyeDist * 0.025)
            Next
        Next
        For i = 0 To c.N - 1
            If hi(i) < 0.002 AndAlso lo(i) < 0.002 Then Continue For
            Dim L = c.L(i)
            c.AddLab(i, ((255 - L) * 0.16 * hi(i) - L * 0.08 * lo(i)) * amt, 0, 0)
        Next
    End Sub

    ''' <summary>眼下打亮：下眼皮下方一大片（中間最寬）往白色提亮、降低彩度（辣妹妝的眼下白），眼睛本身不動。</summary>
    Private Shared Sub UnderEyeMakeup(c As MakeupCtx, b As BeautySettings, eyes As (Upper As Point2f(), Lower As Point2f(), Outer As Point2f, Inner As Point2f, Poly As Point2f(), OuterFirst As Boolean)())
        Dim amt = b.UnderEye / 100.0
        Dim region(c.N - 1) As Single
        For Each e In eyes
            Dim low = e.Lower
            Dim last = low.Length - 1
            Dim bottom = low.Select(Function(p, j) c.Up(p, -c.EyeDist * (0.06 + 0.26 * Math.Sin(Math.PI * j / last)))).ToArray()
            Dim m1 = PolyMask(c.W, c.H, low.Concat(bottom.Reverse()).ToArray(), e.Poly, c.EyeDist * 0.07)
            For i = 0 To c.N - 1
                If m1(i) > region(i) Then region(i) = m1(i)
            Next
        Next
        For i = 0 To c.N - 1
            Dim r = region(i) * amt
            If r < 0.002 Then Continue For
            Dim L = c.L(i)
            c.AddLab(i, (255 - L) * 0.5 * r, (128 - c.A(i)) * 0.45 * r, (128 - c.B(i)) * 0.45 * r)
        Next
    End Sub

    ''' <summary>熊貓白：眼窩到眉下、眼下到眼袋一大圈塗成（接近）純白，眼睛本身和眉毛不塗。</summary>
    Private Shared Sub PandaShadow(c As MakeupCtx, b As BeautySettings, eyes As (Upper As Point2f(), Lower As Point2f(), Outer As Point2f, Inner As Point2f, Poly As Point2f(), OuterFirst As Boolean)(), amt As Double)
        Dim d = c.EyeDist
        Dim spread = 0.7 + 0.6 * b.ShadowSpread / 100.0
        Dim region(c.N - 1) As Single
        For Each e In eyes
            Dim outerDir = If(e.Outer.X < e.Inner.X, -1, 1)
            Dim up = e.Upper, low = e.Lower
            Dim lu = up.Length - 1, ll = low.Length - 1
            Dim top = up.Select(Function(p, j) c.Up(p, d * (0.1 + 0.15 * Math.Sin(Math.PI * j / lu)) * spread)).ToArray()
            Dim bottom = low.Select(Function(p, j) c.Up(p, -d * (0.08 + 0.2 * Math.Sin(Math.PI * j / ll)) * spread)).ToArray()
            ' 眼尾往外拉開一大截、眼頭一點點（Upper 第一眼是外→內，Lower 是內→外，接起來剛好一圈）
            Dim pushCorner = Function(pts As Point2f(), idx As Integer, outer As Boolean, lift As Double) As Point2f()
                                 Dim dx = If(outer, outerDir * d * 0.14, -outerDir * d * 0.04) * spread
                                 pts(idx) = c.Up(New Point2f(CSng(pts(idx).X + dx), pts(idx).Y), lift)
                                 Return pts
                             End Function
            Dim upOuter = If(e.OuterFirst, 0, lu), upInner = If(e.OuterFirst, lu, 0)
            Dim lowOuter = If(e.OuterFirst, ll, 0), lowInner = If(e.OuterFirst, 0, ll)
            top = pushCorner(pushCorner(top, upOuter, True, d * 0.03), upInner, False, 0)
            bottom = pushCorner(pushCorner(bottom, lowOuter, True, -d * 0.02), lowInner, False, 0)
            Dim m1 = PolyMask(c.W, c.H, top.Concat(bottom).ToArray(), e.Poly, d * 0.06)
            For i = 0 To c.N - 1
                If m1(i) > region(i) Then region(i) = m1(i)
            Next
        Next
        ' 眉毛不塗白
        Dim brow = BrowRegion(c)
        Dim t = ColorToLab(b.EyeShadowColor)
        ' 選的顏色只帶一點色調，主體是白
        Dim tl = 250 + (t.L - 250) * 0.25, ta = 128 + (t.A - 128) * 0.25, tb = 128 + (t.B - 128) * 0.25
        For i = 0 To c.N - 1
            Dim r = region(i) * (1 - brow(i)) * amt
            If r < 0.002 Then Continue For
            c.AddLab(i, (tl - c.L(i)) * 0.85 * r, (ta - c.A(i)) * 0.85 * r, (tb - c.B(i)) * 0.85 * r)
        Next
    End Sub

    ''' <summary>眉毛範圍（0..1，邊緣柔和）。</summary>
    Private Shared Function BrowRegion(c As MakeupCtx) As Single()
        Dim region(c.N - 1) As Single
        If c.Mesh IsNot Nothing Then
            For k = 0 To 1
                Dim poly = MeshBrowUpper(k).Select(Function(i) c.Mesh(i)).Concat(MeshBrowLower(k).Reverse().Select(Function(i) c.Mesh(i))).ToArray()
                Dim m1 = PolyMask(c.W, c.H, poly, Nothing, c.EyeDist * 0.03)
                For i = 0 To c.N - 1
                    If m1(i) > region(i) Then region(i) = m1(i)
                Next
            Next
        Else
            For Each start In {17, 22}
                For j = start To start + 3
                    DrawSoftLine(region, c.W, c.H, c.Dense(j).X, c.Dense(j).Y, c.Dense(j + 1).X, c.Dense(j + 1).Y, c.EyeDist * 0.05)
                Next
            Next
            region = BlurArray(region, c.W, c.H, c.EyeDist * 0.02)
        End If
        Return region
    End Function

    ''' <summary>白鼻樑：兩眼中間一筆白色畫到鼻尖，上窄下寬，不管膚色遮罩（就是要蓋上去）。</summary>
    Private Shared Sub WhiteNoseMakeup(c As MakeupCtx, b As BeautySettings)
        Dim amt = b.WhiteNose / 100.0
        Dim d = c.EyeDist
        Dim p = Function(meshIdx As Integer, denseIdx As Integer) If(c.Mesh IsNot Nothing, c.Mesh(meshIdx), c.Dense(denseIdx))
        Dim top = c.Up(p(168, 27), d * 0.06), tip = p(4, 30)
        Dim region(c.N - 1) As Single
        Const segs = 8
        For s = 0 To segs - 1
            Dim t0 = s / CDbl(segs), t1 = (s + 1) / CDbl(segs)
            DrawSoftLine(region, c.W, c.H, top.X + (tip.X - top.X) * t0, top.Y + (tip.Y - top.Y) * t0,
                         top.X + (tip.X - top.X) * t1, top.Y + (tip.Y - top.Y) * t1, d * (0.045 + 0.04 * t0))
        Next
        region = BlurArray(region, c.W, c.H, d * 0.015)
        Dim t = ColorToLab(b.HighlightColor)
        Dim tl = 248.0, ta = 128 + (t.A - 128) * 0.3, tb = 128 + (t.B - 128) * 0.3
        For i = 0 To c.N - 1
            Dim r = region(i) * amt
            If r < 0.002 Then Continue For
            c.AddLab(i, (tl - c.L(i)) * 0.85 * r, (ta - c.A(i)) * 0.8 * r, (tb - c.B(i)) * 0.8 * r)
        Next
    End Sub

    ''' <summary>雙眼皮：上眼皮往上一條細摺痕（暗），摺痕上方一點點亮。</summary>
    Private Shared Sub FoldMakeup(c As MakeupCtx, b As BeautySettings, eyes As (Upper As Point2f(), Lower As Point2f(), Outer As Point2f, Inner As Point2f, Poly As Point2f(), OuterFirst As Boolean)())
        Dim amt = b.Fold / 100.0 * If(b.FoldStyle = FoldStyle.Inner, 0.6, If(b.FoldStyle = FoldStyle.Euro, 1.25, 1.0))
        Dim baseW = c.EyeDist * (0.04 + 0.08 * b.FoldWidth / 100.0) * If(b.FoldStyle = FoldStyle.Inner, 0.5, If(b.FoldStyle = FoldStyle.Euro, 1.5, 1.0))
        Dim crease(c.N - 1) As Single, lift(c.N - 1) As Single
        For Each e In eyes
            Dim lid = e.Upper
            Dim last = lid.Length - 1
            Dim pts = lid.Select(Function(p, j)
                                     Dim tPos = j / CDbl(last)
                                     Dim fromInner = If(e.OuterFirst, 1 - tPos, tPos)
                                     Dim off = baseW * If(b.FoldStyle = FoldStyle.Fan, 0.2 + 0.8 * fromInner, 1.0)
                                     Return c.Up(p, off)
                                 End Function).ToArray()
            For j = 0 To last - 1
                Dim taper = Math.Sin(Math.PI * (j + 0.5) / last) ' 兩端淡出
                Dim m1(c.N - 1) As Single
                DrawSoftLine(m1, c.W, c.H, pts(j).X, pts(j).Y, pts(j + 1).X, pts(j + 1).Y, c.EyeDist * 0.011)
                Dim a1 = c.Up(pts(j), c.EyeDist * 0.025), a2 = c.Up(pts(j + 1), c.EyeDist * 0.025)
                Dim m2(c.N - 1) As Single
                DrawSoftLine(m2, c.W, c.H, a1.X, a1.Y, a2.X, a2.Y, c.EyeDist * 0.02)
                For i = 0 To c.N - 1
                    Dim v1 = CSng(m1(i) * taper), v2 = CSng(m2(i) * taper)
                    If v1 > crease(i) Then crease(i) = v1
                    If v2 > lift(i) Then lift(i) = v2
                Next
            Next
        Next
        For i = 0 To c.N - 1
            If crease(i) < 0.002 AndAlso lift(i) < 0.002 Then Continue For
            Dim L = c.L(i)
            c.AddLab(i, (-L * 0.22 * crease(i) + (255 - L) * 0.05 * lift(i)) * amt, (c.MA - c.A(i)) * 0.0, 0)
        Next
    End Sub

    '---------------------------------------------------------------------
    ' 高光
    '---------------------------------------------------------------------

    Private Shared Sub HighlightMakeup(c As MakeupCtx, b As BeautySettings)
        Dim amt = b.Highlight / 100.0
        Dim region(c.N - 1) As Single
        Dim st = b.HighlightStyle
        Dim all = st = HighlightStyle.All
        Dim p = Function(meshIdx As Integer, denseIdx As Integer) If(c.Mesh IsNot Nothing, c.Mesh(meshIdx), c.Dense(denseIdx))
        Dim d = c.EyeDist
        If all OrElse st = HighlightStyle.Nose Then
            Dim a1 = p(168, 27), a2 = p(4, 30)
            DrawSoftLine(region, c.W, c.H, a1.X, a1.Y, a2.X, a2.Y, d * 0.05)
        End If
        If all OrElse st = HighlightStyle.Cheek Then
            ' 顴骨：眼尾下方偏外側
            Dim midX = (c.Lm(0).X + c.Lm(1).X) / 2
            For k = 0 To 1
                Dim eye = c.Lm(k)
                Dim outward = Math.Sign(eye.X - midX)
                Dim cpt = c.Up(New Point2f(CSng(eye.X + outward * d * 0.25), eye.Y), -d * 0.33)
                FillGaussian(region, c.W, c.H, cpt.X, cpt.Y, d * 0.11)
            Next
        End If
        If all OrElse st = HighlightStyle.BrowBone Then
            For Each pk In {p(105, 19), p(334, 24)}
                Dim cpt = c.Up(pk, -d * 0.09)
                FillGaussian(region, c.W, c.H, cpt.X, cpt.Y, d * 0.07)
            Next
        End If
        If all OrElse st = HighlightStyle.LipPeak Then
            Dim cpt = c.Up(p(0, 51), d * 0.05)
            FillGaussian(region, c.W, c.H, cpt.X, cpt.Y, d * 0.045)
        End If
        region = BlurArray(region, c.W, c.H, d * 0.02)
        Dim t = ColorToLab(b.HighlightColor)
        For i = 0 To c.N - 1
            Dim r = region(i) * amt * Math.Max(0.4, c.Skin(i) / 255.0)
            If r < 0.002 Then Continue For
            Dim L = c.L(i)
            c.AddLab(i, (255 - L) * 0.32 * r, (t.A - c.A(i)) * 0.25 * r, (t.B - c.B(i)) * 0.25 * r)
        Next
    End Sub

    '---------------------------------------------------------------------
    ' 腮紅（BGR 階段）
    '---------------------------------------------------------------------

    Private Shared Sub ApplyBlush(c As MakeupCtx, b As BeautySettings)
        Dim amt = b.Blush / 100.0
        Dim region(c.N - 1) As Single
        Dim d = c.EyeDist
        Dim sz = 0.6 + 0.8 * b.BlushSpread / 100.0
        Dim lm = c.Lm
        Dim midX = (lm(0).X + lm(1).X) / 2
        For e = 0 To 1
            Dim outward = Math.Sign(lm(e).X - midX)
            Select Case b.BlushStyle
                Case BlushStyle.Slant
                    ' 從笑肌斜上到太陽穴
                    For k = 0 To 4
                        Dim tPos = k / 4.0
                        Dim cx = lm(e).X + outward * d * (0.12 + 0.38 * tPos)
                        Dim cpt = c.Up(New Point2f(CSng(cx), lm(e).Y), -d * (0.62 - 0.4 * tPos))
                        FillGaussian(region, c.W, c.H, cpt.X, cpt.Y, d * 0.17 * sz)
                    Next
                Case BlushStyle.Sunburn
                    Dim cpt = c.Up(New Point2f(CSng(lm(e).X + outward * d * 0.05), lm(e).Y), -d * 0.5)
                    FillGaussian(region, c.W, c.H, cpt.X, cpt.Y, d * 0.24 * sz)
                Case BlushStyle.Tipsy
                    Dim cpt = c.Up(New Point2f(CSng(lm(e).X + outward * d * 0.05), lm(e).Y), -d * 0.42)
                    FillGaussian(region, c.W, c.H, cpt.X, cpt.Y, d * 0.38 * sz)
                Case Else
                    Dim cpt = c.Up(New Point2f(CSng(lm(e).X + outward * d * 0.12), lm(e).Y), -d * 0.62)
                    FillGaussian(region, c.W, c.H, cpt.X, cpt.Y, d * 0.34 * sz)
            End Select
        Next
        If b.BlushStyle = BlushStyle.Sunburn Then
            ' 橫過鼻樑
            Dim l0 = c.Up(lm(0), -d * 0.45), l1 = c.Up(lm(1), -d * 0.45)
            DrawSoftLine(region, c.W, c.H, l0.X, l0.Y, l1.X, l1.Y, d * 0.12 * sz)
        End If
        Dim col = b.BlushColor
        Dim strength = If(b.BlushStyle = BlushStyle.Tipsy, 0.38, 0.32)
        For i = 0 To c.N - 1
            Dim r = region(i) * (c.Skin(i) / 255.0) * amt * strength
            If r < 0.002 Then Continue For
            ' 色彩「乘上」再混：深膚色也自然
            Dim sb As Double = c.Src(i * 3), sg As Double = c.Src(i * 3 + 1), sr As Double = c.Src(i * 3 + 2)
            c.Acc(i * 3) += CSng((sb * col.B / 255.0 * 0.5 + col.B * 0.5 - sb) * r)
            c.Acc(i * 3 + 1) += CSng((sg * col.G / 255.0 * 0.5 + col.G * 0.5 - sg) * r)
            c.Acc(i * 3 + 2) += CSng((sr * col.R / 255.0 * 0.5 + col.R * 0.5 - sr) * r)
        Next
    End Sub

    '=====================================================================
    ' 立體光影：網格深度 → 表面朝向 → 依光的方向算明暗
    '=====================================================================

    ''' <summary>
    ''' 用網格 468 點做 Delaunay 三角形、把深度內插成高度圖、算出表面法線，依光源方向得到每個像素的亮度倍率（Gain）
    ''' 與網格涵蓋範圍（Cover，0..1，邊緣淡出）。林布蘭光：光從側前上方；側光：幾乎從正側面。
    ''' </summary>
    Private Shared Function MeshShade(mesh As Point2f(), meshZ As Single(), w As Integer, h As Integer, eyeDist As Double,
                                      kind As BeautyLight, fromRight As Boolean) As (Gain As Single(), Cover As Single())
        Dim n = w * h
        Dim height(n - 1) As Single, cover(n - 1) As Single
        Dim index As New Dictionary(Of Long, Integer)()
        Using sub2 As New Subdiv2D(New Rect(-2, -2, w + 4, h + 4))
            For i = 0 To Math.Min(467, mesh.Length - 1)
                Dim p As New Point2f(Math.Max(0, Math.Min(w - 1, mesh(i).X)), Math.Max(0, Math.Min(h - 1, mesh(i).Y)))
                Dim key = CLng(Math.Round(p.X * 8)) * 1000000L + CLng(Math.Round(p.Y * 8))
                If index.ContainsKey(key) Then Continue For
                index(key) = i
                sub2.Insert(p)
            Next
            Dim zOf = Function(x As Single, y As Single) As Double?
                          Dim key = CLng(Math.Round(x * 8)) * 1000000L + CLng(Math.Round(y * 8))
                          Dim i As Integer
                          If index.TryGetValue(key, i) Then Return -meshZ(i) ' 高度＝靠近鏡頭的程度
                          Return Nothing
                      End Function
            For Each t In sub2.GetTriangleList()
                Dim ax = t.Item0, ay = t.Item1, bx = t.Item2, by = t.Item3, cx = t.Item4, cy = t.Item5
                Dim za = zOf(ax, ay), zb = zOf(bx, by), zc = zOf(cx, cy)
                If Not (za.HasValue AndAlso zb.HasValue AndAlso zc.HasValue) Then Continue For
                Dim den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
                If Math.Abs(den) < 0.0001 Then Continue For
                For y = Math.Max(0, CInt(Math.Floor(Math.Min(ay, Math.Min(by, cy))))) To Math.Min(h - 1, CInt(Math.Ceiling(Math.Max(ay, Math.Max(by, cy)))))
                    For x = Math.Max(0, CInt(Math.Floor(Math.Min(ax, Math.Min(bx, cx))))) To Math.Min(w - 1, CInt(Math.Ceiling(Math.Max(ax, Math.Max(bx, cx)))))
                        Dim l1 = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / den
                        Dim l2 = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / den
                        Dim l3 = 1 - l1 - l2
                        If l1 < -0.001 OrElse l2 < -0.001 OrElse l3 < -0.001 Then Continue For
                        Dim i = y * w + x
                        height(i) = CSng(l1 * za.Value + l2 * zb.Value + l3 * zc.Value)
                        cover(i) = 1
                    Next
                Next
            Next
        End Using
        ' 平滑高度（只在網格內：加權模糊）
        Dim sigma = Math.Max(1.0, eyeDist * 0.06)
        Dim weighted(n - 1) As Single
        For i = 0 To n - 1
            weighted(i) = height(i) * cover(i)
        Next
        Dim num = BlurArray(weighted, w, h, sigma), den2 = BlurArray(cover, w, h, sigma)
        For i = 0 To n - 1
            height(i) = If(den2(i) > 0.05F, num(i) / den2(i), 0)
        Next
        ' 光的方向（x 往右、y 往下、z 朝鏡頭）
        Dim dir = If(fromRight, 1.0, -1.0)
        Dim lx, ly, lz As Double
        If kind = BeautyLight.Rembrandt Then
            lx = dir * 0.6 : ly = -0.45 : lz = 0.66
        Else
            lx = dir * 0.95 : ly = -0.1 : lz = 0.3
        End If
        Dim ll = Math.Sqrt(lx * lx + ly * ly + lz * lz)
        lx /= ll : ly /= ll : lz /= ll
        Dim gain(n - 1) As Single
        For y = 1 To h - 2
            For x = 1 To w - 2
                Dim i = y * w + x
                If cover(i) <= 0 Then Continue For
                Dim dhx = (height(i + 1) - height(i - 1)) / 2.0, dhy = (height(i + w) - height(i - w)) / 2.0
                Dim nx = -dhx, ny = -dhy, nz = 1.0
                Dim nl = Math.Sqrt(nx * nx + ny * ny + nz * nz)
                Dim lam = Math.Max(0, (nx * lx + ny * ly + nz * lz) / nl)
                If kind = BeautyLight.Rembrandt Then
                    gain(i) = CSng(Math.Min(1.1, 0.72 + 0.28 * lam / lz))
                Else
                    gain(i) = CSng(Math.Min(1.14, 0.55 + 0.45 * lam / lz))
                End If
            Next
        Next
        ' 網格外（額頭上緣、頭髮）：把網格邊緣的明暗往外延伸（加權模糊），不要突然變回平面漸層
        Dim gw(n - 1) As Single
        For i = 0 To n - 1
            gw(i) = gain(i) * cover(i)
        Next
        Dim extNum = BlurArray(gw, w, h, eyeDist * 0.35), extDen = BlurArray(cover, w, h, eyeDist * 0.35)
        For i = 0 To n - 1
            If cover(i) <= 0 Then gain(i) = If(extDen(i) > 0.01F, extNum(i) / extDen(i), 1.0F)
        Next
        gain = BlurArray(gain, w, h, Math.Max(1.0, eyeDist * 0.06)) ' 柔一點：鼻樑不要有硬邊
        ' 用網格明暗的權重：網格內 1，往外慢慢淡出
        Dim soft = BlurArray(cover, w, h, eyeDist * 0.3)
        For i = 0 To n - 1
            soft(i) = Math.Min(1.0F, Math.Max(cover(i), soft(i) * 1.5F))
        Next
        Return (gain, soft)
    End Function

    '=====================================================================
    ' 髮色：人像去背模型找出人 → 扣掉臉、皮膚、下巴以下 → 換色
    '=====================================================================

    Private Shared ReadOnly _personLock As New Object()
    Private Shared _personKey As String
    Private Shared _person As (Data As Byte(), W As Integer, H As Integer, Stride As Integer)

    ''' <summary>
    ''' 人的範圍（人像去背模型，灰階、取紅色通道）：用整張照片算（只給臉附近一小塊時，模型看不到全身，會把背景也當成人）；
    ''' 同一張圖只算一次。沒有模型時 Data 是 Nothing。
    ''' </summary>
    Private Shared Function PersonMask(source As Bitmap, w As Integer, h As Integer) As (Data As Byte(), W As Integer, H As Integer, Stride As Integer)
        If source Is Nothing OrElse Not BackgroundRemover.ModelAvailable(CutoutModel.Human) Then Return (Nothing, 0, 0, 0)
        SyncLock _personLock
            Dim key = $"{source.GetHashCode()}|{w}x{h}"
            If key <> _personKey OrElse _person.Data Is Nothing Then
                Using mask = BackgroundRemover.ComputeMask(source, CutoutModel.Human)
                    Dim md = mask.LockBits(New Rectangle(0, 0, mask.Width, mask.Height), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb)
                    Dim bytes(md.Stride * mask.Height - 1) As Byte
                    Marshal.Copy(md.Scan0, bytes, 0, bytes.Length)
                    _person = (bytes, mask.Width, mask.Height, md.Stride)
                    mask.UnlockBits(md)
                End Using
                _personKey = key
            End If
            Return _person
        End SyncLock
    End Function

    ''' <summary>人像遮罩在照片座標 (x, y) 的值 0..1。</summary>
    Private Shared Function PersonAt(m As (Data As Byte(), W As Integer, H As Integer, Stride As Integer), x As Double, y As Double, w As Integer, h As Integer) As Double
        Dim mx = Math.Max(0, Math.Min(m.W - 1, CInt(x * m.W / w))), my = Math.Max(0, Math.Min(m.H - 1, CInt(y * m.H / h)))
        Return m.Data(my * m.Stride + mx * 4 + 2) / 255.0
    End Function

    ''' <summary>
    ''' 把頭髮換成 HairColor（保留明暗與髮絲紋理）；沒有人像去背模型時不做事。
    ''' 頭髮＝人像遮罩裡、臉外、下巴以上，而且顏色接近額頭上方取樣到的髮色（兜帽、衣領、圍巾不算）；
    ''' 範圍邊緣淡出（不會看到方框）；提亮按比例放大（逐點補到同一亮度會把髮絲明暗壓平、看起來糊掉）。
    ''' </summary>
    Private Shared Sub ApplyHair(px As Byte(), stride As Integer, w As Integer, h As Integer, source As Bitmap, f As FaceRegion, b As BeautySettings)
        If b.Hair <= 0 Then Return
        Dim person = PersonMask(source, w, h)
        If person.Data Is Nothing Then Return
        Dim amt = b.Hair / 100.0
        Dim fw = f.Box.Width * w, fh = f.Box.Height * h
        Dim cx = f.Box.X * w + fw / 2
        Dim rect = Rectangle.Intersect(New Rectangle(CInt(cx - fw * 1.6), CInt(f.Box.Y * h - fh * 0.9), CInt(fw * 3.2), CInt(fh * 2.6)), New Rectangle(0, 0, w, h))
        If rect.Width < 16 OrElse rect.Height < 16 Then Return
        ' 臉（網格或 68 點的外輪廓，往外一點點）不算頭髮；下巴以下也不算（衣服、脖子）
        Dim chinY As Double = If(f.Dense IsNot Nothing, f.Dense(8).Y * h, f.Box.Bottom * h)
        Dim facePoly As Point2f()
        If f.Mesh IsNot Nothing Then
            Dim oval = {10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109}
            facePoly = oval.Select(Function(i) New Point2f(f.Mesh(i).X * w - rect.X, f.Mesh(i).Y * h - rect.Y)).ToArray()
        Else
            facePoly = Enumerable.Range(0, 24).Select(Function(k) New Point2f(CSng(cx - rect.X + Math.Cos(k * Math.PI / 12) * fw * 0.48),
                                                                              CSng(f.Box.Y * h + fh * 0.55 - rect.Y + Math.Sin(k * Math.PI / 12) * fh * 0.55))).ToArray()
        End If
        Dim faceMask As Single(), faceDist As Single()
        Using fmMat As New Mat(rect.Height, rect.Width, MatType.CV_8UC1, Scalar.All(0)), ff As New Mat()
            Cv2.FillPoly(fmMat, {facePoly.Select(Function(q) New OpenCvSharp.Point(CInt(q.X), CInt(q.Y))).ToArray()}, Scalar.All(255), LineTypes.AntiAlias)
            fmMat.ConvertTo(ff, MatType.CV_32FC1, 1 / 255.0)
            Cv2.GaussianBlur(ff, ff, New OpenCvSharp.Size(0, 0), Math.Max(1.0, fw * 0.02))
            faceMask = GetFloats(ff)
            ' 離臉的距離（眼睛以下只認臉旁邊的頭髮：兜帽、衣領常常在臉旁邊、顏色又跟頭髮很像）
            Using inv As New Mat(), dist As New Mat()
                Cv2.Threshold(fmMat, inv, 127, 255, ThresholdTypes.BinaryInv)
                Cv2.DistanceTransform(inv, dist, DistanceTypes.L2, DistanceTransformMasks.Mask5)
                faceDist = GetFloats(dist)
            End Using
        End Using
        Dim t = ColorToLab(b.HairColor)
        Dim rw = rect.Width, rh = rect.Height, rn = rw * rh
        ' 整塊轉成 Lab
        Dim region(rn * 3 - 1) As Byte
        For y = 0 To rh - 1
            For x = 0 To rw - 1
                Dim di = (rect.Y + y) * stride + (rect.X + x) * 4
                region((y * rw + x) * 3) = px(di) : region((y * rw + x) * 3 + 1) = px(di + 1) : region((y * rw + x) * 3 + 2) = px(di + 2)
            Next
        Next
        Dim lab(rn * 3 - 1) As Byte
        Using rm As New Mat(rh, rw, MatType.CV_8UC3), lm As New Mat()
            Marshal.Copy(region, 0, rm.Data, region.Length)
            Cv2.CvtColor(rm, lm, ColorConversionCodes.BGR2Lab)
            Marshal.Copy(lm.Data, lab, 0, lab.Length)
        End Using
        ' 這張臉的平均膚色（臉的內部）：顏色接近它的不算頭髮（脖子、耳朵、髮際線之間的額頭）
        Dim sL = 0.0, sA = 0.0, sB = 0.0, cnt = 0
        For i = 0 To rn - 1
            If faceMask(i) < 0.9 Then Continue For
            sL += lab(i * 3) : sA += lab(i * 3 + 1) : sB += lab(i * 3 + 2) : cnt += 1
        Next
        Dim mL = If(cnt > 0, sL / cnt, 170.0), mA = If(cnt > 0, sA / cnt, 140.0), mB = If(cnt > 0, sB / cnt, 145.0)
        ' 髮色取樣：先取臉兩側（眼睛以下到嘴巴的高度、臉外 0.02–0.2 臉寬，帽子遮不到），不夠再取額頭上方（臉頂往上 0.05–0.3 臉高）；
        ' 人像遮罩裡、臉外、不像皮膚的點，取亮度中位附近的平均
        Dim topY = facePoly.Min(Function(q) q.Y)
        Dim eyeY As Double = If(f.Mesh IsNot Nothing, (f.Mesh(33).Y + f.Mesh(263).Y) / 2 * h, (f.Box.Y + f.Box.Height * 0.4) * h)
        Dim samples As New List(Of (L As Double, A As Double, B As Double))
        Dim take = Sub(x As Integer, y As Integer)
                       Dim i = y * rw + x
                       If faceMask(i) > 0.1 OrElse PersonAt(person, rect.X + x, rect.Y + y, w, h) < 0.6 Then Return
                       Dim L As Double = lab(i * 3), a As Double = lab(i * 3 + 1), bb As Double = lab(i * 3 + 2)
                       If Math.Sqrt((a - mA) ^ 2 + (bb - mB) ^ 2 + ((L - mL) * 0.6) ^ 2) < 14 Then Return
                       samples.Add((L, a, bb))
                   End Sub
        For y = Math.Max(0, CInt(eyeY - rect.Y + fh * 0.08)) To Math.Min(rh - 1, CInt(eyeY - rect.Y + fh * 0.45))
            For x = 0 To rw - 1
                Dim dd = faceDist(y * rw + x)
                If dd > fw * 0.02 AndAlso dd < fw * 0.2 Then take(x, y)
            Next
        Next
        If samples.Count < 30 Then
            samples.Clear()
            For y = Math.Max(0, CInt(topY - fh * 0.3)) To Math.Min(rh - 1, CInt(topY - fh * 0.05))
                For x = Math.Max(0, CInt(cx - rect.X - fw * 0.35)) To Math.Min(rw - 1, CInt(cx - rect.X + fw * 0.35))
                    take(x, y)
                Next
            Next
        End If
        Dim hasHair = samples.Count >= 30
        Dim hL = 0.0, hA = 128.0, hB = 128.0, hSpread = 30.0
        If hasHair Then
            Dim sorted = samples.OrderBy(Function(s) s.L).ToList()
            Dim mid = sorted.Skip(sorted.Count \ 5).Take(Math.Max(1, sorted.Count * 3 \ 5)).ToList() ' 去掉最亮與最暗的各 20%
            hL = mid.Average(Function(s) s.L) : hA = mid.Average(Function(s) s.A) : hB = mid.Average(Function(s) s.B)
            hSpread = Math.Max(12, (sorted(sorted.Count * 4 \ 5).L - sorted(sorted.Count \ 5).L) / 2) ' 髮絲本身的明暗範圍
        End If
        ' 每點的頭髮程度
        Dim weight(rn - 1) As Single
        Dim edge = Math.Max(4.0, Math.Min(rw, rh) * 0.12)
        Dim wSum = 0.0, wL = 0.0
        For y = 0 To rh - 1
            Dim iy = rect.Y + y
            Dim below = Smooth(iy - chinY, -fh * 0.05, fh * 0.15) ' 下巴以下淡出
            ' 範圍邊緣淡出（貼著照片邊緣的那側不用）
            Dim ey = Math.Min(If(rect.Y = 0, Double.MaxValue, y), If(rect.Bottom = h, Double.MaxValue, rh - 1 - y))
            For x = 0 To rw - 1
                Dim i = y * rw + x
                Dim ex = Math.Min(If(rect.X = 0, Double.MaxValue, x), If(rect.Right = w, Double.MaxValue, rw - 1 - x))
                Dim k = Smooth(PersonAt(person, rect.X + x, iy, w, h), 0.45, 0.85) * (1 - faceMask(i)) * (1 - below) * Smooth(Math.Min(ex, ey), 0, edge)
                If k < 0.01 Then Continue For
                Dim L As Double = lab(i * 3), a As Double = lab(i * 3 + 1), bb As Double = lab(i * 3 + 2)
                ' 不是頭髮：顏色接近膚色、或接近白色而且沒什麼顏色（衣服、背景）
                Dim skinDist = Math.Sqrt((a - mA) ^ 2 + (bb - mB) ^ 2 + ((L - mL) * 0.6) ^ 2)
                Dim chroma = Math.Sqrt((a - 128) ^ 2 + (bb - 128) ^ 2)
                k *= Smooth(skinDist, 12, 30) * (1 - Smooth(L, 150, 200) * (1 - Smooth(chroma, 8, 22)))
                ' 跟取樣到的髮色差太多的不算（兜帽、衣領）
                If hasHair Then
                    ' 比髮色亮的（高光髮絲）：頭頂放寬，眼睛以下要嚴（兜帽、衣領常常在臉旁邊、比頭髮亮）
                    Dim upper = 1 - Smooth(iy, eyeY, eyeY + fh * 0.3)
                    Dim hairDist = Math.Sqrt((a - hA) ^ 2 + (bb - hB) ^ 2 + (Math.Max(0, L - hL - hSpread * (1.2 + 1.5 * upper)) * (1 - 0.5 * upper)) ^ 2 + (Math.Max(0, hL - L - hSpread * 1.5)) ^ 2)
                    k *= 1 - Smooth(hairDist, 14, 34)
                    k *= 1 - (1 - upper) * Smooth(faceDist(i), fw * 0.22, fw * 0.4)
                End If
                If k < 0.01 Then Continue For
                weight(i) = CSng(k)
                wSum += k : wL += k * L
            Next
        Next
        If wSum < 1 Then Return
        ' 提亮：按比例放大亮度（以頭髮的平均亮度算倍率），亮的髮絲亮得多、暗的縫隙亮得少，紋理才保留
        Dim meanL = wL / wSum
        Dim gain = Math.Min(2.5, Math.Max(1.0, (meanL + (Math.Min(t.L, 140) - meanL) * 0.6) / Math.Max(8.0, meanL)))
        For i = 0 To rn - 1
            Dim k As Double = weight(i) * amt
            If k < 0.01 Then Continue For
            Dim L As Double = lab(i * 3), a As Double = lab(i * 3 + 1), bb As Double = lab(i * 3 + 2)
            Dim newL = L * (1 + (gain - 1) * k)
            ' 暗的地方彩度也要跟著小（黑髮直接套金色的 a/b 會變橄欖綠、灰綠），但不能小到變灰
            Dim cs = Math.Min(1.0, Math.Max(0.45, newL / Math.Max(1.0, t.L)))
            Dim ta = 128 + (t.A - 128) * cs, tb = 128 + (t.B - 128) * cs
            lab(i * 3) = ImagePipeline.ClampByte(newL)
            lab(i * 3 + 1) = ImagePipeline.ClampByte(a + (ta - a) * 0.75 * k)
            lab(i * 3 + 2) = ImagePipeline.ClampByte(bb + (tb - bb) * 0.75 * k)
        Next
        Using lm As New Mat(rh, rw, MatType.CV_8UC3), rm As New Mat()
            Marshal.Copy(lab, 0, lm.Data, lab.Length)
            Cv2.CvtColor(lm, rm, ColorConversionCodes.Lab2BGR)
            Marshal.Copy(rm.Data, region, 0, region.Length)
        End Using
        For y = 0 To rh - 1
            For x = 0 To rw - 1
                If weight(y * rw + x) * amt < 0.01 Then Continue For
                Dim di = (rect.Y + y) * stride + (rect.X + x) * 4
                px(di) = region((y * rw + x) * 3) : px(di + 1) = region((y * rw + x) * 3 + 1) : px(di + 2) = region((y * rw + x) * 3 + 2)
            Next
        Next
    End Sub

    '=====================================================================
    ' 曬黑：整張照片裡和這張臉膚色相近的皮膚一起變深（臉、脖子、手臂顏色才一致）
    '=====================================================================

    Private Shared Sub ApplyTan(px As Byte(), stride As Integer, w As Integer, h As Integer, source As Bitmap, f As FaceRegion, faces As IEnumerable(Of FaceRegion), amount As Double)
        If amount <= 0 Then Return
        ' 這張臉中央的平均膚色（YCrCb）
        Dim bx0 = CInt((f.Box.X + f.Box.Width * 0.3) * w), bx1 = CInt((f.Box.X + f.Box.Width * 0.7) * w)
        Dim by0 = CInt((f.Box.Y + f.Box.Height * 0.45) * h), by1 = CInt((f.Box.Y + f.Box.Height * 0.75) * h)
        Dim sY = 0.0, sCr = 0.0, sCb = 0.0, cnt = 0
        For y = Math.Max(0, by0) To Math.Min(h - 1, by1) Step 2
            For x = Math.Max(0, bx0) To Math.Min(w - 1, bx1) Step 2
                Dim i = y * stride + x * 4
                Dim bb As Double = px(i), gg As Double = px(i + 1), rr As Double = px(i + 2)
                Dim yy = 0.299 * rr + 0.587 * gg + 0.114 * bb
                Dim cr = (rr - yy) * 0.713 + 128, cb = (bb - yy) * 0.564 + 128
                If cr < 128 OrElse cr > 185 OrElse cb < 70 OrElse cb > 135 Then Continue For
                sY += yy : sCr += cr : sCb += cb : cnt += 1
            Next
        Next
        If cnt < 20 Then Return
        Dim avgY = sY / cnt, avgCr = sCr / cnt, avgCb = sCb / cnt
        ' 皮膚程度 → 先算在縮小的遮罩上再模糊，邊緣自然。
        ' 臉裡面：顏色接近、不太暗就算；臉外面（脖子、手臂）要更嚴：在人像遮罩裡、顏色更接近、亮度也接近
        ' （冷色調或昏暗的照片臉色偏灰，只看顏色會把背景、衣服、頭髮、燭光一起曬黑）
        Dim sc = Math.Min(1.0, 640.0 / Math.Max(w, h))
        Dim mw = Math.Max(1, CInt(w * sc)), mh = Math.Max(1, CInt(h * sc))
        Dim inFace(mw * mh - 1) As Single
        Using fm As New Mat(mh, mw, MatType.CV_8UC1, Scalar.All(0)), ff As New Mat()
            Dim oval = {10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109}
            For Each fc In faces
                Dim pts As OpenCvSharp.Point()
                If fc.Mesh IsNot Nothing Then
                    Dim ocx = oval.Average(Function(i) fc.Mesh(i).X), ocy = oval.Average(Function(i) fc.Mesh(i).Y)
                    pts = oval.Select(Function(i) New OpenCvSharp.Point(CInt((ocx + (fc.Mesh(i).X - ocx) * 1.05) * mw), CInt((ocy + (fc.Mesh(i).Y - ocy) * 1.05) * mh))).ToArray()
                Else
                    pts = Enumerable.Range(0, 24).Select(Function(k) New OpenCvSharp.Point(CInt((fc.Box.X + fc.Box.Width * (0.5 + Math.Cos(k * Math.PI / 12) * 0.5)) * mw),
                                                                                         CInt((fc.Box.Y + fc.Box.Height * (0.55 + Math.Sin(k * Math.PI / 12) * 0.55)) * mh))).ToArray()
                End If
                Cv2.FillPoly(fm, {pts}, Scalar.All(255), LineTypes.AntiAlias)
            Next
            fm.ConvertTo(ff, MatType.CV_32FC1, 1 / 255.0)
            Cv2.GaussianBlur(ff, ff, New OpenCvSharp.Size(0, 0), Math.Max(1.0, f.Box.Width * mw * 0.04))
            inFace = GetFloats(ff)
        End Using
        Dim person = PersonMask(source, w, h)
        Dim fcx = (f.Box.X + f.Box.Width / 2) * w, fcy = (f.Box.Y + f.Box.Height / 2) * h, fwp = f.Box.Width * w
        Dim mask(mw * mh - 1) As Single
        For ry = 0 To mh - 1
            Dim y = Math.Min(h - 1, CInt(ry / sc))
            For rx = 0 To mw - 1
                Dim x = Math.Min(w - 1, CInt(rx / sc))
                Dim i = y * stride + x * 4
                Dim bb As Double = px(i), gg As Double = px(i + 1), rr As Double = px(i + 2)
                Dim yy = 0.299 * rr + 0.587 * gg + 0.114 * bb
                Dim cr = (rr - yy) * 0.713 + 128, cb = (bb - yy) * 0.564 + 128
                Dim d = Math.Sqrt((cr - avgCr) ^ 2 + (cb - avgCb) ^ 2)
                Dim face = inFace(ry * mw + rx)
                Dim loose = (1 - Smooth(d, 8, 22)) * Smooth(yy, avgY * 0.35, avgY * 0.6)
                Dim tight = 0.0
                If face < 0.99 Then
                    ' 不能比臉色更冷（藍灰的衣服、背景）；沒有人像模型時，只算臉附近（約 3 個臉寬內）
                    Dim warm = Smooth((cr - cb) - (avgCr - avgCb), -12, -4)
                    Dim where = If(person.Data IsNot Nothing, PersonAt(person, x, y, w, h),
                                   1 - Smooth(Math.Sqrt((x - fcx) ^ 2 + (y - fcy) ^ 2), fwp * 2, fwp * 3.5))
                    tight = (1 - Smooth(d, 6, 16)) * Smooth(yy, avgY * 0.4, avgY * 0.65) * (1 - Smooth(yy, avgY * 1.6, avgY * 2.1)) * warm * where
                End If
                mask(ry * mw + rx) = CSng(face * loose + (1 - face) * tight)
            Next
        Next
        ' 牙齒、眼白不能曬黑（顏色接近膚色時會被算進來）
        Using hole As New Mat(mh, mw, MatType.CV_8UC1, Scalar.All(0))
            For Each fc In faces
                If fc.Dense Is Nothing Then Continue For
                Dim dn = fc.Dense.Select(Function(q) New Point2f(q.X, q.Y)).ToArray()
                Dim ms = If(fc.Mesh Is Nothing, Nothing, fc.Mesh.Select(Function(q) New Point2f(q.X, q.Y)).ToArray())
                Dim polys As New List(Of Point2f()) From {LipInner(dn, ms)}
                polys.AddRange(EyeShapes(dn, ms).Select(Function(e) e.Poly))
                For Each poly In polys
                    Cv2.FillPoly(hole, {poly.Select(Function(q) New OpenCvSharp.Point(CInt(q.X * w * sc), CInt(q.Y * h * sc))).ToArray()}, Scalar.All(255))
                Next
            Next
            Dim hb(mw * mh - 1) As Byte
            Marshal.Copy(hole.Data, hb, 0, hb.Length)
            For i = 0 To hb.Length - 1
                If hb(i) <> 0 Then mask(i) = 0
            Next
        End Using
        mask = BlurArray(mask, mw, mh, 1.5)
        ' 套用：Lab 變暗、偏暖（整張一次轉換）
        Dim bgr(w * h * 3 - 1) As Byte
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * stride + x * 4, j = (y * w + x) * 3
                bgr(j) = px(i) : bgr(j + 1) = px(i + 1) : bgr(j + 2) = px(i + 2)
            Next
        Next
        Using m As New Mat(h, w, MatType.CV_8UC3), lab As New Mat()
            Marshal.Copy(bgr, 0, m.Data, bgr.Length)
            Cv2.CvtColor(m, lab, ColorConversionCodes.BGR2Lab)
            Dim lb(bgr.Length - 1) As Byte
            Marshal.Copy(lab.Data, lb, 0, lb.Length)
            For y = 0 To h - 1
                Dim ry = Math.Min(mh - 1, CInt(y * sc))
                For x = 0 To w - 1
                    Dim k = mask(ry * mw + Math.Min(mw - 1, CInt(x * sc))) * amount
                    If k < 0.003 Then Continue For
                    Dim j = (y * w + x) * 3
                    Dim L As Double = lb(j)
                    lb(j) = ImagePipeline.ClampByte(L - L * 0.3 * k)
                    ' 往小麥色拉（a≈+18、b≈+34）：本來偏白的不會變橘，本來偏灰的會變暖
                    Dim a0 As Double = lb(j + 1), b0 As Double = lb(j + 2)
                    lb(j + 1) = ImagePipeline.ClampByte(a0 + (146 - a0) * 0.65 * k)
                    lb(j + 2) = ImagePipeline.ClampByte(b0 + (162 - b0) * 0.65 * k)
                Next
            Next
            Marshal.Copy(lb, 0, lab.Data, lb.Length)
            Cv2.CvtColor(lab, m, ColorConversionCodes.Lab2BGR)
            Marshal.Copy(m.Data, bgr, 0, bgr.Length)
        End Using
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = y * stride + x * 4, j = (y * w + x) * 3
                px(i) = bgr(j) : px(i + 1) = bgr(j + 1) : px(i + 2) = bgr(j + 2)
            Next
        Next
    End Sub

    '=====================================================================
    ' 原圖解析度：美瞳、睫毛
    '=====================================================================

    ''' <summary>網格的眼睛輪廓（16 點，原圖座標）與虹膜（中心＋4 點）；第一隻＝畫面左邊那隻（和 68 點的 36–41 同一隻）。</summary>
    Private Shared ReadOnly MeshEyeRing As Integer()() = {
        New Integer() {33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246},
        New Integer() {263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466}}

    ''' <summary>美瞳（需要網格的虹膜點）：只在眼皮露出的範圍內換色；放大、外圈、混血、眼神光。</summary>
    Private Shared Sub ApplyIris(px As Byte(), stride As Integer, w As Integer, h As Integer, f As FaceRegion, b As BeautySettings)
        If f.Mesh Is Nothing OrElse f.Mesh.Length < 478 OrElse b.Iris <= 0 Then Return
        Dim amt = b.Iris / 100.0
        Dim P = Function(i As Integer) New Point2f(f.Mesh(i).X * w, f.Mesh(i).Y * h)
        Dim t = ColorToLab(b.IrisColor)
        For Each e In {0, 1}
            ' 哪一組虹膜點在這隻眼睛裡：比較虹膜中心和眼睛輪廓的中心
            Dim ring = MeshEyeRing(e).Select(Function(i) P(i)).ToArray()
            Dim ex = ring.Average(Function(q) q.X), ey = ring.Average(Function(q) q.Y)
            Dim c1 = P(468), c2 = P(473)
            Dim ic = If(Math.Abs(c1.X - ex) + Math.Abs(c1.Y - ey) < Math.Abs(c2.X - ex) + Math.Abs(c2.Y - ey), 468, 473)
            Dim center = P(ic)
            Dim r = Enumerable.Range(ic + 1, 4).Average(Function(i) Math.Sqrt((P(i).X - center.X) ^ 2 + (P(i).Y - center.Y) ^ 2))
            If r < 2 Then Continue For
            Dim enlarge = If(b.IrisStyle = IrisStyle.Enlarge, 1 + 0.25 * Math.Max(b.IrisEnlarge, 40) / 100.0, 1 + 0.12 * b.IrisEnlarge / 100.0)
            Dim rr = r * enlarge
            Dim minX = Math.Max(0, CInt(ring.Min(Function(q) q.X)) - 2), maxX = Math.Min(w - 1, CInt(ring.Max(Function(q) q.X)) + 2)
            Dim minY = Math.Max(0, CInt(ring.Min(Function(q) q.Y)) - 2), maxY = Math.Min(h - 1, CInt(ring.Max(Function(q) q.Y)) + 2)
            Dim rw = maxX - minX + 1, rh = maxY - minY + 1
            If rw < 3 OrElse rh < 3 Then Continue For
            ' 眼皮露出的範圍（眼睛輪廓多邊形），邊緣羽化一點
            Dim visible As Single()
            Using m As New Mat(rh, rw, MatType.CV_8UC1, Scalar.All(0)), mf As New Mat()
                Cv2.FillPoly(m, {ring.Select(Function(q) New OpenCvSharp.Point(CInt(q.X - minX), CInt(q.Y - minY))).ToArray()}, Scalar.All(255), LineTypes.AntiAlias)
                m.ConvertTo(mf, MatType.CV_32FC1, 1 / 255.0)
                Cv2.GaussianBlur(mf, mf, New OpenCvSharp.Size(0, 0), Math.Max(0.6, r * 0.06))
                visible = GetFloats(mf)
            End Using
            ' 放大：在虹膜附近往中心取樣（只在看得到的範圍）
            Dim srcCopy(rw * rh * 4 - 1) As Byte
            For y = 0 To rh - 1
                Buffer.BlockCopy(px, (minY + y) * stride + minX * 4, srcCopy, y * rw * 4, rw * 4)
            Next
            For y = minY To maxY
                For x = minX To maxX
                    Dim vi = (y - minY) * rw + (x - minX)
                    Dim vis = visible(vi)
                    If vis < 0.01 Then Continue For
                    Dim dx = x - center.X, dy = y - center.Y
                    Dim dist = Math.Sqrt(dx * dx + dy * dy)
                    If dist > rr * 1.15 Then Continue For
                    Dim di = y * stride + x * 4
                    ' 放大：從比較靠中心的地方取樣
                    If enlarge > 1.001 AndAlso dist < rr * 1.1 Then
                        Dim s = 1 / enlarge
                        Dim falloff = Smooth(rr * 1.1 - dist, 0, rr * 0.2)
                        Dim sx = center.X + dx * (1 - (1 - s) * falloff) - minX, sy = center.Y + dy * (1 - (1 - s) * falloff) - minY
                        Dim tmp(3) As Byte
                        SampleInto(srcCopy, rw, rh, sx, sy, tmp, 0)
                        For ch = 0 To 2
                            px(di + ch) = CByte(Math.Round(px(di + ch) + (tmp(ch) - CDbl(px(di + ch))) * vis))
                        Next
                    End If
                    ' 換色：在虹膜圓內（邊緣羽化），瞳孔（很暗的中心）保留
                    Dim inIris = 1 - Smooth(dist, rr * 0.92, rr * 1.05)
                    If inIris <= 0.002 Then Continue For
                    Dim bb As Double = px(di), gg As Double = px(di + 1), rd As Double = px(di + 2)
                    Dim lum = (0.114 * bb + 0.587 * gg + 0.299 * rd)
                    Dim pupil = 1 - Smooth(dist, r * 0.28, r * 0.42)
                    Dim k = inIris * vis * amt * (1 - pupil * 0.85)
                    Dim tc = b.IrisColor
                    ' 保留明暗紋理：亮度用原本的（稍微提亮），顏色換成美瞳色
                    Dim scale = Math.Max(0.25, lum / Math.Max(1.0, 0.114 * tc.B + 0.587 * tc.G + 0.299 * tc.R)) * 1.08
                    Dim ringDark = 0.0
                    If b.IrisStyle = IrisStyle.Ring OrElse b.IrisStyle = IrisStyle.Mixed Then
                        ringDark = Smooth(dist, rr * 0.72, rr * 0.95) * (0.35 + 0.5 * b.IrisRing / 100.0)
                    ElseIf b.IrisRing > 0 Then
                        ringDark = Smooth(dist, rr * 0.8, rr * 0.98) * 0.3 * b.IrisRing / 100.0
                    End If
                    Dim inner = If(b.IrisStyle = IrisStyle.Mixed, 1 + 0.35 * (1 - Smooth(dist, rr * 0.3, rr * 0.7)), 1.0)
                    Dim nb = Math.Min(255, tc.B * scale * inner) * (1 - ringDark)
                    Dim ng = Math.Min(255, tc.G * scale * inner) * (1 - ringDark)
                    Dim nr = Math.Min(255, tc.R * scale * inner) * (1 - ringDark)
                    px(di) = ImagePipeline.ClampByte(bb + (nb - bb) * k)
                    px(di + 1) = ImagePipeline.ClampByte(gg + (ng - gg) * k)
                    px(di + 2) = ImagePipeline.ClampByte(rd + (nr - rd) * k)
                Next
            Next
            ' 亮眼：右上方一個小眼神光
            If b.IrisStyle = IrisStyle.Bright Then
                Dim hx = center.X + rr * 0.35, hy = center.Y - rr * 0.38
                Dim hr = Math.Max(1.2, rr * 0.16)
                For y = Math.Max(minY, CInt(hy - hr * 2)) To Math.Min(maxY, CInt(hy + hr * 2))
                    For x = Math.Max(minX, CInt(hx - hr * 2)) To Math.Min(maxX, CInt(hx + hr * 2))
                        Dim vis = visible((y - minY) * rw + (x - minX))
                        Dim g = Math.Exp(-((x - hx) ^ 2 + (y - hy) ^ 2) / (2 * hr * hr)) * vis * amt
                        If g < 0.01 Then Continue For
                        Dim di = y * stride + x * 4
                        For ch = 0 To 2
                            px(di + ch) = ImagePipeline.ClampByte(px(di + ch) + (255 - px(di + ch)) * g * 0.85)
                        Next
                    Next
                Next
            End If
        Next
    End Sub

    ''' <summary>睫毛：沿上眼皮（網格 9 點或 68 點 4 點）畫一根根往外上方彎的細線，根部粗、尖端細。</summary>
    Private Shared Sub ApplyLashes(px As Byte(), stride As Integer, w As Integer, h As Integer, f As FaceRegion, b As BeautySettings)
        If b.Lash <= 0 OrElse (f.Mesh Is Nothing AndAlso f.Dense Is Nothing) Then Return
        Dim amt = b.Lash / 100.0
        Dim toPx = Function(q As PointF) New Point2f(q.X * w, q.Y * h)
        Dim dense = f.Dense?.Select(toPx).ToArray()
        Dim mesh = f.Mesh?.Select(toPx).ToArray()
        If dense Is Nothing Then Return
        Dim eyes = EyeShapes(dense, mesh)
        Dim eyeDist = Math.Sqrt((eyes(0).Outer.X - eyes(1).Outer.X) ^ 2 + (eyes(0).Outer.Y - eyes(1).Outer.Y) ^ 2) * 0.62
        If eyeDist < 8 Then Return
        ' 臉的上方向
        Dim ux As Double = dense(27).X - dense(8).X, uy As Double = dense(27).Y - dense(8).Y
        Dim ul = Math.Max(1.0, Math.Sqrt(ux * ux + uy * uy))
        ux /= ul : uy /= ul
        Dim style = b.LashStyle
        ' 真實的睫毛是細而密：濃密靠根數，不靠加粗（粗了反而像畫上去的）
        Dim count = If(style = LashStyle.Thick, 60, If(style = LashStyle.Separated, 30, 48))
        Dim lenBase = eyeDist * (0.06 + 0.1 * b.LashLength / 100.0)
        Dim curl = 0.25 + 0.5 * b.LashCurl / 100.0 + If(style = LashStyle.Curl, 0.25, 0.0)
        Dim thick = Math.Max(0.5, eyeDist * If(style = LashStyle.Thick, 0.0055, 0.0045))
        ' 超過 100＝假睫毛：更密（不加粗）
        Dim fake = Math.Max(0, b.LashLength - 100) / 50.0
        count = CInt(count * (1 + 0.15 * fake))
        Dim col = b.LashColor
        For Each e In eyes
            Dim region = ComputeLashMask(e.Upper, e.OuterFirst, ux, uy, count, lenBase, curl, thick, style, e.Outer.X < e.Inner.X, False, fake)
            BlendMask(px, stride, w, h, region, col, amt)
            If style = LashStyle.Lower Then
                Dim lowerRegion = ComputeLashMask(e.Lower, Not e.OuterFirst, -ux, -uy, count, lenBase, curl, thick * 0.8, style,
                                                  e.Outer.X < e.Inner.X, True, fake)
                BlendMask(px, stride, w, h, lowerRegion, col, amt * 0.8)
            End If
        Next
    End Sub

    ''' <summary>
    ''' 一隻眼睛的睫毛遮罩（局部範圍＋0..1 值）。outerFirst：lid 第一點是外眼角。outerLeft：外眼角在畫面左邊。
    ''' 上睫毛：根部沿眼皮法線長出、越外側越往外倒（外眼角幾乎水平），尖端往上捲回來；假睫毛另外加一簇簇的尖刺。
    ''' 下睫毛：稀疏、兩三根尖端併在一起的 V 形小簇，往下、外側稍微往外斜，內眼角不長。
    ''' 以 3 倍解析度畫再縮小，尖端才會細細收尖。
    ''' </summary>
    Private Shared Function ComputeLashMask(lid As Point2f(), outerFirst As Boolean, ux As Double, uy As Double, count As Integer,
                                            lenBase As Double, curl As Double, thick As Double, style As LashStyle, outerLeft As Boolean,
                                            lower As Boolean, fake As Double) As (X0 As Integer, Y0 As Integer, W As Integer, H As Integer, M As Single())
        ' 沿眼皮曲線的長度
        Dim seg(lid.Length - 2) As Double, total = 0.0
        For j = 0 To lid.Length - 2
            seg(j) = Math.Sqrt((lid(j + 1).X - lid(j).X) ^ 2 + (lid(j + 1).Y - lid(j).Y) ^ 2)
            total += seg(j)
        Next
        If total < 1 Then Return (0, 0, 1, 1, New Single(0) {})
        Dim margin = lenBase * 2 + thick * 4
        Dim x0 = CInt(lid.Min(Function(p) p.X) - margin), y0 = CInt(lid.Min(Function(p) p.Y) - margin)
        Dim x1 = CInt(lid.Max(Function(p) p.X) + margin), y1 = CInt(lid.Max(Function(p) p.Y) + margin)
        Dim mw = Math.Max(1, x1 - x0 + 1), mh = Math.Max(1, y1 - y0 + 1)
        Const SS = 3
        Using m As New Mat(mh * SS, mw * SS, MatType.CV_8UC1, Scalar.All(0))
            Dim rnd As New Random(If(lower, 91, 77))
            ' 往外轉 ang（弧度）：外眼角在右邊時順時針；下睫毛的「上」朝下，方向相反
            Dim sgn = If(outerLeft, -1.0, 1.0) * If(lower, -1.0, 1.0)
            Dim rot = Function(vx As Double, vy As Double, ang As Double) (X:=vx * Math.Cos(ang * sgn) - vy * Math.Sin(ang * sgn),
                                                                         Y:=vx * Math.Sin(ang * sgn) + vy * Math.Cos(ang * sgn))
            ' 眼皮上 tp（0..1）那一點的位置與朝外的法線（和臉的上方向混一點，眼皮點有雜訊時不會亂跳）
            Dim at = Function(tp As Double) As (X As Double, Y As Double, NX As Double, NY As Double)
                         tp = Math.Max(0.0, Math.Min(1.0, tp))
                         Dim dAlong = tp * total
                         Dim j = 0
                         Do While j < seg.Length - 1 AndAlso dAlong > seg(j)
                             dAlong -= seg(j) : j += 1
                         Loop
                         Dim f = If(seg(j) > 0, dAlong / seg(j), 0)
                         Dim tx As Double = lid(j + 1).X - lid(j).X, ty As Double = lid(j + 1).Y - lid(j).Y
                         Dim tl = Math.Max(0.0001, Math.Sqrt(tx * tx + ty * ty))
                         Dim nx = -ty / tl, ny = tx / tl
                         If nx * ux + ny * uy < 0 Then nx = -nx : ny = -ny
                         nx = nx * 0.8 + ux * 0.2 : ny = ny * 0.8 + uy * 0.2
                         Dim nl = Math.Max(0.0001, Math.Sqrt(nx * nx + ny * ny))
                         Return (lid(j).X + tx * f, lid(j).Y + ty * f, nx / nl, ny / nl)
                     End Function
            ' 一根睫毛的二次曲線：根、控制點、尖端
            Dim geo = Function(tp As Double, jitter As Double, lenMul As Double) As (BX As Double, BY As Double, CX As Double, CY As Double, TX As Double, TY As Double)
                          Dim p = at(tp)
                          Dim u = If(outerFirst, 1 - tp, tp) ' 0＝內眼角、1＝外眼角
                          Dim a0, tipAng, len As Double
                          If lower Then
                              a0 = 0.15 + 0.6 * Math.Pow(u, 1.2) + jitter
                              tipAng = a0 - curl * 0.15
                              len = lenBase * (0.25 + 0.4 * u) * lenMul
                          Else
                              a0 = 0.15 * u + 0.75 * u * u + jitter
                              tipAng = a0 - curl * (0.4 + 0.8 * u) ' 往上捲回來（C 形）
                              len = lenBase * (0.45 + 0.65 * Math.Sin(Math.PI * Math.Min(1.0, u * 0.8 + 0.12))) * lenMul
                          End If
                          Dim d0 = rot(p.NX, p.NY, a0), d1 = rot(p.NX, p.NY, tipAng)
                          Dim bx = p.X - p.NX * thick * 0.5, by = p.Y - p.NY * thick * 0.5 ' 根部稍微埋進眼皮
                          Dim cx = bx + d0.X * len * 0.5, cy = by + d0.Y * len * 0.5
                          Return (bx, by, cx, cy, cx + d1.X * len * 0.55, cy + d1.Y * len * 0.55)
                      End Function
            ' 畫一根：根粗尖細（尖端收到幾乎 0）
            Dim draw = Sub(g As (BX As Double, BY As Double, CX As Double, CY As Double, TX As Double, TY As Double), w As Double)
                           Const steps = 10
                           For s = 0 To steps - 1
                               Dim t0 = s / CDbl(steps), t1 = (s + 1) / CDbl(steps)
                               Dim qx0 = (1 - t0) ^ 2 * g.BX + 2 * (1 - t0) * t0 * g.CX + t0 * t0 * g.TX, qy0 = (1 - t0) ^ 2 * g.BY + 2 * (1 - t0) * t0 * g.CY + t0 * t0 * g.TY
                               Dim qx1 = (1 - t1) ^ 2 * g.BX + 2 * (1 - t1) * t1 * g.CX + t1 * t1 * g.TX, qy1 = (1 - t1) ^ 2 * g.BY + 2 * (1 - t1) * t1 * g.CY + t1 * t1 * g.TY
                               Dim wNow = w * SS * (1 - Math.Pow(t0, 1.2) * 0.9)
                               Dim th = Math.Max(1, CInt(Math.Round(wNow)))
                               Dim shade = CInt(255 * Math.Min(1.0, wNow))
                               Cv2.Line(m, New OpenCvSharp.Point(CInt((qx0 - x0) * SS), CInt((qy0 - y0) * SS)),
                                        New OpenCvSharp.Point(CInt((qx1 - x0) * SS), CInt((qy1 - y0) * SS)), Scalar.All(shade), th, LineTypes.AntiAlias)
                           Next
                       End Sub
            ' 一簇：幾根根部分開、尖端往中間那根併攏
            Dim cluster = Sub(tc As Double, size As Integer, lenMul As Double, w As Double, spacing As Double)
                              Dim center = geo(tc, 0, lenMul)
                              For k = 0 To size - 1
                                  Dim off = k - (size - 1) / 2.0
                                  Dim g = geo(tc + off * spacing, off * 0.06, lenMul * (0.78 + rnd.NextDouble() * 0.32))
                                  g = (g.BX, g.BY, g.CX + (center.CX - g.CX) * 0.3, g.CY + (center.CY - g.CY) * 0.3,
                                       g.TX + (center.TX - g.TX) * 0.55, g.TY + (center.TY - g.TY) * 0.55)
                                  draw(g, w)
                              Next
                          End Sub
            Dim pitch = 1.0 / Math.Max(1, count)
            If lower Then
                ' 下睫毛：V 形小簇＋幾根細的，從內眼角 1/5 以外才長
                Dim nC = CInt(5 + 2 * fake)
                For ci = 0 To nC - 1
                    Dim u = 0.22 + 0.74 * (ci + 0.5 + (rnd.NextDouble() - 0.5) * 0.3) / nC
                    cluster(If(outerFirst, 1 - u, u), If(rnd.NextDouble() < 0.5, 2, 3), 1.0, thick, 0.03)
                Next
                For k = 0 To nC \ 2 - 1
                    Dim u = 0.2 + 0.78 * rnd.NextDouble()
                    draw(geo(If(outerFirst, 1 - u, u), (rnd.NextDouble() - 0.5) * 0.15, 0.6), thick * 0.6)
                Next
            Else
                ' 一根根（根根分明只畫成簇的）
                If style <> LashStyle.Separated Then
                    ' 假睫毛另外有成簇的尖刺，單根就少畫一些（太密在像素上會連成一塊黑）
                    Dim singles = Math.Max(8, CInt(count * (1 - 0.35 * fake)))
                    Dim sp = 1.0 / singles
                    For k = 0 To singles - 1
                        Dim tp = (k + 0.5 + (rnd.NextDouble() - 0.5) * 0.6) * sp
                        draw(geo(Math.Max(0.02, Math.Min(0.98, tp)), (rnd.NextDouble() - 0.5) * 0.12, (0.85 + rnd.NextDouble() * 0.3) * If(fake > 0, 0.85, 1.0)), thick)
                    Next
                End If
                ' 一簇一簇的尖刺：根根分明，或假睫毛（再長一點）
                If style = LashStyle.Separated OrElse fake > 0 Then
                    Dim nC = If(style = LashStyle.Separated, Math.Max(3, count \ 3), Math.Max(4, CInt(count / 8)))
                    Dim lenMul = If(style = LashStyle.Separated, 1.0, 1.1 + 0.1 * fake)
                    For ci = 0 To nC - 1
                        Dim tc = (ci + 0.5 + (rnd.NextDouble() - 0.5) * 0.3) / nC
                        cluster(Math.Max(0.04, Math.Min(0.96, tc)), 3, lenMul, thick * 0.9, pitch * 0.8)
                    Next
                End If
            End If
            ' 睫毛根部沿眼皮一條線（看起來長在眼皮上；假睫毛的根部是一條粗黑邊）
            Dim rootW = If(lower, 0.5, 0.8 + 1.2 * fake) * SS
            Dim rootShade = If(lower, 60, CInt(110 + 120 * Math.Min(1.0, fake)))
            For j = 0 To lid.Length - 2
                Cv2.Line(m, New OpenCvSharp.Point(CInt((lid(j).X - x0) * SS), CInt((lid(j).Y - y0) * SS)),
                         New OpenCvSharp.Point(CInt((lid(j + 1).X - x0) * SS), CInt((lid(j + 1).Y - y0) * SS)),
                         Scalar.All(rootShade), Math.Max(1, CInt(rootW)), LineTypes.AntiAlias)
            Next
            Using small As New Mat(), mf As New Mat()
                Cv2.Resize(m, small, New OpenCvSharp.Size(mw, mh), 0, 0, InterpolationFlags.Area)
                small.ConvertTo(mf, MatType.CV_32FC1, 1 / 255.0)
                Return (x0, y0, mw, mh, GetFloats(mf))
            End Using
        End Using
    End Function

    Private Shared Sub BlendMask(px As Byte(), stride As Integer, w As Integer, h As Integer, r As (X0 As Integer, Y0 As Integer, W As Integer, H As Integer, M As Single()),
                                 col As Color, amt As Double)
        For y = 0 To r.H - 1
            Dim iy = r.Y0 + y
            If iy < 0 OrElse iy >= h Then Continue For
            For x = 0 To r.W - 1
                Dim ix = r.X0 + x
                If ix < 0 OrElse ix >= w Then Continue For
                Dim k = r.M(y * r.W + x) * amt
                If k < 0.004 Then Continue For
                Dim di = iy * stride + ix * 4
                px(di) = ImagePipeline.ClampByte(px(di) + (col.B - CDbl(px(di))) * k)
                px(di + 1) = ImagePipeline.ClampByte(px(di + 1) + (col.G - CDbl(px(di + 1))) * k)
                px(di + 2) = ImagePipeline.ClampByte(px(di + 2) + (col.R - CDbl(px(di + 2))) * k)
            Next
        Next
    End Sub
End Class
