Imports System.IO
Imports Xunit

Public Class ImagingTests
    <Fact>
    Public Sub TransparentPng_RoundTrip_PreservesAlphaAndReportsTransparency()
        Dim dir = TestHelpers.TempDir()
        Dim img = TestHelpers.StickFigure(40, 60, transparent:=True)
        Dim path = IO.Path.Combine(dir, "fig.png")
        Dim codec As New PngCodec()
        codec.Encode(img, path, ExportFormat.Png, 0)
        Dim d = codec.Decode(path)
        TestHelpers.SameSeq(img.Pixels, d.Image.Pixels)
        Assert.True(d.Info.HasAlphaChannel)
        Assert.True(d.Info.HasTransparency)
        Assert.Equal("RGBA", d.Info.ColorMode)
        Assert.True(SourceAlphaSegmentation.HasUsableAlpha(d.Image))
    End Sub

    <Fact>
    Public Sub OpaqueImage_WritesRgb_AndNeedsCutout()
        Dim dir = TestHelpers.TempDir()
        Dim img = TestHelpers.NoisyBackground(33, 17)
        Dim path = IO.Path.Combine(dir, "bg.png")
        Dim codec As New PngCodec()
        codec.Encode(img, path, ExportFormat.Png, 0)
        Dim d = codec.Decode(path)
        Assert.Equal("RGB", d.Info.ColorMode)
        Assert.False(d.Info.HasAlphaChannel)
        TestHelpers.SameSeq(img.Pixels, d.Image.Pixels)
        Assert.False(SourceAlphaSegmentation.HasUsableAlpha(d.Image))
    End Sub

    <Fact>
    Public Sub JpegWithoutWindowsCodec_FailsWithReadableMessage()
        Dim dir = TestHelpers.TempDir()
        Dim path = IO.Path.Combine(dir, "photo.jpg")
        File.WriteAllBytes(path, {&HFF, &HD8, &HFF, &HE0, 0, 0})
        Dim loader As New ImageLoader()
        Dim ex = Assert.Throws(Of NotSupportedException)(Function() loader.Load(path))
        Assert.Contains("photo.jpg", ex.Message)
    End Sub

    <Fact>
    Public Sub Mask16_RoundTrip_KeepsFinePrecision()
        Dim dir = TestHelpers.TempDir()
        Dim m As New FloatMask(50, 3)
        For i = 0 To m.Data.Length - 1
            m.Data(i) = (i Mod 1000) / 999.0F
        Next
        Dim path = IO.Path.Combine(dir, "m.png")
        PngCodec.SaveMask16(m, path)
        Dim r = PngCodec.LoadMask(path)
        For i = 0 To m.Data.Length - 1
            Assert.InRange(Math.Abs(r.Data(i) - m.Data(i)), 0.0F, 1.0F / 65535 + 0.000001F)
        Next
    End Sub

    <Theory>
    <InlineData(1)>
    <InlineData(2)>
    <InlineData(3)>
    <InlineData(4)>
    <InlineData(5)>
    <InlineData(6)>
    <InlineData(7)>
    <InlineData(8)>
    Public Sub ExifOrientation_ProducesUprightImage(orientation As Integer)
        ' 正確方向的影像：3×2，左上紅、右上綠，其餘藍。
        Dim upright As New RgbaImage(3, 2)
        For y = 0 To 1
            For x = 0 To 2
                upright.SetPixel(x, y, 0, 0, 255, 255)
            Next
        Next
        upright.SetPixel(0, 0, 255, 0, 0, 255)
        upright.SetPixel(2, 0, 0, 255, 0, 255)
        ' 依 EXIF 定義產生「感光元件上存的樣子」，再轉正應該回到原圖。
        Dim stored = InverseOrientation(upright, orientation)
        Dim fixedImg = ExifOrientation.Apply(stored, orientation)
        Assert.Equal(3, fixedImg.Width)
        Assert.Equal(2, fixedImg.Height)
        TestHelpers.SameSeq(upright.Pixels, fixedImg.Pixels)
    End Sub

    ' 由正確影像產生各 Orientation 下儲存的像素（Apply 的反運算）。
    Private Shared Function InverseOrientation(img As RgbaImage, o As Integer) As RgbaImage
        Select Case o
            Case 1 : Return img
            Case 2, 3, 4, 5, 7 : Return ExifOrientation.Apply(img, o) ' 這幾種是自己的反運算
            Case 6 : Return ExifOrientation.Apply(img, 8)
            Case Else : Return ExifOrientation.Apply(img, 6)
        End Select
    End Function

    <Fact>
    Public Sub ImageLimits_RejectOversizedImagesBeforeAllocating()
        Assert.Throws(Of ImageTooLargeException)(Sub() ImageLimits.CheckSize(40000, 40000, 4))
        Assert.Throws(Of ImageTooLargeException)(Sub() ImageLimits.CheckSize(30000, 30000, 4))
        ImageLimits.CheckSize(8000, 6000, 4)
        Dim est = ImageLimits.EstimateCompositeBytes(6000, 4000, 2000L * 3000)
        Assert.True(est > 6000L * 4000 * 16)
    End Sub

    <Fact>
    Public Sub Downscale_UsesPremultipliedAverage_NoFringeFromTransparentRgb()
        ' 透明像素藏著白色 RGB，不能滲進縮圖。
        Dim img As New RgbaImage(2, 1)
        img.SetPixel(0, 0, 255, 255, 255, 0)
        img.SetPixel(1, 0, 200, 0, 0, 255)
        Dim r = img.ResizeArea(1, 1)
        Dim px = r.GetPixel(0, 0)
        Assert.Equal(200, CInt(px.R))
        Assert.Equal(0, CInt(px.G))
        Assert.Equal(128, CInt(px.A))
    End Sub

    <Fact>
    Public Sub Blur_PreservesMassApproximately()
        Dim m As New FloatMask(41, 41)
        m(20, 20) = 1
        Blur.GaussianInPlace(m, 3)
        Assert.InRange(m.Sum(), 0.97, 1.03)
        Assert.True(m(20, 20) < 0.1F)
        Assert.True(m(20, 20) > m(25, 20))
    End Sub
End Class
