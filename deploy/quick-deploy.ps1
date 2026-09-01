<#
  一键异地部署脚本 — 流体装卸鹤位上位机监控系统
  不依赖 git，纯 PowerShell + .NET SDK 即可部署。

  用法: 在目标 Windows 机器上打开 PowerShell，执行:
    irm https://raw.githubusercontent.com/AllenWang730/1swj/codex/bugfix-deploy/deploy/quick-deploy.ps1 | iex

  或手动下载后执行:
    powershell -NoProfile -ExecutionPolicy Bypass -File quick-deploy.ps1

  参数:
    -Branch   代码分支 (默认 codex/bugfix-deploy，含3处Bug修复)
    -Config   Debug/Release (默认 Release)
    -Run      $true=编译后启动, $false=仅编译 (默认 $true)
    -UseCN    $true=使用国内NuGet镜像 (默认 $true)
#>
param(
    [string]$Branch = 'codex/bugfix-deploy',
    [ValidateSet('Debug','Release')][string]$Config = 'Release',
    [bool]$Run   = $true,
    [bool]$UseCN = $true
)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

# ---- helpers ----
function S($t){ Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function OK($t){ Write-Host "  [OK]  $t" -ForegroundColor Green }
function FAIL($m){ Write-Host "`n  [FAIL] $m" -ForegroundColor Red; Read-Host '按回车退出'; exit 1 }

# ---- 1. check .NET 10 SDK ----
S '1/5 检查 .NET 10 SDK'
$sdkOk = $false
try { dotnet --list-sdks 2>$null | ForEach-Object { if($_ -match '^\s*10\.'){ $sdkOk=$true; OK $_.Trim() } } } catch {}
if(-not $sdkOk){ FAIL "未检测到 .NET 10 SDK。请安装: https://dotnet.microsoft.com/download/dotnet/10.0 (SDK x64)" }

# ---- 2. download & extract ZIP (no git required) ----
S "2/5 下载代码 ZIP (分支: $Branch)"
$workDir  = Join-Path $PWD.Path '1swj-deploy'
$zipFile  = Join-Path $PWD.Path '1swj.zip'
if(Test-Path $workDir){ Remove-Item $workDir -Recurse -Force }
if(Test-Path $zipFile){ Remove-Item $zipFile -Force }

$zipUrl = "https://github.com/AllenWang730/1swj/archive/refs/heads/$Branch.zip"
try {
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipFile -UseBasicParsing
} catch {
    FAIL "下载失败: $($_.Exception.Message)`n请检查网络/代理，或手动下载: $zipUrl"
}
OK "ZIP 已下载 ($(Get-Item $zipFile | Select-Object -ExpandProperty Length) bytes)"

Expand-Archive -Path $zipFile -DestinationPath $workDir -Force
Remove-Item $zipFile -Force
# GitHub ZIP 解压后目录名格式: 1swj-<branch名>
$innerDir = Get-ChildItem $workDir -Directory | Select-Object -First 1
if(-not $innerDir){ FAIL '解压后未找到子目录' }
# 把内层目录内容提到 workDir 根
Get-ChildItem $innerDir.FullName -Force | ForEach-Object { Move-Item $_.FullName $workDir -Force }
Remove-Item $innerDir.FullName -Recurse -Force
Set-Location $workDir
OK "代码已就绪: $workDir"

# ---- 3. NuGet restore ----
S '3/5 NuGet 还原 (生成 project.assets.json，解决 NETSDK1004)'
if($UseCN){
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget-cn"  value="https://nuget.cdn.azure.cn/v3/index.json" protocolVersion="3" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json"   protocolVersion="3" />
  </packageSources>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
'@ | Set-Content NuGet.Config -Encoding UTF8
    OK '已写入国内 NuGet 镜像'
}
dotnet restore CraneLoadingSystem.sln --verbosity minimal 2>&1 | Out-Host
if($LASTEXITCODE -ne 0){ FAIL 'dotnet restore 失败，请检查网络或重试加 -UseCN $false' }
$assets = 'src\CraneLoadingSystem\obj\project.assets.json'
if(Test-Path $assets){ OK "NETSDK1004 已消除 ($(Get-Item $assets | Select-Object -ExpandProperty Length) bytes)" }
else { FAIL "restore 宣称成功但 $assets 仍不存在" }

# ---- 4. build ----
S "4/5 编译 ($Config)"
dotnet build CraneLoadingSystem.sln -c $Config --no-restore --verbosity minimal 2>&1 | Out-Host
if($LASTEXITCODE -ne 0){ FAIL "编译失败，请检查上方错误输出" }
OK '编译成功'

# ---- 5. run ----
if($Run){
    S '5/5 启动系统 (仿真模式，无需 PLC/串口/ERP)'
    OK '关闭主窗口即退出'
    dotnet run --project src/CraneLoadingSystem/CraneLoadingSystem.csproj -c $Config --no-build 2>&1 | Out-Host
} else {
    S '5/5 跳过启动 (-Run:$false)'
    OK "手动启动: dotnet run --project src/CraneLoadingSystem/CraneLoadingSystem.csproj -c $Config --no-build"
}

Write-Host "`nDone $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
