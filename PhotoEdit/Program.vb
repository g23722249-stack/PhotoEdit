Friend Module Program

    <STAThread>
    Friend Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        ' 介面語言：設定裡選的，沒選就依系統（要在建立任何視窗、名稱表之前）
        Dim settings = AppSettings.Load()
        Global.PhotoEdit.Lang.Init(CType(settings.Language, Global.PhotoEdit.UiLanguage))
        ' 視窗風格（Aqua.Net 的標題列）：所有視窗跟著這個預設
        Aqua.FormChrome.DefaultStyle = If(settings.WindowStyle = 1, Aqua.ChromeStyle.KdePlasma, Aqua.ChromeStyle.MacOS)
        Aqua.PickerText.Translate = Function(s) Global.PhotoEdit.Lang.TC("picker", s)  ' 色彩選擇器（Aqua.Net）的文字也跟著翻譯
        Application.Run(New frmEditor(If(args.Length > 0, args(0), Nothing)))
    End Sub

End Module
