Imports PhotoEdit

''' <summary>
''' 像素圖放大（影像 → 像素圖放大…）：選演算法（最近鄰、Scale2x、HQx、xBR）與倍數，
''' 左邊最近鄰、右邊所選演算法，用同樣的倍數、不插值顯示（每個像素看得清楚），拖曳平移兩邊一起動。
''' 按「開成新影像」把整張放大的結果交給主畫面另存開啟，原圖不變。
''' </summary>
Friend Class frmPixelScale
    Inherits Aqua.AquaForm

    Private Const PreviewW As Integer = 380
    Private Const PreviewH As Integer = 380
    Private ReadOnly _source As Bitmap
    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _before As New PixelView()
    Private ReadOnly _after As New PixelView()
    Private ReadOnly _algo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _factor As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _zoom As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _info As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _note As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
    Private ReadOnly _open As New Button With {.Text = Lang.T("開成新影像")}
    Private _result As Bitmap
    Private _syncing As Boolean
    ''' <summary>目前選的倍數（換演算法時用來挑最接近的倍數；下拉的位置在換演算法後就對不上了）。</summary>
    Private _factorValue As Integer

    ''' <summary>演算法的名稱（下拉的順序同 PixelScaleAlgorithm）。</summary>
    Public Shared ReadOnly AlgorithmNames As String() = {Lang.T("最近鄰（方塊）"), Lang.T("Scale2x／3x（EPX）"), Lang.T("HQx"), Lang.T("xBR")}

    ''' <summary>按「開成新影像」時整張放大的結果（呼叫端負責存檔與釋放）。</summary>
    Public ReadOnly Property Result As Bitmap
        Get
            Return _result
        End Get
    End Property

    Public ReadOnly Property Algorithm As PixelScaleAlgorithm
        Get
            Return CType(Math.Max(0, _algo.SelectedIndex), PixelScaleAlgorithm)
        End Get
    End Property

    Public ReadOnly Property Factor As Integer
        Get
            Dim fs = PixelScaler.Factors(Algorithm)
            Return fs(Math.Max(0, Math.Min(fs.Length - 1, _factor.SelectedIndex)))
        End Get
    End Property

    Public Sub New(source As Bitmap, help As HelpTip, Optional initialAlgorithm As PixelScaleAlgorithm = PixelScaleAlgorithm.XBR, Optional initialFactor As Integer = 4)
        ' 參數不能叫 algorithm／factor：VB 不分大小寫，建構式裡的 Algorithm、Factor 會變成指參數
        _source = source
        Text = Lang.T("像素圖放大")
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        RoundCorners = False
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        Dim top = 23 + 12
        Dim rightX = 16 + PreviewW * 2 + 24
        ClientSize = New Size(rightX + 250, top + PreviewH + 76)

        Dim capBefore As New Label With {.Text = Lang.T("最近鄰（對照）"), .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16, top)}
        Dim capAfter As New Label With {.Text = Lang.T("放大結果"), .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16 + PreviewW + 12, top)}
        _before.SetBounds(16, top + 24, PreviewW, PreviewH)
        _after.SetBounds(16 + PreviewW + 12, top + 24, PreviewW, PreviewH)
        AddHandler _before.Panned, Sub(p) _after.Center = p
        AddHandler _after.Panned, Sub(p) _before.Center = p
        Controls.AddRange({capBefore, capAfter, _before, _after})

        Dim y = top + 24
        Dim addRow = Function(caption As String, c As Control, helpKey As String)
                         Dim l As New Label With {.Text = caption, .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(rightX, y + 4)}
                         c.SetBounds(rightX + 70, y, 160, 26)
                         Controls.AddRange({l, c})
                         help.SetHelpLinked(helpKey, c, l, c)
                         y += 36
                         Return c
                     End Function
        _algo.Items.AddRange(AlgorithmNames.Cast(Of Object)().ToArray())
        addRow(Lang.T("演算法"), _algo, "pixel.algo")
        addRow(Lang.T("倍數"), _factor, "pixel.factor")
        _zoom.Items.AddRange({"100%", "200%", "400%"})
        addRow(Lang.T("預覽"), _zoom, "pixel.zoom")
        _info.SetBounds(rightX, y + 4, 236, 44)
        _note.SetBounds(rightX, y + 52, 236, PreviewH - (y - top) - 60)
        Controls.AddRange({_info, _note})

        Dim closeBtn As New Button With {.Text = Lang.T("關閉"), .DialogResult = DialogResult.Cancel}
        _open.SetBounds(ClientSize.Width - 252, ClientSize.Height - 46, 130, 32)
        closeBtn.SetBounds(ClientSize.Width - 112, ClientSize.Height - 46, 96, 32)
        help.SetHelp("pixel.open", _open)
        AddHandler _open.Click, Sub() OpenResult()
        Controls.AddRange({_open, closeBtn})
        CancelButton = closeBtn

        _syncing = True
        _algo.SelectedIndex = CInt(initialAlgorithm)
        FillFactors(initialFactor)
        _zoom.SelectedIndex = If(_source.Width * 4 <= PreviewW, 2, If(_source.Width * 2 <= PreviewW, 1, 0))
        _syncing = False
        AddHandler _algo.SelectedIndexChanged, Sub()
                                                   If _syncing Then Return
                                                   _syncing = True
                                                   FillFactors(_factorValue)
                                                   _syncing = False
                                                   UpdatePreview()
                                               End Sub
        AddHandler _factor.SelectedIndexChanged, Sub()
                                                     If _syncing Then Return
                                                     _factorValue = Factor
                                                     UpdatePreview()
                                                 End Sub
        AddHandler _zoom.SelectedIndexChanged, Sub()
                                                   If Not _syncing Then UpdatePreview()
                                               End Sub
        ThemeManager.Attach(Me)
        UpdatePreview()
    End Sub

    ''' <summary>這個演算法能選的倍數；原本選的倍數有就留著，沒有就選最接近的。</summary>
    Private Sub FillFactors(want As Integer)
        Dim fs = PixelScaler.Factors(Algorithm)
        _factor.Items.Clear()
        _factor.Items.AddRange(fs.Select(Function(f) CObj(f & "×")).ToArray())
        Dim best = 0
        For i = 0 To fs.Length - 1
            If Math.Abs(fs(i) - want) < Math.Abs(fs(best) - want) Then best = i
        Next
        _factor.SelectedIndex = best
        _factorValue = fs(best)
    End Sub

    ''' <summary>
    ''' 預覽只算看得到的那一塊（多帶 3 像素的鄰域），大圖也不會卡；兩邊同樣的倍數、同樣的位置。
    ''' </summary>
    Private Sub UpdatePreview()
        Dim n = Factor
        Dim w = _source.Width, h = _source.Height
        Dim outW = CLng(w) * n, outH = CLng(h) * n
        Dim tooBig = outW * outH > PixelScaler.MaxOutputPixels
        _info.Text = Lang.F("{0} × {1}  →  {2} × {3}", w, h, outW, outH)
        _info.ForeColor = If(tooBig, Color.FromArgb(200, 40, 40), ForeColor)
        _open.Enabled = Not tooBig
        _note.Text = If(tooBig, Lang.T("放大後太大（超過 1 億像素）：選小一點的倍數，或先把圖裁小。"), NoteFor(Algorithm))
        Dim zoom = {1, 2, 4}(Math.Max(0, _zoom.SelectedIndex))
        ' 看得到的範圍（原圖像素）：預覽框 ÷ (倍數 × 預覽放大)
        Dim viewW = Math.Min(w, CInt(Math.Ceiling(PreviewW / CDbl(n * zoom))) + 1), viewH = Math.Min(h, CInt(Math.Ceiling(PreviewH / CDbl(n * zoom))) + 1)
        Dim c = _before.Center
        Dim cx = If(c.X < 0, w \ 2, c.X), cy = If(c.Y < 0, h \ 2, c.Y)
        Dim x0 = Math.Max(0, Math.Min(w - viewW, cx - viewW \ 2)), y0 = Math.Max(0, Math.Min(h - viewH, cy - viewH \ 2))
        Const Pad As Integer = 3
        Dim px0 = Math.Max(0, x0 - Pad), py0 = Math.Max(0, y0 - Pad)
        Dim px1 = Math.Min(w, x0 + viewW + Pad), py1 = Math.Min(h, y0 + viewH + Pad)
        Using part = _source.Clone(New Rectangle(px0, py0, px1 - px0, py1 - py0), Imaging.PixelFormat.Format32bppArgb)
            Using near = PixelScaler.Upscale(part, PixelScaleAlgorithm.Nearest, If(PixelScaler.Factors(PixelScaleAlgorithm.Nearest).Contains(n), n, 4)),
                  up = PixelScaler.Upscale(part, Algorithm, n)
                Dim crop = New Rectangle((x0 - px0) * n, (y0 - py0) * n, viewW * n, viewH * n)
                _before.SetImage(near.Clone(crop, Imaging.PixelFormat.Format32bppArgb), zoom)
                _after.SetImage(up.Clone(crop, Imaging.PixelFormat.Format32bppArgb), zoom)
            End Using
        End Using
        _before.Region0 = New Rectangle(x0, y0, viewW, viewH) : _after.Region0 = _before.Region0
        _before.SourceSize = New Size(w, h) : _after.SourceSize = _before.SourceSize
        _before.Factor = n * zoom : _after.Factor = n * zoom
    End Sub

    Private Shared Function NoteFor(a As PixelScaleAlgorithm) As String
        Select Case a
            Case PixelScaleAlgorithm.Nearest : Return Lang.T("每個像素變成方塊，完全不改顏色。要保留像素風格（例如放大後再印出、做成貼圖）時用。")
            Case PixelScaleAlgorithm.Scale2x : Return Lang.T("只在對角相同的地方補上鄰點的顏色，不產生新顏色；斜線變成較細的鋸齒。調色盤有限制時適合。")
            Case PixelScaleAlgorithm.HQx : Return Lang.T("相近的顏色之間平滑、不同的顏色之間保持銳利，斜線磨圓。依 HQx 的原理重新實作，和原版相近但不保證逐像素相同。")
            Case Else : Return Lang.T("比較兩個方向的邊緣強度，沿 45° 與較平、較陡的邊切角混色，斜線與曲線最平滑。遊戲畫面、圖示最常用。")
        End Select
    End Function

    ''' <summary>整張放大（大圖會花幾秒，游標轉圈）。</summary>
    Private Sub OpenResult()
        Cursor = Cursors.WaitCursor
        Try
            _result = PixelScaler.Upscale(_source, Algorithm, Factor)
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException
            Cursor = Cursors.Default
            MessageBox.Show(Me, Lang.T("無法放大：") & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Cursor = Cursors.Default
        DialogResult = DialogResult.OK
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _before.Dispose() : _after.Dispose()
            _font.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>不插值的預覽（每個像素是一個方塊），可以拖曳平移；透明的地方墊棋盤格。</summary>
    Private NotInheritable Class PixelView
        Inherits Control
        Private _img As Bitmap
        Private _zoom As Integer = 1
        Private _drag As Point?
        Public Region0 As Rectangle
        Public SourceSize As Size
        Public Factor As Integer = 1
        ''' <summary>預覽中心在原圖的位置（-1 = 置中）。</summary>
        Public Property Center As Point = New Point(-1, -1)
        Public Event Panned(p As Point)

        Public Sub New()
            DoubleBuffered = True
            Cursor = Cursors.SizeAll
        End Sub

        Public Sub SetImage(img As Bitmap, zoom As Integer)
            _img?.Dispose()
            _img = img
            _zoom = zoom
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            For y = 0 To Height Step 12
                For x = 0 To Width Step 12
                    g.FillRectangle(If(((x + y) \ 12) Mod 2 = 0, Brushes.Gainsboro, Brushes.WhiteSmoke), x, y, 12, 12)
                Next
            Next
            If _img Is Nothing Then Return
            g.InterpolationMode = Drawing2D.InterpolationMode.NearestNeighbor
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.Half
            Dim dw = _img.Width * _zoom, dh = _img.Height * _zoom
            g.DrawImage(_img, New Rectangle((Width - dw) \ 2, (Height - dh) \ 2, dw, dh))
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Button = MouseButtons.Left Then _drag = e.Location
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If Not _drag.HasValue OrElse Factor <= 0 Then Return
            Dim dx = (e.X - _drag.Value.X) \ Factor, dy = (e.Y - _drag.Value.Y) \ Factor
            If dx = 0 AndAlso dy = 0 Then Return
            _drag = New Point(_drag.Value.X + dx * Factor, _drag.Value.Y + dy * Factor)
            Dim c = If(Center.X < 0, New Point(Region0.X + Region0.Width \ 2, Region0.Y + Region0.Height \ 2), Center)
            Center = New Point(Math.Max(0, Math.Min(SourceSize.Width - 1, c.X - dx)), Math.Max(0, Math.Min(SourceSize.Height - 1, c.Y - dy)))
            RaiseEvent Panned(Center)
            CType(Parent, frmPixelScale).UpdatePreview()
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            _drag = Nothing
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _img?.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
