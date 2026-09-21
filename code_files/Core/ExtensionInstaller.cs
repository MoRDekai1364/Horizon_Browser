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
    /// Downloads and installs an extension identified by <paramref name="info"/>.
    /// Returns the created ExtensionRecord on success, null on failure.
    /// </summary>
    public static async Task<ExtensionRecord?> InstallAsync(
        StorePageInfo info,
        IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Downloading {info.Name}…");

            string tempFile = Path.Combine(Path.GetTempPath(), $"horizon_ext_{Guid.NewGuid():N}.tmp");
            string destFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extensions", info.ExtensionId);

            // ── Download ──────────────────────────────────────────────────────
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

            // ── Extract ───────────────────────────────────────────────────────
            progress?.Report("Extracting extension…");

            if (info.Kind == StorePageInfo.StoreKind.Firefox)
                ExtractXpi(tempFile, destFolder);
            else
                ExtractCrx(tempFile, destFolder);

            File.Delete(tempFile);

            // ── Build catalog record ──────────────────────────────────────────
            string manifestPath = Path.Combine(destFolder, "manifest.json");
            string name    = File.Exists(manifestPath)
                           ? ExtensionService.ReadManifestName(manifestPath, info.Name)
                           : info.Name;
            string version = File.Exists(manifestPath)
                           ? ExtensionService.ReadManifestVersion(manifestPath)
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

            ExtensionService.Register(record);
            progress?.Report($"{name} installed! Restart to activate.");
            return record;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, $"ExtensionInstaller.InstallAsync '{info.ExtensionId}'");
            progress?.Report($"Install failed: {ex.Message}");
            return null;
        }
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
}