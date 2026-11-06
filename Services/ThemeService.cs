using System.Windows;
using System.Windows.Media;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth.Services;

public static class ThemeService
{
    private static string _appliedTheme = "Horizon";

    public static void Initialize()
    {
        ApplyTheme(SettingsService.Current.Theme);
    }

    public static void ApplyTheme(string themeName)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;

            if (string.IsNullOrWhiteSpace(themeName)) themeName = "Horizon";
            if (string.Equals(_appliedTheme, themeName, StringComparison.OrdinalIgnoreCase)) return;

            ResourceDictionary dict;
            try
            {
                dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{themeName}.xaml") };
            }
            catch
            {
                LogService.Write("THEME-ERR", $"Failed to load {themeName}. Falling back.");
                return;
            }

            var merged = app.Resources.MergedDictionaries;
            if (merged.Count > 0)
            {
                merged[0] = dict;
            }
            else
            {
                merged.Add(dict);
            }

            _appliedTheme = themeName;

            if (app.TryFindResource("Brush_TabChipText") is SolidColorBrush chipText)
            {
                TabViewModel.SetBaseTitleColor(chipText.Color);
            }

            LogService.Write("THEME", $"Applied visual style: {themeName}");
            ContrastGuard.OnThemeChanged();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ThemeService.ApplyTheme");
        }
    }
}