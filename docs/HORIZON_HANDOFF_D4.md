# HORIZON BROWSER HOMEPAGE REWORK: HANDOFF 2 (2026-10-06)

## 1. ROLE AND RULES (user: MoRDekai1364)
- App is WPF / XAML / C# (.NET 8, net8.0-windows10.0.19041.0, UseWPF + UseWindowsForms, ImplicitUsings, Nullable enabled, System.Drawing and System.Windows.Forms usings removed in csproj).
- Repo: gh repo clone MoRDekai1364/Horizon_Browser. Branch: new_changes ONLY. NEVER push. Credentials are in the user's saved preferences; never copy them into files or replies.
- Output changes in Find/Replace format. Per change: file path, New/Existing file, name, New change / Modify existing, exact Ctrl+F Find text, exact Replace text. Full file only for new files or full rewrites.
- Validate every Find block against a patched copy (each must match exactly once). Check XAML well-formedness. State "not compiled". Never compile; user compiles and pastes errors. dotnet is not available in the container anyway.
- No comments in code. Do not rename or shrink uploaded files. Ask before creating new files.
- Ask every question with ask_user_input_v0 (max 3 per call).
- Any visual proposal: ASCII mockup with legend, get approval BEFORE coding. The user was angry twice about unapproved designs.
- Short direct replies, no filler. Warn when context is getting full.
- Pending separate tasks (do not start unless asked): VPN phases 2 to 4, default_settings rework, Web App manage button.

## 2. REPO STATE (new_changes, head was a2bfcab before D1 to D3 patches)
Already in the repo (found by inspection):
- P4 (bookmark grouping setting HomeBookmarkGrouping: Domain|RecentOlder|Single), P5 (GroupCollapseMode.Folder cluster in AutoGridPanel, FolderCard_Click), P6 (uniform grid, ViewportBudget, PuzzlePanel.SetViewportBudget/Resnap, GroupState.SetIsFolder).
- D0 done: OpenCount and LastOpened exist on PinItem (SettingsService.cs) and BookmarkItem (BookmarksService.cs, BookmarkService.RecordOpen).
- Stack: Controls/StackHost.xaml(.cs) (AddElement, RemoveElement, ReplaceElement, Select, Next, Previous, ExpandTo(width,height), Collapse, 300 ms SineEase, dots column), Controls/StackElementBuilders.cs, Views/StackElementWizardWindow, StackElementStore. HomeStack in HomePageView.xaml: Grid.Column=2, stretch, Margin 0,50,40,50, hidden when ColRightBalance.ActualWidth < 260. BuildHomeStack and AddStackEntry in HomePageView.xaml.cs.
- Center: PnlSearchArea (column 1) with SearchBoxBorder, CmbSearchEngine, CmbCategoryFilter, TxtHomeSearch, GridFavBookmarksIsland, PnlFavoritesContainer / ScvFavorites / IcnColumns, PnlBookmarksContainer / ScvBookmarks / IcnBookmarks, BtnToggleFavorites/Bookmarks, focus mode (EnterWidgetFocusMode, ExitWidgetFocusMode, WidgetContainer_PreviewMouseWheel enters focus mode on wheel down when more than 6 items), ApplyScrollCap, ApplyGroupVisual, CollapseModeFromSetting, RefreshFavorites, RefreshBookmarks (sets BookmarkItem.GroupKey, builds CollectionViewSource with PropertyGroupDescription, early return on signature match).

## 3. DELIVERED THIS CHAT (D1 to D3). Compile status UNCONFIRMED; ask the user.
- D1: new Controls/FolderDrawerView.cs (DrawerItem, DrawerGroup, FolderDrawerView). HomePageView.xaml.cs: BuildStackCard removed; fields _favoritesDrawer/_bookmarksDrawer; UpdateFavoritesDrawer/UpdateBookmarksDrawer read IcnColumns.ItemsSource / IcnBookmarks.ItemsSource groups (ICollectionView.Groups) and are called after ItemsSource is set in RefreshFavorites/RefreshBookmarks and at the end of BuildHomeStack; DrawerItem_Activated records OpenCount/LastOpened and raises NavigateRequested; DrawerIconOf uses PinIconConverter.
- D2: FolderDrawerView rewritten: cards start 3x3 (one-row card for 1 to 3 items), grow toward 6x6 by hidden-item count while all cards fit on one page, otherwise paginate at 3x3 in whole rows; page dots; wheel changes pages and passes through to the stack at the edges or with one page; 90 ms debounced relayout; 300 ms SineEase slide.
- D3: OpenFolderView class added to FolderDrawerView.cs (card-less, 64 px icons, title, close button, pages, dots, tooltips only for names). StackHost got an Overlay layer (radial vignette dim, BlurEffect radius 14 on Incoming, 300 ms fade), ShowOverlay/CloseOverlay/IsOverlayOpen/OverlayClosed, Esc and backdrop click close, Go() and wheel switching blocked while open. HomePageView: DrawerFolder_Requested wires FolderOpenRequested of both drawers.
- Known deviations to confirm: overlay is drawn over the still-rendered previous element instead of swapping in a temporary element; no icon name labels in the opened folder (matches mockup); no swipe/drag; Esc depends on the Overlay keeping keyboard focus (a MainWindow handler could intercept).

## 4. D4 DECISIONS (user-approved)
Phases: D4a flat rows, D4b folder-card fallback, D4c wheel expansion in right column. Start with D4a.

### D4a flat rows in the center
- Replace the current grouped/folder-card center view (P5/P6 behavior in the normal view, currently active) with flat icon rows. Favorites and bookmarks have separate logic.
- Order: most used first (OpenCount desc, then LastOpened desc, then name). Favorites rows on top, bookmark rows below. An empty row is not shown.
- Icons per row: auto from available width, clamp(floor(width / cell), MinRowIcons = 3, MaxRowIcons = 6). One named constant each, defined once (user asked whether max 6 is bad hardcoding: decision is good, because it keeps the UI stable and the fallback capacity math stable).
- Row split: home setting, two combos in HomePageSettingsWindow: favorites rows and bookmark rows. DEFAULT 2+2. Max total rows is computed from screen/window height: floor(available height / row height), clamped between 4 and a hard ceiling constant of 8; combos only offer values that fit. The saved setting is never rewritten; the rendered rows are clamped at runtime. Needs: new setting (e.g. HomeFavoriteRows, HomeBookmarkRows), default_settings.txt entry, SettingsService property, settings window combos (follow the HomeBookmarkGrouping pattern from P4: S05.23, CmbBookmarkGrouping).
- ARCHITECTURE FINDING: the drawer reads its groups from IcnColumns/IcnBookmarks ItemsSource. Flat rows show only the top items that fit, so the center needs its OWN truncated list. The drawer must keep the FULL grouped view. Split the data sources in RefreshFavorites/RefreshBookmarks (keep a separate full grouped ICollectionView for the drawer, and call UpdateFavoritesDrawer/UpdateBookmarksDrawer from it), check focus-mode filters (search text, CmbCategoryFilter All/Recent/Older), the XAML GroupStyle templates for IcnColumns/IcnBookmarks, ApplyScrollCap, and the sig early-return. Read those first.
- Interim state after D4a (before D4b): items beyond the row capacity are not shown in the center (they are in the drawer).

### D4b fallback to folder cards (per widget, favorites and bookmarks decided separately)
- Trigger (either): (a) eligible items exceed what the rows hold: the whole group of rows of that widget turns into folder cards; (b) one folder is exceptionally frequently used: its share of that widget's opens (folder usage = sum of OpenCount of its items) is at least a threshold, home-settings value, default 30%.
- Reuse P5 cluster layout and P4 grouping modes (Favorites group by PinItem.Category; bookmarks by HomeBookmarkGrouping, Domain default).

### D4c wheel expansion inside the right column
- Wheel over a center row: the stack switches to the matching element (Favorites or Bookmarks drawer) and expands with StackHost.ExpandTo (same 300 ms SineEase), inside the right column only. No full-page blur. Replace the current WidgetContainer_PreviewMouseWheel focus-mode trigger; keep the "Show more" toggle unless the user says otherwise.
- Stack resting size: adaptive (proposal to confirm in D4c: resting height matches the center block from search bar top to last row bottom, top aligned, width = column width capped at 320; expanded = full column height and width). HomeStack currently stretches to the full column, so a resting size is needed before ExpandTo is meaningful.
- Opening a folder from the expanded drawer keeps the D3 popup.

## 5. PHASE PLAN
| Phase | Scope | Status |
|---|---|---|
| P1 to P6 | tiles, puzzle, collapse styles, bookmark grouping, folder cards | in repo; P5/P6 normal-view use is replaced by D4 |
| S1 to S3 | stack control, right-column stack, wizard and store | in repo (found by inspection) |
| D0 | usage counters | done |
| D1 to D3 | drawer folder cards, adaptive sizing and pages, opened folder popup | delivered, compile unconfirmed |
| D4a | flat rows, most-used order, row settings, data-source split | NEXT |
| D4b | folder-card fallback | todo |
| D4c | wheel expansion in right column, adaptive stack size | todo |
| S4, S5 | built-in widget elements, web templates + optimize detection | todo |
| D5 | tri-switch in the search bar (Favorites / Search / Bookmarks, "Bookmarks here..." / "Favorites here..." text) | todo |
| D6 | side-expand open mode + home setting | todo |

## 6. OPEN ITEMS
- Ask: did D1 to D3 compile and work? Paste errors if any.
- Confirm the stack resting-size proposal in D4c.
- Known limits: changing HomeGroupStyle does not restyle already collapsed groups until rebuild; no virtualization; user's saved settings keep the old HomeGroupStyle.
