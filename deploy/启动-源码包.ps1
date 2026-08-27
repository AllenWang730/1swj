<#
.SYNOPSIS
  CraneLoadingSystem 源码包启动（方案A，PowerShell 版本，可选 ExecutionPolicy Bypass）
#>
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
chcp 65001 | Out-Null
Set-Location $PSScriptRoot

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
