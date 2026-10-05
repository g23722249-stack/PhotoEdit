Imports PhotoEdit

''' <summary>濾鏡列：每個預設集一格（目前照片套用後的縮圖 + 名稱），點一下套用；與目前配方相符的那格加框。</summary>
Friend Class PresetStrip
    Inherits Panel

    Public Const ThumbSize As Integer = 150   ' 縮圖算圖時的長邊
    Private Const TileWidth As Integer = 92
    Private Const TileHeight As Integer = 90

    Private ReadOnly _tiles As New List(Of Tile)()
    Private ReadOnly _help As New Dictionary(Of Tile, HelpTip.Entry)()

    Public Event PresetClicked(preset As Preset)

    Public Sub New()
        AutoScroll = True
        BackColor = Color.FromArgb(52, 52, 56)
        Height = TileHeight + 26  ' 留水平捲軸的空間
        Dim x = 6
        For Each p In Preset.BuiltIn
            Dim t As New Tile(p)
            t.SetBounds(x, 4, TileWidth, TileHeight)
            AddHandler t.Click, Sub(s, e) RaiseEvent PresetClicked(DirectCast(s, Tile).Preset)
            _tiles.Add(t)
            Controls.Add(t)
            x += TileWidth + 6
        Next
    End Sub

    ''' <summary>換上新縮圖（順序同 Preset.BuiltIn）；舊縮圖由本控制項釋放。</summary>
    Public Sub SetThumbnails(thumbs As IList(Of Bitmap))
        For i = 0 To _tiles.Count - 1
            _tiles(i).Thumbnail = If(thumbs IsNot Nothing AndAlso i < thumbs.Count, thumbs(i), Nothing)
            Dim e As HelpTip.Entry = Nothing
            If _help.TryGetValue(_tiles(i), e) Then e.Icon = _tiles(i).Thumbnail ' 說明視窗的圖示用這個濾鏡套在照片上的縮圖
        Next
    End Sub

    ''' <summary>每格掛上 HelpTexts 的「preset.名稱」說明。</summary>
    Public Sub AttachHelp(help As HelpTip)
        For Each t In _tiles
            Dim e = HelpTexts.Get("preset." & t.Preset.Name)?.Clone()
            If e Is Nothing Then Continue For
            e.Icon = t.Thumbnail
            _help(t) = e
            help.SetHelp(t, e)
        Next
    End Sub

    Public Sub UpdateSelection(recipe As EditRecipe)
        For Each t In _tiles
            t.Selected = recipe IsNot Nothing AndAlso t.Preset.Matches(recipe)
        Next
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then SetThumbnails(Nothing)
        MyBase.Dispose(disposing)
    End Sub

    Private Class Tile
        Inherits Control

        Public ReadOnly Preset As Preset
        Private _thumb As Bitmap
        Private _selected As Boolean
        Private _hover As Boolean

        Public Sub New(preset As Preset)
            Me.Preset = preset
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
            If _selected OrElse _hover Then
                Using pen As New Pen(If(_selected, Color.FromArgb(255, 200, 60), Color.FromArgb(160, 160, 165)), 2)
                    g.DrawRectangle(pen, 1, 1, Width - 3, Height - 23)
                End Using
            End If
            TextRenderer.DrawText(g, Preset.Name, Font, New Rectangle(0, Height - 21, Width, 20),
                                  If(_selected, Color.FromArgb(255, 210, 90), ForeColor),
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then Thumbnail = Nothing
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
