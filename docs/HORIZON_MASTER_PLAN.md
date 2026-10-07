# HORIZON BROWSER MASTER PLAN (created 2026-10-07)

Place at: docs/HORIZON_MASTER_PLAN.md (new_changes branch). Single source of truth. Supersedes the task lists in HORIZON_HANDOFF.md and HORIZON_HANDOFF_D4.md; those two stay as design references (section 5 of HORIZON_HANDOFF.md holds the approved homepage design).

## 0. RESUME PROTOCOL (any Claude, any account)

CURRENT POINTER: V0-1

1. Clone: gh repo clone MoRDekai1364/Horizon_Browser, branch new_changes ONLY. NEVER push. Credentials are in the user's saved preferences; never write them into files or replies.
2. Read this whole file, then read the task section named by CURRENT POINTER.
3. Run the section's VERIFY gate against the cloned repo before any code. Statuses marked [?] were inferred from file presence, not tested.
4. Execute ONE step at a time, in order. After each step: output the code change, then output a find/replace patch for THIS file that ticks the step and moves CURRENT POINTER.
5. When the pointer reaches a step marked ASK, ask first (ask_user_input_v0, max 3 questions per call). Never code before an ASK is answered.
6. If the user pasted compile errors, fix them before advancing the pointer.
7. Warn the user when context is getting full, and finish by moving the pointer so the next account resumes cleanly.

Status legend: [ ] todo, [~] in progress, [x] done, [?] exists in code, unverified, [!] blocked (reason inline).

## 1. RULES (apply to every task)

- Stack: WPF / XAML / C# .NET 8, net8.0-windows10.0.19041.0, UseWPF + UseWindowsForms, ImplicitUsings, Nullable enabled. Not a web stack. dotnet is not available in the container.
- Output format per change: file path; New/Existing file; file name; New change / Modify existing; Find (exact Ctrl+F text); Replace (exact text). Full file only for new files or full rewrites. Never shrink a file when rewriting.
- Validate every Find block against a patched copy (each matches exactly once). Check XAML well-formedness. State "not compiled". Never compile; the user compiles and pastes errors.
- No comments in code. Do not rename uploaded files. Ask before creating new files.
- Questions only via ask_user_input_v0.
- Any visual proposal: ASCII mockup with legend and description. Approval BEFORE coding. The user was angry twice about unapproved designs.
- Short direct replies, no filler, no sugar-coating. Failure reports: discrete, factual.
- Non-trivial scripts: extensive error reporting, log file starting in %TEMP% copied to %SOURCE_DIR%\logs, progress bar, drive-space fallback for large moves.
- Phase-plan output format: Status / Findings / Task Skeleton / End Goal / Plan / Execution.
- Avoid innovation. Do the task at hand. If the conversation drifts, ask whether to switch topic.
- Before complex tasks: Task Skeleton, detailed End Goal, Core Requirements. Confidence below 97% means ask or read more code.

## 2. MASTER TABLE (order of execution)

| ID | Task | Status | Section |
|---|---|---|---|
| V0 | Baseline verify and repo hygiene | [ ] | 4 |
| D6 | Side-expand folder open mode + home setting | [~] | 5 |
| S4 | Built-in widget elements for the stack | [~] | 6 |
| S5 | Web templates + optimize detection | [~] | 7 |
| DS | default_settings rework | [?] | 8 |
| WA | Web App manage button | [?] | 9 |
| VP | VPN phases 2 to 4 | [!] undefined | 10 |
| BL | Carry-over backlog from earlier plans | [?] | 11 |

Done per handoffs and repo inspection: P1-P6, S1-S3, D0-D5 (D4a-c and D5 found in code: HomeFavoriteRows, HomeBookmarkRows, HomeFolderThreshold, HomeSearchMode, PnlSearchMode, ExpandStackFor). Compile and behavior of D1-D5 are UNCONFIRMED by the user.

## 3. APPROVED DECISIONS LOG

- Homepage design: HORIZON_HANDOFF.md section 5 (3 columns, stack in right column, tri-switch, drawer folder cards, card-less opened folder, popup default, side-expand later).
- D6: second folder opens by REPLACING the first. Layout "Mockup A" for one open folder was approved in chat, but the mockup text was not stored. D6-A0 re-renders it for storage.
- Home settings pattern: new setting = SettingsService property + default_settings.txt entry (next S05.xx) + combo in HomePageSettingsWindow following CmbFolderThreshold / FolderThresholdValues.
- Row icons clamp(floor(width / cell), MinRowIcons 3, MaxRowIcons 6); folder fallback threshold default 30%.
- default_settings.txt: user's saved config.json wins; Reset button resets ALL settings; searchable IDs [Sxx.yy].
- DebugLogging mode toggled from DEVELOPER settings tab, default off.
- Marquee: shared Controls/MarqueeText.cs, tab-hover timing and easing, travel scaled to text and container width.
- Fullscreen: no edge-hover reveal of header/sidebar; MainWindow title "Horizon Browser".

## 4. V0 BASELINE VERIFY AND REPO HYGIENE

Status: Not started.
Findings:
- default_settings.txt line 37 is corrupted: `@AppIcon=src\icons\IMG_20260930_110456.ico@AppIcon=src\IMG_20260930_110456.ico`. Build_Release.bat parses `@AppIcon`, so the icon value is malformed. Matches the earlier "app icon not taking effect" complaint. Confidence high.
- Stray files in repo root: MainWindow.xaml.cs.bak, emergency_log_failure.txt, cookies.json, history.json, dir, Output. Report only; do not delete.
Task Skeleton: Phase 1 confirmations. Phase 2 hygiene fix.
End Goal: Known-good baseline before new features.
Plan:
- [ ] V0-1 ASK: did D1-D5 compile and work? Paste errors if not.
- [ ] V0-2 Fix line 37 of default_settings.txt (single valid @AppIcon entry; ASK which icon path is intended).
- [ ] V0-3 ASK: are cookies.json, history.json, MainWindow.xaml.cs.bak meant to be tracked in the repo? Report only.
Execution: One step at a time, V0-1 to V0-3.

## 5. D6 SIDE-EXPAND OPEN MODE

Status: Investigation done, no code. Popup mode exists.
Findings:
- DrawerFolder_Requested (HomePageView.xaml.cs) builds OpenFolderView and calls HomeStack.ShowOverlay. ShowOverlay blurs Incoming, adds radial dim, 300 ms SineEase.
- OpenFolderView(group) is reusable: card-less, paged, raises CloseRequested and ItemActivated.
- Layout columns: ColClockWeather (Auto), ColSearchArea (*), ColRightBalance (Auto). HomeStack in column 2: SetRestSize, ExpandTo, Collapse.
- ColRightBalance.Width mirrors the left column width (about lines 446-486) and the stack is hidden when ColRightBalance.ActualWidth < 260. Animating columns can fight this. Confidence medium.
- No open-mode setting exists.
- Geometry source: HORIZON_HANDOFF.md section 5.5: center shifts left, left column pushed away, folder fills the right. Confidence medium until D6-A0 is approved.
Task Skeleton: A setting + routing. B side-expand layout animation. C folder hosting, replace, close. D edge cases. E roadmap update.
End Goal: Home settings offers folder open mode Popup (default, unchanged) or Side-expand. Side-expand: center animates left, left column pushed away, folder fills the right area with no blur or dim, second folder replaces the first, close (Esc, click outside, close button) reverses the animation.
Plan:
- [ ] D6-A0 ASK: ASCII mockup (one open folder, then second folder replacing it) with legend; get approval; store it in section 3.
- [ ] D6-A1 SettingsService.HomeFolderOpenMode (Popup|SideExpand), default Popup.
- [ ] D6-A2 default_settings.txt entry, next free ID after S05.27 (verify numbering); update the S05 count in the INDEX.
- [ ] D6-A3 Combo in HomePageSettingsWindow (xaml + cs), CmbFolderThreshold pattern.
- [ ] D6-A4 Branch in DrawerFolder_Requested by mode; SideExpand calls OpenFolderSide(group).
- [ ] D6-B1 Animated side-expand state in HomePageView: left fades or slides out, center shifts left, right widens. 300 ms SineEase, header widening animation style.
- [ ] D6-B2 Reconcile with the ColRightBalance mirror logic and the 260 visibility rule; animate a layout offset instead of column definitions if they conflict.
- [ ] D6-B3 Reverse animation restores exact prior widths.
- [ ] D6-C1 Host OpenFolderView in the widened right area, no overlay backdrop.
- [ ] D6-C2 Second folder replaces the first with a cross-fade; no stacking.
- [ ] D6-C3 Close paths: Esc, click outside, close button; ItemActivated closes then navigates.
- [ ] D6-C4 Esc handling independent of Overlay focus.
- [ ] D6-D1 Window resize while open: relayout and repaginate.
- [ ] D6-D2 Stack hidden (width < 260): fall back to Popup.
- [ ] D6-D3 Interplay with ExpandedChanged, _favoritesExpanded / _bookmarksExpanded, wheel passthrough, setting change while open.
- [ ] D6-E1 Mark D6 done in both handoff roadmap tables and in this file (find/replace).
Execution: One step at a time, D6-A0 to D6-E1. Compile pause after A4, B3, C4, D3.

## 6. S4 BUILT-IN WIDGET ELEMENTS

Status: [?] Catalog in Services/StackElementStore.cs lists cpu, ram, notifications, notes, calculator, converter. Controls/StackElementBuilders.cs registers factories for all six. The handoff's list of existing browser widgets is larger.
Findings:
- Handoff 5.7 allows any browser widget as an element: clock, media, calendar, weather, battery, VPN/AdBlock. Only the six above are in the catalog. Confidence high.
- RegisterSource calls for notes, calculator, converter appear twice in StackElementBuilders.cs (lines about 344-352 and 452-460). Possible duplicate block; verify before touching.
- Notes, calculator, converter use BuildOpenCard with a null action: likely placeholders. Verify they open real tools.
Task Skeleton: Phase A verify. Phase B missing widgets. Phase C real actions.
End Goal: Every built-in browser widget is addable as a stack element with live or actionable content, and removable and editable like other elements.
Plan:
- [ ] S4-1 Verify: list catalog vs factories; confirm duplicates; ASK whether the six work in the running build.
- [ ] S4-2 Remove the duplicate registration block if confirmed.
- [ ] S4-3 ASK: which widgets to add (clock, media, calendar, weather, battery, VPN/AdBlock) and per-element options.
- [ ] S4-4 Add chosen widgets (catalog entry + factory), one widget per step, compile pause after each.
- [ ] S4-5 Wire notes, calculator, converter cards to their real tools.
- [ ] S4-6 Respect performance rules: no live WebView2 for widgets; suspend hidden elements.
Execution: One step at a time.

## 7. S5 WEB TEMPLATES AND OPTIMIZE DETECTION

Status: [?] 19 templates with profiles (zoom, mobile UA, dark, hide CSS) and a host DetectionMap exist; StackElementWizardWindow calls DetectTemplate on the URL box.
Findings:
- Handoff 5.7 wants an "optimize for Mail?" Yes/No prompt, dismissible. The wizard calls DetectTemplate at one site; whether it shows a prompt is unverified.
- Profiles cover zoom, UA, dark preference, hide CSS. Not covered by profile: refresh interval, unread badge, user CSS. Verify against the wizard step 3 options.
- Torrent & P2P is a layout template with no preloaded site lists (rule): confirmed in profile (hide: false), no sites in DetectionMap. Keep.
Task Skeleton: Phase A verify detection UX. Phase B missing options. Phase C coverage.
End Goal: Wizard offers dismissible optimize prompts, templates apply compact layout, hide CSS, dark theme, zoom, refresh interval, optional unread badge; users may add CSS but not JS.
Plan:
- [ ] S5-1 Verify: wizard flow, prompt presence, dismiss behavior.
- [ ] S5-2 ASK: refresh interval and unread badge scope (which templates).
- [ ] S5-3 Refresh interval option (data model + wizard + runtime).
- [ ] S5-4 Unread badge for Mail and Chat templates (built-in only).
- [ ] S5-5 User CSS field (no JS).
- [ ] S5-6 Detection map review (missing hosts, false positives like youtube.com vs music.youtube.com ordering).
- [ ] S5-7 Performance: one live WebView2 per visible element, cap simultaneous live views.
Execution: One step at a time.

## 8. DS DEFAULT_SETTINGS REWORK

Status: [?] Largely present. default_settings.txt has a header, [Sxx] index, 128 searchable entries, build-time @ keys. Services/DefaultSettingsService.cs exists. SettingsWindow has BtnResetAll_Click calling SettingsService.ResetToDefaults(wipe).
Findings:
- Decisions already made: saved config.json wins; Reset resets everything; searchable IDs; current settings first.
- Open from the original complaint: startup video and app icon not taking effect. Cause candidate: corrupted line 37 (see V0-2). Confidence medium.
- Index counts (S05 = 21) are already stale after D4/D5/D6 additions; every new entry must update the INDEX.
Task Skeleton: Phase A verify. Phase B startup video/icon. Phase C coverage and counts.
End Goal: Every exposed setting editable from one searchable file with correct counts; build-time icon and startup video work; Reset covers header, sidebar, homepage.
Plan:
- [ ] DS-1 Verify: parse test of every Key=Value line against SettingsService properties (list unmatched keys and unexposed properties).
- [ ] DS-2 Fix @AppIcon / @StartupVideo handling end to end (after V0-2); read Build_Release.bat first.
- [ ] DS-3 Correct INDEX counts.
- [ ] DS-4 Expose missing properties found in DS-1 (ASK before large batches).
- [ ] DS-5 Verify Reset button covers header, sidebar, homepage.
Execution: One step at a time.

## 9. WA WEB APP MANAGE BUTTON

Status: [?] BtnInstallWebApp_Click switches to ShowWebAppManageMenu when _matchedWebApp is set; tooltip "Manage Web App: name"; menu actions in MainWindow.xaml.cs about lines 7187-7348 (Open, Rename, desktop and Start Menu shortcut, taskbar pin, update icon, open folder, reset data, uninstall).
Findings:
- Earlier plan Phase B: header manage button with dropdown. Implementation appears present in code-behind. Whether the header button and dropdown look and behave as planned is unverified.
- Earlier plan Phase A: webapp bar trigger, fullscreen taskbar, window title/icon. Verify separately in BL.
Task Skeleton: Phase A verify. Phase B gaps.
End Goal: Header button shows install or manage by matched web app, with a dropdown covering all management actions.
Plan:
- [ ] WA-1 ASK: what is still missing or broken in the manage button?
- [ ] WA-2 Fix reported gaps, one per step.
Execution: One step at a time.

## 10. VP VPN PHASES 2 TO 4

Status: [!] Phase definitions are not stored anywhere in the repo or handoffs.
Findings:
- Present: Services/VpnRelayService.cs (local relay, Connect/Disconnect/Test), VpnProfileStore.cs, VpnThroughputHistoryService.cs, Views/VpnControlCenterWindow (profiles, quick connect, test). Settings S08: VpnEnabled, VpnAutoConnect, VpnActiveProfileId, VpnKillSwitchEnabled, VpnNotifyOnFallback, VpnBlockWebRtcLeak, VpnBypassList, VpnWizardCompleted. Whether each setting is wired is unverified.
- Home widget setting HomeShowVpnAdBlockWidget exists.
Task Skeleton: Phase A define. Phase B verify S08 wiring. Phase C implement defined phases.
End Goal: As defined by the user in VP-1.
Plan:
- [ ] VP-1 ASK: scope of phases 2, 3, 4 (user supplies, or approves a proposal built from the S08 settings and relay code).
- [ ] VP-2 Verify S08 settings wiring; list unwired ones.
- [ ] VP-3 Write the three phases into this section with IDs (VP2-x, VP3-x, VP4-x) and get approval.
- [ ] VP-4 Execute approved phases, one step at a time.
Execution: One step at a time. No code before VP-3 approval.

## 11. BL CARRY-OVER BACKLOG (from earlier planning; statuses unverified)

Each item: verify in code first, then ASK the user whether it is still open.
- [ ] BL-1 Weather widget redesign: wide two-pane window; Overcast/hourly right pane hidden by default, slides from the right edge of the main window; tabs Now, 7d, 16d, Month, Year unchanged; layout adapts to space with a manual compact/wide override in settings; blurred, tinted, darkened wallpaper background. Needs the widget source files.
- [ ] BL-2 Homepage/YT Music: tab named "Home"; homepage URL shows horizon://home; reset-to-default-homepage option in settings; header and sidebar coloring while YT Music plays; YT Music title in header media widget and tab hover mode; header and homepage media marquee identical to tab hover marquee (Controls/MarqueeText.cs exists); header widget widens first (weather-widget style) and starts marquee only when width would collide with tabs.
- [ ] BL-3 Phase A: fullscreen taskbar, window title/icon, webapp bar trigger.
- [ ] BL-4 Phase C: DebugLogging mode from DEVELOPER settings tab (default off).
- [ ] BL-5 Phase D: fullscreen enter/exit fade animation in header-slide style (MainWindow.FullscreenTransition.cs exists).
- [ ] BL-6 Phase E: homepage audio visualizer trimmed to smooth, dense, faded, blurred, pulsing arcs only in the 4 corners, no overflow.
- [ ] BL-7 WebView2 crash recovery: black-screen tabs after session restore (AlohaFind variant); BrowserView full-replacement (Option B) pending crash log confirmation.
- [ ] BL-8 Session restore popup failure after extension-triggered process restart (ShowSessionRestore / LastSessionUrls serialization).
- [ ] BL-9 WinUI 3 migration Phase 5 (FluxSidebar, MainWindow.Widgets.cs): repo is still WPF; ASK whether this track is active.
- [ ] BL-10 Known limits to revisit: HomeGroupStyle change does not restyle collapsed groups until rebuild; expand target height measured at unlimited width; no virtualization (thousands of tiles untested); saved settings keep old HomeGroupStyle.
- [ ] BL-11 If new tasks emerge, append them here with an ID; never reorder done items.

## 12. HOW TO UPDATE THIS FILE

After each step, output find/replace patches:
- Tick: Find `- [ ] D6-A1 ` Replace `- [x] D6-A1 `.
- Pointer: Find `CURRENT POINTER: V0-1` Replace `CURRENT POINTER: <next step>`.
- Task status in section 2: Find `| D6 | Side-expand folder open mode + home setting | [ ] |` Replace with `[~]` or `[x]`.
Every ID is unique, so each Find matches exactly once.
