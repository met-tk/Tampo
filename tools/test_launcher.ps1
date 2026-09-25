$baseDir = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $baseDir "publish\NihongoVocab.exe"
$workingDir = Join-Path $baseDir "publish"

Start-Process -FilePath $exe -WorkingDirectory $workingDir
Start-Sleep -Seconds 1
Get-Process NihongoVocab | Select-Object Id, ProcessName, MainWindowTitle
