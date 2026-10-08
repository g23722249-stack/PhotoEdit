Imports System.Reflection
Imports PhotoEdit

''' <summary>
''' 多文件（MDI）：同時開多張畫布。設定視窗切換，重開程式才生效（_mdi 在啟動時決定）。
''' 編輯器只有一個畫布與一組面板；每份文件把「屬於這張照片」的欄位（照片、配方、復原紀錄、算圖快取、
''' 選取的圖層…，見 DocFieldNames）存在 EditorDocument 裡，切換時換進換出，所以各分頁（文字、繪圖、去背…）
''' 不用改就能作用在目前的文件上。
''' 兩種顯示方式（「視窗」選單切換，會記住）：
''' 分頁——畫布上方一排文件分頁；子視窗——畫布區裡可拖曳、縮放、並排的照片視窗，目前的那張放真正的畫布，
''' 其他的顯示各自最後一次算好的畫面。
''' </summary>
Partial Friend Class frmEditor

    Private _mdi As Boolean
    ''' <summary>測試用：不看設定檔，直接指定單一或多文件（Nothing＝看設定）。</summary>
    Friend Shared Property ForceMdi As Boolean?
    Private ReadOnly _docs As New List(Of EditorDocument)()
    Private _activeDoc As EditorDocument
    Private ReadOnly _docHost As New Panel()
    Private ReadOnly _docTabs As New DocTabStrip()
    Private ReadOnly _docArea As New Panel()
    Private _blankValues As Dictionary(Of String, Object)
    Private _docSwitching As Boolean
    Private _untitledCount As Integer

    ''' <summary>一份文件的狀態。</summary>
    Friend NotInheritable Class EditorDocument
        Public ReadOnly Values As New Dictionary(Of String, Object)()
        Public View As (Zoom As Double, Center As PointF)
        Public Title As String = "未命名"
        Public Dirty As Boolean
        Public Window As DocWindow

        ''' <summary>不是目前文件時，子視窗顯示的畫面（最後一次算好的預覽）。</summary>
        Public ReadOnly Property Snapshot As Bitmap
            Get
                Dim v As Object = Nothing
                Return If(Values.TryGetValue("_rendered", v), TryCast(v, Bitmap), Nothing)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 每份文件各自的欄位。新增「跟著照片走」的欄位時要加進來，否則切換文件時會被別的文件沿用。
    ''' 拖曳中、輸入中等暫時狀態不放這裡：切換前會先結束（裁切、選取、多邊形、圖說輸入）。
    ''' </summary>
    Private Shared ReadOnly DocFieldNames As String() = {
        "_photo", "_previewBase", "_rendered", "_recipe", "_savedRecipe", "_history",
        "_faces", "_faceState", "_retouched", "_retouchKey", "_hiRes", "_hiResKey", "_thumbKey",
        "_docPath", "_projectWorkDir",
        "_aiMask", "_aiMaskModel", "_aiMaskVersion", "_aiMaskDirty", "_cutoutShowMask",
        "_drawIndex", "_overlayIndex", "_stackIndex", "_localIndex", "_cloneSource", "_cloneOffset",
        "_framePhotoSize", "_frameMargins", "_lastCopy"}

    Private Shared _docFields As FieldInfo()

    Private Shared Function DocFields() As FieldInfo()
        If _docFields Is Nothing Then
            _docFields = DocFieldNames.Select(Function(n)
                                                   Dim f = GetType(frmEditor).GetField(n, BindingFlags.Instance Or BindingFlags.NonPublic)
                                                   If f Is Nothing Then Throw New InvalidOperationException("多文件：找不到欄位 " & n)
                                                   Return f
                                               End Function).ToArray()
        End If
        Return _docFields
    End Function

    '=====================================================================
    ' 版面
    '=====================================================================

    ''' <summary>建構時呼叫：多文件模式時畫布放進文件區（分頁列＋畫布，或子視窗區）。回傳要放在表單上的控制項。</summary>
    Private Function BuildDocHost() As Control
        _docHost.Dock = DockStyle.Fill
        _docHost.BackColor = Color.FromArgb(52, 52, 56)
        _docTabs.Dock = DockStyle.Top
        AddHandler _docTabs.TabClicked, Sub(d) SwitchTo(d)
        AddHandler _docTabs.CloseClicked, Sub(d) CloseDocument(d)
        _docArea.Dock = DockStyle.Fill
        _docArea.BackColor = Color.FromArgb(40, 40, 44)
        AddHandler _docArea.Resize, Sub() ClampWindows()
        ' 停靠：最後加入的最先停靠（分頁列在上面）。
        _docHost.Controls.Add(_docArea)
        _docHost.Controls.Add(_canvas)
        _docHost.Controls.Add(_docTabs)
        ApplyMdiLayout()
        Return _docHost
    End Function

    ''' <summary>建構完成後：記下「空白文件」的欄位值（新文件從這裡開始）。</summary>
    Private Sub CaptureBlankDocument()
        If Not _mdi Then Return
        _blankValues = New Dictionary(Of String, Object)()
        For Each f In DocFields()
            _blankValues(f.Name) = f.GetValue(Me)
        Next
    End Sub

    Private ReadOnly Property MdiWindows As Boolean
        Get
            Return _appSettings.MdiWindows
        End Get
    End Property

    ''' <summary>分頁或子視窗：把畫布放到對的地方。</summary>
    Private Sub ApplyMdiLayout()
        If Not _mdi Then Return
        _docHost.SuspendLayout()
        If MdiWindows Then
            _docTabs.Visible = False
            _docArea.Visible = True
            For Each d In _docs
                EnsureWindow(d)
            Next
            PlaceCanvasInActiveWindow()
        Else
            _docArea.Visible = False
            _docTabs.Visible = True
            If _canvas.Parent IsNot _docHost Then
                _canvas.Parent?.Controls.Remove(_canvas)
                _docHost.Controls.Add(_canvas)
                _docHost.Controls.SetChildIndex(_canvas, 1) ' 分頁列之下、填滿
            End If
            _canvas.Dock = DockStyle.Fill
            _canvas.Visible = True
        End If
        _docHost.ResumeLayout()
        UpdateDocChrome()
    End Sub

    Private Sub EnsureWindow(d As EditorDocument)
        If d.Window IsNot Nothing Then Return
        Dim w As New DocWindow(d)
        AddHandler w.ActivateRequested, Sub() SwitchTo(d)
        AddHandler w.CloseRequested, Sub() CloseDocument(d)
        ' 新視窗：從左上往右下錯開
        Dim n = _docs.IndexOf(d)
        Dim size = New Size(Math.Max(320, _docArea.ClientSize.Width * 2 \ 3), Math.Max(240, _docArea.ClientSize.Height * 2 \ 3))
        w.Bounds = New Rectangle(12 + (n Mod 8) * 28, 12 + (n Mod 8) * 28, size.Width, size.Height)
        d.Window = w
        _docArea.Controls.Add(w)
        ThemeManager.Attach(w)
    End Sub

    Private Sub PlaceCanvasInActiveWindow()
        If Not MdiWindows Then Return
        For Each d In _docs
            If d.Window IsNot Nothing Then d.Window.Active = d Is _activeDoc
        Next
        If _activeDoc?.Window Is Nothing Then
            _canvas.Visible = False
            Return
        End If
        Dim body = _activeDoc.Window.Body
        If _canvas.Parent IsNot body Then
            _canvas.Parent?.Controls.Remove(_canvas)
            body.Controls.Add(_canvas)
        End If
        _canvas.Dock = DockStyle.Fill
        _canvas.Visible = True
        _activeDoc.Window.BringToFront()
    End Sub

    Private Sub ClampWindows()
        For Each d In _docs
            d.Window?.ClampToParent()
        Next
    End Sub

    ''' <summary>分頁標題、子視窗標題（含未存檔的 *）跟著目前文件更新。</summary>
    Private Sub UpdateDocChrome()
        If Not _mdi Then Return
        If _activeDoc IsNot Nothing Then
            _activeDoc.Title = If(_photo Is Nothing, _activeDoc.Title, DocumentName)
            _activeDoc.Dirty = IsDirty
        End If
        _docTabs.SetDocuments(_docs, _activeDoc)
        For Each d In _docs
            d.Window?.Invalidate()
        Next
    End Sub

    '=====================================================================
    ' 文件狀態
    '=====================================================================

    Private Sub SaveState(d As EditorDocument)
        For Each f In DocFields()
            d.Values(f.Name) = f.GetValue(Me)
        Next
        d.View = _canvas.ViewState
        d.Title = If(_photo Is Nothing, d.Title, DocumentName)
        d.Dirty = IsDirty
    End Sub

    Private Sub LoadState(d As EditorDocument)
        For Each f In DocFields()
            Dim v As Object = Nothing
            If d.Values.TryGetValue(f.Name, v) Then f.SetValue(Me, v)
        Next
    End Sub

    ''' <summary>欄位換成空白文件（不釋放原本的物件：它們已經存進別的文件）。</summary>
    Private Sub ResetToBlank()
        For Each f In DocFields()
            If f.FieldType Is GetType(EditRecipe) Then
                f.SetValue(Me, New EditRecipe())
            ElseIf f.FieldType Is GetType(EditHistory) Then
                f.SetValue(Me, New EditHistory())
            Else
                f.SetValue(Me, _blankValues(f.Name))
            End If
        Next
    End Sub

    ''' <summary>切換前結束進行中的操作（裁切、選取、多邊形、圖說輸入）。</summary>
    Private Sub FinishTransientWork()
        ExitCropMode(apply:=False)
        CancelSelectionInProgress()
        _polyPoints = Nothing
        CommitCalloutEditor()
        _dd = DrawDrag.None
        _freePoints = Nothing
    End Sub

    '=====================================================================
    ' 開新文件、切換、關閉
    '=====================================================================

    ''' <summary>開檔、新影像、開專案前：多文件時不必問要不要存（會開在新的分頁）。</summary>
    Private Function ConfirmReplaceDocument() As Boolean
        Return _mdi OrElse ConfirmDiscard()
    End Function

    ''' <summary>ShowDocument 開頭：多文件時把目前的文件收起來，換成一份新的空白文件。</summary>
    Private Sub BeginNewDocument()
        If Not _mdi Then Return
        If _activeDoc IsNot Nothing AndAlso _photo Is Nothing Then Return ' 目前是空的：直接用
        FinishTransientWork()
        If _activeDoc IsNot Nothing Then
            SaveState(_activeDoc)
            ResetToBlank()
        End If
        _untitledCount += 1
        Dim d As New EditorDocument With {.Title = "未命名 " & _untitledCount}
        _docs.Add(d)
        _activeDoc = d
        If MdiWindows Then EnsureWindow(d)
        BumpBackgroundWork()
    End Sub

    ''' <summary>ShowDocument 結尾：更新分頁與子視窗。</summary>
    Private Sub EndNewDocument()
        If Not _mdi Then Return
        If MdiWindows Then PlaceCanvasInActiveWindow()
        UpdateDocChrome()
    End Sub

    ''' <summary>背景工作（人臉偵測、全尺寸算圖）的結果屬於切換前的文件：作廢。</summary>
    Private Sub BumpBackgroundWork()
        _faceGeneration += 1
        _hiResGeneration += 1
        _hiResBusy = False
    End Sub

    ''' <summary>切換到另一份文件。</summary>
    Private Sub SwitchTo(d As EditorDocument)
        If Not _mdi OrElse d Is Nothing OrElse d Is _activeDoc OrElse _docSwitching Then Return
        _docSwitching = True
        Try
            FinishTransientWork()
            If _activeDoc IsNot Nothing Then SaveState(_activeDoc)
            LoadState(d)
            _activeDoc = d
            AfterDocumentLoaded()
        Finally
            _docSwitching = False
        End Try
    End Sub

    ''' <summary>換進文件之後：重新整理所有面板、重算預覽、還原檢視。</summary>
    Private Sub AfterDocumentLoaded()
        BumpBackgroundWork()
        If _photo IsNot Nothing AndAlso _faces Is Nothing Then StartFaceDetection()
        _brushPreviewKey = Nothing
        _thumbKey = Nothing ' 濾鏡縮圖重算成這份文件的
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolFromTab()
        UpdatePortraitControls()
        UpdateTitle()
        If MdiWindows Then PlaceCanvasInActiveWindow()
        If _photo Is Nothing Then
            _canvas.Image = Nothing
            UpdateStatus()
        Else
            RenderNow()
            If _activeDoc IsNot Nothing Then _canvas.ViewState = _activeDoc.View
        End If
        UpdateDocChrome()
    End Sub

    ''' <summary>關閉一份文件（未存檔時先問）。回傳 False 表示使用者取消。</summary>
    Private Function CloseDocument(d As EditorDocument) As Boolean
        If Not _mdi OrElse d Is Nothing Then Return True
        If d IsNot _activeDoc Then SwitchTo(d)
        If Not ConfirmDiscard() Then Return False
        FinishTransientWork()
        ReleaseActiveDocument()
        Dim index = _docs.IndexOf(d)
        _docs.Remove(d)
        If d.Window IsNot Nothing Then
            If _canvas.Parent Is d.Window.Body Then d.Window.Body.Controls.Remove(_canvas)
            _docArea.Controls.Remove(d.Window)
            d.Window.Dispose()
        End If
        _activeDoc = Nothing
        ResetToBlank()
        If _docs.Count > 0 Then
            Dim nextDoc = _docs(Math.Min(index, _docs.Count - 1))
            LoadState(nextDoc)
            _activeDoc = nextDoc
        End If
        AfterDocumentLoaded()
        Return True
    End Function

    ''' <summary>釋放目前文件的照片、影像與暫存資料夾。</summary>
    Private Sub ReleaseActiveDocument()
        _canvas.Image = Nothing
        DisposeImages()
        _aiMask?.Dispose()
        _aiMask = Nothing
        SyncLock _sourceLock
            _photo?.Dispose()
            _photo = Nothing
        End SyncLock
        DeleteProjectWorkDir()
    End Sub

    ''' <summary>關閉程式：每份未存檔的文件都問一次。</summary>
    Private Function ConfirmCloseAll() As Boolean
        If Not _mdi Then Return ConfirmDiscard()
        For Each d In _docs.ToList()
            SwitchTo(d)
            If Not ConfirmDiscard() Then Return False
        Next
        Return True
    End Function

    ''' <summary>程式關閉時釋放所有文件（目前的文件由 OnFormClosed 原本的程式釋放）。</summary>
    Private Sub DisposeInactiveDocuments()
        If Not _mdi Then Return
        Dim current = _activeDoc
        If current IsNot Nothing Then SaveState(current)
        For Each d In _docs
            If d Is current Then Continue For
            LoadState(d)
            ReleaseActiveDocument()
        Next
        If current IsNot Nothing Then LoadState(current)
    End Sub

    Private Sub NextDocument(dir As Integer)
        If Not _mdi OrElse _docs.Count < 2 Then Return
        Dim i = _docs.IndexOf(_activeDoc)
        SwitchTo(_docs((i + dir + _docs.Count) Mod _docs.Count))
    End Sub

    '=====================================================================
    ' 「視窗」選單
    '=====================================================================

    Private _mdiTabsItem As Aqua.MenuItem
    Private _mdiWindowsItem As Aqua.MenuItem

    Private Sub BuildWindowMenu(root As Aqua.MenuItem)
        If Not _mdi Then Return
        Dim windowMenu = root.AddItem(New Aqua.MenuItem("視窗"))
        _mdiTabsItem = windowMenu.AddItem(Item("mdi_tabs", "分頁顯示"))
        _mdiWindowsItem = windowMenu.AddItem(Item("mdi_windows", "子視窗顯示"))
        windowMenu.AddItem(New Aqua.MenuItem("-"))
        windowMenu.AddItem(Item("mdi_cascade", "重疊排列"))
        windowMenu.AddItem(Item("mdi_tilev", "左右並排"))
        windowMenu.AddItem(Item("mdi_tileh", "上下並排"))
        windowMenu.AddItem(New Aqua.MenuItem("-"))
        windowMenu.AddItem(Item("nextdoc", "下一份文件 (Ctrl+Tab)"))
        windowMenu.AddItem(Item("closedoc", "關閉文件 (Ctrl+W)"))
        UpdateWindowMenuChecks()
    End Sub

    Private Sub UpdateWindowMenuChecks()
        If _mdiTabsItem Is Nothing Then Return
        _mdiTabsItem.Checked = Not MdiWindows
        _mdiWindowsItem.Checked = MdiWindows
    End Sub

    ''' <summary>多文件的指令；不是的話回傳 False。</summary>
    Private Function HandleMdiCommand(name As String) As Boolean
        If Not _mdi Then Return False
        Select Case name
            Case "mdi_tabs", "mdi_windows"
                _appSettings.MdiWindows = name = "mdi_windows"
                _appSettings.Save()
                ApplyMdiLayout()
                UpdateWindowMenuChecks()
                RenderNow()
            Case "mdi_cascade", "mdi_tilev", "mdi_tileh"
                If Not MdiWindows Then HandleMdiCommand("mdi_windows")
                ArrangeWindows(name)
            Case "nextdoc" : NextDocument(1)
            Case "closedoc" : CloseDocument(_activeDoc)
            Case Else : Return False
        End Select
        Return True
    End Function

    Private Sub ArrangeWindows(kind As String)
        Dim wins = _docs.Where(Function(d) d.Window IsNot Nothing).Select(Function(d) d.Window).ToList()
        If wins.Count = 0 Then Return
        Dim area = _docArea.ClientRectangle
        For i = 0 To wins.Count - 1
            Dim w = wins(i)
            w.Restore()
            Select Case kind
                Case "mdi_cascade"
                    Dim s = New Size(area.Width * 2 \ 3, area.Height * 2 \ 3)
                    w.Bounds = New Rectangle(8 + i * 28, 8 + i * 28, s.Width, s.Height)
                    w.BringToFront()
                Case "mdi_tilev"
                    Dim cw = area.Width \ wins.Count
                    w.Bounds = New Rectangle(i * cw, 0, cw, area.Height)
                Case Else
                    Dim ch = area.Height \ wins.Count
                    w.Bounds = New Rectangle(0, i * ch, area.Width, ch)
            End Select
            w.ClampToParent()
        Next
        _activeDoc?.Window?.BringToFront()
    End Sub

    '=====================================================================
    ' 分頁列
    '=====================================================================

    ''' <summary>畫布上方的文件分頁：點選切換、× 或滑鼠中鍵關閉，未存檔的標題後面有 *。</summary>
    Friend Class DocTabStrip
        Inherits Control

        Public Event TabClicked(d As EditorDocument)
        Public Event CloseClicked(d As EditorDocument)

        Private _docs As New List(Of EditorDocument)()
        Private _active As EditorDocument
        Private _hover As Integer = -1
        Private ReadOnly _rects As New List(Of Rectangle)()

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Height = 30
            Font = New Font("Microsoft JhengHei UI", 9.5F)
        End Sub

        Public Sub SetDocuments(docs As List(Of EditorDocument), active As EditorDocument)
            _docs = docs.ToList()
            _active = active
            Invalidate()
        End Sub

        Private Function CloseRect(r As Rectangle) As Rectangle
            Return New Rectangle(r.Right - 22, r.Y + (r.Height - 16) \ 2, 16, 16)
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(ThemeManager.Back(Color.FromArgb(222, 225, 231)))
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            _rects.Clear()
            Dim x = 4
            For i = 0 To _docs.Count - 1
                Dim d = _docs(i)
                Dim text = d.Title & If(d.Dirty, " *", "")
                Dim w = Math.Min(240, Math.Max(110, TextRenderer.MeasureText(text, Font).Width + 40))
                Dim r As New Rectangle(x, 4, w, Height - 4)
                _rects.Add(r)
                Dim active = d Is _active
                Using b As New SolidBrush(If(active, ThemeManager.Back(Color.FromArgb(250, 250, 252)),
                                             If(i = _hover, ThemeManager.Back(Color.FromArgb(236, 238, 242)), ThemeManager.Back(Color.FromArgb(210, 214, 222)))))
                    g.FillRectangle(b, r)
                End Using
                If active Then
                    Using b As New SolidBrush(Color.FromArgb(47, 128, 237))
                        g.FillRectangle(b, r.X, r.Y, r.Width, 3)
                    End Using
                End If
                TextRenderer.DrawText(g, text, Font, New Rectangle(r.X + 10, r.Y, r.Width - 34, r.Height),
                                      ThemeManager.Fore(If(active, Color.FromArgb(30, 34, 42), Color.FromArgb(80, 86, 98))),
                                      TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
                Dim cr = CloseRect(r)
                Using p As New Pen(ThemeManager.Fore(Color.FromArgb(110, 116, 128)), 1.5F)
                    g.DrawLine(p, cr.X + 4, cr.Y + 4, cr.Right - 4, cr.Bottom - 4)
                    g.DrawLine(p, cr.Right - 4, cr.Y + 4, cr.X + 4, cr.Bottom - 4)
                End Using
                x += w + 2
            Next
            If _docs.Count = 0 Then
                TextRenderer.DrawText(g, "沒有開啟的文件（檔案 → 載入、新增，或把照片拖進來）", Font, ClientRectangle,
                                      ThemeManager.Fore(Color.FromArgb(105, 110, 120)), TextFormatFlags.VerticalCenter Or TextFormatFlags.Left)
            End If
        End Sub

        Private Function HitTab(p As Point) As Integer
            For i = 0 To _rects.Count - 1
                If _rects(i).Contains(p) Then Return i
            Next
            Return -1
        End Function

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            Dim i = HitTab(e.Location)
            If i < 0 OrElse i >= _docs.Count Then Return
            If e.Button = MouseButtons.Middle OrElse (e.Button = MouseButtons.Left AndAlso CloseRect(_rects(i)).Contains(e.Location)) Then
                RaiseEvent CloseClicked(_docs(i))
            ElseIf e.Button = MouseButtons.Left Then
                RaiseEvent TabClicked(_docs(i))
            End If
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim i = HitTab(e.Location)
            If i <> _hover Then _hover = i : Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = -1
            Invalidate()
        End Sub
    End Class

    '=====================================================================
    ' 子視窗
    '=====================================================================

    ''' <summary>
    ''' 文件區裡的照片子視窗：拖曳標題列移動、拖右下角縮放、按兩下標題列放大／還原、× 關閉；點一下成為目前的文件。
    ''' 目前的文件在 Body 裡放真正的畫布；其他的顯示最後一次算好的畫面。
    ''' </summary>
    Friend Class DocWindow
        Inherits Control

        Private Const TitleH As Integer = 24
        Private Const Grip As Integer = 14

        Public ReadOnly Doc As EditorDocument
        Public ReadOnly Body As New Panel()
        Public Event ActivateRequested As EventHandler
        Public Event CloseRequested As EventHandler

        Private _active As Boolean
        Private _dragMode As Integer ' 0 無、1 移動、2 縮放
        Private _dragStart As Point
        Private _startBounds As Rectangle
        Private _normalBounds As Rectangle?

        Public Sub New(d As EditorDocument)
            Doc = d
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Font = New Font("Microsoft JhengHei UI", 9.5F)
            Body.Anchor = AnchorStyles.Top Or AnchorStyles.Left ' 位置由 LayoutBody 排（標題列下面）
            Body.BackColor = Color.FromArgb(52, 52, 56)
            AddHandler Body.Paint, AddressOf PaintSnapshot
            AddHandler Body.MouseDown, Sub() RaiseEvent ActivateRequested(Me, EventArgs.Empty)
            AddHandler Body.Resize, Sub() Body.Invalidate()
            Controls.Add(Body)
            LayoutBody()
        End Sub

        ''' <summary>內容放在標題列下面、留 1 像素外框。</summary>
        Private Sub LayoutBody()
            Body.SetBounds(1, TitleH, Math.Max(1, Width - 2), Math.Max(1, Height - TitleH - 1))
        End Sub

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            LayoutBody()
        End Sub

        Public Property Active As Boolean
            Get
                Return _active
            End Get
            Set(value As Boolean)
                If _active = value Then Return
                _active = value
                Invalidate()
                Body.Invalidate()
            End Set
        End Property

        ''' <summary>不是目前的文件：把最後一次的畫面縮放置中畫出來。</summary>
        Private Sub PaintSnapshot(sender As Object, e As PaintEventArgs)
            If _active Then Return
            Dim img = Doc.Snapshot
            If img Is Nothing Then Return
            Try
                Dim area = Body.ClientRectangle
                area.Inflate(-8, -8)
                Dim k = Math.Min(area.Width / CDbl(img.Width), area.Height / CDbl(img.Height))
                If k <= 0 Then Return
                Dim w = CInt(img.Width * k), h = CInt(img.Height * k)
                e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear
                e.Graphics.DrawImage(img, New Rectangle(area.X + (area.Width - w) \ 2, area.Y + (area.Height - h) \ 2, w, h))
            Catch ex As ArgumentException
                ' 影像已被釋放（文件剛關閉）：不畫
            End Try
        End Sub

        Private ReadOnly Property CloseBox As Rectangle
            Get
                Return New Rectangle(Width - 22, (TitleH - 16) \ 2, 16, 16)
            End Get
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Dim title = If(_active, Color.FromArgb(47, 98, 170), ThemeManager.Back(Color.FromArgb(214, 218, 226)))
            Using b As New SolidBrush(title)
                g.FillRectangle(b, 0, 0, Width, TitleH)
            End Using
            Dim fore = If(_active, Color.White, ThemeManager.Fore(Color.FromArgb(50, 56, 66)))
            TextRenderer.DrawText(g, Doc.Title & If(Doc.Dirty, " *", ""), Font, New Rectangle(8, 0, Width - 40, TitleH), fore,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
            Dim cb = CloseBox
            Using p As New Pen(fore, 1.5F)
                g.DrawLine(p, cb.X + 4, cb.Y + 4, cb.Right - 4, cb.Bottom - 4)
                g.DrawLine(p, cb.Right - 4, cb.Y + 4, cb.X + 4, cb.Bottom - 4)
            End Using
            Using p As New Pen(If(_active, Color.FromArgb(47, 98, 170), ThemeManager.Line(Color.FromArgb(150, 156, 168))))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1)
            End Using
        End Sub

        Private Function InGrip(p As Point) As Boolean
            Return p.X >= Width - Grip AndAlso p.Y >= Height - Grip
        End Function

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            RaiseEvent ActivateRequested(Me, EventArgs.Empty)
            BringToFront()
            If e.Button <> MouseButtons.Left Then Return
            If e.Y < TitleH AndAlso CloseBox.Contains(e.Location) Then
                RaiseEvent CloseRequested(Me, EventArgs.Empty)
                Return
            End If
            _dragMode = If(InGrip(e.Location), 2, If(e.Y < TitleH, 1, 0))
            _dragStart = Cursor.Position
            _startBounds = Bounds
            Capture = _dragMode <> 0
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If _dragMode = 0 Then
                Cursor = If(InGrip(e.Location), Cursors.SizeNWSE, Cursors.Default)
                Return
            End If
            Dim dx = Cursor.Position.X - _dragStart.X, dy = Cursor.Position.Y - _dragStart.Y
            If _dragMode = 1 Then
                Location = New Point(_startBounds.X + dx, _startBounds.Y + dy)
            Else
                Size = New Size(Math.Max(200, _startBounds.Width + dx), Math.Max(150, _startBounds.Height + dy))
            End If
            _normalBounds = Nothing
            ClampToParent()
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            _dragMode = 0
            Capture = False
        End Sub

        Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
            MyBase.OnMouseDoubleClick(e)
            If e.Y >= TitleH OrElse Parent Is Nothing Then Return
            ' 放大到整個文件區／還原
            If _normalBounds.HasValue Then
                Restore()
            Else
                _normalBounds = Bounds
                Bounds = Parent.ClientRectangle
            End If
        End Sub

        Public Sub Restore()
            If _normalBounds.HasValue Then Bounds = _normalBounds.Value
            _normalBounds = Nothing
        End Sub

        ''' <summary>標題列至少留一段在文件區裡，才拖得回來。</summary>
        Public Sub ClampToParent()
            If Parent Is Nothing Then Return
            Dim area = Parent.ClientRectangle
            Dim x = Math.Max(area.Left - Width + 80, Math.Min(Left, area.Right - 80))
            Dim y = Math.Max(area.Top, Math.Min(Top, area.Bottom - TitleH))
            If x <> Left OrElse y <> Top Then Location = New Point(x, y)
        End Sub
    End Class
End Class
