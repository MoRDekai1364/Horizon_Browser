$ErrorActionPreference = 'Continue'

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logPath = Join-Path $env:TEMP "horizon_phase0_$stamp.log"
$dataRoot = Join-Path $env:LOCALAPPDATA 'Horizon_Browser'
$userData = Join-Path $dataRoot 'HorizonData'
$cacheDir = Join-Path $userData 'FaviconCache'
$bookmarksFile = Join-Path $userData 'bookmarks.json'
$logsDir = Join-Path $dataRoot 'logs'
$referenceUrls = @(
    'https://www.google.com/s2/favicons?domain=horizon-reference-missing.invalid&sz=64',
    'https://icons.duckduckgo.com/ip3/horizon-reference-missing.invalid.ico'
)

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = '[{0}] [{1}] {2}' -f (Get-Date -Format 'HH:mm:ss.fff'), $Level, $Message
    Add-Content -Path $logPath -Value $line -Encoding UTF8
    switch ($Level) {
        'ERROR' { Write-Host $line -ForegroundColor Red }
        'WARN'  { Write-Host $line -ForegroundColor Yellow }
        default { Write-Host $line }
    }
}

function Show-Bar {
    param([int]$Done, [int]$Total, [string]$Label)
    if ($Total -le 0) { return }
    $percent = [int](($Done * 100) / $Total)
    $filled = [int]($percent / 5)
    $bar = ('#' * $filled) + ('.' * (20 - $filled))
    Write-Host ("`r[{0}] {1}% {2}" -f $bar, $percent, $Label) -NoNewline
    if ($Done -ge $Total) { Write-Host '' }
}

function Get-BytesHash {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') }
    finally { $sha.Dispose() }
}

Write-Log "Horizon Phase 0 diagnostics started"
Write-Log "Log path: $logPath"
Write-Log "Cache dir: $cacheDir"

$step = 0
$steps = 5

$step++
Show-Bar $step $steps 'cache inventory'
$pngFiles = @()
$noneMarkers = @()
if (Test-Path $cacheDir) {
    $pngFiles = @(Get-ChildItem -Path $cacheDir -Filter '*.png' -File -ErrorAction SilentlyContinue)
    $noneMarkers = @(Get-ChildItem -Path $cacheDir -Filter '*.none' -File -ErrorAction SilentlyContinue)
    $totalBytes = ($pngFiles | Measure-Object -Property Length -Sum).Sum
    Write-Log ("png files: {0}, none markers: {1}, total bytes: {2}" -f $pngFiles.Count, $noneMarkers.Count, $totalBytes)
} else {
    Write-Log 'FaviconCache folder does not exist' 'WARN'
}

$step++
Show-Bar $step $steps 'hashing cached icons'
$hashed = @()
$index = 0
foreach ($file in $pngFiles) {
    $index++
    try {
        $hashed += [pscustomobject]@{
            Name = $file.Name
            Length = $file.Length
            Hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash
        }
    } catch {
        Write-Log ("hash failed for {0}: {1}" -f $file.Name, $_.Exception.Message) 'ERROR'
    }
    if ($index % 25 -eq 0 -or $index -eq $pngFiles.Count) { Show-Bar $index $pngFiles.Count 'hashing' }
}
$groups = @($hashed | Group-Object Hash | Sort-Object Count -Descending)
Write-Log ("distinct hashes: {0}" -f $groups.Count)
foreach ($group in ($groups | Select-Object -First 5)) {
    Write-Log ("hash group count={0} bytes={1} example={2}" -f $group.Count, $group.Group[0].Length, $group.Group[0].Name)
}

$step++
Show-Bar $step $steps 'reference default icons'
$referenceHashes = @{}
foreach ($url in $referenceUrls) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 8 -Headers @{ 'User-Agent' = 'Mozilla/5.0' }
        $bytes = [byte[]]$response.Content
        $hash = Get-BytesHash $bytes
        $referenceHashes[$hash] = $url
        Write-Log ("reference status={0} bytes={1} hash={2} url={3}" -f $response.StatusCode, $bytes.Length, $hash, $url)
    } catch {
        $status = ''
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        Write-Log ("reference request failed status={0} url={1} error={2}" -f $status, $url, $_.Exception.Message) 'WARN'
    }
}
$matching = @($hashed | Where-Object { $referenceHashes.ContainsKey($_.Hash.ToUpper()) })
Write-Log ("cached files equal to a reference default icon: {0}" -f $matching.Count)
$topGroup = $groups | Select-Object -First 1
if ($topGroup -and $referenceHashes.Count -gt 0) {
    $topIsReference = $referenceHashes.ContainsKey($topGroup.Name.ToUpper())
    Write-Log ("largest hash group matches a reference default: {0}" -f $topIsReference)
}

$step++
Show-Bar $step $steps 'bookmarks and chain icon host'
$showme = @(Get-ChildItem -Path $cacheDir -Filter '*showmelinks*' -File -ErrorAction SilentlyContinue)
if ($showme.Count -eq 0) {
    Write-Log 'no cache file or marker contains showmelinks' 'WARN'
} else {
    foreach ($file in $showme) {
        $entry = $hashed | Where-Object { $_.Name -eq $file.Name }
        $groupSize = 0
        if ($entry) { $groupSize = ($groups | Where-Object { $_.Name -eq $entry.Hash }).Count }
        Write-Log ("showmelinks file: {0} bytes={1} sameHashGroupSize={2}" -f $file.Name, $file.Length, $groupSize)
    }
}
if (Test-Path $bookmarksFile) {
    try {
        $bookmarks = Get-Content -Path $bookmarksFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $withIconPath = @($bookmarks | Where-Object { $_.IconPath -and (Test-Path $_.IconPath) })
        Write-Log ("bookmarks: {0}, with an existing IconPath file: {1}" -f @($bookmarks).Count, $withIconPath.Count)
        $hostCounts = @($bookmarks | ForEach-Object {
            try { ([System.Uri]$_.Url).Host } catch { '' }
        } | Where-Object { $_ } | Group-Object | Sort-Object Count -Descending | Select-Object -First 8)
        foreach ($entry in $hostCounts) { Write-Log ("host {0} bookmarks={1}" -f $entry.Name, $entry.Count) }
    } catch {
        Write-Log ("bookmarks.json read failed: {0}" -f $_.Exception.Message) 'ERROR'
    }
} else {
    Write-Log "bookmarks.json not found at $bookmarksFile" 'WARN'
}

$step++
Show-Bar $step $steps 'crash tapes'
$tapeDirs = @($logsDir, (Join-Path $PSScriptRoot 'logs')) | Where-Object { $_ -and (Test-Path $_) }
foreach ($dir in $tapeDirs) {
    $tapes = @(Get-ChildItem -Path $dir -Filter 'crash_tape*.err' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 3)
    foreach ($tape in $tapes) {
        $message = (Select-String -Path $tape.FullName -Pattern '^Message:' -ErrorAction SilentlyContinue | Select-Object -First 1).Line
        Write-Log ("crash tape {0} written {1} {2}" -f $tape.Name, $tape.LastWriteTime, $message)
    }
    if ($tapes.Count -eq 0) { Write-Log "no crash tapes in $dir" }
}

Write-Log 'Phase 0 diagnostics finished'
Write-Host ''
Write-Host "Report saved to: $logPath"
$answer = Read-Host 'Open the report now? (Y/N)'
if ($answer -match '^[Yy]') { Start-Process notepad.exe -ArgumentList $logPath }
