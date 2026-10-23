Imports PhotoEdit

' CorelDRAW 的「結合」（Ctrl+L）與「打散」（Ctrl+K）：見 PhotoEdit.Core\CombineBreak.vb。
' 在繪圖頁籤選好圖形時，Ctrl+L／Ctrl+K 做結合／打散；不能結合或打散時照舊是向左旋轉／裁切。
Partial Class frmEditor

    ''' <summary>可以結合的選取（由下而上，兩個以上、沒鎖定、都轉得成曲線）；不能時回傳 Nothing。</summary>
    Private Function CurveCombineSelection() As List(Of Integer)
        Dim idx = SelectedIndices()
        If idx.Count < 2 Then Return Nothing
        If Not idx.All(Function(i) Not _recipe.Drawings(i).Locked AndAlso CombineBreak.CanCombine(_recipe.Drawings(i))) Then Return Nothing
        Return idx.OrderBy(Function(i) i).ToList()
    End Function

    ''' <summary>可以打散的選取（只選一個、沒鎖定、有兩條以上的路徑）；不能時回傳 -1。</summary>
    Private Function BreakApartCandidate() As Integer
        Dim idx = SelectedIndices()
        If idx.Count <> 1 Then Return -1
        Dim d = _recipe.Drawings(idx(0))
        Return If(Not d.Locked AndAlso CombineBreak.CanBreakApart(d), idx(0), -1)
    End Function

    Private Sub CombineCurves()
        Dim idx = CurveCombineSelection()
        If idx Is Nothing Then
            SetStatusMessage(Lang.T("結合要選兩個以上的向量圖形（文字、圖說、點陣圖層、填滿圖層不行；文字先轉成曲線）。"))
            Return
        End If
        Dim result = CombineBreak.Combine(idx.Select(Function(i) _recipe.Drawings(i)).ToList())
        If result Is Nothing Then Return
        Dim keep = idx.Min() ' 最下面的圖形：位置、樣式、所屬群組都沿用
        ApplyChange(Sub(r)
                        r.Drawings(keep) = result
                        For Each i In idx.Where(Function(x) x <> keep).OrderByDescending(Function(x) x)
                            r.Drawings.RemoveAt(i)
                        Next
                        DrawGroups.Prune(r, EnteredGroup())
                    End Sub)
        SelectDrawLayer(_recipe.Drawings.IndexOf(result))
        SetStatusMessage(Lang.F("已結合 {0} 個圖形：重疊的地方變成洞；Ctrl+K 打散可以再拆開。", idx.Count))
    End Sub

    Private Sub BreakApartSelected()
        Dim i = BreakApartCandidate()
        If i < 0 Then
            SetStatusMessage(Lang.T("打散要選一個有兩條以上路徑的圖形（例如結合過的圖形、轉成曲線的字）。"))
            Return
        End If
        Dim original = _recipe.Drawings(i)
        Dim parts = CombineBreak.BreakApart(original)
        If parts Is Nothing Then Return
        ApplyChange(Sub(r)
                        r.Drawings(i) = parts(0)
                        Dim below = parts(0)
                        For k = 1 To parts.Count - 1
                            r.Drawings.Add(parts(k))
                            LayerStack.PlaceAbove(r, parts(k), below)
                            below = parts(k)
                        Next
                    End Sub)
        Dim newIdx = parts.Select(Function(p) _recipe.Drawings.IndexOf(p)).Where(Function(x) x >= 0).ToList()
        SetMultiSelection(newIdx, newIdx.Max())
        SetStatusMessage(Lang.F("已打散成 {0} 個圖形。", parts.Count))
    End Sub

    ''' <summary>繪圖頁籤的 Ctrl+L／Ctrl+K：能結合／打散時才攔下來，否則讓給向左旋轉／裁切。</summary>
    Private Function CombineKey(keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Control Or Keys.L
                If CurveCombineSelection() Is Nothing Then Return False
                CombineCurves()
            Case Keys.Control Or Keys.K
                If BreakApartCandidate() < 0 Then Return False
                BreakApartSelected()
            Case Else
                Return False
        End Select
        UpdateDrawControls()
        _canvas.Invalidate()
        Return True
    End Function
End Class
