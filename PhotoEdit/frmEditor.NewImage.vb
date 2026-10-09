Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports PhotoEdit

''' <summary>
''' 檔案 → 新增（Ctrl+N）：用 frmNewImage 選大小、解析度、底色與底圖，存成「圖片\PhotoEdit\新影像\新影像_寬x高_日期時間.png」後開啟編輯
''' （編輯配方要依附在檔案上，和「貼成新影像」相同）。
''' </summary>
Partial Friend Class frmEditor

    Private Shared _newImageFolder As String

    ''' <summary>新影像存放處（預設「圖片\PhotoEdit\新影像」；測試時可改）。</summary>
    Friend Shared Property NewImageFolder As String
        Get
            Return If(_newImageFolder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", "新影像"))
        End Get
        Set(value As String)
            _newImageFolder = value
        End Set
    End Property

    Private Sub NewImageWithDialog()
        If Not ConfirmReplaceDocument() Then Return

        ' 使用中影像：大小用輸出大小（含裁切、邊框），當底圖時以全尺寸算圖。
        Dim currentSize As Size? = Nothing
        Dim currentPicture As Func(Of Bitmap) = Nothing
        If _photo IsNot Nothing AndAlso _rendered IsNot Nothing Then
            Dim scale = _photo.Image.Width / CDbl(_previewBase.Width)
            currentSize = New Size(CInt(Math.Round(_rendered.Width * scale)), CInt(Math.Round(_rendered.Height * scale)))
            Dim recipe = _recipe.Clone()
            currentPicture = Function()
                                 Cursor = Cursors.WaitCursor
                                 Try
                                     SyncLock _sourceLock
                                         Return ImagePipeline.Render(_photo.Image, recipe, prepare:=SourcePrepare(recipe), faces:=_faces)
                                     End SyncLock
                                 Finally
                                     Cursor = Cursors.Default
                                 End Try
                             End Function
        End If

        ' 剪貼簿：先讀一次知道大小；當底圖時再讀一份（對話框負責釋放）。
        Dim clipboardSize As Size? = Nothing
        Using clip = ReadClipboardBitmap()
            If clip IsNot Nothing Then clipboardSize = clip.Size
        End Using
        Dim clipboardPicture As Func(Of Bitmap) = If(clipboardSize.HasValue, New Func(Of Bitmap)(AddressOf ReadClipboardBitmap), Nothing)

        Dim file As String = Nothing
        Dim paperChoice As PaperKind? = Nothing
        Using dlg As New frmNewImage(currentSize, currentPicture, clipboardSize, clipboardPicture)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            paperChoice = dlg.PaperChoice
            Cursor = Cursors.WaitCursor
            Try
                Using bmp = dlg.CreateImage()
                    file = SaveNewImage(bmp)
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                       TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse
                                       TypeOf ex Is ExternalException
                MessageBox.Show(Me, "無法建立新影像：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            Finally
                Cursor = Cursors.Default
            End Try
        End Using

        ' 已在上面確認過是否儲存，這裡直接換成新影像。
        _savedRecipe = _recipe.Clone()
        _aiMaskDirty = False
        OpenPhoto(file, remember:=False)
        If _photo IsNot Nothing AndAlso _photo.Path = file Then
            If paperChoice.HasValue Then
                ' 紙張當底：文件套用這種紙（筆刷吃紙紋），並壓印淡淡的表面紋理
                Dim kind = paperChoice.Value
                ApplyChange(Sub(r) r.Paper = New PaperSettings With {.Kind = kind, .SurfaceStrength = 40, .SurfaceTarget = SurfaceTarget.Photo})
            End If
            SetStatusMessage($"已新增 {_photo.Image.Width} × {_photo.Image.Height} 的影像，存成 {file}" &
                             If(paperChoice.HasValue, $"（紙張：{Papers.Names(CInt(paperChoice.Value))}）", ""))
        End If
    End Sub

    ''' <summary>讀剪貼簿的圖片（PNG 優先，或檔案總管複製的圖片檔）；沒有時回傳 Nothing。</summary>
    Private Shared Function ReadClipboardBitmap() As Bitmap
        Try
            Dim data = Clipboard.GetDataObject()
            If data Is Nothing Then Return Nothing
            Dim file = ImageFile(data)
            If file IsNot Nothing Then
                Using photo = PhotoFile.Open(file)
                    Return DirectCast(photo.Image.Clone(), Bitmap)
                End Using
            End If
            Return ImageBitmap(data)
        Catch ex As Exception When TypeOf ex Is ExternalException OrElse TypeOf ex Is IOException OrElse
                                   TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse
                                   TypeOf ex Is OutOfMemoryException
            Return Nothing ' 剪貼簿被占用或圖片壞掉：當作沒有
        End Try
    End Function

    ''' <summary>存成 PNG（保留透明與解析度），檔名「新影像_寬x高_yyyyMMdd_HHmmss.png」，重複時加序號。</summary>
    Private Shared Function SaveNewImage(bmp As Bitmap) As String
        Directory.CreateDirectory(NewImageFolder)
        Dim name = $"新影像_{bmp.Width}x{bmp.Height}_{DateTime.Now:yyyyMMdd_HHmmss}"
        Dim file = Path.Combine(NewImageFolder, name & ".png")
        Dim n = 2
        While IO.File.Exists(file)
            file = Path.Combine(NewImageFolder, $"{name}_{n}.png")
            n += 1
        End While
        bmp.Save(file, ImageFormat.Png)
        Return file
    End Function
End Class
