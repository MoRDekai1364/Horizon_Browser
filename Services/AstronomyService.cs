using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Horizon.Stealth.Services;

public sealed record MoonState(double Phase, double Illumination, string Name, string Icon);
public sealed record SolarDay(DateOnly Date, DateTimeOffset? Sunrise, DateTimeOffset? Sunset, double DaylightSeconds);
public sealed record SolarSnapshot(string City, TimeZoneInfo Zone, IReadOnlyList<SolarDay> Days, DateTimeOffset FetchedUtc);
public sealed record StormSnapshot(double Kp, DateTimeOffset ObservedUtc, DateTimeOffset FetchedUtc);
public sealed record SunState(double? Progress, bool IsDay, string Status, string Detail, string Sunrise, string Sunset);

public static class AstronomyService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly SemaphoreSlim SolarGate = new(1, 1);
    private static readonly SemaphoreSlim StormGate = new(1, 1);
    private static string _requestedCity = "";
    private static DateTimeOffset _solarAttempt;
    private static DateTimeOffset _stormAttempt;
    public static SolarSnapshot? Solar { get; private set; }
    public static StormSnapshot? Storm { get; private set; }
    public static event Action? Updated;

    public static string Text(string key, string fallback) => LocalizationService.Instance.GetString(key, fallback);

    public static Task EnsureAsync(string city) => Task.WhenAll(EnsureSolarAsync(city.Trim()), EnsureStormAsync());

    private static void Notify()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) Updated?.Invoke();
        else dispatcher.BeginInvoke(new Action(() => Updated?.Invoke()));
    }

    public static MoonState MoonAt(DateTimeOffset instant)
    {
        const double rad = Math.PI / 180;
        double days = (instant.ToUniversalTime() - new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero)).TotalDays;
        double anomaly = rad * (357.5291 + 0.98560028 * days);
        double solarLongitude = anomaly + rad * (1.9148 * Math.Sin(anomaly) + 0.02 * Math.Sin(2 * anomaly)
            + 0.0003 * Math.Sin(3 * anomaly) + 102.9372) + Math.PI;
        double lunarAnomaly = rad * (134.963 + 13.064993 * days);
        double lunarLongitude = rad * (218.316 + 13.176396 * days + 6.289 * Math.Sin(lunarAnomaly));
        double lunarLatitude = rad * 5.128 * Math.Sin(rad * (93.272 + 13.229350 * days));
        double distance = 385001 - 20905 * Math.Cos(lunarAnomaly);
        double separation = Math.Acos(Math.Clamp(Math.Cos(lunarLatitude) * Math.Cos(lunarLongitude - solarLongitude), -1, 1));
        double incidence = Math.Atan2(149598000 * Math.Sin(separation), distance - 149598000 * Math.Cos(separation));
        double elongation = (lunarLongitude - solarLongitude) % (2 * Math.PI);
        if (elongation < 0) elongation += 2 * Math.PI;
        double phase = 0.5 + (elongation < Math.PI ? -1 : 1) * incidence / (2 * Math.PI);
        double illuminated = Math.Clamp((1 + Math.Cos(incidence)) / 2, 0, 1);
        string[] names = { "New moon", "Waxing crescent", "First quarter", "Waxing gibbous", "Full moon", "Waning gibbous", "Last quarter", "Waning crescent" };
        string[] icons = { "🌑", "🌒", "🌓", "🌔", "🌕", "🌖", "🌗", "🌘" };
        int index = (int)Math.Floor(phase * 8 + 0.5) % 8;
        return new MoonState(phase, illuminated, Text("Astronomy.Moon" + index, names[index]), icons[index]);
    }

    public static SunState SunAt(SolarSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot == null)
            return new(null, false, Text("Astronomy.SunUnavailable", "Sun times unavailable"),
                Text("Astronomy.ChooseCity", "Choose a weather city or check your connection"), "--:--", "--:--");
        var local = TimeZoneInfo.ConvertTime(now, snapshot.Zone);
        var today = snapshot.Days.FirstOrDefault(d => d.Date == DateOnly.FromDateTime(local.DateTime));
        if (today == null)
            return new(null, false, Text("Astronomy.SunOutdated", "Sun times need refreshing"), snapshot.City, "--:--", "--:--");
        string TimeLabel(DateTimeOffset? value) => value.HasValue
            ? TimeZoneInfo.ConvertTime(value.Value, snapshot.Zone).ToString("t", CultureInfo.CurrentCulture) : "--:--";
        string sunrise = TimeLabel(today.Sunrise), sunset = TimeLabel(today.Sunset);
        if (today.Sunrise == null && today.Sunset == null)
        {
            if (today.DaylightSeconds >= 86300)
                return new(1, true, Text("Astronomy.PolarDay", "Polar day"), Text("Astronomy.NoSunset", "Sun stays above the horizon today"), sunrise, sunset);
            if (today.DaylightSeconds <= 0)
                return new(0, false, Text("Astronomy.PolarNight", "Polar night"), Text("Astronomy.NoSunrise", "Sun stays below the horizon today"), sunrise, sunset);
            return new(null, false, Text("Astronomy.NoEvents", "No sunrise or sunset today"), snapshot.City, sunrise, sunset);
        }
        double? progress = null;
        bool isDay = false;
        if (today.Sunrise is { } rise && today.Sunset is { } set && set > rise)
        {
            progress = Math.Clamp((now - rise).TotalSeconds / (set - rise).TotalSeconds, 0, 1);
            isDay = now >= rise && now < set;
        }
        else if (today.Sunrise is { } onlyRise && today.Sunset == null) isDay = now >= onlyRise;
        else if (today.Sunset is { } onlySet && today.Sunrise == null) isDay = now < onlySet;
        var next = snapshot.Days.SelectMany(d => new[] { (At: d.Sunrise, Rise: true), (At: d.Sunset, Rise: false) })
            .Where(e => e.At.HasValue && e.At.Value > now).OrderBy(e => e.At).FirstOrDefault();
        string detail = snapshot.City;
        if (next.At.HasValue)
        {
            var remaining = next.At.Value - now;
            detail = string.Format(CultureInfo.CurrentCulture, Text("Astronomy.NextEvent", "{0} in {1}h {2}m"),
                next.Rise ? Text("Astronomy.Sunrise", "Sunrise") : Text("Astronomy.Sunset", "Sunset"),
                (int)remaining.TotalHours, remaining.Minutes);
        }
        if (now - snapshot.FetchedUtc > TimeSpan.FromHours(24)) detail += " · " + Text("Astronomy.Cached", "cached");
        return new(progress, isDay, isDay ? Text("Astronomy.Daylight", "Daylight") : Text("Astronomy.Night", "Night"), detail, sunrise, sunset);
    }

    public static string StormLevel(double kp) => kp switch
    {
        >= 9 => "G5 · " + Text("Astronomy.Extreme", "Extreme"),
        >= 8 => "G4 · " + Text("Astronomy.Severe", "Severe"),
        >= 7 => "G3 · " + Text("Astronomy.Strong", "Strong"),
        >= 6 => "G2 · " + Text("Astronomy.Moderate", "Moderate"),
        >= 5 => "G1 · " + Text("Astronomy.Minor", "Minor"),
        _ => Text("Astronomy.NoStorm", "No geomagnetic storm")
    };

    public static string StormText(DateTimeOffset now)
    {
        var storm = Storm;
        if (storm == null) return Text("Astronomy.StormUnavailable", "Magnetic storm data unavailable");
        bool stale = now - storm.ObservedUtc > TimeSpan.FromHours(6);
        return $"Kp {storm.Kp:F2} · {StormLevel(storm.Kp)}" + (stale ? " · " + Text("Astronomy.Stale", "stale") : "");
    }

    public static TimeZoneInfo ResolveZone(string id)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone)) return zone;
        throw new TimeZoneNotFoundException(id);
    }

    public static SolarSnapshot ParseSolar(string json, string city, DateTimeOffset fetchedUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var zone = ResolveZone(root.GetProperty("timezone").GetString() ?? "UTC");
        var daily = root.GetProperty("daily");
        var days = new List<SolarDay>();
        DateTimeOffset? Event(JsonElement element) => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long epoch) && epoch > 0
            ? DateTimeOffset.FromUnixTimeSeconds(epoch) : null;
        for (int i = 0; i < daily.GetProperty("time").GetArrayLength(); i++)
        {
            var day = DateTimeOffset.FromUnixTimeSeconds(daily.GetProperty("time")[i].GetInt64());
            double length = daily.GetProperty("daylight_duration")[i].ValueKind == JsonValueKind.Number
                ? daily.GetProperty("daylight_duration")[i].GetDouble() : double.NaN;
            days.Add(new(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(day, zone).DateTime),
                Event(daily.GetProperty("sunrise")[i]), Event(daily.GetProperty("sunset")[i]), length));
        }
        return new(city, zone, days, fetchedUtc);
    }

    public static StormSnapshot ParseStorm(string json, DateTimeOffset fetchedUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var values = new List<StormSnapshot>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("Kp", out var kpElement)
                || kpElement.ValueKind != JsonValueKind.Number || !kpElement.TryGetDouble(out double kp) || kp < 0 || kp > 9) continue;
            if (!row.TryGetProperty("time_tag", out var timeElement)
                || timeElement.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(timeElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) || time > fetchedUtc) continue;
            values.Add(new(kp, time, fetchedUtc));
        }
        return values.OrderByDescending(v => v.ObservedUtc).FirstOrDefault() ?? throw new FormatException("No valid NOAA Kp observations.");
    }

    private static async Task EnsureSolarAsync(string city)
    {
        if (!string.Equals(city, SettingsService.Current.WidgetWeatherCity.Trim(), StringComparison.OrdinalIgnoreCase)) return;
        bool changed = !string.Equals(city, _requestedCity, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            _requestedCity = city;
            Solar = null;
            _solarAttempt = default;
            Notify();
        }
        if (!SolarGate.Wait(0)) return;
        try
        {
            if (city.Length == 0) return;
            var now = DateTimeOffset.UtcNow;
            if (now - _solarAttempt < TimeSpan.FromMinutes(Solar == null ? 2 : 30)) return;
            _solarAttempt = now;
            string geoJson = await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json");
            using var geoDoc = JsonDocument.Parse(geoJson);
            var loc = geoDoc.RootElement.GetProperty("results")[0];
            string lat = loc.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            string lon = loc.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            string json = await Http.GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&daily=sunrise,sunset,daylight_duration&timezone=auto&timeformat=unixtime&forecast_days=3");
            if (!string.Equals(city, SettingsService.Current.WidgetWeatherCity.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            Solar = ParseSolar(json, loc.GetProperty("name").GetString() ?? city, DateTimeOffset.UtcNow);
            Notify();
        }
        catch (Exception ex) { LogService.Write("Astronomy", "Sun times: " + ex.Message); }
        finally
        {
            SolarGate.Release();
            if (!string.Equals(city, _requestedCity, StringComparison.OrdinalIgnoreCase)) _ = EnsureSolarAsync(_requestedCity);
        }
    }

    private static async Task EnsureStormAsync()
    {
        if (!StormGate.Wait(0)) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _stormAttempt < TimeSpan.FromMinutes(Storm == null ? 2 : 10)) return;
            _stormAttempt = now;
            string json = await Http.GetStringAsync("https://services.swpc.noaa.gov/products/noaa-planetary-k-index.json");
            Storm = ParseStorm(json, DateTimeOffset.UtcNow);
            Notify();
        }
        catch (Exception ex) { LogService.Write("Astronomy", "Geomagnetic activity: " + ex.Message); }
        finally { StormGate.Release(); }
    }
}
