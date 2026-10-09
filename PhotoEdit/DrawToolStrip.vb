Imports PhotoEdit

''' <summary>畫布左側的繪圖工具列：選取、直接繪製＋18 種向量工具，兩欄排列；只在「繪圖」分頁顯示。</summary>
Friend Class DrawToolStrip
    Inherits Panel

    Private Const ButtonSize As Integer = 30
    Private Const Gap As Integer = 3

    Private ReadOnly _buttons As New List(Of ToolButton)()
    Private _selected As Integer = DrawIcons.SelectTool

    Public Event ToolSelected(tool As Integer)

    Public Sub New()
        BackColor = Color.FromArgb(236, 238, 242)
        Width = Gap * 3 + ButtonSize * 2 + 8
        Padding = New Padding(4, 6, 4, 6)
        ' 直接繪製、油漆桶、漸層排在向量的自由繪製前面，其餘依 DrawShape 順序。
        Dim pixelTools = {CInt(DrawShape.Raster), CInt(DrawShape.Bucket), CInt(DrawShape.Gradient)}
        Dim notTools = {CInt(DrawShape.FillLayer)} ' 填滿圖層從圖層區的「填滿…」新增，不是畫的工具
        Dim tools = {DrawIcons.SelectTool}.Concat(pixelTools).Concat(Enumerable.Range(0, DrawGeometry.ShapeNames.Length).Where(Function(t) Not pixelTools.Contains(t) AndAlso Not notTools.Contains(t))).ToArray()
        For i = 0 To tools.Length - 1
            Dim b As New ToolButton(tools(i))
            b.SetBounds(4 + Gap + (i Mod 2) * (ButtonSize + Gap), 6 + (i \ 2) * (ButtonSize + Gap), ButtonSize, ButtonSize)
            AddHandler b.Click, Sub(s, e) SelectedTool = DirectCast(s, ToolButton).Tool
            _buttons.Add(b)
            Controls.Add(b)
        Next
        UpdateButtons()
    End Sub

    Public ReadOnly Property Buttons As IReadOnlyList(Of Control)
        Get
            Return _buttons
        End Get
    End Property

    ''' <summary>目前的工具（-1 選取，其餘為 DrawShape）。</summary>
    Public Property SelectedTool As Integer
        Get
            Return _selected
        End Get
        Set(value As Integer)
            If _selected = value Then Return
            _selected = value
            UpdateButtons()
            RaiseEvent ToolSelected(value)
        End Set
    End Property

    Private Sub UpdateButtons()
        For Each b In _buttons
            b.Selected = b.Tool = _selected
        Next
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Using pen As New Pen(ThemeManager.Line(Color.FromArgb(200, 205, 214)))
            e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height)
        End Using
    End Sub

    Friend Class ToolButton
        Inherits Control

        Public ReadOnly Tool As Integer
        Private _selected As Boolean
        Private _hover As Boolean

        Public Sub New(tool As Integer)
            Me.Tool = tool
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
            Cursor = Cursors.Hand
            AccessibleName = If(tool = DrawIcons.SelectTool, "選取", DrawGeometry.ShapeNames(tool))
            AccessibleRole = AccessibleRole.RadioButton
        End Sub

        Public Property Selected As Boolean
            Get
                Return _selected
            End Get
            Set(value As Boolean)
                _selected = value
                Invalidate()
            End Set
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Parent.BackColor)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Dim r = New Rectangle(0, 0, Width - 1, Height - 1)
            If _selected OrElse _hover Then
                Using br As New SolidBrush(ThemeManager.Back(If(_selected, Color.FromArgb(210, 228, 250), Color.FromArgb(226, 231, 238)))),
                      pen As New Pen(If(_selected, Color.FromArgb(90, 140, 220), ThemeManager.Line(Color.FromArgb(190, 198, 210))))
                    g.FillRectangle(br, r)
                    g.DrawRectangle(pen, r)
                End Using
            End If
            Dim iconColor = ThemeManager.Fore(If(_selected, Color.FromArgb(24, 95, 165), Color.FromArgb(50, 54, 62)))
            DrawIcons.DrawTool(g, Tool, New RectangleF(4, 4, Width - 8, Height - 8), iconColor)
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            _hover = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = False
            Invalidate()
        End Sub
    End Class
End Class
