Imports PhotoEdit
Imports System.Text.RegularExpressions

' 使用手冊（說明 → 使用手冊…，F1）：內容從程式本身產生——
' 選單照實際的選單樹列出；右側頁籤的說明收集每頁控制項的滑鼠停留說明（HelpTexts），
' 加上簡介、步驟與 manual\ 資料夾裡的套用前／後範例圖；繪圖頁另有工具列、筆觸、紙紋的圖例（當場畫出來）。
' 一鍵功能或畫面上有預覽的工具（濾鏡、一鍵美顏、文字樣式、貼圖）只有文字說明。
Partial Class frmEditor

    Private _menuRoot As Aqua.MenuItem
    Private _manual As frmManual

    Private Sub ShowManual()
        If _manual Is Nothing OrElse _manual.IsDisposed Then
            Cursor = Cursors.WaitCursor
            Try
                _manual = New frmManual(BuildManual(), IO.Path.Combine(AppContext.BaseDirectory, "manual")) With {.Owner = Me}
            Finally
                Cursor = Cursors.Default
            End Try
            Dim wa = Screen.FromControl(Me).WorkingArea
            _manual.Location = New Point(Math.Max(wa.Left, Math.Min(wa.Right - _manual.Width, Left + (Width - _manual.Width) \ 2)),
                                         Math.Max(wa.Top, Math.Min(wa.Bottom - _manual.Height, Top + (Height - _manual.Height) \ 2)))
        End If
        ' 停在目前的頁籤那一章（第一次打開時）
        _manual.Show()
        _manual.Activate()
    End Sub

    '=====================================================================
    ' 手冊內容
    '=====================================================================

    Friend Function BuildManual() As List(Of ManualSection)
        Dim result As New List(Of ManualSection) From {BuildStartSection(), BuildTutorialSection(), BuildConceptSection(), BuildMenuSection(), BuildTabsSection(), BuildKeysSection()}
        Return result
    End Function

    Private Function BuildStartSection() As ManualSection
        Dim s As New ManualSection(Lang.T("開始使用"))
        s.Add(ManualBlock.Para(Lang.T("PhotoEdit 是非破壞性的照片編輯器：所有調整、修補、繪圖、文字與貼圖都記在編輯檔裡，原始照片永遠不會被修改，隨時可以回頭再改。")))
        s.Add(ManualBlock.Heading(Lang.T("畫面配置")))
        s.Add(ManualBlock.Bullets(
            Lang.T("上方是選單：檔案、編輯、影像、選取、檢視、說明。"),
            Lang.T("中間是照片；滾輪縮放，按住右鍵或中鍵拖曳移動畫面。"),
            Lang.T("右側是功能頁籤，依修照片的順序排列：調整、修補、人像、局部、效果｜選取、去背｜繪圖、圖層｜文字、貼圖、裝飾。"),
            Lang.T("切到繪圖頁籤時，左側會出現繪圖工具列與色盤。")))
        s.Add(ManualBlock.Heading(Lang.T("基本流程")))
        s.Add(ManualBlock.Steps(
            Lang.T("「檔案 → 載入…」（Ctrl+O）開啟照片，或直接把圖檔拖進視窗。"),
            Lang.T("從右側頁籤由上往下修：先在「調整」套濾鏡或調曝光，再修補、美顏、局部調整。"),
            Lang.T("需要時加上繪圖、文字、貼圖與邊框。"),
            Lang.T("「存檔」（Ctrl+S）存成 PhotoEdit 專案（.pedx），保留所有圖層，下次可以繼續修改。"),
            Lang.T("要得到合併後的圖檔，用「匯出…」（Ctrl+E）存成 PNG、JPG 或 BMP。")))
        s.Add(ManualBlock.Note(Lang.T("滑鼠停在任何按鈕或滑桿上一會兒，就會跳出這個功能的說明（可在「說明 → 顯示使用說明」關閉）。本手冊的說明項目大多和那些提示相同，可以用上方的搜尋找到。")))
        Return s
    End Function

    '---------------------------------------------------------------- 使用教學

    Private Function BuildTutorialSection() As ManualSection
        Dim s As New ManualSection(Lang.T("使用教學"))
        s.Add(ManualBlock.Para(Lang.T("一步一步完成一件作品。教學裡的圖都是在 PhotoEdit 裡實際畫出來的。")))
        Dim links As New ManualBlock With {.Kind = ManualBlockKind.Links}
        links.LinkTargets.Add(BuildStarryTutorial(s))
        links.LinkTargets.Add(BuildInkTutorial(s))
        links.LinkTargets.Add(BuildVectorTutorial(s))
        s.Add(links)
        Return s
    End Function

    ''' <summary>新手用向量工具畫海報：矩形＋漸層、鋼筆描色塊、參考圖右鍵吸填色、由大到小、群組、文字、直線、外框、圓角矩形、橢圓、匯出 SVG。</summary>
    Private Shared Function BuildVectorTutorial(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("用向量工具畫《藍黃金剛鸚鵡》海報"))
        t.Add(ManualBlock.Para(Lang.T("給第一次用向量工具的人：照著一張海報，用矩形、鋼筆、文字、直線、圓角矩形、橢圓把它重畫成向量圖。向量圖的每一塊都是一個可以再修改的圖形，放大不會糊，也能匯出成 SVG 給印刷或網頁用。")))
        t.Add(ManualBlock.Note(Lang.T("向量畫法的訣竅：由大到小。先鋪大色塊（天空、身體、翅膀、樹枝），再疊中等的（羽毛、葉子、山），最後加小細節（眼睛、羽紋、爪子）；後畫的蓋在先畫的上面，所以小的不怕被蓋掉。")))

        t.Add(ManualBlock.Heading(Lang.T("先認識會用到的工具")))
        t.Add(ManualBlock.Bullets(
            Lang.T("矩形、圓角矩形、橢圓：在畫面上從一角拖到對角就畫出來；按住 Shift 畫正方形、正圓。"),
            Lang.T("鋼筆：點一下＝尖角節點；按住拖曳＝平滑節點（拉出把手，曲線順著把手彎）；點回第一個節點＝封閉成一個色塊；Enter 或雙擊＝結束成開放的線；Esc＝取消。"),
            Lang.T("文字：拖一個框，輸入文字；字型、字級、粗體、斜體、文字色在右側設定。"),
            Lang.T("直線：拖一下畫一條線（線條色、粗細在右側）。"),
            Lang.T("選取（工具列第一個）：點圖形選它、拖曳移動、拖控制點縮放；要改已經畫好的圖形顏色，先用選取工具點它。"),
            Lang.T("右側「填色」「外框」兩個勾：色塊通常只勾填色；外框、分隔線只勾外框。")))

        t.Add(ManualBlock.Heading(Lang.T("1. 準備")))
        t.Add(ManualBlock.Steps(
            Lang.T("「檔案 → 新增…」，大小和海報一樣（1024 × 1536），白色。"),
            Lang.T("「檢視 → 參考圖…」開啟海報，按「描圖」：海報半透明疊在畫布上，照著描。"),
            Lang.T("右側切到「繪圖」頁籤。")))
        t.Add(ManualBlock.Picture("tutorial_vec_01.jpg", Lang.T("白色畫布、參考圖視窗。")))

        t.Add(ManualBlock.Heading(Lang.T("2. 天空：矩形＋漸層")))
        t.Add(ManualBlock.Steps(
            Lang.T("工具列選「矩形」。右側勾「填色」、取消「外框」。"),
            Lang.T("「填色」選深藍（可以在參考圖的天空上按右鍵吸色），「填色方式」選「線性漸層」，「第二色」選淺藍。"),
            Lang.T("從畫面左上角外面拖到右下角外面，蓋滿整張。"),
            Lang.T("畫面上會出現漸層的兩個控制點：起點拖到上緣、終點拖到中間偏下，天空就是上深下淺。"),
            Lang.T("「填色方式」改回「單色」，之後的色塊才是單色。")))
        t.Add(ManualBlock.Picture("tutorial_vec_02.jpg", Lang.T("一個矩形就是整片漸層天空。")))

        t.Add(ManualBlock.Heading(Lang.T("3. 用鋼筆描色塊")))
        t.Add(ManualBlock.Steps(
            Lang.T("工具列選「鋼筆」（填色開、外框關沿用剛才的設定）。"),
            Lang.T("換顏色：在參考圖上要描的那塊顏色按右鍵——吸到的顏色直接變成填色（左鍵是線條色）。"),
            Lang.T("沿著那塊顏色的邊緣一圈點下去：轉角點一下，圓滑的地方按住往前拖一點拉出把手。"),
            Lang.T("最後點回第一個節點，色塊就封閉、填上顏色，變成一個圖層。"),
            Lang.T("接著換下一塊的顏色、再描。剛畫好的圖形雖然還選著，換的顏色是給下一塊用的，不會改到它。")))
        t.Add(ManualBlock.Picture("tutorial_vec_03.jpg", Lang.T("第一塊（黃色身體）描好：方框是選取框，下方狀態列顯示從參考圖吸到的填色。")))
        t.Add(ManualBlock.Note(Lang.T("小的色塊要先放大（Ctrl＋滾輪，在要描的地方放大）再描：點下去的地方離第一個節點太近（9 像素內）會被當成「點回起點」而提早封閉。描大塊之前按 F9 快速面板的「符合視窗」縮回整張。")))

        t.Add(ManualBlock.Heading(Lang.T("4. 由大到小")))
        t.Add(ManualBlock.Para(Lang.T("先描最大的幾塊：鸚鵡的黃色身體、藍色背、樹枝、大片的葉子與山。")))
        t.Add(ManualBlock.Picture("tutorial_vec_04.jpg", Lang.T("大色塊。")))
        t.Add(ManualBlock.Para(Lang.T("再描中等的：翅膀的羽毛層次、頭頂的綠、嘴、葉子、雲。")))
        t.Add(ManualBlock.Picture("tutorial_vec_05.jpg", Lang.T("中色塊。")))
        t.Add(ManualBlock.Para(Lang.T("最後是小細節：羽毛的亮暗、爪子、水面的反光。眼睛和臉上的黑紋太細，照色塊描會糊成一團，下一步另外畫。")))
        t.Add(ManualBlock.Picture("tutorial_vec_06.jpg", Lang.T("細節描完。")))
        t.Add(ManualBlock.Heading(Lang.T("5. 群組")))
        t.Add(ManualBlock.Para(Lang.T("每一級描完就組成群組，圖層清單才不會亂：在圖層清單點這一級的第一個，按住 Shift 點最後一個，按 Ctrl+G，再把群組改名（大色塊、中色塊、細節）。之後點群組裡任何一塊會選到整組，雙擊進去才能改裡面單一塊。")))

        t.Add(ManualBlock.Heading(Lang.T("6. 眼睛與臉上的黑紋")))
        t.Add(ManualBlock.Para(Lang.T("眼睛和黑紋只有幾個像素寬，要放大到頭部專心畫：在眼睛附近 Ctrl＋滾輪放大到 300～400%。順序是先鋪白色的臉，再疊眼睛，最後畫黑紋。")))
        t.Add(ManualBlock.Steps(
            Lang.T("白色的臉：鋼筆（填色開、外框關），填色米白，沿著臉的白色範圍描一圈、點回起點。它會蓋掉底下描出來的灰藍碎塊。"),
            Lang.T("眼睛：工具列選「橢圓」，按住 Shift 從圓的左上拖到右下畫正圓。由外往內疊四個圓：深灰外圈（最大）→ 米白虹膜 → 黑色瞳孔 → 一個很小的白色反光點。後畫的會蓋在上面，所以要由大到小。"),
            Lang.T("鼻孔：橢圓（不按 Shift），黑色，在嘴的上方畫一個小橢圓。"),
            Lang.T("黑紋：工具列選「鋼筆」，右側取消「填色」、勾「外框」，線條色黑、粗細 4。"),
            Lang.T("每一條紋：起點點一下（尖角），中間的點沿著紋路方向拖一小段（拉出把手，線就會彎得順），終點點一下，然後按 Enter。按 Enter 是結束成「開放的線」；如果點回起點就會變成封閉的色塊。"),
            Lang.T("眼睛下面畫三條長紋；粗細改 3，在眼睛和嘴之間畫五條直紋。"),
            Lang.T("把臉、眼睛、黑紋選起來 Ctrl+G，改名「眼睛與黑紋」。")))
        t.Add(ManualBlock.Picture("tutorial_vec_06b.jpg", Lang.T("放大到 400%：白色的臉、四個正圓疊成的眼睛、鼻孔，以及用開放路徑畫的黑紋。")))
        t.Add(ManualBlock.Note(Lang.T("封閉色塊和開放的線差在最後一下：點回起點＝封閉（填上填色）；按 Enter 或雙擊＝開放的線。開放的線不會填色，也一定會畫出線條，所以畫紋路時重點是線條色和粗細；取消「填色」、勾「外框」是為了之後改畫封閉圖形時不會忘記切換。")))

        t.Add(ManualBlock.Heading(Lang.T("7. 文字")))
        t.Add(ManualBlock.Steps(
            Lang.T("按「描圖」關掉參考圖的疊圖。工具列選「文字」。"),
            Lang.T("打字前先在右側設好：圖說字型「Georgia」、圖說字級 60、勾「粗體」、文字色米白。"),
            Lang.T("在左上角拖一個框，輸入「BLUE &」，點畫面其他地方完成。"),
            Lang.T("文字色換金黃，再拖一個框輸入「YELLOW」；換回米白輸入「MACAW」。打完的字不會被下一段的設定改到。"),
            Lang.T("學名：字級 26、取消粗體、勾「斜體」，輸入「Ara ararauna」。"),
            Lang.T("小字：字型 Arial、字級 12，輸入三行說明（可以按 Enter 換行）；右上角的說明同樣做法。")))
        t.Add(ManualBlock.Picture("tutorial_vec_07.jpg", Lang.T("標題、學名與說明文字。")))

        t.Add(ManualBlock.Heading(Lang.T("8. 分隔線與金色外框")))
        t.Add(ManualBlock.Steps(
            Lang.T("工具列選「直線」，右側取消「填色」、勾「外框」，線條色金色、粗細 2，在學名下面拖一條線。"),
            Lang.T("工具列選「矩形」（外框還勾著、填色沒勾），從畫面一角內縮一點拖到對角：只有金色的框線。")))

        t.Add(ManualBlock.Heading(Lang.T("9. 底部資訊欄")))
        t.Add(ManualBlock.Steps(
            Lang.T("工具列選「圓角矩形」，勾填色、取消外框，填色深綠，在左下角拖出一塊。"),
            Lang.T("「橢圓」，填色金黃，按住 Shift 拖出一個正圓（地球圖示）。"),
            Lang.T("「鋼筆」：尖角—拖曳—尖角—拖曳，點回起點，就是一片葉子；再做一片（或選取後按 Ctrl+J 再製）。"),
            Lang.T("「文字」：字型 Arial、字級 11、粗體、金黃，三個圖示下面各打兩行字。"),
            Lang.T("把文字、線、外框、資訊欄選起來 Ctrl+G，改名「文字與裝飾」。")))

        t.Add(ManualBlock.Heading(Lang.T("10. 完成與匯出")))
        t.Add(ManualBlock.Steps(
            Lang.T("「檔案 → 存檔」存成 .pedx：每個圖形、群組、文字都保留，下次可以繼續改。"),
            Lang.T("「檔案 → 匯出 SVG（向量圖形）…」：給 Illustrator、Inkscape、印刷或網頁用，放大也不會糊。"),
            Lang.T("「檔案 → 匯出…」存成 PNG／JPG 一般圖檔。")))
        t.Add(ManualBlock.Picture("tutorial_vec_09.jpg", Lang.T("完成的向量海報。")))
        t.Add(ManualBlock.Picture("tutorial_vec_10.jpg", Lang.T("左：原海報；右：向量重畫。")))
        t.Add(ManualBlock.Picture("tutorial_vec_09e.jpg", Lang.T("編輯器裡：大色塊、中色塊、細節、眼睛與黑紋、文字與裝飾五個群組。")))

        t.Add(ManualBlock.Heading(Lang.T("工具切換一覽")))
        t.Add(ManualBlock.Steps(
            Lang.T("準備：檔案 → 新增；檢視 → 參考圖、描圖。"),
            Lang.T("天空：矩形；填色開、外框關；線性漸層＋第二色；拖漸層控制點。"),
            Lang.T("色塊：鋼筆；每塊先在參考圖右鍵吸填色；小塊 Ctrl＋滾輪放大，大塊 F9「符合視窗」。"),
            Lang.T("群組：圖層清單點第一個、Shift＋點最後一個，Ctrl+G，改名。"),
            Lang.T("眼睛與黑紋：放大到頭部；鋼筆鋪白臉；橢圓＋Shift 疊四個正圓；鋼筆（取消填色、勾外框、黑、粗細 4→3）畫開放的紋，每條按 Enter。"),
            Lang.T("文字：關描圖；文字工具；右側字型、字級、粗體／斜體、文字色。"),
            Lang.T("分隔線：直線；填色關、外框開、金色、粗細 2。"),
            Lang.T("外框：矩形（只有外框）。"),
            Lang.T("資訊欄：圓角矩形（填色開、外框關）、橢圓（Shift 正圓）、鋼筆葉子、文字。"),
            Lang.T("匯出：存檔 .pedx、匯出 SVG、匯出 PNG。")))

        t.Add(ManualBlock.Heading(Lang.T("常見問題")))
        t.Add(ManualBlock.Bullets(
            Lang.T("想改剛畫好的那塊顏色？用選取工具點它（或按住 Ctrl 點），再換填色。拿著鋼筆、形狀或文字工具換的顏色，是給下一個圖形用的。"),
            Lang.T("色塊之間有細縫？描的時候沿著邊緣外側一點點，相鄰的兩塊稍微重疊就不會露縫。"),
            Lang.T("描歪了？用「節點」工具拖節點和把手修形；雙擊路徑加節點，雙擊節點刪除。"),
            Lang.T("點太多節點？「編輯 → 簡化路徑…」可以減少節點，讓曲線更平順。")))
        Return t
    End Function

    ''' <summary>用繪圖板在棉紙上以毛筆畫水墨蒙娜麗莎：暈開、墨色取色、由淡到濃、勾線、落款。</summary>
    Private Shared Function BuildInkTutorial(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("用繪圖板畫水墨《蒙娜麗莎》"))
        t.Add(ManualBlock.Para(Lang.T("用 Wacom 等繪圖板，在棉紙上以毛筆把達文西的《蒙娜麗莎》畫成水墨畫。重點是「暈開」（墨沿著紙纖維滲開）、參考圖的「墨色」與「畫筆取色」（原畫的明暗自動換成墨色濃淡），以及水墨由淡到濃、最後勾線的畫法。")))
        t.Add(ManualBlock.Note(Lang.T("水墨的層次：先用淡墨鋪大片明暗、再中墨、濃墨，最後用焦墨勾線點睛；亮的地方留白不畫。墨越濕（暈開越大）邊緣越柔、飛白越少；勾線時把暈開調小，線條才清楚。")))
        t.Add(ManualBlock.Picture("tutorial_ink_bleed.png", Lang.T("暈開 0／40／80 的比較（棉紙上的毛筆）：墨沿紙纖維往外滲，滲出的墨較淡，筆毛間的白縫被濕墨填滿。")))

        t.Add(ManualBlock.Heading(Lang.T("1. 準備：棉紙與參考圖")))
        t.Add(ManualBlock.Steps(
            Lang.T("「檔案 → 新增…」，大小設成和原畫同比例（例如 1000 × 1490），底色選「紙張：棉紙（和紙）」。"),
            Lang.T("「檢視 → 參考圖…」開啟蒙娜麗莎的圖檔；切到右側「繪圖」頁籤。")))
        t.Add(ManualBlock.Picture("tutorial_ink_01.jpg", Lang.T("棉紙畫布與參考圖。")))

        t.Add(ManualBlock.Heading(Lang.T("2. 草稿")))
        t.Add(ManualBlock.Steps(
            Lang.T("參考圖按「描圖」。"),
            Lang.T("F9：「＋ 新圖層」（草稿），常用筆刷選「鉛筆」，大小 2、不透明度 60%，色票點淺灰。"),
            Lang.T("輕輕描出臉、頭髮、肩膀與手的輪廓；描完關掉「描圖」。草稿最後會隱藏。")))
        t.Add(ManualBlock.Picture("tutorial_ink_02.jpg", Lang.T("開著描圖用鉛筆打草稿。")))

        t.Add(ManualBlock.Heading(Lang.T("3. 打開「墨色」")))
        t.Add(ManualBlock.Para(Lang.T("參考圖按「墨色」：參考圖改成灰階顯示，看得出濃淡；之後從參考圖吸到的顏色（點一下或「畫筆取色」）都會換成同樣深淺的墨色。")))
        t.Add(ManualBlock.Picture("tutorial_ink_03r.jpg", Lang.T("「墨色」開著：參考圖以灰階顯示。")))

        t.Add(ManualBlock.Heading(Lang.T("4. 淡墨鋪底")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（淡墨），常用筆刷選「毛筆」，大小 45、不透明度 55%，色票點淡墨灰，「暈開」拉到 60。"),
            Lang.T("順著頭髮、衣服、背景山水的走向，大筆鋪出暗部；臉、胸口、手這些亮的地方留白。")))
        t.Add(ManualBlock.Picture("tutorial_ink_03.jpg", Lang.T("快速面板：毛筆、大小、不透明度、暈開都在這裡調。")))
        t.Add(ManualBlock.Picture("tutorial_ink_04.jpg", Lang.T("淡墨鋪好的明暗。")))

        t.Add(ManualBlock.Heading(Lang.T("5. 中墨")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（中墨），毛筆大小 22、不透明度 70%、暈開 45。"),
            Lang.T("參考圖打開「畫筆取色」（墨色開著）：每一筆自動用原畫那裡的明暗當墨色，不必一直換顏色。"),
            Lang.T("畫頭髮的層次、衣服的皺褶與人物周圍；背景只加在比較暗的地方。")))
        t.Add(ManualBlock.Picture("tutorial_ink_05.jpg", Lang.T("中墨。")))

        t.Add(ManualBlock.Heading(Lang.T("6. 濃墨")))
        t.Add(ManualBlock.Para(Lang.T("F9：「＋ 新圖層」（濃墨），毛筆大小 14、不透明度 85%、暈開 30，「畫筆取色」照開。只畫最暗的地方：頭髮的陰影、衣服的深處、背景最暗的樹叢。")))
        t.Add(ManualBlock.Picture("tutorial_ink_06.jpg", Lang.T("濃墨把最暗的地方壓下去。")))

        t.Add(ManualBlock.Heading(Lang.T("7. 勾線")))
        t.Add(ManualBlock.Steps(
            Lang.T("先關掉「畫筆取色」。F9：「＋ 新圖層」（勾線），毛筆大小 5、不透明度 90%，色票點焦墨（最黑），暈開調小到 15。"),
            Lang.T("勾頭髮的邊、衣領與手；線條一筆到底，不要來回描。臉先不勾，下一步另外畫。")))
        t.Add(ManualBlock.Note(Lang.T("前面淡墨、中墨、濃墨與勾線都避開臉：大筆在臉上只會留下一塊塊墨漬，五官要一筆一筆照著畫。")))
        t.Add(ManualBlock.Picture("tutorial_ink_07.jpg", Lang.T("焦墨勾線。")))

        t.Add(ManualBlock.Heading(Lang.T("8. 臉：白描與點睛")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（臉），毛筆大小 3、不透明度 70%、色票淡墨、暈開 12：輕輕勾下眼瞼。"),
            Lang.T("色票換中墨：鼻翼與鼻底各輕輕帶一小筆。鼻樑不勾線，交給渲染。"),
            Lang.T("在快速面板「麥克筆」那格按右鍵換成「墨水」。墨水筆、中墨：先把眼珠塗滿；再換焦墨點瞳孔。"),
            Lang.T("換回毛筆，大小 6、不透明度 90%、色票濃墨：上眼瞼像杏仁一筆到底（中間重、兩端收），壓住眼珠上緣，眼睛才不會瞪大；嘴從兩邊嘴角往中間各一筆，嘴角微微上揚。"),
            Lang.T("大小 3、色票中墨：把嘴的中段輕輕連起來。")))
        t.Add(ManualBlock.Note(Lang.T("先塗眼珠、後勾上眼瞼；眼瞼離眼珠太遠，看起來會像眉毛。蒙娜麗莎沒有眉毛，眼皮上也不要加線。")))

        t.Add(ManualBlock.Heading(Lang.T("9. 渲染")))
        t.Add(ManualBlock.Para(Lang.T("F9：「＋ 新圖層」（渲染），毛筆大小 40、不透明度 30%、色票淡墨、暈開 85（濕筆）。光從左上來：沿右半邊臉頰、左頰邊與下巴底大筆染開；大小改 18，染眼窩、鼻樑左側、鼻下、下唇下與嘴角。最後換「水彩」、大小 30、不透明度 35%、暈開 70，染手的陰影。")))
        t.Add(ManualBlock.Picture("tutorial_ink_08.jpg", Lang.T("臉部白描、點睛與渲染之後。")))

        t.Add(ManualBlock.Heading(Lang.T("10. 落款與收尾")))
        t.Add(ManualBlock.Steps(
            Lang.T("工具列選「矩形」，填色選朱紅、取消描邊，不透明度調回 100%（會沿用上一支筆的設定），在右下角拖出一個小方塊當印章。"),
            Lang.T("到「圖層」頁籤隱藏「草稿」。"),
            Lang.T("存檔（.pedx 保留每個圖層）或匯出成 PNG／JPG。")))
        t.Add(ManualBlock.Picture("tutorial_ink_09.jpg", Lang.T("完成。")))
        t.Add(ManualBlock.Picture("tutorial_ink_10.jpg", Lang.T("左：原畫；右：水墨。")))
        t.Add(ManualBlock.Picture("tutorial_ink_09e.jpg", Lang.T("編輯器裡：淡墨、中墨、濃墨、勾線、臉、渲染各一層，印章是向量矩形，隨時可以修改。")))

        t.Add(ManualBlock.Heading(Lang.T("工具切換一覽")))
        t.Add(ManualBlock.Steps(
            Lang.T("準備：檔案 → 新增（棉紙）；檢視 → 參考圖。"),
            Lang.T("草稿：參考圖「描圖」開；F9 新圖層、鉛筆 2、60%、淺灰；描完「描圖」關。"),
            Lang.T("參考圖「墨色」開。"),
            Lang.T("淡墨：F9 新圖層、毛筆 45、55%、淡墨、暈開 60。"),
            Lang.T("中墨：F9 新圖層、毛筆 22、70%、暈開 45；參考圖「畫筆取色」開。"),
            Lang.T("濃墨：F9 新圖層、毛筆 14、85%、暈開 30；畫筆取色照開。"),
            Lang.T("勾線：畫筆取色關；F9 新圖層、毛筆 5、90%、焦墨、暈開 15。"),
            Lang.T("臉：F9 新圖層、毛筆 3、70%、淡墨、暈開 12 → 中墨；「麥克筆」格右鍵換成墨水，中墨塗眼珠、焦墨點瞳孔 → 毛筆 6、90%、濃墨 → 毛筆 3、中墨。"),
            Lang.T("渲染：F9 新圖層、毛筆 40、30%、淡墨、暈開 85 → 毛筆 18 → 水彩 30、35%、暈開 70（手）。"),
            Lang.T("落款：工具列矩形、朱紅填色、不描邊、100%。"),
            Lang.T("收尾：圖層頁籤隱藏草稿。")))

        t.Add(ManualBlock.Heading(Lang.T("小技巧")))
        t.Add(ManualBlock.Bullets(
            Lang.T("暈開在放開筆之後才算出來（畫的時候先看到乾的筆畫）。"),
            Lang.T("同一支毛筆：暈開大、筆壓重＝濕筆濃墨；暈開小、筆輕而快＝乾筆飛白。"),
            Lang.T("棉紙、宣紙的纖維會讓暈開的邊不規則；在一般白底上暈開會比較圓滑。"),
            Lang.T("想要更淡的墨，把不透明度調低，比換更淺的灰更像加水。")))
        Return t
    End Function

    ''' <summary>用觸控筆臨摹梵谷《星夜》：參考圖、描圖、畫筆取色、快速面板、分圖層。</summary>
    Private Shared Function BuildStarryTutorial(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("用觸控筆臨摹梵谷《星夜》"))
        t.Add(ManualBlock.Para(Lang.T("用繪圖板或觸控筆，對照原畫臨摹梵谷的《星夜》。會用到參考圖視窗（描圖、畫筆取色）、快速面板（F9）換筆與粗細、分圖層一區一區畫，以及油畫筆、噴槍和鉛筆。")))
        t.Add(ManualBlock.Note(Lang.T("梵谷的筆觸是順著天空的流動、漩渦與柏樹的形狀一筆一筆畫的：每一筆都順著那個方向下筆，起筆輕、中段壓重、收筆放輕，就會有油畫的厚度與方向感。")))

        t.Add(ManualBlock.Heading(Lang.T("1. 準備畫布與參考圖")))
        t.Add(ManualBlock.Steps(
            Lang.T("「檔案 → 新增…」，大小設成和原畫同比例（例如 1500 × 928），底色選「紙張：畫布」。筆刷會吃畫布的紋路，整張也會壓上淡淡的布紋。"),
            Lang.T("「檢視 → 參考圖…」（Ctrl+Shift+R），按「開啟圖片…」選星夜的圖檔。參考圖視窗可以拖到畫面旁邊，滾輪縮放、拖曳移動。"),
            Lang.T("切到右側的「繪圖」頁籤，左側會出現繪圖工具列。")))
        t.Add(ManualBlock.Picture("tutorial_starry_01.jpg", Lang.T("新的畫布（紙張：畫布）與參考圖視窗。")))

        t.Add(ManualBlock.Heading(Lang.T("2. 打草稿：描圖")))
        t.Add(ManualBlock.Steps(
            Lang.T("按 F9 叫出快速面板：按「＋ 新圖層」，名稱改成「草稿」；常用筆刷選「鉛筆」，筆刷大小 3、不透明度 80%，在常用色票點深灰色。再按一次 F9 收起來。"),
            Lang.T("在參考圖視窗按「描圖」：原畫會半透明疊在畫布上（只顯示，不會畫進作品）。"),
            Lang.T("用鉛筆描出主要的輪廓：漩渦、星星與月亮的圓、柏樹、山的稜線、教堂尖塔。描完把「描圖」關掉。")))
        t.Add(ManualBlock.Picture("tutorial_starry_02.jpg", Lang.T("快速面板：新圖層、鉛筆、筆刷大小、顏色都在這裡換，筆不用離開畫面。")))
        t.Add(ManualBlock.Picture("tutorial_starry_03.jpg", Lang.T("開著描圖用鉛筆描輪廓。")))

        t.Add(ManualBlock.Heading(Lang.T("3. 打開「畫筆取色」")))
        t.Add(ManualBlock.Para(Lang.T("臨摹時最累的是一直換顏色。在參考圖視窗按「畫筆取色」，之後每一筆落筆時，會自動用參考圖同一個位置的顏色（參考圖和畫布照描圖的位置對應），筆一路畫下去就好。要自己決定顏色時再關掉，改在參考圖上點一下吸色，或用快速面板的色環、色票。")))
        t.Add(ManualBlock.Picture("tutorial_starry_03r.jpg", Lang.T("參考圖視窗的「畫筆取色」開著（黃色）。")))

        t.Add(ManualBlock.Heading(Lang.T("4. 鋪底色")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（底色），筆刷選「油畫」，筆刷大小 50。"),
            Lang.T("用大筆順著天空的流動方向把整張鋪滿：天空橫向帶一點彎、漩渦繞圈、柏樹由下往上。不必細，先把大色塊放對。")))
        t.Add(ManualBlock.Picture("tutorial_starry_04.jpg", Lang.T("大號油畫筆鋪好的底色。")))

        t.Add(ManualBlock.Heading(Lang.T("5. 天空的筆觸與漩渦")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（天空筆觸），油畫筆、筆刷大小 16。"),
            Lang.T("一筆一筆順著漩渦繞，每筆不要太長；藍、淺藍、白黃交錯，讓天空「流動」起來。"),
            Lang.T("覺得方向不對時，可以按快速面板的「左右翻轉」檢視構圖（只翻畫面，不改作品）。")))
        t.Add(ManualBlock.Picture("tutorial_starry_05q.jpg", Lang.T("換中號油畫筆。")))
        t.Add(ManualBlock.Picture("tutorial_starry_05.jpg", Lang.T("天空的筆觸與兩個漩渦。")))

        t.Add(ManualBlock.Heading(Lang.T("6. 星星與月亮")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（星星月亮），筆刷選「噴槍」，筆刷大小 60、不透明度 35%。"),
            Lang.T("光暈要用星星中心的顏色：先關掉「畫筆取色」，在參考圖上點星星中心吸色，再繞著星星噴一圈。每顆星星都這樣做。"),
            Lang.T("再打開「畫筆取色」，換油畫筆、大小 8，在星星和月亮上用小筆一圈一圈畫出亮黃的核心。")))
        t.Add(ManualBlock.Picture("tutorial_starry_06.jpg", Lang.T("噴槍的光暈加上油畫筆的核心。")))

        t.Add(ManualBlock.Heading(Lang.T("7. 柏樹")))
        t.Add(ManualBlock.Para(Lang.T("F9：「＋ 新圖層」（柏樹），油畫筆、大小 14。由下往上畫火焰形的筆觸，越往上越細；深綠、深棕交錯。")))
        t.Add(ManualBlock.Picture("tutorial_starry_07.jpg", Lang.T("往上竄的柏樹。")))

        t.Add(ManualBlock.Heading(Lang.T("8. 山與村莊")))
        t.Add(ManualBlock.Para(Lang.T("F9：「＋ 新圖層」（山與村莊），油畫筆、大小 10，順著山的稜線畫；最後換大小 6，在房子上點出亮黃的窗戶燈光。")))
        t.Add(ManualBlock.Picture("tutorial_starry_08.jpg", Lang.T("遠山、村莊與燈光。")))

        t.Add(ManualBlock.Heading(Lang.T("9. 收尾")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（細節），油畫筆、大小 5，在輪廓與亮處補幾筆細節。"),
            Lang.T("到「圖層」頁籤點「草稿」的眼睛把它隱藏。"),
            Lang.T("「檔案 → 存檔」存成 .pedx（每個圖層都保留，下次可以繼續畫），「匯出…」存成 PNG 或 JPG。")))
        t.Add(ManualBlock.Picture("tutorial_starry_09.jpg", Lang.T("完成。")))
        t.Add(ManualBlock.Picture("tutorial_starry_10.jpg", Lang.T("左：原畫；右：臨摹。")))
        t.Add(ManualBlock.Picture("tutorial_starry_09e.jpg", Lang.T("編輯器裡的樣子：每一區一個圖層，隨時可以回去修改。")))

        t.Add(ManualBlock.Heading(Lang.T("小技巧")))
        t.Add(ManualBlock.Bullets(
            Lang.T("快速面板（F9）和圓形快速選單（F10）都會出現在筆的旁邊，換筆、換粗細、換圖層都不用把筆移到右側面板。"),
            Lang.T("快速面板的「筆壓」可以選硬、標準、軟；手比較輕就選「軟」，輕輕畫也有顏色。"),
            Lang.T("線條抖的話把「穩定器」調高一點。"),
            Lang.T("畫錯了按快速面板的「↶ 復原」，或 Ctrl+Z。"),
            Lang.T("每一區分開一個圖層：畫壞了只要清掉那一層重畫，不會影響其他部分。")))
        Return t
    End Function

    '---------------------------------------------------------------- 選單

    ''' <summary>選單指令的說明（依指令名稱）。</summary>
    Private Shared Function MenuHelpText(name As String) As String
        Select Case name
            ' 檔案
            Case "new" : Return Lang.T("建立一張空白畫布：選尺寸（常用尺寸或自訂）與背景色，適合從頭畫畫或排版。")
            Case "open" : Return Lang.T("開啟照片或 PhotoEdit 專案（.pedx）。照片上次的編輯會自動載回來。")
            Case "save" : Return Lang.T("存成 PhotoEdit 專案（.pedx）：照片、調整與所有圖層都保留，下次載入可以繼續修改。原始照片不會被修改。")
            Case "saveas" : Return Lang.T("另存一份：存成 .pedx 專案，或存成 PNG／JPG／BMP（合併後的圖檔）。")
            Case "export" : Return Lang.T("把目前的結果合併成一張圖檔（PNG、JPG、BMP），可以選尺寸與品質。")
            Case "exportsvg" : Return Lang.T("把向量圖形（鋼筆、形狀、文字）匯出成 SVG，可以在 Illustrator、Inkscape 或瀏覽器裡開啟。")
            Case "collage" : Return Lang.T("把多張照片排成一張拼貼：選版型、間距與底色。")
            Case "revert" : Return Lang.T("清除這張照片的所有編輯，回到原始照片的樣子。")
            Case "settings" : Return Lang.T("偏好設定：介面語言、外觀、快捷鍵、繪圖、筆印預覽，以及「模型」頁籤（AI 模型的用途、狀態與下載：人臉、去背、AI 重繪的模型不附在程式裡，需要時在這裡下載）。沒開圖片也能設定。")
            Case "exit" : Return Lang.T("關閉 PhotoEdit；有沒存的修改會先詢問。")
            ' 編輯
            Case "undo" : Return Lang.T("取消上一個動作。")
            Case "redo" : Return Lang.T("重做剛剛取消的動作。")
            Case "cut" : Return Lang.T("把選取區的內容複製到剪貼簿，原處變透明。")
            Case "copy" : Return Lang.T("複製到剪貼簿：有選取區時複製選取的部分，否則複製合併後的整張圖。")
            Case "pasteobject" : Return Lang.T("把剪貼簿的圖片貼成一個可以移動、縮放、旋轉的物件圖層。")
            Case "duplicate" : Return Lang.T("再製選取的物件（同 CorelDRAW 的智慧再製）：把複本移動或旋轉後再按一次，下一份會照同樣的距離與角度排下去。")
            Case "steprepeat" : Return Lang.T("一次做好幾份選取的物件：設定份數、水平與垂直距離、每份旋轉的角度。")
            Case "contour" : Return Lang.T("替選取的向量圖形加上一圈圈往外擴或往內縮的輪廓，和原本的組成群組。")
            Case "distort" : Return Lang.T("變形選取的向量圖形：收縮膨脹、粗糙、鋸齒、扭轉。")
            Case "extrude" : Return Lang.T("把選取的向量圖形做成有厚度的立體字或立體圖形。")
            Case "simplify" : Return Lang.T("減少路徑的節點，讓手繪線條更平順、檔案更小。")
            Case "pasteimage" : Return Lang.T("把剪貼簿的圖片開成一張新的影像。")
            Case "copyadj" : Return Lang.T("複製這張照片的調整（曝光、色彩、效果等），可以貼到別張照片。")
            Case "pasteadj" : Return Lang.T("把複製的調整套用到目前的照片。")
            Case "resetadj" : Return Lang.T("把調整頁籤的滑桿都歸零（不影響繪圖、文字與貼圖）。")
            ' 影像
            Case "rotl", "rotr" : Return Lang.T("把照片轉 90 度。")
            Case "fliph", "flipv" : Return Lang.T("把照片左右或上下翻轉。")
            Case "crop" : Return Lang.T("進入裁切模式：拖曳框線調整範圍，可以選比例或形狀（圓形、愛心、星形…），按 Enter 完成。")
            Case "smartcrop" : Return Lang.T("依人臉自動擺好裁切框：臉群水平置中、眼睛落在上方三分線，再進入裁切模式讓你微調。")
            Case "autostraighten" : Return Lang.T("偵測照片裡的水平線或垂直線，自動把歪掉的照片轉正。")
            Case "heal" : Return Lang.T("切到修補頁籤的修補筆刷：塗過污點或雜物，用周圍的紋理補起來。")
            Case "auto" : Return Lang.T("自動調整曝光、對比與色彩，讓照片明亮清楚。")
            Case "autowb" : Return Lang.T("自動修正偏色（偏黃、偏藍），讓白色回到白色。")
            Case "airedraw" : Return Lang.T("用本機顯示卡的 AI 把照片重繪成宮崎駿動畫風格，有多種風格可選。需要下載模型。")
            Case "morph" : Return Lang.T("在兩張臉之間做出漸變的動畫，可以輸出影片或 GIF。")
            Case "trace" : Return Lang.T("把點陣圖描成向量圖形（可以無限放大、修改節點），可以選顏色數與細緻度。")
            Case "facechange" : Return Lang.T("把照片做成川劇變臉的影片：依序換上幾張臉譜，輸出 MP4 或 GIF。")
            ' 選取
            Case "selectall" : Return Lang.T("選取整張照片。")
            Case "deselect" : Return Lang.T("取消選取區。")
            Case "invertsel" : Return Lang.T("選取區內外對調。")
            Case "toobject" : Return Lang.T("把選取的部分複製成一個可以移動的物件圖層。")
            Case "seladjust" : Return Lang.T("之後的調整只作用在選取區（變成一個局部調整）。")
            Case "seldelete" : Return Lang.T("刪除選取區的內容，變成透明。")
            Case "selfill" : Return Lang.T("用顏色、漸層或材質填滿選取區。")
            Case "selstroke" : Return Lang.T("沿著選取區的邊緣描一條線。")
            Case "selcrop" : Return Lang.T("把照片裁切成選取區的外框大小。")
            Case "selcutout" : Return Lang.T("把選取區當成去背範圍：選取的部分保留，其餘變成背景。")
            ' 檢視
            Case "fit" : Return Lang.T("縮放到整張照片剛好放進視窗。")
            Case "actual" : Return Lang.T("以實際像素（100%）顯示。")
            Case "zoomin", "zoomout" : Return Lang.T("放大或縮小畫面（只影響顯示，不改照片）。")
            Case "reference" : Return Lang.T("開一個浮動的參考圖視窗：臨摹時把原作放旁邊，點一下吸色，也可以半透明疊在畫布上描圖。")
            Case "fullscreen" : Return Lang.T("全螢幕繪圖：隱藏選單與頁籤，只留畫布與工具。按 F11 或 Esc 回來。")
            Case "flipview" : Return Lang.T("畫面左右翻轉來檢查構圖（畫家常用的技巧），不會改到照片。")
            Case "rulers" : Return Lang.T("在畫面上方與左邊顯示尺規；從尺規拖出參考線。")
            Case "grid" : Return Lang.T("在畫面上顯示格線（只顯示，不會畫進照片）。")
            Case "snapgrid" : Return Lang.T("移動、畫圖形時貼齊格線。")
            Case "snapguides" : Return Lang.T("移動、畫圖形時貼齊參考線。")
            Case "gridsize" : Return Lang.T("設定格線間距。")
            Case "clearguides" : Return Lang.T("移除所有參考線。")
            ' 說明
            Case "togglehelp" : Return Lang.T("開關滑鼠停留說明：滑鼠停在按鈕或滑桿上一會兒就顯示這個功能的用法。")
            Case "manual" : Return Lang.T("開啟這份使用手冊，可以搜尋。")
            Case "about" : Return Lang.T("版本與授權資訊。")
        End Select
        Return ""
    End Function

    Private Shared ReadOnly ShortcutPattern As New Regex("\s*\(([^()]*(Ctrl|Shift|Alt|F\d+|^[A-Z]$)[^()]*)\)\s*$")

    ''' <summary>選單文字拆成名稱與快捷鍵：「存檔 (Ctrl+S)」→（存檔, Ctrl+S）。</summary>
    Private Shared Function SplitShortcut(text As String) As (Name As String, Keys As String)
        Dim m = ShortcutPattern.Match(text)
        If Not m.Success Then Return (text, "")
        Return (text.Substring(0, m.Index).Trim(), m.Groups(1).Value)
    End Function

    Private Function BuildMenuSection() As ManualSection
        Dim s As New ManualSection(Lang.T("選單"))
        s.Add(ManualBlock.Para(Lang.T("視窗上方的選單。每個指令的作用與快捷鍵如下；點選左邊目錄或下面的連結看各選單。")))
        Dim links As New ManualBlock With {.Kind = ManualBlockKind.Links}
        If _menuRoot IsNot Nothing Then
            For i = 0 To _menuRoot.Count - 1
                Dim menu = _menuRoot(i)
                Dim items = Enumerable.Range(0, menu.Count).Select(Function(k) menu(k)).
                    Where(Function(m) m.Text <> "-" AndAlso Not m.Name.StartsWith("mdi_") AndAlso Not m.Name.StartsWith("recent")).ToList()
                If items.Count = 0 Then Continue For ' 視窗選單（多文件才有內容）
                Dim child = s.AddChild(menu.Text)
                child.Add(ManualBlock.Para(Lang.F("「{0}」選單的指令：", menu.Text)))
                For Each m In items
                    Dim parts = SplitShortcut(m.Text)
                    Dim text = MenuHelpText(m.Name)
                    If m.Name = "" AndAlso m.Count > 0 Then text = Lang.T("列出最近開啟的檔案，點一下直接開啟；最下面可以清除清單。")
                    child.Add(ManualBlock.Help(parts.Name, text, If(parts.Keys = "", "", Lang.T("快捷鍵 ") & parts.Keys)))
                Next
                links.LinkTargets.Add(child)
            Next
        End If
        s.Add(links)
        Return s
    End Function

    '---------------------------------------------------------------- 右側頁籤

    ''' <summary>頁籤的補充內容：簡介之後的步驟、範例圖、附註。</summary>
    Private Shared Sub AddTabExtras(tab As Integer, s As ManualSection)
        Select Case tab
            Case 0 ' 調整
                s.Add(ManualBlock.Steps(
                    Lang.T("上方「濾鏡」先點一個縮圖套用整組風格（分類：色調、復古、繪畫、漫畫）；縮圖就是套用後的預覽。"),
                    Lang.T("下方「手動調整」拖曳滑桿微調曝光、對比、亮部、暗部、色溫、飽和度與銳利度。"),
                    Lang.T("滿意的調整可以用「編輯 → 複製調整」（Ctrl+Shift+C）貼到別張照片。")))
                s.Add(ManualBlock.Pair("adjust", Lang.T("曝光 +0.5、對比 +15、飽和度 +30、色溫 +15：整體更亮、顏色更飽滿。")))
                s.Add(ManualBlock.Note(Lang.T("濾鏡區塊可以收起來（標題右邊的按鈕），手動調整的滑桿會往上移。")))
            Case 1 ' 修補
                s.Add(ManualBlock.Pair("repair_spot", Lang.T("修補筆刷：在三個污點上各點一下，用周圍的紋理補起來。")))
                s.Add(ManualBlock.Pair("repair_denoise", Lang.T("降噪：亮度雜訊 70、色彩雜訊 50，顆粒變少、畫面變乾淨。")))
                s.Add(ManualBlock.Pair("repair_straighten", Lang.T("拉直：照片歪了 4 度，拉直 −4° 轉正（周圍自動裁掉）。")))
            Case 2 ' 人像
                s.Add(ManualBlock.Steps(
                    Lang.T("開啟照片後會自動偵測人臉，偵測到才能用美顏。"),
                    Lang.T("「一鍵美顏」直接套用一組設定（按鈕上就是預覽）；也可以分別調整肌膚與五官。"),
                    Lang.T("有多張臉時可以選「個別臉」，只調整其中一張。")))
                s.Add(ManualBlock.Pair("portrait", Lang.T("磨皮 65、美白 30、亮眼 30、大眼 25、瘦臉 25、腮紅 25。")))
            Case 3 ' 局部
                s.Add(ManualBlock.Pair("local_gradient", Lang.T("漸層濾鏡：從上往下拉，天空壓暗、飽和度提高，下方不受影響。")))
                s.Add(ManualBlock.Pair("local_tiltshift", Lang.T("移軸：上下模糊、中間清楚，看起來像微縮模型。")))
            Case 4 ' 效果
                s.Add(ManualBlock.Pair("effects", Lang.T("暗角 −60、顆粒 35、褪色 20：復古底片感。")))
                s.Add(ManualBlock.Pair("effects_toning", Lang.T("飽和度 −100 再加上色彩濾鏡（色相 35、強度 55）：懷舊的褐色調。")))
                s.Add(ManualBlock.Pair("effects_art", Lang.T("繪畫風格「水彩」。")))
                s.Add(ManualBlock.Pair("effects_sketch", Lang.T("繪畫風格「素描」。")))
            Case 5 ' 選取
                s.Add(ManualBlock.Steps(
                    Lang.T("選一種選取工具（矩形、橢圓、套索、魔術棒、貝茲），在照片上拖曳或點選。"),
                    Lang.T("按住 Shift 加入、Alt 減去；也可以用上方的模式按鈕切換。"),
                    Lang.T("選好之後用「選取」選單或本頁的按鈕：複製成物件、只調整選取區、填色、描邊、裁切、當作去背範圍。")))
            Case 6 ' 去背
                s.Add(ManualBlock.Steps(
                    Lang.T("選模型：「人像」適合人物（頭髮較準），「一般」適合動物與物品。"),
                    Lang.T("按「自動去背」，AI 找出主體（第一次會久一點）。"),
                    Lang.T("用修正筆刷把漏掉的補回來、多出來的擦掉。"),
                    Lang.T("選背景：透明、純色、模糊原背景或換成其他圖片；也可以把主體存成貼圖。")))
                s.Add(ManualBlock.Pair("cutout", Lang.T("人像模型自動去背，背景換成淺藍色。")))
            Case 7 ' 繪圖
                s.Add(ManualBlock.Steps(
                    Lang.T("左側工具列選工具：點陣筆刷直接畫在點陣圖層上；向量工具（鋼筆、形狀、文字）每個圖形都可以再修改。"),
                    Lang.T("本頁選筆刷、粗細、不透明度與顏色；筆刷可以從「筆刷瀏覽」挑。"),
                    Lang.T("文件的紙張（紙紋）會影響筆觸的質感，炭筆、粉彩、鉛筆在粗紙上最明顯。")))
                s.Add(ManualBlock.Note(Lang.T("繪圖不會改到原始照片，存在編輯檔裡。工具列、筆觸、紙紋的圖例見下面的子章節。")))
            Case 8 ' 圖層
                s.Add(ManualBlock.Bullets(
                    Lang.T("用上移、下移、移到最上、移到最下調整順序；點眼睛顯示或隱藏，點鎖頭鎖定繪圖圖層。"),
                    Lang.T("設定混合模式（色彩增值、濾色、覆蓋…）與不透明度。"),
                    Lang.T("按兩下圖層跳到它的頁籤編輯。")))
            Case 9 ' 文字
                s.Add(ManualBlock.Steps(
                    Lang.T("按「＋ 文字」在照片中央加入一段文字，在下面的方框輸入內容。"),
                    Lang.T("選字型與樣式（樣式按鈕上就是預覽），再調整外框、底色、陰影、發光、立體等特效。"),
                    Lang.T("在照片上拖曳移動；Ctrl＋滾輪縮放、Shift＋滾輪旋轉選取中的文字。")))
            Case 10 ' 貼圖
                s.Add(ManualBlock.Steps(
                    Lang.T("選主題，把貼圖拖到照片上就貼在那個位置；在貼圖上按兩下則貼在照片中央。"),
                    Lang.T("拖曳移動；Ctrl＋滾輪縮放、Shift＋滾輪旋轉選取中的貼圖。")))
                s.Add(ManualBlock.Note(Lang.T("貼圖放在程式資料夾的 stick\主題\ 裡，自己加 PNG 進去就會出現新的貼圖。")))
            Case 11 ' 裝飾
                s.Add(ManualBlock.Pair("decor", Lang.T("拍立得邊框：下方留白較多，可以寫字。")))
        End Select
    End Sub

    ''' <summary>一個控制項底下所有有滑鼠停留說明的控制項，依畫面由上而下、由左而右。</summary>
    Private Function CollectHelp(root As Control) As List(Of HelpTip.Entry)
        Dim found As New List(Of (Y As Integer, X As Integer, E As HelpTip.Entry))()
        Dim walk As Action(Of Control, Point) = Nothing
        walk = Sub(c, origin)
                   For Each child As Control In c.Controls
                       Dim p As New Point(origin.X + child.Left, origin.Y + child.Top)
                       Dim e = _help.EntryOf(child)
                       If e IsNot Nothing AndAlso e.Title <> "" Then found.Add((p.Y, p.X, e))
                       walk(child, p)
                   Next
               End Sub
        walk(root, Point.Empty)
        Dim seen As New HashSet(Of String)()
        Return found.OrderBy(Function(f) f.Y).ThenBy(Function(f) f.X).Select(Function(f) f.E).
            Where(Function(e) seen.Add(e.Title & ChrW(1) & e.Text)).ToList()
    End Function

    Private Function BuildTabsSection() As ManualSection
        Dim s As New ManualSection(Lang.T("右側頁籤"))
        s.Add(ManualBlock.Para(Lang.T("右側的功能頁籤依修照片的順序排列：先修照片（調整、修補、人像、局部、效果），再選取、去背，然後繪圖與圖層，最後加文字、貼圖、裝飾。")))
        s.Add(ManualBlock.Note(Lang.T("有範例圖的章節會顯示「套用前／套用後」；一鍵功能和畫面上有預覽的工具（濾鏡、一鍵美顏、文字樣式、貼圖）只有文字說明，直接點點看最快。")))
        Dim links As New ManualBlock With {.Kind = ManualBlockKind.Links}
        For i = 0 To _tabs.TabPages.Count - 1
            Dim page As Control = _tabs.TabPages(i)
            Dim child = s.AddChild(DirectCast(_tabs.TabPages(i), Aqua.TabPage).Title)
            Dim intro = HelpTexts.Get("tab." & i)
            If intro IsNot Nothing Then
                child.Add(ManualBlock.Para(intro.Text))
                If intro.Hint <> "" Then child.Add(ManualBlock.Para(intro.Hint))
            End If
            AddTabExtras(i, child)
            Dim entries = CollectHelp(page)
            If entries.Count > 0 Then
                child.Add(ManualBlock.Heading(Lang.T("本頁的功能")))
                For Each e In entries
                    child.Add(ManualBlock.Help(e))
                Next
            End If
            If i = TabDraw Then AddDrawingSections(child)
            links.LinkTargets.Add(child)
        Next
        s.Add(links)
        Return s
    End Function

    '---------------------------------------------------------------- 繪圖：工具列、筆觸、紙紋

    Private Sub AddDrawingSections(drawSection As ManualSection)
        ' 工具列
        Dim tools = drawSection.AddChild(Lang.T("繪圖工具列"))
        tools.Add(ManualBlock.Para(Lang.T("切到繪圖頁籤時出現在畫面左側。分成共用、點陣、向量三組；右下角有小三角形的按鈕，按右鍵或點那個小三角形，可以換成同一格的其他工具。")))
        For Each grp In _drawStrip.ToolGroups
            tools.Add(ManualBlock.Heading(grp.Title))
            Dim gallery As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(40, 40)}
            Dim seen As New HashSet(Of String)()
            For Each slot In grp.Slots
                For Each t In slot
                    Dim key = DrawIcons.HelpKey(t)
                    If Not seen.Add(key & "|" & t) Then Continue For
                    Dim e = HelpTexts.Get(key)
                    gallery.Tiles.Add(New ManualTile With {.Image = ToolIcon(t), .Title = DrawIcons.ToolName(t),
                                                           .Text = If(e Is Nothing, "", e.Text) & If(slot.Length > 1 AndAlso t <> slot(0), Lang.T("（和「") & DrawIcons.ToolName(slot(0)) & Lang.T("」同一格）"), "")})
                Next
            Next
            tools.Add(gallery)
        Next
        tools.Add(ManualBlock.Heading(Lang.T("色盤")))
        tools.Add(ManualBlock.Bullets(
            Lang.T("左鍵點色塊設成線條色，右鍵設成填色。"),
            Lang.T("點上方的大色塊開完整的選色視窗；雙箭頭對調兩色，斜線是不填色。"),
            Lang.T("「＋」把目前的顏色加入文件色票，按兩下色票可以編輯。")))

        ' 筆觸
        Dim brushSec = drawSection.AddChild(Lang.T("筆觸"))
        brushSec.Add(ManualBlock.Para(Lang.T("點陣筆刷畫出來的樣子（同一條曲線、筆壓由輕到重再到輕）。在繪圖頁籤或「筆刷瀏覽」選筆；繪圖板的筆壓會讓粗細與濃淡變化。")))
        Dim groups = {(Lang.T("基本"), {BrushKind.HardRound, BrushKind.SoftRound, BrushKind.Pencil, BrushKind.Charcoal, BrushKind.Chalk, BrushKind.Crayon, BrushKind.Airbrush}),
                      (Lang.T("繪畫"), {BrushKind.Watercolor, BrushKind.OilPaint, BrushKind.Acrylic, BrushKind.Ink, BrushKind.ChineseBrush, BrushKind.Marker, BrushKind.DryBrush}),
                      (Lang.T("Painter 筆（讀取畫布顏色）"), {BrushKind.Mixer, BrushKind.Smudge, BrushKind.Clone}),
                      (Lang.T("特殊"), {BrushKind.Texture, BrushKind.FX, BrushKind.Particle, BrushKind.StickerHose})}
        For Each grp In groups
            brushSec.Add(ManualBlock.Heading(grp.Item1))
            Dim gallery As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(180, 64)}
            For Each b In grp.Item2
                Dim e = HelpTexts.Get("draw.brush." & CInt(b))
                gallery.Tiles.Add(New ManualTile With {.Image = BrushSwatch(b), .Title = DrawGeometry.BrushNames(CInt(b)), .Text = If(e Is Nothing, "", e.Text)})
            Next
            brushSec.Add(gallery)
        Next
        brushSec.Add(ManualBlock.Note(Lang.T("混色筆、塗抹筆、仿製筆會讀取畫布上的顏色，只能用在點陣圖層；示意圖是畫在彩色底上的效果。")))

        ' 紙紋
        Dim paperSec = drawSection.AddChild(Lang.T("紙紋"))
        paperSec.Add(ManualBlock.Para(Lang.T("整份文件用的紙張或材質（類似 Corel Painter 的紙張）。紙紋有兩個作用：筆刷的顆粒會跟著紙的凹凸（炭筆、粉彩、鉛筆最明顯），以及「表面紋理」把凹凸壓印在整張圖上。")))
        paperSec.Add(ManualBlock.Para(Lang.T("下面每格都是同一種紙加上一筆炭筆：")))
        Dim pg As New ManualBlock With {.Kind = ManualBlockKind.Gallery, .TileImageSize = New Size(168, 84)}
        For Each kind In Papers.DisplayOrder.Where(Function(x) x <> PaperKind.Custom) ' 「自訂」是匯入圖片
            pg.Tiles.Add(New ManualTile With {.Image = PaperSwatch(CInt(kind)), .Title = Papers.Names(CInt(kind)), .Text = PaperDescription(kind)})
        Next
        paperSec.Add(pg)
        paperSec.Add(ManualBlock.Heading(Lang.T("棉紙（和紙）")))
        paperSec.Add(ManualBlock.Para(Lang.T("像日本和紙、楮皮紙、雲龍紙：細而長的纖維往各方向交錯，偶爾聚成一團一團的纖維，底色比宣紙更白，厚薄有點不均勻。和「宣紙」比起來纖維更長、更明顯。")))
        paperSec.Add(ManualBlock.Picture("paper_washi.png", Lang.T("放大比較（照 1000 像素高的影像看到的紋路）：上是宣紙，下是棉紙。")))
        paperSec.Add(ManualBlock.Bullets(
            Lang.T("水墨、毛筆、水彩：顏料沿著纖維斷續吃進去，有手工紙的味道。"),
            Lang.T("炭筆、粉彩、鉛筆：長纖維會在筆觸裡留下一絲一絲的白線。"),
            Lang.T("想要纖維更長更粗：紙張設定的「紋路大小」調大（150～200%）；想要淡一點：「深淺」調低。"),
            Lang.T("新增影像時底色選「紙張：棉紙（和紙）」，筆刷會吃纖維紋，整張也會壓上淡淡的紙紋。")))
        paperSec.Add(ManualBlock.Heading(Lang.T("紙張設定")))
        For Each key In HelpTexts.AllKeys.Where(Function(x) x.StartsWith("paper."))
            Dim e = HelpTexts.Get(key)
            If e IsNot Nothing Then paperSec.Add(ManualBlock.Help(e))
        Next
    End Sub

    ''' <summary>手冊紙紋圖例的說明。</summary>
    Private Shared Function PaperDescription(kind As PaperKind) As String
        Select Case kind
            Case PaperKind.Sketch : Return Lang.T("細顆粒，鉛筆、炭筆素描。")
            Case PaperKind.WatercolorFine : Return Lang.T("細紋水彩紙，淡彩、插畫。")
            Case PaperKind.WatercolorRough : Return Lang.T("粗紋水彩紙，一顆顆圓凸，乾筆會露白。")
            Case PaperKind.Canvas : Return Lang.T("油畫布的編織紋，油畫、壓克力。")
            Case PaperKind.Pastel : Return Lang.T("蜂巢紋，粉彩、蠟筆。")
            Case PaperKind.Laid : Return Lang.T("細的橫向簾紋加稀疏直線，像手工紙。")
            Case PaperKind.Rice : Return Lang.T("短而細的纖維，水墨、毛筆。")
            Case PaperKind.Washi : Return Lang.T("細長纖維交錯，偶有纖維團；水墨、淡彩、版畫風。")
            Case PaperKind.Kraft : Return Lang.T("牛皮紙，帶纖維與小斑點。")
            Case PaperKind.Linen : Return Lang.T("細密的亞麻布紋。")
            Case PaperKind.Burlap : Return Lang.T("粗麻布，大格子編織。")
            Case PaperKind.Crumpled : Return Lang.T("揉過再攤開的摺痕。")
            Case PaperKind.Sandpaper : Return Lang.T("粗顆粒，粉彩、炭筆會很粗獷。")
            Case PaperKind.Concrete : Return Lang.T("水泥牆面，塗鴉風。")
            Case PaperKind.Brick : Return Lang.T("磚牆，塗鴉、壁畫風。")
            Case PaperKind.Wood : Return Lang.T("木紋板。")
            Case PaperKind.Leather : Return Lang.T("皮革紋。")
        End Select
        Return ""
    End Function

    Private Shared Function ToolIcon(tool As Integer) As Bitmap
        Dim bmp As New Bitmap(40, 40)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Using br As New SolidBrush(Color.FromArgb(236, 238, 242)), p As New Pen(Color.FromArgb(200, 204, 212))
                g.FillRectangle(br, 0, 0, 39, 39)
                g.DrawRectangle(p, 0, 0, 39, 39)
            End Using
            DrawIcons.DrawTool(g, tool, New RectangleF(6, 6, 28, 28), Color.FromArgb(40, 44, 52))
        End Using
        Return bmp
    End Function

    Private Shared Function BrushSwatch(b As BrushKind) As Bitmap
        Const W As Integer = 180, H As Integer = 64
        Dim bmp As New Bitmap(W, H)
        Using g = Graphics.FromImage(bmp)
            If DrawLayer.SamplesCanvas(b) Then
                ' 讀畫布的筆：畫在彩色底上才看得出效果
                Using lg As New Drawing2D.LinearGradientBrush(New Rectangle(0, 0, W, H), Color.FromArgb(250, 210, 120), Color.FromArgb(110, 170, 230), 0.0F)
                    g.FillRectangle(lg, 0, 0, W, H)
                End Using
            Else
                g.Clear(Color.FromArgb(252, 251, 248))
            End If
            Try
                Using stroke = DrawingRenderer.BrushPreview(b, FxKind.Fire, MaterialKind.Stone, W, H, Color.FromArgb(40, 70, 140))
                    g.DrawImage(stroke, 0, 0)
                End Using
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
                ' 缺資源（例如沒有貼圖）時只留底色
            End Try
            Using p As New Pen(Color.FromArgb(214, 218, 226))
                g.DrawRectangle(p, 0, 0, W - 1, H - 1)
            End Using
        End Using
        Return bmp
    End Function

    Private Shared Function PaperSwatch(kind As Integer) As Bitmap
        Const W As Integer = 168, H As Integer = 84
        Dim settings As New PaperSettings With {.Kind = CType(kind, PaperKind)}
        Dim map = Papers.HeightMap(settings, W, 1000)
        Dim bmp As New Bitmap(W, H, Imaging.PixelFormat.Format32bppArgb)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Papers.BaseColors(kind))
        End Using
        Papers.ApplySurface(bmp, map, W, 1000, 0, 0, 100, 135)
        ' 一筆炭筆（顆粒跟著這種紙）
        Dim s As New DrawStroke()
        For i = 0 To 30
            Dim t = i / 30.0F
            s.Points.Add(New DrawPoint((0.1F + t * 0.8F) * W / H, 0.5F + CSng(Math.Sin(t * Math.PI * 1.6)) * 0.18F, CSng(0.5 + 0.5 * Math.Sin(t * Math.PI))))
        Next
        Dim layer As New DrawLayer With {.Shape = DrawShape.Freehand, .Brush = BrushKind.Charcoal, .StrokeColorArgb = Color.FromArgb(45, 40, 38).ToArgb(),
                                         .StrokeWidth = 0.22, .Seed = 5, .Strokes = New List(Of DrawStroke) From {s}}
        Dim r = DrawingRenderer.RenderPreview(layer, W, H, settings)
        If r.Bitmap IsNot Nothing Then
            Using g = Graphics.FromImage(bmp)
                g.DrawImage(r.Bitmap, r.Region)
            End Using
            r.Bitmap.Dispose()
        End If
        Return bmp
    End Function

    '---------------------------------------------------------------- 快捷鍵

    Private Function BuildKeysSection() As ManualSection
        Dim s As New ManualSection(Lang.T("快捷鍵"))
        s.Add(ManualBlock.Para(Lang.T("常用的快捷鍵（選單裡的指令都把快捷鍵寫在名稱後面）。")))
        Dim rows As New List(Of String)()
        If _menuRoot IsNot Nothing Then
            For i = 0 To _menuRoot.Count - 1
                Dim menu = _menuRoot(i)
                For k = 0 To menu.Count - 1
                    Dim parts = SplitShortcut(menu(k).Text)
                    If parts.Keys <> "" Then rows.Add(parts.Keys & "　" & parts.Name)
                Next
            Next
        End If
        s.Add(ManualBlock.Heading(Lang.T("選單指令")))
        s.Add(ManualBlock.Bullets(rows.ToArray()))
        s.Add(ManualBlock.Heading(Lang.T("其他")))
        s.Add(ManualBlock.Bullets(
            "F1　" & Lang.T("使用手冊"),
            Lang.F("{0}　快速工具面板（滑鼠旁邊）", _appSettings.QuickPanelHotkey),
            Lang.F("{0}　圓形快速選單", _appSettings.QuickRadialHotkey),
            "F11　" & Lang.T("全螢幕繪圖"),
            Lang.T("滾輪　縮放畫面"),
            Lang.T("右鍵或中鍵拖曳　平移畫面"),
            Lang.T("Esc　取消目前的動作（裁切、吸管、選取…）")))
        s.Add(ManualBlock.Note(Lang.T("快速工具面板與圓形選單的快捷鍵可以在「檔案 → 設定…」更改。")))
        Return s
    End Function
End Class
