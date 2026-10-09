Imports System.Drawing

''' <summary>
''' 繪圖筆的筆壓曲線：每個人下筆的力道不同，把繪圖板讀到的筆壓（0..1）換成筆刷用的筆壓。
''' 軟硬：正值偏軟（輕輕畫就夠濃、夠粗），負值偏硬（要用力才會粗）；
''' 最小筆壓：最輕時也至少有這麼多；滿壓力道：按到這個力道就算全壓（手輕的人不必壓到底）。
''' </summary>
Public NotInheritable Class PenCurve
    ''' <summary>-100（硬）.. 100（軟），0 = 線性。</summary>
    Public Property Softness As Integer
    ''' <summary>0..50（%）。</summary>
    Public Property MinPressure As Integer
    ''' <summary>40..100（%）。</summary>
    Public Property FullPressure As Integer = 100

    Public Function Map(raw As Single) As Single
        Dim full = Math.Max(40, Math.Min(100, FullPressure)) / 100.0
        Dim x = Math.Max(0, Math.Min(1, raw / full))
        Dim gamma = Math.Pow(2, -Math.Max(-100, Math.Min(100, Softness)) / 50.0)
        Dim mn = Math.Max(0, Math.Min(50, MinPressure)) / 100.0
        Return CSng(mn + (1 - mn) * Math.Pow(x, gamma))
    End Function

    Public ReadOnly Property IsLinear As Boolean
        Get
            Return Softness = 0 AndAlso MinPressure = 0 AndAlso FullPressure >= 100
        End Get
    End Property
End Class

''' <summary>
''' 筆畫穩定器（拉線式，同 Krita 的「懶筆」、SAI 的修正）：筆尖用一條長 Radius 的線被游標拖著走，
''' 游標在線長範圍內的抖動不會畫出來，長弧線因此比較順。放開時筆尖補到游標的位置（不會短一截）。
''' 單位由呼叫端決定（編輯器用螢幕像素，縮放畫面時手感一樣）。
''' </summary>
Public NotInheritable Class StrokeStabilizer
    Private ReadOnly _radius As Single
    Private _anchor As PointF

    Public Sub New(start As PointF, radius As Single)
        _anchor = start
        _radius = Math.Max(0, radius)
    End Sub

    ''' <summary>筆尖（實際畫出來的位置）。</summary>
    Public ReadOnly Property Anchor As PointF
        Get
            Return _anchor
        End Get
    End Property

    Public ReadOnly Property Radius As Single
        Get
            Return _radius
        End Get
    End Property

    ''' <summary>游標移動：離筆尖超過線長時把筆尖沿著方向拉過去。回傳筆尖有沒有移動。</summary>
    Public Function Pull(cursor As PointF) As Boolean
        Dim dx = cursor.X - _anchor.X, dy = cursor.Y - _anchor.Y
        Dim d = CSng(Math.Sqrt(dx * dx + dy * dy))
        If d <= _radius Then Return False
        Dim k = (d - _radius) / d
        _anchor = New PointF(_anchor.X + dx * k, _anchor.Y + dy * k)
        Return True
    End Function

    ''' <summary>放開：從筆尖到游標每隔 stepLen 補一點（含游標本身）。</summary>
    Public Function Finish(cursor As PointF, stepLen As Single) As List(Of PointF)
        Dim result As New List(Of PointF)()
        Dim dx = cursor.X - _anchor.X, dy = cursor.Y - _anchor.Y
        Dim d = CSng(Math.Sqrt(dx * dx + dy * dy))
        If d < 0.5F Then Return result
        Dim n = Math.Max(1, CInt(Math.Ceiling(d / Math.Max(0.5F, stepLen))))
        For i = 1 To n
            result.Add(New PointF(_anchor.X + dx * i / n, _anchor.Y + dy * i / n))
        Next
        _anchor = cursor
        Return result
    End Function
End Class
