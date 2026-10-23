Imports PhotoEdit

' 使用教學：用炭筆素描米開朗基羅的《大衛像》（只畫雕像，不畫背景）。圖是 uismoke charcoal 實際在 PhotoEdit 裡畫的。
Partial Class frmEditor

    Private Shared Function BuildCharcoalTutorial(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("用炭筆素描《大衛像》"))
        t.Add(ManualBlock.Para(Lang.T("像在美術教室畫石膏像一樣，用炭筆把米開朗基羅的《大衛像》畫在素描紙上：先打草稿，再由大到小鋪明暗、用塗抹筆抹勻、用橡皮擦擦出亮部，最後壓重暗面的輪廓。只畫雕像，背景留白。")))
        t.Add(ManualBlock.Note(Lang.T("素描的重點是明暗，不是線條：先瞇著眼睛看，分出亮面和暗面兩大塊，再慢慢加中間調與最暗的地方。白色的大理石在紙上就是留白，亮部用橡皮擦擦出來。")))

        t.Add(ManualBlock.Heading(Lang.T("1. 準備素描紙與參考圖")))
        t.Add(ManualBlock.Steps(
            Lang.T("「檔案 → 新增…」，大小設成和照片同比例（例如 900 × 1200），底色選「紙張：素描紙」。炭筆會吃紙的顆粒，輕畫只碰到紙的凸起。"),
            Lang.T("「檢視 → 參考圖…」（Ctrl+Shift+R）開啟大衛像的照片，切到右側「繪圖」頁籤。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_01.jpg", Lang.T("素描紙畫布與參考圖。")))

        t.Add(ManualBlock.Heading(Lang.T("2. 打開「墨色」看明暗")))
        t.Add(ManualBlock.Para(Lang.T("參考圖按「墨色」，照片變成灰階。沒有顏色干擾，哪裡亮、哪裡暗一眼就看得出來：大衛的光從右上來，身體左側、手臂內側、腿的內側是暗面。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_02.jpg", Lang.T("「墨色」開著：參考圖以灰階顯示。")))

        t.Add(ManualBlock.Heading(Lang.T("3. 草稿：只描雕像")))
        t.Add(ManualBlock.Steps(
            Lang.T("參考圖按「描圖」，照片半透明疊在畫布上。"),
            Lang.T("F9：「＋ 新圖層」（草稿），常用筆刷選「鉛筆」，大小 2、不透明度 50%，色票點淺灰。"),
            Lang.T("輕輕描出雕像的外形，以及肌肉、明暗交界這些大的形狀；背景的牆、圓頂和下面的方形底座都不描。"),
            Lang.T("描完關掉「描圖」，之後看著參考圖畫。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_03.jpg", Lang.T("鉛筆草稿：只有雕像的外形與大的明暗交界。")))

        t.Add(ManualBlock.Heading(Lang.T("4. 炭筆鋪暗面")))
        t.Add(ManualBlock.Steps(
            Lang.T("快速面板的常用筆刷沒有炭筆：在「麥克筆」那格按右鍵，換成「炭筆」。"),
            Lang.T("F9：「＋ 新圖層」（炭筆），炭筆大小 30、不透明度 35%，色票點炭黑；繪圖頁籤「進階」的「紙紋吃色」調到 75。"),
            Lang.T("先輕輕鋪一層淡淡的底調：除了最亮的地方，整個雕像都帶一點灰。"),
            Lang.T("再順著肌肉的方向，把暗面鋪重一點。最亮的受光面完全不要碰。")))
        t.Add(ManualBlock.Note(Lang.T("看明暗要看「相對」的：照片上半身比較亮、腿比較暗，但腿上也有受光面。每個地方都跟它旁邊比，哪邊亮、哪邊暗。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_04p.jpg", Lang.T("快速面板：炭筆、大小、不透明度。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_04.jpg", Lang.T("暗面鋪好了：雕像已經有立體感。")))

        t.Add(ManualBlock.Heading(Lang.T("5. 塗抹筆抹勻")))
        t.Add(ManualBlock.Steps(
            Lang.T("在「油畫」那格按右鍵換成「塗抹筆」，大小 26（還是在「炭筆」這一層）。"),
            Lang.T("像用紙筆或手指推炭粉一樣，順著形體把暗面抹勻，筆觸的痕跡變成柔和的漸層。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_05.jpg", Lang.T("抹過之後，暗面變得柔和。")))

        t.Add(ManualBlock.Heading(Lang.T("6. 中間調")))
        t.Add(ManualBlock.Para(Lang.T("炭筆大小 14、不透明度 55%，再畫比較暗的地方：腹肌之間、胸肌下緣、膝蓋與小腿的陰影。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_06.jpg", Lang.T("中間調。")))

        t.Add(ManualBlock.Heading(Lang.T("7. 最暗的地方與臉、頭髮")))
        t.Add(ManualBlock.Steps(
            Lang.T("炭筆大小 7、不透明度 80%：腋下、手指之間、捲髮的縫、膝蓋與腳踝的陰影。"),
            Lang.T("換成大小 3、不透明度 60%：描出側臉的輪廓（額頭、眉骨、鼻子、嘴唇、下巴），點出眼睛與鼻孔，畫耳朵的螺旋與下顎的線。"),
            Lang.T("捲髮不要一根根畫：用一個個開口的小弧線（像 C 字）排出捲曲的紋理，暗的縫壓重一點、亮的地方輕輕帶過。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_07.jpg", Lang.T("壓下最暗的地方。")))

        t.Add(ManualBlock.Heading(Lang.T("8. 橡皮擦擦出亮部")))
        t.Add(ManualBlock.Steps(
            Lang.T("常用筆刷選「軟筆」，大小 16、不透明度 45%，再按 E 換成橡皮擦（擦法「擦掉」）。"),
            Lang.T("在「炭筆」這一層，把最亮的地方輕輕擦回紙的顏色：肩膀、胸口、大腿正面這些直接受光的地方。像素描時用軟橡皮擦提亮。"),
            Lang.T("擦完按 B 回到畫筆。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_08.jpg", Lang.T("亮部擦出來，明暗對比更清楚。")))

        t.Add(ManualBlock.Heading(Lang.T("9. 輪廓：暗面壓重、亮面留淡")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（輪廓），炭筆大小 3、不透明度 35%，沿著整個雕像淡淡描一圈外輪廓。"),
            Lang.T("再把大小調成 4、不透明度 85%，只在暗面那一側的外輪廓與明暗交界加重；受光那一側維持淡淡的線，讓形體「融進」紙裡，看起來更立體。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_09.jpg", Lang.T("暗面那一側的輪廓加重。")))

        t.Add(ManualBlock.Heading(Lang.T("10. 收尾")))
        t.Add(ManualBlock.Bullets(
            Lang.T("到「圖層」頁籤把「草稿」隱藏。"),
            Lang.T("縮小整張看，或用「檢視 → 左右翻轉檢視」，檢查比例與明暗。"),
            Lang.T("存成 .pedx 保留圖層，再匯出 PNG／JPG。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_10.jpg", Lang.T("完成。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_11.jpg", Lang.T("左：照片；右：炭筆素描。")))
        t.Add(ManualBlock.Picture("tutorial_charcoal_10e.jpg", Lang.T("編輯器裡：草稿（隱藏）、炭筆、輪廓三層，隨時可以再修改。")))

        t.Add(ManualBlock.Heading(Lang.T("工具切換一覽")))
        t.Add(ManualBlock.Steps(
            Lang.T("準備：檔案 → 新增（素描紙）；檢視 → 參考圖；參考圖「墨色」開。"),
            Lang.T("草稿：參考圖「描圖」開；F9 新圖層、鉛筆 2、50%、淺灰；描完「描圖」關。"),
            Lang.T("底調與暗面：F9「麥克筆」格右鍵換炭筆；新圖層、炭筆 30、35%、炭黑；進階紙紋吃色 75。"),
            Lang.T("抹勻：F9「油畫」格右鍵換塗抹筆；塗抹筆 26。"),
            Lang.T("中間調：炭筆 14、55%。最暗處：炭筆 7、80%。臉與捲髮：炭筆 3、60%。"),
            Lang.T("亮部：軟筆 16、45%，按 E 橡皮擦；擦完按 B。"),
            Lang.T("輪廓：F9 新圖層、炭筆 3、35%（整圈）→ 炭筆 4、85%（暗面那側）。"),
            Lang.T("收尾：圖層頁籤隱藏草稿。")))

        t.Add(ManualBlock.Heading(Lang.T("小技巧")))
        t.Add(ManualBlock.Bullets(
            Lang.T("由淺到深：炭筆疊上去容易、擦回來難，寧可多鋪幾層淡的。"),
            Lang.T("筆觸順著形體走：手臂、腿是圓柱，筆觸繞著它的弧度畫，立體感會更強。"),
            Lang.T("不要每個地方都畫一樣仔細：臉和胸口畫細一點，腳和岩石可以鬆一點，畫面才有重點。"),
            Lang.T("要畫背景時，另開一層放在雕像下面，用大的炭筆或塗抹筆輕輕鋪一片灰，雕像的亮部就會更亮。")))
        Return t
    End Function
End Class
