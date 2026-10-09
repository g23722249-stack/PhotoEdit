Imports PhotoEdit

''' <summary>
''' 填滿圖層（同 Photoshop 的「新增填滿圖層」，非破壞性）：單色、漸層或材質的整個圖層，之後在圖層清單按兩下可以再改；
''' 建立時有選取範圍就當遮色片，只填在範圍裡。不透明度與混合模式是圖層本身的設定。
''' </summary>
Partial Friend Class frmEditor

    ''' <summary>上次新增填滿圖層的選擇（這次開啟程式期間記得）。</summary>
    Private _lastFillLayer As FillChoice

    Private Shared ReadOnly FillLayerKindNames As String() = {"單色", "漸層", "材質"}

    ''' <summary>繪圖分頁圖層區的「填滿…」：開對話框，新增一個填滿圖層。</summary>
    Private Sub NewFillLayer()
        If _photo Is Nothing Then Return
        If _lastFillLayer Is Nothing Then
            _lastFillLayer = New FillChoice With {.ColorArgb = _drawStyle.FillColorArgb, .Color2Argb = _drawStyle.StrokeColorArgb,
                                                  .Gradient = GradientOptions().Kind, .Material = _drawStyle.Material}
        End If
        Dim c = _lastFillLayer.Clone()
        If c.Content = FillKind.ContentAware Then c.Content = FillKind.Color
        c.Name = ""
        Using dlg As New frmFillSelection(c, Nothing, _help, layerMode:=True)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            c = dlg.Choice
        End Using
        _lastFillLayer = c.Clone()
        Dim layer As New DrawLayer With {.Shape = DrawShape.FillLayer, .Seed = _drawRandom.Next(1, 100000), .Param1 = Math.Round(PhotoAspect(), 5)}
        If HasSelection Then layer.Region = _recipe.Selection.Clone() ' 遮色片：只填在選取範圍裡
        ApplyChoiceToLayer(c, layer)
        AddFillLayer(layer)
    End Sub

    ''' <summary>直接用選擇新增填滿圖層（不開對話框；測試用）。</summary>
    Private Sub AddFillLayer(layer As DrawLayer)
        AddDrawLayer(layer)
        SetStatusMessage($"已新增「{layer.Name}」：在圖層清單按兩下可以再改顏色、漸層或材質。")
    End Sub

    ''' <summary>在圖層清單按兩下填滿圖層：開對話框修改內容（遮色片、位置不變）。</summary>
    Private Sub EditFillLayer(index As Integer)
        If index < 0 OrElse index >= LayerCount() Then Return
        Dim layer = _recipe.Drawings(index)
        If layer.Shape <> DrawShape.FillLayer Then Return
        Dim c = ChoiceFromLayer(layer)
        Using dlg As New frmFillSelection(c, Nothing, _help, layerMode:=True)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            c = dlg.Choice
        End Using
        ApplyChange(Sub(r) ApplyChoiceToLayer(c, r.Drawings(index)))
        UpdateDrawControls()
        SetStatusMessage($"已修改「{_recipe.Drawings(index).Name}」。")
    End Sub

    ''' <summary>把填滿圖層目前的設定換成對話框的選擇。</summary>
    Private Function ChoiceFromLayer(layer As DrawLayer) As FillChoice
        Dim c As New FillChoice With {.ColorArgb = layer.FillColorArgb, .Color2Argb = layer.StrokeColorArgb, .Material = layer.Material,
                                      .Opacity = layer.Opacity, .Blend = layer.Blend, .Name = layer.Name, .Direction = CInt(layer.Param2)}
        If layer.Gradient IsNot Nothing Then
            c.Content = FillKind.Gradient
            c.Gradient = layer.Gradient.Kind
            c.GradientColors = layer.Gradient.Colors
        Else
            c.Content = If(layer.FillContent = FillContent.Material, FillKind.Material, FillKind.Color)
            c.Gradient = GradientOptions().Kind
        End If
        Return c
    End Function

    ''' <summary>依選擇設定填滿圖層：內容、顏色、漸層（起點終點依遮色片外框或整張）、材質、不透明度、混合模式、名稱。</summary>
    Private Sub ApplyChoiceToLayer(c As FillChoice, layer As DrawLayer)
        layer.FillColorArgb = c.ColorArgb
        layer.StrokeColorArgb = c.Color2Argb
        layer.Material = c.Material
        layer.Opacity = c.Opacity
        layer.Blend = c.Blend
        layer.Param2 = c.Direction
        layer.Filled = True : layer.Stroked = False
        If c.Content = FillKind.Gradient Then
            Dim aspect = If(layer.Param1 > 0, layer.Param1, PhotoAspect())
            Dim box As New RectangleF(0, 0, CSng(aspect), 1)
            If layer.Region IsNot Nothing Then
                Dim b = SelectionMask.Bounds(layer.Region, 1024, 1024)
                If Not b.IsEmpty Then box = New RectangleF(CSng(b.X * aspect), b.Y, CSng(b.Width * aspect), b.Height)
            End If
            Dim g = If(layer.Gradient, GradientOptions().Clone())
            g.Kind = c.Gradient
            g.Colors = c.GradientColors
            Dim ends = GradientEnds(box, c.Direction, c.Gradient)
            g.X1 = Math.Round(ends.A.X, 5) : g.Y1 = Math.Round(ends.A.Y, 5) : g.X2 = Math.Round(ends.B.X, 5) : g.Y2 = Math.Round(ends.B.Y, 5)
            layer.Gradient = g
            layer.FillContent = FillContent.Color
        Else
            layer.Gradient = Nothing
            layer.FillContent = If(c.Content = FillKind.Material, FillContent.Material, FillContent.Color)
        End If
        Dim kind = FillLayerKindNames(If(c.Content = FillKind.Gradient, 1, If(c.Content = FillKind.Material, 2, 0)))
        ' 沒有改過名字（自動命名）時跟著內容換名字
        layer.Name = If(String.IsNullOrWhiteSpace(c.Name) OrElse c.Name.StartsWith("填滿圖層（", StringComparison.Ordinal), $"填滿圖層（{kind}）", c.Name)
    End Sub
End Class
