# 更新日志

## 2026-11-04 — 多人事件记录、特殊选项与自动构建

### 记录器

- `啟動.bat` 默认先编译最新源码，再启动；面板未关闭时提示先关闭，避免启动旧版本。
- `build.ps1` 改为临时目录编译，面板被占用时明确报错；游戏 client 被占用时只更新面板。
- Q／E／C 只在 `ExpeditionClient` 当前游戏窗口（含其子窗口）前台时响应；记录器面板、浏览器和其他程序不再触发。
- 新增取消键 C（可在按键设置改）：扫描/框选时取消这一步；不在扫描时再按一次取消整条记录。
- 面板「記錄」区新增 Q、E 对应按钮，与「取消這次記錄」放在同一排。
- 修复 `E` 偶发失效：面板焦点、按键钩子掉钩、卡住的按键状态、商店模式分发。
- 修复事件写入后“已记录”不显示：`Dictionary[]` 强转 `ArrayList` 导致事件合并丢失。
- 自动修复 `%LOCALAPPDATA%\Expedition\app\logs` 断掉的 junction，避免记录写不进去。
- 事件框选支持彩色标注：`樓層名`、`事件名`、`選項N`、`選項N條件`。
- 拖动框选改为只按 Y 轴判断：横向不限制，拖动时高亮所有同高度的目标行。
- 特殊选项按**金色文字或 `★`**识别，写入 `special`、`specialReason`（属性／物品／角色／技能／状态／前置事件）和 `conditionText`。
- 自由事件（`各自选一个去做`）写 `eventKind: "free"`；同一条事件内可记录多个玩家分支。
- 岔路事件（`各自选一条路`）写 `eventKind: "junction"`，事件描述与分支照常记录。
- 一次框到多个 `→ 玩家：「选择」` 时自动拆成 `branches`；也支持连续按 E 记录多个分支。
- 多人共同选择 `→ 天狐、狐天：「…」` 拆出 `players` 与 `characters`。
- 多人 QTE：总体 `qte.outcome`，个人 `慢了一步` 写入 `qte.players`，个人扣血／状态仍照常拆成效果。
- `所有人：各得到 5 硬币` 记为共享效果（`shared: true`），不拆成单人 5 硬币。
- 出口进度拆分：`+N/-N` 写入事件 `progress.delta`；`N/M` 不写事件，只把 M 写入楼层 `length`。
- 大成功／大失败不再单独记录，只按普通成功／失败记录，额外奖惩忽略。
- 饥饿／口渴归零的警示与附带扣血／理智忽略；记忆碎片 `+N` 保留。
- 战斗只记录战斗前介绍；结束窗可勾“进入战斗”并填写实体名称。
- QTE 选项在 Wiki 选项文本下显示 `⚠QTE`。
- 多人队伍栏支持 `Lv7★`、`★`、`?`、`_`、英文名；按 `Lv` 前后拆玩家名与角色称号。
- 普通事件本地玩家名替换为“你”；角色事件按队伍栏称号映射角色名（如 `狐天 → 諾亞`）；自选技能后缀不强行映射。
- 跳过 `要由谁来…`、`大家推派…` 和 `感染拖太久→恶化` 等状态变化。
- 修复 `〔智力 难度14〕` 方括号检定解析。
- 修复其他玩家被误设成本地玩家、结果被写成“你”的问题。

### Wiki 与导入

- `import-manual.mjs` 支持 `effects`、`progress`、`qte`、`combat`、`branches`、`special`、`eventKind`。
- 新增 `floor-lengths-*.jsonl`，导入时写楼层 `floor.json` 的 `length`。
- 事件按 `branches` 合并多个玩家选项/结果；同一选项不同玩家不会被误判重复。
- Wiki 模板优先显示效果文字，修复 `status undefined`／`item undefined`。
- Wiki 模板支持特殊选项金色样式、QTE 选项标记和 QTE 结果行。
- 更新 `AGENTS.md`、根 `README.md`、`wiki/content/README.md` 与 `客户端/README.md` 的多人／角色规则。

### 验证

- `build.ps1`：exit 0
- `node --check 客户端/Recorder/structured/import-manual.mjs`：exit 0
- `sync.mjs --check`：84 项对齐
- `verify-final.mjs`：845 / 845，failed 0，warnings 5
- `wiki/content/build.mjs`：exit 0
- `wiki/smoke.mjs`：8 / 8，Console 无错误
- `run-tests.mjs`：ALL TESTS PASSED

### 说明

- 旧 manual 记录不会自动重算；新规则只作用于之后的新记录。
- 队伍栏必须能在游戏画面中识别；自选技能后缀或未收录称号不会猜角色名。
- 自由事件多分支可一次框选，也可连续按 E；最后按 Q 结束整条事件。
