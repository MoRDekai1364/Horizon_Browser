using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Horizon.Stealth.Services;

public enum ContrastMode
{
    Auto,
    Off
}

public static class ContrastGuard
{
    public static bool Enabled = true;
    public static double MinContrast = 3.5;
    public static double MinContrastOnImage = 4.5;
    public static int LogBudget = 60;

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.RegisterAttached("Mode", typeof(ContrastMode), typeof(ContrastGuard),
            new FrameworkPropertyMetadata(ContrastMode.Auto, FrameworkPropertyMetadataOptions.Inherits));

    public static ContrastMode GetMode(DependencyObject o) => (ContrastMode)o.GetValue(ModeProperty);
    public static void SetMode(DependencyObject o, ContrastMode v) => o.SetValue(ModeProperty, v);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(object), typeof(ContrastGuard), new PropertyMetadata(null));

    private static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(ContrastGuard), new PropertyMetadata(false));

    private sealed class FixState
    {
        public Brush? Applied;
        public Brush? Original;
        public bool Local;
    }

    private sealed class Small
    {
        public int W;
        public int H;
        public byte[] Px = Array.Empty<byte>();
        public Color Avg;
    }

    private sealed class Backdrop
    {
        public readonly List<(Color C, double A)> Layers = new(8);
        public double Transmit = 1.0;
        public bool Image;
        public Visual? Root;
        public Point Center;
        public bool HaveCenter;
        public FrameworkElement Target = null!;
        public bool Done => Transmit <= 0.03;
    }

    private static readonly HashSet<FrameworkElement> Pending = new();
    private static readonly ConditionalWeakTable<BitmapSource, Small> SmallCache = new();
    private static bool _initialized;
    private static bool _flushQueued;
    private static DispatcherTimer? _debounce;
    private static DispatcherTimer? _sweep;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        EventManager.RegisterClassHandler(typeof(TextBoxBase), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        EventManager.RegisterClassHandler(typeof(PasswordBox), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        EventManager.RegisterClassHandler(typeof(Control), UIElement.MouseEnterEvent, new MouseEventHandler(OnMouseChanged));
        EventManager.RegisterClassHandler(typeof(Control), UIElement.MouseLeaveEvent, new MouseEventHandler(OnMouseChanged));

        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) =>
        {
            _debounce!.Stop();
            SweepAllWindows();
        };

        _sweep = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _sweep.Tick += (_, _) =>
        {
            var app = Application.Current;
            if (app == null) return;
            foreach (Window w in app.Windows)
            {
                if (w.IsActive)
                {
                    SweepAllWindows();
                    return;
                }
            }
        };
        _sweep.Start();

        WeatherBridge.ThemeUpdated += RequestRefresh;
        WeatherBridge.SurfaceChanged += RequestRefresh;
    }

    public static void RequestRefresh()
    {
        var app = Application.Current;
        if (app == null || !_initialized) return;
        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_debounce == null) return;
            _debounce.Stop();
            _debounce.Start();
        }));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!Enabled) return;

        if (sender is Window w)
        {
            ApplyWindowDefaults(w);
            return;
        }

        if (sender is not FrameworkElement fe || !IsTarget(fe)) return;

        if (!(bool)fe.GetValue(HookedProperty))
        {
            fe.SetValue(HookedProperty, true);
            fe.IsVisibleChanged += OnStateChanged;
            fe.IsEnabledChanged += OnStateChanged;
        }

        Enqueue(fe);
    }

    private static void OnStateChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Enabled && sender is FrameworkElement fe && fe.IsLoaded) Enqueue(fe);
    }

    private static void OnMouseChanged(object sender, MouseEventArgs e)
    {
        if (!Enabled || sender is not FrameworkElement fe || !fe.IsLoaded) return;
        if (fe is not (ButtonBase or MenuItem or ListBoxItem or ComboBoxItem or TabItem or TreeViewItem)) return;

        var sink = new List<FrameworkElement>();
        CollectTargets(fe, sink);
        foreach (var el in sink) Enqueue(el);
    }

    private static void ApplyWindowDefaults(Window w)
    {
        try
        {
            if (DependencyPropertyHelper.GetValueSource(w, Control.ForegroundProperty).BaseValueSource != BaseValueSource.Default) return;
            if (w.TryFindResource("Brush_Text") == null) return;
            w.SetResourceReference(Control.ForegroundProperty, "Brush_Text");
        }
        catch
        {
        }
    }

    private static bool IsTarget(FrameworkElement fe) => fe is TextBlock || fe is TextBoxBase || fe is PasswordBox;

    private static void CollectTargets(DependencyObject root, List<FrameworkElement> sink)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement u && (u.Visibility != Visibility.Visible || u.Opacity <= 0.01)) continue;
            if (child is FrameworkElement fe && IsTarget(fe)) sink.Add(fe);
            CollectTargets(child, sink);
        }
    }

    private static void SweepAllWindows()
    {
        var app = Application.Current;
        if (app == null || !Enabled) return;

        foreach (Window w in app.Windows)
        {
            if (!w.IsVisible) continue;
            var sink = new List<FrameworkElement>();
            CollectTargets(w, sink);
            foreach (var el in sink) Pending.Add(el);
        }

        if (Pending.Count > 0 && !_flushQueued)
        {
            _flushQueued = true;
            app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Flush));
        }
    }

    private static void Enqueue(FrameworkElement el)
    {
        Pending.Add(el);
        if (_flushQueued) return;
        _flushQueued = true;
        el.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Flush));
    }

    private static void Flush()
    {
        var batch = new List<FrameworkElement>(300);
        foreach (var el in Pending)
        {
            batch.Add(el);
            if (batch.Count >= 300) break;
        }
        foreach (var el in batch) Pending.Remove(el);

        foreach (var el in batch)
        {
            try
            {
                Evaluate(el);
            }
            catch
            {
            }
        }

        var app = Application.Current;
        if (Pending.Count > 0 && app != null)
            app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Flush));
        else
            _flushQueued = false;
    }

    private static void Evaluate(FrameworkElement el)
    {
        var prop = el is TextBlock ? TextBlock.ForegroundProperty : Control.ForegroundProperty;

        Restore(el, prop);

        if (!Enabled || !el.IsLoaded || !el.IsVisible || el.ActualWidth <= 0 || el.ActualHeight <= 0) return;
        if (GetMode(el) == ContrastMode.Off) return;
        if (el is TextBlock tb && string.IsNullOrEmpty(tb.Text)) return;

        if (!TryGetColor(el.GetValue(prop) as Brush, out var fg, out var fgAlpha)) return;
        double effAlpha = fgAlpha * el.Opacity;
        if (effAlpha <= 0.02) return;

        var bg = GetBackdrop(el, out bool image);
        double min = image ? MinContrastOnImage : MinContrast;
        if (ContrastRatio(Over(fg, effAlpha, bg), bg) >= min) return;

        var fixedColor = FixColor(fg, effAlpha, bg, min);
        var brush = new SolidColorBrush(fixedColor);
        brush.Freeze();

        var state = new FixState
        {
            Applied = brush,
            Original = el.GetValue(prop) as Brush,
            Local = el.ReadLocalValue(prop) == DependencyProperty.UnsetValue
        };

        if (state.Local) el.SetValue(prop, brush);
        else el.SetCurrentValue(prop, brush);
        el.SetValue(StateProperty, state);

        if (LogBudget > 0)
        {
            LogBudget--;
            string label = el is TextBlock t ? t.Text : el.GetType().Name;
            if (label.Length > 24) label = label.Substring(0, 24);
            LogService.Write("CONTRAST", $"{el.GetType().Name} '{label}' fg={fg} bg={bg} image={image} -> {fixedColor}");
        }
    }

    private static void Restore(FrameworkElement el, DependencyProperty prop)
    {
        if (el.GetValue(StateProperty) is not FixState st) return;
        el.ClearValue(StateProperty);
        if (!ReferenceEquals(el.GetValue(prop), st.Applied)) return;
        if (st.Local) el.ClearValue(prop);
        else el.SetCurrentValue(prop, st.Original);
    }

    private static Color GetBackdrop(FrameworkElement el, out bool image)
    {
        var s = new Backdrop { Target = el };

        DependencyObject top = el;
        DependencyObject? p;
        while ((p = VisualTreeHelper.GetParent(top)) != null) top = p;
        s.Root = top as Visual;

        if (s.Root != null)
        {
            try
            {
                s.Center = el.TransformToVisual(s.Root).Transform(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
                s.HaveCenter = true;
            }
            catch
            {
            }
        }

        if (el is Control c) AddTemplateLayers(s, c);
        else AddOwn(s, el, false);

        DependencyObject cur = el;
        while (!s.Done)
        {
            var parent = VisualTreeHelper.GetParent(cur);
            if (parent == null) break;

            if (parent is Panel panel && cur is UIElement cu)
            {
                int idx = panel.Children.IndexOf(cu);
                for (int i = idx - 1; i >= 0 && !s.Done; i--)
                {
                    if (panel.Children[i] is FrameworkElement sib) ScanSubtree(s, sib, 2);
                }
            }

            AddOwn(s, parent, false);
            cur = parent;
        }

        var result = GetBaseColor();
        for (int i = s.Layers.Count - 1; i >= 0; i--)
            result = Over(s.Layers[i].C, s.Layers[i].A, result);

        image = s.Image;
        return result;
    }

    private static Color GetBaseColor()
    {
        if (Application.Current?.TryFindResource("Brush_Background") is SolidColorBrush b)
            return Color.FromRgb(b.Color.R, b.Color.G, b.Color.B);
        return Colors.Black;
    }

    private static void AddTemplateLayers(Backdrop s, Control c)
    {
        int before = s.Layers.Count;
        var chain = new List<DependencyObject>(4);
        DependencyObject? node = VisualTreeHelper.GetChildrenCount(c) > 0 ? VisualTreeHelper.GetChild(c, 0) : null;

        while (node != null && chain.Count < 4)
        {
            if (node is not (Border or Panel)) break;
            chain.Add(node);
            node = VisualTreeHelper.GetChildrenCount(node) > 0 ? VisualTreeHelper.GetChild(node, 0) : null;
        }

        for (int i = chain.Count - 1; i >= 0 && !s.Done; i--) AddOwn(s, chain[i], false);
        if (s.Layers.Count == before && !s.Done) AddOwn(s, c, true);
    }

    private static void ScanSubtree(Backdrop s, FrameworkElement node, int depth)
    {
        if (s.Done || node.Visibility != Visibility.Visible || node.Opacity <= 0.01) return;
        if (!Covers(s, node)) return;

        if (depth > 0)
        {
            if (node is Panel p)
            {
                for (int i = p.Children.Count - 1; i >= 0 && !s.Done; i--)
                {
                    if (p.Children[i] is FrameworkElement ch) ScanSubtree(s, ch, depth - 1);
                }
            }
            else if (node is Border b && b.Child is FrameworkElement bc)
            {
                ScanSubtree(s, bc, depth - 1);
            }
        }

        AddOwn(s, node, false);
    }

    private static bool Covers(Backdrop s, FrameworkElement node)
    {
        if (!s.HaveCenter || s.Root == null || node.ActualWidth <= 0 || node.ActualHeight <= 0) return false;
        try
        {
            var inv = node.TransformToVisual(s.Root).Inverse;
            if (inv == null) return false;
            var pt = inv.Transform(s.Center);
            return pt.X >= 0 && pt.Y >= 0 && pt.X <= node.ActualWidth && pt.Y <= node.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private static void AddOwn(Backdrop s, DependencyObject node, bool allowControl)
    {
        if (s.Done) return;

        Brush? brush;
        switch (node)
        {
            case Border b: brush = b.Background; break;
            case Panel p: brush = p.Background; break;
            case System.Windows.Shapes.Shape sh: brush = sh.Fill; break;
            case TextBlock tb: brush = tb.Background; break;
            case Control c when allowControl: brush = c.Background; break;
            default: return;
        }
        if (brush == null) return;

        double opacity = 1.0;
        if (node is UIElement ue)
        {
            if (ue.Visibility != Visibility.Visible) return;
            opacity = ue.Opacity;
        }

        Color col;
        double a;
        bool isImage = false;

        switch (brush)
        {
            case SolidColorBrush sb:
                col = sb.Color;
                a = sb.Color.A / 255.0 * sb.Opacity;
                break;
            case GradientBrush gb:
                if (!TryGetColor(gb, out col, out a)) return;
                a *= gb.Opacity;
                break;
            case ImageBrush ib:
                if (!TryImageColor(ib, s.Target, out col)) return;
                a = ib.Opacity;
                isImage = true;
                break;
            default:
                return;
        }

        a = Math.Min(a * opacity, 1.0);
        if (a <= 0.01) return;

        if (isImage && a >= 0.15) s.Image = true;
        s.Layers.Add((col, a));
        s.Transmit *= 1.0 - a;
    }

    private static bool TryGetColor(Brush? b, out Color c, out double alpha)
    {
        c = default;
        alpha = 0;
        switch (b)
        {
            case SolidColorBrush sb:
                c = sb.Color;
                alpha = sb.Color.A / 255.0 * sb.Opacity;
                return true;
            case GradientBrush gb when gb.GradientStops.Count > 0:
                double r = 0, g = 0, bl = 0, al = 0;
                foreach (var stop in gb.GradientStops)
                {
                    r += stop.Color.R;
                    g += stop.Color.G;
                    bl += stop.Color.B;
                    al += stop.Color.A;
                }
                int n = gb.GradientStops.Count;
                c = Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(bl / n));
                alpha = al / n / 255.0 * gb.Opacity;
                return true;
            default:
                return false;
        }
    }

    private static bool TryImageColor(ImageBrush ib, FrameworkElement target, out Color c)
    {
        c = default;
        if (ib.ImageSource is not BitmapSource bs) return false;

        var small = GetSmall(bs);
        if (small == null) return false;

        var wp = WeatherBridge.ThemeWallpaper;
        if (wp != null && ReferenceEquals(bs, wp))
        {
            var vb = HomeGlassService.GetViewbox(target, 0);
            if (vb != null)
            {
                c = SampleRegion(small, vb.Value);
                return true;
            }
        }

        c = small.Avg;
        return true;
    }

    private static Small? GetSmall(BitmapSource src)
    {
        if (SmallCache.TryGetValue(src, out var hit)) return hit;

        try
        {
            int sw = src.PixelWidth, sh = src.PixelHeight;
            if (sw <= 0 || sh <= 0) return null;

            double k = Math.Min(1.0, 64.0 / Math.Max(sw, sh));
            BitmapSource scaled = k < 1.0 ? new TransformedBitmap(src, new ScaleTransform(k, k)) : src;
            var conv = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

            int w = conv.PixelWidth, h = conv.PixelHeight;
            if (w <= 0 || h <= 0) return null;

            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);

            long r = 0, g = 0, b = 0;
            int count = w * h;
            for (int i = 0; i < px.Length; i += 4)
            {
                b += px[i];
                g += px[i + 1];
                r += px[i + 2];
            }

            var small = new Small
            {
                W = w,
                H = h,
                Px = px,
                Avg = Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count))
            };
            SmallCache.Add(src, small);
            return small;
        }
        catch
        {
            return null;
        }
    }

    private static Color SampleRegion(Small s, Rect f)
    {
        int x0 = Math.Clamp((int)Math.Floor(f.X * s.W), 0, s.W - 1);
        int y0 = Math.Clamp((int)Math.Floor(f.Y * s.H), 0, s.H - 1);
        int x1 = Math.Clamp((int)Math.Ceiling((f.X + f.Width) * s.W), x0 + 1, s.W);
        int y1 = Math.Clamp((int)Math.Ceiling((f.Y + f.Height) * s.H), y0 + 1, s.H);

        long r = 0, g = 0, b = 0;
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = (y * s.W + x) * 4;
                b += s.Px[i];
                g += s.Px[i + 1];
                r += s.Px[i + 2];
                count++;
            }
        }

        if (count == 0) return s.Avg;
        return Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0.0, 1.0) * 255.0);

    private static Color Over(Color top, double alpha, Color bottom)
    {
        alpha = Math.Clamp(alpha, 0.0, 1.0);
        return Color.FromRgb(
            ToByte((top.R * alpha + bottom.R * (1 - alpha)) / 255.0),
            ToByte((top.G * alpha + bottom.G * (1 - alpha)) / 255.0),
            ToByte((top.B * alpha + bottom.B * (1 - alpha)) / 255.0));
    }

    private static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static double ContrastRatio(Color a, Color b)
    {
        double la = Luminance(a) + 0.05;
        double lb = Luminance(b) + 0.05;
        return la > lb ? la / lb : lb / la;
    }

    private static void ToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        double d = max - min;
        if (d < 1e-9)
        {
            h = 0;
            s = 0;
            return;
        }
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6.0;
    }

    private static Color FromHsl(double h, double s, double l)
    {
        if (s < 1e-9)
        {
            byte v = ToByte(l);
            return Color.FromRgb(v, v, v);
        }

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;

        double Channel(double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        return Color.FromRgb(ToByte(Channel(h + 1.0 / 3)), ToByte(Channel(h)), ToByte(Channel(h - 1.0 / 3)));
    }

    private static Color FixColor(Color fg, double alpha, Color bg, double min)
    {
        double whiteC = ContrastRatio(Over(Colors.White, alpha, bg), bg);
        double blackC = ContrastRatio(Over(Colors.Black, alpha, bg), bg);
        bool canLight = whiteC >= min;
        bool canDark = blackC >= min;
        bool preferLight = Luminance(fg) >= Luminance(bg);

        bool light;
        if (canLight && canDark) light = preferLight;
        else if (canLight) light = true;
        else if (canDark) light = false;
        else light = whiteC >= blackC;

        ToHsl(fg, out double h, out double s, out double l);
        double lo = l;
        double hi = light ? 1.0 : 0.0;
        Color best = light ? Colors.White : Colors.Black;

        for (int i = 0; i < 16; i++)
        {
            double mid = (lo + hi) / 2.0;
            var cand = FromHsl(h, s, mid);
            if (ContrastRatio(Over(cand, alpha, bg), bg) >= min)
            {
                best = cand;
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return best;
    }
}