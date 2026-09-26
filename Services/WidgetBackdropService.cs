using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Horizon.Stealth.Services;

public static class WidgetBackdropService
{
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    private const uint MONITOR_DEFAULTTONEAREST = 2u;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    public static Rect GetMonitorBounds(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero)
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        return new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
    }

    private static double GetDpiScale(Window w)
    {
        var src = PresentationSource.FromVisual(w);
        return src?.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : 1.0;
    }

    public static Rect GetWindowScreenRect(Window w)
    {
        double dpi = GetDpiScale(w);
        return new Rect(w.Left * dpi, w.Top * dpi,
            Math.Max(1, w.ActualWidth) * dpi, Math.Max(1, w.ActualHeight) * dpi);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static Rect? GetSurfaceScreenRect()
    {
        var surface = WeatherBridge.WallpaperSurface;
        if (surface == null || !surface.IsVisible) return null;
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return null;

        var source = PresentationSource.FromVisual(surface);
        if (source?.CompositionTarget == null) return null;

        try
        {
            var topLeft = surface.PointToScreen(new Point(0, 0));
            double dpi = source.CompositionTarget.TransformToDevice.M11;
            return new Rect(topLeft.X, topLeft.Y, surface.ActualWidth * dpi, surface.ActualHeight * dpi);
        }
        catch
        {
            return null;
        }
    }

    public static Rect GetUvRect(Window widget)
    {
        var wallpaper = WeatherBridge.ThemeWallpaper;
        if (wallpaper == null) return new Rect(0, 0, 1, 1);

        var monitorBounds = GetSurfaceScreenRect() ?? GetMonitorBounds(widget);
        double imgW = wallpaper.PixelWidth;
        double imgH = wallpaper.PixelHeight;
        if (imgW <= 0 || imgH <= 0 || monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
            return new Rect(0, 0, 1, 1);

        double scale = Math.Max(monitorBounds.Width / imgW, monitorBounds.Height / imgH);
        double dispW = imgW * scale;
        double dispH = imgH * scale;
        double offsetX = monitorBounds.Left - (dispW - monitorBounds.Width) / 2.0;
        double offsetY = monitorBounds.Top - (dispH - monitorBounds.Height) / 2.0;

        var widgetRect = GetWindowScreenRect(widget);

        if (double.IsNaN(widgetRect.Left) || double.IsNaN(widgetRect.Top))
            return new Rect(0, 0, 1, 1);

        double u0 = (widgetRect.Left - offsetX) / dispW;
        double v0 = (widgetRect.Top - offsetY) / dispH;
        double u1 = (widgetRect.Right - offsetX) / dispW;
        double v1 = (widgetRect.Bottom - offsetY) / dispH;

        return new Rect(u0, v0, u1 - u0, v1 - v0);
    }

    private static Rect? GetOwnerScreenRect(Window widget)
    {
        var owner = widget.Owner ?? Application.Current?.MainWindow;
        if (owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized) return null;
        var hwnd = new WindowInteropHelper(owner).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static bool IsAppActive()
    {
        if (Application.Current == null) return false;
        foreach (Window w in Application.Current.Windows)
            if (w.IsActive) return true;
        return false;
    }

    private static Rect? GetOverlapLocal(Window widget)
    {
        if (!IsAppActive()) return null;

        var region = GetSurfaceScreenRect() ?? GetOwnerScreenRect(widget);
        if (region == null) return null;

        var widgetRect = GetWindowScreenRect(widget);
        if (double.IsNaN(widgetRect.Left) || double.IsNaN(widgetRect.Top)) return null;

        var o = Rect.Intersect(widgetRect, region.Value);
        if (o.IsEmpty || o.Width < 1 || o.Height < 1) return null;

        double dpi = GetDpiScale(widget);
        return new Rect((o.Left - widgetRect.Left) / dpi, (o.Top - widgetRect.Top) / dpi, o.Width / dpi, o.Height / dpi);
    }

    public static Action Bind(Window widget, ImageBrush brush, Action<Rect?>? onOverlapChanged = null)
    {
        FrameworkElement? boundSurface = null;
        Window? owner = widget.Owner ?? Application.Current?.MainWindow;
        bool pending = false;

        void Recompute()
        {
            try
            {
                onOverlapChanged?.Invoke(GetOverlapLocal(widget));
                var wallpaper = WeatherBridge.ThemeWallpaper;
                if (wallpaper == null)
                {
                    brush.ImageSource = null;
                    return;
                }
                brush.ImageSource = wallpaper;
                brush.Stretch = Stretch.Fill;
                brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                brush.Viewbox = GetUvRect(widget);
            }
            catch (Exception ex)
            {
                LogService.Write("WidgetBackdrop", "Recompute failed: " + ex);
            }
        }

        void Schedule()
        {
            Recompute();
            if (pending) return;
            pending = true;
            widget.Dispatcher.BeginInvoke(new Action(() =>
            {
                pending = false;
                Recompute();
            }), DispatcherPriority.Loaded);
        }

        void OnSurfaceSizeChanged(object? s, SizeChangedEventArgs e) => Schedule();
        void OnSurfaceVisibleChanged(object? s, DependencyPropertyChangedEventArgs e) => Schedule();
        void OnOwnerChanged(object? s, EventArgs e) => Schedule();
        void OnOwnerSizeChanged(object? s, SizeChangedEventArgs e) => Schedule();

        void RebindSurface()
        {
            if (boundSurface != null)
            {
                boundSurface.SizeChanged -= OnSurfaceSizeChanged;
                boundSurface.IsVisibleChanged -= OnSurfaceVisibleChanged;
            }
            boundSurface = WeatherBridge.WallpaperSurface;
            if (boundSurface != null)
            {
                boundSurface.SizeChanged += OnSurfaceSizeChanged;
                boundSurface.IsVisibleChanged += OnSurfaceVisibleChanged;
            }
            Schedule();
        }

        DateTime lastRecompute = DateTime.MinValue;
        bool recomputeQueued = false;

        void ThrottledRecompute()
        {
            var now = DateTime.UtcNow;
            if ((now - lastRecompute).TotalMilliseconds >= 16)
            {
                lastRecompute = now;
                Recompute();
            }
            else if (!recomputeQueued)
            {
                recomputeQueued = true;
                widget.Dispatcher.BeginInvoke(new Action(() =>
                {
                    recomputeQueued = false;
                    lastRecompute = DateTime.UtcNow;
                    Recompute();
                }), DispatcherPriority.Input);
            }
        }

        widget.LocationChanged += (s, e) => ThrottledRecompute();
        widget.SizeChanged += (s, e) => ThrottledRecompute();
        widget.StateChanged += (s, e) => Schedule();
        widget.Loaded += (s, e) => Recompute();

        if (owner != null)
        {
            owner.LocationChanged += OnOwnerChanged;
            owner.StateChanged += OnOwnerChanged;
            owner.SizeChanged += OnOwnerSizeChanged;
        }

        EventHandler onAppActivation = (s, e) => Schedule();
        if (Application.Current != null)
        {
            Application.Current.Activated += onAppActivation;
            Application.Current.Deactivated += onAppActivation;
        }

        Action themeHandler = () => widget.Dispatcher.BeginInvoke(new Action(Recompute));
        WeatherBridge.ThemeUpdated += themeHandler;

        Action surfaceHandler = () => widget.Dispatcher.BeginInvoke(new Action(RebindSurface));
        WeatherBridge.SurfaceChanged += surfaceHandler;

        RebindSurface();

        return () =>
        {
            WeatherBridge.ThemeUpdated -= themeHandler;
            WeatherBridge.SurfaceChanged -= surfaceHandler;
            if (boundSurface != null)
            {
                boundSurface.SizeChanged -= OnSurfaceSizeChanged;
                boundSurface.IsVisibleChanged -= OnSurfaceVisibleChanged;
            }
            if (owner != null)
            {
                owner.LocationChanged -= OnOwnerChanged;
                owner.StateChanged -= OnOwnerChanged;
                owner.SizeChanged -= OnOwnerSizeChanged;
            }
            if (Application.Current != null)
            {
                Application.Current.Activated -= onAppActivation;
                Application.Current.Deactivated -= onAppActivation;
            }
        };
    }
}