Imports System.Runtime.CompilerServices

''' <summary>
''' 淺色／深色配色。淺色就是原本的樣子；深色時：
''' 一般控制項（面板、標籤、按鈕、清單、下拉選單、文字框）由 Apply 走訪控制項樹換顏色，並記住原本的淺色，切回淺色時還原；
''' 自己畫的控制項（筆刷格子、工具列、圖層清單、直方圖…）在繪製時用 Back／Fore／Line 把淺色換成深色；
''' Aqua 的視窗外框、選單、分頁、滑桿由 Aqua.Theme 處理。
''' 每個視窗建好後呼叫 Attach，之後新增的控制項也會自動套用。
''' </summary>
Friend Module ThemeManager

    Private NotInheritable Class Original
        Public Back As Color
        Public Fore As Color
        Public Flat As FlatStyle
        Public UseVisualStyle As Boolean
        Public ComboFlat As FlatStyle
        Public BorderColor As Color
        Public OverColor As Color
        Public CheckedColor As Color
    End Class

    Private ReadOnly _originals As New ConditionalWeakTable(Of Control, Original)()
    Private ReadOnly _hooked As New ConditionalWeakTable(Of Control, Object)()
    Private _applying As Boolean

    ''' <summary>深色時的按鈕底色、外框、滑過色、主要文字色。</summary>
    Public ReadOnly ButtonBack As Color = Color.FromArgb(50, 55, 64)
    Public ReadOnly ButtonBorder As Color = Color.FromArgb(74, 81, 94)
    Public ReadOnly ButtonOver As Color = Color.FromArgb(62, 68, 80)

    Public ReadOnly Property Dark As Boolean
        Get
            Return Aqua.Theme.Dark
        End Get
    End Property

    ''' <summary>背景色：深色時淺的背景變深（本來就深的不動，例如畫布底色）。</summary>
    Public Function Back(c As Color) As Color
        If Not Dark OrElse c.A = 0 OrElse c.GetBrightness() < 0.5F Then Return c
        Return Aqua.Theme.Map(c)
    End Function

    ''' <summary>文字與線條色：深色時暗的變亮（本來就亮的不動，例如深色底上的白字）。</summary>
    Public Function Fore(c As Color) As Color
        If Not Dark OrElse c.A = 0 OrElse c.GetBrightness() >= 0.5F Then Return c
        If c.GetSaturation() > 0.35F Then
            ' 深色的彩色字（深藍標題、深紅提示）：同色相提亮，在深底上看得清楚
            Dim h = c.GetHue() / 360.0, s = Math.Min(0.75, c.GetSaturation()), l = 0.7
            Dim q = l + s - l * s, p = 2 * l - q
            Dim ch = Function(t As Double) As Integer
                         If t < 0 Then t += 1
                         If t > 1 Then t -= 1
                         Dim v = If(t < 1 / 6.0, p + (q - p) * 6 * t, If(t < 0.5, q, If(t < 2 / 3.0, p + (q - p) * (2 / 3.0 - t) * 6, p)))
                         Return CInt(Math.Round(Math.Max(0, Math.Min(1, v)) * 255))
                     End Function
            Return Color.FromArgb(c.A, ch(h + 1 / 3.0), ch(h), ch(h - 1 / 3.0))
        End If
        Return Aqua.Theme.Map(c)
    End Function

    ''' <summary>Tag 設成這個值的控制項不換顏色（例如顏色樣本按鈕）。</summary>
    Public Const SkipTag As String = "theme:skip"
    ''' <summary>連同子控制項整個不換色（例如 Aqua.ColorPickerWindow，自己跟著 Aqua.Theme 換）。</summary>
    Public Const SkipTreeTag As String = "theme:skiptree"

    ''' <summary>分隔線、外框等中間灰：深色時變成深底上看得到的灰。</summary>
    Public Function Line(c As Color) As Color
        If Not Dark OrElse c.A = 0 Then Return c
        Return Color.FromArgb(c.A, 70, 76, 88)
    End Function

    ''' <summary>切換配色：所有開著的視窗立刻重新套用。</summary>
    Public Sub SetDark(dark As Boolean)
        Aqua.Theme.Dark = dark
        For Each f As Form In Application.OpenForms.Cast(Of Form)().ToList()
            Apply(f)
            f.Invalidate(True)
        Next
    End Sub

    ''' <summary>視窗建好後呼叫：套用目前的配色，之後加進來的控制項也自動套用。</summary>
    Public Sub Attach(root As Control)
        Apply(root)
    End Sub

    ''' <summary>走訪 root 與所有子控制項，依目前配色設定顏色。</summary>
    Public Sub Apply(root As Control)
        If _applying Then Return
        _applying = True
        Try
            ApplyTree(root)
        Finally
            _applying = False
        End Try
    End Sub

    Private Sub ApplyTree(c As Control)
        ' 自己處理深淺色的元件（Aqua 的選色視窗跟著 Aqua.Theme 換色）：整棵樹都不碰
        If TypeOf c.Tag Is String AndAlso CStr(c.Tag) = SkipTreeTag Then Return
        ApplyOne(c)
        Dim hook As Object = Nothing
        If Not _hooked.TryGetValue(c, hook) Then
            _hooked.Add(c, New Object())
            AddHandler c.ControlAdded, Sub(s, e) Apply(e.Control)
        End If
        For Each child As Control In c.Controls
            ApplyTree(child)
        Next
    End Sub

    <Runtime.InteropServices.DllImport("uxtheme.dll", CharSet:=Runtime.InteropServices.CharSet.Unicode)>
    Private Function SetWindowTheme(hwnd As IntPtr, appName As String, idList As String) As Integer
    End Function

    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Function SendMessage(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Function RedrawWindow(hwnd As IntPtr, rect As IntPtr, rgn As IntPtr, flags As Integer) As Boolean
    End Function

    Private Const WM_THEMECHANGED As Integer = &H31A
    Private Const RDW_INVALIDATE As Integer = 1
    Private Const RDW_FRAME As Integer = &H400

    ''' <summary>系統畫的捲軸：深色時用 Windows 的深色主題（Windows 10 1809 之後才有，沒有時維持原樣）。</summary>
    Private Sub ThemeScrollBars(c As Control)
        Dim scrolls = TypeOf c Is ListBox OrElse TypeOf c Is TextBoxBase OrElse TypeOf c Is ScrollBar OrElse
                      (TypeOf c Is ScrollableControl AndAlso DirectCast(c, ScrollableControl).AutoScroll)
        If Not scrolls Then Return
        Dim apply = Sub()
                        Try
                            SetWindowTheme(c.Handle, If(Dark, "DarkMode_Explorer", "Explorer"), Nothing)
                            ' 捲軸畫在非工作區：通知視窗主題換了，外框（含捲軸）重畫
                            SendMessage(c.Handle, WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero)
                            RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero, RDW_FRAME Or RDW_INVALIDATE)
                        Catch ex As Exception When TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException
                        End Try
                    End Sub
        If c.IsHandleCreated Then
            apply()
        Else
            AddHandler c.HandleCreated, Sub() apply()
        End If
    End Sub

    ''' <summary>
    ''' 下拉清單（DropDownList）的系統外觀不理 BackColor：深色時改成自己畫（選取框與下拉項目都用深色），淺色時還原。
    ''' </summary>
    Private ReadOnly _comboHooked As New ConditionalWeakTable(Of ComboBox, Object)()

    Private Sub ThemeCombo(cb As ComboBox)
        If cb.DropDownStyle <> ComboBoxStyle.DropDownList Then Return
        If Dark Then
            Dim hook As Object = Nothing
            If Not _comboHooked.TryGetValue(cb, hook) Then
                _comboHooked.Add(cb, New Object())
                AddHandler cb.DrawItem, AddressOf ComboDrawItem
            End If
            cb.DrawMode = DrawMode.OwnerDrawFixed
        Else
            cb.DrawMode = DrawMode.Normal
        End If
    End Sub

    Private Sub ComboDrawItem(sender As Object, e As DrawItemEventArgs)
        Dim cb = DirectCast(sender, ComboBox)
        If cb.DrawMode = DrawMode.Normal Then Return
        Dim selected = (e.State And DrawItemState.Selected) <> 0 AndAlso (e.State And DrawItemState.ComboBoxEdit) = 0
        Dim back = If(selected, Color.FromArgb(47, 98, 170), Color.FromArgb(30, 33, 39))
        Using b As New SolidBrush(back)
            e.Graphics.FillRectangle(b, e.Bounds)
        End Using
        If e.Index >= 0 Then
            Dim fore = If(cb.Enabled, Aqua.Theme.TextColor, Color.FromArgb(120, 126, 136))
            TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items(e.Index)), cb.Font, e.Bounds, fore,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End If
    End Sub

    Private Sub ApplyOne(c As Control)
        If TypeOf c.Tag Is String AndAlso CStr(c.Tag) = SkipTag Then Return
        ThemeScrollBars(c)
        If TypeOf c Is ComboBox Then ThemeCombo(DirectCast(c, ComboBox))
        Dim o As Original = Nothing
        If Not _originals.TryGetValue(c, o) Then
            If Not Dark Then Return ' 從沒換過顏色、現在又是淺色：保持原樣
            o = New Original With {.Back = c.BackColor, .Fore = c.ForeColor}
            ' 沒有自己設定顏色的控制項是沿用父控制項的顏色：父控制項已經換成深色時，記父控制項原本的淺色。
            Dim parentOrig As Original = Nothing
            If c.Parent IsNot Nothing AndAlso _originals.TryGetValue(c.Parent, parentOrig) Then
                If c.ForeColor = c.Parent.ForeColor Then o.Fore = parentOrig.Fore
                If c.BackColor = c.Parent.BackColor Then o.Back = parentOrig.Back
            End If
            Dim b = TryCast(c, ButtonBase)
            If b IsNot Nothing Then
                o.Flat = b.FlatStyle
                o.BorderColor = b.FlatAppearance.BorderColor
                o.OverColor = b.FlatAppearance.MouseOverBackColor
                o.CheckedColor = b.FlatAppearance.CheckedBackColor
                Dim btn = TryCast(c, Button)
                If btn IsNot Nothing Then o.UseVisualStyle = btn.UseVisualStyleBackColor
                Dim chk = TryCast(c, CheckBox)
                If chk IsNot Nothing Then o.UseVisualStyle = chk.UseVisualStyleBackColor
            End If
            Dim cb = TryCast(c, ComboBox)
            If cb IsNot Nothing Then o.ComboFlat = cb.FlatStyle
            _originals.Add(c, o)
        End If

        If Not Dark Then
            ' 還原淺色
            c.BackColor = o.Back
            c.ForeColor = o.Fore
            Dim b = TryCast(c, ButtonBase)
            If b IsNot Nothing Then
                b.FlatStyle = o.Flat
                b.FlatAppearance.BorderColor = o.BorderColor
                b.FlatAppearance.MouseOverBackColor = o.OverColor
                b.FlatAppearance.CheckedBackColor = o.CheckedColor
                If TypeOf c Is Button Then DirectCast(c, Button).UseVisualStyleBackColor = o.UseVisualStyle
                If TypeOf c Is CheckBox AndAlso DirectCast(c, CheckBox).Appearance = Appearance.Button Then DirectCast(c, CheckBox).UseVisualStyleBackColor = o.UseVisualStyle
            End If
            Dim cb0 = TryCast(c, ComboBox)
            If cb0 IsNot Nothing Then cb0.FlatStyle = o.ComboFlat
            Return
        End If

        ' 深色
        Dim isPushButton = TypeOf c Is Button OrElse (TypeOf c Is CheckBox AndAlso DirectCast(c, CheckBox).Appearance = Appearance.Button) OrElse
                           (TypeOf c Is RadioButton AndAlso DirectCast(c, RadioButton).Appearance = Appearance.Button)
        If isPushButton Then
            Dim b = DirectCast(c, ButtonBase)
            ' 系統外觀的按鈕不理 BackColor：改成平面按鈕才換得了顏色。保留特意設定的彩色底（例如顏色樣本）。
            Dim custom = o.Back.A > 0 AndAlso o.Back.GetSaturation() > 0.25F AndAlso o.Back.GetBrightness() < 0.92F
            b.FlatStyle = FlatStyle.Flat
            b.FlatAppearance.BorderColor = ButtonBorder
            b.FlatAppearance.MouseOverBackColor = If(custom, o.Back, ButtonOver)
            ' 切換按鈕按下時的底色：用深藍（原本的淺藍、淺黃在深色上太刺眼）
            If o.CheckedColor.A > 0 Then b.FlatAppearance.CheckedBackColor = Color.FromArgb(47, 98, 170)
            If TypeOf c Is Button Then DirectCast(c, Button).UseVisualStyleBackColor = False
            If TypeOf c Is CheckBox Then DirectCast(c, CheckBox).UseVisualStyleBackColor = False
            c.BackColor = If(custom, o.Back, ButtonBack)
            c.ForeColor = If(custom, o.Fore, Aqua.Theme.TextColor)
            Return
        End If
        Dim combo = TryCast(c, ComboBox)
        If combo IsNot Nothing Then combo.FlatStyle = FlatStyle.Flat
        If TypeOf c Is TextBoxBase OrElse TypeOf c Is ListBox OrElse TypeOf c Is ComboBox OrElse TypeOf c Is NumericUpDown Then
            c.BackColor = Color.FromArgb(30, 33, 39)
            c.ForeColor = Aqua.Theme.TextColor
            Return
        End If
        c.BackColor = Back(o.Back)
        c.ForeColor = Fore(o.Fore)
    End Sub
End Module
