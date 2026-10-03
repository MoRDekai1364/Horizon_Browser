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
            if (!Uri.TryCreate(manifest.StartUrl, UriKind.Absolute, out var startUri) ||
                (startUri.Scheme != Uri.UriSchemeHttp && startUri.Scheme != Uri.UriSchemeHttps)) return null;
            if (!string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase)) return null;
            if (manifest.Icon.Contains("..") || Path.IsPathRooted(manifest.Icon)) manifest.Icon = "";
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
            RaiseChanged();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.InstallAsync");
            result.Errors.Add(ex.Message);
        }
        return result;
    }

    public static List<WebAppManifest> LoadAll()
    {
        var list = new List<WebAppManifest>();
        try
        {
            if (!Directory.Exists(RootDir)) return list;
            foreach (var dir in Directory.GetDirectories(RootDir))
            {
                string id = Path.GetFileName(dir);
                if (!IsValidId(id)) continue;
                var m = Load(id);
                if (m != null) list.Add(m);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.LoadAll");
        }
        return list;
    }

    public static event Action? Changed;

    private static List<WebAppManifest>? _cacheList;
    private static DateTime _cacheUtc = DateTime.MinValue;
    private static readonly object _cacheLock = new();

    private static void RaiseChanged()
    {
        lock (_cacheLock) { _cacheList = null; }
        try { Changed?.Invoke(); }
        catch (Exception ex) { LogService.RecordCrash(ex, "WebAppService.Changed"); }
    }

    private static List<WebAppManifest> LoadAllCached()
    {
        lock (_cacheLock)
        {
            if (_cacheList != null && (DateTime.UtcNow - _cacheUtc).TotalSeconds < 5) return _cacheList;
            _cacheList = LoadAll();
            _cacheUtc = DateTime.UtcNow;
            return _cacheList;
        }
    }

    public static WebAppManifest? FindForUrl(string? url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target)) return null;
            if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) return null;

            WebAppManifest? best = null;
            int bestLen = -1;

            foreach (var m in LoadAllCached())
            {
                string scopeText = string.IsNullOrEmpty(m.Scope) ? m.StartUrl : m.Scope;
                LogService.Debug("webapp", () => $"FindForUrl candidate id={m.Id} scope={scopeText} target={url}");
                if (!Uri.TryCreate(scopeText, UriKind.Absolute, out var scope)) continue;
                if (!string.Equals(scope.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)) continue;
                if (scope.Port != target.Port) continue;
                if (!string.Equals(NormalizeHost(scope.Host), NormalizeHost(target.Host), StringComparison.OrdinalIgnoreCase)) continue;

                string scopePath = scope.AbsolutePath;
                if (!scopePath.EndsWith("/")) scopePath += "/";
                string targetPath = target.AbsolutePath;
                if (!targetPath.EndsWith("/")) targetPath += "/";
                if (!targetPath.StartsWith(scopePath, StringComparison.OrdinalIgnoreCase)) continue;

                if (scopePath.Length > bestLen)
                {
                    best = m;
                    bestLen = scopePath.Length;
                }
            }

            LogService.Debug("webapp", () => $"FindForUrl result url={url} match={(best == null ? "none" : best.Id)} scopeLen={bestLen}");
            return best;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.FindForUrl");
            return null;
        }
    }

    private static string NormalizeHost(string host)
    {
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;
    }

    public static bool Uninstall(string id, List<string> errors)
    {
        try
        {
            if (!IsValidId(id))
            {
                errors.Add("Invalid Web App id.");
                return false;
            }

            var manifest = Load(id);
            WebAppHostService.CloseById(id);

            if (manifest != null)
            {
                DeleteShortcutFile(manifest.DesktopShortcutPath, errors);
                DeleteShortcutFile(manifest.StartMenuShortcutPath, errors);
                RemoveStartMenuFolderIfEmpty();
            }

            string dir = GetAppDir(id);
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception first)
                {
                    LogService.Write(LogTag, "Uninstall folder delete failed once: " + first.Message);
                    System.Threading.Thread.Sleep(300);
                    try { Directory.Delete(dir, true); }
                    catch (Exception second) { errors.Add("Folder: " + second.Message); }
                }
            }

            LogService.Write(LogTag, $"Uninstalled {id}. errors={errors.Count}");
            RaiseChanged();
            return !Directory.Exists(dir);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.Uninstall");
            errors.Add(ex.Message);
            return false;
        }
    }

    public static bool Rename(string id, string newName, List<string> errors)
    {
        try
        {
            var m = Load(id);
            if (m == null)
            {
                errors.Add("Web App not found.");
                return false;
            }

            string name = (newName ?? "").Trim();
            if (name.Length == 0)
            {
                errors.Add("The name is empty.");
                return false;
            }
            if (name.Length > 40) name = name.Substring(0, 40).Trim();

            bool hadDesktop = !string.IsNullOrEmpty(m.DesktopShortcutPath) && File.Exists(m.DesktopShortcutPath);
            bool hadMenu = !string.IsNullOrEmpty(m.StartMenuShortcutPath) && File.Exists(m.StartMenuShortcutPath);

            DeleteShortcutFile(m.DesktopShortcutPath, errors);
            DeleteShortcutFile(m.StartMenuShortcutPath, errors);

            m.Name = name;
            CreateShortcutsFor(m, hadDesktop, hadMenu, errors);
            SaveManifest(m);
            LogService.Write(LogTag, $"Renamed {id} to '{name}'.");
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.Rename");
            errors.Add(ex.Message);
            return false;
        }
    }

    public static bool SetShortcut(string id, bool desktop, bool enable, List<string> errors)
    {
        try
        {
            var m = Load(id);
            if (m == null)
            {
                errors.Add("Web App not found.");
                return false;
            }

            string keepDesktop = m.DesktopShortcutPath;
            string keepMenu = m.StartMenuShortcutPath;
            string current = desktop ? m.DesktopShortcutPath : m.StartMenuShortcutPath;

            DeleteShortcutFile(current, errors);

            if (enable)
            {
                CreateShortcutsFor(m, desktop, !desktop, errors);
                if (desktop) m.StartMenuShortcutPath = keepMenu;
                else m.DesktopShortcutPath = keepDesktop;
            }
            else
            {
                if (desktop) m.DesktopShortcutPath = "";
                else m.StartMenuShortcutPath = "";
            }

            SaveManifest(m);
            if (!desktop && !enable) RemoveStartMenuFolderIfEmpty();

            LogService.Write(LogTag, $"SetShortcut id={id} kind={(desktop ? "desktop" : "startmenu")} enable={enable} errors={errors.Count}");
            return errors.Count == 0;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.SetShortcut");
            errors.Add(ex.Message);
            return false;
        }
    }

    public static bool RecreateShortcuts(string id, bool desktop, bool startMenu, List<string> errors)
    {
        try
        {
            var m = Load(id);
            if (m == null)
            {
                errors.Add("Web App not found.");
                return false;
            }

            DeleteShortcutFile(m.DesktopShortcutPath, errors);
            DeleteShortcutFile(m.StartMenuShortcutPath, errors);
            CreateShortcutsFor(m, desktop, startMenu, errors);
            SaveManifest(m);
            LogService.Write(LogTag, $"Shortcuts recreated for {id}. desktop={desktop} startMenu={startMenu} errors={errors.Count}");
            return errors.Count == 0;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.RecreateShortcuts");
            errors.Add(ex.Message);
            return false;
        }
    }

    private static void SaveManifest(WebAppManifest m)
    {
        string dir = GetAppDir(m.Id);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, ManifestFileName);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(m, JsonOpts), new UTF8Encoding(false));
        File.Move(tmp, path, true);
        RaiseChanged();
    }

    private static void DeleteShortcutFile(string? path, List<string> errors)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            errors.Add("Shortcut: " + ex.Message);
        }
    }

    private static void RemoveStartMenuFolderIfEmpty()
    {
        try
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrEmpty(programs)) return;
            string folder = Path.Combine(programs, StartMenuFolderName);
            if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
                Directory.Delete(folder);
        }
        catch
        {
        }
    }

    private static void CreateShortcutsFor(WebAppManifest m, bool desktop, bool startMenu, List<string> errors)
    {
        m.DesktopShortcutPath = "";
        m.StartMenuShortcutPath = "";

        string exe = ResolveExePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            errors.Add("Horizon executable path could not be resolved: '" + exe + "'");
            return;
        }

        string workDir = Path.GetDirectoryName(exe) ?? "";
        string iconPath = string.IsNullOrEmpty(m.Icon) ? "" : Path.Combine(GetAppDir(m.Id), m.Icon);
        string iconLocation = (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath) ? iconPath : exe) + ",0";
        string host = Uri.TryCreate(m.StartUrl, UriKind.Absolute, out var u) ? u.Host : "app";
        string hostTag = SanitizeFileName(host, "app");
        string baseName = SanitizeFileName(m.Name, hostTag);
        string args = "--webapp-id=" + m.Id;
        string description = "Horizon Web App: " + m.Name;

        if (desktop)
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(folder))
            {
                errors.Add("Desktop folder could not be resolved.");
            }
            else
            {
                string path = ResolveShortcutPath(folder, baseName, hostTag, null);
                if (CreateShortcut(path, exe, args, workDir, iconLocation, description, "Desktop", errors))
                    m.DesktopShortcutPath = path;
            }
        }

        if (startMenu)
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrEmpty(programs))
            {
                errors.Add("Start Menu folder could not be resolved.");
            }
            else
            {
                string folder = Path.Combine(programs, StartMenuFolderName);
                string path = ResolveShortcutPath(folder, baseName, hostTag, null);
                if (CreateShortcut(path, exe, args, workDir, iconLocation, description, "Start Menu", errors))
                    m.StartMenuShortcutPath = path;
            }
        }
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