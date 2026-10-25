Imports System.IO
Imports System.IO.Compression
Imports System.Text

''' <summary>
''' 純受控的 PNG 編解碼（不依賴原生影像庫，任何平台都能用）。
''' 解碼：非交錯的灰階／RGB／索引色／灰階+Alpha／RGBA，1～16 位元（16 位元降為 8 位元）。
''' 編碼：RGBA8、RGB8，以及高精度遮罩用的 16 位元灰階。
''' </summary>
Public NotInheritable Class PngCodec
    Implements IImageCodec

    Private Shared ReadOnly Signature As Byte() = {137, 80, 78, 71, 13, 10, 26, 10}

    Public Function CanDecode(path As String) As Boolean Implements IImageCodec.CanDecode
        Return String.Equals(IO.Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
    End Function

    Public Function Decode(path As String) As DecodedImage Implements IImageCodec.Decode
        Using fs = File.OpenRead(path)
            Dim r = DecodeStream(fs)
            r.Info.SourcePath = path
            Return r
        End Using
    End Function

    Public Sub Encode(image As RgbaImage, path As String, format As ExportFormat, jpegQuality As Integer) Implements IImageCodec.Encode
        If format <> ExportFormat.Png Then Throw New NotSupportedException("內建編碼器只支援 PNG；JPEG 由主程式的 WIC 編碼器提供。")
        WriteAtomically(path, Sub(s) EncodeRgba(image, s, Not image.HasTransparency()))
    End Sub

    ''' <summary>先寫到暫存檔再換上，避免中途失敗留下壞檔。</summary>
    Friend Shared Sub WriteAtomically(path As String, write As Action(Of Stream))
        Dim dir = IO.Path.GetDirectoryName(IO.Path.GetFullPath(path))
        If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
        Dim tmp = path & ".tmp"
        Using fs = File.Create(tmp)
            write(fs)
        End Using
        File.Move(tmp, path, True)
    End Sub

    ' ---------------- 編碼 ----------------

    ''' <summary>寫成 RGBA8 或 RGB8（dropAlpha）PNG。</summary>
    Public Shared Sub EncodeRgba(image As RgbaImage, output As Stream, Optional dropAlpha As Boolean = False)
        Dim ch = If(dropAlpha, 3, 4)
        Dim stride = image.Width * ch
        Dim raw(CInt((CLng(stride) + 1) * image.Height) - 1) As Byte
        Dim p = image.Pixels
        For y = 0 To image.Height - 1
            Dim o = y * (stride + 1)
            raw(o) = 0
            Dim src = y * image.Width * 4
            If dropAlpha Then
                For x = 0 To image.Width - 1
                    raw(o + 1 + x * 3) = p(src + x * 4)
                    raw(o + 2 + x * 3) = p(src + x * 4 + 1)
                    raw(o + 3 + x * 3) = p(src + x * 4 + 2)
                Next
            Else
                Buffer.BlockCopy(p, src, raw, o + 1, stride)
            End If
        Next
        FilterRows(raw, stride, image.Height, ch)
        WritePng(output, image.Width, image.Height, 8, If(dropAlpha, 2, 6), raw)
    End Sub

    ''' <summary>遮罩寫成 16 位元灰階 PNG（0..65535），保留去背邊緣的細緻漸層。</summary>
    Public Shared Sub EncodeMask16(mask As FloatMask, output As Stream)
        Dim stride = mask.Width * 2
        Dim raw(CInt((CLng(stride) + 1) * mask.Height) - 1) As Byte
        For y = 0 To mask.Height - 1
            Dim o = y * (stride + 1)
            For x = 0 To mask.Width - 1
                Dim v = mask.Data(y * mask.Width + x)
                Dim q = If(v > 0, If(v >= 1, 65535, CInt(Math.Round(v * 65535.0F))), 0)
                raw(o + 1 + x * 2) = CByte(q >> 8)
                raw(o + 2 + x * 2) = CByte(q And &HFF)
            Next
        Next
        FilterRows(raw, stride, mask.Height, 2)
        WritePng(output, mask.Width, mask.Height, 16, 0, raw)
    End Sub

    Public Shared Sub SaveMask16(mask As FloatMask, path As String)
        WriteAtomically(path, Sub(s) EncodeMask16(mask, s))
    End Sub

    ''' <summary>讀遮罩：16 位元灰階保持全精度；其他格式取灰階（有 Alpha 時取 Alpha）。</summary>
    Public Shared Function LoadMask(path As String) As FloatMask
        Using fs = File.OpenRead(path)
            Dim png = ReadRaw(fs)
            If png.ColorType = 0 AndAlso png.BitDepth = 16 Then
                Dim m As New FloatMask(png.Width, png.Height)
                Dim stride = png.Width * 2
                For y = 0 To png.Height - 1
                    For x = 0 To png.Width - 1
                        Dim i = y * stride + x * 2
                        m.Data(y * png.Width + x) = ((CInt(png.Pixels(i)) << 8) Or png.Pixels(i + 1)) / 65535.0F
                    Next
                Next
                Return m
            End If
            Dim img = ToRgba(png)
            Dim mask As New FloatMask(img.Width, img.Height)
            Dim useAlpha = png.ColorType = 4 OrElse png.ColorType = 6
            For i = 0 To mask.Data.Length - 1
                mask.Data(i) = If(useAlpha, img.Pixels(i * 4 + 3), img.Pixels(i * 4)) / 255.0F
            Next
            Return mask
        End Using
    End Function

    ' 每列選最省的濾波（None/Sub/Up/Average/Paeth 中絕對值總和最小者），原地寫入。
    Private Shared Sub FilterRows(raw As Byte(), stride As Integer, height As Integer, bpp As Integer)
        Dim prev(stride - 1) As Byte, cur(stride - 1) As Byte
        Dim cand(4)() As Byte
        For k = 0 To 4
            cand(k) = New Byte(stride - 1) {}
        Next
        For y = 0 To height - 1
            Dim o = y * (stride + 1) + 1
            Buffer.BlockCopy(raw, o, cur, 0, stride)
            Dim best = 0, bestScore = Long.MaxValue
            For f = 0 To 4
                Dim c = cand(f)
                Dim score = 0L
                For i = 0 To stride - 1
                    Dim a = If(i >= bpp, CInt(cur(i - bpp)), 0)
                    Dim b = CInt(prev(i))
                    Dim cc = If(i >= bpp, CInt(prev(i - bpp)), 0)
                    Dim pred As Integer
                    Select Case f
                        Case 0 : pred = 0
                        Case 1 : pred = a
                        Case 2 : pred = b
                        Case 3 : pred = (a + b) \ 2
                        Case Else : pred = Paeth(a, b, cc)
                    End Select
                    Dim v = CByte((CInt(cur(i)) - pred) And &HFF)
                    c(i) = v
                    score += If(v < 128, v, 256 - v)
                Next
                If score < bestScore Then bestScore = score : best = f
            Next
            raw(o - 1) = CByte(best)
            Buffer.BlockCopy(cand(best), 0, raw, o, stride)
            Dim t = prev : prev = cur : cur = t
        Next
    End Sub

    Private Shared Sub WritePng(output As Stream, w As Integer, h As Integer, bitDepth As Integer, colorType As Integer, filtered As Byte())
        output.Write(Signature, 0, Signature.Length)
        Dim ihdr(12) As Byte
        WriteBE(ihdr, 0, w) : WriteBE(ihdr, 4, h)
        ihdr(8) = CByte(bitDepth) : ihdr(9) = CByte(colorType) : ihdr(10) = 0 : ihdr(11) = 0 : ihdr(12) = 0
        WriteChunk(output, "IHDR", ihdr)
        Using ms As New MemoryStream()
            Using z As New ZLibStream(ms, CompressionLevel.Optimal, True)
                z.Write(filtered, 0, filtered.Length)
            End Using
            WriteChunk(output, "IDAT", ms.ToArray())
        End Using
        WriteChunk(output, "IEND", Array.Empty(Of Byte)())
    End Sub

    Private Shared Sub WriteChunk(s As Stream, type As String, data As Byte())
        Dim len(3) As Byte
        WriteBE(len, 0, data.Length)
        s.Write(len, 0, 4)
        Dim t = Encoding.ASCII.GetBytes(type)
        s.Write(t, 0, 4)
        s.Write(data, 0, data.Length)
        Dim crc = Crc32.Update(Crc32.Update(&HFFFFFFFFUI, t, 0, 4), data, 0, data.Length) Xor &HFFFFFFFFUI
        Dim c(3) As Byte
        c(0) = CByte(crc >> 24) : c(1) = CByte((crc >> 16) And &HFFUI) : c(2) = CByte((crc >> 8) And &HFFUI) : c(3) = CByte(crc And &HFFUI)
        s.Write(c, 0, 4)
    End Sub

    Private Shared Sub WriteBE(b As Byte(), o As Integer, v As Integer)
        b(o) = CByte((v >> 24) And &HFF) : b(o + 1) = CByte((v >> 16) And &HFF) : b(o + 2) = CByte((v >> 8) And &HFF) : b(o + 3) = CByte(v And &HFF)
    End Sub

    ' ---------------- 解碼 ----------------

    Private NotInheritable Class RawPng
        Public Width As Integer
        Public Height As Integer
        Public BitDepth As Integer
        Public ColorType As Integer
        ''' <summary>去濾波後的原始掃描列（不含濾波位元組）。</summary>
        Public Pixels As Byte()
        Public Palette As Byte()
        Public PaletteAlpha As Byte()
        Public TransparentGray As Integer = -1
        Public TransparentRgb As Integer() = Nothing
    End Class

    Public Shared Function DecodeStream(s As Stream) As DecodedImage
        Dim png = ReadRaw(s)
        Dim img = ToRgba(png)
        Dim hasAlphaChannel = png.ColorType = 4 OrElse png.ColorType = 6 OrElse png.PaletteAlpha IsNot Nothing OrElse png.TransparentGray >= 0 OrElse png.TransparentRgb IsNot Nothing
        Dim mode As String
        Select Case png.ColorType
            Case 0 : mode = "Gray"
            Case 2 : mode = "RGB"
            Case 3 : mode = "Indexed"
            Case 4 : mode = "GrayAlpha"
            Case Else : mode = "RGBA"
        End Select
        Return New DecodedImage With {
            .Image = img,
            .Info = New ImageInfo With {
                .Format = "PNG", .Width = img.Width, .Height = img.Height, .ColorMode = mode,
                .BitsPerChannel = png.BitDepth, .HasAlphaChannel = hasAlphaChannel,
                .HasTransparency = hasAlphaChannel AndAlso img.HasTransparency()}}
    End Function

    Private Shared Function ReadRaw(s As Stream) As RawPng
        Dim sig(7) As Byte
        ReadExact(s, sig, 8)
        For i = 0 To 7
            If sig(i) <> Signature(i) Then Throw New InvalidDataException("不是 PNG 檔。")
        Next
        Dim png As New RawPng()
        Dim interlace = 0
        Using idat As New MemoryStream()
            Dim done = False
            While Not done
                Dim hdr(7) As Byte
                ReadExact(s, hdr, 8)
                Dim len = (CInt(hdr(0)) << 24) Or (CInt(hdr(1)) << 16) Or (CInt(hdr(2)) << 8) Or hdr(3)
                If len < 0 Then Throw New InvalidDataException("PNG 區塊長度錯誤。")
                Dim type = Encoding.ASCII.GetString(hdr, 4, 4)
                Dim data(len - 1) As Byte
                ReadExact(s, data, len)
                Dim crc(3) As Byte
                ReadExact(s, crc, 4)
                Select Case type
                    Case "IHDR"
                        png.Width = (CInt(data(0)) << 24) Or (CInt(data(1)) << 16) Or (CInt(data(2)) << 8) Or data(3)
                        png.Height = (CInt(data(4)) << 24) Or (CInt(data(5)) << 16) Or (CInt(data(6)) << 8) Or data(7)
                        png.BitDepth = data(8) : png.ColorType = data(9) : interlace = data(12)
                        ImageLimits.CheckSize(png.Width, png.Height, 4)
                    Case "PLTE"
                        png.Palette = data
                    Case "tRNS"
                        If png.ColorType = 3 Then
                            png.PaletteAlpha = data
                        ElseIf png.ColorType = 0 AndAlso len >= 2 Then
                            png.TransparentGray = (CInt(data(0)) << 8) Or data(1)
                        ElseIf png.ColorType = 2 AndAlso len >= 6 Then
                            png.TransparentRgb = {(CInt(data(0)) << 8) Or data(1), (CInt(data(2)) << 8) Or data(3), (CInt(data(4)) << 8) Or data(5)}
                        End If
                    Case "IDAT"
                        idat.Write(data, 0, len)
                    Case "IEND"
                        done = True
                End Select
            End While
            If interlace <> 0 Then Throw New NotSupportedException("內建 PNG 解碼器不支援交錯式（Adam7）PNG，請用主程式的影像載入。")
            Dim channels = ChannelCount(png.ColorType)
            Dim bitsPerPixel = channels * png.BitDepth
            Dim stride = (png.Width * bitsPerPixel + 7) \ 8
            Dim bpp = Math.Max(1, bitsPerPixel \ 8)
            Dim pixels(CInt(CLng(stride) * png.Height) - 1) As Byte
            idat.Position = 0
            Using z As New ZLibStream(idat, CompressionMode.Decompress)
                Dim prev(stride - 1) As Byte, line(stride) As Byte
                For y = 0 To png.Height - 1
                    ReadExact(z, line, stride + 1)
                    Dim f = line(0)
                    Dim o = y * stride
                    For i = 0 To stride - 1
                        Dim a = If(i >= bpp, CInt(pixels(o + i - bpp)), 0)
                        Dim b = CInt(prev(i))
                        Dim c = If(i >= bpp, CInt(prev(i - bpp)), 0)
                        Dim x = CInt(line(i + 1))
                        Dim v As Integer
                        Select Case f
                            Case 0 : v = x
                            Case 1 : v = x + a
                            Case 2 : v = x + b
                            Case 3 : v = x + (a + b) \ 2
                            Case 4 : v = x + Paeth(a, b, c)
                            Case Else : Throw New InvalidDataException($"PNG 濾波類型 {f} 無效。")
                        End Select
                        pixels(o + i) = CByte(v And &HFF)
                    Next
                    Buffer.BlockCopy(pixels, o, prev, 0, stride)
                Next
            End Using
            png.Pixels = pixels
        End Using
        Return png
    End Function

    Private Shared Function ChannelCount(colorType As Integer) As Integer
        Select Case colorType
            Case 0, 3 : Return 1
            Case 2 : Return 3
            Case 4 : Return 2
            Case 6 : Return 4
            Case Else : Throw New InvalidDataException($"PNG 色彩類型 {colorType} 無效。")
        End Select
    End Function

    Private Shared Function ToRgba(png As RawPng) As RgbaImage
        Dim img As New RgbaImage(png.Width, png.Height)
        Dim d = img.Pixels
        Dim channels = ChannelCount(png.ColorType)
        Dim stride = (png.Width * channels * png.BitDepth + 7) \ 8
        Dim maxV = (1 << png.BitDepth) - 1
        For y = 0 To png.Height - 1
            Dim row = y * stride
            For x = 0 To png.Width - 1
                Dim o = (y * png.Width + x) * 4
                Dim r, g, b As Integer, a = 255
                Select Case png.ColorType
                    Case 0
                        Dim v = Sample(png.Pixels, row, x, png.BitDepth)
                        r = Scale8(v, maxV) : g = r : b = r
                        If v = png.TransparentGray Then a = 0
                    Case 3
                        Dim idx = Sample(png.Pixels, row, x, png.BitDepth)
                        If png.Palette IsNot Nothing AndAlso idx * 3 + 2 < png.Palette.Length Then
                            r = png.Palette(idx * 3) : g = png.Palette(idx * 3 + 1) : b = png.Palette(idx * 3 + 2)
                        End If
                        If png.PaletteAlpha IsNot Nothing AndAlso idx < png.PaletteAlpha.Length Then a = png.PaletteAlpha(idx)
                    Case Else
                        Dim vs(channels - 1) As Integer
                        For c = 0 To channels - 1
                            vs(c) = Sample(png.Pixels, row, x * channels + c, png.BitDepth)
                        Next
                        Select Case png.ColorType
                            Case 2
                                r = Scale8(vs(0), maxV) : g = Scale8(vs(1), maxV) : b = Scale8(vs(2), maxV)
                                If png.TransparentRgb IsNot Nothing AndAlso vs(0) = png.TransparentRgb(0) AndAlso vs(1) = png.TransparentRgb(1) AndAlso vs(2) = png.TransparentRgb(2) Then a = 0
                            Case 4
                                r = Scale8(vs(0), maxV) : g = r : b = r : a = Scale8(vs(1), maxV)
                            Case Else
                                r = Scale8(vs(0), maxV) : g = Scale8(vs(1), maxV) : b = Scale8(vs(2), maxV) : a = Scale8(vs(3), maxV)
                        End Select
                End Select
                d(o) = CByte(r) : d(o + 1) = CByte(g) : d(o + 2) = CByte(b) : d(o + 3) = CByte(a)
            Next
        Next
        Return img
    End Function

    ' 第 index 個樣本（以位元深度計）。
    Private Shared Function Sample(p As Byte(), row As Integer, index As Integer, depth As Integer) As Integer
        Select Case depth
            Case 8 : Return p(row + index)
            Case 16 : Return (CInt(p(row + index * 2)) << 8) Or p(row + index * 2 + 1)
            Case Else
                Dim bit = index * depth
                Dim by = p(row + bit \ 8)
                Dim shift = 8 - depth - (bit Mod 8)
                Return (by >> shift) And ((1 << depth) - 1)
        End Select
    End Function

    Private Shared Function Scale8(v As Integer, maxV As Integer) As Integer
        If maxV = 255 Then Return v
        Return CInt(Math.Round(v * 255.0 / maxV))
    End Function

    Private Shared Function Paeth(a As Integer, b As Integer, c As Integer) As Integer
        Dim p = a + b - c
        Dim pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c)
        If pa <= pb AndAlso pa <= pc Then Return a
        If pb <= pc Then Return b
        Return c
    End Function

    Private Shared Sub ReadExact(s As Stream, buf As Byte(), count As Integer)
        Dim read = 0
        While read < count
            Dim n = s.Read(buf, read, count - read)
            If n <= 0 Then Throw New EndOfStreamException("PNG 檔案不完整。")
            read += n
        End While
    End Sub
End Class

Friend NotInheritable Class Crc32
    Private Sub New()
    End Sub

    Private Shared ReadOnly Table As UInteger() = BuildTable()

    Private Shared Function BuildTable() As UInteger()
        Dim t(255) As UInteger
        For n = 0 To 255
            Dim c = CUInt(n)
            For k = 0 To 7
                c = If((c And 1UI) <> 0, &HEDB88320UI Xor (c >> 1), c >> 1)
            Next
            t(n) = c
        Next
        Return t
    End Function

    Public Shared Function Update(crc As UInteger, data As Byte(), offset As Integer, count As Integer) As UInteger
        For i = offset To offset + count - 1
            crc = Table(CInt((crc Xor data(i)) And &HFFUI)) Xor (crc >> 8)
        Next
        Return crc
    End Function
End Class
