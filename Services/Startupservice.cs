using System;
using Microsoft.Win32;

namespace Horizon.Stealth.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HorizonBrowser";
    private const string LaunchArg = "--tray-start";

    public static void Apply(bool enabled)
    {
        try
        {
            if (enabled)
                Enable();
            else
                Disable();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StartupService.Apply");
        }
    }

    private static void Enable()
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        var command = $"\"{exePath}\" {LaunchArg}";

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.SetValue(ValueName, command);
        LogService.Write("SETTINGS", $"StartupService: enabled ({command})");
    }

    private static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        LogService.Write("SETTINGS", "StartupService: disabled");
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StartupService.IsEnabled");
            return false;
        }
    }
}