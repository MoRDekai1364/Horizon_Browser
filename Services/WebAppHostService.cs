using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Horizon.Stealth.Views;

namespace Horizon.Stealth.Services;

public static class WebAppHostService
{
    private const string LogTag = "WEBAPP";
    private const string PipeName = "Horizon.Stealth.WebAppHost";
    private const string MutexName = @"Local\Horizon.Stealth.WebAppHost";
    private const string OpenPrefix = "OPEN:";

    private static readonly Dictionary<string, WebAppWindow> Windows = new(StringComparer.OrdinalIgnoreCase);
    private static Mutex? _mutex;
    private static CancellationTokenSource? _pipeCts;

    public static bool IsHostProcess { get; private set; }

    public static bool TrySendToHost(string id)
    {
        try
        {
            AllowSetForegroundWindow(-1);
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(OpenPrefix + id);
            return true;
        }
        catch
        {
            return false;
        }
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
        StartPipeServer();

        if (Application.Current != null)
            Application.Current.Exit += (_, _) => StopHost();

        LogService.Write(LogTag, "Host started. First app: " + first.Id);
        OpenOrActivate(first);
    }

    public static void OpenOrActivate(WebAppManifest manifest)
    {
        var app = Application.Current;
        if (app == null) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(new Action(() => OpenOrActivate(manifest)));
            return;
        }

        try
        {
            if (Windows.TryGetValue(manifest.Id, out var existing))
            {
                ActivateWindow(existing);
                LogService.Write(LogTag, "Activated existing window: " + manifest.Id);
                return;
            }

            var window = new WebAppWindow(manifest);
            Windows[manifest.Id] = window;
            window.Closed += (_, _) => OnWindowClosed(manifest.Id);
            window.Show();
            ActivateWindow(window);
            LogService.Write(LogTag, $"Opened window: {manifest.Id}. Open windows: {Windows.Count}. Host={IsHostProcess}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppHostService.OpenOrActivate");
            Windows.Remove(manifest.Id);
            if (IsHostProcess && Windows.Count == 0) app.Shutdown();
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
        LogService.Write(LogTag, $"Window closed: {id}. Open windows: {Windows.Count}");

        if (IsHostProcess && Windows.Count == 0)
        {
            LogService.Write(LogTag, "Last Web App window closed. Host exiting.");
            Application.Current?.Shutdown();
        }
    }

    private static void StartPipeServer()
    {
        if (_pipeCts is { IsCancellationRequested: false }) return;

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
                    string? cmd = await reader.ReadLineAsync(token);
                    HandleCommand(cmd);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.Write(LogTag, "Host pipe error: " + ex.Message);
                    try { await Task.Delay(200, token); }
                    catch { break; }
                }
            }
        }, token);
    }

    private static void HandleCommand(string? cmd)
    {
        if (cmd == null || !cmd.StartsWith(OpenPrefix, StringComparison.Ordinal)) return;

        string id = cmd.Substring(OpenPrefix.Length).Trim();
        if (!WebAppService.IsValidId(id))
        {
            LogService.Write(LogTag, "Host rejected invalid id.");
            return;
        }

        var manifest = WebAppService.Load(id);
        if (manifest == null)
        {
            LogService.Write(LogTag, "Host could not load manifest: " + id);
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => OpenOrActivate(manifest)));
    }

    private static void StopHost()
    {
        try { _pipeCts?.Cancel(); } catch { }
        _pipeCts = null;
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
        LogService.Write(LogTag, "Host stopped.");
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}