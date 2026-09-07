#Requires -Version 5.1
<#
    SmartCopy.ps1
    ---------------------------------------------------------------------
    Run this script from inside the folder whose contents you want to
    copy/move. It will:

      1. List every file/folder next to the script (except its own
         config file) and let you multi-select which ones to
         INCLUDE or EXCLUDE. Your choice is saved and reused next time
         (press E at the prompt to edit it).
      2. Ask Copy vs Cut (remembered, editable with C).
      3. Let you pick a target folder - file picker (P) or type a path
         (M) - remembered, editable with T.
      4. Check that the target drive has enough free space.
      5. Copy/move everything, overwriting same-named files.
      6. Save all choices to a small config .txt file next to the
         script, which is always auto-excluded from the file list and
         from the copy/move itself.

    All prompts show your saved default in [brackets] - just press
    Enter to reuse it.
#>

# -- Setup ----------------------------------------------------------------
$ScriptDir  = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$ConfigFile = Join-Path $ScriptDir "_SmartCopy.config.txt"
$ScriptSelf = $MyInvocation.MyCommand.Path

Add-Type -AssemblyName System.Windows.Forms | Out-Null
Add-Type -AssemblyName System.Drawing        | Out-Null

# -- Config load / save --------------------------------------------------
# Simple key=value text file. Multi-value fields are pipe-separated.
function Load-Config {
    $cfg = [ordered]@{
        Mode         = "Exclude"   # Include | Exclude
        Selection    = @()         # names, relative to $ScriptDir
        CopyOrCut    = "Copy"      # Copy | Cut
        TargetFolder = ""
    }
    if (Test-Path $ConfigFile) {
        foreach ($line in Get-Content -LiteralPath $ConfigFile -Encoding UTF8) {
            if ($line -notmatch '^\s*([A-Za-z]+)\s*=\s*(.*)$') { continue }
            $key = $Matches[1]; $val = $Matches[2]
            switch ($key) {
                'Mode'         { $cfg.Mode = $val }
                'Selection'    { $cfg.Selection = @($val -split '\|' | Where-Object { $_ -ne "" }) }
                'CopyOrCut'    { $cfg.CopyOrCut = $val }
                'TargetFolder' { $cfg.TargetFolder = $val }
            }
        }
    }
    return $cfg
}

function Save-Config($cfg) {
    $lines = @(
        "Mode=$($cfg.Mode)"
        "Selection=$(($cfg.Selection -join '|'))"
        "CopyOrCut=$($cfg.CopyOrCut)"
        "TargetFolder=$($cfg.TargetFolder)"
        "SavedAt=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    )
    Set-Content -LiteralPath $ConfigFile -Value $lines -Encoding UTF8
}

# -- Helpers --------------------------------------------------------------
function Format-Bytes($bytes) {
    $units = "B","KB","MB","GB","TB"
    $i = 0; $val = [double]$bytes
    while ($val -ge 1024 -and $i -lt $units.Length - 1) { $val /= 1024; $i++ }
    return "{0:N2} {1}" -f $val, $units[$i]
}

function Get-FolderSize($path) {
    (Get-ChildItem -LiteralPath $path -Recurse -Force -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
}

function Get-ItemSize($item) {
    if ($item.PSIsContainer) { return (Get-FolderSize $item.FullName) }
    return $item.Length
}

# -- 1. List candidate items ---------------------------------------------
function Get-CandidateItems {
    Get-ChildItem -LiteralPath $ScriptDir -Force |
        Where-Object {
            $_.FullName -ne $ConfigFile -and
            $_.FullName -ne $ScriptSelf
        } |
        Sort-Object -Property @{Expression = { -not $_.PSIsContainer }}, Name
}

# Interactive checklist: Up/Down moves the cursor, Space toggles the
# highlighted item, M flips Include/Exclude mode, Enter confirms.
# Draws the frame ONCE, then on every keypress only rewrites the one or
# two lines that actually changed (never a full clear/repaint) -- that's
# what keeps it flicker-free instead of visibly repainting every time.
function Show-InteractiveChecklist($items, $cfg) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "SmartCopy - Select Items"
    $form.StartPosition = "CenterScreen"
    $form.Width = 560
    $form.Height = 620
    $form.MinimumSize = New-Object System.Drawing.Size(400, 300)
    $form.Font = New-Object System.Drawing.Font("Segoe UI", 9)

    $header = New-Object System.Windows.Forms.Label
    $header.Text = "Items found in: $ScriptDir`r`n(the config file and this script are always excluded automatically)"
    $header.SetBounds(10, 10, 520, 40)
    $header.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
    $form.Controls.Add($header)

    $modeBox = New-Object System.Windows.Forms.GroupBox
    $modeBox.Text = "Mode"
    $modeBox.SetBounds(10, 55, 520, 45)
    $modeBox.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
    $form.Controls.Add($modeBox)

    $includeRadio = New-Object System.Windows.Forms.RadioButton
    $includeRadio.Text = "INCLUDE only checked items"
    $includeRadio.SetBounds(10, 18, 240, 20)
    $modeBox.Controls.Add($includeRadio)

    $excludeRadio = New-Object System.Windows.Forms.RadioButton
    $excludeRadio.Text = "EXCLUDE checked items"
    $excludeRadio.SetBounds(260, 18, 220, 20)
    $modeBox.Controls.Add($excludeRadio)

    if ($cfg.Mode -eq "Include") { $includeRadio.Checked = $true } else { $excludeRadio.Checked = $true }

    $listBox = New-Object System.Windows.Forms.CheckedListBox
    $listBox.SetBounds(10, 108, 520, 400)
    $listBox.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
    $listBox.CheckOnClick = $true
    $listBox.IntegralHeight = $false

    $savedSelection = New-Object System.Collections.Generic.HashSet[string]
    foreach ($n in $cfg.Selection) { [void]$savedSelection.Add($n) }

    foreach ($item in $items) {
        $tag  = if ($item.PSIsContainer) { "DIR " } else { "FILE" }
        $text = "{0}  {1}" -f $tag, $item.Name
        $idx  = $listBox.Items.Add($text)
        if ($savedSelection.Contains($item.Name)) {
            $listBox.SetItemChecked($idx, $true)
        }
    }
    $form.Controls.Add($listBox)

    $footer = New-Object System.Windows.Forms.Label
    $footer.Text = "Selected: 0 item(s)"
    $footer.SetBounds(10, 516, 400, 20)
    $footer.Anchor = [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left
    $form.Controls.Add($footer)

    $updateFooter = {
        $footer.Text = "Selected: {0} item(s)" -f $listBox.CheckedItems.Count
    }
    $listBox.Add_ItemCheck({
        $form.BeginInvoke([Action]{
            & $updateFooter
        }) | Out-Null
    })
    & $updateFooter

    $okButton = New-Object System.Windows.Forms.Button
    $okButton.Text = "OK"
    $okButton.SetBounds(370, 542, 80, 28)
    $okButton.Anchor = [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Right
    $okButton.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Controls.Add($okButton)

    $cancelButton = New-Object System.Windows.Forms.Button
    $cancelButton.Text = "Cancel"
    $cancelButton.SetBounds(456, 542, 80, 28)
    $cancelButton.Anchor = [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Right
    $cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($cancelButton)

    $form.AcceptButton = $okButton
    $form.CancelButton = $cancelButton

    $result = $form.ShowDialog()
    if ($result -ne [System.Windows.Forms.DialogResult]::OK) {
        $form.Dispose()
        return $cfg
    }

    $selected = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $items.Count; $i++) {
        if ($listBox.GetItemChecked($i)) {
            $selected.Add($items[$i].Name)
        }
    }

    $cfg.Mode = if ($includeRadio.Checked) { "Include" } else { "Exclude" }
    $cfg.Selection = @($selected)
    $form.Dispose()
    return $cfg
}

function Select-Items($cfg, $items) {
    $haveSaved = $cfg.Selection.Count -gt 0
    $savedNames = $cfg.Selection -join ", "

    if ($haveSaved) {
        Write-Host "Saved selection ($($cfg.Mode)): $savedNames" -ForegroundColor Yellow
        $ans = Read-Host "Press Enter to reuse it, or E to edit"
    } else {
        $ans = "E"
    }

    if ($ans -notmatch '^[Ee]$') {
        # Reuse saved selection as-is
        return $cfg
    }

    $cfg = Show-InteractiveChecklist $items $cfg
    Save-Config $cfg
    return $cfg
}

function Resolve-FinalList($cfg, $items) {
    if ($cfg.Mode -eq "Include") {
        return $items | Where-Object { $cfg.Selection -contains $_.Name }
    } else {
        return $items | Where-Object { $cfg.Selection -notcontains $_.Name }
    }
}

# -- 2. Copy vs Cut -------------------------------------------------------
function Choose-CopyOrCut($cfg) {
    Write-Host ""
    $ans = Read-Host "Copy or Cut? [current: $($cfg.CopyOrCut)]  (press C to change, Enter to keep)"
    if ($ans -match '^[Cc]$') {
        $pick = Read-Host "Type 'copy' or 'cut'"
        if ($pick -match '^(?i)cut$') { $cfg.CopyOrCut = "Cut" }
        elseif ($pick -match '^(?i)copy$') { $cfg.CopyOrCut = "Copy" }
        Save-Config $cfg
    }
    return $cfg
}

# -- 3. Target folder -----------------------------------------------------
function Choose-TargetFolder($cfg) {
    Write-Host ""
    if ($cfg.TargetFolder -ne "" -and (Test-Path -LiteralPath $cfg.TargetFolder)) {
        Write-Host "Saved target folder: $($cfg.TargetFolder)" -ForegroundColor Yellow
        $ans = Read-Host "Press Enter to reuse it, or T to change"
        if ($ans -notmatch '^[Tt]$') { return $cfg }
    }

    $ans = Read-Host "Choose target folder: P = folder picker, M = manually type path"
    if ($ans -match '^[Pp]$') {
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = "Choose the target folder"
        $dlg.ShowNewFolderButton = $true
        $result = $dlg.ShowDialog()
        if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
            $cfg.TargetFolder = $dlg.SelectedPath
        } else {
            Write-Host "No folder selected - aborting." -ForegroundColor Red
            exit 1
        }
    } else {
        $path = Read-Host "Enter full target folder path"
        if (-not (Test-Path -LiteralPath $path)) {
            $create = Read-Host "That folder does not exist. Create it? (Y/N)"
            if ($create -match '^[Yy]$') {
                New-Item -ItemType Directory -Path $path -Force | Out-Null
            } else {
                Write-Host "Aborting." -ForegroundColor Red
                exit 1
            }
        }
        $cfg.TargetFolder = (Resolve-Path -LiteralPath $path).Path
    }

    Save-Config $cfg
    return $cfg
}

# -- 4. Disk space check -------------------------------------------------
function Test-EnoughSpace($targetFolder, $totalBytes) {
    $drive = (Get-Item -LiteralPath $targetFolder).PSDrive
    $free = $drive.Free
    Write-Host ""
    Write-Host "Total to transfer : $(Format-Bytes $totalBytes)"
    Write-Host "Free on target ($($drive.Name):)  : $(Format-Bytes $free)"
    return $free -gt $totalBytes
}

# -- 5. Perform the copy/move --------------------------------------------
function Invoke-Transfer($cfg, $finalItems) {
    $verb = if ($cfg.CopyOrCut -eq "Cut") { "Moving" } else { "Copying" }
    Write-Host ""
    Write-Host "$verb $($finalItems.Count) item(s) to $($cfg.TargetFolder) ..." -ForegroundColor Cyan

    $okCount = 0; $failCount = 0
    foreach ($item in $finalItems) {
        $dest = Join-Path $cfg.TargetFolder $item.Name
        try {
            if ($item.PSIsContainer) {
                # Merge directory contents, overwriting same-named files inside
                if (-not (Test-Path -LiteralPath $dest)) {
                    New-Item -ItemType Directory -Path $dest -Force | Out-Null
                }
                Get-ChildItem -LiteralPath $item.FullName -Recurse -Force | ForEach-Object {
                    $rel = $_.FullName.Substring($item.FullName.Length).TrimStart('\','/')
                    $subDest = Join-Path $dest $rel
                    if ($_.PSIsContainer) {
                        New-Item -ItemType Directory -Path $subDest -Force | Out-Null
                    } else {
                        $subDestDir = Split-Path $subDest -Parent
                        if (-not (Test-Path -LiteralPath $subDestDir)) {
                            New-Item -ItemType Directory -Path $subDestDir -Force | Out-Null
                        }
                        Copy-Item -LiteralPath $_.FullName -Destination $subDest -Force
                    }
                }
                if ($cfg.CopyOrCut -eq "Cut") {
                    Remove-Item -LiteralPath $item.FullName -Recurse -Force
                }
            } else {
                Copy-Item -LiteralPath $item.FullName -Destination $dest -Force
                if ($cfg.CopyOrCut -eq "Cut") {
                    Remove-Item -LiteralPath $item.FullName -Force
                }
            }
            Write-Host "  OK  $($item.Name)" -ForegroundColor Green
            $okCount++
        }
        catch {
            Write-Host "  FAIL $($item.Name) - $($_.Exception.Message)" -ForegroundColor Red
            $failCount++
        }
    }

    Write-Host ""
    Write-Host "Done. $okCount succeeded, $failCount failed." -ForegroundColor Cyan
}

# -- Main -----------------------------------------------------------------
Write-Host "=== SmartCopy ===" -ForegroundColor Magenta

$cfg   = Load-Config
$items = @(Get-CandidateItems)

if ($items.Count -eq 0) {
    Write-Host "No files or folders found next to the script. Nothing to do." -ForegroundColor Yellow
    exit 0
}

$cfg = Select-Items       $cfg $items
$cfg = Choose-CopyOrCut    $cfg
$cfg = Choose-TargetFolder $cfg

$finalItems = @(Resolve-FinalList $cfg $items)

if ($finalItems.Count -eq 0) {
    Write-Host "Your current Include/Exclude selection leaves nothing to transfer." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Will $($cfg.CopyOrCut.ToLower()) the following:" -ForegroundColor Cyan
$finalItems | ForEach-Object { Write-Host "  - $($_.Name)" }

$totalBytes = 0
foreach ($item in $finalItems) { $totalBytes += (Get-ItemSize $item) }

if (-not (Test-EnoughSpace $cfg.TargetFolder $totalBytes)) {
    Write-Host ""
    Write-Host "Not enough free space on the target drive. Aborting - nothing was changed." -ForegroundColor Red
    exit 1
}

$confirm = Read-Host "`nProceed? (Y/N)"
if ($confirm -notmatch '^[Yy]$') {
    Write-Host "Cancelled - nothing was changed." -ForegroundColor Yellow
    exit 0
}

Invoke-Transfer $cfg $finalItems
Save-Config $cfg