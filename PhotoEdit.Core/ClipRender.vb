Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>
''' PowerClip 與「照片放進形狀」的合成：
''' PowerClip 群組（DrawGroup.ClipFrame）裡，框是最下面那個封閉圖形，其他成員先畫在暫存圖上，再用框的範圍（反鋸齒）合回去；
''' 框的填色畫在內容下面、外框畫在內容上面（同 CorelDRAW）。
''' 照片放進形狀（EditRecipe.PhotoClip）：疊圖層之前，形狀外面的照片變透明。
''' </summary>
Public NotInheritable Class ClipRender
    Private Sub New()
    End Sub

    ''' <summary>圖層所在的 PowerClip（最近的一層）；不在 PowerClip 裡、或自己就是框時回傳 Nothing。</summary>
    Public Shared Function ClipOf(recipe As EditRecipe, d As DrawLayer) As (Group As DrawGroup, Frame As DrawLayer)?
        If d Is Nothing OrElse recipe.DrawGroups Is Nothing OrElse recipe.Drawings Is Nothing Then Return Nothing
        Dim g = DrawGroups.Find(recipe, d.GroupId)
        Dim guard = 0
        While g IsNot Nothing AndAlso guard < 64
            If g.ClipFrame IsNot Nothing Then
                Dim id = g.ClipFrame
                Dim f = recipe.Drawings.FirstOrDefault(Function(x) x.Id = id)
                If f IsNot Nothing AndAlso f.GroupId = g.Id Then
                    If f Is d Then Return Nothing
                    Return (g, f)
                End If
            End If
            g = DrawGroups.Find(recipe, g.ParentId)
            guard += 1
        End While
        Return Nothing
    End Function

    ''' <summary>這個圖層是某個 PowerClip 的框。</summary>
    Public Shared Function IsFrame(recipe As EditRecipe, d As DrawLayer) As Boolean
        If d Is Nothing OrElse d.Id Is Nothing OrElse recipe.DrawGroups Is Nothing Then Return False
        Dim g = DrawGroups.Find(recipe, d.GroupId)
        Return g IsNot Nothing AndAlso g.ClipFrame = d.Id
    End Function

    ''' <summary>封閉圖形的範圍（反鋸齒，0..255），只算 bounds 裡面；bounds 是空的表示沒有範圍。</summary>
    Friend Shared Function Mask(frame As DrawLayer, w As Integer, h As Integer, ByRef bounds As Rectangle) As Byte()
        Dim rings = PathBoolean.RegionOf(frame)
        bounds = Rectangle.Empty
        If rings.Count = 0 Then Return Nothing
        Dim scale = CSng(h)
        Dim pts = rings.SelectMany(Function(r) r).ToList()
        Dim b = Rectangle.FromLTRB(CInt(Math.Floor(pts.Min(Function(p) p.X) * scale)) - 1, CInt(Math.Floor(pts.Min(Function(p) p.Y) * scale)) - 1,
                                   CInt(Math.Ceiling(pts.Max(Function(p) p.X) * scale)) + 1, CInt(Math.Ceiling(pts.Max(Function(p) p.Y) * scale)) + 1)
        b.Intersect(New Rectangle(0, 0, w, h))
        If b.Width <= 0 OrElse b.Height <= 0 Then Return Nothing
        bounds = b
        Using bmp As New Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp), path As New GraphicsPath(FillMode.Winding)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.TranslateTransform(-b.X, -b.Y)
                For Each r In rings
                    path.AddPolygon(r.Select(Function(p) New PointF(p.X * scale, p.Y * scale)).ToArray())
                Next
                g.FillPath(Brushes.White, path)
            End Using
            Dim px = Perspective.ReadPixels(bmp)
            Dim m(b.Width * b.Height - 1) As Byte
            For i = 0 To m.Length - 1
                m(i) = px(i * 4 + 3)
            Next
            Return m
        End Using
    End Function

    ''' <summary>照片放進形狀：形狀外面的照片變透明（形狀圖層隱藏時也照樣裁）。</summary>
    Public Shared Sub ApplyPhotoClip(bmp As Bitmap, recipe As EditRecipe)
        If recipe.PhotoClip Is Nothing OrElse recipe.Drawings Is Nothing Then Return
        Dim id = recipe.PhotoClip
        Dim frame = recipe.Drawings.FirstOrDefault(Function(x) x.Id = id)
        If frame Is Nothing Then Return
        Dim b As Rectangle
        Dim m = Mask(frame, bmp.Width, bmp.Height, b)
        Dim rect = New Rectangle(0, 0, bmp.Width, bmp.Height)
        Dim data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
        Try
            Dim stride = data.Stride
            Dim row(stride - 1) As Byte
            For y = 0 To bmp.Height - 1
                Dim ptr = data.Scan0 + y * stride
                Runtime.InteropServices.Marshal.Copy(ptr, row, 0, stride)
                For x = 0 To bmp.Width - 1
                    Dim k = 0
                    If m IsNot Nothing AndAlso b.Contains(x, y) Then k = m((y - b.Y) * b.Width + (x - b.X))
                    If k = 255 Then Continue For
                    row(x * 4 + 3) = CByte(row(x * 4 + 3) * k \ 255)
                Next
                Runtime.InteropServices.Marshal.Copy(row, 0, ptr, stride)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
    End Sub

    ''' <summary>把 d 畫在框的範圍裡（先畫在暫存圖上，再依框的範圍合回去）。</summary>
    Friend Shared Sub DrawClipped(bmp As Bitmap, d As DrawLayer, frame As DrawLayer)
        Dim b As Rectangle
        Dim m = Mask(frame, bmp.Width, bmp.Height, b)
        If m Is Nothing Then Return
        Using tmp = DirectCast(bmp.Clone(), Bitmap)
            DrawingRenderer.DrawOne(tmp, d)
            Dim dst = bmp.LockBits(b, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
            Dim src = tmp.LockBits(b, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Try
                Dim rowD(dst.Stride - 1) As Byte, rowS(src.Stride - 1) As Byte
                For y = 0 To b.Height - 1
                    Runtime.InteropServices.Marshal.Copy(dst.Scan0 + y * dst.Stride, rowD, 0, dst.Stride)
                    Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, rowS, 0, src.Stride)
                    For x = 0 To b.Width - 1
                        Dim k = CInt(m(y * b.Width + x))
                        If k = 0 Then Continue For
                        For c = 0 To 3
                            Dim i = x * 4 + c
                            rowD(i) = CByte(rowD(i) + (CInt(rowS(i)) - rowD(i)) * k \ 255)
                        Next
                    Next
                    Runtime.InteropServices.Marshal.Copy(rowD, 0, dst.Scan0 + y * dst.Stride, dst.Stride)
                Next
            Finally
                bmp.UnlockBits(dst)
                tmp.UnlockBits(src)
            End Try
        End Using
    End Sub

    ''' <summary>框的一部分：只有填色（畫在內容下面）或只有外框（畫在內容上面）。</summary>
    Friend Shared Function FramePart(frame As DrawLayer, fillOnly As Boolean) As DrawLayer
        Dim f = frame.Clone()
        If fillOnly Then
            f.Stroked = False
        Else
            f.Filled = False
            f.Shadow = False
        End If
        Return f
    End Function
End Class
