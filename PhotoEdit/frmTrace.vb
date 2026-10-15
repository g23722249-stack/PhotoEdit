Imports PhotoEdit

''' <summary>
''' 影像描圖（同 CorelDRAW 的 PowerTRACE）的設定視窗：左邊預覽描好的向量圖，右邊選模式、顏色數、細節、去雜點、去背景。
''' 預覽用小一點的解析度在背景算（改了設定 0.3 秒後重算）；按「描圖」後由編輯器用完整解析度描、加成繪圖圖層。
''' </summary>
Friend Class frmTrace
    Inherits Form

    Private ReadOnly _source As Bitmap
    Private ReadOnly _preview As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(60, 62, 68), .Dock = DockStyle.Fill}
    Private ReadOnly _mode As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _colors As New TrackBar With {.Minimum = 2, .Maximum = 24, .Value = 8, .TickFrequency = 2}
    Private ReadOnly _detail As New TrackBar With {.Minimum = 1, .Maximum = 10, .Value = 6}
    Private ReadOnly _speckle As New TrackBar With {.Minimum = 0, .Maximum = 100, .Value = 12, .TickFrequency = 10}
    Private ReadOnly _removeBg As New CheckBox With {.Text = "去掉背景（最大的那個顏色）", .AutoSize = True}
    Private ReadOnly _info As New Label With {.AutoSize = False, .Height = 40, .ForeColor = Color.FromArgb(90, 96, 106)}
    Private ReadOnly _colorsLabel As New Label With {.AutoSize = True}
    Private ReadOnly _detailLabel As New Label With {.AutoSize = True}
    Private ReadOnly _speckleLabel As New Label With {.AutoSize = True}
    Private ReadOnly _timer As New Timer With {.Interval = 300}
    Private _serial As Integer

    ''' <summary>按「描圖」時的設定。</summary>
    Public ReadOnly Property Options As TraceOptions
        Get
            Return New TraceOptions With {.Mode = CType(_mode.SelectedIndex, TraceMode), .Colors = _colors.Value, .Detail = _detail.Value,
                                          .Speckle = _speckle.Value, .RemoveBackground = _removeBg.Checked}
        End Get
    End Property

    Public Sub New(source As Bitmap)
        _source = source
        Text = "影像描圖（轉成向量）"
        StartPosition = FormStartPosition.CenterParent
        Size = New Size(980, 640)
        MinimumSize = New Size(760, 480)
        ShowInTaskbar = False
        MaximizeBox = False
        Font = New Font("Microsoft JhengHei UI", 9.5F)
        Dim side As New Panel With {.Dock = DockStyle.Right, .Width = 280, .Padding = New Padding(14, 12, 14, 12)}
        Dim y = 12
        Dim add = Sub(c As Control, h As Integer)
                      c.SetBounds(14, y, 252, h)
                      side.Controls.Add(c)
                      y += h + 6
                  End Sub
        add(New Label With {.Text = "模式", .AutoSize = True}, 20)
        _mode.Items.AddRange({"黑白線稿", "彩色"})
        _mode.SelectedIndex = 1
        add(_mode, 26)
        add(_colorsLabel, 20) : add(_colors, 34)
        add(_detailLabel, 20) : add(_detail, 34)
        add(_speckleLabel, 20) : add(_speckle, 34)
        add(_removeBg, 24)
        y += 6
        add(_info, 60)
        Dim ok As New Button With {.Text = "描圖", .DialogResult = DialogResult.OK}
        Dim cancel As New Button With {.Text = "取消", .DialogResult = DialogResult.Cancel}
        ok.Size = New Size(84, 30) : cancel.Size = New Size(84, 30)
        ok.Margin = New Padding(6, 0, 0, 0) : cancel.Margin = New Padding(6, 0, 0, 0)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 40, .FlowDirection = FlowDirection.RightToLeft, .WrapContents = False}
        buttons.Controls.AddRange({cancel, ok})
        side.Controls.Add(buttons)
        AcceptButton = ok
        CancelButton = cancel
        Controls.Add(_preview)
        Controls.Add(side)
        AddHandler _mode.SelectedIndexChanged, Sub() Changed()
        AddHandler _colors.ValueChanged, Sub() Changed()
        AddHandler _detail.ValueChanged, Sub() Changed()
        AddHandler _speckle.ValueChanged, Sub() Changed()
        AddHandler _removeBg.CheckedChanged, Sub() Changed()
        AddHandler _timer.Tick, Sub()
                                    _timer.Stop()
                                    RefreshPreview()
                                End Sub
        UpdateLabels()
        RefreshPreview()
    End Sub

    Private Sub UpdateLabels()
        _colors.Enabled = _mode.SelectedIndex = 1
        _colorsLabel.Text = $"顏色數：{_colors.Value}"
        _detailLabel.Text = $"細節：{_detail.Value}（越高越像照片、節點越多）"
        _speckleLabel.Text = $"去雜點：{_speckle.Value}（小於這麼多像素的小塊併掉）"
        _removeBg.Text = If(_mode.SelectedIndex = 0, "去掉背景（只留黑色）", "去掉背景（最大的那個顏色）")
    End Sub

    Private Sub Changed()
        UpdateLabels()
        _timer.Stop()
        _timer.Start()
    End Sub

    ''' <summary>在背景用小解析度描一次，畫成預覽。</summary>
    Private Sub RefreshPreview()
        _serial += 1
        Dim serial = _serial
        Dim opt = Options
        opt.Resolution = 420
        Dim src = _source
        _info.Text = "描圖中…"
        Threading.Tasks.Task.Run(Function()
                                     Dim layers As List(Of DrawLayer)
                                     SyncLock src
                                         layers = ImageTrace.Trace(src, opt)
                                     End SyncLock
                                     Dim pw = 640, ph = Math.Max(1, CInt(640.0 * src.Height / src.Width))
                                     Dim bmp As New Bitmap(pw, ph, Imaging.PixelFormat.Format32bppArgb)
                                     Using g = Graphics.FromImage(bmp)
                                         ' 透明的地方畫棋盤格
                                         For yy = 0 To ph - 1 Step 12
                                             For xx = 0 To pw - 1 Step 12
                                                 g.FillRectangle(If(((xx + yy) \ 12) Mod 2 = 0, Brushes.White, Brushes.Gainsboro), xx, yy, 12, 12)
                                             Next
                                         Next
                                     End Using
                                     LayerStack.Draw(bmp, New EditRecipe With {.Drawings = layers})
                                     Dim nodes = layers.Sum(Function(l) l.Contours.Sum(Function(c) c.Nodes.Count))
                                     Return (bmp, layers.Count, nodes)
                                 End Function).ContinueWith(Sub(t)
                                                                If IsDisposed OrElse serial <> _serial OrElse t.IsFaulted Then
                                                                    If Not t.IsFaulted Then t.Result.bmp.Dispose()
                                                                    Return
                                                                End If
                                                                Dim old = _preview.Image
                                                                _preview.Image = t.Result.bmp
                                                                old?.Dispose()
                                                                _info.Text = $"{t.Result.Item2} 個圖形、{t.Result.nodes} 個節點（預覽是較低的解析度，描圖時會更細）。"
                                                            End Sub, Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext())
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _timer.Dispose()
            _preview.Image?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
