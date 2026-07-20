using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Horizon.Stealth.Core;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;
using Horizon.Stealth.Views;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Horizon.Stealth.Controls;

public partial class BrowserView : UserControl
{
    private string? _pendingUrl = null;
    private string? _pendingSaveTitle;
    private string? _pendingSaveUrl;
    private string? _pendingSaveUser;
    private string? _pendingSavePass;
    public event EventHandler<string>? NewTabRequested;
    /// <summary>Live download state fired on every bytes-received tick.</summary>
    public record DownloadInfo(
        double Progress,
        string FilePath,
        bool   IsComplete,
        double SpeedMBs,
        int    EtaSecs,
        string Uri = "");

    public event EventHandler<DownloadInfo>? DownloadProgressChanged;

    private CoreWebView2DownloadOperation? _activeDownloadOp;
    private bool _dlPaused = false;

    private static readonly HashSet<string> _scriptWhitelist = new(StringComparer.OrdinalIgnoreCase);
    private static bool _whitelistLoaded = false;
    private static readonly object _whitelistLock = new();
    private string? _hostCheckScriptId = null;
    private string _lastNavHost = string.Empty;
    private readonly Queue<DateTime> _recentNavTimes = new();
    private string? _warningHost = null;
    private StorePageInfo? _currentStoreInfo;
    private bool _isInstalling = false;
    private bool _installSucceeded = false;
    private bool _googleDefaultAccountSwitchDone = false; 

    public void ToggleDownloadPause()
    {
        if (_activeDownloadOp == null) return;
        if (_dlPaused) { _activeDownloadOp.Resume(); _dlPaused = false; }
        else           { _activeDownloadOp.Pause();  _dlPaused = true;  }
    }

    public void CancelCurrentDownload()
    {
        _activeDownloadOp?.Cancel();
        _activeDownloadOp = null;
        _dlPaused = false;
    }

    public bool IsDownloadPaused => _dlPaused;

    // Extensions must only be loaded once per app session, not once per tab.
    private static bool _extensionsLoaded = false;
    private static readonly object _extensionLock = new object();

    // Cache janitor: only the first tab triggers the flush (profile is shared across all tabs).
    private static bool _cacheJanitorRan = false;
    private static readonly object _janitorLock = new object();

    /// <summary>
    /// Maps lowercase extension folder name → browser-assigned extension ID.
    /// Populated once during LoadExtensionsAsync so popup navigation can use the real IDs.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadedExtensionIds => _loadedExtensionIds;
    private static readonly Dictionary<string, string> _loadedExtensionIds =
        new(StringComparer.OrdinalIgnoreCase);

    private TabViewModel? _tabViewModel;
    public TabViewModel? ViewModel
    {
        get => _tabViewModel;
        set
        {
            if (_tabViewModel != null)
            {
                _tabViewModel.IsActiveTab = false;
            }

            _tabViewModel = value;

            if (_tabViewModel != null && MainWebView?.CoreWebView2 != null)
                WireViewModelEvents();
        }
    }

    private class JsonCookie
    {
        public string name { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
        public string domain { get; set; } = string.Empty;
        public string path { get; set; } = string.Empty;
        public bool secure { get; set; }
        public bool httpOnly { get; set; }
    }

    protected virtual void OnNewTabRequested(string url)
    {
        NewTabRequested?.Invoke(this, url);
    }

    // Chrome headers needed to download CRX files from Google's servers.
    // Without these, Google rejects the request with a redirect to an error page.
    private static readonly System.Net.Http.HttpClient _crxHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
        DefaultRequestHeaders =
        {
            { "User-Agent",      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36" },
            { "Accept",          "*/*" },
            { "Accept-Language", "en-US,en;q=0.9" },
        }
    };

    private static string? _pdfOpenPreference = null; // null = not set, "external", "browser"

    private static string GetPdfPreferencePath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Horizon_Browser", "pdf_pref.txt");

    private static void LoadPdfPreference()
    {
        if (_pdfOpenPreference != null) return;
        try
        {
            string p = GetPdfPreferencePath();
            _pdfOpenPreference = System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p).Trim() : "";
        }
        catch { _pdfOpenPreference = ""; }
    }

    public static void ResetPdfPreference()
    {
        _pdfOpenPreference = "";
        try { System.IO.File.Delete(GetPdfPreferencePath()); } catch { }
    }

    private static bool SavePdfPreference(string value)
    {
        _pdfOpenPreference = value;
        try
        {
            string p = GetPdfPreferencePath();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
            System.IO.File.WriteAllText(p, value);
            return true;
        }
        catch { return false; }
    }

    private bool IsPdfUri(string uri)
    {
        string u = uri.Split('?')[0].ToLowerInvariant();
        return u.EndsWith(".pdf");
    }

    private bool IsPdfMime(CoreWebView2DownloadOperation op)
    {
        try
        {
            string mime = op.MimeType ?? "";
            return mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                || mime.Equals("application/x-pdf", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void HandlePdfDownload(CoreWebView2DownloadStartingEventArgs e, string uri)
    {
        LoadPdfPreference();

        string suggestedName = System.IO.Path.GetFileName(e.ResultFilePath);
        if (string.IsNullOrEmpty(suggestedName))
            try { suggestedName = System.IO.Path.GetFileName(new Uri(uri).AbsolutePath); } catch { }
        if (string.IsNullOrEmpty(suggestedName)) suggestedName = "document.pdf";
        if (!suggestedName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            suggestedName += ".pdf";

        string savePath = System.IO.Path.Combine(SettingsService.Current.DownloadsPath, suggestedName);
        int n = 1;
        while (System.IO.File.Exists(savePath))
            savePath = System.IO.Path.Combine(SettingsService.Current.DownloadsPath,
                System.IO.Path.GetFileNameWithoutExtension(suggestedName) + $" ({n++}).pdf");

        if (_pdfOpenPreference == "external")
        {
            e.ResultFilePath = savePath;
            e.Handled = true;
            WireExternalPdfOpen(e.DownloadOperation, savePath);
            return;
        }

        if (_pdfOpenPreference == "browser")
        {
            e.ResultFilePath = savePath;
            return;
        }

        // No preference — ask
        e.Handled = true;
        var deferral = e.GetDeferral();

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var dlg = new PdfOpenDialog();
                bool? result = dlg.ShowDialog();

                if (result == true && dlg.OpenExternal)
                {
                    if (dlg.Remember) SavePdfPreference("external");
                    e.ResultFilePath = savePath;
                    e.Handled = true;
                    WireExternalPdfOpen(e.DownloadOperation, savePath);
                }
                else
                {
                    if (dlg.Remember) SavePdfPreference("browser");
                    e.ResultFilePath = savePath;
                    e.Handled = false;
                }
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void WireExternalPdfOpen(CoreWebView2DownloadOperation op, string savePath)
    {
        op.StateChanged += (s, args) =>
        {
            if (op.State == CoreWebView2DownloadState.Completed)
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(savePath)
                        {
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        LogService.Write("PDF", $"Failed to open PDF externally: {ex.Message}");
                    }
                });
            }
        };
    }

    private void CoreWebView2_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var op  = e.DownloadOperation;
        var uri = op.Uri ?? "";

        // ── CRX intercept ────────────────────────────────────────────────────
        // WebView2 cannot download .crx files from Google's update servers —
        // the request is rejected because it doesn't carry Chrome's internal
        // credentials.  Cancel the WebView2 download and re-fetch it ourselves
        // using an HttpClient that presents a real Chrome User-Agent.
        bool isCrx = uri.Contains("clients2.google.com") ||
                     uri.Contains("/service/update2/crx") ||
                     uri.EndsWith(".crx", StringComparison.OrdinalIgnoreCase);

        if (isCrx)
        {
            e.Cancel = true;    // stop WebView2 from attempting the download

            // We need to know the extension name/id so we can save it sensibly.
            // Try to get the page title as the filename.
            string pageTitle = MainWebView?.CoreWebView2?.DocumentTitle ?? "extension";
            // Sanitise for filesystem
            foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) pageTitle = pageTitle.Replace(ch, '_');
            if (pageTitle.Length > 80) pageTitle = pageTitle[..80];

            string savePath = System.IO.Path.Combine(
                SettingsService.Current.DownloadsPath,
                pageTitle.Trim('_', ' ') + ".crx");

            // Ensure unique filename
            int n = 1;
            while (System.IO.File.Exists(savePath))
                savePath = System.IO.Path.Combine(
                    SettingsService.Current.DownloadsPath,
                    pageTitle.Trim('_', ' ') + $" ({n++}).crx");

            string downloadUrl = uri;

            // Fire-and-forget the manual download on a background thread
            _ = Task.Run(async () =>
            {
                try
                {
                    Dispatcher.Invoke(() =>
                        DownloadProgressChanged?.Invoke(this, new DownloadInfo(0.0, savePath, false, 0, 0, downloadUrl)));

                    var response = await _crxHttp.GetAsync(
                        downloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long? total = response.Content.Headers.ContentLength;
                    await using var src  = await response.Content.ReadAsStreamAsync();
                    await using var dest = System.IO.File.Create(savePath);

                    byte[] buf = new byte[81920];
                    long downloaded = 0; int read;
                    double crxSpeed = 0; int crxEta = 0;
                    long crxLastBytes = 0; var crxLastTime = DateTime.UtcNow;
                    while ((read = await src.ReadAsync(buf)) > 0)
                    {
                        await dest.WriteAsync(buf.AsMemory(0, read));
                        downloaded += read;
                        double pct = total.HasValue && total.Value > 0
                            ? (double)downloaded / total.Value : 0.5;
                        var now = DateTime.UtcNow;
                        double elapsed = (now - crxLastTime).TotalSeconds;
                        if (elapsed >= 0.5)
                        {
                            crxSpeed = ((downloaded - crxLastBytes) / elapsed) / (1024.0 * 1024.0);
                            crxEta = crxSpeed > 0 && total.HasValue && total.Value > 0
                                ? (int)(((total.Value - downloaded) / (1024.0 * 1024.0)) / crxSpeed) : 0;
                            crxLastBytes = downloaded; crxLastTime = now;
                        }
                        Dispatcher.Invoke(() =>
                            DownloadProgressChanged?.Invoke(this, new DownloadInfo(pct, savePath, false, crxSpeed, crxEta, downloadUrl)));
                    }

                    FluxJanitorService.NotifyDownloadCompleted();
                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressChanged?.Invoke(this, new DownloadInfo(1.0, savePath, true, 0, 0, downloadUrl));
                        LogService.Write("DL", $"CRX saved: {savePath}");

                        System.Windows.MessageBox.Show(
                            $"CRX downloaded successfully:\n{savePath}\n\n" +
                            "To install it in Horizon, use the Extensions sidebar → Install from file.",
                            "CRX Download Complete",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    LogService.Write("DL", $"CRX download failed: {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgressChanged?.Invoke(this, new DownloadInfo(1.0, savePath, true, 0, 0, downloadUrl));
                        System.Windows.MessageBox.Show(
                            $"CRX download failed:\n{ex.Message}",
                            "Download Error",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    });
                }
            });

            return; // do not wire up the normal progress events
        }

        if (IsPdfUri(uri) || IsPdfMime(op))
        {
            HandlePdfDownload(e, uri);
            if (e.Handled) return;
        }

        // ── Normal download progress wiring ──────────────────────────────────
        if (string.IsNullOrEmpty(e.ResultFilePath))
        {
            string fname = string.Empty;
            try { fname = System.IO.Path.GetFileName(new Uri(uri).AbsolutePath); } catch { }
            if (string.IsNullOrEmpty(fname)) fname = "download";
            string dest = System.IO.Path.Combine(SettingsService.Current.DownloadsPath, fname);
            int n2 = 1;
            while (System.IO.File.Exists(dest))
                dest = System.IO.Path.Combine(SettingsService.Current.DownloadsPath,
                    System.IO.Path.GetFileNameWithoutExtension(fname) + $" ({n2++})" + System.IO.Path.GetExtension(fname));
            e.ResultFilePath = dest;
        }
        _activeDownloadOp = op;
        _dlPaused = false;
        double _dlSpeed = 0; int _dlEta = 0;
        long _dlLastBytes = 0; var _dlLastTime = DateTime.UtcNow;
        string _dlUri = op.Uri ?? "";

        op.BytesReceivedChanged += (s, args) =>
        {
            ulong total = (ulong)(op.TotalBytesToReceive ?? 0);
            double progress = total > 0 ? (double)op.BytesReceived / total : 0.0;
            var now = DateTime.UtcNow;
            double elapsed = (now - _dlLastTime).TotalSeconds;
            if (elapsed >= 0.5)
            {
                long delta = (long)op.BytesReceived - _dlLastBytes;
                _dlSpeed = (delta / elapsed) / (1024.0 * 1024.0);
                _dlEta = _dlSpeed > 0 && total > 0
                    ? (int)((((long)total - (long)op.BytesReceived) / (1024.0 * 1024.0)) / _dlSpeed) : 0;
                _dlLastBytes = (long)op.BytesReceived; _dlLastTime = now;
            }
            Dispatcher.Invoke(() => DownloadProgressChanged?.Invoke(this,
                new DownloadInfo(progress, op.ResultFilePath, false, _dlSpeed, _dlEta, _dlUri)));
        };

        op.StateChanged += (s, args) =>
        {
            if (op.State == CoreWebView2DownloadState.Completed ||
                op.State == CoreWebView2DownloadState.Interrupted)
            {
                _activeDownloadOp = null;
                _dlPaused = false;
                FluxJanitorService.NotifyDownloadCompleted();
                Dispatcher.Invoke(() => DownloadProgressChanged?.Invoke(this,
                    new DownloadInfo(1.0, op.ResultFilePath, true, 0, 0, _dlUri)));
            }
        };
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // ── OAuth / auth popup detection ─────────────────────────────────────
        // Sites like Claude.ai open Google/Apple/Microsoft sign-in via
        // window.open(..., 'width=500,height=600').  These calls always carry
        // explicit window dimensions.  We must NOT convert them to a tab —
        // doing so severs window.opener and the parent page never receives the
        // auth token, producing "There was an error logging you in."
        //
        // Fix: hand WebView2 a real child Window + WebView2 instance that
        // shares the same environment.  WebView2 wires window.opener for us
        // when we assign e.NewWindow before completing the deferral.
        bool isPopup = e.WindowFeatures.HasSize  // window.open() with explicit dimensions
                    || IsAuthUrl(e.Uri);          // well-known auth domains (belt-and-braces)

        if (isPopup)
        {
            var deferral = e.GetDeferral();
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    double w = (e.WindowFeatures.HasSize && e.WindowFeatures.Width  > 50) ? e.WindowFeatures.Width  : 520;
                    double h = (e.WindowFeatures.HasSize && e.WindowFeatures.Height > 50) ? e.WindowFeatures.Height : 640;

                    var popupWin = new System.Windows.Window
                    {
                        Title               = "Sign in",
                        Width               = w,
                        Height              = h,
                        WindowStyle         = System.Windows.WindowStyle.ToolWindow,
                        ResizeMode          = System.Windows.ResizeMode.CanResize,
                        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                        ShowInTaskbar       = false
                    };

                    var popupWv = new Microsoft.Web.WebView2.Wpf.WebView2();
                    popupWin.Content = popupWv;
                    popupWin.Show();

                    // Share the same environment so cookies/profile are shared
                    await popupWv.EnsureCoreWebView2Async(MainWebView.CoreWebView2.Environment);

                    // Assigning e.NewWindow before deferral.Complete() is what
                    // makes WebView2 preserve window.opener for the parent page.
                    e.NewWindow = popupWv.CoreWebView2;
                    e.Handled   = true;

                    // Let the popup close itself (window.close()) normally
                    popupWv.CoreWebView2.WindowCloseRequested += (_, _) =>
                        Dispatcher.Invoke(() => popupWin.Close());

                    popupWin.Closed += (_, _) =>
                    {
                        try { popupWv.Dispose(); } catch { }
                    };
                }
                catch (Exception ex)
                {
                    // Fallback: open as a plain tab so the user isn't stuck
                    LogService.Write("POPUP", $"OAuth popup failed, falling back to tab: {ex.Message}");
                    e.Handled = true;
                    if (!string.IsNullOrEmpty(e.Uri) && e.Uri != "about:blank")
                        Dispatcher.Invoke(() => OnNewTabRequested(e.Uri));
                }
                finally
                {
                    deferral.Complete();
                }
            });
            return;
        }

        // ── Regular new-tab link ─────────────────────────────────────────────
        e.Handled = true;
        if (!string.IsNullOrEmpty(e.Uri) && e.Uri != "about:blank")
            OnNewTabRequested(e.Uri);
    }

    /// <summary>
    /// Returns true for well-known OAuth / SSO domains.
    /// Catches cases where a site opens the auth window without explicit dimensions.
    /// </summary>
    private static bool IsAuthUrl(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return false;
        ReadOnlySpan<char> u = uri.AsSpan();
        return u.Contains("accounts.google.com".AsSpan(),    StringComparison.OrdinalIgnoreCase)
            || u.Contains("login.microsoftonline.com".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || u.Contains("appleid.apple.com".AsSpan(),      StringComparison.OrdinalIgnoreCase)
            || u.Contains("auth0.com".AsSpan(),              StringComparison.OrdinalIgnoreCase)
            || u.Contains("okta.com".AsSpan(),               StringComparison.OrdinalIgnoreCase)
            || u.Contains("facebook.com/login".AsSpan(),     StringComparison.OrdinalIgnoreCase);
    }

    private void CoreWebView2_NotificationReceived(object? sender, CoreWebView2NotificationReceivedEventArgs e)
    {
        // Left unhandled: Windows still shows its native toast. The widget
        // observes the same event in parallel via NotificationCenterService.
        var title  = e.Notification.Title ?? string.Empty;
        var body   = e.Notification.Body  ?? string.Empty;
        string origin;
        try { origin = new Uri(MainWebView.CoreWebView2?.Source ?? "").Host; }
        catch { origin = string.Empty; }

        Services.NotificationCenterService.Add(origin, title, body);
    }

    private void CoreWebView2_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        // Defer so we don't block the WebView2 thread; use GetDeferral to answer asynchronously.
        var deferral = e.GetDeferral();

        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                // Notifications now go through WebView2's native permission flow
                // (site can prompt normally; the padlock/site-info panel controls
                // it). We don't auto-allow or auto-deny here — the widget just
                // observes NotificationReceived independently of this decision.
                string kindLabel = e.PermissionKind switch
                {
                    CoreWebView2PermissionKind.Notifications  => "send you notifications",
                    CoreWebView2PermissionKind.Geolocation    => "access your location",
                    CoreWebView2PermissionKind.Microphone     => "use the microphone",
                    CoreWebView2PermissionKind.Camera         => "use the camera",
                    CoreWebView2PermissionKind.ClipboardRead  => "read your clipboard",
                    _                                         => e.PermissionKind.ToString(),
                };

                string host;
                try { host = new Uri(e.Uri).Host; }
                catch { host = e.Uri; }

                var result = System.Windows.MessageBox.Show(
                    $"{host} wants to {kindLabel}.\n\nAllow?",
                    "Permission Request – Horizon",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                e.State = result == System.Windows.MessageBoxResult.Yes
                    ? CoreWebView2PermissionState.Allow
                    : CoreWebView2PermissionState.Deny;
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    public BrowserView()
    {
        InitializeComponent();
        MainWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 19, 19, 19);
        _ = Task.Run(EnsureWhitelistLoaded);
        DisableScriptBtn.Click   += (_, _) => _ = DisableScriptForCurrentSiteAsync();
        DismissWarningBtn.Click  += (_, _) => HideScriptWarning();
        SavePassSaveBtn.Click    += (_, _) => CommitSavePassword();
        SavePassDismissBtn.Click   += (_, _) => HideSavePasswordBar();
        InstallExtensionBtn.Click  += async (_, _) =>
        {
            if (_installSucceeded) { RestartApp(); return; }
            await InstallExtensionAsync();
        };
        InstallExtDismissBtn.Click += (_, _) => HideExtensionInstallBar();
        Loaded += (_, _) => InitializeAsync();
    }

    private void OnBecameVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) return;
        IsVisibleChanged -= OnBecameVisible;
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        if (!IsVisible)
        {
            IsVisibleChanged += OnBecameVisible;
            return;
        }

        try
        {
            await StealthEnvironment.InitializeAsync();

            if (StealthEnvironment.Instance != null)
            {
                await MainWebView.EnsureCoreWebView2Async(StealthEnvironment.Instance);

                MainWebView.CoreWebView2.Settings.UserAgent = Core.StealthEnvironment.GlobalUserAgent;

                MainWebView.PreviewTouchDown += (_, _) =>
                {
                    if (!MainWebView.IsKeyboardFocused)
                        MainWebView.MoveFocus(new System.Windows.Input.TraversalRequest(
                            System.Windows.Input.FocusNavigationDirection.First));
                };

                MainWebView.CoreWebView2.NewWindowRequested    += CoreWebView2_NewWindowRequested;
                MainWebView.CoreWebView2.DownloadStarting      += CoreWebView2_DownloadStarting;
                MainWebView.CoreWebView2.NavigationCompleted   += CoreWebView2_NavigationCompleted;
                MainWebView.CoreWebView2.PermissionRequested   += CoreWebView2_PermissionRequested;
                MainWebView.CoreWebView2.ContextMenuRequested  += CoreWebView2_ContextMenuRequested;
                MainWebView.CoreWebView2.NavigationStarting    += CoreWebView2_NavigationStarting;
                MainWebView.CoreWebView2.NotificationReceived  += CoreWebView2_NotificationReceived;
                MainWebView.CoreWebView2.HistoryChanged        += CoreWebView2_HistoryChanged;

                await InitializeAutomationAsync();

                MainWebView.CoreWebView2.IsDocumentPlayingAudioChanged += async (s, e) =>
                {
                    bool reported = MainWebView.CoreWebView2.IsDocumentPlayingAudio;
                    bool isPlaying = reported;

                    // Notifications (Telegram, WhatsApp, etc.) can momentarily flip
                    // IsDocumentPlayingAudio to true even with no actual media.
                    // Confirm by checking for a non-paused video/audio element.
                    if (reported)
                    {
                        try
                        {
                            string check = await MainWebView.CoreWebView2.ExecuteScriptAsync(
                                "(() => { return [...document.querySelectorAll('video,audio')].some(m => !m.paused && !m.ended && m.readyState > 2) ? 'true' : 'false'; })()");
                            if (check.Trim('"') == "false")
                                isPlaying = false;  // notification, not real media
                        }
                        catch { /* fail open: assume real media */ }
                    }

                    bool finalPlaying = isPlaying;
                    Dispatcher.Invoke(() =>
                    {
                        if (_tabViewModel != null)
                            _tabViewModel.IsPlayingAudio = finalPlaying;
                    });
                };

                MainWebView.CoreWebView2.IsMutedChanged += (s, e) =>
                {
                    bool isMuted = MainWebView.CoreWebView2.IsMuted;
                    Dispatcher.Invoke(() =>
                    {
                        if (_tabViewModel != null)
                            _tabViewModel.IsMuted = isMuted;
                    });
                };

                if (_tabViewModel != null)
                    WireViewModelEvents();

                await StealthEnvironment.ApplyStealthStrategies(MainWebView.CoreWebView2);

                
                try
                {
                    await MainWebView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Browser.grantPermissions",
                        "{\"permissions\":[\"clipboardReadWrite\",\"clipboardSanitizedWrite\"]}");
                }
                catch { /* graceful degradation on older WebView2 SDK versions */ }

                // Apply per-tab language BEFORE the first navigation so the
                // initial HTTP request already carries the correct Accept-Language header.
                if (SettingsService.Current.PerTabLanguageEnabled && _tabViewModel != null)
                    await ApplyLanguageAsync(_tabViewModel.Language);

                // ── Periodic HTTP cache janitor ──────────────────────────────────────
                // Flushes DiskCache every N days to prevent stale-cache bugs on sites
                // like YouTube. Cookies, localStorage, and IndexedDB are never touched.
                bool shouldFlushCache = false;
                lock (_janitorLock)
                {
                    if (!_cacheJanitorRan && CacheJanitorService.IsDue())
                    {
                        _cacheJanitorRan = true;
                        shouldFlushCache = true;
                    }
                }
                InjectRecoveryCookies();

                if (!string.IsNullOrEmpty(_pendingUrl))
                {
                    Navigate(_pendingUrl);
                    _pendingUrl = null;
                }
                else
                {
                    Navigate(SettingsService.Current.HomePage);
                }

                if (shouldFlushCache)
                {
                    _ = MainWebView.CoreWebView2.Profile
                            .ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache)
                            .ContinueWith(_ => CacheJanitorService.RecordCleared(),
                                          TaskContinuationOptions.OnlyOnRanToCompletion);
                }
            }
        }
        catch (InvalidOperationException)
        {
            
        }
        catch (System.Runtime.InteropServices.COMException comEx) when ((uint)comEx.HResult == 0x80070578)
        {
            await Task.Delay(500);
            InitializeAsync();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BrowserView.Initialize");
        }
    }

    /// <summary>
    /// Loads unpacked Chromium extensions from %LocalAppData%\Horizon_Browser\Extensions\.
    /// Each subfolder is treated as one extension.  Only folders that contain a manifest.json are loaded.
    /// Settings flags (ConsentOMaticEnabled / AdGuardEnabled) gate the relevant extensions.
    /// </summary>
    
	/// <summary>
    /// Returns true if the manifest.json contains Firefox-only markers that
    /// make the extension incompatible with WebView2/Chromium.
    /// </summary>
    private static bool IsFirefoxOnlyExtension(string manifestPath)
    {
        try
        {
            string text = System.IO.File.ReadAllText(manifestPath);
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var root = doc.RootElement;

            // browser_specific_settings.gecko with an id = definitive Firefox packaging
            if (root.TryGetProperty("browser_specific_settings", out var bss) &&
                bss.TryGetProperty("gecko", out var gecko) &&
                gecko.TryGetProperty("id", out _))
                return true;

            // Older key used by pre-MV3 Firefox extensions
            if (root.TryGetProperty("applications", out var apps) &&
                apps.TryGetProperty("gecko", out _))
                return true;

            // XPI packaging: background.scripts without a service_worker is also
            // common in Firefox MV2 — but that alone isn't conclusive, so only
            // flag it if BOTH gecko block and no Chrome key are present.
        }
        catch { }
        return false;
    }
	
	
	private async Task LoadExtensionsAsync()
    {
        lock (_extensionLock)
        {
            if (_extensionsLoaded) return;
            _extensionsLoaded = true;   // guard: prevent parallel loads
        }

        if (MainWebView?.CoreWebView2 == null)
        {
            // CoreWebView2 not ready yet — allow retry on the next tab
            lock (_extensionLock) _extensionsLoaded = false;
            return;
        }

        string extensionsRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Horizon_Browser", "Extensions");

        if (!System.IO.Directory.Exists(extensionsRoot)) return;

        // ── Build the list of extension dirs to load (filter first, then load in parallel) ──
        var toLoad = new List<(string dir, string folderName)>();

        foreach (var extDir in System.IO.Directory.GetDirectories(extensionsRoot))
        {
            string manifest   = System.IO.Path.Combine(extDir, "manifest.json");
            if (!System.IO.File.Exists(manifest)) continue;

            string folderName = System.IO.Path.GetFileName(extDir).ToLowerInvariant();

            if (IsFirefoxOnlyExtension(manifest))
            {
                LogService.Write("EXT", $"Skipped Firefox-only extension: '{folderName}'");
                continue;
            }
            if (folderName.Contains("consent") && !SettingsService.Current.ConsentOMaticEnabled)
            { LogService.Write("EXT", $"Skipped (disabled): {folderName}"); continue; }
            if (folderName.Contains("adguard") && !SettingsService.Current.AdGuardEnabled)
            { LogService.Write("EXT", $"Skipped (disabled): {folderName}"); continue; }
            if (folderName == "kjchkpkjpiloipaonppkmepcbhcncedo")
            { LogService.Write("EXT", $"Skipped (crash suspect): {folderName}"); continue; }

            toLoad.Add((extDir, folderName));
        }

        // ── Load all qualifying extensions concurrently ───────────────────────
        // AddBrowserExtensionAsync is safe to call in parallel on the same profile.
        int loaded = 0, failed = 0;
        var tasks = toLoad.Select(async item =>
        {
            try
            {
                var ext = await MainWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(item.dir);
                try { if (!ext.IsEnabled) await ext.EnableAsync(true); } catch { }
                lock (_extensionLock) _loadedExtensionIds[item.folderName] = ext.Id;
                LogService.Write("EXT", $"Loaded: '{item.folderName}' → ID={ext.Id}");
                System.Threading.Interlocked.Increment(ref loaded);
            }
            catch (Exception ex)
            {
                LogService.Write("EXT", $"FAILED '{item.folderName}': {ex.GetType().Name}: {ex.Message}");
                System.Threading.Interlocked.Increment(ref failed);
            }
        });

        await Task.WhenAll(tasks);

        LogService.Write("EXT", $"Extension loading done — {loaded} loaded, {failed} failed.");

        if (loaded == 0 && failed > 0)
            lock (_extensionLock) _extensionsLoaded = false;
    }

    private void WireViewModelEvents()
    {
        if (MainWebView?.CoreWebView2 != null && _tabViewModel != null)
        {
            _tabViewModel.IsPlayingAudio = MainWebView.CoreWebView2.IsDocumentPlayingAudio;
            _tabViewModel.IsMuted        = MainWebView.CoreWebView2.IsMuted;
        }
    }

    private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_extensionsLoaded) _ = LoadExtensionsAsync();

        string navHost = MainWebView?.Source?.Host ?? string.Empty;
        if (!string.IsNullOrEmpty(navHost))
        {
            if (navHost != _lastNavHost) { _lastNavHost = navHost; _recentNavTimes.Clear(); }
            _recentNavTimes.Enqueue(DateTime.UtcNow);
            while (_recentNavTimes.Count > 0 && (DateTime.UtcNow - _recentNavTimes.Peek()).TotalSeconds > 5)
                _recentNavTimes.Dequeue();
            lock (_whitelistLock)
            {
                if (_recentNavTimes.Count >= 3 && !_scriptWhitelist.Contains(navHost) && _warningHost != navHost)
                    Dispatcher.Invoke(() => ShowScriptWarning(navHost));
            }
        }

        if (e.IsSuccess)
        {
            string storeCheckUrl = MainWebView?.Source?.ToString() ?? string.Empty;
            var storeInfo = ExtensionInstaller.Detect(storeCheckUrl);
            Dispatcher.Invoke(() => { if (storeInfo.IsStorePage) ShowExtensionInstallBar(storeInfo); else HideExtensionInstallBar(); });
        }

        _ = TryDetectGoogleAccountsAsync();

        if (!e.IsSuccess || _tabViewModel == null) return;

        // Apply per-tab language preference
        if (SettingsService.Current.PerTabLanguageEnabled && !string.IsNullOrEmpty(_tabViewModel.Language))
            await ApplyLanguageAsync(_tabViewModel.Language);

        try
        {
            string url = MainWebView.Source?.ToString() ?? string.Empty;
            // Extract only the meta/og tags — not the full DOM.
            string rawJson = await MainWebView.CoreWebView2!.ExecuteScriptAsync(
                @"(function(){
                    var metas = Array.from(document.querySelectorAll('meta[content]'));
                    return JSON.stringify(metas.slice(0,30).map(m=>m.outerHTML).join(''));
                })()");
            string html = JsonSerializer.Deserialize<string>(rawJson) ?? string.Empty;
            var colors = await TabColorPaletteExtractor.ExtractAsync(url, html);
            if (colors.Count >= 2)
                _tabViewModel.PaletteColors = colors;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BrowserView.PaletteExtract");
        }

        _ = TryInjectAccountSwitcherAsync();
    }

    private void InjectRecoveryCookies()
    {
        try
        {
            string recoveryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "twitch_cookies.json");

            if (File.Exists(recoveryPath))
            {
                string jsonContent = File.ReadAllText(recoveryPath);
                var cookies = JsonSerializer.Deserialize<List<JsonCookie>>(jsonContent);

                if (cookies != null)
                {
                    var manager = MainWebView.CoreWebView2?.CookieManager;
                    if (manager == null) return;

                    int successCount = 0;

                    foreach (var c in cookies)
                    {
                        if (c.domain.Contains("twitch.tv"))
                        {
                            try
                            {
                                var cookie = manager.CreateCookie(c.name, c.value, c.domain, c.path ?? "/");
                                cookie.IsSecure   = c.secure;
                                cookie.IsHttpOnly = c.httpOnly;
                                manager.AddOrUpdateCookie(cookie);
                                successCount++;
                            }
                            catch { }
                        }
                    }

                    if (successCount > 0)
                        LogService.Write("INJECT", $"Passive recovery injected {successCount} cookies.");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "InjectRecoveryCookies (Passive)");
        }
    }

    public async Task AttemptActiveRecovery()
    {
        string targetDomain = "twitch.tv";
        try
        {
            if (MainWebView != null && MainWebView.Source != null)
                targetDomain = MainWebView.Source.Host.Replace("www.", "");
        }
        catch { }

        var dialog = new LoginRecoveryDialog(targetDomain);
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true)
        {
            var browserInfo = BrowserDetectionService.DetectDefaultBrowser();
            int injectedCount = 0;

            if (browserInfo.IsChromium)
            {
                try
                {
                    var cookies = ChromiumHarvester.Harvest(browserInfo.UserDataPath, targetDomain);
                    injectedCount = InjectCookieList(cookies);
                }
                catch (Exception ex)
                {
                    LogService.RecordCrash(ex, "ActiveRecovery");
                }
            }

            if (injectedCount > 0)
            {
                MainWebView?.Reload();
                MessageBox.Show($"Success! Synced {injectedCount} session tokens for {targetDomain}.", "Identity Bridge");
            }
            else
            {
                var askManual = MessageBox.Show(
                    $"AUTOMATIC SYNC FAILED.\n\n" +
                    $"We could not find a session for '{targetDomain}' in {browserInfo.Name}.\n" +
                    $"Do you want to try the Manual Method instead?",
                    "Manual Recovery",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (askManual == MessageBoxResult.Yes)
                {
                    var askExt = MessageBox.Show(
                        "Do you need the 'Cookie-Editor' extension?\n\n" +
                        "Click YES to open the download page in your default browser.\n" +
                        "Click NO if you already have it installed.",
                        "Step 1: Preparation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (askExt == MessageBoxResult.Yes)
                    {
                        try
                        {
                            var ps = new System.Diagnostics.ProcessStartInfo("https://chromewebstore.google.com/detail/ojfebgpkimhlhcblbalbfjblapadhbol")
                            { UseShellExecute = true };
                            System.Diagnostics.Process.Start(ps);
                        }
                        catch { MessageBox.Show("Could not open link. Please search for 'Cookie-Editor' on Google."); }
                    }

                    MessageBox.Show(
                        "INSTRUCTIONS:\n\n" +
                        $"1. Go to {targetDomain} in your main browser and Log In.\n\n" +
                        "2. Open Cookie-Editor -> Click 'Export' (Arrow UP ⬆️) -> 'Export as JSON'.\n" +
                        "   (Do NOT use the Arrow Down ⬇️ button!)\n\n" +
                        "3. I will open the App Folder next.\n\n" +
                        "4. Create a file named 'cookies.json', paste the code inside, and Save.\n\n" +
                        "💡 TIP: To fix other websites later, just repeat this process and overwrite 'cookies.json' again.",
                        "Step 2: The Harvest",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    System.Diagnostics.Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory);

                    var confirm = MessageBox.Show(
                        "Did you save the 'cookies.json' file?\n\nClick Yes to inject it now.",
                        "Step 3: Injection",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirm == MessageBoxResult.Yes)
                    {
                        InjectRecoveryCookies("cookies.json");
                        MainWebView?.Reload();
                    }
                }
            }
        }
    }

    private int InjectCookieList(List<ExtractedCookie> cookies)
    {
        if (cookies == null || cookies.Count == 0) return 0;

        var manager = MainWebView.CoreWebView2?.CookieManager;
        if (manager == null) return 0;

        int count = 0;

        foreach (var c in cookies)
        {
            try
            {
                var cookie = manager.CreateCookie(c.Name, c.Value, c.Host, c.Path);
                cookie.IsSecure   = c.IsSecure;
                cookie.IsHttpOnly = c.IsHttpOnly;
                manager.AddOrUpdateCookie(cookie);
                count++;
            }
            catch { }
        }
        return count;
    }

    private void InjectRecoveryCookies(string filename = "cookies.json")
    {
        try
        {
            string recoveryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
            if (!File.Exists(recoveryPath))
                recoveryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "twitch_cookies.json");

            if (File.Exists(recoveryPath))
            {
                string jsonContent = File.ReadAllText(recoveryPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var cookies = JsonSerializer.Deserialize<List<JsonCookie>>(jsonContent, options);

                if (cookies != null)
                {
                    var manager = MainWebView.CoreWebView2?.CookieManager;
                    if (manager == null) return;

                    int successCount = 0;

                    foreach (var c in cookies)
                    {
                        try
                        {
                            var cookie = manager.CreateCookie(c.name, c.value, c.domain, c.path ?? "/");
                            cookie.IsSecure   = c.secure;
                            cookie.IsHttpOnly = c.httpOnly;
                            manager.AddOrUpdateCookie(cookie);
                            successCount++;
                        }
                        catch { }
                    }

                    if (successCount > 0)
                    {
                        LogService.Write("INJECT", $"Manual recovery injected {successCount} cookies.");
                        MessageBox.Show($"Manual Injection Successful.\nLoaded {successCount} cookies.", "Identity Bridge");
                    }
                    else
                    {
                        MessageBox.Show("File found, but no cookies could be parsed.\nCheck JSON format.", "Injection Failed");
                    }
                }
            }
            else
            {
                MessageBox.Show($"File '{filename}' not found in app folder.", "Error");
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ManualInjection");
            MessageBox.Show("Error reading file: " + ex.Message, "Error");
        }
    }

    private static string GetWhitelistPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Horizon_Browser", "script_disabled_hosts.json");

    private static void EnsureWhitelistLoaded()
    {
        lock (_whitelistLock)
        {
            if (_whitelistLoaded) return;
            _whitelistLoaded = true;
            try
            {
                string path = GetWhitelistPath();
                if (System.IO.File.Exists(path))
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(
                        System.IO.File.ReadAllText(path));
                    if (arr != null)
                        foreach (var h in arr) _scriptWhitelist.Add(h);
                }
            }
            catch { }
        }
    }

    private static void SaveWhitelist()
    {
        try
        {
            string path = GetWhitelistPath();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            lock (_whitelistLock)
                System.IO.File.WriteAllText(path,
                    System.Text.Json.JsonSerializer.Serialize(_scriptWhitelist.ToArray()));
        }
        catch { }
    }

    private static string BuildHostCheckScript()
    {
        lock (_whitelistLock)
        {
            var hosts = System.Text.Json.JsonSerializer.Serialize(_scriptWhitelist.ToArray());
            return $"(function(){{var h=window.location.hostname;var d={hosts};" +
                   $"if(d.some(function(x){{return h===x||h.endsWith('.'+x);}}))window.__horizonScriptDisabled=true;}})();";
        }
    }

    private async Task RefreshHostCheckScriptAsync()
    {
        if (MainWebView?.CoreWebView2 == null) return;
        if (_hostCheckScriptId != null)
        {
            MainWebView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_hostCheckScriptId);
            _hostCheckScriptId = null;
        }
        _hostCheckScriptId = await MainWebView.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(BuildHostCheckScript());
    }

    private void ShowScriptWarning(string host)
    {
        _warningHost = host;
        ScriptWarningBar.Visibility = System.Windows.Visibility.Visible;
    }

    private void HideScriptWarning()
    {
        _warningHost = null;
        ScriptWarningBar.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void ShowSavePasswordBar(string title, string url, string user, string pass)
    {
        _pendingSaveTitle = title;
        _pendingSaveUrl   = url;
        _pendingSaveUser  = user;
        _pendingSavePass  = pass;
        string host;
        try { host = new Uri(url).Host; } catch { host = url; }
        SavePasswordText.Text = $"🔑  Save password for {host}?";
        SavePasswordBar.Visibility = Visibility.Visible;
    }

    private void HideSavePasswordBar()
    {
        SavePasswordBar.Visibility = Visibility.Collapsed;
        _pendingSaveTitle = _pendingSaveUrl = _pendingSaveUser = _pendingSavePass = null;
    }

    private void CommitSavePassword()
    {
        if (_pendingSaveUrl != null && _pendingSavePass != null)
            VaultService.Add(_pendingSaveUrl, _pendingSaveUser ?? "", _pendingSavePass, _pendingSaveTitle ?? "");
        HideSavePasswordBar();
    }

    private void ShowExtensionInstallBar(StorePageInfo info)
    {
        if (_isInstalling) return;
        _installSucceeded = false;
        _currentStoreInfo = info;
        ExtensionInstallText.Text = $"🧩  Install \"{info.Name}\" from {info.Kind} store?";
        InstallExtensionBtn.Content = "Install";
        InstallExtensionBtn.IsEnabled = true;
        InstallExtensionBtn.Visibility = Visibility.Visible;
        InstallExtDismissBtn.IsEnabled = true;
        ExtensionInstallBar.Visibility = Visibility.Visible;
    }

    private void HideExtensionInstallBar()
    {
        if (_isInstalling) return;
        _installSucceeded = false;
        ExtensionInstallBar.Visibility = Visibility.Collapsed;
        _currentStoreInfo = null;
    }

    private void ShowInstallProgress(string step)
    {
        InstallExtensionBtn.Content = "⏳";
        InstallExtensionBtn.IsEnabled = false;
        InstallExtDismissBtn.IsEnabled = false;
        ExtensionInstallText.Text = step;
        ExtensionInstallBar.Visibility = Visibility.Visible;
    }

    private async Task InstallExtensionAsync()
    {
        if (_currentStoreInfo == null || _isInstalling) return;
        var info = _currentStoreInfo;
        _currentStoreInfo = null;
        _isInstalling = true;
        var progress = new Progress<string>(msg => Dispatcher.Invoke(() => ShowInstallProgress(msg)));
        var record = await ExtensionInstaller.InstallAsync(info, progress);
        if (record == null)
        {
            _isInstalling = false;
            Dispatcher.Invoke(() =>
            {
                ExtensionInstallText.Text = "✗  Installation failed. Check logs.";
                InstallExtensionBtn.Visibility = Visibility.Collapsed;
                InstallExtDismissBtn.IsEnabled = true;
            });
            return;
        }
        _isInstalling = false;
        Dispatcher.Invoke(() =>
        {
            _installSucceeded = true;
            ExtensionInstallText.Text   = "✓  Installed! Restart to activate.";
            InstallExtensionBtn.Content    = "↺ Restart";
            InstallExtensionBtn.IsEnabled  = true;
            InstallExtensionBtn.Visibility = Visibility.Visible;
            InstallExtDismissBtn.IsEnabled = true;
        });
    }

    /// <summary>
    /// Register a synchronous save-session delegate here from MainWindow so that
    /// RestartApp() can flush tabs to disk before the new process starts.
    /// </summary>
    public static Action? SaveSessionBeforeRestart;
    private static bool _restartPending = false;
    public static bool IsRestartPending => _restartPending;

    private static void RestartApp()
    {
        _restartPending = true;
        try { SaveSessionBeforeRestart?.Invoke(); } catch { }

        try
        {
            string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch { }
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }

    private async Task DisableScriptForCurrentSiteAsync()
    {
        string host = MainWebView?.Source?.Host ?? string.Empty;
        if (string.IsNullOrEmpty(host)) return;
        lock (_whitelistLock) _scriptWhitelist.Add(host);
        SaveWhitelist();
        HideScriptWarning();
        await RefreshHostCheckScriptAsync();
        try { await MainWebView.CoreWebView2?.ExecuteScriptAsync("window.__horizonScriptDisabled = true;"); } catch { }
        MainWebView?.Reload();
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        // CWS, Edge Add-ons and AMO use SPA routing — NavigationCompleted
        // does not fire for pushState / replaceState changes.
        // Re-run store detection here so the install bar appears immediately.
        string url = MainWebView?.CoreWebView2?.Source ?? string.Empty;
        if (string.IsNullOrEmpty(url)) return;
        var storeInfo = ExtensionInstaller.Detect(url);
        Dispatcher.Invoke(() =>
        {
            if (storeInfo.IsStorePage && !_isInstalling)
                ShowExtensionInstallBar(storeInfo);
            else if (!storeInfo.IsStorePage)
                HideExtensionInstallBar();
        });
    }

    private async void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Dispatcher.Invoke(HideScriptWarning);
        Dispatcher.Invoke(HideExtensionInstallBar);
        string newHost = new Uri(e.Uri ?? "about:blank").Host;
        if (newHost == _lastNavHost) return; // same host — no need to refresh
        _lastNavHost = string.Empty;
        _recentNavTimes.Clear();
        await RefreshHostCheckScriptAsync();
    }

    private async Task InitializeAutomationAsync()
    {
        string automationScript = @"
            (function() {
                'use strict';
                if (window.__horizonScriptDisabled) return;

                // ── Ad skipping ──────────────────────────────────────────────────────
                const manageAds = () => {
                    const adOverlay = document.querySelector('.ad-interrupting');
                    const video = document.querySelector('video');
                    if (adOverlay && video) {
                        const isLive = video.duration === Infinity
                                    || !!document.querySelector('.ytp-live');
                        if (!isLive) {
                            video.playbackRate = 20.0;
                            video.muted = true;
                        }
                    }
                };

                // ── Comprehensive consent / gate dismissal ───────────────────────────
                //
                //  Handles:
                //   • Стандартни GDPR бутони (BG, EN, DE, FR, PL, RU, BG, IT, ES, NL...)
                //   • Google consent gate: ""Преди да продължите към Google"" + similar
                //   • Consent portals inside <iframe> (e.g. consent.google.com)
                //   • Shadow DOM consent widgets (e.g. Didomi, OneTrust, Cookiebot)
                //   • Elements identified by common CSS selectors / data-attributes
                //
                const REJECT_KEYWORDS = [
                    // English
                    'reject all', 'decline all', 'deny all', 'refuse all',
                    'reject cookies', 'decline cookies',
                    'reject', 'decline', 'deny',
                    'i do not accept', 'do not accept', 'do not consent',
                    'continue without accepting', 'continue without agreeing',
                    'use necessary cookies only', 'necessary only',
                    'essential only', 'only essential',
                    'save preferences',
                    // Bulgarian
                    'отхвърляне на всички', 'отхвърли всички',
                    'откажи всички', 'откажи',
                    'не приемам', 'не се съгласявам',
                    'продължете без да приемате',
                    'само необходими',
                    // German
                    'alle ablehnen', 'ablehnen', 'nicht einverstanden',
                    'ohne akzeptieren fortfahren',
                    // French
                    'tout refuser', 'refuser tout', 'refuser',
                    'continuer sans accepter',
                    // Spanish
                    'rechazar todo', 'rechazar todas', 'rechazar',
                    'continuar sin aceptar',
                    // Italian
                    'rifiuta tutto', 'rifiuta',
                    // Polish
                    'odrzuć wszystkie', 'odrzuć',
                    // Dutch
                    'alles weigeren', 'weigeren',
                    // Russian
                    'отклонить всё', 'отклонить',
                    // Finnish
                    'hylkää kaikki', 'hylkää',
                    // Swedish
                    'avvisa alla', 'neka alla',
                    // Czech
                    'odmítnout vše', 'odmítnout',
                ];

                // CSS selectors that typically appear on consent dialogs
                const CONSENT_SELECTORS = [
                    // Google consent gate (the most common broken one)
                    'form[action*=""consent.google""] button[value=""2""]',
                    'form[action*=""consent.youtube""] button[value=""2""]',
                    '#W0wltc',        // Google ""Reject all"" button id
                    '.KxvlWc',        // Google consent reject
                    // OneTrust
                    '#onetrust-reject-all-handler',
                    '.ot-pc-refuse-all-handler',
                    // Cookiebot
                    '#CybotCookiebotDialogBodyButtonDecline',
                    // Didomi
                    '#didomi-notice-disagree-button',
                    '.didomi-components-button--highlight',
                    // TrustArc
                    '.truste_popframe .call',
                    // Quantcast
                    '.qc-cmp2-summary-buttons button:first-child',
                    // Generic data attrs
                    '[data-action=""reject""]',
                    '[data-testid*=""reject""]',
                    '[data-testid*=""decline""]',
                ];

                // Try to click one element from CONSENT_SELECTORS
                const trySelectorsOnRoot = (root) => {
                    for (const sel of CONSENT_SELECTORS) {
                        try {
                            const el = root.querySelector(sel);
                            if (el && el.offsetParent !== null) {
                                el.click();
                                console.log('[Horizon] Consent dismissed via selector:', sel);
                                window.__horizonConsentDismissed = true;
                                return true;
                            }
                        } catch(e) {}
                    }
                    return false;
                };

                // Keyword match on all visible buttons / links
                const tryKeywordsOnRoot = (root) => {
                    const candidates = root.querySelectorAll(
                        'button, [role=""button""], a[href=""#""], input[type=""button""], input[type=""submit""]'
                    );
                    for (const el of candidates) {
                        if (el.offsetParent === null && !el.closest('dialog[open]')) continue;
                        const text = (el.innerText || el.textContent || el.value || '').trim().toLowerCase();
                        if (!text || text.length > 80) continue;
                        if (REJECT_KEYWORDS.some(kw => text === kw || text.startsWith(kw))) {
                            el.click();
                            console.log('[Horizon] Consent dismissed via keyword:', text);
                            window.__horizonConsentDismissed = true;
                            return true;
                        }
                    }
                    return false;
                };

                // Walk shadow roots recursively
                const walkShadow = (root) => {
                    if (trySelectorsOnRoot(root) || tryKeywordsOnRoot(root)) return true;
                    for (const el of root.querySelectorAll('*')) {
                        if (el.shadowRoot && walkShadow(el.shadowRoot)) return true;
                    }
                    return false;
                };

                // Main consent handler — checks main doc + all same-origin iframes
                const rejectConsent = () => {
                    if (window.__horizonConsentDismissed) return;
                    if (walkShadow(document)) return;
                    try {
                        for (const frame of document.querySelectorAll('iframe')) {
                            try {
                                const doc = frame.contentDocument;
                                if (doc) walkShadow(doc);
                            } catch(e) {}
                        }
                    } catch(e) {}
                };

                // ── YouTube 'Video paused, continue watching?' suppressor ────────────
                const suppressYtPause = () => {
                    document.querySelectorAll(
                        '.ytp-pause-overlay, .ytp-pause-overlay-container'
                    ).forEach(el => el.remove());
                };

                // Intercept YouTube's idle setInterval timers
                const _origSetInterval = window.setInterval;
                window.setInterval = function(fn, delay, ...rest) {
                    if (typeof delay === 'number' && delay >= 55000) {
                        try {
                            const s = (typeof fn === 'function' ? fn.toString() : String(fn));
                            if (/pauseVideo|pause-overlay|idleTime|confirmDialo|pauseObserv/i.test(s))
                                return 0;
                        } catch(e) {}
                    }
                    return _origSetInterval.call(this, fn, delay, ...rest);
                };

                // MutationObserver: covers YouTube pause overlay + consent dialogs
                const masterObserver = new MutationObserver(() => {
                    suppressYtPause();
                    // Remove YouTube confirm dialogs
                    document.querySelectorAll('yt-confirm-dialog-renderer').forEach(el => {
                        const t = (el.innerText || '').toLowerCase();
                        if (t.includes('pause') || t.includes('continue')) el.remove();
                    });
                    rejectConsent();
                    manageAds();
                });

                const startObserver = () => {
                    masterObserver.observe(document.documentElement, { childList: true, subtree: true });
                    rejectConsent(); // run immediately on load too
                    manageAds();
                };

                if (document.readyState === 'loading') {
                    document.addEventListener('DOMContentLoaded', startObserver, { once: true });
                } else {
                    startObserver();
                }

                // ── Fullscreen bridge ────────────────────────────────────────────────
                document.addEventListener('fullscreenchange', () => {
                    window.chrome?.webview?.postMessage({
                        type: 'fullscreen', value: !!document.fullscreenElement
                    });
                });

                // ── Q key → Picture-in-Picture ───────────────────────────────────────
                document.addEventListener('keydown', (e) => {
                    if (e.key.toLowerCase() !== 'q') return;
                    const active = document.activeElement;
                    const tag = active?.tagName?.toLowerCase?.() ?? '';
                    if (tag === 'input' || tag === 'textarea' || active?.isContentEditable) return;
                    const target = Array.from(document.querySelectorAll('video'))
                        .find(v => !v.paused && v.readyState > 2)
                        || document.querySelector('video');
                    if (!target) return;
                    document.pictureInPictureElement
                        ? document.exitPictureInPicture().catch(()=>{})
                        : target.requestPictureInPicture().catch(()=>{});
                });

                document.addEventListener('wheel', (e) => {
                    if (e.altKey) {
                        e.preventDefault();
                        const boost = 5;
                        let node = e.target;
                        let handled = false;
                        while (node && node !== document.body && node !== document.documentElement) {
                            const style = window.getComputedStyle(node);
                            if ((style.overflowY === 'auto' || style.overflowY === 'scroll') && node.scrollHeight > node.clientHeight) {
                                const start = node.scrollTop;
                                node.scrollTop += e.deltaY * boost;
                                if (node.scrollTop !== start) { handled = true; break; }
                            }
                            node = node.parentElement;
                        }
                        if (!handled) { window.scrollBy(0, e.deltaY * boost); }
                    }
                }, { passive: false });

                document.addEventListener('keydown', (e) => {
                    if (!e.altKey) return;
                    if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
                    e.preventDefault();
                    const boost = 5;
                    const step  = 100;
                    const delta = e.key === 'ArrowDown' ? step : -step;
                    let node = document.activeElement || document.body;
                    let handled = false;
                    while (node && node !== document.body && node !== document.documentElement) {
                        const style = window.getComputedStyle(node);
                        if ((style.overflowY === 'auto' || style.overflowY === 'scroll') && node.scrollHeight > node.clientHeight) {
                            const start = node.scrollTop;
                            node.scrollTop += delta * boost;
                            if (node.scrollTop !== start) { handled = true; break; }
                        }
                        node = node.parentElement;
                    }
                    if (!handled) { window.scrollBy(0, delta * boost); }
                });

            })();
        ";

        var _regAutomation = MainWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(automationScript);
        var _regPrint      = MainWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
(function() {
    var _origPrint = window.print;
    window.print = function() {
        window.chrome?.webview?.postMessage({ type: 'horizonPrint' });
    };
})();
");
        MainWebView.CoreWebView2.WebMessageReceived += async (s, msg) =>
        {
            try
            {
                var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(msg.WebMessageAsJson);
                string? msgType = obj.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (msgType == "vault_save")
                {
                    string svTitle = obj.TryGetProperty("title", out var tv) ? tv.GetString() ?? "" : "";
                    string svUrl   = obj.TryGetProperty("url",   out var uv) ? uv.GetString() ?? "" : "";
                    string svUser  = obj.TryGetProperty("user",  out var un) ? un.GetString() ?? "" : "";
                    string svPass  = obj.TryGetProperty("pass",  out var pw) ? pw.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(svPass))
                        Dispatcher.Invoke(() => ShowSavePasswordBar(svTitle, svUrl, svUser, svPass));
                }
                else if (msgType == "horizonPrint")
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        string title = _tabViewModel?.Title ?? "";
                        string url   = MainWebView.Source?.ToString() ?? "";

                        string name = title;
                        if (string.IsNullOrWhiteSpace(name) || name.Equals("index", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var seg = new Uri(url).AbsolutePath.TrimEnd('/');
                                name = System.IO.Path.GetFileNameWithoutExtension(seg);
                            }
                            catch { }
                        }
                        foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                            name = name.Replace(ch, '_');
                        if (string.IsNullOrWhiteSpace(name)) name = "document";

                        string pdfPath = System.IO.Path.Combine(SettingsService.Current.DownloadsPath, name + ".pdf");
                        int n = 1;
                        while (System.IO.File.Exists(pdfPath))
                            pdfPath = System.IO.Path.Combine(SettingsService.Current.DownloadsPath, $"{name} ({n++}).pdf");

                        try
                        {
                            await MainWebView.CoreWebView2.PrintToPdfAsync(pdfPath, null);
                            LogService.Write("PDF", $"Printed to PDF: {pdfPath}");
                        }
                        catch (Exception ex)
                        {
                            LogService.Write("PDF", $"PrintToPdf failed: {ex.Message}");
                        }
                    });
                }
                else if (msgType == "google_account_switch")
                {
                    string swEmail = obj.TryGetProperty("email", out var swEm) ? swEm.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(swEmail))
                    {
                        SettingsService.Current.DefaultGoogleAccountEmail = swEmail;
                        if (obj.TryGetProperty("order", out var swOrd))
                        {
                            var newOrder = new List<string>();
                            foreach (var item in swOrd.EnumerateArray())
                            { string? e = item.GetString(); if (e != null) newOrder.Add(e); }
                            SettingsService.Current.GoogleAccountOrder = newOrder;
                        }
                        SettingsService.Save();
                        // Navigate to Google's AccountChooser so the browser session actually
                        // switches. WebView2 persists cookies, so after restart the chosen
                        // account stays active — fixing the "resets to account 1" bug.
                        string switchUrl = "https://accounts.google.com/AccountChooser" +
                            $"?Email={Uri.EscapeDataString(swEmail)}&continue=https://www.google.com/";
                        MainWebView?.CoreWebView2?.Navigate(switchUrl);
                    }
                }
                else if (msgType == "google_account_order")
                {
                    if (obj.TryGetProperty("order", out var ordArr))
                    {
                        var newOrder = new List<string>();
                        foreach (var item in ordArr.EnumerateArray())
                        { string? e = item.GetString(); if (e != null) newOrder.Add(e); }
                        SettingsService.Current.GoogleAccountOrder = newOrder;
                        SettingsService.Save();
                    }
                }
                else if (msgType == "account_switcher_move")
                {
                    if (obj.TryGetProperty("x", out var xv) && obj.TryGetProperty("y", out var yv))
                    {
                        SettingsService.Current.AccountSwitcherButtonX = xv.GetDouble();
                        SettingsService.Current.AccountSwitcherButtonY = yv.GetDouble();
                        SettingsService.Save();
                    }
                }
            }
            catch { }
        };

        // ── chrome.downloads shim ────────────────────────────────────────────
        // WebView2 does not implement the chrome.downloads extension API.
        // Extensions like CRX Extractor call chrome.downloads.download({url,filename})
        // to save files; without this shim those calls silently fail and the user
        // sees "Download interrupted".
        // We replace chrome.downloads.download with a function that performs the
        // same fetch + <a download> trick a web page would use, which DOES route
        // through CoreWebView2's DownloadStarting pipeline where our CRX intercept
        // (and normal download tracking) then takes over.
        var _regVault = MainWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
(function() {
    'use strict';
    if (window.__horizonVaultWired) return;
    window.__horizonVaultWired = true;

    function tryCapture(form) {
        var inputs = Array.from(form.querySelectorAll('input'));
        var passInput = inputs.find(function(i) { return i.type === 'password' && i.value; });
        if (!passInput) return;
        var userInput = null;
        var idx = inputs.indexOf(passInput);
        for (var i = idx - 1; i >= 0; i--) {
            var tp = inputs[i].type;
            if (tp === 'text' || tp === 'email' || tp === 'tel') { userInput = inputs[i]; break; }
        }
        try {
            window.chrome.webview.postMessage(JSON.stringify({
                type:  'vault_save',
                title: document.title,
                url:   window.location.href,
                user:  userInput ? userInput.value : '',
                pass:  passInput.value
            }));
        } catch(e) {}
    }

    document.addEventListener('submit', function(e) {
        if (e.target && e.target.tagName === 'FORM') tryCapture(e.target);
    }, true);

    document.addEventListener('click', function(e) {
        var btn = e.target && e.target.closest
            ? e.target.closest('button[type=""submit""], input[type=""submit""]') : null;
        if (!btn) return;
        var form = btn.form || btn.closest('form');
        if (form) tryCapture(form);
    }, true);
})();
");

        await Task.WhenAll(
            _regAutomation,
            _regPrint,
            _regVault,
            MainWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
(function() {
    'use strict';

    const _patchDownloads = () => {
        if (typeof chrome === 'undefined' || !chrome) return false;

        // Ensure chrome.downloads exists as a namespace
        chrome.downloads = chrome.downloads || {};

        const _orig = chrome.downloads.download;

        chrome.downloads.download = function(options, callback) {
            const url      = options?.url      || '';
            const filename = options?.filename || '';

            if (!url) {
                if (typeof callback === 'function') callback(-1);
                return;
            }

            // Use a temporary <a> element so WebView2 sees a real navigation-based
            // download and fires CoreWebView2.DownloadStarting normally.
            try {
                const a = document.createElement('a');
                a.href     = url;
                a.download = filename || '';
                a.style.display = 'none';
                document.body.appendChild(a);
                a.click();
                setTimeout(() => document.body.removeChild(a), 500);
                if (typeof callback === 'function') callback(Date.now());
            } catch(e) {
                // Fallback: open in same tab — at least won't be silently dropped
                try { window.location.href = url; } catch(e2) {}
                if (typeof callback === 'function') callback(-1);
            }
        };

        // Also patch the search / query stubs so extensions don't crash when
        // they call chrome.downloads.search() to check download state.
        chrome.downloads.search = chrome.downloads.search || function(query, callback) {
            if (typeof callback === 'function') callback([]);
            return Promise.resolve([]);
        };
        chrome.downloads.onCreated  = chrome.downloads.onCreated  || { addListener: () => {}, removeListener: () => {} };
        chrome.downloads.onChanged  = chrome.downloads.onChanged  || { addListener: () => {}, removeListener: () => {} };
        chrome.downloads.onErased   = chrome.downloads.onErased   || { addListener: () => {}, removeListener: () => {} };
        chrome.downloads.erase      = chrome.downloads.erase      || function(q, cb) { if (cb) cb([]); };
        chrome.downloads.removeFile = chrome.downloads.removeFile || function(id, cb) { if (cb) cb(); };

        return true;
    };

    if (!_patchDownloads()) {
        const t = setInterval(() => { if (_patchDownloads()) clearInterval(t); }, 30);
    }
})();
"));
    }

    /// <summary>
    /// Applies the tab's language preference via the CDP Emulation API.
    /// Call this after navigation completes and whenever Language changes.
    /// </summary>
    public async Task ApplyLanguageAsync(string languageTag)
    {
        if (MainWebView?.CoreWebView2 == null || string.IsNullOrEmpty(languageTag)) return;
        try
        {
            string ua = MainWebView.CoreWebView2.Settings.UserAgent;
            await MainWebView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Emulation.setUserAgentOverride",
                $"{{\"userAgent\":\"{ua}\",\"acceptLanguage\":\"{languageTag}\"}}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BrowserView.ApplyLanguage");
        }
    }

    // ── Media control public API ─────────────────────────────────────────────
    /// <summary>Mute or unmute this tab's audio.</summary>
    public void SetMuted(bool muted)
    {
        if (MainWebView?.CoreWebView2 == null) return;
        try { MainWebView.CoreWebView2.IsMuted = muted; } catch { }
    }

    /// <summary>Toggle mute state.</summary>
    public void ToggleMute()
    {
        if (MainWebView?.CoreWebView2 == null) return;
        try { MainWebView.CoreWebView2.IsMuted = !MainWebView.CoreWebView2.IsMuted; } catch { }
    }

    private void CoreWebView2_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var env = MainWebView.CoreWebView2.Environment;

        // ── Selected text → Search & Translate ──────────────────────────────
        if (e.ContextMenuTarget.HasSelection)
        {
            string sel = e.ContextMenuTarget.SelectionText ?? "";
            if (!string.IsNullOrWhiteSpace(sel))
            {
                string displayText = sel.Length > 28 ? sel[..28] + "…" : sel;

                var searchItem = env.CreateContextMenuItem(
                    $"Search for \"{displayText}\"", null, CoreWebView2ContextMenuItemKind.Command);
                searchItem.CustomItemSelected += (s, args) =>
                {
                    string searchUrl = BuildSearchUrl(sel);
                    Dispatcher.Invoke(() => OnNewTabRequested(searchUrl));
                };

                var translateItem = env.CreateContextMenuItem(
                    $"Translate \"{displayText}\"", null, CoreWebView2ContextMenuItemKind.Command);
                translateItem.CustomItemSelected += (s, args) =>
                {
                    string translateUrl =
                        $"https://translate.google.com/?sl=auto&tl=en&text={Uri.EscapeDataString(sel)}&op=translate";
                    Dispatcher.Invoke(() => OnNewTabRequested(translateUrl));
                };

                var selSep = env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);

                // Insert: [Search, Translate, ----, …rest…]
                e.MenuItems.Insert(0, selSep);
                e.MenuItems.Insert(0, translateItem);
                e.MenuItems.Insert(0, searchItem);
            }
        }

        // ── Image context: Download Image ────────────────────────────────────
        if (e.ContextMenuTarget.Kind == CoreWebView2ContextMenuTargetKind.Image)
        {
            var downloadItem = env.CreateContextMenuItem(
                "Download Image", null, CoreWebView2ContextMenuItemKind.Command);

            downloadItem.CustomItemSelected += (s, args) =>
                _ = DownloadContextMediaAsync(e.ContextMenuTarget.SourceUri, "image");

            e.MenuItems.Insert(0, downloadItem);
        }

        // ── Video/Audio context: Download Video / Download Audio ────────────
        if (e.ContextMenuTarget.Kind == CoreWebView2ContextMenuTargetKind.Video ||
            e.ContextMenuTarget.Kind == CoreWebView2ContextMenuTargetKind.Audio)
        {
            bool isVideo = e.ContextMenuTarget.Kind == CoreWebView2ContextMenuTargetKind.Video;
            string label = isVideo ? "Download Video" : "Download Audio";
            string kind  = isVideo ? "video" : "audio";
            var loc      = e.Location;
            string srcUri = e.ContextMenuTarget.SourceUri;

            var downloadMediaItem = env.CreateContextMenuItem(
                label, null, CoreWebView2ContextMenuItemKind.Command);

            downloadMediaItem.CustomItemSelected += (s, args) =>
                _ = DownloadMediaElementAsync(srcUri, loc, kind);

            e.MenuItems.Insert(0, downloadMediaItem);
        }

        // ── Link context: Open Link in New Tab ───────────────────────────────
        if (e.ContextMenuTarget.HasLinkUri && !string.IsNullOrEmpty(e.ContextMenuTarget.LinkUri))
        {
            string linkUri = e.ContextMenuTarget.LinkUri;

            var openLinkItem = env.CreateContextMenuItem(
                "Open Link in New Tab", null, CoreWebView2ContextMenuItemKind.Command);
            openLinkItem.CustomItemSelected += (s, args) =>
                Dispatcher.Invoke(() => OnNewTabRequested(linkUri));

            var copyLinkItem = env.CreateContextMenuItem(
                "Copy Link Address", null, CoreWebView2ContextMenuItemKind.Command);
            copyLinkItem.CustomItemSelected += (s, args) =>
                Dispatcher.Invoke(() => Clipboard.SetText(linkUri));

            var linkSep = env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);

            e.MenuItems.Insert(0, linkSep);
            e.MenuItems.Insert(0, copyLinkItem);
            e.MenuItems.Insert(0, openLinkItem);
        }

        // ── Page context: Copy URL & View Source ─────────────────────────────
        if (e.ContextMenuTarget.Kind == CoreWebView2ContextMenuTargetKind.Page)
        {
            string pageUrl = MainWebView.CoreWebView2.Source ?? "";

            var copyUrlItem = env.CreateContextMenuItem(
                "Copy Page URL", null, CoreWebView2ContextMenuItemKind.Command);
            copyUrlItem.CustomItemSelected += (s, args) =>
            {
                if (!string.IsNullOrEmpty(pageUrl))
                    Dispatcher.Invoke(() => Clipboard.SetText(pageUrl));
            };

            var viewSourceItem = env.CreateContextMenuItem(
                "View Page Source", null, CoreWebView2ContextMenuItemKind.Command);
            viewSourceItem.CustomItemSelected += (s, args) =>
                Dispatcher.Invoke(() => OnNewTabRequested("view-source:" + pageUrl));

            var pageSep = env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);

            e.MenuItems.Add(pageSep);
            e.MenuItems.Add(copyUrlItem);
            e.MenuItems.Add(viewSourceItem);
        }

        // ── Always: Open Downloads Manager ───────────────────────────────────
        var dlSep = env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);
        var downloadsItem = env.CreateContextMenuItem(
            "Open Downloads", null, CoreWebView2ContextMenuItemKind.Command);
        downloadsItem.CustomItemSelected += (s, args) =>
            Dispatcher.Invoke(() => OnNewTabRequested("edge://downloads"));

        e.MenuItems.Add(dlSep);
        e.MenuItems.Add(downloadsItem);
    }

    /// <summary>
    /// Downloads a directly-addressable media URL (image, or a video/audio element
    /// with a resolvable src) via MediaDownloadService — a plain server-side HTTP
    /// fetch, so it isn't subject to the browser's same-origin "download" attribute
    /// restriction the old JS anchor-click approach hit on cross-origin images.
    /// </summary>
    private async Task DownloadContextMediaAsync(string uri, string mediaKind)
    {
        if (string.IsNullOrWhiteSpace(uri) ||
            !(uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.Invoke(() => MessageBox.Show(
                "Could not detect a downloadable source for this item.",
                "Horizon — Download", MessageBoxButton.OK, MessageBoxImage.Warning));
            return;
        }

        using var service = new MediaDownloadService();
        var sw = Stopwatch.StartNew();
        long lastBytes = 0;
        double lastElapsedSecs = 0;

        void OnProgress(MediaDownloadProgress p)
        {
            double elapsed = sw.Elapsed.TotalSeconds;
            double intervalSecs = Math.Max(elapsed - lastElapsedSecs, 0.05);
            double deltaBytes = Math.Max(p.BytesReceived - lastBytes, 0);
            double speedMBs = deltaBytes / intervalSecs / (1024.0 * 1024.0);
            lastBytes = p.BytesReceived;
            lastElapsedSecs = elapsed;

            double progress = p.TotalBytes > 0 ? (double)p.BytesReceived / p.TotalBytes : 0;
            int etaSecs = (speedMBs > 0.01 && p.TotalBytes > 0)
                ? (int)((p.TotalBytes - p.BytesReceived) / (speedMBs * 1024 * 1024))
                : 0;

            Dispatcher.Invoke(() => DownloadProgressChanged?.Invoke(this,
                new DownloadInfo(progress, p.FilePath, false, speedMBs, etaSecs, uri)));
        }

        bool ok = await service.DownloadDirectFileAsync(uri, mediaKind, OnProgress);

        string finalPath = service.LastResolvedPath ?? "";
        Dispatcher.Invoke(() => DownloadProgressChanged?.Invoke(this,
            new DownloadInfo(ok ? 1.0 : 0.0, finalPath, true, 0, 0, uri)));

        LogService.Write("MEDIA", $"Context-menu {mediaKind} download {(ok ? "succeeded" : "failed")}: {uri}");
    }

    /// <summary>
    /// Video/Audio context-menu targets. If WebView2 didn't supply a SourceUri
    /// (common for players that swap the src dynamically), fall back to reading
    /// the actual playing element at the click point via JS, then hand off to the
    /// same direct-download path used for images.
    /// </summary>
    private async Task DownloadMediaElementAsync(string sourceUri, System.Drawing.Point clickLocation, string mediaKind)
    {
        string uri = sourceUri;

        if (string.IsNullOrWhiteSpace(uri))
        {
            try
            {
                string script =
                    $"(function(){{var el=document.elementFromPoint({clickLocation.X},{clickLocation.Y});" +
                    "if(!el) return ''; var m = el.closest('video, audio'); if(!m) return '';" +
                    "if(m.currentSrc) return m.currentSrc; var s=m.querySelector('source'); return s ? s.src : '';})();";

                string raw = await MainWebView.CoreWebView2.ExecuteScriptAsync(script);
                uri = JsonSerializer.Deserialize<string>(raw) ?? "";
            }
            catch (Exception ex)
            {
                LogService.Write("MEDIA", $"Media source resolution failed: {ex.Message}");
            }
        }

        await DownloadContextMediaAsync(uri, mediaKind);
    }

    private async Task TryDetectGoogleAccountsAsync()
    {
        try
        {
            string url = MainWebView?.Source?.ToString() ?? "";
            LogService.Write("GACCT", $"DetectCalled — url={url}");
            if (!url.Contains("google.com", StringComparison.OrdinalIgnoreCase)) return;

            var cookieManager = MainWebView.CoreWebView2?.CookieManager;
            if (cookieManager == null) return;

            var wv2Cookies = await cookieManager.GetCookiesAsync("https://accounts.google.com");
            if (wv2Cookies == null || wv2Cookies.Count == 0)
            {
                LogService.Write("GACCT", "No cookies found for accounts.google.com");
                return;
            }

            string cookieHeader = string.Join("; ", wv2Cookies.Select(c => $"{c.Name}={c.Value}"));

            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",      cookieHeader);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Origin",      "https://accounts.google.com");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer",     "https://accounts.google.com/");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Same-Domain", "1");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept",      "*/*");

            string response = await http.GetStringAsync("https://accounts.google.com/ListAccounts?gpsia=1&source=ogb");
            LogService.Write("GACCT", $"Response ({response.Length} chars): {response.Substring(0, Math.Min(response.Length, 300))}");

            // Response is HTML wrapping: window.parent.postMessage('JSON', 'origin')
            // JSON uses \xNN hex escapes — extract and unescape it
            int msgStart = response.IndexOf("postMessage('");
            if (msgStart < 0) { LogService.Write("GACCT", "postMessage not found"); return; }
            msgStart += "postMessage('".Length;
            int msgEnd = response.LastIndexOf("', '");
            if (msgEnd <= msgStart) { LogService.Write("GACCT", "postMessage end not found"); return; }

            string escaped = response.Substring(msgStart, msgEnd - msgStart);
            string json = System.Text.RegularExpressions.Regex.Replace(escaped, @"\\x([0-9a-fA-F]{2})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            json = json.Replace("\\/", "/");

            LogService.Write("GACCT", $"Parsed JSON: {json.Substring(0, Math.Min(json.Length, 300))}");

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Array || root.GetArrayLength() < 2) return;

            // Format: ["gaia.l.a.r", [["gaia.l.a", idx, "Name", "email", "avatarUrl", ...], ...]]
            var accounts = new List<GoogleBrowserAccount>();
            foreach (var entry in root[1].EnumerateArray())
            {
                if (entry.ValueKind != System.Text.Json.JsonValueKind.Array || entry.GetArrayLength() < 4) continue;

                string name   = entry[2].ValueKind == System.Text.Json.JsonValueKind.String ? entry[2].GetString() ?? "" : "";
                string email  = entry[3].ValueKind == System.Text.Json.JsonValueKind.String ? entry[3].GetString() ?? "" : "";
                string avatar = entry.GetArrayLength() > 4 && entry[4].ValueKind == System.Text.Json.JsonValueKind.String ? entry[4].GetString() ?? "" : "";

                LogService.Write("GACCT", $"Entry — email='{email}' name='{name}'");
                if (!string.IsNullOrEmpty(email))
                    accounts.Add(new GoogleBrowserAccount { Email = email, Name = name, AvatarUrl = avatar });
            }

            if (!accounts.Any()) { LogService.Write("GACCT", "Parsed 0 accounts"); return; }

            SettingsService.Current.GoogleBrowserAccounts = accounts;
            // Preserve any user-defined order. Only initialise from scratch when empty,
            // or do a safe merge when new accounts appear — never blow away custom ordering.
            var existingOrder = SettingsService.Current.GoogleAccountOrder;
            var allEmails     = accounts.Select(a => a.Email).ToList();
            if (!existingOrder.Any() || !allEmails.All(e => existingOrder.Contains(e)))
            {
                // Keep the positions of already-ordered accounts; append unknowns at the end.
                var merged = existingOrder.Where(e => allEmails.Contains(e)).ToList();
                foreach (var e in allEmails.Where(e => !merged.Contains(e)))
                    merged.Add(e);
                SettingsService.Current.GoogleAccountOrder = merged;
            }
            SettingsService.Save();
            LogService.Write("GACCT", $"Saved {accounts.Count} browser Google account(s).");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BrowserView.DetectGoogleAccounts");
        }
    }

    private async Task TryInjectAccountSwitcherAsync()
    {
        try
        {
            string currentUrl = MainWebView?.Source?.ToString() ?? "";
            string homepage   = SettingsService.Current.HomePage;

            // Built-in homepage (data: URI) already has the button baked in by HomePageService
            if (currentUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

            // Only inject on the page that is set as the homepage
            bool isHomepage = string.Equals(
                currentUrl.TrimEnd('/'), homepage.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
            if (!isHomepage) return;

            var browserAccts = SettingsService.Current.GoogleBrowserAccounts;
            if (!browserAccts.Any()) return;

            var order = SettingsService.Current.GoogleAccountOrder;
            if (order.Count > 0)
                browserAccts = browserAccts
                    .OrderBy(a => { var i = order.IndexOf(a.Email); return i < 0 ? 999 : i; })
                    .ToList();

            string defEmail = SettingsService.Current.DefaultGoogleAccountEmail;
            if (string.IsNullOrEmpty(defEmail)) defEmail = browserAccts[0].Email;

            bool isGoogle = currentUrl.Contains("google.com", StringComparison.OrdinalIgnoreCase);

            // Auto-switch: if on a Google page and the active browser account doesn't
            // match the chosen default, navigate to AccountChooser once per tab lifetime.
            if (isGoogle && !string.IsNullOrEmpty(defEmail) && !_googleDefaultAccountSwitchDone &&
                !currentUrl.Contains("AccountChooser", StringComparison.OrdinalIgnoreCase))
            {
                var activeAcct = SettingsService.Current.GoogleBrowserAccounts.FirstOrDefault()?.Email ?? "";
                if (!string.IsNullOrEmpty(activeAcct) &&
                    !string.Equals(activeAcct, defEmail, StringComparison.OrdinalIgnoreCase))
                {
                    _googleDefaultAccountSwitchDone = true;
                    string switchUrl = "https://accounts.google.com/AccountChooser" +
                        $"?Email={Uri.EscapeDataString(defEmail)}&continue=https://www.google.com/";
                    MainWebView?.CoreWebView2?.Navigate(switchUrl);
                    return;
                }
            }

            string acctJson = System.Text.Json.JsonSerializer.Serialize(
                browserAccts.Select(a => new { email = a.Email, name = a.Name, avatar = a.AvatarUrl }));

            double? bx = SettingsService.Current.AccountSwitcherButtonX;
            double? by = SettingsService.Current.AccountSwitcherButtonY;

            string script = HomePageService.BuildSwitcherScript(
                acctJson, defEmail,
                isGoogle ? "true" : "false",
                bx.HasValue ? bx.Value.ToString("F0") : "null",
                by.HasValue ? by.Value.ToString("F0") : "null");

            if (MainWebView?.CoreWebView2 != null)
                await MainWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BrowserView.AccountSwitcherInject");
        }
    }

    /// <summary>
    /// Builds a search URL for the given query using the same engine as Navigate().
    /// Swap out the return value here if a SearchEngine setting is added to SettingsService.
    /// </summary>
    private static string BuildSearchUrl(string query) =>
        $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";

    public void Navigate(string url)
    {
        if (MainWebView == null || MainWebView.CoreWebView2 == null)
        {
            _pendingUrl = url;
            return;
        }

        if (!url.StartsWith("http",         StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("about:",       StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("edge://",      StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("chrome://",    StringComparison.OrdinalIgnoreCase))
        {
            if (!url.Contains(" ") && url.Contains("."))
                url = "https://" + url;
            else
                url = $"https://www.google.com/search?q={Uri.EscapeDataString(url)}";
        }

        MainWebView.CoreWebView2.Navigate(url);
    }
}