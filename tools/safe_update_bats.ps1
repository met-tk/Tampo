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

Get-ChildItem -Path $workspace -Filter "*.bat" | ForEach-Object {
    if ($_.Name -ne "run.bat" -and $_.Length -lt 1000 -and $_.Length -ne 199) {
        Write-Output "Overwriting root launcher: $($_.FullName)"
        [System.IO.File]::WriteAllText($_.FullName, $rootBat, [System.Text.Encoding]::ASCII)
    }
}

$pubDir = Join-Path $workspace "publish"
if (Test-Path $pubDir) {
    Get-ChildItem -Path $pubDir -Filter "*.bat" | ForEach-Object {
        Write-Output "Overwriting publish launcher: $($_.FullName)"
        [System.IO.File]::WriteAllText($_.FullName, $publishBat, [System.Text.Encoding]::ASCII)
    }
}

$toolsBatsDir = Join-Path $PSScriptRoot "publish_bats"
if (Test-Path $toolsBatsDir) {
    Get-ChildItem -Path $toolsBatsDir -Filter "*.bat" | ForEach-Object {
        Write-Output "Overwriting tools launcher: $($_.FullName)"
        [System.IO.File]::WriteAllText($_.FullName, $publishBat, [System.Text.Encoding]::ASCII)
    }
}

Write-Output "Done!"
