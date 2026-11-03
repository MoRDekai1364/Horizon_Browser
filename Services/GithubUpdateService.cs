using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Stealth.Services;

public class UpdateCheckResult
{
    public bool UpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string CurrentChannel { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string? AssetUrl { get; set; }
    public string? AssetName { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class GithubUpdateService
{
    public static event Action<string>? UpdateReadyToInstall;

    public static void NotifyUpdateReady(string installerPath)
    {
        UpdateReadyToInstall?.Invoke(installerPath);
    }

    private static readonly HttpClient _apiClient = new HttpClient();
    private static readonly HttpClient _downloadClient = new HttpClient();

    static GithubUpdateService()
    {
        _apiClient.DefaultRequestHeaders.UserAgent.ParseAdd("Horizon-Browser-Update-Checker");
        _apiClient.Timeout = TimeSpan.FromSeconds(15);

        _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("Horizon-Browser-Update-Downloader");
        _downloadClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }

    public static (string version, string channel) ParseLocalVersion(string raw)
    {
        string version = "0.0.0";
        string channel = "release";

        string[] lines = raw.Split('\n');

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string candidate = trimmed;
            if (trimmed.StartsWith("version", StringComparison.OrdinalIgnoreCase))
                candidate = trimmed.Substring(7).Trim();

            string[] parts = candidate.Split('.');
            bool allNumeric = parts.Length > 0;
            foreach (string p in parts)
                if (!int.TryParse(p.Trim(), out _)) { allNumeric = false; break; }

            if (allNumeric)
                version = parts.Length == 1 ? parts[0].Trim() + ".0" : string.Join(".", Array.ConvertAll(parts, p => p.Trim()));
        }

        foreach (string line in lines)
        {
            string trimmed = line.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string[] words = trimmed.Split(new[] { ' ', '\t', '_' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string word in words)
            {
                if (word == "alpha" || word == "beta" || word == "rc")
                    channel = word;
                else if (word == "official" || word == "release" || word == "stable")
                    channel = "release";
            }
        }

        return (version, channel);
    }

    private static string? ExtractOwnerRepo(string repoUrl)
    {
        try
        {
            var uri = new Uri(repoUrl.Trim());
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2) return $"{segments[0]}/{segments[1]}";
        }
        catch { }
        return null;
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(string updateTxtPath, string versionTxtPath)
    {
        var result = new UpdateCheckResult();

        try
        {
            string localRaw = File.ReadAllText(versionTxtPath).Trim();
            var (localVersion, localChannel) = ParseLocalVersion(localRaw);
            result.CurrentVersion = localVersion;
            result.CurrentChannel = localChannel;

            string repoUrl = File.ReadAllText(updateTxtPath).Trim();
            string? ownerRepo = ExtractOwnerRepo(repoUrl);
            if (ownerRepo == null)
            {
                result.ErrorMessage = "Could not parse repository URL from update.txt.";
                return result;
            }

            string apiUrl = $"https://api.github.com/repos/{ownerRepo}/releases?per_page=30";
            using var response = await _apiClient.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"GitHub API returned {(int)response.StatusCode}.";
                return result;
            }

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            JsonElement root = default;
            bool releaseFound = false;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    if (rel.TryGetProperty("draft", out var draftEl) && draftEl.ValueKind == JsonValueKind.True) continue;
                    bool isPre = rel.TryGetProperty("prerelease", out var preEl) && preEl.ValueKind == JsonValueKind.True;
                    if (isPre && localChannel == "release") continue;
                    if (!ReleaseHasChannelAsset(rel, localChannel)) continue;
                    root = rel;
                    releaseFound = true;
                    break;
                }
            }
            if (!releaseFound)
            {
                result.ErrorMessage = $"No release found for channel '{localChannel}'.";
                return result;
            }

            string tagName = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            string remoteVersion = ExtractVersionFromText(tagName);
            if (string.IsNullOrEmpty(remoteVersion))
            {
                string releaseName = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                remoteVersion = ExtractVersionFromText(releaseName);
            }
            result.LatestVersion = string.IsNullOrEmpty(remoteVersion) ? "UNKNOWN" : remoteVersion;

            if (!root.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
            {
                result.ErrorMessage = "Latest release has no assets.";
                return result;
            }

            string? matchedUrl = null;
            string? matchedName = null;
            int bestRank = int.MaxValue;
            foreach (var asset in assetsEl.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string nameLower = name.ToLowerInvariant();
                if (!MatchesChannel(nameLower, localChannel)) continue;
                int rank = RankAsset(nameLower);
                if (rank < 0 || rank >= bestRank) continue;
                string? assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(assetUrl)) continue;
                bestRank = rank;
                matchedName = name;
                matchedUrl = assetUrl;
            }

            if (matchedUrl == null)
            {
                result.ErrorMessage = $"No release asset found matching channel '{localChannel}'.";
                return result;
            }

            result.AssetUrl = matchedUrl;
            result.AssetName = matchedName;

            if (string.IsNullOrEmpty(remoteVersion))
            {
                result.ErrorMessage = "Could not determine version number from release tag.";
                return result;
            }

            result.UpdateAvailable = CompareVersions(remoteVersion, localVersion) > 0;
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz" };

    private static bool MatchesChannel(string nameLower, string channel)
    {
        if (channel != "release") return nameLower.Contains(channel);
        if (nameLower.Contains("alpha") || nameLower.Contains("beta") || nameLower.Contains("rc") || nameLower.Contains("nightly") || nameLower.Contains("dev"))
            return false;

        string ext = Path.GetExtension(nameLower);
        if (ext == ".exe" || ext == ".msi" || Array.IndexOf(ArchiveExtensions, ext) >= 0)
            return true;

        return nameLower.Contains("release") || nameLower.Contains("official") || nameLower.Contains("stable") || nameLower.Contains("setup");
    }

    private static bool ReleaseHasChannelAsset(JsonElement release, string channel)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return false;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string nameLower = name.ToLowerInvariant();
            if (MatchesChannel(nameLower, channel) && RankAsset(nameLower) >= 0) return true;
        }
        return false;
    }

    private static int RankAsset(string nameLower)
    {
        string ext = Path.GetExtension(nameLower);
        bool setupLike = nameLower.Contains("setup") || nameLower.Contains("install");
        if (ext == ".exe") return setupLike ? 0 : 1;
        if (ext == ".msi") return 2;
        if (Array.IndexOf(ArchiveExtensions, ext) >= 0) return 3;
        return -1;
    }

    private static string ExtractVersionFromText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var parts = text.Split(new[] { '_', ' ', '-', 'v', 'V' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var nums = part.Split('.');
            bool allNumeric = nums.Length >= 2;
            foreach (var n in nums)
                if (!int.TryParse(n, out _)) { allNumeric = false; break; }
            if (allNumeric) return part;
        }
        return "";
    }

    private static int CompareVersions(string a, string b)
    {
        try
        {
            var pa = a.Split('.').Select(p => int.TryParse(p, out int v) ? v : 0).ToArray();
            var pb = b.Split('.').Select(p => int.TryParse(p, out int v) ? v : 0).ToArray();
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length ? pa[i] : 0;
                int vb = i < pb.Length ? pb[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }
        catch { return 0; }
    }

    public static async Task<string> DownloadAssetAsync(
        string assetUrl,
        string assetName,
        IProgress<(long downloadedBytes, long totalBytes, double percent)>? progress = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        string downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloadsFolder)) downloadsFolder = Path.GetTempPath();

        string safeFileName = Path.GetFileName(assetName);
        string destPath = Path.Combine(downloadsFolder, safeFileName);
        string stagingPath = destPath + ".downloading";

        // Clean up any stale partial staging download
        try
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }
        catch { }

        try
        {
            using var response = await _downloadClient.GetAsync(
                assetUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (var fileStream = new FileStream(
                stagingPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalBytesRead += bytesRead;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double pct = Math.Clamp((double)totalBytesRead / totalBytes.Value * 100.0, 0.0, 100.0);
                        progress?.Report((totalBytesRead, totalBytes.Value, pct));
                    }
                    else
                    {
                        progress?.Report((totalBytesRead, -1, 0.0));
                    }
                }

                await fileStream.FlushAsync(cancellationToken);

                // Verify size integrity against Content-Length
                if (totalBytes.HasValue && totalBytes.Value > 0 && totalBytesRead != totalBytes.Value)
                {
                    throw new InvalidDataException(
                        $"Downloaded size ({totalBytesRead} bytes) does not match expected Content-Length ({totalBytes.Value} bytes). File is corrupted or incomplete.");
                }

                if (totalBytesRead < 1024)
                {
                    throw new InvalidDataException($"Downloaded file is too small ({totalBytesRead} bytes) to be a valid installer.");
                }
            }

            // Verify binary integrity (valid PE / Zip header)
            if (!VerifyInstallerIntegrity(stagingPath))
            {
                throw new InvalidDataException("The downloaded update package failed binary integrity verification (corrupted PE header).");
            }

            // Remove existing destination file if present
            if (File.Exists(destPath))
            {
                try { File.Delete(destPath); } catch { }
            }

            // Move verified staging file to final destination
            File.Move(stagingPath, destPath, overwrite: true);

            // Remove Mark of the Web (Zone.Identifier) so Windows / Antivirus doesn't block execution
            TryUnblockFile(destPath);

            return destPath;
        }
        catch
        {
            // Clean up partial staging file so corrupted files never linger
            try
            {
                if (File.Exists(stagingPath)) File.Delete(stagingPath);
            }
            catch { }
            throw;
        }
    }

    public static bool VerifyInstallerIntegrity(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;

            var fi = new FileInfo(filePath);
            // Horizon installers are at least 1 MB
            if (fi.Length < 1024 * 1024) return false;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 64) return false;

            byte[] header = new byte[64];
            int read = fs.Read(header, 0, 64);
            if (read < 64) return false;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".zip")
            {
                // ZIP magic bytes: 'P', 'K', 0x03, 0x04
                return header[0] == 0x50 && header[1] == 0x4B;
            }

            if (ext == ".msi")
            {
                // MSI compound document header: 0xD0, 0xCF, 0x11, 0xE0
                return header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0xD0 && header[3] == 0xCF ||
                       header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0;
            }

            // Standard PE executable (.exe)
            // 'M', 'Z' DOS signature
            if (header[0] != 0x4D || header[1] != 0x5A) return false;

            // Offset to PE header is at 0x3C
            int peOffset = BitConverter.ToInt32(header, 0x3C);
            if (peOffset <= 0 || peOffset + 4 > fs.Length) return false;

            fs.Seek(peOffset, SeekOrigin.Begin);
            byte[] peSig = new byte[4];
            if (fs.Read(peSig, 0, 4) < 4) return false;

            // 'P', 'E', 0x00, 0x00
            return peSig[0] == 0x50 && peSig[1] == 0x45 && peSig[2] == 0x00 && peSig[3] == 0x00;
        }
        catch
        {
            return false;
        }
    }

    public static void TryUnblockFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows() && File.Exists(path))
            {
                // Deletes the Zone.Identifier alternate data stream on NTFS
                DeleteFile(path + ":Zone.Identifier");
            }
        }
        catch { }
    }

    public static string ResolveAppropriateInstallLocation(string channel, string? currentBaseDir = null)
    {
        // 1. User-configured override in Settings
        if (!string.IsNullOrWhiteSpace(SettingsService.Current.AutoInstallLocation) &&
            Directory.Exists(SettingsService.Current.AutoInstallLocation))
        {
            return SettingsService.Current.AutoInstallLocation;
        }

        string baseDir = (currentBaseDir ?? AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/');

        // 2. If the current running directory contains Horizon.Browser.exe, update this active instance in-place!
        if (File.Exists(Path.Combine(baseDir, "Horizon.Browser.exe")))
        {
            return baseDir;
        }

        // If running from a subfolder like bin\Release, check parent folder for Horizon.Browser.exe
        string? parentDir = Directory.GetParent(baseDir)?.FullName?.TrimEnd('\\', '/');
        if (parentDir != null && File.Exists(Path.Combine(parentDir, "Horizon.Browser.exe")))
        {
            return parentDir;
        }

        // 3. Check discovered existing installations on the system
        try
        {
            var discovered = InstallationDiscoveryService.DiscoverInstallations();
            var match = discovered.FirstOrDefault(d => File.Exists(Path.Combine(d.Path, "Horizon.Browser.exe")));
            if (match != null && Directory.Exists(match.Path))
                return match.Path;
        }
        catch { }

        // 4. Check Daily Driver location (%LOCALAPPDATA%\Programs\Horizon_Browser)
        string localAppDataPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Horizon_Browser");
        if (File.Exists(Path.Combine(localAppDataPrograms, "Horizon.Browser.exe")))
        {
            return localAppDataPrograms;
        }

        // 5. Check standard Program Files installations if they actually exist
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesHorizon = Path.Combine(progFiles, "Horizon_Browser");
        if (File.Exists(Path.Combine(progFilesHorizon, "Horizon.Browser.exe")))
        {
            return progFilesHorizon;
        }

        string progFilesOfficial = Path.Combine(progFiles, "Horizon_Browser_Official_Release");
        if (File.Exists(Path.Combine(progFilesOfficial, "Horizon.Browser.exe")))
        {
            return progFilesOfficial;
        }

        // 6. Fallback to current baseDir
        return baseDir;
    }

    public static bool ShouldCheckForUpdatesNow()
    {
        if (!SettingsService.Current.SilentUpdateCheckEnabled) return false;

        string freq = SettingsService.Current.UpdateCheckFrequency?.Trim() ?? "Weekly";
        if (freq.Equals("Manual", StringComparison.OrdinalIgnoreCase) ||
            freq.Equals("Never", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check if snooze is active
        if (SettingsService.Current.SnoozeUntilDate.HasValue)
        {
            if (DateTime.UtcNow < SettingsService.Current.SnoozeUntilDate.Value)
            {
                return false;
            }
            SettingsService.Current.SnoozeUntilDate = null;
            SettingsService.Save();
        }

        DateTime lastCheck = SettingsService.Current.LastUpdateCheckTime ?? DateTime.MinValue;
        TimeSpan elapsed = DateTime.UtcNow - lastCheck;
        TimeSpan requiredInterval = freq switch
        {
            "Daily" => TimeSpan.FromDays(1),
            "Monthly" => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(7) // Weekly default
        };

        return elapsed >= requiredInterval;
    }

    public static void RecordUpdateCheckCompleted()
    {
        string freq = SettingsService.Current.UpdateCheckFrequency?.Trim() ?? "Weekly";
        TimeSpan interval = freq switch
        {
            "Daily" => TimeSpan.FromDays(1),
            "Monthly" => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(7)
        };

        SettingsService.Current.LastUpdateCheckTime = DateTime.UtcNow;
        SettingsService.Current.NextUpdateCheckTime = DateTime.UtcNow.Add(interval);
        SettingsService.Save();
    }

    public static void SnoozeUpdates(string duration, string currentVersion)
    {
        switch (duration.ToLowerInvariant())
        {
            case "week":
                SettingsService.Current.SnoozeUntilDate = DateTime.UtcNow.AddDays(7);
                break;
            case "month":
                SettingsService.Current.SnoozeUntilDate = DateTime.UtcNow.AddDays(30);
                break;
            case "year":
                SettingsService.Current.SnoozeUntilDate = DateTime.UtcNow.AddDays(365);
                break;
            case "never":
                SettingsService.Current.SkippedUpdateVersion = currentVersion;
                SettingsService.Current.SnoozeUntilDate = null;
                break;
        }
        SettingsService.Save();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteFile(string lpFileName);
}