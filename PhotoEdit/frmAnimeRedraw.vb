Imports System.Threading
Imports PhotoEdit

''' <summary>
''' 宮崎風 AI 重繪（本機顯卡）：左邊原圖、右邊結果；選模型（吉卜力電影風／動畫插畫風）與風格、重繪強度、種子、
''' 畫面描述（英文，可空白），按「生成」在背景跑（約 30 秒，快速動畫幾秒；有進度條、可停止）。
''' 按「開成新影像」把結果交給主畫面另存開啟，原照片不變。
''' </summary>
Friend Class frmAnimeRedraw
    Inherits Aqua.AquaForm

    Private Const PreviewW As Integer = 340
    Private Const PreviewH As Integer = 480
    Private ReadOnly _source As Bitmap
    Private ReadOnly _help As HelpTip
    Private ReadOnly _models As IReadOnlyList(Of AnimeRedrawModel)
    Private ReadOnly _presets As IReadOnlyList(Of AnimeRedrawPreset)
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10.0F)
    Private ReadOnly _small As New Font("Microsoft JhengHei UI", 9.0F)
    Private ReadOnly _before As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(30, 30, 32)}
    Private ReadOnly _after As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(30, 30, 32)}
    ' 模型與風格各放一個容器（同一容器裡的選項按鈕會互斥）
    Private ReadOnly _modelPanel As New Panel With {.BackColor = Color.Transparent}
    Private ReadOnly _stylePanel As New Panel With {.BackColor = Color.Transparent}
    Private ReadOnly _modelButtons As New List(Of RadioButton)()
    Private ReadOnly _styles As New List(Of RadioButton)()
    Private ReadOnly _styleIndex As New List(Of Integer)()
    Private ReadOnly _note As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120)}
    Private ReadOnly _strength As New Aqua.Slider With {.Minimum = 30, .Maximum = 70, .ShowTicks = False}
    Private ReadOnly _strengthValue As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _seed As New NumericUpDown With {.Minimum = 0, .Maximum = 999999}
    Private ReadOnly _subject As New TextBox()
    Private ReadOnly _generate As New Button With {.Text = "生成"}
    Private ReadOnly _cancelGen As New Button With {.Text = "停止", .Enabled = False}
    Private ReadOnly _progress As New ProgressBar With {.Minimum = 0, .Maximum = 100}
    Private ReadOnly _status As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _open As New Button With {.Text = "開成新影像", .Enabled = False}
    Private _cts As CancellationTokenSource
    Private _result As Bitmap
    Private _model As Integer
    Private _preset As Integer

    ''' <summary>按「開成新影像」時的結果（呼叫端負責存檔與釋放）。</summary>
    Public ReadOnly Property Result As Bitmap
        Get
            Return _result
        End Get
    End Property

    ''' <summary>產生結果用的模型、風格與種子（存檔命名用）。</summary>
    Public ReadOnly Property ResultName As String
        Get
            Return $"{_models(_model).Name}_{_presets(_preset).Name}_{_seed.Value}"
        End Get
    End Property

    Public Sub New(source As Bitmap, help As HelpTip)
        _source = source
        _help = help
        _models = AnimeRedraw.Models()
        _presets = AnimeRedraw.Presets()
        Text = "宮崎風 AI 重繪"
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        Dim top = 23 + 12
        Dim rightX = 16 + PreviewW * 2 + 24
        ClientSize = New Size(rightX + 270, top + PreviewH + 70)

        Dim capBefore As New Label With {.Text = "原圖", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16, top)}
        Dim capAfter As New Label With {.Text = "宮崎風", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16 + PreviewW + 12, top)}
        _before.SetBounds(16, top + 24, PreviewW, PreviewH)
        _after.SetBounds(16 + PreviewW + 12, top + 24, PreviewW, PreviewH)
        _before.Image = source
        Controls.AddRange({capBefore, capAfter, _before, _after})

        ' 右邊：模型、風格、強度、種子、描述、生成
        Dim y = top
        Controls.Add(New Label With {.Text = "模型", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(rightX, y)})
        y += 22
        _modelPanel.SetBounds(rightX, y, 256, 28)
        For i = 0 To _models.Count - 1
            Dim index = i
            Dim rb As New RadioButton With {.Text = _models(i).Name, .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(4 + i * 124, 4)}
            AddHandler rb.CheckedChanged, Sub()
                                              If rb.Checked Then SelectModel(index)
                                          End Sub
            help?.SetHelp("redraw.model", rb)
            _modelButtons.Add(rb)
            _modelPanel.Controls.Add(rb)
        Next
        Controls.Add(_modelPanel)
        y += 34
        Controls.Add(New Label With {.Text = "風格", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(rightX, y)})
        y += 22
        _stylePanel.SetBounds(rightX, y, 256, 4 * 25 + 4)
        Controls.Add(_stylePanel)
        y += 4 * 25 + 6
        _note.Font = _small
        _note.SetBounds(rightX, y, 256, 40)
        Controls.Add(_note)
        y += 44
        Dim scap As New Label With {.Text = "重繪強度", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(rightX, y + 4)}
        _strength.SetBounds(rightX + 70, y + 2, 140, 24)
        _strengthValue.SetBounds(rightX + 210, y, 46, 26)
        AddHandler _strength.ValueChanged, Sub() _strengthValue.Text = (_strength.Value / 100.0).ToString("0.00")
        help?.SetHelpLinked("redraw.strength", _strength, scap, _strength, _strengthValue)
        Controls.AddRange({scap, _strength, _strengthValue})
        y += 34
        Dim seedCap As New Label With {.Text = "種子", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(rightX, y + 4)}
        _seed.SetBounds(rightX + 70, y, 96, 26)
        Dim dice As New Button With {.Text = "隨機"}
        dice.SetBounds(rightX + 174, y - 1, 82, 28)
        AddHandler dice.Click, Sub() _seed.Value = New Random().Next(0, 999999)
        help?.SetHelpLinked("redraw.seed", _seed, seedCap, _seed, dice)
        Controls.AddRange({seedCap, _seed, dice})
        y += 36
        Dim subjCap As New Label With {.Text = "畫面描述（英文，可空白）", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(rightX, y)}
        _subject.SetBounds(rightX, y + 22, 256, 54)
        _subject.Multiline = True
        _subject.ScrollBars = ScrollBars.Vertical
        help?.SetHelpLinked("redraw.subject", _subject, subjCap, _subject)
        Controls.AddRange({subjCap, _subject})
        y += 84
        _generate.SetBounds(rightX, y, 126, 34)
        _cancelGen.SetBounds(rightX + 130, y, 126, 34)
        AddHandler _generate.Click, Sub() StartGenerate()
        AddHandler _cancelGen.Click, Sub() _cts?.Cancel()
        help?.SetHelp("redraw.generate", _generate)
        Controls.AddRange({_generate, _cancelGen})
        y += 42
        _progress.SetBounds(rightX, y, 256, 16)
        _status.Font = _small
        _status.SetBounds(rightX, y + 20, 256, 44)
        Controls.AddRange({_progress, _status})

        ' 下方：開成新影像／關閉
        Dim closeBtn As New Button With {.Text = "關閉", .DialogResult = DialogResult.Cancel}
        _open.SetBounds(ClientSize.Width - 252, ClientSize.Height - 46, 130, 32)
        closeBtn.SetBounds(ClientSize.Width - 116, ClientSize.Height - 46, 100, 32)
        AddHandler _open.Click, Sub()
                                    DialogResult = DialogResult.OK
                                    Close()
                                End Sub
        help?.SetHelp("redraw.open", _open)
        Controls.AddRange({_open, closeBtn})
        CancelButton = closeBtn

        If _modelButtons.Count > 0 Then _modelButtons(0).Checked = True
        ThemeManager.Attach(Me)
    End Sub

    ''' <summary>換模型：風格清單換成這個模型的，選第一個；檢查模型檔在不在。</summary>
    Private Sub SelectModel(index As Integer)
        _model = index
        Dim m = _models(index)
        _stylePanel.Controls.Clear()
        _styles.Clear()
        _styleIndex.Clear()
        Dim y = 2
        For i = 0 To _presets.Count - 1
            If _presets(i).Model <> m.Id Then Continue For
            Dim presetIndex = i
            Dim rb As New RadioButton With {.Text = _presets(i).Name, .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(4, y)}
            AddHandler rb.CheckedChanged, Sub()
                                              If rb.Checked Then SelectPreset(presetIndex)
                                          End Sub
            _help?.SetHelp("redraw.style", rb)
            _styles.Add(rb)
            _styleIndex.Add(i)
            _stylePanel.Controls.Add(rb)
            y += 25
        Next
        ThemeManager.Apply(_stylePanel)
        If _styles.Count > 0 Then _styles(0).Checked = True
        Dim why = AnimeRedraw.Availability(m)
        _generate.Enabled = why Is Nothing AndAlso _cts Is Nothing
        _status.Text = If(why IsNot Nothing, why & "（模型要另外下載，放在 Models\）", "按「生成」開始（用顯卡）。")
    End Sub

    ''' <summary>選風格：強度與種子換成該風格的設定。</summary>
    Private Sub SelectPreset(index As Integer)
        _preset = index
        Dim p = _presets(index)
        _strength.Value = CInt(Math.Round(p.Strength * 100))
        _strengthValue.Text = p.Strength.ToString("0.00")
        _seed.Value = p.Seed
        _note.Text = p.Note
    End Sub

    Private Sub StartGenerate()
        If _cts IsNot Nothing Then Return
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        Dim preset = _presets(_preset)
        Dim strength = _strength.Value / 100.0, seed = CInt(_seed.Value), subject = _subject.Text
        SetBusy(True)
        _progress.Value = 0
        _status.Text = "打底與畫線稿…"
        Dim t0 = Environment.TickCount
        Threading.Tasks.Task.Run(Function() AnimeRedraw.Generate(_source, preset, strength, seed, subject,
                                                                   Sub(cur, total)
                                                                       If IsDisposed OrElse Not IsHandleCreated Then Return
                                                                       BeginInvoke(Sub()
                                                                                       _progress.Value = Math.Min(100, CInt(cur * 100.0 / Math.Max(1, total)))
                                                                                       _status.Text = $"重繪中 {cur}/{total}…"
                                                                                   End Sub)
                                                                   End Sub, ct)).ContinueWith(
            Sub(task)
                If IsDisposed OrElse Not IsHandleCreated Then
                    If task.Status = Threading.Tasks.TaskStatus.RanToCompletion Then task.Result.Dispose()
                    Return
                End If
                BeginInvoke(Sub()
                                _cts.Dispose()
                                _cts = Nothing
                                SetBusy(False)
                                If task.Status = Threading.Tasks.TaskStatus.RanToCompletion Then
                                    _result?.Dispose()
                                    _result = task.Result
                                    _after.Image = _result
                                    _progress.Value = 100
                                    _status.Text = $"完成（{(Environment.TickCount - t0) / 1000.0:0} 秒）。不滿意可以換種子或強度再生成。"
                                    _open.Enabled = True
                                ElseIf task.IsCanceled OrElse (task.Exception IsNot Nothing AndAlso TypeOf task.Exception.InnerException Is OperationCanceledException) Then
                                    _progress.Value = 0
                                    _status.Text = "已停止。"
                                Else
                                    _progress.Value = 0
                                    _status.Text = "生成失敗：" & task.Exception?.InnerException?.Message
                                End If
                            End Sub)
            End Sub)
    End Sub

    Private Sub SetBusy(busy As Boolean)
        _generate.Enabled = Not busy AndAlso AnimeRedraw.Availability(_models(_model)) Is Nothing
        _cancelGen.Enabled = busy
        _modelPanel.Enabled = Not busy
        _stylePanel.Enabled = Not busy
        _strength.Enabled = Not busy
        _seed.Enabled = Not busy
        _subject.Enabled = Not busy
        _open.Enabled = Not busy AndAlso _result IsNot Nothing
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _before.Image = Nothing
            _after.Image = Nothing
            If DialogResult <> DialogResult.OK Then _result?.Dispose()
            _font.Dispose()
            _small.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
