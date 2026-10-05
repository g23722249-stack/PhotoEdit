Imports System.Drawing.Drawing2D
Imports PhotoEdit

''' <summary>
''' 裁切框的進階操作：8 個控制點拖曳縮放（固定比例時等比例、不超出影像）、輔助線（三分線、黃金比例、格線、對角線）、
''' 裁切形狀預覽（形狀外變暗），以及框內顯示輸出的像素尺寸。
''' </summary>
Partial Friend Class PreviewCanvas
    Public Enum CropGuide
        Thirds
        Golden
        Grid
        Diagonal
        None
    End Enum

    Private _cropGuide As CropGuide = CropGuide.Thirds
    Private _cropShapeKind As CropShape = CropShape.Rectangle
    Private _cropPixelSize As Size
    Private _cropHandle As Integer = -1

    Public Property CropGuideKind As CropGuide
        Get
            Return _cropGuide
        End Get
        Set(value As CropGuide)
            _cropGuide = value
            Invalidate()
        End Set
    End Property

    ''' <summary>裁切形狀預覽（形狀外以暗色表示會變透明）。</summary>
    Public Property CropShapeKind As CropShape
        Get
            Return _cropShapeKind
        End Get
        Set(value As CropShape)
            _cropShapeKind = value
            Invalidate()
        End Set
    End Property

    ''' <summary>未裁切影像的原圖像素尺寸，用來在框內顯示輸出尺寸；Empty 不顯示。</summary>
    Public Property CropPixelSize As Size
        Get
            Return _cropPixelSize
        End Get
        Set(value As Size)
            _cropPixelSize = value
            Invalidate()
        End Set
    End Property

    ''' <summary>控制點位置：0 左上、1 上、2 右上、3 右、4 右下、5 下、6 左下、7 左。</summary>
    Private Shared Function CropHandlePoints(r As RectangleF) As PointF()
        Dim cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2
        Return {New PointF(r.Left, r.Top), New PointF(cx, r.Top), New PointF(r.Right, r.Top), New PointF(r.Right, cy),
                New PointF(r.Right, r.Bottom), New PointF(cx, r.Bottom), New PointF(r.Left, r.Bottom), New PointF(r.Left, cy)}
    End Function

    ''' <summary>滑鼠下的控制點：角落優先，其次邊線（整條邊都可以拖）；沒有時 -1。</summary>
    Private Function CropHandleAt(p As Point) As Integer
        If _image Is Nothing Then Return -1
        Dim r = CropToScreen(ImageBounds())
        Dim pts = CropHandlePoints(r)
        For Each i In {0, 2, 4, 6, 1, 3, 5, 7}
            If Math.Abs(p.X - pts(i).X) <= 10 AndAlso Math.Abs(p.Y - pts(i).Y) <= 10 Then Return i
        Next
        Const Edge = 5
        Dim inX = p.X > r.Left AndAlso p.X < r.Right, inY = p.Y > r.Top AndAlso p.Y < r.Bottom
        If inX AndAlso Math.Abs(p.Y - r.Top) <= Edge Then Return 1
        If inX AndAlso Math.Abs(p.Y - r.Bottom) <= Edge Then Return 5
        If inY AndAlso Math.Abs(p.X - r.Left) <= Edge Then Return 7
        If inY AndAlso Math.Abs(p.X - r.Right) <= Edge Then Return 3
        Return -1
    End Function

    Private Shared Function CropHandleCursor(i As Integer) As Cursor
        Select Case i
            Case 0, 4 : Return Cursors.SizeNWSE
            Case 2, 6 : Return Cursors.SizeNESW
            Case 1, 5 : Return Cursors.SizeNS
            Case Else : Return Cursors.SizeWE
        End Select
    End Function

    ''' <summary>拖曳控制點後的裁切框（p 為影像像素座標）：對邊固定；有比例時角落等比例、邊線則另一邊以中心伸縮，且不超出影像。</summary>
    Private Function ResizedCrop(p As PointF, iw As Double, ih As Double) As CropRect
        Dim s = _dragStartCrop
        Dim l = s.X * iw, t = s.Y * ih, r = (s.X + s.Width) * iw, b = (s.Y + s.Height) * ih
        Const MinPx = 8.0
        Dim i = _cropHandle
        Dim moveL = i = 0 OrElse i = 6 OrElse i = 7
        Dim moveR = i = 2 OrElse i = 3 OrElse i = 4
        Dim moveT = i = 0 OrElse i = 1 OrElse i = 2
        Dim moveB = i = 4 OrElse i = 5 OrElse i = 6
        If moveL Then l = Math.Min(p.X, r - MinPx)
        If moveR Then r = Math.Max(p.X, l + MinPx)
        If moveT Then t = Math.Min(p.Y, b - MinPx)
        If moveB Then b = Math.Max(p.Y, t + MinPx)
        Dim a = _aspectRatio
        If a > 0 Then
            If (moveL OrElse moveR) AndAlso (moveT OrElse moveB) Then
                Dim w = r - l, h = b - t
                If w / h > a Then h = w / a Else w = h * a
                Dim maxW = If(moveL, r, iw - l), maxH = If(moveT, b, ih - t)
                If w > maxW Then w = maxW : h = w / a
                If h > maxH Then h = maxH : w = h * a
                If moveL Then l = r - w Else r = l + w
                If moveT Then t = b - h Else b = t + h
            ElseIf moveL OrElse moveR Then
                Dim w = r - l, h = w / a, cy = (t + b) / 2
                Dim maxH = 2 * Math.Min(cy, ih - cy)
                If h > maxH Then h = maxH : w = h * a
                t = cy - h / 2 : b = cy + h / 2
                If moveL Then l = r - w Else r = l + w
            ElseIf moveT OrElse moveB Then
                Dim h = b - t, w = h * a, cx = (l + r) / 2
                Dim maxW = 2 * Math.Min(cx, iw - cx)
                If w > maxW Then w = maxW : h = w / a
                l = cx - w / 2 : r = cx + w / 2
                If moveT Then t = b - h Else b = t + h
            End If
        End If
        Return New CropRect(l / iw, t / ih, (r - l) / iw, (b - t) / ih).Normalized()
    End Function

    Private Sub DrawCropShape(g As Graphics, r As RectangleF)
        If _cropShapeKind = CropShape.Rectangle OrElse r.Width < 4 OrElse r.Height < 4 Then Return
        Using path = ImagePipeline.CropShapePath(_cropShapeKind, r), region As New Region(r),
              shade As New SolidBrush(Color.FromArgb(130, 0, 0, 0)),
              pen As New Pen(Color.FromArgb(230, 255, 255, 255), 1.5F) With {.DashStyle = DashStyle.Dash}
            region.Exclude(path)
            g.FillRegion(shade, region)
            Dim old = g.SmoothingMode
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.DrawPath(pen, path)
            g.SmoothingMode = old
        End Using
    End Sub

    Private Sub DrawCropGuides(g As Graphics, r As RectangleF)
        Using pen As New Pen(Color.FromArgb(120, 255, 255, 255))
            Select Case _cropGuide
                Case CropGuide.Thirds, CropGuide.Golden
                    Dim stops = If(_cropGuide = CropGuide.Thirds, {1 / 3.0F, 2 / 3.0F}, {0.382F, 0.618F})
                    For Each f In stops
                        g.DrawLine(pen, r.Left + r.Width * f, r.Top, r.Left + r.Width * f, r.Bottom)
                        g.DrawLine(pen, r.Left, r.Top + r.Height * f, r.Right, r.Top + r.Height * f)
                    Next
                Case CropGuide.Grid
                    For k = 1 To 5
                        g.DrawLine(pen, r.Left + r.Width * k / 6, r.Top, r.Left + r.Width * k / 6, r.Bottom)
                        g.DrawLine(pen, r.Left, r.Top + r.Height * k / 6, r.Right, r.Top + r.Height * k / 6)
                    Next
                Case CropGuide.Diagonal
                    Dim old = g.SmoothingMode
                    g.SmoothingMode = SmoothingMode.AntiAlias
                    g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom)
                    g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom)
                    ' 從角落畫 45° 線（對角線構圖法）。
                    Dim s = Math.Min(r.Width, r.Height)
                    g.DrawLine(pen, r.Left, r.Top, r.Left + s, r.Top + s)
                    g.DrawLine(pen, r.Right, r.Bottom, r.Right - s, r.Bottom - s)
                    g.SmoothingMode = old
            End Select
        End Using
    End Sub

    ''' <summary>角落畫 L 形、邊線中央畫短橫條。</summary>
    Private Shared Sub DrawCropHandles(g As Graphics, r As RectangleF)
        Dim len = Math.Min(18.0F, Math.Min(r.Width, r.Height) / 3)
        Dim cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, half = len / 2
        Dim lines = {
            {New PointF(r.Left, r.Top + len), New PointF(r.Left, r.Top), New PointF(r.Left + len, r.Top)},
            {New PointF(r.Right - len, r.Top), New PointF(r.Right, r.Top), New PointF(r.Right, r.Top + len)},
            {New PointF(r.Right, r.Bottom - len), New PointF(r.Right, r.Bottom), New PointF(r.Right - len, r.Bottom)},
            {New PointF(r.Left + len, r.Bottom), New PointF(r.Left, r.Bottom), New PointF(r.Left, r.Bottom - len)}}
        Dim bars = {
            {New PointF(cx - half, r.Top), New PointF(cx + half, r.Top)}, {New PointF(cx - half, r.Bottom), New PointF(cx + half, r.Bottom)},
            {New PointF(r.Left, cy - half), New PointF(r.Left, cy + half)}, {New PointF(r.Right, cy - half), New PointF(r.Right, cy + half)}}
        Using shadow As New Pen(Color.FromArgb(120, 0, 0, 0), 6), pen As New Pen(Color.White, 4)
            For k = 0 To 3
                Dim pts = {lines(k, 0), lines(k, 1), lines(k, 2)}
                g.DrawLines(shadow, pts)
                g.DrawLines(pen, pts)
                g.DrawLine(shadow, bars(k, 0), bars(k, 1))
                g.DrawLine(pen, bars(k, 0), bars(k, 1))
            Next
        End Using
    End Sub

    ''' <summary>框內下方顯示輸出尺寸（原圖像素）。</summary>
    Private Sub DrawCropSize(g As Graphics, r As RectangleF)
        If _cropPixelSize.IsEmpty Then Return
        Dim w = CInt(Math.Round(_crop.Width * _cropPixelSize.Width)), h = CInt(Math.Round(_crop.Height * _cropPixelSize.Height))
        Dim text = $"{w} × {h} px"
        Dim sz = TextRenderer.MeasureText(text, Font)
        Dim box = New Rectangle(CInt(r.X + r.Width / 2 - sz.Width / 2 - 6), CInt(r.Bottom - sz.Height - 14), sz.Width + 12, sz.Height + 4)
        If r.Height < sz.Height + 30 Then box.Y = CInt(r.Bottom + 8)
        Using br As New SolidBrush(Color.FromArgb(170, 0, 0, 0))
            g.FillRectangle(br, box)
        End Using
        TextRenderer.DrawText(g, text, Font, box, Color.White, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
    End Sub
End Class
