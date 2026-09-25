$rootBat = @"
@echo off
setlocal
title Tampo
cd /d "%~dp0"

if exist "publish\NihongoVocab.exe" (
    cd /d "%~dp0publish"
    start "" "NihongoVocab.exe"
    exit /b 0
)

if exist "NihongoVocab.exe" (
    start "" "NihongoVocab.exe"
    exit /b 0
)

if exist "bin\Release\net8.0-windows10.0.26100.0\win-x64\publish\NihongoVocab.exe" (
    cd /d "%~dp0bin\Release\net8.0-windows10.0.26100.0\win-x64\publish"
    start "" "NihongoVocab.exe"
    exit /b 0
)

echo [ERROR] NihongoVocab.exe not found.
pause
exit /b 1
"@

$publishBat = @"
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
"@

$workspace = Split-Path -Parent $PSScriptRoot

# 写入所有目标，全部强制纯 ASCII
Set-Content -Path (Join-Path $workspace "启动Tampo.bat") -Value $rootBat -Encoding Ascii
Set-Content -Path (Join-Path $workspace "启动Tampo.bat") -Value $rootBat -Encoding Ascii
Set-Content -Path (Join-Path $workspace "publish\启动Tampo.bat") -Value $publishBat -Encoding Ascii
Set-Content -Path (Join-Path $workspace "publish\启动Tampo.bat") -Value $publishBat -Encoding Ascii
Set-Content -Path (Join-Path $workspace "tools\publish_bats\启动Tampo.bat") -Value $publishBat -Encoding Ascii
Set-Content -Path (Join-Path $workspace "tools\publish_bats\启动Tampo.bat") -Value $publishBat -Encoding Ascii

Write-Host "Updated all bat files successfully."
