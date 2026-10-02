''' <summary>
''' 單行狀態列。不用 Label：Label 改文字時會把寬度設回最初指定的值（預設 100），停靠在底部也一樣，文字因此被擠成兩行。
''' </summary>
Friend Class StatusLine
    Inherits Control

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Height = 22
    End Sub

    Protected Overrides Sub OnTextChanged(e As EventArgs)
        MyBase.OnTextChanged(e)
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.Clear(BackColor)
        Dim r = New Rectangle(6, 0, Width - 12, Height)
        TextRenderer.DrawText(e.Graphics, Text, Font, r, ForeColor,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis)
    End Sub
End Class
