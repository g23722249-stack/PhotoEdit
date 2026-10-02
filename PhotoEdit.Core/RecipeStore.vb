Imports System.IO
Imports System.Text
Imports System.Text.Json

''' <summary>編輯配方存在照片旁的附屬檔「相片檔名.pedit.json」，原始照片不動。</summary>
Public NotInheritable Class RecipeStore
    Private Sub New()
    End Sub

    Public Const SidecarSuffix As String = ".pedit.json"

    Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {.WriteIndented = True}

    Public Shared Function SidecarPath(photoPath As String) As String
        Return photoPath & SidecarSuffix
    End Function

    ''' <summary>沒有附屬檔或內容損壞時回傳 Nothing。</summary>
    Public Shared Function Load(photoPath As String) As EditRecipe
        Dim path = SidecarPath(photoPath)
        If Not File.Exists(path) Then Return Nothing
        Try
            Return FromJson(File.ReadAllText(path, Encoding.UTF8))
        Catch ex As JsonException
            Return Nothing
        End Try
    End Function

    ''' <summary>配方等於原圖時刪除附屬檔，避免留下沒有作用的檔案。</summary>
    Public Shared Sub Save(photoPath As String, recipe As EditRecipe)
        Dim path = SidecarPath(photoPath)
        If recipe Is Nothing OrElse recipe.IsIdentity Then
            If File.Exists(path) Then File.Delete(path)
            Return
        End If
        File.WriteAllText(path, ToJson(recipe), New UTF8Encoding(False))
    End Sub

    Public Shared Function ToJson(recipe As EditRecipe) As String
        Return JsonSerializer.Serialize(recipe, JsonOptions)
    End Function

    Public Shared Function FromJson(json As String) As EditRecipe
        Dim r = JsonSerializer.Deserialize(Of EditRecipe)(json, JsonOptions)
        If r IsNot Nothing Then r.Version = EditRecipe.CurrentVersion
        Return r
    End Function
End Class
