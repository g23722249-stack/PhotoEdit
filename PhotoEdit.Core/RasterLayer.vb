Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>
''' 點陣圖層（直接繪製）：把圖層的操作紀錄（筆畫、橡皮擦、點陣化的圖形）由下而上依序合成成一張整幅的像素圖。
''' 合成結果依「前 k 筆」快取：畫上新的一筆時從上一次的結果接著畫，只要多算那一筆；復原時前幾步也還在快取裡。
''' </summary>
Partial Public NotInheritable Class DrawingRenderer

    ''' <summary>
    ''' 快取上限：至少留 8 張；記憶體預算內可以更多（圖層多時每層的結果都要留著，否則每畫一筆都得從頭重畫所有圖層）。
    ''' </summary>
    Private Const MinRasterCache As Integer = 8
    Private Const MaxRasterCache As Integer = 64
    Private Const RasterCacheBudget As Long = 600L * 1024 * 1024
    Private Shared ReadOnly _rasterCache As New Dictionary(Of ULong, Bitmap)()
    Private Shared ReadOnly _rasterOrder As New LinkedList(Of ULong)()

    Private Shared Sub DrawRasterLayer(dst As Bitmap, layer As DrawLayer, w As Integer, h As Integer)
        Dim opacity = Math.Max(0, Math.Min(100, layer.Opacity)) / 100.0F
        If opacity <= 0 OrElse layer.Ops Is Nothing OrElse layer.Ops.Count = 0 Then Return
        Dim dest As New Rectangle(CInt(Math.Round(layer.X * h)), CInt(Math.Round(layer.Y * h)), w, h)
        ' 混色、塗抹、仿製筆要讀取下面已經合成好的照片與圖層。
        Dim below As Byte() = Nothing
        If layer.Ops.Any(Function(o) (Global.PhotoEdit.DrawLayer.SamplesCanvas(o.Brush) AndAlso o.Shape <> DrawShape.Bucket AndAlso o.Shape <> DrawShape.Gradient AndAlso o.Region Is Nothing) OrElse
                                     BucketNeedsBelow(o) OrElse (o.Region IsNot Nothing AndAlso o.FillContent = FillContent.ContentAware)) AndAlso
           dst.Width = w AndAlso dst.Height = h Then
            below = Perspective.ReadPixels(dst)
        End If
        ' 油漆桶「取樣所有圖層」：上面的圖層也要看（畫在透明底上；畫的時候不再往上找，避免互相遞迴）
        Dim above As Byte() = Nothing
        Dim drawAbove = AboveDrawer
        If drawAbove IsNot Nothing AndAlso dst.Width = w AndAlso dst.Height = h AndAlso
           layer.Ops.Any(Function(o) o.Shape = DrawShape.Bucket AndAlso o.Bucket IsNot Nothing AndAlso o.Bucket.Sample = BucketSample.AllLayers) Then
            AboveDrawer = Nothing
            Try
                Using tmp As New Bitmap(w, h, PixelFormat.Format32bppArgb)
                    drawAbove(tmp)
                    above = Perspective.ReadPixels(tmp)
                End Using
            Finally
                AboveDrawer = drawAbove
            End Try
        End If
        Dim bmp = RasterBitmap(layer, w, h, below, dest.X, dest.Y, above)
        If bmp Is Nothing Then Return
        SyncLock bmp
            Using shaded = SurfaceOnDrawing(bmp, dest, w, h) ' 表面紋理「只有繪圖」
                LayerBlend.Composite(dst, If(shaded, bmp), dest, opacity, layer.Blend)
            End Using
        End SyncLock
    End Sub

    ''' <summary>點陣圖層合成好的像素（w × h、未加位移與不透明度）；結果放在快取裡，呼叫端不可釋放。</summary>
    Private Shared Function RasterBitmap(layer As DrawLayer, w As Integer, h As Integer,
                                         Optional below As Byte() = Nothing, Optional ox As Integer = 0, Optional oy As Integer = 0,
                                         Optional above As Byte() = Nothing) As Bitmap
        Dim ops = layer.Ops
        Dim keys(ops.Count) As ULong
        keys(0) = ChainHash(0UL, $"raster|{w}x{h}|{ox},{oy}" & PaperKey())
        ' 有讀取畫布的筆時，下面的照片一改（例如調色），抹過的地方就要重算。
        If below IsNot Nothing Then keys(0) = ChainHash(keys(0), BelowSignature(below))
        ' 油漆桶取樣所有圖層：上面的圖層改了（例如線稿）也要重算
        If above IsNot Nothing Then keys(0) = ChainHash(keys(0), "above|" & BelowSignature(above))
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
            Dim op = ops(i)
            ' 保留透明度：先記下每個像素的透明度，畫完再放回去（只改顏色，透明的地方不會被畫上）
            Dim keptAlpha As Byte() = If(op.KeepAlpha, Perspective.ReadPixels(bmp), Nothing)
            If op.Shape = DrawShape.Gradient Then
                ApplyGradientOp(bmp, w, h, op)
            ElseIf op.Shape = DrawShape.Bucket Then
                ApplyBucketOp(bmp, below, above, w, h, ox, oy, op)
            ElseIf op.Region IsNot Nothing AndAlso op.FillContent = FillContent.ContentAware Then
                ApplyContentFillOp(bmp, below, w, h, ox, oy, op)
            ElseIf op.Region IsNot Nothing AndAlso op.FillContent = FillContent.Material AndAlso op.Filled Then
                ApplyMaterialRegionOp(bmp, w, h, op)
            ElseIf Global.PhotoEdit.DrawLayer.SamplesCanvas(op.Brush) AndAlso op.Shape <> DrawShape.Raster AndAlso op.Item Is Nothing Then
                Dim px = Perspective.ReadPixels(bmp)
                ApplySamplingOp(px, below, w, h, op, ox, oy)
                Perspective.WritePixels(bmp, px)
            Else
                ApplyRasterOp(bmp, op, w, h)
            End If
            If keptAlpha IsNot Nothing Then
                Dim px = Perspective.ReadPixels(bmp)
                For k = 3 To px.Length - 1 Step 4
                    px(k) = keptAlpha(k)
                Next
                Perspective.WritePixels(bmp, px)
            End If
        Next

        SyncLock _rasterCache
            Dim existing As Bitmap = Nothing
            If _rasterCache.TryGetValue(keys(ops.Count), existing) Then
                bmp.Dispose()
                Return existing
            End If
            _rasterCache(keys(ops.Count)) = bmp
            _rasterOrder.AddFirst(keys(ops.Count))
            While _rasterOrder.Count > MaxRasterCache OrElse (_rasterOrder.Count > MinRasterCache AndAlso RasterCacheBytes() > RasterCacheBudget)
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
                        Dim amount = sa / 255.0F * opacity
                        Select Case op.EraseMode
                            Case EraseMode.Bleach, EraseMode.Darken
                                ' 漂白／加深：每一筆往白（黑）靠一半的筆刷覆蓋率，多擦幾次越來越淡（暗）；透明度不變
                                Dim k = amount * 0.5F
                                Dim targetV = If(op.EraseMode = EraseMode.Bleach, 255.0F, 0.0F)
                                For ch = 0 To 2
                                    Dim v = d(x * 4 + ch)
                                    d(x * 4 + ch) = CByte(Math.Max(0, Math.Min(255, Math.Round(v + (targetV - v) * k))))
                                Next
                            Case Else
                                d(x * 4 + 3) = CByte(Math.Max(0, Math.Min(255, Math.Round(d(x * 4 + 3) * (1.0F - amount)))))
                        End Select
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
    ''' <summary>下面照片的指紋：均勻取約 16000 個像素算 SHA1（照片有改，抹過的地方才重算）。</summary>
    Private Shared Function BelowSignature(below As Byte()) As String
        Dim pixels = below.Length \ 4
        Dim stepPx = Math.Max(1, pixels \ 16384)
        Dim sample As New List(Of Byte)(16384 * 4 + 8)
        For i = 0 To pixels - 1 Step stepPx
            sample.Add(below(i * 4)) : sample.Add(below(i * 4 + 1)) : sample.Add(below(i * 4 + 2)) : sample.Add(below(i * 4 + 3))
        Next
        Return Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(sample.ToArray()))
    End Function

    Private Shared Function ChainHash(seed As ULong, text As String) As ULong
        Dim bytes = System.Text.Encoding.UTF8.GetBytes(text)
        Dim buf(bytes.Length + 7) As Byte
        BitConverter.GetBytes(seed).CopyTo(buf, 0)
        bytes.CopyTo(buf, 8)
        Return BitConverter.ToUInt64(System.Security.Cryptography.SHA1.HashData(buf), 0)
    End Function

    Private Shared Function RasterCacheBytes() As Long
        Dim total = 0L
        For Each b In _rasterCache.Values
            total += CLng(b.Width) * b.Height * 4
        Next
        Return total
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
