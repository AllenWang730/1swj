<#
.SYNOPSIS
  CraneLoadingSystem 一键下载+运行脚本（异地 Windows PowerShell 直接执行）

.DESCRIPTION
  适用于异地机器直接下载最新 master 源码 + 还原 NuGet + 编译 + 启动。
  与之前的 run.ps1 区别：可通过 irm|iex 单行直接拉取本脚本执行，无需手动复制。
  依赖：.NET 10 SDK。若没装会给出下载地址。

.EXAMPLE
  一键执行（推荐，PowerShell 窗口粘贴回车）：
    irm https://raw.githubusercontent.com/AllenWang730/1swj/master/deploy/remote-install.ps1 | iex

  若 raw.githubusercontent.com 无法访问（GitHub raw 被墙）：
    irm "https://gitee.com/mirrors-1swj/1swj/raw/master/deploy/remote-install.ps1" | iex
    （或改用下方"镜像双路重试"命令）

.EXAMPLE
  先落地到本地再执行（ExecutionPolicy 被锁时用）：
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/AllenWang730/1swj/master/deploy/remote-install.ps1' -OutFile "$env:USERPROFILE\Desktop\run.ps1" -UseBasicParsing"
    powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\Desktop\run.ps1"
#>
param(
    [string]$WorkDir = "$env:USERPROFILE\Desktop\1swj_run",
    [string]$Repo    = "AllenWang730/1swj",
    [string]$Branch  = "master",
    [string]$Config  = "Release"
)

# ====== 前置：TLS / 编码 / 错误策略 ======
[Net.ServicePointManager]::SecurityProtocol =
    [Net.SecurityProtocolType]::Tls12 -bor
    [Net.SecurityProtocolType]::Tls13
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$Csproj  = "src\CraneLoadingSystem\CraneLoadingSystem.csproj"
$ZipFile = Join-Path $WorkDir "_src.zip"

function Write-Step($Text) { Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Fail($Msg) {
    Write-Host "`n❌ $Msg" -ForegroundColor Red
    Write-Host "   请拍整个窗口或复制报错，发回给维护人员。" -ForegroundColor DarkYellow
    Read-Host "按回车键退出"
    exit 1
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  CraneLoadingSystem  一键下载+运行 (PowerShell)"             -ForegroundColor Cyan
Write-Host "  仓库 $Repo @ $Branch"                                         -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ====== [1/6] 清理旧工作目录 ======
Write-Step "1/6 准备工作目录：$WorkDir"
if (Test-Path $WorkDir) {
    Write-Host "  ↳ 旧目录存在，安全清理（仅删 src/ 子目录，避免误删用户文件）..."
    Remove-Item -Recurse -Force (Join-Path $WorkDir "src")  -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $WorkDir "docs") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $WorkDir "*.sln") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $WorkDir "*.md")  -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $WorkDir "_src.zip") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Get-ChildItem -Directory $WorkDir | Where-Object { $_.Name -like "1swj-*" } | Select-Object -ExpandProperty FullName) -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
Set-Location $WorkDir
Write-Host "  ✓ 当前目录：$(Get-Location)" -ForegroundColor Green

# ====== [2/6] 检查 .NET 10 SDK ======
Write-Step "2/6 检查 .NET 10 SDK"
$sdks = $null
try { $sdks = & dotnet --list-sdks 2>$null } catch {}
$hasNet10 = $sdks | Where-Object { $_ -match "^\s*10\." }
if (-not $hasNet10) {
    Write-Host "  当前已安装 SDK：" -ForegroundColor Yellow
    $sdks | ForEach-Object { Write-Host "    - $_" -ForegroundColor DarkYellow }
    Write-Host ""
    Write-Host "  👉 浏览器打开下载安装：" -ForegroundColor Yellow
    Write-Host "     https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Cyan
    Write-Host "     （页面中找到 Build apps - SDK x64 下载安装即可）" -ForegroundColor DarkYellow
    Fail "未检测到 .NET 10 SDK"
}
Write-Host "  ✓ .NET 10 SDK 已就绪 ($($hasNet10 | Select-Object -First 1))" -ForegroundColor Green

# ====== [3/6] 双路下载 ZIP（GitHub 主路 + codeload 备路）======
Write-Step "3/6 下载源码 ZIP"
$urls = @(
    "https://github.com/$Repo/archive/refs/heads/$Branch.zip",
    "https://codeload.github.com/$Repo/zip/refs/heads/$Branch"
)
$downloaded = $false
foreach ($url in $urls) {
    Write-Host "  尝试：$url"
    try {
        $ProgressPreference = "SilentlyContinue"   # 关闭大文件下载进度条（性能提升10x+）
        Invoke-WebRequest -Uri $url -OutFile $ZipFile -UseBasicParsing -TimeoutSec 180 -ErrorAction Stop
        $ProgressPreference = "Continue"
        if ((Test-Path $ZipFile) -and (Get-Item $ZipFile).Length -ge 10KB) {
            $downloaded = $true
            break
        } else {
            Write-Host "    ↳ 下载产物过小：$([math]::Round((Get-Item $ZipFile).Length/1KB,1)) KB，尝试下一镜像" -ForegroundColor Yellow
        }
    } catch {
        $ProgressPreference = "Continue"
        Write-Host "    ↳ 失败：$($_.Exception.Message)" -ForegroundColor Yellow
    }
}
if (-not $downloaded) {
    Write-Host ""
    Write-Host "  👉 两个镜像都不可用，请手动下载后放到：" -ForegroundColor Yellow
    Write-Host "     $ZipFile" -ForegroundColor Cyan
    Write-Host "     下载链接：https://github.com/$Repo/archive/refs/heads/$Branch.zip" -ForegroundColor Cyan
    Fail "ZIP 下载失败（网络不可达/代理问题/超时）"
}
Write-Host "  ✓ ZIP 下载完成 ($([math]::Round((Get-Item $ZipFile).Length/1KB,1)) KB)" -ForegroundColor Green

# ====== [4/6] 解压（兼容 GitHub ZIP 多一层子目录）======
Write-Step "4/6 解压源码"
try {
    Expand-Archive -Path $ZipFile -DestinationPath $WorkDir -Force -ErrorAction Stop
} catch {
    Fail "解压失败：$($_.Exception.Message)"
}
# GitHub ZIP 解压出的目录形如：{repo name}-{branch}，如 1swj-master
$innerDirs = Get-ChildItem -Directory $WorkDir | Where-Object { $_.Name -ne "_src" -and $_.Name -notlike "logs" }
if ($innerDirs.Count -eq 1) {
    $inner = $innerDirs[0].FullName
    Write-Host "  检测到子目录 '$($innerDirs[0].Name)'，把内容移到工作目录根..."
    Get-ChildItem -LiteralPath $inner -Force | ForEach-Object {
        Move-Item -LiteralPath $_.FullName -Destination $WorkDir -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $inner -Recurse -Force -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath $ZipFile -Force -ErrorAction SilentlyContinue
if (-not (Test-Path $Csproj)) {
    Write-Host "  当前工作目录结构：" -ForegroundColor Yellow
    Get-ChildItem -Force | Select-Object -ExpandProperty Name
    Fail "解压后找不到项目文件：$Csproj（目录结构不符合预期）"
}
Write-Host "  ✓ 解压完成" -ForegroundColor Green

# ====== [5/6] 还原 + 编译 ======
Write-Step "5/6 还原 NuGet 依赖 + 编译 $Config"
dotnet restore $Csproj --verbosity minimal
if ($LASTEXITCODE -ne 0) { Fail "NuGet 还原失败，请查看上方红字（公司内网常见：需设置 HTTP_PROXY / HTTPS_PROXY）" }
Write-Host "  ✓ 依赖还原完成" -ForegroundColor Green

dotnet build $Csproj -c $Config --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { Fail "编译失败，请把上方红色错误行贴回给维护人员" }
Write-Host "  ✓ 编译成功" -ForegroundColor Green

# ====== [6/6] 运行 ======
Write-Step "6/6 启动 CraneLoadingSystem（仿真模式）"
Write-Host "  日志目录：$(Join-Path (Get-Location) "logs")" -ForegroundColor DarkGray
Write-Host "  关闭主窗口后本脚本自动退出。" -ForegroundColor DarkGray
Write-Host ""
dotnet run --project $Csproj -c $Config --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n⚠ 程序退出码非 0：$LASTEXITCODE" -ForegroundColor Yellow
}
Write-Host "`n=== 脚本结束 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" -ForegroundColor Cyan
Read-Host "按回车键退出"
