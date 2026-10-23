Imports System.Drawing.Drawing2D
Imports PhotoEdit

' 段落文字框（同 CorelDRAW 的段落文字）：文字工具拖一個框就是段落文字（點一下是一般文字）。
' 選到文字時畫布上方出現「段落」「對齊」「行距」「欄」；文字放不下時框的下方標一個紅色的 ▼。排版見 PhotoEdit.Core\ParagraphText.vb。
Partial Class frmEditor

    Private ReadOnly _paraBar As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                           .WrapContents = False, .BackColor = Color.Transparent, .Padding = New Padding(6, 0, 0, 0)}
    Private ReadOnly _paraToggle As New CheckBox With {.Text = Lang.T("段落"), .Appearance = Appearance.Button, .AutoSize = True, .Height = 28,
                                                       .TextAlign = ContentAlignment.MiddleCenter, .Margin = New Padding(0, 0, 4, 0), .UseVisualStyleBackColor = True}
    Private ReadOnly _paraAlign As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 82, .Margin = New Padding(0, 3, 4, 0)}
    Private ReadOnly _paraSpacing As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 62, .Margin = New Padding(0, 3, 4, 0)}
    Private ReadOnly _paraColumns As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 44, .Margin = New Padding(0, 3, 4, 0)}
    Private ReadOnly _paraLabels As New List(Of Label)()
    Private Shared ReadOnly ParaSpacings As Integer() = {80, 100, 120, 150, 200}
    Private _paraSyncing As Boolean

    Private Function BuildParagraphBar() As Control
        _paraAlign.Items.AddRange({Lang.T("靠左"), Lang.T("置中"), Lang.T("靠右"), Lang.T("左右對齊")})
        _paraSpacing.Items.AddRange(ParaSpacings.Select(Function(v) CObj(v & "%")).ToArray())
        _paraColumns.Items.AddRange({"1", "2", "3", "4"})
        _help.SetHelp("draw.para.toggle", _paraToggle)
        _help.SetHelp("draw.para.align", _paraAlign)
        _help.SetHelp("draw.para.spacing", _paraSpacing)
        _help.SetHelp("draw.para.columns", _paraColumns)
        Dim lbl = Function(t As String)
                      Dim l As New Label With {.Text = t, .AutoSize = True, .Margin = New Padding(2, 7, 1, 0), .BackColor = Color.Transparent}
                      _paraLabels.Add(l)
                      Return l
                  End Function
        AddHandler _paraToggle.CheckedChanged, Sub()
                                                   If _paraSyncing Then Return
                                                   Dim isOn = _paraToggle.Checked
                                                   SetParagraph(Sub(d) d.Paragraph = isOn, allowPlain:=True)
                                               End Sub
        AddHandler _paraAlign.SelectedIndexChanged, Sub()
                                                        If _paraSyncing OrElse _paraAlign.SelectedIndex < 0 Then Return
                                                        Dim a = CType(_paraAlign.SelectedIndex, TextAlignKind)
                                                        SetParagraph(Sub(d) d.TextAlign = a)
                                                    End Sub
        AddHandler _paraSpacing.SelectedIndexChanged, Sub()
                                                          If _paraSyncing OrElse _paraSpacing.SelectedIndex < 0 Then Return
                                                          Dim v = ParaSpacings(_paraSpacing.SelectedIndex)
                                                          SetParagraph(Sub(d) d.LineSpacing = v)
                                                      End Sub
        AddHandler _paraColumns.SelectedIndexChanged, Sub()
                                                          If _paraSyncing OrElse _paraColumns.SelectedIndex < 0 Then Return
                                                          Dim v = _paraColumns.SelectedIndex + 1
                                                          SetParagraph(Sub(d) d.TextColumns = v)
                                                      End Sub
        _paraBar.Controls.AddRange({_paraToggle, _paraAlign, lbl(Lang.T("行距")), _paraSpacing, lbl(Lang.T("欄")), _paraColumns})
        _paraBar.Visible = False
        Return _paraBar
    End Function

    ''' <summary>目前選取的文字（不是路徑文字）；沒有時 -1。</summary>
    Private Function SelectedTextFrame() As Integer
        If IsMultiSelected() OrElse _builderOn Then Return -1
        Dim sel = SelDraw(_recipe)
        If sel Is Nothing OrElse sel.Shape <> DrawShape.Text OrElse DrawGeometry.IsPathText(sel) Then Return -1
        Return _drawIndex
    End Function

    Private Sub UpdateParagraphBar()
        Dim i = If(_drawStrip.SelectedTool = DrawIcons.SelectTool OrElse _drawStrip.SelectedTool = CInt(DrawShape.Text), SelectedTextFrame(), -1)
        _paraBar.Visible = i >= 0
        If i < 0 Then Return
        Dim d = _recipe.Drawings(i)
        _paraSyncing = True
        Try
            _paraToggle.Checked = d.Paragraph
            _paraAlign.SelectedIndex = Math.Max(0, Math.Min(3, CInt(d.TextAlign)))
            Dim si = Array.IndexOf(ParaSpacings, d.LineSpacing)
            If si < 0 Then si = 1
            _paraSpacing.SelectedIndex = si
            _paraColumns.SelectedIndex = Math.Max(0, Math.Min(3, d.TextColumns - 1))
        Finally
            _paraSyncing = False
        End Try
        For Each c As Control In {_paraAlign, _paraSpacing, _paraColumns}
            c.Visible = d.Paragraph
        Next
        For Each l In _paraLabels
            l.Visible = d.Paragraph
        Next
    End Sub

    ''' <summary>改選取的文字框；allowPlain：一般文字也可以（切換「段落」本身）。</summary>
    Private Sub SetParagraph(change As Action(Of DrawLayer), Optional allowPlain As Boolean = False)
        Dim i = SelectedTextFrame()
        If i < 0 Then Return
        If Not allowPlain AndAlso Not _recipe.Drawings(i).Paragraph Then Return
        ApplyChange(Sub(r) change(r.Drawings(i)))
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    ''' <summary>段落文字放不下：在框的下方中間畫紅色的 ▼（只在編輯器裡，不會匯出）。</summary>
    Private Sub PaintParagraphOverflow(g As Graphics)
        If _recipe?.Drawings Is Nothing Then Return
        For Each d In _recipe.Drawings
            If Not d.Visible OrElse d.Shape <> DrawShape.Text OrElse Not d.Paragraph OrElse DrawGeometry.IsPathText(d) Then Continue For
            If Not DrawGeometry.ParagraphOverflows(d) Then Continue For
            Dim rot = d.Rotation * Math.PI / 180
            Dim lx = 0.0, ly = Math.Abs(d.H) / 2
            Dim p = UnitToScreen(New PointF(CSng(d.X + lx * Math.Cos(rot) - ly * Math.Sin(rot)), CSng(d.Y + lx * Math.Sin(rot) + ly * Math.Cos(rot))))
            Dim r As New RectangleF(p.X - 8, p.Y - 2, 16, 14)
            Using br As New SolidBrush(Color.FromArgb(230, 210, 40, 40))
                g.FillRectangle(br, r)
                g.FillPolygon(Brushes.White, {New PointF(r.X + 4, r.Y + 4), New PointF(r.Right - 4, r.Y + 4), New PointF(r.X + 8, r.Bottom - 3)})
            End Using
        Next
    End Sub
End Class
