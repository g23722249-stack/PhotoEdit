Imports PhotoEdit

''' <summary>共用去背面板的遮罩計算：內縮／外擴、去掉零碎小塊、填補小破洞、筆刷、去除殘色開關、淡層清除。</summary>
Module CutoutEditTests

    ''' <summary>100×100：中間一個 40×60 的人（30..69 × 20..79），人身上一個 4×4 的洞，左上角一個 3×3 的碎塊，右邊一個碰到圖邊的大缺口。</summary>
    Private Function Mask() As Byte()
        Dim m(100 * 100 - 1) As Byte
        For y = 20 To 79
            For x = 30 To 69
                m(y * 100 + x) = 255
            Next
        Next
        For y = 40 To 43
            For x = 48 To 51
                m(y * 100 + x) = 0
            Next
        Next
        For y = 5 To 7
            For x = 5 To 7
                m(y * 100 + x) = 255
            Next
        Next
        Return m
    End Function

    Private Function Count(m As Byte()) As Integer
        Return m.Where(Function(v) v >= 128).Count()
    End Function

    Sub CutoutEditTestsRun()
        Console.WriteLine("去背面板（遮罩修整）")
        Dim m = Mask()
        Dim grown = CutoutEdit.Shift(m, 100, 100, 3), shrunk = CutoutEdit.Shift(m, 100, 100, -3)
        Check("外擴：人變大、邊往外 3 像素", Count(grown) > Count(m) AndAlso grown(50 * 100 + 27) = 255 AndAlso grown(50 * 100 + 26) = 0, $"{Count(m)} → {Count(grown)}")
        Check("內縮：人變小、邊往內 3 像素", Count(shrunk) < Count(m) AndAlso shrunk(50 * 100 + 32) = 0 AndAlso shrunk(50 * 100 + 33) = 255, $"{Count(m)} → {Count(shrunk)}")
        Check("內縮／外擴 0：原樣", CutoutEdit.Shift(m, 100, 100, 0) Is m)

        Dim clean = CutoutEdit.RemoveSpecks(m, 100, 100)
        Check("去掉零碎小塊：左上的碎塊清掉、人還在", clean(6 * 100 + 6) = 0 AndAlso clean(50 * 100 + 50) = 255 AndAlso Count(clean) = Count(m) - 9)
        Dim filled = CutoutEdit.FillHoles(m, 100, 100)
        Check("填補小破洞：人身上的洞補起來、外面的背景不動", filled(41 * 100 + 49) = 255 AndAlso filled(50 * 100 + 90) = 0 AndAlso filled(1) = 0)
        ' 很大的洞（像手臂和身體之間）不補
        Dim big = Mask()
        For y = 30 To 69
            For x = 35 To 64
                big(y * 100 + x) = 0
            Next
        Next
        Check("填補小破洞：大的空隙不補", CutoutEdit.FillHoles(big, 100, 100)(50 * 100 + 50) = 0)

        ' 整套：原圖本來透明時不精修；設定依序套用；筆刷最後
        Dim px(100 * 100 * 4 - 1) As Byte
        Dim opts As New CutoutEditOptions With {.EdgeRefine = 0, .RemoveSpecks = True, .FillHoles = True}
        Dim dabs = {New CutoutDab(90, 50, 4, True), New CutoutDab(40, 50, 4, False)}
        Dim built = CutoutEdit.Build(m, False, px, 100, 100, CutoutModel.ModNet, opts, dabs)
        Check("整套：碎塊清掉、洞補起來、保留筆刷補上、擦除筆刷擦掉",
              built(6 * 100 + 6) = 0 AndAlso built(41 * 100 + 49) = 255 AndAlso built(50 * 100 + 90) = 255 AndAlso built(50 * 100 + 40) = 0)
        Check("整套：不會改到 AI 遮罩本身", m(6 * 100 + 6) = 255 AndAlso m(50 * 100 + 40) = 255)
        Dim plain = CutoutEdit.Build(m, True, px, 100, 100, CutoutModel.ModNet, New CutoutEditOptions(), Nothing)
        Check("整套：原圖本來透明、沒設定時原樣（但是複本）", plain.SequenceEqual(m) AndAlso plain IsNot m)

        ' 去除殘色：關掉時像素不動
        Dim before = DirectCast(px.Clone(), Byte())
        CutoutEdit.Decontaminate(px, m, 100, 100, New CutoutEditOptions With {.Decontaminate = False}, CutoutModel.ModNet)
        Check("去除邊緣殘色關掉：像素不變", px.SequenceEqual(before))

        Dim faint = CutoutEdit.StretchFaint({0, 20, 26, 128, 229, 240, 255})
        Check("淡層清除：10% 以下變 0、90% 以上變 255、中間拉開", faint(1) = 0 AndAlso faint(5) = 255 AndAlso faint(6) = 255 AndAlso Math.Abs(faint(3) - 128) <= 1, String.Join(",", faint))
    End Sub
End Module
