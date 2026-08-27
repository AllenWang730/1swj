<#
.SYNOPSIS
  CraneLoadingSystem — 每次变更后统一入口：自动拉最新 → 编译 → 启动
  （本地有 git 用 git pull，没 git 自动退 ZIP 下载；粘贴执行/保存为 ps1 都可）

.DESCRIPTION
  为同事异地测试专门写的"零配置脚本"：
    · 工作目录：参数优先 → 当前目录有 .sln → 脚本所在目录 → Desktop\1swj（兜底创建）
    · 拉取模式：
        ① 本地已有仓库且有 git → git fetch + 比较 + pull（落后才拉，stash 本地改动）
        ② 本地无仓库 + 有 git → 浅克隆 clone --depth 1
        ③ 无 git / git 失败 → ZIP 双镜像下载 + 解压覆盖
    · 拉取成功后：还原 NuGet → 编译 Release → 启动 CraneLoadingSystem

.PARAMETER WorkDir
  可选：指定工作目录（代码存放位置）。不传则自动选。

.PARAMETER Repo
  可选：GitHub 仓库 owner/name，默认 AllenWang730/1swj

.PARAMETER Branch
  可选：分支，默认 master

.PARAMETER NoBuild
  可选：仅拉取不编译不运行（默认 $false = 拉取+编译+启动）

.PARAMETER Config
  可选：Debug/Release，默认 Release

.EXAMPLE
  最常用（PowerShell 直接粘贴回车，连文件都不用保存）：
    irm https://raw.githubusercontent.com/AllenWang730/1swj/master/deploy/更新并运行.ps1 | iex

.EXAMPLE
  传参指定目录：
    powershell -ExecutionPolicy Bypass -File .\更新并运行.ps1 -WorkDir "D:\Code\1swj"
#>
param(
    [string]$WorkDir = "",
    [string]$Repo    = "AllenWang730/1swj",
    [string]$Branch  = "master",
    [switch]$NoBuild = $false,
    [string]$Config  = "Release"
)

# ====== 基础设置 ======
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"   # 大文件下载性能提升 10x+

$RepoUrl   = "https://github.com/$Repo.git"
$ZipUrls   = @(
    "https://github.com/$Repo/archive/refs/heads/$Branch.zip",
    "https://codeload.github.com/$Repo/zip/refs/heads/$Branch"
)
$Csproj    = "src\CraneLoadingSystem\CraneLoadingSystem.csproj"

function Write-Step($Text){ Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok  ($Text){ Write-Host "  ✓ $Text" -ForegroundColor Green }
function Write-Wrn ($Text){ Write-Host "  ⚠ $Text" -ForegroundColor Yellow }
function Fail($Msg){
    Write-Host "`n❌ $Msg" -ForegroundColor Red
    Write-Host "   把整个窗口截图/复制红字，发回给维护人员。" -ForegroundColor DarkYellow
    Read-Host "按回车键退出"
    exit 1
}

# ==================== [0] 容错定位工作目录 ====================
Write-Step "0/7 定位工作目录"
function PickBaseDir($arg){
    # 1) 传参优先（先造父目录再造目标）
    if($arg){
        $parent = Split-Path $arg -Parent
        if(-not $parent){ $parent = "." }
        try { New-Item -ItemType Directory -Force -Path $parent -ErrorAction Stop | Out-Null } catch {}
        try { New-Item -ItemType Directory -Force -Path $arg    -ErrorAction Stop | Out-Null } catch {}
        if(Test-Path $arg){ return (Resolve-Path $arg).Path }
    }
    # 2) 当前 pwd 有 .sln → 已在仓库根
    if(Test-Path (Join-Path (Get-Location) "CraneLoadingSystem.sln")){ return (Get-Location).Path }
    # 3) $PSScriptRoot（脚本被保存为 ps1 时才存在）
    if($PSScriptRoot -and (Test-Path $PSScriptRoot)){
        $cand = Resolve-Path (Join-Path $PSScriptRoot "..") -ErrorAction SilentlyContinue
        if($cand -and (Test-Path (Join-Path $cand.Path "CraneLoadingSystem.sln"))){ return $cand.Path }
        if(Test-Path (Join-Path $PSScriptRoot "CraneLoadingSystem.sln")){ return $PSScriptRoot }
    }
    # 4) 兜底
    foreach($f in @(
        (Join-Path $env:USERPROFILE "Desktop\1swj"),
        (Join-Path $env:USERPROFILE "1swj"),
        (Join-Path $env:TEMP "1swj")
    )){
        try { New-Item -ItemType Directory -Force -Path $f -ErrorAction Stop | Out-Null
              return (Resolve-Path $f).Path } catch { continue }
    }
    throw "所有候选目录都不可写，请用 -WorkDir 指定一个有权限的目录"
}
$WorkDir = PickBaseDir $WorkDir
Set-Location $WorkDir -ErrorAction Stop
Write-Ok "工作目录：$(Get-Location)"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  CraneLoadingSystem  更新并运行"                              -ForegroundColor Cyan
Write-Host "  $RepoUrl  @  $Branch  ·  $Config"                      -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan

# ==================== [1] 探测环境 ====================
Write-Step "1/7 探测环境"
$hasGit = $false
try {
    $gitVer = & git --version 2>$null
    if ($LASTEXITCODE -eq 0 -and $gitVer -match "git version") {
        $hasGit = $true
        Write-Ok "Git 可用：$gitVer"
    }
} catch { Write-Wrn "未检测到 git（或未加入 PATH）" }

$sdks = $null
try { $sdks = & dotnet --list-sdks 2>$null } catch {}
$net10 = $sdks | Where-Object { $_ -match "^\s*10\." }
if (-not $net10 -and -not $NoBuild) {
    Write-Host "  当前已安装 SDK：" -ForegroundColor Yellow
    $sdks | ForEach-Object { Write-Host "    - $_" -ForegroundColor DarkYellow }
    Fail "未检测到 .NET 10 SDK。请安装 Build apps - SDK x64：`nhttps://dotnet.microsoft.com/download/dotnet/10.0"
}
if (-not $NoBuild) { Write-Ok ".NET 10 SDK OK ($($net10 | Select-Object -First 1))" }

# ==================== [2] 拉取：优先 git，回退 ZIP ====================
$pullMode = "zip"
if ($hasGit) {
    $gitDir = Join-Path $WorkDir ".git"
    if (Test-Path $gitDir) {
        Write-Step "2/7 本地已有仓库 → git fetch + 对比"
        $lockFile = Join-Path $gitDir "index.lock"
        if (Test-Path $lockFile) { Remove-Item -LiteralPath $lockFile -Force -ErrorAction SilentlyContinue; Write-Wrn "清理 index.lock" }
        $curBranch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
        if ($curBranch -ne $Branch) {
            Write-Wrn "当前分支 $curBranch ≠ $Branch，切换..."
            git checkout $Branch 2>$null
            if ($LASTEXITCODE -ne 0) { Write-Wrn "切换失败，回退 ZIP 模式"; $hasGit = $false }
        }
        if ($hasGit) {
            try { git fetch origin --prune } catch {}
            if ($LASTEXITCODE -ne 0) { Write-Wrn "git fetch 失败，回退 ZIP"; $hasGit = $false } else {
                $local  = (git rev-parse HEAD 2>$null).Trim()
                $remote = (git rev-parse "origin/$Branch" 2>$null).Trim()
                if ($local -eq $remote) {
                    Write-Ok "已是最新（本地 $($local.Substring(0,7)) == 远程 $($remote.Substring(0,7))）"
                } else {
                    $behind = (git rev-list --count "HEAD..origin/$Branch" 2>$null).Trim()
                    Write-Wrn "落后 $behind 个 commit → git pull origin $Branch"
                    $stashNeed = (git status --porcelain 2>$null)
                    if ($stashNeed) {
                        Write-Wrn "本地有未提交改动 → 自动 stash（完成后尝试 pop）"
                        git stash push -u -m "auto-stash before 1swj auto-pull" 2>$null
                    }
                    git pull --ff-only origin $Branch
                    if ($LASTEXITCODE -ne 0) { Fail "git pull 失败（冲突/非 fast-forward）。建议：备份后删除 $WorkDir 重跑本脚本" }
                    $sl = (git stash list 2>$null)
                    if ($sl -and $sl[0] -like "*auto-stash before 1swj auto-pull*") {
                        Write-Wrn "恢复 stash（git stash pop）"
                        git stash pop 2>$null
                        if ($LASTEXITCODE -ne 0) { Write-Wrn "stash pop 冲突，请手动解决" }
                    }
                    Write-Host ""
                    Write-Host "  最近 3 条提交：" -ForegroundColor Cyan
                    git log --oneline -3 2>$null | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
                }
                $pullMode = "git"
            }
        }
    } else {
        Write-Step "2/7 本地无仓库 → git clone --depth 1"
        git clone --depth 1 -b $Branch $RepoUrl $WorkDir
        if ($LASTEXITCODE -eq 0) { Write-Ok "clone 完成"; $pullMode = "git" }
        else { Write-Wrn "clone 失败，回退 ZIP 模式" }
    }
}

if (-not $hasGit) {
    Write-Step "2/7 ZIP 下载模式（双镜像重试）"
    $zipFile = Join-Path $WorkDir "_src.zip"
    Remove-Item -LiteralPath $zipFile -Force -ErrorAction SilentlyContinue
    # 安全清理源码层（保留 bin/obj/logs 等产物）
    foreach ($d in @("src","docs","deploy")) {
        $p = Join-Path $WorkDir $d
        if (Test-Path $p) { Remove-Item -Recurse -Force $p -ErrorAction SilentlyContinue }
    }
    Remove-Item -Path (Join-Path $WorkDir "*.sln") -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $WorkDir "*.md")  -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $WorkDir "*.ps1") -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $WorkDir "*.bat") -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Directory $WorkDir -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "1swj-*" } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    $ok = $false
    foreach ($url in $ZipUrls) {
        Write-Host "  尝试：$url"
        try {
            Invoke-WebRequest -Uri $url -OutFile $zipFile -UseBasicParsing -TimeoutSec 180 -ErrorAction Stop
            if ((Get-Item $zipFile).Length -ge 10KB) { $ok = $true ; break }
            Write-Wrn "下载过小，重试下一镜像"
        } catch { Write-Wrn "失败：$($_.Exception.Message)" }
    }
    if (-not $ok) { Fail "两个 ZIP 镜像都不可用，请检查网络/代理；或手动下载 $($ZipUrls[0]) 放到 $zipFile" }
    Write-Ok "ZIP OK ($([math]::Round((Get-Item $zipFile).Length/1MB,1)) MB)"

    try { Expand-Archive -Path $zipFile -DestinationPath $WorkDir -Force -ErrorAction Stop }
    catch { Fail "解压失败：$($_.Exception.Message)" }
    $inners = Get-ChildItem -Directory $WorkDir -ErrorAction SilentlyContinue |
              Where-Object { $_.Name -ne "_src" -and $_.Name -ne "bin" -and $_.Name -ne "obj" -and $_.Name -ne "logs" }
    if ($inners.Count -eq 1) {
        $inner = $inners[0].FullName
        Write-Host "  提取嵌套目录 $($inners[0].Name) → 仓库根"
        Get-ChildItem -LiteralPath $inner -Force | ForEach-Object {
            Move-Item -LiteralPath $_.FullName -Destination $WorkDir -Force -ErrorAction SilentlyContinue
        }
        Remove-Item -LiteralPath $inner -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $zipFile -Force -ErrorAction SilentlyContinue
    $pullMode = "zip"
}

# ==================== [3] 校验项目文件 ====================
Write-Step "3/7 校验项目文件"
if (-not (Test-Path $Csproj)) { Fail "找不到 $Csproj（拉取/解压后目录结构不对）" }
Write-Ok "项目文件就位（拉取模式：$pullMode）"

if ($NoBuild) {
    Write-Host ""
    Write-Ok "已按 -NoBuild 完成拉取，未编译未运行。"
    Write-Host "`n完成  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
    Read-Host "按回车键退出"
    exit 0
}

# ==================== [4/5] 还原 + 编译 ====================
Write-Step "4/7 还原 NuGet"
dotnet restore $Csproj --verbosity minimal
if ($LASTEXITCODE -ne 0) { Fail "NuGet 还原失败（公司内网需设置 HTTP_PROXY / HTTPS_PROXY）" }
Write-Ok "还原完成"

Write-Step "5/7 编译 $Config"
dotnet build $Csproj -c $Config --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { Fail "编译失败，请把上方红色错误行贴回给维护人员" }
Write-Ok "编译成功"

# ==================== [6/7] 启动 ====================
Write-Step "6/7 启动 CraneLoadingSystem（仿真模式）"
Write-Host "  日志目录：$(Join-Path (Get-Location) "logs")" -ForegroundColor DarkGray
Write-Host "  关闭主窗口后脚本自动退出" -ForegroundColor DarkGray
Write-Host ""
dotnet run --project $Csproj -c $Config --no-build
if ($LASTEXITCODE -ne 0) { Write-Wrn "进程退出码：$LASTEXITCODE" }

Write-Host "`n============================================================" -ForegroundColor Cyan
Write-Host "  完成  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Read-Host "按回车键退出"
exit 0
