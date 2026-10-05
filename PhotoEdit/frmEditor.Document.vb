Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text.Json
Imports PhotoEdit

''' <summary>
''' 檔案：存檔（Ctrl+S，專案檔 .pedx）、另存新檔（.pedx 或 PNG／JPG／BMP；有圖層時先提示會合併）、
''' 匯出（PNG／JPG／BMP）、載入專案；編輯：複製（Ctrl+C，合併所有圖層的全尺寸影像放到剪貼簿，畫面不變）。
''' 存成平面圖片後視窗仍是原本的文件，圖層都還在，可以繼續修改。
''' </summary>
Partial Friend Class frmEditor

    ''' <summary>目前文件的專案檔路徑；一般照片、新影像還沒存成專案時為 Nothing。</summary>
    Private _docPath As String
    ''' <summary>載入專案時解開原圖、遮罩、貼圖副本的暫存資料夾；換文件或關閉時刪除。</summary>
    Private _projectWorkDir As String

    Private Const ProjectFilter As String = "PhotoEdit 專案（保留圖層，可繼續修改）|*" & ProjectFile.Extension
    Private Const ImageFilter As String = "PNG 圖片（保留透明）|*.png|JPEG 圖片|*.jpg|BMP 圖片|*.bmp"
    Private Shared ReadOnly ImageExtensions As String() = {".png", ".jpg", ".bmp"}

    ''' <summary>標題列與提示用的文件名稱。</summary>
    Private ReadOnly Property DocumentName As String
        Get
            Return Path.GetFileName(If(_docPath, _photo?.Path))
        End Get
    End Property

    ''' <summary>建議存檔位置與檔名的依據：專案檔，或原照片。</summary>
    Private ReadOnly Property DocumentBasePath As String
        Get
            Return If(_docPath, _photo?.Path)
        End Get
    End Property

    Private Sub SetDocumentPath(docPath As String, workDir As String)
        _docPath = docPath
        If Not String.Equals(workDir, _projectWorkDir, StringComparison.OrdinalIgnoreCase) Then
            DeleteProjectWorkDir()
            _projectWorkDir = workDir
        End If
    End Sub

    Private Sub DeleteProjectWorkDir()
        If _projectWorkDir Is Nothing Then Return
        TryDeleteDirectory(_projectWorkDir)
        _projectWorkDir = Nothing
    End Sub

    Private Shared Sub TryDeleteDirectory(dir As String)
        Try
            If Directory.Exists(dir) Then Directory.Delete(dir, recursive:=True)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            ' 暫存檔刪不掉沒關係，下次開專案時會清掉舊的。
        End Try
    End Sub

    Private Shared ReadOnly Property ProjectTempRoot As String
        Get
            Return Path.Combine(Path.GetTempPath(), "PhotoEdit", "專案")
        End Get
    End Property

    ''' <summary>圖層數（文字、貼圖、繪圖圖層）。</summary>
    Private ReadOnly Property MergeLayerCount As Integer
        Get
            Return If(_recipe.Overlays?.Count, 0) + If(_recipe.Drawings?.Count, 0)
        End Get
    End Property

    '---------------------------------------------------------------------
    ' 載入專案
    '---------------------------------------------------------------------

    Private Sub OpenProject(projectPath As String)
        If Not ConfirmDiscard() Then Return
        CleanOldWorkDirs()
        Dim workDir = Path.Combine(ProjectTempRoot, Guid.NewGuid().ToString("N"))
        Dim loaded As LoadedProject
        Dim photo As PhotoFile
        Cursor = Cursors.WaitCursor
        Try
            loaded = ProjectFile.Load(projectPath, workDir)
            photo = PhotoFile.Open(loaded.PhotoPath)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is InvalidDataException OrElse TypeOf ex Is JsonException OrElse
                                   TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse
                                   TypeOf ex Is ExternalException
            TryDeleteDirectory(workDir)
            MessageBox.Show(Me, $"無法載入「{Path.GetFileName(projectPath)}」：{ex.Message}", AppName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Finally
            Cursor = Cursors.Default
        End Try
        StickerLibrary.ClearCache() ' 貼圖副本換了，讓快取重新找檔
        ShowDocument(photo, loaded.Recipe, projectPath, workDir)
        SetStatusMessage("已載入專案：" & projectPath)
    End Sub

    ''' <summary>清掉上次當機等原因留下、超過兩天的暫存資料夾。</summary>
    Private Sub CleanOldWorkDirs()
        Try
            If Not Directory.Exists(ProjectTempRoot) Then Return
            For Each d In Directory.GetDirectories(ProjectTempRoot)
                If String.Equals(d, _projectWorkDir, StringComparison.OrdinalIgnoreCase) Then Continue For
                If Directory.GetLastWriteTime(d) < DateTime.Now.AddDays(-2) Then TryDeleteDirectory(d)
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub

    '---------------------------------------------------------------------
    ' 存檔
    '---------------------------------------------------------------------

    ''' <summary>存檔（Ctrl+S）：已有專案檔就覆寫，否則選位置存成專案。回傳 False 表示沒有存（取消或失敗）。</summary>
    Private Function SaveDocument() As Boolean
        If _photo Is Nothing Then Return False
        If _docPath Is Nothing Then Return SaveProjectWithDialog()
        Return WriteProject(_docPath)
    End Function

    Private Function SaveProjectWithDialog() As Boolean
        Using dlg As New SaveFileDialog()
            dlg.Title = "存檔"
            dlg.Filter = ProjectFilter
            SetDialogDefaults(dlg, ProjectFile.Extension)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return False
            Return WriteProject(dlg.FileName)
        End Using
    End Function

    ''' <summary>另存新檔：專案（保留圖層）或 PNG／JPG／BMP（合併圖層，視窗裡仍可繼續編輯）。</summary>
    Private Sub SaveAsWithDialog()
        Using dlg As New SaveFileDialog()
            dlg.Title = "另存新檔"
            dlg.Filter = ProjectFilter & "|" & ImageFilter
            SetDialogDefaults(dlg, ProjectFile.Extension)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim file = WithFilterExtension(dlg.FileName, dlg.FilterIndex, {ProjectFile.Extension}.Concat(ImageExtensions).ToArray())
            If ProjectFile.IsProject(file) Then
                WriteProject(file)
            Else
                SaveFlatImage(file, confirmMerge:=True)
            End If
        End Using
    End Sub

    ''' <summary>匯出（Ctrl+E）：存一份合併後的 PNG／JPG／BMP，不改變目前的文件。</summary>
    Private Sub ExportWithDialog()
        ExitCropMode(apply:=True)
        ' 去背成透明背景、或裁成圓形等形狀時預設存 PNG（JPG 不能透明）。
        Dim transparent = (_recipe.Cutout IsNot Nothing AndAlso _recipe.Cutout.Background = CutoutBackground.Transparent) OrElse
                          _recipe.CropShape <> CropShape.Rectangle
        Dim ext = If(transparent, ".png", ".jpg")
        Using dlg As New SaveFileDialog()
            dlg.Title = "匯出"
            dlg.Filter = ImageFilter
            Dim suggested = PhotoFile.SuggestExportPath(DocumentBasePath, ext)
            dlg.InitialDirectory = Path.GetDirectoryName(suggested)
            dlg.FileName = Path.GetFileName(suggested)
            dlg.FilterIndex = Array.IndexOf(ImageExtensions, ext) + 1
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            SaveFlatImage(WithFilterExtension(dlg.FileName, dlg.FilterIndex, ImageExtensions), confirmMerge:=False)
        End Using
    End Sub

    ''' <summary>預設位置：專案檔或原照片的資料夾，檔名同名、換副檔名。</summary>
    Private Sub SetDialogDefaults(dlg As SaveFileDialog, extension As String)
        Dim basePath = DocumentBasePath
        dlg.InitialDirectory = Path.GetDirectoryName(basePath)
        dlg.FileName = Path.GetFileNameWithoutExtension(basePath) & extension
        dlg.FilterIndex = 1
        dlg.OverwritePrompt = True
    End Sub

    ''' <summary>檔名的副檔名不是可存的格式時（例如自己打了 .jpeg 以外的東西），補上所選類型的副檔名。</summary>
    Private Shared Function WithFilterExtension(file As String, filterIndex As Integer, extensions As String()) As String
        Dim ext = Path.GetExtension(file).ToLowerInvariant()
        If ext = ".jpeg" Then Return file
        If extensions.Contains(ext) Then Return file
        Return file & extensions(Math.Max(0, Math.Min(extensions.Length - 1, filterIndex - 1)))
    End Function

    ''' <summary>寫入專案檔（原圖、配方、去背遮罩、用到的外部圖片）。之後 Ctrl+S 就存到這裡。</summary>
    Private Function WriteProject(projectPath As String) As Boolean
        ExitCropMode(apply:=True)
        CommitCalloutEditor()
        Dim masks As New Dictionary(Of CutoutModel, Bitmap)()
        If _aiMask IsNot Nothing AndAlso _recipe.Cutout IsNot Nothing Then masks(_aiMaskModel) = _aiMask
        Cursor = Cursors.WaitCursor
        Try
            ProjectFile.Save(projectPath, Path.GetFileName(_photo.Path), _photo.OriginalBytes, _recipe, masks)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is ExternalException
            MessageBox.Show(Me, "無法存檔：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        Finally
            Cursor = Cursors.Default
        End Try
        _docPath = projectPath
        _savedRecipe = _recipe.Clone()
        _aiMaskDirty = False
        UpdateTitle()
        SetStatusMessage("已存檔：" & projectPath)
        Return True
    End Function

    ''' <summary>以全尺寸合併所有圖層存成 PNG／JPG／BMP。confirmMerge：有圖層時先提示會合併。</summary>
    Private Sub SaveFlatImage(file As String, confirmMerge As Boolean)
        ExitCropMode(apply:=True)
        CommitCalloutEditor()
        Dim format = Path.GetExtension(file).TrimStart("."c).ToUpperInvariant()
        If confirmMerge AndAlso MergeLayerCount > 0 Then
            Dim answer = MessageBox.Show(Me,
                $"目前有 {MergeLayerCount} 個圖層（文字、貼圖、繪圖）。存成 {format} 會把所有圖層合併成一張圖。" & vbCrLf & vbCrLf &
                "視窗裡的圖層會保留，可以繼續修改；要保留圖層下次接著改，請另存成 PhotoEdit 專案（.pedx）。" & vbCrLf & vbCrLf &
                "要合併並存檔嗎？",
                AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Information)
            If answer <> DialogResult.OK Then Return
        End If
        Cursor = Cursors.WaitCursor
        Try
            SyncLock _sourceLock
                _photo.Export(_recipe, file, prepare:=SourcePrepare(_recipe), faces:=_faces)
            End SyncLock
            SetStatusMessage($"已存成 {format}：{file}" & If(MergeLayerCount > 0, "（圖層已合併到檔案裡；視窗裡仍可繼續編輯）", ""))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ExternalException
            MessageBox.Show(Me, "存檔失敗：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    '---------------------------------------------------------------------
    ' 複製
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 複製（Ctrl+C）：以全尺寸合併所有圖層（調色、裁切、邊框、文字、貼圖、繪圖）放到剪貼簿。
    ''' 同時放 PNG（保留透明）與一般點陣圖（透明處為白色），畫面與文件都不變。
    ''' </summary>
    Private Sub CopyMergedImage()
        If HasSelection Then CopySelection() : Return ' 有選取區時只複製選取區
        Dim recipe = _recipe.Clone()
        Cursor = Cursors.WaitCursor
        Try
            Using merged = RenderMerged(recipe), opaque As New Bitmap(merged.Width, merged.Height, PixelFormat.Format24bppRgb), png As New MemoryStream()
                Using g = Graphics.FromImage(opaque)
                    g.Clear(Color.White)
                    g.DrawImage(merged, 0, 0, merged.Width, merged.Height)
                End Using
                merged.Save(png, ImageFormat.Png)
                Dim data As New DataObject()
                data.SetData("PNG", False, png)
                data.SetData(DataFormats.Bitmap, True, opaque)
                Clipboard.SetDataObject(data, True, 5, 100)
                SetStatusMessage($"已複製合併後的影像（{merged.Width} × {merged.Height}）到剪貼簿。")
            End Using
        Catch ex As Exception When TypeOf ex Is ExternalException OrElse TypeOf ex Is OutOfMemoryException OrElse
                                   TypeOf ex Is ArgumentException
            MessageBox.Show(Me, "無法複製到剪貼簿：" & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    ''' <summary>以原圖全尺寸算出合併所有圖層的結果（和匯出相同）。</summary>
    Private Function RenderMerged(recipe As EditRecipe) As Bitmap
        SyncLock _sourceLock
            Return ImagePipeline.Render(_photo.Image, recipe, prepare:=SourcePrepare(recipe), faces:=_faces)
        End SyncLock
    End Function
End Class
