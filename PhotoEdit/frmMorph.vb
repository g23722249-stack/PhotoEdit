Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports PhotoEdit

''' <summary>
''' 變形動畫（影像 → 變形動畫…）：第一張（目前的影像）變成第二張（例如人臉變野狼），輸出 MP4、GIF。
''' 兩張圖各有一組對應點：第一張找得到臉就自動放約 50 點；第二張是人臉也自動放，不是人臉（動物）時依序點 5 個關鍵點（兩眼、鼻尖、兩嘴角），
''' 其餘的點自動推估過去再手動拖。兩張解析度不同沒關係：輸出用第一張的比例，第二張依兩眼對齊縮放（「對齊」滑桿）。
''' </summary>
Friend Class frmMorph
    Inherits Aqua.AquaForm

    Private Const EditW As Integer = 420
    Private Const EditH As Integer = 520
    Private Const PreviewW As Integer = 330

    Private ReadOnly _help As HelpTip
    Private ReadOnly _font As New Font("Microsoft JhengHei UI", 10.0F)
    Private ReadOnly _small As New Font("Microsoft JhengHei UI", 9.0F)
    Private ReadOnly _editA As New MorphEditor()
    Private ReadOnly _editB As New MorphEditor()
    Private ReadOnly _preview As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.FromArgb(30, 30, 32)}
    Private ReadOnly _t As New Aqua.Slider With {.Minimum = 0, .Maximum = 100, .Value = 50, .ShowTicks = False}
    Private ReadOnly _tValue As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _play As New Button With {.Text = "播放"}
    Private ReadOnly _guide As New Label With {.AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(47, 128, 237)}
    Private ReadOnly _capB As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _seconds As New NumericUpDown With {.Minimum = 1, .Maximum = 20, .DecimalPlaces = 1, .Increment = 0.5D, .Value = 3}
    Private ReadOnly _fps As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _holdStart As New NumericUpDown With {.Minimum = 0, .Maximum = 5, .DecimalPlaces = 1, .Increment = 0.5D, .Value = 0.5D}
    Private ReadOnly _holdEnd As New NumericUpDown With {.Minimum = 0, .Maximum = 5, .DecimalPlaces = 1, .Increment = 0.5D, .Value = 1}
    Private ReadOnly _pingPong As New CheckBox With {.Text = "變過去再變回來", .AutoSize = True, .BackColor = Color.Transparent}
    Private ReadOnly _ease As New CheckBox With {.Text = "慢入慢出", .AutoSize = True, .Checked = True, .BackColor = Color.Transparent}
    Private ReadOnly _align As New Aqua.Slider With {.Minimum = 0, .Maximum = 100, .Value = 50, .ShowTicks = False}
    Private ReadOnly _alignValue As New Label With {.AutoSize = False, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
    Private ReadOnly _size As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _saveMp4 As New Button With {.Text = "存成 MP4…"}
    Private ReadOnly _saveGif As New Button With {.Text = "存成 GIF…"}
    Private ReadOnly _cancelExport As New Button With {.Text = "停止", .Enabled = False}
    Private ReadOnly _progress As New ProgressBar With {.Minimum = 0, .Maximum = 100}
    Private ReadOnly _status As New Label With {.AutoSize = False, .BackColor = Color.Transparent}
    Private ReadOnly _previewTimer As New System.Windows.Forms.Timer With {.Interval = 120}
    Private ReadOnly _playTimer As New System.Windows.Forms.Timer With {.Interval = 50}

    Private ReadOnly _imageA As Bitmap
    Private ReadOnly _nameA As String
    Private _imageB As Bitmap
    Private _nameB As String
    Private _pointsA As New List(Of MorphPoint)
    Private _pointsB As New List(Of MorphPoint)
    Private _templateA As List(Of MorphPoint) ' 第一張的預設點（第二張標關鍵點後推估用）
    Private _aHasFace As Boolean
    Private ReadOnly _keys As New List(Of PointF)
    Private ReadOnly _undo As New Stack(Of (A As List(Of MorphPoint), B As List(Of MorphPoint)))
    Private _morph As FaceMorph
    Private _playDir As Integer = 1
    Private _cts As CancellationTokenSource

    ''' <summary>輸出的資料夾（測試時可改）。</summary>
    Friend Shared Property OutputFolder As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoEdit", "變形動畫")

    Public Sub New(imageA As Bitmap, nameA As String, help As HelpTip)
        _imageA = imageA
        _nameA = nameA
        _help = help
        Text = "變形動畫"
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        KeyPreview = True
        Dim top = 23 + 10
        Dim xB = 16 + EditW + 10, xP = xB + EditW + 16
        ClientSize = New Size(xP + PreviewW + 16, top + 30 + EditH + 150)

        ' 上方：兩張圖的標題與換圖
        Dim capA As New Label With {.Text = "第一張（目前的影像）", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(16, top + 4)}
        _capB.SetBounds(xB, top + 4, EditW - 100, 22)
        _capB.Text = "第二張（變成這張）"
        Dim pickB As New Button With {.Text = "選圖…"}
        pickB.SetBounds(xB + EditW - 96, top, 96, 28)
        AddHandler pickB.Click, Sub() PickImageB()
        help?.SetHelp("morph.pick", pickB)
        Controls.AddRange({capA, _capB, pickB})
        Dim y = top + 32
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
        Dim resetPts As New Button With {.Text = "重設點位", .Font = _small}
        resetPts.SetBounds(16 + EditW * 2 + 10 - 324, ty, 76, 28)
        AddHandler resetPts.Click, Sub() ResetPoints()
        Dim loadPts As New Button With {.Text = "讀點位…", .Font = _small}
        loadPts.SetBounds(16 + EditW * 2 + 10 - 244, ty, 76, 28)
        AddHandler loadPts.Click, Sub() LoadPoints()
        Dim savePts As New Button With {.Text = "存點位…", .Font = _small}
        savePts.SetBounds(16 + EditW * 2 + 10 - 164, ty, 76, 28)
        AddHandler savePts.Click, Sub() SavePoints()
        Dim fit As New Button With {.Text = "整張", .Font = _small}
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
        Controls.Add(New Label With {.Text = "預覽", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(xP, top + 4)})
        _preview.SetBounds(xP, y, PreviewW, 400)
        Controls.Add(_preview)
        Dim py = y + 406
        _t.SetBounds(xP, py + 2, PreviewW - 110, 24)
        _tValue.SetBounds(xP + PreviewW - 108, py, 44, 26)
        _play.SetBounds(xP + PreviewW - 60, py - 1, 60, 28)
        AddHandler _t.ValueChanged, Sub()
                                        _tValue.Text = _t.Value & "%"
                                        RenderPreview()
                                    End Sub
        AddHandler _play.Click, Sub() TogglePlay()
        help?.SetHelpLinked("morph.preview", _t, _t, _tValue, _play)
        Controls.AddRange({_t, _tValue, _play})
        py += 36
        Dim alignCap As New Label With {.Text = "對齊", .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(xP, py + 4)}
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
        Controls.Add(lab("變形秒數", 16))
        _seconds.SetBounds(90, by, 64, 26)
        Controls.Add(lab("每秒格數", 168))
        _fps.Items.AddRange({"24", "30", "60"})
        _fps.SelectedIndex = 1
        _fps.SetBounds(242, by, 56, 26)
        Controls.Add(lab("開頭停", 312))
        _holdStart.SetBounds(370, by, 56, 26)
        Controls.Add(lab("結尾停", 436))
        _holdEnd.SetBounds(494, by, 56, 26)
        _pingPong.Location = New Point(566, by + 3)
        _ease.Location = New Point(706, by + 3)
        help?.SetHelp("morph.timing", _seconds)
        help?.SetHelp("morph.timing", _holdStart)
        help?.SetHelp("morph.timing", _holdEnd)
        help?.SetHelp("morph.pingpong", _pingPong)
        help?.SetHelp("morph.ease", _ease)
        Controls.AddRange({_seconds, _fps, _holdStart, _holdEnd, _pingPong, _ease})
        by += 36
        Controls.Add(lab("輸出大小（長邊）", 16))
        _size.Items.AddRange({"720", "1080", "1440", "第一張原尺寸"})
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
        Dim closeBtn As New Button With {.Text = "關閉", .DialogResult = DialogResult.Cancel}
        closeBtn.SetBounds(ClientSize.Width - 112, ClientSize.Height - 44, 96, 32)
        Controls.Add(closeBtn)
        CancelButton = closeBtn

        AddHandler _previewTimer.Tick, Sub()
                                           _previewTimer.Stop()
                                           RebuildNow()
                                       End Sub
        AddHandler _playTimer.Tick, Sub() PlayStep()
        _tValue.Text = "50%"
        _alignValue.Text = "50%"
        _editA.SetImage(_imageA, _pointsA)
        _editB.SetImage(Nothing, _pointsB)
        ResetPointsA()
        UpdateGuide()
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
                                            Dim pts = If(isA, _pointsA, _pointsB)
                                            If i >= 0 AndAlso i < pts.Count Then _status.Text = $"第 {i + 1} 點：{pts(i).Name}"
                                        End Sub
        AddHandler ed.PointAddRequested, Sub(p) AddPair(p, isA)
        AddHandler ed.PointDeleteRequested, Sub(i) DeletePair(i)
        AddHandler ed.KeyClicked, Sub(p) If Not isA Then KeyClick(p)
    End Sub

    ''' <summary>第一張：找臉放預設點；找不到臉時只放 5 個關鍵點的位置讓使用者拖。</summary>
    Private Sub ResetPointsA()
        Dim faces = New FaceDetector().Detect(_imageA)
        FaceMesh.Fit(_imageA, faces)
        Dim face = faces.OrderByDescending(Function(f) f.Box.Width * f.Box.Height).FirstOrDefault()
        _templateA = If(face Is Nothing, Nothing, FaceMorph.DefaultPoints(face, _imageA.Width, _imageA.Height))
        _aHasFace = _templateA IsNot Nothing
        If _templateA Is Nothing Then
            ' 沒有臉：五個關鍵點放在中間，使用者自己拖
            Dim w = _imageA.Width, h = _imageA.Height
            _templateA = New List(Of MorphPoint) From {
                New MorphPoint("左眼", New PointF(w * 0.38F, h * 0.42F)), New MorphPoint("右眼", New PointF(w * 0.62F, h * 0.42F)),
                New MorphPoint("鼻尖", New PointF(w * 0.5F, h * 0.55F)), New MorphPoint("左嘴角", New PointF(w * 0.42F, h * 0.66F)),
                New MorphPoint("右嘴角", New PointF(w * 0.58F, h * 0.66F))}
            _status.Text = "第一張找不到臉：先把 5 個關鍵點拖到兩眼、鼻尖、兩嘴角，其他點自己加。"
        End If
        _pointsA = Clone(_templateA)
        _editA.SetPoints(_pointsA)
    End Sub

    ''' <summary>第二張：人臉→自動放點；不是人臉→進入標關鍵點模式。</summary>
    Private Sub ResetPointsB()
        _keys.Clear()
        _editB.KeyMarks.Clear()
        _editB.KeyMode = False
        If _imageB Is Nothing Then
            _pointsB = New List(Of MorphPoint)
        Else
            Dim faces = New FaceDetector().Detect(_imageB)
            FaceMesh.Fit(_imageB, faces)
            Dim face = faces.OrderByDescending(Function(f) f.Box.Width * f.Box.Height).FirstOrDefault()
            Dim auto = If(face Is Nothing OrElse Not _aHasFace, Nothing, FaceMorph.DefaultPoints(face, _imageB.Width, _imageB.Height))
            If auto IsNot Nothing AndAlso auto.Count = _pointsA.Count Then
                _pointsB = auto
                _status.Text = "第二張也找到臉：點位已自動對好，可以再微調。"
            Else
                _pointsB = New List(Of MorphPoint)
                _editB.KeyMode = True
                _status.Text = "第二張不是人臉：請依照下方提示，在第二張圖上依序點 5 個關鍵點。"
            End If
        End If
        _editB.SetPoints(_pointsB)
        _editB.Invalidate()
        UpdateGuide()
        Rebuild()
    End Sub

    Private Sub KeyClick(p As PointF)
        _keys.Add(p)
        _editB.KeyMarks.Add(p)
        _editB.Invalidate()
        If _keys.Count >= FaceMorph.KeyNames.Length Then
            _editB.KeyMode = False
            _editB.KeyMarks.Clear()
            PushUndo()
            _pointsB = FaceMorph.EstimateFromKeys(_pointsA, _keys.ToArray(), _imageB.Width, _imageB.Height)
            _editB.SetPoints(_pointsB)
            _status.Text = "其他點已依 5 個關鍵點推估過去：請把第二張的點拖到對應的位置（耳朵、輪廓、嘴…）。"
            Rebuild()
        End If
        UpdateGuide()
    End Sub

    Private Sub UpdateGuide()
        If _imageB Is Nothing Then
            _guide.Text = "按右上「選圖…」選第二張（要變成的圖）。"
        ElseIf _editB.KeyMode Then
            Dim n = FaceMorph.KeyNames(_keys.Count)
            _guide.Text = $"在第二張圖上點：{n}（{_keys.Count + 1}/5）" & If(n.StartsWith("左"), "　※ 左＝畫面左邊", "")
        Else
            _guide.Text = "拖點對齊兩張圖相同的部位。空白處點一下加點、右鍵點刪除、滾輪縮放、中鍵拖曳平移、Ctrl+Z 復原。"
        End If
    End Sub

    Private Sub ResetPoints()
        PushUndo()
        _pointsA = Clone(_templateA)
        _editA.SetPoints(_pointsA)
        ResetPointsB()
    End Sub

    Private Shared Function Clone(pts As IEnumerable(Of MorphPoint)) As List(Of MorphPoint)
        Return pts.Select(Function(q) New MorphPoint(q.Name, q.Pt)).ToList()
    End Function

    Private Sub PushUndo()
        _undo.Push((Clone(_pointsA), Clone(_pointsB)))
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
        _pointsA = s.A : _pointsB = s.B
        _editA.SetPoints(_pointsA) : _editB.SetPoints(_pointsB)
        Rebuild()
    End Sub

    ''' <summary>在一張圖上加點：另一張放在「最近三個配對點的仿射」換算過去的位置。</summary>
    Private Sub AddPair(p As PointF, isA As Boolean)
        If _imageB Is Nothing OrElse _pointsB.Count <> _pointsA.Count Then Return
        PushUndo()
        Dim src = If(isA, _pointsA, _pointsB), dst = If(isA, _pointsB, _pointsA)
        Dim dstImg = If(isA, _imageB, _imageA)
        Dim q = MapPoint(p, src, dst)
        q = New PointF(Math.Max(0, Math.Min(dstImg.Width - 1, q.X)), Math.Max(0, Math.Min(dstImg.Height - 1, q.Y)))
        Dim name = "自訂" & (_pointsA.Where(Function(m) m.Name.StartsWith("自訂")).Count() + 1)
        src.Add(New MorphPoint(name, p))
        dst.Add(New MorphPoint(name, q))
        Dim i = _pointsA.Count - 1
        _editA.Selected = i : _editB.Selected = i
        _editA.Invalidate() : _editB.Invalidate()
        _status.Text = $"加了第 {i + 1} 點；請到另一張圖把它拖到對應的位置。"
        Rebuild()
    End Sub

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
        If i < 0 OrElse i >= _pointsA.Count Then Return
        If FaceMorph.KeyNames.Contains(_pointsA(i).Name) Then
            _status.Text = $"「{_pointsA(i).Name}」是對齊用的關鍵點，不能刪（可以拖動）。"
            Return
        End If
        PushUndo()
        _pointsA.RemoveAt(i)
        If i < _pointsB.Count Then _pointsB.RemoveAt(i)
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
    ' 第二張圖、點位檔
    '---------------------------------------------------------------------

    Private Sub PickImageB()
        Using dlg As New OpenFileDialog With {.Title = "選第二張圖（要變成的圖）",
                                              .Filter = "圖片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|所有檔案|*.*"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim img As Bitmap
            Try
                Using pf = PhotoFile.Open(dlg.FileName)
                    img = New Bitmap(pf.Image) ' 已依 EXIF 轉正；複製一份（PhotoFile 釋放時圖也會釋放）
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is NotSupportedException
                MessageBox.Show(Me, "無法開啟：" & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End Try
            SetImageB(img, dlg.FileName)
        End Using
    End Sub

    ''' <summary>換第二張圖（測試也從這裡進）。</summary>
    Friend Sub SetImageB(img As Bitmap, path As String)
        _imageB?.Dispose()
        _imageB = img
        _nameB = IO.Path.GetFileNameWithoutExtension(path)
        _capB.Text = $"第二張：{IO.Path.GetFileName(path)}（{img.Width}×{img.Height}）"
        _editB.SetImage(_imageB, _pointsB)
        ResetPointsB()
    End Sub

    Private Sub SavePoints()
        If _imageB Is Nothing Then Return
        Using dlg As New SaveFileDialog With {.Title = "存點位", .Filter = "變形點位|*.morph.json", .FileName = $"{_nameA}_{_nameB}.morph.json"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim data As New Dictionary(Of String, Object) From {{"a", _pointsA}, {"b", _pointsB}, {"sizeA", {_imageA.Width, _imageA.Height}}, {"sizeB", {_imageB.Width, _imageB.Height}}}
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(data, New JsonSerializerOptions With {.WriteIndented = True, .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}))
            _status.Text = "已存點位：" & dlg.FileName
        End Using
    End Sub

    Private Sub LoadPoints()
        If _imageB Is Nothing Then
            _status.Text = "先選第二張圖再讀點位。"
            Return
        End If
        Using dlg As New OpenFileDialog With {.Title = "讀點位", .Filter = "變形點位|*.morph.json|所有檔案|*.*"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                LoadPointsFile(dlg.FileName)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is KeyNotFoundException
                MessageBox.Show(Me, "無法讀取：" & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    ''' <summary>讀點位檔；存檔時的圖片大小和現在不同時依比例換算。</summary>
    Friend Sub LoadPointsFile(file As String)
        Using doc = JsonDocument.Parse(IO.File.ReadAllText(file))
            Dim root = doc.RootElement
            Dim read = Function(name As String, sizeName As String, w As Integer, h As Integer) As List(Of MorphPoint)
                           Dim sz = root.GetProperty(sizeName)
                           Dim kx = w / sz(0).GetDouble(), ky = h / sz(1).GetDouble()
                           Return root.GetProperty(name).EnumerateArray().Select(Function(e) New MorphPoint(e.GetProperty("Name").GetString(),
                               New PointF(CSng(e.GetProperty("X").GetDouble() * kx), CSng(e.GetProperty("Y").GetDouble() * ky)))).ToList()
                       End Function
            Dim a = read("a", "sizeA", _imageA.Width, _imageA.Height), b = read("b", "sizeB", _imageB.Width, _imageB.Height)
            If a.Count <> b.Count Then Throw New InvalidOperationException("兩張圖的點數不同。")
            PushUndo()
            _pointsA = a : _pointsB = b
            _editB.KeyMode = False : _editB.KeyMarks.Clear() : _keys.Clear()
            _editA.SetPoints(_pointsA) : _editB.SetPoints(_pointsB)
            UpdateGuide()
            Rebuild()
            _status.Text = $"已讀點位（{a.Count} 點）。"
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 預覽
    '---------------------------------------------------------------------

    Private Sub Rebuild()
        _previewTimer.Stop()
        _previewTimer.Start()
    End Sub

    Private Function Ready() As Boolean
        Return _imageB IsNot Nothing AndAlso _pointsA.Count = _pointsB.Count AndAlso _pointsA.Count >= 3 AndAlso Not _editB.KeyMode
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
        _morph = New FaceMorph(_imageA, _imageB, _pointsA, _pointsB, 540, _align.Value / 100.0)
        RenderPreview()
    End Sub

    Private Sub RenderPreview()
        If _morph Is Nothing Then Return
        Dim bmp = _morph.FrameBitmap(_t.Value / 100.0)
        Dim old = _preview.Image
        _preview.Image = bmp
        old?.Dispose()
    End Sub

    Private Sub TogglePlay()
        If _playTimer.Enabled Then
            _playTimer.Stop()
            _play.Text = "播放"
        ElseIf _morph IsNot Nothing Then
            _playTimer.Start()
            _play.Text = "停止"
        End If
    End Sub

    Private Sub PlayStep()
        Dim v = _t.Value + _playDir * 4
        If v >= 100 Then v = 100 : _playDir = -1
        If v <= 0 Then v = 0 : _playDir = 1
        _t.Value = v
    End Sub

    '---------------------------------------------------------------------
    ' 輸出
    '---------------------------------------------------------------------

    Private Function Timing() As MorphTiming
        Return New MorphTiming With {.Seconds = CDbl(_seconds.Value), .Fps = Integer.Parse(CStr(_fps.SelectedItem)),
                                     .HoldStart = CDbl(_holdStart.Value), .HoldEnd = CDbl(_holdEnd.Value),
                                     .PingPong = _pingPong.Checked, .Ease = _ease.Checked}
    End Function

    Private Function OutputSide() As Integer
        Return If(_size.SelectedIndex = 3, Math.Max(_imageA.Width, _imageA.Height), Integer.Parse(CStr(_size.SelectedItem)))
    End Function

    Private Sub Export(gif As Boolean)
        If Not Ready() Then
            _status.Text = "先選第二張圖、標好點位。"
            Return
        End If
        Directory.CreateDirectory(OutputFolder)
        Dim ext = If(gif, ".gif", ".mp4")
        Using dlg As New SaveFileDialog With {.Title = If(gif, "存成 GIF", "存成 MP4"), .Filter = If(gif, "GIF 動畫|*.gif", "MP4 影片|*.mp4"),
                                              .InitialDirectory = OutputFolder, .FileName = $"{_nameA}_變成_{_nameB}{ext}"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ExportTo(dlg.FileName, gif)
        End Using
    End Sub

    ''' <summary>在背景輸出（測試也從這裡進）；完成後狀態列顯示檔名與大小。</summary>
    Friend Sub ExportTo(file As String, gif As Boolean)
        Dim tm = Timing()
        Dim side = OutputSide()
        Dim align = _align.Value / 100.0
        Dim a = Clone(_pointsA), b = Clone(_pointsB)
        _cts = New CancellationTokenSource()
        Dim ct = _cts.Token
        SetExporting(True)
        _status.Text = "產生中…"
        Dim imgA = CType(_imageA.Clone(), Bitmap), imgB = CType(_imageB.Clone(), Bitmap)
        Dim t0 = Environment.TickCount
        Threading.Tasks.Task.Run(Sub()
                                     Dim msg As String
                                     Try
                                         Using m As New FaceMorph(imgA, imgB, a, b, side, align)
                                             Dim prog = Sub(c As Integer, n As Integer) BeginInvokeSafe(Sub() _progress.Value = Math.Min(100, c * 100 \ Math.Max(1, n)))
                                             If gif Then
                                                 m.WriteGif(file, tm, Math.Min(side, 480), Math.Min(tm.Fps, 15), prog, ct)
                                             Else
                                                 m.WriteMp4(file, tm, prog, ct)
                                             End If
                                             msg = $"完成（{(Environment.TickCount - t0) / 1000.0:0.0} 秒、{New FileInfo(file).Length / 1048576.0:0.0} MB）：{file}"
                                         End Using
                                     Catch ex As OperationCanceledException
                                         msg = "已停止。"
                                         Try
                                             IO.File.Delete(file)
                                         Catch ex2 As IOException
                                         End Try
                                     Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is OpenCvSharp.OpenCVException OrElse TypeOf ex Is UnauthorizedAccessException
                                         msg = "輸出失敗：" & ex.Message
                                     Finally
                                         imgA.Dispose() : imgB.Dispose()
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
            _imageB?.Dispose()
            _preview.Image?.Dispose()
            _playTimer.Dispose()
            _previewTimer.Dispose()
            _font.Dispose()
            _small.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
