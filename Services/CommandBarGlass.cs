using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Horizon.Stealth.Services;

/// <summary>
/// Gives secondary surfaces (dialogs, widget windows) the Command Bar's blur background
/// whenever the browser is NOT showing the homepage. While the homepage is active the
/// surface keeps its own wallpaper glass. The browser header and sidebar are never touched.
/// </summary>
public static class CommandBarGlass
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    public static bool IsHomeActive()
    {
        try
        {
            return Application.Current?.MainWindow is MainWindow mw && mw.CurrentBrowser?.IsHomeActive == true;
        }
        catch { return false; }
    }

    /// <summary>True when the Command Bar glass should replace the surface's own backdrop.</summary>
    public static bool ShouldUse() => !SettingsService.Current.DisableBlurEffects && !IsHomeActive();

    /// <param name="win">The surface window.</param>
    /// <param name="host">A panel at the bottom of the window's visual tree that receives the tint layer.</param>
    /// <param name="onModeChanged">Called with true when Command Bar glass becomes active (hide own wallpaper glass), false otherwise.</param>
    /// <param name="cornerRadius">Corner radius of the surface.</param>
    /// <returns>Detach action.</returns>
    public static Action Attach(Window win, Panel host, Action<bool>? onModeChanged = null, double cornerRadius = 8)
    {
        // Same tint as the Command Bar card (0x8C0E0E0E).
        var tint = new Border
        {
            CornerRadius = new CornerRadius(cornerRadius),
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x0E, 0x0E)),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        host.Children.Insert(0, tint);

        Brush? originalBackground = win.Background;
        bool? active = null;
        bool fallbackApplied = false;

        void SetBlur(bool on)
        {
            IntPtr hwnd = new WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) return;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                if (on)
                {
                    var src = HwndSource.FromHwnd(hwnd);
                    if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                    if (!win.AllowsTransparency)
                    {
                        var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                        DwmExtendFrameIntoClientArea(hwnd, ref m);
                    }
                    int corner = 2;
                    DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
                }
                var accent = new AccentPolicy { AccentState = on ? 3 : 0 };
                int size = Marshal.SizeOf(accent);
                ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new CompositionData { Attribute = 19, Data = ptr, SizeOfData = size };
                int result = SetWindowCompositionAttribute(hwnd, ref data);
                if (on && result == 0 && !fallbackApplied)
                {
                    fallbackApplied = true;
                    NativeGlassFallbackService.ApplyFallbackCascade(win, hwnd, tint, host);
                }
            }
            catch (Exception ex)
            {
                LogService.Debug("glass", () => $"CommandBarGlass blur failed: {ex.Message}");
            }
            finally
            {
                if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
            }
        }

        void Evaluate()
        {
            bool use = ShouldUse();
            if (active == use) return;
            active = use;
            tint.Visibility = use ? Visibility.Visible : Visibility.Collapsed;
            if (use) win.Background = Brushes.Transparent;
            else if (originalBackground != null) win.Background = originalBackground;
            SetBlur(use);
            onModeChanged?.Invoke(use);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => Evaluate();

        void Start()
        {
            Evaluate();
            timer.Start();
        }

        if (new WindowInteropHelper(win).Handle != IntPtr.Zero) Start();
        else win.SourceInitialized += (_, _) => Start();

        void Detach()
        {
            timer.Stop();
            host.Children.Remove(tint);
        }
        win.Closed += (_, _) => Detach();
        return Detach;
    }
}
