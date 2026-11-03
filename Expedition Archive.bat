@echo off
set "REPO=%~dp0"
if "%REPO:~-1%"=="\" set "REPO=%REPO:~0,-1%"
start "" "%~dp0tools\ExpeditionArchive.exe" --repo "%REPO%"
