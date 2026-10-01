using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Horizon.Stealth.Services;

public sealed class WebAppManifest
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string StartUrl { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Icon { get; set; } = "";
    public string InstalledUtc { get; set; } = "";
    public string DesktopShortcutPath { get; set; } = "";
    public string StartMenuShortcutPath { get; set; } = "";
}

public sealed class WebAppInstallResult
{
    public WebAppManifest? Manifest { get; set; }
    public string AppDir { get; set; } = "";
    public bool IsUpdate { get; set; }
    public bool IconOk { get; set; }
    public bool DesktopRequested { get; set; }
    public bool DesktopOk { get; set; }
    public bool StartMenuRequested { get; set; }
    public bool StartMenuOk { get; set; }
    public List<string> Errors { get; } = new();
}

public static class WebAppService
{
    private const string LogTag = "WEBAPP";
    private const string ManifestFileName = "manifest.json";
    private const string IconFileName = "icon.ico";
    private const string StartMenuFolderName = "Horizon Web Apps";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Horizon_Browser", "WebApps");

    public static string GetAppDir(string id) => Path.Combine(RootDir, id);

    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id.Contains("..")) return false;
        foreach (char c in id)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-')) return false;
        }
        return true;
    }

    public static WebAppManifest? Load(string? id)
    {
        try
        {
            if (!IsValidId(id)) return null;
            string path = Path.Combine(GetAppDir(id!), ManifestFileName);
            if (!File.Exists(path)) return null;
            var manifest = JsonSerializer.Deserialize<WebAppManifest>(File.ReadAllText(path, Encoding.UTF8));
            if (manifest == null || string.IsNullOrEmpty(manifest.StartUrl)) return null;
            return manifest;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.Load");
            return null;
        }
    }

    public static async Task<WebAppInstallResult> InstallAsync(
        string startUrl,
        string pageTitle,
        Func<Task<Stream?>>? faviconProvider,
        bool desktop,
        bool startMenu)
    {
        var result = new WebAppInstallResult { DesktopRequested = desktop, StartMenuRequested = startMenu };
        try
        {
            if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                result.Errors.Add("Invalid start URL: " + startUrl);
                LogService.Write(LogTag, "Install aborted. Invalid start URL: " + startUrl);
                return result;
            }

            string id = BuildId(uri);
            string name = BuildName(pageTitle, uri);
            string dir = GetAppDir(id);
            string exe = ResolveExePath();
            string workDir = string.IsNullOrEmpty(exe) ? "" : (Path.GetDirectoryName(exe) ?? "");
            result.AppDir = dir;

            var existing = Load(id);
            result.IsUpdate = existing != null;

            Directory.CreateDirectory(dir);
            LogService.Write(LogTag, $"Install start. id={id} name='{name}' url={uri.AbsoluteUri} update={result.IsUpdate} dir={dir}");

            string iconPath = Path.Combine(dir, IconFileName);
            await TryWriteIconAsync(faviconProvider, iconPath, result.Errors);
            result.IconOk = File.Exists(iconPath);
            string iconLocation = (result.IconOk ? iconPath : exe) + ",0";

            string? desktopPath = null;
            string? menuPath = null;

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                string msg = "Horizon executable path could not be resolved: '" + exe + "'";
                result.Errors.Add(msg);
                LogService.Write(LogTag, msg);
            }
            else
            {
                string hostTag = SanitizeFileName(uri.Host, "app");
                string baseName = SanitizeFileName(name, hostTag);
                string args = "--webapp-id=" + id;
                string description = "Horizon Web App: " + name;

                if (desktop)
                {
                    string folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if (string.IsNullOrEmpty(folder))
                    {
                        result.Errors.Add("Desktop folder could not be resolved.");
                    }
                    else
                    {
                        string path = ResolveShortcutPath(folder, baseName, hostTag, existing?.DesktopShortcutPath);
                        result.DesktopOk = CreateShortcut(path, exe, args, workDir, iconLocation, description, "Desktop", result.Errors);
                        if (result.DesktopOk) desktopPath = path;
                    }
                }

                if (startMenu)
                {
                    string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    if (string.IsNullOrEmpty(programs))
                    {
                        result.Errors.Add("Start Menu folder could not be resolved.");
                    }
                    else
                    {
                        string folder = Path.Combine(programs, StartMenuFolderName);
                        string path = ResolveShortcutPath(folder, baseName, hostTag, existing?.StartMenuShortcutPath);
                        result.StartMenuOk = CreateShortcut(path, exe, args, workDir, iconLocation, description, "Start Menu", result.Errors);
                        if (result.StartMenuOk) menuPath = path;
                    }
                }
            }

            var manifest = new WebAppManifest
            {
                Id = id,
                Name = name,
                StartUrl = uri.AbsoluteUri,
                Scope = uri.GetLeftPart(UriPartial.Authority) + "/",
                Icon = result.IconOk ? IconFileName : "",
                InstalledUtc = existing?.InstalledUtc ?? DateTime.UtcNow.ToString("o"),
                DesktopShortcutPath = desktopPath ?? (desktop ? "" : existing?.DesktopShortcutPath ?? ""),
                StartMenuShortcutPath = menuPath ?? (startMenu ? "" : existing?.StartMenuShortcutPath ?? "")
            };

            string manifestPath = Path.Combine(dir, ManifestFileName);
            string tmp = manifestPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, JsonOpts), new UTF8Encoding(false));
            File.Move(tmp, manifestPath, true);

            result.Manifest = manifest;
            LogService.Write(LogTag, $"Install done. id={id} icon={result.IconOk} desktop={result.DesktopOk} startMenu={result.StartMenuOk} errors={result.Errors.Count}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.InstallAsync");
            result.Errors.Add(ex.Message);
        }
        return result;
    }

    private static string BuildId(Uri uri)
    {
        var sb = new StringBuilder();
        foreach (char c in uri.Host.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-') sb.Append(c);
        }
        string host = sb.ToString();
        if (host.StartsWith("www.")) host = host.Substring(4);
        host = host.Trim('.', '-');
        if (host.Length > 40) host = host.Substring(0, 40).Trim('.', '-');
        if (host.Length == 0) host = "app";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri.ToLowerInvariant()));
        return host + "-" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private static string BuildName(string? title, Uri uri)
    {
        string host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host.Substring(4);

        string name = (title ?? "").Trim();
        if (name.Length > 0)
        {
            foreach (var sep in new[] { " - ", " | ", " \u2013 ", " \u2014 " })
            {
                int i = name.IndexOf(sep, StringComparison.Ordinal);
                if (i > 0) name = name.Substring(0, i);
            }
            name = name.Trim();
        }
        if (name.Length == 0) name = host;
        if (name.Length > 40) name = name.Substring(0, 40).Trim();
        return name;
    }

    private static string SanitizeFileName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 || char.IsControl(c) ? '_' : c);
        }
        string s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length == 0) s = fallback;
        if (Array.IndexOf(ReservedNames, s.ToUpperInvariant()) >= 0) s = "_" + s;
        return s;
    }

    private static bool SamePath(string a, string? b)
        => !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string ResolveShortcutPath(string folder, string baseName, string hostTag, string? ownPath)
    {
        string first = Path.Combine(folder, baseName + ".lnk");
        if (!File.Exists(first) || SamePath(first, ownPath)) return first;

        string second = Path.Combine(folder, baseName + " (" + hostTag + ").lnk");
        if (!File.Exists(second) || SamePath(second, ownPath)) return second;

        for (int i = 2; i < 100; i++)
        {
            string candidate = Path.Combine(folder, baseName + " (" + hostTag + " " + i + ").lnk");
            if (!File.Exists(candidate) || SamePath(candidate, ownPath)) return candidate;
        }
        return second;
    }

    private static string ResolveExePath()
    {
        string p = Environment.ProcessPath ?? "";
        if (!string.IsNullOrEmpty(p) &&
            !string.Equals(Path.GetFileNameWithoutExtension(p), "dotnet", StringComparison.OrdinalIgnoreCase))
            return p;

        string asm = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrEmpty(asm)) return p;
        string candidate = Path.ChangeExtension(asm, ".exe");
        return File.Exists(candidate) ? candidate : p;
    }

    private static bool CreateShortcut(
        string lnkPath, string target, string args, string workDir,
        string iconLocation, string description, string label, List<string> errors)
    {
        object? shell = null;
        object? link = null;
        try
        {
            string? folder = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                errors.Add(label + " shortcut: WScript.Shell is not available.");
                LogService.Write(LogTag, label + " shortcut failed: WScript.Shell not available.");
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            dynamic ws = shell!;
            link = ws.CreateShortcut(lnkPath);
            dynamic sc = link!;
            sc.TargetPath = target;
            sc.Arguments = args;
            sc.WorkingDirectory = workDir;
            sc.IconLocation = iconLocation;
            sc.Description = description;
            sc.WindowStyle = 1;
            sc.Save();

            bool ok = File.Exists(lnkPath);
            if (ok)
            {
                LogService.Write(LogTag, label + " shortcut created: " + lnkPath);
            }
            else
            {
                errors.Add(label + " shortcut: file was not created at " + lnkPath);
                LogService.Write(LogTag, label + " shortcut failed: file missing after save: " + lnkPath);
            }
            return ok;
        }
        catch (Exception ex)
        {
            errors.Add(label + " shortcut: " + ex.Message);
            LogService.RecordCrash(ex, "WebAppService.CreateShortcut." + label);
            return false;
        }
        finally
        {
            if (link != null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static async Task<bool> TryWriteIconAsync(Func<Task<Stream?>>? provider, string iconPath, List<string> errors)
    {
        if (provider == null)
        {
            LogService.Write(LogTag, "No favicon provider. Using Horizon icon.");
            return false;
        }

        try
        {
            byte[] raw;
            using (var src = await provider())
            {
                if (src == null)
                {
                    LogService.Write(LogTag, "Favicon unavailable. Using Horizon icon.");
                    return false;
                }
                using var buffer = new MemoryStream();
                await src.CopyToAsync(buffer);
                raw = buffer.ToArray();
            }

            if (raw.Length == 0)
            {
                LogService.Write(LogTag, "Favicon stream empty. Using Horizon icon.");
                return false;
            }

            BitmapSource frame;
            using (var decodeStream = new MemoryStream(raw))
            {
                var decoder = BitmapDecoder.Create(decodeStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame = decoder.Frames[0];
            }

            if (frame.PixelWidth < 16 || frame.PixelHeight < 16)
            {
                LogService.Write(LogTag, $"Favicon too small ({frame.PixelWidth}x{frame.PixelHeight}). Using Horizon icon.");
                return false;
            }

            var images = new List<(int Size, byte[] Png)>();
            foreach (int size in new[] { 256, 48, 32, 16 })
            {
                images.Add((size, RenderSquarePng(frame, size)));
            }

            byte[] ico = BuildIco(images);
            string tmp = iconPath + ".tmp";
            File.WriteAllBytes(tmp, ico);
            File.Move(tmp, iconPath, true);
            LogService.Write(LogTag, $"Icon written ({frame.PixelWidth}x{frame.PixelHeight} source, {ico.Length} bytes): {iconPath}");
            return true;
        }
        catch (Exception ex)
        {
            errors.Add("Icon: " + ex.Message);
            LogService.RecordCrash(ex, "WebAppService.TryWriteIcon");
            return false;
        }
    }

    private static byte[] RenderSquarePng(BitmapSource src, int size)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            double scale = Math.Min((double)size / src.PixelWidth, (double)size / src.PixelHeight);
            double w = src.PixelWidth * scale;
            double h = src.PixelHeight * scale;
            dc.DrawImage(src, new Rect((size - w) / 2, (size - h) / 2, w, h));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static byte[] BuildIco(List<(int Size, byte[] Png)> images)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)images.Count);

        int offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            byte dim = (byte)(size >= 256 ? 0 : size);
            bw.Write(dim);
            bw.Write(dim);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(png.Length);
            bw.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images) bw.Write(png);

        bw.Flush();
        return ms.ToArray();
    }
}