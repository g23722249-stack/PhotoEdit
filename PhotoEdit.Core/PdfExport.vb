Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.IO.Compression
Imports System.Text

''' <summary>
''' 匯出 PDF：把合成好的整張圖放進一頁 PDF。頁面大小依影像解析度（像素 ÷ DPI × 72 點），
''' 影像用 Flate 無失真壓縮；有透明的地方另存成 SMask，在 PDF 裡一樣是透明的。
''' 不需要外部元件，Acrobat、瀏覽器、印表機都能開。
''' </summary>
Public NotInheritable Class PdfExport
    Private Sub New()
    End Sub

    ''' <summary>寫出 PDF。dpi 小於等於 0 時用 72（一個像素一點）。</summary>
    Public Shared Sub WriteImage(path As String, image As Bitmap, dpi As Double, Optional title As String = Nothing)
        Using fs As New FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)
            WriteImage(fs, image, dpi, title)
        End Using
    End Sub

    Public Shared Sub WriteImage(output As Stream, image As Bitmap, dpi As Double, Optional title As String = Nothing)
        If dpi <= 0 Then dpi = 72
        Dim w = image.Width, h = image.Height
        Dim rgb(w * h * 3 - 1) As Byte, alpha(w * h - 1) As Byte
        Dim hasAlpha = False
        Using bmp32 As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp32)
                g.DrawImage(image, 0, 0, w, h)
            End Using
            Dim d = bmp32.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Dim px(d.Stride * h - 1) As Byte
            Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length)
            bmp32.UnlockBits(d)
            For y = 0 To h - 1
                For x = 0 To w - 1
                    Dim s = y * d.Stride + x * 4, i = y * w + x
                    rgb(i * 3) = px(s + 2) : rgb(i * 3 + 1) = px(s + 1) : rgb(i * 3 + 2) = px(s)
                    alpha(i) = px(s + 3)
                    If px(s + 3) < 255 Then hasAlpha = True
                Next
            Next
        End Using
        Dim pageW = w / dpi * 72, pageH = h / dpi * 72
        Dim objs As New List(Of Byte())()
        ' 1 Catalog、2 Pages、3 Page、4 內容、5 影像、6 SMask（有透明時）、最後 Info
        Dim imgObj = 5, maskObj = If(hasAlpha, 6, 0), infoObj = If(hasAlpha, 7, 6)
        objs.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"))
        objs.Add(Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"))
        objs.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(pageW)} {N(pageH)}] /Resources << /XObject << /Im1 {imgObj} 0 R >> >> /Contents 4 0 R >>"))
        Dim content = Ascii($"q {N(pageW)} 0 0 {N(pageH)} 0 0 cm /Im1 Do Q")
        objs.Add(StreamObj($"<< /Length {content.Length} >>", content))
        Dim zrgb = Deflate(rgb)
        objs.Add(StreamObj($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode{If(hasAlpha, $" /SMask {maskObj} 0 R", "")} /Length {zrgb.Length} >>", zrgb))
        If hasAlpha Then
            Dim za = Deflate(alpha)
            objs.Add(StreamObj($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length {za.Length} >>", za))
        End If
        objs.Add(Ascii($"<< /Producer (PhotoEdit) /Title {PdfText(If(title, ""))} /CreationDate (D:{Date.Now:yyyyMMddHHmmss}) >>"))

        Dim offsets As New List(Of Long)()
        Dim write = Sub(b As Byte()) output.Write(b, 0, b.Length)
        Dim start = output.Position
        write(Ascii("%PDF-1.4" & vbLf))
        write(New Byte() {AscW("%"c), &HE2, &HE3, &HCF, &HD3, 10}) ' 二進位標記：讓傳輸工具把檔案當成二進位
        For k = 0 To objs.Count - 1
            offsets.Add(output.Position - start)
            write(Ascii($"{k + 1} 0 obj" & vbLf))
            write(objs(k))
            write(Ascii(vbLf & "endobj" & vbLf))
        Next
        Dim xref = output.Position - start
        Dim sb As New StringBuilder()
        sb.Append($"xref{vbLf}0 {objs.Count + 1}{vbLf}0000000000 65535 f " & vbLf)
        For Each o In offsets
            sb.Append(o.ToString("0000000000") & " 00000 n " & vbLf)
        Next
        sb.Append($"trailer{vbLf}<< /Size {objs.Count + 1} /Root 1 0 R /Info {infoObj} 0 R >>{vbLf}startxref{vbLf}{xref}{vbLf}%%EOF{vbLf}")
        write(Ascii(sb.ToString()))
    End Sub

    Private Shared Function StreamObj(dict As String, data As Byte()) As Byte()
        Using ms As New MemoryStream()
            Dim a = Ascii(dict & vbLf & "stream" & vbLf)
            ms.Write(a, 0, a.Length)
            ms.Write(data, 0, data.Length)
            Dim b = Ascii(vbLf & "endstream")
            ms.Write(b, 0, b.Length)
            Return ms.ToArray()
        End Using
    End Function

    Private Shared Function Deflate(data As Byte()) As Byte()
        Using ms As New MemoryStream()
            Using z As New ZLibStream(ms, CompressionLevel.Optimal, leaveOpen:=True)
                z.Write(data, 0, data.Length)
            End Using
            Return ms.ToArray()
        End Using
    End Function

    Private Shared Function Ascii(s As String) As Byte()
        Return Encoding.ASCII.GetBytes(s)
    End Function

    Private Shared Function N(v As Double) As String
        Return Math.Round(v, 3).ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>PDF 文字字串：UTF-16BE 加 BOM 的十六進位字串（中文標題也能正確顯示）。</summary>
    Private Shared Function PdfText(s As String) As String
        Dim b = Encoding.BigEndianUnicode.GetBytes(s)
        Return "<FEFF" & String.Concat(b.Select(Function(x) x.ToString("X2"))) & ">"
    End Function
End Class
