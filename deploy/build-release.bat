@echo off
REM ================================================================================
REM   CraneLoadingSystem  异地发布打包脚本（在 Windows 开发机上执行）
REM   产出：.\out\ 目录下 3 个 ZIP，任选一个拷到异地机器解压即可运行
REM
REM   产物说明：
REM     CraneLoadingSystem-vX.X.X-src.zip         【方案A 源码包】异地有 .NET 10 SDK → 双击 启动.bat
REM     CraneLoadingSystem-vX.X.X-fdd.zip         【方案B 框架依赖包】异地装 .NET 10 Desktop Runtime → 双击 CraneLoadingSystem.exe
REM     CraneLoadingSystem-vX.X.X-selfcontained.zip【方案C 自包含包】异地没装 .NET 也能跑（体积大 150MB+）
REM ================================================================================
setlocal EnableDelayedExpansion
chcp 65001 >nul
title CraneLoadingSystem 打包

cd /d "%~dp0"
set ROOT=%~dp0..
cd /d "%ROOT%"

set CONFIG=Release
set RID=win-x64
set CSPROJ=src\CraneLoadingSystem\CraneLoadingSystem.csproj
set OUT=%~dp0out
set VERSION=1.0.0
set ZIP_A=CraneLoadingSystem-v%VERSION%-src.zip
set ZIP_B=CraneLoadingSystem-v%VERSION%-fdd.zip
set ZIP_C=CraneLoadingSystem-v%VERSION%-selfcontained.zip

echo.
echo ============================================================
echo   CraneLoadingSystem 发布打包 v%VERSION%
echo ============================================================
echo   方案A: 源码包（异地需 .NET 10 SDK）
echo   方案B: 框架依赖包（异地需 .NET 10 Desktop Runtime）
echo   方案C: 自包含包（异地无需 .NET，体积最大）
echo ============================================================
echo.

REM ============================================================
REM  0. 环境检查
REM ============================================================
echo [0/6] 环境检查 ...
if not exist "%CSPROJ%" (
  echo   ❌ 找不到项目文件：%CSPROJ%，请确认脚本放在 deploy\ 目录与项目同仓库
  pause & exit /b 1
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo   ❌ 找不到 dotnet，请先安装 .NET 10 SDK
  echo     https://dotnet.microsoft.com/download/dotnet/10.0
  pause & exit /b 1
)
dotnet --list-sdks 2>nul | findstr /B "10\." >nul
if errorlevel 1 (
  echo   ❌ 未检测到 .NET 10 SDK，已装版本：
  dotnet --list-sdks
  pause & exit /b 1
)
echo   ✓ .NET 10 SDK 就绪

REM 清理产物目录
if exist "%OUT%" rmdir /S /Q "%OUT%" 2>nul
mkdir "%OUT%" 2>nul

REM ============================================================
REM  1. 编译验证（Release）
REM ============================================================
echo.
echo [1/6] 编译 Release ...
dotnet restore "%CSPROJ%" --verbosity minimal
if errorlevel 1 (echo   ❌ NuGet 还原失败 & pause & exit /b 1)
dotnet build "%CSPROJ%" -c %CONFIG% --no-restore --verbosity minimal
if errorlevel 1 (echo   ❌ 编译失败，查看上方红色错误 & pause & exit /b 1)
echo   ✓ 编译通过

REM ============================================================
REM  2. 方案A：源码包（src + deploy/启动辅助文件）
REM ============================================================
echo.
echo [2/6] 打包方案A：源码包 ...
set SRCSTAGE=%OUT%\_stage_src
mkdir "%SRCSTAGE%\CraneLoadingSystem" 2>nul

REM 复制源码（排除 bin / obj / .git / .vs / deploy/out）
robocopy . "%SRCSTAGE%\CraneLoadingSystem" /E /NFL /NDL /NJH /NJS ^
  /XD bin obj .git .vs "deploy\out" node_modules ^
  /XF *.user *.suo *.log

REM 把启动辅助文件放在根（解包后用户在根双击 启动.bat）
copy /Y "%~dp0启动-源码包.bat"            "%SRCSTAGE%\启动.bat"               >nul
copy /Y "%~dp0启动-源码包.ps1"            "%SRCSTAGE%\启动.ps1"               >nul
copy /Y "%~dp0异地部署说明.md"             "%SRCSTAGE%\异地部署说明.md"         >nul
REM 根目录放一个快速跳转标记，避免 GitHub ZIP 解压嵌套多一层
echo CraneLoadingSystem 源码包 v%VERSION% > "%SRCSTAGE%\版本.txt"

REM 压缩
powershell -NoProfile -Command ^
  "Compress-Archive -Path '%SRCSTAGE%\*' -DestinationPath '%OUT%\%ZIP_A%' -Force"
rmdir /S /Q "%SRCSTAGE%" 2>nul
echo   ✓ %ZIP_A%

REM ============================================================
REM  3. 方案B：FDD 框架依赖发布（异地需装 .NET 10 Desktop Runtime）
REM ============================================================
echo.
echo [3/6] 打包方案B：FDD 框架依赖包 ...
set PUB_B=%OUT%\_pub_b
dotnet publish "%CSPROJ%" -c %CONFIG% -r %RID% --no-build --no-restore ^
  -o "%PUB_B%" /p:PublishSingleFile=false /p:SelfContained=false ^
  --verbosity minimal 1>nul
if errorlevel 1 (echo   ❌ 发布失败 & pause & exit /b 1)

REM 拷贝运行辅助（放在 publish 目录之外一层，解包后用户根目录双击即可）
set BSTAGE=%OUT%\_stage_b
mkdir "%BSTAGE%" 2>nul
robocopy "%PUB_B%" "%BSTAGE%\app" /E /NFL /NDL /NJH /NJS >nul
copy /Y "%~dp0启动-运行时包.bat"           "%BSTAGE%\启动.bat"               >nul
copy /Y "%~dp0异地部署说明.md"             "%BSTAGE%\异地部署说明.md"         >nul
echo CraneLoadingSystem 框架依赖包 v%VERSION% ^(需 .NET 10 Desktop Runtime^) > "%BSTAGE%\版本.txt"

powershell -NoProfile -Command ^
  "Compress-Archive -Path '%BSTAGE%\*' -DestinationPath '%OUT%\%ZIP_B%' -Force"
rmdir /S /Q "%PUB_B%" "%BSTAGE%" 2>nul
echo   ✓ %ZIP_B%

REM ============================================================
REM  4. 方案C：自包含发布（免 .NET，单文件）
REM ============================================================
echo.
echo [4/6] 打包方案C：自包含包（单文件，Trim 减少体积）...
set PUB_C=%OUT%\_pub_c
dotnet publish "%CSPROJ%" -c %CONFIG% -r %RID% --no-restore ^
  -o "%PUB_C%" ^
  /p:PublishSingleFile=true ^
  /p:SelfContained=true ^
  /p:EnableCompressionInSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:DebugType=embedded ^
  --verbosity minimal 1>nul
if errorlevel 1 (echo   ⚠ 自包含发布报错（可能是首次或 RID 包未缓存），尝试补 restore...
  dotnet publish "%CSPROJ%" -c %CONFIG% -r %RID% ^
    -o "%PUB_C%" ^
    /p:PublishSingleFile=true ^
    /p:SelfContained=true ^
    /p:EnableCompressionInSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:DebugType=embedded ^
    --verbosity minimal
  if errorlevel 1 (echo   ❌ 自包含发布失败 & pause & exit /b 1)
)

set CSTAGE=%OUT%\_stage_c
mkdir "%CSTAGE%" 2>nul
robocopy "%PUB_C%" "%CSTAGE%\app" /E /NFL /NDL /NJH /NJS >nul
copy /Y "%~dp0启动-运行时包.bat"           "%CSTAGE%\启动.bat"               >nul
copy /Y "%~dp0异地部署说明.md"             "%CSTAGE%\异地部署说明.md"         >nul
echo CraneLoadingSystem 自包含包 v%VERSION% ^(免 .NET Runtime^) > "%CSTAGE%\版本.txt"

powershell -NoProfile -Command ^
  "Compress-Archive -Path '%CSTAGE%\*' -DestinationPath '%OUT%\%ZIP_C%' -Force"
rmdir /S /Q "%PUB_C%" "%CSTAGE%" 2>nul
echo   ✓ %ZIP_C%

REM ============================================================
REM  5. 输出产物清单 + 体积
REM ============================================================
echo.
echo [5/6] 产物清单：
echo   --------------------------------------------------------
setlocal EnableDelayedExpansion
for %%f in ("%OUT%\%ZIP_A%" "%OUT%\%ZIP_B%" "%OUT%\%ZIP_C%") do (
  set sz=%%~zf
  set /a "mb=sz/1048576"
  echo     %%~nf%%~xf       !mb! MB
)
echo   --------------------------------------------------------
echo   输出目录：%OUT%
endlocal

REM ============================================================
REM  6. 完成
REM ============================================================
echo.
echo [6/6] 打包完成 ✓
echo.
echo   推荐选型：
echo     异地机器没装 .NET（纯裸机）→ 方案C 自包含包
echo     异地机器装了 .NET 10 Runtime → 方案B 框架依赖包（体积小）
echo     异地机器装了 .NET 10 SDK   → 方案A 源码包（可二次开发）
echo.
pause
explorer "%OUT%"
exit /b 0
