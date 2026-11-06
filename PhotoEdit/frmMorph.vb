Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports PhotoEdit

''' <summary>
''' 變形動畫（影像 → 變形動畫…）：第一張（目前的影像）變成第二張（例如人臉變野狼），可以再接第三張、第四張…連續變形，輸出 MP4、GIF。
''' 每張圖都有一組對應點（同名、同順序）：找得到臉就自動放約 50 點；不是人臉（動物）時依序點 5 個關鍵點（兩眼、鼻尖、兩嘴角），
''' 其餘的點從前一張推估過去再手動拖。上方的順序列選要編輯哪一段（相鄰兩張：左邊＝前一張、右邊＝後一張）。
''' 解析度不同沒關係：輸出用第一張的比例，第二張以後依兩眼對齊第一張（「對齊」滑桿）。
''' </summary>
Friend Class frmMorph
    Inherits Aqua.AquaForm

    Private Const EditW As Integer = 420
    Private Const EditH As Integer = 520
    Private Const PreviewW As Integer = 330
    Private Const StripH As Integer = 72
    ''' <summary>最多幾張（每段都要標點，太多張也不實用）。</summary>
    Friend Const MaxImages As Integer = 9

    Private ReadOnly _help As HelpTip
    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _small As New Font(Lang.UiFont, 9.0F)
    Private ReadOnly _strip As New SequenceStrip()
    Private ReadOnly _editA As New MorphEditor()
    Private ReadOnly _editB As New MorphEditor()
    Private ReadOnly _capA As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _capB As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _addImage As New Button With {.Text = Lang.T("加一張…")}
    Private ReadOnly _replaceImage As New Button With {.Text = Lang.T("換這張…")}
    Private ReadOnly _removeImage As New Button With {.Text = Lang.T("移除")}
    Private ReadOnly _moveLeft As New Button With {.Text = "◀"}
    Private ReadOnly _moveRight As New Button With {.Text = "▶"}
    Private ReadOnly _preview As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(30, 30, 32)}
    Private ReadOnly _t As New Aqua.Slider With {.Minimum = 0, .Maximum = 100, .Value = 50, .ShowTicks = False}
    Private ReadOnly _tValue As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _play As New Button With {.Text = Lang.T("播放")}
    Private ReadOnly _guide As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(47, 128, 237)}
    Private ReadOnly _seconds As New NumericUpDown With {.Minimum = 1, .Maximum = 20, .DecimalPlaces = 1, .Increment = 0.5D, .Value = 3}
    Private ReadOnly _fps As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _holdStart As New NumericUpDown With {.Minimum = 0, .Maximum = 5, .DecimalPlaces = 1, .Increment = 0.5D, .Value = 0.5D}
    Private ReadOnly _holdBetween As New NumericUpDown With {.Minimum = 0, .Maximum = 5, .DecimalPlaces = 1, .Increment = 0.5D, .Value = 0.5D}
    Private ReadOnly _holdEnd As New NumericUpDown With {.Minimum = 0, .Maximum = 5, .DecimalPlaces = 1, .Increment = 0.5D, .Value = 1}
    Private ReadOnly _pingPong As New CheckBox With {.Text = Lang.T("變過去再變回來"), .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _ease As New CheckBox With {.Text = Lang.T("慢入慢出"), .AutoSize = True, .Checked = True, .BackColor = Color.Transparent}
    Private ReadOnly _align As New Aqua.Slider With {.Minimum = 0, .Maximum = 100, .Value = 50, .ShowTicks = False}
    Private ReadOnly _alignValue As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _size As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _saveMp4 As New Button With {.Text = Lang.T("存成 MP4…")}
    Private ReadOnly _saveGif As New Button With {.Text = Lang.T("存成 GIF…")}
    Private ReadOnly _cancelExport As New Button With {.Text = Lang.T("停止"), .Enabled = False}
    Private ReadOnly _progress As New ProgressBar With {.Minimum = 0, .Maximum = 100}
    Private ReadOnly _status As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _previewTimer As New System.Windows.Forms.Timer With {.Interval = 120}
    Private ReadOnly _playTimer As New System.Windows.Forms.Timer With {.Interval = 50}

    ' 圖片順序：第 0 張是目前的影像（不歸這裡釋放），之後是加進來的
    Private ReadOnly _images As New List(Of Bitmap)
    Private ReadOnly _names As New List(Of String)
    ''' <summary>每張圖的點（同名、同順序）。</summary>
    Private _points As New List(Of List(Of MorphPoint))
    ''' <summary>正在編輯的段：左邊第 _pair 張、右邊第 _pair+1 張。</summary>
    Private _pair As Integer
    ''' <summary>選中的圖（順序列上框起來的；換圖、移除、移動都對它）。</summary>
    Private _sel As Integer
    Private _templateA As List(Of MorphPoint) ' 第一張的預設點
    Private _aHasFace As Boolean
    ''' <summary>正在標 5 個關鍵點的那張（-1＝沒有），還在排隊等著標的。</summary>
    Private _keyTarget As Integer = -1
    Private ReadOnly _pendingKeys As New List(Of Integer)
    Private ReadOnly _keys As New List(Of PointF)
    Private ReadOnly _undo As New Stack(Of List(Of List(Of MorphPoint)))
    Private _morph As FaceMorph
    Private _playDir As Integer = 1
    Private _cts As CancellationTokenSource

    ''' <summary>輸出的資料夾（測試時可改）。</summary>
    Friend Shared Property OutputFolder As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", Lang.T("變形動畫"))

    Public Sub New(imageA As Bitmap, nameA As String, help As HelpTip)
        _images.Add(imageA)
        _names.Add(nameA)
        _help = help
        Text = Lang.T("變形動畫")
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        RoundCorners = False ' 小工具視窗：方角（圓角的描線會被裁掉一半）
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        KeyPreview = True
        Dim top = 23 + 10
        Dim xB = 16 + EditW + 10, xP = xB + EditW + 16
        ClientSize = New Size(xP + PreviewW + 16, top + StripH + 8 + 30 + EditH + 150)

        ' 最上面：圖片順序（點一張選它，編輯它和前一張那一段）＋加圖、換圖、移除、移動
        _strip.SetBounds(16, top, EditW * 2 + 10, StripH)
        AddHandler _strip.TileClicked, Sub(i) SelectImage(i)
        AddHandler _strip.AddClicked, Sub() PickImage(replace:=False)
        help?.SetHelp("morph.strip", _strip)
        _addImage.SetBounds(xP, top, 110, 30)
        _replaceImage.SetBounds(xP + 116, top, 110, 30)
        _removeImage.SetBounds(xP + 232, top, 98, 30)
        _moveLeft.SetBounds(xP, top + 38, 54, 30)
        _moveRight.SetBounds(xP + 60, top + 38, 54, 30)
        AddHandler _addImage.Click, Sub() PickImage(replace:=False)
        AddHandler _replaceImage.Click, Sub() PickImage(replace:=True)
        AddHandler _removeImage.Click, Sub() RemoveImage(_sel)
        AddHandler _moveLeft.Click, Sub() MoveImage(_sel, -1)
        AddHandler _moveRight.Click, Sub() MoveImage(_sel, 1)
        help?.SetHelp("morph.pick", _addImage)
        help?.SetHelp("morph.replace", _replaceImage)
        help?.SetHelp("morph.remove", _removeImage)
        help?.SetHelp("morph.move", _moveLeft, _moveRight)
        Controls.AddRange({_strip, _addImage, _replaceImage, _removeImage, _moveLeft, _moveRight})

        ' 兩張圖（目前這一段）的標題與編輯區
        Dim cy = top + StripH + 8
        _capA.SetBounds(16, cy + 4, EditW, 22)
        _capB.SetBounds(xB, cy + 4, EditW, 22)
        Controls.AddRange({_capA, _capB})
        Dim y = cy + 30
        _editA.SetBounds(16, y, EditW, EditH)
        _editB.SetBounds(xB, y, EditW, EditH)
        help?.SetHelp("morph.edit", _editA)
        help?.SetHelp("morph.edit", _editB)
        Controls.AddRange({_editA, _editB})
        WireEditor(_editA, True)
        WireEditor(_editB, False)

        ' 點位工具列
        Dim ty = y + EditH + 6
        _guide.Font = _small
        _guide.SetBounds(16, ty, EditW * 2 + 10 - 330, 40)
        Dim resetPts As New Button With {.Text = Lang.T("重設點位"), .Font = _small}
        resetPts.SetBounds(16 + EditW * 2 + 10 - 324, ty, 76, 28)
        AddHandler resetPts.Click, Sub() ResetPoints()
        Dim loadPts As New Button With {.Text = Lang.T("讀點位…"), .Font = _small}
        loadPts.SetBounds(16 + EditW * 2 + 10 - 244, ty, 76, 28)
        AddHandler loadPts.Click, Sub() LoadPoints()
        Dim savePts As New Button With {.Text = Lang.T("存點位…"), .Font = _small}
        savePts.SetBounds(16 + EditW * 2 + 10 - 164, ty, 76, 28)
        AddHandler savePts.Click, Sub() SavePoints()
        Dim fit As New Button With {.Text = Lang.T("整張"), .Font = _small}
        fit.SetBounds(16 + EditW * 2 + 10 - 84, ty, 76, 28)
        AddHandler fit.Click, Sub()
                                  _editA.ResetView()
                                  _editB.ResetView()
                              End Sub
        help?.SetHelp("morph.reset", resetPts)
        help?.SetHelp("morph.points", loadPts)
        help?.SetHelp("morph.points", savePts)
        Controls.AddRange({_guide, resetPts, loadPts, savePts, fit})

        ' 右邊：預覽
        Controls.Add(New Label With {.Text = Lang.T("預覽"), .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(xP, cy + 4)})
        _preview.SetBounds(xP, y, PreviewW, 400)
        Controls.Add(_preview)
        Dim py = y + 406
        _t.SetBounds(xP, py + 2, PreviewW - 110, 24)
        _tValue.SetBounds(xP + PreviewW - 108, py, 44, 26)
        _play.SetBounds(xP + PreviewW - 60, py - 1, 60, 28)
        AddHandler _t.ValueChanged, Sub()
                                        UpdateTLabel()
                                        RenderPreview()
                                    End Sub
        AddHandler _play.Click, Sub() TogglePlay()
        help?.SetHelpLinked("morph.preview", _t, _t, _tValue, _play)
        Controls.AddRange({_t, _tValue, _play})
        py += 36
        Dim alignCap As New Label With {.Text = Lang.T("對齊"), .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(xP, py + 4)}
        _align.SetBounds(xP + 44, py + 2, PreviewW - 98, 24)
        _alignValue.SetBounds(xP + PreviewW - 52, py, 52, 26)
        AddHandler _align.ValueChanged, Sub()
                                            _alignValue.Text = _align.Value & "%"
                                            Rebuild()
                                        End Sub
        help?.SetHelpLinked("morph.align", _align, alignCap, _align, _alignValue)
        Controls.AddRange({alignCap, _align, _alignValue})

        ' 下方：時間與輸出
        Dim by = y + EditH + 52
        Dim lab = Function(s As String, x As Integer) New Label With {.Text = s, .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(x, by + 4)}
        Controls.Add(lab(Lang.T("變形秒數"), 16))
        _seconds.SetBounds(90, by, 64, 26)
        Controls.Add(lab(Lang.T("每秒格數"), 168))
        _fps.Items.AddRange({"24", "30", "60"})
        _fps.SelectedIndex = 1
        _fps.SetBounds(242, by, 56, 26)
        Controls.Add(lab(Lang.T("開頭停"), 312))
        _holdStart.SetBounds(370, by, 56, 26)
        Controls.Add(lab(Lang.T("中間停"), 436))
        _holdBetween.SetBounds(494, by, 56, 26)
        Controls.Add(lab(Lang.T("結尾停"), 560))
        _holdEnd.SetBounds(618, by, 56, 26)
        _pingPong.Location = New Point(690, by + 3)
        _ease.Location = New Point(830, by + 3)
        help?.SetHelp("morph.timing", _seconds)
        help?.SetHelp("morph.timing", _holdStart)
        help?.SetHelp("morph.timing", _holdEnd)
        help?.SetHelp("morph.holdbetween", _holdBetween)
        help?.SetHelp("morph.pingpong", _pingPong)
        help?.SetHelp("morph.ease", _ease)
        Controls.AddRange({_seconds, _fps, _holdStart, _holdBetween, _holdEnd, _pingPong, _ease})
        by += 36
        Controls.Add(lab(Lang.T("輸出大小（長邊）"), 16))
        _size.Items.AddRange({"720", "1080", "1440", Lang.T("第一張原尺寸")})
        _size.SelectedIndex = 1
        _size.SetBounds(140, by, 118, 26)
        help?.SetHelp("morph.size", _size)
        _saveMp4.SetBounds(272, by - 1, 110, 30)
        _saveGif.SetBounds(388, by - 1, 110, 30)
        _cancelExport.SetBounds(504, by - 1, 70, 30)
        AddHandler _saveMp4.Click, Sub() Export(False)
        AddHandler _saveGif.Click, Sub() Export(True)
        AddHandler _cancelExport.Click, Sub() _cts?.Cancel()
        help?.SetHelp("morph.mp4", _saveMp4)
        help?.SetHelp("morph.gif", _saveGif)
        _progress.SetBounds(588, by + 6, 230, 16)
        Controls.AddRange({_size, _saveMp4, _saveGif, _cancelExport, _progress})
        by += 34
        _status.Font = _small
        _status.SetBounds(16, by, xP - 30, 22)
        Controls.Add(_status)
        Dim closeBtn As New Button With {.Text = Lang.T("關閉"), .DialogResult = DialogResult.Cancel}
        closeBtn.SetBounds(ClientSize.Width - 112, ClientSize.Height - 44, 96, 32)
        Controls.Add(closeBtn)
        CancelButton = closeBtn

        AddHandler _previewTimer.Tick, Sub()
                                           _previewTimer.Stop()
                                           RebuildNow()
                                       End Sub
        AddHandler _playTimer.Tick, Sub() PlayStep()
        _alignValue.Text = "50%"
        _points.Add(New List(Of MorphPoint))
        _strip.Add(imageA, nameA)
        ResetPointsA()
        ShowPair(0)
    End Sub

    '---------------------------------------------------------------------
    ' 圖片順序
    '---------------------------------------------------------------------

    ''' <summary>張數。</summary>
    Friend ReadOnly Property ImageCount As Integer
        Get
            Return _images.Count
        End Get
    End Property

    ''' <summary>第 i 張的點（測試用）。</summary>
    Friend Function PointsOf(i As Integer) As List(Of MorphPoint)
        Return _points(i)
    End Function

    ''' <summary>點順序列上的第 i 張：選它，編輯它和前一張那一段（第一張＝第一段）。</summary>
    Friend Sub SelectImage(i As Integer)
        If i < 0 OrElse i >= _images.Count Then Return
        _sel = i
        ShowPair(Math.Max(0, Math.Min(_images.Count - 2, i - 1)))
    End Sub

    ''' <summary>兩個編輯區換成第 pair、pair+1 張。</summary>
    Private Sub ShowPair(pair As Integer)
        _pair = Math.Max(0, pair)
        Dim hasB = _pair + 1 < _images.Count
        _editA.SetImage(_images(_pair), _points(_pair))
        _editB.SetImage(If(hasB, _images(_pair + 1), Nothing), If(hasB, _points(_pair + 1), New List(Of MorphPoint)))
        _editA.KeyMode = False
        _editB.KeyMode = hasB AndAlso _keyTarget = _pair + 1
        _editB.KeyMarks.Clear()
        If _editB.KeyMode Then _editB.KeyMarks.AddRange(_keys)
        _editA.Selected = -1 : _editB.Selected = -1
        _capA.Text = Caption(_pair)
        _capB.Text = If(hasB, Caption(_pair + 1), Lang.T("第二張（變成這張）：按「加一張…」"))
        _strip.Selected = _sel
        _strip.Pair = If(hasB, _pair, -1)
        _strip.Invalidate()
        _replaceImage.Enabled = _sel >= 1
        _removeImage.Enabled = _sel >= 1
        _moveLeft.Enabled = _sel >= 2
        _moveRight.Enabled = _sel >= 1 AndAlso _sel < _images.Count - 1
        _addImage.Enabled = _images.Count < MaxImages
        _editA.Invalidate() : _editB.Invalidate()
        UpdateGuide()
        UpdateTLabel()
        Rebuild()
    End Sub

    Private Function Caption(i As Integer) As String
        Dim img = _images(i)
        Return Lang.F("第 {0} 張：{1}（{2}×{3}）", i + 1, _names(i), img.Width, img.Height) & If(i = 0, Lang.T("（目前的影像）"), "")
    End Function

    Private Sub PickImage(replace As Boolean)
        If replace AndAlso _sel < 1 Then Return
        If Not replace AndAlso _images.Count >= MaxImages Then
            _status.Text = Lang.F("最多 {0} 張。", MaxImages)
            Return
        End If
        Using dlg As New OpenFileDialog With {.Title = If(replace, Lang.T("換成另一張圖"), Lang.T("選下一張圖（要變成的圖）")),
                                              .Filter = Lang.T("圖片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|所有檔案|*.*")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim img As Bitmap
            Try
                Using pf = PhotoFile.Open(dlg.FileName)
                    img = New Bitmap(pf.Image) ' 已依 EXIF 轉正；複製一份（PhotoFile 釋放時圖也會釋放）
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is NotSupportedException
                MessageBox.Show(Me, Lang.T("無法開啟：") & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End Try
            If replace Then ReplaceImage(_sel, img, dlg.FileName) Else AddImage(img, dlg.FileName)
        End Using
    End Sub

    ''' <summary>加到最後面（測試也從這裡進）：是人臉就自動放點，不是就標 5 個關鍵點。</summary>
    Friend Sub AddImage(img As Bitmap, path As String)
        PushUndo()
        _images.Add(img)
        _names.Add(IO.Path.GetFileNameWithoutExtension(path))
        _points.Add(New List(Of MorphPoint))
        _strip.Add(img, _names(_names.Count - 1))
        Dim i = _images.Count - 1
        _sel = i
        AutoPointsFor(i)
        ShowPair(i - 1)
    End Sub

    ''' <summary>換掉第 i 張（第一張是目前的影像，不能換）。</summary>
    Friend Sub ReplaceImage(i As Integer, img As Bitmap, path As String)
        If i < 1 OrElse i >= _images.Count Then Return
        PushUndo()
        _images(i).Dispose()
        _images(i) = img
        _names(i) = IO.Path.GetFileNameWithoutExtension(path)
        _strip.Replace(i, img, _names(i))
        _sel = i
        AutoPointsFor(i)
        ShowPair(i - 1)
    End Sub

    ''' <summary>換第二張圖（舊的入口；只有一張時是加一張）。</summary>
    Friend Sub SetImageB(img As Bitmap, path As String)
        If _images.Count < 2 Then AddImage(img, path) Else ReplaceImage(1, img, path)
    End Sub

    Friend Sub RemoveImage(i As Integer)
        If i < 1 OrElse i >= _images.Count Then Return
        _images(i).Dispose()
        _images.RemoveAt(i)
        _names.RemoveAt(i)
        _points.RemoveAt(i)
        _strip.RemoveAt(i)
        _undo.Clear() ' 張數變了，舊的復原紀錄對不上
        If _keyTarget = i Then
            _keyTarget = -1
            _keys.Clear()
        ElseIf _keyTarget > i Then
            _keyTarget -= 1
        End If
        _pendingKeys.Remove(i)
        For k = 0 To _pendingKeys.Count - 1
            If _pendingKeys(k) > i Then _pendingKeys(k) -= 1
        Next
        If _keyTarget < 0 Then NextPendingKeys()
        _sel = Math.Min(i, _images.Count - 1)
        SelectImage(_sel)
        _status.Text = Lang.F("已移除第 {0} 張。", i + 1)
    End Sub

    ''' <summary>第 i 張往前（-1）或往後（+1）搬；第一張固定是目前的影像。</summary>
    Friend Sub MoveImage(i As Integer, dir As Integer)
        Dim j = i + dir
        If i < 1 OrElse j < 1 OrElse j >= _images.Count Then Return
        PushUndo()
        Swap(_images, i, j) : Swap(_names, i, j) : Swap(_points, i, j)
        _strip.Swap(i, j)
        If _keyTarget = i Then _keyTarget = j Else If _keyTarget = j Then _keyTarget = i
        _sel = j
        SelectImage(j)
    End Sub

    Private Shared Sub Swap(Of T)(list As List(Of T), i As Integer, j As Integer)
        Dim tmp = list(i)
        list(i) = list(j)
        list(j) = tmp
    End Sub

    '---------------------------------------------------------------------
    ' 點位
    '---------------------------------------------------------------------

    Private Sub WireEditor(ed As MorphEditor, isA As Boolean)
        AddHandler ed.EditStarting, Sub() PushUndo()
        AddHandler ed.PointMoved, Sub(i) Rebuild()
        AddHandler ed.SelectionChanged, Sub(i)
                                            _editA.Selected = i
                                            _editB.Selected = i
                                            _editA.Invalidate()
                                            _editB.Invalidate()
                                            Dim pts = _points(If(isA, _pair, Math.Min(_points.Count - 1, _pair + 1)))
                                            If i >= 0 AndAlso i < pts.Count Then _status.Text = Lang.F("第 {0} 點：{1}", i + 1, pts(i).Name)
                                        End Sub
        AddHandler ed.PointAddRequested, Sub(p) AddPair(p, isA)
        AddHandler ed.PointDeleteRequested, Sub(i) DeletePair(i)
        AddHandler ed.KeyClicked, Sub(p) If Not isA Then KeyClick(p)
    End Sub

    ''' <summary>找臉＋478 點網格（處理中視窗；用複本算，畫面照常重畫）。</summary>
    Private Function FindFaces(img As Bitmap, which As String) As List(Of FaceRegion)
        Dim copy = DirectCast(img.Clone(), Bitmap)
        Try
            Return BusyDialog.Run(Me, Lang.T("找臉中"),
                                  Function(p)
                                      p.Stage(1, 2, Lang.F("{0}：找臉", which))
                                      Dim found = New FaceDetector().Detect(copy)
                                      p.Stage(2, 2, Lang.F("{0}：臉部特徵點", which))
                                      FaceMesh.Fit(copy, found)
                                      Return found
                                  End Function, Lang.T("第一次載入模型會久一點。"))
        Finally
            copy.Dispose()
        End Try
    End Function

    Private Shared Function WhichName(i As Integer) As String
        Return Lang.F("第 {0} 張", i + 1)
    End Function

    ''' <summary>第一張：找臉放預設點；找不到臉時只放 5 個關鍵點的位置讓使用者拖。</summary>
    Private Sub ResetPointsA()
        Dim img = _images(0)
        Dim faces = FindFaces(img, WhichName(0))
        Dim face = faces.OrderByDescending(Function(f) f.Box.Width * f.Box.Height).FirstOrDefault()
        _templateA = If(face Is Nothing, Nothing, FaceMorph.DefaultPoints(face, img.Width, img.Height))
        _aHasFace = _templateA IsNot Nothing
        If _templateA Is Nothing Then
            ' 沒有臉：五個關鍵點放在中間，使用者自己拖
            Dim w = img.Width, h = img.Height
            _templateA = New List(Of MorphPoint) From {
                New MorphPoint(Lang.T("左眼"), New PointF(w * 0.38F, h * 0.42F)), New MorphPoint(Lang.T("右眼"), New PointF(w * 0.62F, h * 0.42F)),
                New MorphPoint(Lang.T("鼻尖"), New PointF(w * 0.5F, h * 0.55F)), New MorphPoint(Lang.T("左嘴角"), New PointF(w * 0.42F, h * 0.66F)),
                New MorphPoint(Lang.T("右嘴角"), New PointF(w * 0.58F, h * 0.66F))}
            _status.Text = Lang.T("第一張找不到臉：先把 5 個關鍵點拖到兩眼、鼻尖、兩嘴角，其他點自己加。")
        End If
        _points(0) = Clone(_templateA)
    End Sub

    ''' <summary>
    ''' 第 i 張（i ≥ 1）的點：是人臉→自動放（自己加的點從前一張換算過去）；不是人臉→排隊標 5 個關鍵點。
    ''' </summary>
    Private Sub AutoPointsFor(i As Integer)
        Dim img = _images(i)
        Dim faces = FindFaces(img, WhichName(i))
        Dim face = faces.OrderByDescending(Function(f) f.Box.Width * f.Box.Height).FirstOrDefault()
        Dim auto = If(face Is Nothing OrElse Not _aHasFace, Nothing, FaceMorph.DefaultPoints(face, img.Width, img.Height))
        _pendingKeys.Remove(i)
        If auto IsNot Nothing Then
            _points(i) = MergeAuto(auto, _points(i - 1), img)
            If _keyTarget = i Then _keyTarget = -1 : _keys.Clear()
            _status.Text = Lang.F("第 {0} 張也找到臉：點位已自動對好，可以再微調。", i + 1)
        Else
            _points(i) = New List(Of MorphPoint)
            If _keyTarget < 0 OrElse _keyTarget = i Then
                StartKeys(i)
            Else
                _pendingKeys.Add(i)
            End If
            _status.Text = Lang.F("第 {0} 張不是人臉：請依照下方提示，在右邊的圖上依序點 5 個關鍵點。", i + 1)
        End If
    End Sub

    ''' <summary>
    ''' 自動找到的點排成和其他張一樣的順序；自動點裡沒有的（自己加的點）用前一張附近三點的仿射換算過去。
    ''' </summary>
    Private Shared Function MergeAuto(auto As List(Of MorphPoint), prev As List(Of MorphPoint), img As Bitmap) As List(Of MorphPoint)
        Dim byName = auto.ToDictionary(Function(q) q.Name)
        Dim commonPrev = prev.Where(Function(q) byName.ContainsKey(q.Name)).ToList()
        Dim commonAuto = commonPrev.Select(Function(q) byName(q.Name)).ToList()
        Dim result As New List(Of MorphPoint)
        For Each q In prev
            Dim a As MorphPoint = Nothing
            If byName.TryGetValue(q.Name, a) Then
                result.Add(New MorphPoint(q.Name, a.Pt))
            Else
                Dim m = MapPoint(q.Pt, commonPrev, commonAuto)
                result.Add(New MorphPoint(q.Name, New PointF(Math.Max(0, Math.Min(img.Width - 1, m.X)), Math.Max(0, Math.Min(img.Height - 1, m.Y)))))
            End If
        Next
        Return result
    End Function

    Private Sub StartKeys(i As Integer)
        _keyTarget = i
        _keys.Clear()
    End Sub

    ''' <summary>還有排隊等著標關鍵點的：換到那一段。</summary>
    Private Sub NextPendingKeys()
        If _pendingKeys.Count = 0 Then Return
        Dim i = _pendingKeys(0)
        _pendingKeys.RemoveAt(0)
        StartKeys(i)
        _sel = i
        ShowPair(i - 1)
        _status.Text = Lang.F("第 {0} 張不是人臉：請依照下方提示，在右邊的圖上依序點 5 個關鍵點。", i + 1)
    End Sub

    Friend Sub KeyClick(p As PointF)
        If _keyTarget < 1 OrElse _pair + 1 <> _keyTarget Then Return
        _keys.Add(p)
        _editB.KeyMarks.Add(p)
        _editB.Invalidate()
        If _keys.Count >= FaceMorph.KeyNames.Length Then
            Dim i = _keyTarget
            PushUndo()
            _points(i) = FaceMorph.EstimateFromKeys(_points(i - 1), _keys.ToArray(), _images(i).Width, _images(i).Height)
            _keyTarget = -1
            _keys.Clear()
            _status.Text = Lang.T("其他點已依 5 個關鍵點推估過去：請把右邊的點拖到對應的位置（耳朵、輪廓、嘴…）。")
            If _pendingKeys.Count > 0 Then
                NextPendingKeys()
            Else
                ShowPair(_pair)
            End If
            Return
        End If
        UpdateGuide()
    End Sub

    Private Sub UpdateGuide()
        If _images.Count < 2 Then
            _guide.Text = Lang.T("按右上「加一張…」選第二張（要變成的圖）；之後還可以再加，連續變下去。")
        ElseIf _editB.KeyMode Then
            Dim n = FaceMorph.KeyNames(_keys.Count)
            _guide.Text = Lang.F("在第 {0} 張圖上點：{1}（{2}/5）", _keyTarget + 1, n, _keys.Count + 1) & If(n.StartsWith(Lang.T("左")), Lang.T("　※ 左＝畫面左邊"), "")
        ElseIf _keyTarget >= 0 Then
            _guide.Text = Lang.F("第 {0} 張還沒標關鍵點：在上面點它。", _keyTarget + 1)
        Else
            _guide.Text = Lang.T("拖點對齊兩張圖相同的部位。空白處點一下加點、右鍵點刪除、滾輪縮放、中鍵拖曳平移、Ctrl+Z 復原。")
        End If
    End Sub

    ''' <summary>全部重設：第一張重新放點，之後每張重新自動放點（不是人臉的依序標關鍵點）。</summary>
    Private Sub ResetPoints()
        PushUndo()
        _points(0) = Clone(_templateA)
        _keyTarget = -1
        _keys.Clear()
        _pendingKeys.Clear()
        For i = 1 To _images.Count - 1
            AutoPointsFor(i)
        Next
        If _keyTarget >= 1 Then
            _sel = _keyTarget
            ShowPair(_keyTarget - 1)
        Else
            ShowPair(_pair)
        End If
    End Sub

    Private Shared Function Clone(pts As IEnumerable(Of MorphPoint)) As List(Of MorphPoint)
        Return pts.Select(Function(q) New MorphPoint(q.Name, q.Pt)).ToList()
    End Function

    Private Sub PushUndo()
        _undo.Push(_points.Select(Function(p) Clone(p)).ToList())
        If _undo.Count > 60 Then
            Dim keep = _undo.Take(40).Reverse().ToList()
            _undo.Clear()
            For Each k In keep
                _undo.Push(k)
            Next
        End If
    End Sub

    Private Sub Undo()
        If _undo.Count = 0 Then Return
        Dim s = _undo.Pop()
        If s.Count <> _images.Count Then
            _undo.Clear() ' 加減過圖片：張數對不上的紀錄不用
            Return
        End If
        _points = s
        ShowPair(_pair)
    End Sub

    ''' <summary>
    ''' 在一張圖上加點：這一段的另一張放在「最近三個配對點的仿射」換算過去的位置，其他張沿著順序一張一張換算過去。
    ''' </summary>
    Private Sub AddPair(p As PointF, isA As Boolean)
        If Not AllPointsReady() Then Return
        PushUndo()
        Dim side = If(isA, _pair, _pair + 1), other = If(isA, _pair + 1, _pair)
        Dim n = _images.Count
        Dim pos(n - 1) As PointF
        pos(side) = p
        pos(other) = ClampTo(MapPoint(p, _points(side), _points(other)), _images(other))
        For j = Math.Max(side, other) + 1 To n - 1
            pos(j) = ClampTo(MapPoint(pos(j - 1), _points(j - 1), _points(j)), _images(j))
        Next
        For j = Math.Min(side, other) - 1 To 0 Step -1
            pos(j) = ClampTo(MapPoint(pos(j + 1), _points(j + 1), _points(j)), _images(j))
        Next
        Dim name = Lang.T("自訂") & (_points(0).Where(Function(m) m.Name.StartsWith(Lang.T("自訂"))).Count() + 1)
        For j = 0 To n - 1
            _points(j).Add(New MorphPoint(name, pos(j)))
        Next
        Dim i = _points(0).Count - 1
        _editA.Selected = i : _editB.Selected = i
        _editA.Invalidate() : _editB.Invalidate()
        _status.Text = Lang.F("加了第 {0} 點；請到另一張圖把它拖到對應的位置。", i + 1) &
                       If(n > 2, Lang.T("其他張也加了，換到那一段再對。"), "")
        Rebuild()
    End Sub

    Private Shared Function ClampTo(p As PointF, img As Bitmap) As PointF
        Return New PointF(Math.Max(0, Math.Min(img.Width - 1, p.X)), Math.Max(0, Math.Min(img.Height - 1, p.Y)))
    End Function

    Private Shared Function MapPoint(p As PointF, src As List(Of MorphPoint), dst As List(Of MorphPoint)) As PointF
        If src.Count < 3 Then Return p
        Dim near = Enumerable.Range(0, src.Count).OrderBy(Function(i) (src(i).X - p.X) ^ 2 + (src(i).Y - p.Y) ^ 2).ToList()
        ' 找不共線的三點
        For a = 0 To Math.Min(near.Count - 1, 8)
            For b = a + 1 To Math.Min(near.Count - 1, 8)
                For c = b + 1 To Math.Min(near.Count - 1, 8)
                    Dim s0 = src(near(a)).Pt, s1 = src(near(b)).Pt, s2 = src(near(c)).Pt
                    Dim den = (s1.X - s0.X) * (s2.Y - s0.Y) - (s2.X - s0.X) * (s1.Y - s0.Y)
                    If Math.Abs(den) < 4 Then Continue For
                    ' 重心座標
                    Dim u = ((p.X - s0.X) * (s2.Y - s0.Y) - (s2.X - s0.X) * (p.Y - s0.Y)) / den
                    Dim v = ((s1.X - s0.X) * (p.Y - s0.Y) - (p.X - s0.X) * (s1.Y - s0.Y)) / den
                    Dim d0 = dst(near(a)).Pt, d1 = dst(near(b)).Pt, d2 = dst(near(c)).Pt
                    Return New PointF(d0.X + u * (d1.X - d0.X) + v * (d2.X - d0.X), d0.Y + u * (d1.Y - d0.Y) + v * (d2.Y - d0.Y))
                Next
            Next
        Next
        Return p
    End Function

    Private Sub DeletePair(i As Integer)
        If i < 0 OrElse i >= _points(0).Count Then Return
        If FaceMorph.KeyNames.Contains(_points(0)(i).Name) Then
            _status.Text = Lang.F("「{0}」是對齊用的關鍵點，不能刪（可以拖動）。", _points(0)(i).Name)
            Return
        End If
        PushUndo()
        For Each pts In _points
            If i < pts.Count Then pts.RemoveAt(i)
        Next
        _editA.Selected = -1 : _editB.Selected = -1
        _editA.Invalidate() : _editB.Invalidate()
        Rebuild()
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = (Keys.Control Or Keys.Z) Then
            Undo()
            Return True
        End If
        If keyData = Keys.Delete AndAlso _editA.Selected >= 0 AndAlso (_editA.Focused OrElse _editB.Focused) Then
            DeletePair(_editA.Selected)
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    '---------------------------------------------------------------------
    ' 點位檔
    '---------------------------------------------------------------------

    Private Sub SavePoints()
        If _images.Count < 2 Then Return
        Using dlg As New SaveFileDialog With {.Title = Lang.T("存點位"), .Filter = Lang.T("變形點位|*.morph.json"), .FileName = $"{String.Join("_", _names)}.morph.json"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            SavePointsFile(dlg.FileName)
            _status.Text = Lang.T("已存點位：") & dlg.FileName
        End Using
    End Sub

    ''' <summary>存點位檔：每張的名稱、大小、點（測試也用這個）。</summary>
    Friend Sub SavePointsFile(file As String)
        Dim data As New Dictionary(Of String, Object) From {
            {"images", Enumerable.Range(0, _images.Count).Select(Function(i) New Dictionary(Of String, Object) From {
                {"name", _names(i)}, {"size", {_images(i).Width, _images(i).Height}}, {"points", _points(i)}}).ToList()}}
        IO.File.WriteAllText(file, JsonSerializer.Serialize(data, New JsonSerializerOptions With {.WriteIndented = True, .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}))
    End Sub

    Private Sub LoadPoints()
        If _images.Count < 2 Then
            _status.Text = Lang.T("先加第二張圖再讀點位。")
            Return
        End If
        Using dlg As New OpenFileDialog With {.Title = Lang.T("讀點位"), .Filter = Lang.T("變形點位|*.morph.json|所有檔案|*.*")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                LoadPointsFile(dlg.FileName)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is KeyNotFoundException
                MessageBox.Show(Me, Lang.T("無法讀取：") & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    ''' <summary>
    ''' 讀點位檔；存檔時的圖片大小和現在不同時依比例換算。新格式（images）要張數相同；
    ''' 舊格式（只有兩張：a、b）套到前兩張，第三張以後先照前一張的位置放。
    ''' </summary>
    Friend Sub LoadPointsFile(file As String)
        Using doc = JsonDocument.Parse(IO.File.ReadAllText(file))
            Dim root = doc.RootElement
            Dim read = Function(arr As JsonElement, sz As JsonElement, w As Integer, h As Integer) As List(Of MorphPoint)
                           Dim kx = w / sz(0).GetDouble(), ky = h / sz(1).GetDouble()
                           Return arr.EnumerateArray().Select(Function(e) New MorphPoint(e.GetProperty("Name").GetString(),
                               New PointF(CSng(e.GetProperty("X").GetDouble() * kx), CSng(e.GetProperty("Y").GetDouble() * ky)))).ToList()
                       End Function
            Dim lists As New List(Of List(Of MorphPoint))
            Dim imagesEl As JsonElement
            If root.TryGetProperty("images", imagesEl) Then
                Dim items = imagesEl.EnumerateArray().ToList()
                If items.Count <> _images.Count Then Throw New InvalidOperationException(Lang.F("點位檔有 {0} 張，現在有 {1} 張。", items.Count, _images.Count))
                For i = 0 To items.Count - 1
                    lists.Add(read(items(i).GetProperty("points"), items(i).GetProperty("size"), _images(i).Width, _images(i).Height))
                Next
            Else
                lists.Add(read(root.GetProperty("a"), root.GetProperty("sizeA"), _images(0).Width, _images(0).Height))
                lists.Add(read(root.GetProperty("b"), root.GetProperty("sizeB"), _images(1).Width, _images(1).Height))
            End If
            If lists.Any(Function(l) l.Count <> lists(0).Count) Then Throw New InvalidOperationException(Lang.T("兩張圖的點數不同。"))
            PushUndo()
            For i = 0 To lists.Count - 1
                _points(i) = lists(i)
            Next
            ' 舊格式只有兩張：後面幾張先照前一張的點位置放（點數要一樣），再自己拖
            For i = lists.Count To _images.Count - 1
                _points(i) = MergeAuto(New List(Of MorphPoint), _points(i - 1), _images(i))
            Next
            _keyTarget = -1 : _keys.Clear() : _pendingKeys.Clear()
            ShowPair(_pair)
            _status.Text = Lang.F("已讀點位（{0} 點）。", lists(0).Count)
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 預覽
    '---------------------------------------------------------------------

    Private Sub Rebuild()
        _previewTimer.Stop()
        _previewTimer.Start()
    End Sub

    Private Function AllPointsReady() As Boolean
        Return _images.Count >= 2 AndAlso _points.All(Function(p) p.Count = _points(0).Count) AndAlso _points(0).Count >= 3
    End Function

    Private Function Ready() As Boolean
        Return AllPointsReady() AndAlso _keyTarget < 0 AndAlso _pendingKeys.Count = 0
    End Function

    Private Function PointLists() As IList(Of IList(Of MorphPoint))
        Return _points.Select(Function(p) CType(Clone(p), IList(Of MorphPoint))).ToList()
    End Function

    Private Sub RebuildNow()
        _morph?.Dispose()
        _morph = Nothing
        If Not Ready() Then
            Dim old = _preview.Image
            _preview.Image = Nothing
            old?.Dispose()
            Return
        End If
        _morph = New FaceMorph(_images, PointLists(), 540, _align.Value / 100.0)
        RenderPreview()
    End Sub

    ''' <summary>預覽滑桿的位置換成第幾張（0..張數-1）。</summary>
    Private Function PreviewPos() As Double
        Return _t.Value / 100.0 * Math.Max(1, _images.Count - 1)
    End Function

    Private Sub UpdateTLabel()
        _tValue.Text = If(_images.Count > 2, Lang.F("{0:0.0}", PreviewPos() + 1), _t.Value & "%")
    End Sub

    Private Sub RenderPreview()
        If _morph Is Nothing Then Return
        Dim bmp = _morph.FrameBitmap(PreviewPos())
        Dim old = _preview.Image
        _preview.Image = bmp
        old?.Dispose()
    End Sub

    Private Sub TogglePlay()
        If _playTimer.Enabled Then
            _playTimer.Stop()
            _play.Text = Lang.T("播放")
        ElseIf _morph IsNot Nothing Then
            _playTimer.Start()
            _play.Text = Lang.T("停止")
        End If
    End Sub

    Private Sub PlayStep()
        ' 張數多時每格走少一點，每一段看起來一樣快
        Dim stepSize = Math.Max(1, CInt(Math.Round(4.0 / Math.Max(1, _images.Count - 1))))
        Dim v = _t.Value + _playDir * stepSize
        If v >= 100 Then v = 100 : _playDir = -1
        If v <= 0 Then v = 0 : _playDir = 1
        _t.Value = v
    End Sub

    '---------------------------------------------------------------------
    ' 輸出
    '---------------------------------------------------------------------

    Private Function Timing() As MorphTiming
        Return New MorphTiming With {.Seconds = CDbl(_seconds.Value), .Fps = Integer.Parse(CStr(_fps.SelectedItem)),
                                     .HoldStart = CDbl(_holdStart.Value), .HoldEnd = CDbl(_holdEnd.Value), .HoldBetween = CDbl(_holdBetween.Value),
                                     .PingPong = _pingPong.Checked, .Ease = _ease.Checked}
    End Function

    Private Function OutputSide() As Integer
        Return If(_size.SelectedIndex = 3, Math.Max(_images(0).Width, _images(0).Height), Integer.Parse(CStr(_size.SelectedItem)))
    End Function

    Private Function OutputName(ext As String) As String
        Return If(_images.Count = 2, Lang.F("{0}_變成_{1}{2}", _names(0), _names(1), ext), Lang.F("{0}_連續變形_{1}張{2}", _names(0), _images.Count, ext))
    End Function

    Private Sub Export(gif As Boolean)
        If Not Ready() Then
            _status.Text = Lang.T("先加第二張圖、標好點位。")
            Return
        End If
        Directory.CreateDirectory(OutputFolder)
        Dim ext = If(gif, ".gif", ".mp4")
        Using dlg As New SaveFileDialog With {.Title = If(gif, Lang.T("存成 GIF"), Lang.T("存成 MP4")), .Filter = If(gif, Lang.T("GIF 動畫|*.gif"), Lang.T("MP4 影片|*.mp4")),
                                              .InitialDirectory = OutputFolder, .FileName = OutputName(ext)}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ExportTo(dlg.FileName, gif)
        End Using
    End Sub

    ''' <summary>在背景輸出（測試也從這裡進）；完成後狀態列顯示檔名與大小。</summary>
    Friend Sub ExportTo(file As String, gif As Boolean)
        Dim tm = Timing()
        Dim side = OutputSide()
        Dim align = _align.Value / 100.0
        Dim pts = PointLists()
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        SetExporting(True)
        _status.Text = Lang.T("產生中…")
        Dim imgs = _images.Select(Function(b) CType(b.Clone(), Bitmap)).ToList()
        Dim t0 = Environment.TickCount
        Threading.Tasks.Task.Run(Sub()
                                     Dim msg As String
                                     Try
                                         Using m As New FaceMorph(imgs, pts, side, align)
                                             Dim prog = Sub(c As Integer, n As Integer) BeginInvokeSafe(Sub() _progress.Value = Math.Min(100, c * 100 \ Math.Max(1, n)))
                                             If gif Then
                                                 m.WriteGif(file, tm, Math.Min(side, 480), Math.Min(tm.Fps, 15), prog, ct)
                                             Else
                                                 m.WriteMp4(file, tm, prog, ct)
                                             End If
                                             msg = Lang.F("完成（{0:0.0} 秒、{1:0.0} MB）：{2}", (Environment.TickCount - t0) / 1000.0, New FileInfo(file).Length / 1048576.0, file)
                                         End Using
                                     Catch ex As OperationCanceledException
                                         msg = Lang.T("已停止。")
                                         Try
                                             IO.File.Delete(file)
                                         Catch ex2 As IOException
                                         End Try
                                     Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is OpenCvSharp.OpenCVException OrElse TypeOf ex Is UnauthorizedAccessException
                                         msg = Lang.T("輸出失敗：") & ex.Message
                                     Finally
                                         For Each b In imgs
                                             b.Dispose()
                                         Next
                                     End Try
                                     BeginInvokeSafe(Sub()
                                                         _status.Text = msg
                                                         SetExporting(False)
                                                         LastExportMessage = msg
                                                     End Sub)
                                 End Sub)
    End Sub

    ''' <summary>最後一次輸出的結果訊息（測試用）。</summary>
    Friend Property LastExportMessage As String

    Private Sub BeginInvokeSafe(a As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(a)
        Catch ex As InvalidOperationException
        End Try
    End Sub

    Private Sub SetExporting(busy As Boolean)
        _saveMp4.Enabled = Not busy
        _saveGif.Enabled = Not busy
        _cancelExport.Enabled = busy
        If Not busy Then _progress.Value = 0
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _cts?.Cancel()
        _playTimer.Stop()
        _previewTimer.Stop()
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _morph?.Dispose()
            For i = 1 To _images.Count - 1 ' 第一張是目前的影像，不是這裡的
                _images(i).Dispose()
            Next
            _preview.Image?.Dispose()
            _playTimer.Dispose()
            _previewTimer.Dispose()
            _font.Dispose()
            _small.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    '---------------------------------------------------------------------
    ' 順序列
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 圖片順序：縮圖一張接一張（中間箭頭），最後一格「＋」加一張。選中的那張框起來，正在編輯的那一段底下畫一條線。
    ''' </summary>
    Private NotInheritable Class SequenceStrip
        Inherits Control

        Private Const Tile As Integer = 64
        Private Const Gap As Integer = 26
        Private ReadOnly _thumbs As New List(Of Bitmap)
        Private ReadOnly _names As New List(Of String)

        Public Property Selected As Integer
        Public Property Pair As Integer = -1
        Public Event TileClicked(index As Integer)
        Public Event AddClicked()

        Public Sub New()
            SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
            BackColor = Color.FromArgb(244, 245, 248)
            Cursor = Cursors.Hand
        End Sub

        Private Shared Function Thumb(img As Bitmap) As Bitmap
            Dim k = Math.Min(Tile / CDbl(img.Width), Tile / CDbl(img.Height))
            Dim b As New Bitmap(Math.Max(1, CInt(img.Width * k)), Math.Max(1, CInt(img.Height * k)))
            Using g = Graphics.FromImage(b)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.DrawImage(img, 0, 0, b.Width, b.Height)
            End Using
            Return b
        End Function

        Public Sub Add(img As Bitmap, name As String)
            _thumbs.Add(Thumb(img)) : _names.Add(name)
            Invalidate()
        End Sub

        Public Sub Replace(i As Integer, img As Bitmap, name As String)
            _thumbs(i).Dispose()
            _thumbs(i) = Thumb(img) : _names(i) = name
            Invalidate()
        End Sub

        Public Sub RemoveAt(i As Integer)
            _thumbs(i).Dispose()
            _thumbs.RemoveAt(i) : _names.RemoveAt(i)
            Invalidate()
        End Sub

        Public Sub Swap(i As Integer, j As Integer)
            Dim t = _thumbs(i) : _thumbs(i) = _thumbs(j) : _thumbs(j) = t
            Dim n = _names(i) : _names(i) = _names(j) : _names(j) = n
            Invalidate()
        End Sub

        Private Function TileRect(i As Integer) As Rectangle
            Return New Rectangle(4 + i * (Tile + Gap), (Height - Tile) \ 2, Tile, Tile)
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.Clear(BackColor)
            Using f As New Font(Lang.UiFont, 8.5F, FontStyle.Bold), arrowPen As New Pen(Color.FromArgb(140, 146, 158), 2) With {.EndCap = Drawing2D.LineCap.ArrowAnchor},
                  pairPen As New Pen(Color.FromArgb(47, 128, 237), 3), selPen As New Pen(Color.FromArgb(47, 128, 237), 2)
                For i = 0 To _thumbs.Count - 1
                    Dim r = TileRect(i)
                    Using bg As New SolidBrush(Color.FromArgb(225, 228, 234))
                        g.FillRectangle(bg, r)
                    End Using
                    Dim t = _thumbs(i)
                    g.DrawImage(t, r.X + (r.Width - t.Width) \ 2, r.Y + (r.Height - t.Height) \ 2, t.Width, t.Height)
                    ' 編號
                    Dim badge As New Rectangle(r.X + 2, r.Y + 2, 18, 16)
                    Using bb As New SolidBrush(Color.FromArgb(200, 30, 34, 42))
                        g.FillRectangle(bb, badge)
                    End Using
                    TextRenderer.DrawText(g, (i + 1).ToString(), f, badge, Color.White, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                    If i = Selected Then g.DrawRectangle(selPen, r.X - 2, r.Y - 2, r.Width + 3, r.Height + 3)
                    If i < _thumbs.Count - 1 Then
                        Dim ay = r.Y + r.Height \ 2
                        g.DrawLine(arrowPen, r.Right + 4, ay, r.Right + Gap - 5, ay)
                    End If
                Next
                ' 正在編輯的那一段：兩張底下連一條藍線
                If Pair >= 0 AndAlso Pair + 1 < _thumbs.Count Then
                    Dim a = TileRect(Pair), b = TileRect(Pair + 1)
                    g.DrawLine(pairPen, a.X, a.Bottom + 4, b.Right, b.Bottom + 4)
                End If
                ' 加一張
                If _thumbs.Count < MaxImages Then
                    Dim r = TileRect(_thumbs.Count)
                    Using dash As New Pen(Color.FromArgb(150, 156, 168), 1.5F) With {.DashStyle = Drawing2D.DashStyle.Dash}
                        g.DrawRectangle(dash, r)
                    End Using
                    Using big As New Font(Lang.UiFont, 18.0F)
                        TextRenderer.DrawText(g, "+", big, r, Color.FromArgb(110, 116, 128), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                    End Using
                End If
            End Using
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            If e.Button <> MouseButtons.Left Then Return
            For i = 0 To _thumbs.Count - 1
                If TileRect(i).Contains(e.Location) Then
                    RaiseEvent TileClicked(i)
                    Return
                End If
            Next
            If _thumbs.Count < MaxImages AndAlso TileRect(_thumbs.Count).Contains(e.Location) Then RaiseEvent AddClicked()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                For Each t In _thumbs
                    t.Dispose()
                Next
            End If
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
