Imports System.Drawing.Imaging
Imports System.Threading.Tasks
Imports PhotoEdit

''' <summary>
''' 共用的去背面板：模型＋「去背」按鈕、邊緣精修、內縮／外擴、去除邊緣殘色、去掉零碎小塊、填補小破洞、
''' 檢查遮罩、保留／擦除筆刷（可以復原一筆）。人物合成、吶喊等特效都放這一塊。
''' 面板自己記 AI 遮罩（每個模型算過就記住，切回去不用重算）、筆刷與最後的遮罩；
''' 預覽畫在呼叫的視窗的畫布上：DrawView 畫人物＋遮罩，滑鼠事件轉給 BeginStroke／ContinueStroke／EndStroke。
''' 去背用 BusyDialog（游標等待、處理中視窗）。
''' </summary>
Friend Class CutoutPanel
    Inherits Panel

    ''' <summary>最後的遮罩變了（去背完成、改設定、一筆畫完、復原）。</summary>
    Public Event MaskChanged As EventHandler
    ''' <summary>預覽要重畫（畫筆刷的途中也會發）。</summary>
    Public Event ViewChanged As EventHandler
    ''' <summary>不影響遮罩、但影響圖層的設定變了（去除邊緣殘色）。</summary>
    Public Event OptionsChanged As EventHandler
    ''' <summary>模型換了（還沒算時只換選擇）。</summary>
    Public Event ModelChanged As EventHandler
    ''' <summary>要顯示給使用者的訊息（呼叫的視窗放在狀態列）。</summary>
    Public Event StatusChanged As EventHandler(Of String)

    Private ReadOnly _help As HelpTip
    Private ReadOnly _w As Integer
    Private ReadOnly _portrait As Boolean

    ' 人物
    Private _figure As Bitmap
    Private _px As Byte()
    Private _fw, _fh As Integer
    Private _aiMask As Byte()
    Private _aiMaskIsSource As Boolean
    Private _aiModel As CutoutModel
    Private ReadOnly _aiCache As New Dictionary(Of CutoutModel, Byte())()
    Private _mask As Byte()
    Private ReadOnly _strokes As New List(Of List(Of CutoutDab))()
    Private _current As List(Of CutoutDab)
    Private _options As New CutoutEditOptions()
    Private _view As Bitmap
    Private _busy As Boolean
    Private _syncing As Boolean
    Private ReadOnly _recomputeTimer As New Timer With {.Interval = 150}

    ' 介面
    Private ReadOnly _model As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _run As New Button With {.UseVisualStyleBackColor = True}
    Private ReadOnly _refine As New Aqua.Slider()
    Private ReadOnly _shift As New Aqua.Slider()
    Private ReadOnly _decontam As New CheckBox With {.Text = Lang.T("去除邊緣殘色"), .AutoSize = True, .Checked = True, .BackColor = Color.Transparent}
    Private ReadOnly _specks As New CheckBox With {.Text = Lang.T("去掉零碎小塊"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _holes As New CheckBox With {.Text = Lang.T("填補小破洞"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _showMask As New CheckBox With {.Text = Lang.T("檢查遮罩（紅色＝去掉的部分）"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _keepBrush As New CheckBox With {.Text = Lang.T("保留筆刷"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter}
    Private ReadOnly _eraseBrush As New CheckBox With {.Text = Lang.T("擦除筆刷"), .Appearance = Appearance.Button, .TextAlign = ContentAlignment.MiddleCenter}
    Private ReadOnly _brushSize As New Aqua.Slider()
    Private ReadOnly _undo As New Button With {.Text = Lang.T("復原一筆"), .UseVisualStyleBackColor = True}
    Private ReadOnly _clear As New Button With {.Text = Lang.T("清除筆刷修正"), .UseVisualStyleBackColor = True}

    ''' <param name="width">面板寬度。</param>
    ''' <param name="runText">去背按鈕的字，例如「自動去背」。</param>
    ''' <param name="portrait">預設模型挑人像的（吶喊）還是一般的（人物合成）。</param>
    Public Sub New(help As HelpTip, width As Integer, runText As String, portrait As Boolean)
        _help = help
        _w = width
        _portrait = portrait
        BackColor = Color.Transparent
        _aiModel = CutoutModels.Preferred(portrait, AddressOf BackgroundRemover.ModelAvailable)
        Dim y = 0
        Dim l As New Label With {.Text = Lang.T("模型"), .AutoSize = False, .BackColor = Color.Transparent}
        l.SetBounds(0, y + 4, 50, 22)
        _model.SetBounds(52, y, width - 52, 26)
        FillModels(_aiModel)
        AddHandler _model.DropDown, Sub() FillModels(Model)
        AddHandler _model.SelectedIndexChanged, Sub() ModelPicked()
        _help?.SetHelpLinked("cut.model", _model, l, _model)
        Controls.AddRange({l, _model})
        y += 34
        _run.Text = runText
        _run.SetBounds(0, y, width, 30)
        AddHandler _run.Click, Sub() RunCutoutAsync(FindForm())
        _help?.SetHelp("cut.run", _run)
        Controls.Add(_run)
        y += 40
        y = AddSlider(y, Lang.T("邊緣精修"), _refine, 0, 100, 50, "cut.refine", Function(v) v.ToString())
        y = AddSlider(y, Lang.T("內縮／外擴"), _shift, -10, 10, 0, "cut.shift", Function(v) If(v > 0, "+" & v, v.ToString()))
        AddHandler _refine.ValueChanged, Sub()
                                             If _syncing Then Return
                                             _options.EdgeRefine = _refine.Value
                                             ScheduleRecompute()
                                         End Sub
        AddHandler _shift.ValueChanged, Sub()
                                            If _syncing Then Return
                                            _options.EdgeShift = _shift.Value
                                            ScheduleRecompute()
                                        End Sub
        For Each c In {_decontam, _specks, _holes, _showMask}
            c.Location = New Point(0, y)
            Controls.Add(c)
            y += 26
        Next
        AddHandler _decontam.CheckedChanged, Sub()
                                                 If _syncing Then Return
                                                 _options.Decontaminate = _decontam.Checked
                                                 RaiseEvent OptionsChanged(Me, EventArgs.Empty)
                                             End Sub
        AddHandler _specks.CheckedChanged, Sub()
                                               If _syncing Then Return
                                               _options.RemoveSpecks = _specks.Checked
                                               Recompute()
                                           End Sub
        AddHandler _holes.CheckedChanged, Sub()
                                              If _syncing Then Return
                                              _options.FillHoles = _holes.Checked
                                              Recompute()
                                          End Sub
        AddHandler _showMask.CheckedChanged, Sub() RebuildView()
        _help?.SetHelp("cut.decontam", _decontam)
        _help?.SetHelp("cut.specks", _specks)
        _help?.SetHelp("cut.holes", _holes)
        _help?.SetHelp("cut.showmask", _showMask)
        y += 6
        Dim half = (width - 8) \ 2
        _keepBrush.SetBounds(0, y, half, 30)
        _eraseBrush.SetBounds(half + 8, y, half, 30)
        For Each b In {_keepBrush, _eraseBrush}
            Dim me1 = b
            AddHandler b.CheckedChanged, Sub()
                                             If me1.Checked Then
                                                 For Each other In {_keepBrush, _eraseBrush}
                                                     If other IsNot me1 Then other.Checked = False
                                                 Next
                                             End If
                                             RaiseEvent ViewChanged(Me, EventArgs.Empty)
                                         End Sub
        Next
        _help?.SetHelp("cut.keep", _keepBrush)
        _help?.SetHelp("cut.erase", _eraseBrush)
        Controls.AddRange({_keepBrush, _eraseBrush})
        y += 40
        y = AddSlider(y, Lang.T("筆刷大小"), _brushSize, 4, 120, 24, "cut.brush", Function(v) v.ToString())
        _undo.SetBounds(0, y, half, 30)
        _clear.SetBounds(half + 8, y, half, 30)
        AddHandler _undo.Click, Sub() UndoStroke()
        AddHandler _clear.Click, Sub() ClearStrokes()
        _help?.SetHelp("cut.undo", _undo)
        _help?.SetHelp("cut.clear", _clear)
        Controls.AddRange({_undo, _clear})
        y += 34
        Size = New Size(width, y)
        AddHandler _recomputeTimer.Tick, Sub()
                                             _recomputeTimer.Stop()
                                             Recompute()
                                         End Sub
        UpdateButtons()
    End Sub

    Private Function AddSlider(y As Integer, caption As String, s As Aqua.Slider, min As Integer, max As Integer, value As Integer, helpKey As String, fmt As Func(Of Integer, String)) As Integer
        Dim l As New Label With {.Text = caption, .AutoSize = False, .BackColor = Color.Transparent}
        l.SetBounds(0, y + 3, 90, 22)
        s.Minimum = min : s.Maximum = max : s.Value = value : s.ShowTicks = False
        s.SetBounds(92, y, _w - 92 - 52, 24)
        Dim v As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight, .Text = fmt(value)}
        v.SetBounds(_w - 50, y + 3, 50, 22)
        AddHandler s.ValueChanged, Sub() v.Text = fmt(s.Value)
        Controls.AddRange({l, s, v})
        _help?.SetHelpLinked(helpKey, s, l, s)
        Return y + 30
    End Function

    Private Sub FillModels(selected As CutoutModel)
        _syncing = True
        _model.BeginUpdate()
        _model.Items.Clear()
        For Each m In CutoutModels.Order
            _model.Items.Add(CutoutModels.DisplayName(m) & If(BackgroundRemover.ModelAvailable(m), If(_aiCache.ContainsKey(m), Lang.T("（算過）"), ""), Lang.T("（未下載）")))
        Next
        _model.SelectedIndex = Math.Max(0, Array.IndexOf(CutoutModels.Order, selected))
        _model.EndUpdate()
        _syncing = False
    End Sub

    Private Sub ModelPicked()
        If _syncing OrElse _model.SelectedIndex < 0 Then Return
        RaiseEvent ModelChanged(Me, EventArgs.Empty)
        Dim m = Model
        Dim cached As Byte() = Nothing
        If _aiCache.TryGetValue(m, cached) Then
            ' 算過的模型：直接換回來
            UseAiMask(cached, m)
            Status(Lang.F("換回「{0}」的去背結果（之前算過）。", CutoutModels.DisplayName(m)))
        ElseIf Not BackgroundRemover.ModelAvailable(m) Then
            Status(Lang.F("「{0}」模型還沒下載：請到 設定 → 模型 下載（或換一個已下載的模型）。", CutoutModels.DisplayName(m)))
        ElseIf _figure IsNot Nothing Then
            Status(Lang.F("已選「{0}」：按「{1}」用這個模型重新去背。", CutoutModels.DisplayName(m), _run.Text))
        End If
    End Sub

    Private Sub Status(text As String)
        RaiseEvent StatusChanged(Me, text)
    End Sub

    Private Sub UpdateButtons()
        _run.Enabled = _figure IsNot Nothing AndAlso Not _busy
        _undo.Enabled = _strokes.Count > 0
        _clear.Enabled = _strokes.Count > 0
    End Sub

    '=====================================================================
    ' 狀態
    '=====================================================================

    ''' <summary>最後的遮罩（和人物圖同大小）；還沒去背時 Nothing。</summary>
    Friend ReadOnly Property Mask As Byte()
        Get
            Return _mask
        End Get
    End Property

    ''' <summary>目前用的 AI 遮罩（原圖本來透明時是原圖的透明度）。</summary>
    Friend ReadOnly Property AiMask As Byte()
        Get
            Return _aiMask
        End Get
    End Property

    ''' <summary>遮罩來自原圖本身的透明度（不是 AI 算的）。</summary>
    Friend ReadOnly Property AiMaskIsSource As Boolean
        Get
            Return _aiMaskIsSource
        End Get
    End Property

    ''' <summary>下拉選的模型。</summary>
    Friend ReadOnly Property Model As CutoutModel
        Get
            Return CutoutModels.Order(Math.Max(0, _model.SelectedIndex))
        End Get
    End Property

    ''' <summary>目前的遮罩是哪個模型算的。</summary>
    Friend ReadOnly Property MaskModel As CutoutModel
        Get
            Return _aiModel
        End Get
    End Property

    Friend ReadOnly Property Options As CutoutEditOptions
        Get
            Return _options
        End Get
    End Property

    Friend ReadOnly Property StrokeCount As Integer
        Get
            Return _strokes.Count
        End Get
    End Property

    Friend ReadOnly Property Busy As Boolean
        Get
            Return _busy
        End Get
    End Property

    ''' <summary>預覽要顯示人物＋遮罩（勾了「檢查遮罩」或選了筆刷）。</summary>
    Friend ReadOnly Property WantsMaskView As Boolean
        Get
            Return _showMask.Checked OrElse BrushActive
        End Get
    End Property

    Friend ReadOnly Property BrushActive As Boolean
        Get
            Return _keepBrush.Checked OrElse _eraseBrush.Checked
        End Get
    End Property

    ''' <summary>AI 遮罩先把模型留下的淡淡一層清掉（吶喊用：人放到畫裡，不要留一層霧）。</summary>
    Friend Property StretchFaint As Boolean

    Friend ReadOnly Property FigureWidth As Integer
        Get
            Return _fw
        End Get
    End Property

    Friend ReadOnly Property FigureHeight As Integer
        Get
            Return _fh
        End Get
    End Property

    ''' <summary>
    ''' 換人物：記下圖與像素（面板不負責釋放 figure），清掉筆刷與算過的遮罩。
    ''' aiMask 有值時直接用（原圖的透明度，或修改既有圖層時的 AI 遮罩）。
    ''' </summary>
    Friend Sub SetFigure(figure As Bitmap, px As Byte(), aiMask As Byte(), aiMaskIsSource As Boolean, Optional model As CutoutModel? = Nothing)
        _figure = figure
        _px = px
        _fw = figure.Width : _fh = figure.Height
        _aiCache.Clear()
        _strokes.Clear()
        _current = Nothing
        _aiMask = aiMask
        _aiMaskIsSource = aiMaskIsSource
        If model.HasValue Then _aiModel = model.Value Else If Not aiMaskIsSource Then _aiModel = CutoutModels.Preferred(_portrait, AddressOf BackgroundRemover.ModelAvailable)
        If aiMask IsNot Nothing AndAlso Not aiMaskIsSource Then _aiCache(_aiModel) = aiMask
        FillModels(_aiModel)
        _mask = Nothing
        If aiMask IsNot Nothing Then _mask = CutoutEdit.Build(_aiMask, _aiMaskIsSource, _px, _fw, _fh, _aiModel, _options, Nothing)
        RebuildView()
        UpdateButtons()
        RaiseEvent MaskChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>直接指定最後的遮罩（修改既有的合成圖層：讀回存好的遮罩，不重算）。</summary>
    Friend Sub SetFinalMask(mask As Byte())
        _mask = mask
        RebuildView()
    End Sub

    ''' <summary>還原設定（修改既有圖層時）；不重算遮罩。</summary>
    Friend Sub SetOptions(o As CutoutEditOptions)
        _options = o.Clone()
        _syncing = True
        _refine.Value = Math.Clamp(o.EdgeRefine, 0, 100)
        _shift.Value = Math.Clamp(o.EdgeShift, -10, 10)
        _decontam.Checked = o.Decontaminate
        _specks.Checked = o.RemoveSpecks
        _holes.Checked = o.FillHoles
        _syncing = False
    End Sub

    Friend Sub SelectModel(m As CutoutModel)
        FillModels(m)
    End Sub

    '=====================================================================
    ' 去背
    '=====================================================================

    ''' <summary>AI 去背（背景執行緒也可以呼叫）：回傳和 figure 同大小的遮罩。</summary>
    Friend Shared Function ComputeAi(figure As Bitmap, model As CutoutModel, stretchFaint As Boolean) As Byte()
        Dim m = CompositeBaker.AiMask(figure, model)
        Return If(stretchFaint, CutoutEdit.StretchFaint(m), m)
    End Function

    ''' <summary>用下拉選的模型去背（處理中視窗）；成功回傳 True。測試也用這個。</summary>
    Friend Async Function RunCutoutAsync(owner As Form) As Task(Of Boolean)
        If _figure Is Nothing OrElse _busy Then Return False
        Dim m = Model
        If Not BackgroundRemover.ModelAvailable(m) Then
            Status(Lang.F("「{0}」模型還沒下載：請到 設定 → 模型 下載（或換一個已下載的模型）。", CutoutModels.DisplayName(m)))
            Return False
        End If
        _busy = True
        UpdateButtons()
        Dim copy = DirectCast(_figure.Clone(), Bitmap)
        Dim stretch = StretchFaint
        Dim watch = Diagnostics.Stopwatch.StartNew()
        Status(Lang.T("去背計算中…"))
        Try
            Dim result = Await BusyDialog.RunAsync(owner, Lang.T("去背中"),
                                                   Function(p)
                                                       p.Stage(1, 1, Lang.F("AI 去背：{0}", CutoutModels.DisplayName(m)))
                                                       Return ComputeAi(copy, m, stretch)
                                                   End Function, Lang.T("第一次載入模型會久一點。"))
            If IsDisposed Then Return False
            ApplyAiMask(result, m)
            Status(Lang.F("去背完成（{0:0.0} 秒，{1}）。可以用筆刷修正，或調整邊緣。", watch.Elapsed.TotalSeconds, CutoutModels.DisplayName(m)))
            Return True
        Catch ex As Exception ' 模型、記憶體、檔案的各種錯誤都只顯示在狀態列
            Status(Lang.T("去背失敗：") & ex.Message)
            Return False
        Finally
            copy.Dispose()
            _busy = False
            UpdateButtons()
        End Try
    End Function

    ''' <summary>算好的 AI 遮罩交給面板（記住這個模型的結果；筆刷清掉）。</summary>
    Friend Sub ApplyAiMask(mask As Byte(), m As CutoutModel)
        _aiCache(m) = mask
        _strokes.Clear()
        FillModels(m)
        UseAiMask(mask, m)
    End Sub

    Private Sub UseAiMask(mask As Byte(), m As CutoutModel)
        _aiMask = mask
        _aiMaskIsSource = False
        _aiModel = m
        Recompute()
    End Sub

    Private Sub ScheduleRecompute()
        _recomputeTimer.Stop()
        _recomputeTimer.Start()
    End Sub

    ''' <summary>由 AI 遮罩重算最後的遮罩（設定、筆刷）。</summary>
    Friend Sub Recompute()
        _recomputeTimer.Stop()
        If _aiMask Is Nothing Then Return
        _mask = CutoutEdit.Build(_aiMask, _aiMaskIsSource, _px, _fw, _fh, _aiModel, _options, _strokes.SelectMany(Function(s) s))
        RebuildView()
        UpdateButtons()
        RaiseEvent MaskChanged(Me, EventArgs.Empty)
    End Sub

    '=====================================================================
    ' 筆刷
    '=====================================================================

    ''' <summary>開始一筆（figurePoint＝人物圖像素；viewScale＝畫面像素 / 人物像素）。沒有選筆刷時回傳 False。</summary>
    Friend Function BeginStroke(figurePoint As PointF, viewScale As Single) As Boolean
        If Not BrushActive OrElse _mask Is Nothing OrElse _busy Then Return False
        _current = New List(Of CutoutDab)()
        _strokes.Add(_current)
        ContinueStroke(figurePoint, viewScale)
        Return True
    End Function

    Friend Sub ContinueStroke(figurePoint As PointF, viewScale As Single)
        If _current Is Nothing Then Return
        Dim r = CSng(_brushSize.Value / 2.0 / Math.Max(0.0001, viewScale))
        Dim s As New CutoutDab(figurePoint.X, figurePoint.Y, r, _keepBrush.Checked)
        _current.Add(s)
        CutoutEdit.PaintDab(_mask, _fw, _fh, s.X, s.Y, s.R, s.Keep)
        UpdateView(Rectangle.FromLTRB(CInt(s.X - r - 2), CInt(s.Y - r - 2), CInt(s.X + r + 3), CInt(s.Y + r + 3)))
    End Sub

    Friend Sub EndStroke()
        If _current Is Nothing Then Return
        _current = Nothing
        UpdateButtons()
        RaiseEvent MaskChanged(Me, EventArgs.Empty)
    End Sub

    Friend Sub UndoStroke()
        If _strokes.Count = 0 Then Return
        _strokes.RemoveAt(_strokes.Count - 1)
        Recompute()
    End Sub

    Friend Sub ClearStrokes()
        _strokes.Clear()
        Recompute()
    End Sub

    ''' <summary>筆刷在畫面上的半徑（畫游標用）。</summary>
    Friend ReadOnly Property BrushScreenRadius As Single
        Get
            Return _brushSize.Value / 2.0F
        End Get
    End Property

    '=====================================================================
    ' 預覽
    '=====================================================================

    Private Sub RebuildView()
        _view?.Dispose()
        _view = Nothing
        If _figure IsNot Nothing Then
            _view = New Bitmap(_fw, _fh, PixelFormat.Format32bppArgb)
            UpdateView(New Rectangle(0, 0, _fw, _fh))
        Else
            RaiseEvent ViewChanged(Me, EventArgs.Empty)
        End If
    End Sub

    ''' <summary>預覽只更新 r 這一塊：人物＋遮罩（透明），或「檢查遮罩」時把去掉的部分蓋紅色。</summary>
    Private Sub UpdateView(r As Rectangle)
        If _view Is Nothing Then Return
        r.Intersect(New Rectangle(0, 0, _fw, _fh))
        If r.Width <= 0 OrElse r.Height <= 0 Then Return
        Dim red = _showMask.Checked
        Dim row(r.Width * 4 - 1) As Byte
        Dim d = _view.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To r.Height - 1
                Dim fy = r.Y + y
                For x = 0 To r.Width - 1
                    Dim i = fy * _fw + r.X + x
                    Dim a = If(_mask Is Nothing, 255, CInt(_mask(i)))
                    Dim b = _px(i * 4), g = _px(i * 4 + 1), rr = _px(i * 4 + 2)
                    If red Then
                        Dim k = (255 - a) * 0.6 / 255
                        row(x * 4) = CByte(b * (1 - k))
                        row(x * 4 + 1) = CByte(g * (1 - k))
                        row(x * 4 + 2) = CByte(rr * (1 - k) + 255 * k)
                        row(x * 4 + 3) = 255
                    Else
                        row(x * 4) = b : row(x * 4 + 1) = g : row(x * 4 + 2) = rr
                        row(x * 4 + 3) = CByte(a)
                    End If
                Next
                Runtime.InteropServices.Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, row.Length)
            Next
        Finally
            _view.UnlockBits(d)
        End Try
        RaiseEvent ViewChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>在 rect（畫面座標）畫人物＋遮罩（棋盤格當透明）；hover 有值且選了筆刷時畫筆刷圓圈。</summary>
    Friend Sub DrawView(g As Graphics, rect As RectangleF, Optional hover As Point? = Nothing)
        If _view Is Nothing Then Return
        DrawChecker(g, Rectangle.Round(rect))
        g.DrawImage(_view, rect)
        If hover.HasValue AndAlso BrushActive Then
            Dim r = BrushScreenRadius
            Using pen As New Pen(If(_keepBrush.Checked, Color.FromArgb(220, 40, 200, 90), Color.FromArgb(220, 230, 60, 60)), 1.5F)
                g.DrawEllipse(pen, hover.Value.X - r, hover.Value.Y - r, 2 * r, 2 * r)
            End Using
        End If
    End Sub

    Friend Shared Sub DrawChecker(g As Graphics, r As Rectangle)
        Const cell = 12
        Using light As New SolidBrush(Color.FromArgb(250, 250, 250)), dark As New SolidBrush(Color.FromArgb(220, 222, 226))
            g.FillRectangle(light, r)
            For y = r.Top To r.Bottom - 1 Step cell
                For x = r.Left + If(((y - r.Top) \ cell) Mod 2 = 0, 0, cell) To r.Right - 1 Step cell * 2
                    g.FillRectangle(dark, x, y, Math.Min(cell, r.Right - x), Math.Min(cell, r.Bottom - y))
                Next
            Next
        End Using
    End Sub

    ''' <summary>檢查遮罩的勾選（測試、呼叫的視窗用）。</summary>
    Friend Property ShowMask As Boolean
        Get
            Return _showMask.Checked
        End Get
        Set(value As Boolean)
            _showMask.Checked = value
        End Set
    End Property

    ''' <summary>選筆刷：1＝保留、-1＝擦除、0＝不用筆刷（測試用）。</summary>
    Friend Sub SetBrush(kind As Integer)
        _keepBrush.Checked = kind > 0
        _eraseBrush.Checked = kind < 0
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _recomputeTimer.Dispose()
            _view?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
