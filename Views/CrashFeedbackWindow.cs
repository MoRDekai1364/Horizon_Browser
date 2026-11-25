using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Horizon.Stealth.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Horizon.Stealth.Views;

public sealed class CrashReportPayload
{
    public string TimestampUtc { get; set; } = "";
    public string CrashSource { get; set; } = "";
    public string BrowserVersion { get; set; } = "";
    public string UpdateChannel { get; set; } = "";
    public string ReleaseBuildId { get; set; } = "";
    public string ExceptionType { get; set; } = "";
    public string ExceptionMessage { get; set; } = "";
    public string StackTrace { get; set; } = "";
    public string InnerException { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
    public string Architecture { get; set; } = "";
    public long WorkingSetMb { get; set; }
    public string LastActiveUrl { get; set; } = "";
    public int RepairAttemptsPrior { get; set; }
    public string SelfRepairLogPath { get; set; } = "";
    public string LatestLogPath { get; set; } = "";
    public string LatestCrashLogPath { get; set; } = "";
}

/// <summary>
/// Fully independent crash feedback window launched in a separate process (--crash-report=...)
/// whenever Horizon Browser encounters an unhandled crash. Uses its own isolated WebView2 profile
/// and automatically pre-fills crash metadata, Release/Build ID, stack trace, and log attachments.
/// </summary>
public sealed class CrashFeedbackWindow : Window
{
    private const string FeedbackUrl = "https://feedbackcol-nszemvwz.manus.space/feedback";
    private const string ProjectName = "Horizon_Browser";
    private const string GitHubUrl = "https://github.com/MoRDekai1364/Horizon_Browser.git";
    private const long MaxLogBytes = 2L * 1024 * 1024;

    private const string CrashAutoFillScript = """
(function () {
    var ctx = null;
    var pending = null;

    function describe(el) {
        var t = [el.placeholder, el.getAttribute('aria-label'), el.name, el.id].join(' ');
        if (el.labels) {
            for (var i = 0; i < el.labels.length; i++) t += ' ' + el.labels[i].textContent;
        }
        var by = el.getAttribute('aria-labelledby');
        if (by) {
            by.split(' ').forEach(function (id) {
                var n = document.getElementById(id);
                if (n) t += ' ' + n.textContent;
            });
        }
        var p = el.parentElement;
        for (var d = 0; d < 3 && p; d++, p = p.parentElement) {
            if (p.querySelectorAll('input,textarea,select').length !== 1) break;
            var l = p.querySelector('label');
            if (l) { t += ' ' + l.textContent; break; }
            var s = el.previousElementSibling;
            if (s) { t += ' ' + s.textContent; break; }
        }
        return t;
    }

    function setValue(el, v) {
        var proto = el.tagName === 'TEXTAREA'
            ? HTMLTextAreaElement.prototype
            : (el.tagName === 'SELECT' ? HTMLSelectElement.prototype : HTMLInputElement.prototype);
        var setter = Object.getOwnPropertyDescriptor(proto, 'value');
        if (setter && setter.set) setter.set.call(el, v);
        else el.value = v;
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function attachFiles(input) {
        if (input.dataset.hzCrashFiles || !ctx || !ctx.files || !ctx.files.length) return;
        if (input.files && input.files.length) return;
        var dt = new DataTransfer();
        var list = input.multiple ? ctx.files : ctx.files.slice(0, 1);
        list.forEach(function (f) {
            var bin = atob(f.data);
            var bytes = new Uint8Array(bin.length);
            for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            dt.items.add(new File([bytes], f.name, { type: f.type }));
        });
        input.dataset.hzCrashFiles = '1';
        input.files = dt.files;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function scan() {
        if (!ctx) return;

        var fileInputs = document.querySelectorAll('input[type=file]');
        for (var i = 0; i < fileInputs.length; i++) attachFiles(fileInputs[i]);

        var els = document.querySelectorAll('input,textarea');
        for (var j = 0; j < els.length; j++) {
            var el = els[j];
            var type = (el.type || '').toLowerCase();
            if (el.dataset.hzCrashFilled || el.value) continue;
            if (type === 'hidden' || type === 'checkbox' || type === 'radio' || type === 'file' || type === 'submit' || type === 'button') continue;
            var text = describe(el);
            if (/github[\s_-]*url/i.test(text)) {
                el.dataset.hzCrashFilled = '1';
                setValue(el, ctx.github);
            } else if (/project[\s_-]*name/i.test(text)) {
                el.dataset.hzCrashFilled = '1';
                setValue(el, ctx.project);
            } else if (/title|subject|summary/i.test(text) && ctx.title) {
                el.dataset.hzCrashFilled = '1';
                setValue(el, ctx.title);
            } else if (/description|details|message|steps|crash/i.test(text) && ctx.description) {
                el.dataset.hzCrashFilled = '1';
                setValue(el, ctx.description);
            }
        }

        // Select "Bug" / "Crash" if there is a category/type select
        var selects = document.querySelectorAll('select');
        for (var k = 0; k < selects.length; k++) {
            var sel = selects[k];
            if (sel.dataset.hzCrashFilled) continue;
            for (var o = 0; o < sel.options.length; o++) {
                if (/bug|crash|error/i.test(sel.options[o].text + ' ' + sel.options[o].value)) {
                    sel.dataset.hzCrashFilled = '1';
                    setValue(sel, sel.options[o].value);
                    break;
                }
            }
        }
    }

    function schedule() {
        if (pending) return;
        pending = setTimeout(function () { pending = null; scan(); }, 120);
    }

    function start() {
        new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true });
        setInterval(scan, 700);
    }

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', function (e) {
            ctx = e.data;
            scan();
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
""";

    private readonly CrashReportPayload _payload;
    private readonly string _payloadPath;
    private readonly WebView2 _web = new();
    private readonly Border _consentOverlay;
    private readonly StackPanel _askPanel;
    private readonly StackPanel _gatheringPanel;
    private readonly TextBlock _gatherStatusText;
    private readonly TextBlock _headerSubText;
    private string? _reproDebugLogPath;
    private bool _webInitialized;

    public CrashFeedbackWindow(CrashReportPayload payload, string payloadPath = "")
    {
        _payload = payload;
        _payloadPath = payloadPath;
        Title = "Horizon Browser \u2014 Crash Reporter";
        Width = 720;
        Height = 840;
        MinWidth = 480;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x16));

        var root = new DockPanel { LastChildFill = true };

        // Sleek dark-glass crash header bar
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF4, 0x16, 0x12, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0xEF, 0x44, 0x44)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12, 16, 12)
        };
        DockPanel.SetDock(header, Dock.Top);

        var hdrDock = new DockPanel();

        var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(btnStack, Dock.Right);

        var btnRestart = new Button
        {
            Content = "Restart Horizon",
            Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x93, 0xC5, 0xFD)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnRestart.Click += (_, _) => RestartHorizonBrowser();

        var btnClose = new Button
        {
            Content = "Close",
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnClose.Click += (_, _) => Close();

        btnStack.Children.Add(btnRestart);
        btnStack.Children.Add(btnClose);
        hdrDock.Children.Add(btnStack);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoStack.Children.Add(new TextBlock
        {
            Text = "\u26A0 Horizon Browser encountered an unexpected crash",
            Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5)),
            FontSize = 14,
            FontWeight = FontWeights.Bold
        });
        _headerSubText = new TextBlock
        {
            Text = $"v{payload.BrowserVersion} ({payload.UpdateChannel})  \u2022  Build {payload.ReleaseBuildId}  \u2022  {payload.ExceptionType}: {payload.ExceptionMessage}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 12, 0)
        };
        infoStack.Children.Add(_headerSubText);
        hdrDock.Children.Add(infoStack);

        header.Child = hdrDock;
        root.Children.Add(header);

        // Body host: WebView2 + Consent / Background Debug Gathering overlay
        var bodyGrid = new Grid();
        bodyGrid.Children.Add(_web);

        _consentOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x16)),
            Padding = new Thickness(32)
        };

        var card = new Border
        {
            MaxWidth = 520,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x15, 0x18, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(28, 24, 28, 24)
        };

        var cardContainer = new Grid();

        // Step 1: Ask User Consent Panel
        _askPanel = new StackPanel();
        _askPanel.Children.Add(new TextBlock
        {
            Text = "Would you like to report this crash?",
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        _askPanel.Children.Add(new TextBlock
        {
            Text = "If you choose to report it, Horizon will briefly run in Debug Mode in the background to gather ultra-detailed diagnostic logs and try to reproduce the issue, then automatically attach everything (version, Build ID, crash tape, and debug logs) to the feedback form.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var askButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnNoRestart = new Button
        {
            Content = "No, Restart Horizon",
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 10, 0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnNoRestart.Click += (_, _) => { RestartHorizonBrowser(); Close(); };

        var btnYesReport = new Button
        {
            Content = "Yes, Gather Debug Data & Report",
            Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x93, 0xC5, 0xFD)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 8, 16, 8),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnYesReport.Click += async (_, _) => await BeginGatherAndReportAsync();

        askButtons.Children.Add(btnNoRestart);
        askButtons.Children.Add(btnYesReport);
        _askPanel.Children.Add(askButtons);

        // Step 2: Gathering Crucial Diagnostic Data Panel
        _gatheringPanel = new StackPanel { Visibility = Visibility.Collapsed };
        _gatheringPanel.Children.Add(new TextBlock
        {
            Text = "Please wait \u2014 gathering crucial diagnostic data...",
            Foreground = Brushes.White,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        _gatherStatusText = new TextBlock
        {
            Text = "Starting Horizon Browser in Debug Mode in the background to capture detailed diagnostic logs and attempt to reproduce the issue. This takes only a few seconds...",
            Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        _gatheringPanel.Children.Add(_gatherStatusText);
        _gatheringPanel.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Height = 6,
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0)
        });

        cardContainer.Children.Add(_askPanel);
        cardContainer.Children.Add(_gatheringPanel);
        card.Child = cardContainer;
        _consentOverlay.Child = card;
        bodyGrid.Children.Add(_consentOverlay);

        root.Children.Add(bodyGrid);
        Content = root;

        Closed += (_, _) => { try { _web.Dispose(); } catch { } };
    }

    private static void RestartHorizonBrowser()
    {
        try
        {
            string exe = DefaultBrowserService.ResolveExecutablePath();
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch { }
    }

    private async Task BeginGatherAndReportAsync()
    {
        _askPanel.Visibility = Visibility.Collapsed;
        _gatheringPanel.Visibility = Visibility.Visible;

        try
        {
            string exe = DefaultBrowserService.ResolveExecutablePath();
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe) && !string.IsNullOrWhiteSpace(_payloadPath))
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Horizon_Browser", "CrashReports");
                Directory.CreateDirectory(dir);
                string tracePath = Path.Combine(dir, $"debug_repro_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");

                _gatherStatusText.Text = "Running background Debug Mode reproduction probe and capturing deep telemetry...";

                await Task.Run(async () =>
                {
                    try
                    {
                        using var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = $"--debug-mode --repro-crash=\"{_payloadPath}\" --debug-output=\"{tracePath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });

                        if (proc != null)
                        {
                            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
                            try { await proc.WaitForExitAsync(cts.Token); }
                            catch (OperationCanceledException)
                            {
                                try { if (!proc.HasExited) proc.Kill(); } catch { }
                            }
                        }
                    }
                    catch { }
                });

                if (File.Exists(tracePath) && new FileInfo(tracePath).Length > 0)
                {
                    _reproDebugLogPath = tracePath;
                }
            }

            _gatherStatusText.Text = "Attaching crash report, Release/Build ID, and Debug Mode logs to the feedback form...";
            await InitializeAsync();
            _consentOverlay.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(_reproDebugLogPath))
            {
                _headerSubText.Text = $"\u2713 Debug Mode diagnostic logs attached  \u2022  v{_payload.BrowserVersion} ({_payload.UpdateChannel})  \u2022  Build {_payload.ReleaseBuildId}";
                _headerSubText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
            }
        }
        catch
        {
            await InitializeAsync();
            _consentOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async Task InitializeAsync()
    {
        if (_webInitialized) return;
        _webInitialized = true;
        try
        {
            // Fully independent WebView2 user-data folder isolated from both MainWindow and FeedbackWindow
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon", "CrashFeedbackWebView");
            Directory.CreateDirectory(folder);

            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(CrashAutoFillScript);
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                core.Navigate(e.Uri);
            };

            string contextJson = await Task.Run(BuildContextJson);
            core.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess)
                {
                    try { core.PostWebMessageAsJson(contextJson); } catch { }
                }
            };

            core.Navigate(FeedbackUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Horizon crashed ({_payload.ExceptionType}: {_payload.ExceptionMessage}), and the crash reporter WebView could not initialize:\n\n{ex.Message}",
                "Horizon Crash Reporter",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string BuildContextJson()
    {
        string shortMsg = (_payload.ExceptionMessage ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (shortMsg.Length > 80) shortMsg = shortMsg[..80] + "...";

        string title = $"[Crash] {_payload.ExceptionType}: {shortMsg} (v{_payload.BrowserVersion} / {_payload.ReleaseBuildId})";

        var sb = new StringBuilder();
        sb.AppendLine("=== HORIZON BROWSER CRASH REPORT ===");
        sb.AppendLine($"Browser version: {_payload.BrowserVersion}");
        sb.AppendLine($"Update channel: {_payload.UpdateChannel}");
        sb.AppendLine($"Release / Build ID: {_payload.ReleaseBuildId}");
        sb.AppendLine($"Timestamp (UTC): {_payload.TimestampUtc}");
        sb.AppendLine($"Crash source: {_payload.CrashSource}");
        sb.AppendLine($"OS: {_payload.OsVersion} ({_payload.Architecture})");
        sb.AppendLine($".NET Runtime: {_payload.DotNetVersion}");
        sb.AppendLine($"Working Set: {_payload.WorkingSetMb} MB");
        sb.AppendLine($"Self-Repair Attempts Prior: {_payload.RepairAttemptsPrior}");
        if (!string.IsNullOrWhiteSpace(_payload.LastActiveUrl))
            sb.AppendLine($"Last Active URL: {_payload.LastActiveUrl}");
        sb.AppendLine();
        sb.AppendLine($"Exception: {_payload.ExceptionType}");
        sb.AppendLine($"Message: {_payload.ExceptionMessage}");
        if (!string.IsNullOrWhiteSpace(_payload.InnerException))
        {
            sb.AppendLine();
            sb.AppendLine("Inner Exception:");
            sb.AppendLine(_payload.InnerException);
        }
        sb.AppendLine();
        sb.AppendLine("Stack Trace:");
        sb.AppendLine(_payload.StackTrace);
        sb.AppendLine();
        sb.AppendLine("What were you doing right before the crash? (optional):");
        sb.AppendLine();

        string reportText = sb.ToString();

        var files = new List<object>
        {
            new
            {
                name = $"horizon_crash_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt",
                type = "text/plain",
                data = Convert.ToBase64String(Encoding.UTF8.GetBytes(reportText))
            }
        };

        TryAddLogAttachment(files, _reproDebugLogPath);
        TryAddLogAttachment(files, _payload.SelfRepairLogPath);
        TryAddLogAttachment(files, _payload.LatestCrashLogPath);
        TryAddLogAttachment(files, _payload.LatestLogPath);

        return JsonSerializer.Serialize(new
        {
            project = ProjectName,
            github = GitHubUrl,
            title,
            description = reportText,
            files
        });
    }

    private static void TryAddLogAttachment(List<object> files, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            if (len <= 0) return;
            long start = Math.Max(0, len - MaxLogBytes);
            fs.Seek(start, SeekOrigin.Begin);
            byte[] buf = new byte[len - start];
            int total = 0;
            while (total < buf.Length)
            {
                int n = fs.Read(buf, total, buf.Length - total);
                if (n <= 0) break;
                total += n;
            }
            if (total <= 0) return;
            if (total != buf.Length) Array.Resize(ref buf, total);

            files.Add(new
            {
                name = Path.GetFileName(path),
                type = "text/plain",
                data = Convert.ToBase64String(buf)
            });
        }
        catch { }
    }

    /// <summary>
    /// Writes a self-contained crash payload JSON file to CrashReports\ and returns its path.
    /// Shared by both SelfRepairService and CrashFeedbackWindow.
    /// </summary>
    public static string? WriteCrashPayloadFile(Exception ex, string crashSource, int priorRepairAttempts = 0, string selfRepairLogPath = "")
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon_Browser", "CrashReports");
            Directory.CreateDirectory(dir);

            string version = "unknown";
            string channel = "release";
            try
            {
                string versionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                if (File.Exists(versionPath))
                {
                    var parsed = GithubUpdateService.ParseLocalVersion(File.ReadAllText(versionPath));
                    version = parsed.version;
                    channel = parsed.channel;
                }
            }
            catch { }

            string latestCrashLog = FindLatestLogFile("*crash*.log") ?? FindLatestLogFile("*crash*.txt") ?? FindLatestLogFile("*.err") ?? "";
            string latestSessionLog = FindLatestLogFile("*.log") ?? "";
            string lastUrl = "";
            try
            {
                if (Application.Current?.MainWindow is MainWindow mw)
                    lastUrl = mw.CurrentBrowser?.MainWebView?.Source?.ToString() ?? "";
            }
            catch { }

            var payload = new CrashReportPayload
            {
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                CrashSource = crashSource,
                BrowserVersion = version,
                UpdateChannel = channel,
                ReleaseBuildId = BuildIdentityService.GetReleaseBuildId(),
                ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                ExceptionMessage = ex.Message,
                StackTrace = ex.StackTrace ?? "(no stack trace)",
                InnerException = ex.InnerException?.ToString() ?? "",
                OsVersion = Environment.OSVersion.VersionString,
                DotNetVersion = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                WorkingSetMb = Environment.WorkingSet / (1024 * 1024),
                LastActiveUrl = lastUrl,
                RepairAttemptsPrior = priorRepairAttempts,
                SelfRepairLogPath = selfRepairLogPath,
                LatestLogPath = latestSessionLog,
                LatestCrashLogPath = latestCrashLog
            };

            string payloadPath = Path.Combine(dir, $"crash_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Environment.ProcessId}.json");
            File.WriteAllText(payloadPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            return payloadPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a self-contained crash payload to disk and spawns a completely independent
    /// Horizon.Browser.exe --crash-report="..." process so the reporter survives the dying browser.
    /// </summary>
    public static bool LaunchOutOfProcessCrashReporter(Exception ex, string crashSource)
    {
        try
        {
            string exe = DefaultBrowserService.ResolveExecutablePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;

            string? payloadPath = WriteCrashPayloadFile(ex, crashSource);
            if (string.IsNullOrEmpty(payloadPath)) return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--crash-report=\"{payloadPath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindLatestLogFile(string pattern)
    {
        try
        {
            var dirs = new[]
            {
                ConfigService.LogsRoot,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
            }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase);

            string? best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string d in dirs)
            {
                foreach (string f in Directory.EnumerateFiles(d, pattern))
                {
                    DateTime t = File.GetLastWriteTimeUtc(f);
                    if (best == null || t > bestTime)
                    {
                        best = f;
                        bestTime = t;
                    }
                }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }
}
