# D:\BACKROOMS\ — 後室專案唯一根目錄

> ⛔ **2026-10-18／10-21：自動玩（auto-play）已完全移除**
> 遊戲作者不允許自動玩程式。`auto\`、`quark\`、`sessions\`、`app\Wiki探索者.exe`、
> `app\後室記錄與自動玩.exe` 都已刪除；2026-10-21 連 `backroom-app-gui.ps1` 裡最後的
> 自動玩程式碼也清空了。**不要再建立或恢復任何自動玩功能。**
> 現在只有三個東西：`logger\`（記錄器）、`app\Wiki摄影机.exe`（記錄器 ＋ 快照）、`wiki\`（Wiki 專案）。

> 2026-10-17 整合完成。以前散在 `C:\Users\HX\backroom-*` 等十幾個資料夾，現在**全部集中在這裡**。

---

## 目錄結構

```
D:\BACKROOMS\
  wiki\        ← Wiki 專案（頁面\、_data\、_gen\、site\index.html）
  logger\      ← OCR 記錄器（backroom-logger.ps1、logs\、合併紀錄檔）
    player\    ← FoyerPlayer（給玩家的遊戲客戶端 ＋ logs 記錄）
  app\         ← 只有一個 app：Wiki摄影机（記錄器 ＋ 快照）
    Wiki摄影机.exe        ← 主程式（-Mode camera；只有「記錄器 / 快照」兩個分頁）
    backroom-app-gui.ps1  ← GUI 原始碼（改這個，再重新打包）
    _pkg_build.ps1        ← 重新打包（csc /target:winexe）
    _pkgbuild\Template.cs ← 解壓外殼（把兩個 .ps1 內嵌進 exe）
    _verify_gui\          ← 打包後的驗證截圖（暫存，可清）
  AGENTS.md    ← 工作規則（C:\Users\HX\AGENTS.md 的副本，那邊仍是工作區正本）
  交接.md      ← 專案現況（接手先讀）
  規例-跟這位使用者合作的方式.md ← 行為規則（接手先讀）
  README.md    ← 本檔
```

## 常用入口

| 想做什麼 | 去哪裡 |
|---|---|
| **看 Wiki 網頁** | `wiki\site\index.html`（單一檔案，雙擊即開） |
| **改 Wiki 內容** | `wiki\頁面\*.md` → 重跑 `wiki\build-site.ps1` |
| **記錄器（Wiki摄影机）** | `app\Wiki摄影机.exe`（或雙擊本目錄的 `Wiki摄影机.lnk`） |
| **單獨開記錄器（舊）** | `logger\BackroomLogger.exe`（10-14 的舊 exe，**不建議**；請用上面的 app） |
| **看記錄器目前場次** | `logger\logs\backroom-<時間戳>\` |
| **重新打包 app** | `app\_pkg_build.ps1` |

## Wiki摄影机 有哪兩個分頁

| 分頁 | 做什麼 |
|---|---|
| **記錄器** | ▶ 開始記錄／■ 停止、目前場次、事件數、幀數、已跑時間、logger.log 尾端；`-NoResize` 勾選框；下方是「遊戲視窗（記錄器專用，獨立 Edge profile）」 |
| **快照** | CDP `Page.captureScreenshot` 抓圖，**全程記憶體、不落地**；可切「適應視窗／1:1」 |

## 路徑規則（重要）

- 腳本一律用 **`$PSScriptRoot`**（或 `$MyInvocation.MyCommand.Path`）推算自己的位置，**不要再寫死絕對路徑**。
- 含中文的 `.ps1` **必須存成 UTF-8 with BOM**（`239,187,191`），否則 Windows PowerShell 5.1 會讀成亂碼。
- 快照預設埠：讀 `player.json`（FoyerPlayer 寫的 CDP 埠）；沒有就 9222。

## 不要刪

- `logger\backroom-update-*.md`、`backroom-summary-*.md`（**Wiki 出處行號的唯一依據**）
- `wiki\来源纪录\`（合併紀錄檔備份）
- `logger\logs\`（原始 OCR 紀錄，只讀不寫）
- `app\FoyerPlayer.exe`（舊拷貝，已刪）→ 現在只有 `logger\player\FoyerPlayer.exe`
  （給其他玩家用的遊戲客戶端 ＋ logs 記錄，**新產品，跟記錄器／Wiki 都無關但不要刪**）

## 相關文件

- `交接.md` — **接手先讀**：專案現況、目錄、待辦、怎麼跑、怎麼驗證。
- `規例-跟這位使用者合作的方式.md` — **接手先讀**：行為規則（改對檔案、先約定外形、回報要附證據…）。
- `AGENTS.md`（本目錄）— 工作規則。**§1 不要關使用者的程式**、**§2 不要動遊戲紀錄**、
  **§4 PowerShell 5.1 陷阱**、**§12 不要在 UI 執行緒做網路 I/O**、**§14 自動玩已移除**。
- `wiki\ARCHITECTURE.md` — Wiki 架構與資料規格。
- `wiki\README.md` — Wiki 首頁與導覽。

## 2026-10-17 搬家摘要

- 全部路徑改用 `$PSScriptRoot` 相對推算（`logger`、`app`、`wiki` 三處）
- 舊的測試輸出與重複瀏覽器 profile 已清掉，只留正式資料
