Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 繪圖工具的圖示：以 24 × 24 的格子設計、用 GDI+ 向量畫出，任何大小與 DPI 都清楚。
''' 方框類形狀直接用 DrawGeometry 算外形，圖示和實際畫出來的形狀一致。
''' tool = -1 為「選取」，其餘為 DrawShape。
''' </summary>
Friend Module DrawIcons

    Public Const SelectTool As Integer = -1
    ''' <summary>節點工具（改鋼筆路徑的節點與把手）。</summary>
    Public Const NodeTool As Integer = -2
    ' 共用
    Public Const PanTool As Integer = -3
    Public Const ZoomTool As Integer = -4
    Public Const EyedropperTool As Integer = -5
    ' 點陣（都是「直接繪製」加上不同的筆或橡皮擦）
    Public Const EraserTool As Integer = -6
    Public Const CloneTool As Integer = -7
    Public Const HealTool As Integer = -8
    Public Const SmudgeTool As Integer = -9
    Public Const MixerTool As Integer = -10
    Public Const BlurTool As Integer = -11
    Public Const SharpenTool As Integer = -12
    Public Const DodgeTool As Integer = -13
    Public Const BurnTool As Integer = -14
    Public Const SpongeMinusTool As Integer = -15
    Public Const SpongePlusTool As Integer = -16
    ' 向量
    Public Const ShapeBuilderTool As Integer = -17
    Public Const KnifeTool As Integer = -18
    Public Const SmartFillTool As Integer = -19
    Public Const ShadowTool As Integer = -20
    Public Const TransparencyTool As Integer = -21
    Public Const EnvelopeTool As Integer = -22
    ''' <summary>屬性吸管（吸管工具的第二個）：吸取一個圖形的外觀，套到其他圖形。</summary>
    Public Const AttrDropperTool As Integer = -23
    ' 向量修飾筆刷：塗抹、轉動、吸引、排斥、粗糙、平滑（模式在上方選）
    Public Const TouchUpTool As Integer = -24
    ' 可變寬度：在線上拖曳讓那裡變粗變細（同 Illustrator 的寬度工具）
    Public Const WidthTool As Integer = -25

    ''' <summary>點陣工具的變化（實際上是「直接繪製」）：橡皮擦、仿製、修復、塗抹、混色、模糊、銳利化、加亮、加深、海綿。</summary>
    Public Function IsRasterVariant(tool As Integer) As Boolean
        Return tool <= EraserTool AndAlso tool >= SpongePlusTool
    End Function

    ''' <summary>工具實際用的引擎工具：點陣的變化都是直接繪製。</summary>
    Public Function EngineTool(tool As Integer) As Integer
        Return If(IsRasterVariant(tool), CInt(DrawShape.Raster), tool)
    End Function

    ''' <summary>點陣工具對應的筆（橡皮擦、一般筆刷為 Nothing）。</summary>
    Public Function BrushOf(tool As Integer) As BrushKind?
        Select Case tool
            Case CloneTool : Return BrushKind.Clone
            Case HealTool : Return BrushKind.Heal
            Case SmudgeTool : Return BrushKind.Smudge
            Case MixerTool : Return BrushKind.Mixer
            Case BlurTool : Return BrushKind.Blur
            Case SharpenTool : Return BrushKind.Sharpen
            Case DodgeTool : Return BrushKind.Dodge
            Case BurnTool : Return BrushKind.Burn
            Case SpongeMinusTool : Return BrushKind.SpongeDesaturate
            Case SpongePlusTool : Return BrushKind.SpongeSaturate
        End Select
        Return Nothing
    End Function

    ''' <summary>筆對應的點陣工具（一般筆刷回傳直接繪製）。</summary>
    Public Function ToolOfBrush(b As BrushKind) As Integer
        For t = CloneTool To SpongePlusTool Step -1
            Dim k = BrushOf(t)
            If k.HasValue AndAlso k.Value = b Then Return t
        Next
        Return CInt(DrawShape.Raster)
    End Function

    Public Function ToolName(tool As Integer) As String
        Select Case tool
            Case SelectTool : Return Lang.T("選取")
            Case NodeTool : Return Lang.T("節點")
            Case PanTool : Return Lang.T("平移")
            Case ZoomTool : Return Lang.T("縮放")
            Case EyedropperTool : Return Lang.T("吸管")
            Case EraserTool : Return Lang.T("橡皮擦")
            Case ShapeBuilderTool : Return Lang.T("形狀建立")
            Case KnifeTool : Return Lang.T("刀具")
            Case SmartFillTool : Return Lang.T("智慧填色")
            Case ShadowTool : Return Lang.T("陰影")
            Case TransparencyTool : Return Lang.T("透明度")
            Case EnvelopeTool : Return Lang.T("封套")
            Case AttrDropperTool : Return Lang.T("屬性吸管")
            Case TouchUpTool : Return Lang.T("修飾筆刷")
            Case WidthTool : Return Lang.T("寬度工具")
            Case CInt(DrawShape.Raster) : Return Lang.T("筆刷")
        End Select
        Dim b = BrushOf(tool)
        If b.HasValue Then Return DrawGeometry.BrushNames(CInt(b.Value))
        If tool >= 0 AndAlso tool < DrawGeometry.ShapeNames.Length Then Return DrawGeometry.ShapeNames(tool)
        Return ""
    End Function

    ''' <summary>說明文字的鍵。</summary>
    Public Function HelpKey(tool As Integer) As String
        Select Case tool
            Case SelectTool : Return "draw.tool.select"
            Case NodeTool : Return "draw.tool.node"
            Case PanTool : Return "draw.tool.pan"
            Case ZoomTool : Return "draw.tool.zoom"
            Case EyedropperTool : Return "draw.tool.eyedropper"
            Case EraserTool : Return "draw.tool.eraser"
            Case CloneTool : Return "draw.tool.clone"
            Case HealTool : Return "draw.tool.heal"
            Case SmudgeTool : Return "draw.tool.smudge"
            Case MixerTool : Return "draw.tool.mixer"
            Case BlurTool : Return "draw.tool.blur"
            Case SharpenTool : Return "draw.tool.sharpen"
            Case DodgeTool : Return "draw.tool.dodge"
            Case BurnTool : Return "draw.tool.burn"
            Case SpongeMinusTool, SpongePlusTool : Return "draw.tool.sponge"
            Case ShapeBuilderTool : Return "draw.builder"
            Case KnifeTool : Return "draw.tool.knife"
            Case SmartFillTool : Return "draw.tool.smartfill"
            Case ShadowTool : Return "draw.tool.shadow"
            Case TransparencyTool : Return "draw.tool.fade"
            Case EnvelopeTool : Return "draw.tool.envelope"
            Case AttrDropperTool : Return "draw.tool.attrdropper"
            Case TouchUpTool : Return "draw.tool.touchup"
            Case WidthTool : Return "draw.tool.width"
        End Select
        Return "draw.tool." & tool
    End Function

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
                If tool <= PanTool OrElse tool = CInt(DrawShape.Text) Then DrawExtra(g, tool, pen, color)
                Select Case tool
                    Case NodeTool
                        ' 白色箭頭＋右下一個節點方塊
                        Using bg As New SolidBrush(Color.White)
                            g.FillPolygon(bg, {New PointF(5, 3), New PointF(15, 10), New PointF(10.5F, 11), New PointF(8.5F, 16)})
                        End Using
                        g.DrawPolygon(pen, {New PointF(5, 3), New PointF(15, 10), New PointF(10.5F, 11), New PointF(8.5F, 16)})
                        Using br As New SolidBrush(color)
                            g.FillRectangle(br, 15.5F, 15.5F, 5, 5)
                        End Using
                    Case DrawShape.Path
                        ' 筆尖中間的小圓孔
                        Using bg As New SolidBrush(Color.White)
                            g.FillEllipse(bg, 10.6F, 9.6F, 2.8F, 2.8F)
                        End Using
                        g.DrawEllipse(pen, 10.6F, 9.6F, 2.8F, 2.8F)
                    Case DrawShape.Line
                        Dot(g, pen, 4, 19, 1.6F) : Dot(g, pen, 20, 5, 1.6F)
                    Case DrawShape.Dimension
                        Using br As New SolidBrush(color)
                            g.FillPolygon(br, {New PointF(4.5F, 15), New PointF(8, 13), New PointF(8, 17)})
                            g.FillPolygon(br, {New PointF(19.5F, 15), New PointF(16, 13), New PointF(16, 17)})
                            g.FillRectangle(br, 9, 9.5F, 6, 2)
                        End Using
                    Case DrawShape.Connector
                        Using br As New SolidBrush(System.Drawing.Color.FromArgb(110, color))
                            g.FillRectangle(br, 2.5F, 3, 7, 6) : g.FillRectangle(br, 14.5F, 15, 7, 6)
                        End Using
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
                    Case DrawShape.FillLayer
                        Using br As New SolidBrush(Color.FromArgb(170, color))
                            g.FillRectangle(br, 3, 7.5F, 15, 13)
                        End Using
                    Case DrawShape.Gradient
                        ' 五條由淡到濃的色條
                        For i = 0 To 4
                            Using br As New SolidBrush(Color.FromArgb(25 + i * 55, color))
                                g.FillRectangle(br, 3.8F + i * 3.3F, 6.3F, 3.4F, 11.4F)
                            End Using
                        Next
                    Case DrawShape.Bucket
                        ' 從桶口流下的顏料與一滴顏料（熱點在顏料滴的下緣）
                        Using br As New SolidBrush(color)
                            Using drip As New GraphicsPath()
                                drip.AddBezier(8.5F, 9.5F, 6.5F, 11.5F, 5, 13.5F, 4.6F, 16.5F)
                                drip.AddLine(4.6F, 16.5F, 6.6F, 16.5F)
                                drip.AddBezier(6.6F, 16.5F, 7.2F, 14, 9, 12.5F, 11.2F, 12)
                                drip.CloseFigure()
                                g.FillPath(br, drip)
                            End Using
                            g.FillEllipse(br, 3.2F, 17.4F, 4.4F, 4.4F)
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

    ''' <summary>共用、點陣修飾、形狀建立、刀具、文字的圖示。</summary>
    Private Sub DrawExtra(g As Graphics, tool As Integer, pen As Pen, color As Color)
        Using br As New SolidBrush(color), soft As New SolidBrush(Color.FromArgb(110, color)), bg As New SolidBrush(Color.White),
              thick As New Pen(color, 2.6F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
            Select Case tool
                Case PanTool
                    ' 手掌與四根手指、拇指
                    For Each f In {(8.0F, 4.5F), (11.0F, 3.0F), (14.0F, 3.5F), (17.0F, 6.0F)}
                        g.DrawLine(pen, f.Item1, 12.0F, f.Item1, f.Item2)
                    Next
                    Using palm As New GraphicsPath()
                        palm.AddLine(8, 12, 8, 15)
                        palm.AddLine(8, 15, 5, 12.5F)
                        palm.AddBezier(5, 12.5F, 3.5F, 13.5F, 6, 17, 8.5F, 20)
                        palm.AddLine(8.5F, 20, 15.5F, 20)
                        palm.AddBezier(15.5F, 20, 17, 18, 17, 15, 17, 12)
                        g.DrawPath(pen, palm)
                    End Using
                Case ZoomTool
                    g.DrawEllipse(pen, 3.5F, 3.5F, 12, 12)
                    g.DrawLine(thick, 14.5F, 14.5F, 20.5F, 20.5F)
                    g.DrawLine(pen, 6.5F, 9.5F, 12.5F, 9.5F) : g.DrawLine(pen, 9.5F, 6.5F, 9.5F, 12.5F)
                Case WidthTool
                    ' 中間粗、兩端尖的線，粗的地方一個控制點與往外的箭頭
                    Using lens As New GraphicsPath()
                        lens.AddBezier(2.5F, 15, 8, 9.5F, 16, 9.5F, 21.5F, 15)
                        lens.AddBezier(21.5F, 15, 16, 18.5F, 8, 18.5F, 2.5F, 15)
                        g.FillPath(soft, lens) : g.DrawPath(pen, lens)
                    End Using
                    g.DrawLine(pen, 12, 10.5F, 12, 3)
                    g.DrawLine(pen, 12, 3, 9.8F, 5.4F) : g.DrawLine(pen, 12, 3, 14.2F, 5.4F)
                    g.FillPolygon(br, {New PointF(12, 8.4F), New PointF(14, 10.6F), New PointF(12, 12.8F), New PointF(10, 10.6F)})
                Case TouchUpTool
                    ' 一條被推彎的線，旁邊一個筆刷圓
                    Using wave As New GraphicsPath()
                        wave.AddBezier(2.5F, 19, 7, 19, 8, 12.5F, 12, 12.5F)
                        wave.AddBezier(12, 12.5F, 16, 12.5F, 15, 19, 21.5F, 19)
                        g.DrawPath(pen, wave)
                    End Using
                    g.FillEllipse(soft, 8, 3, 9, 9)
                    g.DrawEllipse(pen, 8, 3, 9, 9)
                    g.DrawLine(pen, 12.5F, 9.5F, 12.5F, 11.5F)
                Case AttrDropperTool
                    ' 吸管加上右下角的小色塊（線條＋填色），表示吸的是外觀
                    g.DrawLine(pen, 3, 17, 9, 11)
                    Using body As New Pen(color, 3.6F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                        g.DrawLine(body, 8.5F, 11.5F, 12.5F, 7.5F)
                    End Using
                    g.FillEllipse(br, 12, 2.5F, 5, 5)
                    g.FillRectangle(soft, 13.5F, 13.5F, 8, 8)
                    g.DrawRectangle(pen, 13.5F, 13.5F, 8, 8)
                Case EyedropperTool
                    g.DrawLine(pen, 4, 20, 12, 12)
                    Using body As New Pen(color, 4.2F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                        g.DrawLine(body, 11.5F, 12.5F, 16, 8)
                    End Using
                    g.FillEllipse(br, 15.5F, 3, 5.5F, 5.5F)
                    g.DrawLine(pen, 10, 10, 14, 14)
                Case EraserTool
                    Dim body = {New PointF(3, 15), New PointF(10.5F, 7.5F), New PointF(18, 15), New PointF(10.5F, 22.5F)}
                    g.DrawPolygon(pen, body)
                    g.FillPolygon(br, {New PointF(3, 15), New PointF(6.75F, 11.25F), New PointF(14.25F, 18.75F), New PointF(10.5F, 22.5F)})
                    g.DrawLine(pen, 12, 22, 21, 22)
                Case CloneTool
                    g.DrawEllipse(pen, 9.5F, 2.5F, 5, 5)
                    g.DrawRectangle(pen, 11, 7.5F, 2, 5)
                    g.FillRectangle(br, 5, 12.5F, 14, 4.5F)
                    g.DrawLine(pen, 6, 20, 18, 20)
                Case HealTool
                    g.DrawPolygon(pen, {New PointF(4, 15), New PointF(15, 4), New PointF(20, 9), New PointF(9, 20)})
                    g.FillPolygon(soft, {New PointF(9.5F, 9.5F), New PointF(12, 7), New PointF(17, 12), New PointF(14.5F, 14.5F)})
                    For Each d In {(10.5F, 12.5F), (12.5F, 10.5F), (13.5F, 13.5F)}
                        g.FillEllipse(br, d.Item1 - 0.9F, d.Item2 - 0.9F, 1.8F, 1.8F)
                    Next
                Case SmudgeTool
                    Using finger As New Pen(color, 4.6F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                        g.DrawLine(finger, 15.5F, 4, 11, 13.5F)
                    End Using
                    g.FillEllipse(bg, 12.2F, 4.6F, 2.4F, 2.4F)
                    Using smear As New Pen(Color.FromArgb(150, color), 2.4F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                        g.DrawBezier(smear, 3, 21, 6, 18, 9, 20, 12, 17)
                    End Using
                Case MixerTool
                    g.FillEllipse(soft, 3, 7, 11, 11)
                    g.DrawEllipse(pen, 3, 7, 11, 11)
                    g.FillEllipse(br, 10, 7, 11, 11)
                Case BlurTool
                    Using drop As New GraphicsPath()
                        drop.AddBezier(12, 3, 9, 8, 5, 11.5F, 5, 15)
                        drop.AddBezier(5, 15, 5, 19, 8.5F, 21, 12, 21)
                        drop.AddBezier(12, 21, 15.5F, 21, 19, 19, 19, 15)
                        drop.AddBezier(19, 15, 19, 11.5F, 15, 8, 12, 3)
                        g.FillPath(soft, drop)
                        g.DrawPath(pen, drop)
                    End Using
                Case SharpenTool
                    g.DrawPolygon(pen, {New PointF(12, 3), New PointF(20, 20), New PointF(4, 20)})
                    g.FillPolygon(soft, {New PointF(12, 3), New PointF(16, 11.5F), New PointF(8, 11.5F)})
                Case DodgeTool, BurnTool
                    g.DrawLine(thick, 11, 12, 5, 21)
                    If tool = BurnTool Then g.FillEllipse(br, 9, 3, 10, 10) Else g.FillEllipse(bg, 9, 3, 10, 10)
                    g.DrawEllipse(pen, 9, 3, 10, 10)
                Case SpongeMinusTool, SpongePlusTool
                    g.DrawRectangle(pen, 3, 7, 15, 12)
                    For Each d In {(6.5F, 10.5F), (11.0F, 13.0F), (7.5F, 15.5F), (14.0F, 10.0F), (14.5F, 16.0F)}
                        g.FillEllipse(soft, d.Item1 - 1.3F, d.Item2 - 1.3F, 2.6F, 2.6F)
                    Next
                    g.DrawLine(pen, 17, 3.5F, 22, 3.5F)
                    If tool = SpongePlusTool Then g.DrawLine(pen, 19.5F, 1, 19.5F, 6)
                Case ShapeBuilderTool
                    Using a As New GraphicsPath(), b As New GraphicsPath()
                        a.AddEllipse(2, 8, 12, 12) : b.AddEllipse(9, 8, 12, 12)
                        Using r As New Region(a)
                            r.Intersect(b)
                            g.FillRegion(br, r)
                        End Using
                        g.DrawPath(pen, a) : g.DrawPath(pen, b)
                    End Using
                    g.DrawLine(pen, 17, 1.5F, 17, 6.5F) : g.DrawLine(pen, 14.5F, 4, 19.5F, 4)
                Case ShadowTool
                    ' 方塊與右下的陰影
                    g.FillRectangle(soft, 9, 9, 12, 12)
                    g.FillRectangle(bg, 3, 3, 12, 12)
                    g.DrawRectangle(pen, 3, 3, 12, 12)
                Case TransparencyTool
                    ' 由實到透明的方塊（棋盤格露出來）
                    For i = 0 To 3
                        For j = 0 To 3
                            If (i + j) Mod 2 = 0 Then g.FillRectangle(soft, 4 + i * 4, 4 + j * 4, 4, 4)
                        Next
                    Next
                    For k = 0 To 3
                        Using fb As New SolidBrush(Color.FromArgb(230 - k * 70, color))
                            g.FillRectangle(fb, 4 + k * 4, 4, 4, 16)
                        End Using
                    Next
                    g.DrawRectangle(pen, 4, 4, 16, 16)
                Case EnvelopeTool
                    ' 被拉彎的方框與控制點
                    Using p As New GraphicsPath()
                        p.AddBezier(4, 6, 9, 2, 15, 2, 20, 6)
                        p.AddLine(20, 6, 20, 18)
                        p.AddBezier(20, 18, 15, 21, 9, 21, 4, 18)
                        p.CloseFigure()
                        g.DrawPath(pen, p)
                    End Using
                    For Each q In {(4.0F, 6.0F), (12.0F, 3.0F), (20.0F, 6.0F), (20.0F, 18.0F), (4.0F, 18.0F)}
                        g.FillRectangle(br, q.Item1 - 1.6F, q.Item2 - 1.6F, 3.2F, 3.2F)
                    Next
                Case SmartFillTool
                    ' 兩條交叉的線圍出的一塊被填滿，加一滴顏料
                    g.FillPolygon(soft, {New PointF(6, 8), New PointF(17, 8), New PointF(17, 19), New PointF(6, 19)})
                    g.DrawLine(pen, 2, 8, 21, 8) : g.DrawLine(pen, 6, 3, 6, 22) : g.DrawLine(pen, 2, 19, 21, 19) : g.DrawLine(pen, 17, 3, 17, 22)
                    g.FillEllipse(br, 10, 11, 4, 4)
                Case KnifeTool
                    g.FillPolygon(soft, {New PointF(3, 21), New PointF(15, 9), New PointF(18, 12), New PointF(7, 21)})
                    g.DrawPolygon(pen, {New PointF(3, 21), New PointF(15, 9), New PointF(18, 12), New PointF(7, 21)})
                    g.DrawLine(thick, 17, 10, 21.5F, 5.5F)
                Case CInt(DrawShape.Text)
                    Using t As New Pen(color, 2.2F)
                        g.DrawLine(t, 5, 5, 19, 5)
                        g.DrawLine(t, 12, 5, 12, 20)
                    End Using
                    g.DrawLine(pen, 9, 20, 15, 20)
                    g.DrawLine(pen, 5, 5, 5, 7.5F) : g.DrawLine(pen, 19, 5, 19, 7.5F)
            End Select
        End Using
    End Sub

    Private Sub Dot(g As Graphics, pen As Pen, x As Single, y As Single, r As Single)
        g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2)
    End Sub

    Private Function IconPath(tool As Integer) As GraphicsPath
        Dim p As New GraphicsPath()
        If tool <= PanTool OrElse tool = CInt(DrawShape.Text) Then Return p ' 在 DrawExtra 畫
        Select Case tool
            Case SelectTool
                p.AddPolygon({New PointF(6, 3), New PointF(18, 11), New PointF(12.5F, 12.2F), New PointF(10, 18)})
            Case NodeTool
                ' 箭頭在 DrawTool 畫（白底），這裡只畫通往節點的一條曲線
                p.AddBezier(3, 21, 7, 17, 12, 21, 16, 18)
            Case DrawShape.Path
                ' 鋼筆筆尖：上寬下尖，中間一道縫
                p.AddPolygon({New PointF(12, 21), New PointF(6.5F, 12), New PointF(9, 4), New PointF(15, 4), New PointF(17.5F, 12)})
                p.StartFigure()
                p.AddLine(12, 21, 12, 12.4F)
                p.StartFigure()
                p.AddLine(8.5F, 4, 15.5F, 4)
            Case DrawShape.Freehand
                p.AddBezier(3, 18, 5, 11.5F, 8, 11, 12, 12)
                p.AddBezier(12, 12, 16, 13, 18, 8, 21, 6)
            Case DrawShape.Raster
                ' 筆桿（斜放）
                p.AddPolygon({New PointF(19.2F, 2.6F), New PointF(21.4F, 4.8F), New PointF(13.6F, 12.8F), New PointF(11.2F, 10.4F)})
            Case DrawShape.FillLayer
                ' 填滿圖層：疊在一起的兩張（後面一張只露出邊）
                p.AddRectangle(New RectangleF(6, 3.5F, 15, 13))
                p.AddRectangle(New RectangleF(3, 7.5F, 15, 13))
            Case DrawShape.Gradient
                ' 漸層：外框（裡面由淡到濃的色條在 DrawTool 畫）
                p.AddRectangle(New RectangleF(3, 5.5F, 18, 13))
            Case DrawShape.Bucket
                ' 油漆桶：斜放的桶身（桶口朝左下）與提把
                p.AddPolygon({New PointF(8.5F, 9.5F), New PointF(15.5F, 3.5F), New PointF(22, 11), New PointF(15, 17)})
                p.StartFigure()
                p.AddBezier(12, 6.5F, 13.5F, 1.5F, 21, 2.5F, 19, 7.5F)
            Case DrawShape.Line
                p.AddLine(4, 19, 20, 5)
            Case DrawShape.Bezier
                p.AddBezier(4, 18, 7, 4, 17, 20, 20, 6)
            Case DrawShape.Polygon
                p.AddPolygon({New PointF(12, 3), New PointF(20.5F, 9.2F), New PointF(17.2F, 19), New PointF(6.8F, 19), New PointF(3.5F, 9.2F)})
            Case DrawShape.Dimension
                ' 兩條延伸線＋中間的尺寸線（箭頭在 DrawTool 畫）
                p.AddLine(3.5F, 7, 3.5F, 19) : p.StartFigure()
                p.AddLine(20.5F, 7, 20.5F, 19) : p.StartFigure()
                p.AddLine(5, 15, 19, 15)
            Case DrawShape.Connector
                ' 兩個方塊之間一條直角線
                p.AddRectangle(New RectangleF(2.5F, 3, 7, 6)) : p.StartFigure()
                p.AddRectangle(New RectangleF(14.5F, 15, 7, 6)) : p.StartFigure()
                p.AddLine(9.5F, 6, 12, 6) : p.AddLine(12, 6, 12, 18) : p.AddLine(12, 18, 14.5F, 18)
            Case Else
                Dim layer As New DrawLayer With {.Shape = CType(tool, DrawShape), .X = 12, .Y = 12, .W = 18, .H = 13, .Seed = 3}
                Select Case layer.Shape
                    Case DrawShape.Rectangle, DrawShape.RoundRect : layer.W = 16 : layer.H = 12
                    Case DrawShape.Star4, DrawShape.Star5, DrawShape.Star6, DrawShape.Heart : layer.W = 19 : layer.H = 19
                    Case DrawShape.Lightning : layer.W = 13 : layer.H = 20
                    Case DrawShape.RegularPolygon, DrawShape.Star, DrawShape.Arc : layer.W = 19 : layer.H = 19
                    Case DrawShape.Spiral : layer.W = 19 : layer.H = 19
                    Case DrawShape.Grid : layer.W = 18 : layer.H = 16
                    Case DrawShape.Arrow : layer.H = 14
                End Select
                DrawGeometry.ApplyDefaults(layer)
                If layer.Shape = DrawShape.RoundRect Then layer.Param1 = 0.33
                If layer.Shape = DrawShape.Spiral Then layer.Param1 = 2.5
                If layer.Shape = DrawShape.Grid Then layer.Sides = 3 : layer.Rows = 3
                If layer.Shape = DrawShape.Star Then layer.Sides = 7 : layer.Param1 = 0.5
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
