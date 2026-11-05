param(
    [Parameter(Mandatory = $true)][string]$Label,
    [string]$ProcessName = 'Horizon.Browser',
    [int]$Samples = 3,
    [int]$IntervalSeconds = 5,
    [string]$OutputDir = (Join-Path $env:LOCALAPPDATA 'Horizon_Browser\Diagnostics'),
    [string]$SourceDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$tempLog = Join-Path $env:TEMP ('horizon_sampler_' + $stamp + '.log')
$csvPath = Join-Path $OutputDir 'p0_memory_samples.csv'
$exitCode = 0

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    Add-Content -Path $tempLog -Value $line
    if ($Level -eq 'ERROR') { Write-Host $line -ForegroundColor Red }
    elseif ($Level -eq 'WARN') { Write-Host $line -ForegroundColor Yellow }
    else { Write-Host $line }
}

function Get-DescendantIds {
    param([int]$RootId, $ByParent)
    $result = New-Object System.Collections.Generic.List[int]
    $queue = New-Object System.Collections.Generic.Queue[int]
    $queue.Enqueue($RootId)
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if ($ByParent.ContainsKey($current)) {
            foreach ($child in $ByParent[$current]) {
                $result.Add($child)
                $queue.Enqueue($child)
            }
        }
    }
    return $result.ToArray()
}

function Get-SumMb {
    param($Ids, $Perf, [string]$Field)
    $sum = 0.0
    foreach ($id in $Ids) {
        if ($Perf.ContainsKey([int]$id)) { $sum += [double]$Perf[[int]$id].$Field }
    }
    return [math]::Round($sum / 1MB, 1)
}

try {
    Add-Type -AssemblyName System.Windows.Forms
    if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
    Write-Log ('Label {0}, {1} samples, {2}s apart' -f $Label, $Samples, $IntervalSeconds)

    $power = [System.Windows.Forms.SystemInformation]::PowerStatus
    $lineStatus = $power.PowerLineStatus.ToString()
    $hasBattery = -not ($power.BatteryChargeStatus.ToString() -match 'NoSystemBattery')
    $batteryPct = -1
    if ($hasBattery) { $batteryPct = [int]([math]::Round($power.BatteryLifePercent * 100)) }
    $monitorCount = [System.Windows.Forms.Screen]::AllScreens.Length
    Write-Log ('Power line: {0}, battery: {1}, monitors: {2}' -f $lineStatus, $batteryPct, $monitorCount)

    $rows = @()
    for ($i = 1; $i -le $Samples; $i++) {
        Write-Progress -Activity ('Sampling ' + $Label) -Status ('Sample {0} of {1}' -f $i, $Samples) -PercentComplete ((($i - 1) / $Samples) * 100)

        $found = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
        if ($found.Count -eq 0) { throw ('Process ' + $ProcessName + ' is not running.') }
        if ($found.Count -gt 1) { Write-Log ('Found {0} processes named {1}; using the oldest.' -f $found.Count, $ProcessName) 'WARN' }
        $mainProc = $found | Sort-Object StartTime | Select-Object -First 1
        $mainId = [int]$mainProc.Id

        $byParent = @{}
        foreach ($p in (Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId)) {
            $parentId = [int]$p.ParentProcessId
            if (-not $byParent.ContainsKey($parentId)) {
                $byParent[$parentId] = New-Object System.Collections.Generic.List[int]
            }
            $byParent[$parentId].Add([int]$p.ProcessId)
        }

        $perf = @{}
        foreach ($r in (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Select-Object IDProcess, WorkingSetPrivate, PrivateBytes)) {
            $perf[[int]$r.IDProcess] = $r
        }
        if (-not $perf.ContainsKey($mainId)) { throw ('No performance counters returned for PID ' + $mainId) }

        $direct = @()
        if ($byParent.ContainsKey($mainId)) { $direct = @($byParent[$mainId]) }
        $all = @(Get-DescendantIds -RootId $mainId -ByParent $byParent)

        $hostWs = Get-SumMb -Ids @($mainId) -Perf $perf -Field 'WorkingSetPrivate'
        $hostCommit = Get-SumMb -Ids @($mainId) -Perf $perf -Field 'PrivateBytes'
        $directWs = Get-SumMb -Ids $direct -Perf $perf -Field 'WorkingSetPrivate'
        $allWs = Get-SumMb -Ids $all -Perf $perf -Field 'WorkingSetPrivate'

        $row = [pscustomobject]@{
            Timestamp           = (Get-Date).ToString('s')
            Label               = $Label
            Sample              = $i
            PowerLine           = $lineStatus
            BatteryPct          = $batteryPct
            Monitors            = $monitorCount
            HostPid             = $mainId
            HostPrivateWS_MB    = $hostWs
            HostCommit_MB       = $hostCommit
            DirectChildren      = $direct.Count
            DirectChildrenWS_MB = $directWs
            Descendants         = $all.Count
            DescendantsWS_MB    = $allWs
            TotalPrivateWS_MB   = [math]::Round($hostWs + $allWs, 1)
        }
        $rows += $row
        $row | Export-Csv -Path $csvPath -Append -NoTypeInformation -Encoding UTF8
        Write-Log ('Sample {0}: host {1} MB, direct children {2} ({3} MB), all descendants {4} ({5} MB)' -f $i, $hostWs, $direct.Count, $directWs, $all.Count, $allWs)

        if ($i -lt $Samples) { Start-Sleep -Seconds $IntervalSeconds }
    }
    Write-Progress -Activity ('Sampling ' + $Label) -Completed

    $rows | Format-Table Sample, HostPrivateWS_MB, HostCommit_MB, DirectChildren, DirectChildrenWS_MB, Descendants, DescendantsWS_MB, TotalPrivateWS_MB -AutoSize
    $avgHost = [math]::Round(($rows | Measure-Object HostPrivateWS_MB -Average).Average, 1)
    $avgAll = [math]::Round(($rows | Measure-Object DescendantsWS_MB -Average).Average, 1)
    Write-Log ('Average for {0}: host {1} MB, all descendants {2} MB' -f $Label, $avgHost, $avgAll)
    Write-Log ('Rows appended to ' + $csvPath)
}
catch {
    Write-Log $_.Exception.Message 'ERROR'
    Write-Log $_.ScriptStackTrace 'ERROR'
    $exitCode = 1
}
finally {
    try {
        $logDir = Join-Path $SourceDir 'logs'
        if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
        Copy-Item -Path $tempLog -Destination $logDir -Force
        Write-Host ('Log copied to ' + $logDir)
    }
    catch {
        Write-Host ('Could not copy the log to ' + $SourceDir + '. It stays at ' + $tempLog) -ForegroundColor Yellow
    }
}
exit $exitCode