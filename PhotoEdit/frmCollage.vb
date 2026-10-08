Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports PhotoEdit

''' <summary>
''' 拼貼：選版型、比例、間距、圓角與背景色，照片依序填入格子（套用各自的 .pedit.json 編輯）。
''' 在預覽上把一格拖到另一格可交換位置，在格子上按右鍵可移除。
''' </summary>
Friend Class frmCollage
    Inherits Aqua.AquaForm

    Private Const PreviewPhotoSize As Integer = 1200

    Private Class CollagePhoto
        Public Path As String
        Public Recipe As EditRecipe
        Public Preview As Bitmap
    End Class

    Private ReadOnly _photos As New List(Of CollagePhoto)()
    Private ReadOnly _settings As New CollageSettings()
    Private ReadOnly _preview As New CollagePreview()
    Private ReadOnly _layoutList As New ListBox()
    Private ReadOnly _aspect As New ComboBox()
    Private ReadOnly _spacing As New Aqua.Slider()
    Private ReadOnly _corner As New Aqua.Slider()
    Private ReadOnly _background As New Button()
    Private ReadOnly _longSide As New ComboBox()
    Private ReadOnly _status As New StatusLine()
    Private _rendered As Bitmap

    Private Shared ReadOnly Aspects As (Name As String, Value As Double)() = {
        ("3:2 橫式", 3 / 2.0), ("4:3 橫式", 4 / 3.0), ("16:9 橫式", 16 / 9.0), ("1:1 方形", 1),
        ("2:3 直式", 2 / 3.0), ("3:4 直式", 3 / 4.0), ("9:16 直式", 9 / 16.0)}
    Private Shared ReadOnly LongSides As Integer() = {2000, 3000, 4000, 6000}

    Public Sub New(initial As IEnumerable(Of (Path As String, Recipe As EditRecipe)))
        Text = "拼貼"
        Size = New Size(1180, 800)
        MinimumSize = New Size(860, 600)
        StartPosition = FormStartPosition.CenterParent
        WindowBorderStyle = Aqua.FormBorderStyle.Sizable

        Dim root As New Aqua.MenuItem()
        Dim fileMenu = root.AddItem(New Aqua.MenuItem("檔案"))
        fileMenu.AddItem(New Aqua.MenuItem("加入照片…") With {.Name = "add"})
        fileMenu.AddItem(New Aqua.MenuItem("匯出 JPG…") With {.Name = "export"})
        fileMenu.AddItem(New Aqua.MenuItem("-"))
        fileMenu.AddItem(New Aqua.MenuItem("關閉") With {.Name = "close"})
        AddMenu(root)
        AddHandler MenuSelected, Sub(s, item)
                                     Select Case item.Name
                                         Case "add" : AddPhotos()
                                         Case "export" : ExportCollage()
                                         Case "close" : Close()
                                     End Select
                                 End Sub
        Dim menuHeight = TextRenderer.MeasureText("Ag", MenuFont).Height + 6
        Padding = New Padding(4, 23 + menuHeight + 2, 4, 16)

        Dim side = BuildSidePanel()
        _preview.Dock = DockStyle.Fill
        _status.Dock = DockStyle.Bottom
        AddHandler _preview.CellsSwapped, AddressOf SwapCells
        AddHandler _preview.CellRemoveRequested, AddressOf RemoveCell
        AddHandler _preview.EmptyCellClicked, Sub() AddPhotos()
        Controls.Add(_preview)
        Controls.Add(side)
        Controls.Add(_status)

        For Each p In initial
            AddPhoto(p.Path, p.Recipe)
        Next
        ' 依照片數挑一個剛好的版型。
        Dim fit = CollageLayout.BuiltIn.FirstOrDefault(Function(l) l.Cells.Count >= Math.Max(2, _photos.Count))
        _layoutList.SelectedIndex = If(fit Is Nothing, 0, CollageLayout.BuiltIn.ToList().IndexOf(fit))
        RenderPreview()
        ThemeManager.Attach(Me)
    End Sub

    Private Function BuildSidePanel() As Panel
        Dim side As New Panel With {.Dock = DockStyle.Right, .Width = 280, .BackColor = Color.FromArgb(236, 238, 242), .Padding = New Padding(10)}
        Dim y = 8
        Dim add As New Button With {.Text = "加入照片…", .UseVisualStyleBackColor = True}
        add.SetBounds(10, y, 125, 30)
        AddHandler add.Click, Sub() AddPhotos()
        Dim clear As New Button With {.Text = "清除全部", .UseVisualStyleBackColor = True}
        clear.SetBounds(145, y, 125, 30)
        AddHandler clear.Click, Sub()
                                    For Each p In _photos
                                        p.Preview?.Dispose()
                                    Next
                                    _photos.Clear()
                                    RenderPreview()
                                End Sub
        side.Controls.AddRange(New Control() {add, clear})
        y += 42

        side.Controls.Add(Heading("版型", y)) : y += 26
        _layoutList.SetBounds(10, y, 260, 190)
        _layoutList.IntegralHeight = False
        For Each l In CollageLayout.BuiltIn
            _layoutList.Items.Add(l.Name)
        Next
        AddHandler _layoutList.SelectedIndexChanged, Sub()
                                                          If _layoutList.SelectedIndex < 0 Then Return
                                                          _settings.Layout = CollageLayout.BuiltIn(_layoutList.SelectedIndex)
                                                          RenderPreview()
                                                      End Sub
        side.Controls.Add(_layoutList)
        y += 198

        side.Controls.Add(Heading("畫布", y)) : y += 26
        side.Controls.Add(Caption("比例", y))
        _aspect.DropDownStyle = ComboBoxStyle.DropDownList
        _aspect.Items.AddRange(Aspects.Select(Function(a) CObj(a.Name)).ToArray())
        _aspect.SelectedIndex = 0
        _aspect.SetBounds(90, y, 180, 24)
        AddHandler _aspect.SelectedIndexChanged, Sub()
                                                      _settings.Aspect = Aspects(_aspect.SelectedIndex).Value
                                                      RenderPreview()
                                                  End Sub
        side.Controls.Add(_aspect)
        y += 32

        side.Controls.Add(Caption("間距", y))
        SetupSlider(_spacing, y, _settings.Spacing, Sub(v) _settings.Spacing = v)
        side.Controls.Add(_spacing)
        y += 32
        side.Controls.Add(Caption("圓角", y))
        SetupSlider(_corner, y, _settings.CornerRadius, Sub(v) _settings.CornerRadius = v)
        side.Controls.Add(_corner)
        y += 32

        side.Controls.Add(Caption("背景色", y))
        _background.SetBounds(90, y - 2, 180, 26)
        _background.BackColor = _settings.Background
        _background.Text = ""
        AddHandler _background.Click, Sub()
                                          Using dlg As New ColorDialog With {.Color = _settings.Background, .FullOpen = True}
                                              If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                                              _settings.Background = dlg.Color
                                              _background.BackColor = dlg.Color
                                              RenderPreview()
                                          End Using
                                      End Sub
        side.Controls.Add(_background)
        y += 40

        side.Controls.Add(Heading("輸出", y)) : y += 26
        side.Controls.Add(Caption("長邊", y))
        _longSide.DropDownStyle = ComboBoxStyle.DropDownList
        _longSide.Items.AddRange(LongSides.Select(Function(v) CObj(v & " 像素")).ToArray())
        _longSide.SelectedIndex = 1
        _longSide.SetBounds(90, y, 180, 24)
        side.Controls.Add(_longSide)
        y += 36
        Dim export As New Button With {.Text = "匯出 JPG…", .UseVisualStyleBackColor = True}
        export.SetBounds(10, y, 260, 34)
        AddHandler export.Click, Sub() ExportCollage()
        side.Controls.Add(export)
        y += 44

        Dim hint As New Label With {.AutoSize = False, .ForeColor = Color.FromArgb(105, 110, 120),
            .Text = "點空白格加入照片；把一格拖到另一格可交換位置；在格子上按右鍵可移除。" & vbCrLf &
                    "照片會套用各自已儲存的編輯。"}
        hint.SetBounds(10, y, 260, 70)
        side.Controls.Add(hint)
        Return side
    End Function

    Private Function Heading(text As String, y As Integer) As Label
        Dim l As New Label With {.Text = text, .AutoSize = False, .Font = New Font(Font, FontStyle.Bold), .ForeColor = Color.FromArgb(40, 70, 120)}
        l.SetBounds(10, y, 260, 20)
        Return l
    End Function

    Private Shared Function Caption(text As String, y As Integer) As Label
        Dim l As New Label With {.Text = text, .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
        l.SetBounds(10, y, 78, 24)
        Return l
    End Function

    Private Sub SetupSlider(s As Aqua.Slider, y As Integer, value As Integer, apply As Action(Of Integer))
        s.Minimum = 0 : s.Maximum = 100 : s.Value = value : s.ShowTicks = False
        s.SetBounds(90, y, 180, 24)
        AddHandler s.ValueChanged, Sub()
                                       apply(s.Value)
                                       RenderPreview()
                                   End Sub
    End Sub

    '---------------------------------------------------------------------
    ' 照片
    '---------------------------------------------------------------------

    Private Sub AddPhotos()
        Using dlg As New OpenFileDialog With {.Title = "加入照片", .Multiselect = True,
            .Filter = "圖片檔|" & String.Join(";", PhotoFile.SupportedExtensions.Select(Function(x) "*" & x))}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Cursor = Cursors.WaitCursor
            Try
                For Each f In dlg.FileNames
                    AddPhoto(f, Nothing)
                Next
            Finally
                Cursor = Cursors.Default
            End Try
        End Using
        ' 照片比格子多時換成格子夠的版型。
        If _photos.Count > _settings.Layout.Cells.Count Then
            Dim fit = CollageLayout.BuiltIn.FirstOrDefault(Function(l) l.Cells.Count >= _photos.Count)
            If fit IsNot Nothing Then _layoutList.SelectedIndex = CollageLayout.BuiltIn.ToList().IndexOf(fit)
        End If
        RenderPreview()
    End Sub

    ''' <param name="recipe">Nothing 時讀取照片的 .pedit.json。</param>
    Private Sub AddPhoto(path As String, recipe As EditRecipe)
        Try
            Dim r = If(recipe, If(RecipeStore.Load(path), New EditRecipe()))
            Dim p As New CollagePhoto With {.Path = path, .Recipe = r}
            p.Preview = RenderPhoto(p, PreviewPhotoSize)
            _photos.Add(p)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse
                                   TypeOf ex Is ExternalException
            MessageBox.Show(Me, $"無法加入「{IO.Path.GetFileName(path)}」：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ''' <summary>以照片的編輯配方算圖（修補、降噪會套用；人臉相關效果因沒有偵測而略過）。</summary>
    Private Shared Function RenderPhoto(p As CollagePhoto, maxDimension As Integer) As Bitmap
        Using photo = PhotoFile.Open(p.Path)
            Dim recipe = p.Recipe
            Dim prepare As Func(Of Bitmap, Bitmap) = Nothing
            ' 有去背時讀回已儲存的遮罩。
            Dim mask = If(recipe.Cutout IsNot Nothing, MaskStore.Load(p.Path, recipe.Cutout.Model), Nothing)
            Try
                If recipe.HasSourceFix Then prepare = Function(b As Bitmap) SourceFix.Apply(b, Array.Empty(Of FaceRegion)(), recipe, mask)
                Return ImagePipeline.Render(photo.Image, recipe, maxDimension, prepare)
            Finally
                mask?.Dispose()
            End Try
        End Using
    End Function

    Private Sub SwapCells(a As Integer, b As Integer)
        If a < 0 OrElse b < 0 OrElse a >= _photos.Count OrElse b >= _photos.Count OrElse a = b Then Return
        Dim t = _photos(a) : _photos(a) = _photos(b) : _photos(b) = t
        RenderPreview()
    End Sub

    Private Sub RemoveCell(index As Integer)
        If index < 0 OrElse index >= _photos.Count Then Return
        _photos(index).Preview?.Dispose()
        _photos.RemoveAt(index)
        RenderPreview()
    End Sub

    '---------------------------------------------------------------------
    ' 預覽與匯出
    '---------------------------------------------------------------------

    Private Sub RenderPreview()
        Dim size = CollageRenderer.CanvasSize(_settings, 1400)
        Dim bmp = CollageRenderer.Render(_photos.Select(Function(p) p.Preview).ToList(), _settings, 1400, drawPlaceholders:=True)
        _preview.SetImage(bmp, CollageRenderer.CellRects(_settings, size), _photos.Count)
        _rendered?.Dispose()
        _rendered = bmp
        Dim used = Math.Min(_photos.Count, _settings.Layout.Cells.Count)
        _status.Text = $"{_settings.Layout.Name}　｜　已放 {used} / {_settings.Layout.Cells.Count} 格" &
                       If(_photos.Count > _settings.Layout.Cells.Count, $"（另有 {_photos.Count - _settings.Layout.Cells.Count} 張放不下）", "")
    End Sub

    Private Sub ExportCollage()
        If _photos.Count = 0 Then
            MessageBox.Show(Me, "請先加入照片。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using dlg As New SaveFileDialog With {.Title = "匯出拼貼", .Filter = "JPEG 圖片|*.jpg", .FileName = "拼貼.jpg"}
            dlg.InitialDirectory = IO.Path.GetDirectoryName(_photos(0).Path)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim longSide = LongSides(_longSide.SelectedIndex)
            Cursor = Cursors.WaitCursor
            Dim full As New List(Of Bitmap)()
            Try
                ' 每張照片以輸出長邊重新算圖，畫質才夠。
                For Each p In _photos.Take(_settings.Layout.Cells.Count)
                    full.Add(RenderPhoto(p, longSide))
                Next
                Using collage = CollageRenderer.Render(full, _settings, longSide), output As New Bitmap(collage.Width, collage.Height, PixelFormat.Format24bppRgb)
                    Using g = Graphics.FromImage(output)
                        g.DrawImageUnscaled(collage, 0, 0)
                    End Using
                    Dim codec = ImageCodecInfo.GetImageEncoders().First(Function(c) c.FormatID = ImageFormat.Jpeg.Guid)
                    Using ep As New EncoderParameters(1)
                        ep.Param(0) = New EncoderParameter(Encoder.Quality, 92L)
                        output.Save(dlg.FileName, codec, ep)
                    End Using
                End Using
                _status.Text = "已匯出：" & dlg.FileName
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ExternalException
                MessageBox.Show(Me, "匯出失敗：" & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Finally
                For Each b In full
                    b.Dispose()
                Next
                Cursor = Cursors.Default
            End Try
        End Using
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        For Each p In _photos
            p.Preview?.Dispose()
        Next
        _preview.SetImage(Nothing, Nothing, 0)
        _rendered?.Dispose()
        MyBase.OnFormClosed(e)
    End Sub

    ''' <summary>拼貼預覽：等比縮放置中，可拖曳交換格子、右鍵移除。</summary>
    Private Class CollagePreview
        Inherits Control

        Private _image As Bitmap
        Private _cells As List(Of RectangleF)
        Private _filled As Integer
        Private _dragFrom As Integer = -1
        Private _hover As Integer = -1

        Public Event CellsSwapped(a As Integer, b As Integer)
        Public Event CellRemoveRequested(index As Integer)
        Public Event EmptyCellClicked()

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                     ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            BackColor = Color.FromArgb(52, 52, 56)
        End Sub

        Public Sub SetImage(image As Bitmap, cells As List(Of RectangleF), filled As Integer)
            _image = image
            _cells = cells
            _filled = filled
            Invalidate()
        End Sub

        Private Function ImageArea() As RectangleF
            If _image Is Nothing Then Return RectangleF.Empty
            Dim s = Math.Min((Width - 40) / CDbl(_image.Width), (Height - 40) / CDbl(_image.Height))
            Dim w = CSng(_image.Width * s), h = CSng(_image.Height * s)
            Return New RectangleF((Width - w) / 2, (Height - h) / 2, w, h)
        End Function

        Private Function CellAt(p As Point) As Integer
            If _image Is Nothing OrElse _cells Is Nothing Then Return -1
            Dim b = ImageArea()
            Dim s = b.Width / _image.Width
            For i = 0 To _cells.Count - 1
                Dim c = _cells(i)
                If New RectangleF(b.X + c.X * s, b.Y + c.Y * s, c.Width * s, c.Height * s).Contains(p) Then Return i
            Next
            Return -1
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            e.Graphics.Clear(BackColor)
            If _image Is Nothing Then Return
            Dim b = ImageArea()
            e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear
            e.Graphics.DrawImage(_image, b)
            Dim s = b.Width / _image.Width
            For Each i In {_dragFrom, _hover}
                If i < 0 OrElse _cells Is Nothing OrElse i >= _cells.Count Then Continue For
                Dim c = _cells(i)
                Using pen As New Pen(If(i = _dragFrom, Color.FromArgb(255, 200, 60), Color.FromArgb(200, 255, 255, 255)), 3)
                    e.Graphics.DrawRectangle(pen, b.X + c.X * s, b.Y + c.Y * s, c.Width * s, c.Height * s)
                End Using
            Next
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Dim i = CellAt(e.Location)
            If i < 0 Then Return
            If e.Button = MouseButtons.Right Then
                If i < _filled Then RaiseEvent CellRemoveRequested(i)
            ElseIf i >= _filled Then
                RaiseEvent EmptyCellClicked()
            Else
                _dragFrom = i
                Capture = True
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim i = CellAt(e.Location)
            If i <> _hover Then
                _hover = i
                Invalidate()
            End If
            Cursor = If(i < 0, Cursors.Default, If(i < _filled, Cursors.SizeAll, Cursors.Hand))
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            If _dragFrom < 0 Then Return
            Dim target = CellAt(e.Location)
            Dim from = _dragFrom
            _dragFrom = -1
            Capture = False
            Invalidate()
            If target >= 0 AndAlso target <> from AndAlso target < _filled Then RaiseEvent CellsSwapped(from, target)
        End Sub
    End Class
End Class
