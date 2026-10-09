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
''' 美顏手動調整：左邊臉部放大預覽（拉滑桿即時更新、按住看原圖；選美妝的眼妝、口紅時自動放大到那個部位），
''' 右邊六個頁籤：肌膚、五官、臉型、美妝、戲曲、光影。戲曲頁：京劇、歌仔戲的俊扮、臉譜、丑角。美妝頁像手機的美妝：分類 → 樣式小圖（用這張照片算）→ 顏色 → 滑桿。
''' 按「確定」才套到照片，「取消」不變。多張臉時可以切換要調哪一張。
''' </summary>
Friend Class frmBeautyAdjust
    Inherits Aqua.AquaForm

    Private Const PreviewSize As Integer = 470
    Private Const RightW As Integer = 500
    Private ReadOnly _targets As List(Of BeautyTarget)
    Private ReadOnly _look As EditRecipe
    Private ReadOnly _hasDense As Boolean
    Private ReadOnly _hasMesh As Boolean
    Private ReadOnly _help As HelpTip
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10.0F)
    Private ReadOnly _small As New Font("Microsoft JhengHei UI", 9.0F)
    Private ReadOnly _preview As New FacePreview()
    Private ReadOnly _target As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _tabButtons As New List(Of RadioButton)()
    Private ReadOnly _pages As New List(Of Panel)()
    Private ReadOnly _rows As New List(Of (Key As String, Slider As Aqua.Slider, Value As Label, GetV As Func(Of BeautySettings, Integer), Fmt As Func(Of Integer, String)))()
    Private ReadOnly _lightKind As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _lightSide As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _timer As New Timer With {.Interval = 70}
    Private ReadOnly _onLooksChanged As Action
    Private _current As Integer
    Private _syncing As Boolean
    Private _generation As Integer
    Private _showOriginal As Boolean

    ' 美妝頁
    Private ReadOnly _catButtons As New List(Of Button)()
    Private ReadOnly _styleGrid As New BeautyThumbGrid()
    Private ReadOnly _styleLabel As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _colorPanel As New FlowLayoutPanel With {.WrapContents = False, .BackColor = Color.Transparent}
    Private ReadOnly _makeupSliders As New Panel With {.BackColor = Color.Transparent}
    Private ReadOnly _makeupRows As New List(Of (Slider As Aqua.Slider, Value As Label, GetV As Func(Of BeautySettings, Integer)))()
    Private ReadOnly _makeupNote As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(150, 90, 40)}
    Private ReadOnly _deleteLook As New Button With {.Text = "刪除這組"}
    Private _cat As Integer
    Private _styleGeneration As Integer

    ''' <param name="look">照片的色調（只用 CopyLookFrom 的部分），預覽跟主畫面顏色一致。</param>
    ''' <param name="onLooksChanged">我的妝容新增或刪除後呼叫（存設定、更新一鍵美顏小圖）。</param>
    Public Sub New(targets As List(Of BeautyTarget), startIndex As Integer, look As EditRecipe, hasDense As Boolean, hasMesh As Boolean,
                   help As HelpTip, onLooksChanged As Action)
        _targets = targets
        _look = look
        _hasDense = hasDense
        _hasMesh = hasMesh
        _help = help
        _onLooksChanged = onLooksChanged
        _current = Math.Max(0, Math.Min(targets.Count - 1, startIndex))
        Text = "美顏手動調整"
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        KeyPreview = True
        ClientSize = New Size(PreviewSize + 40 + RightW, PreviewSize + 23 + 110)
        Dim top = 23 + 12

        ' 左：對象、預覽、按住看原圖、全部歸零
        Dim tcap As New Label With {.Text = "調整", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16, top + 4)}
        _target.Items.AddRange(targets.Select(Function(t) CObj(t.Name)).ToArray())
        _target.SetBounds(60, top, PreviewSize - 44, 26)
        _target.Enabled = targets.Count > 1
        AddHandler _target.SelectedIndexChanged, Sub()
                                                     If _syncing Then Return
                                                     _current = _target.SelectedIndex
                                                     SyncControls()
                                                     RequestPreview()
                                                     RefreshStyleTiles()
                                                     RefreshOperaTiles()
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

        ' 右：頁籤列＋各頁
        Dim rx = PreviewSize + 32
        Dim names = {"肌膚", "五官", "臉型", "美妝", "戲曲", "光影"}
        Dim tw = RightW \ names.Length
        For i = 0 To names.Length - 1
            Dim index = i
            Dim rb As New RadioButton With {.Appearance = Appearance.Button, .Text = names(i), .TextAlign = ContentAlignment.MiddleCenter,
                                            .FlatStyle = FlatStyle.Flat, .Tag = ThemeManager.SkipTag}
            rb.SetBounds(rx + i * tw, top, tw - 4, 30)
            rb.FlatAppearance.BorderSize = 1
            AddHandler rb.CheckedChanged, Sub()
                                              If rb.Checked Then ShowPage(index)
                                          End Sub
            _tabButtons.Add(rb)
            Controls.Add(rb)
            Dim pg As New Panel With {.AutoScroll = True, .BackColor = Color.Transparent, .Visible = False}
            pg.SetBounds(rx, top + 38, RightW, PreviewSize - 2)
            _pages.Add(pg)
            Controls.Add(pg)
        Next
        BuildSkinPage(_pages(0))
        BuildFeaturePage(_pages(1))
        BuildShapePage(_pages(2))
        BuildMakeupPage(_pages(3))
        BuildOperaPage(_pages(4))
        BuildLightPage(_pages(5))

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
        _tabButtons(0).Checked = True
        RequestPreview()
    End Sub

    Private ReadOnly Property Cur As BeautyTarget
        Get
            Return _targets(_current)
        End Get
    End Property

    ''' <summary>目前顯示的頁籤（0 肌膚、1 五官、2 臉型、3 美妝、4 戲曲、5 光影）。</summary>
    Public ReadOnly Property PageIndex As Integer
        Get
            Return _pages.FindIndex(Function(p) p.Visible)
        End Get
    End Property

    Public Sub ShowPage(index As Integer)
        For i = 0 To _pages.Count - 1
            _pages(i).Visible = i = index
            Dim isOn = i = index
            _tabButtons(i).BackColor = If(isOn, Color.FromArgb(47, 128, 237), If(ThemeManager.Dark, ThemeManager.ButtonBack, Color.White))
            _tabButtons(i).ForeColor = If(isOn, Color.White, ThemeManager.Fore(Color.FromArgb(40, 44, 52)))
            _tabButtons(i).FlatAppearance.BorderColor = If(isOn, Color.FromArgb(47, 128, 237), ThemeManager.Line(Color.FromArgb(200, 205, 214)))
            _tabButtons(i).FlatAppearance.CheckedBackColor = Color.FromArgb(47, 128, 237)
            If isOn AndAlso Not _tabButtons(i).Checked Then _tabButtons(i).Checked = True
        Next
        ' 美妝頁：預覽放大到目前分類的部位；其他頁看整張臉
        _preview.Zoom = If(index = 3, CategoryRegion(_cat), Nothing)
        If index = 3 Then RefreshStyleTiles()
        If index = 4 Then RefreshOperaTiles()
    End Sub

    '---------------------------------------------------------------------
    ' 一般滑桿頁
    '---------------------------------------------------------------------

    Private Function NewY(page As Panel) As Integer
        Return If(page.Controls.Count = 0, 0, page.Controls.Cast(Of Control)().Max(Function(c) c.Bottom))
    End Function

    Private Sub Heading(page As Panel, text As String)
        Dim y = NewY(page)
        Dim l As New Label With {.Text = text, .AutoSize = False, .Font = New Font(_font, FontStyle.Bold),
                                 .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
        l.SetBounds(4, y + 6, RightW - 30, 22)
        page.Controls.Add(l)
    End Sub

    ''' <summary>一行：名稱｜滑桿｜數值（數值按兩下歸零）。dense＝需要臉部特徵點。</summary>
    Private Function Row(page As Panel, key As String, caption As String, min As Integer, max As Integer,
                         getV As Func(Of BeautySettings, Integer), setV As Action(Of BeautySettings, Integer),
                         Optional dense As Boolean = False, Optional fmt As Func(Of Integer, String) = Nothing) As Aqua.Slider
        Dim y = NewY(page) + 4
        Dim w = RightW - SystemInformation.VerticalScrollBarWidth - 6
        Dim cap As New Label With {.Text = caption, .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
        cap.SetBounds(6, y, 84, 26)
        Dim s As New Aqua.Slider With {.Minimum = min, .Maximum = max, .ShowTicks = False}
        s.SetBounds(92, y + 1, w - 92 - 64, 24)
        Dim v As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        v.SetBounds(w - 62, y, 58, 26)
        Dim f = If(fmt, Function(n As Integer) n.ToString())
        AddHandler s.ValueChanged, Sub()
                                       v.Text = f(s.Value)
                                       If _syncing Then Return
                                       setV(Cur.Settings, s.Value)
                                       Changed()
                                   End Sub
        AddHandler v.DoubleClick, Sub() s.Value = Math.Max(min, 0)
        s.Enabled = Not dense OrElse _hasDense
        page.Controls.AddRange({cap, s, v})
        _help?.SetHelpLinked("row." & key, s, cap, s, v)
        _rows.Add((key, s, v, getV, f))
        Return s
    End Function

    Private Shared Function Signed(neg As String, pos As String) As Func(Of Integer, String)
        Return Function(n) If(n < 0, neg & " " & -n, If(n > 0, pos & " " & n, "0"))
    End Function

    Private Sub Note(page As Panel, text As String)
        Dim y = NewY(page) + 4
        Dim l As New Label With {.Text = text, .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120), .Font = _small}
        Dim h = TextRenderer.MeasureText(text, _small, New Size(RightW - 40, 0), TextFormatFlags.WordBreak).Height + 4
        l.SetBounds(6, y, RightW - 40, h)
        page.Controls.Add(l)
    End Sub

    Private Sub BuildSkinPage(p As Panel)
        Row(p, "skin", "磨皮", 0, 100, Function(b) b.Smoothing, Sub(b, v) b.Smoothing = v)
        Row(p, "even", "勻膚", 0, 100, Function(b) b.Even, Sub(b, v) b.Even = v)
        Row(p, "redness", "去紅", 0, 100, Function(b) b.Redness, Sub(b, v) b.Redness = v)
        Row(p, "whiten", "美白", 0, 100, Function(b) b.Whiten, Sub(b, v) b.Whiten = v)
        Row(p, "skintone", "膚色", -100, 100, Function(b) b.Tone, Sub(b, v) b.Tone = v, fmt:=Signed("白皙", "小麥"))
        Row(p, "tan", "曬黑", 0, 100, Function(b) b.Tan, Sub(b, v) b.Tan = v)
        Row(p, "facebright", "臉部提亮", 0, 100, Function(b) b.Brighten, Sub(b, v) b.Brighten = v)
        Row(p, "shine", "去油光", 0, 100, Function(b) b.Shine, Sub(b, v) b.Shine = v)
        Row(p, "blemish", "去痘", 0, 100, Function(b) b.Blemish, Sub(b, v) b.Blemish = v)
        Row(p, "darkcircle", "黑眼圈", 0, 100, Function(b) b.DarkCircles, Sub(b, v) b.DarkCircles = v)
    End Sub

    Private Sub BuildFeaturePage(p As Panel)
        Row(p, "eyebright", "亮眼", 0, 100, Function(b) b.Eyes, Sub(b, v) b.Eyes = v)
        Row(p, "eyeenlarge", "大眼", 0, 100, Function(b) b.EyeEnlarge, Sub(b, v) b.EyeEnlarge = v)
        Row(p, "teeth", "牙齒美白", 0, 100, Function(b) b.Teeth, Sub(b, v) b.Teeth = v)
        Row(p, "eyebag", "臥蠶", 0, 100, Function(b) b.EyeBag, Sub(b, v) b.EyeBag = v, dense:=True)
        Row(p, "contour", "修容", 0, 100, Function(b) b.Contour, Sub(b, v) b.Contour = v)
        Note(p, "口紅、眼影、眼線、睫毛、美瞳、雙眼皮、眉毛、腮紅、高光、髮色在「美妝」頁。")
    End Sub

    Private Sub BuildShapePage(p As Panel)
        Row(p, "faceslim", "瘦臉", 0, 100, Function(b) b.FaceSlim, Sub(b, v) b.FaceSlim = v, dense:=True)
        Row(p, "vface", "V 臉", 0, 100, Function(b) b.VFace, Sub(b, v) b.VFace = v, dense:=True)
        Row(p, "chin", "下巴", -100, 100, Function(b) b.Chin, Sub(b, v) b.Chin = v, dense:=True, fmt:=Signed("短", "長"))
        Row(p, "noseslim", "瘦鼻", 0, 100, Function(b) b.NoseSlim, Sub(b, v) b.NoseSlim = v, dense:=True)
        Row(p, "smile", "嘴角上揚", 0, 100, Function(b) b.Smile, Sub(b, v) b.Smile = v, dense:=True)
        Row(p, "lipfull", "豐唇", -100, 100, Function(b) b.LipFull, Sub(b, v) b.LipFull = v, dense:=True, fmt:=Signed("薄", "厚"))
        Row(p, "eyecorner", "開眼角", 0, 100, Function(b) b.EyeCorner, Sub(b, v) b.EyeCorner = v, dense:=True)
        If Not _hasDense Then Note(p, "需要臉部特徵點（Models\face_landmarks.onnx 或 lbfmodel.yaml），目前停用。")
        Note(p, "眉形（挑眉、平眉、柳葉）在「美妝」頁的眉毛。")
    End Sub

    Private Sub BuildLightPage(p As Panel)
        Dim y = NewY(p) + 4
        Dim lcap As New Label With {.Text = "種類", .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
        lcap.SetBounds(6, y, 84, 26)
        _lightKind.Items.AddRange({"無", "柔光", "林布蘭光", "側光"})
        _lightKind.SetBounds(92, y, 130, 26)
        _lightSide.Items.AddRange({"光從左邊", "光從右邊"})
        _lightSide.SetBounds(230, y, 120, 26)
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
        _help?.SetHelpLinked("beauty.light", _lightKind, lcap, _lightKind, _lightSide)
        p.Controls.AddRange({lcap, _lightKind, _lightSide})
        Row(p, "light", "強度", 0, 100, Function(b) b.Light, Sub(b, v) b.Light = v)
        Note(p, If(_hasMesh, "有 478 點網格時，林布蘭光與側光依臉的立體起伏打光。", "沒有 478 點網格時，光影以左右漸層表現。"))
    End Sub

    '---------------------------------------------------------------------
    ' 美妝頁：分類 → 樣式小圖 → 顏色 → 滑桿
    '---------------------------------------------------------------------

    ''' <summary>一個美妝分類：樣式、顏色、滑桿（第一個是濃度）、預覽放大的部位、需要的特徵點。</summary>
    Private NotInheritable Class MakeupCategory
        Public Name As String
        Public Styles As String() = {}
        Public GetStyle As Func(Of BeautySettings, Integer)
        Public SetStyle As Action(Of BeautySettings, Integer)
        Public Colors As Color() = {}
        Public GetColor As Func(Of BeautySettings, Color)
        Public SetColor As Action(Of BeautySettings, Integer)
        Public Sliders As New List(Of (Caption As String, Min As Integer, Max As Integer, GetV As Func(Of BeautySettings, Integer), SetV As Action(Of BeautySettings, Integer)))()
        ''' <summary>預覽放大的部位："eyes"、"lips"、"brows"、"face"、"hair"、"look"。</summary>
        Public Region As String = "face"
        Public NeedsDense As Boolean
        Public NeedsMesh As Boolean
        Public Note As String = ""
    End Class

    Private Shared Function C(hex As Integer) As Color
        Return Color.FromArgb(255, (hex >> 16) And 255, (hex >> 8) And 255, hex And 255)
    End Function

    Private ReadOnly _cats As MakeupCategory() = BuildCategories()

    Private Const MyLooksCat As Integer = 10

    Private Shared Function BuildCategories() As MakeupCategory()
        Dim list As New List(Of MakeupCategory)()
        Dim lip As New MakeupCategory With {.Name = "口紅", .Styles = {"霧面", "水潤", "咬唇", "漸層", "唇線"}, .Region = "lips", .NeedsDense = True,
            .GetStyle = Function(b) CInt(b.LipStyle), .SetStyle = Sub(b, v) b.LipStyle = CType(v, LipStyle),
            .Colors = {C(&HC83C55), C(&HE05A6B), C(&HB4243A), C(&HE8846A), C(&H9C2F4E), C(&HD9707E)},
            .GetColor = Function(b) b.LipColor, .SetColor = Sub(b, v) b.LipColorArgb = v}
        lip.Sliders.Add(("濃度", 0, 100, Function(b) b.Lips, Sub(b, v) b.Lips = v))
        lip.Sliders.Add(("光澤", 0, 100, Function(b) b.LipGloss, Sub(b, v) b.LipGloss = v))
        list.Add(lip)
        Dim shadow As New MakeupCategory With {.Name = "眼影", .Styles = {"單色", "大地漸層", "煙燻", "桃花", "亮片", "熊貓白"}, .Region = "eyes", .NeedsDense = True,
            .GetStyle = Function(b) CInt(b.ShadowStyle), .SetStyle = Sub(b, v) b.ShadowStyle = CType(v, ShadowStyle),
            .Colors = {C(&HC98A73), C(&HA8735E), C(&H7A5244), C(&HE39BB0), C(&HB9A2D6), C(&HD8B27A), C(&HFAFAF8)},
            .GetColor = Function(b) b.EyeShadowColor, .SetColor = Sub(b, v) b.EyeShadowColorArgb = v}
        shadow.Sliders.Add(("濃度", 0, 100, Function(b) b.EyeShadow, Sub(b, v) b.EyeShadow = v))
        shadow.Sliders.Add(("範圍", 0, 100, Function(b) b.ShadowSpread, Sub(b, v) b.ShadowSpread = v))
        shadow.Sliders.Add(("閃粉", 0, 100, Function(b) b.ShadowGlitter, Sub(b, v) b.ShadowGlitter = v))
        list.Add(shadow)
        Dim liner As New MakeupCategory With {.Name = "眼線", .Styles = {"自然", "上揚", "貓眼", "內眼線", "下眼線"}, .Region = "eyes", .NeedsDense = True,
            .GetStyle = Function(b) CInt(b.LinerStyle), .SetStyle = Sub(b, v) b.LinerStyle = CType(v, LinerStyle),
            .Colors = {C(&H1C1818), C(&H4A3328), C(&H3A3A55)},
            .GetColor = Function(b) b.LinerColor, .SetColor = Sub(b, v) b.LinerColorArgb = v}
        liner.Sliders.Add(("濃度", 0, 100, Function(b) b.EyeLiner, Sub(b, v) b.EyeLiner = v))
        liner.Sliders.Add(("粗細", 0, 100, Function(b) b.LinerWidth, Sub(b, v) b.LinerWidth = v))
        liner.Sliders.Add(("眼尾", 0, 100, Function(b) b.LinerWing, Sub(b, v) b.LinerWing = v))
        list.Add(liner)
        Dim lash As New MakeupCategory With {.Name = "睫毛", .Styles = {"自然", "濃密", "捲翹", "根根分明", "下睫毛"}, .Region = "eyes", .NeedsDense = True,
            .GetStyle = Function(b) CInt(b.LashStyle), .SetStyle = Sub(b, v) b.LashStyle = CType(v, LashStyle),
            .Colors = {C(&H141010), C(&H3E2A20)},
            .GetColor = Function(b) b.LashColor, .SetColor = Sub(b, v) b.LashColorArgb = v}
        lash.Sliders.Add(("濃度", 0, 100, Function(b) b.Lash, Sub(b, v) b.Lash = v))
        lash.Sliders.Add(("長度", 0, 150, Function(b) b.LashLength, Sub(b, v) b.LashLength = v)) ' 超過 100＝假睫毛（辣妹妝）
        lash.Sliders.Add(("捲翹", 0, 100, Function(b) b.LashCurl, Sub(b, v) b.LashCurl = v))
        list.Add(lash)
        Dim iris As New MakeupCategory With {.Name = "美瞳", .Styles = {"自然", "放大", "外圈", "混血", "亮眼"}, .Region = "eyes", .NeedsMesh = True,
            .GetStyle = Function(b) CInt(b.IrisStyle), .SetStyle = Sub(b, v) b.IrisStyle = CType(v, IrisStyle),
            .Colors = {C(&H785234), C(&H7A5A3A), C(&H6B7A86), C(&H4A6A8A), C(&H5A7A55), C(&H8A6A9A)},
            .GetColor = Function(b) b.IrisColor, .SetColor = Sub(b, v) b.IrisColorArgb = v,
            .Note = "需要 478 點臉部網格（虹膜點）。"}
        iris.Sliders.Add(("濃度", 0, 100, Function(b) b.Iris, Sub(b, v) b.Iris = v))
        iris.Sliders.Add(("放大", 0, 100, Function(b) b.IrisEnlarge, Sub(b, v) b.IrisEnlarge = v))
        iris.Sliders.Add(("外圈", 0, 100, Function(b) b.IrisRing, Sub(b, v) b.IrisRing = v))
        list.Add(iris)
        Dim fold As New MakeupCategory With {.Name = "雙眼皮", .Styles = {"平行", "開扇", "內雙", "歐式"}, .Region = "eyes", .NeedsDense = True,
            .GetStyle = Function(b) CInt(b.FoldStyle), .SetStyle = Sub(b, v) b.FoldStyle = CType(v, FoldStyle)}
        fold.Sliders.Add(("濃度", 0, 100, Function(b) b.Fold, Sub(b, v) b.Fold = v))
        fold.Sliders.Add(("寬度", 0, 100, Function(b) b.FoldWidth, Sub(b, v) b.FoldWidth = v))
        list.Add(fold)
        Dim brow As New MakeupCategory With {.Name = "眉毛", .Styles = {"原眉加深", "自然", "平眉", "挑眉", "柳葉眉"}, .Region = "brows", .NeedsDense = True,
            .GetStyle = Function(b) CInt(b.BrowStyle), .SetStyle = Sub(b, v) b.BrowStyle = CType(v, BrowStyle),
            .Colors = {C(&H5C4030), C(&H7A5A44), C(&H4A4A4A), C(&H2A2420)},
            .GetColor = Function(b) b.BrowColor, .SetColor = Sub(b, v) b.BrowColorArgb = v}
        brow.Sliders.Add(("濃度", 0, 100, Function(b) b.Brows, Sub(b, v) b.Brows = v))
        brow.Sliders.Add(("粗細", 0, 100, Function(b) b.BrowThick, Sub(b, v) b.BrowThick = v))
        brow.Sliders.Add(("眉峰", 0, 100, Function(b) b.BrowPeak, Sub(b, v) b.BrowPeak = v))
        list.Add(brow)
        Dim blush As New MakeupCategory With {.Name = "腮紅", .Styles = {"蘋果肌", "斜刷", "曬傷妝", "微醺"}, .Region = "face",
            .GetStyle = Function(b) CInt(b.BlushStyle), .SetStyle = Sub(b, v) b.BlushStyle = CType(v, BlushStyle),
            .Colors = {C(&HEC788C), C(&HF0A07A), C(&HE07A8A), C(&HD48AB0)},
            .GetColor = Function(b) b.BlushColor, .SetColor = Sub(b, v) b.BlushColorArgb = v}
        blush.Sliders.Add(("濃度", 0, 100, Function(b) b.Blush, Sub(b, v) b.Blush = v))
        blush.Sliders.Add(("範圍", 0, 100, Function(b) b.BlushSpread, Sub(b, v) b.BlushSpread = v))
        list.Add(blush)
        Dim hi As New MakeupCategory With {.Name = "高光", .Styles = {"鼻樑", "顴骨", "眉骨", "唇峰", "全臉"}, .Region = "face", .NeedsDense = True,
            .GetStyle = Function(b) CInt(b.HighlightStyle), .SetStyle = Sub(b, v) b.HighlightStyle = CType(v, HighlightStyle),
            .Colors = {C(&HFFF6EA), C(&HFFE9D6), C(&HF2F0FF)},
            .GetColor = Function(b) b.HighlightColor, .SetColor = Sub(b, v) b.HighlightColorArgb = v}
        hi.Sliders.Add(("濃度", 0, 100, Function(b) b.Highlight, Sub(b, v) b.Highlight = v))
        hi.Sliders.Add(("眼下", 0, 100, Function(b) b.UnderEye, Sub(b, v) b.UnderEye = v))
        hi.Sliders.Add(("白鼻樑", 0, 100, Function(b) b.WhiteNose, Sub(b, v) b.WhiteNose = v))
        list.Add(hi)
        Dim hair As New MakeupCategory With {.Name = "髮色", .Region = "hair",
            .Colors = {C(&H784628), C(&H963C3C), C(&HB48C64), C(&H7A7A82), C(&H3C2A22), C(&H5A3A6A)},
            .GetColor = Function(b) b.HairColor, .SetColor = Sub(b, v) b.HairColorArgb = v,
            .Note = "用人像去背模型找出頭髮（第一次約 1 秒）；沒有模型時不作用。"}
        hair.Sliders.Add(("濃度", 0, 100, Function(b) b.Hair, Sub(b, v) b.Hair = v))
        list.Add(hair)
        list.Add(New MakeupCategory With {.Name = "我的妝容", .Region = "look"})
        Return list.ToArray()
    End Function

    Private ReadOnly Property Cat As MakeupCategory
        Get
            Return _cats(_cat)
        End Get
    End Property

    Private Function CategoryUsable(c As MakeupCategory) As Boolean
        Return (Not c.NeedsDense OrElse _hasDense) AndAlso (Not c.NeedsMesh OrElse _hasMesh)
    End Function

    Private Sub BuildMakeupPage(p As Panel)
        ' 分類：兩排
        Dim bw = (RightW - 30) \ 6
        For i = 0 To _cats.Length - 1
            Dim index = i
            Dim btn As New Button With {.Text = _cats(i).Name, .FlatStyle = FlatStyle.Flat, .Tag = ThemeManager.SkipTag, .Font = _small}
            btn.SetBounds(4 + (i Mod 6) * (bw + 2), (i \ 6) * 34, bw, 30)
            AddHandler btn.Click, Sub() SelectCategory(index)
            _catButtons.Add(btn)
            p.Controls.Add(btn)
        Next
        Dim y = 72
        _styleLabel.Text = "樣式"
        _styleLabel.Font = _small
        _styleLabel.SetBounds(6, y, RightW - 40, 18)
        p.Controls.Add(_styleLabel)
        y += 20
        _styleGrid.SetBounds(4, y, RightW - 30, 116)
        AddHandler _styleGrid.ItemClicked, Sub(i) OnStyleClicked(i)
        _help?.SetHelp("makeup.style", _styleGrid)
        p.Controls.Add(_styleGrid)
        y += 122
        Dim ccap As New Label With {.Text = "顏色", .AutoSize = False, .BackColor = Color.Transparent, .Font = _small}
        ccap.SetBounds(6, y + 6, 40, 20)
        _colorPanel.SetBounds(48, y, RightW - 80, 32)
        p.Controls.AddRange({ccap, _colorPanel})
        y += 38
        _makeupSliders.SetBounds(0, y, RightW - 24, 3 * 32)
        p.Controls.Add(_makeupSliders)
        y += 3 * 32 + 2
        _makeupNote.SetBounds(6, y, RightW - 40, 34)
        _makeupNote.Font = _small
        p.Controls.Add(_makeupNote)
        y += 36
        Dim clearItem As New Button With {.Text = "這項歸零"}
        clearItem.SetBounds(6, y, 110, 30)
        AddHandler clearItem.Click, Sub() ClearCategory()
        _help?.SetHelp("makeup.clear", clearItem)
        Dim save As New Button With {.Text = "存成我的妝容"}
        save.SetBounds(122, y, 130, 30)
        AddHandler save.Click, Sub() SaveLook()
        _help?.SetHelp("makeup.save", save)
        _deleteLook.SetBounds(258, y, 100, 30)
        AddHandler _deleteLook.Click, Sub() DeleteLook()
        _help?.SetHelp("makeup.delete", _deleteLook)
        p.Controls.AddRange({clearItem, save, _deleteLook})
        SelectCategory(0)
    End Sub

    Private Sub SelectCategory(index As Integer)
        _cat = index
        For i = 0 To _catButtons.Count - 1
            Dim isOn = i = index
            _catButtons(i).BackColor = If(isOn, Color.FromArgb(47, 128, 237), If(ThemeManager.Dark, ThemeManager.ButtonBack, Color.White))
            _catButtons(i).ForeColor = If(isOn, Color.White, If(CategoryUsable(_cats(i)), ThemeManager.Fore(Color.FromArgb(40, 44, 52)), Color.FromArgb(150, 150, 150)))
            _catButtons(i).FlatAppearance.BorderColor = If(isOn, Color.FromArgb(47, 128, 237), ThemeManager.Line(Color.FromArgb(200, 205, 214)))
        Next
        Dim c = Cat
        Dim usable = CategoryUsable(c)
        ' 樣式（我的妝容：存過的組合）
        If index = MyLooksCat Then
            _styleLabel.Text = "我的妝容（點一下套用整組）"
            _styleGrid.SetItems(BeautySettings.CustomLooks.Select(Function(l) l.Name).ToArray())
        Else
            _styleLabel.Text = If(c.Styles.Length > 0, "樣式", "")
            _styleGrid.SetItems(c.Styles)
        End If
        _styleGrid.Visible = index = MyLooksCat OrElse c.Styles.Length > 0
        _styleGrid.Enabled = usable
        ' 顏色
        _colorPanel.Controls.Clear()
        For Each col In c.Colors
            Dim cc = col
            Dim dot As New Button With {.FlatStyle = FlatStyle.Flat, .BackColor = cc, .Tag = ThemeManager.SkipTag, .Margin = New Padding(3, 2, 3, 2), .Size = New Size(26, 26)}
            dot.FlatAppearance.BorderSize = 2
            AddHandler dot.Click, Sub() OnColorPicked(cc.ToArgb())
            dot.Enabled = usable
            _colorPanel.Controls.Add(dot)
        Next
        If c.Colors.Length > 0 Then
            Dim more As New Button With {.Text = "更多顏色…", .AutoSize = True, .Font = _small, .Margin = New Padding(6, 2, 0, 0), .Height = 26}
            AddHandler more.Click, Sub()
                                       Using dlg As New Aqua.ColorPickerDialog With {.Color = c.GetColor(Cur.Settings)}
                                           If dlg.ShowDialog(Me) = DialogResult.OK Then OnColorPicked(Color.FromArgb(255, dlg.Color).ToArgb())
                                       End Using
                                   End Sub
            more.Enabled = usable
            _colorPanel.Controls.Add(more)
        ElseIf index <> MyLooksCat Then
            _colorPanel.Controls.Add(New Label With {.Text = "（這項不用選顏色）", .AutoSize = True, .Font = _small, .ForeColor = Color.FromArgb(130, 130, 130), .Margin = New Padding(0, 6, 0, 0)})
        End If
        ' 滑桿
        _makeupSliders.Controls.Clear()
        _makeupRows.Clear()
        Dim w = _makeupSliders.Width
        For k = 0 To c.Sliders.Count - 1
            Dim sl = c.Sliders(k)
            Dim y = k * 32
            Dim cap As New Label With {.Text = sl.Caption, .AutoSize = False, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft}
            cap.SetBounds(6, y, 84, 26)
            Dim s As New Aqua.Slider With {.Minimum = sl.Min, .Maximum = sl.Max, .ShowTicks = False, .Enabled = usable}
            s.SetBounds(92, y + 1, w - 92 - 64, 24)
            Dim v As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
            v.SetBounds(w - 62, y, 58, 26)
            Dim setV = sl.SetV
            AddHandler s.ValueChanged, Sub()
                                           v.Text = s.Value.ToString()
                                           If _syncing Then Return
                                           setV(Cur.Settings, s.Value)
                                           Changed()
                                           _styleGrid.SelectedIndex = CurrentStyleIndex()
                                       End Sub
            _makeupSliders.Controls.AddRange({cap, s, v})
            _makeupRows.Add((s, v, sl.GetV))
        Next
        If index = MyLooksCat Then
            _makeupNote.Text = If(BeautySettings.CustomLooks.Count = 0, "還沒有存過。調好之後按「存成我的妝容」，也會出現在人像頁的一鍵美顏裡。",
                                  "存過的組合也會出現在人像頁一鍵美顏的最後面。")
        Else
            _makeupNote.Text = If(usable, c.Note, If(c.NeedsMesh, "需要 478 點臉部網格（Models\face_landmarks.onnx），目前停用。",
                                                                    "需要臉部特徵點（478 點網格或 68 點），目前停用。"))
        End If
        _deleteLook.Visible = index = MyLooksCat
        SyncMakeup()
        If PageIndex = 3 Then _preview.Zoom = CategoryRegion(index)
        RefreshStyleTiles()
    End Sub

    ''' <summary>目前分類用的樣式（濃度是 0 時算沒有選）。我的妝容：目前套用的是第幾組。</summary>
    Private Function CurrentStyleIndex() As Integer
        Dim s = Cur.Settings
        If _cat = MyLooksCat Then
            Dim idx = s.PresetIndex.GetValueOrDefault(-1) - BeautySettings.PresetNames.Length
            Return If(idx >= 0 AndAlso idx < BeautySettings.CustomLooks.Count, idx, -1)
        End If
        Dim c = Cat
        If c.Sliders.Count = 0 OrElse c.GetStyle Is Nothing Then Return -1
        Return If(c.Sliders(0).GetV(s) > 0, c.GetStyle(s), -1)
    End Function

    Private Sub OnStyleClicked(i As Integer)
        If _cat = MyLooksCat Then
            If i < 0 OrElse i >= BeautySettings.CustomLooks.Count Then Return
            Dim keep = (Cur.Settings.FaceX, Cur.Settings.FaceY)
            Cur.Settings.CopyValuesFrom(BeautySettings.CustomLooks(i).Look)
            Cur.Settings.FaceX = keep.FaceX : Cur.Settings.FaceY = keep.FaceY
            Changed()
            Cur.Settings.PresetIndex = BeautySettings.PresetNames.Length + i ' 一鍵美顏小圖上也選到這組
            Cur.Settings.PresetStrength = 100
            SyncControls()
            Return
        End If
        Dim c = Cat
        c.SetStyle(Cur.Settings, i)
        If c.Sliders(0).GetV(Cur.Settings) = 0 Then c.Sliders(0).SetV(Cur.Settings, 50) ' 點了樣式就看得到
        Changed()
        SyncControls()
    End Sub

    Private Sub OnColorPicked(argb As Integer)
        Dim c = Cat
        c.SetColor(Cur.Settings, argb)
        If c.Sliders.Count > 0 AndAlso c.Sliders(0).GetV(Cur.Settings) = 0 Then c.Sliders(0).SetV(Cur.Settings, 50)
        Changed()
        SyncControls()
        RefreshStyleTiles()
    End Sub

    ''' <summary>這項歸零：目前分類的濃度設 0（樣式、顏色保留）。</summary>
    Private Sub ClearCategory()
        If _cat = MyLooksCat OrElse Cat.Sliders.Count = 0 Then Return
        Cat.Sliders(0).SetV(Cur.Settings, 0)
        Changed()
        SyncControls()
    End Sub

    Private Sub SaveLook()
        Dim name = Microsoft.VisualBasic.Interaction.InputBox("妝容名稱：", "存成我的妝容", $"我的妝容 {BeautySettings.CustomLooks.Count + 1}")
        If String.IsNullOrWhiteSpace(name) Then Return
        Dim look = Cur.Settings.Clone()
        look.FaceX = Nothing : look.FaceY = Nothing : look.PresetIndex = Nothing : look.PresetStrength = 100
        BeautySettings.CustomLooks.Add((name.Trim(), look))
        Cur.Settings.PresetIndex = BeautySettings.PresetNames.Length + BeautySettings.CustomLooks.Count - 1
        Cur.Modified = True
        _onLooksChanged?.Invoke()
        If _cat = MyLooksCat Then SelectCategory(MyLooksCat)
    End Sub

    Private Sub DeleteLook()
        Dim i = _styleGrid.SelectedIndex
        If i < 0 OrElse i >= BeautySettings.CustomLooks.Count Then Return
        If MessageBox.Show(Me, $"刪除「{BeautySettings.CustomLooks(i).Name}」？", "我的妝容", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        BeautySettings.CustomLooks.RemoveAt(i)
        For Each t In _targets
            Dim pi = t.Settings.PresetIndex.GetValueOrDefault(-1) - BeautySettings.PresetNames.Length
            If pi = i Then
                t.Settings.PresetIndex = Nothing
            ElseIf pi > i Then
                t.Settings.PresetIndex -= 1
            End If
        Next
        _onLooksChanged?.Invoke()
        SelectCategory(MyLooksCat)
    End Sub

    Private Sub SyncMakeup()
        Dim was = _syncing
        _syncing = True
        Try
            For Each r In _makeupRows
                Dim v = Math.Max(r.Slider.Minimum, Math.Min(r.Slider.Maximum, r.GetV(Cur.Settings)))
                If r.Slider.Value <> v Then r.Slider.Value = v
                r.Value.Text = v.ToString()
            Next
            _styleGrid.SelectedIndex = CurrentStyleIndex()
            Dim c = Cat
            If c.GetColor IsNot Nothing Then
                Dim curArgb = c.GetColor(Cur.Settings).ToArgb()
                For Each ctl In _colorPanel.Controls.OfType(Of Button)().Where(Function(b) b.Text = "")
                    ctl.FlatAppearance.BorderColor = If(ctl.BackColor.ToArgb() = curArgb, Color.FromArgb(47, 128, 237), Color.FromArgb(200, 205, 214))
                Next
            End If
        Finally
            _syncing = was
        End Try
    End Sub

    ''' <summary>預覽要放大的部位（裁切圖的 0..1 座標，正方形）；Nothing＝整張臉。</summary>
    ''' <summary>
    ''' 預覽要放大的部位（裁切圖的 0..1 座標，正方形）；Nothing＝整張臉。
    ''' oneEye：只框一隻眼睛（眼妝的樣式小圖用，細節才看得清楚）。
    ''' </summary>
    Private Function CategoryRegion(index As Integer, Optional oneEye As Boolean = False) As RectangleF?
        Dim f = Cur.Face
        Dim region = _cats(index).Region
        If region = "face" OrElse region = "hair" OrElse region = "look" OrElse f Is Nothing Then Return Nothing
        Dim pts As IEnumerable(Of PointF)
        Dim factor = 1.15F
        If f.Dense IsNot Nothing Then
            Select Case region
                Case "lips" : pts = f.Dense.Skip(48).Take(20) : factor = 1.45F
                Case "brows"
                    pts = If(oneEye, f.Dense.Skip(17).Take(5).Concat(f.Dense.Skip(36).Take(6)), f.Dense.Skip(17).Take(10).Concat(f.Dense.Skip(36).Take(12)))
                    factor = 1.15F
                Case Else
                    pts = If(oneEye, f.Dense.Skip(36).Take(6), f.Dense.Skip(36).Take(12))
                    factor = If(oneEye, 1.7F, 1.12F)
            End Select
        Else
            pts = If(region = "lips", {f.Landmarks(3), f.Landmarks(4)}, {f.Landmarks(0), f.Landmarks(1)})
            factor = 1.6F
        End If
        Dim x0 = pts.Min(Function(q) q.X), x1 = pts.Max(Function(q) q.X), y0 = pts.Min(Function(q) q.Y), y1 = pts.Max(Function(q) q.Y)
        Dim cx = (x0 + x1) / 2, cy = (y0 + y1) / 2
        Dim side = Math.Max(x1 - x0, y1 - y0) * factor
        side = Math.Max(side, f.Box.Width * If(oneEye, 0.22F, 0.32F))
        Return New RectangleF(cx - side / 2, cy - side / 2, side, side)
    End Function


    ''' <summary>
    ''' 樣式小圖：用這張臉（縮小版）套上每個樣式（目前的顏色、濃度至少 50），切出那個部位；我的妝容：整張臉。在背景算，算好一張換一張。
    ''' </summary>
    Private Sub RefreshStyleTiles()
        If PageIndex <> 3 Then Return
        _styleGeneration += 1
        Dim gen = _styleGeneration
        Dim t = Cur
        Dim c = Cat
        Dim catIndex = _cat
        Dim count = If(catIndex = MyLooksCat, BeautySettings.CustomLooks.Count, c.Styles.Length)
        If count = 0 OrElse t.Crop Is Nothing Then Return
        Dim region = CategoryRegion(catIndex, oneEye:=c.Region = "eyes" OrElse c.Region = "brows")
        Dim baseSettings = t.Settings.Clone()
        Dim looks = BeautySettings.CustomLooks.Select(Function(l) l.Look.Clone()).ToList()
        Dim look = _look
        Dim usable = CategoryUsable(c)
        Dim small As Bitmap
        Using g0 As New Bitmap(t.Crop, 520, 520) ' 一隻眼睛放大成小圖，要夠大才清楚
            small = CType(g0.Clone(), Bitmap)
        End Using
        Dim face = t.Face
        Threading.Tasks.Task.Run(Sub()
                                     Try
                                         For i = 0 To count - 1
                                             If gen <> _styleGeneration Then Exit For
                                             Dim s = baseSettings.Clone()
                                             If catIndex = MyLooksCat Then
                                                 s = looks(i)
                                             ElseIf usable Then
                                                 c.SetStyle(s, i)
                                                 If c.Sliders(0).GetV(s) < 50 Then c.Sliders(0).SetV(s, 60)
                                             End If
                                             Dim img = BeautyPreviewRenderer.Render(small, face, s, look)
                                             Dim tile = CutTile(img, region, 120)
                                             img.Dispose()
                                             Dim index = i
                                             If IsDisposed OrElse Not IsHandleCreated Then tile.Dispose() : Exit For
                                             BeginInvoke(Sub()
                                                             If gen = _styleGeneration AndAlso Not IsDisposed Then _styleGrid.SetImage(index, tile) Else tile.Dispose()
                                                         End Sub)
                                         Next
                                     Catch ex As ObjectDisposedException
                                     Catch ex As InvalidOperationException
                                     Finally
                                         small.Dispose()
                                     End Try
                                 End Sub)
    End Sub

    Private Shared Function CutTile(img As Bitmap, region As RectangleF?, size As Integer) As Bitmap
        Dim r = If(region, New RectangleF(0.15F, 0.1F, 0.7F, 0.7F))
        Dim src As New RectangleF(r.X * img.Width, r.Y * img.Height, r.Width * img.Width, r.Height * img.Height)
        Dim tile As New Bitmap(size, size)
        Using g = Graphics.FromImage(tile)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.Half
            g.DrawImage(img, New RectangleF(0, 0, size, size), src, GraphicsUnit.Pixel)
        End Using
        Return tile
    End Function

    '---------------------------------------------------------------------
    ' 戲曲頁：分組（俊扮／臉譜／丑角）→ 角色小圖 → 濃度、吊眉、保留明暗、片子、髯口
    '---------------------------------------------------------------------

    Private ReadOnly _operaGroupButtons As New List(Of Button)()
    Private ReadOnly _operaGrid As New BeautyThumbGrid()
    Private ReadOnly _operaNote As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(150, 90, 40)}
    Private ReadOnly _operaPian As New CheckBox With {.Text = "貼片子", .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _operaBeard As New CheckBox With {.Text = "髯口", .AutoSize = True, .BackColor = Color.Transparent}
    Private _operaGroup As Integer
    Private _operaGeneration As Integer

    ''' <summary>某一組的角色編號（OperaRoles 索引＋1）。</summary>
    Private Shared Function OperaIds(group As Integer) As Integer()
        Return Enumerable.Range(0, OperaRoles.All.Count).Where(Function(i) CInt(OperaRoles.All(i).Group) = group).Select(Function(i) i + 1).ToArray()
    End Function

    Private Sub BuildOperaPage(p As Panel)
        Dim groups = {"俊扮（生、旦）", "臉譜（淨）", "丑角"}
        For i = 0 To groups.Length - 1
            Dim index = i
            Dim btn As New Button With {.Text = groups(i), .FlatStyle = FlatStyle.Flat, .Tag = ThemeManager.SkipTag, .Font = _small}
            btn.SetBounds(4 + i * 122, 0, 118, 30)
            AddHandler btn.Click, Sub() SelectOperaGroup(index)
            _help?.SetHelp("opera.group", btn)
            _operaGroupButtons.Add(btn)
            p.Controls.Add(btn)
        Next
        Dim clear As New Button With {.Text = "清除戲曲妝", .Font = _small}
        clear.SetBounds(RightW - 128, 0, 100, 30)
        AddHandler clear.Click, Sub()
                                    Cur.Settings.OperaRole = 0
                                    Cur.Settings.Opera = 0
                                    Changed()
                                    SyncControls()
                                End Sub
        _help?.SetHelp("opera.clear", clear)
        p.Controls.Add(clear)
        _operaGrid.SetBounds(4, 38, RightW - 30, 226)
        _operaGrid.Enabled = _hasDense
        AddHandler _operaGrid.ItemClicked, Sub(i)
                                               Dim ids = OperaIds(_operaGroup)
                                               If i < 0 OrElse i >= ids.Length Then Return
                                               Cur.Settings.SetOperaRole(ids(i))
                                               Changed()
                                               SyncControls()
                                           End Sub
        _help?.SetHelp("opera.style", _operaGrid)
        p.Controls.Add(_operaGrid)
        _operaNote.Font = _small
        _operaNote.SetBounds(6, 268, RightW - 40, 36)
        p.Controls.Add(_operaNote)
        Row(p, "opera", "濃度", 0, 100, Function(b) b.Opera, Sub(b, v) b.Opera = v, dense:=True)
        Row(p, "operalift", "吊眉", 0, 100, Function(b) b.OperaLift, Sub(b, v) b.OperaLift = v, dense:=True)
        Row(p, "operashade", "保留明暗", 0, 100, Function(b) b.OperaShade, Sub(b, v) b.OperaShade = v, dense:=True)
        Dim y = NewY(p) + 6
        _operaPian.Location = New Point(8, y)
        _operaBeard.Location = New Point(110, y)
        AddHandler _operaPian.CheckedChanged, Sub()
                                                  If _syncing Then Return
                                                  Cur.Settings.OperaPian = _operaPian.Checked
                                                  Changed()
                                              End Sub
        AddHandler _operaBeard.CheckedChanged, Sub()
                                                   If _syncing Then Return
                                                   Cur.Settings.OperaBeard = _operaBeard.Checked
                                                   Changed()
                                               End Sub
        _operaPian.Enabled = _hasDense
        _operaBeard.Enabled = _hasDense
        _help?.SetHelp("opera.pian", _operaPian)
        _help?.SetHelp("opera.beard", _operaBeard)
        p.Controls.AddRange({_operaPian, _operaBeard})
        SelectOperaGroup(0)
    End Sub

    Private Sub SelectOperaGroup(index As Integer)
        Dim changedGroup = index <> _operaGroup OrElse _operaGrid.Tag Is Nothing
        _operaGroup = index
        _operaGrid.Tag = index
        For i = 0 To _operaGroupButtons.Count - 1
            Dim isOn = i = index
            _operaGroupButtons(i).BackColor = If(isOn, Color.FromArgb(47, 128, 237), If(ThemeManager.Dark, ThemeManager.ButtonBack, Color.White))
            _operaGroupButtons(i).ForeColor = If(isOn, Color.White, ThemeManager.Fore(Color.FromArgb(40, 44, 52)))
            _operaGroupButtons(i).FlatAppearance.BorderColor = If(isOn, Color.FromArgb(47, 128, 237), ThemeManager.Line(Color.FromArgb(200, 205, 214)))
        Next
        If changedGroup Then
            _operaGrid.SetItems(OperaIds(index).Select(Function(id) OperaRoles.Get(id).Name).ToArray())
            RefreshOperaTiles()
        End If
        SyncOpera()
    End Sub

    ''' <summary>戲曲頁的小圖、說明、勾選跟著目前的設定。</summary>
    Private Sub SyncOpera()
        Dim was = _syncing
        _syncing = True
        Try
            Dim s = Cur.Settings
            Dim role = OperaRoles.Get(s.OperaRole)
            Dim ids = OperaIds(_operaGroup)
            _operaGrid.SelectedIndex = If(s.Opera > 0, Array.IndexOf(ids, s.OperaRole), -1)
            _operaNote.Text = If(Not _hasDense, "需要臉部特徵點（478 點網格或 68 點），目前停用。",
                                 If(role Is Nothing, "點一個角色套用。臉譜、丑角會整張臉重畫，一般美妝（口紅、眼影…）先不套用。",
                                    role.Name & "：" & role.Note))
            _operaPian.Checked = s.OperaPian
            _operaBeard.Checked = s.OperaBeard
        Finally
            _syncing = was
        End Try
    End Sub

    ''' <summary>角色小圖：用這張臉（縮小版）套上每個角色，在背景算，算好一張換一張。</summary>
    Private Sub RefreshOperaTiles()
        If PageIndex <> 4 OrElse Not _hasDense Then Return
        _operaGeneration += 1
        Dim gen = _operaGeneration
        Dim t = Cur
        If t.Crop Is Nothing Then Return
        Dim ids = OperaIds(_operaGroup)
        Dim baseSettings = t.Settings.Clone()
        Dim look = _look
        Dim small As Bitmap
        Using g0 As New Bitmap(t.Crop, 320, 320)
            small = CType(g0.Clone(), Bitmap)
        End Using
        Dim face = t.Face
        Threading.Tasks.Task.Run(Sub()
                                     Try
                                         For i = 0 To ids.Length - 1
                                             If gen <> _operaGeneration Then Exit For
                                             Dim s = baseSettings.Clone()
                                             s.Opera = 0
                                             s.SetOperaRole(ids(i))
                                             Dim img = BeautyPreviewRenderer.Render(small, face, s, look)
                                             Dim tile = CutTile(img, New RectangleF(0.1F, 0.04F, 0.8F, 0.8F), 120)
                                             img.Dispose()
                                             Dim index = i
                                             If IsDisposed OrElse Not IsHandleCreated Then tile.Dispose() : Exit For
                                             BeginInvoke(Sub()
                                                             If gen = _operaGeneration AndAlso Not IsDisposed Then _operaGrid.SetImage(index, tile) Else tile.Dispose()
                                                         End Sub)
                                         Next
                                     Catch ex As ObjectDisposedException
                                     Catch ex As InvalidOperationException
                                     Finally
                                         small.Dispose()
                                     End Try
                                 End Sub)
    End Sub

    '---------------------------------------------------------------------
    ' 同步、預覽
    '---------------------------------------------------------------------

    ''' <summary>對象換了或數值被整組改掉：滑桿、光影、美妝頁跟著同步。</summary>
    Private Sub SyncControls()
        _syncing = True
        Try
            Dim s = Cur.Settings
            For Each r In _rows
                Dim v = Math.Max(r.Slider.Minimum, Math.Min(r.Slider.Maximum, r.GetV(s)))
                If r.Slider.Value <> v Then r.Slider.Value = v
                r.Value.Text = r.Fmt(v)
            Next
            _lightKind.SelectedIndex = CInt(s.LightKind)
            _lightSide.SelectedIndex = If(s.LightFromRight, 1, 0)
            _lightSide.Enabled = s.LightKind = BeautyLight.Rembrandt OrElse s.LightKind = BeautyLight.Side
        Finally
            _syncing = False
        End Try
        SyncMakeup()
        ' 戲曲頁：目前的角色在別組時切過去
        Dim role = OperaRoles.Get(Cur.Settings.OperaRole)
        If role IsNot Nothing AndAlso Cur.Settings.Opera > 0 AndAlso CInt(role.Group) <> _operaGroup Then
            SelectOperaGroup(CInt(role.Group))
        Else
            SyncOpera()
        End If
    End Sub

    ''' <summary>手動改了數值：記為自訂（不再是某組一鍵美顏），重畫預覽。</summary>
    Private Sub Changed()
        Cur.Settings.PresetIndex = Nothing
        Cur.Settings.PresetStrength = 100
        Cur.Modified = True
        _lightSide.Enabled = Cur.Settings.LightKind = BeautyLight.Rembrandt OrElse Cur.Settings.LightKind = BeautyLight.Side
        RequestPreview()
    End Sub

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
            _styleGeneration += 1
            _timer.Dispose()
            _font.Dispose()
            _small.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>預覽圖：等比例置中，高品質縮放；Zoom 指定時只畫那一塊（放大到部位）。</summary>
    Private NotInheritable Class FacePreview
        Inherits Control
        Private _image As Bitmap
        Private _zoom As RectangleF?
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
        Public Property Zoom As RectangleF?
            Get
                Return _zoom
            End Get
            Set(value As RectangleF?)
                _zoom = value
                Invalidate()
            End Set
        End Property
        Public ReadOnly Property HasImage As Boolean
            Get
                Return _image IsNot Nothing
            End Get
        End Property
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            e.Graphics.Clear(BackColor)
            If _image Is Nothing Then Return
            Dim z = If(_zoom, New RectangleF(0, 0, 1, 1))
            Dim src As New RectangleF(z.X * _image.Width, z.Y * _image.Height, z.Width * _image.Width, z.Height * _image.Height)
            Dim k = Math.Min(Width / src.Width, Height / src.Height)
            Dim w = CSng(src.Width * k), h = CSng(src.Height * k)
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half
            e.Graphics.DrawImage(_image, New RectangleF((Width - w) / 2, (Height - h) / 2, w, h), src, GraphicsUnit.Pixel)
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
            .Dense = face.Dense?.Select(map).ToArray(),
            .Mesh = face.Mesh?.Select(map).ToArray(),
            .MeshZ = face.MeshZ?.Select(Function(z) CSng(z * W / side)).ToArray()}
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
