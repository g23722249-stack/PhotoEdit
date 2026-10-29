Imports System.Globalization
Imports PhotoEdit

''' <summary>多語系：系統語言對應、英文翻譯表、簡體轉換、情境翻譯。測完切回繁體（其他測試都是原文）。</summary>
Module LangTests

    Sub LangTestsRun()
        Console.WriteLine("多語系")
        Try
            Check("zh-TW、zh-HK、zh-Hant → 繁體",
                  Lang.FromCulture(New CultureInfo("zh-TW")) = UiLanguage.TraditionalChinese AndAlso
                  Lang.FromCulture(New CultureInfo("zh-HK")) = UiLanguage.TraditionalChinese AndAlso
                  Lang.FromCulture(New CultureInfo("zh-Hant")) = UiLanguage.TraditionalChinese)
            Check("zh-CN、zh-SG → 簡體",
                  Lang.FromCulture(New CultureInfo("zh-CN")) = UiLanguage.SimplifiedChinese AndAlso
                  Lang.FromCulture(New CultureInfo("zh-SG")) = UiLanguage.SimplifiedChinese)
            Check("en-US、ja-JP → 英文",
                  Lang.FromCulture(New CultureInfo("en-US")) = UiLanguage.English AndAlso
                  Lang.FromCulture(New CultureInfo("ja-JP")) = UiLanguage.English)

            Lang.Init(UiLanguage.TraditionalChinese)
            Check("繁體：原文照用", Lang.T("調整") = "調整" AndAlso Lang.F("找到 {0} 張臉", 3) = "找到 3 張臉")

            Lang.Init(UiLanguage.English)
            Check("英文：翻譯表已內嵌並載入", Lang.T("調整") = "Adjust", Lang.T("調整"))
            Check("英文：有參數的句子", Lang.F("找到 {0} 張臉", 3) = "3 faces found", Lang.F("找到 {0} 張臉", 3))
            Check("英文：格式化參數 {0:0.0}", Lang.F("完成（{0:0.0} 秒，{1}）", 1.25, "AI") = "Done (1.3 s, AI)",
                  Lang.F("完成（{0:0.0} 秒，{1}）", 1.25, "AI"))
            Check("英文：查不到的句子顯示原文", Lang.T("這句不在翻譯表裡") = "這句不在翻譯表裡")
            Check("英文：反斜線路徑不被當成跳脫字元", Lang.T("貼圖放在程式資料夾的 stick\主題\ 裡。").Contains("stick\theme\"),
                  Lang.T("貼圖放在程式資料夾的 stick\主題\ 裡。"))
            Check("英文：片段前後的空白保留", Lang.T(" 複本") = " copy", "[" & Lang.T(" 複本") & "]")
            Check("英文：情境翻譯（色調 → 滑桿 Tint、濾鏡分類 Tone）", Lang.TC("tint", "色調") = "Tint" AndAlso Lang.T("色調") = "Tone")
            Check("英文：沒有情境譯文時用一般翻譯", Lang.TC("沒有這個情境", "調整") = "Adjust")
            Check("英文：色彩選擇器有自己的情境（調和 → Harmony，繪圖的調和 → Blend）",
                  Lang.TC("picker", "調和") = "Harmony" AndAlso Lang.T("調和") = "Blend")
            Check("英文：多行文字一行一行查", Lang.T("調整" & vbCrLf & "效果") = "Adjust" & vbCrLf & "Effects")
            Check("英文：跳脫寫法", Lang.Escape("a\b" & vbTab & "c" & vbCrLf & "d") = "a\\b\tc\nd")

            Lang.Init(UiLanguage.SimplifiedChinese)
            Check("簡體：用語換成大陸用語再轉字", Lang.T("設定檔案") = "设置文件", Lang.T("設定檔案"))
            Check("簡體：常見字完整轉換（後、黃、偵）", Lang.T("點選後黃點偵測") = "点选后黄点侦测", Lang.T("點選後黃點偵測"))
            Check("簡體：介面字型用微軟雅黑", Lang.UiFont = "Microsoft YaHei UI")
            Check("簡體：參數照樣代入", Lang.F("找到 {0} 張臉", 2) = "找到 2 张脸", Lang.F("找到 {0} 張臉", 2))
        Finally
            Lang.Init(UiLanguage.TraditionalChinese)
        End Try
        Check("測完切回繁體", Lang.Current = UiLanguage.TraditionalChinese AndAlso Lang.T("調整") = "調整" AndAlso Lang.UiFont = "Microsoft JhengHei UI")
    End Sub
End Module
