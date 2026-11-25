using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class WebAppManagerWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GlassMargins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlassAccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlassCompositionData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll", EntryPoint = "DwmExtendFrameIntoClientArea")]
    private static extern int DwmExtendFrameRaw(IntPtr hwnd, ref GlassMargins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref GlassCompositionData data);

    private void AttachNativeBlur()
    {
        if (SettingsService.Current.DisableBlurEffects)
        {
            CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x12, 0x12));
            ApplyContrastForeground();
            return;
        }

        CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x0E, 0x0E));
        ApplyContrastForeground();
        SourceInitialized += (_, _) => EnableNativeBlur();
    }

    private void ApplyContrastForeground()
    {
        Color bg = (CardBaseBg.Background as SolidColorBrush)?.Color ?? Colors.Black;
        double lum = (0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B) / 255.0;
        bool isDark = lum < 0.5;

        Resources["Brush_Text"]               = new SolidColorBrush(isDark ? Colors.White : Color.FromRgb(0x11, 0x14, 0x18));
        Resources["Brush_SoftText"]           = new SolidColorBrush(isDark ? Color.FromRgb(0xEB, 0xF1, 0xF7) : Color.FromRgb(0x22, 0x26, 0x2E));
        Resources["Brush_MutedText"]          = new SolidColorBrush(isDark ? Color.FromRgb(0xCC, 0xD8, 0xE6) : Color.FromRgb(0x45, 0x4E, 0x59));
        Resources["Brush_Accent"]             = new SolidColorBrush(isDark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x67, 0xC0));
        Resources["Brush_SurfaceChrome"]      = new SolidColorBrush(isDark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00));
        Resources["Brush_SurfaceChromeStroke"]= new SolidColorBrush(isDark ? Color.FromArgb(0x36, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x2A, 0x00, 0x00, 0x00));
        Resources["Brush_SurfaceField"]       = new SolidColorBrush(isDark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
        Resources["Brush_SurfaceFieldStroke"] = new SolidColorBrush(isDark ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x3A, 0x00, 0x00, 0x00));
    }

    private void EnableNativeBlur()
    {
        IntPtr ptr = IntPtr.Zero;
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;

            var margins = new GlassMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameRaw(hwnd, ref margins);

            int cornerPreference = 2;
            DwmSetWindowAttribute(hwnd, 33, ref cornerPreference, sizeof(int));

            var accent = new GlassAccentPolicy { AccentState = 3, AccentFlags = 0, GradientColor = 0, AnimationId = 0 };
            int size = Marshal.SizeOf(accent);
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);

            var data = new GlassCompositionData { Attribute = 19, Data = ptr, SizeOfData = size };
            if (SetWindowCompositionAttribute(hwnd, ref data) == 0)
                CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x12, 0x12));
        }
        catch (Exception ex)
        {
            LogService.Debug("glass", () => $"web app manager blur failed: {ex.Message}");
            CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x12, 0x12));
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
        }
    }

    private void UpdateCardClip()
    {
        if (CardClipHost.ActualWidth <= 0 || CardClipHost.ActualHeight <= 0) return;
        CardClipHost.Clip = new RectangleGeometry(new Rect(0, 0, CardClipHost.ActualWidth, CardClipHost.ActualHeight), 8, 8);
    }
}
