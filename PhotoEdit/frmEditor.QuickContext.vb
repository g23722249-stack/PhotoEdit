Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 快速面板（F9）下半部依目前的工具換內容：常用筆刷（平常）、修飾筆刷（模式、大小、力道）、寬度工具（寬度樣式）、
''' 選取的向量物件（再製、鏡射、外框轉物件、效果）、節點工具或鋼筆（節點操作）。面板本身見 frmEditor.QuickTools.vb。
''' </summary>
Partial Friend Class frmEditor

    Friend Enum QuickContext
        Brushes
        TouchUp
        Width
        Objects
        Nodes
    End Enum

    ''' <summary>目前該顯示哪一區。</summary>
    Friend Function QuickContextNow() As QuickContext
        Select Case _drawStrip.SelectedTool
            Case DrawIcons.TouchUpTool : Return QuickContext.TouchUp
            Case DrawIcons.WidthTool : Return QuickContext.Width
            Case DrawIcons.NodeTool, CInt(DrawShape.Path) : Return QuickContext.Nodes
            Case DrawIcons.SelectTool
                If _photo IsNot Nothing AndAlso EffectTargets().Count > 0 Then Return QuickContext.Objects
        End Select
        Return QuickContext.Brushes
    End Function

    ''' <summary>快速面板檢視、排版按鈕的開關狀態（符合視窗、100% 是動作，但目前正是那樣時也亮起）。</summary>
    Friend Function QuickToggleState(cmd As String) As Boolean
        Select Case cmd
            Case "flipview" : Return _canvas.MirrorView
            Case "rulers" : Return _appSettings.ShowRulers
            Case "grid" : Return _appSettings.ShowGrid
            Case "snapgrid" : Return _appSettings.SnapGrid
            Case "snapguides" : Return _appSettings.SnapGuides
            Case "fit" : Return _canvas.IsFit
        End Select
        Return False
    End Function

    ' ---- 修飾筆刷 ----
    Friend Property QuickTouchMode As Integer
        Get
            Return Math.Max(0, _touchMode.SelectedIndex)
        End Get
        Set(value As Integer)
            _touchMode.SelectedIndex = Math.Max(0, Math.Min(_touchMode.Items.Count - 1, value))
        End Set
    End Property

    Friend Property QuickTouchSize As Integer
        Get
            Return CInt(_touchSize.Value)
        End Get
        Set(value As Integer)
            _touchSize.Value = Math.Max(_touchSize.Minimum, Math.Min(_touchSize.Maximum, value))
        End Set
    End Property

    Friend Property QuickTouchStrength As Integer
        Get
            Return CInt(_touchStrength.Value)
        End Get
        Set(value As Integer)
            _touchStrength.Value = Math.Max(_touchStrength.Minimum, Math.Min(_touchStrength.Maximum, value))
        End Set
    End Property

    Friend ReadOnly Property TouchModeNames As String()
        Get
            Return _touchMode.Items.Cast(Of Object)().Select(Function(o) o.ToString()).ToArray()
        End Get
    End Property

    ' ---- 寬度樣式 ----
    Friend Sub QuickWidthPreset(index As Integer)
        ApplyWidthPreset(index)
    End Sub

    ''' <summary>選取的線（沒有時是之後畫的設定）用的是哪一種寬度樣式；都不是時 −1。</summary>
    Friend Function CurrentWidthPreset() As Integer
        Dim idx = SelectedIndices().Where(Function(i) DrawGeometry.CanVaryWidth(_recipe.Drawings(i))).ToList()
        Dim prof = If(idx.Count > 0, _recipe.Drawings(idx(0)).WidthProfile, _drawStyle.WidthProfile)
        Dim presets = DrawGeometry.WidthPresets()
        For k = 0 To presets.Count - 1
            Dim st = presets(k).Stops
            If (prof Is Nothing OrElse prof.Count = 0) AndAlso st.Length = 0 Then Return k
            If prof IsNot Nothing AndAlso prof.Count = st.Length AndAlso
               prof.Select(Function(s, i) Math.Abs(s.T - st(i).T) < 0.0001 AndAlso Math.Abs(s.W - st(i).W) < 0.0001).All(Function(x) x) Then Return k
        Next
        Return -1
    End Function

    ' ---- 節點 ----
    ''' <summary>節點按鈕現在能不能用（同操作列的判斷，不看它是不是收進「»」）。</summary>
    Friend Function QuickNodeAvailable(key As String) As Boolean
        If _photo Is Nothing Then Return False
        Dim sel = If(IsMultiSelected(), Nothing, SelDraw(_recipe))
        If sel Is Nothing OrElse sel.Locked Then Return False
        Dim isPath = DrawGeometry.HasNodes(sel)
        Dim editing = isPath AndAlso (NodeToolActive OrElse _drawStrip.SelectedTool = CInt(DrawShape.Path))
        Select Case key
            Case "corner", "smooth", "symmetric", "delete", "break" : Return editing AndAlso ValidNodeSel().Count > 0
            Case "close" : Return editing
            Case "join" : Return editing AndAlso JoinableEnds().HasValue
            Case "convert" : Return Not isPath AndAlso (DrawGeometry.ToPath(sel) IsNot Nothing OrElse (DrawGeometry.HasText(sel.Shape) AndAlso Not String.IsNullOrWhiteSpace(sel.Text)))
        End Select
        Return False
    End Function

    Friend Function QuickCloseText() As String
        Dim sel = SelDraw(_recipe)
        If DrawGeometry.HasNodes(sel) AndAlso sel.Contours.All(Function(c) c.Closed) Then Return Lang.T("開放")
        Return Lang.T("封閉")
    End Function

    Friend Sub QuickNodeCommand(key As String)
        NodeCommand(key)
        UpdateDrawControls()
        _canvas.Invalidate()
    End Sub

    ''' <summary>寬度樣式的小圖：沿一條橫線畫出粗細。</summary>
    Friend Shared Sub PaintWidthPreview(g As Graphics, r As RectangleF, stops As WidthStop(), col As Color)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim n = 40
        Dim prof = stops.ToList()
        Dim top As New List(Of PointF)(), bottom As New List(Of PointF)()
        For k = 0 To n
            Dim t = k / CDbl(n)
            Dim w = Math.Min(2, DrawGeometry.WidthAt(prof, t)) * r.Height * 0.22
            Dim x = CSng(r.X + t * r.Width), y = r.Y + r.Height / 2
            top.Add(New PointF(x, CSng(y - w))) : bottom.Add(New PointF(x, CSng(y + w)))
        Next
        bottom.Reverse()
        Using br As New SolidBrush(col)
            g.FillPolygon(br, top.Concat(bottom).ToArray())
        End Using
    End Sub
End Class
