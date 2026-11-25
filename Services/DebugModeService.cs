using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Horizon.Stealth.Core;
using Horizon.Stealth.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Horizon.Stealth.Services;

/// <summary>
/// Dedicated Horizon Browser Debug Mode (--debug-mode) and headless background Crash Reproduction
/// Probe (--repro-crash). Enables ultra-verbose logging, first-chance exception tracing, periodic
/// process/memory telemetry, and automated crash-context reproduction for feedback attachment.
/// </summary>
public static class DebugModeService
{
    public static bool IsDebugMode { get; private set; }
    public static bool IsReproProbe { get; private set; }
    public static string? ActiveDeepLogPath { get; private set; }

    private static Timer? _telemetryTimer;
    private static readonly ConcurrentDictionary<string, long> _firstChanceThrottle = new();
    private static int _firstChanceHooked;

    public static void Enable(string? dedicatedOutputPath = null, bool isReproProbe = false)
    {
        IsDebugMode = true;
        IsReproProbe = isReproProbe;
        LogService.DebugEnabled = true;

        try
        {
            string logDir = !string.IsNullOrEmpty(ConfigService.LogsRoot)
                ? ConfigService.LogsRoot
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);

            string tracePath = !string.IsNullOrWhiteSpace(dedicatedOutputPath)
                ? dedicatedOutputPath
                : Path.Combine(logDir, $"deep_debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            string? parent = Path.GetDirectoryName(tracePath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            ActiveDeepLogPath = tracePath;
            LogService.SetDeepTraceSink(tracePath);

            WriteEnvironmentSnapshot();
            HookFirstChanceExceptions();

            if (!isReproProbe && _telemetryTimer == null)
            {
                _telemetryTimer = new Timer(_ => LogTelemetrySnapshot(), null, 5000, 10000);
            }

            LogService.Write("DEBUGMODE", $"Dedicated Debug Mode ACTIVE (ReproProbe={isReproProbe}, Trace={tracePath})");
        }
        catch (Exception ex)
        {
            LogService.Write("DEBUGMODE", "Enable error: " + ex.Message);
        }
    }

    private static void HookFirstChanceExceptions()
    {
        if (Interlocked.Exchange(ref _firstChanceHooked, 1) != 0) return;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        try
        {
            var ex = e.Exception;
            if (ex == null) return;
            // Throttle identical exception type+message pairs to once every 3 seconds
            string key = ex.GetType().Name + "|" + (ex.Message?.Length > 80 ? ex.Message[..80] : ex.Message);
            long now = Environment.TickCount64;
            if (_firstChanceThrottle.TryGetValue(key, out long last) && now - last < 3000)
                return;
            _firstChanceThrottle[key] = now;

            string topFrame = "";
            try
            {
                var st = new StackTrace(2, false);
                var frame = st.GetFrame(0);
                var m = frame?.GetMethod();
                if (m != null) topFrame = $" at {m.DeclaringType?.Name}.{m.Name}";
            }
            catch { }

            LogService.Write("FIRSTCHANCE", $"{ex.GetType().FullName}: {ex.Message}{topFrame}");
        }
        catch { }
    }

    public static void WriteEnvironmentSnapshot()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("================ HORIZON DEBUG MODE ENVIRONMENT SNAPSHOT ================");
            sb.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:o}");
            sb.AppendLine($"Release/Build ID: {BuildIdentityService.GetReleaseBuildId()}");
            sb.AppendLine($"Executable:       {DefaultBrowserService.ResolveExecutablePath()}");
            sb.AppendLine($"PID / Arch:       {Environment.ProcessId} / {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"OS:               {Environment.OSVersion.VersionString}");
            sb.AppendLine($".NET Runtime:     {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"CPU Cores:        {Environment.ProcessorCount}");
            sb.AppendLine($"Working Set:      {Environment.WorkingSet / (1024 * 1024)} MB");

            try
            {
                string? wv2 = CoreWebView2Environment.GetAvailableBrowserVersionString();
                sb.AppendLine($"WebView2 Runtime: {wv2 ?? "unavailable"}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"WebView2 Runtime: error ({ex.Message})");
            }

            try
            {
                var s = SettingsService.Current;
                sb.AppendLine($"Settings: Theme={s.Theme}, BlurOff={s.DisableBlurEffects}, AdGuard={s.AdGuardEnabled}, ConsentOMatic={s.ConsentOMaticEnabled}, FullscreenOverlay={s.ShowFullscreenOverlay}");
            }
            catch { }

            try
            {
                var exts = ExtensionService.All;
                sb.AppendLine($"Extensions ({exts.Count}): " +
                    string.Join(", ", exts.Select(x => $"{x.Name}[{(x.Enabled ? "ON" : "OFF")}:{x.Version}]")));
            }
            catch { }

            try
            {
                var apps = WebAppService.LoadAll();
                sb.AppendLine($"WebApps ({apps.Count}): " +
                    string.Join(", ", apps.Select(a => $"{a.Name}({a.Id})")));
            }
            catch { }

            sb.AppendLine("=========================================================================");
            LogService.Write("DEBUGMODE", Environment.NewLine + sb.ToString());
        }
        catch { }
    }

    private static void LogTelemetrySnapshot()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            long wsMb = proc.WorkingSet64 / (1024 * 1024);
            long privMb = proc.PrivateMemorySize64 / (1024 * 1024);
            int threads = proc.Threads.Count;
            int handles = proc.HandleCount;
            LogService.Write("TELEMETRY", $"WS={wsMb}MB Priv={privMb}MB Threads={threads} Handles={handles} GC=({GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)})");
        }
        catch { }
    }

    /// <summary>
    /// Restarts Horizon Browser either entering or exiting Dedicated Debug Mode.
    /// </summary>
    public static void RestartWithDebugMode(bool enableDebugMode)
    {
        try { Controls.BrowserView.SaveSessionBeforeRestart?.Invoke(); } catch { }

        try
        {
            string exe = DefaultBrowserService.ResolveExecutablePath();
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = enableDebugMode ? "--debug-mode" : "",
                    UseShellExecute = true
                });
            }
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "DebugModeService.RestartWithDebugMode");
        }
    }

    /// <summary>
    /// Runs a bounded, hidden background reproduction probe in Debug Mode after a crash,
    /// capturing ultra-detailed logs of environment init, extension load, and last active URL
    /// into outputTracePath so CrashFeedbackWindow can auto-attach it.
    /// </summary>
    public static async Task RunCrashReproProbeAsync(string crashPayloadPath, string outputTracePath)
    {
        Enable(outputTracePath, isReproProbe: true);
        LogService.Write("REPRO", $"Starting background crash reproduction probe. Payload={crashPayloadPath}");

        using var hardTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(9));
        Window? hiddenHost = null;

        try
        {
            CrashReportPayload? payload = null;
            if (!string.IsNullOrWhiteSpace(crashPayloadPath) && File.Exists(crashPayloadPath))
            {
                payload = JsonSerializer.Deserialize<CrashReportPayload>(File.ReadAllText(crashPayloadPath, Encoding.UTF8));
            }

            if (payload != null)
            {
                LogService.Write("REPRO", $"Original Crash: {payload.ExceptionType}: {payload.ExceptionMessage}");
                LogService.Write("REPRO", $"Crash Source={payload.CrashSource}, Version={payload.BrowserVersion}, BuildId={payload.ReleaseBuildId}, LastActiveUrl={payload.LastActiveUrl}");
            }

            // Load settings & extensions catalog in debug mode
            try { SettingsService.Load(); } catch (Exception ex) { LogService.Write("REPRO", "SettingsService.Load failed: " + ex.Message); }
            try { ExtensionService.EnsureInstalled(); } catch (Exception ex) { LogService.Write("REPRO", "ExtensionService.EnsureInstalled failed: " + ex.Message); }
            WriteEnvironmentSnapshot();

            // Initialize WebView2 environment & probe last active URL offscreen (if valid http/https)
            string probeUrl = payload?.LastActiveUrl ?? "";
            bool hasHttpUrl = Uri.TryCreate(probeUrl, UriKind.Absolute, out var parsedUri) &&
                              (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps);

            LogService.Write("REPRO", "Initializing HorizonEnvironment (WebView2)...");
            await HorizonEnvironment.InitializeAsync();
            LogService.Write("REPRO", $"HorizonEnvironment ready: Instance={(HorizonEnvironment.Instance != null ? "OK" : "NULL")}");

            if (HorizonEnvironment.Instance != null && hasHttpUrl)
            {
                LogService.Write("REPRO", $"Launching offscreen WebView2 reproduction probe for URL: {probeUrl}");
                var wv = new WebView2();
                hiddenHost = new Window
                {
                    Width = 800,
                    Height = 600,
                    Left = -32000,
                    Top = -32000,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Opacity = 0,
                    Content = wv
                };
                hiddenHost.Show();

                var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await wv.EnsureCoreWebView2Async(HorizonEnvironment.Instance);
                var core = wv.CoreWebView2;

                // Log all installed extensions in the profile
                try
                {
                    var installedExts = await core.Profile.GetBrowserExtensionsAsync();
                    LogService.Write("REPRO", $"WebView2 Profile Extensions ({installedExts.Count}): " +
                        string.Join(", ", installedExts.Select(x => $"{x.Name}(id={x.Id},en={x.IsEnabled})")));
                }
                catch (Exception ex)
                {
                    LogService.Write("REPRO", "GetBrowserExtensionsAsync error: " + ex.Message);
                }

                core.NavigationStarting += (_, e) => LogService.Write("REPRO", $"NavStarting: {e.Uri} (redirect={e.IsRedirected})");
                core.NavigationCompleted += (_, e) =>
                {
                    LogService.Write("REPRO", $"NavCompleted: success={e.IsSuccess} status={e.WebErrorStatus} http={e.HttpStatusCode}");
                    navDone.TrySetResult(e.IsSuccess);
                };
                core.ProcessFailed += (_, e) =>
                {
                    LogService.Write("REPRO", $"WebView2 ProcessFailed: kind={e.ProcessFailedKind} reason={e.Reason} exit={e.ExitCode}");
                    navDone.TrySetResult(false);
                };
                core.WebResourceResponseReceived += (_, e) =>
                {
                    if (e.Response.StatusCode >= 400)
                        LogService.Write("REPRO", $"HTTP {e.Response.StatusCode}: {e.Request.Uri}");
                };

                // Enable CDP console & runtime exception capture
                try
                {
                    await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                    await core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
                    core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, ev) =>
                        LogService.Write("REPRO_JS_ERR", ev.ParameterObjectAsJson);
                    core.GetDevToolsProtocolEventReceiver("Log.entryAdded").DevToolsProtocolEventReceived += (_, ev) =>
                        LogService.Write("REPRO_CONSOLE", ev.ParameterObjectAsJson);
                }
                catch { }

                core.Navigate(probeUrl);

                // Wait up to 4.5s for navigation + JS errors to settle
                await Task.WhenAny(navDone.Task, Task.Delay(4500, hardTimeoutCts.Token));
                await Task.Delay(750, hardTimeoutCts.Token);
                LogTelemetrySnapshot();
            }
            else
            {
                LogService.Write("REPRO", "No external HTTP URL in crash payload; completed environment & extension diagnostic audit.");
                LogTelemetrySnapshot();
            }
        }
        catch (OperationCanceledException)
        {
            LogService.Write("REPRO", "Probe reached time budget cleanly.");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "DebugModeService.RunCrashReproProbeAsync (Reproduced Exception!)");
        }
        finally
        {
            try { hiddenHost?.Close(); } catch { }
            LogService.Write("REPRO", "Background crash reproduction probe complete.");
            LogService.FlushDebug();
        }
    }
}
