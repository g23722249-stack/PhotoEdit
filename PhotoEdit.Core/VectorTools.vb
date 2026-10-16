Imports System.Drawing

''' <summary>
''' 向量物件的鏡射、複製屬性（屬性吸管）（同 CorelDRAW 的鏡射鈕、屬性滴管；Illustrator 的 Reflect、吸管套用外觀）。
''' 鏡射以 (cx, cy) 為軸：方框類圖形用旋轉角度加上形狀本身的對稱性做出鏡像（文字保持正的、看得懂），
''' 點類、自由繪製、鋼筆路徑直接鏡射每個點；漸層與透明度漸層跟著鏡射。點陣圖層、填滿圖層不鏡射。
''' </summary>
Partial Public NotInheritable Class DrawGeometry

    ''' <summary>能不能鏡射（向量圖形；點陣、填滿圖層不行）。</summary>
    Public Shared Function CanMirror(d As DrawLayer) As Boolean
        If d Is Nothing Then Return False
        Select Case d.Shape
            Case DrawShape.Raster, DrawShape.Bucket, DrawShape.Gradient, DrawShape.FillLayer : Return False
        End Select
        Return True
    End Function

    ''' <summary>水平（horizontal = True，左右對調）或垂直鏡射，軸通過 (cx, cy)（照片高度單位）。</summary>
    Public Shared Sub Mirror(layer As DrawLayer, horizontal As Boolean, cx As Double, cy As Double)
        If Not CanMirror(layer) Then Return
        Dim flip = Function(p As PointF) If(horizontal, New PointF(CSng(2 * cx - p.X), p.Y), New PointF(p.X, CSng(2 * cy - p.Y)))
        ' 漸層端點先記下世界座標，形狀改完再換回比例
        Dim before = layer.Clone()
        Dim gradWorld = Function(g As GradientFill) As (A As PointF, B As PointF)?
                            If g Is Nothing Then Return Nothing
                            Return (flip(GradientPoint(before, g.X1, g.Y1)), flip(GradientPoint(before, g.X2, g.Y2)))
                        End Function
        Dim fill = gradWorld(layer.FillGradient), fade = gradWorld(layer.FadeGradient)

        If IsBoxLayer(layer) Then
            If horizontal Then layer.X = 2 * cx - layer.X Else layer.Y = 2 * cy - layer.Y
            MirrorBox(layer, horizontal)
        Else
            ' 尺寸線：尺寸線中間的位置跟著鏡射，再反算位移（方向、上下側會變）
            Dim dimMid As PointF? = Nothing
            If layer.Shape = DrawShape.Dimension Then
                Dim g = Dimension(layer)
                If g.HasValue Then dimMid = flip(New PointF((g.Value.A2.X + g.Value.B2.X) / 2, (g.Value.A2.Y + g.Value.B2.Y) / 2))
            End If
            For Each p In If(layer.Points, New List(Of DrawPoint)())
                MirrorPoint(p, horizontal, cx, cy)
            Next
            If dimMid.HasValue Then SetDimensionOffset(layer, dimMid.Value)
            For Each s In If(layer.Strokes, New List(Of DrawStroke)())
                For Each p In s.Points
                    MirrorPoint(p, horizontal, cx, cy)
                Next
            Next
            For Each c In If(layer.Contours, New List(Of PathContour)())
                For Each n In c.Nodes
                    If horizontal Then
                        n.X = CSng(2 * cx - n.X) : n.InX = CSng(2 * cx - n.InX) : n.OutX = CSng(2 * cx - n.OutX)
                    Else
                        n.Y = CSng(2 * cy - n.Y) : n.InY = CSng(2 * cy - n.InY) : n.OutY = CSng(2 * cy - n.OutY)
                    End If
                Next
            Next
        End If
        ' 對稱繪圖的中心
        If layer.Symmetry <> SymmetryKind.None Then
            If horizontal Then layer.SymX = 2 * cx - layer.SymX Else layer.SymY = 2 * cy - layer.SymY
        End If
        Dim back = Function(src As GradientFill, w As (A As PointF, B As PointF)?) As GradientFill
                       If src Is Nothing OrElse Not w.HasValue Then Return src
                       Dim a = GradientFraction(layer, w.Value.A), b = GradientFraction(layer, w.Value.B)
                       Dim g = src.Clone()
                       g.X1 = Math.Round(a.X, 4) : g.Y1 = Math.Round(a.Y, 4) : g.X2 = Math.Round(b.X, 4) : g.Y2 = Math.Round(b.Y, 4)
                       Return g
                   End Function
        layer.FillGradient = back(layer.FillGradient, fill)
        layer.FadeGradient = back(layer.FadeGradient, fade)
    End Sub

    Private Shared Sub MirrorPoint(p As DrawPoint, horizontal As Boolean, cx As Double, cy As Double)
        If horizontal Then
            p.X = CSng(2 * cx - p.X) : p.Tx = -p.Tx
        Else
            p.Y = CSng(2 * cy - p.Y) : p.Ty = -p.Ty
        End If
    End Sub

    ''' <summary>
    ''' 方框類圖形的鏡像（中心已經移好）。世界座標的鏡射 M 和旋轉 R(θ) 的關係：M·R(θ) = R(−θ)·M，
    ''' 所以形狀本身左右（或上下）對稱時，角度變成 −θ 就好；只在另一個方向對稱的（箭頭、奇數角的星形、愛心）
    ''' 再轉 180°；兩個方向都不對稱的（閃電、螺旋）用 MirrorX 翻過來。圖說的尖角、弧形的角度一起鏡射。
    ''' </summary>
    Private Shared Sub MirrorBox(layer As DrawLayer, horizontal As Boolean)
        Dim symX As Boolean, symY As Boolean ' 形狀本身（未旋轉）左右對稱、上下對稱
        Select Case layer.Shape
            Case DrawShape.Rectangle, DrawShape.RoundRect, DrawShape.Ellipse, DrawShape.Grid, DrawShape.Text
                symX = True : symY = True
            Case DrawShape.Arrow
                symX = False : symY = True
            Case DrawShape.Heart
                symX = True : symY = False
            Case DrawShape.Star4, DrawShape.Star5, DrawShape.Star6, DrawShape.Star
                symX = True : symY = StarPointCount(layer) Mod 2 = 0
            Case DrawShape.RegularPolygon
                symX = True : symY = Math.Max(3, layer.Sides) Mod 2 = 0
            Case DrawShape.Lightning, DrawShape.Spiral
                symX = False : symY = False
            Case Else ' 圖說：本體對稱，尖角另外鏡射；弧形：角度另外鏡射
                symX = True : symY = True
        End Select
        Dim mirrored = If(horizontal, symX, symY)
        Dim other = If(horizontal, symY, symX)
        If mirrored Then
            layer.Rotation = -layer.Rotation
        ElseIf other Then
            layer.Rotation = 180 - layer.Rotation
        Else
            ' 兩個方向都不對稱：左右翻過來；垂直鏡射＝左右翻再轉 180°
            layer.MirrorX = Not layer.MirrorX
            layer.Rotation = If(horizontal, -layer.Rotation, 180 - layer.Rotation)
        End If
        layer.Rotation = NormalizeAngle(layer.Rotation)
        If IsCallout(layer.Shape) Then
            If horizontal Then layer.TailX = -layer.TailX Else layer.TailY = -layer.TailY
        End If
        If layer.Shape = DrawShape.Arc Then
            ' 本地角度 a：左右鏡射 → 180 − a，上下鏡射 → −a；起點、終點對調
            Dim e = layer.Param1 + layer.Param2
            layer.Param1 = If(horizontal, 180 - e, -e)
        End If
    End Sub

    Private Shared Function NormalizeAngle(a As Double) As Double
        a = a Mod 360
        If a > 180 Then a -= 360
        If a <= -180 Then a += 360
        Return Math.Round(a, 6)
    End Function

    ''' <summary>
    ''' 屬性吸管：把 source 的外觀（填色、漸層、線條顏色粗細與筆刷、虛線端點箭頭、陰影、透明度、不透明度、混合模式；
    ''' 兩個都有文字時連字型）套到 target。形狀、位置、文字內容不變。
    ''' </summary>
    Public Shared Sub CopyAppearance(target As DrawLayer, source As DrawLayer)
        If target Is Nothing OrElse source Is Nothing OrElse target Is source Then Return
        target.Brush = source.Brush : target.Fx = source.Fx : target.Material = source.Material
        target.StrokeColorArgb = source.StrokeColorArgb : target.FillColorArgb = source.FillColorArgb
        target.FillColor2Argb = source.FillColor2Argb
        target.StrokeWidth = source.StrokeWidth : target.Softness = source.Softness : target.Flow = source.Flow
        target.Opacity = source.Opacity : target.Blend = source.Blend
        target.Stroked = source.Stroked
        ' 開放的線條沒有填色可言：只有封閉的目標才跟著改「填色」
        If IsClosedLayer(target) Then target.Filled = source.Filled
        target.Dash = source.Dash : target.Cap = source.Cap : target.Join = source.Join
        target.StartArrow = source.StartArrow : target.EndArrow = source.EndArrow
        target.FillGradient = source.FillGradient?.Clone()
        target.FadeGradient = source.FadeGradient?.Clone()
        target.FillPattern = source.FillPattern?.Clone()
        target.WidthProfile = source.WidthProfile?.Select(Function(w) w.Clone()).ToList()
        target.FillSwatch = source.FillSwatch : target.StrokeSwatch = source.StrokeSwatch
        target.Shadow = source.Shadow : target.ShadowOffsetX = source.ShadowOffsetX : target.ShadowOffsetY = source.ShadowOffsetY
        target.ShadowSoft = source.ShadowSoft : target.ShadowOpacity = source.ShadowOpacity : target.ShadowColorArgb = source.ShadowColorArgb
        target.SpacingPct = source.SpacingPct : target.Scatter = source.Scatter : target.SizeJitter = source.SizeJitter
        target.HueJitter = source.HueJitter : target.LumJitter = source.LumJitter : target.PressureOpacity = source.PressureOpacity
        target.PaperGrain = source.PaperGrain : target.Particle = source.Particle : target.HoseTheme = source.HoseTheme
        If HasText(target.Shape) AndAlso HasText(source.Shape) Then
            target.FontName = source.FontName : target.TextColorArgb = source.TextColorArgb
            target.TextSize = source.TextSize : target.TextBold = source.TextBold : target.TextItalic = source.TextItalic
        End If
    End Sub

    ''' <summary>能不能當屬性吸管的來源或目標（點陣、填滿圖層沒有這些屬性）。</summary>
    Public Shared Function HasAppearance(d As DrawLayer) As Boolean
        Return CanMirror(d)
    End Function
End Class

''' <summary>
''' 外框轉物件（同 CorelDRAW 的「外框轉換為物件」、Illustrator 的 Outline Stroke）：線條本身變成可以填色、焊接的封閉形狀。
''' 用和畫面一樣的筆刷引擎把線條的範圍算成高解析度的遮罩（筆壓粗細、收尾變細、虛線、端點、轉角、箭頭都在），
''' 再描成外形線，所以轉出來的形狀和看到的一樣。筆刷質感（炭筆的顆粒、筆毛）不算，取實心的範圍。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    ''' <summary>能不能轉：有線條的向量圖形（特效筆、粒子、貼圖噴槍、點陣、填滿圖層不行）。</summary>
    Public Shared Function CanOutlineStroke(d As DrawLayer) As Boolean
        If d Is Nothing OrElse Not DrawGeometry.CanMirror(d) Then Return False
        Select Case d.Brush
            Case BrushKind.FX, BrushKind.Particle, BrushKind.StickerHose : Return False
        End Select
        If Global.PhotoEdit.DrawLayer.SamplesCanvas(d.Brush) Then Return False
        If d.Shape = DrawShape.Text AndAlso Not DrawGeometry.IsPathText(d) Then Return d.Stroked ' 文字方框
        Dim stroked = d.Stroked OrElse Not DrawGeometry.IsClosedLayer(d)
        Return stroked AndAlso DrawGeometry.Figures(d).Count > 0
    End Function

    ''' <summary>線條的範圍（世界座標的外形線，內部在左邊）；沒有時回傳空清單。</summary>
    Public Shared Function StrokeOutlineRings(layer As DrawLayer) As List(Of PointF())
        Dim result As New List(Of PointF())()
        If Not CanOutlineStroke(layer) Then Return result
        Dim b = DrawGeometry.Bounds(layer)
        Dim pad = CSng(layer.StrokeWidth * 3 + 0.002)
        b = RectangleF.FromLTRB(b.Left - pad, b.Top - pad, b.Right + pad, b.Bottom + pad)
        ' 解析度：外框長邊約 2400 像素（細線也夠細緻），最多 40000 像素一個單位
        Dim scale = CSng(Math.Min(40000, 2400 / Math.Max(0.0001, Math.Max(b.Width, b.Height))))
        Dim region = Rectangle.FromLTRB(CInt(Math.Floor(b.Left * scale)), CInt(Math.Floor(b.Top * scale)),
                                        CInt(Math.Ceiling(b.Right * scale)), CInt(Math.Ceiling(b.Bottom * scale)))
        If region.Width <= 2 OrElse region.Height <= 2 Then Return result
        Dim figs = DrawGeometry.Figures(layer).
            Select(Function(f) New DrawGeometry.Figure With {.Points = f.Points.Select(Function(p) New PointF(p.X * scale, p.Y * scale)).ToArray(),
                                                             .Closed = f.Closed, .Pressure = f.Pressure, .Angle = f.Angle, .Flat = f.Flat}).
            Where(Function(f) f.Points.Length > 0).ToList()
        If figs.Count = 0 Then Return result
        Dim widthPx = CSng(Math.Max(0.6, layer.StrokeWidth * scale))
        Dim cv As New Canvas(region)
        Dim styled = StyleStroke(figs, layer, widthPx)
        Dim cov As Single()
        If NeedsGeometricStroke(layer) Then
            cov = GeometricStrokeMask(styled.Lines, layer, widthPx, region)
        Else
            ' 實心的範圍：筆毛筆刷（會斷開）改用墨水的圓頭，粗細變化（筆壓、收尾）照原本的筆刷
            Dim solid = layer.Clone()
            solid.Softness = 0 : solid.Flow = 100 : solid.Scatter = 0 : solid.SizeJitter = 0 : solid.SpacingPct = 0
            solid.HueJitter = 0 : solid.LumJitter = 0 : solid.PressureOpacity = 0
            If Spec(solid.Brush, solid.Fx).Tip = TipKind.Bristle Then solid.Brush = BrushKind.Ink
            cov = New Single(cv.W * cv.H - 1) {}
            StampFigures(cov, Nothing, Nothing, cv, styled.Lines, solid, widthPx)
        End If
        AddHeads(cov, styled.Heads, region)
        Dim mask(cov.Length - 1) As Boolean
        For i = 0 To cov.Length - 1
            mask(i) = cov(i) >= 0.5F
        Next
        For Each ring In SmartFill.Trace(mask, region.Width, region.Height)
            Dim simple = DrawGeometry.Simplify(ring, 0.6, True)
            If simple.Length < 3 Then Continue For
            Dim pts = simple.Select(Function(q) New PointF((q.X + region.X) / scale, (q.Y + region.Y) / scale)).ToArray()
            If Math.Abs(PathBoolean.SignedArea(pts)) > 0.0000002 Then result.Add(pts)
        Next
        Return result
    End Function

    ''' <summary>
    ''' 外框轉物件的結果：鋼筆路徑，填色＝原本的線條色，沒有外框；名稱加「外框」。不能轉或結果是空的時回傳 Nothing。
    ''' </summary>
    Public Shared Function OutlineToObject(layer As DrawLayer) As DrawLayer
        Dim rings = StrokeOutlineRings(layer)
        If rings.Count = 0 Then Return Nothing
        Dim style = layer.Clone()
        style.Shape = DrawShape.Path
        style.Text = "" : style.TextPathSource = Nothing
        Dim r = PathBoolean.MakeLayer(style, rings)
        If r Is Nothing Then Return Nothing
        r.Filled = True
        r.FillColorArgb = layer.StrokeColorArgb
        r.FillGradient = Nothing
        r.Stroked = False
        r.Brush = BrushKind.HardRound
        r.Dash = LineDash.Solid : r.Cap = LineCapKind.Round : r.Join = LineJoinKind.Round
        r.StartArrow = ArrowKind.None : r.EndArrow = ArrowKind.None
        r.Strokes = Nothing : r.Points = Nothing
        r.X = 0 : r.Y = 0 : r.W = 0 : r.H = 0 : r.Rotation = 0
        r.Id = Nothing
        r.Name = If(String.IsNullOrEmpty(layer.Name), Lang.T("外框"), layer.Name & Lang.T(" 外框"))
        Return r
    End Function
End Class
