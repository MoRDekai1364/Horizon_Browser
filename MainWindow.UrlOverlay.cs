using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Horizon.Stealth.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Horizon.Stealth;

public partial class MainWindow
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private Window? _urlOverlayWindow;
    private bool _urlOverlayHooked;
    private static readonly HttpClient _shortenHttp = new() { Timeout = TimeSpan.FromSeconds(8) };

    private string? _pendingShareUrl;
    private string? _pendingShareTitle;
    private IntPtr _shareAnchorHwnd;
    private bool _isSharing;

    private IntPtr CreateShareAnchor(Rect screenRect, IntPtr parentHwnd)
    {
        CleanupShareAnchor();
        try
        {
            int x = (int)Math.Round(screenRect.Left);
            int y = (int)Math.Round(screenRect.Top);
            int w = Math.Max(20, (int)Math.Round(screenRect.Width));
            int h = Math.Max(20, (int)Math.Round(screenRect.Height));

            _shareAnchorHwnd = CreateWindowExW(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                "STATIC",
                string.Empty,
                WS_POPUP | WS_VISIBLE,
                x, y, w, h,
                parentHwnd != IntPtr.Zero ? parentHwnd : IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
            return _shareAnchorHwnd;
        }
        catch (Exception ex)
        {
            LogService.Debug("urloverlay", () => $"Failed to create share anchor HWND: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    private void CleanupShareAnchor()
    {
        if (_shareAnchorHwnd != IntPtr.Zero)
        {
            try { DestroyWindow(_shareAnchorHwnd); } catch { }
            _shareAnchorHwnd = IntPtr.Zero;
        }
    }

    public void ShowSystemShare(string? url = null, string? title = null, Rect? anchorScreenRect = null)
    {
        IntPtr hstring = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        IntPtr managerPtr = IntPtr.Zero;
        string shareUrl = !string.IsNullOrWhiteSpace(url)
            ? url
            : (GetOverlayShareUrl() ?? CurrentBrowser?.MainWebView?.Source?.ToString() ?? "");

        string shareTitle = !string.IsNullOrWhiteSpace(title)
            ? title
            : (CurrentBrowser?.MainWebView?.CoreWebView2?.DocumentTitle ?? CurrentTab?.Title ?? "Horizon Browser");

        _pendingShareUrl = shareUrl;
        _pendingShareTitle = shareTitle;

        try
        {
            var mainHwnd = new WindowInteropHelper(this).Handle;
            if (mainHwnd == IntPtr.Zero) return;

            IntPtr targetHwnd = mainHwnd;
            if (anchorScreenRect.HasValue && anchorScreenRect.Value.Width > 0 && anchorScreenRect.Value.Height > 0)
            {
                var anchor = CreateShareAnchor(anchorScreenRect.Value, mainHwnd);
                if (anchor != IntPtr.Zero)
                {
                    targetHwnd = anchor;
                }
            }
            else if (GetCursorPos(out var mousePt))
            {
                var fallbackRect = new Rect(mousePt.X - 20, mousePt.Y - 15, 40, 30);
                var anchor = CreateShareAnchor(fallbackRect, mainHwnd);
                if (anchor != IntPtr.Zero)
                {
                    targetHwnd = anchor;
                }
            }

            const string className = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            int hr = WindowsCreateString(className, className.Length, out hstring);
            if (hr < 0 || hstring == IntPtr.Zero)
            {
                LogService.Write("SHARE", $"WindowsCreateString failed: 0x{hr:X8}");
                CleanupShareAnchor();
                _isSharing = false;
                FallbackShare(shareUrl, shareTitle);
                return;
            }

            var interopIid = new Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8");
            hr = RoGetActivationFactory(hstring, ref interopIid, out factory);
            if (hr < 0 || factory == IntPtr.Zero)
            {
                LogService.Write("SHARE", $"RoGetActivationFactory failed: 0x{hr:X8}");
                CleanupShareAnchor();
                _isSharing = false;
                FallbackShare(shareUrl, shareTitle);
                return;
            }

            var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory);
            hr = interop.GetForWindow(targetHwnd, ref _dtmIid, out managerPtr);
            if (hr < 0 || managerPtr == IntPtr.Zero)
            {
                LogService.Write("SHARE", $"GetForWindow failed: 0x{hr:X8}");
                CleanupShareAnchor();
                _isSharing = false;
                FallbackShare(shareUrl, shareTitle);
                return;
            }

            var manager = global::WinRT.MarshalInspectable<DataTransferManager>.FromAbi(managerPtr);
            manager.DataRequested -= OnShareDataRequested;
            manager.DataRequested += OnShareDataRequested;
            manager.TargetApplicationChosen -= OnShareTargetChosen;
            manager.TargetApplicationChosen += OnShareTargetChosen;

            hr = interop.ShowShareUIForWindow(targetHwnd);
            LogService.Write("SHARE", $"ShowShareUIForWindow hr: 0x{hr:X8}");

            if (hr < 0)
            {
                CleanupShareAnchor();
                _isSharing = false;
                FallbackShare(shareUrl, shareTitle);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Share Interface");
            CleanupShareAnchor();
            _isSharing = false;
            FallbackShare(shareUrl, shareTitle);
        }
        finally
        {
            if (managerPtr != IntPtr.Zero) Marshal.Release(managerPtr);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
        }
    }

    private void OnShareTargetChosen(DataTransferManager sender, TargetApplicationChosenEventArgs args)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _isSharing = false;
            CloseUrlOverlay();
        }));
    }

    private void FallbackShare(string? url, string? title)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                System.Windows.Clipboard.SetDataObject(url, true);
                ImageActionService.FlashStatus($"\uD83D\uDCCE Link copied to clipboard:\n{url}", 2600);
            }
            else if (!string.IsNullOrWhiteSpace(title))
            {
                System.Windows.Clipboard.SetDataObject(title, true);
                ImageActionService.FlashStatus($"\uD83D\uDCCE Copied to clipboard:\n{title}", 2600);
            }
        }
        catch { }
    }

    private void ApplyGlassBackdropSnapshot(Window win, Border root, double leftDip, double topDip, double radius)
    {
        win.Content = null;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = root.DesiredSize;

        var clipHost = new Grid { IsHitTestVisible = false };
        clipHost.Clip = new RectangleGeometry(new Rect(0, 0, size.Width, size.Height), radius, radius);
        clipHost.SizeChanged += (_, _) =>
            clipHost.Clip = new RectangleGeometry(new Rect(0, 0, clipHost.ActualWidth, clipHost.ActualHeight), radius, radius);

        var solid = new Border { Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x12, 0x12)) };
        BitmapSourceHolder snap = new BitmapSourceHolder();
        const double pad = 40;

        if (!SettingsService.Current.DisableBlurEffects)
        {
            try
            {
                double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
                if (sc <= 0) sc = 1.0;
                int px = (int)Math.Round((leftDip - pad) * sc);
                int py = (int)Math.Round((topDip - pad) * sc);
                int pw = (int)Math.Round((size.Width + 2 * pad) * sc);
                int ph = (int)Math.Round((size.Height + 2 * pad) * sc);
                snap.Image = Views.TabSwitcherWindow.CaptureScreenArea(px, py, pw, ph);
            }
            catch (Exception ex)
            {
                LogService.Debug("glass", () => $"capture failed: {ex.Message}");
            }
        }

        if (snap.Image == null)
        {
            clipHost.Children.Add(solid);
        }
        else
        {
            var wall = new Border
            {
                Margin = new Thickness(-pad),
                Background = new ImageBrush(snap.Image) { Stretch = Stretch.Fill },
                Effect = new BlurEffect
                {
                    Radius = 28,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                }
            };
            var tint = new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0E, 0x0E, 0x0E)) };
            clipHost.Children.Add(wall);
            clipHost.Children.Add(tint);
        }

        var host = new Grid();
        host.Children.Add(clipHost);
        host.Children.Add(root);
        win.Content = host;
    }

    private sealed class BitmapSourceHolder
    {
        public System.Windows.Media.Imaging.BitmapSource? Image;
    }

    private void BtnSplitView_Click(object sender, RoutedEventArgs e)
    {
        CloseUrlOverlay();
        ToggleSplitView();
    }

    private void Omnibox_UrlFocused(object? sender, EventArgs e)
    {
        if (_isSharing) return;
        ShowUrlOverlay();
    }

    private void Omnibox_UrlUnfocused(object? sender, EventArgs e)
    {
        if (_isSharing) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var w = _urlOverlayWindow;
            if (w == null) return;
            if (w.IsActive || w.IsMouseOver) return;
            CloseUrlOverlay();
        }));
    }

    private void Omnibox_UrlDismissed(object? sender, EventArgs e)
    {
        CloseUrlOverlay();
    }

    private void UrlOverlayOwnerChanged(object? sender, EventArgs e)
    {
        CloseUrlOverlay();
    }

    private void UrlOverlayOwnerDeactivated(object? sender, EventArgs e)
    {
        if (_isSharing) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var w = _urlOverlayWindow;
            if (w == null) return;
            if (w.IsActive) return;
            CloseUrlOverlay();
        }));
    }

    private void UrlOverlayOwnerActivated(object? sender, EventArgs e)
    {
        if (_isSharing)
        {
            _isSharing = false;
            CloseUrlOverlay();
        }
    }

    private string? GetOverlayShareUrl()
    {
        string raw = "";
        try
        {
            raw = CurrentBrowser?.MainWebView?.Source?.ToString() ?? "";
        }
        catch (Exception ex)
        {
            LogService.Debug("urloverlay", () => $"source read failed: {ex.Message}");
        }
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return uri.AbsoluteUri;
    }

    private void CloseUrlOverlay()
    {
        CleanupShareAnchor();
        _isSharing = false;
        var w = _urlOverlayWindow;
        if (w == null) return;
        _urlOverlayWindow = null;
        try
        {
            w.Close();
        }
        catch (Exception ex)
        {
            LogService.Debug("urloverlay", () => $"close failed: {ex.Message}");
        }
    }

    private static void UrlOverlaySetClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetDataObject(text, true);    
        }
        catch (Exception ex)
        {
            LogService.Debug("urloverlay", () => $"clipboard failed: {ex.Message}");
        }
    }

    private static async Task<(string? Url, string Error)> ShortenUrlAsync(string url)
    {
        string[] endpoints =
        {
            "https://is.gd/create.php?format=simple&url=",
            "https://tinyurl.com/api-create.php?url="
        };
        string error = "no response";
        foreach (var endpoint in endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint + Uri.EscapeDataString(url));
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) HorizonBrowser");
                using var resp = await _shortenHttp.SendAsync(req).ConfigureAwait(true);
                string body = (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Trim();
                if (resp.IsSuccessStatusCode
                    && Uri.TryCreate(body, UriKind.Absolute, out var shortUri)
                    && (shortUri.Scheme == Uri.UriSchemeHttp || shortUri.Scheme == Uri.UriSchemeHttps))
                {
                    return (shortUri.AbsoluteUri, "");
                }
                error = (int)resp.StatusCode + " " + (body.Length > 50 ? body.Substring(0, 50) : body);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }
        }
        return (null, error);
    }

    private FrameworkElement BuildUrlOverlayTile(
        string glyph,
        string label,
        Brush textBrush,
        Brush mutedBrush,
        Brush fieldBrush,
        Brush accentBrush,
        Func<Border, TextBlock, TextBlock, Task> action)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            Foreground = textBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var caption = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = mutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(icon);
        content.Children.Add(caption);
        var tile = new Border
        {
            Width = 92,
            Height = 62,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(8),
            Background = fieldBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = content
        };
        tile.MouseEnter += (_, _) => { if (!_isSharing) tile.BorderBrush = accentBrush; };
        tile.MouseLeave += (_, _) => { if (!_isSharing) tile.BorderBrush = Brushes.Transparent; };
        tile.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            try
            {
                await action(tile, icon, caption);
            }
            catch (Exception ex)
            {
                LogService.Debug("urloverlay", () => $"action failed: {ex.Message}");
            }
        };
        return tile;
    }

    private void ShowUrlOverlay()
    {
        if (_urlOverlayWindow != null) return;
        if (!IsLoaded || WindowState == WindowState.Minimized) return;
        string? url = GetOverlayShareUrl();
        if (url == null) return;

        var textBrush = SplitBrush("Brush_Text", Brushes.White);
        var mutedBrush = SplitBrush("Brush_MutedText", Brushes.Gray);
        var fieldBrush = SplitBrush("Brush_SurfaceField", Brushes.DimGray);
        var chromeBrush = SplitBrush("Brush_SurfaceChrome", Brushes.Black);
        var accentBrush = SplitBrush("Brush_Accent", Brushes.Gray);

        var status = new TextBlock
        {
            Text = new Uri(url).Host,
            FontSize = 12,
            Foreground = mutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 290,
            Margin = new Thickness(10, 8, 10, 4)
        };

        Window? win = null;

        var tiles = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        tiles.Children.Add(BuildUrlOverlayTile("\uE8C8", "Copy URL", textBrush, mutedBrush, fieldBrush, accentBrush, async (tile, icon, caption) =>
        {
            UrlOverlaySetClipboard(url);
            status.Text = "Link copied";
            caption.Text = "Copied!";
            tile.BorderBrush = accentBrush;
            await Task.Delay(650);
            if (ReferenceEquals(_urlOverlayWindow, win)) CloseUrlOverlay();
        }));

        tiles.Children.Add(BuildUrlOverlayTile("\uE71B", "Shorten URL", textBrush, mutedBrush, fieldBrush, accentBrush, async (tile, icon, caption) =>
        {
            status.Text = "Shortening...";
            caption.Text = "Working...";
            tile.BorderBrush = accentBrush;
            var (shortUrl, error) = await ShortenUrlAsync(url);
            if (!ReferenceEquals(_urlOverlayWindow, win)) return;
            if (shortUrl == null)
            {
                status.Text = "Could not shorten: " + error;
                caption.Text = "Failed";
                return;
            }
            UrlOverlaySetClipboard(shortUrl);
            status.Text = "Copied: " + shortUrl;
            caption.Text = "Copied!";
            await Task.Delay(1600);
            if (ReferenceEquals(_urlOverlayWindow, win)) CloseUrlOverlay();
        }));

        tiles.Children.Add(BuildUrlOverlayTile("\uE72D", "Share", textBrush, mutedBrush, fieldBrush, accentBrush, (tile, icon, caption) =>
        {
            if (_isSharing) return Task.CompletedTask;
            _isSharing = true;

            // Stay calm & provide instant visual state change ("stay calm, change color")
            tile.Background = accentBrush;
            tile.BorderBrush = accentBrush;
            icon.Foreground = Brushes.White;
            caption.Foreground = Brushes.White;
            caption.Text = "Sharing...";
            status.Text = "Opening Windows Share...";
            tile.IsEnabled = false;

            // Clear keyboard focus from UrlInput so focus does not bounce and re-trigger Omnibox_UrlFocused
            try { Keyboard.ClearFocus(); } catch { }

            Rect? anchorRect = null;
            try
            {
                Point screenPt = tile.PointToScreen(new Point(0, 0));
                double scaleX = 1.0, scaleY = 1.0;
                var src = PresentationSource.FromVisual(tile);
                if (src?.CompositionTarget != null)
                {
                    scaleX = src.CompositionTarget.TransformToDevice.M11;
                    scaleY = src.CompositionTarget.TransformToDevice.M22;
                }
                anchorRect = new Rect(screenPt.X, screenPt.Y, tile.ActualWidth * scaleX, tile.ActualHeight * scaleY);
            }
            catch (Exception ex)
            {
                LogService.Debug("urloverlay", () => $"PointToScreen failed: {ex.Message}");
            }

            string? shareUrl = url ?? GetOverlayShareUrl();
            string? shareTitle = CurrentBrowser?.MainWebView?.CoreWebView2?.DocumentTitle ?? CurrentTab?.Title;

            ShowSystemShare(shareUrl, shareTitle, anchorRect);
            return Task.CompletedTask;
        }));

        var stack = new StackPanel();
        stack.Children.Add(status);
        stack.Children.Add(tiles);

        var root = new Border
        {
            Background = chromeBrush,
            BorderBrush = fieldBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Child = stack
        };

        win = new Window
        {
            Owner = this,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = root
        };

        Point origin = new Point(0, 0);
        try
        {
            var px = OmniboxControl.PointToScreen(new Point(0, OmniboxControl.ActualHeight));
            var src = PresentationSource.FromVisual(this);
            origin = src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(px) : px;
        }
        catch (Exception ex)
        {
            LogService.Debug("urloverlay", () => $"origin failed: {ex.Message}");
            return;
        }

        const double gap = 6;
        win.Left = origin.X;
        win.Top = origin.Y + gap;
        win.Opacity = 0;

        ApplyGlassBackdrop(win, root, origin.X, origin.Y + gap, 10);

        var shown = win;
        win.Loaded += (_, _) =>
        {
            var dur = TimeSpan.FromMilliseconds(130);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            shown.BeginAnimation(Window.TopProperty, new DoubleAnimation(origin.Y - 2, origin.Y + gap, dur) { EasingFunction = ease });
            shown.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        };
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_urlOverlayWindow, shown)) _urlOverlayWindow = null;
        };
        win.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseUrlOverlay();
            }
        };
        win.Deactivated += (_, _) =>
        {
            if (_isSharing) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!ReferenceEquals(_urlOverlayWindow, shown)) return;
                if (IsActive || OmniboxControl.IsKeyboardFocusWithin) return;
                CloseUrlOverlay();
            }));
        };

        if (!_urlOverlayHooked)
        {
            _urlOverlayHooked = true;
            LocationChanged += UrlOverlayOwnerChanged;
            SizeChanged += (s, e) => UrlOverlayOwnerChanged(s, e);
            StateChanged += UrlOverlayOwnerChanged;
            Deactivated += UrlOverlayOwnerDeactivated;
            Activated += UrlOverlayOwnerActivated;
        }

        _urlOverlayWindow = win;
        win.Show();
    }
}