using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Core;

public static class FingerprintExemptions
{
    private static readonly object _lock = new();
    private static readonly HashSet<string> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<DateTime>> _failures = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    private static string FilePath => Path.Combine(ConfigService.UserDataRoot, "fp_exempt.txt");

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var h = line.Trim().ToLowerInvariant();
                if (h.Length > 0) _hosts.Add(h);
            }
        }
        catch (Exception ex)
        {
            LogService.Write("FP", $"Exemption load failed: {ex.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, _hosts.OrderBy(h => h));
        }
        catch (Exception ex)
        {
            LogService.Write("FP", $"Exemption save failed: {ex.Message}");
        }
    }

    public static bool IsExempt(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        lock (_lock)
        {
            EnsureLoaded();
            string h = host.ToLowerInvariant();
            return _hosts.Any(e => h == e || h.EndsWith("." + e));
        }
    }

    public static bool Toggle(string host)
    {
        string h = host.ToLowerInvariant();
        lock (_lock)
        {
            EnsureLoaded();
            bool now;
            if (_hosts.Contains(h)) { _hosts.Remove(h); now = false; }
            else { _hosts.Add(h); now = true; }
            Save();
            return now;
        }
    }

    public static bool RecordFailure(string host)
    {
        string h = host.ToLowerInvariant();
        lock (_lock)
        {
            EnsureLoaded();
            if (_hosts.Any(e => h == e || h.EndsWith("." + e))) return false;
            if (!_failures.TryGetValue(h, out var list))
            {
                list = new List<DateTime>();
                _failures[h] = list;
            }
            var now = DateTime.UtcNow;
            list.RemoveAll(t => (now - t).TotalMinutes > 10);
            list.Add(now);
            if (list.Count < 2) return false;
            _hosts.Add(h);
            _failures.Remove(h);
            Save();
            return true;
        }
    }

    public static string ToJsArray()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return System.Text.Json.JsonSerializer.Serialize(_hosts.ToArray());
        }
    }
}