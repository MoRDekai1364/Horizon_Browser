param(
    [Parameter(Mandatory=$true)][string]$InstallerPath,
    [Parameter(Mandatory=$true)][string]$TargetDir,
    [Parameter(Mandatory=$true)][string]$StatusFile
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.VisualBasic

function Write-Status($step, $percent) {
    Set-Content -Path $StatusFile -Value @("STEP:$step", "PERCENT:$percent")
}

Write-Status "Closing running Horizon instances..." 31
Get-Process -Name "Horizon.Browser" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 300 } catch { }
    try { if (-not $_.HasExited) { $_.Kill() } } catch { }
}
Start-Sleep -Milliseconds 500

Write-Status "Starting installer..." 33

if (-not (Test-Path $InstallerPath)) {
    Write-Status "ERROR: Installer not found at $InstallerPath" 30
    exit 1
}

$LogFile = Join-Path $env:TEMP "horizon_update_install.log"

function Write-Log($msg) {
    try { Add-Content -Path $LogFile -Value ("[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg) } catch { }
}

function Expand-Package($archive, $dest) {
    $archiveExt = [IO.Path]::GetExtension($archive).ToLowerInvariant()
    if ($archiveExt -eq ".zip") {
        Expand-Archive -LiteralPath $archive -DestinationPath $dest -Force
        return
    }
    $tar = Join-Path $env:SystemRoot "System32\tar.exe"
    if (-not (Test-Path $tar)) { throw "No extractor available for $archiveExt" }
    & $tar -xf $archive -C $dest
    if ($LASTEXITCODE -ne 0) { throw "tar exited with code $LASTEXITCODE" }
}

Write-Log "InstallerPath=$InstallerPath TargetDir=$TargetDir"

$archiveExts = @(".zip", ".7z", ".rar", ".tar", ".gz", ".tgz")
$launchPath = $InstallerPath
$launchExt = [IO.Path]::GetExtension($InstallerPath).ToLowerInvariant()
$stageDir = $null
$portableDone = $false
$proc = $null

if ($archiveExts -contains $launchExt) {
    $stageDir = Join-Path $env:TEMP ("horizon_update_" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
    Write-Status "Extracting update..." 34
    try {
        Expand-Package $InstallerPath $stageDir
    } catch {
        Write-Log "Extract failed: $($_.Exception.Message)"
        Write-Status "ERROR: Extract failed - $($_.Exception.Message)" 30
        exit 1
    }

    $found = Get-ChildItem -LiteralPath $stageDir -Recurse -File |
        Where-Object { $_.Extension -in ".exe", ".msi" -and $_.Name -match "setup|install" -and $_.Name -notmatch "unins" } |
        Sort-Object { $_.Extension -ne ".exe" }, FullName |
        Select-Object -First 1

    if ($found) {
        $launchPath = $found.FullName
        $launchExt = $found.Extension.ToLowerInvariant()
        Write-Log "Installer found in archive: $launchPath"
    } else {
        $mainExe = Get-ChildItem -LiteralPath $stageDir -Recurse -File -Filter "Horizon.Browser.exe" | Select-Object -First 1
        if (-not $mainExe) {
            Write-Log "Archive contains no installer and no Horizon.Browser.exe"
            Write-Status "ERROR: Archive has no installer or app files" 30
            exit 1
        }
        Write-Status "Copying files..." 60
        & robocopy $mainExe.DirectoryName $TargetDir /E /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
        Write-Log "Portable copy from $($mainExe.DirectoryName) to $TargetDir, robocopy exit $LASTEXITCODE"
        if ($LASTEXITCODE -ge 8) {
            Write-Status "ERROR: File copy failed (robocopy $LASTEXITCODE)" 60
            exit 1
        }
        $portableDone = $true
    }
}

if (-not $portableDone) {
    Write-Log "Launching $launchPath"
    if ($launchExt -eq ".msi") {
        $proc = Start-Process -FilePath "msiexec.exe" -ArgumentList @("/i", "`"$launchPath`"", "/passive", "/norestart") -PassThru
    } else {
        # InstallForge supports -silent and -path "<TargetDir>"
        $argList = @("-silent", "-path", "`"$TargetDir`"")
        Write-Log "Starting process $launchPath with args: $($argList -join ' ')"
        $proc = Start-Process -FilePath $launchPath -ArgumentList $argList -PassThru
    }
}

Write-Status "Installing update..." 45

$timeoutSeconds = 300
$elapsed = 0
$intervalMs = 1000

while ($proc -and -not $proc.HasExited) {
    Start-Sleep -Milliseconds $intervalMs
    $elapsed += ($intervalMs / 1000)

    $pct = 45 + [math]::Min(40, [int](($elapsed / 30.0) * 40))
    Write-Status "Installing update..." $pct

    if ($elapsed -ge $timeoutSeconds) {
        Write-Log "Installer timed out after $timeoutSeconds seconds"
        break
    }
}

if ($proc) {
    try { Write-Log "Installer process exited with code $($proc.ExitCode)" } catch { }
}

if ($stageDir -and (Test-Path $stageDir)) {
    try { Remove-Item -LiteralPath $stageDir -Recurse -Force } catch { Write-Log "Stage cleanup failed: $($_.Exception.Message)" }
}
try {
    $logDir = Join-Path $TargetDir "logs"
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    Copy-Item -LiteralPath $LogFile -Destination $logDir -Force
} catch { }
Write-Status "Installer finished" 88
exit 0
