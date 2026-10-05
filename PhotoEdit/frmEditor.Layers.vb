Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 「圖層」分頁：文字、貼圖與繪圖圖層在同一張清單（上面的蓋住下面的），可以任意調整上下順序、
''' 設定混合模式與不透明度、顯示／隱藏、合併向下、合併可見圖層。按兩下（或「編輯」）跳到該圖層的分頁編輯。
''' 照片本身與調色不是圖層，永遠在最底下。
''' </summary>
Partial Friend Class frmEditor

    Private Const TabLayers As Integer = 10

    Private ReadOnly _stackList As New ListBox()
    Private ReadOnly _stackBlend As New ComboBox()
    Private ReadOnly _stackButtons As New List(Of Button)()
    Private _stackTop As Integer
    ''' <summary>選取的圖層在堆疊中的位置（由下而上）；-1 表示沒有選取。</summary>
    Private _stackIndex As Integer = -1
    ''' <summary>切換到「圖層」分頁之前的分頁，用來帶入那邊選取的圖層。</summary>
    Private _lastTab As Integer

    Private Sub BuildLayersPage(page As Aqua.TabPage)
        Dim L = NewLayout(page, autoScroll:=False)
        AddHint(L, "文字、貼圖與繪圖圖層都在這裡，上面的蓋住下面的。點眼睛顯示／隱藏、按兩下到該分頁編輯。")

        _help.SetHelpLinked("layers.blend", _stackBlend, AddCaption(L, "混合模式", L.Y), _stackBlend)
        _stackBlend.DropDownStyle = ComboBoxStyle.DropDownList
        _stackBlend.Items.AddRange(LayerBlend.Names.Cast(Of Object)().ToArray())
        _stackBlend.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _stackBlend.SelectedIndexChanged, Sub()
                                                         If _syncing OrElse _stackBlend.SelectedIndex < 0 Then Return
                                                         Dim mode = CType(_stackBlend.SelectedIndex, BlendMode)
                                                         ApplyChange(Sub(r)
                                                                         Dim s = StackSel(r)
                                                                         If s IsNot Nothing Then s.Blend = mode
                                                                     End Sub)
                                                     End Sub
        L.Add(_stackBlend)
        L.Y += RowHeight
        AddRow(L, MakeRow("ly_opacity", "不透明度", 0, 100, Function(v) v & "%",
                          Function(r) If(StackSel(r)?.Opacity, 100),
                          Sub(r, v)
                              Dim s = StackSel(r)
                              If s IsNot Nothing Then s.Opacity = v
                          End Sub))

        Dim names = {("移到最上", "layers.top"), ("上移", "layers.up"), ("下移", "layers.down"), ("移到最下", "layers.bottom"),
                     ("合併向下", "layers.mergedown"), ("合併可見", "layers.mergevisible"), ("編輯", "layers.edit"), ("刪除", "layers.delete")}
        Dim bw = (L.Width - 8 - 3 * 4) \ 4
        For i = 0 To names.Length - 1
            Dim b = MakeButton(names(i).Item1, names(i).Item2)
            b.SetBounds(8 + (i Mod 4) * (bw + 4), L.Y + 2 + (i \ 4) * 32, bw, 28)
            Dim index = i
            AddHandler b.Click, Sub() StackCommand(index)
            _stackButtons.Add(b)
            L.Add(b)
        Next
        L.Y += 70

        _stackTop = L.Y
        _stackList.SetBounds(8, L.Y, L.Width - 8, 220)
        _stackList.DrawMode = DrawMode.OwnerDrawFixed
        _stackList.ItemHeight = 30
        _stackList.IntegralHeight = False
        _stackList.BorderStyle = BorderStyle.FixedSingle
        AddHandler _stackList.DrawItem, AddressOf StackList_DrawItem
        AddHandler _stackList.MouseDown, AddressOf StackList_MouseDown
        AddHandler _stackList.SelectedIndexChanged, Sub()
                                                         If _syncing OrElse _stackList.SelectedIndex < 0 Then Return
                                                         SelectStack(StackCount() - 1 - _stackList.SelectedIndex)
                                                     End Sub
        AddHandler _stackList.DoubleClick, Sub() EditStackLayer()
        _help.SetHelp("layers.list", _stackList)
        L.Add(_stackList)
        AddHandler page.Resize, Sub() _stackList.Height = Math.Max(120, page.ClientSize.Height - _stackTop - 8)
    End Sub

    Private Function StackCount() As Integer
        Return If(_recipe.Overlays?.Count, 0) + If(_recipe.Drawings?.Count, 0)
    End Function

    ''' <summary>配方 r 裡目前選取的圖層（依堆疊位置）。</summary>
    Private Function StackSel(r As EditRecipe) As LayerRef
        If _stackIndex < 0 Then Return Nothing
        Dim o = LayerStack.Order(r)
        Return If(_stackIndex < o.Count, o(_stackIndex), Nothing)
    End Function

    ''' <summary>選取堆疊中的圖層，並同步各分頁的選取（文字貼圖 _overlayIndex、繪圖 _drawIndex）。</summary>
    Private Sub SelectStack(position As Integer)
        Dim o = LayerStack.Order(_recipe)
        _stackIndex = If(position >= 0 AndAlso position < o.Count, position, -1)
        Dim s = StackSel(_recipe)
        If s IsNot Nothing Then
            If s.IsOverlay Then
                _overlayIndex = _recipe.Overlays.IndexOf(s.Overlay)
            Else
                _drawIndex = _recipe.Drawings.IndexOf(s.Drawing)
            End If
        End If
        SyncSliders()
        UpdateCreativeControls()
        UpdateDrawControls()
        UpdateLayersPanel()
        _canvas.Invalidate()
    End Sub

    ''' <summary>從別的分頁切過來時，選取那邊正在編輯的圖層。</summary>
    Private Sub SyncStackSelectionFromTab(fromTab As Integer)
        Dim item As Object = Nothing
        Select Case fromTab
            Case TabText, TabSticker, TabDecor : item = SelOverlay(_recipe)
            Case TabDraw : item = SelDraw(_recipe)
        End Select
        If item IsNot Nothing Then _stackIndex = LayerStack.PositionOf(_recipe, item)
        SelectStack(_stackIndex)
    End Sub

    ''' <summary>配方或選取改變後更新「圖層」分頁。</summary>
    Private Sub UpdateLayersPanel()
        If _stackButtons.Count = 0 OrElse _stackList.IsDisposed Then Return ' 分頁還沒建好（啟動中）
        Dim n = StackCount()
        If _stackIndex >= n Then _stackIndex = n - 1
        Dim wasSyncing = _syncing
        _syncing = True
        Try
            If _stackList.Items.Count <> n Then
                _stackList.Items.Clear()
                For i = 0 To n - 1
                    _stackList.Items.Add(i)
                Next
            End If
            _stackList.SelectedIndex = If(_stackIndex < 0, -1, n - 1 - _stackIndex)
            _stackList.Invalidate()
            Dim s = StackSel(_recipe)
            _stackBlend.Enabled = s IsNot Nothing
            _stackBlend.SelectedIndex = If(s Is Nothing, -1, CInt(s.Blend))
            For Each row In _rows.Where(Function(r) r.Key = "ly_opacity")
                row.Slider.Enabled = s IsNot Nothing
            Next
            Dim has = s IsNot Nothing
            _stackButtons(0).Enabled = has AndAlso _stackIndex < n - 1
            _stackButtons(1).Enabled = has AndAlso _stackIndex < n - 1
            _stackButtons(2).Enabled = has AndAlso _stackIndex > 0
            _stackButtons(3).Enabled = has AndAlso _stackIndex > 0
            _stackButtons(4).Enabled = has AndAlso _stackIndex > 0
            _stackButtons(5).Enabled = n >= 2
            _stackButtons(6).Enabled = has
            _stackButtons(7).Enabled = has
        Finally
            _syncing = wasSyncing
        End Try
    End Sub

    Private Sub StackList_DrawItem(sender As Object, e As DrawItemEventArgs)
        Dim o = LayerStack.Order(_recipe)
        If e.Index < 0 OrElse e.Index >= o.Count Then Return
        Dim position = o.Count - 1 - e.Index
        Dim s = o(position)
        Dim g = e.Graphics
        Dim r = e.Bounds
        Using bg As New SolidBrush(If(position = _stackIndex, Color.FromArgb(210, 228, 250), Color.White))
            g.FillRectangle(bg, r)
        End Using
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim fg = If(s.Visible, Color.FromArgb(40, 44, 52), Color.FromArgb(160, 165, 172))
        DrawEye(g, New RectangleF(r.X + 4, r.Y + 7, 18, 16), s.Visible)
        If Not s.IsOverlay Then DrawLock(g, New RectangleF(r.X + 26, r.Y + 7, 16, 16), s.Locked)
        Dim icon = New RectangleF(r.X + 48, r.Y + 5, 20, 20)
        If s.IsOverlay Then
            Dim glyph = If(s.Overlay.Kind = OverlayKind.Text, "T", "★")
            Using f As New Font(_stackList.Font, FontStyle.Bold)
                TextRenderer.DrawText(g, glyph, f, Rectangle.Round(icon), If(s.Overlay.Kind = OverlayKind.Text, Color.FromArgb(40, 90, 170), Color.FromArgb(220, 120, 20)),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Using
        Else
            DrawIcons.DrawTool(g, CInt(s.Drawing.Shape), icon, fg)
        End If
        Dim right = If(s.Blend = BlendMode.Normal, "", LayerBlend.Name(s.Blend) & " ") & s.Opacity & "%"
        Dim rightWidth = TextRenderer.MeasureText(right, _stackList.Font).Width + 6
        TextRenderer.DrawText(g, s.DisplayName, _stackList.Font, New Rectangle(r.X + 74, r.Y, r.Width - 74 - rightWidth, r.Height), fg,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        TextRenderer.DrawText(g, right, _stackList.Font, New Rectangle(r.Right - rightWidth, r.Y, rightWidth - 4, r.Height), Color.FromArgb(130, 136, 146),
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.Right Or TextFormatFlags.NoPrefix)
        Using line As New Pen(Color.FromArgb(232, 235, 240))
            g.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1)
        End Using
    End Sub

    ''' <summary>點眼睛切換顯示、點鎖頭切換鎖定（只有繪圖圖層能鎖定）。</summary>
    Private Sub StackList_MouseDown(sender As Object, e As MouseEventArgs)
        Dim row = _stackList.IndexFromPoint(e.Location)
        Dim n = StackCount()
        If row < 0 OrElse row >= n OrElse e.X > 46 Then Return
        Dim position = n - 1 - row
        If e.X < 24 Then
            ApplyChange(Sub(r)
                            Dim s = LayerStack.Order(r)(position)
                            s.Visible = Not s.Visible
                        End Sub)
        Else
            ApplyChange(Sub(r)
                            Dim s = LayerStack.Order(r)(position)
                            If Not s.IsOverlay Then s.Drawing.Locked = Not s.Drawing.Locked
                        End Sub)
        End If
        _canvas.Invalidate()
    End Sub

    ''' <summary>0 移到最上、1 上移、2 下移、3 移到最下、4 合併向下、5 合併可見、6 編輯、7 刪除。</summary>
    Private Sub StackCommand(command As Integer)
        If _photo Is Nothing Then Return
        Dim n = StackCount()
        Dim from = _stackIndex
        Select Case command
            Case 0, 1, 2, 3
                If from < 0 Then Return
                Dim target = Select4(command, from, n)
                If target = from Then Return
                ApplyChange(Sub(r) LayerStack.Move(r, from, target))
                SelectStack(target)
            Case 4
                Dim reason As String = Nothing
                Dim merged As DrawLayer = Nothing
                Dim aspect = PhotoAspect()
                Dim test = _recipe.Clone()
                If LayerStack.MergeDown(test, from, aspect, reason) Is Nothing Then
                    SetStatusMessage(reason) : Return
                End If
                ApplyChange(Sub(r) merged = LayerStack.MergeDown(r, from, aspect, reason))
                SelectStack(LayerStack.PositionOf(_recipe, merged))
                SetStatusMessage("已合併成點陣圖層「" & merged.Name & "」；可以整體移動、直接繪製或擦除，按復原可以還原。")
            Case 5
                Dim reason As String = Nothing
                Dim merged As DrawLayer = Nothing
                Dim aspect = PhotoAspect()
                Dim test = _recipe.Clone()
                If LayerStack.MergeVisible(test, aspect, reason) Is Nothing Then
                    SetStatusMessage(reason) : Return
                End If
                ApplyChange(Sub(r) merged = LayerStack.MergeVisible(r, aspect, reason))
                SelectStack(LayerStack.PositionOf(_recipe, merged))
                SetStatusMessage("已把所有顯示中的圖層合併成「合併圖層」；隱藏的圖層保留不動，按復原可以還原。")
            Case 6 : EditStackLayer()
            Case 7 : DeleteStackLayer()
        End Select
    End Sub

    Private Shared Function Select4(command As Integer, from As Integer, n As Integer) As Integer
        Select Case command
            Case 0 : Return n - 1
            Case 1 : Return Math.Min(n - 1, from + 1)
            Case 2 : Return Math.Max(0, from - 1)
            Case Else : Return 0
        End Select
    End Function

    ''' <summary>跳到圖層所屬的分頁並選取它：文字 → 文字、內建貼圖 → 裝飾、圖片貼圖 → 貼圖、繪圖 → 繪圖。</summary>
    Private Sub EditStackLayer()
        Dim s = StackSel(_recipe)
        If s Is Nothing Then Return
        If s.IsOverlay Then
            _overlayIndex = _recipe.Overlays.IndexOf(s.Overlay)
            _tabs.SelectedIndex = If(s.Overlay.Kind = OverlayKind.Text, TabText, If(s.Overlay.Kind = OverlayKind.Image, TabSticker, TabDecor))
        Else
            _tabs.SelectedIndex = TabDraw
            SelectDrawLayer(_recipe.Drawings.IndexOf(s.Drawing))
        End If
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolFromTab()
    End Sub

    Private Sub DeleteStackLayer()
        Dim s = StackSel(_recipe)
        If s Is Nothing Then Return
        CommitCalloutEditor()
        Dim position = _stackIndex
        ApplyChange(Sub(r)
                        Dim t = LayerStack.Order(r)(position)
                        If t.IsOverlay Then r.Overlays.Remove(t.Overlay) Else r.Drawings.Remove(t.Drawing)
                    End Sub)
        _overlayIndex = -1
        _drawIndex = -1
        SelectStack(Math.Min(position, StackCount() - 1))
    End Sub
End Class
