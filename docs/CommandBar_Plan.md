# Horizon Browser: Command Bar Improvement Plan

Handoff document. Written so any AI agent (or human) can execute one phase without the rest of the conversation.

## 0. Context

- Repo: MoRDekai1364/Horizon_Browser, branch `Stable_pre-release`, verified at commit `2a7480a` plus uncommitted restyle changes (see Phase 0).
- Stack: WPF, .NET 8, WebView2, namespace `Horizon.Stealth`.
- Feature: the Ctrl+T "Command Bar".
  - Window: `Views/CommandBarWindow.xaml` and `.xaml.cs`
  - Hotkey: `MainWindow.xaml.cs`, `if (ctrl && e.Key == Key.T)` calls `CommandBarWindow.ShowCommandBar(this)`
  - Shortcut data and execution: `Services/CustomShortcutService.cs` (stored in `custom_shortcuts.json` under `ConfigService.UserDataRoot`)
  - Browser commands: `MainWindow.Commands.cs`, method `ExecuteBrowserCommand(string)`
  - Global hotkeys: `CustomShortcutService.TryExecuteHotkey`, called from `MainWindow.xaml.cs` key handler
- `MainWindow.xaml.cs` is roughly 47,000 lines. Do not add large blocks to it. Create new partial class files (like `MainWindow.Commands.cs`) instead.

## 1. Verified findings (what exists today)

| Item | State |
|---|---|
| Open URL / `localhost` | Works, opens a new tab |
| Web search | Works, but hardcoded to Google (`google.com/search?q=`), ignores the user's search engine setting |
| Switch to open tab | Works, substring match on title/URL, capped around 15 |
| Built-in `.new` links, 4 scripts | Work |
| Browser commands: toggle_sidebar, toggle_mini_mode, screenshot, open_notes, open_timer, mute_tab | Work |
| `split_view` ("Split View (Dual Tab)") | **Does nothing.** Shortcut exists, but `ExecuteBrowserCommand` has no `split_view` case and no split-view code exists in the project |
| `new_tab`, `close_tab`, `reopen_closed_tab`, `open_utilities` | Implemented in `ExecuteBrowserCommand` but not exposed as shortcuts |
| Custom shortcuts | Can be added (URL template or JS) via the in-bar form. No edit, disable or delete UI. `UpdateShortcut` and `DeleteShortcut` exist in the service but nothing calls them |
| Matching | Substring only, no scoring or fuzzy match |
| Click behavior | Double-click or Enter runs an item; single click only selects |
| Search engine setting | `SettingsService.Current.SearchEngine` and `BuiltInSearchEngines` (entries have `{query}` URL templates) |

## 2. End goal

A command bar that is fast to use by keyboard, shows only commands that work, covers the browser's common actions, lets the user manage their own shortcuts, and offers a working split view.

Acceptable deviations: split view may ship as a minimal version first; fuzzy matching may be a simple scored subsequence match rather than a library.

## 3. Core requirements (all phases)

- Behavior must follow existing app settings (search engine, `DisableBlurEffects`, theme).
- Light and dark theme must both look correct. Use `DynamicResource` theme brushes (`Brush_Text`, `Brush_MutedText`, `Brush_SurfaceChrome`, `Brush_SurfaceField`, `Brush_Accent`, and so on), not hardcoded colors.
- Existing `custom_shortcuts.json` files must keep loading. Any schema change must be additive.
- Do not break the Ctrl+Tab switcher (`Views/TabSwitcherWindow*.cs`); the command bar reuses `TabSwitcherWindow.CaptureScreenArea`.
- Build must pass after each phase. Run a build and fix errors before reporting done.
- Keep each phase small enough to review as one diff.

## 4. Agent working rules

These apply to whichever agent runs a phase:

- Read the files listed under "Read first" before editing.
- Work only on the stated branch. Do not push.
- You may build, edit files directly and add comments where they help. Match the surrounding code style.
- Do not touch files outside the phase's "Files" list without noting it in the report.
- If a finding in section 1 turns out to be wrong, stop and report it before continuing.
- Report format when done: files changed, what was built or tested, anything not verified, open questions.

Running several agents: phases touch different files except where noted. Phases 1, 2 and 3 all touch `CommandBarWindow.xaml.cs` or `CustomShortcutService.cs`. Run them in order, or merge between them. Phase 4 is independent of 1 to 3 except the command registration step.

## 5. Phases

### Phase 0: Baseline

Goal: a known-good starting point.

- Apply the pending restyle (already produced): `CommandBarWindow.xaml`, `CommandBarWindow.xaml.cs`, and in `TabSwitcherWindow.Grid.cs` change `private static BitmapSource? CaptureScreenArea` to `internal static`.
- Build, run, open Ctrl+T in Dark, Light and Redmond themes, with and without `DisableBlurEffects`.
- Check: panel is centered, draggable by the search bar and bottom bar, blurred backdrop follows drag, panel closes on focus loss.
- Commit locally.

Done when: builds, all three themes legible, drag works. Note that `Brush_Accent` is grey `#606060`; report whether accent text is readable.

### Phase 1: Truthful and complete command list

Goal: every listed command works; useful existing commands are exposed.

Files: `Services/CustomShortcutService.cs`, `MainWindow.Commands.cs`.

- Remove or hide the `cmd_split_view` entry until Phase 4. Keep its Id reserved. `EnsureBuiltIns` re-adds built-ins that are missing by Id/title, so the removal must be done in `GetDefaultShortcuts` and any already-saved `custom_shortcuts.json` entry with that Id must be filtered out or marked disabled, otherwise it persists.
- Add built-in shortcuts for the existing commands: New Tab, Close Tab, Reopen Closed Tab, Open Utilities.
- Add useful new commands only if the underlying action already exists in the app (check before adding): history, downloads, settings, bookmarks.
- Make "Mute / Unmute" subtitle reflect state if simple (`CurrentTab.IsMuted`).

Done when: every entry in an empty-query list does something visible; split view entry gone; no duplicates after restart.

### Phase 2: Search, ranking, and click behavior

Goal: typing finds the right thing fast.

Files: `Views/CommandBarWindow.xaml.cs` (and `.xaml` for single-click).

- Replace the hardcoded Google search with the user's configured engine: read `SettingsService.Current.SearchEngine`, resolve its `{query}` template from `BuiltInSearchEngines` plus user-added engines (find where custom engines are stored). Fix the subtitle text accordingly.
- Add scoring: exact prefix > word prefix > subsequence > substring. Sort by score. Apply to tabs and shortcuts (title, keywords, category).
- Empty query ordering: commands first (or a short "recent" list), tabs after. Decide and document.
- Group results with small headers or badges (Tabs / Commands / Scripts). Keep keyboard navigation skipping headers.
- Single click runs the item (or add a setting if you want to keep double-click).
- Raise the 15-item tab cap or make it scroll properly.

Done when: `dm` finds Dark Invert, tab switching by typing needs no extra arrow key, search opens the configured engine.

### Phase 3: Shortcut management and validation

Goal: the user can personalize without editing JSON.

Files: `Views/CommandBarWindow.xaml(.cs)` (or a new `Views/ShortcutManagerWindow` if cleaner, approval needed for new files), `Services/CustomShortcutService.cs`.

- Manage view listing all shortcuts: edit, enable/disable, delete (custom only; built-ins can only be disabled, using `IsEnabled`).
- Persist built-in disable state in a way `EnsureBuiltIns` does not undo (it currently re-adds missing built-ins by Id/title; a disabled built-in must stay present).
- Hotkey field: validate with the same parser as `MatchesGesture`, show conflicts with existing shortcuts and known app hotkeys (Ctrl+T, Ctrl+W, Ctrl+Tab, Ctrl+Shift+T, and so on).
- Allow the custom form to create `BrowserCommand` actions from a dropdown of known commands.
- URL templates: show the available placeholders; if no `{query}` is present, do not silently drop typed text (either append or warn).

Done when: user can create, edit, disable and delete shortcuts from the UI; invalid or conflicting hotkeys are rejected with a clear message; JSON still loads.

### Phase 4: Split view (largest phase)

Goal: two tabs visible side by side. Treat as its own mini-project with sub-steps. Nothing exists today.

4a. Design spike (no feature code):
- Document how `MainWindow` hosts the active tab's `BrowserView`/WebView2 (find the content host in `MainWindow.xaml` and how `CurrentBrowser` swaps views on tab selection).
- Identify what breaks if two WebView2 controls are visible at once: focus routing, `IsVisibleChanged` deferred init (past crash on collapsed visibility), tab sleeping, media playback state machine, the Win32 Ctrl detection used by the switcher.
- Propose the layout approach (second host column with a draggable splitter) and a list of risks. Get approval before 4b.

4b. Minimal version (new file `MainWindow.SplitView.cs`, partial class):
- Command `split_view`: choose a second tab (reuse the command bar tab list or the active tab list), show both side by side with a divider.
- Command to close split view. Closing either tab, or switching tabs, must leave a consistent state.
- Wire `split_view` into `ExecuteBrowserCommand` and re-enable the shortcut from Phase 1.

4c. Polish:
- Draggable divider with minimum widths, remember ratio.
- Focus indicator for the active pane.
- Behavior with sleeping tabs, fullscreen (F11), and window resize.

Done when: split opens and closes without crashes, both panes load and are interactive, tab close/switch while split behaves, and a regression pass of Phase 0 checks passes.

### Phase 5: Verification

- Smoke checklist: open bar, each command type, theme switch, drag, restart persistence.
- Check `custom_shortcuts.json` migration from an older file.
- Re-test Ctrl+Tab switcher after any change to `TabSwitcherWindow.Grid.cs`.
- Remove the unused `ApplyBlur`, `IslandAccentPolicy` and DllImports in `CommandBarWindow.xaml.cs` if the user approves.

## 6. Known risks

- Brush_Accent is grey; accent-colored text may be hard to read on the dark card.
- The snapshot backdrop is taken from the main window's screen; multi-monitor and mixed-DPI alignment is untested.
- `EnsureBuiltIns` re-adds missing built-ins, so removing or disabling one must account for it.
- `MainWindow.xaml.cs` size makes edits there risky; prefer partial files.
