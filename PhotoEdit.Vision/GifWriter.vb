Imports System.IO
Imports OpenCvSharp

''' <summary>
''' 動態 GIF 寫入（GDI+ 不能寫多格 GIF）：全片共用一組 256 色調色盤（k-means 取樣），4×4 有序抖色減少色帶，LZW 壓縮，無限循環。
''' </summary>
Public NotInheritable Class GifWriter
    Implements IDisposable

    Private ReadOnly _out As FileStream
    Private ReadOnly _w As Integer, _h As Integer
    Private ReadOnly _palette As Byte() ' 256×3（R、G、B）
    Private ReadOnly _lut As Byte() ' 32×32×32 → 調色盤索引

    Private Shared ReadOnly Bayer As Integer(,) = {{0, 8, 2, 10}, {12, 4, 14, 6}, {3, 11, 1, 9}, {15, 7, 13, 5}}

    ''' <summary>從幾張樣本（BGR）算 256 色調色盤（R、G、B 順序）。</summary>
    Public Shared Function BuildPalette(samples As IEnumerable(Of Mat)) As Byte()
        Dim pts As New List(Of Single)
        Dim rnd As New Random(1)
        For Each s In samples
            Dim n = s.Rows * s.Cols
            Dim px(n * 3 - 1) As Byte
            Using c = s.Clone()
                Runtime.InteropServices.Marshal.Copy(c.Data, px, 0, px.Length)
            End Using
            Dim stepN = Math.Max(1, n \ 12000)
            For i = rnd.Next(stepN) To n - 1 Step stepN
                pts.Add(px(i * 3 + 2)) : pts.Add(px(i * 3 + 1)) : pts.Add(px(i * 3))
            Next
        Next
        Dim count = pts.Count \ 3
        Dim k = Math.Min(256, Math.Max(2, count))
        Dim pal(255 * 3 + 2) As Byte
        Using data As New Mat(count, 3, MatType.CV_32FC1), labels As New Mat(), centers As New Mat()
            Runtime.InteropServices.Marshal.Copy(pts.ToArray(), 0, data.Data, pts.Count)
            Cv2.Kmeans(data, k, labels, New TermCriteria(CriteriaTypes.Eps Or CriteriaTypes.MaxIter, 12, 1.0), 1, KMeansFlags.PpCenters, centers)
            For i = 0 To k - 1
                For c = 0 To 2
                    pal(i * 3 + c) = CByte(Math.Max(0, Math.Min(255, Math.Round(centers.At(Of Single)(i, c)))))
                Next
            Next
        End Using
        Return pal
    End Function

    Public Sub New(path As String, width As Integer, height As Integer, palette As Byte())
        _w = width : _h = height : _palette = palette
        ' 查表：每個 5 位元色格找最近的調色盤顏色
        ReDim _lut(32 * 32 * 32 - 1)
        For r = 0 To 31
            For g = 0 To 31
                For b = 0 To 31
                    Dim rr = r * 8 + 4, gg = g * 8 + 4, bb = b * 8 + 4
                    Dim best = 0, bd = Integer.MaxValue
                    For i = 0 To 255
                        Dim dr = rr - palette(i * 3), dg = gg - palette(i * 3 + 1), db = bb - palette(i * 3 + 2)
                        Dim d = 3 * dr * dr + 4 * dg * dg + 2 * db * db
                        If d < bd Then bd = d : best = i
                    Next
                    _lut((r << 10) Or (g << 5) Or b) = CByte(best)
                Next
            Next
        Next
        _out = New FileStream(path, FileMode.Create, FileAccess.Write)
        Dim hdr = System.Text.Encoding.ASCII.GetBytes("GIF89a")
        _out.Write(hdr, 0, hdr.Length)
        WriteShort(_w) : WriteShort(_h)
        _out.WriteByte(&HF7) ' 有全域調色盤、256 色
        _out.WriteByte(0) : _out.WriteByte(0)
        _out.Write(palette, 0, 768)
        ' 無限循環
        _out.Write({&H21, &HFF, &HB, &H4E, &H45, &H54, &H53, &H43, &H41, &H50, &H45, &H32, &H2E, &H30, 3, 1, 0, 0, 0}, 0, 19)
    End Sub

    Private Sub WriteShort(v As Integer)
        _out.WriteByte(CByte(v And &HFF)) : _out.WriteByte(CByte((v >> 8) And &HFF))
    End Sub

    ''' <summary>加一格（BGR，大小要和建立時相同）；delay＝停留時間（1/100 秒）。</summary>
    Public Sub AddFrame(bgr As Mat, delay As Integer)
        Dim n = _w * _h
        Dim px(n * 3 - 1) As Byte
        Using c = bgr.Clone()
            Runtime.InteropServices.Marshal.Copy(c.Data, px, 0, px.Length)
        End Using
        Dim idx(n - 1) As Byte
        For y = 0 To _h - 1
            For x = 0 To _w - 1
                Dim i = y * _w + x
                Dim d = (Bayer(y And 3, x And 3) - 7.5) * 0.6 ' ±4.5 左右的抖色
                Dim r = Clamp5(px(i * 3 + 2) + d), g = Clamp5(px(i * 3 + 1) + d), b = Clamp5(px(i * 3) + d)
                idx(i) = _lut((r << 10) Or (g << 5) Or b)
            Next
        Next
        ' 圖形控制：延遲、不透明
        _out.Write({&H21, &HF9, 4, 0}, 0, 4)
        WriteShort(Math.Max(2, delay))
        _out.WriteByte(0) : _out.WriteByte(0)
        ' 影像描述
        _out.WriteByte(&H2C)
        WriteShort(0) : WriteShort(0) : WriteShort(_w) : WriteShort(_h)
        _out.WriteByte(0)
        Lzw(idx)
    End Sub

    Private Shared Function Clamp5(v As Double) As Integer
        Return Math.Max(0, Math.Min(31, CInt(v) >> 3))
    End Function

    ''' <summary>GIF 的 LZW（8 位元碼，可變碼長，表滿 4096 時送清除碼）。</summary>
    Private Sub Lzw(data As Byte())
        Const MinCode = 8
        Dim clearCode = 1 << MinCode, eoi = clearCode + 1
        _out.WriteByte(MinCode)
        Dim block As New List(Of Byte)(255)
        Dim bitBuf = 0, bitCnt = 0
        Dim emit = Sub(code As Integer, size As Integer)
                       bitBuf = bitBuf Or (code << bitCnt)
                       bitCnt += size
                       While bitCnt >= 8
                           block.Add(CByte(bitBuf And &HFF))
                           bitBuf >>= 8
                           bitCnt -= 8
                           If block.Count = 255 Then
                               _out.WriteByte(255)
                               _out.Write(block.ToArray(), 0, 255)
                               block.Clear()
                           End If
                       End While
                   End Sub
        Dim dict As New Dictionary(Of Integer, Integer)
        Dim codeSize = MinCode + 1, nextCode = eoi + 1
        emit(clearCode, codeSize)
        Dim prefix = CInt(data(0))
        For i = 1 To data.Length - 1
            Dim c = CInt(data(i))
            Dim key = (prefix << 8) Or c
            Dim found As Integer
            If dict.TryGetValue(key, found) Then
                prefix = found
                Continue For
            End If
            emit(prefix, codeSize)
            ' 新碼加進表（同 gif-h：剛加的碼超過目前碼長就加長；加到 4095 立刻送清除碼重來）
            Dim added = nextCode
            dict(key) = added
            nextCode += 1
            If added >= (1 << codeSize) AndAlso codeSize < 12 Then codeSize += 1
            If added = 4095 Then
                emit(clearCode, codeSize)
                dict.Clear()
                codeSize = MinCode + 1
                nextCode = eoi + 1
            End If
            prefix = c
        Next
        emit(prefix, codeSize)
        emit(eoi, codeSize)
        If bitCnt > 0 Then emit(0, 8 - bitCnt)
        If block.Count > 0 Then
            _out.WriteByte(CByte(block.Count))
            _out.Write(block.ToArray(), 0, block.Count)
        End If
        _out.WriteByte(0)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _out.WriteByte(&H3B)
        _out.Dispose()
    End Sub
End Class
