using System;
using System.IO;
using System.Security.Cryptography;

namespace Horizon.Stealth.Services;

/// <summary>
/// Computes and caches a deterministic Release / Build ID for the running Horizon Browser binary
/// so Settings, Feedback, and Crash Reports can uniquely identify even same-version release updates.
/// </summary>
public static class BuildIdentityService
{
    private static string? _cachedBuildId;
    private static readonly object _lock = new();

    /// <summary>
    /// Returns a compact, unique Release/Build ID (e.g. "20261125-a3f9c1b2" or includes GitHub asset ID if present).
    /// </summary>
    public static string GetReleaseBuildId()
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_cachedBuildId))
                return _cachedBuildId;

            string datePart = DateTime.UtcNow.ToString("yyyyMMdd");
            string hashPart = "local";

            try
            {
                string exe = DefaultBrowserService.ResolveExecutablePath();
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    datePart = File.GetLastWriteTimeUtc(exe).ToString("yyyyMMdd");
                    using var fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sha = SHA256.Create();
                    byte[] hash = sha.ComputeHash(fs);
                    hashPart = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
                }
            }
            catch { }

            // If a GitHub release signature was recorded by the updater, append its short asset/digest tag
            string sigSuffix = "";
            try
            {
                string sig = SettingsService.Current.LastInstalledReleaseSignature ?? "";
                if (!string.IsNullOrWhiteSpace(sig))
                {
                    var parts = sig.Split('|');
                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        string token = parts[1].Replace("sha256:", "", StringComparison.OrdinalIgnoreCase).Trim();
                        if (token.Length > 8) token = token[..8];
                        sigSuffix = "-" + token.ToLowerInvariant();
                    }
                }
            }
            catch { }

            _cachedBuildId = $"{datePart}-{hashPart}{sigSuffix}";
            return _cachedBuildId;
        }
    }
}
