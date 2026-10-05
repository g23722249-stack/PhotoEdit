Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Drawing.Text
Imports System.Threading.Tasks

Public Enum LocalKind
    Gradient = 0
    Brush = 1
End Enum

''' <summary>
''' 局部調整（漸層濾鏡或筆刷）。位置存在「已轉正原圖」的 0..1 座標，旋轉、裁切後仍貼在同一處。
''' </summary>
Public Class LocalAdjustment
    Public Property Kind As LocalKind
    ''' <summary>漸層：起點效果 100%，終點 0%。</summary>
    Public Property StartX As Double
    Public Property StartY As Double
    Public Property EndX As Double
    Public Property EndY As Double
    ''' <summary>筆刷筆觸（半徑以原圖長邊為 1）。</summary>
    Public Property Strokes As List(Of SpotStroke) = New List(Of SpotStroke)()

    Public Property Exposure As Double
    Public Property Contrast As Integer
    Public Property Saturation As Integer
    Public Property Temperature As Integer

    Public Function Clone() As LocalAdjustment
        Dim c = DirectCast(MemberwiseClone(), LocalAdjustment)
        c.Strokes = Strokes?.Select(Function(s) s.Clone()).ToList()
        Return c
    End Function

    Public ReadOnly Property HasEffect As Boolean
        Get
            Return Math.Abs(Exposure) > 0.001 OrElse Contrast <> 0 OrElse Saturation <> 0 OrElse Temperature <> 0
        End Get
    End Property

    Friend Function ToneRecipe() As EditRecipe
        Return New EditRecipe With {.Exposure = Exposure, .Contrast = Contrast, .Saturation = Saturation, .Temperature = Temperature}
    End Function
End Class

Public Enum OverlayKind
    Text = 0
    ''' <summary>內建向量貼圖（Sticker 為 heart、star…）。</summary>
    Sticker = 1
    ''' <summary>stick 資料夾裡的圖片貼圖（ImagePath 為「主題\檔名」）。</summary>
    Image = 2
End Enum

''' <summary>文字或貼圖。位置為輸出照片（裁切後、加邊框前）的 0..1 座標，Size 為照片高度的比例。</summary>
Public Class Overlay
    Public Property Kind As OverlayKind
    ''' <summary>圖層識別碼（排列順序用，見 EditRecipe.LayerOrder）；舊檔為 Nothing。</summary>
    Public Property Id As String
    ''' <summary>圖層是否顯示。</summary>
    Public Property Visible As Boolean = True
    ''' <summary>和下面圖層的混合模式。</summary>
    Public Property Blend As BlendMode
    Public Property Text As String = ""
    ''' <summary>heart / star / sparkle / bubble / arrow / ring</summary>
    Public Property Sticker As String = "heart"
    Public Property X As Double = 0.5
    Public Property Y As Double = 0.5
    Public Property Size As Double = 0.08
    Public Property ColorArgb As Integer = Color.White.ToArgb()
    Public Property FontName As String = "Microsoft JhengHei"
    Public Property Bold As Boolean = True
    Public Property Shadow As Boolean = True
    ''' <summary>順時針旋轉角度（度），以 (X, Y) 為中心。</summary>
    Public Property Rotation As Double
    ''' <summary>圖片貼圖：相對於 stick 資料夾的路徑，例如「生日\cake.png」。</summary>
    Public Property ImagePath As String

    ' ---- 共用效果（文字與貼圖）----
    ''' <summary>整體不透明度，0..100。</summary>
    Public Property Opacity As Integer = 100
    Public Property ShadowColorArgb As Integer = Color.Black.ToArgb()
    ''' <summary>陰影不透明度，0..100。</summary>
    Public Property ShadowOpacity As Integer = 43
    ''' <summary>陰影距離，0..100（以字高為準）。</summary>
    Public Property ShadowDistance As Integer = 20
    ''' <summary>陰影方向（度，0 = 往右，90 = 往下）。</summary>
    Public Property ShadowAngle As Integer = 51
    ''' <summary>陰影模糊，0..100。</summary>
    Public Property ShadowBlur As Integer
    Public Property GlowColorArgb As Integer = Color.FromArgb(255, 60, 200).ToArgb()
    ''' <summary>發光大小，0..100；0 表示不發光。</summary>
    Public Property GlowSize As Integer

    ' ---- 文字效果 ----
    Public Property OutlineColorArgb As Integer = Color.Black.ToArgb()
    ''' <summary>外框粗細，0..100。</summary>
    Public Property OutlineWidth As Integer
    Public Property Outline2ColorArgb As Integer = Color.White.ToArgb()
    ''' <summary>第二層（最外層）外框粗細，0..100。</summary>
    Public Property Outline2Width As Integer
    Public Property BackgroundStyle As TextBackground
    Public Property BackgroundColorArgb As Integer = Color.Black.ToArgb()
    Public Property BackgroundOpacity As Integer = 60
    Public Property BackgroundRadius As Integer = 30
    Public Property BackgroundPadding As Integer = 30
    Public Property FillMode As TextFill
    ''' <summary>漸層的第二色。</summary>
    Public Property Color2Argb As Integer = Color.FromArgb(255, 120, 40).ToArgb()
    ''' <summary>漸層／彩虹方向（度，90 = 由上而下）。</summary>
    Public Property GradientAngle As Integer = 90
    ''' <summary>圖片填字：完整路徑或 stick 資料夾裡的相對路徑。</summary>
    Public Property TexturePath As String
    Public Property ExtrudeColorArgb As Integer = Color.FromArgb(150, 80, 20).ToArgb()
    ''' <summary>立體厚度，0..100；0 表示平面。</summary>
    Public Property ExtrudeDepth As Integer
    Public Property ExtrudeAngle As Integer = 45
    ''' <summary>直排（由右而左、由上而下）。</summary>
    Public Property Vertical As Boolean
    ''' <summary>0 靠左、1 置中、2 靠右（多行時）。</summary>
    Public Property Align As Integer = 1
    ''' <summary>字距，-20..100（字高的百分比）。</summary>
    Public Property LetterSpacing As Integer
    ''' <summary>行距，50..300（%）。</summary>
    Public Property LineSpacing As Integer = 100
    ''' <summary>弧形，-360..360 度：正值向上拱（彩虹形），負值向下彎（微笑形），360 為整圈。</summary>
    Public Property Arc As Integer

    Public Function Clone() As Overlay
        Return DirectCast(MemberwiseClone(), Overlay)
    End Function

    ''' <summary>套用文字樣式預設集：複製外觀，保留文字內容、位置、大小與角度。</summary>
    Public Sub CopyStyleFrom(s As Overlay)
        Dim keepText = Text, keepX = X, keepY = Y, keepSize = Size, keepRot = Rotation, keepKind = Kind
        Dim keepSticker = Sticker, keepImage = ImagePath
        Dim keepId = Id, keepVisible = Visible, keepBlend = Blend ' 圖層屬性不算樣式
        For Each p In GetType(Overlay).GetProperties()
            If p.CanWrite Then p.SetValue(Me, p.GetValue(s))
        Next
        Text = keepText : X = keepX : Y = keepY : Size = keepSize : Rotation = keepRot : Kind = keepKind
        Sticker = keepSticker : ImagePath = keepImage
        Id = keepId : Visible = keepVisible : Blend = keepBlend
    End Sub
End Class

Public Enum TextBackground
    None = 0
    ''' <summary>圓角方塊。</summary>
    Box = 1
    ''' <summary>橫跨整張照片的字幕條。</summary>
    Bar = 2
    ''' <summary>壓暗整張照片（搭配「照片本身」填色就是鏤空字）。</summary>
    DimPhoto = 3
    ''' <summary>只有外框的印章框。</summary>
    Frame = 4
End Enum

Public Enum TextFill
    Solid = 0
    Gradient = 1
    Rainbow = 2
    Texture = 3
    ''' <summary>用照片本身填字。</summary>
    Photo = 4
End Enum

''' <summary>文字/貼圖的選取框：旋轉中心（0..1）、相對中心的未旋轉外框（以照片寬高為 1）、角度。</summary>
Public Structure OverlayFrameInfo
    Public PivotX As Single
    Public PivotY As Single
    Public Left As Single
    Public Top As Single
    Public Right As Single
    Public Bottom As Single
    Public Rotation As Single
End Structure

Public Enum PhotoFrameStyle
    None = 0
    White = 1
    Black = 2
    Polaroid = 3
    Rounded = 4
    Film = 5
End Enum

''' <summary>創意特效的算圖：局部調整、背景模糊、移軸、文字貼圖、邊框。</summary>
Public NotInheritable Class Creative
    Private Sub New()
    End Sub

    ''' <summary>內建向量貼圖：最早的 6 種加上 BuiltInStickers 的款式。</summary>
    Public Shared ReadOnly StickerNames As IReadOnlyList(Of (Key As String, Name As String)) =
        {("heart", "愛心"), ("star", "星星"), ("sparkle", "閃亮"), ("bubble", "對話框"), ("arrow", "箭頭"), ("ring", "圓圈")}.
        Concat(BuiltInStickers.Items.Select(Function(i) (i.Key, i.Name))).ToList()

    Public Shared ReadOnly FrameNames As IReadOnlyList(Of String) = {"無", "白邊", "黑邊", "拍立得", "圓角", "底片"}

    '=====================================================================
    ' 局部調整
    '=====================================================================

    ''' <param name="sourceWidth">算圖時（縮放後、轉向前）來源的寬高，用來換算座標與筆刷大小。</param>
    Public Shared Sub ApplyLocal(bmp As Bitmap, recipe As EditRecipe, sourceWidth As Integer, sourceHeight As Integer)
        If recipe.LocalAdjustments Is Nothing Then Return
        For Each adj In recipe.LocalAdjustments
            If Not adj.HasEffect Then Continue For
            Dim mask = BuildLocalMask(adj, recipe, bmp.Width, bmp.Height, sourceWidth, sourceHeight)
            If mask Is Nothing Then Continue For
            Using adjusted = DirectCast(bmp.Clone(), Bitmap)
                ImagePipeline.ApplyTone(adjusted, adj.ToneRecipe())
                Blend(bmp, adjusted, mask)
            End Using
        Next
    End Sub

    ''' <summary>局部調整的遮罩（0..255，每個像素一格）。</summary>
    Public Shared Function BuildLocalMask(adj As LocalAdjustment, recipe As EditRecipe, w As Integer, h As Integer,
                                          sourceWidth As Integer, sourceHeight As Integer) As Byte()
        Dim mask(w * h - 1) As Byte
        If adj.Kind = LocalKind.Gradient Then
            Dim a = ToPixels(GeometryMapper.MapPoint(New PointF(CSng(adj.StartX), CSng(adj.StartY)), recipe, sourceWidth, sourceHeight, True), w, h)
            Dim b = ToPixels(GeometryMapper.MapPoint(New PointF(CSng(adj.EndX), CSng(adj.EndY)), recipe, sourceWidth, sourceHeight, True), w, h)
            Dim vx = b.X - a.X, vy = b.Y - a.Y
            Dim len2 = Math.Max(1.0, vx * vx + vy * vy)
            Parallel.For(0, h,
                Sub(y)
                    For x = 0 To w - 1
                        Dim t = ((x + 0.5 - a.X) * vx + (y + 0.5 - a.Y) * vy) / len2
                        t = Math.Max(0, Math.Min(1, t))
                        mask(y * w + x) = CByte(Math.Round((1 - t * t * (3 - 2 * t)) * 255))
                    Next
                End Sub)
            Return mask
        End If

        If adj.Strokes Is Nothing OrElse adj.Strokes.Count = 0 Then Return Nothing
        ' 輸出像素與縮放後的來源像素同尺度（裁切只切掉、不縮放），只有拉直會放大。
        Dim scale = Math.Max(sourceWidth, sourceHeight) * GeometryMapper.ScaleFactor(recipe, sourceWidth, sourceHeight)
        Dim maxR = 1.0
        Using canvas As New Bitmap(w, h, PixelFormat.Format32bppArgb), g = Graphics.FromImage(canvas)
            g.Clear(Color.Black)
            g.SmoothingMode = SmoothingMode.AntiAlias
            For Each s In adj.Strokes
                Dim r = CSng(Math.Max(1, s.Radius * scale))
                maxR = Math.Max(maxR, r)
                Dim pts = s.Points().Select(Function(p) ToPixels(GeometryMapper.MapPoint(p, recipe, sourceWidth, sourceHeight, True), w, h)).ToArray()
                If pts.Length = 1 Then
                    g.FillEllipse(Brushes.White, pts(0).X - r, pts(0).Y - r, r * 2, r * 2)
                ElseIf pts.Length > 1 Then
                    Using pen As New Pen(Color.White, r * 2) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                        g.DrawLines(pen, pts)
                    End Using
                End If
            Next
            Dim px = Perspective.ReadPixels(canvas)
            For i = 0 To mask.Length - 1
                mask(i) = px(i * 4 + 2)
            Next
        End Using
        ' 筆刷邊緣羽化（半徑的一半）。
        BoxBlur(mask, w, h, 1, Math.Max(1, CInt(maxR * 0.5)))
        Return mask
    End Function

    '=====================================================================
    ' 背景模糊與移軸
    '=====================================================================

    ''' <param name="faces">已轉正原圖的臉（0..1）；沒有時以畫面中央為主體。</param>
    Public Shared Sub ApplyBlurs(bmp As Bitmap, recipe As EditRecipe, faces As IReadOnlyList(Of FaceRegion),
                                 sourceWidth As Integer, sourceHeight As Integer)
        If recipe.BackgroundBlur > 0 Then ApplyBackgroundBlur(bmp, recipe, faces, sourceWidth, sourceHeight)
        If recipe.TiltShift > 0 Then ApplyTiltShift(bmp, recipe)
    End Sub

    Private Shared Sub ApplyBackgroundBlur(bmp As Bitmap, recipe As EditRecipe, faces As IReadOnlyList(Of FaceRegion),
                                           sourceWidth As Integer, sourceHeight As Integer)
        Dim w = bmp.Width, h = bmp.Height
        Dim mask = SubjectMask(recipe, faces, w, h, sourceWidth, sourceHeight)
        ' 主體以外用模糊版本：mask 255 = 清楚。
        For i = 0 To mask.Length - 1
            mask(i) = CByte(255 - mask(i))
        Next
        Dim radius = Math.Max(1, CInt(recipe.BackgroundBlur / 100.0 * 0.022 * Math.Max(w, h)))
        Using blurred = DirectCast(bmp.Clone(), Bitmap)
            BlurBitmap(blurred, radius)
            Blend(bmp, blurred, mask)
        End Using
    End Sub

    ''' <summary>主體遮罩：每張臉推估頭與上半身的橢圓；沒有臉時用畫面中央的橢圓。</summary>
    Public Shared Function SubjectMask(recipe As EditRecipe, faces As IReadOnlyList(Of FaceRegion), w As Integer, h As Integer,
                                       sourceWidth As Integer, sourceHeight As Integer) As Byte()
        Dim mask(w * h - 1) As Byte
        Dim feather As Integer
        Using canvas As New Bitmap(w, h, PixelFormat.Format32bppArgb), g = Graphics.FromImage(canvas)
            g.Clear(Color.Black)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim boxes = If(faces, Array.Empty(Of FaceRegion)()).Select(
                Function(f)
                    Dim corners = {New PointF(f.Box.Left, f.Box.Top), New PointF(f.Box.Right, f.Box.Top),
                                   New PointF(f.Box.Left, f.Box.Bottom), New PointF(f.Box.Right, f.Box.Bottom)}.
                                   Select(Function(c) ToPixels(GeometryMapper.MapPoint(c, recipe, sourceWidth, sourceHeight, True), w, h)).ToArray()
                    Return RectangleF.FromLTRB(corners.Min(Function(c) c.X), corners.Min(Function(c) c.Y),
                                               corners.Max(Function(c) c.X), corners.Max(Function(c) c.Y))
                End Function).Where(Function(b) b.Right > 0 AndAlso b.Bottom > 0 AndAlso b.Left < w AndAlso b.Top < h).ToList()
            If boxes.Count > 0 Then
                For Each b In boxes
                    Dim cx = b.X + b.Width / 2
                    g.FillEllipse(Brushes.White, cx - b.Width * 0.8F, b.Y - b.Height * 0.35F, b.Width * 1.6F, b.Height * 1.6F)
                    g.FillEllipse(Brushes.White, cx - b.Width * 1.7F, b.Y + b.Height * 0.9F, b.Width * 3.4F, b.Height * 4.5F)
                Next
                feather = Math.Max(2, CInt(boxes.Average(Function(b) b.Width) * 0.3))
            Else
                g.FillEllipse(Brushes.White, w * 0.2F, h * 0.12F, w * 0.6F, h * 0.76F)
                feather = Math.Max(2, CInt(Math.Max(w, h) * 0.06))
            End If
            Dim px = Perspective.ReadPixels(canvas)
            For i = 0 To mask.Length - 1
                mask(i) = px(i * 4 + 2)
            Next
        End Using
        BoxBlur(mask, w, h, 1, feather)
        Return mask
    End Function

    ''' <summary>移軸：TiltShiftPosition 處一條清楚的水平帶，上下逐漸模糊，並稍微加強飽和度與對比，像模型。</summary>
    Private Shared Sub ApplyTiltShift(bmp As Bitmap, recipe As EditRecipe)
        Dim w = bmp.Width, h = bmp.Height
        Dim s = recipe.TiltShift / 100.0
        Dim center = recipe.TiltShiftPosition / 100.0 * h
        Dim band = 0.1 * h, ramp = 0.3 * h
        Dim mask(w * h - 1) As Byte
        For y = 0 To h - 1
            Dim t = Math.Max(0, Math.Min(1, (Math.Abs(y + 0.5 - center) - band) / ramp))
            Dim v = CByte(Math.Round(t * t * (3 - 2 * t) * 255))
            For x = 0 To w - 1
                mask(y * w + x) = v
            Next
        Next
        Dim radius = Math.Max(1, CInt(s * 0.018 * Math.Max(w, h)))
        Using blurred = DirectCast(bmp.Clone(), Bitmap)
            BlurBitmap(blurred, radius)
            Blend(bmp, blurred, mask)
        End Using
        ImagePipeline.ApplyTone(bmp, New EditRecipe With {.Saturation = CInt(s * 30), .Contrast = CInt(s * 12)})
    End Sub

    '=====================================================================
    ' 文字與貼圖
    '=====================================================================

    ''' <summary>
    ''' 依序畫上文字與貼圖。每個物件先畫在只涵蓋自身範圍的圖層（底色＋主體），
    ''' 再由主體的不透明度算出陰影與發光，最後依整體不透明度合成到照片上。
    ''' 「壓暗照片」底色直接畫在照片上，搭配「照片本身」填色就是鏤空字。
    ''' </summary>
    Public Shared Sub DrawOverlays(bmp As Bitmap, recipe As EditRecipe)
        If recipe.Overlays Is Nothing OrElse recipe.Overlays.Count = 0 Then Return
        Dim photo As Bitmap = Nothing
        If recipe.Overlays.Any(Function(o) o.Kind = OverlayKind.Text AndAlso o.FillMode = TextFill.Photo) Then
            photo = DirectCast(bmp.Clone(), Bitmap) ' 還沒蓋上任何文字的照片
        End If
        Try
            For Each o In recipe.Overlays
                If o.Visible Then DrawOverlayOnto(bmp, o, photo)
            Next
        Finally
            photo?.Dispose()
        End Try
    End Sub

    ''' <summary>畫上一個文字或貼圖（依它的不透明度與混合模式）。photo 為「照片本身」填字用的照片，可為 Nothing。</summary>
    Public Shared Sub DrawOverlayOnto(bmp As Bitmap, o As Overlay, photo As Bitmap)
        DrawOverlayLayered(bmp, o, bmp.Width, bmp.Height, photo)
    End Sub

    Private Shared Sub DrawOverlayLayered(dst As Bitmap, o As Overlay, w As Integer, h As Integer, photo As Bitmap)
        Dim opacity = Math.Max(0, Math.Min(100, o.Opacity)) / 100.0F
        If opacity <= 0 Then Return
        Dim em = CSng(Math.Max(0.005, o.Size) * h)
        Dim cx = CSng(o.X * w), cy = CSng(o.Y * h)
        Using layout = If(o.Kind = OverlayKind.Text, TextRender.BuildLayout(o, em), Nothing)
            If layout IsNot Nothing AndAlso o.BackgroundStyle = TextBackground.DimPhoto Then
                Using g = Graphics.FromImage(dst), br As New SolidBrush(Color.FromArgb(CInt(o.BackgroundOpacity * 2.55 * opacity), Color.FromArgb(o.BackgroundColorArgb)))
                    g.FillRectangle(br, 0, 0, w, h)
                End Using
            End If

            ' 只處理物件附近的範圍（加上陰影、發光的邊距）。
            Dim shadowOn = o.Shadow AndAlso o.ShadowOpacity > 0
            Dim dist = If(shadowOn, CSng(o.ShadowDistance / 100.0 * em * 0.3), 0)
            Dim blur = If(shadowOn, CSng(o.ShadowBlur / 100.0 * em * 0.3), 0)
            Dim glow = CSng(o.GlowSize / 100.0 * em * 0.5)
            Dim body = BodyBounds(o, layout, em, w, h)
            Dim margin = dist + blur * 2 + glow * 2 + 4
            body.Inflate(margin, margin)
            Dim region = Rectangle.Intersect(Rectangle.FromLTRB(CInt(Math.Floor(body.Left)), CInt(Math.Floor(body.Top)),
                                                                CInt(Math.Ceiling(body.Right)), CInt(Math.Ceiling(body.Bottom))),
                                             New Rectangle(0, 0, w, h))
            If region.Width <= 0 OrElse region.Height <= 0 Then Return

            Using layer As New Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb)
                Using gl = Graphics.FromImage(layer)
                    gl.SmoothingMode = SmoothingMode.AntiAlias
                    gl.InterpolationMode = InterpolationMode.HighQualityBicubic
                    gl.PixelOffsetMode = PixelOffsetMode.HighQuality
                    gl.TranslateTransform(-region.X, -region.Y)
                    If layout IsNot Nothing Then
                        gl.TranslateTransform(cx, cy)
                        gl.RotateTransform(CSng(o.Rotation))
                        Dim photoToLocal As Matrix = Nothing
                        If photo IsNot Nothing Then
                            ' 照片座標 → 圖層座標 → 文字座標，讓照片像素對齊原位。
                            Using inv = gl.Transform
                                inv.Invert()
                                photoToLocal = New Matrix()
                                photoToLocal.Translate(-region.X, -region.Y)
                                photoToLocal.Multiply(inv, MatrixOrder.Append)
                            End Using
                        End If
                        TextRender.DrawBody(gl, o, layout, photo, photoToLocal, -cx, w - cx)
                        photoToLocal?.Dispose()
                    Else
                        DrawOverlay(gl, o, w, h)
                    End If
                End Using

                Using final As New Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb)
                    Using gf = Graphics.FromImage(final)
                        Dim px = Perspective.ReadPixels(layer)
                        Dim full = New Rectangle(0, 0, region.Width, region.Height)
                        If shadowOn Then
                            Dim a = o.ShadowAngle * Math.PI / 180
                            Using sh = AlphaToColor(px, region.Width, region.Height, CInt(blur), Color.FromArgb(o.ShadowColorArgb), o.ShadowOpacity / 100.0, 1)
                                Dim offset = New Point(CInt(Math.Round(Math.Cos(a) * dist)), CInt(Math.Round(Math.Sin(a) * dist)))
                                gf.DrawImage(sh, New Rectangle(offset, full.Size))
                            End Using
                        End If
                        If glow >= 1 Then
                            ' 兩層光暈：外層柔和擴散但淡，內層貼著字形且亮；只用一層加強會在字與字之間填成一整塊。
                            Using outer = AlphaToColor(px, region.Width, region.Height, CInt(glow), Color.FromArgb(o.GlowColorArgb), 0.85, 1.3),
                                  inner = AlphaToColor(px, region.Width, region.Height, Math.Max(1, CInt(glow * 0.3)), Color.FromArgb(o.GlowColorArgb), 1, 2.2)
                                gf.DrawImage(outer, full)
                                gf.DrawImage(inner, full)
                            End Using
                        End If
                        gf.DrawImage(layer, full)
                    End Using
                    LayerBlend.Composite(dst, final, region, opacity, o.Blend)
                End Using
            End Using
        End Using
    End Sub

    ''' <summary>由圖層的不透明度做出單色剪影（可模糊、可加強），陰影與發光用。</summary>
    Private Shared Function AlphaToColor(px As Byte(), w As Integer, h As Integer, blurRadius As Integer, color As Color,
                                         opacity As Double, boost As Double) As Bitmap
        Dim a(w * h - 1) As Byte
        For i = 0 To a.Length - 1
            a(i) = px(i * 4 + 3)
        Next
        If blurRadius > 0 Then BoxBlur(a, w, h, 1, blurRadius)
        Dim outPx(w * h * 4 - 1) As Byte
        For i = 0 To a.Length - 1
            Dim alpha = Math.Min(255.0, a(i) * boost) * opacity
            If alpha <= 0 Then Continue For
            outPx(i * 4) = color.B
            outPx(i * 4 + 1) = color.G
            outPx(i * 4 + 2) = color.R
            outPx(i * 4 + 3) = CByte(Math.Min(255, Math.Round(alpha)))
        Next
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Perspective.WritePixels(bmp, outPx)
        Return bmp
    End Function

    ''' <summary>物件主體（含外框、立體、底色）在照片上的外接矩形。</summary>
    Private Shared Function BodyBounds(o As Overlay, layout As TextRender.TextLayout, em As Single, w As Integer, h As Integer) As RectangleF
        If layout Is Nothing Then
            Using path = OverlayPath(o, w, h)
                Dim b = path.GetBounds()
                b.Inflate(em * 0.08F, em * 0.08F)
                Return b
            End Using
        End If
        Dim cx = CSng(o.X * w), cy = CSng(o.Y * h)
        Dim r = TextLocalBounds(o, layout, includeBar:=True, barLeft:=-cx, barRight:=w - cx)
        Dim ex = TextRender.ExtrudePx(o, em)
        r = RectangleF.FromLTRB(r.Left - ex, r.Top - ex, r.Right + ex, r.Bottom + ex)
        Dim pts = {New PointF(r.Left, r.Top), New PointF(r.Right, r.Top), New PointF(r.Right, r.Bottom), New PointF(r.Left, r.Bottom)}
        Using m As New Matrix()
            m.Translate(cx, cy)
            m.Rotate(CSng(o.Rotation))
            m.TransformPoints(pts)
        End Using
        Return RectangleF.FromLTRB(pts.Min(Function(p) p.X), pts.Min(Function(p) p.Y), pts.Max(Function(p) p.X), pts.Max(Function(p) p.Y))
    End Function

    ''' <summary>文字的區域座標範圍：字＋外框，有方塊或印章框底色時含底色；字幕條只在 includeBar 時納入。</summary>
    Private Shared Function TextLocalBounds(o As Overlay, layout As TextRender.TextLayout, includeBar As Boolean,
                                            barLeft As Single, barRight As Single) As RectangleF
        Dim r = layout.Bounds
        Dim ol = TextRender.OutlinePx(o, layout.Em) + layout.Em * 0.05F
        r.Inflate(ol, ol)
        Select Case o.BackgroundStyle
            Case TextBackground.Box, TextBackground.Frame
                r = RectangleF.Union(r, TextRender.BackgroundRect(o, layout, barLeft, barRight))
            Case TextBackground.Bar
                Dim bar = TextRender.BackgroundRect(o, layout, barLeft, barRight)
                If includeBar Then
                    r = RectangleF.Union(r, bar)
                Else
                    r = RectangleF.FromLTRB(r.Left, Math.Min(r.Top, bar.Top), r.Right, Math.Max(r.Bottom, bar.Bottom))
                End If
        End Select
        Return r
    End Function

    ''' <summary>文字或貼圖在輸出照片上的範圍（0..1），畫布點選用。</summary>
    Public Shared Function OverlayBounds(o As Overlay, w As Integer, h As Integer) As RectangleF
        Using path = OverlayPath(o, w, h)
            Dim b = path.GetBounds()
            Dim pad = CSng(o.Size * h * 0.1)
            b.Inflate(pad, pad)
            Return New RectangleF(b.X / w, b.Y / h, b.Width / w, b.Height / h)
        End Using
    End Function

    ''' <summary>向量貼圖的主體（陰影、發光、透明度由 DrawOverlayLayered 處理）。</summary>
    Private Shared Sub DrawOverlay(g As Graphics, o As Overlay, w As Integer, h As Integer)
        If o.Kind = OverlayKind.Image Then
            DrawImageOverlay(g, o, w, h)
            Return
        End If
        Using path = OverlayPath(o, w, h)
            Dim size = CSng(o.Size * h)
            Dim color = Drawing.Color.FromArgb(o.ColorArgb)
            If o.Kind = OverlayKind.Sticker AndAlso o.Sticker = "bubble" Then
                ' 對話框：白底、彩色外框。
                g.FillPath(Brushes.White, path)
                Using pen As New Pen(color, Math.Max(1.5F, size * 0.05F))
                    g.DrawPath(pen, path)
                End Using
            ElseIf o.Kind = OverlayKind.Sticker AndAlso o.Sticker = "ring" Then
                Using pen As New Pen(color, Math.Max(2.0F, size * 0.09F))
                    g.DrawPath(pen, path)
                End Using
            Else
                Using br As New SolidBrush(color)
                    g.FillPath(br, path)
                End Using
            End If
        End Using
    End Sub

    ''' <summary>圖片貼圖：高度 = Size × 照片高、寬度依圖片比例。圖檔不見時不畫。</summary>
    Private Shared Sub DrawImageOverlay(g As Graphics, o As Overlay, w As Integer, h As Integer)
        Dim img = StickerLibrary.GetImage(o.ImagePath)
        If img Is Nothing Then Return
        Dim r = ImageRect(o, h, img)
        Dim state = g.Save()
        Try
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.TranslateTransform(CSng(o.X * w), CSng(o.Y * h))
            g.RotateTransform(CSng(o.Rotation))
            SyncLock img
                g.DrawImage(img, r)
            End SyncLock
        Finally
            g.Restore(state)
        End Try
    End Sub

    ''' <summary>圖片貼圖以中心為原點的矩形（像素）。</summary>
    Private Shared Function ImageRect(o As Overlay, h As Integer, img As Image) As RectangleF
        Dim ph = CSng(Math.Max(0.005, o.Size) * h)
        Dim pw = If(img Is Nothing OrElse img.Height = 0, ph, ph * img.Width / CSng(img.Height))
        Return New RectangleF(-pw / 2, -ph / 2, pw, ph)
    End Function

    ''' <summary>貼圖面板用的預覽圖（size × size，透明底）。向量貼圖照預設顏色畫；圖片貼圖等比縮放置中。</summary>
    Public Shared Function StickerPreview(o As Overlay, size As Integer) As Bitmap
        Dim bmp As New Bitmap(size, size, PixelFormat.Format32bppArgb)
        Dim preview = o.Clone()
        preview.X = 0.5 : preview.Y = 0.5 : preview.Rotation = 0 : preview.Shadow = False
        preview.Size = If(o.Kind = OverlayKind.Image, 0.86, 0.78)
        If o.Kind = OverlayKind.Image Then
            Dim img = StickerLibrary.GetImage(o.ImagePath)
            If img IsNot Nothing AndAlso img.Width > img.Height Then preview.Size = 0.86 * img.Height / img.Width
        End If
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            DrawOverlay(g, preview, size, size)
        End Using
        Return bmp
    End Function

    ''' <summary>文字樣式預設集的預覽：在 background 上以該樣式畫出 sample，自動縮放到放得下。</summary>
    Public Shared Function TextPreview(style As Overlay, sample As String, background As Bitmap) As Bitmap
        Dim bmp = DirectCast(background.Clone(), Bitmap)
        Dim o = style.Clone()
        o.Kind = OverlayKind.Text : o.Text = sample : o.X = 0.5 : o.Y = 0.5 : o.Rotation = 0 : o.Size = 0.5
        For i = 1 To 8
            Dim f = OverlayFrame(o, bmp.Width, bmp.Height)
            Dim fw = (f.Right - f.Left) * bmp.Width, fh = (f.Bottom - f.Top) * bmp.Height
            Dim s = Math.Min(bmp.Width * 0.86 / fw, bmp.Height * 0.86 / fh)
            If s >= 0.98 Then Exit For
            o.Size *= s
        Next
        DrawOverlays(bmp, New EditRecipe With {.Overlays = New List(Of Overlay) From {o}})
        Return bmp
    End Function

    ''' <summary>以 (X, Y) 為中心、轉了 Rotation 度的路徑。</summary>
    Private Shared Function OverlayPath(o As Overlay, w As Integer, h As Integer) As GraphicsPath
        Dim path = UnrotatedPath(o, w, h)
        If Math.Abs(o.Rotation) > 0.01 Then
            Using m As New Matrix()
                m.RotateAt(CSng(o.Rotation), New PointF(CSng(o.X * w), CSng(o.Y * h)))
                path.Transform(m)
            End Using
        End If
        Return path
    End Function

    ''' <summary>
    ''' 選取框：以 (X, Y) 為旋轉中心，未旋轉時的外框（相對中心的像素，再除以照片寬高），加上旋轉角度。
    ''' 畫布用來畫控制點與判斷點選。文字的框含外框與方塊底色（字幕條只算文字那一段）。
    ''' </summary>
    Public Shared Function OverlayFrame(o As Overlay, w As Integer, h As Integer) As OverlayFrameInfo
        Dim b As RectangleF
        If o.Kind = OverlayKind.Text Then
            Dim em = CSng(Math.Max(0.005, o.Size) * h)
            Using layout = TextRender.BuildLayout(o, em)
                b = TextLocalBounds(o, layout, includeBar:=False, barLeft:=CSng(-o.X * w), barRight:=CSng((1 - o.X) * w))
            End Using
            Return New OverlayFrameInfo With {
                .PivotX = CSng(o.X), .PivotY = CSng(o.Y),
                .Left = b.Left / w, .Top = b.Top / h, .Right = b.Right / w, .Bottom = b.Bottom / h,
                .Rotation = CSng(o.Rotation)}
        End If
        Using path = UnrotatedPath(o, w, h)
            b = path.GetBounds()
            Dim pad = CSng(o.Size * h * 0.08)
            b.Inflate(pad, pad)
            Dim cx = CSng(o.X * w), cy = CSng(o.Y * h)
            Return New OverlayFrameInfo With {
                .PivotX = CSng(o.X), .PivotY = CSng(o.Y),
                .Left = (b.Left - cx) / w, .Top = (b.Top - cy) / h, .Right = (b.Right - cx) / w, .Bottom = (b.Bottom - cy) / h,
                .Rotation = CSng(o.Rotation)}
        End Using
    End Function

    ''' <summary>以 (X, Y) 為中心、未旋轉的路徑；文字高度約為 Size × 照片高。</summary>
    Private Shared Function UnrotatedPath(o As Overlay, w As Integer, h As Integer) As GraphicsPath
        Dim size = CSng(Math.Max(0.005, o.Size) * h)
        Dim cx = CSng(o.X * w), cy = CSng(o.Y * h)
        Dim path As New GraphicsPath()
        If o.Kind = OverlayKind.Image Then
            Dim r = ImageRect(o, h, StickerLibrary.GetImage(o.ImagePath))
            r.Offset(cx, cy)
            path.AddRectangle(r)
            Return path
        End If
        If o.Kind = OverlayKind.Text Then
            Using layout = TextRender.BuildLayout(o, size)
                path.AddPath(layout.Path, False)
            End Using
            Using m As New Matrix()
                m.Translate(cx, cy)
                path.Transform(m)
            End Using
            Return path
        End If

        Dim s = size ' 貼圖寬高約為 size
        Dim designed = BuiltInStickers.Build(o.Sticker, cx, cy, s)
        If designed IsNot Nothing Then
            path.Dispose()
            Return designed
        End If
        Select Case o.Sticker
            Case "heart"
                path.AddBezier(cx, cy + s * 0.45F, cx - s * 0.75F, cy - s * 0.05F, cx - s * 0.45F, cy - s * 0.65F, cx, cy - s * 0.25F)
                path.AddBezier(cx, cy - s * 0.25F, cx + s * 0.45F, cy - s * 0.65F, cx + s * 0.75F, cy - s * 0.05F, cx, cy + s * 0.45F)
                path.CloseFigure()
            Case "star", "sparkle"
                Dim points = If(o.Sticker = "star", 5, 4)
                Dim inner = If(o.Sticker = "star", 0.4, 0.22)
                Dim pts As New List(Of PointF)()
                For i = 0 To points * 2 - 1
                    Dim r = If(i Mod 2 = 0, s * 0.5, s * 0.5 * inner)
                    Dim a = -Math.PI / 2 + i * Math.PI / points
                    pts.Add(New PointF(CSng(cx + r * Math.Cos(a)), CSng(cy + r * Math.Sin(a))))
                Next
                path.AddPolygon(pts.ToArray())
            Case "bubble"
                Dim bw = s * 1.4F, bh = s * 0.85F, rad = s * 0.22F
                Dim l = cx - bw / 2, t = cy - bh / 2 - s * 0.1F
                path.AddArc(l, t, rad * 2, rad * 2, 180, 90)
                path.AddArc(l + bw - rad * 2, t, rad * 2, rad * 2, 270, 90)
                path.AddArc(l + bw - rad * 2, t + bh - rad * 2, rad * 2, rad * 2, 0, 90)
                path.AddLine(l + bw * 0.45F, t + bh, l + bw * 0.25F, t + bh + s * 0.3F)
                path.AddLine(l + bw * 0.25F, t + bh + s * 0.3F, l + bw * 0.28F, t + bh)
                path.AddArc(l, t + bh - rad * 2, rad * 2, rad * 2, 90, 90)
                path.CloseFigure()
            Case "arrow"
                path.AddPolygon({New PointF(cx - s * 0.6F, cy - s * 0.12F), New PointF(cx + s * 0.1F, cy - s * 0.12F),
                                 New PointF(cx + s * 0.1F, cy - s * 0.32F), New PointF(cx + s * 0.6F, cy),
                                 New PointF(cx + s * 0.1F, cy + s * 0.32F), New PointF(cx + s * 0.1F, cy + s * 0.12F),
                                 New PointF(cx - s * 0.6F, cy + s * 0.12F)})
            Case Else ' ring
                path.AddEllipse(cx - s * 0.5F, cy - s * 0.5F, s, s)
        End Select
        Return path
    End Function

    '=====================================================================
    ' 邊框
    '=====================================================================

    ''' <summary>加上邊框後的新圖；沒有邊框時回傳 Nothing。</summary>
    ''' <summary>邊框四邊的寬度（像素）；沒有邊框時全部為 0。畫布用來換算照片與畫面座標。</summary>
    Public Shared Function FrameMargins(recipe As EditRecipe, w As Integer, h As Integer) As (Left As Integer, Top As Integer, Right As Integer, Bottom As Integer)
        If recipe.Frame = PhotoFrameStyle.None Then Return (0, 0, 0, 0)
        Dim b = Math.Max(2, CInt(Math.Min(w, h) * (0.01 + recipe.FrameSize / 100.0 * 0.07)))
        Select Case recipe.Frame
            Case PhotoFrameStyle.Polaroid : Return (b, b, b, b * 4)
            Case PhotoFrameStyle.Rounded : Return (b \ 2, b \ 2, b \ 2, b \ 2)
            Case PhotoFrameStyle.Film
                Dim side = Math.Max(2, b \ 3), tb = CInt(b * 1.8)
                Return (side, tb, side, tb)
            Case Else : Return (b, b, b, b)
        End Select
    End Function

    Public Shared Function ApplyFrame(bmp As Bitmap, recipe As EditRecipe) As Bitmap
        If recipe.Frame = PhotoFrameStyle.None Then Return Nothing
        Dim w = bmp.Width, h = bmp.Height
        Dim m = FrameMargins(recipe, w, h)
        Dim l = m.Left, t = m.Top, r = m.Right, btm = m.Bottom
        Dim bg = Color.White
        Select Case recipe.Frame
            Case PhotoFrameStyle.Black : bg = Color.Black
            Case PhotoFrameStyle.Polaroid : bg = Color.FromArgb(250, 249, 245)
            Case PhotoFrameStyle.Film : bg = Color.FromArgb(18, 18, 18)
        End Select

        Dim result As New Bitmap(w + l + r, h + t + btm, PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(result)
            g.Clear(bg)
            g.SmoothingMode = SmoothingMode.AntiAlias
            If recipe.Frame = PhotoFrameStyle.Rounded Then
                Dim rad = Math.Max(4, CInt(Math.Min(w, h) * (0.02 + recipe.FrameSize / 100.0 * 0.06)))
                Using path = RoundedRect(New RectangleF(l, t, w, h), rad), tb As New TextureBrush(bmp)
                    tb.TranslateTransform(l, t)
                    g.FillPath(tb, path)
                End Using
            Else
                g.DrawImageUnscaled(bmp, l, t)
            End If
            If recipe.Frame = PhotoFrameStyle.Film Then
                ' 底片齒孔。
                Dim holeH = t * 0.42F, holeW = holeH * 1.35F, gap = holeW * 0.9F
                Using br As New SolidBrush(Color.FromArgb(225, 225, 220))
                    Dim x = gap / 2
                    While x + holeW < result.Width
                        For Each y In {(t - holeH) / 2, h + t + (btm - holeH) / 2}
                            Using hole = RoundedRect(New RectangleF(x, y, holeW, holeH), holeH * 0.25F)
                                g.FillPath(br, hole)
                            End Using
                        Next
                        x += holeW + gap
                    End While
                End Using
            End If
        End Using
        Return result
    End Function

    Private Shared Function RoundedRect(r As RectangleF, radius As Single) As GraphicsPath
        Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height))
        Dim p As New GraphicsPath()
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    '=====================================================================
    ' 共用：模糊與混合
    '=====================================================================

    Private Shared Function ToPixels(p As PointF, w As Integer, h As Integer) As PointF
        Return New PointF(p.X * w, p.Y * h)
    End Function

    ''' <summary>dst = dst + (src - dst) × mask/255。</summary>
    Public Shared Sub Blend(dst As Bitmap, src As Bitmap, mask As Byte())
        Dim w = dst.Width, h = dst.Height
        Dim a = Perspective.ReadPixels(dst), b = Perspective.ReadPixels(src)
        Parallel.For(0, h,
            Sub(y)
                For x = 0 To w - 1
                    Dim m = CInt(mask(y * w + x))
                    If m = 0 Then Continue For
                    Dim i = (y * w + x) * 4
                    For ch = 0 To 2
                        a(i + ch) = CByte((CInt(a(i + ch)) * (255 - m) + CInt(b(i + ch)) * m + 127) \ 255)
                    Next
                Next
            End Sub)
        Perspective.WritePixels(dst, a)
    End Sub

    Public Shared Sub BlurBitmap(bmp As Bitmap, radius As Integer)
        Dim px = Perspective.ReadPixels(bmp)
        BoxBlur(px, bmp.Width, bmp.Height, 4, radius)
        Perspective.WritePixels(bmp, px)
    End Sub

    ''' <summary>三次方框模糊（近似高斯），只處理前 3 個通道（BGRA 的 alpha 不動）。</summary>
    Public Shared Sub BoxBlur(data As Byte(), w As Integer, h As Integer, channels As Integer, radius As Integer)
        If radius < 1 Then Return
        Dim r = Math.Max(1, CInt(Math.Round(radius / Math.Sqrt(3))))
        Dim chs = Math.Min(3, channels)
        For pass = 1 To 3
            ' 水平
            Parallel.For(0, h,
                Sub(y)
                    Dim line(w * chs - 1) As Integer
                    For ch = 0 To chs - 1
                        Dim sum = 0
                        For k = -r To r
                            sum += data((y * w + Math.Max(0, Math.Min(w - 1, k))) * channels + ch)
                        Next
                        For x = 0 To w - 1
                            line(x * chs + ch) = sum
                            Dim addX = Math.Min(w - 1, x + r + 1), subX = Math.Max(0, x - r)
                            sum += CInt(data((y * w + addX) * channels + ch)) - data((y * w + subX) * channels + ch)
                        Next
                    Next
                    Dim n = 2 * r + 1
                    For x = 0 To w - 1
                        For ch = 0 To chs - 1
                            data((y * w + x) * channels + ch) = CByte(line(x * chs + ch) \ n)
                        Next
                    Next
                End Sub)
            ' 垂直
            Parallel.For(0, w,
                Sub(x)
                    Dim col(h * chs - 1) As Integer
                    For ch = 0 To chs - 1
                        Dim sum = 0
                        For k = -r To r
                            sum += data((Math.Max(0, Math.Min(h - 1, k)) * w + x) * channels + ch)
                        Next
                        For y = 0 To h - 1
                            col(y * chs + ch) = sum
                            Dim addY = Math.Min(h - 1, y + r + 1), subY = Math.Max(0, y - r)
                            sum += CInt(data((addY * w + x) * channels + ch)) - data((subY * w + x) * channels + ch)
                        Next
                    Next
                    Dim n = 2 * r + 1
                    For y = 0 To h - 1
                        For ch = 0 To chs - 1
                            data((y * w + x) * channels + ch) = CByte(col(y * chs + ch) \ n)
                        Next
                    Next
                End Sub)
        Next
    End Sub
End Class
