Imports System.Threading

Namespace Global.AutoComposite.App
    Friend Module Program
        <STAThread>
        Sub Main(args As String())
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            AppLog.FilePath = AppLog.DefaultLogPath()
            AddHandler Application.ThreadException, Sub(s, e) ShowCrash(e.Exception)
            AddHandler AppDomain.CurrentDomain.UnhandledException, Sub(s, e) AppLog.Error("未處理的例外", TryCast(e.ExceptionObject, Exception))
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
            AppLog.Info($"AI Auto Composite Studio 啟動，.NET {Environment.Version}，{Environment.OSVersion}")
            Using f As New MainForm(args)
                Application.Run(f)
            End Using
        End Sub

        Private Sub ShowCrash(ex As Exception)
            AppLog.Error("介面執行緒發生錯誤", ex)
            MessageBox.Show($"發生錯誤：{ex.Message}{Environment.NewLine}{Environment.NewLine}詳細內容已寫入日誌：{AppLog.FilePath}",
                            "AI Auto Composite Studio", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Sub
    End Module
End Namespace
