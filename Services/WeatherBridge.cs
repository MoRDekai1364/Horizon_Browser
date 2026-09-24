using System;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace Horizon.Stealth.Services;

public static class WeatherBridge
{
    public static string WeatherText { get; private set; } = "⛅ N/A";
    public static int    WmoCode     { get; private set; } = -1;
    public static string City        { get; private set; } = "";

    public static event Action? Updated;

    public static System.Windows.Media.Imaging.BitmapSource? ThemeWallpaper { get; private set; }
    public static System.Windows.FrameworkElement? WallpaperSurface { get; private set; }
    public static bool       HasTheme             { get; private set; }
    public static bool       ThemeDarkWallpaper   { get; private set; } = true;
    public static bool       ThemeAdaptive        { get; private set; } = true;
    public static MediaColor ThemeAverage         { get; private set; } = MediaColor.FromRgb(0x28, 0x28, 0x28);
    public static MediaColor ThemePill            { get; private set; } = MediaColor.FromRgb(0x2E, 0x6A, 0xA0);
    public static MediaColor ThemeText            { get; private set; } = MediaColors.White;
    public static MediaColor ThemeAccent          { get; private set; } = MediaColor.FromRgb(0xCC, 0xCC, 0xCC);
    public static MediaColor ThemeAccentSecondary { get; private set; } = MediaColor.FromRgb(0x95, 0x95, 0x95);
    public static MediaColor ThemeAccentSubtle    { get; private set; } = MediaColor.FromRgb(0xA0, 0xA0, 0xA0);

    public static event Action? ThemeUpdated;
    public static event Action? SurfaceChanged;

    public static void SetWallpaper(System.Windows.Media.Imaging.BitmapSource? wallpaper)
    {
        ThemeWallpaper = wallpaper;
    }

    public static void SetWallpaperSurface(System.Windows.FrameworkElement? surface)
    {
        WallpaperSurface = surface;
        SurfaceChanged?.Invoke();
    }

    public static void PublishTheme(MediaColor average, MediaColor pill, MediaColor text, MediaColor accent,
        MediaColor accentSecondary, MediaColor accentSubtle, bool darkWallpaper, bool adaptive)
    {
        ThemeAverage         = average;
        ThemePill            = pill;
        ThemeText            = text;
        ThemeAccent          = accent;
        ThemeAccentSecondary = accentSecondary;
        ThemeAccentSubtle    = accentSubtle;
        ThemeDarkWallpaper   = darkWallpaper;
        ThemeAdaptive        = adaptive;
        HasTheme             = true;
        LogService.Write("WeatherTheme", $"published avg={average} pill={pill} dark={darkWallpaper} adaptive={adaptive} wallpaper={(ThemeWallpaper != null)}");
        ThemeUpdated?.Invoke();
    }

    public static void ClearTheme()
    {
        ThemeWallpaper       = null;
        ThemeAverage         = MediaColor.FromRgb(0x28, 0x28, 0x28);
        ThemePill            = MediaColor.FromRgb(0x2E, 0x6A, 0xA0);
        ThemeText            = MediaColors.White;
        ThemeAccent          = MediaColor.FromRgb(0xCC, 0xCC, 0xCC);
        ThemeAccentSecondary = MediaColor.FromRgb(0x95, 0x95, 0x95);
        ThemeAccentSubtle    = MediaColor.FromRgb(0xA0, 0xA0, 0xA0);
        ThemeDarkWallpaper   = true;
        ThemeAdaptive        = false;
        HasTheme             = true;
        LogService.Write("WeatherTheme", "cleared (no wallpaper)");
        ThemeUpdated?.Invoke();
    }

    public static void Publish(string weatherText, int wmoCode, string city)
    {
        WeatherText = weatherText;
        WmoCode     = wmoCode;
        City        = city;
        Updated?.Invoke();
    }
}