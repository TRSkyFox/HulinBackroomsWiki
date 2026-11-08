# 给下一个 Agent 的接手说明

更新：2026-11-08 18:25  
工作区：`D:\Project\HulinBackroomsWiki`

> **2026-11-04 补充**：这是 2026-11-03 的交接快照。项目已搬到 `D:\Project\HulinBackroomsWiki`；现行统计、新记录器流程与 roadmap 请看根目录 `README.md`。本文的 824 项、13 个 events、4 个 endings、9 个 id-only 等数字已过时，不要拿来当目前状态。

> 先读 `AGENTS.md`（现行硬规则）。这份只讲“现在是什么状态、最近改了什么、下一步做什么”。
> 旧交接、旧 Wiki 管线已封存在 `备份/legacy-wiki-20261101.zip`。

---

## 0. 2026-11-08 最新交接（优先阅读）

> **2026-11-08 19:35 更新：这一轮已经做完了。** 第 0.2 节的 9 条已全部重制（见 0.5），
> 编辑器版面、滚动条、标签、多行高度、字号缩放和「已記錄」窗口都换成了统一规则。
> 下面的 0.2 保留为当时的问题清单，方便对照；**不要再按它逐条打补丁**。
> 完整前后对比、量测数据和截图在 `文档/报告/2026-11-08-记录器编辑界面UI审查.md`。

### 0.1 当前代码状态

- 最新提交：`6bf2892 fix: 稳定文本框宽度并统一右边缘`
- 前一串相关提交：
  - `24fc368` 统一文本框右侧留白
  - `896db9a` 恢复系统滚动条、滚轮翻页
  - `b14cfab` 恢复滚轮并压缩字段右边距
  - `ff3d0dc` 选项框左边线与标签分离
  - `13fbdb3` 标签右对齐、只留一个字宽
- 主要改动文件：`客户端/ExpeditionRecorder.cs`
- 运行方式：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Project\HulinBackroomsWiki\客户端\build.ps1"
cmd /c '"D:\Project\HulinBackroomsWiki\客户端\啟動.bat"'
```

- **只重启 `ExpeditionRecorder`，不要关 `ExpeditionClient`。**
- 当前 `git status` 里有 `客户端/Recorder/structured/tests/**/_test-staging` 的测试产物和 `例子截图/` 的新增图片；这些不是这轮 UI 修改的源码，**不要直接 `git add -A` 全部提交**。先分辨哪些是用户资料／测试产物。

### 0.2 当时的问题清单（2026-11-08 19:35 已重制，保留作对照）

1. **右侧边缘仍可能视觉不齐**
   - 最新一次 Win32 量测里，所有 `Edit` 子窗口的右边界都到了同一坐标；但用户随后贴的截图仍显示多条输入框右边缘参差。
   - 可能原因：量到的是控件外框而不是可见边框、不同控件的 `Anchor`／原生滚动条预留宽度不同、截图里混入了按钮或标签、或 `PanelWidth()` 在构建过程中取到了不同宽度。
   - **先复现用户截图里的那条记录和窗口宽度，再同时检查 `GetWindowRect` 与实际像素。**
2. **滚动条模型混用**
   - 为了恢复滚轮，当前隐藏了 `DarkScrollBar`，改用系统原生垂直滚动条；滚轮由 `PanelMouseWheelHook` 低级鼠标钩子驱动。
   - 现在系统滚动条会出现，外观和原先“自定义深色滚动条”不一致；`DarkScrollBar` 和 `PanelWheelRouter` 仍留在代码里，属于死代码或半死代码。
   - 要决定：**要么彻底用系统滚动条并删掉自定义类，要么改成真正的手动 viewport 滚动**。不要再出现“隐藏原生滚动条 + 调 `AutoScrollPosition`”这种无效组合。
3. **滚轮只在部分场景验证过**
   - 已验证：窗口缩小出现纵向溢出时，滚轮值能从 0 到 120、240；但还要测：
     - 鼠标停在单行输入框、多行输入框、属性按钮、选项框空白处；
     - 打开的下拉框上滚轮不应误滚页面；
     - 拖动系统滚动条；
     - 同时打开多个记录编辑器时钩子是否正确卸载、是否互相抢滚轮；
     - 关闭编辑器后低级鼠标钩子是否泄漏。
4. **选项框／标题**
   - 「选项 N」标题背景应只盖住文字；左侧竖线曾因为和标签同 x 坐标被标签背景切成一截一截，后来把外框移到 x=8。
   - 需要复查：标题宽度、左边线连续性、外框左右留白、选项文字框右边缘是否和页面其它输入框一致、结果子框是否与外框对齐。
5. **标签栏**
   - 现在标签右对齐，标签与字段之间只留一个字宽；楼层行也套用了。
   - 需要复查 1x／1.2x／1.4x／1.6x 字号、窄窗口、窗口拉伸、`AddFixed`／`AddChoice`／`AddComboField`／`AddConditionRow` 是否全部遵守。
6. **条件选项**
   - 文字里的 `★`／`☆` 不再记录，标签显示为 `⭐条件选项`（星号在前）。
   - 条件类型下拉、属性检定只在属性条件显示、属性检定位置在条件行下方。
   - 需要复查旧数据、OCR 覆盖层、保存后的 JSONL 是否都不含文字星号。
7. **多行文本框高度**
   - 使用 `GetLineFromCharIndex` 按实际换行行数算高度。
   - 需要复查：空文本、末尾换行、超长文本、窗口拉伸、字体切换后是否仍精确，不要多出一整行空白。
8. **字段右侧留白**
   - 为了统一，已取消「选项」文本框单独的 30px 右留白；一般 `AddRaw` 默认右留白为 2px。
   - 需要复查这是否符合用户要的“一致留空”，以及结果框、介绍框、选项框是否真的同一条右边缘。
9. **布局代码重复**
   - `PanelWidth()`、`FieldLabelWidth()`、`FieldGap()`、各方法里的 `rightPad`／`avail` 是这一轮临时补丁，规则分散。
   - 建议收敛成一套布局常量：页面左右 padding、标签列宽、字段右边距、滚动条宽度、窄窗口阈值。不要每个方法各算一套。

### 0.3 验收顺序（改完之后照这个走）

1. 用真实记录（包含多选项、多结果、长介绍、条件选项、属性检定）打开「已记录」。
2. 在 1x 和 1.6x 字号下各截一张全窗口图，再用 Win32 `GetWindowRect` 量所有 `Edit`、按钮、选项框的左右边界。
3. 拉伸窗口、拖分割条、切换语言、切换字号，确认重排后所有边界仍一致、滚动位置不跳。
4. 用真实鼠标滚轮（不是 `WM_VSCROLL`）在输入框、空白、按钮、下拉框上各滚一次；确认页面滚动、下拉框不被误滚、无横向滚动条。
5. 修改一条记录，等自动保存，检查 JSONL：星号、选项文字、条件、结果字段是否正确。
6. 关闭编辑器再重开，确认保存状态、滚轮、滚动位置都正常（滚轮现在是 `IMessageFilter`，关窗即摘，不会再泄漏）。
7. 修完后跑构建，按用户习惯**关记录器 → 重新编译 → 只重启记录器**，不要关游戏。

### 0.4 交接原则

- 用户对像素和留白非常敏感；不要只凭“代码看起来对”或 harness 截图说完成，要在实际运行的「已记录」窗口里看。
- 不要为了修一个控件再加一个特殊 `rightPad`；统一布局规则。
- 不要把测试 staging、日志、截图、`dist` 一起提交。
- 不清楚用户要的“留空”具体指外框、标签列还是输入框时，先看当前截图并量边界，不要猜。
- **2026-11-08 已做过一次实测审查**：`文档/报告/2026-11-08-记录器编辑界面UI审查.md`
  （8 张改前截图 + 控件包围盒数据 + `Probe.cs` 复现脚本 + 6 类测试记录）。
  第 3 节的 P0/P1 清单和量到的数字可以直接当验收标准；第 8 节是重制后的实测对比，改后截图在 `after/`。

### 0.5 2026-11-08 19:35 已完成：编辑界面重制

改动集中在 `客户端/ExpeditionRecorder.cs`（682 增 / 517 删），`build.ps1` 已通过。

- 版面收敛成一套：`MeasureMetrics()` 一次量出 `_fieldH/_comboH/_buttonH/_capH/_labelW`；
  `PageLeft/PageRight/LabelGap/RowGap/FieldX/FieldRight/FieldW/Narrow` 是唯一来源，所有 `Add*` 不再各算 `Math.Max`。
- 宽度恒定：`PanelWidth()` = 控件宽 − 滚动条宽；没有滚动条时把那 17px 画成槽。**翻记录不会重新断行**。
- 滚动条只剩一套：删掉 `DarkScrollBar` / `PanelMouseWheelHook` / `PanelWheelRouter`，
  换成 `PanelWheelFilter`（`IMessageFilter` + `WindowFromPoint`，不会跨窗口误滚，关窗即摘）。
- 编辑器顶部加身份栏（`類型 · 標題` + `編輯中…／已保存 hh:mm:ss／保存失敗`）；
  选项框只画外框、结果区改成分隔线；标签与字段同 Top 同高；多行高度按 `Font.Height` 算。
- 「已記錄」窗口：顶部工具条（搜尋框 + 類型）、自绘列表（深色选中／省略号／时间右对齐）、
  最小尺寸 640×420、编辑区不再套第二层滚动与边框。
- 修掉「一打开记录就改写 JSONL」：`UiText.ApplyTo/SetLang` 期间不触发自动保存。

**下一步必须在真实窗口确认（本次没做）：**

1. 用 **1.6 字号 + 简体**（使用者实际配置，见 `%LOCALAPPDATA%\Expedition\app\hotkeys.json`）
   开「已记录」，翻几条长短不同的记录，看右边缘是否稳定在同一列。
2. 滚轮：在输入框、空白、打开的下拉上各滚一次；再把记录窗口拖到游戏后面滚一次（不应误滚）。
3. 拖系统滚动条、按 PageDown；拉窗口看滚动位置是否保持。
4. 150% DPI、1.2／1.4 字号各看一眼（本机 96 DPI）。

**这一轮刻意没做**：列表行分隔线、空多行框 placeholder、键盘翻页、配色预览文字随行高。

**19:45 实机反馈修复（同一天第二个提交）**：

- 「滚下去 → 拉伸 → 滚回顶部，顶部空白」：`RelayoutFields` 重建时面板还在滚动，
  WinForms 把滚动量加进了新控件的 `Location`。现在重建前 `AutoScrollPosition = (0,0)`、重建后再恢复。
- 「窗口一窄，右边输入框被裁」：内层 `RecordEditForm` 停靠时没解除 `MinimumSize`（430×scale），
  宿主变窄它拒绝缩小。现在只有独立窗口设 `MinimumSize`，停靠时 `Size.Empty`；分隔条上限改成
  `MaxListWidth(w) = w − 留白 − 分隔 − EditorMinW`，清单宽度不回写，窗口放大时多出的宽度给编辑区。
- 验证：1.6 字号下 1024／1100／1546 宽窗口的编辑区分别是 692／692／1101，分隔条拖到最右也保持 692；
  滚动-拉伸-回顶后第一个控件 `Y = 22`（`Px(14)`），不再偏移。截图 `after/09`、`after/10`。

**20:57 使用者反馈（第三个提交）**：

- 「結果」字段统一改名 **選擇結果**（多段时 選擇結果 1／2…）。
- **折行被当成多笔结果**：录像按画面行写 `results`。新增 `Program.NormalizeResults()`，
  在「某行以标点开头」或「某行 ≥20 字却没有句末标点」时把整组并成一段（空字符串视为段落分隔）。
  录像（OCR 分支）与编辑器打开时都会跑；旧记录被合并后会**自动写回 JSONL**（表头显示「已保存」）。
- 选项正文下新增 **附加選項** 勾选（`sideOption: true`，取消即删键），表示「選擇不會導致通過該事件」。
  注意：`import-manual.mjs` 尚未消费 `sideOption`，等确认字段名后要接上。
- 截图 `after/11-合并结果与附加选项.png`。

**00:21 追加调整**：附加選項从「選項」下方移到「選項 N 結果」区块内，格式改为
`☐ 附加選項（選擇不會導致通過該事件）`（描述为灰色小字，点描述也能切换）；
没有结果段的选项仍留在選項框内。另外修正**選項文字颜色**：游戏给的暗色（如 `#3B3516`）
在深色输入框里看不见，`ReadableOptionColor()` 保持色相把亮度抬到可读范围。
截图 `after/12-附加选项移入结果区.png`。

**00:35 定稿**：附加選項是**结果**的属性，不是选项的——只有该选项有「結果」段时才显示，
数据写在 branch 上（`"step":2,"sideOption":true`），没有结果段的选项不再显示这一行。
导入端（`import-manual.mjs`）仍不消费 `sideOption`。

**02:40 事件类型驱动字段**：编辑器现在按 `eventKind` 换字段——普通/自由/岔路 → 樓層；
角色/合力 → 角色（合力可填多个，`、` 分隔）；通用 → 两者都不出。切换类型会重建表单，
并**同步 `scopes`**（floor/character/common）。
顺带修了 `import-manual.mjs` 的一个真 bug：它原来先按 `floorId` 找楼层、找不到就跳过，
导致角色／通用事件从来没被写过（那段写入代码是死代码）。现在先按 `scopes` 分流。
验证：两条无楼层的合成记录 dry-run `events:2 / skipped:0`（改前 0/2）；`sync --check` 84 对齐、
`verify-final` 845/845。截图 `after/14-事件类型字段-角色.png`、`...-合力.png`。

**03:06 多步事件**：编辑器把「跟第一屏选项对不上」的步骤显示成 `步驟 N` 区块
（可改 選擇、補 步驟選項、編集結果/附加選項），不再叫「其他結果」；對得上选项的仍挂在选項下。
錄影端：某一步整屏被框的列全是選項且 ≥2 条时，记成 `options` 而不是结果（共用 `BuildOptionDicts()`）。
**限制**：wiki 的 `events.json` 还没有「步骤链」结构，`import-manual` 会把 options-only 的步骤过滤掉；
要真正写进 wiki 得先定 schema（如 `branches[].next`）。截图 `after/15-多步事件步骤区块.png`。

**已提交**：`797d144 fix: 重制记录器编辑界面布局、滚动条与字号缩放`（只含 `客户端/ExpeditionRecorder.cs`、
本文、`文档/报告/2026-11-08-*`）。其余 `_test-staging`／`例子截图` 是原先就有的改动，**不要一起提交**。

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
