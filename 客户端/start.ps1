param(
  [switch]$NoRecord,
  [switch]$NoOverlay,
  [string]$Url = 'https://hulin-backrooms-rpg.firebaseapp.com/',
  [string]$Preset = '1600x900',
  [string]$Profile = ''
)

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$dst = Join-Path $env:LOCALAPPDATA 'Expedition\app'

New-Item -ItemType Directory -Force $dst | Out-Null
$copyFailed = @()
$logsPrefix = (Join-Path $src 'logs') + [IO.Path]::DirectorySeparatorChar
foreach ($f in Get-ChildItem $src -Recurse -File) {
  if ($f.FullName.StartsWith($logsPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
  if ($f.FullName.StartsWith((Join-Path $src 'dist') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { continue }
  if ($f.Name -match '\.cs$' -or $f.Name -in @('build.ps1','README.md','start.ps1','啟動.bat','啟動記錄器.bat')) { continue }
  if ($f.FullName -match '\\fonts\\') { continue }
  $rel = $f.FullName.Substring($src.Length + 1)
  $to = Join-Path $dst $rel
  New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
  try {
    Copy-Item -LiteralPath $f.FullName -Destination $to -Force -ErrorAction Stop
  } catch {
    # 客戶端/面板在執行時，DLL 或 exe 可能被鎖住；跳過該檔，不動正在跑的程式。
    $copyFailed += $f.Name
  }
}
if ($copyFailed.Count -gt 0) {
  Write-Host ('客戶端檔案正在使用，這次跳過：' + ($copyFailed -join '、')) -ForegroundColor DarkGray
}

# logs 的實體位置：D:\BACKROOMS\记录器\logs
$loggerRoot = Join-Path (Split-Path $PSScriptRoot -Parent) '记录器'
$logDir = Join-Path $loggerRoot 'logs'
New-Item -ItemType Directory -Force $logDir | Out-Null

# 一次性把舊的 app\logs 內容搬進 logger\logs
$appLogs = Join-Path $dst 'logs'
if (Test-Path $appLogs) {
  $item = Get-Item $appLogs -Force
  $isLink = (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
  if (-not $isLink) {
    Get-ChildItem $appLogs -Force | ForEach-Object {
      $target = Join-Path $logDir $_.Name
      if (-not (Test-Path $target)) { Move-Item -LiteralPath $_.FullName -Destination $target }
    }
    if (-not (Get-ChildItem $appLogs -Force)) { Remove-Item $appLogs -Force }
  }
}

# app\logs 指向 logger\logs；直接開 app 時也會寫到同一個地方
if (-not (Test-Path $appLogs)) {
  cmd /c mklink /J "$appLogs" "$logDir" | Out-Null
}

$exe = Join-Path $dst 'ExpeditionRecorder.exe'
$argsList = @('--url', $Url, '--logdir', $logDir)
if ($Profile) { $argsList += @('--profile', $Profile) }
if ($NoRecord) { $argsList += '--no-record' }
if ($NoOverlay) { $argsList += '--no-overlay' }

# 只啟動剛複製到本機的這一份（D:\ 的 exe 會被 Windows 擋）
Start-Process -FilePath $exe -ArgumentList $argsList -WorkingDirectory $dst
