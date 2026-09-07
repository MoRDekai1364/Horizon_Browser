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

$proc = Start-Process -FilePath $InstallerPath -PassThru

$autoClickSeconds = 60
$elapsed = 0
$intervalMs = 1500
$autoClickActive = $true

Write-Status "Installing (auto)..." 40

while (-not $proc.HasExited -and $elapsed -lt $autoClickSeconds) {
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

if (-not $proc.HasExited) {
    $autoClickActive = $false
    Write-Status "Waiting for installer (manual)..." 80
    $proc.WaitForExit()
}

Write-Status "Installer finished" 88
exit 0
