@echo off
chcp 65001 >nul
echo 正在将项目目录重命名为 Tampo...
cd /d "x:\AI project"
ren "日语词汇宝库" "Tampo"
if %errorlevel% equ 0 (
    echo [成功] 项目目录已成功更名为 Tampo！
) else (
    echo [提示] 目录可能仍被 IDE 或编辑器占用，请先关闭相关程序后再运行本脚本。
)
pause
