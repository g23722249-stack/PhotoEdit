Imports PhotoEdit
Imports System.Drawing.Drawing2D

''' <summary>
''' 使用手冊的內容區：自己排版、畫出章節的各段（標題、段落、條列、說明項目、範例圖、小圖格、連結），
''' 或搜尋結果（關鍵字標黃）。點連結或搜尋結果時發出 Navigate。
''' </summary>
Friend Class ManualView
    Inherits ScrollableControl

    Private Const PageMargin As Integer = 22
    Private Const MaxTextWidth As Integer = 860
    Private Const Flags As TextFormatFlags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.TextBoxControl Or TextFormatFlags.NoPadding Or TextFormatFlags.PreserveGraphicsTranslateTransform

    ''' <summary>要跳到某章節（blockIndex = -1 表示從頭）。</summary>
    Public Event Navigate(section As ManualSection, blockIndex As Integer)

    ''' <summary>範例圖所在的資料夾。</summary>
    Public Property ImageFolder As String = ""

    Private ReadOnly _body As New Font(Lang.UiFont, 10.5F)
    Private ReadOnly _bold As New Font(Lang.UiFont, 10.5F, FontStyle.Bold)
    Private ReadOnly _small As New Font(Lang.UiFont, 9.25F)
    Private ReadOnly _smallBold As New Font(Lang.UiFont, 9.25F, FontStyle.Bold)
    Private ReadOnly _h1 As New Font(Lang.UiFont, 17.0F, FontStyle.Bold)
    Private ReadOnly _h2 As New Font(Lang.UiFont, 12.5F, FontStyle.Bold)
    Private ReadOnly _images As New Dictionary(Of String, Image)(StringComparer.OrdinalIgnoreCase)

    Private _section As ManualSection
    Private _hits As List(Of ManualHit)
    Private _query As String = ""
    Private _mark As Integer = -1
    ' 排版結果：每段的 Y 與高度（-1 = 頁首），以及可以點的區域
    Private ReadOnly _rows As New List(Of (Y As Integer, H As Integer))()
    Private ReadOnly _links As New List(Of (Rect As Rectangle, Section As ManualSection, Block As Integer))()
    Private _headerH As Integer
    Private _hover As Integer = -1

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or
                 ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        AutoScroll = True
        Tag = ThemeManager.SkipTag ' 顏色自己管
    End Sub

    '-------------------------------------------------------------- 顏色

    Private ReadOnly Property Dark As Boolean
        Get
            Return ThemeManager.Dark
        End Get
    End Property

    Private ReadOnly Property PageBack As Color
        Get
            Return If(Dark, Color.FromArgb(36, 39, 46), Color.White)
        End Get
    End Property

    Private ReadOnly Property TextColor As Color
        Get
            Return If(Dark, Color.FromArgb(222, 225, 232), Color.FromArgb(34, 38, 46))
        End Get
    End Property

    Private ReadOnly Property SubColor As Color
        Get
            Return If(Dark, Color.FromArgb(150, 156, 168), Color.FromArgb(108, 114, 126))
        End Get
    End Property

    Private ReadOnly Property LinkColor As Color
        Get
            Return If(Dark, Color.FromArgb(120, 175, 255), Color.FromArgb(24, 100, 210))
        End Get
    End Property

    Private ReadOnly Property NoteBack As Color
        Get
            Return If(Dark, Color.FromArgb(44, 56, 74), Color.FromArgb(234, 243, 255))
        End Get
    End Property

    Private ReadOnly Property MarkBack As Color
        Get
            Return If(Dark, Color.FromArgb(74, 66, 32), Color.FromArgb(255, 246, 200))
        End Get
    End Property

    Private ReadOnly Property LineColor As Color
        Get
            Return If(Dark, Color.FromArgb(62, 67, 78), Color.FromArgb(226, 229, 235))
        End Get
    End Property

    '-------------------------------------------------------------- 對外

    Public ReadOnly Property Section As ManualSection
        Get
            Return _section
        End Get
    End Property

    ''' <summary>顯示章節；mark ≥ 0 時把那段標出來並捲到那裡。</summary>
    Public Sub ShowSection(s As ManualSection, Optional mark As Integer = -1)
        _section = s
        _hits = Nothing
        _mark = mark
        Relayout()
        Dim y = 0
        If mark >= 0 AndAlso mark < _rows.Count Then y = Math.Max(0, _rows(mark).Y - 16)
        AutoScrollPosition = New Point(0, y)
        Invalidate()
    End Sub

    ''' <summary>顯示搜尋結果。</summary>
    Public Sub ShowResults(query As String, hits As List(Of ManualHit))
        _query = query
        _hits = hits
        _mark = -1
        Relayout()
        AutoScrollPosition = Point.Empty
        Invalidate()
    End Sub

    Public ReadOnly Property ShowingResults As Boolean
        Get
            Return _hits IsNot Nothing
        End Get
    End Property

    ''' <summary>測試用：可以點的連結數。</summary>
    Friend ReadOnly Property LinkCount As Integer
        Get
            Return _links.Count
        End Get
    End Property

    ''' <summary>測試用：模擬點第 i 個連結。</summary>
    Friend Sub ClickLink(i As Integer)
        Dim l = _links(i)
        RaiseEvent Navigate(l.Section, l.Block)
    End Sub

    ''' <summary>內容總高度（測試用）。</summary>
    Friend ReadOnly Property ContentHeight As Integer
        Get
            Return AutoScrollMinSize.Height
        End Get
    End Property

    '-------------------------------------------------------------- 排版

    Private ReadOnly Property ContentWidth As Integer
        Get
            Return Math.Max(200, Math.Min(MaxTextWidth, Width - SystemInformation.VerticalScrollBarWidth - PageMargin * 2))
        End Get
    End Property

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If _section IsNot Nothing OrElse _hits IsNot Nothing Then Relayout()
    End Sub

    Private Sub Relayout()
        _rows.Clear()
        _links.Clear()
        Dim w = ContentWidth
        Dim y = PageMargin
        Using g = CreateGraphics()
            y += DrawHeader(g, PageMargin, y, w, False)
            _headerH = y
            If _hits IsNot Nothing Then
                For i = 0 To _hits.Count - 1
                    Dim h = DrawHit(g, i, PageMargin, y, w, False)
                    _rows.Add((y, h))
                    _links.Add((New Rectangle(PageMargin - 8, y - 4, w + 16, h + 4), _hits(i).Section, _hits(i).BlockIndex))
                    y += h + 6
                Next
            ElseIf _section IsNot Nothing Then
                For i = 0 To _section.Blocks.Count - 1
                    Dim b = _section.Blocks(i)
                    y += Before(b)
                    Dim h = DrawBlock(g, b, PageMargin, y, w, False)
                    _rows.Add((y, h))
                    y += h
                Next
            End If
        End Using
        AutoScrollMinSize = New Size(0, y + PageMargin * 2)
    End Sub

    ''' <summary>段落上方的間距。</summary>
    Private Shared Function Before(b As ManualBlock) As Integer
        Select Case b.Kind
            Case ManualBlockKind.Heading : Return 18
            Case ManualBlockKind.HelpItem : Return 8
            Case Else : Return 10
        End Select
    End Function

    '-------------------------------------------------------------- 繪製（draw = False 只量高度）

    Private Function TextH(g As Graphics, text As String, font As Font, w As Integer) As Integer
        If String.IsNullOrEmpty(text) Then Return 0
        Return TextRenderer.MeasureText(g, text, font, New Size(w, Integer.MaxValue), Flags).Height
    End Function

    Private Function PutText(g As Graphics, draw As Boolean, text As String, font As Font, x As Integer, y As Integer, w As Integer, color As Color) As Integer
        Dim h = TextH(g, text, font, w)
        If draw AndAlso h > 0 Then TextRenderer.DrawText(g, text, font, New Rectangle(x, y, w, h + 2), color, Flags)
        Return h
    End Function

    Private Function DrawHeader(g As Graphics, x As Integer, y As Integer, w As Integer, draw As Boolean) As Integer
        Dim y0 = y
        If _hits IsNot Nothing Then
            y += PutText(g, draw, Lang.F("搜尋「{0}」", _query), _h1, x, y, w, TextColor) + 4
            y += PutText(g, draw, If(_hits.Count = 0, Lang.T("找不到符合的內容。可以換個說法，或用空白分開多個關鍵字。"),
                                     Lang.F("共 {0} 筆，點一下跳到那一段。", _hits.Count)), _small, x, y, w, SubColor) + 14
        ElseIf _section IsNot Nothing Then
            If _section.Parent IsNot Nothing Then y += PutText(g, draw, _section.Parent.Path, _small, x, y, w, SubColor) + 2
            y += PutText(g, draw, _section.Title, _h1, x, y, w, TextColor) + 8
            If draw Then
                Using p As New Pen(LineColor)
                    g.DrawLine(p, x, y, x + w, y)
                End Using
            End If
            y += 4
        End If
        Return y - y0
    End Function

    Private Function DrawHit(g As Graphics, i As Integer, x As Integer, y As Integer, w As Integer, draw As Boolean) As Integer
        Dim h = _hits(i)
        Dim y0 = y
        If draw AndAlso i = _hover Then
            Using br As New SolidBrush(NoteBack)
                g.FillRectangle(br, x - 8, y - 4, w + 16, RowHeight(i) + 4)
            End Using
        End If
        y += PutMarked(g, draw, h.Title, _bold, x, y, w, LinkColor) + 2
        If h.Snippet <> "" Then y += PutMarked(g, draw, h.Snippet, _small, x, y, w, SubColor)
        Return y - y0 + 4
    End Function

    Private Function RowHeight(i As Integer) As Integer
        Return If(i >= 0 AndAlso i < _rows.Count, _rows(i).H, 0)
    End Function

    ''' <summary>一行文字（太長加…），關鍵字底下塗黃。</summary>
    Private Function PutMarked(g As Graphics, draw As Boolean, text As String, font As Font, x As Integer, y As Integer, w As Integer, color As Color) As Integer
        Const F As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or TextFormatFlags.PreserveGraphicsTranslateTransform
        Dim h = TextRenderer.MeasureText(g, "國", font, New Size(w, 100), F).Height
        If Not draw Then Return h
        ' 寬度不夠時從後面截掉
        Dim s = text
        Do While s.Length > 1 AndAlso TextRenderer.MeasureText(g, s, font, New Size(Integer.MaxValue, 100), F).Width > w
            s = s.Substring(0, s.Length - 2) & "…"
        Loop
        Using br As New SolidBrush(If(Dark, Color.FromArgb(120, 100, 30), Color.FromArgb(255, 226, 110)))
            For Each t In ManualSearch.Terms(_query)
                Dim start = 0
                Do
                    Dim k = s.IndexOf(t, start, StringComparison.OrdinalIgnoreCase)
                    If k < 0 Then Exit Do
                    Dim x0 = If(k = 0, 0, TextRenderer.MeasureText(g, s.Substring(0, k), font, New Size(Integer.MaxValue, 100), F).Width)
                    Dim x1 = TextRenderer.MeasureText(g, s.Substring(0, k + t.Length), font, New Size(Integer.MaxValue, 100), F).Width
                    g.FillRectangle(br, x + x0, y, x1 - x0, h)
                    start = k + t.Length
                Loop
            Next
        End Using
        TextRenderer.DrawText(g, s, font, New Rectangle(x, y, w + 4, h + 2), color, F)
        Return h
    End Function

    Private Function DrawBlock(g As Graphics, b As ManualBlock, x As Integer, y As Integer, w As Integer, draw As Boolean) As Integer
        Select Case b.Kind
            Case ManualBlockKind.Heading
                Return PutText(g, draw, b.Text, _h2, x, y, w, TextColor) + 2
            Case ManualBlockKind.Paragraph
                Return PutText(g, draw, b.Text, _body, x, y, w, TextColor)
            Case ManualBlockKind.Note
                Dim h = TextH(g, b.Text, _body, w - 28) + 16
                If draw Then
                    Using br As New SolidBrush(NoteBack), p As New Pen(LinkColor, 3)
                        g.FillRectangle(br, x, y, w, h)
                        g.DrawLine(p, x + 1, y, x + 1, y + h - 1)
                    End Using
                End If
                PutText(g, draw, b.Text, _body, x + 16, y + 8, w - 28, TextColor)
                Return h
            Case ManualBlockKind.Bullets, ManualBlockKind.Steps
                Dim y0 = y
                For i = 0 To b.Items.Count - 1
                    Dim mark = If(b.Kind = ManualBlockKind.Steps, (i + 1) & ".", "•")
                    If draw Then TextRenderer.DrawText(g, mark, If(b.Kind = ManualBlockKind.Steps, _bold, _body), New Point(x + 4, y), If(b.Kind = ManualBlockKind.Steps, LinkColor, SubColor), TextFormatFlags.NoPadding Or TextFormatFlags.PreserveGraphicsTranslateTransform)
                    y += PutText(g, draw, b.Items(i), _body, x + 26, y, w - 26, TextColor) + 4
                Next
                Return y - y0
            Case ManualBlockKind.HelpItem
                Return DrawHelpItem(g, b, x, y, w, draw)
            Case ManualBlockKind.ImagePair
                Return DrawPair(g, b, x, y, w, draw)
            Case ManualBlockKind.Picture
                Return DrawPicture(g, b, x, y, w, draw)
            Case ManualBlockKind.Gallery
                Return DrawGallery(g, b, x, y, w, draw)
            Case ManualBlockKind.Links
                Dim y0 = y
                For Each s In b.LinkTargets
                    Dim intro = s.Blocks.FirstOrDefault(Function(k) k.Kind = ManualBlockKind.Paragraph)
                    Dim th = PutText(g, draw, "› " & s.Title, _bold, x, y, w, LinkColor)
                    If Not draw Then _links.Add((New Rectangle(x - 4, y - 2, w + 8, th + 4), s, -1))
                    y += th + 2
                    If intro IsNot Nothing Then y += PutText(g, draw, intro.Text, _small, x + 14, y, w - 14, SubColor)
                    y += 8
                Next
                Return y - y0
        End Select
        Return 0
    End Function

    Private Function DrawHelpItem(g As Graphics, b As ManualBlock, x As Integer, y As Integer, w As Integer, draw As Boolean) As Integer
        Dim hasIcon = b.Icon IsNot Nothing OrElse b.Glyph <> ""
        Dim tx = x + If(hasIcon, 40, 0), tw = w - (tx - x)
        If draw AndAlso hasIcon Then
            Dim r As New Rectangle(x, y + 1, 30, 30)
            If b.Icon IsNot Nothing Then
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                Dim s = Math.Min(30.0F / b.Icon.Width, 30.0F / b.Icon.Height)
                Dim iw = b.Icon.Width * s, ih = b.Icon.Height * s
                g.DrawImage(b.Icon, x + (30 - iw) / 2, y + 1 + (30 - ih) / 2, iw, ih)
            Else
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(r, 7), br As New SolidBrush(Color.FromArgb(70, 130, 220))
                    g.FillPath(br, path)
                End Using
                TextRenderer.DrawText(g, b.Glyph, _bold, r, Color.White, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.PreserveGraphicsTranslateTransform)
            End If
        End If
        Dim y0 = y
        y += PutText(g, draw, b.Title, _bold, tx, y, tw, TextColor) + 2
        y += PutText(g, draw, b.Text, _body, tx, y, tw, TextColor)
        If b.Hint <> "" Then y += 3 + PutText(g, draw, b.Hint, _small, tx, y + 3, tw, SubColor)
        Return Math.Max(y - y0, If(hasIcon, 32, 0))
    End Function

    Private Function DrawPair(g As Graphics, b As ManualBlock, x As Integer, y As Integer, w As Integer, draw As Boolean) As Integer
        Const Gap As Integer = 14, MaxH As Integer = 340
        Dim colW = (w - Gap) \ 2
        Dim imgs = {LoadImage(b.Before), LoadImage(b.After)}
        ' 兩張用同一個縮放後的高度（左右對齊）
        Dim h = 60
        If imgs(0) IsNot Nothing AndAlso imgs(1) IsNot Nothing Then
            h = CInt(Math.Min(MaxH, Math.Min(imgs(0).Height * colW / imgs(0).Width, imgs(1).Height * colW / imgs(1).Width)))
        End If
        Dim labelH = TextH(g, "前", _smallBold, colW) + 4
        If draw Then
            Dim labels = {Lang.T("套用前"), Lang.T("套用後")}
            For i = 0 To 1
                Dim cx = x + i * (colW + Gap)
                TextRenderer.DrawText(g, labels(i), _smallBold, New Point(cx, y), If(i = 1, LinkColor, SubColor), TextFormatFlags.NoPadding Or TextFormatFlags.PreserveGraphicsTranslateTransform)
                Dim img = imgs(i)
                Dim area As New Rectangle(cx, y + labelH, colW, h)
                If img Is Nothing Then
                    Using p As New Pen(LineColor)
                        g.DrawRectangle(p, area)
                    End Using
                    TextRenderer.DrawText(g, Lang.T("（找不到範例圖）"), _small, area, SubColor, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.PreserveGraphicsTranslateTransform)
                Else
                    Dim s = Math.Min(colW / img.Width, h / img.Height)
                    Dim iw = CInt(img.Width * s), ih = CInt(img.Height * s)
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    g.DrawImage(img, cx + (colW - iw) \ 2, y + labelH + (h - ih) \ 2, iw, ih)
                End If
            Next
        End If
        Dim capH = If(b.Text = "", 0, 6 + TextH(g, b.Text, _small, w))
        If draw AndAlso b.Text <> "" Then PutText(g, True, b.Text, _small, x, y + labelH + h + 6, w, SubColor)
        Return labelH + h + capH
    End Function

    Private Function DrawPicture(g As Graphics, b As ManualBlock, x As Integer, y As Integer, w As Integer, draw As Boolean) As Integer
        Const MaxH As Integer = 520
        Dim img = LoadImage(b.Before)
        Dim iw = w, ih = 80
        If img IsNot Nothing Then
            Dim s = Math.Min(Math.Min(1.0, w / CDbl(img.Width)), MaxH / CDbl(img.Height))
            iw = CInt(img.Width * s) : ih = CInt(img.Height * s)
        End If
        If draw Then
            If img Is Nothing Then
                Using p As New Pen(LineColor)
                    g.DrawRectangle(p, x, y, w, ih)
                End Using
                TextRenderer.DrawText(g, Lang.T("（找不到範例圖）"), _small, New Rectangle(x, y, w, ih), SubColor,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.PreserveGraphicsTranslateTransform)
            Else
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.DrawImage(img, x, y, iw, ih)
                Using p As New Pen(LineColor)
                    g.DrawRectangle(p, x, y, iw - 1, ih - 1)
                End Using
            End If
        End If
        Dim capH = If(b.Text = "", 0, 6 + TextH(g, b.Text, _small, w))
        If draw AndAlso b.Text <> "" Then PutText(g, True, b.Text, _small, x, y + ih + 6, w, SubColor)
        Return ih + capH
    End Function

    Private Function DrawGallery(g As Graphics, b As ManualBlock, x As Integer, y As Integer, w As Integer, draw As Boolean) As Integer
        Const Gap As Integer = 12
        Dim tileW = Math.Max(b.TileImageSize.Width + 8, 168)
        Dim perRow = Math.Max(1, (w + Gap) \ (tileW + Gap))
        tileW = (w - Gap * (perRow - 1)) \ perRow
        Dim y0 = y
        For start = 0 To b.Tiles.Count - 1 Step perRow
            Dim rowH = 0
            For k = start To Math.Min(b.Tiles.Count, start + perRow) - 1
                Dim t = b.Tiles(k)
                Dim tx = x + (k - start) * (tileW + Gap)
                Dim th = b.TileImageSize.Height + 6
                If draw AndAlso t.Image IsNot Nothing Then
                    Dim s = Math.Min(1.0F, Math.Min(tileW / CSng(t.Image.Width), b.TileImageSize.Height / CSng(t.Image.Height)))
                    Dim iw = CInt(t.Image.Width * s), ih = CInt(t.Image.Height * s)
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    g.DrawImage(t.Image, tx, y + (b.TileImageSize.Height - ih) \ 2, iw, ih)
                End If
                th += PutText(g, draw, t.Title, _bold, tx, y + th, tileW, TextColor) + 2
                th += PutText(g, draw, t.Text, _small, tx, y + th, tileW, SubColor)
                rowH = Math.Max(rowH, th)
            Next
            y += rowH + Gap + 4
        Next
        Return y - y0
    End Function

    Private Function LoadImage(name As String) As Image
        If String.IsNullOrEmpty(name) Then Return Nothing
        Dim img As Image = Nothing
        If _images.TryGetValue(name, img) Then Return img
        Dim file = IO.Path.Combine(ImageFolder, name)
        If IO.File.Exists(file) Then
            Try
                ' 讀進記憶體再關檔，不鎖住圖檔
                Using fs As New IO.FileStream(file, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite), src = Image.FromStream(fs)
                    img = New Bitmap(src)
                End Using
            Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException
                img = Nothing
            End Try
        End If
        _images(name) = img
        Return img
    End Function

    Private Shared Function RoundRect(r As Rectangle, radius As Integer) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim d = radius * 2
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    '-------------------------------------------------------------- 畫面

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(PageBack)
        If _section Is Nothing AndAlso _hits Is Nothing Then Return
        Dim oy = AutoScrollPosition.Y
        g.TranslateTransform(0, oy)
        Dim top = -oy, bottom = -oy + ClientSize.Height
        Dim w = ContentWidth
        DrawHeader(g, PageMargin, PageMargin, w, True)
        For i = 0 To _rows.Count - 1
            Dim r = _rows(i)
            If r.Y + r.H < top - 20 OrElse r.Y > bottom + 20 Then Continue For
            If _hits IsNot Nothing Then
                DrawHit(g, i, PageMargin, r.Y, w, True)
            Else
                If i = _mark Then
                    Using br As New SolidBrush(MarkBack)
                        g.FillRectangle(br, PageMargin - 10, r.Y - 6, w + 20, r.H + 12)
                    End Using
                End If
                DrawBlock(g, _section.Blocks(i), PageMargin, r.Y, w, True)
            End If
        Next
    End Sub

    Protected Overrides Sub OnScroll(se As ScrollEventArgs)
        MyBase.OnScroll(se)
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        Invalidate()
    End Sub

    Private Function LinkAt(p As Point) As Integer
        Dim q As New Point(p.X, p.Y - AutoScrollPosition.Y)
        For i = 0 To _links.Count - 1
            If _links(i).Rect.Contains(q) Then Return i
        Next
        Return -1
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim i = LinkAt(e.Location)
        Cursor = If(i >= 0, Cursors.Hand, Cursors.Default)
        If _hits IsNot Nothing AndAlso i <> _hover Then
            _hover = i
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hover >= 0 Then _hover = -1 : Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
    End Sub

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        MyBase.OnMouseClick(e)
        If e.Button <> MouseButtons.Left Then Return
        Dim i = LinkAt(e.Location)
        If i >= 0 Then
            _hover = -1
            ClickLink(i)
        End If
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            For Each f In {_body, _bold, _small, _smallBold, _h1, _h2}
                f.Dispose()
            Next
            For Each img In _images.Values
                img?.Dispose()
            Next
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
