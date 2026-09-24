using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Horizon.Stealth.Services;

public static class WidgetBackdropService
{
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
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

        double u0 = Clamp01((widgetRect.Left - offsetX) / dispW);
        double v0 = Clamp01((widgetRect.Top - offsetY) / dispH);
        double u1 = Clamp01((widgetRect.Right - offsetX) / dispW);
        double v1 = Clamp01((widgetRect.Bottom - offsetY) / dispH);

        if (u1 <= u0) u1 = Math.Min(1.0, u0 + 0.01);
        if (v1 <= v0) v1 = Math.Min(1.0, v0 + 0.01);

        return new Rect(u0, v0, u1 - u0, v1 - v0);
    }

    public static Action Bind(Window widget, ImageBrush brush)
    {
        FrameworkElement? boundSurface = null;

        void Recompute()
        {
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

        void OnSurfaceSizeChanged(object? s, SizeChangedEventArgs e) => Recompute();

        void RebindSurface()
        {
            if (boundSurface != null) boundSurface.SizeChanged -= OnSurfaceSizeChanged;
            boundSurface = WeatherBridge.WallpaperSurface;
            if (boundSurface != null) boundSurface.SizeChanged += OnSurfaceSizeChanged;
            Recompute();
        }

        widget.LocationChanged += (s, e) => Recompute();
        widget.SizeChanged += (s, e) => Recompute();
        widget.Loaded += (s, e) => Recompute();

        Action themeHandler = () => widget.Dispatcher.BeginInvoke(new Action(Recompute));
        WeatherBridge.ThemeUpdated += themeHandler;

        Action surfaceHandler = () => widget.Dispatcher.BeginInvoke(new Action(RebindSurface));
        WeatherBridge.SurfaceChanged += surfaceHandler;

        RebindSurface();

        return () =>
        {
            WeatherBridge.ThemeUpdated -= themeHandler;
            WeatherBridge.SurfaceChanged -= surfaceHandler;
            if (boundSurface != null) boundSurface.SizeChanged -= OnSurfaceSizeChanged;
        };
    }
}