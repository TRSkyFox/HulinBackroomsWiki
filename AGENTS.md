# 工作約定（AGENTS.md）

> 2026-10-29 整理。工作區是 `D:\BACKROOMS`。
> 這份檔案只留「現在仍然有效」的規則；舊路徑、舊 GUI、自動玩、歷史事故的細節
> 已移到 `文档/报告/2026-10-28-修復記錄.md` 與 `备份/`，需要考古再看。
> 現行結構、管線、驗收以 `AGENT-接手.md`、`要求文档.md` 為準。

---

## 1. 絕對不要關閉使用者正在用的程式或視窗

**2026-10-14 事故**：為了清理驗證用瀏覽器，執行了

```powershell
# ❌ 千萬不要這樣做
Get-Process msedge | Where-Object { $_.Path -like '*Edge*' } | Stop-Process -Force
```

結果把使用者正在玩的 Edge 遊戲一併殺掉，進度中斷。

### 規則

1. **禁止用程序名稱批次關閉**（`Stop-Process -Name`、`taskkill /IM`、
   `Get-Process xxx | Stop-Process` 一律禁止）。
2. **只關閉自己啟動、且明確記下 PID 的程序**：
   ```powershell
   $p = Start-Process ... -PassThru
   # ...驗證...
   Stop-Process -Id $p.Id
   ```
3. **不要對使用者的遊戲／瀏覽器呼叫 `bring_to_front`**，也不要用會搶焦點的輸入。
4. 驗證用瀏覽器一律用**獨立 profile**，例如
   `--user-data-dir=D:\BACKROOMS\_edge-verify`；不要動使用者原本的 profile。
5. 不確定是不是使用者的程序 → **先問，不要動手**。

---

## 2. 不要動使用者的遊戲紀錄與原始檔

- `记录器/logs/backroom-*/` 的 `structured.jsonl`、`wiki.md` 等原始 OCR **只讀不寫**。
- `levels/` 只允許 `clean-levels.mjs` 刪掉「空白／恢復畫面造成的假樓層檔」；
  `floor-shots/` 只允許配色取樣腳本取樣後刪除。其他檔案不要動。
- 合併紀錄檔是 Wiki 出處行號的唯一依據，**不要刪、不要覆蓋**。
- `wiki/_drafts/` 是整理過程的中間產物，要刪之前先問。
- 正常匯入流程**不需要 AI**：關遊戲後 `postprocess` 會跑 `run.mjs --auto`。
  高信心樓層／配色自動寫 `wiki/content`；事件先用固定區域＋難度/選項/結果截斷清洗，
  乾淨的才自動寫樓層 `events.json`，其餘留 staging。
- 事件文字在收工後用 `structured/bin/rapid-ocr.exe` 對 `linecrops` 重認（只做識別、
  同一張圖只跑一次）；RapidOCR 不存在時退回 Windows OCR，不阻塞流程。

---

## 3. 截圖是暫存素材：轉錄完就刪

- **不要把截圖放進 Wiki 頁面**；截圖只拿來讀文字。
- 流程：看圖 → 把文字寫進 Wiki → 刪掉截圖。
- 只刪**自己讀過的那幾張**，先列出檔名，不要用萬用字元掃整個資料夾。
- 刪之前再確認一次檔案數：比讀過的多，就是有新截圖，先讀完再刪。
- 配色取樣必需的 `floor-shots` 由記錄器處理；取樣完會自動刪除。

---

## 4. 環境注意事項

- PowerShell 是 **Windows PowerShell 5.1**（`powershell`），不是 `pwsh`。
- 執行腳本加 `-ExecutionPolicy Bypass`。
- 含中文的 `.ps1` 必須存成 **UTF-8 with BOM**；寫回前先剝掉開頭 BOM，避免 BOM 累積。
- 不要用 `$pid`、`$home`、`$host` 等唯讀自動變數。
- 改 `.ps1` 優先用 `read` + `edit` 或按行號切割，不要用含中文的多行 `Replace`。
- 需要跨幀保存狀態時用 `[pscustomobject]`；`[ordered]@{}` 的 `PSObject.Properties`
  查不到自訂 key，容易靜靜走錯分支。
- 不要恢復任何自動玩／CDP 機器人；使用者已明確禁止。

---

## 5. 這個專案是什麼

`D:\BACKROOMS` 是《狐霖的後室 RPG》的 Wiki ＋ 記錄器：

> **2026-10-30 目錄重整**：根目錄只留四個資料夾 ＋ 一個啟動器。
> `记录器\`（OCR 記錄器＋管線）、`客户端\`（FoyerPlayer）、`wiki\`（內容＋站點）、
> `文档\`（說明與報告）、`备份\`（舊備份）。舊路徑 `logger/`、`player-logger/`、`_reports/`、`_archive/` 已不存在。
| 路徑 | 用途 |
|---|---|
| `wiki/content/` | **Wiki 唯一資料來源**（`floors/`、`entities/`、`items/`、`characters/`…） |
| `wiki/content/build.mjs` | 把 JSON 注入 `template.html`，產生單檔 `wiki/site/index.html` |
| `wiki/site/index.html` | 產出的離線 Wiki（雙擊即開） |
| `记录器/backroom-logger.ps1` | OCR 記錄器本體（分區 OCR、逐層分檔、配色快取） |
| `记录器/structured/` | **管線唯一來源**：parse／i18n／import／merge／tests |
| `客户端/` | FoyerPlayer 客戶端；`app/`、`dist/` 是管線鏡像 |
| `wiki/_incoming/` | 每次收工的 staging（預設不寫正式 content） |
| `文档/报告/` | 修復與驗收記錄 |
| `备份/` | 改動前的備份 |

管線鏡像由 `记录器/structured/sync.mjs` 維護；`--check` 有漂移就 exit 1。
**不要手改 `客户端/app/structured` 或 `客户端/dist/.../structured`**，改來源再同步。

---

## 6. 玩家名與角色名要分開

| 類型 | 判斷 | 處理 |
|---|---|---|
| 角色名 | 在 `wiki/頁面/09-角色名錄.md`（含別名／稱號） | 保留 |
| 玩家帳號名 | 只出現在面板上方、發言者位置 | 換成「你」或「玩家」 |

- 目前玩家的帳號名是 `狐天`（OCR 可能變 `狐夭`）→ 正文一律換成「你」。
- **`天狐` 已經是遊戲內角色，不要當玩家名刪掉。**
- 角色名也可能是 `小猦`／`呐豆` 這類看起來像暱稱的名字；查不到不等於不是角色。
- 不確定這一局操作的是哪個角色 → **先問使用者**，不要猜。

---

## 7. 對話語言

- 跟使用者對話：**简体中文**。
- Wiki 正文：**繁體中文**；簡中／英文由 `_i18n/` 轉換。
- 專案文件與腳本註解：維持原本語言（多為繁中）。
- 遊戲內文字照抄，但轉錄進 Wiki 時要轉成 Wiki 用語。

---

## 8. 記錄器與管線的硬規則

1. **管線只有一份來源**：`记录器/structured/`。改完跑 `sync.mjs`。
2. **收工 `--auto`**：自動合併高信心樓層／配色；事件用固定區域邊界清洗後自動寫入，低信心或 validation error 不寫。
3. **樓層以 `id|name` 為 key**：`1/7/9` 有兩層共用 id；不要只用 id 判斷解鎖或配色。
4. **配色一定要從真實截圖取樣**：不自己生成、不重複配色；取樣完刪截圖。
5. **樓層介紹不硬合併**：低信心或太短只進 staging，`merge-final` 不覆蓋既有正文。
6. **战斗只記「進入戰鬥＋敵人名字」**，不記每回合流水。
7. 驗收：`node 记录器/structured/tests/verify-final.mjs`（目前 **339 項**）。
8. 前端冒烟（改完 `wiki/content/template.html` 必跑）：`node wiki/smoke.mjs`。
9. 不要建立或恢復 `auto/`、`quark/`、`sessions/`、`Wiki探索者.exe` 等自動玩產物。

---

## 9. 歷史資料在哪

- 2026-10-28 的修復、真實遊玩測試、配色取樣：
  `文档/报告/2026-10-28-修復記錄.md`
- 改動前備份：`备份/20261028-214357/`
- 舊版 `C:\Users\HX\backroom-wiki` 的規則與事故紀錄：只當歷史，不要再照做。

有衝突時，優先順序：
**使用者當下指示 → 本檔 → `AGENT-接手.md` → `要求文档.md` → 舊文件**。
