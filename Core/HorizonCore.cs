using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Horizon.Stealth.Core;

/// <summary>
/// Managed bridge to the native Rust engine (horizon_core.dll).
/// Provides high-performance SIMD/native implementations of heavy operations:
/// - Sub-millisecond color palette extraction &amp; quantization
/// - Native WCAG contrast ratio calculation
/// - Fast AdBlock / URL filter rule matching
/// - Direct OS memory pressure diagnostics
/// </summary>
public static class HorizonCore
{
    private const string DllName = "horizon_core.dll";
    private static readonly bool _isAvailable;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeSystemMemoryInfo
    {
        public ulong TotalPhysMb;
        public ulong AvailPhysMb;
        public uint MemoryLoadPct;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeBrowserMemoryMetrics
    {
        public uint MainPid;
        public ulong MainProcessRamBytes;
        public uint RenderProcessesCount;
        public ulong RenderProcessesRamBytes;
        public ulong TotalBrowserRamBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeLuminanceMetrics
    {
        public float MeanLuminance;
        public float MinLuminance;
        public float MaxLuminance;
        public uint RecommendedTextColor; // 0 = Light, 1 = Dark
        public float ContrastRatioAgainstWhite;
        public float ContrastRatioAgainstBlack;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeTextFitResult
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool FitsWithoutClipping;
        public float OptimalFontSize;
        public int TruncateCharIndex;
    }

    public record BrowserProcessMemoryReport(
        uint MainPid,
        long MainProcessRamMb,
        int RenderProcessesCount,
        long RenderProcessesRamMb,
        long TotalBrowserRamMb);

    public record RegionLuminanceReport(
        float MeanLuminance,
        float MinLuminance,
        float MaxLuminance,
        bool PreferDarkText,
        float ContrastAgainstWhite,
        float ContrastAgainstBlack);

    public record TextFitReport(
        bool FitsWithoutClipping,
        double OptimalFontSize,
        int TruncateCharIndex);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "horizon_core_version")]
    private static extern int NativeVersion();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "horizon_calculate_contrast_ratio")]
    private static extern double NativeCalculateContrastRatio(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "horizon_extract_palette")]
    private static extern int NativeExtractPalette(IntPtr pixelsPtr, UIntPtr pixelBytesLen, IntPtr outColors, UIntPtr maxColors);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "horizon_matches_filter_rule")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NativeMatchesFilterRule([MarshalAs(UnmanagedType.LPStr)] string url, [MarshalAs(UnmanagedType.LPStr)] string rule);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "horizon_get_system_memory")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NativeGetSystemMemory(ref NativeSystemMemoryInfo info);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "horizon_get_browser_process_memory")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NativeGetBrowserProcessMemory(uint mainPid, ref NativeBrowserMemoryMetrics metrics);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "horizon_calculate_region_luminance")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NativeCalculateRegionLuminance(
        IntPtr pixelsPtr, UIntPtr totalBytesLen, uint imgWidth, uint imgHeight, uint stride,
        uint rx, uint ry, uint rw, uint rh, ref NativeLuminanceMetrics metrics);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "horizon_compute_text_fit")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NativeComputeTextFit(
        float availableWidth, uint charCount, float avgCharWidthAt12pt, float minFontSize, float maxFontSize,
        ref NativeTextFitResult result);

    static HorizonCore()
    {
        try
        {
            // Verify that the DLL can be loaded and entry point exists
            int ver = NativeVersion();
            _isAvailable = ver >= 100;
            if (_isAvailable)
            {
                Services.LogService.Write("RUST", $"Native Horizon Core loaded successfully (v{ver / 100.0:F2})");
            }
        }
        catch (Exception ex)
        {
            _isAvailable = false;
            Services.LogService.Write("RUST", $"Native engine not loaded (managed fallback active): {ex.Message}");
        }
    }

    /// <summary>
    /// Gets whether the high-performance native Rust core is loaded.
    /// </summary>
    public static bool IsAvailable => _isAvailable;

    /// <summary>
    /// Fast native WCAG contrast ratio calculation between two colors.
    /// </summary>
    public static double? CalculateContrastRatio(Color c1, Color c2)
    {
        if (!_isAvailable) return null;
        try
        {
            return NativeCalculateContrastRatio(c1.R, c1.G, c1.B, c2.R, c2.G, c2.B);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ultra-fast native color palette extraction from raw BGRA pixel bytes.
    /// </summary>
    public static List<Color>? ExtractPalette(byte[] bgraPixels, int maxColors = 3)
    {
        if (!_isAvailable || bgraPixels == null || bgraPixels.Length < 4 || maxColors <= 0)
            return null;

        try
        {
            uint[] outColors = new uint[maxColors];
            GCHandle pixelsHandle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
            GCHandle outHandle = GCHandle.Alloc(outColors, GCHandleType.Pinned);

            try
            {
                int count = NativeExtractPalette(
                    pixelsHandle.AddrOfPinnedObject(),
                    (UIntPtr)bgraPixels.Length,
                    outHandle.AddrOfPinnedObject(),
                    (UIntPtr)maxColors);

                if (count <= 0) return null;

                var list = new List<Color>(count);
                for (int i = 0; i < count; i++)
                {
                    uint c = outColors[i];
                    byte r = (byte)((c >> 16) & 0xFF);
                    byte g = (byte)((c >> 8) & 0xFF);
                    byte b = (byte)(c & 0xFF);
                    list.Add(Color.FromRgb(r, g, b));
                }
                return list;
            }
            finally
            {
                pixelsHandle.Free();
                outHandle.Free();
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fast native AdBlock / URL filter rule matching.
    /// </summary>
    public static bool MatchesFilterRule(string url, string rule)
    {
        if (!_isAvailable || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(rule))
            return false;

        try
        {
            return NativeMatchesFilterRule(url, rule);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Direct native Win32 physical memory diagnostic.
    /// </summary>
    public static bool GetSystemMemoryInfo(out ulong totalMb, out ulong availMb, out uint loadPct)
    {
        totalMb = 0;
        availMb = 0;
        loadPct = 0;

        if (!_isAvailable) return false;

        try
        {
            var info = new NativeSystemMemoryInfo();
            if (NativeGetSystemMemory(ref info))
            {
                totalMb = info.TotalPhysMb;
                availMb = info.AvailPhysMb;
                loadPct = info.MemoryLoadPct;
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Ultra-fast native memory aggregation for Horizon Browser and child WebView2 processes.
    /// </summary>
    public static BrowserProcessMemoryReport? GetBrowserProcessMemory(uint mainPid = 0)
    {
        if (mainPid == 0)
        {
            mainPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        if (_isAvailable)
        {
            try
            {
                var metrics = new NativeBrowserMemoryMetrics();
                if (NativeGetBrowserProcessMemory(mainPid, ref metrics))
                {
                    return new BrowserProcessMemoryReport(
                        metrics.MainPid,
                        (long)(metrics.MainProcessRamBytes / (1024 * 1024)),
                        (int)metrics.RenderProcessesCount,
                        (long)(metrics.RenderProcessesRamBytes / (1024 * 1024)),
                        (long)(metrics.TotalBrowserRamBytes / (1024 * 1024)));
                }
            }
            catch { }
        }

        // Managed fallback
        try
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            long mainMb = proc.WorkingSet64 / (1024 * 1024);
            return new BrowserProcessMemoryReport(mainPid, mainMb, 0, 0, mainMb);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Computes perceived brightness and optimal text contrast for a sub-region of an image.
    /// </summary>
    public static RegionLuminanceReport? CalculateRegionLuminance(
        byte[] bgraPixels, int width, int height, int stride, int rx, int ry, int rw, int rh)
    {
        if (bgraPixels == null || bgraPixels.Length == 0 || width <= 0 || height <= 0 || rw <= 0 || rh <= 0)
            return null;

        if (_isAvailable)
        {
            try
            {
                GCHandle handle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
                try
                {
                    var m = new NativeLuminanceMetrics();
                    if (NativeCalculateRegionLuminance(
                        handle.AddrOfPinnedObject(),
                        (UIntPtr)bgraPixels.Length,
                        (uint)width,
                        (uint)height,
                        (uint)stride,
                        (uint)Math.Max(0, rx),
                        (uint)Math.Max(0, ry),
                        (uint)rw,
                        (uint)rh,
                        ref m))
                    {
                        return new RegionLuminanceReport(
                            m.MeanLuminance,
                            m.MinLuminance,
                            m.MaxLuminance,
                            m.RecommendedTextColor == 1,
                            m.ContrastRatioAgainstWhite,
                            m.ContrastRatioAgainstBlack);
                    }
                }
                finally
                {
                    handle.Free();
                }
            }
            catch { }
        }

        // Managed fallback (approximate 0.5 default)
        return new RegionLuminanceReport(0.5f, 0.4f, 0.6f, false, 4.5f, 4.5f);
    }

    /// <summary>
    /// Computes ideal font size and truncation point to guarantee text never overflows its container.
    /// </summary>
    public static TextFitReport ComputeTextFit(
        double availableWidth, string text, double minFontSize, double maxFontSize, double avgCharWidthAt12pt = 0.0)
    {
        if (string.IsNullOrEmpty(text) || availableWidth <= 0)
        {
            return new TextFitReport(true, maxFontSize, -1);
        }

        if (_isAvailable)
        {
            try
            {
                var res = new NativeTextFitResult();
                if (NativeComputeTextFit(
                    (float)availableWidth,
                    (uint)text.Length,
                    (float)avgCharWidthAt12pt,
                    (float)minFontSize,
                    (float)maxFontSize,
                    ref res))
                {
                    return new TextFitReport(res.FitsWithoutClipping, res.OptimalFontSize, res.TruncateCharIndex);
                }
            }
            catch { }
        }

        // Managed fallback
        double charW = avgCharWidthAt12pt > 0 ? (avgCharWidthAt12pt / 12.0) : 0.55;
        double needed = text.Length * (maxFontSize * charW);
        if (needed <= availableWidth)
        {
            return new TextFitReport(true, maxFontSize, -1);
        }
        double ideal = Math.Clamp(availableWidth / (text.Length * charW), minFontSize, maxFontSize);
        if (text.Length * (ideal * charW) <= availableWidth)
        {
            return new TextFitReport(true, ideal, -1);
        }
        int maxChars = (int)Math.Max(1, (availableWidth / (minFontSize * charW)) - 3);
        return new TextFitReport(false, minFontSize, Math.Min(text.Length - 1, maxChars));
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeLayoutRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    [DllImport(DllName, EntryPoint = "horizon_compute_homepage_layout", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint NativeComputeHomepageLayout(
        float containerW,
        float containerH,
        uint numItems,
        float itemW,
        float itemH,
        float targetGap,
        [Out] NativeLayoutRect[] outRects,
        uint maxRects);

    /// <summary>
    /// Computes mathematically uniform item coordinates and centered grid spacing
    /// using the native Rust engine with zero WPF layout jitter or rounding drift.
    /// </summary>
    public static NativeLayoutRect[] ComputeHomepageLayout(
        double containerW,
        double containerH,
        int count,
        double itemW,
        double itemH,
        double targetGap)
    {
        if (count <= 0 || containerW <= 0) return Array.Empty<NativeLayoutRect>();
        var results = new NativeLayoutRect[count];
        if (_isAvailable)
        {
            try
            {
                uint placed = NativeComputeHomepageLayout(
                    (float)containerW,
                    (float)containerH,
                    (uint)count,
                    (float)itemW,
                    (float)itemH,
                    (float)targetGap,
                    results,
                    (uint)count);
                if (placed > 0) return results;
            }
            catch { }
        }

        // Managed fallback
        double availableW = Math.Max(containerW, itemW);
        int cols = Math.Max(1, (int)Math.Floor((availableW + targetGap) / (itemW + targetGap)));
        int actualCols = Math.Max(1, Math.Min(cols, count));
        double totalGridW = actualCols * itemW + (actualCols - 1) * targetGap;
        double startX = Math.Max(0, (containerW - totalGridW) / 2.0);

        for (int i = 0; i < count; i++)
        {
            int col = i % actualCols;
            int row = i / actualCols;
            results[i] = new NativeLayoutRect
            {
                X = (float)Math.Round(startX + col * (itemW + targetGap)),
                Y = (float)Math.Round(row * (itemH + targetGap)),
                Width = (float)itemW,
                Height = (float)itemH
            };
        }
        return results;
    }
}
