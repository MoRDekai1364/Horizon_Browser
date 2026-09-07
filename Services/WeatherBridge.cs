using System;

namespace Horizon.Stealth.Services;

public static class WeatherBridge
{
    public static string WeatherText { get; private set; } = "⛅ N/A";
    public static int    WmoCode     { get; private set; } = -1;
    public static string City        { get; private set; } = "";

    public static event Action? Updated;

    public static void Publish(string weatherText, int wmoCode, string city)
    {
        WeatherText = weatherText;
        WmoCode     = wmoCode;
        City        = city;
        Updated?.Invoke();
    }
}