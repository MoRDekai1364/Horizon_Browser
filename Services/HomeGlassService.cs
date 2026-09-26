using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        var raw = new Rect((target.X - frame.X) / frame.Width, (target.Y - frame.Y) / frame.Height,
            target.Width / frame.Width, target.Height / frame.Height);

        double x0 = Math.Clamp(raw.X, 0.0, 1.0);
        double y0 = Math.Clamp(raw.Y, 0.0, 1.0);
        double x1 = Math.Clamp(raw.X + raw.Width, 0.0, 1.0);
        double y1 = Math.Clamp(raw.Y + raw.Height, 0.0, 1.0);
        if (x1 <= x0 || y1 <= y0) return null;

        return new Rect(x0, y0, x1 - x0, y1 - y0);
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

    public static (Action Unbind, Action Refresh) Bind(FrameworkElement host, ImageBrush brush, double padDip, Action<Rect?>? onOverlapChanged = null, bool requireOverlap = true)
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
                Rect? overlap = requireOverlap
                    ? GetOverlapLocal(host)
                    : (WeatherBridge.WallpaperSurface is { IsVisible: true } ? new Rect(0, 0, host.ActualWidth, host.ActualHeight) : (Rect?)null);
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

        return (
            Unbind: () =>
            {
                host.Loaded -= onLoaded;
                host.Unloaded -= onUnloaded;
                Detach();
            },
            Refresh: Schedule
        );
    }

    public const double DefaultEdgeGlassPad = 48.0;
    public const double DefaultEdgeGlassBlurRadius = 32.0;

    public static (Action Unbind, Action Refresh) AttachWallpaperEdgeGlass(
        FrameworkElement host,
        UIElement glassHost,
        Border blurTarget,
        double padDip = DefaultEdgeGlassPad,
        double blurRadius = DefaultEdgeGlassBlurRadius,
        Action<Rect?>? onOverlapChanged = null)
    {
        blurTarget.Effect = new System.Windows.Media.Effects.BlurEffect
        {
            Radius = blurRadius,
            KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
        };

        var brush = new ImageBrush { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
        blurTarget.Background = brush;

        return Bind(host, brush, padDip, overlap =>
        {
            ApplyOverlap(glassHost, overlap);
            onOverlapChanged?.Invoke(overlap);
        }, requireOverlap: false);
    }

    public const double MinGlassSize = 8.0;

    public static bool IsGlassEligible(FrameworkElement el)
    {
        if (HomeGlass.GetExclude(el)) return false;
        if (el.ActualWidth < MinGlassSize || el.ActualHeight < MinGlassSize) return false;
        Brush? bg = el switch
        {
            Border b => b.Background,
            Panel p => p.Background,
            Control c => c.Background,
            _ => null
        };
        return bg is SolidColorBrush scb && scb.Color.A > 0 && scb.Color.A < 255;
    }
}

public static class HomeGlass
{
    public static readonly DependencyProperty ExcludeProperty =
        DependencyProperty.RegisterAttached("Exclude", typeof(bool), typeof(HomeGlass),
            new PropertyMetadata(false));

    public static void SetExclude(DependencyObject el, bool value) => el.SetValue(ExcludeProperty, value);
    public static bool GetExclude(DependencyObject el) => (bool)el.GetValue(ExcludeProperty);
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
        el.Unloaded += OnElementUnloaded;
        QueueRefresh();
    }

    public void Unregister(FrameworkElement el)
    {
        if (!_shapes.TryGetValue(el, out var shape)) return;
        _shapes.Remove(el);
        _maskCanvas.Children.Remove(shape);
        el.SizeChanged -= OnElementSizeChanged;
        el.IsVisibleChanged -= OnElementVisibleChanged;
        el.Unloaded -= OnElementUnloaded;
        QueueRefresh();
    }

    private void OnElementUnloaded(object sender, RoutedEventArgs e) => Unregister((FrameworkElement)sender);

    public void RegisterSubtree(DependencyObject node)
    {
        if (node is FrameworkElement fe && !ReferenceEquals(fe, _root) && HomeGlassService.IsGlassEligible(fe))
            Register(fe);

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            RegisterSubtree(VisualTreeHelper.GetChild(node, i));
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

    private DateTime _lastRefreshUtc = DateTime.MinValue;

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        double elapsedMs = (DateTime.UtcNow - _lastRefreshUtc).TotalMilliseconds;
        if (elapsedMs >= 16)
        {
            _root.Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Render);
        }
        else
        {
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16 - elapsedMs)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Refresh();
            };
            timer.Start();
        }
    }

    private Mode ResolveMode()
    {
        if (_video.Visibility == Visibility.Visible && _video.Source != null) return Mode.Video;
        return _imageSource.ImageSource != null ? Mode.Image : Mode.None;
    }

    private const double RescanIntervalMs = 500.0;
    private DateTime _lastRescanUtc = DateTime.MinValue;

    private void Refresh()
    {
        _refreshQueued = false;
        _lastRefreshUtc = DateTime.UtcNow;
        try
        {
            if ((DateTime.UtcNow - _lastRescanUtc).TotalMilliseconds >= RescanIntervalMs)
            {
                _lastRescanUtc = DateTime.UtcNow;
                RegisterSubtree(_root);
            }

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
            bool diag = el.Name == "TestClockPill2";
            double op = EffectiveOpacity(el);
            if (diag) LogService.Write("PillDiag", $"op={op:0.###} isVisible={el.IsVisible} w={el.ActualWidth:0.#} h={el.ActualHeight:0.#} isLoaded={el.IsLoaded} inShapes={_shapes.ContainsKey(el)}");
            if (op <= 0.001 || !el.IsVisible || el.ActualWidth <= 0 || el.ActualHeight <= 0)
            {
                shape.Visibility = Visibility.Collapsed;
                if (diag) LogService.Write("PillDiag", "  -> collapsed (opacity/visible/size gate)");
                continue;
            }
            Rect b;
            try
            {
                b = el.TransformToVisual(_root).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                if (diag) LogService.Write("PillDiag", $"  -> bounds x={b.X:0.#} y={b.Y:0.#} w={b.Width:0.#} h={b.Height:0.#}");
            }
            catch (InvalidOperationException ex)
            {
                shape.Visibility = Visibility.Collapsed;
                if (diag) LogService.Write("PillDiag", "  -> TransformToVisual threw: " + ex.Message);
                continue;
            }
            Canvas.SetLeft(shape, b.X);
            Canvas.SetTop(shape, b.Y);
            shape.Width = b.Width;
            shape.Height = b.Height;
            shape.CornerRadius = el is Border border ? border.CornerRadius : new CornerRadius(0);
            shape.Opacity = op;
            shape.Visibility = Visibility.Visible;
            if (diag) LogService.Write("PillDiag", $"  -> shape set: opacity={shape.Opacity:0.###} visibility={shape.Visibility} cornerRadius={shape.CornerRadius}");
        }
        RasterizeMask(w, h);
    }

    private double GetMaskDeviceScale()
    {
        var src = PresentationSource.FromVisual(_root);
        return src?.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : 1.0;
    }

    private RenderTargetBitmap? _maskBitmap;
    private ImageBrush? _maskBrush;
    private int _maskPw;
    private int _maskPh;
    private double _maskScale;

    private void RasterizeMask(double w, double h)
    {
        double k = GetMaskDeviceScale();
        int pw = Math.Max(1, (int)Math.Round(w * k));
        int ph = Math.Max(1, (int)Math.Round(h * k));

        _maskCanvas.Measure(new Size(w, h));
        _maskCanvas.Arrange(new Rect(0, 0, w, h));
        _maskCanvas.UpdateLayout();

        if (_maskBitmap == null || pw != _maskPw || ph != _maskPh || Math.Abs(k - _maskScale) > 0.0001)
        {
            _maskPw = pw;
            _maskPh = ph;
            _maskScale = k;
            _maskBitmap = new RenderTargetBitmap(pw, ph, 96 * k, 96 * k, PixelFormats.Pbgra32);
            _maskBrush = new ImageBrush(_maskBitmap)
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h)
            };
        }
        else
        {
            _maskBitmap.Clear();
            _maskBrush!.Viewport = new Rect(0, 0, w, h);
        }

        _maskBitmap.Render(_maskCanvas);

        if (!ReferenceEquals(_layer.OpacityMask, _maskBrush))
            _layer.OpacityMask = _maskBrush;
    }
}