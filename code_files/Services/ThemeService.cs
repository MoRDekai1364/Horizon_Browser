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

        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
            NotifySidebarColorModeChanged(SettingsService.Current.SidebarColorMode);
        };
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

    // Keys whose Color is mutated in place for Dark/Light/System. Because every
    // element in the tree references the SAME SolidColorBrush instance via
    // StaticResource, changing .Color here updates them all immediately —
    // no re-merge, no restart, works for live updates.
    private static readonly string[] ColorModeKeys =
    {
        "Brush_Background", "Brush_DeepBackground", "Brush_Text", "Brush_SoftText", "Brush_Alert",
        "Brush_TabStripBg", "Brush_TabStripBgSelected", "Brush_TabStripBorder",
        "Brush_TabStripTextInactive", "Brush_TabStripTextHover",
        "Brush_ButtonBorder", "Brush_ButtonHoverBg", "Brush_ButtonHoverBorder", "Brush_ButtonPressedBg"
    };

    private static ResourceDictionary? _lightPalette;
    private static ResourceDictionary? _darkPalette;

    public static void ApplyColorMode(FrameworkElement target, string mode)
    {
        try
        {
            if (target == null) return;

            string effective = ResolveEffectiveMode(mode);

            _lightPalette ??= new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Light.xaml") };
            _darkPalette  ??= new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Horizon.xaml") };

            var palette = effective == "Light" ? _lightPalette : _darkPalette;

            foreach (var key in ColorModeKeys)
            {
                if (palette[key] is not SolidColorBrush sourceBrush) continue;
                if (target.TryFindResource(key) is not SolidColorBrush liveBrush) continue;
                if (liveBrush.IsFrozen) continue;

                liveBrush.Color = sourceBrush.Color;
            }

            LogService.Write("THEME", $"Applied color mode '{mode}' (resolved: {effective}) to {target.GetType().Name}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ThemeService.ApplyColorMode");
        }
    }

    // Fired when the sidebar's color mode changes so any live FluxSidebar
    // instance can re-skin itself immediately without a restart.
    public static event Action<string>? SidebarColorModeChanged;

    public static void NotifySidebarColorModeChanged(string mode)
        => SidebarColorModeChanged?.Invoke(mode);

    private static string ResolveEffectiveMode(string mode)
    {
        if (mode != "System") return mode;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int useLight)
                return useLight == 1 ? "Light" : "Dark";
        }
        catch { }

        return "Dark";
    }
}