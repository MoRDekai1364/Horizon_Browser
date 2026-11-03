# Gotchas & Failure Mode Ledger

## Inno Setup `CreateProcess failed; code 2` & Elevation Mismatch
- **Symptom**: Updating Horizon Browser failed with dialog:
  `Unable to execute file: C:\Program Files\Horizon_Browser\Horizon.Browser.exe. CreateProcess failed; code 2. The system cannot find the file specified.`
- **Cause**:
  1. The installer runner (`Horizon.Updater` and `install_auto.ps1`) previously passed InstallForge CLI arguments (`-silent -path "<dir>"`). Inno Setup ignored `-path` and defaulted to `C:\Program Files\Horizon_Browser`.
  2. The packaged installer had placed files inside `bin\Release\Horizon.Browser.exe` instead of at `{app}` root. At the end of installation, Inno Setup's `[Run]` line attempted to run `{app}\Horizon.Browser.exe`, failing with Win32 Error 2.
  3. `UseShellExecute = false` without elevation verb caused UAC elevation failures (Error 740 / Error 5).
- **Resolution**:
  1. Detect Inno Setup and pass native arguments: `/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /DIR="{targetDir}"`.
  2. Set `ProcessStartInfo.UseShellExecute = true` and `Verb = "runas"`.
  3. Post-install check `EnsureBrowserBinariesAtRoot` promotes any binaries unpacked into `bin\Release\` up to the root target directory.
  4. In `Setup_Horizon_Browser_*.iss`, use Pascal script in `CurStepChanged(ssPostInstall)` to promote binaries and guard `[Run]` with `ExeExistsAtAppRoot`.
  5. Multi-location target resolver `GithubUpdateService.ResolveAppropriateInstallLocation(...)` ensures updates target the appropriate channel directory (e.g. `D:\Program Files\Horizon_Browser_Official_Release`).
