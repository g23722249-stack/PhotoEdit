Imports PhotoEdit

''' <summary>
''' 繪圖分頁的油漆桶：點一下把顏色相近的區域填滿，畫在目前選取的點陣圖層上（沒有選圖層時自動新增）。
''' 每次填色記成點陣圖層的一筆操作（種子點＋選項），照片或線稿改了會自動重算。
''' 選項在畫布上方的操作列（同 Photoshop 的選項列），記在 settings.json：
''' 容許度、相鄰、取樣（所有圖層／目前圖層）、補缺口、擴張、柔邊、填入（目前筆刷／原照片）。
''' </summary>
Partial Friend Class frmEditor

    Private ReadOnly _bucketBar As New FlowLayoutPanel()
    Private ReadOnly _bucketTolerance As New NumericUpDown()
    Private ReadOnly _bucketContiguous As New CheckBox()
    Private ReadOnly _bucketSample As New ComboBox()
    Private ReadOnly _bucketGap As New NumericUpDown()
    Private ReadOnly _bucketExpand As New NumericUpDown()
    Private ReadOnly _bucketSoft As New CheckBox()
    Private ReadOnly _bucketSource As New ComboBox()

    Private Function BucketOptions() As BucketFill
        If _appSettings.Bucket Is Nothing Then _appSettings.Bucket = New BucketFill()
        Return _appSettings.Bucket
    End Function

    ''' <summary>操作列上的油漆桶選項（只在選了油漆桶時顯示，取代橡皮擦按鈕的位置）。</summary>
    Private Sub BuildBucketBar(done As Control)
        _bucketBar.Dock = DockStyle.Left
        _bucketBar.AutoSize = True
        _bucketBar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        _bucketBar.WrapContents = False
        _bucketBar.Padding = New Padding(6, 0, 0, 0)
        _bucketBar.BackColor = Color.Transparent
        _bucketBar.Visible = False
        Dim caption = Function(text As String) New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(5, 7, 1, 0), .BackColor = Color.Transparent}
        Dim updown = Sub(n As NumericUpDown, max As Integer, width As Integer)
                         n.Minimum = 0 : n.Maximum = max : n.Width = width
                         n.Margin = New Padding(0, 3, 0, 0)
                     End Sub
        updown(_bucketTolerance, 255, 48)
        updown(_bucketGap, 10, 38)
        updown(_bucketExpand, 10, 38)
        For Each cb In {_bucketContiguous, _bucketSoft}
            cb.AutoSize = True
            cb.Margin = New Padding(6, 6, 0, 0)
            cb.BackColor = Color.Transparent
        Next
        _bucketContiguous.Text = "相鄰"
        _bucketSoft.Text = "柔邊"
        For Each combo In {_bucketSample, _bucketSource}
            combo.DropDownStyle = ComboBoxStyle.DropDownList
            combo.Width = 78
            combo.Margin = New Padding(0, 3, 0, 0)
        Next
        _bucketSample.Items.AddRange({"所有圖層", "目前圖層"})
        _bucketSource.Items.AddRange({"筆刷", "原照片"})

        Dim tolCaption = caption("容許度"), sampleCaption = caption("取樣"), gapCaption = caption("補缺口"),
            expandCaption = caption("擴張"), sourceCaption = caption("填入")
        _bucketBar.Controls.AddRange({tolCaption, _bucketTolerance, _bucketContiguous, sampleCaption, _bucketSample,
                                      gapCaption, _bucketGap, expandCaption, _bucketExpand, _bucketSoft, sourceCaption, _bucketSource})
        _help.SetHelpLinked("bucket.tolerance", _bucketTolerance, tolCaption, _bucketTolerance)
        _help.SetHelp("bucket.contiguous", _bucketContiguous)
        _help.SetHelpLinked("bucket.sample", _bucketSample, sampleCaption, _bucketSample)
        _help.SetHelpLinked("bucket.gap", _bucketGap, gapCaption, _bucketGap)
        _help.SetHelpLinked("bucket.expand", _bucketExpand, expandCaption, _bucketExpand)
        _help.SetHelp("bucket.soft", _bucketSoft)
        _help.SetHelpLinked("bucket.source", _bucketSource, sourceCaption, _bucketSource)

        SyncBucketBar()
        AddHandler _bucketTolerance.ValueChanged, Sub() SetBucketOption(Sub(b) b.Tolerance = CInt(_bucketTolerance.Value))
        AddHandler _bucketContiguous.CheckedChanged, Sub() SetBucketOption(Sub(b) b.Contiguous = _bucketContiguous.Checked)
        AddHandler _bucketSample.SelectedIndexChanged, Sub() SetBucketOption(Sub(b) b.Sample = CType(Math.Max(0, _bucketSample.SelectedIndex), BucketSample))
        AddHandler _bucketGap.ValueChanged, Sub() SetBucketOption(Sub(b) b.GapClose = CInt(_bucketGap.Value))
        AddHandler _bucketExpand.ValueChanged, Sub() SetBucketOption(Sub(b) b.Expand = CInt(_bucketExpand.Value))
        AddHandler _bucketSoft.CheckedChanged, Sub() SetBucketOption(Sub(b) b.AntiAlias = _bucketSoft.Checked)
        AddHandler _bucketSource.SelectedIndexChanged, Sub() SetBucketOption(Sub(b) b.Source = CType(Math.Max(0, _bucketSource.SelectedIndex), BucketSource))
        ' 停靠順序看 z-order（索引大的先停靠）：「完成」先停靠在右邊，選項列接在復原／重做右邊，窄的時候也不會把「完成」擠掉
        _drawBar.Controls.Add(_bucketBar)
        _drawBar.Controls.SetChildIndex(_bucketBar, _drawBar.Controls.GetChildIndex(done))
    End Sub

    Private _syncingBucket As Boolean

    Private Sub SyncBucketBar()
        Dim b = BucketOptions()
        _syncingBucket = True
        Try
            _bucketTolerance.Value = Math.Max(0, Math.Min(255, b.Tolerance))
            _bucketContiguous.Checked = b.Contiguous
            _bucketSample.SelectedIndex = Math.Max(0, Math.Min(1, CInt(b.Sample)))
            _bucketGap.Value = Math.Max(0, Math.Min(10, b.GapClose))
            _bucketExpand.Value = Math.Max(0, Math.Min(10, b.Expand))
            _bucketSoft.Checked = b.AntiAlias
            _bucketSource.SelectedIndex = Math.Max(0, Math.Min(1, CInt(b.Source)))
        Finally
            _syncingBucket = False
        End Try
    End Sub

    Private Sub SetBucketOption(change As Action(Of BucketFill))
        If _syncingBucket Then Return
        change(BucketOptions())
        _appSettings.Save()
    End Sub

    ''' <summary>點一下填色：畫在選取的點陣圖層；沒選圖層時新增一個；選到向量圖層時問要點陣化還是新增點陣圖層。</summary>
    Private Sub BucketClick(u As PointF)
        Dim aspect = PhotoAspect()
        If u.X < 0 OrElse u.Y < 0 OrElse u.X > aspect OrElse u.Y > 1 Then
            SetStatusMessage("油漆桶：在照片範圍內點一下。")
            Return
        End If
        Dim sel As DrawLayer = Nothing
        If Not PixelToolTarget("填色", sel) Then Return
        Dim op As New DrawLayer With {.Shape = DrawShape.Bucket, .Seed = _drawRandom.Next(1, 100000), .Param1 = Math.Round(aspect, 5)}
        op.CopyStyleFrom(_drawStyle)
        op.Shadow = False
        Dim b = BucketOptions().Clone()
        Dim dx = If(sel IsNot Nothing, sel.X, 0), dy = If(sel IsNot Nothing, sel.Y, 0) ' 圖層移動過時扣掉位移
        b.X = Math.Round(u.X - dx, 5) : b.Y = Math.Round(u.Y - dy, 5)
        op.Bucket = b
        AddPixelOp(op, sel)
        SetStatusMessage(If(b.Source = BucketSource.Photo, "已用原照片的顏色填回。", "已填色。") &
                         "（填錯了按 Ctrl+Z；漏到外面可以調高「補缺口」，邊緣有白邊可以調高「擴張」）")
    End Sub

    '---------------------------------------------------------------------
    ' 游標：油漆桶，熱點在顏料滴的下緣
    '---------------------------------------------------------------------

    Private Shared _bucketCursor As Cursor

    Private Shared Function BucketCursor() As Cursor
        If _bucketCursor IsNot Nothing Then Return _bucketCursor
        Try
            Using bmp As New Bitmap(32, 32, Imaging.PixelFormat.Format32bppArgb)
                Using g = Graphics.FromImage(bmp)
                    ' 白色外框讓深色畫面上也看得到
                    For Each off In {New Point(-1, 0), New Point(1, 0), New Point(0, -1), New Point(0, 1)}
                        DrawIcons.DrawTool(g, CInt(DrawShape.Bucket), New RectangleF(off.X, off.Y, 32, 32), Color.White)
                    Next
                    DrawIcons.DrawTool(g, CInt(DrawShape.Bucket), New RectangleF(0, 0, 32, 32), Color.Black)
                End Using
                _bucketCursor = CursorFactory.Create(bmp, 7, 29)
            End Using
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is Runtime.InteropServices.ExternalException
            _bucketCursor = Cursors.Cross
        End Try
        Return _bucketCursor
    End Function
End Class
