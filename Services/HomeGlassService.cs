using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

public sealed class HomeGlassInlineLayer
{
    private enum Mode { None, Image, Video }

    private const double Pad = 48;
    private const double BlurRadius = 32;

    private readonly Grid _root;
    private readonly ImageBrush _imageSource;
    private readonly MediaElement _video;
    private readonly Grid _layer;
    private readonly Border _base;
    private readonly Border _blur;
    private readonly ImageBrush _blurImage;
    private VisualBrush? _blurVideo;
    private readonly Canvas _maskCanvas;
    private readonly Dictionary<FrameworkElement, Border> _shapes = new();
    private Mode _mode = Mode.None;
    private bool _refreshQueued;

    public HomeGlassInlineLayer(Grid root, ImageBrush imageSource, MediaElement video)
    {
        _root = root;
        _imageSource = imageSource;
        _video = video;

        _base = new Border { Background = Brushes.Black };

        _blurImage = new ImageBrush
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox
        };
        BindingOperations.SetBinding(_blurImage, ImageBrush.ImageSourceProperty,
            new Binding(nameof(ImageBrush.ImageSource)) { Source = _imageSource });

        _blur = new Border
        {
            Margin = new Thickness(-Pad),
            Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = BlurRadius,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            }
        };

        _layer = new Grid { IsHitTestVisible = false, ClipToBounds = true, Visibility = Visibility.Collapsed };
        _layer.Children.Add(_base);
        _layer.Children.Add(_blur);

        _maskCanvas = new Canvas { IsHitTestVisible = false };
        _layer.OpacityMask = new VisualBrush(_maskCanvas)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };

        int idx = _root.Children.IndexOf(_video);
        _root.Children.Insert(idx >= 0 ? idx + 1 : 0, _layer);

        DependencyPropertyDescriptor.FromProperty(ImageBrush.ImageSourceProperty, typeof(ImageBrush))
            ?.AddValueChanged(_imageSource, OnSourceChanged);
        DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(MediaElement))
            ?.AddValueChanged(_video, OnSourceChanged);

        _root.SizeChanged += OnRootSizeChanged;
        _root.IsVisibleChanged += OnRootVisibleChanged;
        if (_root.IsVisible) _root.LayoutUpdated += OnLayoutUpdated;
        QueueRefresh();
    }

    public void Register(FrameworkElement el)
    {
        if (_shapes.ContainsKey(el)) return;
        var shape = new Border { Background = Brushes.Black, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _shapes[el] = shape;
        _maskCanvas.Children.Add(shape);
        el.SizeChanged += OnElementSizeChanged;
        el.IsVisibleChanged += OnElementVisibleChanged;
        QueueRefresh();
    }

    public void Unregister(FrameworkElement el)
    {
        if (!_shapes.TryGetValue(el, out var shape)) return;
        _shapes.Remove(el);
        _maskCanvas.Children.Remove(shape);
        el.SizeChanged -= OnElementSizeChanged;
        el.IsVisibleChanged -= OnElementVisibleChanged;
        QueueRefresh();
    }

    public void Invalidate() => QueueRefresh();

    private void OnSourceChanged(object? sender, EventArgs e) => QueueRefresh();
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => QueueRefresh();
    private void OnElementSizeChanged(object sender, SizeChangedEventArgs e) => QueueRefresh();
    private void OnElementVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueRefresh();
    private void OnLayoutUpdated(object? sender, EventArgs e) => QueueRefresh();

    private void OnRootVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _root.LayoutUpdated -= OnLayoutUpdated;
        if (_root.IsVisible) _root.LayoutUpdated += OnLayoutUpdated;
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        _root.Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Render);
    }

    private Mode ResolveMode()
    {
        if (_video.Visibility == Visibility.Visible && _video.Source != null) return Mode.Video;
        return _imageSource.ImageSource != null ? Mode.Image : Mode.None;
    }

    private void Refresh()
    {
        _refreshQueued = false;
        try
        {
            double w = _root.ActualWidth;
            double h = _root.ActualHeight;
            var mode = ResolveMode();
            if (!_root.IsVisible || w <= 0 || h <= 0 || mode == Mode.None || _shapes.Count == 0)
            {
                _layer.Visibility = Visibility.Collapsed;
                return;
            }
            ApplySource(mode, w, h);
            UpdateMask(w, h);
            _layer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LogService.Write("HomeGlass", "Inline refresh failed, falling back to transparency: " + ex);
            _layer.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplySource(Mode mode, double w, double h)
    {
        var baseBrush = (Window.GetWindow(_root)?.Background as SolidColorBrush) ?? Brushes.Black;
        if (!ReferenceEquals(_base.Background, baseBrush)) _base.Background = baseBrush;

        if (mode != _mode)
        {
            _mode = mode;
            var opacityBinding = mode == Mode.Video
                ? new Binding(nameof(UIElement.Opacity)) { Source = _video }
                : new Binding(nameof(Brush.Opacity)) { Source = _imageSource };
            BindingOperations.SetBinding(_blur, UIElement.OpacityProperty, opacityBinding);
        }

        if (mode == Mode.Video)
        {
            _blurVideo ??= new VisualBrush(_video)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute
            };
            _blurVideo.Viewbox = new Rect(-Pad, -Pad, w + 2 * Pad, h + 2 * Pad);
            if (!ReferenceEquals(_blur.Background, _blurVideo)) _blur.Background = _blurVideo;
            return;
        }

        var src = _imageSource.ImageSource;
        if (src == null || src.Width <= 0 || src.Height <= 0) throw new InvalidOperationException("wallpaper image has no size");
        var frame = HomeGlassService.GetWallpaperFrame(new Rect(0, 0, w, h), src.Width / src.Height);
        _blurImage.Viewbox = new Rect(
            (-Pad - frame.X) / frame.Width,
            (-Pad - frame.Y) / frame.Height,
            (w + 2 * Pad) / frame.Width,
            (h + 2 * Pad) / frame.Height);
        if (!ReferenceEquals(_blur.Background, _blurImage)) _blur.Background = _blurImage;
    }

    private double EffectiveOpacity(FrameworkElement el)
    {
        double op = 1.0;
        DependencyObject? cur = el;
        while (cur != null && !ReferenceEquals(cur, _root))
        {
            if (cur is UIElement u) op *= u.Opacity;
            cur = cur is Visual || cur is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : null;
        }
        return op;
    }

    private void UpdateMask(double w, double h)
    {
        _maskCanvas.Width = w;
        _maskCanvas.Height = h;
        foreach (var pair in _shapes)
        {
            var el = pair.Key;
            var shape = pair.Value;
            double op = EffectiveOpacity(el);
            if (op <= 0.001 || !el.IsVisible || el.ActualWidth <= 0 || el.ActualHeight <= 0)
            {
                shape.Visibility = Visibility.Collapsed;
                continue;
            }
            Rect b;
            try
            {
                b = el.TransformToVisual(_root).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                shape.Visibility = Visibility.Collapsed;
                continue;
            }
            Canvas.SetLeft(shape, b.X);
            Canvas.SetTop(shape, b.Y);
            shape.Width = b.Width;
            shape.Height = b.Height;
            shape.CornerRadius = el is Border border ? border.CornerRadius : new CornerRadius(0);
            shape.Opacity = op;
            shape.Visibility = Visibility.Visible;
        }
    }
}