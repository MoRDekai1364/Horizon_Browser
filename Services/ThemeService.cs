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
                
                
                if (app.Resources.MergedDictionaries.Count > 0)
                {
                    app.Resources.MergedDictionaries[0] = dict;
                }
                else
                {
                    app.Resources.MergedDictionaries.Add(dict);
                }
                
                LogService.Write("THEME", $"Applied visual style: {themeName}");
                    ContrastGuard.RequestRefresh();
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