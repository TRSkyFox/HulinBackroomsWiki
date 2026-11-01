Set-Location $PSScriptRoot
node build.mjs
if ($LASTEXITCODE -eq 0) { Write-Host "`n已重建 site/index.html" }
