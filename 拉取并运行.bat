@echo off
REM ================================================================================
REM   CraneLoadingSystem  拉取 GitHub + 编译 + 运行（CMD 双击入口）
REM   · 内部自动调用 PowerShell Bypass 模式跑 deploy\拉取并运行.ps1
REM   · 用户不用自己处理 ExecutionPolicy 限制，双击即可
REM ================================================================================
setlocal
chcp 65001 >nul
title CraneLoadingSystem 拉取 GitHub + 运行
cd /d "%~dp0"

echo.
echo ============================================================
echo   CraneLoadingSystem  拉取 GitHub + 编译 + 运行
echo ============================================================
echo.

REM —— 脚本位置定位 ——
set "PS=deploy\拉取并运行.ps1"
if not exist "%PS%" (
  REM 如果用户把此 bat 放到了仓库根（与 deploy 同级），正确
  REM 如果在 deploy 同级：也是正确
  REM 如果从别的位置启动：尝试仓库根相对
  if exist "..\%PS%" ( cd .. & goto :run )
  echo ❌ 找不到 %PS%
  echo   请确认【拉取并运行.bat】放在仓库根目录或 deploy\ 同级目录
  pause & exit /b 1
)

:run
REM —— ExecutionPolicy Bypass 方式调用 PowerShell ——
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS%"

set ERR=%ERRORLEVEL%
echo.
if "%ERR%"=="0" (echo ✓ 完成) else (echo ❌ 结束，退出码=%ERR%)
pause
exit /b %ERR%
