# 默认流程：先编译最新源码，再复制到本机 app 并启动。
# 加 -NoBuild 可跳过编译，只启动现有 exe。
param(
  [switch]$NoBuild,
  [switch]$NoRecord,
  [switch]$NoOverlay,
  [string]$Url = 'https://hulin-backrooms-rpg.firebaseapp.com/',
  [string]$Preset = '1600x900',
  [string]$Profile = ''
)

$ErrorActionPreference = 'Stop'

# 面板正在运行时 exe 会被锁住，编译无法替换；先明确报错，避免启动旧版本。
if (-not $NoBuild) {
  $buildScript = Join-Path $PSScriptRoot 'build.ps1'
  $panel = Get-Process -Name 'ExpeditionRecorder' -ErrorAction SilentlyContinue
  if ($panel) {
    Write-Host '记录器面板正在运行。请先关闭面板，再运行 啟動.bat。' -ForegroundColor Yellow
    exit 1
  }
  if (-not (Test-Path -LiteralPath $buildScript)) {
    Write-Host ('找不到编译脚本：' + $buildScript) -ForegroundColor Red
    exit 1
  }
  Write-Host '正在编译最新版本…' -ForegroundColor Cyan
  try {
    & $buildScript
  } catch {
    Write-Host ('编译失败：' + $_.Exception.Message) -ForegroundColor Red
    Write-Host '已停止启动，避免运行旧版本。' -ForegroundColor Red
    exit 1
  }
}

$src = $PSScriptRoot
$dst = Join-Path $env:LOCALAPPDATA 'Expedition\app'

# 文件大小与写入时间都没变就跳过，避免每次启动都重拷几百 MB 的 structured。
function Test-SameCopy {
  param([string]$Source, [string]$Target)
  if (-not (Test-Path -LiteralPath $Target)) { return $false }
  try {
    $a = Get-Item -LiteralPath $Source
    $b = Get-Item -LiteralPath $Target
    return ($a.Length -eq $b.Length -and $a.LastWriteTimeUtc -eq $b.LastWriteTimeUtc)
  } catch { return $false }
}

New-Item -ItemType Directory -Force $dst | Out-Null
$copyFailed = @()
$logsPrefix = (Join-Path $src 'logs') + [IO.Path]::DirectorySeparatorChar
foreach ($f in Get-ChildItem $src -Recurse -File) {
  if ($f.FullName.StartsWith($logsPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
  if ($f.FullName.StartsWith((Join-Path $src 'dist') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { continue }
  if ($f.FullName.StartsWith((Join-Path $src 'Recorder') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { continue }
  if ($f.Name -match '\.cs$' -or $f.Name -in @('build.ps1','README.md','start.ps1','啟動.bat','啟動記錄器.bat')) { continue }
  if ($f.FullName -match '\\fonts\\') { continue }
  $rel = $f.FullName.Substring($src.Length + 1)
  $to = Join-Path $dst $rel
  if (Test-SameCopy -Source $f.FullName -Target $to) { continue }
  New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
  try {
    Copy-Item -LiteralPath $f.FullName -Destination $to -Force -ErrorAction Stop
  } catch {
    # 客户端/面板在执行时，DLL 或 exe 可能被锁住；跳过该档，不动正在运行的程序。
    $copyFailed += $f.Name
  }
}
if ($copyFailed.Count -gt 0) {
  Write-Host ('以下文件正在使用，这次跳过：' + ($copyFailed -join '、')) -ForegroundColor Yellow
  Write-Host '以上文件未更新；关闭对应程序后重新运行 啟動.bat。' -ForegroundColor Yellow
}

# 圓盤字型（Noto Sans TC）：第一次啟動複製到 app，之後跳過（字型檔較大）
$fontSrc = Join-Path $src 'fonts\NotoSansCJKtc-Regular.otf'
$fontDstDir = Join-Path $dst 'fonts'
$fontDst = Join-Path $fontDstDir 'NotoSansCJKtc-Regular.otf'
if ((Test-Path -LiteralPath $fontSrc) -and -not (Test-Path -LiteralPath $fontDst)) {
  New-Item -ItemType Directory -Force $fontDstDir | Out-Null
  try { Copy-Item -LiteralPath $fontSrc -Destination $fontDst -Force -ErrorAction Stop } catch { }
}

# 記錄器腳本的唯一來源在 Recorder。根目錄舊副本已移除，
# 這裡直接從來源覆蓋 app，避免「改了來源、start 卻抄到舊副本」。
foreach ($name in @('backroom-logger.ps1', 'palette-from-shots.ps1')) {
  $from = Join-Path $src ('Recorder\' + $name)
  if (-not (Test-Path $from)) { continue }
  try { Copy-Item -LiteralPath $from -Destination (Join-Path $dst $name) -Force -ErrorAction Stop }
  catch { $copyFailed += $name }
}

# logs 的實體位置：D:\Project\HulinBackroomsWiki\客户端\Recorder\logs
$loggerRoot = Join-Path $PSScriptRoot 'Recorder'
$logDir = Join-Path $loggerRoot 'logs'
New-Item -ItemType Directory -Force $logDir | Out-Null

# app\logs 必须指向 Recorder\logs；搬过目录后可能留下指向旧路径的断链。
# 注意：断掉的 junction 用 Test-Path 会返回 false，但它仍然占着路径，
# 直接 mklink 会失败，记录器就会写不进 logs。这里先检查 reparse point。
$appLogs = Join-Path $dst 'logs'
$appLogsItem = Get-Item -LiteralPath $appLogs -Force -ErrorAction SilentlyContinue
$isLink = $appLogsItem -and (($appLogsItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)

if ($isLink) {
  $linkTarget = [string]$appLogsItem.Target
  $want = [IO.Path]::GetFullPath($logDir).TrimEnd('\')
  $got = if ($linkTarget) { [IO.Path]::GetFullPath($linkTarget).TrimEnd('\') } else { '' }
  if ($got -ne $want) {
    # 只删除 junction 本身（非递归），不会动到目标目录内容。
    [System.IO.Directory]::Delete($appLogs, $false)
    $appLogsItem = $null
  }
} elseif ($appLogsItem) {
  # 旧版普通目录：把内容搬进 logger\logs，再删空目录。
  Get-ChildItem -LiteralPath $appLogs -Force | ForEach-Object {
    $target = Join-Path $logDir $_.Name
    if (-not (Test-Path -LiteralPath $target)) { Move-Item -LiteralPath $_.FullName -Destination $target }
  }
  if (-not (Get-ChildItem -LiteralPath $appLogs -Force)) { Remove-Item -LiteralPath $appLogs -Force }
  $appLogsItem = $null
}

if (-not $appLogsItem) {
  Split-Path -Parent $appLogs | ForEach-Object { New-Item -ItemType Directory -Force $_ | Out-Null }
  cmd /c mklink /J "$appLogs" "$logDir" | Out-Null
  if (-not (Test-Path -LiteralPath $appLogs)) {
    Write-Host "无法建立 logs 链接：$appLogs -> $logDir" -ForegroundColor Red
    exit 1
  }
}

$exe = Join-Path $dst 'ExpeditionRecorder.exe'
$argsList = @('--url', $Url, '--logdir', $logDir)
if ($Profile) { $argsList += @('--profile', $Profile) }
if ($NoRecord) { $argsList += '--no-record' }
if ($NoOverlay) { $argsList += '--no-overlay' }

# 只啟動剛複製到本機的這一份（D:\ 的 exe 會被 Windows 擋）
Start-Process -FilePath $exe -ArgumentList $argsList -WorkingDirectory $dst


