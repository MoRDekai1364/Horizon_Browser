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
                Directory.CreateDirectory(localAppData);
                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

                // 1. Restore core user files
                progress?.Report(("Restoring settings and security vault...", 30));
                string[] filesToRestore = new[]
                {
                    "vault.dat",
                    "settings.json",
                    "cookies.json",
                    "bookmarks.json",
                    "history.json",
                    "notes.json"
                };

                foreach (var file in filesToRestore)
                {
                    string src = Path.Combine(backupFolderPath, file);
                    if (File.Exists(src))
                    {
                        try { File.Copy(src, Path.Combine(localAppData, file), overwrite: true); } catch { }
                        try
                        {
                            if (Directory.Exists(appDir) && Path.GetDirectoryName(appDir) != null)
                                File.Copy(src, Path.Combine(appDir, file), overwrite: true);
                        }
                        catch { }
                    }
                }

                // 2. Restore profile
                progress?.Report(("Restoring browser profile data...", 60));
                string profileSrc = Path.Combine(backupFolderPath, "HorizonData");
                if (Directory.Exists(profileSrc))
                {
                    string profileDest = Path.Combine(localAppData, "HorizonData");
                    CopyDirectoryFiltered(profileSrc, profileDest);
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
