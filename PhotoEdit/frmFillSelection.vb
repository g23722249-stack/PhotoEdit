Imports PhotoEdit

''' <summary>選取分頁「填滿…」的選擇結果。</summary>
Friend NotInheritable Class FillChoice
    Public Content As FillKind = FillKind.Color
    Public ColorArgb As Integer
    ''' <summary>第二色：漸層的終點。</summary>
    Public Color2Argb As Integer = Color.White.ToArgb()
    ''' <summary>填滿圖層的名稱（空白時自動命名）。</summary>
    Public Name As String = ""
    Public Gradient As GradientKind
    Public Direction As Integer
    Public GradientColors As GradientColors
    Public Material As MaterialKind
    Public Opacity As Integer = 100
    Public Blend As BlendMode
    ''' <summary>True：填在選取的點陣圖層；False：新增圖層。</summary>
    Public IntoLayer As Boolean
    Public KeepAlpha As Boolean

    Public Function Clone() As FillChoice
        Return DirectCast(MemberwiseClone(), FillChoice)
    End Function
End Class

Friend Enum FillKind
    Color = 0
    Gradient = 1
    Material = 2
    ContentAware = 3
End Enum

''' <summary>
''' 填滿選取範圍（同 Photoshop 的「編輯 → 填滿」）：顏色、漸層、材質、內容感知；不透明度、混合模式；
''' 填在新圖層或選取的點陣圖層，填在既有圖層時可以「保留透明度」。
''' </summary>
Friend Class frmFillSelection
    Inherits Aqua.AquaForm

    Public Shared ReadOnly DirectionNames As String() = {"左 → 右", "右 → 左", "上 → 下", "下 → 上", "左上 → 右下", "右下 → 左上"}

    Private ReadOnly _choice As FillChoice
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10.0F)
    Private ReadOnly _rbColor As New RadioButton With {.Text = "顏色"}
    Private ReadOnly _rbGradient As New RadioButton With {.Text = "漸層"}
    Private ReadOnly _rbMaterial As New RadioButton With {.Text = "材質"}
    Private ReadOnly _rbAware As New RadioButton With {.Text = "內容感知"}
    Private ReadOnly _swatch As New Button With {.FlatStyle = FlatStyle.Flat, .Tag = ThemeManager.SkipTag}
    Private ReadOnly _gradKind As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _gradDir As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _gradColors As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _material As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _opacity As New NumericUpDown With {.Minimum = 0, .Maximum = 100}
    Private ReadOnly _blend As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _targetPanel As New Panel With {.BackColor = Color.Transparent}
    Private ReadOnly _rbNewLayer As New RadioButton With {.Text = "新圖層"}
    Private ReadOnly _rbIntoLayer As New RadioButton()
    Private ReadOnly _keepAlpha As New CheckBox With {.Text = "保留透明度（只改有內容的地方）"}
    Private ReadOnly _swatch2 As New Button With {.FlatStyle = FlatStyle.Flat, .Tag = ThemeManager.SkipTag}
    Private ReadOnly _name As New TextBox()
    Private ReadOnly _layerMode As Boolean

    ''' <param name="targetName">選取的點陣圖層名稱；沒有時 Nothing（只能填在新圖層）。</param>
    ''' <param name="layerMode">填滿圖層（新增或修改）：多一個名稱，沒有內容感知、填在、保留透明度。</param>
    Public Sub New(choice As FillChoice, targetName As String, help As HelpTip, Optional layerMode As Boolean = False)
        _choice = choice.Clone()
        _layerMode = layerMode
        Text = If(layerMode, "填滿圖層", "填滿選取範圍")
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        KeyPreview = True
        ClientSize = New Size(520, 418)

        Dim y = 23 + 14
        Dim heading = Function(t As String, top As Integer) As Label
                          Dim l As New Label With {.Text = t, .AutoSize = False, .Font = New Font(_font, FontStyle.Bold),
                                                   .ForeColor = Color.FromArgb(40, 70, 120), .BackColor = Color.Transparent}
                          l.SetBounds(18, top, 480, 22)
                          Controls.Add(l)
                          Return l
                      End Function
        If layerMode Then
            Dim nameCap As New Label With {.Text = "名稱", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(26, y + 4)}
            _name.SetBounds(126, y + 1, 240, 24)
            Controls.AddRange({nameCap, _name})
            help?.SetHelpLinked("fill.name", _name, nameCap, _name)
            y += 36
        End If
        heading("內容", y) : y += 28
        Dim row = Sub(rb As RadioButton, top As Integer)
                      rb.AutoSize = False
                      rb.BackColor = Color.Transparent
                      rb.SetBounds(26, top, 96, 26)
                      Controls.Add(rb)
                  End Sub
        row(_rbColor, y)
        _swatch.SetBounds(126, y, 80, 26)
        _swatch.FlatAppearance.BorderColor = Color.FromArgb(120, 126, 138)
        AddHandler _swatch.Click, Sub() PickColor()
        Controls.Add(_swatch)
        ' 第二色：漸層的終點顏色
        Dim c2Cap As New Label With {.Text = "第二色（漸層的終點）", .AutoSize = True, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(105, 110, 120),
                                     .Location = New Point(220, y + 4)}
        _swatch2.SetBounds(380, y, 80, 26)
        _swatch2.FlatAppearance.BorderColor = Color.FromArgb(120, 126, 138)
        AddHandler _swatch2.Click, Sub() PickColor2()
        Controls.AddRange({c2Cap, _swatch2})
        help?.SetHelpLinked("fill.color2", _swatch2, c2Cap, _swatch2)
        y += 34
        row(_rbGradient, y)
        _gradKind.Items.AddRange({"線性", "放射", "角度", "反射", "菱形", "四色"})
        _gradDir.Items.AddRange(DirectionNames)
        _gradColors.Items.AddRange({"顏色 → 第二色", "顏色 → 透明", "彩虹"})
        _gradKind.SetBounds(126, y + 1, 70, 24)
        _gradDir.SetBounds(202, y + 1, 112, 24)
        _gradColors.SetBounds(320, y + 1, 180, 24)
        Controls.AddRange({_gradKind, _gradDir, _gradColors})
        y += 34
        row(_rbMaterial, y)
        _material.Items.AddRange(EffectCatalog.MaterialCategories.SelectMany(Function(c) c.Items.Select(Function(i) c.Name & "：" & i.Name)).Cast(Of Object)().ToArray())
        _material.SetBounds(126, y + 1, 374, 24)
        _material.MaxDropDownItems = 16
        Controls.Add(_material)
        y += 34
        If Not layerMode Then
            row(_rbAware, y)
            Dim awareHint As New Label With {.Text = "用附近的照片補滿，適合移除雜物、電線、路人。", .AutoSize = False, .BackColor = Color.Transparent,
                                             .ForeColor = Color.FromArgb(105, 110, 120), .TextAlign = ContentAlignment.MiddleLeft}
            awareHint.SetBounds(126, y, 380, 26)
            Controls.Add(awareHint)
            y += 34
        End If
        y += 6

        heading("混合", y) : y += 28
        Dim cap = Function(t As String, x As Integer, top As Integer) As Label
                      Dim l As New Label With {.Text = t, .AutoSize = True, .BackColor = Color.Transparent}
                      l.Location = New Point(x, top + 4)
                      Controls.Add(l)
                      Return l
                  End Function
        Dim opCap = cap("不透明度", 26, y)
        _opacity.SetBounds(100, y + 1, 60, 24)
        Dim pct = cap("%", 162, y)
        Dim blendCap = cap("混合模式", 200, y)
        _blend.Items.AddRange(LayerBlend.Names)
        _blend.SetBounds(274, y + 1, 150, 24)
        Controls.AddRange({_opacity, _blend})
        y += 40

        If Not layerMode Then
            heading("填在", y) : y += 28
            _targetPanel.SetBounds(20, y, 490, 30)
            For Each rb In {_rbNewLayer, _rbIntoLayer}
                rb.AutoSize = False
                rb.BackColor = Color.Transparent
            Next
            _rbNewLayer.SetBounds(6, 0, 100, 26)
            _rbIntoLayer.SetBounds(110, 0, 380, 26)
            _rbIntoLayer.Text = If(targetName Is Nothing, "選取的點陣圖層（目前沒有）", $"選取的點陣圖層「{targetName}」")
            _rbIntoLayer.Enabled = targetName IsNot Nothing
            _targetPanel.Controls.AddRange({_rbNewLayer, _rbIntoLayer})
            Controls.Add(_targetPanel)
            y += 30
            _keepAlpha.AutoSize = False
            _keepAlpha.BackColor = Color.Transparent
            _keepAlpha.SetBounds(26, y, 400, 26)
            Controls.Add(_keepAlpha)
            y += 34
        End If
        ClientSize = New Size(520, y + 52)

        Dim ok As New Button With {.Text = "確定", .DialogResult = DialogResult.OK, .UseVisualStyleBackColor = True}
        Dim cancel As New Button With {.Text = "取消", .DialogResult = DialogResult.Cancel, .UseVisualStyleBackColor = True}
        ok.SetBounds(306, ClientSize.Height - 42, 96, 30)
        cancel.SetBounds(408, ClientSize.Height - 42, 96, 30)
        AcceptButton = ok
        CancelButton = cancel
        Controls.AddRange({ok, cancel})

        If help IsNot Nothing Then
            help.SetHelp("fill.color", _rbColor) : help.SetHelp("fill.color", _swatch)
            help.SetHelp("fill.gradient", _rbGradient) : help.SetHelp("fill.gradient", _gradKind) : help.SetHelp("fill.gradient", _gradDir) : help.SetHelp("fill.gradient", _gradColors)
            help.SetHelp("fill.material", _rbMaterial) : help.SetHelp("fill.material", _material)
            help.SetHelp("fill.aware", _rbAware)
            help.SetHelpLinked("fill.opacity", _opacity, opCap, _opacity, pct)
            help.SetHelpLinked("fill.blend", _blend, blendCap, _blend)
            help.SetHelp("fill.target", _rbNewLayer) : help.SetHelp("fill.target", _rbIntoLayer)
            help.SetHelp("fill.keepalpha", _keepAlpha)
        End If

        LoadChoice()
        For Each rb In {_rbColor, _rbGradient, _rbMaterial, _rbAware, _rbNewLayer, _rbIntoLayer}
            AddHandler rb.CheckedChanged, Sub() UpdateEnabled()
        Next
        ' 改了某一列的選項就選那一列
        AddHandler _gradKind.SelectedIndexChanged, Sub() _rbGradient.Checked = True
        AddHandler _gradDir.SelectedIndexChanged, Sub() _rbGradient.Checked = True
        AddHandler _gradColors.SelectedIndexChanged, Sub() _rbGradient.Checked = True
        AddHandler _material.SelectedIndexChanged, Sub() _rbMaterial.Checked = True
        UpdateEnabled()
        ThemeManager.Attach(Me)
    End Sub

    Public ReadOnly Property Choice As FillChoice
        Get
            Return _choice
        End Get
    End Property

    Private Sub LoadChoice()
        Dim c = _choice
        _rbColor.Checked = c.Content = FillKind.Color
        _rbGradient.Checked = c.Content = FillKind.Gradient
        _rbMaterial.Checked = c.Content = FillKind.Material
        _rbAware.Checked = c.Content = FillKind.ContentAware
        _swatch.BackColor = Color.FromArgb(255, Color.FromArgb(c.ColorArgb))
        _swatch2.BackColor = Color.FromArgb(255, Color.FromArgb(c.Color2Argb))
        _name.Text = c.Name
        _gradKind.SelectedIndex = Math.Max(0, Math.Min(5, CInt(c.Gradient)))
        _gradDir.SelectedIndex = Math.Max(0, Math.Min(DirectionNames.Length - 1, c.Direction))
        _gradColors.SelectedIndex = Math.Max(0, Math.Min(2, CInt(c.GradientColors)))
        _material.SelectedIndex = Math.Max(0, Math.Min(_material.Items.Count - 1, MaterialIndex(c.Material)))
        _opacity.Value = Math.Max(0, Math.Min(100, c.Opacity))
        _blend.SelectedIndex = Math.Max(0, Math.Min(LayerBlend.Names.Length - 1, CInt(c.Blend)))
        _rbIntoLayer.Checked = c.IntoLayer AndAlso _rbIntoLayer.Enabled
        _rbNewLayer.Checked = Not _rbIntoLayer.Checked
        _keepAlpha.Checked = c.KeepAlpha
    End Sub

    ''' <summary>材質下拉的順序（分類展開）對應 MaterialKind。</summary>
    Private Shared Function MaterialOrder() As List(Of MaterialKind)
        Return EffectCatalog.MaterialCategories.SelectMany(Function(c) c.Items.Select(Function(i) CType(i.Value, MaterialKind))).ToList()
    End Function

    Private Shared Function MaterialIndex(m As MaterialKind) As Integer
        Return Math.Max(0, MaterialOrder().IndexOf(m))
    End Function

    ''' <summary>漸層的顏色只在漸層時用；保留透明度只在填進既有圖層時能選。</summary>
    Private Sub UpdateEnabled()
        _keepAlpha.Enabled = _rbIntoLayer.Checked
        If Not _keepAlpha.Enabled Then _keepAlpha.Checked = False
    End Sub

    Private Sub PickColor()
        Using dlg As New Aqua.ColorPickerDialog With {.Color = _swatch.BackColor}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _swatch.BackColor = Color.FromArgb(255, dlg.Color)
            If Not _rbMaterial.Checked AndAlso Not _rbGradient.Checked Then _rbColor.Checked = True ' 漸層、材質也用這個顏色
        End Using
    End Sub

    Private Sub PickColor2()
        Using dlg As New Aqua.ColorPickerDialog With {.Color = _swatch2.BackColor}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _swatch2.BackColor = Color.FromArgb(255, dlg.Color)
            _rbGradient.Checked = True
        End Using
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If DialogResult = DialogResult.OK Then
            Dim c = _choice
            c.Content = If(_rbGradient.Checked, FillKind.Gradient, If(_rbMaterial.Checked, FillKind.Material, If(_rbAware.Checked, FillKind.ContentAware, FillKind.Color)))
            c.ColorArgb = _swatch.BackColor.ToArgb()
            c.Color2Argb = _swatch2.BackColor.ToArgb()
            c.Name = _name.Text.Trim()
            c.Gradient = CType(Math.Max(0, _gradKind.SelectedIndex), GradientKind)
            c.Direction = Math.Max(0, _gradDir.SelectedIndex)
            c.GradientColors = CType(Math.Max(0, _gradColors.SelectedIndex), GradientColors)
            c.Material = MaterialOrder()(Math.Max(0, _material.SelectedIndex))
            c.Opacity = CInt(_opacity.Value)
            c.Blend = CType(Math.Max(0, _blend.SelectedIndex), BlendMode)
            c.IntoLayer = _rbIntoLayer.Checked
            c.KeepAlpha = _keepAlpha.Checked
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _font.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
