# 工作约定（AGENTS.md）

> 2026-11-01 全面重写。工作区：`D:\Project\HulinBackroomsWiki`。
> 这份只留“现在仍然有效”的规则；历史事故、旧路径、旧管线已移到
> `文档/报告/` 与 `备份/legacy-wiki-20261101.zip`。
>
> 有冲突时，优先顺序：
> **用户当下指示 → 本文件 → `文档/AGENT-接手.md` → `文档/要求文档.md` → 旧文件。**
>
> **2026-11-04 迁移**：工作区已搬到 `D:\Project\HulinBackroomsWiki`。
> 项目总结、现行统计与新记录器流程见根目录 `README.md`；本文件第 2、3、6 节已同步为搬迁后的现况。

---

## 1. 这个项目是什么

《狐霖的后室 RPG｜Hulin Backrooms RPG》的**非官方 Wiki ＋ OCR 记录器 ＋ 玩家客户端**。

- 游戏是 Unity WebGL，`#game` 是 `<canvas>`，DOM／无障碍树读不到内容，只能靠 OCR。
- 三条产品线：
  1. `wiki/`：内容与离线单档网站。
  2. `客户端/Recorder/`：OCR 记录器与 staging 管线。
  3. `客户端/`：ExpeditionRecorder 控制台＋ExpeditionClient 游戏客户端，附带 logs 记录与便携版。
- **不做自动玩**。游戏作者不允许，任何 `auto/`、`quark/`、`sessions/`、自动玩入口都不要恢复。

---

## 2. 根目录结构（现行）

```text
D:\Project\HulinBackroomsWiki\
  AGENTS.md                 本文件：现行规则
  README.md                 项目总结、现况、新开发者上手
  Expedition Archive.bat    玩家资料包导入工具入口
  tools\
    ExpeditionArchive.cs    玩家资料包导入工具源代码
    build-import.ps1        编译脚本
  客户端\
    Recorder\
      backroom-logger.ps1   旧 OCR 记录器本体（唯一来源）
      structured\           管线唯一来源（parse／event／import／merge／tests）
      logs\                 原始 logs＋manual 记录（只读；不进 git）
      characters.json       角色称号对照（新记录器用）
      s2t.json              简→繁转换表
      說明.md               旧记录器手册
    ExpeditionPlayer.cs     游戏客户端源代码（client-only 由 ExpeditionClient 启动）
    ExpeditionRecorder.cs   控制台＋OCR 浮层＋人工记录器源代码
    structured\             app 同步副本；只由 sync.mjs 更新，不要手改
    dist\Expedition\        便携版成品（sync 目标）
    dist\Expedition.zip     给别人的便携版压缩包
    start.ps1／啟動.bat     复制到 %LOCALAPPDATA% 后启动 ExpeditionRecorder
    build.ps1               重新编译 panel＋client
    volume-watch.ps1        音量记录
    fonts\                  字体与字体图集
  wiki\
    content\                ★ Wiki 唯一内容来源
    site\index.html         建置产物（双击即开；不要手改）
    _incoming\              每次收工的 staging（不进正式库）
    smoke.mjs               前端冒烟测试
    import-package.ps1      导入 .expedition.zip（默认只 staging）
    匯入紀錄.bat            拖 zip 的入口
  文档\                     AGENT-接手、要求文档、事件结构规范、背景设定、报告
  例子截图\                 参考截图（不进 wiki）
  备份\                     legacy 压缩包与站点备份
  导出\                     记录器默认导出目录（不进 git）
```

已归档／移除：

- `wiki/_data`、`wiki/_drafts`、`wiki/_backup`、`wiki/_gen`、`wiki/_i18n`、
  `wiki/build-site.ps1`、`wiki/lib-*.ps1`、`wiki/sync-floor-colors.ps1`、`wiki/ui-smoke.mjs`、
  `wiki/README.md`、`wiki/ARCHITECTURE.md`、`文档/交接.md` → `备份/legacy-wiki-20261101.zip`。
- `wiki/页面` → `备份/legacy-wiki-pages-20261101.zip`。
- `wiki/_design`、2026-10-29 的 failed-run 备份 → `备份/20261029-old-runs-and-design.zip`。
- `客户端/Recorder/player/`（旧 ExpeditionPlayer 副本与 135.8 MB 测试 profile）已移除；
  `客户端/build.ps1` 改用 `客户端/dist/Expedition` 的 WebView2 DLL。

---

## 3. 资料流

```text
ExpeditionRecorder（控制台）
  ├─ 新：人工辅助记录（现行方向）
  │   → Q 选模式／E 框选
  │   → 客户端/Recorder/logs/manual/*.jsonl
  │   → 客户端/Recorder/structured/import-manual.mjs
  │   → wiki/content/
  │
  └─ 旧：自动 OCR 管线（既有 logs／兼容）
      → ExpeditionClient（WebView 游戏）
      → 客户端/Recorder/backroom-logger.ps1
      → 客户端/Recorder/logs/backroom-<时间>/structured.jsonl
      → 客户端/Recorder/structured（font-correct → clean-levels → parse／event-parse／input-bind
                           → event-import → import-wiki → validate → merge-final）
      → wiki/_incoming/<session>/        staging（默认不写正式库）
      → wiki/content/

wiki/content/ → node wiki/content/build.mjs → wiki/site/index.html
```

- `客户端/Recorder/structured/` 是**管线唯一来源**。
- `客户端/structured/` 与 `客户端/dist/Expedition/structured/` 是同步副本；
  **不要手改副本**，改来源后跑 `node 客户端/Recorder/structured/sync.mjs`。
- 新记录器的六种模式：事件／物品／楼层介绍／出口／结局／商店；输出在 `logs/manual/`。
- 旧管线仍可处理历史 logs；新资料优先走 `import-manual.mjs`，避免两条管线同时写正式 content。
- 客户端/volume-watch.ps1 是音量记录；
  `sync.mjs` 会把它们复制到 app／dist。
- 旧调试覆盖层：记录器每帧写 `客户端/Recorder/logs/overlay.json`；`--no-overlay` 可关。
- 事件分段：`event-parse.mjs` 会用“大空隙”（> max(2.5×行距, 50px)）切断 intro／results；普通段落 35px 不会被切。没有标题的事件不会自动生成。

---

## 4. 内容来源与 schema

`wiki/content/` 是 Wiki 唯一来源。详细字段见 `wiki/content/README.md`。

```text
characters/<角色>/character.json     角色
characters/_meta.json               顺序、永久能力、状态表
characters/_skills.json             战斗技能表
characters/_extra.json              角色额外资料
entities/<实体>/entity.json         实体
items/<分类>/<物品>/item.json       物品（weapon／armor／accessory／tool／consumable／ammo）
floors/<id-name>/floor.json         楼层资料与正文
floors/<id-name>/events.json        该层事件／岔路（唯一来源）
floors/<id-name>/endings.json       该层结局（唯一来源）
floors/<id-name>/design.css         该层专属样式
floors/_names.json                  楼层 id → 名称
shops/<商店>/shop.json              商店
pages/pages.json                    关于页等静态页
recipes/recipes.json                合成表
enhancements/enhancements.json      强化表
```

硬规则：

- **事件与结局只能放该楼层文件夹**；不要再用全域 `content/events.json` 或 `content/endings/`。
- 楼层 key 一律 `id|name`；`1/7/9` 共用 id，不要只用 id 判断解锁或配色。
- `_status: 'name-only' | 'id-only'` 只给“只有名字／只有编号”的占位楼层；有正文就不要留这个字段。
- 楼层 `doc` 只放页面会显示的“环境”引言 HTML；不要混事件、商店、战斗流水。
- 不要把截图放进 `wiki/content`；截图只拿来读字，读完删。
- 玩家名与角色名按右侧队伍栏判断：普通事件本地玩家名 →“你”；角色事件玩家名 → 对应角色名（如 `狐天 → 諾亞`）；后缀是自选技能、对不上 `characters.json` 的不强行映射。
- 内容 JSON 不要写入 `D:\Project\HulinBackroomsWiki\...` 这类绝对路径；用相对路径或只留 session 名。

---

## 5. 指令

```powershell
# 建置网站（产物：wiki/site/index.html）
node D:\Project\HulinBackroomsWiki\wiki\content\build.mjs

# 管线同步／检查
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\sync.mjs
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\sync.mjs --check

# 完整验收（目前 845 项；项数会变，以输出 summary 为准）
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\tests\verify-final.mjs

# 内容引用完整性（verify-final 也会跑；可单独执行）
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\validate-content.mjs

# 前端冒烟（改 template／前端必跑）
node D:\Project\HulinBackroomsWiki\wiki\smoke.mjs

# 端到端 fixture
node D:\Project\HulinBackroomsWiki\客户端\Recorder\structured\tests\run-tests.mjs
```

- `verify-final` 的 `root` 由脚本位置推算，不再写死 `D:/Project/HulinBackroomsWiki`。
- `font.test` 与 `smoke` 使用系统暂存目录，不往仓库里写测试档。
- 建置完成后用浏览器实际开 `wiki/site/index.html` 看一次；不要只看 exit code。

---

## 6. 目前状态（2026-11-04）

> 数字会变；下面是这次整理时的实测，不是永久保证。要现在的值就重跑指令。

- 楼层：**96 层**（38 一般、41 `name-only`、17 `id-only`）。
- 有 `events.json` 的楼层：**16**；有 `endings.json`：**0**；有 doc：**37**；有 palette：**36**；有 `design.css`：**2**。
- 角色 **104**、实体 **71**、物品 **465**、商店 **17**。
- 验收：`verify-final` **845 / 845**（failed 0、warnings 5）；`smoke` **8 / 8**；`sync --check` **84 项对齐**；`build` exit 0。
- 已知内容债：
  - 465 件物品中 271 件没有 `desc`、101 件 `eff` 空、26 件没有 `eff` 字段；**多数是用户尚未截图，先不要代写**。
  - 116 处角色／实体／商店／配方引用找不到对应物品（含 OCR 杂讯与真缺条目）。
  - 54 个 JSON 仍含 `D:\Project\HulinBackroomsWiki\...` 绝对路径（多在 `_staging`／`_observations`）。
  - 17 个 `id-only` 楼层没有可恢复的名字（其中 9 个是 `▒▒▒▒▒`），不要编造。
  - `212-發光深淵` 缺完整介绍；`14-冷漠病院`、`40-有害的衰敗` 缺配色，`1-停車區` 只有底色。
- 新记录器（2026-11-03 起）：
  - `ExpeditionRecorder.cs` 约 4,921 行；六种模式（事件／物品／楼层介绍／出口／结局／商店）、圆盘、待处理浮窗、导出 ZIP、导入工具。
  - 输出在 `客户端/Recorder/logs/manual/*.jsonl`；`import-manual.mjs` 已支持 events／items／floors／exits／endings，**尚未支持 shops**。
  - 最后一份真实 manual 记录仍有 step 1 `floorId`／`title` 空白、结果断行；大量记录前先重测。
- 客户端／记录器生命周期：
  - `ExpeditionRecorder.exe` 是独立控制台；`ExpeditionClient.exe --client-only` 才是游戏 client。
  - 关控制台只退出 panel、不关 client；更新面板不需要重开游戏；只有更新 `ExpeditionClient.exe` 才要重开游戏。
  - 记录器不再用全机窗口标题扫描认游戏；只认已知 client process／同安装文件夹的 legacy client，并排除浏览器。
  - OCR 分区按 1600×900 设计坐标定义，依实际窗口比例换算后裁切；**像素不缩放**。事件／楼层／出口／结局／商店扫左侧内容区，物品扫右侧详情，队伍条单独扫。
- Git：搬迁前 108 commits、887 个 tracked files、约 8.2 MB；`.gitignore` 排除 logs、dist、fonts、OCR binary、备份、staging、`site/index.html`。
- 本机完整工作区约 1.8 GB（dist 825 MB、备份 172 MB、logs 51 MB、incoming 50 MB）。

---

## 7. 硬规则

1. **不要关用户正在用的程序或窗口。**
   - 禁止 `Stop-Process -Name`、`taskkill /IM`、`Get-Process xxx | Stop-Process`。
   - 只关自己 `Start-Process -PassThru` 记下 PID 的程序。
   - 不要对用户的游戏／浏览器 `bring_to_front`，也不要用会抢焦点的输入。
   - 验证用浏览器一律独立 profile；不确定是不是用户的程序 → 先问。

2. **不做自动玩。**
   - 不建立／恢复 `auto/`、`quark/`、`sessions/`、`Wiki探索者.exe` 等自动玩产物。

3. **正常导入流程不依赖 AI。**
   - 规则、校验、staging、确认流程必须能独立跑。
   - AI 可以协助分析，但不能是正式导入的必需步骤。

4. **不编造。**
   - 事件、路径、战斗、结局、配色、物品效果都不能猜。
   - 不确定就进 `wiki/_incoming/<session>` staging，不覆盖正式 content。
   - 低于 0.8 信心的候选不合并；没有实际截图就不生成配色。

5. **截图是暂存素材。**
   - 只拿来读文字；读完删。不要放进 `wiki/content` 或 `wiki/site`。
   - 只删自己读过的那几张；不要用万用字元扫整个文件夹。

6. **玩家名与角色名分开。**
   - 普通事件：本地玩家名（队伍栏带“（你）”）→“你”；其他玩家名按队伍栏后缀映射角色名。
   - 角色事件：玩家名 → 对应角色名（如 `狐天 → 諾亞`），不要一律换成“你”。
   - 后缀是自选特殊技能、对不上 `characters.json` 的，按自订角色处理，不强行映射。
   - 不确定这一局操作的是哪个角色 → 先问。

7. **语言。**
   - 跟用户对话：简体中文。
   - Wiki 正文：繁体中文（游戏原文照抄）。
   - 项目文件、说明文档与脚本注释：简体中文。

8. **PowerShell 5.1 陷阱。**
   - 含中文的 `.ps1` 必须是 **UTF-8 with BOM**。
   - 不要用 PowerShell 对中文档做多行字串替换；用 `read` + `edit` 或 node 脚本。
   - 不要用 `$pid`、`$home`、`$host` 等唯读自动变数。
   - 需要跨帧保存状态时用 `[pscustomobject]`，不要用 `[ordered]@{}` 查自订 key。

9. **内容与产物分开。**
   - 只改 `wiki/content/`；不要直接改 `wiki/site/index.html`。
   - 重建一律跑 `node wiki/content/build.mjs`。

10. **管线只有一份来源。**
    - 只改 `客户端/Recorder/structured/`；改完跑 `sync.mjs`。
    - 不要手改 `客户端/structured` 或 `客户端/dist/Expedition/structured`。
    - `sync.mjs --check` 有漂移就 exit 1；漂移没修好之前不要说“验收全过”。

11. **事件／结局按楼层。**
    - 只写 `floors/<楼层>/events.json`、`floors/<楼层>/endings.json`。
    - 多楼层共用的结局要拆到各层，不要用全域档。
    - 事件浏览器只读当前楼层；虚拟根节点不列入清单。

12. **楼层 key 与占位。**
    - key 一律 `id|name`；`1/7/9` 共用 id。
    - `name-only`／`id-only` 不要留 `events.json`，否则 `events:non-empty` 会挂。
    - 有正文的楼层不要留 `_status: 'name-only'`。

13. **原始 logs 只读。**
    - `客户端/Recorder/logs/backroom-*/` 的 `structured.jsonl`、`wiki.md`、`ocr-raw.log` 不要删、不要覆盖。
    - `levels/` 只允许 `clean-levels.mjs` 删假楼层档；`floor-shots/` 只允许配色脚本取样后删。
    - 要清旧场次，先确认没有要回溯的出处行号，再问用户。

14. **合并前备份。**
    - `merge-final` 默认 dry-run；`--confirm` 只在看过 `validation.json`／`merge-final-report.json` 后使用。
    - 没有备份不要 `--confirm`／`--apply`。

15. **改完要验。**
    - 内容：`build.mjs` → `smoke.mjs` → `verify-final`。
    - 管线：`sync.mjs` → `verify-final`。
    - 前端：`smoke.mjs`。
    - 没实际跑过，不要说通过。

---

## 8. 修改流程

### 8.1 改 Wiki 内容

```text
改 wiki/content/**/*.json
→ node wiki/content/build.mjs
→ node wiki/smoke.mjs
→ node 客户端/Recorder/structured/tests/verify-final.mjs
→ git diff／commit
```

### 8.2 改管线

```text
改 客户端/Recorder/structured/*.mjs
→ node 客户端/Recorder/structured/sync.mjs
→ node 客户端/Recorder/structured/sync.mjs --check
→ node 客户端/Recorder/structured/tests/verify-final.mjs
→ git diff／commit
```

### 8.3 改客户端／记录器脚本

```text
改 客户端/ExpeditionRecorder.cs（控制台／OCR 浮层）或 ExpeditionPlayer.cs（client-only 游戏）
→ 直接运行 客户端/啟動.bat（默认先跑 build.ps1 再启动）
```

- 只改 `ExpeditionRecorder.exe` 或 `backroom-logger.ps1`：关掉控制台、重新 `啟動.bat` 即可，**不用关游戏**。
- 只有改到 `ExpeditionPlayer.cs` / `ExpeditionClient.exe`（WebView 游戏本身）才需要关游戏 client。
- `start.ps1` 默认先编译；面板正在运行时提示先关闭，client 锁住的 exe／DLL 会跳过；更新面板不会强关客户端。
- 只想跳过编译时用 `啟動.bat -NoBuild`。

---

## 9. 清理、备份、版本控制

- `git` 已初始化（2026-11-01）。`.gitignore` 排除：
  `客户端/Recorder/logs/`、`客户端/Recorder/structured/bin/`、`客户端/Recorder/structured/rapid-primary-service/`、`客户端/Recorder/structured/*.otf`、
  `客户端/app/`、`客户端/dist/`、`客户端/fonts/`、`客户端/structured/`、`客户端/*.exe`、`客户端/*.dll`、`tools/*.exe`、
  `备份/`、`wiki/_incoming/`、`wiki/site/index.html`、`wiki/site/index.backup-before-content.html`。
- 内容变更用 commit 记录；不要用“复制整个文件夹”当版本控制。
- `备份/legacy-wiki-20261101.zip` 是旧 Wiki 管线与旧 handover 的封存；不要没事解开覆盖现行文件。
- `客户端/Recorder/logs/` 是原始资料，体积大；要清旧场次先问，不要用批量删除。
- `客户端/dist/Expedition/` 是 sync 目标；`Expedition.zip` 是给别人的成品。两个都保留。
- `客户端/app/` 是 build.ps1 产物；要重新生成就跑 `build.ps1`。

---

## 10. 不要做

- 不要恢复自动玩。
- 不要用程序名称批量关程序。
- 不要把截图放进 Wiki。
- 不要写玩家名；普通事件本地玩家 →“你”，角色事件 → 对应角色名（按队伍栏映射）。
- 不要直接改 `wiki/site/index.html`。
- 不要只改 `客户端/structured` 或 `dist/.../structured`（会被 sync 覆盖）。
- 不要用全域 `content/events.json` 或 `content/endings/`。
- 不要自己生成楼层配色。
- 不要在没有备份的情况下 `--confirm`／`--apply`。
- 不要在新程序里写死 `D:/Project/HulinBackroomsWiki`；用 `import.meta.url` 或参数。
- 不要把 `客户端/Recorder/logs`、`备份`、字体、OCR binary 加进 git。
