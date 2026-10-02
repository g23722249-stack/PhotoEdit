Imports System.Drawing

''' <summary>
''' 一筆污點／雜物移除筆觸。座標是「已轉正原圖」寬高的 0..1 比例，所以和預覽或全尺寸無關，
''' 之後旋轉、裁切也不受影響。
''' </summary>
Public Class SpotStroke
    ''' <summary>筆刷半徑，以原圖長邊為 1 的比例。</summary>
    Public Property Radius As Double
    ''' <summary>筆觸路徑 x0, y0, x1, y1, …（JSON 精簡）。</summary>
    Public Property Path As List(Of Double) = New List(Of Double)()

    Public Function Clone() As SpotStroke
        Return New SpotStroke With {.Radius = Radius, .Path = New List(Of Double)(Path)}
    End Function

    Public Sub AddPoint(p As PointF)
        Path.Add(Math.Round(p.X, 5))
        Path.Add(Math.Round(p.Y, 5))
    End Sub

    Public Function Points() As List(Of PointF)
        Dim list As New List(Of PointF)()
        For i = 0 To Path.Count - 2 Step 2
            list.Add(New PointF(CSng(Path(i)), CSng(Path(i + 1))))
        Next
        Return list
    End Function
End Class
