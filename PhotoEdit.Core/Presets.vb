''' <summary>濾鏡預設集：一組色調與效果數值，套用時整組替換（見 EditRecipe.CopyLookFrom）。</summary>
Public Class Preset
    Public ReadOnly Property Name As String
    Public ReadOnly Property Look As EditRecipe

    Public Sub New(name As String, look As EditRecipe)
        Me.Name = name
        Me.Look = look
    End Sub

    Public Sub ApplyTo(recipe As EditRecipe)
        recipe.CopyLookFrom(Look)
    End Sub

    Public Function Matches(recipe As EditRecipe) As Boolean
        Return recipe.LookEquals(Look)
    End Function

    ''' <summary>內建預設集，第一個「原色」等於清除所有色調與效果。</summary>
    Public Shared ReadOnly BuiltIn As IReadOnlyList(Of Preset) = New List(Of Preset) From {
        New Preset("原色", New EditRecipe()),
        New Preset("鮮豔", New EditRecipe With {.Contrast = 15, .Saturation = 35, .Shadows = 10}),
        New Preset("日系清新", New EditRecipe With {.Exposure = 0.4, .Contrast = -15, .Highlights = -20, .Shadows = 30,
                                                .Temperature = -10, .Saturation = -10, .Fade = 25}),
        New Preset("暖陽", New EditRecipe With {.Temperature = 35, .Contrast = 10, .Saturation = 10, .Vignette = -20}),
        New Preset("冷調", New EditRecipe With {.Temperature = -30, .Tint = 5, .Contrast = 10}),
        New Preset("懷舊", New EditRecipe With {.Temperature = 20, .Saturation = -35, .Contrast = -10, .Fade = 35,
                                              .Vignette = -30, .Grain = 25, .ToningHue = 35, .ToningStrength = 20}),
        New Preset("老照片", New EditRecipe With {.Saturation = -100, .Contrast = 10, .Fade = 15, .Vignette = -35,
                                               .Grain = 30, .ToningHue = 32, .ToningStrength = 45}),
        New Preset("黑白", New EditRecipe With {.Saturation = -100, .Contrast = 25}),
        New Preset("高反差黑白", New EditRecipe With {.Saturation = -100, .Contrast = 60, .Shadows = -20, .Vignette = -25, .Grain = 15}),
        New Preset("底片", New EditRecipe With {.Contrast = 10, .Highlights = -15, .Temperature = 8, .Saturation = -10,
                                              .Fade = 20, .Grain = 35}),
        New Preset("電影感", New EditRecipe With {.Contrast = 20, .Highlights = -25, .Shadows = -10, .Saturation = -15,
                                               .ToningHue = 190, .ToningStrength = 15, .Vignette = -25}),
        New Preset("夢幻", New EditRecipe With {.Exposure = 0.3, .Contrast = -25, .Saturation = -5, .Tint = 10,
                                              .Fade = 20, .Vignette = 20})
    }
End Class
