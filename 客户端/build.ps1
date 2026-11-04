# 从 ExpeditionPlayer.cs / ExpeditionRecorder.cs 重新编译，并同步到客户端目录。
# 通常由 start.ps1 / 啟動.bat 自动调用；也可以单独运行。
$ErrorActionPreference = 'Stop'

$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 csc.exe：$csc" }

$playerSrc = Join-Path $root 'ExpeditionPlayer.cs'
$panelSrc = Join-Path $root 'ExpeditionRecorder.cs'
foreach ($src in @($playerSrc, $panelSrc)) {
  if (-not (Test-Path -LiteralPath $src)) { throw "找不到源文件：$src" }
}

# WebView2 的 DLL 以可携版 dist 为准；没有 dist 时退回客户端目录已有的副本。
$webDllDir = Join-Path $root 'dist\Expedition'
if (-not (Test-Path -LiteralPath (Join-Path $webDllDir 'Microsoft.Web.WebView2.Core.dll'))) {
  $webDllDir = $root
}
$core = Join-Path $webDllDir 'Microsoft.Web.WebView2.Core.dll'
$wf = Join-Path $webDllDir 'Microsoft.Web.WebView2.WinForms.dll'
$loader = Join-Path $webDllDir 'WebView2Loader.dll'
foreach ($dll in @($core, $wf, $loader)) {
  if (-not (Test-Path -LiteralPath $dll)) { throw "缺少 WebView2 DLL：$dll" }
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('Expedition-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
$failed = New-Object System.Collections.Generic.List[string]

function Install-File {
  param([string]$From, [string]$To, [string]$Label)
  try {
    Copy-Item -LiteralPath $From -Destination $To -Force -ErrorAction Stop
    return $true
  } catch {
    $script:failed.Add($Label + '：' + $_.Exception.Message)
    Write-Warning ("跳过 " + $Label + "（文件可能正在使用）")
    return $false
  }
}

# 只复制有差异的档：structured 含 OCR 服务与字型，整包重拷一次就是几百 MB。
function Sync-Directory {
  param([string]$From, [string]$To)
  if (-not (Test-Path -LiteralPath $To)) { New-Item -ItemType Directory -Force $To | Out-Null }
  $base = (Get-Item -LiteralPath $From).FullName.TrimEnd('\')
  foreach ($f in Get-ChildItem -LiteralPath $From -Recurse -File) {
    $rel = $f.FullName.Substring($base.Length).TrimStart('\')
    $dst = Join-Path $To $rel
    $parent = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
    $copy = $true
    if (Test-Path -LiteralPath $dst) {
      $d = Get-Item -LiteralPath $dst
      if ($d.Length -eq $f.Length -and $d.LastWriteTimeUtc -eq $f.LastWriteTimeUtc) { $copy = $false }
    }
    if ($copy) {
      try {
        Copy-Item -LiteralPath $f.FullName -Destination $dst -Force -ErrorAction Stop
      } catch {
        $script:failed.Add($rel + '：' + $_.Exception.Message)
        Write-Warning ("跳过 " + $rel + "（文件可能正在使用）")
      }
    }
  }
}

try {
  # 先编译到临时目录，避免 csc 直接写正在使用的 exe。
  $playerTmp = Join-Path $tmp 'ExpeditionPlayer.exe'
  & $csc /nologo /target:winexe /platform:x64 /out:$playerTmp `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
    /r:$core /r:$wf $playerSrc
  if ($LASTEXITCODE -ne 0) { throw 'csc 编译失败（ExpeditionPlayer）' }

  $panelTmp = Join-Path $tmp 'ExpeditionRecorder.exe'
  & $csc /nologo /target:winexe /platform:x64 /out:$panelTmp `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $panelSrc
  if ($LASTEXITCODE -ne 0) { throw 'csc 编译失败（ExpeditionRecorder）' }

  # 面板必须先替换；失败就直接报错，避免“看起来启动成功、其实还是旧面板”。
  $panelOut = Join-Path $root 'ExpeditionRecorder.exe'
  try {
    Copy-Item -LiteralPath $panelTmp -Destination $panelOut -Force -ErrorAction Stop
  } catch {
    throw "ExpeditionRecorder.exe 正在使用，无法替换。请先关闭记录器面板，再重新运行 啟動.bat。"
  }

  # 游戏客户端可以保持开启；被锁住时跳过，只更新面板。
  $clientOut = Join-Path $root 'ExpeditionClient.exe'
  $clientUpdated = $true
  try {
    Copy-Item -LiteralPath $playerTmp -Destination $clientOut -Force -ErrorAction Stop
  } catch {
    $clientUpdated = $false
    Write-Warning 'ExpeditionClient.exe 正在使用，这次只更新面板；关闭游戏客户端后重新运行 啟動.bat 即可更新客户端。'
  }

  Remove-Item -LiteralPath (Join-Path $root 'ExpeditionPlayer.exe') -Force -ErrorAction SilentlyContinue

  if ($webDllDir -ne $root) {
    Install-File -From $core -To (Join-Path $root 'Microsoft.Web.WebView2.Core.dll') -Label 'Microsoft.Web.WebView2.Core.dll' | Out-Null
    Install-File -From $wf -To (Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll') -Label 'Microsoft.Web.WebView2.WinForms.dll' | Out-Null
    Install-File -From $loader -To (Join-Path $root 'WebView2Loader.dll') -Label 'WebView2Loader.dll' | Out-Null
  }

  Install-File -From (Join-Path $root 'Recorder\backroom-logger.ps1') -To (Join-Path $root 'backroom-logger.ps1') -Label 'backroom-logger.ps1' | Out-Null
  Install-File -From (Join-Path $root 'Recorder\palette-from-shots.ps1') -To (Join-Path $root 'palette-from-shots.ps1') -Label 'palette-from-shots.ps1' | Out-Null

  $structuredSrc = Join-Path $root 'Recorder\structured'
  $structuredDst = Join-Path $root 'structured'
  try {
    Sync-Directory -From $structuredSrc -To $structuredDst
  } catch {
    $failed.Add('structured：' + $_.Exception.Message)
    Write-Warning 'structured 同步有文件被占用，已跳过。'
  }

  if ($failed.Count -gt 0) {
    Write-Warning ('以下文件未更新：' + ($failed -join '；'))
  }

  if ($clientUpdated) {
    Write-Host '已编译：ExpeditionRecorder / ExpeditionClient'
  } else {
    Write-Host '已编译：ExpeditionRecorder（ExpeditionClient 被占用，未替换）'
  }
}
finally {
  Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
