Imports PhotoEdit

' 使用手冊「觀念與技巧」：圖層的介紹與功能、向量物件的結合與打散。
' 示意圖當場用 PhotoEdit 自己的繪圖與焊接／修剪程式畫出來，和實際結果一致。
Partial Class frmEditor

    Private Function BuildConceptSection() As ManualSection
        Dim s As New ManualSection(Lang.T("觀念與技巧"))
        s.Add(ManualBlock.Para(Lang.T("給剛開始電繪的人：一張圖從草稿到完成的流程、筆刷參數、繪圖板手感、選顏色、不怕畫壞的習慣，以及圖層與向量圖形的觀念。照順序讀，或直接跳到需要的那篇。")))
        Dim links As New ManualBlock With {.Kind = ManualBlockKind.Links}
        links.LinkTargets.Add(BuildWorkflowConcept(s))
        links.LinkTargets.Add(BuildLayerConcept(s))
        links.LinkTargets.Add(BuildBrushConcept(s))
        links.LinkTargets.Add(BuildTabletConcept(s))
        links.LinkTargets.Add(BuildColorConcept(s))
        links.LinkTargets.Add(BuildUndoConcept(s))
        links.LinkTargets.Add(BuildReferenceConcept(s))
        links.LinkTargets.Add(BuildGuidesConcept(s))
        links.LinkTargets.Add(BuildColoringConcept(s))
        links.LinkTargets.Add(BuildPenConcept(s))
        links.LinkTargets.Add(BuildLightConcept(s))
        links.LinkTargets.Add(BuildExportConcept(s))
        links.LinkTargets.Add(BuildQuickConcept(s))
        links.LinkTargets.Add(BuildPaperConcept(s))
        links.LinkTargets.Add(BuildCombineConcept(s))
        s.Add(links)
        Return s
    End Function

    '---------------------------------------------------------------- 基本流程

    Private Function BuildWorkflowConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("電繪的基本流程"))
        t.Add(ManualBlock.Para(Lang.T("不管畫什麼，大多照這個順序：草稿 → 線稿 → 上色 → 陰影與亮部 → 收尾。每一步開一個新圖層，畫壞了只要重畫那一層，其他的不受影響。")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(120, 120)}
        Dim captions = {(Lang.T("1. 草稿"), Lang.T("淺色鉛筆，輕輕畫出大概的形狀，畫錯不用擦。")),
                        (Lang.T("2. 線稿"), Lang.T("新圖層，深色硬筆或墨水，沿著草稿描出乾淨的線；草稿隱藏。")),
                        (Lang.T("3. 上色"), Lang.T("線稿下面開新圖層，先平平地塗底色。")),
                        (Lang.T("4. 陰影與亮部"), Lang.T("再開一層，決定光從哪裡來，加暗面與亮點。"))}
        For i = 0 To 3
            g.Tiles.Add(New ManualTile With {.Image = WorkflowSwatch(i), .Title = captions(i).Item1, .Text = captions(i).Item2})
        Next
        t.Add(g)

        t.Add(ManualBlock.Heading(Lang.T("1. 草稿")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9 快速面板按「＋ 新圖層」，取名「草稿」。"),
            Lang.T("選鉛筆、淺灰色，不透明度 50～70%。"),
            Lang.T("先用大圓、方塊、線條抓出位置與比例，不要一開始就畫細節。"),
            Lang.T("有參考圖時用「檢視 → 參考圖…」放在旁邊對照；要描的話開「描圖」。")))
        t.Add(ManualBlock.Heading(Lang.T("2. 線稿")))
        t.Add(ManualBlock.Steps(
            Lang.T("新圖層「線稿」，選硬筆或墨水、深色，大小調小。"),
            Lang.T("看著草稿，一筆畫長一點、不要短短地來回描；手抖的話在「進階」打開穩定器。"),
            Lang.T("描完把「草稿」那層的眼睛關掉，只看線稿。")))
        t.Add(ManualBlock.Heading(Lang.T("3. 上色")))
        t.Add(ManualBlock.Steps(
            Lang.T("新圖層「上色」，在圖層清單把它移到線稿下面，顏色才不會蓋住線。"),
            Lang.T("大面積用大筆刷或油漆桶塗底色；油漆桶要線稿是封閉的才不會漏出去。"),
            Lang.T("不同的部位（皮膚、衣服、頭髮）可以各開一層，之後改色比較方便。")))
        t.Add(ManualBlock.Heading(Lang.T("4. 陰影與亮部")))
        t.Add(ManualBlock.Steps(
            Lang.T("先決定光從哪個方向來（例如左上）。"),
            Lang.T("在上色層上面開新圖層「陰影」，用比底色深一點、偏冷或偏紫的顏色，畫在背光的那一側。"),
            Lang.T("陰影層可以把混合模式改成「色彩增值」、不透明度調到 50～70%，顏色會自然地壓在底色上。"),
            Lang.T("最後用淺色或白色在受光最強的地方點亮部。")))
        t.Add(ManualBlock.Heading(Lang.T("5. 收尾")))
        t.Add(ManualBlock.Bullets(
            Lang.T("縮小整張看看（滾輪縮放），或用「檢視 → 左右翻轉檢視」，比例歪掉的地方一眼就看得出來。"),
            Lang.T("需要時加背景、紙紋、文字。"),
            Lang.T("Ctrl+S 存成 .pedx 保留所有圖層，再「匯出…」成 PNG 或 JPG 分享。")))
        t.Add(ManualBlock.Note(Lang.T("「使用教學」的三篇作品也是照這個流程畫的，可以跟著做一次。")))
        Return t
    End Function

    ''' <summary>流程示意：同一顆蘋果的草稿、線稿、上色、陰影。</summary>
    Private Shared Function WorkflowSwatch(stage As Integer) As Bitmap
        Const S As Integer = 120
        Dim bmp As New Bitmap(S, S)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.Clear(Color.FromArgb(252, 251, 248))
            Dim apple As New Drawing2D.GraphicsPath()
            apple.AddBezier(60, 38, 40, 22, 14, 36, 18, 64)
            apple.AddBezier(18, 64, 22, 92, 44, 104, 60, 96)
            apple.AddBezier(60, 96, 76, 104, 98, 92, 102, 64)
            apple.AddBezier(102, 64, 106, 36, 80, 22, 60, 38)
            apple.CloseFigure()
            Dim stem = {New PointF(60, 38), New PointF(62, 24), New PointF(66, 16)}
            Dim leaf As New Drawing2D.GraphicsPath()
            leaf.AddBezier(64, 24, 72, 12, 86, 14, 90, 20)
            leaf.AddBezier(90, 20, 82, 28, 70, 28, 64, 24)
            If stage = 0 Then
                Dim rnd As New Random(3)
                Using p As New Pen(Color.FromArgb(150, 150, 150, 150), 1.2F)
                    For k = 0 To 2
                        g.DrawEllipse(p, 18 + rnd.Next(-3, 4), 30 + rnd.Next(-3, 4), 84 + rnd.Next(-4, 4), 70 + rnd.Next(-4, 4))
                    Next
                    g.DrawLine(p, 60, 40, 64, 16)
                    g.DrawEllipse(p, 66, 14, 22, 10)
                    g.DrawLine(p, 18, 65, 102, 65) : g.DrawLine(p, 60, 30, 60, 100)
                End Using
            Else
                If stage >= 2 Then
                    Using br As New SolidBrush(Color.FromArgb(214, 52, 48))
                        g.FillPath(br, apple)
                    End Using
                    Using br As New SolidBrush(Color.FromArgb(90, 170, 70))
                        g.FillPath(br, leaf)
                    End Using
                End If
                If stage >= 3 Then
                    Dim old = g.Clip
                    g.SetClip(apple)
                    Using br As New SolidBrush(Color.FromArgb(120, 90, 20, 60))
                        g.FillEllipse(br, 48, 40, 80, 80)
                    End Using
                    g.Clip = old
                    Using br As New SolidBrush(Color.FromArgb(230, 255, 255, 255))
                        g.FillEllipse(br, 34, 48, 12, 18)
                    End Using
                    Using br As New SolidBrush(Color.FromArgb(70, 0, 0, 0))
                        g.FillEllipse(br, 26, 100, 72, 10)
                    End Using
                End If
                Using p As New Pen(Color.FromArgb(40, 30, 30), 2.2F) With {.LineJoin = Drawing2D.LineJoin.Round}
                    g.DrawPath(p, apple)
                    g.DrawLines(p, stem)
                    g.DrawPath(p, leaf)
                End Using
            End If
            Using p As New Pen(Color.FromArgb(214, 218, 226))
                g.DrawRectangle(p, 0, 0, S - 1, S - 1)
            End Using
        End Using
        Return bmp
    End Function

    '---------------------------------------------------------------- 筆刷參數

    Private Shared Function BuildBrushConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("筆刷的幾個參數"))
        t.Add(ManualBlock.Para(Lang.T("繪圖頁籤與 F9 快速面板裡的滑桿，決定一筆畫出來的樣子。下面每格都是只改一個設定畫出來的（下方墊一條黑帶看透明度）；不透明度和流量兩格是「一筆畫成 X」，看同一筆來回經過的地方會不會變深。")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(160, 80)}
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d) d.StrokeWidth = 0.04), .Title = Lang.T("一般"), .Text = Lang.T("不透明度、流量 100%，柔邊 0。")})
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d) d.StrokeWidth = 0.1), .Title = Lang.T("粗細"), .Text = Lang.T("線條的寬度（以照片高度為準，縮放畫面不會變）。")})
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d) d.Softness = 70), .Title = Lang.T("柔邊 70"), .Text = Lang.T("邊緣淡化，越大越柔；畫陰影、腮紅、光暈常用。")})
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d) d.Opacity = 50, oneStroke:=True), .Title = Lang.T("不透明度 50%（硬筆）"), .Text = Lang.T("一筆最多這麼深：同一筆來回經過不會更深。")})
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d)
                                                                       d.Brush = BrushKind.Watercolor
                                                                       d.Flow = 35
                                                                   End Sub, oneStroke:=True), .Title = Lang.T("流量 35%（水彩）"), .Text = Lang.T("每個筆印的顏料少：同一筆來回刷，交叉處慢慢疊深。")})
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d) d.SpacingPct = 180), .Title = Lang.T("間距 180%"), .Text = Lang.T("筆印之間的距離，調大會變成一顆顆的點（虛線、珠串）。")})
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d) d.Scatter = 70), .Title = Lang.T("散佈 70"), .Text = Lang.T("筆印隨機往兩側散開，適合畫星星、雪、樹葉。")})
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d)
                                                                                         d.StrokeWidth = 0.09
                                                                                         d.PressureOpacity = 100
                                                                                     End Sub, pressureRamp:=True), .Title = Lang.T("筆壓濃淡 100"), .Text = Lang.T("繪圖筆輕畫淡、重畫濃（這格的筆壓由左往右越來越重）。")})
        t.Add(g)
        t.Add(ManualBlock.Heading(Lang.T("不透明度和流量差在哪裡")))
        t.Add(ManualBlock.Bullets(
            Lang.T("不透明度：一筆最多多深。同一筆裡來回塗不會更深；放開筆再畫一筆疊上去才會更深。向量圖形則是整個圖形的透明度；整個圖層要淡一點，到「圖層」頁籤調圖層的不透明度。"),
            Lang.T("流量：每個筆印帶多少顏料。水彩、噴槍的流量調低時，同一筆來回刷就會慢慢疊深，適合輕輕刷出漸層；硬筆、鉛筆這類流量調低只是變淡。"),
            Lang.T("新手常見的狀況：不透明度調低後一直來回塗都不變深——那是正常的。放開筆再畫一筆，或改用水彩、噴槍配低流量。")))
        t.Add(ManualBlock.Heading(Lang.T("其他常用設定")))
        t.Add(ManualBlock.Bullets(
            Lang.T("穩定器（「進階」裡）：畫長弧線時去掉手抖，數字越大線越穩，但筆尖會稍微跟在游標後面。"),
            Lang.T("大小變化、明暗變化：每個筆印隨機變化，畫草地、毛髮、雲比較自然。"),
            Lang.T("紙紋吃色：文件有紙紋時，筆畫只畫到紙的凸起，炭筆、粉彩最明顯。"),
            Lang.T("暈開：毛筆、水彩的墨沿著紙纖維滲開。")))
        t.Add(ManualBlock.Note(Lang.T("先選對筆刷（硬筆畫線、軟筆或噴槍畫漸層、水彩疊色），再調參數；每支筆刷的樣子可以看「右側頁籤 → 繪圖 → 筆觸」。")))
        Return t
    End Function

    ''' <summary>筆刷參數示意：同一個圖層裡交叉的兩筆硬筆，後面墊一條黑帶。</summary>
    Private Shared Function BrushParamSwatch(setup As Action(Of DrawLayer), Optional pressureRamp As Boolean = False, Optional oneStroke As Boolean = False) As Bitmap
        Const W As Integer = 160, H As Integer = 80
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.FromArgb(252, 251, 248))
            Using br As New SolidBrush(Color.FromArgb(40, 40, 46))
                g.FillRectangle(br, 0, 62, W, 10)
            End Using
        End Using
        Dim line = Function(x0 As Single, y0 As Single, x1 As Single, y1 As Single)
                       Dim s As New DrawStroke()
                       For i = 0 To 30
                           Dim k = i / 30.0F
                           s.Points.Add(New DrawPoint(x0 + (x1 - x0) * k, y0 + (y1 - y0) * k + CSng(Math.Sin(k * Math.PI)) * 0.06F,
                                                      If(pressureRamp, 0.08F + 0.92F * k, 1.0F)))
                       Next
                       Return s
                   End Function
        Dim d As New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.HardRound, .StrokeWidth = 0.06, .Seed = 5,
                                     .StrokeColorArgb = Color.FromArgb(40, 90, 200).ToArgb()}
        setup(d)
        Dim shown As DrawLayer = d
        If pressureRamp Then
            d.Strokes = New List(Of DrawStroke) From {line(0.15F, 0.42F, 1.85F, 0.42F)}
        ElseIf oneStroke Then
            ' 一筆畫成 X（中間不放筆），畫在點陣圖層上（同直接繪製）
            Dim one = line(0.2F, 0.2F, 1.8F, 0.8F)
            one.Points.AddRange(line(0.2F, 0.8F, 1.8F, 0.2F).Points.AsEnumerable().Reverse())
            d.Strokes = New List(Of DrawStroke) From {one}
            shown = New DrawLayer With {.Shape = DrawShape.Raster, .Ops = New List(Of DrawLayer) From {d}}
        Else
            d.Strokes = New List(Of DrawStroke) From {line(0.2F, 0.2F, 1.8F, 0.8F), line(0.2F, 0.8F, 1.8F, 0.2F)}
        End If
        DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {shown}})
        Using g = Graphics.FromImage(bmp), p As New Pen(Color.FromArgb(214, 218, 226))
            g.DrawRectangle(p, 0, 0, W - 1, H - 1)
        End Using
        Return bmp
    End Function

    '---------------------------------------------------------------- 繪圖板

    Private Function BuildTabletConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("繪圖板設定與手感"))
        t.Add(ManualBlock.Para(Lang.T("繪圖板（Wacom 等數位板、繪圖螢幕、觸控筆）最大的好處是筆壓：輕輕畫細而淡、用力畫粗而濃，就像真的筆。PhotoEdit 會自動讀取筆壓，不用另外設定也能畫。")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(160, 80)}
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d) d.StrokeWidth = 0.09, pressureRamp:=True), .Title = Lang.T("筆壓 → 粗細"),
                                         .Text = Lang.T("由左往右越壓越重：線條由細變粗。")})
        g.Tiles.Add(New ManualTile With {.Image = BrushParamSwatch(Sub(d)
                                                                       d.StrokeWidth = 0.09
                                                                       d.PressureOpacity = 100
                                                                   End Sub, pressureRamp:=True), .Title = Lang.T("筆壓 → 粗細＋濃淡"),
                                         .Text = Lang.T("再打開「筆壓濃淡」，輕畫也會比較淡。")})
        t.Add(g)
        t.Add(ManualBlock.Heading(Lang.T("調整筆壓手感（檔案 → 設定… → 繪圖筆）")))
        t.Add(ManualBlock.Bullets(
            Lang.T("軟硬：軟＝輕輕畫就夠粗夠濃；硬＝要用力才會粗。手輕的人選軟，容易壓太重的人選硬。"),
            Lang.T("最小筆壓：輕碰時的最細程度，避免起筆斷掉。"),
            Lang.T("滿壓力道：多大的力道就算「最重」。手輕的人調低，不必很用力就能畫出最粗的線。"),
            Lang.T("筆壓曲線：圖上可以直接看出力道和粗細的關係。"),
            Lang.T("穩定器：去掉手抖，畫長弧線、簽名特別有用。")))
        t.Add(ManualBlock.Heading(Lang.T("筆的按鈕與橡皮擦")))
        t.Add(ManualBlock.Bullets(
            Lang.T("筆身按鈕：按住時暫時變成橡皮擦，放開回到原本的筆。"),
            Lang.T("把筆翻過來用尾端（橡皮擦端）畫，就會自動擦。"),
            Lang.T("快速點兩下筆尖＝滑鼠按兩下。"),
            Lang.T("繪圖筆的傾斜與旋轉（「進階」裡）會改變毛筆、扁筆的筆尖方向與扁平程度。")))
        t.Add(ManualBlock.Heading(Lang.T("讓手不離開畫布")))
        t.Add(ManualBlock.Bullets(
            Lang.F("{0}：在筆旁邊叫出快速面板，換筆刷、大小、顏色、新增圖層都在這裡。", _appSettings.QuickPanelHotkey),
            Lang.F("{0}：圓形快速選單。", _appSettings.QuickRadialHotkey),
            Lang.T("F11：全螢幕繪圖，畫面最大。"),
            Lang.T("繪圖板的快捷鍵可以在驅動程式裡設成上面這些鍵、Ctrl+Z（復原）與 E（橡皮擦）。")))
        t.Add(ManualBlock.Heading(Lang.T("筆壓沒有反應時")))
        t.Add(ManualBlock.Steps(
            Lang.T("確認繪圖板的驅動程式已安裝，而且在驅動程式設定裡打開了「Windows Ink」（Wacom 是「使用 Windows Ink」）。"),
            Lang.T("換一支會用到筆壓的筆刷（硬筆、毛筆、墨水），確認不是筆刷本身沒有粗細變化。"),
            Lang.T("到「設定 → 繪圖筆」看筆壓曲線；把「滿壓力道」調低再試。")))
        t.Add(ManualBlock.Note(Lang.T("沒有繪圖板也可以畫：用滑鼠畫毛筆、墨水時，會依移動速度模擬筆壓（快＝細、慢＝粗）。線條要很整齊時，改用鋼筆工具或形狀工具更好。")))
        Return t
    End Function

    '---------------------------------------------------------------- 顏色

    Private Shared Function BuildColorConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("顏色怎麼選"))
        t.Add(ManualBlock.Heading(Lang.T("在 PhotoEdit 裡選顏色")))
        t.Add(ManualBlock.Bullets(
            Lang.T("左側色盤：左鍵點色塊設成線條色（筆的顏色），右鍵設成填色。"),
            Lang.T("點色盤上方的大色塊開完整的選色視窗：色環、經典、調和、色票、常用（最近用過的顏色）。"),
            Lang.T("吸管工具，或任何工具按住 Alt 點一下，就吸取畫面上的顏色。"),
            Lang.T("參考圖視窗點一下也能吸色；打開「畫筆取色」，每一筆自動用參考圖那個位置的顏色。"),
            Lang.T("色盤的「＋」把目前的顏色加入文件色票，一張圖用固定的幾個顏色比較統一。")))
        t.Add(ManualBlock.Heading(Lang.T("色相、明度、彩度")))
        t.Add(ManualBlock.Bullets(
            Lang.T("色相：紅、橙、黃、綠、藍、紫，色環上的位置。"),
            Lang.T("明度：亮或暗。黑白照片只剩下明度——一張圖好不好看，明暗對比常常比顏色更重要。"),
            Lang.T("彩度：鮮豔或灰。全部都很鮮豔會很吵，主角鮮豔、背景灰一點，主角就跳出來。")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(150, 150)}
        g.Tiles.Add(New ManualTile With {.Image = HarmonySwatch(0), .Title = Lang.T("同色系"), .Text = Lang.T("同一個色相，只改明暗與彩度：最安全、最統一。")})
        g.Tiles.Add(New ManualTile With {.Image = HarmonySwatch(1), .Title = Lang.T("類似色"), .Text = Lang.T("色環上相鄰的顏色（例如黃、橙、紅）：溫和、自然。")})
        g.Tiles.Add(New ManualTile With {.Image = HarmonySwatch(2), .Title = Lang.T("互補色"), .Text = Lang.T("色環上正對面（例如藍和橙）：對比最強，適合讓重點跳出來，用一點點就好。")})
        t.Add(g)
        t.Add(ManualBlock.Para(Lang.T("選色視窗的「調和」頁會依目前的顏色列出這些配色，點一下就能用。")))
        t.Add(ManualBlock.Heading(Lang.T("陰影不要只加黑")))
        Dim sh As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(150, 110)}
        sh.Tiles.Add(New ManualTile With {.Image = ShadowSwatch(False), .Title = Lang.T("暗部加黑"), .Text = Lang.T("同一個顏色混黑：容易變得髒、灰。")})
        sh.Tiles.Add(New ManualTile With {.Image = ShadowSwatch(True), .Title = Lang.T("暗部偏冷、亮部偏暖"), .Text = Lang.T("暗部往藍紫偏、亮部往黃偏：顏色比較乾淨、有光線感。")})
        t.Add(sh)
        t.Add(ManualBlock.Note(Lang.T("不確定顏色好不好時，可以在「調整」頁暫時把飽和度拉到最低看黑白，檢查明暗夠不夠分明，看完按復原。")))
        Return t
    End Function

    ''' <summary>配色示意：色環上標出選到的顏色，下面一排色塊。kind 0 同色系、1 類似色、2 互補色。</summary>
    Private Shared Function HarmonySwatch(kind As Integer) As Bitmap
        Const S As Integer = 150
        Dim bmp As New Bitmap(S, S)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.Clear(Color.FromArgb(252, 251, 248))
            Dim cx = 75.0F, cy = 58.0F, r0 = 30.0F, r1 = 48.0F
            For a = 0 To 359 Step 3
                Using br As New SolidBrush(Hsv(a, 0.85, 0.95))
                    g.FillPie(br, cx - r1, cy - r1, r1 * 2, r1 * 2, a - 90, 4)
                End Using
            Next
            Using br As New SolidBrush(Color.FromArgb(252, 251, 248))
                g.FillEllipse(br, cx - r0, cy - r0, r0 * 2, r0 * 2)
            End Using
            Dim hues = If(kind = 0, {210.0, 210.0, 210.0, 210.0}, If(kind = 1, {20.0, 40.0, 0.0, 55.0}, {215.0, 215.0, 30.0, 215.0}))
            Dim sats = If(kind = 0, {0.9, 0.55, 0.35, 0.75}, {0.85, 0.85, 0.85, 0.7})
            Dim vals = If(kind = 0, {0.55, 0.8, 0.95, 0.35}, {0.95, 0.95, 0.9, 0.95})
            For Each hh In hues.Distinct()
                Dim ang = (hh - 90) * Math.PI / 180
                Dim mx = cx + CSng(Math.Cos(ang) * (r0 + r1) / 2), my = cy + CSng(Math.Sin(ang) * (r0 + r1) / 2)
                Using p As New Pen(Color.FromArgb(30, 30, 30), 2)
                    g.DrawEllipse(p, mx - 6, my - 6, 12, 12)
                End Using
            Next
            For i = 0 To 3
                Using br As New SolidBrush(Hsv(hues(i), sats(i), vals(i)))
                    g.FillRectangle(br, 15 + i * 31, 116, 28, 24)
                End Using
            Next
            Using p As New Pen(Color.FromArgb(214, 218, 226))
                g.DrawRectangle(p, 0, 0, S - 1, S - 1)
            End Using
        End Using
        Return bmp
    End Function

    ''' <summary>陰影示意：同一顆球，暗部混黑 vs 暗部偏冷、亮部偏暖。</summary>
    Private Shared Function ShadowSwatch(tinted As Boolean) As Bitmap
        Const W As Integer = 150, H As Integer = 110
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.Clear(Color.FromArgb(252, 251, 248))
            Dim baseC = Color.FromArgb(230, 120, 60)
            Dim lightC = If(tinted, Color.FromArgb(255, 205, 120), Color.FromArgb(245, 160, 110))
            Dim darkC = If(tinted, Color.FromArgb(120, 60, 110), Color.FromArgb(90, 45, 22))
            Using shadowBr As New SolidBrush(Color.FromArgb(If(tinted, 90, 110), If(tinted, 70, 0), If(tinted, 60, 0), If(tinted, 120, 0)))
                g.FillEllipse(shadowBr, 40, 88, 80, 14)
            End Using
            Using path As New Drawing2D.GraphicsPath()
                path.AddEllipse(35, 10, 80, 80)
                Using pb As New Drawing2D.PathGradientBrush(path) With {.CenterPoint = New PointF(58, 34), .CenterColor = lightC,
                                                                      .SurroundColors = {darkC}}
                    Dim blend As New Drawing2D.ColorBlend(3)
                    blend.Colors = {darkC, baseC, lightC}
                    blend.Positions = {0.0F, 0.55F, 1.0F}
                    pb.InterpolationColors = blend
                    g.FillPath(pb, path)
                End Using
            End Using
            Using p As New Pen(Color.FromArgb(214, 218, 226))
                g.DrawRectangle(p, 0, 0, W - 1, H - 1)
            End Using
        End Using
        Return bmp
    End Function

    Private Shared Function Hsv(h As Double, s As Double, v As Double) As Color
        h = ((h Mod 360) + 360) Mod 360
        Dim c = v * s, x = c * (1 - Math.Abs((h / 60) Mod 2 - 1)), m = v - c
        Dim r, g, b As Double
        Select Case CInt(Math.Floor(h / 60))
            Case 0 : r = c : g = x : b = 0
            Case 1 : r = x : g = c : b = 0
            Case 2 : r = 0 : g = c : b = x
            Case 3 : r = 0 : g = x : b = c
            Case 4 : r = x : g = 0 : b = c
            Case Else : r = c : g = 0 : b = x
        End Select
        Return Color.FromArgb(CInt((r + m) * 255), CInt((g + m) * 255), CInt((b + m) * 255))
    End Function

    '---------------------------------------------------------------- 不怕畫壞

    Private Shared Function BuildUndoConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("復原與不怕畫壞"))
        t.Add(ManualBlock.Para(Lang.T("電繪最大的好處是可以一直重來。養成下面幾個習慣，就能放心地試。")))
        t.Add(ManualBlock.Heading(Lang.T("原圖永遠不會被改")))
        t.Add(ManualBlock.Para(Lang.T("PhotoEdit 是非破壞性的：調色、修補、繪圖、文字都記在編輯檔裡，開啟的照片本身不會被修改。不滿意時，調整頁的「重設調整」可以回到原樣，按住「按住看原圖」可以隨時比較。")))
        t.Add(ManualBlock.Heading(Lang.T("復原與重做")))
        t.Add(ManualBlock.Bullets(
            Lang.T("Ctrl+Z 復原、Ctrl+Y 重做；畫一筆、拖一次、改一次顏色都各算一步，最多可以退 200 步。"),
            Lang.T("繪圖板的快捷鍵設一個 Ctrl+Z，畫錯就按，比拿橡皮擦快。"),
            Lang.T("復原紀錄在關閉程式後就沒了；要留住某個狀態，請存檔。")))
        t.Add(ManualBlock.Heading(Lang.T("常存檔、留版本")))
        t.Add(ManualBlock.Bullets(
            Lang.T("Ctrl+S 存成 .pedx（保留所有圖層，下次能繼續改）。PhotoEdit 不會自動存檔，畫一段就存一次。"),
            Lang.T("做大改變前用「另存新檔…」（Ctrl+Shift+S）存一份新版本，例如「貓_01.pedx」「貓_02.pedx」，想回頭時還找得到。"),
            Lang.T("匯出的 PNG／JPG 只有合成後的一張圖，不能再分圖層修改；作品一定要另外留一份 .pedx。")))
        t.Add(ManualBlock.Heading(Lang.T("先複製一份再試")))
        t.Add(ManualBlock.Bullets(
            Lang.T("想試試看大改一個圖層：先在圖層清單「複製」一份，把原本的隱藏起來，試壞了刪掉複本、打開原本的就好。"),
            Lang.T("合併圖層、點陣化、焊接這些做了就回不去（除了復原）的動作，做之前先複製或存一個版本。"),
            Lang.T("向量圖形畫好後還能改大小、顏色、節點；不確定的時候先用向量畫，最後再決定。")))
        t.Add(ManualBlock.Note(Lang.T("畫不好是正常的。多畫幾張、每張都存下來，過一陣子回頭看，就看得到自己的進步。")))
        Return t
    End Function

    '---------------------------------------------------------------- 圖層

    Private Shared Function BuildLayerConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("圖層的介紹與功能"))
        t.Add(ManualBlock.Para(Lang.T("圖層就像疊在照片上的一張張透明片：每張各畫各的，上面的蓋住下面的。要修改某個東西時只動它那一層，其他的不受影響；也可以隨時調換順序、隱藏、調淡或刪掉。")))
        Dim pic As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(320, 190)}
        pic.Tiles.Add(New ManualTile With {.Image = LayerStackSwatch(), .Title = Lang.T("由下往上疊"),
                                           .Text = Lang.T("照片與調色永遠在最底下，上面依序疊繪圖、文字、貼圖等圖層；看到的是從上往下看穿透明片的結果。")})
        t.Add(pic)

        t.Add(ManualBlock.Heading(Lang.T("圖層的種類")))
        t.Add(ManualBlock.Bullets(
            Lang.T("照片：原始照片加上調整、修補、人像、局部、效果的結果。它不是圖層，永遠在最底下，原始檔也不會被改動。"),
            Lang.T("點陣圖層：用「直接繪製」與筆刷畫的筆觸，像 Photoshop 的圖層；可以擦除、塗抹、仿製。「新增」建立的就是空白點陣圖層。"),
            Lang.T("向量圖形：矩形、橢圓、星形、鋼筆路徑、箭頭、圖說……每個圖形自己就是一個圖層，畫好後還能縮放、旋轉、改顏色、改節點，放大也不會糊。"),
            Lang.T("群組：像資料夾，把幾個圖層收在一起，一起移動、縮放、隱藏、鎖定。"),
            Lang.T("填滿圖層：整片的單色、漸層或材質，按兩下可以隨時再改。"),
            Lang.T("文字、貼圖、邊框：在各自的頁籤建立，也會出現在「圖層」頁籤的總清單裡。")))

        t.Add(ManualBlock.Heading(Lang.T("兩個地方管理圖層")))
        t.Add(ManualBlock.Bullets(
            Lang.T("「繪圖」頁籤下方的圖層清單：只列繪圖圖層（點陣、向量、群組、填滿），可以建群組、改名、拖曳排列。"),
            Lang.T("「圖層」頁籤：文字、貼圖與繪圖圖層的總清單，在這裡調整彼此的上下順序、混合模式與不透明度，以及合併圖層。")))

        t.Add(ManualBlock.Heading(Lang.T("常用操作")))
        t.Add(ManualBlock.Bullets(
            Lang.T("選取：在清單點一下；Ctrl＋點選加選、Shift＋點選選一段。點群組那一列選整個群組。"),
            Lang.T("顯示／隱藏：點眼睛。隱藏的圖層不會匯出，但還在，隨時可以再打開（例如打草稿的圖層畫完就隱藏）。"),
            Lang.T("鎖定：點鎖頭，鎖住的圖層不會被點到、移動或畫到；鎖群組時裡面一起鎖。"),
            Lang.T("改名：在繪圖頁的清單按兩下名稱。取好名字（例如「草稿」「淡墨」「勾線」）圖層多時比較好找。"),
            Lang.T("排順序：上移、下移、移到最上、移到最下，或直接拖曳；拖到群組上＝放進群組，拖到圖層上＝放到它上面。"),
            Lang.T("複製：複製選取的圖層，放在稍微偏右下的位置。"),
            Lang.T("刪除：Del 或「刪除」，可以按復原救回來。")))

        t.Add(ManualBlock.Heading(Lang.T("混合模式與不透明度")))
        t.Add(ManualBlock.Para(Lang.T("在「圖層」頁籤選一個圖層，可以調它的不透明度，以及它和下面所有東西（含照片）怎麼混合，和 Photoshop 相同。")))
        t.Add(ManualBlock.Bullets(
            Lang.T("色彩增值：白色變透明，適合疊線稿、掃描的手繪稿。"),
            Lang.T("濾色：黑色變透明，適合光線、火花、特效筆畫的光束。"),
            Lang.T("覆蓋、柔光：增加對比，適合疊紋理或光影。"),
            Lang.T("不透明度：整個圖層調淡，例如參考線或陰影。")))

        t.Add(ManualBlock.Heading(Lang.T("點陣與向量要分清楚")))
        t.Add(ManualBlock.Bullets(
            Lang.T("直接繪製畫在目前選取的點陣圖層上；沒選圖層時會自動新增一個。"),
            Lang.T("選到向量圖形時用直接繪製，會問你要「點陣化」（把那個圖形變成像素再畫上去，之後不能再改形狀）還是「新增點陣圖層」在它上面畫——通常選新增，原本的圖形保持可以修改。"),
            Lang.T("向量的工具（形狀、鋼筆、節點、焊接……）只對向量圖形有用；點陣圖層要用筆刷、橡皮擦、選取工具處理。")))

        t.Add(ManualBlock.Heading(Lang.T("合併圖層")))
        t.Add(ManualBlock.Bullets(
            Lang.T("合併向下：選取的圖層和正下方的圖層合成一個點陣圖層。"),
            Lang.T("合併可見：所有顯示中的圖層合成一個點陣圖層，隱藏的不動。"),
            Lang.T("合併後文字、形狀就不能個別修改了（但可以整體移動、繼續畫或擦）；不確定的話先另存一份，或按復原還原。")))
        t.Add(ManualBlock.Note(Lang.T("存成 .pedx 會保留每一個圖層，下次打開還能繼續改；匯出 PNG／JPG 時才把所有顯示中的圖層合成一張。")))
        Return t
    End Function

    ''' <summary>圖層示意：斜放的透明片由下往上疊。</summary>
    Private Shared Function LayerStackSwatch() As Bitmap
        Const W As Integer = 320, H As Integer = 190
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
            g.Clear(Color.FromArgb(252, 251, 248))
            Dim names = {Lang.T("照片（含調色）"), Lang.T("點陣圖層"), Lang.T("向量圖形"), Lang.T("文字"), Lang.T("貼圖")}
            Dim fills = {Color.FromArgb(235, 120, 170, 210), Color.FromArgb(150, 240, 200, 120), Color.FromArgb(150, 120, 200, 140),
                         Color.FromArgb(150, 230, 150, 150), Color.FromArgb(150, 190, 160, 230)}
            Using f As New Font("Microsoft JhengHei UI", 9.0F), pen As New Pen(Color.FromArgb(90, 100, 120))
                For i = 0 To names.Length - 1
                    Dim y = H - 40 - i * 30
                    Dim pts = {New PointF(20, y + 22), New PointF(150, y + 22), New PointF(190, y), New PointF(60, y)}
                    Using br As New SolidBrush(fills(i))
                        g.FillPolygon(br, pts)
                    End Using
                    g.DrawPolygon(pen, pts)
                    If i = 1 Then g.DrawBezier(New Pen(Color.FromArgb(60, 60, 60), 2), 70, y + 15, 95, y + 2, 120, y + 22, 150, y + 6)
                    If i = 2 Then g.DrawEllipse(New Pen(Color.FromArgb(40, 110, 60), 2), 95, y + 3, 40, 15)
                    If i = 3 Then g.DrawString("Aa", f, Brushes.DarkRed, 100, y + 3)
                    If i = 4 Then g.FillEllipse(Brushes.MediumPurple, 115, y + 5, 12, 12)
                    g.DrawLine(Pens.Silver, 192, y + 11, 210, y + 11)
                    g.DrawString(names(i), f, Brushes.DimGray, 214, y + 3)
                Next
                g.DrawString(Lang.T("↑ 上面的蓋住下面的"), f, Brushes.SteelBlue, 20, 6)
            End Using
            Using p As New Pen(Color.FromArgb(214, 218, 226))
                g.DrawRectangle(p, 0, 0, W - 1, H - 1)
            End Using
        End Using
        Return bmp
    End Function

    '---------------------------------------------------------------- 結合與打散

    Private Shared Function BuildCombineConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("向量物件的結合與打散"))
        t.Add(ManualBlock.Para(Lang.T("和 CorelDRAW 一樣，PhotoEdit 有「結合」（Ctrl+L）與「打散」（Ctrl+K）：把幾個向量圖形放進同一個圖形、重疊的地方變成洞，之後隨時可以再拆開。另外還有群組、焊接、修剪、形狀建立、刀具等組合與切開的方法，各有用途。")))

        t.Add(ManualBlock.Heading(Lang.T("結合與打散（Ctrl+L／Ctrl+K）")))
        Dim cb As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(150, 100)}
        cb.Tiles.Add(New ManualTile With {.Image = ShapeOpSwatch(Nothing), .Title = Lang.T("原本"), .Text = Lang.T("正方形在下、圓形在上，兩個都選取。")})
        cb.Tiles.Add(New ManualTile With {.Image = CombineSwatch(False), .Title = Lang.T("結合"), .Text = Lang.T("變成一個圖形（樣式用最下面的），重疊的地方是洞。")})
        cb.Tiles.Add(New ManualTile With {.Image = CombineSwatch(True), .Title = Lang.T("打散"), .Text = Lang.T("每條路徑變回一個圖形；洞那塊變成實心的。")})
        t.Add(cb)
        t.Add(ManualBlock.Steps(
            Lang.T("用選取工具選好兩個以上的向量圖形。"),
            Lang.T("按 Ctrl+L，或畫布上方工具列的「結合」：變成一個圖形，樣式（顏色、外框）用最下面那個，重疊的地方挖空。"),
            Lang.T("要拆開：選結合過的圖形按 Ctrl+K，或「打散」，每條路徑變回一個圖形，樣式不變。")))
        t.Add(ManualBlock.Bullets(
            Lang.T("做有洞的形狀：大圓上放小圓，結合 → 甜甜圈；方塊上放轉成曲線的字，結合 → 鏤空字。"),
            Lang.T("和焊接不同：結合時每條路徑的節點原封不動，只是放進同一個圖形；焊接會把外形合成一條新的外框，打散也拆不回原本的圖形。"),
            Lang.T("文字先「轉成曲線」才能結合；圖說、點陣圖層、填滿圖層不能結合，鎖住的圖形也不行。"),
            Lang.T("轉成曲線的字、焊接或排除後有洞的圖形，都可以打散成一塊一塊。"),
            Lang.T("沒有選到可以結合或打散的圖形時，Ctrl+L 和 Ctrl+K 照舊是「向左旋轉」與「裁切」。")))
        t.Add(ManualBlock.Note(Lang.T("先記得一個差別：群組只是綁在一起，裡面每個圖形都還在；結合是放進同一個圖形，打散可以拆回來；焊接這類合併會產生一個新的鋼筆路徑，原本的圖形就不見了（可以按復原，或合併前先複製一份）。")))

        t.Add(ManualBlock.Heading(Lang.T("群組與解散群組")))
        t.Add(ManualBlock.Steps(
            Lang.T("用選取工具框選，或 Shift／Ctrl＋點選，選好幾個圖形。"),
            Lang.T("按 Ctrl+G（或繪圖頁圖層清單的右鍵 →「群組」）。之後點一下就選到整個群組，一起移動、縮放、旋轉、隱藏、鎖定。"),
            Lang.T("要改群組裡的某個圖形：在群組上按兩下進去編輯，改完按 Esc 或點群組外面離開。"),
            Lang.T("要拆開：選群組按 Ctrl+Shift+G（或右鍵 →「解散」），裡面的圖形回到上一層，位置不變。")))
        t.Add(ManualBlock.Para(Lang.T("在圖層清單把一列拖到群組上也能放進群組；「新增 → 新增群組」先開一個空群組，接著畫的圖形都會放進去。")))

        t.Add(ManualBlock.Heading(Lang.T("合成一個外形：焊接、修剪、相交、排除")))
        t.Add(ManualBlock.Para(Lang.T("選兩個以上的封閉向量圖形，畫布上方的工具列會出現這四個按鈕。下圖是藍色正方形（在下面）和橘色圓形（在上面）分別按下去的結果：")))
        Dim ops As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(150, 100)}
        ops.Tiles.Add(New ManualTile With {.Image = ShapeOpSwatch(Nothing), .Title = Lang.T("原本"), .Text = Lang.T("正方形在下、圓形在上，兩個都選取。")})
        ops.Tiles.Add(New ManualTile With {.Image = ShapeOpSwatch(ShapeOp.Weld), .Title = Lang.T("焊接"), .Text = Lang.T("合成一個外形，樣式用最上面的。")})
        ops.Tiles.Add(New ManualTile With {.Image = ShapeOpSwatch(ShapeOp.Trim), .Title = Lang.T("修剪"), .Text = Lang.T("最下面的減掉上面所有的，像餅乾模切掉一塊；樣式用最下面的。")})
        ops.Tiles.Add(New ManualTile With {.Image = ShapeOpSwatch(ShapeOp.Intersect), .Title = Lang.T("相交"), .Text = Lang.T("只留下全部重疊的地方。")})
        ops.Tiles.Add(New ManualTile With {.Image = ShapeOpSwatch(ShapeOp.Exclude), .Title = Lang.T("排除"), .Text = Lang.T("重疊的地方挖空，其餘合成一個。")})
        t.Add(ops)
        t.Add(ManualBlock.Bullets(
            Lang.T("結果都是鋼筆路徑，可以用節點工具繼續修改；原本的圓角、角數、黃點等設定就不能再調了。"),
            Lang.T("圖說、點陣圖層、填滿圖層與開放的線不能合併；線條要先用「外框轉物件」變成封閉圖形。"),
            Lang.T("文字要先「轉成曲線」，變成每個字一個鋼筆路徑，才能和其他圖形焊接或修剪（例如在色塊上挖出字）。")))

        t.Add(ManualBlock.Heading(Lang.T("形狀建立：挑幾塊合成、刪掉不要的")))
        t.Add(ManualBlock.Steps(
            Lang.T("選取重疊的封閉圖形，按左側工具列的「形狀建立」。"),
            Lang.T("圖形重疊的地方會切成一塊一塊；在幾塊上拖曳，就把它們合成一塊。"),
            Lang.T("按住 Alt 點（或拖）把不要的那幾塊刪掉。"),
            Lang.T("按 Esc、Enter 或點外面結束。")))
        t.Add(ManualBlock.Para(Lang.T("比焊接、修剪更自由：想留哪幾塊、合哪幾塊，直接在畫布上挑（像 Illustrator 的形狀建立工具）。")))

        t.Add(ManualBlock.Heading(Lang.T("打散：切開、斷開、拆成一個個字")))
        t.Add(ManualBlock.Bullets(
            Lang.T("刀具：拖一條線穿過圖形把它切開（Shift：45°）；有選取時只切選取的。封閉圖形切成好幾塊、各自獨立，開放的線在交點斷開。"),
            Lang.T("節點的「斷開」：用節點工具選一個節點按「斷開」，封閉路徑變開放、開放路徑分成兩條；「接合」則把兩條開放路徑接回一條。"),
            Lang.T("轉成曲線：文字、圖說的字變成外框，每個字一個鋼筆路徑，可以各自移動、改色、變形。"),
            Lang.T("形狀建立按住 Alt 刪塊：把重疊處切開後，只留下需要的那幾塊。"),
            Lang.T("取出內容：用「放進形狀」（PowerClip）放進去的東西，按「取出內容」拿回來。")))

        t.Add(ManualBlock.Heading(Lang.T("小技巧")))
        t.Add(ManualBlock.Bullets(
            Lang.T("合併前先用圖層清單的「複製」留一份（再把它隱藏），之後想改原本的圖形還找得到。"),
            Lang.T("要整組套同一個效果、一起對齊，用群組就好，不必焊接。"),
            Lang.T("做 Logo、剪影：先用基本圖形拼出來，再焊接成一個外形，最後用節點工具修細節。"),
            Lang.T("在照片上挖出形狀：畫好形狀後用「照片放進形狀」，照片只留形狀裡面。")))
        Return t
    End Function

    ''' <summary>結合與打散的示意：同 ShapeOpSwatch 的正方形與圓形，broken＝結合後再打散。</summary>
    Private Shared Function CombineSwatch(broken As Boolean) As Bitmap
        Const W As Integer = 150, H As Integer = 100
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.FromArgb(252, 251, 248))
        End Using
        Dim edge = Color.FromArgb(50, 60, 80).ToArgb()
        Dim square As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.6, .Y = 0.5, .W = 0.62, .H = 0.62, .Filled = True, .Stroked = True,
                                          .FillColorArgb = Color.FromArgb(90, 140, 210).ToArgb(), .StrokeColorArgb = edge, .StrokeWidth = 0.012, .Seed = 1}
        Dim circle As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.9, .Y = 0.5, .W = 0.62, .H = 0.62, .Filled = True, .Stroked = True,
                                          .FillColorArgb = Color.FromArgb(245, 160, 70).ToArgb(), .StrokeColorArgb = edge, .StrokeWidth = 0.012, .Seed = 2}
        Dim combined = CombineBreak.Combine(New List(Of DrawLayer) From {square, circle})
        Dim layers As New List(Of DrawLayer)()
        If combined IsNot Nothing Then
            If broken Then
                Dim parts = CombineBreak.BreakApart(combined)
                If parts IsNot Nothing Then
                    ' 拆開的兩塊稍微錯開，看得出是兩個圖形
                    DrawGeometry.Offset(parts(parts.Count - 1), 0.08, 0)
                    layers.AddRange(parts)
                End If
            Else
                layers.Add(combined)
            End If
        End If
        DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = layers})
        Using g = Graphics.FromImage(bmp), p As New Pen(Color.FromArgb(214, 218, 226))
            g.DrawRectangle(p, 0, 0, W - 1, H - 1)
        End Using
        Return bmp
    End Function

    ''' <summary>焊接／修剪／相交／排除的示意：藍色正方形在下、橘色圓形在上；op 為 Nothing 時畫原本的兩個圖形。</summary>
    Private Shared Function ShapeOpSwatch(op As ShapeOp?) As Bitmap
        Const W As Integer = 150, H As Integer = 100
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.FromArgb(252, 251, 248))
        End Using
        Dim edge = Color.FromArgb(50, 60, 80).ToArgb()
        Dim square As New DrawLayer With {.Shape = DrawShape.Rectangle, .X = 0.6, .Y = 0.5, .W = 0.62, .H = 0.62, .Filled = True, .Stroked = True,
                                          .FillColorArgb = Color.FromArgb(90, 140, 210).ToArgb(), .StrokeColorArgb = edge, .StrokeWidth = 0.012, .Seed = 1}
        Dim circle As New DrawLayer With {.Shape = DrawShape.Ellipse, .X = 0.9, .Y = 0.5, .W = 0.62, .H = 0.62, .Filled = True, .Stroked = True,
                                          .FillColorArgb = Color.FromArgb(245, 160, 70).ToArgb(), .StrokeColorArgb = edge, .StrokeWidth = 0.012, .Seed = 2}
        Dim layers As New List(Of DrawLayer)()
        If op.HasValue Then
            Dim result As DrawLayer = Nothing
            Try
                result = PathBoolean.Apply(New List(Of DrawLayer) From {square, circle}, op.Value)
            Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            End Try
            If result IsNot Nothing Then layers.Add(result)
            ' 原本的位置用淡虛線標出來
            Using g = Graphics.FromImage(bmp), p As New Pen(Color.FromArgb(150, 160, 175)) With {.DashStyle = Drawing2D.DashStyle.Dash}
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                g.DrawRectangle(p, CSng((0.6 - 0.31) * H), CSng((0.5 - 0.31) * H), CSng(0.62 * H), CSng(0.62 * H))
                g.DrawEllipse(p, CSng((0.9 - 0.31) * H), CSng((0.5 - 0.31) * H), CSng(0.62 * H), CSng(0.62 * H))
            End Using
        Else
            layers.Add(square)
            layers.Add(circle)
        End If
        DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = layers})
        Using g = Graphics.FromImage(bmp), p As New Pen(Color.FromArgb(214, 218, 226))
            g.DrawRectangle(p, 0, 0, W - 1, H - 1)
        End Using
        Return bmp
    End Function
End Class
