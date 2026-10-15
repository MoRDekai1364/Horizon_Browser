using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace Horizon.Stealth.Services;

public class LanguageInfo
{
    public string Code { get; set; } = "en";
    public string DisplayName { get; set; } = "English";
    public string EnglishName { get; set; } = "English";

    public override string ToString() => $"{DisplayName} ({Code})";
}

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    private readonly Dictionary<string, string> _currentTranslations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _englishFallback = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LanguageInfo> _availableLanguages = new();

    public string CurrentLanguage { get; private set; } = "en";
    public IReadOnlyList<LanguageInfo> AvailableLanguages => _availableLanguages;

    public string this[string key] => GetString(key);

    public LocalizationService()
    {
        InitializeLanguages();
    }

    private void InitializeLanguages()
    {
        ScanAvailableLanguages();

        // Always load English as the master fallback dictionary
        LoadDictionaryInto("en", _englishFallback);

        // Determine starting language from settings or OS culture
        string desiredLang = SettingsService.Current.AppLanguage;
        if (string.IsNullOrWhiteSpace(desiredLang))
        {
            string osLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            desiredLang = _availableLanguages.Any(l => l.Code.Equals(osLang, StringComparison.OrdinalIgnoreCase))
                ? osLang
                : "en"; // Default to English
        }

        SetLanguage(desiredLang, persistSetting: false);
    }

    public void ScanAvailableLanguages()
    {
        _availableLanguages.Clear();

        var knownDefaults = new List<LanguageInfo>
        {
            new() { Code = "en", DisplayName = "English", EnglishName = "English" },
            new() { Code = "bg", DisplayName = "Български", EnglishName = "Bulgarian" },
            new() { Code = "de", DisplayName = "Deutsch", EnglishName = "German" },
            new() { Code = "it", DisplayName = "Italiano", EnglishName = "Italian" },
            new() { Code = "fr", DisplayName = "Français", EnglishName = "French" },
            new() { Code = "ru", DisplayName = "Русский", EnglishName = "Russian" },
            new() { Code = "es", DisplayName = "Español", EnglishName = "Spanish" }
        };

        var scannedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Scan filesystem folders for custom or drop-in language JSON files
        string[] searchDirs = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Horizon_Browser", "Languages")
        };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    string code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

                    string displayName = code;
                    string englishName = code;

                    if (doc.RootElement.TryGetProperty("_meta", out var meta))
                    {
                        if (meta.TryGetProperty("code", out var c)) code = c.GetString() ?? code;
                        if (meta.TryGetProperty("displayName", out var d)) displayName = d.GetString() ?? displayName;
                        if (meta.TryGetProperty("englishName", out var e)) englishName = e.GetString() ?? englishName;
                    }
                    else
                    {
                        var match = knownDefaults.FirstOrDefault(k => k.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            displayName = match.DisplayName;
                            englishName = match.EnglishName;
                        }
                    }

                    if (scannedCodes.Add(code))
                    {
                        _availableLanguages.Add(new LanguageInfo
                        {
                            Code = code,
                            DisplayName = displayName,
                            EnglishName = englishName
                        });
                    }
                }
                catch { }
            }
        }

        // Add any missing built-in known defaults
        foreach (var def in knownDefaults)
        {
            if (scannedCodes.Add(def.Code))
            {
                _availableLanguages.Add(def);
            }
        }
    }

    public void SetLanguage(string languageCode, bool persistSetting = true)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) languageCode = "en";
        languageCode = languageCode.ToLowerInvariant();

        CurrentLanguage = languageCode;
        _currentTranslations.Clear();
        LoadDictionaryInto(languageCode, _currentTranslations);

        if (persistSetting)
        {
            try
            {
                SettingsService.Current.AppLanguage = languageCode;
                SettingsService.Save();
            }
            catch { }
        }

        // Notify WPF binding engine to re-evaluate all indexer bindings [{Key}]
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        LanguageChanged?.Invoke();
    }

    public string GetString(string key, string? fallback = null)
    {
        if (string.IsNullOrEmpty(key)) return fallback ?? string.Empty;

        // 1. Try active language dictionary
        if (_currentTranslations.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
            return val;

        // 2. Try English fallback dictionary
        if (_englishFallback.TryGetValue(key, out var enVal) && !string.IsNullOrWhiteSpace(enVal))
            return enVal;

        // 3. Fallback to key or specified fallback string
        return fallback ?? key;
    }

    public static string Get(string key, string? fallback = null) => Instance.GetString(key, fallback);

    public static string Format(string key, params object[] args)
    {
        string pattern = Instance.GetString(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    private void LoadDictionaryInto(string code, Dictionary<string, string> target)
    {
        string[] searchPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Horizon_Browser", "Languages", $"{code}.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", $"{code}.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Languages", $"{code}.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Languages", $"{code}.json"),
            Path.Combine(Environment.CurrentDirectory, "Languages", $"{code}.json")
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    FlattenJson(json, target);
                    if (target.Count > 0) return;
                }
                catch (Exception ex)
                {
                    LogService.RecordCrash(ex, $"LoadDictionaryInto({code}) from {path}");
                }
            }
        }

        // Fallback to assembly embedded resource
        try
        {
            var asm = typeof(LocalizationService).Assembly;
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($".Languages.{code}.json", StringComparison.OrdinalIgnoreCase)
                                  || n.EndsWith($"Languages.{code}.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    string json = reader.ReadToEnd();
                    FlattenJson(json, target);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, $"LoadDictionaryInto({code}) from embedded resource");
        }
    }

    private static void FlattenJson(string json, Dictionary<string, string> target)
    {
        using var doc = JsonDocument.Parse(json);
        FlattenElement(string.Empty, doc.RootElement, target);
    }

    private static void FlattenElement(string prefix, JsonElement element, Dictionary<string, string> target)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name.StartsWith("_")) continue; // Skip metadata like _meta

                string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                FlattenElement(key, prop.Value, target);

                // Also register simple property name for convenience
                if (!target.ContainsKey(prop.Name))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        target[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            target[prefix] = element.GetString() ?? string.Empty;
        }
    }
}

/// <summary>
/// XAML markup extension allowing direct dynamic binding: Text="{services:Loc 'Common.Close'}"
/// Automatically updates in real-time when the active application language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(Binding))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;
    public string? Fallback { get; set; }

    public LocExtension() { }
    public LocExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = Fallback ?? Key
        };
        return binding.ProvideValue(serviceProvider);
    }
}
