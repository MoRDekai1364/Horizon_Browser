using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class ArcCommandBarWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GlassMargins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", EntryPoint = "DwmExtendFrameIntoClientArea")]
    private static extern int DwmExtendFrameRaw(IntPtr hwnd, ref GlassMargins margins);

    private void AttachNativeBlur()
    {
        if (SettingsService.Current.DisableBlurEffects)
        {
            CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x12, 0x12));
            return;
        }
        CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x0E, 0x0E));
        SourceInitialized += (_, _) => EnableNativeBlur();
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
            var accent = new IslandAccentPolicy
            {
                AccentState = 3,
                AccentFlags = 0,
                GradientColor = 0,
                AnimationId = 0
            };
            int size = Marshal.SizeOf(accent);
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new IslandCompositionData
            {
                Attribute = 19,
                Data = ptr,
                SizeOfData = size
            };
            int result = SetWindowCompositionAttribute(hwnd, ref data);
            if (result == 0)
            {
                CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x12, 0x12));
            }
        }
        catch (Exception ex)
        {
            LogService.Debug("glass", () => $"native blur failed: {ex.Message}");
            CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x12, 0x12));
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
        }
    }
}