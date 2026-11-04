# 狐霖的后室 RPG — ExpeditionRecorder ＋ ExpeditionClient

> 2026-11-04 更新。记录器已改成**人工辅助**流程（Q／E、六种模式）。
> 项目总结、目前状态与完整导入流程看根目录 `README.md`；本文件只讲 `客户端/` 怎么跑、怎么编、文件放哪。

---

## 1. 两个 process

| 可执行文件 | 作用 |
|---|---|
| `ExpeditionRecorder.exe` | 控制台、OCR 服务、人工记录器、待处理浮窗、导出 |
| `ExpeditionClient.exe` | 只负责 WebView2 游戏窗口（`--client-only`） |

两者是独立 process：

- 关掉控制台只退出 panel，**不会关游戏**。
- 更新 `ExpeditionRecorder.exe`／记录器脚本不需要重开游戏，只要重开控制台。
- 只有更新 `ExpeditionClient.exe` 或 WebView2 DLL 才需要关游戏 client。

---

## 2. 启动

双击：

```text
客户端\啟動.bat
```

或：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 客户端\start.ps1
```

`start.ps1` 默认会：

1. 先运行 `build.ps1`，把最新源码编译成 `ExpeditionRecorder.exe`、`ExpeditionClient.exe`；
2. 把 `客户端` 的可执行文件、DLL、`structured` 同步副本复制到 `%LOCALAPPDATA%\Expedition\app`；
3. 从 `客户端\Recorder` 覆盖 `backroom-logger.ps1`、`palette-from-shots.ps1`；
4. 把 `app\logs` 接成 `客户端\Recorder\logs` 的 junction；
5. 启动本机那份 `ExpeditionRecorder.exe`；
6. 控制台再启动 `ExpeditionClient.exe`。

**运行前先关闭旧的记录器面板**；面板的 exe 被占用时无法替换，`start.ps1` 会直接提示并停止，避免启动旧版本。只想跳过编译、直接启动现有 exe 时，加 `-NoBuild`。

客户端已经开著时，控制台会接上现有窗口，不会再开第二个。浏览器开同一个游戏页面不会被误认。

---

## 3. 记录流程（默认 Q／E）

- **Q**：按住出现圆盘；滑向模式后放开即选。再按 Q 结束当前记录。
- **E**：框选／记录下一步。覆盖层会用彩色框标出识别结果：**蓝=楼层名、橙=事件名、绿=选项、黄=选项条件**。
- 拖动框选只按 Y 轴判断：只要垂直范围覆盖到文字，横向再远也会选中并高亮整行。
- Q／E 只在 `ExpeditionClient` 当前游戏窗口（含其子窗口）位于前台时响应；记录器面板、浏览器和其他程序里按键不会触发。
- 圆盘六种模式：**事件／物品／楼层介绍／出口／结局／商店**。
- 按键可在面板“按键设置”改，设置写在 app 目录的 `hotkeys.json`。

事件模式：

1. 第 1 步：框事件名＋介绍＋这一屏的选项。
2. 第 2 步以后：框选择、结果、检定、战斗、跳层。
3. 没有后续就再按 Q 结束；结束窗可选楼层（事件发生地）、归属与结果分类（普通／跳层／商店／休息／战斗）。

记录规则：

- 选项条件只记“属性＋是否有检定”，不写难度数字；例如“敏捷 难度12”只记敏捷检定。
- 选择需要检定的事件后，只记检定结果“成功／失败”，不写骰值与难度。
- 大成功／大失败按普通成功／失败记录；它们额外附带的奖励或惩罚行整行不记。
- 结果正文单独记录；体力、理智、饥饿、口渴只记增加／减少，状态效果与获得物品照常记录。
- 饥饿／口渴归零触发的警示，以及随之扣体力／理智，整行不记。
- 事件造成的“出口进度 +N／-N”会记录为事件结果。
- “出口进度 N/M”不进事件；只把 M 记到对应楼层的 `length`（楼层长度）。
- “记忆碎片 +N”会记录为效果。
- QTE：红三角提示行不记；“反应过来了”“反应过来的有 N 个人”记成功，“只有 0 个人反应过来”“慢了一步”记失败；结果正文与效果照常记录。多人 QTE 的总体结果写 `qte.outcome`，个人成功／`（慢了一步）`写 `qte.players`。QTE 选项会在 Wiki 选项文本下显示 `⚠QTE`。
- 进入战斗时，只记到战斗前的介绍；结束窗勾“进入战斗”并填实体名称，不记录战斗过程。

特殊选项与自由／岔路事件：

- 金色文字或 `★` 开头的选项记为 `special: true`；按条件来源写 `specialReason`：`attribute`／`item`／`character`／`skill`／`status`／`event`。
- `各自选一个去做` 之类的事件记为 `eventKind: "free"`；一次框到多个“→ 玩家：「选择」”会自动拆成多个分支，也可以连续按 E 记录，分支带 `player`／`players`／`character`／`characters`。
- 多人共同选择写成 `→ 天狐、狐天：「…」` 时，`players` 会拆成两个名字，`characters` 分别映射。
- `所有人：各得到 5 硬币` 记为共享效果，`text` 保留“所有人 各得到 5 硬币”，不会拆成普通的 5 硬币。
- `各自选一条路` 记为 `eventKind: "junction"`；事件描述照常记录，分支同样按玩家分步记录。

多人队伍：

- 按 E 时会扫描右侧队伍栏，读取“玩家名＋角色后缀”；名字允许带 `★`、`?`、`_`、英文和等级星号（如 `Lv7★`），先取 `Lv` 前的玩家名，再用 `Lv` 后的称号对 `characters.json`。
- 后缀能对上 `characters.json` 的角色称号时，建立“玩家名 → 角色名”映射。
- `「玩家名 自己的选择」…一按就算数` 这类事件会自动标成角色事件，并把玩家名换成真实角色名（例如 `狐天 → 諾亞`）。
- 普通事件里，本地玩家名（队伍栏带“（你）”）换成“你”；其他玩家名按映射换成角色名。
- 后缀是自选特殊技能、找不到角色称号的，按自订角色处理，不强行映射。
- `要由谁来…`、`大家推派…` 两行不记录；`感染拖太久→恶化` 这类身上状态变化也不记录。

输出（JSONL，一行一笔）：

```text
客户端\Recorder\logs\manual\
  events-YYYYMMDD.jsonl
  items-YYYYMMDD.jsonl
  floors-YYYYMMDD.jsonl
  floor-lengths-YYYYMMDD.jsonl
  exits-YYYYMMDD.jsonl
  endings-YYYYMMDD.jsonl
  shops-YYYYMMDD.jsonl
  pending-YYYYMMDD.jsonl
  draft-YYYYMMDD.jsonl
```

面板：

- **已记录**：依事件／物品／楼层／出口／结局／商店分类查看。
- **待处理**：固定右侧浮窗，可直接改。
- **导出**：生成 `記錄-YYYYMMDD-HHMM.zip`（`記錄.json`＋`記錄.md`），默认在 `导出\`。

导入本机：

```powershell
node 客户端\Recorder\structured\import-manual.mjs
# 看 dry-run 报告；确认后：
node 客户端\Recorder\structured\import-manual.mjs --confirm
```

> 目前 `import-manual.mjs` 支持 events／items／floors／exits／endings，**还不支持 shops**。

---

## 4. 生命周期

- **关闭控制台（X）**：只关 `ExpeditionRecorder.exe`；客户端继续跑。
- **取消这次记录**：清掉当前 draft，不写正式 JSONL。
- **关闭客户端**：才关游戏窗口。
- 背景 OCR 服务由 `ExpeditionRecorder` 管理，关 panel 时一起停。

参数：

```powershell
客户端\啟動.bat -NoBuild                 # 跳过编译，直接启动现有 exe
客户端\啟動.bat -NoRecord                # 只开游戏，不录
客户端\啟動.bat -NoOverlay               # 不显示旧 OCR 覆盖层
客户端\啟動.bat -Preset 1600x900         # 1280x720 / 1600x900 / 1920x1080 / 2400x1350
客户端\啟動.bat -Url https://...         # 换测试网址
```

---

## 5. 编译

正常情况下**不需要手动编译**：`啟動.bat`／`start.ps1` 默认会先跑 `build.ps1`。只有想单独验证编译结果时才手动运行：

```powershell
powershell -ExecutionPolicy Bypass -File 客户端\build.ps1
```

需要：

- `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- WebView2 DLL：`Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`、`WebView2Loader.dll`
  - 优先从 `客户端\dist\Expedition` 取；没有就用手上 `客户端` 内已有的副本。

`build.ps1` 会：

- 先编译到临时目录，再替换 `ExpeditionRecorder.exe`；面板正在运行时直接报错，要求先关闭面板；
- 编译 `ExpeditionPlayer.cs` → `ExpeditionClient.exe`；目标 exe 被占用时警告并跳过；
- 复制 WebView2 DLL、`Recorder\backroom-logger.ps1`、`Recorder\palette-from-shots.ps1`、`Recorder\structured\*`。

游戏客户端正在跑时，`start.ps1` 复制到 app 会跳过被占用的客户端 exe／DLL；面板仍可更新，关闭游戏后重新运行 `啟動.bat` 即可更新客户端。

---

## 6. 旧 OCR 管线

`Recorder\backroom-logger.ps1` 与 `Recorder\structured\run.mjs` 仍保留，用来处理既有 logs：

```powershell
node 客户端\Recorder\structured\run.mjs <sessionDir>
node 客户端\Recorder\structured\run.mjs <sessionDir> --auto
```

- 默认只生成 staging，不写正式 Wiki。
- 这条管线 OCR 错误率偏高；新资料请用第 3 节的人工记录器＋`import-manual.mjs`。
- `Recorder\structured\` 是唯一来源；`客户端\structured\` 与 `dist\Expedition\structured\` 是同步副本，不要手改。
- 同步：

```powershell
node 客户端\Recorder\structured\sync.mjs
node 客户端\Recorder\structured\sync.mjs --check
```

---

## 7. Logs

```text
客户端\Recorder\logs\backroom-YYYYMMDD-HHMMSS\   旧 OCR 场次（只读）
客户端\Recorder\logs\manual\                     新人工记录 JSONL
%LOCALAPPDATA%\Expedition\app\logs                → junction 到上面
```

旧场次文件：`wiki.md`、`wiki.jsonl`、`structured.jsonl`、`ocr-raw.log`、`levels\`、`floor-colors.log`、`logger.log`。

原始 logs 只读；要清旧场次先确认没有要回溯的出处行号。

---

## 8. 文件

| 文件 | 用途 |
|---|---|
| `ExpeditionPlayer.cs` | WebView2 游戏客户端源代码 |
| `ExpeditionRecorder.cs` | 控制台＋OCR＋人工记录器源代码（唯一来源） |
| `Recorder\backroom-logger.ps1` | 旧 OCR 记录器本体 |
| `Recorder\structured\` | 管线唯一来源 |
| `Recorder\characters.json` | 角色称号对照 |
| `Recorder\s2t.json` | 简→繁转换表 |
| `build.ps1` | 重新编译 |
| `start.ps1`／`啟動.bat` | 复制到 `%LOCALAPPDATA%` 并启动 |
| `volume-watch.ps1` | 音量记录 |
| `fonts\` | 字体（不进 git） |
| `dist\Expedition\` | 便携版成品（不进 git） |

---

## 9. 注意

- 这个 repo **不包含** OCR 服务、字体、WebView2 DLL、便携版；fresh clone 需要另外取得 assets，详见根目录 `README.md` 第 10 节。
- 不要对用户的游戏窗口抢焦点，也不要用程序名称批量关程序。
- 不要直接改 `客户端\structured\` 或 `dist\...\structured\`。
- 改完面板：

```powershell
powershell -ExecutionPolicy Bypass -File 客户端\build.ps1
node 客户端\Recorder\structured\sync.mjs --check
```
