Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 繪圖工具的圖示：以 24 × 24 的格子設計、用 GDI+ 向量畫出，任何大小與 DPI 都清楚。
''' 方框類形狀直接用 DrawGeometry 算外形，圖示和實際畫出來的形狀一致。
''' tool = -1 為「選取」，其餘為 DrawShape。
''' </summary>
Friend Module DrawIcons

    Public Const SelectTool As Integer = -1

    Public Sub DrawTool(g As Graphics, tool As Integer, r As RectangleF, color As Color)
        Dim k = Math.Min(r.Width, r.Height) / 24.0F
        Dim state = g.Save()
        Try
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TranslateTransform(r.X + (r.Width - 24 * k) / 2, r.Y + (r.Height - 24 * k) / 2)
            g.ScaleTransform(k, k)
            Using pen As New Pen(color, 1.6F) With {.LineJoin = LineJoin.Round, .StartCap = LineCap.Round, .EndCap = LineCap.Round},
                  path = IconPath(tool)
                g.DrawPath(pen, path)
                Select Case tool
                    Case DrawShape.Line
                        Dot(g, pen, 4, 19, 1.6F) : Dot(g, pen, 20, 5, 1.6F)
                    Case DrawShape.Bezier
                        Using dash As New Pen(color, 1.0F) With {.DashPattern = {2, 2}}
                            g.DrawLine(dash, 4, 18, 8, 7)
                            g.DrawLine(dash, 20, 6, 16, 17)
                        End Using
                        Dot(g, pen, 8, 7, 1.5F) : Dot(g, pen, 16, 17, 1.5F)
                    Case DrawShape.Freehand
                        ' 向量：中間錨點的切線把手＋三個錨點方塊（像鋼筆工具的路徑）。
                        Using thin As New Pen(color, 1.0F)
                            g.DrawLine(thin, 5.5F, 9.4F, 18.5F, 14.6F)
                        End Using
                        Using br As New SolidBrush(color)
                            g.FillEllipse(br, 3.8F, 7.7F, 3.4F, 3.4F)
                            g.FillEllipse(br, 16.8F, 12.9F, 3.4F, 3.4F)
                            g.FillRectangle(br, 1F, 16F, 4F, 4F)
                            g.FillRectangle(br, 19F, 4F, 4F, 4F)
                        End Using
                        Using bg As New SolidBrush(Color.White), thin As New Pen(color, 1.3F)
                            g.FillRectangle(bg, 9.8F, 9.8F, 4.4F, 4.4F)
                            g.DrawRectangle(thin, 9.8F, 9.8F, 4.4F, 4.4F)
                        End Using
                    Case DrawShape.Raster
                        ' 直接繪製：畫筆的金屬箍與沾了顏料的筆尖，下方一抹顏料。
                        Using br As New SolidBrush(color)
                            g.FillPolygon(br, {New PointF(11.2F, 10.4F), New PointF(13.6F, 12.8F), New PointF(11.6F, 14.8F), New PointF(9.2F, 12.4F)})
                            Using tip As New GraphicsPath()
                                tip.AddBezier(9.2F, 12.4F, 6.5F, 13.5F, 5.5F, 17F, 3.5F, 20.5F)
                                tip.AddBezier(3.5F, 20.5F, 7F, 18.5F, 10.5F, 17.5F, 11.6F, 14.8F)
                                tip.CloseFigure()
                                g.FillPath(br, tip)
                            End Using
                        End Using
                        Using swash As New Pen(color, 2.2F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                            g.DrawBezier(swash, 10.5F, 21F, 13.5F, 18.5F, 16.5F, 22.5F, 21F, 19F)
                        End Using
                End Select
            End Using
        Finally
            g.Restore(state)
        End Try
    End Sub

    Private Sub Dot(g As Graphics, pen As Pen, x As Single, y As Single, r As Single)
        g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2)
    End Sub

    Private Function IconPath(tool As Integer) As GraphicsPath
        Dim p As New GraphicsPath()
        Select Case tool
            Case SelectTool
                p.AddPolygon({New PointF(6, 3), New PointF(18, 11), New PointF(12.5F, 12.2F), New PointF(10, 18)})
            Case DrawShape.Freehand
                p.AddBezier(3, 18, 5, 11.5F, 8, 11, 12, 12)
                p.AddBezier(12, 12, 16, 13, 18, 8, 21, 6)
            Case DrawShape.Raster
                ' 筆桿（斜放）
                p.AddPolygon({New PointF(19.2F, 2.6F), New PointF(21.4F, 4.8F), New PointF(13.6F, 12.8F), New PointF(11.2F, 10.4F)})
            Case DrawShape.Line
                p.AddLine(4, 19, 20, 5)
            Case DrawShape.Bezier
                p.AddBezier(4, 18, 7, 4, 17, 20, 20, 6)
            Case DrawShape.Polygon
                p.AddPolygon({New PointF(12, 3), New PointF(20.5F, 9.2F), New PointF(17.2F, 19), New PointF(6.8F, 19), New PointF(3.5F, 9.2F)})
            Case Else
                Dim layer As New DrawLayer With {.Shape = CType(tool, DrawShape), .X = 12, .Y = 12, .W = 18, .H = 13, .Seed = 3}
                Select Case layer.Shape
                    Case DrawShape.Rectangle, DrawShape.RoundRect : layer.W = 16 : layer.H = 12
                    Case DrawShape.Star4, DrawShape.Star5, DrawShape.Star6, DrawShape.Heart : layer.W = 19 : layer.H = 19
                    Case DrawShape.Lightning : layer.W = 13 : layer.H = 20
                    Case DrawShape.Arrow : layer.H = 14
                End Select
                DrawGeometry.ApplyDefaults(layer)
                If layer.Shape = DrawShape.RoundRect Then layer.Param1 = 0.33
                If DrawGeometry.IsCallout(layer.Shape) Then
                    layer.Y = 10 : layer.W = 19 : layer.H = 12.5
                    layer.TailX = -6 : layer.TailY = 10.5
                    If layer.Shape = DrawShape.CalloutCloud Then layer.TailX = -7.5 : layer.TailY = 11
                    If layer.Shape = DrawShape.CalloutShout Then layer.Y = 11 : layer.W = 21 : layer.H = 17 : layer.TailX = -8 : layer.TailY = 10.5
                End If
                For Each f In DrawGeometry.Figures(layer)
                    If f.Points.Length >= 2 Then
                        p.StartFigure()
                        p.AddLines(f.Points)
                        If f.Closed Then p.CloseFigure()
                    End If
                Next
        End Select
        Return p
    End Function
End Module
