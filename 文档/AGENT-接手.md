# 给下一个 Agent 的接手说明

更新：2026-10-29  
工作区：`D:\BACKROOMS`

> 先读这份，再看 `要求文档.md`。  
> 旧的 `交接.md` 是历史过程；以这份、`要求文档.md` 和 `_reports/2026-10-28-修復記錄.md` 为准。

---

## 1. 用户最在意的规则

1. **不要随意关闭用户的游戏窗口。**
   - 测试期间保持客户端开着；停止记录／整理／导出 logs 都不需要关游戏。
   - 不要用进程名批量杀进程；只关自己启动、记下 PID 的程序。

2. **不做自动玩。**
   - 游戏作者不允许。不要恢复 `auto\`、`quark\`、`sessions\` 或任何自动玩入口。

3. **正常导入流程不依赖 AI。**
   - 规则、校验、staging、确认流程必须能独立运行。

4. **不编造。**
   - 事件、路径、战斗、结局、配色都不能猜。
   - 不确定就进 staging／review，不能覆盖正式 Wiki。

5. **对话用简体中文。**
   - Wiki 正文用繁中；玩家名不能进正文；角色名要保留（`天狐` 已经是角色）。

6. **截图不进 Wiki。**
   - 截图只用于读取文字，读完删除；不要放进 `site\` 或正文。

---

## 2. 当前结构

```text
wiki/content/                     正式资料（唯一来源，不要手改 site/index.html）
  floors/<楼层>/floor.json       楼层介绍／物品
  floors/<楼层>/events.json      该层事件／岔路（唯一来源）
  floors/<楼层>/endings.json     该层结局（唯一来源）
  floors/<楼层>/design.css       楼层专属样式
  characters/<角色>/character.json
  entities/<实体>/entity.json
  ...

logger/structured/                ★ 管线唯一来源（只改这里）
player-logger/app/                运行时副本（由 sync 同步）
player-logger/dist/FoyerRecorder/ 可携版副本（由 sync 同步）
player-logger/FoyerPlayer.cs      游戏客户端＋控制窗口
wiki/content/build.mjs            由 content 产生 site/index.html
wiki/site/index.html              产物（单文件离线站点，不要当来源）
_archive/                         动手前的快照与移出的旧资料
_reports/                         修复记录
```

### 已删除／移出

```text
wiki/content/floors/0.01-出口？    正文截断的 OCR 假页 → _archive/.../cleanup
player-logger/structured/          第三份旧副本 → _archive/.../player-logger-structured-archived
5 个 0 事件的 events.json 空壳      → _archive/.../cleanup/empty-events
重复实体 河后／冰湖底下的蛇／有失控的礦車 → 已合并，旧档在 _archive/.../cleanup/dup-entities
```

---

## 3. 当前数据状态

> **不要在这里手写数字。** 这一段每次收工都会变，写死就会变成下一个人的错误前提。
> 要现在的真实状态，跑：
>
> ```powershell
> node D:\BACKROOMS\记录器\structured\tests\verify-final.mjs
> ```
>
> 输出的 `summary`（总数／失败数）和 `coverage`（每层收到几个事件）就是现值。

2026-10-29 17:59 重建后的状态（仅供参考，会过期）：

```text
楼层：2（0-大廳、1-停車區），都有正文与配色
事件：14 条（0 层 10/15、1 层 4/6 → 都标 partial）
路口：4 个（0 层 2、1 层 2）
结局：1 笔（全員迷失，lines 为空、status=待補）
角色：78　实体：71　物品：453　商店：17
验收：verify-final 136/136；run-tests 3/3
```

「已收录」= 有正文；`partial` = 事件数少于 `progress.total`，站点会显示待核对。

---

## 4. 管线怎么跑

### 4.1 正常收工（自动汇入楼层／配色／干净事件）

关掉游戏窗口后，`postprocess.mjs` 会自动跑 `run.mjs --auto`：

```text
linecrops（每帧唯一）
→ font-correct.mjs：rapid-ocr.exe 只做识别，同一张图只跑一次
→ structured.jsonl（Windows OCR 被高精度重认替换）
→ parse.mjs／event-parse.mjs／input-bind.mjs
→ event-import.mjs --apply --auto（固定区域边界清洗后写 events.json）
→ transcript／consensus
→ import-wiki.mjs → wiki/_incoming/<session>/
→ validate.mjs（errors 应为 0）
→ merge-final.mjs（只合并高信心楼层／配色；既有介绍不覆盖）
→ build.mjs（重建站点）
```

结束后会写 `wiki/_incoming/<session>/auto-import.json`：
`imported:true` 表示至少写入了楼层或事件；`reason:"nothing high-confidence"` 表示整场都留在 staging。

事件不再需要逐个确认：`event-clean.mjs` 用固定 UI 语义截断正文（难度／选项／结果／进度），
选项去重、保留完整动作；没有玩家选项的事件也可以自动入库。真正低信心或 OCR 残缺的才留 staging。

### 4.2 什么时候还要人工看

不是每个事件都要看。只有以下情况才打开 staging：

- `auto-import.json` 的 `events.rejected` 很高；
- `validation.json` 有 error；
- 楼层介绍被截断（`_docCandidate`）；
- 新事件正文明显是 OCR 垃圾。

### 4.3 需要手动补跑时

正常收工已经自动处理事件；下面只用于 auto 被 validation 挡住、或你明确要重跑某场：

```powershell
# 1) 先看
#   wiki/_incoming/<session>/validation.json
#   wiki/_incoming/<session>/events-review.jsonl
#   wiki/_incoming/<session>/merge-final-report.json
#   wiki/_incoming/<session>/auto-import.json

# 2) 事件写进楼层的 events.json（auto 场次会套用同一套清洗）
node D:\BACKROOMS\记录器\structured\event-import.mjs <sessionDir> --apply --auto

# 3) 重建站点
node D:\BACKROOMS\wiki\content\build.mjs
```

一次跑完（明确表示要写）：

```powershell
node D:\BACKROOMS\记录器\structured\run.mjs <sessionDir> --apply-events --merge
```

### 4.3 别人的记录包

```text
对方：启动记录器 → 关游戏 → 产出 .foyerlog.zip
你：wiki\匯入紀錄.bat <zip>            → 只展开／验证／审查
    wiki\import-package.ps1 -Staging <dir> -Apply  → 才写入
```

`import-package.ps1` 不再默认写入；`匯入紀錄.bat` 直接把 zip 拖上去也只会停在 staging。

---

## 5. 构建／验证命令

```powershell
# 站点
node D:\BACKROOMS\wiki\content\build.mjs

# 管线副本同步
node D:\BACKROOMS\记录器\structured\sync.mjs
node D:\BACKROOMS\记录器\structured\sync.mjs --check

# 验收（项数会变，看 summary.total／failed）
node D:\BACKROOMS\记录器\structured\tests\verify-final.mjs

# 端到端 fixture（战斗／前缀／选项，3 项；沙箱里也跑得动）
node D:\BACKROOMS\记录器\structured\tests\run-tests.mjs

# 单项
node D:\BACKROOMS\记录器\structured\tests\i18n.test.mjs
node D:\BACKROOMS\记录器\structured\tests\font.test.mjs
node D:\BACKROOMS\记录器\structured\tests\data.test.mjs
node D:\BACKROOMS\记录器\structured\tests\recorder.test.mjs
```

输出站点：`D:\BACKROOMS\wiki\site\index.html`

### 简繁转换

- `import-wiki.mjs` → `i18n.mjs`，**不要**再把 `wiki/_i18n` 的繁→简表按位置反推。
- 字频来源：`logger/structured/char-freq.json`。
- 改转换规则后必须跑 `tests/i18n.test.mjs`（含「四面八方／皇后／裡面」等负面案例）。
- `char-freq.json` 由 `build-char-freq.mjs` 生成；在可携版没 wiki 可读时它会拒绝覆盖。

### 字形校正

- `font-correct.mjs` 只接受 `SAFE` 清单里的形近字（夭→天、潦→流、証→證、闆→板、彆→別）。
- 不再把 `麵` 改成 `面`。
- 测试用固定 fixture，不再临时 spawn PowerShell。

---

## 6. 2026-10-28 修了什么（摘要）

完整记录见 `_reports/2026-10-28-修復記錄.md`。

1. **默认不自动写正式库**：`run.mjs` 去掉写死的 `--apply`／`--confirm`。
2. **管线只剩一份**：`logger/structured` 为唯一来源，`sync.mjs` 同步 app/dist，`verify-final` 比 hash。
3. **简繁反推修好**：词表＋字频判断方向，不再产生「四麵八方」；新增 27 例回归。
4. **数据模型**：结局数组化、399 的 `own`→`owns`、合并 3 组重复实体、删 0.01／空 events、`merge-final` 不再覆盖既有介绍、新楼层要 0.8 信心。
5. **楼层锁定**：改用 `id|name` 判定；移除「只有 Level 0 能点」的硬锁；新增「待核对」横幅；计数改「已收录」。
6. **楼层介绍误杀修复**：`parse.mjs` 不再把所有含引号的行滤掉，只滤整行台词；`狐夭/狐天` 换成「你」。
   Level 2 的介紹補回「定会加倍还回来」。
7. **敌人名字清理**：`cleanEnemyName` ＋连续 2 帧投票；同一场从 11 个 combat／6 个垃圾实体 → 1 个 combat／1 个实体。
8. **实体脏数据**：清掉 10 个实体的「技能」假技能与战斗流水；validation warnings 15 → 8。
9. **配色管线**：修 `line=$card`、空 JSON 0 byte、删错档、底色取样；楼层截图改成每层一次。
   已用真实游戏截图验证；Level 0/2 取样见 §3.2。
10. **真实测试（2026-10-29 00:25–00:35，全新一局）**：简单／后室百科／2 撬棍＋登山靴，
    Level 0 → 马尼拉房间 → **Level 2「管道梦境」**。确认：没有假 Level 1 档、Level 0/2 的
    `floor-shots` 不是黑屏、Level 2 介绍完整、palette 五色。修了第四处标题括号
    （`Get-LevelNumberFromText` 不认 `Leve 《 2`）、主菜单截图（加 `LevelFromHeader` 门闸）、
    重复 id 的 palette 互盖（`FLOOR_PAL_NAME`）。真实取样：
    Level 0 `#2C2410 / #383414 / #8C8C88 / #E0CC58 / #C0B488`；
    Level 2 `#2C1408 / #2C1808 / #2C1404 / #C47038 / #E4E0C0`。
11. **重复 id 键冲突**：`FLOOR_PAL` 不再收 `1/9` 的 palette，改存 `FLOOR_PAL_NAME['9|黑暗郊區']`；
    `setFloor` 带 `name` 查表。UI 冒烟 13 项通过：`1|停車區`／`1|停電的樓上` 标题不互串、
    `9|隧道` 锁住不串色、无 JS 错误。脚本在 `wiki/ui-smoke.mjs`。
12. **Level 483 前缀误判**：`Resolve-FloorNumber` 把 483 当 4 的前缀，产生假的 `04-廢棄辦公室.log`。
    修法：3 位以上编号不参与前缀修正；名称对不上也不修正（`$feName`／`$nameOk`）。
13. **Level 483 介绍只剩两段**：`noise` 的 `^，` 把「，也是灰色的。」整行丢掉，段落间距变成 56 > 45。
    修法：`textOf` 把 `，。、；：` 开头的续行接回上一行，不再丢。
14. **收工自动导入**：`postprocess` 改跑 `run.mjs --auto`。
    高信心楼层／配色自动合并；低信心／validation error 留 staging；既有介绍不覆盖。
    事件先用固定区域边界清洗，干净的后来自动写 `events.json`。
15. **`Leve 483` 无尾 `l`**：`Update-LevelFromText` 第一道门原本要求 `leve[l1i]`，
    OCR 只读到 `Leve 483` 时会进“读不出编号”。已放宽成 `leve[l1i]*`。
    最终 build 实玩 Level 0 → 483：`levels/` 只有 00/483，无假 04；自动导入只更新 483 palette。
16. **固定行 RapidOCR**：`linecrops` 原本用 `FrameSeq` 命名，所有帧互相覆盖；改成每帧
    `FrameNo`，再用 `rapid-ocr.exe` 只做识别（SHA1 去重，529 张约 57 秒）。
    实玩修正 `里调跗萸巴墅`→`单调的黄色壁纸`、`刂 - 播座`→`小心绕过插座`、
    `狐夭`→`狐天`。没有 RapidOCR 时退回 Windows OCR，不阻塞收工。


> 更正上一轮的说法：`FLOOR_READY` 其实有被 `floorReady()` 读取，不是完全没接线。  
> 真正的问题是：判定用 id（1/7/9 会互相解锁）＋「除了 L0 全部禁止点击」的硬编码拦截＋53 层有正文却因为缺 `_observations` 被锁。这三件已经修掉。

---

## 7. 仍未解决（不要假装已解决）

1. **楼层介绍仍可能被 OCR 截断**：引号误杀已修（Level 2 已补回），但 OCR 断行／行距过大仍会漏。
   `merge-final` 不会自动覆盖，差异进 `_docCandidate`。
2. **OCR 变体仍在**：敌名已清（A/B、UI 标签、2 帧投票），但罕见变体（如「窈皮者」对「竊皮者」）
   仍可能出现；低信心只进 staging。
3. **配色**：已用真实截图验证（Level 0/2）；部分楼层仍只有底色，**不要自己生成**。
4. **重复 id 不改**：`1/7/9` 共用 id；解锁／配色／deep link 已改成 `id|name`。
   真要改 id 会动到深链接与配色 key，需先问用户。
5. **15 层正文空白**：现在锁住；要不要补是产品决定。
6. **楼层入口随机**：不保证每局遇到同一层；不要为了某个特定楼层反复刷。
   按正常游玩收工，自动导入会处理高信心楼层。
7. ~~**事件不会自动写入**~~（2026-10-29 作废）：`--auto` 会在事件通过门闸后自动写
   `events.json`。门闸只有一个：**清完之后 `selected` 仍然落在 `actions` 里**。
   OCR 把选项读到对不上，就留 staging。本轮 55 笔候选 → 10 条入库 + 1 个结局。

---

## 8. 下一步建议

按顺序：

1. 用指定套组实际玩一场：简单／肉盾／后室百科／2×撬棍＋1×登山靴／双持攻击。
2. 进一次战斗，只记「会进入战斗＋敌人名字」，不记流程。
3. 进一个新楼层，确认：楼层标题、介绍、配色截图（用完删）。
4. 收工 → 看 `_incoming/<session>` 的 validation／events-review／merge-final-report。
5. 只对通过审查的项目执行 `--apply-events`／`--confirm`。
6. 重建站点 → `verify-final` 121/121。
7. 特别检查：新楼层介绍是否完整、有没有被截断；没有就留在 `_docCandidate`，不要硬合并。

---

## 9. 不要做

- 不要恢复自动玩。
- 不要用全局 `content/events.json` 或 `content/endings/`。
- 不要把战斗流程写成事件。
- 不要用截图当 Wiki 内容。
- 不要写玩家名（`狐天` 已确认是帐号名；`天狐` 是角色，可保留）。
- 不要直接改 `site/index.html`。
- 不要只改 `player-logger/app/structured`（会被 sync 覆盖）；改 `logger/structured` 再 sync。
- 不要在没有备份的情况下 `--confirm`／`--apply`。


---

# 2026-10-31 ~ 11-01 進度與待辦（接手先看這段）

## 已完成

### 目錄重整
根目錄只剩：`记录器/`（OCR 記錄器＋管線）、`客户端/`（FoyerPlayer）、`wiki/`（內容＋站點）、`文档/`、`备份/`、`AGENTS.md`、`启动记录器.bat`。
舊路徑 `logger/ logger\`、`player-logger/`、`_reports/`、`_archive/` 已不存在。

### 樓層
- 從舊資料（logs／wiki／备份／文档）掃出樓層名，補進 52 個「只有名字」＋ 23 個「只有編號」的樓層，共 92 層入庫。
- `_status: 'name-only' | 'id-only'` 的樓層：不標「只記錄到一小角」、不要求正文、列表顯示名字與編號（未解鎖時兩者同色偏暗、名字有亂碼動畫）。
- 樓層介紹規則（使用者口述，已實作於 `parse.mjs`）：
  1. 起點是橫幅 `—— Level N「名稱」 ——`（橫幅本身不算正文）
  2. 終點是緊接的大空行（行距 × 2.2 為門檻）
  3. 橫幅以上的殘影一律不算
  4. 結尾連續的「角色名＋一句引號台詞」全部剝掉（多人模式可能好幾句）
  5. 介紹內出現玩家角色名 → 換成「你」
- 17 層已用新規則覆蓋；`212-發光深淵` 只有碎片，已清空並標 `_introMissing`（保留名字）。

### 事件
- QTE：偵測「閃！／快躲」等提示，成功／失敗各自收 branch。
- 檢定：解析「（感知 難度13）擲出 6+3=9 → 失敗」，掛到被選的選項上；難度與擲骰數字**不顯示**在頁面。
- 選項按畫面順序（y）排序。
- 逐行取色 `ink`（記錄器每行寫入），`记录器/structured/ink.mjs` 做相對正文色的角色分類（標題／數值／暗字／選項）。
- 隨機結果樣本：`variants` 掛在「選項」上（count＋百分比），檢定分支另有 `outcomeStats`。
- 連環事件：`记录器/structured/steps.mjs` 提供 `splitSteps / eventToSteps / hasMultipleSteps`；
  `event-parse.mjs` 會保留第二次以後的「你決定」當階段標記；`event-import.mjs` 寫入時自動展開 `steps[]`。

## 未完成（接手優先做）

1. **steps 回填**：原始日誌裡多段事件已能解析（單場已驗證 15 條中有 2 條多段），
   但 wiki 的 61 條事件還是單段格式 —— 需要重跑 19 個場次並覆蓋 `floors/*/events.json`。
   驗收樣本：「通風管和走廊」應拆成 3~4 段。
2. **事件與結果仍會錯配**：results 會混進樓層介紹或上一則事件的句子。
   `event-import.mjs` 已預置 `isFloorIntroLine`（bigram 重疊 ≥ 0.7 丟棄），但**還沒接進流程**。
3. **`212-發光深淵`** 沒錄到完整介紹，等下次進入該層再重跑覆蓋。
4. **`14-冷漠病院`、`40-有害的衰敗` 沒有配色**（`1-停車區` 只有底色），需再進一次取樣。

## 驗收現狀

```
node 记录器/structured/tests/verify-final.mjs   → 792 / 792
node wiki/smoke.mjs                            → 9 / 9
node 记录器/structured/sync.mjs --check        → 76 項對齊
```

## 這輪踩過的坑（別重犯）

- 用 PowerShell 對中文檔做字串替換常常靜靜失敗（跳脫被吃掉）；改檔請用 `read` + `edit` 或 node 腳本。
- 對「不同 OCR 區塊」的 y 座標做比較是錯的（曾導致 19 個事件結果全空）。
- 占位樓層（name-only／id-only）不要留 `events.json`，否則 `events:non-empty` 驗收會掛。
