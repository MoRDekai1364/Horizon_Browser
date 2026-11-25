using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Horizon.Stealth.Services;

/// <summary>
/// Registers Horizon Browser with Windows 11 Apps / Default Apps (HKCU, zero elevation)
/// and handles one-click default browser assignment with direct Windows Settings fallback.
/// </summary>
public static class DefaultBrowserService
{
    public const string ProgId = "HorizonBrowser.HTML";
    public const string LegacyProgId = "HorizonStealth.HTML";
    public const string AppDisplayName = "Horizon Browser";

    private const string StartMenuKey = @"Software\Clients\StartMenuInternet\HorizonBrowser";
    private const string CapabilitiesKey = @"Software\Horizon Browser\Capabilities";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HorizonBrowser";

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [ComImport]
    [Guid("4e530b0a-e611-4c77-a3ac-9031d022281b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationAssociationRegistration
    {
        [PreserveSig]
        int QueryCurrentDefault(
            [MarshalAs(UnmanagedType.LPWStr)] string pszQuery,
            int atQueryType,
            int alQueryLevel,
            [MarshalAs(UnmanagedType.LPWStr)] out string ppszAssociation);

        [PreserveSig]
        int QueryAppIsDefault(
            [MarshalAs(UnmanagedType.LPWStr)] string pszQuery,
            int atQueryType,
            int alQueryLevel,
            [MarshalAs(UnmanagedType.LPWStr)] string pszAppRegistryName,
            [MarshalAs(UnmanagedType.Bool)] out bool pfDefault);

        [PreserveSig]
        int QueryAppIsDefaultAll(
            int alQueryLevel,
            [MarshalAs(UnmanagedType.LPWStr)] string pszAppRegistryName,
            [MarshalAs(UnmanagedType.Bool)] out bool pfDefault);

        [PreserveSig]
        int SetAppAsDefault(
            [MarshalAs(UnmanagedType.LPWStr)] string pszAppRegistryName,
            [MarshalAs(UnmanagedType.LPWStr)] string pszSet,
            int atSetType);

        [PreserveSig]
        int SetAppAsDefaultAll(
            [MarshalAs(UnmanagedType.LPWStr)] string pszAppRegistryName);

        [PreserveSig]
        int ClearUserAssociations();
    }

    private static readonly Guid ClsidApplicationAssociationRegistration =
        new("591209c7-767b-42b2-9fba-44ee4615f2c7");

    public static string ResolveExecutablePath()
    {
        try
        {
            string? proc = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(proc) &&
                !string.Equals(Path.GetFileNameWithoutExtension(proc), "dotnet", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(proc))
            {
                return proc;
            }
        }
        catch { }

        string candidate = Path.Combine(AppContext.BaseDirectory, "Horizon.Browser.exe");
        if (File.Exists(candidate)) return candidate;

        string asm = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(asm))
        {
            string fromAsm = Path.ChangeExtension(asm, ".exe");
            if (File.Exists(fromAsm)) return fromAsm;
        }

        return candidate;
    }

    public static bool IsDefaultBrowser()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            string? progId = key?.GetValue("ProgId") as string;
            if (string.Equals(progId, ProgId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(progId, LegacyProgId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            using var latestKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoiceLatest\ProgId");
            string? latestProgId = latestKey?.GetValue("ProgId") as string;
            return string.Equals(latestProgId, ProgId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(latestProgId, LegacyProgId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures Horizon Browser is registered in HKCU for Windows 11 Default Apps &amp; Installed Apps.
    /// Safe to call on startup (idempotent, non-elevated).
    /// </summary>
    public static bool EnsureRegistered(bool notifyShell = true)
    {
        try
        {
            string exePath = ResolveExecutablePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                LogService.Write("DEFAULT", $"Skipped default browser registration; exe not found: '{exePath}'");
                return false;
            }

            string exeName = Path.GetFileName(exePath);
            string exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            string iconVal = $"\"{exePath}\",0";
            string openCmd = $"\"{exePath}\" \"%1\"";
            string plainCmd = $"\"{exePath}\"";

            // 1. Primary ProgID (plus legacy ProgID redirect so existing UserChoice keeps working)
            RegisterProgId(ProgId, AppDisplayName + " HTML Document", iconVal, openCmd);
            RegisterProgId(LegacyProgId, AppDisplayName + " HTML Document", iconVal, openCmd);

            // 2. Applications\<exe> registration (helps Windows 11 Open With & Default Apps discovery)
            string appClassesKey = $@"Software\Classes\Applications\{exeName}";
            using (var key = Registry.CurrentUser.CreateSubKey(appClassesKey))
            {
                key.SetValue("FriendlyAppName", AppDisplayName);
                key.SetValue("ApplicationCompany", "Swordmaster_Studios Inc.");
            }
            using (var key = Registry.CurrentUser.CreateSubKey($@"{appClassesKey}\DefaultIcon"))
                key.SetValue("", iconVal);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{appClassesKey}\shell\open"))
                key.SetValue("FriendlyAppName", AppDisplayName);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{appClassesKey}\shell\open\command"))
                key.SetValue("", openCmd);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{appClassesKey}\SupportedTypes"))
            {
                foreach (string ext in new[] { ".htm", ".html", ".shtml", ".xht", ".xhtml", ".svg", ".webp", ".pdf" })
                    key.SetValue(ext, "");
            }

            // 3. StartMenuInternet registration (Windows Default Apps browser category)
            using (var key = Registry.CurrentUser.CreateSubKey(StartMenuKey))
                key.SetValue("", AppDisplayName);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{StartMenuKey}\DefaultIcon"))
                key.SetValue("", iconVal);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{StartMenuKey}\shell\open\command"))
                key.SetValue("", plainCmd);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{StartMenuKey}\InstallInfo"))
            {
                key.SetValue("ReinstallCommand", plainCmd);
                key.SetValue("HideIconsCommand", plainCmd);
                key.SetValue("ShowIconsCommand", plainCmd);
                key.SetValue("IconsVisible", 1, RegistryValueKind.DWord);
            }
            WriteCapabilities($@"{StartMenuKey}\Capabilities", iconVal);

            // 4. Capabilities under Software\Horizon Browser\Capabilities
            WriteCapabilities(CapabilitiesKey, iconVal);

            // 5. RegisteredApplications pointer (required for Windows 11 Default Apps list)
            using (var regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                regApps.SetValue(AppDisplayName, CapabilitiesKey);

            // 6. HKCU Uninstall / Installed Apps entry (only if no machine-wide or Inno installer entry exists)
            EnsureInstalledAppEntry(exePath, exeDir, iconVal);

            if (notifyShell)
            {
                try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
                catch { }
            }

            LogService.Write("DEFAULT", $"Registered {AppDisplayName} for Windows Default Apps -> {exePath}");
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "DefaultBrowserService.EnsureRegistered");
            return false;
        }
    }

    private static void RegisterProgId(string progId, string friendlyName, string iconVal, string openCmd)
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
        {
            key.SetValue("", friendlyName);
            key.SetValue("FriendlyTypeName", friendlyName);
            key.SetValue("URL Protocol", "");
        }
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\Application"))
        {
            key.SetValue("ApplicationName", AppDisplayName);
            key.SetValue("ApplicationDescription", "High-Performance Privacy & Spatial Web Browser");
            key.SetValue("ApplicationCompany", "Swordmaster_Studios Inc.");
            key.SetValue("ApplicationIcon", iconVal);
        }
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\DefaultIcon"))
            key.SetValue("", iconVal);
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command"))
            key.SetValue("", openCmd);
    }

    private static void WriteCapabilities(string keyPath, string iconVal)
    {
        using var caps = Registry.CurrentUser.CreateSubKey(keyPath);
        caps.SetValue("ApplicationName", AppDisplayName);
        caps.SetValue("ApplicationDescription", "High-Performance Privacy & Spatial Web Browser");
        caps.SetValue("ApplicationIcon", iconVal);

        using (var url = caps.CreateSubKey("URLAssociations"))
        {
            url.SetValue("http", ProgId);
            url.SetValue("https", ProgId);
            url.SetValue("ftp", ProgId);
            url.SetValue("mailto", ProgId);
        }
        using (var fa = caps.CreateSubKey("FileAssociations"))
        {
            fa.SetValue(".htm", ProgId);
            fa.SetValue(".html", ProgId);
            fa.SetValue(".shtml", ProgId);
            fa.SetValue(".xhtml", ProgId);
            fa.SetValue(".xht", ProgId);
            fa.SetValue(".svg", ProgId);
            fa.SetValue(".webp", ProgId);
            fa.SetValue(".pdf", ProgId);
        }
        using (var mimes = caps.CreateSubKey("MIMEAssociations"))
        {
            mimes.SetValue("text/html", ProgId);
            mimes.SetValue("application/xhtml+xml", ProgId);
        }
        using (var startMenu = caps.CreateSubKey("StartMenu"))
        {
            startMenu.SetValue("StartMenuInternet", "HorizonBrowser");
        }
    }

    private static void EnsureInstalledAppEntry(string exePath, string exeDir, string iconVal)
    {
        try
        {
            using var existing = Registry.CurrentUser.OpenSubKey(UninstallKey);
            string? existingUninstall = existing?.GetValue("UninstallString") as string;
            if (!string.IsNullOrWhiteSpace(existingUninstall)) return;

            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.8.0";
            string uninstExe = Path.Combine(exeDir, "unins000.exe");
            string uninstallCmd = File.Exists(uninstExe) ? $"\"{uninstExe}\"" : $"\"{exePath}\" /maintenance";

            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", AppDisplayName);
            key.SetValue("DisplayIcon", iconVal);
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "Swordmaster_Studios Inc.");
            key.SetValue("InstallLocation", exeDir);
            key.SetValue("UninstallString", uninstallCmd);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch { }
    }

    /// <summary>
    /// Registers Horizon Browser, attempts programmatic default assignment via official Windows COM API,
    /// and if Windows 11 UCPD requires user confirmation, opens Horizon Browser's direct Default Apps page.
    /// Returns true if Horizon is now the default browser.
    /// </summary>
    public static bool TrySetAsDefaultBrowser()
    {
        EnsureRegistered(notifyShell: true);

        // Attempt official Windows IApplicationAssociationRegistration COM API
        IApplicationAssociationRegistration? reg = null;
        try
        {
            Type? comType = Type.GetTypeFromCLSID(ClsidApplicationAssociationRegistration, throwOnError: false);
            if (comType != null)
            {
                reg = Activator.CreateInstance(comType) as IApplicationAssociationRegistration;
                if (reg != null)
                {
                    int hrAll = reg.SetAppAsDefaultAll(AppDisplayName);
                    if (hrAll != 0)
                    {
                        // AT_URLPROTOCOL = 1, AT_FILEEXTENSION = 0
                        reg.SetAppAsDefault(AppDisplayName, "http", 1);
                        reg.SetAppAsDefault(AppDisplayName, "https", 1);
                        reg.SetAppAsDefault(AppDisplayName, ".htm", 0);
                        reg.SetAppAsDefault(AppDisplayName, ".html", 0);
                    }
                    try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Write("DEFAULT", $"COM IApplicationAssociationRegistration fallback: {ex.Message}");
        }
        finally
        {
            if (reg != null && Marshal.IsComObject(reg))
                Marshal.ReleaseComObject(reg);
        }

        if (IsDefaultBrowser())
        {
            LogService.Write("DEFAULT", "Horizon Browser is now the default browser.");
            return true;
        }

        // Windows 11 UCPD requires the one-click confirmation in Settings -> Default Apps.
        // Deep-link directly to Horizon Browser's dedicated page so the user only clicks "Set default".
        string deepLink = "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppDisplayName);
        try
        {
            Process.Start(new ProcessStartInfo(deepLink) { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }

        return false;
    }
}
