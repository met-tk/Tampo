$gbk = [System.Text.Encoding]::GetEncoding(936)

# 根目录启动器内容
$rootBat = "@echo off`r`nsetlocal`r`ncd /d `"%~dp0`"`r`nif exist `"publish\NihongoVocab.exe`" (`r`n    cd /d `"%~dp0publish`"`r`n    start `"`" `"NihongoVocab.exe`"`r`n    exit /b 0`r`n)`r`nif exist `"bin\Release\net8.0-windows10.0.26100.0\win-x64\publish\NihongoVocab.exe`" (`r`n    cd /d `"%~dp0bin\Release\net8.0-windows10.0.26100.0\win-x64\publish`"`r`n    start `"`" `"NihongoVocab.exe`"`r`n    exit /b 0`r`n)`r`nif exist `"NihongoVocab.exe`" (`r`n    start `"`" `"NihongoVocab.exe`"`r`n    exit /b 0`r`n)`r`necho [错误] 未找到可执行文件 NihongoVocab.exe。`r`npause`r`nexit /b 1`r`n"

# 发布目录启动器内容
$publishBat = "@echo off`r`nsetlocal`r`ncd /d `"%~dp0`"`r`nif exist `"NihongoVocab.exe`" (`r`n    start `"`" `"NihongoVocab.exe`"`r`n    exit /b 0`r`n)`r`necho [错误] 未找到可执行文件 NihongoVocab.exe。`r`npause`r`nexit /b 1`r`n"

[System.IO.File]::WriteAllText("$PSScriptRoot\..\启动Tampo.bat", $rootBat, $gbk)
[System.IO.File]::WriteAllText("$PSScriptRoot\..\启动Tampo.bat", $rootBat, $gbk)
[System.IO.File]::WriteAllText("$PSScriptRoot\publish_bats\启动Tampo.bat", $publishBat, $gbk)
[System.IO.File]::WriteAllText("$PSScriptRoot\publish_bats\启动Tampo.bat", $publishBat, $gbk)
[System.IO.File]::WriteAllText("$PSScriptRoot\..\publish\启动Tampo.bat", $publishBat, $gbk)
[System.IO.File]::WriteAllText("$PSScriptRoot\..\publish\启动Tampo.bat", $publishBat, $gbk)

Write-Host "All bat files updated with GBK encoding successfully."
