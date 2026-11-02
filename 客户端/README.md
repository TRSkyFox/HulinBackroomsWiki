# 狐霖的後室 RPG — 自帶 Edge 的遊戲客戶端 ＋ logs 記錄器

這個資料夾分成兩個程式：

- **ExpeditionRecorder.exe**：記錄器控制台、OCR 浮層、整理流程。
- **ExpeditionClient.exe**：只負責 WebView2 遊戲視窗。

兩個是獨立 process。更新面板／記錄器 script 不需要重開遊戲，只要重開控制台。

## 啟動

雙擊：

```text
啟動.bat
```

它會：

1. 把 `app\` 複製到 `%LOCALAPPDATA%\Expedition\app\`
2. 把 `app\logs` 接成 `D:\BACKROOMS\记录器\logs` 的 junction
3. 從本機那份啟動 `ExpeditionRecorder.exe`
4. 控制台再啟動 `ExpeditionClient.exe`，遊戲載入後自動開始記錄

> 更新面板時如果客戶端還在跑，`start.ps1` 會跳過被鎖住的 `ExpeditionClient.exe`／WebView DLL，只更新面板；之後重開控制台即可。

## 停止／生命週期

客戶端和記錄器是分開的 process：

- **關閉控制台（X）**：只關 `ExpeditionRecorder.exe`；客戶端繼續跑。重新開控制台會自動接上現有客戶端。
- **停止記錄**：只停記錄器，客戶端繼續；再按「開始記錄」會續錄同一場。
- **停止並整理**：停止記錄器並跑 `postprocess.mjs`；**不會關客戶端**。
- **關閉客戶端**：才關遊戲視窗；記錄器在背景快速停止，**不跑整理**。

要整理剛才那一場，客戶端關掉後也可以直接按「停止並整理」。

## 控制窗口

啟動後會開一個小視窗：

```text
狐霖記錄器控制
```

- **開啟／關閉客戶端**：啟動或關閉 `ExpeditionClient.exe`。關閉客戶端不會跑整理。
- **開始／停止記錄**：只控制記錄器。停止後遊戲仍開著，可續錄同一場。
- **OCR 浮層**：同時開關遊戲畫面的透明框線＋右側 OCR 讀取面板；讀取面板可拖曳調整寬度。
- **停止並整理**：停止記錄器並跑整理流程；不會關客戶端。整理時下方有進度條與目前階段。
- **開啟 logs**

手動截圖是暫存素材；確認文字已轉錄後請自行刪除，不要放進 Wiki。

## OCR 覆蓋層（除錯用）

啟動客戶端後，會多兩個**不影響操作**的視窗：

- **透明框線**：浮在遊戲畫面上，畫出記錄器這一幀實際使用的
  `title／enemy／main／opts／char` 區塊（只畫線、不填色，滑鼠可穿透）。
- **OCR 讀取面板**：放在遊戲視窗右側（右邊放不下就放左側，兩邊都放不下就收起），
  顯示目前 frame、區塊座標、OCR 行、`measured` 自動校正邊界。
- 虛線橘框 = 自動校正量到的邊界；實線 = 這一幀真正送進 OCR 的邊界。

記錄器每幀會寫 `记录器\logs\overlay.json`；這是除錯資料，不進正式 Wiki。
不要看覆蓋層時，啟動時加 `-NoOverlay`：

```text
啟動.bat -NoOverlay
```

也可以直接給 ExpeditionPlayer：

```text
ExpeditionRecorder.exe --no-overlay
```

## 整理（clean\）

按「停止並整理」時才會執行 `postprocess.mjs`，把原始事件再拆成：

```text
clean\session.json
clean\run-01\0-大廳.md
clean\run-02\0-大廳.md
clean\run-02\13-玻璃之狂.md
...
```

- 一局一個 `run-NN`：用「開始冒險」與結局切開，**不會把兩局混在一起**
- 一層一個檔案
- 只認 `Level N「名稱」` 當樓層；`第 N 層` 是場景／區域深度，不會亂切樓層
- 原始 `levels\`、`wiki.jsonl` 仍保留，方便回溯

## 結構化 staging（按「停止並整理」後）

按「停止並整理」後，若該場有 `structured.jsonl`，`postprocess.mjs` 會再執行：

```text
记录器\structured\run.mjs --auto
  → parse／event-parse／input-bind
  → event-import（乾淨事件可自動寫正式 events.json；其餘留 staging）
  → import-wiki.mjs → D:\BACKROOMS\wiki\_incoming\<session>\
  → validate.mjs → merge-final.mjs（只合併高信心樓層／配色）
  → build.mjs（重建站點）
```

**自動匯入規則：** 高信心樓層／配色自動寫正式 Wiki；低信心或 validation error 只留 staging；
事件會先用固定區域邊界清洗，通過門閘的乾淨事件自動寫 `events.json`，其餘留 staging。收工時看這四個檔：

```text
wiki\_incoming\<session>\validation.json
wiki\_incoming\<session>\events-review.jsonl
wiki\_incoming\<session>\merge-final-report.json
wiki\_incoming\<session>\auto-import.json
```

事件要人工確認後才寫入：

```powershell
node D:\BACKROOMS\记录器\structured\event-import.mjs <session> --apply
node D:\BACKROOMS\wiki\content\build.mjs
```

> `记录器\structured` 是管線唯一來源；`app\structured` 是同步副本。
> 改程式請改 `记录器\structured`，再跑 `node 记录器\structured\sync.mjs`。

## 給別人的可攜版

要傳給別人時，傳這個壓縮檔：

```text
D:\BACKROOMS\客户端\dist\Expedition.zip
```

對方解壓後雙擊：

```text
啟動記錄器.bat
```

- 不需要 Wiki
- 不需要安裝 Node（已內建 `node.exe`）
- logs 存在 `%LOCALAPPDATA%\Expedition\logs`
- 關閉遊戲後，紀錄包會出現在：

```text
%LOCALAPPDATA%\Expedition\exports\Expedition-<session>.expedition.zip
```

對方把 `.expedition.zip` 傳回來即可。

Wiki 主人收到後，把 zip 拖到：

```text
D:\BACKROOMS\wiki\匯入紀錄.bat
```

它只會**展開＋驗證＋產生審查報告**，不會自動合併。確認後再跑：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\BACKROOMS\wiki\import-package.ps1 -Staging <展開後的資料夾> -Apply
```

才會寫入正式內容並重建 Wiki。

## Logs 位置

```text
D:\BACKROOMS\记录器\logs\backroom-YYYYMMDD-HHMMSS\
```

`%LOCALAPPDATA%\Expedition\app\logs` 是指向這裡的 junction，所以從哪邊開都看到同一批 logs。

每場一個資料夾：

| 檔案 | 內容 |
|---|---|
| `wiki.md` | 主要逐字稿：樓層／場景／旁白／決定／選項／檢定／狀態／道具／隊伍 |
| `wiki.jsonl` | 同事件流，一行一事件，有型別 |
| `ocr-raw.log` | 未過濾 OCR 全文（每幀，含區塊標記） |
| `levels\NN-樓層名.log` | **依樓層拆檔**，只放該層事件 |
| `_transitions.log` | 跨樓層／轉場／岔路／樓層結算 |
| `floor-colors.log` | 各樓層介面配色 |
| `logger.log` | 記錄器自己的運行記錄 |

## 事件格式（wiki.md）

```markdown
## Level 0「大廳」
<!-- color:#302810 -->

### 〖開場的鈴聲〗
@ 08:10:37
N 單調的黃色壁紙、潮濕的地毯……
D 你決定：「往傳來「叮——」的一聲的方向走」
O 跟著鈴聲的節奏，帶你往前走（魅力 難度11）｜捂住耳朵，快走
Q 隊友：跟我走！
C 魅力 難度11 你 · 魅力檢定 · 難度 11
R 魅力 難度11 你 5+5=10 失敗
S 你：理智 -2、口渴 +1
I 你：得到「急救毯」
```

| 標記 | 意思 |
|---|---|
| `##` / `###` | 樓層／場景 |
| `@` | 時間（跨分鐘或間隔 >15 秒才標） |
| `N` | 旁白 |
| `D` | 決定 |
| `O` | 選項（含檢定難度） |
| `R` | 擲骰結果 |
| `C` | 檢定宣告 |
| `S` | 狀態變化 |
| `I` | 道具 |
| `Q` | 隊伍頻道 |
| `>` | 記錄中斷 |

- 畫面出現過的文字才會被記錄；DOM 讀不到，遊戲是 Unity WebGL canvas，所以用 Windows OCR。
- 同一段旁白合併、重複句去重、介面按鈕／角色卡不記。
- 截圖不落地。

## 參數

```powershell
啟動.bat -NoRecord                 # 只開遊戲，不錄
啟動.bat -NoOverlay                # 不顯示 OCR 覆蓋層／讀取面板
啟動.bat -Preset 1600x900          # 1280x720 / 1600x900 / 1920x1080
啟動.bat -Url https://...          # 換測試網址
```

## 開發

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

`build.ps1` 會從 `ExpeditionPlayer.cs` 重新編譯，並把 WebView2、`backroom-logger.ps1` 放進 `app\`。

- `ExpeditionPlayer.cs`：WebView2 客戶端＋啟動記錄器
- `app\`：可直接複製到本機執行的成品
- `start.ps1`／`啟動.bat`：複製到本機並啟動
## 音量記錄

客戶端啟動時會另外啟動 `volume-watch.ps1`，每 400ms 讀一次 Windows 預設輸出裝置：

- 主音量百分比
- 是否靜音
- 輸出裝置 ID

有變化就寫入目前場次：

```text
logs\backroom-...\volume.jsonl
```

如果尚未建立場次，先寫到 app 目錄的 `logs\volume.jsonl`。
