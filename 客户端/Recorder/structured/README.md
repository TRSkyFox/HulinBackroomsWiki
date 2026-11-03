# 结构化 logs → Wiki staging

> **2026-11-04 补充**：本文件描述的是**旧自动 OCR 管线**（`run.mjs`）。新资料请用人工记录器＋`import-manual.mjs`，流程见根目录 `README.md` 第 6.2 节。指令中的 `记录器/` 已改名为 `客户端/Recorder/`。

这个文件夹是**管线唯一来源**。`客户端/app/structured` 与
`客户端/dist/Expedition/structured` 都是同步副本，不要在那两份上改。

```powershell
node 客户端/Recorder/structured/sync.mjs           # 同步到 app／便携版
node 客户端/Recorder/structured/sync.mjs --check   # 有漂移就 exit 1
```

## 流程

```text
客户端/Recorder/backroom-logger.ps1
  → session/structured.jsonl     每帧的区块、坐标、文字
  → font-correct.mjs             形近字校正（只改 SAFE 清单内的字）
  → clean-levels.mjs             删掉换层瞬间的假楼层档（只有恢复画面／空白）
  → parse.mjs                    规则式 UI 状态与候选记录
  → event-parse.mjs              路口／事件候选
  → input-bind.mjs               绑定玩家实际输入
  → event-import.mjs             事件 staging（默认不写正式 events.json）
  → transcript／consensus        逐字稿与共识稿
  → import-wiki.mjs              简→繁＋生成 Wiki staging（不直接改 content）
  → validate.mjs                 validation.json
  → merge-final.mjs              dry-run 报告（--confirm 才写）
  → wiki/content/build.mjs       重建站点
```

关掉游戏时，`postprocess.mjs` 会自动呼叫 `run.mjs --auto`：
高信心楼层／配色自动写正式 Wiki；低信心或 validation error 只留 staging。
事件先用固定区域边界清洗（难度／选项／结果），干净的才自动写 `events.json`；
RapidOCR 会对 `linecrops` 重认，失败时退回 Windows OCR。

## 使用

```powershell
# 收工自动导入（postprocess 默认就是这个）
node 客户端/Recorder/structured/run.mjs <session> --auto

# 只生成 staging／审查报告
node 客户端/Recorder/structured/run.mjs <session>

# 明确要把事件写进正式楼层（人工审查后）
node 客户端/Recorder/structured/run.mjs <session> --apply-events

# 明确要合并楼层／实体并重建
node 客户端/Recorder/structured/run.mjs <session> --merge
```

分步跑：

```powershell
node 客户端/Recorder/structured/event-import.mjs <session> --apply
node 客户端/Recorder/structured/merge-final.mjs <stagingDir> --confirm
node wiki/content/build.mjs
```

## 审查要看的三个档

```text
staging/validation.json          硬错误＝不能合并；warning 逐条看
staging/events-review.jsonl      每个事件候选：accepted／low_confidence／unreadable_or_noise
staging/merge-final-report.json  哪些楼层／实体会被改、哪些被跳过、备份在哪
```

低于 0.8 的项目不要合并。没有正式页的实体目前会跳过（不会自动新增）。

## 目前规则

- 楼层：只认画面顶端的 `Level N「名稱」`；用既有 `content/floors` 比对
- 配色：先取 main 区底色；既有楼层的 palette 优先
- 介绍：只取楼层标题后、选项前的连续文字块（仍可能被大行距截断，见下）
- 岔路：以选项面板坐标与“要走哪／要怎么做”判定
- 战斗：只记“会进入战斗”＋敌人名字，不记流程
- 选择后：只接受建立 choice 之后的“→ 你决定：…”

## 重要限制

- 这是 **staging**，不是可直接信任的正式资料。OCR 仍会有错字、选项重复、敌人技能／掉落缺失。
- **楼层介绍仍可能被 OCR 截断**：引号误杀已修，但断行／大行距仍会漏。
  `merge-final` 不会自动覆盖既有介绍；差异写进 `_docCandidate`，要人工比对。
- **敌名有清理但非万能**：`cleanEnemyName` 会去掉 A/B、前缀、UI 标签，并要求连续 2 帧一致；
  罕见变体仍可能留下，低信心只进 staging。
- **配色取样可能为 0 笔**：楼层标题 OCR 不稳 → 没有 floor-shots → `floor-palettes.json` 是空的。
- 截图只作 OCR 暂存，转录后删除，不进 Wiki。

## 简繁转换

`i18n.mjs` 负责。**不要**把旧 `wiki/_i18n`（已封存）的繁→简表按位置反推：

- 那张表本身混有反序 pair（例：`发 发`）。
- 反推会把“四面”变“四面”、“皇后”变“皇后”。
- 现在用 `char-freq.json`（Wiki 现有字频）判断方向；两边都在用就不转。
- 词表处理一简对多繁（头发／里面／干净／一只／面条／游戏…）。
- 改规则后跑：`node tests/i18n.test.mjs`。

## 字形校正

`font-correct.mjs` 只接受 `SAFE` 内的形近字（夭→天、潦→流、证→证、板→板、别→别），
不再把 `面` 改成 `面`。测试用 `tests/fixtures/font-line.png`，不依赖 pipe／PowerShell。

## 测试

```powershell
node tests/i18n.test.mjs
node tests/font.test.mjs
node tests/data.test.mjs
node tests/verify-final.mjs     # 以输出 summary 为准
```

## Palette 取样

`run.mjs` 会呼叫 `palette-from-shots.ps1`：读 `floor-shots/*.png`，写 `floor-palettes.json`，
然后删掉处理过的截图。取样规则：

- `bg`：四角取样（整张图的众数常是内容面板，不是楼层底色）
- `card`：面板内、和底色不同的主要色
- `line`：沿 card 色范围的边界取（避免拿到文字反锯齿的色边）
- `acc` / `tx`：标题区／内文的亮色

空结果会写 `[]`，不是 0 byte。也可以用人工校准工具：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File palette-sample.ps1 -Image <png> -Out <json>
```

不会自动把推测色写进正式 content。
## 多敌人的 A/B 临时编号

同一场战斗出现多只同型敌人时，敌名常被加上 `A`、`B`。这些不是不同敌人：

```text
嗡鸣体 A、嗡鸣体 B  →  嗡鸣体
回声人影 A、回声人影 B → 回声人影
```

parser 与 importer 会剥除尾端单一字母，并把同场掉落／抗性合并到同一 entity。

## 敌人前缀

这些前缀不是新物种，会先剥除再比对：

```text
巨大、强壮、不死、嗜血、恐怖、饥饿、恶梦般、敏捷、坚硬、残影、守护、呼唤、
时间错乱、猎杀、吞噬、群体、脆弱、统率、不可名状、吞光、适应、越打越凶、不灭、
贪婪、带刺、粘人
```

例：`巨大的 强壮的 猎犬` → `猎犬`。

## 观测累积与可信度

staging 会多存：

```text
evidence/entities/<entity>.jsonl
evidence/floors/<floor>.jsonl
```

每个实体保留：每次观测的敌名／介绍／技能／掉落／抗性、各字段次数、既有正式内容。

选择规则：

1. 正式 content 已有的介绍／字段优先，不覆盖
2. 没有策展资料时，取出现次数最多、长度较完整的 OCR 文字
3. 掉落／抗性／技能以联集保留，依次数排序
4. 低于 0.8 的候选留在 staging

## 事件写入方式

事件唯一来源是 `floors/<層>/events.json`。`merge-final` 默认**不再**把事件塞进
`floor.json` 的 doc（旧的 `AUTO-EVENTS` 会生成第二份事件）；只有调试时才加：

```powershell
node merge-final.mjs <stagingDir> --confirm --with-doc-events
```
