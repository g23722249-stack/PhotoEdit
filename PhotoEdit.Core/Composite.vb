Imports System.Drawing
Imports System.Text.Json.Serialization

' 人物合成（影像 → 人物合成…）：資料模型、人物分析、背景分析、自動定位、色彩匹配。
' 演算法參考 AutoComposite（g23722249-stack/AutoComposite）的 FigureAnalyzer、SceneAnalyzer、AutoPlacementService、
' ColorMatchingService，改寫成 PhotoEdit 用的 BGRA 位元組陣列；背景照片永遠不會被改，只是在上面加一個圖層。

Public Enum CompositeColorMode
    ''' <summary>保持人物原本的顏色。</summary>
    Original = 0
    ''' <summary>依放置處附近的背景小幅調整亮度、對比、飽和度、色溫。</summary>
    Environment = 1
End Enum

''' <summary>人物的色彩調整（套用在人物上，背景不動）。</summary>
Public Class CompositeColor
    Public Property Mode As CompositeColorMode = CompositeColorMode.Original
    ''' <summary>整體強度 0..100（0 = 原色）。</summary>
    Public Property Strength As Integer = 100
    ''' <summary>亮度位移（-0.5～0.5）。</summary>
    Public Property Brightness As Double
    ''' <summary>對比倍率（1 = 不變）。</summary>
    Public Property Contrast As Double = 1
    ''' <summary>飽和度倍率（1 = 不變）。</summary>
    Public Property Saturation As Double = 1
    ''' <summary>色溫（-1 冷～1 暖）。</summary>
    Public Property Temperature As Double
    ''' <summary>色調（-1 綠～1 洋紅）。</summary>
    Public Property Tint As Double
    ''' <summary>環境色偏（各通道倍率）。</summary>
    Public Property GainR As Double = 1
    Public Property GainG As Double = 1
    Public Property GainB As Double = 1

    Public Function Clone() As CompositeColor
        Return DirectCast(MemberwiseClone(), CompositeColor)
    End Function

    ''' <summary>是否等於不變（原色模式、強度 0、或全部參數都是預設值）。</summary>
    <JsonIgnore>
    Public ReadOnly Property IsIdentity As Boolean
        Get
            If Mode = CompositeColorMode.Original OrElse Strength <= 0 Then Return True
            Return Math.Abs(Brightness) < 0.0005 AndAlso Math.Abs(Contrast - 1) < 0.0005 AndAlso Math.Abs(Saturation - 1) < 0.0005 AndAlso
                   Math.Abs(Temperature) < 0.0005 AndAlso Math.Abs(Tint) < 0.0005 AndAlso
                   Math.Abs(GainR - 1) < 0.0005 AndAlso Math.Abs(GainG - 1) < 0.0005 AndAlso Math.Abs(GainB - 1) < 0.0005
        End Get
    End Property
End Class

''' <summary>
''' 合成人物圖層的來源與參數（存在 Overlay.Composite；Overlay 本身是一般的圖片物件，ImagePath 是做好的人物 PNG）。
''' 之後可以回到引導視窗，用原圖與遮罩重新調整。
''' </summary>
Public Class CompositeFigure
    ''' <summary>圖層清單上的名稱（人物原圖的檔名）。</summary>
    Public Property Name As String = ""
    ''' <summary>工作用人物圖（原圖縮到長邊 2048，存進合成資料夾的檔案）。</summary>
    Public Property SourcePath As String
    ''' <summary>最後使用的遮罩（8 位元灰階 PNG，和工作用的人物圖同尺寸，白 = 人物）。</summary>
    Public Property MaskPath As String
    ''' <summary>AI 算出來、還沒用筆刷修正的遮罩（「還原 AI 結果」用；原圖本來就透明時是 Nothing）。</summary>
    Public Property AiMaskPath As String
    Public Property Model As CutoutModel = CutoutModel.BiRefNetLite
    Public Property EdgeRefine As Integer = 50
    Public Property Decontaminate As Boolean = True
    Public Property FlipHorizontal As Boolean
    ''' <summary>人物上的錨點（工作用人物圖的像素、翻轉前），腳點或半身像的裁切邊中點。</summary>
    Public Property AnchorX As Double
    Public Property AnchorY As Double
    Public Property Color As CompositeColor = New CompositeColor()
    ''' <summary>錨點在背景上的位置（照片 0..1）與人物高度（照片高度的比例）——回到引導視窗再修改時用。</summary>
    Public Property TargetX As Double
    Public Property TargetY As Double
    Public Property HeightFraction As Double
    ''' <summary>以錨點為中心的旋轉（度，順時針為正）。</summary>
    Public Property Rotation As Double
    ''' <summary>仰角透視：人物頂部寬度 ÷ 錨點處寬度（1 = 不變；仰拍的建築前通常小於 1）。</summary>
    Public Property Keystone As Double = 1
    ''' <summary>遮擋遮罩（在人物前面的東西，背景預覽尺寸的 8 位元灰階 PNG）；沒有遮擋時 Nothing。</summary>
    Public Property OcclusionMaskPath As String
    Public Property Shadow As CompositeShadow = New CompositeShadow()
    ''' <summary>對應的陰影圖層 PNG（陰影是另一個圖片物件，放在人物下面）；沒有陰影時 Nothing。</summary>
    Public Property ShadowLayerPath As String
    ''' <summary>套用當時圖片物件的位置與高度：之後在主畫面上被移動、縮放過，回到引導視窗時依這個換算。</summary>
    Public Property LayerX As Double
    Public Property LayerY As Double
    Public Property LayerSize As Double
    ''' <summary>邊緣透光 0..100：把背景的光滲進人物邊緣，減少剪貼感。</summary>
    Public Property LightWrap As Integer
    ''' <summary>顆粒：加在人物上的雜訊強度（背景預覽尺寸下的灰階標準差 × 10）。</summary>
    Public Property Grain As Integer
    ''' <summary>地平線（照片高度的比例）；沒有用地平線輔助時 -1。</summary>
    Public Property Horizon As Double = -1

    Public Function Clone() As CompositeFigure
        Dim c = DirectCast(MemberwiseClone(), CompositeFigure)
        c.Color = Color?.Clone()
        c.Shadow = Shadow?.Clone()
        Return c
    End Function
End Class

''' <summary>
''' 人物的陰影：投射陰影（以腳點為支點投到地面，離腳越遠越淡越模糊）＋接觸陰影（腳下的扁橢圓）＋環境遮蔽（更寬更淡）。
''' 單張照片無法精準還原光源，只有粗略推薦，方向、長度、濃淡都可以調。
''' </summary>
Public Class CompositeShadow
    Public Property Enabled As Boolean = True
    ''' <summary>影子在地面上的方向（度；0 = 向右、90 = 朝鏡頭（畫面下方）、180 = 向左、-90 = 遠離鏡頭）。</summary>
    Public Property Direction As Double = 30
    ''' <summary>影子長度（人物高度的倍數）。</summary>
    Public Property Length As Double = 0.5
    ''' <summary>地面透視壓扁（0.1～1）。</summary>
    Public Property Flatten As Double = 0.35
    ''' <summary>投射陰影濃淡 0..100。</summary>
    Public Property Opacity As Integer = 28
    ''' <summary>柔和程度：模糊半徑占人物高度的千分比。</summary>
    Public Property Softness As Integer = 12
    ''' <summary>接觸陰影濃淡 0..100。</summary>
    Public Property Contact As Integer = 55
    ''' <summary>環境遮蔽 0..100。</summary>
    Public Property Ambient As Integer = 25
    ''' <summary>離腳越遠越淡 0..100（100 = 頭的影子完全消失）。</summary>
    Public Property Falloff As Integer = 55
    ''' <summary>陰影不落在天空上。</summary>
    Public Property AvoidSky As Boolean = True

    Public Function Clone() As CompositeShadow
        Return DirectCast(MemberwiseClone(), CompositeShadow)
    End Function

    ''' <summary>
    ''' 依背景推薦：光從哪一側來，影子就往另一側；判斷不出時往右前方、淡一點。站在柱頂等窄平台時影子短、接觸陰影重。
    ''' 半身像沒有踩在地上，不產生陰影。
    ''' </summary>
    Public Shared Function Recommend(scene As SceneAnalysis, fig As FigureAnalysis) As CompositeShadow
        Dim s As New CompositeShadow()
        If fig IsNot Nothing AndAlso fig.BottomCropped Then
            s.Enabled = False
            Return s
        End If
        If scene IsNot Nothing AndAlso scene.LightSide <> 0 Then
            s.Direction = If(scene.LightSide > 0, 160, 20)
            s.Opacity = CInt(22 + 10 * Math.Abs(scene.LightSide))
        Else
            s.Direction = 30
            s.Opacity = 18
        End If
        If scene IsNot Nothing AndAlso scene.Suggestions.Count > 0 AndAlso scene.Suggestions(0).FromSkyline Then
            s.Length = 0.25
            s.Contact = 60
        End If
        Return s
    End Function
End Class

''' <summary>
''' 人物在背景上的幾何：人物圖（顯示方向）的錨點放到背景 (TX, TY)，縮放 Scale，再以錨點為中心做仰角透視（Keystone，
''' 每一列依高度等比左右縮放）與旋轉（Rotation，順時針度）。Forward：人物 → 背景；Inverse：背景 → 人物。
''' </summary>
Public Structure CompositeGeometry
    Public AnchorX, AnchorY As Double
    Public TX, TY, Scale As Double
    Public Rotation As Double
    Public Keystone As Double
    ''' <summary>人物高度（人物圖像素，外框高度）：透視依「離錨點多高」占這個高度的比例變化。</summary>
    Public FigureHeight As Double

    Private ReadOnly Property Cos As Double
        Get
            Return Math.Cos(Rotation * Math.PI / 180)
        End Get
    End Property

    Private ReadOnly Property Sin As Double
        Get
            Return Math.Sin(Rotation * Math.PI / 180)
        End Get
    End Property

    Private Function Widen(ly As Double) As Double
        Dim h = Math.Max(1, FigureHeight * Scale)
        Dim t = -ly / h
        Return Math.Max(0.2, 1 + ((If(Keystone <= 0, 1, Keystone)) - 1) * t)
    End Function

    Public Function Forward(px As Double, py As Double) As PointF
        Dim lx = (px - AnchorX) * Scale, ly = (py - AnchorY) * Scale
        lx *= Widen(ly)
        Return New PointF(CSng(TX + lx * Cos - ly * Sin), CSng(TY + lx * Sin + ly * Cos))
    End Function

    Public Function Inverse(bx As Double, by As Double) As PointF
        Dim dx = bx - TX, dy = by - TY
        Dim lx = dx * Cos + dy * Sin, ly = -dx * Sin + dy * Cos
        lx /= Widen(ly)
        Return New PointF(CSng(lx / Scale + AnchorX), CSng(ly / Scale + AnchorY))
    End Function

    ''' <summary>人物圖上一塊範圍變形到背景後的外框（每一列是仿射，四個角就是極值）。</summary>
    Public Function BoundsOf(r As Rectangle) As RectangleF
        Dim pts = {Forward(r.Left, r.Top), Forward(r.Right, r.Top), Forward(r.Left, r.Bottom), Forward(r.Right, r.Bottom)}
        Dim minX = pts.Min(Function(p) p.X), minY = pts.Min(Function(p) p.Y)
        Return RectangleF.FromLTRB(minX, minY, pts.Max(Function(p) p.X), pts.Max(Function(p) p.Y))
    End Function

    ''' <summary>
    ''' 由兩條參考線（例如柱子、門框的左右邊）推估：旋轉＝兩線的平均傾斜（限 ±20°），
    ''' 透視＝兩線在人物頂部與錨點處的寬度比（線收斂到消失點，限 0.6～1.4）。平行或資料不足時傳回不變。
    ''' </summary>
    Public Shared Function FromGuides(a1 As PointF, a2 As PointF, b1 As PointF, b2 As PointF, anchorY As Double, figureHeightOnBg As Double) As (Rotation As Double, Keystone As Double)
        Dim ang = Function(p As PointF, q As PointF)
                      Dim dx = q.X - p.X, dy = q.Y - p.Y
                      If dy > 0 Then dx = -dx : dy = -dy
                      Return Math.Atan2(dx, -dy) * 180 / Math.PI
                  End Function
        Dim rot = Math.Clamp((ang(a1, a2) + ang(b1, b2)) / 2, -20, 20)
        Dim k = 1.0
        Dim d = (a1.X - a2.X) * (b1.Y - b2.Y) - (a1.Y - a2.Y) * (b1.X - b2.X)
        If Math.Abs(d) > 0.000001 AndAlso figureHeightOnBg > 0 Then
            Dim t = ((a1.X - b1.X) * (b1.Y - b2.Y) - (a1.Y - b1.Y) * (b1.X - b2.X)) / d
            Dim vy = a1.Y + t * (a2.Y - a1.Y)
            If Math.Abs(vy) < 1000000 Then
                Dim dBottom = anchorY - vy
                Dim dTop = anchorY - figureHeightOnBg - vy
                If Math.Abs(dBottom) >= 1 Then
                    k = dTop / dBottom
                    k = If(Double.IsNaN(k) OrElse k <= 0, 0.6, Math.Clamp(k, 0.6, 1.4))
                End If
            End If
        End If
        Return (rot, k)
    End Function
End Structure

''' <summary>雜訊估計：亮度和 3×3 鄰域平均的差，取中位數（換算成高斯標準差）。用來讓人物的顆粒接近背景。</summary>
Public NotInheritable Class CompositeNoise
    Private Sub New()
    End Sub

    ''' <summary>px 為 BGRA；alpha 不為 Nothing 時只看人物內部（透明度 ≥ 250 而且四鄰也是）。回傳灰階（0..255）標準差。</summary>
    Public Shared Function Estimate(px As Byte(), w As Integer, h As Integer, region As Rectangle, alpha As Byte()) As Double
        Dim r = Rectangle.Intersect(region, New Rectangle(1, 1, Math.Max(0, w - 2), Math.Max(0, h - 2)))
        If r.Width <= 0 OrElse r.Height <= 0 Then Return 0
        Dim stepPx = Math.Max(1, CInt(Math.Sqrt(CDbl(r.Width) * r.Height / 60000)))
        Dim lum = Function(i As Integer) 0.0722 * px(i * 4) + 0.7152 * px(i * 4 + 1) + 0.2126 * px(i * 4 + 2)
        Dim diffs As New List(Of Double)()
        For y = r.Y To r.Bottom - 1 Step stepPx
            For x = r.X To r.Right - 1 Step stepPx
                Dim i = y * w + x
                If alpha IsNot Nothing AndAlso (alpha(i) < 250 OrElse alpha(i - 1) < 250 OrElse alpha(i + 1) < 250 OrElse alpha(i - w) < 250 OrElse alpha(i + w) < 250) Then Continue For
                Dim m = 0.0
                For dy = -1 To 1
                    For dx = -1 To 1
                        If dx <> 0 OrElse dy <> 0 Then m += lum(i + dy * w + dx)
                    Next
                Next
                diffs.Add(Math.Abs(lum(i) - m / 8))
            Next
        Next
        If diffs.Count < 20 Then Return 0
        diffs.Sort()
        ' 中位數絕對差 → 標準差（高斯 1.4826），再扣掉鄰域平均本身帶的（×8/9）
        Return diffs(diffs.Count \ 2) * 1.4826 * 8 / 9
    End Function
End Class

'=====================================================================
' 人物分析
'=====================================================================

''' <summary>人物輪廓分析結果（人物圖像素座標）。</summary>
Public Class FigureAnalysis
    ''' <summary>透明度超過門檻的外框。</summary>
    Public Property Bounds As Rectangle
    ''' <summary>兩腳之間的地面接觸點（底部接地列的透明度加權中心，y 取接地列下緣）。</summary>
    Public Property FeetX As Double
    Public Property FeetY As Double
    ''' <summary>腳部左右範圍（x）。</summary>
    Public Property FeetLeft As Double
    Public Property FeetRight As Double
    ''' <summary>頭部（頂部 12% 的加權中心）。</summary>
    Public Property HeadX As Double
    Public Property HeadY As Double
    ''' <summary>
    ''' 人物下緣被原圖裁切（半身像、胸像）：前景碰到最底列，而且那一列的寬度至少是最寬列的 40%。
    ''' 全身像腳底那一列很窄，不會被當成裁切。
    ''' </summary>
    Public Property BottomCropped As Boolean

    Public ReadOnly Property Height As Double
        Get
            Return Bounds.Height
        End Get
    End Property

    Public ReadOnly Property FeetWidth As Double
        Get
            Return Math.Max(1, FeetRight - FeetLeft)
        End Get
    End Property

    ''' <summary>錨點：全身像是兩腳之間，半身像是裁切邊的中點。</summary>
    Public ReadOnly Property Anchor As PointF
        Get
            If BottomCropped Then Return New PointF(CSng(Bounds.X + Bounds.Width / 2.0), Bounds.Bottom)
            Return New PointF(CSng(FeetX), CSng(FeetY))
        End Get
    End Property

    ''' <summary>左右翻轉後的分析（人物圖寬 width）。</summary>
    Public Function Mirrored(width As Integer) As FigureAnalysis
        Dim m = DirectCast(MemberwiseClone(), FigureAnalysis)
        m.Bounds = New Rectangle(width - Bounds.Right, Bounds.Y, Bounds.Width, Bounds.Height)
        m.FeetX = width - FeetX
        m.FeetLeft = width - FeetRight
        m.FeetRight = width - FeetLeft
        m.HeadX = width - HeadX
        Return m
    End Function
End Class

''' <summary>
''' 由透明度分析人物輪廓：外框、腳部接觸點與寬度、頭部、是否為半身像。
''' 只依輪廓統計，不辨識姿勢；腳點以底部「接地列」計算，排除只占極少像素的細長物（矛尖、裙襬尖）。
''' </summary>
Public NotInheritable Class FigureAnalyzer
    Private Sub New()
    End Sub

    ''' <summary>透明度門檻（0..255）。</summary>
    Public Const Threshold As Integer = 51
    ''' <summary>最底列寬度達最寬列的這個比例以上，就當成下緣被裁切。</summary>
    Public Const CroppedRowFraction As Double = 0.4

    Public Shared Function Analyze(alpha As Byte(), w As Integer, h As Integer) As FigureAnalysis
        Dim minX = w, minY = h, maxX = -1, maxY = -1
        Dim rowSum(h - 1) As Double
        Dim maxRow = 0.0
        For y = 0 To h - 1
            Dim s = 0.0
            Dim row = y * w
            For x = 0 To w - 1
                Dim v = alpha(row + x)
                If v > Threshold Then
                    s += v / 255.0
                    If x < minX Then minX = x
                    If x > maxX Then maxX = x
                    If y < minY Then minY = y
                    maxY = y
                End If
            Next
            rowSum(y) = s
            If s > maxRow Then maxRow = s
        Next
        If maxX < 0 Then Throw New InvalidOperationException(Lang.T("遮罩中沒有人物（全部透明），無法分析。"))
        Dim bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1)

        ' 接地列：由下往上第一個「前景量達最寬列 8%」的列，排除底部零星雜點與細長物。
        Dim groundRow = bounds.Bottom - 1
        For y = bounds.Bottom - 1 To bounds.Y Step -1
            If rowSum(y) >= maxRow * 0.08 Then groundRow = y : Exit For
        Next
        ' 腳部帶：接地列往上 4% 人物高度；左右範圍取累積分布 3%～97%（忽略腳邊的武器尖端）。
        Dim band = Math.Max(2, CInt(bounds.Height * 0.04))
        Dim colSum(w - 1) As Double
        Dim fx = 0.0, fw = 0.0
        For y = Math.Max(bounds.Y, groundRow - band) To groundRow
            For x = bounds.X To bounds.Right - 1
                Dim v = alpha(y * w + x)
                If v > Threshold Then
                    Dim a = v / 255.0
                    fx += x * a : fw += a
                    colSum(x) += a
                End If
            Next
        Next
        Dim left = Double.NaN, right = Double.NaN, acc = 0.0
        For x = bounds.X To bounds.Right - 1
            acc += colSum(x)
            If Double.IsNaN(left) AndAlso acc >= fw * 0.03 Then left = x
            If acc >= fw * 0.97 Then right = x + 1 : Exit For
        Next
        If Double.IsNaN(left) Then left = bounds.X
        If Double.IsNaN(right) Then right = bounds.Right

        ' 頭部：頂部 12% 的加權中心。
        Dim headBand = Math.Max(2, CInt(bounds.Height * 0.12))
        Dim hx = 0.0, hy = 0.0, hw = 0.0
        For y = bounds.Y To Math.Min(bounds.Bottom - 1, bounds.Y + headBand)
            For x = bounds.X To bounds.Right - 1
                Dim v = alpha(y * w + x)
                If v > Threshold Then
                    Dim a = v / 255.0
                    hx += x * a : hy += y * a : hw += a
                End If
            Next
        Next

        Return New FigureAnalysis With {
            .Bounds = bounds,
            .FeetX = If(fw > 0, fx / fw + 0.5, bounds.X + bounds.Width / 2.0), .FeetY = groundRow + 1,
            .FeetLeft = left, .FeetRight = right,
            .HeadX = If(hw > 0, hx / hw + 0.5, bounds.X + bounds.Width / 2.0), .HeadY = If(hw > 0, hy / hw + 0.5, bounds.Y),
            .BottomCropped = bounds.Bottom >= h AndAlso rowSum(h - 1) >= maxRow * CroppedRowFraction}
    End Function
End Class

'=====================================================================
' 背景分析
'=====================================================================

''' <summary>背景上推薦的站立位置（背景像素）。</summary>
Public Class SurfaceSuggestion
    Public Property AnchorX As Double
    Public Property AnchorY As Double
    Public Property SurfaceLeft As Double
    Public Property SurfaceRight As Double
    ''' <summary>0..1，越高越推薦。</summary>
    Public Property Score As Double
    Public Property Reason As String = ""
    ''' <summary>由天際線（結構頂部對著天空）找到的。</summary>
    Public Property FromSkyline As Boolean

    Public ReadOnly Property SurfaceWidth As Double
        Get
            Return Math.Max(0, SurfaceRight - SurfaceLeft)
        End Get
    End Property
End Class

''' <summary>背景分析結果。</summary>
Public Class SceneAnalysis
    Public Property Width As Integer
    Public Property Height As Integer
    ''' <summary>天空遮罩（分析用的小圖，0..1）。</summary>
    Public Property Sky As Single()
    Public Property SkyWidth As Integer
    Public Property SkyHeight As Integer
    Public Property SkyFraction As Double
    Public Property Suggestions As New List(Of SurfaceSuggestion)()
    ''' <summary>粗略光源方向：+1 光從右邊來、-1 左邊、0 無法判斷。</summary>
    Public Property LightSide As Double

    ''' <summary>背景像素 (x, y) 是天空的程度（0..1）；超出畫面時 0。</summary>
    Public Function SkyAt(x As Double, y As Double) As Single
        If Sky Is Nothing OrElse x < 0 OrElse y < 0 OrElse x >= Width OrElse y >= Height Then Return 0
        Dim sx = Math.Min(SkyWidth - 1, CInt(x * SkyWidth / Width)), sy = Math.Min(SkyHeight - 1, CInt(y * SkyHeight / Height))
        Return Sky(sy * SkyWidth + sx)
    End Function
End Class

''' <summary>
''' 背景分析：估計天空與天際線，找出頂部對著天空、寬度足夠又平坦的結構頂面（柱頂、屋頂、平台）當作推薦站立點；
''' 沒有天空時推薦畫面下方中央的地面。單張照片的啟發式分析，不是 3D 重建，結果只當推薦。
''' </summary>
Public NotInheritable Class SceneAnalyzer
    Private Sub New()
    End Sub

    Private Const WorkSide As Integer = 512

    ''' <summary>px 為 BGRA。</summary>
    Public Shared Function Analyze(px As Byte(), w As Integer, h As Integer) As SceneAnalysis
        Dim k = Math.Min(1.0, WorkSide / CDbl(Math.Max(w, h)))
        Dim sw = Math.Max(1, CInt(w * k)), sh = Math.Max(1, CInt(h * k))
        Dim small = Downscale(px, w, h, sw, sh)
        Dim sky = EstimateSky(small, sw, sh)
        Dim result As New SceneAnalysis With {.Width = w, .Height = h, .Sky = sky, .SkyWidth = sw, .SkyHeight = sh}
        Dim skyCount = 0.0
        For Each v In sky
            skyCount += v
        Next
        result.SkyFraction = skyCount / sky.Length
        result.LightSide = EstimateLightSide(small, sw, sh)

        ' 天際線：每一欄第一個非天空像素。
        Dim line(sw - 1) As Integer
        For x = 0 To sw - 1
            Dim y = 0
            While y < sh AndAlso sky(y * sw + x) > 0.5F
                y += 1
            End While
            line(x) = y
        Next
        Dim inv = w / CDbl(sw)
        If result.SkyFraction >= 0.03 Then
            For Each p In FindPlateaus(line, sw, sh)
                result.Suggestions.Add(New SurfaceSuggestion With {
                    .AnchorX = p.Center * inv, .AnchorY = p.Y * inv, .SurfaceLeft = p.Left * inv, .SurfaceRight = (p.Right + 1) * inv,
                    .Score = p.Score, .FromSkyline = True,
                    .Reason = Lang.F("天際線上的平坦頂面（寬 {0} 像素），頂部對著天空", CInt((p.Right - p.Left + 1) * inv))})
            Next
        End If
        If result.Suggestions.Count = 0 Then
            result.Suggestions.Add(New SurfaceSuggestion With {
                .AnchorX = w / 2.0, .AnchorY = h * 0.9, .SurfaceLeft = w * 0.3, .SurfaceRight = w * 0.7, .Score = 0.2,
                .Reason = If(result.SkyFraction < 0.03, Lang.T("沒有偵測到天空，放在畫面下方中央的地面"), Lang.T("沒有找到夠寬的平坦頂面，放在畫面下方中央"))})
        End If
        result.Suggestions.Sort(Function(a, b) b.Score.CompareTo(a.Score))
        Return result
    End Function

    ''' <summary>區塊平均縮小（BGRA）。</summary>
    Private Shared Function Downscale(px As Byte(), w As Integer, h As Integer, sw As Integer, sh As Integer) As Byte()
        If sw = w AndAlso sh = h Then Return px
        Dim r(sw * sh * 4 - 1) As Byte
        For y = 0 To sh - 1
            Dim y0 = y * h \ sh, y1 = Math.Max(y0 + 1, (y + 1) * h \ sh)
            For x = 0 To sw - 1
                Dim x0 = x * w \ sw, x1 = Math.Max(x0 + 1, (x + 1) * w \ sw)
                Dim b = 0, g = 0, rr = 0, n = 0
                For yy = y0 To y1 - 1
                    For xx = x0 To x1 - 1
                        Dim i = (yy * w + xx) * 4
                        b += px(i) : g += px(i + 1) : rr += px(i + 2) : n += 1
                    Next
                Next
                Dim j = (y * sw + x) * 4
                r(j) = CByte(b \ n) : r(j + 1) = CByte(g \ n) : r(j + 2) = CByte(rr \ n) : r(j + 3) = 255
            Next
        Next
        Return r
    End Function

    ''' <summary>
    ''' 天空：以頂部幾列的顏色為模型，從頂端往下區域成長，只接受和鄰點相近、又和天空模型（容許往下變亮變淡）不太遠的像素。
    ''' 頂部本身就很雜（樹葉、建築、室內）時當成沒有天空。
    ''' </summary>
    Public Shared Function EstimateSky(px As Byte(), w As Integer, h As Integer) As Single()
        Dim mask(w * h - 1) As Single
        Dim seedRows = Math.Max(1, CInt(h * 0.04))
        Dim mb = 0.0, mg = 0.0, mr = 0.0, n = 0
        For y = 0 To seedRows - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                mb += px(i) : mg += px(i + 1) : mr += px(i + 2) : n += 1
            Next
        Next
        mb /= n : mg /= n : mr /= n
        Dim varSum = 0.0
        For y = 0 To seedRows - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                varSum += (px(i) - mb) ^ 2 + (px(i + 1) - mg) ^ 2 + (px(i + 2) - mr) ^ 2
            Next
        Next
        Dim seedStd = Math.Sqrt(varSum / n / 3)
        If seedStd > 40 Then Return mask
        Dim globalTol = 60 + seedStd * 3
        Dim localTol = 14 + seedStd * 0.8
        Dim dist = Function(i As Integer) Math.Sqrt((px(i * 4) - mb) ^ 2 + (px(i * 4 + 1) - mg) ^ 2 + (px(i * 4 + 2) - mr) ^ 2)
        Dim near = Function(a As Integer, b As Integer) Math.Sqrt((CInt(px(a * 4)) - px(b * 4)) ^ 2 + (CInt(px(a * 4 + 1)) - px(b * 4 + 1)) ^ 2 + (CInt(px(a * 4 + 2)) - px(b * 4 + 2)) ^ 2)

        Dim visited(w * h - 1) As Boolean
        Dim queue As New Queue(Of Integer)()
        For x = 0 To w - 1
            If dist(x) <= globalTol Then visited(x) = True : queue.Enqueue(x)
        Next
        While queue.Count > 0
            Dim i = queue.Dequeue()
            mask(i) = 1
            Dim x = i Mod w, y = i \ w
            For Each nb In {If(x > 0, i - 1, -1), If(x < w - 1, i + 1, -1), If(y > 0, i - w, -1), If(y < h - 1, i + w, -1)}
                If nb < 0 OrElse visited(nb) Then Continue For
                If near(i, nb) > localTol Then Continue For
                ' 天空往地平線會變亮、變淡：越往下容許差越大。
                If dist(nb) > globalTol * (1 + 0.8 * (nb \ w) / CDbl(h)) Then Continue For
                visited(nb) = True
                queue.Enqueue(nb)
            Next
        End While
        ' 去掉細小的天空孔洞（結構裡的縫隙不算可站立的天際線）：3×3 侵蝕再膨脹。
        Return Morph(Morph(mask, w, h, erode:=True), w, h, erode:=False)
    End Function

    Private Shared Function Morph(m As Single(), w As Integer, h As Integer, erode As Boolean) As Single()
        Dim r(m.Length - 1) As Single
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim v = If(erode, 1.0F, 0.0F)
                For dy = -1 To 1
                    For dx = -1 To 1
                        Dim xx = Math.Clamp(x + dx, 0, w - 1), yy = Math.Clamp(y + dy, 0, h - 1)
                        Dim s = m(yy * w + xx)
                        v = If(erode, Math.Min(v, s), Math.Max(v, s))
                    Next
                Next
                r(y * w + x) = v
            Next
        Next
        Return r
    End Function

    Private Structure Plateau
        Public Left As Integer
        Public Right As Integer
        Public Y As Double
        Public Center As Double
        Public Score As Double
    End Structure

    ''' <summary>天際線上「局部最高、平坦、夠寬」的區段。</summary>
    Private Shared Function FindPlateaus(line As Integer(), w As Integer, h As Integer) As List(Of Plateau)
        Dim result As New List(Of Plateau)()
        Dim tol = Math.Max(2, CInt(h * 0.015))
        Dim minWidth = Math.Max(3, CInt(w * 0.025))
        Dim x = 0
        While x < w
            If line(x) >= h * 0.92 OrElse line(x) <= 0 Then
                x += 1
                Continue While
            End If
            Dim startX = x, ref = line(x)
            Dim sum = 0.0, cnt = 0
            While x < w AndAlso Math.Abs(line(x) - ref) <= tol AndAlso line(x) < h * 0.92
                sum += line(x) : cnt += 1
                x += 1
            End While
            Dim endX = x - 1
            Dim width = endX - startX + 1
            If width >= minWidth Then
                Dim y = sum / cnt
                ' 兩側比頂面低（或是畫面邊緣）：真正的頂面是局部最高點。
                Dim leftLower = startX = 0 OrElse line(Math.Max(0, startX - 1)) > y + tol
                Dim rightLower = endX = w - 1 OrElse line(Math.Min(w - 1, endX + 1)) > y + tol
                If leftLower OrElse rightLower Then
                    Dim flat = 0.0
                    For k = startX To endX
                        flat += Math.Abs(line(k) - y)
                    Next
                    flat = 1 - Math.Min(1, flat / cnt / tol)
                    Dim center = (startX + endX + 1) / 2.0
                    Dim centrality = 1 - Math.Abs(center / w - 0.5) * 1.2
                    ' 寬度適中最好（畫面寬 5%～35%），太寬的通常是遠方地平線。
                    Dim wf = width / CDbl(w)
                    Dim widthScore = If(wf < 0.05, wf / 0.05, If(wf > 0.35, Math.Max(0, 1 - (wf - 0.35) * 2), 1))
                    Dim headroom = Math.Min(1, y / (h * 0.3))
                    Dim bothSides = If(leftLower AndAlso rightLower, 1.0, 0.7)
                    Dim score = 0.3 * widthScore + 0.2 * flat + 0.2 * centrality + 0.2 * headroom + 0.1 * bothSides
                    result.Add(New Plateau With {.Left = startX, .Right = endX, .Y = y, .Center = center, .Score = Math.Clamp(score, 0, 1)})
                End If
            End If
            If x = startX Then x += 1
        End While
        Return result
    End Function

    ''' <summary>粗略光源方向：比較上半部左右兩側的亮度，差距太小時回傳 0。</summary>
    Private Shared Function EstimateLightSide(px As Byte(), w As Integer, h As Integer) As Double
        Dim l = 0.0, r = 0.0, nl = 0, nr = 0
        For y = 0 To h \ 2
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                Dim lum = 0.0722 * px(i) + 0.7152 * px(i + 1) + 0.2126 * px(i + 2)
                If x < w \ 2 Then
                    l += lum : nl += 1
                Else
                    r += lum : nr += 1
                End If
            Next
        Next
        If nl = 0 OrElse nr = 0 Then Return 0
        Dim diff = (r / nr - l / nl) / 255
        If Math.Abs(diff) < 0.03 Then Return 0
        Return Math.Clamp(diff * 5, -1, 1)
    End Function
End Class

'=====================================================================
' 自動定位
'=====================================================================

''' <summary>一個候選擺放：人物錨點放到背景 (TargetX, TargetY)，縮放 Scale（背景像素 / 人物像素）。</summary>
Public Class PlacementCandidate
    Public Property TargetX As Double
    Public Property TargetY As Double
    Public Property Scale As Double
    ''' <summary>0..1，越高越好。</summary>
    Public Property Score As Double
    Public Property Penalties As New Dictionary(Of String, Double)()
    Public Property Description As String = ""

    Public Function PenaltySummary() As String
        Return String.Join("，", Penalties.Where(Function(kv) kv.Value > 0.005).Select(Function(kv) $"{kv.Key} -{kv.Value:0.00}"))
    End Function
End Class

''' <summary>
''' 自動定位：人物錨點對齊背景推薦的站立點，依可站立寬度與上方空間決定大小，再評估多個候選（超出畫面、超出站立面、腳下懸空、偏離建議尺寸）。
''' 半身像沒有腳可以站：裁切邊貼齊畫面下緣，高度約畫面 60%，放在中央、左右三分之一。
''' </summary>
Public NotInheritable Class CompositePlacement
    Private Sub New()
    End Sub

    Private Shared ReadOnly ScaleMultipliers As Double() = {0.75, 0.85, 1.0, 1.15, 1.3}
    Private Shared ReadOnly OffsetFractions As Double() = {-0.15, 0, 0.15}
    Private Const GridSamples As Integer = 32

    ''' <summary>fig 與 alpha 都是翻轉後（顯示方向）的人物；回傳依分數排好的候選。</summary>
    Public Shared Function Suggest(scene As SceneAnalysis, fig As FigureAnalysis, alpha As Byte(), fw As Integer, fh As Integer) As List(Of PlacementCandidate)
        Dim bw = scene.Width, bh = scene.Height
        Dim anchor = fig.Anchor
        Dim candidates As New List(Of PlacementCandidate)()
        If fig.BottomCropped Then
            Dim baseScale = bh * 0.6 / fig.Height
            Dim maxScale = bh * 0.95 / fig.Height
            Dim positions = {(0.5, Lang.T("畫面中央")), (1 / 3.0, Lang.T("左三分之一")), (2 / 3.0, Lang.T("右三分之一"))}
            For Each mult In ScaleMultipliers
                Dim s = Math.Min(maxScale, baseScale * mult)
                For Each pos In positions
                    Dim c = Evaluate(scene, fig, alpha, fw, fh, anchor, bw * pos.Item1, bh, s, 0, 0, mult)
                    If pos.Item1 <> 0.5 Then c.Penalties(Lang.T("偏離中央")) = 0.02 : c.Score = Math.Max(0, c.Score - 0.02)
                    c.Description = Lang.F("半身像：裁切邊貼齊畫面下緣，{0}；高度約畫面 {1:P0}", pos.Item2, fig.Height * s / bh)
                    candidates.Add(c)
                Next
            Next
        Else
            Dim sug = scene.Suggestions(0)
            Dim surfaceWidth = sug.SurfaceWidth
            ' 站在有限寬度的平台上：腳寬約占平台 55%；人物高度不超過站立點上方空間的 85%，也不小於畫面 6%。
            Dim baseScale = If(surfaceWidth > 0, surfaceWidth * 0.55 / fig.FeetWidth, bh * 0.4 / fig.Height)
            baseScale = Math.Min(baseScale, Math.Max(0.01, sug.AnchorY * 0.85) / fig.Height)
            baseScale = Math.Max(baseScale, bh * 0.06 / fig.Height)
            Dim offsetUnit = If(surfaceWidth > 0, surfaceWidth, fig.FeetWidth * baseScale)
            For Each mult In ScaleMultipliers
                For Each off In OffsetFractions
                    Dim c = Evaluate(scene, fig, alpha, fw, fh, anchor, sug.AnchorX + off * offsetUnit, sug.AnchorY, baseScale * mult,
                                     sug.SurfaceLeft, sug.SurfaceRight, mult)
                    c.Description = sug.Reason & Lang.F("；高度約畫面 {0:P0}", fig.Height * baseScale * mult / bh) &
                                    If(off <> 0, Lang.F("，往{0}一點", If(off < 0, Lang.T("左"), Lang.T("右"))), "")
                    candidates.Add(c)
                Next
            Next
        End If
        candidates.Sort(Function(a, b) b.Score.CompareTo(a.Score))
        Return candidates
    End Function

    ''' <summary>評估一個候選（錨點 anchor 放到 (tx, ty)，縮放 s）。</summary>
    Private Shared Function Evaluate(scene As SceneAnalysis, fig As FigureAnalysis, alpha As Byte(), fw As Integer, fh As Integer, anchor As PointF,
                                     tx As Double, ty As Double, s As Double, surfL As Double, surfR As Double, mult As Double) As PlacementCandidate
        Dim bw = scene.Width, bh = scene.Height
        Dim p As New Dictionary(Of String, Double)()
        Dim toBg = Function(x As Double, y As Double) New PointF(CSng(tx + (x - anchor.X) * s), CSng(ty + (y - anchor.Y) * s))
        ' 格點取樣人物前景，估計超出畫面的比例（半身像的裁切邊剛好在畫面下緣，不算超出）。
        Dim total = 0, outside = 0
        Dim b = fig.Bounds
        For gy = 0 To GridSamples - 1
            For gx = 0 To GridSamples - 1
                Dim sx = CInt(b.X + (gx + 0.5) * b.Width / GridSamples), sy = CInt(b.Y + (gy + 0.5) * b.Height / GridSamples)
                If alpha(Math.Min(fh - 1, sy) * fw + Math.Min(fw - 1, sx)) < 128 Then Continue For
                total += 1
                Dim q = toBg(sx, sy)
                If q.X < 0 OrElse q.Y < 0 OrElse q.X >= bw OrElse q.Y >= bh Then outside += 1
            Next
        Next
        If total > 0 Then p(Lang.T("超出畫面")) = 1.5 * outside / total
        If Not fig.BottomCropped Then
            Dim feetL = toBg(fig.FeetLeft, fig.FeetY).X, feetR = toBg(fig.FeetRight, fig.FeetY).X
            If surfR > surfL Then
                Dim over = Math.Max(0, surfL - feetL) + Math.Max(0, feetR - surfR)
                p(Lang.T("超出站立面")) = 0.8 * Math.Min(1, over / (surfR - surfL))
            End If
            ' 腳下是天空（懸空）。
            Dim skyUnder = 0.0, n = 0
            For k = 0 To 8
                Dim x = feetL + (feetR - feetL) * k / 8.0
                Dim y = ty + Math.Max(2, fig.Height * s * 0.01)
                If x < 0 OrElse x >= bw OrElse y < 0 OrElse y >= bh Then Continue For
                skyUnder += scene.SkyAt(x, y) : n += 1
            Next
            If n > 0 Then p(Lang.T("腳下懸空")) = 1.2 * skyUnder / n
        End If
        p(Lang.T("偏離建議尺寸")) = 0.25 * Math.Abs(Math.Log(mult))
        Return New PlacementCandidate With {.TargetX = tx, .TargetY = ty, .Scale = s, .Penalties = p, .Score = Math.Max(0, 1 - p.Values.Sum())}
    End Function

    ''' <summary>
    ''' 地平線輔助：一般拍照時相機和眼睛差不多高，站在地上的人眼睛會落在地平線上。腳在 feetY、地平線在 horizonY 時，
    ''' 人物高度＝(feetY − horizonY) ÷ 眼睛高度比例（眼睛約在身高 93% 處）。腳在地平線上方時回傳 0（不適用）。
    ''' </summary>
    Public Const EyeFraction As Double = 0.93

    Public Shared Function HeightFromHorizon(feetY As Double, horizonY As Double) As Double
        If feetY <= horizonY + 1 Then Return 0
        Return (feetY - horizonY) / EyeFraction
    End Function

    ''' <summary>
    ''' 圖片物件在主畫面上被移動、縮放過：由套用當時的位置（layerX, layerY, layerSize）與現在的位置換算錨點與縮放倍率。
    ''' 回傳新的錨點位置（照片 0..1）與倍率（現在 ÷ 當時）。
    ''' </summary>
    Public Shared Function FollowLayer(targetX As Double, targetY As Double, layerX As Double, layerY As Double, layerSize As Double,
                                       nowX As Double, nowY As Double, nowSize As Double) As (TargetX As Double, TargetY As Double, Factor As Double)
        If layerSize <= 0 Then Return (targetX, targetY, 1)
        Dim k = nowSize / layerSize
        ' 照片 0..1 座標：縮放以物件中心為準；寬高比固定，水平與垂直用同一個倍率（以照片高度為單位換算）
        Return (nowX + (targetX - layerX) * k, nowY + (targetY - layerY) * k, k)
    End Function

    ''' <summary>
    ''' 換算成圖片物件的位置：做好的人物 PNG 是人物圖裡 crop 那一塊（顯示方向），錨點 anchor（顯示方向）放在背景 (tx, ty)、縮放 s。
    ''' 回傳物件中心（照片 0..1）與高度（照片高度的比例）。
    ''' </summary>
    Public Shared Function ToOverlay(crop As Rectangle, anchorX As Double, anchorY As Double, tx As Double, ty As Double, s As Double,
                                     bw As Integer, bh As Integer) As (X As Double, Y As Double, Size As Double)
        Dim cx = tx + (crop.X + crop.Width / 2.0 - anchorX) * s
        Dim cy = ty + (crop.Y + crop.Height / 2.0 - anchorY) * s
        Return (cx / bw, cy / bh, crop.Height * s / bh)
    End Function

    ''' <summary>ToOverlay 的反運算：由物件位置、大小算回錨點在背景上的位置與縮放（回到引導視窗再修改時用）。</summary>
    Public Shared Function FromOverlay(crop As Rectangle, anchorX As Double, anchorY As Double, x As Double, y As Double, size As Double,
                                       bw As Integer, bh As Integer) As (TargetX As Double, TargetY As Double, Scale As Double)
        Dim s = size * bh / Math.Max(1, crop.Height)
        Dim tx = x * bw - (crop.X + crop.Width / 2.0 - anchorX) * s
        Dim ty = y * bh - (crop.Y + crop.Height / 2.0 - anchorY) * s
        Return (tx, ty, s)
    End Function
End Class

'=====================================================================
' 色彩匹配
'=====================================================================

''' <summary>區域色彩統計（0..1）。</summary>
Public Class CompositeColorStats
    Public Property MeanLuma As Double
    Public Property StdLuma As Double
    Public Property MeanSaturation As Double
    ''' <summary>亮部（亮度前 30%）的 R − B，偏暖程度。</summary>
    Public Property Warmth As Double
    ''' <summary>亮部的 G −（R+B）/2，偏綠程度。</summary>
    Public Property GreenShift As Double
    Public Property MeanR As Double
    Public Property MeanG As Double
    Public Property MeanB As Double
    Public Property SampleCount As Integer
End Class

''' <summary>
''' 人物的色彩融合：EnvironmentMatch 依放置處附近的背景做輕度調整，每一項只往背景移動一小部分而且有上限，
''' 保留人物（尤其是動漫、插畫）原本的線條與配色，只讓亮度、色溫、對比往環境靠攏。
''' </summary>
Public NotInheritable Class CompositeColorMatch
    Private Sub New()
    End Sub

    ''' <summary>套用色彩（BGRA，就地修改；透明度不變）。</summary>
    Public Shared Sub Apply(px As Byte(), w As Integer, h As Integer, c As CompositeColor)
        If c Is Nothing OrElse c.IsIdentity Then Return
        Dim tR = 1 + 0.12 * c.Temperature, tB = 1 - 0.12 * c.Temperature, tG = 1 - 0.08 * c.Tint
        Dim gr = CSng(c.GainR * tR), gg = CSng(c.GainG * tG), gb = CSng(c.GainB * tB)
        Dim br = CSng(c.Brightness), ct = CSng(c.Contrast), sat = CSng(c.Saturation)
        Dim k = CSng(Math.Clamp(c.Strength / 100.0, 0, 1))
        Threading.Tasks.Parallel.For(0, h, Sub(y)
                                               Dim row = y * w * 4
                                               For x = 0 To w - 1
                                                   Dim i = row + x * 4
                                                   If px(i + 3) = 0 Then Continue For
                                                   Dim b0 = px(i) / 255.0F, g0 = px(i + 1) / 255.0F, r0 = px(i + 2) / 255.0F
                                                   Dim r = r0 * gr + br, g = g0 * gg + br, b = b0 * gb + br
                                                   r = (r - 0.5F) * ct + 0.5F : g = (g - 0.5F) * ct + 0.5F : b = (b - 0.5F) * ct + 0.5F
                                                   Dim l = 0.2126F * r + 0.7152F * g + 0.0722F * b
                                                   r = l + (r - l) * sat : g = l + (g - l) * sat : b = l + (b - l) * sat
                                                   px(i) = ToByte(b0 + (b - b0) * k)
                                                   px(i + 1) = ToByte(g0 + (g - g0) * k)
                                                   px(i + 2) = ToByte(r0 + (r - r0) * k)
                                               Next
                                           End Sub)
    End Sub

    Private Shared Function ToByte(v As Single) As Byte
        Return CByte(Math.Clamp(CInt(Math.Round(v * 255)), 0, 255))
    End Function

    ''' <summary>
    ''' 推薦環境匹配：比較 region（背景像素，人物附近）與人物前景的統計；bg、fig 都是 BGRA，alpha 是人物透明度。
    ''' </summary>
    Public Shared Function Recommend(bg As Byte(), bw As Integer, bh As Integer, region As Rectangle,
                                     fig As Byte(), fw As Integer, fh As Integer, alpha As Byte()) As CompositeColor
        Dim b = Measure(bg, bw, bh, region, Nothing)
        Dim f = Measure(fig, fw, fh, New Rectangle(0, 0, fw, fh), alpha)
        Dim c As New CompositeColor With {.Mode = CompositeColorMode.Environment}
        If b.SampleCount = 0 OrElse f.SampleCount = 0 Then Return c
        c.Brightness = Math.Clamp((b.MeanLuma - f.MeanLuma) * 0.25, -0.12, 0.12)
        c.Contrast = Math.Clamp(1 + (SafeRatio(b.StdLuma, f.StdLuma) - 1) * 0.3, 0.85, 1.15)
        c.Saturation = Math.Clamp(1 + (SafeRatio(b.MeanSaturation, f.MeanSaturation) - 1) * 0.3, 0.8, 1.1)
        c.Temperature = Math.Clamp((b.Warmth - f.Warmth) * 1.5, -0.4, 0.4)
        c.Tint = Math.Clamp(-(b.GreenShift - f.GreenShift) * 1.5, -0.3, 0.3)
        ' 環境色偏：背景平均色相對灰色的偏差取 15%。
        Dim bm = (b.MeanR + b.MeanG + b.MeanB) / 3
        If bm > 0.02 Then
            c.GainR = Math.Clamp(1 + (b.MeanR / bm - 1) * 0.15, 0.92, 1.08)
            c.GainG = Math.Clamp(1 + (b.MeanG / bm - 1) * 0.15, 0.92, 1.08)
            c.GainB = Math.Clamp(1 + (b.MeanB / bm - 1) * 0.15, 0.92, 1.08)
        End If
        Return c
    End Function

    Private Shared Function SafeRatio(a As Double, b As Double) As Double
        If b < 0.0001 Then Return 1
        Return Math.Clamp(a / b, 0.2, 5)
    End Function

    ''' <summary>統計區域色彩（BGRA）；alpha 不為 Nothing 時只看前景（透明度 ≥ 50%）。大區域跳點取樣。</summary>
    Public Shared Function Measure(px As Byte(), w As Integer, h As Integer, region As Rectangle, alpha As Byte()) As CompositeColorStats
        Dim r = Rectangle.Intersect(region, New Rectangle(0, 0, w, h))
        Dim st As New CompositeColorStats()
        If r.Width <= 0 OrElse r.Height <= 0 Then Return st
        Dim stepPx = Math.Max(1, CInt(Math.Sqrt(CDbl(r.Width) * r.Height / 40000)))
        Dim samples As New List(Of (L As Double, R As Double, G As Double, B As Double))()
        Dim sl = 0.0, sl2 = 0.0, ss = 0.0, sr = 0.0, sg = 0.0, sb = 0.0
        For y = r.Y To r.Bottom - 1 Step stepPx
            For x = r.X To r.Right - 1 Step stepPx
                If alpha IsNot Nothing AndAlso alpha(y * w + x) < 128 Then Continue For
                Dim i = (y * w + x) * 4
                Dim bb = px(i) / 255.0, gg = px(i + 1) / 255.0, rr = px(i + 2) / 255.0
                Dim l = 0.2126 * rr + 0.7152 * gg + 0.0722 * bb
                Dim mx = Math.Max(rr, Math.Max(gg, bb)), mn = Math.Min(rr, Math.Min(gg, bb))
                sl += l : sl2 += l * l : ss += If(mx > 0.0001, (mx - mn) / mx, 0)
                sr += rr : sg += gg : sb += bb
                samples.Add((l, rr, gg, bb))
            Next
        Next
        Dim n = samples.Count
        If n = 0 Then Return st
        st.SampleCount = n
        st.MeanLuma = sl / n
        st.StdLuma = Math.Sqrt(Math.Max(0, sl2 / n - st.MeanLuma * st.MeanLuma))
        st.MeanSaturation = ss / n
        st.MeanR = sr / n : st.MeanG = sg / n : st.MeanB = sb / n
        ' 亮部色溫：亮度前 30%。
        samples.Sort(Function(a, b2) b2.L.CompareTo(a.L))
        Dim top = Math.Max(1, CInt(n * 0.3))
        Dim wr = 0.0, wg = 0.0, wb = 0.0
        For i = 0 To top - 1
            wr += samples(i).R : wg += samples(i).G : wb += samples(i).B
        Next
        st.Warmth = (wr - wb) / top
        st.GreenShift = (wg - (wr + wb) / 2) / top
        Return st
    End Function
End Class
