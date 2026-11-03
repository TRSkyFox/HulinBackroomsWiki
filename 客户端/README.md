# 狐霖的後室 RPG — ExpeditionRecorder ＋ ExpeditionClient

這個資料夾是 Windows 控制台／WebView2 客戶端與 OCR 記錄器。

- **ExpeditionRecorder.exe**：控制台、OCR 浮層、整理流程。
- **ExpeditionClient.exe**：只負責 WebView2 遊戲視窗。

兩者是獨立 process。更新面板或 `backroom-logger.ps1` 不需要關遊戲，只要重開控制台。

---

## 啟動

雙擊專案根目錄的：

```text
启动记录器.bat
```

或直接跑：

```text
客户端\啟動.bat
```

`start.ps1` 會：

1. 把 `客户端` 的執行檔、DLL、`structured` 同步副本複製到 `%LOCALAPPDATA%\Expedition\app`；
2. 從 `客户端\Recorder` 直接覆蓋 `backroom-logger.ps1`、`palette-from-shots.ps1`，避免舊副本；
3. 把 `app\logs` 接成 `客户端\Recorder\logs` 的 junction；
4. 啟動本機那份 `ExpeditionRecorder.exe`；
5. 控制台再啟動 `ExpeditionClient.exe`；遊戲視窗就緒後才開始記錄。

客戶端已經開著時，控制台會接上現有視窗，不會再開第二個客戶端。瀏覽器開著同一個遊戲頁面不會被誤認。

更新面板時如果客戶端還在跑，`start.ps1` 會跳過被鎖住的 `ExpeditionClient.exe`／WebView DLL，只更新面板；之後重開控制台即可。

---

## 停止／生命週期

- **關閉控制台（X）**：只關 `ExpeditionRecorder.exe`；客戶端繼續跑。重新開控制台會接上現有客戶端。
- **停止記錄**：只停記錄器，客戶端繼續；再按「開始記錄」會續錄同一場。
- **停止並整理**：停止記錄器並跑 `客户端\Recorder\structured\run.mjs`；不會關客戶端。
- **關閉客戶端**：才關遊戲視窗；記錄器在背景快速停止，不跑整理。

---

## 控制窗口

- **開啟／關閉客戶端**：啟動或關閉 `ExpeditionClient.exe`。
- **開始／停止記錄**：只控制記錄器。
- **OCR 浮層**：透明框線＋右側 OCR 讀取面板；面板可拖曳調整寬度。
- **停止並整理**：跑 parse／event／import／validate／merge／build。
- **開啟 logs**

手動截圖是暫存素材；確認文字已轉錄後請自行刪除，不要放進 Wiki。

---

## OCR 覆蓋層（除錯用）

啟動客戶端後會多兩個不影響操作視窗：

- **透明框線**：畫出這一幀實際使用的 `title／enemy／main／opts／char` 區塊，只畫邊、滑鼠可穿透。
- **OCR 讀取面板**：顯示 frame、區塊座標、OCR 行、`measured` 自動校正邊界。
- 虛線橘框 = 自動量到的邊界；實線 = 真正送進 OCR 的邊界。

記錄器每幀寫 `客户端\Recorder\logs\overlay.json`，這是除錯資料。

關閉覆蓋層：

```text
啟動.bat -NoOverlay
```

---

## 結構化管線

```text
客户端\Recorder\logs\backroom-<時間>\structured.jsonl
  → 客户端\Recorder\structured\run.mjs
  → parse / event-parse / input-bind
  → event-import
  → import-wiki → wiki\_incoming\<session>\
  → validate → merge-final
  → wiki\content\ → build.mjs → wiki\site\index.html
```

- `客户端\Recorder\structured` 是**唯一來源**。
- `客户端\structured`、`客户端\dist\Expedition\structured` 是 `sync.mjs` 的同步副本，不要手改。
- 改完管線：

```powershell
node 客户端\Recorder\structured\sync.mjs
node 客户端\Recorder\structured\sync.mjs --check
```

- 事件只寫各樓層的 `floors\<id-name>\events.json`；判定結果與事件邊界由 `event-extract.mjs`／`event-parse.mjs` 處理。

---

## 給別人的可攜版

傳：

```text
客户端\dist\Expedition.zip
```

對方解壓後雙擊裡面的 `啟動記錄器.bat`。不需要安裝 Node，可攜版已內建 `node.exe`。

Wiki 主人收到 `.expedition.zip` 後，拖到：

```text
wiki\匯入紀錄.bat
```

預設只展開、驗證、產生 staging，不會自動合併。確認後再跑：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\BACKROOMS\wiki\import-package.ps1 -Staging <展開後的資料夾> -Apply
```

---

## Logs 位置

```text
D:\BACKROOMS\客户端\Recorder\logs\backroom-YYYYMMDD-HHMMSS\
```

`%LOCALAPPDATA%\Expedition\app\logs` 是指向這裡的 junction。

| 檔案 | 內容 |
|---|---|
| `wiki.md` | 逐字稿：樓層／旁白／決定／選項／檢定／狀態／道具 |
| `wiki.jsonl` | 同事件流，一行一事件 |
| `structured.jsonl` | OCR 結構化幀，管線輸入 |
| `ocr-raw.log` | 未過濾 OCR 全文 |
| `levels\NN-樓層名.log` | 依樓層拆檔 |
| `floor-colors.log` | 各樓層介面配色 |
| `logger.log` | 記錄器運行記錄 |

原始 logs 只讀；要清舊場次先確認沒有要回溯的出處行號。

---

## 參數

```powershell
啟動.bat -NoRecord                 # 只開遊戲，不錄
啟動.bat -NoOverlay                # 不顯示 OCR 覆蓋層／讀取面板
啟動.bat -Preset 1600x900          # 1280x720 / 1600x900 / 1920x1080
啟動.bat -Url https://...          # 換測試網址
```

---

## 開發

```powershell
powershell -ExecutionPolicy Bypass -File 客户端\build.ps1
```

`build.ps1` 會從 `ExpeditionPlayer.cs` 重新編譯，並把 WebView2、記錄器腳本放進 `客户端` 與 sync 目標。

- `ExpeditionPlayer.cs`：WebView2 客戶端＋啟動記錄器
- `ExpeditionRecorder.cs`：控制台／OCR 浮層
- `Recorder\backroom-logger.ps1`：OCR 記錄器唯一來源
- `Recorder\structured\`：管線唯一來源
- `start.ps1`／`啟動.bat`：複製到 `%LOCALAPPDATA%\Expedition\app` 並啟動
