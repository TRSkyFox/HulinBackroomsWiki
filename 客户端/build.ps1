# 從 ExpeditionPlayer.cs 重新編譯，並把 WebView2 / 記錄器腳本放進 app\
$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$app = $root
New-Item -ItemType Directory -Force $app | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $root 'ExpeditionPlayer.cs'
# WebView2 的 DLL 以可攜版 dist 為準；沒有 dist 時退回 app 內已有的副本。
$expeditionWebDllPath = [System.IO.Path]::Combine($root, 'dist\Expedition')
if (-not (Test-Path -LiteralPath (Join-Path -Path $expeditionWebDllPath -ChildPath 'Microsoft.Web.WebView2.Core.dll'))) {
  $expeditionWebDllPath = $app
}
$core = Join-Path -Path $expeditionWebDllPath -ChildPath 'Microsoft.Web.WebView2.Core.dll'
$wf = Join-Path -Path $expeditionWebDllPath -ChildPath 'Microsoft.Web.WebView2.WinForms.dll'

$outExe = Join-Path $app 'ExpeditionPlayer.exe'
& $csc /nologo /target:winexe /platform:x64 /out:$outExe `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
  /r:$core /r:$wf $src
if ($LASTEXITCODE -ne 0) { throw 'csc 編譯失敗（ExpeditionPlayer）' }

# 控制台/記錄器面板：獨立 exe，更新它不需要關遊戲客戶端。
$panelSrc = Join-Path $root 'ExpeditionRecorder.cs'
$panelOut = Join-Path $app 'ExpeditionRecorder.exe'
& $csc /nologo /target:winexe /platform:x64 /out:$panelOut `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll $panelSrc
if ($LASTEXITCODE -ne 0) { throw 'csc 編譯失敗（ExpeditionRecorder）' }

# 遊戲客戶端：client-only 模式；獨立檔名，才能在客戶端執行時更新面板。
try {
  Copy-Item $outExe (Join-Path $app 'ExpeditionClient.exe') -Force -ErrorAction Stop
  Copy-Item (Join-Path $root 'ExpeditionClient.exe.config') (Join-Path $app 'ExpeditionClient.exe.config') -Force -ErrorAction Stop
} catch {
  Write-Warning 'ExpeditionClient.exe 正在執行，這次只更新面板；客戶端版本未覆蓋。'
}
# ExpeditionPlayer.exe 只是開發用完整體，不進玩家包；玩家只拿 ExpeditionClient.exe。
Remove-Item (Join-Path $app 'ExpeditionPlayer.exe') -Force -ErrorAction SilentlyContinue

Copy-Item $core (Join-Path $app 'Microsoft.Web.WebView2.Core.dll') -Force
Copy-Item $wf (Join-Path $app 'Microsoft.Web.WebView2.WinForms.dll') -Force
Copy-Item (Join-Path $expeditionWebDllPath 'WebView2Loader.dll') $app -Force
Copy-Item (Join-Path $root '..\记录器\backroom-logger.ps1') $app -Force
Copy-Item (Join-Path $root '..\记录器\palette-from-shots.ps1') $app -Force
New-Item -ItemType Directory -Force (Join-Path $app 'structured') | Out-Null
Copy-Item (Join-Path $root '..\记录器\structured\*') (Join-Path $app 'structured') -Recurse -Force

Write-Host '已編譯：ExpeditionRecorder／ExpeditionPlayer／ExpeditionClient'
