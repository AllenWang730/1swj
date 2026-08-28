<#
 CraneLoadingSystem - ONE-CLICK entry.
   1. Workdir auto resolve (always create first, never Set-Location fail).
   2. Source acquisition (mirrors -> search local disk -> GUI file picker).
   3. Build + run WPF app.
 Pure ASCII -> works on any PowerShell 5 / 7 / any codepage.
#>
param(
  [string]$WorkDir = "",
  [string]$Repo    = "AllenWang730/1swj",
  [string]$Branch  = "master",
  [switch]$NoBuild = $false,
  [string]$Config  = "Release"
)
try   { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls13 -bor [Net.SecurityProtocolType]::Tls12 }
catch { try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {} }
$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"

function S($t){ Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function O($t){ Write-Host "  OK  : $t" -ForegroundColor Green }
function W($t){ Write-Host "  WARN: $t" -ForegroundColor Yellow }
function F($m){ Write-Host "`nFAIL: $m" -ForegroundColor Red; Read-Host "Press Enter to exit"; exit 1 }

# ------------------------------------------------------------
# 0) Working dir - 6-level fallback, always create first.
# ------------------------------------------------------------
S "Resolve working directory"
function PickDir($a){
  if($a){
    $parent = Split-Path $a -Parent; if(-not $parent){ $parent = "." }
    try { New-Item -ItemType Directory -Force $parent -ErrorAction Stop | Out-Null } catch {}
    try { New-Item -ItemType Directory -Force $a      -ErrorAction Stop | Out-Null } catch {}
    if(Test-Path -LiteralPath $a){ return (Resolve-Path -LiteralPath $a).Path }
  }
  try { if(Test-Path (Join-Path (Get-Location) "CraneLoadingSystem.sln")){ return (Get-Location).Path } } catch {}
  foreach($cand in @(
    (Join-Path $env:USERPROFILE "Desktop\1swj"),
    (Join-Path $env:USERPROFILE "Desktop\1swj_run"),
    (Join-Path $env:USERPROFILE "1swj"),
    (Join-Path $env:TEMP      "1swj")
  )){
    try { New-Item -ItemType Directory -Force $cand -ErrorAction Stop | Out-Null; return (Resolve-Path -LiteralPath $cand).Path }
    catch { continue }
  }
  throw "No writable directories. Pass -WorkDir explicitly."
}
$WorkDir = PickDir $WorkDir
try { Set-Location -LiteralPath $WorkDir -ErrorAction Stop } catch { F "Set-Location($WorkDir) failed: $($_.Exception.Message)" }
O "WorkDir = $WorkDir"

$CsprojRel = "src\CraneLoadingSystem\CraneLoadingSystem.csproj"
$CsprojAbs = Join-Path $WorkDir $CsprojRel
$SLN       = Join-Path $WorkDir "CraneLoadingSystem.sln"

# ------------------------------------------------------------
# 1) Source acquisition. Order:
#    A. Already extracted project present -> skip download entirely.
#    B. Download ZIP via mirrors.
#    C. Search local disk for downloaded zip / extracted folder.
#    D. GUI OpenFileDialog picker.
# ------------------------------------------------------------
S "1/5 Source acquisition"
$haveProject = (Test-Path -LiteralPath $CsprojAbs) -and (Test-Path -LiteralPath $SLN)
if($haveProject){
  O "Project already present. To force re-download delete $WorkDir\src."
} else {

  $ZipAbs = Join-Path $WorkDir "_src.zip"
  Remove-Item -LiteralPath $ZipAbs -Force -ErrorAction SilentlyContinue

  # 1-B. URL mirrors (CN-friendly first, GitHub last).
  $Mirrors = @(
    "https://kkgithub.com/$Repo/archive/refs/heads/$Branch.zip",
    "https://mirror.ghproxy.com/https://github.com/$Repo/archive/refs/heads/$Branch.zip",
    "https://ghproxy.com/https://github.com/$Repo/archive/refs/heads/$Branch.zip",
    "https://gh-proxy.com/https://github.com/$Repo/archive/refs/heads/$Branch.zip",
    "https://codeload.github.com/$Repo/zip/refs/heads/$Branch",
    "https://github.com/$Repo/archive/refs/heads/$Branch.zip"
  )
  $downloaded = $false
  $idx = 0
  foreach($u in $Mirrors){
    $idx++
    Write-Host "  [$idx/$($Mirrors.Count)] $u" -ForegroundColor DarkGray
    try {
      Invoke-WebRequest -Uri $u -OutFile $ZipAbs -UseBasicParsing -TimeoutSec 300 -ErrorAction Stop
      $sz = (Get-Item -LiteralPath $ZipAbs).Length
      if($sz -ge 10000){
        O "Downloaded $([math]::Round($sz/1MB,2)) MB"
        $downloaded = $true
        break
      }
      W "file too small ($sz B); next mirror"
      Remove-Item -LiteralPath $ZipAbs -Force -ErrorAction SilentlyContinue
    }
    catch [System.Net.WebException] {
      $resp = $_.Exception.Response
      $info = if($resp){ "HTTP $([int]$resp.StatusCode) $($resp.StatusCode)" } else { $_.Exception.InnerException.Message }
      W "$info"
    }
    catch { W "$($_.Exception.Message)" }
  }

  # 1-C. Auto-search local disk (Downloads / Desktop / common dirs).
  if(-not $downloaded){
    W "All mirrors unreachable. Searching local disks..."
    $probeRoots = @(
      (Join-Path $env:USERPROFILE "Downloads"),
      (Join-Path $env:USERPROFILE "Desktop"),
      (Join-Path $env:USERPROFILE "Documents"),
      $WorkDir,
      "C:\",
      "D:\"
    )
    $foundZip = $null
    $foundDir = $null
    :search foreach($root in $probeRoots){
      if(-not (Test-Path -LiteralPath $root)){ continue }
      Write-Host "  scan: $root" -ForegroundColor DarkGray
      try {
        Get-ChildItem -LiteralPath $root -Filter "*.zip" -File -Recurse -Depth 3 -ErrorAction SilentlyContinue |
          Where-Object { $_.Name -match "1swj|AllenWang" -and $_.Length -ge 10000 } |
          ForEach-Object {
            $foundZip = $_.FullName
            break search
          }
        if(-not $foundZip){
          Get-ChildItem -LiteralPath $root -Directory -Recurse -Depth 3 -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "1swj" -and (Test-Path (Join-Path $_.FullName $CsprojRel)) } |
            ForEach-Object {
              $foundDir = $_.FullName
              break search
            }
        }
      } catch { continue }
    }

    if($foundDir){
      O "Existing source dir found: $foundDir"
      Write-Host "  Copying -> $WorkDir" -ForegroundColor DarkGray
      Get-ChildItem -LiteralPath $foundDir -Force | ForEach-Object {
        $dst = Join-Path $WorkDir $_.Name
        if($_.PSIsContainer){
          if($_.Name -in @("bin","obj","logs")){ return }
          robocopy $_.FullName $dst /E /NFL /NDL /NJH /NJS /R:2 | Out-Null
        } else {
          Copy-Item -LiteralPath $_.FullName -Destination $dst -Force -ErrorAction SilentlyContinue
        }
      }
    }
    elseif($foundZip){
      O "Existing ZIP found: $foundZip"
      Copy-Item -LiteralPath $foundZip -Destination $ZipAbs -Force
      $downloaded = $true
    }

    # 1-D. GUI file picker fallback (works in STA / interactive sessions).
    if(-not $downloaded -and -not $foundDir){
      W "Nothing on disk. Showing File Open dialog..."
      try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Filter           = "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*"
        $dlg.Title            = "Pick 1swj-master.zip (downloaded from GitHub)"
        $dlg.InitialDirectory = Join-Path $env:USERPROFILE "Downloads"
        if($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){
          $picked = $dlg.FileName
          if(Test-Path -LiteralPath $picked){
            if((Get-Item -LiteralPath $picked).Length -lt 10000){ F "Picked ZIP is too small" }
            Copy-Item -LiteralPath $picked -Destination $ZipAbs -Force
            $downloaded = $true
            O "Picked ZIP: $picked"
          }
        }
      }
      catch {
        W "GUI dialog unavailable ($($_.Exception.Message)). Asking manual input..."
      }
      if(-not $downloaded){
        Write-Host ""
        Write-Host "  --> Browser download any of: " -ForegroundColor Yellow
        foreach($u in $Mirrors){ Write-Host "      $u" -ForegroundColor DarkGray }
        Write-Host ""
        $m = Read-Host "  Paste downloaded ZIP full path (blank to exit)"
        if([string]::IsNullOrWhiteSpace($m)){ F "Aborted by user" }
        $m = $m.Trim('"')
        if(-not (Test-Path -LiteralPath $m)){ F "File does not exist: $m" }
        if((Get-Item -LiteralPath $m).Length -lt 10000){ F "ZIP too small" }
        Copy-Item -LiteralPath $m -Destination $ZipAbs -Force
        $downloaded = $true
        O "Manual ZIP: $m"
      }
    }
  }

  # Extract downloaded ZIP into WorkDir (clean old src/deploy/docs first).
  if($downloaded){
    S "Extract ZIP"
    # Purge old source files (preserve bin/obj/logs to keep build cache).
    foreach($d in @("src","deploy","docs","tests")){
      $pp = Join-Path $WorkDir $d
      if(Test-Path -LiteralPath $pp){ Remove-Item -Recurse -Force -LiteralPath $pp -ErrorAction SilentlyContinue }
    }
    Get-ChildItem -File -LiteralPath $WorkDir -ErrorAction SilentlyContinue |
      Where-Object { $_.Extension -in ".sln",".md",".ps1",".bat",".props",".editorconfig",".gitignore",".json",".config" } |
      ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
    Get-ChildItem -Directory -LiteralPath $WorkDir -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -like "1swj-*" -or $_.Name -eq "_u" -or $_.Name -eq "_t" } |
      ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

    $tmp = Join-Path $WorkDir "_u"
    Remove-Item -Recurse -Force -LiteralPath $tmp -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $ZipAbs -DestinationPath $tmp -Force -ErrorAction Stop
    $ins = @(Get-ChildItem -LiteralPath $tmp -Directory -ErrorAction SilentlyContinue)
    $src = if(@($ins).Count -eq 1){ $ins[0].FullName } else { $tmp }
    Write-Host "  Moving $src -> $WorkDir" -ForegroundColor DarkGray
    Get-ChildItem -LiteralPath $src -Force | ForEach-Object {
      $dst = Join-Path $WorkDir $_.Name
      if($_.PSIsContainer){
        robocopy $_.FullName $dst /E /NFL /NDL /NJH /NJS /R:2 | Out-Null
      } else {
        Copy-Item -LiteralPath $_.FullName -Destination $dst -Force -ErrorAction SilentlyContinue
      }
    }
    Remove-Item -Recurse -Force -LiteralPath $tmp -ErrorAction SilentlyContinue
    Remove-Item -Force -LiteralPath $ZipAbs -ErrorAction SilentlyContinue
    O "Extract OK"
  }
}

# ------------------------------------------------------------
# 2) Verify project layout.
# ------------------------------------------------------------
S "2/5 Verify project layout"
# Handle 1swj-master nested folder (when user unzipped without flattening).
if(-not (Test-Path -LiteralPath $CsprojAbs)){
  $nested = Get-ChildItem -LiteralPath $WorkDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName $CsprojRel) } | Select-Object -First 1
  if($nested){
    W "Nested source dir found: $($nested.Name); lifting contents up"
    Get-ChildItem -LiteralPath $nested.FullName -Force | ForEach-Object {
      $dst = Join-Path $WorkDir $_.Name
      if($_.PSIsContainer){
        if($_.Name -in @("bin","obj","logs")){ return }
        robocopy $_.FullName $dst /E /NFL /NDL /NJH /NJS /R:2 | Out-Null
      } else {
        Copy-Item -LiteralPath $_.FullName -Destination $dst -Force -ErrorAction SilentlyContinue
      }
    }
  }
}
if(-not (Test-Path -LiteralPath $CsprojAbs)){ F "Still missing $CsprojRel; WorkDir content broken" }
O "Project OK"

# ------------------------------------------------------------
# 3) .NET 10 SDK check + restore.
# ------------------------------------------------------------
if($NoBuild){ S "3/5 -NoBuild flag; stop."; O "Source at $WorkDir"; Read-Host "Enter to exit"; exit 0 }

S "3/5 .NET SDK check"
$net10OK = $false
try { &dotnet --list-sdks 2>$null | ForEach-Object { if($_ -match "^\s*10\."){ $net10OK = $true } } } catch {}
if(-not $net10OK){ F ".NET 10 SDK missing. Install: https://dotnet.microsoft.com/download/dotnet/10.0 (SDK x64)" }
O ".NET 10 SDK OK"

S "4/5 NuGet restore"
dotnet restore $CsprojAbs --verbosity minimal
if($LASTEXITCODE -ne 0){ F "dotnet restore failed (corporate proxy? set HTTP_PROXY/HTTPS_PROXY)" }
O "Restore OK"

S "4.2/5 Build $Config"
dotnet build $CsprojAbs -c $Config --no-restore --verbosity minimal
if($LASTEXITCODE -ne 0){
  Write-Host ""
  F "Build FAILED. Paste the RED error lines above back to the maintainer."
}
O "Build OK"

# ------------------------------------------------------------
# 5) Open solution in Visual Studio (if installed) + run app.
# ------------------------------------------------------------
S "5/5 Launch"
if(Test-Path -LiteralPath $SLN){
  try {
    Write-Host "  Opening Visual Studio: $SLN" -ForegroundColor DarkGray
    Start-Process -FilePath $SLN -ErrorAction SilentlyContinue
  } catch { W "VS not installed; skipping open-sln" }
}
Write-Host "  Logs: $(Join-Path $WorkDir logs)" -ForegroundColor DarkGray
Write-Host "  Close main window to exit cleanly." -ForegroundColor DarkGray
Write-Host ""
dotnet run --project $CsprojAbs -c $Config --no-build
Write-Host "`nDone  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
Read-Host "Press Enter to exit"
