Imports PhotoEdit

''' <summary>
''' 「效果」分頁的藝術風格區：選風格（依分類列出）、強度、線條、細節。
''' 下方濾鏡列的「繪畫」「漫畫／版畫」分類一次套用風格與適合的調色；這裡可以再細調。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _artStyle As New ComboBox()
    ''' <summary>選單每一項對應的風格（索引同 _artStyle.Items）。</summary>
    Private ReadOnly _artItems As New List(Of ArtStyle)()

    Private Sub BuildArtSection(L As PageLayout)
        L.Y += 6
        AddHeading(L, "藝術風格")
        _help.SetHelpLinked("art.style", _artStyle, AddCaption(L, "風格", L.Y), _artStyle)
        _artStyle.DropDownStyle = ComboBoxStyle.DropDownList
        _artStyle.MaxDropDownItems = 16
        _artItems.Add(ArtStyle.None)
        _artStyle.Items.Add("無")
        For Each cat In ArtStyles.Categories
            For Each st In cat.Styles
                _artItems.Add(st)
                _artStyle.Items.Add($"{cat.Name}｜{ArtStyles.Names(CInt(st))}")
            Next
        Next
        _artStyle.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _artStyle.SelectedIndexChanged, Sub()
                                                       If _syncing OrElse _artStyle.SelectedIndex < 0 OrElse _photo Is Nothing Then Return
                                                       Dim st = _artItems(_artStyle.SelectedIndex)
                                                       ApplyChange(Sub(r) r.ArtStyle = st)
                                                   End Sub
        L.Add(_artStyle)
        L.Y += RowHeight
        AddRow(L, MakeRow("artstrength", "強度", 0, 100, AddressOf Plain, Function(r) r.ArtStrength, Sub(r, v) r.ArtStrength = v))
        AddRow(L, MakeRow("artline", "線條", 0, 100, AddressOf Plain, Function(r) r.ArtLine, Sub(r, v) r.ArtLine = v))
        AddRow(L, MakeRow("artdetail", "筆觸大小", 0, 100, AddressOf Plain, Function(r) r.ArtDetail, Sub(r, v) r.ArtDetail = v))
        ' 宮崎風 AI 重繪：結果開成新影像（不是即時套用的風格）
        Dim redraw = MakeButton("宮崎風 AI 重繪…", "btn.airedraw")
        redraw.SetBounds(8, L.Y + 2, L.Width - 16, 30)
        AddHandler redraw.Click, Sub() RunCommand("airedraw")
        L.Add(redraw)
        L.Y += 40
        AddHint(L, "強度：和原圖混合的比例。線條：輪廓線粗細。筆觸大小：筆觸、網點、色塊的大小（越大越粗獷）。" & vbCrLf &
                   "風格會畫在色調之後，文字、貼圖與繪圖不會被風格化。")
    End Sub

    Private Sub SyncArtControls()
        If _artItems.Count = 0 Then Return
        Dim i = _artItems.IndexOf(_recipe.ArtStyle)
        If _artStyle.SelectedIndex <> Math.Max(0, i) Then _artStyle.SelectedIndex = Math.Max(0, i)
    End Sub
End Class
