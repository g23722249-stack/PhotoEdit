Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>繪圖圖層的種類（數值存進編輯檔，不可更動；工具列順序見 DrawToolStrip）。</summary>
Public Enum DrawShape
    Freehand = 0
    Line = 1
    Bezier = 2
    Rectangle = 3
    RoundRect = 4
    Ellipse = 5
    Polygon = 6
    Arrow = 7
    Heart = 8
    Star4 = 9
    Star5 = 10
    Star6 = 11
    CalloutRect = 12
    CalloutEllipse = 13
    CalloutCloud = 14
    CalloutBubble = 15
    CalloutShout = 16
    Lightning = 17
    ''' <summary>點陣圖層：直接繪製的筆畫、橡皮擦與點陣化的圖形依序疊成像素（見 DrawLayer.Ops）。</summary>
    Raster = 18
End Enum

''' <summary>筆刷（數值存進編輯檔，不可更動）。</summary>
Public Enum BrushKind
    HardRound = 0
    SoftRound = 1
    Pencil = 2
    Charcoal = 3
    Chalk = 4
    Crayon = 5
    Watercolor = 6
    OilPaint = 7
    Acrylic = 8
    Ink = 9
    ChineseBrush = 10
    Marker = 11
    Airbrush = 12
    DryBrush = 13
    Texture = 14
    FX = 15
End Enum

''' <summary>筆畫上的一點：座標以「照片高度 = 1」為單位，P 為筆壓 0..1。</summary>
Public Class DrawPoint
    Public Property X As Single
    Public Property Y As Single
    Public Property P As Single = 1

    Public Sub New()
    End Sub

    Public Sub New(x As Single, y As Single, Optional p As Single = 1)
        Me.X = x : Me.Y = y : Me.P = p
    End Sub

    Public Function Clone() As DrawPoint
        Return New DrawPoint(X, Y, P)
    End Function

    Public Function ToPointF() As PointF
        Return New PointF(X, Y)
    End Function
End Class

''' <summary>自由繪製的一筆。</summary>
Public Class DrawStroke
    Public Property Points As List(Of DrawPoint) = New List(Of DrawPoint)()

    Public Function Clone() As DrawStroke
        Return New DrawStroke With {.Points = Points.Select(Function(p) p.Clone()).ToList()}
    End Function
End Class

''' <summary>
''' 一個繪圖圖層。座標單位是「輸出照片（裁切後、加邊框前）的高度 = 1」：X 介於 0..寬高比、Y 介於 0..1，
''' 所以換裁切比例時形狀不會變形。
''' 方框類形狀（矩形、橢圓、星形、圖說…）用中心 X、Y、寬 W、高 H 與旋轉角度描述；
''' 直線、貝茲曲線、多邊形用 Points；自由繪製用 Strokes。
''' </summary>
Public Class DrawLayer
    Public Property Shape As DrawShape
    Public Property Name As String = ""
    Public Property Visible As Boolean = True
    Public Property Locked As Boolean

    ' ---- 方框類 ----
    Public Property X As Double
    Public Property Y As Double
    Public Property W As Double = 0.2
    Public Property H As Double = 0.15
    ''' <summary>順時針旋轉角度（度），以中心為軸。</summary>
    Public Property Rotation As Double
    ''' <summary>形狀參數：圓角比例、星形內徑比例、箭頭的箭頭長度比例。</summary>
    Public Property Param1 As Double
    ''' <summary>箭頭的箭身粗細比例。</summary>
    Public Property Param2 As Double
    ''' <summary>圖說的指示尖端：相對中心、未旋轉的位移。</summary>
    Public Property TailX As Double
    Public Property TailY As Double

    ' ---- 點類 ----
    Public Property Points As List(Of DrawPoint)
    Public Property Strokes As List(Of DrawStroke)

    ' ---- 圖說文字 ----
    Public Property Text As String = ""
    Public Property FontName As String = "Microsoft JhengHei"
    Public Property TextColorArgb As Integer = Color.FromArgb(30, 30, 34).ToArgb()
    ''' <summary>字高（照片高度的比例）。</summary>
    Public Property TextSize As Double = 0.045
    Public Property TextBold As Boolean = True

    ' ---- 筆觸 ----
    Public Property Brush As BrushKind
    Public Property Fx As FxKind
    Public Property Material As MaterialKind
    Public Property StrokeColorArgb As Integer = Color.FromArgb(232, 69, 90).ToArgb()
    Public Property FillColorArgb As Integer = Color.White.ToArgb()
    ''' <summary>線條粗細（照片高度的比例）。</summary>
    Public Property StrokeWidth As Double = 0.008
    ''' <summary>邊緣淡化 0..100。</summary>
    Public Property Softness As Integer
    ''' <summary>不透明度 0..100。</summary>
    Public Property Opacity As Integer = 100
    ''' <summary>流量（顏料濃度）0..100。</summary>
    Public Property Flow As Integer = 100
    Public Property Filled As Boolean
    Public Property Stroked As Boolean = True
    Public Property Shadow As Boolean
    ''' <summary>紋理與特效的亂數種子：同一個圖層每次畫出來都一樣。</summary>
    Public Property Seed As Integer = 1

    ' ---- 點陣圖層 ----
    ''' <summary>
    ''' 點陣圖層的內容：由下而上依序畫上去的筆畫（每筆是一個單筆的自由繪製圖層，帶自己的筆刷與顏色）、
    ''' 橡皮擦（Eraser = True）與點陣化前的圖形。畫上去就合成像素、不能再個別修改；
    ''' 存成操作紀錄而不是圖片，所以編輯檔很小，匯出大圖也一樣清楚。X、Y 是整個圖層的位移。
    ''' </summary>
    Public Property Ops As List(Of DrawLayer)
    ''' <summary>點陣圖層裡的一筆橡皮擦：用筆刷的形狀擦掉下面已經畫上去的像素。</summary>
    Public Property Eraser As Boolean

    Public Function Clone() As DrawLayer
        Dim c = DirectCast(MemberwiseClone(), DrawLayer)
        c.Points = Points?.Select(Function(p) p.Clone()).ToList()
        c.Strokes = Strokes?.Select(Function(s) s.Clone()).ToList()
        c.Ops = Ops?.Select(Function(o) o.Clone()).ToList()
        Return c
    End Function

    ''' <summary>複製筆觸與顏色設定（不含形狀、位置、文字內容）。</summary>
    Public Sub CopyStyleFrom(s As DrawLayer)
        Brush = s.Brush : Fx = s.Fx : Material = s.Material
        StrokeColorArgb = s.StrokeColorArgb : FillColorArgb = s.FillColorArgb
        StrokeWidth = s.StrokeWidth : Softness = s.Softness : Opacity = s.Opacity : Flow = s.Flow
        Filled = s.Filled : Stroked = s.Stroked : Shadow = s.Shadow
        FontName = s.FontName : TextColorArgb = s.TextColorArgb : TextSize = s.TextSize : TextBold = s.TextBold
    End Sub

    ''' <summary>和另一個圖層的筆觸設定相同（自由繪製時決定要不要接著畫在同一圖層）。</summary>
    Public Function SameStroke(s As DrawLayer) As Boolean
        Return Brush = s.Brush AndAlso Fx = s.Fx AndAlso Material = s.Material AndAlso StrokeColorArgb = s.StrokeColorArgb AndAlso
               Math.Abs(StrokeWidth - s.StrokeWidth) < 0.00001 AndAlso Softness = s.Softness AndAlso Opacity = s.Opacity AndAlso
               Flow = s.Flow AndAlso Shadow = s.Shadow
    End Function

    <System.Text.Json.Serialization.JsonIgnore>
    Public ReadOnly Property Center As PointF
        Get
            Return New PointF(CSng(X), CSng(Y))
        End Get
    End Property
End Class

''' <summary>繪圖圖層的形狀幾何、點選與編輯（全部以照片高度為單位，畫布與算圖共用）。</summary>
Public NotInheritable Class DrawGeometry
    Private Sub New()
    End Sub

    Public Shared ReadOnly ShapeNames As String() = {
        "自由繪製（向量）", "直線", "貝茲曲線", "矩形", "圓角矩形", "橢圓", "多邊形", "箭頭", "愛心",
        "四角星形", "五角星形", "六角星形", "圓角矩形圖說", "橢圓圖說", "雲朵圖說", "氣泡圖說", "吶喊框", "閃電", "直接繪製"}

    Public Shared ReadOnly BrushNames As String() = {
        "硬筆", "軟筆", "鉛筆", "炭筆", "粉筆", "蠟筆", "水彩", "油畫", "壓克力", "墨水", "毛筆", "麥克筆", "噴槍", "乾刷", "紋理筆", "特效筆"}

    ''' <summary>特效與材質名稱（依列舉值）；分類與預設顏色見 EffectCatalog。</summary>
    Public Shared ReadOnly Property FxNames As String()
        Get
            Return EffectCatalog.FxNames
        End Get
    End Property

    Public Shared ReadOnly Property MaterialNames As String()
        Get
            Return EffectCatalog.MaterialNames
        End Get
    End Property

    Public Shared Function IsBox(s As DrawShape) As Boolean
        Return s >= DrawShape.Rectangle AndAlso s <> DrawShape.Polygon AndAlso s <> DrawShape.Raster
    End Function

    Public Shared Function IsCallout(s As DrawShape) As Boolean
        Return s >= DrawShape.CalloutRect AndAlso s <= DrawShape.CalloutShout
    End Function

    ''' <summary>以點描述、可個別拖曳各點的形狀。</summary>
    Public Shared Function IsPointShape(s As DrawShape) As Boolean
        Return s = DrawShape.Line OrElse s = DrawShape.Bezier OrElse s = DrawShape.Polygon
    End Function

    Public Shared Function IsClosed(s As DrawShape) As Boolean
        Return s <> DrawShape.Freehand AndAlso s <> DrawShape.Line AndAlso s <> DrawShape.Bezier AndAlso s <> DrawShape.Raster
    End Function

    ''' <summary>新圖層的預設形狀參數。</summary>
    Public Shared Sub ApplyDefaults(layer As DrawLayer)
        Select Case layer.Shape
            Case DrawShape.RoundRect, DrawShape.CalloutRect : layer.Param1 = 0.18
            Case DrawShape.Star4 : layer.Param1 = 0.38
            Case DrawShape.Star5 : layer.Param1 = 0.45
            Case DrawShape.Star6 : layer.Param1 = 0.58
            Case DrawShape.Arrow : layer.Param1 = 0.4 : layer.Param2 = 0.45
        End Select
        If IsCallout(layer.Shape) Then
            layer.TailX = -layer.W * 0.3
            layer.TailY = layer.H * 0.95
        End If
    End Sub

    '=====================================================================
    ' 旋轉
    '=====================================================================

    ''' <summary>順時針轉 degrees 度（y 軸向下）。</summary>
    Public Shared Function Rotate(p As PointF, degrees As Double) As PointF
        If Math.Abs(degrees) < 0.0001 Then Return p
        Dim a = degrees * Math.PI / 180
        Dim c = Math.Cos(a), s = Math.Sin(a)
        Return New PointF(CSng(p.X * c - p.Y * s), CSng(p.X * s + p.Y * c))
    End Function

    Public Shared Function LocalToWorld(layer As DrawLayer, p As PointF) As PointF
        Dim r = Rotate(p, layer.Rotation)
        Return New PointF(CSng(layer.X + r.X), CSng(layer.Y + r.Y))
    End Function

    Public Shared Function WorldToLocal(layer As DrawLayer, p As PointF) As PointF
        Return Rotate(New PointF(CSng(p.X - layer.X), CSng(p.Y - layer.Y)), -layer.Rotation)
    End Function

    '=====================================================================
    ' 外形
    '=====================================================================

    ''' <summary>一條外形線：點（照片高度單位）與是否封閉。</summary>
    Public Structure Figure
        Public Points As PointF()
        Public Closed As Boolean
        ''' <summary>各點筆壓（自由繪製）；Nothing 表示全部為 1。</summary>
        Public Pressure As Single()
    End Structure

    ''' <summary>圖層的所有外形線（世界座標）。</summary>
    Public Shared Function Figures(layer As DrawLayer) As List(Of Figure)
        Dim list As New List(Of Figure)()
        Select Case layer.Shape
            Case DrawShape.Freehand
                If layer.Strokes IsNot Nothing Then
                    For Each s In layer.Strokes
                        If s.Points.Count = 0 Then Continue For
                        Dim sm = Smooth(s.Points)
                        list.Add(New Figure With {.Points = sm.Select(Function(p) p.ToPointF()).ToArray(), .Pressure = sm.Select(Function(p) p.P).ToArray()})
                    Next
                End If
            Case DrawShape.Line
                If layer.Points IsNot Nothing AndAlso layer.Points.Count >= 2 Then
                    list.Add(New Figure With {.Points = {layer.Points(0).ToPointF(), layer.Points(1).ToPointF()}})
                End If
            Case DrawShape.Bezier
                If layer.Points IsNot Nothing AndAlso layer.Points.Count >= 4 Then
                    list.Add(New Figure With {.Points = SampleBezier(layer.Points(0).ToPointF(), layer.Points(1).ToPointF(),
                                                                     layer.Points(2).ToPointF(), layer.Points(3).ToPointF(), 64)})
                End If
            Case DrawShape.Polygon
                If layer.Points IsNot Nothing AndAlso layer.Points.Count >= 2 Then
                    list.Add(New Figure With {.Points = layer.Points.Select(Function(p) p.ToPointF()).ToArray(), .Closed = layer.Points.Count >= 3})
                End If
            Case DrawShape.Raster
                ' 點陣圖層沒有外形線，範圍見 RasterBounds。
            Case Else
                For Each f In LocalFigures(layer)
                    list.Add(New Figure With {.Points = f.Points.Select(Function(p) LocalToWorld(layer, p)).ToArray(), .Closed = f.Closed})
                Next
        End Select
        Return list
    End Function

    ''' <summary>方框類形狀未旋轉、以中心為原點的外形線。</summary>
    Public Shared Function LocalFigures(layer As DrawLayer) As List(Of Figure)
        Dim w = CSng(Math.Max(0.0005, Math.Abs(layer.W))), h = CSng(Math.Max(0.0005, Math.Abs(layer.H)))
        Dim hw = w / 2, hh = h / 2
        Dim list As New List(Of Figure)()
        Dim tail As New PointF(CSng(layer.TailX), CSng(layer.TailY))
        Select Case layer.Shape
            Case DrawShape.Rectangle
                list.Add(Closed({New PointF(-hw, -hh), New PointF(hw, -hh), New PointF(hw, hh), New PointF(-hw, hh)}))
            Case DrawShape.RoundRect
                list.Add(Closed(RoundRectPoints(w, h, CSng(layer.Param1 * Math.Min(w, h)))))
            Case DrawShape.Ellipse
                list.Add(Closed(EllipsePoints(hw, hh, 96)))
            Case DrawShape.Arrow
                Dim head = CSng(Math.Max(0.05, Math.Min(1, layer.Param1)) * w)
                Dim shaft = CSng(Math.Max(0.05, Math.Min(1, layer.Param2)) * h) / 2
                list.Add(Closed({New PointF(-hw, -shaft), New PointF(hw - head, -shaft), New PointF(hw - head, -hh), New PointF(hw, 0),
                                 New PointF(hw - head, hh), New PointF(hw - head, shaft), New PointF(-hw, shaft)}))
            Case DrawShape.Heart
                Using path As New GraphicsPath()
                    path.AddBezier(0, 0.45F, -0.62F, 0.05F, -0.42F, -0.62F, 0, -0.22F)
                    path.AddBezier(0, -0.22F, 0.42F, -0.62F, 0.62F, 0.05F, 0, 0.45F)
                    path.CloseFigure()
                    path.Flatten(Nothing, 0.002F)
                    list.Add(Closed(FitTo(path.PathPoints, w, h)))
                End Using
            Case DrawShape.Star4, DrawShape.Star5, DrawShape.Star6
                Dim n = If(layer.Shape = DrawShape.Star4, 4, If(layer.Shape = DrawShape.Star5, 5, 6))
                list.Add(Closed(StarPoints(n, hw, hh, Math.Max(0.05, Math.Min(0.95, layer.Param1)))))
            Case DrawShape.Lightning
                Dim bolt = {New PointF(-0.1F, -0.5F), New PointF(0.28F, -0.5F), New PointF(0.06F, -0.12F), New PointF(0.32F, -0.12F),
                            New PointF(-0.22F, 0.5F), New PointF(-0.04F, 0.02F), New PointF(-0.3F, 0.02F)}
                list.Add(Closed(FitTo(bolt, w, h)))
            Case DrawShape.CalloutRect
                list.Add(Closed(InsertTail(RoundRectPoints(w, h, CSng(layer.Param1 * Math.Min(w, h))), tail, curved:=False, baseFrac:=0.045)))
            Case DrawShape.CalloutEllipse
                list.Add(Closed(InsertTail(EllipsePoints(hw, hh, 120), tail, curved:=False, baseFrac:=0.05)))
            Case DrawShape.CalloutBubble
                list.Add(Closed(InsertTail(SquirclePoints(hw, hh, 120), tail, curved:=True, baseFrac:=0.055)))
            Case DrawShape.CalloutCloud
                Dim body = CloudPoints(hw, hh, layer.Seed)
                list.Add(Closed(body))
                list.AddRange(CloudTail(body, tail, Math.Min(w, h)))
            Case DrawShape.CalloutShout
                list.Add(Closed(ShoutPoints(hw, hh, tail, layer.Seed)))
        End Select
        Return list
    End Function

    ''' <summary>圖說文字的範圍（未旋轉、以中心為原點）。</summary>
    Public Shared Function TextBox(layer As DrawLayer) As RectangleF
        Dim w = CSng(Math.Abs(layer.W)), h = CSng(Math.Abs(layer.H))
        Dim fx, fy As Single
        Select Case layer.Shape
            Case DrawShape.CalloutRect : fx = 0.84F : fy = 0.78F
            Case DrawShape.CalloutCloud : fx = 0.62F : fy = 0.56F
            Case DrawShape.CalloutShout : fx = 0.52F : fy = 0.46F
            Case Else : fx = 0.7F : fy = 0.64F
        End Select
        Return New RectangleF(-w * fx / 2, -h * fy / 2, w * fx, h * fy)
    End Function

    Private Shared Function Closed(pts As PointF()) As Figure
        Return New Figure With {.Points = pts, .Closed = True}
    End Function

    Private Shared Function Closed(pts As List(Of PointF)) As Figure
        Return Closed(pts.ToArray())
    End Function

    Public Shared Function EllipsePoints(rx As Single, ry As Single, n As Integer) As PointF()
        Dim pts(n - 1) As PointF
        For i = 0 To n - 1
            Dim t = -Math.PI / 2 + i * 2 * Math.PI / n
            pts(i) = New PointF(CSng(rx * Math.Cos(t)), CSng(ry * Math.Sin(t)))
        Next
        Return pts
    End Function

    ''' <summary>介於橢圓與圓角矩形之間的圓胖外形（氣泡圖說）。</summary>
    Private Shared Function SquirclePoints(rx As Single, ry As Single, n As Integer) As PointF()
        Dim pts(n - 1) As PointF
        For i = 0 To n - 1
            Dim t = -Math.PI / 2 + i * 2 * Math.PI / n
            Dim c = Math.Cos(t), s = Math.Sin(t)
            pts(i) = New PointF(CSng(rx * Math.Sign(c) * Math.Abs(c) ^ 0.7), CSng(ry * Math.Sign(s) * Math.Abs(s) ^ 0.7))
        Next
        Return pts
    End Function

    Private Shared Function RoundRectPoints(w As Single, h As Single, r As Single) As PointF()
        r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2 - 0.00001F))
        Dim hw = w / 2, hh = h / 2
        If r <= 0.00001F Then Return {New PointF(-hw, -hh), New PointF(hw, -hh), New PointF(hw, hh), New PointF(-hw, hh)}
        Dim pts As New List(Of PointF)()
        Dim corners = {(hw - r, -hh + r, -90.0), (hw - r, hh - r, 0.0), (-hw + r, hh - r, 90.0), (-hw + r, -hh + r, 180.0)}
        For Each c In corners
            For k = 0 To 8
                Dim a = (c.Item3 + k * 90.0 / 8) * Math.PI / 180
                pts.Add(New PointF(CSng(c.Item1 + r * Math.Cos(a)), CSng(c.Item2 + r * Math.Sin(a))))
            Next
        Next
        Return pts.ToArray()
    End Function

    Private Shared Function StarPoints(n As Integer, rx As Single, ry As Single, inner As Double) As PointF()
        Dim pts(n * 2 - 1) As PointF
        For i = 0 To n * 2 - 1
            Dim k = If(i Mod 2 = 0, 1.0, inner)
            Dim a = -Math.PI / 2 + i * Math.PI / n
            pts(i) = New PointF(CSng(rx * k * Math.Cos(a)), CSng(ry * k * Math.Sin(a)))
        Next
        Return pts
    End Function

    ''' <summary>把點等比例以外框對齊到 w × h（以原點為中心）。</summary>
    Private Shared Function FitTo(pts As PointF(), w As Single, h As Single) As PointF()
        Dim minX = pts.Min(Function(p) p.X), maxX = pts.Max(Function(p) p.X)
        Dim minY = pts.Min(Function(p) p.Y), maxY = pts.Max(Function(p) p.Y)
        Dim sx = w / Math.Max(0.000001F, maxX - minX), sy = h / Math.Max(0.000001F, maxY - minY)
        Return pts.Select(Function(p) New PointF((p.X - (minX + maxX) / 2) * sx, (p.Y - (minY + maxY) / 2) * sy)).ToArray()
    End Function

    ''' <summary>
    ''' 在外形上插入指向 tip 的尖角（圖說的對話指示）：從中心往 tip 的射線與外形的交點兩側各取 baseFrac 周長，
    ''' 中間換成到尖端的兩條邊；curved 時兩邊彎成漫畫泡泡的弧線。tip 在外形內時不加。
    ''' </summary>
    Public Shared Function InsertTail(body As PointF(), tip As PointF, curved As Boolean, baseFrac As Double) As PointF()
        Dim n = body.Length
        If n < 3 OrElse PointInPolygon(body, tip) Then Return body
        Dim cum(n) As Double
        For i = 0 To n - 1
            Dim a = body(i), b = body((i + 1) Mod n)
            cum(i + 1) = cum(i) + Dist(a, b)
        Next
        Dim total = cum(n)
        ' 射線（中心→tip）與外形的交點，以周長位置表示。
        Dim s0 = -1.0
        For i = 0 To n - 1
            Dim a = body(i), b = body((i + 1) Mod n)
            Dim t As Double
            If SegmentIntersect(PointF.Empty, tip, a, b, t) Then
                s0 = cum(i) + t * (cum(i + 1) - cum(i))
                Exit For
            End If
        Next
        If s0 < 0 Then Return body
        Dim half = baseFrac * total
        Dim s1 = s0 - half, s2 = s0 + half
        Dim pA = AtArc(body, cum, s1), pB = AtArc(body, cum, s2)
        Dim result As New List(Of PointF) From {pB}
        ' 從 s2 往前繞到 s1：留下周長位置不在尖角底邊範圍內的頂點，依離 s2 的距離排序。
        Dim span = total - 2 * half
        Dim kept = Enumerable.Range(0, n).
            Select(Function(i) (Index:=i, Rel:=Wrap(cum(i) - s2, total))).
            Where(Function(v) v.Rel > 0 AndAlso v.Rel < span).
            OrderBy(Function(v) v.Rel)
        For Each v In kept
            result.Add(body(v.Index))
        Next
        result.Add(pA)
        If curved Then
            Dim nx = -tip.Y, ny = tip.X
            Dim nl = CSng(Math.Max(0.000001, Math.Sqrt(nx * nx + ny * ny)))
            nx /= nl : ny /= nl
            Dim len = Dist(pA, tip)
            Dim c1 = New PointF(CSng((pA.X + tip.X) / 2 + nx * len * 0.22), CSng((pA.Y + tip.Y) / 2 + ny * len * 0.22))
            Dim c2 = New PointF(CSng((pB.X + tip.X) / 2 + nx * len * 0.12), CSng((pB.Y + tip.Y) / 2 + ny * len * 0.12))
            For k = 1 To 12
                result.Add(Quad(pA, c1, tip, k / 12.0))
            Next
            For k = 1 To 11
                result.Add(Quad(tip, c2, pB, k / 12.0))
            Next
        Else
            result.Add(tip)
        End If
        Return result.ToArray()
    End Function

    Private Shared Function Wrap(s As Double, total As Double) As Double
        Dim r = s Mod total
        If r < 0 Then r += total
        Return r
    End Function

    Private Shared Function AtArc(body As PointF(), cum As Double(), s As Double) As PointF
        Dim n = body.Length
        Dim total = cum(n)
        s = Wrap(s, total)
        For i = 0 To n - 1
            If s <= cum(i + 1) Then
                Dim seg = cum(i + 1) - cum(i)
                Dim t = If(seg <= 0, 0, (s - cum(i)) / seg)
                Dim a = body(i), b = body((i + 1) Mod n)
                Return New PointF(CSng(a.X + (b.X - a.X) * t), CSng(a.Y + (b.Y - a.Y) * t))
            End If
        Next
        Return body(0)
    End Function

    Private Shared Function Quad(a As PointF, c As PointF, b As PointF, t As Double) As PointF
        Dim u = 1 - t
        Return New PointF(CSng(u * u * a.X + 2 * u * t * c.X + t * t * b.X), CSng(u * u * a.Y + 2 * u * t * c.Y + t * t * b.Y))
    End Function

    ''' <summary>雲朵：沿橢圓排一圈向外鼓起的半圓。</summary>
    Private Shared Function CloudPoints(rx As Single, ry As Single, seed As Integer) As PointF()
        Dim baseX = rx * 0.82F, baseY = ry * 0.8F
        Dim n = 11
        Dim anchors = EllipsePoints(baseX, baseY, n)
        Dim rnd As New Random(seed)
        Dim pts As New List(Of PointF)()
        For i = 0 To n - 1
            Dim a = anchors(i), b = anchors((i + 1) Mod n)
            Dim c = New PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2)
            Dim ux = a.X - c.X, uy = a.Y - c.Y
            Dim ul = Math.Sqrt(ux * ux + uy * uy)
            ' 向外的法線（離中心較遠的那一側）。
            Dim vx = -uy, vy = ux
            If vx * c.X + vy * c.Y < 0 Then vx = -vx : vy = -vy
            Dim bulge = 0.85 + rnd.NextDouble() * 0.35
            Dim vl = Math.Sqrt(vx * vx + vy * vy)
            vx = CSng(vx / vl * ul * bulge) : vy = CSng(vy / vl * ul * bulge)
            For k = 0 To 9
                Dim t = k * Math.PI / 10
                pts.Add(New PointF(CSng(c.X + ux * Math.Cos(t) + vx * Math.Sin(t)), CSng(c.Y + uy * Math.Cos(t) + vy * Math.Sin(t))))
            Next
        Next
        Return FitTo(pts.ToArray(), rx * 2, ry * 2)
    End Function

    ''' <summary>雲朵圖說的指示：由大到小、往 tip 排列的三個小橢圓。</summary>
    Private Shared Function CloudTail(body As PointF(), tip As PointF, size As Single) As List(Of Figure)
        Dim list As New List(Of Figure)()
        If PointInPolygon(body, tip) Then Return list
        Dim edge = tip
        For i = 0 To body.Length - 1
            Dim t As Double
            If SegmentIntersect(PointF.Empty, tip, body(i), body((i + 1) Mod body.Length), t) Then
                Dim a = body(i), b = body((i + 1) Mod body.Length)
                edge = New PointF(CSng(a.X + (b.X - a.X) * t), CSng(a.Y + (b.Y - a.Y) * t))
                Exit For
            End If
        Next
        Dim specs = {(0.3, 0.1), (0.64, 0.068), (1.0, 0.04)}
        For Each s In specs
            Dim cx = edge.X + (tip.X - edge.X) * s.Item1, cy = edge.Y + (tip.Y - edge.Y) * s.Item1
            Dim r = CSng(size * s.Item2)
            Dim e = EllipsePoints(r * 1.25F, r, 32).Select(Function(p) New PointF(CSng(p.X + cx), CSng(p.Y + cy))).ToArray()
            list.Add(Closed(e))
        Next
        Return list
    End Function

    ''' <summary>吶喊框：長短不一的尖刺圍成一圈，最靠近 tip 的尖刺拉長成指示。</summary>
    Private Shared Function ShoutPoints(rx As Single, ry As Single, tip As PointF, seed As Integer) As PointF()
        Dim n = 14
        Dim rnd As New Random(seed)
        Dim pts(n * 2 - 1) As PointF
        For i = 0 To n * 2 - 1
            Dim a = -Math.PI / 2 + (i + (rnd.NextDouble() - 0.5) * 0.35) * Math.PI / n
            Dim k = If(i Mod 2 = 0, 0.86 + rnd.NextDouble() * 0.14, 0.62 + rnd.NextDouble() * 0.1)
            pts(i) = New PointF(CSng(rx * k * Math.Cos(a)), CSng(ry * k * Math.Sin(a)))
        Next
        Dim inner = pts.Where(Function(p, i) i Mod 2 = 1).ToArray()
        If Not PointInPolygon(pts, tip) Then
            ' 方向最接近 tip 的尖刺換成 tip。
            Dim ta = Math.Atan2(tip.Y / ry, tip.X / rx)
            Dim best = 0, bestD = Double.MaxValue
            For i = 0 To n * 2 - 1 Step 2
                Dim d = Math.Abs(AngleDiff(Math.Atan2(pts(i).Y / ry, pts(i).X / rx), ta))
                If d < bestD Then bestD = d : best = i
            Next
            pts(best) = tip
        End If
        Return pts
    End Function

    Private Shared Function AngleDiff(a As Double, b As Double) As Double
        Dim d = a - b
        While d > Math.PI : d -= 2 * Math.PI : End While
        While d < -Math.PI : d += 2 * Math.PI : End While
        Return d
    End Function

    Public Shared Function SampleBezier(p0 As PointF, p1 As PointF, p2 As PointF, p3 As PointF, n As Integer) As PointF()
        Dim pts(n) As PointF
        For i = 0 To n
            Dim t = i / CDbl(n), u = 1 - t
            pts(i) = New PointF(CSng(u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X),
                                CSng(u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y))
        Next
        Return pts
    End Function

    ''' <summary>自由繪製的平滑：Catmull-Rom 補點，筆壓一起內插。</summary>
    Public Shared Function Smooth(pts As List(Of DrawPoint)) As List(Of DrawPoint)
        If pts.Count < 3 Then Return pts
        Dim result As New List(Of DrawPoint)()
        For i = 0 To pts.Count - 2
            Dim p0 = pts(Math.Max(0, i - 1)), p1 = pts(i), p2 = pts(i + 1), p3 = pts(Math.Min(pts.Count - 1, i + 2))
            For k = 0 To 3
                Dim t = k / 4.0F, t2 = t * t, t3 = t2 * t
                Dim f = Function(a As Single, b As Single, c As Single, d As Single) _
                    0.5F * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3)
                result.Add(New DrawPoint(f(p0.X, p1.X, p2.X, p3.X), f(p0.Y, p1.Y, p2.Y, p3.Y), p1.P + (p2.P - p1.P) * t))
            Next
        Next
        result.Add(pts(pts.Count - 1))
        Return result
    End Function

    '=====================================================================
    ' 幾何小工具
    '=====================================================================

    Public Shared Function Dist(a As PointF, b As PointF) As Double
        Dim dx = CDbl(a.X) - b.X, dy = CDbl(a.Y) - b.Y
        Return Math.Sqrt(dx * dx + dy * dy)
    End Function

    Public Shared Function PointInPolygon(poly As PointF(), p As PointF) As Boolean
        Dim inside = False
        Dim j = poly.Length - 1
        For i = 0 To poly.Length - 1
            If (poly(i).Y > p.Y) <> (poly(j).Y > p.Y) AndAlso
               p.X < (poly(j).X - poly(i).X) * (p.Y - poly(i).Y) / (poly(j).Y - poly(i).Y) + poly(i).X Then inside = Not inside
            j = i
        Next
        Return inside
    End Function

    ''' <summary>線段 p→q 與 a→b 相交時回傳 True，t 為交點在 a→b 上的位置（0..1）。</summary>
    Private Shared Function SegmentIntersect(p As PointF, q As PointF, a As PointF, b As PointF, ByRef t As Double) As Boolean
        Dim rX = CDbl(q.X) - p.X, rY = CDbl(q.Y) - p.Y
        Dim sX = CDbl(b.X) - a.X, sY = CDbl(b.Y) - a.Y
        Dim den = rX * sY - rY * sX
        If Math.Abs(den) < 0.0000000001 Then Return False
        Dim u = ((a.X - p.X) * rY - (a.Y - p.Y) * rX) / den
        Dim v = ((a.X - p.X) * sY - (a.Y - p.Y) * sX) / den
        If u < 0 OrElse u > 1 OrElse v < 0 OrElse v > 1 Then Return False
        t = u
        Return True
    End Function

    Private Shared Function DistToSegment(p As PointF, a As PointF, b As PointF) As Double
        Dim dx = CDbl(b.X) - a.X, dy = CDbl(b.Y) - a.Y
        Dim l2 = dx * dx + dy * dy
        If l2 <= 0 Then Return Dist(p, a)
        Dim t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2))
        Return Dist(p, New PointF(CSng(a.X + t * dx), CSng(a.Y + t * dy)))
    End Function

    '=====================================================================
    ' 範圍與點選
    '=====================================================================

    ''' <summary>圖層外形的外接矩形（不含線寬）。</summary>
    Public Shared Function Bounds(layer As DrawLayer) As RectangleF
        If layer.Shape = DrawShape.Raster Then Return RasterBounds(layer)
        Dim pts = Figures(layer).SelectMany(Function(f) f.Points).ToList()
        If pts.Count = 0 Then Return RectangleF.Empty
        Dim minX = pts.Min(Function(p) p.X), maxX = pts.Max(Function(p) p.X)
        Dim minY = pts.Min(Function(p) p.Y), maxY = pts.Max(Function(p) p.Y)
        Return RectangleF.FromLTRB(minX, minY, maxX, maxY)
    End Function

    ''' <summary>點 p 有沒有點到圖層：封閉形狀點內部即可，線條則要在線寬加上 tolerance 以內。</summary>
    Public Shared Function HitTest(layer As DrawLayer, p As PointF, tolerance As Double) As Boolean
        If layer.Shape = DrawShape.Raster Then
            Dim rb = RasterBounds(layer)
            Return Not rb.IsEmpty AndAlso p.X >= rb.Left AndAlso p.X <= rb.Right AndAlso p.Y >= rb.Top AndAlso p.Y <= rb.Bottom
        End If
        Dim tol = tolerance + layer.StrokeWidth / 2
        For Each f In Figures(layer)
            If f.Closed AndAlso f.Points.Length >= 3 AndAlso PointInPolygon(f.Points, p) Then Return True
            If f.Points.Length = 1 AndAlso Dist(f.Points(0), p) <= tol Then Return True
            Dim last = If(f.Closed, f.Points.Length, f.Points.Length - 1)
            For i = 0 To last - 1
                If DistToSegment(p, f.Points(i), f.Points((i + 1) Mod f.Points.Length)) <= tol Then Return True
            Next
        Next
        Return False
    End Function

    ''' <summary>點陣圖層畫過的範圍（含線寬、加上圖層位移；橡皮擦不算）。沒有內容時為 Empty。</summary>
    Public Shared Function RasterBounds(layer As DrawLayer) As RectangleF
        If layer.Ops Is Nothing Then Return RectangleF.Empty
        Dim any = False
        Dim l = Single.MaxValue, t = Single.MaxValue, r = Single.MinValue, b = Single.MinValue
        For Each op In layer.Ops
            If op.Eraser OrElse op.Shape = DrawShape.Raster Then Continue For
            Dim pts = Figures(op).SelectMany(Function(f) f.Points).ToList()
            If pts.Count = 0 Then Continue For
            Dim pad = CSng(op.StrokeWidth / 2)
            l = Math.Min(l, pts.Min(Function(p) p.X) - pad) : t = Math.Min(t, pts.Min(Function(p) p.Y) - pad)
            r = Math.Max(r, pts.Max(Function(p) p.X) + pad) : b = Math.Max(b, pts.Max(Function(p) p.Y) + pad)
            any = True
        Next
        If Not any Then Return RectangleF.Empty
        Return RectangleF.FromLTRB(CSng(l + layer.X), CSng(t + layer.Y), CSng(r + layer.X), CSng(b + layer.Y))
    End Function

    ''' <summary>把向量圖層點陣化：原本的圖形成為點陣圖層的第一筆，之後只能直接繪製或擦除。</summary>
    Public Shared Function Rasterize(layer As DrawLayer) As DrawLayer
        If layer.Shape = DrawShape.Raster Then Return layer.Clone()
        Dim op = layer.Clone()
        op.Opacity = 100 : op.Visible = True : op.Locked = False : op.Name = ""
        Return New DrawLayer With {.Shape = DrawShape.Raster, .Name = layer.Name, .Visible = layer.Visible, .Locked = layer.Locked,
                                   .Opacity = layer.Opacity, .X = 0, .Y = 0, .Ops = New List(Of DrawLayer) From {op}}
    End Function

    '=====================================================================
    ' 編輯
    '=====================================================================

    Public Shared Sub Offset(layer As DrawLayer, dx As Double, dy As Double)
        layer.X += dx : layer.Y += dy
        If layer.Points IsNot Nothing Then
            For Each p In layer.Points
                p.X = CSng(p.X + dx) : p.Y = CSng(p.Y + dy)
            Next
        End If
        If layer.Strokes IsNot Nothing Then
            For Each s In layer.Strokes
                For Each p In s.Points
                    p.X = CSng(p.X + dx) : p.Y = CSng(p.Y + dy)
                Next
            Next
        End If
    End Sub

    ''' <summary>方框的 8 個縮放控制點（0 左上、1 上、2 右上、3 右、4 右下、5 下、6 左下、7 左）的未旋轉位置。</summary>
    Public Shared Function BoxHandleLocal(layer As DrawLayer, index As Integer) As PointF
        Dim hw = CSng(Math.Abs(layer.W) / 2), hh = CSng(Math.Abs(layer.H) / 2)
        Dim xs = {-hw, 0, hw, hw, hw, 0, -hw, -hw}
        Dim ys = {-hh, -hh, -hh, 0, hh, hh, hh, 0}
        Return New PointF(xs(index), ys(index))
    End Function

    ''' <summary>
    ''' 拖曳縮放控制點：以 start（按下時的圖層）為準、對邊固定。keepAspect 時角落控制點等比例。
    ''' 結果寫回 layer 的 X、Y、W、H。
    ''' </summary>
    Public Shared Sub ResizeBox(layer As DrawLayer, start As DrawLayer, index As Integer, world As PointF, keepAspect As Boolean)
        Dim local = WorldToLocal(start, world)
        Dim l = -Math.Abs(start.W) / 2, r = Math.Abs(start.W) / 2, t = -Math.Abs(start.H) / 2, b = Math.Abs(start.H) / 2
        Const MinSize = 0.004
        Dim moveL = index = 0 OrElse index = 6 OrElse index = 7
        Dim moveR = index = 2 OrElse index = 3 OrElse index = 4
        Dim moveT = index = 0 OrElse index = 1 OrElse index = 2
        Dim moveB = index = 4 OrElse index = 5 OrElse index = 6
        If moveL Then l = Math.Min(local.X, r - MinSize)
        If moveR Then r = Math.Max(local.X, l + MinSize)
        If moveT Then t = Math.Min(local.Y, b - MinSize)
        If moveB Then b = Math.Max(local.Y, t + MinSize)
        If keepAspect AndAlso (moveL Or moveR) AndAlso (moveT Or moveB) Then
            Dim ratio = Math.Abs(start.W) / Math.Max(0.000001, Math.Abs(start.H))
            Dim w = r - l, h = b - t
            If w / h > ratio Then h = w / ratio Else w = h * ratio
            If moveL Then l = r - w Else r = l + w
            If moveT Then t = b - h Else b = t + h
        End If
        Dim c = LocalToWorld(start, New PointF(CSng((l + r) / 2), CSng((t + b) / 2)))
        layer.X = c.X : layer.Y = c.Y
        layer.W = r - l : layer.H = b - t
        ' 圖說的尖端（相對中心）跟著框等比例縮放。
        If IsCallout(layer.Shape) Then
            layer.TailX = start.TailX * layer.W / Math.Max(0.000001, Math.Abs(start.W))
            layer.TailY = start.TailY * layer.H / Math.Max(0.000001, Math.Abs(start.H))
        End If
    End Sub

    ''' <summary>點類／自由繪製用：依外接矩形縮放所有點（oldBounds → newBounds）。</summary>
    Public Shared Sub ScalePoints(layer As DrawLayer, start As DrawLayer, oldBounds As RectangleF, newBounds As RectangleF)
        Dim sx = If(oldBounds.Width <= 0, 1, newBounds.Width / oldBounds.Width)
        Dim sy = If(oldBounds.Height <= 0, 1, newBounds.Height / oldBounds.Height)
        Dim map = Function(p As DrawPoint) New DrawPoint(newBounds.X + (p.X - oldBounds.X) * sx, newBounds.Y + (p.Y - oldBounds.Y) * sy, p.P)
        If start.Points IsNot Nothing Then layer.Points = start.Points.Select(map).ToList()
        If start.Strokes IsNot Nothing Then
            layer.Strokes = start.Strokes.Select(Function(s) New DrawStroke With {.Points = s.Points.Select(map).ToList()}).ToList()
        End If
        ' 線寬依面積比例縮放，看起來才一致。
        layer.StrokeWidth = start.StrokeWidth * Math.Sqrt(Math.Abs(sx * sy))
    End Sub

    ''' <summary>軸對齊外接矩形的 8 個控制點（自由繪製）。</summary>
    Public Shared Function RectHandle(r As RectangleF, index As Integer) As PointF
        Dim xs = {r.Left, (r.Left + r.Right) / 2, r.Right, r.Right, r.Right, (r.Left + r.Right) / 2, r.Left, r.Left}
        Dim ys = {r.Top, r.Top, r.Top, (r.Top + r.Bottom) / 2, r.Bottom, r.Bottom, r.Bottom, (r.Top + r.Bottom) / 2}
        Return New PointF(xs(index), ys(index))
    End Function

    ''' <summary>拖曳外接矩形控制點後的新矩形（對邊固定）。</summary>
    Public Shared Function DragRect(r As RectangleF, index As Integer, p As PointF, keepAspect As Boolean) As RectangleF
        Dim l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom
        Const MinSize = 0.004F
        If index = 0 OrElse index = 6 OrElse index = 7 Then l = Math.Min(p.X, rt - MinSize)
        If index = 2 OrElse index = 3 OrElse index = 4 Then rt = Math.Max(p.X, l + MinSize)
        If index = 0 OrElse index = 1 OrElse index = 2 Then t = Math.Min(p.Y, b - MinSize)
        If index = 4 OrElse index = 5 OrElse index = 6 Then b = Math.Max(p.Y, t + MinSize)
        If keepAspect AndAlso index Mod 2 = 0 AndAlso r.Height > 0 Then
            Dim ratio = r.Width / r.Height
            Dim w = rt - l, h = b - t
            If w / h > ratio Then h = w / ratio Else w = h * ratio
            If index = 0 OrElse index = 6 Then l = rt - w Else rt = l + w
            If index = 0 OrElse index = 2 Then t = b - h Else b = t + h
        End If
        Return RectangleF.FromLTRB(l, t, rt, b)
    End Function

    ''' <summary>形狀參數控制點（黃點）的世界座標；沒有參數的形狀回傳空清單。</summary>
    Public Shared Function ParamHandles(layer As DrawLayer) As List(Of PointF)
        Dim list As New List(Of PointF)()
        Dim w = Math.Abs(layer.W), h = Math.Abs(layer.H)
        Select Case layer.Shape
            Case DrawShape.RoundRect, DrawShape.CalloutRect
                Dim r = layer.Param1 * Math.Min(w, h)
                list.Add(LocalToWorld(layer, New PointF(CSng(-w / 2 + r), CSng(-h / 2))))
            Case DrawShape.Star4, DrawShape.Star5, DrawShape.Star6
                Dim n = If(layer.Shape = DrawShape.Star4, 4, If(layer.Shape = DrawShape.Star5, 5, 6))
                Dim a = -Math.PI / 2 + Math.PI / n
                list.Add(LocalToWorld(layer, New PointF(CSng(w / 2 * layer.Param1 * Math.Cos(a)), CSng(h / 2 * layer.Param1 * Math.Sin(a)))))
            Case DrawShape.Arrow
                list.Add(LocalToWorld(layer, New PointF(CSng(w / 2 - layer.Param1 * w), CSng(-layer.Param2 * h / 2))))
        End Select
        If IsCallout(layer.Shape) Then list.Add(LocalToWorld(layer, New PointF(CSng(layer.TailX), CSng(layer.TailY))))
        Return list
    End Function

    ''' <summary>拖曳第 index 個黃點到 world。</summary>
    Public Shared Sub SetParamHandle(layer As DrawLayer, index As Integer, world As PointF)
        Dim local = WorldToLocal(layer, world)
        Dim w = Math.Max(0.000001, Math.Abs(layer.W)), h = Math.Max(0.000001, Math.Abs(layer.H))
        Dim hasShapeParam = layer.Shape = DrawShape.RoundRect OrElse layer.Shape = DrawShape.CalloutRect OrElse
                            layer.Shape = DrawShape.Star4 OrElse layer.Shape = DrawShape.Star5 OrElse layer.Shape = DrawShape.Star6 OrElse
                            layer.Shape = DrawShape.Arrow
        If IsCallout(layer.Shape) AndAlso (index = 1 OrElse Not hasShapeParam) Then
            layer.TailX = local.X : layer.TailY = local.Y
            Return
        End If
        Select Case layer.Shape
            Case DrawShape.RoundRect, DrawShape.CalloutRect
                layer.Param1 = Math.Max(0, Math.Min(0.5, (local.X + w / 2) / Math.Min(w, h)))
            Case DrawShape.Star4, DrawShape.Star5, DrawShape.Star6
                Dim nx = local.X / (w / 2), ny = local.Y / (h / 2)
                layer.Param1 = Math.Max(0.05, Math.Min(0.95, Math.Sqrt(nx * nx + ny * ny)))
            Case DrawShape.Arrow
                layer.Param1 = Math.Max(0.05, Math.Min(1, (w / 2 - local.X) / w))
                layer.Param2 = Math.Max(0.05, Math.Min(1, -2 * local.Y / h))
        End Select
    End Sub
End Class
