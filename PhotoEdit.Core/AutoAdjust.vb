Imports System.Drawing

''' <summary>一鍵調整：分析「只套幾何、未調色」的影像，算出建議的色調參數。</summary>
Public NotInheritable Class AutoAdjust
    Private Sub New()
    End Sub

    Private Const AnalyzeSize As Integer = 512
    Private Const WhiteBalanceStrength As Double = 0.7

    ''' <summary>灰色世界假設：整張照片平均應接近中性灰。回傳新配方，幾何與其他參數保持不變。</summary>
    Public Shared Function WhiteBalance(source As Bitmap, recipe As EditRecipe) As EditRecipe
        Dim avg = AnalyzeRgb(source, recipe)
        Dim result = recipe.Clone()
        If avg.R + avg.B < 1 OrElse avg.G < 1 Then Return result

        ' 對應 ImagePipeline：R 乘 (1+t)、B 乘 (1-t)，G 乘 (1-m)。
        Dim t = (avg.B - avg.R) / (avg.B + avg.R)
        Dim balancedRB = ((1 + t) * avg.R + (1 - t) * avg.B) / 2
        Dim m = 1 - balancedRB / avg.G
        ' 大片草地、天空、夕陽會讓灰色世界假設失準，所以只修正 70% 並限制幅度，寧可保守。
        result.Temperature = ClampInt(t / 0.3 * 100 * WhiteBalanceStrength, -50, 50)
        result.Tint = ClampInt(m / 0.2 * 100 * WhiteBalanceStrength, -30, 30)
        Return result
    End Function

    ''' <summary>自動增強：白平衡 + 依平均亮度調曝光 + 依亮度分布調對比與暗部。</summary>
    Public Shared Function Enhance(source As Bitmap, recipe As EditRecipe) As EditRecipe
        Dim result = WhiteBalance(source, recipe)

        Dim neutral = recipe.Clone()
        neutral.ResetAdjustments()
        Dim hist As Histogram
        Using bmp = ImagePipeline.RenderGeometry(source, neutral, AnalyzeSize, applyCrop:=True)
            hist = Histogram.Compute(bmp)
        End Using

        Dim mean = Math.Max(0.02, hist.MeanLuminance())
        result.Exposure = Math.Round(Math.Max(-1.5, Math.Min(1.5, Math.Log(0.46 / mean, 2) * 0.6)), 1)

        Dim spread = hist.Percentile(0.99) - hist.Percentile(0.01)
        result.Contrast = ClampInt((0.85 - spread) * 100, 0, 40)

        ' 暗部比例偏高（逆光、陰影多）就稍微拉亮暗部。
        Dim darkShare = hist.Percentile(0.25)
        result.Shadows = If(darkShare < 0.15, ClampInt((0.15 - darkShare) * 300, 0, 40), 0)
        result.Highlights = If(hist.Percentile(0.98) > 0.97, -20, 0)
        result.Saturation = 10
        Return result
    End Function

    Private Shared Function AnalyzeRgb(source As Bitmap, recipe As EditRecipe) As (R As Double, G As Double, B As Double)
        Dim neutral = recipe.Clone()
        neutral.ResetAdjustments()
        Using bmp = ImagePipeline.RenderGeometry(source, neutral, AnalyzeSize, applyCrop:=True)
            Return Histogram.Compute(bmp).MeanRgb()
        End Using
    End Function

    Private Shared Function ClampInt(v As Double, min As Integer, max As Integer) As Integer
        Return CInt(Math.Max(min, Math.Min(max, Math.Round(v))))
    End Function
End Class
