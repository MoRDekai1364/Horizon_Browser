using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Horizon.Stealth.Views;

namespace Horizon.Stealth.Services;

public sealed class WebAppJournalEntry
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    public double ScrollX { get; set; }
    public double ScrollY { get; set; }
    public double MediaTime { get; set; }
    public int Generation { get; set; }
    public string SavedUtc { get; set; } = "";
}

public static class WebAppJournalService
{
    private const string LogTag = "WEBAPP";
    private const int TickMs = 3000;
    private const int SnapshotEvery = 4;
    private const int WatcherCheckEvery = 4;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly Dictionary<string, WebAppJournalEntry> Last = new(StringComparer.OrdinalIgnoreCase);

    private static System.Windows.Threading.DispatcherTimer? _timer;
    private static Func<IReadOnlyList<WebAppWindow>>? _provider;
    private static bool _busy;
    private static bool _exitHooked;
    private static int _tick;

    public static bool CrashShutdown { get; set; }

    public static string JournalDir => Path.Combine(WebAppService.RootDir, "_journal");

    private static string PathFor(int pid) => Path.Combine(JournalDir, pid + ".json");

    private static string OwnPath => PathFor(Environment.ProcessId);

    private static string StandbyMutexName(int pid) => @"Local\Horizon.Stealth.WebAppStandby." + pid;

    public static void EnsureRunning(Func<IReadOnlyList<WebAppWindow>> provider)
    {
        _provider = provider;
        CleanupStale();

        if (!_exitHooked && Application.Current != null)
        {
            _exitHooked = true;
            Application.Current.Exit += (_, _) => OnAppExit();
        }

        if (_timer == null)
        {
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
            _timer.Tick += OnTick;
        }
        if (!_timer.IsEnabled) _timer.Start();

        EnsureWatcher();
    }

    public static void NotifyWindowClosed(string id, int remaining)
    {
        if (CrashShutdown) return;
        Last.Remove(id);
        if (remaining == 0)
        {
            DeleteOwn();
            return;
        }
        WriteOwn();
    }

    private static async void OnTick(object? sender, EventArgs e)
    {
        if (_busy || CrashShutdown) return;
        _busy = true;
        try
        {
            var windows = _provider?.Invoke() ?? new List<WebAppWindow>();
            if (windows.Count == 0)
            {
                _timer?.Stop();
                Last.Clear();
                DeleteOwn();
                return;
            }

            _tick++;
            bool snapshot = _tick % SnapshotEvery == 0;

            foreach (var w in windows)
            {
                var entry = await w.CaptureStateAsync(snapshot);
                if (entry != null) Last[entry.Id] = entry;
            }

            if (CrashShutdown) return;
            WriteOwn();
            if (_tick % WatcherCheckEvery == 0) EnsureWatcher();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppJournalService.OnTick");
        }
        finally
        {
            _busy = false;
        }
    }

    private static void WriteOwn()
    {
        try
        {
            Directory.CreateDirectory(JournalDir);
            string path = OwnPath;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Last.Values.ToList(), JsonOpts), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppJournalService.WriteOwn");
        }
    }

    private static void DeleteOwn()
    {
        try { File.Delete(OwnPath); }
        catch { }
    }

    private static void OnAppExit()
    {
        _timer?.Stop();
        if (CrashShutdown)
        {
            LogService.Write(LogTag, "Crash shutdown. Journal kept for the standby watcher.");
            return;
        }
        DeleteOwn();
    }

    private static void EnsureWatcher()
    {
        try
        {
            int pid = Environment.ProcessId;
            if (Mutex.TryOpenExisting(StandbyMutexName(pid), out var existing))
            {
                existing.Dispose();
                return;
            }

            string exe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;
            if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase)) return;

            var psi = new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{exe}\" --webapp-standby --parent-pid={pid}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi)?.Dispose();
            LogService.Write(LogTag, $"Standby watcher requested for pid {pid}.");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppJournalService.EnsureWatcher");
        }
    }

    public static Mutex? TryClaimStandby(int parentPid)
    {
        try
        {
            var m = new Mutex(true, StandbyMutexName(parentPid), out bool created);
            if (!created)
            {
                m.Dispose();
                return null;
            }
            return m;
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupStale()
    {
        try
        {
            if (!Directory.Exists(JournalDir)) return;
            foreach (var file in Directory.GetFiles(JournalDir, "*.json"))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int pid)) continue;
                if (pid == Environment.ProcessId) continue;
                if (File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddHours(-1)) continue;

                bool alive = false;
                try
                {
                    using var p = Process.GetProcessById(pid);
                    alive = !p.HasExited;
                }
                catch
                {
                }
                if (!alive) File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppJournalService.CleanupStale");
        }
    }

    public static bool JournalExists(int pid) => File.Exists(PathFor(pid));

    public static List<WebAppJournalEntry> Read(int pid)
    {
        try
        {
            string path = PathFor(pid);
            if (!File.Exists(path)) return new List<WebAppJournalEntry>();
            var list = JsonSerializer.Deserialize<List<WebAppJournalEntry>>(File.ReadAllText(path, Encoding.UTF8))
                       ?? new List<WebAppJournalEntry>();
            list.RemoveAll(x => !WebAppService.IsValidId(x.Id));
            return list;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppJournalService.Read");
            return new List<WebAppJournalEntry>();
        }
    }

    public static void Delete(int pid)
    {
        try { File.Delete(PathFor(pid)); }
        catch { }
    }
}

public static class WebAppHostService
{
    private const string LogTag = "WEBAPP";
    private const string MutexName = @"Local\Horizon.Stealth.WebAppHost";

    private static readonly Dictionary<string, (WebAppManifest Manifest, WebAppWindow Window)> Windows =
        new(StringComparer.OrdinalIgnoreCase);

    private static Mutex? _mutex;

    public static bool IsHostProcess { get; private set; }

    public static int OpenWindowCount => Windows.Count;

    public static IReadOnlyList<(string Id, string Name)> GetOpenApps()
        => Windows.Values.Select(v => (Id: v.Manifest.Id, Name: v.Manifest.Name)).ToList();

    public static bool ActivateById(string id)
    {
        if (!Windows.TryGetValue(id, out var entry)) return false;
        ActivateWindow(entry.Window);
        return true;
    }

    public static void CloseById(string id)
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            if (Windows.TryGetValue(id, out var entry)) entry.Window.Close();
        });
    }

    public static void AllowForeground()
    {
        try { AllowSetForegroundWindow(-1); }
        catch { }
    }

    public static bool TrySendToHost(string id)
    {
        AllowForeground();
        return BackgroundKeepAliveService.TryActivateExistingInstance("OPENWEBAPP:" + id);
    }

    public static bool TryBecomeHost()
    {
        try
        {
            if (_mutex != null) return true;
            var m = new Mutex(true, MutexName, out bool created);
            if (!created)
            {
                m.Dispose();
                return false;
            }
            _mutex = m;
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppHostService.TryBecomeHost");
            return false;
        }
    }

    public static void StartHost(WebAppManifest first)
    {
        IsHostProcess = true;

        BackgroundKeepAliveService.OnOpenWebAppReceived = id =>
        {
            var m = WebAppService.Load(id);
            if (m != null) OpenOrActivate(m);
        };
        BackgroundKeepAliveService.StartPipeServerForHost();

        if (Application.Current != null)
            Application.Current.Exit += (_, _) => ReleaseHostMutex();

        LogService.Write(LogTag, "Host started. First app: " + first.Id);
        OpenOrActivate(first);
    }

    public static int RestoreHost(IReadOnlyList<WebAppJournalEntry> entries)
    {
        IsHostProcess = true;

        BackgroundKeepAliveService.OnOpenWebAppReceived = id =>
        {
            var m = WebAppService.Load(id);
            if (m != null) OpenOrActivate(m);
        };
        BackgroundKeepAliveService.StartPipeServerForHost();

        if (Application.Current != null)
            Application.Current.Exit += (_, _) => ReleaseHostMutex();

        int opened = 0;
        foreach (var entry in entries)
        {
            var m = WebAppService.Load(entry.Id);
            if (m == null)
            {
                LogService.Write(LogTag, "Restore skipped, manifest missing: " + entry.Id);
                continue;
            }
            OpenOrActivate(m, entry);
            opened++;
        }

        LogService.Write(LogTag, $"Restore host started. Restored {opened} of {entries.Count} window(s).");
        return opened;
    }

    public static void OpenOrActivate(WebAppManifest manifest, WebAppJournalEntry? restore = null)
    {
        var app = Application.Current;
        if (app == null) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(new Action(() => OpenOrActivate(manifest, restore)));
            return;
        }

        try
        {
            if (Windows.TryGetValue(manifest.Id, out var existing))
            {
                ActivateWindow(existing.Window);
                LogService.Write(LogTag, "Activated existing window: " + manifest.Id);
                return;
            }

            var window = new WebAppWindow(manifest, restore);
            Windows[manifest.Id] = (manifest, window);
            window.Closed += (_, _) => OnWindowClosed(manifest.Id);
            window.Show();
            ActivateWindow(window);
            WebAppJournalService.EnsureRunning(() => Windows.Values.Select(v => v.Window).ToList());
            LogService.Write(LogTag, $"Opened window: {manifest.Id}. Open windows: {Windows.Count}. Host={IsHostProcess}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppHostService.OpenOrActivate");
            Windows.Remove(manifest.Id);
            if (Windows.Count == 0 && !BackgroundKeepAliveService.HasLiveMainWindow) app.Shutdown();
        }
    }

    private static void ActivateWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        if (!window.Topmost)
        {
            window.Topmost = true;
            window.Topmost = false;
        }
        window.Focus();
    }

    private static void OnWindowClosed(string id)
    {
        Windows.Remove(id);
        WebAppJournalService.NotifyWindowClosed(id, Windows.Count);
        LogService.Write(LogTag, $"Window closed: {id}. Open windows: {Windows.Count}");

        if (Windows.Count == 0 && !BackgroundKeepAliveService.HasLiveMainWindow)
        {
            LogService.Write(LogTag, "No main window and no Web App windows left. Exiting.");
            Application.Current?.Shutdown();
        }
    }

    private static void ReleaseHostMutex()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}