Imports System.IO

''' <summary>測試用的合成影像與暫存資料夾。</summary>
Friend NotInheritable Class TestHelpers
    Private Sub New()
    End Sub

    Public Shared Function TempDir() As String
        Dim d = Path.Combine(Path.GetTempPath(), "ac-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(d)
        Return d
    End Function

    Public Shared Function Asset(name As String) As String
        Return Path.Combine(AppContext.BaseDirectory, "TestAssets", name)
    End Function

    ''' <summary>帶雜訊紋理的背景（每個像素都不同，方便偵測非預期改動）。</summary>
    Public Shared Function NoisyBackground(w As Integer, h As Integer, Optional seed As Integer = 7) As RgbaImage
        Dim rnd As New Random(seed)
        Dim img As New RgbaImage(w, h)
        Dim p = img.Pixels
        For i = 0 To w * h - 1
            p(i * 4) = CByte(rnd.Next(30, 220)) : p(i * 4 + 1) = CByte(rnd.Next(30, 220)) : p(i * 4 + 2) = CByte(rnd.Next(30, 220)) : p(i * 4 + 3) = 255
        Next
        Return img
    End Function

    ''' <summary>
    ''' 簡化的「人物」：黑底上白色站立長條（身體）＋頭部圓形，腳在底部。可選透明背景。
    ''' </summary>
    Public Shared Function StickFigure(w As Integer, h As Integer, Optional transparent As Boolean = False) As RgbaImage
        Dim img As New RgbaImage(w, h)
        Dim p = img.Pixels
        Dim cx = w / 2.0
        Dim bodyW = w * 0.3, top = h * 0.25, bottom = h * 0.92
        Dim headR = w * 0.15, headY = h * 0.15
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim inBody = Math.Abs(x + 0.5 - cx) <= bodyW / 2 AndAlso y + 0.5 >= top AndAlso y + 0.5 <= bottom
                Dim inHead = (x + 0.5 - cx) ^ 2 + (y + 0.5 - headY) ^ 2 <= headR * headR
                Dim i = (y * w + x) * 4
                If inBody OrElse inHead Then
                    p(i) = 240 : p(i + 1) = 200 : p(i + 2) = 60 : p(i + 3) = 255
                Else
                    p(i) = 10 : p(i + 1) = 10 : p(i + 2) = 10 : p(i + 3) = If(transparent, CByte(0), CByte(255))
                End If
            Next
        Next
        Return img
    End Function

    ''' <summary>StickFigure 的理想遮罩。</summary>
    Public Shared Function StickMask(img As RgbaImage) As FloatMask
        Dim m As New FloatMask(img.Width, img.Height)
        For i = 0 To m.Data.Length - 1
            m.Data(i) = If(img.Pixels(i * 4) > 128, 1.0F, 0.0F)
        Next
        Return m
    End Function

    ''' <summary>藍天＋灰色石柱的背景（石柱頂部在 columnTop，左右在 columnLeft..columnRight）。</summary>
    Public Shared Function ColumnScene(w As Integer, h As Integer, columnLeft As Integer, columnRight As Integer, columnTop As Integer) As RgbaImage
        Dim img As New RgbaImage(w, h)
        Dim rnd As New Random(3)
        Dim p = img.Pixels
        For y = 0 To h - 1
            For x = 0 To w - 1
                Dim i = (y * w + x) * 4
                If x >= columnLeft AndAlso x < columnRight AndAlso y >= columnTop Then
                    Dim v = 150 + rnd.Next(-12, 12)
                    p(i) = CByte(v) : p(i + 1) = CByte(v - 8) : p(i + 2) = CByte(v - 20)
                ElseIf y >= h * 0.95 Then
                    p(i) = 120 : p(i + 1) = 110 : p(i + 2) = 90
                Else
                    ' 天空：越往下越淡。
                    Dim t = y / CDbl(h)
                    p(i) = CByte(70 + 80 * t) : p(i + 1) = CByte(130 + 60 * t) : p(i + 2) = CByte(220 + 20 * t)
                End If
                p(i + 3) = 255
            Next
        Next
        Return img
    End Function

    Public Shared Function NewLayer(source As RgbaImage, alpha As FloatMask) As CompositeLayer
        Dim l As New CompositeLayer With {.Source = source, .Alpha = alpha, .Name = "測試人物"}
        Dim fig = FigureAnalyzer.Analyze(alpha)
        l.AnchorX = fig.FeetCenter.X : l.AnchorY = fig.FeetCenter.Y
        l.Shadow.Enabled = False
        Return l
    End Function

    ''' <summary>序列相等（xUnit 的 Span 多載 VB 不能用）。</summary>
    Public Shared Sub SameSeq(Of T)(expected As IEnumerable(Of T), actual As IEnumerable(Of T))
        Dim e = expected.ToArray(), a = actual.ToArray()
        Xunit.Assert.True(e.Length = a.Length, $"長度不同：預期 {e.Length}，實際 {a.Length}")
        For i = 0 To e.Length - 1
            If Not Equals(e(i), a(i)) Then Xunit.Assert.Fail($"第 {i} 個元素不同：預期 {e(i)}，實際 {a(i)}")
        Next
    End Sub
End Class
