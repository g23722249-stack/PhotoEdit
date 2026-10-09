Imports PhotoEdit

''' <summary>
''' 繪圖分頁的「直接繪製」：像 Photoshop 的筆刷，畫在目前選取的點陣圖層上，畫完就是像素、不能再個別調整。
''' 沒有選圖層時自動新增點陣圖層；選到向量圖層（形狀、圖說、向量筆畫）時先問要點陣化還是新增點陣圖層。
''' 橡皮擦（E）用目前的筆刷形狀擦掉點陣圖層上畫過的地方。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _drawEraser As New CheckBox()
    ''' <summary>橡皮擦的擦法：擦掉、漂白、加深（同 Painter 的橡皮擦類，記在設定）。</summary>
    Private ReadOnly _eraseMode As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Left, .Width = 70}
    ''' <summary>這一筆是暫時的橡皮擦：按住右鍵畫，或用繪圖筆的橡皮擦端（筆倒過來）。</summary>
    Private _tempErase As Boolean

    ''' <summary>這一筆要擦：橡皮擦按鈕按下，或右鍵／筆的橡皮擦端。</summary>
    Private Function IsErasing() As Boolean
        Return _drawEraser.Checked OrElse _tempErase
    End Function

    Private Sub BuildEraseModeCombo()
        _eraseMode.Items.AddRange({"擦掉", "漂白", "加深"})
        _eraseMode.SelectedIndex = Math.Max(0, Math.Min(2, _appSettings.EraseMode))
        _eraseMode.Visible = False
        _help.SetHelp("draw.erasemode", _eraseMode)
        AddHandler _eraseMode.SelectedIndexChanged, Sub()
                                                        If _eraseMode.SelectedIndex < 0 OrElse _appSettings.EraseMode = _eraseMode.SelectedIndex Then Return
                                                        _appSettings.EraseMode = _eraseMode.SelectedIndex
                                                        _appSettings.Save()
                                                        UpdateDrawHint()
                                                    End Sub
    End Sub
    ''' <summary>拖曳中的這一筆是直接繪製（放開時加進點陣圖層），不是向量的自由繪製。</summary>
    Private _freeRaster As Boolean

    Private Sub BuildEraserToggle()
        _drawEraser.Appearance = Appearance.Button
        _drawEraser.Text = "橡皮擦 (E)"
        _drawEraser.TextAlign = ContentAlignment.MiddleCenter
        _drawEraser.Dock = DockStyle.Left
        _drawEraser.Width = 100
        _drawEraser.Visible = False
        _drawEraser.UseVisualStyleBackColor = True
        _help.SetHelp("draw.eraser", _drawEraser)
        AddHandler _drawEraser.CheckedChanged, Sub()
                                                   _drawEraser.BackColor = If(_drawEraser.Checked, Color.FromArgb(255, 214, 120), If(ThemeManager.Dark, ThemeManager.ButtonBack, SystemColors.Control))
                                                   _drawEraser.ForeColor = If(_drawEraser.Checked OrElse Not ThemeManager.Dark, SystemColors.ControlText, Aqua.Theme.TextColor)
                                                   UpdateDrawHint()
                                               End Sub
    End Sub

    Private Function NewRasterLayer() As DrawLayer
        Return New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer)(), .Name = "點陣圖層 " & (LayerCount() + 1)}
    End Function

    ''' <summary>直接繪製按下：決定畫在哪個圖層，必要時先問要不要點陣化。</summary>
    Private Sub BeginRasterStroke(e As MouseEventArgs, u As PointF)
        Dim sel = SelDraw(_recipe)
        If sel IsNot Nothing Then
            Dim name = If(String.IsNullOrEmpty(sel.Name), DrawGeometry.ShapeNames(CInt(sel.Shape)), sel.Name)
            If Not sel.Visible OrElse sel.Locked Then
                SetStatusMessage($"「{name}」{If(sel.Locked, "已鎖定", "已隱藏")}，不能在上面畫。")
                Return
            End If
            If sel.Shape <> DrawShape.Raster Then
                ' 等滑鼠按下的事件結束再問，畫布才不會以為按鈕還按著。
                Dim index = _drawIndex
                BeginInvoke(Sub() AskRasterize(index))
                Return
            End If
        ElseIf IsErasing() Then
            SetStatusMessage("先在圖層清單選一個點陣圖層，再用橡皮擦擦。")
            Return
        End If
        _dd = DrawDrag.Freehand
        _freeRaster = True
        StartFreehand(u, e.Location)
    End Sub

    ''' <summary>向量圖層不能直接畫：點陣化它，或在它上面新增一個點陣圖層。</summary>
    Private Sub AskRasterize(index As Integer)
        If index < 0 OrElse index >= LayerCount() Then Return
        Dim d = _recipe.Drawings(index)
        Dim name = If(String.IsNullOrEmpty(d.Name), DrawGeometry.ShapeNames(CInt(d.Shape)), d.Name)
        Dim rasterize As New TaskDialogButton("點陣化")
        Dim addLayer As New TaskDialogButton("新增點陣圖層")
        Dim page As New TaskDialogPage With {
            .Caption = AppName,
            .Heading = $"「{name}」是向量圖層",
            .Text = "直接繪製要畫在點陣圖層上。" & vbCrLf &
                    "點陣化：把這個圖層變成像素再畫，之後就不能再調整形狀、文字與筆刷。" & vbCrLf &
                    "新增點陣圖層：在它上面加一個空白圖層來畫，原本的圖層不變。",
            .Icon = TaskDialogIcon.Information,
            .DefaultButton = rasterize}
        page.Buttons.Add(rasterize)
        page.Buttons.Add(addLayer)
        page.Buttons.Add(TaskDialogButton.Cancel)
        Dim result = TaskDialog.ShowDialog(Me, page)
        If result Is rasterize Then
            ApplyChange(Sub(r) r.Drawings(index) = DrawGeometry.Rasterize(r.Drawings(index)))
            SelectDrawLayer(index)
            SetStatusMessage($"「{name}」已點陣化，可以直接畫上去了。")
        ElseIf result Is addLayer Then
            Dim layer = NewRasterLayer()
            ApplyChange(Sub(r)
                            r.Drawings.Insert(index + 1, layer)
                            LayerStack.PlaceAbove(r, layer, r.Drawings(index))
                        End Sub)
            SelectDrawLayer(index + 1)
            SetStatusMessage($"已在「{name}」上面新增「{layer.Name}」。")
        End If
    End Sub

    ''' <summary>直接繪製放開：這一筆（或橡皮擦）加進選取的點陣圖層；沒有選圖層時新增一個。</summary>
    Private Sub CommitRasterStroke(stroke As DrawStroke)
        _freeRaster = False
        Dim op As New DrawLayer With {.Shape = DrawShape.Freehand, .Seed = _drawRandom.Next(1, 100000), .Eraser = IsErasing(), .EraseMode = CType(Math.Max(0, Math.Min(2, _appSettings.EraseMode)), EraseMode)}
        _tempErase = False ' 右鍵、筆的橡皮擦端只擦這一筆
        op.CopyStyleFrom(_drawStyle)
        If op.Eraser Then op.Shadow = False
        If op.Brush = BrushKind.Clone AndAlso Not op.Eraser AndAlso stroke.Points.Count > 0 Then
            ' 對齊模式（同 Photoshop）：第一筆決定來源與筆畫的距離，之後每一筆都維持這個距離，直到重新 Alt 點選來源。
            If Not _cloneOffset.HasValue AndAlso _cloneSource.HasValue Then
                _cloneOffset = New PointF(_cloneSource.Value.X - stroke.Points(0).X, _cloneSource.Value.Y - stroke.Points(0).Y)
            End If
            If _cloneOffset.HasValue Then op.CloneDX = Math.Round(_cloneOffset.Value.X, 5) : op.CloneDY = Math.Round(_cloneOffset.Value.Y, 5)
        End If
        Dim sel = SelDraw(_recipe)
        If sel IsNot Nothing AndAlso sel.Shape = DrawShape.Raster Then
            ' 圖層移動過時，筆畫（與對稱中心）要扣掉圖層的位移。
            Dim dx = CSng(sel.X), dy = CSng(sel.Y)
            For Each p In stroke.Points
                p.X -= dx : p.Y -= dy
            Next
            op.SymX -= dx : op.SymY -= dy
            op.Strokes = New List(Of DrawStroke) From {stroke}
            Dim index = _drawIndex
            ApplyChange(Sub(r)
                            If r.Drawings(index).Ops Is Nothing Then r.Drawings(index).Ops = New List(Of DrawLayer)()
                            r.Drawings(index).Ops.Add(op)
                        End Sub)
            UpdateDrawControls()
        ElseIf Not op.Eraser Then
            op.Strokes = New List(Of DrawStroke) From {stroke}
            Dim layer = NewRasterLayer()
            layer.Ops.Add(op)
            AddDrawLayer(layer)
        End If
    End Sub
End Class
