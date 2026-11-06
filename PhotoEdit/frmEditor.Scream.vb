Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>
''' 影像 → 吶喊（孟克）…：用目前看到的樣子做人物，臉變成《吶喊》的臉、去背放到背景上。
''' 套用後背景存成新影像（圖片\PhotoEdit\吶喊）並打開，人物放成一個圖片圖層（可以拖曳、縮放、調透明度），原照片不變。
''' </summary>
Partial Friend Class frmEditor

    Private Shared _screamFolder As String

    ''' <summary>結果存放的資料夾（測試時可以改到暫存區）。</summary>
    Friend Shared Property ScreamFolder As String
        Get
            Return If(_screamFolder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", Lang.T("吶喊")))
        End Get
        Set(value As String)
            _screamFolder = value
        End Set
    End Property

    Private Sub ShowScream()
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        Dim source As Bitmap
        Try
            source = RenderMerged(_recipe)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            MessageBox.Show(Me, Lang.T("無法取得目前的影像：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Dim baseName = Path.GetFileNameWithoutExtension(_photo.Path)
        Using source
            Using dlg As New frmScream(source, _help)
                If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.ResultBackdrop Is Nothing Then Return
                OpenScreamResult(dlg, baseName)
            End Using
        End Using
    End Sub

    ''' <summary>把吶喊視窗的結果存檔、背景開成新影像、人物加成圖層（測試也用這個）。</summary>
    Friend Sub OpenScreamResult(dlg As frmScream, baseName As String)
        Dim bgFile, layerFile As String
        Try
            Directory.CreateDirectory(ScreamFolder)
            Dim name = baseName & "_" & Lang.T("吶喊")
            For Each c In Path.GetInvalidFileNameChars()
                name = name.Replace(c, "_"c)
            Next
            bgFile = Path.Combine(ScreamFolder, name & ".jpg")
            Dim n = 2
            While File.Exists(bgFile)
                bgFile = Path.Combine(ScreamFolder, $"{name}_{n}.jpg")
                n += 1
            End While
            layerFile = Path.ChangeExtension(bgFile, Nothing) & "_" & Lang.T("人物") & ".png"
            dlg.ResultBackdrop.Save(bgFile, ImageFormat.Jpeg)
            dlg.ResultLayer.Save(layerFile, ImageFormat.Png)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Runtime.InteropServices.ExternalException
            MessageBox.Show(Me, Lang.T("無法儲存吶喊的檔案：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Finally
            dlg.ResultBackdrop.Dispose()
            dlg.ResultLayer.Dispose()
        End Try
        OpenPhoto(bgFile, remember:=False)
        If _photo Is Nothing OrElse Not String.Equals(_photo.Path, bgFile, StringComparison.OrdinalIgnoreCase) Then Return
        Dim o As New Overlay With {.Kind = OverlayKind.Image, .ImagePath = layerFile, .Shadow = False,
                                   .X = dlg.ResultX, .Y = dlg.ResultY, .Size = Math.Max(0.02, dlg.ResultSize)}
        ApplyChange(Sub(rc)
                        If rc.Overlays Is Nothing Then rc.Overlays = New List(Of Overlay)()
                        rc.Overlays.Add(o)
                    End Sub)
        SetStatusMessage(Lang.T("吶喊：背景已存成 ") & bgFile & Lang.T("，人物是一個圖層，可以拖曳、縮放。"))
    End Sub
End Class
