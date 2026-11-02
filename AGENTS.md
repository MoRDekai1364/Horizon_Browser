# Horizon Browser - Workspace Knowledge & Engine Directives

## 1. Stack & Architecture
- **Framework & Runtime**: .NET 9.0 Windows WPF (x64), C# 13.
- **Web Engine**: Microsoft WebView2 (`Microsoft.Web.WebView2`), Chromium CDP (Chrome DevTools Protocol).
- **Core Components**:
  - `MainWindow.xaml` / `MainWindow.xaml.cs`: Shell host, tab management, sidebar docking, and CDP capture pipeline.
  - `Views/HomePageView.xaml` / `.cs`: Customizable homepage canvas with dynamic blur glass backdrop.
  - `Views/TabSwitcherWindow.xaml` / `.cs`: Topmost Ctrl+Tab switcher with live website previews, search filter, and simulated glass.
  - `Controls/FluxSidebar.xaml` / `.cs`: Multi-mode collapsible tool/tab sidebar.
  - `Services/SettingsService.cs`: Settings configuration and persistence model.

## 2. Key Commands
- **Build**:
  ```powershell
  dotnet build -c Release "Horizon.Browser.csproj"
  ```
- **Run Smoke Test**:
  ```powershell
  & "bin\Release\Horizon.Browser.exe"
  ```

## 3. Engineered Workarounds & Gotchas
- **WPF StaticResource Scope Across Nested UserControls**:
  - *Symptom*: Browser fails to launch with dialog `"Could not launch Browser. Provide value on 'System.Windows.StaticResourceExtension' threw an exception."`
  - *Cause*: A control (e.g., `FluxSidebar.xaml`) referenced `{StaticResource PillComboBoxStyle}` which was defined locally in `Views/HomePageView.xaml.Resources`. Since `FluxSidebar` is hosted by `MainWindow.xaml` and not a child of `HomePageView`, the XAML parser cannot locate the resource in its visual or logical ancestor tree.
  - *Resolution*: Define shared UI styles and converters in `Themes/Horizon.xaml` (merged into `App.xaml.Resources`) or style locally with explicit setters, preventing missing static resource lookup failures.
- **Chromium CDP `Page.captureScreenshot` Conflict**:
  - *Symptom*: Protocol exception `"fromSurface is not supported with captureBeyondViewport"`.
  - *Cause*: In background/unvisited tabs where `isVisibleViewport == false`, passing both `captureBeyondViewport: true` and `fromSurface: true` causes Chromium to abort the screenshot request.
  - *Resolution*: When `captureBeyondViewport` is set to `true`, omit `fromSurface`. Implement a multi-tier fallback helper (`TryCdpCaptureAsync`) falling back from beyond-viewport to viewport-only to basic capture.
- **WPF Animation Clock Precedence on Auto-Hide Sidebar**:
  - *Symptom*: Sidebar opens upon clicking the button, but immediately snaps back closed or crashes layout.
  - *Cause*: A storyboard or animation clock (`BeginAnimation(WidthProperty, ...)`) retains high precedence over local property setters. Subsequent manual changes don't persist.
  - *Resolution*: When manually expanding or collapsing, clear the active animation clock with `SidebarContainer.BeginAnimation(FrameworkElement.WidthProperty, null)` before setting `SidebarContainer.Width = targetW`.
- **Header Auto-Hide Inactivity Timer & Airspace Outside-Click Lock Release**:
  - *Symptom*: Header auto-hide works on initial hover, but fails to hide after clicking elements inside the header because `_isHeaderLockedOpen` became permanently set to `true`, and outside clicks on WebView2 Win32 HWND never triggered WPF routed events to clear the lock. Furthermore, there was no inactivity timeout for cursor resting in the header.
  - *Resolution*: Implemented inactivity tracking in `_sensorPollTimer` (16ms loop) evaluating Win32 `GetCursorPos` and `GetAsyncKeyState(0x01)`. If the cursor is outside the header for > 350ms, or inside the header with no motion/clicks for > 3000ms (while not focused on typing or dragging tabs), the header automatically closes and clears `_isHeaderLockedOpen`. Any outside click immediately clears the lock across native Win32 WebView2 HWND boundaries.
- **Dynamic Island Pill Auto-Collapse on Outside Clicks**:
  - *Symptom*: Expanding the Dynamic Island pill to the big state (`580x82`) remained stuck open when clicking anywhere else in the browser or on web pages.
  - *Resolution*: Track mouse button down transitions in `_sensorPollTimer` and check `screenPt` against `DynamicIslandPill.PointToScreen()`. If a click occurs outside the pill's bounding rectangle (on WPF elements or native Win32 WebView2 HWNDs), `ExpandDynamicIsland(false)` immediately animates the pill back to its compact resting state (`120x28`).
- **Homepage Mode-Switch Island Visibility Gating & Persistent Search Row Anchoring**:
  - *Symptom*: Clicking "Favorites" or "Bookmarks" on the homepage caused the centralized search bar to jump upwards by ~175px instead of persisting its vertical position across all states.
  - *Cause*: `PnlSearchArea` had `VerticalAlignment="Center"`. In "Search" mode, the island below is collapsed (height ~0), so `PnlSearchArea` measures ~110px. When "Favorites" or "Bookmarks" was selected, the island expanded to ~350px, causing WPF to re-center the entire ~460px container, pulling the search bar 175px up toward the screen ceiling.
  - *Resolution*: Set `PnlSearchArea.VerticalAlignment = VerticalAlignment.Top` in both XAML and code-behind, anchoring `PnlSearchArea.Margin.Top` to a calculated `persistentSearchTop = Math.Min(idealCenterTop, maxSafeTop)`. The search bar stays strictly frozen at the optical center across `Search`, `Favorites`, `Bookmarks`, and `Widgets` modes while islands expand smoothly downward below it. Dynamic available height clamping on `ScvFavorites` and `ScvBookmarks` (`Math.Min(320.0, availIslandH)`) ensures zero overflow past the bottom island buttons.
- **Homepage Mode-Switch Island Visibility Gating**:
  - *Symptom*: Clicking "Favorites" or "Bookmarks" on the homepage tri-switch / segment pill (`PnlSearchMode`) resulted in no island appearing, even when items were present.
  - *Cause*: `UpdateFavBookmarksIslandVisibility()` and `RefreshFavorites()`/`RefreshBookmarks()` gated visibility behind legacy global settings `SettingsService.Current.HomeShowFavoritesWidget` and `HomeShowBookmarksWidget`, which defaulted to `false` in `config.json`.
  - *Resolution*: When the user explicitly clicks a mode segment (`_searchMode == "Favorites"` or `_searchMode == "Bookmarks"`), the island visibility must evaluate to `true` directly, decoupling the active switch state from the legacy passive layout toggle. Added empty-state textblocks (`TxtFavoritesEmpty`, `TxtBookmarksEmpty`) so zero-item scenarios still present an interactive container.
- **TabSwitcher Wallpaper Blur Bleed**:
  - *Symptom*: TabSwitcher card displays a yellow/brown halo/border artifact when viewed over dark web pages (e.g. Reddit).
  - *Cause*: Wallpaper backdrop was sampling `WeatherBridge.ThemeWallpaper` even when browsing web pages where the homepage is hidden.
  - *Resolution*: Check `IsHomeActive()`. Collapse the wallpaper glass host and zero `wallLayer.Opacity` whenever a web page is active, revealing the sleek `#EB121419` dark glass card with matching 16px corner radius.
- **Homepage Sidebar Animation Throttling & Deferral**:
  - *Symptom*: Sidebar animation is laggy on the homepage, while smooth on web pages.
  - *Cause*: Resizing Column `*` triggers continuous `HomePageView.SizeChanged` reflows, recomputing card budgets, scroller clips, and WPF software blurs 60 times/second, while `SidebarContainer.SizeChanged` repeatedly recalculates screen transforms.
  - *Resolution*: Set `IsSidebarAnimating = true` during `ToggleSidebar`. Bypass per-frame layout and blur updates until `anim.Completed`, settling layout in a single pass via `OnSidebarAnimationCompleted()`.
- **Deterministic Unicode Escapes for UI Glyphs**:
  - *Symptom*: Header widgets and menus render mojibake characters (`dY r`, `,`, `A`).
  - *Cause*: Literal multi-byte UTF-8 emoji strings in C# source files can become corrupt when loaded across mismatched codepages or tools.
  - *Resolution*: Use deterministic C# Unicode escape sequences (`\U0001F550`, `\u26A1`, `\U0001F4BE`, `\U0001F3B5`, `\u26C5`, `\U0001F9EE`, `\U0001F4DD`, `\u21C4`, `\U0001F4C5`, `\U0001F514`, `\U0001F50B`, `\u2261`).
- **Aegis Resource Management & Rust Core Integration**:
  - `Native/horizon_core`: Native Rust cdylib (`horizon_core.dll`) providing sub-millisecond process tree memory auditing (`horizon_get_browser_process_memory`), SIMD wallpaper luminance sampling (`horizon_calculate_region_luminance`), and zero-allocation text fit calculation (`horizon_compute_text_fit`).
  - `Services/AegisResourceManager.cs`: Multi-tiered tab governor managing `Active` -> `Warm` -> `Dormant` -> `Suspended` -> `Cryo` with state capture and emergency shedding under critical RAM pressure.
  - `Services/WidgetSyncGuard.cs`: Deterministic layout & typography synchronizer guaranteeing background borders and text content scale and fit in lockstep with zero clipping.
- **Roslyn CodePage 65001 & XAML Character Entities**:
  - *Symptom*: Context menus, sidebars, and widgets display mojibake sequences like `â†©`, `ðŸ”§`, `â¬‡` in Windows builds.
  - *Cause*: Roslyn `csc.exe` defaults to the system ANSI code page (Windows-1252) for source files lacking a UTF-8 BOM, misinterpreting multibyte UTF-8 byte streams.
  - *Resolution*: Specify `<CodePage>65001</CodePage>` in `Horizon.Browser.csproj`, convert all emoji in C# string literals to `\uXXXX` / `\U0000XXXX` escape sequences, and use XML numeric character entities (`&#x1F527;`, `&#x2699;`) in XAML files.
- **Homepage Multi-World Architecture & Rust Layout Engine**:
  - `Views/HomePageView.Themes.cs`: Handles lifecycle for 9 distinct visual launchers (Horizon Flow, CyberTerminal HUD, Metro Matrix, Rust Neo-Canvas, Retro CRT Cyberdeck, Editorial Paper, Starship LCARS, Zen Garden, Win95 RetroDesk).
  - `horizon_compute_homepage_layout` in `horizon_core.dll`: Subpixel symmetrical grid coordinate solver powering Arc/macOS spatial dock layouts.
- **Homepage Theme Peer Hierarchy in XAML**:
  - *Symptom*: Switching homepage theme causes all content to disappear (`GridHomeLayout: 0x0 vis=Collapsed`).
  - *Cause*: Theme host containers declared inside `GridHomeLayout` or `PnlSearchArea` inherit parent visibility. Collapsing `GridHomeLayout` hides all children.
  - *Resolution*: Declare all `ThemeHost_*` containers as direct sibling peers of `GridHomeLayout` at `Grid.Row="1"` inside the main host grid (`<Grid Margin="0">`).
- **Homepage Floating Sidebar Flyout vs Web Page HWND Push Mode**:
  - *Symptom*: Expanding the sidebar on the homepage shifted centered search and widget islands to the left by half the sidebar width.
  - *Cause*: `MainWindow.xaml` used a 2-column Grid where Column 1 pushed Column 0 (`ViewContainer`).
  - *Resolution*: When on the homepage, place `SidebarContainer` in overlay mode spanning columns (`Grid.SetColumn(SidebarContainer, 0)`, `Grid.SetColumnSpan(SidebarContainer, 2)`, `HorizontalAlignment = Right`, `Panel.SetZIndex = 100`, `ColSidebar.Width = 0`), keeping homepage width constant and widgets perfectly centered. When navigating to web pages, automatically restore push mode (`ColSidebar.Width = Auto`) so the Win32 WebView2 HWND does not obscure or clip the sidebar. Toggleable via `SettingsService.Current.HomeSidebarFloatingOverlay`.
- **Sidebar Width Animation Layout Thrashing & Content Squishing**:
  - *Symptom*: Sidebar stuttered heavily and crushed child cards/text during open/close animations. Rapid clicking caused visual snapping.
  - *Cause*: Animating container width while child elements stretch forces WPF to re-measure and re-arrange all 41 list items on every pixel change without clipping (`ClipToBounds="False"`). Omission of `anim.From` caused animation restarts to snap. Unchecked `SizeChanged` triggered wallpaper re-crops during motion.
  - *Resolution*: Set `ClipToBounds="True"` on `SidebarContainer`, lock `RightSidebar.Width` to its target width with `HorizontalAlignment="Right"` during animation to freeze child layout (zero reflows), provide `anim.From = SidebarContainer.ActualWidth`, and guard `RefreshHeaderWallpaperCrop()` with `if (IsSidebarAnimating) return;`.
- **Tab-Identical Sidebar Spring Physics & Homepage Header Blur Isolation**:
  - *Sidebar Spring Physics*: Ported the asymptotic spring physics formula from header tabs (`width += (target - width) * 0.22` on a 16ms `DispatcherPriority.Render` loop), replacing WPF Storyboards with instant, zero-snap directional reversals and buttery 60 FPS deceleration.
  - *Homepage Header Blur Isolation*: In `RefreshHeaderWallpaperCrop()`, when `IsHomeActive()` is true, the header blur background spans 100% of the window width with `HeaderSidebarStripHost` permanently collapsed, ensuring the header glass never updates, splits, or shifts when the sidebar shows or hides on the homepage. Normal web pages preserve standard behavior.
- **Homepage Search Bar Vertical Anchoring & Island Spring Physics**:
  - *Search Bar Vertical Anchoring*: `PnlSearchArea` is locked to `VerticalAlignment="Top"` with `Margin.Top` anchored to the composite height midpoint (`(totalHeight - 380) / 2.3`), preventing vertical jumping when toggling between Search (empty island) and Favorites/Bookmarks/Widgets.
  - *Island Spring Popup & Sizing*: `GridFavBookmarksIsland` uses `TranslateTransform` (Y: 28 -> 0) and `Opacity` (0 -> 1) governed by a 16ms `diff * 0.22` render loop (`HomePageView.WidgetBoard.cs`). Width expands up to 1320px and height dynamically allocates up to `HomeBottomBoundary() - islandTop - 28`, cleanly fitting 7+ favorites and 14+ bookmarks folders with zero bottom overflow.
- **Windows 11 Widget Board & Slot Reordering**:
  - `SegModeWidgets` serves as the 4th mode in the mode switch revealing `PnlWidgetBoardContainer`.
  - Default layout: Clock upper-left (0,0), Weather lower-left (0,1), Calendar far-right (2,0, RowSpan=2), Media Player (1,0), Aegis System RAM (1,1), Quick Notes (1,2).
  - Cards support inline title renaming (`\u270E`), directional swapping (`◄`, `▲`, `▼`, `►`), and live persistence in `SettingsService.Current.HomeWidgetBoardConfigJson`.
- **Homepage Sidebar Overlay Blur Isolation vs Docked Webpage Blur**:
  - *Symptom*: When sidebar became a floating flyout overlay on the homepage, its blur glass was horizontally mirrored, stretched/squished during open/close animations, and lacked acrylic glass edge contrast.
  - *Cause*: Legacy docked push mode used `_sidebarOwnBlurBrush.RelativeTransform = new ScaleTransform(-1, 1, 0.5, 0.5)` to mirror the browser's right edge outwards.
  - *Resolution*: In `RefreshSidebarBlurCrop()`, when `IsHomeActive()` is true, set `_sidebarOwnBlurBrush.RelativeTransform = null`, sample unmirrored wallpaper directly underneath the overlay, display `SidebarOverlayTint` (`#B812151E` acrylic scrim with 1px border), and freeze `SidebarWallpaperGlass.Width = contentW` with `HorizontalAlignment="Right"` during slide animation so it reveals without rubber-banding. Restore legacy mirrored behavior and collapse tint on normal web pages.
- **Real Physical Sliding Sidebar Animation vs Clip Mask Wipe**:
  - *Symptom*: Sidebar appeared to "unmask" or "wipe" in place rather than slide in as a physical object. The content was stationary while a boundary opened up.
  - *Cause*: `RightSidebar` was aligned to `HorizontalAlignment.Right` inside `SidebarContainer`. Its right edge was pinned to the right edge of the window, so as `SidebarContainer.Width` grew, the contents remained stationary on screen while the left clip edge moved.
  - *Resolution*: Wrapped `SidebarWallpaperGlass`, `SidebarSiteGlow`, and `RightSidebar` in `SidebarSlideHost` (`Grid`) with `TranslateTransform x:Name="SidebarSlideTransform"`. On Homepage (Overlay mode), `SidebarSlideHost` slides along the X axis (`SidebarSlideTransform.X`: `320 -> 0` on open, `0 -> 320` on close) via pure GPU composition (`diff * 0.22`), with zero WPF layout recalculations. On web pages (Docked Push mode), set `SidebarSlideHost.HorizontalAlignment = HorizontalAlignment.Left` with fixed `Width = contentW` so the left edge of the sidebar enters first and physically travels across the screen as Column 1 expands, pushing the web view in complete lockstep. Rapid toggling immediately flips the spring target coordinate for instant zero-snap reversal.
- **Widget Board Configuration Slot Index Collapse**:
  - *Symptom*: All widget cards in the fullscreen widget board collapsed onto a single cell at (0, 0), stacking on top of each other.
  - *Cause*: Introducing an intermediary `SlotIndex` property caused existing `config.json` payloads without `SlotIndex` to deserialize with default `0` for all cards, collapsing all cards into `CanonicalSlots[0]`.
  - *Resolution*: Revert to direct `Column` (0, 1, 2) and `Row` (0, 1, 2) coordinates on `WidgetBoardCardConfig` without slot-index indirection, and add self-healing validation in `LoadWidgetBoardCards` that detects any duplicate coordinates and resets to canonical defaults. Retain the fullscreen `_startMenuHost` presentation with blur backdrop.
- **FadeGlassIn WPF FillBehavior & Sidebar Blur Toggle Alternation**:
  - *Symptom*: Sidebar wallpaper blur background only displayed on alternating open/close attempts (every second time).
  - *Cause*: `FadeGlassIn` configured `FillBehavior = FillBehavior.Stop`, resetting opacity back to base value (0.0) upon completion. Concurrently, `HomeGlassService.Bind` fired on `SidebarContainer.Width = 0` (close), collapsing `SidebarWallpaperGlass.Clip` to 0 width.
  - *Resolution*: Set `FillBehavior = FillBehavior.HoldEnd` and permanently set `Opacity = 1.0` in `anim.Completed`. In `HomeGlassService.Bind`, guard homepage overlay mode to decouple `SidebarWallpaperGlass` from zero-width container clipping, and explicitly set `SidebarWallpaperGlass.Clip = null`.
- **Clock Widget Glass Clip Corner Radius & Instant Frame-0 Measurement**:
  - *Symptom*: Right edge of the clock widget background was clipped with a sharp vertical edge, and the background appeared after quite a while.
  - *Cause*: `HomeGlassInlineLayer` applied `RectangleGeometry(rect)` without corner radius when an ancestor clip was detected, stripping the 24px rounded corners. Measuring was deferred until `ActualWidth > 0` and opacity was initialized to 0 in XAML.
  - *Resolution*: Pass `cr.TopRight` / `cr.TopLeft` into `RectangleGeometry(rect, rx, ry)` in `HomeGlassInlineLayer.Refresh()`, give `SearchBlurPill` and `ClockWeatherIslandBorder` `Grid.ColumnSpan="2"`, set initial `Opacity="1"`, measure `PnlClockWeather` on frame 0, and invalidate `_homeGlass` on bounds updates.
- **Centered Bottom VPN & Wallpaper Island**:
  - `PnlBottomRightButtons` moved to `HorizontalAlignment="Center"`, `VerticalAlignment="Bottom"`, `Margin="0,0,0,16"`. Both `PnlVpnAdBlock` and `BtnChangeWallpaper` normalized to `Height="26"`, `CornerRadius="13"`, and registered with `_homeGlass` for dynamic acrylic blur. Safe scroll boundaries in `HomeBottomBoundary()` adapt seamlessly.
- **Windows 11 Native Share UI & CsWinRT ABI Projection**:
  - *Symptom*: Clicking "Share Page" failed to trigger the Windows 11 native share sheet.
  - *Cause*: In modern .NET 8/9 with CsWinRT, WinRT projected classes like `DataTransferManager` cannot be resolved via legacy CLR COM interop (`Marshal.GetObjectForIUnknown(dtmPtr) as DataTransferManager` returns `null`). Furthermore, Windows 11 Share Sheet requires the parent window to regain full foreground activation after context menu dismissal.
  - *Resolution*: Use `DataTransferManager.FromAbi(dtmPtr)`, dispatch with a short 100ms delay to allow context menu tear-down, and populate `request.Data` with resilient Title and WebLink/Text fallbacks across web pages and native homepage tabs.
- **Header Right-Click Context Menu Glass Theme**:
  - Replaced default classic WPF context menu with `HeaderContextMenuStyle` (`#E612151E` dark acrylic glass, 12px corner radius, `#35FFFFFF` 1px border, 20px drop shadow, 6px rounded hover pills `#25FFFFFF`, and matching recursive submenu popup glass styling), bringing it into seamless visual harmony with the homepage.
- **Header Far-Right Close Button Edge Alignment & Invisible Auto-Open Sensor**:
  - Eliminated the 10px right margin on `PnlHeaderRightIcons` (`Margin="5,0,0,0"`), allowing `BtnClose` to extend flush against the screen's top-right corner to fulfill Fitts's Law.
  - The sidebar auto-open detection remains fully active and responsive via global cursor polling in `_sensorPollTimer` (`clientPt.X >= ActualWidth - 20`) with zero visible pill or seam.
- **Dynamic Island Top-Notch Flush Placement & Draggable Position**:
  - Re-anchored the capsule to `Y = 2px` (flush top-edge notch) to prevent overlap with web page headers (e.g. YouTube search bar).
  - Added horizontal dragging (`_islandUserOffsetX`) clamped between 10px and screen boundary, with click-vs-drag distinction.
  - Polled visibility continuously in `_sensorPollTimer` (100ms) with `_isHeaderOpen` boolean tracking, ensuring instant appearance whenever the header closes.
- **Sidebar Auto-Open Scrollbar Handle Immunity**:
  - Excluded the browser vertical scrollbar track (4–22px from right edge) on web pages, requiring cursor to touch the outer bezel (`clientPt.X >= ActualWidth - 3`) or top/bottom corners to trigger auto-open.
  - Added mouse button press guard (`(GetAsyncKeyState(0x01) & 0x8000) != 0 || Mouse.LeftButton == MouseButtonState.Pressed`) to immediately abort auto-show while scrolling or dragging.
- **4-Column 15-Card Fullscreen Widget Board Matrix**:
  - Expanded `GridWidgetBoard` to 4 columns (`320px`, `340px`, `340px`, `340px`) and 4 rows (`Auto`), centering in fullscreen `_startMenuHost`.
  - Added complete card roster: Calculator (interactive keypad & evaluator), Converter (currency, length, weight, temp), Battery & Power (Win32 `GetSystemPowerStatus`), Notifications, Data Saver & Privacy Shield, Tab Group Manager, Clipboard Monitor, Reading List, and Session Restorer.
  - Self-healing config migration auto-merges new cards into empty cells for existing installations.
- **Centered Bottom Island Integration (VPN, Battery, Wallpaper, Settings)**:
  - Unified `PnlVpnAdBlock`, `PnlBatteryPill`, `BtnChangeWallpaper`, and `BtnHomeSettings` into a single horizontal stack centered at the bottom of the homepage (`PnlBottomRightButtons`).
  - Standardized all 4 controls to `Height="26"`, `CornerRadius="13"`, `#4D141822` acrylic glass with 1px `#35FFFFFF` borders and live blur registration via `_homeGlass.Register(...)`.
- **Fullscreen Glass Curtain Animations**:
  - Preserved the original multi-stage glass curtain transition in `MainWindow.FullscreenTransition.cs` (`FadeInAsync`, `WaitForOwnerStableAsync`, `WaitForPageSettledAsync`, `FitToOwner`, `FadeOutAsync`), providing smooth cinematic fullscreen transitions while keeping WebView2 page settles and Dynamic Island visibility in sync.
- **TikTok Login Popups & Guest Restrictions Bypass & Renderer Infinite Loops**:
  - `SCRIPT_TIKTOK_INTERVENTION` injected via `AddScriptToExecuteOnDocumentCreatedAsync` in `HorizonEnvironment.cs` dismisses login modals, removes overlay masks, and enforces `overflow: auto !important`.
  - *Gotcha*: Observing `{ attributes: true, attributeFilter: ['style', 'class'] }` in `MutationObserver` while mutating inline styles causes an infinite synchronous recursion on the Chromium renderer thread, triggering `RenderProcessUnresponsive` crashes.
  - *Resolution*: Never mutate attributes inside an attribute observer. Rely on declarative CSS rules (`display: none !important;`) and observe `childList` only with a 400ms debounce timer. Video autoplay on page load is gated behind `SettingsService.Current.TikTokAutoplayOnLoad`.
- **Favorites & Bookmarks Folders and Drag-and-Drop Pipeline**:
  - `FavoriteFolders` and `BookmarkFolders` lists in `SettingsService.cs` persist custom folders even when empty. Empty folders render on the homepage with an empty folder glyph (`📁`).
  - `FolderCardView.cs` handles `AllowDrop = true`, accepting dropped `PinItem` and `BookmarkItem` references to assign their `Category` dynamically.
  - In `FluxSidebar.xaml.cs`, selecting a folder chip filters the view and updates `BtnAddPin` / `BtnAddBookmark` to `+ PIN TO [FOLDER]`, directing added URLs straight into the opened container.
  - Context menus on both homepage and islands support "Add last opened website" by querying `HistoryService.Items` for the most recent non-home browsing target.
- **System Info Dialog Header Polish**:
  - Removed redundant `[ ]` (`pinGlyph`) maximize/bracket character from `ShowSystemInformationWindow()` header.
- **Chrome & Transition Invariants**:
  - *Edge-Flush Caption Buttons*: Window caption buttons must have zero outer margin against the window boundary so that corner mouse clicks trigger reliably (Fitts's Law). Auto-open hover zones must use transparent polling rather than physical visual spacers.
  - *Continuous Media Playback in Transitions*: Window state changes (fullscreen, auto-hide collapse) must never pause active media elements or hammer document execution scripts.
  - *No Restarts on Cosmetic Settings*: Modifying sidebar docking, auto-hide thresholds, or theme configurations must apply dynamically via `ApplyLayoutState()` without restarting the application.
  - *WebView2 Airspace Dynamic Island Capsule*: Direct WPF children in `MainWindow.xaml` overlapping `ViewContainer` are covered by the native Win32 child HWND of WebView2. Dynamic Island / Live Capsule headers must be hosted inside a topmost WPF `Popup` (`AllowsTransparency="True"`, `StaysOpen="True"`, `Placement="Custom"`) or layered HWND to float with 100% alpha transparency, drop shadows, and full hit-testing over web views.
  - *Deterministic Blur Joint Points*: Sibling WPF element data-bindings (`{Binding ActualWidth, ElementName=...}`) evaluate asynchronously on deferred layout passes. Synchronizing background and companion blur borders requires physical joint constraints via `WidgetSyncGuard.BindIsland(companionElements: ...)` so dimensions and margins update simultaneously in the exact same render frame.
- **Chromium CDP `Emulation.setDeviceMetricsOverride` Viewport Freeze & Constrained Box**:
  - *Symptom*: When opening a second tab identical to the first one or backgrounding a tab, the original tab becomes completely unresponsive and renders constrained into a small box in the upper-left with black surrounding bars.
  - *Cause*: Calling `EnsureBackgroundViewportAsync` applied `Emulation.setDeviceMetricsOverride` without immediate cleanup in a `finally` block during CDP snapshot captures (`CaptureViaCdpAsync`). Additionally, `InitializeAsync` was asynchronously overriding device metrics before initial layout completed, causing tabs to get permanently trapped in DevTools device emulation mode.
  - *Resolution*: Removed preemptive `EnsureBackgroundViewportAsync` from `InitializeAsync()`. Wrapped `CaptureViaCdpAsync` in a `try...finally` block that immediately executes `_ = browser.ClearBackgroundViewportAsync()`. Made `ClearBackgroundViewportAsync()` public and idempotent (always invoking `Emulation.clearDeviceMetricsOverride`), and bound it to `IsVisibleChanged`, `MainWebView.SizeChanged`, `Tabs_SelectionChanged`, and `WakeTab`.

- **Account Container Isolation, about:blank Hangs & Deferred Initialization**:
  - *Symptom*: Opening a new tab with a specific account container profile caused the tab to become permanently stuck on `about:blank` when attempting to search or load a URL.
  - *Cause*: In `BrowserView.Navigate()`, when `MainWebView.CoreWebView2 == null`, it stored `_pendingUrl = url;` and returned without triggering initialization. Because the native homepage starts with `MainWebView.Visibility = Visibility.Collapsed`, deferred initialization was never resumed. Furthermore, `CreateNewTab` did not pass `containerId` to the newly instantiated `BrowserView` or `TabViewModel`.
  - *Resolution*: In `BrowserView.Navigate()`, explicitly trigger `_ = EnsureInitializedAsync(forceInit: true)` whenever `MainWebView.CoreWebView2 == null`. Pass `containerId`, `containerName`, and `containerColor` into `newTab` and `newBrowser` in `CreateNewTab`, and wrap profile initialization in a `try...catch` block with fallback to standard environment initialization.
- **SPA DOM Streaming Layout Thrashing & `RenderProcessUnresponsive` Freeze**:
  - *Symptom*: Complex streaming web applications (e.g. Claude.ai, Reddit, Discord) became completely frozen, unresponsive to clicks, and could not be scrolled.
  - *Cause*: `masterObserver` in `BrowserView.xaml.cs` ran un-debounced on every DOM mutation, executing recursive `walkShadow` with `querySelectorAll('*')` and querying `el.innerText` on hundreds of buttons. For streaming SPAs producing rapid mutations, this saturated the Chromium renderer thread and triggered `RenderProcessUnresponsive` crashes.
  - *Resolution*: Debounce `masterObserver` with a 400ms timer. Cap consent detection to the first 5 seconds or 5 attempts from page load, permanently setting `window.__horizonConsentDismissed = true;` so consent detection never executes during ongoing web app usage.
- **Auto-Hide Edge Proximity Glow & Dynamic Island Capsule Predictability**:
  - *Symptom*: The auto-hide header Dynamic Island / Live Capsule pill header showed unpredictably or failed to appear after closing the header. In addition, the edge proximity glow was inactive.
  - *Cause*: `UpdateDynamicIslandVisibility()` relied on `HeaderContainer.Height == 0` during active WPF Storyboard animations before `Height` had settled, and was only invoked at the start of closing animations. Storyboard completion callbacks on resource instances failed to fire consistently.
  - *Resolution*: Track open state explicitly via `_isHeaderOpen`. Re-evaluate `UpdateDynamicIslandVisibility()` on every 100ms tick of `_sensorPollTimer`. Drive `HeaderProximityGlow` and `SidebarProximityGlow` via cursor proximity in `_sensorPollTimer`, providing a smooth Microsoft Store style soft blurry acrylic glow as the cursor approaches the auto-hide trigger boundaries.

- **Wave Arc Proximity Glow Waveform & Dynamic Tab Auto-Widening**:
  - *Wave Arc Proximity Glow*: Replaced 850px rectangular border with a 30% wider (1105px) smooth cubic Bezier arc waveform path (M 0,0 C 300,0 420,38 552.5,38 C 685,38 805,0 1105,0 Z) with radial gradient and 22px blur effect. The peak crest of the wave arc (552.5) is dynamically aligned with the mouse cursor (	argetX = clientPt.X - glowW / 2.0;), creating a fluid wave aura hanging from the top edge.
  - *Dynamic Tab Auto-Widening*: Tab headers now compute natural desired widths using FormattedText at 12pt SemiBold plus tab chrome overhead (container badge, close button, margins, sleep indicator), clamped between [TAB_DEFAULT_WIDTH, 340.0]. When available space allows, each tab widens to show its complete title with zero truncation. When space is constrained, tabs scale down proportionally before overflowing. 
ewTab.PropertyChanged automatically schedules reflows when titles or badges update.
- **Header Battery Widget Alignment with Homepage Battery Button**:
  - *Symptom*: When charging, the header battery widget rendered two lightning symbols (⚡ 85% ⚡ · 1h 20m) and looked disjointed from the homepage battery pill.
  - *Cause*: GetBatteryWidgetText() included a leading icon (⚡ when charging) AND an additional chargingSuffix (⚡ when charging) along with inline time remaining text.
  - *Resolution*: Unified the header battery widget text format with the homepage button (TxtBatteryPill.Text): ${icon} {Services.BatteryBridge.Percent}%, eliminating the redundant second lightning glyph. Adjusted BatteryTrackBorder and BatteryFillRect height to 2.5px with matching 1.25px corner radius, and relocated extended status details (charging state, time remaining) to HeaderWidgetBorder.ToolTip.

- **Sidebar Mini Mode (52px) Strict Settings Compliance & Dedicated Panes/Utilities Layout**:
  - *Symptom*: Sidebar mini mode was missing or erratic even when enabled in settings (`SettingsService.Current.SidebarMiniMode = true`). When forced to 52px, Panes and Utilities buttons were crushed together horizontally into unclickable 13px slivers, and opening the sidebar when mini mode was disabled would falsely trigger mini mode mid-animation.
  - *Cause*: `GetTargetSidebarWidth()` previously lacked the `SidebarMiniMode` branch, and `FluxSidebar.xaml.cs` used a dynamic `ActualWidth <= 70` heuristic that triggered during the `0 -> 320` opening animation. Furthermore, `PnlHeaderTabModes` was a single horizontal row incapable of gracefully housing multiple modes in a 52px column.
  - *Resolution*: Tied mini mode strictly to `SettingsService.Current.SidebarMiniMode` across `GetTargetSidebarWidth()`, `ApplySidebarMode()`, `ApplyLayoutState()`, and removed the dynamic width-guessing heuristic. Restructured `PnlHeaderTabModes` into a dual-layout grid: horizontal row in expanded mode (320px) and a dedicated 3-row vertical stack in mini mode (52px) with `BtnToggleSidebarDock` (Row 0, 38x24), `BtnSidebarPanesTab` (Row 1, 38x32 with neon active border), and `BtnSidebarUtilitiesTab` (Row 2, 38x32 with neon active border). In mini mode, the Utilities Hub switches to `Template.UtilitySidebarItemMini` (38x38px icon tiles with rich description tooltips) while collapsing category chips and search inputs.

- **Dynamic Island / Executive Frosted Glass Hub & Multi-Mode Deck Revamp**:
  - *Symptom*: The header pill had a solid dark background with zero blur, felt dysfunctional, suffered from cut text and cramped buttons, and had literal escaped unicode sequences (e.g. `\\u23F8`).
  - *Cause*: `DynamicIslandPill` lacked Win32 hardware blur-behind (`SetWindowCompositionAttribute` / DWM backdrop), used a narrow 400px deck that clipped buttons and text, and lacked high-value ambient tab context and live badges.
  - *Resolution*: Transformed the capsule into an Executive Frosted Glass Hub following user reference mockups (Images 0–3). Implemented a dual-state design:
    1. *Resting Micro-Capsule* (120x28px, CornerRadius 14px): Displays compact status (`\u266B` / `\u2726`, `•••`, `\u23F1`) resting unobtrusively at the top edge.
    2. *Executive Command Deck* (540x74px, CornerRadius 18px): Expanded via click or hover into two generous rows with frosted acrylic glass backdrop (specular top rim, light refraction line, and Win32 hardware blur via `ACCENT_ENABLE_BLURBEHIND` and `DWMWA_SYSTEMBACKDROP_TYPE = 4`). Row 0 houses mode chips (`+ Ambient`, `⏱ Timer`, `🎵 Media`, `📝 Notes`, `⬇ Down`) and `▼ Header` expand button. Row 1 houses the Ambient Deck with full tab favicon (`CoreWebView2.FaviconUri`), tab title, domain host, vertical separator, and live clickable status badges (`⏱ Timer` with cyan dot indicator, `📝 Notes` with amber dot indicator).

- **Arc-Style Quick Search Command Bar (`Ctrl+T`), Custom Shortcuts & JavaScript Scripting**:
  - *Context*: Rather than opening a blank tab blindly, users desired Arc Browser's Command Bar (`Ctrl+T`) with quick URL navigation, fuzzy tab switching, built-in productivity creation shortcuts (`.new`), and user-defined custom shortcuts with arbitrary JavaScript execution on active tabs.
  - *Architecture & Implementation*:
    1. `Views/ArcCommandBarWindow.xaml` / `.xaml.cs`: Centered, floating, acrylic frosted glass palette with 16px corner radius, specular top rim, and hardware blur (`DWMWA_SYSTEMBACKDROP_TYPE = 4` & `SetWindowCompositionAttribute`). Features real-time search filtering across open tabs, browser commands, `.new` templates, user shortcuts, and direct web navigation (`↑↓` navigate, `↵` execute, `Esc` dismiss).
    2. `Services/CustomShortcut.cs` & `Services/CustomShortcutService.cs`: Persistent shortcut store (`custom_shortcuts.json`) supporting URLs with `{query}`/`{url}`/`{title}` placeholders, internal browser commands, and active tab JavaScript scripts executed via `CoreWebView2.ExecuteScriptAsync`. Default built-ins include: Toggle Dark Invert, Copy Markdown Link, Smooth Auto-Scroll, Dismiss Overlays, New Google Doc/Meet/Notion/Sheet/Repo.
    3. `MainWindow.Commands.cs` & `Window_KeyDown`: Wired `Ctrl+T` to `ArcCommandBarWindow.ShowCommandBar(this)`, global hotkey interception via `CustomShortcutService.TryExecuteHotkey`, and centralized browser dispatch via `ExecuteBrowserCommand`.

- **Dynamic Island Pill Transparency, Click Stability, Audio Edge Visualizer & Glow/Homepage Polish**:
  - *Symptom 1 (Black Box)*: A massive solid black rectangle (600x150) covered the top of the screen where the Dynamic Island popup sat.
    *Cause*: Applying Win32 `SetWindowCompositionAttribute` / DWM backdrop onto a transparent WPF `Popup` HWND (`WS_EX_LAYERED`) overrides per-pixel alpha and forces DWM to render the entire window bounding box solid black.
    *Resolution*: Removed Win32 HWND composition calls on the popup, letting WPF per-pixel alpha render the rounded frosted glass card with 100% transparent surround.
  - *Symptom 2 (Click to Close)*: Clicking anywhere inside the expanded pill closed it.
    *Resolution*: In `DynamicIsland_PreviewMouseLeftButtonUp`, only expand when in compact resting state; interior clicks when expanded never toggle collapse. Added `PnlIslandTabInfo_Click` to reveal header on tab info click.
  - *Symptom 3 (Content Cutoff)*: Expanded pill content clipped top chips and bottom badges.
    *Resolution*: Expanded dimensions to 580x82px and removed rigid clipping bounds.
  - *Symptom 4 (Audio Edge Visualizer)*: Added `DynamicIslandAudioGlow` outer border that gently pulses and breathes with live music playback (`MediaBridge.IsPlaying`) via an 80ms render loop.
  - *Symptom 5 (Header Proximity Glow Stutter & Boundary Mismatch)*: Glow stuttered at 100ms and displayed across the full window width, even though the header sensor only triggers in the left 2/3.
    *Resolution*: Constrained glow strictly to `clientPt.X < ActualWidth * (2.0 / 3.0)` with horizontal edge taper, sped up `_sensorPollTimer` to 16ms (60 FPS), and replaced `Margin` mutations with `HeaderProximityGlowTransform` (`TranslateTransform`).
  - *Symptom 6 (Homepage Central Column)*: Search elements sat off-center and high up.
    *Resolution*: Spanned `PnlSearchArea` across `Grid.ColumnSpan="3"` with `VerticalAlignment="Center"` and `HorizontalAlignment="Center"`.

- **Homepage Bookmarks & Favorites Interactive Textbox Focus, Context Menus & Scroller Budget**:
  - *Symptom 1 (Unclickable Input Fields)*: Add Favorite and Add Bookmark dialog text fields were unclickable, failed to receive keyboard focus, and showed no caret.
    *Cause*: In `StyleRoundedTextBox`, the `ControlTemplate` placed a `ScrollViewer` named `PART_ContentHost` without `Focusable="false"`. In WPF, an unflagged `ScrollViewer` intercepts mouse clicks and steals focus away from the parent `TextBox`. Furthermore, `Foreground` and `CaretBrush` defaulted to black on `#1A1A1A` dark glass, rendering input invisible.
    *Resolution*: Explicitly set `host.SetValue(UIElement.FocusableProperty, false);`, configured white text and white caret (`tb.Foreground = Brushes.White`, `tb.CaretBrush = Brushes.White`), wired `PreviewMouseLeftButtonDown` direct focus, and focused the name field on `dlg.Loaded`. Added Enter (submit) and Escape (cancel) key handling on `dlg.PreviewKeyDown`.
  - *Symptom 2 (Missing Edit / Context Menus)*: Right-clicking favorite or bookmark cards on the homepage did nothing or bubbled to the canvas.
    *Resolution*: Added right-click event hooks (`Pin_MouseRightButtonUp`, `Bookmark_MouseRightButtonUp`, `FolderCard_MouseRightButtonUp`) revealing native dark glass context menus with Open, Open in New Tab, Open in Background Tab, Copy URL, Edit (with interactive dialog pre-filled with existing data), Pin/Bookmark toggle, and Delete with instant UI refresh.
  - *Symptom 3 (Bookmarks Island Truncation & Gap)*: Second row of cards was clipped horizontally in half with labels hidden, and asymmetrical blur margins created an awkward bottom gap.
    *Cause*: `ScvBookmarks.MaxHeight` was hard-clamped to `Math.Min(320.0, availIslandH)`, which prematurely cut off 2-row layouts (~340px+ required), and margins were asymmetrical (`-22px` bottom border vs `0px` container).
    *Resolution*: Expanded island budget to `Math.Min(600.0, availIslandH)` computed against `HomeBottomBoundary() - (PnlSearchArea.Margin.Top + searchRowH) - 36.0`, standardized symmetric padding (`-16, 14, -16, -16` on borders and `0, 14, 0, 16` on containers), and added mouse-wheel scrolling (`Scroller_PreviewMouseWheel`).

- **Homepage Island Corner Insets, Dimension Expansion & Scroller Clip Immunity**:
  - *Symptom*: Bookmarks and Favorites islands displayed cut-off rounded corners (sharp 90° corners, cards colliding with curved glass boundaries), and row 2 of cards was sliced horizontally with cramped dimensions.
  - *Cause*: `FavoritesIslandBorder` and `BookmarksIslandBorder` had only 16px horizontal margins and 0px top padding ($14 - 14 = 0$) relative to the content containers, causing square card edges to collide with the 24px corner radius arc. Additionally, `ApplyScrollerClip` applied a tight `RectangleGeometry` clip directly on `ScvFavorites`/`ScvBookmarks` without card padding, while `FolderCardView.SnapHeight` truncated row 2 down to `tops[1]` (the top coordinate of row 2).
  - *Resolution*:
    1. *Expanded Dimensions*: Increased `maxCenterContentWidth` ceiling from 1320px to 1480px (decoupled from artificial column balancing), raised `ScvFavorites`/`ScvBookmarks.MaxHeight` to 650px, and increased `availableForCards` floor to 340px.
    2. *Corner Inset & Breathing Buffer*: Standardized island border margins to `-30, 4, -30, -8` and content containers to `0, 22, 0, 24`, creating a generous 30px horizontal, 18px top, and 32px bottom buffer. All cards sit cleanly inside the flat interior of the capsule with zero corner collision.
    3. *Fluent Corner Curvature*: Increased `CornerRadius` on both island borders to 28px.
    4. *Scroller Clip & Snap Immunity*: Set `scroller.Clip = null` in `ApplyScrollerClip` so child card transforms and labels render unclipped, and updated `SnapHeight` in `FolderCardView.cs` to measure `tops[i] + singleCardHeight` with a 50px budget window, guaranteeing full multi-row visibility.


- **Homepage Island Complete Capsule Symmetry & Native Wallpaper Anti-Aliased Fidelity**:
  - *Symptom 1 (Sharp 90° Cut on Island Left & Bottom Corners)*: Bookmarks and Favorites islands displayed flat straight cuts on the left and bottom edges, completely missing their 28px rounded corners, and multi-row bookmarks had the second row sliced horizontally in half.
    *Cause*:
    1. In `Views/HomePageView.xaml.cs` (`ApplyHomeLayoutConstraints`), lines 1186 and 1190 continually overwrote `FavoritesIslandBorder.Margin` and `BookmarksIslandBorder.Margin` to `Thickness(-30, 4, -30, -8)` on every resize/layout event. When `HomeGlassService` evaluated ancestor clips (`GetAncestorClipRect`), any border extending into negative coordinates ($x < 0$ or $y < 0$) triggered `isMeaningfullyClipped = true`, which sliced off the left 30px and bottom 8px with a rectangular clip, replacing the 28px rounded corners with flat 90° cuts.
    2. In `UpdateSearchAreaMaxHeight`, `PnlSearchArea.MaxHeight` was calculated as `layoutHeight - rowOffset - PnlSearchArea.Margin.Top - reserve`, clamping `PnlSearchArea` to ~400-500px and setting `PnlSearchArea.Margin.Bottom = reserve`. This physically truncated child islands and caused `HomeGlassService` to slice the blur glass right through row 2 cards.
    3. `persistentSearchTop` only reserved 260px headroom below the search bar, leaving insufficient space for 2-row layouts on smaller viewports.
    *Resolution*:
    1. Replaced the negative margin in `ApplyHomeLayoutConstraints` with `Thickness(0, 12, 0, 16)` and container margins with `Thickness(0)`, keeping islands completely within positive parent bounds with zero ancestor clipping.
    2. Set `PnlSearchArea.MaxHeight = double.PositiveInfinity` and eliminated the bottom reserve margin push, removing parent container clipping.
    3. Dynamically constrained `ScvFavorites.MaxHeight` and `ScvBookmarks.MaxHeight` to `spaceBelow - islandOverhead` (accounting for 42px padding, 28px margin, and 48px toggle button), guaranteeing the island ends exactly 16px above `HomeBottomBoundary()` with all 4 rounded corners (`CornerRadius="28"`) fully intact.
    4. Reserved 560px headroom in `persistentSearchTop` when islands are active, keeping the search bar safely positioned while allocating at least 450px for card content.
    5. Updated `FolderCardHost.SnapHeight` to include child card margins ($12\text{px}$) and expanded the snap window tolerance to `budget + 85.0`, ensuring 2 complete rows of folder cards and text labels render 100% visible with zero scroll clipping. Added single-mode row resolution in `ResolvedRows()`.
  - *Symptom 2 (Jagged / Pixelated Wallpaper Artifacts)*: Homepage wallpapers rendered with blocky, pixelated, or aliased stair-step artifacts.
    *Cause*: `RenderOptions.EdgeMode="Aliased"` had been explicitly set on `RootHomeGrid` and `BgImageBrush`. In WPF, `EdgeMode="Aliased"` disables primitive and bitmap anti-aliasing. Additionally, `LoadWallpaperAndSample` lacked `BitmapCreateOptions.PreservePixelFormat`.
    *Resolution*: Removed `EdgeMode="Aliased"`, ensuring anti-aliasing remains enabled. Added `BitmapCreateOptions.PreservePixelFormat` to preserve native 24-bit/32-bit color fidelity, and enforced `RenderOptions.BitmapScalingMode="HighQuality"` on `RootHomeGrid`, `BgImageBrush`, and `CrossfadeWallpaper`.
- **Nested `.git` in Subdirectories Blocking Git Push / Staging**:
  - *Symptom*: Running `git add -A` or `push_to_github.py` fails with:
    `error: 'Native/horizon_core/' does not have a commit checked out`
    `error: unable to index file 'Native/horizon_core/'`
    `fatal: adding files failed`
  - *Cause*: `cargo new --lib horizon_core` automatically created a nested `.git` repository inside `Native/horizon_core/`. Because no commits were made inside the inner repo, Git treated it as an uncommitted, unborn submodule, blocking `git add -A` on the parent repository.
  - *Resolution*: Removed `Native/horizon_core/.git/`. The Rust crate's source files (`Cargo.toml`, `Cargo.lock`, `src/lib.rs`) are now tracked directly by the root repository, while build output (`target/`) is ignored via `.gitignore`.
