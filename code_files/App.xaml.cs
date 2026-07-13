using System.Windows;
using System.Windows.Threading;
using Horizon.Stealth.Services;
using Horizon.Stealth.Core;
using Horizon.Stealth.Views;

namespace Horizon.Stealth;

public partial class App : Application
{
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
                browser.Show();
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