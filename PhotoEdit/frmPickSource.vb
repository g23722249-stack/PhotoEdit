Imports System.IO
Imports PhotoEdit

''' <summary>
''' 影像選單的功能在沒有開啟影像時，先用這個小視窗選一張圖：開啟檔案、從剪貼簿貼上，或把圖檔拖進來。
''' 選好的圖由主畫面開啟，功能接著照平常的方式進行。
''' </summary>
Friend Class frmPickSource
    Inherits Aqua.AquaForm

    Private ReadOnly _font As New Font(Lang.UiFont, 10.0F)
    Private ReadOnly _bold As New Font(Lang.UiFont, 11.0F, FontStyle.Bold)
    Private ReadOnly _paste As New Button With {.Text = Lang.T("從剪貼簿貼上"), .UseVisualStyleBackColor = True}

    ''' <summary>選的檔案（開啟檔案或拖進來的）；貼上時 Nothing。</summary>
    Public Property FileName As String
    ''' <summary>貼上時的剪貼簿內容；選檔案時 Nothing。</summary>
    Public Property ClipboardData As IDataObject

    ''' <param name="feature">功能名稱（標題列與說明用），例如「黑白照片上色」。</param>
    Public Sub New(feature As String)
        Text = feature
        Font = _font
        WindowBorderStyle = Aqua.FormBorderStyle.Fixed
        RoundCorners = False
        MinButton = False
        MaxButton = False
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        AllowDrop = True
        Dim top = 23 + 14
        ClientSize = New Size(470, top + 150)
        Dim title As New Label With {.Text = Lang.F("「{0}」要處理哪一張圖？", feature), .AutoSize = False, .Font = _bold, .BackColor = Color.Transparent}
        title.SetBounds(20, top, 430, 26)
        Dim desc As New Label With {.Text = Lang.T("目前沒有開啟影像。開啟一張圖片、從剪貼簿貼上，或直接把圖檔拖進這個視窗。"), .AutoSize = False, .BackColor = Color.Transparent, .ForeColor = Color.FromArgb(80, 86, 98)}
        desc.SetBounds(20, top + 30, 430, 44)
        Dim open As New Button With {.Text = Lang.T("開啟圖片…"), .UseVisualStyleBackColor = True}
        open.SetBounds(20, top + 86, 150, 34)
        open.Font = New Font(_font, FontStyle.Bold)
        _paste.SetBounds(178, top + 86, 150, 34)
        Dim cancel As New Button With {.Text = Lang.T("取消"), .DialogResult = DialogResult.Cancel, .UseVisualStyleBackColor = True}
        cancel.SetBounds(ClientSize.Width - 20 - 100, top + 86, 100, 34)
        AddHandler open.Click, Sub() OpenFile()
        AddHandler _paste.Click, Sub() PasteClipboard()
        Controls.AddRange({title, desc, open, _paste, cancel})
        CancelButton = cancel
        AcceptButton = open
        Try
            _paste.Enabled = Clipboard.ContainsImage() OrElse Clipboard.ContainsFileDropList() OrElse Clipboard.ContainsData("PNG")
        Catch ex As Runtime.InteropServices.ExternalException
            _paste.Enabled = True ' 剪貼簿被占用：按了再說
        End Try
        ThemeManager.Attach(Me)
    End Sub

    Private Sub OpenFile()
        Using dlg As New OpenFileDialog With {.Title = Lang.T("開啟圖片"),
                                              .Filter = Lang.T("圖片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|所有檔案|*.*")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            UseFile(dlg.FileName)
        End Using
    End Sub

    ''' <summary>選定檔案並關閉（測試也用這個）。</summary>
    Friend Sub UseFile(file As String)
        FileName = file
        ClipboardData = Nothing
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub PasteClipboard()
        Try
            Dim data = Clipboard.GetDataObject()
            If data Is Nothing OrElse (frmEditor.ImageFile(data) Is Nothing AndAlso Not data.GetDataPresent(DataFormats.Bitmap) AndAlso Not data.GetDataPresent("PNG")) Then
                MessageBox.Show(Me, Lang.T("剪貼簿裡沒有圖片。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            ClipboardData = data
            FileName = Nothing
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Runtime.InteropServices.ExternalException
            MessageBox.Show(Me, Lang.T("剪貼簿正被其他程式使用，請稍後再試。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Try
    End Sub

    Protected Overrides Sub OnDragEnter(e As DragEventArgs)
        MyBase.OnDragEnter(e)
        e.Effect = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
    End Sub

    Protected Overrides Sub OnDragDrop(e As DragEventArgs)
        MyBase.OnDragDrop(e)
        Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If files IsNot Nothing AndAlso files.Length > 0 AndAlso File.Exists(files(0)) Then UseFile(files(0))
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _font.Dispose()
            _bold.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
