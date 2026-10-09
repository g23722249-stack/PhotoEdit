Imports System.Drawing

''' <summary>偵測到的一張臉，座標為「已轉正原圖」寬高的 0..1 比例，與預覽圖或原圖大小無關。</summary>
Public Class FaceRegion
    Public Property Box As RectangleF
    ''' <summary>右眼、左眼、鼻尖、右嘴角、左嘴角（YuNet 順序）。</summary>
    Public Property Landmarks As PointF() = New PointF(4) {}
    Public Property Score As Single
    ''' <summary>68 點特徵點（0..1，FaceLandmarks 算的）；沒有模型時 Nothing。下顎 0–16、眉 17–26、鼻 27–35、眼 36–47、嘴唇外 48–59、內 60–67。</summary>
    Public Property Dense As PointF()
End Class

''' <summary>
''' 已轉正原圖座標與畫面座標（0..1）之間的換算，順序與 ImagePipeline 相同：
''' 旋轉/翻轉 → 透視 → 拉直 →（可選）裁切。
''' </summary>
Public NotInheritable Class GeometryMapper
    Private Sub New()
    End Sub

    ''' <summary>原圖點 → 裁切前（applyCrop:=True 時為裁切後）的畫面點。</summary>
    Public Shared Function MapPoint(p As PointF, recipe As EditRecipe, sourceWidth As Integer, sourceHeight As Integer,
                                    Optional applyCrop As Boolean = False) As PointF
        Dim w As Double = sourceWidth, h As Double = sourceHeight
        Dim x As Double = p.X, y As Double = p.Y

        ' 1. 旋轉（順時針）再左右翻轉，與 ImagePipeline.ToRotateFlipType 相同。
        Dim rft = CInt(ImagePipeline.ToRotateFlipType(recipe.Rotation, recipe.FlipHorizontal, recipe.FlipVertical))
        Select Case rft Mod 4
            Case 1 : Dim t = x : x = 1 - y : y = t : Swap(w, h)
            Case 2 : x = 1 - x : y = 1 - y
            Case 3 : Dim t = x : x = y : y = 1 - t : Swap(w, h)
        End Select
        If rft >= 4 Then x = 1 - x

        ' 2. 透視。
        Dim q = Perspective.Warp(New PointF(CSng(x), CSng(y)), recipe.PerspectiveVertical, recipe.PerspectiveHorizontal)
        x = q.X : y = q.Y

        ' 3. 拉直：以中心旋轉並放大（與 GDI+ RotateTransform 相同方向）。
        If Math.Abs(recipe.Straighten) > 0.001 Then
            Dim s = ImagePipeline.StraightenScale(CInt(w), CInt(h), recipe.Straighten)
            Dim a = recipe.Straighten * Math.PI / 180
            Dim px = (x - 0.5) * w, py = (y - 0.5) * h
            x = (px * Math.Cos(a) - py * Math.Sin(a)) * s / w + 0.5
            y = (px * Math.Sin(a) + py * Math.Cos(a)) * s / h + 0.5
        End If

        ' 4. 裁切。
        If applyCrop AndAlso recipe.Crop IsNot Nothing AndAlso Not recipe.Crop.IsFull Then
            Dim c = recipe.Crop.Normalized()
            x = (x - c.X) / c.Width
            y = (y - c.Y) / c.Height
        End If
        Return New PointF(CSng(x), CSng(y))
    End Function

    ''' <summary>畫面點 → 原圖點（MapPoint 的反函數）。fromCropped 表示 p 是裁切後畫面上的點。</summary>
    Public Shared Function UnmapPoint(p As PointF, recipe As EditRecipe, sourceWidth As Integer, sourceHeight As Integer,
                                      Optional fromCropped As Boolean = False) As PointF
        Dim x As Double = p.X, y As Double = p.Y
        Dim rft = CInt(ImagePipeline.ToRotateFlipType(recipe.Rotation, recipe.FlipHorizontal, recipe.FlipVertical))
        Dim w As Double = sourceWidth, h As Double = sourceHeight
        If rft Mod 2 = 1 Then Swap(w, h) ' 轉 90/270 度後的寬高

        If fromCropped AndAlso recipe.Crop IsNot Nothing AndAlso Not recipe.Crop.IsFull Then
            Dim c = recipe.Crop.Normalized()
            x = c.X + x * c.Width
            y = c.Y + y * c.Height
        End If

        If Math.Abs(recipe.Straighten) > 0.001 Then
            Dim s = ImagePipeline.StraightenScale(CInt(w), CInt(h), recipe.Straighten)
            Dim a = recipe.Straighten * Math.PI / 180
            Dim px = (x - 0.5) * w / s, py = (y - 0.5) * h / s
            x = (px * Math.Cos(a) + py * Math.Sin(a)) / w + 0.5
            y = (-px * Math.Sin(a) + py * Math.Cos(a)) / h + 0.5
        End If

        Dim q = Perspective.Unwarp(New PointF(CSng(x), CSng(y)), recipe.PerspectiveVertical, recipe.PerspectiveHorizontal)
        x = q.X : y = q.Y

        If rft >= 4 Then x = 1 - x
        Select Case rft Mod 4
            Case 1 : Dim t = x : x = y : y = 1 - t
            Case 2 : x = 1 - x : y = 1 - y
            Case 3 : Dim t = x : x = 1 - y : y = t
        End Select
        Return New PointF(CSng(x), CSng(y))
    End Function

    ''' <summary>
    ''' 原圖上一段長度（以原圖長邊為 1）在畫面上約等於幾個「原圖像素」：只有拉直會放大。
    ''' 用來把畫面上的筆刷大小換成原圖比例。
    ''' </summary>
    Public Shared Function ScaleFactor(recipe As EditRecipe, sourceWidth As Integer, sourceHeight As Integer) As Double
        If Math.Abs(recipe.Straighten) <= 0.001 Then Return 1
        Dim rft = CInt(ImagePipeline.ToRotateFlipType(recipe.Rotation, recipe.FlipHorizontal, recipe.FlipVertical))
        Dim w = sourceWidth, h = sourceHeight
        If rft Mod 2 = 1 Then
            Dim t = w : w = h : h = t
        End If
        Return ImagePipeline.StraightenScale(w, h, recipe.Straighten)
    End Function

    ''' <summary>臉框四角換算後的外接矩形。</summary>
    Public Shared Function MapBox(box As RectangleF, recipe As EditRecipe, sourceWidth As Integer, sourceHeight As Integer) As RectangleF
        Dim corners = {New PointF(box.Left, box.Top), New PointF(box.Right, box.Top),
                       New PointF(box.Left, box.Bottom), New PointF(box.Right, box.Bottom)}
        Dim mapped = corners.Select(Function(c) MapPoint(c, recipe, sourceWidth, sourceHeight)).ToArray()
        Dim l = mapped.Min(Function(m) m.X), t = mapped.Min(Function(m) m.Y)
        Return RectangleF.FromLTRB(l, t, mapped.Max(Function(m) m.X), mapped.Max(Function(m) m.Y))
    End Function

    Private Shared Sub Swap(ByRef a As Double, ByRef b As Double)
        Dim t = a : a = b : b = t
    End Sub
End Class

''' <summary>依臉的位置建議裁切框：臉群水平置中、眼睛落在上方三分線，並保證臉都在框內。</summary>
Public NotInheritable Class SmartCrop
    Private Sub New()
    End Sub

    ''' <param name="faces">臉框，座標為裁切前影像的 0..1（見 GeometryMapper.MapBox）。</param>
    ''' <param name="aspectRatio">裁切框寬高比（像素）；0 表示沿用影像比例。</param>
    ''' <param name="sizeFactor">裁切框相對於最大可能尺寸的比例（0.5..1）；臉放不下時會自動放大。</param>
    Public Shared Function Suggest(faces As IEnumerable(Of RectangleF), aspectRatio As Double,
                                   imageWidth As Integer, imageHeight As Integer,
                                   Optional sizeFactor As Double = 1) As CropRect
        If aspectRatio <= 0 Then aspectRatio = imageWidth / CDbl(imageHeight)
        Dim full = CropRect.CenteredForAspect(aspectRatio, imageWidth, imageHeight)
        Dim list = faces?.Where(Function(f) f.Width > 0 AndAlso f.Height > 0).ToList()
        If list Is Nothing OrElse list.Count = 0 Then
            Return Scaled(full, sizeFactor)
        End If

        ' 臉群外接框，四周各留半張臉的空間。
        Dim gl = list.Min(Function(f) f.Left), gt = list.Min(Function(f) f.Top)
        Dim gr = list.Max(Function(f) f.Right), gb = list.Max(Function(f) f.Bottom)
        Dim faceW = list.Average(Function(f) f.Width), faceH = list.Average(Function(f) f.Height)
        Dim needW = (gr - gl) + faceW, needH = (gb - gt) + faceH

        Dim factor = Math.Max(0.3, Math.Min(1, sizeFactor))
        factor = Math.Max(factor, Math.Min(1, Math.Max(needW / full.Width, needH / full.Height)))
        Dim w = full.Width * factor, h = full.Height * factor

        ' 眼睛大約在臉框上緣往下 40%。
        Dim cx = (gl + gr) / 2
        Dim eyeY = gt + faceH * 0.4
        Dim x = cx - w / 2
        Dim y = eyeY - h / 3
        ' 確保臉群在框內（框比臉群大時才有意義），再夾回影像範圍。
        x = Math.Min(Math.Max(x, gr + faceW / 2 - w), gl - faceW / 2)
        y = Math.Min(Math.Max(y, gb + faceH / 2 - h), gt - faceH / 2)
        x = Math.Max(0, Math.Min(1 - w, x))
        y = Math.Max(0, Math.Min(1 - h, y))
        Return New CropRect(x, y, w, h)
    End Function

    Private Shared Function Scaled(c As CropRect, factor As Double) As CropRect
        Dim f = Math.Max(0.3, Math.Min(1, factor))
        Dim w = c.Width * f, h = c.Height * f
        Return New CropRect(c.X + (c.Width - w) / 2, c.Y + (c.Height - h) / 2, w, h)
    End Function
End Class
