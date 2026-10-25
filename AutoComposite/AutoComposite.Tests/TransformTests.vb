Imports Xunit

Public Class TransformTests
    Private Shared Sub Near(expected As Double, actual As Double, Optional tol As Double = 0.000001)
        Assert.InRange(actual, expected - tol, expected + tol)
    End Sub

    <Fact>
    Public Sub Compose_TranslateScaleRotate_MapsAnchorToTarget()
        Dim t As New LayerTransform With {.X = 500, .Y = 300, .Scale = 0.5, .Rotation = 30}
        Dim anchor As New PointD(40, 100)
        Dim m = TransformService.Compose(t, anchor, 80, 100)
        Dim p = m.Transform(anchor)
        Near(500, p.X) : Near(300, p.Y)
        ' 錨點正上方 100 px（人物座標）→ 縮放 0.5 後長 50，順時針轉 30°。
        Dim q = m.Transform(New PointD(40, 0))
        Near(500 + 50 * Math.Sin(30 * Math.PI / 180), q.X, 0.0001)
        Near(300 - 50 * Math.Cos(30 * Math.PI / 180), q.Y, 0.0001)
        Near(0.5, m.LocalScale(anchor), 0.0001)
    End Sub

    <Fact>
    Public Sub Matrix_InverseRoundTrips()
        Dim m = Matrix3.Translation(10, -4) * Matrix3.Rotation(17) * Matrix3.Scaling(2.5, 1.5)
        Dim inv = m.Inverse()
        Dim p As New PointD(123.4, -56.7)
        Dim back = inv.Transform(m.Transform(p))
        Near(p.X, back.X, 0.000001) : Near(p.Y, back.Y, 0.000001)
    End Sub

    <Fact>
    Public Sub FromQuad_MapsFourCornersExactly()
        Dim src = {New PointD(0, 0), New PointD(100, 0), New PointD(100, 200), New PointD(0, 200)}
        Dim dst = {New PointD(20, 10), New PointD(80, 12), New PointD(110, 190), New PointD(-5, 205)}
        Dim h = Matrix3.FromQuad(src, dst)
        Assert.False(h.IsAffine)
        For i = 0 To 3
            Dim p = h.Transform(src(i))
            Near(dst(i).X, p.X, 0.00001) : Near(dst(i).Y, p.Y, 0.00001)
        Next
    End Sub

    <Fact>
    Public Sub FromQuad_DegenerateThrows()
        Dim src = {New PointD(0, 0), New PointD(1, 1), New PointD(2, 2), New PointD(0, 5)}
        Assert.Throws(Of InvalidOperationException)(Function() Matrix3.FromQuad(src, src))
    End Sub

    <Fact>
    Public Sub Keystone_KeepsAnchorAndNarrowsTop()
        Dim t As New LayerTransform With {.X = 0, .Y = 0, .Scale = 1,
            .Perspective = New PerspectiveParams With {.Mode = PerspectiveMode.Keystone, .KeystoneRatio = 0.8}}
        Dim anchor As New PointD(50, 200) ' 腳在底部中央
        Dim m = TransformService.Compose(t, anchor, 100, 200)
        Dim a = m.Transform(anchor)
        Near(0, a.X, 0.000001) : Near(0, a.Y, 0.000001)
        Dim tl = m.Transform(New PointD(0, 0)), tr = m.Transform(New PointD(100, 0))
        Dim bl = m.Transform(New PointD(0, 200)), br = m.Transform(New PointD(100, 200))
        Near(0.8, (tr.X - tl.X) / (br.X - bl.X), 0.0001)
        ' 頭部仍在錨點正上方（左右對稱）。
        Near(0, (tl.X + tr.X) / 2, 0.0001)
        ' 高度不變（VerticalFactor = 1），不拉長人物。
        Near(-200, tl.Y, 0.0001)
    End Sub

    <Fact>
    Public Sub Keystone_IsClampedToSafeRange()
        Dim t As New LayerTransform With {.Scale = 1, .Perspective = New PerspectiveParams With {.Mode = PerspectiveMode.Keystone, .KeystoneRatio = 0.1}}
        Dim m = TransformService.Compose(t, New PointD(50, 100), 100, 100)
        Dim tl = m.Transform(New PointD(0, 0)), tr = m.Transform(New PointD(100, 0))
        Near(TransformService.MinKeystone, (tr.X - tl.X) / 100, 0.0001)
    End Sub

    <Fact>
    Public Sub KeystoneFromGuides_ConvergingColumnEdges()
        ' 兩條往上收斂的柱邊，消失點在 (100, -900)。
        Dim a As New GuideLine With {.X1 = 50, .Y1 = 1000, .X2 = 100 - 50 * 0.5, .Y2 = 50}
        Dim b As New GuideLine With {.X1 = 150, .Y1 = 1000, .X2 = 100 + 50 * 0.5, .Y2 = 50}
        Dim vp = TransformService.Intersect(a.P1, a.P2, b.P1, b.P2)
        Assert.True(vp.HasValue)
        Near(-900, vp.Value.Y, 0.01)
        ' 腳在 y = 500，人物高 280 → 頂部 y = 220：寬度比 (220+900)/(500+900) = 0.8。
        Near(0.8, TransformService.KeystoneFromGuides(a, b, 500, 280), 0.0001)
        ' 平行參考線 → 1。
        Dim c As New GuideLine With {.X1 = 0, .Y1 = 0, .X2 = 0, .Y2 = 100}
        Dim d As New GuideLine With {.X1 = 10, .Y1 = 0, .X2 = 10, .Y2 = 100}
        Assert.Equal(1.0, TransformService.KeystoneFromGuides(c, d, 500, 280))
    End Sub

    <Fact>
    Public Sub RenderLayer_TranslationOnly_IsPixelExact()
        Dim src = TestHelpers.StickFigure(20, 30)
        Dim alpha = TestHelpers.StickMask(src)
        Dim sampler As New LayerSampler(src, alpha)
        Dim svc As New TransformService()
        Dim m = Matrix3.Translation(7, 5)
        Dim tile = svc.RenderLayer(sampler, m, 50, 50, ResampleQuality.Bicubic)
        ' 整數平移時每個像素中心剛好落在原像素中心，雙三次權重退化成 1。
        For y = 0 To 29
            For x = 0 To 19
                Dim gx = x + 7, gy = y + 5
                Dim o = ((gy - tile.Bounds.Y) * tile.Bounds.Width + (gx - tile.Bounds.X)) * 4
                Assert.InRange(tile.Data(o + 3), alpha(x, y) - 0.0001F, alpha(x, y) + 0.0001F)
            Next
        Next
    End Sub

    <Fact>
    Public Sub RenderLayer_ScaleAndRotate_KeepsCoverageArea()
        Dim src = TestHelpers.StickFigure(40, 60)
        Dim alpha = TestHelpers.StickMask(src)
        Dim sampler As New LayerSampler(src, alpha)
        Dim svc As New TransformService()
        Dim t As New LayerTransform With {.X = 100, .Y = 150, .Scale = 2, .Rotation = 45}
        Dim m = TransformService.Compose(t, New PointD(20, 55), 40, 60)
        Dim tile = svc.RenderLayer(sampler, m, 300, 300, ResampleQuality.Bilinear)
        Dim covered = 0.0
        For i = 3 To tile.Data.Length - 1 Step 4
            covered += tile.Data(i)
        Next
        ' 面積放大 scale² = 4 倍（旋轉不改變面積）。
        Assert.InRange(covered / alpha.Sum(), 3.85, 4.15)
    End Sub

    <Fact>
    Public Sub RenderLayer_StrongDownscale_UsesPyramidWithoutAliasingLoss()
        Dim src = TestHelpers.StickFigure(400, 600)
        Dim alpha = TestHelpers.StickMask(src)
        Dim sampler As New LayerSampler(src, alpha)
        Assert.True(sampler.LevelFor(0.1) >= 2)
        Dim tile = New TransformService().RenderLayer(sampler, Matrix3.Scaling(0.1, 0.1), 40, 60, ResampleQuality.Bicubic)
        Dim covered = 0.0
        For i = 3 To tile.Data.Length - 1 Step 4
            covered += tile.Data(i)
        Next
        Assert.InRange(covered / (alpha.Sum() * 0.01), 0.93, 1.07)
    End Sub

    <Fact>
    Public Sub PerspectiveBehindViewer_IsRejected()
        Dim m As New Matrix3 With {.M11 = 1, .M22 = 1, .M31 = -0.02, .M33 = 1}
        Assert.Throws(Of InvalidOperationException)(Sub() TransformService.CheckProjectable(m, 100, 100))
    End Sub
End Class
