<#
.SYNOPSIS
  CraneLoadingSystem  从 GitHub 拉取最新代码 + 自动编译 + 运行
  （适用于：异地机器已/未装 Git，都能跑）

.DESCRIPTION
  优先级：
    1) 本地已有仓库 → 先 fetch 对比 → 有更新则 pull（保留 git 历史）
    2) 本地无仓库但有 git 命令 → 全新 git clone
    3) 无 git → 回退到 ZIP 下载 + 解压（纯 PowerShell，不依赖外部程序）
  拉取完成后：还原 NuGet → 编译 Release → 启动 CraneLoadingSystem

.PARAMETER WorkDir
  工作目录（代码存放处），默认：$env:USERPROFILE\Desktop\1swj

.PARAMETER Repo
  GitHub 仓库（owner/repo），默认：AllenWang730/1swj

.PARAMETER Branch
  分支，默认：master

.PARAMETER NoBuild
  仅拉取，不编译不运行（默认 $false = 拉取+编译+启动）

.EXAMPLE
  最常用：直接运行
    .\拉取并运行.ps1

.EXAMPLE
  只想拉取不运行：
    .\拉取并运行.ps1 -NoBuild
#>
param(
    [string]$WorkDir  = "$env:USERPROFILE\Desktop\1swj",
    [string]$Repo     = "AllenWang730/1swj",
    [string]$Branch   = "master",
    [switch]$NoBuild  = $false,
    [string]$Config   = "Release"
)

# ====== 基础设置 ======
[Net.ServicePointManager]::SecurityProtocol =
    [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # 下载提速 10x+

$RepoUrl   = "https://github.com/$Repo.git"
$ZipUrls   = @(
    "https://github.com/$Repo/archive/refs/heads/$Branch.zip",
    "https://codeload.github.com/$Repo/zip/refs/heads/$Branch"
)
$Csproj    = "src\CraneLoadingSystem\CraneLoadingSystem.csproj"

function Write-Step($Text) { Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok  ($Text) { Write-Host "  ✓ $Text" -ForegroundColor Green }
function Write-Wrn ($Text) { Write-Host "  ⚠ $Text" -ForegroundColor Yellow }
function Fail($Msg) {
    Write-Host "`n❌ $Msg" -ForegroundColor Red
    Read-Host "按回车键退出"
    exit 1
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  CraneLoadingSystem  拉取 GitHub + 编译 + 运行"               -ForegroundColor Cyan
Write-Host "  $RepoUrl  @  $Branch"                                       -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan

# ==================== [0] 准备目录 ====================
Write-Step "0/7 准备工作目录：$WorkDir"
if (-not (Test-Path $WorkDir)) {
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
}
Set-Location $WorkDir
Write-Ok "当前目录：$(Get-Location)"

# ==================== [1] 探测 Git 是否可用 ====================
Write-Step "1/7 探测环境"
$hasGit = $false
try {
    $gitVer = & git --version 2>$null
    if ($LASTEXITCODE -eq 0 -and $gitVer -match "git version") {
        $hasGit = $true
        Write-Ok "Git 可用：$gitVer"
    } else { Write-Wrn "git 命令返回非预期结果，回退到 ZIP 下载模式" }
} catch { Write-Wrn "未检测到 git（没有安装或未加入 PATH），回退到 ZIP 下载模式" }

# ==================== [2] 方法 A：用 Git 拉取（有 git 时）====================
$pullMode = "zip"
if ($hasGit) {
    $gitDir = Join-Path $WorkDir ".git"
    if (Test-Path $gitDir) {
        # —— 本地已有仓库 ——
        Write-Step "2/7 本地已有仓库，检测远程更新（git fetch + 比较）"

        # 清理可能的 index.lock 残留（避免 fatal: Unable to create '...index.lock': File exists.）
        $lockFile = Join-Path $gitDir "index.lock"
        if (Test-Path $lockFile) {
            try {
                Remove-Item -LiteralPath $lockFile -Force -ErrorAction Stop
                Write-Wrn "清理遗留 git 锁文件：index.lock"
            } catch {
                Write-Wrn "index.lock 存在但删除失败（可能有其他 git 进程在操作）：$($_.Exception.Message)"
            }
        }

        # 检查分支是否正确（不在 master 则自动切）
        $curBranch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
        if ($curBranch -ne $Branch) {
            Write-Wrn "当前分支 $curBranch ≠ 目标 $Branch，切换到 $Branch"
            git checkout $Branch 2>$null
            if ($LASTEXITCODE -ne 0) { Fail "切换分支 $Branch 失败" }
        }

        # fetch 远程
        try { git fetch origin --prune } catch {}
        if ($LASTEXITCODE -ne 0) {
            Write-Wrn "git fetch 失败（网络不可达？），回退到 ZIP 下载模式"
            $hasGit = $false
        } else {
            # 比较本地 HEAD 与 origin/$Branch
            $local  = (git rev-parse HEAD 2>$null).Trim()
            $remote = (git rev-parse "origin/$Branch" 2>$null).Trim()
            if ($local -eq $remote) {
                Write-Ok "已是最新（本地 $($local.Substring(0,7)) == 远程 $($remote.Substring(0,7))）"
            } else {
                $behind = (git rev-list --count "HEAD..origin/$Branch" 2>$null).Trim()
                Write-Wrn "落后 $behind 个提交，正在 git pull origin $Branch"

                # 若本地有未提交改动 → stash（以防用户本地改了东西）
                $statusShort = (git status --porcelain 2>$null)
                if ($statusShort) {
                    Write-Wrn "本地有未提交改动，执行 git stash 暂存（拉取完成后会尝试 stash pop）"
                    git stash push -u -m "auto-stash before CraneLoadingSystem auto-pull" 2>$null
                }

                git pull --ff-only origin $Branch
                if ($LASTEXITCODE -ne 0) {
                    Fail "git pull 失败（可能有合并冲突/非 fast-forward）。`n建议：备份本地重要文件后手动删除 $WorkDir 重跑本脚本"
                }

                # 恢复 stash（如果有暂存）
                $stashList = (git stash list 2>$null)
                if ($stashList -and $stashList[0] -like "*auto-stash before CraneLoadingSystem auto-pull*") {
                    Write-Wrn "尝试恢复暂存的本地改动（git stash pop）"
                    git stash pop 2>$null
                    if ($LASTEXITCODE -ne 0) {
                        Write-Wrn "stash pop 有冲突，请手动解决（冲突文件已标记）"
                    }
                }

                # 打印最近 3 条提交（给用户看拉到了什么）
                Write-Host ""
                Write-Host "  最近 3 条提交：" -ForegroundColor Cyan
                git log --oneline -3 2>$null | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
            }
            $pullMode = "git"
        }
    } else {
        # —— 本地无仓库 → 全新 clone ——
        Write-Step "2/7 本地无仓库 → git clone"
        git clone --depth 1 -b $Branch $RepoUrl $WorkDir
        if ($LASTEXITCODE -ne 0) {
            Write-Wrn "git clone 失败（$Repo 不可达/网络受限），回退到 ZIP 下载模式"
            $hasGit = $false
        } else {
            Write-Ok "clone 完成（浅克隆 --depth 1）"
            $pullMode = "git"
        }
    }
}

# ==================== [3] 方法 B：ZIP 下载（无 git / git 失败时回退）====================
if (-not $hasGit) {
    Write-Step "2/7 ZIP 下载模式（无 git / git 失败回退）"
    $zipFile = Join-Path $WorkDir "_src.zip"

    # ZIP 模式需要清旧源码（否则与新解压混在一起）
    foreach ($d in @("src","docs","deploy")) {
        $p = Join-Path $WorkDir $d
        if (Test-Path $p) { Remove-Item -Recurse -Force $p -ErrorAction SilentlyContinue }
    }
    Remove-Item -Path (Join-Path $WorkDir "*.sln") -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $WorkDir "*.md")  -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $WorkDir "*.bat") -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $WorkDir "*.ps1") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $zipFile -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Directory $WorkDir | Where-Object { $_.Name -like "1swj-*" } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    # 双镜像重试
    $ok = $false
    foreach ($url in $ZipUrls) {
        Write-Host "  尝试：$url"
        try {
            Invoke-WebRequest -Uri $url -OutFile $zipFile -UseBasicParsing -TimeoutSec 180 -ErrorAction Stop
            if ((Get-Item $zipFile).Length -ge 10KB) { $ok = $true ; break }
            Write-Wrn "下载产物过小，重试下一镜像"
        } catch {
            Write-Wrn "失败：$($_.Exception.Message)"
        }
    }
    if (-not $ok) { Fail "两个 ZIP 镜像都不可用，请检查网络/代理；或手动下载 ZIP 放到 $zipFile" }
    $mb = [math]::Round((Get-Item $zipFile).Length/1MB,1)
    Write-Ok "ZIP OK ($mb MB)"

    # 解压 + 提取嵌套目录
    try { Expand-Archive -Path $zipFile -DestinationPath $WorkDir -Force -ErrorAction Stop }
    catch { Fail "解压失败：$($_.Exception.Message)" }
    $inners = Get-ChildItem -Directory $WorkDir | Where-Object { $_.Name -ne "_src" }
    if ($inners.Count -eq 1) {
        $inner = $inners[0].FullName
        Write-Host "  提取嵌套目录 $($inners[0].Name) → 工作目录根"
        Get-ChildItem -LiteralPath $inner -Force | ForEach-Object {
            Move-Item -LiteralPath $_.FullName -Destination $WorkDir -Force -ErrorAction SilentlyContinue
        }
        Remove-Item -LiteralPath $inner -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $zipFile -Force -ErrorAction SilentlyContinue
    $pullMode = "zip"
}

# ==================== [4] 校验项目文件 ====================
Write-Step "3/7 校验项目文件"
if (-not (Test-Path $Csproj)) { Fail "找不到 $Csproj（拉取/解压后目录结构不对）" }
Write-Ok "项目文件就位：$Csproj（拉取模式：$pullMode）"

# ==================== [5] 检查 .NET 10 SDK ====================
if (-not $NoBuild) {
    Write-Step "4/7 检查 .NET 10 SDK"
    $sdks = $null
    try { $sdks = & dotnet --list-sdks 2>$null } catch {}
    $net10 = $sdks | Where-Object { $_ -match "^\s*10\." }
    if (-not $net10) {
        Write-Host "  当前已安装：" -ForegroundColor Yellow
        $sdks | ForEach-Object { Write-Host "    - $_" -ForegroundColor DarkYellow }
        Fail "未检测到 .NET 10 SDK。`n请安装：https://dotnet.microsoft.com/download/dotnet/10.0  （Build apps - SDK x64）"
    }
    Write-Ok ".NET 10 SDK OK ($($net10 | Select-Object -First 1))"

    # ==================== [6] 还原 + 编译 ====================
    Write-Step "5/7 还原 NuGet"
    dotnet restore $Csproj --verbosity minimal
    if ($LASTEXITCODE -ne 0) { Fail "NuGet 还原失败（公司内网需要设置 HTTP_PROXY / HTTPS_PROXY）" }
    Write-Ok "依赖还原完成"

    Write-Step "6/7 编译 $Config"
    dotnet build $Csproj -c $Config --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { Fail "编译失败，把上方红色错误贴回给维护人员" }
    Write-Ok "编译成功"

    # ==================== [7] 启动 ====================
    Write-Step "7/7 启动 CraneLoadingSystem（仿真模式）"
    Write-Host "  日志目录：$(Join-Path (Get-Location) "logs")" -ForegroundColor DarkGray
    Write-Host "  关闭主窗口即退出本脚本" -ForegroundColor DarkGray
    Write-Host ""
    dotnet run --project $Csproj -c $Config --no-build
    if ($LASTEXITCODE -ne 0) { Write-Wrn "进程退出码：$LASTEXITCODE" }
} else {
    Write-Host ""
    Write-Ok "已按 -NoBuild 模式完成拉取，未编译未运行。"
}

Write-Host "`n============================================================" -ForegroundColor Cyan
Write-Host "  完成  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Read-Host "按回车键退出"
