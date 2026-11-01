# 給下一個 Agent 的接手說明

更新：2026-11-01  
工作區：`D:\BACKROOMS`

> 先讀 `AGENTS.md`（現行規則）；這份只講「現在是什麼狀態、下一步做什麼」。
> 舊的 `交接.md`、舊 Wiki 管線、舊 handover 已封存在 `备份/legacy-wiki-20261101.zip`。

---

## 1. 現在是什麼狀態

- 專案：非官方 Wiki ＋ OCR 記錄器 ＋ FoyerPlayer 客戶端。
- 內容來源：`wiki/content/`；產物：`wiki/site/index.html`。
- 管線來源：`记录器/structured/`；同步副本：`客户端/app/structured`、`客户端/dist/FoyerRecorder/structured`。
- 版本控制：`git` 已初始化，`.gitignore` 已排除 logs／app／dist／字型／OCR binary／備份／staging。

目前實測（2026-11-01）：

```text
樓層：93（含 name-only／id-only 占位）
事件檔：17 層有 events.json
結局檔：4
角色：97　實體：71　物品：453　商店：17

node 记录器/structured/tests/verify-final.mjs  → 870 / 870（warnings 5）
node wiki/smoke.mjs                            → 9 / 9
node 记录器/structured/sync.mjs --check        → 80 項對齊
```

> 這些數字會變；要現在的值就重跑指令，不要拿這份當永久保證。

---

## 2. 這一輪做了什麼

1. **修掉管線漂移**：`event-parse.mjs` 的「多段事件」修正已同步到 `客户端/app` 與 `dist`。
2. **修掉 7 個樓層的過期 `_status`**：`0.1`、`2`、`399`、`4`、`499`、`595`、`63` 已有正文，不該再標 `name-only`／`id-only`。
3. **放寬單向路口**：`verify-final` 現在允許 1 條路徑的路口（有些場景只有一個前進選項），仍禁止 0 條或超過 4 條。
4. **測試可攜化**：
   - `verify-final.mjs` 的 `root` 改由 `import.meta.url` 推算，不再寫死 `D:/BACKROOMS`。
   - `font.test.mjs`、`wiki/smoke.mjs` 改用系統暫存目錄，不往倉庫寫測試檔。
5. **清理**：
   - 移除 `记录器/player/`（舊 FoyerPlayer 副本＋135.8 MB 測試 profile）。
   - `客户端/build.ps1` 改用 `客户端/dist/FoyerRecorder` 的 WebView2 DLL。
   - 舊 Wiki 管線（`_data`、`_drafts`、`_backup`、`_gen`、`_i18n`、`build-site.ps1`、`lib-*.ps1`、`ui-smoke.mjs`、舊 README／ARCHITECTURE）與舊 `交接.md` 壓進 `备份/legacy-wiki-20261101.zip`。
   - `wiki/_design` 與 2026-10-29 failed-run 備份壓進 `备份/20261029-old-runs-and-design.zip`。
   - `wiki/頁面` 舊 Markdown 頁壓進 `备份/legacy-wiki-pages-20261101.zip`。
6. **移除對舊 `_data`／`_i18n` 的依賴**：
   - `merge-final` 的新樓層名稱改讀 `floors/_names.json`。
   - `event-import` 的繁簡比對改讀 `记录器/structured/i18n-table.txt`。
   - `build-char-freq` 只讀 `wiki/content`。
7. **重寫 `AGENTS.md`**：只留現行規則、目錄、指令、狀態與禁區。
8. **OCR 覆蓋層（除錯）**：記錄器每幀寫 `记录器/logs/overlay.json`；FoyerPlayer 讀它，畫透明區塊框（title／enemy／main／opts／char）＋右側 OCR 讀取面板；框線只畫邊、滑鼠穿透，`--no-overlay`／`啟動.bat -NoOverlay` 可關。
9. **建立 git 基線**：第一個 commit 是清理後的可運作狀態。
10. **客戶端／記錄器生命週期解耦**：關閉客戶端只快速停記錄器、不跑整理；「停止記錄」只停記錄器、遊戲仍開；「停止並整理」才跑 postprocess；「結束程式」快速退出。開始記錄可在客戶端沒開時自動開啟客戶端。控制窗有「OCR 讀取」勾選框，可即時開關右側讀取面板。

---

## 3. 還沒做（接手優先）

### 3.1 內容引用完整性

`validate-content.mjs` 已建立並接進 `verify-final`，會檢查角色 `items[]`、實體 `drops[]`、商店 `it[]`、樓層 `info.items[]`、`recipes`／`enhancements` 的材料是否存在。目前缺條目是 warning，不擋建置。

這次審計約有 116 處懸空引用（角色、實體、商店、配方／強化加總），有些是 OCR 雜訊（`1 硬幣。`、`「手繪地圖」`），有些是真缺條目（`刮痕之門`、`母親的項鍊`、`特里普斯合金`）。另外有 43 個 JSON 仍帶 `D:\BACKROOMS\...` 絕對路徑。

### 3.2 多段事件回填

wiki 仍有 61 條事件是單段格式。`steps.mjs` 與 `event-parse` 已經能保留第二次以後的「你決定」，但需要重跑 19 個場次並覆蓋 `floors/*/events.json`。驗收樣本：「通風管和走廊」應拆成 3～4 段。

### 3.3 事件結果錯配

`event-import.mjs` 的 `isFloorIntroLine`（bigram 重疊 ≥ 0.7 丟棄）已接進 results 清洗：和樓層介紹／自身 intro 高度重疊的句子會丟掉。還需要拿真實場次驗證，尤其是多段事件。

### 3.4 樓層資料債

- `212-發光深淵` 只有碎片，介紹為空，等下次進該層重錄。
- `14-冷漠病院`、`40-有害的衰敗` 沒有配色；`1-停車區` 只有底色。
- 453 件物品中約 259 件沒有 `desc`、101 件沒有 `eff`；**多數是使用者尚未截圖，先不要代寫**，等他自己補。若要標記，用 `_stub` 或 `_status`，不要猜效果。
- 少數 `floor.json` 的 `_staging`／`_observations` 還留著 `D:\BACKROOMS\...` 絕對路徑；搬家前要清。
- `Get-PageRect` 已改成遞迴找 `Chrome_RenderWidgetHostHWND`；crop 現在是真正的 render **1600×900**（以前只掃直接子視窗，失敗就退回整個視窗 1606×929，造成 3×26px 偏移與縮放）。
- 1600×900 normal 已加專用分區：`title 9,0,656,66`、`main 20,80,1005,529`、`opts 20,609,1005,278`、`enemy 9,42,121,48`、`char 1030,66,565,84`。戰鬥時先 OCR 選項區判斷 `攻擊／技能／防禦／逃跑／右手…`，改用 `enemy 15,70,175,125`、`main 20,200,1005,409`（主區從敵人卡下方開始）。此尺寸不要開 AutoRegion：measured main 會量到 x≈990（實際面板右緣 1025）。

### 3.5 管線可攜性

路徑已集中到 `记录器/structured/paths.mjs`：從 `import.meta.url` 往上找含 `wiki/content` 的根目錄，統一匯出 `ROOT`／`WIKI`／`CONTENT`／`FLOORS`／`INCOMING`／`LOGS`。`event-import`、`parse`、`event-parse`、`validate`、`apply-wiki`、`run`、`merge-final` 都已改用；`postprocess` 也改成往上找 repo。新腳本不要再寫死 `D:/BACKROOMS`。

### 3.6 前端與工程衛生

- `template.html` 2.44 MB，產物 2.93 MB；建議拆成 `template/` 局部檔再拼成單檔，加 size budget 與 CSS／JS lint。
- 沒有 CI／pre-commit；至少做一個 `check.ps1` 或 `package.json` scripts，把 build／verify／smoke／sync 串起來。
- `客户端/dist/FoyerRecorder.zip` 約 169 MB；若不需要常備成品，可只留 `dist/FoyerRecorder/`，用打包腳本重新產生 zip。
- `记录器/logs/` 約 633 MB；要清舊場次先確認出處行號，再問使用者。

---

## 4. 常用指令

```powershell
# 建置
node D:\BACKROOMS\wiki\content\build.mjs

# 管線同步
node D:\BACKROOMS\记录器\structured\sync.mjs
node D:\BACKROOMS\记录器\structured\sync.mjs --check

# 驗收
node D:\BACKROOMS\记录器\structured\tests\verify-final.mjs
node D:\BACKROOMS\记录器\structured\validate-content.mjs
node D:\BACKROOMS\wiki\smoke.mjs
node D:\BACKROOMS\记录器\structured\tests\run-tests.mjs
```

---

## 5. 不要做

- 不要恢復自動玩。
- 不要用程序名稱批次關程式；只關自己記下 PID 的程序。
- 不要把截圖放進 Wiki。
- 不要寫玩家名（`狐天`／`狐夭`）；`天狐` 是角色。
- 不要直接改 `wiki/site/index.html`。
- 不要手改 `客户端/app/structured` 或 `dist/.../structured`；改來源再 sync。
- 不要用全域 `content/events.json` 或 `content/endings/`。
- 不要自己生成樓層配色。
- 不要在沒有備份的情況下 `--confirm`／`--apply`。
- 不要在新程式裡寫死 `D:/BACKROOMS`。
