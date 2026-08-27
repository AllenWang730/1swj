@echo off
REM ================================================================================
REM   CraneLoadingSystem 源码包启动入口（方案A，双击运行）
REM   需要：.NET 10 SDK；网络首次还原 NuGet（离线需离线 NuGet 缓存）
REM ================================================================================
chcp 65001 >nul
title CraneLoadingSystem 启动（源码包方案A）
cd /d "%~dp0"

echo ============================================================
echo   CraneLoadingSystem  v1.0.0  源码包启动
echo ============================================================
echo.

REM 探测项目文件位置：GitHub ZIP 解压会多一层 CraneLoadingSystem-*/ 子目录，
REM 但 build-release.bat 已经把源码放进 CraneLoadingSystem\ 子目录，
REM 所以本 bat 放根目录，下面两层都探测
set PROJ=
if exist "src\CraneLoadingSystem\CraneLoadingSystem.csproj" (
  set PROJ=src\CraneLoadingSystem\CraneLoadingSystem.csproj
) else if exist "CraneLoadingSystem\src\CraneLoadingSystem\CraneLoadingSystem.csproj" (
  cd CraneLoadingSystem
  set PROJ=src\CraneLoadingSystem\CraneLoadingSystem.csproj
)
if "%PROJ%"=="" (
  echo ❌ 找不到项目文件，请确认解压后的目录结构：
  echo    根目录\src\CraneLoadingSystem\CraneLoadingSystem.csproj
  pause & exit /b 1
)
echo   ✓ 项目文件：%PROJ%

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ❌ 未检测到 dotnet 命令，请先安装 .NET 10 SDK：
  echo    https://dotnet.microsoft.com/download/dotnet/10.0
  pause & exit /b 1
)
dotnet --list-sdks 2>nul | findstr /B "10\." >nul
if errorlevel 1 (
  echo ❌ 未检测到 .NET 10 SDK，当前已安装：
  dotnet --list-sdks
  echo    请安装 .NET 10 SDK：
  echo    https://dotnet.microsoft.com/download/dotnet/10.0
  pause & exit /b 1
)
echo   ✓ .NET 10 SDK 已就绪

echo.
echo [1/3] 还原 NuGet ...
dotnet restore "%PROJ%" --verbosity minimal
if errorlevel 1 (echo ❌ 还原失败 & pause & exit /b 1)

echo.
echo [2/3] 编译 Release ...
dotnet build "%PROJ%" -c Release --no-restore --verbosity minimal
if errorlevel 1 (echo ❌ 编译失败 & pause & exit /b 1)

echo.
echo [3/3] 启动应用（仿真模式）...
dotnet run --project "%PROJ%" -c Release --no-build

if errorlevel 1 (
  echo.
  echo ⚠ 进程退出码：%ERRORLEVEL%
)
echo.
echo 按回车键关闭窗口...
pause
exit /b 0
