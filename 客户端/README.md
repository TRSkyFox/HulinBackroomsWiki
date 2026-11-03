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

`start.ps1` 会：

1. 把 `客户端` 的可执行文件、DLL、`structured` 同步副本复制到 `%LOCALAPPDATA%\Expedition\app`；
2. 从 `客户端\Recorder` 覆盖 `backroom-logger.ps1`、`palette-from-shots.ps1`；
3. 把 `app\logs` 接成 `客户端\Recorder\logs` 的 junction；
4. 启动本机那份 `ExpeditionRecorder.exe`；
5. 控制台再启动 `ExpeditionClient.exe`。

客户端已经开著时，控制台会接上现有窗口，不会再开第二个。浏览器开同一个游戏页面不会被误认。

---

## 3. 记录流程（默认 Q／E）

- **Q**：按住出现圆盘；滑向模式后放开即选。再按 Q 结束当前记录。
- **E**：框选／记录下一步。
- 圆盘六种模式：**事件／物品／楼层介绍／出口／结局／商店**。
- 按键可在面板“按键设置”改，设置写在 app 目录的 `hotkeys.json`。

事件模式：

1. 第 1 步：框事件名＋介绍＋这一屏的选项。
2. 第 2 步以后：框选择、结果、检定、战斗、跳层。
3. 没有后续就再按 Q 结束；结束窗可选楼层（事件发生地）与结果分类。

输出（JSONL，一行一笔）：

```text
客户端\Recorder\logs\manual\
  events-YYYYMMDD.jsonl
  items-YYYYMMDD.jsonl
  floors-YYYYMMDD.jsonl
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
客户端\啟動.bat -NoRecord                 # 只开游戏，不录
客户端\啟動.bat -NoOverlay                # 不显示旧 OCR 覆盖层
客户端\啟動.bat -Preset 1600x900          # 1280x720 / 1600x900 / 1920x1080 / 2400x1350
客户端\啟動.bat -Url https://...          # 换测试网址
```

---

## 5. 编译

```powershell
powershell -ExecutionPolicy Bypass -File 客户端\build.ps1
```

需要：

- `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- WebView2 DLL：`Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`、`WebView2Loader.dll`
  - 优先从 `客户端\dist\Expedition` 取；没有就用手上 `客户端` 内已有的副本。

`build.ps1` 会：

- 编译 `ExpeditionPlayer.cs` → `ExpeditionPlayer.exe`，再复制成 `ExpeditionClient.exe`；
- 编译 `ExpeditionRecorder.cs` → `ExpeditionRecorder.exe`；
- 复制 WebView2 DLL、`Recorder\backroom-logger.ps1`、`Recorder\palette-from-shots.ps1`、`Recorder\structured\*`。

客户端正在跑时，被锁住的 exe／DLL 会跳过，只更新面板；之后重开控制台即可。

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
