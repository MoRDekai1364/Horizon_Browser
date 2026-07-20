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
            try { BookmarkService.Load(); }
            catch (Exception ex) { LogService.RecordCrash(ex, "BookmarkService.Load (startup)"); }
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
            _ = StealthEnvironment.InitializeAsync();

            try
            {
                string? startUrl = null;
                bool isWebApp = false;

                for (int i = 0; i < e.Args.Length; i++)
                {
                    var arg = e.Args[i];

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
                MessageBox.Show("Could not launch Browser.\n" + ex.Message);
                Shutdown();
            }
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.RecordCrash(e.Exception, "UI Dispatcher");
        e.Handled = true; 
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