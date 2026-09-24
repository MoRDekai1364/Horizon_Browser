using System;
using System.Windows.Media;

namespace Horizon.Stealth.Services;

public static class TintService
{
    public static byte Apply(byte baseAlpha)
    {
        double fraction = Math.Clamp(SettingsService.Current.HomeTintStrength / 100.0, 0.0, 1.0);
        return (byte)Math.Round(baseAlpha * fraction);
    }

    public static Color Apply(Color baseColor)
    {
        return Color.FromArgb(Apply(baseColor.A), baseColor.R, baseColor.G, baseColor.B);
    }
}