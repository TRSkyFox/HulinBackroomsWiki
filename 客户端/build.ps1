# 從 FoyerPlayer.cs 重新編譯，並把 WebView2 / 記錄器腳本放進 app\
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'app'
New-Item -ItemType Directory -Force $app | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $root 'FoyerPlayer.cs'
# WebView2 的 DLL 以可攜版 dist 為準；沒有 dist 時退回 app 內已有的副本。
$dllDir = Join-Path $root 'dist\FoyerRecorder'
if (-not (Test-Path (Join-Path $dllDir 'Microsoft.Web.WebView2.Core.dll'))) {
  $dllDir = $app
}
$core = Join-Path $dllDir 'Microsoft.Web.WebView2.Core.dll'
$wf = Join-Path $dllDir 'Microsoft.Web.WebView2.WinForms.dll'

$outExe = Join-Path $app 'FoyerPlayer.exe'
& $csc /nologo /target:winexe /platform:x64 /out:$outExe `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  /r:$core /r:$wf $src
if ($LASTEXITCODE -ne 0) { throw 'csc 編譯失敗' }

Copy-Item $core (Join-Path $app 'Microsoft.Web.WebView2.Core.dll') -Force
Copy-Item $wf (Join-Path $app 'Microsoft.Web.WebView2.WinForms.dll') -Force
Copy-Item (Join-Path $dllDir 'WebView2Loader.dll') $app -Force
Copy-Item (Join-Path $root '..\记录器\backroom-logger.ps1') $app -Force
Copy-Item (Join-Path $root 'FoyerPlayer.exe.config') $app -Force
Copy-Item (Join-Path $root 'postprocess.mjs') $app -Force
Copy-Item (Join-Path $root 'volume-watch.ps1') $app -Force
Copy-Item (Join-Path $root '..\记录器\palette-from-shots.ps1') $app -Force
New-Item -ItemType Directory -Force (Join-Path $app 'structured') | Out-Null
Copy-Item (Join-Path $root '..\记录器\structured\*') (Join-Path $app 'structured') -Recurse -Force

Write-Host '已編譯：' (Join-Path $app 'FoyerPlayer.exe')
