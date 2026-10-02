Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>
''' 編輯 → 貼成新影像（Ctrl+V）：剪貼簿的圖片存成「圖片\PhotoEdit\剪貼簿\貼上_日期時間.png」後開啟編輯
''' （編輯配方要依附在檔案上）。檔案總管複製的圖片檔則直接開啟該檔。
''' </summary>
Partial Friend Class frmEditor

    Private _pasteImageItem As Aqua.MenuItem
    Private Shared _pasteFolder As String

    ''' <summary>貼上的圖片存放處（預設「圖片\PhotoEdit\剪貼簿」；測試時可改）。</summary>
    Friend Shared Property PasteFolder As String
        Get
            Return If(_pasteFolder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", "剪貼簿"))
        End Get
        Set(value As String)
            _pasteFolder = value
        End Set
    End Property

    ''' <summary>剪貼簿裡有沒有可以貼上的圖片（選單開啟時用來決定是否可用）。</summary>
    Private Shared Function ClipboardHasImage() As Boolean
        Try
            Dim data = Clipboard.GetDataObject()
            Return data IsNot Nothing AndAlso (data.GetDataPresent(DataFormats.Bitmap) OrElse data.GetDataPresent("PNG") OrElse ImageFile(data) IsNot Nothing)
        Catch ex As Runtime.InteropServices.ExternalException
            Return False ' 剪貼簿被其他程式占用
        End Try
    End Function

    ''' <summary>檔案總管複製的第一個支援的圖片檔；沒有時回傳 Nothing。</summary>
    Private Shared Function ImageFile(data As IDataObject) As String
        Dim files = TryCast(data.GetData(DataFormats.FileDrop), String())
        Return files?.FirstOrDefault(Function(f) PhotoFile.IsSupported(f) AndAlso File.Exists(f))
    End Function

    ''' <summary>優先讀 PNG（保留透明），其次一般點陣圖；讀不到時回傳 Nothing。</summary>
    Private Shared Function ImageBitmap(data As IDataObject) As Bitmap
        If data.GetDataPresent("PNG") Then
            Dim stream = TryCast(data.GetData("PNG"), Stream)
            If stream IsNot Nothing Then
                Try
                    stream.Position = 0
                    Using img = Image.FromStream(stream)
                        Dim bmp As New Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb)
                        Using g = Graphics.FromImage(bmp)
                            g.DrawImage(img, 0, 0, img.Width, img.Height)
                        End Using
                        Return bmp
                    End Using
                Catch ex As ArgumentException
                    ' PNG 資料壞掉就改讀一般點陣圖。
                End Try
            End If
        End If
        Dim img2 = TryCast(data.GetData(DataFormats.Bitmap, True), Image)
        If img2 IsNot Nothing Then
            Using img2
                Return New Bitmap(img2)
            End Using
        End If
        Return Nothing
    End Function

    Private Sub PasteAsNewImage()
        Dim data As IDataObject
        Try
            data = Clipboard.GetDataObject()
        Catch ex As Runtime.InteropServices.ExternalException
            MessageBox.Show(Me, "剪貼簿正被其他程式使用，請稍後再試。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End Try
        PasteAsNewImage(data)
    End Sub

    Private Sub PasteAsNewImage(data As IDataObject)
        Dim file As String = Nothing
        Try
            If data IsNot Nothing Then file = ImageFile(data)
            If file Is Nothing AndAlso data IsNot Nothing Then
                Using bmp = ImageBitmap(data)
                    If bmp IsNot Nothing Then file = SaveClipboardImage(bmp)
                End Using
            End If
        Catch ex As Exception When TypeOf ex Is Runtime.InteropServices.ExternalException OrElse TypeOf ex Is IOException OrElse
                                   TypeOf ex Is UnauthorizedAccessException
            MessageBox.Show(Me, "無法從剪貼簿貼上：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        If file Is Nothing Then
            SetStatusMessage("剪貼簿裡沒有圖片。")
            Return
        End If
        Dim before = _photo?.Path
        OpenPhoto(file)
        If _photo IsNot Nothing AndAlso _photo.Path = file AndAlso file <> before Then
            SetStatusMessage(If(file.StartsWith(PasteFolder, StringComparison.OrdinalIgnoreCase),
                                "已從剪貼簿貼上，存成 " & file, "已開啟剪貼簿裡的檔案：" & file))
        End If
    End Sub

    ''' <summary>存成 PNG（保留透明），檔名「貼上_yyyyMMdd_HHmmss.png」，同一秒內重複時加序號。</summary>
    Private Shared Function SaveClipboardImage(bmp As Bitmap) As String
        Directory.CreateDirectory(PasteFolder)
        Dim name = "貼上_" & DateTime.Now.ToString("yyyyMMdd_HHmmss")
        Dim file = Path.Combine(PasteFolder, name & ".png")
        Dim n = 2
        While IO.File.Exists(file)
            file = Path.Combine(PasteFolder, $"{name}_{n}.png")
            n += 1
        End While
        bmp.Save(file, ImageFormat.Png)
        Return file
    End Function
End Class
