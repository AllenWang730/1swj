<#
.SYNOPSIS
  CraneLoadingSystem 源码包启动（方案A，PowerShell 版本，可选 ExecutionPolicy Bypass）
#>
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
chcp 65001 | Out-Null

# ===== 容错定位工作目录（永远不会因路径不存在而 Set-Location 失败）=====
# 优先级：
#   1) param -WorkDir 传参
#   2) $PSScriptRoot（脚本所在目录；若不存在则跳过）
#   3) 父目录存在 CraneLoadingSystem.sln（用户把脚本放在 repo 根时）
#   4) 当前工作目录 pwd
#   5) $env:USERPROFILE\Desktop\1swj（强制创建）
param([string]$WorkDir = "")
$base = $null
if ($WorkDir -and (Test-Path (Split-Path $WorkDir -Parent))) {
    $base = $WorkDir
} elseif ($PSScriptRoot -and (Test-Path $PSScriptRoot)) {
    $base = $PSScriptRoot
} elseif (Test-Path (Join-Path (Get-Location) "CraneLoadingSystem.sln")) {
    $base = (Get-Location).Path
} else {
    $base = Join-Path $env:USERPROFILE "Desktop\1swj_run"
}
New-Item -ItemType Directory -Force -Path $base -ErrorAction Stop | Out-Null
try { Set-Location $base -ErrorAction Stop } catch { throw "无法切换到目录 $base ：$($_.Exception.Message)" }
Write-Host "  工作目录：$base" -ForegroundColor Gray

Write-Host "============================================================"  -ForegroundColor Cyan
Write-Host "  CraneLoadingSystem  v1.0.0  源码包启动 (PowerShell)"          -ForegroundColor Cyan
Write-Host "============================================================"  -ForegroundColor Cyan
Write-Host ""

# --- 项目文件探测（兼容单层/双层目录结构）---
$proj = $null
if (Test-Path "src\CraneLoadingSystem\CraneLoadingSystem.csproj") {
    $proj = "src\CraneLoadingSystem\CraneLoadingSystem.csproj"
} elseif (Test-Path "CraneLoadingSystem\src\CraneLoadingSystem\CraneLoadingSystem.csproj") {
    Set-Location CraneLoadingSystem
    $proj = "src\CraneLoadingSystem\CraneLoadingSystem.csproj"
}
if (-not $proj) {
    Write-Host "❌ 找不到项目文件，请确认解压后存在 src\CraneLoadingSystem\CraneLoadingSystem.csproj" -ForegroundColor Red
    Read-Host "按回车退出"; exit 1
}
Write-Host "  ✓ 项目文件：$proj" -ForegroundColor Green

try { $sdks = dotnet --list-sdks 2>$null } catch {}
$net10 = $sdks | Where-Object { $_ -match "^\s*10\." }
if (-not $net10) {
    Write-Host "❌ 未检测到 .NET 10 SDK，请安装：https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Red
    Read-Host "按回车退出"; exit 1
}
Write-Host "  ✓ .NET 10 SDK 已就绪" -ForegroundColor Green

Write-Host ""
Write-Host "[1/3] 还原 NuGet ..." -ForegroundColor Cyan
dotnet restore $proj --verbosity minimal
if ($LASTEXITCODE -ne 0) { Read-Host "❌ 还原失败，按回车退出"; exit 1 }

Write-Host ""
Write-Host "[2/3] 编译 Release ..." -ForegroundColor Cyan
dotnet build $proj -c Release --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { Read-Host "❌ 编译失败，按回车退出"; exit 1 }

Write-Host ""
Write-Host "[3/3] 启动应用（仿真模式）..." -ForegroundColor Cyan
dotnet run --project $proj -c Release --no-build

if ($LASTEXITCODE -ne 0) { Write-Host "⚠ 进程退出码：$LASTEXITCODE" -ForegroundColor Yellow }
Write-Host ""
Read-Host "按回车退出"
exit 0
