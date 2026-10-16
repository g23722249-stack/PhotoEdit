Imports PhotoEdit

''' <summary>
''' 使用手冊（說明 → 使用手冊…，F1）：上方搜尋、左邊目錄、右邊內容。
''' 內容由 frmEditor.BuildManual 產生（選單、右側頁籤、繪圖工具／筆觸／紙紋）。
''' 關閉只是隱藏，下次打開停在同一頁。
''' </summary>
Friend Class frmManual
    Inherits Aqua.AquaForm

    Private ReadOnly _sections As List(Of ManualSection)
    Private ReadOnly _search As New TextBox()
    Private ReadOnly _searchLabel As New Label With {.Text = Lang.T("搜尋"), .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent}
    Private ReadOnly _back As New Button With {.Text = Lang.T("◀ 搜尋結果"), .UseVisualStyleBackColor = True, .Visible = False}
    Private ReadOnly _tree As New TreeView With {.HideSelection = False, .ShowLines = False, .FullRowSelect = True, .ItemHeight = 24, .BorderStyle = BorderStyle.None}
    Private ReadOnly _view As New ManualView()
    Private ReadOnly _timer As New Timer With {.Interval = 300}
    Private ReadOnly _font As New Font(Lang.UiFont, 10)
    Private ReadOnly _nodes As New Dictionary(Of ManualSection, TreeNode)()
    Private _syncing As Boolean
    Private _lastHits As List(Of ManualHit)

    Public Sub New(sections As List(Of ManualSection), imageFolder As String)
        _sections = sections
        Text = Lang.T("使用手冊")
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Sizable
        RoundCorners = False
        StartPosition = FormStartPosition.Manual
        ShowInTaskbar = False
        KeyPreview = True
        ClientSize = New Size(1060, 760)
        MinimumSize = New Size(640, 420)
        _view.ImageFolder = imageFolder

        For Each s In sections
            _tree.Nodes.Add(MakeNode(s))
        Next
        _tree.ExpandAll()
        If _tree.Nodes.Count > 0 Then _tree.Nodes(0).EnsureVisible()
        AddHandler _tree.AfterSelect, Sub(o, e)
                                          If _syncing OrElse e.Node Is Nothing Then Return
                                          _back.Visible = False
                                          _view.ShowSection(DirectCast(e.Node.Tag, ManualSection))
                                      End Sub
        AddHandler _view.Navigate, Sub(s, i) ShowPage(s, i, fromResults:=_view.ShowingResults)
        AddHandler _search.TextChanged, Sub()
                                            _timer.Stop()
                                            _timer.Start()
                                        End Sub
        AddHandler _search.KeyDown, Sub(o, e)
                                        If e.KeyCode = Keys.Enter Then
                                            e.SuppressKeyPress = True
                                            RunSearch()
                                        ElseIf e.KeyCode = Keys.Escape AndAlso _search.Text <> "" Then
                                            e.SuppressKeyPress = True
                                            _search.Text = ""
                                            RunSearch()
                                        End If
                                    End Sub
        AddHandler _timer.Tick, Sub() RunSearch()
        AddHandler _back.Click, Sub()
                                    If _lastHits Is Nothing Then Return
                                    _back.Visible = False
                                    _view.ShowResults(_search.Text.Trim(), _lastHits)
                                End Sub
        Controls.AddRange({_searchLabel, _search, _back, _tree, _view})
        LayoutBody()
        ThemeManager.Attach(Me)
        If sections.Count > 0 Then ShowPage(sections(0), -1)
    End Sub

    Private Function MakeNode(s As ManualSection) As TreeNode
        Dim n As New TreeNode(s.Title) With {.Tag = s}
        _nodes(s) = n
        For Each c In s.Children
            n.Nodes.Add(MakeNode(c))
        Next
        Return n
    End Function

    ''' <summary>所有章節（測試用）。</summary>
    Friend ReadOnly Property Sections As List(Of ManualSection)
        Get
            Return _sections
        End Get
    End Property

    ''' <summary>跳到某章節；從搜尋結果來的，標出那一段並顯示「◀ 搜尋結果」。</summary>
    Public Sub ShowPage(s As ManualSection, Optional blockIndex As Integer = -1, Optional fromResults As Boolean = False)
        _syncing = True
        Try
            Dim n As TreeNode = Nothing
            If _nodes.TryGetValue(s, n) Then
                _tree.SelectedNode = n
                n.EnsureVisible()
            End If
        Finally
            _syncing = False
        End Try
        _back.Visible = fromResults AndAlso _lastHits IsNot Nothing
        _view.ShowSection(s, blockIndex)
    End Sub

    ''' <summary>依標題找章節（例如從頁籤直接開到那一頁）。</summary>
    Public Function FindSection(title As String) As ManualSection
        Return _sections.SelectMany(Function(r) r.All()).FirstOrDefault(Function(s) s.Title = title)
    End Function

    Private Sub RunSearch()
        _timer.Stop()
        Dim q = _search.Text.Trim()
        If q = "" Then
            _lastHits = Nothing
            _back.Visible = False
            Dim s = If(TryCast(_tree.SelectedNode?.Tag, ManualSection), _sections.FirstOrDefault())
            If s IsNot Nothing AndAlso _view.ShowingResults Then _view.ShowSection(s)
            Return
        End If
        _lastHits = ManualSearch.Find(_sections, q)
        _back.Visible = False
        _view.ShowResults(q, _lastHits)
    End Sub

    ''' <summary>測試用：直接搜尋。</summary>
    Friend Function Search(q As String) As List(Of ManualHit)
        _search.Text = q
        RunSearch()
        Return _lastHits
    End Function

    Friend ReadOnly Property View As ManualView
        Get
            Return _view
        End Get
    End Property

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        LayoutBody()
    End Sub

    Private Sub LayoutBody()
        If _view Is Nothing Then Return ' AquaForm 的建構式會先觸發 OnResize
        Dim top = 23 + 8, w = ClientSize.Width, h = ClientSize.Height
        Const TreeW As Integer = 240
        _searchLabel.SetBounds(12, top, 44, 28)
        _search.SetBounds(58, top + 2, Math.Min(420, w - 58 - 140), 26)
        _back.SetBounds(_search.Right + 10, top, 118, 28)
        Dim body = top + 38
        _tree.SetBounds(8, body, TreeW, Math.Max(40, h - body - 8))
        _view.SetBounds(8 + TreeW + 6, body, Math.Max(80, w - TreeW - 22), Math.Max(40, h - body - 8))
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = (Keys.Control Or Keys.F) Then _search.Focus() : _search.SelectAll() : Return True
        If keyData = Keys.Escape AndAlso _search.Text = "" Then Hide() : Return True
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    ''' <summary>關閉只是隱藏：停在同一頁，下次打開還在。</summary>
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            Hide()
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _timer.Dispose()
            _font.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
