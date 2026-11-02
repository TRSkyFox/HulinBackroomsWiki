param(
  [string]$Package = '',
  [string]$Staging = '',
  [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$wiki = Join-Path $root 'wiki'
$stagingRoot = Join-Path $wiki '_incoming'
$importRegistry = Join-Path $stagingRoot '.imported-packages.json'
$node = (Get-Command node.exe -ErrorAction SilentlyContinue).Source
if (-not $node) { throw '找不到 node.exe，請先安裝 Node.js 或把 node.exe 放進 PATH' }

function Read-ImportRegistry {
  if (Test-Path -LiteralPath $importRegistry) {
    try {
      $r = Get-Content -LiteralPath $importRegistry -Raw -Encoding UTF8 | ConvertFrom-Json
      if ($r -and $r.packages) { return $r }
    } catch { }
  }
  return [pscustomobject]@{ packages = [pscustomobject]@{} }
}

function Save-ImportRegistry($r) {
  New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
  $tmp = $importRegistry + '.tmp'
  [System.IO.File]::WriteAllText($tmp, (($r | ConvertTo-Json -Depth 6) + [Environment]::NewLine), (New-Object System.Text.UTF8Encoding($false)))
  Move-Item -LiteralPath $tmp -Destination $importRegistry -Force
}

function Get-StagingPackageHash($dest) {
  $f = Join-Path $dest '.package-sha256'
  if (Test-Path -LiteralPath $f) { return (Get-Content -LiteralPath $f -Raw -Encoding UTF8).Trim().ToLowerInvariant() }
  return ''
}

# 兩種用法：
#   1) 匯入新紀錄包：import-package.ps1 -Package <zip>        → 只展開＋驗證＋審查
#   2) 套用已展開的審查結果：import-package.ps1 -Staging <dir> -Apply
if ($Staging) {
  $dest = (Resolve-Path -LiteralPath $Staging).Path
  $staging = Join-Path $dest 'staging'
  if (-not (Test-Path -LiteralPath $staging)) { throw "找不到 staging：$staging" }
} else {
  if (-not $Package) { $Package = Read-Host '請把 .expedition.zip 拖進來，或貼上路徑' }
  $Package = $Package.Trim().Trim('"')
  if (-not (Test-Path -LiteralPath $Package)) { throw "找不到檔案：$Package" }
  $packageHash = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant()
  $reg = Read-ImportRegistry
  if ($reg.packages.PSObject.Properties.Name -contains $packageHash) {
    $prev = $reg.packages.$packageHash
    throw ('這個紀錄包已匯入過，不重複處理。Hash=' + $packageHash + '；檔案=' + $Package + '；時間=' + $prev.applied)
  }
  $pending = Get-ChildItem -LiteralPath $stagingRoot -Directory -Filter 'import-*' -ErrorAction SilentlyContinue |
    Where-Object { (Get-StagingPackageHash $_.FullName) -eq $packageHash } | Select-Object -First 1
  if ($pending -and $Apply) {
    # 上次只展開／驗證過（或中途失敗），直接沿用同一份 staging 再套用，不重複展開。
    $dest = $pending.FullName
    $staging = Join-Path $dest 'staging'
    Write-Host ('沿用待審查 staging：' + $dest)
  } else {
    if ($pending) {
      throw ('這個紀錄包已在待審查 staging，請直接套用：' + $pending.FullName)
    }
    $name = 'import-' + [IO.Path]::GetFileNameWithoutExtension($Package) + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    $dest = Join-Path $stagingRoot $name
    New-Item -ItemType Directory -Force $dest | Out-Null
    Expand-Archive -LiteralPath $Package -DestinationPath $dest -Force
    [System.IO.File]::WriteAllText((Join-Path $dest '.package-sha256'), $packageHash, (New-Object System.Text.UTF8Encoding($false)))
    $staging = Join-Path $dest 'staging'
    if (-not (Test-Path -LiteralPath (Join-Path $staging 'validation.json'))) {
      throw '紀錄包裡找不到 staging\validation.json'
    }
  }
}

Write-Host '驗證 staging...'
& $node (Join-Path $root '客户端\Recorder\structured\validate.mjs') $staging
if ($LASTEXITCODE -ne 0) { throw 'staging 驗證失敗' }

Write-Host '產生審查報告（不寫入正式 Wiki）...'
& $node (Join-Path $root '客户端\Recorder\structured\merge-final.mjs') $staging
if ($LASTEXITCODE -ne 0) { throw '審查報告產生失敗' }

if (-not $Apply) {
  Write-Host ''
  Write-Host '── 已停在 staging，正式 Wiki 沒有被修改 ──'
  Write-Host ('審查資料：' + $dest)
  Write-Host ('  驗證：' + (Join-Path $staging 'validation.json'))
  Write-Host ('  事件：' + (Join-Path $staging 'events-review.jsonl'))
  Write-Host ('  合併：' + (Join-Path $staging 'merge-final-report.json'))
  Write-Host ''
  Write-Host '確認沒問題後，套用同一份 staging：'
  Write-Host ('  powershell -NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Staging "' + $dest + '" -Apply')
  exit 0
}

$applyHash = Get-StagingPackageHash $dest
if ($applyHash) {
  $reg2 = Read-ImportRegistry
  if ($reg2.packages.PSObject.Properties.Name -contains $applyHash) {
    throw ('這個紀錄包已套用過，不重複合併。Hash=' + $applyHash)
  }
}
Write-Host '合併正式 Wiki...'
& $node (Join-Path $root '客户端\Recorder\structured\merge-final.mjs') $staging --confirm
if ($LASTEXITCODE -ne 0) { throw '合併失敗' }
Write-Host '重建 Wiki...'
& $node (Join-Path $wiki 'content\build.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Wiki build 失敗' }
if ($applyHash) {
  $reg3 = Read-ImportRegistry
  $reg3.packages | Add-Member -NotePropertyName $applyHash -NotePropertyValue ([pscustomobject]@{
    applied = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    package = [IO.Path]::GetFileName($Package)
    staging = [IO.Path]::GetFileName($dest)
  }) -Force
  Save-ImportRegistry $reg3
}
Write-Host ('完成：' + $dest)
Write-Host ('網站：' + (Join-Path $wiki 'site\index.html'))
