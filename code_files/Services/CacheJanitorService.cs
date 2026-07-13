using System;
using System.IO;
using System.Text.Json;

namespace Horizon.Stealth.Services;

/// <summary>
/// Tracks and performs periodic HTTP disk-cache flushes to prevent
/// the creeping-stale-cache bugs that accumulate over months of use.
/// Only DiskCache is cleared — cookies, localStorage, and IndexedDB
/// are fully preserved so the user is never logged out.
/// </summary>
public static class CacheJanitorService
{
    private static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horizon_Browser", "HorizonData", "cache_janitor.json");

    /// <summary>How many days between automatic cache flushes. Default: 14.</summary>
    public static int IntervalDays { get; set; } = 14;

    /// <summary>Returns true if enough time has passed since the last flush.</summary>
    public static bool IsDue()
    {
        try
        {
            if (!File.Exists(StateFile)) return true;
            var state = JsonSerializer.Deserialize<JanitorState>(File.ReadAllText(StateFile));
            return state == null || (DateTime.UtcNow - state.LastCleared).TotalDays >= IntervalDays;
        }
        catch { return true; }
    }

    /// <summary>Call immediately after ClearBrowsingDataAsync completes successfully.</summary>
    public static void RecordCleared()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile,
                JsonSerializer.Serialize(
                    new JanitorState { LastCleared = DateTime.UtcNow },
                    new JsonSerializerOptions { WriteIndented = true }));
            LogService.Write("JANITOR",
                $"HTTP disk-cache flushed. Next flush in {IntervalDays} days.");
        }
        catch (Exception ex) { LogService.RecordCrash(ex, "CacheJanitorService.RecordCleared"); }
    }

    /// <summary>Returns the UTC datetime of the last successful flush, or null if never run.</summary>
    public static DateTime? LastCleared()
    {
        try
        {
            if (!File.Exists(StateFile)) return null;
            var state = JsonSerializer.Deserialize<JanitorState>(File.ReadAllText(StateFile));
            return state?.LastCleared;
        }
        catch { return null; }
    }

    /// <summary>Force a flush regardless of schedule (e.g. from Maintenance window).</summary>
    public static void ForceNextRun()
    {
        try { if (File.Exists(StateFile)) File.Delete(StateFile); }
        catch (Exception ex) { LogService.RecordCrash(ex, "CacheJanitorService.ForceNextRun"); }
    }

    private class JanitorState
    {
        public DateTime LastCleared { get; set; }
    }
}