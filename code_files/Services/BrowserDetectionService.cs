using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace Horizon.Stealth.Services;

public class BrowserInfo
{
    public string Name { get; set; } = "Unknown";
    public string ProcessName { get; set; } = "";
    public string UserDataPath { get; set; } = "";
    public bool IsChromium { get; set; } = false;
}

public static class BrowserDetectionService
{
    public static BrowserInfo DetectDefaultBrowser()
    {
        var info = new BrowserInfo { Name = "Google Chrome", ProcessName = "chrome", IsChromium = true }; 

        try
        {
            const string userChoicePath = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice";
            using var key = Registry.CurrentUser.OpenSubKey(userChoicePath);
            
            if (key != null)
            {
                var progId = key.GetValue("ProgId")?.ToString() ?? "";
                
                if (progId.Contains("ChromeHTML"))
                {
                    info.Name = "Google Chrome";
                    info.ProcessName = "chrome";
                    info.IsChromium = true;
                    info.UserDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data");
                }
                else if (progId.Contains("MSEdgeHTM"))
                {
                    info.Name = "Microsoft Edge";
                    info.ProcessName = "msedge";
                    info.IsChromium = true;
                    info.UserDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data");
                }
                else if (progId.Contains("Brave"))
                {
                    info.Name = "Brave Browser";
                    info.ProcessName = "brave";
                    info.IsChromium = true;
                    info.UserDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BraveSoftware\Brave-Browser\User Data");
                }
                else if (progId.Contains("Opera"))
                {
                    info.Name = "Opera";
                    info.ProcessName = "opera";
                    info.IsChromium = true;
                    info.UserDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Opera Software\Opera Stable");
                }
                else if (progId.Contains("Firefox"))
                {
                    info.Name = "Mozilla Firefox";
                    info.ProcessName = "firefox";
                    info.IsChromium = false;
                    info.UserDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox\Profiles");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BrowserDetection");
        }

        return info;
    }

    public static bool IsBrowserRunning(string processName)
    {
        return Process.GetProcessesByName(processName).Length > 0;
    }
}