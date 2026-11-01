param(
  [switch]$NoRecord,
  [switch]$NoOverlay,
  [string]$Url = 'https://hulin-backrooms-rpg.firebaseapp.com/',
  [string]$Preset = '1600x900',
  [string]$Profile = ''
)

$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'app'
$dst = Join-Path $env:LOCALAPPDATA 'FoyerLogger\app'

New-Item -ItemType Directory -Force $dst | Out-Null
try {
  Copy-Item (Join-Path $src '*') $dst -Recurse -Force -ErrorAction Stop
} catch {
  Write-Host '客戶端檔案複製失敗：請先關閉正在執行的 FoyerPlayer（或舊的「狐霖記錄器控制」）再啟動。' -ForegroundColor Yellow
  Write-Host ('原因：' + $_.Exception.Message) -ForegroundColor DarkGray
  exit 1
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

$exe = Join-Path $dst 'FoyerPlayer.exe'
$argsList = @('--url', $Url, '--preset', $Preset, '--logdir', $logDir)
if ($Profile) { $argsList += @('--profile', $Profile) }
if ($NoRecord) { $argsList += '--no-record' } else { $argsList += '--record' }
if ($NoOverlay) { $argsList += '--no-overlay' }

# 只啟動剛複製到本機的這一份（D:\ 的 exe 會被 Windows 擋）
Start-Process -FilePath $exe -ArgumentList $argsList -WorkingDirectory $dst
