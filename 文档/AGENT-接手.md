# 给下一个 Agent 的接手说明

更新：2026-11-03 14:35  
工作区：`D:\Project\HulinBackroomsWiki`

> **2026-11-04 补充**：这是 2026-11-03 的交接快照。项目已搬到 `D:\Project\HulinBackroomsWiki`；现行统计、新记录器流程与 roadmap 请看根目录 `README.md`。本文的 824 项、13 个 events、4 个 endings、9 个 id-only 等数字已过时，不要拿来当目前状态。

> 先读 `AGENTS.md`（现行硬规则）。这份只讲“现在是什么状态、最近改了什么、下一步做什么”。
> 旧交接、旧 Wiki 管线已封存在 `备份/legacy-wiki-20261101.zip`。

---

## 1. 交接摘要

- 项目：非官方 Wiki ＋ OCR 记录器 ＋ ExpeditionRecorder／ExpeditionClient。
- Wiki 内容来源：`wiki/content/`；产物：`wiki/site/index.html`。
- 管线唯一来源：`客户端/Recorder/structured/`。
- app 同步副本：`客户端/structured/`；便携版同步副本：`客户端/dist/Expedition/structured/`。
- 原始 logs：`客户端/Recorder/logs/`，只读。
- 版本控制：git 工作树目前干净。

目前实测：

```text
楼层：96（含 name-only／id-only 占位）
角色：98
实体：71
物品：455
商店：17
有 events.json 的楼层：13
结局条目：3（build 统计；不是独立 endings.json 档）

verify-final   824 / 824，failed 0，warnings 5
smoke           7 / 7
sync --check    82 项对齐
```

> 数字会变；要现在的值就重跑指令，不要拿这份当永久保证。

---

## 2. 最近完成（2026-11-03）

### 2.1 记录器不再误认浏览器

- [ExpeditionRecorder.cs](D:/Project/HulinBackroomsWiki/客户端/ExpeditionRecorder.cs)
  - 标题兜底只接受同安装文件夹的 client。
  - 明确排除 Chrome、Edge、Firefox、Brave、Opera、Vivaldi、360、QQ、Sogou、Liebao、Maxthon、UC、2345、`msedgewebview2`。
- [backroom-logger.ps1](D:/Project/HulinBackroomsWiki/客户端/Recorder/backroom-logger.ps1)
  - 收到 `-Hwnd` 时检查窗口拥有程序，浏览器直接拒绝。
  - 不再自动扫全机浏览器窗口。
  - 窗口消失时只等待，不再重新锁定到其他浏览器。
- 实测：
  - Firefox 窗口 → `exit 1`，拒绝记录。
  - 目前 `ExpeditionClient` 窗口 → 正常 OCR。

### 2.2 事件管线改为“全部候选可见”

- [event-extract.mjs](D:/Project/HulinBackroomsWiki/客户端/Recorder/structured/event-extract.mjs)
  - 以 `【事件标题】` 为边界，不再把上一事件结果带进下一事件。
  - 只取目前投票区的选项，不把箭头、感知提示、道具标签当主选项。
  - `狐天`／`狐夭` 转“你”；`天狐` 是角色，保留。
  - 同一事件的多个选择保留 steps。
- [event-import.mjs](D:/Project/HulinBackroomsWiki/客户端/Recorder/structured/event-import.mjs)
  - 所有事件候选都写到 `structured/events-review.jsonl`，不再无声消失。
  - 正式 `events.json` 只收通过的；review 会留下 accepted／reason。
  - 最新真实场次：26 个候选、19 个通过、7 个在 review（多为 clean-rejected）。
- [merge-final.mjs](D:/Project/HulinBackroomsWiki/客户端/Recorder/structured/merge-final.mjs)
  - 楼层 doc 截断防线：没有句末标点的新介绍不覆盖正式 doc。
  - 最新 dry-run 实际挡下 `废弃办公室` 的截断 staging，正式页保持完整。

### 2.3 楼层名称与 12／60 资料

- 从 legacy archive 恢复 14 个 `▒▒▒▒▒` 名称：
  `0.22 全面翻修`、`109 噪音污染`、`12 血肉之屋`、`15 未來感的走廊`、`161 阿帕拉的荒涼`、`20 物資倉庫`、`271 卡斯托羅薩斯的巢穴`、`28 閣樓`、`288 熵`、`398 慘無人道的惡作劇`、`399 霓虹天堂`、`595 綠洲`、`60 盡力彌補`、`63 平靜之雲`。
- 剩 9 层只有编号、没有可恢复名字，保留 `id-only`，不要编造：
  `0.41、1.1、154.1、188、21、372、399.1、6.2、87`。
- `12-血肉之屋`、`60-盡力彌補` 已恢复正文、配色与专属 `design.css`。

### 2.4 新角色

- `狐玥`（雪国来的白狐）
  - 角色：`wiki/content/characters/狐玥/character.json`
  - 装备：`印弩`、`祀衣`
  - 已写入 `_meta.json` 排序与 Wiki。

### 2.5 Expedition Archive 路径错误

- 错误堆栈指向 `Path.Combine`，根因是 [Expedition Archive.bat](D:/Project/HulinBackroomsWiki/Expedition%20Archive.bat) 的：

```bat
--repo "%~dp0"
```

`%~dp0` 结尾是反斜线，会把结尾引号吃掉，变成 `D:\Project\HulinBackroomsWiki"`。  
`Path.Combine` 因此抛“路径中具有非法字符”。

- 已修：
  - bat 启动前去掉尾反斜线。
  - C# 再清理 `--repo` 的引号与尾斜线，旧快捷方式也能用。
  - Archive 增加全域例外记录：`%TEMP%\ExpeditionArchive-error.log`。
  - CLIXML 错误改成可读文字。
  - 已重新编译 `tools/ExpeditionArchive.exe`。
- **注意：目前若还开著旧 Archive 窗口，要关掉重开**，旧程序仍是旧版。

### 2.6 项目文件／脚本整理

- `AGENTS.md` 根目录、统计、指令已更新。
- `客户端/README.md` 改成现行 `start.ps1`／`run.mjs` 流程。
- `start.ps1` 会直接从 `Recorder` 覆盖 app 的 `backroom-logger.ps1`、`palette-from-shots.ps1`。
- `sync.mjs` 不再把合法的 `客户端/structured` 误报成旧副本。
- 新增 [2026-11-03-项目整理与优化.md](D:/Project/HulinBackroomsWiki/文档/报告/2026-11-03-项目整理与优化.md)。

---

## 3. 目前待处理

### 3.1 已展开、尚未套用的记录包

`Expedition-backroom-20261103-130944.expedition.zip` 已经展开验证：

```text
wiki/_incoming/import-Expedition-backroom-20261103-130944.expedition-20261103-141918
validation: 0 errors / 20 warnings
floors: 5
entities: 13
```

目前**没有写入正式 Wiki**。  
若确认要收，请用新任一个 Archive：

- 点“整合进 Wiki”；
- 或跑：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\Project\HulinBackroomsWiki\wiki\import-package.ps1 -Staging "D:\Project\HulinBackroomsWiki\wiki\_incoming\import-Expedition-backroom-20261103-130944.expedition-20261103-141918" -Apply
```

不要重复展开同一个 zip；package SHA256 会挡。

### 3.2 事件覆盖率

最新真实场次：

```text
raw 事件观察：138
去重后候选：26
通过 event-import：19
review：26（7 个有原因）
正式 events.json：18（该场次）
```

review 档：

```text
<session>\structured\events-review.jsonl
```

之后若要提升覆盖率，先看 review 的 `clean-rejected` 原因，不要直接放宽全部。

### 3.3 内容债

- 455 件物品中约 261 件没有 `desc`。
- 116 处角色／实体／商店／配方引用找不到物品。
- 54 个 JSON 仍有 `D:\Project\HulinBackroomsWiki\...` 绝对路径（多在 `_staging`／`_observations`）。
- 9 个 `id-only` 楼层没有真实名字。
- `212-發光深淵` 缺完整介绍；`14-冷漠病院`、`40-有害的衰敗` 缺配色；`1-停車區` 只有底色。
- `0.2`、`11` 等旧楼层介绍仍有来源截断；新的 merge gate 已避免再被截短。

**不要代写物品效果、事件结果、楼层名字。** 没有真实来源就留 staging／占位。

### 3.4 空间与清理

- `客户端/dist` 约 825 MB，`Expedition.zip` 约 315 MB；是正常成品，不要为省空间删 DLL／OCR 模型。
- `备份` 约 172 MB；可移到其他磁碟。
- `wiki/_incoming` 目前有 3 个 staging（其中 1 个是上述待套用包）。
- `客户端/Recorder/logs` 约 24 MB，原始 logs，只读。

---

## 4. 现行资料流

```text
ExpeditionRecorder
  → ExpeditionClient（WebView2）
  → 客户端/Recorder/backroom-logger.ps1
  → 客户端/Recorder/logs/backroom-<时间>/structured.jsonl
  → 客户端/Recorder/structured/run.mjs
      → parse / event-parse / input-bind
      → event-import（events.staging.json + events-review.jsonl）
      → import-wiki → wiki/_incoming/<session>/
      → validate → merge-final
      → wiki/content/ → build.mjs → wiki/site/index.html
```

- 便携版（玩家）：`run.mjs --portable`，只生成 `.expedition.zip`，不碰正式 Wiki。
- 本机 Wiki 主人：默认 staging；只有明确 `--auto`／`--apply-events`／`--merge` 才写正式。
- 事件正式档：各楼层 `floors/<id-name>/events.json`。
- 事件 review：`structured/events-review.jsonl`，不是正式内容。

---

## 5. 常用指令

```powershell
# 建置网站
node D:\Project\HulinBackroomsWiki\wiki\content\build.mjs

# 前端冒烟
node D:\Project\HulinBackroomsWiki\wiki\smoke.mjs

# 管线同步
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\sync.mjs
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\sync.mjs --check

# 完整验收
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\tests\verify-final.mjs

# 内容引用
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\validate-content.mjs

# 端到端 fixture
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\tests\run-tests.mjs
```

---

## 6. 重要设计／雷区

- 只改 `客户端/Recorder/structured/`；改完跑 `sync.mjs`。
- 不要手改 `客户端/structured/` 或 `dist/.../structured/`。
- `start.ps1` 会把 `客户端`（排除 `Recorder`、`dist`、`logs`）复制到 `%LOCALAPPDATA%\Expedition\app`；再从 `Recorder` 直接覆盖记录器脚本。
- `start.ps1` 会跳过被锁住的 exe／DLL；更新 `ExpeditionRecorder.exe` 不需要关游戏 client。
- 事件候选要留 review，不要为了整齐而整批丢掉。
- 楼层介绍截断时，正式 doc 优先保留；staging 看得到即可。
- 浏览器窗口一律不能当游戏来源；不要把“标题像游戏”当唯一判准。
- `Expedition Archive.bat` 的 `--repo` 不可以传尾反斜线；C# 会清，但 bat 也已经修。
- 原始 logs 不要删、不要覆盖；`backup` 不要进 git。
- 不要写玩家名 `狐天`／`狐夭`；`天狐` 是角色，保留。

---

## 7. 接手第一步

1. 先跑一次：

```powershell
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\tests\verify-final.mjs
node D:\Project\HulinBackroomsWiki\wiki\smoke.mjs
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\sync.mjs --check
```

2. 确认 `git status` 干净，没有把 `dist`、logs、备份加进 git。
3. 如果要收 `130944` 包，先看 staging 的 `validation.json`／`merge-final-report.json`，再决定套用。
4. 如果要改事件规则，先跑一场真实 log，对照 `events-review.jsonl`，不要用手写 fixture 猜。
