using System.IO;
using System.Timers;

namespace Horizon.Stealth.Services;

public static class FluxJanitorService
{
    private static System.Timers.Timer? _scanTimer;
    private static bool _isScanning = false;

    public static event Action? OnDownloadCompleted;

    public static void NotifyDownloadCompleted()
    {
        OnDownloadCompleted?.Invoke();
    }

    public static void Initialize()
    {
        LogService.Write("JANITOR", "Initializing Service...");
        _scanTimer = new System.Timers.Timer(60000); // Check every 1 minute
        _scanTimer.Elapsed += OnScan;
        _scanTimer.Start();
        LogService.Write("JANITOR", "Timer started. Interval: 60s.");
    }

    private static void OnScan(object? sender, ElapsedEventArgs e)
    {
        if (_isScanning) return;
        _isScanning = true;

        var settings = SettingsService.Current;
        
        if (string.IsNullOrEmpty(settings.DownloadsPath) || !Directory.Exists(settings.DownloadsPath)) 
        {
             _isScanning = false;
             return;
        }

        try
        {
            LogService.Write("JANITOR", $"Starting scan of: {settings.DownloadsPath}");
            var files = Directory.GetFiles(settings.DownloadsPath);
            LogService.Write("JANITOR", $"Found {files.Length} files.");

            foreach (var file in files)
            {
                var fi = new FileInfo(file);
                bool shouldDelete = false;
                string reason = "";

                

                if (settings.JanitorSmallFileMb > 0)
                {
                    long limitBytes = settings.JanitorSmallFileMb * 1024 * 1024;
                    if (fi.Length < limitBytes)
                    {
                        shouldDelete = true;
                        reason = $"Too Small (< {settings.JanitorSmallFileMb}MB)";
                    }
                }

                if (!shouldDelete && settings.JanitorLargeFileGb > 0)
                {
                    long limitBytes = (long)settings.JanitorLargeFileGb * 1024 * 1024 * 1024;
                    if (fi.Length > limitBytes)
                    {
                        shouldDelete = true;
                        reason = $"Too Large (> {settings.JanitorLargeFileGb}GB)";
                    }
                }

                if (!shouldDelete && settings.JanitorRetentionMin > 0)
                {
                    if (DateTime.Now - fi.CreationTime > TimeSpan.FromMinutes(settings.JanitorRetentionMin))
                    {
                        shouldDelete = true;
                        reason = $"Expired (> {settings.JanitorRetentionMin} min)";
                    }
                }

                if (shouldDelete)
                {
                    try 
                    {
                        LogService.Write("JANITOR", $"EXECUTING DELETE: {fi.Name} -> Reason: {reason}");
                        File.Delete(file);
                        LogService.Write("JANITOR", "Delete Successful.");
                    }
                    catch (Exception deleteEx)
                    { 
                        LogService.Write("JANITOR-ERR", $"Failed to delete {fi.Name}: {deleteEx.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "FluxJanitor Scan Cycle");
        }
        finally
        {
            _isScanning = false;
        }
    }
}