using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Horizon.Stealth.Services;

public static class DefaultSettingsService
{
    public const string FileName = "default_settings.txt";

    private static Dictionary<string, string>? _cache;

    private static Dictionary<string, string> Load()
    {
        if (_cache != null) return _cache;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            if (File.Exists(path))
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';') || line.StartsWith('@')) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                        value = value.Substring(1, value.Length - 2);
                    if (key.Length > 0) map[key] = value;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Default Settings Read");
        }

        _cache = map;
        return map;
    }

    public static T Apply<T>(T target) where T : class
    {
        var map = Load();
        if (map.Count == 0) return target;

        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in map)
        {
            if (!props.TryGetValue(kv.Key, out PropertyInfo? pi)) continue;
            try
            {
                if (TryConvert(kv.Value, pi.PropertyType, out object? converted))
                    pi.SetValue(target, converted);
            }
            catch (Exception ex)
            {
                LogService.Write("DefaultSettings", $"Skipped '{kv.Key}': {ex.Message}");
            }
        }

        return target;
    }

    private static bool TryConvert(string text, Type type, out object? result)
    {
        result = null;
        Type target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(string)) { result = text; return true; }

        if (target == typeof(bool))
        {
            switch (text.ToLowerInvariant())
            {
                case "true": case "1": case "yes": case "on": result = true; return true;
                case "false": case "0": case "no": case "off": result = false; return true;
                default: return false;
            }
        }

        if (target == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) { result = i; return true; }
        if (target == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) { result = l; return true; }
        if (target == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { result = d; return true; }

        if (target.IsEnum && Enum.TryParse(target, text, true, out object? e)) { result = e; return true; }

        if (target == typeof(List<string>))
        {
            result = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return true;
        }

        return false;
    }
}