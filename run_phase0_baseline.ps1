param(
    [int]$Launches = 5,
    [int]$DwellSeconds = 360,
    [int]$Repeats = 10
)

$exe = Join-Path $PSScriptRoot "bin\Release\Horizon.Browser.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Horizon.Browser.exe not found at $exe"
    exit 1
}

$diagDir = Join-Path $env:LOCALAPPDATA "Horizon_Browser\Diagnostics"
$launchesCsv = Join-Path $diagDir "phase0_baseline_launches.csv"
if (Test-Path $launchesCsv) {
    Remove-Item -Force $launchesCsv
}

for ($i = 1; $i -le $Launches; $i++) {
    Write-Host "Starting Cold Launch $i of $Launches (Dwell: ${DwellSeconds}s, Repeats: $Repeats)..."
    $proc = Start-Process -FilePath $exe -ArgumentList "-debug", "--phase0-benchmark", "--launch-id=$i", "--dwell-seconds=$DwellSeconds", "--repeats=$Repeats", "--auto-exit-after-benchmark" -PassThru
    $proc.WaitForExit()
    Write-Host "Cold Launch $i exited with code $($proc.ExitCode)."
    Start-Sleep -Seconds 3
}

Write-Host "All $Launches launches finished."
