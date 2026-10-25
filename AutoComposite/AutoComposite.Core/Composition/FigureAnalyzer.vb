''' <summary>人物輪廓分析結果（人物原圖像素座標）。</summary>
Public NotInheritable Class FigureAnalysis
    ''' <summary>Alpha &gt; 門檻的有效外框。</summary>
    Public Property Bounds As PixelRect
    ''' <summary>兩腳之間的地面接觸點（底部數列的 Alpha 加權中心，y 取最底列）。</summary>
    Public Property FeetCenter As PointD
    ''' <summary>腳部左右範圍（x）。</summary>
    Public Property FeetLeft As Double
    Public Property FeetRight As Double
    ''' <summary>頭部估計位置（頂部區域的 Alpha 加權中心）。</summary>
    Public Property Head As PointD
    ''' <summary>整體 Alpha 重心。</summary>
    Public Property Centroid As PointD
    ''' <summary>軀幹區域（中段）主軸相對垂直方向的傾角（度，順時針為正）。</summary>
    Public Property AxisTiltDegrees As Double
    ''' <summary>軀幹中心。</summary>
    Public Property Torso As PointD
    ''' <summary>前景像素比例（0..1）。</summary>
    Public Property Coverage As Double

    Public ReadOnly Property Height As Double
        Get
            Return Bounds.Height
        End Get
    End Property

    Public ReadOnly Property FeetWidth As Double
        Get
            Return Math.Max(1, FeetRight - FeetLeft)
        End Get
    End Property

    ''' <summary>依錨點種類取人物上的點（Custom／SingleFoot 由使用者指定，這裡回傳腳中心）。</summary>
    Public Function AnchorFor(kind As AnchorKind) As PointD
        Select Case kind
            Case AnchorKind.BottomCenter : Return New PointD(Bounds.X + Bounds.Width / 2.0, Bounds.Bottom)
            Case AnchorKind.Center : Return Centroid
            Case AnchorKind.Head : Return Head
            Case Else : Return FeetCenter
        End Select
    End Function
End Class

''' <summary>
''' 由前景 Alpha 分析人物輪廓：外框、腳部接觸點與寬度、頭部、軀幹主軸。
''' 只依輪廓統計估算，不辨識姿勢；長矛、披風等延伸物會影響外框，所以腳點以底部「接地列」的分布計算，
''' 並排除只占極少像素的細長物（例如矛尖）。
''' </summary>
Public NotInheritable Class FigureAnalyzer
    Private Sub New()
    End Sub

    Public Const Threshold As Single = 0.2F

    Public Shared Function Analyze(alpha As FloatMask) As FigureAnalysis
        Dim w = alpha.Width, h = alpha.Height
        Dim boundsQ = alpha.BoundsAbove(Threshold)
        If Not boundsQ.HasValue Then Throw New InvalidOperationException("遮罩中沒有前景像素，無法分析人物。")
        Dim bounds = boundsQ.Value
        Dim d = alpha.Data

        ' 每列前景量；接地列 = 由下往上第一個「前景量達最寬列 8%」的列，排除底部零星雜點與細長物。
        Dim rowSum(h - 1) As Double
        Dim maxRow = 0.0, total = 0.0, cx = 0.0, cy = 0.0
        For y = bounds.Y To bounds.Bottom - 1
            Dim s = 0.0
            For x = bounds.X To bounds.Right - 1
                Dim v = d(y * w + x)
                If v > Threshold Then
                    s += v
                    cx += x * v : cy += y * v
                End If
            Next
            rowSum(y) = s
            total += s
            If s > maxRow Then maxRow = s
        Next
        Dim groundRow = bounds.Bottom - 1
        For y = bounds.Bottom - 1 To bounds.Y Step -1
            If rowSum(y) >= maxRow * 0.08 Then groundRow = y : Exit For
        Next

        ' 腳部帶：接地列往上 4% 人物高度。
        Dim band = Math.Max(2, CInt(bounds.Height * 0.04))
        Dim fx = 0.0, fw = 0.0, left = Double.MaxValue, right = Double.MinValue
        Dim colSum(w - 1) As Double
        For y = Math.Max(bounds.Y, groundRow - band) To groundRow
            For x = bounds.X To bounds.Right - 1
                Dim v = d(y * w + x)
                If v > Threshold Then
                    fx += x * v : fw += v
                    colSum(x) += v
                End If
            Next
        Next
        ' 左右範圍取累積分布 3%～97%，忽略落在腳邊的武器尖端等零星像素。
        Dim acc = 0.0
        For x = bounds.X To bounds.Right - 1
            acc += colSum(x)
            If left = Double.MaxValue AndAlso acc >= fw * 0.03 Then left = x
            If acc >= fw * 0.97 Then right = x + 1 : Exit For
        Next
        If left = Double.MaxValue Then left = bounds.X
        If right = Double.MinValue Then right = bounds.Right
        Dim feet = New PointD(If(fw > 0, fx / fw + 0.5, bounds.X + bounds.Width / 2.0), groundRow + 1)

        ' 頭部：頂部 12% 的 Alpha 加權中心。
        Dim headBand = Math.Max(2, CInt(bounds.Height * 0.12))
        Dim hx = 0.0, hy = 0.0, hw = 0.0
        For y = bounds.Y To Math.Min(bounds.Bottom - 1, bounds.Y + headBand)
            For x = bounds.X To bounds.Right - 1
                Dim v = d(y * w + x)
                If v > Threshold Then hx += x * v : hy += y * v : hw += v
            Next
        Next
        Dim head = If(hw > 0, New PointD(hx / hw + 0.5, hy / hw + 0.5), New PointD(bounds.X + bounds.Width / 2.0, bounds.Y))

        ' 軀幹主軸：外框 25%～75% 高度的二階矩。
        Dim ty0 = bounds.Y + CInt(bounds.Height * 0.25), ty1 = bounds.Y + CInt(bounds.Height * 0.75)
        Dim sx = 0.0, sy = 0.0, sw = 0.0
        For y = ty0 To ty1
            For x = bounds.X To bounds.Right - 1
                Dim v = d(y * w + x)
                If v > Threshold Then sx += x * v : sy += y * v : sw += v
            Next
        Next
        Dim tilt = 0.0
        Dim torso = New PointD(bounds.X + bounds.Width / 2.0, (ty0 + ty1) / 2.0)
        If sw > 0 Then
            Dim mx = sx / sw, my = sy / sw
            torso = New PointD(mx + 0.5, my + 0.5)
            Dim cxx = 0.0, cyy = 0.0, cxy = 0.0
            For y = ty0 To ty1
                For x = bounds.X To bounds.Right - 1
                    Dim v = d(y * w + x)
                    If v > Threshold Then
                        Dim dx = x - mx, dy = y - my
                        cxx += dx * dx * v : cyy += dy * dy * v : cxy += dx * dy * v
                    End If
                Next
            Next
            ' 主軸角度（相對 x 軸），轉成相對垂直方向的傾角。
            Dim theta = 0.5 * Math.Atan2(2 * cxy, cxx - cyy)
            Dim fromVertical = theta * 180 / Math.PI - 90
            While fromVertical < -90 : fromVertical += 180 : End While
            While fromVertical > 90 : fromVertical -= 180 : End While
            ' 軀幹明顯是直立長條時主軸才有意義；接近圓形時不判斷傾角。
            If cyy > cxx * 1.3 Then tilt = fromVertical
        End If

        Return New FigureAnalysis With {
            .Bounds = bounds, .FeetCenter = feet, .FeetLeft = left, .FeetRight = right, .Head = head,
            .Centroid = If(total > 0, New PointD(cx / total + 0.5, cy / total + 0.5), torso),
            .AxisTiltDegrees = tilt, .Torso = torso, .Coverage = total / (CDbl(w) * h)}
    End Function
End Class
