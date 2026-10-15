# HORIZON BROWSER - SYSTEM ARCHITECTURE & DEVELOPER KNOWLEDGE BASE

> **For Future AI Agents & Developers**: Read this document first. It consolidates all critical architectural patterns, resolved bugs, localization systems, folder/widget animation constraints, and developer rules for the Horizon Browser codebase.

---

## 1. Project Overview & Environment

- **Repository**: `MoRDekai1364/Horizon_Browser`
- **Active Development Branch**: `new_optimization_changes`
- **Tech Stack**:
  - C# 12 / .NET 8 (`net8.0-windows10.0.19041.0`)
  - WPF (Windows Presentation Foundation) with Windows Forms interop for system integration
  - Microsoft WebView2 for web engine tabs
  - InstallForge for packaging and installer generation
- **Build Quality Standard**: Strict **0 Warnings, 0 Errors** on `dotnet build Horizon.Browser.csproj -c Release`.
- **CRITICAL COMMIT RULE**:
  - **DO NOT EXECUTE `git push`**. The owner (MoRDekai) pushes to GitHub manually.
  - All changes must be cleanly committed locally to git branch `new_optimization_changes`.

---

## 2. Directory Structure & Key Components

```
├── App.xaml / App.xaml.cs            # Entry point, single-instance mutex, crash recovery, startup language init
├── MainWindow.xaml / .cs             # Primary browser window, tab bar, navigation buttons, omnibox, header glow
├── Horizon.Browser.csproj            # Embedded resource configurations (Languages/**), dependencies
├── Controls/
│   ├── BrowserView.xaml / .cs        # WebView2 tab wrapper, download hooks, context menus, restart logic
│   ├── Omnibox.xaml / .cs            # Address & search bar, secure lock indicator, domain vs query parser
│   ├── FolderCardView.cs             # Homepage folder cards, FolderSheetHost open/close animation
│   ├── FolderDrawerView.cs           # Drawer popups for Favorites and Bookmarks
│   ├── StackHost.xaml / .cs          # Right-column widgets stack (HomeStack), overlay sheets, pager dots
│   ├── PuzzlePanel.cs                # Masonry / packing panels for folder grid and responsive tiles
│   └── HomeGlassMotion.cs            # Motion coordination for blur & glass effects
├── Services/
│   ├── LocalizationService.cs        # INotifyPropertyChanged + [Key] indexer + embedded resource fallback
│   ├── SettingsService.cs            # App configuration store (settings.json / default_settings.txt)
│   ├── HomeGlassService.cs           # Glass/blur mask rendering layer, RenderTargetBitmap caching
│   ├── GithubUpdateService.cs        # Silent background GitHub release checker & updater
│   ├── InstallationDetectorService.cs# Multi-installation scanning & safe backup-before-restore engine
│   ├── HistoryService.cs             # Browsing history persistence
│   ├── BookmarkService.cs            # Bookmarks manager
│   └── VaultService.cs               # Encrypted credential store
├── Views/
│   ├── HomePageView.xaml / .cs       # Main homepage: adaptive search bar, favorites/bookmarks, island widgets
│   ├── SettingsWindow.xaml / .cs     # 9-tab configuration interface (General, Visuals, Homepage, Input, etc.)
│   └── MaintenanceWindow.xaml / .cs  # Safe recovery and installation migration tool
└── Languages/
    ├── bg.json                       # Bulgarian (Primary / Default UI language)
    ├── en.json                       # English (Master fallback dictionary)
    ├── de.json                       # German
    ├── fr.json                       # French
    ├── it.json                       # Italian
    ├── es.json                       # Spanish
    └── ru.json                       # Russian
```

---

## 3. Localization Architecture (`LocalizationService`)

### How Localization Works:
1. **Dictionary Loading Priority**:
   - `%LOCALAPPDATA%\Horizon_Browser\Languages\{code}.json` (User overrides/drop-ins)
   - App BaseDirectory `Languages\{code}.json`
   - Assembly Embedded Resources (`Horizon.Browser.Languages.{code}.json`)
   - English (`en.json`) is always loaded into `_englishFallback` for seamless fallback if any key is missing.
2. **Real-Time Dynamic Switching**:
   - When the user selects a language in `SettingsWindow.xaml` (`CboAppLanguage`), `CboAppLanguage_SelectionChanged` immediately invokes:
     ```csharp
     LocalizationService.Instance.SetLanguage(code, persistSetting: true);
     ```
   - This fires `LocalizationService.Instance.LanguageChanged` and raises `PropertyChanged` on `Binding.IndexerName` (`Item[]`).
   - `HomePageView`, `SettingsWindow`, and `MainWindow` subscribe to `LanguageChanged` and execute their respective `ApplyLocalization()` methods immediately without requiring a restart.
3. **Key Mappings**:
   - Flattened JSON keys: `Category.SubKey` (e.g. `Homepage.SearchPlaceholder`, `Settings.General`, `Common.Save`). Simple keys like `SearchPlaceholder` are also indexed for convenience.

---

## 4. Homepage Animation & Glass Rendering Pipeline

### Critical Architectural Insight: The "Glass Motion Trap"
- `HomeGlassService` uses a single background glass layer that renders blurred elements using dynamic opacity masks via `RenderTargetBitmap`.
- **Do NOT pulse `HomeGlassMotion` during scale/translate transforms**:
  - Calling `HomeGlassMotion.Pulse(ms)` hooks into `CompositionTarget.Rendering` every VSYNC frame.
  - If animated controls (e.g., folder sheets or overlays) are registered with `HomeGlass`, their bounds change every frame, forcing a 4K/1080p `RenderTargetBitmap` re-render every 16ms on the CPU! This drops animation frame rates from 60fps to 10-15fps.
  - **Rule**: All transient overlays, popups, and expanding folder sheets (`FolderSheetHost`, `StackHost.Overlay`, `OpenFolderView`) MUST be explicitly excluded:
    ```csharp
    HomeGlass.SetExclude(element, true);
    ```
  - Animated sheets must use native WPF hardware-accelerated transforms (`ScaleTransform`, `TranslateTransform`) with GPU caching, and avoid motion pulses.

### 1-Frame Closing Flash Fix:
- When closing an expanding folder (`FolderSheetHost.Finish()` or `StackHost.CloseInPlace()`):
  - **Always set `Visibility = Visibility.Collapsed;` BEFORE resetting animation transforms.**
  - In `ClearAnimations()`, reset `Opacity` to `0` (never `1`), so hidden elements can never flash on screen.

---

## 5. Multi-Installation Detection & Safe Data Restoration

- **Location**: `Services/InstallationDetectorService.cs` and `Views/MaintenanceWindow.xaml.cs`.
- **Behavior**:
  - Scans registry, `%LOCALAPPDATA%`, `Program Files`, and known install locations for prior Horizon Browser installs.
  - Before restoring/migrating profile data (cookies, bookmarks, passwords, sessions, local storage) from an existing installation, it **ALWAYS creates an automatic safety backup** of current user data into `%LOCALAPPDATA%\Horizon_Browser\Backups\pre_migration_backup_{timestamp}`.
  - Status display uses standard sentence-case text (never screaming all-caps): `"Възстановяване на данни на Horizon..."` / `"Restoring Horizon data..."`.

---

## 6. Developer Guidelines for Future Edits

1. **Local Commits Only**: Never run `git push`. Always verify with `git status`.
2. **0 Warnings, 0 Errors**: Always test with `dotnet build Horizon.Browser.csproj -c Release`.
3. **Responsive Homepage Design**:
   - Search bar area is width-capped (`MaxWidth = 1100`) for ultrawide monitors and adapts down to mobile screens.
   - Folders with 1–4 items (no cluster canvas) are directly clickable on card body and title.
4. **Preserve Code Structure**: Do not remove existing event handlers or refactor unrelated logic.

---

## 7. Homepage Animation Pipeline & Right-Column Architecture

1. **HomeGlass Exclude & Motion Pulse**:
   - `HomeGlassInlineLayer` provides acrylic-style blurring behind homepage elements.
   - Any container with many moving sub-elements (`FolderDrawerView`, `StartMenuView`, `OpenFolderView`, `FolderCardView`, `FolderSheetHost`, `StackHost.Overlay`) MUST set `HomeGlass.SetExclude(this, true)`.
   - `HomeGlassInlineLayer.RegisterSubtree` checks `HomeGlass.GetExclude(fe)` and aborts recursion early, preventing deep tree registration and 60 FPS CPU `RenderTargetBitmap` invalidations.
   - `HomeGlassMotion.Pulse(ms)` MUST NOT be called in paging/scroll animations (`SlideToPage`, `GoTo`, `StackHost.Go`); smooth animations are handled by WPF hardware composition, with `HomeGlass.Invalidate()` called once on `Completed`.
2. **StartMenu Grid Centering**:
   - `StartMenuView.Relayout` arranges folder cards into row `StackPanel`s centered horizontally (`HorizontalAlignment.Center`) inside a vertical `StackPanel` centered vertically (`VerticalAlignment.Center`), ensuring items remain centered regardless of total item or column count.
3. **Favicon Loading & Notification**:
   - `FaviconConverter.Convert` returns a placeholder URL string when querying remote domains.
   - `DrawerIconOf` handles both `ImageSource` instances and remote URL strings by initializing a `BitmapImage(UriSource)` with `BitmapCacheOption.OnLoad`.
   - `FaviconConverter.IconDownloaded` notifies subscribers when an icon has been saved to disk, prompting `UpdateFavoritesDrawer()` and `UpdateBookmarksDrawer()` to refresh icons asynchronously.
   - `BookmarkItem.AsPin` passes `IconPath = this.IconPath` so custom bookmark icons are preserved.
4. **Background Tab Synchronization**:
   - `HomePageView.IsVisibleChanged` synchronizes `_searchMode` with `SettingsService.Current.HomeSearchMode` when a background tab becomes active.
   - `TabViewModel.DomainTitle` maps `horizon://home` and the `horizon` scheme to `"Home"`.
   - `MainWindow.CreateNewTab` initializes `newTab.Title = "Home"` when navigating to `HomeSentinel`, avoiding title desync.

---

## 8. Right-Column Defaults, Stack Calendar Widget & Settings Localization

1. **Right-Column Enabled by Default**:
   - `SettingsService.CurrentConfigVersion` bumped to `3`.
   - `HomeShowRightColumn`, `HomeRightColumnFavorites`, `HomeRightColumnBookmarks` default to `true` in `SettingsData` and `default_settings.txt`.
   - `SettingsService.Migrate` upgrades any config where `ConfigVersion < 3` to enable these options.

2. **Bookmarks & Favorites Drawers Robustness**:
   - `FolderDrawerView` defines `MinHeight = 180` and `MinWidth = 200`.
   - When `_groups.Count == 0`, `Relayout()` renders a centered empty-state placeholder (`"Nothing to show"` / `"Няма елементи за показване"`), preventing the viewport from collapsing to 0 height with only the title visible.
   - `HomePageView.xaml.cs` provides direct fallbacks to group from `SettingsService.Current.PinnedUrls` and `BookmarkService.Items` when collection views are not yet grouped.
   - `RefreshBookmarks()` always calls `UpdateBookmarksDrawer()` before early return.
   - `SyncBuiltinStackPages()` retains entries based on user settings without purging when groups are temporarily empty.

3. **Adapted Calendar Widget for Right Column Stack**:
   - `Controls/StackCalendarWidget.cs` implements an interactive, responsive monthly calendar designed for the stack (~280–320px width).
   - Features:
     - Header with previous/next month navigation and clickable month/year label to reset to current month.
     - 7-column weekday row with language-aware abbreviations (e.g. Пн..Нд in Bulgarian, Mo..Su in English/German).
     - 6-row × 7-col day grid highlighting today with theme accent (`WeatherBridge.ThemeAccent`), dimming out-of-month days, and placing green/accent dots on days with events.
     - Event preview footer bar displaying selected day events or next upcoming event from `CalendarBridge.GetNextEvent()`.
     - Direct click / double-click invokes `MainWindow.OpenCalendarWindow()` without reflection.
     - Live reactive updates via `CalendarBridge.Updated` and `LocalizationService.Instance.LanguageChanged`.
   - Registered in `StackElementBuilders.RegisterLaunchWidgets()` for `"calendar"`.

4. **Comprehensive Dynamic Settings Localization**:
   - `SettingsWindow.xaml.cs` implements `LocalizeElementTree()` in `ApplyLocalization()`.
   - Automatically traverses all elements in the window and all TabItems (including tabs not yet rendered).
   - Caches original English strings in `_originalTexts`, `_originalToolTips`, `_originalLabels`, `_originalDescriptions` to ensure lossless multi-language switching.
   - Localizes `TextBlock.Text`, `ContentControl.Content` (CheckBoxes, RadioButtons, Buttons), `ToolTip`, `SearchIndex.Label`, and `SearchIndex.Description`.
   - Invalidates `_searchIndexCache = null` on language change, enabling real-time search in Bulgarian and all other languages.
   - Expanded language dictionaries (`Languages/bg.json`, `en.json`, `de.json`, `fr.json`, `it.json`, `ru.json`, `es.json`) with over 380 settings strings.

