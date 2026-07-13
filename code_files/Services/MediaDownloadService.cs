using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Horizon.Stealth.Services;

public sealed class MediaDownloadService : IDisposable
{
    private static readonly HttpClient _http;
    private static readonly string _appRoot;
    private static readonly string _ytDlpPath;
    private static readonly string _logsDir;
    private const long MinFreeBytes = 524_288_000L;

    static MediaDownloadService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.Add("User-Agent", "Horizon-Browser/1.0");
        _appRoot = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location)
            ?? AppDomain.CurrentDomain.BaseDirectory;
        _ytDlpPath = Path.Combine(_appRoot, "yt-dlp.exe");
        _logsDir   = Path.Combine(_appRoot, "logs");
    }

    private readonly StringBuilder _log = new();
    private string _tempLogPath = string.Empty;
    private bool _disposed;

    public static async Task<bool> EnsureYtDlpAsync(IProgress<string>? status = null)
    {
        if (File.Exists(_ytDlpPath))
            return true;

        status?.Report("yt-dlp.exe not found — fetching latest release from GitHub...");
        LogService.Write("MEDIA", "yt-dlp.exe missing. Fetching from GitHub.");

        try
        {
            var req = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");

            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            string tagName    = doc.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
            string? dlUrl     = null;

            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == "yt-dlp.exe")
                {
                    dlUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(dlUrl))
                throw new InvalidOperationException("yt-dlp.exe asset not found in release manifest.");

            status?.Report($"Downloading yt-dlp {tagName}...");
            LogService.Write("MEDIA", $"Downloading yt-dlp {tagName} from {dlUrl}");

            byte[] bytes = await _http.GetByteArrayAsync(dlUrl);
            await File.WriteAllBytesAsync(_ytDlpPath, bytes);

            status?.Report($"yt-dlp {tagName} ready.");
            LogService.Write("MEDIA", $"yt-dlp saved to {_ytDlpPath} ({bytes.Length / 1024} KB).");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Write("MEDIA", $"yt-dlp bootstrap failed: {ex.GetType().Name}: {ex.Message}");
            status?.Report($"Bootstrap error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DownloadAsync(
        string url,
        Action<string>? onProgressBar = null,
        CancellationToken ct = default)
    {
        InitLog();
        Log($"[START]  URL: {url}");
        Log($"[ENV]    AppRoot: {_appRoot}");
        Log($"[ENV]    yt-dlp: {_ytDlpPath}");

        try
        {
            bool ytDlpReady = await EnsureYtDlpAsync(
                new Progress<string>(s => Log($"[BOOTSTRAP] {s}")));

            if (!ytDlpReady)
            {
                Log("[FATAL] Could not obtain yt-dlp.exe. Aborting.");
                return false;
            }

            string? outputDir = ResolveOutputDirectory();
            if (outputDir == null)
            {
                Log("[FATAL] No writable output directory found. User cancelled.");
                return false;
            }

            Log($"[OUTPUT] Directory: {outputDir}");
            Directory.CreateDirectory(outputDir);

            bool success = await RunYtDlpAsync(url, outputDir, onProgressBar, ct);
            Log($"[END]    Result: {(success ? "SUCCESS" : "FAILED")}");
            return success;
        }
        catch (Exception ex)
        {
            Log($"[EXCEPTION] {ex.GetType().Name}: {ex.Message}");
            Log($"[STACK]     {ex.StackTrace}");
            return false;
        }
        finally
        {
            await FinalizeLogAsync();
        }
    }

    private string? ResolveOutputDirectory()
    {
        string candidate1 = Path.Combine(_appRoot, "temp_downloads");
        Log($"[DISK] Candidate 1 (project temp): {candidate1}");
        long free1 = GetFreeSpaceMB(candidate1);
        if (HasSufficientSpace(candidate1))
        {
            Log($"[DISK] Candidate 1 accepted ({free1} MB free).");
            return candidate1;
        }
        Log($"[DISK] Candidate 1 rejected ({free1} MB free, need {MinFreeBytes / 1024 / 1024} MB).");

        string candidate2 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Log($"[DISK] Candidate 2 (OS Downloads): {candidate2}");
        long free2 = GetFreeSpaceMB(candidate2);
        if (HasSufficientSpace(candidate2))
        {
            Log($"[DISK] Candidate 2 accepted ({free2} MB free).");
            return candidate2;
        }
        Log($"[DISK] Candidate 2 rejected ({free2} MB free). Prompting user.");

        string? userDir = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select download destination — both default locations are full"
            };
            if (dlg.ShowDialog() == true)
                userDir = dlg.FolderName;
        });

        if (!string.IsNullOrEmpty(userDir))
            Log($"[DISK] User selected: {userDir}");
        else
            Log("[DISK] User cancelled folder selection.");

        return userDir;
    }

    private static bool HasSufficientSpace(string path)
    {
        try
        {
            string root = Path.GetPathRoot(path) ?? path;
            return new DriveInfo(root).AvailableFreeSpace >= MinFreeBytes;
        }
        catch { return false; }
    }

    private static long GetFreeSpaceMB(string path)
    {
        try
        {
            string root = Path.GetPathRoot(path) ?? path;
            return new DriveInfo(root).AvailableFreeSpace / (1024 * 1024);
        }
        catch { return -1; }
    }

    private async Task<bool> RunYtDlpAsync(
        string url,
        string outputDir,
        Action<string>? onProgressBar,
        CancellationToken ct)
    {
        string template = Path.Combine(outputDir, "%(title)s.%(ext)s");
        string args     = $"--no-playlist --newline -o \"{template}\" \"{url}\"";
        Log($"[EXEC] yt-dlp {args}");

        var psi = new ProcessStartInfo
        {
            FileName               = _ytDlpPath,
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        var progressRegex = new Regex(
            @"\[download\]\s+(\d+(?:\.\d+)?)%",
            RegexOptions.Compiled);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            Log($"[OUT] {e.Data}");

            var match = progressRegex.Match(e.Data);
            if (match.Success &&
                double.TryParse(
                    match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double pct))
            {
                onProgressBar?.Invoke(RenderProgressBar(pct));
            }
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Log($"[ERR] {e.Data}");
        };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);

            Log($"[EXIT] Code: {proc.ExitCode}");
            return proc.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            Log("[CANCELLED] Download cancelled.");
            try { proc.Kill(entireProcessTree: true); } catch { }
            return false;
        }
        catch (Exception ex)
        {
            Log($"[PROC_EX] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string RenderProgressBar(double percent, int width = 20)
    {
        int filled = Math.Clamp((int)Math.Round(percent / 100.0 * width), 0, width);
        return $"[{new string('#', filled)}{new string('.', width - filled)}] {percent:F0}%";
    }

    private void InitLog()
    {
        _log.Clear();
        string ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"horizon_media_{ts}.log");
        Log($"[LOG] Session started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Log($"[LOG] Temp path: {_tempLogPath}");
    }

    private void Log(string message)
    {
        _log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        LogService.Write("MEDIA", message);
    }

    private async Task FinalizeLogAsync()
    {
        Log($"[LOG] Session ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        string content = _log.ToString();

        try
        {
            await File.WriteAllTextAsync(_tempLogPath, content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LogService.Write("MEDIA", $"Temp log write failed: {ex.Message}");
        }

        string finalLogPath = _tempLogPath;
        try
        {
            Directory.CreateDirectory(_logsDir);
            string destPath = Path.Combine(_logsDir, Path.GetFileName(_tempLogPath));
            await File.WriteAllTextAsync(destPath, content, Encoding.UTF8);
            finalLogPath = destPath;
            LogService.Write("MEDIA", $"Log copied to {finalLogPath}");
        }
        catch (Exception ex)
        {
            LogService.Write("MEDIA", $"Log copy to logs dir failed: {ex.Message}. Keeping temp path.");
        }

        if (!File.Exists(finalLogPath)) return;

        string pathCapture = finalLogPath;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var choice = MessageBox.Show(
                $"Media download session complete.\n\nLog: {pathCapture}\n\nOpen log file?",
                "Horizon — Media Download",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (choice == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(pathCapture) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open log file:\n{ex.Message}",
                        "Horizon", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}