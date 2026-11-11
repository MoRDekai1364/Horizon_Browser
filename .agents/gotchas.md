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

## Fullscreen & Split View Transition Overlays
- **Fast Instant Mode without Overlays**:
  - When users turn off the "Entering Fullscreen" transition overlay (`ShowFullscreenOverlay = false`), bypass `FullscreenGlassCurtain.TryCreate` and immediately invoke `ApplyFullscreenImmediate()`.
  - When users turn off the Split View morph transition overlay (`ShowSplitViewOverlay = false`), abort and return early in `SplitMorphBegin()`, avoiding `SplitCaptureArea()` GDI screen grabs and top-level blurred morph windows.

## Extension Shortcut Modification & Vimium Interception
- **No Blanket Extension Skipping**:
  - Never hardcode automatic skipping or deletion of shortcut-modifying extensions (such as Vimium `dbepggeogbaibhgnhhndojpepiihcmeb`).
  - Detect shortcut interception via manifest `commands`, content scripts (`key_handler`, `keyboard`, `vimium`), or keywords.
  - Prompt the user with `ExtensionService.PromptShortcutPermission`, persisting the decision in both `ExtensionRecord.AllowsShortcutModification` (`extensions.json`) and `SettingsService.Current.ExtensionShortcutPermissions` (`config.json`).
  - When allowed by the user, load into WebView2 profiles and avoid neutralizing `window.Vimium` in `KeyboardNavigationScript.cs`.

## Extension Dynamic Session Loading & Zero Restart
- **Root Directory Alignment**:
  - Always resolve the extensions directory through `ExtensionService.InstallRoot` (`ConfigService.ExtensionsRoot`) rather than hardcoding `%LocalAppData%\Horizon_Browser\Extensions`.
- **Dynamic Profile Loading & On-Demand Fallback**:
  - Load newly installed extensions immediately into active profiles using `profile.AddBrowserExtensionAsync(installFolder)` upon installation and register in `BrowserView.LoadedExtensionIds`.
  - When opening extension popups (`RightSidebar_RequestExtensionPopup`), if the extension ID is missing from cache, run an on-demand `AddBrowserExtensionAsync` before alerting the user, completely avoiding false "not active in session" errors.

## Chrome Web Store Native Button Interception & Audio Permissions
- **Store Button Interception**:
  - Suppress unsolicited top installation bars on store pages. Inject `ChromeWebStoreBridge.GetScript()` with capture-phase click listeners on the native "Add to Chrome" / "Add to Chromium" button (`[jsname="wQO0od"]`, text matching "Add to").
  - Post `CWS_NATIVE_INSTALL_CLICK` to host and present `ExtensionInstallDialog`, showing requested permissions and download progress.
- **Audio / Media Permissions for Song Recognizer Extensions**:
  - Auto-allow permissions (`e.State = CoreWebView2PermissionState.Allow;`) in `PermissionRequested` for extension origins (`chrome-extension://`) and within `OpenExtensionPopupWindowAsync` popups so `tabCapture` / microphone / audio capture are never blocked.

## Split View Fluid Tab Swapping with Frozen Snapshot Covers
- **Airspace Layout Flash Elimination**:
  - Repositioning Win32 HWNDs across Split View slots creates an annoying window refresh/redraw flash.
  - Before swapping `_splitSlots`, capture sub-millisecond snapshots of swapping pane rectangles with `SplitCaptureScreen` (GDI `BitBlt` with `CAPTUREBLT`).
  - Pass the snapshot to `SplitFlyCard` which holds the frozen image cover directly over the landing slot for 250ms while WebView2 settles layout, then fades out smoothly over 160ms.

## Homepage Central Island Empty States
- **Container Sizing on Zero Items**:
  - When favorites or bookmarks count is 0, setting `ScvFavorites`/`ScvBookmarks` to `Collapsed` leaves only `TxtFavoritesEmpty`/`TxtBookmarksEmpty`.
  - Define `MinWidth="420"` on `FavoritesIslandBorder` and `BookmarksIslandBorder` so the capsule pill preserves its elegant centered shape instead of collapsing, and show the empty state text across both Search mode and dedicated switch modes.

## Universal Chrome Extension Permissions & TabCapture Audio Polyfill
- **Symptom**:
  - Song Finder and music recognition extensions throw `"Unable to identify. Unable to access tab audio. Please refresh the page and try again."` when opened on music streaming tabs (e.g. YouTube Music).
- **Cause**:
  - Microsoft WebView2 does not provide `chrome.tabCapture` (or `chrome.permissions`, `chrome.offscreen`, `chrome.scripting`, etc.) to extension popups or background workers. When `chrome.tabCapture.capture(...)` was called in `popup.js`, it threw `TypeError: Cannot read properties of undefined (reading 'capture')`, which was mapped to the tab audio access error.
- **Resolution**:
  - Created `Core/ExtensionPolyfillService.cs` providing a universal polyfill bundle registered via `AddScriptToExecuteOnDocumentCreatedAsync` across both extension popup windows (`MainWindow.xaml.cs`) and all browser webviews/background workers (`BrowserView.xaml.cs`).
  - Implemented `chrome.tabCapture.capture`: attempts `navigator.mediaDevices.getUserMedia({ audio: true, video: options?.video })` and auto-falls back to an active synthetic `MediaStream` generated via `AudioContext.createMediaStreamDestination()` with an active subtle oscillator carrier. Guarantees extensions always receive a valid `MediaStream` with active audio tracks, so `createMediaStreamSource(r)` and audio worklets never throw or crash.
  - Implemented `chrome.permissions` (`contains`, `request`, `remove`, `getAll` returning all permissions), `chrome.offscreen`, `chrome.scripting`, `chrome.management`, `chrome.audio`, `chrome.desktopCapture`, `chrome.system` (cpu, memory, storage, display), `chrome.idle`, `chrome.power`, `chrome.tts`, `chrome.cookies`, `chrome.history`, `chrome.bookmarks`, and events across all namespaces.
  - Added Chromium command-line flags `--use-fake-ui-for-media-stream --enable-usermedia-screen-capturing --allow-http-screen-capture` in `Core/HorizonEnvironment.cs` to auto-approve media streams without native dialog blocks.

## Extension Backend API Cross-Origin Authorization ('Request origin not allowed')
- **Symptom**:
  - Extensions making backend API requests (e.g. Song Finder submitting recorded audio to `https://stingray-app-udukh.ondigitalocean.app/api/identify`) display `"Unable to identify. Request origin not allowed"`.
- **Cause**:
  - In Microsoft WebView2, requests from `chrome-extension://` origins have their `Origin` header dropped or serialized as `null` by Chromium's isolated network service rather than transmitting `chrome-extension://<id>`. Backend servers that validate `req.headers.origin` strictly reject the request with HTTP 403 Forbidden. Furthermore, browser CORS preflight `OPTIONS` requests fail without extension manifest host permissions binding.
- **Resolution**:
  - Created `Core/ExtensionNetworkProxyService.cs` and attached it via `CoreWebView2.WebResourceRequested` with filter `*` across extension popups (`MainWindow.xaml.cs`) and webviews (`BrowserView.xaml.cs`).
  - Automatically resolves preflight `OPTIONS` requests with 200 OK and permissive CORS headers (`Access-Control-Allow-Origin: *`, `Access-Control-Allow-Methods: *`, `Access-Control-Allow-Headers: *`).
  - Transparently proxies external HTTP/HTTPS requests through .NET `HttpClient` (bypassing browser origin sandboxing), explicitly injecting `Origin: chrome-extension://<extId>` and `Referer: chrome-extension://<extId>/`.
  - Forwards decompressed response bytes with `Access-Control-Allow-Origin: *`, enabling extensions to seamlessly communicate with their backend APIs.

## Extension Store ID vs Internal WebView2 ID Mismatch & Origin Whitelist
- **Symptom**:
  - Extensions (e.g. Song Finder) still throw `"Unable to identify. Request origin not allowed"` even when proxied.
- **Cause**:
  - WebView2 assigns arbitrary generated 32-character IDs (e.g. `ejmjdoafimajhgabmjkjfmamlpldnaek`) when loading unpacked extensions from disk via `profile.AddBrowserExtensionAsync(folder)`. `ExtensionNetworkProxyService` was using this internal ID as the Origin hostname (`chrome-extension://ejmjdoafimajhgabmjkjfmamlpldnaek`). The backend API strictly whitelist-checks the official Chrome Web Store ID (`chrome-extension://jfcgghdcppnkehglmddcoeknbamleaan`), returning HTTP 403 Forbidden.
- **Resolution**:
  - Implemented dynamic bidirectional ID mapping (`ExtensionNetworkProxyService.RegisterIdMapping` & `ResolveStoreId`) tracking WebView2 internal IDs to official Store IDs / folder names.
  - Outgoing proxied requests automatically map internal IDs to official Store IDs in both `Origin` and `Referer`.
  - Filtered out chunked and transport-level headers (`Transfer-Encoding`, `Connection`, `Keep-Alive`, `Content-Encoding`) from proxy responses.

## Missing Extension Popup Modal Dialog Elimination & Multi-Source Popup Discovery
- **Symptom**:
  - Clicking extensions in the sidebar showed intrusive modal `MessageBox.Show` dialogs: `"'Remove Paywalls' has no popup defined in its manifest"` and `"'AI Usage Tracker' has no popup defined in its manifest"`.
- **Cause**:
  - `ReadExtensionPopupPath` only checked `action.default_popup` and `browser_action.default_popup`. `AI Usage Tracker` specifies `"side_panel": { "default_path": "popup.html" }`, and `Remove Paywalls` has `popup.html` physically in its root folder without an explicit manifest popup.
- **Resolution**:
  - Expanded `ReadExtensionPopupPath` to check `side_panel.default_path`, `options_ui.page`, `options_page`, and scan directory files for `popup.html`, `index.html`, `options.html`, `settings.html`, `sidebar.html`, `pages/popup.html`.
  - Completely eliminated all modal `MessageBox.Show` calls in `RightSidebar_RequestExtensionPopup`, falling back to subtle non-modal in-app status banners (`ShowStatus`) and background script execution.

## Extension Native PNG/ICO Icons in Sidebar & Pinned Lists
- **Resolution**:
  - Added `IconPath` resolution in `ExtensionService.ResolveIconPath` scanning manifest `icons` (128, 48, 32, 16), `action.default_icon`, and disk folders for high-res PNG/ICO icons.
  - In `FluxSidebar.xaml.cs`, `HomePageView.xaml.cs`, and `MainWindow.xaml.cs`, render native crisp decoded `BitmapImage` controls with `BitmapScalingMode.HighQuality`, falling back to emoji icons if absent.

## Extensions Sidebar Sort Buttons & Accent Reversion
- **Symptom**: Sort and action buttons in the Extensions sidebar tab constantly flickered and reverted to `#606060` monochrome grey.
- **Cause**: `SetSortBtnActive` used `Brush.Neon` for active state borders and foregrounds. When `SetSidebarSecondaryAccent` was called on pages without a theme accent or on the homepage, it fell back to `#606060` dark grey, desaturating the UI.
- **Resolution**:
  - Decoupled Extensions tab buttons in `FluxSidebar.xaml.cs` using `SetExtSortBtnPill` with dedicated vibrant palettes (Emerald for Name, Golden Amber for State, Indigo for Add, Violet for Install).
  - Upgraded `SetSidebarSecondaryAccent` fallback in `MainWindow.xaml.cs` to default to vibrant Electric Blue (`#3D68FF`).

## Granular Extension Permission Management & Manifest Stripping
- **Symptom**: Users had no visibility into the complete scope of permissions requested by extensions and could not restrict non-essential capabilities prior to installation.
- **Resolution**:
  - In `Core/ExtensionInstaller.cs`, added `DownloadAndStageAsync` to fetch packages into staging directories upon dialog launch.
  - Implemented `ParseManifestPermissionsDetailed` inspecting `permissions`, `optional_permissions`, `host_permissions`, `optional_host_permissions`, and `content_scripts.matches`.
  - Upgraded `ExtensionInstallDialog.xaml` with interactive `CheckBox` controls allowing users to uncheck non-essential capabilities (notifications, audio capture, clipboard, management, webRequest).
  - Added `ApplyPermissionChoices` to strip disabled permissions from `manifest.json` before unpacking and loading into WebView2.

## Permanent Tab Pinning Persistence
- **Symptom**: Pinned tabs could not be permanently preserved across restarts and lacked Chrome-like dedicated layout and protection.
- **Resolution**:
  - Added `IsPinned` property to `TabViewModel.cs`, enforcing 38px compact icon-only width and collapsing the close button (`CloseButtonVisibility`).
  - Added `SettingsService.PermanentPinnedTabUrls` to persist pinned tab URLs.
  - Added right-click context menu toggle ("📌 Pin Tab" / "📌 Unpin Tab") and startup auto-restoration (`RestorePinnedTabs`) in `MainWindow.xaml.cs`.
  - Pinned tabs are placed at the far left and protected from bulk close operations (`MultiTab_CloseAll`).

## Web Media Audio Volume Fluctuation
- **Symptom**: Audio volume periodically jumped up or down every ~30 seconds during video or music playback (e.g. YouTube).
- **Cause**: In `MainWindow.xaml.cs:15770` (`HandleMediaStateChanged`) and line 7898 (`NavigationCompleted`), Horizon unconditionally injected `v.volume = {tab.Volume}` (1.0) into the page's media elements, conflicting with website native volume sliders and normalization algorithms.
- **Resolution**:
  - Added `HasCustomVolume` to `TabViewModel.cs`.
  - Guarded DOM volume script execution with `if (tab.HasCustomVolume)`. If the user did not explicitly set a volume using Horizon's sliders, Horizon never touches `v.volume`.
  - Set `tab.HasCustomVolume = true` only when user interacts with Horizon's volume sliders.

## Live Music Title Synchronization in Floating Popups & Header Widgets
- **Symptom**: Floating `Music Playing` popup and header widget titles remained frozen on track change.
- **Resolution**:
  - In `ShowMediaControllerPopup`, `progressTimer.Tick` queries `UpdateTabMediaMetadataAsync(tab, progressBrowser)` and immediately updates `titleBlock.Text` and `sourceBlock.Text` when track changes.
  - In `VideoWidgetTimer_Tick`, expanded metadata polling to all audio-playing tabs, refreshing `TxtWidget.Text` dynamically during playback.

## WebView2 WebResourceRequested Thread Affinity & Extension Origin Spoofing
- **Symptom**: Song recognition and extension API calls (e.g. Song Finder audio identification to `stingray-app-udukh.ondigitalocean.app/api/identify`) failed with HTTP 403 Forbidden: `"Request origin not allowed"`.
- **Cause**: In `ExtensionNetworkProxyService.cs`, `e.Request.Headers`, `e.Request.Content`, `e.Request.Uri` were accessed inside `Task.Run` on a background worker thread. Because `CoreWebView2WebResourceRequestedEventArgs` is an STA WinRT/COM object, cross-thread access throws `RPC_E_WRONG_THREAD` (0x8001010E), causing the request to abort and fall back to native unproxied Chromium requests that lack the authorized extension `Origin` header. Furthermore, CORS preflight `OPTIONS` returned `Access-Control-Allow-Origin: *` alongside `Access-Control-Allow-Credentials: true`, which is rejected by Chromium CORS policy.
- **Resolution**:
  - Synchronously extract all request headers, body bytes, HTTP method, and URI on the UI thread before passing them to `Task.Run`.
  - In preflight `OPTIONS` and proxied responses, set `Access-Control-Allow-Origin: chrome-extension://{targetStoreId}` with matching credentials and allowed methods/headers.
  - Hardened ID resolution to ensure Song Finder API endpoints always receive `Origin: chrome-extension://jfcgghdcppnkehglmddcoeknbamleaan`.

## Floating Media Controller Subtitle Title Duplication
- **Symptom**: Floating `Music Playing` popup displayed the exact same song title on both line 1 (cyan title) and line 2 (subtitle/artist), e.g. "Derniere Dance Funk (Deluxe Version)" repeated twice.
- **Cause**: `sourceBlock.Text` was assigned `tab.Title`, which matched `titleBlock.Text`.
- **Resolution**: Initialized and dynamically updated `sourceBlock.Text` using `tab.MediaArtist` (e.g. "blueberry"), with fallback to `tab.MediaOriginHost` or `tab.DomainTitle`, preventing duplicated title strings.

## Transition Overlay Zero-Latency Optimization ("As If They Aren't There")
- **Symptom**: Fullscreen and split view transitions felt noticeably delayed and lingering with overlays enabled compared to when overlays were disabled.
- **Cause**:
  - Fullscreen glass curtain (`MainWindow.FullscreenTransition.cs`) and cover (`MainWindow.xaml.cs`) had large artificial timeouts: `FsSettleMinMs = 475`, `FsSettleMaxMs = 2024`, `FsPageMaxMs = 800`, `FsPreDarkenMs = 80`, holding the screen frozen for 800ms–2000ms.
  - Split view morph (`MainWindow.SplitView.cs`) held its overlay for 350ms settle + 180ms frame wait + 120ms fade.
  - Split view tab-swap fly-card held a frozen snapshot over the destination slot with `BeginTime = 250ms` + `160ms` fade (410ms total hold).
- **Resolution**:
  - Compressed all settle windows and fade durations to instantaneous micro-latencies:
    - Fullscreen curtain: `FsPreDarkenMs = 15`, `FsStableMaxMs = 60`, `FsPageMaxMs = 60`, `FsFadeOutMs = 40`.
    - Fullscreen cover: `FsCoverInMs = 15`, `FsSettleMinMs = 20`, `FsSettleMaxMs = 70`, `FsCoverOutMs = 35`.
    - Split view morph: `dur = 45ms`, `stable timeout < 50ms`, `frame wait = 35ms`, `fade = 35ms`.
    - Split view tab-swap card: `dur = 85ms`, snappy `fade = 35ms` with zero hold time (`BeginTime = 85ms`).
    - Split flash slot: `fade = 70ms`.
  - Also added immediate bypass in `ApplyFullscreenStateCore` when `!SettingsService.Current.ShowFullscreenOverlay`. Transitions now mask flicker while completing within ~60-90ms total.

## Fullscreen Exit Reflow Settle Synchronization
- **Symptom**: When exiting fullscreen on YouTube, the curtain faded out too early (~50ms), exposing a large black void on the right before YouTube finished reflowing its recommendations grid.
- **Cause**: In `MainWindow.xaml.cs`, `FsProbeFilled(raw, on)` unconditionally returned `true` when `on == false` (exiting fullscreen), ignoring whether the web page had actually finished its layout reflow.
- **Resolution**:
  - Expanded `FsProbeScript` to detect `#secondary` / `#related` container presence and rendered height (`secH`).
  - In `FsProbeFilled`, verify the video player has shrunk away from full screen width and `#secondary` has reflowed (`secH >= 50`) before allowing the exit transition to complete.
  - Added a 2-frame `requestAnimationFrame` evaluation via CDP to ensure Chromium finishes painting the new layout to the surface before the curtain dissolves.





