Imports PhotoEdit

' 使用教學：在木板上用油畫畫《蒙娜麗莎》，臉部放大細畫（達文西的暈塗法）。圖是 uismoke oilmona 實際在 PhotoEdit 裡畫的。
Partial Class frmEditor

    Private Shared Function BuildOilTutorial(parent As ManualSection) As ManualSection
        Dim t = parent.AddChild(Lang.T("在木板上用油畫畫《蒙娜麗莎》"))
        t.Add(ManualBlock.Para(Lang.T("達文西的《蒙娜麗莎》畫在白楊木板上：木板先上一層石膏底，再一層層薄薄地上油彩。這篇照著同樣的順序畫：打底、素描、暗部、鋪色、揉勻，最後放大到臉，用軟筆一小塊一小塊排膚色、用模糊筆柔化（暈塗法）、薄薄罩染明暗，再點上五官。")))
        t.Add(ManualBlock.Note(Lang.T("暈塗法（sfumato）是《蒙娜麗莎》的精髓：臉上沒有任何一條硬線，明暗像煙霧一樣慢慢過渡。嘴角和眼角特別模糊，看的人自己去「補完」表情，所以她的微笑看起來一下有、一下沒有。")))

        t.Add(ManualBlock.Heading(Lang.T("1. 準備木板與參考圖")))
        t.Add(ManualBlock.Steps(
            Lang.T("「檔案 → 新增…」，大小設成和原畫同比例（例如 1200 × 1789），底色選「紙張：木板」。"),
            Lang.T("木板上了石膏底之後是平滑的：「效果」頁籤「紙張與表面紋理」的「表面紋理」設成 0，木紋只在石膏刷得薄的地方淡淡透出來。"),
            Lang.T("「檢視 → 參考圖…」（Ctrl+Shift+R）開啟蒙娜麗莎，切到右側「繪圖」頁籤。")))
        t.Add(ManualBlock.Note(Lang.T("想讓木紋或畫布紋在薄塗的地方透出來：表面紋理調到 30～50，「作用在」選「紙張（顏料蓋住的地方變淡）」。沒畫的地方紋理最明顯，厚塗的地方只留「顏料上的紋理」設定的一點點，就像真的畫在木板上。")))
        t.Add(ManualBlock.Note(Lang.T("原畫上滿是五百年留下的龜裂紋，那不是達文西畫的：臨摹時看「裂紋底下」的顏色，不要把裂紋畫進去。")))
        t.Add(ManualBlock.Picture("tutorial_oil_01.jpg", Lang.T("木板畫布與參考圖。")))

        t.Add(ManualBlock.Heading(Lang.T("2. 打底：石膏底與暖色底")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（打底），常用筆刷選「油畫」，大小 200、不透明度 55%，色票點米白，橫向一筆筆刷滿整張：這是石膏底。"),
            Lang.T("不透明度改 28%，調一個暖土黃，斜斜地再刷一層薄底色。之後的顏色都從這個暖色調出發，整張畫會統一在溫暖的色調裡。")))
        t.Add(ManualBlock.Picture("tutorial_oil_02.jpg", Lang.T("石膏底加上暖土黃的底色。")))

        t.Add(ManualBlock.Heading(Lang.T("3. 素描")))
        t.Add(ManualBlock.Steps(
            Lang.T("參考圖按「描圖」，照片半透明疊在畫布上。"),
            Lang.T("F9：「＋ 新圖層」（素描），硬筆大小 3、不透明度 45%，調稀稀的赭棕色。"),
            Lang.T("勾出人物外形、手、遠山的大輪廓；臉放大來勾：臉的外形、眼睛、鼻底、嘴的位置要準，之後的細節都靠它。"),
            Lang.T("勾完關掉「描圖」。")))
        t.Add(ManualBlock.Picture("tutorial_oil_03.jpg", Lang.T("赭棕色的素描（描圖開著）。")))

        t.Add(ManualBlock.Heading(Lang.T("4. 暗部打底")))
        t.Add(ManualBlock.Para(Lang.T("F9：「＋ 新圖層」（上色），油畫大小 70、不透明度 45%，調深棕色；繪圖頁籤「進階」的「紙紋吃色」調到 0（石膏底是平滑的）。把頭髮、衣服、背景深處這些暗的大塊先鋪上，畫面一下子就有了明暗的骨架。")))
        t.Add(ManualBlock.Picture("tutorial_oil_04.jpg", Lang.T("深棕色鋪出暗的大塊。")))

        t.Add(ManualBlock.Heading(Lang.T("5. 鋪色")))
        t.Add(ManualBlock.Steps(
            Lang.T("同一層，油畫大小 40、不透明度 90%。"),
            Lang.T("看著參考圖，在調色盤（色票、色環）調出要的顏色，順著形體一筆筆鋪滿：天空、遠山、衣服、皮膚。顏色差不多的地方就用同一個顏色連著畫，差很多時才去調新的顏色。")))
        t.Add(ManualBlock.Picture("tutorial_oil_05p.jpg", Lang.T("快速面板：油畫筆、大小、不透明度。")))
        t.Add(ManualBlock.Picture("tutorial_oil_05.jpg", Lang.T("大筆鋪滿顏色。")))

        t.Add(ManualBlock.Heading(Lang.T("6. 中間層")))
        t.Add(ManualBlock.Para(Lang.T("油畫大小 18、不透明度 85%，只補有細節的地方：遠山的層次、小路與橋、衣服的褶子、頭髮的形、手。臉先不要畫，留到最後專心畫。筆觸拉長，順著形體走。")))
        t.Add(ManualBlock.Picture("tutorial_oil_06.jpg", Lang.T("中間層：風景、衣褶、手的形出來了。")))

        t.Add(ManualBlock.Heading(Lang.T("7. 揉勻與空氣遠近法")))
        t.Add(ManualBlock.Steps(
            Lang.T("快速面板的常用筆刷沒有塗抹筆和模糊：在「麥克筆」那格按右鍵換成「塗抹筆」，在「毛筆」那格按右鍵換成「模糊」。"),
            Lang.T("塗抹筆大小 30（還是在「上色」這一層），筆壓放輕，把風景與衣服的筆觸揉開。"),
            Lang.T("模糊大小 30，把遠山和天空再柔化一次：越遠的東西越朦朧、越偏藍，這就是達文西擅長的「空氣遠近法」。")))
        t.Add(ManualBlock.Picture("tutorial_oil_07.jpg", Lang.T("揉勻之後，筆觸消失，遠山退到後面。")))

        t.Add(ManualBlock.Heading(Lang.T("8. 臉：放大，一小塊一小塊排膚色")))
        t.Add(ManualBlock.Steps(
            Lang.T("用滾輪放大到臉（約 170%，符合視窗的 3 倍左右），臉擺在畫面中間。放大來畫，手比較穩、位置比較準。"),
            Lang.T("F9：「＋ 新圖層」（臉），常用筆刷選「軟筆」，大小 12、不透明度 85%。臉用軟筆，像細軟的貂毛筆，不會留下油畫筆的刷痕。"),
            Lang.T("看著參考圖，額頭、臉頰、鼻樑、下巴，一小塊一小塊排上膚色；亮的地方偏黃、暗的地方偏橄欖棕。只畫臉，不要碰到頭髮。"),
            Lang.T("換成大小 5，只在眼睛、鼻子、嘴的周圍，把顏色變化大的地方再分細一點。")))
        t.Add(ManualBlock.Picture("tutorial_oil_08.jpg", Lang.T("放大到臉來畫。")))
        t.Add(ManualBlock.Picture("tutorial_oil_08a.jpg", Lang.T("臉部特寫：膚色排好了，還看得到一塊塊的色塊。")))

        t.Add(ManualBlock.Heading(Lang.T("9. 暈塗法與罩染")))
        t.Add(ManualBlock.Steps(
            Lang.T("常用筆刷選「模糊」，大小 18（同一層「臉」），整張臉輕輕刷一遍，色塊之間的交界變成平滑的漸層。只刷一遍就好，刷太多會失去立體感。"),
            Lang.T("換成大小 8，眼睛、嘴附近輕輕帶過：五官的形不能糊掉。"),
            Lang.T("罩染：軟筆大小 34、不透明度 9%，調暗面的暖棕色，薄薄地疊在右頰、左頰邊、下巴底；大小 16 再疊眼窩、鼻樑右側、下唇下、嘴角的小凹。每一層都很淡，一層層疊出明暗，像油畫的透明罩染。")))
        t.Add(ManualBlock.Note(Lang.T("塗抹筆會「推」顏料，用在臉上容易把五官推歪；模糊筆只柔化不移動，比較適合暈塗法。")))
        t.Add(ManualBlock.Picture("tutorial_oil_09a.jpg", Lang.T("暈塗之後：臉變得柔和，明暗像煙霧一樣過渡。")))

        t.Add(ManualBlock.Heading(Lang.T("10. 五官")))
        t.Add(ManualBlock.Steps(
            Lang.T("F9：「＋ 新圖層」（五官），硬筆（細圓筆）大小 5、不透明度 55%，調虹膜的棕色，塗出兩個圓圓的虹膜；大小 4、更深的顏色點瞳孔。"),
            Lang.T("大小 3、不透明度 40%：上眼瞼像杏仁，中間重、兩端收，蓋住虹膜的上緣。大小 2、不透明度 45%：下眼瞼很淡。蒙娜麗莎幾乎沒有眉毛和睫毛，不要畫。"),
            Lang.T("瞳孔左上點一點極淡的反光（不透明度 35%），眼睛就有了水氣。"),
            Lang.T("鼻子：只畫鼻翼的弧，不透明度 28%，鼻孔幾乎看不見。"),
            Lang.T("嘴：唇縫中段淡（不透明度 32%），兩邊嘴角往外微微上揚、加深一點點（42%）。微笑全在嘴角那兩筆，畫得越輕越神秘。最後軟筆大小 8、不透明度 18% 在上唇薄薄罩一層（上唇比下唇暗）。")))
        t.Add(ManualBlock.Picture("tutorial_oil_10.jpg", Lang.T("硬筆細細點上五官。")))
        t.Add(ManualBlock.Picture("tutorial_oil_10a.jpg", Lang.T("臉部特寫：眼睛、鼻翼、嘴角。")))

        t.Add(ManualBlock.Heading(Lang.T("11. 柔化五官、提亮、暖色罩染")))
        t.Add(ManualBlock.Steps(
            Lang.T("模糊大小 6，在眼瞼、鼻翼、嘴角的線上極輕地刷一下：達文西的臉上沒有硬線。"),
            Lang.T("F9：「＋ 新圖層」（亮部），軟筆大小 22、不透明度 14%，調比額頭再亮一點的暖白，輕輕提亮額頭、鼻樑、左頰、下巴（光從左上來）；大小 8 點下唇中間與下眼瞼下方的小亮點。"),
            Lang.T("軟筆大小 60、不透明度 8%，調暖黃，整張臉最後罩一層：膚色會像從裡面透出光來。")))
        t.Add(ManualBlock.Picture("tutorial_oil_11a.jpg", Lang.T("臉部特寫：完成的臉。")))

        t.Add(ManualBlock.Heading(Lang.T("12. 頭髮與手")))
        t.Add(ManualBlock.Steps(
            Lang.T("回到符合視窗。F9：「＋ 新圖層」（頭髮），硬筆大小 2、不透明度 30%，順著髮流畫一絲絲的細線，暗的先畫；再大小 2、不透明度 45% 畫亮的髮絲與捲度。"),
            Lang.T("F9：「＋ 新圖層」（手），油畫大小 8，看著參考圖排手的膚色；再用模糊大小 14 柔化兩遍。手是畫裡第二重要的地方，但不要畫得比臉還細，視線才會停在臉上。")))
        t.Add(ManualBlock.Picture("tutorial_oil_12.jpg", Lang.T("頭髮。")))
        t.Add(ManualBlock.Picture("tutorial_oil_13.jpg", Lang.T("手。")))

        t.Add(ManualBlock.Heading(Lang.T("13. 完成")))
        t.Add(ManualBlock.Bullets(
            Lang.T("縮小整張看，或用「檢視 → 左右翻轉檢視」，檢查臉的比例：兩眼的高度、鼻子的長度、嘴角的位置差一點點，神韻就不一樣。"),
            Lang.T("存成 .pedx 保留圖層（打底、素描、上色、臉、五官、亮部、頭髮、手），之後還能再修。")))
        t.Add(ManualBlock.Picture("tutorial_oil_14.jpg", Lang.T("完成。")))
        t.Add(ManualBlock.Picture("tutorial_oil_15.jpg", Lang.T("左：原畫；右：油畫臨摹。")))
        t.Add(ManualBlock.Picture("tutorial_oil_16.jpg", Lang.T("臉部比較。左：原畫（滿是龜裂紋）；右：臨摹，畫的是剛畫好、還沒裂的樣子。")))
        t.Add(ManualBlock.Picture("tutorial_oil_14e.jpg", Lang.T("編輯器裡：每個步驟一個圖層，隨時可以再修改。")))

        t.Add(ManualBlock.Heading(Lang.T("工具切換一覽")))
        t.Add(ManualBlock.Steps(
            Lang.T("準備：檔案 → 新增（木板），表面紋理 0；檢視 → 參考圖。"),
            Lang.T("打底：F9 新圖層、油畫 200、55%、米白（石膏底）→ 28%、暖土黃（底色）。"),
            Lang.T("素描：參考圖「描圖」開；F9 新圖層、硬筆 3、45%、赭棕；勾完「描圖」關。"),
            Lang.T("暗部：F9 新圖層（上色）、油畫 70、45%、深棕；進階紙紋吃色 0。"),
            Lang.T("鋪色：油畫 40、90%。中間層：油畫 18、85%。"),
            Lang.T("揉勻：F9「麥克筆」格右鍵換塗抹筆、「毛筆」格換模糊；塗抹筆 30 → 模糊 30（遠景）。"),
            Lang.T("臉：滾輪放大到 170%；F9 新圖層、軟筆 12、100% → 軟筆 5（五官周圍）→ 模糊 18 → 模糊 8 → 軟筆 34、9% 與 16、12%（罩染）。"),
            Lang.T("五官：F9 新圖層、硬筆 5、55%（虹膜）→ 4、45%（瞳孔）→ 3、40%（上眼瞼）→ 2、45%（下眼瞼）→ 2、35%（反光）→ 3、28%（鼻翼）→ 3、32%（唇縫）→ 4、42%（嘴角）→ 軟筆 8、18%（上唇）。"),
            Lang.T("亮部：模糊 6（柔化五官）；F9 新圖層、軟筆 22、14% → 8（小亮點）→ 軟筆 60、8%、暖黃（罩染）。"),
            Lang.T("頭髮：符合視窗；F9 新圖層、硬筆 2、30% → 2、45%。手：F9 新圖層、油畫 8 → 模糊 14。")))

        t.Add(ManualBlock.Heading(Lang.T("小技巧")))
        t.Add(ManualBlock.Bullets(
            Lang.T("先大後小、先暗後亮：大筆把整張鋪滿，再一層層變小；臉最後畫，那時周圍的顏色都定了，膚色比較好判斷。"),
            Lang.T("臉畫在自己的圖層：畫壞了可以整層重來，不會弄髒背景。"),
            Lang.T("顏色要「調」不要「吸」：對著參考圖看，在色環上調出差不多的顏色；吸管吸到的常是裂紋或反光的顏色。"),
            Lang.T("罩染要淡：不透明度 10% 左右疊好幾層，比一次 50% 自然得多。"),
            Lang.T("五官的線寧可淡：線太黑，臉就像畫了眼線、硬邦邦的；淡一點再慢慢加深。")))
        Return t
    End Function
End Class
