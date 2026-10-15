Imports PhotoEdit

''' <summary>
''' 上方操作列放不下時（視窗窄、選了很多種東西）：比較少用的按鈕依序收進最右邊的「»」選單，全螢幕、完成永遠看得到。
''' 每次更新操作列（UpdateDrawControls）和操作列改變大小時重新排；選單裡點了就跟按原本的按鈕一樣。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _drawOverflow As New Button With {.Text = "»", .Dock = DockStyle.Right, .Width = 34, .Visible = False}
    Private ReadOnly _overflowMenu As New ContextMenuStrip()
    ''' <summary>因為放不下而藏起來的（每組第一個是放進選單的按鈕，其餘一起藏，例如調和的步數）。</summary>
    Private ReadOnly _overflowHidden As New List(Of Control())()
    Private _fitting As Boolean

    Private Sub BuildDrawOverflow()
        _drawOverflow.Font = New Font(_panelFont.FontFamily, 12)
        _help.SetHelp("draw.overflow", _drawOverflow)
        AddHandler _drawOverflow.Click, Sub() ShowOverflowMenu()
        AddHandler _drawBar.Resize, Sub() FitDrawBar()
    End Sub

    ''' <summary>可以收起來的按鈕，先收的排前面（每組第一個放進選單）。</summary>
    Private Function OverflowCandidates() As List(Of Control())
        Dim list As New List(Of Control())()
        Dim addButtons = Sub(dict As Dictionary(Of String, Button), keys As String())
                             For Each k In keys
                                 If dict.ContainsKey(k) Then list.Add({dict(k)})
                             Next
                         End Sub
        addButtons(_shapeButtons, {"exclude", "intersect", "trim", "weld"})
        addButtons(_shapeButtons, {"more", "top", "hcenter", "left"})
        addButtons(_shapeButtons, {"photoclip", "clipout", "clipin"})
        If _btnDetachText IsNot Nothing Then
            list.Add({_btnDetachText}) : list.Add({_btnFlipText})
        End If
        If _btnUngroup IsNot Nothing Then
            list.Add({_btnUngroup}) : list.Add({_btnGroup})
        End If
        addButtons(_nodeButtons, {"convert", "join", "close", "break", "delete", "symmetric", "smooth", "corner"})
        If _fxControls.Count > 0 Then
            list.Add({_fxControls("blend"), _fxControls("blendL"), _fxControls("blendSteps")})
            For Each k In {"envReset", "fadeOff", "shadowOff", "shadowColor"}
                list.Add({_fxControls(k)})
            Next
        End If
        ' 最後：屬性列的角度欄（輸入框不能放進選單，直接藏）
        If _propBoxes.ContainsKey("angle") Then
            Dim lbl = _propBar.Controls.OfType(Of Label)().FirstOrDefault(Function(l) l.Text = "∠")
            If lbl IsNot Nothing Then list.Add({lbl, _propBoxes("angle")})
        End If
        ' 還是放不下（非常窄）：整個屬性列也收起來
        list.Add({_propBar})
        Return list
    End Function

    ''' <summary>操作列需要的寬度（靠左的各區塊＋靠右的按鈕）。</summary>
    Private Function DrawBarNeeded() As Integer
        Dim w = 0
        For Each c As Control In _drawBar.Controls
            If Not c.Visible OrElse c.Dock = DockStyle.Fill Then Continue For
            If c.Dock = DockStyle.Left AndAlso TypeOf c Is FlowLayoutPanel Then
                w += c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal
            Else
                w += c.Width
            End If
        Next
        Return w
    End Function

    ''' <summary>放不下時依序收起按鈕，放得下時放回來。</summary>
    Private Sub FitDrawBar()
        If _fitting OrElse _shapeButtons.Count = 0 OrElse Not _drawBar.Visible Then Return
        _fitting = True
        Try
            RestoreOverflow()
            Dim available = _drawBar.ClientSize.Width - _drawBar.Padding.Horizontal - 4
            If DrawBarNeeded() <= available Then Return
            _drawOverflow.Visible = True
            For Each grp In OverflowCandidates()
                If DrawBarNeeded() <= available Then Exit For
                If Not grp(0).Visible Then Continue For
                For Each c In grp
                    c.Visible = False
                Next
                _overflowHidden.Add(grp)
            Next
            ' 收完之後從重要的（後收的）開始試著放回來：剩下的空間放得下就留著
            For k = _overflowHidden.Count - 1 To 0 Step -1
                Dim grp = _overflowHidden(k)
                For Each c In grp : c.Visible = True : Next
                If DrawBarNeeded() <= available Then
                    _overflowHidden.RemoveAt(k)
                Else
                    For Each c In grp : c.Visible = False : Next
                End If
            Next
            _drawOverflow.Visible = _overflowHidden.Any(Function(g) TypeOf g(0) Is Button)
        Finally
            _fitting = False
        End Try
    End Sub

    ''' <summary>把收起來的放回來（這些在收起來之前都是看得到的）。UpdateDrawControls 一開始也呼叫，之後各區塊再決定要不要顯示。</summary>
    Private Sub RestoreOverflow()
        For Each grp In _overflowHidden
            For Each c In grp
                c.Visible = True
            Next
        Next
        _overflowHidden.Clear()
        _drawOverflow.Visible = False
    End Sub

    ''' <summary>「»」選單：收起來的按鈕（點了就像按原本的按鈕）。</summary>
    Private Sub ShowOverflowMenu()
        _overflowMenu.Items.Clear()
        For Each grp In _overflowHidden
            Dim b = TryCast(grp(0), Button)
            If b Is Nothing Then Continue For
            Dim text = If(String.IsNullOrEmpty(b.Text), b.AccessibleName, b.Text)
            Dim target = b
            _overflowMenu.Items.Add(text, Nothing, Sub()
                                                       ' 調和：步數一起藏了，用上次的步數
                                                       InvokeOnClick(target, EventArgs.Empty)
                                                   End Sub)
        Next
        If _overflowMenu.Items.Count = 0 Then Return
        _overflowMenu.Show(_drawOverflow, New Point(0, _drawOverflow.Height))
    End Sub
End Class
