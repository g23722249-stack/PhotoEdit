''' <summary>使用手冊的一個段落種類。</summary>
Friend Enum ManualBlockKind
    Heading
    Paragraph
    Bullets
    Steps
    Note
    ''' <summary>一則說明：圖示、標題、內容、小字提示（多半取自滑鼠停留說明 HelpTexts）。</summary>
    HelpItem
    ''' <summary>套用前／套用後兩張範例圖（manual\ 資料夾）。</summary>
    ImagePair
    ''' <summary>一張圖（manual\ 資料夾的 Before 檔名）加說明。</summary>
    Picture
    ''' <summary>小圖格：工具圖示、筆觸、紙紋。</summary>
    Gallery
    ''' <summary>子章節的連結清單。</summary>
    Links
End Enum

''' <summary>小圖格的一格。</summary>
Friend Class ManualTile
    Public Image As Image
    Public Title As String = ""
    Public Text As String = ""
End Class

''' <summary>手冊內容的一段。</summary>
Friend Class ManualBlock
    Public Kind As ManualBlockKind
    ''' <summary>標題、段落、附註的文字；範例圖的說明。</summary>
    Public Text As String = ""
    ''' <summary>條列、步驟的每一項。</summary>
    Public Items As New List(Of String)()
    ''' <summary>說明項目的標題、提示、圖示。</summary>
    Public Title As String = ""
    Public Hint As String = ""
    Public Icon As Image
    Public Glyph As String = ""
    ''' <summary>範例圖的檔名（manual\ 資料夾內）。</summary>
    Public Before As String = ""
    Public After As String = ""
    Public Tiles As New List(Of ManualTile)()
    ''' <summary>小圖格每格的圖片大小。</summary>
    Public TileImageSize As Size = New Size(64, 64)
    Public LinkTargets As New List(Of ManualSection)()

    ''' <summary>搜尋用的全部文字。</summary>
    Public Function SearchText() As String
        Dim parts As New List(Of String) From {Title, Text, Hint}
        parts.AddRange(Items)
        For Each t In Tiles
            parts.Add(t.Title) : parts.Add(t.Text)
        Next
        Return String.Join(" ", parts.Where(Function(s) Not String.IsNullOrEmpty(s)))
    End Function

    Public Shared Function Heading(text As String) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.Heading, .Text = text}
    End Function

    Public Shared Function Para(text As String) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.Paragraph, .Text = text}
    End Function

    Public Shared Function Note(text As String) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.Note, .Text = text}
    End Function

    Public Shared Function Bullets(ParamArray items As String()) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.Bullets, .Items = items.ToList()}
    End Function

    Public Shared Function Steps(ParamArray items As String()) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.Steps, .Items = items.ToList()}
    End Function

    Public Shared Function Pair(name As String, caption As String) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.ImagePair, .Before = name & "_before.jpg", .After = name & "_after.jpg", .Text = caption}
    End Function

    Public Shared Function Picture(file As String, caption As String) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.Picture, .Before = file, .Text = caption}
    End Function

    Public Shared Function Help(e As HelpTip.Entry) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.HelpItem, .Title = e.Title, .Text = e.Text, .Hint = e.Hint, .Icon = e.Icon, .Glyph = e.Glyph}
    End Function

    Public Shared Function Help(title As String, text As String, Optional hint As String = "", Optional icon As Image = Nothing) As ManualBlock
        Return New ManualBlock With {.Kind = ManualBlockKind.HelpItem, .Title = title, .Text = text, .Hint = hint, .Icon = icon}
    End Function
End Class

''' <summary>手冊的一個章節（目錄的一個節點）。</summary>
Friend Class ManualSection
    Public Title As String = ""
    Public Parent As ManualSection
    Public ReadOnly Blocks As New List(Of ManualBlock)()
    Public ReadOnly Children As New List(Of ManualSection)()

    Public Sub New(title As String)
        Me.Title = title
    End Sub

    Public Function AddChild(title As String) As ManualSection
        Dim s As New ManualSection(title) With {.Parent = Me}
        Children.Add(s)
        Return s
    End Function

    Public Function Add(b As ManualBlock) As ManualBlock
        Blocks.Add(b)
        Return b
    End Function

    ''' <summary>「選單 › 檔案」這樣的路徑。</summary>
    Public ReadOnly Property Path As String
        Get
            Return If(Parent Is Nothing, Title, Parent.Path & " › " & Title)
        End Get
    End Property

    ''' <summary>自己和所有子章節（目錄順序）。</summary>
    Public Iterator Function All() As IEnumerable(Of ManualSection)
        Yield Me
        For Each c In Children
            For Each s In c.All()
                Yield s
            Next
        Next
    End Function
End Class

''' <summary>一筆搜尋結果。</summary>
Friend Class ManualHit
    Public Section As ManualSection
    ''' <summary>符合的段落；-1 表示章節標題符合。</summary>
    Public BlockIndex As Integer
    Public Title As String = ""
    Public Snippet As String = ""
End Class

Friend Module ManualSearch
    ''' <summary>
    ''' 搜尋：以空白分隔多個關鍵字，全部出現在同一段才算符合（不分大小寫）。
    ''' 章節標題符合的排前面，其次是說明項目的標題符合，再來是內文。
    ''' </summary>
    Public Function Find(sections As IEnumerable(Of ManualSection), query As String, Optional limit As Integer = 200) As List(Of ManualHit)
        Dim words = Terms(query)
        Dim hits As New List(Of (Rank As Integer, Order As Integer, Hit As ManualHit))()
        If words.Length = 0 Then Return New List(Of ManualHit)()
        Dim order = 0
        For Each root In sections
            For Each s In root.All()
                If MatchAll(s.Title, words) Then
                    hits.Add((0, order, New ManualHit With {.Section = s, .BlockIndex = -1, .Title = s.Path, .Snippet = FirstText(s)}))
                    order += 1
                End If
                For i = 0 To s.Blocks.Count - 1
                    Dim b = s.Blocks(i)
                    If b.Kind = ManualBlockKind.Links Then Continue For
                    Dim all = b.SearchText()
                    If Not MatchAll(all, words) Then Continue For
                    Dim titleHit = MatchAll(If(b.Kind = ManualBlockKind.Heading, b.Text, b.Title), words)
                    Dim title = If(b.Title <> "", b.Title, If(b.Kind = ManualBlockKind.Heading, b.Text, ""))
                    hits.Add((If(titleHit, 1, 2), order, New ManualHit With {
                        .Section = s, .BlockIndex = i,
                        .Title = s.Path & If(title = "" OrElse title = s.Title, "", " › " & title),
                        .Snippet = Snippet(all, words(0))}))
                    order += 1
                Next
            Next
        Next
        Return hits.OrderBy(Function(h) h.Rank).ThenBy(Function(h) h.Order).Take(limit).Select(Function(h) h.Hit).ToList()
    End Function

    Public Function Terms(query As String) As String()
        Return If(query, "").Split({" "c, "　"c, ChrW(9)}, StringSplitOptions.RemoveEmptyEntries)
    End Function

    Private Function MatchAll(text As String, terms As String()) As Boolean
        If String.IsNullOrEmpty(text) Then Return False
        Return terms.All(Function(t) text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
    End Function

    Private Function FirstText(s As ManualSection) As String
        Dim b = s.Blocks.FirstOrDefault(Function(x) x.Kind = ManualBlockKind.Paragraph OrElse x.Kind = ManualBlockKind.HelpItem)
        Return If(b Is Nothing, "", Clip(b.SearchText(), 0, 70))
    End Function

    ''' <summary>關鍵字前後的一小段文字。</summary>
    Private Function Snippet(text As String, term As String) As String
        Dim i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase)
        Return Clip(text, Math.Max(0, i - 24), 70)
    End Function

    Private Function Clip(text As String, start As Integer, len As Integer) As String
        Dim s = text.Substring(start, Math.Min(len, text.Length - start)).Replace(vbCr, " ").Replace(vbLf, " ")
        Return If(start > 0, "…", "") & s & If(start + len < text.Length, "…", "")
    End Function
End Module
