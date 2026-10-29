Imports PhotoEdit

' 選取 → 打馬賽克…／平滑馬賽克（反馬賽克）…／安全遮蔽（實心色塊）：各加成一個點陣圖層（原照片不變）。
' 修補頁的「JPEG 區塊」滑桿：第一次調整時從原圖偵測 8×8 格線的起點。算圖在 PhotoEdit.Core\MosaicEffect.vb。
Partial Class frmEditor

    Private _jpegGrid As (X As Integer, Y As Integer, Strength As Double)?
    Private _jpegGridPhoto As PhotoFile

    ''' <summary>原圖 JPEG 格線的起點（每張照片偵測一次）。</summary>
    Private Function JpegGrid() As (X As Integer, Y As Integer, Strength As Double)
        If _photo Is Nothing Then Return (0, 0, 1)
        If Not _jpegGrid.HasValue OrElse _jpegGridPhoto IsNot _photo Then
            SyncLock _sourceLock
                _jpegGrid = MosaicEffect.DetectJpegGrid(_photo.Image)
            End SyncLock
            _jpegGridPhoto = _photo
        End If
        Return _jpegGrid.Value
    End Function

    ''' <summary>選取範圍（沒有選取時整張）。</summary>
    Private Function SelectionOrAll() As SelectionSpec
        If HasSelection Then Return _recipe.Selection.Clone()
        Dim all As New SelectionSpec()
        all.Apply(New SelectionOp With {.Mode = SelectionMode.Replace, .Shape = SelectionShape.All})
        Return all
    End Function

    ''' <summary>打馬賽克（smooth = False）或平滑馬賽克（True）。</summary>
    Private Sub ShowMosaic(smooth As Boolean)
        If _photo Is Nothing Then Return
        Dim region = SelectionOrAll()
        Dim flat = _recipe.Clone()
        flat.Frame = PhotoFrameStyle.None
        Dim source As Bitmap
        Try
            source = RenderMerged(flat)
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            MessageBox.Show(Me, Lang.T("無法取得目前的影像：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try
        Using source
            Dim w = source.Width, h = source.Height
            Dim mask = SelectionMask.Render(region, w, h)
            Dim b = SelectionMask.Bounds(region, w, h)
            Dim area = Rectangle.FromLTRB(CInt(Math.Floor(b.Left * w)), CInt(Math.Floor(b.Top * h)), CInt(Math.Ceiling(b.Right * w)), CInt(Math.Ceiling(b.Bottom * h)))
            area.Intersect(New Rectangle(0, 0, w, h))
            If area.Width < 2 OrElse area.Height < 2 Then SetStatusMessage(Lang.T("選取區是空的。")) : Return
            Using dlg As New frmMosaic(source, mask, area, smooth, _help)
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                AddMosaicLayer(region, h, smooth, dlg.BlockSize, If(smooth, dlg.OffsetX, area.X), If(smooth, dlg.OffsetY, area.Y), dlg.Method)
            End Using
        End Using
    End Sub

    ''' <summary>加一個馬賽克／平滑馬賽克圖層：大小與格線起點（像素）換成照片高度 h 的比例。</summary>
    Friend Sub AddMosaicLayer(region As SelectionSpec, h As Integer, smooth As Boolean, blockSize As Integer, gridX As Integer, gridY As Integer, method As MosaicSmoothMethod)
        Dim hh = CDbl(h)
        Dim op As New DrawLayer With {.Shape = DrawShape.Raster, .Region = region, .Filled = True, .Stroked = False,
                                      .FillContent = If(smooth, FillContent.Demosaic, FillContent.Mosaic),
                                      .MosaicSize = Math.Round(blockSize / hh, 6), .MosaicX = Math.Round(gridX / hh, 6), .MosaicY = Math.Round(gridY / hh, 6),
                                      .MosaicMethod = CInt(method), .Param1 = Math.Round(PhotoAspect(), 5), .Seed = _drawRandom.Next(1, 100000)}
        AddRegionLayer(If(smooth, Lang.T("平滑馬賽克"), Lang.T("馬賽克")), op)
        SetStatusMessage(If(smooth, Lang.T("已平滑馬賽克（新的點陣圖層；方塊裡原本的細節不會回來）。"),
                            Lang.F("已打馬賽克（方塊 {0} 像素，新的點陣圖層，可以隱藏或刪除）。", blockSize)))
    End Sub

    ''' <summary>安全遮蔽：選取範圍蓋上不透明的實心黑塊（邊緣不羽化，往外多蓋 1 像素）。</summary>
    Private Sub Redact()
        If Not RequireSelection() Then Return
        Dim op As New DrawLayer With {.Shape = DrawShape.Raster, .Region = _recipe.Selection.Clone(), .Filled = True, .Stroked = False,
                                      .FillContent = FillContent.Redact, .FillColorArgb = Color.Black.ToArgb(), .Opacity = 100,
                                      .Param1 = Math.Round(PhotoAspect(), 5), .Seed = _drawRandom.Next(1, 100000)}
        AddRegionLayer(Lang.T("安全遮蔽"), op)
        SetStatusMessage(Lang.T("已用實心色塊蓋住選取範圍（新的點陣圖層）。匯出成 JPG／PNG 後底下的內容就不存在了；存成 .pedx 時原圖還在檔案裡。"))
    End Sub
End Class
