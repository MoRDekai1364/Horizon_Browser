# HORIZON BROWSER - HOMEPAGE HANDOFF STATE

Read this first, then HORIZON_HOME_LAYOUT_PLAN.md (original end goal and requirements R1-R8) and HORIZON_HOME_BUGFIX_PLAN.md (bugfix phases).
Repo: MoRDekai1364/Horizon_Browser. Branch: new_changes ONLY. Never touch official_release or Stable_pre-release. Never push. Output edits only.
Stack: WPF, .NET 8, WebView2. Owner (MoRDekai) compiles and tests. Do not compile.
GitHub credentials are in the owner's saved preferences. Clone read-only, reset the remote URL after cloning so the token is not stored in the clone, never echo or write the token into files.

## HARD RULES
1. No comments in code.
2. Questions to the owner go through the built-in question UI.
3. Patch format per change: output file path, existing or new file, file name, type of change (add new or modify existing), exact Ctrl+F find text, exact replacement text. State apply order when changes depend on each other. Find strings must be unique in the file. Verify uniqueness with grep before sending.
4. One phase at a time. Owner reports compile and test results between phases.
5. Before each phase state: current scenario, whether confidence is above 97 percent, what information is missing.
6. No hardcoded single-resolution values. Everything derived from live sizes.
7. Short direct communication, no filler.
8. Debug aid: Ctrl+Shift+D copies a layout dump to the clipboard. Ask the owner for dumps at several window sizes.

## ROOT CAUSES FOUND AND FIXED (owner applied and tested)

A. Panel assignments swapped (Views/HomePageView.xaml, Views/HomePageView.xaml.cs).
   In WPF, GroupStyle.Panel hosts the GroupItems and ItemsControl.ItemsPanel hosts the items inside each group. The code had them reversed, so AutoGridPanel.Mode was never applied and the bookmarks list rendered fully open (76929px tall).
   Fix: GroupStyle Panel = PuzzleItemsPanel and ItemsPanel = BlockItemsPanel on both IcnColumns and IcnBookmarks. SetRowsPanel now uses "RowsItemsPanel" : "BlockItemsPanel".
   Result: confirmed better.

B. Stray blurred glass tiles (Services/HomeGlassService.cs).
   HomeGlassInlineLayer draws one overlay with a mask shape per eligible element and never intersected shapes with ancestor clips.
   Fix: new method GetAncestorClipRect(FrameworkElement, Rect) walks ancestors that are ScrollViewer or have ClipToBounds or Clip, intersects bounds in root coordinates. UpdateMask collapses shapes with empty intersection and sets shape.Clip to the visible part.
   Result: owner confirmed no blur tiles.

C. Row gap between folder cards (Controls/PuzzlePanel.cs).
   PackUniform used the tallest collapsed card as the height of every row (cell 260x295 while many cards are 172).
   Fix: PackUniform now uses each card's own desired height, row height = tallest card in that row, and records cumulative row bottoms in _rowBottoms. Snap picks the number of rows from _rowBottoms against the viewport budget. MeasureUniform uses a row signature (count + sum of bottoms) instead of cellH to decide when to re-snap. DebugUniform() was added and is printed in the dump tree.
   Result: IcnBookmarks height dropped from 2146 to 1654 at 1213x750. Owner dumps confirm.

D. Rounded clip on scrollers (Views/HomePageView.xaml.cs).
   New method ApplyScrollerClip(ScrollViewer) sets a RectangleGeometry clip using FavBookmarksIslandBorder.CornerRadius.TopLeft capped to half the shorter side. Hooked to ScvFavorites.SizeChanged and ScvBookmarks.SizeChanged in the constructor.
   Result: applied, visual effect subtle (scrollers sit about 45px inside the island). Not separately verified by the owner. Alternatives offered: soft fade at edges, concentric radius.

E. Already done earlier in chat: malformed @AppIcon line in default_settings.txt fixed.

## OPEN PROBLEMS (ordered by my recommendation)

P1. Bookmarks scroller ignores the available height (this is layout plan R1 and R2).
    Evidence from dumps (page 1533x800, 1213x750, 955x569): ScvBookmarks is 607px and the island 810px at every size. searchAreaBottom=928 at every size while boundary is 664, 614 and 433. PnlSearchArea reports 868px tall with maxH 604, 554 and 373.
    Causes: (1) PuzzlePanel.MinRows = 2 forces at least two rows in Snap regardless of budget. (2) The budget set in ApplyFallbackBudget is rows * HomeRowLimits.RowHeight (constant 171) and card heights are 295 or 172, so the constant is wrong. (3) The island grid rows are Auto, so PnlSearchArea.MaxHeight never constrains ScvBookmarks (children desired size exceeds MaxHeight and WPF arranges at least the unclipped desired size, so ActualHeight can exceed MaxHeight).
    Planned fix: measure other = PnlSearchArea.ActualHeight - ScvBookmarks.ActualHeight, available = PnlSearchArea.MaxHeight - other, use that as the viewport budget for the bookmarks scroller. Snap starts from one row, adds rows while their bottom fits, and if even the first row does not fit uses the budget itself (partial row, scrollable). Then continue with layout plan Phase 2 (search bar stays centered, pushed up only as last resort, capped icon count, overflow to HomeStack).
    Relevant code: HomePageView.xaml.cs ApplyScrollCap, ApplyFallbackBudget, UpdateSearchAreaMaxHeight, HomeBottomBoundary, HomeButtonGap, ResolvedRows. PuzzlePanel.cs Snap, HomeRowLimits.

P2. 3-item folder card clips its third icon.
    Dump shows AutoGridPanel actual=300x123 desired=242x123 inside an ItemsPresenter of 242x123 for the group with children=3 (mode=Folder). Arrange is wider than measure. Not yet traced. Read Controls/PuzzlePanel.cs AutoGridPanel MeasureOverride and ArrangeOverride for Folder mode with 3 children.

P3. Scroll chaining (wheel).
    Cause: WidgetContainer_PreviewMouseWheel (HomePageView.xaml.cs near line 401) runs on PnlFavoritesContainer and PnlBookmarksContainer before the nested ScrollViewers. Wheel up with the stack expanded collapses the stack and handles the event. Wheel down in fallback mode expands the stack or enters focus mode and handles the event. The nested scroller only scrolls when nothing else claims the wheel.
    Needs owner decision on wheel rules before coding. Proposed: wheel over a widget with overflow scrolls that widget first; at its limit do not pass on unless the owner wants it; wheel over empty space scrolls nothing.

P4. Focus mode overlap.
    ComputeExpandedScrollMaxHeight uses RootHomeGrid.ActualHeight minus fixed reserves (10, 52, 24) that do not match real margins (container margin becomes 28 after reposition) and ignores the shared bottom boundary. PnlSearchArea is vertically centered so oversized content overflows both ends: search bar cut at the top, bottom row over the VPN button. FadeMoveSearchArea not fully read. Fix should use the same measured boundary as P1.

P5. Phase 3 of the layout plan: rounded finite bottoms for the center island and the right column (HomeStack).
P6. Phase 4 of the layout plan: search bar mode transition reusing the header widget widen animation (MainWindow.xaml.cs near lines 12464-12510).
P7. Phase 5: multi-resolution verification with dumps.

## KEY FACTS ABOUT THE CODE
- HomeBottomBoundary() and HomeButtonGap() already exist. UpdateSearchAreaMaxHeight sets PnlSearchArea bottom margin to layoutHeight - boundary and MaxHeight to layoutHeight - top margin - reserve.
- Favorites uses RowsPanel when favFallback is false. Bookmarks uses grouped folder mode (PuzzlePanel hosting GroupItems, AutoGridPanel inside each group) when bmFallback is true.
- PuzzlePanel finds its host ScrollViewer on Loaded and Snap sets host.MaxHeight. GroupState attached properties: IsCollapsed on GroupItem, IsFolder on the owner ItemsControl.
- Dump fields: page size, rows, columns, boundary, gap, layoutHeight, searchAreaBottom, islandBottom, stackBottom, element sizes, panel tree with uniform cell, cols and cards=[cN or oN] (c = collapsed, N = desired height).
- Owner machine: dumps seen at 1533x800 (fullscreen), 1213x750, 955x569. Windowed 955x569 drops to 1 column and a 3076px tall PuzzlePanel.

## HOW TO CONTINUE
1. Clone branch new_changes, check git log for newer commits (owner may have changed files), read this file and both plan files.
2. Ask the owner for current dumps at fullscreen and one windowed size if code may have changed.
3. Work P1 first unless the owner redirects. State scenario, confidence, missing info, then send patches in the required format.

## STATUS UPDATE (after P1 and P2 patches, owner dumps and screenshots)

Applied by owner and verified by dumps:
F. P1 budget fix. HomePageView.xaml.cs: ApplyFallbackBudget now takes a double budget; new method BookmarksBudget(int rows) calls UpdateSearchAreaMaxHeight, then returns PnlSearchArea.MaxHeight minus (PnlSearchArea.ActualHeight - ScvBookmarks.ActualHeight), falling back to rows * HomeRowLimits.RowHeight when sizes are not measured yet. Favorites budget stays favoriteRows * HomeRowLimits.RowHeight. PuzzlePanel.Snap starts from one row, adds rows while _rowBottoms[k-1] <= budget, target = Math.Min(bottomAtRows + 4, budget). MinRows const is now unused.
   Dumps: 1275x656 searchAreaBottom 520 = boundary 520 and stackBottom 520. 1533x800 searchAreaBottom 642 under boundary 664. 955x656 and 1213x800 also within boundary.
G. P2 3-item folder fix. AutoGridPanel: new const FolderRowMax = 2. Single-row folder layout only for 1 or 2 items, 3 items now use the 2x2 grid. Dump confirms AutoGridPanel for the 3-item group is 200x246 and the card is 218 wide (cell=218x295).

New findings from the latest dumps and screenshots:
N1. Phone-sized window FAILS the boundary. Dump page=453x616: GridHomeLayout 453x542, boundary=480, but searchAreaBottom=616 and islandBottom=616 although PnlSearchArea is 433x460 with margin 10,20,10,62. Bottom edge is 136px lower than margin plus height would give, and below layoutHeight. Cause not traced. Suspects: RenderTransform or vertical offset applied to PnlSearchArea (FadeMoveSearchArea), narrow-width layout path, or the window being taller than the page grid. HomeStack collapsed at this width.
N2. Focus mode (screenshot of mode "Search bookmarks...") still broken: search bar clipped at the top, folder card labels and contents clipped on the right, card list runs past the window bottom and over the VPN button, ghost glass panel at the left. This is P4, untouched.
N3. In the normal-size screenshot the bookmarks folder cards render blank icon tiles (rounded squares with labels but no favicon images) and the right Bookmarks panel is empty. Favorites icons render. Not yet known whether this is a loading or cache effect or a regression. Owner asked about the cause; waiting for the owner's answer on whether the icons appear after a few seconds. If they stay blank, suspect the item template Image or the favicon loading path, then check my patches B and D (glass clip and scroller clip) as the only visual-layer changes.
N4. Normal-size layout now looks right: three folder cards per row, no row gap, one row shown at 1920x1080 with a "Show more" button under the scroller, island and right column both end above the VPN and wallpaper buttons.

Next steps in order: answer N3 with the owner, trace N1 (read HomePageView.xaml.cs around FadeMoveSearchArea and any code setting PnlSearchArea transform or margin, plus the narrow-width branch), then P3 scroll chaining (needs owner decision), then P4 focus mode using the measured boundary, then layout plan Phase 3 and 4.

## STATUS UPDATE 3

Patches sent, awaiting owner test report:
H. Phone-size boundary (N1). HomePageView.xaml.cs UpdateSearchAreaMaxHeight now subtracts rowOffset = Grid.GetRow(PnlSearchArea) == 0 ? 0 : RowClockWeather.ActualHeight from the MaxHeight (under 600px wide the search area sits in row 1 below the clock row). Constructor also calls ApplyScrollCap when PnlClockWeather.SizeChanged fires. Expect dump at 453x616 to show searchAreaBottom <= boundary.
I. Removed scroll-triggered widening. WidgetContainer_PreviewMouseWheel no longer calls EnterWidgetFocusMode: the two lines are now "if (!ExpandStackFor("Favorites")) return;" and the Bookmarks equivalent. Wheel now passes through to the nested scroller when the stack cannot expand. EnterWidgetFocusMode is still called from lines near 384 and 397 (click or button triggers) and was left on purpose.

Remaining work:
1. Owner test report for H and I.
2. N3 blank bookmark icons. Pipeline: PinIconConverter (IsAsync=True) falls back to FaviconConverter in Controls/FluxSidebar.xaml.cs, which returns a google favicons URL string for hosts without a disk cache and never rebinds after the background fetch. Waiting on owner: does FaviconCache under the user data root contain .png files, does a restart bring icons back, was the machine online. None of patches A-I touch this path.
3. P3 scroll chaining, reduced scope: wheel up with the stack expanded still collapses the stack and handles the event, which blocks scrolling the widget back up. Needs the owner's wheel rule decision.
4. P4 focus mode clipping and overlap (search bar cut at top, card labels clipped, list past the bottom, ghost glass panel). Use the measured boundary, replace the fixed reserves in ComputeExpandedScrollMaxHeight.
5. Layout plan Phase 3: rounded finite bottoms for the center island and HomeStack, check the left column.
6. Layout plan Phase 2 leftovers: BookmarksBudget ignores HomeBookmarkRows (it was an upper limit before). The favorites folder fallback can now show a partial row. Verify the search bar stays centered and only moves up as a last resort.
7. Layout plan Phase 4 search bar mode animation, Phase 5 multi-resolution verification (fullscreen, 1275x656, 955x656, 453x616).
8. Cosmetic: tile labels are cut off at the right edge inside 100px folder tiles (for example "emag.bç"). Not investigated.
9. Cleanup: unused MinRows const in PuzzlePanel.cs. Watch scroll smoothness after the GetAncestorClipRect patch.
10. Owner: local commit after each verified phase, push only when handing over.
