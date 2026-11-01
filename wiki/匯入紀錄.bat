@echo off
chcp 65001 >nul
if "%~1"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0import-package.ps1"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0import-package.ps1" -Package "%~1" %2 %3
)
pause
