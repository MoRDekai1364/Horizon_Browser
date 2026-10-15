using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Stealth.Services;

public class BackupInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public string FormattedSize => FormatBytes(SizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0) return "0 B";
        long b = Math.Abs(bytes);
        int place = Convert.ToInt32(Math.Floor(Math.Log(b, 1024)));
        double num = Math.Round(b / Math.Pow(1024, place), 1);
        return $"{Math.Sign(bytes) * num} {suf[place]}";
    }
}

public static class BackupService
{
    public static string DefaultBackupsRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Horizon_Browser", "Backups");

    private static readonly string[] ExcludedFolders = new[]
    {
        "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache", "Crashpad"
    };

    public static List<BackupInfo> GetAvailableBackups()
    {
        var list = new List<BackupInfo>();
        try
        {
            if (!Directory.Exists(DefaultBackupsRoot)) return list;

            foreach (var dir in Directory.GetDirectories(DefaultBackupsRoot))
            {
                var dirInfo = new DirectoryInfo(dir);
                long totalSize = 0;
                try
                {
                    totalSize = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                                       .Sum(f => f.Length);
                }
                catch { }

                list.Add(new BackupInfo
                {
                    Name = dirInfo.Name,
                    Path = dirInfo.FullName,
                    CreatedAt = dirInfo.CreationTime,
                    SizeBytes = totalSize
                });
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BackupService.GetAvailableBackups");
        }

        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    public static async Task<string> CreateBackupAsync(
        string? targetDir = null,
        IProgress<(string step, int percent)>? progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                progress?.Report(("Initializing backup...", 5));

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                string destinationFolder = targetDir ?? Path.Combine(DefaultBackupsRoot, $"Backup_{timestamp}");
                Directory.CreateDirectory(destinationFolder);

                string localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Horizon_Browser");
                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

                // 1. Export core user files
                progress?.Report(("Backing up settings and security vault...", 20));
                string[] filesToBackup = new[]
                {
                    "vault.dat",
                    "settings.json",
                    "cookies.json",
                    "bookmarks.json",
                    "history.json",
                    "notes.json",
                    "default_settings.txt"
                };

                foreach (var file in filesToBackup)
                {
                    string localPath = Path.Combine(localAppData, file);
                    string rootPath = Path.Combine(appDir, file);
                    string? chosen = File.Exists(localPath) ? localPath : (File.Exists(rootPath) ? rootPath : null);

                    if (chosen != null)
                    {
                        try { File.Copy(chosen, Path.Combine(destinationFolder, file), overwrite: true); }
                        catch { }
                    }
                }

                // 2. Profile and WebView2 data
                progress?.Report(("Backing up user profile and history...", 50));
                string profileSrc = Path.Combine(localAppData, "HorizonData");
                if (!Directory.Exists(profileSrc) && Directory.Exists(Path.Combine(appDir, "HorizonData")))
                    profileSrc = Path.Combine(appDir, "HorizonData");

                if (Directory.Exists(profileSrc))
                {
                    string profileDest = Path.Combine(destinationFolder, "HorizonData");
                    CopyDirectoryFiltered(profileSrc, profileDest);
                }

                // 3. WebApps and custom configs
                progress?.Report(("Backing up WebApps and extensions catalog...", 75));
                string webAppsSrc = Path.Combine(localAppData, "webapps");
                if (Directory.Exists(webAppsSrc))
                {
                    CopyDirectoryFiltered(webAppsSrc, Path.Combine(destinationFolder, "webapps"));
                }

                // 4. Metadata manifest
                progress?.Report(("Finalizing backup package...", 90));
                var manifest = new
                {
                    Version = "1.0",
                    CreatedAt = DateTime.UtcNow,
                    Machine = Environment.MachineName,
                    User = Environment.UserName,
                    Files = filesToBackup.Where(f => File.Exists(Path.Combine(destinationFolder, f))).ToList()
                };
                File.WriteAllText(
                    Path.Combine(destinationFolder, "backup_manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                progress?.Report(("Backup completed successfully!", 100));
                LogService.Write("BACKUP", $"Backup created at: {destinationFolder}");
                return destinationFolder;
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "BackupService.CreateBackupAsync");
                throw;
            }
        });
    }

    public static bool LaunchExternalRestore(string backupFolderPath)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            int currentPid = Environment.ProcessId;

            string[] candidates = new[]
            {
                Path.Combine(baseDir, "Horizon.Updater.exe"),
                Path.Combine(baseDir, "Tools", "Horizon.Updater.exe"),
                Path.Combine(baseDir, "Tools", "Horizon.Updater", "Horizon.Updater.exe"),
                Path.Combine(baseDir, "..", "..", "..", "Tools", "Horizon.Updater", "bin", "Release", "net8.0-windows10.0.19041.0", "Horizon.Updater.exe")
            };

            string? updaterExe = candidates.FirstOrDefault(File.Exists);
            if (updaterExe != null)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments = $"--restore \"{backupFolderPath}\" --target \"{baseDir}\" --pid {currentPid}",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                System.Windows.Application.Current.Shutdown();
                return true;
            }

            string batPath = Path.Combine(baseDir, "restore.bat");
            if (File.Exists(batPath))
            {
                try { File.WriteAllText(Path.Combine(baseDir, "last_backup_path.txt"), backupFolderPath); } catch { }
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batPath}\"",
                    WorkingDirectory = baseDir,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                System.Windows.Application.Current.Shutdown();
                return true;
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BackupService.LaunchExternalRestore");
        }

        return false;
    }

    public static async Task<bool> RestoreBackupAsync(
        string backupFolderPath,
        IProgress<(string step, int percent)>? progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(backupFolderPath))
                    throw new DirectoryNotFoundException($"Backup folder not found: {backupFolderPath}");

                progress?.Report(("Validating backup package...", 10));

                string localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Horizon_Browser");
                string appDataHorizonData = Path.Combine(localAppData, "HorizonData");
                Directory.CreateDirectory(localAppData);
                Directory.CreateDirectory(appDataHorizonData);

                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                string appDirHorizonData = Path.Combine(appDir, "HorizonData");
                Directory.CreateDirectory(appDirHorizonData);

                // 1. Restore core user files to BOTH LocalAppData and AppDir
                progress?.Report(("Restoring settings and security vault...", 30));
                string[] filesToRestore = new[]
                {
                    "vault.dat",
                    "settings.json",
                    "config.json",
                    "cookies.json",
                    "bookmarks.json",
                    "history.json",
                    "notes.json"
                };

                foreach (var file in filesToRestore)
                {
                    string rootSrc = Path.Combine(backupFolderPath, file);
                    string dataSrc = Path.Combine(backupFolderPath, "HorizonData", file);
                    string? chosenSrc = File.Exists(rootSrc) ? rootSrc : (File.Exists(dataSrc) ? dataSrc : null);

                    if (chosenSrc != null)
                    {
                        try { File.Copy(chosenSrc, Path.Combine(localAppData, file), overwrite: true); } catch { }
                        try { File.Copy(chosenSrc, Path.Combine(appDataHorizonData, file), overwrite: true); } catch { }
                        try { File.Copy(chosenSrc, Path.Combine(appDir, file), overwrite: true); } catch { }
                        try { File.Copy(chosenSrc, Path.Combine(appDirHorizonData, file), overwrite: true); } catch { }
                    }
                }

                // 2. Restore profile (EBWebView) to BOTH LocalAppData and AppDir
                progress?.Report(("Restoring browser profile data...", 60));
                string profileSrc = Path.Combine(backupFolderPath, "HorizonData");
                if (!Directory.Exists(profileSrc) && Directory.Exists(Path.Combine(backupFolderPath, "EBWebView")))
                    profileSrc = backupFolderPath;

                if (Directory.Exists(profileSrc))
                {
                    CopyDirectoryFiltered(profileSrc, appDataHorizonData);
                    CopyDirectoryFiltered(profileSrc, appDirHorizonData);
                }

                // 3. Restore WebApps
                progress?.Report(("Restoring WebApps...", 85));
                string webAppsSrc = Path.Combine(backupFolderPath, "webapps");
                if (Directory.Exists(webAppsSrc))
                {
                    string webAppsDest = Path.Combine(localAppData, "webapps");
                    CopyDirectoryFiltered(webAppsSrc, webAppsDest);
                }

                progress?.Report(("Restore completed successfully!", 100));
                LogService.Write("BACKUP", $"Backup restored from: {backupFolderPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "BackupService.RestoreBackupAsync");
                throw;
            }
        });
    }

    private static void CopyDirectoryFiltered(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            try
            {
                string targetFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, targetFile, overwrite: true);
            }
            catch { }
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(dir);
            if (ExcludedFolders.Any(e => e.Equals(dirName, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                string targetSubDir = Path.Combine(destDir, dirName);
                CopyDirectoryFiltered(dir, targetSubDir);
            }
            catch { }
        }
    }
}
