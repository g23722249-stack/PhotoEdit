Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization

''' <summary>專案檔（*.acsp.json）內容。</summary>
Public NotInheritable Class CompositeProject
    Public Const CurrentVersion As Integer = 1
    Public Property Version As Integer = CurrentVersion
    Public Property SavedUtc As DateTime = DateTime.UtcNow
    Public Property Application As String = "AI Auto Composite Studio"
    Public Property Scene As CompositeScene
End Class

''' <summary>載入專案時找不到的檔案。</summary>
Public NotInheritable Class MissingFile
    ''' <summary>"background"、"layer-source"、"mask" 等。</summary>
    Public Property Kind As String = ""
    Public Property Path As String = ""
    Public Property LayerId As String = ""
    Public Property Description As String = ""
End Class

''' <summary>
''' 專案存取：參數存 JSON（不把大圖轉 Base64 塞進去），遮罩另存成「專案名_files\」裡的 16 位元 PNG，
''' 原始圖片只記路徑（在專案資料夾內時存相對路徑）。載入時檢查來源檔案，遺失的列出來讓使用者重新指定。
''' </summary>
Public NotInheritable Class ProjectSerializer
    Public Const Extension As String = ".acsp.json"

    Public Shared ReadOnly JsonOptions As JsonSerializerOptions = CreateOptions()

    Private Shared Function CreateOptions() As JsonSerializerOptions
        Dim o As New JsonSerializerOptions With {
            .WriteIndented = True, .IncludeFields = True, .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            .PropertyNameCaseInsensitive = True, .ReadCommentHandling = JsonCommentHandling.Skip, .AllowTrailingCommas = True}
        o.Converters.Add(New JsonStringEnumConverter())
        Return o
    End Function

    ''' <summary>儲存專案（包含所有遮罩）。成功後 scene 內的路徑會更新成實際寫入的位置。</summary>
    Public Sub Save(scene As CompositeScene, projectPath As String)
        projectPath = Path.GetFullPath(projectPath)
        Dim dir = Path.GetDirectoryName(projectPath)
        Dim filesDirName = ProjectBaseName(projectPath) & "_files"
        Dim filesDir = Path.Combine(dir, filesDirName)
        Directory.CreateDirectory(filesDir)

        Dim saveMask = Function(m As FloatMask, name As String) As String
                           If m Is Nothing Then Return ""
                           PngCodec.SaveMask16(m, Path.Combine(filesDir, name))
                           Return Path.Combine(filesDirName, name)
                       End Function

        Dim copy = CloneForSave(scene)
        copy.BackgroundPath = MakeRelative(scene.BackgroundPath, dir)
        copy.ForegroundMaskPath = saveMask(scene.ForegroundMask, "foreground-mask.png")
        copy.ProtectMaskPath = saveMask(scene.ProtectMask, "protect-mask.png")
        copy.ShadowReceiverMaskPath = saveMask(scene.ShadowReceiverMask, "shadow-receiver-mask.png")
        For i = 0 To scene.Layers.Count - 1
            Dim src = scene.Layers(i), dst = copy.Layers(i)
            dst.SourcePath = MakeRelative(src.SourcePath, dir)
            dst.MaskPath = saveMask(src.Alpha, $"layer-{src.Id}-mask.png")
            dst.AiMaskPath = saveMask(src.AiAlpha, $"layer-{src.Id}-ai-mask.png")
            dst.OcclusionMaskPath = saveMask(src.OcclusionMask, $"layer-{src.Id}-occlusion.png")
        Next
        Dim proj As New CompositeProject With {.Scene = copy}
        Dim json = JsonSerializer.Serialize(proj, JsonOptions)
        File.WriteAllText(projectPath & ".tmp", json)
        File.Move(projectPath & ".tmp", projectPath, True)

        ' 回寫實際路徑（之後再存檔或匯出時用）。
        scene.ForegroundMaskPath = Resolve(copy.ForegroundMaskPath, dir)
        scene.ProtectMaskPath = Resolve(copy.ProtectMaskPath, dir)
        scene.ShadowReceiverMaskPath = Resolve(copy.ShadowReceiverMaskPath, dir)
        For i = 0 To scene.Layers.Count - 1
            scene.Layers(i).MaskPath = Resolve(copy.Layers(i).MaskPath, dir)
            scene.Layers(i).AiMaskPath = Resolve(copy.Layers(i).AiMaskPath, dir)
            scene.Layers(i).OcclusionMaskPath = Resolve(copy.Layers(i).OcclusionMaskPath, dir)
        Next
        AppLog.Info($"已儲存專案 {projectPath}")
    End Sub

    Private Shared Function CloneForSave(scene As CompositeScene) As CompositeScene
        Dim json = JsonSerializer.Serialize(scene, JsonOptions)
        Return JsonSerializer.Deserialize(Of CompositeScene)(json, JsonOptions)
    End Function

    ''' <summary>讀專案 JSON，路徑轉成絕對路徑，回傳遺失檔案清單（尚未載入影像）。</summary>
    Public Function Load(projectPath As String, ByRef missing As List(Of MissingFile)) As CompositeScene
        projectPath = Path.GetFullPath(projectPath)
        Dim dir = Path.GetDirectoryName(projectPath)
        Dim proj = JsonSerializer.Deserialize(Of CompositeProject)(File.ReadAllText(projectPath), JsonOptions)
        If proj Is Nothing OrElse proj.Scene Is Nothing Then Throw New InvalidDataException("專案檔內容無效。")
        If proj.Version > CompositeProject.CurrentVersion Then Throw New InvalidDataException($"專案版本 {proj.Version} 比程式支援的 {CompositeProject.CurrentVersion} 新，請更新程式。")
        Dim s = proj.Scene
        s.Layers = If(s.Layers, New List(Of CompositeLayer)())
        s.Guides = If(s.Guides, New List(Of GuideLine)())
        s.Target = If(s.Target, New PlacementTarget())
        s.Export = If(s.Export, New ExportSettings())
        s.BackgroundPath = Resolve(s.BackgroundPath, dir)
        s.ForegroundMaskPath = Resolve(s.ForegroundMaskPath, dir)
        s.ProtectMaskPath = Resolve(s.ProtectMaskPath, dir)
        s.ShadowReceiverMaskPath = Resolve(s.ShadowReceiverMaskPath, dir)
        For Each l In s.Layers
            l.Transform = If(l.Transform, New LayerTransform())
            l.Transform.Perspective = If(l.Transform.Perspective, New PerspectiveParams())
            l.Color = If(l.Color, New ColorParams())
            l.Shadow = If(l.Shadow, New ShadowParams())
            l.SourcePath = Resolve(l.SourcePath, dir)
            l.MaskPath = Resolve(l.MaskPath, dir)
            l.AiMaskPath = Resolve(l.AiMaskPath, dir)
            l.OcclusionMaskPath = Resolve(l.OcclusionMaskPath, dir)
        Next
        missing = FindMissing(s)
        Return s
    End Function

    Public Shared Function FindMissing(s As CompositeScene) As List(Of MissingFile)
        Dim list As New List(Of MissingFile)()
        If Not String.IsNullOrEmpty(s.BackgroundPath) AndAlso Not File.Exists(s.BackgroundPath) Then
            list.Add(New MissingFile With {.Kind = "background", .Path = s.BackgroundPath, .Description = "背景照片"})
        End If
        For Each l In s.Layers
            If Not String.IsNullOrEmpty(l.SourcePath) AndAlso Not File.Exists(l.SourcePath) Then
                list.Add(New MissingFile With {.Kind = "layer-source", .Path = l.SourcePath, .LayerId = l.Id, .Description = $"人物圖「{l.Name}」"})
            End If
            For Each mp In {l.MaskPath, l.AiMaskPath, l.OcclusionMaskPath}
                If Not String.IsNullOrEmpty(mp) AndAlso Not File.Exists(mp) Then
                    list.Add(New MissingFile With {.Kind = "mask", .Path = mp, .LayerId = l.Id, .Description = $"「{l.Name}」的遮罩"})
                End If
            Next
        Next
        Return list
    End Function

    ''' <summary>把遺失的檔案改指到新位置。</summary>
    Public Shared Sub Relocate(s As CompositeScene, item As MissingFile, newPath As String)
        Select Case item.Kind
            Case "background" : s.BackgroundPath = newPath
            Case "layer-source"
                Dim l = s.Layers.FirstOrDefault(Function(x) x.Id = item.LayerId)
                If l IsNot Nothing Then l.SourcePath = newPath
            Case "mask"
                Dim l = s.Layers.FirstOrDefault(Function(x) x.Id = item.LayerId)
                If l Is Nothing Then Return
                If l.MaskPath = item.Path Then l.MaskPath = newPath
                If l.AiMaskPath = item.Path Then l.AiMaskPath = newPath
                If l.OcclusionMaskPath = item.Path Then l.OcclusionMaskPath = newPath
        End Select
    End Sub

    ''' <summary>
    ''' 載入影像與遮罩到場景。遮罩尺寸與影像不符時重新縮放並記錄警告；遮罩遺失時以原圖 Alpha 代替。
    ''' </summary>
    Public Shared Sub LoadImages(s As CompositeScene, loader As ImageLoader, warnings As List(Of String))
        Dim bg = loader.Load(s.BackgroundPath)
        s.Background = bg.Image
        s.BackgroundInfo = bg.Info
        s.ForegroundMask = LoadMaskSized(s.ForegroundMaskPath, s.Width, s.Height, warnings)
        s.ProtectMask = LoadMaskSized(s.ProtectMaskPath, s.Width, s.Height, warnings)
        s.ShadowReceiverMask = LoadMaskSized(s.ShadowReceiverMaskPath, s.Width, s.Height, warnings)
        For Each l In s.Layers
            Dim src = loader.Load(l.SourcePath)
            l.Source = src.Image
            l.SourceInfo = src.Info
            l.Alpha = LoadMaskSized(l.MaskPath, l.Source.Width, l.Source.Height, warnings)
            l.AiAlpha = LoadMaskSized(l.AiMaskPath, l.Source.Width, l.Source.Height, warnings)
            If l.Alpha Is Nothing Then
                l.Alpha = If(l.AiAlpha?.Clone(), l.Source.ExtractAlpha())
                warnings.Add($"「{l.Name}」沒有遮罩檔，暫用{If(l.AiAlpha IsNot Nothing, " AI 原始遮罩", "原圖 Alpha")}。")
            End If
            l.OcclusionMask = LoadMaskSized(l.OcclusionMaskPath, s.Width, s.Height, warnings)
            l.Touch()
        Next
        s.TouchMasks()
    End Sub

    Private Shared Function LoadMaskSized(path As String, w As Integer, h As Integer, warnings As List(Of String)) As FloatMask
        If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then Return Nothing
        Dim m = PngCodec.LoadMask(path)
        If m.Width <> w OrElse m.Height <> h Then
            warnings.Add($"遮罩 {IO.Path.GetFileName(path)} 尺寸 {m.Width}×{m.Height} 與影像 {w}×{h} 不符，已重新縮放。")
            m = m.Resize(w, h)
        End If
        Return m
    End Function

    Public Shared Function ProjectBaseName(projectPath As String) As String
        Dim name = Path.GetFileName(projectPath)
        If name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) Then Return name.Substring(0, name.Length - Extension.Length)
        Return Path.GetFileNameWithoutExtension(name)
    End Function

    Public Shared Function MakeRelative(fullPath As String, baseDir As String) As String
        If String.IsNullOrEmpty(fullPath) Then Return ""
        Dim full = Path.GetFullPath(fullPath)
        Dim rel = Path.GetRelativePath(baseDir, full)
        ' 不在專案資料夾內（需要 ..\ 或不同磁碟）時保留絕對路徑。
        If rel.StartsWith("..", StringComparison.Ordinal) OrElse Path.IsPathRooted(rel) Then Return full
        Return rel
    End Function

    Public Shared Function Resolve(p As String, baseDir As String) As String
        If String.IsNullOrEmpty(p) Then Return ""
        Dim norm = p.Replace("\"c, Path.DirectorySeparatorChar).Replace("/"c, Path.DirectorySeparatorChar)
        If Path.IsPathRooted(norm) Then Return norm
        Return Path.GetFullPath(Path.Combine(baseDir, norm))
    End Function
End Class
