''' <summary>背景上推薦的站立位置。</summary>
Public NotInheritable Class SurfaceSuggestion
    ''' <summary>建議的人物腳點（背景像素）。</summary>
    Public Property Anchor As PointD
    Public Property SurfaceLeftX As Double
    Public Property SurfaceRightX As Double
    Public Property SurfaceY As Double
    ''' <summary>0..1，越高越推薦。</summary>
    Public Property Score As Double
    Public Property Reason As String = ""
    ''' <summary>是否由天際線（結構頂部對天空）偵測而來；是的話陰影只應落在非天空區域。</summary>
    Public Property FromSkyline As Boolean

    Public ReadOnly Property SurfaceWidth As Double
        Get
            Return SurfaceRightX - SurfaceLeftX
        End Get
    End Property
End Class

''' <summary>背景分析結果。</summary>
Public NotInheritable Class SceneAnalysis
    ''' <summary>天空遮罩（背景原尺寸，1 = 天空）。</summary>
    Public Property SkyMask As FloatMask
    Public Property SkyFraction As Double
    ''' <summary>每一欄（背景原圖 x）天際線的 y（該欄第一個非天空像素），沒有天空的欄為 0。</summary>
    Public Property Skyline As Double()
    Public Property Suggestions As New List(Of SurfaceSuggestion)()
    ''' <summary>粗略光源方向估計：+1 = 光從右邊來，-1 = 左邊，0 = 無法判斷。</summary>
    Public Property LightSide As Double
End Class

''' <summary>
''' 背景分析：估計天空區域與天際線，找出頂部對著天空、寬度足夠又平坦的結構頂面（柱頂、屋頂、平台）作為推薦站立點。
''' 這是單張照片的啟發式分析，不是 3D 重建；結果只當推薦，使用者可以在畫布上修正錨點。
''' </summary>
Public NotInheritable Class SceneAnalyzer
    Private Sub New()
    End Sub

    Private Const WorkSide As Integer = 512

    Public Shared Function Analyze(background As RgbaImage) As SceneAnalysis
        Dim scale As Double
        Dim small = background.Downscale(WorkSide, scale)
        Dim w = small.Width, h = small.Height
        Dim sky = EstimateSky(small)

        Dim result As New SceneAnalysis()
        Dim skyCount = 0.0
        For Each v In sky.Data
            skyCount += v
        Next
        result.SkyFraction = skyCount / sky.Data.Length
        result.SkyMask = sky.Resize(background.Width, background.Height)
        result.LightSide = EstimateLightSide(small)

        ' 天際線（小圖座標）。
        Dim line(w - 1) As Integer
        For x = 0 To w - 1
            Dim y = 0
            While y < h AndAlso sky(x, y) > 0.5F
                y += 1
            End While
            line(x) = y
        Next
        Dim inv = 1 / scale
        result.Skyline = New Double(background.Width - 1) {}
        For x = 0 To background.Width - 1
            result.Skyline(x) = line(Math.Min(w - 1, CInt(x * scale))) * inv
        Next

        If result.SkyFraction >= 0.03 Then
            For Each s In FindPlateaus(line, w, h)
                result.Suggestions.Add(New SurfaceSuggestion With {
                    .Anchor = New PointD(s.Center * inv, s.Y * inv), .SurfaceLeftX = s.Left * inv, .SurfaceRightX = (s.Right + 1) * inv,
                    .SurfaceY = s.Y * inv, .Score = s.Score, .FromSkyline = True,
                    .Reason = $"天際線上的平坦頂面（寬 {CInt((s.Right - s.Left + 1) * inv)} px），頂部對著天空"})
            Next
        End If
        If result.Suggestions.Count = 0 Then
            result.Suggestions.Add(New SurfaceSuggestion With {
                .Anchor = New PointD(background.Width / 2.0, background.Height * 0.9),
                .SurfaceLeftX = background.Width * 0.3, .SurfaceRightX = background.Width * 0.7, .SurfaceY = background.Height * 0.9,
                .Score = 0.2, .Reason = If(result.SkyFraction < 0.03, "未偵測到天空，預設放在畫面下方中央的地面", "沒有找到夠寬的平坦頂面，預設放在畫面下方中央")})
        End If
        result.Suggestions.Sort(Function(a, b) b.Score.CompareTo(a.Score))
        Return result
    End Function

    ''' <summary>
    ''' 天空估計：以頂部幾列的顏色為種子模型，從頂端往下做區域成長，只接受和鄰點相近、
    ''' 又和天空模型（容許垂直漸層）不太遠的像素。回傳 0/1 遮罩，邊緣稍微模糊。
    ''' </summary>
    Public Shared Function EstimateSky(img As RgbaImage) As FloatMask
        Dim w = img.Width, h = img.Height
        Dim p = img.Pixels
        Dim mask As New FloatMask(w, h)
        ' 頂部 4% 的平均與標準差。
        Dim seedRows = Math.Max(1, CInt(h * 0.04))
        Dim mr = 0.0, mg = 0.0, mb = 0.0, n = 0
        For y = 0 To seedRows - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                mr += p(i) : mg += p(i + 1) : mb += p(i + 2) : n += 1
            Next
        Next
        mr /= n : mg /= n : mb /= n
        Dim varSum = 0.0
        For y = 0 To seedRows - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                varSum += (p(i) - mr) ^ 2 + (p(i + 1) - mg) ^ 2 + (p(i + 2) - mb) ^ 2
            Next
        Next
        Dim seedStd = Math.Sqrt(varSum / n / 3)
        ' 頂部本身就很雜（樹葉、建築），代表沒有乾淨的天空。
        If seedStd > 40 Then Return mask
        Dim globalTol = 60 + seedStd * 3
        Dim localTol = 14 + seedStd * 0.8

        Dim visited(w * h - 1) As Boolean
        Dim queue As New Queue(Of Integer)()
        For x = 0 To w - 1
            Dim i = x
            If ColorDist(p, i, mr, mg, mb) <= globalTol Then
                visited(i) = True
                queue.Enqueue(i)
            End If
        Next
        While queue.Count > 0
            Dim i = queue.Dequeue()
            mask.Data(i) = 1
            Dim x = i Mod w, y = i \ w
            For Each nb In {If(x > 0, i - 1, -1), If(x < w - 1, i + 1, -1), If(y > 0, i - w, -1), If(y < h - 1, i + w, -1)}
                If nb < 0 OrElse visited(nb) Then Continue For
                If NeighborDist(p, i, nb) > localTol Then Continue For
                ' 天空往地平線會變亮、變淡：垂直越往下，對模型的容許差越大。
                Dim tol = globalTol * (1 + 0.8 * (nb \ w) / CDbl(h))
                If ColorDist(p, nb, mr, mg, mb) > tol Then Continue For
                visited(nb) = True
                queue.Enqueue(nb)
            Next
        End While
        ' 去掉細小的天空孔洞（結構中的縫隙不算可站立的天際線），再柔化邊緣。
        Dim opened = Blur.Morph(Blur.Morph(mask, -1), 1)
        Blur.GaussianInPlace(opened, 0.8)
        Return opened
    End Function

    Private Shared Function ColorDist(p As Byte(), i As Integer, r As Double, g As Double, b As Double) As Double
        Dim j = i * 4
        Return Math.Sqrt((p(j) - r) ^ 2 + (p(j + 1) - g) ^ 2 + (p(j + 2) - b) ^ 2)
    End Function

    Private Shared Function NeighborDist(p As Byte(), i As Integer, k As Integer) As Double
        Dim a = i * 4, b = k * 4
        Return Math.Sqrt((CInt(p(a)) - p(b)) ^ 2 + (CInt(p(a + 1)) - p(b + 1)) ^ 2 + (CInt(p(a + 2)) - p(b + 2)) ^ 2)
    End Function

    Private Structure Plateau
        Public Left As Integer
        Public Right As Integer
        Public Y As Double
        Public Center As Double
        Public Score As Double
    End Structure

    ' 天際線上「局部最高、平坦、夠寬」的區段。
    Private Shared Function FindPlateaus(line As Integer(), w As Integer, h As Integer) As List(Of Plateau)
        Dim result As New List(Of Plateau)()
        Dim tol = Math.Max(2, CInt(h * 0.015))
        Dim minWidth = Math.Max(3, CInt(w * 0.025))
        Dim x = 0
        While x < w
            ' 沒有結構（整欄都是天空）或結構頂部貼近底部的欄跳過。
            If line(x) >= h * 0.92 OrElse line(x) <= 0 Then
                x += 1
                Continue While
            End If
            Dim startX = x
            Dim ref = line(x)
            Dim sum = 0.0, cnt = 0
            While x < w AndAlso Math.Abs(line(x) - ref) <= tol AndAlso line(x) < h * 0.92
                sum += line(x) : cnt += 1
                x += 1
            End While
            Dim endX = x - 1
            Dim width = endX - startX + 1
            If width >= minWidth Then
                Dim y = sum / cnt
                ' 兩側必須比頂面低（往下）或是畫面邊緣：真正的頂面是局部最高點。
                Dim leftLower = startX = 0 OrElse line(Math.Max(0, startX - 1)) > y + tol
                Dim rightLower = endX = w - 1 OrElse line(Math.Min(w - 1, endX + 1)) > y + tol
                If leftLower OrElse rightLower Then
                    Dim flat = 0.0
                    For k = startX To endX
                        flat += Math.Abs(line(k) - y)
                    Next
                    flat = 1 - Math.Min(1, flat / cnt / tol)
                    Dim center = (startX + endX + 1) / 2.0
                    Dim centrality = 1 - Math.Abs(center / w - 0.5) * 1.2
                    ' 適中的寬度最好（畫面寬 5%～35%），太寬的通常是遠方地平線。
                    Dim wf = width / CDbl(w)
                    Dim widthScore = If(wf < 0.05, wf / 0.05, If(wf > 0.35, Math.Max(0, 1 - (wf - 0.35) * 2), 1))
                    ' 頂面上方要有足夠的天空讓人物站立。
                    Dim headroom = Math.Min(1, y / (h * 0.3))
                    Dim bothSides = If(leftLower AndAlso rightLower, 1.0, 0.7)
                    Dim score = 0.3 * widthScore + 0.2 * flat + 0.2 * centrality + 0.2 * headroom + 0.1 * bothSides
                    result.Add(New Plateau With {.Left = startX, .Right = endX, .Y = y, .Center = center, .Score = Math.Clamp(score, 0, 1)})
                End If
            End If
            If x = startX Then x += 1
        End While
        Return result
    End Function

    ' 粗略光源方向：比較上半部左右兩側的亮度。差距太小時回傳 0。
    Private Shared Function EstimateLightSide(img As RgbaImage) As Double
        Dim w = img.Width, h = img.Height, p = img.Pixels
        Dim l = 0.0, r = 0.0, nl = 0, nr = 0
        For y = 0 To h \ 2
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                Dim lum = 0.2126 * p(i) + 0.7152 * p(i + 1) + 0.0722 * p(i + 2)
                If x < w \ 2 Then
                    l += lum : nl += 1
                Else
                    r += lum : nr += 1
                End If
            Next
        Next
        If nl = 0 OrElse nr = 0 Then Return 0
        Dim diff = (r / nr - l / nl) / 255
        If Math.Abs(diff) < 0.03 Then Return 0
        Return Math.Clamp(diff * 5, -1, 1)
    End Function
End Class
