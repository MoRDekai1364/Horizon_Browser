using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Horizon.Stealth.Services;

public static class HomeGlassService
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    private const uint MONITOR_DEFAULTTONEAREST = 2u;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    public static bool IsAppActive()
    {
        var app = Application.Current;
        if (app == null) return false;
        foreach (Window w in app.Windows)
            if (w.IsActive) return true;
        return false;
    }

    private static double GetDeviceScale(Visual v)
    {
        var src = PresentationSource.FromVisual(v);
        return src?.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : 1.0;
    }

    public static Rect? GetElementScreenRect(FrameworkElement el)
    {
        if (!el.IsLoaded || el.ActualWidth <= 0 || el.ActualHeight <= 0) return null;
        if (PresentationSource.FromVisual(el) == null) return null;
        try
        {
            var a = el.PointToScreen(new Point(0, 0));
            var b = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            return new Rect(a, b);
        }
        catch (Exception ex)
        {
            LogService.Write("HomeGlass", "GetElementScreenRect failed: " + ex.Message);
            return null;
        }
    }

    public static Rect? GetSurfaceScreenRect()
    {
        var surface = WeatherBridge.WallpaperSurface;
        if (surface == null || !surface.IsVisible) return null;
        return GetElementScreenRect(surface);
    }

    public static Rect? GetBrowserScreenRect()
    {
        var main = Application.Current?.MainWindow;
        if (main == null || !main.IsVisible || main.WindowState == WindowState.Minimized) return null;
        var hwnd = new WindowInteropHelper(main).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static Rect GetMonitorBounds(FrameworkElement el)
    {
        double k = GetDeviceScale(el);
        var fallback = new Rect(0, 0, SystemParameters.PrimaryScreenWidth * k, SystemParameters.PrimaryScreenHeight * k);
        if (PresentationSource.FromVisual(el) is not HwndSource src || src.Handle == IntPtr.Zero) return fallback;
        var hMonitor = MonitorFromWindow(src.Handle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi)) return fallback;
        return new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
    }

    public static Rect GetWallpaperFrame(Rect surface, double imageAspect)
    {
        double fw, fh;
        if (surface.Width / surface.Height > imageAspect) { fw = surface.Width; fh = fw / imageAspect; }
        else { fh = surface.Height; fw = fh * imageAspect; }
        return new Rect(surface.X + (surface.Width - fw) / 2.0, surface.Y + (surface.Height - fh) / 2.0, fw, fh);
    }

    public static Rect? GetViewbox(FrameworkElement el, double padDip)
    {
        var wp = WeatherBridge.ThemeWallpaper;
        if (wp == null || wp.PixelWidth <= 0 || wp.PixelHeight <= 0) return null;

        var r = GetElementScreenRect(el);
        if (r == null) return null;

        var surface = GetSurfaceScreenRect() ?? GetMonitorBounds(el);
        if (surface.Width <= 0 || surface.Height <= 0) return null;

        double pad = padDip * GetDeviceScale(el);
        var target = new Rect(r.Value.X - pad, r.Value.Y - pad, r.Value.Width + 2 * pad, r.Value.Height + 2 * pad);
        var frame = GetWallpaperFrame(surface, (double)wp.PixelWidth / wp.PixelHeight);

        return new Rect((target.X - frame.X) / frame.Width, (target.Y - frame.Y) / frame.Height,
            target.Width / frame.Width, target.Height / frame.Height);
    }

    public static Rect? GetOverlapLocal(FrameworkElement el)
    {
        if (!IsAppActive()) return null;

        var r = GetElementScreenRect(el);
        if (r == null) return null;

        var region = GetSurfaceScreenRect() ?? GetBrowserScreenRect();
        if (region == null) return null;

        var o = Rect.Intersect(r.Value, region.Value);
        if (o.IsEmpty || o.Width < 1 || o.Height < 1) return null;

        double k = GetDeviceScale(el);
        return new Rect((o.Left - r.Value.Left) / k, (o.Top - r.Value.Top) / k, o.Width / k, o.Height / k);
    }

    public static void ApplyOverlap(UIElement glassHost, Rect? overlap)
    {
        if (overlap == null)
        {
            glassHost.Visibility = Visibility.Collapsed;
            return;
        }
        glassHost.Clip = new RectangleGeometry(overlap.Value);
        glassHost.Visibility = Visibility.Visible;
    }

    public static Action Bind(FrameworkElement host, ImageBrush brush, double padDip, Action<Rect?>? onOverlapChanged = null)
    {
        FrameworkElement? boundSurface = null;
        Window? mainWindow = null;
        Window? hostWindow = null;
        bool attached = false;
        bool pending = false;

        void Recompute()
        {
            try
            {
                var overlap = GetOverlapLocal(host);
                onOverlapChanged?.Invoke(overlap);
                var wallpaper = WeatherBridge.ThemeWallpaper;
                var viewbox = (wallpaper != null && overlap != null) ? GetViewbox(host, padDip) : null;
                if (wallpaper == null || viewbox == null)
                {
                    brush.ImageSource = null;
                    return;
                }
                brush.ImageSource = wallpaper;
                brush.Stretch = Stretch.Fill;
                brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                brush.Viewbox = viewbox.Value;
            }
            catch (Exception ex)
            {
                LogService.Write("HomeGlass", "Recompute failed: " + ex);
            }
        }

        void Schedule()
        {
            Recompute();
            if (pending) return;
            pending = true;
            host.Dispatcher.BeginInvoke(new Action(() =>
            {
                pending = false;
                Recompute();
            }), DispatcherPriority.Loaded);
        }

        void OnSizeChanged(object? s, SizeChangedEventArgs e) => Schedule();
        void OnVisibleChanged(object? s, DependencyPropertyChangedEventArgs e) => Schedule();
        void OnWindowChanged(object? s, EventArgs e) => Schedule();

        Action themeHandler = () => host.Dispatcher.BeginInvoke(new Action(Schedule));

        void RebindSurface()
        {
            if (boundSurface != null)
            {
                boundSurface.SizeChanged -= OnSizeChanged;
                boundSurface.IsVisibleChanged -= OnVisibleChanged;
            }
            boundSurface = WeatherBridge.WallpaperSurface;
            if (boundSurface != null)
            {
                boundSurface.SizeChanged += OnSizeChanged;
                boundSurface.IsVisibleChanged += OnVisibleChanged;
            }
            Schedule();
        }

        Action surfaceHandler = () => host.Dispatcher.BeginInvoke(new Action(RebindSurface));

        void SubscribeWindow(Window? w)
        {
            if (w == null) return;
            w.LocationChanged += OnWindowChanged;
            w.StateChanged += OnWindowChanged;
            w.SizeChanged += OnSizeChanged;
        }

        void UnsubscribeWindow(Window? w)
        {
            if (w == null) return;
            w.LocationChanged -= OnWindowChanged;
            w.StateChanged -= OnWindowChanged;
            w.SizeChanged -= OnSizeChanged;
        }

        void Attach()
        {
            if (attached) return;
            attached = true;
            host.SizeChanged += OnSizeChanged;
            host.IsVisibleChanged += OnVisibleChanged;
            mainWindow = Application.Current?.MainWindow;
            hostWindow = Window.GetWindow(host);
            SubscribeWindow(mainWindow);
            if (hostWindow != mainWindow) SubscribeWindow(hostWindow);
            if (Application.Current != null)
            {
                Application.Current.Activated += OnWindowChanged;
                Application.Current.Deactivated += OnWindowChanged;
            }
            WeatherBridge.ThemeUpdated += themeHandler;
            WeatherBridge.SurfaceChanged += surfaceHandler;
            RebindSurface();
        }

        void Detach()
        {
            if (!attached) return;
            attached = false;
            host.SizeChanged -= OnSizeChanged;
            host.IsVisibleChanged -= OnVisibleChanged;
            UnsubscribeWindow(mainWindow);
            if (hostWindow != mainWindow) UnsubscribeWindow(hostWindow);
            mainWindow = null;
            hostWindow = null;
            if (Application.Current != null)
            {
                Application.Current.Activated -= OnWindowChanged;
                Application.Current.Deactivated -= OnWindowChanged;
            }
            WeatherBridge.ThemeUpdated -= themeHandler;
            WeatherBridge.SurfaceChanged -= surfaceHandler;
            if (boundSurface != null)
            {
                boundSurface.SizeChanged -= OnSizeChanged;
                boundSurface.IsVisibleChanged -= OnVisibleChanged;
                boundSurface = null;
            }
        }

        RoutedEventHandler onLoaded = (s, e) => Attach();
        RoutedEventHandler onUnloaded = (s, e) => Detach();
        host.Loaded += onLoaded;
        host.Unloaded += onUnloaded;
        if (host.IsLoaded) Attach();

        return () =>
        {
            host.Loaded -= onLoaded;
            host.Unloaded -= onUnloaded;
            Detach();
        };
    }
}