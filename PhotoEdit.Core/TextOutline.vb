Imports System.Drawing
Imports System.Drawing.Drawing2D

''' <summary>
''' 文字轉外框（同 CorelDRAW 的「轉換為曲線」、Illustrator 的「建立外框」）：
''' 用和畫文字一樣的排版（字級太大時縮小到放得進框）取得字形的外框，每個字變成一個鋼筆路徑（填文字色、沒有線條）。
''' 圖說的框另外轉成鋼筆路徑。
''' </summary>
Partial Public NotInheritable Class DrawGeometry

    ''' <summary>排版用的倍率：照片高度 1 單位 = 1000 像素（GDI+ 量字用）。</summary>
    Private Const OutlineScale As Single = 1000

    ''' <summary>
    ''' 文字或圖說轉成鋼筆路徑：圖說的框（第一個，若有）加上每個字的外框（由前到後）。沒有可轉的內容時回傳空清單。
    ''' </summary>
    Public Shared Function TextOutlines(layer As DrawLayer) As List(Of DrawLayer)
        Dim result As New List(Of DrawLayer)()
        If Not HasText(layer.Shape) Then Return result
        If IsCallout(layer.Shape) Then
            Dim bubble = ToPath(layer)
            If bubble IsNot Nothing Then result.Add(bubble)
        End If
        If String.IsNullOrWhiteSpace(layer.Text) Then Return result
        Dim box = TextBox(layer)
        Dim rect = New RectangleF(box.X * OutlineScale, box.Y * OutlineScale, box.Width * OutlineScale, box.Height * OutlineScale)
        If rect.Width < 2 OrElse rect.Height < 2 Then Return result
        Dim style = If(layer.TextBold, FontStyle.Bold, FontStyle.Regular)
        Using bmp As New Bitmap(8, 8), g = Graphics.FromImage(bmp),
              sf As New StringFormat() With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
            g.TextRenderingHint = Text.TextRenderingHint.AntiAlias
            ' 字級：和 DrawText 一樣，太大時每次縮 8% 直到放得進框
            Dim size = CSng(Math.Max(0.004, layer.TextSize) * OutlineScale)
            Dim minSize = size * 0.4F
            Dim family = FamilyOf(layer.FontName)
            While size > minSize
                Using f As New Font(family, Math.Max(1, size), style, GraphicsUnit.Pixel)
                    Dim m = g.MeasureString(layer.Text, f, New SizeF(rect.Width, 100000), sf)
                    If m.Height <= rect.Height AndAlso m.Width <= rect.Width + 1 Then Exit While
                End Using
                size *= 0.92F
            End While
            Using path As New GraphicsPath(FillMode.Winding), font As New Font(family, Math.Max(1, size), style, GraphicsUnit.Pixel)
                path.AddString(layer.Text, family, CInt(style), Math.Max(1, size), rect, sf)
                ' 每個字的範圍（一次最多 32 個）
                Dim charRects As New List(Of (Index As Integer, Rect As RectangleF))()
                Dim visible = Enumerable.Range(0, layer.Text.Length).Where(Function(i) Not Char.IsWhiteSpace(layer.Text(i))).ToList()
                For start = 0 To visible.Count - 1 Step 32
                    Dim batch = visible.Skip(start).Take(32).ToList()
                    sf.SetMeasurableCharacterRanges(batch.Select(Function(i) New CharacterRange(i, 1)).ToArray())
                    Dim regions = g.MeasureCharacterRanges(layer.Text, font, rect, sf)
                    For k = 0 To regions.Length - 1
                        charRects.Add((batch(k), regions(k).GetBounds(g)))
                        regions(k).Dispose()
                    Next
                Next
                ' 外形線依所在的字分組
                Dim byChar As New SortedDictionary(Of Integer, List(Of PathContour))()
                For Each c In PathContours(path)
                    Dim xs = c.Nodes.Select(Function(n) n.X), ys = c.Nodes.Select(Function(n) n.Y)
                    Dim cx = (xs.Min() + xs.Max()) / 2, cy = (ys.Min() + ys.Max()) / 2
                    Dim owner = -1, best = Single.MaxValue
                    For Each cr In charRects
                        Dim dx = Math.Max(0, Math.Max(cr.Rect.Left - cx, cx - cr.Rect.Right))
                        Dim dy = Math.Max(0, Math.Max(cr.Rect.Top - cy, cy - cr.Rect.Bottom))
                        Dim d = CSng(dx + dy)
                        If d < best Then best = d : owner = cr.Index
                    Next
                    If Not byChar.ContainsKey(owner) Then byChar(owner) = New List(Of PathContour)()
                    byChar(owner).Add(c)
                Next
                For Each kv In byChar
                    Dim d = layer.Clone()
                    d.Shape = DrawShape.Path
                    d.Brush = BrushKind.HardRound
                    d.Filled = True
                    d.FillColorArgb = layer.TextColorArgb
                    d.Stroked = False
                    d.Symmetry = SymmetryKind.None
                    d.Text = ""
                    d.Name = If(kv.Key >= 0 AndAlso kv.Key < layer.Text.Length, layer.Text(kv.Key).ToString(), "文字")
                    d.Id = Nothing
                    ' 外框座標：量字時的像素 → 以圖層中心為原點的單位 → 旋轉、移到圖層位置
                    For Each c In kv.Value
                        For Each n In c.Nodes
                            n.Transform(Function(p) LocalToWorld(layer, New PointF(p.X / OutlineScale, p.Y / OutlineScale)))
                        Next
                    Next
                    d.Contours = kv.Value
                    d.Rotation = 0
                    result.Add(d)
                Next
            End Using
        End Using
        Return result
    End Function

    Private Shared Function FamilyOf(name As String) As FontFamily
        Try
            Return New FontFamily(name)
        Catch ex As ArgumentException
            Return FontFamily.GenericSansSerif
        End Try
    End Function

    ''' <summary>GraphicsPath 的每個子路徑轉成鋼筆路徑（直線＝尖角節點，貝茲曲線保留把手）。</summary>
    Private Shared Function PathContours(path As GraphicsPath) As List(Of PathContour)
        Dim list As New List(Of PathContour)()
        If path.PointCount = 0 Then Return list
        Dim pts = path.PathPoints, types = path.PathTypes
        Dim cur As PathContour = Nothing
        Dim i = 0
        While i < pts.Length
            Dim t = types(i) And &H7
            Dim closeHere = (types(i) And &H80) <> 0
            If t = 0 Then
                If cur IsNot Nothing AndAlso cur.Nodes.Count >= 2 Then list.Add(cur)
                cur = New PathContour()
                cur.Nodes.Add(New PathNode(pts(i).X, pts(i).Y))
                i += 1
            ElseIf t = 1 Then
                cur.Nodes.Add(New PathNode(pts(i).X, pts(i).Y))
                i += 1
            ElseIf t = 3 AndAlso i + 2 < pts.Length Then
                Dim prev = cur.Nodes(cur.Nodes.Count - 1)
                prev.OutX = pts(i).X : prev.OutY = pts(i).Y
                Dim n As New PathNode(pts(i + 2).X, pts(i + 2).Y) With {.InX = pts(i + 1).X, .InY = pts(i + 1).Y}
                cur.Nodes.Add(n)
                closeHere = (types(i + 2) And &H80) <> 0
                i += 3
            Else
                i += 1
            End If
            If closeHere AndAlso cur IsNot Nothing Then
                cur.Closed = True
                ' 最後一點和起點重疊：合併（保留它的 In 把手）
                If cur.Nodes.Count >= 2 Then
                    Dim a = cur.Nodes(0), z = cur.Nodes(cur.Nodes.Count - 1)
                    If Math.Abs(a.X - z.X) < 0.01 AndAlso Math.Abs(a.Y - z.Y) < 0.01 Then
                        a.InX = z.InX : a.InY = z.InY
                        cur.Nodes.RemoveAt(cur.Nodes.Count - 1)
                    End If
                End If
                If cur.Nodes.Count >= 2 Then list.Add(cur)
                cur = Nothing
            End If
        End While
        If cur IsNot Nothing AndAlso cur.Nodes.Count >= 2 Then list.Add(cur)
        Return list
    End Function
End Class
