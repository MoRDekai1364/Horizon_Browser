using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Horizon.Updater;

public partial class MainWindow : Window
{
    private readonly string _installerPath;
    private readonly string _targetDir;
    private readonly int _parentPid;
    private readonly bool _silent;

    public MainWindow(string installerPath, string targetDir, int parentPid, bool silent)
    {
        InitializeComponent();
        _installerPath = installerPath;
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
        await RunUpdateSequenceAsync();
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
        });
    }

    private static void CreateSafetySnapshot(string targetDir, string snapshotDir)
    {
        try
        {
            Directory.CreateDirectory(snapshotDir);
            string localAppDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon_Browser");

            string[] criticalFiles = new[]
            {
                "vault.dat", "settings.json", "cookies.json", "bookmarks.json", "history.json", "notes.json"
            };

            foreach (var file in criticalFiles)
            {
                string targetPath = Path.Combine(targetDir, file);
                string localPath = Path.Combine(localAppDir, file);
                string? src = File.Exists(targetPath) ? targetPath : (File.Exists(localPath) ? localPath : null);

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
                "Horizon_Browser");

            foreach (var file in Directory.GetFiles(snapshotDir))
            {
                string fileName = Path.GetFileName(file);
                string localTarget = Path.Combine(localAppDir, fileName);
                string appTarget = Path.Combine(targetDir, fileName);

                // Ensure always available in LocalAppData
                if (!File.Exists(localTarget))
                {
                    try { File.Copy(file, localTarget, overwrite: true); } catch { }
                }

                // If originally present in app folder, restore it there too
                try { File.Copy(file, appTarget, overwrite: false); } catch { }
            }
        }
        catch { }
    }

    private async Task<bool> ApplyUpdateAsync(string installerPath, string targetDir)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException($"Update installer file not found at: {installerPath}");

        string ext = Path.GetExtension(installerPath).ToLowerInvariant();

        // 1. Direct zip extraction
        if (ext == ".zip")
        {
            return await Task.Run(() =>
            {
                SetStatus("Extracting update files...", 60);
                ZipFile.ExtractToDirectory(installerPath, targetDir, overwriteFiles: true);
                return true;
            });
        }

        // 2. MSI installer
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

        // 3. EXE installer (InstallForge / Setup)
        return await Task.Run(() =>
        {
            // InstallForge accepts -silent -path "<TargetDir>"
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

            return proc.ExitCode == 0 || proc.ExitCode == 3010; // 3010 = reboot pending
        });
    }
}
