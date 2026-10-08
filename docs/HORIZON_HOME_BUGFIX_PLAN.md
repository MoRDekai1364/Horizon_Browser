# HORIZON BROWSER - HOMEPAGE BUGFIX PLAN (HANDOFF)

Repo: MoRDekai1364/Horizon_Browser
Branch: new_changes ONLY. Never push. Output edits only, in the find/replace format.
Owner compiles and tests. No code comments. Questions via the built-in question UI.
Rule: one phase at a time. Wait for the owner's compile/test report before the next phase.

## STATE SO FAR

Applied by owner (panel swap fix):
- HomePageView.xaml: GroupStyle Panel = PuzzleItemsPanel on IcnColumns and IcnBookmarks
- HomePageView.xaml: ItemsPanel = BlockItemsPanel on IcnColumns and IcnBookmarks
- HomePageView.xaml.cs: SetRowsPanel uses "RowsItemsPanel" : "BlockItemsPanel"
Result: layout looks better. Remaining glitches below.

## OPEN GLITCHES (from screenshots, not yet traced in code)

G1. Stray blurred rounded tiles over the wallpaper outside the center island (right edge) and small smudges elsewhere. Folder overlay icons render as blurred smears.
G2. Bookmarks scroller cuts folder cards with a flat rectangular edge at top and bottom, not rounded, not row-snapped. Large empty gap between folder rows. Island and right column bottoms are flat and square.
G3. Focus mode: container overlaps the search bar (cut off), ghost glass panel behind the first folder card, folder cards clip their own contents on the right, bottom row runs past the container and over the VPN button.
G4. Scroll chaining: wheel handling is split between WidgetContainer_PreviewMouseWheel on the containers and the nested ScrollViewers. Which one owns the wheel is unclear.

## PHASE 0 - Recon (no edits)

Read and report:
1. Services/HomeGlass*.cs and every use of services:HomeGlass (Exclude flag, how tiles are positioned, clipped and removed).
2. HomePageView.xaml.cs: WidgetContainer_PreviewMouseWheel, Widget_PreviewMouseLeftButtonDown, Widget_MouseMove.
3. HomePageView.xaml: HomeScrollViewerStyle, GridFavBookmarksIsland clip and CornerRadius, StackHost container clip.
4. Focus mode: _inFocusMode, focus enter and exit code, how PnlSearchArea and the widget containers are resized and moved.
Report: findings per glitch, confidence, proposed Phase 1 patch. Ask for missing files by repo path.
Acceptance: written findings only.

## PHASE 1 - Stray blur tiles (G1)

Goal: glass backdrop tiles exist only for icons that are visible inside their viewport.
Likely work: clip glass tiles to the scroll viewport and island bounds, remove or hide tiles for items scrolled out, hidden by AutoGridPanel HiddenOffset, or inside collapsed folders. Check folder overlay icon rendering.
Acceptance: no blurred tiles outside the island at any scroll position, while scrolling, while a folder is open or collapsing.
Test: owner scrolls the bookmarks fast, opens and closes folders, resizes the window, sends screenshots.

## PHASE 2 - Clipping and spacing (G2)

Goal: scroller edges look finished and rows do not leave gaps.
Likely work: rounded clip on the scroll viewport using the island CornerRadius, optional soft fade at the scroll edges, remove the extra gap between folder rows (measure and arrange of PuzzlePanel uniform mode), snap the scroll position to row pitch if the existing Snap logic allows.
Overlap with layout plan Phase 3 (bottom edges of center and right columns). Do the viewport clip here, the bottom corner treatment of the whole columns stays in layout Phase 3.
Acceptance: no flat mid-card cuts without a rounded or faded edge, no empty gaps between rows, no overflow of icon backgrounds in the central column.

## PHASE 3 - Scroll chaining (G4)

Goal: one clear owner for the mouse wheel at every point.
Rules to propose to the owner before coding:
1. Wheel over a widget with overflow scrolls that widget first.
2. At the top or bottom limit the wheel is not passed on unless the owner wants it.
3. Wheel over empty space scrolls nothing.
Likely work: make WidgetContainer_PreviewMouseWheel decide ownership explicitly, mark events handled consistently, stop nested ScrollViewers from competing.
Acceptance: no double scroll, no scroll jumping between favorites, bookmarks and the right column.

## PHASE 4 - Focus mode (G3)

Goal: focus mode respects the same bounds as the normal layout.
Likely work: place the focus container below the search bar, keep it above the shared bottom boundary, remove the ghost glass panel, let folder cards size to their content instead of clipping.
Acceptance: no overlap with the search bar or the VPN and wallpaper buttons, no clipped card contents.

## PHASE 5 - Verification

Owner sends Ctrl+Shift+D dumps and screenshots at phone-sized, tablet, square and widescreen sizes, plus live resize. Check G1-G4 and that nothing from the layout plan regressed. Fix only regressions.

## AFTER THE BUGFIX

Return to HORIZON_HOME_LAYOUT_PLAN.md phases 1-5. Phases 1 and 2 there (global boundary, capped center icons) assume the glitches above are gone.

## REPORTING FORMAT PER PHASE

Before each phase: current scenario, whether confidence is above 97 percent, what information is missing.
Patch format per change: output file path, existing or new file, file name, type of change, exact find text, exact replace text. State apply order when changes depend on each other.
