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
            ["consent-o-matic"] = ("🍪", "Automatically handles GDPR/cookie consent dialogs."),
            ["adguard"]         = ("🛡",  "Blocks ads, trackers, and malware on every site."),
            ["sponsorblock-for-youtube-skip-sponsorships-chrome-web-store"] = ("⏭", "Skip sponsorships, intro animations, and annoying segments on YouTube."),
            ["sponsorblock"]    = ("⏭", "Skip sponsorships, intro animations, and annoying segments on YouTube."),
            ["linguist-web-page-translator-chrome-web-store"] = ("🌐", "Translate full web pages and selected text instantly."),
            ["linguist"]        = ("🌐", "Translate full web pages and selected text instantly."),
            ["twp-translate-[beta]-chrome-web-store"] = ("🈯", "Translate web pages in real-time."),
            ["twp-translate"]   = ("🈯", "Translate web pages in real-time."),
            ["remove-paywalls-chrome-web-store"] = ("🔓", "Read articles cleanly without paywalls or nag screens."),
            ["remove-paywalls"] = ("🔓", "Read articles cleanly without paywalls or nag screens."),
            ["save-it-later-chrome-web-store"] = ("💾", "Save articles and bookmarks to read offline later."),
            ["save-it-later"]   = ("💾", "Save articles and bookmarks to read offline later."),
            ["crx-extractor-downloader-chrome-web-store"] = ("📦", "Download and unpack Chrome extensions as ZIP/CRX."),
            ["crx-extractor"]   = ("📦", "Download and unpack Chrome extensions as ZIP/CRX."),
        };

    private static (string Icon, string Description) GetBundledMeta(string id)
    {
        if (_bundledMeta.TryGetValue(id, out var meta)) return meta;
        string lower = id.ToLowerInvariant();
        if (lower.Contains("consent")) return ("🍪", "Automatically handles GDPR/cookie consent dialogs.");
        if (lower.Contains("adguard")) return ("🛡", "Blocks ads, trackers, and malware on every site.");
        if (lower.Contains("sponsorblock")) return ("⏭", "Skip sponsorships, intro animations, and annoying segments on YouTube.");
        if (lower.Contains("linguist")) return ("🌐", "Translate full web pages and selected text instantly.");
        if (lower.Contains("twp") || lower.Contains("translate")) return ("🈯", "Translate web pages in real-time.");
        if (lower.Contains("paywall")) return ("🔓", "Read articles cleanly without paywalls or nag screens.");
        if (lower.Contains("save")) return ("💾", "Save articles and bookmarks to read offline later.");
        if (lower.Contains("crx")) return ("📦", "Download and unpack Chrome extensions as ZIP/CRX.");
        return ("🧩", string.Empty);
    }

    // ── Paths ────────────────────────────────────────────────────────────────
    private static string DataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Horizon_Browser", "HorizonData");

    private static string CatalogFile => Path.Combine(DataRoot, "extensions.json");

    public static string InstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Horizon_Browser", "Extensions");

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

            var existing = Find(id);
            if (existing != null)
            {
                existing.Version = version;
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
                Icon        = meta.Icon ?? "🧩",
                Source      = ExtensionSource.Bundled,
                Enabled     = true,
                InstalledAt = DateTime.Now,
            });
        }
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