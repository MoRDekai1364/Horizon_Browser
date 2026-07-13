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

    /// <summary>
    /// Call at App startup before creating MainWindow.
    /// Returns true if a hidden instance was signalled — caller must then Shutdown().
    /// </summary>
    public static bool TryActivateExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var w = new StreamWriter(client) { AutoFlush = true };
            w.WriteLine("ACTIVATE");
            return true;
        }
        catch { return false; }
    }

    /// <summary>Called from MainWindow constructor.</summary>
    public static void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;
        if (SettingsService.Current.BackgroundKeepAliveEnabled)
            StartPipeServer();
    }

    /// <summary>Call after Settings window saves to apply the new value immediately.</summary>
    public static void OnSettingChanged()
    {
        if (SettingsService.Current.BackgroundKeepAliveEnabled)
            StartPipeServer();
        else
        {
            StopPipeServer();
            DestroyTrayIcon();
        }
    }

    /// <summary>
    /// Call from MainWindow.Closing. Returns true = close was intercepted (hide to tray).
    /// Returns false = let close proceed normally.
    /// </summary>
    public static bool InterceptClose()
    {
        if (!SettingsService.Current.BackgroundKeepAliveEnabled) return false;
        HideToTray();
        return true;
    }

    /// <summary>Tray → Exit, or any hard-quit path.</summary>
    public static void ForceShutdown()
    {
        StopPipeServer();
        DestroyTrayIcon();
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    // ── Private ──────────────────────────────────────────────────────────────

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
        _mainWindow?.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
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
        menu.Items.Add("Open Horizon", null, (_, _) => ShowFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ForceShutdown());
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
                    if (cmd == "ACTIVATE") ShowFromTray();
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