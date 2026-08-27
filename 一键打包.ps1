<#
.SYNOPSIS
  CraneLoadingSystem 一键打包（PowerShell 版，仓库根右键"使用 PowerShell 运行"即可）
.DESCRIPTION
  调用 deploy\build-release.ps1 完成 3 种 ZIP 打包，
  并将产物归档到仓库根目录 _release\，完成后自动打开该目录。
.EXAMPLE
  右键 【一键打包.ps1】 → 使用 PowerShell 运行
  或在 PowerShell 中：
    powershell -ExecutionPolicy Bypass -File "一键打包.ps1"
#>
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol =
    [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$root = $PSScriptRoot
Set-Location $root

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   CraneLoadingSystem  一键打包（PowerShell 版）"             -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   输出目录：$root\_release" -ForegroundColor Gray
Write-Host ""

# -------- 路径检查 --------
if (-not (Test-Path (Join-Path $root "deploy\build-release.ps1"))) {
    throw "找不到 deploy\build-release.ps1，请确认本脚本放在仓库根目录（与 .sln 同级）"
}
if (-not (Test-Path (Join-Path $root "CraneLoadingSystem.sln"))) {
    throw "找不到 CraneLoadingSystem.sln，请确认本脚本放在仓库根目录"
}

# -------- 执行 deploy\build-release.ps1 --------
& (Join-Path $root "deploy\build-release.ps1")
if ($LASTEXITCODE -ne 0 -and -not $?) {
    throw "deploy\build-release.ps1 返回非 0，打包失败"
}

# -------- 归档产物到根目录 _release --------
$srcOut = Join-Path $root "deploy\out"
$dst    = Join-Path $root "_release"
if (Test-Path $srcOut) {
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Force -Path $dst | Out-Null }
    $zips = Get-ChildItem -LiteralPath $srcOut -Filter *.zip -File -ErrorAction SilentlyContinue
    if ($zips) {
        Copy-Item -LiteralPath ($zips | Select-Object -ExpandProperty FullName) `
                  -Destination $dst -Force
    }
    Write-Host ""
    Write-Host "----------------------------------------------------------" -ForegroundColor Green
    Write-Host "  ✓ 打包完成，产物归档到：$dst" -ForegroundColor Green
    Write-Host "----------------------------------------------------------" -ForegroundColor Green
    Get-ChildItem -LiteralPath $dst -Filter *.zip -File | ForEach-Object {
        $sz = [math]::Round($_.Length / 1MB, 1)
        Write-Host ("    {0,-50} {1,8} MB" -f $_.Name, $sz)
    }
    Write-Host "----------------------------------------------------------" -ForegroundColor Green
    try { explorer $dst } catch {}
} else {
    Write-Host "⚠ deploy\out 不存在（可能打包提前中止）" -ForegroundColor Yellow
}

Read-Host "`n按回车关闭窗口"
