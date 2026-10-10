Imports OpenCvSharp

''' <summary>
''' 影片輸出（變形動畫、川劇變臉共用）：一串「畫一格的函式＋這格停多久（秒）」寫成 MP4 或 GIF。
''' MP4：H.264 優先（程式資料夾有 Cisco OpenH264 時檔案小；沒有時 OpenCV 改用系統內建的 H.264），都不行用 MPEG-4。
''' GIF：GifWriter（全片共用 256 色調色盤，從頭到尾取 5 格算）。
''' </summary>
Public NotInheritable Class VideoExport
    Private Sub New()
    End Sub

    ''' <summary>寫 MP4；frames 每一項的 Render 回傳 BGR、w×h 的一格（寫完會釋放）。回傳用的編碼。</summary>
    Public Shared Function WriteMp4(path As String, w As Integer, h As Integer, fps As Integer, frames As IList(Of (Render As Func(Of Mat), Duration As Double)),
                                    Optional progress As Action(Of Integer, Integer) = Nothing, Optional ct As Threading.CancellationToken = Nothing) As String
        EnsureOpenH264Path()
        Dim used As String = Nothing
        Dim writer As VideoWriter = Nothing
        For Each codec In {"avc1", "mp4v"}
            Dim vw As New VideoWriter(path, FourCC.FromString(codec), fps, New Size(w, h), True)
            If vw.IsOpened() Then
                writer = vw : used = codec : Exit For
            End If
            vw.Dispose()
        Next
        If writer Is Nothing Then Throw New InvalidOperationException("無法建立 MP4（找不到可用的影片編碼器）。")
        Using writer
            For i = 0 To frames.Count - 1
                ct.ThrowIfCancellationRequested()
                Using f = frames(i).Render()
                    Dim reps = Math.Max(1, CInt(Math.Round(frames(i).Duration * fps)))
                    For r = 1 To reps
                        writer.Write(f)
                    Next
                End Using
                progress?.Invoke(i + 1, frames.Count)
            Next
        End Using
        Return used
    End Function

    ''' <summary>寫 GIF（每格縮到 gw×gh；Duration 換成 1/100 秒的延遲）。</summary>
    Public Shared Sub WriteGif(path As String, gw As Integer, gh As Integer, frames As IList(Of (Render As Func(Of Mat), Duration As Double)),
                               Optional progress As Action(Of Integer, Integer) = Nothing, Optional ct As Threading.CancellationToken = Nothing)
        Dim small = Function(i As Integer) As Mat
                        Using f = frames(i).Render()
                            Dim s As New Mat()
                            Cv2.Resize(f, s, New Size(gw, gh), 0, 0, InterpolationFlags.Area)
                            Return s
                        End Using
                    End Function
        ' 調色盤：從頭到尾平均取 5 格
        Dim samples As New List(Of Mat)
        For k = 0 To 4
            samples.Add(small(Math.Min(frames.Count - 1, CInt(Math.Round(k * (frames.Count - 1) / 4.0)))))
        Next
        Dim palette = GifWriter.BuildPalette(samples)
        For Each s In samples
            s.Dispose()
        Next
        Using gif As New GifWriter(path, gw, gh, palette)
            For i = 0 To frames.Count - 1
                ct.ThrowIfCancellationRequested()
                Using s = small(i)
                    gif.AddFrame(s, CInt(Math.Round(frames(i).Duration * 100)))
                End Using
                progress?.Invoke(i + 1, frames.Count)
            Next
        End Using
    End Sub

    ''' <summary>GIF 的大小：長邊縮到 side 以內。</summary>
    Public Shared Function GifSize(w As Integer, h As Integer, side As Integer) As (W As Integer, H As Integer)
        Dim k = Math.Min(1.0, side / Math.Max(w, h))
        Return (Math.Max(1, CInt(w * k)), Math.Max(1, CInt(h * k)))
    End Function

    ''' <summary>OpenCV 的 FFmpeg 用 LoadLibrary 找 OpenH264：用 dotnet 主機執行時程式資料夾不在搜尋路徑裡，加到 PATH。</summary>
    Private Shared Sub EnsureOpenH264Path()
        Dim dir = AppContext.BaseDirectory.TrimEnd(IO.Path.DirectorySeparatorChar)
        If Not IO.File.Exists(IO.Path.Combine(dir, "openh264-1.8.0-win64.dll")) Then Return
        Dim p = If(Environment.GetEnvironmentVariable("PATH"), "")
        If p.Split(IO.Path.PathSeparator).Any(Function(x) String.Equals(x.TrimEnd(IO.Path.DirectorySeparatorChar), dir, StringComparison.OrdinalIgnoreCase)) Then Return
        Environment.SetEnvironmentVariable("PATH", dir & IO.Path.PathSeparator & p)
    End Sub
End Class
