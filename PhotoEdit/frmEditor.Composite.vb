Imports System.Drawing.Imaging
Imports System.IO
Imports PhotoEdit

''' <summary>
''' 影像 → 人物合成…：把主畫面目前的結果（縮小過）交給引導視窗，套用後把人物圖、遮罩、做好的圖層 PNG 存進合成資料夾，
''' 加成一個圖片物件（Overlay.Composite 記住來源與參數）。原照片不變，可以復原。
''' </summary>
Partial Friend Class frmEditor

    ''' <summary>引導視窗用的背景長邊上限。</summary>
    Private Const CompositeBackgroundSide As Integer = 1600

    Private Shared _compositeFolder As String

    ''' <summary>合成用的檔案放這裡（預設「圖片\PhotoEdit\合成」；測試時可以改到暫存區）。</summary>
    Friend Shared Property CompositeFolder As String
        Get
            Return If(_compositeFolder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", Lang.T("合成")))
        End Get
        Set(value As String)
            _compositeFolder = value
        End Set
    End Property

    ''' <summary>目前結果縮到長邊 CompositeBackgroundSide（引導視窗的背景）；recipe 不給時用目前的配方。</summary>
    Private Function CompositeBackground(Optional recipe As EditRecipe = Nothing) As Bitmap
        Using full = RenderMerged(If(recipe, _recipe))
            Dim k = Math.Min(1.0, CompositeBackgroundSide / CDbl(Math.Max(full.Width, full.Height)))
            Dim w = Math.Max(1, CInt(full.Width * k)), h = Math.Max(1, CInt(full.Height * k))
            Dim b As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(b)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
                g.DrawImage(full, 0, 0, w, h)
            End Using
            Return b
        End Using
    End Function

    Private Sub ShowCompositeGuide()
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        Dim bg As Bitmap
        Cursor = Cursors.WaitCursor
        Try
            bg = CompositeBackground()
        Finally
            Cursor = Cursors.Default
        End Try
        Using dlg As New frmCompositeGuide(bg, _help, FullOutputSize())
            bg.Dispose() ' 引導視窗自己複製了一份
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.Result Is Nothing Then Return
            AddCompositeLayer(dlg.Result)
        End Using
    End Sub

    ''' <summary>合成圖層的陰影圖層（依 ShadowLayerPath 找）；沒有時 Nothing。</summary>
    Private Function CompositeShadowOf(figure As Overlay) As Overlay
        Dim path = figure?.Composite?.ShadowLayerPath
        If String.IsNullOrEmpty(path) OrElse _recipe.Overlays Is Nothing Then Return Nothing
        Return _recipe.Overlays.FirstOrDefault(Function(x) x.CompositeShadowName IsNot Nothing AndAlso String.Equals(x.ImagePath, path, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>陰影圖層所屬的人物合成圖層；不是陰影圖層時 Nothing。</summary>
    Private Function CompositeFigureOf(shadow As Overlay) As Overlay
        If shadow?.CompositeShadowName Is Nothing OrElse _recipe.Overlays Is Nothing Then Return Nothing
        Return _recipe.Overlays.FirstOrDefault(Function(x) x.Composite IsNot Nothing AndAlso String.Equals(x.Composite.ShadowLayerPath, shadow.ImagePath, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' 回到引導視窗修改合成圖層（o 是人物圖層或它的陰影圖層）。背景是不含這個人物與陰影的結果。
    ''' 不是合成圖層時回傳 False（呼叫端照一般圖層處理）。
    ''' </summary>
    Private Function EditCompositeLayer(o As Overlay) As Boolean
        Dim figure = If(o?.Composite IsNot Nothing, o, CompositeFigureOf(o))
        If figure Is Nothing OrElse _photo Is Nothing Then Return False
        Dim dlg = OpenCompositeEditor(figure)
        If dlg Is Nothing Then Return True
        Using dlg
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.Result IsNot Nothing Then AddCompositeLayer(dlg.Result, replace:=figure)
        End Using
        Return True
    End Function

    ''' <summary>建立載入了既有合成圖層的引導視窗（還沒顯示）；讀不到原圖或遮罩時告知並回傳 Nothing。測試也用這個。</summary>
    Friend Function OpenCompositeEditor(figure As Overlay) As frmCompositeGuide
        Dim shadow = CompositeShadowOf(figure)
        Dim without = _recipe.Clone()
        without.Overlays = without.Overlays?.Where(Function(x) Not (String.Equals(x.ImagePath, figure.ImagePath, StringComparison.OrdinalIgnoreCase) OrElse
                                                                    (shadow IsNot Nothing AndAlso String.Equals(x.ImagePath, shadow.ImagePath, StringComparison.OrdinalIgnoreCase)))).ToList()
        Dim bg As Bitmap
        Cursor = Cursors.WaitCursor
        Try
            bg = CompositeBackground(without)
        Finally
            Cursor = Cursors.Default
        End Try
        Dim dlg As New frmCompositeGuide(bg, _help, FullOutputSize())
        bg.Dispose()
        If Not dlg.LoadExisting(figure) Then
            MessageBox.Show(Me, Lang.T("無法修改這個合成圖層：人物原圖或遮罩檔案不見了（可能被移動或刪除）。"), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            dlg.Dispose()
            Return Nothing
        End If
        Return dlg
    End Function

    ''' <summary>把引導視窗的結果存成檔案、加成合成圖層；replace 不是 Nothing 時取代那個合成圖層（和它的陰影），留在原本的圖層位置。測試也用這個。</summary>
    Friend Sub AddCompositeLayer(r As frmCompositeGuide.GuideResult, Optional replace As Overlay = Nothing)
        Dim baseName As String
        Try
            Directory.CreateDirectory(CompositeFolder)
            Dim stem = Lang.T("合成_") & DateTime.Now.ToString("yyyyMMdd_HHmmss")
            baseName = Path.Combine(CompositeFolder, stem)
            Dim n = 2
            While File.Exists(baseName & "_layer.png")
                baseName = Path.Combine(CompositeFolder, $"{stem}_{n}")
                n += 1
            End While
            Dim f = r.Figure
            If r.SourceFile IsNot Nothing Then
                f.Name = Path.GetFileNameWithoutExtension(r.SourceFile)
            ElseIf String.IsNullOrEmpty(f.Name) Then
                f.Name = Lang.T("貼上的圖片")
            End If
            f.LayerX = r.X : f.LayerY = r.Y : f.LayerSize = Math.Max(0.005, r.Size)
            f.SourcePath = baseName & "_figure.png"
            f.MaskPath = baseName & "_mask.png"
            f.AiMaskPath = If(r.AiMask Is Nothing, Nothing, baseName & "_ai.png")
            r.FigureImage.Save(f.SourcePath, ImageFormat.Png)
            CompositeBaker.SaveMask(r.Mask, r.FigureWidth, r.FigureHeight, f.MaskPath)
            If r.AiMask IsNot Nothing Then CompositeBaker.SaveMask(r.AiMask, r.FigureWidth, r.FigureHeight, f.AiMaskPath)
            r.Baked.Save(baseName & "_layer.png", ImageFormat.Png)
            f.OcclusionMaskPath = Nothing
            If r.Occlusion IsNot Nothing Then
                f.OcclusionMaskPath = baseName & "_occlusion.png"
                CompositeBaker.SaveMask(r.Occlusion, r.BackgroundWidth, r.BackgroundHeight, f.OcclusionMaskPath)
            End If
            f.ShadowLayerPath = Nothing
            If r.ShadowImage IsNot Nothing Then
                f.ShadowLayerPath = baseName & "_shadow.png"
                r.ShadowImage.Save(f.ShadowLayerPath, ImageFormat.Png)
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Runtime.InteropServices.ExternalException
            MessageBox.Show(Me, Lang.T("無法儲存合成的檔案：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Finally
            r.Baked.Dispose()
            r.FigureImage.Dispose()
            r.ShadowImage?.Dispose()
        End Try
        Dim o As New Overlay With {.Kind = OverlayKind.Image, .ImagePath = baseName & "_layer.png", .Shadow = False,
                                   .X = r.X, .Y = r.Y, .Size = Math.Max(0.005, r.Size), .Composite = r.Figure}
        Dim shadowLayer As Overlay = Nothing
        If r.Figure.ShadowLayerPath IsNot Nothing Then
            shadowLayer = New Overlay With {.Kind = OverlayKind.Image, .ImagePath = r.Figure.ShadowLayerPath, .Shadow = False,
                                            .X = r.ShadowX, .Y = r.ShadowY, .Size = Math.Max(0.005, r.ShadowSize), .CompositeShadowName = r.Figure.Name}
        End If
        ' 陰影在人物下面；兩個圖層一次加入（復原一次就一起拿掉）
        Dim oldShadow = If(replace Is Nothing, Nothing, CompositeShadowOf(replace))
        Dim replacePath = replace?.ImagePath, oldShadowPath = oldShadow?.ImagePath
        ApplyChange(Sub(rc)
                        If rc.Overlays Is Nothing Then rc.Overlays = New List(Of Overlay)()
                        Dim old = If(replacePath Is Nothing, Nothing, rc.Overlays.FirstOrDefault(Function(x) x.Composite IsNot Nothing AndAlso String.Equals(x.ImagePath, replacePath, StringComparison.OrdinalIgnoreCase)))
                        If old Is Nothing Then
                            If shadowLayer IsNot Nothing Then rc.Overlays.Add(shadowLayer)
                            rc.Overlays.Add(o)
                            Return
                        End If
                        ' 取代：新的人物（和陰影）放在舊人物原本的位置，舊的陰影拿掉
                        Dim order = LayerStack.Order(rc)
                        Dim at = order.FindIndex(Function(l) l.Item Is old)
                        Dim isOldShadow = Function(l As LayerRef) oldShadowPath IsNot Nothing AndAlso l.IsOverlay AndAlso l.Overlay.CompositeShadowName IsNot Nothing AndAlso
                                                                 String.Equals(l.Overlay.ImagePath, oldShadowPath, StringComparison.OrdinalIgnoreCase)
                        ' 舊陰影通常在人物下面：拿掉後人物原本的位置要往前移那麼多格
                        at -= order.Take(at).Count(isOldShadow)
                        order.RemoveAll(Function(l) l.Item Is old OrElse isOldShadow(l))
                        Dim inserts As New List(Of LayerRef)()
                        If shadowLayer IsNot Nothing Then inserts.Add(New LayerRef(shadowLayer))
                        inserts.Add(New LayerRef(o))
                        order.InsertRange(Math.Min(at, order.Count), inserts)
                        If Not rc.Overlays.Contains(o) Then rc.Overlays.Add(o)
                        If shadowLayer IsNot Nothing AndAlso Not rc.Overlays.Contains(shadowLayer) Then rc.Overlays.Add(shadowLayer)
                        LayerStack.SetOrder(rc, order)
                    End Sub)
        _overlayIndex = _recipe.Overlays.Count - 1
        SyncSliders()
        UpdateCreativeControls()
        UpdateToolFromTab()
        SetStatusMessage(If(shadowLayer Is Nothing, Lang.T("已加入合成圖層：可以拖曳移動、拖角落縮放；原本的照片不變。"),
                            Lang.T("已加入合成圖層與陰影圖層：可以拖曳移動、拖角落縮放；原本的照片不變。")))
    End Sub
End Class
