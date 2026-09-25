@echo off
setlocal
title Tampo
cd /d "%~dp0"

if exist "NihongoVocab.exe" (
    start "" "NihongoVocab.exe"
    exit /b 0
)

echo [ERROR] NihongoVocab.exe not found.
pause
exit /b 1