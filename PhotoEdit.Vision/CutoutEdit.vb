Imports System.Runtime.InteropServices
Imports OpenCvSharp

''' <summary>去背面板筆刷的一點（人物圖的像素座標；Keep＝保留，否則擦除）。</summary>
Public Structure CutoutDab
    Public X, Y, R As Single
    Public Keep As Boolean
    Public Sub New(x As Single, y As Single, r As Single, keep As Boolean)
        Me.X = x : Me.Y = y : Me.R = r : Me.Keep = keep
    End Sub
End Structure

''' <summary>去背面板的設定（AI 遮罩之後的修整；不含筆刷）。</summary>
Public Class CutoutEditOptions
    ''' <summary>邊緣精修 0..100（見 CompositeBaker.RefineMask）。</summary>
    Public Property EdgeRefine As Integer = 50
    ''' <summary>邊緣內縮（負）／外擴（正），像素，-10..10。</summary>
    Public Property EdgeShift As Integer
    ''' <summary>去掉零碎小塊：和最大的那塊比起來很小、又沒連在一起的前景。</summary>
    Public Property RemoveSpecks As Boolean
    ''' <summary>填補小破洞：被人包住的小塊背景（不碰到圖邊的）。</summary>
    Public Property FillHoles As Boolean
    ''' <summary>去除邊緣殘色（做圖層時用，不改遮罩）。</summary>
    Public Property Decontaminate As Boolean = True

    Public Function Clone() As CutoutEditOptions
        Return DirectCast(MemberwiseClone(), CutoutEditOptions)
    End Function
End Class

''' <summary>
''' 去背面板的遮罩計算：AI 遮罩 →（原圖本來透明時不精修）邊緣精修 → 內縮／外擴 → 去掉零碎小塊 → 填補小破洞 → 依序套用筆刷。
''' 人物合成、吶喊等特效共用（介面是 PhotoEdit 的 CutoutPanel）。
''' </summary>
Public NotInheritable Class CutoutEdit
    Private Sub New()
    End Sub

    ''' <summary>小塊的門檻：比最大那塊的這個比例還小就算零碎。</summary>
    Public Const SpeckFraction As Double = 0.02
    ''' <summary>破洞的門檻：比人（前景面積）的這個比例還小才補。</summary>
    Public Const HoleFraction As Double = 0.01

    Public Shared Function Build(aiMask As Byte(), aiMaskIsSource As Boolean, figurePx As Byte(), w As Integer, h As Integer,
                                 model As CutoutModel, options As CutoutEditOptions, strokes As IEnumerable(Of CutoutDab)) As Byte()
        Dim m = If(aiMaskIsSource OrElse options.EdgeRefine <= 0, DirectCast(aiMask.Clone(), Byte()),
                   CompositeBaker.RefineMask(aiMask, figurePx, w, h, options.EdgeRefine, model))
        If options.EdgeShift <> 0 Then m = Shift(m, w, h, options.EdgeShift)
        If options.RemoveSpecks Then m = RemoveSpecks(m, w, h)
        If options.FillHoles Then m = FillHoles(m, w, h)
        If strokes IsNot Nothing Then
            For Each s In strokes
                PaintDab(m, w, h, s.X, s.Y, s.R, s.Keep)
            Next
        End If
        Return m
    End Function

    ''' <summary>圓形筆刷點一下（邊上 1.5 像素柔和過渡）。</summary>
    Public Shared Sub PaintDab(mask As Byte(), w As Integer, h As Integer, cx As Single, cy As Single, r As Single, keep As Boolean)
        Dim x0 = Math.Max(0, CInt(cx - r)), x1 = Math.Min(w - 1, CInt(cx + r))
        Dim y0 = Math.Max(0, CInt(cy - r)), y1 = Math.Min(h - 1, CInt(cy + r))
        For y = y0 To y1
            For x = x0 To x1
                Dim d = Math.Sqrt((x - cx) ^ 2 + (y - cy) ^ 2)
                If d > r Then Continue For
                Dim a = Math.Clamp((r - d) / 1.5, 0, 1)
                Dim i = y * w + x
                Dim target = If(keep, 255, 0)
                mask(i) = CByte(Math.Round(mask(i) + (target - mask(i)) * a))
            Next
        Next
    End Sub

    ''' <summary>內縮（負）／外擴（正）：灰階的侵蝕／膨脹（圓形），半透明的邊跟著移動。</summary>
    Public Shared Function Shift(mask As Byte(), w As Integer, h As Integer, pixels As Integer) As Byte()
        If pixels = 0 Then Return mask
        Dim r = Math.Abs(pixels)
        Using m As New Mat(h, w, MatType.CV_8UC1), k = Cv2.GetStructuringElement(MorphShapes.Ellipse, New Size(2 * r + 1, 2 * r + 1))
            Marshal.Copy(mask, 0, m.Data, mask.Length)
            If pixels > 0 Then Cv2.Dilate(m, m, k) Else Cv2.Erode(m, m, k)
            Dim out(mask.Length - 1) As Byte
            Marshal.Copy(m.Data, out, 0, out.Length)
            Return out
        End Using
    End Function

    ''' <summary>去掉零碎小塊：前景（≥ 50%）裡沒和最大那塊連在一起、面積又小於它 SpeckFraction 的，整塊變透明。</summary>
    Public Shared Function RemoveSpecks(mask As Byte(), w As Integer, h As Integer) As Byte()
        Dim out = DirectCast(mask.Clone(), Byte())
        Using bin As New Mat(), labels As New Mat(), stats As New Mat(), cent As New Mat(), src As New Mat(h, w, MatType.CV_8UC1)
            Marshal.Copy(mask, 0, src.Data, mask.Length)
            Cv2.Threshold(src, bin, 127, 255, ThresholdTypes.Binary)
            Dim n = Cv2.ConnectedComponentsWithStats(bin, labels, stats, cent, PixelConnectivity.Connectivity8)
            If n <= 2 Then Return out
            Dim largest = 0
            For i = 1 To n - 1
                largest = Math.Max(largest, stats.At(Of Integer)(i, CInt(ConnectedComponentsTypes.Area)))
            Next
            Dim drop(n - 1) As Boolean
            Dim any = False
            For i = 1 To n - 1
                drop(i) = stats.At(Of Integer)(i, CInt(ConnectedComponentsTypes.Area)) < largest * SpeckFraction
                any = any OrElse drop(i)
            Next
            If Not any Then Return out
            Dim lab(w * h - 1) As Integer
            Marshal.Copy(labels.Data, lab, 0, lab.Length)
            ' 小塊和它旁邊淡淡的邊一起清掉：先標出要清的像素，再往外擴 2 像素清半透明的部分
            Dim kill(w * h - 1) As Byte
            For i = 0 To lab.Length - 1
                If drop(lab(i)) Then kill(i) = 255
            Next
            Dim grown = Shift(kill, w, h, 2)
            For i = 0 To out.Length - 1
                If kill(i) <> 0 OrElse (grown(i) <> 0 AndAlso out(i) < 128) Then out(i) = 0
            Next
        End Using
        Return out
    End Function

    ''' <summary>填補小破洞：背景（&lt; 50%）裡不碰到圖邊、面積小於前景 HoleFraction 的，整塊變不透明。</summary>
    Public Shared Function FillHoles(mask As Byte(), w As Integer, h As Integer) As Byte()
        Dim out = DirectCast(mask.Clone(), Byte())
        Dim fg = 0
        For Each v In mask
            If v >= 128 Then fg += 1
        Next
        If fg = 0 Then Return out
        Using bin As New Mat(), labels As New Mat(), stats As New Mat(), cent As New Mat(), src As New Mat(h, w, MatType.CV_8UC1)
            Marshal.Copy(mask, 0, src.Data, mask.Length)
            Cv2.Threshold(src, bin, 127, 255, ThresholdTypes.BinaryInv)
            Dim n = Cv2.ConnectedComponentsWithStats(bin, labels, stats, cent, PixelConnectivity.Connectivity4)
            Dim fill(n - 1) As Boolean
            Dim any = False
            For i = 1 To n - 1
                Dim x = stats.At(Of Integer)(i, CInt(ConnectedComponentsTypes.Left)), y = stats.At(Of Integer)(i, CInt(ConnectedComponentsTypes.Top))
                Dim bw = stats.At(Of Integer)(i, CInt(ConnectedComponentsTypes.Width)), bh = stats.At(Of Integer)(i, CInt(ConnectedComponentsTypes.Height))
                Dim touches = x = 0 OrElse y = 0 OrElse x + bw >= w OrElse y + bh >= h
                fill(i) = Not touches AndAlso stats.At(Of Integer)(i, CInt(ConnectedComponentsTypes.Area)) < fg * HoleFraction
                any = any OrElse fill(i)
            Next
            If Not any Then Return out
            Dim lab(w * h - 1) As Integer
            Marshal.Copy(labels.Data, lab, 0, lab.Length)
            For i = 0 To lab.Length - 1
                If fill(lab(i)) Then out(i) = 255
            Next
        End Using
        Return out
    End Function

    ''' <summary>去除邊緣殘色（直接改 px；半徑和合成圖層一樣依精修與模型決定）。</summary>
    Public Shared Sub Decontaminate(px As Byte(), mask As Byte(), w As Integer, h As Integer, options As CutoutEditOptions, model As CutoutModel)
        If Not options.Decontaminate Then Return
        CutoutCompositor.Decontaminate(px, mask, w, h, CutoutCompositor.RefineRadius(w, h, Math.Max(30, options.EdgeRefine), CutoutModels.InputSize(model)))
    End Sub

    ''' <summary>模型留下的淡淡一層清掉（10% 以下當背景、90% 以上當人）。回傳新的陣列。</summary>
    Public Shared Function StretchFaint(mask As Byte()) As Byte()
        Dim out(mask.Length - 1) As Byte
        For i = 0 To mask.Length - 1
            out(i) = CByte(Math.Clamp((mask(i) - 25.5) / 0.8, 0, 255))
        Next
        Return out
    End Function
End Class
