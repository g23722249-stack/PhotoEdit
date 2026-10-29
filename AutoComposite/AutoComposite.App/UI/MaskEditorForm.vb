Imports AutoComposite

''' <summary>
''' 人物遮罩編輯：左邊原圖、右邊去背結果並排。可用保留／移除筆刷修正遮罩、羽化、還原 AI 遮罩，
''' 以及在人物圖上點選自訂錨點（例如單腳接觸點）。每一筆修改都記錄在主視窗的復原紀錄中。
''' </summary>
Friend NotInheritable Class MaskEditorForm
    Inherits Form

    Private ReadOnly _wf As CompositeWorkflow
    Private ReadOnly _layer As CompositeLayer
    Private ReadOnly _left As New FigureView() With {.Dock = DockStyle.Fill}
    Private ReadOnly _right As New FigureView() With {.Dock = DockStyle.Fill}
    Private ReadOnly _viewMode As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 140}
    Private ReadOnly _tool As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 150}
    Private ReadOnly _size As New NumericUpDown() With {.Minimum = 1, .Maximum = 500, .Value = 20, .Width = 60}
    Private ReadOnly _soft As New NumericUpDown() With {.Minimum = 0, .Maximum = 100, .Value = 50, .Width = 55}
    Private ReadOnly _status As New Label() With {.Dock = DockStyle.Bottom, .Height = 24}

    Public Sub New(wf As CompositeWorkflow, layer As CompositeLayer)
        _wf = wf
        _layer = layer
        Text = $"編輯遮罩 － {layer.Name}"
        Width = 1200 : Height = 800
        StartPosition = FormStartPosition.CenterParent
        Font = New Font("Microsoft JhengHei UI", 9)

        _viewMode.Items.AddRange({"前景保留（棋盤格底）", "背景移除（紅色覆蓋）", "Alpha 遮罩"})
        _viewMode.SelectedIndex = 0
        _tool.Items.AddRange({"保留筆刷（補回前景）", "移除筆刷（去掉背景）", "點選人物錨點"})
        _tool.SelectedIndex = 0

        Dim bar As New FlowLayoutPanel() With {.Dock = DockStyle.Top, .Height = 36, .WrapContents = False}
        Dim lbl = Function(t As String) New Label() With {.Text = t, .AutoSize = True, .Padding = New Padding(0, 7, 0, 0)}
        Dim btn = Function(t As String, a As Action) As Button
                      Dim b As New Button() With {.Text = t, .AutoSize = True, .Height = 28}
                      AddHandler b.Click, Sub() a()
                      Return b
                  End Function
        bar.Controls.AddRange({lbl("右圖顯示"), _viewMode, lbl("工具"), _tool, lbl("筆刷大小（原圖像素）"), _size, lbl("柔邊 %"), _soft,
                               btn("羽化 1px", Sub() Apply(Sub() _wf.FeatherFigureMask(_layer, 1))),
                               btn("還原 AI 遮罩", Sub() Apply(Sub() _wf.RestoreAiMask(_layer))),
                               btn("復原", Sub() Apply(Sub() _wf.History.Undo(_wf.Scene))),
                               btn("關閉", Sub() Close())})
        Dim split As New SplitContainer() With {.Dock = DockStyle.Fill}
        split.Panel1.Controls.Add(_left)
        split.Panel2.Controls.Add(_right)
        Controls.Add(split)
        Controls.Add(bar)
        Controls.Add(_status)
        AddHandler Load, Sub() split.SplitterDistance = split.Width \ 2

        For Each v In {_left, _right}
            AddHandler v.StrokeFinished, AddressOf OnStroke
            AddHandler v.PointClicked, AddressOf OnPoint
        Next
        AddHandler _viewMode.SelectedIndexChanged, Sub() RefreshViews()
        AddHandler _tool.SelectedIndexChanged, Sub() UpdateToolState()
        AddHandler _size.ValueChanged, Sub() UpdateToolState()
        AddHandler _soft.ValueChanged, Sub() UpdateToolState()
        UpdateToolState()
        RefreshViews()
    End Sub

    ''' <summary>目前的圖層物件（復原後圖層會換成快照的複本，所以每次依 Id 重新取得）。</summary>
    Private ReadOnly Property Layer As CompositeLayer
        Get
            Return If(_wf.Scene.Layers.FirstOrDefault(Function(l) l.Id = _layer.Id), _layer)
        End Get
    End Property

    Private Sub UpdateToolState()
        Dim painting = _tool.SelectedIndex < 2
        For Each v In {_left, _right}
            v.Painting = painting
            v.BrushRadius = CDbl(_size.Value)
        Next
        _status.Text = If(painting, "左鍵拖曳繪製；兩邊的圖都可以畫。頭髮、長矛等細節遺漏時用保留筆刷補回。",
                          "在人物圖上點選錨點（例如支撐腳的鞋底），自動定位會用它對齊背景目標。")
    End Sub

    Private Sub Apply(a As Action)
        a()
        RefreshViews()
    End Sub

    Private Sub OnStroke(points As List(Of PointD))
        Dim stroke As New BrushStroke With {.Mode = If(_tool.SelectedIndex = 0, BrushMode.Add, BrushMode.Remove),
            .Radius = CDbl(_size.Value), .Softness = CDbl(_soft.Value) / 100, .Points = points}
        _wf.PaintFigureMask(Layer, stroke)
        RefreshViews()
    End Sub

    Private Sub OnPoint(p As PointD)
        If _tool.SelectedIndex <> 2 Then Return
        Dim l = Layer
        If l.AnchorKind <> AnchorKind.SingleFoot Then l.AnchorKind = AnchorKind.Custom
        l.AnchorX = p.X : l.AnchorY = p.Y
        _wf.CommitEdit("指定人物錨點")
        _status.Text = $"人物錨點 ({p.X:0}, {p.Y:0})。回到主視窗按「自動定位」。"
        RefreshViews()
    End Sub

    Private Sub RefreshViews()
        Dim l = Layer
        _left.SetImage(BitmapConvert.ToBitmap(l.Source), l.Source.Width, l.Source.Height)
        Dim mode = If(_viewMode.SelectedIndex = 2, PreviewMode.AlphaMask, PreviewMode.CutoutFigure)
        Dim img As RgbaImage
        If _viewMode.SelectedIndex = 1 Then
            img = RemovedOverlay(l)
        Else
            img = _wf.Services.Renderer.Render(New CompositeScene With {.Background = RgbaImage.Filled(1, 1, 0, 0, 0), .Layers = New List(Of CompositeLayer) From {l.CloneParameters()}},
                                               New RenderOptions With {.Mode = mode, .LayerId = l.Id}).Image
        End If
        _right.SetImage(BitmapConvert.ToBitmap(img), l.Source.Width, l.Source.Height)
        For Each v In {_left, _right}
            v.MarkerPoint = l.Anchor
        Next
    End Sub

    ' 背景移除檢視：被移除的區域蓋上半透明紅色，方便看出誤刪的頭髮或武器。
    Private Shared Function RemovedOverlay(l As CompositeLayer) As RgbaImage
        Dim img = l.Source.Clone()
        Dim p = img.Pixels
        For i = 0 To l.Alpha.Data.Length - 1
            Dim k = (1 - l.Alpha.Data(i)) * 0.6F
            p(i * 4) = RgbaImage.ClampByte(p(i * 4) * (1 - k) + 255 * k)
            p(i * 4 + 1) = RgbaImage.ClampByte(p(i * 4 + 1) * (1 - k))
            p(i * 4 + 2) = RgbaImage.ClampByte(p(i * 4 + 2) * (1 - k))
            p(i * 4 + 3) = 255
        Next
        Return img
    End Function
End Class

''' <summary>等比例顯示人物圖、把滑鼠座標換算回人物原圖像素的檢視控制項。</summary>
Friend NotInheritable Class FigureView
    Inherits Control

    Private _bmp As Bitmap
    Private _srcW As Integer = 1, _srcH As Integer = 1
    Private _points As List(Of PointD)

    Public Property Painting As Boolean = True
    Public Property BrushRadius As Double = 20
    Public Property MarkerPoint As PointD

    Public Event StrokeFinished(points As List(Of PointD))
    Public Event PointClicked(p As PointD)

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        BackColor = Color.FromArgb(48, 48, 52)
    End Sub

    Public Sub SetImage(bmp As Bitmap, srcW As Integer, srcH As Integer)
        Dim old = _bmp
        _bmp = bmp : _srcW = srcW : _srcH = srcH
        old?.Dispose()
        Invalidate()
    End Sub

    Private ReadOnly Property ViewScale As Double
        Get
            Return Math.Min((Width - 8) / CDbl(_srcW), (Height - 8) / CDbl(_srcH))
        End Get
    End Property

    Private ReadOnly Property Origin As PointF
        Get
            Dim s = ViewScale
            Return New PointF(CSng((Width - _srcW * s) / 2), CSng((Height - _srcH * s) / 2))
        End Get
    End Property

    Private Function ToSource(p As Point) As PointD
        Dim o = Origin, s = ViewScale
        Return New PointD((p.X - o.X) / s, (p.Y - o.Y) / s)
    End Function

    Private Function ToScreen(p As PointD) As PointF
        Dim o = Origin, s = ViewScale
        Return New PointF(CSng(o.X + p.X * s), CSng(o.Y + p.Y * s))
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.Clear(BackColor)
        If _bmp Is Nothing Then Return
        Dim o = Origin, s = ViewScale
        e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear
        e.Graphics.DrawImage(_bmp, New RectangleF(o.X, o.Y, CSng(_srcW * s), CSng(_srcH * s)))
        Dim a = ToScreen(MarkerPoint)
        Using pen As New Pen(Color.Magenta, 2)
            e.Graphics.DrawEllipse(pen, a.X - 6, a.Y - 6, 12, 12)
            e.Graphics.DrawLine(pen, a.X - 10, a.Y, a.X + 10, a.Y)
        End Using
        If _points IsNot Nothing AndAlso _points.Count > 1 Then
            Using pen As New Pen(Color.FromArgb(140, 255, 255, 0), CSng(BrushRadius * 2 * s)) With {.StartCap = Drawing2D.LineCap.Round, .EndCap = Drawing2D.LineCap.Round, .LineJoin = Drawing2D.LineJoin.Round}
                e.Graphics.DrawLines(pen, _points.Select(Function(p) ToScreen(p)).ToArray())
            End Using
        End If
        If Painting AndAlso ClientRectangle.Contains(PointToClient(MousePosition)) Then
            Dim c = PointToClient(MousePosition)
            Dim r = CSng(BrushRadius * s)
            e.Graphics.DrawEllipse(Pens.White, c.X - r, c.Y - r, 2 * r, 2 * r)
        End If
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button <> MouseButtons.Left OrElse _bmp Is Nothing Then Return
        If Painting Then
            _points = New List(Of PointD) From {ToSource(e.Location)}
        Else
            Dim p = ToSource(e.Location)
            If p.X >= 0 AndAlso p.Y >= 0 AndAlso p.X <= _srcW AndAlso p.Y <= _srcH Then RaiseEvent PointClicked(p)
        End If
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _points IsNot Nothing Then _points.Add(ToSource(e.Location))
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _points Is Nothing Then Return
        Dim pts = _points
        _points = Nothing
        RaiseEvent StrokeFinished(pts)
        Invalidate()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _bmp?.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
