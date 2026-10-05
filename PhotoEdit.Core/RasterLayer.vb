Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>
''' 點陣圖層（直接繪製）：把圖層的操作紀錄（筆畫、橡皮擦、點陣化的圖形）由下而上依序合成成一張整幅的像素圖。
''' 合成結果依「前 k 筆」快取：畫上新的一筆時從上一次的結果接著畫，只要多算那一筆；復原時前幾步也還在快取裡。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    Private Const MaxRasterCache As Integer = 8
    Private Shared ReadOnly _rasterCache As New Dictionary(Of ULong, Bitmap)()
    Private Shared ReadOnly _rasterOrder As New LinkedList(Of ULong)()

    Private Shared Sub DrawRasterLayer(dst As Bitmap, layer As DrawLayer, w As Integer, h As Integer)
        Dim opacity = Math.Max(0, Math.Min(100, layer.Opacity)) / 100.0F
        If opacity <= 0 OrElse layer.Ops Is Nothing OrElse layer.Ops.Count = 0 Then Return
        Dim bmp = RasterBitmap(layer, w, h)
        If bmp Is Nothing Then Return
        Dim dest As New Rectangle(CInt(Math.Round(layer.X * h)), CInt(Math.Round(layer.Y * h)), w, h)
        SyncLock bmp
            LayerBlend.Composite(dst, bmp, dest, opacity, layer.Blend)
        End SyncLock
    End Sub

    ''' <summary>點陣圖層合成好的像素（w × h、未加位移與不透明度）；結果放在快取裡，呼叫端不可釋放。</summary>
    Private Shared Function RasterBitmap(layer As DrawLayer, w As Integer, h As Integer) As Bitmap
        Dim ops = layer.Ops
        Dim keys(ops.Count) As ULong
        keys(0) = ChainHash(0UL, $"raster|{w}x{h}")
        For i = 0 To ops.Count - 1
            keys(i + 1) = ChainHash(keys(i), System.Text.Json.JsonSerializer.Serialize(ops(i), _jsonOptions))
        Next

        ' 找快取裡最長的「前 k 筆」，從那裡接著畫。
        Dim start = 0
        Dim baseBmp As Bitmap = Nothing
        SyncLock _rasterCache
            For k = ops.Count To 1 Step -1
                If _rasterCache.TryGetValue(keys(k), baseBmp) Then
                    _rasterOrder.Remove(keys(k))
                    _rasterOrder.AddFirst(keys(k))
                    start = k
                    Exit For
                End If
            Next
        End SyncLock
        If start = ops.Count Then Return baseBmp

        Dim bmp As Bitmap
        If baseBmp IsNot Nothing Then
            SyncLock baseBmp
                bmp = baseBmp.Clone(New Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb)
            End SyncLock
        Else
            bmp = New Bitmap(w, h, PixelFormat.Format32bppArgb)
        End If
        For i = start To ops.Count - 1
            ApplyRasterOp(bmp, ops(i), w, h)
        Next

        SyncLock _rasterCache
            Dim existing As Bitmap = Nothing
            If _rasterCache.TryGetValue(keys(ops.Count), existing) Then
                bmp.Dispose()
                Return existing
            End If
            _rasterCache(keys(ops.Count)) = bmp
            _rasterOrder.AddFirst(keys(ops.Count))
            While _rasterOrder.Count > MaxRasterCache
                Dim old = _rasterOrder.Last.Value
                _rasterOrder.RemoveLast()
                Dim oldBmp = _rasterCache(old)
                _rasterCache.Remove(old)
                SyncLock oldBmp
                    oldBmp.Dispose()
                End SyncLock
            End While
        End SyncLock
        Return bmp
    End Function

    ''' <summary>
    ''' 把一筆畫上去：一般筆畫與圖形疊在上面，橡皮擦依筆刷的覆蓋率減去下面的不透明度。
    ''' 合併圖層時併進來的文字貼圖（Item）與整個點陣圖層（巢狀的 Ops）也是一筆，各自保留不透明度與混合模式。
    ''' </summary>
    Private Shared Sub ApplyRasterOp(bmp As Bitmap, op As DrawLayer, w As Integer, h As Integer)
        If op.Item IsNot Nothing Then
            Creative.DrawOverlayOnto(bmp, op.Item, Nothing)
            Return
        End If
        Dim opacity = Math.Max(0, Math.Min(100, op.Opacity)) / 100.0F
        If opacity <= 0 Then Return
        If op.Region IsNot Nothing Then
            Using fill = RegionBitmap(op, w, h)
                If fill IsNot Nothing Then LayerBlend.Composite(bmp, fill, New Rectangle(0, 0, w, h), opacity, op.Blend)
            End Using
            Return
        End If
        If op.Shape = DrawShape.Raster Then
            If op.Ops Is Nothing OrElse op.Ops.Count = 0 Then Return
            Dim nested = RasterBitmap(op, w, h)
            If nested Is Nothing Then Return
            SyncLock nested
                LayerBlend.Composite(bmp, nested, New Rectangle(CInt(Math.Round(op.X * h)), CInt(Math.Round(op.Y * h)), w, h), opacity, op.Blend)
            End SyncLock
            Return
        End If
        Dim item = Render(op, w, h)
        If item.Bitmap Is Nothing Then Return
        Try
            If Not op.Eraser Then
                LayerBlend.Composite(bmp, item.Bitmap, item.Region, opacity, op.Blend)
                Return
            End If

            Dim r = item.Region
            Dim dst = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb)
            Dim src = item.Bitmap.LockBits(New Rectangle(0, 0, r.Width, r.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Try
                Dim d(r.Width * 4 - 1) As Byte, s(r.Width * 4 - 1) As Byte
                For y = 0 To r.Height - 1
                    Dim dp = dst.Scan0 + y * dst.Stride
                    Marshal.Copy(dp, d, 0, d.Length)
                    Marshal.Copy(src.Scan0 + y * src.Stride, s, 0, s.Length)
                    Dim changed = False
                    For x = 0 To r.Width - 1
                        Dim sa = s(x * 4 + 3)
                        If sa = 0 OrElse d(x * 4 + 3) = 0 Then Continue For
                        Dim keep = 1.0F - sa / 255.0F * opacity
                        d(x * 4 + 3) = CByte(Math.Max(0, Math.Min(255, Math.Round(d(x * 4 + 3) * keep))))
                        changed = True
                    Next
                    If changed Then Marshal.Copy(d, 0, dp, d.Length)
                Next
            Finally
                item.Bitmap.UnlockBits(src)
                bmp.UnlockBits(dst)
            End Try
        Finally
            item.Bitmap.Dispose()
        End Try
    End Sub

    ''' <summary>選取區填色／描邊畫成 w × h 的圖層。</summary>
    Private Shared Function RegionBitmap(op As DrawLayer, w As Integer, h As Integer) As Bitmap
        Dim mask = SelectionMask.Render(op.Region, w, h)
        Dim px(w * h * 4 - 1) As Byte
        If op.Filled Then
            Dim c = Color.FromArgb(op.FillColorArgb)
            For i = 0 To mask.Length - 1
                If mask(i) = 0 Then Continue For
                px(i * 4) = c.B : px(i * 4 + 1) = c.G : px(i * 4 + 2) = c.R : px(i * 4 + 3) = CByte(CInt(mask(i)) * c.A \ 255)
            Next
        End If
        If op.Stroked Then
            Dim line = SelectionMask.Outline(mask, w, h, CInt(Math.Max(1, Math.Round(op.StrokeWidth * h))), CInt(op.Param2))
            Dim c = Color.FromArgb(op.StrokeColorArgb)
            For i = 0 To line.Length - 1
                If line(i) = 0 Then Continue For
                ' 描邊蓋在填色上面（一般「正常」合成）。
                Dim sa = CInt(line(i)) * c.A / 255.0 / 255.0
                Dim da = px(i * 4 + 3) / 255.0
                Dim oa = sa + da * (1 - sa)
                px(i * 4) = CByte(Math.Round((c.B * sa + px(i * 4) * da * (1 - sa)) / oa))
                px(i * 4 + 1) = CByte(Math.Round((c.G * sa + px(i * 4 + 1) * da * (1 - sa)) / oa))
                px(i * 4 + 2) = CByte(Math.Round((c.R * sa + px(i * 4 + 2) * da * (1 - sa)) / oa))
                px(i * 4 + 3) = CByte(Math.Round(oa * 255))
            Next
        End If
        Dim bmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
        Perspective.WritePixels(bmp, px)
        Return bmp
    End Function

    ''' <summary>接續前一個雜湊值（SHA1 取前 8 位元組），算出「前 k 筆」的快取鍵。</summary>
    Private Shared Function ChainHash(seed As ULong, text As String) As ULong
        Dim bytes = System.Text.Encoding.UTF8.GetBytes(text)
        Dim buf(bytes.Length + 7) As Byte
        BitConverter.GetBytes(seed).CopyTo(buf, 0)
        bytes.CopyTo(buf, 8)
        Return BitConverter.ToUInt64(System.Security.Cryptography.SHA1.HashData(buf), 0)
    End Function

    Private Shared Sub ClearRasterCache()
        SyncLock _rasterCache
            For Each b In _rasterCache.Values
                b.Dispose()
            Next
            _rasterCache.Clear()
            _rasterOrder.Clear()
        End SyncLock
    End Sub
End Class
