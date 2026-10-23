Imports System.IO
Imports PhotoEdit

' 匯出 PDF（「匯出…」選 PDF）與匯入 SVG（檔案 → 匯入 SVG…）。核心在 PhotoEdit.Core\PdfExport.vb、SvgImport.vb。
Partial Class frmEditor

    ''' <summary>匯出成 PDF：全尺寸合併所有圖層，頁面大小依影像的解析度（沒有時 300 DPI）。</summary>
    Private Sub SavePdf(file As String)
        ExitCropMode(apply:=True)
        CommitCalloutEditor()
        Cursor = Cursors.WaitCursor
        Try
            Dim recipe = _recipe.Clone()
            Using merged = RenderMerged(recipe)
                Dim dpi = If(_photo.Resolution > 1, _photo.Resolution, 300.0F)
                PdfExport.WriteImage(file, merged, dpi, Path.GetFileNameWithoutExtension(file))
                SetStatusMessage(Lang.F("已存成 PDF：{0}（{1:0.0} × {2:0.0} 公分）", file, merged.Width / dpi * 2.54, merged.Height / dpi * 2.54))
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is Runtime.InteropServices.ExternalException
            MessageBox.Show(Me, Lang.T("存檔失敗：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub ImportSvgWithDialog()
        If _photo Is Nothing Then
            MessageBox.Show(Me, Lang.T("先開啟一張照片或「新增」一張影像，再匯入 SVG。"), AppName, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using dlg As New OpenFileDialog With {.Title = Lang.T("匯入 SVG"), .Filter = Lang.T("SVG 向量圖 (*.svg)|*.svg")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ImportSvg(dlg.FileName)
        End Using
    End Sub

    ''' <summary>把 SVG 的圖形加成繪圖圖層（多個時組成一個群組），切到繪圖頁籤並選取。回傳加了幾個圖形。</summary>
    Friend Function ImportSvg(file As String) As Integer
        Dim res As SvgImport.Result
        Try
            Dim size = PhotoPixelSize()
            res = SvgImport.Load(file, size.Width, size.Height)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Xml.XmlException
            MessageBox.Show(Me, Lang.T("無法讀取 SVG：") & ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return 0
        End Try
        If res.Layers.Count = 0 Then
            SetStatusMessage(Lang.T("這個 SVG 裡沒有可以匯入的圖形。"))
            Return 0
        End If
        Dim baseName = Path.GetFileNameWithoutExtension(file)
        For i = 0 To res.Layers.Count - 1
            res.Layers(i).Name = baseName & " " & (i + 1)
        Next
        _tabs.SelectedIndex = TabDraw
        ApplyChange(Sub(r)
                        If r.Drawings Is Nothing Then r.Drawings = New List(Of DrawLayer)()
                        r.Drawings.AddRange(res.Layers)
                    End Sub)
        Dim idx = res.Layers.Select(Function(d) _recipe.Drawings.IndexOf(d)).Where(Function(i) i >= 0).ToList()
        _drawStrip.SelectedTool = DrawIcons.SelectTool
        If idx.Count >= 2 Then
            SetMultiSelection(idx, idx.Max())
            If CanGroup() Then
                GroupSelected()
                Dim g = DrawGroups.Find(_recipe, _recipe.Drawings(idx(0)).GroupId)
                If g IsNot Nothing Then ApplyChange(Sub(r) DrawGroups.Find(r, g.Id).Name = baseName)
            End If
        ElseIf idx.Count = 1 Then
            SelectDrawLayer(idx(0))
        End If
        Dim skipped = If(res.Skipped.Count = 0, "", Lang.T("（沒匯入：") & String.Join("、", res.Skipped.Select(Function(kv) $"{kv.Key} {kv.Value}")) & Lang.T("）"))
        SetStatusMessage(Lang.F("已匯入 {0} 個圖形，可以用節點工具修改。", res.Layers.Count) & skipped)
        UpdateDrawControls()
        _canvas.Invalidate()
        Return res.Layers.Count
    End Function
End Class
