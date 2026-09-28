using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth.Services;

/// <summary>
/// Persists per-domain sleep rules across sessions.
/// Saved to: %LocalAppData%\Horizon_Browser\sleep_rules.json
/// Rules are keyed by domain (e.g. "youtube.com") so they reapply
/// automatically whenever the user navigates to that domain.
/// </summary>
public static class TabSleepRulesService
{
    private static readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horizon_Browser", "sleep_rules.json");

    public class DomainRule
    {
        public bool   NeverSleep     { get; set; }
        public int?   IdleMinutes    { get; set; }
        public long?  RamThresholdMb { get; set; }
    }

    private static Dictionary<string, DomainRule> _rules =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            _rules = JsonSerializer.Deserialize<Dictionary<string, DomainRule>>(
                         File.ReadAllText(_filePath))
                     ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { _rules = new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath,
                JsonSerializer.Serialize(_rules,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static DomainRule? GetRule(string url)
    {
        string? d = GetDomain(url);
        return d != null && _rules.TryGetValue(d, out var r) ? r : null;
    }

    public static void SetRule(string url, DomainRule rule)
    {
        string? d = GetDomain(url);
        if (d == null) return;
        _rules[d] = rule;
        Save();
    }

    public static void RemoveRule(string url)
    {
        string? d = GetDomain(url);
        if (d == null) return;
        _rules.Remove(d);
        Save();
    }

    /// <summary>
    /// If a persisted rule exists for this tab's domain, applies it.
    /// Call this after every NavigationCompleted so saved rules auto-apply.
    /// </summary>
    public static void ApplyIfExists(TabViewModel tab)
    {
        var rule = GetRule(tab.Url);
        if (rule == null) return;
        tab.NeverSleep                = rule.NeverSleep;
        tab.SleepIdleMinutesOverride  = rule.IdleMinutes;
        tab.SleepRamThresholdMbOverride = rule.RamThresholdMb;
    }

    private static string? GetDomain(string url)
    {
        try { return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : null; }
        catch { return null; }
    }
}