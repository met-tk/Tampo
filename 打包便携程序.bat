@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_publish.ps1"
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Build failed!
    pause
    exit /b %ERRORLEVEL%
)
pause
exit /b 0
