Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices

''' <summary>選取區的組合方式（像 PhotoImpact／Photoshop：新增、加入、減去、交集）。</summary>
Public Enum SelectionMode
    Replace = 0
    Add = 1
    Subtract = 2
    Intersect = 3
    ''' <summary>反轉目前的選取區（不看形狀）。</summary>
    Invert = 4
End Enum

Public Enum SelectionShape
    Rectangle = 0
    Ellipse = 1
    ''' <summary>多邊形：套索（自由或點選）與貝茲選取都轉成多邊形存。</summary>
    Polygon = 2
    ''' <summary>整張。</summary>
    All = 3
    ''' <summary>算好的遮罩（魔術棒、換算座標後的結果），存成 PNG。</summary>
    Mask = 4
End Enum

''' <summary>選取區的一步。座標為 0..1 的相對位置（畫面上的照片：裁切後、加邊框前）。</summary>
Public Class SelectionOp
    Public Property Mode As SelectionMode
    Public Property Shape As SelectionShape
    ''' <summary>矩形、橢圓的外框。</summary>
    Public Property X As Double
    Public Property Y As Double
    Public Property W As Double
    Public Property H As Double
    ''' <summary>多邊形頂點：x0, y0, x1, y1…</summary>
    Public Property Points As List(Of Double)
    ''' <summary>遮罩（8 位元灰階 PNG 的 Base64），蓋滿整張照片。</summary>
    Public Property MaskPng As String

    Public Function Clone() As SelectionOp
        Dim c = DirectCast(MemberwiseClone(), SelectionOp)
        c.Points = If(Points Is Nothing, Nothing, New List(Of Double)(Points))
        Return c
    End Function
End Class

''' <summary>選取區：依序套用的各步（新增／加入／減去／交集／反轉），最後羽化。</summary>
Public Class SelectionSpec
    Public Property Ops As New List(Of SelectionOp)()
    ''' <summary>羽化 0..100（100 = 照片短邊的 5%）。</summary>
    Public Property Feather As Integer

    Public Function Clone() As SelectionSpec
        Return New SelectionSpec With {.Ops = If(Ops, New List(Of SelectionOp)()).Select(Function(o) o.Clone()).ToList(), .Feather = Feather}
    End Function

    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return Ops Is Nothing OrElse Ops.Count = 0
        End Get
    End Property

    ''' <summary>加上一步；「新增」模式會先清掉之前的步驟。</summary>
    Public Sub Apply(op As SelectionOp)
        If Ops Is Nothing Then Ops = New List(Of SelectionOp)()
        If op.Mode = SelectionMode.Replace Then Ops.Clear()
        Ops.Add(op)
    End Sub
End Class

''' <summary>把選取區畫成遮罩（每個像素 0..255）、算範圍、編碼遮罩、換算到原圖座標。</summary>
Public NotInheritable Class SelectionMask
    Private Sub New()
    End Sub

    Private Shared ReadOnly Decoded As New ConditionalWeakTable(Of String, Bitmap)()

    ''' <summary>w × h 的遮罩（0 = 沒選、255 = 選到）。</summary>
    Public Shared Function Render(spec As SelectionSpec, w As Integer, h As Integer) As Byte()
        Dim m(w * h - 1) As Byte
        If spec Is Nothing OrElse spec.IsEmpty OrElse w <= 0 OrElse h <= 0 Then Return m
        For Each op In spec.Ops
            If op.Mode = SelectionMode.Invert Then
                For i = 0 To m.Length - 1
                    m(i) = CByte(255 - m(i))
                Next
                Continue For
            End If
            Dim s = ShapeMask(op, w, h)
            Select Case op.Mode
                Case SelectionMode.Replace : m = s
                Case SelectionMode.Add
                    For i = 0 To m.Length - 1
                        If s(i) > m(i) Then m(i) = s(i)
                    Next
                Case SelectionMode.Subtract
                    For i = 0 To m.Length - 1
                        m(i) = CByte(m(i) * (255 - s(i)) \ 255)
                    Next
                Case SelectionMode.Intersect
                    For i = 0 To m.Length - 1
                        If s(i) < m(i) Then m(i) = s(i)
                    Next
            End Select
        Next
        If spec.Feather > 0 Then
            Dim r = CInt(Math.Round(spec.Feather / 100.0 * 0.05 * Math.Min(w, h)))
            If r >= 1 Then Creative.BoxBlur(m, w, h, 1, r)
        End If
        Return m
    End Function

    Private Shared Function ShapeMask(op As SelectionOp, w As Integer, h As Integer) As Byte()
        Select Case op.Shape
            Case SelectionShape.All
                Dim all(w * h - 1) As Byte
                Array.Fill(all, CByte(255))
                Return all
            Case SelectionShape.Mask
                Return DecodeMask(op.MaskPng, w, h)
        End Select
        Using bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb), g = Graphics.FromImage(bmp)
            g.Clear(Color.Black)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.Half
            Using path = ShapePath(op, w, h)
                If path IsNot Nothing Then g.FillPath(Brushes.White, path)
            End Using
            Return Channel(bmp)
        End Using
    End Function

    ''' <summary>形狀的外形（像素座標）；遮罩、全選沒有外形時回傳 Nothing。</summary>
    Public Shared Function ShapePath(op As SelectionOp, w As Integer, h As Integer) As GraphicsPath
        Dim path As New GraphicsPath(FillMode.Winding)
        Select Case op.Shape
            Case SelectionShape.Rectangle
                path.AddRectangle(New RectangleF(CSng(op.X * w), CSng(op.Y * h), CSng(op.W * w), CSng(op.H * h)))
            Case SelectionShape.Ellipse
                path.AddEllipse(New RectangleF(CSng(op.X * w), CSng(op.Y * h), CSng(op.W * w), CSng(op.H * h)))
            Case SelectionShape.Polygon
                If op.Points Is Nothing OrElse op.Points.Count < 6 Then path.Dispose() : Return Nothing
                Dim pts(op.Points.Count \ 2 - 1) As PointF
                For i = 0 To pts.Length - 1
                    pts(i) = New PointF(CSng(op.Points(i * 2) * w), CSng(op.Points(i * 2 + 1) * h))
                Next
                path.AddPolygon(pts)
            Case Else
                path.Dispose()
                Return Nothing
        End Select
        Return path
    End Function

    ''' <summary>選到的範圍（0..1 相對座標，遮罩值 &gt; 0 的外接矩形）；沒選到時回傳 Empty。</summary>
    Public Shared Function Bounds(spec As SelectionSpec, Optional sampleWidth As Integer = 512, Optional sampleHeight As Integer = 512) As RectangleF
        If spec Is Nothing OrElse spec.IsEmpty Then Return RectangleF.Empty
        Dim m = Render(spec, sampleWidth, sampleHeight)
        Dim l = sampleWidth, t = sampleHeight, r = -1, b = -1
        For y = 0 To sampleHeight - 1
            For x = 0 To sampleWidth - 1
                If m(y * sampleWidth + x) > 8 Then
                    If x < l Then l = x
                    If x > r Then r = x
                    If y < t Then t = y
                    If y > b Then b = y
                End If
            Next
        Next
        If r < 0 Then Return RectangleF.Empty
        Return RectangleF.FromLTRB(CSng(l / sampleWidth), CSng(t / sampleHeight), CSng((r + 1) / sampleWidth), CSng((b + 1) / sampleHeight))
    End Function

    '---------------------------------------------------------------------
    ' 遮罩編碼
    '---------------------------------------------------------------------

    ''' <summary>把 w × h 的遮罩存成 PNG（Base64）。</summary>
    Public Shared Function EncodeMask(mask As Byte(), w As Integer, h As Integer) As String
        Using bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Dim px(w * h * 4 - 1) As Byte
            For i = 0 To w * h - 1
                px(i * 4) = mask(i) : px(i * 4 + 1) = mask(i) : px(i * 4 + 2) = mask(i) : px(i * 4 + 3) = 255
            Next
            Perspective.WritePixels(bmp, px)
            Using ms As New MemoryStream()
                bmp.Save(ms, ImageFormat.Png)
                Return Convert.ToBase64String(ms.ToArray())
            End Using
        End Using
    End Function

    ''' <summary>解開遮罩並縮放成 w × h（雙線性）。</summary>
    Public Shared Function DecodeMask(png As String, w As Integer, h As Integer) As Byte()
        If String.IsNullOrEmpty(png) Then Return New Byte(w * h - 1) {}
        Dim src As Bitmap = Nothing
        SyncLock Decoded
            If Not Decoded.TryGetValue(png, src) Then
                Try
                    Using ms As New MemoryStream(Convert.FromBase64String(png)), img = Image.FromStream(ms)
                        src = New Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb)
                        Using g = Graphics.FromImage(src)
                            g.DrawImage(img, 0, 0, img.Width, img.Height)
                        End Using
                    End Using
                Catch ex As Exception When TypeOf ex Is FormatException OrElse TypeOf ex Is ArgumentException
                    Return New Byte(w * h - 1) {}
                End Try
                Decoded.Add(png, src)
            End If
        End SyncLock
        SyncLock src
            If src.Width = w AndAlso src.Height = h Then Return Channel(src)
            Using scaled As New Bitmap(w, h, PixelFormat.Format32bppArgb), g = Graphics.FromImage(scaled)
                g.InterpolationMode = InterpolationMode.HighQualityBilinear
                g.PixelOffsetMode = PixelOffsetMode.Half
                Using ia As New ImageAttributes()
                    ia.SetWrapMode(WrapMode.TileFlipXY)
                    g.DrawImage(src, New Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia)
                End Using
                Return Channel(scaled)
            End Using
        End SyncLock
    End Function

    ''' <summary>取出藍色色版（灰階遮罩三色相同）。</summary>
    Private Shared Function Channel(bmp As Bitmap) As Byte()
        Dim px = Perspective.ReadPixels(bmp)
        Dim m(bmp.Width * bmp.Height - 1) As Byte
        For i = 0 To m.Length - 1
            m(i) = px(i * 4)
        Next
        Return m
    End Function

    '---------------------------------------------------------------------
    ' 換算到原圖座標（去背用）
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 把畫面座標的選取區換算成原圖座標的遮罩選取區（去背在原圖上算）：
    ''' 原圖的每個點換到目前的畫面位置，取選取區在那裡的值。之後改裁切、旋轉，範圍會跟著照片內容走。
    ''' </summary>
    Public Shared Function ToSource(spec As SelectionSpec, recipe As EditRecipe, sourceWidth As Integer, sourceHeight As Integer,
                                    outputWidth As Integer, outputHeight As Integer, Optional maxSide As Integer = 2048) As SelectionSpec
        Dim scale = Math.Min(1.0, maxSide / CDbl(Math.Max(sourceWidth, sourceHeight)))
        Dim mw = Math.Max(1, CInt(Math.Round(sourceWidth * scale))), mh = Math.Max(1, CInt(Math.Round(sourceHeight * scale)))
        Dim ow = Math.Max(1, outputWidth), oh = Math.Max(1, outputHeight)
        Dim outMask = Render(spec, ow, oh)
        Dim m(mw * mh - 1) As Byte
        Threading.Tasks.Parallel.For(0, mh,
            Sub(y)
                For x = 0 To mw - 1
                    Dim p = GeometryMapper.MapPoint(New PointF(CSng((x + 0.5) / mw), CSng((y + 0.5) / mh)), recipe, sourceWidth, sourceHeight, applyCrop:=True)
                    Dim ox = CInt(Math.Floor(p.X * ow)), oy = CInt(Math.Floor(p.Y * oh))
                    If ox >= 0 AndAlso oy >= 0 AndAlso ox < ow AndAlso oy < oh Then m(y * mw + x) = outMask(oy * ow + ox)
                Next
            End Sub)
        Dim result As New SelectionSpec()
        result.Ops.Add(New SelectionOp With {.Mode = SelectionMode.Replace, .Shape = SelectionShape.Mask, .MaskPng = EncodeMask(m, mw, mh)})
        Return result
    End Function

    ''' <summary>反過來的選取區（選取區以外）。</summary>
    Public Shared Function Inverted(spec As SelectionSpec) As SelectionSpec
        Dim c = spec.Clone()
        c.Ops.Add(New SelectionOp With {.Mode = SelectionMode.Invert})
        Return c
    End Function

    '---------------------------------------------------------------------
    ' 描邊
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 選取區的邊線遮罩：寬度 widthPx，position 0 = 內側、1 = 置中、2 = 外側。
    ''' 以最大／最小值濾波做膨脹與侵蝕，再相減。
    ''' </summary>
    Public Shared Function Outline(mask As Byte(), w As Integer, h As Integer, widthPx As Integer, position As Integer) As Byte()
        widthPx = Math.Max(1, widthPx)
        Dim outerR = If(position = 0, 0, If(position = 1, (widthPx + 1) \ 2, widthPx))
        Dim innerR = If(position = 0, widthPx, If(position = 1, widthPx \ 2, 0))
        Dim outer = If(outerR > 0, Morph(mask, w, h, outerR, dilate:=True), mask)
        Dim inner = If(innerR > 0, Morph(mask, w, h, innerR, dilate:=False), mask)
        Dim result(w * h - 1) As Byte
        For i = 0 To result.Length - 1
            result(i) = CByte(Math.Max(0, CInt(outer(i)) - inner(i)))
        Next
        Return result
    End Function

    ''' <summary>方形視窗的最大值（膨脹）或最小值（侵蝕）濾波，先橫後直。</summary>
    Private Shared Function Morph(src As Byte(), w As Integer, h As Integer, r As Integer, dilate As Boolean) As Byte()
        Dim tmp(src.Length - 1) As Byte, dst(src.Length - 1) As Byte
        Dim pick = Function(a As Byte, b As Byte) If(dilate, If(a > b, a, b), If(a < b, a, b))
        Threading.Tasks.Parallel.For(0, h,
            Sub(y)
                Dim row = y * w
                For x = 0 To w - 1
                    Dim v = src(row + x)
                    For k = Math.Max(0, x - r) To Math.Min(w - 1, x + r)
                        v = pick(v, src(row + k))
                    Next
                    tmp(row + x) = v
                Next
            End Sub)
        Threading.Tasks.Parallel.For(0, w,
            Sub(x)
                For y = 0 To h - 1
                    Dim v = tmp(y * w + x)
                    For k = Math.Max(0, y - r) To Math.Min(h - 1, y + r)
                        v = pick(v, tmp(k * w + x))
                    Next
                    dst(y * w + x) = v
                Next
            End Sub)
        Return dst
    End Function
End Class
