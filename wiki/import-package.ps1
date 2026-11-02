param(
  [string]$Package = '',
  [string]$Staging = '',
  [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$wiki = Join-Path $root 'wiki'
$stagingRoot = Join-Path $wiki '_incoming'
$node = (Get-Command node.exe -ErrorAction SilentlyContinue).Source
if (-not $node) { throw '找不到 node.exe，請先安裝 Node.js 或把 node.exe 放進 PATH' }

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
  $name = 'import-' + [IO.Path]::GetFileNameWithoutExtension($Package) + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
  $dest = Join-Path $stagingRoot $name
  New-Item -ItemType Directory -Force $dest | Out-Null
  Expand-Archive -LiteralPath $Package -DestinationPath $dest -Force
  $staging = Join-Path $dest 'staging'
  if (-not (Test-Path -LiteralPath (Join-Path $staging 'validation.json'))) {
    throw '紀錄包裡找不到 staging\validation.json'
  }
}

Write-Host '驗證 staging...'
& $node (Join-Path $root '记录器\structured\validate.mjs') $staging
if ($LASTEXITCODE -ne 0) { throw 'staging 驗證失敗' }

Write-Host '產生審查報告（不寫入正式 Wiki）...'
& $node (Join-Path $root '记录器\structured\merge-final.mjs') $staging
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

Write-Host '合併正式 Wiki...'
& $node (Join-Path $root '记录器\structured\merge-final.mjs') $staging --confirm
if ($LASTEXITCODE -ne 0) { throw '合併失敗' }
Write-Host '重建 Wiki...'
& $node (Join-Path $wiki 'content\build.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Wiki build 失敗' }
Write-Host ('完成：' + $dest)
Write-Host ('網站：' + (Join-Path $wiki 'site\index.html'))
