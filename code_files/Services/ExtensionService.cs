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
        };

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

            SyncBundledExtensions();
            LoadCatalog();
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
        _catalog.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string GetInstallFolder(string id) => Path.Combine(InstallRoot, id);

    // ── Internal ─────────────────────────────────────────────────────────────

    private static void SyncBundledExtensions()
    {
        if (!Directory.Exists(AppBundleRoot)) return;
        foreach (var src in Directory.GetDirectories(AppBundleRoot))
        {
            string id   = Path.GetFileName(src);
            string dest = Path.Combine(BundleCache, id);
            CopyDirectory(src, dest);
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

            var existing = Find(id);
            if (existing != null) { existing.Version = ReadManifestVersion(manifest); continue; }

            _bundledMeta.TryGetValue(id, out var meta);

            _catalog.Add(new ExtensionRecord
            {
                Id          = id,
                Name        = ReadManifestName(manifest, id),
                Description = meta.Description ?? string.Empty,
                Version     = ReadManifestVersion(manifest),
                Icon        = meta.Icon ?? "🧩",
                Source      = ExtensionSource.Bundled,
                Enabled     = true,
                InstalledAt = DateTime.Now,
            });
        }
    }

    private static void EnsureFolders()
    {
        foreach (var ext in _catalog.Where(e => e.Enabled))
        {
            string dest = Path.Combine(InstallRoot, ext.FolderName);

            if (ext.Source == ExtensionSource.Bundled)
            {
                string src = Path.Combine(BundleCache, ext.FolderName);
                if (Directory.Exists(src)) CopyDirectory(src, dest);
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
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.TryGetProperty("name", out var n))
            {
                string name = n.GetString() ?? fallback;
                return name.StartsWith("__MSG_") ? fallback : name;
            }
        }
        catch { }
        return fallback;
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
}