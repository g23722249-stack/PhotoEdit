Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Text

''' <summary>
''' 文字排版與特效：多行、對齊、字距、行距、直排、弧形；外框（兩層）、底色、立體、填色（單色/漸層/彩虹/圖片/照片本身）。
''' 排版結果是以文字中心為原點、未旋轉的路徑，所有尺寸都以字高 em（像素）為準，預覽與全尺寸看起來一樣。
''' </summary>
Public NotInheritable Class TextRender
    Private Sub New()
    End Sub

    ''' <summary>特殊字型名稱：七段顯示器數字（傳統相機日期戳記）。</summary>
    Public Const SevenSegmentFont As String = "@7seg"

    Public Class TextLayout
        Implements IDisposable
        ''' <summary>以文字中心為原點的路徑（未旋轉）。</summary>
        Public Path As GraphicsPath
        Public Bounds As RectangleF
        Public Em As Single

        Public Sub Dispose() Implements IDisposable.Dispose
            Path?.Dispose()
        End Sub
    End Class

    '=====================================================================
    ' 排版
    '=====================================================================

    Public Shared Function BuildLayout(o As Overlay, em As Single) As TextLayout
        Dim seven = o.FontName = SevenSegmentFont
        Dim family = GetFamily(If(seven, "Consolas", o.FontName))
        Dim style = If(o.Bold AndAlso family.IsStyleAvailable(FontStyle.Bold), FontStyle.Bold,
                       If(family.IsStyleAvailable(FontStyle.Regular), FontStyle.Regular, FontStyle.Bold))
        Dim fmt = DirectCast(StringFormat.GenericTypographic.Clone(), StringFormat)
        fmt.FormatFlags = fmt.FormatFlags Or StringFormatFlags.MeasureTrailingSpaces
        Dim text = If(String.IsNullOrEmpty(o.Text), " ", o.Text).Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        Dim lines = text.Split(ChrW(10))
        Dim spacing = CSng(o.LetterSpacing / 100.0 * em)
        Dim result As New GraphicsPath()

        Using measureBmp As New Bitmap(1, 1), mg = Graphics.FromImage(measureBmp),
              font As New Font(family, Math.Max(1, em), style, GraphicsUnit.Pixel)
            mg.TextRenderingHint = TextRenderingHint.AntiAlias
            Dim glyph = Function(ch As String, ByRef adv As Single) As GraphicsPath
                            If seven AndAlso SevenSegment.Supports(ch) Then Return SevenSegment.Glyph(ch, em, adv)
                            Dim p As New GraphicsPath()
                            If ch <> " " Then p.AddString(ch, family, CInt(style), em, PointF.Empty, fmt)
                            adv = mg.MeasureString(ch, font, PointF.Empty, fmt).Width
                            If ch = " " Then adv = Math.Max(adv, em * 0.3F)
                            Return p
                        End Function

            If o.Vertical Then
                LayoutVertical(result, lines, em, spacing, o, glyph)
            ElseIf Math.Abs(o.Arc) >= 1 Then
                LayoutArc(result, String.Join(" ", lines), em, spacing, o.Arc, glyph)
            Else
                LayoutHorizontal(result, lines, em, spacing, o, glyph, seven, family, style, fmt, mg, font)
            End If
        End Using
        fmt.Dispose()
        family.Dispose()

        ' 以外框中心為原點。
        Dim b = result.GetBounds()
        If b.Width <= 0 OrElse b.Height <= 0 Then b = New RectangleF(0, 0, em, em)
        Using m As New Matrix()
            m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2))
            result.Transform(m)
        End Using
        Return New TextLayout With {.Path = result, .Bounds = New RectangleF(-b.Width / 2, -b.Height / 2, b.Width, b.Height), .Em = em}
    End Function

    Private Delegate Function GlyphMaker(ch As String, ByRef advance As Single) As GraphicsPath

    Private Shared Sub AddGlyph(target As GraphicsPath, glyph As GraphicsPath, m As Matrix)
        If glyph.PointCount > 0 Then
            glyph.Transform(m)
            target.AddPath(glyph, False)
        End If
        glyph.Dispose()
        m.Dispose()
    End Sub

    Private Shared Function Chars(s As String) As List(Of String)
        ' 以「字」為單位（處理 surrogate pair）。
        Dim list As New List(Of String)()
        Dim e = Globalization.StringInfo.GetTextElementEnumerator(s)
        While e.MoveNext()
            list.Add(e.GetTextElement())
        End While
        Return list
    End Function

    Private Shared Sub LayoutHorizontal(result As GraphicsPath, lines As String(), em As Single, spacing As Single, o As Overlay,
                                        glyph As GlyphMaker, seven As Boolean, family As FontFamily, style As FontStyle,
                                        fmt As StringFormat, mg As Graphics, font As Font)
        Dim lineH = em * 1.25F * o.LineSpacing / 100.0F
        Dim built As New List(Of (Path As GraphicsPath, Width As Single))()
        For Each line In lines
            Dim lp As New GraphicsPath()
            Dim width As Single
            If spacing = 0 AndAlso Not seven Then
                ' 字距不變時整行一起排，字型的字距調整（kerning）才會生效。
                If line.Length > 0 Then lp.AddString(line, family, CInt(style), em, PointF.Empty, fmt)
                width = If(line.Length = 0, 0, mg.MeasureString(line, font, PointF.Empty, fmt).Width)
            Else
                Dim x = 0.0F
                For Each ch In Chars(line)
                    Dim adv As Single
                    Dim g = glyph(ch, adv)
                    Dim m As New Matrix()
                    m.Translate(x, 0)
                    AddGlyph(lp, g, m)
                    x += adv + spacing
                Next
                width = Math.Max(0, x - spacing)
            End If
            built.Add((lp, width))
        Next
        Dim maxW = built.Max(Function(b) b.Width)
        For i = 0 To built.Count - 1
            Dim offset = Select1(o.Align, 0, (maxW - built(i).Width) / 2, maxW - built(i).Width)
            Using m As New Matrix()
                m.Translate(offset, i * lineH)
                built(i).Path.Transform(m)
            End Using
            If built(i).Path.PointCount > 0 Then result.AddPath(built(i).Path, False)
            built(i).Path.Dispose()
        Next
        ' 空白行也要佔高度：補一個看不見的點讓外框包含整段。
        If lines.Length > 1 Then
            result.AddLine(0, 0, 0, 0)
            result.AddLine(0, (lines.Length - 1) * lineH + em, 0, (lines.Length - 1) * lineH + em)
        End If
    End Sub

    ''' <summary>直排：每一行是一欄，由右而左；欄內由上而下，字置中。</summary>
    Private Shared Sub LayoutVertical(result As GraphicsPath, lines As String(), em As Single, spacing As Single, o As Overlay, glyph As GlyphMaker)
        Dim colW = em * 1.15F * o.LineSpacing / 100.0F
        Dim stepY = em + spacing
        Dim heights = lines.Select(Function(l) Chars(l).Count * stepY - If(l.Length > 0, spacing, 0)).ToList()
        Dim maxH = If(heights.Count = 0, 0, heights.Max())
        For i = 0 To lines.Length - 1
            Dim x = -i * colW
            Dim y = Select1(o.Align, 0, (maxH - heights(i)) / 2, maxH - heights(i))
            For Each ch In Chars(lines(i))
                Dim adv As Single
                Dim g = glyph(ch, adv)
                Dim m As New Matrix()
                m.Translate(x + (em - adv) / 2, y)
                AddGlyph(result, g, m)
                y += stepY
            Next
        Next
    End Sub

    ''' <summary>弧形：字沿圓弧排列並跟著轉，中間的字在最上（或最下）。</summary>
    Private Shared Sub LayoutArc(result As GraphicsPath, line As String, em As Single, spacing As Single, arcDegrees As Integer, glyph As GlyphMaker)
        Dim items As New List(Of (Path As GraphicsPath, Adv As Single))()
        For Each ch In Chars(line)
            Dim adv As Single
            Dim g = glyph(ch, adv)
            items.Add((g, adv))
        Next
        Dim total = items.Sum(Function(i) i.Adv) + spacing * Math.Max(0, items.Count - 1)
        If total <= 0 Then Return
        Dim theta = Math.Min(360, Math.Abs(arcDegrees)) * Math.PI / 180
        Dim r = total / theta
        Dim k = Math.Sign(arcDegrees)
        Dim s = 0.0
        For Each it In items
            Dim a = ((s + it.Adv / 2) / total - 0.5) * theta
            Dim m As New Matrix()
            m.Translate(CSng(r * Math.Sin(a)), CSng(k * r * (1 - Math.Cos(a))))
            m.Rotate(CSng(k * a * 180 / Math.PI))
            m.Translate(-it.Adv / 2, -em * 0.55F)
            AddGlyph(result, it.Path, m)
            s += it.Adv + spacing
        Next
    End Sub

    Private Shared Function Select1(align As Integer, left As Single, center As Single, right As Single) As Single
        Return If(align = 0, left, If(align = 2, right, center))
    End Function

    Public Shared Function GetFamily(name As String) As FontFamily
        Try
            Return New FontFamily(If(String.IsNullOrWhiteSpace(name), "Microsoft JhengHei", name))
        Catch ex As ArgumentException
            Return New FontFamily(GenericFontFamilies.SansSerif)
        End Try
    End Function

    '=====================================================================
    ' 繪製（g 已平移、旋轉到文字中心）
    '=====================================================================

    ''' <summary>以字高為準的各種尺寸（像素）。</summary>
    Public Shared Function OutlinePx(o As Overlay, em As Single) As Single
        Return CSng((o.OutlineWidth + o.Outline2Width) / 100.0 * em * 0.25)
    End Function

    Public Shared Function ExtrudePx(o As Overlay, em As Single) As Single
        Return CSng(o.ExtrudeDepth / 100.0 * em * 0.4)
    End Function

    Public Shared Function PaddingPx(o As Overlay, em As Single) As Single
        Return CSng(o.BackgroundPadding / 100.0 * em * 0.8)
    End Function

    ''' <summary>底色方塊（或印章框、字幕條）的範圍，區域座標。barLeft/barRight 為字幕條在區域座標的左右界。</summary>
    Public Shared Function BackgroundRect(o As Overlay, layout As TextLayout, barLeft As Single, barRight As Single) As RectangleF
        Dim pad = PaddingPx(o, layout.Em) + OutlinePx(o, layout.Em)
        Dim r = layout.Bounds
        r.Inflate(pad, pad)
        If o.BackgroundStyle = TextBackground.Bar Then r = RectangleF.FromLTRB(barLeft, r.Top, barRight, r.Bottom)
        Return r
    End Function

    Public Shared Sub DrawBody(g As Graphics, o As Overlay, layout As TextLayout, photo As Bitmap, photoToLocal As Matrix,
                               barLeft As Single, barRight As Single)
        Dim em = layout.Em
        Dim path = layout.Path

        ' 底色（方塊／字幕條／印章框）。
        If o.BackgroundStyle = TextBackground.Box OrElse o.BackgroundStyle = TextBackground.Bar OrElse o.BackgroundStyle = TextBackground.Frame Then
            Dim r = BackgroundRect(o, layout, barLeft, barRight)
            Dim radius = If(o.BackgroundStyle = TextBackground.Bar, 0, CSng(o.BackgroundRadius / 100.0 * em))
            Dim c = Color.FromArgb(CInt(Math.Round(o.BackgroundOpacity * 2.55)), Color.FromArgb(o.BackgroundColorArgb))
            Using bg = RoundRect(r, radius)
                If o.BackgroundStyle = TextBackground.Frame Then
                    Using pen As New Pen(c, Math.Max(2, em * 0.09F))
                        g.DrawPath(pen, bg)
                    End Using
                Else
                    Using br As New SolidBrush(c)
                        g.FillPath(br, bg)
                    End Using
                End If
            End Using
        End If

        ' 立體：從最遠處一層層往前畫，越遠越暗。
        Dim depth = ExtrudePx(o, em)
        If depth >= 1 Then
            Dim steps = Math.Min(60, CInt(Math.Ceiling(depth)))
            Dim a = o.ExtrudeAngle * Math.PI / 180
            Dim baseColor = Color.FromArgb(o.ExtrudeColorArgb)
            For i = steps To 1 Step -1
                Dim d = depth * i / steps
                Dim shade = 0.65 + 0.35 * (1 - i / CDbl(steps))
                Dim c = Color.FromArgb(255, CInt(baseColor.R * shade), CInt(baseColor.G * shade), CInt(baseColor.B * shade))
                Dim state = g.Save()
                g.TranslateTransform(CSng(Math.Cos(a) * d), CSng(Math.Sin(a) * d))
                Using br As New SolidBrush(c), pen As New Pen(c, Math.Max(1, depth / steps * 1.6F) + OutlinePx(o, em) * 2) With {.LineJoin = LineJoin.Round}
                    g.FillPath(br, path)
                    g.DrawPath(pen, path)
                End Using
                g.Restore(state)
            Next
        End If

        ' 外框：先畫最外層，再畫內層，最後填字蓋住內半邊。
        Dim o1 = CSng(o.OutlineWidth / 100.0 * em * 0.25), o2 = CSng(o.Outline2Width / 100.0 * em * 0.25)
        If o2 > 0 Then
            Using pen As New Pen(Color.FromArgb(o.Outline2ColorArgb), (o1 + o2) * 2) With {.LineJoin = LineJoin.Round}
                g.DrawPath(pen, path)
            End Using
        End If
        If o1 > 0 Then
            Using pen As New Pen(Color.FromArgb(o.OutlineColorArgb), o1 * 2) With {.LineJoin = LineJoin.Round}
                g.DrawPath(pen, path)
            End Using
        End If

        Using br = FillBrush(o, layout, photo, photoToLocal)
            g.FillPath(br, path)
        End Using
    End Sub

    Private Shared Function FillBrush(o As Overlay, layout As TextLayout, photo As Bitmap, photoToLocal As Matrix) As Brush
        Dim b = layout.Bounds
        b.Inflate(1, 1)
        Dim c1 = Color.FromArgb(o.ColorArgb)
        Select Case o.FillMode
            Case TextFill.Gradient
                Return New LinearGradientBrush(b, c1, Color.FromArgb(o.Color2Argb), o.GradientAngle, True)
            Case TextFill.Rainbow
                Dim lg As New LinearGradientBrush(b, Color.Red, Color.Violet, o.GradientAngle, True)
                lg.InterpolationColors = New ColorBlend With {
                    .Colors = {Color.FromArgb(240, 50, 60), Color.FromArgb(255, 150, 30), Color.FromArgb(255, 220, 40),
                               Color.FromArgb(60, 190, 80), Color.FromArgb(40, 170, 240), Color.FromArgb(70, 90, 220), Color.FromArgb(160, 70, 200)},
                    .Positions = {0, 0.17F, 0.33F, 0.5F, 0.67F, 0.83F, 1}}
                Return lg
            Case TextFill.Texture
                Dim img = StickerLibrary.GetImageFile(o.TexturePath)
                If img Is Nothing Then Exit Select
                Dim tb As TextureBrush
                SyncLock img
                    tb = New TextureBrush(img, WrapMode.Tile)
                End SyncLock
                ' 圖片縮放到剛好蓋滿文字範圍。
                Dim s = Math.Max(b.Width / img.Width, b.Height / img.Height)
                tb.ScaleTransform(s, s, MatrixOrder.Append)
                tb.TranslateTransform(b.X, b.Y, MatrixOrder.Append)
                Return tb
            Case TextFill.Photo
                If photo Is Nothing OrElse photoToLocal Is Nothing Then Exit Select
                ' 照片像素對齊原位置：照片座標 → 區域座標 → 文字座標。
                Dim tb As New TextureBrush(photo, WrapMode.Clamp)
                tb.Transform = photoToLocal
                Return tb
        End Select
        Return New SolidBrush(c1)
    End Function

    Public Shared Function RoundRect(r As RectangleF, radius As Single) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height))
        If d < 1 Then
            p.AddRectangle(r)
            Return p
        End If
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function
End Class

''' <summary>七段顯示器字形（日期戳記用）：數字與 - . : ' / 空白，略微右斜。</summary>
Friend NotInheritable Class SevenSegment
    Private Sub New()
    End Sub

    Private Shared ReadOnly Digits As String() = {"abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg"}

    Public Shared Function Supports(ch As String) As Boolean
        Return ch.Length = 1 AndAlso ("0123456789-.:'/ ’".Contains(ch(0)))
    End Function

    Public Shared Function Glyph(ch As String, em As Single, ByRef advance As Single) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim t = em * 0.11F
        Dim cw = em * 0.58F
        Dim x0 = t * 0.6F, x1 = cw - t * 0.6F
        Dim y0 = em * 0.08F + t / 2, ym = em * 0.5F, y1 = em * 0.92F - t / 2
        Dim gap = t * 0.28F
        Dim c = ch(0)
        If Char.IsDigit(c) Then
            For Each seg In Digits(AscW(c) - AscW("0"c))
                Select Case seg
                    Case "a"c : HSeg(p, x0 + gap, x1 - gap, y0, t)
                    Case "g"c : HSeg(p, x0 + gap, x1 - gap, ym, t)
                    Case "d"c : HSeg(p, x0 + gap, x1 - gap, y1, t)
                    Case "f"c : VSeg(p, x0, y0 + gap, ym - gap, t)
                    Case "b"c : VSeg(p, x1, y0 + gap, ym - gap, t)
                    Case "e"c : VSeg(p, x0, ym + gap, y1 - gap, t)
                    Case "c"c : VSeg(p, x1, ym + gap, y1 - gap, t)
                End Select
            Next
            advance = cw + em * 0.1F
        Else
            Select Case c
                Case "-"c : HSeg(p, x0 + gap, x1 - gap, ym, t) : advance = cw + em * 0.1F
                Case "."c : p.AddRectangle(New RectangleF(t * 0.2F, y1 - t / 2, t, t)) : advance = t * 2.2F
                Case ":"c
                    p.AddRectangle(New RectangleF(t * 0.2F, em * 0.3F, t, t))
                    p.AddRectangle(New RectangleF(t * 0.2F, em * 0.62F, t, t))
                    advance = t * 2.2F
                Case "'"c, "’"c
                    p.AddPolygon({New PointF(t * 0.9F, y0 - t / 2), New PointF(t * 1.9F, y0 - t / 2), New PointF(t * 0.9F, em * 0.36F), New PointF(t * 0.2F, em * 0.36F)})
                    advance = t * 2.6F
                Case "/"c
                    p.AddPolygon({New PointF(cw - t, y0 - t / 2), New PointF(cw, y0 - t / 2), New PointF(t, y1 + t / 2), New PointF(0, y1 + t / 2)})
                    advance = cw + em * 0.1F
                Case Else ' 空白
                    advance = cw * 0.6F
            End Select
        End If
        ' 傳統日期戳記略微右斜。
        Using m As New Matrix(1, 0, -0.12F, 1, em * 0.06F, 0)
            p.Transform(m)
        End Using
        Return p
    End Function

    Private Shared Sub HSeg(p As GraphicsPath, xa As Single, xb As Single, y As Single, t As Single)
        p.AddPolygon({New PointF(xa, y), New PointF(xa + t / 2, y - t / 2), New PointF(xb - t / 2, y - t / 2),
                      New PointF(xb, y), New PointF(xb - t / 2, y + t / 2), New PointF(xa + t / 2, y + t / 2)})
    End Sub

    Private Shared Sub VSeg(p As GraphicsPath, x As Single, ya As Single, yb As Single, t As Single)
        p.AddPolygon({New PointF(x, ya), New PointF(x + t / 2, ya + t / 2), New PointF(x + t / 2, yb - t / 2),
                      New PointF(x, yb), New PointF(x - t / 2, yb - t / 2), New PointF(x - t / 2, ya + t / 2)})
    End Sub
End Class
