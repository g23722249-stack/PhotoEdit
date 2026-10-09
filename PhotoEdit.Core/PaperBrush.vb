Imports System.Drawing
Imports System.Drawing.Imaging

''' <summary>
''' 繪圖圖層和紙張的互動：LayerStack.Draw 畫圖層前設定 CurrentPaper，
''' 筆刷上色時讀紙紋高度（Shade：輕畫只碰到凸起、水彩沉積在凹處），
''' 表面紋理選「只有繪圖」時，每個圖層合成前先壓印紙紋。
''' 紙張每個執行緒各自一份（預覽與匯出可能同時算圖）。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    <ThreadStatic> Private Shared _paper As PaperSettings
    <ThreadStatic> Private Shared _paperMap As Single()
    <ThreadStatic> Private Shared _paperW As Integer
    <ThreadStatic> Private Shared _paperH As Integer

    ''' <summary>目前算圖的文件紙張（Nothing = 不用紙張，筆刷用內建紋路）。</summary>
    Public Shared Property CurrentPaper As PaperSettings
        Get
            Return _paper
        End Get
        Set(value As PaperSettings)
            _paper = value
            _paperMap = Nothing
        End Set
    End Property

    ''' <summary>放進快取鍵：換紙或改紙紋時，圖層要重算。</summary>
    Private Shared Function PaperKey() As String
        If _paper Is Nothing Then Return ""
        Return "|paper:" & _paper.HeightKey()
    End Function

    ''' <summary>這次算圖（w × h）要用的紙紋高度圖。</summary>
    Private Shared Sub PreparePaper(w As Integer, h As Integer)
        If _paper Is Nothing Then
            _paperMap = Nothing
            Return
        End If
        If _paperMap Is Nothing OrElse _paperW <> w OrElse _paperH <> h Then
            _paperMap = Papers.HeightMap(_paper, w, h)
            _paperW = w : _paperH = h
        End If
    End Sub

    Private Shared Function PaperAt(gx As Integer, gy As Integer) As Single
        Return _paperMap(Math.Max(0, Math.Min(_paperH - 1, gy)) * _paperW + Math.Max(0, Math.Min(_paperW - 1, gx)))
    End Function

    ''' <summary>乾性媒材與水彩：紙紋影響最明顯。</summary>
    Private Shared Function IsGrainy(b As BrushKind) As Boolean
        Select Case b
            Case BrushKind.Pencil, BrushKind.Charcoal, BrushKind.Chalk, BrushKind.Crayon, BrushKind.DryBrush, BrushKind.Watercolor
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>這支筆吃紙紋的程度 0..1：乾性媒材照「紙紋吃色」，其他筆刷只有三分之一，特效、紋理、貼圖不吃。</summary>
    Private Shared Function PaperGrainAmount(layer As DrawLayer) As Single
        If _paperMap Is Nothing Then Return 0
        Select Case layer.Brush
            Case BrushKind.FX, BrushKind.Texture, BrushKind.StickerHose, BrushKind.Particle
                Return 0
        End Select
        Dim g = Math.Max(0, Math.Min(100, layer.PaperGrain)) / 100.0F
        Return If(IsGrainy(layer.Brush), g, g * 0.35F)
    End Function

    ''' <summary>鉛筆、炭筆、粉筆、蠟筆、水彩本來就有紙紋雜訊：有紙張時依吃色程度換成紙張的紋路。</summary>
    Private Shared Function PaperNoise(own As Single, height As Single, grain As Single) As Single
        If grain <= 0 Then Return own
        Return own * (1 - grain) + height * grain
    End Function

    ''' <summary>
    ''' 紙紋遮罩：覆蓋率 a 越高（用力、顏料多）門檻越低、越能填進凹處；輕畫只留下凸起。
    ''' 回傳透明度要乘的值（1 = 不受紙紋影響）。
    ''' </summary>
    Private Shared Function PaperMask(a As Single, height As Single, grain As Single) As Single
        If grain <= 0 Then Return 1
        Dim thr = 1 - Math.Min(1, a) * 1.15F
        Dim mask = SmoothStep(thr - 0.15F, thr + 0.15F, height)
        Return 1 - grain * (1 - mask)
    End Function

    ''' <summary>表面紋理選「只有繪圖」：圖層的影像先壓印紙紋再合成（region 為圖層在照片上的位置）。</summary>
    Private Shared Function SurfaceOnDrawing(src As Bitmap, region As Rectangle, w As Integer, h As Integer) As Bitmap
        If _paper Is Nothing OrElse _paper.SurfaceStrength <= 0 OrElse _paper.SurfaceTarget <> SurfaceTarget.Drawings Then Return Nothing
        Dim map = Papers.HeightMap(_paper, w, h)
        Dim shaded = src.Clone(New Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format32bppArgb)
        Papers.ApplySurface(shaded, map, w, h, region.X, region.Y, _paper.SurfaceStrength, _paper.LightAngle)
        Return shaded
    End Function
End Class
