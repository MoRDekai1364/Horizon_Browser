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

Write-Status "Starting installer..." 32

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
        $proc = Start-Process -FilePath $launchPath -PassThru
    }
}

$autoClickSeconds = 60
$elapsed = 0
$intervalMs = 1500
$autoClickActive = $true

Write-Status "Installing (auto)..." 40

while ($proc -and -not $proc.HasExited -and $elapsed -lt $autoClickSeconds) {
    Start-Sleep -Milliseconds $intervalMs
    $elapsed += ($intervalMs / 1000)

    try {
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne 0) {
            [Microsoft.VisualBasic.Interaction]::AppActivate($proc.Id)
            Start-Sleep -Milliseconds 200
            [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
        }
    } catch { }

    $pct = 40 + [math]::Min(40, [int](($elapsed / $autoClickSeconds) * 40))
    Write-Status "Installing (auto)..." $pct
}

if ($proc -and -not $proc.HasExited) {
    $autoClickActive = $false
    Write-Status "Waiting for installer (manual)..." 80
    $proc.WaitForExit()
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
