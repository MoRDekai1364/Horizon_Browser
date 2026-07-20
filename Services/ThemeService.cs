using System.Windows;
using System.Windows.Media;
using System.Linq;
using Microsoft.Win32;

namespace Horizon.Stealth.Services;

public static class ThemeService
{
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

            string uriStr = $"pack://application:,,,/Themes/{themeName}.xaml";
            
            try 
            {
                var dict = new ResourceDictionary { Source = new Uri(uriStr) };
                
                
                app.Resources.MergedDictionaries.Clear();
                app.Resources.MergedDictionaries.Add(dict);
                
                LogService.Write("THEME", $"Applied visual style: {themeName}");
            }
            catch 
            {
                LogService.Write("THEME-ERR", $"Failed to load {themeName}. Falling back.");
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ThemeService.ApplyTheme");
        }
    }

    }