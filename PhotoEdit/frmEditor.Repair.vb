Imports PhotoEdit

''' <summary>修補分頁的動作：修補筆刷、自動拉直；以及最大化時不蓋住工作列。</summary>
Partial Friend Class frmEditor

    Private Sub SetHealMode(enabled As Boolean)
        If enabled AndAlso _photo Is Nothing Then
            _healToggle.Checked = False
            Return
        End If
        If enabled Then
            ExitCropMode(apply:=True)
            If _tabs.SelectedIndex <> TabRepair Then _tabs.SelectedIndex = TabRepair
        End If
        UpdateToolFromTab()
        SetStatusMessage(If(enabled, "修補筆刷：在照片上塗抹要移除的污點或雜物，放開滑鼠就會補上。Esc 結束。", ""))
        If Not enabled Then UpdateStatus()
    End Sub

    ''' <summary>畫布上完成一筆塗抹：修補筆刷存成污點，局部筆刷加進選取的局部調整。</summary>
    Private Sub OnCanvasStroke(points As List(Of PointF), screenRadius As Single)
        If _photo Is Nothing OrElse points.Count = 0 OrElse _showingOriginal Then Return
        If _canvas.Tool = PreviewCanvas.CanvasTool.LocalBrush Then
            OnLocalStroke(points, screenRadius)
            Return
        End If
        If _canvas.Tool = PreviewCanvas.CanvasTool.MaskBrush Then
            OnMaskStroke(points, screenRadius)
            Return
        End If
        Dim stroke = MakeSourceStroke(points, screenRadius)
        ApplyChange(Sub(r)
                        If r.Spots Is Nothing Then r.Spots = New List(Of SpotStroke)()
                        r.Spots.Add(stroke)
                    End Sub)
        SetStatusMessage($"已修補 {_recipe.Spots.Count} 處。Ctrl+Z 可復原，「清除全部修補」可全部移除。")
    End Sub

    ''' <summary>
    ''' 畫面（已裁切）上的筆觸換成已轉正原圖座標；筆刷半徑從螢幕像素換成「原圖長邊」的比例。
    ''' </summary>
    Private Function MakeSourceStroke(points As List(Of PointF), screenRadius As Single) As SpotStroke
        Dim pw = _previewBase.Width, ph = _previewBase.Height
        Dim stroke As New SpotStroke()
        For Each p In points
            stroke.AddPoint(GeometryMapper.UnmapPoint(DisplayToPhoto(p), _recipe, pw, ph, fromCropped:=True))
        Next
        Dim originalPixels = screenRadius / _canvas.EffectiveZoom()
        stroke.Radius = originalPixels / Math.Max(_photo.Image.Width, _photo.Image.Height) /
                        GeometryMapper.ScaleFactor(_recipe, pw, ph)
        Return stroke
    End Function

    ''' <summary>分析「已旋轉/透視、尚未拉直與裁切」的畫面，設定建議的拉直角度。</summary>
    Private Sub RunAutoStraighten()
        Dim g = _recipe.Clone()
        g.Straighten = 0
        g.Crop = Nothing
        Dim angle As Double?
        Using img = ImagePipeline.RenderGeometry(_previewBase, g, 1000, applyCrop:=False)
            angle = AutoStraighten.Suggest(img)
        End Using
        If angle Is Nothing Then
            SetStatusMessage("找不到明顯的水平線或垂直線，請用「拉直」滑桿手動調整。")
            Return
        End If
        Dim value = Math.Max(-45, Math.Min(45, angle.Value))
        ApplyChange(Sub(r) r.Straighten = value)
        SetStatusMessage(If(Math.Abs(value) < 0.05, "畫面已經是水平的。", $"自動拉直：{value:+0.0;-0.0}°"))
    End Sub

    '---------------------------------------------------------------------
    ' 最大化：AquaForm 沒有系統邊框，最大化時預設會蓋住工作列，所以限制在目前螢幕的工作區。
    '---------------------------------------------------------------------

    Private Sub UpdateMaximizedBounds()
        Dim scr = Screen.FromControl(Me)
        ' MaximizedBounds 的位置是相對於該螢幕左上角。
        MaximizedBounds = New Rectangle(scr.WorkingArea.X - scr.Bounds.X, scr.WorkingArea.Y - scr.Bounds.Y,
                                        scr.WorkingArea.Width, scr.WorkingArea.Height)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        UpdateMaximizedBounds()
        MyBase.OnHandleCreated(e)
    End Sub

    Protected Overrides Sub OnLocationChanged(e As EventArgs)
        MyBase.OnLocationChanged(e)
        If WindowState = FormWindowState.Normal AndAlso IsHandleCreated Then UpdateMaximizedBounds()
    End Sub
End Class
