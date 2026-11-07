using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Stealth.Services;

/// <summary>
/// Decides which startup video to play, based on the Phase 2 settings (Single / Selection / Collection)
/// and the startup-video library. No UI. Called from StartupVideoWindow, which runs on its own thread,
/// so everything here is guarded by a lock.
/// </summary>
public static class StartupVideoResolver
{
    private static readonly Random Rng = new();
    private static readonly object Gate = new();
    private static readonly object MigrationGate = new();
    private static Task? _migrationTask;
    private static volatile bool _migrationKnownDone;

    private static string QueueFile => Path.Combine(ConfigService.UserDataRoot, "startup_video_queue.txt");

    /// <summary>True while the one-time copy of the old start_videos folder is still pending/running.</summary>
    public static bool MigrationPending
    {
        get
        {
            if (_migrationKnownDone) return false;
            bool pending = StartupVideoLibraryService.NeedsLegacyMigration();
            if (!pending) _migrationKnownDone = true;
            return pending;
        }
    }

    /// <summary>The library is authoritative only when enabled and the old files have been copied over.</summary>
    public static bool UseLibrary => SettingsService.Current.UseMediaLibrary && !MigrationPending;

    // ------------------------------------------------------------------ candidates

    public static List<string> GetCandidates(SettingsData s)
    {
        var lib = StartupVideoLibraryService.Library;
        var list = new List<string>();
        switch (s.StartupVideoSource)
        {
            case MediaSource.Single:
                if (lib.TryResolve(s.StartupVideoSingle.Collection, s.StartupVideoSingle.FileName, out string one))
                    list.Add(one);
                break;

            case MediaSource.Selection:
                foreach (var r in s.StartupVideoSelection)
                    if (lib.TryResolve(r.Collection, r.FileName, out string p)) list.Add(p);
                break;

            default: // Collection ("" = loose files)
                list.AddRange(lib.ListItems(s.StartupVideoActiveCollection).Select(i => i.FullPath));
                break;
        }
        return list;
    }

    private static List<string> CandidatesWithFallback(SettingsData s)
    {
        var c = GetCandidates(s);
        bool alreadyLoose = s.StartupVideoSource == MediaSource.Collection && string.IsNullOrEmpty(s.StartupVideoActiveCollection);
        if (c.Count == 0 && !alreadyLoose)
            c = StartupVideoLibraryService.Library.ListItems("").Select(i => i.FullPath).ToList();
        return c;
    }

    // ------------------------------------------------------------------ resolving

    /// <summary>Picks the video for this launch and advances the rotation. "" when nothing usable exists.</summary>
    public static string ResolveNext()
    {
        try
        {
            lock (Gate)
            {
                var s = SettingsService.Current;
                var c = CandidatesWithFallback(s);
                if (c.Count == 0) return "";
                if (s.StartupVideoSource == MediaSource.Single || c.Count == 1) return c[0];

                if (s.StartupVideoPlayOrder == MediaSource.Sequential)
                {
                    int next = ((s.StartupVideoPlayIndex + 1) % c.Count + c.Count) % c.Count;
                    s.StartupVideoPlayIndex = next;
                    SettingsService.Save();
                    return c[next];
                }

                return NextFromQueue(c);
            }
        }
        catch (Exception ex)
        {
            LogService.Write("STARTUP", "Startup video ResolveNext failed: " + ex.Message);
            return "";
        }
    }

    // ------------------------------------------------------------------ shuffle queue (no repeats until all were played)

    private static string NextFromQueue(List<string> candidates)
    {
        string sig = Signature(candidates);
        var valid = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
        var queue = ReadQueue(sig, valid);
        if (queue.Count == 0) queue = Shuffle(candidates);

        string selected = queue[0];
        queue.RemoveAt(0);
        if (queue.Count == 0)
        {
            queue = Shuffle(candidates);
            if (queue.Count > 1 && string.Equals(queue[0], selected, StringComparison.OrdinalIgnoreCase))
                (queue[0], queue[1]) = (queue[1], queue[0]);
        }
        WriteQueue(sig, queue);
        return selected;
    }

    private static List<string> Shuffle(List<string> source)
    {
        var l = new List<string>(source);
        for (int i = l.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (l[i], l[j]) = (l[j], l[i]);
        }
        return l;
    }

    private static string Signature(List<string> candidates)
    {
        string joined = string.Join("\n", candidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private static List<string> ReadQueue(string signature, HashSet<string> valid)
    {
        try
        {
            if (!File.Exists(QueueFile)) return new List<string>();
            var lines = File.ReadAllLines(QueueFile);
            if (lines.Length == 0 || lines[0] != signature) return new List<string>();
            return lines.Skip(1).Where(valid.Contains).ToList();
        }
        catch { return new List<string>(); }
    }

    private static void WriteQueue(string signature, List<string> queue)
    {
        try { File.WriteAllLines(QueueFile, new[] { signature }.Concat(queue)); }
        catch { }
    }

    // ------------------------------------------------------------------ one-time copy of the old start_videos folder

    /// <summary>Copies the old startup videos into user data. Only one copy runs at a time; callers that arrive
    /// while it is running share the same task. Completes immediately when there is nothing to copy.</summary>
    public static Task EnsureMigrationAsync(IProgress<MediaProgress>? progress = null)
    {
        lock (MigrationGate)
        {
            if (_migrationTask != null && !_migrationTask.IsCompleted) return _migrationTask;
            if (!StartupVideoLibraryService.NeedsLegacyMigration())
            {
                _migrationKnownDone = true;
                return Task.CompletedTask;
            }

            _migrationTask = Task.Run(async () =>
            {
                try
                {
                    LogService.Write("STARTUP", "Legacy startup video migration started");
                    var r = await StartupVideoLibraryService.MigrateLegacyAsync(progress).ConfigureAwait(false);
                    LogService.Write("STARTUP",
                        $"Legacy startup video migration finished: copied={r.Copied.Count}, skipped={r.Skipped.Count}, failed={r.Failed.Count}, cancelled={r.Cancelled}");
                    if (!StartupVideoLibraryService.NeedsLegacyMigration()) _migrationKnownDone = true;
                }
                catch (Exception ex)
                {
                    LogService.RecordCrash(ex, "StartupVideoMigration");
                }
            });
            return _migrationTask;
        }
    }

    /// <summary>Startup hook: copies the old videos in the background, once the library is enabled.</summary>
    public static void StartMigrationInBackground()
    {
        if (!SettingsService.Current.UseMediaLibrary) return;
        _ = EnsureMigrationAsync();
    }
}
