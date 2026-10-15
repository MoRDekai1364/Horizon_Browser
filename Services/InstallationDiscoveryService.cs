using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Horizon.Stealth.Services;

public class HorizonInstallationInfo
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = "Unknown";
    public string Channel { get; set; } = string.Empty;
    public string FullVersionString => string.IsNullOrEmpty(Channel) ? Version : $"{Version} ({Channel})";
    
    public bool HasVault { get; set; }
    public bool HasBookmarks { get; set; }
    public bool HasCookies { get; set; }
    public bool HasHistory { get; set; }
    public bool HasProfileData { get; set; }
    public bool HasNotes { get; set; }
    public bool HasSettings { get; set; }
    
    public long TotalDataSizeBytes { get; set; }
    public string FormattedDataSize
    {
        get
        {
            if (TotalDataSizeBytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double size = TotalDataSizeBytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:0.#} {units[unitIndex]}";
        }
    }

    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            if (HasVault) parts.Add("Vault");
            if (HasBookmarks) parts.Add("Bookmarks");
            if (HasCookies) parts.Add("Cookies");
            if (HasSettings) parts.Add("Settings");
            if (HasProfileData) parts.Add($"Profile ({FormattedDataSize})");
            return parts.Count > 0 ? string.Join(", ", parts) : "Executable only";
        }
    }
}

public static class InstallationDiscoveryService
{
    private static readonly string[] VolatileFolderNames = new[]
    {
        "Cache", "Code Cache", "GPUCache", "ShaderCache",
        "GrShaderCache", "Crashpad", "Service Worker"
    };

    public static List<HorizonInstallationInfo> DiscoverInstallations()
    {
        var results = new List<HorizonInstallationInfo>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string currentBaseDir = NormalizePath(AppDomain.CurrentDomain.BaseDirectory);
        scannedPaths.Add(currentBaseDir);

        var candidateDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan Registry uninstall keys
        ScanRegistry(candidateDirs);

        // 2. Scan Standard & Well-Known filesystem paths
        ScanStandardPaths(candidateDirs);

        // 3. Inspect each candidate directory
        foreach (string candidate in candidateDirs)
        {
            string norm = NormalizePath(candidate);
            if (string.IsNullOrEmpty(norm) || scannedPaths.Contains(norm))
                continue;

            scannedPaths.Add(norm);

            var info = InspectInstallation(norm);
            if (info != null)
            {
                results.Add(info);
            }
        }

        return results;
    }

    private static void ScanRegistry(HashSet<string> candidates)
    {
        try
        {
            var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
            var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };

            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                        if (uninstallKey == null) continue;

                        foreach (string subkeyName in uninstallKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = uninstallKey.OpenSubKey(subkeyName);
                                if (appKey == null) continue;

                                string displayName = appKey.GetValue("DisplayName")?.ToString() ?? "";
                                string displayIcon = appKey.GetValue("DisplayIcon")?.ToString() ?? "";
                                string installLocation = appKey.GetValue("InstallLocation")?.ToString() ?? "";
                                string appPath = appKey.GetValue("Inno Setup: App Path")?.ToString() ?? "";
                                string uninstallString = appKey.GetValue("UninstallString")?.ToString() ?? "";

                                bool isHorizon = displayName.Contains("Horizon", StringComparison.OrdinalIgnoreCase) ||
                                                 displayIcon.Contains("Horizon", StringComparison.OrdinalIgnoreCase) ||
                                                 installLocation.Contains("Horizon", StringComparison.OrdinalIgnoreCase) ||
                                                 subkeyName.Contains("Horizon", StringComparison.OrdinalIgnoreCase);

                                if (!isHorizon) continue;

                                AddCleanPath(candidates, installLocation);
                                AddCleanPath(candidates, appPath);

                                if (!string.IsNullOrEmpty(displayIcon))
                                {
                                    string iconFile = displayIcon.Trim('"', ' ');
                                    string? iconDir = Path.GetDirectoryName(iconFile);
                                    AddCleanPath(candidates, iconDir);
                                }

                                if (!string.IsNullOrEmpty(uninstallString))
                                {
                                    string uninstaller = uninstallString.Trim('"', ' ');
                                    if (uninstaller.Contains(".exe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int exeIdx = uninstaller.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                                        uninstaller = uninstaller.Substring(0, exeIdx + 4).Trim('"', ' ');
                                    }
                                    string? uninstDir = Path.GetDirectoryName(uninstaller);
                                    AddCleanPath(candidates, uninstDir);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "InstallationDiscoveryService.ScanRegistry");
        }
    }

    private static void ScanStandardPaths(HashSet<string> candidates)
    {
        try
        {
            // Explicit known stable paths
            AddCleanPath(candidates, @"D:\Program Files\Horizon_Browser_Official_Release");
            AddCleanPath(candidates, @"C:\Program Files\Horizon_Browser_Official_Release");
            AddCleanPath(candidates, @"C:\Program Files\Horizon_Browser");
            AddCleanPath(candidates, @"C:\Program Files (x86)\Horizon_Browser");
            AddCleanPath(candidates, @"D:\Program Files\Horizon_Browser");

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                AddCleanPath(candidates, Path.Combine(localAppData, "Horizon_Browser"));
            }

            // Scan all fixed drives for Horizon folders in Program Files
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    string pf = Path.Combine(drive.RootDirectory.FullName, "Program Files");
                    string pfx86 = Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)");

                    foreach (var root in new[] { pf, pfx86 })
                    {
                        if (!Directory.Exists(root)) continue;
                        foreach (var sub in Directory.GetDirectories(root, "*Horizon*"))
                        {
                            AddCleanPath(candidates, sub);
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "InstallationDiscoveryService.ScanStandardPaths");
        }
    }

    private static void AddCleanPath(HashSet<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            string clean = path.Trim('"', ' ', '\t', '\r', '\n');
            if (Directory.Exists(clean))
            {
                candidates.Add(NormalizePath(clean));
            }
        }
        catch { }
    }

    private static HorizonInstallationInfo? InspectInstallation(string dirPath)
    {
        try
        {
            string exePath = Path.Combine(dirPath, "Horizon.Browser.exe");
            string dllPath = Path.Combine(dirPath, "Horizon.Browser.dll");
            string horizonDataDir = Path.Combine(dirPath, "HorizonData");
            string uninstallerPath = Path.Combine(dirPath, "unins000.exe");

            // Must look like a real Horizon installation
            bool hasExecutable = File.Exists(exePath) || File.Exists(dllPath);
            bool hasHorizonData = Directory.Exists(horizonDataDir);
            bool hasUninstaller = File.Exists(uninstallerPath);

            if (!hasExecutable && !hasHorizonData && !hasUninstaller)
                return null;

            var info = new HorizonInstallationInfo
            {
                Path = dirPath,
                DisplayName = new DirectoryInfo(dirPath).Name
            };

            // Version discovery
            string versionFile = Path.Combine(dirPath, "version.txt");
            if (File.Exists(versionFile))
            {
                try
                {
                    var lines = File.ReadAllLines(versionFile);
                    if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
                    {
                        string v = lines[0].Trim();
                        if (v.StartsWith("version ", StringComparison.OrdinalIgnoreCase))
                            v = v.Substring("version ".Length).Trim();
                        info.Version = v;
                    }
                    if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]))
                    {
                        info.Channel = lines[1].Trim();
                    }
                }
                catch { }
            }

            if (info.Version == "Unknown" && File.Exists(exePath))
            {
                try
                {
                    var fvi = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrEmpty(fvi.ProductVersion))
                        info.Version = fvi.ProductVersion;
                    else if (!string.IsNullOrEmpty(fvi.FileVersion))
                        info.Version = fvi.FileVersion;
                }
                catch { }
            }

            // Inspect available user data
            string rootVault = Path.Combine(dirPath, "vault.dat");
            string dataVault = Path.Combine(horizonDataDir, "vault.dat");
            info.HasVault = File.Exists(rootVault) || File.Exists(dataVault);

            string rootBookmarks = Path.Combine(dirPath, "bookmarks.json");
            string dataBookmarks = Path.Combine(horizonDataDir, "bookmarks.json");
            info.HasBookmarks = File.Exists(rootBookmarks) || File.Exists(dataBookmarks);

            string rootCookies = Path.Combine(dirPath, "cookies.json");
            string dataCookies = Path.Combine(horizonDataDir, "cookies.json");
            info.HasCookies = File.Exists(rootCookies) || File.Exists(dataCookies);

            string rootHistory = Path.Combine(dirPath, "history.json");
            string dataHistory = Path.Combine(horizonDataDir, "history.json");
            string rawHistory = Path.Combine(horizonDataDir, "EBWebView", "Default", "History");
            info.HasHistory = File.Exists(rootHistory) || File.Exists(dataHistory) || File.Exists(rawHistory);

            string rootSettings = Path.Combine(dirPath, "settings.json");
            string dataConfig = Path.Combine(horizonDataDir, "config.json");
            info.HasSettings = File.Exists(rootSettings) || File.Exists(dataConfig);

            string notesDir = Path.Combine(dirPath, "Notes");
            info.HasNotes = Directory.Exists(notesDir);

            info.HasProfileData = Directory.Exists(horizonDataDir);

            // Calculate user data size
            long totalBytes = 0;
            if (File.Exists(rootVault)) totalBytes += new FileInfo(rootVault).Length;
            if (File.Exists(dataVault)) totalBytes += new FileInfo(dataVault).Length;
            if (File.Exists(rootBookmarks)) totalBytes += new FileInfo(rootBookmarks).Length;
            if (File.Exists(dataBookmarks)) totalBytes += new FileInfo(dataBookmarks).Length;
            if (File.Exists(rootCookies)) totalBytes += new FileInfo(rootCookies).Length;
            if (File.Exists(dataCookies)) totalBytes += new FileInfo(dataCookies).Length;
            if (File.Exists(rootSettings)) totalBytes += new FileInfo(rootSettings).Length;
            if (File.Exists(dataConfig)) totalBytes += new FileInfo(dataConfig).Length;

            if (hasHorizonData)
            {
                totalBytes += CalculateDirectorySizeSafe(horizonDataDir);
            }

            if (info.HasNotes)
            {
                totalBytes += CalculateDirectorySizeSafe(notesDir);
            }

            info.TotalDataSizeBytes = totalBytes;

            return info;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, $"InspectInstallation({dirPath})");
            return null;
        }
    }

    private static long CalculateDirectorySizeSafe(string dirPath)
    {
        long bytes = 0;
        try
        {
            var dir = new DirectoryInfo(dirPath);
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    // Skip volatile caches in size calculation
                    if (file.DirectoryName != null &&
                        VolatileFolderNames.Any(v => file.DirectoryName.Contains(v, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    bytes += file.Length;
                }
                catch { }
            }
        }
        catch { }
        return bytes;
    }

    public static async Task<bool> RestoreDataFromInstallationAsync(
        HorizonInstallationInfo source,
        Action<string, double>? progress = null)
    {
        return await Task.Run(async () =>
        {
            try
            {
                progress?.Invoke("Creating safety backup of current state...", 5);
                var backupProgress = new Progress<(string step, int percent)>(p =>
                {
                    progress?.Invoke($"Safety backup: {p.step}", 5 + (p.percent * 0.15));
                });
                await BackupService.CreateBackupAsync(null, backupProgress);

                string currentBaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                string currentDataDir = Path.Combine(currentBaseDir, "HorizonData");
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string currentAppDataDir = Path.Combine(localAppData, "Horizon_Browser");
                string currentAppDataHorizonData = Path.Combine(currentAppDataDir, "HorizonData");

                Directory.CreateDirectory(currentDataDir);
                Directory.CreateDirectory(currentAppDataDir);
                Directory.CreateDirectory(currentAppDataHorizonData);

                progress?.Invoke("Restoring vault and credentials...", 25);
                string srcVault = File.Exists(Path.Combine(source.Path, "vault.dat"))
                    ? Path.Combine(source.Path, "vault.dat")
                    : Path.Combine(source.Path, "HorizonData", "vault.dat");
                if (File.Exists(srcVault))
                {
                    CopyFileIfExists(srcVault, Path.Combine(currentBaseDir, "vault.dat"));
                    CopyFileIfExists(srcVault, Path.Combine(currentDataDir, "vault.dat"));
                    CopyFileIfExists(srcVault, Path.Combine(currentAppDataHorizonData, "vault.dat"));
                }

                progress?.Invoke("Restoring bookmarks...", 40);
                string srcBookmarks = File.Exists(Path.Combine(source.Path, "bookmarks.json"))
                    ? Path.Combine(source.Path, "bookmarks.json")
                    : Path.Combine(source.Path, "HorizonData", "bookmarks.json");
                if (File.Exists(srcBookmarks))
                {
                    CopyFileIfExists(srcBookmarks, Path.Combine(currentBaseDir, "bookmarks.json"));
                    CopyFileIfExists(srcBookmarks, Path.Combine(currentDataDir, "bookmarks.json"));
                    CopyFileIfExists(srcBookmarks, Path.Combine(currentAppDataHorizonData, "bookmarks.json"));
                }

                progress?.Invoke("Restoring cookies and session state...", 55);
                string srcCookies = File.Exists(Path.Combine(source.Path, "cookies.json"))
                    ? Path.Combine(source.Path, "cookies.json")
                    : Path.Combine(source.Path, "HorizonData", "cookies.json");
                if (File.Exists(srcCookies))
                {
                    CopyFileIfExists(srcCookies, Path.Combine(currentBaseDir, "cookies.json"));
                    CopyFileIfExists(srcCookies, Path.Combine(currentDataDir, "cookies.json"));
                    CopyFileIfExists(srcCookies, Path.Combine(currentAppDataHorizonData, "cookies.json"));
                }

                progress?.Invoke("Restoring configuration...", 70);
                string srcConfig = File.Exists(Path.Combine(source.Path, "HorizonData", "config.json"))
                    ? Path.Combine(source.Path, "HorizonData", "config.json")
                    : Path.Combine(source.Path, "settings.json");
                if (File.Exists(srcConfig))
                {
                    CopyFileIfExists(srcConfig, Path.Combine(currentBaseDir, "settings.json"));
                    CopyFileIfExists(srcConfig, Path.Combine(currentDataDir, "config.json"));
                    CopyFileIfExists(srcConfig, Path.Combine(currentAppDataHorizonData, "config.json"));
                }

                string srcNotes = Path.Combine(source.Path, "Notes");
                if (Directory.Exists(srcNotes))
                {
                    progress?.Invoke("Restoring user notes...", 75);
                    string targetNotes = Path.Combine(currentBaseDir, "Notes");
                    CopyDirectoryFiltered(srcNotes, targetNotes);
                }

                string srcHorizonData = Path.Combine(source.Path, "HorizonData");
                if (Directory.Exists(srcHorizonData))
                {
                    progress?.Invoke("Restoring browser profile and storage...", 85);
                    CopyDirectoryFiltered(srcHorizonData, currentDataDir);
                    CopyDirectoryFiltered(srcHorizonData, currentAppDataHorizonData);
                }

                progress?.Invoke("Restoration completed successfully!", 100);
                LogService.Write("RESTORE", $"Restored data from existing installation at {source.Path} ({source.FullVersionString})");
                return true;
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, $"RestoreDataFromInstallationAsync({source.Path})");
                progress?.Invoke($"Error: {ex.Message}", 100);
                return false;
            }
        });
    }

    private static void CopyFileIfExists(string sourceFile, string targetFile)
    {
        try
        {
            if (File.Exists(sourceFile))
            {
                string? targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                File.Copy(sourceFile, targetFile, true);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, $"CopyFileIfExists: {sourceFile} -> {targetFile}");
        }
    }

    private static void CopyDirectoryFiltered(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            try
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            catch { }
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            try
            {
                string subName = Path.GetFileName(subDir);
                if (VolatileFolderNames.Contains(subName, StringComparer.OrdinalIgnoreCase))
                    continue;

                string destSub = Path.Combine(targetDir, subName);
                CopyDirectoryFiltered(subDir, destSub);
            }
            catch { }
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd('\\', '/');
        }
    }
}
