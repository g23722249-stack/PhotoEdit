# AI Auto Composite Studio（人物合成）

VB.NET、.NET 8 的 Windows 桌面 AI 人物合成軟體。選一張背景照片和一張人物圖，程式會自動去背、推薦人物位置、調整比例與透視、匹配色彩、產生接觸陰影，最後以背景原始解析度輸出。

架構是**以傳統影像合成為主，AI 模型為輔**：AI 只負責去背（以後再加局部修補）。定位、變換、遮擋、色彩、陰影、合成都是可重現的確定性計算，背景照片不會被生成式模型重畫。

> 範例情境：背景是雅典娜神殿的石柱照片，人物是藍髮、金色盔甲的動漫女戰士。
> 用 ToonOut 去背 → 天際線分析找到柱頂 → 腳點對齊柱頂、腳寬不超出平台、頭部不超出畫面 → 兩條石柱邊緣參考線推估仰角透視 → 依柱頂附近的背景輕度調整色溫與亮度 → 陰影只落在石頭上、不落在天空。

## 專案結構

```
AutoComposite/
├─ AutoComposite.sln
├─ AutoComposite.Core/          核心（net8.0，不依賴 Windows，可在任何平台建置與測試）
│  ├─ Models/                   CompositeScene、CompositeLayer、變換／色彩／陰影參數
│  ├─ Imaging/                  RgbaImage、FloatMask、PNG 編解碼（含 16 位元遮罩）、EXIF 轉正、模糊、形態學
│  ├─ Transforms/               Matrix3（仿射、四點透視）、TransformService、LayerSampler（金字塔＋雙三次）
│  ├─ Composition/              FigureAnalyzer、SceneAnalyzer、AutoPlacementService、CompositeRenderer、ExportService、SceneHistory
│  ├─ Segmentation/             OnnxModelLoader、OnnxHeaderReader、BackgroundRemovalService、MaskRefinementService
│  ├─ Occlusion/                OcclusionService、BrushPainter
│  ├─ Lighting/                 ColorMatchingService
│  ├─ Shadow/                   ShadowService
│  ├─ ModelManagement/          ModelManifest、HubClient、FileDownloader、ModelManager、LicensePolicy
│  ├─ ProjectIO/                ProjectSerializer、ImageLoader
│  ├─ Inpainting/               IInpaintingService（第二階段，目前停用）、InpaintBlend
│  ├─ Diagnostics/              AppLog、PerformanceReport
│  └─ Services/                 CompositeServices（服務容器）、CompositeWorkflow（流程協調）
├─ AutoComposite.App/           WinForms 主程式（net8.0-windows、x64）
│  ├─ UI/                       MainForm、CompositeCanvas、MaskEditorForm、ModelManagerForm、WicImageCodec…
│  └─ ModelStore/manifests/     模型清單（複製到輸出的 ModelManifests\）
├─ AutoComposite.Tests/         xUnit 測試（77 個）＋ TestAssets 極小 ONNX 測試模型
└─ tools/make_test_models.py    產生測試模型的腳本
```

介面不直接做推論或合成計算：UI → `CompositeWorkflow` → 各服務介面（`ITransformService`、`IBackgroundRemovalService`、`IColorMatchingService`、`IShadowService`、`IOcclusionService`、`IAutoPlacementService`、`ICompositeRenderer`、`IInpaintingService`）。服務都可以替換。

## NuGet 套件

| 套件 | 版本 | 用途 | 授權 |
|---|---|---|---|
| Microsoft.ML.OnnxRuntime.Managed | 1.24.4 | Core：ONNX Runtime 受控 API | MIT |
| Microsoft.ML.OnnxRuntime.DirectML | 1.24.4 | App：原生執行檔（DirectML＋CPU 執行提供者） | MIT |
| Microsoft.NET.Test.Sdk / xunit / xunit.runner.visualstudio | 18.11.0 / 2.9.3 / 3.1.5 | 測試 | MIT / Apache-2.0 |
| Microsoft.ML.OnnxRuntime | 1.24.4 | 測試：CPU 原生執行檔 | MIT |

### 影像處理套件的選擇

依規格評估了 ImageSharp 和 OpenCvSharp，最後都沒用，影像處理自己寫：

- **ImageSharp**：NuGet 上 3.1.12 以前的版本（包括 Apache-2.0 授權的 2.1.x）都有未修補的高風險漏洞公告；3.x／4.x 改用 Six Labors Split License，年營收超過 100 萬美元的公司要付費。
- **OpenCvSharp**（Apache-2.0，PhotoEdit 主程式有用）：Linux 原生程式庫依賴舊版 Ubuntu 的套件，在 CI 和雲端無法執行測試。
- **結果**：核心改用**純受控程式碼**（RGBA8 影像、浮點遮罩、PNG、重採樣、模糊），任何平台都能測試。主程式用 **Windows Imaging Component**（透過 WPF 的 `System.Windows.Media.Imaging`）讀 JPEG／PNG／BMP／TIFF／GIF，系統裝了擴充元件時也能讀 WebP／HEIC。它會讀取 EXIF Orientation，JPEG 也由它輸出。

## AI 模型

模型不附在程式裡，也不放進 git。到「模型管理」逐一下載，存到程式資料夾的 `Models\`（不能寫入時改存 `%LOCALAPPDATA%\AutoComposite\Models`）：

```
Models/
├─ manifests/      安裝狀態 *.state.json（鎖定的 commit、檔案雜湊、授權同意紀錄、驗證結果）
├─ segmentation/   去背模型
├─ inpainting/     （第二階段）
└─ cache/
```

| 模型 | 倉庫 | 用途 | 狀態 |
|---|---|---|---|
| BiRefNet ToonOut | [sprited/birefnet-toonout-onnx](https://huggingface.co/sprited/birefnet-toonout-onnx) | 動漫人物去背（首選） | 可安裝 |
| BiRefNet | [onnx-community/BiRefNet-ONNX](https://huggingface.co/onnx-community/BiRefNet-ONNX) | 一般照片、真人（備援，FP32／FP16 兩種） | 可安裝 |
| SD 1.5 Inpainting | [stable-diffusion-v1-5/stable-diffusion-inpainting](https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-inpainting) | 局部修補 | 第二階段，只顯示說明 |
| Depth Anything V2 Small | [depth-anything/Depth-Anything-V2-Small](https://huggingface.co/depth-anything/Depth-Anything-V2-Small) | 深度估測 | 選配，未整合 |
| BRIA RMBG 2.0 | [briaai/RMBG-2.0](https://huggingface.co/briaai/RMBG-2.0) | 參考 | CC BY-NC 4.0，商用被擋 |

下載規則：

- 不猜檔名：先呼叫 Hub API（`/api/models/{repo}/revision/{rev}`、`/tree/{commit}?recursive=true`）取得 commit、模型卡授權與完整檔案清單，再依清單裡的萬用字元規則挑出主模型、外部權重（`.onnx_data`）和設定檔。
- 下載走 `/resolve/{commit}/{path}`：
  - 先寫到 `.part`，可以取消，網路中斷時用 Range 續傳，失敗會指數退避重試。
  - 完成後檢查大小、排除 Git LFS 指標檔，用 Hub 提供的 LFS SHA-256（小檔案用 Git blob SHA-1）驗證。驗證失敗的檔案會刪除。
- 首次安裝時把 commit 鎖在安裝狀態中，之後**不會自動升級**。啟動時不連網，可以離線使用。
- 載入時讀 ONNX metadata 與 opset，和清單規格比對輸入輸出名稱、形狀、型別，不相容就拒絕。DirectML 載入失敗或執行失敗時自動改用 CPU，並記錄原因。
- 需要同意條款的模型：設定環境變數 `HF_TOKEN`。

### 授權

安裝前顯示授權並依「預定用途」（非商業／商業）判斷：

- **非商業授權**（例如 CC BY-NC）在商業用途下直接拒絕安裝。
- **授權未標示、自訂授權、或模型卡和清單預期不同**時，使用者必須確認後才能安裝。
- **注意**：撰寫清單時這個環境連不到 huggingface.co，ToonOut 與 BiRefNet-ONNX 的授權（預期 MIT）**尚未對照模型卡核實**，所以清單把商用條件標成 `unknown`，安裝時會要求確認。請以模型卡上的授權全文為準。

## 合成流程

1. 載入背景與人物，並依 EXIF Orientation 轉正。
2. 去背：
   - 透明 PNG 直接用原圖 Alpha。
   - 其他圖片用 ONNX 模型：保留比例補邊到 1024×1024，ImageNet 正規化，輸出已經是機率時不重複套 sigmoid，最後還原成原圖尺寸。
3. 遮罩精修：黑白點、內縮外擴、移除碎塊、羽化、保留／移除筆刷。全程使用浮點遮罩，不二值化。
4. 定位：
   - 背景分析：天空區域成長法估計天空，找出天際線上頂部對著天空的平坦頂面（柱頂、屋頂）。
   - 人物分析：腳點、腳寬、頭部、軀幹主軸。分析時會排除細長的矛尖。
   - 人物錨點對齊背景目標錨點。目標可以是自動推薦，也可以手動點選或拖曳站立面。
   - 候選位置以規則評分：錨點誤差、超出畫面、超出站立面、腳下懸空、遮住保護區、被前景遮住過多、透視過強。
5. 透視：兩條參考線（例如石柱左右邊緣）推估垂直消失點和仰角收斂比例，並限制在 0.6～1.4。另外提供四角自由透視。預設不變形。
6. 遮擋：前景物件遮罩、圖層專屬遮擋遮罩，由使用者用筆刷指定，可以羽化。
7. 色彩：
   - **原始風格**：完全不改變人物。
   - **環境匹配**：取樣放置處附近的背景，只做小幅、有上限的亮度、色溫、對比、飽和度調整。
   - **自訂**。
8. 陰影：投射陰影（以腳點為支點投影到地面，離腳越遠越淡）、接觸陰影、環境遮蔽。可以限制陰影只落在非天空區域或自訂接收區域。
9. 合成：預乘 Alpha，人物 Alpha 為 0 的像素完全保留背景原值。邊緣會先去色污染，避免白邊和黑邊。
10. 預覽與輸出：預覽用代理解析度在背景執行緒計算；匯出以背景原尺寸、雙三次重採樣從原圖重新合成，存成 PNG 或 JPEG。
11. 專案 `*.acsp.json`：
    - 只記參數與路徑（專案資料夾內的檔案存相對路徑）。
    - 遮罩存成 `專案名_files\` 裡的 16 位元 PNG，不把影像轉 Base64 塞進 JSON。
    - 開啟時找不到的檔案可以重新指定位置。

## 建置與執行

需求：Windows 10/11 x64、.NET 8 SDK（或 Visual Studio 2022 17.8 以上）。

本機專案位置：`C:\專案\AutoComposite`。

```powershell
cd C:\專案\AutoComposite
dotnet build AutoComposite.sln -c Release
dotnet test AutoComposite.Tests
dotnet run --project AutoComposite.App -c Release
```

第一次使用：

1. 「模型管理」→ 選 BiRefNet ToonOut → 下載安裝。安裝完會自動測試載入。
2. 開啟背景 → 開啟人物 → 自動去背 → 自動定位 → 自動融合。
3. 在右側各分頁微調，最後匯出圖片。

畫布操作：

| 操作 | 方式 |
|---|---|
| 移動人物 | 拖曳 |
| 縮放檢視 | 滾輪 |
| 縮放人物 | Shift+滾輪 |
| 旋轉人物 | Ctrl+滾輪 |
| 平移檢視 | 中鍵或按住空白鍵拖曳 |
| 微調位置 | 方向鍵（Shift 加大） |
| 微調旋轉／縮放 | `[` `]` 旋轉、`+` `-` 縮放 |
| 復原／重做 | Ctrl+Z ／ Ctrl+Y |
| 儲存專案 | Ctrl+S |
| 匯出 | Ctrl+E |

在 Linux／macOS 上也能建置（App 專案設了 `EnableWindowsTargeting`），但需要包含 WindowsDesktop 目標的官方 .NET SDK。Ubuntu 套件庫的 `dotnet-sdk-8.0` 沒有這個目標，只能建置 Core 和 Tests。

## 測試結果

`dotnet test AutoComposite.Tests`：**77 個測試全部通過**（Linux、.NET 8.0.427、ONNX Runtime 1.24.4 CPU）。涵蓋：

- 透明 PNG 與不透明圖片輸入、各種長寬比的人物
- 遮罩輸出尺寸、Alpha 邊緣（不二值化、去色污染、移除碎塊）
- 平移／縮放／旋轉、透視（四點、仰角收斂、參考線消失點）
- Alpha 合成的逐像素正確性、遮擋遮罩、色彩調整與推薦、陰影與接收遮罩
- 專案存讀與遺失檔案重新指定、匯出、復原／重做
- 模型相關：缺少模型、下載中斷後續傳、SHA-256 錯誤、LFS 指標檔、授權阻擋、DirectML 不可用時改用 CPU
- 大圖（6000×4000）合成
- **陰影與修補都關閉時，人物以外的背景像素逐一與原圖相同**
- 柱頂偵測與自動站上柱頂（合成場景）

去背推論用三個極小的測試模型（sigmoid 輸出、logits 輸出、FP16 輸入輸出）驗證前後處理與介面。它們不是真的 AI 模型。

## 尚未完成與限制

- **主程式介面還沒在 Windows 上實際執行過**：這次的開發環境是 Linux 雲端容器，只確認了能編譯（0 錯誤、0 警告），以及核心邏輯的自動化測試通過。視窗版面、DirectML、WIC 解碼要在 Windows 上實測。
- **真正的 ToonOut／BiRefNet 模型沒有下載測試過**：開發環境連不到 huggingface.co。前處理依規格與模型卡描述實作，輸出啟動函數由程式依實際數值判斷。第一次安裝後請按「測試載入」確認輸入輸出規格，並實際去背檢查頭髮與長矛。
- **定位是單張照片的啟發式推薦，不是 3D 重建**：天際線分析適合「結構頂部對著天空」的照片。其他場景請手動點目標錨點、拖站立面或畫參考線。
- **光源方向只做粗略估計**（比較畫面左右亮度），需要手動調整陰影方向。
- **AI 局部修補**（Stable Diffusion Inpainting）是第二階段：介面與「只改遮罩內」的混合規則已經完成，ONNX 管線還沒整合，所以按鈕停用。
- **深度估測**（Depth Anything V2）未整合。
- 影像管線是 8 位元：16 位元 PNG／TIFF 會降成 8 位元，也不做色彩描述檔轉換（保留原始數值）。
- 效能沒有預先承諾。「輸出」分頁有每次預覽、去背、匯出的實際耗時報告。
