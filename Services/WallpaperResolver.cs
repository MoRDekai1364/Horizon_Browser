using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Horizon.Stealth.Services;

/// <summary>
/// Decides which wallpaper file to show next, based on the Phase 2 settings (Single / Selection / Collection)
/// and the Phase 1 library. No UI. HomePageView calls it when SettingsService.Current.UseMediaLibrary is on;
/// otherwise the old folder-based code keeps running unchanged.
/// </summary>
public static class WallpaperResolver
{
    /// <summary>Upper bound for decoded wallpapers kept in memory (a decoded 8K image is ~132 MB).</summary>
    public const long CacheMaxBytes = 512L * 1024 * 1024;

    private static readonly Random Rng = new();
    private static readonly object Gate = new();
    private static volatile bool _migrationKnownDone;

    /// <summary>Shuffle queue lives in user data, not inside collection folders, so renaming/moving a collection can't orphan it.</summary>
    private static string QueueFile => Path.Combine(ConfigService.UserDataRoot, "wallpaper_queue.txt");

    /// <summary>True while the one-time copy of the old exe-side wallpapers is still pending/running.</summary>
    public static bool MigrationPending
    {
        get
        {
            if (_migrationKnownDone) return false;
            bool pending = WallpaperLibraryService.NeedsLegacyMigration();
            if (!pending) _migrationKnownDone = true;
            return pending;
        }
    }

    /// <summary>The library is authoritative only when enabled and the old files have been copied over.</summary>
    public static bool UseLibrary => SettingsService.Current.UseMediaLibrary && !MigrationPending;

    // ------------------------------------------------------------------ candidates

    /// <summary>Existing files for the current selection (no fallback).</summary>
    public static List<string> GetCandidates(SettingsData s)
    {
        var lib = WallpaperLibraryService.Library;
        var list = new List<string>();
        switch (s.WallpaperSource)
        {
            case MediaSource.Single:
                if (lib.TryResolve(s.WallpaperSingle.Collection, s.WallpaperSingle.FileName, out string one))
                    list.Add(one);
                break;

            case MediaSource.Selection:
                foreach (var r in s.WallpaperSelection)
                    if (lib.TryResolve(r.Collection, r.FileName, out string p)) list.Add(p);
                break;

            default: // Collection ("" = loose files)
                list.AddRange(lib.ListItems(s.WallpaperActiveCollection).Select(i => i.FullPath));
                break;
        }
        return list;
    }

    /// <summary>Candidates, falling back to the loose files when the chosen file/collection is empty or gone.</summary>
    private static List<string> CandidatesWithFallback(SettingsData s)
    {
        var c = GetCandidates(s);
        bool alreadyLoose = s.WallpaperSource == MediaSource.Collection && string.IsNullOrEmpty(s.WallpaperActiveCollection);
        if (c.Count == 0 && !alreadyLoose)
            c = WallpaperLibraryService.Library.ListItems("").Select(i => i.FullPath).ToList();
        return c;
    }

    // ------------------------------------------------------------------ resolving

    /// <summary>Picks the wallpaper to show now and advances the rotation. "" when the library has nothing usable.</summary>
    public static string ResolveNext()
    {
        try
        {
            lock (Gate)
            {
                var s = SettingsService.Current;
                var c = CandidatesWithFallback(s);
                if (c.Count == 0) return "";
                if (s.WallpaperSource == MediaSource.Single || c.Count == 1) return c[0];

                if (s.WallpaperPlayOrder == MediaSource.Sequential)
                {
                    int next = ((s.WallpaperPlayIndex + 1) % c.Count + c.Count) % c.Count;
                    s.WallpaperPlayIndex = next;
                    SettingsService.Save();
                    return c[next];
                }

                return NextFromQueue(c);
            }
        }
        catch (Exception ex)
        {
            LogService.Write("WALLPAPER", "ResolveNext failed: " + ex.Message);
            return "";
        }
    }

    /// <summary>Next wallpapers that will be shown, for pre-decoding. Never changes any state.</summary>
    public static List<string> PeekUpcoming(int count)
    {
        var result = new List<string>();
        try
        {
            lock (Gate)
            {
                var s = SettingsService.Current;
                var c = CandidatesWithFallback(s);
                if (c.Count < 2 || s.WallpaperSource == MediaSource.Single) return result;

                if (s.WallpaperPlayOrder == MediaSource.Sequential)
                {
                    for (int i = 1; i <= count; i++)
                        result.Add(c[((s.WallpaperPlayIndex + i) % c.Count + c.Count) % c.Count]);
                }
                else
                {
                    var valid = new HashSet<string>(c, StringComparer.OrdinalIgnoreCase);
                    result.AddRange(ReadQueue(Signature(c), valid).Take(count));
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Side-effect-free pick (first candidate), e.g. for building the weather theme at startup.</summary>
    public static string ResolveStatic()
    {
        try
        {
            var c = CandidatesWithFallback(SettingsService.Current);
            return c.Count > 0 ? c[0] : "";
        }
        catch { return ""; }
    }

    // ------------------------------------------------------------------ shuffle queue (no repeats until all were shown)

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

    /// <summary>Hash of the candidate set; a changed set (files added/removed, other collection) starts a fresh queue.</summary>
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

    // ------------------------------------------------------------------ bitmap cache sizing

    /// <summary>Approximate decoded size (32 bits per pixel).</summary>
    public static long EstimateBytes(BitmapSource b) => (long)b.PixelWidth * b.PixelHeight * 4L;

    /// <summary>True if adding <paramref name="incoming"/> would push the decoded cache over <see cref="CacheMaxBytes"/>.</summary>
    public static bool CacheExceeds(IEnumerable<BitmapSource> cached, BitmapSource incoming)
    {
        long total = EstimateBytes(incoming);
        foreach (var b in cached) total += EstimateBytes(b);
        return total > CacheMaxBytes;
    }

    // ------------------------------------------------------------------ one-time copy of the old exe-side wallpapers

    private static readonly object MigrationGate = new();
    private static Task? _migrationTask;

    /// <summary>Copies the old wallpapers into user data. Only one copy runs at a time: callers that arrive while it
    /// is running get the same task, so the manager window and the startup path can never duplicate files.
    /// Completes immediately when there is nothing to copy.</summary>
    public static Task EnsureMigrationAsync(IProgress<MediaProgress>? progress = null)
    {
        lock (MigrationGate)
        {
            if (_migrationTask != null && !_migrationTask.IsCompleted) return _migrationTask;
            if (!WallpaperLibraryService.NeedsLegacyMigration())
            {
                _migrationKnownDone = true;
                return Task.CompletedTask;
            }

            _migrationTask = Task.Run(async () =>
            {
                try
                {
                    LogService.Write("WALLPAPER", "Legacy wallpaper migration started");
                    var r = await WallpaperLibraryService.MigrateLegacyAsync(progress).ConfigureAwait(false);
                    LogService.Write("WALLPAPER",
                        $"Legacy wallpaper migration finished: copied={r.Copied.Count}, skipped={r.Skipped.Count}, failed={r.Failed.Count}, cancelled={r.Cancelled}");
                    if (!WallpaperLibraryService.NeedsLegacyMigration()) _migrationKnownDone = true;
                }
                catch (Exception ex)
                {
                    LogService.RecordCrash(ex, "WallpaperMigration");
                }
            });
            return _migrationTask;
        }
    }

    /// <summary>Startup hook: copies the old wallpapers in the background, but only once the library is enabled.</summary>
    public static void StartMigrationInBackground()
    {
        if (!SettingsService.Current.UseMediaLibrary) return;
        _ = EnsureMigrationAsync();
    }
}
