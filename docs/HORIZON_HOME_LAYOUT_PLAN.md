# HORIZON BROWSER - HOMEPAGE LAYOUT PLAN (HANDOFF)

Repo: MoRDekai1364/Horizon_Browser
Branch: new_changes ONLY. Never touch official_release or Stable_pre-release. Never push. Output edits only.
Stack: WPF, .NET 8, WebView2. Windows only.
Owner: MoRDekai1364. Owner compiles and tests. Do not try to compile.

---

## 0. HARD RULES

1. No comments in any generated or edited code.
2. Output format for every code change, one block per change:

   New text box:
   New/Existing file: [New/Existing]
   File name: `filename`
   New change / Modify existing: [New change/Modify existing]
   New text box:
   Find:
   exact code to search
   New text box:
   Replace:
   exact replacement code

3. Find strings must match the file exactly (whitespace included) and be unique. Apply order must be stated when changes depend on each other.
4. File names must exactly match the repo. Ask before creating any new file.
5. Do not reverse the owner's recent layout upgrades. The current homepage structure is the baseline. Extend it, do not revert it.
6. No hardcoded values for one resolution. The browser runs on phone-sized, tablet, square and widescreen windows. Every size, row count and threshold must be derived from the live window size, DPI-independent units and measured element sizes.
7. Do not innovate beyond the listed issues. One phase at a time. Wait for the owner's compile/test report before the next phase.
8. Tone of owner communication: short, direct, no filler, no sugar-coating. Failures reported as plain status.
9. Non-trivial scripts need error reporting, a log file and a progress bar. Not expected for these UI edits.
10. Before each phase state: current scenario, whether confidence is above 97 percent, what information is missing. Ask for missing files by repo path.

---

## 1. TASK SKELETON

- Phase 0: Recon, no edits
- Phase 1: Global bottom boundary for all homepage elements
- Phase 2: Center column icon reduction (capped count, existing order, adaptive rows)
- Phase 3: Bottom edge treatment of center and right columns (rounded corners, no "endless" look)
- Phase 4: Search bar state transition animation
- Phase 5: Multi-resolution verification
- Done in advance: malformed AppIcon entry in `default_settings.txt` (see section 6)

## 2. END GOAL

A homepage that adapts to any window size where:

- every homepage element stays above one shared bottom boundary
- the central column shows only a capped number of icons (existing order), in a small adaptive number of rows
- the bulk of icons lives in the right column (StackHost), unchanged in behavior
- the search bar stays at the vertical center, and moves up only as a last-effort fallback by the minimum needed
- the bottom ends of the center and right columns have rounded corners like their top ends and handle widgets cleanly
- switching search bar modes uses the same widening animation as the header widget

Allowed deviations: threshold values may be tuned after owner testing. Behavior of unrelated features must not change.

## 3. CORE REQUIREMENTS

R1. Bottom boundary B: the maximum bottom line for ALL homepage elements equals twice the horizontal distance between the "Change Wallpaper" button and the "VPN off" button, applied vertically, measured upward from those buttons. Measure the gap at runtime from the real elements. Do not hardcode it.
R2. Center column shows only a capped number of icons, taken in the existing item order. Row count adapts to available height. The search bar stays at the vertical center of the page. It may be pushed up only as a last-effort fallback, when even the minimum icon rows cannot fit between the search bar and boundary B. Push as little as possible, never beyond the necessary amount.
R3. Overflow icons go to the right column (StackHost) as already handled today.
R4. Bottom edges of the center column island and the right column look like the top edges: rounded, clipped, finite.
R5. Widgets in the lower parts of the center and right columns are laid out and clipped correctly at every size.
R6. Search bar mode transitions reuse the widen animation logic of the header widget.
R7. Icon background overflow in the central column (known from the old release branch) must not occur.
R8. Works from phone-sized to widescreen windows with no fixed-resolution assumptions.

---

## 4. KEY FILES AND ANCHORS (verify before editing)

- `Views/HomePageView.xaml`
  - `GridHomeLayout` (root layout grid)
  - `PnlSearchArea` (center column, Grid.Column 1, spans rows 0-1, centered)
  - `GridFavBookmarksIsland` (favorites and bookmarks island in the center column)
  - `ScvFavorites` (MaxHeight 320), `IcnColumns`
  - `ScvBookmarks` (MaxHeight 220), `IcnBookmarks`
  - `HomeStack` (`controls:StackHost`, right column, Grid.Column 2)
  - bottom-right StackPanel (Grid.Row 1, bottom aligned, margin 0,0,16,16) holding `PnlVpnAdBlock` and `BtnChangeWallpaper`. This is the reference for boundary B.
- `Views/HomePageView.xaml.cs`
  - `UpdateSearchAreaMaxHeight()`, `ResolvedRows()`, `CenterPanelWidth()`, `RowCapacity(int)`, `DumpHomeLayout()`
  - `SetSearchMode(string mode, bool focus)` near line 2677
  - search box sizing near lines 150, 272, 451, 495 (`SearchBoxBorder.MaxWidth`)
  - `AnimateInteractiveContent(bool show)` near line 635
  - folder fallback fields: `_favoritesFallback`, `_bookmarksFallback`, `_fallbackTimer`, `ApplyFallbackBudget`
- `Controls/PuzzlePanel.cs`
  - `HomeRowLimits` (MinTotalRows, MaxTotalRows, RowHeight, ReservedHeight, Ceiling, Resolve). Current constants are tuned for one layout and need to become measurement-driven.
  - `RowsPanel` (ColumnsFor, MaxColumns, CellWidth), `FolderFallback`
- `Controls/StackElementBuilders.cs`, `Services/StackElementStore.cs`, StackHost control: right column content and widgets
- `MainWindow.xaml.cs` near lines 12464-12510: header widget hover-widen logic (Weather and Media/Music modes). This is the animation source for Phase 4.
- `Services/SettingsService.cs`: `HomeFavoriteRows`, `HomeBookmarkRows`
- `default_settings.txt`: `HomeFavoriteRows`, `HomeBookmarkRows`, `@AppIcon`

Debug aid already present: Ctrl+Shift+D copies a layout dump (sizes, visibility, rows, columns, ceiling) to the clipboard. Ask the owner to paste the dump at different window sizes to verify.

Reference: `official_release` branch has an older homepage whose icon items interact mostly correctly, with a small icon background overflow in the central column. Use it as inspiration for item interaction only. Do not copy its layout.

---

## 5. PHASES

### Phase 0 - Recon (no edits)

Goal: confirm all facts before any change.
Tasks:
1. Read the files in section 4. Request any missing file by repo path.
2. Determine how the bottom ends of the center island and the right column are currently clipped (Clip, CornerRadius, ClipToBounds, ScrollViewer MaxHeight, panel stretch). Identify why they look "endless".
3. Determine how items are ordered today for favorites and bookmarks and how the right column (StackHost) receives the overflow. No usage counters are to be added. Ranking is the existing order.
4. Locate the exact header widget widen animation (duration, easing, properties animated) and how it is triggered.
5. Report: current scenario, findings, confidence, and proposed Phase 1 changes.
Acceptance: written findings only.

### Phase 1 - Global bottom boundary (foundation)

Goal: one shared boundary B that every homepage element respects.
Tasks:
1. Measure the horizontal gap between `BtnChangeWallpaper` and `PnlVpnAdBlock` at runtime. Compute B = 2 x gap.
2. Define the boundary line: B above the top edge of that bottom-right button row.
3. Expose it as a single computed value used by the center column, right column and any other homepage element.
4. Apply it as a MaxHeight or bottom margin budget on `PnlSearchArea`, `HomeStack` and anything else on the homepage. Extend the existing `UpdateSearchAreaMaxHeight()` approach instead of adding parallel logic.
5. Recompute on size change, DPI change and when the VPN button visibility changes (it can be collapsed).
Acceptance: with the dump hotkey, no element's bottom edge is below the boundary at any window size. Nothing is cut off. Layout upgrades preserved.

### Phase 2 - Center column: capped icons, search bar stays centered

Goal: the central column holds a small capped set of icons in existing order, the rest live in the right column. The search bar stays at the vertical center.
Tasks:
1. Show only the first N items in the center, in existing order. N equals the number of cells that fit.
2. Row count and column count derive from available width and height under boundary B. Replace the fixed `RowHeight` and `ReservedHeight` constants in `HomeRowLimits` with measured values.
3. Order of reduction when space is short: first reduce icon rows (down to the minimum), then, only if the minimum rows still do not fit between the search bar and boundary B, move the search bar up by the smallest amount needed. Pushing the search bar is the last fallback.
4. Items not shown in the center remain available in the right column exactly as that column handles icons today.
5. Fix the icon background overflow in the central column. Clip icon backgrounds to their cell bounds and consider the rounded corners.
6. Keep `HomeFavoriteRows` and `HomeBookmarkRows` settings working as upper limits.
Acceptance: at phone-sized, tablet, square and widescreen sizes the center shows few icons, adaptive rows, no overflow, search bar at the vertical center whenever space allows and moved up only as a last resort.

### Phase 3 - Bottom edges of center and right columns

Goal: bottoms look like the tops: rounded and finite.
Tasks:
1. Apply a rounded clip to the center island and to the right column container so the bottom corners match the top corners. Use the same CornerRadius as the top parts.
2. Make the right column height finite and bounded by B. Content that does not fit scrolls or collapses inside the clip, it must not run past the bottom.
3. Make widgets in the lower parts of both columns respect the clip and the boundary at every size.
4. Check the left column too and apply the same treatment if it shows the same symptom.
Acceptance: no "endless" bottoms. Rounded bottom corners visible. Widgets fully inside the container at all sizes.

### Phase 4 - Search bar state transition

Goal: switching search bar states uses the widen animation of the header widget.
Tasks:
1. Reuse the header widget's widen animation (duration, easing, animated properties) for `SetSearchMode` transitions. Share the logic if practical, otherwise mirror its parameters exactly.
2. Animate width changes of the search bar between states. Respect `SearchBoxBorder.MaxWidth` rules for each window size.
3. Do not break focus handling or the inactivity logic (`_inactivityTimer`, `AnimateInteractiveContent`).
Acceptance: mode changes visibly animate with the same feel as the header widget widening.

### Phase 5 - Multi-resolution verification

Goal: confirm all behavior across sizes.
Tasks:
1. Ask the owner to test and paste Ctrl+Shift+D dumps at: phone-sized, tablet, square, widescreen windows, plus window resize while running.
2. Check each requirement R1-R8 against the dumps and screenshots.
3. Fix only regressions in the listed requirements.
Acceptance: all requirements hold at every tested size.

---

## 6. ALREADY DONE (IN CHAT)

Malformed AppIcon line in `default_settings.txt` (line 37) contained two entries glued together. Correct line:

`@AppIcon=src\icons\IMG_20260930_110456.ico`

The icon file exists at `src/icons/IMG_20260930_110456.ico`.

## 7. ASSUMPTIONS TO CONFIRM WITH OWNER

Confirmed by owner:
C1. The search bar is not pushed far past the vertical center, if at all. Pushing it is always the last-effort fallback after reducing icon rows.
C2. Boundary B = 2 x the horizontal gap between the wallpaper and VPN buttons, measured upward from the top edge of that button row.
C3. Icon ranking = existing order. Cap the count only. No usage counters.
