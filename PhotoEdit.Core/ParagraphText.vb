Imports System.Drawing

''' <summary>段落文字的對齊。</summary>
Public Enum TextAlignKind
    Left = 0
    Center = 1
    Right = 2
    ''' <summary>左右對齊：每行（段落最後一行除外）撐滿欄寬。</summary>
    Justify = 3
End Enum

''' <summary>
''' 段落文字框（同 CorelDRAW 的段落文字）：字級固定，文字在框裡自動換行、可以分欄，放不下的部分不顯示（編輯器會標出溢出）。
''' 換行：英文在空白處斷、中日韓文字每個字都可以斷；行首不放「，。、！？；：）」』」等標點（擠到上一行）。
''' 排版結果是一個個字的位置（GlyphPlace），畫圖、轉曲線、匯出 SVG 都用同一份，所以看起來一致。
''' </summary>
Partial Public NotInheritable Class DrawGeometry

    Private Const NoLineStart As String = "，。、！？；：）」』】》〉,.!?;:)]}%…—～ー々ゝゞ"

    Private Shared Function IsCjk(ch As Char) As Boolean
        Return AscW(ch) >= &H2E80 OrElse (AscW(ch) >= &H3000 AndAlso AscW(ch) <= &H303F)
    End Function

    ''' <summary>段落文字是否有放不下的部分（以 1000 像素為照片高度排版）。</summary>
    Public Shared Function ParagraphOverflows(layer As DrawLayer) As Boolean
        Dim size As Single, family As FontFamily = Nothing, overflow As Boolean
        ParagraphLayout(layer, 1000, size, family, overflow)
        Return overflow
    End Function

    ''' <summary>
    ''' 段落文字的排版：回傳每個字的位置（座標：照片高度 = scale 像素，已套用圖層的位置與旋轉）。
    ''' overflow：還有字放不下。
    ''' </summary>
    Public Shared Function ParagraphLayout(layer As DrawLayer, scale As Single, ByRef size As Single, ByRef family As FontFamily, ByRef overflow As Boolean) As List(Of GlyphPlace)
        Dim result As New List(Of GlyphPlace)()
        overflow = False
        family = FamilyOf(layer.FontName)
        size = CSng(Math.Max(0.004, layer.TextSize) * scale)
        Dim text = If(layer.Text, "").Replace(vbCr, "")
        If text.Length = 0 Then Return result
        Dim box = TextBox(layer)
        Dim rect = New RectangleF(box.X * scale, box.Y * scale, box.Width * scale, box.Height * scale)
        Dim cols = Math.Max(1, Math.Min(6, layer.TextColumns))
        Dim gap = size * 1.0F
        Dim colW = (rect.Width - gap * (cols - 1)) / cols
        If colW < size * 0.8F OrElse rect.Height < size * 0.5F Then overflow = True : Return result
        Dim style = layer.TextFontStyle
        Dim em = Math.Max(1, family.GetEmHeight(style))
        Dim ascent = size * family.GetCellAscent(style) / em
        Dim lineH = size * family.GetLineSpacing(style) / em * Math.Max(50, Math.Min(400, layer.LineSpacing)) / 100.0F
        ' 每個字的寬度
        Dim widths(text.Length - 1) As Single
        Using bmp As New Bitmap(8, 8), g = Graphics.FromImage(bmp), font As New Font(family, Math.Max(1, size), style, GraphicsUnit.Pixel),
              sf = DirectCast(StringFormat.GenericTypographic.Clone(), StringFormat)
            sf.FormatFlags = sf.FormatFlags Or StringFormatFlags.MeasureTrailingSpaces
            Dim cache As New Dictionary(Of Char, Single)()
            For i = 0 To text.Length - 1
                Dim ch = text(i)
                If ch = vbLf Then Continue For
                Dim w As Single
                If Not cache.TryGetValue(ch, w) Then
                    w = g.MeasureString(ch.ToString(), font, PointF.Empty, sf).Width
                    cache(ch) = w
                End If
                widths(i) = w
            Next
        End Using
        ' 斷成一行行（每行是一段字的索引範圍）
        Dim lines As New List(Of (Start As Integer, Count As Integer, Last As Boolean))()
        Dim pStart = 0
        For Each para In text.Split(ChrW(10))
            ' 可以斷開的單位：英文單字（含後面的空白）、一個中日韓字、一個空白
            Dim tokens As New List(Of (Start As Integer, Len As Integer))()
            Dim i = 0
            While i < para.Length
                Dim j = i + 1
                If Not IsCjk(para(i)) AndAlso Not Char.IsWhiteSpace(para(i)) Then
                    While j < para.Length AndAlso Not IsCjk(para(j)) AndAlso Not Char.IsWhiteSpace(para(j))
                        j += 1
                    End While
                End If
                While j < para.Length AndAlso para(j) = " "c
                    j += 1
                End While
                tokens.Add((i, j - i))
                i = j
            End While
            Dim lineStart = 0, lineW = 0.0F
            Dim lineTokens = 0
            Dim tokW = Function(t As (Start As Integer, Len As Integer)) As Single
                           Dim s = 0.0F
                           For k = t.Start To t.Start + t.Len - 1
                               s += widths(pStart + k)
                           Next
                           Return s
                       End Function
            Dim trimmedW = Function(t As (Start As Integer, Len As Integer)) As Single
                               Dim s = tokW(t), k = t.Start + t.Len - 1
                               While k >= t.Start AndAlso para(k) = " "c
                                   s -= widths(pStart + k) : k -= 1
                               End While
                               Return s
                           End Function
            Dim ti = 0
            While ti < tokens.Count
                Dim t = tokens(ti)
                Dim fits = lineW + trimmedW(t) <= colW + 0.5F
                ' 行首禁則：放不下但是收尾標點時，擠在這一行
                Dim hang = Not fits AndAlso lineTokens > 0 AndAlso t.Len = 1 AndAlso NoLineStart.Contains(para(t.Start))
                If fits OrElse lineTokens = 0 OrElse hang Then
                    If Not fits AndAlso lineTokens = 0 AndAlso t.Len > 1 Then
                        ' 一個英文字比欄寬還長：硬斷
                        Dim k = t.Start, w = 0.0F
                        While k < t.Start + t.Len AndAlso (w + widths(pStart + k) <= colW OrElse k = t.Start)
                            w += widths(pStart + k) : k += 1
                        End While
                        lines.Add((pStart + lineStart, k - lineStart, False))
                        tokens(ti) = (k, t.Start + t.Len - k)
                        lineStart = k : lineW = 0 : lineTokens = 0
                        Continue While
                    End If
                    lineW += tokW(t) : lineTokens += 1 : ti += 1
                Else
                    lines.Add((pStart + lineStart, t.Start - lineStart, False))
                    lineStart = t.Start : lineW = 0 : lineTokens = 0
                End If
            End While
            lines.Add((pStart + lineStart, para.Length - lineStart, True))
            pStart += para.Length + 1
        Next
        ' 放進欄裡
        Dim col = 0, y = rect.Top
        Dim rot = layer.Rotation * Math.PI / 180, cr = Math.Cos(rot), sr = Math.Sin(rot)
        Dim cx = layer.X * scale, cy = layer.Y * scale
        For Each ln In lines
            If y + lineH > rect.Bottom + 0.5F AndAlso y > rect.Top Then
                col += 1 : y = rect.Top
                If col >= cols Then overflow = True : Exit For
            End If
            Dim x0 = rect.Left + col * (colW + gap)
            ' 行寬（去掉行尾空白）
            Dim n = ln.Count
            While n > 0 AndAlso text(ln.Start + n - 1) = " "c
                n -= 1
            End While
            Dim w = 0.0F
            For k = ln.Start To ln.Start + n - 1
                w += widths(k)
            Next
            Dim extra = 0.0F, gaps = 0, perGap = 0.0F, byChar = False
            Select Case layer.TextAlign
                Case TextAlignKind.Center : x0 += (colW - w) / 2
                Case TextAlignKind.Right : x0 += colW - w
                Case TextAlignKind.Justify
                    If Not ln.Last AndAlso n > 1 Then
                        extra = colW - w
                        For k = ln.Start To ln.Start + n - 2
                            If text(k) = " "c Then gaps += 1
                        Next
                        If gaps = 0 Then
                            ' 沒有空白：中日韓文字可以在字與字之間撐開；英文只有一個單字時不拉開字母
                            Dim hasCjk = False
                            For k = ln.Start To ln.Start + n - 1
                                If IsCjk(text(k)) Then hasCjk = True : Exit For
                            Next
                            If hasCjk Then gaps = n - 1 : byChar = True
                        End If
                        perGap = If(gaps > 0 AndAlso extra > 0, extra / gaps, 0)
                    End If
            End Select
            Dim baseY = y + ascent
            Dim xx = x0
            For k = ln.Start To ln.Start + n - 1
                Dim ch = text(k)
                Dim wch = widths(k)
                If Not Char.IsWhiteSpace(ch) Then
                    Dim lx = xx + wch / 2, ly = baseY
                    result.Add(New GlyphPlace With {.Ch = ch.ToString(), .X = CSng(cx + lx * cr - ly * sr), .Y = CSng(cy + lx * sr + ly * cr),
                                                    .Angle = CSng(layer.Rotation), .Width = wch})
                End If
                xx += wch
                If perGap > 0 AndAlso k < ln.Start + n - 1 AndAlso (byChar OrElse ch = " "c) Then xx += perGap
            Next
            y += lineH
        Next
        Return result
    End Function
End Class
