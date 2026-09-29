using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Stealth.Services;

public static class GeoIpService
{
    private const string DbUrl       = "https://github.com/sapics/ip-location-db/releases/download/latest/server-country-ipv4-num.csv";
    private const string ChecksumUrl = "https://github.com/sapics/ip-location-db/releases/download/checksum/server-country-ipv4-num.csv.sha256";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(14);

    private static readonly string _dir      = Path.Combine(LogService.UserDataRoot, "geoip");
    private static readonly string _dbPath   = Path.Combine(_dir, "server-country-ipv4-num.csv");
    private static readonly string _metaPath = Path.Combine(_dir, "meta.json");
    private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static long[]? _starts;
    private static string[]? _codes;
    private static bool _loadAttempted;

    private class MetaFile
    {
        public DateTime LastRefreshUtc { get; set; }
        public string Sha256 { get; set; } = "";
    }

    public static async Task<string?> GetCountryAsync(string ip)
    {
        var mode = SettingsService.Current.GeoLookupMode;
        if (mode == "Off" || string.IsNullOrWhiteSpace(ip)) return null;

        try
        {
            if (mode == "Api")
                return await LookupApiAsync(ip);

            return await LookupLocalAsync(ip);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "GeoIpService.GetCountry");
            return null;
        }
    }

    // ── Local mode ──────────────────────────────────────────────────────────

    private static async Task<string?> LookupLocalAsync(string ip)
    {
        await EnsureLoadedAsync();
        if (_starts == null || _codes == null || _starts.Length == 0) return null;

        if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork)
            return null;

        var value = ToUInt32(addr);
        int idx = Array.BinarySearch(_starts, (long)value);
        if (idx < 0) idx = ~idx - 1;
        if (idx < 0 || idx >= _codes.Length) return null;
        return _codes[idx];
    }

    private static async Task EnsureLoadedAsync()
    {
        if (_starts != null || _loadAttempted) return;

        await _lock.WaitAsync();
        try
        {
            if (_starts != null || _loadAttempted) return;
            _loadAttempted = true;

            Directory.CreateDirectory(_dir);

            if (!File.Exists(_dbPath) || IsStale())
            {
                var ok = await DownloadDatabaseAsync();
                if (!ok && !File.Exists(_dbPath))
                {
                    LogService.Write("GeoIpService", "No local DB available and download failed.");
                    return;
                }
            }

            ParseDatabase();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool IsStale()
    {
        try
        {
            if (!File.Exists(_metaPath)) return true;
            var meta = JsonSerializer.Deserialize<MetaFile>(File.ReadAllText(_metaPath));
            if (meta == null) return true;
            return DateTime.UtcNow - meta.LastRefreshUtc > RefreshInterval;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<bool> DownloadDatabaseAsync()
    {
        LogService.Write("GeoIpService", "Downloading country database...");
        try
        {
            var tmpPath = _dbPath + ".tmp";
            using (var resp = await _http.GetAsync(DbUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmpPath);
                await resp.Content.CopyToAsync(fs);
            }

            string? expectedHash = null;
            try
            {
                var checksumLine = await _http.GetStringAsync(ChecksumUrl);
                expectedHash = checksumLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }
            catch (Exception ex)
            {
                LogService.Write("GeoIpService", $"Checksum fetch failed (continuing without verification): {ex.Message}");
            }

            if (!string.IsNullOrEmpty(expectedHash))
            {
                using var sha = SHA256.Create();
                await using var verifyStream = File.OpenRead(tmpPath);
                var actualHash = Convert.ToHexString(sha.ComputeHash(verifyStream)).ToLowerInvariant();
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    LogService.Write("GeoIpService", "Checksum mismatch, discarding download.");
                    File.Delete(tmpPath);
                    return false;
                }
            }

            File.Copy(tmpPath, _dbPath, overwrite: true);
            File.Delete(tmpPath);

            File.WriteAllText(_metaPath, JsonSerializer.Serialize(new MetaFile
            {
                LastRefreshUtc = DateTime.UtcNow,
                Sha256 = expectedHash ?? ""
            }));

            LogService.Write("GeoIpService", "Country database updated.");
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "GeoIpService.Download");
            return false;
        }
    }

    private static void ParseDatabase()
    {
        try
        {
            var starts = new List<long>();
            var codes = new List<string>();

            foreach (var line in File.ReadLines(_dbPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 3) continue;
                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)) continue;

                starts.Add(start);
                codes.Add(parts[2].Trim());
            }

            _starts = starts.ToArray();
            _codes = codes.ToArray();
            LogService.Write("GeoIpService", $"Loaded {_starts.Length} country ranges.");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "GeoIpService.Parse");
            _starts = Array.Empty<long>();
            _codes = Array.Empty<string>();
        }
    }

    private static uint ToUInt32(IPAddress addr)
    {
        var bytes = addr.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    // ── Api mode ────────────────────────────────────────────────────────────

    private static async Task<string?> LookupApiAsync(string ip)
    {
        try
        {
            var url = $"http://ip-api.com/json/{Uri.EscapeDataString(ip)}?fields=status,countryCode";
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var status) && status.GetString() != "success") return null;
            if (root.TryGetProperty("countryCode", out var cc)) return cc.GetString();
            return null;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "GeoIpService.LookupApi");
            return null;
        }
    }

    // Called once at startup; no-op if mode is Off or DB is already fresh.
    public static void KickOffBackgroundRefresh()
    {
        if (SettingsService.Current.GeoLookupMode != "Local") return;
        _ = EnsureLoadedAsync();
    }
}