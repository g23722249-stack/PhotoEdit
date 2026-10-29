''' <summary>
''' 參數列：標籤＋滑桿＋數值框，兩者同步。拖曳時觸發 ValueChanged（即時預覽），放開滑鼠或數值框輸入完成時觸發 Committed（記錄復原點）。
''' </summary>
Friend NotInheritable Class ParamSlider
    Inherits UserControl

    Private ReadOnly _label As New Label()
    Private ReadOnly _track As New TrackBar()
    Private ReadOnly _num As New NumericUpDown()
    Private _min As Double, _max As Double, _decimals As Integer
    Private _suppress As Boolean
    Private Const Steps As Integer = 1000

    Public Event ValueChanged As EventHandler
    Public Event Committed As EventHandler

    Public Sub New(caption As String, min As Double, max As Double, decimals As Integer, Optional value As Double = 0, Optional tooltip As String = "")
        _min = min : _max = max : _decimals = decimals
        Height = 30
        Dock = DockStyle.Top
        _label.Text = caption
        _label.AutoSize = False
        _label.Width = 96
        _label.Dock = DockStyle.Left
        _label.TextAlign = ContentAlignment.MiddleLeft
        _num.Dock = DockStyle.Right
        _num.Width = 72
        _num.DecimalPlaces = decimals
        _num.Minimum = CDec(min)
        _num.Maximum = CDec(max)
        _num.Increment = CDec(Math.Pow(10, -decimals))
        _track.Dock = DockStyle.Fill
        _track.Minimum = 0
        _track.Maximum = Steps
        _track.TickStyle = TickStyle.None
        _track.AutoSize = False
        _track.Height = 26
        Controls.Add(_track)
        Controls.Add(_num)
        Controls.Add(_label)
        If Not String.IsNullOrEmpty(tooltip) Then
            Dim tt As New ToolTip()
            tt.SetToolTip(_label, tooltip)
            tt.SetToolTip(_track, tooltip)
        End If
        AddHandler _track.Scroll, AddressOf OnTrack
        AddHandler _track.MouseUp, Sub() RaiseEvent Committed(Me, EventArgs.Empty)
        AddHandler _track.KeyUp, Sub() RaiseEvent Committed(Me, EventArgs.Empty)
        AddHandler _num.ValueChanged, AddressOf OnNum
        AddHandler _num.Leave, Sub() RaiseEvent Committed(Me, EventArgs.Empty)
        AddHandler _num.KeyDown, Sub(s, e)
                                     If e.KeyCode = Keys.Enter Then RaiseEvent Committed(Me, EventArgs.Empty)
                                 End Sub
        Me.Value = value
    End Sub

    Public Property Value As Double
        Get
            Return CDbl(_num.Value)
        End Get
        Set(v As Double)
            _suppress = True
            Try
                v = Math.Clamp(If(Double.IsNaN(v), _min, v), _min, _max)
                _num.Value = CDec(Math.Round(v, _decimals))
                _track.Value = CInt(Math.Round((v - _min) / (_max - _min) * Steps))
            Finally
                _suppress = False
            End Try
        End Set
    End Property

    ''' <summary>改變範圍（例如 X、Y 依背景尺寸），目前值會夾在新範圍內。</summary>
    Public Sub SetRange(min As Double, max As Double)
        If max <= min Then max = min + 1
        Dim v = Value
        _suppress = True
        Try
            _min = min : _max = max
            _num.Minimum = CDec(min) : _num.Maximum = CDec(max)
        Finally
            _suppress = False
        End Try
        Value = v
    End Sub

    ''' <summary>設定值並觸發事件（例如重設按鈕）。</summary>
    Public Sub SetValueAndNotify(v As Double)
        Value = v
        RaiseEvent ValueChanged(Me, EventArgs.Empty)
        RaiseEvent Committed(Me, EventArgs.Empty)
    End Sub

    Private Sub OnTrack(sender As Object, e As EventArgs)
        If _suppress Then Return
        _suppress = True
        Try
            Dim v = _min + (_max - _min) * _track.Value / Steps
            _num.Value = CDec(Math.Round(Math.Clamp(v, _min, _max), _decimals))
        Finally
            _suppress = False
        End Try
        RaiseEvent ValueChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub OnNum(sender As Object, e As EventArgs)
        If _suppress Then Return
        _suppress = True
        Try
            _track.Value = CInt(Math.Round((CDbl(_num.Value) - _min) / (_max - _min) * Steps))
        Finally
            _suppress = False
        End Try
        RaiseEvent ValueChanged(Me, EventArgs.Empty)
    End Sub
End Class
