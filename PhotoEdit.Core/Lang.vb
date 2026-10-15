Imports System.Globalization
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions

''' <summary>介面語言。</summary>
Public Enum UiLanguage
    ''' <summary>依照系統的顯示語言。</summary>
    Auto = 0
    TraditionalChinese = 1
    SimplifiedChinese = 2
    English = 3
End Enum

''' <summary>
''' 多語系（繁體中文、簡體中文、英文）：程式裡寫的是繁體中文原文，原文本身就是翻譯的鍵（同 gettext）。
''' 繁體：原文照用；簡體：Windows 的繁轉簡（LCMapStringEx）再加用語對照（檔案→文件、設定→设置…）；
''' 英文：查內嵌的 Strings.en.txt（原文⇥譯文，一行一句），查不到就顯示原文。
''' 啟動時呼叫 Init（在任何視窗、靜態名稱表建立之前）；沒呼叫時一律原文（自我測試就是這樣）。
''' </summary>
Public NotInheritable Class Lang
    Private Sub New()
    End Sub

    Private Shared _current As UiLanguage = UiLanguage.TraditionalChinese
    Private Shared _english As Dictionary(Of String, String)
    Private Shared ReadOnly _cache As New Dictionary(Of String, String)()
    Private Shared ReadOnly _lock As New Object()

    ''' <summary>實際使用的語言（Auto 已換成系統的語言）。</summary>
    Public Shared ReadOnly Property Current As UiLanguage
        Get
            Return _current
        End Get
    End Property

    ''' <summary>介面字型：簡體用微軟雅黑（字形是大陸寫法），其他用微軟正黑體。</summary>
    Public Shared ReadOnly Property UiFont As String
        Get
            Return If(_current = UiLanguage.SimplifiedChinese, "Microsoft YaHei UI", "Microsoft JhengHei UI")
        End Get
    End Property

    ''' <summary>系統的顯示語言對應到哪一種：繁體（台灣、香港、澳門、Hant）、簡體（大陸、新加坡、Hans）、其他都是英文。</summary>
    Public Shared Function FromSystem() As UiLanguage
        Return FromCulture(CultureInfo.CurrentUICulture)
    End Function

    Public Shared Function FromCulture(c As CultureInfo) As UiLanguage
        Dim n = c.Name.ToLowerInvariant()
        If n.StartsWith("zh") Then
            If n.Contains("hant") OrElse n.EndsWith("-tw") OrElse n.EndsWith("-hk") OrElse n.EndsWith("-mo") Then Return UiLanguage.TraditionalChinese
            Return UiLanguage.SimplifiedChinese
        End If
        Return UiLanguage.English
    End Function

    ''' <summary>設定語言（Auto＝依系統）。之後所有 T()／F() 都用這個語言。</summary>
    Public Shared Sub Init(choice As UiLanguage)
        SyncLock _lock
            _current = If(choice = UiLanguage.Auto, FromSystem(), choice)
            _cache.Clear()
            If _current = UiLanguage.English AndAlso _english Is Nothing Then _english = LoadTable("Strings.en.txt")
        End SyncLock
    End Sub

    ''' <summary>翻譯一句（原文是繁體中文）。</summary>
    Public Shared Function T(text As String) As String
        If String.IsNullOrEmpty(text) OrElse _current = UiLanguage.TraditionalChinese Then Return text
        SyncLock _lock
            Dim r As String = Nothing
            If _cache.TryGetValue(text, r) Then Return r
            r = If(_current = UiLanguage.English, English(text), Simplified(text))
            _cache(text) = r
            Return r
        End SyncLock
    End Function

    ''' <summary>同一個原文在不同地方要不同譯法時（色調：濾鏡分類 Tone、滑桿 Tint）：英文先查「情境|原文」，查不到才照一般翻譯。</summary>
    Public Shared Function TC(context As String, text As String) As String
        If _current = UiLanguage.English AndAlso _english IsNot Nothing Then
            Dim r As String = Nothing
            If _english.TryGetValue(context & "|" & text, r) Then Return r
        End If
        Return T(text)
    End Function

    ''' <summary>翻譯有參數的句子：原文用 {0}、{1:0.0}… 標出參數的位置（譯文可以換順序）。</summary>
    Public Shared Function F(text As String, ParamArray args As Object()) As String
        Return String.Format(CultureInfo.CurrentCulture, T(text), args)
    End Function

    '---------------------------------------------------------------------
    ' 英文
    '---------------------------------------------------------------------

    Private Shared Function English(text As String) As String
        Dim r As String = Nothing
        If _english IsNot Nothing AndAlso _english.TryGetValue(text, r) Then Return r
        ' 有換行的長句：一行一行查
        If text.Contains(vbLf) Then
            Dim lines = text.Replace(vbCrLf, vbLf).Split(vbLf(0))
            If lines.Length > 1 AndAlso lines.All(Function(l) l.Length = 0 OrElse (_english IsNot Nothing AndAlso _english.ContainsKey(l))) Then
                Return String.Join(vbCrLf, lines.Select(Function(l) If(l.Length = 0, l, _english(l))))
            End If
        End If
        Return text
    End Function

    ''' <summary>內嵌的翻譯表：每行「原文⇥譯文」；譯文裡的 \n 是換行。# 開頭是註解。</summary>
    Private Shared Function LoadTable(name As String) As Dictionary(Of String, String)
        Dim d As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim asm = GetType(Lang).Assembly
        Dim res = asm.GetManifestResourceNames().FirstOrDefault(Function(n) n.EndsWith(name, StringComparison.OrdinalIgnoreCase))
        If res Is Nothing Then Return d
        Using s = asm.GetManifestResourceStream(res), rd As New IO.StreamReader(s, Text.Encoding.UTF8)
            Do
                Dim line = rd.ReadLine()
                If line Is Nothing Then Exit Do
                If line.Length = 0 OrElse line.StartsWith("#") Then Continue Do
                Dim tab = line.IndexOf(ControlChars.Tab)
                If tab <= 0 Then Continue Do
                Dim key = Unescape(line.Substring(0, tab)), value = Unescape(line.Substring(tab + 1))
                d(key) = value
            Loop
        End Using
        Return d
    End Function

    ''' <summary>\n 換行、\t 定位字元、\\ 反斜線；其他反斜線照原樣（路徑 stick\theme\ 要寫成 stick\\theme\\）。</summary>
    Friend Shared Function Unescape(s As String) As String
        If s.IndexOf("\"c) < 0 Then Return s
        Dim sb As New Text.StringBuilder(s.Length)
        Dim i = 0
        While i < s.Length
            Dim c = s(i)
            If c = "\"c AndAlso i + 1 < s.Length Then
                Select Case s(i + 1)
                    Case "n"c : sb.Append(vbCrLf) : i += 2 : Continue While
                    Case "t"c : sb.Append(vbTab) : i += 2 : Continue While
                    Case "\"c : sb.Append("\"c) : i += 2 : Continue While
                End Select
            End If
            sb.Append(c)
            i += 1
        End While
        Return sb.ToString()
    End Function

    ''' <summary>翻譯表的寫法（給工具用）：反斜線寫成 \\、換行寫成 \n。</summary>
    Public Shared Function Escape(s As String) As String
        Return s.Replace("\", "\\").Replace(vbCrLf, "\n").Replace(vbLf, "\n").Replace(vbTab, "\t")
    End Function

    '---------------------------------------------------------------------
    ' 簡體
    '---------------------------------------------------------------------

    ''' <summary>兩岸用語不同的詞（繁體原文 → 繁體寫的大陸用語，之後再轉成簡體字）。長的先換。</summary>
    Private Shared ReadOnly Terms As (From As String, [To] As String)() = {
        ("剪貼簿", "剪貼板"), ("資料夾", "文件夾"), ("檔案總管", "資源管理器"), ("檔案", "文件"), ("檔名", "文件名"), ("副檔名", "擴展名"),
        ("設定", "設置"), ("預設", "默認"), ("視窗", "窗口"), ("螢幕", "屏幕"), ("滑鼠", "鼠標"), ("游標", "光標"),
        ("程式", "程序"), ("選單", "菜單"), ("列印", "打印"), ("解析度", "分辨率"), ("品質", "質量"), ("匯出", "導出"), ("匯入", "導入"),
        ("復原", "撤銷"), ("圖示", "圖標"), ("字型", "字體"), ("位元", "位"), ("支援", "支持"), ("網路", "網絡"), ("影片", "視頻"),
        ("資訊", "信息"), ("訊息", "消息"), ("存檔", "保存"), ("開啟", "打開"), ("物件", "對象"), ("介面", "界面"), ("記憶體", "內存"),
        ("硬碟", "硬盤"), ("伺服器", "服務器"), ("數位", "數碼"), ("捷徑", "快捷方式"), ("對話方塊", "對話框"), ("核取方塊", "復選框"),
        ("下拉式", "下拉"), ("相容", "兼容"), ("擷取", "截取"), ("貼上", "粘貼"), ("剪下", "剪切"), ("自訂", "自定義"), ("載入", "加載"),
        ("圖層清單", "圖層列表"), ("清單", "列表"), ("執行", "運行"), ("搜尋", "搜索"), ("最佳化", "優化"), ("像素", "像素"),
        ("軟體", "軟件"), ("硬體", "硬件"), ("顯示卡", "顯卡"), ("繪圖板", "數位板"), ("繪圖筆", "數位筆"), ("觸控", "觸摸"),
        ("全螢幕", "全屏"), ("縮圖", "縮略圖"), ("頁籤", "標籤頁"), ("分頁", "標籤頁"), ("項目", "項目"), ("文字方塊", "文本框"),
        ("文字", "文字"), ("色票", "色板"), ("色盤", "調色板"), ("筆刷", "筆刷"), ("點陣", "位圖"), ("向量", "矢量"), ("線上", "在線"),
        ("取消", "取消"), ("確定", "確定"), ("完成", "完成"), ("訊號", "信號"), ("透過", "通過"), ("資料", "數據"), ("預覽", "預覽")}

    Private Const LCMAP_SIMPLIFIED_CHINESE As UInteger = &H2000000UI

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LCMapStringEx(lpLocaleName As String, dwMapFlags As UInteger, lpSrcStr As String, cchSrc As Integer,
                                          <Out> lpDestStr As Char(), cchDest As Integer, lpVersionInformation As IntPtr,
                                          lpReserved As IntPtr, sortHandle As IntPtr) As Integer
    End Function

    ' Windows 10 1903 以後內建的 ICU：Hant-Hans 轉換比 LCMapString 完整（後→后、黃→黄、偵→侦…）。
    <DllImport("icu.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function utrans_openU(id As String, idLength As Integer, dir As Integer, rules As IntPtr, rulesLength As Integer,
                                         parseError As IntPtr, ByRef status As Integer) As IntPtr
    End Function

    <DllImport("icu.dll", CharSet:=CharSet.Unicode)>
    Private Shared Sub utrans_transUChars(trans As IntPtr, <[In], Out> text As Char(), ByRef textLength As Integer, textCapacity As Integer,
                                          start As Integer, ByRef limit As Integer, ByRef status As Integer)
    End Sub

    Private Shared _icu As IntPtr, _icuTried As Boolean
    Private Shared ReadOnly _icuLock As New Object()

    Private Shared Function IcuSimplified(s As String) As String
        SyncLock _icuLock
            Try
                If Not _icuTried Then
                    _icuTried = True
                    Dim st = 0
                    Dim h = utrans_openU("Hant-Hans", 9, 0, IntPtr.Zero, 0, IntPtr.Zero, st)
                    If st <= 0 Then _icu = h
                End If
                If _icu = IntPtr.Zero Then Return Nothing
                Dim buf(s.Length * 2 + 16) As Char
                s.CopyTo(0, buf, 0, s.Length)
                Dim len = s.Length, limit = s.Length, status = 0
                utrans_transUChars(_icu, buf, len, buf.Length, 0, limit, status)
                If status > 0 Then Return Nothing
                Return New String(buf, 0, len)
            Catch ex As Exception When TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException
                _icu = IntPtr.Zero
                Return Nothing
            End Try
        End SyncLock
    End Function

    ''' <summary>繁體 → 簡體（先換用語再換字；ICU 優先，沒有時用 LCMapString）。</summary>
    Public Shared Function Simplified(text As String) As String
        Dim s = text
        For Each term In Terms
            If term.From <> term.To Then s = s.Replace(term.From, term.To)
        Next
        Dim icu = IcuSimplified(s)
        If icu IsNot Nothing Then Return icu
        Try
            Dim buf(s.Length * 2 + 16) As Char
            Dim n = LCMapStringEx("zh-CN", LCMAP_SIMPLIFIED_CHINESE, s, s.Length, buf, buf.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
            If n > 0 Then Return New String(buf, 0, n)
        Catch ex As Exception When TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException
        End Try
        Return s
    End Function
End Class
