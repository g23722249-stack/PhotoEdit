Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>手動調整視窗的一個對象（全部的臉，或某一張臉）：預覽用的臉部裁切、臉的位置（裁切座標）、正在調整的數值。</summary>
Friend NotInheritable Class BeautyTarget
    Public Name As String
    Public Crop As Bitmap
    Public Face As FaceRegion
    Public Settings As BeautySettings
    Public Modified As Boolean
End Class

''' <summary>
''' 美顏手動調整：左邊臉部放大預覽（拉滑桿即時更新、按住看原圖），右邊全部的滑桿
''' （肌膚、五官、臉型與唇眉、妝容、光影）。按「確定」才套到照片，「取消」不變。多張臉時可以切換要調哪一張。
''' </summary>
Friend Class frmBeautyAdjust
    Inherits Aqua.AquaForm

    Private Const PreviewSize As Integer = 470
    Private ReadOnly _targets As List(Of BeautyTarget)
    Private ReadOnly _look As EditRecipe
    Private ReadOnly _hasDense As Boolean
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10.0F)
    Private ReadOnly _preview As New FacePreview()
    Private ReadOnly _target As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _scroll As New Panel With {.AutoScroll = True, .BackColor = Color.Transparent}
    Private ReadOnly _rows As New List(Of (Key As String, Slider As Aqua.Slider, Value As Label, GetV As Func(Of BeautySettings, Integer), Fmt As Func(Of Integer, String)))()
    Private ReadOnly _chips As New List(Of (Chip As Button, GetC As Func(Of BeautySettings, Color)))()
    Private ReadOnly _lightKind As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _lightSide As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _timer As New Timer With {.Interval = 70}
    Private _current As Integer
    Private _syncing As Boolean
    Private _generation As Integer
    Private _showOriginal As Boolean

    ''' <param name="look">照片的色調（只用 CopyLookFrom 的部分），預覽跟主畫面顏色一致。</param>
    Public Sub New(targets As List(Of BeautyTarget), startIndex As Integer, look As EditRecipe, hasDense As Boolean, help As HelpTip)
        _targets = targets
        _look = look
        _hasDense = hasDense
        _current = Math.Max(0, Math.Min(targets.Count - 1, startIndex))
        Text = "美顏手動調整"
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        KeyPreview = True
        ClientSize = New Size(PreviewSize + 36 + 470, PreviewSize + 23 + 110)
        Dim top = 23 + 12

        ' 左：對象、預覽、按住看原圖
        Dim tcap As New Label With {.Text = "調整", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16, top + 4)}
        _target.Items.AddRange(targets.Select(Function(t) CObj(t.Name)).ToArray())
        _target.SetBounds(60, top, PreviewSize - 44, 26)
        _target.Enabled = targets.Count > 1
        AddHandler _target.SelectedIndexChanged, Sub()
                                                     If _syncing Then Return
                                                     _current = _target.SelectedIndex
                                                     SyncControls()
                                                     RequestPreview()
                                                 End Sub
        help?.SetHelpLinked("beauty.target", _target, tcap, _target)
        _preview.SetBounds(16, top + 36, PreviewSize, PreviewSize)
        Dim orig As New Button With {.Text = "按住看原圖"}
        orig.SetBounds(16, top + 36 + PreviewSize + 8, 140, 32)
        AddHandler orig.MouseDown, Sub() SetShowOriginal(True)
        AddHandler orig.MouseUp, Sub() SetShowOriginal(False)
        AddHandler orig.MouseLeave, Sub() SetShowOriginal(False)
        Dim reset As New Button With {.Text = "全部歸零"}
        reset.SetBounds(164, top + 36 + PreviewSize + 8, 110, 32)
        AddHandler reset.Click, Sub()
                                    Dim s = Cur.Settings
                                    Dim keepFace = (s.FaceX, s.FaceY)
                                    s.CopyValuesFrom(New BeautySettings())
                                    s.FaceX = keepFace.FaceX : s.FaceY = keepFace.FaceY
                                    Changed()
                                    SyncControls()
                                End Sub
        help?.SetHelp("beauty.dlgreset", reset)
        Controls.AddRange({tcap, _target, _preview, orig, reset})

        ' 右：全部滑桿（可捲動）
        Dim rx = PreviewSize + 32
        _scroll.SetBounds(rx, top, ClientSize.Width - rx - 8, PreviewSize + 36)
        Controls.Add(_scroll)
        BuildSliders(help)

        ' 確定／取消
        Dim ok As New Button With {.Text = "確定", .DialogResult = DialogResult.OK}
        Dim cancel As New Button With {.Text = "取消", .DialogResult = DialogResult.Cancel}
        ok.SetBounds(ClientSize.Width - 220, ClientSize.Height - 48, 96, 32)
        cancel.SetBounds(ClientSize.Width - 116, ClientSize.Height - 48, 96, 32)
        Controls.AddRange({ok, cancel})
        AcceptButton = ok
        CancelButton = cancel

        AddHandler _timer.Tick, Sub()
                                    _timer.Stop()
                                    RenderPreview()
                                End Sub
        _syncing = True
        _target.SelectedIndex = _current
        _syncing = False
        SyncControls()
        ThemeManager.Attach(Me)
        RequestPreview()
    End Sub

    Private ReadOnly Property Cur As BeautyTarget
        Get
            Return _targets(_current)
        End Get
    End Property

    '---------------------------------------------------------------------
    ' 滑桿
    '---------------------------------------------------------------------

    Private Sub BuildSliders(help As HelpTip)
        Dim w = _scroll.Width - SystemInformation.VerticalScrollBarWidth - 8
        Dim y = 0
        Dim heading = Sub(t As String, note As String)
                          Dim l As New Label With {.Text = t & note, .AutoSize = False, .Font = New Font(_font, FontStyle.Bold),
                                                   .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
                          l.SetBounds(4, y + 6, w - 8, 22)
                          Dim line As New Label With {.BackColor = Color.FromArgb(190, 200, 215), .AutoSize = False}
                          line.SetBounds(4, y + 29, w - 8, 1)
                          _scroll.Controls.AddRange({l, line})
                          y += 36
                      End Sub
        Dim row = Sub(key As String, caption As String, min As Integer, max As Integer, getV As Func(Of BeautySettings, Integer),
                      setV As Action(Of BeautySettings, Integer), dense As Boolean, fmt As Func(Of Integer, String),
                      colorOf As Func(Of BeautySettings, Color), setColor As Action(Of BeautySettings, Integer))
                      Dim cap As New Label With {.Text = caption, .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
                      cap.SetBounds(6, y + 2, 78, 26)
                      Dim chipW = If(colorOf Is Nothing, 0, 34)
                      Dim s As New Aqua.Slider With {.Minimum = min, .Maximum = max, .ShowTicks = False}
                      s.SetBounds(86, y + 3, w - 86 - 64 - chipW, 24)
                      Dim v As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
                      v.SetBounds(w - 62 - chipW, y + 2, 58, 26)
                      Dim f = If(fmt, Function(n As Integer) n.ToString())
                      AddHandler s.ValueChanged, Sub()
                                                     v.Text = f(s.Value)
                                                     If _syncing Then Return
                                                     setV(Cur.Settings, s.Value)
                                                     Changed()
                                                 End Sub
                      AddHandler v.DoubleClick, Sub() s.Value = Math.Max(min, 0)
                      s.Enabled = Not dense OrElse _hasDense
                      _scroll.Controls.AddRange({cap, s, v})
                      help?.SetHelpLinked("row." & key, s, cap, s, v)
                      _rows.Add((key, s, v, getV, f))
                      If colorOf IsNot Nothing Then
                          Dim chip As New Button With {.FlatStyle = FlatStyle.Flat, .Tag = ThemeManager.SkipTag}
                          chip.SetBounds(w - 32, y + 3, 28, 24)
                          chip.FlatAppearance.BorderColor = Color.FromArgb(120, 126, 138)
                          chip.Enabled = s.Enabled
                          AddHandler chip.Click, Sub()
                                                     Using dlg As New Aqua.ColorPickerDialog With {.Color = colorOf(Cur.Settings)}
                                                         If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                                                         setColor(Cur.Settings, Color.FromArgb(255, dlg.Color).ToArgb())
                                                         If getV(Cur.Settings) = 0 Then setV(Cur.Settings, 35) ' 選了顏色就看得到
                                                         Changed()
                                                         SyncControls()
                                                     End Using
                                                 End Sub
                          _scroll.Controls.Add(chip)
                          _chips.Add((chip, colorOf))
                      End If
                      y += 32
                  End Sub
        Dim signedFmt = Function(neg As String, pos As String) As Func(Of Integer, String)
                            Return Function(n) If(n < 0, neg & " " & -n, If(n > 0, pos & " " & n, "0"))
                        End Function
        Dim denseNote = If(_hasDense, "", "（需要 68 點特徵點，目前停用）")

        heading("肌膚", "")
        row("skin", "磨皮", 0, 100, Function(b) b.Smoothing, Sub(b, v) b.Smoothing = v, False, Nothing, Nothing, Nothing)
        row("even", "勻膚", 0, 100, Function(b) b.Even, Sub(b, v) b.Even = v, False, Nothing, Nothing, Nothing)
        row("redness", "去紅", 0, 100, Function(b) b.Redness, Sub(b, v) b.Redness = v, False, Nothing, Nothing, Nothing)
        row("whiten", "美白", 0, 100, Function(b) b.Whiten, Sub(b, v) b.Whiten = v, False, Nothing, Nothing, Nothing)
        row("skintone", "膚色", -100, 100, Function(b) b.Tone, Sub(b, v) b.Tone = v, False, signedFmt("白皙", "小麥"), Nothing, Nothing)
        row("facebright", "臉部提亮", 0, 100, Function(b) b.Brighten, Sub(b, v) b.Brighten = v, False, Nothing, Nothing, Nothing)
        row("shine", "去油光", 0, 100, Function(b) b.Shine, Sub(b, v) b.Shine = v, False, Nothing, Nothing, Nothing)
        row("blemish", "去痘", 0, 100, Function(b) b.Blemish, Sub(b, v) b.Blemish = v, False, Nothing, Nothing, Nothing)
        row("darkcircle", "黑眼圈", 0, 100, Function(b) b.DarkCircles, Sub(b, v) b.DarkCircles = v, False, Nothing, Nothing, Nothing)
        heading("五官", "")
        row("eyebright", "亮眼", 0, 100, Function(b) b.Eyes, Sub(b, v) b.Eyes = v, False, Nothing, Nothing, Nothing)
        row("eyeenlarge", "大眼", 0, 100, Function(b) b.EyeEnlarge, Sub(b, v) b.EyeEnlarge = v, False, Nothing, Nothing, Nothing)
        row("teeth", "牙齒美白", 0, 100, Function(b) b.Teeth, Sub(b, v) b.Teeth = v, False, Nothing, Nothing, Nothing)
        row("blush", "腮紅", 0, 100, Function(b) b.Blush, Sub(b, v) b.Blush = v, False, Nothing, Function(b) b.BlushColor, Sub(b, c) b.BlushColorArgb = c)
        row("contour", "修容", 0, 100, Function(b) b.Contour, Sub(b, v) b.Contour = v, False, Nothing, Nothing, Nothing)
        heading("臉型與唇眉", denseNote)
        row("faceslim", "瘦臉", 0, 100, Function(b) b.FaceSlim, Sub(b, v) b.FaceSlim = v, True, Nothing, Nothing, Nothing)
        row("vface", "V 臉", 0, 100, Function(b) b.VFace, Sub(b, v) b.VFace = v, True, Nothing, Nothing, Nothing)
        row("chin", "下巴", -100, 100, Function(b) b.Chin, Sub(b, v) b.Chin = v, True, signedFmt("短", "長"), Nothing, Nothing)
        row("noseslim", "瘦鼻", 0, 100, Function(b) b.NoseSlim, Sub(b, v) b.NoseSlim = v, True, Nothing, Nothing, Nothing)
        row("lips", "唇色", 0, 100, Function(b) b.Lips, Sub(b, v) b.Lips = v, True, Nothing, Function(b) b.LipColor, Sub(b, c) b.LipColorArgb = c)
        row("brows", "眉毛", 0, 100, Function(b) b.Brows, Sub(b, v) b.Brows = v, True, Nothing, Nothing, Nothing)
        heading("妝容", denseNote)
        row("eyeshadow", "眼影", 0, 100, Function(b) b.EyeShadow, Sub(b, v) b.EyeShadow = v, True, Nothing, Function(b) b.EyeShadowColor, Sub(b, c) b.EyeShadowColorArgb = c)
        row("eyeliner", "眼線", 0, 100, Function(b) b.EyeLiner, Sub(b, v) b.EyeLiner = v, True, Nothing, Nothing, Nothing)
        row("eyebag", "臥蠶", 0, 100, Function(b) b.EyeBag, Sub(b, v) b.EyeBag = v, True, Nothing, Nothing, Nothing)
        heading("光影", "")
        Dim lcap As New Label With {.Text = "種類", .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
        lcap.SetBounds(6, y + 2, 78, 26)
        _lightKind.Items.AddRange({"無", "柔光", "林布蘭光", "側光"})
        _lightKind.SetBounds(86, y + 2, 130, 26)
        _lightSide.Items.AddRange({"光從左邊", "光從右邊"})
        _lightSide.SetBounds(224, y + 2, 120, 26)
        AddHandler _lightKind.SelectedIndexChanged, Sub()
                                                        If _syncing Then Return
                                                        Cur.Settings.LightKind = CType(_lightKind.SelectedIndex, BeautyLight)
                                                        If Cur.Settings.LightKind <> BeautyLight.None AndAlso Cur.Settings.Light = 0 Then Cur.Settings.Light = 60
                                                        Changed()
                                                        SyncControls()
                                                    End Sub
        AddHandler _lightSide.SelectedIndexChanged, Sub()
                                                        If _syncing Then Return
                                                        Cur.Settings.LightFromRight = _lightSide.SelectedIndex = 1
                                                        Changed()
                                                    End Sub
        help?.SetHelpLinked("beauty.light", _lightKind, lcap, _lightKind, _lightSide)
        _scroll.Controls.AddRange({lcap, _lightKind, _lightSide})
        y += 34
        row("light", "強度", 0, 100, Function(b) b.Light, Sub(b, v) b.Light = v, False, Nothing, Nothing, Nothing)
        Dim pad As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
        pad.SetBounds(0, y, 10, 8)
        _scroll.Controls.Add(pad)
    End Sub

    ''' <summary>對象換了或數值被整組改掉：滑桿、色塊、光影跟著同步。</summary>
    Private Sub SyncControls()
        _syncing = True
        Try
            Dim s = Cur.Settings
            For Each r In _rows
                Dim v = Math.Max(r.Slider.Minimum, Math.Min(r.Slider.Maximum, r.GetV(s)))
                If r.Slider.Value <> v Then r.Slider.Value = v
                r.Value.Text = r.Fmt(v)
            Next
            For Each c In _chips
                c.Chip.BackColor = c.GetC(s)
            Next
            _lightKind.SelectedIndex = CInt(s.LightKind)
            _lightSide.SelectedIndex = If(s.LightFromRight, 1, 0)
            _lightSide.Enabled = s.LightKind = BeautyLight.Rembrandt OrElse s.LightKind = BeautyLight.Side
        Finally
            _syncing = False
        End Try
    End Sub

    ''' <summary>手動改了數值：記為自訂（不再是某組一鍵美顏），重畫預覽。</summary>
    Private Sub Changed()
        Cur.Settings.PresetIndex = Nothing
        Cur.Settings.PresetStrength = 100
        Cur.Modified = True
        _lightSide.Enabled = Cur.Settings.LightKind = BeautyLight.Rembrandt OrElse Cur.Settings.LightKind = BeautyLight.Side
        RequestPreview()
    End Sub

    '---------------------------------------------------------------------
    ' 預覽
    '---------------------------------------------------------------------

    Private Sub SetShowOriginal(show As Boolean)
        If _showOriginal = show Then Return
        _showOriginal = show
        RenderPreview()
    End Sub

    Private Sub RequestPreview()
        _timer.Stop()
        _timer.Start()
    End Sub

    ''' <summary>在背景算預覽（美顏＋照片色調），算好再換上；拖曳滑桿時舊的結果直接丟掉。</summary>
    Private Sub RenderPreview()
        _generation += 1
        Dim gen = _generation
        Dim t = Cur
        Dim settings = If(_showOriginal, New BeautySettings(), t.Settings.Clone())
        Dim crop = t.Crop
        Dim face = t.Face
        Dim look = _look
        Threading.Tasks.Task.Run(Function() BeautyPreviewRenderer.Render(crop, face, settings, look)).ContinueWith(
            Sub(task)
                If task.Status <> Threading.Tasks.TaskStatus.RanToCompletion Then Return
                If IsDisposed OrElse Not IsHandleCreated Then task.Result?.Dispose() : Return
                BeginInvoke(Sub()
                                If gen <> _generation OrElse IsDisposed Then task.Result?.Dispose() : Return
                                _preview.SetImage(task.Result)
                            End Sub)
            End Sub)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _timer.Dispose()
            _font.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>預覽圖：等比例置中，高品質縮放。</summary>
    Private NotInheritable Class FacePreview
        Inherits Control
        Private _image As Bitmap
        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            BackColor = Color.FromArgb(30, 30, 32)
            Tag = ThemeManager.SkipTag
        End Sub
        Public Sub SetImage(b As Bitmap)
            _image?.Dispose()
            _image = b
            Invalidate()
        End Sub
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            e.Graphics.Clear(BackColor)
            If _image Is Nothing Then Return
            Dim k = Math.Min(Width / _image.Width, Height / _image.Height)
            Dim w = CSng(_image.Width * k), h = CSng(_image.Height * k)
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half
            e.Graphics.DrawImage(_image, (Width - w) / 2, (Height - h) / 2, w, h)
        End Sub
        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _image?.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class

''' <summary>美顏預覽（小圖、手動調整視窗）：臉部裁切 → 美顏 → 照片色調。</summary>
Friend NotInheritable Class BeautyPreviewRenderer
    Private Sub New()
    End Sub

    ''' <summary>從原圖裁出一張臉附近（約臉框的 1.5 倍，正方形），縮到 size；回傳裁切圖與換算到裁切座標的臉。</summary>
    Public Shared Function Crop(source As Bitmap, face As FaceRegion, size As Integer) As (Image As Bitmap, Face As FaceRegion)
        Dim W = source.Width, H = source.Height
        Dim bw = face.Box.Width * W, bh = face.Box.Height * H
        Dim side = CSng(Math.Max(bw, bh) * 1.5)
        side = Math.Min(side, Math.Min(W, H))
        Dim cx = face.Box.X * W + bw / 2, cy = face.Box.Y * H + bh * 0.45F
        Dim x0 = Math.Max(0, Math.Min(W - side, cx - side / 2)), y0 = Math.Max(0, Math.Min(H - side, cy - side / 2))
        Dim rect As New RectangleF(x0, y0, side, side)
        Dim img As New Bitmap(size, size, Imaging.PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(img)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.Half
            g.DrawImage(source, New RectangleF(0, 0, size, size), rect, GraphicsUnit.Pixel)
        End Using
        Dim map = Function(p As PointF) New PointF((p.X * W - x0) / side, (p.Y * H - y0) / side)
        Dim f As New FaceRegion With {
            .Box = New RectangleF((face.Box.X * W - x0) / side, (face.Box.Y * H - y0) / side, bw / side, bh / side),
            .Landmarks = face.Landmarks.Select(map).ToArray(),
            .Score = face.Score,
            .Dense = face.Dense?.Select(map).ToArray()}
        Return (img, f)
    End Function

    ''' <summary>裁切圖套上美顏與照片色調（look 只用色調、效果的部分）；回傳新圖。</summary>
    Public Shared Function Render(crop As Bitmap, face As FaceRegion, settings As BeautySettings, look As EditRecipe) As Bitmap
        Dim r As New EditRecipe()
        r.SetGlobalBeauty(settings)
        Dim beautified = If(PortraitRetouch.Apply(crop, {face}, r), CType(crop.Clone(), Bitmap))
        If look Is Nothing Then Return beautified
        Dim l As New EditRecipe()
        l.CopyLookFrom(look)
        l.Vignette = 0 : l.Grain = 0 ' 暗角、顆粒跟整張照片的位置有關，裁出來的臉不套
        If l.Equals(New EditRecipe()) Then Return beautified
        Dim colored = ImagePipeline.Render(beautified, l)
        beautified.Dispose()
        Return colored
    End Function
End Class
