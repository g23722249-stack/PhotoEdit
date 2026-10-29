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
    ''' <summary>讀下面畫面的筆（塗抹、模糊…）之後每隔幾筆留一張檢查點。</summary>
    Private Const CheckpointEvery As Integer = 32
    Private Shared ReadOnly _rasterCache As New Dictionary(Of ULong, Bitmap)()
    ''' <summary>
    ''' 快取裡每張圖的位元組數（放進去時記下）：算總量時不能去讀快取圖的 Width，別的執行緒可能正鎖著那張圖合成，
    ''' GDI+ 會丟「Object is currently in use elsewhere」。
    ''' </summary>
    Private Shared ReadOnly _rasterBytes As New Dictionary(Of ULong, Long)()
    Private Shared ReadOnly _rasterOrder As New LinkedList(Of ULong)()

    Private Shared Sub DrawRasterLayer(dst As Bitmap, layer As DrawLayer, w As Integer, h As Integer)
        Dim opacity = Math.Max(0, Math.Min(100, layer.Opacity)) / 100.0F
        If opacity <= 0 OrElse layer.Ops Is Nothing OrElse layer.Ops.Count = 0 Then Return
        Dim dest As New Rectangle(CInt(Math.Round(layer.X * h)), CInt(Math.Round(layer.Y * h)), w, h)
        ' 混色、塗抹、仿製筆要讀取下面已經合成好的照片與圖層。
        Dim below As Byte() = Nothing
        If layer.Ops.Any(Function(o) (Global.PhotoEdit.DrawLayer.SamplesCanvas(o.Brush) AndAlso o.Shape <> DrawShape.Bucket AndAlso o.Shape <> DrawShape.Gradient AndAlso o.Region Is Nothing) OrElse
                                     BucketNeedsBelow(o) OrElse RegionReadsBelow(o)) AndAlso
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
        For attempt = 1 To 3
            Dim bmp = RasterBitmap(layer, w, h, below, dest.X, dest.Y, above)
            If bmp Is Nothing Then Return
            SyncLock bmp
                If IsEvicted(bmp) Then Continue For ' 剛被別的執行緒擠出快取釋放了：重算
                Using shaded = SurfaceOnDrawing(bmp, dest, w, h) ' 表面紋理「只有繪圖」
                    LayerBlend.Composite(dst, If(shaded, bmp), dest, opacity, layer.Blend)
                End Using
                Return
            End SyncLock
        Next
    End Sub

    ''' <summary>點陣圖層合成好的像素（w × h、未加位移與不透明度）；結果放在快取裡，呼叫端不可釋放。</summary>
    Private Shared Function RasterBitmap(layer As DrawLayer, w As Integer, h As Integer,
                                         Optional below As Byte() = Nothing, Optional ox As Integer = 0, Optional oy As Integer = 0,
                                         Optional above As Byte() = Nothing) As Bitmap
        Dim ops = layer.Ops
        Dim keys(ops.Count) As ULong
        keys(0) = ChainHash(0UL, $"raster|{w}x{h}|{ox},{oy}" & PaperKey())
        ' 每一筆只記它真正讀到的東西：普通的筆不讀下面；塗抹、模糊這類筆只讀筆畫附近（下面那幾格的雜湊）；
        ' 油漆桶、內容感知填色讀整張。原本整層都綁著整張下面的雜湊，在下面的圖層畫一筆，這層就得從第一筆重畫起（幾千筆時一筆要一秒多）。
        Dim belowSig As String = Nothing, aboveSig As String = Nothing
        Dim tiles As BelowTiles = Nothing
        For i = 0 To ops.Count - 1
            Dim op = ops(i)
            Dim k = ChainStep(keys(i), op)
            If below IsNot Nothing OrElse above IsNot Nothing Then
                Select Case BelowDependency(op)
                    Case BelowUse.Whole
                        If below IsNot Nothing Then
                            If belowSig Is Nothing Then belowSig = BelowSignature(below)
                            k = ChainHash(k, belowSig)
                        End If
                        If above IsNot Nothing AndAlso op.Shape = DrawShape.Bucket Then
                            If aboveSig Is Nothing Then aboveSig = "above|" & BelowSignature(above)
                            k = ChainHash(k, aboveSig)
                        End If
                    Case BelowUse.Around
                        If below IsNot Nothing Then
                            If tiles Is Nothing Then tiles = New BelowTiles(below, w, h)
                            k = ChainHash(k, tiles.Signature(SamplingReadRect(op, w, h, ox, oy)))
                        End If
                End Select
            End If
            keys(i + 1) = k
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
                If IsEvicted(baseBmp) Then
                    bmp = New Bitmap(w, h, PixelFormat.Format32bppArgb) ' 接續的那張剛被釋放：從頭畫
                    start = 0
                Else
                    bmp = baseBmp.Clone(New Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb)
                End If
            End SyncLock
        Else
            bmp = New Bitmap(w, h, PixelFormat.Format32bppArgb)
        End If
        ' 第一筆讀下面畫面的筆：它之前的筆和下面無關，在那裡留一張檢查點；下面改了只要從這裡接著畫。
        ' 之後每 CheckpointEvery 筆再留一張：下面改到哪一筆讀的地方，就從最近的檢查點接著畫
        Dim firstDep = -1
        If below IsNot Nothing OrElse above IsNot Nothing Then
            For i = 0 To ops.Count - 1
                If BelowDependency(ops(i)) <> BelowUse.None Then firstDep = i : Exit For
            Next
        End If
        Dim samplePx As Byte() = Nothing ' 讀畫布的筆共用的整層像素（每筆只讀寫它改動的那一塊）
        For i = start To ops.Count - 1
            Dim op = ops(i)
            If firstDep >= 0 AndAlso i > start AndAlso i >= firstDep AndAlso i > 0 AndAlso (i - firstDep) Mod CheckpointEvery = 0 Then
                StoreRaster(keys(i), bmp.Clone(New Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb), w, h)
            End If
            ' 保留透明度：先記下每個像素的透明度，畫完再放回去（只改顏色，透明的地方不會被畫上）
            Dim keptAlpha As Byte() = If(op.KeepAlpha, Perspective.ReadPixels(bmp), Nothing)
            If op.Shape = DrawShape.Gradient Then
                ApplyGradientOp(bmp, w, h, op)
            ElseIf op.Shape = DrawShape.Bucket Then
                ApplyBucketOp(bmp, below, above, w, h, ox, oy, op)
            ElseIf op.Region IsNot Nothing AndAlso (op.FillContent = FillContent.Mosaic OrElse op.FillContent = FillContent.Demosaic) Then
                ApplyMosaicOp(bmp, below, w, h, ox, oy, op)
            ElseIf op.Region IsNot Nothing AndAlso op.FillContent = FillContent.ContentAware Then
                ApplyContentFillOp(bmp, below, w, h, ox, oy, op)
            ElseIf op.Region IsNot Nothing AndAlso op.FillContent = FillContent.Material AndAlso op.Filled Then
                ApplyMaterialRegionOp(bmp, w, h, op)
            ElseIf Global.PhotoEdit.DrawLayer.SamplesCanvas(op.Brush) AndAlso op.Shape <> DrawShape.Raster AndAlso op.Item Is Nothing Then
                ' 只讀寫這一筆碰到的那一塊（仿製、修復筆連來源一起）；原本每筆都讀寫整層，一筆 5 ms
                Dim area = SamplingReadRect(op, w, h, 0, 0)
                If area.Width > 0 AndAlso area.Height > 0 Then
                    If samplePx Is Nothing Then samplePx = New Byte(w * h * 4 - 1) {}
                    CopyRegion(bmp, samplePx, w, area, toArray:=True)
                    ApplySamplingOp(samplePx, below, w, h, op, ox, oy)
                    CopyRegion(bmp, samplePx, w, area, toArray:=False)
                End If
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

        Return StoreRaster(keys(ops.Count), bmp, w, h)
    End Function

    ''' <summary>放進快取（已經有同一個鍵時丟掉這張、傳回原本那張）；超過張數或記憶體預算時擠掉最久沒用的。</summary>
    Private Shared Function StoreRaster(key As ULong, bmp As Bitmap, w As Integer, h As Integer) As Bitmap
        SyncLock _rasterCache
            Dim existing As Bitmap = Nothing
            If _rasterCache.TryGetValue(key, existing) Then
                bmp.Dispose()
                Return existing
            End If
            _rasterCache(key) = bmp
            _rasterBytes(key) = CLng(w) * h * 4
            _rasterOrder.AddFirst(key)
            While _rasterOrder.Count > MaxRasterCache OrElse (_rasterOrder.Count > MinRasterCache AndAlso RasterCacheBytes() > RasterCacheBudget)
                Dim old = _rasterOrder.Last.Value
                _rasterOrder.RemoveLast()
                Dim oldBmp = _rasterCache(old)
                _rasterCache.Remove(old)
                _rasterBytes.Remove(old)
                SyncLock oldBmp
                    oldBmp.Dispose()
                    _evicted.AddOrUpdate(oldBmp, Nothing)
                End SyncLock
            End While
        End SyncLock
        Return bmp
    End Function

    ''' <summary>圖層像素（BGRA，w 寬）與點陣圖之間只複製一塊範圍：toArray＝圖 → 陣列。</summary>
    Private Shared Sub CopyRegion(bmp As Bitmap, px As Byte(), w As Integer, area As Rectangle, toArray As Boolean)
        Dim d = bmp.LockBits(area, If(toArray, ImageLockMode.ReadOnly, ImageLockMode.WriteOnly), PixelFormat.Format32bppArgb)
        Try
            Dim rowBytes = area.Width * 4
            For y = 0 To area.Height - 1
                Dim offset = ((area.Y + y) * w + area.X) * 4
                If toArray Then
                    Marshal.Copy(d.Scan0 + y * d.Stride, px, offset, rowBytes)
                Else
                    Marshal.Copy(px, offset, d.Scan0 + y * d.Stride, rowBytes)
                End If
            Next
        Finally
            bmp.UnlockBits(d)
        End Try
    End Sub

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
            For attempt = 1 To 3
                Dim nested = RasterBitmap(op, w, h)
                If nested Is Nothing Then Return
                SyncLock nested
                    If IsEvicted(nested) Then Continue For
                    LayerBlend.Composite(bmp, nested, New Rectangle(CInt(Math.Round(op.X * h)), CInt(Math.Round(op.Y * h)), w, h), opacity, op.Blend)
                    Return
                End SyncLock
            Next
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
    ''' <summary>安全遮蔽的範圍：有蓋到一點點的就全蓋（羽化、反鋸齒的邊不會半透明露出底下），再往外多 1 像素。</summary>
    Private Shared Function HardMask(mask As Byte(), w As Integer, h As Integer) As Byte()
        Dim result(mask.Length - 1) As Byte
        For y = 0 To h - 1
            For x = 0 To w - 1
                If mask(y * w + x) = 0 Then Continue For
                For yy = Math.Max(0, y - 1) To Math.Min(h - 1, y + 1)
                    For xx = Math.Max(0, x - 1) To Math.Min(w - 1, x + 1)
                        result(yy * w + xx) = 255
                    Next
                Next
            Next
        Next
        Return result
    End Function

    Private Shared Function RegionBitmap(op As DrawLayer, w As Integer, h As Integer) As Bitmap
        Dim mask = SelectionMask.Render(op.Region, w, h)
        If op.FillContent = FillContent.Redact Then mask = HardMask(mask, w, h)
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
    ''' <summary>選取區填滿時會讀下面畫面的：內容感知、打馬賽克、平滑馬賽克。</summary>
    Friend Shared Function RegionReadsBelow(op As Global.PhotoEdit.DrawLayer) As Boolean
        Return op.Region IsNot Nothing AndAlso (op.FillContent = FillContent.ContentAware OrElse op.FillContent = FillContent.Mosaic OrElse op.FillContent = FillContent.Demosaic)
    End Function

    Private Enum BelowUse
        None
        ''' <summary>只讀筆畫附近（塗抹、混色、模糊…）。</summary>
        Around
        ''' <summary>讀整張（油漆桶、內容感知填色）。</summary>
        Whole
    End Enum

    ''' <summary>這一筆畫的時候讀了下面合成好的畫面哪裡（判斷和 RasterBitmap 裡畫的分支一致）。</summary>
    Private Shared Function BelowDependency(op As Global.PhotoEdit.DrawLayer) As BelowUse
        If op.Shape = DrawShape.Gradient Then Return BelowUse.None
        If op.Shape = DrawShape.Bucket Then Return If(BucketNeedsBelow(op), BelowUse.Whole, BelowUse.None)
        If op.Region IsNot Nothing Then Return If(RegionReadsBelow(op), BelowUse.Whole, BelowUse.None)
        If Global.PhotoEdit.DrawLayer.SamplesCanvas(op.Brush) AndAlso op.Shape <> DrawShape.Raster AndAlso op.Item Is Nothing Then Return BelowUse.Around
        Return BelowUse.None
    End Function

    ''' <summary>
    ''' 下面合成好的畫面切成 64 × 64 的格子，每格一個雜湊（整張只掃一遍）；
    ''' 一筆塗抹讀到的範圍＝它蓋到的那幾格的雜湊，別的地方改了不影響這一筆。
    ''' </summary>
    Private NotInheritable Class BelowTiles
        Private Const Tile As Integer = 64
        Private ReadOnly _hash As ULong(), _gw As Integer, _gh As Integer

        Public Sub New(below As Byte(), w As Integer, h As Integer)
            _gw = (w + Tile - 1) \ Tile : _gh = (h + Tile - 1) \ Tile
            Dim gw = _gw
            Dim hash(_gw * _gh - 1) As ULong
            Threading.Tasks.Parallel.For(0, _gh, Sub(ty)
                                                     Dim y0 = ty * Tile, rows = Math.Min(Tile, h - y0)
                                                     Dim buf(Tile * 4 * Tile - 1) As Byte
                                                     For tx = 0 To gw - 1
                                                         Dim x0 = tx * Tile, cols = Math.Min(Tile, w - x0)
                                                         Dim n = 0
                                                         For y = y0 To y0 + rows - 1
                                                             Buffer.BlockCopy(below, (y * w + x0) * 4, buf, n, cols * 4)
                                                             n += cols * 4
                                                         Next
                                                         If n < buf.Length Then Array.Clear(buf, n, buf.Length - n)
                                                         hash(ty * gw + tx) = BitConverter.ToUInt64(System.Security.Cryptography.SHA1.HashData(buf), 0) Xor CULng(n)
                                                     Next
                                                 End Sub)
            _hash = hash
        End Sub

        ''' <summary>範圍（下面畫面的像素座標）蓋到的格子的雜湊；範圍空的時候固定字串。</summary>
        Public Function Signature(r As Rectangle) As String
            If r.Width <= 0 OrElse r.Height <= 0 Then Return "below:none"
            Dim tx0 = r.Left \ Tile, tx1 = (r.Right - 1) \ Tile, ty0 = r.Top \ Tile, ty1 = (r.Bottom - 1) \ Tile
            Dim sb As New System.Text.StringBuilder("below:")
            sb.Append(tx0).Append(","c).Append(ty0).Append(","c).Append(tx1).Append(","c).Append(ty1)
            For ty = ty0 To ty1
                For tx = tx0 To tx1
                    sb.Append("|"c).Append(_hash(ty * _gw + tx).ToString("x"))
                Next
            Next
            Return sb.ToString()
        End Function
    End Class

    Private Shared Function BelowSignature(below As Byte()) As String
        Dim pixels = below.Length \ 4
        Dim stepPx = Math.Max(1, pixels \ 16384)
        Dim sample As New List(Of Byte)(16384 * 4 + 8)
        For i = 0 To pixels - 1 Step stepPx
            sample.Add(below(i * 4)) : sample.Add(below(i * 4 + 1)) : sample.Add(below(i * 4 + 2)) : sample.Add(below(i * 4 + 3))
        Next
        Return Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(sample.ToArray()))
    End Function

    ' 每一筆的內容雜湊：算一次記在那一筆上（畫了幾千筆時，每次算圖不必把所有筆再序列化一次）。
    ' 指紋（點數、顏色、粗細…）變了就重算，正在畫、還在變長的那一筆也對。
    Private Shared ReadOnly _opKeys As New Runtime.CompilerServices.ConditionalWeakTable(Of DrawLayer, Tuple(Of Integer, String))()

    ''' <summary>一筆的指紋（點數、顏色、粗細、位置、頭尾的點…）：用數字雜湊，不組字串（每次算圖每一筆都要算，幾千筆時字串格式化要十幾 ms）。</summary>
    Private Shared Function OpFingerprint(op As DrawLayer) As Integer
        Dim pts = 0
        If op.Strokes IsNot Nothing Then
            For Each st In op.Strokes
                pts += If(st.Points?.Count, 0)
            Next
        End If
        Dim h As New HashCode()
        h.Add(pts) : h.Add(If(op.Strokes?.Count, 0)) : h.Add(If(op.Ops?.Count, 0)) : h.Add(If(op.Points?.Count, 0))
        h.Add(op.StrokeColorArgb) : h.Add(op.FillColorArgb) : h.Add(op.StrokeWidth) : h.Add(op.Opacity) : h.Add(op.Eraser) : h.Add(op.Brush)
        h.Add(op.X) : h.Add(op.Y) : h.Add(op.W) : h.Add(op.H) : h.Add(op.Rotation) : h.Add(op.Param1) : h.Add(op.Param2) : h.Add(op.Sides)
        If op.Strokes IsNot Nothing AndAlso op.Strokes.Count > 0 AndAlso op.Strokes(0).Points?.Count > 0 Then
            Dim p0 = op.Strokes(0).Points(0), ls = op.Strokes(op.Strokes.Count - 1)
            Dim p1 = If(ls.Points?.Count > 0, ls.Points(ls.Points.Count - 1), p0)
            h.Add(p0.X) : h.Add(p0.Y) : h.Add(p1.X) : h.Add(p1.Y)
        End If
        Return h.ToHashCode()
    End Function

    Private Shared Function OpKey(op As DrawLayer) As String
        Dim fp = OpFingerprint(op)
        Dim hit As Tuple(Of Integer, String) = Nothing
        If _opKeys.TryGetValue(op, hit) AndAlso hit.Item1 = fp Then Return hit.Item2
        Dim key = ChainHash(0UL, System.Text.Json.JsonSerializer.Serialize(op, _jsonOptions)).ToString("x16")
        _opKeys.AddOrUpdate(op, Tuple.Create(fp, key))
        Return key
    End Function

    ''' <summary>
    ''' 圖層畫到畫面上的樣子的鍵（不含下面的畫面：由呼叫端把下面的圖層串起來）：點陣圖層＝每一筆的鍵串起來＋不透明度、混合、位移；
    ''' 其他圖層＝整個圖層的 JSON。LayerStack 用它快取「底圖＋下面幾層」合成好的畫面。
    ''' </summary>
    Friend Shared Function StackKey(d As DrawLayer) As String
        If d.Shape = DrawShape.Raster Then
            Dim k = 0UL
            If d.Ops IsNot Nothing Then
                For Each op In d.Ops
                    k = ChainStep(k, op)
                Next
            End If
            Return $"R|{k:x16}|{d.Ops?.Count}|{d.Opacity}|{d.Blend}|{d.X:R}|{d.Y:R}"
        End If
        Return "V|" & System.Text.Json.JsonSerializer.Serialize(d, _jsonOptions)
    End Function

    ''' <summary>這一層畫的時候會不會看上面的圖層（油漆桶「取樣所有圖層」）：會的話下面幾層的合成不能快取。</summary>
    Friend Shared Function ReadsAbove(d As DrawLayer) As Boolean
        If d.Shape <> DrawShape.Raster OrElse d.Ops Is Nothing Then Return False
        Return d.Ops.Any(Function(o) (o.Shape = DrawShape.Bucket AndAlso o.Bucket IsNot Nothing AndAlso o.Bucket.Sample = BucketSample.AllLayers) OrElse
                                    (o.Shape = DrawShape.Raster AndAlso ReadsAbove(o)))
    End Function

    ' 每一筆接在前面之後的鍵也記在那一筆上（預覽、高解析度各一組）：畫了幾千筆時每次算圖不必每筆都再算一次 SHA1
    Private NotInheritable Class ChainMemo
        Public Prev(1) As ULong, Key(1) As String, Result(1) As ULong, Count As Integer, NextSlot As Integer
    End Class
    Private Shared ReadOnly _chainMemo As New Runtime.CompilerServices.ConditionalWeakTable(Of DrawLayer, ChainMemo)()

    ''' <summary>ChainHash(prev, OpKey(op))，結果記在 op 上。</summary>
    Private Shared Function ChainStep(prev As ULong, op As DrawLayer) As ULong
        Dim key = OpKey(op)
        Dim memo = _chainMemo.GetValue(op, Function(o) New ChainMemo())
        SyncLock memo
            For j = 0 To memo.Count - 1
                If memo.Prev(j) = prev AndAlso ReferenceEquals(memo.Key(j), key) Then Return memo.Result(j)
            Next
        End SyncLock
        Dim result = ChainHash(prev, key)
        SyncLock memo
            Dim slot = If(memo.Count < 2, memo.Count, memo.NextSlot)
            memo.Prev(slot) = prev : memo.Key(slot) = key : memo.Result(slot) = result
            If memo.Count < 2 Then memo.Count += 1 Else memo.NextSlot = 1 - memo.NextSlot
        End SyncLock
        Return result
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
        For Each n In _rasterBytes.Values
            total += n
        Next
        Return total
    End Function

    ' 擠出快取而釋放的圖：別的執行緒可能已經拿到它，鎖住後要先檢查（弱參照，不會留住圖）
    Private Shared ReadOnly _evicted As New Runtime.CompilerServices.ConditionalWeakTable(Of Bitmap, Object)()

    Private Shared Function IsEvicted(b As Bitmap) As Boolean
        Dim o As Object = Nothing
        Return _evicted.TryGetValue(b, o)
    End Function

    Private Shared Sub ClearRasterCache()
        SyncLock _rasterCache
            For Each b In _rasterCache.Values
                SyncLock b
                    b.Dispose()
                    _evicted.AddOrUpdate(b, Nothing)
                End SyncLock
            Next
            _rasterCache.Clear()
            _rasterBytes.Clear()
            _rasterOrder.Clear()
        End SyncLock
    End Sub
End Class
