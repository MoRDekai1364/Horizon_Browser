using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Horizon.Stealth.Services;

public static class BackgroundKeepAliveService
{
    private const string PipeName = "HorizonBrowserActivate_v1";

    private static Forms.NotifyIcon? _trayIcon;
    private static CancellationTokenSource? _pipeCts;
    private static Window? _mainWindow;
    private static bool _allowRealClose;

    public static Action<string?>? OnMainWindowRequested;

    public static bool HasLiveMainWindow => _mainWindow != null;

    /// <summary>
    /// Fired on the UI thread when another process (e.g. a Jump List task) sends
    /// a "MEDIA:&lt;cmd&gt;" message over the pipe instead of "ACTIVATE".
    /// Wired up by MainWindow to its media-widget command handler.
    /// </summary>
    public static Action<string>? OnMediaCommandReceived;
    public static Action<string>? OnOpenUrlReceived;
    public static Action<string>? OnOpenWebAppReceived;

    /// <summary>
    /// Call at App startup before creating MainWindow.
    /// Returns true if a hidden/running instance was signalled — caller must then Shutdown().
    /// </summary>
    public static bool TryActivateExistingInstance(string command = "ACTIVATE")
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var w = new StreamWriter(client) { AutoFlush = true };
            w.WriteLine(command);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Called from MainWindow constructor.</summary>
    public static void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;
        mainWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_mainWindow, mainWindow)) _mainWindow = null;
            DestroyTrayIcon();
        };
        // Always on now, independent of the "hide to tray on close" setting below —
        // this pipe is also what catches Jump List media-control relaunches and
        // routes them back into this instance instead of spawning a new window.
        StartPipeServer();
    }

    /// <summary>Call after Settings window saves to apply the new value immediately.</summary>
    public static void OnSettingChanged()
    {
        if (!SettingsService.Current.BackgroundKeepAliveEnabled)
            DestroyTrayIcon();
    }

    /// <summary>
    /// Call from MainWindow.Closing. Returns true = close was intercepted (hide to tray).
    /// Returns false = let close proceed normally.
    /// </summary>
    public static bool InterceptClose()
    {
        if (_allowRealClose) return false;
        if (!SettingsService.Current.BackgroundKeepAliveEnabled) return false;
        HideToTray();
        return true;
    }

    /// <summary>
    /// Call at startup when launched via the auto-start registry entry ("--tray-start").
    /// Hides the window and shows the tray icon immediately, independent of the
    /// BackgroundKeepAliveEnabled setting.
    /// </summary>
    public static void StartHiddenInTray()
    {
        HideToTray();
    }

    /// <summary>Tray → Exit, or any hard-quit path.</summary>
    public static void ForceShutdown()
    {
        StopPipeServer();
        DestroyTrayIcon();
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    // ── Private ──────────────────────────────────────────────────────────────

    public static void StartPipeServerForHost() => StartPipeServer();

    public static void CloseMainKeepWebApps()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            DestroyTrayIcon();
            var w = _mainWindow;
            if (w == null) return;
            _allowRealClose = true;
            try { w.Close(); }
            finally { _allowRealClose = false; }
        });
    }

    private static void RebuildTrayMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add("Open Horizon", null, (_, _) => ShowFromTray());

        var apps = WebAppHostService.GetOpenApps();
        if (apps.Count > 0)
        {
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(new Forms.ToolStripMenuItem("Web Apps") { Enabled = false });
            foreach (var entry in apps)
            {
                string id = entry.Id;
                menu.Items.Add("    " + entry.Name, null, (_, _) =>
                    Application.Current?.Dispatcher.Invoke(() => WebAppHostService.ActivateById(id)));
            }
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit Horizon (keep Web Apps open)", null, (_, _) => CloseMainKeepWebApps());
            menu.Items.Add("Quit everything (close Web Apps too)", null, (_, _) => ForceShutdown());
        }
        else
        {
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ForceShutdown());
        }
    }

    private static void HideToTray()
    {
        _mainWindow?.Dispatcher.Invoke(() =>
        {
            _mainWindow.Hide();
            EnsureTrayIcon();
        });
    }

    private static void ShowFromTray()
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            var w = _mainWindow;
            if (w == null)
            {
                DestroyTrayIcon();
                OnMainWindowRequested?.Invoke(null);
                return;
            }

            w.Show();
            if (w.WindowState == WindowState.Minimized)
                w.WindowState = WindowState.Normal;
            w.Activate();
            w.Focus();
            DestroyTrayIcon();
        });
    }

    private static void EnsureTrayIcon()
    {
        if (_trayIcon != null) return;

        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var icon    = System.Drawing.SystemIcons.Application;
        try { icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath) ?? icon; } catch { }

        _trayIcon = new Forms.NotifyIcon
        {
            Icon    = icon,
            Text    = "Horizon Browser — running in background",
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => RebuildTrayMenu(menu);
        RebuildTrayMenu(menu);
        _trayIcon.ContextMenuStrip = menu;
    }

    private static void DestroyTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private static void StartPipeServer()
    {
        if (_pipeCts is { IsCancellationRequested: false }) return; // already running

        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var cmd = await reader.ReadLineAsync(token);
                    if (cmd == "ACTIVATE")
                    {
                        ShowFromTray();
                    }
                    else if (cmd != null && cmd.StartsWith("OPENWEBAPP:"))
                    {
                        var webAppId = cmd.Substring("OPENWEBAPP:".Length).Trim();
                        if (WebAppService.IsValidId(webAppId))
                        {
                            Application.Current?.Dispatcher.Invoke(() => OnOpenWebAppReceived?.Invoke(webAppId));
                        }
                    }
                    else if (cmd != null && cmd.StartsWith("OPENURL:"))
                    {
                        var openUrl = cmd.Substring("OPENURL:".Length).Trim();
                        if (Uri.TryCreate(openUrl, UriKind.Absolute, out var parsedUrl) &&
                            (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps))
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                ShowFromTray();
                                OnOpenUrlReceived?.Invoke(parsedUrl.AbsoluteUri);
                            });
                        }
                    }
                    else if (cmd != null && cmd.StartsWith("MEDIA:"))
                    {
                        var mediaCmd = cmd.Substring("MEDIA:".Length);
                        Application.Current?.Dispatcher.Invoke(() => OnMediaCommandReceived?.Invoke(mediaCmd));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* pipe reset — recreate on next loop */ }
            }
        }, token);
    }

    private static void StopPipeServer()
    {
        _pipeCts?.Cancel();
        _pipeCts = null;
    }
}