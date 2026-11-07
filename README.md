# 狐霖的后室 RPG｜Hulin Backrooms Wiki

> 非官方 Wiki ＋ OCR 记录器 ＋ 玩家客户端  
> 最后整理：2026-11-04  
> 规则以 `AGENTS.md` 为准；本文件是目前架构、状态与新开发者上手的入口。
>
> **关于繁体字**：正文使用简体中文；文中保留的繁体只出现在**实际文件名、游戏原文、Wiki 条目名**（例如 `啟動.bat`、`記錄-*.zip`、`212-發光深淵`），照抄是为了对得上真实路径。

---

## 1. 项目目标

把 Unity WebGL《狐霖的后室 RPG》的实际游玩画面，变成一条**可追溯、可验证**的 Wiki 资料流程：

```text
游戏画面
→ 分区 OCR／人工框选
→ 结构化 JSONL（保留原始文字、坐标、信心、帧号、时间、楼层）
→ staging／待处理审核
→ wiki/content
→ node wiki/content/build.mjs
→ wiki/site/index.html（离线单档网站）
```

三个不能退让的原则：

1. **只记录实际看过的内容。** 事件、路径、战斗、结局、配色、物品效果都不可以猜。
2. **不确定就进 staging／待处理。** 低信心或未确认的资料不覆盖正式 `wiki/content`。
3. **正常导入流程不依赖 AI。** 规则、校验、staging、确认流程必须能独立跑；AI 只协助开发与分析。

**明确不做自动玩。** 游戏作者不允许，任何 `auto/`、`quark/`、`sessions/`、自动玩入口都不要恢复。

---

## 2. 项目用途（三条产品线）

| 产品线 | 位置 | 给谁 | 主要产出 |
|---|---|---|---|
| 离线 Wiki | `wiki/` | 玩家、读者 | `wiki/site/index.html`（单档，字体与资料内嵌） |
| 记录器／客户端 | `客户端/` | Wiki 主人、贡献者 | `客户端/Recorder/logs/manual/*.jsonl`、`記錄-*.zip` |
| 管线／导入 | `客户端/Recorder/structured/`、`tools/` | Wiki 主人 | `wiki/_incoming/*` staging、正式 `wiki/content` |

- **Wiki 主人**：用新记录器把看到的楼层／事件／物品记下来，导入 `wiki/content`，重建网站。
- **一般玩家**：只拿便携版或记录器，导出 `記錄-*.zip` 给 Wiki 主人；不需要碰 Wiki、不需要 Node。
- **新开发者**：维护 Wiki 前端、OCR 记录器、导入管线与内容质量。

---

## 3. 系统框架

目前同时存在两条资料路径：**新的人工辅助记录器（现行方向）**与**旧的自动 OCR 管线（兼容／历史资料）**。

```text
                              ┌──────────────────────────────────────────┐
                              │ 新：ExpeditionRecorder（人工辅助，现行） │
游戏（Unity WebGL <canvas>）  │  Q 选模式／E 框选                        │
        │                     │  → Recorder/logs/manual/*.jsonl          │
        │                     │  → import-manual.mjs                     │
        │                     └──────────────────┬───────────────────────┘
        │                                        │
        ├─ 旧：backroom-logger.ps1 ──────────────┤
        │   → logs/backroom-*/structured.jsonl    │
        │   → run.mjs（parse／event／input-bind） │
        │   → import-wiki／validate／merge-final  │
        │   → wiki/_incoming/<session>            │
        │                                        ▼
        │                              wiki/content（唯一内容来源）
        │                                        │
        │                              node wiki/content/build.mjs
        │                                        ▼
        │                              wiki/site/index.html
        │
玩家资料包：記錄-*.zip
  → tools/ExpeditionArchive.exe
  → wiki/_incoming/player-*
  → import-manual.mjs
  → wiki/content
```

重点：

- `wiki/content/` 是**唯一内容来源**；`wiki/site/index.html` 是建置产物，不要手改。
- `客户端/Recorder/structured/` 是**管线唯一来源**；`客户端/structured/`、`客户端/dist/Expedition/structured/` 是 `sync.mjs` 的同步副本，不要手改。
- 新记录器不依赖旧的 `backroom-logger.ps1`；旧管线仍保留，用来处理既有 logs 与旧格式。
- 新记录器的输出是 `客户端/Recorder/logs/manual/` 下的 JSONL，导入工具是 `import-manual.mjs`。

---

## 4. 目录结构

```text
D:\Project\HulinBackroomsWiki\
├─ AGENTS.md                      硬规则（优先读）
├─ README.md                      本文件：架构、状态、上手
├─ Expedition Archive.bat         启动玩家资料包导入工具
├─ 文档\
│  ├─ AGENT-接手.md               2026-11-03 交接快照（部分过时）
│  ├─ 要求文档.md                 验收标准与禁止事项
│  ├─ 事件结构规范.md             事件／路口／路径 schema
│  ├─ 背景设定.docx               世界观
│  ├─ 規例-跟這位使用者合作的方式.md
│  └─ 报告\                       历史修复与验收记录
├─ wiki\
│  ├─ content\                    ★ Wiki 唯一内容来源
│  │  ├─ floors\<id-name>\        floor.json／events.json／endings.json／design.css
│  │  ├─ characters\<角色>\       character.json／events.json
│  │  ├─ entities\<实体>\         entity.json
│  │  ├─ items\<分类>\<物品>\     item.json
│  │  ├─ shops\<商店>\            shop.json
│  │  ├─ pages\ recipes\ enhancements\
│  │  ├─ template.html            单档网站外壳
│  │  ├─ build.mjs                建置脚本
│  │  └─ README.md                内容 schema
│  ├─ site\index.html             建置产物（git ignore）
│  ├─ _incoming\                  staging（git ignore）
│  ├─ smoke.mjs                   前端冒烟测试
│  └─ import-package.ps1          旧 .expedition.zip 导入
├─ 客户端\
│  ├─ ExpeditionRecorder.cs       控制台＋OCR 浮层＋人工记录器（唯一来源）
│  ├─ ExpeditionRecorder.exe      编译产物（git ignore）
│  ├─ ExpeditionPlayer.cs         WebView2 游戏客户端
│  ├─ ExpeditionClient.exe        客户端产物（git ignore）
│  ├─ Microsoft.Web.WebView2.*.dll、WebView2Loader.dll（git ignore）
│  ├─ build.ps1                   重新编译 panel／client
│  ├─ start.ps1                   复制到 %LOCALAPPDATA% 并启动
│  ├─ 啟動.bat                    对外入口
│  ├─ volume-watch.ps1            音量记录
│  ├─ fonts\                      字体（git ignore）
│  ├─ Recorder\
│  │  ├─ backroom-logger.ps1      旧 OCR 记录器本体（唯一来源）
│  │  ├─ palette-from-shots.ps1   配色取样
│  │  ├─ characters.json          角色称号对照
│  │  ├─ s2t.json                 简→繁转换表
│  │  ├─ structured\              ★ 管线唯一来源
│  │  │  ├─ run.mjs              旧管线入口
│  │  │  ├─ import-manual.mjs    新人工记录导入
│  │  │  ├─ sync.mjs             同步到 app／dist
│  │  │  ├─ tests\               测试与 verify-final
│  │  │  ├─ rapid-primary-service\ OCR 服务（git ignore）
│  │  │  └─ *.otf                 字体（git ignore）
│  │  └─ logs\                    原始 logs＋manual 记录（git ignore，只读）
│  ├─ structured\                 sync 副本（git ignore，不要手改）
│  └─ dist\Expedition\            便携版成品（git ignore，约 825 MB）
├─ tools\
│  ├─ ExpeditionArchive.cs        玩家资料包导入工具源代码
│  ├─ ExpeditionArchive.exe       编译产物（git ignore）
│  └─ build-import.ps1            编译脚本
├─ 导出\                          记录器默认导出目录（git ignore）
├─ 例子截图\                      参考截图（目前 1 张结局图）
└─ 备份\                          legacy 压缩包与站点备份（git ignore，约 172 MB）
```

> `客户端/app/` 不是 repo 内的目录；`start.ps1` 会把可执行文件复制到 `%LOCALAPPDATA%\Expedition\app`，并把 `app\logs` junction 回 `客户端\Recorder\logs`。

---

## 5. 内容模型与硬规则

详细 schema 见 `wiki/content/README.md`，这里只列最常踩的：

```text
floors/<id-name>/floor.json     楼层资料、正文、配色
floors/<id-name>/events.json    该层事件／岔路（唯一来源）
floors/<id-name>/endings.json   该层结局（唯一来源）
floors/<id-name>/design.css     该层专属样式
characters/<角色>/character.json
characters/<角色>/events.json   角色专属事件
characters/_meta.json           角色顺序、永久能力、状态表
characters/_skills.json         战斗技能表
entities/<实体>/entity.json
items/<分类>/<物品>/item.json    weapon／armor／accessory／tool／consumable／ammo
shops/<商店>/shop.json
pages/pages.json
recipes/recipes.json
enhancements/enhancements.json
```

- 楼层 key 一律 `id|name`；`1/7/9` 共用 id，不要只用 id 判断解锁或配色。
- 事件与结局**只能**放该楼层文件夹；不要恢复全域 `content/events.json` 或 `content/endings/`。
- 角色事件放 `characters/<角色>/events.json`；通用事件放 `events/common/events.json`（由新管线支持）。
- `_status: 'name-only' | 'id-only'` 只给只有名字／只有编号的占位楼层；有正文就不要留。
- 楼层 `doc` 只放页面会显示的“环境”引言 HTML，不要混事件、商店、战斗流水。
- 玩家账号名 `狐天`／`狐夭` 不进正文，一律换成“你”；`天狐` 是角色，保留。
- 内容 JSON 不要写绝对路径；截图不要放进 `wiki/content` 或 `wiki/site`。

---

## 6. 执行流程

### 6.1 建置与验收 Wiki

```powershell
cd D:\Project\HulinBackroomsWiki
node wiki\content\build.mjs
node wiki\smoke.mjs
node 客户端\Recorder\structured\tests\verify-final.mjs
```

- `build.mjs` 产物：`wiki/site/index.html`（约 2.88 MB）。
- `smoke.mjs` 会用 headless Edge 实际载入 8 个入口；改 `template.html` 后必跑。
- `verify-final.mjs` 是目前最完整的验收（内容、schema、站点、同步副本）。
- `verify-final` 的 `root` 由脚本位置推算，不写死磁盘路径。

### 6.2 新记录器（现行方向）

```text
客户端\啟動.bat
  → start.ps1 先编译（build.ps1）
  → 复制到 %LOCALAPPDATA%\Expedition\app
  → ExpeditionRecorder.exe（控制台）
  → 按“开启客户端”启动 ExpeditionClient.exe
```

默认每次启动都会先编译；加 `-NoBuild` 可跳过编译，只启动现有 exe。运行前先关闭旧的记录器面板，否则面板 exe 被占用时会提示并停止。

操作（默认键，可在“按键设置”改）：

- **Q**：按住出现圆盘，滑向模式后放开；再按 Q 结束当前记录。
- **E**：框选／记录下一步。
- **C**：扫描/框选时取消这一步；不在扫描时再按 C 取消整条记录。
- 「按鍵設定／快捷鍵：開／關」在“記錄”组内单独一行；关闭时 Q/E/C 直接交给游戏，不再触发记录器。Q/E/C、解析度、界面语言、字号倍率与快捷键开关会保存到 `hotkeys.json`。
- 面板底部“設置”列有“說明／繁體／簡體／English”，可切换繁中、简中或 English 界面文字；說明視窗內容也會跟隨語言切換。
- 面板「记录」区按钮跟随按键设置：Q 空闲为“开始记录”，记录中高亮为“结束记录”；E 为“记录下一步”；C 为“取消记录”。
- 圆盘六种模式：**事件／物品／楼层介绍／出口／结局／商店**。
- 事件模式：第 1 步框事件名＋介绍＋选项；第 2 步以后框选择、结果、检定、战斗、跳层。没有后续就再按 Q 结束。
- 客户端组的“刷新网页”会向游戏 WebView 发送 F5；若焦点无法切到游戏，则直接把 F5 送进 WebView 的 render 子窗口。
- “清理缓存”需要连按三次（前两次为确认）；会先关闭客户端，只删除 WebView2 缓存目录，保留登录和存档。
- 结束窗单选事件类型：普通／自由／岔路／角色／合力／通用。普通／自由／岔路可填事件出现楼层并进楼层事件；角色／合力进角色事件；通用进通用事件。选项导向只剩跳层／进入战斗，并可选“休息处”标签。
- 属性条件只记力量／敏捷／体质／感知／智力／魅力与成功／失败，不记骰值和计算；选项保留 OCR 颜色。
- 楼层介绍模式会优先读取左上角的 `Level …「…」`；有左上角楼层时以它为准。
- 楼层保存前判断类型：OCR 识别出 `Level`＋数字时为常规楼层，ID 只能改数字；否则为异常楼层，ID／名称可自由输入。楼层窗只留“类型／楼层”两列，常规为 `Level`＋数字＋名称，异常为 ID＋名称；已记录编辑器同样合并为一列。配色只显示已记录三色生成的范例标题／文字预览，字体跟随字号，不能改色值；主题为“明亮／昏暗”。
- 楼层归属默认“事件发生地”；可在结束窗改成自己填。跳层目标另存 `jumpTo`。
- “已记录”里选中条目后，右侧直接显示结构化编辑字段，不需要“編輯選中”或手改 JSONL 原文；事件名／楼层／归属等识别栏固定只读。修改后停顿 0.6 秒自动保存，关闭或切换记录前再保存一次，不再显示“保存修改／取消”。窗口左右分区自适应，设置区的“字號”提供 1x／1.2x／1.4x／1.6x 四个选项，可调整个记录器；除主面板外都可自由拉伸，窗口尺寸随字号调整。已记录、说明、按键设定、导出均为非模态窗口，开着时主面板仍可用。「本次記錄」浮窗贴游戏窗口右侧持续跟随，列出本轮记录并默认直接编辑最后一条。
- 记录结果写到 `客户端\Recorder\logs\manual\`：

```text
events-YYYYMMDD.jsonl
items-YYYYMMDD.jsonl
floors-YYYYMMDD.jsonl
exits-YYYYMMDD.jsonl
endings-YYYYMMDD.jsonl
shops-YYYYMMDD.jsonl
pending-YYYYMMDD.jsonl
draft-YYYYMMDD.jsonl
```

- 面板上的“已记录”可以看分类清单；“待处理”是固定右侧浮窗，可直接改；“导出”生成 `記錄-YYYYMMDD-HHMM.zip`（内含 `記錄.json`＋`記錄.md`）。

导入本机：

```powershell
node 客户端\Recorder\structured\import-manual.mjs
# 看 dry-run 报告，确认无误后：
node 客户端\Recorder\structured\import-manual.mjs --confirm
```

### 6.3 旧 OCR 管线（既有 logs／兼容）

```powershell
node 客户端\Recorder\structured\run.mjs <sessionDir>
node 客户端\Recorder\structured\run.mjs <sessionDir> --auto
node 客户端\Recorder\structured\run.mjs <sessionDir> --apply-events
node 客户端\Recorder\structured\run.mjs <sessionDir> --merge
```

- 默认只生成 staging，不写正式 `wiki/content`。
- `--auto` 会把高信心楼层／配色写正式；事件仍建议人工看 review。
- `merge-final` 默认 dry-run；`--confirm` 前先看 `validation.json`、`merge-final-report.json`，并且要有备份。
- 这条管线的 OCR 错误率仍然偏高；新资料请优先走 6.2。

### 6.4 玩家资料包

```text
玩家：记录器“导出”→ 記錄-YYYYMMDD-HHMM.zip
主人：Expedition Archive.bat → 选 zip →“只检查”→“导入 Wiki”
```

- 工具会展开到 `wiki/_incoming/player-*`，再呼叫 `import-manual.mjs`。
- 只检查不会改正式 Wiki；导入前会备份。
- 命令行：

```powershell
tools\ExpeditionArchive.exe --repo D:\Project\HulinBackroomsWiki
```

### 6.5 管线同步

```powershell
node 客户端\Recorder\structured\sync.mjs
node 客户端\Recorder\structured\sync.mjs --check
```

- 只改 `客户端/Recorder/structured/`。
- `sync.mjs --check` 有漂移就 exit 1；漂移没修好之前不要说“验收全过”。

---

## 7. 目前状态（2026-11-04 实测）

> 数字会变；下面是这次整理时的实测，不是永久保证。要现在的值就重跑指令。

### 7.1 内容

| 项目 | 数量 |
|---|---:|
| 楼层 | 96 |
| 　一般楼层 | 38 |
| 　`name-only` | 41 |
| 　`id-only` | 17 |
| 有楼层介绍（doc） | 37 |
| 有 `events.json` 的楼层 | 16 |
| 有 `endings.json` 的楼层 | 0 |
| 有配色（palette）的楼层 | 36 |
| 有 `design.css` 的楼层 | 2 |
| 角色 | 104 |
| 实体 | 71 |
| 物品 | 465 |
| 商店 | 17 |
| 建置后单档大小 | 2,886,505 bytes |

- 37 层有介绍、16 层有事件；也就是 **22 层有介绍但还没有事件**。
- 58 层仍是占位（41 `name-only`＋17 `id-only`）；其中 9 层是 `▒▒▒▒▒` 乱码名，没有可恢复的名字，不要编造。
- `212-發光深淵` 是一般楼层但没有完整介绍（`_introMissing`）。

### 7.2 验收

```text
verify-final   845 / 845，failed 0，warnings 5
smoke          8 / 8 入口，Console 无错误
sync --check   84 项对齐
build          exit 0
```

5 类 warning（不是 failed，但都是内容债）：

| warning | 数量 | 意思 |
|---|---:|---|
| `item-empty-desc` | 271 | 物品没有 `desc`（多数是用户尚未截图，不要代写） |
| `item-empty-eff` | 101 | 物品有 `eff` 字段但内容空 |
| `item-no-eff-field` | 26 | 物品完全没有 `eff` 字段 |
| `dangling-item-refs` | 116 | 角色／实体／商店／配方引用找不到对应物品（含 OCR 杂讯与真缺条目） |
| `absolute-paths` | 54 | JSON 内仍有 `D:\BACKROOMS\...` 历史绝对路径（多在 `_staging`／`_observations`） |

### 7.3 Git／体积

- 搬迁前：**108 commits**、887 个 tracked files、约 8.2 MB（不含本次新增的 README 与截图）。
- `.git` 约 7.8 MB。
- 本机完整工作区约 **1.8 GB**，大部分是 git ignore 的成品与原始资料：

| 目录 | 大小 | 进 git？ |
|---|---:|---|
| `客户端/dist` | 824.8 MB | 否 |
| `备份` | 172.4 MB | 否 |
| `客户端/Recorder/logs` | 51.4 MB | 否 |
| `wiki/_incoming` | 50.3 MB | 否 |
| `wiki/site` | 5.6 MB | 只 ignore `index.html` |
| `文档` | 4.1 MB | 是 |

---

## 8. 当前问题与风险

### P0：会直接影响“能不能用”或“新开发者能不能跑”

1. **新记录器还没有完成实战验收。**
   - `ExpeditionRecorder.cs` 已经有 4,921 行、六种模式、圆盘、待处理、导出、导入工具，编译与自测都过。
   - 但最后一份真实 manual 记录（`events-20261104.jsonl`，00:09）仍然出现：step 1 的 `floorId`／`title` 空白、结果文字被切成“办”“公室。”两段。
   - 这代表“能记录”和“能稳定产出可导入的资料”之间还有距离；在大量记录前，先拿真实画面把六种模式各跑一次。

2. **部分旧文档仍是历史快照。**
   - 本文件、`AGENTS.md`、`客户端/README.md`、`wiki/content/README.md` 已按现行状态更新；`文档/AGENT-接手.md`、`客户端/Recorder/structured/README.md` 与 `文档/报告/` 仍是旧流程／旧统计。
   - `文档/AGENT-接手.md` 的 824 checks／13 events／4 endings／9 id-only 等数字已过时，读时以本文件第 7 节为准。

3. **Fresh clone 不能直接跑记录器。**
   - `.gitignore` 排除了：`客户端/*.exe`、`客户端/*.dll`、`客户端/fonts/`、`客户端/dist/`、`客户端/structured/`、`客户端/Recorder/structured/rapid-primary-service/`、`客户端/Recorder/structured/*.otf`。
   - 因此 clone 下来只有源代码与内容；没有 WebView2 DLL、OCR 服务（约 18 MB）、CJK 字体（2×16 MB）、便携版（825 MB）。
   - `build.ps1` 需要 WebView2 DLL 才能编译；`start.ps1` 需要 `客户端/structured/rapid-primary-service` 与字体才能跑 OCR。
   - 目前要靠 maintainer 另外给 `客户端/dist/Expedition` 或一包 assets。这是新开发者第一个会撞到的墙。

4. **两条管线并存，容易出现“两个真相”。**
   - 旧 `backroom-logger.ps1` → `run.mjs` 仍会写 staging／正式 content；新记录器 → `import-manual.mjs` 也会写 content。
   - 目前没有统一的“新资料只走新管线”开关或迁移计划；长期要决定旧管线是只读历史，还是完全退役。

5. **导入链还有缺口。**
   - `import-manual.mjs` 已支持 events／items／floors／exits／endings，但**还没有 shops**；记录器导出的商店资料会被跳过。
   - `exits` 会写 `exits.json` 与目标楼层的 `entrances.json`，但 Wiki 的“出入口”页仍是占位，`build.mjs`／`template.html` 还没有把 `entrances.json` 画出来。
   - 结局目前 0 笔；`endings.json` 的 schema 与分类已接好，但没有真实资料。
   - 待处理类型只做了“未知楼层／出口未填／物品资料不全／角色未识别”等少数；条件未归类、配色未定、出入口连锁不上还没进清单。

### P1：内容债（不要代写，等真实截图／场次）

- 59 层没有 doc；58 层是占位；22 层有 doc 但 0 事件；16 层有事件；0 层有结局。
- 465 件物品中 271 件没有 `desc`、101 件 `eff` 空、26 件没有 `eff` 字段。
- 116 处角色／实体／商店／配方引用找不到物品。
- 54 个 JSON 仍含 `D:\BACKROOMS\...` 绝对路径。
- `212-發光深淵` 缺完整介绍；`14-冷漠病院`、`40-有害的衰敗` 缺配色；`1-停車區` 只有底色。
- 17 层 `id-only` 没有可恢复的名字（其中 9 个是 `▒▒▒▒▒`）。

### P2：工程与协作

- 没有 CI；`build`／`smoke`／`verify-final` 目前靠手跑。
- 没有 `package.json`；管线是纯 Node ESM，没有 npm 依赖，这是优点，但也没有统一的 `npm test` 入口。
- Git identity 目前是 local 的 `BACKROOMS <backrooms@localhost>`；公开 commit 前应改成维护者自己的名字／GitHub email。
- 根目录的 `启动记录器.bat` 已被删除；入口统一为 `客户端\啟動.bat`。
- `例子截图/` 只有 1 张 2 MB 的结局参考图；这次随仓库提交，供新开发者对照，不属于 Wiki 内容。
- `备份/`、`dist/`、logs 都很大；如果换机器，不能只 clone repo，还要另外搬这些资料。

---

## 9. 已知发展方向

### 短期（先把“能稳定记一条事件”做完）

1. **新记录器真实场次验收**：事件／物品／楼层介绍／出口／结局／商店各跑一次，确认：
   - 楼层与事件标题有抓到；
   - 选项、需求（属性／角色／物品／消耗）、检定、战斗、跳层正确；
   - 结果文字不被行距切断；
   - 角色从右侧队伍栏自动判断，不需要手填；
   - 待处理可以修改并回写。
2. **修正 manual schema 的合并／去重**：同名事件合并 branches、`unseen` 选项、`leadsTo`、商店多页。
3. **补完剩余旧文档**：`文档/AGENT-接手.md`、`客户端/Recorder/structured/README.md`、`文档/报告/` 仍是历史快照；新文档以本文件为准。
4. **Asset bootstrap**：加一支 `fetch-assets.ps1`／release 说明，让新开发者知道去哪里拿 WebView2 DLL、OCR 服务、字体、便携版。

### 中期（把导入链补完）

1. `import-manual.mjs` 支持 shops（对齐 `wiki/content/shops/<商店>/shop.json`）。
2. `build.mjs`＋`template.html` 实现出入口页，读 `exits.json`／`entrances.json`。
3. 结局资料开始进 `endings.json`，补 `bad/settle/neutral` 分类与后日谈拆分。
4. 待处理类型补齐：条件未归类、配色未定、出入口连锁不上、低信心乱码、通用事件未分类。
5. 决定旧 OCR 管线的定位：只处理历史 logs，或完全退役；避免两条管线同时写正式 content。

### 长期（内容与协作）

1. 用真实场次补楼层／事件／结局／物品；优先补“有介绍但 0 事件”的 22 层，以及 271 件缺 `desc` 的物品。
2. 建立 CI（GitHub Actions）：`build` → `smoke` → `verify-final` → `sync --check`。
3. 建立 release 流程：便携版 zip、OCR 服务、字体、WebView2 DLL 分开发布，不进 git。
4. 新开发者 onboarding：第一条 issue 可以是“shop 导入”或“出入口页”，不需要先碰 OCR。

---

## 10. 新开发者上手

### 10.1 环境

- Windows 10/11（x64）
- PowerShell 5.1（项目脚本以 5.1 为准，不要用 pwsh 7 的差异行为）
- .NET Framework 4.x 的 `csc.exe`（`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）
- Node.js 22（目前实测 v22.11.0）
- WebView2 Runtime（客户端）
- 项目 assets（见 10.3）

### 10.2 第一次跑

```powershell
git clone git@github.com:TRSkyFox/HulinBackroomsWiki.git
cd HulinBackroomsWiki

node wiki\content\build.mjs
node wiki\smoke.mjs
node 客户端\Recorder\structured\tests\verify-final.mjs
node 客户端\Recorder\structured\sync.mjs --check
```

- 建置后用浏览器开 `wiki\site\index.html` 看一次，不要只看 exit code。
- 这时只能改内容／前端／管线，还不能跑记录器（缺 assets）。

### 10.3 取得 assets（目前不在 git）

向 maintainer 取得下列其中一种：

- `客户端\dist\Expedition\`（便携版，含 WebView2 DLL、OCR 服务、字体、node.exe），或
- 分别取得：
  - `客户端\Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`、`WebView2Loader.dll`
  - `客户端\Recorder\structured\rapid-primary-service\`（含 `_internal`）
  - `客户端\fonts\NotoSansCJKtc-Regular.otf`、`客户端\Recorder\structured\NotoSansCJK*.otf`

放到对应位置后：

```powershell
powershell -ExecutionPolicy Bypass -File 客户端\build.ps1
客户端\啟動.bat
```

### 10.4 适合第一个做的任务

1. 跑一次 `verify-final`，看 5 类 warning，选一类做报告（不要直接改内容）。
2. 读 `wiki/content/floors/0-大廳/floor.json` 与 `events.json`，理解 schema。
3. 用 `import-manual.mjs` 对 `客户端/Recorder/logs/manual/` 做 dry-run，看它会写哪些文件。
4. 挑一个不需要 OCR 的 issue：
   - `import-manual.mjs` 支持 shops；
   - 出入口页读 `entrances.json`；
   - 文件路径／统计更新；
   - 建 CI。

### 10.5 协作规则（摘要）

- 先读 `AGENTS.md`；有冲突时：用户当下指示 → `AGENTS.md` → `文档/AGENT-接手.md` → `文档/要求文档.md`。
- 不要自动玩；不要批量关用户程序；不要对用户的游戏窗口抢焦点。
- 不要编造；不确定就 staging；没有截图不要生成配色。
- 不要直接改 `wiki/site/index.html`；改 `wiki/content` 后重建。
- 不要手改 `客户端/structured` 或 `dist/.../structured`；改 `客户端/Recorder/structured` 后跑 `sync.mjs`。
- 截图只拿来读字，读完删；不要放进 `wiki/content`。
- 玩家名 `狐天`／`狐夭` 不进正文；`天狐` 是角色。

---

## 11. Git 与上传

- Remote：`git@github.com:TRSkyFox/HulinBackroomsWiki.git`
- Branch：`master`
- 目前 repo 是从旧工作区 `D:\BACKROOMS` → `D:\HulinBackroomsWiki` → `D:\Project\HulinBackroomsWiki` 搬过来的；旧的 108 个 commits 保留。
- Commit 风格（沿用历史）：`feat:`、`fix:`、`docs:`、`chore:`、`refactor:`。
- 不进 git：`客户端/Recorder/logs/`、`客户端/dist/`、`客户端/fonts/`、`客户端/structured/`、`客户端/*.exe`、`客户端/*.dll`、`备份/`、`wiki/_incoming/`、`wiki/site/index.html`、OCR 服务与 `.otf`。
- 大档策略：repo 只放源代码＋内容＋测试 fixture；便携版与 OCR 服务用 release／云端硬盘传，不要用 git LFS 硬塞 825 MB。

第一次上传（如果还没 push）：

```powershell
git status
git add -A
git commit -m "docs: add project summary and refresh onboarding"
git push -u origin master
```

> 如果 GitHub 回 `Repository not found`，先确认 SSH key 已加到 GitHub，且账号有 `TRSkyFox/HulinBackroomsWiki` 的权限。

---

## 12. 文件索引

| 文件 | 用途 |
|---|---|
| `AGENTS.md` | **硬规则**：能做什么、不能做什么、修改流程 |
| `README.md` | 本文件：架构、现状、上手、roadmap |
| `文档/AGENT-接手.md` | 2026-11-03 交接快照；数字与流程已过时，看本文件第 7 节 |
| `文档/要求文档.md` | 现行验收标准与禁止事项 |
| `文档/事件结构规范.md` | 事件／路口／路径／判定 schema |
| `文档/背景设定.docx` | 游戏世界观（逆元、负域、智慧体、存续设施、终端） |
| `文档/規例-跟這位使用者合作的方式.md` | 与用户合作的沟通规则 |
| `文档/报告/` | 历史修复与验收记录，只当历史 |
| `wiki/content/README.md` | 内容 schema |
| `客户端/Recorder/structured/README.md` | 旧管线细节（部分路径过时） |

---

## 13. 本次整理做了什么（2026-11-04）

- 实际跑过：`build`（exit 0）、`smoke`（8/8）、`verify-final`（845/845，5 warnings）、`sync --check`（84）。
- 新增本文件，作为目前架构、状态、问题与 roadmap 的单一入口。
- 把工作区从 `D:\HulinBackroomsWiki` 搬到 `D:\Project\HulinBackroomsWiki`，保留 Git 历史与未提交的内容变更。
- 更新主要文件与脚本内的旧路径（历史报告与 `_staging` 观测记录保留原样，因为它们是历史出处）。
