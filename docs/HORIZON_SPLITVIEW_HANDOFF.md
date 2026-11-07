# Horizon Browser: Split View Multi-Layout Rework (Handoff)

Repo: MoRDekai1364/Horizon_Browser, branch new_changes ONLY. No pushes. No GitHub credentials in this file; ask the user.
Owner does the compiling. Do not try to build.

## Working rules (from the user)
- Code changes only as Find/Replace blocks: file name, New/Existing, "New change / Modify existing", then exact Find text, then exact Replace text.
- No comments in generated or edited code.
- File names must match originals exactly. Ask before creating new files.
- Solve step by step, one phase at a time. Short, direct replies. Ask questions with the builtin tappable-question tool.
- Add error reporting and logging in non-trivial scripts. Avoid innovation beyond the task.
- Note: user files contain CRLF with blank lines between statements. Multi-line Find blocks failed once because of this. Prefer single-line Find anchors, and verify uniqueness.

## Status of earlier work this session (all confirmed working by user)
1. Ctrl+Shift+S: `cmd_split_view` gesture was missing in saved shortcuts; backfilled in `CustomShortcutService.EnsureBuiltIns`.
2. Auto-hide sidebar dead after opening a link from Home: cleared held `BeginAnimation(WidthProperty)` in `ToggleSidebar` (MainWindow.xaml.cs).
3. Homepage search bar in narrow mode: added `ApplySearchBoxMetrics(bool)` in HomePageView.xaml.cs.
These are not part of this task. Do not touch them.

## Current split view (as found)
- `MainWindow.SplitView.cs` (519 lines), partial class MainWindow.
- Exactly 2 panes. State: `_splitActive`, `_splitPicking`, `_splitOtherTab` (single TabViewModel), `_splitAwaitNewTab`.
- Key methods: `ToggleSplitView`, `EnterSplitView`, `ExitSplitView(keep)`, `SplitBuildLayout`, `SplitRemoveLayout`, `SplitSaveRatio`, `SplitAssignSecondary`, `SplitCreateNewSecondary`, `SplitViewPrepareSwitch`, `SplitViewOnTabClosing`, `SplitViewOwnsTab`, `SplitCandidates`, `SplitShowPicker`, `SplitBuildCard`, `SplitHookFocus`, `SplitUpdateIndicator`.
- Picker today: card grid titled "Choose a tab for this side" (`SplitShowPicker`), no drag and drop.
- Settings: `SplitViewPickMode` (1 = picker, 2 = auto), `SplitViewRatio` (0.5), in Services/SettingsService.cs near line 441.
- Command entry: Services/MainWindow.Commands.cs, cases "split_view" and "close_split_view". Gesture Ctrl+Shift+S defined in Services/CustomShortcutService.cs (cmd_split_view).

## Header tab drag (reference behaviour to replicate), MainWindow.xaml.cs
- `TabHeader_MouseMove` (about line 22401): drag starts after `SystemParameters.MinimumHorizontalDragDistance` / `MinimumVerticalDragDistance`; then `fe.CaptureMouse()`, then `CreateDragGhost(_draggedTab, pos)`.
- Ghost: a separate borderless topmost `Window` (`_dragGhost`, created in `CreateDragGhost` about line 22680, destroyed in `DestroyDragGhost` about line 22845). Positioned each move at cursor DIP + (10, -24) using `GetCursorScreenDip()`.
- Drop indicator in the tab lists: `TabDropGhostAdorner` (about line 22866), shown by `ShowTabDropGhost(list, slot)`, removed by `ClearTabDropGhost()`.
- Drop handled in `TabHeader_MouseUp` (about line 22560): computes insert position, reorders `_allTabs`, calls `DestroyDragGhost()`.

## Design constraints discovered
1. Mouse stays captured by the dragged tab header, so WPF drag events will not fire on panes. Drop targets must be found by screen-coordinate hit-testing against pane/slot rectangles.
2. Panes contain WebView2 (HWND airspace): WPF adorners cannot be drawn over a live pane. Pane drop highlights need a transparent topmost overlay Window (same technique as `_dragGhost`).
3. While the cursor is over the split area, the tab-list drop logic must be ignored; while over the tab list, pane drop logic must be ignored.
4. Tab ownership rules (`SplitViewOwnsTab`, `SplitViewPrepareSwitch`, `SplitViewOnTabClosing`) assume one secondary tab; they must generalise to N slots without changing 2-pane behaviour.

## End goal
Ctrl+Shift+S opens a Windows 11 style snap layout picker with thumbnails (user's screenshot reference). Layouts adapt to the monitor shape. The user drags a header tab (same ghost as header drag) onto a picker slot or onto a live pane to fill it. Live panes can be swapped by dragging with the same ghost. Panes have resizable dividers. Existing 2-pane behaviour must remain identical.

## Layout catalog (normalised rectangles, x y w h in 0..1)
Standard monitor (aspect about 16:9 to 16:10), user confirmed these are what his screen shows:
- L1 50/50: [0,0,.5,1] [.5,0,.5,1]
- L2 65/35: [0,0,.65,1] [.65,0,.35,1]
- L3 35/65: [0,0,.35,1] [.35,0,.65,1]
- L4 one left, two stacked right: [0,0,.5,1] [.5,0,.5,.5] [.5,.5,.5,.5]
- L5 two stacked left, one right: [0,0,.5,.5] [0,.5,.5,.5] [.5,0,.5,1]
- L6 2x2: four equal quadrants
Note: the user's screenshot shows duplicates (50/50 and "1 left + 2 right" each appear twice). The above distinct set is the assumption; the user had not yet confirmed it.
Ultrawide (aspect about 2.1 or more): add 3 equal columns, 25/50/25, and 3 columns with stacked ends.
Portrait (aspect below 1): stacked row variants (50/50 rows, 1 top + 2 bottom, 3 rows, 2x2).
Selection by aspect ratio of the monitor the window is currently on.

## Phases (one at a time, user tests after each)
P0 Analysis: DONE (this document).
P1 Data model, no UI change.
 - New file Services/SplitLayoutCatalog.cs (needs user approval): layout definition (id, slot rects, divider specs) and monitor-adaptive catalog.
 - MainWindow.SplitView.cs: replace single `_splitOtherTab` with a slot-to-tab map; keep a compatibility path so 2-pane L1 behaves exactly as now.
 - Test: existing 2-pane split still works.
P2 Layout renderer.
 - Build N panes from a definition with GridSplitters or equivalent, per-layout ratios saved (extend `SplitSaveRatio`; add settings field(s) only with user approval).
 - Test: apply each layout from a temporary entry point; resize panes.
P3 Snap-style picker.
 - Replace/demote the card picker. Thumbnail picker per screenshot, opened by Ctrl+Shift+S, monitor-adaptive. Click-to-fill fallback for slots.
 - Test: picker looks right, layouts apply.
P4 Drag and drop from header tabs.
 - Reuse `CreateDragGhost`/`DestroyDragGhost` (or extract to a shared helper if the user approves a new file).
 - Drop onto picker slots (WPF highlight) and onto live panes (overlay window highlight). Screen-coordinate hit-test.
 - Test: drag a tab into every layout.
P5 Pane swap and N-pane rules.
 - Drag grip on each pane's top edge, same ghost, swap slot contents.
 - Generalise close/switch/focus rules (`SplitViewOwnsTab`, `SplitViewPrepareSwitch`, `SplitViewOnTabClosing`, `SplitHookFocus`).
 - Test: swap, close tabs, switch tabs inside a split.
P6 Polish: Esc cancel everywhere, persistence across restarts, edge cases (closing the last tab of a pane, window resize, monitor change), settings.

## Open items needing the user
- Confirm the distinct standard layout set above.
- Approve new file Services/SplitLayoutCatalog.cs and any shared drag helper file.
- User said "hold, do not start yet" at the time of writing: wait for explicit go-ahead before P1.
