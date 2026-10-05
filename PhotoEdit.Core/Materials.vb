Imports System.Drawing

''' <summary>
''' 紋理筆：63 種材質，分「自然、植物／木材、建築、手工／材料、金屬、生物／有機」六類。
''' 每種材質由雜訊、Voronoi 細胞、條紋、磚格等算出顏色與高度；高度差再算成光影（左上打光），
''' 金屬另外加環境反光、黏液與磁磚加高光。座標以照片高度為 1，預覽與匯出的紋理大小一致。
''' 大部分材質用自己的顏色；可調色的材質（花瓣、布料、磁磚…，見 EffectCatalog.Tinted）用線條色。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    Private Structure Cell
        Public F1 As Double, F2 As Double, Id As Double, Cx As Double, Cy As Double
    End Structure

    Private Shared Function N2(x As Double, y As Double, seed As Integer) As Double
        Return Noise2(CSng(x), CSng(y), seed)
    End Function

    Private Shared Function Fb(x As Double, y As Double, seed As Integer, octaves As Integer) As Double
        Return Fbm(CSng(x), CSng(y), seed, octaves)
    End Function

    ''' <summary>脊狀雜訊（岩石、樹皮）：1 − |2n − 1|，多層。</summary>
    Private Shared Function Ridged(x As Double, y As Double, seed As Integer, octaves As Integer) As Double
        Dim sum = 0.0, amp = 0.5, norm = 0.0
        For o = 0 To octaves - 1
            Dim n = 1 - Math.Abs(2 * N2(x, y, seed + o * 31) - 1)
            sum += n * n * amp : norm += amp
            x *= 2.1 : y *= 2.1 : amp *= 0.5
        Next
        Return sum / norm
    End Function

    ''' <summary>擾動（大理石紋）：Σ |2n − 1|。</summary>
    Private Shared Function Turb(x As Double, y As Double, seed As Integer, octaves As Integer) As Double
        Dim sum = 0.0, amp = 0.5, norm = 0.0
        For o = 0 To octaves - 1
            sum += Math.Abs(2 * N2(x, y, seed + o * 57) - 1) * amp : norm += amp
            x *= 2.0 : y *= 2.0 : amp *= 0.5
        Next
        Return sum / norm
    End Function

    ''' <summary>Voronoi 細胞：最近與次近的距離、細胞代號（0..1）與中心。</summary>
    Private Shared Function Voronoi(x As Double, y As Double, seed As Integer, Optional jitter As Double = 1) As Cell
        Dim ix = CInt(Math.Floor(x)), iy = CInt(Math.Floor(y))
        Dim c As New Cell With {.F1 = 9, .F2 = 9}
        For j = -1 To 1
            For i = -1 To 1
                Dim cx = ix + i, cy = iy + j
                Dim px = cx + 0.5 + (Hash(cx, cy, seed) - 0.5) * jitter
                Dim py = cy + 0.5 + (Hash(cx, cy, seed + 17) - 0.5) * jitter
                Dim d = Math.Sqrt((x - px) ^ 2 + (y - py) ^ 2)
                If d < c.F1 Then
                    c.F2 = c.F1 : c.F1 = d
                    c.Id = Hash(cx, cy, seed + 29) : c.Cx = px : c.Cy = py
                ElseIf d < c.F2 Then
                    c.F2 = d
                End If
            Next
        Next
        Return c
    End Function

    Private Shared Function Ss(e0 As Double, e1 As Double, v As Double) As Double
        Dim t = Math.Max(0, Math.Min(1, (v - e0) / (e1 - e0)))
        Return t * t * (3 - 2 * t)
    End Function

    Private Shared Function Frac(v As Double) As Double
        Return v - Math.Floor(v)
    End Function

    Private Shared Function C3(r As Integer, g As Integer, b As Integer) As Rgb
        Return New Rgb(r / 255.0, g / 255.0, b / 255.0)
    End Function

    ''' <summary>金屬：低頻的環境反光（亮暗對比強）加上一點高光。</summary>
    Private Shared Function MetalColor(baseC As Rgb, u As Double, v As Double, seed As Integer, contrast As Double) As Rgb
        Dim env = Ss(0.2, 0.8, Fb(u * 7, v * 7 + u * 2, seed + 400, 4))
        Dim k = 0.62 + (env - 0.5) * contrast * 0.55
        Dim c = baseC.Times(k)
        Dim hi = Math.Pow(env, 8) * 0.25
        Return New Rgb(Math.Min(1, c.R + hi), Math.Min(1, c.G + hi), Math.Min(1, c.B + hi))
    End Function

    ''' <summary>材質的光影強度與高光量。</summary>
    Private Shared Function MatProps(m As MaterialKind) As (Bump As Double, Spec As Double)
        Select Case m
            Case MaterialKind.Rock, MaterialKind.Bark, MaterialKind.Gravel, MaterialKind.StoneWall, MaterialKind.Dino : Return (1.6, 0)
            Case MaterialKind.Granite, MaterialKind.Marble : Return (0.25, 0.35)
            Case MaterialKind.Mud : Return (1.2, 0.6)
            Case MaterialKind.Slime : Return (1.8, 1.0)
            Case MaterialKind.Tile : Return (1.0, 0.5)
            Case MaterialKind.Meat : Return (0.7, 0.5)
            Case MaterialKind.Steel, MaterialKind.Silver, MaterialKind.Gold, MaterialKind.Copper, MaterialKind.Brass : Return (0.5, 0.6)
            Case MaterialKind.Iron, MaterialKind.BrushedMetal, MaterialKind.Scratched : Return (0.5, 0.35)
            Case MaterialKind.Lizard, MaterialKind.Snake, MaterialKind.Monster, MaterialKind.FishScale : Return (1.3, 0.3)
            Case MaterialKind.Paper, MaterialKind.Kraft, MaterialKind.Parchment, MaterialKind.Cement, MaterialKind.Marble : Return (0.3, 0)
            Case MaterialKind.Leopard, MaterialKind.Tiger, MaterialKind.Zebra : Return (0.15, 0)
            Case Else : Return (0.9, 0.05)
        End Select
    End Function

    ''' <summary>材質在 (u, v)（照片高度為 1）的顏色與高度。</summary>
    Private Shared Function Mat(m As MaterialKind, u As Double, v As Double, seed As Integer, tint As Rgb, ByRef height As Double) As Rgb
        Select Case m
            '---------------- 自然 ----------------
            Case MaterialKind.Stone
                Dim n = Fb(u * 90, v * 90, seed, 4)
                height = n
                Dim c = Rgb.Lerp(C3(105, 100, 95), C3(185, 180, 170), n)
                If Math.Abs(N2(u * 26 + Fb(u * 40, v * 40, seed + 8, 2) * 0.6, v * 26, seed + 7) - 0.5) < 0.012 AndAlso Fb(u * 5, v * 5, seed + 9, 2) > 0.5 Then c = c.Times(0.55) : height -= 0.3
                If Hash(CInt(u * 700), CInt(v * 700), seed) > 0.96F Then c = c.Times(1.2)
                Return c
            Case MaterialKind.Rock
                Dim wu = u + Fb(u * 12, v * 12, seed + 5, 3) * 0.05, wv = v + Fb(u * 12 + 7, v * 12, seed + 6, 3) * 0.05
                Dim r = Ridged(wu * 28, wv * 28, seed, 4)
                Dim strata = 0.5 + 0.5 * Math.Sin(v * 70 + Fb(u * 8, v * 8, seed + 1, 3) * 6)
                height = r
                Return Rgb.Lerp(C3(60, 55, 50), C3(155, 145, 130), r * 0.75 + strata * 0.25)
            Case MaterialKind.Granite
                Dim cl = Voronoi(u * 260, v * 260, seed)
                Dim pal = {C3(35, 34, 36), C3(225, 222, 216), C3(165, 132, 125), C3(120, 118, 122), C3(200, 196, 190)}
                height = Fb(u * 200, v * 200, seed, 2) * 0.3
                Return Rgb.Lerp(pal(CInt(Math.Floor(cl.Id * 4.999))), C3(150, 145, 140), 0.12 + Fb(u * 500, v * 500, seed + 3, 2) * 0.15)
            Case MaterialKind.Marble
                Dim t1 = Math.Abs(Math.Sin((u * 6 + v * 2.5) * Math.PI + Turb(u * 5, v * 5, seed, 5) * 7))
                Dim t2 = Math.Abs(Math.Sin((u * 3 - v * 5) * Math.PI + Turb(u * 9, v * 9, seed + 9, 4) * 5))
                Dim vein = (1 - Ss(0, 0.22, t1)) * 0.45 + (1 - Ss(0, 0.05, t1)) * 0.4 + (1 - Ss(0, 0.06, t2)) * 0.3
                height = -vein * 0.2
                Dim baseC = Rgb.Lerp(C3(236, 233, 228), C3(222, 216, 208), Fb(u * 4, v * 4, seed + 2, 3))
                Return Rgb.Lerp(baseC, C3(105, 105, 112), Math.Min(1, vein))
            Case MaterialKind.Sand
                Dim grain = Hash(CInt(u * 900), CInt(v * 900), seed)
                Dim ripple = Math.Sin(v * 140 + Fb(u * 10, v * 10, seed, 3) * 4)
                height = ripple * 0.3 + grain * 0.2
                Return Rgb.Lerp(C3(196, 166, 112), C3(232, 206, 155), 0.5 + ripple * 0.18 + (grain - 0.5) * 0.35)
            Case MaterialKind.Soil
                Dim n = Fb(u * 60, v * 60, seed, 4)
                Dim cl = Voronoi(u * 80, v * 80, seed + 5)
                Dim clod = Ss(0.0, 0.35, cl.F2 - cl.F1)
                height = n * 0.8 + clod * 0.2
                Dim c = Rgb.Lerp(C3(62, 42, 26), C3(120, 86, 55), n * 0.85 + clod * 0.15)
                If Hash(CInt(u * 800), CInt(v * 800), seed) > 0.97F Then c = C3(160, 140, 110)
                Return c
            Case MaterialKind.Mud
                Dim n = Fb(u * 25, v * 25, seed, 4)
                height = Fb(u * 40, v * 40, seed + 3, 3)
                Return Rgb.Lerp(C3(45, 30, 18), C3(95, 66, 40), n)
            Case MaterialKind.Desert
                Dim ph = v * 45 + u * 8 + Fb(u * 4, v * 4, seed, 3) * 6
                Dim d = Math.Pow(Frac(ph / (2 * Math.PI)), 1.8)
                height = d
                Return Rgb.Lerp(C3(180, 110, 55), C3(242, 185, 110), d * 0.85 + Hash(CInt(u * 700), CInt(v * 700), seed) * 0.15)
            Case MaterialKind.Gravel
                Dim cl = Voronoi(u * 70, v * 70, seed, 0.9)
                Dim edge = cl.F2 - cl.F1
                height = Ss(0, 0.35, edge)
                If edge < 0.05 Then Return C3(32, 30, 28)
                Return Rgb.Lerp(C3(90, 85, 78), C3(185, 175, 160), cl.Id).Times(0.85 + Fb(u * 400, v * 400, seed, 2) * 0.3)
            Case MaterialKind.CrackedSoil
                Dim cl = Voronoi(u * 18, v * 18, seed)
                Dim edge = cl.F2 - cl.F1
                Dim crack = 1 - Ss(0.02, 0.06 + Fb(u * 60, v * 60, seed + 4, 2) * 0.04, edge)
                height = Ss(0, 0.3, edge) - crack
                Return Rgb.Lerp(Rgb.Lerp(C3(158, 122, 82), C3(200, 165, 118), Fb(u * 80, v * 80, seed + 1, 3)), C3(45, 30, 20), crack)

            '---------------- 植物／木材 ----------------
            Case MaterialKind.Wood
                Dim d = v * 38 + Fb(u * 6, v * 6, seed, 3) * 3.5
                Dim ring = 0.5 + 0.5 * Math.Sin(d * Math.PI * 2)
                Dim grain = N2(u * 420, v * 24, seed + 3)
                height = grain * 0.3
                Return Rgb.Lerp(C3(112, 70, 38), C3(185, 130, 78), ring ^ 3 * 0.7 + grain * 0.3)
            Case MaterialKind.Bark
                Dim r = Ridged(u * 55, v * 7, seed, 4)
                height = r
                Return Rgb.Lerp(C3(42, 32, 25), C3(120, 102, 82), r).Times(0.85 + Fb(u * 200, v * 200, seed, 2) * 0.3)
            Case MaterialKind.TreeRings
                Dim cx = 0.3 + Hash(seed, 1, 3) * 0.8, cy = 0.3 + Hash(seed, 2, 3) * 0.4
                Dim dist = Math.Sqrt((u - cx) ^ 2 + (v - cy) ^ 2)
                Dim ring = 0.5 + 0.5 * Math.Sin(dist * 170 + Fb(u * 10, v * 10, seed, 3) * 3)
                height = ring * 0.25
                Return Rgb.Lerp(C3(140, 88, 45), C3(215, 165, 105), ring ^ 2)
            Case MaterialKind.Leaf, MaterialKind.DeadLeaf
                Dim cl = Voronoi(u * 12, v * 12, seed, 0.9)
                Dim ang = cl.Id * Math.PI
                Dim lx = u * 12 - cl.Cx, ly = v * 12 - cl.Cy
                Dim along = lx * Math.Cos(ang) + ly * Math.Sin(ang), perp = -lx * Math.Sin(ang) + ly * Math.Cos(ang)
                Dim vein = If(Math.Abs(perp) < 0.02, 1, 0) + If(Math.Abs(Frac((along + Math.Abs(perp) * 0.8) * 7) - 0.5) < 0.06 AndAlso Math.Abs(perp) > 0.03, 0.5, 0)
                Dim edge = cl.F2 - cl.F1
                height = Ss(0, 0.2, edge)
                Dim baseC = If(m = MaterialKind.Leaf,
                               Rgb.Lerp(C3(30, 85, 20), C3(95, 160, 45), cl.Id * 0.6 + Fb(u * 40, v * 40, seed, 2) * 0.4),
                               Rgb.Lerp(Rgb.Lerp(C3(115, 60, 20), C3(210, 145, 50), cl.Id), C3(150, 75, 25), Fb(u * 30, v * 30, seed + 2, 2) * 0.4))
                If edge < 0.035 Then Return baseC.Times(0.45)
                Return Rgb.Lerp(baseC, baseC.Times(1.35), Math.Min(1, vein) * 0.6)
            Case MaterialKind.Grass
                Dim n = N2(u * 520, v * 22 + N2(u * 40, v * 4, seed + 1) * 3, seed)
                height = n
                Return Rgb.Lerp(C3(25, 72, 15), C3(120, 180, 55), n * 0.8 + Fb(u * 20, v * 20, seed + 2, 2) * 0.2)
            Case MaterialKind.Shrub
                Dim cl = Voronoi(u * 90, v * 90, seed)
                Dim bump = Ss(0, 0.4, cl.F2 - cl.F1)
                Dim clump = Fb(u * 15, v * 15, seed + 3, 3)
                height = bump * 0.6 + clump * 0.4
                Return Rgb.Lerp(C3(15, 45, 12), C3(70, 130, 40), cl.Id * 0.5 + bump * 0.3 + clump * 0.3)
            Case MaterialKind.Vine
                Dim baseC = Rgb.Lerp(C3(15, 25, 12), C3(45, 60, 30), Fb(u * 30, v * 30, seed, 3))
                height = 0
                Dim s = Math.Abs(Math.Sin(u * 30 + Math.Sin(v * 12 + seed) * 2 + Fb(u * 5, v * 5, seed + 1, 3) * 2.5))
                Dim cl = Voronoi(u * 40, v * 40, seed + 2)
                If cl.Id < 0.45 AndAlso cl.F1 < 0.28 AndAlso s < 0.5 Then height = 0.8 - cl.F1 : Return Rgb.Lerp(C3(50, 130, 35), C3(100, 175, 60), 1 - cl.F1 * 3)
                If s < 0.1 Then height = 1 - s * 5 : Return C3(85, 95, 40)
                Return baseC
            Case MaterialKind.Moss
                Dim n = Fb(u * 220, v * 220, seed, 3), clump = Fb(u * 30, v * 30, seed + 1, 3)
                height = n * 0.5 + clump * 0.5
                Return Rgb.Lerp(C3(40, 70, 12), C3(140, 170, 45), n * 0.5 + clump * 0.5)
            Case MaterialKind.Petal
                Dim veins = N2(u * 600, v * 40, seed)
                Dim shade = Fb(u * 8, v * 8, seed + 1, 3)
                height = veins * 0.15
                Return Rgb.Lerp(tint.Times(0.85 + shade * 0.25), White, veins * 0.12 + 0.05)

            '---------------- 建築 ----------------
            Case MaterialKind.Cement
                height = Fb(u * 150, v * 150, seed, 3) * 0.25
                Dim c = C3(158, 158, 152).Times(0.92 + (Fb(u * 150, v * 150, seed, 3) - 0.5) * 0.25 + (Fb(u * 8, v * 8, seed + 2, 3) - 0.5) * 0.12)
                If Hash(CInt(u * 600), CInt(v * 600), seed) > 0.985F Then c = c.Times(0.6) : height = -0.3
                Return c
            Case MaterialKind.Concrete
                Dim cl = Voronoi(u * 120, v * 120, seed)
                Dim c = C3(140, 140, 135).Times(0.9 + (Fb(u * 100, v * 100, seed, 3) - 0.5) * 0.25 - Ss(0.55, 0.8, Fb(u * 10, v * 10, seed + 3, 3)) * 0.15)
                height = Fb(u * 100, v * 100, seed, 3) * 0.3
                If cl.Id > 0.72 AndAlso cl.F1 < 0.3 Then c = Rgb.Lerp(C3(100, 95, 88), C3(190, 185, 175), Hash(CInt(cl.Cx), CInt(cl.Cy), seed)) : height = 0.5
                If Hash(CInt(u * 500), CInt(v * 500), seed + 9) > 0.988F Then c = c.Times(0.5)
                Return c
            Case MaterialKind.Brick, MaterialKind.Tile
                Dim brick = m = MaterialKind.Brick
                Dim bw = If(brick, 0.075, 0.06), bh = If(brick, 0.032, 0.06), mortar = If(brick, 0.005, 0.004)
                Dim row = Math.Floor(v / bh)
                Dim offset = If(brick AndAlso CInt(row) Mod 2 = 1, bw / 2, 0)
                Dim col = Math.Floor((u + offset) / bw)
                Dim fu = Frac((u + offset) / bw) * bw, fv = Frac(v / bh) * bh
                Dim dEdge = Math.Min(Math.Min(fu, bw - fu), Math.Min(fv, bh - fv))
                If dEdge < mortar Then
                    height = 0
                    Return If(brick, C3(185, 180, 168), C3(215, 214, 208)).Times(0.85 + Fb(u * 300, v * 300, seed, 2) * 0.3)
                End If
                height = 0.6 + Ss(mortar, mortar * 3, dEdge) * 0.4
                Dim h = Hash(CInt(col), CInt(row), seed)
                If brick Then Return Rgb.Lerp(C3(125, 45, 30), C3(185, 85, 55), h).Times(0.85 + Fb(u * 120, v * 120, seed + 1, 3) * 0.3)
                Return tint.Times(0.9 + h * 0.18 + (Fb(u * 30, v * 30, seed + 2, 2) - 0.5) * 0.08)
            Case MaterialKind.StoneWall
                Dim cl = Voronoi(u * 16, v * 16, seed, 0.9)
                Dim edge = cl.F2 - cl.F1
                If edge < 0.06 Then height = 0 : Return C3(120, 115, 105)
                height = Ss(0.03, 0.25, edge) * 0.8 + Fb(u * 60, v * 60, seed, 3) * 0.2
                Return Rgb.Lerp(C3(85, 80, 72), C3(170, 162, 150), cl.Id).Times(0.85 + Fb(u * 60, v * 60, seed, 3) * 0.3)
            Case MaterialKind.WallCrack, MaterialKind.OldWall
                Dim old = m = MaterialKind.OldWall
                Dim baseC = If(old, C3(195, 180, 152), C3(212, 204, 188)).Times(0.92 + Fb(u * 60, v * 60, seed, 3) * 0.16)
                If old Then
                    Dim stain = Ss(0.5, 0.75, Fb(u * 6, v * 6, seed + 5, 4))
                    baseC = Rgb.Lerp(baseC, C3(130, 110, 80), stain * 0.6)
                End If
                height = Fb(u * 60, v * 60, seed, 3) * 0.2
                Dim cl = Voronoi(u * If(old, 9, 5), v * If(old, 9, 5), seed + 2)
                Dim width = If(old, 0.008, 0.014) + (N2(u * 50, v * 50, seed + 3) - 0.5) * 0.01
                If cl.F2 - cl.F1 < width AndAlso Fb(u * 3, v * 3, seed + 4, 2) > If(old, 0.5, 0.42) Then height = -1 : Return C3(55, 48, 42)
                Return baseC
            Case MaterialKind.PeelingPaint
                Dim p = Fb(u * 12, v * 12, seed, 4)
                If p > 0.6 Then height = 0 : Return C3(140, 133, 122).Times(0.85 + Fb(u * 150, v * 150, seed + 1, 2) * 0.3)
                If p > 0.57 Then height = 1.3 : Return tint.Times(1.15)
                height = 1
                Return tint.Times(0.92 + Fb(u * 80, v * 80, seed + 2, 2) * 0.12)
            Case MaterialKind.Rust
                Dim n = Fb(u * 70, v * 70, seed, 5)
                height = n * 0.8 + Fb(u * 300, v * 300, seed + 1, 2) * 0.2
                Dim c = Rgb.Lerp(C3(80, 32, 12), C3(185, 92, 30), n)
                If Fb(u * 200, v * 200, seed + 2, 2) > 0.7 Then c = C3(215, 125, 50)
                If Hash(CInt(u * 400), CInt(v * 400), seed) > 0.98F Then c = C3(35, 18, 10) : height = -0.5
                Return c

            '---------------- 手工／材料 ----------------
            Case MaterialKind.Paper, MaterialKind.Kraft, MaterialKind.Parchment
                Dim fibers = N2(u * 900, v * 220, seed) * 0.5 + Fb(u * 400, v * 400, seed + 1, 2) * 0.5
                height = fibers * 0.2
                Select Case m
                    Case MaterialKind.Paper : Return C3(244, 242, 234).Times(0.96 + fibers * 0.06)
                    Case MaterialKind.Kraft
                        Dim c = C3(170, 128, 85).Times(0.9 + fibers * 0.2)
                        If Hash(CInt(u * 700), CInt(v * 700), seed) > 0.985F Then c = c.Times(0.65)
                        Return c
                    Case Else
                        Dim stain = Ss(0.45, 0.8, Fb(u * 5, v * 5, seed + 2, 4))
                        Return Rgb.Lerp(C3(236, 214, 162), C3(175, 130, 75), stain * 0.55).Times(0.95 + fibers * 0.1)
                End Select
            Case MaterialKind.Fabric, MaterialKind.Canvas, MaterialKind.Burlap
                Dim scale = If(m = MaterialKind.Fabric, 380, If(m = MaterialKind.Canvas, 200, 110))
                Dim cx = Math.Floor(u * scale), cy = Math.Floor(v * scale)
                Dim over = (CInt(cx) + CInt(cy)) Mod 2 = 0
                Dim shade = If(over, Math.Sin(Frac(v * scale) * Math.PI), Math.Sin(Frac(u * scale) * Math.PI))
                Dim irregular = N2(u * scale * 2, v * scale * 0.2, seed) * 0.5 + N2(u * scale * 0.2, v * scale * 2, seed + 1) * 0.5
                height = shade
                If m = MaterialKind.Burlap Then
                    Dim gapU = Math.Abs(Math.Sin(u * scale * Math.PI)), gapV = Math.Abs(Math.Sin(v * scale * Math.PI))
                    If gapU < 0.22 AndAlso gapV < 0.22 Then height = -0.5 : Return C3(40, 28, 15)
                    Return C3(176, 145, 96).Times(0.65 + shade * 0.35 + (irregular - 0.5) * 0.3)
                End If
                Dim baseC = If(m = MaterialKind.Fabric, tint, C3(212, 198, 162))
                Return baseC.Times(0.72 + shade * 0.3 + (irregular - 0.5) * 0.12)
            Case MaterialKind.Blanket
                Dim fuzz = Fb(u * 300, v * 300, seed, 3)
                Dim stripe = If(Frac(u * 18) < 0.16 OrElse Frac(v * 18) < 0.16, 0.7, 1.0)
                height = fuzz
                Return tint.Times(stripe * (0.82 + fuzz * 0.3))
            Case MaterialKind.Fur, MaterialKind.Hair
                Dim ang = Math.PI / 2 + (Fb(u * 4, v * 4, seed, 3) - 0.5) * If(m = MaterialKind.Fur, 1.6, 0.8)
                Dim a = u * Math.Cos(ang) + v * Math.Sin(ang), b = -u * Math.Sin(ang) + v * Math.Cos(ang)
                Dim strands = N2(b * If(m = MaterialKind.Fur, 800, 1300), a * 30, seed)
                height = strands
                If m = MaterialKind.Hair Then
                    Dim sheen = 0.5 + 0.5 * Math.Sin(a * 25 + Fb(u * 6, v * 6, seed + 3, 2) * 4)
                    Return tint.Times(0.55 + strands * 0.6 + sheen * 0.25)
                End If
                Return Rgb.Lerp(C3(60, 38, 20), C3(170, 125, 80), strands * 0.7 + Fb(u * 20, v * 20, seed + 1, 2) * 0.3)
            Case MaterialKind.Plank
                Dim bw = 0.09
                Dim board = Math.Floor(u / bw)
                Dim off = Hash(CInt(board), 0, seed) * 10
                If Frac(u / bw) * bw < 0.003 Then height = 0 : Return C3(40, 25, 15)
                Dim d = u * 110 + Fb(u * 4, v * 1.5 + off, seed, 3) * 6
                Dim ring = 0.5 + 0.5 * Math.Sin(d * Math.PI * 2)
                height = 0.8 + ring * 0.1
                Return Rgb.Lerp(C3(120, 78, 42), C3(190, 140, 88), ring ^ 2 * 0.7 + N2(u * 30, v * 500, seed) * 0.3).Times(0.85 + Hash(CInt(board), 1, seed) * 0.3)
            Case MaterialKind.Leather
                Dim cl = Voronoi(u * 90, v * 90, seed)
                Dim crease = 1 - Ss(0, 0.08, cl.F2 - cl.F1)
                height = Ss(0, 0.25, cl.F2 - cl.F1)
                Return tint.Times((0.85 + Fb(u * 40, v * 40, seed, 3) * 0.15) * (1 - crease * 0.35))

            '---------------- 金屬 ----------------
            Case MaterialKind.Iron
                height = Fb(u * 80, v * 80, seed, 3) * 0.3
                Return MetalColor(C3(118, 118, 124), u, v, seed, 0.6).Times(0.9 + Fb(u * 40, v * 40, seed + 1, 3) * 0.2)
            Case MaterialKind.Steel
                height = N2(u * 4, v * 300, seed) * 0.15
                Return MetalColor(C3(165, 172, 185), u, v, seed, 1.0).Times(0.95 + N2(u * 4, v * 300, seed) * 0.1)
            Case MaterialKind.Copper : height = Fb(u * 60, v * 60, seed, 2) * 0.15 : Return MetalColor(C3(215, 118, 72), u, v, seed, 0.9)
            Case MaterialKind.Brass : height = Fb(u * 60, v * 60, seed, 2) * 0.15 : Return MetalColor(C3(200, 162, 78), u, v, seed, 0.9)
            Case MaterialKind.Silver : height = Fb(u * 60, v * 60, seed, 2) * 0.1 : Return MetalColor(C3(215, 217, 224), u, v, seed, 1.15)
            Case MaterialKind.Gold : height = Fb(u * 60, v * 60, seed, 2) * 0.1 : Return MetalColor(C3(245, 190, 65), u, v, seed, 1.05)
            Case MaterialKind.Corroded
                height = Fb(u * 80, v * 80, seed, 3) * 0.4
                Dim c = MetalColor(C3(130, 130, 135), u, v, seed, 0.6)
                Dim rust = Fb(u * 12, v * 12, seed + 1, 4)
                If rust > 0.55 Then c = Rgb.Lerp(C3(95, 40, 15), C3(185, 95, 35), Fb(u * 80, v * 80, seed + 2, 3)) : height = rust
                If Fb(u * 20, v * 20, seed + 3, 3) > 0.68 Then c = C3(85, 150, 125)
                Return c
            Case MaterialKind.BrushedMetal
                Dim brushed = N2(u * 6, v * 900, seed)
                height = brushed * 0.1
                Return MetalColor(C3(180, 184, 192), u, v, seed, 0.7).Times(0.9 + brushed * 0.2)
            Case MaterialKind.Scratched
                Dim c = MetalColor(C3(150, 155, 162), u, v, seed, 0.8)
                height = 0
                For Each sc In {7.0, 13.0, 23.0}
                    Dim gx = Math.Floor(u * sc), gy = Math.Floor(v * sc)
                    For dy = -1 To 1
                        For dx = -1 To 1
                            Dim cx = gx + dx, cy = gy + dy
                            Dim ang = Hash(CInt(cx), CInt(cy), seed + CInt(sc)) * Math.PI
                            Dim ox = (cx + Hash(CInt(cx), CInt(cy), seed + 5)) / sc, oy = (cy + Hash(CInt(cx), CInt(cy), seed + 6)) / sc
                            Dim along = (u - ox) * Math.Cos(ang) + (v - oy) * Math.Sin(ang)
                            Dim perp = Math.Abs(-(u - ox) * Math.Sin(ang) + (v - oy) * Math.Cos(ang))
                            If Math.Abs(along) < 0.35 / sc AndAlso perp < 0.0006 AndAlso Hash(CInt(cx), CInt(cy), seed + 7) < 0.6 Then c = c.Times(1.15) : height = -0.25
                        Next
                    Next
                Next
                Return c
            Case MaterialKind.CastIron
                Dim cl = Voronoi(u * 150, v * 150, seed)
                height = Fb(u * 300, v * 300, seed, 2) * 0.5
                Dim c = C3(62, 62, 65).Times(0.85 + Fb(u * 300, v * 300, seed, 2) * 0.3)
                If cl.F1 < 0.14 AndAlso cl.Id < 0.4 Then c = c.Times(0.5) : height = -0.5
                Return c

            '---------------- 生物／有機 ----------------
            Case MaterialKind.Skin
                height = Fb(u * 300, v * 300, seed, 2) * 0.2
                Dim l = 0.97 + (Fb(u * 300, v * 300, seed, 2) - 0.5) * 0.08 + If(Hash(CInt(u * 260), CInt(v * 260), seed + 1) > 0.92F, -0.07, 0)
                Return tint.Times(l)
            Case MaterialKind.Leopard
                height = 0
                Dim baseC = Rgb.Lerp(C3(205, 145, 65), C3(235, 190, 110), Fb(u * 10, v * 10, seed, 3)).Times(0.95 + N2(u * 900, v * 60, seed) * 0.1)
                Dim cl = Voronoi(u * 24, v * 24, seed, 0.8)
                Dim angle = Math.Atan2(v * 24 - cl.Cy, u * 24 - cl.Cx)
                If cl.Id < 0.25 Then
                    If cl.F1 < 0.13 Then Return C3(30, 20, 12)
                ElseIf cl.F1 > 0.17 AndAlso cl.F1 < 0.3 AndAlso N2(angle * 1.6 + cl.Id * 50, cl.Id * 9, seed) > 0.33 Then
                    Return C3(30, 20, 12)
                ElseIf cl.F1 <= 0.17 Then
                    Return Rgb.Lerp(baseC, C3(170, 100, 40), 0.5)
                End If
                Return baseC
            Case MaterialKind.Tiger, MaterialKind.Zebra
                Dim tiger = m = MaterialKind.Tiger
                Dim s = Math.Sin(u * If(tiger, 75, 85) + Fb(u * 4, v * 4, seed, 4) * If(tiger, 9, 10) + v * 6)
                Dim thr = If(tiger, 0.55 + Fb(u * 3, v * 12, seed + 1, 2) * 0.45, 0.0)
                height = 0
                Dim stripe = Ss(thr - 0.04, thr + 0.04, s)
                Dim baseC = If(tiger, Rgb.Lerp(C3(215, 110, 25), C3(245, 160, 60), Fb(u * 8, v * 8, seed + 2, 3)), C3(242, 241, 236))
                Return Rgb.Lerp(baseC, C3(20, 18, 16), stripe)
            Case MaterialKind.Lizard, MaterialKind.Dino
                Dim lizard = m = MaterialKind.Lizard
                Dim cl = Voronoi(u * If(lizard, 120, 38), v * If(lizard, 120, 38), seed, 0.75)
                Dim bump = Ss(0, 0.35, cl.F2 - cl.F1)
                If Not lizard Then
                    Dim small = Voronoi(u * 140, v * 140, seed + 3)
                    bump = Math.Max(bump, Ss(0, 0.35, small.F2 - small.F1) * 0.6)
                End If
                height = bump
                Dim baseC = If(lizard, Rgb.Lerp(C3(60, 85, 28), C3(150, 160, 70), cl.Id), Rgb.Lerp(C3(70, 78, 58), C3(130, 135, 105), cl.Id))
                Return baseC.Times(0.6 + bump * 0.5)
            Case MaterialKind.Snake
                Dim k = 70.0
                Dim a = (u + v) * k, b = (u - v) * k
                Dim d = Math.Max(Math.Abs(Frac(a) - 0.5), Math.Abs(Frac(b) - 0.5))
                height = 1 - d * 2
                Dim blot = Voronoi(u * 6, v * 6, seed)
                Dim baseC = C3(165, 135, 85)
                Dim bf = blot.F1 + (Fb(u * 40, v * 40, seed + 3, 3) - 0.5) * 0.12
                If bf < 0.24 Then baseC = C3(75, 50, 28)
                If bf >= 0.24 AndAlso bf < 0.29 Then baseC = C3(225, 200, 150)
                Return baseC.Times(0.65 + height * 0.45)
            Case MaterialKind.Monster
                Dim cl = Voronoi(u * 60, v * 60, seed, 0.9)
                Dim wart = Ss(0, 0.3, cl.F2 - cl.F1) * If(cl.Id < 0.3, 1.3, 0.7)
                height = wart
                Dim c = tint.Times(0.55 + Fb(u * 30, v * 30, seed, 3) * 0.5 + wart * 0.2)
                If Ridged(u * 14, v * 14, seed + 4, 3) > 0.82 Then c = Rgb.Lerp(c, C3(130, 25, 70), 0.75) : height -= 0.3
                Return c
            Case MaterialKind.Feather
                Dim col = 0.07
                Dim lx = Frac(u / col) - 0.5
                height = 0
                If Math.Abs(lx) < 0.035 Then Return Rgb.Lerp(tint, White, 0.7)
                Dim barbs = 0.5 + 0.5 * Math.Sin((v + Math.Abs(lx) * col * 1.2) * 300)
                height = barbs
                Return tint.Times(0.65 + barbs * 0.4 + (Fb(u * 20, v * 20, seed, 2) - 0.5) * 0.2)
            Case MaterialKind.FishScale
                Dim r = 0.022
                Dim row = Math.Floor(v / (r * 1.1))
                Dim off = If(CInt(row) Mod 2 = 1, r, 0)
                Dim colI = Math.Floor((u + off) / (2 * r))
                Dim cx = colI * 2 * r + r - off, cy = (row + 1) * r * 1.1
                Dim d = Math.Sqrt((u - cx) ^ 2 + (v - cy) ^ 2) / r
                If d > 1 Then
                    Dim cy2 = cy + r * 1.1, cx2 = cx + r
                    d = Math.Min(1, Math.Sqrt((u - cx2) ^ 2 + (v - cy2) ^ 2) / r)
                End If
                height = d
                Return Rgb.Lerp(tint.Times(0.65), Rgb.Lerp(tint, White, 0.45), d).Times(0.95 + Fb(u * 8, v * 8, seed, 2) * 0.1)
            Case MaterialKind.Slime
                height = Fb(u * 18, v * 18, seed, 4)
                Dim c = tint.Times(0.5 + height * 0.55)
                Dim cl = Voronoi(u * 50, v * 50, seed + 2)
                If cl.Id < 0.08 AndAlso cl.F1 < 0.12 AndAlso cl.F1 > 0.08 Then c = Rgb.Lerp(c, White, 0.6)
                Return c
            Case MaterialKind.Meat
                Dim f = Turb(u * 8, v * 8, seed, 4)
                Dim fat = 1 - Ss(0.12, 0.32, Math.Abs(Math.Sin(u * 14 + f * 7)))
                height = N2(u * 30, v * 400, seed) * 0.4
                Dim c = Rgb.Lerp(C3(135, 20, 25), C3(190, 50, 50), N2(u * 30, v * 400, seed))
                Return Rgb.Lerp(c, C3(240, 220, 205), fat * 0.9)
            Case MaterialKind.Mercury ' 實際由 ShadeMercury 上色；這裡給個近似值
                height = Fb(u * 4, v * 4, seed + 70, 3)
                Return Chrome(0.62 + (height - 0.5) * 0.6)
            Case Else ' 骨頭
                Dim cl = Voronoi(u * 220, v * 220, seed)
                height = Fb(u * 50, v * 50, seed, 3) * 0.3
                Dim c = Rgb.Lerp(C3(220, 208, 175), C3(240, 233, 212), Fb(u * 20, v * 20, seed + 1, 3))
                If cl.F1 < 0.12 AndAlso cl.Id < 0.4 Then c = C3(150, 135, 105) : height = -0.4
                Return c
        End Select
    End Function

    ''' <summary>
    ''' 鉻面反射的色帶（水銀）：e 是表面朝向（0 = 朝下映出地面，1 = 朝上映出天空），
    ''' 0.45～0.5 之間是「地平線」：暗帶接亮帶，金屬感主要來自這條銳利的明暗交界。
    ''' </summary>
    Private Shared Function Chrome(e As Double) As Rgb
        Dim stops = {(0.0, New Rgb(0.18, 0.19, 0.22)), (0.38, New Rgb(0.46, 0.48, 0.52)), (0.45, New Rgb(0.15, 0.16, 0.19)),
                     (0.5, New Rgb(0.93, 0.95, 0.98)), (0.65, New Rgb(0.66, 0.7, 0.76)), (0.85, New Rgb(0.86, 0.89, 0.94)), (1.0, New Rgb(1, 1, 1))}
        e = Math.Max(0, Math.Min(1, e))
        For k = 1 To stops.Length - 1
            If e <= stops(k).Item1 Then
                Return Rgb.Lerp(stops(k - 1).Item2, stops(k).Item2, (e - stops(k - 1).Item1) / (stops(k).Item1 - stops(k - 1).Item1))
            End If
        Next
        Return stops(stops.Length - 1).Item2
    End Function

    ''' <summary>
    ''' 水銀／液態金屬：把覆蓋率模糊成圓弧的「液體厚度」，由厚度的斜率決定表面朝向，再查鉻面色帶；
    ''' 加上低頻起伏當作液面流動的倒影，邊緣收得比較利（液體的表面張力）。
    ''' </summary>
    Private Shared Sub ShadeMercury(cv As Canvas, mask As Single(), layer As DrawLayer, imageH As Integer)
        ' 液體厚度：覆蓋率模糊後開根號（邊緣陡、頂部平，像表面張力撐起的液體）。模糊半徑跟著筆寬。
        Dim widthPx = Math.Max(4.0, layer.StrokeWidth * imageH)
        Dim r = CInt(Math.Max(2, Math.Min(widthPx * 0.3, imageH * 0.02)))
        Dim blurred = BoxBlur(BoxBlur(mask, cv.W, cv.H, r), cv.W, cv.H, r)
        Dim body(blurred.Length - 1) As Single
        For i = 0 To body.Length - 1
            body(i) = CSng(Math.Sqrt(Math.Max(0, blurred(i))))
        Next
        Dim tint = Rgb.FromColor(Color.FromArgb(layer.StrokeColorArgb))
        Dim inv = 1.0 / Math.Max(1, imageH)
        Dim slope = r * 2.6
        ' 光從左上方來；視線垂直畫面。
        Dim lx = -0.45, ly = -0.65, lz = 0.62
        Dim ll = Math.Sqrt(lx * lx + ly * ly + lz * lz)
        lx /= ll : ly /= ll : lz /= ll
        Dim hx = lx, hy = ly, hz = lz + 1
        Dim hl = Math.Sqrt(hx * hx + hy * hy + hz * hz)
        hx /= hl : hy /= hl : hz /= hl
        For y = 0 To cv.H - 1
            For x = 0 To cv.W - 1
                Dim i = y * cv.W + x
                Dim a = mask(i)
                If a <= 0.003F Then Continue For
                Dim u = (x + cv.OX) * inv, v = (y + cv.OY) * inv
                ' 表面朝向（法線），加上低頻擾動當作液面流動。
                Dim gx = (Sample(body, cv, x + 1, y) - Sample(body, cv, x - 1, y)) / 2 * slope
                Dim gy = (Sample(body, cv, x, y + 1) - Sample(body, cv, x, y - 1)) / 2 * slope
                gx += (Fb(u * 5, v * 5, layer.Seed + 70, 3) - 0.5) * 0.6
                gy += (Fb(u * 5 + 3.1, v * 5, layer.Seed + 72, 3) - 0.5) * 0.6
                Dim nx = -gx, ny = -gy, nz = 1.0
                Dim nl = Math.Sqrt(nx * nx + ny * ny + nz * nz)
                nx /= nl : ny /= nl : nz /= nl
                ' 環境：朝上映出亮的天空、正下方黑帶、最底一圈地面反光；正對畫面時是深灰。
                Dim up = -ny + nx * 0.22
                Dim env = StudioEnv(up)
                ' 第二層反射：周圍環境的倒影隨表面朝向流動（液態金屬的多重反光帶）。
                Dim scene = Math.Sin((nx * 2.6 + ny * 1.7 + Fb(u * 3, v * 3, layer.Seed + 74, 2) * 2.2) * Math.PI)
                env = env * 0.72 + 0.28 * (0.5 + 0.5 * scene) * (0.55 + 0.45 * nz)
                ' 鏡面高光（尖銳＋柔和兩層）與邊緣菲涅耳亮邊。
                Dim nh = Math.Max(0, nx * hx + ny * hy + nz * hz)
                Dim spec = Math.Pow(nh, 90) * 1.6 + Math.Pow(nh, 14) * 0.18
                Dim rim = Math.Pow(1 - nz, 3) * 0.35 * If(up > -0.2, 1, 0.4)
                Dim lum = env + spec + rim
                Dim c = New Rgb(Math.Min(1, lum * (0.94 + tint.R * 0.08)), Math.Min(1, lum * (0.95 + tint.G * 0.08)), Math.Min(1, lum * (0.98 + tint.B * 0.06)))
                cv.Over(i, CSng(c.R), CSng(c.G), CSng(c.B), SmoothStep(0.18F, 0.42F, Math.Min(1, a)))
            Next
        Next
    End Sub

    ''' <summary>
    ''' 攝影棚環境亮度（高對比的鉻面反射）：up = 表面朝上的程度（-1..1）。
    ''' 地平線（up ≈ 0.1）是黑線接亮帶，金屬感主要來自這條銳利的交界。
    ''' </summary>
    Private Shared Function StudioEnv(up As Double) As Double
        Dim stops = {(-1.0, 0.72), (-0.6, 0.5), (-0.42, 0.1), (-0.2, 0.16), (0.0, 0.4), (0.08, 0.28), (0.11, 0.04), (0.17, 1.0),
                     (0.4, 0.86), (0.7, 0.7), (1.0, 1.0)}
        up = Math.Max(-1, Math.Min(1, up))
        For k = 1 To stops.Length - 1
            If up <= stops(k).Item1 Then
                Dim t = (up - stops(k - 1).Item1) / (stops(k).Item1 - stops(k - 1).Item1)
                Return stops(k - 1).Item2 + (stops(k).Item2 - stops(k - 1).Item2) * t
            End If
        Next
        Return stops(stops.Length - 1).Item2
    End Function

    ''' <summary>紋理筆的上色：材質顏色 × 由高度算出的光影（＋高光），透明度 = 覆蓋率。</summary>
    Private Shared Sub ShadeMaterial(cv As Canvas, mask As Single(), layer As DrawLayer, imageH As Integer)
        If layer.Material = MaterialKind.Mercury Then
            ShadeMercury(cv, mask, layer, imageH)
            Return
        End If
        Dim n = cv.W * cv.H
        Dim hgt(n - 1) As Single
        Dim cols(n - 1) As Rgb
        Dim has(n - 1) As Boolean
        Dim tint = Rgb.FromColor(Color.FromArgb(layer.StrokeColorArgb))
        Dim m = layer.Material
        Dim inv = 1.0 / Math.Max(1, imageH)
        ' 有覆蓋的像素算顏色與高度（多算一圈，光影才看得到邊）。
        For y = 0 To cv.H - 1
            For x = 0 To cv.W - 1
                Dim i = y * cv.W + x
                Dim need = mask(i) > 0.001F OrElse
                           (x > 0 AndAlso mask(i - 1) > 0.001F) OrElse (x < cv.W - 1 AndAlso mask(i + 1) > 0.001F) OrElse
                           (y > 0 AndAlso mask(i - cv.W) > 0.001F) OrElse (y < cv.H - 1 AndAlso mask(i + cv.W) > 0.001F)
                If Not need Then Continue For
                Dim h As Double = 0
                cols(i) = Mat(m, (x + cv.OX) * inv, (y + cv.OY) * inv, layer.Seed, tint, h)
                hgt(i) = CSng(h)
                has(i) = True
            Next
        Next
        Dim props = MatProps(m)
        Dim eScale = imageH / 800.0 * 1.6
        Dim alphaK = If(m = MaterialKind.Slime, 0.88F, 1.0F)
        For y = 0 To cv.H - 1
            For x = 0 To cv.W - 1
                Dim i = y * cv.W + x
                Dim a = mask(i)
                If a <= 0.001F Then Continue For
                Dim h0 = hgt(i)
                Dim hA = If(x > 0 AndAlso y > 0 AndAlso has(i - cv.W - 1), hgt(i - cv.W - 1), h0)
                Dim hB = If(x < cv.W - 1 AndAlso y < cv.H - 1 AndAlso has(i + cv.W + 1), hgt(i + cv.W + 1), h0)
                Dim e = Math.Max(-1, Math.Min(1, (hA - hB) * eScale))
                Dim shade = 1 + e * props.Bump * 0.5
                Dim spec = If(e > 0, e * e * props.Spec, 0)
                Dim c = cols(i)
                cv.Over(i, CSng(Math.Min(1, Math.Max(0, c.R * shade + spec))), CSng(Math.Min(1, Math.Max(0, c.G * shade + spec))),
                        CSng(Math.Min(1, Math.Max(0, c.B * shade + spec))), Math.Min(1, a) * alphaK)
            Next
        Next
    End Sub
End Class
