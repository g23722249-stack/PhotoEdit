Imports System.Drawing

''' <summary>文字樣式預設集：一組外觀設定，套用時保留文字內容、位置、大小與角度（Overlay.CopyStyleFrom）。</summary>
Public Class TextPreset
    Public ReadOnly Property Name As String
    Public ReadOnly Property Style As Overlay

    Public Sub New(name As String, style As Overlay)
        Me.Name = name
        Me.Style = style
        style.Kind = OverlayKind.Text
    End Sub

    Public Sub ApplyTo(o As Overlay)
        o.CopyStyleFrom(Style)
    End Sub

    Private Shared Function C(r As Integer, g As Integer, b As Integer) As Integer
        Return Color.FromArgb(r, g, b).ToArgb()
    End Function

    ''' <summary>日期戳記：傳統底片相機的橘色七段數字，右下角。</summary>
    Public Shared ReadOnly DateStamp As New Overlay With {
        .Kind = OverlayKind.Text, .FontName = TextRender.SevenSegmentFont, .ColorArgb = C(255, 130, 40),
        .Shadow = False, .GlowSize = 12, .GlowColorArgb = C(255, 80, 0), .Bold = False}

    Public Shared ReadOnly BuiltIn As IReadOnlyList(Of TextPreset) = New List(Of TextPreset) From {
        New TextPreset("標題", New Overlay With {.ColorArgb = C(255, 255, 255), .OutlineWidth = 45, .OutlineColorArgb = C(20, 20, 20),
                                                .ShadowBlur = 40, .ShadowOpacity = 55}),
        New TextPreset("貼紙", New Overlay With {.ColorArgb = C(255, 90, 120), .OutlineWidth = 70, .OutlineColorArgb = C(255, 255, 255),
                                                .ShadowDistance = 12, .ShadowBlur = 50, .ShadowOpacity = 45}),
        New TextPreset("霓虹", New Overlay With {.ColorArgb = C(255, 240, 255), .OutlineWidth = 18, .OutlineColorArgb = C(255, 60, 200),
                                                .GlowSize = 60, .GlowColorArgb = C(255, 40, 200), .Shadow = False}),
        New TextPreset("復古", New Overlay With {.FontName = "Georgia", .ColorArgb = C(245, 225, 175), .OutlineWidth = 14, .OutlineColorArgb = C(90, 50, 20),
                                                .ExtrudeDepth = 35, .ExtrudeColorArgb = C(120, 60, 25), .ExtrudeAngle = 45, .Shadow = False}),
        New TextPreset("可愛", New Overlay With {.ColorArgb = C(255, 140, 185), .OutlineWidth = 50, .OutlineColorArgb = C(255, 255, 255),
                                                .Outline2Width = 20, .Outline2ColorArgb = C(255, 140, 185), .ShadowDistance = 10, .ShadowOpacity = 30}),
        New TextPreset("字幕", New Overlay With {.ColorArgb = C(255, 255, 255), .Bold = False, .BackgroundStyle = TextBackground.Bar,
                                                .BackgroundColorArgb = C(0, 0, 0), .BackgroundOpacity = 55, .BackgroundPadding = 35, .Shadow = False}),
        New TextPreset("標籤", New Overlay With {.ColorArgb = C(255, 255, 255), .BackgroundStyle = TextBackground.Box, .BackgroundColorArgb = C(230, 70, 90),
                                                .BackgroundOpacity = 100, .BackgroundRadius = 45, .BackgroundPadding = 40, .ShadowBlur = 40, .ShadowOpacity = 35}),
        New TextPreset("漸層", New Overlay With {.FillMode = TextFill.Gradient, .ColorArgb = C(255, 200, 40), .Color2Argb = C(255, 60, 130),
                                                .OutlineWidth = 25, .OutlineColorArgb = C(255, 255, 255), .ShadowBlur = 40}),
        New TextPreset("彩虹", New Overlay With {.FillMode = TextFill.Rainbow, .GradientAngle = 0, .OutlineWidth = 25, .OutlineColorArgb = C(40, 40, 60),
                                                .ShadowBlur = 30}),
        New TextPreset("立體", New Overlay With {.ColorArgb = C(255, 215, 60), .OutlineWidth = 10, .OutlineColorArgb = C(90, 45, 10),
                                                .ExtrudeDepth = 55, .ExtrudeColorArgb = C(190, 95, 20), .ExtrudeAngle = 50, .ShadowBlur = 40, .ShadowOpacity = 35}),
        New TextPreset("鏤空", New Overlay With {.FillMode = TextFill.Photo, .BackgroundStyle = TextBackground.DimPhoto, .BackgroundColorArgb = C(0, 0, 0),
                                                .BackgroundOpacity = 65, .OutlineWidth = 8, .OutlineColorArgb = C(255, 255, 255), .Shadow = False}),
        New TextPreset("印章", New Overlay With {.FontName = "DFKai-SB", .ColorArgb = C(255, 255, 255), .Vertical = True, .LineSpacing = 90,
                                                .BackgroundStyle = TextBackground.Box, .BackgroundColorArgb = C(200, 30, 35), .BackgroundOpacity = 100,
                                                .BackgroundRadius = 8, .BackgroundPadding = 18, .Shadow = False}),
        New TextPreset("外框章", New Overlay With {.FontName = "DFKai-SB", .ColorArgb = C(200, 30, 35), .Vertical = True, .LineSpacing = 90,
                                                 .BackgroundStyle = TextBackground.Frame, .BackgroundColorArgb = C(200, 30, 35), .BackgroundOpacity = 100,
                                                 .BackgroundRadius = 8, .BackgroundPadding = 18, .Shadow = False}),
        New TextPreset("浮水印", New Overlay With {.ColorArgb = C(255, 255, 255), .Opacity = 45, .Shadow = False, .Bold = False}),
        New TextPreset("日期", DateStamp.Clone())
    }
End Class

''' <summary>從照片 EXIF 讀出的資訊（插入文字、日期戳記用）。</summary>
Public Class PhotoInfo
    Public Property TakenDate As DateTime?
    Public Property Camera As String
    ''' <summary>例如「25.0340°N 121.5645°E」；沒有 GPS 時為 Nothing。</summary>
    Public Property Gps As String

    ''' <summary>傳統日期戳記格式：'05 7 12（年取兩位，月日不補零）。</summary>
    Public Shared Function DateStampText(d As DateTime) As String
        Return $"'{d:yy} {d.Month} {d.Day}"
    End Function

    ''' <summary>EXIF 日期字串「2005:07:12 14:03:21」（結尾可能有 \0）。</summary>
    Public Shared Function ParseExifDate(raw As Byte()) As DateTime?
        If raw Is Nothing Then Return Nothing
        Dim s = System.Text.Encoding.ASCII.GetString(raw).TrimEnd(ChrW(0), " "c)
        Dim d As DateTime
        If DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None, d) Then Return d
        Return Nothing
    End Function

    ''' <summary>EXIF GPS 度分秒（三組 rational）轉成十進位度數。</summary>
    Public Shared Function ParseGpsCoordinate(raw As Byte()) As Double?
        If raw Is Nothing OrElse raw.Length < 24 Then Return Nothing
        Dim v(2) As Double
        For i = 0 To 2
            Dim num = BitConverter.ToUInt32(raw, i * 8), den = BitConverter.ToUInt32(raw, i * 8 + 4)
            v(i) = If(den = 0, 0, num / CDbl(den))
        Next
        Return v(0) + v(1) / 60 + v(2) / 3600
    End Function
End Class
