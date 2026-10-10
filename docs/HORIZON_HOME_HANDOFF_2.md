# HORIZON BROWSER - HOMEPAGE HANDOFF 2 (PHASE PLAN + STATE)

Read this first. Older context: HORIZON_HANDOFF_STATE.md, HORIZON_HOME_LAYOUT_PLAN.md (R1-R8). This file supersedes their open-problem lists.
Repo: MoRDekai1364/Horizon_Browser. Branch: new_changes ONLY. Never touch official_release or Stable_pre-release. Never push. Output edits only.
Stack: WPF, .NET 8, WebView2, Windows. Owner (MoRDekai) compiles and tests. Do not compile.
GitHub credentials are in the owner's saved preferences. Never echo or write the token. The owner's working copy is AHEAD of the GitHub clone: the clone is stale. For every file you will edit, ask the owner to upload the current file before patching.

## HARD RULES
1. No comments in code.
2. Questions to the owner go through the built-in question UI (ask_user_input_v0).
3. Patch format per change: New/Existing file, file name, New change / Modify existing, Find, Replace. Owner's files are CRLF: multi-line Find blocks FAIL. Use single-line Find anchors only. For anything larger, return the COMPLETE file (CRLF) and say it replaces the owner's file. Verify every Find is unique with grep on a scratch copy before sending.
4. One phase at a time. Owner reports compile and test results between phases. Before each phase state: current scenario, confidence (97 percent target), missing information.
5. No hardcoded single-resolution values. Everything derived from live sizes.
6. Short direct communication, no filler, no sugar-coating.
7. New visuals need an ASCII mockup approved by the owner before code.
8. Ask before creating new files. Already approved new files: Controls/FolderCardView.cs, Controls/PagerDots.cs.
9. Debug aids: Ctrl+Shift+D copies a layout dump to the clipboard. Crash tapes and debug logs are in bin\Release\logs next to the exe. Ask for them on any crash.
10. Owner wants a phase plan and handoff before big work, and no ad-hoc patching.

## STATE AT HANDOFF (everything below compiled and applied by the owner unless noted)

Files created this session:
- Controls/FolderCardView.cs: FolderCardView (one folder card renderer: 3 tiles + 4-icon cluster, optional clusterOnly click), FolderCardHost (WrapPanel of cards for the center widgets, SnapHeight to whole rows, RowsChanged event), TileFallback (letter tiles with hashed color when an item has no icon), FolderSheetHost (overlay sheet over the center widget: grows from the cluster with SnapMotion, Underlays fade, Esc and buttons close, scrim does not close).
- Controls/PagerDots.cs: SnapMotion (bezier 0.175,0.885,0.32,1.1 over 0.4 s, stretch keyframes), PagerDots (dynamic count, 24 px hit cells, gliding pill, horizontal or vertical), SegmentThumb (sliding thumb behind the 3-way mode switch).

Files modified (owner has these versions):
- Controls/FolderDrawerView.cs: OpenFolderView (labels under tiles, back arrow, close, adaptive cell width, letter tiles) and FolderDrawerView (right column drawer) both use PagerDots and FolderCardView. DebugInfo and PageProbe added to both. Relayout crash fixed: pageHeight is clamped to at least 1 (exception was "-19.94 is not a valid value for property Height" at Relayout, caused by a ~4 px tall viewport). Dead DispatcherTimer debounce still present in both classes (never ticks); the queued Loaded-priority handler is what works.
- Controls/StackHost.xaml and .xaml.cs: vertical dots use PagerDots; folder overlay no longer blurs or darkens (Incoming fades instead, backdrop transparent).
- Controls/PuzzlePanel.cs: older budget and uniform-row work (A to G in HORIZON_HANDOFF_STATE.md). Now only used by the focus-mode grouped lists.
- Controls/FluxSidebar.xaml.cs: FaviconConverter now detects Google's default icon by hash, marks hosts with no icon (.none marker, 7 days, only when a server answered), purges cached defaults at startup. RESULT UNVERIFIED: chain icons still visible in the first folder after a restart. Next step is the owner's hash-group PowerShell output (see Open problems).
- Views/HomePageView.xaml: Fav/Bkm labels are now Favorites/Bookmarks. Wheel handler binding removed from both widget containers.
- Views/HomePageView.xaml.cs: ApplyCenterCards, CardSheet_Requested, BuildCenterSheet, BuildModeThumb, UpdateModeThumb, FixSearchBarWidth (measures all 3 modes with UpdateLayout, sets SearchBoxBorder.MinWidth to the widest), SyncSearchBarMinWidth, BookmarksBudget now sums PnlSearchArea.RowDefinitions ActualHeight (clamp-safe), ApplyScrollCap uses SnapHeight. Old unused methods still present: CardFolder_Requested, FolderCard_Click, WidgetContainer_PreviewMouseWheel, ExpandStackFor and the whole focus mode.

Verified by dumps: searchAreaBottom and islandBottom stay at or under the boundary at 1213x800 and 1533x800; center cards snap to whole rows; drawer pages and dots work (page=3/4 seen).

## OWNER DECISIONS (do not re-ask)
- Center block (search bar + island) stays vertically centered as a block.
- Cluster (4-icon cell) opens a folder sheet over the whole center widget. Only the cluster is clickable on a card; card padding does nothing; tiles launch sites.
- Folder sheet: back arrow, x, Esc close it. Clicking the dimmed margin does NOT close it.
- Missing favicon fallback: letter tile with colored background.
- Remove focus mode (EnterWidgetFocusMode) entirely.
- Wheel over widgets must not expand or collapse the right column (removed).
- Search-mode slider: full labels, bar width must not change when the mode changes, thumb glides iOS-style.
- Right column pager dots: bigger, dynamic, gliding pill (done).
- Right column drawer, folder view hover look: no blur ghost, no dark square (done).

## NEW REQUIREMENTS FROM THE OWNER (this handoff)
N1. The website shown in a right column widget (Gmail in screenshot) must render in MOBILE view, best fitting the column format.
N2. Show more / Show less buttons must be completely detached from the right column. They must expand the central widget into a Linux-start-menu-like view covering the entire browser window, with X and < buttons. (This replaces the earlier "island fills the central column" decision.)
N3. Search bar: no animation when the engine picker (search engine combo) appears or disappears when the mode switch changes. The owner does not care how, but wants no harsh search bar length shake. Bar width is already constant; the inner content pops.
N4. Bookmarks and Favorites widgets in the right column: removable by the user, OFF by default. When the right column has no widgets, a big + icon in the middle of the column invites the user to add one. The whole right column can be switched off in the homepage settings.
N5. Homepage settings window, Widget tab: too vertically long, the close button is not visible. Copy how the browser settings window (Views/SettingsWindow) handles it; the owner says that one is perfect.
N6. Browser settings window: move its search bar a bit higher and give it the blur effect the homepage settings window uses. Text in BOTH settings windows must stay clearly readable on any blurred background.

## PHASE PLAN (one phase at a time; ask for the listed files before each)

Phase 0 - Verify and baseline (no new features)
- Owner confirms: crash fix applied and no more Relayout exception; right column drawer and the cluster sheet render normally; search bar width equal across the 3 modes.
- Favicon check: owner runs
  Get-ChildItem "$env:LOCALAPPDATA\Horizon_Browser\HorizonData\FaviconCache\*.png" | Get-FileHash | Group-Object Hash | Sort-Object Count -Descending | Select-Object -First 5 Count, @{n='Example';e={$_.Group[0].Path}}
  and reports the output and whether clck.showmelinks.com.png exists. If the top hash group is huge, the reference hash does not match and the detection needs another reference or a size check; if the file does not exist, the chain icon comes from somewhere else (BookmarkItem icon path or the Google URL placeholder) and the converter path must be traced.
- Files needed: none.

Phase 1 - Homepage settings, Widget tab fits the window (N5)
- Read Views/HomePageSettingsWindow.xaml(.cs) and Views/SettingsWindow.xaml(.cs). Reuse the browser settings layout pattern (scrolling body, fixed header and footer, close button always visible).
- Acceptance: at small window heights the close button is visible on every tab and the Widget tab scrolls.
- Files to ask for: both settings windows (xaml and cs).

Phase 2 - Search bar engine picker transition (N3)
- Animate the engine combo collapse and expand (width and opacity with SnapMotion curve) while the bar width stays constant and the text box stretches smoothly. No width change of SearchBoxBorder at any time.
- Files: Views/HomePageView.xaml(.cs).

Phase 3 - Right column widgets: removable, off by default, plus button, settings switch (N4)
- New settings (SettingsService, default_settings.txt, saved config, Homepage settings UI): HomeShowRightColumn, plus per-widget flags for the Favorites drawer and Bookmarks drawer (default false). Existing configs: migration rule must be agreed with the owner first.
- Removing a widget: user action on the drawer (remove button or context menu; ask the owner which). Empty column: big + in the middle that opens the existing stack-element wizard (Views/StackElementWizardWindow) or an add menu (ask).
- HomeStack must handle zero, one and many pages; dots hidden below two pages.
- Files: Services/SettingsService.cs, default_settings.txt, Views/HomePageView.xaml(.cs), Controls/StackHost.xaml(.cs), Services/StackElementStore.cs, Controls/StackElementBuilders.cs, Views/HomePageSettingsWindow.xaml(.cs).

Phase 4 - Show more start-menu view (N2)
- Detach Show more / Show less from the right column (stop calling ExpandStackFor).
- New window-level host (generalize FolderSheetHost or add a sibling) that grows from the Show more button to cover the whole browser window (ask: does it also cover the tab and address header, or only the page area). Contents: all bookmarks as folder cards with the cluster sheet working inside it; X and < buttons close; Esc closes.
- Mockup first.
- Files: Views/HomePageView.xaml(.cs), Controls/FolderCardView.cs, MainWindow.xaml and .cs if the host must live above the header.

Phase 5 - Web widget mobile view (N1)
- Trace how StackElementBuilders creates web elements (WebView2). Apply a mobile user agent and a mobile viewport (CoreWebView2 UserAgent and DevToolsProtocol Emulation.setDeviceMetricsOverride with width from the live column width, mobile=true), re-applied on resize. Keep it per element so only the right column widgets are affected.
- Files: Controls/StackElementBuilders.cs, Services/StackElementStore.cs, MainWindow.Widgets.cs, Core/StealthEnvironment.cs if the user agent is configured there.

Phase 6 - Browser settings search bar and readability (N6)
- Move the browser settings search bar higher; add the same blur treatment the homepage settings window uses; make text in both windows readable on any blurred background (scrim or adaptive foreground; reuse the existing contrast service, log tag CONTRAST).
- Files: Views/SettingsWindow.xaml(.cs), Views/HomePageSettingsWindow.xaml(.cs), any shared theme resources.

Phase 7 - Cleanup and leftovers
- Delete dead code: Controls/PuzzlePanel.cs (AutoGridPanel, GroupCollapseMode, FolderFallback), CardFolder_Requested, FolderCard_Click, WidgetContainer_PreviewMouseWheel, dead DispatcherTimer debounce in FolderDrawerView.cs, collapse-style settings plumbing (keep SettingsService properties so saved configs load), the old CardFolder path, focus mode and its helpers, HomeRowLimits constants no longer used.
- Leftovers: dotted focus rectangle around the bookmarks scroller (FocusVisualStyle null), 20 px bottom misalignment between island and right column, center width anomaly (center grew to 1369 and overlapped the right column once, state unknown), phone-size check at 453x616, startup secondary tab row (shows on startup and disappears after a new tab is opened; owner wants it fixed after the homepage queue), duplicate StartupVideoFileName line in default_settings.txt (line 570 overrides line 52), glued @AppIcon line in default_settings.txt (must be: @AppIcon=src\icons\IMG_20260930_110456.ico).
- Files: ask per item.

Phase 8 - Multi-resolution verification
- Owner provides dumps and screenshots at 1920x1080, 1275x656, 955x656, 453x616. Check R1-R8 of HORIZON_HOME_LAYOUT_PLAN.md.

## KEY CODE FACTS
- Folder cards: FolderCardView(group, n, clusterOnly). Center uses FolderCardHost (n=2, scale 1.25). Drawer uses n from FolderDrawerView.Plan.
- Center host swap: ApplyCenterCards(favorites, groups) sets ScvFavorites/ScvBookmarks Content to the host when folder mode applies (FolderFor and not focus mode).
- Height budget: BookmarksBudget(rows) = PnlSearchArea.MaxHeight minus (sum of PnlSearchArea row heights minus ScvBookmarks.ActualHeight). ApplyScrollCap then sets scroller MaxHeight to FolderCardHost.SnapHeight(budget).
- Search bar width: FixSearchBarWidth runs at Loaded and when the page first becomes visible; MinWidth = widest measured mode, capped by MaxWidth in SyncSearchBarMinWidth.
- Stack: StackHost owns pages, vertical dots, overlay. HomeStack margin top and rest size are set by UpdateSearchAreaMaxHeight and related code. HomeFolderOpenMode setting values seen: Popup, InPlace.
- The glass effect is HomeGlassInlineLayer (Services/HomeGlassService.cs): a layer behind the UI, masked per registered element, honors ancestor opacity.
- Dump fields: page size, mode, rows, columns, boundary, gap, layoutHeight, searchAreaBottom, islandBottom, stackBottom, element sizes, drawers (groups, items, viewport, pages, page probe), centerSheet.

## HOW TO CONTINUE
1. Read this file. Do not re-clone unless the owner asks; ask for uploads of the specific files for the phase.
2. State scenario, confidence and missing information. Start with Phase 0 results from the owner, then Phase 1.
3. Send patches in the required format, one phase at a time.
