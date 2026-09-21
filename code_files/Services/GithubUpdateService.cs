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

    private static readonly HttpClient _http = new HttpClient();

    static GithubUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Horizon-Browser-Update-Checker");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public static (string version, string channel) ParseLocalVersion(string raw)
    {
        string version = "0.0.0";
        string channel = "official_release";

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
                    channel = "official_release";
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

            string apiUrl = $"https://api.github.com/repos/{ownerRepo}/releases/latest";
            using var response = await _http.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"GitHub API returned {(int)response.StatusCode}.";
                return result;
            }

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

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
            foreach (var asset in assetsEl.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string nameLower = name.ToLowerInvariant();
                if (nameLower.Contains(localChannel))
                {
                    matchedName = name;
                    matchedUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
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

    private static string ExtractVersionFromText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var parts = text.Split(new[] { '_', ' ', 'v', 'V' }, StringSplitOptions.RemoveEmptyEntries);
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

    public static async Task<string> DownloadAssetAsync(string assetUrl, string assetName)
    {
        string downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloadsFolder)) downloadsFolder = Path.GetTempPath();
        string destPath = Path.Combine(downloadsFolder, assetName);

        using var response = await _http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);

        return destPath;
    }
}