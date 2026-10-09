Imports PhotoEdit

''' <summary>
''' 繪圖的顏色：浮動的 Aqua.ColorPickerWindow（點繪圖分頁的線條色／填色／文字色打開），改顏色立即套用到畫筆；
''' 上方「線條／填色／文字」切換要改哪一個。視窗位置記在 settings.json，下次開程式還原。
'''
''' Alt 吸色（比照 Photoshop）：按住 Alt 在畫布上點或拖曳，吸畫面上看到的顏色（照片＋可見圖層）設成線條色，
''' Alt＋Shift 設成填色；放開時記進最近使用色。仿製筆的 Alt 點選仍然是設定仿製來源（仿製筆不用線條色）。
''' </summary>
Partial Friend Class frmEditor

    Private Shared ReadOnly ColorTargets As String() = {"線條", "填色", "文字"}
    Private Const TargetStroke As Integer = 0, TargetFill As Integer = 1, TargetText As Integer = 2

    Private _colorWin As Aqua.ColorPickerWindow
    ''' <summary>選色視窗改的顏色正在套用：這時不要把顏色再寫回視窗（會打斷拖曳）。</summary>
    Private _applyingPicked As Boolean
    ''' <summary>選色視窗停止調整一會兒後才記進最近使用色（拖曳中不記）。</summary>
    Private ReadOnly _recentTimer As New Timer() With {.Interval = 1200}
    Private _colorWinMoved As Boolean
    ''' <summary>拖曳中最新、還沒套用的顏色（合併成每 30 ms 一次）。</summary>
    Private _pendingPick As (Color As Color, Target As Integer)?
    Private ReadOnly _applyTimer As New Timer() With {.Interval = 30}
    ''' <summary>上次在選色視窗改顏色的時間；拖曳中先不重畫筆刷預覽（每次要 8 ms），停下來再畫。</summary>
    Private _lastPickTime As Integer = Integer.MinValue
    Private ReadOnly _brushPreviewTimer As New Timer() With {.Interval = 250}

    ''' <summary>剛在選色視窗拖曳（250 ms 內）：筆刷預覽延後到停下來再重畫。</summary>
    Private Function DeferBrushPreviews() As Boolean
        If _lastPickTime = Integer.MinValue OrElse Environment.TickCount - _lastPickTime > 250 Then Return False
        _brushPreviewTimer.Stop()
        _brushPreviewTimer.Start()
        Return True
    End Function

    Private Shared Function ColorOf(d As DrawLayer, target As Integer) As Integer
        Select Case target
            Case TargetFill : Return d.FillColorArgb
            Case TargetText : Return d.TextColorArgb
            Case Else : Return d.StrokeColorArgb
        End Select
    End Function

    Private Shared Sub SetColorOf(d As DrawLayer, target As Integer, argb As Integer)
        Select Case target
            Case TargetFill
                d.FillColorArgb = argb
                d.Filled = True ' 同原本的填色按鈕：選了填色就打開填色
            Case TargetText : d.TextColorArgb = argb
            Case Else : d.StrokeColorArgb = argb
        End Select
    End Sub

    Private Sub EnsureColorWindow()
        If _colorWin IsNot Nothing AndAlso Not _colorWin.IsDisposed Then Return
        _colorWin = New Aqua.ColorPickerWindow With {.Targets = ColorTargets, .Owner = Me, .Tag = ThemeManager.SkipTreeTag}
        _colorWin.Location = ColorWindowStartLocation(_colorWin.Size)
        AddHandler _colorWin.ColorChanged, Sub()
                                               ' 拖曳時不每一下都套用：記下最新的顏色，最多每 30 ms 套用一次，選色器才跟得上手
                                               _pendingPick = (_colorWin.Color, _colorWin.SelectedTarget)
                                               _lastPickTime = Environment.TickCount
                                               If Not _applyTimer.Enabled Then _applyTimer.Start()
                                               _recentTimer.Stop()
                                               _recentTimer.Start()
                                           End Sub
        AddHandler _applyTimer.Tick, Sub()
                                         _applyTimer.Stop()
                                         If _pendingPick.HasValue Then
                                             Dim p = _pendingPick.Value
                                             _pendingPick = Nothing
                                             ApplyPickedColor(p.Color, p.Target)
                                         End If
                                     End Sub
        AddHandler _brushPreviewTimer.Tick, Sub()
                                                _brushPreviewTimer.Stop()
                                                UpdateBrushPreviews(If(StyleTarget(_recipe), _drawStyle))
                                            End Sub
        AddHandler _colorWin.TargetChanged, Sub() SyncColorWindow()
        ' 焦點在選色視窗時按快速工具的熱鍵：一樣叫出快速面板、輪盤
        AddHandler _colorWin.KeyDown, Sub(s, e)
                                          If HandleQuickKey(e.KeyData) Then e.Handled = True : e.SuppressKeyPress = True
                                      End Sub
        AddHandler _colorWin.LocationChanged, Sub()
                                                  If Not _colorWin.Visible Then Return
                                                  _appSettings.ColorWindowX = _colorWin.Left
                                                  _appSettings.ColorWindowY = _colorWin.Top
                                                  _colorWinMoved = True
                                              End Sub
        AddHandler _colorWin.VisibleChanged, Sub() If Not _colorWin.Visible Then SaveColorWindowPosition()
        AddHandler _recentTimer.Tick, Sub()
                                          _recentTimer.Stop()
                                          If _colorWin IsNot Nothing AndAlso Not _colorWin.IsDisposed Then _colorWin.AddRecentColor(_colorWin.Color)
                                      End Sub
    End Sub

    ''' <summary>上次的位置（還在某個螢幕裡時）；否則放在側邊面板左邊、畫布右上。</summary>
    Private Function ColorWindowStartLocation(size As Size) As Point
        If _appSettings.ColorWindowX.HasValue AndAlso _appSettings.ColorWindowY.HasValue Then
            Dim saved As New Rectangle(_appSettings.ColorWindowX.Value, _appSettings.ColorWindowY.Value, size.Width, 40)
            If Screen.AllScreens.Any(Function(s) s.WorkingArea.IntersectsWith(saved)) Then Return saved.Location
        End If
        Dim wa = Screen.FromControl(Me).WorkingArea
        Dim x = Math.Max(wa.Left, Math.Min(wa.Right - size.Width, Right - size.Width - _appSettings.SidePanelWidth - 30))
        Dim y = Math.Max(wa.Top, Math.Min(wa.Bottom - size.Height, Top + 110))
        Return New Point(x, y)
    End Function

    Private Sub SaveColorWindowPosition()
        If Not _colorWinMoved Then Return
        _colorWinMoved = False
        _appSettings.Save()
    End Sub

    ''' <summary>打開選色視窗並切到指定目標（繪圖分頁的三個顏色按鈕）。</summary>
    Private Sub ShowColorWindow(target As Integer)
        EnsureColorWindow()
        _colorWin.SelectedTarget = target
        SyncColorWindow(force:=True)
        If Not _colorWin.Visible Then _colorWin.Show(Me)
        _colorWin.Activate()
    End Sub

    ''' <summary>
    ''' 切到「繪圖」分頁自動顯示選色視窗（不搶鍵盤焦點），離開就收起；在繪圖分頁裡關掉的，下次切進來會再出現。
    ''' </summary>
    Private Sub AutoShowColorWindow(drawing As Boolean)
        If drawing Then
            If Not Visible OrElse WindowState = FormWindowState.Minimized Then Return
            EnsureColorWindow()
            SyncColorWindow(force:=True)
            If Not _colorWin.Visible Then _colorWin.ShowInactive(Me)
        ElseIf _colorWin IsNot Nothing AndAlso Not _colorWin.IsDisposed AndAlso _colorWin.Visible Then
            _colorWin.Hide()
        End If
    End Sub

    ''' <summary>選色視窗改了顏色：套用到下一筆的設定（選取的圖層也一起改），連續拖曳在復原紀錄裡算一步。</summary>
    Private Sub ApplyPickedColor(c As Color, target As Integer)
        Dim argb = Color.FromArgb(255, c).ToArgb()
        _applyingPicked = True
        Try
            SetDrawProp(Sub(d) SetColorOf(d, target, argb), "colorwin:" & target)
        Finally
            _applyingPicked = False
        End Try
    End Sub

    ''' <summary>畫筆設定換了（選了別的圖層、復原…）：選色視窗顯示目前目標的顏色。</summary>
    Private Sub SyncColorWindow(Optional force As Boolean = False)
        If _applyingPicked OrElse _colorWin Is Nothing OrElse _colorWin.IsDisposed Then Return
        If Not force AndAlso Not _colorWin.Visible Then Return
        Dim st = If(StyleTarget(_recipe), _drawStyle)
        Dim c = Color.FromArgb(255, Color.FromArgb(ColorOf(st, _colorWin.SelectedTarget)))
        If _colorWin.Color.ToArgb() <> c.ToArgb() OrElse force Then _colorWin.Color = c
    End Sub

    '=====================================================================
    ' Alt 吸色
    '=====================================================================

    Private _altPicking As Boolean
    Private _altPickTarget As Integer
    Private _dropper As Aqua.DropperCursor

    ''' <summary>仿製筆（直接繪製、不是橡皮擦）時 Alt 是設定仿製來源，不吸色。</summary>
    Private Function AltSetsCloneSource() As Boolean
        Return _drawStyle.Brush = BrushKind.Clone AndAlso Not _drawEraser.Checked AndAlso
               (_drawStrip.SelectedTool = CInt(DrawShape.Raster) OrElse _drawStrip.SelectedTool = CInt(DrawShape.Freehand))
    End Function

    Private Function WantsAltPick() As Boolean
        Return _pickOnce OrElse (ModifierKeys.HasFlag(Keys.Alt) AndAlso Not AltSetsCloneSource()) ' 輪盤／面板選了吸管：點一下就吸
    End Function

    Private Function DropperCursor() As Cursor
        If _dropper Is Nothing Then _dropper = Aqua.DropperCursor.TryCreate()
        Return If(_dropper IsNot Nothing, _dropper.Cursor, Cursors.Cross)
    End Function

    Private Sub BeginAltPick(p As Point)
        _altPicking = True
        _altPickTarget = If(ModifierKeys.HasFlag(Keys.Shift), TargetFill, TargetStroke)
        AltPickAt(p)
    End Sub

    ''' <summary>拖曳中一直更新（看得到吸到的顏色），連續吸色在復原紀錄裡算一步。</summary>
    Private Sub AltPickAt(p As Point)
        Dim c = _canvas.SampleColor(p)
        If Not c.HasValue Then Return
        Dim argb = c.Value.ToArgb()
        SetDrawProp(Sub(d) SetColorOf(d, _altPickTarget, argb), "altpick:" & _altPickTarget)
        If _colorWin IsNot Nothing AndAlso _colorWin.Visible Then
            If _colorWin.SelectedTarget <> _altPickTarget Then _colorWin.SelectedTarget = _altPickTarget
            SyncColorWindow()
        End If
    End Sub

    Private Sub EndAltPick(p As Point)
        _altPicking = False
        _pickOnce = False ' 吸管只吸一次
        AltPickAt(p)
        Dim st = If(StyleTarget(_recipe), _drawStyle)
        Dim c = Color.FromArgb(255, Color.FromArgb(ColorOf(st, _altPickTarget)))
        EnsureColorWindow()
        _colorWin.AddRecentColor(c)
        SetStatusMessage($"吸色 RGB({c.R}, {c.G}, {c.B})，已設成{If(_altPickTarget = TargetFill, "填色", "線條色")}。" &
                         "（Alt：線條色、Alt＋Shift：填色；仿製筆的 Alt 是設定來源）")
    End Sub

    ''' <summary>按下或放開 Alt 時馬上換游標（不必等滑鼠移動）。</summary>
    Private Sub RefreshAltCursor()
        If _tabs.SelectedIndex <> TabDraw OrElse _photo Is Nothing OrElse _dd <> DrawDrag.None Then Return
        Dim p = _canvas.PointToClient(_canvas.PointerPosition)
        If Not _canvas.ClientRectangle.Contains(p) Then Return
        UpdateDrawCursor(p, ScreenToUnit(p))
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.KeyCode = Keys.Menu Then RefreshAltCursor()
    End Sub

    Protected Overrides Sub OnKeyUp(e As KeyEventArgs)
        MyBase.OnKeyUp(e)
        If e.KeyCode = Keys.Menu Then RefreshAltCursor()
    End Sub

    Private Const WM_SYSCOMMAND As Integer = &H112
    Private Const SC_KEYMENU As Integer = &HF100

    ''' <summary>繪圖分頁裡單按 Alt 是吸色：不要讓 Windows 把它當成「開視窗選單」（焦點會跑掉）。</summary>
    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_SYSCOMMAND AndAlso (m.WParam.ToInt64() And &HFFF0L) = SC_KEYMENU AndAlso m.LParam = IntPtr.Zero AndAlso
           _tabs IsNot Nothing AndAlso _tabs.SelectedIndex = TabDraw Then Return
        MyBase.WndProc(m)
    End Sub
End Class
