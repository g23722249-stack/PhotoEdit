''' <summary>濾鏡預設集：一組色調、效果與藝術風格，套用時整組替換（見 EditRecipe.CopyLookFrom）。</summary>
Public Class Preset
    Public ReadOnly Property Name As String
    Public ReadOnly Property Look As EditRecipe
    ''' <summary>濾鏡列的分類（見 Categories）；「原色」不屬於任何分類，永遠排第一。</summary>
    Public ReadOnly Property Category As String

    Public Sub New(name As String, look As EditRecipe, Optional category As String = "")
        Me.Name = name
        Me.Look = look
        Me.Category = category
    End Sub

    Public Sub ApplyTo(recipe As EditRecipe)
        recipe.CopyLookFrom(Look)
    End Sub

    Public Function Matches(recipe As EditRecipe) As Boolean
        Return recipe.LookEquals(Look)
    End Function

    Public Const ToneCategory As String = "色調"
    Public Const RetroCategory As String = "復古／黑白"
    Public Const PaintCategory As String = "繪畫"
    Public Const ComicCategory As String = "漫畫／版畫"

    ''' <summary>濾鏡列的分類（依序）。</summary>
    Public Shared ReadOnly Categories As String() = {ToneCategory, RetroCategory, PaintCategory, ComicCategory}

    Private Shared Function Art(style As ArtStyle, Optional setup As Action(Of EditRecipe) = Nothing) As EditRecipe
        Dim r As New EditRecipe With {.ArtStyle = style}
        setup?.Invoke(r)
        Return r
    End Function

    ''' <summary>內建預設集，第一個「原色」等於清除所有色調、效果與藝術風格。</summary>
    Public Shared ReadOnly BuiltIn As IReadOnlyList(Of Preset) = New List(Of Preset) From {
        New Preset("原色", New EditRecipe()),
        New Preset("鮮豔", New EditRecipe With {.Contrast = 15, .Saturation = 35, .Shadows = 10}, ToneCategory),
        New Preset("日系清新", New EditRecipe With {.Exposure = 0.4, .Contrast = -15, .Highlights = -20, .Shadows = 30,
                                                .Temperature = -10, .Saturation = -10, .Fade = 25}, ToneCategory),
        New Preset("暖陽", New EditRecipe With {.Temperature = 35, .Contrast = 10, .Saturation = 10, .Vignette = -20}, ToneCategory),
        New Preset("冷調", New EditRecipe With {.Temperature = -30, .Tint = 5, .Contrast = 10}, ToneCategory),
        New Preset("夢幻", New EditRecipe With {.Exposure = 0.3, .Contrast = -25, .Saturation = -5, .Tint = 10,
                                              .Fade = 20, .Vignette = 20}, ToneCategory),
        New Preset("懷舊", New EditRecipe With {.Temperature = 20, .Saturation = -35, .Contrast = -10, .Fade = 35,
                                              .Vignette = -30, .Grain = 25, .ToningHue = 35, .ToningStrength = 20}, RetroCategory),
        New Preset("老照片", New EditRecipe With {.Saturation = -100, .Contrast = 10, .Fade = 15, .Vignette = -35,
                                               .Grain = 30, .ToningHue = 32, .ToningStrength = 45}, RetroCategory),
        New Preset("黑白", New EditRecipe With {.Saturation = -100, .Contrast = 25}, RetroCategory),
        New Preset("高反差黑白", New EditRecipe With {.Saturation = -100, .Contrast = 60, .Shadows = -20, .Vignette = -25, .Grain = 15}, RetroCategory),
        New Preset("底片", New EditRecipe With {.Contrast = 10, .Highlights = -15, .Temperature = 8, .Saturation = -10,
                                              .Fade = 20, .Grain = 35}, RetroCategory),
        New Preset("電影感", New EditRecipe With {.Contrast = 20, .Highlights = -25, .Shadows = -10, .Saturation = -15,
                                               .ToningHue = 190, .ToningStrength = 15, .Vignette = -25}, RetroCategory),
        New Preset("水彩畫", Art(ArtStyle.Watercolor, Sub(r) r.Saturation = 10), PaintCategory),
        New Preset("油畫", Art(ArtStyle.OilPainting, Sub(r)
                                                     r.Contrast = 10 : r.Saturation = 15
                                                 End Sub), PaintCategory),
        New Preset("水墨畫", Art(ArtStyle.InkWash), PaintCategory),
        New Preset("素描", Art(ArtStyle.Sketch), PaintCategory),
        New Preset("彩色鉛筆", Art(ArtStyle.ColoredPencil, Sub(r) r.Saturation = 15), PaintCategory),
        New Preset("點描", Art(ArtStyle.Pointillism, Sub(r) r.Saturation = 10), PaintCategory),
        New Preset("黑白漫畫", Art(ArtStyle.MangaBW), ComicCategory),
        New Preset("彩色漫畫", Art(ArtStyle.MangaColor, Sub(r) r.Saturation = 10), ComicCategory),
        New Preset("浮世繪", Art(ArtStyle.Ukiyoe), ComicCategory),
        New Preset("銅版畫", Art(ArtStyle.Etching), ComicCategory),
        New Preset("木刻版畫", Art(ArtStyle.Woodcut), ComicCategory),
        New Preset("普普藝術", Art(ArtStyle.PopArt), ComicCategory),
        New Preset("像素藝術", Art(ArtStyle.PixelArt), ComicCategory)
    }
End Class
