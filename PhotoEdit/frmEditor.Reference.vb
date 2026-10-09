Imports PhotoEdit

''' <summary>參考圖視窗（檢視 → 參考圖…）：吸色設成繪圖的線條色、描圖時把參考圖疊在畫布上。</summary>
Partial Friend Class frmEditor

    Private _reference As frmReference

    Private Sub ShowReference()
        If _reference Is Nothing OrElse _reference.IsDisposed Then
            _reference = New frmReference(_appSettings) With {.Owner = Me}
            ' 預設放在編輯器右上角、側邊面板的左邊，不擋住照片中央
            Dim wa = Screen.FromControl(Me).WorkingArea
            Dim x = Math.Max(wa.Left, Math.Min(wa.Right - _reference.Width, Right - _reference.Width - _appSettings.SidePanelWidth - 30))
            Dim y = Math.Max(wa.Top, Math.Min(wa.Bottom - _reference.Height, Top + 90))
            _reference.Location = New Point(x, y)
            AddHandler _reference.ColorPicked, Sub(c)
                                                   SetDrawProp(Sub(d) d.StrokeColorArgb = c.ToArgb())
                                                   SetStatusMessage($"從參考圖吸色 RGB({c.R}, {c.G}, {c.B})，已設成線條色。")
                                               End Sub
            AddHandler _reference.TraceChanged, Sub() UpdateTrace()
            AddHandler _reference.VisibleChanged, Sub() UpdateTrace()
        End If
        _reference.Show()
        _reference.Activate()
    End Sub

    Private Sub UpdateTrace()
        Dim r = _reference
        Dim tracing = r IsNot Nothing AndAlso Not r.IsDisposed AndAlso r.Visible AndAlso r.Tracing
        _canvas.SetTrace(If(tracing, r.RefImage, Nothing), If(tracing, r.TraceOpacity, 0))
    End Sub
End Class
