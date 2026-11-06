Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports PhotoEdit

''' <summary>
''' 影像 → 怪獸去背小幫手…（去背分頁也有按鈕）：C:\專案\Monster 的引導式去背工具（MonsterCutout.Tool）。
''' 小幫手修整完二選一：
'''   只要剪好的怪獸 → 遮罩變成這裡的「去背」（和自動去背一樣存進專案、可以再用去背分頁修）。
'''   放進場景，加上特效 → 怪獸交給「人物合成」（去背那步略過，先選背景），合成完切到特效筆「動畫／特攝／電影」。
''' </summary>
Partial Friend Class frmEditor

    ''' <summary>特效筆「動畫／特攝／電影」分類的預設招式。</summary>
    Private Const MonsterDefaultFx As FxKind = FxKind.AtomicBreath
    ''' <summary>光束的預設粗細（照片高度的比例；「粗細」滑桿的 30）。</summary>
    Private Const MonsterBeamWidth As Double = 0.03

    Private Sub ShowMonsterCutout()
        If _photo Is Nothing Then Return
        ExitCropMode(apply:=True)
        ' 怪獸用原照片（長邊縮到合成的工作尺寸 2048），去背和合成都夠清楚
        Dim source As Bitmap
        Using copy = DirectCast(_photo.Image.Clone(), Bitmap)
            source = CompositeBaker.FitWorking(New Bitmap(copy))
        End Using
        Dim options As New MonsterCutout.MonsterCutoutOptions With {
            .Title = Lang.T("怪獸去背小幫手"),
            .SourceName = IO.Path.GetFileName(_photo.Path),
            .AllowComposite = True,
            .CompositeDescription = Lang.T("接著打開「人物合成」：選一張城市或自己的照片，調整位置、大小、透視、遮擋、光線和陰影，合成好再用特效筆噴出原子吐息、放射熱線等招式。")}
        Dim result As MonsterCutout.MonsterCutoutResult
        Using source
            result = MonsterCutout.MonsterCutoutTool.ShowDialog(Me, source, options)
        End Using
        If result Is Nothing Then Return
        Using result
            If result.Action = MonsterCutout.MonsterCutoutAction.Composite Then
                OpenMonsterComposite(result)
            Else
                ApplyMonsterCutout(result)
            End If
        End Using
    End Sub

    ''' <summary>
    ''' 「只要剪好的怪獸」：遮罩換算成自動去背的格式（和 BackgroundRemover.ComputeMask 一樣：預覽圖大小、長邊不超過 MaskStore.MaxSide），
    ''' 當成 AI 遮罩套用，背景改透明。小幫手已經修過邊，所以不再精修；先前的筆刷修正清掉。測試也用這個。
    ''' </summary>
    Friend Sub ApplyMonsterCutout(result As MonsterCutout.MonsterCutoutResult)
        If _photo Is Nothing OrElse _previewBase Is Nothing Then Return
        Dim k = Math.Min(1.0, MaskStore.MaxSide / CDbl(Math.Max(_previewBase.Width, _previewBase.Height)))
        Dim mw = Math.Max(1, CInt(_previewBase.Width * k)), mh = Math.Max(1, CInt(_previewBase.Height * k))
        SetAiMask(MonsterMaskBitmap(result.Mask, result.Width, result.Height, mw, mh), CutoutModel.BiRefNetLite, dirty:=True)
        ApplyChange(Sub(r)
                        If r.Cutout Is Nothing Then r.Cutout = New CutoutSettings With {.EdgeDecontaminate = True}
                        r.Cutout.Model = CutoutModel.BiRefNetLite
                        r.Cutout.EdgeRefine = 0
                        r.Cutout.Strokes.Clear()
                        r.Cutout.Wand.Clear()
                        If r.Cutout.Background = CutoutBackground.Original Then r.Cutout.Background = CutoutBackground.Transparent
                    End Sub)
        _tabs.SelectedIndex = TabCutout
        _cutoutStatus.Text = Lang.T("已套用怪獸去背小幫手的結果。")
        SetStatusMessage(Lang.T("怪獸去背好了：可以在「去背」分頁換背景、用筆刷修正，或存成貼圖。"))
    End Sub

    ''' <summary>小幫手的遮罩（0..255）縮放成去背用的灰階 32bpp 圖（白＝主體）。</summary>
    Private Shared Function MonsterMaskBitmap(mask As Byte(), w As Integer, h As Integer, tw As Integer, th As Integer) As Bitmap
        Dim px(w * h * 4 - 1) As Byte
        For i = 0 To mask.Length - 1
            Dim v = mask(i)
            px(i * 4) = v : px(i * 4 + 1) = v : px(i * 4 + 2) = v : px(i * 4 + 3) = 255
        Next
        Using src As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Dim d = src.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
            Try
                For y = 0 To h - 1
                    Marshal.Copy(px, y * w * 4, d.Scan0 + y * d.Stride, w * 4)
                Next
            Finally
                src.UnlockBits(d)
            End Try
            Dim dst As New Bitmap(tw, th, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(dst)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear
                g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
                g.DrawImage(src, 0, 0, tw, th)
            End Using
            Return dst
        End Using
    End Function

    ''' <summary>
    ''' 「放進場景，加上特效」：打開人物合成（沒有預設背景，因為目前的影像就是怪獸照片），怪獸已經去好背；
    ''' 套用後（背景開成新影像、加上合成圖層）切到特效筆。
    ''' </summary>
    Private Sub OpenMonsterComposite(result As MonsterCutout.MonsterCutoutResult)
        Using dlg = CreateMonsterCompositeGuide(result)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.Result Is Nothing Then Return
            Dim withBackground = Not dlg.Result.NoBackground
            OpenCompositeResult(dlg.Result)
            If withBackground Then StartMonsterEffects()
        End Using
    End Sub

    ''' <summary>建立帶著怪獸的人物合成視窗（測試也用這個）。</summary>
    Friend Function CreateMonsterCompositeGuide(result As MonsterCutout.MonsterCutoutResult) As frmCompositeGuide
        Dim dlg As New frmCompositeGuide(Nothing, _help, Size.Empty)
        dlg.PresetFigure(result.Image, Lang.T("怪獸（已去背）"),
                         Lang.T("怪獸已經去好背了。先在下面按「開啟背景…」或「貼上背景」選一張要放怪獸的照片，再按「下一步」。"))
        Return dlg
    End Function

    ''' <summary>
    ''' 合成完：切到「繪圖」分頁的自由繪製、特效筆「動畫／特攝／電影」的第一招（原子吐息），
    ''' 提示從怪獸嘴巴拖出光束。特效是一般的特效筆畫，之後可以換招式、顏色、粗細。測試也用這個。
    ''' </summary>
    Friend Sub StartMonsterEffects()
        If _photo Is Nothing Then Return
        _tabs.SelectedIndex = TabDraw
        _drawStrip.SelectedTool = CInt(DrawShape.Freehand)
        SetDrawBrush(BrushKind.FX)
        Dim cats = EffectCatalog.FxCategories
        Dim w = EffectCatalog.Locate(cats, CInt(MonsterDefaultFx))
        ApplyEffectEntry(cats(w.Category).Items(w.Item))
        ' 光束要夠粗才有氣勢（預設 8 只像一條線）；使用者原本就調得更粗時不動
        SetDrawProp(Sub(d) d.StrokeWidth = Math.Max(d.StrokeWidth, MonsterBeamWidth))
        SyncSliders()
        SetStatusMessage(Lang.F("合成好了！特效筆已選「{0}」：從怪獸嘴巴往要打的方向拖曳。右側「效果」可以換放射熱線、斯派修姆光線等「{1}」招式。",
                                cats(w.Category).Items(w.Item).Name, cats(w.Category).Name))
    End Sub

End Class
