Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.Json

''' <summary>專案檔裡包進去的外部圖片（貼圖、圖片填字、去背背景圖）。</summary>
Public Class ProjectAsset
    ''' <summary>配方裡原本寫的路徑（stick 資料夾的相對路徑，或完整路徑）。</summary>
    Public Property Source As String
    ''' <summary>壓縮檔裡的項目名稱。</summary>
    Public Property Entry As String
End Class

''' <summary>專案檔的目錄（manifest.json）。</summary>
Public Class ProjectManifest
    Public Const FormatName As String = "PhotoEdit.Project"
    Public Const CurrentVersion As Integer = 1

    Public Property Format As String = FormatName
    Public Property Version As Integer = CurrentVersion
    Public Property SavedAt As DateTime
    ''' <summary>原圖的檔名（例如 IMG_1234.jpg），載入時用同樣的檔名解開。</summary>
    Public Property OriginalName As String
    Public Property OriginalEntry As String
    Public Property RecipeEntry As String = "recipe.json"
    ''' <summary>去背遮罩：模型（General／Human）→ 項目名稱。</summary>
    Public Property Masks As New Dictionary(Of String, String)()
    Public Property Assets As New List(Of ProjectAsset)()
End Class

''' <summary>載入專案的結果。</summary>
Public Class LoadedProject
    ''' <summary>解開到工作資料夾的原圖（之後就像一般照片一樣開啟）。</summary>
    Public Property PhotoPath As String
    ''' <summary>外部圖片的路徑已換成工作資料夾裡的副本。</summary>
    Public Property Recipe As EditRecipe
End Class

''' <summary>
''' PhotoEdit 專案檔（.pedx）：一個 zip，裡面有原圖（原封不動，保留 EXIF）、編輯配方（調色、裁切、文字、貼圖、繪圖圖層…）、
''' 去背遮罩，以及用到的外部圖片。搬到別台電腦、或原圖刪掉也能完整載入繼續修改。
''' </summary>
Public NotInheritable Class ProjectFile
    Private Sub New()
    End Sub

    Public Const Extension As String = ".pedx"

    Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {.WriteIndented = True}

    Public Shared Function IsProject(path As String) As Boolean
        Return String.Equals(IO.Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase)
    End Function

    '---------------------------------------------------------------------
    ' 存檔
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 寫入專案檔（先寫暫存檔再取代，寫到一半失敗不會毀掉舊檔）。
    ''' masks 為目前的去背遮罩（可為空）；外部圖片依配方裡的路徑讀取，找不到的略過。
    ''' </summary>
    Public Shared Sub Save(projectPath As String, originalName As String, originalBytes As Byte(), recipe As EditRecipe,
                           Optional masks As IDictionary(Of CutoutModel, Bitmap) = Nothing)
        Dim manifest As New ProjectManifest With {
            .SavedAt = DateTime.Now,
            .OriginalName = SafeName(originalName),
            .OriginalEntry = "original" & IO.Path.GetExtension(SafeName(originalName)).ToLowerInvariant()}

        Dim temp = projectPath & ".saving"
        Try
            Using fs As New FileStream(temp, FileMode.Create, FileAccess.Write), zip As New ZipArchive(fs, ZipArchiveMode.Create)
                ' 原圖已經是壓縮過的格式，不再壓縮。
                WriteEntry(zip, manifest.OriginalEntry, originalBytes, CompressionLevel.NoCompression)
                WriteEntry(zip, manifest.RecipeEntry, Encoding.UTF8.GetBytes(RecipeStore.ToJson(recipe)), CompressionLevel.Optimal)

                If masks IsNot Nothing Then
                    For Each kv In masks
                        If kv.Value Is Nothing Then Continue For
                        Dim entry = $"mask-{kv.Key.ToString().ToLowerInvariant()}.png"
                        Using ms As New MemoryStream()
                            SyncLock kv.Value
                                kv.Value.Save(ms, ImageFormat.Png)
                            End SyncLock
                            WriteEntry(zip, entry, ms.ToArray(), CompressionLevel.NoCompression)
                        End Using
                        manifest.Masks(kv.Key.ToString()) = entry
                    Next
                End If

                For Each source In ReferencedFiles(recipe)
                    Dim file = StickerLibrary.ResolveFile(source)
                    If file Is Nothing Then Continue For ' 找不到的圖片：載入時會和原本一樣顯示不出來
                    Dim entry = $"assets/{manifest.Assets.Count}{IO.Path.GetExtension(file).ToLowerInvariant()}"
                    WriteEntry(zip, entry, IO.File.ReadAllBytes(file), CompressionLevel.NoCompression)
                    manifest.Assets.Add(New ProjectAsset With {.Source = source, .Entry = entry})
                Next

                WriteEntry(zip, "manifest.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, JsonOptions)), CompressionLevel.Optimal)
            End Using
            IO.File.Move(temp, projectPath, overwrite:=True)
        Finally
            If IO.File.Exists(temp) Then IO.File.Delete(temp)
        End Try
    End Sub

    ''' <summary>配方裡用到的外部圖片路徑（不重複）。</summary>
    Public Shared Function ReferencedFiles(recipe As EditRecipe) As List(Of String)
        Dim list As New List(Of String)()
        Dim add = Sub(p As String)
                      If Not String.IsNullOrWhiteSpace(p) AndAlso Not list.Contains(p, StringComparer.OrdinalIgnoreCase) Then list.Add(p)
                  End Sub
        For Each o In If(recipe.Overlays, New List(Of Overlay)())
            If o.Kind = OverlayKind.Image Then add(o.ImagePath)
            add(o.TexturePath)
        Next
        add(recipe.Cutout?.BackgroundImagePath)
        Return list
    End Function

    Private Shared Sub WriteEntry(zip As ZipArchive, name As String, data As Byte(), level As CompressionLevel)
        Using s = zip.CreateEntry(name, level).Open()
            s.Write(data, 0, data.Length)
        End Using
    End Sub

    '---------------------------------------------------------------------
    ' 載入
    '---------------------------------------------------------------------

    ''' <summary>
    ''' 把專案解開到 workDir（原圖、遮罩放在原圖旁，用 MaskStore 的檔名；外部圖片放在 assets\）。
    ''' stick 資料夾的貼圖：本機有就用本機的，沒有就用專案裡的副本；完整路徑的圖片一律換成專案裡的副本。
    ''' </summary>
    Public Shared Function Load(projectPath As String, workDir As String) As LoadedProject
        Directory.CreateDirectory(workDir)
        Using zip = ZipFile.OpenRead(projectPath)
            Dim manifestEntry = zip.GetEntry("manifest.json")
            If manifestEntry Is Nothing Then Throw New InvalidDataException("不是 PhotoEdit 專案檔（缺少 manifest.json）。")
            Dim manifest = JsonSerializer.Deserialize(Of ProjectManifest)(ReadText(manifestEntry), JsonOptions)
            If manifest Is Nothing OrElse manifest.Format <> ProjectManifest.FormatName Then Throw New InvalidDataException("不是 PhotoEdit 專案檔。")
            If manifest.Version > ProjectManifest.CurrentVersion Then Throw New InvalidDataException("這個專案檔是用較新版的 PhotoEdit 存的，請更新程式。")

            Dim photoPath = IO.Path.Combine(workDir, SafeName(If(manifest.OriginalName, "original.png")))
            Extract(zip, manifest.OriginalEntry, photoPath)

            Dim recipeEntry = zip.GetEntry(If(manifest.RecipeEntry, "recipe.json"))
            Dim recipe = If(recipeEntry Is Nothing, Nothing, RecipeStore.FromJson(ReadText(recipeEntry)))
            If recipe Is Nothing Then recipe = New EditRecipe()

            For Each kv In If(manifest.Masks, New Dictionary(Of String, String)())
                Dim model As CutoutModel
                If [Enum].TryParse(kv.Key, model) Then Extract(zip, kv.Value, MaskStore.MaskPath(photoPath, model))
            Next

            Dim assetDir = IO.Path.Combine(workDir, "assets")
            For Each a In If(manifest.Assets, New List(Of ProjectAsset)())
                If String.IsNullOrEmpty(a.Source) OrElse String.IsNullOrEmpty(a.Entry) Then Continue For
                Dim target = IO.Path.Combine(assetDir, SafeName(a.Entry))
                If Not Extract(zip, a.Entry, target) Then Continue For
                If IO.Path.IsPathRooted(a.Source) Then
                    ReplaceReference(recipe, a.Source, target)
                Else
                    StickerLibrary.RegisterFallback(a.Source, target)
                End If
            Next
            Return New LoadedProject With {.PhotoPath = photoPath, .Recipe = recipe}
        End Using
    End Function

    Private Shared Sub ReplaceReference(recipe As EditRecipe, source As String, target As String)
        Dim same = Function(p As String) String.Equals(p, source, StringComparison.OrdinalIgnoreCase)
        For Each o In If(recipe.Overlays, New List(Of Overlay)())
            If same(o.ImagePath) Then o.ImagePath = target
            If same(o.TexturePath) Then o.TexturePath = target
        Next
        If recipe.Cutout IsNot Nothing AndAlso same(recipe.Cutout.BackgroundImagePath) Then recipe.Cutout.BackgroundImagePath = target
    End Sub

    Private Shared Function Extract(zip As ZipArchive, entryName As String, target As String) As Boolean
        If String.IsNullOrEmpty(entryName) Then Return False
        Dim entry = zip.GetEntry(entryName)
        If entry Is Nothing Then Return False
        Directory.CreateDirectory(IO.Path.GetDirectoryName(target))
        entry.ExtractToFile(target, overwrite:=True)
        Return True
    End Function

    Private Shared Function ReadText(entry As ZipArchiveEntry) As String
        Using r As New StreamReader(entry.Open(), Encoding.UTF8)
            Return r.ReadToEnd()
        End Using
    End Function

    ''' <summary>只留檔名、去掉不合法字元（專案檔內容不可信任，不能讓它寫到工作資料夾以外）。</summary>
    Private Shared Function SafeName(name As String) As String
        Dim n = IO.Path.GetFileName(If(name, "").Replace("/"c, "\"c).Split("\"c).Last())
        For Each c In IO.Path.GetInvalidFileNameChars()
            n = n.Replace(c, "_"c)
        Next
        Return If(String.IsNullOrWhiteSpace(n) OrElse n.Trim("."c) = "", "file", n)
    End Function
End Class
