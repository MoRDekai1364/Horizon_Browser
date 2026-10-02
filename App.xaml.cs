using System.Windows;
using System.Windows.Threading;
using Horizon.Stealth.Services;
using Horizon.Stealth.Core;
using Horizon.Stealth.Views;

namespace Horizon.Stealth;

public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        IntPtr foregroundWindow = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        uint currentThread = GetCurrentThreadId();

        bool attached = false;
        try
        {
            if (foregroundThread != currentThread)
                attached = AttachThreadInput(currentThread, foregroundThread, true);

            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        LogService.Initialize();
        LogService.Write("BOOT", "Horizon Stealth Browser is starting...");

        ConfigService.InitializeFileSystem();
        _ = Task.Run(() =>
        {
            try { BookmarkService.Initialize(); }
            catch (Exception ex) { LogService.RecordCrash(ex, "BookmarkService.Initialize (startup)"); }
        });

        this.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        bool maintenanceMode = false;
        bool trayStart = false;

        foreach (var arg in e.Args)
        {
            if (arg.Equals("--tray-start", StringComparison.OrdinalIgnoreCase))
            {
                trayStart = true;
                LogService.Write("BOOT", "Argument detected: --tray-start (auto-start on login)");
                break;
            }
        }
        
        if (e.Args.Length > 0)
        {
            foreach (var arg in e.Args)
            {
                if (arg.Equals("/maintenance", StringComparison.OrdinalIgnoreCase) || 
                    arg.Equals("-maintenance", StringComparison.OrdinalIgnoreCase))
                {
                    maintenanceMode = true;
                    LogService.Write("BOOT", "Argument detected: /maintenance");
                    break;
                }
            }
        }


        // Jump List media-control click: relay to the running instance (if any)
        // and exit immediately — never opens a browser window for these.
        string? mediaCmd = null;
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--media-cmd=", StringComparison.OrdinalIgnoreCase))
            {
                mediaCmd = arg.Substring("--media-cmd=".Length);
                break;
            }
        }
        if (mediaCmd != null)
        {
            Horizon.Stealth.Services.BackgroundKeepAliveService.TryActivateExistingInstance("MEDIA:" + mediaCmd);
            Shutdown();
            return;
        }

        foreach (var standbyArg in e.Args)
        {
            if (standbyArg.Equals("--webapp-standby", StringComparison.OrdinalIgnoreCase))
            {
                RunStandby(e);
                return;
            }
        }

        string? webAppId = null;
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--webapp-id=", StringComparison.OrdinalIgnoreCase))
            {
                webAppId = arg.Substring("--webapp-id=".Length).Trim('"');
                break;
            }
        }
        if (webAppId != null)
        {
            LaunchWebApp(e, webAppId);
            return;
        }

        // Single-instance: if a hidden Horizon is already running, wake it and exit.
        if (Horizon.Stealth.Services.BackgroundKeepAliveService.TryActivateExistingInstance())
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        if (maintenanceMode)
        {
            LogService.Write("BOOT", "Mode: Maintenance Dashboard");
            try 
            {
                var dash = new MaintenanceWindow();
                dash.Show();
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "Failed to launch MaintenanceWindow");
                MessageBox.Show("Could not launch Maintenance Mode.\n" + ex.Message);
                Shutdown();
            }
        }
        else
        {
            LogService.Write("BOOT", "Mode: Standard Browser");

            // Kick off WebView2 environment creation immediately — in parallel
            // with WPF window construction so the first tab is ready faster.
            try { SettingsService.Load(); }
            catch (Exception ex) { LogService.RecordCrash(ex, "Startup Settings Load"); }

            _ = StealthEnvironment.InitializeAsync();
            GeoIpService.KickOffBackgroundRefresh();
            VpnThroughputHistoryService.Start();
            _mainServicesStarted = true;

            try
            {
                string? startUrl = null;
                bool isWebApp = false;
                Horizon.Stealth.Services.WebAppManifest? webAppManifest = null;

                for (int i = 0; i < e.Args.Length; i++)
                {
                    var arg = e.Args[i];

                    if (arg.StartsWith("--webapp-id=", StringComparison.OrdinalIgnoreCase))
                    {
                        string appId = arg.Substring("--webapp-id=".Length).Trim('"');
                        var appManifest = Horizon.Stealth.Services.WebAppService.Load(appId);
                        if (appManifest != null)
                        {
                            isWebApp = true;
                            startUrl = appManifest.StartUrl;
                            webAppManifest = appManifest;
                            LogService.Write("BOOT", $"WebApp manifest loaded: {appId} -> {startUrl}");
                        }
                        else
                        {
                            LogService.Write("BOOT", $"WebApp manifest not found or invalid: {appId}");
                        }
                        continue;
                    }

                    if (arg.StartsWith("--webapp="))
                    {
                        isWebApp = true;
                        startUrl = arg.Substring(9).Trim('"');
                        LogService.Write("BOOT", $"WebApp mode detected: {startUrl}");
                        continue;
                    }
                    if (arg.Equals("-webapp", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                    {
                        isWebApp = true;
                        startUrl = e.Args[i + 1].Trim('"');
                        LogService.Write("BOOT", $"WebApp mode detected: {startUrl}");
                        i++;
                        continue;
                    }

                    if (arg.StartsWith("-") || arg.StartsWith("/")) continue;
                    if (startUrl == null)
                    {
                        startUrl = arg;
                        LogService.Write("BOOT", $"Launch argument detected: {arg}");
                    }
                }

                if (webAppManifest != null)
                {
                    LogService.Write("BOOT", $"Mode: WebApp window ({webAppManifest.Id})");
                    var webAppWindow = new Horizon.Stealth.Views.WebAppWindow(webAppManifest);
                    webAppWindow.Show();
                    return;
                }

                _isBrowserProcess = true;
                BackgroundKeepAliveService.OnMainWindowRequested = ShowNewMainWindow;

                var browser = new MainWindow(startUrl, isWebApp);

                if (trayStart)
                {
                    // Auto-start on login: never show the window or splash video.
                    // Go straight to the tray, same as a manual "keep alive" hide.
                    browser.WindowState = WindowState.Minimized;
                    browser.ShowInTaskbar = false;
                    browser.Show();
                    BackgroundKeepAliveService.StartHiddenInTray();
                }
                else
                {
                    // Show immediately but minimized, so layout/render/Loaded work
                    // happens in parallel with the splash video instead of cold
                    // at reveal time.
                    browser.WindowState = WindowState.Minimized;
                    browser.ShowInTaskbar = true;
                    browser.Show();

                    StartupVideoWindow.PlayIfEnabled(() =>
                    {
                        var unminimizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                        unminimizeTimer.Tick += (s, e) =>
                        {
                            unminimizeTimer.Stop();

                            browser.WindowState = WindowState.Normal;
                            browser.Topmost = true;
                            browser.Activate();
                            browser.Topmost = false;

                            var hwnd = new System.Windows.Interop.WindowInteropHelper(browser).Handle;
                            ForceForeground(hwnd);
                        };
                        unminimizeTimer.Start();
                    });
                }
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "Failed to launch MainWindow");
                MessageBox.Show("Could not launch Browser.\n" + ex.Message + "\n\nAt: " + (ex.StackTrace ?? "").Split('\n')[0].Trim());
                Shutdown();
            }
        }
    }

    private async void RunStandby(StartupEventArgs e)
    {
        int parentPid = 0;
        foreach (var a in e.Args)
        {
            if (a.StartsWith("--parent-pid=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(a.Substring("--parent-pid=".Length), out var p))
                parentPid = p;
        }

        var claim = parentPid > 0 ? WebAppJournalService.TryClaimStandby(parentPid) : null;
        if (claim == null)
        {
            LogService.Write("BOOT", $"Standby not needed or already running (parent {parentPid}).");
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        LogService.Write("BOOT", $"Mode: Web App standby watcher (parent {parentPid}).");

        try
        {
            System.Diagnostics.Process? parent = null;
            try { parent = System.Diagnostics.Process.GetProcessById(parentPid); }
            catch { }

            bool gone = parent == null;
            while (!gone)
            {
                await System.Threading.Tasks.Task.Delay(1500);
                try { gone = parent!.HasExited; }
                catch { gone = true; }

                if (!gone && !WebAppJournalService.JournalExists(parentPid))
                {
                    LogService.Write("BOOT", "Standby: parent has no Web Apps left. Watcher exiting.");
                    GC.KeepAlive(claim);
                    Shutdown();
                    return;
                }
            }
            GC.KeepAlive(claim);

            var entries = WebAppJournalService.Read(parentPid);
            WebAppJournalService.Delete(parentPid);
            if (entries.Count == 0)
            {
                LogService.Write("BOOT", "Standby: parent ended with nothing to restore.");
                Shutdown();
                return;
            }

            LogService.Write("BOOT", $"Standby: parent {parentPid} ended unexpectedly. Restoring {entries.Count} Web App window(s).");

            if (BackgroundKeepAliveService.TryActivateExistingInstance("PING"))
            {
                foreach (var entry in entries)
                    BackgroundKeepAliveService.TryActivateExistingInstance("OPENWEBAPP:" + entry.Id);
                LogService.Write("BOOT", "Standby: another Horizon instance is running. Forwarded the Web Apps to it.");
                Shutdown();
                return;
            }

            bool isHost = WebAppHostService.TryBecomeHost();
            for (int i = 0; !isHost && i < 10; i++)
            {
                await System.Threading.Tasks.Task.Delay(500);
                if (BackgroundKeepAliveService.TryActivateExistingInstance("PING"))
                {
                    foreach (var entry in entries)
                        BackgroundKeepAliveService.TryActivateExistingInstance("OPENWEBAPP:" + entry.Id);
                    LogService.Write("BOOT", "Standby: another host took over. Forwarded the Web Apps to it.");
                    Shutdown();
                    return;
                }
                isHost = WebAppHostService.TryBecomeHost();
            }

            if (!isHost)
            {
                LogService.Write("BOOT", "Standby: could not become the host. Giving up.");
                Shutdown();
                return;
            }

            foreach (var entry in entries) entry.Generation++;
            entries.RemoveAll(x => x.Generation >= 3);
            if (entries.Count == 0)
            {
                LogService.Write("BOOT", "Standby: restore loop guard reached. Nothing restored.");
                Shutdown();
                return;
            }

            try { SettingsService.Load(); }
            catch (Exception ex) { LogService.RecordCrash(ex, "Standby Settings Load"); }

            try { ThemeService.ApplyTheme(SettingsService.Current.Theme); }
            catch (Exception ex) { LogService.RecordCrash(ex, "Standby ApplyTheme"); }

            _ = StealthEnvironment.InitializeAsync();
            BackgroundKeepAliveService.OnMainWindowRequested = ShowNewMainWindow;

            int opened = WebAppHostService.RestoreHost(entries);
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            if (opened == 0) Shutdown();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "RunStandby");
            Shutdown();
        }
    }

    private static bool _mainServicesStarted;
    private static bool _isBrowserProcess;

    private static void ApplyBrowserWindowIdentity(Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) return;

            string exe = Environment.ProcessPath ?? "";
            bool a = WindowPropertyStore.SetString(hwnd, WindowPropertyStore.PidRelaunchCommand, "\"" + exe + "\"");
            bool b = WindowPropertyStore.SetString(hwnd, WindowPropertyStore.PidRelaunchIconResource, exe + ",0");
            bool c = WindowPropertyStore.SetString(hwnd, WindowPropertyStore.PidRelaunchDisplayName, "Horizon Browser");
            bool d = WindowPropertyStore.SetString(hwnd, WindowPropertyStore.PidAppUserModelId, "Horizon.Stealth.Browser");
            LogService.Write("BOOT", $"Browser window identity set in host process. relaunch={a} icon={b} name={c} id={d}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ApplyBrowserWindowIdentity");
        }
    }

    private void ShowNewMainWindow(string? startUrl)
    {
        try
        {
            if (!_mainServicesStarted)
            {
                _mainServicesStarted = true;
                GeoIpService.KickOffBackgroundRefresh();
                VpnThroughputHistoryService.Start();
            }

            var browser = new MainWindow(startUrl, false);
            if (!_isBrowserProcess) ApplyBrowserWindowIdentity(browser);
            browser.Show();
            if (browser.WindowState == WindowState.Minimized) browser.WindowState = WindowState.Normal;
            browser.Activate();
            LogService.Write("BOOT", "Main window created in the running process.");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ShowNewMainWindow");
            MessageBox.Show("Could not open the main window.\n" + ex.Message, "Horizon", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LaunchWebApp(StartupEventArgs e, string webAppId)
    {
        try
        {
            var manifest = WebAppService.Load(webAppId);
            if (manifest == null)
            {
                LogService.Write("BOOT", $"WebApp manifest not found or invalid: {webAppId}");
                MessageBox.Show("This Web App is no longer installed.", "Horizon Web App", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            WebAppHostService.AllowForeground();

            if (BackgroundKeepAliveService.TryActivateExistingInstance("OPENWEBAPP:" + webAppId))
            {
                LogService.Write("BOOT", $"WebApp {webAppId} routed to the running Horizon.");
                Shutdown();
                return;
            }

            bool routed = WebAppHostService.TrySendToHost(webAppId);
            bool isHost = false;
            if (!routed)
            {
                isHost = WebAppHostService.TryBecomeHost();
                for (int i = 0; !routed && !isHost && i < 25; i++)
                {
                    System.Threading.Thread.Sleep(200);
                    routed = WebAppHostService.TrySendToHost(webAppId);
                    if (!routed) isHost = WebAppHostService.TryBecomeHost();
                }
            }

            if (routed)
            {
                LogService.Write("BOOT", $"WebApp {webAppId} routed to the running Web App host.");
                Shutdown();
                return;
            }

            if (!isHost)
            {
                LogService.Write("BOOT", $"WebApp {webAppId} could not be routed or hosted.");
                MessageBox.Show("Could not start the Web App host. Check the log for details.", "Horizon Web App", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            LogService.Write("BOOT", "Mode: Web App Host");
            base.OnStartup(e);

            try { SettingsService.Load(); }
            catch (Exception ex) { LogService.RecordCrash(ex, "WebAppHost Settings Load"); }

            try { ThemeService.ApplyTheme(SettingsService.Current.Theme); }
            catch (Exception ex) { LogService.RecordCrash(ex, "WebAppHost ApplyTheme"); }

            _ = StealthEnvironment.InitializeAsync();
            BackgroundKeepAliveService.OnMainWindowRequested = ShowNewMainWindow;
            WebAppHostService.StartHost(manifest);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "LaunchWebApp");
            MessageBox.Show("Could not launch the Web App.\n" + ex.Message, "Horizon Web App", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.RecordCrash(e.Exception, "UI Dispatcher");
        e.Handled = true; 
        WebAppJournalService.CrashShutdown = true;
        ShowCrashDialog(e.Exception);
        Shutdown();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogService.RecordCrash(ex, "AppDomain (Critical)");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogService.RecordCrash(e.Exception, "Background Task");
        e.SetObserved(); 
    }

    private void ShowCrashDialog(Exception ex)
    {
        MessageBox.Show(
            $"A critical error occurred.\n\nError: {ex.Message}\n\nCheck the 'logs' folder for the Crash Tape.", 
            "Horizon Black Box", 
            MessageBoxButton.OK, 
            MessageBoxImage.Error);
    }
}