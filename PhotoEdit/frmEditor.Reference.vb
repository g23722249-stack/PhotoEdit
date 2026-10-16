Imports PhotoEdit

''' <summary>參考圖視窗（檢視 → 參考圖…）：吸色設成繪圖的線條色、描圖時把參考圖疊在畫布上。</summary>
Partial Friend Class frmEditor

    Private _reference As frmReference

    Private Sub ShowReference()
        EnsureReference()
        _reference.Show()
        _reference.Activate()
    End Sub

    ''' <summary>建立參考圖視窗並接好事件（還不顯示）。</summary>
    Private Function EnsureReference() As frmReference
        If _reference Is Nothing OrElse _reference.IsDisposed Then
            _reference = New frmReference(_appSettings) With {.Owner = Me}
            ' 預設放在編輯器右上角、側邊面板的左邊，不擋住照片中央
            Dim wa = Screen.FromControl(Me).WorkingArea
            Dim x = Math.Max(wa.Left, Math.Min(wa.Right - _reference.Width, Right - _reference.Width - _appSettings.SidePanelWidth - 30))
            Dim y = Math.Max(wa.Top, Math.Min(wa.Bottom - _reference.Height, Top + 90))
            _reference.Location = New Point(x, y)
            AddHandler _reference.ColorPicked, Sub(c)
                                                   SetDrawProp(Sub(d) d.StrokeColorArgb = c.ToArgb())
                                                   SetStatusMessage(Lang.F("從參考圖吸色 RGB({0}, {1}, {2})，已設成線條色。", c.R, c.G, c.B))
                                               End Sub
            AddHandler _reference.FillColorPicked, Sub(c)
                                                       SetDrawProp(Sub(d) d.FillColorArgb = c.ToArgb())
                                                       SetStatusMessage(Lang.F("從參考圖吸色 RGB({0}, {1}, {2})，已設成填色。", c.R, c.G, c.B))
                                                   End Sub
            AddHandler _reference.TraceChanged, Sub() UpdateTrace()
            AddHandler _reference.VisibleChanged, Sub() UpdateTrace()
        End If
        Return _reference
    End Function

    ''' <summary>
    ''' 參考圖「畫筆取色」：落筆處對應到參考圖（和描圖同樣等比例置中）的顏色設成線條色。
    ''' 臨摹時不必每換一個顏色就回參考圖點一下（同 Painter 的仿製顏色）。
    ''' </summary>
    Private Sub ApplyReferenceColor(p As Point)
        Dim r = _reference
        If r Is Nothing OrElse r.IsDisposed OrElse Not r.Visible OrElse Not r.PickWhileDrawing OrElse IsErasing() Then Return
        If DrawLayer.SamplesCanvas(_drawStyle.Brush) Then Return ' 混色、塗抹、仿製筆用畫布本身的顏色
        Dim img = r.RefImage
        Dim b = _canvas.ImageBounds()
        Dim s = Math.Min(b.Width / img.Width, b.Height / img.Height)
        Dim w = img.Width * s, h = img.Height * s
        Dim px = If(_canvas.MirrorActive, _canvas.ViewMirrorX(p.X), p.X)
        Dim c = r.ColorAt(CSng((px - (b.X + (b.Width - w) / 2)) / w), CSng((p.Y - (b.Y + (b.Height - h) / 2)) / h))
        If Not c.HasValue Then Return
        _drawStyle.StrokeColorArgb = Color.FromArgb(255, c.Value).ToArgb()
        SyncToolStrip(_drawStyle)
    End Sub

    Private Sub UpdateTrace()
        Dim r = _reference
        Dim tracing = r IsNot Nothing AndAlso Not r.IsDisposed AndAlso r.Visible AndAlso r.Tracing
        _canvas.SetTrace(If(tracing, r.RefImage, Nothing), If(tracing, r.TraceOpacity, 0))
    End Sub
End Class
