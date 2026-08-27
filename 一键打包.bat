@echo off
REM ================================================================================
REM   CraneLoadingSystem  一键打包  ⭐ 双击这个文件即可 ⭐
REM
REM   · 入口放在仓库根目录（与 .sln 同级），不用切子目录
REM   · 自动调用 deploy\build-release.bat 完成 3 种产物打包
REM   · 所有 ZIP 自动挪到仓库根 _release\ 目录，并自动弹出该目录
REM   · 失败会停留并高亮提示，不会一闪而过
REM ================================================================================
setlocal
title CraneLoadingSystem 一键打包
chcp 65001 >nul
cd /d "%~dp0"

if not exist "deploy\build-release.bat" (
  echo ❌ 找不到 deploy\build-release.bat
  echo   请确认【一键打包.bat】放在仓库根目录（与 CraneLoadingSystem.sln 同级）
  pause & exit /b 1
)
if not exist "CraneLoadingSystem.sln" (
  echo ❌ 找不到 CraneLoadingSystem.sln
  echo   请确认【一键打包.bat】放在仓库根目录
  pause & exit /b 1
)

echo.
echo ============================================================
echo    CraneLoadingSystem  一键打包（双击版）
echo ============================================================
echo    输出目录：%~dp0_release\
echo ============================================================
echo.

REM —— 打包：调用 deploy\build-release.bat ——
call deploy\build-release.bat
set ERR=%ERRORLEVEL%

REM —— 如果 build-release.bat 成功，把 deploy\out\ 下的 ZIP 挪到根 _release\ ——
if "%ERR%"=="0" (
  if exist "deploy\out" (
    if not exist "_release" mkdir "_release" 2>nul
    REM 覆盖同名旧 ZIP
    copy /Y "deploy\out\*.zip" "_release\" >nul
    echo.
    echo ----------------------------------------------------------
    echo   ✓ 打包完成！产物已归档到：%~dp0_release\
    echo ----------------------------------------------------------
    dir /b "_release\*.zip" 2>nul
    echo ----------------------------------------------------------
    echo.
    REM 打开 _release 目录
    explorer "%~dp0_release" 2>nul
  ) else (
    echo ⚠ build-release.bat 返回 0 但 deploy\out 不存在
  )
) else (
  echo.
  echo ❌ 打包失败，退出码=%ERR%，请查看上方报错
)

echo.
pause
exit /b %ERR%
