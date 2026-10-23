Imports PhotoEdit

' 使用手冊「觀念與技巧」（續）：參考圖、輔助工具、線稿上色、鋼筆、明暗、存檔匯出、快速操作、紙紋。
Partial Class frmEditor

    '---------------------------------------------------------------- 參考圖與描圖

    Private Shared Function BuildReferenceConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("參考圖與描圖"))
        t.Add(ManualBlock.Para(Lang.T("畫畫時看著參考圖畫，是最快進步的方法。PhotoEdit 有一個浮動的參考圖視窗，可以放在畫布旁邊對照、吸色，也能半透明疊在畫布上描。")))
        t.Add(ManualBlock.Heading(Lang.T("打開參考圖")))
        t.Add(ManualBlock.Steps(
            Lang.T("「檢視 → 參考圖…」（Ctrl+Shift+R），按「開啟圖片…」，或直接把圖片拖進參考圖視窗。"),
            Lang.T("把視窗拖到畫布旁邊；滾輪縮放、拖曳移動、按兩下看全圖。")))
        t.Add(ManualBlock.Heading(Lang.T("參考圖視窗的按鈕")))
        t.Add(ManualBlock.Bullets(
            Lang.T("點一下吸色：左鍵設成線條色、右鍵設成填色。"),
            Lang.T("描圖：把參考圖半透明疊在畫布上，對著描輪廓。只顯示在畫面上，不會存進文件或匯出。"),
            Lang.T("畫筆取色：每一筆自動用參考圖同一個位置的顏色，臨摹名畫、照片上色很方便。"),
            Lang.T("墨色：吸到的顏色換成同樣深淺的灰（墨色），參考圖也改成灰階顯示，畫水墨或素描時看明暗用。")))
        t.Add(ManualBlock.Heading(Lang.T("臨摹的步驟")))
        t.Add(ManualBlock.Steps(
            Lang.T("新增和參考圖同比例的畫布（「檔案 → 新增…」）。"),
            Lang.T("開「描圖」，在「草稿」圖層用淺色鉛筆描出大輪廓，描完關掉「描圖」。"),
            Lang.T("看著參考圖畫線稿與上色；需要顏色時在參考圖上點一下吸色。"),
            Lang.T("不時用「檢視 → 左右翻轉檢視」看一下，畫歪的地方很明顯。")))
        t.Add(ManualBlock.Note(Lang.T("描圖適合抓比例，但不要整張都描——自己看著畫，觀察力才會進步。用別人的圖臨摹練習沒問題，公開發表時要註明原作。")))
        Return t
    End Function

    '---------------------------------------------------------------- 輔助工具

    Private Shared Function BuildGuidesConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("尺規、格線與對稱"))
        t.Add(ManualBlock.Para(Lang.T("讓線條畫直、位置對齊、左右一樣的輔助工具。它們只顯示在畫面上，不會畫進作品。")))
        t.Add(ManualBlock.Heading(Lang.T("尺規與參考線")))
        t.Add(ManualBlock.Bullets(
            Lang.T("「檢視 → 顯示尺規」（Ctrl+Alt+R）：畫面上方與左邊出現尺規。"),
            Lang.T("從尺規往畫面裡拖，就拉出一條參考線（水平或垂直），例如標出地平線、臉的中線、版面邊界。"),
            Lang.T("「檢視 → 貼齊參考線」打開後，移動或畫圖形時會吸附到參考線上。"),
            Lang.T("「檢視 → 清除參考線」一次移除全部。")))
        t.Add(ManualBlock.Heading(Lang.T("格線")))
        t.Add(ManualBlock.Bullets(
            Lang.T("「檢視 → 顯示格線」（Ctrl+'）：畫面鋪上格子，抓比例、透視、排版都好用。"),
            Lang.T("「檢視 → 格線間距…」設定格子大小；「貼齊格線」讓圖形對齊格子。"),
            Lang.T("臨摹的老方法：參考圖和畫布都分成一樣的格子，一格一格對著畫，比例就不會跑掉。")))
        t.Add(ManualBlock.Heading(Lang.T("對稱繪圖")))
        t.Add(ManualBlock.Para(Lang.T("在繪圖頁籤「進階」裡選對稱方式，畫一筆會同時畫出對稱的複本：")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(120, 120)}
        g.Tiles.Add(New ManualTile With {.Image = SymmetrySwatch(SymmetryKind.MirrorX), .Title = Lang.T("左右"), .Text = Lang.T("畫臉、蝴蝶、建築正面。")})
        g.Tiles.Add(New ManualTile With {.Image = SymmetrySwatch(SymmetryKind.MirrorXY), .Title = Lang.T("上下左右"), .Text = Lang.T("四向對稱的花紋、徽章。")})
        g.Tiles.Add(New ManualTile With {.Image = SymmetrySwatch(SymmetryKind.Rotate), .Title = Lang.T("旋轉 6 等分"), .Text = Lang.T("花朵、齒輪、放射圖案。")})
        g.Tiles.Add(New ManualTile With {.Image = SymmetrySwatch(SymmetryKind.Kaleido), .Title = Lang.T("萬花筒 6 等分"), .Text = Lang.T("每份再鏡射，畫曼陀羅、雪花。")})
        t.Add(g)
        t.Add(ManualBlock.Para(Lang.T("每格都只畫了一筆。旋轉與萬花筒的份數用「等分」調整；形狀、特效筆、粒子筆也都會跟著對稱。")))
        t.Add(ManualBlock.Heading(Lang.T("畫直線的小技巧")))
        t.Add(ManualBlock.Bullets(
            Lang.T("要完全筆直：用直線工具，Shift 鎖住 45° 的角度。"),
            Lang.T("長弧線手抖：打開穩定器（「進階」或 F9 快速面板）。"),
            Lang.T("畫形狀時按住 Shift 等比例（正方形、正圓）。")))
        Return t
    End Function

    ''' <summary>對稱示意：同一筆曲線，套用不同的對稱方式。</summary>
    Private Shared Function SymmetrySwatch(kind As SymmetryKind) As Bitmap
        Const S As Integer = 120
        Dim bmp As New Bitmap(S, S)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.FromArgb(252, 251, 248))
            Using p As New Pen(Color.FromArgb(200, 210, 225)) With {.DashStyle = Drawing2D.DashStyle.Dot}
                g.DrawLine(p, S \ 2, 0, S \ 2, S)
                If kind <> SymmetryKind.MirrorX Then g.DrawLine(p, 0, S \ 2, S, S \ 2)
            End Using
        End Using
        Dim st As New DrawStroke()
        For i = 0 To 30
            Dim k = i / 30.0F
            st.Points.Add(New DrawPoint(0.56F + k * 0.3F, 0.22F + CSng(Math.Sin(k * Math.PI * 1.4)) * 0.12F + k * 0.18F, 0.5F + 0.5F * CSng(Math.Sin(k * Math.PI))))
        Next
        Dim d As New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.HardRound, .StrokeWidth = 0.035, .Seed = 3,
                                     .StrokeColorArgb = Color.FromArgb(200, 60, 120).ToArgb(), .Symmetry = kind, .SymCount = 6, .SymX = 0.5, .SymY = 0.5,
                                     .Strokes = New List(Of DrawStroke) From {st}}
        DrawingRenderer.DrawLayers(bmp, New EditRecipe With {.Drawings = New List(Of DrawLayer) From {d}})
        Using g = Graphics.FromImage(bmp), p As New Pen(Color.FromArgb(214, 218, 226))
            g.DrawRectangle(p, 0, 0, S - 1, S - 1)
        End Using
        Return bmp
    End Function

    '---------------------------------------------------------------- 線稿上色

    Private Shared Function BuildColoringConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("線稿上色"))
        t.Add(ManualBlock.Para(Lang.T("漫畫、插畫最常見的畫法：先有乾淨的線稿，再在線稿下面填顏色。重點是線稿和顏色分開放在不同圖層。")))
        t.Add(ManualBlock.Heading(Lang.T("圖層怎麼排")))
        t.Add(ManualBlock.Bullets(
            Lang.T("最上面：線稿。"),
            Lang.T("中間：陰影、亮部（可以用「色彩增值」「濾色」混合模式）。"),
            Lang.T("下面：底色（各部位可以各一層）。"),
            Lang.T("線稿是掃描或拍照的黑白手繪稿時，把它的混合模式設成「色彩增值」，白色就變透明，只剩下線。")))
        t.Add(ManualBlock.Heading(Lang.T("用油漆桶填底色")))
        t.Add(ManualBlock.Steps(
            Lang.T("選「上色」圖層（在線稿下面），左側工具列選油漆桶，線條色設成要填的顏色。"),
            Lang.T("畫布上方的選項把「取樣」設成「所有圖層」：油漆桶才看得到在別的圖層的線稿。"),
            Lang.T("在要填的區域裡點一下。")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(150, 110)}
        g.Tiles.Add(New ManualTile With {.Image = BucketSwatch(0), .Title = Lang.T("線稿封閉"), .Text = Lang.T("顏色剛好填在線裡面。")})
        g.Tiles.Add(New ManualTile With {.Image = BucketSwatch(1), .Title = Lang.T("線稿有缺口"), .Text = Lang.T("顏色從缺口漏出去，填滿整片背景。")})
        g.Tiles.Add(New ManualTile With {.Image = BucketSwatch(2), .Title = Lang.T("調高「補缺口」"), .Text = Lang.T("小缺口當作封閉，不會漏出去。")})
        t.Add(g)
        t.Add(ManualBlock.Heading(Lang.T("油漆桶的選項")))
        t.Add(ManualBlock.Bullets(
            Lang.T("容許度：顏色差多少以內算同一區；照片或漸層上要調高。"),
            Lang.T("補缺口：線稿有小缺口時當作封閉；數字越大能補的缺口越寬。"),
            Lang.T("擴張：填色往外多長一點，蓋住線稿邊緣半透明的像素，不會留下一圈白邊。"),
            Lang.T("相鄰：只填和點下去相連的區域；取消時整張圖相近的顏色都會填。")))
        t.Add(ManualBlock.Note(Lang.T("油漆桶填不到的細縫、頭髮尖端，用筆刷補塗就好。顏色畫出線外也沒關係，因為底色在線稿下面，會被線蓋住。")))
        Return t
    End Function

    ''' <summary>油漆桶示意：0 線稿封閉、1 有缺口漏出去、2 補缺口。</summary>
    Private Shared Function BucketSwatch(kind As Integer) As Bitmap
        Const W As Integer = 150, H As Integer = 110
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.Clear(Color.FromArgb(252, 251, 248))
            Dim fill = Color.FromArgb(120, 190, 240)
            If kind = 1 Then
                Using br As New SolidBrush(fill)
                    g.FillRectangle(br, 1, 1, W - 2, H - 2)
                End Using
            Else
                Using br As New SolidBrush(fill)
                    g.FillEllipse(br, 35, 15, 80, 80)
                End Using
            End If
            Using p As New Pen(Color.FromArgb(30, 30, 30), 3)
                If kind = 0 Then
                    g.DrawEllipse(p, 35, 15, 80, 80)
                Else
                    g.DrawArc(p, 35, 15, 80, 80, -60, 330) ' 右上留一個缺口
                End If
            End Using
            If kind = 2 Then
                Using p As New Pen(Color.FromArgb(220, 60, 60), 1.5F) With {.DashStyle = Drawing2D.DashStyle.Dash}
                    g.DrawArc(p, 35, 15, 80, 80, -90, 30)
                End Using
            End If
            Using p As New Pen(Color.FromArgb(214, 218, 226))
                g.DrawRectangle(p, 0, 0, W - 1, H - 1)
            End Using
        End Using
        Return bmp
    End Function

    '---------------------------------------------------------------- 鋼筆

    Private Shared Function BuildPenConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("鋼筆工具與貝茲曲線入門"))
        t.Add(ManualBlock.Para(Lang.T("鋼筆工具用「節點」和「把手」畫出非常平順的線，適合 Logo、圖示、需要很整齊的輪廓。一開始會覺得不順手，抓到訣竅後比手繪還快。")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(160, 110)}
        g.Tiles.Add(New ManualTile With {.Image = BezierSwatch(0), .Title = Lang.T("點一下＝尖角"), .Text = Lang.T("每點一下加一個節點，節點之間是直線。")})
        g.Tiles.Add(New ManualTile With {.Image = BezierSwatch(1), .Title = Lang.T("按住拖曳＝曲線"), .Text = Lang.T("拖出藍色把手，把手的方向與長度決定曲線怎麼彎。")})
        g.Tiles.Add(New ManualTile With {.Image = BezierSwatch(2), .Title = Lang.T("節點越少越順"), .Text = Lang.T("一段弧線用兩三個節點就夠，節點太多線會抖。")})
        t.Add(g)
        t.Add(ManualBlock.Heading(Lang.T("畫一條路徑")))
        t.Add(ManualBlock.Steps(
            Lang.T("左側工具列選「鋼筆」。"),
            Lang.T("點一下放第一個節點；直線就點下一個位置，曲線就在下一個位置按住往前拖。"),
            Lang.T("要封閉成可以填色的形狀：點回第一個節點。"),
            Lang.T("不封閉：按兩下、Enter 或 Esc 結束。")))
        t.Add(ManualBlock.Heading(Lang.T("畫好後用節點工具修")))
        t.Add(ManualBlock.Bullets(
            Lang.T("拖曳節點移動位置，拖曳藍色把手改彎曲。"),
            Lang.T("按兩下路徑加一個節點，按兩下節點刪掉它。"),
            Lang.T("Alt＋拖曳把手只動一邊，平滑的節點會變成尖角。"),
            Lang.T("上方的按鈕可以把節點改成尖角、平滑、對稱，或斷開、接合路徑。"),
            Lang.T("矩形、星形這些形狀按「轉成曲線」後，也能用節點工具任意改。")))
        t.Add(ManualBlock.Heading(Lang.T("訣竅")))
        t.Add(ManualBlock.Bullets(
            Lang.T("節點放在曲線轉彎最多的地方（弧的兩端），不要放在彎的正中間。"),
            Lang.T("把手大約拉到下一段長度的三分之一，曲線最自然。"),
            Lang.T("把手方向順著曲線前進的方向；兩邊把手一直線，曲線經過節點時才不會折角。"),
            Lang.T("先放少一點節點，畫完再加；比一開始放太多再刪容易。")))
        Return t
    End Function

    ''' <summary>貝茲示意：0 尖角折線、1 有把手的曲線、2 節點多（抖）與少（順）的比較。</summary>
    Private Shared Function BezierSwatch(kind As Integer) As Bitmap
        Const W As Integer = 160, H As Integer = 110
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.Clear(Color.FromArgb(252, 251, 248))
            Dim node = Sub(x As Single, y As Single)
                           g.FillRectangle(Brushes.White, x - 3.5F, y - 3.5F, 7, 7)
                           g.DrawRectangle(Pens.Black, x - 3.5F, y - 3.5F, 7, 7)
                       End Sub
            Dim handle = Sub(x0 As Single, y0 As Single, x1 As Single, y1 As Single)
                             Using p As New Pen(Color.FromArgb(60, 130, 230))
                                 g.DrawLine(p, x0, y0, x1, y1)
                             End Using
                             g.FillEllipse(New SolidBrush(Color.FromArgb(60, 130, 230)), x1 - 3, y1 - 3, 6, 6)
                         End Sub
            Using line As New Pen(Color.FromArgb(40, 40, 40), 2)
                Select Case kind
                    Case 0
                        Dim pts = {New PointF(20, 85), New PointF(60, 25), New PointF(100, 85), New PointF(140, 25)}
                        g.DrawLines(line, pts)
                        For Each p In pts : node(p.X, p.Y) : Next
                    Case 1
                        g.DrawBezier(line, 20, 80, 50, 15, 110, 15, 140, 80)
                        handle(20, 80, 50, 15) : handle(140, 80, 110, 15)
                        node(20, 80) : node(140, 80)
                    Case Else
                        Dim rnd As New Random(2)
                        Dim jag As New List(Of PointF)()
                        For i = 0 To 14
                            Dim a = i / 14.0
                            jag.Add(New PointF(CSng(15 + a * 60), CSng(70 - Math.Sin(a * Math.PI) * 40 + rnd.NextDouble() * 6 - 3)))
                        Next
                        g.DrawCurve(line, jag.ToArray(), 0.5F)
                        For Each p In jag : node(p.X, p.Y) : Next
                        g.DrawBezier(line, 85, 70, 95, 15, 135, 15, 145, 70)
                        node(85, 70) : node(145, 70)
                End Select
            End Using
            Using p As New Pen(Color.FromArgb(214, 218, 226))
                g.DrawRectangle(p, 0, 0, W - 1, H - 1)
            End Using
        End Using
        Return bmp
    End Function

    '---------------------------------------------------------------- 明暗

    Private Shared Function BuildLightConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("明暗與光影"))
        t.Add(ManualBlock.Para(Lang.T("有明暗，平平的圖才會變立體。先決定光從哪裡來，其他的位置就跟著決定了。")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(320, 200)}
        g.Tiles.Add(New ManualTile With {.Image = LightSwatch(), .Title = Lang.T("一顆球的五個明暗"),
                                         .Text = Lang.T("光從左上來：亮點、亮面、明暗交界（最暗的帶）、反光（暗面邊緣被地面照亮）、投影。")})
        t.Add(g)
        t.Add(ManualBlock.Heading(Lang.T("步驟")))
        t.Add(ManualBlock.Steps(
            Lang.T("決定光源方向，可以在畫面角落畫一個小箭頭提醒自己（畫在草稿層）。"),
            Lang.T("在底色上面開「陰影」圖層：背光的那一側整片塗上陰影色。"),
            Lang.T("明暗交界最暗、往暗面邊緣稍微變亮（反光），物體才有圓的感覺。"),
            Lang.T("開「亮部」圖層，在最靠近光的地方加亮面，最後點一個小亮點。"),
            Lang.T("物體下面加投影：越靠近物體越深、越清楚，越遠越淡越模糊。")))
        t.Add(ManualBlock.Heading(Lang.T("在 PhotoEdit 裡怎麼畫")))
        t.Add(ManualBlock.Bullets(
            Lang.T("柔和的陰影：軟筆或噴槍、柔邊調高、流量調低，輕輕刷幾次。"),
            Lang.T("邊緣清楚的陰影（動畫上色）：硬筆，或用鋼筆畫出陰影形狀再填色。"),
            Lang.T("陰影圖層用「色彩增值」、亮部圖層用「濾色」或「覆蓋」，顏色會自然地壓在底色上，改底色也不用重畫陰影。"),
            Lang.T("大面積的明暗變化：漸層工具（起點是線條色、終點是填色）。"),
            Lang.T("陰影不要只用黑色：偏冷（藍紫）會比較乾淨，見「顏色怎麼選」。")))
        t.Add(ManualBlock.Note(Lang.T("檢查明暗對不對：瞇著眼睛看，或暫時把飽和度拉到最低看黑白——亮的、暗的地方有沒有分開。")))
        Return t
    End Function

    ''' <summary>明暗示意：球、光源箭頭、五個明暗的標示。</summary>
    Private Shared Function LightSwatch() As Bitmap
        Const W As Integer = 320, H As Integer = 200
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
            g.Clear(Color.FromArgb(252, 251, 248))
            ' 投影
            Using path As New Drawing2D.GraphicsPath()
                path.AddEllipse(95, 150, 150, 34)
                Using pb As New Drawing2D.PathGradientBrush(path) With {.CenterPoint = New PointF(160, 165), .CenterColor = Color.FromArgb(150, 40, 40, 60),
                                                                      .SurroundColors = {Color.FromArgb(0, 40, 40, 60)}}
                    g.FillPath(pb, path)
                End Using
            End Using
            ' 球：明暗交界在右下，邊緣有反光
            Dim cx = 130.0F, cy = 95.0F, r = 62.0F
            Using path As New Drawing2D.GraphicsPath()
                path.AddEllipse(cx - r, cy - r, r * 2, r * 2)
                Using pb As New Drawing2D.PathGradientBrush(path) With {.CenterPoint = New PointF(cx - 24, cy - 26)}
                    Dim blend As New Drawing2D.ColorBlend(5)
                    blend.Colors = {Color.FromArgb(150, 120, 170), Color.FromArgb(70, 50, 90), Color.FromArgb(150, 130, 200), Color.FromArgb(210, 200, 240), Color.FromArgb(245, 240, 255)}
                    blend.Positions = {0.0F, 0.12F, 0.4F, 0.75F, 1.0F}
                    pb.InterpolationColors = blend
                    g.FillPath(pb, path)
                End Using
            End Using
            g.FillEllipse(Brushes.White, cx - 34, cy - 36, 12, 10)
            ' 光源
            Using p As New Pen(Color.FromArgb(230, 170, 40), 3) With {.CustomEndCap = New Drawing2D.AdjustableArrowCap(4, 5)}
                g.DrawLine(p, 18, 14, 52, 42)
            End Using
            Using f As New Font("Microsoft JhengHei UI", 9.0F), lp As New Pen(Color.FromArgb(150, 150, 160))
                g.DrawString(Lang.T("光源"), f, Brushes.DarkGoldenrod, 8, 44)
                Dim label = Sub(text As String, x0 As Single, y0 As Single, x1 As Single, y1 As Single)
                                g.DrawLine(lp, x0, y0, x1, y1)
                                g.DrawString(text, f, Brushes.DimGray, x1 + 3, y1 - 8)
                            End Sub
                label(Lang.T("亮點"), cx - 28, cy - 31, 230, 22)
                label(Lang.T("亮面"), cx - 5, cy - 15, 230, 52)
                label(Lang.T("明暗交界"), cx + 33, cy + 33, 230, 88)
                label(Lang.T("反光"), cx + 42, cy + 42, 230, 118)
                label(Lang.T("投影"), 210, 170, 230, 172)
            End Using
            Using p As New Pen(Color.FromArgb(214, 218, 226))
                g.DrawRectangle(p, 0, 0, W - 1, H - 1)
            End Using
        End Using
        Return bmp
    End Function

    '---------------------------------------------------------------- 存檔與匯出

    Private Shared Function BuildExportConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("存檔、匯出與畫布大小"))
        t.Add(ManualBlock.Heading(Lang.T("檔案格式怎麼選")))
        t.Add(ManualBlock.Bullets(
            Lang.T(".pedx（「存檔」Ctrl+S）：PhotoEdit 的專案檔，保留所有圖層、調整、文字與向量圖形，下次可以繼續改。作品一定要留一份。"),
            Lang.T("PNG（「匯出…」Ctrl+E）：不失真、可以保留透明背景。插畫、貼圖、去背、要再加工的圖用它。"),
            Lang.T("JPG：檔案小，適合照片類的圖上傳分享；不支援透明，存很多次畫質會變差。"),
            Lang.T("BMP：不壓縮的舊格式，檔案很大，只有特別需要時才用。"),
            Lang.T("SVG（「檔案 → 匯出 SVG（向量圖形）…」）：只匯出向量圖形，可以在其他向量軟體或網頁裡無限放大，適合 Logo、圖示。")))
        t.Add(ManualBlock.Note(Lang.T("匯出的 PNG／JPG 是所有顯示中的圖層合成的一張圖，不能再分圖層修改；隱藏的圖層不會出現在匯出檔裡。")))
        t.Add(ManualBlock.Heading(Lang.T("畫布要開多大")))
        t.Add(ManualBlock.Para(Lang.T("「檔案 → 新增…」可以選常用的大小、直向或橫向、底色（白色、透明、紙張），以及解析度。")))
        t.Add(ManualBlock.Bullets(
            Lang.T("只在螢幕上看、上傳社群：長邊 2000～3000 像素就很夠。"),
            Lang.T("要列印：選列印用的版面（相片、證件照、紙張大小），解析度選 300（相片列印）或 350（印刷），程式會換算成像素。"),
            Lang.T("寧可開大一點：大圖縮小沒問題，小圖放大會糊。但太大會畫得比較慢，一般插畫 3000～4000 像素是好的起點。"),
            Lang.T("向量圖形、文字放大匯出一樣清楚；點陣筆觸以畫的大小為準。")))
        t.Add(ManualBlock.Heading(Lang.T("透明背景")))
        t.Add(ManualBlock.Bullets(
            Lang.T("新增時底色選「透明」，或用「去背」頁籤把背景變透明。"),
            Lang.T("要保留透明一定要存 PNG；存成 JPG 透明的地方會變成白色。")))
        Return t
    End Function

    '---------------------------------------------------------------- 快速操作

    Private Function BuildQuickConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("快速面板、圓形選單與快捷鍵"))
        t.Add(ManualBlock.Para(Lang.T("畫圖時一直把筆移到畫面右邊換設定，很打斷思緒。下面這些方法讓手留在畫布上。")))
        t.Add(ManualBlock.Heading(Lang.F("快速面板（{0}）", _appSettings.QuickPanelHotkey)))
        t.Add(ManualBlock.Para(Lang.T("在筆的旁邊跳出一個小面板，畫圖常用的都在這裡；再按一次收起。")))
        t.Add(ManualBlock.Bullets(
            Lang.T("常用筆刷：點一下換筆；在格子上按右鍵可以換成別的筆刷。"),
            Lang.T("大小、不透明度、暈開、穩定器、筆壓軟硬、對稱。"),
            Lang.T("常用色票與文件色票，點一下換顏色。"),
            Lang.T("快速圖層：「＋ 新圖層」與切換圖層。"),
            Lang.T("檢視與排版：符合視窗、100%、左右翻轉、尺規、格線、貼齊。"),
            Lang.T("復原、重做。")))
        t.Add(ManualBlock.Heading(Lang.F("圓形選單（{0}）", _appSettings.QuickRadialHotkey)))
        t.Add(ManualBlock.Para(Lang.T("在游標位置跳出一圈常用工具，往那個方向點就換過去；格子上按右鍵可以換成你常用的工具，筆刷那格展開外圈選常用筆刷。")))
        t.Add(ManualBlock.Heading(Lang.T("最常用的快捷鍵")))
        t.Add(ManualBlock.Bullets(
            Lang.T("Ctrl+Z 復原、Ctrl+Y 重做"),
            Lang.T("Ctrl+S 存檔、Ctrl+Shift+S 另存新檔、Ctrl+E 匯出"),
            Lang.T("B 畫筆、E 橡皮擦（繪圖頁籤）"),
            Lang.T("Alt＋點一下：吸色"),
            Lang.T("Ctrl＋點選：暫時切成選取工具"),
            Lang.T("Ctrl+G 群組、Ctrl+Shift+G 解散群組"),
            Lang.T("Ctrl+0 符合視窗、Ctrl+1 實際大小、滾輪縮放、右鍵或中鍵拖曳平移"),
            Lang.T("Ctrl+Shift+R 參考圖、F11 全螢幕繪圖、F1 使用手冊")))
        t.Add(ManualBlock.Note(Lang.T("完整的快捷鍵在本手冊最後一章「快捷鍵」。快速面板與圓形選單的按鍵可以在「檔案 → 設定…」更改；繪圖板的快捷鍵設成這些按鍵最順手。")))
        Return t
    End Function

    '---------------------------------------------------------------- 紙紋

    Private Shared Function BuildPaperConcept(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("紙紋與質感"))
        t.Add(ManualBlock.Para(Lang.T("電繪的線條太乾淨，有時候會顯得「很數位」。加上紙紋，筆觸會吃進紙的凹凸，整張圖也會壓上紙的質感，看起來就像畫在真的紙上。")))
        Dim g As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(168, 84)}
        For Each k In {PaperKind.Sketch, PaperKind.WatercolorRough, PaperKind.Canvas, PaperKind.Washi}
            g.Tiles.Add(New ManualTile With {.Image = PaperSwatch(CInt(k)), .Title = Papers.Names(CInt(k)), .Text = PaperUse(k)})
        Next
        t.Add(g)
        t.Add(ManualBlock.Heading(Lang.T("怎麼設定")))
        t.Add(ManualBlock.Steps(
            Lang.T("新增影像時，底色選「紙張」並挑一種紙；或到「效果」頁籤下方的「紙張與表面紋理」換紙。"),
            Lang.T("「紙紋大小」「紙紋深淺」調整紋路的粗細與明顯程度。"),
            Lang.T("「表面紋理」把紙的凹凸壓成光影，0 為不壓印；「作用在」可以選整張或只有繪圖圖層。")))
        t.Add(ManualBlock.Heading(Lang.T("哪些筆會吃紙紋")))
        t.Add(ManualBlock.Bullets(
            Lang.T("鉛筆、炭筆、粉筆、蠟筆、乾刷：最明顯，輕畫只碰到紙的凸起，重畫才填進凹處。"),
            Lang.T("水彩、毛筆：顏料沿著紙紋與纖維斷續地吃進去。"),
            Lang.T("硬筆、墨水：幾乎不受影響，適合乾淨的線稿。"),
            Lang.T("繪圖頁籤「進階」的「紙紋吃色」調整每支筆受紙紋影響多少。")))
        t.Add(ManualBlock.Note(Lang.T("每種紙的樣子在「右側頁籤 → 繪圖 → 紙紋」都有圖例。紙紋是整份文件共用的，換紙會影響所有吃紙紋的筆觸。")))
        Return t
    End Function

    Private Shared Function PaperUse(k As PaperKind) As String
        Select Case k
            Case PaperKind.Sketch : Return Lang.T("鉛筆素描、炭筆速寫。")
            Case PaperKind.WatercolorRough : Return Lang.T("水彩、粉彩，顆粒明顯。")
            Case PaperKind.Canvas : Return Lang.T("油畫、壓克力的畫布紋。")
            Case PaperKind.Washi : Return Lang.T("水墨、毛筆，長纖維的手工紙。")
            Case Else : Return ""
        End Select
    End Function
End Class
