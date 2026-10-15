# HORIZON BROWSER // SYSTEM ARCHITECTURE & DEVELOPER COMPENDIUM

> **CONFIDENTIAL & COMPREHENSIVE DEVELOPER REFERENCE**  
> *Targeted for future AI pair-programming agents and software engineers working on Horizon Browser.*

---

## 1. Project Overview & Technology Stack

Horizon Browser is a high-performance, cyberpunk/sleek glass-themed desktop web browser engineered on Microsoft .NET and Windows Presentation Foundation (WPF), leveraging the modern Microsoft Edge WebView2 (Chromium) engine.

- **Target Framework**: .NET 8.0 (`net8.0-windows10.0.19041.0`)
- **Platform Architecture**: `win-x64` (64-bit Windows)
- **UI Subsystem**: WPF with custom DirectX hardware-accelerated shaders, glass motion physics, and vector styling.
- **Web Engine**: Microsoft.Web.WebView2 (Chromium Evergreen Runtime)
- **Primary Source Directories**:
  - `MainWindow.xaml` / `MainWindow.xaml.cs`: Root window shell, tab strip management, omnibox wiring, window controls, and global event dispatching.
  - `Controls/`: Modular browser view (`BrowserView`), tab controls, glass panels (`HomeGlassMotion`), sidebar hosts (`VaultSidebar`, `FluxSidebar`), drawer/folder views (`FolderCardView`, `FolderDrawerView`), and widget stack (`StackHost`).
  - `Views/`: Major feature dialogs (`SettingsWindow`, `HomePageView`, `BackupRestoreDialog`, `ExistingInstallationDialog`, `PdfOpenDialog`, etc.).
  - `Services/`: Core singleton/static services for persistent configuration, security, extensions, backups, downloads, VPN relay, and localization.
  - `Tools/Horizon.Updater/`: Standalone out-of-process updater and data migration/recovery engine (`Horizon.Updater.exe`).
  - `Tools/Horizon.SessionImporter/`: Migration tool for importing bookmarks, cookies, and history from Chrome, Brave, and Edge.

---

## 2. Dual Storage & File System Model

Horizon operates under a dual-location profile and storage strategy designed to support both portable directory deployments and standard Windows Application Data paths.

### Path Topologies

| Location Role | File Path | Contents |
| :--- | :--- | :--- |
| **Project / Bin Root** | `AppDomain.CurrentDomain.BaseDirectory` | Binaries (`Horizon.Browser.exe`, DLLs), `settings.json`, `default_settings.txt`, `Tools/`, `version.txt`. |
| **Local AppRoot Data** | `<AppDir>\HorizonData\` | Primary `config.json`, `bookmarks.json`, `vault.dat`, `cookies.json`. |
| **Windows LocalAppData** | `%LOCALAPPDATA%\Horizon_Browser\` | AppData mirror, `Backups/`, `webapps/`, downloads tracking. |
| **Chromium Profile Root**| `%LOCALAPPDATA%\Horizon_Browser\HorizonData\` | Chromium user data folder (`EBWebView\Default`), SQLite DBs, LevelDB, cookies, storage. |

### Critical File Locking Rule
> [!CAUTION]
> **Never copy or restore profile files (`EBWebView\Default\...`, SQLite, `Preferences`) while `Horizon.Browser.exe` or `msedgewebview2.exe` are active.**
> Chromium holds mandatory OS locks on these files. Furthermore, on shutdown, WebView2 flushes in-memory session caches to disk, overwriting any in-process file writes. All restore operations **must** be delegated to the out-of-process engine: `Horizon.Updater.exe --restore "<sourcePath>" --target "<targetPath>" --pid <currentPid>`.

---

## 3. Configuration & Settings Architecture

Configuration is managed centrally via `Services/SettingsService.cs`:
- **Model Class**: `SettingsData`
- **Serialization**: `System.Text.Json` (with `CompatJson` deserializer fallback)
- **Save Strategy**: Atomic write to `config.json.tmp` followed by backup rotation (`config.json.bak`) and `File.Move(tmp, path, overwrite: true)`.
- **Migration Pipeline**: `SettingsService.Migrate(data)` runs on every startup, ensuring missing fields from older versions (e.g. `DismissedInstallationPaths`, layout IDs) are initialized with sensible defaults.

### Key Configuration Settings
- `DefaultLanguage`: Web content language code (`en`, `bg`, etc.).
- `AppLanguage`: Interface language for the browser shell.
- `DismissedInstallationPaths`: List of discovered historical installations the user has dismissed or restored from.
- `HomeShowRightColumn`, `HomeRightColumnFavorites`, `HomeRightColumnBookmarks`: Homepage sidebar widgets toggles.
- `StartupVideoMode`, `WallpaperFolderPath`, `NarrowWindowThresholdPx`: Visual customization toggles.

---

## 4. Multilingual & Localization Architecture (`LocalizationService`)

The localization architecture provides a resilient, zero-crash, modular translation engine:

1. **Resolution Hierarchy**:
   - `LocalizationService.GetString(key)` inspects the active language dictionary (`Languages/{lang}.json`).
   - If key is missing or blank, it immediately falls back to the default English dictionary (`Languages/en.json`).
   - If still missing, it falls back to the provided default string or key name.
2. **Modular Language Packs**:
   - Packaged in `Languages/` (e.g. `en.json`, `bg.json`, `de.json`, `it.json`, `fr.json`, `ru.json`, `es.json`).
   - Adding a new language simply requires placing `<culture>.json` in `Languages/`. The browser discovers it dynamically without recompilation.
3. **Active-Development Resilience**:
   - Because Horizon is actively evolving, newly created menus or modified features will automatically render English if a translation is not yet supplied in a language pack, preventing any missing labels or broken dialogs.
4. **Real-Time UI Binding**:
   - `LocalizationService` exposes `INotifyPropertyChanged` and an indexed property (`Loc.Current["Key"]`), enabling dynamic updates in XAML without requiring application restart.

---

## 5. Homepage Layout & Widget Architecture (`Views/HomePageView`)

The homepage features a dynamic, responsive 3-column cyberpunk layout:

### Column Hierarchy
1. **Left Column (`ColClockWeather`)**:
   - Clock widget (12h/24h), dynamic weather pill, calendar events preview, media player controls, battery status pill.
2. **Central Column (`ColSearchArea`)**:
   - Omnibox search pill with multi-engine dropdown (`AlohFind`, `DuckDuckGo`, `Google`, `Bing`, `Brave`, `Ecosia`).
   - Search mirrors & mode selector (`PnlSearchMode`).
   - Merged glass island (`GridFavBookmarksIsland`) hosting favorites (`PnlFavoritesContainer`) and bookmarks (`PnlBookmarksContainer`).
   - Folder drawers and sheet overlay host (`_centerSheet`).
3. **Right Column (`ColRightBalance` / `HomeStack`)**:
   - Collapsible/expandable `StackHost` card deck containing quick drawers, custom widgets, system notes, or web snippets.
   - Dynamic rest-size calculation based on central island height.

### Responsive Breakpoints
- **Mobile / Compact (< 600px)**:
  - Left column stacks on top; central column expands to fill width; right column collapses cleanly.
- **Standard Laptop / Desktop (600px - 1800px)**:
  - Left column anchored; central search island centered in viewport; right column visible if balance allows `>= 260px`.
- **Ultrawide / 4K (> 1800px)**:
  - Central column scales adaptively (max width expanded up to 1200px); grid items expand into 4-6 balanced columns; right column gracefully balances spacing.

---

## 6. Standalone Updater & Data Migration Engine (`Horizon.Updater`)

The updater is an independent, out-of-process WPF application located at `Tools/Horizon.Updater/`:
- **Executable**: `Horizon.Updater.exe`
- **CLI Invocations**:
  - `Horizon.Updater.exe --update "<installerPath>" --target "<appDir>" --pid <browserPid>`
  - `Horizon.Updater.exe --restore "<sourcePath>" --target "<appDir>" --pid <browserPid>`
- **Workflow**:
  1. Shuts down browser parent process and all lingering `msedgewebview2.exe` instances.
  2. Creates a pre-restore safety snapshot at `%LOCALAPPDATA%\Horizon_Browser\Backups\pre_restore_snapshot`.
  3. Replaces core databases, vaults, notes, cookies, and `EBWebView` profile files to **both** target directories.
  4. Patches restored `config.json` with `DismissedInstallationPaths` so restored installations never re-trigger warnings on subsequent launches.
  5. Relaunches `Horizon.Browser.exe` and closes itself.

---

## 7. Developer Rules & Critical Constraints

1. **STRICT GIT CONSTRAINT**:
   - **NEVER EXECUTE `git push`**.
   - The repository owner explicitly manages all pushes to GitHub. All AI agents must only commit locally to the active branch (`git commit`).
2. **ZERO COMPILATION WARNINGS & ZERO ERRORS**:
   - All code must compile cleanly with `dotnet build -c Release` with 0 Warnings and 0 Errors.
   - Use proper nullable reference type annotations (`string?`, null-coalescing, pattern matching).
3. **BATCH SCRIPT COMPATIBILITY**:
   - Maintain compatibility with `Build_Release.bat` and `restore.bat`.
   - Never remove or break batch command hooks used by InstallForge or release packagers.
