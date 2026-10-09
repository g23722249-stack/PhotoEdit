Imports System.Drawing.Drawing2D
Imports PhotoEdit

Partial Friend Class frmEditor

    ''' <summary>
    ''' 輪盤「更多…」：全部 21 種筆刷依分類排成格子，點一下換筆刷。
    ''' 紋理筆、特效筆、粒子筆、貼圖噴槍點了之後再選效果（材質、特效、粒子種類、貼圖主題）。
    ''' 點外面、Esc 或 × 關閉。
    ''' </summary>
    Friend NotInheritable Class BrushBrowser
        Inherits Form

        Private Const HeaderH As Integer = 34
        Private Const W As Integer = 456
        Private Shared ReadOnly Groups As (Name As String, Brushes As BrushKind())() = {
            ("基本", {BrushKind.HardRound, BrushKind.SoftRound, BrushKind.Pencil, BrushKind.Charcoal, BrushKind.Chalk, BrushKind.Crayon, BrushKind.Airbrush}),
            ("繪畫", {BrushKind.Watercolor, BrushKind.OilPaint, BrushKind.Acrylic, BrushKind.Ink, BrushKind.ChineseBrush, BrushKind.Marker, BrushKind.DryBrush}),
            ("Painter 筆（讀取畫布顏色）", {BrushKind.Mixer, BrushKind.Smudge, BrushKind.Clone}),
            ("特殊（點了再選效果）", {BrushKind.Texture, BrushKind.FX, BrushKind.Particle, BrushKind.StickerHose})}
        Private ReadOnly _ed As frmEditor
        Private ReadOnly _font As New Font("Microsoft JhengHei UI", 9.5F)
        Private ReadOnly _main As New Panel()
        Private ReadOnly _sub As New Panel With {.Visible = False}
        Private ReadOnly _tiles As New List(Of BrushChip)()
        Private ReadOnly _labels As New List(Of Label)()
        Private ReadOnly _back As New Button With {.Text = "◀ 全部筆刷", .FlatStyle = FlatStyle.Flat}
        Private ReadOnly _cats As New FlowLayoutPanel With {.WrapContents = True}
        Private ReadOnly _items As New FlowLayoutPanel With {.WrapContents = True, .AutoScroll = True}
        Private _subBrush As BrushKind
        Private _subCats As EffectCatalog.Category()
        Private _subCat As Integer
        Private _drag As Point?

        Public Sub New(ed As frmEditor)
            _ed = ed
            Owner = ed
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            KeyPreview = True
            Font = _font
            DoubleBuffered = True
            Tag = ThemeManager.SkipTreeTag

            Dim y = 8
            For Each grp In Groups
                Dim lbl As New Label With {.Text = grp.Name, .AutoSize = False}
                lbl.SetBounds(16, y, W - 32, 20)
                _labels.Add(lbl)
                _main.Controls.Add(lbl)
                y += 22
                For k = 0 To grp.Brushes.Length - 1
                    Dim b = grp.Brushes(k)
                    Dim t As New BrushChip()
                    t.SetBounds(16 + (k Mod 4) * 106, y + (k \ 4) * 58, 100, 52)
                    t.Brush = b
                    AddHandler t.MouseUp, Sub(s, e) If e.Button = MouseButtons.Left Then Choose(b)
                    _tiles.Add(t)
                    _main.Controls.Add(t)
                Next
                y += ((grp.Brushes.Length + 3) \ 4) * 58 + 4
            Next
            ClientSize = New Size(W, HeaderH + y + 26)
            _main.SetBounds(0, HeaderH, W, y)
            _sub.SetBounds(0, HeaderH, W, y)

            ' 效果頁：返回、分類按鈕、項目按鈕
            _back.SetBounds(16, 6, 110, 28)
            _back.FlatAppearance.BorderSize = 1
            AddHandler _back.Click, Sub() ShowMain()
            _cats.SetBounds(12, 40, W - 24, 64)
            _items.SetBounds(12, 108, W - 24, y - 112)
            _sub.Controls.AddRange({_back, _cats, _items})
            Controls.AddRange({_main, _sub})
        End Sub

        Public Sub ShowAt(p As Point)
            ShowMain()
            Dim wa = Screen.FromPoint(p).WorkingArea
            Location = New Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, p.X - Width \ 2)), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, p.Y - Height \ 2)))
            Show(_ed)
            Activate()
        End Sub

        ''' <summary>選了一種筆刷：換上去；有效果清單的筆刷接著選效果，其他直接關閉。</summary>
        Private Sub Choose(b As BrushKind)
            _ed.UseQuickBrush(b)
            Dim cats = _ed.QuickEffectCategories(b)
            If cats Is Nothing Then Hide() : Return
            ShowSub(b, cats)
        End Sub

        Private Sub ShowMain()
            _sub.Visible = False
            _main.Visible = True
            Dim col = _ed.ForegroundColor
            If ThemeManager.Dark AndAlso col.GetBrightness() < 0.25 Then col = Color.FromArgb(210, 214, 222)
            For Each t In _tiles
                t.SetBrush(t.Brush, _ed.CurrentQuickTool() = QuickTool.Brush AndAlso _ed.CurrentBrush = t.Brush, col)
            Next
            ApplyColors()
            Invalidate()
        End Sub

        Private Sub ShowSub(b As BrushKind, cats As EffectCatalog.Category())
            _subBrush = b
            _subCats = cats
            _subCat = Math.Max(0, EffectCatalog.Locate(cats, _ed.QuickEffectValue()).Category)
            _cats.Controls.Clear()
            For i = 0 To cats.Length - 1
                Dim index = i
                Dim cb As New Button With {.Text = cats(i).Name, .FlatStyle = FlatStyle.Flat, .AutoSize = True, .Height = 26, .Margin = New Padding(4, 2, 4, 2)}
                AddHandler cb.Click, Sub()
                                         _subCat = index
                                         FillItems()
                                     End Sub
                _cats.Controls.Add(cb)
            Next
            _main.Visible = False
            _sub.Visible = True
            FillItems()
            Invalidate()
        End Sub

        ''' <summary>列出目前分類的項目；目前用的那個是藍底。</summary>
        Private Sub FillItems()
            _items.SuspendLayout()
            For Each c As Control In _items.Controls
                c.Dispose()
            Next
            _items.Controls.Clear()
            Dim current = _ed.QuickEffectValue()
            Dim curCat = EffectCatalog.Locate(_subCats, current).Category
            For Each entry In _subCats(_subCat).Items
                Dim en = entry
                Dim ib As New Button With {.Text = en.Name, .FlatStyle = FlatStyle.Flat, .AutoSize = True, .MinimumSize = New Size(96, 30), .Margin = New Padding(4)}
                ib.Tag = en.Value = current AndAlso curCat = _subCat
                AddHandler ib.Click, Sub()
                                         _ed.QuickApplyEffect(en)
                                         Hide()
                                     End Sub
                _items.Controls.Add(ib)
            Next
            _items.ResumeLayout()
            ApplyColors()
        End Sub

        Private Sub ApplyColors()
            BackColor = ThemeManager.Back(Color.FromArgb(246, 247, 249))
            ForeColor = ThemeManager.Fore(Color.FromArgb(40, 44, 52))
            Dim accent = Color.FromArgb(47, 128, 237)
            For Each p In {_main, _sub, _cats, _items}
                p.BackColor = BackColor
            Next
            For Each l In _labels
                l.ForeColor = ThemeManager.Fore(Color.FromArgb(105, 110, 120))
                l.BackColor = BackColor
            Next
            Dim style = Sub(b As Button, active As Boolean)
                            b.BackColor = If(active, accent, If(ThemeManager.Dark, ThemeManager.ButtonBack, Color.White))
                            b.ForeColor = If(active, Color.White, ForeColor)
                            b.FlatAppearance.BorderColor = If(active, accent, ThemeManager.Line(Color.FromArgb(200, 205, 214)))
                        End Sub
            style(_back, False)
            For i = 0 To _cats.Controls.Count - 1
                style(CType(_cats.Controls(i), Button), i = _subCat)
            Next
            For Each c As Control In _items.Controls
                style(CType(c, Button), TypeOf c.Tag Is Boolean AndAlso CBool(c.Tag))
            Next
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using head As New SolidBrush(ThemeManager.Back(Color.FromArgb(232, 235, 241)))
                g.FillRectangle(head, 0, 0, Width, HeaderH)
            End Using
            Dim title = If(_sub.Visible, $"{DrawGeometry.BrushNames(CInt(_subBrush))}：選效果", "全部筆刷")
            Using bold As New Font(_font, FontStyle.Bold), fg As New SolidBrush(ForeColor), small As New Font(_font.FontFamily, 8.5F),
                  s2 As New SolidBrush(ThemeManager.Fore(Color.FromArgb(105, 110, 120)))
                g.DrawString(title, bold, fg, 12, 8)
                Using p As New Pen(ForeColor, 1.6F)
                    g.DrawLine(p, Width - 24, 12, Width - 14, 22)
                    g.DrawLine(p, Width - 14, 12, Width - 24, 22)
                End Using
                Dim hint = If(_sub.Visible, "點一下套用（線條色換成建議顏色）　｜　Esc 關閉", "點一下換筆刷　｜　常用筆刷在輪盤外圈按右鍵更換　｜　Esc 關閉")
                Using sf As New StringFormat With {.Alignment = StringAlignment.Center}
                    g.DrawString(hint, small, s2, New RectangleF(0, Height - 22, Width, 18), sf)
                End Using
            End Using
            Using p As New Pen(If(ThemeManager.Dark, Color.FromArgb(150, 160, 178), Color.FromArgb(130, 136, 150)), 2)
                g.DrawRectangle(p, 1, 1, Width - 2, Height - 2)
            End Using
        End Sub

        ' 標題列拖曳移動、× 關閉
        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Y > HeaderH Then Return
            If e.X > Width - 32 Then Hide() : Return
            _drag = e.Location
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If _drag.HasValue Then Location = New Point(Location.X + e.X - _drag.Value.X, Location.Y + e.Y - _drag.Value.Y)
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            _drag = Nothing
        End Sub

        Protected Overrides Sub OnDeactivate(e As EventArgs)
            MyBase.OnDeactivate(e)
            BeginInvoke(Sub() If Not ContainsFocus Then Hide())
        End Sub

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            If keyData = Keys.Escape Then Hide() : Return True
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _font.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
