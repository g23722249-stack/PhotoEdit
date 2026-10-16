Imports PhotoEdit

''' <summary>
''' 濾鏡列：左邊是分類（色調、復古／黑白、繪畫、漫畫／版畫），右邊是該分類的濾鏡（「原色」永遠在第一格）。
''' 每格是目前照片套用後的縮圖＋名稱，點一下套用；與目前配方相符的那格加框。
''' 套用的濾鏡換了而且不在目前分類時，自動切到它的分類。
''' </summary>
Friend Class PresetStrip
    Inherits Panel

    Public Const ThumbSize As Integer = 150   ' 縮圖算圖時的長邊
    Private Const TileWidth As Integer = 92
    Private Const TileHeight As Integer = 90
    Private Const CategoryWidth As Integer = 92

    Private ReadOnly _tiles As New List(Of Tile)()
    Private ReadOnly _help As New Dictionary(Of Tile, HelpTip.Entry)()
    Private ReadOnly _categoryBar As New Panel()
    Private ReadOnly _categoryButtons As New List(Of CategoryButton)()
    Private ReadOnly _scroll As New Panel()
    Private _category As String = Preset.Categories(0)
    Private _lastMatch As Preset

    Public Event PresetClicked(preset As Preset)
    ''' <summary>切換了分類（調整頁依分類換提示文字）。</summary>
    Public Event CategoryChanged As EventHandler

    ''' <summary>放在右側「調整」頁籤裡：分類是上方一排按鈕、縮圖一列左右捲動，顏色跟著面板。</summary>
    Private ReadOnly _embedded As Boolean
    Private Const EmbeddedTile As Integer = 84
    Private Const EmbeddedTileHeight As Integer = 84
    Private Const EmbeddedCategoryHeight As Integer = 28

    Private ReadOnly Property TileW As Integer
        Get
            Return If(_embedded, EmbeddedTile, TileWidth)
        End Get
    End Property

    Private ReadOnly Property TileH As Integer
        Get
            Return If(_embedded, EmbeddedTileHeight, TileHeight)
        End Get
    End Property

    Public Sub New()
        Me.New(False)
    End Sub

    Public Sub New(embedded As Boolean)
        _embedded = embedded
        BackColor = If(embedded, Color.FromArgb(236, 238, 242), Color.FromArgb(52, 52, 56))
        Height = If(embedded, EmbeddedCategoryHeight + 4 + EmbeddedTileHeight + 20, TileHeight + 26) ' 留水平捲軸的空間
        _scroll.Dock = DockStyle.Fill
        _scroll.AutoScroll = True
        _scroll.BackColor = BackColor
        If embedded Then
            _categoryBar.Dock = DockStyle.Top
            _categoryBar.Height = EmbeddedCategoryHeight + 4
            _categoryBar.BackColor = BackColor
            AddHandler _categoryBar.Resize, Sub() LayoutCategories()
        Else
            _categoryBar.Dock = DockStyle.Left
            _categoryBar.Width = CategoryWidth
            _categoryBar.BackColor = Color.FromArgb(44, 44, 48)
        End If
        For i = 0 To Preset.Categories.Length - 1
            Dim b As New CategoryButton(Preset.Categories(i), embedded)
            b.SetBounds(4, 6 + i * 26, CategoryWidth - 8, 24)
            AddHandler b.Click, Sub(s, e) ShowCategory(DirectCast(s, CategoryButton).Category)
            _categoryButtons.Add(b)
            _categoryBar.Controls.Add(b)
        Next
        For Each p In Preset.BuiltIn
            Dim t As New Tile(p, embedded)
            t.Size = New Size(TileW, TileH)
            AddHandler t.Click, Sub(s, e) RaiseEvent PresetClicked(DirectCast(s, Tile).Preset)
            _tiles.Add(t)
            _scroll.Controls.Add(t)
        Next
        ' 停靠：最後加入的最先停靠（分類列在左邊）
        Controls.Add(_scroll)
        Controls.Add(_categoryBar)
        ShowCategory(_category)
        If embedded Then LayoutCategories()
    End Sub

    ''' <summary>嵌入時分類按鈕平分一排。</summary>
    Private Sub LayoutCategories()
        If Not _embedded OrElse _categoryButtons.Count = 0 Then Return
        Dim gap = 4, w = _categoryBar.ClientSize.Width
        Dim bw = Math.Max(30, (w - gap * (_categoryButtons.Count - 1)) \ _categoryButtons.Count)
        For i = 0 To _categoryButtons.Count - 1
            _categoryButtons(i).SetBounds(i * (bw + gap), 0, If(i = _categoryButtons.Count - 1, w - i * (bw + gap), bw), EmbeddedCategoryHeight)
        Next
    End Sub

    ''' <summary>面板換配色（深色主題）時跟著。</summary>
    Protected Overrides Sub OnParentBackColorChanged(e As EventArgs)
        MyBase.OnParentBackColorChanged(e)
        If Not _embedded OrElse Parent Is Nothing Then Return
        BackColor = Parent.BackColor
        _scroll.BackColor = BackColor
        _categoryBar.BackColor = BackColor
        Invalidate(True)
    End Sub

    ''' <summary>目前顯示的分類。</summary>
    Public ReadOnly Property Category As String
        Get
            Return _category
        End Get
    End Property

    ''' <summary>只顯示「原色」與這個分類的濾鏡，由左往右排。</summary>
    Public Sub ShowCategory(category As String)
        Dim changed = category <> _category
        _category = category
        _scroll.SuspendLayout()
        _scroll.AutoScrollPosition = Point.Empty
        Dim x = 6
        For Each t In _tiles
            Dim show = String.IsNullOrEmpty(t.Preset.Category) OrElse t.Preset.Category = category
            t.Visible = show
            If show Then
                t.Location = New Point(x, If(_embedded, 0, 4))
                x += TileW + 6
            End If
        Next
        _scroll.ResumeLayout()
        For Each b In _categoryButtons
            b.Selected = b.Category = category
        Next
        If changed Then RaiseEvent CategoryChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>換上新縮圖（順序同 Preset.BuiltIn）；舊縮圖由本控制項釋放。</summary>
    Public Sub SetThumbnails(thumbs As IList(Of Bitmap))
        For i = 0 To _tiles.Count - 1
            _tiles(i).Thumbnail = If(thumbs IsNot Nothing AndAlso i < thumbs.Count, thumbs(i), Nothing)
            Dim e As HelpTip.Entry = Nothing
            If _help.TryGetValue(_tiles(i), e) Then e.Icon = _tiles(i).Thumbnail ' 說明視窗的圖示用這個濾鏡套在照片上的縮圖
        Next
    End Sub

    ''' <summary>每格掛上 HelpTexts 的「preset.名稱」說明，分類按鈕掛「preset.cat.分類」。</summary>
    Public Sub AttachHelp(help As HelpTip)
        For Each t In _tiles
            Dim e = HelpTexts.Get("preset." & t.Preset.Name)?.Clone()
            If e Is Nothing Then Continue For
            e.Icon = t.Thumbnail
            _help(t) = e
            help.SetHelp(t, e)
        Next
        For Each b In _categoryButtons
            Dim e = HelpTexts.Get("preset.cat." & b.Category)
            If e IsNot Nothing Then help.SetHelp(b, e)
        Next
    End Sub

    Public Sub UpdateSelection(recipe As EditRecipe)
        Dim match As Preset = Nothing
        For Each t In _tiles
            t.Selected = recipe IsNot Nothing AndAlso t.Preset.Matches(recipe)
            If t.Selected Then match = t.Preset
        Next
        ' 套用的濾鏡換了（例如開了一張用水彩畫的照片）：切到它的分類，看得到加框的那格
        If match IsNot _lastMatch Then
            _lastMatch = match
            If match IsNot Nothing AndAlso Not String.IsNullOrEmpty(match.Category) AndAlso match.Category <> _category Then ShowCategory(match.Category)
        End If
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then SetThumbnails(Nothing)
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>分類按鈕：選中的那個亮起來。</summary>
    Private Class CategoryButton
        Inherits Control

        Public ReadOnly Category As String
        Private ReadOnly _embedded As Boolean
        Private _selected As Boolean
        Private _hover As Boolean

        Public Sub New(category As String, embedded As Boolean)
            Me.Category = category
            _embedded = embedded
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
            Cursor = Cursors.Hand
            Font = New Font(Lang.UiFont, 9.5F)
            AccessibleRole = AccessibleRole.PageTab
            AccessibleName = category
        End Sub

        Public Property Selected As Boolean
            Get
                Return _selected
            End Get
            Set(value As Boolean)
                If _selected = value Then Return
                _selected = value
                Invalidate()
            End Set
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Parent.BackColor)
            If _embedded Then
                ' 面板裡：選中的藍底白字，其他白底細框（同面板上的按鈕）
                Dim accent = Color.FromArgb(47, 128, 237)
                Dim back = If(_selected, accent, ThemeManager.Back(If(_hover, Color.FromArgb(244, 246, 250), Color.White)))
                Using b As New SolidBrush(back), pen As New Pen(If(_selected, accent, ThemeManager.Line(Color.FromArgb(190, 196, 206))))
                    g.FillRectangle(b, 0, 0, Width - 1, Height - 1)
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1)
                End Using
                TextRenderer.DrawText(g, Lang.T(Category), Font, New Rectangle(0, 0, Width, Height),
                                      If(_selected, Color.White, ThemeManager.Fore(Color.FromArgb(40, 44, 52))),
                                      TextFormatFlags.VerticalCenter Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.EndEllipsis)
                Return
            End If
            If _selected OrElse _hover Then
                Using b As New SolidBrush(If(_selected, Color.FromArgb(78, 78, 86), Color.FromArgb(62, 62, 68)))
                    g.FillRectangle(b, ClientRectangle)
                End Using
            End If
            If _selected Then
                Using b As New SolidBrush(Color.FromArgb(255, 200, 60))
                    g.FillRectangle(b, 0, 0, 3, Height)
                End Using
            End If
            TextRenderer.DrawText(g, Lang.T(Category), Font, New Rectangle(8, 0, Width - 8, Height),
                                  If(_selected, Color.FromArgb(255, 210, 90), Color.FromArgb(210, 210, 214)),
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis)
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

    Private Class Tile
        Inherits Control

        Public ReadOnly Preset As Preset
        Private ReadOnly _embedded As Boolean
        Private _thumb As Bitmap
        Private _selected As Boolean
        Private _hover As Boolean

        Public Sub New(preset As Preset, embedded As Boolean)
            Me.Preset = preset
            _embedded = embedded
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
            ForeColor = Color.FromArgb(225, 225, 228)
            Cursor = Cursors.Hand
        End Sub

        Public Property Thumbnail As Bitmap
            Get
                Return _thumb
            End Get
            Set(value As Bitmap)
                If _thumb IsNot Nothing AndAlso _thumb IsNot value Then _thumb.Dispose()
                _thumb = value
                Invalidate()
            End Set
        End Property

        Public Property Selected As Boolean
            Get
                Return _selected
            End Get
            Set(value As Boolean)
                If _selected = value Then Return
                _selected = value
                Invalidate()
            End Set
        End Property

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

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Parent.BackColor)
            Dim box As New Rectangle(2, 2, Width - 4, Height - 24)
            If _thumb IsNot Nothing Then
                ' 縮圖填滿方框（裁掉多餘部分），各格大小一致。
                Dim s = Math.Max(box.Width / _thumb.Width, box.Height / _thumb.Height)
                Dim sw = box.Width / s, sh = box.Height / s
                Dim src As New RectangleF(CSng((_thumb.Width - sw) / 2), CSng((_thumb.Height - sh) / 2), CSng(sw), CSng(sh))
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear
                g.DrawImage(_thumb, box, src, GraphicsUnit.Pixel)
            Else
                Using br As New SolidBrush(Color.FromArgb(70, 70, 75))
                    g.FillRectangle(br, box)
                End Using
            End If
            Dim selColor = If(_embedded, Color.FromArgb(47, 128, 237), Color.FromArgb(255, 200, 60))
            If _selected OrElse _hover Then
                Using pen As New Pen(If(_selected, selColor, Color.FromArgb(160, 160, 165)), 2)
                    g.DrawRectangle(pen, 1, 1, Width - 3, Height - 23)
                End Using
            End If
            Dim textColor = If(_embedded, ThemeManager.Fore(Color.FromArgb(40, 44, 52)), ForeColor)
            TextRenderer.DrawText(g, Lang.T(Preset.Name), Font, New Rectangle(0, Height - 21, Width, 20),
                                  If(_selected, If(_embedded, selColor, Color.FromArgb(255, 210, 90)), textColor),
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then Thumbnail = Nothing
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
