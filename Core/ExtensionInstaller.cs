using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Core;

/// <summary>
/// Identifies which store a URL belongs to and carries the extension ID and display name.
/// </summary>
public class StorePageInfo
{
    public enum StoreKind { None, Chrome, Edge, Firefox }

    public StoreKind Kind        { get; init; }
    public string    ExtensionId { get; init; } = string.Empty;
    public string    Name        { get; init; } = string.Empty;  // Display name parsed from URL slug
    public bool      IsStorePage => Kind != StoreKind.None;
}

public class ExtensionPermissionModel : System.ComponentModel.INotifyPropertyChanged
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "API";
    public bool IsEssential { get; set; }

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public bool CanToggle => !IsEssential;
    public System.Windows.Visibility EssentialBadgeVisibility => IsEssential ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Downloads and installs browser extensions from the Chrome Web Store, Microsoft Edge
/// Add-ons, and Firefox AMO.
///
/// CRX FORMAT
/// ──────────
///   CRX2:  "Cr24" + u32(version=2) + u32(pubkeyLen) + u32(sigLen) + [pubkey] + [sig] + ZIP
///   CRX3:  "Cr24" + u32(version=3) + u32(headerSize) + [protobuf header] + ZIP
///   We skip the header by scanning for the ZIP magic bytes PK\x03\x04 after offset 12.
///
/// XPI FORMAT (Firefox)
///   A plain ZIP — no header stripping needed.
/// </summary>
public static class ExtensionInstaller
{
    // ── Chrome extension ID pattern: 32 lowercase letters a-p ───────────────
    private static readonly Regex ChromeIdRegex = new(@"[a-p]{32}", RegexOptions.Compiled);

    // ── HTTP client (shared, reused) ─────────────────────────────────────────
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
    })
    {
        Timeout = TimeSpan.FromSeconds(60),
        DefaultRequestHeaders = {
            { "User-Agent",
              "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
              "(KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36" }
        }
    };

    // ── Store page detection ─────────────────────────────────────────────────

    /// <summary>
    /// Inspects a URL and returns a StorePageInfo if it is an extension store page.
    /// Call this on every NavigationCompleted to update the sidebar.
    /// </summary>
    public static StorePageInfo Detect(string url)
    {
        if (string.IsNullOrEmpty(url)) return new() { Kind = StorePageInfo.StoreKind.None };

        try
        {
            var uri = new Uri(url);
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath;

            // ── Chrome Web Store ─────────────────────────────────────────────
            // https://chromewebstore.google.com/detail/{name}/{id}
            if (host == "chromewebstore.google.com" && path.StartsWith("/detail/"))
            {
                var segments = path.TrimStart('/').Split('/');
                // segments: ["detail", "{name}", "{id}"]  or  ["detail", "{id}"]
                string id   = segments.Last();
                string name = segments.Length >= 3 ? Slug(segments[^2]) : id;

                if (ChromeIdRegex.IsMatch(id))
                    return new() { Kind = StorePageInfo.StoreKind.Chrome, ExtensionId = id, Name = name };
            }

            // ── Microsoft Edge Add-ons ────────────────────────────────────────
            // https://microsoftedge.microsoft.com/addons/detail/{name}/{id}
            if (host == "microsoftedge.microsoft.com" && path.Contains("/addons/detail/"))
            {
                var segments = path.TrimStart('/').Split('/');
                string id   = segments.Last();
                string name = segments.Length >= 4 ? Slug(segments[^2]) : id;

                if (ChromeIdRegex.IsMatch(id))
                    return new() { Kind = StorePageInfo.StoreKind.Edge, ExtensionId = id, Name = name };
            }

            // ── Firefox Add-ons (AMO) ─────────────────────────────────────────
            // https://addons.mozilla.org/{locale}/firefox/addon/{slug}/
            if ((host == "addons.mozilla.org") &&
                path.Contains("/addon/"))
            {
                // Extract slug — last non-empty segment before trailing slash
                string slug = path.TrimEnd('/').Split('/').LastOrDefault() ?? "";
                if (!string.IsNullOrEmpty(slug))
                    return new() { Kind = StorePageInfo.StoreKind.Firefox, ExtensionId = slug, Name = Slug(slug) };
            }
        }
        catch { /* not a valid URI */ }

        return new() { Kind = StorePageInfo.StoreKind.None };
    }

    // ── Main install entry point ──────────────────────────────────────────────

    /// <summary>
    /// Downloads and extracts the extension into a temporary staging folder so manifest permissions
    /// can be inspected and selectively toggled before final installation.
    /// </summary>
    public static async Task<string?> DownloadAndStageAsync(StorePageInfo info, IProgress<string>? progress = null)
    {
        try
        {
            string stageDir = Path.Combine(Path.GetTempPath(), $"horizon_ext_stage_{info.ExtensionId}_{Guid.NewGuid():N}");
            if (Directory.Exists(stageDir))
            {
                try { Directory.Delete(stageDir, recursive: true); } catch { }
            }
            Directory.CreateDirectory(stageDir);

            string tempFile = Path.Combine(Path.GetTempPath(), $"horizon_ext_{Guid.NewGuid():N}.tmp");
            string downloadUrl = info.Kind switch
            {
                StorePageInfo.StoreKind.Chrome  => BuildChromeCrxUrl(info.ExtensionId),
                StorePageInfo.StoreKind.Edge    => BuildEdgeCrxUrl(info.ExtensionId),
                StorePageInfo.StoreKind.Firefox => await ResolveFirefoxXpiUrl(info.ExtensionId),
                _ => throw new InvalidOperationException("Unknown store kind")
            };

            if (string.IsNullOrEmpty(downloadUrl))
            {
                progress?.Report("Could not resolve download URL.");
                return null;
            }

            progress?.Report($"Fetching from store…");
            using (var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = File.Create(tempFile);
                await response.Content.CopyToAsync(fs);
            }

            progress?.Report("Extracting package…");
            if (info.Kind == StorePageInfo.StoreKind.Firefox)
                ExtractXpi(tempFile, stageDir);
            else
                ExtractCrx(tempFile, stageDir);

            try { File.Delete(tempFile); } catch { }
            return stageDir;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, $"ExtensionInstaller.DownloadAndStageAsync '{info.ExtensionId}'");
            progress?.Report($"Download failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Applies user-selected permissions, copies the staged files to Horizon's extensions directory,
    /// and registers the extension in the catalog.
    /// </summary>
    public static async Task<ExtensionRecord?> FinalizeInstallFromStageAsync(
        string stageDir,
        StorePageInfo info,
        System.Collections.Generic.IEnumerable<string>? disabledKeys = null,
        IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(stageDir)) return null;

                string manifestPath = Path.Combine(stageDir, "manifest.json");
                if (disabledKeys != null && File.Exists(manifestPath))
                {
                    ApplyPermissionChoices(manifestPath, disabledKeys);
                }

                string destFolder = Path.Combine(ConfigService.ExtensionsRoot, info.ExtensionId);
                if (Directory.Exists(destFolder))
                {
                    try { Directory.Delete(destFolder, recursive: true); } catch { }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destFolder)!);

                CopyDirectory(stageDir, destFolder);
                try { Directory.Delete(stageDir, recursive: true); } catch { }

                string finalManifest = Path.Combine(destFolder, "manifest.json");
                string name = File.Exists(finalManifest)
                    ? ExtensionService.ReadManifestName(finalManifest, info.Name)
                    : info.Name;
                string version = File.Exists(finalManifest)
                    ? ExtensionService.ReadManifestVersion(finalManifest)
                    : "";

                var record = new ExtensionRecord
                {
                    Id          = info.ExtensionId,
                    Name        = name,
                    Description = string.Empty,
                    Version     = version,
                    Icon        = "🧩",
                    Source      = info.Kind switch
                    {
                        StorePageInfo.StoreKind.Chrome  => ExtensionSource.ChromeStore,
                        StorePageInfo.StoreKind.Edge    => ExtensionSource.EdgeStore,
                        StorePageInfo.StoreKind.Firefox => ExtensionSource.FirefoxStore,
                        _ => ExtensionSource.Manual
                    },
                    Enabled     = true,
                    InstalledAt = DateTime.Now,
                };

                if (ExtensionService.ModifiesShortcuts(destFolder, record.Id, record.Name, record.Description))
                {
                    bool allowed = ExtensionService.PromptShortcutPermission(record.Id, record.Name);
                    record.AllowsShortcutModification = allowed;
                    record.Enabled = allowed;
                }

                ExtensionService.Register(record);
                progress?.Report($"{name} installed!");
                return record;
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, $"ExtensionInstaller.FinalizeInstallFromStageAsync '{info.ExtensionId}'");
                progress?.Report($"Install failed: {ex.Message}");
                return null;
            }
        });
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;
        Directory.CreateDirectory(destinationDir);
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }
        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    /// <summary>
    /// Downloads and installs an extension identified by <paramref name="info"/>.
    /// Returns the created ExtensionRecord on success, null on failure.
    /// </summary>
    public static async Task<ExtensionRecord?> InstallAsync(
        StorePageInfo info,
        IProgress<string>? progress = null)
    {
        string? stageDir = await DownloadAndStageAsync(info, progress);
        if (stageDir == null) return null;
        return await FinalizeInstallFromStageAsync(stageDir, info, null, progress);
    }

    /// <summary>
    /// Installs a local .crx file directly into Horizon's extension directory and registers it in the catalog.
    /// </summary>
    public static async Task<ExtensionRecord?> InstallFromCrxFileAsync(
        string crxPath,
        string? suggestedName = null,
        IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(crxPath)) return null;

                progress?.Report("Extracting CRX package…");
                string extId = Path.GetFileNameWithoutExtension(crxPath)
                                   .ToLowerInvariant()
                                   .Replace(' ', '_');

                foreach (var ch in Path.GetInvalidFileNameChars()) extId = extId.Replace(ch, '_');
                if (extId.Length > 64) extId = extId[..64];

                string destFolder = Path.Combine(ConfigService.ExtensionsRoot, extId);
                if (Directory.Exists(destFolder))
                {
                    try { Directory.Delete(destFolder, recursive: true); } catch { }
                }
                Directory.CreateDirectory(destFolder);

                ExtractCrx(crxPath, destFolder);

                string manifestPath = Path.Combine(destFolder, "manifest.json");
                string name = File.Exists(manifestPath)
                    ? ExtensionService.ReadManifestName(manifestPath, suggestedName ?? extId)
                    : (suggestedName ?? extId);

                string version = File.Exists(manifestPath)
                    ? ExtensionService.ReadManifestVersion(manifestPath)
                    : "1.0.0";

                var record = new ExtensionRecord
                {
                    Id          = extId,
                    Name        = name,
                    Description = string.Empty,
                    Version     = version,
                    Source      = ExtensionSource.ChromeStore,
                    Enabled     = true,
                    InstalledAt = DateTime.Now
                };

                if (ExtensionService.ModifiesShortcuts(destFolder, record.Id, record.Name, record.Description))
            {
                bool allowed = ExtensionService.PromptShortcutPermission(record.Id, record.Name);
                record.AllowsShortcutModification = allowed;
                record.Enabled = allowed;
            }

            ExtensionService.Register(record);
                progress?.Report($"Extension '{name}' successfully installed!");
                return record;
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "ExtensionInstaller.InstallFromCrxFileAsync");
                progress?.Report($"Installation failed: {ex.Message}");
                return null;
            }
        });
    }

    // ── CRX download URLs ─────────────────────────────────────────────────────

    private static string BuildChromeCrxUrl(string id) =>
        $"https://clients2.google.com/service/update2/crx" +
        $"?response=redirect&acceptformat=crx2,crx3" +
        $"&x=id%3D{id}%26uc&prodversion=130.0.0.0";

    private static string BuildEdgeCrxUrl(string id) =>
        $"https://edge.microsoft.com/extensionwebstorebase/v1/crx" +
        $"?response=redirect&acceptformat=crx3,crx2" +
        $"&x=id%3D{id}%26uc" +
        $"&updatesurl=https%3A%2F%2Fedge.microsoft.com%2Fupdates%2Fcrx" +
        $"&prodversion=130.0.0.0";

    private static async Task<string> ResolveFirefoxXpiUrl(string slug)
    {
        // AMO public API
        string apiUrl = $"https://addons.mozilla.org/api/v5/addons/addon/{slug}/";
        using var resp = await _http.GetAsync(apiUrl);
        if (!resp.IsSuccessStatusCode) return string.Empty;

        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Navigate: current_version → file → url
        if (doc.RootElement.TryGetProperty("current_version", out var cv) &&
            cv.TryGetProperty("file", out var file) &&
            file.TryGetProperty("url", out var urlProp))
        {
            return urlProp.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    // ── CRX extraction ────────────────────────────────────────────────────────

    private static void ExtractCrx(string crxPath, string destFolder)
    {
        using var fs = File.OpenRead(crxPath);
        using var reader = new BinaryReader(fs);

        // Validate magic "Cr24"
        byte[] magic = reader.ReadBytes(4);
        if (magic[0] != 'C' || magic[1] != 'r' || magic[2] != '2' || magic[3] != '4')
            throw new InvalidDataException("Not a valid CRX file (bad magic bytes).");

        uint version = reader.ReadUInt32();

        if (version == 2)
        {
            uint pubKeyLen = reader.ReadUInt32();
            uint sigLen    = reader.ReadUInt32();
            fs.Seek(pubKeyLen + sigLen, SeekOrigin.Current);
        }
        else if (version == 3)
        {
            uint headerSize = reader.ReadUInt32();
            fs.Seek(headerSize, SeekOrigin.Current);
        }
        else
        {
            // Unknown version — scan forward for ZIP PK magic
            fs.Seek(0, SeekOrigin.Begin);
            SeekToZip(fs);
        }

        // fs is now positioned at the start of the embedded ZIP
        ExtractZipFromStream(fs, destFolder);
    }

    private static void SeekToZip(Stream stream)
    {
        // Scan for PK\x03\x04 (local file header signature)
        int b;
        while ((b = stream.ReadByte()) != -1)
        {
            if (b != 'P') continue;
            long pos = stream.Position - 1;
            byte[] next = new byte[3];
            if (stream.Read(next, 0, 3) == 3 &&
                next[0] == 'K' && next[1] == 0x03 && next[2] == 0x04)
            {
                stream.Seek(pos, SeekOrigin.Begin);
                return;
            }
            stream.Seek(pos + 1, SeekOrigin.Begin);
        }
        throw new InvalidDataException("Could not locate embedded ZIP in CRX file.");
    }

    private static void ExtractXpi(string xpiPath, string destFolder)
    {
        // XPI is a straight ZIP
        using var fs = File.OpenRead(xpiPath);
        ExtractZipFromStream(fs, destFolder);
    }

    private static void ExtractZipFromStream(Stream zipStream, string destFolder)
    {
        Directory.CreateDirectory(destFolder);

        using var ms = new MemoryStream();
        zipStream.CopyTo(ms);
        ms.Seek(0, SeekOrigin.Begin);

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            string destPath = Path.GetFullPath(Path.Combine(destFolder, entry.FullName));

            // Zip-slip protection
            if (!destPath.StartsWith(Path.GetFullPath(destFolder) + Path.DirectorySeparatorChar))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Converts a URL slug like "ublock-origin" → "uBlock Origin".</summary>
    private static string Slug(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return slug;
        // Replace hyphens/underscores with spaces and title-case each word
        string spaced = slug.Replace('-', ' ').Replace('_', ' ');
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(spaced);
    }

    public static System.Collections.Generic.List<ExtensionPermissionModel> ParseManifestPermissionsDetailed(string manifestPath)
    {
        var list = new System.Collections.Generic.List<ExtensionPermissionModel>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(manifestPath)) return list;
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;

            void AddItem(string key, bool isHost)
            {
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key)) return;
                list.Add(CreatePermissionModel(key, isHost));
            }

            if (root.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in perms.EnumerateArray())
                {
                    string? s = p.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) AddItem(s, isHost: false);
                }
            }

            if (root.TryGetProperty("optional_permissions", out var optPerms) && optPerms.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in optPerms.EnumerateArray())
                {
                    string? s = p.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) AddItem(s, isHost: false);
                }
            }

            if (root.TryGetProperty("host_permissions", out var hPerms) && hPerms.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in hPerms.EnumerateArray())
                {
                    string? s = p.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) AddItem(s, isHost: true);
                }
            }

            if (root.TryGetProperty("optional_host_permissions", out var optHPerms) && optHPerms.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in optHPerms.EnumerateArray())
                {
                    string? s = p.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) AddItem(s, isHost: true);
                }
            }

            if (root.TryGetProperty("content_scripts", out var cs) && cs.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cs.EnumerateArray())
                {
                    if (item.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in matches.EnumerateArray())
                        {
                            string? s = m.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) AddItem(s, isHost: true);
                        }
                    }
                }
            }
        }
        catch { }

        return list;
    }

    public static ExtensionPermissionModel CreatePermissionModel(string key, bool isHost)
    {
        if (isHost)
        {
            bool isAll = key == "<all_urls>" || key == "*://*/*" || key == "http://*/*" || key == "https://*/*";
            return new ExtensionPermissionModel
            {
                Key = key,
                Category = "Host",
                DisplayName = isAll ? "All Websites" : (key.Length > 35 ? key.Substring(0, 32) + "…" : key),
                Description = isAll ? "Read and modify data across all visited websites" : $"Read and modify web page data on {key}",
                IsEssential = !isAll && !key.Contains("*"),
                IsSelected = true
            };
        }

        string lk = key.ToLowerInvariant();
        return lk switch
        {
            "tabs" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Browser Tabs",
                Description = "Read tab URLs, titles, and manage open tabs",
                IsEssential = false,
                IsSelected = true
            },
            "activetab" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Active Tab Interaction",
                Description = "Temporary access to the active tab when invoked",
                IsEssential = true,
                IsSelected = true
            },
            "storage" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Extension Storage",
                Description = "Store application settings and local cache",
                IsEssential = true,
                IsSelected = true
            },
            "unlimitedstorage" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Unlimited Storage",
                Description = "Store an unrestricted volume of local client data",
                IsEssential = false,
                IsSelected = true
            },
            "scripting" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Script Execution",
                Description = "Inject and execute helper scripts in web pages",
                IsEssential = false,
                IsSelected = true
            },
            "notifications" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Desktop Notifications",
                Description = "Display system notifications and badge alerts",
                IsEssential = false,
                IsSelected = true
            },
            "clipboardread" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Clipboard Read",
                Description = "Read copied data and images from your clipboard",
                IsEssential = false,
                IsSelected = true
            },
            "clipboardwrite" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Clipboard Write",
                Description = "Copy text and media into your clipboard",
                IsEssential = false,
                IsSelected = true
            },
            "audio" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Tab Audio",
                Description = "Monitor and process audio played by websites",
                IsEssential = false,
                IsSelected = true
            },
            "tabcapture" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Tab Media Capture",
                Description = "Capture media streams directly from tabs",
                IsEssential = false,
                IsSelected = true
            },
            "webrequest" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Web Requests",
                Description = "Inspect network requests initiated by browser tabs",
                IsEssential = false,
                IsSelected = true
            },
            "webrequestblocking" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Request Blocking",
                Description = "Block or redirect HTTP requests (e.g. ad-blocking)",
                IsEssential = false,
                IsSelected = true
            },
            "declarativenetrequest" or "declarativenetrequestwithhostaccess" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Declarative Net Request",
                Description = "Block or filter network traffic declaratively",
                IsEssential = true,
                IsSelected = true
            },
            "cookies" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Website Cookies",
                Description = "Read and modify cookies for websites",
                IsEssential = false,
                IsSelected = true
            },
            "management" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Extension Management",
                Description = "Enumerate and control other installed extensions",
                IsEssential = false,
                IsSelected = true
            },
            "alarms" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Background Alarms",
                Description = "Schedule periodic background update tasks",
                IsEssential = true,
                IsSelected = true
            },
            "contextmenus" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Context Menus",
                Description = "Add custom items to right-click menus",
                IsEssential = false,
                IsSelected = true
            },
            "downloads" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "File Downloads",
                Description = "Initiate and track downloaded files",
                IsEssential = false,
                IsSelected = true
            },
            "bookmarks" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Bookmarks Access",
                Description = "Read and organize browser bookmarks",
                IsEssential = false,
                IsSelected = true
            },
            "history" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Browsing History",
                Description = "Read and search visited page history",
                IsEssential = false,
                IsSelected = true
            },
            "geolocation" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Geolocation",
                Description = "Determine your physical geographical coordinates",
                IsEssential = false,
                IsSelected = true
            },
            "offscreen" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Offscreen Processing",
                Description = "Spawn offscreen DOM documents for background tasks",
                IsEssential = true,
                IsSelected = true
            },
            "sidepanel" => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = "Side Panel",
                Description = "Render extension UI inside the browser side panel",
                IsEssential = true,
                IsSelected = true
            },
            _ => new ExtensionPermissionModel
            {
                Key = key,
                DisplayName = key,
                Description = $"Capability permission requested by extension: {key}",
                IsEssential = false,
                IsSelected = true
            }
        };
    }

    public static void ApplyPermissionChoices(string manifestPath, System.Collections.Generic.IEnumerable<string> disabledKeys)
    {
        try
        {
            if (!File.Exists(manifestPath)) return;
            var disabledSet = new System.Collections.Generic.HashSet<string>(disabledKeys, StringComparer.OrdinalIgnoreCase);
            if (disabledSet.Count == 0) return;

            string json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var dict = new System.Collections.Generic.Dictionary<string, object?>();
            foreach (var prop in root.EnumerateObject())
            {
                if ((prop.Name == "permissions" || prop.Name == "optional_permissions") && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var kept = prop.Value.EnumerateArray()
                        .Select(p => p.GetString())
                        .Where(s => !string.IsNullOrEmpty(s) && !disabledSet.Contains(s))
                        .ToList();
                    dict[prop.Name] = kept;
                }
                else if ((prop.Name == "host_permissions" || prop.Name == "optional_host_permissions") && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var kept = prop.Value.EnumerateArray()
                        .Select(p => p.GetString())
                        .Where(s => !string.IsNullOrEmpty(s) && !disabledSet.Contains(s))
                        .ToList();
                    dict[prop.Name] = kept;
                }
                else
                {
                    dict[prop.Name] = JsonElementToValue(prop.Value);
                }
            }

            string updated = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, updated);
            LogService.Write("EXT", $"Applied permission choices; stripped {disabledSet.Count} permissions: {string.Join(", ", disabledSet)}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ApplyPermissionChoices");
        }
    }

    private static object? JsonElementToValue(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.String => elem.GetString(),
        JsonValueKind.Number => elem.TryGetInt64(out var l) ? l : elem.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => elem.EnumerateArray().Select(JsonElementToValue).ToList(),
        JsonValueKind.Object => elem.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToValue(p.Value)),
        _ => null
    };

    public static System.Collections.Generic.List<string> ParseManifestPermissions(string manifestPath)
    {
        return ParseManifestPermissionsDetailed(manifestPath).Select(p => $"{p.DisplayName}: {p.Description}").ToList();
    }
}