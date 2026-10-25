Imports System.Text.Json
Imports System.Text.Json.Serialization

''' <summary>模型清單中的檔案選取規則（實際檔名在安裝時由 Hub 檔案清單決定，不猜檔名）。</summary>
Public NotInheritable Class ManifestFileRule
    ''' <summary>Hub 路徑的萬用字元樣式（* 與 ?，/ 分隔資料夾），例如 "onnx/model.onnx"、"*.onnx"。</summary>
    Public Property Pattern As String = ""
    ''' <summary>"model"（主模型，取第一個符合者）、"external-weights"、"config"、"tokenizer" 等。</summary>
    Public Property Role As String = "model"
    Public Property Required As Boolean = True
End Class

''' <summary>模型變體（例如 FP32／FP16），每個變體有自己的檔案規則。</summary>
Public NotInheritable Class ManifestVariant
    Public Property Id As String = "default"
    Public Property Label As String = ""
    Public Property Files As New List(Of ManifestFileRule)()
    ''' <summary>預估下載大小（位元組，只供顯示；實際以 Hub 回報為準）。</summary>
    Public Property EstimatedBytes As Long
End Class

Public NotInheritable Class ManifestLicense
    ''' <summary>預期授權識別（例如 "mit"、"apache-2.0"、"cc-by-nc-4.0"）；安裝時以 Hub 模型卡實際值覆核。</summary>
    Public Property Expected As String = ""
    Public Property Url As String = ""
    ''' <summary>"allowed"、"forbidden"、"unknown"（需要使用者自行確認）。</summary>
    Public Property CommercialUse As String = "unknown"
    Public Property Notes As String = ""
End Class

''' <summary>模型輸入輸出規格（載入時與 ONNX metadata 比對）。</summary>
Public NotInheritable Class ManifestIo
    ''' <summary>空字串 = 使用第一個輸入。</summary>
    Public Property InputName As String = ""
    Public Property InputShape As Long() = Array.Empty(Of Long)()
    ''' <summary>"float32" 或 "float16"。</summary>
    Public Property InputType As String = "float32"
    ''' <summary>空字串 = 自動挑選形狀符合的輸出。</summary>
    Public Property OutputName As String = ""
    Public Property OutputShape As Long() = Array.Empty(Of Long)()
    Public Property OutputType As String = "float32"
End Class

''' <summary>去背前處理與後處理設定。</summary>
Public NotInheritable Class ManifestPreprocess
    ''' <summary>模型輸入邊長。</summary>
    Public Property Size As Integer = 1024
    ''' <summary>RGB 順序的平均值與標準差（0..1 尺度）。</summary>
    Public Property Mean As Double() = {0.485, 0.456, 0.406}
    Public Property Std As Double() = {0.229, 0.224, 0.225}
    ''' <summary>"letterbox"（保留比例補邊，預設）或 "stretch"。</summary>
    Public Property Resize As String = "letterbox"
    ''' <summary>"auto"（數值超出 0..1 才套 sigmoid）、"sigmoid"、"none"。</summary>
    Public Property OutputActivation As String = "auto"
    ''' <summary>輸出做最小最大值正規化（部分模型需要）。</summary>
    Public Property MinMaxNormalize As Boolean
End Class

''' <summary>
''' 模型清單（Models/manifests/*.json）。記錄來源、固定版本、檔案規則、授權、輸入輸出與前處理。
''' Revision 可以是 commit 或分支；首次安裝時解析成 commit 並鎖定在安裝狀態中，之後不會自動升級。
''' </summary>
Public NotInheritable Class ModelManifest
    Public Property Id As String = ""
    Public Property Name As String = ""
    ''' <summary>"segmentation"、"inpainting"、"depth"。</summary>
    Public Property Purpose As String = "segmentation"
    Public Property Description As String = ""
    ''' <summary>Hugging Face 模型倉庫（owner/name）。</summary>
    Public Property Repo As String = ""
    Public Property Revision As String = "main"
    Public Property SourceUrl As String = ""
    Public Property Variants As New List(Of ManifestVariant)()
    Public Property License As New ManifestLicense()
    Public Property Io As New ManifestIo()
    Public Property Preprocess As New ManifestPreprocess()
    ''' <summary>執行提供者相容性：鍵 "directml"／"cpu"，值 "supported"、"untested"、"unsupported"。</summary>
    Public Property Providers As New Dictionary(Of String, String)()
    ''' <summary>第二階段功能等尚未接上推論的模型，設為 False 時只顯示不安裝。</summary>
    Public Property Installable As Boolean = True

    <JsonIgnore> Public Property ManifestPath As String = ""

    Public Function GetVariant(id As String) As ManifestVariant
        If Variants.Count = 0 Then Return New ManifestVariant()
        Return If(Variants.FirstOrDefault(Function(v) v.Id = id), Variants(0))
    End Function

    Public Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True, .ReadCommentHandling = JsonCommentHandling.Skip, .AllowTrailingCommas = True,
        .WriteIndented = True, .PropertyNamingPolicy = JsonNamingPolicy.CamelCase}

    Public Shared Function Load(path As String) As ModelManifest
        Dim m = JsonSerializer.Deserialize(Of ModelManifest)(System.IO.File.ReadAllText(path), JsonOptions)
        If m Is Nothing OrElse String.IsNullOrWhiteSpace(m.Id) Then Throw New System.IO.InvalidDataException($"模型清單 {path} 缺少 id。")
        m.ManifestPath = path
        m.Variants = If(m.Variants, New List(Of ManifestVariant)())
        m.Providers = If(m.Providers, New Dictionary(Of String, String)())
        m.License = If(m.License, New ManifestLicense())
        m.Io = If(m.Io, New ManifestIo())
        m.Preprocess = If(m.Preprocess, New ManifestPreprocess())
        Return m
    End Function
End Class

''' <summary>萬用字元比對（* 不跨 /，** 跨資料夾，? 單一字元），不分大小寫。</summary>
Public NotInheritable Class GlobPattern
    Private Sub New()
    End Sub

    Public Shared Function IsMatch(path As String, pattern As String) As Boolean
        If String.IsNullOrEmpty(pattern) Then Return False
        ' 沒有 / 的樣式比對檔名（任何資料夾）。
        If Not pattern.Contains("/"c) Then path = path.Substring(path.LastIndexOf("/"c) + 1)
        Dim rx = "^" & Text.RegularExpressions.Regex.Escape(pattern).Replace("\*\*", ".*").Replace("\*", "[^/]*").Replace("\?", "[^/]") & "$"
        Return Text.RegularExpressions.Regex.IsMatch(path, rx, Text.RegularExpressions.RegexOptions.IgnoreCase)
    End Function
End Class
