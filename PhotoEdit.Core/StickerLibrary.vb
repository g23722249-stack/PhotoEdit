Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO

''' <summary>
''' 貼圖庫：程式資料夾下的 stick\，每個子資料夾是一個主題，裡面的圖片就是貼圖。
''' 編輯配方只記「主題\檔名」，圖片在第一次使用時讀進快取（不鎖住檔案）。
''' </summary>
Public NotInheritable Class StickerLibrary
    Private Sub New()
    End Sub

    ''' <summary>內建向量貼圖的主題名稱（不是資料夾）。</summary>
    Public Const BuiltInTheme As String = "內建"

    Public Shared ReadOnly Extensions As String() = {".png", ".gif", ".bmp", ".jpg", ".jpeg"}

    Private Shared _root As String = Path.Combine(AppContext.BaseDirectory, "stick")
    Private Shared ReadOnly Cache As New Dictionary(Of String, Bitmap)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly CacheLock As New Object()
    ''' <summary>專案檔帶進來的貼圖副本：「主題\檔名」→ 完整路徑。本機 stick 資料夾沒有這張貼圖時才用。</summary>
    Private Shared ReadOnly Fallbacks As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>stick 資料夾的完整路徑（測試時可改）。</summary>
    Public Shared Property Root As String
        Get
            Return _root
        End Get
        Set(value As String)
            _root = value
            ClearCache()
        End Set
    End Property

    Public Shared ReadOnly Property RootExists As Boolean
        Get
            Return Directory.Exists(_root)
        End Get
    End Property

    ''' <summary>主題資料夾名稱，依名稱排序。</summary>
    Public Shared Function Themes() As List(Of String)
        If Not RootExists Then Return New List(Of String)()
        Return Directory.GetDirectories(_root).
            Select(Function(d) Path.GetFileName(d)).
            Where(Function(n) Not n.StartsWith(".")).
            OrderBy(Function(n) n, StringComparer.CurrentCulture).ToList()
    End Function

    ''' <summary>主題裡的貼圖，回傳「主題\檔名」相對路徑；封面圖（底線或 cover 開頭，如 _cover.png、cover-1.png）不算。</summary>
    Public Shared Function Stickers(theme As String) As List(Of String)
        Dim dir = Path.Combine(_root, theme)
        If Not Directory.Exists(dir) Then Return New List(Of String)()
        Return Directory.GetFiles(dir).
            Where(Function(f) Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()) AndAlso Not IsCover(Path.GetFileName(f))).
            OrderBy(Function(f) Path.GetFileName(f), StringComparer.CurrentCulture).
            Select(Function(f) theme & "\" & Path.GetFileName(f)).ToList()
    End Function

    Public Shared Function IsCover(fileName As String) As Boolean
        Return fileName.StartsWith("_") OrElse fileName.StartsWith("cover", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>相對路徑換成完整路徑；路徑跑出 stick 資料夾（例如含 ..）時回傳 Nothing。</summary>
    Public Shared Function FullPath(relative As String) As String
        If String.IsNullOrWhiteSpace(relative) Then Return Nothing
        Dim full = Path.GetFullPath(Path.Combine(_root, relative))
        Dim rootFull = Path.GetFullPath(_root).TrimEnd("\"c) & "\"
        Return If(full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase), full, Nothing)
    End Function

    ''' <summary>
    ''' 取得貼圖圖片（快取，呼叫端不要釋放）。檔案不存在或讀不出來時回傳 Nothing。
    ''' 同一張圖可能被預覽與背景算圖同時使用，繪製時請 SyncLock 該圖。
    ''' </summary>
    Public Shared Function GetImage(relative As String) As Bitmap
        ' 完整路徑：選取區「轉成物件」、「貼成物件」存的圖片。
        Return LoadCached(ResolveFile(relative))
    End Function

    ''' <summary>圖片填字用：完整路徑，或 stick 資料夾裡的相對路徑。</summary>
    Public Shared Function GetImageFile(pathOrRelative As String) As Bitmap
        Return LoadCached(ResolveFile(pathOrRelative))
    End Function

    ''' <summary>
    ''' 實際存在的圖檔路徑：完整路徑直接用；相對路徑先找 stick 資料夾，沒有再找專案檔帶進來的副本。找不到時回傳 Nothing。
    ''' </summary>
    Public Shared Function ResolveFile(pathOrRelative As String) As String
        If String.IsNullOrWhiteSpace(pathOrRelative) Then Return Nothing
        If Path.IsPathRooted(pathOrRelative) Then Return If(File.Exists(pathOrRelative), pathOrRelative, Nothing)
        Dim full = FullPath(pathOrRelative)
        If full IsNot Nothing AndAlso File.Exists(full) Then Return full
        Dim copy As String = Nothing
        SyncLock CacheLock
            If Not Fallbacks.TryGetValue(pathOrRelative, copy) Then Return Nothing
        End SyncLock
        Return If(File.Exists(copy), copy, Nothing)
    End Function

    ''' <summary>專案檔載入時登記貼圖副本（本機 stick 資料夾沒有同名貼圖時使用）。</summary>
    Public Shared Sub RegisterFallback(relative As String, fullPath As String)
        SyncLock CacheLock
            Fallbacks(relative) = fullPath
        End SyncLock
    End Sub

    Private Shared Function LoadCached(full As String) As Bitmap
        If full Is Nothing Then Return Nothing
        SyncLock CacheLock
            Dim cached As Bitmap = Nothing
            If Cache.TryGetValue(full, cached) Then Return cached
            Dim bmp As Bitmap = Nothing
            Try
                If File.Exists(full) Then
                    Using ms As New MemoryStream(File.ReadAllBytes(full)), img = Image.FromStream(ms)
                        bmp = New Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb)
                        Using g = Graphics.FromImage(bmp)
                            g.DrawImage(img, 0, 0, img.Width, img.Height)
                        End Using
                    End Using
                End If
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse
                                       TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is UnauthorizedAccessException
                bmp?.Dispose()
                bmp = Nothing
            End Try
            Cache(full) = bmp ' 讀不到也記下來，避免每次重畫都重試
            Return bmp
        End SyncLock
    End Function

    ''' <summary>貼圖檔有增減或修改後呼叫（面板的「重新整理」）。</summary>
    Public Shared Sub ClearCache()
        SyncLock CacheLock
            For Each b In Cache.Values
                b?.Dispose()
            Next
            Cache.Clear()
        End SyncLock
    End Sub
End Class
