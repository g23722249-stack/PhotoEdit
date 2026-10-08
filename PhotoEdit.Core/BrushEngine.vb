Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Drawing.Text

''' <summary>
''' 繪圖圖層的算圖：筆刷以「筆印」沿路徑蓋章累積成覆蓋率，再依筆刷加上紙紋、顆粒、暈染、筆毛、厚塗光影；
''' 特效筆（火焰、煙霧、星光）則沿路徑灑粒子。紋理全部由程式產生（同一圖層每次結果相同），
''' 尺寸都以照片高度換算，所以預覽與匯出看起來一致。
''' 每個圖層算好的結果會快取，改其他圖層或調色時不必重算。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer
    Private Sub New()
    End Sub

    '=====================================================================
    ' 筆刷設定
    '=====================================================================

    Private Enum TipKind
        Round
        Gaussian
        Chisel
        Bristle
    End Enum

    Private Structure BrushSpec
        Public Tip As TipKind
        ''' <summary>筆尖本身的邊緣柔和度（0..1）；圖層的「柔邊」只會更柔。</summary>
        Public Edge As Single
        ''' <summary>筆印間距（筆寬的比例）。</summary>
        Public Spacing As Single
        ''' <summary>筆壓影響粗細的程度（0 = 不影響）。</summary>
        Public SizePressure As Single
        ''' <summary>True：筆印疊加（越塗越濃）；False：取最大值（一筆之內濃度一致）。</summary>
        Public Additive As Boolean
        ''' <summary>每個筆印的濃度。</summary>
        Public Amount As Single
        ''' <summary>頭尾收筆變細。</summary>
        Public Taper As Boolean
    End Structure

    Private Shared Function Spec(b As BrushKind, fx As FxKind) As BrushSpec
        Select Case b
            Case BrushKind.SoftRound : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 1, .Spacing = 0.08F, .SizePressure = 0.3F, .Amount = 1}
            Case BrushKind.Pencil : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0.25F, .Spacing = 0.1F, .SizePressure = 0.35F, .Amount = 1}
            Case BrushKind.Charcoal : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0.5F, .Spacing = 0.12F, .SizePressure = 0.4F, .Additive = True, .Amount = 0.55F}
            Case BrushKind.Chalk : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0.3F, .Spacing = 0.1F, .SizePressure = 0.2F, .Amount = 1}
            Case BrushKind.Crayon : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0.15F, .Spacing = 0.08F, .SizePressure = 0.25F, .Amount = 1}
            Case BrushKind.Watercolor : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0.7F, .Spacing = 0.06F, .SizePressure = 0.4F, .Additive = True, .Amount = 0.22F}
            Case BrushKind.OilPaint : Return New BrushSpec With {.Tip = TipKind.Bristle, .SizePressure = 0.3F, .Amount = 1}
            Case BrushKind.Acrylic : Return New BrushSpec With {.Tip = TipKind.Bristle, .SizePressure = 0.25F, .Amount = 1}
            Case BrushKind.Ink : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0, .Spacing = 0.05F, .SizePressure = 0.9F, .Amount = 1, .Taper = True}
            Case BrushKind.ChineseBrush : Return New BrushSpec With {.Tip = TipKind.Bristle, .SizePressure = 1, .Amount = 1, .Taper = True}
            Case BrushKind.Marker : Return New BrushSpec With {.Tip = TipKind.Chisel, .Edge = 0.1F, .Spacing = 0.06F, .Amount = 1}
            Case BrushKind.Airbrush : Return New BrushSpec With {.Tip = TipKind.Gaussian, .Edge = 1, .Spacing = 0.05F, .Additive = True, .Amount = 0.1F}
            Case BrushKind.DryBrush : Return New BrushSpec With {.Tip = TipKind.Bristle, .SizePressure = 0.2F, .Amount = 1}
            Case BrushKind.Texture : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0.35F, .Spacing = 0.08F, .SizePressure = 0.3F, .Amount = 1}
            Case BrushKind.FX : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0.05F, .Spacing = 0.06F, .SizePressure = 0.5F, .Amount = 1}
            Case Else : Return New BrushSpec With {.Tip = TipKind.Round, .Edge = 0, .Spacing = 0.08F, .SizePressure = 0.6F, .Amount = 1}
        End Select
    End Function

    ''' <summary>滑鼠（沒有筆壓）時，依移動速度模擬筆壓的筆刷：毛筆、墨水。</summary>
    Public Shared Function SimulatesPressure(b As BrushKind) As Boolean
        Return b = BrushKind.ChineseBrush OrElse b = BrushKind.Ink
    End Function

    '=====================================================================
    ' 圖層合成與快取
    '=====================================================================

    Private NotInheritable Class Rendered
        Public Bitmap As Bitmap
        Public Region As Rectangle
    End Class

    Private Const MaxCache As Integer = 48
    Private Shared ReadOnly _cache As New Dictionary(Of String, Rendered)()
    Private Shared ReadOnly _order As New LinkedList(Of String)()
    Private Shared ReadOnly _jsonOptions As New System.Text.Json.JsonSerializerOptions()

    ''' <summary>把配方裡的繪圖圖層（由下而上）畫到 bmp 上。</summary>
    Public Shared Sub DrawLayers(bmp As Bitmap, recipe As EditRecipe)
        If recipe.Drawings Is Nothing OrElse recipe.Drawings.Count = 0 Then Return
        For Each layer In recipe.Drawings
            If layer.Visible Then DrawOne(bmp, layer)
        Next
    End Sub

    ''' <summary>畫上一個繪圖圖層（依它的不透明度與混合模式；不看 Visible）。</summary>
    Public Shared Sub DrawOne(bmp As Bitmap, layer As DrawLayer)
        If layer.Shape = DrawShape.Raster Then
            DrawRasterLayer(bmp, layer, bmp.Width, bmp.Height)
        Else
            DrawLayer(bmp, layer, bmp.Width, bmp.Height)
        End If
    End Sub

    Private Shared Sub DrawLayer(dst As Bitmap, layer As DrawLayer, w As Integer, h As Integer)
        Dim opacity = Math.Max(0, Math.Min(100, layer.Opacity)) / 100.0F
        If opacity <= 0 Then Return
        ' 不透明度與混合模式在合成時才套用，調整時不必重算圖層。
        Dim keyLayer = layer.Clone()
        keyLayer.Opacity = 100 : keyLayer.Name = "" : keyLayer.Visible = True : keyLayer.Locked = False
        keyLayer.Id = Nothing : keyLayer.Blend = BlendMode.Normal
        Dim key = System.Text.Json.JsonSerializer.Serialize(keyLayer, _jsonOptions) & "|" & w & "x" & h
        Dim item As Rendered = Nothing
        SyncLock _cache
            If _cache.TryGetValue(key, item) Then
                _order.Remove(key)
                _order.AddFirst(key)
            End If
        End SyncLock
        If item Is Nothing Then
            item = Render(layer, w, h)
            SyncLock _cache
                If Not _cache.ContainsKey(key) Then
                    _cache(key) = item
                    _order.AddFirst(key)
                    While _order.Count > MaxCache
                        Dim old = _order.Last.Value
                        _order.RemoveLast()
                        _cache(old).Bitmap?.Dispose()
                        _cache.Remove(old)
                    End While
                Else
                    item.Bitmap?.Dispose()
                    item = _cache(key)
                End If
            End SyncLock
        End If
        If item.Bitmap Is Nothing Then Return
        SyncLock item
            LayerBlend.Composite(dst, item.Bitmap, item.Region, opacity, layer.Blend)
        End SyncLock
    End Sub

    ''' <summary>清空快取（測試用）。</summary>
    Public Shared Sub ClearCache()
        SyncLock _cache
            For Each v In _cache.Values
                v.Bitmap?.Dispose()
            Next
            _cache.Clear()
            _order.Clear()
        End SyncLock
        ClearRasterCache()
    End Sub

    '=====================================================================
    ' 一個圖層
    '=====================================================================

    ''' <summary>RGBA（預乘 alpha，0..1）緩衝區。</summary>
    Private NotInheritable Class Canvas
        Public ReadOnly W As Integer, H As Integer
        Public ReadOnly OX As Integer, OY As Integer
        Public ReadOnly R As Single(), G As Single(), B As Single(), A As Single()

        Public Sub New(region As Rectangle)
            W = region.Width : H = region.Height : OX = region.X : OY = region.Y
            Dim n = W * H
            R = New Single(n - 1) {} : G = New Single(n - 1) {} : B = New Single(n - 1) {} : A = New Single(n - 1) {}
        End Sub

        ''' <summary>把一個（非預乘）顏色以 alpha 疊在上面。</summary>
        Public Sub Over(i As Integer, cr As Single, cg As Single, cb As Single, ca As Single)
            If ca <= 0 Then Return
            If ca > 1 Then ca = 1
            Dim k = 1 - ca
            R(i) = cr * ca + R(i) * k
            G(i) = cg * ca + G(i) * k
            B(i) = cb * ca + B(i) * k
            A(i) = ca + A(i) * k
        End Sub
    End Class

    Private Shared Function Render(layer As DrawLayer, w As Integer, h As Integer) As Rendered
        Dim scale = CSng(h)
        Dim figs = DrawGeometry.Figures(layer).
            Select(Function(f) New DrawGeometry.Figure With {.Points = f.Points.Select(Function(p) New PointF(p.X * scale, p.Y * scale)).ToArray(),
                                                             .Closed = f.Closed, .Pressure = f.Pressure, .Angle = f.Angle, .Flat = f.Flat}).
            Where(Function(f) f.Points.Length > 0).ToList()
        If figs.Count = 0 Then Return New Rendered()
        ' 混色、塗抹、仿製要讀畫布，只在點陣圖層裡算（SpecialBrushes.ApplySamplingOp）。
        If Global.PhotoEdit.DrawLayer.SamplesCanvas(layer.Brush) Then Return New Rendered()
        Dim widthPx = CSng(Math.Max(0.6, layer.StrokeWidth * scale))

        ' 範圍：外形加上線寬、特效與陰影需要的邊。
        Dim pts = figs.SelectMany(Function(f) f.Points).ToList()
        Dim padL = widthPx * 1.2F + 4, padT = padL, padR = padL, padB = padL
        Dim stroked = layer.Stroked OrElse Not DrawGeometry.IsClosed(layer.Shape) ' 開放的線條一律畫出；「外框」只管封閉形狀
        If stroked AndAlso layer.Brush = BrushKind.FX Then
            Dim extent = Math.Max(pts.Max(Function(p) p.X) - pts.Min(Function(p) p.X), pts.Max(Function(p) p.Y) - pts.Min(Function(p) p.Y))
            Dim fp = FxPad(layer.Fx, widthPx, extent)
            padL += fp.L : padT += fp.T : padR += fp.R : padB += fp.B
        End If
        ' 粒子會飛出筆畫外、貼圖比筆寬大、散佈會偏離筆畫、繪圖筆傾斜時筆觸變寬
        Dim extra = 0.0F
        If layer.Brush = BrushKind.Particle Then extra = widthPx * 5
        If layer.Brush = BrushKind.StickerHose Then extra = widthPx * 4
        If layer.Scatter > 0 Then extra = Math.Max(extra, widthPx * 1.6F * layer.Scatter / 100.0F + widthPx * 0.5F)
        If layer.SizeJitter > 0 OrElse figs.Any(Function(f) f.Angle IsNot Nothing) Then extra = Math.Max(extra, widthPx * 0.8F)
        padL += extra : padT += extra : padR += extra : padB += extra
        If layer.Shadow Then
            Dim s = ShadowOffset(widthPx) + ShadowBlur(widthPx) * 2
            padR += s : padB += s
        End If
        Dim region = Rectangle.FromLTRB(CInt(Math.Floor(pts.Min(Function(p) p.X) - padL)), CInt(Math.Floor(pts.Min(Function(p) p.Y) - padT)),
                                        CInt(Math.Ceiling(pts.Max(Function(p) p.X) + padR)), CInt(Math.Ceiling(pts.Max(Function(p) p.Y) + padB)))
        region.Intersect(New Rectangle(0, 0, w, h))
        If region.Width <= 0 OrElse region.Height <= 0 Then Return New Rendered()
        ' 防呆：太大的筆刷不讓記憶體爆掉。
        If CLng(region.Width) * region.Height > 60_000_000L Then Return New Rendered()

        Dim cv As New Canvas(region)
        Dim rnd As New Random(layer.Seed)
        Dim closedFigs = figs.Where(Function(f) f.Closed AndAlso f.Points.Length >= 3).ToList()

        ' 1. 填色
        If layer.Filled AndAlso closedFigs.Count > 0 Then
            Dim mask = FillMask(closedFigs, region)
            Shade(cv, mask, Nothing, layer, Color.FromArgb(layer.FillColorArgb), widthPx, h, isFill:=True)
        End If

        ' 2. 線條
        If stroked Then
            If layer.Brush = BrushKind.FX Then
                RenderFx(cv, figs, layer, widthPx, rnd, h)
            ElseIf layer.Brush = BrushKind.Particle Then
                RenderParticles(cv, figs, layer, widthPx, rnd, h)
            ElseIf layer.Brush = BrushKind.StickerHose Then
                RenderHose(cv, figs, layer, widthPx, rnd)
            Else
                Dim sp = Spec(layer.Brush, layer.Fx)
                Dim cov(cv.W * cv.H - 1) As Single
                Dim streak As Single() = If(sp.Tip = TipKind.Bristle, New Single(cv.W * cv.H - 1) {}, Nothing)
                ' 顏色變化：每個筆印（筆毛）的顏色記在 tint，上色時逐像素使用。
                Dim jitterColor = (layer.HueJitter > 0 OrElse layer.LumJitter > 0) AndAlso layer.Brush <> BrushKind.Texture
                Dim tint As Single() = If(jitterColor, New Single(cv.W * cv.H * 3 - 1) {}, Nothing)
                Dim stampRnd As New Random(layer.Seed * 7 + 1)
                Dim edge = Math.Max(sp.Edge, layer.Softness / 100.0F)
                Dim flow = Math.Max(0.02F, layer.Flow / 100.0F)
                For Each f In figs
                    Dim path = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
                    Dim pres = If(f.Pressure, Nothing)
                    Dim angles = f.Angle, flats = f.Flat
                    If f.Closed Then
                        If pres IsNot Nothing Then pres = pres.Concat({pres(0)}).ToArray()
                        If angles IsNot Nothing Then angles = angles.Concat({angles(0)}).ToArray()
                        If flats IsNot Nothing Then flats = flats.Concat({flats(0)}).ToArray()
                    End If
                    If sp.Tip = TipKind.Bristle Then
                        Bristles(cov, streak, cv, path, pres, widthPx, layer, sp, flow, edge, angles, flats, tint)
                    Else
                        Stamp(cov, cv, path, pres, angles, flats, widthPx, sp, flow, edge, layer, stampRnd, tint)
                    End If
                Next
                Shade(cv, cov, streak, layer, Color.FromArgb(layer.StrokeColorArgb), widthPx, h, isFill:=False, tint:=tint)
            End If
        End If

        ' 3. 圖說文字
        If DrawGeometry.IsCallout(layer.Shape) AndAlso Not String.IsNullOrWhiteSpace(layer.Text) Then DrawText(cv, layer, region, scale)

        ' 4. 陰影
        If layer.Shadow Then AddShadow(cv, widthPx)

        Return New Rendered With {.Bitmap = ToBitmap(cv), .Region = region}
    End Function

    Private Shared Function ShadowOffset(widthPx As Single) As Single
        Return Math.Max(3, widthPx * 0.8F)
    End Function

    Private Shared Function ShadowBlur(widthPx As Single) As Single
        Return Math.Max(2, widthPx * 0.9F)
    End Function

    Private Shared Function ToBitmap(cv As Canvas) As Bitmap
        Dim px(cv.W * cv.H * 4 - 1) As Byte
        For i = 0 To cv.W * cv.H - 1
            Dim a = cv.A(i)
            If a <= 0.002F Then Continue For
            If a > 1 Then a = 1
            px(i * 4) = ToByte(cv.B(i) / a)
            px(i * 4 + 1) = ToByte(cv.G(i) / a)
            px(i * 4 + 2) = ToByte(cv.R(i) / a)
            px(i * 4 + 3) = ToByte(a)
        Next
        Dim bmp As New Bitmap(cv.W, cv.H, PixelFormat.Format32bppArgb)
        Perspective.WritePixels(bmp, px)
        Return bmp
    End Function

    Private Shared Function ToByte(v As Single) As Byte
        If v <= 0 Then Return 0
        If v >= 1 Then Return 255
        Return CByte(v * 255 + 0.5F)
    End Function

    ''' <summary>封閉形狀的填色範圍（反鋸齒，0..1）。</summary>
    Private Shared Function FillMask(figs As List(Of DrawGeometry.Figure), region As Rectangle) As Single()
        Using bmp As New Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp), path As New GraphicsPath(FillMode.Winding)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.TranslateTransform(-region.X, -region.Y)
                For Each f In figs
                    path.AddPolygon(f.Points)
                Next
                g.FillPath(Brushes.White, path)
            End Using
            Dim px = Perspective.ReadPixels(bmp)
            Dim mask(region.Width * region.Height - 1) As Single
            For i = 0 To mask.Length - 1
                mask(i) = px(i * 4 + 3) / 255.0F
            Next
            Return mask
        End Using
    End Function

    '=====================================================================
    ' 筆印
    '=====================================================================

    ''' <summary>沿路徑每隔一段取一點（內插筆壓與方向），交給 visit。</summary>
    Private Shared Sub Walk(path As PointF(), pres As Single(), spacing As Single,
                            visit As Action(Of Single, Single, Single, Single, Single, Single, Single))
        ' visit(x, y, 筆壓, 方向x, 方向y, 已走距離, 總長)
        Dim total = 0.0F
        For i = 1 To path.Length - 1
            total += CSng(DrawGeometry.Dist(path(i - 1), path(i)))
        Next
        If path.Length = 1 OrElse total < 0.01F Then
            visit(path(0).X, path(0).Y, If(pres Is Nothing, 1, pres(0)), 1, 0, 0, 0)
            Return
        End If
        spacing = Math.Max(0.35F, spacing)
        Dim travelled = 0.0F, nextAt = 0.0F
        For i = 1 To path.Length - 1
            Dim a = path(i - 1), b = path(i)
            Dim seg = CSng(DrawGeometry.Dist(a, b))
            If seg <= 0 Then Continue For
            Dim dx = (b.X - a.X) / seg, dy = (b.Y - a.Y) / seg
            Dim pa = If(pres Is Nothing, 1, pres(i - 1)), pb = If(pres Is Nothing, 1, pres(i))
            While nextAt <= travelled + seg
                Dim t = (nextAt - travelled) / seg
                visit(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, pa + (pb - pa) * t, dx, dy, nextAt, total)
                nextAt += spacing
            End While
            travelled += seg
        Next
    End Sub

    ''' <summary>
    ''' 同 Walk，另外內插繪圖筆的筆尖角度（弧度，NaN = 沒有）與扁平程度（1 = 圓）。
    ''' visit(x, y, 筆壓, 方向x, 方向y, 已走距離, 總長, 角度, 扁平)
    ''' </summary>
    Private Delegate Sub WalkVisitor(x As Single, y As Single, p As Single, dx As Single, dy As Single, s As Single, total As Single,
                                     angle As Single, flat As Single)

    Private Shared Sub WalkEx(path As PointF(), pres As Single(), angles As Single(), flats As Single(), spacing As Single, visit As WalkVisitor)
        Dim total = 0.0F
        For i = 1 To path.Length - 1
            total += CSng(DrawGeometry.Dist(path(i - 1), path(i)))
        Next
        Dim angAt = Function(i As Integer) If(angles Is Nothing, Single.NaN, angles(i))
        Dim flatAt = Function(i As Integer) If(flats Is Nothing, 1.0F, flats(i))
        If path.Length = 1 OrElse total < 0.01F Then
            visit(path(0).X, path(0).Y, If(pres Is Nothing, 1, pres(0)), 1, 0, 0, 0, angAt(0), flatAt(0))
            Return
        End If
        spacing = Math.Max(0.35F, spacing)
        Dim travelled = 0.0F, nextAt = 0.0F
        For i = 1 To path.Length - 1
            Dim a = path(i - 1), b = path(i)
            Dim seg = CSng(DrawGeometry.Dist(a, b))
            If seg <= 0 Then Continue For
            Dim dx = (b.X - a.X) / seg, dy = (b.Y - a.Y) / seg
            Dim pa = If(pres Is Nothing, 1, pres(i - 1)), pb = If(pres Is Nothing, 1, pres(i))
            Dim aa = angAt(i - 1), ab = angAt(i)
            Dim fa = flatAt(i - 1), fb = flatAt(i)
            While nextAt <= travelled + seg
                Dim t = (nextAt - travelled) / seg
                Dim ang As Single
                If Single.IsNaN(aa) Then
                    ang = ab
                ElseIf Single.IsNaN(ab) Then
                    ang = aa
                Else
                    ' 角度走最短的方向內插
                    Dim d = CSng(Math.IEEERemainder(ab - aa, 2 * Math.PI))
                    ang = aa + d * t
                End If
                visit(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, pa + (pb - pa) * t, dx, dy, nextAt, total, ang, fa + (fb - fa) * t)
                nextAt += spacing
            End While
            travelled += seg
        Next
    End Sub

    ''' <summary>
    ''' 一個筆印的顏色變化：色相 ±hue/100 × 180°、明暗 ±lum/100 × 35%。
    ''' </summary>
    Private Shared Function JitterColor(r As Single, g As Single, b As Single, hue As Integer, lum As Integer, rnd As Random) As (R As Single, G As Single, B As Single)
        If hue <= 0 AndAlso lum <= 0 Then Return (r, g, b)
        Dim mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b))
        Dim l = (mx + mn) / 2, h = 0.0F, s = 0.0F
        If mx - mn > 0.0001F Then
            Dim d = mx - mn
            s = If(l > 0.5F, d / (2 - mx - mn), d / (mx + mn))
            If mx = r Then
                h = (g - b) / d + If(g < b, 6, 0)
            ElseIf mx = g Then
                h = (b - r) / d + 2
            Else
                h = (r - g) / d + 4
            End If
            h /= 6
        End If
        h += CSng((rnd.NextDouble() * 2 - 1) * hue / 100.0 * 0.5)
        h -= CSng(Math.Floor(h))
        l = Clamp01(l + CSng((rnd.NextDouble() * 2 - 1) * lum / 100.0 * 0.35))
        If s <= 0 Then Return (l, l, l)
        Dim q = If(l < 0.5F, l * (1 + s), l + s - l * s), p = 2 * l - q
        Dim hue2 = Function(t As Single) As Single
                       If t < 0 Then t += 1
                       If t > 1 Then t -= 1
                       If t < 1 / 6.0F Then Return p + (q - p) * 6 * t
                       If t < 0.5F Then Return q
                       If t < 2 / 3.0F Then Return p + (q - p) * (2 / 3.0F - t) * 6
                       Return p
                   End Function
        Return (hue2(h + 1 / 3.0F), hue2(h), hue2(h - 1 / 3.0F))
    End Function

    ''' <summary>頭尾收筆：起筆與收筆各一段由細到粗。</summary>
    Private Shared Function TaperAt(s As Single, total As Single, widthPx As Single) As Single
        If total <= 0 Then Return 1
        Dim len = Math.Min(widthPx * 3, total / 3)
        If len <= 0 Then Return 1
        Dim a = Math.Min(1, s / len), b = Math.Min(1, (total - s) / len)
        Return CSng(Math.Max(0.12, Math.Pow(Math.Min(a, b), 0.6)))
    End Function

    Private Shared Sub Stamp(cov As Single(), cv As Canvas, path As PointF(), pres As Single(), widthPx As Single,
                             sp As BrushSpec, flow As Single, edge As Single)
        Stamp(cov, cv, path, pres, Nothing, Nothing, widthPx, sp, flow, edge, Nothing, Nothing, Nothing)
    End Sub

    ''' <summary>
    ''' 沿路徑蓋筆印。layer 不是 Nothing 時套用進階參數：間距、散佈、大小變化、筆壓濃淡、
    ''' 繪圖筆的筆尖角度與扁平（angles、flats），以及顏色變化（寫進 tint，每像素 3 個值）。
    ''' </summary>
    Private Shared Sub Stamp(cov As Single(), cv As Canvas, path As PointF(), pres As Single(), angles As Single(), flats As Single(),
                             widthPx As Single, sp As BrushSpec, flow As Single, edge As Single,
                             layer As DrawLayer, rnd As Random, tint As Single())
        Dim amount = sp.Amount * If(sp.Additive, flow, 1)
        Dim cap = If(sp.Additive, 1, flow)
        Dim spacing = widthPx * sp.Spacing
        Dim scatter = 0.0F, sizeJ = 0.0F, presOp = 0.0F
        Dim baseC As Color = Color.Black
        If layer IsNot Nothing Then
            If layer.SpacingPct > 0 Then spacing = widthPx * layer.SpacingPct / 100.0F
            scatter = layer.Scatter / 100.0F
            sizeJ = layer.SizeJitter / 100.0F
            presOp = layer.PressureOpacity / 100.0F
            baseC = Color.FromArgb(layer.StrokeColorArgb)
        End If
        WalkEx(path, pres, angles, flats, spacing,
             Sub(x, y, p, dx, dy, s, total, ang, flat)
                 Dim size = widthPx * (1 - sp.SizePressure * (1 - p))
                 If sp.Taper Then size *= TaperAt(s, total, widthPx)
                 Dim a = amount * cap
                 Dim tipAngle = Single.NaN, ratio = 1.0F
                 If Not Single.IsNaN(ang) Then
                     ' 繪圖筆：筆越斜筆觸越寬越扁，扁的方向跟著筆。
                     tipAngle = ang
                     ratio = flat
                     size *= 1 + 0.6F * (1 - flat)
                 End If
                 If rnd IsNot Nothing Then
                     If sizeJ > 0 Then size *= Math.Max(0.15F, 1 + CSng(rnd.NextDouble() * 2 - 1) * sizeJ * 0.7F)
                     If scatter > 0 Then
                         Dim off = CSng(rnd.NextDouble() * 2 - 1) * scatter * widthPx * 1.5F
                         Dim along = CSng(rnd.NextDouble() * 2 - 1) * scatter * widthPx * 0.4F
                         x += -dy * off + dx * along : y += dx * off + dy * along
                     End If
                 End If
                 If presOp > 0 Then a *= 1 - presOp * (1 - p)
                 Dim col = (R:=0.0F, G:=0.0F, B:=0.0F)
                 If tint IsNot Nothing Then col = JitterColor(baseC.R / 255.0F, baseC.G / 255.0F, baseC.B / 255.0F, layer.HueJitter, layer.LumJitter, rnd)
                 Dab(cov, Nothing, 0, cv, x, y, Math.Max(0.5F, size / 2), edge, a, sp.Additive, sp.Tip, tipAngle, ratio, tint, col.R, col.G, col.B)
             End Sub)
    End Sub

    Private Shared Sub Dab(cov As Single(), streak As Single(), streakValue As Single, cv As Canvas,
                           cx As Single, cy As Single, radius As Single, edge As Single, amount As Single,
                           additive As Boolean, tip As TipKind)
        Dab(cov, streak, streakValue, cv, cx, cy, radius, edge, amount, additive, tip, Single.NaN, 1, Nothing, 0, 0, 0)
    End Sub

    ''' <summary>
    ''' 一個筆印。累加模式：cov += a × (1 − cov)；否則取最大值（一筆內不會越疊越濃）。
    ''' streak 不是 Nothing 時，覆蓋率變大的像素記下這根筆毛的明暗值（油畫、壓克力的筆痕）。
    ''' angle（弧度）與 ratio（短軸 / 長軸）讓筆尖變成任意方向的橢圓；麥克筆預設斜 45°、扁 0.38。
    ''' tint 不是 Nothing 時，同時記下這個筆印的顏色（tr、tg、tb），供顏色變化使用。
    ''' </summary>
    Private Shared Sub Dab(cov As Single(), streak As Single(), streakValue As Single, cv As Canvas,
                           cx As Single, cy As Single, radius As Single, edge As Single, amount As Single,
                           additive As Boolean, tip As TipKind, angle As Single, ratio As Single,
                           tint As Single(), tr As Single, tg As Single, tb As Single)
        Dim lx = cx - cv.OX, ly = cy - cv.OY
        Dim reach = radius + 1
        Dim x0 = Math.Max(0, CInt(Math.Floor(lx - reach))), x1 = Math.Min(cv.W - 1, CInt(Math.Ceiling(lx + reach)))
        Dim y0 = Math.Max(0, CInt(Math.Floor(ly - reach))), y1 = Math.Min(cv.H - 1, CInt(Math.Ceiling(ly + reach)))
        If x0 > x1 OrElse y0 > y1 Then Return
        Dim softW = radius * edge
        Dim inner = radius - softW
        If tip = TipKind.Chisel Then
            ' 斜切的麥克筆頭：預設轉 45°、短軸縮成 0.38；繪圖筆的角度與傾斜會改變它。
            If Single.IsNaN(angle) Then angle = CSng(Math.PI / 4)
            ratio = 0.38F * ratio
        End If
        Dim oval = Not Single.IsNaN(angle) AndAlso ratio < 0.999F
        Dim ca = If(oval, CSng(Math.Cos(angle)), 1.0F), sa = If(oval, CSng(Math.Sin(angle)), 0.0F)
        Dim invRatio = If(oval, 1 / Math.Max(0.05F, ratio), 1.0F)
        For y = y0 To y1
            Dim dy = y + 0.5F - ly
            Dim row = y * cv.W
            For x = x0 To x1
                Dim dx = x + 0.5F - lx
                Dim d As Single
                If oval Then
                    Dim u = dx * ca + dy * sa, v = (dy * ca - dx * sa) * invRatio
                    d = CSng(Math.Sqrt(u * u + v * v))
                Else
                    d = CSng(Math.Sqrt(dx * dx + dy * dy))
                End If
                If d >= radius + 0.5F Then Continue For
                Dim a As Single
                If tip = TipKind.Gaussian Then
                    Dim t = d / radius
                    a = CSng(Math.Exp(-4.5 * t * t)) * If(t >= 1, 0, 1)
                ElseIf softW < 1 Then
                    a = Math.Min(1, Math.Max(0, radius + 0.5F - d))
                ElseIf d <= inner Then
                    a = 1
                Else
                    Dim t = Math.Min(1, (d - inner) / softW)
                    a = CSng(0.5 + 0.5 * Math.Cos(t * Math.PI))
                End If
                a *= amount
                If a <= 0 Then Continue For
                Dim i = row + x
                If additive Then
                    Dim inc = a * (1 - cov(i))
                    cov(i) += inc
                    If tint IsNot Nothing AndAlso inc > 0 Then
                        ' 依這個筆印加進來的比例混色
                        Dim w = inc / Math.Max(0.0001F, cov(i))
                        tint(i * 3) += (tr - tint(i * 3)) * w
                        tint(i * 3 + 1) += (tg - tint(i * 3 + 1)) * w
                        tint(i * 3 + 2) += (tb - tint(i * 3 + 2)) * w
                    End If
                ElseIf a > cov(i) Then
                    cov(i) = a
                    If streak IsNot Nothing Then streak(i) = streakValue
                    If tint IsNot Nothing Then tint(i * 3) = tr : tint(i * 3 + 1) = tg : tint(i * 3 + 2) = tb
                End If
            Next
        Next
    End Sub

    ''' <summary>
    ''' 筆毛筆刷（油畫、壓克力、乾刷、毛筆）：一排細筆毛橫跨筆寬，各自沿路徑留下痕跡；
    ''' 乾刷與毛筆的筆毛會依雜訊斷開（飛白），油畫的每根筆毛明暗略有不同。
    ''' </summary>
    Private Shared Sub Bristles(cov As Single(), streak As Single(), cv As Canvas, path As PointF(), pres As Single(),
                                widthPx As Single, layer As DrawLayer, sp As BrushSpec, flow As Single, edge As Single,
                                Optional angles As Single() = Nothing, Optional flats As Single() = Nothing, Optional tint As Single() = Nothing)
        Dim count = CInt(Math.Max(6, Math.Min(36, widthPx / 1.6)))
        Dim seed = layer.Seed * 131
        Dim offsets(count - 1) As Single, sizes(count - 1) As Single, tones(count - 1) As Single
        ' 顏色變化：每根筆毛沾的顏色各有一點不同（像沒調勻的顏料）
        Dim cols(count - 1) As (R As Single, G As Single, B As Single)
        Dim baseC = Color.FromArgb(layer.StrokeColorArgb)
        Dim colRnd As New Random(seed + 5)
        For i = 0 To count - 1
            offsets(i) = (i + 0.5F) / count - 0.5F + (Hash(i, 3, seed) - 0.5F) * 0.6F / count
            sizes(i) = 0.75F + Hash(i, 7, seed) * 0.5F
            tones(i) = Hash(i, 11, seed) * 2 - 1
            If tint IsNot Nothing Then cols(i) = JitterColor(baseC.R / 255.0F, baseC.G / 255.0F, baseC.B / 255.0F, layer.HueJitter, layer.LumJitter, colRnd)
        Next
        Dim brush = layer.Brush
        Dim presOp = layer.PressureOpacity / 100.0F
        WalkEx(path, pres, angles, flats, Math.Max(0.5F, widthPx / count * 0.6F),
             Sub(x, y, p, dx, dy, s, total, ang, flat)
                 Dim size = widthPx * (1 - sp.SizePressure * (1 - p))
                 If sp.Taper Then size *= TaperAt(s, total, widthPx)
                 Dim along = If(total > 0, s / total, 0)
                 ' 筆毛排成一列；預設橫跨筆畫方向，繪圖筆的角度會轉動這一列，筆越斜排得越寬。
                 Dim nx = -dy, ny = dx
                 If Not Single.IsNaN(ang) Then
                     nx = CSng(Math.Cos(ang)) : ny = CSng(Math.Sin(ang))
                     size *= 1 + 0.6F * (1 - flat)
                 End If
                 Dim pf = If(presOp > 0, 1 - presOp * (1 - p), 1.0F)
                 Dim rb = Math.Max(0.55F, size / count * 0.8F)
                 For i = 0 To count - 1
                     Dim n = Noise1(s / Math.Max(1, widthPx * 0.9F), i * 17 + seed)
                     Dim intensity As Single
                     Select Case brush
                         Case BrushKind.DryBrush
                             Dim thr = 0.42F + 0.3F * along
                             intensity = If(n > thr, 1, 0)
                         Case BrushKind.ChineseBrush
                             Dim thr = If(along < 0.6F, -1, (along - 0.6F) / 0.4F * 0.8F)
                             intensity = If(n > thr, 1, 0)
                         Case BrushKind.OilPaint
                             intensity = If(along > 0.88F AndAlso n < (along - 0.88F) * 6, 0, 0.78F + 0.22F * n)
                         Case Else ' 壓克力
                             intensity = If(along > 0.93F AndAlso n < (along - 0.93F) * 8, 0, 0.92F + 0.08F * n)
                     End Select
                     If intensity <= 0 Then Continue For
                     Dim o = offsets(i) * size
                     Dab(cov, streak, tones(i), cv, x + nx * o, y + ny * o, rb * sizes(i), Math.Min(edge, 0.6F),
                         intensity * flow * pf, False, TipKind.Round, Single.NaN, 1, tint, cols(i).R, cols(i).G, cols(i).B)
                 Next
             End Sub)
    End Sub

    ''' <summary>血液：沿線隨機往下滴的血滴。</summary>
    Private Shared Sub Drips(cov As Single(), cv As Canvas, figs As List(Of DrawGeometry.Figure), widthPx As Single, rnd As Random)
        For Each f In figs
            Dim path = If(f.Closed, f.Points.Concat({f.Points(0)}).ToArray(), f.Points)
            Walk(path, Nothing, widthPx * 0.6F,
                 Sub(x, y, p, dx, dy, s, total)
                     If rnd.NextDouble() > 0.1 Then Return
                     Dim len = CSng(widthPx * (1 + rnd.NextDouble() * 5))
                     Dim r = CSng(widthPx * (0.12 + rnd.NextDouble() * 0.12))
                     Dim yy = 0.0F
                     While yy < len
                         Dab(cov, Nothing, 0, cv, x, y + yy, r * (1 - 0.25F * yy / len), 0, 1, False, TipKind.Round)
                         yy += Math.Max(0.5F, r * 0.4F)
                     End While
                     Dab(cov, Nothing, 0, cv, x, y + len, r * 1.45F, 0, 1, False, TipKind.Round)
                 End Sub)
        Next
    End Sub

    '=====================================================================
    ' 上色：覆蓋率 → 顏色與透明度（紙紋、顆粒、暈染、厚塗光影、材質）
    '=====================================================================

    Private Shared Sub Shade(cv As Canvas, mask As Single(), streak As Single(), layer As DrawLayer, color As Color,
                             widthPx As Single, imageH As Integer, isFill As Boolean, Optional tint As Single() = Nothing)
        If layer.Brush = BrushKind.Texture Then
            ShadeMaterial(cv, mask, layer, imageH)
            Return
        End If
        Dim cr = color.R / 255.0F, cg = color.G / 255.0F, cb = color.B / 255.0F
        Dim brush = layer.Brush
        Dim seed = layer.Seed
        ' 紋理尺寸以照片高度為準：預覽與匯出一致。
        Dim fine = Math.Max(1.0F, imageH / 900.0F)
        Dim medium = Math.Max(1.5F, imageH / 260.0F)
        Dim blurred As Single() = Nothing
        If brush = BrushKind.Watercolor Then
            Dim radius = CInt(Math.Max(1, If(isFill, Math.Min(cv.W, cv.H) * 0.02F, widthPx * 0.3F)))
            blurred = BoxBlur(mask, cv.W, cv.H, radius)
        End If
        Dim emboss = brush = BrushKind.OilPaint OrElse brush = BrushKind.Acrylic OrElse
                     (brush = BrushKind.FX AndAlso layer.Fx = FxKind.Blood)
        If isFill Then emboss = False

        For y = 0 To cv.H - 1
            Dim gy = y + cv.OY
            For x = 0 To cv.W - 1
                Dim i = y * cv.W + x
                Dim a = mask(i)
                If a <= 0.001F Then Continue For
                If a > 1 Then a = 1
                Dim gx = x + cv.OX
                Dim alpha = a, lum = 1.0F
                Select Case brush
                    Case BrushKind.Pencil
                        Dim n = Fbm(gx / fine, gy / fine, seed, 2)
                        alpha = a * Clamp01(0.2F + (n - 0.5F) * 2.6F + a * 0.45F) * 0.9F
                    Case BrushKind.Charcoal
                        Dim n = Fbm(gx / fine, gy / (fine * 1.6F), seed, 3)
                        alpha = a * SmoothStep(0.32F, 0.72F, n * 0.85F + a * 0.45F)
                        lum = 0.82F
                    Case BrushKind.Chalk
                        Dim n = Fbm(gx / (fine * 1.3F), gy / (fine * 1.3F), seed + 5, 2)
                        alpha = a * SmoothStep(0.38F, 0.5F, n + (a - 0.6F) * 0.35F)
                        lum = 1.06F
                    Case BrushKind.Crayon
                        Dim n = Fbm(gx / fine, gy / (fine * 2.5F), seed + 9, 2)
                        alpha = a * SmoothStep(0.22F, 0.5F, n + (a - 0.5F) * 0.3F) * 0.96F
                    Case BrushKind.Watercolor
                        Dim edge = Clamp01((a - blurred(i)) * 2.8F)
                        Dim gran = Fbm(gx / medium, gy / medium, seed + 3, 3)
                        alpha = Clamp01(a * 0.45F + edge * 0.38F + (gran - 0.5F) * 0.16F * a)
                        lum = 0.96F + gran * 0.08F
                    Case BrushKind.Marker
                        alpha = a * 0.62F
                    Case BrushKind.OilPaint, BrushKind.Acrylic, BrushKind.DryBrush
                        Dim s = If(streak Is Nothing, 0, streak(i))
                        lum = 1 + s * If(brush = BrushKind.OilPaint, 0.13F, If(brush = BrushKind.DryBrush, 0.08F, 0.05F))
                        If brush = BrushKind.DryBrush Then alpha = a * 0.95F
                    Case BrushKind.FX ' 血液
                        lum = 0.78F
                End Select
                If emboss Then
                    ' 以覆蓋率的斜率當作厚度起伏，光從左上來。
                    Dim e = (Sample(mask, cv, x - 1, y - 1) - Sample(mask, cv, x + 1, y + 1))
                    Select Case brush
                        Case BrushKind.OilPaint : lum += e * 0.55F
                        Case BrushKind.Acrylic : lum += e * 0.3F
                        Case BrushKind.FX
                            lum += e * 1.4F
                            If e > 0.18F Then lum += (e - 0.18F) * 2.2F
                        Case Else : lum += e * 0.35F
                    End Select
                End If
                If alpha <= 0.001F Then Continue For
                If tint IsNot Nothing Then
                    cv.Over(i, Shift(tint(i * 3), lum), Shift(tint(i * 3 + 1), lum), Shift(tint(i * 3 + 2), lum), alpha)
                    Continue For
                End If
                cv.Over(i, Shift(cr, lum), Shift(cg, lum), Shift(cb, lum), alpha)
            Next
        Next
    End Sub

    ''' <summary>亮度倍率：大於 1 往白色靠，小於 1 變暗。</summary>
    Private Shared Function Shift(c As Single, lum As Single) As Single
        If lum >= 1 Then Return Math.Min(1, c + (1 - c) * (lum - 1))
        Return Math.Max(0, c * lum)
    End Function

    Private Shared Function Sample(a As Single(), cv As Canvas, x As Integer, y As Integer) As Single
        If x < 0 OrElse y < 0 OrElse x >= cv.W OrElse y >= cv.H Then Return 0
        Return Math.Min(1, a(y * cv.W + x))
    End Function

    Private Shared Function FireColor(t As Single) As (R As Single, G As Single, B As Single)
        Dim stops = {(0.0F, 1.0F, 0.97F, 0.8F), (0.3F, 1.0F, 0.78F, 0.24F), (0.62F, 0.96F, 0.43F, 0.1F), (1.0F, 0.66F, 0.12F, 0.06F)}
        For k = 1 To stops.Length - 1
            If t <= stops(k).Item1 Then
                Dim a = stops(k - 1), b = stops(k)
                Dim u = (t - a.Item1) / (b.Item1 - a.Item1)
                Return (a.Item2 + (b.Item2 - a.Item2) * u, a.Item3 + (b.Item3 - a.Item3) * u, a.Item4 + (b.Item4 - a.Item4) * u)
            End If
        Next
        Return (stops(3).Item2, stops(3).Item3, stops(3).Item4)
    End Function

    ''' <summary>四芒星光：十字光芒加光暈，中心偏白。</summary>
    Private Shared Sub Sparkle(cv As Canvas, cx As Single, cy As Single, size As Single, tr As Single, tg As Single, tb As Single,
                               angle As Single, amount As Single)
        Dim c = CSng(Math.Cos(angle)), s = CSng(Math.Sin(angle))
        Dim lx = cx - cv.OX, ly = cy - cv.OY
        Dim x0 = Math.Max(0, CInt(lx - size - 1)), x1 = Math.Min(cv.W - 1, CInt(lx + size + 1))
        Dim y0 = Math.Max(0, CInt(ly - size - 1)), y1 = Math.Min(cv.H - 1, CInt(ly + size + 1))
        Dim thin = Math.Max(0.6F, size * 0.06F)
        For y = y0 To y1
            For x = x0 To x1
                Dim dx = x + 0.5F - lx, dy = y + 0.5F - ly
                Dim u = Math.Abs(dx * c + dy * s), v = Math.Abs(-dx * s + dy * c)
                If u > size AndAlso v > size Then Continue For
                Dim ray = Math.Max(CSng(Math.Exp(-v / thin)) * Math.Max(0, 1 - u / size), CSng(Math.Exp(-u / thin)) * Math.Max(0, 1 - v / size))
                Dim d2 = (dx * dx + dy * dy) / (size * size)
                Dim glow = CSng(Math.Exp(-d2 * 9)) * 0.55F
                Dim a = Clamp01(ray + glow) * amount
                If a <= 0.003F Then Continue For
                Dim white = Clamp01(ray * 1.3F)
                cv.Over(y * cv.W + x, tr + (1 - tr) * white, tg + (1 - tg) * white, tb + (1 - tb) * white, a)
            Next
        Next
    End Sub

    ''' <param name="stretchY">垂直方向的拉長倍數。</param>
    Private Shared Sub ForDisc(cv As Canvas, cx As Single, cy As Single, radius As Single, stretchY As Single, visit As Action(Of Integer, Single))
        Dim lx = cx - cv.OX, ly = cy - cv.OY
        Dim ry = radius * stretchY
        Dim x0 = Math.Max(0, CInt(Math.Floor(lx - radius))), x1 = Math.Min(cv.W - 1, CInt(Math.Ceiling(lx + radius)))
        Dim y0 = Math.Max(0, CInt(Math.Floor(ly - ry))), y1 = Math.Min(cv.H - 1, CInt(Math.Ceiling(ly + ry)))
        For y = y0 To y1
            For x = x0 To x1
                Dim dx = x + 0.5F - lx, dy = y + 0.5F - ly
                Dim ey = dy / stretchY
                Dim t = CSng(Math.Sqrt(dx * dx + ey * ey)) / radius
                If t < 1 Then visit(y * cv.W + x, t)
            Next
        Next
    End Sub

    '=====================================================================
    ' 文字與陰影
    '=====================================================================

    ''' <summary>圖說文字：在文字範圍內置中、自動換行，放不下時縮小字級（最小 40%）。</summary>
    Private Shared Sub DrawText(cv As Canvas, layer As DrawLayer, region As Rectangle, scale As Single)
        Dim box = DrawGeometry.TextBox(layer)
        Dim rect = New RectangleF(box.X * scale, box.Y * scale, box.Width * scale, box.Height * scale)
        If rect.Width < 2 OrElse rect.Height < 2 Then Return
        Using bmp As New Bitmap(cv.W, cv.H, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.TextRenderingHint = TextRenderingHint.AntiAlias
                g.TranslateTransform(CSng(layer.X * scale - region.X), CSng(layer.Y * scale - region.Y))
                g.RotateTransform(CSng(layer.Rotation))
                Dim size = CSng(Math.Max(0.004, layer.TextSize) * scale)
                Dim minSize = size * 0.4F
                Using sf As New StringFormat() With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                    Dim style = If(layer.TextBold, FontStyle.Bold, FontStyle.Regular)
                    Dim font = MakeFont(layer.FontName, size, style)
                    Try
                        While size > minSize
                            Dim m = g.MeasureString(layer.Text, font, New SizeF(rect.Width, 100000), sf)
                            If m.Height <= rect.Height AndAlso m.Width <= rect.Width + 1 Then Exit While
                            size *= 0.92F
                            font.Dispose()
                            font = MakeFont(layer.FontName, size, style)
                        End While
                        Using br As New SolidBrush(Color.FromArgb(layer.TextColorArgb))
                            g.DrawString(layer.Text, font, br, rect, sf)
                        End Using
                    Finally
                        font.Dispose()
                    End Try
                End Using
            End Using
            Dim px = Perspective.ReadPixels(bmp)
            For i = 0 To cv.W * cv.H - 1
                Dim a = px(i * 4 + 3)
                If a = 0 Then Continue For
                cv.Over(i, px(i * 4 + 2) / 255.0F, px(i * 4 + 1) / 255.0F, px(i * 4) / 255.0F, a / 255.0F)
            Next
        End Using
    End Sub

    Private Shared Function MakeFont(name As String, sizePx As Single, style As FontStyle) As Font
        Try
            Return New Font(name, Math.Max(1, sizePx), style, GraphicsUnit.Pixel)
        Catch ex As ArgumentException
            Return New Font(FontFamily.GenericSansSerif, Math.Max(1, sizePx), style, GraphicsUnit.Pixel)
        End Try
    End Function

    ''' <summary>右下方的柔和陰影，墊在圖層下面。</summary>
    Private Shared Sub AddShadow(cv As Canvas, widthPx As Single)
        Dim off = CInt(Math.Round(ShadowOffset(widthPx)))
        Dim blur = BoxBlur(cv.A, cv.W, cv.H, CInt(ShadowBlur(widthPx)))
        For y = cv.H - 1 To 0 Step -1
            For x = cv.W - 1 To 0 Step -1
                Dim sx = x - off, sy = y - off
                If sx < 0 OrElse sy < 0 Then Continue For
                Dim sa = Math.Min(1, blur(sy * cv.W + sx)) * 0.45F
                If sa <= 0.002F Then Continue For
                Dim i = y * cv.W + x
                ' 陰影在下：結果 = 圖層 + 陰影 × (1 − 圖層 alpha)
                Dim k = 1 - cv.A(i)
                cv.A(i) += sa * k
            Next
        Next
    End Sub

    '=====================================================================
    ' 雜訊與模糊
    '=====================================================================

    Private Shared Function Hash(x As Integer, y As Integer, seed As Integer) As Single
        ' VB 會檢查整數溢位：用 ULong 相乘再遮成 32 位元。
        Const Mask32 As ULong = &HFFFFFFFFUL
        Dim h = ((CULng(x And &H7FFFFFFF) * 374761393UL) Xor (CULng(y And &H7FFFFFFF) * 668265263UL) Xor
                 (CULng(seed And &H7FFFFFFF) * 2246822519UL)) And Mask32
        h = ((h Xor (h >> 13)) * 1274126177UL) And Mask32
        h = h Xor (h >> 16)
        Return CSng(h And &HFFFFFFUL) / 16777216.0F
    End Function

    Private Shared Function Noise2(x As Single, y As Single, seed As Integer) As Single
        Dim ix = CInt(Math.Floor(x)), iy = CInt(Math.Floor(y))
        Dim fx = x - ix, fy = y - iy
        fx = fx * fx * (3 - 2 * fx) : fy = fy * fy * (3 - 2 * fy)
        Dim a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed)
        Return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy
    End Function

    Private Shared Function Noise1(x As Single, seed As Integer) As Single
        Dim ix = CInt(Math.Floor(x))
        Dim f = x - ix
        f = f * f * (3 - 2 * f)
        Dim a = Hash(ix, 0, seed), b = Hash(ix + 1, 0, seed)
        Return a + (b - a) * f
    End Function

    Private Shared Function Fbm(x As Single, y As Single, seed As Integer, octaves As Integer) As Single
        Dim sum = 0.0F, amp = 0.5F, norm = 0.0F
        For o = 0 To octaves - 1
            sum += Noise2(x, y, seed + o * 101) * amp
            norm += amp
            x *= 2.03F : y *= 2.03F : amp *= 0.5F
        Next
        Return sum / norm
    End Function

    Private Shared Function Clamp01(v As Single) As Single
        Return If(v < 0, 0, If(v > 1, 1, v))
    End Function

    Private Shared Function SmoothStep(e0 As Single, e1 As Single, v As Single) As Single
        Dim t = Clamp01((v - e0) / (e1 - e0))
        Return t * t * (3 - 2 * t)
    End Function

    ''' <summary>兩次一維平均模糊（浮點）。</summary>
    Private Shared Function BoxBlur(src As Single(), w As Integer, h As Integer, radius As Integer) As Single()
        Dim tmp(w * h - 1) As Single, dst(w * h - 1) As Single
        Dim inv = 1.0F / (radius * 2 + 1)
        For y = 0 To h - 1
            Dim row = y * w
            Dim sum = 0.0F
            For k = -radius To radius
                sum += src(row + Math.Max(0, Math.Min(w - 1, k)))
            Next
            For x = 0 To w - 1
                tmp(row + x) = sum * inv
                sum += src(row + Math.Min(w - 1, x + radius + 1)) - src(row + Math.Max(0, x - radius))
            Next
        Next
        For x = 0 To w - 1
            Dim sum = 0.0F
            For k = -radius To radius
                sum += tmp(Math.Max(0, Math.Min(h - 1, k)) * w + x)
            Next
            For y = 0 To h - 1
                dst(y * w + x) = sum * inv
                sum += tmp(Math.Min(h - 1, y + radius + 1) * w + x) - tmp(Math.Max(0, y - radius) * w + x)
            Next
        Next
        Return dst
    End Function

    '=====================================================================
    ' 預覽
    '=====================================================================

    ''' <summary>筆刷面板的預覽：在透明底上畫一條 S 形筆觸（筆壓由輕到重再變輕）。</summary>
    ''' <summary>混色、塗抹、仿製筆的預覽：在彩色直條紋上實際抹一筆（仿製從上方一截取樣）。</summary>
    Private Shared Function SamplingPreview(layer As DrawLayer, w As Integer, h As Integer) As Bitmap
        Dim below(w * h * 4 - 1) As Byte
        Dim bands = {Color.FromArgb(232, 69, 90), Color.FromArgb(250, 190, 60), Color.FromArgb(80, 180, 110), Color.FromArgb(60, 130, 220)}
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim c = If(layer.Brush = BrushKind.Clone, If((x \ 6 + y \ 6) Mod 2 = 0, Color.FromArgb(70, 72, 80), Color.FromArgb(230, 232, 236)),
                           bands(Math.Min(bands.Length - 1, x * bands.Length \ w)))
                Dim i = (y * w + x) * 4
                below(i) = c.B : below(i + 1) = c.G : below(i + 2) = c.R : below(i + 3) = 255
            Next
        Next
        layer.Opacity = 100 : layer.Wet = 70 : layer.Softness = 40
        If layer.Brush = BrushKind.Clone Then layer.CloneDX = 0.37
        Dim layerPx(w * h * 4 - 1) As Byte
        ApplySamplingOp(layerPx, below, w, h, layer, 0, 0)
        ' 只顯示改動過的地方（其餘透明），格子底色才看得出來是哪一筆。
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Perspective.WritePixels(bmp, layerPx)
        If layer.Brush <> BrushKind.Clone Then
            ' 塗抹、混色：底下襯淡淡的條紋，看得出顏色被推開
            Using faint As New Bitmap(w, h, PixelFormat.Format32bppArgb)
                For i = 3 To below.Length - 1 Step 4
                    below(i) = 70
                Next
                Perspective.WritePixels(faint, below)
                Using g = Graphics.FromImage(faint)
                    g.DrawImage(bmp, 0, 0, w, h)
                End Using
                Return New Bitmap(faint)
            End Using
        End If
        Return bmp
    End Function

    Public Shared Function BrushPreview(brush As BrushKind, fx As FxKind, material As MaterialKind, w As Integer, h As Integer,
                                        color As Color) As Bitmap
        Dim stroke As New DrawStroke()
        Dim n = 40
        For i = 0 To n
            Dim t = i / CSng(n)
            Dim x = (0.12F + t * 0.76F) * w / h
            Dim y = 0.55F + CSng(Math.Sin(t * Math.PI * 2)) * 0.16F
            If brush = BrushKind.FX AndAlso fx <> FxKind.Blood Then y += 0.12F
            If brush = BrushKind.FX AndAlso fx = FxKind.Blood Then y -= 0.12F
            stroke.Points.Add(New DrawPoint(x, y, CSng(0.45 + 0.55 * Math.Sin(t * Math.PI))))
        Next
        Dim layer As New DrawLayer With {
            .Shape = DrawShape.Freehand, .Brush = brush, .Fx = fx, .Material = material,
            .StrokeColorArgb = color.ToArgb(), .StrokeWidth = If(brush = BrushKind.Pencil, 0.1, 0.2), .Seed = 7,
            .Strokes = New List(Of DrawStroke) From {stroke}}
        If Global.PhotoEdit.DrawLayer.SamplesCanvas(brush) Then Return SamplingPreview(layer, w, h)
        If brush = BrushKind.StickerHose Then layer.StrokeWidth = 0.13
        Dim r = Render(layer, w, h)
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        If r.Bitmap IsNot Nothing Then
            Using g = Graphics.FromImage(bmp)
                g.DrawImage(r.Bitmap, r.Region)
            End Using
            r.Bitmap.Dispose()
        End If
        Return bmp
    End Function
End Class
