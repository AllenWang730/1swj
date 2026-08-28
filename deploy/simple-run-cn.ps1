<#
 CraneLoadingSystem — China Mirror Optimized Entry (PURE ASCII)
   * All remote URLs come with 3+ CN-friendly mirrors (tried in order).
   * Default flow: ZIP download (fastest in CN) -> compile -> run.
   * Falls back to git-clone if ZIP mirrors are unreachable.
   * Zero Chinese characters (works on any PowerShell 5 codepage).

 Usage:
   # 1) Download then run:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\simple-run-cn.ps1

   # 2) Pull only, no build / no run
   powershell -NoProfile -ExecutionPolicy Bypass -File .\simple-run-cn.ps1 -NoBuild

   # 3) Custom working folder
   powershell -NoProfile -ExecutionPolicy Bypass -File .\simple-run-cn.ps1 -WorkDir "D:\Crane\1swj"
#>
param(
    [string]$WorkDir = "",
    [string]$Repo    = "AllenWang730/1swj",
    [string]$Branch  = "master",
    [switch]$NoBuild = $false,
    [string]$Config  = "Release"
)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"

# ============================================================
# CN MIRROR TABLE — (fastest mirrors first, official last)
# ============================================================
# 1) Zip (repo archive) — official GitHub only
$ZIP_MIRRORS = @(
    "https://codeload.github.com/$Repo/zip/refs/heads/$Branch",
    "https://github.com/$Repo/archive/refs/heads/$Branch.zip"
)
# 2) Git clone — official GitHub only
$GIT_MIRRORS = @(
    "https://github.com/$Repo.git"
)

$CSPROJ = "src\CraneLoadingSystem\CraneLoadingSystem.csproj"
$SLN    = "CraneLoadingSystem.sln"

# ------------------------------------------------------------
# Helpers
# ------------------------------------------------------------
function S($t){ Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function O($t){ Write-Host "  OK  : $t" -ForegroundColor Green }
function W($t){ Write-Host "  WARN: $t" -ForegroundColor Yellow }
function F($m){ Write-Host "`nFAIL : $m" -ForegroundColor Red; Read-Host "Press Enter to exit"; exit 1 }
function SizeMB($p){ return [math]::Round((Get-Item $p).Length/1MB,1) }

# ------------------------------------------------------------
# 0) Pick working dir
# ------------------------------------------------------------
S "0/7 Resolve working directory"
function PickDir($a){
    if($a){
        $p = Split-Path $a -Parent
        if(-not $p){ $p = "." }
        try { New-Item -ItemType Directory -Force -Path $p -ErrorAction Stop | Out-Null } catch {}
        try { New-Item -ItemType Directory -Force -Path $a -ErrorAction Stop | Out-Null } catch {}
        if(Test-Path $a){ return (Resolve-Path $a).Path }
    }
    if(Test-Path (Join-Path (Get-Location) $SLN)){ return (Get-Location).Path }
    if($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot $SLN))){ return (Resolve-Path $PSScriptRoot).Path }
    foreach($f in @(
        (Join-Path $env:USERPROFILE "Desktop\1swj"),
        (Join-Path $env:USERPROFILE "Desktop\1swj_run"),
        (Join-Path $env:USERPROFILE "1swj"),
        (Join-Path $env:TEMP "1swj")
    )){
        try { New-Item -ItemType Directory -Force -Path $f -ErrorAction Stop | Out-Null; return (Resolve-Path $f).Path } catch { continue }
    }
    throw "No writable directory. Pass -WorkDir 'D:\some\path' explicitly."
}
$WorkDir = PickDir $WorkDir
try { Set-Location $WorkDir -ErrorAction Stop } catch { F "Cannot cd into $WorkDir" }
O "WorkDir: $(Get-Location)"
Write-Host "CraneLoadingSystem  $Repo @ $Branch   mode=$Config  NoBuild=$NoBuild" -ForegroundColor Cyan

# ------------------------------------------------------------
# 1) Environment
# ------------------------------------------------------------
S "1/7 Detect environment"
$hasGit = $false
try {
    $gv = & git --version 2>$null
    if($LASTEXITCODE -eq 0 -and $gv -match "git version"){ $hasGit = $true; O "Git: $gv" }
} catch {}

$net10 = $false
try {
    & dotnet --list-sdks 2>$null | ForEach-Object { if($_ -match "^\s*10\."){ $net10 = $true } }
} catch {}
if(-not $NoBuild -and -not $net10){
    F ".NET 10 SDK missing. Install: https://dotnet.microsoft.com/download/dotnet/10.0  (SDK x64 Build Apps)"
}
if(-not $NoBuild){ O ".NET 10 SDK available" }

# ------------------------------------------------------------
# 2) Fetch source (mirrors)
# ------------------------------------------------------------
$pullMode = ""

# 2a) Local git repo already exists: prefer git pull (with CN mirror remote if available)
if($hasGit -and (Test-Path ".git")){
    S "2/7 Local repo found -> fetch + compare"
    $lock = ".git\index.lock"
    if(Test-Path $lock){ Remove-Item $lock -Force -ErrorAction SilentlyContinue; W "Removed stale index.lock" }

    $cur = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
    if($cur -ne $Branch){
        W "Switch branch $cur -> $Branch"
        git checkout $Branch 2>$null
        if($LASTEXITCODE -ne 0){ F "git checkout $Branch failed" }
    }

    # Ensure there is a fetchable remote
    $fetched = $false
    $origUrl = git remote get-url origin 2>$null
    foreach($url in @($origUrl) + $GIT_MIRRORS){
        if(-not $url){ continue }
        Write-Host "  fetch via: $url" -ForegroundColor DarkGray
        try { git fetch origin $Branch --prune --update-shallow 2>$null } catch {}
        if($LASTEXITCODE -eq 0){ $fetched = $true ; break }
        # Also attempt: override URL for one fetch
        try { git -c "url.$url.insteadof=$origUrl" fetch origin $Branch --prune 2>$null } catch {}
        if($LASTEXITCODE -eq 0){ $fetched = $true ; break }
    }
    if($fetched){
        $local  = (git rev-parse HEAD 2>$null).Trim()
        $remote = (git rev-parse "origin/$Branch" 2>$null).Trim()
        if($local -eq $remote){
            O "Already up to date  $($local.Substring(0,7))"
        } else {
            $n = (git rev-list --count "HEAD..origin/$Branch" 2>$null).Trim()
            W "Behind by $n commit(s) -> fast-forward pull"
            $dirty = $false
            if((git status --porcelain 2>$null).Count -gt 0){ $dirty = $true; W "auto-stash local changes"; git stash push -u -m "auto-stash-1swj" 2>$null }
            git pull --ff-only origin $Branch
            if($LASTEXITCODE -ne 0){ F "git pull failed. Delete $WorkDir and retry." }
            if($dirty -and (git stash list 2>$null)[0] -like "*auto-stash-1swj*"){ W "stash pop"; git stash pop 2>$null }
            Write-Host "Recent 3 commits:" -ForegroundColor Cyan
            git log --oneline -3 2>$null | % { Write-Host "    $_" -ForegroundColor Gray }
        }
        $pullMode = "git-pull"
    } else {
        W "All git mirrors unreachable -> fallback to ZIP download"
    }
}

# 2b) No local git repo OR git fetch failed -> try ZIP download (CN mirrors FIRST)
if(-not $pullMode){
    S "2/7 Download source (CN mirrors prioritized)"
    $zipFile = Join-Path $WorkDir "_src.zip"
    Remove-Item $zipFile -Force -ErrorAction SilentlyContinue

    # Clean existing source dirs/files so ZIP extraction is pristine
    foreach($d in @("src","docs","deploy","tests")){
        $p = Join-Path $WorkDir $d
        if(Test-Path $p){ Remove-Item -Recurse -Force $p -ErrorAction SilentlyContinue }
    }
    Get-ChildItem -File $WorkDir -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in ".sln",".md",".props",".editorconfig",".gitignore",".ps1",".bat",".cmd" } |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    Get-ChildItem -Directory $WorkDir -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "1swj-*" -or $_.Name -like "_u*" -or $_.Name -like "*-update" } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

    $zipOk = $false
    foreach($u in $ZIP_MIRRORS){
        Write-Host "  -> $u"
        try {
            $ProgressPreference = "SilentlyContinue"
            Invoke-WebRequest -Uri $u -OutFile $zipFile -UseBasicParsing -TimeoutSec 240 -ErrorAction Stop
            if((Get-Item $zipFile).Length -ge 10000){ $zipOk = $true ; break }
            W "  file too small, try next mirror"
        } catch {
            W "  failed: $($_.Exception.Message)"
        }
    }
    if(-not $zipOk -and $hasGit){
        W "All ZIP mirrors failed -> try git clone mirrors"
        # git clone over CN mirrors (shallow)
        foreach($u in $GIT_MIRRORS){
            Write-Host "  -> clone: $u"
            try {
                git clone --depth 1 -b $Branch $u $WorkDir 2>$null
            } catch {}
            if($LASTEXITCODE -eq 0 -and (Test-Path (Join-Path $WorkDir $CSPROJ))){
                $pullMode = "git-clone"; break
            }
        }
        if(-not $pullMode){ F "All ZIP + Git clone mirrors unreachable" }
    } elseif(-not $zipOk){
        F "All ZIP mirrors unreachable (and git not available)."
    }

    if($zipOk){
        O "ZIP downloaded  ($(SizeMB $zipFile) MB)"
        # 3) Extract
        $tmp = Join-Path $WorkDir "_u"
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
        try {
            Expand-Archive -Path $zipFile -DestinationPath $tmp -Force -ErrorAction Stop
        } catch { F "ZIP expand failed: $($_.Exception.Message)" }
        # GitHub adds a "1swj-master" nested dir; lift it out
        $inner = Get-ChildItem -Directory $tmp -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin @("bin","obj","logs") } | Select-Object -First 1
        $src = if($inner){ $inner.FullName } else { $tmp }
        Write-Host "  Extract from inner: $src" -ForegroundColor DarkGray

        Get-ChildItem -LiteralPath $src -Force | ForEach-Object {
            $dst = Join-Path $WorkDir $_.Name
            if($_.PSIsContainer){
                if($_.Name -in @("bin","obj","logs","_release")){ return }
                robocopy $_.FullName $dst /E /NFL /NDL /NJH /NJS /R:2 | Out-Null
            } else {
                Copy-Item -LiteralPath $_.FullName -Destination $dst -Force -ErrorAction SilentlyContinue
            }
        }
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
        Remove-Item -Force $zipFile -ErrorAction SilentlyContinue
        $pullMode = "zip"
    }
}

# ------------------------------------------------------------
# 3) Verify + Build + Run
# ------------------------------------------------------------
S "3/7 Verify project"
if(-not (Test-Path $CSPROJ)){ F "$CSPROJ not found — layout broken" }
O "Project ready (mode: $pullMode)"

if($NoBuild){
    O "-NoBuild requested. Stopping."
    Write-Host "`nDone (pull only). Source is at: $(Get-Location)" -ForegroundColor Green
    Read-Host "Press Enter to exit"
    exit 0
}

S "4/7 dotnet restore"
dotnet restore $CSPROJ --verbosity minimal
if($LASTEXITCODE -ne 0){ F "restore failed (corporate proxy? set HTTP_PROXY / HTTPS_PROXY env vars)" }
O "Restore OK"

S "5/7 dotnet build $Config"
dotnet build $CSPROJ -c $Config --no-restore --verbosity minimal
if($LASTEXITCODE -ne 0){ F "build FAILED — paste the RED error lines back to maintainers" }
O "Build OK"

S "6/7 dotnet run  (simulation mode)"
Write-Host "  Logs : $(Join-Path (Get-Location) logs)" -ForegroundColor DarkGray
Write-Host "  Quit : close the main window" -ForegroundColor DarkGray
Write-Host ""
dotnet run --project $CSPROJ -c $Config --no-build
if($LASTEXITCODE -ne 0){ W "Exit code = $LASTEXITCODE" }

Write-Host "`nDone  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
Read-Host "Press Enter to exit"
