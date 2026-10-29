Imports PhotoEdit

''' <summary>
''' 說明 → 關於 PhotoEdit：左邊 logo（Assets\logo.png 內嵌，320 像素、圓外透明，顯示成 176 像素），右邊名稱、版本與說明。
''' </summary>
Friend Class frmAbout
    Inherits Aqua.AquaForm

    Private Const LogoSize As Integer = 176
    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _titleFont As New Font(Lang.UiFont, 20.0F, FontStyle.Bold)
    Private ReadOnly _logo As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.Transparent}

    ''' <summary>內嵌的 logo（找不到時 Nothing）。</summary>
    Friend Shared Function LoadLogo() As Image
        Using s = GetType(frmAbout).Assembly.GetManifestResourceStream("PhotoEditApp.logo.png")
            If s Is Nothing Then Return Nothing
            Using img = Image.FromStream(s)
                Return New Bitmap(img)
            End Using
        End Using
    End Function

    Public Sub New(version As String)
        Text = Lang.T("關於 PhotoEdit")
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        RoundCorners = False
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        Dim top = 23 + 20
        Dim textX = 24 + LogoSize + 24, textW = 360
        _logo.Image = LoadLogo()
        _logo.SetBounds(24, top, LogoSize, LogoSize)
        Controls.Add(_logo)

        Dim y = top
        Dim title As New Label With {.Text = "PhotoEdit", .Font = _titleFont, .AutoSize = True, .BackColor = Color.Transparent, .Location = New Point(textX - 3, y)}
        Controls.Add(title)
        y += title.PreferredHeight + 2
        Dim addText = Sub(text As String, color As Color, gapAfter As Integer)
                          Dim l As New Label With {.Text = text, .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = color}
                          Dim h = l.GetPreferredSize(New Size(textW, 0)).Height
                          l.SetBounds(textX, y, textW, h)
                          Controls.Add(l)
                          y += h + gapAfter
                      End Sub
        Dim muted = Color.FromArgb(105, 110, 120)
        addText(Lang.F("版本 {0}　非破壞性照片編輯器", version), muted, 14)
        addText(Lang.T("「存檔」存成 PhotoEdit 專案（.pedx），保留所有圖層，下次載入可繼續修改；原始照片不會被修改。"), ForeColor, 6)
        addText(Lang.T("要得到合併後的圖檔，請用「另存新檔」或「匯出」存成 PNG／JPG／BMP。"), ForeColor, 14)
        addText(Lang.T("人臉偵測：YuNet（OpenCV Zoo，Apache-2.0）"), muted, 2)
        addText(Lang.T("其他 AI 模型（去背、臉部網格、AI 重繪）的用途與授權：設定 → 模型。"), muted, 0)

        ' logo 和右邊的文字區垂直置中
        _logo.Top = top + Math.Max(0, (y - top - LogoSize) \ 2)

        Dim ok As New Button With {.Text = Lang.T("確定"), .DialogResult = DialogResult.OK}
        Dim bottom = Math.Max(top + LogoSize, y) + 20
        ClientSize = New Size(textX + textW + 24, bottom + 32 + 16)
        ok.SetBounds(ClientSize.Width - 24 - 96, bottom, 96, 32)
        Controls.Add(ok)
        AcceptButton = ok : CancelButton = ok
        ThemeManager.Attach(Me)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _logo.Image?.Dispose()
            _font.Dispose()
            _titleFont.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
