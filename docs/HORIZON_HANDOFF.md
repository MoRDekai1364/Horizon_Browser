# HORIZON BROWSER HOMEPAGE REWORK: HANDOFF (2026-10-04)

## 1. ROLE AND RULES (user: MoRDekai1364)
- Continue the Horizon Browser homepage rework. App is WPF / XAML / C# (.NET 8, net8.0-windows). NOT a web stack.
- Repo: gh repo clone MoRDekai1364/Horizon_Browser. Branch: new_changes ONLY. NEVER push. Credentials are in the user's saved preferences, never copy them into files or replies.
- Output changes in Find/Replace format. Per change: output file path, New/Existing file, name, type (add new / modify existing), exact Ctrl+F Find text, exact Replace text.
- Validate every Find block against a patched copy (each must match exactly once). Check XAML well-formedness. State "not compiled". Never compile; user compiles and pastes errors.
- No comments in code. Do not rename or shrink uploaded files. Ask before creating new files.
- Ask every question with the builtin question tool (ask_user_input_v0), max 3 per call.
- Any visual proposal must be text-symbol (ASCII) mockups with a legend and description. Get approval BEFORE coding. The user was angry twice about unapproved designs.
- Short direct replies, no filler, no sugar-coating. Warn the user when context is getting full.
- Separate pending tasks in user memory (do not start unless asked): VPN phases 2 to 4, default_settings rework, Web App manage button.

## 2. FILES IN THIS HANDOFF
- p4_bookmark_grouping.diff : repo (new_changes) -> P4 state
- p5_folder_cards.diff : P4 state -> P5 state (user DISLIKES this result, see 4)
- p6_uniform_grid.diff : P5 state -> P6 state (same, on hold)
Each is a unified diff against the repo files. The repo clone itself contains P1 to P3 only (PuzzlePanel.cs etc.). Whether the user applied the P4/P5/P6 patches is UNCONFIRMED; ask.

## 3. WHAT EXISTS IN THE REPO (new_changes)
- Controls/PuzzlePanel.cs (namespace Horizon.Stealth.Controls): PuzzlePanel (packs child rects), GroupState (attached IsCollapsed), enum GroupCollapseMode {Full, FirstRow, Stack, Mini}, AutoGridPanel (ColumnsFor, HeightFor, Stack, FirstRow, Mini 3x3: 8 full tiles + cluster cell scaled 0.46 or 0.3).
- Services/SettingsService.cs: HomeGroupStyle (HeaderOnly|FirstRow|Stack|Mini|Full). default_settings.txt entry S05.22.
- Views/HomePageView.xaml(.cs): IcnColumns (favorites, grouped by PinItem.Category), IcnBookmarks (grouped by BookmarkItem.GroupKey), PuzzleGroupItemStyle, GroupItem_Loaded, GroupHeader_Click, AnimateGroup (300 ms SineEase EaseInOut), ApplyGroupVisual, CollapseModeFromSetting, ApplyScrollCap, focus mode (EnterWidgetFocusMode, wheel, "Show more", CmbCategoryFilter All/Recent/Older sorted by DateAdded).
- Views/HomePageSettingsWindow.xaml(.cs): WIDGETS tab, CmbGroupStyle with GroupStyleChoices/GroupStyleValues.
- Header widget holder: lives INSIDE MainWindow.xaml.cs as code-behind, not a reusable control. Names: _widgetDefaultWidth = 146.0, AnimateWidgetWidth(...) (about lines 8347, 12336), PnlWidgetCycle, BtnWidgetPrev_Click / BtnWidgetNext_Click / BtnWidgetNav_RightClick, BtnWidgetMoveUp/Down_Click, WidgetPosition class in SettingsService.cs.
- WebView2 is used (MainWindow.xaml.cs, CoreWebView2, IsSuspended checks). WebAppWindow.xaml, MusicSites helper exist.
- NO usage counters exist on PinItem or BookmarkItem (only DateAdded on bookmarks). "Most used" is not implementable yet.

## 4. PATCH HISTORY AND STATUS
- Patches 1 to 10 = P4 (p4_bookmark_grouping.diff): setting HomeBookmarkGrouping (Domain|RecentOlder|Single), S05.23, settings combo CmbBookmarkGrouping, RefreshBookmarks modes, BuildRecentOlder/NewerGroupTitle, RefreshBookmarks call on visibility, `using Horizon.Stealth.Controls;` fix (build error CS0246 for GroupCollapseMode/AutoGridPanel). Valid, independent of the redesign. Unconfirmed compile.
- Patches 11 to 31 = P5 (p5_folder_cards.diff): style "Folder" (default), GroupCollapseMode.Folder in AutoGridPanel (3 full + 4-mini cluster, 3x1 for up to 3 items), card with name below, FolderCard_Click, open/collapsed sets. User screenshot showed groups as tall single columns, not folder cards: panel Mode did not switch to Folder. CAUSE NOT FOUND (a patch may be missing on the user's disk, or ApplyGroupVisual/CollapseModeFromSetting bug).
- Patches 32 to 43 = P6 (p6_uniform_grid.diff): uniform grid in PuzzlePanel (up to 6 columns, rows 2 to 6 via ViewportBudget snap), IsCollapsed affects parent layout.
- DECISION: P5/P6 put folder cards in the NORMAL view, which the user says is wrong. Hold them. Reuse their cluster layout and uniform-grid code inside the DRAWER later (phase D1/D2). P4 stays.

## 5. FINAL DESIGN (user-approved decisions)
Legend: `╭─╮` card, `▣` full icon (clickable), `▫` mini icon (opens folder), `░` blur+dim+vignette, `⇅` vertical tri-switch, `●○` position dots.

### 5.1 Page: 3 invisible columns
```
┌─────────┬──────────────────────┬───────────────────┐
│ LEFT    │ CENTER               │ STACK (right)     │
│ clock   │ ⇅Search here...      │ ╭───────────────╮ │ ●
│ date    │ ▣ ▣ ▣ ▣ ▣  favorites │ │ Favorites     │ │ ○
│ tasks   │ ▣ ▣ ▣ ▣ ▣  bookmarks │ │ drawer        │ │ ○
│ (done)  │                      │ ╰───────────────╯ │ ＋
└─────────┴──────────────────────┴───────────────────┘
```
- LEFT is done (clock, date, latest calendar tasks).
- CENTER: finished search bar (idle text "Search here...", engine button at left end) plus rows of favorites and bookmarks.
- RIGHT: an iOS-Smart-Stack-style stack, OxygenOS look, reusing the header widget holder's controls and animations (must first be extracted from MainWindow into a reusable control). If a widget is placed in the right column, its space and rules are respected.

### 5.2 Search bar tri-switch
Replaces the word "Search". Vertical, endless loop: Favorites / Search (default, middle) / Bookmarks. Click expands vertically with the header widening animation (same 300 ms SineEase). Draggable with snap and bounce, scrollable, clickable. The existing search bar that turns into a bookmark/favorites search is reused. When a stack element is expanded, the text auto-switches to "Bookmarks here..." / "Favorites here...".

### 5.3 Normal mode rows (center)
- 2 to 4 rows. Favorites and bookmarks have SEPARATE logic. Top rows: most used favorites. Below: most used bookmarks. An empty row is not shown. Default = flat icon rows.
- Fallback to folder cards (per group, favorites and bookmarks decided separately) if EITHER: (a) eligible items exceed what the rows hold, or (b) a folder is exceptionally frequently used. User had no preference on details; defaults chosen: in case (a) the whole group of rows turns into folder cards; case (b) threshold is a home-settings value, default 30% of that widget's opens.
- Scrolling the wheel while hovering a row expands it (existing focus-mode wheel trigger idea; keep wheel).

### 5.4 Expanded drawer (OxygenOS Categories style)
```
 ╭─────────────────╮  ╭────────────╮ ╭────────────╮
 │ ▣   ▣   ▣       │  │ ▣  ▣  ▣   │ │ ▣  ▣  ▣   │
 ╰─────────────────╯  │ ▣  ▣  ▣   │ │ ▣  ▣  ▣   │
   up to 3 items      │ ▣  ▣  ▫▫  │ │ ▣  ▣  ▫▫  │
   (one-row card)     ╰────────────╯ ╰────────────╯
                         Social         Games
```
- Folder card N x N, N from 3 to 6; N^2 - 1 full icons, last cell = 4 mini icons (scale about 0.46). Clicking the cluster (or empty card) opens the folder; the 4 minis are NOT clickable individually.
- Groups with up to 3 items = a mini folder with one horizontal row (like "Suggested apps").
- Sizing: adapt to free space, grow toward 6x6 first; if no space, or a neighbour would drop below 3x3, keep size and paginate (OxygenOS folder pages).
- No "All" view (user chose folders only). Names below cards.
- Bookmark grouping keeps the P4 modes (Domain default, RecentOlder, Single). Favorites group by PinItem.Category.

### 5.5 Opened folder (screenshot style, NO card)
```
 ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
 ░        Entertainment          ░
 ░     ▣     ▣     ▣     ▣       ░
 ░     ▣     ▣     ▣     ▣       ░
 ░            ● ○ ○              ░
```
Title above, big icons directly on the blurred dimmed backdrop, page dots, scroll/swipe = next page. Close on click outside, Esc, or a close button. Two open modes in home settings: Option 1 = side-expand (left folder fills left side, right folder the right, center goes left, others pushed away, a second folder opens on the right); Option 2 = popup with blurred/dimmed background (DEFAULT). Both use the header widget widening animation adapted. Build popup first; side-expand later.

### 5.6 Where the expansion appears (latest decision)
When the user scrolls over a row, the expansion stays INSIDE THE RIGHT COLUMN ONLY. The stack switches to the matching element (Favorites or Bookmarks drawer), and that element grows vertically and horizontally within the right column using the identical widening animation as the header widget. No full-page blur takeover. Opening a folder switches the stack to a temporary "folder" element; closing returns to the previous element.

### 5.7 The stack (new, big)
- Elements: Favorites drawer, Bookmarks drawer, plus any number of user-added elements.
- Add via a wizard: Step 1 what (built-in widget / website / template); Step 2 which (template list or URL); Step 3 options (name, icon, refresh interval, login profile); Step 4 preview + Add. User can edit and remove elements later.
- Web login profile: choice in the wizard, default SHARED with the main browser.
- Auto-detect: if the user enters a URL that matches a template (example: gmail.com -> Mail), offer "optimize for Mail?" with Yes/No; dismissible.
- Templates (user list): Mail, Videos, Music, Streaming & Gaming, Movies, Torrent & P2P, Office, AI, Info & Research. Additional proposals: Chat & Messaging, Calendar & Planner, News, Developer, Cloud & Files, Social feeds, Shopping & Price watch, Translate & Dictionary, Maps & Travel, Notes & Scratchpad, Generic site (default). Any existing browser widget (clock, media, calendar, weather, battery, VPN/AdBlock) can be an element.
- "Optimized" = compact mobile-like layout, built-in CSS hiding banners/sidebars, dark theme, preset zoom, refresh interval, optional unread badge. Only built-in templates inject scripts; users may add CSS, not arbitrary JS. Torrent & P2P is a layout template with NO preloaded site lists.
- Performance rules: one live WebView2 per visible element, suspend others (existing IsSuspended pattern), cap simultaneous live views.

## 6. PHASE PLAN (status: none of S/D phases started)
| Phase | Scope | Status |
|---|---|---|
| P1 to P3 | tiles, puzzle packing, collapse styles | applied (P3 compile unconfirmed) |
| P4 | bookmark grouping setting | delivered (Patches 1 to 10), unconfirmed |
| P5, P6 | folder cards in normal view | delivered, ON HOLD (wrong place) |
| S1 | extract header widget holder into a reusable stack control, no behavior change | NEXT (riskiest, own compile test) |
| S2 | put the stack in the right column with Favorites and Bookmarks elements | todo |
| D0 | usage counters (OpenCount, LastOpened) on PinItem and BookmarkItem; folder usage = sum | todo |
| D1 to D3 | drawer folder cards, adaptive 3x3 to 6x6 with pages, card-less opened folder (popup) inside the stack | todo |
| D4 | normal-mode rows + fallback rules + wheel expansion inside the right column | todo |
| S3 | wizard, element storage, edit/remove | todo |
| S4 | built-in widget elements | todo |
| S5 | web templates + "optimize this site?" detection | todo |
| D5 | tri-switch in the search bar | todo |
| D6 | side-expand open mode + home setting | todo |
The user asked what to start with and said: output this plan first. Confirm the start phase (recommended S1 or D0) with the question tool.

## 7. OPEN ITEMS
- Confirm which of Patches 1 to 43 are on the user's disk; compile status of P3/P4.
- Find the cause of the P5 bug only if P5 is reused.
- User must confirm plan approval (asked twice, answered "change the plan"; then added the stack idea).
- Known limits from before: changing HomeGroupStyle does not restyle already collapsed groups until rebuild; expand target height measured at unlimited width; no virtualization (hundreds of tiles fine, thousands untested).
- The user's saved settings keep the old HomeGroupStyle; only fresh settings get the new default.
