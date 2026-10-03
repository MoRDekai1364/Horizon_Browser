using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Horizon.Stealth.Services;

public sealed class FullscreenGlassCurtain : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const double BlurDownscale = 4.0;
    private const double BlurRadiusSmall = 8.0;
    private const double BlurBleed = 14.0;
    private const double CornerRadiusDip = 8.0;

    private readonly IntPtr _owner;
    private readonly Window _win;
    private readonly Grid _root;
    private IntPtr _hwnd;
    private bool _disposed;
    private bool _rounded;
    private double _opacity;

    private FullscreenGlassCurtain(IntPtr owner, Window win, Grid root, IntPtr hwnd)
    {
        _owner = owner;
        _win = win;
        _root = root;
        _hwnd = hwnd;
    }

    public static FullscreenGlassCurtain? TryCreate(IntPtr owner, string message)
    {
        try
        {
            if (owner == IntPtr.Zero || !IsWindow(owner)) return null;
            if (!GetWindowRect(owner, out var r)) return null;
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w < 8 || h < 8) return null;

            var shot = CaptureScreen(r.Left, r.Top, w, h);
            if (shot == null) return null;

            var blurred = BuildBlurred(shot, w, h);

            var root = new Grid { ClipToBounds = true, Background = Brushes.Black };

            var picture = new System.Windows.Controls.Image { Source = blurred, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            root.Children.Add(picture);

            root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0x1A, 0x1A, 0x1A)) });

            var label = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                TextAlignment = TextAlignment.Center
            };
            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(34, 16, 34, 16),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = label
            };
            root.Children.Add(pill);

            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Opacity = 0.0,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                Width = 100,
                Height = 100,
                Content = root
            };

            var helper = new WindowInteropHelper(win) { Owner = owner };
            var hwnd = helper.EnsureHandle();
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            var curtain = new FullscreenGlassCurtain(owner, win, root, hwnd);
            root.SizeChanged += (_, _) => curtain.UpdateClip();

            win.Show();
            curtain.Move(r, !IsZoomed(owner));
            return curtain;
        }
        catch
        {
            return null;
        }
    }

    public bool OwnerAlive => !_disposed && IsWindow(_owner);

    public Task FadeInAsync(int ms) => AnimateAsync(_opacity, 1.0, ms, EaseOutCubic);

    public Task FadeOutAsync(int ms) => AnimateAsync(_opacity, 0.0, ms, EaseInOutSine);

    public void CoverUnion()
    {
        if (_disposed || !GetWindowRect(_owner, out var w)) return;
        var m = GetMonitorRect();
        Move(new RECT
        {
            Left = Math.Min(w.Left, m.Left),
            Top = Math.Min(w.Top, m.Top),
            Right = Math.Max(w.Right, m.Right),
            Bottom = Math.Max(w.Bottom, m.Bottom)
        }, false);
    }

    public void FitToOwner()
    {
        if (_disposed || !GetWindowRect(_owner, out var w)) return;
        Move(w, !IsZoomed(_owner));
    }

    public void Flush()
    {
        try
        {
            DwmFlush();
        }
        catch
        {
        }
    }

    public async Task WaitForOwnerStableAsync(int minMs, int maxMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool have = false;
        RECT last = default;
        int stable = 0;

        while (sw.ElapsedMilliseconds < maxMs && !_disposed)
        {
            await Task.Delay(8);
            if (!GetWindowRect(_owner, out var r)) break;

            if (have && r.Left == last.Left && r.Top == last.Top && r.Right == last.Right && r.Bottom == last.Bottom) stable++;
            else stable = 0;

            last = r;
            have = true;
            if (stable >= 3 && sw.ElapsedMilliseconds >= minMs) break;
        }
    }

    private static BitmapSource? CaptureScreen(int x, int y, int w, int h)
    {
        IntPtr screen = IntPtr.Zero;
        IntPtr mem = IntPtr.Zero;
        IntPtr bmp = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return null;
            mem = CreateCompatibleDC(screen);
            if (mem == IntPtr.Zero) return null;
            bmp = CreateCompatibleBitmap(screen, w, h);
            if (bmp == IntPtr.Zero) return null;
            old = SelectObject(mem, bmp);
            if (!BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY | CAPTUREBLT)) return null;
            SelectObject(mem, old);
            old = IntPtr.Zero;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (old != IntPtr.Zero && mem != IntPtr.Zero) SelectObject(mem, old);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static BitmapSource BuildBlurred(BitmapSource shot, int pxW, int pxH)
    {
        int sw = Math.Max(64, (int)Math.Round(pxW / BlurDownscale));
        int sh = Math.Max(64, (int)Math.Round(pxH / BlurDownscale));

        var picture = new System.Windows.Controls.Image
        {
            Source = shot,
            Stretch = Stretch.Fill,
            Margin = new Thickness(-BlurBleed),
            Effect = new BlurEffect
            {
                Radius = BlurRadiusSmall,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            }
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.Linear);

        var host = new Grid { Width = sw, Height = sh, ClipToBounds = true, Background = Brushes.Black };
        host.Children.Add(picture);
        host.Measure(new System.Windows.Size(sw, sh));
        host.Arrange(new Rect(0, 0, sw, sh));
        host.UpdateLayout();

        var target = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
        target.Render(host);
        target.Freeze();
        return target;
    }

    private void UpdateClip()
    {
        if (_disposed) return;
        if (_rounded && _root.ActualWidth > 0 && _root.ActualHeight > 0)
            _root.Clip = new RectangleGeometry(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight), CornerRadiusDip, CornerRadiusDip);
        else
            _root.Clip = null;
    }

    private RECT GetMonitorRect()
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        var mon = MonitorFromWindow(_owner, MONITOR_DEFAULTTONEAREST);
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info)) return info.rcMonitor;
        GetWindowRect(_owner, out var w);
        return w;
    }

    private void Move(RECT r, bool rounded)
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        _rounded = rounded;
        SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        UpdateClip();
    }

    private void SetOpacity(double value)
    {
        if (_disposed) return;
        _opacity = Math.Clamp(value, 0.0, 1.0);
        _win.Opacity = _opacity;
    }

    private async Task AnimateAsync(double from, double to, int ms, Func<double, double> ease)
    {
        if (_disposed) return;
        if (ms <= 0)
        {
            SetOpacity(to);
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / ms);
            SetOpacity(from + (to - from) * ease(t));
            if (t >= 1.0)
            {
                CompositionTarget.Rendering -= handler!;
                tcs.TrySetResult(true);
            }
        };

        SetOpacity(from);
        CompositionTarget.Rendering += handler;
        await Task.WhenAny(tcs.Task, Task.Delay(ms + 300));
        CompositionTarget.Rendering -= handler;
        SetOpacity(to);
    }

    private static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - t, 3.0);

    private static double EaseInOutSine(double t) => -(Math.Cos(Math.PI * t) - 1.0) / 2.0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _win.Close();
        }
        catch
        {
        }
        _hwnd = IntPtr.Zero;
    }
}
