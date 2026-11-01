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
8. **建立 git 基線**：第一個 commit 是清理後的可運作狀態。

---

## 3. 還沒做（接手優先）

### 3.1 內容引用完整性

目前沒有 build-time 檢查以下東西：

- 角色 `items[]` 是否存在於物品庫。
- 實體 `drops[]` 是否存在。
- 商店 `it[]` 的商品是否存在。
- 樓層 `info.items[]` 是否存在。
- `recipes`／`enhancements` 的材料是否存在。

這次審計約有 116 處懸空引用（角色、實體、商店、配方／強化加總），有些是 OCR 雜訊（`1 硬幣。`、`「手繪地圖」`），有些是真缺條目（`刮痕之門`、`母親的項鍊`、`特里普斯合金`）。另外有 43 個 JSON 仍帶 `D:\BACKROOMS\...` 絕對路徑。`validate-content.mjs` 已建立並接進 `verify-final`；缺條目目前是 warning，不擋建置。

### 3.2 多段事件回填

wiki 仍有 61 條事件是單段格式。`steps.mjs` 與 `event-parse` 已經能保留第二次以後的「你決定」，但需要重跑 19 個場次並覆蓋 `floors/*/events.json`。驗收樣本：「通風管和走廊」應拆成 3～4 段。

### 3.3 事件結果錯配

`event-import.mjs` 已預置 `isFloorIntroLine`（bigram 重疊 ≥ 0.7 丟棄），但還沒接進流程。接上之前，results 仍可能混進樓層介紹或上一則事件的句子。

### 3.4 樓層資料債

- `212-發光深淵` 只有碎片，介紹為空，等下次進該層重錄。
- `14-冷漠病院`、`40-有害的衰敗` 沒有配色；`1-停車區` 只有底色。
- 453 件物品中約 259 件沒有 `desc`、101 件沒有 `eff`；建議區分「故意留空」與「漏寫」，用 `_stub` 或 `_status` 標記。
- 少數 `floor.json` 的 `_staging`／`_observations` 還留著 `D:\BACKROOMS\...` 絕對路徑；搬家前要清。

### 3.5 管線可攜性

`event-import`、`parse`、`validate`、`validate-events`、`apply-wiki`、`run` 等仍有 `D:/BACKROOMS/...` 硬編碼。建議做一個 `记录器/structured/paths.mjs`，從 `import.meta.url` 往上找含 `wiki/content` 的根目錄，統一匯出 `ROOT`／`WIKI`／`CONTENT`／`LOGS`。

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
