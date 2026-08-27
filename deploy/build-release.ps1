<#
.SYNOPSIS
  CraneLoadingSystem 异地发布打包脚本（Windows 开发机上执行，PowerShell 版）
.DESCRIPTION
  产出：.\out\ 下 3 个 ZIP：
    CraneLoadingSystem-v1.0.0-src.zip           方案A 源码包（异地需 .NET 10 SDK）
    CraneLoadingSystem-v1.0.0-fdd.zip           方案B 框架依赖包（异地需 .NET 10 Desktop Runtime）
    CraneLoadingSystem-v1.0.0-selfcontained.zip 方案C 自包含包（异地无需 .NET）

  运行前（如被 ExecutionPolicy 拦）：
    Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force
.EXAMPLE
  cd deploy
  powershell -ExecutionPolicy Bypass -File build-release.ps1
#>
[Net.ServicePointManager]::SecurityProtocol =
    [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
$ErrorActionPreference = "Stop"

# ============ 参数 ============
$CONFIG    = "Release"
$RID       = "win-x64"
$VERSION   = "1.0.0"
$ROOT      = (Resolve-Path "$PSScriptRoot\..").Path
$CSPROJ    = Join-Path $ROOT "src\CraneLoadingSystem\CraneLoadingSystem.csproj"
$DEPLOYDIR = $PSScriptRoot
$OUT       = Join-Path $DEPLOYDIR "out"
$ZIP_A     = "CraneLoadingSystem-v$VERSION-src.zip"
$ZIP_B     = "CraneLoadingSystem-v$VERSION-fdd.zip"
$ZIP_C     = "CraneLoadingSystem-v$VERSION-selfcontained.zip"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  CraneLoadingSystem 发布打包 v$VERSION" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  方案A: 源码包 + 启动.bat / 启动.ps1   （异地需 .NET 10 SDK）"
Write-Host "  方案B: FDD 框架依赖发布                （异地需 .NET 10 Desktop Runtime）"
Write-Host "  方案C: 自包含发布（单文件，已压缩）     （异地无需 .NET，~150MB）"
Write-Host "============================================================" -ForegroundColor Cyan

# --- [0/6] 环境检查 ---
Write-Host ""
Write-Host "[0/6] 环境检查" -ForegroundColor Cyan
if (-not (Test-Path $CSPROJ)) { throw "找不到项目文件：$CSPROJ" }
Write-Host "  ✓ 项目：$CSPROJ" -ForegroundColor Green

try { $sdks = dotnet --list-sdks 2>$null } catch {}
$net10 = $sdks | Where-Object { $_ -match "^\s*10\." }
if (-not $net10) {
    Write-Host "  当前已安装 SDK：" -ForegroundColor Yellow
    $sdks | ForEach-Object { Write-Host "    - $_" -ForegroundColor DarkYellow }
    throw "未检测到 .NET 10 SDK，请安装：https://dotnet.microsoft.com/download/dotnet/10.0"
}
Write-Host "  ✓ .NET 10 SDK OK" -ForegroundColor Green

if (Test-Path $OUT) { Remove-Item -Recurse -Force $OUT -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $OUT | Out-Null

# --- [1/6] 编译验证 Release ---
Write-Host ""
Write-Host "[1/6] 编译 Release" -ForegroundColor Cyan
Set-Location $ROOT
dotnet restore $CSPROJ --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "NuGet 还原失败" }
dotnet build $CSPROJ -c $CONFIG --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "编译失败，查看上方红色错误" }
Write-Host "  ✓ 编译通过" -ForegroundColor Green

# --- [2/6] 方案A 源码包 ---
Write-Host ""
Write-Host "[2/6] 打包方案A：源码包" -ForegroundColor Cyan
$srcStage = Join-Path $OUT "_stage_src"
$srcRoot  = Join-Path $srcStage "CraneLoadingSystem"
New-Item -ItemType Directory -Force -Path $srcRoot | Out-Null

# 复制（排除中间产物）
$exclDirs = @("bin", "obj", ".git", ".vs", "node_modules", "out")
Get-ChildItem -LiteralPath $ROOT -Force | ForEach-Object {
    $tgt = Join-Path $srcRoot $_.Name
    if ($_.PSIsContainer) {
        if ($_.Name -in $exclDirs -or $_.FullName -eq $DEPLOYDIR) { return }
        Copy-Item -LiteralPath $_.FullName -Destination $tgt -Recurse -Force
    } else {
        if ($_.Extension -in @(".user", ".suo", ".log")) { return }
        Copy-Item -LiteralPath $_.FullName -Destination $tgt -Force
    }
}

# 启动脚本与说明放在根（与 CraneLoadingSystem 文件夹同级，用户双击即可）
Copy-Item (Join-Path $DEPLOYDIR "启动-源码包.bat")    (Join-Path $srcStage "启动.bat")       -Force
Copy-Item (Join-Path $DEPLOYDIR "启动-源码包.ps1")    (Join-Path $srcStage "启动.ps1")       -Force
Copy-Item (Join-Path $DEPLOYDIR "异地部署说明.md")     (Join-Path $srcStage "异地部署说明.md") -Force
"CraneLoadingSystem 源码包 v$VERSION" | Set-Content (Join-Path $srcStage "版本.txt")

Compress-Archive -Path (Join-Path $srcStage "*") -DestinationPath (Join-Path $OUT $ZIP_A) -Force
Remove-Item -Recurse -Force $srcStage -ErrorAction SilentlyContinue
Write-Host "  ✓ $ZIP_A" -ForegroundColor Green

# --- [3/6] 方案B FDD 框架依赖发布 ---
Write-Host ""
Write-Host "[3/6] 打包方案B：FDD 框架依赖发布" -ForegroundColor Cyan
$pubB = Join-Path $OUT "_pub_b"
dotnet publish $CSPROJ -c $CONFIG -r $RID --no-build --no-restore `
    -o $pubB /p:PublishSingleFile=false /p:SelfContained=false --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "FDD 发布失败" }

$bStage = Join-Path $OUT "_stage_b"
New-Item -ItemType Directory -Force -Path (Join-Path $bStage "app") | Out-Null
Copy-Item -LiteralPath (Join-Path $pubB "*") -Destination (Join-Path $bStage "app") -Recurse -Force
Copy-Item (Join-Path $DEPLOYDIR "启动-运行时包.bat")  (Join-Path $bStage "启动.bat")       -Force
Copy-Item (Join-Path $DEPLOYDIR "异地部署说明.md")     (Join-Path $bStage "异地部署说明.md") -Force
"CraneLoadingSystem 框架依赖包 v$VERSION（需 .NET 10 Desktop Runtime）" | Set-Content (Join-Path $bStage "版本.txt")

Compress-Archive -Path (Join-Path $bStage "*") -DestinationPath (Join-Path $OUT $ZIP_B) -Force
Remove-Item -Recurse -Force $pubB, $bStage -ErrorAction SilentlyContinue
Write-Host "  ✓ $ZIP_B" -ForegroundColor Green

# --- [4/6] 方案C 自包含发布（单文件，压缩）---
Write-Host ""
Write-Host "[4/6] 打包方案C：自包含发布（单文件，压缩）" -ForegroundColor Cyan
$pubC = Join-Path $OUT "_pub_c"
dotnet publish $CSPROJ -c $CONFIG -r $RID --no-restore `
    -o $pubC `
    /p:PublishSingleFile=true `
    /p:SelfContained=true `
    /p:EnableCompressionInSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=embedded `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ⚠ 首次发布失败，尝试带 restore 重试..." -ForegroundColor Yellow
    dotnet publish $CSPROJ -c $CONFIG -r $RID `
        -o $pubC `
        /p:PublishSingleFile=true /p:SelfContained=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:DebugType=embedded `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "自包含发布失败" }
}

$cStage = Join-Path $OUT "_stage_c"
New-Item -ItemType Directory -Force -Path (Join-Path $cStage "app") | Out-Null
Copy-Item -LiteralPath (Join-Path $pubC "*") -Destination (Join-Path $cStage "app") -Recurse -Force
Copy-Item (Join-Path $DEPLOYDIR "启动-运行时包.bat")  (Join-Path $cStage "启动.bat")       -Force
Copy-Item (Join-Path $DEPLOYDIR "异地部署说明.md")     (Join-Path $cStage "异地部署说明.md") -Force
"CraneLoadingSystem 自包含包 v$VERSION（免 .NET Runtime）" | Set-Content (Join-Path $cStage "版本.txt")

Compress-Archive -Path (Join-Path $cStage "*") -DestinationPath (Join-Path $OUT $ZIP_C) -Force
Remove-Item -Recurse -Force $pubC, $cStage -ErrorAction SilentlyContinue
Write-Host "  ✓ $ZIP_C" -ForegroundColor Green

# --- [5/6] 产物清单 ---
Write-Host ""
Write-Host "[5/6] 产物清单" -ForegroundColor Cyan
Write-Host "  -------------------------------------------------------------"
foreach ($zip in @($ZIP_A, $ZIP_B, $ZIP_C)) {
    $full = Join-Path $OUT $zip
    $size = if (Test-Path $full) { [math]::Round((Get-Item $full).Length / 1MB, 1) } else { "N/A" }
    Write-Host ("    {0,-50} {1,8} MB" -f $zip, $size)
}
Write-Host "  -------------------------------------------------------------"
Write-Host "  输出目录：$OUT"
Write-Host "  推荐选型：" -ForegroundColor Yellow
Write-Host "    异地机器裸机（没 .NET）       → 方案C 自包含包" -ForegroundColor DarkYellow
Write-Host "    异地机器装了 .NET 10 Runtime → 方案B 框架依赖包（体积小）" -ForegroundColor DarkYellow
Write-Host "    异地机器装了 .NET 10 SDK     → 方案A 源码包（可二次开发）" -ForegroundColor DarkYellow

# --- [6/6] 完成并打开输出目录 ---
Write-Host ""
Write-Host "[6/6] 打包完成 ✓" -ForegroundColor Green
try {
    explorer $OUT
} catch {}
