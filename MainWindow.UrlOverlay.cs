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
    private Window? _urlOverlayWindow;
    private bool _urlOverlayHooked;
    private static readonly HttpClient _shortenHttp = new() { Timeout = TimeSpan.FromSeconds(8) };

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShareInterop
    {
        void GetIids(out int iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow([In] IntPtr appWindow);
    }

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", ExactSpelling = true, EntryPoint = "RoGetActivationFactory")]
    private static extern int RoGetActivationFactoryRaw(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    private void ShowSystemShare()
    {
        IntPtr hstring = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetForegroundWindow(hwnd);
            const string className = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            if (WindowsCreateString(className, className.Length, out hstring) < 0) return;
            var interopIid = new Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8");
            int hr = RoGetActivationFactoryRaw(hstring, ref interopIid, out factory);
            if (hr < 0 || factory == IntPtr.Zero)
            {
                LogService.Debug("share", () => $"activation factory failed: 0x{hr:X8}");
                return;
            }
            var interop = (IShareInterop)Marshal.GetObjectForIUnknown(factory);
            var managerIid = new Guid("a5caee9b-8708-49d1-8d36-67d25a8da00c");
            IntPtr managerPtr = interop.GetForWindow(hwnd, ref managerIid);
            var manager = global::WinRT.MarshalInspectable<DataTransferManager>.FromAbi(managerPtr);
            manager.DataRequested -= OnShareDataRequested;
            manager.DataRequested += OnShareDataRequested;
            interop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Share Interface");
        }
        finally
        {
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
        }
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
        ShowUrlOverlay();
    }

    private void Omnibox_UrlUnfocused(object? sender, EventArgs e)
    {
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
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var w = _urlOverlayWindow;
            if (w == null) return;
            if (w.IsActive) return;
            CloseUrlOverlay();
        }));
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

    private FrameworkElement BuildUrlOverlayTile(string glyph, string label, Brush textBrush, Brush mutedBrush, Brush fieldBrush, Brush accentBrush, Func<Task> action)
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
        tile.MouseEnter += (_, _) => tile.BorderBrush = accentBrush;
        tile.MouseLeave += (_, _) => tile.BorderBrush = Brushes.Transparent;
        tile.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            try
            {
                await action();
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

        tiles.Children.Add(BuildUrlOverlayTile("\uE8C8", "Copy URL", textBrush, mutedBrush, fieldBrush, accentBrush, async () =>
        {
            UrlOverlaySetClipboard(url);
            status.Text = "Link copied";
            await Task.Delay(650);
            if (ReferenceEquals(_urlOverlayWindow, win)) CloseUrlOverlay();
        }));

        tiles.Children.Add(BuildUrlOverlayTile("\uE71B", "Shorten URL", textBrush, mutedBrush, fieldBrush, accentBrush, async () =>
        {
            status.Text = "Shortening...";
            var (shortUrl, error) = await ShortenUrlAsync(url);
            if (!ReferenceEquals(_urlOverlayWindow, win)) return;
            if (shortUrl == null)
            {
                status.Text = "Could not shorten: " + error;
                return;
            }
            UrlOverlaySetClipboard(shortUrl);
            status.Text = "Copied: " + shortUrl;
            await Task.Delay(1600);
            if (ReferenceEquals(_urlOverlayWindow, win)) CloseUrlOverlay();
        }));

        tiles.Children.Add(BuildUrlOverlayTile("\uE72D", "Share", textBrush, mutedBrush, fieldBrush, accentBrush, () =>
        {
            CloseUrlOverlay();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowSystemShare));
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
        }

        _urlOverlayWindow = win;
        win.Show();
    }
}