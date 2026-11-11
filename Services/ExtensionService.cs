using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Horizon.Stealth.Services;

/// <summary>
/// Central service for managing Horizon's browser extensions.
///
/// DATA LAYOUT
/// ───────────
///   HorizonData\extensions.json          — extension catalog (name, version, enabled flag …)
///   %LocalAppData%\Horizon_Browser\
///       Extensions\{id}\                 — unpacked extension folders (WebView2 loads from here)
///       BundleCache\{id}\                — read-only copy of in-app bundled extensions
///
/// STARTUP FLOW
/// ────────────
///   1.  EnsureInstalled()  — called once before WebView2 init
///       a.  Syncs bundled extensions from {AppDir}\Extensions\ → BundleCache\
///       b.  Loads extensions.json catalog
///       c.  Registers bundled extensions in catalog if not already present
///       d.  For each catalog entry that is enabled, ensures folder is present in Extensions\
///
/// USER INSTALLS
/// ─────────────
///   ExtensionInstaller downloads & extracts CRX/XPI → Extensions\{id}\
///   Then Register() is called here to add/update the catalog entry.
/// </summary>
public static class ExtensionService
{
    // ── Bundled extension metadata ───────────────────────────────────────────
    private static readonly Dictionary<string, (string Icon, string Description)> _bundledMeta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["consent-o-matic"] = ("\U0001F36A", "Automatically handles GDPR/cookie consent dialogs."),
            ["adguard"]         = ("\U0001F6E1",  "Blocks ads, trackers, and malware on every site."),
            ["sponsorblock-for-youtube-skip-sponsorships-chrome-web-store"] = ("\u23ED", "Skip sponsorships, intro animations, and annoying segments on YouTube."),
            ["sponsorblock"]    = ("\u23ED", "Skip sponsorships, intro animations, and annoying segments on YouTube."),
            ["linguist-web-page-translator-chrome-web-store"] = ("\U0001F310", "Translate full web pages and selected text instantly."),
            ["linguist"]        = ("\U0001F310", "Translate full web pages and selected text instantly."),
            ["twp-translate-[beta]-chrome-web-store"] = ("\U0001F22F", "Translate web pages in real-time."),
            ["twp-translate"]   = ("\U0001F22F", "Translate web pages in real-time."),
            ["remove-paywalls-chrome-web-store"] = ("\U0001F513", "Read articles cleanly without paywalls or nag screens."),
            ["remove-paywalls"] = ("\U0001F513", "Read articles cleanly without paywalls or nag screens."),
            ["save-it-later-chrome-web-store"] = ("\U0001F4BE", "Save articles and bookmarks to read offline later."),
            ["save-it-later"]   = ("\U0001F4BE", "Save articles and bookmarks to read offline later."),
            ["crx-extractor-downloader-chrome-web-store"] = ("\U0001F4E6", "Download and unpack Chrome extensions as ZIP/CRX."),
            ["crx-extractor"]   = ("\U0001F4E6", "Download and unpack Chrome extensions as ZIP/CRX."),
            ["dbepggeogbaibhgnhhndojpepiihcmeb"] = ("\u2328\uFE0F", "Navigate web pages and links with keyboard shortcuts."),
            ["vimium"]          = ("\u2328\uFE0F", "Navigate web pages and links with keyboard shortcuts."),
        };

    private static (string Icon, string Description) GetBundledMeta(string id)
    {
        if (_bundledMeta.TryGetValue(id, out var meta)) return meta;
        string lower = id.ToLowerInvariant();
        if (lower.Contains("vimium") || lower == "dbepggeogbaibhgnhhndojpepiihcmeb") return ("\u2328\uFE0F", "Navigate web pages and links with keyboard shortcuts.");
        if (lower.Contains("consent")) return ("\U0001F36A", "Automatically handles GDPR/cookie consent dialogs.");
        if (lower.Contains("adguard")) return ("\U0001F6E1", "Blocks ads, trackers, and malware on every site.");
        if (lower.Contains("sponsorblock")) return ("\u23ED", "Skip sponsorships, intro animations, and annoying segments on YouTube.");
        if (lower.Contains("linguist")) return ("\U0001F310", "Translate full web pages and selected text instantly.");
        if (lower.Contains("twp") || lower.Contains("translate")) return ("\U0001F22F", "Translate web pages in real-time.");
        if (lower.Contains("paywall")) return ("\U0001F513", "Read articles cleanly without paywalls or nag screens.");
        if (lower.Contains("save")) return ("\U0001F4BE", "Save articles and bookmarks to read offline later.");
        if (lower.Contains("crx")) return ("\U0001F4E6", "Download and unpack Chrome extensions as ZIP/CRX.");
        return ("\U0001F9E9", string.Empty);
    }

    // ── Paths ────────────────────────────────────────────────────────────────
    private static string DataRoot => ConfigService.UserDataRoot;

    private static string CatalogFile => Path.Combine(DataRoot, "extensions.json");

    public static string InstallRoot => ConfigService.ExtensionsRoot;

    private static string BundleCache =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Horizon_Browser", "BundleCache");

    private static string AppBundleRoot =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extensions");

    // ── In-memory catalog ────────────────────────────────────────────────────
    private static List<ExtensionRecord> _catalog = new();

    public static IReadOnlyList<ExtensionRecord> All => _catalog.AsReadOnly();

    // ── Events ───────────────────────────────────────────────────────────────
    public static event Action? CatalogChanged;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Call once at startup, before WebView2 initialises.
    /// </summary>
    public static void EnsureInstalled()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(InstallRoot);
            Directory.CreateDirectory(BundleCache);

            LoadCatalog();
            SyncBundledExtensions();
            RegisterBundledInCatalog();
            ScanInstalledFolder();
            ResolveAllIconPaths();
            EnsureFolders();
            SaveCatalog();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ExtensionService.EnsureInstalled");
        }
    }

    /// <summary>
    /// Registers a freshly installed extension in the catalog.
    /// Call after ExtensionInstaller has extracted the files.
    /// </summary>
    public static void Register(ExtensionRecord record)
    {
        string folder = GetInstallFolder(record.FolderName);
        if (Directory.Exists(folder))
        {
            string manifest = Path.Combine(folder, "manifest.json");
            record.IconPath ??= ResolveIconPath(folder, manifest);
        }

        _catalog.RemoveAll(e => e.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase));
        _catalog.Add(record);
        SaveCatalog();
        CatalogChanged?.Invoke();
        LogService.Write("EXT", $"Registered extension: {record.Name} ({record.Id})");
    }

    public static void SetEnabled(string id, bool enabled)
    {
        var ext = Find(id);
        if (ext == null) return;
        ext.Enabled = enabled;
        SaveCatalog();
        CatalogChanged?.Invoke();
    }

    /// <summary>
    /// Removes a user-installed extension. Bundled extensions are disabled instead.
    /// </summary>
    public static bool Uninstall(string id)
    {
        var ext = Find(id);
        if (ext == null) return false;

        if (ext.Source == ExtensionSource.Bundled)
        {
            SetEnabled(id, false);
            return true;
        }

        string dir = Path.Combine(InstallRoot, id);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { LogService.RecordCrash(ex, $"ExtensionService.Uninstall '{id}'"); }

        _catalog.RemoveAll(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        SaveCatalog();
        CatalogChanged?.Invoke();
        LogService.Write("EXT", $"Uninstalled extension: {id}");
        return true;
    }

    public static ExtensionRecord? Find(string id) =>
        _catalog.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                                     e.Name.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string GetInstallFolder(string id) => Path.Combine(InstallRoot, id);

    public static bool? GetShortcutPermission(string id)
    {
        var ext = Find(id);
        if (ext?.AllowsShortcutModification != null)
            return ext.AllowsShortcutModification;

        if (SettingsService.Current.ExtensionShortcutPermissions.TryGetValue(id, out bool perm))
            return perm;

        return null;
    }

    public static void SetShortcutPermission(string id, bool allowed)
    {
        var ext = Find(id);
        if (ext != null)
        {
            ext.AllowsShortcutModification = allowed;
            SaveCatalog();
        }
        SettingsService.Current.ExtensionShortcutPermissions[id] = allowed;
        SettingsService.Save();
        CatalogChanged?.Invoke();
    }

    public static bool PromptShortcutPermission(string id, string name, System.Windows.Window? owner = null)
    {
        var app = System.Windows.Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() => PromptShortcutPermission(id, name, owner));
        }

        string title = "Extension Keyboard Shortcuts";
        string message =
            $"The extension '{name}' modifies keyboard shortcuts and page navigation controls.\n\n" +
            $"Do you want to allow '{name}' to manage and override keyboard shortcuts in Horizon Browser?\n\n" +
            "\u2022 Yes: Allow extension to manage shortcuts and keep it enabled.\n" +
            "\u2022 No: Block shortcut modification and keep the extension disabled.";

        owner ??= app?.MainWindow;
        System.Windows.MessageBoxResult res = owner != null && owner.IsLoaded && owner.IsVisible
            ? System.Windows.MessageBox.Show(owner, message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
            : System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

        bool allowed = res == System.Windows.MessageBoxResult.Yes;
        SetShortcutPermission(id, allowed);
        if (allowed)
        {
            SetEnabled(id, true);
        }
        else
        {
            SetEnabled(id, false);
        }
        return allowed;
    }

    public static bool ModifiesShortcuts(string manifestPathOrDir, string? id = null, string? name = null, string? desc = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(id))
            {
                string lowerId = id.ToLowerInvariant();
                if (lowerId.Contains("vimium") || lowerId == "dbepggeogbaibhgnhhndojpepiihcmeb")
                    return true;
            }

            if (!string.IsNullOrEmpty(name))
            {
                string lowerName = name.ToLowerInvariant();
                if (lowerName.Contains("vimium") || lowerName.Contains("surfingkeys"))
                    return true;
            }

            if (!string.IsNullOrEmpty(desc))
            {
                string lowerDesc = desc.ToLowerInvariant();
                if (lowerDesc.Contains("keyboard shortcut") || lowerDesc.Contains("spirit of vim") ||
                    lowerDesc.Contains("shortcuts for navigation") || lowerDesc.Contains("shortcut navigation"))
                    return true;
            }

            string manifestPath = manifestPathOrDir;
            if (Directory.Exists(manifestPathOrDir))
            {
                manifestPath = Path.Combine(manifestPathOrDir, "manifest.json");
            }

            if (File.Exists(manifestPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;

                // Check "commands" section in manifest
                if (root.TryGetProperty("commands", out var cmdProp) && cmdProp.ValueKind == JsonValueKind.Object)
                {
                    int cmdCount = 0;
                    foreach (var _ in cmdProp.EnumerateObject()) { cmdCount++; }
                    if (cmdCount > 0) return true;
                }

                // Check name & description in manifest
                if (root.TryGetProperty("name", out var nProp))
                {
                    string n = nProp.GetString() ?? "";
                    if (n.Contains("vimium", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("surfingkeys", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                if (root.TryGetProperty("description", out var dProp))
                {
                    string d = dProp.GetString() ?? "";
                    if (d.Contains("keyboard shortcut", StringComparison.OrdinalIgnoreCase) ||
                        d.Contains("spirit of vim", StringComparison.OrdinalIgnoreCase) ||
                        d.Contains("shortcuts for navigation", StringComparison.OrdinalIgnoreCase) ||
                        d.Contains("shortcut navigation", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Check content_scripts for keyboard interception scripts
                if (root.TryGetProperty("content_scripts", out var csProp) && csProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cs in csProp.EnumerateArray())
                    {
                        if (cs.TryGetProperty("js", out var jsProp) && jsProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var js in jsProp.EnumerateArray())
                            {
                                string script = js.GetString() ?? "";
                                if (script.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ||
                                    script.Contains("key_handler", StringComparison.OrdinalIgnoreCase) ||
                                    script.Contains("vimium", StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug("EXT", () => $"ModifiesShortcuts check failed: {ex.Message}");
        }

        return false;
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private static void SyncBundledExtensions()
    {
        var currentIds = Directory.Exists(AppBundleRoot)
            ? Directory.GetDirectories(AppBundleRoot).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(AppBundleRoot))
        {
            foreach (var src in Directory.GetDirectories(AppBundleRoot))
            {
                string id   = Path.GetFileName(src);
                string dest = Path.Combine(BundleCache, id);
                CopyDirectoryIfChanged(src, dest);
            }
        }

        // Remove cached copies (and their catalog entries / install folders) for
        // bundled extensions whose source folder no longer exists in AppBundleRoot.
        if (Directory.Exists(BundleCache))
        {
            foreach (var cachedDir in Directory.GetDirectories(BundleCache))
            {
                string id = Path.GetFileName(cachedDir);
                if (currentIds.Contains(id)) continue;

                try { Directory.Delete(cachedDir, recursive: true); }
                catch (Exception ex) { LogService.RecordCrash(ex, $"ExtensionService.SyncBundledExtensions cleanup '{id}'"); }

                string installedDir = Path.Combine(InstallRoot, id);
                try { if (Directory.Exists(installedDir)) Directory.Delete(installedDir, recursive: true); }
                catch (Exception ex) { LogService.RecordCrash(ex, $"ExtensionService.SyncBundledExtensions cleanup install '{id}'"); }

                _catalog.RemoveAll(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && e.Source == ExtensionSource.Bundled);
            }
        }
    }

    private static void RegisterBundledInCatalog()
    {
        if (!Directory.Exists(BundleCache)) return;
        foreach (var dir in Directory.GetDirectories(BundleCache))
        {
            string id       = Path.GetFileName(dir).ToLowerInvariant();
            string manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) continue;

            var meta = GetBundledMeta(id);
            string version = ReadManifestVersion(manifest);
            string name = ReadManifestName(manifest, id);
            string desc = ReadManifestDescription(manifest, meta.Description);
            string? iconPath = ResolveIconPath(dir, manifest);

            var existing = Find(id);
            if (existing != null)
            {
                existing.Version = version;
                if (!string.IsNullOrEmpty(iconPath)) existing.IconPath = iconPath;
                if (string.IsNullOrEmpty(existing.Name) || existing.Name.StartsWith("__MSG_") || existing.Name.Equals(id, StringComparison.OrdinalIgnoreCase))
                    existing.Name = name;
                if (string.IsNullOrEmpty(existing.Description))
                    existing.Description = !string.IsNullOrEmpty(meta.Description) ? meta.Description : desc;
                continue;
            }

            _catalog.Add(new ExtensionRecord
            {
                Id          = id,
                Name        = name,
                Description = !string.IsNullOrEmpty(meta.Description) ? meta.Description : desc,
                Version     = version,
                Icon        = meta.Icon ?? "\U0001F9E9",
                IconPath    = iconPath,
                Source      = ExtensionSource.Bundled,
                Enabled     = true,
                InstalledAt = DateTime.Now,
            });
        }
    }

    private static void ScanInstalledFolder()
    {
        if (!Directory.Exists(InstallRoot)) return;
        foreach (var dir in Directory.GetDirectories(InstallRoot))
        {
            string id = Path.GetFileName(dir);
            string manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) continue;

            var existing = Find(id);
            if (existing == null)
            {
                var meta = GetBundledMeta(id);
                string version = ReadManifestVersion(manifest);
                string name = ReadManifestName(manifest, id);
                string desc = ReadManifestDescription(manifest, meta.Description);
                string? iconPath = ResolveIconPath(dir, manifest);

                ExtensionSource src = ExtensionSource.Manual;
                if (id.Length == 32 && id.All(c => c >= 'a' && c <= 'p'))
                    src = ExtensionSource.ChromeStore;

                _catalog.Add(new ExtensionRecord
                {
                    Id          = id,
                    Name        = name,
                    Description = !string.IsNullOrEmpty(meta.Description) ? meta.Description : desc,
                    Version     = version,
                    Icon        = meta.Icon ?? "\U0001F9E9",
                    IconPath    = iconPath,
                    Source      = src,
                    Enabled     = true,
                    InstalledAt = DateTime.Now
                });
            }
            else
            {
                if (string.IsNullOrEmpty(existing.IconPath) || !File.Exists(existing.IconPath))
                {
                    existing.IconPath = ResolveIconPath(dir, manifest);
                }
            }
        }
    }

    public static void ResolveAllIconPaths()
    {
        foreach (var ext in _catalog)
        {
            string folder = GetInstallFolder(ext.FolderName);
            if (!Directory.Exists(folder) && ext.Source == ExtensionSource.Bundled)
            {
                folder = Path.Combine(BundleCache, ext.FolderName);
            }

            if (Directory.Exists(folder))
            {
                string manifest = Path.Combine(folder, "manifest.json");
                string? iconPath = ResolveIconPath(folder, manifest);
                if (!string.IsNullOrEmpty(iconPath))
                {
                    ext.IconPath = iconPath;
                }
            }
        }
    }

    public static string? ResolveIconPath(string folder, string manifestPath)
    {
        try
        {
            if (File.Exists(manifestPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;

                // 1. Check "icons" property in manifest.json (prefer highest resolution)
                if (root.TryGetProperty("icons", out var iconsObj) && iconsObj.ValueKind == JsonValueKind.Object)
                {
                    string[] preferredSizes = { "128", "256", "512", "64", "48", "32", "16" };
                    foreach (var s in preferredSizes)
                    {
                        if (iconsObj.TryGetProperty(s, out var iconProp))
                        {
                            string rel = iconProp.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(rel))
                            {
                                string full = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
                                if (File.Exists(full)) return full;
                            }
                        }
                    }

                    // Any size in icons object
                    foreach (var prop in iconsObj.EnumerateObject())
                    {
                        string rel = prop.Value.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(rel))
                        {
                            string full = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(full)) return full;
                        }
                    }
                }

                // 2. Check "action.default_icon" or "browser_action.default_icon"
                foreach (var actionPropName in new[] { "action", "browser_action", "page_action" })
                {
                    if (root.TryGetProperty(actionPropName, out var actObj) && actObj.ValueKind == JsonValueKind.Object)
                    {
                        if (actObj.TryGetProperty("default_icon", out var dIcon))
                        {
                            if (dIcon.ValueKind == JsonValueKind.String)
                            {
                                string rel = dIcon.GetString() ?? "";
                                if (!string.IsNullOrWhiteSpace(rel))
                                {
                                    string full = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
                                    if (File.Exists(full)) return full;
                                }
                            }
                            else if (dIcon.ValueKind == JsonValueKind.Object)
                            {
                                string[] preferredSizes = { "128", "256", "64", "48", "32", "16" };
                                foreach (var s in preferredSizes)
                                {
                                    if (dIcon.TryGetProperty(s, out var iProp))
                                    {
                                        string rel = iProp.GetString() ?? "";
                                        if (!string.IsNullOrWhiteSpace(rel))
                                        {
                                            string full = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
                                            if (File.Exists(full)) return full;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 3. Fallback: Search directory on disk
            if (Directory.Exists(folder))
            {
                string[] candidates =
                {
                    "icon128.png", "icon-128.png", "icon_128.png", "128.png", "icon.png",
                    "icon48.png", "icon-48.png", "icon_48.png", "48.png",
                    "icon32.png", "icon-32.png", "icon_32.png", "32.png",
                    "icon16.png", "icon-16.png", "icon_16.png", "16.png",
                    "logo.png", "icon.ico", "favicon.ico",
                    Path.Combine("icons", "icon128.png"), Path.Combine("assets", "icon-128.png"),
                    Path.Combine("assets", "icons", "icon128.png"), Path.Combine("assets", "icons", "on-128.png"),
                    Path.Combine("static", "logo.png")
                };

                foreach (var c in candidates)
                {
                    string p = Path.Combine(folder, c);
                    if (File.Exists(p)) return p;
                }

                // Recursive search for any icon png/ico
                var files = Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories);
                var iconFile = files.FirstOrDefault(f => f.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                        f.IndexOf("128", StringComparison.OrdinalIgnoreCase) >= 0)
                            ?? files.FirstOrDefault(f => f.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                            ?? files.FirstOrDefault(f => f.IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0);

                if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
                    return iconFile;

                if (files.Length > 0)
                    return files[0];
            }
        }
        catch (Exception ex)
        {
            LogService.Debug("EXT", () => $"ResolveIconPath failed for '{folder}': {ex.Message}");
        }

        return null;
    }

    private static void EnsureFolders()
    {
        // Clean up empty extension folders that lack a manifest.json
        if (Directory.Exists(InstallRoot))
        {
            foreach (var sub in Directory.GetDirectories(InstallRoot))
            {
                string mf = Path.Combine(sub, "manifest.json");
                if (!File.Exists(mf))
                {
                    try { Directory.Delete(sub, recursive: true); }
                    catch { }
                }
            }
        }

        foreach (var ext in _catalog.Where(e => e.Enabled))
        {
            string dest = Path.Combine(InstallRoot, ext.FolderName);

            if (ext.Source == ExtensionSource.Bundled)
            {
                string src = Path.Combine(BundleCache, ext.FolderName);
                if (Directory.Exists(src)) CopyDirectoryIfChanged(src, dest);
                continue;
            }

            if (!Directory.Exists(dest))
                LogService.Write("EXT", $"WARNING: Extension folder missing for '{ext.Id}'");
        }
    }

    // ── Catalog I/O ──────────────────────────────────────────────────────────

    private static void LoadCatalog()
    {
        if (!File.Exists(CatalogFile)) return;
        try
        {
            _catalog = JsonSerializer.Deserialize<List<ExtensionRecord>>(
                           File.ReadAllText(CatalogFile))
                       ?? new List<ExtensionRecord>();
        }
        catch (Exception ex) { LogService.RecordCrash(ex, "ExtensionService.LoadCatalog"); }
    }

    private static void SaveCatalog()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            File.WriteAllText(CatalogFile,
                JsonSerializer.Serialize(_catalog,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { LogService.RecordCrash(ex, "ExtensionService.SaveCatalog"); }
    }

    // ── Manifest helpers ─────────────────────────────────────────────────────

    internal static string ReadManifestVersion(string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    internal static string ReadManifestName(string manifestPath, string fallback)
    {
        try
        {
            if (!File.Exists(manifestPath)) return fallback;
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            string defaultLocale = root.TryGetProperty("default_locale", out var dl) ? dl.GetString() ?? "en" : "en";

            if (root.TryGetProperty("name", out var n))
            {
                string raw = n.GetString() ?? fallback;
                string dir = Path.GetDirectoryName(manifestPath) ?? "";
                string resolved = ResolveLocaleString(dir, raw, defaultLocale);
                return !string.IsNullOrWhiteSpace(resolved) ? resolved : fallback;
            }
        }
        catch { }
        return fallback;
    }

    internal static string ReadManifestDescription(string manifestPath, string fallback)
    {
        try
        {
            if (!File.Exists(manifestPath)) return fallback;
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            string defaultLocale = root.TryGetProperty("default_locale", out var dl) ? dl.GetString() ?? "en" : "en";

            if (root.TryGetProperty("description", out var d))
            {
                string raw = d.GetString() ?? fallback;
                string dir = Path.GetDirectoryName(manifestPath) ?? "";
                string resolved = ResolveLocaleString(dir, raw, defaultLocale);
                return !string.IsNullOrWhiteSpace(resolved) ? resolved : fallback;
            }
        }
        catch { }
        return fallback;
    }

    internal static string ResolveLocaleString(string manifestDir, string value, string defaultLocale = "en")
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!value.StartsWith("__MSG_") || !value.EndsWith("__") || value.Length <= 8)
            return value;

        string key = value.Substring(6, value.Length - 8);

        string localesDir = Path.Combine(manifestDir, "_locales");
        if (!Directory.Exists(localesDir)) return key;

        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(defaultLocale)) candidates.Add(defaultLocale);
        candidates.Add("en");
        candidates.Add("en_US");
        candidates.Add("en_GB");

        foreach (var c in candidates)
        {
            string msgFile = Path.Combine(localesDir, c, "messages.json");
            if (File.Exists(msgFile))
            {
                string? found = TryLookupMessage(msgFile, key);
                if (!string.IsNullOrEmpty(found)) return found;
            }
        }

        foreach (var sub in Directory.GetDirectories(localesDir))
        {
            string msgFile = Path.Combine(sub, "messages.json");
            if (File.Exists(msgFile))
            {
                string? found = TryLookupMessage(msgFile, key);
                if (!string.IsNullOrEmpty(found)) return found;
            }
        }

        return key;
    }

    private static string? TryLookupMessage(string msgFile, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(msgFile));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.TryGetProperty("message", out var m))
                        return m.GetString();
                }
            }
        }
        catch { }
        return null;
    }

    // ── File utils ───────────────────────────────────────────────────────────

    internal static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    internal static void CopyDirectoryIfChanged(string src, string dest)
    {
        string srcManifest = Path.Combine(src, "manifest.json");
        string destManifest = Path.Combine(dest, "manifest.json");
        if (File.Exists(srcManifest) && File.Exists(destManifest))
        {
            string vSrc = ReadManifestVersion(srcManifest);
            string vDest = ReadManifestVersion(destManifest);
            if (!string.IsNullOrEmpty(vSrc) && vSrc == vDest) return;
        }
        CopyDirectory(src, dest);
    }
}