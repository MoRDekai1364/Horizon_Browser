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

## WebView2 Zero-Crash & Content-Script Guardrails
- **CDP Worker Debugger Breakpoint Crash (`0x80000003`)**:
  - *Never* call `Target.setAutoAttach` with `waitForDebuggerOnStart: true` on `worker`/`shared_worker` targets in WebView2 (`msedge.dll`); it triggers fatal `STATUS_BREAKPOINT` crashes on `DedicatedWorker thread` (`claude.ai`, `tiktok.com`, `z-library.sk`) and trips Cloudflare Turnstile (`devtools_present = true`).
- **`CoreWebView2.ProcessFailed` Discrimination**:
  - *Only* `BrowserProcessExited` and `RenderProcessExited` indicate actual tab/browser process death. Never reload, isolate, or mark a tab as `"⚠ Crashed"` on `RenderProcessUnresponsive` (`Exit=259 STILL_ACTIVE`), `FrameRenderProcessExited` (subframe/ad iframe exit), `GpuProcessExited`, or `UtilityProcessExited`.
- **Injected Script Performance & Layout Thrashing**:
  - *Never* run `querySelectorAll('*')`, recursive Shadow DOM walks, or read layout-forcing properties (`el.offsetParent`, `el.innerText`) inside `MutationObserver` callbacks. Scope queries to specific dialog/banner containers, use `el.textContent`, and disconnect startup observers after 5 seconds.
- **Synthetic Click Leakage (`!e.isTrusted`) & Modal Portal Masks**:
  - When programmatically dismissing modals (`closeBtn.click()`), always guard user-interaction listeners with `if (!e.isTrusted) return;` so synthetic clicks do not unlock autoplay or gesture-gated state.
  - When hiding login gates via CSS, hide the portal/mask containers (`[class*="DivModalContainer"]`, `[data-floating-ui-portal]`, `#tiktok-portal-container`) and enforce `pointer-events: auto !important; overflow: auto !important;` on `html, body, #app`.
- **YouTube MSE Video Playback & BotGuard (`poToken`) Integrity**:
  - *Never* mutate `video.playbackRate` (note: values `> 16.0` throw `NotSupportedError` in Chromium) on YouTube's single `<video>` element when AdGuard/SponsorBlock are active, as it wedges the `MediaSource` (MSE) buffer at `0:00`.
  - Keep `youtube.com` / `googlevideo.com` exempt from `FingerprintProfile` prototype/canvas/WebGL hooks and keep `GlobalUserAgent` aligned with `CoreWebView2Environment.GetAvailableBrowserVersionString()` so `User-Agent` and `Sec-CH-UA` never mismatch.
- **Crashed Tab Isolation in Shell Features**:
  - Any shell feature iterating across tabs (Split Screen, Tab Switcher, Command Bar) must skip CDP/script calls (`CallDevToolsProtocolMethodAsync`, `ExecuteScriptAsync`) when `view.IsProcessCrashed || view.IsCrashLoopHalted || view.IsHomeActive || view.MainWebView?.Visibility != Visibility.Visible`.
