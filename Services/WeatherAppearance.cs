using System.Windows;
using System.Windows.Media;

namespace Horizon.Stealth.Services;

public static class WeatherAppearance
{
    public static SolidColorBrush TextBrush { get; } = new();
    public static SolidColorBrush MutedBrush { get; } = new();
    public static SolidColorBrush AccentBrush { get; } = new();
    public static SolidColorBrush CardBrush { get; } = new();
    public static SolidColorBrush BorderBrush { get; } = new();
    public static SolidColorBrush InputBrush { get; } = new();
    public static SolidColorBrush HighlightBrush { get; } = new();
    public static SolidColorBrush HoverBrush { get; } = new();
    private static readonly Dictionary<Color, SolidColorBrush> SemanticBrushes = new();
    public static bool IsDark => WeatherBridge.ThemeDarkWallpaper;
    public static Color PanelColor => Blend(WeatherBridge.ThemeAverage, IsDark ? Colors.Black : Colors.White, 0.82);

    static WeatherAppearance()
    {
        Refresh();
        WeatherBridge.ThemeUpdated += Refresh;
    }

    private static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    public static double Contrast(Color a, Color b)
    {
        static double Channel(byte value)
        {
            double s = value / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static Color Readable(Color color)
    {
        Color background = Blend(PanelColor, IsDark ? Colors.White : Colors.Black, 0.12);
        Color target = IsDark ? Colors.White : Colors.Black;
        for (int i = 0; i <= 20; i++)
        {
            Color candidate = Blend(color, target, i / 20.0);
            if (Contrast(candidate, background) >= 4.5) return candidate;
        }
        return target;
    }

    public static SolidColorBrush SemanticBrush(Color color)
    {
        if (!SemanticBrushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(Readable(color));
            SemanticBrushes[color] = brush;
        }
        return brush;
    }

    private static void Refresh()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(new Action(Refresh)); return; }
        TextBrush.Color = IsDark ? Color.FromRgb(0xF3, 0xF5, 0xF7) : Color.FromRgb(0x17, 0x19, 0x1F);
        MutedBrush.Color = IsDark ? Color.FromRgb(0xBC, 0xC5, 0xD0) : Color.FromRgb(0x40, 0x47, 0x52);
        AccentBrush.Color = Readable(WeatherBridge.ThemeAccent);
        CardBrush.Color = IsDark ? Color.FromArgb(0x48, 0, 0, 0) : Color.FromArgb(0x80, 255, 255, 255);
        BorderBrush.Color = IsDark ? Color.FromArgb(0x40, 255, 255, 255) : Color.FromArgb(0x40, 0, 0, 0);
        InputBrush.Color = IsDark ? Color.FromArgb(0xB4, 0x10, 0x10, 0x10) : Color.FromArgb(0xD0, 255, 255, 255);
        HighlightBrush.Color = Blend(WeatherBridge.ThemeAccent, PanelColor, 0.8);
        HoverBrush.Color = IsDark ? Color.FromArgb(0x24, 255, 255, 255) : Color.FromArgb(0x18, 0, 0, 0);
        foreach (var pair in SemanticBrushes) pair.Value.Color = Readable(pair.Key);
    }
}
