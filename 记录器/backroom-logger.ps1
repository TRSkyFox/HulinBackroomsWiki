<#
  狐霖的後室 RPG — 畫面文字記錄器（Wiki 版）
  ------------------------------------------------------------------
  這個遊戲是 Unity WebGL：所有文字都畫在 <canvas> 裡，DOM 和無障礙樹裡沒有半個字，
  所以只能「看畫面」：背景截圖 → 只裁遊戲畫面 → Windows 內建 OCR → 分類 → 寫成 wiki。

  輸出（每場次一個資料夾）：
    wiki.md         ← 主要成果：給 AI 直接整理成 wiki 用的結構化逐字稿
    wiki.jsonl      ← 同一份資料的事件流（有型別：旁白/決定/選項/擲骰/狀態/道具/聊天）
    ocr-raw.log     ← 未過濾的原始 OCR 全文（每一幀都留；每行前面有 [區塊名]，
                       幀標頭有色碼：配色=#2C2512。OCR 認錯字時回來查這裡）
    floor-colors.log← 各樓層的介面配色（自動偵測：底色變化超過門檻就記一筆）
    logger.log      ← 程式自己的運行記錄
    frames\         ← 只有加 -SaveFrames 才會有；預設不留截圖

  分區 OCR（預設開啟，-NoRegion 關掉）：
    整窗 OCR 會把右上按鈕列（催促／背包）、隊伍頻道聊天一起讀進來，浪費行數又容易混進
    wiki。預設改成先依 _drafts\截圖分區座標.md 的校正值分區裁切（裁切一定在放大之前），
    再分區送 OCR：title / main / opts / char。座標可用 -RegionTitle / -RegionMain /
    -RegionOpts / -RegionChar 調（格式 'x0,x1,y0,y1'，render 區像素），換樓層或換縮放
    可跑 -Calibrate 重新量。分區總行數 < -RegionMinLines（或某區失敗）→ 自動退回整張 OCR
    並記一筆警告（那時行首標記是 [full]）。

  樓層配色：每幀在 main 區取樣一小塊無文字區域算平均色，色差超過 -ColorThreshold 就寫一筆
  到 floor-colors.log（樓層名從 title 區 OCR 抓）。

  OCR 錯字修正（預設開啟，-NoOcrFix 關掉）：
    Windows OCR 對這個遊戲有固定且重複的誤讀（例：你→亻尔、前任→甫亻壬、小心！→刁吣！、
    Level→LeveI、理智．1→理智-1）。正規化（Convert-OcrText）之後再套一層規則表
    （$script:OcrFixRules，腳本上方），把「確定且不會誤傷」的誤讀修回來。
    規則表是資料驅動的（每條＝@(類型,樣式,取代值,說明)），要調直接改那張表；
    規則保守（只在有明確誤讀特徵時命中），正常正文不會被動到。

  特殊版面（背包與裝備／全螢幕結局／彈窗三欄面板）：
    分區座標只對標準遊玩畫面有效。每幀先用「標題列那一小條」做便宜預檢：
    看不到「Level N「…」」就代表不是標準畫面 → 改用整張 OCR（行首標記 [full]）。
    再依文字特徵判背包（背包与装备）／三欄面板（编号／雕刻／门后）／結局（整片近黑）。
    ocr-raw.log 的幀標頭會寫 版面=normal|backpack|ending|panel，背包的物品／屬性行
    另外標 [item]（方便事後挑出來）。

  配色自動校正分區（新增）：floor-colors-cache.json 累積「樓層 → 底色」
  （種子＝backroom-wiki\_data\_floor-colors.md 的實測表，之後由各場次 floor-colors.log 累積；
  實測值永不覆蓋）。每幀用取樣到的底色比對「最接近哪一層」→ 拿該層的預期底色當面板掃描的
  種子色；量到的邊界按「畫面尺寸＋樓層」快取起來，只有「沒快取」或「色差 > -ColorThreshold」
  才做全圖掃描（同一層一次、至少隔 4 秒），所以不會拖慢每一幀。
  量到的邊界與目前參數差 > 10px → 寫警告到 logger.log 並印出建議座標；
  加 -AutoRegion（預設關）則「這一幀」直接改用新邊界（只存在記憶體，不改參數、不改檔案）。
  配色也會寫進輸出（只新增、不動既有欄位與行格式）：樓層段落後面加 <!-- color:#RRGGBB -->、
  wiki.jsonl 的樓層事件多一個 color 欄位。

  另外（-WikiAll）：把舊場次的 transcript.jsonl 轉成同樣的 wiki 格式，
  並產生 logs\wiki-all.md（全部場次合併）與 logs\INDEX.md（場次索引）。

  停止：Ctrl+C，或由 BackroomLogger.exe 放一個 -StopFile 叫它收工。
#>
[CmdletBinding()]
param(
  # 每隔幾秒抓一張
  [double]$Interval = 2.0,
  # 輸出資料夾（預設：本程式旁邊的 logs）
  [string]$OutDir,
  # Windows OCR 語言；這台機器可用：zh-Hans-CN、en-US
  [string]$Language = 'zh-Hans-CN',
  # OCR 前把畫面放大幾倍（1 = 不放大）。實測 1.25 最佳：能修好 CJK 被拆成部首的錯
  # （例：你→亻尔），又不會讓「Level」被讀成「LeveI」。放大到 2 倍以上反而變差。
  [double]$OcrScale = 1.25,
  # 強制視窗尺寸（寬x高）。預設 1720x1392 = 3440x1440 螢幕的「右半貼靠」尺寸，
  # 而且是**固定值**（不是螢幕工作區的一半）：換螢幕或改解析度時尺寸不會跟著跑掉，
  # 分區座標的校正基準（render 1696x1300）才穩定。留空 = 不指定尺寸，由 -WinPos 決定。
  [string]$WinSize = '1720x1392',
  # 視窗位置。可用關鍵字：right（貼齊右緣、保留尺寸，預設）／topright／left／center／
  #   splitright／splitleft（Windows 真正的半螢幕貼靠，尺寸＝工作區一半，會隨螢幕變動）／
  #   或直接給座標 'x,y'；留空 = 只改尺寸、完全不動位置
  [string]$WinPos = 'right',
  # 不要動視窗大小位置（用目前視窗）
  [switch]$NoResize,
  # 直接指定視窗代碼（不給就自動找標題符合的瀏覽器視窗）
  [long]$Hwnd = 0,
  # 自動找視窗時，標題要符合的關鍵字
  [string]$TitleMatch = '後室|Backrooms|狐霖|hulin',
  # 只抓一張、印出結果就結束（測試用）
  [switch]$Once,
  # 要留截圖才加這個（預設不留：OCR 完就把圖刪掉）
  [switch]$SaveFrames,
  # 字體模板校正用：只留 OCR 文字行的暫存裁圖（處理完可刪）
  [switch]$SaveLineCrops,
  # 進入新樓層且確認樓層名時，自動留一張全幀截圖供 palette 取樣（取樣後刪）
  [switch]$AutoFloorShots,
  # 只有 -SaveFrames 時有用：最多保留幾張截圖
  [int]$KeepFrames = 200,
  # 收到這個檔案就收工（給小 EXE 的「停止」按鈕用）
  [string]$StopFile,
  # 續錄到指定場次資料夾（暫停後再按「開始記錄」用；不給就開新場次）
  [string]$SessionDir,
  # 把 logs 底下所有舊場次轉成 wiki 格式，然後結束（不錄影）
  [switch]$WikiAll,
  # 只轉某一個場次（資料夾名稱或完整路徑）
  [string]$WikiSession,
  # 匯出上次之後的新內容（增量），然後清空 logs（給 EXE 的「收工」按鈕用）
  [switch]$Export,
  # 搭配 -Export：忽略上次匯出紀錄，完整匯出全部場次
  [switch]$ExportAll,
  # 不要在主控台印每一行
  [switch]$Quiet,
  # 關掉 OCR 錯字修正規則表（Repair-OcrText）。預設開啟；規則表在腳本上方 $script:OcrFixRules
  [switch]$NoOcrFix,
  # 2026-10-31：每一行 OCR 除了文字，再記下這行的「字色」（QTE 字體顏色不同）。
  # 想省 CPU 或舊行為可以加這個開關關掉。
  [switch]$NoLineColor,
  # 不在 logs 根目錄寫 overlay.json（給客戶端畫 OCR 覆蓋層用）；預設會寫。
  [switch]$NoOverlay,

  # ---- 分區 OCR（預設開啟）----
  # 座標系 = render 區（$crop）像素，且一定在 Scale-Image 放大「之前」裁切。
  # 格式：'x0,x1,y0,y1'（x0/x1 = 左右界、y0/y1 = 上下界）
  #   x0/x1/y0/y1 給「負數」= 從右緣／下緣起算（例：x1=-5 表示畫面右緣往內 5px），
  #   用來鎖住「貼齊畫面右緣」的介面（角色卡、隊伍頻道）；時間久了畫布變寬也不會被切掉。
  #   量到的值一律夾進畫面內。
  # 校正基準 = render 1696x1300（視窗 1720x1392，Windows 右半貼靠）。
  # 若 render 區實際尺寸不同 → 依該基準等比縮放（x 用 Width/1696、y 用 Height/1300）。
  [switch]$NoRegion,
  # 標題列：Level N「名稱」＋ 第 N 層 | 難度 | 區域。
  # 這一區同時是「特殊版面預檢」的探針（看不到 Level N「…」就改用整張 OCR）
  [string]$RegionTitle = '10,1091,0,95',
  # 主文字面板：故事全文（OCR 主力）。上下界是**實測**值（1696x1300 的 frame 逐列掃描），
  # 不是從舊的 2067x1205 基準等比換算來的 —— 面板上緣會隨上方敵人列表高度變動，
  # 用換算會偏掉 100px 以上（把整段故事切掉）。
  [string]$RegionMain = '21,1030,221,947',
  # 選項面板：要走哪條路？／要怎麼做？＋ 選項（事件選項實測會落到 y≈650–700，
  # 原本從 956 render 座標開始會切掉；上移到 880 保留完整選項區）
  [string]$RegionOpts = '21,1030,880,1281',
  # 角色卡：名字／Lv／職業／HP／MP（介面，不記事件）。右界鎖畫面右緣（-5），
  # 因為右側資訊欄是「貼齊右緣」排版，絕對座標會隨畫布寬度跑掉
  [string]$RegionChar = '-548,-5,95,216',
  # 敵人欄：戰鬥時左上角（名稱／HP／防禦）。以前靠整張 OCR 才抓到，現在獨立一區。
  [string]$RegionEnemy = '10,450,60,240',
  # 分區 OCR 的總行數少於這個數字 → 自動退回「整張 OCR」
  [int]$RegionMinLines = 5,
  # 樓層配色：兩幀底色差多少才算「換樓層」（單通道最大差 ＋ RGB 距離）
  [int]$ColorThreshold = 24,
  # 樓層配色：取樣方塊大小（放在 main 區右上角往內縮 40px，避開文字）
  [int]$ColorSample = 48,
  # 小角度去斜（deskew）。某些樓層（例：Level 0.02「空虛恐懼」）會把整個遊戲容器
  # 做小角度旋轉，文字行跟著傾斜 → Windows OCR 的辨識率會掉。
  # 開啟後每一幀都會先量傾斜角（純像素運算，約 0.3~0.6 秒），
  # 角度超過 -DeskewMinDeg 才把圖轉正再 OCR；同時在 ocr-raw.log 的幀標頭記下角度。
  # 預設關閉（關閉時零額外成本、行為與舊版完全相同）。
  [switch]$Deskew,
  # 去斜的觸發門檻（度）。低於這個角度視為「本來就是正的」，不做任何重取樣（避免無謂的畫質損失）
  [double]$DeskewMinDeg = 0.3,
  # 去斜角度的搜尋上限（度）。超過這個角度就當成異常、不套用
  [double]$DeskewMaxDeg = 12.0,
  # 只跑「找面板邊界」的校正掃描，印出實際量到的座標就結束（不錄影）
  [switch]$Calibrate,
  # 搭配 -Calibrate：不用即時視窗，改讀這個圖檔來掃描（圖必須是 render 區尺寸，例如
  # C:\Users\HX\Desktop\后室RPG-参考截图\_即時frame_2067x1205.jpg）
  [string]$CalibrateImage,
  # 逐層分檔的自我測試：在 %TEMP% 跑完整的分類→分檔流程並印出結果就結束
  # （不開視窗、不讀 OCR、完全不碰 logs\ 底下的真實場次）
  [switch]$SelfTestLevels,

  # ---- 配色自動校正分區（預設關）----
  # 開啟時：本幀自動量到的面板邊界若與 -RegionMain／-RegionOpts 差超過 10px，
  # 「這一幀」就直接用新邊界（只存在記憶體，不會改寫參數、不會改檔案），
  # 讓換樓層／換縮放不會漏抓。關掉時只寫警告到 logger.log 並提議新座標。
  [switch]$AutoRegion
)

$ErrorActionPreference = 'Stop'
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'logs' }

# ============================ 原生 API ============================
if (-not ('BrWin' -as [type])) {
  Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public class BrWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

  public static string ClassName(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
  public static string Title(IntPtr h) { StringBuilder sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }

  public static double Sim(string a, string b) {
    if (a == null) a = ""; if (b == null) b = "";
    if (a == b) return 1.0;
    int n = a.Length, m = b.Length;
    if (n == 0 || m == 0) return 0.0;
    int[] prev = new int[m + 1], cur = new int[m + 1];
    for (int j = 0; j <= m; j++) prev[j] = j;
    for (int i = 1; i <= n; i++) {
      cur[0] = i;
      for (int j = 1; j <= m; j++) {
        int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
        int del = prev[j] + 1, ins = cur[j - 1] + 1, sub = prev[j - 1] + cost;
        int best = del < ins ? del : ins; if (sub < best) best = sub;
        cur[j] = best;
      }
      int[] t = prev; prev = cur; cur = t;
    }
    return 1.0 - prev[m] / (double)Math.Max(n, m);
  }

  public static double Overlap(RECT a, RECT b) {
    int x1 = Math.Max(a.Left, b.Left), y1 = Math.Max(a.Top, b.Top);
    int x2 = Math.Min(a.Right, b.Right), y2 = Math.Min(a.Bottom, b.Bottom);
    double inter = Math.Max(0, x2 - x1) * (double)Math.Max(0, y2 - y1);
    double areaA = Math.Max(1, (a.Right - a.Left) * (double)(a.Bottom - a.Top));
    double areaB = Math.Max(1, (b.Right - b.Left) * (double)(b.Bottom - b.Top));
    return inter / Math.Min(areaA, areaB);
  }
}
"@
}

# ============================ Windows OCR ============================
Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
$script:AsTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
  })[0]

function Await($op, $type) {
  $m = $script:AsTaskGeneric.MakeGenericMethod($type)
  $t = $m.Invoke($null, @($op))
  $t.Wait(-1) | Out-Null
  $t.Result
}

[Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.StorageFile, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null

Add-Type -AssemblyName System.Drawing
[void][BrWin]::SetProcessDPIAware()

# ============================ 文字正規化 ============================
$Cjk = '[\u2E80-\u9FFF\u3000-\u303F\uFF00-\uFFEF\uF900-\uFAFF]'
$RxCjkPair = [regex]("(?<=" + $Cjk + ") (?=" + $Cjk + ")")
$RxSpace = [regex]'\s+'
$RxCjkChar = [regex]'[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF]'

function Convert-OcrText([string]$s) {
  if ([string]::IsNullOrEmpty($s)) { return '' }
  $t = $RxSpace.Replace($s, ' ').Trim()
  for ($k = 0; $k -lt 6; $k++) {
    $n = $RxCjkPair.Replace($t, '')
    if ($n -eq $t) { break }
    $t = $n
  }
  return $t.Trim()
}

# ============================ OCR 錯字修正規則表 ============================
# 為什麼要這一層：Windows OCR（zh-Hans-CN）對這個遊戲的「固定幾種字」會重複誤讀，
# 而且同一種誤讀每一幀都出現（不是隨機雜訊）。正規化（Convert-OcrText）只清空白，
# 不會改字；所以在它「之後」再套一層規則表，把「確定且不會誤傷」的誤讀修回來。
# 為什麼獨立成 Repair-OcrText 而不是改 Convert-OcrText：
#   1) 出問題時可以整層關掉（-NoOcrFix），正規化行為完全不變
#   2) 規則可以隨時增減、資料驅動，不用動到主流程
# 為什麼保守：寧可少修，不要亂改正常文字。每條都寫明「為什麼安全」。
#   規則只在「有明確誤讀特徵」時才會命中，例如 亻尔／甫亻壬 這種部首拆解，
#   正常中文（他、你、前任）不會被碰到。
#
# 規則兩種寫法（每一條＝一個 @(類型, 樣式, 取代值) 或 @(類型, 樣式, 取代值, 說明)）：
#   'x'   固定字串：$t.Replace(樣式, 取代值)，全行所有出現都換
#   'r'   規則運算式：[regex]::Replace($t, 樣式, 取代值)；取代值可用 $1
#   'r1'  規則運算式，只換第一筆
# 執行順序＝陣列順序（上面的先跑）。要停用某一條：在該條前面加 # 或整條刪掉。
$script:OcrFixRules = @(
  # --- 快速聊天按鈕「小心！」被讀成 刁吣！／刁 № 心！／刂吣！ ---
  # 為什麼安全：這些字形（刁／刂／刀／习 ＋ 吣／心 ＋ №）組合起來只可能是這個按鈕，
  # 正文裡不會整行只有「刁吣」「刂№心」這種字串（有字數上限＋整行錨定）。
  @('r', '^\s*(?:刁|刂|刀|习)\s*(?:(?:№|No\.?|N[o0º°])\s*)?[吣心]\s*[！!・]{0,3}\s*$', '小心！', '快速聊天鈕被讀成 刁吣！/刁 № 心！/刂吣！'),
  # --- 角色卡「天狐 Lv1」的 Lv 被讀成 A／AI／LI（1 被讀成 I，A 被補出來）---
  # 為什麼安全：只認「天狐」後面緊接 A／AI／LI，而且必須是整行結尾；
  # 正文寫「天狐」後面接別的詞（例如「天狐說」）不會命中。
  @('r', '^(\s*天狐)\s*(?:AI|A|LI|I)\s*$', '$1 Lv1', '角色卡 Lv1 被讀成 天狐 A/天狐 LI'),
  # --- 標題列分隔符：「第 2 层《岔路」其實是「第 2 层 | 岔路」 ---
  # 為什麼安全：只作用在「第 N 层」這種標題行；《／<／〈 出現在層數後面時一定是分隔符。
  @('r', '^(第\s*[0-9]{1,4}(?:\.[0-9])?\s*[层層])\s*[《<〈【\[|｜]\s*(?=[^|｜]*$)', '$1 | ', '標題列的 | 被讀成 《／〈／【'),
  # --- 樓層編號 0/1 被讀成 O/I/l：LevelO → Level 0、LevelI → Level 1 ---
  # 為什麼安全：只認「Level/Leve 後面緊接單一 O/I/l，且後面是標題分隔符或行尾」；
  # 正文的英文單字（Level One）不會命中。
  @('r', '(?i)(?<![A-Za-z])(Level|Leve)\s*O(?=\s*[\[\(\|｜·\-「『《〈<{（]|\s*$)', '$1 0', 'LevelO → Level 0'),
  @('r', '(?i)(?<![A-Za-z])(Level|Leve)\s*[Il](?=\s*[\[\(\|｜·\-「『《〈<{（]|\s*$)', '$1 1', 'LevelI/Levell → Level 1'),
  # --- 場景標題外框：〔…〕／〖…〗 其實是【…】，含左右被讀成不同符號的混合版 ---
  # 為什麼安全：只換「整行就是 一組外框 的短語」的行（場景標題的固定樣式）；
  # 正文裡的 〔 〕 用量極少，而且要求整行只有一組外框，不會咬到句子中間。
  @('r', '^\s*[〔〖]([^〕〗]{1,30})[〕〗]\s*$', '【$1】', '場景標題 〔…〕／〖…〗／混用 → 【…】'),
  # --- 狀態列的減號被讀成 ．／·／.（理智．1 → 理智 -1）---
  # 為什麼安全：只在「已知狀態名 ＋ 分隔符 ＋ 一位數字（後面不是數字）」時才換。
  # 負向環視 (?![0-9]) 是為了不咬到小數（理智.15 是數值，不是減 1）；
  # 狀態名用白名單，正文「理智．思考」不會命中。
  @('r', '(理智|体力|體力|饥饿|飢餓|口渴|经验|經驗|经验值|生命|血量|精神|饱食|飽食)\s*[．·\.]\s*([0-9])(?![0-9])', '$1 -$2', '狀態減號被讀成 ．／·'),
  # --- 一句旁白的誤讀：徒出了这」层 → 走出了这一层 ---
  # 為什麼安全：開頭動詞白名單（走／徒）＋「出了」＋这＋層；而且只認這一個固定短語，
  # 沒有錨定行尾（OCR 常把它跟後面的「一一这段路的记忆留了下来…」黏成一行）。
  @('r', '[走徒]出了\s*[这這]\s*[」』]?\s*[层層]', '走出了这一层', '旁白「走出了这一层」被讀成 徒出了这」层'),
  # --- 部首拆解：甫亻壬 → 前任 ---
  # 一定要排在「亻尔 → 你」之前：甫＋亻＋壬 先合成「前任」，只剩單獨的亻尔才是「你」。
  @('x', '甫亻壬', '前任', '部首拆解：前任 被讀成 甫亻壬'),
  # --- 部首拆解：亻尔 → 你 ---
  # 為什麼安全：亻尔 是「你」被 OCR 拆成兩個字，正常中文不會出現這個組合。
  @('x', '亻尔', '你', '部首拆解：你 被讀成 亻尔'),
  # --- Level 的老問題：LeveI／Leve1 → Level，Leve15 → Level5 ---
  # 為什麼要兩條、而且順序不能顛倒（陣列前面的先跑）：
  #   1) leve＋1 後面緊接數字 → 那個 1 其實是 l 被讀成 1（Level5 → Leve15），只砍掉 1、保留後面數字
  #   2) 剩下的 leve＋I／i／l／1（後面不是數字）→ 那個字元就是 l，直接換成 Level
  @('r', '(?i)\bleve1(?=[0-9])', 'Level', 'Leve15 其實是 Level5（1 是 l）'),
  @('r', '(?i)\bleve[Iil1](?![0-9])', 'Level', 'Level 被讀成 LeveI／Leve1'),
  # --- 全形句點當小數點 ---
  # 為什麼安全：只在「數字 ． 數字」時換（小數點），句尾的 。 不受影響。
  @('r', '(?<=\d)\s*[．·]\s*(?=\d)', '.', '小數點被讀成全形 ．／·'),
  # --- OCR 把省略號讀成一串引號／逗號：冷靜． “ “ 、文章說 → 冷靜……文章說 ---
  # 為什麼安全：只處理「CJK 或引號後面連續 2 個以上純符號」且後面接 CJK；正常對話引號不會連成這樣。
  @('r', '(?<=[\u4e00-\u9fff：:」』】「『])(?:\s*[．.。]\s*)?(?:\s*["“”‘’]\s*[，,、]?\s*|[\s，,、．.。]+){2,}(?=[\u4e00-\u9fff])', '……', 'OCR 省略號／引號雜訊收斂為 ……'),
  # --- 字型把 Latin d 讀成「山」：Void → Voi 山 ---
  # 只在 Latin 單字後面接「山」時修；正常中文「…山」不受影響。
  @('r', '(?i)\bVoi\s*山', 'Void', 'Void 的 d 被讀成 山'),
  @('r', '(?i)\bVoid(?=\s*「)', 'Void」', 'Void 後面的 」 被吃掉'),
  # --- 引號結尾被讀成半角 ]：「会议] → 「会议」 ---
  @('r', '「([^」]{1,12})\]$', '「$1」', '引號結尾被讀成 ]')
)

# 套用上面那張表（-NoOcrFix 時整層跳過）。回傳修好的字串。
function Repair-OcrText {
  param([string]$Text)
  if ($script:NoOcrFix) { return $Text }
  if ([string]::IsNullOrEmpty($Text)) { return $Text }
  $t = $Text
  foreach ($rule in $script:OcrFixRules) {
    $kind = [string]$rule[0]
    $pat = [string]$rule[1]
    $rep = [string]$rule[2]
    if (-not $pat) { continue }
    if ($kind -eq 'x') {
      if ($t.Contains($pat)) { $t = $t.Replace($pat, $rep) }
    } elseif ($kind -eq 'r1') {
      # 只換第一筆：$Matches[1] 要先存起來，[regex]::Replace 會蓋掉 $Matches
      $m = [regex]::Match($t, $pat)
      if ($m.Success) {
        $g1 = ''
        if ($m.Groups.Count -gt 1) { $g1 = $m.Groups[1].Value }
        $new = $rep.Replace('$1', $g1)
        $t = $t.Substring(0, $m.Index) + $new + $t.Substring($m.Index + $m.Length)
      }
    } else {
      if ([regex]::IsMatch($t, $pat)) { $t = [regex]::Replace($t, $pat, $rep) }
    }
  }
  return $t
}

function Test-KeepLine([string]$t) {
  if ($t.Length -lt 2) { return $false }
  if ($RxCjkChar.Matches($t).Count -ge 2) { return $true }
  if ($t.Length -ge 3 -and $t -match '[0-9A-Za-z]') { return $true }
  return $false
}

$RxChromeNoise = [regex]'Microsoft\s*Edge|edge://|https?://|新增索引標籤|New tab'

# ==================== 哪些行才需要 RapidOCR 重認 ====================
# 2026-10-30：以前每一行都裁圖送 RapidOCR，一場 4 分鐘 1500 張 ≈ 70 秒，
# 但實測真正讀壞的不到三成。這裡先篩一遍，只留「看起來有問題」的行。
$script:CropByText = @{}          # 同一段文字只留一張裁圖
$script:KnownChars = $null        # Wiki 語料收過的字（char-freq.json）

function Initialize-KnownChars {
  if ($script:KnownChars) { return }
  $set = New-Object 'System.Collections.Generic.HashSet[char]'
  try {
    # 記錄器可能從 repo（logger\）跑，也可能從 app 鏡像（app\）跑，幾個位置都找
    $cands = @(
      (Join-Path $PSScriptRoot 'structured\char-freq.json'),
      (Join-Path (Split-Path $PSScriptRoot -Parent) 'structured\char-freq.json'),
      (Join-Path (Split-Path $PSScriptRoot -Parent) '记录器\structured\char-freq.json')
    )
    $fp = $null
    foreach ($c in $cands) { if ($c -and (Test-Path $c)) { $fp = $c; break } }
    if ($fp) {
      $j = (Get-Content -LiteralPath $fp -Raw -Encoding UTF8 | ConvertFrom-Json).chars
      foreach ($p in $j.PSObject.Properties) {
        if ($p.Name -and $p.Name.Length -eq 1) { [void]$set.Add([char]$p.Name) }
      }
    }
  } catch { }
  $script:KnownChars = $set
}

function Test-NeedsRapid([string]$Text) {
  if (-not $Text -or $Text.Length -lt 2) { return $false }
  # 殘渣字：部首、孤兒筆畫（用字面正則，避免 $script: 作用域在函式裡解析不到）
  if ($Text -match '[冂綫迄寰蟋堡刂丶丿丨乀灬]') { return $true }
  # 中英夾雜：女 r 粉筆
  if ($Text -match '[\u3400-\u9fff][A-Za-z][\u3400-\u9fff]') { return $true }
  # 含 Wiki 語料沒收過的字 → 很可能讀錯
  Initialize-KnownChars
  if ($script:KnownChars -and $script:KnownChars.Count -gt 500) {
    foreach ($ch in $Text.ToCharArray()) {
      $cp = [int]$ch
      if ($cp -lt 0x3400 -or $cp -gt 0x9FFF) { continue }
      if (-not $script:KnownChars.Contains($ch)) { return $true }
    }
  }
  return $false
}

# ============================ 事件分類 ============================
# K 的代號：
#   H  樓層 / 場景標題      N  旁白        D  決定       O  選項（可含檢定）
#   R  擲骰結果             C  檢定宣告     S  狀態變化   I  道具
#   Q  隊伍頻道             X  記錄中斷註記
$script:SkillRx = '(魅力|力量|敏捷|體質|体质|感知|智力|知|力|敏|体|感|智|魅)'

$script:DropMain = @(
  '设定','設定','离开','離開','背包 (B)','背包(B)','背包（B）','催促','继续','繼續','存档','存檔','看背景',
  '送出','思考中','掷先攻','要怎么做？','要走哪条路？','要怎么做？(按右键可以取消)'
)
$script:DropChat = @(
  '哈哈','好','不要','救命','我没事','等一下','小心！','刂吣！','跟我走！','送出','嗯','好的','……','说点什么……'
)
$RxDropLike = [regex]'按 Enter|按空白键|按空白鍵|点一下文字|點一下文字|按右键|按右鍵|可以跳过|可以跳過|看完之后按|看完之後按|说的话会出现在|新增索引標籤|说点什么|說點什麼|队伍頻道|队伍频道|隊伍頻道|跟随多数|跟随多数|强制倒数|等大家决定|等大家決定|已决定|看完了|阅读中|閱讀中|思考中|Lv\s*\d|（你）|（亻尔）|\(你\)'
$RxPrompt = [regex]'要怎么做|要怎麼做|要走哪条路|要走哪條路'
$RxLevelHead = [regex]'^\s*(Level|Leve\w*|LEVEL)\s*[\[\(\|｜·\-「『《〈<{（]{0,2}\s*[\dIlLOo]+(?:\.[\dIlLOo]{1,2})?\s*[「『]|^第\s*[\dIlLOo]+\s*[层層]|^\s*(Level|Leve\w*)\s*[\[\(\|｜·\-「『《〈<{（]{0,2}\s*[\dIlLOo]+(?:\.[\dIlLOo]{1,2})?\s*$'
$RxSceneHead = [regex]'^[〔〖\[【（(].+[〕〗\]】）)]$'
$RxRoll = [regex]'掷出|擲出'
$RxCheck = [regex]'检定|檢定'
$RxDcOnly = [regex]'难度\s*\d+\s*$'
$RxDecide = [regex]'^[→>*\s]*(你决定|你決定)'
$RxItem = [regex]'得到「|获得「|獲得「|捡起|撿起|拾取|放入背包'
$RxStat = [regex]'(体力|體力|理智|饥饿|飢餓|口渴|力量|敏捷|体质|體質|感知|智力|魅力|经验|經驗|出口进度|出口進度)\s*[+＋\-－]\s*\d|^第\s*\d+\s*回合|^防御\s*\d+|出口进度|经验值'

function New-LogEvent {
  param(
    [string]$K, [string]$V, [datetime]$T,
    $Sk = $null, $Dc = $null, $Who = $null, $Roll = $null, $Ok = $null,
    [int]$X = 0, [int]$Y = 0, [int]$H = 0, $Color = $null
  )
  # Color：樓層標題（K='H'）的介面配色 #RRGGBB；由配色快取查得（見 Get-FloorColorHex）
  return [pscustomobject]@{ K = $K; V = $V; T = $T; Sk = $Sk; Dc = $Dc; Who = $Who; Roll = $Roll; Ok = $Ok; X = $X; Y = $Y; H = $H; Color = $Color }
}

function ConvertTo-LogEvent {
  param([string]$Text, [int]$X, [int]$Y, [int]$Hh, [int]$ImgW, [datetime]$T, [string]$Region = '')

  $v = $Text.Trim()
  if ($v.Length -lt 2) { return $null }
  # 「右半邊 = 隊伍頻道」只是整張 OCR（[full]）時的近似規則；分區 OCR 的時候
  # 每一行都知道自己來自哪一區，比座標可靠：
  #   char  = 角色卡（名字／Lv／職業／HP/MP）→ 全是介面，不記事件
  #   title = 樓層標題列、opts = 選項面板 → 都不是隊伍頻道
  #   main  = 故事正文，可能延伸到畫面右側 → 仍然允許座標規則判 Q（多人時隊友發言在右側）
  # 為什麼不能直接縮小 char 區：char 收到 x<=1330 會把右半切掉，OCR 變 0 行。
  $isCharRegion = ($Region -eq 'char')
  $isMainRegion = ($Region -eq 'main' -or $Region -eq 'full' -or -not $Region)
  $inChat = ($isMainRegion) -and ($ImgW -gt 0) -and ($X -ge [int]($ImgW * 0.66))

  # --- 介面雜訊：直接不記（原始全文仍在 ocr-raw.log） ---
  if ($RxDropLike.IsMatch($v)) { return $null }
  $bare = $v.TrimEnd([char[]]'。！!？?…·.：: 　')
  if ($script:DropMain -contains $bare) { return $null }
  # 角色卡區整區都是介面（名字、職業、HP/MP、快捷鈕）：連 DropChat 都不用比，直接不記
  if ($isCharRegion) { return $null }
  if ($inChat -and ($script:DropChat -contains $bare)) { return $null }
  if ($bare.Length -lt 2) { return $null }

  # --- 樓層 / 場景 ---
  # 只有「Level N」才是樓層；「第 N 层」是場景／區域深度，不能拿來切樓層檔
  if ($RxLevelHead.IsMatch($v) -and ($v -match '^\s*(?i:level|leve[l1i]*)')) {
    # ⚠ 5.1 陷阱：空的 Generic.List 在 if 裡是 False（[bool]$emptyList = False），
    # 所以這裡一定要寫 $null -ne ... 而不是 if ($S.FrameLevelEvents)。
    if ($S -and $null -ne $S.FrameLevelEvents) {
      # 逐層分檔：樓層標題是事件流裡最權威的樓層邊界（比 title 區可靠）。
      # 先換檔，這一幀的事件之後由 Write-LevelFiles 寫進「新樓層」那一檔。
      $S.FrameHadHeader = $true
      $S.LevelFromHeader = $true
      $fromId = [string]$S.CurrentLevel
      $fromName = [string]$S.CurrentLevelName
      $r = Update-LevelFromText -S $S -Text $v -Bg $S.LastBg
      if ($r -eq 'changed') {
        $S.FrameLevelChanged = $true
        $S.LastLevelFrame = $FrameNo
        Write-TransitionEntry -S $S -FrameNo $FrameNo -Ev $null -FromLevel $fromId -FromName $fromName -ToLevel $S.CurrentLevel -ToName $S.CurrentLevelName -Reason '事件流 H（樓層標題行）'
      } elseif ($r -eq '') {
        Add-LevelUnknownNote -S $S -Text $v
      }
    }
    return (New-LogEvent 'H' $v $T -X $X -Y $Y -H $Hh)
  }
  if ($v.Length -le 26 -and $RxSceneHead.IsMatch($v)) { return (New-LogEvent 'H' $v $T -X $X -Y $Y -H $Hh) }

  # --- 檢定宣告（天狐 · 知檢定 · 難度 16） ---
  if ($RxCheck.IsMatch($v)) {
    $sk = $null; $dc = $null
    if ($v -match "($script:SkillRx)\s*[·・]?\s*(检定|檢定)") { $sk = $Matches[1] }
    if ($v -match '难度\s*(\d+)') { $dc = [int]$Matches[1] }
    return (New-LogEvent 'C' $v $T -Sk $sk -Dc $dc -X $X -Y $Y -H $Hh)
  }

  # --- 擲骰結果 ---
  if ($RxRoll.IsMatch($v) -or ($v -match '=\s*\d+' -and $v -match '成功|失败|失敗')) {
    $sk = $null; $dc = $null; $who = $null; $roll = $null; $ok = $null
    if ($v -match "($script:SkillRx)\s*难度") { $sk = $Matches[1] }
    if ($v -match '难度\s*(\d+)') { $dc = [int]$Matches[1] }
    if ($v -match '([0-9]{1,2}(?:\s*[+＋]\s*[0-9]{1,2})*)\s*=\s*([0-9]{1,2})') {
      $roll = ($Matches[1] -replace '\s', '' -replace '＋', '+') + '=' + $Matches[2]
    }
    if ($v -match '成功') { $ok = $true } elseif ($v -match '失败|失敗') { $ok = $false }
    if ($v -match "([\u4e00-\u9fff]{1,6})\s*掷出") { $who = $Matches[1] }
    return (New-LogEvent 'R' $v $T -Sk $sk -Dc $dc -Who $who -Roll $roll -Ok $ok -X $X -Y $Y -H $Hh)
  }

  # --- 只有難度的那一行（屬於上一個選項） ---
  if ($v.Length -le 24 -and $RxDcOnly.IsMatch($v)) {
    $sk = $null; $dc = $null
    if ($v -match "($script:SkillRx)\s*难度") { $sk = $Matches[1] }
    if ($v -match '难度\s*(\d+)') { $dc = [int]$Matches[1] }
    return (New-LogEvent 'DC' $v $T -Sk $sk -Dc $dc -X $X -Y $Y -H $Hh)
  }

  if ($RxDecide.IsMatch($v)) { return (New-LogEvent 'D' $v $T -X $X -Y $Y -H $Hh) }
  if ($RxItem.IsMatch($v)) { return (New-LogEvent 'I' $v $T -X $X -Y $Y -H $Hh) }
  if ($RxStat.IsMatch($v)) { return (New-LogEvent 'S' $v $T -X $X -Y $Y -H $Hh) }
  if ($inChat) { return (New-LogEvent 'Q' $v $T -X $X -Y $Y -H $Hh) }

  return (New-LogEvent 'N' $v $T -X $X -Y $Y -H $Hh)
}

# 把一行分類後併進事件串（處理：段落合併、選項綁難度、打字機覆蓋）
function Add-ClassifiedLine {
  param($S, [string]$Text, [int]$X, [int]$Y, [int]$Hh, [int]$ImgW, [datetime]$T, [bool]$SameFrame, $FrameEvents, [string]$Region = '', [int]$FrameNo = 0, [int]$W = 0)

  $ev = ConvertTo-LogEvent -Text $Text -X $X -Y $Y -Hh $Hh -ImgW $ImgW -T $T -Region $Region
  if ($null -eq $ev) { return $null }
  if ($W -gt 0) { $ev | Add-Member -NotePropertyName W -NotePropertyValue $W -Force }

  # 選項的難度 → 綁到上一個短行
  if ($ev.K -eq 'DC') {
    $target = $null
    if ($FrameEvents -and $FrameEvents.Count -gt 0) { $target = $FrameEvents[$FrameEvents.Count - 1] }
    elseif ($S.Events.Count -gt 0) {
      $cand = $S.Events[$S.Events.Count - 1]
      if (($T - $cand.T).TotalSeconds -le 6) { $target = $cand }
    }
    if ($target -and ($target.K -eq 'N' -or $target.K -eq 'O') -and $target.V.Length -le 24) {
      $target.K = 'O'
      $target.Sk = $ev.Sk
      $target.Dc = $ev.Dc
    }
    return 'M'
  }

  $list = $FrameEvents
  if (-not $SameFrame) { $list = $S.Events }
  $prev = $null
  if ($list.Count -gt 0) { $prev = $list[$list.Count - 1] }

  # 打字機效果：同一位置、字變長 → 覆蓋上一行
  if ($prev -and $prev.K -eq 'N' -and $ev.K -eq 'N' -and ($T - $prev.T).TotalSeconds -le (3 * $S.Interval + 1)) {
    $ra = New-Object 'BrWin+RECT'; $ra.Left = $prev.X; $ra.Top = $prev.Y; $ra.Right = $prev.X + 1; $ra.Bottom = $prev.Y + $prev.H
    $rb = New-Object 'BrWin+RECT'; $rb.Left = $ev.X; $rb.Top = $ev.Y; $rb.Right = $ev.X + 1; $rb.Bottom = $ev.Y + $ev.H
    if (([BrWin]::Overlap($ra, $rb) -ge 0.45) -and ($ev.V.Length -gt $prev.V.Length) -and
      (($ev.V.StartsWith($prev.V)) -or ([BrWin]::Sim($prev.V, $ev.V) -ge 0.7))) {
      $prev.V = $ev.V; $prev.X = $ev.X; $prev.Y = $ev.Y; $prev.H = $ev.H; $prev.T = $T
      return 'M'
    }
  }

  # 同一行被 OCR 拆成左右兩個 box（左：…說：／右：「我、我們是不是該）
  # 這時 X 差很多，不能再要求同 X；只要求同一 Y、右邊緊接、沒有太大空隙。
  if ($prev -and $prev.K -eq 'N' -and $ev.K -eq 'N' -and $prev.W -and $ev.W -and
    ([Math]::Abs($prev.Y - $ev.Y) -le 4)) {
    $gapX = $ev.X - ($prev.X + $prev.W)
    if ($gapX -ge -8 -and $gapX -le 36 -and
      (($prev.V -notmatch '[。！？!?」』）)]$') -or ($ev.V -match '^[「『（(。，、；：！？!?…]'))) {
      $prev.V = Repair-OcrText ($prev.V + $ev.V)
      $prev.W = ($ev.X + $ev.W) - $prev.X
      $prev.H = [Math]::Max($prev.H, $ev.H)
      return 'M'
    }
  }

  # 段落合併：同一段被 OCR 斷成兩行（同 x、緊接著、上一行沒有句尾標點）
  if ($prev -and $prev.K -eq 'N' -and $ev.K -eq 'N' -and
    ([Math]::Abs($prev.X - $ev.X) -le 14) -and
    (($ev.Y - ($prev.Y + $prev.H)) -le [Math]::Max(14, [int](0.75 * $Hh))) -and
    ($prev.V -notmatch '[。！？!?」』）)]$')) {
    $prev.V = Repair-OcrText ($prev.V + $ev.V)
    $prev.H = ($ev.Y + $ev.H) - $prev.Y
    return 'M'
  }

  $list.Add($ev)
  return $ev
}

function Add-Recent {
  param($S, [string]$Text)
  if ([string]::IsNullOrEmpty($Text) -or $Text.Length -lt 2) { return }
  $S.Recent.Add($Text)
  while ($S.Recent.Count -gt 240) { $S.Recent.RemoveAt(0) }
}

# 半截片段：短、沒有句尾標點、而且前面已經收過更完整的同一句 → 不收
function Test-Fragment {
  param($S, [string]$Text)
  if ($Text.Length -ge 16) { return $false }
  if ($Text -match '[。！？!?」』）)]$') { return $false }
  $head = $Text.Substring(0, [Math]::Min(6, $Text.Length))
  $n = 0
  for ($i = $S.Events.Count - 1; $i -ge 0 -and $n -lt 60; $i--) {
    $e = $S.Events[$i]; $n++
    if ($e.K -eq 'N' -and $e.V.Length -gt ($Text.Length + 5) -and $e.V.StartsWith($head)) { return $true }
  }
  return $false
}

function Test-Duplicate {
  param($S, [string]$Text)
  foreach ($r in $S.Recent) { if ($r -eq $Text) { return $true } }
  $pos = 0
  $from = $S.Recent.Count - 25
  foreach ($r in $S.Recent) {
    $pos++
    if ($pos -lt $from) { continue }
    if ([Math]::Abs($r.Length - $Text.Length) -gt [Math]::Max(4, [int](0.35 * $Text.Length))) { continue }
    if ([BrWin]::Sim($r, $Text) -ge 0.88) { return $true }
  }
  return $false
}

# ============================ Wiki 輸出 ============================
function Format-OptionText {
  param($E)
  $s = $E.V
  if ($E.Dc) {
    $tag = ''
    if ($E.Sk) { $tag = $E.Sk + ' ' }
    $s = $s + '（' + $tag + '難度' + $E.Dc + '）'
  }
  return $s
}

function Format-EventLine {
  param($E)
  switch ($E.K) {
    'N' { return 'N ' + $E.V }
    'D' { return 'D ' + $E.V }
    'Q' { return 'Q ' + $E.V }
    'S' { return 'S ' + $E.V }
    'I' { return 'I ' + $E.V }
    'O' { return 'O ' + (Format-OptionText $E) }
    'C' {
      $s = 'C '
      if ($E.Sk) { $s += $E.Sk + ' ' }
      if ($E.Dc) { $s += '難度' + $E.Dc + ' ' }
      return ($s + $E.V).Trim()
    }
    'R' {
      $s = 'R '
      if ($E.Sk) { $s += $E.Sk + ' ' }
      if ($E.Dc) { $s += '難度' + $E.Dc + ' ' }
      if ($E.Who) { $s += $E.Who + ' ' }
      if ($E.Roll) { $s += $E.Roll + ' ' }
      elseif ($E.V) { $s += $E.V + ' ' }
      if ($E.Ok -eq $true) { $s += '成功' } elseif ($E.Ok -eq $false) { $s += '失敗' }
      return $s.Trim()
    }
    'X' { return $E.V }
    default { return $E.K + ' ' + $E.V }
  }
}

function Write-WikiBody {
  param($Events)
  $sb = New-Object System.Text.StringBuilder
  $lastT = $null
  $opt = New-Object System.Collections.Generic.List[string]
  $chat = New-Object System.Collections.Generic.List[string]

  $flushOpt = {
    if ($opt.Count -gt 0) { [void]$sb.AppendLine('O ' + ($opt -join '｜')); $opt.Clear() }
  }
  $flushChat = {
    if ($chat.Count -gt 0) { [void]$sb.AppendLine('Q ' + ($chat -join '｜')); $chat.Clear() }
  }

  foreach ($e in $Events) {
    if ($e.K -eq 'O') { & $flushChat; $opt.Add((Format-OptionText $e)); continue }
    if ($e.K -eq 'Q') { & $flushOpt; $chat.Add($e.V); continue }
    & $flushOpt
    & $flushChat

    if ($e.K -eq 'H') {
      [void]$sb.AppendLine('')
      if ($e.V -match '^\s*(Level|Leve\w*|LEVEL)\s*[\dIlLOo]+\s*[「『]') {
        [void]$sb.AppendLine('## ' + $e.V)
        # 新增（不影響既有格式）：樓層段落後面補一行 HTML 註解色碼，給 Wiki 直接用
        $hx = Get-FloorColorHex -Text $e.V -Fallback $e.Color
        if ($hx) { [void]$sb.AppendLine('<!-- color:' + $hx + ' -->') }
      }
      else { [void]$sb.AppendLine('### ' + $e.V) }
      $lastT = $e.T
      continue
    }

    if ($null -eq $lastT -or $e.T.Hour -ne $lastT.Hour -or $e.T.Minute -ne $lastT.Minute -or ($e.T - $lastT).TotalSeconds -gt 15) {
      [void]$sb.AppendLine('@ ' + $e.T.ToString('HH:mm:ss'))
    }
    [void]$sb.AppendLine((Format-EventLine $e))
    $lastT = $e.T
  }
  & $flushOpt
  & $flushChat
  return $sb.ToString()
}

function Get-WikiHeader {
  param($S)
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('# 狐霖的後室 RPG — 冒險紀錄')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('- 場次：' + $S.SessionName + '　開始：' + $S.Started)
  if ($S.WindowTitle) { [void]$sb.AppendLine('- 遊戲視窗：' + $S.WindowTitle) }
  [void]$sb.AppendLine('- 來源：Edge 視窗截圖 → Windows OCR（' + $S.Language + '）；畫面出現過的文字才會被記到')
  [void]$sb.AppendLine('- 標記：`##` 樓層　`###` 場景　`@` 時間　N 旁白　D 決定　O 選項（含檢定難度）　R 擲骰結果　C 檢定宣告　S 狀態變化　I 道具　Q 隊伍頻道　`>` 記錄中斷')
  [void]$sb.AppendLine('- 已整理：同一段旁白合併成一行、介面按鈕等雜訊不記、時間只在跨分鐘或間隔 >15 秒時標出')
  [void]$sb.AppendLine('- 未過濾的原始 OCR 全文在 ocr-raw.log（懷疑 OCR 認錯字時去那裡比對）')
  [void]$sb.AppendLine('')
  return $sb.ToString()
}

function Write-WikiFiles {
  param($S)
  $md = (Get-WikiHeader $S) + (Write-WikiBody -Events $S.Events)
  [System.IO.File]::WriteAllText($S.Files.Wiki, $md, $S.Utf8Bom)

  $jl = New-Object System.Text.StringBuilder
  foreach ($e in $S.Events) {
    $o = [ordered]@{ t = $e.T.ToString('HH:mm:ss'); k = $e.K; v = $e.V }
    if ($e.Sk) { $o.sk = $e.Sk }
    if ($e.Dc) { $o.dc = $e.Dc }
    if ($e.Who) { $o.who = $e.Who }
    if ($e.Roll) { $o.roll = $e.Roll }
    if ($null -ne $e.Ok) { $o.ok = $e.Ok }
    # 新增欄位（既有欄位順序與名稱都沒動）：樓層事件附上配色
    if ($e.K -eq 'H') {
      $hx = Get-FloorColorHex -Text ([string]$e.V) -Fallback $e.Color
      if ($hx) { $o.color = $hx }
    }
    [void]$jl.AppendLine(($o | ConvertTo-Json -Compress))
  }
  [System.IO.File]::WriteAllText($S.Files.Jsonl, $jl.ToString(), $S.Utf8Bom)
}

# ==================== 逐層分檔（levels\） ====================
# 需求（AGENTS.md §11）：每個樓層寫自己的 log，樓層之間不互相污染。
# 這一整段【只新增額外產出】，完全不動 wiki.md / wiki.jsonl / ocr-raw.log / 匯出管線。
#
# 樓層邊界判斷（三層防護，越前面越可信）：
#   1) 事件流裡的 H 事件（樓層標題行 "Level 4「廢棄辦公室」"）→ 一定是邊界
#   2) title 區的樓層標題 OCR（Get-FloorTitleText，即使被歸成場景也讀得到編號）
#   3) 配色快取的最接近色（樓層標題 / 數字 OCR 全掛時，用底色回推是哪一層）
# 標題完全讀不出來時【不倒推樓層】，一律歸到「上一層延續」（append 到同一檔），
# 並在 logger.log / _transitions.log 留一筆「樓層標題讀不到」，不假裝知道。

$script:RxLevelName = [regex]'[「『]([^」』]{1,24})[」』]'
# 已經寫過的事件（避免同一個 H 事件在 _transitions.log 與樓層檔各寫一次）
$script:LevelWriteMap = @{}
$script:RxTitleNum = [regex]'([0-9]{1,4}(?:\.[0-9]{1,2})?)'
$script:RxUnsafeName = [regex]'[\\/:*?"<>|\x00-\x1F]'
$script:LevelKindLabel = [ordered]@{ H = '樓層/場景'; N = '旁白'; D = '決定'; O = '選項'; R = '擲骰結果'; C = '檢定宣告'; S = '狀態變化'; I = '道具'; Q = '隊伍頻道'; X = '記錄中斷' }

# 樓層編號 → 檔名前綴：4 → '04'、13 → '13'、4.5 → '04.5'
function Format-LevelNumber {
  param([string]$Num)
  $n = ([string]$Num).Trim()
  if (-not $n) { return '' }
  if ($n -match '^([0-9]{1,4})(?:\.([0-9]{1,2}))?$') {
    return (('' + [int]$Matches[1]).PadLeft(2, '0') + $(if ($Matches[2]) { '.' + $Matches[2] } else { '' }))
  }
  return ''
}

# 檔名安全化：拿掉 Windows 不合法字元、去尾端空白與句點，太長截斷
function ConvertTo-SafeFileName {
  param([string]$Name)
  if (-not $Name) { return '' }
  $t = $script:RxUnsafeName.Replace([string]$Name, '_')
  $t = $t.Trim().TrimEnd('.', ' ')
  $t = ($t -replace '\s+', ' ')
  if ($t.Length -gt 16) { $t = $t.Substring(0, 16).Trim() }
  return $t
}

# 樓層編號（'Level 4' / 'Level 4.5' / '第 4 層…'）→ '4'
function Get-LevelNumberFromId {
  param([string]$Id)
  $t = [string]$Id
  if (-not $t) { return '' }
  if ($t -match '(?i)level\s*([0-9]{1,4}(?:\.[0-9]{1,2})?)') { return [string]$Matches[1] }
  if ($t -match '第\s*([0-9]{1,4})\s*[层層]') { return [string]$Matches[1] }
  return ''
}

# 這一行的樓層編號（H 事件 / title 標題都吃；讀不到回 ''）
function Get-LevelNumberFromText {
  param([string]$Text)
  $t = [string]$Text
  if (-not $t) { return '' }
  # OCR 常把 0 讀成 O、1 讀成 I/l；RxLevelHead 已經允許這些字，數字解析也要對應。
  if ($t -match '(?i)leve[l1i]*\s*[\[\(\|｜·\-「『《〈<{（]{0,2}\s*([0-9OoIl]{1,4}(?:\.[0-9OoIl]{1,2})?)') {
    return ([string]$Matches[1] -replace '[Oo]', '0' -replace '[Il]', '1')
  }
  if ($t -match '第\s*([0-9OoIl]{1,4})\s*[层層]') {
    return ([string]$Matches[1] -replace '[Oo]', '0' -replace '[Il]', '1')
  }
  return ''
}

# 樓層編號兩兩比對：'154' vs '1'、'09' vs '9'、'4.5' vs '4.5'
# OCR 常把「Level 1」讀成「Level 154」（1 後面多黏了數字）→ 前綴相同就當同一層
function Test-LevelNumSame {
  param([string]$A, [string]$B)
  $a = ([string]$A).Trim(); $b = ([string]$B).Trim()
  if (-not $a -or -not $b) { return $false }
  $a = $a.TrimStart('0'); $b = $b.TrimStart('0')
  if (-not $a) { $a = '0' }
  if (-not $b) { $b = '0' }
  if ($a -eq $b) { return $true }
  if ($a.StartsWith($b) -or $b.StartsWith($a)) { return $true }
  return $false
}

# 把 OCR 讀到的樓層編號對回「配色快取裡已經收錄過的樓層編號」。
# 例：讀到 154 → 快取裡有 Level 1（同色域）→ 回 1；讀到 09 → 快取裡有 Level 9 → 回 9。
# 對不上就回原字串（不亂猜）。
function Resolve-FloorNumber {
  param([string]$Num, [int]$LevelIdNum = 0)
  $n = ([string]$Num).Trim()
  if (-not $n) { return '' }
  # 3 位以上通常是真實樓層（483／161／999）；不要拿 4 去前綴修正 483。
  if ($n -match '^\d{3,}') { return $n }
  if (-not $script:FloorCache) { return $n }
  $nz = $n.TrimStart('0'); if (-not $nz) { $nz = '0' }
  $best = ''; $bestCount = -1
  foreach ($e in $script:FloorCache.Floors) {
    $cid = [string]$e.id
    if ($cid -notmatch '^(?i)Level\s*([0-9]{1,4}(?:\.[0-9]{1,2})?)$') { continue }
    $cnum = [string]$Matches[1]
    if (-not (Test-LevelNumSame -A $n -B $cnum)) { continue }
    $cn = [int]($e.count)
    # 完全相等最優先；其次取在快取裡出現最多次的（代表是常走的主樓層）
    if (($cnum.TrimStart('0') -eq $nz) -and $cn -ge 1) { return $cnum }
    if ($cn -gt $bestCount) { $bestCount = $cn; $best = $cnum }
  }
  if ($best) { return $best }
  return $n
}

# 樓層名稱：先抓「…」；沒有就取「第 N 層 | 難度 | 區域」的中段
function Get-LevelNameFromText {
  param([string]$Text)
  $t = [string]$Text
  if (-not $t) { return '' }
  $m = $script:RxLevelName.Match($t)
  if ($m.Success) { return ([string]$m.Groups[1].Value).Trim() }
  if ($t -match '^第\s*[0-9]{1,4}\s*[层層]\s*[|｜]') {
    $parts = @(([string]$t -split '[|｜]') | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -ne '' })
    if ($parts.Count -ge 2) {
      $mid = [string]$parts[1]
      if ($mid -notmatch '^(難度|难度|[0-9]+|區域|区域|難|难)' -and $mid.Length -le 16) { return $mid }
    }
  }
  return ''
}

# 配色快取裡最接近的樓層名（只有「讀到的樓層編號跟那個顏色指向同一層」時才敢用）
# 為什麼要這麼嚴：底色相近的樓層不少（同色系），放任它猜會把事件歸到錯的樓層；
# 寧可回 '' → 走「上一層延續」。
function Get-NearbyFloorName {
  param($Bg, [string]$LevelIdNum = '')
  if (-not $Bg) { return '' }
  if (-not $script:FloorCache) { return '' }
  $hit = Find-NearestFloorColor -R ([int]$Bg.R) -G ([int]$Bg.G) -B ([int]$Bg.B) -MaxDist 40
  if (-not $hit) { return '' }
  $num = ([string]$LevelIdNum).Trim()
  if ($num) {
    $hitNum = Get-LevelNumberFromId -Id ([string]$hit.Id)
    if (-not $hitNum) { return '' }
    if (-not (Test-LevelNumSame -A $num -B $hitNum)) { return '' }
  }
  return [string]$hit.Name
}

# 啟動時初始化「目前樓層」＝未讀到樓層標題前的預設檔（第一層通常是大廳 00）
function Set-InitialLevelLog {
  param($S)
  $nm = ''
  if ($script:FloorCache -and $script:FloorCache.FloorsById['Level 0']) { $nm = [string]$script:FloorCache.FloorsById['Level 0'].name }
  $nm = $nm.Replace('大厅','大廳')
  $safe = ConvertTo-SafeFileName $nm
  $fn = '00' + $(if ($safe) { '-' + $safe } else { '-大廳' }) + '.log'
  $S.CurrentLevel = 'Level 0'
  $S.CurrentLevelName = $(if ($nm) { $nm } else { '大廳（未讀到樓層標題前的預設檔）' })
  $S.CurrentLevelFile = Join-Path $S.Files.LevelsDir $fn
  if ($S.LevelFileById) { $S.LevelFileById['Level 0'] = $S.CurrentLevelFile }
  $S.LevelEntries = 0
}

# 樓層換檔：新樓層（或同層再次進入）→ 建檔＋寫檔頭；舊檔回來 → 標「再次進入」
# 同一層在這一場只用【一個檔名】：第一次決定後就固定（避免後來讀到更完整的名字又多出一個檔）
function Switch-LevelLog {
  param($S, [string]$Id, [string]$Name)
  $id = ([string]$Id).Trim()
  if (-not $id) { return $false }
  $nm2 = ([string]$Name).Trim()
  $path = ''
  if ($S.LevelFileById -and $S.LevelFileById.ContainsKey($id)) { $path = [string]$S.LevelFileById[$id] }
  if (-not $path) {
    $num = Get-LevelNumberFromId -Id $id
    $safe = ConvertTo-SafeFileName $nm2
    $fn = ''
    if ($num) { $fn = (Format-LevelNumber -Num $num) }
    if (-not $fn) { $fn = (ConvertTo-SafeFileName $id) }
    if (-not $fn) { $fn = 'unknown' }
    if ($safe) { $fn = $fn + '-' + $safe }
    $fn = $fn + '.log'
    $path = Join-Path $S.Files.LevelsDir $fn
    if ($S.LevelFileById) { $S.LevelFileById[$id] = $path }
  }
  $same = (([string]$S.CurrentLevelFile) -eq $path)
  $S.CurrentLevel = $id
  $S.CurrentLevelName = $nm2
  $S.CurrentLevelFile = $path
  $S.LevelUnknown = $false
  if ($same) { return $false }
  $known = $false
  if ($null -ne $S.LevelSeen -and $S.LevelSeen.ContainsKey($path)) { $known = $true }
  if (-not $known) { $S.LevelEntries = [int]$S.LevelEntries + 1 }
  # 這一層在同一個檔裡的最新一筆寫入時間 —— 用來擋「樓層標題每幀重讀一次」造成的
  # 一堆空白的「再次進入」段落（20 秒內重複回到同一層就不另開段落）
  $lastAt = [datetime]::MinValue
  if ($null -ne $S.LevelSeenAt -and $S.LevelSeenAt.ContainsKey($path)) { $lastAt = [datetime]$S.LevelSeenAt[$path] }
  $nowTs = Get-Date
  if ($known -and (($nowTs - $lastAt).TotalSeconds -lt 20)) {
    # 剛寫過同一層 → 不重複寫段落，只把「目前樓層」換回來
    if ($null -ne $S.LevelSeenAt) { $S.LevelSeenAt[$path] = $nowTs }
    return $true
  }
  if (-not (Test-Path -LiteralPath $S.Files.LevelsDir)) { [void](New-Item -ItemType Directory -Force -Path $S.Files.LevelsDir) }
  $sb = New-Object System.Text.StringBuilder
  if (-not $known) {
    [void]$sb.AppendLine('# 樓層紀錄：' + $id + $(if ($nm2) { '「' + $nm2 + '」' } else { '' }))
    [void]$sb.AppendLine('# 場次：' + $S.SessionName + '')
    [void]$sb.AppendLine('# 建立：' + $nowTs.ToString('yyyy-MM-dd HH:mm:ss') + '（第 ' + $S.LevelEntries + ' 個樓層檔）')
    [void]$sb.AppendLine('# 說明：本檔只放這一層的事件；跨樓層的轉場／岔路／樓層結算在 ..\_transitions.log')
    [void]$sb.AppendLine('')
  } else {
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('===== 再次進入 ' + $nowTs.ToString('yyyy-MM-dd HH:mm:ss') + ' =====')
    [void]$sb.AppendLine('')
  }
  [void]$sb.AppendLine('## ' + $id + $(if ($nm2) { '「' + $nm2 + '」' } else { '' }))
  [System.IO.File]::AppendAllText($path, $sb.ToString(), $S.Utf8Bom)
  # 用索引子而不是 .Add()：同一個檔名重複進來時 .Add() 會拋「Item has already been added」
  if ($null -ne $S.LevelSeen) { $S.LevelSeen[$path] = $true }
  if ($null -ne $S.LevelSeenAt) { $S.LevelSeenAt[$path] = $nowTs }
  return $true
}

# 由 OCR 文字（H 事件或 title 標題）＋底色決定要不要換檔。
# 回傳：'changed' 換了檔、'same' 同一層、'unknown' 讀不到編號也問不出顏色（＝上一層延續）、'' 沒線索
function Update-LevelFromText {
  param($S, [string]$Text, $Bg = $null)
  $t = ([string]$Text).Trim()
  if (-not $t) { return '' }
  $id = ''
  $nm = ''
  if ($t -match '(?i)leve[l1i]*' -or $t -match '第\s*[0-9]{1,4}\s*[层層]') {
    $num = Get-LevelNumberFromText -Text $t
    if ($num) { $id = 'Level ' + $num; $nm = Get-LevelNameFromText -Text $t }
  } elseif ($t -match '(?i)(the\s*hub|樞紐|枢纽)') {
    $id = 'The Hub'; $nm = Get-LevelNameFromText -Text $t
  }
  if (-not $id) { return '' }
  # 名稱與編號的穩定化（OCR 會把「Level 1」讀成「Level 154」、把標題讀成場景名）：
  #   1) 配色快取已經有這一層的名字 → 以快取為準（同時把誤讀的編號對回正規編號）
  #   2) 這一節早就記過的名字（同場同層以第一次讀到的為準）
  #   3) 這次標題裡的「名稱」  4) 最接近色的名字
  $cacheName = ''
  if ($script:FloorCache) {
    $ce = $script:FloorCache.FloorsById[$id]
    if ($ce) { $cacheName = [string]$ce.name }
  }
  if (-not $cacheName -and $script:FloorCache) {
    # 編號被 OCR 讀歪時，先問「配色快取裡有沒有前綴相同的樓層」（154 → 1）
    $numTxt = Get-LevelNumberFromId -Id $id
    if ($numTxt) {
      $fixNum = Resolve-FloorNumber -Num $numTxt
      if ($fixNum -and ($fixNum -ne $numTxt)) {
        $fixId = 'Level ' + $fixNum
        $fe = $script:FloorCache.FloorsById[$fixId]
        if ($fe) {
          # 只有「名稱也對得上」才接受前綴修正；483 是真樓層，不能因為 4 是前綴就變成 Level 4。
          $feName = [string]$fe.name
          $nameOk = (-not $nm) -or (-not $feName) -or ($nm -like ('*' + $feName + '*')) -or ($feName -like ('*' + $nm + '*'))
          if ($nameOk) { $id = $fixId; $cacheName = $feName }
        }
      }
    }
  }
  $known = ''
  if ($S.LevelNameById -and $S.LevelNameById.ContainsKey($id)) { $known = [string]$S.LevelNameById[$id] }
  if ($cacheName) { $nm = $cacheName }
  elseif ($known -and $known -notmatch '^大廳（未讀到') { $nm = $known }
  elseif (-not $nm) { $nm = $known }
  elseif ($known -and $nm.Length -lt $known.Length) {
    # 新的名字比較短（例如只讀到場景名）→ 沿用已經讀到的完整樓層名
    $nm = $known
  }
  $nm = $nm.Replace('大厅','大廳').Replace('废弃办公室','廢棄辦公室')
  if (-not $nm) { $nm = Get-NearbyFloorName -Bg $Bg -LevelIdNum (Get-LevelNumberFromId -Id $id) }
  if ($S.LevelNameById) { $S.LevelNameById[$id] = $nm }
  if (Switch-LevelLog -S $S -Id $id -Name $nm) { return 'changed' }
  return 'same'
}

# 樓層標題讀不到時：留在上一層的檔案（append），只記一筆「讀不到」不亂猜樓層
function Add-LevelUnknownNote {
  param($S, [string]$Text)
  $k = ([string]$Text).Trim()
  if (-not $k) { return }
  if ($S.LevelUnknownKey -eq $k) { return }
  $S.LevelUnknownKey = $k
  $S.LevelUnknown = $true
  Write-Log $S ('樓層標題讀不出編號，事件歸到「上一層延續」（' + $S.CurrentLevelFile + '）：' + $k)
  $ln = (Get-Date).ToString('HH:mm:ss') + '  [記錄器] 樓層標題讀不出編號 → 之後的事件先歸「上一層延續」（' + $S.CurrentLevel + '）：' + $k
  [System.IO.File]::AppendAllText($S.Files.Transitions, $ln + [Environment]::NewLine, $S.Utf8Bom)
}

# 逐層記錄用的一行（帶時間；不影響 wiki.md 的格式）
function Format-LevelLine {
  param($E)
  $body = ''
  if ($E.K -eq 'H') { $body = '## ' + [string]$E.V }
  else { $body = Format-EventLine -E $E }
  $k = [string]$E.K
  $lbl = ''
  if ($script:LevelKindLabel.Contains($k)) { $lbl = [string]$script:LevelKindLabel[$k] }
  if (-not $lbl) { $lbl = $k }
  return ('[' + $E.T.ToString('HH:mm:ss') + '] ' + $lbl + '　' + $body)
}

function Format-TransitionHeader {
  param([int]$FrameNo, [string]$FromLevel, [string]$FromName, [string]$ToLevel, [string]$ToName, [string]$Reason)
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('===== 樓層交界 ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '（幀 #' + $FrameNo + '）=====')
  [void]$sb.AppendLine('  從：' + [string]$FromLevel + $(if ($FromName) { '「' + $FromName + '」' } else { '' }))
  [void]$sb.AppendLine('  到：' + $ToLevel + $(if ($ToName) { '「' + $ToName + '」' } else { '' }))
  if ($Reason) { [void]$sb.AppendLine('  依據：' + $Reason) }
  return $sb.ToString()
}

# 把一個樓層邊界寫進 _transitions.log（轉場／岔路／樓層結算都集中在這裡）
# $FromLevel／$FromName 一定要在換檔【之前】就先抓好，否則「從」會跟「到」一樣
function Write-TransitionEntry {
  param($S, [int]$FrameNo, $Ev, [string]$FromLevel, [string]$FromName, [string]$ToLevel, [string]$ToName, [string]$Reason)
  if (-not $S.Files.Transitions) { return }
  # 擋抖動：樓層標題被 OCR 每一幀重讀時，會在一兩幀內反覆「換到同一層」，
  # 那種不是真的轉場。同一層只在前後相隔 3 幀以上時才算一次交界。
  # （不用秒數當條件：真的走回上一層常常只花幾秒，不該被吃掉）
  $toKey = [string]$ToLevel
  if ($toKey -and $null -ne $S.TransitionFrame) {
    $prevFr = -1
    if ($S.TransitionFrame.ContainsKey($toKey)) { $prevFr = [int]$S.TransitionFrame[$toKey] }
    if ($prevFr -ge 0 -and ($FrameNo - $prevFr) -le 2) { return }
    $S.TransitionFrame[$toKey] = $FrameNo
  }
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.Append((Format-TransitionHeader -FrameNo $FrameNo -FromLevel $FromLevel -FromName $FromName -ToLevel $ToLevel -ToName $ToName -Reason $Reason))
  if ($Ev) {
    [void]$sb.AppendLine((Format-LevelLine -E $Ev))
    if ($script:LevelWriteMap) { $script:LevelWriteMap[$Ev] = $true }
  }
  [System.IO.File]::AppendAllText($S.Files.Transitions, $sb.ToString(), $S.Utf8Bom)
}

# 樓層交界那一幀的非標題事件 → 前後兩層都有份，寫進 _transitions.log，不進單層檔（避免污染）
function Write-TransitionEvents {
  param($S, [int]$FrameNo, $Events)
  if (-not $S.Files.Transitions) { return }
  $arr = [object[]]$Events
  if ($arr.Count -eq 0) { return }
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('  交界幀同幀事件（前後兩層都有份，因此不寫進任一單層檔）：')
  foreach ($e in $arr) { [void]$sb.AppendLine('  ' + (Format-LevelLine -E $e)) }
  [System.IO.File]::AppendAllText($S.Files.Transitions, $sb.ToString(), $S.Utf8Bom)
}

# 逐層寫檔：掛在 Write-WikiFiles 後面，把這一幀剛歸類好的事件寫進「目前樓層」的檔案
# 邊界那一幀的處理原則（避免整幀被吃掉、也避免歸錯層）：
#   * H 樓層標題行本身 → 只進 _transitions.log（它是邊界，不屬於任何單層）
#   * 同幀的其他事件 → 全部歸「新樓層」那一檔（新樓層標題出現後，畫面正文就是新樓層的）
function Write-LevelFiles {
  param($S, [int]$FrameNo)
  if (-not $S) { return }
  if (-not $S.Files -or -not $S.Files.LevelsDir) { return }
  if (-not $S.CurrentLevelFile) { Set-InitialLevelLog -S $S }
  if (-not (Test-Path -LiteralPath $S.Files.LevelsDir)) { [void](New-Item -ItemType Directory -Force -Path $S.Files.LevelsDir) }
  $arr = [object[]]$S.FrameLevelEvents
  if ($arr.Count -eq 0) { if ($S.FrameLevelEvents) { $S.FrameLevelEvents.Clear() }; return }
  $sb = New-Object System.Text.StringBuilder
  $n = 0
  $skipped = 0
  foreach ($e in $arr) {
    # 已經在交界標頭寫過的事件（例如 H 樓層標題行）不重複寫
    if ($script:LevelWriteMap -and $script:LevelWriteMap.ContainsKey($e)) { $skipped++; continue }
    if ($script:LevelWriteMap) { $script:LevelWriteMap[$e] = $true }
    [void]$sb.AppendLine((Format-LevelLine -E $e))
    $n = $n + 1
  }
  if ($n -gt 0) { [System.IO.File]::AppendAllText($S.CurrentLevelFile, $sb.ToString(), $S.Utf8Bom) }
  $S.LevelEventCount = [int]$S.LevelEventCount + $n
  $S.FrameLevelEvents.Clear()
  $S.FrameLevelChanged = $false
}

# ============================ 視窗 / 畫面 ============================
function Find-GameWindow {
  param([long]$Hwnd, [string]$TitleMatch)
  if ($Hwnd -ne 0 -and [BrWin]::IsWindow([IntPtr]$Hwnd)) {
    return [pscustomobject]@{ Hwnd = [IntPtr]$Hwnd; Pid = 0; Title = [BrWin]::Title([IntPtr]$Hwnd) }
  }
  $browsers = @('msedge', 'chrome', 'brave', 'msedgewebview2')
  $procs = @()
  foreach ($b in $browsers) { $procs += @(Get-Process -Name $b -ErrorAction SilentlyContinue) }
  $withWin = $procs | Where-Object { $_.MainWindowHandle -ne 0 }
  $hit = $withWin | Where-Object { $_.MainWindowTitle -match $TitleMatch } | Select-Object -First 1
  if (-not $hit) { $hit = $withWin | Select-Object -First 1 }
  if (-not $hit) { return $null }
  return [pscustomobject]@{ Hwnd = [IntPtr]$hit.MainWindowHandle; Pid = $hit.Id; Title = $hit.MainWindowTitle }
}

function Find-RenderWidget {
  # WebView2 的 Chrome_RenderWidgetHostHWND 不是 Top 的直接子視窗：
  # Top → WindowsForms10 → Chrome_WidgetWin_0 → Chrome_WidgetWin_1 → RenderWidget。
  # 只掃直接子／兄弟會找不到，Get-PageRect 就退回整個視窗（標題列也被 OCR）。
  param([IntPtr]$Parent)
  $child = [BrWin]::GetWindow($Parent, 5)
  while ($child -ne [IntPtr]::Zero) {
    if ([BrWin]::ClassName($child) -eq 'Chrome_RenderWidgetHostHWND' -and [BrWin]::IsWindowVisible($child)) {
      return $child
    }
    $found = Find-RenderWidget -Parent $child
    if ($found -ne [IntPtr]::Zero) { return $found }
    $child = [BrWin]::GetWindow($child, 2)
  }
  return [IntPtr]::Zero
}

function Get-PageRect {
  # 回傳相對於視窗左上角的「遊戲畫面」範圍；找不到 render widget 就用整個視窗
  # 注意：PowerShell 變數不分大小寫 —— 視窗寬高一定要叫 $winW/$winH
  param([IntPtr]$Top)
  $wr = New-Object 'BrWin+RECT'
  [void][BrWin]::GetWindowRect($Top, [ref]$wr)
  $winW = $wr.Right - $wr.Left; $winH = $wr.Bottom - $wr.Top
  $child = Find-RenderWidget -Parent $Top
  if ($child -ne [IntPtr]::Zero) {
    $rr = New-Object 'BrWin+RECT'
    [void][BrWin]::GetWindowRect($child, [ref]$rr)
    $px = $rr.Left - $wr.Left; $py = $rr.Top - $wr.Top
    $pw = $rr.Right - $rr.Left; $ph = $rr.Bottom - $rr.Top
    if ($px -ge 0 -and $py -ge 0 -and $pw -gt 100 -and $ph -gt 100 -and ($px + $pw) -le $winW -and ($py + $ph) -le $winH) {
      return [pscustomobject]@{ X = $px; Y = $py; W = $pw; H = $ph; WndW = $winW; WndH = $winH; Page = $true }
    }
  }
  return [pscustomobject]@{ X = 0; Y = 0; W = $winW; H = $winH; WndW = $winW; WndH = $winH; Page = $false }
}

# 把視窗調整成固定尺寸（不搶焦點）。回傳實際結果。
# 取得主要螢幕的工作區（扣掉工作列）。回傳 @{X;Y;W;H}；失敗回 $null。
function Get-WorkArea {
  try {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    return [pscustomobject]@{ X = $wa.X; Y = $wa.Y; W = $wa.Width; H = $wa.Height }
  } catch { return $null }
}

# 把「關鍵字位置」換算成實際座標。
#   right  = 貼齊右緣（保留尺寸）        left  = 貼齊左緣（保留尺寸）
#   topright = 右上角（保留尺寸）        center = 水平置中（保留尺寸）
#   splitright = Windows 右半貼靠（尺寸 = 工作區一半）
#   splitleft  = Windows 左半貼靠（尺寸 = 工作區一半）
function Resolve-SnapPosition {
  param([string]$Pos, [int]$W, [int]$H)
  $out = [pscustomobject]@{ X = $null; Y = $null; W = $W; H = $H; Mode = '' }
  if (-not $Pos) { return $out }
  $k = $Pos.Trim().ToLower()
  $wa = Get-WorkArea
  if (-not $wa) { return $out }
  switch ($k) {
    'right'      { $out.X = $wa.X + $wa.W - $W; $out.Y = $wa.Y; $out.Mode = 'right' }
    'left'       { $out.X = $wa.X; $out.Y = $wa.Y; $out.Mode = 'left' }
    'topright'   { $out.X = $wa.X + $wa.W - $W; $out.Y = $wa.Y; $out.Mode = 'topright' }
    'center'     { $out.X = $wa.X + [int](($wa.W - $W) / 2); $out.Y = $wa.Y; $out.Mode = 'center' }
    'splitright' { $out.W = [int]($wa.W / 2); $out.H = $wa.H; $out.X = $wa.X + $wa.W - $out.W; $out.Y = $wa.Y; $out.Mode = 'splitright' }
    'splitleft'  { $out.W = [int]($wa.W / 2); $out.H = $wa.H; $out.X = $wa.X; $out.Y = $wa.Y; $out.Mode = 'splitleft' }
  }
  if ($out.X -lt $wa.X) { $out.X = $wa.X }
  if ($out.X + $out.W -gt $wa.X + $wa.W) { $out.X = $wa.X + $wa.W - $out.W }
  if ($out.Y -lt $wa.Y) { $out.Y = $wa.Y }
  return $out
}

function Force-WindowSize {
  param([IntPtr]$Top, [string]$Size, [string]$Pos)
  $k = if ($Pos) { $Pos.Trim().ToLower() } else { '' }
  $isSplit = ($k -eq 'splitright' -or $k -eq 'splitleft')
  if (-not $Size -and -not $isSplit) { return $null }
  if ($Size -and $Size -notmatch '^\s*(\d+)\s*[xX]\s*(\d+)\s*$') { return $null }
  if ($Size) { $w = [int]$Matches[1]; $h = [int]$Matches[2] } else { $w = 0; $h = 0 }
  $r0 = New-Object 'BrWin+RECT'
  [void][BrWin]::GetWindowRect($Top, [ref]$r0)
  $x = $r0.Left; $y = $r0.Top
  $snap = Resolve-SnapPosition -Pos $Pos -W $w -H $h
  if ($snap.W -ne $w -or $snap.H -ne $h) { $w = $snap.W; $h = $snap.H }
  if ($null -ne $snap.X) { $x = $snap.X; $y = $snap.Y }
  elseif ($Pos -and $Pos -match '^\s*(-?\d+)\s*,\s*(-?\d+)\s*$') { $x = [int]$Matches[1]; $y = [int]$Matches[2] }
  # 不搶焦點、不改 Z 序
  $flags = 0x0004 -bor 0x0010
  $ok = [BrWin]::SetWindowPos($Top, [IntPtr]::Zero, $x, $y, $w, $h, $flags)
  Start-Sleep -Milliseconds 700
  $r1 = New-Object 'BrWin+RECT'
  [void][BrWin]::GetWindowRect($Top, [ref]$r1)
  return [pscustomobject]@{
    Ok = $ok; X = $r1.Left; Y = $r1.Top
    W = ($r1.Right - $r1.Left); H = ($r1.Bottom - $r1.Top)
    WantW = $w; WantH = $h
    Exact = (($r1.Right - $r1.Left) -eq $w -and ($r1.Bottom - $r1.Top) -eq $h)
  }
}

function Get-WindowImage {
  param([IntPtr]$Top)
  $wr = New-Object 'BrWin+RECT'
  [void][BrWin]::GetWindowRect($Top, [ref]$wr)
  $iw = $wr.Right - $wr.Left; $ih = $wr.Bottom - $wr.Top
  if ($iw -le 0 -or $ih -le 0) { return $null }
  $bmp = New-Object System.Drawing.Bitmap($iw, $ih)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  $ok = $false
  try { $ok = [BrWin]::PrintWindow($Top, $hdc, 2) } finally { $g.ReleaseHdc($hdc); $g.Dispose() }
  if (-not $ok) { $bmp.Dispose(); return $null }
  return $bmp
}

# 背景 RapidOCR 已經認好的同一張裁圖 → 直接採用它的文字。
# 這樣 overlay／wiki 不用等收工，下一幀就會從 Windows OCR 的錯字換成 RapidOCR 的結果。
$script:RapidFixMap = @{}
$script:RapidFixStamp = [datetime]::MinValue
$script:RapidFixLen = -1
$script:RapidFixPath = ''
function Get-RapidFix {
  param([string]$LineDir, [string]$Name)
  if (-not $LineDir -or -not $Name) { return $null }
  $p = Join-Path (Split-Path $LineDir -Parent) 'rapid.jsonl'
  if (-not (Test-Path -LiteralPath $p)) { return $null }
  try {
    $fi = Get-Item -LiteralPath $p
    if ($p -ne $script:RapidFixPath -or $fi.LastWriteTimeUtc -ne $script:RapidFixStamp -or $fi.Length -ne $script:RapidFixLen) {
      $map = @{}
      foreach ($ln in [System.IO.File]::ReadLines($p)) {
        if (-not $ln.Trim()) { continue }
        try {
          $o = $ln | ConvertFrom-Json
          if ($o.name -and $o.text) { $map[[string]$o.name] = [pscustomobject]@{ Text = [string]$o.text; Score = [double]$o.score } }
        } catch { }
      }
      $script:RapidFixMap = $map
      $script:RapidFixPath = $p
      $script:RapidFixStamp = $fi.LastWriteTimeUtc
      $script:RapidFixLen = $fi.Length
    }
    $hit = $script:RapidFixMap[$Name]
    if ($hit -and $hit.Score -ge 0.75 -and $hit.Text) { return $hit.Text }
  } catch { }
  return $null
}

# 放大點陣圖（OCR 前用）。HighQualityBicubic 對小字效果最好。
function Scale-Image {  param($Bmp, [double]$Factor)
  if (-not $Bmp) { return $Bmp }
  if ($Factor -le 1.0001) { return $Bmp }
  $nw = [int][Math]::Round($Bmp.Width * $Factor)
  $nh = [int][Math]::Round($Bmp.Height * $Factor)
  if ($nw -lt 1 -or $nh -lt 1) { return $Bmp }
  # Windows OCR 對超大圖不友善，長邊上限 8000
  $maxEdge = [Math]::Max($nw, $nh)
  if ($maxEdge -gt 8000) {
    $k = 8000.0 / $maxEdge
    $nw = [int][Math]::Round($nw * $k); $nh = [int][Math]::Round($nh * $k)
  }
  $out = New-Object System.Drawing.Bitmap($nw, $nh)
  $out.SetResolution(96, 96)
  $g = [System.Drawing.Graphics]::FromImage($out)
  try {
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($Bmp, 0, 0, $nw, $nh)
  } finally { $g.Dispose() }
  $Bmp.Dispose()
  return $out
}

# ============================ 行字色取樣（QTE 判定用） ============================
# 使用者指出「QTE 出現的時候字體顏色是特殊的」。純文字比對（閃！／慢了一步）
# 只能猜；顏色是畫面的事實，所以每一行除了文字，再記下「這行字是什麼顏色」。
# 做法：在 OCR 框裡取樣幾個點，中位數＝背景，離中位數最遠的前 20%＝筆畫顏色。
# 成本：每行約 300 次 GetPixel（步進 3px、4 條掃描線）。
function Get-InkColor {
  param($Bmp, [int]$X1, [int]$Y1, [int]$X2, [int]$Y2)
  if (-not $Bmp) { return $null }
  $x1 = [Math]::Max(0, $X1); $y1 = [Math]::Max(0, $Y1)
  $x2 = [Math]::Min($Bmp.Width - 1, $X2); $y2 = [Math]::Min($Bmp.Height - 1, $Y2)
  if ($x2 -le $x1 -or $y2 -le $y1) { return $null }
  $lums = New-Object 'System.Collections.Generic.List[double]'
  $cols = New-Object 'System.Collections.Generic.List[object]'
  $stepY = [Math]::Max(1, [int](($y2 - $y1) / 3))
  for ($y = $y1; $y -le $y2; $y += $stepY) {
    for ($x = $x1; $x -le $x2; $x += 3) {
      $p = $Bmp.GetPixel($x, $y)
      $lums.Add(0.299 * $p.R + 0.587 * $p.G + 0.114 * $p.B)
      $cols.Add($p)
    }
  }
  if ($lums.Count -lt 6) { return $null }
  $sorted = @($lums | Sort-Object)
  $med = $sorted[[int]($sorted.Count / 2)]
  $pairs = New-Object 'System.Collections.Generic.List[object]'
  for ($i = 0; $i -lt $lums.Count; $i++) {
    $pairs.Add([pscustomobject]@{ d = [Math]::Abs($lums[$i] - $med); R = $cols[$i].R; G = $cols[$i].G; B = $cols[$i].B })
  }
  $top = @($pairs | Sort-Object d -Descending | Select-Object -First ([Math]::Max(3, [int]($pairs.Count * 0.2))))
  $r = 0; $g = 0; $b = 0
  foreach ($t in $top) { $r += $t.R; $g += $t.G; $b += $t.B }
  $k = [Math]::Max(1, $top.Count)
  return ('#{0:X2}{1:X2}{2:X2}' -f [int]($r / $k), [int]($g / $k), [int]($b / $k))
}

function Invoke-FrameOcr {
  param([string]$PngPath, $Engine, [double]$Scale = 1.0)  $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($PngPath)) ([Windows.Storage.StorageFile])
  $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
  $bitmap = $null
  try {
    $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $res = Await ($Engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    # 取色用的 Bitmap（與 OCR 同一張圖；失敗就跳過，不影響錄製）
    $inkBmp = $null
    if (-not $NoLineColor) {
      try { $inkBmp = [System.Drawing.Bitmap]::new($file.Path) } catch { $inkBmp = $null }
    }
    $lines = New-Object System.Collections.Generic.List[object]
    foreach ($line in $res.Lines) {
      $x1 = [double]::MaxValue; $y1 = [double]::MaxValue; $x2 = [double]::MinValue; $y2 = [double]::MinValue
      foreach ($wd in $line.Words) {
        $br = $wd.BoundingRect
        if ($br.X -lt $x1) { $x1 = $br.X }
        if ($br.Y -lt $y1) { $y1 = $br.Y }
        if (($br.X + $br.Width) -gt $x2) { $x2 = $br.X + $br.Width }
        if (($br.Y + $br.Height) -gt $y2) { $y2 = $br.Y + $br.Height }
      }
      if ($Scale -gt 1.0001) { $x1 /= $Scale; $y1 /= $Scale; $x2 /= $Scale; $y2 /= $Scale }
      $txt = Convert-OcrText $line.Text
      $txt = Repair-OcrText $txt            # 錯字修正規則表（-NoOcrFix 可整層關掉）
      if (-not (Test-KeepLine $txt)) { continue }
      # 字色：在「縮放前」的影像座標取樣（$x1..$y2 這時還是原圖座標）
      $ink = $null
      if ($inkBmp) {
        try { $ink = Get-InkColor -Bmp $inkBmp -X1 ([int]$x1 * $Scale) -Y1 ([int]$y1 * $Scale) -X2 ([int]$x2 * $Scale) -Y2 ([int]$y2 * $Scale) } catch { $ink = $null }
      }
      $lines.Add([pscustomobject]@{
          Text = $txt; X = [int]$x1; Y = [int]$y1
          W = [int]($x2 - $x1); H = [int]($y2 - $y1)
          Ink = $ink
        })
    }
    return $lines
  } finally {
    if ($bitmap) { $bitmap.Dispose() }
    if ($inkBmp) { $inkBmp.Dispose() }
    if ($stream) { $stream.Dispose() }
  }
}

# ============================ 小角度去斜（deskew，-Deskew 開啟才用） ============================
# 為什麼要這一層：某些樓層（例：Level 0.02「空虛恐懼」）會把整個遊戲容器做小角度旋轉，
# 文字行跟著傾斜，Windows OCR 對傾斜文字的辨識率會掉。做法是「先量角度 → 把整張圖轉正
# → 再用原本的分區座標 OCR」（分區座標也一起換算）。
#
# 偵測原理（row-profile cross-correlation）：把面板切左右兩半，各算逐列灰度平均（列投影）；
# 文字行若傾斜，右半投影就是左半投影上下平移 shift → 互相關找 shift → 角度 = atan(shift/半寬)。
#
# ⚠ 兩個已知限制（實測 2026-10-16）：
#   1. 搜尋窗必須 < 半個行距（行距約 40px），否則會鎖到隔壁行。所以只適合 |角度| ≲ 3°。
#   2. 這遊戲是 CJK 短行，相關性只有 0.6~0.9（長行文件可到 0.99）。
#      短行造成「不同降採樣尺度量到的角度會不一致」→ 用雙尺度一致性當守衛，
#      不一致就判定「不可信」→ 不套用旋轉（寧可漏判，也不要亂轉正常畫面）。
function Get-RowProfiles {
  param($Bmp, [int]$X0, [int]$Y0, [int]$W, [int]$H, [int]$Dw)
  if ($W -lt 40 -or $H -lt 40) { return $null }
  if ($Dw -lt 40) { $Dw = 40 }
  if ($Dw -gt $W) { $Dw = $W }
  $dh = $H
  $b = New-Object System.Drawing.Bitmap($Dw, $dh)
  try {
    $g = [System.Drawing.Graphics]::FromImage($b)
    try {
      $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
      $g.DrawImage($Bmp, (New-Object System.Drawing.Rectangle(0, 0, $Dw, $dh)), (New-Object System.Drawing.Rectangle($X0, $Y0, $W, $H)), [System.Drawing.GraphicsUnit]::Pixel)
    } finally { $g.Dispose() }
    $bd = $b.LockBits((New-Object System.Drawing.Rectangle(0, 0, $Dw, $dh)), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
      $st = $bd.Stride
      $by = New-Object byte[] ($st * $dh)
      [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $by, 0, $by.Length)
    } finally { $b.UnlockBits($bd) }
    $mid = [int]($Dw / 2)
    $L = New-Object 'double[]' $dh
    $R = New-Object 'double[]' $dh
    for ($y = 0; $y -lt $dh; $y++) {
      $ro = $y * $st
      $a = 0.0; $c = 0.0
      for ($x = 0; $x -lt $mid; $x++) { $i = $ro + $x * 4; $a += 0.299 * $by[$i + 2] + 0.587 * $by[$i + 1] + 0.114 * $by[$i] }
      for ($x = $mid; $x -lt $Dw; $x++) { $i = $ro + $x * 4; $c += 0.299 * $by[$i + 2] + 0.587 * $by[$i + 1] + 0.114 * $by[$i] }
      $L[$y] = $a / $mid
      $R[$y] = $c / ($Dw - $mid)
    }
    $mL = 0.0; $mR = 0.0
    for ($y = 0; $y -lt $dh; $y++) { $mL += $L[$y]; $mR += $R[$y] }
    $mL /= $dh; $mR /= $dh
    $nl = 0.0; $nr = 0.0
    for ($y = 0; $y -lt $dh; $y++) { $L[$y] -= $mL; $R[$y] -= $mR; $nl += $L[$y] * $L[$y]; $nr += $R[$y] * $R[$y] }
    return [pscustomobject]@{ L = $L; R = $R; H = $dh; NL = $nl; NR = $nr; Mid = $mid; Dw = $Dw }
  } catch {
    return $null
  } finally {
    if ($b) { $b.Dispose() }
  }
}

function Get-ProfileShift {
  param($Pair, [int]$MaxShift)
  if (-not $Pair) { return $null }
  $L = $Pair.L; $R = $Pair.R; $dh = $Pair.H
  $den = [Math]::Sqrt($Pair.NL * $Pair.NR)
  if ($den -le 1e-9) { return $null }
  $span = ($Pair.Dw - 1) / 2.0
  $bestS = 0; $bestC = -2.0; $zeroC = 0.0
  $curve = New-Object 'double[]' (2 * $MaxShift + 1)
  for ($s = -$MaxShift; $s -le $MaxShift; $s++) {
    $dot = 0.0
    for ($y = 0; $y -lt $dh; $y++) {
      $yy = $y + $s
      if ($yy -lt 0 -or $yy -ge $dh) { continue }
      $dot += $L[$y] * $R[$yy]
    }
    $cc = $dot / $den
    $curve[$s + $MaxShift] = $cc
    if ($s -eq 0) { $zeroC = $cc }
    if ($cc -gt $bestC) { $bestC = $cc; $bestS = $s }
  }
  $sub = [double]$bestS
  $si = $bestS + $MaxShift
  if ($si -gt 0 -and $si -lt (2 * $MaxShift)) {
    $c0 = $curve[$si - 1]; $c1 = $curve[$si]; $c2 = $curve[$si + 1]
    $dd = $c0 - 2 * $c1 + $c2
    if ([Math]::Abs($dd) -gt 1e-12) { $sub = $bestS + 0.5 * ($c0 - $c2) / $dd }
  }
  $ang = [Math]::Atan($sub / $span) * 180.0 / [Math]::PI
  return [pscustomobject]@{ Shift = $sub; Angle = $ang; Corr = $bestC; ZeroCorr = $zeroC; Gain = ($bestC - $zeroC); Span = $span }
}

function Measure-ImageSkew {
  # Angle = 要把圖轉正的角度（Graphics.RotateTransform 直接吃這個值）
  param($Bmp, [int]$X0, [int]$Y0, [int]$W, [int]$H,
    [int]$Dw = 600, [double]$MinCorr = 0.55, [double]$MinGain = 0.02,
    [double]$FlatDeg = 0.3, [double]$MaxDeg = 3.5, [double]$TolTwoScale = 0.6)
  $res = [pscustomobject]@{ Angle = 0.0; Apply = $false; Corr = 0.0; Gain = 0.0; Shift = 0.0; Angle2 = 0.0; Reason = '' }
  try {
    $ms = [int][Math]::Floor($Dw * 0.05)
    if ($ms -gt 20) { $ms = 20 }
    if ($ms -lt 4) { $ms = 4 }
    $p1 = Get-RowProfiles -Bmp $Bmp -X0 $X0 -Y0 $Y0 -W $W -H $H -Dw $Dw
    $s1 = Get-ProfileShift -Pair $p1 -MaxShift $ms
    if (-not $s1) { $res.Reason = 'profile-failed'; return $res }
    $res.Corr = $s1.Corr; $res.Gain = $s1.Gain; $res.Shift = $s1.Shift; $res.Angle = $s1.Angle
    $dw2 = [int]($Dw * 0.5)
    if ($dw2 -ge 60) {
      $p2 = Get-RowProfiles -Bmp $Bmp -X0 $X0 -Y0 $Y0 -W $W -H $H -Dw $dw2
      $ms2 = [int][Math]::Floor($dw2 * 0.05)
      if ($ms2 -lt 4) { $ms2 = 4 }
      $s2 = Get-ProfileShift -Pair $p2 -MaxShift $ms2
      if ($s2) { $res.Angle2 = $s2.Angle }
    }
    if ([Math]::Abs($s1.Angle) -le $FlatDeg) { $res.Angle = 0.0; $res.Reason = 'flat'; return $res }
    if ($s1.Corr -lt $MinCorr) { $res.Angle = 0.0; $res.Reason = ('low-corr(' + [Math]::Round($s1.Corr, 3) + ')'); return $res }
    if ($s1.Gain -lt $MinGain) { $res.Angle = 0.0; $res.Reason = ('low-gain(' + [Math]::Round($s1.Gain, 3) + ')'); return $res }
    if ([Math]::Abs($s1.Angle) -gt $MaxDeg) { $res.Angle = 0.0; $res.Reason = ('over-range(' + [Math]::Round($s1.Angle, 2) + ')'); return $res }
    if ([Math]::Abs($res.Angle2) -gt 0.01 -and [Math]::Abs($res.Angle2 - $s1.Angle) -gt $TolTwoScale) {
      $res.Angle = 0.0
      $res.Reason = ('unstable-across-scales(' + [Math]::Round($s1.Angle, 2) + '/' + [Math]::Round($res.Angle2, 2) + ')')
      return $res
    }
    $res.Apply = $true; $res.Reason = 'ok'
    return $res
  } catch {
    $res.Angle = 0.0; $res.Apply = $false; $res.Reason = ('error:' + $_.Exception.Message)
    return $res
  }
}

function Rotate-ImageDeskew {
  # 依角度把整張圖轉正；畫布尺寸不變（座標原點不變），內容繞中心轉。
  # ⚠ 因此分區座標要一起換算（ConvertTo-DeskewedRect + Get-DeskewOffset）。
  param($Bmp, [double]$Deg)
  if (-not $Bmp) { return $null }
  $w = $Bmp.Width; $h = $Bmp.Height
  $out = New-Object System.Drawing.Bitmap($w, $h, $Bmp.PixelFormat)
  $out.SetResolution(96, 96)
  $g = [System.Drawing.Graphics]::FromImage($out)
  try {
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Black)
    $g.TranslateTransform($w / 2.0, $h / 2.0)
    $g.RotateTransform([single]$Deg)
    $g.TranslateTransform(-$w / 2.0, -$h / 2.0)
    $g.DrawImage($Bmp, 0, 0, $w, $h)
  } finally { $g.Dispose() }
  return $out
}

function Get-DeskewOffset {
  param([int]$W, [int]$H, [double]$Deg, [int]$Dx = 0, [int]$Dy = 0)
  $rad = $Deg * [Math]::PI / 180.0
  $cs = [Math]::Cos($rad); $sn = [Math]::Sin($rad)
  $offX = [double]::MaxValue; $offY = [double]::MaxValue
  foreach ($p in @(@(0, 0), @($W, 0), @(0, $H), @($W, $H))) {
    $px = $p[0] + $Dx - $W / 2.0
    $py = $p[1] + $Dy - $H / 2.0
    $rx = $cs * $px + $sn * $py + $W / 2.0
    $ry = -$sn * $px + $cs * $py + $H / 2.0
    if ($rx -lt $offX) { $offX = $rx }
    if ($ry -lt $offY) { $offY = $ry }
  }
  return [pscustomobject]@{ X = [int][Math]::Round([Math]::Max(0.0, $offX)); Y = [int][Math]::Round([Math]::Max(0.0, $offY)) }
}

function ConvertTo-DeskewedRect {
  # 把「原圖座標」的 Rectangle 換算成「轉正後圖」的座標（旋轉後的軸對齊外框）
  param($Rect, [int]$W, [int]$H, [double]$Deg, $Offset)
  if (-not $Rect) { return $null }
  $rad = $Deg * [Math]::PI / 180.0
  $cs = [Math]::Cos($rad); $sn = [Math]::Sin($rad)
  $x1 = [double]::MaxValue; $y1 = [double]::MaxValue
  $x2 = [double]::MinValue; $y2 = [double]::MinValue
  foreach ($p in @(@($Rect.Left, $Rect.Top), @($Rect.Right, $Rect.Top), @($Rect.Left, $Rect.Bottom), @($Rect.Right, $Rect.Bottom))) {
    $px = $p[0] - $W / 2.0; $py = $p[1] - $H / 2.0
    $rx = $cs * $px + $sn * $py + $W / 2.0 - $Offset.X
    $ry = -$sn * $px + $cs * $py + $H / 2.0 - $Offset.Y
    if ($rx -lt $x1) { $x1 = $rx }
    if ($ry -lt $y1) { $y1 = $ry }
    if ($rx -gt $x2) { $x2 = $rx }
    if ($ry -gt $y2) { $y2 = $ry }
  }
  $l = [int][Math]::Round($x1); $t = [int][Math]::Round($y1)
  $r = [int][Math]::Round($x2); $b = [int][Math]::Round($y2)
  if ($l -lt 0) { $l = 0 }
  if ($t -lt 0) { $t = 0 }
  if ($r -gt $W) { $r = $W }
  if ($b -gt $H) { $b = $H }
  if (($r - $l) -lt 4 -or ($b - $t) -lt 4) { return $null }
  return (New-Object System.Drawing.Rectangle($l, $t, ($r - $l), ($b - $t)))
}

function Save-Jpeg {
  param($Bmp, [string]$Path)
  $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
  $ps = New-Object System.Drawing.Imaging.EncoderParameters 1
  $ps.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), ([long]82)
  $Bmp.Save($Path, $codec, $ps)
  $ps.Dispose()
}

# ============================ 版面偵測（特殊版面） ============================
# 為什麼要這一層：分區座標只對「標準遊玩畫面」有效。這三種版面會整個蓋掉標準版面，
# 硬套分區會把大段文字切掉（例如背包的屬性表、結局的置中大字、三欄表格的欄位）。
# 所以先判斷「這是哪種版面」，特殊版面就改用整張 OCR（[full]），
# 並在 ocr-raw.log 的幀標頭寫明 版面=…（事後追查才看得出哪一幀是哪種版面）。
# 判斷原則：用「文字特徵」為主、顏色為輔。
#   背包：出現「背包与装备」（掃描全行文字，不受分區限制）或 背包＋装备 相關詞
#   結局：整片近黑底色（#101010 這一類）＋ 畫面置中，或出現「跳过」
#   彈窗：三欄表格（编号／雕刻／门后 這類欄位名，或 專案｜目標 這種標題）
#   其他：normal（原本的分區 OCR 照舊）
function Test-NearBlackScreen {
  param($Bmp)
  if (-not $Bmp) { return $false }
  # 螢幕中央一大塊取樣（大步距，很便宜）：幾乎全黑才算
  $x0 = [int]($Bmp.Width * 0.20); $x1 = [int]($Bmp.Width * 0.80)
  $y0 = [int]($Bmp.Height * 0.25); $y1 = [int]($Bmp.Height * 0.85)
  $n = 0; $dark = 0; $step = 12
  for ($y = $y0; $y -le $y1; $y += $step) {
    for ($x = $x0; $x -le $x1; $x += $step) {
      $c = $Bmp.GetPixel($x, $y)
      $lum = (0.299 * $c.R) + (0.587 * $c.G) + (0.114 * $c.B)
      if ($lum -lt 40) { $dark++ }
      $n++
    }
  }
  if ($n -lt 50) { return $false }
  return (($dark / [double]$n) -ge 0.90)
}

# 把 OCR 行接成一個大字串（判斷關鍵字用；不受分區影響）
function Join-LineText {
  param($Lines)
  $arr = @()
  if ($null -ne $Lines) { $arr = [object[]]$Lines }
  $sb = New-Object System.Text.StringBuilder
  foreach ($l in $arr) { [void]$sb.Append([string]$l.Text); [void]$sb.Append(' ') }
  return $sb.ToString()
}

# 用「文字特徵」判背包／彈窗面板，結果寫進 $Layout（pass-by-ref，不覆蓋已判定的 ending）。
# 回傳 $true＝這一幀看到版面特徵字（背包標題／三欄表格欄位名）。
function Test-RegionTextLayout {
  param($Lines, [ref]$Layout)
  $flat = (Join-LineText -Lines $Lines) -replace '\s', ''
  if (-not $flat) { return $false }
  if ($flat -match '背包与装备|背包與裝備|背包和装备|装备栏|裝備欄') {
    if ($Layout.Value -eq 'normal') { $Layout.Value = 'backpack' }
    return $true
  }
  if ($flat -match '编号|編號|雕刻|门后|門後') {
    if ($Layout.Value -eq 'normal') { $Layout.Value = 'panel' }
    return $true
  }
  if ($flat -match '專案|项目|項目|任务|任務' -and $flat -match '目标|目標|属性|屬性|物品|道具') {
    if ($Layout.Value -eq 'normal') { $Layout.Value = 'panel' }
    return $true
  }
  return $false
}

# 便宜的特殊版面預檢（只 OCR 標題列那一小條）：標準遊玩畫面的標題列一定有
# 「Level N「…」」；看不到就代表這不是標準畫面（背包／結局／三欄面板都會蓋掉它）。
function Test-SpecialScreen {
  param($Lines)
  $arr = @()
  if ($null -ne $Lines) { $arr = [object[]]$Lines }
  foreach ($l in $arr) {
    if ($rxLevelHead.IsMatch([string]$l.Text)) { return $false }
  }
  return $true
}

# 背包面板：哪些行值得留下（屬性／物品列表）。只是「順便抽出結構」，
# 不強求完整；判斷不出來的行照舊不記，不會弄壞既有輸出。
function Test-BackpackLine {
  param([string]$T)
  if ([string]::IsNullOrEmpty($T)) { return $false }
  if ($T.Length -lt 2) { return $false }
  if ($RxDropLike.IsMatch($T)) { return $false }
  $bare = $T.TrimEnd([char[]]'。！!？?…·.：: 　')
  if ($script:DropMain -contains $bare) { return $false }
  if ($T -match '(数量|數量|耐久|负重|負重|攻击|攻擊|防御|防禦|价格|價格|效果|说明|說明|等级|等級|属性|屬性|物品|道具|装备|裝備|武器|护甲|護甲|消耗|使用|剩余|剩餘|任务|任務|键|選單|选单)\s*[:：]?\s*\S') { return $true }
  if ($T -match '^[\u4e00-\u9fff]{1,8}\s*[xX×]\s*[0-9]{1,3}$') { return $true }
  if ($T -match '^[\u4e00-\u9fff]{2,10}\s+[0-9]{1,4}(?:\s+[0-9]{1,4}){0,3}$') { return $true }
  return $false
}

# ============================ 分區 OCR / 配色 ============================
# 校正基準（見 _drafts\截圖分區座標.md）：render 區 1696x1300、座標在放大之前。
# ⚠ 這個基準必須與 -WinSize 的預設值（1720x1392 → render 1696x1300）成對更新：
#   換了預設視窗尺寸，就要用 -Calibrate 重量預設的 -Region* 值，並同步改這裡。
$script:BaseW = 1696
$script:BaseH = 1300

# 座標規格解析：'x0,x1,y0,y1'，允許負值 = 從右緣／下緣起算（見 param 區說明）。
# 回傳 @{ OK; X0; X1; Y0; Y1; RightAnchored; BottomAnchored }；解析失敗回 OK=$false。
function Read-RegionSpec {
  param([string]$Spec)
  $bad = [pscustomobject]@{ OK = $false; X0 = 0; X1 = 0; Y0 = 0; Y1 = 0; RightAnchored = $false; BottomAnchored = $false }
  if (-not $Spec) { return $bad }
  # ⚠ 一定要用 regex 抓數字：用 -split '[,\s]+' 再 [double]::TryParse 的話，
  #   PowerShell 5.1 會把 "-548" 當成運算子而轉成空字串，負值（右緣錨定）永遠解析失敗。
  $rx = [regex]'^\s*(-?\d+(?:\.\d+)?)\s*[,\s]\s*(-?\d+(?:\.\d+)?)\s*[,\s]\s*(-?\d+(?:\.\d+)?)\s*[,\s]\s*(-?\d+(?:\.\d+)?)\s*$'
  $m = $rx.Match([string]$Spec)
  if (-not $m.Success) { return $bad }
  $n = @()
  for ($i = 1; $i -le 4; $i++) {
    $d = 0.0
    if (-not [double]::TryParse($m.Groups[$i].Value, [ref]$d)) { return $bad }
    $n += $d
  }
  return [pscustomobject]@{
    OK = $true; X0 = $n[0]; X1 = $n[1]; Y0 = $n[2]; Y1 = $n[3]
    RightAnchored = ($n[0] -lt 0 -or $n[1] -lt 0)
    BottomAnchored = ($n[2] -lt 0 -or $n[3] -lt 0)
  }
}

# 已實測過的 render 尺寸 → 各區塊的實際邊界。**這張表優先於任何換算。**
# 為什麼需要它：這遊戲是 Unity canvas，但版面不是單純等比縮放 ——
#   寬度：2067→1696 剛好是 0.8205 倍（實測左右界完全吻合）
#   高度：故事面板的上下緣跟著「上方敵人列表高度」跑，不是等比（實測 1696x1300 的
#         面板上緣在 y=221，用高度比 1.079 乘舊基準的 100 只會得到 108 → 差 113px）
# 所以「已量過的尺寸」直接用實測值；沒量過的尺寸才退回等比縮放＋自動掃描校正。
# 要新增尺寸：跑 `-WinSize '<W>x<H>' -WinPos right` 再跑 `-Calibrate`，把量到的填進來。
$script:MeasuredRegions = @{
  '1696x1300' = @{   # 視窗 1720x1392（3440x1440 的右半貼靠）；2026-10-16 逐列掃描實測
    title = '10,1091,0,95'
    main  = '21,1091,221,947'
    opts  = '21,1091,956,1281'
    char  = '-548,-5,95,216'
  }
  '2067x1205' = @{   # 視窗 2091x1297 @ 右緣；舊基準尺寸（_drafts\截圖分區座標.md）
    # ⚠ 上下界是實測後**修過**的：舊值 y 起 100／783 會把上方敵人列表（佔到 y≈220）
    # 一起收進 main，[main] 開頭就混進「不死的飢餓的回聲人影A／防禦 13」。
    # 實測面板上緣 = 225、下緣 = 776（本尺寸與 1696x1300 都用 0.82/0.95 的比例對得上）。
    title = '10,1330,0,88'
    main  = '18,1336,225,776'
    opts  = '18,1336,785,1184'
    char  = '1155,1700,88,200'
  }
}

# 這個 render 尺寸有沒有實測值？有的話回傳直接可用的 Rectangle（已夾進畫面內）
function Resolve-MeasuredRegion {
  param([string]$Name, [int]$CropW, [int]$CropH)
  if (-not $Name -or -not $script:MeasuredRegions) { return $null }
  $key = ('' + $CropW + 'x' + $CropH)
  if (-not $script:MeasuredRegions.ContainsKey($key)) { return $null }
  $tbl = $script:MeasuredRegions[$key]
  if (-not $tbl.ContainsKey($Name)) { return $null }
  $sp = Read-RegionSpec -Spec ([string]$tbl[$Name])
  if (-not $sp.OK) { return $null }
  $x0 = [int]$sp.X0; $x1 = [int]$sp.X1; $y0 = [int]$sp.Y0; $y1 = [int]$sp.Y1
  if ($sp.X0 -lt 0) { $x0 = $CropW + [int]$sp.X0 }
  if ($sp.X1 -lt 0) { $x1 = $CropW + [int]$sp.X1 }
  if ($sp.Y0 -lt 0) { $y0 = $CropH + [int]$sp.Y0 }
  if ($sp.Y1 -lt 0) { $y1 = $CropH + [int]$sp.Y1 }
  if ($x1 -gt $CropW) { $d = $x1 - $CropW; $x0 -= $d; $x1 = $CropW }
  if ($y1 -gt $CropH) { $d = $y1 - $CropH; $y0 -= $d; $y1 = $CropH }
  if ($x0 -lt 0) { $x0 = 0 }
  if ($y0 -lt 0) { $y0 = 0 }
  if ($x1 -gt $CropW) { $x1 = $CropW }
  if ($y1 -gt $CropH) { $y1 = $CropH }
  if ($x1 -le $x0 -or $y1 -le $y0) { return $null }
  return (New-Object System.Drawing.Rectangle($x0, $y0, ($x1 - $x0), ($y1 - $y0)))
}

# 'x0,x1,y0,y1'（校正基準像素）→ 依實際 render 區尺寸縮放 ＋ 負值改由右緣／下緣起算
# ＋ 超出畫面的區塊夾回畫面內（夾不住才回 $null）。回傳 System.Drawing.Rectangle。
# 給了 -Name 而且該尺寸有實測值 → 直接用實測值（見上面 $script:MeasuredRegions）。
function ConvertTo-RegionRect {
  param([string]$Spec, [int]$CropW, [int]$CropH, [string]$Name = '')
  $sp = Read-RegionSpec -Spec $Spec
  if (-not $sp.OK -or $CropW -lt 1 -or $CropH -lt 1) { return $null }
  if ($Name) {
    $mr = Resolve-MeasuredRegion -Name $Name -CropW $CropW -CropH $CropH
    if ($mr) { return $mr }
  }
  # 沒實測值的尺寸才換算：x 用寬度比、y 用高度比（兩軸比例本來就不同）
  $kx = $CropW / [double]$script:BaseW
  $ky = $CropH / [double]$script:BaseH
  $x0 = [int][Math]::Round($sp.X0 * $kx); $x1 = [int][Math]::Round($sp.X1 * $kx)
  $y0 = [int][Math]::Round($sp.Y0 * $ky); $y1 = [int][Math]::Round($sp.Y1 * $ky)
  # 負值 = 從右緣／下緣起算（相對於畫面尺寸，不隨縮放跑掉）
  if ($sp.X0 -lt 0) { $x0 = $CropW + [int][Math]::Round($sp.X0 * $kx) }
  if ($sp.X1 -lt 0) { $x1 = $CropW + [int][Math]::Round($sp.X1 * $kx) }
  if ($sp.Y0 -lt 0) { $y0 = $CropH + [int][Math]::Round($sp.Y0 * $ky) }
  if ($sp.Y1 -lt 0) { $y1 = $CropH + [int][Math]::Round($sp.Y1 * $ky) }
  if ($x1 -lt $x0) { $t = $x0; $x0 = $x1; $x1 = $t }
  if ($y1 -lt $y0) { $t = $y0; $y0 = $y1; $y1 = $t }
  # 夾進畫面內（先平移、平移不夠才縮）
  if ($x1 -gt $CropW) { $d = $x1 - $CropW; $x0 -= $d; $x1 = $CropW }
  if ($y1 -gt $CropH) { $d = $y1 - $CropH; $y0 -= $d; $y1 = $CropH }
  if ($x0 -lt 0) { $x0 = 0 }
  if ($y0 -lt 0) { $y0 = 0 }
  if ($x1 -gt $CropW) { $x1 = $CropW }
  if ($y1 -gt $CropH) { $y1 = $CropH }
  if ($x1 -le $x0 -or $y1 -le $y0) { return $null }
  return (New-Object System.Drawing.Rectangle($x0, $y0, ($x1 - $x0), ($y1 - $y0)))
}

# 同上，但直接把「目前參數」換成掃描用的提示字串（'x0,x1,y0,y1'，render 區像素）。
# Invoke-PanelScan 的 -Hint 一定要吃**這一組**，不能用校正基準的原始字串 ——
# 否則在小畫面上會拿著 2067 基準的座標去找 1696 寬的面板，永遠量不到（假陽性來源）。
function Get-RegionHint {
  param([string]$Spec, [int]$CropW, [int]$CropH, [string]$Name = '')
  $rc = ConvertTo-RegionRect -Spec $Spec -CropW $CropW -CropH $CropH -Name $Name
  if (-not $rc) { return '' }
  return ('' + $rc.Left + ',' + $rc.Right + ',' + $rc.Top + ',' + $rc.Bottom)
}

# 把 'x0,x1,y0,y1' 拆成 int 陣列；失敗回 $null
function ConvertTo-SpecArray {
  param([string]$Spec)
  $sp = Read-RegionSpec -Spec $Spec
  if (-not $sp.OK) { return $null }
  return @([int][Math]::Round($sp.X0), [int][Math]::Round($sp.X1), [int][Math]::Round($sp.Y0), [int][Math]::Round($sp.Y1))
}

# 在 $Bmp 的 $Rect 裡取樣平均色
function Get-RectAverageColor {
  param($Bmp, $Rect)
  if (-not $Bmp -or -not $Rect) { return $null }
  $x0 = [Math]::Max(0, $Rect.Left); $y0 = [Math]::Max(0, $Rect.Top)
  $x1 = [Math]::Min($Bmp.Width - 1, $Rect.Right - 1); $y1 = [Math]::Min($Bmp.Height - 1, $Rect.Bottom - 1)
  if ($x1 -lt $x0 -or $y1 -lt $y0) { return $null }
  $r = 0L; $g = 0L; $b = 0L; $n = 0
  for ($y = $y0; $y -le $y1; $y += 2) {
    for ($x = $x0; $x -le $x1; $x += 2) {
      $c = $Bmp.GetPixel($x, $y)
      $r += $c.R; $g += $c.G; $b += $c.B; $n++
    }
  }
  if ($n -eq 0) { return $null }
  $ar = [int][Math]::Round($r / [double]$n); $ag = [int][Math]::Round($g / [double]$n); $ab = [int][Math]::Round($b / [double]$n)
  return [pscustomobject]@{
    R = $ar; G = $ag; B = $ab
    Hex = ('#{0:X2}{1:X2}{2:X2}' -f $ar, $ag, $ab)
    Rgb = ('rgb({0},{1},{2})' -f $ar, $ag, $ab)
  }
}

function Test-ColorSame {
  param($A, $B, [int]$Threshold)
  if (-not $A -or -not $B) { return $false }
  $d = [Math]::Max([Math]::Abs($A.R - $B.R), [Math]::Max([Math]::Abs($A.G - $B.G), [Math]::Abs($A.B - $B.B)))
  return ($d -le $Threshold)
}

# 從 title 區的 OCR 行抓「Level N「名稱」」
function Get-LevelName {
  param($Lines)
  $arr = @()
  if ($null -ne $Lines) { $arr = [object[]]$Lines }
  $best = ''
  foreach ($l in $arr) {
    $t = [string]$l.Text
    if ($t -match '(Level\s*[0-9]+(?:\.5)?\s*[「『][^」』]{1,24}[」』])') { return $Matches[1].Trim() }
    if ($t -match '(第\s*[0-9]+\s*[层層][^|｜]{0,24})') { if (-not $best) { $best = $Matches[1].Trim() } }
  }
  return $best
}

# 寫一幀的 ocr-raw.log（幀標頭 ＋ 每行 [區塊名] 前綴）
function Write-RawFrame {
  param([string]$Path, $Bom, [int]$FrameNo, [int]$ImgW, [int]$ImgH, [int]$OcrW, [int]$OcrH, [string]$ColorHex, $Lines, [string]$Layout = 'normal', [string]$Deskew = '')
  # $Bom 不宣告型別：宣告成 [string] 會把 UTF8Encoding 轉成字串，AppendAllText 就找不到多載
  $sb = New-Object System.Text.StringBuilder
  if (-not $ColorHex) { $ColorHex = '?' }
  # PowerShell 5.1 的兩個陷阱（實測會拋「參數類型不匹配 Argument types do not match」）：
  #   1) @($List)  ← 對 Generic.List[object] 用陣列子表達式會炸 → 要用 [object[]] 轉型
  #   2) '...' + @($List).Count ← 會解析成 ('...' + @($List)).Count → 要先把數字算好
  $arr = @()
  if ($null -ne $Lines) { $arr = [object[]]$Lines }
  if (-not $Layout) { $Layout = 'normal' }
  $dk = ''
  if ($Deskew) { $dk = ' 去斜=' + $Deskew }
  [void]$sb.AppendLine('===== ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' 幀 #' + $FrameNo + ' ' + $ImgW + 'x' + $ImgH + ' (OCR ' + $OcrW + 'x' + $OcrH + ')' + ' 配色=' + $ColorHex + ' 版面=' + $Layout + $dk + ' 行數=' + $arr.Count + ' =====')
  foreach ($l in $arr) { [void]$sb.AppendLine('[' + $l.Region + '] ' + $l.Text) }
  [System.IO.File]::AppendAllText($Path, $sb.ToString(), $Bom)
}

# 同一幀的結構化資料：保留每行的區塊、座標與文字，供 UI 狀態機／Wiki 匯入用
function Write-StructuredFrame {
  param([string]$Path, [int]$FrameNo, [int]$ImgW, [int]$ImgH, [string]$Layout, [string]$ColorHex, $Lines)
  $arr = @()
  if ($null -ne $Lines) { $arr = [object[]]$Lines }
  $out = @()
  foreach ($l in $arr) {
    $out += [pscustomobject]@{
      r = [string]$l.Region; t = [string]$l.Text;
      x = [int]$l.X; y = [int]$l.Y; w = [int]$l.W; h = [int]$l.H;
      c = [string]$l.Crop; ink = [string]$l.Ink
    }
  }
  $lay = "normal"
  if ($Layout) { $lay = [string]$Layout }
  $obj = [pscustomobject]@{
    ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"); frame = $FrameNo;
    w = $ImgW; h = $ImgH; layout = $lay;
    bg = $ColorHex; lines = @($out)
  }
  $json = $obj | ConvertTo-Json -Compress -Depth 4
  [System.IO.File]::AppendAllText($Path, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
}

# 靜止畫面：連續多幀完全一樣時只記一筆「區間」，不再每幀寫一份完整 lines。
# 2026-10-30：8 小時停在主選單 → 35,618 幀 → structured.jsonl 152 MB（同一段文字寫了三萬多次）。
# 現在同樣的情況只會留 1 筆 dup 區間 + 前後各 1 幀。
function Write-StructuredDup {
  param([string]$Path, [int]$From, [int]$To, [string]$Layout, [string]$ColorHex)
  if ($To -le $From) { return }
  $obj = [pscustomobject]@{
    ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"); frame = $From;
    dupTo = $To; dup = ($To - $From + 1);
    layout = $(if ($Layout) { [string]$Layout } else { 'normal' });
    bg = $ColorHex; lines = @()
  }
  $json = $obj | ConvertTo-Json -Compress -Depth 3
  [System.IO.File]::AppendAllText($Path, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
}

# 給客戶端覆蓋層用的輕量狀態：只寫「這一幀用了哪些區塊、讀到哪些行」。
# 刻意不寫進 structured.jsonl：overlay 是除錯 UI，不該污染正式紀錄。
function Write-OverlayState {
  param(
    $S, [int]$FrameNo, [int]$W, [int]$H, [string]$Layout, [string]$ColorHex,
    $Specs, $Lines, [int]$Fallbacks = 0, [string]$Detail = '', [bool]$Running = $true
  )
  if ($NoOverlay) { return }
  try {
    $regions = @()
    $specArr = @()
    if ($null -ne $Specs) { $specArr = [object[]]$Specs }
    foreach ($sp in $specArr) {
      if (-not $sp) { continue }
      $r = ConvertTo-RegionRect -Spec ([string]$sp.Spec) -CropW $W -CropH $H -Name ([string]$sp.Name)
      if ($r) {
        $regions += [pscustomobject]@{ n = [string]$sp.Name; x = [int]$r.Left; y = [int]$r.Top; w = [int]$r.Width; h = [int]$r.Height }
      }
    }
    $measured = @()
    if ($S.LastMeasured) {
      foreach ($pair in @(@('main', [string]$S.LastMeasured.Main), @('opts', [string]$S.LastMeasured.Opts))) {
        $spec = [string]$pair[1]
        if (-not $spec) { continue }
        $p = @($spec -split '[,\s]+' | Where-Object { $_ -ne '' })
        if ($p.Count -eq 4) {
          $x0 = [int]$p[0]; $x1 = [int]$p[1]; $y0 = [int]$p[2]; $y1 = [int]$p[3]
          if ($x1 -gt $x0 -and $y1 -gt $y0) {
            $measured += [pscustomobject]@{ n = ('measured-' + [string]$pair[0]); x = $x0; y = $y0; w = ($x1 - $x0); h = ($y1 - $y0) }
          }
        }
      }
    }
    $outLines = @()
    $lineArr = @()
    if ($null -ne $Lines) { $lineArr = [object[]]$Lines }
    $n = 0
    foreach ($l in $lineArr) {
      if ($n -ge 80) { break }
      $t = [string]$l.Text
      if ($t.Length -gt 140) { $t = $t.Substring(0, 140) }
      $outLines += [pscustomobject]@{
        r = [string]$l.Region; t = $t;
        x = [int]$l.X; y = [int]$l.Y; w = [int]$l.W; h = [int]$l.H
      }
      $n++
    }
    $obj = [pscustomobject]@{
      running = $Running; session = [string]$S.SessionName; frame = $FrameNo;
      w = $W; h = $H; layout = $Layout; bg = $ColorHex;
      regions = @($regions); measured = @($measured); lines = @($outLines);
      fallbacks = $Fallbacks; detail = [string]$Detail
    }
    $json = $obj | ConvertTo-Json -Depth 6 -Compress
    $tmp = $overlayFile + '.tmp'
    [System.IO.File]::WriteAllText($tmp, $json, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $overlayFile -Force
  } catch {
    try { Write-Log $S ('overlay 寫入失敗：' + $_.Exception.Message) } catch { }
  }
}

# 分區 OCR 用的區塊表（名稱／座標規格／固定輸出順序）
# -Main／-Opts：本幀的實際座標（-AutoRegion 自動校正時會不一樣）；不給就用參數值
function Get-RegionSpecs {
  param([string]$Main, [string]$Opts)
  if (-not $Main) { $Main = $RegionMain }
  if (-not $Opts) { $Opts = $RegionOpts }
  return @(
    [pscustomobject]@{ Name = 'title'; Spec = $RegionTitle },
    [pscustomobject]@{ Name = 'enemy'; Spec = $RegionEnemy },
    [pscustomobject]@{ Name = 'main'; Spec = $Main },
    [pscustomobject]@{ Name = 'opts'; Spec = $Opts },
    [pscustomobject]@{ Name = 'char'; Spec = $RegionChar }
  )
}

# 對每個區塊：裁切（放大之前）→ Scale-Image → 暫存 PNG → OCR → 標記來源區塊。
# 座標會加回區塊原點，所以下游的 X/Y 仍等同整張 render 區的座標（隊伍頻道判斷不會壞）。
function Invoke-RegionOcr {
  param($Crop, $Engine, [double]$Scale, [string]$TmpPng, $Specs, [string]$LineDir = '', [int]$FrameNo = 0)
  $all = New-Object System.Collections.Generic.List[object]
  $detail = New-Object System.Collections.Generic.List[string]
  $failed = 0
  foreach ($sp in $Specs) {
    $rc = ConvertTo-RegionRect -Spec $sp.Spec -CropW $Crop.Width -CropH $Crop.Height -Name ('' + $sp.Name)
    if (-not $rc) { $failed++; $detail.Add('' + $sp.Name + '=座標無效'); continue }
    $bm = $null; $scaled = $null
    try {
      $bm = $Crop.Clone($rc, $Crop.PixelFormat)
      $scaled = Scale-Image $bm $Scale      # 注意：Scale-Image 會 Dispose 傳進去的圖
      $bm = $null
      # 先存暫存 PNG（Windows OCR 只吃檔案），OCR 完立刻由呼叫者刪掉
      Remove-Item -LiteralPath $TmpPng -Force -ErrorAction SilentlyContinue
      $scaled.Save($TmpPng, [System.Drawing.Imaging.ImageFormat]::Png)
      $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($TmpPng)) ([Windows.Storage.StorageFile])
      $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
      $bitmap = $null
      $n = 0
      try {
        $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $res = Await ($Engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $k = [Math]::Round($scaled.Width / [double]$rc.Width, 4)
        foreach ($line in $res.Lines) {
          $x1 = [double]::MaxValue; $y1 = [double]::MaxValue; $x2 = [double]::MinValue; $y2 = [double]::MinValue
          foreach ($wd in $line.Words) {
            $br = $wd.BoundingRect
            if ($br.X -lt $x1) { $x1 = $br.X }
            if ($br.Y -lt $y1) { $y1 = $br.Y }
            if (($br.X + $br.Width) -gt $x2) { $x2 = $br.X + $br.Width }
            if (($br.Y + $br.Height) -gt $y2) { $y2 = $br.Y + $br.Height }
          }
          if ($k -gt 1.0001) { $x1 /= $k; $y1 /= $k; $x2 /= $k; $y2 /= $k }
          $txt = Convert-OcrText $line.Text
          $txt = Repair-OcrText $txt            # 錯字修正規則表（-NoOcrFix 可整層關掉）
          if (-not (Test-KeepLine $txt)) { continue }
          $cropRel = ''
          if ($LineDir) {
            try {
              # 2026-10-30：只留「可能被讀壞」的行給 RapidOCR。
              # 以前每一行都裁、都重認，一場 4 分鐘 1500 張 ≈ 70 秒；實測真正需要重認的
              # 不到三成，其餘只是把 Windows OCR 已經讀對的字再讀一次。
              # 判斷：殘渣字／中英夾雜／Wiki 語料沒收過的字；同一段文字只留一張。
              if ($txt -and $script:CropByText.ContainsKey($txt)) {
                $cropRel = $script:CropByText[$txt]
              } elseif ($txt -and ((Test-NeedsRapid -Text $txt) -or ($sp.Name -eq 'opts') -or ($txt -match '[A-Za-z]'))) {
                # 選項區常把第一個字讀掉（例如「绕开」只剩「开」），
                # 裁圖往左多留 24px，RapidOCR 才有機會把被漏掉的字補回來。
                $padL = $(if ($sp.Name -eq 'opts') { 24 } else { 6 })
                $padR = 6; $padT = 3; $padB = 3
                $lx = [int][Math]::Max(0, [Math]::Floor($x1 + $rc.Left) - $padL)
                $ly = [int][Math]::Max(0, [Math]::Floor($y1 + $rc.Top) - $padT)
                $lw = [int][Math]::Min($Crop.Width - $lx, [Math]::Ceiling($x2 - $x1) + $padL + $padR)
                $lh = [int][Math]::Min($Crop.Height - $ly, [Math]::Ceiling($y2 - $y1) + $padT + $padB)
                if ($lw -gt 2 -and $lh -gt 2) {
                  New-Item -ItemType Directory -Force -Path $LineDir | Out-Null
                  $lb = $Crop.Clone((New-Object System.Drawing.Rectangle($lx, $ly, $lw, $lh)), $Crop.PixelFormat)
                  $ms = $null
                  try {
                    # 先編碼到記憶體 → 算內容 hash → 同內容只落盤一次（A）。
                    # 以前每幀每行都寫一張，一場 11 分鐘就 32k 張 / 115MB，其中 77% 是重複位元組。
                    $ms = New-Object System.IO.MemoryStream
                    $lb.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                    $bytes = $ms.ToArray()
                    $sha = [System.Security.Cryptography.SHA1]::Create()
                    try { $hashHex = [BitConverter]::ToString($sha.ComputeHash($bytes)) } finally { $sha.Dispose() }
                    $hashHex = $hashHex.Replace('-', '').Substring(0, 16).ToLower()
                    if ($script:LineCropHash.ContainsKey($hashHex)) {
                      $cropRel = $script:LineCropHash[$hashHex]
                    } else {
                      $cropRel = $hashHex + '.png'
                      [System.IO.File]::WriteAllBytes((Join-Path $LineDir $cropRel), $bytes)
                      $script:LineCropHash[$hashHex] = $cropRel
                    }
                    if ($cropRel -and $txt) { $script:CropByText[$txt] = $cropRel }
                  } finally {
                  if ($ms) { $ms.Dispose() }
                  $lb.Dispose()
                }
              }
              }
            } catch { $cropRel = '' }
          }
          # 背景 RapidOCR 已有同一張裁圖的結果 → 下一幀直接換掉 Windows OCR 的錯字。
          if ($cropRel) {
            $fix = Get-RapidFix -LineDir $LineDir -Name $cropRel
            if ($fix) { $txt = Repair-OcrText (Convert-OcrText $fix) }
          }
          $all.Add([pscustomobject]@{
              Text = $txt; Region = $sp.Name
              X = [int]($x1 + $rc.Left); Y = [int]($y1 + $rc.Top)
              W = [int]($x2 - $x1); H = [int]($y2 - $y1)
              Crop = $cropRel
            })
          $n++
        }
      } finally {
        if ($bitmap) { $bitmap.Dispose() }
        if ($stream) { $stream.Dispose() }
      }
      $detail.Add($sp.Name + '=' + $n)
    } catch {
      $failed++
      $detail.Add('' + $sp.Name + '=失敗')
    } finally {
      if ($bm) { $bm.Dispose() }
      if ($scaled) { $scaled.Dispose() }
    }
  }
  return [pscustomobject]@{ Lines = $all; Detail = ($detail -join ' '); Failed = $failed }
}

# ---- 校正：找面板邊界 ----
# 做法（同 _drafts\measure-frame3.ps1 的思路）：取樣面板底色當參考色 → 逐列／逐欄算「跟參考色
# 相近」的命中率 → 命中率掉到 10% 以下的橫列＝面板之間的空白分隔帶 → 夾住預設範圍的那兩條
# 分隔帶之間就是這個面板；左右界＝該範圍內第一／最後一條命中率 ≥90% 的直欄。
function Invoke-PanelScan {
  param($Bmp, [string]$Name, [string]$Hint, [int]$Tol = 16, [int]$HiHit = 90, [int]$LoHit = 10, [switch]$BoundX, [string]$SeedHex = '')
  $W = $Bmp.Width; $H = $Bmp.Height
  $hx0 = 0; $hx1 = $W - 1; $hy0 = 0; $hy1 = $H - 1
  $hp = @(([string]$Hint -split '[,\s]+') | Where-Object { $_ -ne '' })
  # 注意：PowerShell 5.1 的 [int]$arr[2] 會解析成 ([int]$arr)[2] → 一定要寫 [int]($arr[2])
  if ($hp.Count -eq 4) { $hx0 = [int]($hp[0]); $hx1 = [int]($hp[1]); $hy0 = [int]($hp[2]); $hy1 = [int]($hp[3]) }
  $sx = [int](($hx0 + $hx1) / 2); $sy = [int](($hy0 + $hy1) / 2)
  $sx = [Math]::Min($W - 1, [Math]::Max(0, $sx))
  $sy = [Math]::Min($H - 1, [Math]::Max($hy0 + 4, [Math]::Min($hy1, $sy)))
  $ref = $Bmp.GetPixel($sx, $sy)
  $rx = $ref.R; $gy = $ref.G; $bz = $ref.B
  # 種子色：優先使用配色快取裡「這一層的預期底色」（取樣點可能剛好壓在文字上 → 用種子色才穩）
  if ($SeedHex -and $SeedHex -match '^#?([0-9A-Fa-f]{6})$') {
    $h6 = $Matches[1]
    $rx = [Convert]::ToInt32($h6.Substring(0, 2), 16)
    $gy = [Convert]::ToInt32($h6.Substring(2, 2), 16)
    $bz = [Convert]::ToInt32($h6.Substring(4, 2), 16)
  }
  $hex = ('#{0:X2}{1:X2}{2:X2}' -f $rx, $gy, $bz)

  $rowX0 = [Math]::Max(0, $hx0); $rowX1 = [Math]::Min($hx1, $W - 1)
  if (($rowX1 - $rowX0) -lt 40) { $rowX0 = 0; $rowX1 = [Math]::Min($W - 1, 1340) }

  $rows = New-Object int[] $H
  for ($y = 0; $y -lt $H; $y++) {
    $n = 0; $tot = 0
    for ($x = $rowX0; $x -lt $rowX1; $x += 8) {
      $c = $Bmp.GetPixel($x, $y); $tot++
      if ([Math]::Abs($c.R - $rx) -le $Tol -and [Math]::Abs($c.G - $gy) -le $Tol -and [Math]::Abs($c.B - $bz) -le $Tol) { $n++ }
    }
    if ($tot -eq 0) { $rows[$y] = 0 } else { $rows[$y] = [int]($n * 100 / $tot) }
  }

  # 空白分隔帶（命中率 <= LoHit 的連續橫列，長度 >= 2；相距 <=3 列的先合併）
  $rawGaps = New-Object System.Collections.Generic.List[object]
  $st = -1
  for ($y = 0; $y -le $H; $y++) {
    $empty = ($y -lt $H) -and ($rows[$y] -le $LoHit)
    if ($empty) { if ($st -lt 0) { $st = $y } }
    elseif ($st -ge 0) { if (($y - $st) -ge 2) { $rawGaps.Add([pscustomobject]@{ Y0 = $st; Y1 = $y - 1 }) }; $st = -1 }
  }
  $gaps = New-Object System.Collections.Generic.List[object]
  foreach ($g in $rawGaps) {
    if ($gaps.Count -gt 0 -and ($g.Y0 - $gaps[$gaps.Count - 1].Y1) -le 3) { $gaps[$gaps.Count - 1].Y1 = $g.Y1 }
    else { $gaps.Add([pscustomobject]@{ Y0 = $g.Y0; Y1 = $g.Y1 }) }
  }
  $blank = [pscustomobject]@{ Name = $Name; Hex = $hex; Y0 = -1; Y1 = -1; X0 = -1; X1 = -1; Gaps = $gaps; SampleX = $sx; SampleY = $sy }
  if ($gaps.Count -lt 2) { return $blank }

  # 面板 = 兩條分隔帶之間：取「離預設範圍最近、但沒有切進預設範圍」的那兩條 ——
  # 上界 = 分隔帶底部 <= 預設上緣（+4px 容差）的最後一條；
  # 下界 = 分隔帶頂部 >= 預設下緣（-4px 容差）的第一條。
  # （畫面最上面本來就有一條大空白帶，不能用「第一條」來找。）
  $gTop = $null
  foreach ($g in $gaps) { if (([int]$g.Y1) -le ($hy0 + 4)) { $gTop = $g } }
  if ($null -eq $gTop) { foreach ($g in $gaps) { if (([int]$g.Y1) -le ($hy0 + 16)) { $gTop = $g } } }
  $gBot = $null
  foreach ($g in $gaps) { if (([int]$g.Y0) -ge ($hy1 - 4)) { $gBot = $g; break } }
  if ($null -eq $gBot) { foreach ($g in $gaps) { if (([int]$g.Y0) -gt $sy) { $gBot = $g; break } } }
  if ($null -eq $gBot) { return $blank }
  $py0 = 0
  if ($null -ne $gTop) { $py0 = [int]$gTop.Y1 + 1 }
  $py1 = [int]$gBot.Y0 - 1
  # 上下界可信嗎？偏離提示超過 6px 就會被夾回提示值（＝量測失敗，不是量到）。
  # 一定要把這件事記下來：沿用提示值卻回報「量到的」，就是校正的假陽性。
  $yPhi = ($py0 -le ($hy0 + 6) -and $py0 -ge ($hy0 - 6))
  $yPlo = ($py1 -le ($hy1 + 6) -and $py1 -ge ($hy1 - 6))
  $yCred = ($yPhi -and $yPlo)
  if (-not $yPhi) { $py0 = $hy0 }
  if (-not $yPlo) { $py1 = $hy1 }
  if ($py1 -le $py0) { return $blank }

  $cols = New-Object int[] $W
  for ($x = 0; $x -lt $W; $x++) {
    $n = 0; $tot = 0
    for ($y = $py0; $y -le $py1; $y += 4) {
      $c = $Bmp.GetPixel($x, $y); $tot++
      if ([Math]::Abs($c.R - $rx) -le $Tol -and [Math]::Abs($c.G - $gy) -le $Tol -and [Math]::Abs($c.B - $bz) -le $Tol) { $n++ }
    }
    if ($tot -eq 0) { $cols[$x] = 0 } else { $cols[$x] = [int]($n * 100 / $tot) }
  }
  # 同一種底色常常橫跨相鄰面板（例如隊伍頻道跟主面板同色）→ 可要求只找預設範圍附近那塊
  if ($BoundX) {
    $bx0 = [Math]::Max(0, $hx0 - 40); $bx1 = [Math]::Min($W - 1, $hx1 + 40)
    $sm = New-Object int[] $W
    for ($x = 0; $x -lt $W; $x++) {
      $s = 0; $cnt = 0
      for ($k = -6; $k -le 6; $k++) { $i = $x + $k; if ($i -ge $bx0 -and $i -le $bx1) { $s += $cols[$i]; $cnt++ } }
      if ($cnt -eq 0) { $sm[$x] = 0 } else { $sm[$x] = [int]($s / $cnt) }
    }
    $x0 = -1; $x1 = -1
    for ($x = $bx0; $x -le $bx1; $x++) { if ($sm[$x] -ge 85) { if ($x0 -lt 0) { $x0 = $x }; $x1 = $x } }
    $xFellBack = $false
    if ($x0 -lt 0) { $x0 = $hx0; $x1 = $hx1; $xFellBack = $true }
    # 左右界可信嗎？主文字面板被文字佈滿時，幾乎沒有整欄都是底色的直欄 → 量出來的左右界會
    # 遠比真正的面板窄（把文字切掉）。所以只信「跨距至少是提示範圍 70%」的量測結果。
    $hSpan = $hx1 - $hx0
    $xCred = ((-not $xFellBack) -and ($hSpan -gt 0) -and (($x1 - $x0) -ge (0.7 * $hSpan)))
    return [pscustomobject]@{ Name = $Name; Hex = $hex; Y0 = $py0; Y1 = $py1; X0 = $x0; X1 = $x1; Gaps = $gaps; SampleX = $sx; SampleY = $sy; Bound = $true; XCredible = $xCred; YCredible = $yCred }
  }
  $x0 = -1; $x1 = -1
  for ($x = 0; $x -lt $W; $x++) { if ($cols[$x] -ge $HiHit) { if ($x0 -lt 0) { $x0 = $x }; $x1 = $x } }
  return [pscustomobject]@{ Name = $Name; Hex = $hex; Y0 = $py0; Y1 = $py1; X0 = $x0; X1 = $x1; Gaps = $gaps; SampleX = $sx; SampleY = $sy; Bound = $false; XCredible = ($x0 -ge 0); YCredible = $yCred }
}

# 量到的邊界（render 區像素）→ 'x0,x1,y0,y1'
function Format-RegionRect {
  param($Rect)
  if (-not $Rect) { return '' }
  return ('' + $Rect.Left + ',' + $Rect.Right + ',' + $Rect.Top + ',' + $Rect.Bottom)
}

# 把一次掃描結果整理成「可信／沿用參數」講清楚的物件。
# ⚠ 為什麼要這樣：舊版在左右界「量不到」時會悄悄沿用參數值再拿去跟參數比，
#   於是永遠顯示「差 0px ✓」——那是假陽性（使用者會以為校準正確）。
#   現在 x／y 兩軸各自標示「量到」或「沿用」，只有真的量到才給結論。
function ConvertTo-RegionMeasurement {
  param($R, $Hint)
  # Hint 不宣告型別：PowerShell 5.1 對 [System.Drawing.Rectangle] 參數傳 $null 會拋
  # ParameterArgumentTransformationException（這裡常常沒有提示矩形可用）
  $hr = $null
  if ($Hint -is [System.Drawing.Rectangle]) { $hr = $Hint }
  $o = [pscustomobject]@{
    Rect = $null; Text = ''; XCredible = $false; YCredible = $false; Credible = $false
    XFrom = '失敗'; YFrom = '失敗'; X0 = -1; X1 = -1; Y0 = -1; Y1 = -1
  }
  if (-not $R -or -not $hr) { return $o }
  $xCred = $false
  if ($R.PSObject.Properties['XCredible']) { $xCred = [bool]$R.XCredible }
  $yCred = $false
  if ($R.PSObject.Properties['YCredible']) { $yCred = [bool]$R.YCredible }
  if ($R.Y0 -lt 0) { $yCred = $false }
  # 可信的軸用「量到的值」，不可信的軸沿用提示（並如實標示）
  if ($xCred -and $R.X0 -ge 0 -and $R.X1 -gt $R.X0) {
    $o.X0 = [int]$R.X0; $o.X1 = [int]$R.X1; $o.XCredible = $true; $o.XFrom = '量到'
  } else {
    $o.X0 = $hr.Left; $o.X1 = $hr.Right; $o.XFrom = '沿用參數'
  }
  if ($yCred -and $R.Y1 -gt $R.Y0) {
    $o.Y0 = [int]$R.Y0; $o.Y1 = [int]$R.Y1; $o.YCredible = $true; $o.YFrom = '量到'
  } else {
    $o.Y0 = $hr.Top; $o.Y1 = $hr.Bottom; $o.YFrom = '沿用參數'
  }
  $o.Rect = New-Object System.Drawing.Rectangle($o.X0, $o.Y0, ($o.X1 - $o.X0), ($o.Y1 - $o.Y0))
  $o.Text = Format-RegionRect -Rect $o.Rect
  $o.Credible = ($o.XCredible -and $o.YCredible)
  return $o
}

# 量到的邊界物件 ＋「目前參數在 render 區的實際提示」→ 比較用的完整結果
function Compare-RegionMeasure {
  param($Meas, $Hint)
  $hr = $null
  if ($Hint -is [System.Drawing.Rectangle]) { $hr = $Hint }
  $o = [pscustomobject]@{ Text = ''; Hint = ''; XFrom = '失敗'; YFrom = '失敗'; Diff = -1; Any = $false }
  if ($hr) { $o.Hint = Format-RegionRect -Rect $hr }
  if (-not $Meas -or -not $Meas.Rect -or -not $hr) { return $o }
  $o.Text = $Meas.Text; $o.XFrom = $Meas.XFrom; $o.YFrom = $Meas.YFrom
  if (-not $Meas.XCredible -or -not $Meas.YCredible) { return $o }
  # 左上界「量到比參數更靠左／更靠上」不算問題（同色的隔壁面板會被算進來）→ 只夾窄的方向
  $x0 = $Meas.Rect.Left; if ($x0 -lt $hr.Left) { $x0 = $hr.Left }
  $y0 = $Meas.Rect.Top; if ($y0 -lt $hr.Top) { $y0 = $hr.Top }
  $x1 = $Meas.Rect.Right; if ($x1 -gt $hr.Right) { $x1 = $hr.Right }
  $y1 = $Meas.Rect.Bottom; if ($y1 -gt $hr.Bottom) { $y1 = $hr.Bottom }
  $d = 0
  foreach ($pair in @(@($x0, $hr.Left), @($y0, $hr.Top), @($x1, $hr.Right), @($y1, $hr.Bottom))) {
    $dd = [Math]::Abs(([int]$pair[0]) - ([int]$pair[1])); if ($dd -gt $d) { $d = $dd }
  }
  $o.Diff = $d; $o.Any = $true
  return $o
}

# ---- 樓層配色：找 main 區內一小塊沒有文字的區域 ----
# ⚠ 為什麼不能只看「右上角往內縮 40px」：空的故事面板是半透明的，畫面剛好是空白旁白時，
#   那一小塊量到的其實是「背後的世界貼圖」而不是面板底色 —— 面板掃描的參考色就全錯，
#   掃描找不到任何同色的列／欄，校正永遠「量測失敗」（或更糟：給出假結論）。
# 改成：先找 main 區的「主色」（＝面板底色的最大公約數），再從幾個候選位置裡挑
# 「最接近主色」的一塊當取樣。
function Get-MainFillColor {
  param($Bmp, $MainRect, [int]$Step = 8)
  if (-not $Bmp -or -not $MainRect -or $MainRect.Width -lt 40 -or $MainRect.Height -lt 40) { return $null }
  # 只掃中央 80% 區域＋放大取樣間距：面板底色到處都一樣，全片細掃只是浪費時間
  # （每一步都是 GetPixel，實測 4px 全掃要 270ms/幀，8px 中央掃約 20ms）
  $w = $MainRect.Right - $MainRect.Left; $h = $MainRect.Bottom - $MainRect.Top
  $x0 = $MainRect.Left + [int]($w * 0.1); $x1 = $MainRect.Right - [int]($w * 0.1)
  $y0 = $MainRect.Top + [int]($h * 0.1); $y1 = $MainRect.Bottom - [int]($h * 0.1)
  if ($x1 -le $x0 -or $y1 -le $y0) { $x0 = $MainRect.Left; $x1 = $MainRect.Right; $y0 = $MainRect.Top; $y1 = $MainRect.Bottom }
  $histo = @{}
  for ($y = $y0; $y -lt $y1; $y += $Step) {
    for ($x = $x0; $x -lt $x1; $x += $Step) {
      $c = $Bmp.GetPixel($x, $y)
      $k = ('{0:X2}{1:X2}{2:X2}' -f ([int]($c.R / 8) * 8), ([int]($c.G / 8) * 8), ([int]($c.B / 8) * 8))
      if ($histo.ContainsKey($k)) { $histo[$k]++ } else { $histo[$k] = 1 }
    }
  }
  if ($histo.Count -eq 0) { return $null }
  $best = $histo.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1
  $top = $histo.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 3
  $tot = 0; foreach ($v in $histo.Values) { $tot += $v }
  return [pscustomobject]@{
    Hex = ('#' + $best.Key)
    Fraction = [Math]::Round($best.Value / [double][Math]::Max(1, $tot), 3)
    Ties = (($top | ForEach-Object { '#' + $_.Key }) -join '/')
  }
}

# 牆色：面板「外面」的底色。
# 2026-10-30：Wiki 頁面底色一直被填成面板內側的卡片色（甚至整幀近黑），
# 因為 Get-MainFillColor 掃的是 main 面板裡面。這裡改成掃畫面四邊的窄條，
# 那才是遊戲真正的「牆」。四條都掃、取出現最多次的量化色。
function Get-WallColor {
  param($Bmp, $MainRect, [int]$Step = 6)
  if (-not $Bmp) { return $null }
  $W = $Bmp.Width; $H = $Bmp.Height
  if ($W -lt 40 -or $H -lt 40) { return $null }
  $band = [Math]::Max(6, [int]($W * 0.025))
  $histo = @{}
  $scan = {
    param($xs, $xe, $ys, $ye, $xo, $yo)
    for ($y = $ys; $y -lt $ye; $y += $Step) {
      for ($x = $xs; $x -lt $xe; $x += $Step) {
        $px = $x; $py = $y
        if ($MainRect -and $px -ge $MainRect.Left -and $px -le $MainRect.Right -and $py -ge $MainRect.Top -and $py -le $MainRect.Bottom) { continue }
        $c = $Bmp.GetPixel($px, $py)
        $k = ('{0:X2}{1:X2}{2:X2}' -f ([int]($c.R / 8) * 8), ([int]($c.G / 8) * 8), ([int]($c.B / 8) * 8))
        if ($histo.ContainsKey($k)) { $histo[$k]++ } else { $histo[$k] = 1 }
      }
    }
  }
  & $scan 2 ($W - 2) 2 $band 0 0
  & $scan 2 ($W - 2) ($H - $band) ($H - 2) 0 0
  & $scan 2 $band 2 ($H - 2) 0 0
  & $scan ($W - $band) ($W - 2) 2 ($H - 2) 0 0
  if ($histo.Count -eq 0) { return $null }
  $best = $histo.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1
  $tot = 0; foreach ($v in $histo.Values) { $tot += $v }
  return [pscustomobject]@{ Hex = ('#' + $best.Key); Fraction = [Math]::Round($best.Value / [double][Math]::Max(1, $tot), 3) }
}

# 取樣方塊：從候選位置挑「最接近主色」的一塊；沒有圖／沒有主色時退回舊行為。
function Get-BgSampleRect {
  param($MainRect, [int]$Sample, $Bmp = $null, [string]$FillHex = '')
  if (-not $MainRect) { return $null }
  $w = $MainRect.Width - 80; $h = [Math]::Min(160, [Math]::Max(40, $MainRect.Height - 120))
  $bw = [Math]::Min($Sample, [Math]::Max(8, $w)); $bh = [Math]::Min($Sample, [Math]::Max(8, $h))
  if ($bw -lt 8 -or $bh -lt 8) { return $null }
  $ptX = $MainRect.Right - 40 - $bw
  $ptY = $MainRect.Top + 40
  $fr = -1; $fg = -1; $fb = -1; $haveFill = $false
  if ($FillHex -and $FillHex -match '^#?([0-9A-Fa-f]{6})$') {
    $h6 = $Matches[1]
    $fr = [Convert]::ToInt32($h6.Substring(0, 2), 16); $fg = [Convert]::ToInt32($h6.Substring(2, 2), 16); $fb = [Convert]::ToInt32($h6.Substring(4, 2), 16)
    $haveFill = $true
  }
  if (-not $Bmp -or -not $haveFill) {
    $x = $ptX; $y = $ptY
    if ($x -lt $MainRect.Left) { $x = $MainRect.Left }
    if (($y + $bh) -gt $MainRect.Bottom) { $y = $MainRect.Bottom - $bh }
    if ($x -lt 0 -or $y -lt 0) { return $null }
    return (New-Object System.Drawing.Rectangle($x, $y, $bw, $bh))
  }
  $cands = @(
    @($ptX, $ptY),
    @($ptX, ($MainRect.Top + 60)),
    @($ptX, ($MainRect.Bottom - $bh - 20)),
    @(($MainRect.Left + 20), ($MainRect.Bottom - $bh - 20)),
    @($ptX, [int](($MainRect.Top + $MainRect.Bottom) / 2))
  )
  $bestScore = [double]::MaxValue; $bestRect = $null
  foreach ($cd in $cands) {
    $x = $cd[0]; $y = $cd[1]
    if ($x -lt $MainRect.Left) { $x = $MainRect.Left }
    if ($y -lt $MainRect.Top) { $y = $MainRect.Top }
    if (($x + $bw) -gt $MainRect.Right) { $x = $MainRect.Right - $bw }
    if (($y + $bh) -gt $MainRect.Bottom) { $y = $MainRect.Bottom - $bh }
    if ($x -lt 0 -or $y -lt 0) { continue }
    # 「離主色很近」的像素比例越高越像面板底色（越低越像文字或貼圖）
    $near = 0; $tot2 = 0
    for ($yy = $y; $yy -lt ($y + $bh); $yy += 3) {
      for ($xx = $x; $xx -lt ($x + $bw); $xx += 3) {
        $c = $Bmp.GetPixel($xx, $yy); $tot2++
        if ([Math]::Abs($c.R - $fr) -le 16 -and [Math]::Abs($c.G - $fg) -le 16 -and [Math]::Abs($c.B - $fb) -le 16) { $near++ }
      }
    }
    $score = 1.0 - ($near / [double][Math]::Max(1, $tot2))
    if ($score -lt $bestScore) { $bestScore = $score; $bestRect = (New-Object System.Drawing.Rectangle($x, $y, $bw, $bh)) }
  }
  return $bestRect
}

# ============================ 樓層配色快取（自動校正分區） ============================
# 為什麼要這個：
#   面板底色「每一層都不一樣」，用固定色差門檻找邊界，換一層就可能失準。
#   所以先把「樓層 → 底色」累積成快取（種子＝backroom-wiki\_data\_floor-colors.md，
#   之後由各場次的 floor-colors.log 自動累積），每一幀就用「這一層的預期底色」當
#   面板掃描的種子色；量到的邊界也按「畫面尺寸＋樓層」快取起來，
#   只有「沒有快取」或「實測色與預期色差 > -ColorThreshold」時才做全圖掃描 → 不會拖慢每一幀。
$script:ColorCachePath = Join-Path $PSScriptRoot 'floor-colors-cache.json'
$script:FloorCache = $null
# 「看起來像樓層標題」的寬鬆樣式：LeveI 13／Leve [ 13／LEVEL 288 這些 OCR 變體都要吃
# （只用來挑行、對配色；Convert-OcrText 與分類規則完全沒動）
$script:RxFloorHead = '(?i)leve[l1i]*\s*[\[\(\|｜·\-「『《〈<{（]{0,2}\s*([0-9]{1,4}(?:\.[0-9]{1,2})?)'

# 唯一來源（Wiki 的實測配色表）；找不到就用空快取，不影響錄影
function Get-FloorColorTablePath {
  # 2026-10-17 搬家後：logger\ 與 wiki\ 同為 D:\BACKROOMS\ 的子目錄。
  $cand = @(
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'wiki\_data\_floor-colors.md')
  )
  foreach ($p in $cand) { if ($p -and (Test-Path -LiteralPath $p)) { return $p } }
  return ''
}

function Convert-HexToRgb {
  param([string]$Hex)
  $h = ([string]$Hex).Trim()
  if ($h -notmatch '^#?([0-9A-Fa-f]{6})$') { return $null }
  $h = [string]$Matches[1]
  return [pscustomobject]@{
    R = [Convert]::ToInt32($h.Substring(0, 2), 16)
    G = [Convert]::ToInt32($h.Substring(2, 2), 16)
    B = [Convert]::ToInt32($h.Substring(4, 2), 16)
  }
}

# 「Level 13 「玻璃之狂」」→ 玻璃之狂
function Get-FloorNameFromText {
  param([string]$Text)
  if (-not $Text) { return '' }
  if ($Text -match '[「『]([^」』]{1,24})[」』]') { return ([string]$Matches[1]).Trim() }
  return ''
}

# 樓層標題 → 快取用的鍵。OCR 變體（LeveI／Leve 595／LEVEL 288／第 N 層）都要吃。
function Get-FloorKeyFromText {
  param([string]$Text)
  if (-not $Text) { return '' }
  $t = [string]$Text
  if ($t -match $script:RxFloorHead) { return ('Level ' + $Matches[1]) }
  if ($t -match '第\s*([0-9]{1,4})\s*[层層]') { return ('Level ' + $Matches[1]) }
  if ($t -match '(?i)(the\s*hub|樞紐|枢纽)') { return 'The Hub' }
  # 有 Level 字樣但數字讀不出來 → 不要退成「名稱」當鍵（會生出怪鍵污染最接近色比對）
  if ($t -match '(?i)leve[l1i]') { return '' }
  return (Get-FloorNameFromText -Text $t)
}

# 新增／更新一筆「樓層 → 底色」。人工實測（Wiki 表）最高優先，永不覆蓋。
function Add-FloorColorEntry {
  param([string]$Id, [string]$Name, [string]$Hex, [int]$R, [int]$G, [int]$B, [string]$Source = '自動偵測', [switch]$NoCount)
  if (-not $script:FloorCache -or -not $Id) { return $null }
  $h = ([string]$Hex).Trim()
  if ($h -notmatch '^#?[0-9A-Fa-f]{6}$') { return $null }
  if ($h[0] -ne '#') { $h = '#' + $h }
  $h = $h.ToUpper()
  $now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  $e = $script:FloorCache.FloorsById[$Id]
  if ($e) {
    $e.last = $now
    if ($Name -and -not $e.name) { $e.name = $Name }
    if (([string]$e.source) -eq '實測') { return $e }
    if (([string]$e.hex) -eq $h) { if (-not $NoCount) { $e.count = [int]$e.count + 1 }; return $e }
    # 同層不同色（換區域／彈窗底色）→ 主色不動，新的記進 alt 讓人看
    $alt = @()
    if ($e.alt) { $alt = @($e.alt) }
    if (($alt -notcontains $h) -and $alt.Count -lt 6) { $alt += $h }
    $e.alt = $alt
    return $e
  }
  $ne = [pscustomobject]@{
    id = $Id; name = $Name; hex = $h; r = [int]$R; g = [int]$G; b = [int]$B
    source = $Source; count = 1; first = $now; last = $now; alt = @()
  }
  $script:FloorCache.Floors.Add($ne)
  $script:FloorCache.FloorsById[$Id] = $ne
  return $ne
}

function Add-FloorColorFromSample {
  param([string]$Hex, [int]$R, [int]$G, [int]$B, [string]$LevelText, [string]$Source = '自動偵測')
  $id = Get-FloorKeyFromText -Text $LevelText
  if (-not $id) { return $null }
  return (Add-FloorColorEntry -Id $id -Name (Get-FloorNameFromText -Text $LevelText) -Hex $Hex -R $R -G $G -B $B -Source $Source)
}

# 從 title 區的 OCR 行裡挑「真正的樓層標題」來對配色快取。
# （Get-LevelName 的規則不動；這裡只是挑一行，選不到才退回它給的名字，
#   而且退回時必須帶「名稱」，免得把「第 N 層…」的場景標題當成樓層配色記進去。）
function Get-FloorTitleText {
  param($Lines, [string]$Fallback = '')
  $arr = @()
  if ($null -ne $Lines) { $arr = [object[]]$Lines }
  $lvl = ''
  foreach ($l in $arr) {
    $t = ([string]$l.Text).Trim()
    if ($t -notmatch $script:RxFloorHead) { continue }
    if ($t -match '[「『]') { return $t }
    if (-not $lvl) { $lvl = $t }
  }
  if ($lvl) { return $lvl }
  if ($Fallback -and (Get-FloorNameFromText -Text $Fallback)) { return $Fallback }
  return ''
}

# 找最接近的樓層色（回傳 $null = 快取裡沒有相近的 → 視為沒收錄過的樓層）
function Find-NearestFloorColor {
  param([int]$R, [int]$G, [int]$B, [int]$MaxDist = 120)
  if (-not $script:FloorCache) { return $null }
  $best = $null; $bd = [double]::MaxValue
  foreach ($e in $script:FloorCache.Floors) {
    $dr = ([int]$e.r) - $R; $dg = ([int]$e.g) - $G; $db = ([int]$e.b) - $B
    $d = [Math]::Sqrt([double]($dr * $dr + $dg * $dg + $db * $db))
    if ($d -lt $bd) { $bd = $d; $best = $e }
  }
  if (-not $best -or $bd -gt $MaxDist) { return $null }
  return [pscustomobject]@{ Id = $best.id; Hex = $best.hex; Name = $best.name; Dist = [int][Math]::Round($bd) }
}

# 文字（樓層標題或場景標題）→ 色碼；先查樓層編號、再退而比名稱
function Get-FloorColorHex {
  param([string]$Text, $Fallback = $null)
  $fb = [string]$Fallback
  if ($fb -match '^#[0-9A-Fa-f]{6}$') {
    # 只有「看起來像樓層標題」的行才吃沿用值；場景行（例：'第 2 层 | 岔路'）不該帶樓層配色
    if (($Text -match $script:RxFloorHead) -or (Get-FloorNameFromText -Text $Text)) { return $fb.ToUpper() }
  }
  if (-not $script:FloorCache) { return '' }
  $id = Get-FloorKeyFromText -Text $Text
  if ($id) {
    $e = $script:FloorCache.FloorsById[$id]
    if ($e) { return ([string]$e.hex).ToUpper() }
  }
  $nm = Get-FloorNameFromText -Text $Text
  if ($nm) {
    foreach ($e in $script:FloorCache.Floors) {
      $en = [string]$e.name
      if ($en -and ($en -eq $nm -or $en.Contains($nm) -or $nm.Contains($en))) { return ([string]$e.hex).ToUpper() }
    }
  }
  return ''
}

# 種子：讀 Wiki 的 _floor-colors.md（表格在 FLOORCOLORS:BEGIN/END 之間）
function Import-FloorColorSeeds {
  param([string]$Path)
  if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return 0 }
  $n = 0; $in = $false
  foreach ($line in [System.IO.File]::ReadLines($Path)) {
    if ($line.IndexOf('FLOORCOLORS:BEGIN') -ge 0) { $in = $true; continue }
    if ($line.IndexOf('FLOORCOLORS:END') -ge 0) { $in = $false; continue }
    if (-not $in) { continue }
    if ($line -notmatch '^\s*\|') { continue }
    $c = @($line.Trim().Trim('|') -split '\|')
    if ($c.Count -lt 8) { continue }
    $hex = ([string]$c[2]) -replace '[`\s]', ''
    if ($hex -notmatch '^#[0-9A-Fa-f]{6}$') { continue }
    $st = ([string]$c[7]).Trim()
    if ($st.StartsWith('衝突')) { continue }
    $id = Get-FloorKeyFromText -Text ([string]$c[0])
    if (-not $id) { $id = ([string]$c[1]).Trim() }
    if (-not $id) { continue }
    $r = 0; $g = 0; $b = 0
    if (([string]$c[3]) -match 'rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)') {
      $r = [int]$Matches[1]; $g = [int]$Matches[2]; $b = [int]$Matches[3]
    } else {
      $rgb = Convert-HexToRgb $hex
      if ($rgb) { $r = $rgb.R; $g = $rgb.G; $b = $rgb.B }
    }
    $e = Add-FloorColorEntry -Id $id -Name ([string]$c[1]).Trim() -Hex $hex -R $r -G $g -B $b -Source ([string]$c[6]).Trim() -NoCount
    if ($e) { $e.first = ([string]$c[8]).Trim(); $n++ }
  }
  return $n
}

# 累積：掃 logs\*\floor-colors.log 的「樓層名 → 色碼」
function Update-FloorCacheFromLogs {
  param([string]$Root)
  $n = 0
  if (-not $script:FloorCache -or -not $Root -or -not (Test-Path -LiteralPath $Root)) { return 0 }
  $rx = [regex]'^\s*\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\s+(#[0-9A-Fa-f]{6})\s+rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)\s*(.*?)\s*幀#\d+\s*$'
  foreach ($d in @(Get-ChildItem -Path $Root -Directory -Filter 'backroom-*' -ErrorAction SilentlyContinue)) {
    $lg = Join-Path $d.FullName 'floor-colors.log'
    if (-not (Test-Path -LiteralPath $lg)) { continue }
    foreach ($line in [System.IO.File]::ReadLines($lg)) {
      if ($line.IndexOf('#') -lt 0) { continue }
      $m = $rx.Match($line)
      if (-not $m.Success) { continue }
      $txt = ([string]$m.Groups[5].Value).Trim()
      # 樓層名要有「名稱」或明確的 Level N；「第 N 層…」多半是場景標題（Get-LevelName 抓到的），
      # 記成樓層配色會污染「最接近的顏色」比對 → 跳過
      if (($txt -notmatch '[「『]') -and ($txt -notmatch $script:RxFloorHead)) { continue }
      $id = Get-FloorKeyFromText -Text $txt
      if (-not $id) { continue }
      $e = Add-FloorColorEntry -Id $id -Name (Get-FloorNameFromText -Text $txt) -Hex ([string]$m.Groups[1].Value) `
        -R ([int]$m.Groups[2].Value) -G ([int]$m.Groups[3].Value) -B ([int]$m.Groups[4].Value) -Source '自動偵測' -NoCount
      if ($e) { $e.count = [int]$e.count + 1; $n++ }
    }
  }
  return $n
}

# 邊界快取：鍵＝畫面尺寸＋樓層（或沒收錄的樓層就用色碼）
function Set-CachedRegion {
  param([string]$Key, [string]$Main, [string]$Opts, [string]$Hex, [string]$Floor)
  if (-not $script:FloorCache -or -not $Key) { return $null }
  $now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  $e = $script:FloorCache.RegionsByKey[$Key]
  if ($e) {
    if ($Main) { $e.main = $Main }
    if ($Opts) { $e.opts = $Opts }
    $e.hex = $Hex; $e.floor = $Floor; $e.last = $now
    return $e
  }
  $ne = [pscustomobject]@{ key = $Key; floor = $Floor; hex = $Hex; main = $Main; opts = $Opts; first = $now; last = $now }
  $script:FloorCache.Regions.Add($ne)
  $script:FloorCache.RegionsByKey[$Key] = $ne
  return $ne
}

function Get-CachedRegion {
  param([string]$Key)
  if (-not $script:FloorCache -or -not $Key) { return $null }
  return $script:FloorCache.RegionsByKey[$Key]
}

function Import-FloorColorCacheFile {
  if (-not (Test-Path -LiteralPath $script:ColorCachePath)) { return 0 }
  $j = $null
  try { $j = Get-Content -LiteralPath $script:ColorCachePath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return 0 }
  if (-not $j) { return 0 }
  $n = 0
  foreach ($f in @($j.floors)) {
    if (-not $f -or -not $f.id) { continue }
    $e = Add-FloorColorEntry -Id ([string]$f.id) -Name ([string]$f.name) -Hex ([string]$f.hex) `
      -R ([int]$f.r) -G ([int]$f.g) -B ([int]$f.b) -Source ([string]$f.source) -NoCount
    if ($e) {
      if ($null -ne $f.count) { $e.count = [int]$f.count }
      if ($f.first) { $e.first = [string]$f.first }
      if ($f.alt) { $e.alt = @($f.alt) }
      $n++
    }
  }
  foreach ($r in @($j.regions)) {
    if (-not $r -or -not $r.key) { continue }
    $e = Set-CachedRegion -Key ([string]$r.key) -Main ([string]$r.main) -Opts ([string]$r.opts) -Hex ([string]$r.hex) -Floor ([string]$r.floor)
    if ($e -and $r.first) { $e.first = [string]$r.first }
  }
  return $n
}

function Save-FloorColorCache {
  if (-not $script:FloorCache) { return $false }
  try {
    $o = [ordered]@{
      version     = 1
      updated     = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
      sourceTable = (Get-FloorColorTablePath)
      note        = 'floors=樓層→介面底色（來源=實測的永不覆蓋）；regions=畫面尺寸+樓層→量到的面板邊界'
      floors      = [object[]]$script:FloorCache.Floors
      regions     = [object[]]$script:FloorCache.Regions
    }
    [System.IO.File]::WriteAllText($script:ColorCachePath, ($o | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding $true))
    return $true
  } catch { return $false }
}

# 啟動時：種子（Wiki 表）→ 快取檔 → 各場次 floor-colors.log，三層合併
function Initialize-FloorColors {
  param([string]$Root)
  $script:FloorCache = @{
    Floors       = New-Object System.Collections.Generic.List[object]
    Regions      = New-Object System.Collections.Generic.List[object]
    FloorsById   = @{}
    RegionsByKey = @{}
  }
  $tbl = Get-FloorColorTablePath
  $seed = Import-FloorColorSeeds -Path $tbl
  $fromFile = Import-FloorColorCacheFile
  $fromLogs = Update-FloorCacheFromLogs -Root $Root
  Save-FloorColorCache
  return [pscustomobject]@{
    Seeds = $seed; FromCache = $fromFile; FromLogs = $fromLogs
    Floors = $script:FloorCache.Floors.Count; Regions = $script:FloorCache.Regions.Count
    Table = $tbl; Path = $script:ColorCachePath
  }
}

# 量到的座標是否合理（避免把奇怪的掃描結果套上去）
function Test-RegionRectSane {
  param([string]$Spec, [int]$W, [int]$H)
  if (-not $Spec) { return $false }
  $p = @(([string]$Spec -split '[,\s]+') | Where-Object { $_ -ne '' })
  if ($p.Count -ne 4) { return $false }
  $x0 = [int]($p[0]); $x1 = [int]($p[1]); $y0 = [int]($p[2]); $y1 = [int]($p[3])
  if ($x0 -lt 0 -or $y0 -lt 0 -or $x1 -gt $W -or $y1 -gt $H) { return $false }
  if (($x1 - $x0) -lt 200 -or ($y1 - $y0) -lt 40) { return $false }
  return $true
}

# 把「render 區座標」的邊界換算回校正基準（$script:BaseW/$script:BaseH）的等效字串。
# $Rect 是「目前參數」在 render 區的位置；$Rect2 是量到的邊界（-XCredible/-YCredible 決定要不要採用）。
function ConvertTo-BaseRegion {
  param([System.Drawing.Rectangle]$Rect, [System.Drawing.Rectangle]$Rect2, [switch]$XCredible, [switch]$YCredible)
  if (-not $Rect) { return '' }
  $kx = $script:BaseW / [double][Math]::Max(1, $Rect.Width)
  $ky = $script:BaseH / [double][Math]::Max(1, $Rect.Height)
  $x0 = $Rect.Left; $x1 = $Rect.Right; $y0 = $Rect.Top; $y1 = $Rect.Bottom
  if ($Rect2 -and $XCredible) { $x0 = $Rect2.Left; $x1 = $Rect2.Right }
  if ($Rect2 -and $YCredible) { $y0 = $Rect2.Top; $y1 = $Rect2.Bottom }
  return ('' + [int][Math]::Round($x0 * $kx) + ',' + [int][Math]::Round($x1 * $kx) + ',' +
    [int][Math]::Round($y0 * $ky) + ',' + [int][Math]::Round($y1 * $ky))
}

# 量到的邊界 vs 目前參數（都在 render 區座標）。只取「比提示窄」的方向，
# 更寬可能是同色的隔壁面板被算進來。
function Compare-RegionSpec {
  param([string]$Hint, [string]$Meas)
  if (-not $Hint -or -not $Meas) { return $null }
  $c = @(([string]$Hint -split '[,\s]+') | Where-Object { $_ -ne '' })
  $m = @(([string]$Meas -split '[,\s]+') | Where-Object { $_ -ne '' })
  if ($c.Count -ne 4 -or $m.Count -ne 4) { return $null }
  $mm = @()
  foreach ($v in $m) { $mm += [int]$v }
  if ($mm[0] -lt [int]($c[0])) { $mm[0] = [int]($c[0]) }
  if ($mm[1] -gt [int]($c[1])) { $mm[1] = [int]($c[1]) }
  if ($mm[2] -lt [int]($c[2])) { $mm[2] = [int]($c[2]) }
  if ($mm[3] -gt [int]($c[3])) { $mm[3] = [int]($c[3]) }
  $diff = 0
  for ($i = 0; $i -lt 4; $i++) { $dd = [Math]::Abs($mm[$i] - [int]($c[$i])); if ($dd -gt $diff) { $diff = $dd } }
  return [pscustomobject]@{ Diff = $diff; Clamped = ($mm -join ','); Cur = $Hint; Meas = $Meas }
}

# 每一幀（OCR 之前）決定這一幀要用哪一組邊界：
#   1) 用 main 區取樣到的底色 → 快取比對「最接近哪一層」→ 拿該層預期底色當掃描種子色
#   2) 尺寸＋樓層的邊界快取命中就沿用 → 不做全圖掃描（不拖慢每一幀）
#   3) 只有「沒快取」或「實測色與預期色差 > -ColorThreshold」才重掃（同一層一次、至少間隔 4 秒）
#   4) 重掃後與參數差 > 10px → 寫警告到 logger.log 並提議新座標；
#      -AutoRegion 開啟時「本幀」直接用新邊界（只存在記憶體，不改參數、不改檔案）
function Resolve-FrameRegion {
  param($S, $Bmp, $Bg, [int]$W, [int]$H, [string]$FillHex = '')

  $r = [pscustomobject]@{ Main = $RegionMain; Opts = $RegionOpts; Hex = ''; Floor = ''; Scanned = $false; Note = '' }
  if (-not $Bg -or -not $script:FloorCache) { return $r }
  $r.Hex = [string]$Bg.Hex

  $match = Find-NearestFloorColor -R ([int]$Bg.R) -G ([int]$Bg.G) -B ([int]$Bg.B)
  $dev = -1; $id = ''; $seedHex = ''
  if ($match) { $dev = [int]$match.Dist; $id = [string]$match.Id; $seedHex = [string]$match.Hex }
  # 種子色只在「配色真的對得上」時才用；對不上就讓 Invoke-PanelScan 自己取樣（錯的種子比取樣點更糟）
  if ($dev -lt 0 -or $dev -gt $ColorThreshold) { $seedHex = '' }
  # 快取對不上時，用「main 區主色」當種子（單點取樣可能壓在世界貼圖或文字上）
  if (-not $seedHex -and $FillHex -and $FillHex -match '^#?[0-9A-Fa-f]{6}$') { $seedHex = $FillHex }
  if (-not $id) {
    # 沒收錄過的底色 → 用「量化後」的色碼當鍵（同一色才不會因為 JPEG 噪訊每幀換鍵、每幀重掃）
    $id = ('color-{0:X2}{1:X2}{2:X2}' -f ([int]([Math]::Floor($Bg.R / 8) * 8)), ([int]([Math]::Floor($Bg.G / 8) * 8)), ([int]([Math]::Floor($Bg.B / 8) * 8)))
  }
  $r.Floor = $id
  $key = ('' + $W + 'x' + $H + '|' + $id)
  $cached = Get-CachedRegion -Key $key
  # 提示一定要換成「這個 render 尺寸下的座標」：拿著校正基準的絕對值去掃別尺寸的畫面，
  # 掃描會落空，然後靜靜沿用參數 → 校正永遠顯示「差 0px」（假陽性）。
  $hMainRect = ConvertTo-RegionRect -Spec $RegionMain -CropW $W -CropH $H -Name 'main'
  $hOptsRect = ConvertTo-RegionRect -Spec $RegionOpts -CropW $W -CropH $H -Name 'opts'
  $hMain = Format-RegionRect -Rect $hMainRect
  $hOpts = Format-RegionRect -Rect $hOptsRect

  $needScan = ((-not $cached) -or ($dev -lt 0) -or ($dev -gt $ColorThreshold))
  if ($needScan -and $S.LastScanAt -and (((Get-Date) - $S.LastScanAt).TotalSeconds -lt 4)) { $needScan = $false }
  if ($needScan -and $S.ScannedKeys -and $S.ScannedKeys.ContainsKey($key)) { $needScan = $false }

  if ($needScan) {
    if (-not $S.ScannedKeys) { $S | Add-Member -NotePropertyName ScannedKeys -NotePropertyValue @{} -Force }
    $S.ScannedKeys[$key] = $true
    $S.LastScanAt = Get-Date
    $S.Scans = [int]$S.Scans + 1
    $rsB = $null; $roB = $null
    try {
      if ($hMain) { $rsB = Invoke-PanelScan -Bmp $Bmp -Name 'main' -Hint $hMain -BoundX -SeedHex $seedHex }
      if ($hOpts) { $roB = Invoke-PanelScan -Bmp $Bmp -Name 'opts' -Hint $hOpts -BoundX -SeedHex $seedHex }
    } catch { $rsB = $null; $roB = $null }
    $mmMain = ConvertTo-RegionMeasurement -R $rsB -Hint $hMainRect
    $mmOpts = ConvertTo-RegionMeasurement -R $roB -Hint $hOptsRect
    $mMain = $mmMain.Text
    $mOpts = ''
    if ($mmOpts -and $mmOpts.Text) { $mOpts = $mmOpts.Text }
    if ($mMain -or $mOpts) {
      $cached = Set-CachedRegion -Key $key -Main $mMain -Opts $mOpts -Hex $r.Hex -Floor $id
      [void](Save-FloorColorCache)
      $r.Scanned = $true
      $S.LastMeasured = [pscustomobject]@{ Main = $mMain; Opts = $mOpts; Hex = $r.Hex; Floor = $id; At = (Get-Date).ToString('HH:mm:ss') }
      Write-Log $S ('自動校正掃描：' + $id + '（實測 ' + $r.Hex + $(if ($dev -lt 0) { '，快取無相近色' } else { '，預期 ' + $seedHex + ' 色距 ' + $dev }) + '）→ main=' + $(if ($mMain) { $mMain } else { '—' }) + ' opts=' + $(if ($mOpts) { $mOpts } else { '—' }))
      $cm = Compare-RegionSpec -Hint $hMain -Meas $mMain
      $co = $null; if ($mOpts) { $co = Compare-RegionSpec -Hint $hOpts -Meas $mOpts }
      $worst = 0
      if ($cm) { $worst = [int]$cm.Diff }
      if ($co -and ([int]$co.Diff -gt $worst)) { $worst = [int]$co.Diff }
      if ($worst -gt 10) {
        if (-not $S.CalWarned) { $S | Add-Member -NotePropertyName CalWarned -NotePropertyValue @{} -Force }
        if (-not $S.CalWarned.ContainsKey($key)) {
          $S.CalWarned[$key] = $true
          $pMain = $mMain
          $pOpts = $mOpts
          $msg = '⚠ 自動校正：' + $id + ' 量到的邊界與目前參數差 ' + $worst + 'px（main=' + $(if ($mMain) { $mMain } else { '—' }) + '、opts=' + $(if ($mOpts) { $mOpts } else { '—' }) + '）'
          Write-Log $S $msg
          if (-not $Quiet) { Write-Host ('  ' + $msg) -ForegroundColor Yellow }
          Write-Log $S ('   建議改參數：-RegionMain ''' + $pMain + ''' -RegionOpts ''' + $pOpts + '''' + $(if ($AutoRegion) { '（-AutoRegion 已開啟：本幀直接用新邊界）' } else { '（加 -AutoRegion 可讓之後每一幀自動套用）' }))
        }
      }
    }
  }

  # 本幀要不要改用快取裡量到的邊界（只有 -AutoRegion 會真的改）
  $cacheMain = ''; $cacheOpts = ''
  if ($cached) {
    if ($cached.main) { $cacheMain = [string]$cached.main }
    if ($cached.opts) { $cacheOpts = [string]$cached.opts }
  }
  if ($AutoRegion) {
    $cm2 = Compare-RegionSpec -Hint $hMain -Meas $cacheMain
    if ($cm2 -and ([int]$cm2.Diff -gt 10) -and (Test-RegionRectSane -Spec $cacheMain -W $W -H $H)) {
      $r.Main = $cacheMain
      $S.AutoRegionUses = [int]$S.AutoRegionUses + 1
      $r.Note = 'main→' + $cacheMain
    }
    $co2 = $null; if ($cacheOpts) { $co2 = Compare-RegionSpec -Hint $hOpts -Meas $cacheOpts }
    if ($co2 -and ([int]$co2.Diff -gt 10) -and (Test-RegionRectSane -Spec $cacheOpts -W $W -H $H)) {
      $r.Opts = $cacheOpts
      $S.AutoRegionUses = [int]$S.AutoRegionUses + 1
      $r.Note = ($r.Note + ' opts→' + $cacheOpts).Trim()
    }
    if ($r.Note -and -not $S.AutoRegionLogged) {
      $S.AutoRegionLogged = $true
      Write-Log $S ('-AutoRegion 生效：' + $id + '（' + $r.Hex + '）本幀改用 ' + $r.Note + '；只影響這一幀，參數與檔案都沒改')
      if (-not $Quiet) { Write-Host ('  -AutoRegion：本幀改用 ' + $r.Note) -ForegroundColor Magenta }
    }
  }
  return $r
}

# ============================ 舊場次轉檔 / 匯出 ============================
function Test-SessionLive {
  param([string]$Dir)
  $lg = Join-Path $Dir 'logger.log'
  if (-not (Test-Path -LiteralPath $lg)) { return $false }
  if (((Get-Date) - (Get-Item -LiteralPath $lg).LastWriteTime).TotalSeconds -ge 15) { return $false }
  $last = ''
  try {
    $lines = [System.IO.File]::ReadAllLines($lg)
    for ($i = $lines.Count - 1; $i -ge 0; $i--) { if ($lines[$i].Trim()) { $last = $lines[$i]; break } }
  } catch { return $true }
  if ($last -match '結束。') { return $false }
  return $true
}

# ---- 匯出水位線（export-state.json）----------------------------------------
# 目的：**預設永遠不會重匯**。水位線壞掉／不見時，改成從既有的
# backroom-update-*.md 回推「已經匯出過哪些場次」，推不出來就「不匯出並警告」，
# 絕不靜默把所有場次重匯一遍（那會把已處理過的內容再餵一次給 AI）。

function Read-ExportStateFile {
  # 回傳 @{ Ok = <bool>; State = <psobject>; Reason = <string> }；Ok=$false 代表壞掉／不存在
  param([string]$Root)
  $r = @{ Ok = $false; State = $null; Reason = '' }
  $f = Join-Path $Root 'export-state.json'
  if (-not (Test-Path -LiteralPath $f)) { $r.Reason = 'export-state.json 不存在'; return $r }
  try {
    $j = Get-Content -LiteralPath $f -Raw -Encoding UTF8 | ConvertFrom-Json
  } catch { $r.Reason = 'export-state.json 不是有效 JSON（可能寫到一半就中斷）'; return $r }
  if (-not $j) { $r.Reason = 'export-state.json 內容是空的'; return $r }
  $ver = 1
  if ($j.PSObject.Properties['v']) { $ver = [int]$j.v }
  if ($ver -ge 2) {
    if (-not $j.PSObject.Properties['lastSession'] -or -not $j.PSObject.Properties['exportedSessions']) {
      $r.Reason = 'export-state.json（v2）缺少必要欄位（lastSession / exportedSessions）'; return $r
    }
    $n = 0
    if ($j.PSObject.Properties['sessionCount']) { $n = [int]($j.sessionCount) }
    $listed = @($j.exportedSessions | Where-Object { $_ })
    if ($listed.Count -ne $n) {
      $r.Reason = ('export-state.json 校驗失敗：sessionCount=' + $n + ' 但清單有 ' + $listed.Count + ' 筆'); return $r
    }
    if ([string]::IsNullOrWhiteSpace([string]$j.lastSession)) { $r.Reason = 'export-state.json 的 lastSession 是空的'; return $r }
    $r.Ok = $true; $r.State = $j; return $r
  }
  # v1（舊版）：只有 lastSession / lastCount，可能是舊寫法留下的，照樣能用
  if (-not $j.PSObject.Properties['lastSession']) { $r.Reason = 'export-state.json 缺少 lastSession'; return $r }
  if ([string]::IsNullOrWhiteSpace([string]$j.lastSession)) { $r.Reason = 'export-state.json 的 lastSession 是空的'; return $r }
  $r.Ok = $true; $r.State = $j; return $r
}

function Write-ExportStateFile {
  # 先寫 .tmp 再改名（atomic）→ 不會留下半截的 JSON
  param([string]$Root, $State)
  $p = Join-Path $Root 'export-state.json'
  $tmp = $p + '.tmp'
  $enc = New-Object System.Text.UTF8Encoding $true
  [System.IO.File]::WriteAllText($tmp, ($State | ConvertTo-Json -Compress -Depth 6), $enc)
  if (Test-Path -LiteralPath $p) { try { (Get-Item -LiteralPath $p -Force).Attributes = 'Normal' } catch { } }
  try {
    [System.IO.File]::Copy($tmp, $p, $true)
  } catch {
    try { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue } catch { }
    [System.IO.File]::Copy($tmp, $p, $true)
  }
  try { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } catch { }
  try { (Get-Item -LiteralPath $p -Force).Attributes = 'Hidden' } catch { }
}

function Get-DirEventCount {
  param([string]$Dir)
  $j = Join-Path $Dir 'wiki.jsonl'
  if (-not (Test-Path -LiteralPath $j)) { return 0 }
  $n = 0
  foreach ($line in [System.IO.File]::ReadLines($j)) { if ($line.Trim()) { $n++ } }
  return $n
}

function Get-ExportedSessionsFromUpdates {
  # 掃既有的 backroom-update-*.md 回推已匯出的場次：
  #   Sessions = 全部已匯出場次；Last = 最新那一場（＝水位線用的 lastSession）
  #   來源欄位＝檔頭「場次：N」、「內容時間：起～迄」、內文「### 場次 <name>」
  param([string]$Root)
  $files = @(Get-ChildItem -Path $Root -File -Filter 'backroom-update-*.md' -ErrorAction SilentlyContinue | Sort-Object Name)
  $names = New-Object System.Collections.Generic.List[string]
  $lastAt = ''; $lastN = 0; $parts = 0; $lastSess = ''
  foreach ($f in $files) {
    $txt = ''
    try { $txt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($f.FullName)) } catch { continue }
    $cnt = 0
    if ($txt -match '場次：\s*(\d+)') { $cnt = [int]($Matches[1]) }
    # 檔頭的內容時間範圍：起點＝這一包最舊的場次（就算只有一場也一定有）
    $rngStart = ''; $rngEnd = ''
    if ($txt -match '內容時間：\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s*～\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') {
      $rngStart = $Matches[1]; $rngEnd = $Matches[2]
    }
    $found2 = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($txt, '(?m)^###\s+場次\s+(backroom-\d{8}-\d{6})')) { $found2.Add($m.Groups[1].Value) }
    $un = @($found2 | Sort-Object -Unique)
    # ⚠ 只有一場時匯出檔不會寫「### 場次」標頭（原本的產生器就是這樣），
    #    所以單場的 update 檔以前完全回推不出來 → 改用「內容時間」的起點回推。
    if ($un.Count -eq 0 -and $rngStart) {
      $cand = 'backroom-' + $rngStart.Replace('-', '').Replace(':', '').Replace(' ', '-')
      if ($cand -match '^backroom-\d{8}-\d{6}$') { $un = @($cand) }
    }
    if ($un.Count -gt 0 -and ($cnt -eq 0 -or $un.Count -eq $cnt)) {
      foreach ($nm in $un) { if (-not $names.Contains($nm)) { $names.Add($nm) } }
      $parts++
    }
    $at = $f.CreationTime.ToString('yyyy-MM-dd HH:mm:ss')
    if ($txt -match '產生時間：\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') { $at = $Matches[1] }
    if ($at -gt $lastAt) { $lastAt = $at }
    if ($un.Count -gt $lastN) { $lastN = $un.Count }
    # 這一包最「新」的場次＝內容時間的終點（單場檔沒有標頭時，就是那一場本身）
    if ($rngEnd) {
      $candEnd = 'backroom-' + $rngEnd.Replace('-', '').Replace(':', '').Replace(' ', '-')
      if ($candEnd -match '^backroom-\d{8}-\d{6}$' -and $candEnd -gt $lastSess) { $lastSess = $candEnd }
    }
    if ($un.Count -gt 0) {
      $maxUn = [string](@($un | Sort-Object)[$un.Count - 1])
      if ($maxUn -gt $lastSess) { $lastSess = $maxUn }
    }
  }
  $arr = @($names | Sort-Object)
  return [pscustomobject]@{
    Sessions = $arr
    Count    = $arr.Count
    Files    = $files.Count
    Parts    = $parts
    LastAt   = $lastAt
    MaxN     = $lastN
    Last     = $lastSess
  }
}

function Get-ExportedSessionsFromDirs {
  # 既有 update 檔推不出來時，改用「曾經被匯出過、但還留在 logs 底下」的場次資料夾，
  # 以「紀錄不中斷」為判斷：每個資料夾的事件數和它下一個資料夾的起點相同 → 它一定是被匯出後留下的尾巴。
  param([string]$OutDir)
  $dirs = @(Get-ChildItem -Path $OutDir -Directory -Filter 'backroom-*' -ErrorAction SilentlyContinue | Sort-Object Name)
  $names = New-Object System.Collections.Generic.List[string]
  for ($i = 0; $i -lt $dirs.Count - 1; $i++) {
    $a = Get-DirEventCount -Dir $dirs[$i].FullName
    $b = Get-DirEventCount -Dir $dirs[$i + 1].FullName
    if ($a -gt 0 -and $a -eq $b) { $names.Add($dirs[$i].Name) }
  }
  $arr = @($names | Sort-Object)
  return [pscustomobject]@{ Sessions = $arr; Count = $arr.Count }
}

# 給 AI／人看的「最新匯出」指標檔：只讀這一份就知道該匯入什麼、不要翻舊的 6 MB
function Write-ExportManifest {
  param(
    [string]$Root,
    $State,
    [string]$ReasonText,
    [string]$SummaryName = '',
    [string]$UpdateName = '',
    [string]$SessionList = '',
    [string]$RangeText = '',
    [string]$LastAt = '',
    [long]$EventTotal = 0,
    [switch]$NoContent
  )

  $stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  $out = New-Object System.Text.StringBuilder
  [void]$out.AppendLine('# 最新匯出（記錄器自動產生 — 給 AI 的入口指標）')
  [void]$out.AppendLine('')
  [void]$out.AppendLine('> ⚠ 這份檔案由 `backroom-logger.ps1` 的匯出功能自動維護，**不要手改**。')
  [void]$out.AppendLine('')
  [void]$out.AppendLine('- 最後檢查（每次按「收工」都會更新）：' + $stamp)
  [void]$out.AppendLine('- 上次實際匯出：' + $LastAt)

  $listed = @($State.exportedSessions | Where-Object { $_ })
  $nListed = $listed.Count
  $nTotal = 0
  if ($State.PSObject.Properties['sessionCount']) { $nTotal = [int]($State.sessionCount) }
  $nEvt = 0
  if ($State.PSObject.Properties['eventTotal']) { $nEvt = [int]($State.eventTotal) }

  if ($NoContent) {
    [void]$out.AppendLine('- 狀態：**沒有新內容** — ' + $ReasonText)
    [void]$out.AppendLine('')
    [void]$out.AppendLine('## ⚠ 要匯入 Wiki 請只讀這一份 summary')
    [void]$out.AppendLine('')
    [void]$out.AppendLine('- **本次沒有產生任何新檔案**，不要讀 `' + $Root + '` 或 `來源紀錄\` 底下的舊檔。')
    [void]$out.AppendLine('- 沒有新東西要匯入 → **什麼都不用做**（不要為了「確認」去開那幾個 MB 的舊 update）。')
  } elseif ($State.lastSession -and $SummaryName) {
    [void]$out.AppendLine('')
    [void]$out.AppendLine('## ⚠ 要匯入 Wiki 請只讀這一份 summary')
    [void]$out.AppendLine('')
    [void]$out.AppendLine('- ✅ 只讀：`' + (Join-Path $Root $SummaryName) + '`')
    [void]$out.AppendLine('- 完整出處（**不要整份讀**，只在需要查某句的「行 N」時才開）：`' + (Join-Path $Root $UpdateName) + '`')
    [void]$out.AppendLine('- 這一包涵蓋：' + $SessionList + '（' + $RangeText + '）')
    [void]$out.AppendLine('- 匯出時間：' + $LastAt + '　場次數：' + $nTotal + '　事件數：' + $nEvt)
    [void]$out.AppendLine('')
    [void]$out.AppendLine('## 上一次匯出的是哪個檔（避免重複處理）')
    [void]$out.AppendLine('')
    $ps = ''; $pn = 0
    if ($State.PSObject.Properties['prevSummary']) { $ps = [string]$State.prevSummary }
    if ($State.PSObject.Properties['prevSessionCount']) { $pn = [int]($State.prevSessionCount) }
    if ($ps) { [void]$out.AppendLine('- `' + $ps + '`（' + $pn + ' 個場次）　← **已經處理過，不要再讀**') }
    else { [void]$out.AppendLine('- （這是第一次匯出，沒有更早的檔）') }
    [void]$out.AppendLine('- ⚠ 本目錄底下所有 `backroom-summary-*.md` / `backroom-update-*.md` **除了上面那份，都已經處理過**；')
    [void]$out.AppendLine('  除非要查舊出處行號，**不要讀**（5 個舊 update 約 6 MB ≈ 150 萬 token，白花錢）。')
  } elseif ($ReasonText) {
    [void]$out.AppendLine('- 狀態：**這次沒有匯出任何內容** — ' + $ReasonText)
    [void]$out.AppendLine('')
    [void]$out.AppendLine('## ⚠ 要匯入 Wiki 請只讀這一份 summary')
    [void]$out.AppendLine('')
    [void]$out.AppendLine('- **沒有新檔案產生**；請不要讀 `' + $Root + '` 底下的舊 summary／update。')
    [void]$out.AppendLine('- 上一次的匯出內容可看 `' + $Root + '` 底下最新的 `backroom-summary-*.md`（若已處理過就別再讀）。')
  } else {
    [void]$out.AppendLine('- 狀態：還沒有匯出紀錄')
    [void]$out.AppendLine('')
    [void]$out.AppendLine('## ⚠ 要匯入 Wiki 請只讀這一份 summary')
    [void]$out.AppendLine('')
    [void]$out.AppendLine('- 目前沒有可讀的匯出檔；請先按記錄器的「收工」產生。')
  }

  [void]$out.AppendLine('')
  [void]$out.AppendLine('## 累計匯出過的場次（' + $nTotal + '）')
  [void]$out.AppendLine('')
  if ($nTotal -eq 0) {
    [void]$out.AppendLine('- （無）')
  } else {
    $sorted = @($listed | Sort-Object)
    foreach ($nm in $sorted) { [void]$out.AppendLine('- ' + $nm) }
    if ($nTotal -gt $nListed) { [void]$out.AppendLine('- …另有 ' + ($nTotal - $nListed) + ' 個較早場次（清單從 v2 水位線開始累計）') }
  }
  [void]$out.AppendLine('')
  [void]$out.AppendLine('## 不要讀')
  [void]$out.AppendLine('')
  [void]$out.AppendLine('- `' + $Root + '` 底下所有 `backroom-update-*.md`（除了本檔標示的那一份）')
  [void]$out.AppendLine('- `' + (Join-Path (Split-Path $Root -Parent) 'wiki\来源纪录\') + '` 底下的任何檔案（那是歷史備份，只在查舊出處行號時開）')
  [void]$out.AppendLine('- ⚠ `backroom-update-*.md` / `backroom-summary-*.md` **不可以刪**（AGENTS.md §3b：wiki 出處行號的唯一依據）')

  $enc = New-Object System.Text.UTF8Encoding $true
  [System.IO.File]::WriteAllText((Join-Path $Root '最新匯出.md'), $out.ToString(), $enc)
}

function Update-ExportManifestCheckTime {
  # 沒有新內容時：只把「最新匯出.md」的檢查時間往前推，不新增／不改其他任何檔案。
  # 逐行改寫（不是重寫整份），manifest 才不會每次都變、也才對得上水位線。
  param([string]$Root, [string]$Now)
  $mp = Join-Path $Root '最新匯出.md'
  if (-not (Test-Path -LiteralPath $mp)) { return $false }
  $txt = ''
  try { $txt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($mp)).TrimStart([char]0xFEFF) } catch { return $false }
  if (-not $txt) { return $false }
  $line = ('- 最後檢查（每次按「收工」都會更新）：' + $Now)
  $re = New-Object System.Text.RegularExpressions.Regex('(?m)^- 最後檢查（每次按「收工」都會更新）：.*$')
  if ($re.IsMatch($txt)) { $txt = $re.Replace($txt, $line, 1) }
  [System.IO.File]::WriteAllText($mp, $txt, (New-Object System.Text.UTF8Encoding $true))
  return $true
}

function Set-ExportState {
  # 每次匯出都寫水位線（atomic）＋ 更新「最新匯出.md」指標；NoContent 時只更新檢查時間
  param(
    [string]$Root,
    [string]$Session,
    [int]$Count,
    $Sessions = $null,
    [long]$EventTotal = 0,
    [string]$SummaryName = '',
    [string]$UpdateName = '',
    [string]$SessionList = '',
    [string]$RangeText = '',
    [switch]$NoContent,
    [string]$ReasonText = ''
  )

  $now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  $prevSummary = ''; $prevCount = 0
  $list = New-Object System.Collections.Generic.List[string]
  $prevTotal = 0
  $prevEvt = 0

  $rd = Read-ExportStateFile -Root $Root
  if ($rd.Ok) {
    $old = $rd.State
    $oList = @()
    if ($old.PSObject.Properties['exportedSessions']) { $oList = @($old.exportedSessions | Where-Object { $_ }) }
    foreach ($nm in $oList) { if (-not $list.Contains($nm)) { $list.Add($nm) } }
    if ($old.PSObject.Properties['lastSummary']) { $prevSummary = [string]$old.lastSummary }
    if ($old.PSObject.Properties['sessionCount']) { $prevTotal = [int]($old.sessionCount) }
    if ($old.PSObject.Properties['eventTotal']) { $prevEvt = [int]($old.eventTotal) }
    if (-not $prevSummary -and $old.PSObject.Properties['lastUpdate']) { $prevSummary = [string]$old.lastUpdate }
    if (-not $Session) { $Session = [string]$old.lastSession }
    if ($Count -le 0) { $Count = [int]$old.lastCount }
  }
  $prevCount = $prevTotal

  if (-not $NoContent) {
    if ($Sessions) {
      $newList = @($Sessions)
      foreach ($nm in $newList) { if (-not $list.Contains($nm)) { $list.Add($nm) } }
    }
    if (-not $Sessions -or @($Sessions).Count -eq 0) {
      if ($Session -and -not $list.Contains($Session)) { $list.Add($Session) }
    }
    if ($EventTotal -le 0) { $EventTotal = $prevEvt }
  }

  if ($NoContent) {
    # 沒有新內容：不新增任何匯出檔、也不重寫水位線（避免把已累計的清單打薄），
    # 只把「最新匯出.md」的檢查時間往前推，讓人看得出「機器還在動、而且確認過沒有新東西」。
    $mp = Join-Path $Root '最新匯出.md'
    if (Test-Path -LiteralPath $mp) {
      $null = Update-ExportManifestCheckTime -Root $Root -Now $now
    } else {
      Write-ExportManifest -Root $Root -State ([pscustomobject]@{ exportedSessions = @() }) -NoContent -ReasonText $ReasonText
    }
    return [pscustomobject]@{ Manifest = $mp; NoContent = $true }
  }

  $arr = @($list | Sort-Object)
  $maxCount = $Count
  if ($Sessions) {
    foreach ($d in @(Get-ChildItem -Path (Join-Path $Root 'logs') -Directory -Filter 'backroom-*' -ErrorAction SilentlyContinue)) {
      $n = Get-DirEventCount -Dir $d.FullName
      if ($n -gt $maxCount) { $maxCount = $n }
    }
  }
  $st = [pscustomobject]@{
    v                = 2
    lastSession      = $Session
    lastCount        = $maxCount
    exportedSessions = $arr
    sessionCount     = $arr.Count
    eventTotal       = $EventTotal
    lastSummary      = $SummaryName
    lastUpdate       = $UpdateName
    prevSummary      = $prevSummary
    prevSessionCount = $prevCount
    lastCheckAt      = $now
    exportedAt       = $now
    checksum         = ([guid]::NewGuid().ToString('N'))
  }
  Write-ExportStateFile -Root $Root -State $st
  $reason = ''
  if (-not $Sessions -and -not $NoContent) { $reason = $ReasonText }
  Write-ExportManifest -Root $Root -State $st -SummaryName $SummaryName -UpdateName $UpdateName -SessionList $SessionList -RangeText $RangeText -LastAt $now -ReasonText $reason
  return [pscustomobject]@{ Manifest = (Join-Path $Root '最新匯出.md'); NoContent = [bool]$NoContent }
}

# 匯出「上次之後的新內容」成一個檔案（放在 logs 的上層，方便直接拖走），然後刪掉所有場次
function Test-JunkText {
  # OCR 碎裂雜訊：沒有中文、太短、或符號比中文多（遊戲的數值面板常被認成一堆符號）
  param([string]$T)
  $cjk = 0; $sym = 0
  foreach ($ch in $T.ToCharArray()) {
    $code = [int]$ch
    if (($code -ge 0x4E00 -and $code -le 0x9FFF) -or ($code -ge 0x3400 -and $code -le 0x4DBF)) { $cjk++ }
    elseif ($ch -eq ' ') { }
    else { $sym++ }
  }
  if ($cjk -eq 0) { return $true }
  if ($T.Length -lt 8) { return $true }
  if ($sym -gt $cjk) { return $true }
  return $false
}

# 去重 + 去碎裂雜訊（回傳清理後的事件，並回報省了多少）
function Get-CleanedEvents {
  param($Events, [switch]$DropChat)

  $out = New-Object System.Collections.Generic.List[object]
  $seen = New-Object 'System.Collections.Generic.HashSet[string]'
  $prefixMax = @{}
  $lastH = ''
  $dropJunk = 0
  $dropDup = 0
  $i = 0
  while ($i -lt $Events.Count) {
    $e = $Events[$i]
    $i++
    $v = ([string]$e.V).Trim()
    if ($v.Length -eq 0) { $dropJunk++; continue }

    if ($e.K -eq 'Q' -and $DropChat) { $dropJunk++; continue }

    # 樓層／場景標題：同一個連續出現很多次 → 只留第一次
    if ($e.K -eq 'H') {
      if ($v -eq $lastH) { $dropDup++; continue }
      $lastH = $v
      $out.Add($e)
      continue
    }

    # 檢定宣告：緊接著就是同一技能、同一難度的擲骰結果 → 宣告是多餘的
    if ($e.K -eq 'C') {
      if ($i -lt $Events.Count) {
        $nx = $Events[$i]
        if ($nx.K -eq 'R' -and ([string]$nx.Sk) -eq ([string]$e.Sk) -and ([string]$nx.Dc) -eq ([string]$e.Dc)) {
          $dropDup++
          continue
        }
      }
    }

    if ($e.K -eq 'N' -or $e.K -eq 'Q') {
      if (Test-JunkText -T $v) { $dropJunk++; continue }
    }

    if ($seen.Contains($v)) { $dropDup++; continue }

    # 半截片段：前面已經收過更完整的同一句（用前 6 個字的索引比對，O(1)）
    if ($e.K -eq 'N') {
      $head = $v.Substring(0, [Math]::Min(6, $v.Length))
      if ($v.Length -lt 16 -and $v -notmatch '[。！？!?」』）)]$') {
        if ($prefixMax.ContainsKey($head) -and ([string]$prefixMax[$head]).Length -gt ($v.Length + 5)) { $dropJunk++; continue }
      }
      if (-not $prefixMax.ContainsKey($head) -or ([string]$prefixMax[$head]).Length -lt $v.Length) { $prefixMax[$head] = $v }
    }

    [void]$seen.Add($v)
    $out.Add($e)
  }

  return [pscustomobject]@{ Events = $out; DropJunk = $dropJunk; DropDup = $dropDup }
}

# 掃出樓層／場景／道具／檢定／選項／關鍵句，做成「先讀這段就夠」的摘要
function Get-DigestData {
  param($Events)
  $d = [ordered]@{
    Levels = New-Object System.Collections.Generic.List[string]
    Scenes = New-Object System.Collections.Generic.List[string]
    SceneLevel = @{}
    Items = New-Object System.Collections.Generic.List[string]
    Checks = @{}
    Options = New-Object System.Collections.Generic.List[string]
    SceneBest = @{}
    KeyLines = New-Object System.Collections.Generic.List[string]
    Ok = 0; Fail = 0
  }
  $curLevel = ''; $curScene = ''
  $optSeen = New-Object 'System.Collections.Generic.HashSet[string]'
  $itemSeen = New-Object 'System.Collections.Generic.HashSet[string]'
  $keySeen = New-Object 'System.Collections.Generic.HashSet[string]'
  $rxKey = [regex]'技能|傷害|伤害|狀態|状态|效果|難度|难度|回合|裝備|装备|能力|耐久|消耗|冷卻|冷卻|機制|规则|規則|檢定|检定|加成|減免|减免'

  foreach ($e in $Events) {
    $v = ([string]$e.V).Trim()
    switch ($e.K) {
      'H' {
        if ($v -match '^\s*(Level|Leve\w*|LEVEL)\s*[\dIlLOo]+') {
          if (-not $d.Levels.Contains($v)) { $d.Levels.Add($v) }
          $curLevel = $v; $curScene = ''
        } else {
          if (-not $d.Scenes.Contains($v)) { $d.Scenes.Add($v); $d.SceneLevel[$v] = $curLevel }
          $curScene = $v
        }
      }
      'I' {
        foreach ($m in [regex]::Matches($v, '[「『]([^」』]{1,20})[」』]')) {
          if ($itemSeen.Add($m.Groups[1].Value)) { $d.Items.Add($m.Groups[1].Value) }
        }
      }
      'O' {
        if ($v.Length -ge 4 -and $optSeen.Add($v)) { $d.Options.Add($v) }
      }
      'R' {
        $sk = [string]$e.Sk; if (-not $sk) { $sk = '?' }
        $dc = [string]$e.Dc; if (-not $dc) { $dc = '?' }
        $key = $sk + '|' + $dc
        if (-not $d.Checks.ContainsKey($key)) { $d.Checks[$key] = @{ sk = $sk; dc = $dc; ok = 0; no = 0; rolls = (New-Object System.Collections.Generic.List[string]) } }
        if ($e.Ok -eq $true) { $d.Checks[$key].ok++; $d.Ok++ } elseif ($e.Ok -eq $false) { $d.Checks[$key].no++; $d.Fail++ }
        $r = [string]$e.Roll
        if ($r -and $d.Checks[$key].rolls.Count -lt 4 -and -not $d.Checks[$key].rolls.Contains($r)) { $d.Checks[$key].rolls.Add($r) }
      }
      'N' {
        if ($curScene -and $v.Length -ge 12) {
          if (-not $d.SceneBest.ContainsKey($curScene) -or ([string]$d.SceneBest[$curScene]).Length -lt $v.Length) { $d.SceneBest[$curScene] = $v }
        }
        if ($v.Length -ge 16 -and $rxKey.IsMatch($v) -and $keySeen.Add($v)) { $d.KeyLines.Add($v) }
      }
    }
  }
  return $d
}

function Format-Digest {
  param($D, $RawCount, $KeptCount, $DropJunk, $DropDup, $PrevAt, [switch]$All, $FullName)

  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('## ① 快速摘要（**先讀這段通常就夠**；要原文再往下看 ②）')
  [void]$sb.AppendLine('')
  if ($All) {
    [void]$sb.AppendLine('- 範圍：**全部場次**（忽略上次匯出記錄）')
  } else {
    [void]$sb.AppendLine('- 範圍：**上次匯出之後的新內容**；原始 ' + $RawCount + ' 筆 → 去重/去雜訊後 ' + $KeptCount + ' 筆（省 ' + [int](100 - 100.0 * $KeptCount / [Math]::Max(1, $RawCount)) + '%：重複 ' + $DropDup + '、碎裂 ' + $DropJunk + '）')
  }
  if ($PrevAt) { [void]$sb.AppendLine('- 上次匯出：' + $PrevAt) }
  if ($FullName) { [void]$sb.AppendLine('- 完整（未截斷）檔案：`' + $FullName + '`') }
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('### 走過的樓層（' + $D.Levels.Count + '）')
  if ($D.Levels.Count -eq 0) { [void]$sb.AppendLine('- （沒有偵測到樓層標題）') } else {
    foreach ($l in $D.Levels) {
      [void]$sb.AppendLine('- ' + $l)
      # 新增（既有行原封不動）：樓層清單每一行下面補一行色碼註解
      $hx = Get-FloorColorHex -Text ([string]$l)
      if ($hx) { [void]$sb.AppendLine('  <!-- color:' + $hx + ' -->') }
    }
  }
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('### 場景（依序，共 ' + $D.Scenes.Count + ' 個）')
  if ($D.Scenes.Count -eq 0) { [void]$sb.AppendLine('- —') }
  else {
    $lim = [Math]::Min($D.Scenes.Count, 60)
    $lastLv = ''
    for ($j = 0; $j -lt $lim; $j++) {
      $s = $D.Scenes[$j]
      $lv = ''
      if ($D.SceneLevel.ContainsKey($s)) { $lv = [string]$D.SceneLevel[$s] }
      if ($lv -ne $lastLv) { [void]$sb.AppendLine('- **' + $lv + '**'); $lastLv = $lv }
      [void]$sb.AppendLine('  - ' + $s)
    }
    if ($D.Scenes.Count -gt $lim) { [void]$sb.AppendLine('- …還有 ' + ($D.Scenes.Count - $lim) + ' 個場景') }
  }
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('### 這次拿到的道具（' + $D.Items.Count + '）')
  if ($D.Items.Count -eq 0) { [void]$sb.AppendLine('- —') } else { [void]$sb.AppendLine('- ' + ($D.Items -join '、')) }
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('### 檢定／擲骰彙總（成功 ' + $D.Ok + '、失敗 ' + $D.Fail + '）')
  if ($D.Checks.Count -eq 0) { [void]$sb.AppendLine('- —') }
  else {
    foreach ($k in ($D.Checks.Keys | Sort-Object)) {
      $c = $D.Checks[$k]
      $line = '- ' + $c.sk + ' 難度' + $c.dc + '：' + ($c.ok + $c.no) + ' 次（成功 ' + $c.ok + '、失敗 ' + $c.no + '）'
      if ($c.rolls.Count -gt 0) { $line += '　骰值例：' + ($c.rolls -join '、') }
      [void]$sb.AppendLine($line)
    }
  }
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('### 出現過的選項（去重 ' + $D.Options.Count + ' 個）')
  if ($D.Options.Count -eq 0) { [void]$sb.AppendLine('- —') }
  else {
    $lim = [Math]::Min($D.Options.Count, 60)
    for ($j = 0; $j -lt $lim; $j++) { [void]$sb.AppendLine('- ' + $D.Options[$j]) }
    if ($D.Options.Count -gt $lim) { [void]$sb.AppendLine('- …還有 ' + ($D.Options.Count - $lim) + ' 個選項') }
  }
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('### 機制關鍵句（含技能／傷害／狀態／效果／難度…的旁白，' + $D.KeyLines.Count + ' 句，取前 100）')
  if ($D.KeyLines.Count -eq 0) { [void]$sb.AppendLine('- —') }
  else {
    $lim = [Math]::Min($D.KeyLines.Count, 100)
    for ($j = 0; $j -lt $lim; $j++) {
      $t = $D.KeyLines[$j]
      if ($t.Length -gt 100) { $t = $t.Substring(0, 100) + '…' }
      [void]$sb.AppendLine('- ' + $t)
    }
    if ($D.KeyLines.Count -gt $lim) { [void]$sb.AppendLine('- …還有 ' + ($D.KeyLines.Count - $lim) + ' 句') }
  }
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('### 每個場景代表句（前 30 個場景，最長的一句）')
  if ($D.SceneBest.Count -eq 0) { [void]$sb.AppendLine('- —') }
  else {
    $n = 0
    foreach ($s in $D.Scenes) {
      if ($n -ge 30) { break }
      if (-not $D.SceneBest.ContainsKey($s)) { continue }
      $t = [string]$D.SceneBest[$s]
      if ($t.Length -gt 100) { $t = $t.Substring(0, 100) + '…' }
      [void]$sb.AppendLine('- **' + $s + '**：' + $t)
      $n++
    }
  }
  [void]$sb.AppendLine('')
  return $sb.ToString()
}

# 重點事件：給「小檔案」用（結構化事件全留、旁白只留有意義的、不含聊天、全部有上限）
function Format-KeyEvents {
  param($Events)
  $sb = New-Object System.Text.StringBuilder
  $caps = @{ 'H' = 250; 'D' = 100; 'O' = 100; 'R' = 150; 'I' = 100; 'S' = 120; 'N' = 150; 'C' = 100 }
  $used = @{}
  $seenS = New-Object 'System.Collections.Generic.HashSet[string]'
  $skipped = @{}
  foreach ($e in $Events) {
    $k = [string]$e.K
    if ($k -eq 'Q' -or $k -eq 'X') { continue }
    if ($k -eq 'N' -and ([string]$e.V).Length -lt 20) { continue }
    if ($k -eq 'S') { if (-not $seenS.Add(([string]$e.V).Trim())) { continue } }
    if ($caps.ContainsKey($k)) {
      if (-not $used.ContainsKey($k)) { $used[$k] = 0 }
      if ($used[$k] -ge $caps[$k]) {
        if (-not $skipped.ContainsKey($k)) { $skipped[$k] = 0 }
        $skipped[$k]++
        continue
      }
      $used[$k]++
    }
    [void]$sb.AppendLine((Format-EventLine $e))
    # 新增（不影響既有行）：樓層行後面補一行 HTML 註解色碼
    if ($k -eq 'H') {
      $hx = Get-FloorColorHex -Text ([string]$e.V) -Fallback $e.Color
      if ($hx) { [void]$sb.AppendLine('<!-- color:' + $hx + ' -->') }
    }
  }
  return [pscustomobject]@{ Text = $sb.ToString(); Skipped = $skipped }
}

# 匯出「上次之後的新內容」：一個小檔（拖給 AI）+ 一個完整檔（留出處），然後清掉 logs
function Invoke-ExportAndClear {
  param([string]$OutDir, [string]$Language, [switch]$All)

  $root = Split-Path $OutDir -Parent
  $dirs = @(Get-ChildItem -Path $OutDir -Directory -Filter 'backroom-*' -ErrorAction SilentlyContinue | Sort-Object Name)
  if ($dirs.Count -eq 0) {
    # 收工時 logs 底下沒有場次資料夾 → 直接說「沒有新內容」，並把指標檔的檢查時間往前推
    $reason = 'logs 底下沒有新的場次資料夾'
    if (-not $All) {
      $rd0 = Read-ExportStateFile -Root $root
      if ($rd0.Ok) {
        $null = Set-ExportState -Root $root -Session ([string]$rd0.State.lastSession) -Count 0 -Sessions $null -NoContent -ReasonText $reason
      }
    }
    return [pscustomobject]@{ Empty = $true; Reason = $reason; ReasonCode = 'no-content'; SkippedSessions = 0; Manifest = (Join-Path $root '最新匯出.md') }
  }

  $live = @($dirs | Where-Object { Test-SessionLive -Dir $_.FullName })
  if ($live.Count -gt 0) { throw ('還有場次正在錄影（' + $live[0].Name + '），請先按「停止記錄」再匯出。') }

  # ---- 水位線（v2：含已匯出清單／筆數／時間；壞掉不靜默重匯）----
  # 注意：連 -ExportAll 也先讀一次舊水位線／舊 update 檔，只用來「累計已匯出清單」；
  #       要匯出什麼一律由下面的 $lastSession/$lastCount 決定（-ExportAll 時兩者都被清空＝全部匯）。
  $lastSession = ''; $lastCount = 0; $prevAt = ''
  $known = New-Object System.Collections.Generic.List[string]
  $syncTxt = ''
  $rd = Read-ExportStateFile -Root $root
  if ($rd.Ok) {
    $st = $rd.State
    if ($st.PSObject.Properties['exportedSessions']) {
      foreach ($nm in @($st.exportedSessions | Where-Object { $_ })) { if (-not $known.Contains($nm)) { $known.Add($nm) } }
    }
    if ($st.PSObject.Properties['exportedAt']) { $prevAt = [string]$st.exportedAt }
    if ($All) {
      Write-Host ('  （-ExportAll：忽略水位線，全部重匯；既有的 ' + $known.Count + ' 場紀錄仍會保留在累計清單）') -ForegroundColor DarkGray
    } else {
      $lastSession = [string]$st.lastSession
      if ($st.PSObject.Properties['lastCount']) { $lastCount = [int]$st.lastCount }
      $syncTxt = ('（水位線：' + $lastSession + '，已匯出 ' + $known.Count + ' 場）')
    }
  } else {
    # 水位線不見／壞掉 → 絕不靜默全部重匯：先從既有 update 檔回推
    $rec = Get-ExportedSessionsFromUpdates -Root $root
    if ($rec.Count -gt 0 -and $rec.Parts -gt 0) {
      foreach ($nm in $rec.Sessions) { if (-not $known.Contains($nm)) { $known.Add($nm) } }
      if ($All) {
        Write-Host ('  （-ExportAll：全部重匯；已從 ' + $rec.Parts + ' 個 update 檔補回 ' + $rec.Count + ' 場的累計紀錄）') -ForegroundColor DarkGray
      } else {
        # 水位線用「最新的那一場」＝update 檔內容時間的終點（單場檔沒有標頭時也推得出來）
        $lastSession = ''
        if ($rec.PSObject.Properties['Last']) { $lastSession = [string]$rec.Last }
        if (-not $lastSession -and $rec.Count -gt 0) {
          $lastSession = [string](@($rec.Sessions | Sort-Object)[$rec.Count - 1])
        }
        $lastCount = 0
        $syncTxt = ('（水位線缺失，已從 ' + $rec.Parts + ' 個 update 檔回推：' + $rec.Count + ' 場，' + $rec.LastAt + '）')
        Write-Host ('  [警告] ' + $rd.Reason + ' → 已從既有 update 檔回推已匯出 ' + $rec.Count + ' 場，只匯出比 ' + $lastSession + ' 新的場次。') -ForegroundColor Yellow
      }
    } elseif (-not $All) {
      # 回推不出來 → 明確警告 + 不匯出（要 -ExportAll 才會全匯；詳細訊息由主流程印）
      $msg = 'export-state.json 不存在或已損毀（' + $rd.Reason + '），而且無法從既有的 backroom-update-*.md 回推出已匯出的場次。'
      $msg = $msg + '為避免把已經處理過的內容重匯一次（白白消耗 token），這次【不匯出任何東西】。'
      $msg = $msg + '確定要全部重匯時，請用 -ExportAll（例如：backroom-logger.ps1 -Export -ExportAll）。'
      Write-ExportManifest -Root $root -State ([pscustomobject]@{ exportedSessions = @() }) -ReasonText $msg
      return [pscustomobject]@{ Empty = $true; Reason = $msg; ReasonCode = 'state-broken'; Manifest = (Join-Path $root '最新匯出.md') }
    } else {
      Write-Host '  （-ExportAll：沒有水位線也沒有舊 update 檔可回推，全部重匯）' -ForegroundColor DarkGray
    }
  }

  $loaded = New-Object System.Collections.Generic.List[object]
  $lastLoadedName = ''; $lastLoadedTotal = 0
  $skipped = 0
  $dupGuard = 0
  foreach ($d in $dirs) {
    $j = Join-Path $d.FullName 'wiki.jsonl'
    if (-not (Test-Path -LiteralPath $j)) { continue }
    $evts = New-Object System.Collections.Generic.List[object]
    foreach ($line in [System.IO.File]::ReadLines($j)) {
      if (-not $line.Trim()) { continue }
      try { $o = $line | ConvertFrom-Json } catch { continue }
      if (-not $o.k -or -not $o.v) { continue }
      $t = $null
      try { $t = [datetime]::ParseExact($o.t, 'HH:mm:ss', $null) } catch { $t = Get-Date }
      $evts.Add((New-LogEvent -K ([string]$o.k) -V ([string]$o.v) -T $t -Sk $o.sk -Dc $o.dc -Who $o.who -Roll $o.roll -Ok $o.ok -Color $o.color))
    }
    if ($evts.Count -eq 0) { continue }
    # -ExportAll = 使用者明確要求「全部重匯」→ 不做重複防護（但仍累計已匯出清單）
    if ($known.Contains($d.Name) -and -not $All) {
      # 這一場先前已經匯出過（水位線／update 檔回推都有紀錄）→ 跳過，除非筆數變少了（＝logs 被清掉後又開了同名場次）
      if ($evts.Count -lt $lastCount -and $d.Name -eq $lastSession) {
        $lastCount = 0
      } else {
        $skipped++
        continue
      }
    }
    if (-not $All -and $lastSession -and ([string]::CompareOrdinal($d.Name, $lastSession) -lt 0)) { continue }
    $total = $evts.Count
    if (-not $All -and $d.Name -eq $lastSession -and $lastCount -gt 0) {
      if ($total -le $lastCount) { $skipped++; continue }
      $evts = $evts.GetRange($lastCount, $total - $lastCount)
      if ($evts.Count -gt 0) { $dupGuard++ }
    }
    if ($evts.Count -eq 0) { continue }

    $started = $d.CreationTime.ToString('yyyy-MM-dd HH:mm:ss')
    if ($d.Name -match 'backroom-(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})(\d{2})') {
      $started = $Matches[1] + '-' + $Matches[2] + '-' + $Matches[3] + ' ' + $Matches[4] + ':' + $Matches[5] + ':' + $Matches[6]
    }
    $lastLoadedName = $d.Name
    $lastLoadedTotal = $total
    $loaded.Add([pscustomobject]@{ SessionName = $d.Name; Started = $started; Events = $evts })
  }
  if ($loaded.Count -eq 0) {
    $reason = '沒有新內容（沒有比上次匯出更新的場次）'
    if ($skipped -gt 0) { $reason = ('' + $skipped + ' 個場次都已經匯出過，全部跳過（沒有重複匯出）') }
    $null = Set-ExportState -Root $root -Session $lastSession -Count $lastCount -Sessions $null -NoContent -ReasonText $reason
    return [pscustomobject]@{ Empty = $true; Reason = $reason; ReasonCode = 'no-content'; SkippedSessions = $skipped; Manifest = (Join-Path $root '最新匯出.md') }
  }
  $sessionNames = @()
  foreach ($s in $loaded) { $sessionNames += $s.SessionName }
  $sessionListText = ($sessionNames -join '、')
  # 寫回水位線的清單＝「先前已匯出的」＋「這次匯出的」；漏掉前面那段會讓累計清單越寫越薄
  $merged = New-Object System.Collections.Generic.List[string]
  foreach ($nm in $known) { if (-not $merged.Contains($nm)) { $merged.Add($nm) } }
  foreach ($nm in $sessionNames) { if (-not $merged.Contains($nm)) { $merged.Add($nm) } }
  $mergedArr = [object[]]$merged

  $allEvents = New-Object System.Collections.Generic.List[object]
  foreach ($s in $loaded) {
    if ($loaded.Count -gt 1) {
      $allEvents.Add((New-LogEvent -K 'H' -V ('場次 ' + $s.SessionName + '（' + $s.Started + '）') -T $s.Events[0].T))
    }
    foreach ($e in $s.Events) { $allEvents.Add($e) }
  }
  $rawCount = $allEvents.Count

  $clean = Get-CleanedEvents -Events $allEvents
  $digest = Get-DigestData -Events $clean.Events
  $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
  $rangeText = $loaded[0].Started + ' 起'
  $fullName = 'backroom-update-' + $stamp + '.md'
  $sumName = 'backroom-summary-' + $stamp + '.md'
  $fullPath = Join-Path $root $fullName
  $sumPath = Join-Path $root $sumName
  $bom = New-Object System.Text.UTF8Encoding $true

  # ---- 小檔案（拖給 AI 用）----
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('# 狐霖的後室 RPG — 最新補充摘要（' + $rangeText + '）')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('> **給 AI 的說明**：這是**精簡版**（已去重、去碎裂雜訊、去掉隊伍頻道、各段有筆數上限）。')
  if (-not $All) { [void]$sb.AppendLine('> 只是「上次匯出之後的新內容」，請只更新／追加對應部分，**不要重寫既有 wiki 內容**。') }
  [void]$sb.AppendLine('> **讀完 ① 通常就夠**；② 是重點事件；真的要看每一句原文再開完整檔。')
  [void]$sb.AppendLine('> 標記：`##`/`###` 樓層場景　`@` 時間　N 旁白　D 決定　O 選項（含難度）　R 擲骰結果　C 檢定宣告　S 狀態　I 道具')
  [void]$sb.AppendLine('> OCR 可能有錯字（小圖示常被認成字，例如「小心！」→「刂吣！」），必要時用上下文判斷。')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('- 內容時間：' + $loaded[0].Started + ' ～ ' + $loaded[$loaded.Count - 1].Started + '　場次：' + $loaded.Count)
  [void]$sb.AppendLine('- 產生時間：' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '　來源：Edge 視窗截圖 → Windows OCR（' + $Language + '）')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine((Format-Digest -D $digest -RawCount $rawCount -KeptCount $clean.Events.Count -DropJunk $clean.DropJunk -DropDup $clean.DropDup -PrevAt $prevAt -All:$All -FullName $fullName))
  [void]$sb.AppendLine('---')
  [void]$sb.AppendLine('')
  $keyEv = Format-KeyEvents -Events $clean.Events
  [void]$sb.AppendLine('## ② 重點事件（結構化事件全留；旁白只留 ≥20 字的；不含隊伍頻道）')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine($keyEv.Text)
  if ($keyEv.Skipped.Count -gt 0) {
    [void]$sb.AppendLine('')
    $skipTxt = @()
    foreach ($k in ($keyEv.Skipped.Keys | Sort-Object)) { $skipTxt += ($k + ' ' + $keyEv.Skipped[$k] + ' 筆') }
    [void]$sb.AppendLine('> 因為筆數上限，省略了：' + ($skipTxt -join '、') + '（完整內容看 `' + $fullName + '`）')
  }
  [System.IO.File]::WriteAllText($sumPath, $sb.ToString(), $bom)

  # ---- 完整檔（出處用，不截斷）----
  $fb = New-Object System.Text.StringBuilder
  [void]$fb.AppendLine('# 狐霖的後室 RPG — 最新補充（完整，' + $rangeText + '）')
  [void]$fb.AppendLine('')
  [void]$fb.AppendLine('> 完整版（已去重、去碎裂雜訊，但**不截斷**）——留著當 wiki 出處的依據。')
  [void]$fb.AppendLine('> 要省閱讀量請用摘要檔 `' + $sumName + '`。')
  [void]$fb.AppendLine('')
  [void]$fb.AppendLine('- 內容時間：' + $loaded[0].Started + ' ～ ' + $loaded[$loaded.Count - 1].Started + '　場次：' + $loaded.Count)
  [void]$fb.AppendLine('- 產生時間：' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '　來源：Edge 視窗截圖 → Windows OCR（' + $Language + '）')
  [void]$fb.AppendLine('')
  [void]$fb.AppendLine((Format-Digest -D $digest -RawCount $rawCount -KeptCount $clean.Events.Count -DropJunk $clean.DropJunk -DropDup $clean.DropDup -PrevAt $prevAt -All:$All -FullName ''))
  [void]$fb.AppendLine('---')
  [void]$fb.AppendLine('')
  [void]$fb.AppendLine('## ② 完整事件（已去重、去碎裂雜訊）')
  [void]$fb.AppendLine('')
  [void]$fb.AppendLine((Write-WikiBody -Events $clean.Events))
  [System.IO.File]::WriteAllText($fullPath, $fb.ToString(), $bom)

  $null = Set-ExportState -Root $root -Session $lastLoadedName -Count $lastLoadedTotal -Sessions $mergedArr -EventTotal $rawCount -SummaryName $sumName -UpdateName $fullName -SessionList $sessionListText -RangeText $rangeText
  $newSessions = $loaded.Count
  $newEvents = $clean.Events.Count

  $removed = 0
  foreach ($d in $dirs) {
    if ($d.FullName -like (Join-Path $OutDir 'backroom-*')) {
      Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
      $removed++
    }
  }
  foreach ($f in @('wiki-all.md', 'INDEX.md')) {
    $fp = Join-Path $OutDir $f
    if (Test-Path -LiteralPath $fp) { Remove-Item -LiteralPath $fp -Force -ErrorAction SilentlyContinue }
  }

  return [pscustomobject]@{
    Target          = $sumPath
    Full            = $fullPath
    SummaryName     = $sumName
    UpdateName      = $fullName
    Sessions        = $loaded.Count
    SkippedSessions = $skipped
    DupGuard        = $dupGuard
    Removed         = $removed
    RawEvents       = $rawCount
    KeptEvents      = $clean.Events.Count
    NewSessions     = $newSessions
    NewEvents       = $newEvents
    SessionList     = $sessionListText
    RangeText       = $rangeText
    Sync            = $syncTxt
    Manifest        = (Join-Path $root '最新匯出.md')
  }
}


function Convert-SessionToWiki {
  param([string]$Dir, [string]$Language)

  if (Test-SessionLive -Dir $Dir) { return $null }

  $rows = New-Object System.Collections.Generic.List[object]
  $hasGeom = $false
  $imgW = 2221
  $src = Join-Path $Dir 'transcript.jsonl'
  $wikiJsonl = Join-Path $Dir 'wiki.jsonl'
  $loadedEvents = $null

  if (Test-Path -LiteralPath $src) {
    $hasGeom = $true
    foreach ($line in [System.IO.File]::ReadLines($src)) {
      if (-not $line.Trim()) { continue }
      try { $o = $line | ConvertFrom-Json } catch { continue }
      if (-not $o.text) { continue }
      $t = $null
      try { $t = [datetime]::ParseExact($o.t, 'HH:mm:ss', $null) } catch { $t = Get-Date }
      $rows.Add([pscustomobject]@{ Text = [string]$o.text; X = [int]$o.x; Y = [int]$o.y; T = $t; Stamp = [string]$o.t })
    }
  } elseif (Test-Path -LiteralPath $wikiJsonl) {
    # 已經是分好類的事件流 → 直接讀，只做去重與片段過濾（保留原本的分類）
    $loadedEvents = New-Object System.Collections.Generic.List[object]
    foreach ($line in [System.IO.File]::ReadLines($wikiJsonl)) {
      if (-not $line.Trim()) { continue }
      try { $o = $line | ConvertFrom-Json } catch { continue }
      if (-not $o.k -or -not $o.v) { continue }
      $t = $null
      try { $t = [datetime]::ParseExact($o.t, 'HH:mm:ss', $null) } catch { $t = Get-Date }
      $loadedEvents.Add((New-LogEvent -K ([string]$o.k) -V ([string]$o.v) -T $t -Sk $o.sk -Dc $o.dc -Who $o.who -Roll $o.roll -Ok $o.ok -Color $o.color))
    }
    if ($loadedEvents.Count -eq 0) { return $null }
  } else {
    # 沒有座標版也沒有事件流 → 用未過濾的 ocr-raw.log 重建
    $raw = Join-Path $Dir 'ocr-raw.log'
    if (-not (Test-Path -LiteralPath $raw)) { return $null }
    $stamp = ''
    $t = Get-Date
    $i = 0
    $started = $false
    foreach ($line in [System.IO.File]::ReadLines($raw)) {
      if ($line.StartsWith('=====')) {
        $started = $true
        $i = 0
        if ($line -match '(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') {
          $stamp = $Matches[1]
          try { $t = [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss', $null) } catch { $t = Get-Date }
        }
        if ($line -match '(\d+)x(\d+)') { if ([int]$Matches[1] -gt $imgW) { $imgW = [int]$Matches[1] } }
        continue
      }
      if (-not $started) { continue }
      if (-not $line.Trim()) { continue }
      $i++
      $rows.Add([pscustomobject]@{ Text = $line.Trim(); X = 0; Y = (1000 + $i * 40); T = $t; Stamp = $stamp })
    }
  }
  if (-not $loadedEvents -and $rows.Count -eq 0) { return $null }

  if ($hasGeom) {
    $maxX = 0
    foreach ($r in $rows) { if ($r.X -gt $maxX) { $maxX = $r.X } }
    $imgW = $maxX + 80
  }
  $lh = [Math]::Max(18, [int]($imgW * 0.0171))

  $S = [pscustomobject]@{
    SessionName = Split-Path $Dir -Leaf
    Started     = (Get-Item -LiteralPath $Dir).CreationTime.ToString('yyyy-MM-dd HH:mm:ss')
    WindowTitle = ''
    Language    = $Language
    Interval    = 2
    Recent      = New-Object System.Collections.Generic.List[string]
    Events      = New-Object System.Collections.Generic.List[object]
    Utf8Bom     = New-Object System.Text.UTF8Encoding $true
  }

  if ($loadedEvents) {
    # 事件流是「已整理過的真相」→ 只重新渲染，不再二次過濾（這樣重跑幾次結果都一樣）
    foreach ($e in $loadedEvents) { $S.Events.Add($e) }
    $S | Add-Member -NotePropertyName Files -NotePropertyValue @{
      Wiki  = Join-Path $Dir 'wiki.md'
      Jsonl = Join-Path $Dir 'wiki.jsonl'
    } -Force
    Write-WikiFiles -S $S
    return $S
  }

  $optMode = $false
  $curStamp = ''
  $frameEvents = New-Object System.Collections.Generic.List[object]
  foreach ($r in $rows) {
    if ($r.Stamp -ne $curStamp) {
      foreach ($fe in $frameEvents) { $S.Events.Add($fe) }
      $curStamp = $r.Stamp
      $optMode = $false
      $frameEvents = New-Object System.Collections.Generic.List[object]
    }
    if (-not (Test-KeepLine $r.Text)) { continue }
    if ($RxPrompt.IsMatch($r.Text)) { $optMode = $true; continue }
    if (Test-Duplicate -S $S -Text $r.Text) { continue }
    if (Test-Fragment -S $S -Text $r.Text) { Add-Recent -S $S -Text $r.Text; continue }
    $ev = Add-ClassifiedLine -S $S -Text $r.Text -X $r.X -Y $r.Y -Hh $lh -ImgW $imgW -T $r.T -SameFrame $true -FrameEvents $frameEvents
    if ($ev -is [string]) { Add-Recent -S $S -Text $r.Text; continue }
    if ($ev) {
      Add-Recent -S $S -Text $r.Text
      Add-Recent -S $S -Text $ev.V
      if ($optMode -and $ev.K -eq 'N' -and $ev.V.Length -le 24) { $ev.K = 'O' }
      if ($ev.K -ne 'N' -and $ev.K -ne 'O') { $optMode = $false }
    }
  }

  foreach ($fe in $frameEvents) { $S.Events.Add($fe) }

  $S | Add-Member -NotePropertyName Files -NotePropertyValue @{
    Wiki  = Join-Path $Dir 'wiki.md'
    Jsonl = Join-Path $Dir 'wiki.jsonl'
  } -Force
  Write-WikiFiles -S $S
  return $S
}

function Write-CombinedWiki {
  param($Sessions, [string]$Root, [string]$Language)

  $bom = New-Object System.Text.UTF8Encoding $true
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('# 狐霖的後室 RPG — 全部冒險紀錄（合併）')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('- 場次數：' + $Sessions.Count + '　合併時間：' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
  [void]$sb.AppendLine('- 來源：Edge 視窗截圖 → Windows OCR（' + $Language + '）；畫面出現過的文字才會被記到')
  [void]$sb.AppendLine('- 標記：`##` 樓層　`###` 場景　`@` 時間　N 旁白　D 決定　O 選項（含檢定難度）　R 擲骰結果　C 檢定宣告　S 狀態變化　I 道具　Q 隊伍頻道　`>` 記錄中斷')
  [void]$sb.AppendLine('- 已整理：同一段旁白合併成一行、介面按鈕等雜訊不記、時間只在跨分鐘或間隔 >15 秒時標出')
  [void]$sb.AppendLine('- 每場次的原始 OCR 全文在該場次資料夾的 ocr-raw.log')
  [void]$sb.AppendLine('')
  foreach ($s in $Sessions) {
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 場次 ' + $s.SessionName + '（' + $s.Started + '）')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine((Write-WikiBody -Events $s.Events))
  }
  [System.IO.File]::WriteAllText((Join-Path $Root 'wiki-all.md'), $sb.ToString(), $bom)

  $ix = New-Object System.Text.StringBuilder
  [void]$ix.AppendLine('# 場次索引')
  [void]$ix.AppendLine('')
  [void]$ix.AppendLine('| 場次 | 開始 | 事件 | 走過的樓層 | 檔案 |')
  [void]$ix.AppendLine('|---|---|---|---|---|')
  foreach ($s in $Sessions) {
    $levels = New-Object System.Collections.Generic.List[string]
    foreach ($e in $s.Events) {
      if ($e.K -eq 'H' -and $e.V -match '^\s*(Level|Leve\w*|LEVEL)\s*[\dIlLOo]+') {
        if (-not $levels.Contains($e.V)) { $levels.Add($e.V) }
      }
    }
    $lv = '—'
    if ($levels.Count -gt 0) { $lv = ($levels -join '、') }
    [void]$ix.AppendLine('| ' + $s.SessionName + ' | ' + $s.Started + ' | ' + $s.Events.Count + ' | ' + $lv + ' | wiki.md |')
  }
  [System.IO.File]::WriteAllText((Join-Path $Root 'INDEX.md'), $ix.ToString(), $bom)
}

# ---- 樓層配色快取（種子＝Wiki 實測表 ＋ 舊場次 floor-colors.log；匯出／錄影都要用）----
$colorInit = Initialize-FloorColors -Root $OutDir

# ---- 逐層分檔自我測試（-SelfTestLevels）：不碰使用者的 logs，在 %TEMP% 跑完整流程 ----
if ($SelfTestLevels) {
  $stRoot = Join-Path $env:TEMP ('br-levels-selftest-' + (Get-Date -Format 'HHmmss'))
  $stDir = Join-Path $stRoot 'backroom-SELFTEST'
  New-Item -ItemType Directory -Force -Path (Join-Path $stDir 'levels') | Out-Null
  $stUtf8 = New-Object System.Text.UTF8Encoding $true
  $stFiles = @{
    Wiki = (Join-Path $stDir 'wiki.md'); Jsonl = (Join-Path $stDir 'wiki.jsonl'); Raw = (Join-Path $stDir 'ocr-raw.log')
  Structured = (Join-Path $stDir 'structured.jsonl')
    Log = (Join-Path $stDir 'logger.log'); FloorColors = (Join-Path $stDir 'floor-colors.log')
    LevelsDir = (Join-Path $stDir 'levels'); Transitions = (Join-Path $stDir 'transitions.log')
  }
  [System.IO.File]::WriteAllText($stFiles.Transitions, '# 跨樓層事件' + [Environment]::NewLine, $stUtf8)
  $st = [pscustomobject]@{
    SessionName = 'backroom-SELFTEST'; Started = '2026-10-16 22:00:00'; WindowTitle = 'SELFTEST'; Language = $Language
    Interval = 1.2; Events = (New-Object System.Collections.Generic.List[object]); Recent = (New-Object System.Collections.Generic.List[string])
    Files = $stFiles; Utf8Bom = $stUtf8; Frames = 0; FrameNo = 0
    CurrentLevel = ''; CurrentLevelName = ''; CurrentLevelFile = ''; LevelEntries = 0; LevelSeen = @{}
    LevelSeenAt = @{}; TransitionFrame = @{}; FrameHadHeader = $false; LevelFromHeader = $false; LastLevelFrame = 0
    LevelNameById = @{}; LevelFileById = @{}; LevelUnknown = $false; LevelUnknownKey = ''; LevelEventCount = 0
    LastBg = $null; LevelWriteMap = $script:LevelWriteMap
    FrameLevelEvents = (New-Object System.Collections.Generic.List[object]); FrameLevelChanged = $false
  }
  Set-InitialLevelLog -S $st
  # 自我測試會在 Write-Log 被定義之前執行，先給一個等價的版本（只寫測試場次的 logger.log）
  function Write-Log { param($S, [string]$Msg) [System.IO.File]::AppendAllText($S.Files.Log, ('[' + (Get-Date).ToString('HH:mm:ss') + '] ' + $Msg) + [Environment]::NewLine, $S.Utf8Bom) }
  # 每一幀：$Title = 模擬 title 區讀到的樓層標題；$Lines = 模擬 main 區讀到的正文
  $stFrames = @(
    @{ T = ''; L = @('你走进了大厅', '出口进度 +5') },
    @{ T = ''; L = @('你决定：先看看四周') },
    @{ T = 'Level 1 「杏仁香的水窪」'; L = @('水窪的水很甜'); Bg = 'Level 1' },
    @{ T = ''; L = @('你捡起了一把生锈的钥匙', '体力 -1') },
    @{ T = 'Level 1 杏仁香的水窪'; L = @('水窪旁边有一串脚印') },
    @{ T = '第 1 层 | 難度 3 | 潮濕區'; L = @('一只滴水的水龙头') },
    @{ T = ''; L = @('一张蒙尘的办公桌', 'Level 4 「廢棄辦公室」') },
    @{ T = ''; L = @('桌上的文件写着：实验体 07') },
    @{ T = ''; L = @('你决定：打开抽屉') },
    @{ T = 'Level 1 「杏仁香的水窪」'; L = @('水窪旁邊又出現腳印') },
    @{ T = ''; L = @('LeveI 「玻璃之狂」') },
    @{ T = ''; L = @('玻璃碎片散落一地') }
  )
  $stFrameNo = 0
  foreach ($fr in $stFrames) {
    $stFrameNo++
    $st.FrameNo = $stFrameNo; $st.Frames = $stFrameNo
    $st.FrameLevelEvents = New-Object System.Collections.Generic.List[object]
    $st.FrameLevelChanged = $false
    $st.FrameHadHeader = $false
    $fromId = [string]$st.CurrentLevel; $fromName = [string]$st.CurrentLevelName
    # 模擬 title 區那一側的底色（用配色快取的實測值；沒有就留 $null = 只有 H 事件能換層）
    $st.LastBg = $null
    if ($fr.Bg -and $script:FloorCache) {
      $be = $script:FloorCache.FloorsById[[string]$fr.Bg]
      if ($be) { $st.LastBg = [pscustomobject]@{ R = [int]$be.r; G = [int]$be.g; B = [int]$be.b; Hex = [string]$be.hex; Rgb = '' } }
    }
    $stFrameEvents = New-Object System.Collections.Generic.List[object]
    $sy = 100
    foreach ($tx in @($fr.L)) {
      $null = Add-ClassifiedLine -S $st -Text $tx -X 20 -Y $sy -Hh 34 -ImgW 1696 -T ([datetime]'2026-10-16T22:00:00').AddSeconds($stFrameNo) -SameFrame $true -FrameEvents $stFrameEvents -Region 'main' -FrameNo $stFrameNo
      $sy += 40
    }
    # title 區只有在事件流這一幀沒有 H 樓層標題行時才決定樓層（跟正式流程一致）
    if (-not $st.FrameHadHeader -and $fr.T) {
      $tNum = Get-LevelNumberFromText -Text ([string]$fr.T)
      $curNum = Get-LevelNumberFromId -Id ([string]$st.CurrentLevel)
      $allowTitle = $false
      if ($tNum -and $curNum -and (Test-LevelNumSame -A $tNum -B $curNum)) { $allowTitle = $true }
      elseif ($tNum -and $st.LastBg -and $script:FloorCache) {
        $tFix = Resolve-FloorNumber -Num $tNum
        if ($script:FloorCache.FloorsById['Level ' + $tFix]) {
          $colHit = Get-NearbyFloorName -Bg $st.LastBg -LevelIdNum $tNum
          if ($colHit) { $allowTitle = $true }
        }
      }
      if ($allowTitle) {
        $r0 = Update-LevelFromText -S $st -Text ([string]$fr.T) -Bg $st.LastBg
        if ($r0 -eq 'changed') {
          $st.FrameLevelChanged = $true
          $st.LastLevelFrame = $stFrameNo
          Write-TransitionEntry -S $st -FrameNo $stFrameNo -Ev $null -FromLevel $fromId -FromName $fromName -ToLevel $st.CurrentLevel -ToName $st.CurrentLevelName -Reason ('title 區樓層標題（後備）：' + [string]$fr.T)
        }
      } elseif (-not $tNum) { Add-LevelUnknownNote -S $st -Text ([string]$fr.T) }
    }
    if ($stFrameEvents.Count -gt 0) {
      foreach ($e in $stFrameEvents) { $st.Events.Add($e); $st.FrameLevelEvents.Add($e) }
      Write-WikiFiles -S $st
      Write-LevelFiles -S $st -FrameNo $stFrameNo
    }
  }
  Write-Host ''
  Write-Host ('逐層分檔自我測試（不碰 logs\，全部在 ' + $stRoot + '）') -ForegroundColor Yellow
  Write-Host ''
  Write-Host 'levels\ 檔案清單：' -ForegroundColor Cyan
  foreach ($f in @(Get-ChildItem -LiteralPath $stFiles.LevelsDir -File | Sort-Object Name)) { Write-Host ('  ' + $f.Name + '  (' + $f.Length + ' B)') }
  Write-Host ''
  foreach ($f in @(Get-ChildItem -LiteralPath $stFiles.LevelsDir -File | Sort-Object Name)) {
    Write-Host ('----- ' + $f.Name + ' -----') -ForegroundColor Green
    foreach ($ln in @(Get-Content -LiteralPath $f.FullName -Encoding UTF8)) { Write-Host ('  ' + $ln) }
    Write-Host ''
  }
  Write-Host '----- _transitions.log -----' -ForegroundColor Magenta
  foreach ($ln in @(Get-Content -LiteralPath $stFiles.Transitions -Encoding UTF8)) { Write-Host ('  ' + $ln) }
  Write-Host ''
  Write-Host ('事件總數=' + $st.Events.Count + '；逐層寫入行數=' + $st.LevelEventCount + '；wiki.jsonl 行數=' + @(Get-Content -LiteralPath $stFiles.Jsonl -Encoding UTF8).Count) -ForegroundColor Cyan
  Write-Host ('輸出目錄：' + $stRoot) -ForegroundColor DarkGray
  exit 0
}



if ($Export) {
  $r = Invoke-ExportAndClear -OutDir $OutDir -Language $Language -All:$ExportAll
  if ($null -eq $r) {
    Write-Host '  沒有新內容（距離上次收工還沒有新紀錄）。'
    exit 1
  }
  if ($r.Empty) {
    Write-Host ''
    if ($r.ReasonCode -eq 'state-broken') {
      Write-Host '  [警告] 水位線檔（export-state.json）不存在或損毀，無法確認哪些場次已經匯出過。' -ForegroundColor Yellow
      Write-Host '  為避免把已處理過的內容重匯一次（白花 token），這次【不匯出任何東西】。' -ForegroundColor Yellow
      Write-Host '  確定要全部重匯 → 加 -ExportAll（backroom-logger.ps1 -Export -ExportAll）。' -ForegroundColor Yellow
    } else {
      Write-Host ('  沒有新內容：' + $r.Reason) -ForegroundColor Yellow
      if ($r.PSObject.Properties['SkippedSessions'] -and [int]($r.SkippedSessions) -gt 0) {
        Write-Host ('  被跳過的場次（已匯出過）：' + $r.SkippedSessions) -ForegroundColor Yellow
      }
    }
    if ($r.Manifest) { Write-Host ('  指標：' + $r.Manifest) }
    if ($r.ReasonCode -eq 'state-broken') { exit 2 }
    exit 1
  }
  Write-Host ('EXPORT: ' + $r.Target)
  Write-Host ('EXPORT_FULL: ' + $r.Full)
  Write-Host ('  摘要（拖給 AI）  ：' + $r.Target)
  Write-Host ('  完整（wiki 出處）：' + $r.Full)
  Write-Host ('  已清空 logs（刪掉 ' + $r.Removed + ' 個場次資料夾）')
  # ---- D. 匯出後的自我檢查 ----
  Write-Host ''
  Write-Host '  [匯出自我檢查]'
  Write-Host ('    新增場次：' + $r.NewSessions + '（' + $r.SessionList + '）')
  Write-Host ('    新增事件：原始 ' + $r.RawEvents + ' 筆 → 去重去雜訊後 ' + $r.NewEvents + ' 筆')
  Write-Host ('    被跳過的場次（先前已匯出）：' + $r.SkippedSessions)
  if ($r.Sync) { Write-Host ('    水位線來源：' + $r.Sync) }
  if ($r.SkippedSessions -eq 0) {
    Write-Host '    ✅ 沒有偵測到可能重複的內容。' -ForegroundColor Green
  } else {
    Write-Host ('    ⚠ 有 ' + $r.SkippedSessions + ' 個場次因為「已經匯出過」被跳過 → 沒有重複匯出。') -ForegroundColor Yellow
  }
  if ($r.DupGuard -gt 0) {
    Write-Host ('    ⚠ 有 ' + $r.DupGuard + ' 個場次是「同場續錄」→ 只匯出上次之後的新事件（不是重複）。') -ForegroundColor Yellow
  }
  Write-Host ('    最新匯出指標：' + $r.Manifest)
  Write-Host ''
  Write-Host '  ⚠ 要匯入 Wiki 請只讀上面那個 summary；不要整份重讀 來源紀錄\ 底下的舊 update。' -ForegroundColor Yellow
  exit 0
}

if ($WikiAll -or $WikiSession) {
  $targets = @()
  if ($WikiSession) {
    $d = $WikiSession
    if (-not (Test-Path -LiteralPath $d)) { $d = Join-Path $OutDir $WikiSession }
    if (-not (Test-Path -LiteralPath $d)) { throw ('找不到場次：' + $WikiSession) }
    $targets = @(Get-Item -LiteralPath $d)
  } else {
    $targets = @(Get-ChildItem -Path $OutDir -Directory -Filter 'backroom-*' -ErrorAction SilentlyContinue | Sort-Object Name)
  }

  $done = New-Object System.Collections.Generic.List[object]
  foreach ($d in $targets) {
    $s = Convert-SessionToWiki -Dir $d.FullName -Language $Language
    if ($s) {
      $done.Add($s)
      Write-Host ('  轉好：' + $s.SessionName + '  ' + $s.Events.Count + ' 個事件')
      # 舊的 transcript.* 已被 wiki 取代（原文仍完整留在 ocr-raw.log）
      foreach ($old in @('transcript.md', 'transcript.jsonl')) {
        $p = Join-Path $d.FullName $old
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
      }
    }
  }
  if ($done.Count -gt 0) {
    Write-CombinedWiki -Sessions $done -Root $OutDir -Language $Language
    Write-Host ''
    Write-Host ('  合併檔：' + (Join-Path $OutDir 'wiki-all.md'))
    Write-Host ('  索引  ：' + (Join-Path $OutDir 'INDEX.md'))
  } else {
    Write-Host '  沒有可以轉的場次。'
  }
  exit 0
}



# ============================ 主流程（錄影） ============================
# ---- 校正模式：只掃面板邊界，不錄影 ----
function Invoke-Calibrate {
  param([string]$Image, [long]$Hwnd, [string]$TitleMatch)

  $tmp = $null
  if ($Image) {
    if (-not (Test-Path -LiteralPath $Image)) { throw ('找不到校正圖檔：' + $Image) }
    $bmp = [System.Drawing.Bitmap]::FromFile($Image)
    Write-Host ('  校正來源：' + $Image + '（' + $bmp.Width + 'x' + $bmp.Height + '）') -ForegroundColor Gray
  } else {
    $w = Find-GameWindow -Hwnd $Hwnd -TitleMatch $TitleMatch
    if (-not $w) { throw '找不到瀏覽器視窗；請用 -CalibrateImage <圖檔> 或先把遊戲開在 Edge 裡。' }
    Write-Host ('  校正來源：即時視窗「' + $w.Title + '」（不會動視窗大小、不搶焦點）') -ForegroundColor Gray
    $full = Get-WindowImage -Top $w.Hwnd
    if (-not $full) { throw 'PrintWindow 截圖失敗。' }
    $pr = Get-PageRect -Top $w.Hwnd
    $rc = New-Object System.Drawing.Rectangle($pr.X, $pr.Y, $pr.W, $pr.H)
    $rc.Intersect((New-Object System.Drawing.Rectangle(0, 0, $full.Width, $full.Height)))
    $bmp = $full.Clone($rc, $full.PixelFormat)
    $full.Dispose()
    Write-Host ('  render 區：' + $bmp.Width + 'x' + $bmp.Height + '（校正基準 ' + $script:BaseW + 'x' + $script:BaseH + '）') -ForegroundColor Gray
  }
  # 掃描提示一律換成「這個 render 尺寸下的實際座標」：拿著校正基準的絕對值去掃別尺寸的畫面，
  # 掃描會落空，然後靜靜沿用參數 → 校正顯示「差 0px」的假陽性就是這樣來的。
  $hMainRect = ConvertTo-RegionRect -Spec $RegionMain -CropW $bmp.Width -CropH $bmp.Height -Name 'main'
  $hOptsRect = ConvertTo-RegionRect -Spec $RegionOpts -CropW $bmp.Width -CropH $bmp.Height -Name 'opts'
  $hMain = Format-RegionRect -Rect $hMainRect
  $hOpts = Format-RegionRect -Rect $hOptsRect

  try {
    # 0) 先用 main 區取樣底色 → 找快取裡最接近的樓層 → 拿它的「預期底色」當掃描種子色
    $calRect = ConvertTo-RegionRect -Spec $RegionMain -CropW $bmp.Width -CropH $bmp.Height -Name 'main'
    $calFill = Get-MainFillColor -Bmp $bmp -MainRect $calRect
    $calSample = Get-BgSampleRect -MainRect $calRect -Sample $ColorSample -Bmp $bmp -FillHex $(if ($calFill) { $calFill.Hex } else { '' })
    $bgCal = Get-RectAverageColor -Bmp $bmp -Rect $calSample
    $calNear = $null; $calSeed = ''
    if ($bgCal) {
      $calNear = Find-NearestFloorColor -R ([int]$bgCal.R) -G ([int]$bgCal.G) -B ([int]$bgCal.B)
      if ($calNear -and ([int]$calNear.Dist -le $ColorThreshold)) { $calSeed = [string]$calNear.Hex }
    }
    # 種子色對不上樓層快取時，用「main 區主色」（面板底色的最大公約數）當種子：
    # 單點取樣可能剛好壓在世界貼圖或文字上，主色穩得多。
    if (-not $calSeed -and $calFill) { $calSeed = [string]$calFill.Hex }
    # 1) 不限範圍掃（看這個底色的面板實際到哪裡）
    $rs = $null; $ro = $null
    if ($hMain) { $rs = Invoke-PanelScan -Bmp $bmp -Name 'main' -Hint $hMain -SeedHex $calSeed }
    if ($hOpts) { $ro = Invoke-PanelScan -Bmp $bmp -Name 'opts' -Hint $hOpts -SeedHex $calSeed }
    # 2) 限定在預設 x 附近掃（避開「同色的隔壁面板」，例如隊伍頻道）
    $rsB = $null; $roB = $null
    if ($hMain) { $rsB = Invoke-PanelScan -Bmp $bmp -Name 'main' -Hint $hMain -BoundX -SeedHex $calSeed }
    if ($hOpts) { $roB = Invoke-PanelScan -Bmp $bmp -Name 'opts' -Hint $hOpts -BoundX -SeedHex $calSeed }
    if (-not $rs) { $rs = $rsB }
    if (-not $ro) { $ro = $roB }
    $rMainAll = $null
    if ($rs -and $rs.X0 -ge 0 -and $rs.X1 -gt $rs.X0 -and $rs.Y0 -ge 0 -and $rs.Y1 -gt $rs.Y0) {
      $rMainAll = New-Object System.Drawing.Rectangle($rs.X0, $rs.Y0, ($rs.X1 - $rs.X0), ($rs.Y1 - $rs.Y0))
    }
    $rOptsAll = $null
    if ($ro -and $ro.X0 -ge 0 -and $ro.X1 -gt $ro.X0 -and $ro.Y0 -ge 0 -and $ro.Y1 -gt $ro.Y0) {
      $rOptsAll = New-Object System.Drawing.Rectangle($ro.X0, $ro.Y0, ($ro.X1 - $ro.X0), ($ro.Y1 - $ro.Y0))
    }
    Write-Host ('  掃描提示（render 區）：main ' + $(if ($hMain) { $hMain } else { '—' }) + '　opts ' + $(if ($hOpts) { $hOpts } else { '—' })) -ForegroundColor DarkGray
    Write-Host ('  掃描種子色：' + $(if ($calSeed) { $calSeed + '（快取：' + $calNear.Id + '，色距 ' + $calNear.Dist + '）' } else { '（沒有對得上的樓層色 → 用取樣點顏色）' })) -ForegroundColor DarkGray

    Write-Host ''
    Write-Host '  ── 量到的面板 ─────────────────────────────────────────────' -ForegroundColor Yellow
    if ($rs) {
      Write-Host ('  main 底色=' + $rs.Hex + '（取樣點 x=' + $rs.SampleX + ' y=' + $rs.SampleY + '）')
      $ys = $(if ($rs.Y0 -lt 0) { '量測失敗' } elseif ($rs.YCredible) { '量到 ' + $rs.Y0 + '..' + $rs.Y1 } else { '量測失敗（沿用參數 ' + $rs.Y0 + '..' + $rs.Y1 + '）' })
      Write-Host ('       上下界：' + $ys)
      Write-Host ('       左右界：不限範圍 x ' + $rs.X0 + '..' + $rs.X1 + '　｜　限定提示附近 x ' + $rsB.X0 + '..' + $rsB.X1 + $(if ($rsB.XCredible) { '（可信）' } else { '（不可信 → 錄影時沿用參數的 x）' }))
      if ($rs.Gaps -and $rs.Gaps.Count -gt 0) {
        $rt = @()
        foreach ($q in ($rs.Gaps | Sort-Object Y0)) { if ($rt.Count -lt 12) { $rt += ('y' + $q.Y0 + '-' + $q.Y1) } }
        Write-Host ('       空白分隔帶：' + ($rt -join '、')) -ForegroundColor DarkGray
      }
    } else { Write-Host '  main 底色=取樣失敗（畫面可能不是遊戲主畫面）' -ForegroundColor Yellow }
    if ($ro -and $ro.Y1 -ge 0) {
      Write-Host ('  opts 底色=' + $ro.Hex + '（取樣點 x=' + $ro.SampleX + ' y=' + $ro.SampleY + '）')
      $yo = $(if ($ro.YCredible) { '量到 ' + $ro.Y0 + '..' + $ro.Y1 } else { '量測失敗（沿用參數 ' + $ro.Y0 + '..' + $ro.Y1 + '）' })
      Write-Host ('       上下界：' + $yo)
      Write-Host ('       左右界：不限範圍 x ' + $ro.X0 + '..' + $ro.X1 + '　｜　限定提示附近 x ' + $roB.X0 + '..' + $roB.X1 + $(if ($roB.XCredible) { '（可信）' } else { '（不可信 → 錄影時沿用參數的 x）' }))
    } else { Write-Host '  opts 底色=取樣失敗（畫面可能不是遊戲主畫面）' -ForegroundColor Yellow }
    Write-Host ''

    # 3) 用目前參數實際跑一次分區 OCR，看看每一區抓到什麼（校正才會知道座標對不對）
    $tmp = Join-Path $env:TEMP 'br-calib-region.png'
    $tmpTitle = Join-Path $env:TEMP 'br-calib-title.png'
    $lvl = ''
    $titleLines = @()
    try {
      $lang = New-Object Windows.Globalization.Language 'zh-Hans-CN'
      $eng = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($lang)
      Write-Host '  ── 用目前參數試跑分區 OCR（每區最多列 10 行）───────────────' -ForegroundColor Yellow
      $specs = @(
        [pscustomobject]@{ Name = 'title'; Spec = $RegionTitle },
        [pscustomobject]@{ Name = 'main'; Spec = $RegionMain },
        [pscustomobject]@{ Name = 'opts'; Spec = $RegionOpts },
        [pscustomobject]@{ Name = 'char'; Spec = $RegionChar }
      )
      foreach ($sp in $specs) {
        $rc = ConvertTo-RegionRect -Spec $sp.Spec -CropW $bmp.Width -CropH $bmp.Height -Name ('' + $sp.Name)
        if (-not $rc) { Write-Host ('  [' + $sp.Name + '] 座標無效：' + $sp.Spec) -ForegroundColor Yellow; continue }
        $bm = $bmp.Clone($rc, $bmp.PixelFormat)
        $sc = Scale-Image $bm 1.25
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        $sc.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
        $ls = Invoke-FrameOcr -PngPath $tmp -Engine $eng -Scale 1.25
        $sc.Dispose()
        Write-Host ('  [' + $sp.Name + '] ' + $rc.X + ',' + $rc.Y + ' ' + $rc.Width + 'x' + $rc.Height + ' → ' + $ls.Count + ' 行')
        $n = 0
        foreach ($l in $ls) { if ($n -ge 10) { break }; Write-Host ('        ' + $l.Text) -ForegroundColor DarkGray; $n++ }
        if ($sp.Name -eq 'title') { $lvl = Get-LevelName -Lines $ls; $titleLines = $ls }
      }
    } catch { Write-Host ('  （試跑分區 OCR 失敗：' + $_.Exception.Message + '）') -ForegroundColor Yellow }
    if ($lvl) { Write-Host ('  title 抓到樓層：' + $lvl) -ForegroundColor Gray }
    else { Write-Host '  title 沒抓到樓層名（畫面可能不在樓層標題上）' -ForegroundColor DarkGray }
    Write-Host ''

    # ---- 樓層配色快取比對（新增）----
    # 配色用「跟錄影時同一套取樣」（main 區右上角無文字方塊）量，才會跟 floor-colors.log 對得上
    $cRgb = $null
    if ($bgCal) { $cRgb = Convert-HexToRgb $bgCal.Hex } else { $cRgb = Convert-HexToRgb $rs.Hex }
    $cFloorText = Get-FloorTitleText -Lines $titleLines -Fallback $lvl
    $cId = Get-FloorKeyFromText -Text $cFloorText
    $cNear = $calNear
    if (-not $cNear -and $cRgb) { $cNear = Find-NearestFloorColor -R $cRgb.R -G $cRgb.G -B $cRgb.B }
    $cHex = $rs.Hex
    if ($bgCal) { $cHex = [string]$bgCal.Hex }
    $cKey = ('' + $bmp.Width + 'x' + $bmp.Height + '|' + $(if ($cId) { $cId } else { 'color' + $cHex }))
    Write-Host '  ── 樓層配色快取 ──────────────────────────────────────────' -ForegroundColor Yellow
    Write-Host ('  偵測到的樓層：' + $(if ($cId) { $cId + '「' + (Get-FloorNameFromText -Text $cFloorText) + '」（title：' + $cFloorText + '）' } else { '—（title 沒讀到樓層，這次只用底色）' }))
    Write-Host ('  取樣到的配色：main ' + $cHex + '（面板取樣點 ' + $rs.Hex + '）　opts ' + $(if ($ro -and $ro.Y1 -ge 0) { $ro.Hex } else { '—' }))
    if ($cId) {
      $ce = $script:FloorCache.FloorsById[$cId]
      if ($ce) { Write-Host ('  快取預期配色：' + $ce.hex + '（來源 ' + $ce.source + '、累積 ' + $ce.count + ' 次）' + $(if (([string]$ce.hex).ToUpper() -eq ([string]$cHex).ToUpper()) { ' ✓ 一致' } else { ' ⚠ 不同色（實測值優先，新的記進 alt）' })) }
      else { Write-Host ('  快取預期配色：尚未收錄（這次會寫進快取）') -ForegroundColor DarkGray }
    }
    if ($cNear) { Write-Host ('  最接近的樓層色：' + $cNear.Id + ' ' + $cNear.Hex + '（色距 ' + $cNear.Dist + $(if ($cNear.Dist -gt $ColorThreshold) { '，超過門檻 ' + $ColorThreshold + ' → 錄影時會重掃邊界' } else { '' }) + '）') }
    else { Write-Host '  最接近的樓層色：快取裡沒有相近的底色（視為新樓層）' -ForegroundColor DarkGray }
    $cr = Get-CachedRegion -Key $cKey
    if ($cr) { Write-Host ('  快取邊界（' + $cKey + '）：main=' + $cr.main + '　opts=' + $cr.opts + '（' + $cr.last + '）') }
    else { Write-Host ('  快取邊界（' + $cKey + '）：無，這次校正會寫入') -ForegroundColor DarkGray }
    Write-Host ('  快取檔：' + $script:ColorCachePath + '（' + $script:FloorCache.Floors.Count + ' 層配色、' + $script:FloorCache.Regions.Count + ' 筆邊界）') -ForegroundColor DarkGray
    Write-Host ''

    $presets = @(
      [pscustomobject]@{ Name = 'main'; Cur = $RegionMain; Hint = $hMainRect; Meas = (ConvertTo-RegionMeasurement -R $rsB -Hint $hMainRect); Meas2 = (Format-RegionRect -Rect $rMainAll) }
      [pscustomobject]@{ Name = 'opts'; Cur = $RegionOpts; Hint = $hOptsRect; Meas = (ConvertTo-RegionMeasurement -R $roB -Hint $hOptsRect); Meas2 = (Format-RegionRect -Rect $rOptsAll) }
      [pscustomobject]@{ Name = 'title'; Cur = $RegionTitle; Hint = $null; Meas = $null; Meas2 = '' }
      [pscustomobject]@{ Name = 'char'; Cur = $RegionChar; Hint = $null; Meas = $null; Meas2 = '' }
    )
    $anyWarn = $false
    $anyMeasured = $false
    $sugMain = $RegionMain; $sugOpts = $RegionOpts
    Write-Host '  ── 與目前參數比較（差 >10px 就警告）───────────────────────' -ForegroundColor Yellow
    foreach ($p in $presets) {
      $mm = $null
      if ($p.Meas -and $p.Meas.Rect) { $mm = $p.Meas }
      if (-not $mm -or -not $mm.Text) {
        Write-Host ('  ' + $p.Name.PadRight(5) + ' 目前 ' + $p.Cur.PadRight(18) + ' 量測失敗（此區沒有自動掃描，沿用參數）') -ForegroundColor DarkGray
        continue
      }
      $cm = Compare-RegionMeasure -Meas $mm -Hint $p.Hint
      $from = 'x=' + $mm.XFrom + ' y=' + $mm.YFrom
      if (-not $cm.Any) {
        # ⚠ 這裡是舊版的假陽性來源：量不到就沿用參數，卻顯示「✓ 差 0px」。
        #   現在只要有一軸沒量到，就明說「量測失敗（沿用參數）」，不給 ✓/⚠ 結論。
        Write-Host ('  ' + $p.Name.PadRight(5) + ' 目前 ' + $p.Cur.PadRight(18) + ' 量測失敗（' + $from + '）→ 無法判定，沿用參數') -ForegroundColor DarkGray
      } elseif ($cm.Diff -gt 10) {
        $anyWarn = $true; $anyMeasured = $true
        Write-Host ('  ' + $p.Name.PadRight(5) + ' 目前 ' + $p.Cur.PadRight(18) + ' 量到 ' + $cm.Text.PadRight(18) + ' ⚠ 差 ' + $cm.Diff + 'px（' + $from + '）') -ForegroundColor Yellow
      } else {
        $anyMeasured = $true
        Write-Host ('  ' + $p.Name.PadRight(5) + ' 目前 ' + $p.Cur.PadRight(18) + ' 量到 ' + $cm.Text.PadRight(18) + ' ✓ 差 ' + $cm.Diff + 'px（' + $from + '）') -ForegroundColor Green
      }
      if ($p.Meas2 -and $p.Meas2 -ne $mm.Text) { Write-Host ('        （不限左右範圍時量到 ' + $p.Meas2 + '：同色的隔壁面板可能被算進來）') -ForegroundColor DarkGray }
      if ($p.Name -eq 'main' -and $mm.XCredible -and $mm.YCredible) {
        $sugMain = ('' + $mm.Rect.Left + ',' + $mm.Rect.Right + ',' + $mm.Rect.Top + ',' + $mm.Rect.Bottom)
      }
      if ($p.Name -eq 'opts' -and $mm.XCredible -and $mm.YCredible) {
        $sugOpts = ('' + $mm.Rect.Left + ',' + $mm.Rect.Right + ',' + $mm.Rect.Top + ',' + $mm.Rect.Bottom)
      }
    }
    Write-Host ''
    Write-Host '  ── 可直接複製的新參數 ─────────────────────────────────────' -ForegroundColor Yellow
    Write-Host ('  render 區座標（' + $bmp.Width + 'x' + $bmp.Height + '，只適用這個尺寸）')
    Write-Host ('  -RegionTitle ''' + $RegionTitle + ''' -RegionMain ''' + $sugMain + ''' -RegionOpts ''' + $sugOpts + ''' -RegionChar ''' + $RegionChar + '''')
    Write-Host ('  校正基準 ' + $script:BaseW + 'x' + $script:BaseH + ' 的等效值（換尺寸也能用）') -ForegroundColor DarkGray
    Write-Host ('  -RegionMain ''' + (ConvertTo-BaseRegion -Rect $hMainRect -Rect2 $rMainAll -XCredible:([bool]($rsB -and $rsB.XCredible)) -YCredible:([bool]($rsB -and $rsB.YCredible))) + ''' -RegionOpts ''' + (ConvertTo-BaseRegion -Rect $hOptsRect -Rect2 $rOptsAll -XCredible:([bool]($roB -and $roB.XCredible)) -YCredible:([bool]($roB -and $roB.YCredible))) + '''') -ForegroundColor DarkGray
    Write-Host ''
    if ($anyWarn) { Write-Host '  ⚠ 有區塊與預設值差超過 10px，建議用上面的座標值。' -ForegroundColor Yellow }
    elseif ($anyMeasured) { Write-Host '  ✓ 量到的邊界與目前參數相符（都在 10px 內）。' -ForegroundColor Green }
    else { Write-Host '  ⚠ 全部區塊都量不到邊界（沿用參數）→ 沒有可用的校正結論。' -ForegroundColor Yellow }
    Write-Host ''

    # ---- 把這次量到的寫回配色快取（錄影時 -AutoRegion／自動校正就直接用得到）----
    $fMain = ''; $fOpts = ''
    if ($rsB) { $fMain = (ConvertTo-RegionMeasurement -R $rsB -Hint $hMainRect).Text }
    if ($roB) { $fOpts = (ConvertTo-RegionMeasurement -R $roB -Hint $hOptsRect).Text }
    if ($fMain -or $fOpts) {
      [void](Set-CachedRegion -Key $cKey -Main $fMain -Opts $fOpts -Hex $cHex -Floor $cId)
    }
    if ($cRgb -and $cId) { [void](Add-FloorColorFromSample -Hex $cHex -R $cRgb.R -G $cRgb.G -B $cRgb.B -LevelText $cFloorText -Source '自動偵測') }
    if (Save-FloorColorCache) { Write-Host ('  已寫入配色快取：' + $script:ColorCachePath + '（' + $script:FloorCache.Floors.Count + ' 層）') -ForegroundColor DarkGray }
    else { Write-Host '  ⚠ 配色快取寫入失敗（不影響校正結果）' -ForegroundColor Yellow }
    Write-Host ''
  } finally {
    if ($bmp) { $bmp.Dispose() }
    foreach ($f in @($tmp, $tmpTitle)) { if ($f) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue } }
  }
}

if ($Calibrate) {
  Invoke-Calibrate -Image $CalibrateImage -Hwnd $Hwnd -TitleMatch $TitleMatch
  exit 0
}

$ocrLang = New-Object Windows.Globalization.Language $Language
$ocrEngine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($ocrLang)
if ($null -eq $ocrEngine) {
  $avail = ([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | ForEach-Object { $_.LanguageTag }) -join ', '
  throw ('這個語言沒有 OCR 引擎：' + $Language + '（可用：' + $avail + '）')
}

if ($SessionDir -and (Test-Path -LiteralPath $SessionDir)) {
  # 續錄：沿用同一個場次資料夾，檔案一律往後追加（暫停 → 繼續 不會變成兩個場次）
  $sessionDir = (Resolve-Path -LiteralPath $SessionDir).Path
  $session = Split-Path $sessionDir -Leaf
  if ($session -match '^backroom-(.+)$') { $session = $Matches[1] }
  Write-Host ('  續錄到既有場次：' + $sessionDir) -ForegroundColor DarkGray
} else {
  $session = Get-Date -Format 'yyyyMMdd-HHmmss'
  $sessionDir = Join-Path $OutDir ('backroom-' + $session)
}
$framesDir = Join-Path $sessionDir 'frames'
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
# 順手清掉以前跑掛掉留下來的暫存圖（兩小時前的）
Get-ChildItem -Path $env:TEMP -Filter 'br-logger-*.png' -ErrorAction SilentlyContinue |
  Where-Object { $_.LastWriteTime -lt (Get-Date).AddHours(-2) } |
  Remove-Item -Force -ErrorAction SilentlyContinue

# 預設不留任何截圖：先把以前留下的 frames 資料夾清掉（那些圖的文字早就 OCR 進 wiki 了）
$purgedFrames = 0
foreach ($oldSession in @(Get-ChildItem -Path $OutDir -Directory -Filter 'backroom-*' -ErrorAction SilentlyContinue)) {
  if ($oldSession.FullName -eq $sessionDir) { continue }
  $oldFrames = Join-Path $oldSession.FullName 'frames'
  if (Test-Path -LiteralPath $oldFrames) {
    $purgedFrames += @(Get-ChildItem -LiteralPath $oldFrames -File -ErrorAction SilentlyContinue).Count
    Remove-Item -LiteralPath $oldFrames -Recurse -Force -ErrorAction SilentlyContinue
  }
}

$utf8Bom = New-Object System.Text.UTF8Encoding $true
$files = @{
  Wiki        = Join-Path $sessionDir 'wiki.md'
  Jsonl       = Join-Path $sessionDir 'wiki.jsonl'
  Raw         = Join-Path $sessionDir 'ocr-raw.log'
  Structured  = Join-Path $sessionDir 'structured.jsonl'
  Log         = Join-Path $sessionDir 'logger.log'
  FloorColors = Join-Path $sessionDir 'floor-colors.log'
  LevelsDir   = Join-Path $sessionDir 'levels'
  Transitions = Join-Path $sessionDir '_transitions.log'
}
# 給客戶端覆蓋層讀的「目前這一幀」；放在 logs 根目錄，不進單一場次。
$overlayFile = Join-Path $OutDir 'overlay.json'
[System.IO.File]::WriteAllText($files.Raw, '[' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '] 原始 OCR 逐幀記錄（完整保留，未經過濾）' + [Environment]::NewLine, $utf8Bom)
[System.IO.File]::WriteAllText($files.FloorColors, '[' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '] 各樓層介面配色（自動偵測；底色變化超過 ' + $ColorThreshold + ' 就記一筆）' + [Environment]::NewLine, $utf8Bom)
# 逐層分檔：levels\ 放各樓層，_transitions.log 放跨樓層事件（額外產出，不動既有檔案）
New-Item -ItemType Directory -Force -Path $files.LevelsDir | Out-Null
[System.IO.File]::WriteAllText($files.Transitions, '# 跨樓層事件（轉場／岔路／樓層結算／樓層交界同幀事件）' + [Environment]::NewLine + '# 場次：backroom-' + $session + '　建立：' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + [Environment]::NewLine + '# 說明：各樓層自己的紀錄在 levels\ 底下；這裡只放「不屬於單一樓層」的事件' + [Environment]::NewLine, $utf8Bom)

$win = Find-GameWindow -Hwnd $Hwnd -TitleMatch $TitleMatch
if (-not $win) { throw '找不到瀏覽器視窗。請先把遊戲開在 Edge 裡，或用 -Hwnd <視窗代碼> 指定。' }

$lineDir = ''
if ($SaveLineCrops) { $lineDir = Join-Path $sessionDir 'linecrops'; New-Item -ItemType Directory -Force -Path $lineDir | Out-Null }
# 裁圖內容 → 檔名（同內容只寫一次）。檔名直接用 hash，所以同圖必定同名。
$script:LineCropHash = @{}
# ---- 邊錄邊認（C）：背景 worker 會把新裁圖丟給 RapidOCR，收工時只套結果 ----
$ocrWorker = $null
$ocrStopFlag = Join-Path $sessionDir 'ocr-stop.flag'
$ocrAckFile = Join-Path $sessionDir 'ocr-done.ack'
$floorShotDir = ''
if ($AutoFloorShots) { $floorShotDir = Join-Path $sessionDir 'floor-shots'; New-Item -ItemType Directory -Force -Path $floorShotDir | Out-Null }
$floorShotSeen = @{}
$state = [pscustomobject]@{
  SessionName = 'backroom-' + $session
  Started     = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  WindowTitle = $win.Title
  WindowPid   = $win.Pid
  Language    = $Language
  Interval    = $Interval
  Events      = New-Object System.Collections.Generic.List[object]
  Recent      = New-Object System.Collections.Generic.List[string]
  Files       = $files
  Utf8Bom     = $utf8Bom
  Frames      = 0
  FrameSeq    = 0
  FrameNo     = 0
  # ---- 2026-10-29：裁圖去重（A）＋ 幀去重（B）----
  DupFrames   = 0          # 與上一幀完全相同、跳過 OCR 的幀數
  DupStart    = 0          # 靜止畫面區間：起始幀號（0 = 沒有開著的區間）
  DupEnd      = 0          # 靜止畫面區間：結束幀號
  CropReused  = 0          # 內容 hash 相同、沒有重寫的裁圖張數
  LastImgHash = ''
  LastRawFrame = $null
  LineDir     = $lineDir
  FloorShotDir = $floorShotDir
  FloorShotSeen = $floorShotSeen
  LastLayerNo = ''
  Fallbacks   = 0
  LastColor   = $null
  LastState   = ''
  # ---- 逐層分檔（levels\ ＋ _transitions.log）----
  CurrentLevel     = ''
  CurrentLevelName = ''
  CurrentLevelFile = ''
  LevelEntries     = 0
  LevelSeen        = @{}
  LevelSeenAt      = @{}
  TransitionFrame  = @{}
  LevelNameById    = @{}
  LevelFileById    = @{}
  LastBg           = $null
  # 逐層分檔用：避免同一個 H 事件在 _transitions.log 與樓層檔各寫一次
  LevelWriteMap    = $script:LevelWriteMap
  LevelUnknown     = $false
  FrameHadHeader   = $false
  LevelFromHeader  = $false
  LastLevelFrame   = 0
  LevelUnknownKey  = ''
  LevelEventCount  = 0
  FrameLevelEvents = New-Object System.Collections.Generic.List[object]
  FrameLevelChanged = $false
  FloorStableKey   = ''
  FloorStableCount = 0
  TmpPng      = Join-Path $env:TEMP ('br-logger-' + $session + '.png')
  # ---- 配色自動校正分區用的狀態 ----
  FloorId         = ''
  CacheFloor      = ''
  LastScanAt      = $null
  Scans           = 0
  ScannedKeys     = @{}
  CalWarned       = @{}
  LastMeasured    = $null
  AutoRegionUses  = 0
  AutoRegionLogged = $false
  LastLevelName   = ''
  # ---- 特殊版面偵測用的狀態 ----
  Layout          = 'normal'
  LastLayout      = ''
}

function Write-Log {
  param($S, [string]$Msg)
  $line = '[' + (Get-Date).ToString('HH:mm:ss') + '] ' + $Msg
  [System.IO.File]::AppendAllText($S.Files.Log, $line + [Environment]::NewLine, $S.Utf8Bom)
  if (-not $Quiet) { Write-Host $line -ForegroundColor DarkGray }
}

function Add-Note {


  param($S, [string]$Msg)
  $S.Events.Add((New-LogEvent -K 'X' -V ('> （' + (Get-Date).ToString('HH:mm:ss') + ' ' + $Msg + '）') -T ([datetime]::Now)))
  Write-WikiFiles -S $S
}



if (-not $Quiet) {
  Write-Host ''
  Write-Host '  狐霖的後室 RPG — 畫面文字記錄器（Wiki 版）' -ForegroundColor Yellow
  Write-Host ('  視窗      ：' + $win.Title) -ForegroundColor Gray
  Write-Host ('  OCR 語言  ：' + $Language) -ForegroundColor Gray
  Write-Host ('  OCR 放大  ：' + $OcrScale + ' 倍') -ForegroundColor Gray
  Write-Host ('  OCR 分區  ：' + $(if ($NoRegion) { '關（整張 OCR）' } else { '開（title/main/opts/char，座標可在 _drafts\截圖分區座標.md 查；-NoRegion 可關）' })) -ForegroundColor Gray
  Write-Host ('  OCR 錯字  ：' + $(if ($NoOcrFix) { '關（-NoOcrFix）' } else { '開（' + $script:OcrFixRules.Count + ' 條規則；-NoOcrFix 可關）' })) -ForegroundColor Gray
  Write-Host ('  特殊版面  ：自動偵測 backpack／ending／panel（改用整張 OCR，幀標頭記 版面=…）') -ForegroundColor Gray
  Write-Host ('  視窗鎖定  ：' + $(if ($NoResize) { '關' } else { $WinSize + ' @ ' + $WinPos })) -ForegroundColor Gray
  Write-Host ('  每 ' + $Interval + ' 秒抓一張，按 Ctrl+C 停止') -ForegroundColor Gray
  Write-Host ('  輸出      ：' + $sessionDir) -ForegroundColor Gray
  Write-Host ''
}

  # ---- 開始記錄前：強制視窗尺寸（固定尺寸才能用固定座標分區裁切）----
  if (-not $NoResize) {
    $fr = Force-WindowSize -Top $win.Hwnd -Size $WinSize -Pos $WinPos
    if ($fr) {
      if ($fr.Exact) {
        Write-Host ('  視窗尺寸  ：' + $fr.W + 'x' + $fr.H + ' （已強制）') -ForegroundColor Gray
      } else {
        Write-Host ('  ⚠ 視窗尺寸：實際 ' + $fr.W + 'x' + $fr.H + '，要求 ' + $fr.WantW + 'x' + $fr.WantH + '（螢幕放不下會被夾住）') -ForegroundColor Yellow
      }
      Start-Sleep -Milliseconds 2500   # 等 Unity 重新排版畫布
    }
  } else {
    Write-Host '  視窗尺寸  ：未調整（-NoResize）' -ForegroundColor Gray
  }

Write-Log $state ('啟動。視窗=' + $win.Title + ' pid=' + $win.Pid + ' OCR=' + $Language + ' 間隔=' + $Interval + ' 秒；已清掉舊截圖 ' + $purgedFrames + ' 張')
Write-Log $state ('配色快取：' + $script:FloorCache.Floors.Count + ' 層配色、' + $script:FloorCache.Regions.Count + ' 筆邊界（種子 ' + $colorInit.Seeds + '、快取檔 ' + $colorInit.FromCache + '、舊場次 log ' + $colorInit.FromLogs + '）→ ' + $script:ColorCachePath)
if (-not $Quiet) {
  Write-Host ('  配色快取  ：' + $script:FloorCache.Floors.Count + ' 層（種子 ' + $colorInit.Seeds + '、快取 ' + $colorInit.FromCache + '、舊 log ' + $colorInit.FromLogs + '）' + $(if ($AutoRegion) { '；-AutoRegion 開' } else { '；-AutoRegion 關（只警告）' })) -ForegroundColor Gray
}

# ---- 邊錄邊認：背景 OCR worker（C）----
# 錄製期間就把新出現的裁圖丟給 RapidOCR，收工時 font-correct 只要套結果。
# worker 掛掉不影響錄製：收工後 font-correct 會退回批次路徑。
if ($lineDir) {
  try {
    # 這一行同時是「裁圖篩選」的自檢：字數 0 就代表找不到 char-freq.json，
    # 那所有行都會被判定成「不用重認」，RapidOCR 等於白開。
    Initialize-KnownChars
    Write-Log $state ('可疑字表：' + $script:KnownChars.Count + ' 字（0 = 沒載入，重認會失效）')
    $workerJs = Join-Path $PSScriptRoot 'structured\ocr-worker.mjs'
    $nodeExe = $null
    try { $nodeExe = (Get-Command node.exe -ErrorAction Stop).Source } catch { $nodeExe = $null }
    if (-not $nodeExe) {
      $probe = Join-Path $env:ProgramFiles 'nodejs\node.exe'
      if (Test-Path -LiteralPath $probe) { $nodeExe = $probe }
    }
    if ($nodeExe -and (Test-Path -LiteralPath $nodeExe) -and (Test-Path -LiteralPath $workerJs)) {
      Remove-Item -LiteralPath $ocrStopFlag -Force -ErrorAction SilentlyContinue
      Remove-Item -LiteralPath $ocrAckFile -Force -ErrorAction SilentlyContinue
      $ocrWorker = Start-Process -FilePath $nodeExe -ArgumentList @('"' + $workerJs + '"', '"' + $sessionDir + '"') -WindowStyle Hidden -PassThru
      Write-Log $state ('並行 OCR worker 已啟動（pid=' + $ocrWorker.Id + '）')
    } else {
      Write-Log $state '找不到 node 或 ocr-worker.mjs：改用收工批次 OCR'
    }
  } catch {
    $ocrWorker = $null
    Write-Log $state ('並行 OCR worker 啟動失敗（改用收工批次）：' + $_.Exception.Message)
  }
}

# ---- 分區 OCR 設定（$RegionMode 預設開；-NoRegion 才關）----
$RegionMode = -not $NoRegion
$regionSpecs = @()
if ($RegionMode) {
  $regionSpecs = Get-RegionSpecs
  $bad = @()
  foreach ($sp in $regionSpecs) {
    $t = @(([string]$sp.Spec -split '[,\s]+') | Where-Object { $_ -ne '' })
    if ($t.Count -ne 4) { $bad += ($sp.Name + '(' + $sp.Spec + ')') }
    else {
      foreach ($v in $t) { $d = 0.0; if (-not [double]::TryParse($v, [ref]$d)) { $bad += ($sp.Name + '(' + $sp.Spec + ')'); break } }
    }
  }
  if ($bad.Count -gt 0) {
    Write-Host ('  ⚠ 分區座標格式錯誤（要 x0,x1,y0,y1）：' + ($bad -join '、') + ' → 這一場改用整張 OCR') -ForegroundColor Yellow
    Write-Log $state ('分區座標格式錯誤（' + ($bad -join '、') + '），改用整張 OCR')
    $RegionMode = $false
  }
}
Write-Log $state ('分區 OCR=' + $RegionMode + $(if ($RegionMode) { '；' + (($regionSpecs | ForEach-Object { $_.Name + '=' + $_.Spec }) -join ' ') + '；下限=' + $RegionMinLines + ' 行' } else { '' }) + '；配色門檻=' + $ColorThreshold)
# 「這一場真正會用的座標」：已實測過的 render 尺寸會用實測值，而不是上面那組基準參數。
# 印出來才不會讓人以為「參數沒生效」（也方便事後追為什麼某一區抓到／漏掉東西）。
if ($RegionMode) {
  $prEff = Get-PageRect -Top $win.Hwnd
  if ($prEff -and $prEff.W -gt 60 -and $prEff.H -gt 60) {
    $effText = @()
    foreach ($sp in $regionSpecs) {
      $rr2 = ConvertTo-RegionRect -Spec $sp.Spec -CropW $prEff.W -CropH $prEff.H -Name ([string]$sp.Name)
      $effText += ($sp.Name + '=' + $(if ($rr2) { Format-RegionRect -Rect $rr2 } else { '無效' }))
    }
    Write-Log $state ('分區座標（實際 render ' + $prEff.W + 'x' + $prEff.H + '）：' + ($effText -join ' '))
    if (-not $Quiet) { Write-Host ('  實際座標  ：' + ($effText -join '　')) -ForegroundColor DarkGray }
  }
}
Write-WikiFiles -S $state
# 逐層分檔：開場先決定「目前樓層」檔（第一層通常是大廳 00），之後由樓層標題換檔
Set-InitialLevelLog -S $state
Write-Log $state ('逐層分檔：' + $state.Files.LevelsDir + '（目前：' + $state.CurrentLevelFile + '）；跨樓層事件：' + $state.Files.Transitions)


$pageRect = $null
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
  while ($true) {
    if ($StopFile -and (Test-Path -LiteralPath $StopFile)) {
      Remove-Item -LiteralPath $StopFile -Force -ErrorAction SilentlyContinue
      Write-Log $state '收到停止訊號，收工'
      # 收工前把背景 OCR 佇列清乾淨（C）：留 flag 讓 worker 收尾，最多等 90 秒。
      # 逾時就交給收工後的批次路徑，不會卡住流程。
      if ($ocrWorker) {
        try {
          New-Item -ItemType File -Force -Path $ocrStopFlag | Out-Null
          $ocrDeadline = (Get-Date).AddSeconds(90)
          while ((Get-Date) -lt $ocrDeadline -and -not (Test-Path -LiteralPath $ocrAckFile)) {
            Start-Sleep -Milliseconds 400
          }
          if (Test-Path -LiteralPath $ocrAckFile) {
            $rapidLines = 0
            $rp = Join-Path $sessionDir 'rapid.jsonl'
            if (Test-Path -LiteralPath $rp) {
              try { $rapidLines = ([System.IO.File]::ReadAllLines($rp)).Count } catch { }
            }
            Write-Log $state ('並行 OCR 收尾完成：' + $rapidLines + ' 行')
          } else {
            Write-Log $state '並行 OCR 收尾逾時：剩下的交給收工批次處理'
          }
        } catch {
          Write-Log $state ('並行 OCR 收尾失敗（交給收工批次）：' + $_.Exception.Message)
        }
      }
      break
    }
    $loopStart = [DateTime]::Now
    try {
      if (-not [BrWin]::IsWindow($win.Hwnd) -or -not [BrWin]::IsWindowVisible($win.Hwnd)) {
        $newWin = Find-GameWindow -Hwnd 0 -TitleMatch $TitleMatch
        if ($newWin) {
          $win = $newWin
          $state.WindowTitle = $newWin.Title
          $state.WindowPid = $newWin.Pid
          Write-Log $state ('重新鎖定視窗：' + $newWin.Title)
          Add-Note $state ('重新鎖定視窗：' + $newWin.Title)
        } else {
          Start-Sleep -Milliseconds 1500
          continue
        }
      }

      $title = [BrWin]::Title($win.Hwnd)
      if ($title -notmatch $TitleMatch) {
        if ($state.LastState -ne 'othertab') {
          $state.LastState = 'othertab'
          Write-Log $state ('遊戲分頁不在前景（目前標題：' + $title + '）→ 暫停記錄')
          Add-Note $state '遊戲分頁不在前景，暫停記錄'
        }
        Start-Sleep -Milliseconds ([Math]::Max(300, $Interval * 1000))
        continue
      }
      if ($state.LastState -eq 'othertab') { Add-Note $state '回到遊戲分頁，繼續記錄' }
      $state.LastState = 'game'

      if ([BrWin]::IsIconic($win.Hwnd)) {
        Start-Sleep -Milliseconds ([Math]::Max(300, $Interval * 1000))
        continue
      }

      # ---- 截圖 ----
      $pageRect = Get-PageRect -Top $win.Hwnd
      $bmp = Get-WindowImage -Top $win.Hwnd
      if ($null -eq $bmp) {
        Write-Log $state '截圖失敗（PrintWindow 回傳失敗），跳過這一幀'
        Start-Sleep -Milliseconds 1000
        continue
      }

      $crop = $null
      try {
        $rect = New-Object System.Drawing.Rectangle($pageRect.X, $pageRect.Y, $pageRect.W, $pageRect.H)
        $rect.Intersect((New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)))
        if ($rect.Width -lt 50 -or $rect.Height -lt 50) { throw ('裁切範圍不合理：' + $rect.Width + 'x' + $rect.Height) }
        $crop = $bmp.Clone($rect, $bmp.PixelFormat)
      } finally {
        $bmp.Dispose()
      }

      # ---- OCR ----
      # 分區裁切一定在放大「之前」；每區自己放大 → 自己 OCR
      $origW = $crop.Width; $origH = $crop.Height
      $state.FrameNo++
      $rawFrame = New-Object System.Collections.Generic.List[object]
      $ocrW = 0; $ocrH = 0
      $regRect = ConvertTo-RegionRect -Spec $RegionMain -CropW $origW -CropH $origH -Name 'main'
      $fillInfo = Get-MainFillColor -Bmp $crop -MainRect $regRect
      $regSample = Get-BgSampleRect -MainRect $regRect -Sample $ColorSample -Bmp $crop -FillHex $(if ($fillInfo) { $fillInfo.Hex } else { '' })
      $bg = Get-RectAverageColor -Bmp $crop -Rect $regSample
      $colorHex = '?'
      if ($bg) { $colorHex = $bg.Hex }
      $state.LastBg = $bg   # 逐層分檔用：樓層標題讀不到時靠底色問「這是不是另一層」
      # ---- 版面偵測（第一階段：顏色）----
      # 結局版面是整片近黑（#101010），用「底色」就能先認出來 —— 這種幀連配色自動校正
      # 的掃描都要跳過（掃一個不存在的面板只會量到垃圾邊界、還會污染邊界快取）。
      $layout = 'normal'
      if (Test-NearBlackScreen -Bmp $crop) { $layout = 'ending' }

      # 1600x900 是客戶端實際 render；1696x1300 的比例換算會把 main 上緣算太低，
      # 導致 enemy 蓋到故事、main/opts 重疊。這裡直接給這個尺寸的 normal 實測值。
      # 戰鬥時左上有敵人卡（名稱／HP／防禦），main 要從卡片下方開始；先 OCR 一小塊
      # 選項區判斷是不是戰鬥，避免每幀都做整張探測。
      $isCombat = $false
      if ($origW -eq 1600 -and $origH -eq 900 -and $layout -eq 'normal') {
        try {
          $probeSpec = @([pscustomobject]@{ Name = 'probe'; Spec = '21,339,880,1011' })
          $probe = Invoke-RegionOcr -Crop $crop -Engine $ocrEngine -Scale $OcrScale -TmpPng $state.TmpPng -Specs $probeSpec
          $probeLines = @()
          if ($null -ne $probe.Lines) { $probeLines = [object[]]$probe.Lines }
          $probeText = (($probeLines | ForEach-Object { [string]$_.Text }) -join ' ')
          if ($probeText -match '右手|撤退|切换|使用物品|战利品|攻击|攻擊|技能|防御|防禦|閃躲|闪躲|逃跑|回合|选择行动|選擇行動') {
            $isCombat = $true
          }
        } catch { Write-Log $state ('combat probe error: ' + $_.Exception.Message) }
      }

      $regionOverride = $false
      if (-not $AutoRegion -and $origW -eq 1600 -and $origH -eq 900 -and $layout -eq 'normal') {
        # 這些是 1696x1300 基準座標（ConvertTo-RegionRect 會再換算成 1600x900）
        $RegionTitle = '10,705,0,95'          # 只到黃色出口進度條左側（x≈665）
        if ($isCombat) {
          $RegionEnemy = '16,201,101,282'      # 左上敵人卡（x15..190, y70..195）
          $RegionMain = '21,1087,289,880'      # 卡片下方 y200..609
        } else {
          $RegionEnemy = '10,138,61,130'       # 只有「第 N 層｜岔路」這條
          $RegionMain = '21,1087,116,880'      # 故事 y80..609
        }
        $RegionOpts = '21,1087,880,1281'
        $RegionChar = '-604,-5,95,217'
        $regionSpecs = Get-RegionSpecs
        $state.LastMeasured = $null
        $regionOverride = $true
      }

      # ---- 配色自動校正分區（要在 OCR 之前算：-AutoRegion 會換掉本幀要用的座標）----
      $effMain = $RegionMain; $effOpts = $RegionOpts
      $frameSpecs = $regionSpecs
      if ($RegionMode -and $bg -and $layout -eq 'normal' -and -not $regionOverride) {
        try {
          $rr = Resolve-FrameRegion -S $state -Bmp $crop -Bg $bg -W $origW -H $origH -FillHex $(if ($fillInfo) { $fillInfo.Hex } else { '' })
          if ($rr) {
            $state.FloorId = [string]$rr.Floor
            if ($rr.Main) { $effMain = [string]$rr.Main }
            if ($rr.Opts) { $effOpts = [string]$rr.Opts }
          }
        } catch {
          Write-Log $state ('配色自動校正失敗（忽略，用原參數）：' + $_.Exception.Message)
        }
        if ($effMain -ne $RegionMain -or $effOpts -ne $RegionOpts) {
          $frameSpecs = Get-RegionSpecs -Main $effMain -Opts $effOpts
        }
      }

      # ---- 小角度去斜（-Deskew）：整張畫面轉正後才 OCR，分區座標一起換算 ----
      # 放在這裡的理由：配色／自動校正都要看「原始截圖」（旋轉會動到面板邊界量測），
      # 所以順序是「先量底色、先校正分區」→ 再決定要不要轉正 → 才 OCR。
      $deskewAngle = 0.0
      $deskewInfo = $null
      if ($Deskew -and $layout -eq 'normal') {
        try {
          $deskewInfo = Measure-ImageSkew -Bmp $crop -X0 $regRect.Left -Y0 $regRect.Top `
            -W $regRect.Width -H $regRect.Height -Dw 600
          if ($deskewInfo.Apply -and [Math]::Abs($deskewInfo.Angle) -ge $DeskewMinDeg -and [Math]::Abs($deskewInfo.Angle) -le $DeskewMaxDeg) {
            $rot2 = Rotate-ImageDeskew -Bmp $crop -Deg $deskewInfo.Angle
            if ($rot2) {
              $off2 = Get-DeskewOffset -W $origW -H $origH -Deg $deskewInfo.Angle -Dx 0 -Dy 0
              $nspec = New-Object System.Collections.Generic.List[object]
              foreach ($sp in $frameSpecs) {
                $rr2 = ConvertTo-RegionRect -Spec $sp.Spec -CropW $origW -CropH $origH -Name ('' + $sp.Name)
                $nr2 = $null
                if ($rr2) { $nr2 = ConvertTo-DeskewedRect -Rect $rr2 -W $origW -H $origH -Deg $deskewInfo.Angle -Offset $off2 }
                if ($nr2) {
                  $nspec.Add([pscustomobject]@{ Name = [string]$sp.Name; Spec = ('' + $nr2.Left + ',' + $nr2.Right + ',' + $nr2.Top + ',' + $nr2.Bottom) })
                } else {
                  $nspec.Add($sp)    # 換算失敗就沿用原座標，不要讓這一幀整個爆掉
                }
              }
              $crop.Dispose()
              $crop = $rot2
              $rot2 = $null
              $frameSpecs = $nspec
              $deskewAngle = $deskewInfo.Angle
              Write-Log $state ('小角度去斜：轉正 ' + [Math]::Round($deskewInfo.Angle, 2) + '°（corr=' + [Math]::Round($deskewInfo.Corr, 3) + '，第二尺度=' + [Math]::Round($deskewInfo.Angle2, 2) + '°）')
            }
          } elseif ($deskewInfo) {
            Write-Log $state ('去斜偵測：不套用（' + $deskewInfo.Reason + '，主尺度=' + [Math]::Round($deskewInfo.Angle, 2) + '° corr=' + [Math]::Round($deskewInfo.Corr, 3) + '）')
          }
        } catch {
          Write-Log $state ('去斜失敗（忽略，用原始畫面 OCR）：' + $_.Exception.Message)
        }
      }

      # ---- 便宜的特殊版面預檢（只 OCR 標題列那一小條）----
      # 標準畫面的標題列一定有「Level N「…」」；看不到就代表這不是標準畫面
      # （背包／結局／三欄面板都會蓋掉它），改用整張 OCR。這樣正常幀只多花一次
      # 小區塊 OCR（約 1300x88），不會變成「每幀都整張 OCR 兩次」。
      $isSpecialScreen = $false
      # 2026-10-27：大型標題字的 OCR 常失敗，正常畫面不能因此每幀退回整張 OCR。
      if ($false -and $layout -eq 'normal' -and $RegionMode) {
        $probeRect = ConvertTo-RegionRect -Spec $RegionTitle -CropW $origW -CropH $origH -Name 'title'
        if ($probeRect) {
          $isSpecialScreen = $true
          $pbm = $null; $psc = $null
          try {
            $pbm = $crop.Clone($probeRect, $crop.PixelFormat)
            $psc = Scale-Image $pbm $OcrScale
            $pbm = $null
            Remove-Item -LiteralPath $state.TmpPng -Force -ErrorAction SilentlyContinue
            $psc.Save($state.TmpPng, [System.Drawing.Imaging.ImageFormat]::Png)
            $pk = [Math]::Round($psc.Width / [double]$probeRect.Width, 4)
            $plines = Invoke-FrameOcr -PngPath $state.TmpPng -Engine $ocrEngine -Scale $pk
            $pl = New-Object System.Collections.Generic.List[object]
            foreach ($l in $plines) { $pl.Add([pscustomobject]@{ Text = $l.Text; Region = 'title'; X = $l.X; Y = $l.Y; W = $l.W; H = $l.H }) }
            if (-not (Test-SpecialScreen -Lines $pl)) { $isSpecialScreen = $false }
          } catch {
            $isSpecialScreen = $false    # 預檢失敗就當成正常畫面，走原本的分區流程
          } finally {
            if ($pbm) { $pbm.Dispose() }
            if ($psc) { $psc.Dispose() }
            Remove-Item -LiteralPath $state.TmpPng -Force -ErrorAction SilentlyContinue
          }
        }
      }
      if ($isSpecialScreen) {
        Write-Log $state ('版面預檢：標題列看不到「Level N「…」」→ 這不是標準遊玩畫面，改用整張 OCR')
      }

      # ---- 幀級去重（B）：畫面完全沒變就不重跑 OCR ----
      # 實測 42%~76% 的幀跟上一幀一模一樣，卻還是重跑分區 OCR + 重寫裁圖。
      # 這裡用整張圖的 SHA1 判斷；相同就沿用上一幀的行（含裁圖名），
      # structured.jsonl 照樣寫這一幀，所以敵名連 2 幀投票那些邏輯不受影響。
      $frameHash = ''
      try {
        $rectAll = New-Object System.Drawing.Rectangle(0, 0, $crop.Width, $crop.Height)
        $fm = $crop.LockBits($rectAll, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $crop.PixelFormat)
        try {
          $len = [Math]::Abs($fm.Stride) * $crop.Height
          $buf = New-Object byte[] $len
          [System.Runtime.InteropServices.Marshal]::Copy($fm.Scan0, $buf, 0, $len)
          $sha = [System.Security.Cryptography.SHA1]::Create()
          try { $frameHash = ([BitConverter]::ToString($sha.ComputeHash($buf))).Replace('-', '').Substring(0, 16) } finally { $sha.Dispose() }
        } finally { $crop.UnlockBits($fm) }
      } catch { $frameHash = '' }
      $reuseFrame = ($layout -eq 'normal' -and $frameHash -ne '' -and $frameHash -eq $state.LastImgHash -and $state.LastRawFrame -and $state.LastRawFrame.Count -gt 0)
      if ($reuseFrame) {
        foreach ($l in $state.LastRawFrame) {
          $rawFrame.Add([pscustomobject]@{ Text = $l.Text; Region = $l.Region; X = $l.X; Y = $l.Y; W = $l.W; H = $l.H; Crop = $l.Crop })
        }
        $ocrW = $origW; $ocrH = $origH
        $state.DupFrames++
      }
      # ---- 分區 OCR（特殊版面直接整張；正常版面才用分區）----
      elseif ($layout -ne 'normal' -or $isSpecialScreen) {
        $full = $null
        try {
          $full = Scale-Image $crop.Clone() $OcrScale
          Remove-Item -LiteralPath $state.TmpPng -Force -ErrorAction SilentlyContinue
          $full.Save($state.TmpPng, [System.Drawing.Imaging.ImageFormat]::Png)
          $k = [Math]::Round($full.Width / [double]$origW, 4)
          $lines = Invoke-FrameOcr -PngPath $state.TmpPng -Engine $ocrEngine -Scale $k
          $ocrW = $full.Width; $ocrH = $full.Height
          foreach ($l in $lines) { $rawFrame.Add([pscustomobject]@{ Text = $l.Text; Region = 'full'; X = $l.X; Y = $l.Y; W = $l.W; H = $l.H }) }
        } finally {
          if ($full) { $full.Dispose() }
          Remove-Item -LiteralPath $state.TmpPng -Force -ErrorAction SilentlyContinue
        }
      } elseif ($RegionMode) {
        $rg = Invoke-RegionOcr -Crop $crop -Engine $ocrEngine -Scale $OcrScale -TmpPng $state.TmpPng -Specs $frameSpecs -LineDir $state.LineDir -FrameNo $state.FrameNo
        foreach ($l in $rg.Lines) { $rawFrame.Add($l) }
        $ocrW = $origW; $ocrH = $origH
        # Fallback：總行數太少、或有區塊失敗 → 改用整張 OCR
        $needFull = ($rg.Lines.Count -lt $RegionMinLines) -or ($rg.Failed -gt 0)
        $why = ''
        if ($needFull) {
          $why = '某區 OCR 失敗'
          if ($rg.Lines.Count -lt $RegionMinLines) { $why = ('分區只抓到 ' + $rg.Lines.Count + ' 行（<' + $RegionMinLines + '）') }
        }
        if ($needFull) {
          $ok = $false
          $ok = $false
          try {
            # 2026-10-30：正常遊玩畫面改成「只認故事面板那一塊」。
            # 以前退回整張 OCR，連「背包（B）」「設定（靜音）」「Level 37.1「深水區」」
            # 都一起掃進來，這些行最後會被當成事件內容寫進 Wiki。
            if ($layout -eq 'normal' -and $regRect -and $regRect.Width -gt 40 -and $regRect.Height -gt 40) {
              $mainBmp = $crop.Clone($regRect, $crop.PixelFormat)
              $mainScaled = $null
              $full = $null
              try {
                $mainScaled = Scale-Image $mainBmp $OcrScale   # 會 Dispose mainBmp
                $mainBmp = $null
                Remove-Item -LiteralPath $state.TmpPng -Force -ErrorAction SilentlyContinue
                $mainScaled.Save($state.TmpPng, [System.Drawing.Imaging.ImageFormat]::Png)
                $k = [Math]::Round($mainScaled.Width / [double]$regRect.Width, 4)
                $lines = Invoke-FrameOcr -PngPath $state.TmpPng -Engine $ocrEngine -Scale $k
                $ocrW = $mainScaled.Width; $ocrH = $mainScaled.Height
                $newFrame = New-Object System.Collections.Generic.List[object]
                foreach ($l in $lines) {
                  $newFrame.Add([pscustomobject]@{
                      Text = $l.Text; Region = 'main'
                      X = [int]($l.X + $regRect.Left); Y = [int]($l.Y + $regRect.Top); W = $l.W; H = $l.H
                    })
                }
                $rawFrame = $newFrame
                $ok = $true
                $why = $why + '｜只認故事面板 ' + $newFrame.Count + ' 行'
              } finally { if ($mainScaled) { $mainScaled.Dispose() } }
            } else {
              # 注意：一定要 Clone，因為 Scale-Image 在放大時會把傳進去的圖 Dispose 掉
              $full = Scale-Image $crop.Clone() $OcrScale
              Remove-Item -LiteralPath $state.TmpPng -Force -ErrorAction SilentlyContinue
              $full.Save($state.TmpPng, [System.Drawing.Imaging.ImageFormat]::Png)
              $k = [Math]::Round($full.Width / [double]$origW, 4)
              $lines = Invoke-FrameOcr -PngPath $state.TmpPng -Engine $ocrEngine -Scale $k
              $ocrW = $full.Width; $ocrH = $full.Height
              $newFrame = New-Object System.Collections.Generic.List[object]
              foreach ($l in $lines) { $newFrame.Add([pscustomobject]@{ Text = $l.Text; Region = 'full'; X = $l.X; Y = $l.Y; W = $l.W; H = $l.H }) }
              # 整張 OCR 的結果才看得到版面特徵字 → 用它再判一次（背包／三欄表格）
              [void](Test-RegionTextLayout -Lines ([object[]]$newFrame) -Layout ([ref]$layout))
              $rawFrame = $newFrame
              $ok = $true
            }
          } finally { if ($full) { $full.Dispose() } }   # $full 是自己的複本，可以安心 Dispose
          if (-not $ok) { throw ('分區與整張 OCR 都失敗（' + $why + '）') }
          $state.Fallbacks++
          Write-Log $state ('⚠ 分區 OCR 退回整張 OCR：' + $why + '｜版面=' + $layout + '｜分區=' + $rg.Detail + '｜整張=' + $rawFrame.Count + ' 行')
          Write-Host ('  ⚠ 分區 OCR 退回整張 OCR（' + $why + '，版面=' + $layout + '）') -ForegroundColor Yellow
        } else {
          if (-not $Quiet) { Write-Host ('  [分區] ' + $rg.Detail + ' 共 ' + $rg.Lines.Count + ' 行') -ForegroundColor DarkGray }
        }
      } else {
        # 注意：Scale-Image 放大時會 Dispose 傳進去的圖，所以只有「放大後」才能 Dispose
        $full = Scale-Image $crop $OcrScale
        try {
          Remove-Item -LiteralPath $state.TmpPng -Force -ErrorAction SilentlyContinue
          $full.Save($state.TmpPng, [System.Drawing.Imaging.ImageFormat]::Png)
          $k = [Math]::Round($full.Width / [double]$origW, 4)
          $lines = Invoke-FrameOcr -PngPath $state.TmpPng -Engine $ocrEngine -Scale $k
          $ocrW = $full.Width; $ocrH = $full.Height
          foreach ($l in $lines) { $rawFrame.Add([pscustomobject]@{ Text = $l.Text; Region = 'full'; X = $l.X; Y = $l.Y; W = $l.W; H = $l.H }) }
        } finally { if (-not [object]::ReferenceEquals($full, $crop)) { $full.Dispose() } }
        [void](Test-RegionTextLayout -Lines ([object[]]$rawFrame) -Layout ([ref]$layout))
        if ($layout -eq 'normal' -and ((Join-LineText -Lines ([object[]]$rawFrame)) -match '跳过|跳過')) { $layout = 'ending' }
      }
      # 記住這一幀，下一幀如果一模一樣就直接沿用（B）
      if (-not $reuseFrame) {
        $state.LastImgHash = $frameHash
        $state.LastRawFrame = @($rawFrame | ForEach-Object {
            [pscustomobject]@{ Text = $_.Text; Region = $_.Region; X = $_.X; Y = $_.Y; W = $_.W; H = $_.H; Crop = $_.Crop }
          })
      }
      # 版面變化記一筆（特殊版面是「內容抓法變了」，事後看 log 才知道哪一幀用了哪種抓法）
      if ($layout -ne $state.LastLayout) {
        Write-Log $state ('版面=' + $layout + $(if ($state.LastLayout) { '（上一幀 ' + $state.LastLayout + '）' } else { '' }) + '：' + $(switch ($layout) {
              'backpack' { '背包與裝備面板 → 改用整張 OCR，行標記 [full]，背包行另標 [item]' }
              'ending'   { '全螢幕結局（近黑）→ 改用整張 OCR，行標記 [full]' }
              'panel'    { '彈窗三欄面板 → 改用整張 OCR，行標記 [full]' }
              default    { '標準遊玩畫面' }
            }))
        $state.LastLayout = $layout
      }
      if ($layout -ne 'normal' -and -not $Quiet) { Write-Host ('  版面：' + $layout + '（改用整張 OCR）') -ForegroundColor Cyan }
      if ($SaveFrames) {
        # 留一張原尺度的圖（校準／事後比對用；預設不留）
        New-Item -ItemType Directory -Force -Path $framesDir | Out-Null
        $state.FrameSeq++
        $name = '{0}-{1:d3}.jpg' -f (Get-Date -Format 'HHmmss'), $state.FrameSeq
        Save-Jpeg -Bmp $crop -Path (Join-Path $framesDir $name)
        $all = @(Get-ChildItem -Path $framesDir -Filter '*.jpg' | Sort-Object Name)
        if ($KeepFrames -gt 0 -and $all.Count -gt $KeepFrames) {
          $all | Select-Object -First ($all.Count - $KeepFrames) | ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
        }
      }
      $state.Frames++
      $lines = $rawFrame

      # 不做長度／寬度裁切判斷：主文段落長短差很多，任何長度門檻都會誤刪正文。
      $imgW = $origW
      $state.LastRawFrame = @($rawFrame | ForEach-Object {
          [pscustomobject]@{ Text = $_.Text; Region = $_.Region; X = $_.X; Y = $_.Y; W = $_.W; H = $_.H; Crop = $_.Crop }
        })

      # 給客戶端浮層的即時狀態（只有區塊與 OCR 行；不進正式紀錄）
      Write-OverlayState -S $state -FrameNo $state.FrameNo -W $origW -H $origH -Layout $layout `
        -ColorHex $colorHex -Specs $frameSpecs -Lines $rawFrame -Fallbacks $state.Fallbacks -Detail ''

      # ---- 樓層配色（自動記錄 ＋ 累積配色快取） ----
      if ($bg) {
        $titleLines = @($rawFrame | Where-Object { $_.Region -eq 'title' })
        $lvlName = Get-LevelName -Lines $titleLines
        # 快取用的樓層標題：優先「Level N「名稱」」那一行（title 常同時有場景標題）
        $floorText = Get-FloorTitleText -Lines $titleLines -Fallback $lvlName
        $changed = $false
        if ($null -eq $state.LastColor) { $changed = $true }
        elseif (-not (Test-ColorSame -A $state.LastColor -B $bg -Threshold $ColorThreshold)) { $changed = $true }
        if ($changed) {
          $state.LastColor = $bg
          # 有樓層名時順便量一次「牆色」（面板外側），Wiki 的頁面底色要用這個
          $wallHex = ''
          try {
            if ($lvlName) {
              $wall = Get-WallColor -Bmp $crop -MainRect $regRect
              if ($wall) { $wallHex = '  牆色=' + $wall.Hex }
            }
          } catch { }
          $rec = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $bg.Hex + '  ' + $bg.Rgb + '  ' + $lvlName + $wallHex + '  幀#' + $state.FrameNo
          [System.IO.File]::AppendAllText($state.Files.FloorColors, $rec + [Environment]::NewLine, $utf8Bom)
          if (-not $Quiet) { Write-Host ('  配色變化 → ' + $bg.Hex + '  ' + $lvlName) -ForegroundColor Magenta }
        }
        # 累積「樓層名 → 色碼」到快取（樓層名要 OCR 到才有得記；沒記到就先靠色碼）
        if ($floorText) {
          $ce = Add-FloorColorFromSample -Hex $bg.Hex -R $bg.R -G $bg.G -B $bg.B -LevelText $floorText -Source '自動偵測'
          if ($ce -and ([string]$ce.id -ne [string]$state.CacheFloor)) {
            $state.CacheFloor = [string]$ce.id
            [void](Save-FloorColorCache)
            Write-Log $state ('配色快取：' + $ce.id + '「' + $ce.name + '」= ' + $ce.hex + '（來源 ' + $ce.source + '、累積 ' + $ce.count + ' 次、共 ' + $script:FloorCache.Floors.Count + ' 層）')
          }
        }
        if ($lvlName -and $lvlName -ne $state.LastLevelName) {
          $state.LastLevelName = $lvlName
          $exp = ''
          if ($script:FloorCache.FloorsById[$state.FloorId]) { $exp = [string]$script:FloorCache.FloorsById[$state.FloorId].hex }
          Write-Log $state ('樓層：' + $lvlName + '　配色=' + $bg.Hex + $(if ($floorText -and $floorText -ne $lvlName) { '　樓層標題=' + $floorText } else { '' }) + $(if ($exp) { '　快取預期=' + $exp } else { '' }))
        }
      }

      # ---- 特殊版面：背包面板的行加 [item] 標記（只影響原始記錄的標記，不影響分類）----
      if ($layout -eq 'backpack') {
        foreach ($l in @($rawFrame | Where-Object { (Test-BackpackLine -T ([string]$_.Text)) })) {
          $l.Region = ([string]$l.Region) + ' [item]'
        }
      }

      # ---- 原始記錄（完整保留，不經過濾）----
      # 2026-10-30：與上一幀完全相同時不重寫 —— 內容一字不差，重寫只是把同一段文字再抄一遍。
      # （那場 8 小時停在主選單的 ocr-raw.log 就是這樣長到 45 MB 的。）
      if (-not $reuseFrame) {
        Write-RawFrame -Path $files.Raw -Bom $utf8Bom -FrameNo $state.FrameNo -ImgW $origW -ImgH $origH `
          -OcrW $ocrW -OcrH $ocrH -ColorHex $colorHex -Lines $rawFrame -Layout $layout `
          -Deskew $(if ($deskewAngle -ne 0.0) { [string][Math]::Round($deskewAngle, 2) + '度' } else { '' })
      }
      try {
        if ($reuseFrame -and $layout -eq 'normal') {
          if (-not $state.DupStart) { $state.DupStart = $state.FrameNo }
          $state.DupEnd = $state.FrameNo
        } else {
          if ($state.DupStart) {
            Write-StructuredDup -Path $files.Structured -From $state.DupStart -To $state.DupEnd -Layout 'normal' -ColorHex $colorHex
            $state.DupStart = 0
            $state.DupEnd = 0
          }
          Write-StructuredFrame -Path $files.Structured -FrameNo $state.FrameNo -ImgW $origW -ImgH $origH -Layout $layout -ColorHex $colorHex -Lines $rawFrame
        }
      } catch { }

      if ($Once) {
        foreach ($l in $rawFrame) { Write-Host ('  [' + $l.Region + '] y={0,4} x={1,4}  {2}' -f $l.Y, $l.X, $l.Text) }
        Write-Host ''
        Write-Host ('(單張測試：共 ' + $rawFrame.Count + ' 行，畫面 ' + $origW + 'x' + $origH + '，分區=' + $RegionMode + '，版面=' + $layout + '，配色=' + $colorHex + '，區塊=' + $pageRect.Page + ')') -ForegroundColor Yellow
        break
      }

      # ---- 逐層分檔：先判斷這一幀的樓層邊界（只影響 levels\ 額外產出）----
      # 樓層來源的優先序（事件流贏過標題區，因為標題區常混著場景行）：
      #   1) 事件流裡的 H 樓層標題行（在 Add-ClassifiedLine 裡處理）
      #   2) title 區讀到「有編號」的樓層標題（只有這一幀事件流沒有 H 時才採用）
      #   3) 配色快取的最接近色
      #   4) 都沒有 → 上一層延續（append 到原檔），絕不亂猜樓層
      $state.FrameLevelEvents = New-Object System.Collections.Generic.List[object]
      $state.FrameLevelChanged = $false
      $state.FrameHadHeader = $false
      $fromLvId = [string]$state.CurrentLevel
      $fromLvName = [string]$state.CurrentLevelName
      $titleText = ''
      if ($titleLines) {
        $titleText = Get-FloorTitleText -Lines $titleLines -Fallback ''
        if (-not $titleText) {
          foreach ($tl in @($titleLines)) { if ($script:RxLevelName.IsMatch([string]$tl.Text)) { $titleText = ([string]$tl.Text).Trim(); break } }
        }
        if (-not $titleText -and $lvlName) { $titleText = [string]$lvlName }
      }

      # ---- 分類 → 事件 ----
      $imgW = $origW
      $frameEvents = New-Object System.Collections.Generic.List[object]
      $optMode = $false
      $added = 0
      $prevLine = $null
      $prevSkipped = $false
      foreach ($l in @($rawFrame | Sort-Object Y, X)) {
        $sameLineRight = $prevLine -and ([Math]::Abs($prevLine.Y - $l.Y) -le 4) -and
          (($l.X - ($prevLine.X + $prevLine.W)) -ge -8) -and
          (($l.X - ($prevLine.X + $prevLine.W)) -le 36)
        $prevLine = $l
        # 左邊那截如果是重複／片段／UI 而被跳過，右邊同線的碎片也不能重新記一次。
        if ($sameLineRight -and $prevSkipped) { continue }
        $prevSkipped = $false
        if ($pageRect.Page -and $RxChromeNoise.IsMatch($l.Text)) { $prevSkipped = $true; continue }
        if ($RxPrompt.IsMatch($l.Text)) { $optMode = $true; $prevSkipped = $true; continue }
        if (-not $sameLineRight) {
          if (Test-Duplicate -S $state -Text $l.Text) { $prevSkipped = $true; continue }
          if (Test-Fragment -S $state -Text $l.Text) { Add-Recent -S $state -Text $l.Text; $prevSkipped = $true; continue }
        }
        $ev = Add-ClassifiedLine -S $state -Text $l.Text -X $l.X -Y $l.Y -Hh $l.H -ImgW $imgW -T ([datetime]::Now) -SameFrame $true -FrameEvents $frameEvents -Region ([string]$l.Region) -FrameNo $state.FrameNo -W $l.W
        if ($ev -is [string]) { Add-Recent -S $state -Text $l.Text; continue }
        if ($ev) {
          Add-Recent -S $state -Text $l.Text
          Add-Recent -S $state -Text $ev.V
          $fromOpts = ([string]$l.Region -eq 'opts')
          if ($optMode -and $ev.K -eq 'N' -and ($fromOpts -or $ev.V.Length -le 24)) { $ev.K = 'O' }
          if ($ev.K -ne 'N' -and $ev.K -ne 'O') { $optMode = $false }
          $added++
        } else { $prevSkipped = $true }
      }

      # 事件流這一幀【沒有】H 樓層標題行時，才讓 title 區當後備。
      # title 區常常停在上一層的標題（換層瞬間尤其明顯），所以只有「底色也同意」時才跟：
      #   * 標題講的樓層跟目前這層一致 → 用它補樓層名
      #   * 標題講的是別層 → 必須「那層在配色快取裡」而且「本幀底色最接近的也是那層」
      #     （＝換層瞬間底色已經換了；OCR 抖動不會同時騙過兩邊）
      if (-not $state.FrameHadHeader -and $titleText) {
        $tNum = Get-LevelNumberFromText -Text $titleText
        $curNum = Get-LevelNumberFromId -Id ([string]$state.CurrentLevel)
        $allowTitle = $false
        if ($tNum -and $curNum -and (Test-LevelNumSame -A $tNum -B $curNum)) { $allowTitle = $true }
        elseif ($tNum -and $bg -and $script:FloorCache) {
          # 快取鍵是「正規化後的編號」（Level 1），OCR 可能讀成 01 → 先對回快取
          $tFix = Resolve-FloorNumber -Num $tNum
          $tId = 'Level ' + $tFix
          if ($script:FloorCache.FloorsById[$tId]) {
            $colHit = Get-NearbyFloorName -Bg $bg -LevelIdNum $tNum
            if ($colHit) { $allowTitle = $true }
          }
        }
        if ($allowTitle) {
          $lvRes = Update-LevelFromText -S $state -Text $titleText -Bg $bg
          if ($lvRes -eq 'changed') {
            $state.FrameLevelChanged = $true
            $state.LastLevelFrame = $state.FrameNo
            Write-TransitionEntry -S $state -FrameNo $state.FrameNo -Ev $null -FromLevel $fromLvId -FromName $fromLvName -ToLevel $state.CurrentLevel -ToName $state.CurrentLevelName -Reason ('title 區樓層標題（後備，底色同意）：' + $titleText)
          }
        } elseif (-not $tNum) {
          Add-LevelUnknownNote -S $state -Text $titleText
        }
      }

      # 進入新樓層：有正式樓層名就用它；未知樓層（Level ????）用狀態列層號，
      # 不要因為 CurrentLevel 沒更新就完全不截圖。取樣後由 pipeline 刪除。
      $layerNo = ''
      foreach ($ll in $rawFrame) {
        $mm = [regex]::Match([string]$ll.Text, '第\s*(\d+)\s*[层層]')
        if ($mm.Success) { $layerNo = $mm.Groups[1].Value; break }
      }
      $newLayerNo = ($layerNo -and $layerNo -ne [string]$state.LastLayerNo)
      # 同一層連續 3 幀再截，避開換層黑屏／載入畫面
      $shotMainLines = @($rawFrame | Where-Object { $_.Region -eq 'main' }).Count
      if ($state.FloorShotDir -and $crop -and $state.LevelFromHeader -and ($layout -eq 'normal') -and ($shotMainLines -ge 4) -and ($state.CurrentLevel -or $layerNo)) {
        try {
          $shotKey = if ($state.CurrentLevel) { [string]$state.CurrentLevel } else { 'unknown-' + $layerNo }
          if (-not $state.FloorShotSeen.ContainsKey($shotKey)) {
            if ($state.FloorStableKey -eq $shotKey) { $state.FloorStableCount = [int]$state.FloorStableCount + 1 }
            else { $state.FloorStableKey = $shotKey; $state.FloorStableCount = 1 }
            if ([int]$state.FloorStableCount -ge 3) {
              $state.FloorShotSeen[$shotKey] = 1
              if ($state.CurrentLevel) {
                $safe = ([string]$state.CurrentLevel + '-' + [string]$state.CurrentLevelName)
              } else {
                $safe = ('unknown-' + $layerNo + '-layer')
              }
              $safe = $safe -replace '[\\/:*?"<>|]', '_'
              $sf = Join-Path $state.FloorShotDir ($safe + '-f' + $state.FrameNo + '.png')
              $crop.Save($sf, [System.Drawing.Imaging.ImageFormat]::Png)
            }
          }
          if ($layerNo) { $state.LastLayerNo = $layerNo }
        } catch { }
      }

      if ($added -gt 0) {
        foreach ($ev in $frameEvents) {
          $state.Events.Add($ev)
          $state.FrameLevelEvents.Add($ev)
          if (-not $Quiet) { Write-Host ('  [' + $ev.T.ToString('HH:mm:ss') + '] ' + (Format-EventLine $ev)) -ForegroundColor Green }
        }
        Write-WikiFiles -S $state
        Write-LevelFiles -S $state -FrameNo $state.FrameNo
      }

      if ($crop) { $crop.Dispose() }
      $crop = $null

      if ($state.Frames % 300 -eq 0) {
        [System.GC]::Collect()
        Write-Log $state ('進度：' + $state.Frames + ' 幀、' + $state.Events.Count + ' 個事件、已跑 ' + [int]$stopwatch.Elapsed.TotalMinutes + ' 分鐘')
      }
    } catch {
      $where = ''
      if ($_.InvocationInfo) { $where = ' @' + $_.InvocationInfo.ScriptLineNumber }
      Write-Log $state ('這一幀出錯（已跳過）：' + $_.Exception.Message + $where + [Environment]::NewLine + $_.ScriptStackTrace)
      if (-not $Quiet) { Write-Host ('  ✗ ' + $_.Exception.Message + $where) -ForegroundColor Red }
      Start-Sleep -Milliseconds 1000
    }

    if ($Once) { break }
    $used = ([DateTime]::Now - $loopStart).TotalMilliseconds
    $wait = [Math]::Max(250, ($Interval * 1000) - $used)
    Start-Sleep -Milliseconds $wait
  }
} finally {
  if ($Once) { exit 0 }
  if ($state.Events.Count -gt 0) { Write-WikiFiles -S $state }
  # 逐層分檔：把還沒落檔的事件補寫完（正常情況下每一幀都已經寫過，這裡只是保險）
  if ($state.FrameLevelEvents -and $state.FrameLevelEvents.Count -gt 0) { Write-LevelFiles -S $state -FrameNo $state.FrameNo }
  [void](Save-FloorColorCache)
  # 收工時補寫還沒關閉的靜止畫面區間
  if ($state.DupStart) {
    try { Write-StructuredDup -Path $state.Files.Structured -From $state.DupStart -To $state.DupEnd -Layout 'normal' -ColorHex '' } catch { }
    $state.DupStart = 0; $state.DupEnd = 0
  }
  try {
    Write-OverlayState -S $state -FrameNo $state.FrameNo -W ([int]$origW) -H ([int]$origH) `
      -Layout ([string]$state.LastLayout) -ColorHex '' -Specs $regionSpecs -Lines $state.LastRawFrame `
      -Fallbacks $state.Fallbacks -Detail 'stopped' -Running $false
  } catch { }
  Write-Log $state ('結束。共 ' + $state.Frames + ' 幀（其中 ' + $state.DupFrames + ' 幀與上一幀相同、已合併成區間）、' + $state.Events.Count + ' 個事件、自動校正掃描 ' + $state.Scans + ' 次' + $(if ($AutoRegion) { '、-AutoRegion 套用 ' + $state.AutoRegionUses + ' 次' } else { '' }))
  Remove-Item -LiteralPath $state.TmpPng -Force -ErrorAction SilentlyContinue
  if (-not $Quiet) {
    Write-Host ''
    Write-Host ('  Wiki 逐字稿：' + $files.Wiki) -ForegroundColor Yellow
    Write-Host ('  事件流    ：' + $files.Jsonl) -ForegroundColor Gray
    Write-Host ('  原始 OCR  ：' + $files.Raw) -ForegroundColor Gray
    Write-Host ('  逐層紀錄  ：' + $files.LevelsDir) -ForegroundColor Cyan
    Write-Host ('  跨樓層事件：' + $files.Transitions) -ForegroundColor Cyan
    Write-Host ''
  }
}
