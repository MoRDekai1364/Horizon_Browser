param(
    [string]$Phase = "1"
)

$ConfigDir = Join-Path $env:USERPROFILE ".horizon_deploy"
$ConfigPath = Join-Path $ConfigDir "config.json"
$LogDir = Join-Path $env:TEMP "horizon_deploy_logs"
$LogFile = Join-Path $LogDir ("deploy_{0}.log" -f (Get-Date -Format "yyyyMMdd_HHmmss"))

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    if (-not (Test-Path $LogDir)) {
        New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
    }
    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Level, $Message
    Add-Content -Path $LogFile -Value $line
}

function Write-Progress-Bar {
    param([int]$Percent, [string]$Label = "")
    $barLength = 20
    $filled = [math]::Floor($barLength * $Percent / 100)
    $bar = ("#" * $filled) + ("." * ($barLength - $filled))
    Write-Host ("[{0}] {1}% {2}" -f $bar, $Percent, $Label)
}

function Initialize-Config {
    if (-not (Test-Path $ConfigDir)) {
        New-Item -ItemType Directory -Path $ConfigDir -Force | Out-Null
        Write-Log "Created config directory: $ConfigDir"
    }
    if (-not (Test-Path $ConfigPath)) {
        $default = [ordered]@{
            repos = @()
            defaultRepo = ""
            githubToken = ""
            folderBranchMap = [ordered]@{}
            lastUsed = [ordered]@{}
        }
        $default | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigPath -Encoding UTF8
        Write-Log "Created default config: $ConfigPath"
    }
}

function Load-Config {
    try {
        $json = Get-Content -Path $ConfigPath -Raw -Encoding UTF8
        return $json | ConvertFrom-Json
    } catch {
        Write-Log "Failed to load config: $_" "ERROR"
        Write-Host "[ERROR] Config load failed. See log: $LogFile"
        exit 1
    }
}

function Save-Config {
    param($ConfigObject)
    try {
        $ConfigObject | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigPath -Encoding UTF8
        Write-Log "Config saved."
    } catch {
        Write-Log "Failed to save config: $_" "ERROR"
        Write-Host "[ERROR] Config save failed. See log: $LogFile"
        exit 1
    }
}

function Select-Repo {
    param($Config)

    $repos = @($Config.repos)

    if ($repos.Count -eq 0) {
        Write-Host "No saved repos found."
        $newPath = Select-FolderDialog -Description "Select local repo folder"
        $newUrl = Read-Host "Enter GitHub repo URL (e.g. https://github.com/user/repo.git)"
        $newName = Read-Host "Enter a short name for this repo"

        $repoEntry = [ordered]@{
            name = $newName
            path = $newPath
            url = $newUrl
        }
        $Config.repos = @($repoEntry)
        $Config.defaultRepo = $newName
        Save-Config $Config
        Write-Log "Added first repo: $newName ($newPath)"
        return $repoEntry
    }

    Write-Host "Saved repos:"
    for ($i = 0; $i -lt $repos.Count; $i++) {
        $marker = if ($repos[$i].name -eq $Config.defaultRepo) { " (default)" } else { "" }
        Write-Host ("{0}: {1}{2}" -f ($i + 1), $repos[$i].name, $marker)
    }
    Write-Host "N: Add new repo"

    $choice = Read-Host ("Select repo index (or Enter for default '{0}')" -f $Config.defaultRepo)

    if ([string]::IsNullOrWhiteSpace($choice)) {
        $selected = $repos | Where-Object { $_.name -eq $Config.defaultRepo } | Select-Object -First 1
        if (-not $selected) {
            Write-Log "Default repo '$($Config.defaultRepo)' not found in list." "ERROR"
            Write-Host "[ERROR] Default repo not found. See log: $LogFile"
            exit 1
        }
        Write-Log "Selected default repo: $($selected.name)"
        return $selected
    }

    if ($choice -eq "N" -or $choice -eq "n") {
        $newPath = Select-FolderDialog -Description "Select local repo folder"
        $newUrl = Read-Host "Enter GitHub repo URL"
        $newName = Read-Host "Enter a short name for this repo"
        $repoEntry = [ordered]@{
            name = $newName
            path = $newPath
            url = $newUrl
        }
        $Config.repos = @($Config.repos) + $repoEntry
        $setDefault = Read-Host "Set as default repo? (y/N)"
        if ($setDefault -eq "y" -or $setDefault -eq "Y") {
            $Config.defaultRepo = $newName
        }
        Save-Config $Config
        Write-Log "Added new repo: $newName ($newPath)"
        return $repoEntry
    }

    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $repos.Count) {
        Write-Log "Invalid repo selection: $choice" "ERROR"
        Write-Host "[ERROR] Invalid selection."
        exit 1
    }
    Write-Log "Selected repo: $($repos[$index].name)"
    return $repos[$index]
}

function Ensure-Token {
    param($Config)

    if ([string]::IsNullOrWhiteSpace($Config.githubToken)) {
        Write-Host "No GitHub token found in config."
        $token = Read-Host "Paste GitHub Personal Access Token (repo scope)"
        $Config.githubToken = $token
        Save-Config $Config
        Write-Log "GitHub token saved to config."
    }
    return $Config.githubToken
}

function Parse-RepoUrl {
    param([string]$Url)

    $clean = $Url -replace '\.git$', ''
    if ($clean -match 'github\.com[:/]+([^/]+)/([^/]+)$') {
        return [ordered]@{
            owner = $Matches[1]
            repo  = $Matches[2]
        }
    }
    Write-Log "Failed to parse GitHub URL: $Url" "ERROR"
    Write-Host "[ERROR] Could not parse owner/repo from URL: $Url"
    exit 1
}

function Get-GitHubBranches {
    param([string]$Owner, [string]$Repo, [string]$Token)

    $uri = "https://api.github.com/repos/$Owner/$Repo/branches"
    $headers = @{
        Authorization = "token $Token"
        "User-Agent" = "horizon-deploy-script"
    }

    try {
        $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        Write-Log "Fetched $($response.Count) branches from $Owner/$Repo"
        return $response
    } catch {
        Write-Log "Failed to fetch branches: $_" "ERROR"
        Write-Host "[ERROR] GitHub branch fetch failed. See log: $LogFile"
        exit 1
    }
}

function Get-GitHubTree {
    param([string]$Owner, [string]$Repo, [string]$Branch, [string]$Token)

    $uri = "https://api.github.com/repos/$Owner/$Repo/git/trees/$Branch`?recursive=1"
    $headers = @{
        Authorization = "token $Token"
        "User-Agent" = "horizon-deploy-script"
    }

    try {
        $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        Write-Log "Fetched tree for branch '$Branch' ($($response.tree.Count) entries)"
        return $response.tree
    } catch {
        Write-Log "Failed to fetch tree for branch '$Branch': $_" "ERROR"
        Write-Host "[ERROR] GitHub tree fetch failed. See log: $LogFile"
        exit 1
    }
}

function Get-LocalFileList {
    param([string]$LocalPath)

    if (-not (Test-Path $LocalPath)) {
        Write-Log "Local path not found: $LocalPath" "ERROR"
        Write-Host "[ERROR] Local repo path not found: $LocalPath"
        exit 1
    }

    $files = Get-ChildItem -Path $LocalPath -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch '\\\.git\\' } |
        ForEach-Object { $_.FullName.Substring($LocalPath.Length).TrimStart('\') -replace '\\', '/' }

    return $files
}

function Get-BranchForFolder {
    param($Config, $RepoName, [string]$LocalPath, $Branches)

    $folderName = Split-Path -Path $LocalPath -Leaf
    $branchNames = @($Branches | ForEach-Object { $_.name })

    $mapKey = "$RepoName|$folderName"
    if ($Config.folderBranchMap.PSObject.Properties.Name -contains $mapKey) {
        $saved = $Config.folderBranchMap.$mapKey
        Write-Log "Using saved folder->branch mapping: $folderName -> $saved"
        return $saved
    }

    $autoMatch = $branchNames | Where-Object { $_ -eq $folderName } | Select-Object -First 1
    if ($autoMatch) {
        Write-Host "Auto-matched local folder '$folderName' to remote branch '$autoMatch'."
        $confirm = Read-Host "Use this match? (Y/n)"
        if ($confirm -eq "" -or $confirm -eq "y" -or $confirm -eq "Y") {
            $Config.folderBranchMap | Add-Member -NotePropertyName $mapKey -NotePropertyValue $autoMatch -Force
            Save-Config $Config
            Write-Log "Saved folder->branch mapping: $folderName -> $autoMatch"
            return $autoMatch
        }
    }

    Write-Host "No confirmed match for folder '$folderName'."
    Write-Host "Available branches:"
    for ($i = 0; $i -lt $branchNames.Count; $i++) {
        Write-Host ("{0}: {1}" -f ($i + 1), $branchNames[$i])
    }
    $choice = Read-Host "Select branch index for this folder"
    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $branchNames.Count) {
        Write-Log "Invalid branch selection: $choice" "ERROR"
        Write-Host "[ERROR] Invalid selection."
        exit 1
    }
    $selectedBranch = $branchNames[$index]
    $Config.folderBranchMap | Add-Member -NotePropertyName $mapKey -NotePropertyValue $selectedBranch -Force
    Save-Config $Config
    Write-Log "Saved folder->branch mapping: $folderName -> $selectedBranch"
    return $selectedBranch
}

function Compare-RemoteLocalTree {
    param($RemoteTree, $LocalFiles)

    $remoteFiles = @($RemoteTree | Where-Object { $_.type -eq "blob" } | ForEach-Object { $_.path })

    $onlyRemote = $remoteFiles | Where-Object { $_ -notin $LocalFiles }
    $onlyLocal = $LocalFiles | Where-Object { $_ -notin $remoteFiles }

    $possibleRenames = @()
    foreach ($r in $onlyRemote) {
        $rName = Split-Path -Path $r -Leaf
        $match = $onlyLocal | Where-Object { (Split-Path -Path $_ -Leaf) -eq $rName }
        foreach ($m in $match) {
            $possibleRenames += [ordered]@{ from = $r; to = $m }
        }
    }

    return [ordered]@{
        onlyRemote = $onlyRemote
        onlyLocal = $onlyLocal
        possibleRenames = $possibleRenames
    }
}

function Confirm-RenameDiff {
    param($DiffResult)

    if ($DiffResult.possibleRenames.Count -eq 0) {
        Write-Log "No rename/move candidates detected."
        return $true
    }

    Write-Host ""
    Write-Host "Detected possible renamed/moved files:"
    foreach ($r in $DiffResult.possibleRenames) {
        Write-Host (" - {0}  ->  {1}" -f $r.from, $r.to)
    }
    Write-Log "Detected $($DiffResult.possibleRenames.Count) possible rename(s)."

    $confirm = Read-Host "Continue with these changes? (y/N)"
    if ($confirm -eq "y" -or $confirm -eq "Y") {
        Write-Log "User confirmed rename/move diff."
        return $true
    }

    Write-Log "User aborted due to rename/move diff."
    Write-Host "[ABORTED] User declined to proceed with detected renames/moves."
    return $false
}

function Show-ManageMenu {
    param($Config)

    while ($true) {
        Write-Host ""
        Write-Host "=== Manage Settings ==="
        Write-Host "1: List/edit saved repos"
        Write-Host "2: Set default repo"
        Write-Host "3: Update GitHub token"
        Write-Host "4: View/delete folder-branch mappings"
        Write-Host "5: View last-used selections"
        Write-Host "B: Back / run deploy"
        $choice = Read-Host "Select option"

        switch ($choice) {
            "1" { Show-ReposMenu -Config $Config }
            "2" { Set-DefaultRepo -Config $Config }
            "3" {
                $newToken = Read-Host "Enter new GitHub PAT"
                $Config.githubToken = $newToken
                Save-Config $Config
                Write-Log "GitHub token updated via manage menu."
                Write-Host "Token updated."
            }
            "4" { Show-MappingsMenu -Config $Config }
            "5" { Show-LastUsed -Config $Config }
            "B" { return }
            "b" { return }
            default { Write-Host "Invalid option." }
        }
    }
}

function Show-ReposMenu {
    param($Config)

    $repos = @($Config.repos)
    if ($repos.Count -eq 0) {
        Write-Host "No saved repos."
        return
    }

    Write-Host ""
    for ($i = 0; $i -lt $repos.Count; $i++) {
        $marker = if ($repos[$i].name -eq $Config.defaultRepo) { " (default)" } else { "" }
        Write-Host ("{0}: {1}{2}" -f ($i + 1), $repos[$i].name, $marker)
        Write-Host ("     Path: {0}" -f $repos[$i].path)
        Write-Host ("     URL:  {0}" -f $repos[$i].url)
    }

    Write-Host "Enter index to delete, or B to go back"
    $choice = Read-Host "Selection"
    if ($choice -eq "B" -or $choice -eq "b") { return }

    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $repos.Count) {
        Write-Host "[ERROR] Invalid selection."
        return
    }

    $removedName = $repos[$index].name
    $confirm = Read-Host ("Delete repo '{0}'? (y/N)" -f $removedName)
    if ($confirm -eq "y" -or $confirm -eq "Y") {
        $Config.repos = @($repos | Where-Object { $_.name -ne $removedName })
        if ($Config.defaultRepo -eq $removedName) {
            $Config.defaultRepo = ""
        }
        Save-Config $Config
        Write-Log "Deleted repo entry: $removedName"
        Write-Host "Deleted."
    }
}

function Set-DefaultRepo {
    param($Config)

    $repos = @($Config.repos)
    if ($repos.Count -eq 0) {
        Write-Host "No saved repos."
        return
    }

    Write-Host ""
    for ($i = 0; $i -lt $repos.Count; $i++) {
        Write-Host ("{0}: {1}" -f ($i + 1), $repos[$i].name)
    }
    $choice = Read-Host "Select new default repo index"
    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $repos.Count) {
        Write-Host "[ERROR] Invalid selection."
        return
    }

    $Config.defaultRepo = $repos[$index].name
    Save-Config $Config
    Write-Log "Default repo set to: $($repos[$index].name)"
    Write-Host "Default repo set to $($repos[$index].name)."
}

function Show-MappingsMenu {
    param($Config)

    $mapProps = @($Config.folderBranchMap.PSObject.Properties)
    if ($mapProps.Count -eq 0) {
        Write-Host "No saved folder-branch mappings."
        return
    }

    Write-Host ""
    for ($i = 0; $i -lt $mapProps.Count; $i++) {
        Write-Host ("{0}: {1}  ->  {2}" -f ($i + 1), $mapProps[$i].Name, $mapProps[$i].Value)
    }
    Write-Host "Enter index to delete, or B to go back"
    $choice = Read-Host "Selection"
    if ($choice -eq "B" -or $choice -eq "b") { return }

    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $mapProps.Count) {
        Write-Host "[ERROR] Invalid selection."
        return
    }

    $keyToRemove = $mapProps[$index].Name
    $Config.folderBranchMap.PSObject.Properties.Remove($keyToRemove)
    Save-Config $Config
    Write-Log "Deleted folder-branch mapping: $keyToRemove"
    Write-Host "Deleted."
}

function Show-LastUsed {
    param($Config)

    $lastProps = @($Config.lastUsed.PSObject.Properties)
    if ($lastProps.Count -eq 0) {
        Write-Host "No last-used data recorded."
        return
    }

    Write-Host ""
    foreach ($p in $lastProps) {
        Write-Host ("{0}: {1}" -f $p.Name, $p.Value)
    }
}

function Select-FolderDialog {
    param([string]$Description = "Select local repo folder")

    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = $Description
    $dialog.ShowNewFolderButton = $false

    $result = $dialog.ShowDialog()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
        return $dialog.SelectedPath
    }

    Write-Log "User cancelled folder selection dialog." "ERROR"
    Write-Host "[ABORTED] No folder selected."
    exit 1
}

function Get-CurrentLocalBranch {
    param([string]$LocalPath)

    Push-Location $LocalPath
    try {
        $branch = git rev-parse --abbrev-ref HEAD 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) {
            Write-Log "Failed to determine current local branch in $LocalPath" "ERROR"
            Write-Host "[ERROR] Could not determine current git branch. Is this a git repo?"
            exit 1
        }
        return $branch.Trim()
    } finally {
        Pop-Location
    }
}

function Invoke-CommitAndPush {
    param([string]$LocalPath, [string]$TargetBranch, [string]$CommitMessage)

    Push-Location $LocalPath
    try {
        $currentBranch = git rev-parse --abbrev-ref HEAD 2>$null
        if ($currentBranch.Trim() -ne $TargetBranch) {
            Write-Log "Branch mismatch: local='$($currentBranch.Trim())' target='$TargetBranch'" "ERROR"
            Write-Host "[ERROR] Local branch '$($currentBranch.Trim())' does not match target branch '$TargetBranch'."
            Write-Host "[ABORTED] Push cancelled to prevent creating/overwriting wrong remote branch."
            exit 1
        }
        Write-Log "Branch verification passed: $TargetBranch"

        git add .
        git commit -m "$CommitMessage" | Out-Null
        Write-Log "Committed with message: $CommitMessage"

        git push origin $TargetBranch
        if ($LASTEXITCODE -ne 0) {
            Write-Log "Push failed for branch $TargetBranch" "ERROR"
            Write-Host "[ERROR] Push failed. See log: $LogFile"
            exit 1
        }

        Write-Log "Push succeeded to branch $TargetBranch"
        Write-Host "[SUCCESS] Pushed to $TargetBranch."
    } finally {
        Pop-Location
    }
}

function Copy-LogToSource {
    param([string]$LocalPath)

    try {
        $destDir = Join-Path $LocalPath "logs"
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item -Path $LogFile -Destination $destDir -Force
        Write-Log "Log copied to $destDir"
    } catch {
        Write-Log "Failed to copy log to source dir: $_" "ERROR"
        Write-Host "[WARNING] Could not copy log to $LocalPath\logs. Log remains at $LogFile"
    }
}

Write-Log "=== Deploy script started (Phase $Phase) ==="
Write-Progress-Bar -Percent 0 -Label "Initializing config"

Initialize-Config
$Config = Load-Config

Write-Host ""
Write-Host "1: Run deploy"
Write-Host "M: Manage settings"
$topChoice = Read-Host ("Selection (Enter for deploy)")
if ($topChoice -eq "M" -or $topChoice -eq "m") {
    Show-ManageMenu -Config $Config
    $Config = Load-Config
}

Write-Progress-Bar -Percent 20 -Label "Selecting repo"
$SelectedRepo = Select-Repo -Config $Config

Write-Progress-Bar -Percent 40 -Label "Checking GitHub token"
$Token = Ensure-Token -Config $Config

Write-Host ""
Write-Host "Repo selected: $($SelectedRepo.name)"
Write-Host "Local path: $($SelectedRepo.path)"
Write-Host "Remote URL: $($SelectedRepo.url)"
Write-Host "Log file: $LogFile"

Write-Progress-Bar -Percent 60 -Label "Phase 1 complete"
Write-Log "=== Phase 1 complete ==="
Copy-LogToSource -LocalPath $SelectedRepo.path

if ([int]$Phase -ge 2) {
    Write-Progress-Bar -Percent 65 -Label "Parsing repo URL"
    $RepoInfo = Parse-RepoUrl -Url $SelectedRepo.url

    Write-Progress-Bar -Percent 75 -Label "Fetching branches from GitHub"
    $Branches = Get-GitHubBranches -Owner $RepoInfo.owner -Repo $RepoInfo.repo -Token $Token

    Write-Host ""
    Write-Host "Remote branches:"
    foreach ($b in $Branches) {
        Write-Host " - $($b.name)"
    }

    Write-Progress-Bar -Percent 85 -Label "Fetching local file list"
    $LocalFiles = Get-LocalFileList -LocalPath $SelectedRepo.path
    Write-Log "Local file count: $($LocalFiles.Count)"

    Write-Progress-Bar -Percent 100 -Label "Phase 2 complete"
    Write-Log "=== Phase 2 complete ==="
}

if ([int]$Phase -ge 3) {
    Write-Progress-Bar -Percent 0 -Label "Matching folder to branch"
    $TargetBranch = Get-BranchForFolder -Config $Config -RepoName $SelectedRepo.name -LocalPath $SelectedRepo.path -Branches $Branches

    Write-Host ""
    Write-Host "Target branch: $TargetBranch"

    Write-Progress-Bar -Percent 40 -Label "Fetching remote tree for target branch"
    $RemoteTree = Get-GitHubTree -Owner $RepoInfo.owner -Repo $RepoInfo.repo -Branch $TargetBranch -Token $Token

    Write-Progress-Bar -Percent 70 -Label "Comparing local vs remote files"
    $DiffResult = Compare-RemoteLocalTree -RemoteTree $RemoteTree -LocalFiles $LocalFiles

    Write-Progress-Bar -Percent 90 -Label "Checking for renames/moves"
    $proceed = Confirm-RenameDiff -DiffResult $DiffResult

    if (-not $proceed) {
        Write-Log "=== Script halted at Phase 3 (user declined) ==="
        exit 1
    }

    Write-Progress-Bar -Percent 100 -Label "Phase 3 complete"
    Write-Log "=== Phase 3 complete ==="
}

if ([int]$Phase -ge 4) {
    Write-Progress-Bar -Percent 0 -Label "Verifying local branch"
    $CurrentBranch = Get-CurrentLocalBranch -LocalPath $SelectedRepo.path

    if ($CurrentBranch -ne $TargetBranch) {
        Write-Log "Pre-check branch mismatch: local='$CurrentBranch' target='$TargetBranch'" "ERROR"
        Write-Host "[ERROR] Local repo is on branch '$CurrentBranch' but target is '$TargetBranch'."
        Write-Host "[ABORTED] Checkout the correct branch or fix the folder mapping before running again."
        exit 1
    }
    Write-Log "Pre-check passed: local branch matches target ($TargetBranch)"

    Write-Progress-Bar -Percent 30 -Label "Awaiting commit message"
    $CommitMessage = Read-Host "Enter commit message"

    Write-Progress-Bar -Percent 60 -Label "Committing and pushing"
    Invoke-CommitAndPush -LocalPath $SelectedRepo.path -TargetBranch $TargetBranch -CommitMessage $CommitMessage

    Write-Progress-Bar -Percent 100 -Label "Phase 4 complete"
    Write-Log "=== Phase 4 complete ==="
    Copy-LogToSource -LocalPath $SelectedRepo.path
    Write-Host ""
    Write-Host "Deploy finished. Log: $LogFile"
    Write-Host "Log copy: $($SelectedRepo.path)\logs"
}