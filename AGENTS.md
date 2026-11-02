# 工作約定（AGENTS.md）

> 2026-11-01 全面重寫。工作區：`D:\BACKROOMS`。
> 這份只留「現在仍然有效」的規則；歷史事故、舊路徑、舊管線已移到
> `文档/报告/` 與 `备份/legacy-wiki-20261101.zip`。
>
> 有衝突時，優先順序：
> **使用者當下指示 → 本檔 → `文档/AGENT-接手.md` → `文档/要求文档.md` → 舊文件。**

---

## 1. 這個專案是什麼

《狐霖的後室 RPG｜Hulin Backrooms RPG》的**非官方 Wiki ＋ OCR 記錄器 ＋ 玩家客戶端**。

- 遊戲是 Unity WebGL，`#game` 是 `<canvas>`，DOM／無障礙樹讀不到內容，只能靠 OCR。
- 三條產品線：
  1. `wiki/`：內容與離線單檔網站。
  2. `记录器/`：OCR 記錄器與 staging 管線。
  3. `客户端/`：HulinRecorder 控制台＋HulinClient 遊戲客戶端，附帶 logs 記錄與可攜版。
- **不做自動玩**。遊戲作者不允許，任何 `auto/`、`quark/`、`sessions/`、自動玩入口都不要恢復。

---

## 2. 根目錄結構（現行）

```text
D:\BACKROOMS\
  AGENTS.md                 本檔：現行規則
  启动记录器.bat             啟動記錄器
  记录器\
    backroom-logger.ps1      OCR 記錄器本體
    structured\              管線唯一來源（parse／event／import／merge／tests）
    logs\                    各場次原始 logs（只讀）
    說明.md                  記錄器手冊
  客户端\
    HulinPlayer.cs           遊戲客戶端原始碼（client-only 由 HulinClient 啟動）
    HulinRecorder.cs            獨立控制台／記錄器面板原始碼
    app\                     可直接啟動的成品（HulinRecorder + HulinClient）
    dist\HulinExpedition\      可攜版成品（sync 目標）
    dist\HulinExpedition.zip   給別人的可攜版壓縮檔
    start.ps1／啟動.bat      複製到 %LOCALAPPDATA% 後啟動 HulinRecorder
    build.ps1                重新編譯 panel＋client
    postprocess.mjs          按「停止並整理」時執行
    volume-watch.ps1         音量記錄
    fonts\                   字型與字型圖集
  wiki\
    content\                 ★ Wiki 唯一內容來源
    site\index.html          建置產物（雙擊即開；不要手改）
    _incoming\               每次收工的 staging（不進正式庫）
    smoke.mjs                前端冒煙測試
    import-package.ps1       匯入別人的 .hulinlog.zip（預設只 staging）
    匯入紀錄.bat             拖 zip 的入口
  文档\                      AGENT-接手、要求文档、事件规范、背景设定、报告
  备份\                      legacy 壓縮檔與站點備份
```

已歸檔／移除：

- `wiki/_data`、`wiki/_drafts`、`wiki/_backup`、`wiki/_gen`、`wiki/_i18n`、
  `wiki/build-site.ps1`、`wiki/lib-*.ps1`、`wiki/sync-floor-colors.ps1`、`wiki/ui-smoke.mjs`、
  `wiki/README.md`、`wiki/ARCHITECTURE.md`、`文档/交接.md` → `备份/legacy-wiki-20261101.zip`。
- `wiki/頁面` → `备份/legacy-wiki-pages-20261101.zip`。
- `wiki/_design`、2026-10-29 的 failed-run 備份 → `备份/20261029-old-runs-and-design.zip`。
- `记录器/player/`（舊 HulinPlayer 副本與 135.8 MB 測試 profile）已移除；
  `客户端/build.ps1` 改用 `客户端/dist/HulinExpedition` 的 WebView2 DLL。

---

## 3. 資料流

```text
HulinRecorder（控制台）
  → HulinClient（WebView 遊戲）
  → 记录器/backroom-logger.ps1
  → 记录器/logs/backroom-<時間>/structured.jsonl
  → 记录器/structured（font-correct → clean-levels → parse／event-parse／input-bind
                       → event-import → import-wiki → validate → merge-final）
  → wiki/_incoming/<session>/            staging（預設不寫正式庫）
  → wiki/content/                       正式內容
  → node wiki/content/build.mjs
  → wiki/site/index.html
```

- `记录器/structured/` 是**管線唯一來源**。
- `客户端/app/structured/` 與 `客户端/dist/HulinExpedition/structured/` 是同步副本；
  **不要手改副本**，改來源後跑 `node 记录器/structured/sync.mjs`。
- `客户端/postprocess.mjs`、`volume-watch.ps1` 是管線的一部分，原始檔在 `客户端/`；
  `sync.mjs` 會把它們複製到 app／dist。
- 除錯覆蓋層：記錄器每幀寫 `记录器/logs/overlay.json`；HulinRecorder 讀它，畫透明區塊框
  與右側 OCR 讀取面板。框線只畫邊、滑鼠穿透，不遮遊戲；`--no-overlay` 可關。
- 事件分段：`event-parse.mjs` 會用「大空隙」（> max(2.5×行距, 50px)）切斷 intro／results；普通段落 35px 不會被切。沒有標題的事件不會自動生成。

---

## 4. 內容來源與 schema

`wiki/content/` 是 Wiki 唯一來源。詳細欄位見 `wiki/content/README.md`。

```text
characters/<角色>/character.json     角色
characters/_meta.json               順序、永久能力、狀態表
characters/_skills.json             戰鬥技能表
characters/_extra.json              角色額外資料
entities/<實體>/entity.json         實體
items/<分類>/<物品>/item.json       物品（weapon／armor／accessory／tool／consumable／ammo）
floors/<id-name>/floor.json         樓層資料與正文
floors/<id-name>/events.json        該層事件／岔路（唯一來源）
floors/<id-name>/endings.json       該層結局（唯一來源）
floors/<id-name>/design.css         該層專屬樣式
floors/_names.json                  樓層 id → 名稱
shops/<商店>/shop.json              商店
pages/pages.json                    關於頁等靜態頁
recipes/recipes.json                合成表
enhancements/enhancements.json      強化表
```

硬規則：

- **事件與結局只能放該樓層資料夾**；不要再用全域 `content/events.json` 或 `content/endings/`。
- 樓層 key 一律 `id|name`；`1/7/9` 共用 id，不要只用 id 判斷解鎖或配色。
- `_status: 'name-only' | 'id-only'` 只給「只有名字／只有編號」的占位樓層；有正文就不要留這個欄位。
- 樓層 `doc` 只放頁面會顯示的「環境」引言 HTML；不要混事件、商店、戰鬥流水。
- 不要把截圖放進 `wiki/content`；截圖只拿來讀字，讀完刪。
- 玩家帳號名 `狐天`／`狐夭` 不進正文，一律換成「你」；`天狐` 是遊戲角色，保留。
- 內容 JSON 不要寫入 `D:\BACKROOMS\...` 這類絕對路徑；用相對路徑或只留 session 名。

---

## 5. 指令

```powershell
# 建置網站（產物：wiki/site/index.html）
node D:\BACKROOMS\wiki\content\build.mjs

# 管線同步／檢查
node D:\BACKROOMS\记录器\structured\sync.mjs
node D:\BACKROOMS\记录器\structured\sync.mjs --check

# 完整驗收（目前 870 項；項數會變，以輸出 summary 為準）
node D:\BACKROOMS\记录器\structured\tests\verify-final.mjs

# 內容引用完整性（verify-final 也會跑；可單獨執行）
node D:\BACKROOMS\记录器\structured\validate-content.mjs

# 前端冒煙（改 template／前端必跑）
node D:\BACKROOMS\wiki\smoke.mjs

# 端到端 fixture
node D:\BACKROOMS\记录器\structured\tests\run-tests.mjs
```

- `verify-final` 的 `root` 由腳本位置推算，不再寫死 `D:/BACKROOMS`。
- `font.test` 與 `smoke` 使用系統暫存目錄，不往倉庫裡寫測試檔。
- 建置完成後用瀏覽器實際開 `wiki/site/index.html` 看一次；不要只看 exit code。

---

## 6. 目前狀態（2026-11-01）

> 數字會變；下面是這次交接時的實測，不是永久保證。要現在的值就重跑指令。

- 樓層：**93 層**（含 `name-only`／`id-only` 占位；以 `verify-final` 輸出為準）。
- 事件檔：17 個樓層有 `events.json`；結局檔：4 個。
- 角色 97、實體 71、物品 453、商店 17。
- 驗收：`verify-final` **870 / 870**（warnings 5）；`smoke` **9 / 9**；`sync --check` **80 項對齊**。
- 已知內容債：
  - 453 件物品中約 259 件沒有 `desc`、101 件沒有 `eff`（這次審計值）；**多數是使用者尚未截圖，先不要代寫**，等他自己補。
  - 116 處角色／實體／商店／配方引用找不到對應物品（含 OCR 雜訊與真缺條目）。
  - 43 個 JSON 仍含 `D:\BACKROOMS\...` 絕對路徑（多在 `_staging`／`_observations`）。
  - `steps` 回填未完成：wiki 仍有 61 條事件是單段格式。
  - `event-import` 的 `isFloorIntroLine` 已接進 results 清洗；仍需用真實場次驗證。
  - `212-發光深淵` 缺完整介紹；`14-冷漠病院`、`40-有害的衰敗` 缺配色，`1-停車區` 只有底色。
  - `Get-PageRect` 已改成遞迴找 `Chrome_RenderWidgetHostHWND`；crop 現在是真正的 render **1600×900**（以前只掃直接子視窗，失敗就退回整個視窗 1606×929，造成 3×26px 偏移與縮放）。
  - 1600×900 normal 已加專用分區：`title 9,0,656,66`、`main 20,80,1005,529`、`opts 20,609,1005,278`、`enemy 9,42,121,48`、`char 1030,66,565,84`。戰鬥時先 OCR 選項區判斷 `攻擊／技能／防禦／逃跑／右手…`，改用 `enemy 15,70,175,125`、`main 20,200,1005,409`（主區從敵人卡下方開始）。此尺寸不要開 AutoRegion：measured main 會量到 x≈990（實際面板右緣 1025）。
  - 管線路徑已集中到 `记录器/structured/paths.mjs`；新腳本不要再寫死 `D:/BACKROOMS`。
- 已做的清理：
  - 移除 `记录器/player/`（136.6 MB）。
  - 舊 Wiki 管線與舊 handover 壓進 `备份/legacy-wiki-20261101.zip`（約 7.9 MB）。
  - `verify-final`／`font.test`／`smoke` 已改為可攜、暫存目錄。
  - HulinRecorder／HulinClient 增加 OCR 覆蓋層：透明區塊框＋右側讀取面板；記錄器寫 `logs/overlay.json`，`--no-overlay` 可關；勾選框即時開關兩者。
  - 客戶端／記錄器生命週期已解耦：`HulinRecorder.exe` 是獨立控制台；`HulinClient.exe --client-only` 才是遊戲 client。關控制台只退出 panel、不關 client；「關閉客戶端」才送 WM_CLOSE。更新 `HulinRecorder.exe`／記錄器 script 不需要重開遊戲；只有更新 `HulinClient.exe` 才要重開遊戲。
  - `git` 已初始化；`.gitignore` 排除 logs、app／dist、字型、OCR binary、備份與 staging。

---

## 7. 硬規則

1. **不要關使用者正在用的程式或視窗。**
   - 禁止 `Stop-Process -Name`、`taskkill /IM`、`Get-Process xxx | Stop-Process`。
   - 只關自己 `Start-Process -PassThru` 記下 PID 的程序。
   - 不要對使用者的遊戲／瀏覽器 `bring_to_front`，也不要用會搶焦點的輸入。
   - 驗證用瀏覽器一律獨立 profile；不確定是不是使用者的程序 → 先問。

2. **不做自動玩。**
   - 不建立／恢復 `auto/`、`quark/`、`sessions/`、`Wiki探索者.exe` 等自動玩產物。

3. **正常匯入流程不依賴 AI。**
   - 規則、校驗、staging、確認流程必須能獨立跑。
   - AI 可以協助分析，但不能是正式匯入的必需步驟。

4. **不編造。**
   - 事件、路徑、戰鬥、結局、配色、物品效果都不能猜。
   - 不確定就進 `wiki/_incoming/<session>` staging，不覆蓋正式 content。
   - 低於 0.8 信心的候選不合併；沒有實際截圖就不生成配色。

5. **截圖是暫存素材。**
   - 只拿來讀文字；讀完刪。不要放進 `wiki/content` 或 `wiki/site`。
   - 只刪自己讀過的那幾張；不要用萬用字元掃整個資料夾。

6. **玩家名與角色名分開。**
   - `狐天`／`狐夭` 是玩家帳號名 → 正文換「你」。
   - `天狐`、`小猦`、`呐豆` 等是角色名 → 保留。
   - 不確定這一局操作的是哪個角色 → 先問。

7. **語言。**
   - 跟使用者對話：簡體中文。
   - Wiki 正文：繁體中文。
   - 專案文件與腳本註解：維持原本語言（多為繁中）。

8. **PowerShell 5.1 陷阱。**
   - 含中文的 `.ps1` 必須是 **UTF-8 with BOM**。
   - 不要用 PowerShell 對中文檔做多行字串替換；用 `read` + `edit` 或 node 腳本。
   - 不要用 `$pid`、`$home`、`$host` 等唯讀自動變數。
   - 需要跨幀保存狀態時用 `[pscustomobject]`，不要用 `[ordered]@{}` 查自訂 key。

9. **內容與產物分開。**
   - 只改 `wiki/content/`；不要直接改 `wiki/site/index.html`。
   - 重建一律跑 `node wiki/content/build.mjs`。

10. **管線只有一份來源。**
    - 只改 `记录器/structured/`；改完跑 `sync.mjs`。
    - 不要手改 `客户端/app/structured` 或 `客户端/dist/HulinExpedition/structured`。
    - `sync.mjs --check` 有漂移就 exit 1；漂移沒修好之前不要說「驗收全過」。

11. **事件／結局按樓層。**
    - 只寫 `floors/<樓層>/events.json`、`floors/<樓層>/endings.json`。
    - 多樓層共用的結局要拆到各層，不要用全域檔。
    - 事件瀏覽器只讀當前樓層；虛擬根節點不列入清單。

12. **樓層 key 與占位。**
    - key 一律 `id|name`；`1/7/9` 共用 id。
    - `name-only`／`id-only` 不要留 `events.json`，否則 `events:non-empty` 會掛。
    - 有正文的樓層不要留 `_status: 'name-only'`。

13. **原始 logs 只讀。**
    - `记录器/logs/backroom-*/` 的 `structured.jsonl`、`wiki.md`、`ocr-raw.log` 不要刪、不要覆蓋。
    - `levels/` 只允許 `clean-levels.mjs` 刪假樓層檔；`floor-shots/` 只允許配色腳本取樣後刪。
    - 要清舊場次，先確認沒有要回溯的出處行號，再問使用者。

14. **合併前備份。**
    - `merge-final` 預設 dry-run；`--confirm` 只在看過 `validation.json`／`merge-final-report.json` 後使用。
    - 沒有備份不要 `--confirm`／`--apply`。

15. **改完要驗。**
    - 內容：`build.mjs` → `smoke.mjs` → `verify-final`。
    - 管線：`sync.mjs` → `verify-final`。
    - 前端：`smoke.mjs`。
    - 沒實際跑過，不要說通過。

---

## 8. 修改流程

### 8.1 改 Wiki 內容

```text
改 wiki/content/**/*.json
→ node wiki/content/build.mjs
→ node wiki/smoke.mjs
→ node 记录器/structured/tests/verify-final.mjs
→ git diff／commit
```

### 8.2 改管線

```text
改 记录器/structured/*.mjs
→ node 记录器/structured/sync.mjs
→ node 记录器/structured/sync.mjs --check
→ node 记录器/structured/tests/verify-final.mjs
→ git diff／commit
```

### 8.3 改客戶端／記錄器腳本

```text
改 客户端/HulinRecorder.cs（控制台／OCR 浮層）或 HulinPlayer.cs（client-only 遊戲）
→ powershell -ExecutionPolicy Bypass -File 客户端/build.ps1
→ 用 start.ps1 重開 HulinRecorder；HulinClient 可保持開啟
```

- 只改 `HulinRecorder.exe` 或 `backroom-logger.ps1`：關掉控制台、重新 `start.ps1` 即可，**不用關遊戲**。
- 只有改到 `HulinPlayer.cs` / `HulinClient.exe`（WebView 遊戲本身）才需要關遊戲 client。
- `start.ps1` 會跳過被 client 鎖住的 exe／DLL；更新面板不會強關客戶端。

---

## 9. 清理、備份、版本控制

- `git` 已初始化（2026-11-01）。`.gitignore` 排除：
  `记录器/logs/`、`记录器/player/`、`记录器/structured/bin/`、`记录器/structured/*.otf`、
  `客户端/app/`、`客户端/dist/`、`客户端/fonts/`、`备份/`、`wiki/_incoming/`、
  `wiki/site/index.html`、`wiki/site/index.backup-before-content.html`。
- 內容變更用 commit 記錄；不要用「複製整個資料夾」當版本控制。
- `备份/legacy-wiki-20261101.zip` 是舊 Wiki 管線與舊 handover 的封存；不要沒事解開覆蓋現行檔案。
- `记录器/logs/` 是原始資料，體積大；要清舊場次先問，不要用批次刪除。
- `客户端/dist/HulinExpedition/` 是 sync 目標；`HulinExpedition.zip` 是給別人的成品。兩個都保留。
- `客户端/app/` 是 build.ps1 產物；要重新產生就跑 `build.ps1`。

---

## 10. 不要做

- 不要恢復自動玩。
- 不要用程序名稱批次關程式。
- 不要把截圖放進 Wiki。
- 不要寫玩家名（`狐天`／`狐夭`）；`天狐` 是角色。
- 不要直接改 `wiki/site/index.html`。
- 不要只改 `客户端/app/structured` 或 `dist/.../structured`（會被 sync 覆蓋）。
- 不要用全域 `content/events.json` 或 `content/endings/`。
- 不要自己生成樓層配色。
- 不要在沒有備份的情況下 `--confirm`／`--apply`。
- 不要在新程式裡寫死 `D:/BACKROOMS`；用 `import.meta.url` 或參數。
- 不要把 `记录器/logs`、`备份`、字型、OCR binary 加進 git。
