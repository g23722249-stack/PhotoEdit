''' <summary>
''' 捲軸：影像放大到超出畫布時，下方與右方出現捲軸，拖曳捲軸移動檢視；用滑鼠平移、縮放時捲軸跟著同步。
''' 符合視窗、裁切模式時不顯示。影像的顯示範圍以 ViewSize（扣掉捲軸）計算。
''' </summary>
Partial Friend Class PreviewCanvas

    Private ReadOnly _hScroll As New HScrollBar()
    Private ReadOnly _vScroll As New VScrollBar()
    Private ReadOnly _scrollCorner As New Panel()
    Private _hOn As Boolean, _vOn As Boolean

    ''' <summary>可以顯示影像的範圍（畫布扣掉出現中的捲軸）。</summary>
    Private ReadOnly Property ViewSize As Size
        Get
            Return New Size(Math.Max(1, ClientSize.Width - If(_vOn, _vScroll.Width, 0)),
                            Math.Max(1, ClientSize.Height - If(_hOn, _hScroll.Height, 0)))
        End Get
    End Property

    Private Sub InitScrollBars()
        _hScroll.Visible = False
        _vScroll.Visible = False
        _scrollCorner.Visible = False
        _scrollCorner.BackColor = Color.FromArgb(38, 38, 40)
        _hScroll.Cursor = Cursors.Default
        _vScroll.Cursor = Cursors.Default
        AddHandler _hScroll.Scroll, Sub(s, e) OnScrollBar(horizontal:=True)
        AddHandler _vScroll.Scroll, Sub(s, e) OnScrollBar(horizontal:=False)
        Controls.Add(_hScroll)
        Controls.Add(_vScroll)
        Controls.Add(_scrollCorner)
    End Sub

    ''' <summary>依目前的倍率與中心決定要不要顯示捲軸、位置與數值。</summary>
    Private Sub UpdateScrollBars()
        Dim hOn = False, vOn = False
        Dim dispW = 0.0, dispH = 0.0
        If _image IsNot Nothing AndAlso _zoom <> 0 AndAlso Not _cropMode Then
            dispW = _image.Width * _imageScale * _zoom
            dispH = _image.Height * _imageScale * _zoom
            Dim sw = _vScroll.Width, sh = _hScroll.Height
            ' 一邊出現會讓另一邊的可見範圍變小，算兩次
            For i = 1 To 2
                hOn = dispW > ClientSize.Width - If(vOn, sw, 0) + 0.5
                vOn = dispH > ClientSize.Height - If(hOn, sh, 0) + 0.5
            Next
        End If
        Dim changed = hOn <> _hOn OrElse vOn <> _vOn
        _hOn = hOn : _vOn = vOn
        Dim cw = ClientSize.Width, ch = ClientSize.Height
        If hOn Then _hScroll.SetBounds(0, ch - _hScroll.Height, cw - If(vOn, _vScroll.Width, 0), _hScroll.Height)
        If vOn Then _vScroll.SetBounds(cw - _vScroll.Width, 0, _vScroll.Width, ch - If(hOn, _hScroll.Height, 0))
        _scrollCorner.SetBounds(cw - _vScroll.Width, ch - _hScroll.Height, _vScroll.Width, _hScroll.Height)
        If hOn Then SetBar(_hScroll, dispW, ViewSize.Width, _center.X)
        If vOn Then SetBar(_vScroll, dispH, ViewSize.Height, _center.Y)
        _hScroll.Visible = hOn
        _vScroll.Visible = vOn
        _scrollCorner.Visible = hOn AndAlso vOn
        If changed Then Invalidate()
    End Sub

    ''' <summary>捲軸數值＝可見範圍左（上）緣在放大後影像中的像素位置。</summary>
    Private Shared Sub SetBar(bar As ScrollBar, disp As Double, view As Integer, center As Single)
        Dim max = Math.Max(1, CInt(Math.Ceiling(disp)))
        bar.Minimum = 0
        bar.Maximum = max
        bar.LargeChange = Math.Max(1, Math.Min(max, view))
        bar.SmallChange = Math.Max(1, view \ 10)
        Dim v = CInt(Math.Round(center * disp - view / 2.0))
        bar.Value = Math.Max(0, Math.Min(max - bar.LargeChange + 1, v))
    End Sub

    ''' <summary>拖曳捲軸：換算回檢視中心。</summary>
    Private Sub OnScrollBar(horizontal As Boolean)
        If _image Is Nothing OrElse _zoom = 0 Then Return
        Dim dispW = _image.Width * _imageScale * _zoom, dispH = _image.Height * _imageScale * _zoom
        If horizontal Then
            _center = New PointF(CSng((_hScroll.Value + ViewSize.Width / 2.0) / dispW), _center.Y)
        Else
            _center = New PointF(_center.X, CSng((_vScroll.Value + ViewSize.Height / 2.0) / dispH))
        End If
        Invalidate()
        RaiseEvent ZoomChanged(Me, EventArgs.Empty) ' 讓編輯器更新跟著畫面走的東西（圖說輸入框、狀態列）
    End Sub
End Class
