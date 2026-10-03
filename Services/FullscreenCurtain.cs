using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Horizon.Stealth.Services;

public sealed class FullscreenCurtain : IDisposable
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string? windowName, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SS_BLACKRECT = 0x00000004;
    private const uint LWA_ALPHA = 0x2;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private readonly IntPtr _owner;
    private IntPtr _hwnd;
    private bool _disposed;
    private int _alpha;

    private FullscreenCurtain(IntPtr owner, IntPtr hwnd)
    {
        _owner = owner;
        _hwnd = hwnd;
    }

    public static FullscreenCurtain? TryCreate(IntPtr owner)
    {
        try
        {
            if (owner == IntPtr.Zero || !IsWindow(owner)) return null;
            if (!GetWindowRect(owner, out var r)) return null;
            if (r.Right - r.Left < 8 || r.Bottom - r.Top < 8) return null;

            int style = unchecked((int)0x80000000) | SS_BLACKRECT;
            int ex = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            var hwnd = CreateWindowExW(ex, "Static", null, style, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return null;

            var curtain = new FullscreenCurtain(owner, hwnd);
            curtain.SetAlpha(0);
            curtain.ApplyRounding(!IsZoomed(owner));
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            UpdateWindow(hwnd);
            return curtain;
        }
        catch
        {
            return null;
        }
    }

    public bool OwnerAlive => !_disposed && IsWindow(_owner);

    public Task FadeInAsync(int ms) => AnimateAsync(_alpha, 255, ms, EaseOutCubic);

    public Task FadeOutAsync(int ms) => AnimateAsync(_alpha, 0, ms, EaseInOutSine);

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
        var sw = Stopwatch.StartNew();
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
        SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        ApplyRounding(rounded);
        UpdateWindow(_hwnd);
    }

    private void ApplyRounding(bool rounded)
    {
        try
        {
            int pref = rounded ? 2 : 1;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
        }
    }

    private void SetAlpha(int a)
    {
        if (_disposed) return;
        _alpha = Math.Clamp(a, 0, 255);
        SetLayeredWindowAttributes(_hwnd, 0, (byte)_alpha, LWA_ALPHA);
    }

    private async Task AnimateAsync(int from, int to, int ms, Func<double, double> ease)
    {
        if (_disposed) return;
        if (ms <= 0)
        {
            SetAlpha(to);
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / ms);
            SetAlpha((int)Math.Round(from + (to - from) * ease(t)));
            if (t >= 1.0)
            {
                CompositionTarget.Rendering -= handler!;
                tcs.TrySetResult(true);
            }
        };

        SetAlpha(from);
        CompositionTarget.Rendering += handler;
        await Task.WhenAny(tcs.Task, Task.Delay(ms + 300));
        CompositionTarget.Rendering -= handler;
        SetAlpha(to);
    }

    private static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - t, 3.0);

    private static double EaseInOutSine(double t) => -(Math.Cos(Math.PI * t) - 1.0) / 2.0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        }
        catch
        {
        }
        _hwnd = IntPtr.Zero;
    }
}