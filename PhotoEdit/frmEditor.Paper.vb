Imports PhotoEdit

''' <summary>
''' 「效果」分頁的紙張與表面紋理（類似 Corel Painter 的 Paper）：
''' 選紙張（每格是紙紋的小樣張）、紋路大小／深淺／旋轉／反轉、匯入自訂紙紋；
''' 表面紋理把紙紋壓印成光影，作用在整張照片或只有繪圖。
''' 紙張是整份文件共用的：鉛筆、炭筆、粉筆、蠟筆、乾刷、水彩會吃紙紋（繪圖「進階」的紙紋吃色），藝術風格也用它。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _paperGrid As New PaperGrid()
    Private ReadOnly _paperInvert As New CheckBox()
    Private ReadOnly _surfaceTarget As New ComboBox()

    Private Sub BuildPaperSection(L As PageLayout)
        L.Y += 6
        AddHeading(L, "紙張與表面紋理")
        _paperGrid.SetBounds(8, L.Y, L.Width - 8, _paperGrid.HeightFor(L.Width - 8))
        AddHandler _paperGrid.KindClicked, AddressOf OnPaperClicked
        _help.SetHelp("paper.grid", _paperGrid)
        L.Add(_paperGrid)
        L.Y += _paperGrid.Height + 6

        Dim pct As Func(Of Integer, String) = Function(v) v & "%"
        AddRow(L, MakeRow("paperscale", "紙紋大小", 25, 400, pct, Function(r) If(r.Paper?.Scale, 100), Sub(r, v) EnsurePaper(r).Scale = v))
        AddRow(L, MakeRow("paperdepth", "紙紋深淺", 0, 100, AddressOf Plain, Function(r) If(r.Paper?.Contrast, 50), Sub(r, v) EnsurePaper(r).Contrast = v))
        AddRow(L, MakeRow("paperrot", "紙紋旋轉", 0, 359, Function(v) v & "°", Function(r) If(r.Paper?.Rotation, 0), Sub(r, v) EnsurePaper(r).Rotation = v))

        Dim half = (L.Width - 8 - 6) \ 2
        _paperInvert.Text = "凹凸反轉"
        _paperInvert.BackColor = Color.Transparent
        _paperInvert.SetBounds(8, L.Y + 4, half, 24)
        _help.SetHelp("paper.invert", _paperInvert)
        AddHandler _paperInvert.CheckedChanged, Sub() If Not _syncing Then ChangePaper(Sub(p) p.Invert = _paperInvert.Checked)
        L.Add(_paperInvert)
        Dim import = MakeButton("匯入紙紋…", "paper.import")
        import.SetBounds(8 + half + 6, L.Y + 1, half, 28)
        AddHandler import.Click, Sub() ImportPaper()
        L.Add(import)
        L.Y += 34

        AddRow(L, MakeRow("surface", "表面紋理", 0, 100, AddressOf Plain, Function(r) If(r.Paper?.SurfaceStrength, 0), Sub(r, v) EnsurePaper(r).SurfaceStrength = v))
        _help.SetHelpLinked("paper.target", _surfaceTarget, AddCaption(L, "作用在", L.Y), _surfaceTarget)
        _surfaceTarget.DropDownStyle = ComboBoxStyle.DropDownList
        _surfaceTarget.Items.AddRange({"整張照片（照片、文字、貼圖、繪圖）", "只有繪圖（照片本身不變）"})
        _surfaceTarget.SetBounds(8 + CaptionWidth, L.Y + 3, L.Width - CaptionWidth - 8, 24)
        AddHandler _surfaceTarget.SelectedIndexChanged, Sub()
                                                           If _syncing OrElse _surfaceTarget.SelectedIndex < 0 Then Return
                                                           Dim t = CType(_surfaceTarget.SelectedIndex, SurfaceTarget)
                                                           ChangePaper(Sub(p) p.SurfaceTarget = t)
                                                       End Sub
        L.Add(_surfaceTarget)
        L.Y += RowHeight
        AddRow(L, MakeRow("lightangle", "光源方向", 0, 359, Function(v) v & "°", Function(r) If(r.Paper?.LightAngle, 135), Sub(r, v) EnsurePaper(r).LightAngle = v))
        AddHint(L, "紙張是整份文件共用的：鉛筆、炭筆、粉筆、蠟筆、乾刷、水彩會吃紙紋（輕畫只碰到凸起），" &
                   "其他筆刷輕微受影響；繪圖「進階」的紙紋吃色可調。藝術風格的紙紋也用這張紙。")
    End Sub

    ''' <summary>配方沒有紙張時建一張素描紙（調紋路或表面紋理時用）。</summary>
    Private Shared Function EnsurePaper(r As EditRecipe) As PaperSettings
        If r.Paper Is Nothing Then r.Paper = New PaperSettings With {.Kind = PaperKind.Sketch}
        Return r.Paper
    End Function

    Private Sub ChangePaper(change As Action(Of PaperSettings))
        If _photo Is Nothing Then Return
        ApplyChange(Sub(r) change(EnsurePaper(r)))
    End Sub

    ''' <summary>點了一種紙：-1 為「無」（不用紙張）；自訂還沒有圖片時先選圖。</summary>
    Private Sub OnPaperClicked(kind As Integer)
        If _photo Is Nothing Then Return
        If kind < 0 Then
            ApplyChange(Sub(r) r.Paper = Nothing)
            Return
        End If
        If kind = PaperKind.Custom AndAlso String.IsNullOrEmpty(_recipe.Paper?.CustomPath) Then
            ImportPaper()
            Return
        End If
        ChangePaper(Sub(p) p.Kind = CType(kind, PaperKind))
    End Sub

    ''' <summary>匯入一張圖片當紙紋（灰階，亮的地方是凸起；會自動平鋪）。</summary>
    Private Sub ImportPaper()
        If _photo Is Nothing Then Return
        Using dlg As New OpenFileDialog With {.Title = "選擇紙紋圖片（亮 = 凸起，會平鋪）",
                                              .Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有檔案|*.*"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim path = dlg.FileName
            ChangePaper(Sub(p)
                            p.Kind = PaperKind.Custom
                            p.CustomPath = path
                        End Sub)
            _paperGrid.SetCustomPreview(path)
        End Using
    End Sub

    Private Sub SyncPaperControls()
        Dim p = _recipe.Paper
        _paperGrid.SelectedKind = If(p Is Nothing, -1, CInt(p.Kind))
        _paperInvert.Checked = p IsNot Nothing AndAlso p.Invert
        _surfaceTarget.SelectedIndex = If(p Is Nothing, 0, CInt(p.SurfaceTarget))
        If p IsNot Nothing AndAlso p.Kind = PaperKind.Custom Then _paperGrid.SetCustomPreview(p.CustomPath)
    End Sub

    '=====================================================================
    ' 紙張格子
    '=====================================================================

    ''' <summary>紙張選擇：「無」＋17 種紙張與材質，每格是紙紋打光後的小樣張＋名稱。</summary>
    Friend Class PaperGrid
        Inherits Control

        Private Const Cols As Integer = 3
        Private Const TileH As Integer = 58
        Private Const GapPx As Integer = 4

        Public Event KindClicked(kind As Integer)

        Private ReadOnly _thumbs As New Dictionary(Of Integer, Bitmap)()
        Private _selected As Integer = -1
        Private _hover As Integer = Integer.MinValue
        Private _customPath As String

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Font = New Font("Microsoft JhengHei UI", 8.5F)
            Cursor = Cursors.Hand
        End Sub

        Private ReadOnly Property Count As Integer
            Get
                Return Papers.Names.Length + 1 ' 第一格「無」
            End Get
        End Property

        Public Function HeightFor(width As Integer) As Integer
            Dim rows = (Count + Cols - 1) \ Cols
            Return rows * (TileH + GapPx)
        End Function

        Public Property SelectedKind As Integer
            Get
                Return _selected
            End Get
            Set(value As Integer)
                If _selected = value Then Return
                _selected = value
                Invalidate()
            End Set
        End Property

        Public Sub SetCustomPreview(path As String)
            If path = _customPath Then Return
            _customPath = path
            Dim old As Bitmap = Nothing
            If _thumbs.TryGetValue(PaperKind.Custom, old) Then old.Dispose() : _thumbs.Remove(PaperKind.Custom)
            Invalidate()
        End Sub

        Private Function TileRect(index As Integer) As Rectangle
            Dim w = (Width - GapPx * (Cols - 1)) \ Cols
            Return New Rectangle((index Mod Cols) * (w + GapPx), (index \ Cols) * (TileH + GapPx), w, TileH)
        End Function

        ''' <summary>紙紋小樣張：紙的底色依高度打光（左上來光）。</summary>
        Private Function Thumb(kind As Integer, w As Integer, h As Integer) As Bitmap
            Dim bmp As Bitmap = Nothing
            If _thumbs.TryGetValue(kind, bmp) AndAlso bmp.Width = w AndAlso bmp.Height = h Then Return bmp
            bmp?.Dispose()
            Dim settings As New PaperSettings With {.Kind = CType(kind, PaperKind), .CustomPath = _customPath}
            ' 用 1000 像素高的照片看到的紋路大小
            Dim map = Papers.HeightMap(settings, w, 1000)
            Dim baseC = Papers.BaseColors(kind)
            bmp = New Bitmap(w, h)
            Dim tmp As New Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(tmp)
                g.Clear(baseC)
            End Using
            Papers.ApplySurface(tmp, map, w, 1000, 0, 0, 100, 135)
            Using g = Graphics.FromImage(bmp)
                g.DrawImage(tmp, 0, 0)
            End Using
            tmp.Dispose()
            _thumbs(kind) = bmp
            Return bmp
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Parent.BackColor)
            For i = 0 To Count - 1
                Dim kind = i - 1
                Dim r = TileRect(i)
                Dim img = New Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 20)
                If kind < 0 Then
                    Using b As New SolidBrush(ThemeManager.Back(Color.White))
                        g.FillRectangle(b, img)
                    End Using
                    TextRenderer.DrawText(g, "內建紋路", Font, img, ThemeManager.Fore(Color.FromArgb(120, 126, 136)),
                                          TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                Else
                    g.DrawImage(Thumb(kind, img.Width, img.Height), img.Location)
                End If
                Dim name = If(kind < 0, "無", Papers.Names(kind))
                Dim sel = kind = _selected
                TextRenderer.DrawText(g, name, Font, New Rectangle(r.X, r.Bottom - 18, r.Width, 18),
                                      ThemeManager.Fore(If(sel, Color.FromArgb(24, 95, 165), Color.FromArgb(70, 76, 88))),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
                Using p As New Pen(If(sel, Color.FromArgb(55, 138, 221), If(i - 1 = _hover, Color.FromArgb(150, 170, 200), ThemeManager.Line(Color.FromArgb(205, 210, 218)))), If(sel, 2, 1))
                    g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1)
                End Using
            Next
        End Sub

        Private Function HitIndex(p As Point) As Integer
            For i = 0 To Count - 1
                If TileRect(i).Contains(p) Then Return i
            Next
            Return -1
        End Function

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            Dim i = HitIndex(e.Location)
            If i >= 0 Then RaiseEvent KindClicked(i - 1)
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim i = HitIndex(e.Location)
            Dim k = If(i < 0, Integer.MinValue, i - 1)
            If k <> _hover Then _hover = k : Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = Integer.MinValue
            Invalidate()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                For Each b In _thumbs.Values
                    b.Dispose()
                Next
                _thumbs.Clear()
            End If
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
