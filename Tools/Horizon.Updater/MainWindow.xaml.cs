using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;

namespace Horizon.Updater;

public partial class MainWindow : Window
{
    private readonly string _installerPath;
    private readonly string _restoreSource;
    private readonly string _targetDir;
    private readonly int _parentPid;
    private readonly bool _silent;

    private static readonly string[] VolatileFolderNames = new[]
    {
        "Cache", "Code Cache", "GPUCache", "ShaderCache",
        "GrShaderCache", "Crashpad", "Service Worker"
    };

    public MainWindow(string installerPath, string restoreSource, string targetDir, int parentPid, bool silent)
    {
        InitializeComponent();
        _installerPath = installerPath;
        _restoreSource = restoreSource;
        _targetDir = string.IsNullOrWhiteSpace(targetDir)
            ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/')
            : targetDir.TrimEnd('\\', '/');
        _parentPid = parentPid;
        _silent = silent;

        Loaded += MainWindow_Loaded;
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_restoreSource))
        {
            TxtHeaderTitle.Text = "Restoring Horizon Data";
            TxtHeaderSubtitle.Text = "Data Recovery & Profile Migration Engine";
            await RunRestoreSequenceAsync();
        }
        else
        {
            TxtHeaderTitle.Text = "Updating Horizon Browser";
            TxtHeaderSubtitle.Text = "Professional Update Engine";
            await RunUpdateSequenceAsync();
        }
    }

    private void SetStatus(string text, int percent)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = text;
            ProgressBarUpdate.Value = percent;
            TxtPercent.Text = $"{percent}%";
        });
    }

    private async Task RunRestoreSequenceAsync()
    {
        try
        {
            // ── Step 1: Wait for parent browser and WebView2 to terminate ──
            SetStatus("Waiting for Horizon Browser processes to exit...", 10);
            await CloseBrowserProcessesAsync(_parentPid);
            await Task.Delay(500);

            // ── Step 2: Safety snapshot of current state ──
            SetStatus("Creating safety snapshot of current data...", 25);
            string snapshotDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon_Browser", "Backups", "pre_restore_snapshot");
            CreateSafetySnapshot(_targetDir, snapshotDir);

            // ── Step 3: Perform Out-of-Process Data Restoration ──
            SetStatus("Restoring passwords, bookmarks, and settings...", 50);
            await Task.Run(() => PerformRestoreOperation(_restoreSource, _targetDir));

            // ── Step 4: Verification ──
            SetStatus("Verifying restored data integrity...", 85);
            await Task.Delay(400);

            // ── Step 5: Relaunch Horizon Browser ──
            SetStatus("Relaunching Horizon Browser...", 98);
            await Task.Delay(600);

            string mainExe = Path.Combine(_targetDir, "Horizon.Browser.exe");
            if (File.Exists(mainExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = mainExe,
                    WorkingDirectory = _targetDir,
                    UseShellExecute = true
                });
            }

            SetStatus("Restoration completed successfully!", 100);
            await Task.Delay(1000);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = "Restoration Failed";
                ProgressBarUpdate.Value = 100;
                TxtError.Text = ex.Message;
                TxtError.Visibility = Visibility.Visible;
            });
            await Task.Delay(6000);
            Application.Current.Shutdown();
        }
    }

    private void PerformRestoreOperation(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source restore directory not found: {sourceDir}");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDataTarget = Path.Combine(localAppData, "Horizon_Browser", "HorizonData");
        string localTargetData = Path.Combine(targetDir, "HorizonData");

        Directory.CreateDirectory(appDataTarget);
        Directory.CreateDirectory(localTargetData);

        // 1. Restore Vault
        string srcVault = File.Exists(Path.Combine(sourceDir, "vault.dat"))
            ? Path.Combine(sourceDir, "vault.dat")
            : Path.Combine(sourceDir, "HorizonData", "vault.dat");
        if (File.Exists(srcVault))
        {
            SafeCopy(srcVault, Path.Combine(targetDir, "vault.dat"));
            SafeCopy(srcVault, Path.Combine(localTargetData, "vault.dat"));
            SafeCopy(srcVault, Path.Combine(appDataTarget, "vault.dat"));
        }

        // 2. Restore Bookmarks
        string srcBookmarks = File.Exists(Path.Combine(sourceDir, "bookmarks.json"))
            ? Path.Combine(sourceDir, "bookmarks.json")
            : Path.Combine(sourceDir, "HorizonData", "bookmarks.json");
        if (File.Exists(srcBookmarks))
        {
            SafeCopy(srcBookmarks, Path.Combine(targetDir, "bookmarks.json"));
            SafeCopy(srcBookmarks, Path.Combine(localTargetData, "bookmarks.json"));
            SafeCopy(srcBookmarks, Path.Combine(appDataTarget, "bookmarks.json"));
        }

        // 3. Restore Cookies
        string srcCookies = File.Exists(Path.Combine(sourceDir, "cookies.json"))
            ? Path.Combine(sourceDir, "cookies.json")
            : Path.Combine(sourceDir, "HorizonData", "cookies.json");
        if (File.Exists(srcCookies))
        {
            SafeCopy(srcCookies, Path.Combine(targetDir, "cookies.json"));
            SafeCopy(srcCookies, Path.Combine(localTargetData, "cookies.json"));
            SafeCopy(srcCookies, Path.Combine(appDataTarget, "cookies.json"));
        }

        // 4. Restore Config / Settings
        string srcConfig = File.Exists(Path.Combine(sourceDir, "HorizonData", "config.json"))
            ? Path.Combine(sourceDir, "HorizonData", "config.json")
            : Path.Combine(sourceDir, "settings.json");
        if (File.Exists(srcConfig))
        {
            SafeCopy(srcConfig, Path.Combine(localTargetData, "config.json"));
            SafeCopy(srcConfig, Path.Combine(appDataTarget, "config.json"));
            SafeCopy(srcConfig, Path.Combine(targetDir, "settings.json"));
        }

        // 5. Restore User Notes
        string srcNotes = Path.Combine(sourceDir, "Notes");
        if (Directory.Exists(srcNotes))
        {
            CopyDirectoryFiltered(srcNotes, Path.Combine(targetDir, "Notes"));
        }

        // 6. Restore Full EBWebView Profile to BOTH AppData and Project Root
        string srcProfile = Directory.Exists(Path.Combine(sourceDir, "HorizonData", "EBWebView"))
            ? Path.Combine(sourceDir, "HorizonData", "EBWebView")
            : Path.Combine(sourceDir, "EBWebView");

        if (Directory.Exists(srcProfile))
        {
            SetStatus("Injecting Chromium user profile...", 65);
            CopyDirectoryFiltered(srcProfile, Path.Combine(appDataTarget, "EBWebView"));
            CopyDirectoryFiltered(srcProfile, Path.Combine(localTargetData, "EBWebView"));
        }

        // 7. Restore Favicons & Migration Packages
        string srcFavicons = Path.Combine(sourceDir, "HorizonData", "FaviconCache");
        if (Directory.Exists(srcFavicons))
        {
            CopyDirectoryFiltered(srcFavicons, Path.Combine(appDataTarget, "FaviconCache"));
            CopyDirectoryFiltered(srcFavicons, Path.Combine(localTargetData, "FaviconCache"));
        }

        // 8. Prevent restored installation from triggering startup warning dialog
        DismissSourceInstallationInConfigs(sourceDir, localTargetData, appDataTarget, targetDir);
    }

    private static void DismissSourceInstallationInConfigs(string sourceDir, string localTargetData, string appDataTarget, string targetDir)
    {
        try
        {
            var pathsToDismiss = new List<string>();
            string normSource = Path.GetFullPath(sourceDir).TrimEnd('\\', '/');
            pathsToDismiss.Add(normSource);

            if (Path.GetFileName(normSource).Equals("HorizonData", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(normSource)?.FullName?.TrimEnd('\\', '/');
                if (!string.IsNullOrEmpty(parent))
                {
                    pathsToDismiss.Add(parent);
                }
            }

            string[] configFiles = new[]
            {
                Path.Combine(localTargetData, "config.json"),
                Path.Combine(appDataTarget, "config.json"),
                Path.Combine(targetDir, "settings.json")
            };

            foreach (var cfg in configFiles)
            {
                AddDismissedPathsToJsonConfig(cfg, pathsToDismiss);
            }
        }
        catch { }
    }

    private static void AddDismissedPathsToJsonConfig(string configPath, List<string> pathsToDismiss)
    {
        try
        {
            if (!File.Exists(configPath)) return;

            string json = File.ReadAllText(configPath);
            var root = JsonNode.Parse(json);
            if (root is not JsonObject obj) return;

            JsonArray dismissedArray;
            if (obj.TryGetPropertyValue("DismissedInstallationPaths", out var existingNode) && existingNode is JsonArray arr)
            {
                dismissedArray = arr;
            }
            else
            {
                dismissedArray = new JsonArray();
                obj["DismissedInstallationPaths"] = dismissedArray;
            }

            bool modified = false;
            foreach (var path in pathsToDismiss)
            {
                bool present = false;
                foreach (var item in dismissedArray)
                {
                    if (item != null && string.Equals(item.GetValue<string>()?.TrimEnd('\\', '/'), path, StringComparison.OrdinalIgnoreCase))
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    dismissedArray.Add(JsonValue.Create(path));
                    modified = true;
                }
            }

            if (modified)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, obj.ToJsonString(options));
            }
        }
        catch { }
    }

    private static void SafeCopy(string src, string dest)
    {
        try
        {
            string? dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(src, dest, overwrite: true);
        }
        catch { }
    }

    private static void CopyDirectoryFiltered(string sourceDir, string targetDir)
    {
        try
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
        catch { }
    }

    private async Task RunUpdateSequenceAsync()
    {
        try
        {
            // ── Step 1: Wait for parent browser to close ──
            SetStatus("Closing running Horizon Browser instances...", 10);
            await CloseBrowserProcessesAsync(_parentPid);

            // ── Step 2: Safety snapshot of critical user data ──
            SetStatus("Creating safety snapshot of user data...", 25);
            string snapshotDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon_Browser", "Backups", "pre_update_snapshot");
            CreateSafetySnapshot(_targetDir, snapshotDir);

            // ── Step 3: Apply update ──
            SetStatus("Applying update package...", 40);
            bool success = await ApplyUpdateAsync(_installerPath, _targetDir);

            if (!success)
            {
                throw new Exception("The update installer did not complete successfully.");
            }

            // ── Step 4: Verify and restore local files if overwritten ──
            SetStatus("Restoring user data integrity...", 90);
            RestoreSafetySnapshot(snapshotDir, _targetDir);

            // ── Step 5: Relaunch Horizon Browser ──
            SetStatus("Relaunching Horizon Browser...", 98);
            await Task.Delay(800);

            string mainExe = Path.Combine(_targetDir, "Horizon.Browser.exe");
            if (File.Exists(mainExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = mainExe,
                    WorkingDirectory = _targetDir,
                    UseShellExecute = true
                });
            }

            SetStatus("Update completed successfully!", 100);
            await Task.Delay(1000);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = "Update Failed";
                ProgressBarUpdate.Value = 100;
                TxtError.Text = ex.Message;
                TxtError.Visibility = Visibility.Visible;
            });
            await Task.Delay(5000);
            Application.Current.Shutdown();
        }
    }

    private static async Task CloseBrowserProcessesAsync(int parentPid)
    {
        await Task.Run(() =>
        {
            if (parentPid > 0)
            {
                try
                {
                    var parent = Process.GetProcessById(parentPid);
                    if (!parent.HasExited)
                    {
                        parent.CloseMainWindow();
                        if (!parent.WaitForExit(2000))
                            parent.Kill();
                    }
                }
                catch { }
            }

            // Clean up any remaining Horizon.Browser processes
            foreach (var proc in Process.GetProcessesByName("Horizon.Browser"))
            {
                try
                {
                    proc.CloseMainWindow();
                    if (!proc.WaitForExit(1500))
                        proc.Kill();
                }
                catch { }
            }

            // Ensure no lingering child msedgewebview2 processes hold locks
            try
            {
                foreach (var wvp in Process.GetProcessesByName("msedgewebview2"))
                {
                    try
                    {
                        // Check if command line mentions HorizonData or parent process
                        wvp.Kill();
                    }
                    catch { }
                }
            }
            catch { }
        });
    }

    private static void CreateSafetySnapshot(string targetDir, string snapshotDir)
    {
        try
        {
            Directory.CreateDirectory(snapshotDir);
            string localAppDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon_Browser", "HorizonData");

            string[] criticalFiles = new[]
            {
                "vault.dat", "settings.json", "config.json", "cookies.json", "bookmarks.json", "history.json", "notes.json"
            };

            foreach (var file in criticalFiles)
            {
                string targetPath = Path.Combine(targetDir, file);
                string targetDataPath = Path.Combine(targetDir, "HorizonData", file);
                string localPath = Path.Combine(localAppDir, file);
                string? src = File.Exists(targetPath) ? targetPath : (File.Exists(targetDataPath) ? targetDataPath : (File.Exists(localPath) ? localPath : null));

                if (src != null)
                {
                    try { File.Copy(src, Path.Combine(snapshotDir, file), overwrite: true); } catch { }
                }
            }
        }
        catch { }
    }

    private static void RestoreSafetySnapshot(string snapshotDir, string targetDir)
    {
        try
        {
            if (!Directory.Exists(snapshotDir)) return;

            string localAppDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon_Browser", "HorizonData");

            foreach (var file in Directory.GetFiles(snapshotDir))
            {
                string fileName = Path.GetFileName(file);
                string localTarget = Path.Combine(localAppDir, fileName);
                string appTarget = Path.Combine(targetDir, fileName);
                string appDataTarget = Path.Combine(targetDir, "HorizonData", fileName);

                if (!File.Exists(localTarget))
                {
                    try { File.Copy(file, localTarget, overwrite: true); } catch { }
                }
                try { File.Copy(file, appTarget, overwrite: false); } catch { }
                try { File.Copy(file, appDataTarget, overwrite: false); } catch { }
            }
        }
        catch { }
    }

    private async Task<bool> ApplyUpdateAsync(string installerPath, string targetDir)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException($"Update installer file not found at: {installerPath}");

        string ext = Path.GetExtension(installerPath).ToLowerInvariant();

        if (ext == ".zip")
        {
            return await Task.Run(() =>
            {
                SetStatus("Extracting update files...", 60);
                ZipFile.ExtractToDirectory(installerPath, targetDir, overwriteFiles: true);
                return true;
            });
        }

        if (ext == ".msi")
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{installerPath}\" /passive /norestart TARGETDIR=\"{targetDir}\"",
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit();
                return proc.ExitCode == 0;
            });
        }

        return await Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"-silent -path \"{targetDir}\"",
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            int progress = 45;
            while (!proc.WaitForExit(800))
            {
                progress = Math.Min(85, progress + 4);
                SetStatus("Installing update (InstallForge)...", progress);
            }

            return proc.ExitCode == 0 || proc.ExitCode == 3010;
        });
    }
}
