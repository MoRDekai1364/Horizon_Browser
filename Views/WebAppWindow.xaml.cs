using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Horizon.Stealth.Services;
using Microsoft.Web.WebView2.Core;
using Forms = System.Windows.Forms;

namespace Horizon.Stealth.Views;

internal enum WebAppLinkAction
{
    SameWindow = 0,
    Horizon = 1,
    SystemBrowser = 2
}

public partial class WebAppWindow : Window
{
    private const string LogTag = "WEBAPP";
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const long BarHoldMs = 4000;

    private readonly WebAppManifest _manifest;
    private readonly string _scopeHost;
    private readonly string _scopeSite;
    private readonly HashSet<string> _allowedSites = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _barTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private string? _pendingOfferUrl;
    private string? _bypassUrl;
    private bool _coreHooked;
    private bool _barVisible;
    private long _barLastActive;
    private bool _isFullscreen;
    private WindowState _prevState = WindowState.Normal;
    private ResizeMode _prevResize = ResizeMode.CanResize;
    private Rect _prevBounds;

    private readonly WebAppJournalEntry? _restore;
    private bool _restoreRevealed;
    private int _generation;

    public WebAppManifest Manifest => _manifest;

    public WebAppWindow(WebAppManifest manifest, WebAppJournalEntry? restore = null)
    {
        _manifest = manifest;
        _restore = restore;
        _generation = restore?.Generation ?? 0;
        _scopeHost = Uri.TryCreate(manifest.StartUrl, UriKind.Absolute, out var startUri) ? startUri.Host : "";
        _scopeSite = RegistrableDomain(_scopeHost);

        InitializeComponent();

        if (_restore != null) PrepareRestore(_restore);

        Title = manifest.Name;
        BarTitle.Text = manifest.Name;
        ApplyIcon();

        Browser.NewTabRequested += OnNewTabRequested;
        Browser.MainWebView.CoreWebView2InitializationCompleted += OnCoreInitialized;

        SourceInitialized += (_, _) => ApplyWindowIdentity();
        Loaded += (_, _) => _barTimer.Start();
        LocationChanged += (_, _) => HideBar(true);
        SizeChanged += (_, _) => HideBar(true);
        StateChanged += (_, _) => HideBar(true);
        Deactivated += (_, _) => HideBar(true);
        Closed += (_, _) =>
        {
            _barTimer.Stop();
            BarPopup.IsOpen = false;
        };
        _barTimer.Tick += OnBarTick;

        Browser.Navigate(!string.IsNullOrEmpty(_restore?.Url) ? _restore!.Url : manifest.StartUrl);
        LogService.Write(LogTag, $"Window created. id={manifest.Id} start={manifest.StartUrl} scopeSite={_scopeSite}");
    }

    private void ApplyIcon()
    {
        try
        {
            if (string.IsNullOrEmpty(_manifest.Icon)) return;
            string path = Path.Combine(WebAppService.GetAppDir(_manifest.Id), _manifest.Icon);
            if (!File.Exists(path)) return;
            Icon = BitmapFrame.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppWindow.ApplyIcon");
        }
    }

    private void ApplyWindowIdentity()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            string exe = Environment.ProcessPath ?? "";
            string iconResource = exe + ",0";
            if (!string.IsNullOrEmpty(_manifest.Icon))
            {
                string iconPath = Path.Combine(WebAppService.GetAppDir(_manifest.Id), _manifest.Icon);
                if (File.Exists(iconPath)) iconResource = iconPath + ",0";
            }

            string aumid = "Horizon.Stealth.WebApp." + _manifest.Id;
            bool a = WindowPropertyStore.SetString(hwnd, WindowPropertyStore.PidRelaunchCommand, "\"" + exe + "\" --webapp-id=" + _manifest.Id);
            bool b = WindowPropertyStore.SetString(hwnd, WindowPropertyStore.PidRelaunchIconResource, iconResource);
            bool c = WindowPropertyStore.SetString(hwnd, WindowPropertyStore.PidRelaunchDisplayName, _manifest.Name);
            bool d = WindowPropertyStore.SetString(hwnd, WindowPropertyStore.PidAppUserModelId, aumid);
            LogService.Write(LogTag, $"Window identity set. aumid={aumid} relaunch={a} icon={b} name={c} id={d}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppWindow.ApplyWindowIdentity");
        }
    }

    private void OnCoreInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _coreHooked) return;
        _coreHooked = true;

        var core = Browser.MainWebView.CoreWebView2;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompletedForRestore;
        core.DocumentTitleChanged += (_, _) => UpdateTitle();
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
    }

    private const string StateScript =
        "(function(){try{var m=document.querySelector('video,audio');var t=0;" +
        "if(m&&isFinite(m.currentTime)&&m.currentTime>0&&!m.ended)t=m.currentTime;" +
        "return JSON.stringify({x:window.scrollX||0,y:window.scrollY||0,t:t});}catch(e){return null;}})()";

    private static bool IsOnAnyScreen(WebAppJournalEntry r)
    {
        double vl = SystemParameters.VirtualScreenLeft;
        double vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth;
        double vb = vt + SystemParameters.VirtualScreenHeight;
        return r.Left + 100 < vr && r.Left + r.Width - 100 > vl && r.Top + 50 < vb && r.Top + r.Height - 50 > vt;
    }

    private void PrepareRestore(WebAppJournalEntry r)
    {
        try
        {
            if (r.Width >= 300 && r.Height >= 200 && IsOnAnyScreen(r))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = r.Left;
                Top = r.Top;
                Width = r.Width;
                Height = r.Height;
            }
            if (r.Maximized) WindowState = WindowState.Maximized;

            string snapshotPath = Path.Combine(WebAppService.GetAppDir(_manifest.Id), "snapshot.png");
            if (File.Exists(snapshotPath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(snapshotPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                SnapshotImage.Source = bmp;
                SnapshotImage.Visibility = Visibility.Visible;
                Browser.Visibility = Visibility.Hidden;
            }

            var revealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            revealTimer.Tick += (_, _) =>
            {
                revealTimer.Stop();
                RevealBrowser();
            };
            revealTimer.Start();

            var stableTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            stableTimer.Tick += (_, _) =>
            {
                stableTimer.Stop();
                _generation = 0;
            };
            stableTimer.Start();

            LogService.Write(LogTag, $"Restore prepared for {_manifest.Id}. Generation={_generation}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppWindow.PrepareRestore");
            RevealBrowser();
        }
    }

    private void RevealBrowser()
    {
        if (_restoreRevealed) return;
        _restoreRevealed = true;
        Browser.Visibility = Visibility.Visible;
        SnapshotImage.Visibility = Visibility.Collapsed;
        SnapshotImage.Source = null;
    }

    private async void OnNavigationCompletedForRestore(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_restore == null || _restoreRevealed) return;
        try
        {
            var core = Browser.MainWebView.CoreWebView2;
            if (e.IsSuccess && core != null)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string js =
                    "(function(){try{window.scrollTo(" + _restore.ScrollX.ToString(inv) + "," + _restore.ScrollY.ToString(inv) + ");}catch(e){}" +
                    "try{var t=" + _restore.MediaTime.ToString(inv) + ";if(t>0){var m=document.querySelector('video,audio');" +
                    "if(m){var s=function(){try{m.currentTime=t;}catch(e){}};" +
                    "if(m.readyState>0)s();else m.addEventListener('loadedmetadata',s,{once:true});}}}catch(e){}})()";
                await core.ExecuteScriptAsync(js);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppWindow.RestoreScript");
        }
        finally
        {
            RevealBrowser();
        }
    }

    public async System.Threading.Tasks.Task<WebAppJournalEntry?> CaptureStateAsync(bool withSnapshot)
    {
        try
        {
            if (_restore != null && !_restoreRevealed) return _restore;

            var core = Browser.MainWebView?.CoreWebView2;

            Rect bounds;
            bool maximized;
            if (_isFullscreen)
            {
                bounds = _prevBounds;
                maximized = _prevState == WindowState.Maximized;
            }
            else if (WindowState == WindowState.Normal)
            {
                bounds = new Rect(Left, Top, Width, Height);
                maximized = false;
            }
            else
            {
                bounds = RestoreBounds;
                maximized = WindowState == WindowState.Maximized;
            }
            if (bounds.IsEmpty) bounds = new Rect(Left, Top, Width, Height);

            var entry = new WebAppJournalEntry
            {
                Id = _manifest.Id,
                Url = core != null && !string.IsNullOrEmpty(core.Source) ? core.Source : _manifest.StartUrl,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = maximized,
                Generation = _generation,
                SavedUtc = DateTime.UtcNow.ToString("o")
            };

            if (core != null)
            {
                string res = await core.ExecuteScriptAsync(StateScript);
                string? inner = System.Text.Json.JsonSerializer.Deserialize<string>(res);
                if (!string.IsNullOrEmpty(inner))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(inner);
                    entry.ScrollX = doc.RootElement.GetProperty("x").GetDouble();
                    entry.ScrollY = doc.RootElement.GetProperty("y").GetDouble();
                    entry.MediaTime = doc.RootElement.GetProperty("t").GetDouble();
                }

                if (withSnapshot && WindowState != WindowState.Minimized && IsVisible && Browser.Visibility == Visibility.Visible)
                    await SaveSnapshotAsync(core);
            }

            return entry;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppWindow.CaptureStateAsync");
            return null;
        }
    }

    private async System.Threading.Tasks.Task SaveSnapshotAsync(CoreWebView2 core)
    {
        string dir = WebAppService.GetAppDir(_manifest.Id);
        string tmpPath = Path.Combine(dir, "snapshot.png.tmp");
        string finalPath = Path.Combine(dir, "snapshot.png");
        using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
        }
        File.Move(tmpPath, finalPath, true);
    }

    private void UpdateTitle()
    {
        string t = Browser.MainWebView.CoreWebView2?.DocumentTitle ?? "";
        Title = string.IsNullOrWhiteSpace(t) ? _manifest.Name : t;
        BarTitle.Text = Title;
    }

    private static bool IsWebScheme(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static string RegistrableDomain(string host)
    {
        host = (host ?? "").ToLowerInvariant().TrimEnd('.');
        if (host.Length == 0) return "";
        if (System.Net.IPAddress.TryParse(host, out _)) return host;

        var parts = host.Split('.');
        if (parts.Length <= 2) return host;

        string last = parts[parts.Length - 1];
        string second = parts[parts.Length - 2];
        bool twoLevel = last.Length == 2 &&
            (second == "co" || second == "com" || second == "org" || second == "net" ||
             second == "gov" || second == "edu" || second == "ac" || second == "or" ||
             second == "ne" || second == "go");
        int take = twoLevel ? 3 : 2;
        return string.Join(".", parts.Skip(parts.Length - take));
    }

    private bool IsInScope(Uri uri)
    {
        string site = RegistrableDomain(uri.Host);
        return string.Equals(site, _scopeSite, StringComparison.OrdinalIgnoreCase) || _allowedSites.Contains(site);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        try
        {
            if (!e.IsUserInitiated || e.IsRedirected) return;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !IsWebScheme(uri)) return;

            if (_bypassUrl != null && string.Equals(_bypassUrl, e.Uri, StringComparison.OrdinalIgnoreCase))
            {
                _bypassUrl = null;
                return;
            }

            if (IsInScope(uri)) return;
            if (!SettingsService.Current.WebAppOfferOnLeave) return;

            e.Cancel = true;
            ShowOffer(e.Uri);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppWindow.OnNavigationStarting");
        }
    }

    private void OnNewTabRequested(object? sender, string url)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsWebScheme(uri))
                {
                    OpenLink(url, WebAppLinkAction.SameWindow);
                    return;
                }

                if (!IsInScope(uri) && SettingsService.Current.WebAppOfferOnLeave)
                {
                    ShowOffer(url);
                    return;
                }

                OpenLink(url, WebAppLinkAction.SameWindow);
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "WebAppWindow.OnNewTabRequested");
            }
        }));
    }

    private void ShowOffer(string url)
    {
        _pendingOfferUrl = url;
        string host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
        OfferText.Text = "This link leaves " + _scopeHost + " and goes to " + host + ".";
        OfferBar.Visibility = Visibility.Visible;
        LogService.Write(LogTag, "Offer shown for: " + url);
    }

    private void ResolveOffer(WebAppLinkAction action, bool neverAskAgain)
    {
        string? url = _pendingOfferUrl;
        _pendingOfferUrl = null;
        OfferBar.Visibility = Visibility.Collapsed;

        if (neverAskAgain)
        {
            SettingsService.Current.WebAppOfferOnLeave = false;
            SettingsService.Save();
            LogService.Write(LogTag, "Offer permanently disabled by user.");
        }

        if (string.IsNullOrEmpty(url)) return;
        OpenLink(url, action);
    }

    private void BtnOfferStay_Click(object sender, RoutedEventArgs e) => ResolveOffer(WebAppLinkAction.SameWindow, false);
    private void BtnOfferHorizon_Click(object sender, RoutedEventArgs e) => ResolveOffer(WebAppLinkAction.Horizon, false);
    private void BtnOfferBrowser_Click(object sender, RoutedEventArgs e) => ResolveOffer(WebAppLinkAction.SystemBrowser, false);
    private void BtnOfferNever_Click(object sender, RoutedEventArgs e) => ResolveOffer(WebAppLinkAction.SameWindow, true);

    private void OpenLink(string url, WebAppLinkAction start)
    {
        var order = new[] { WebAppLinkAction.SameWindow, WebAppLinkAction.Horizon, WebAppLinkAction.SystemBrowser };
        foreach (var step in order)
        {
            if (step < start) continue;
            if (TryOpen(step, url))
            {
                LogService.Write(LogTag, $"Link opened via {step}: {url}");
                return;
            }
            LogService.Write(LogTag, $"Link could not open via {step}, falling back: {url}");
        }
        LogService.Write(LogTag, "Link could not be opened by any method: " + url);
    }

    private bool TryOpen(WebAppLinkAction step, string url)
    {
        try
        {
            switch (step)
            {
                case WebAppLinkAction.SameWindow:
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var sameUri) || !IsWebScheme(sameUri)) return false;
                    var core = Browser.MainWebView.CoreWebView2;
                    if (core == null) return false;
                    _bypassUrl = url;
                    _allowedSites.Add(RegistrableDomain(sameUri.Host));
                    core.Navigate(url);
                    return true;
                }
                case WebAppLinkAction.Horizon:
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var horizonUri) || !IsWebScheme(horizonUri)) return false;
                    return BackgroundKeepAliveService.TryActivateExistingInstance("OPENURL:" + url);
                }
                case WebAppLinkAction.SystemBrowser:
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var sysUri)) return false;
                    string s = sysUri.Scheme;
                    if (s != Uri.UriSchemeHttp && s != Uri.UriSchemeHttps && s != Uri.UriSchemeMailto && s != "tel") return false;
                    Process.Start(new ProcessStartInfo(sysUri.AbsoluteUri) { UseShellExecute = true });
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppWindow.TryOpen." + step);
        }
        return false;
    }

    private void SetFullscreen(bool on)
    {
        try
        {
            if (on == _isFullscreen) return;
            _isFullscreen = on;

            if (on)
            {
                _prevState = WindowState;
                _prevResize = ResizeMode;
                _prevBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                HideBar(true);

                var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
                var source = PresentationSource.FromVisual(this);
                Matrix toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                var tl = toDip.Transform(new Point(screen.Bounds.Left, screen.Bounds.Top));
                var br = toDip.Transform(new Point(screen.Bounds.Right, screen.Bounds.Bottom));

                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                Topmost = true;
                Left = tl.X;
                Top = tl.Y;
                Width = br.X - tl.X;
                Height = br.Y - tl.Y;
            }
            else
            {
                Topmost = false;
                ResizeMode = _prevResize;
                Left = _prevBounds.Left;
                Top = _prevBounds.Top;
                Width = _prevBounds.Width;
                Height = _prevBounds.Height;
                WindowState = _prevState;
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppWindow.SetFullscreen");
        }
    }

    private void OnBarTick(object? sender, EventArgs e)
    {
        try
        {
            if (!IsActive || WindowState == WindowState.Minimized || _isFullscreen)
            {
                HideBar(true);
                return;
            }

            bool hot = IsCursorInTopEdge();
            if (hot)
            {
                _barLastActive = Environment.TickCount64;
                if (!_barVisible) ShowBar();
            }
            else if (_barVisible && Environment.TickCount64 - _barLastActive > BarHoldMs)
            {
                HideBar(false);
            }
        }
        catch (Exception ex)
        {
            _barTimer.Stop();
            LogService.RecordCrash(ex, "WebAppWindow.OnBarTick");
        }
    }

    private bool IsCursorInTopEdge()
    {
        if (!GetCursorPos(out var p)) return false;
        var source = PresentationSource.FromVisual(RootGrid);
        if (source?.CompositionTarget == null) return false;

        Matrix toDevice = source.CompositionTarget.TransformToDevice;
        Point tl = RootGrid.PointToScreen(new Point(0, 0));
        double width = RootGrid.ActualWidth * toDevice.M11;
        double edge = (_barVisible ? 56 : 24) * toDevice.M22;
        double zoneLeft = tl.X + width * 0.75;
        return p.X >= zoneLeft && p.X <= tl.X + width && p.Y >= tl.Y && p.Y <= tl.Y + edge;
    }

    private void ShowBar()
    {
        BarRoot.Width = RootGrid.ActualWidth;
        BarPopup.IsOpen = true;
        _barVisible = true;
        BarRoot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
    }

    private void HideBar(bool immediate)
    {
        if (!_barVisible) return;
        _barVisible = false;

        if (immediate)
        {
            BarRoot.BeginAnimation(UIElement.OpacityProperty, null);
            BarRoot.Opacity = 0;
            BarPopup.IsOpen = false;
            return;
        }

        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        anim.Completed += (_, _) =>
        {
            if (!_barVisible) BarPopup.IsOpen = false;
        };
        BarRoot.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnBarMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnBarMax_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void BtnBarClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BarDragGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

internal static class WindowPropertyStore
{
    public const uint PidRelaunchCommand = 2;
    public const uint PidRelaunchIconResource = 3;
    public const uint PidRelaunchDisplayName = 4;
    public const uint PidAppUserModelId = 5;

    private static readonly Guid AppUserModelFmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    public static bool SetString(IntPtr hwnd, uint pid, string value)
    {
        IPropertyStore? store = null;
        var pv = new PropVariant();
        try
        {
            var riid = typeof(IPropertyStore).GUID;
            int hr = SHGetPropertyStoreForWindow(hwnd, ref riid, out store);
            if (hr != 0 || store == null) return false;

            var key = new PropertyKey { fmtid = AppUserModelFmtid, pid = pid };
            pv.vt = 31;
            pv.pointerValue = Marshal.StringToCoTaskMemUni(value);
            store.SetValue(ref key, ref pv);
            store.Commit();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            PropVariantClear(ref pv);
            if (store != null) Marshal.ReleaseComObject(store);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IPropertyStore? propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        int Commit();
    }
}