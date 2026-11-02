# 編譯 Wiki 主人專用的記錄整合工具；這個工具不進玩家可攜包。
$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $root 'ExpeditionArchive.cs'
$out = Join-Path $root 'ExpeditionArchive.exe'
& $csc /nologo /target:winexe /platform:x64 /out:$out `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $src
if ($LASTEXITCODE -ne 0) { throw 'csc 編譯失敗（ExpeditionArchive）' }
Write-Host '已編譯：工具\ExpeditionArchive.exe'


