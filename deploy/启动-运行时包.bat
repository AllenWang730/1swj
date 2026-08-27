@echo off
REM ================================================================================
REM   CraneLoadingSystem 运行包启动入口（方案B/方案C，双击运行）
REM   方案B 需要：.NET 10 Desktop Runtime（x64）
REM   方案C 需要：无（自包含，已内嵌 .NET）
REM ================================================================================
chcp 65001 >nul
title CraneLoadingSystem 启动
cd /d "%~dp0"

echo ============================================================
echo   CraneLoadingSystem  v1.0.0  启动
echo ============================================================
echo.

REM 优先 app\ 子目录（打包脚本放置的发布产物），否则同级
set EXE=
if exist "app\CraneLoadingSystem.exe" (
  set EXE=app\CraneLoadingSystem.exe
  cd app
) else if exist "CraneLoadingSystem.exe" (
  set EXE=CraneLoadingSystem.exe
)
if "%EXE%"=="" (
  echo ❌ 找不到 CraneLoadingSystem.exe，请确认解压后目录结构：
  echo    根目录\app\CraneLoadingSystem.exe     （或）
  echo    根目录\CraneLoadingSystem.exe
  pause & exit /b 1
)
echo   ✓ 程序：%EXE%

REM 方案B 检查 .NET 10 Desktop Runtime（方案C 自包含跳过）
"%EXE%" --check-dotnet-only-2>&1>nul
REM ← 上面命令没法区分"缺 Runtime"和"程序启动后的报错"，
REM    改用 dotnet --list-runtimes 单独检测（可选）
where dotnet >nul 2>nul
if not errorlevel 1 (
  dotnet --list-runtimes 2>nul | findstr /I "Microsoft.WindowsDesktop.App 10\." >nul
  if not errorlevel 1 (
    echo   ✓ .NET 10 Desktop Runtime 已就绪
  ) else (
    echo   ⚠ 未检测到 .NET 10 Desktop Runtime
    echo     如启动闪退，请安装：https://dotnet.microsoft.com/download/dotnet/10.0
    echo     （如果是自包含包方案C 可忽略此提示）
  )
)

echo.
echo 启动 CraneLoadingSystem（仿真模式）...
echo 关闭主窗口即退出本脚本。
echo.

REM 直接 EXE 运行（相对路径已 cd）
CraneLoadingSystem.exe

if errorlevel 1 (
  echo.
  echo ⚠ 程序退出码：%ERRORLEVEL%
  echo   常见原因：
  echo   - 方案B：未装 .NET 10 Desktop Runtime x64
  echo   - 缺少 Visual C++ 运行库（装 vc_redist.x64.exe）
  echo   - WPF 需要 Windows 桌面体验（Windows Server 需安装 桌面体验）
)
echo.
echo 按回车键关闭窗口...
pause
exit /b 0
