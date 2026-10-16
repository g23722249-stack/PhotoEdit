Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports PhotoEdit

''' <summary>
''' 向量效果的設定視窗（輪廓圖、變形效果、立體化、簡化路徑共用）：上面一排排參數，右邊是即時預覽。
''' 參數一改，等一下（不要每打一個字就算）再呼叫 Compute 重算預覽；預覽把照片大小的繪圖縮小畫好，再裁出效果附近放大顯示。
''' </summary>
Friend Class frmVectorEffect
    Inherits Form

    Private ReadOnly _rows As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoScroll = True}
    Private ReadOnly _preview As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BorderStyle = BorderStyle.FixedSingle, .BackColor = Color.White}
    Private ReadOnly _info As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft, .ForeColor = Color.FromArgb(90, 96, 106)}
    Private ReadOnly _timer As New Timer With {.Interval = 180}
    Private ReadOnly _controls As New Dictionary(Of String, Control)()
    Private ReadOnly _rowOf As New Dictionary(Of String, Control)()
    Private ReadOnly _aspect As Double
    Private _building As Boolean = True

    ''' <summary>預覽要畫的圖層（全部，照片高度單位）；回傳 Nothing＝沒有結果。</summary>
    Public Property Compute As Func(Of List(Of DrawLayer))
    ''' <summary>預覽下面的說明（例如節點數）；可以不設。</summary>
    Public Property Describe As Func(Of String)
    ''' <summary>參數改了（重算之前）：用來依選項顯示、隱藏其他列。</summary>
    Public Event ParamsChanged As EventHandler

    Public Sub New(title As String, aspect As Double, font As Font)
        Text = title
        _aspect = Math.Max(0.05, aspect)
        Me.Font = font
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False : MinimizeBox = False : ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(640, 360)
        _rows.SetBounds(12, 12, 290, 286)
        _preview.SetBounds(314, 12, 314, 262)
        _info.SetBounds(314, 278, 314, 22)
        Dim ok As New Button With {.Text = Lang.T("確定"), .DialogResult = DialogResult.OK}
        Dim cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel}
        ok.SetBounds(444, 316, 88, 30) : cancel.SetBounds(540, 316, 88, 30)
        Controls.AddRange({_rows, _preview, _info, ok, cancel})
        AcceptButton = ok : CancelButton = cancel
        AddHandler _timer.Tick, Sub()
                                    _timer.Stop()
                                    RefreshPreview()
                                End Sub
        AddHandler Shown, Sub()
                              _building = False
                              RefreshPreview()
                          End Sub
        AddHandler FormClosed, Sub()
                                   _timer.Dispose()
                                   _preview.Image?.Dispose()
                               End Sub
    End Sub

    '---------------------------------------------------------------------
    ' 參數列
    '---------------------------------------------------------------------

    Private Function AddRow(key As String, caption As String, c As Control) As Control
        Dim row As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 0, 6)}
        Dim lbl As New Label With {.Text = caption, .AutoSize = False, .Width = 150, .Height = 26, .TextAlign = ContentAlignment.MiddleLeft}
        row.Controls.Add(lbl)
        row.Controls.Add(c)
        _rows.Controls.Add(row)
        _controls(key) = c
        _rowOf(key) = row
        Return c
    End Function

    Private Sub Changed()
        If _building Then Return
        RaiseEvent ParamsChanged(Me, EventArgs.Empty)
        _timer.Stop() : _timer.Start()
    End Sub

    Public Sub AddNumber(key As String, caption As String, min As Decimal, max As Decimal, value As Decimal, Optional decimals As Integer = 0)
        Dim nb As New NumericUpDown With {.Minimum = min, .Maximum = max, .DecimalPlaces = decimals, .Width = 90,
                                          .Increment = If(decimals > 0, 0.5D, 1D), .Value = Math.Max(min, Math.Min(max, value))}
        AddHandler nb.ValueChanged, Sub() Changed()
        AddRow(key, caption, nb)
    End Sub

    Public Sub AddChoice(key As String, caption As String, items As String(), index As Integer)
        Dim cb As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 120}
        cb.Items.AddRange(items)
        cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, index))
        AddHandler cb.SelectedIndexChanged, Sub() Changed()
        AddRow(key, caption, cb)
    End Sub

    Public Sub AddCheck(key As String, caption As String, value As Boolean)
        Dim ck As New CheckBox With {.Checked = value, .AutoSize = True, .Margin = New Padding(0, 5, 0, 0)}
        AddHandler ck.CheckedChanged, Sub() Changed()
        AddRow(key, caption, ck)
    End Sub

    ''' <summary>顏色鈕：點了開選色視窗（Windows 的選色）。</summary>
    Public Sub AddColor(key As String, caption As String, value As Color)
        Dim b As New Button With {.Width = 90, .Height = 26, .BackColor = value, .FlatStyle = FlatStyle.Flat, .Tag = value}
        AddHandler b.Click, Sub()
                                Using dlg As New ColorDialog With {.Color = CType(b.Tag, Color), .FullOpen = True}
                                    If dlg.ShowDialog(Me) = DialogResult.OK Then SetColor(key, dlg.Color)
                                End Using
                            End Sub
        AddRow(key, caption, b)
    End Sub

    Public Sub SetColor(key As String, value As Color)
        Dim b = CType(_controls(key), Button)
        b.Tag = value : b.BackColor = Color.FromArgb(255, value)
        Changed()
    End Sub

    Public Function Number(key As String) As Double
        Return CDbl(CType(_controls(key), NumericUpDown).Value)
    End Function

    Public Sub SetNumber(key As String, value As Double)
        Dim nb = CType(_controls(key), NumericUpDown)
        nb.Value = CDec(Math.Max(nb.Minimum, Math.Min(nb.Maximum, value)))
    End Sub

    Public Function Choice(key As String) As Integer
        Return CType(_controls(key), ComboBox).SelectedIndex
    End Function

    Public Sub SetChoice(key As String, index As Integer)
        CType(_controls(key), ComboBox).SelectedIndex = index
    End Sub

    Public Function Checked(key As String) As Boolean
        Return CType(_controls(key), CheckBox).Checked
    End Function

    Public Function ColorOf(key As String) As Color
        Return CType(_controls(key).Tag, Color)
    End Function

    Public Sub SetRowVisible(key As String, visible As Boolean)
        _rowOf(key).Visible = visible
    End Sub

    Public Sub SetCaption(key As String, caption As String)
        _rowOf(key).Controls.OfType(Of Label)().First().Text = caption
    End Sub

    Public ReadOnly Property PreviewImage As Image
        Get
            Return _preview.Image
        End Get
    End Property

    Public ReadOnly Property InfoText As String
        Get
            Return _info.Text
        End Get
    End Property

    '---------------------------------------------------------------------
    ' 預覽
    '---------------------------------------------------------------------

    ''' <summary>重算並畫預覽（測試也直接呼叫）。</summary>
    Public Sub RefreshPreview()
        If Compute Is Nothing Then Return
        Dim layers As List(Of DrawLayer)
        Try
            Cursor = Cursors.WaitCursor
            layers = Compute.Invoke()
        Finally
            Cursor = Cursors.Default
        End Try
        _info.Text = If(Describe Is Nothing, "", Describe.Invoke())
        Dim old = _preview.Image
        _preview.Image = If(layers Is Nothing OrElse layers.Count = 0, Nothing, RenderPreview(layers))
        old?.Dispose()
    End Sub

    Private Function RenderPreview(layers As List(Of DrawLayer)) As Bitmap
        ' 範圍：所有圖層的外框，留一點邊，配合預覽框的比例
        Dim b = DrawGeometry.GroupBounds(layers)
        Dim pad = Math.Max(b.Width, b.Height) * 0.08 + 0.01
        b = RectangleF.FromLTRB(CSng(b.Left - pad), CSng(b.Top - pad), CSng(b.Right + pad), CSng(b.Bottom + pad))
        Dim boxAspect = _preview.Width / CDbl(Math.Max(1, _preview.Height))
        If b.Width / b.Height < boxAspect Then
            Dim w = b.Height * boxAspect
            b = New RectangleF(CSng(b.X - (w - b.Width) / 2), b.Y, CSng(w), b.Height)
        Else
            Dim h = b.Width / boxAspect
            b = New RectangleF(b.X, CSng(b.Y - (h - b.Height) / 2), b.Width, CSng(h))
        End If
        ' 照片大小的畫布：範圍要畫到大約預覽框的兩倍大
        Dim scale = Math.Min(4000, 2 * _preview.Height / Math.Max(0.01, b.Height))
        Dim fullH = CInt(Math.Max(50, Math.Min(4000, scale)))
        Dim fullW = CInt(Math.Max(50, Math.Min(6000, scale * _aspect)))
        Dim crop = Rectangle.Intersect(New Rectangle(0, 0, fullW, fullH),
                                       Rectangle.Round(New RectangleF(b.X * fullH, b.Y * fullH, b.Width * fullH, b.Height * fullH)))
        Using full As New Bitmap(fullW, fullH, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(full)
                g.Clear(Color.White)
            End Using
            LayerStack.Draw(full, New EditRecipe With {.Drawings = layers.Select(Function(d) d.Clone()).ToList()})
            If crop.Width < 2 OrElse crop.Height < 2 Then Return CType(full.Clone(), Bitmap)
            Return full.Clone(crop, PixelFormat.Format32bppArgb)
        End Using
    End Function
End Class
