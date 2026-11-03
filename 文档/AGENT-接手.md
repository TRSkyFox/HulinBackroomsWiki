# 給下一個 Agent 的接手說明

更新：2026-11-03 14:35  
工作區：`D:\BACKROOMS`

> 先讀 `AGENTS.md`（現行硬規則）。這份只講「現在是什麼狀態、最近改了什麼、下一步做什麼」。
> 舊交接、舊 Wiki 管線已封存在 `备份/legacy-wiki-20261101.zip`。

---

## 1. 交接摘要

- 專案：非官方 Wiki ＋ OCR 記錄器 ＋ ExpeditionRecorder／ExpeditionClient。
- Wiki 內容來源：`wiki/content/`；產物：`wiki/site/index.html`。
- 管線唯一來源：`客户端/Recorder/structured/`。
- app 同步副本：`客户端/structured/`；可攜版同步副本：`客户端/dist/Expedition/structured/`。
- 原始 logs：`客户端/Recorder/logs/`，只讀。
- 版本控制：git 工作樹目前乾淨。

目前實測：

```text
樓層：96（含 name-only／id-only 占位）
角色：98
實體：71
物品：455
商店：17
有 events.json 的樓層：13
結局條目：3（build 統計；不是獨立 endings.json 檔）

verify-final   824 / 824，failed 0，warnings 5
smoke           7 / 7
sync --check    82 項對齊
```

> 數字會變；要現在的值就重跑指令，不要拿這份當永久保證。

---

## 2. 最近完成（2026-11-03）

### 2.1 記錄器不再誤認瀏覽器

- [ExpeditionRecorder.cs](D:/BACKROOMS/客户端/ExpeditionRecorder.cs)
  - 標題兜底只接受同安裝資料夾的 client。
  - 明確排除 Chrome、Edge、Firefox、Brave、Opera、Vivaldi、360、QQ、Sogou、Liebao、Maxthon、UC、2345、`msedgewebview2`。
- [backroom-logger.ps1](D:/BACKROOMS/客户端/Recorder/backroom-logger.ps1)
  - 收到 `-Hwnd` 時檢查視窗擁有程序，瀏覽器直接拒絕。
  - 不再自動掃全機瀏覽器視窗。
  - 視窗消失時只等待，不再重新鎖定到其他瀏覽器。
- 實測：
  - Firefox 視窗 → `exit 1`，拒絕記錄。
  - 目前 `ExpeditionClient` 視窗 → 正常 OCR。

### 2.2 事件管線改為「全部候選可見」

- [event-extract.mjs](D:/BACKROOMS/客户端/Recorder/structured/event-extract.mjs)
  - 以 `【事件標題】` 為邊界，不再把上一事件結果帶進下一事件。
  - 只取目前投票區的選項，不把箭頭、感知提示、道具標籤當主選項。
  - `狐天`／`狐夭` 轉「你」；`天狐` 是角色，保留。
  - 同一事件的多個選擇保留 steps。
- [event-import.mjs](D:/BACKROOMS/客户端/Recorder/structured/event-import.mjs)
  - 所有事件候選都寫到 `structured/events-review.jsonl`，不再無聲消失。
  - 正式 `events.json` 只收通過的；review 會留下 accepted／reason。
  - 最新真實場次：26 個候選、19 個通過、7 個在 review（多為 clean-rejected）。
- [merge-final.mjs](D:/BACKROOMS/客户端/Recorder/structured/merge-final.mjs)
  - 樓層 doc 截斷防線：沒有句末標點的新介紹不覆蓋正式 doc。
  - 最新 dry-run 實際擋下 `廢棄辦公室` 的截斷 staging，正式頁保持完整。

### 2.3 樓層名稱與 12／60 資料

- 從 legacy archive 恢復 14 個 `▒▒▒▒▒` 名稱：
  `0.22 全面翻修`、`109 噪音污染`、`12 血肉之屋`、`15 未來感的走廊`、`161 阿帕拉的荒涼`、`20 物資倉庫`、`271 卡斯托羅薩斯的巢穴`、`28 閣樓`、`288 熵`、`398 慘無人道的惡作劇`、`399 霓虹天堂`、`595 綠洲`、`60 盡力彌補`、`63 平靜之雲`。
- 剩 9 層只有編號、沒有可恢復名字，保留 `id-only`，不要編造：
  `0.41、1.1、154.1、188、21、372、399.1、6.2、87`。
- `12-血肉之屋`、`60-盡力彌補` 已恢復正文、配色與專屬 `design.css`。

### 2.4 新角色

- `狐玥`（雪國來的白狐）
  - 角色：`wiki/content/characters/狐玥/character.json`
  - 裝備：`印弩`、`祀衣`
  - 已寫入 `_meta.json` 排序與 Wiki。

### 2.5 Expedition Archive 路徑錯誤

- 錯誤堆棧指向 `Path.Combine`，根因是 [Expedition Archive.bat](D:/BACKROOMS/Expedition%20Archive.bat) 的：

```bat
--repo "%~dp0"
```

`%~dp0` 結尾是反斜線，會把結尾引號吃掉，變成 `D:\BACKROOMS"`。  
`Path.Combine` 因此拋「路徑中具有非法字符」。

- 已修：
  - bat 啟動前去掉尾反斜線。
  - C# 再清理 `--repo` 的引號與尾斜線，舊快捷方式也能用。
  - Archive 增加全域例外記錄：`%TEMP%\ExpeditionArchive-error.log`。
  - CLIXML 錯誤改成可讀文字。
  - 已重新編譯 `tools/ExpeditionArchive.exe`。
- **注意：目前若還開著舊 Archive 視窗，要關掉重開**，舊程序仍是舊版。

### 2.6 專案文件／腳本整理

- `AGENTS.md` 根目錄、統計、指令已更新。
- `客户端/README.md` 改成現行 `start.ps1`／`run.mjs` 流程。
- `start.ps1` 會直接從 `Recorder` 覆蓋 app 的 `backroom-logger.ps1`、`palette-from-shots.ps1`。
- `sync.mjs` 不再把合法的 `客户端/structured` 誤報成舊副本。
- 新增 [2026-11-03-项目整理与优化.md](D:/BACKROOMS/文档/报告/2026-11-03-项目整理与优化.md)。

---

## 3. 目前待處理

### 3.1 已展開、尚未套用的記錄包

`Expedition-backroom-20261103-130944.expedition.zip` 已經展開驗證：

```text
wiki/_incoming/import-Expedition-backroom-20261103-130944.expedition-20261103-141918
validation: 0 errors / 20 warnings
floors: 5
entities: 13
```

目前**沒有寫入正式 Wiki**。  
若確認要收，請用新任一個 Archive：

- 點「整合進 Wiki」；
- 或跑：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\BACKROOMS\wiki\import-package.ps1 -Staging "D:\BACKROOMS\wiki\_incoming\import-Expedition-backroom-20261103-130944.expedition-20261103-141918" -Apply
```

不要重複展開同一個 zip；package SHA256 會擋。

### 3.2 事件覆蓋率

最新真實場次：

```text
raw 事件觀察：138
去重後候選：26
通過 event-import：19
review：26（7 個有原因）
正式 events.json：18（該場次）
```

review 檔：

```text
<session>\structured\events-review.jsonl
```

之後若要提升覆蓋率，先看 review 的 `clean-rejected` 原因，不要直接放寬全部。

### 3.3 內容債

- 455 件物品中約 261 件沒有 `desc`。
- 116 處角色／實體／商店／配方引用找不到物品。
- 54 個 JSON 仍有 `D:\BACKROOMS\...` 絕對路徑（多在 `_staging`／`_observations`）。
- 9 個 `id-only` 樓層沒有真實名字。
- `212-發光深淵` 缺完整介紹；`14-冷漠病院`、`40-有害的衰敗` 缺配色；`1-停車區` 只有底色。
- `0.2`、`11` 等舊樓層介紹仍有來源截斷；新的 merge gate 已避免再被截短。

**不要代寫物品效果、事件結果、樓層名字。** 沒有真實來源就留 staging／占位。

### 3.4 空間與清理

- `客户端/dist` 約 825 MB，`Expedition.zip` 約 315 MB；是正常成品，不要為省空間刪 DLL／OCR 模型。
- `备份` 約 172 MB；可移到其他磁碟。
- `wiki/_incoming` 目前有 3 個 staging（其中 1 個是上述待套用包）。
- `客户端/Recorder/logs` 約 24 MB，原始 logs，只讀。

---

## 4. 現行資料流

```text
ExpeditionRecorder
  → ExpeditionClient（WebView2）
  → 客户端/Recorder/backroom-logger.ps1
  → 客户端/Recorder/logs/backroom-<時間>/structured.jsonl
  → 客户端/Recorder/structured/run.mjs
      → parse / event-parse / input-bind
      → event-import（events.staging.json + events-review.jsonl）
      → import-wiki → wiki/_incoming/<session>/
      → validate → merge-final
      → wiki/content/ → build.mjs → wiki/site/index.html
```

- 可攜版（玩家）：`run.mjs --portable`，只產生 `.expedition.zip`，不碰正式 Wiki。
- 本機 Wiki 主人：預設 staging；只有明確 `--auto`／`--apply-events`／`--merge` 才寫正式。
- 事件正式檔：各樓層 `floors/<id-name>/events.json`。
- 事件 review：`structured/events-review.jsonl`，不是正式內容。

---

## 5. 常用指令

```powershell
# 建置網站
node D:\BACKROOMS\wiki\content\build.mjs

# 前端冒煙
node D:\BACKROOMS\wiki\smoke.mjs

# 管線同步
node D:\BACKROOMS\客户端\Recorder\structured\sync.mjs
node D:\BACKROOMS\客户端\Recorder\structured\sync.mjs --check

# 完整驗收
node D:\BACKROOMS\客户端\Recorder\structured\tests\verify-final.mjs

# 內容引用
node D:\BACKROOMS\客户端\Recorder\structured\validate-content.mjs

# 端到端 fixture
node D:\BACKROOMS\客户端\Recorder\structured\tests\run-tests.mjs
```

---

## 6. 重要設計／雷區

- 只改 `客户端/Recorder/structured/`；改完跑 `sync.mjs`。
- 不要手改 `客户端/structured/` 或 `dist/.../structured/`。
- `start.ps1` 會把 `客户端`（排除 `Recorder`、`dist`、`logs`）複製到 `%LOCALAPPDATA%\Expedition\app`；再從 `Recorder` 直接覆蓋記錄器腳本。
- `start.ps1` 會跳過被鎖住的 exe／DLL；更新 `ExpeditionRecorder.exe` 不需要關遊戲 client。
- 事件候選要留 review，不要為了整齊而整批丟掉。
- 樓層介紹截斷時，正式 doc 優先保留；staging 看得到即可。
- 瀏覽器視窗一律不能當遊戲來源；不要把「標題像遊戲」當唯一判準。
- `Expedition Archive.bat` 的 `--repo` 不可以傳尾反斜線；C# 會清，但 bat 也已經修。
- 原始 logs 不要刪、不要覆蓋；`backup` 不要進 git。
- 不要寫玩家名 `狐天`／`狐夭`；`天狐` 是角色，保留。

---

## 7. 接手第一步

1. 先跑一次：

```powershell
node D:\BACKROOMS\客户端\Recorder\structured\tests\verify-final.mjs
node D:\BACKROOMS\wiki\smoke.mjs
node D:\BACKROOMS\客户端\Recorder\structured\sync.mjs --check
```

2. 確認 `git status` 乾淨，沒有把 `dist`、logs、備份加進 git。
3. 如果要收 `130944` 包，先看 staging 的 `validation.json`／`merge-final-report.json`，再決定套用。
4. 如果要改事件規則，先跑一場真實 log，對照 `events-review.jsonl`，不要用手寫 fixture 猜。
