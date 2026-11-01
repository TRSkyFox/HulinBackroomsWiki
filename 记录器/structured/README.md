# 結構化 logs → Wiki staging

這個資料夾是**管線唯一來源**。`客户端/app/structured` 與
`客户端/dist/FoyerRecorder/structured` 都是同步副本，不要在那兩份上改。

```powershell
node 记录器/structured/sync.mjs           # 同步到 app／可攜版
node 记录器/structured/sync.mjs --check   # 有漂移就 exit 1
```

## 流程

```text
记录器/backroom-logger.ps1
  → session/structured.jsonl     每幀的區塊、座標、文字
  → font-correct.mjs             形近字校正（只改 SAFE 清單內的字）
  → clean-levels.mjs             刪掉換層瞬間的假樓層檔（只有恢復畫面／空白）
  → parse.mjs                    規則式 UI 狀態與候選記錄
  → event-parse.mjs              路口／事件候選
  → input-bind.mjs               綁定玩家實際輸入
  → event-import.mjs             事件 staging（預設不寫正式 events.json）
  → transcript／consensus        逐字稿與共識稿
  → import-wiki.mjs              簡→繁＋產生 Wiki staging（不直接改 content）
  → validate.mjs                 validation.json
  → merge-final.mjs              dry-run 報告（--confirm 才寫）
  → wiki/content/build.mjs       重建站點
```

關掉遊戲時，`postprocess.mjs` 會自動呼叫 `run.mjs --auto`：
高信心樓層／配色自動寫正式 Wiki；低信心或 validation error 只留 staging。
事件先用固定區域邊界清洗（難度／選項／結果），乾淨的才自動寫 `events.json`；
RapidOCR 會對 `linecrops` 重認，失敗時退回 Windows OCR。

## 使用

```powershell
# 收工自動匯入（postprocess 預設就是這個）
node 记录器/structured/run.mjs <session> --auto

# 只產生 staging／審查報告
node 记录器/structured/run.mjs <session>

# 明確要把事件寫進正式樓層（人工審查後）
node 记录器/structured/run.mjs <session> --apply-events

# 明確要合併樓層／實體並重建
node 记录器/structured/run.mjs <session> --merge
```

分步跑：

```powershell
node 记录器/structured/event-import.mjs <session> --apply
node 记录器/structured/merge-final.mjs <stagingDir> --confirm
node wiki/content/build.mjs
```

## 審查要看的三個檔

```text
staging/validation.json          硬錯誤＝不能合併；warning 逐條看
staging/events-review.jsonl      每個事件候選：accepted／low_confidence／unreadable_or_noise
staging/merge-final-report.json  哪些樓層／實體會被改、哪些被跳過、備份在哪
```

低於 0.8 的項目不要合併。沒有正式頁的實體目前會跳過（不會自動新增）。

## 目前規則

- 樓層：只認畫面頂端的 `Level N「名稱」`；用既有 `content/floors` 比對
- 配色：先取 main 區底色；既有樓層的 palette 優先
- 介紹：只取樓層標題後、選項前的連續文字塊（仍可能被大行距截斷，見下）
- 岔路：以選項面板座標與「要走哪／要怎麼做」判定
- 戰鬥：只記「會進入戰鬥」＋敵人名字，不記流程
- 選擇後：只接受建立 choice 之後的「→ 你決定：…」

## 重要限制

- 這是 **staging**，不是可直接信任的正式資料。OCR 仍會有錯字、選項重複、敵人技能／掉落缺失。
- **樓層介紹仍可能被 OCR 截斷**：引號誤殺已修，但斷行／大行距仍會漏。
  `merge-final` 不會自動覆蓋既有介紹；差異寫進 `_docCandidate`，要人工比對。
- **敵名有清理但非萬能**：`cleanEnemyName` 會去掉 A/B、前綴、UI 標籤，並要求連續 2 幀一致；
  罕見變體仍可能留下，低信心只進 staging。
- **配色取樣可能為 0 筆**：樓層標題 OCR 不穩 → 沒有 floor-shots → `floor-palettes.json` 是空的。
- 截圖只作 OCR 暫存，轉錄後刪除，不進 Wiki。

## 簡繁轉換

`i18n.mjs` 負責。**不要**把舊 `wiki/_i18n`（已封存）的繁→簡表按位置反推：

- 那張表本身混有反序 pair（例：`发 髮`）。
- 反推會把「四面」變「四麵」、「皇后」變「皇後」。
- 現在用 `char-freq.json`（Wiki 現有字頻）判斷方向；兩邊都在用就不轉。
- 詞表處理一簡對多繁（頭髮／裡面／乾淨／一隻／麵條／遊戲…）。
- 改規則後跑：`node tests/i18n.test.mjs`。

## 字形校正

`font-correct.mjs` 只接受 `SAFE` 內的形近字（夭→天、潦→流、証→證、闆→板、彆→別），
不再把 `麵` 改成 `面`。測試用 `tests/fixtures/font-line.png`，不依賴 pipe／PowerShell。

## 測試

```powershell
node tests/i18n.test.mjs
node tests/font.test.mjs
node tests/data.test.mjs
node tests/verify-final.mjs     # 以輸出 summary 為準
```

## Palette 取樣

`run.mjs` 會呼叫 `palette-from-shots.ps1`：讀 `floor-shots/*.png`，寫 `floor-palettes.json`，
然後刪掉處理過的截圖。取樣規則：

- `bg`：四角取樣（整張圖的眾數常是內容面板，不是樓層底色）
- `card`：面板內、和底色不同的主要色
- `line`：沿 card 色範圍的邊界取（避免拿到文字反鋸齒的色邊）
- `acc` / `tx`：標題區／內文的亮色

空結果會寫 `[]`，不是 0 byte。也可以用人工校準工具：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File palette-sample.ps1 -Image <png> -Out <json>
```

不會自動把推測色寫進正式 content。
## 多敵人的 A/B 臨時編號

同一場戰鬥出現多隻同型敵人時，敵名常被加上 `A`、`B`。這些不是不同敵人：

```text
嗡鳴體 A、嗡鳴體 B  →  嗡鳴體
回聲人影 A、回聲人影 B → 回聲人影
```

parser 與 importer 會剝除尾端單一字母，並把同場掉落／抗性合併到同一 entity。

## 敵人前綴

這些前綴不是新物種，會先剝除再比對：

```text
巨大、強壯、不死、嗜血、恐怖、飢餓、惡夢般、敏捷、堅硬、殘影、守護、呼喚、
時間錯亂、獵殺、吞噬、群體、脆弱、統率、不可名狀、吞光、適應、越打越兇、不滅、
貪婪、帶刺、粘人
```

例：`巨大的 強壯的 獵犬` → `獵犬`。

## 觀測累積與可信度

staging 會多存：

```text
evidence/entities/<entity>.jsonl
evidence/floors/<floor>.jsonl
```

每個實體保留：每次觀測的敵名／介紹／技能／掉落／抗性、各欄位次數、既有正式內容。

選擇規則：

1. 正式 content 已有的介紹／欄位優先，不覆蓋
2. 沒有策展資料時，取出現次數最多、長度較完整的 OCR 文字
3. 掉落／抗性／技能以聯集保留，依次數排序
4. 低於 0.8 的候選留在 staging

## 事件寫入方式

事件唯一來源是 `floors/<層>/events.json`。`merge-final` 預設**不再**把事件塞進
`floor.json` 的 doc（舊的 `AUTO-EVENTS` 會產生第二份事件）；只有除錯時才加：

```powershell
node merge-final.mjs <stagingDir> --confirm --with-doc-events
```
