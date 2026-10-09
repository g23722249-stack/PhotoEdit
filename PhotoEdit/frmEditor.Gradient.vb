Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 繪圖分頁的漸層工具：在畫布上拖一條線（Shift 鎖 45°），畫在選取的點陣圖層（沒選時新增）；
''' 有選取範圍時只畫在範圍裡。顏色用線條色 → 填色（或 → 透明、彩虹），四色漸層用操作列上的四個色塊。
''' 選項在畫布上方的操作列，記在 settings.json。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _gradientBar As New FlowLayoutPanel()
    Private ReadOnly _gradKind As New ComboBox()
    Private ReadOnly _gradColors As New ComboBox()
    Private ReadOnly _gradReverse As New CheckBox()
    Private ReadOnly _gradDither As New CheckBox()
    Private ReadOnly _gradColorsCaption As New Label()
    Private ReadOnly _gradCorners(3) As Button
    Private _syncingGradient As Boolean

    Private Shared ReadOnly GradientKindNames As String() = {"線性", "放射", "角度", "反射", "菱形", "四色"}
    Private Shared ReadOnly GradientColorNames As String() = {"線條色 → 填色", "線條色 → 透明", "彩虹"}

    ''' <summary>油漆桶或漸層（畫在點陣圖層上的填色工具）。</summary>
    Private Function IsFillTool() As Boolean
        Return _drawStrip.SelectedTool = CInt(DrawShape.Bucket) OrElse _drawStrip.SelectedTool = CInt(DrawShape.Gradient)
    End Function

    Private Function GradientOptions() As GradientFill
        If _appSettings.Gradient Is Nothing Then _appSettings.Gradient = New GradientFill()
        Return _appSettings.Gradient
    End Function

    Private Sub BuildGradientBar(done As Control)
        _gradientBar.Dock = DockStyle.Left
        _gradientBar.AutoSize = True
        _gradientBar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        _gradientBar.WrapContents = False
        _gradientBar.Padding = New Padding(6, 0, 0, 0)
        _gradientBar.BackColor = Color.Transparent
        _gradientBar.Visible = False
        Dim caption = Function(text As String) New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(5, 7, 1, 0), .BackColor = Color.Transparent}
        _gradKind.DropDownStyle = ComboBoxStyle.DropDownList
        _gradKind.Items.AddRange(GradientKindNames)
        _gradKind.Width = 64
        _gradColors.DropDownStyle = ComboBoxStyle.DropDownList
        _gradColors.Items.AddRange(GradientColorNames)
        _gradColors.Width = 120
        For Each c In {_gradKind, _gradColors}
            c.Margin = New Padding(0, 3, 0, 0)
        Next
        For Each cb In {_gradReverse, _gradDither}
            cb.AutoSize = True
            cb.Margin = New Padding(6, 6, 0, 0)
            cb.BackColor = Color.Transparent
        Next
        _gradReverse.Text = "反轉"
        _gradDither.Text = "防色階"
        _gradColorsCaption.Text = "顏色"
        _gradColorsCaption.AutoSize = True
        _gradColorsCaption.Margin = New Padding(5, 7, 1, 0)
        _gradColorsCaption.BackColor = Color.Transparent
        Dim kindCaption = caption("類型")
        _gradientBar.Controls.AddRange({kindCaption, _gradKind, _gradColorsCaption, _gradColors})
        ' 四色漸層的四個角（左上、右上、左下、右下）
        Dim cornerTips = {"左上", "右上", "左下", "右下"}
        For i = 0 To 3
            Dim k = i
            Dim b As New Button With {.Width = 24, .Height = 22, .Margin = New Padding(If(i = 0, 0, 2), 3, 0, 0), .FlatStyle = FlatStyle.Flat,
                                      .Tag = ThemeManager.SkipTag, .Visible = False, .AccessibleName = "四色漸層" & cornerTips(i)}
            b.FlatAppearance.BorderColor = Color.FromArgb(120, 126, 138)
            AddHandler b.Click, Sub() PickGradientCorner(k)
            _help.SetHelp("gradient.corner", b)
            _gradCorners(i) = b
            _gradientBar.Controls.Add(b)
        Next
        _gradientBar.Controls.AddRange({_gradReverse, _gradDither})
        _help.SetHelpLinked("gradient.kind", _gradKind, kindCaption, _gradKind)
        _help.SetHelpLinked("gradient.colors", _gradColors, _gradColorsCaption, _gradColors)
        _help.SetHelp("gradient.reverse", _gradReverse)
        _help.SetHelp("gradient.dither", _gradDither)

        SyncGradientBar()
        AddHandler _gradKind.SelectedIndexChanged, Sub()
                                                       SetGradientOption(Sub(g) g.Kind = CType(Math.Max(0, _gradKind.SelectedIndex), GradientKind))
                                                       UpdateGradientBarParts()
                                                   End Sub
        AddHandler _gradColors.SelectedIndexChanged, Sub() SetGradientOption(Sub(g) g.Colors = CType(Math.Max(0, _gradColors.SelectedIndex), GradientColors))
        AddHandler _gradReverse.CheckedChanged, Sub() SetGradientOption(Sub(g) g.Reverse = _gradReverse.Checked)
        AddHandler _gradDither.CheckedChanged, Sub() SetGradientOption(Sub(g) g.Dither = _gradDither.Checked)
        _drawBar.Controls.Add(_gradientBar)
        _drawBar.Controls.SetChildIndex(_gradientBar, _drawBar.Controls.GetChildIndex(done))
    End Sub

    Private Sub SyncGradientBar()
        Dim g = GradientOptions()
        _syncingGradient = True
        Try
            _gradKind.SelectedIndex = Math.Max(0, Math.Min(GradientKindNames.Length - 1, CInt(g.Kind)))
            _gradColors.SelectedIndex = Math.Max(0, Math.Min(GradientColorNames.Length - 1, CInt(g.Colors)))
            _gradReverse.Checked = g.Reverse
            _gradDither.Checked = g.Dither
        Finally
            _syncingGradient = False
        End Try
        UpdateGradientBarParts()
    End Sub

    ''' <summary>四色漸層顯示四個角的色塊，其他類型顯示顏色選單。</summary>
    Private Sub UpdateGradientBarParts()
        Dim g = GradientOptions()
        Dim four = g.Kind = GradientKind.FourColor
        _gradColors.Visible = Not four
        _gradColorsCaption.Text = If(four, "四角", "顏色")
        Dim corners = {g.Corner1, g.Corner2, g.Corner3, g.Corner4}
        For i = 0 To 3
            _gradCorners(i).Visible = four
            _gradCorners(i).BackColor = Color.FromArgb(255, Color.FromArgb(corners(i)))
        Next
    End Sub

    Private Sub SetGradientOption(change As Action(Of GradientFill))
        If _syncingGradient Then Return
        change(GradientOptions())
        _appSettings.Save()
    End Sub

    Private Sub PickGradientCorner(index As Integer)
        Dim g = GradientOptions()
        Dim corners = {g.Corner1, g.Corner2, g.Corner3, g.Corner4}
        Using dlg As New Aqua.ColorPickerDialog With {.Color = Color.FromArgb(corners(index))}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim argb = Color.FromArgb(255, dlg.Color).ToArgb()
            SetGradientOption(Sub(o)
                                  Select Case index
                                      Case 0 : o.Corner1 = argb
                                      Case 1 : o.Corner2 = argb
                                      Case 2 : o.Corner3 = argb
                                      Case Else : o.Corner4 = argb
                                  End Select
                              End Sub)
        End Using
        UpdateGradientBarParts()
    End Sub

    '---------------------------------------------------------------------
    ' 拖曳
    '---------------------------------------------------------------------

    Private Sub BeginGradientDrag(u As PointF)
        _dd = DrawDrag.GradientLine
        _ddStart = u
        _ddCur = u
    End Sub

    ''' <summary>Shift：角度鎖在 45° 的倍數（同 Photoshop）。</summary>
    Private Function GradientEnd(u As PointF) As PointF
        If Not ModifierKeys.HasFlag(Keys.Shift) Then Return u
        Dim dx = u.X - _ddStart.X, dy = u.Y - _ddStart.Y
        Dim len = Math.Sqrt(dx * dx + dy * dy)
        Dim a = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4)
        Return New PointF(CSng(_ddStart.X + Math.Cos(a) * len), CSng(_ddStart.Y + Math.Sin(a) * len))
    End Function

    Private Sub GradientDragMove(u As PointF)
        _ddCur = GradientEnd(u)
        _canvas.Invalidate()
    End Sub

    ''' <summary>放開：起點到終點夠長就畫上漸層。</summary>
    Private Sub EndGradientDrag()
        Dim a = _ddStart, b = _ddCur
        If ScreenDist(UnitToScreen(a), UnitToScreen(b)) < 4 Then
            SetStatusMessage("漸層：在照片上拖一條線，起點是第一個顏色、終點是第二個顏色。")
            Return
        End If
        Dim sel As DrawLayer = Nothing
        If Not PixelToolTarget("畫漸層", sel) Then Return
        Dim op As New DrawLayer With {.Shape = DrawShape.Gradient, .Seed = _drawRandom.Next(1, 100000), .Param1 = Math.Round(PhotoAspect(), 5)}
        op.CopyStyleFrom(_drawStyle)
        op.Shadow = False
        Dim g = GradientOptions().Clone()
        Dim dx = If(sel IsNot Nothing, sel.X, 0), dy = If(sel IsNot Nothing, sel.Y, 0)
        g.X1 = Math.Round(a.X - dx, 5) : g.Y1 = Math.Round(a.Y - dy, 5)
        g.X2 = Math.Round(b.X - dx, 5) : g.Y2 = Math.Round(b.Y - dy, 5)
        op.Gradient = g
        If HasSelection Then op.Region = _recipe.Selection.Clone() ' 有選取範圍：只畫在範圍裡
        AddPixelOp(op, sel)
        SetStatusMessage($"已畫上{GradientKindNames(CInt(g.Kind))}漸層{If(op.Region IsNot Nothing, "（只在選取範圍裡）", "")}。")
    End Sub

    ''' <summary>拖曳中：起點、終點與連線（四色漸層畫成方框）。</summary>
    Private Sub PaintGradientDrag(g As Graphics)
        If _dd <> DrawDrag.GradientLine Then Return
        Dim a = UnitToScreen(_ddStart), b = UnitToScreen(_ddCur)
        Using outer As New Pen(Color.FromArgb(160, 0, 0, 0), 3), inner As New Pen(Color.White, 1.4F)
            If GradientOptions().Kind = GradientKind.FourColor Then
                Dim r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))
                g.DrawRectangle(outer, r.X, r.Y, r.Width, r.Height)
                inner.DashStyle = DashStyle.Dash
                g.DrawRectangle(inner, r.X, r.Y, r.Width, r.Height)
            Else
                g.DrawLine(outer, a, b)
                g.DrawLine(inner, a, b)
            End If
        End Using
        For Each pt In {(a, _drawStyle.StrokeColorArgb), (b, _drawStyle.FillColorArgb)}
            Using br As New SolidBrush(Color.FromArgb(255, Color.FromArgb(pt.Item2)))
                g.FillEllipse(br, pt.Item1.X - 5, pt.Item1.Y - 5, 10, 10)
            End Using
            g.DrawEllipse(Pens.White, pt.Item1.X - 5, pt.Item1.Y - 5, 10, 10)
            g.DrawEllipse(Pens.Black, pt.Item1.X - 6, pt.Item1.Y - 6, 12, 12)
        Next
    End Sub

    '---------------------------------------------------------------------
    ' 油漆桶、漸層共用：畫在哪個點陣圖層
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 決定畫在哪裡：選取的點陣圖層（sel）；沒選圖層時 sel = Nothing（之後新增）。
    ''' 選到隱藏、鎖定或向量圖層時回傳 False（向量圖層會問要點陣化還是新增點陣圖層）。
    ''' </summary>
    Private Function PixelToolTarget(verb As String, ByRef sel As DrawLayer) As Boolean
        sel = SelDraw(_recipe)
        If sel Is Nothing Then Return True
        Dim name = If(String.IsNullOrEmpty(sel.Name), DrawGeometry.ShapeNames(CInt(sel.Shape)), sel.Name)
        If Not sel.Visible OrElse sel.Locked Then
            SetStatusMessage($"「{name}」{If(sel.Locked, "已鎖定", "已隱藏")}，不能在上面{verb}。")
            Return False
        End If
        If sel.Shape <> DrawShape.Raster Then
            Dim index = _drawIndex
            BeginInvoke(Sub() AskRasterize(index))
            Return False
        End If
        Return True
    End Function

    ''' <summary>把一筆操作加進選取的點陣圖層；沒有選圖層時新增一個點陣圖層。</summary>
    Private Sub AddPixelOp(op As DrawLayer, sel As DrawLayer)
        If sel IsNot Nothing Then
            Dim index = _drawIndex
            ApplyChange(Sub(r)
                            If r.Drawings(index).Ops Is Nothing Then r.Drawings(index).Ops = New List(Of DrawLayer)()
                            r.Drawings(index).Ops.Add(op)
                        End Sub)
            UpdateDrawControls()
        Else
            Dim layer = NewRasterLayer()
            layer.Ops.Add(op)
            AddDrawLayer(layer)
        End If
    End Sub
End Class
