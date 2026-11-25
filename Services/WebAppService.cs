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
    public int IconVersion { get; set; } = 0;
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
                StartMenuShortcutPath = menuPath ?? (startMenu ? "" : existing?.StartMenuShortcutPath ?? ""),
                IconVersion = result.IconOk ? CurrentIconPipelineVersion : 0
            };

            string manifestPath = Path.Combine(dir, ManifestFileName);
            string tmp = manifestPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, JsonOpts), new UTF8Encoding(false));
            File.Move(tmp, manifestPath, true);

            result.Manifest = manifest;
            RegisterUninstallEntry(manifest);
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

    private const string UninstallKeyRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private static bool _uninstallEntriesSynced;

    private static string UninstallKeyPath(string id) => UninstallKeyRoot + "\\HorizonWebApp_" + id;

    public static void RegisterUninstallEntry(WebAppManifest m)
    {
        try
        {
            string exe = ResolveExePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;
            string iconPath = string.IsNullOrEmpty(m.Icon) ? "" : Path.Combine(GetAppDir(m.Id), m.Icon);
            string icon = (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath) ? iconPath : exe) + ",0";
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKeyPath(m.Id));
            if (key == null) return;
            key.SetValue("DisplayName", m.Name);
            key.SetValue("DisplayIcon", icon);
            key.SetValue("Publisher", "Horizon");
            key.SetValue("UninstallString", "\"" + exe + "\" --webapp-uninstall=" + m.Id);
            key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
            LogService.Write(LogTag, "UNINSTALLREG registered " + m.Id + " -> " + exe);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.RegisterUninstallEntry");
        }
    }

    public static void UnregisterUninstallEntry(string id)
    {
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath(id), false); }
        catch (Exception ex) { LogService.RecordCrash(ex, "WebAppService.UnregisterUninstallEntry"); }
    }

    private static void SyncUninstallEntries(List<WebAppManifest> list)
    {
        if (_uninstallEntriesSynced) return;
        _uninstallEntriesSynced = true;
        foreach (var m in list) RegisterUninstallEntry(m);
        try
        {
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallKeyRoot);
            if (root == null) return;
            var live = new HashSet<string>(list.Select(x => "HorizonWebApp_" + x.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var sub in root.GetSubKeyNames())
            {
                if (sub.StartsWith("HorizonWebApp_", StringComparison.OrdinalIgnoreCase) && !live.Contains(sub))
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyRoot + "\\" + sub, false);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.SyncUninstallEntries");
        }
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
            SyncUninstallEntries(list);
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
            UnregisterUninstallEntry(id);

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
            RegisterUninstallEntry(m);
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

    private const int CurrentIconPipelineVersion = 2;
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static readonly System.Net.Http.HttpClient _iconHttp = CreateIconHttpClient();

    private static System.Net.Http.HttpClient CreateIconHttpClient()
    {
        var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = true };
        var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36");
        return client;
    }

    private sealed class WebAppSyncState
    {
        public string BrowserVersion { get; set; } = "0.0.0.0";
        public string ExePath { get; set; } = "";
        public long ExeWriteUtcTicks { get; set; }
        public int IconPipelineVersion { get; set; }
    }

    private static string SyncStatePath => Path.Combine(RootDir, "_sync_state.json");

    /// <summary>
    /// Loads all WebApps, syncs uninstall entries, and automatically upgrades all existing WebApps
    /// (high-res icons + refreshed shortcuts + uninstall entries) whenever Horizon Browser is
    /// updated or reinstalled with the same or newer version, or if any WebApp still has a legacy low-res icon.
    /// </summary>
    public static async Task SyncAllWebAppsIfNeededAsync()
    {
        try
        {
            var list = LoadAll();
            if (list.Count == 0) return;

            string currentExe = ResolveExePath();
            long currentTicks = (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                ? File.GetLastWriteTimeUtc(currentExe).Ticks
                : 0;
            Version currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 8, 0, 0);

            WebAppSyncState? prev = null;
            try
            {
                if (File.Exists(SyncStatePath))
                    prev = JsonSerializer.Deserialize<WebAppSyncState>(File.ReadAllText(SyncStatePath, Encoding.UTF8));
            }
            catch { }

            Version prevVer = Version.TryParse(prev?.BrowserVersion, out var parsed) ? parsed : new Version(0, 0, 0, 0);
            bool isSameOrNewer = currentVer >= prevVer;
            bool versionOrBinaryChanged =
                prev == null ||
                currentVer > prevVer ||
                !string.Equals(prev.ExePath, currentExe, StringComparison.OrdinalIgnoreCase) ||
                prev.ExeWriteUtcTicks != currentTicks ||
                prev.IconPipelineVersion < CurrentIconPipelineVersion;

            bool anyLegacyIcon = list.Any(m => m.IconVersion < CurrentIconPipelineVersion);

            if (!isSameOrNewer || (!versionOrBinaryChanged && !anyLegacyIcon))
                return;

            LogService.Write(LogTag, $"Auto-upgrading {list.Count} installed WebApp(s) (ver={currentVer}, prev={prevVer}, legacyIcons={anyLegacyIcon})...");
            bool anyShellChanged = false;

            foreach (var m in list)
            {
                try
                {
                    if (!Uri.TryCreate(m.StartUrl, UriKind.Absolute, out var startUri)) continue;
                    string dir = GetAppDir(m.Id);
                    string iconPath = Path.Combine(dir, IconFileName);
                    var errors = new List<string>();

                    // Upgrade icon to high-res if legacy or missing
                    if (m.IconVersion < CurrentIconPipelineVersion || !File.Exists(iconPath))
                    {
                        bool iconOk = await TryWriteIconAsync(() => FetchHighResIconByUrlAsync(startUri), iconPath, errors);
                        if (iconOk)
                        {
                            m.Icon = IconFileName;
                            m.IconVersion = CurrentIconPipelineVersion;
                            anyShellChanged = true;
                        }
                    }

                    // Refresh existing shortcuts so they point to the current Horizon.Browser.exe and crisp icon.ico
                    bool hasDesktop = !string.IsNullOrEmpty(m.DesktopShortcutPath) && File.Exists(m.DesktopShortcutPath);
                    bool hasMenu = !string.IsNullOrEmpty(m.StartMenuShortcutPath) && File.Exists(m.StartMenuShortcutPath);
                    if (hasDesktop || hasMenu)
                    {
                        string workDir = Path.GetDirectoryName(currentExe) ?? "";
                        string iconLoc = (!string.IsNullOrEmpty(m.Icon) && File.Exists(iconPath) ? iconPath : currentExe) + ",0";
                        string args = "--webapp-id=" + m.Id;
                        string desc = "Horizon Web App: " + m.Name;

                        if (hasDesktop)
                            CreateShortcut(m.DesktopShortcutPath, currentExe, args, workDir, iconLoc, desc, "Desktop", errors);
                        if (hasMenu)
                            CreateShortcut(m.StartMenuShortcutPath, currentExe, args, workDir, iconLoc, desc, "Start Menu", errors);
                        anyShellChanged = true;
                    }

                    SaveManifest(m);
                    RegisterUninstallEntry(m);
                }
                catch (Exception ex)
                {
                    LogService.RecordCrash(ex, "WebAppService.SyncWebApp." + m.Id);
                }
            }

            if (anyShellChanged)
            {
                try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
                catch { }
            }

            try
            {
                Directory.CreateDirectory(RootDir);
                var state = new WebAppSyncState
                {
                    BrowserVersion = currentVer.ToString(),
                    ExePath = currentExe,
                    ExeWriteUtcTicks = currentTicks,
                    IconPipelineVersion = CurrentIconPipelineVersion
                };
                string tmp = SyncStatePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOpts), new UTF8Encoding(false));
                File.Move(tmp, SyncStatePath, true);
            }
            catch { }

            LogService.Write(LogTag, $"Auto-upgrade complete for {list.Count} WebApp(s).");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppService.SyncAllWebAppsIfNeededAsync");
        }
    }

    /// <summary>
    /// Extracts the highest-resolution icon available from an active WebView2 page (apple-touch-icon,
    /// large &lt;link rel="icon"&gt;, web manifest icons), falling back to URL/high-res icon APIs and CoreWebView2.GetFaviconAsync.
    /// </summary>
    public static async Task<Stream?> FetchHighResIconFromPageAsync(
        Microsoft.Web.WebView2.Core.CoreWebView2 core,
        Uri pageUri)
    {
        var candidateUrls = new List<string>();
        try
        {
            const string js = @"(async () => {
                const out = [];
                const add = (href, sz) => {
                    if (!href) return;
                    try { out.push({ url: new URL(href, location.href).href, size: sz || 0 }); } catch {}
                };
                const parseSize = (s) => {
                    if (!s) return 0;
                    let best = 0;
                    for (const m of s.matchAll(/(\d+)\s*x\s*(\d+)/gi)) {
                        const v = Math.max(parseInt(m[1], 10) || 0, parseInt(m[2], 10) || 0);
                        if (v > best) best = v;
                    }
                    return best;
                };
                for (const l of document.querySelectorAll('link[rel*=""icon"" i], link[rel=""apple-touch-icon"" i], link[rel=""apple-touch-icon-precomposed"" i]')) {
                    const rel = (l.getAttribute('rel') || '').toLowerCase();
                    let sz = parseSize(l.getAttribute('sizes'));
                    if (sz === 0 && rel.includes('apple-touch-icon')) sz = 180;
                    add(l.getAttribute('href'), sz);
                }
                const mf = document.querySelector('link[rel=""manifest"" i]');
                if (mf && mf.getAttribute('href')) {
                    try {
                        const mfUrl = new URL(mf.getAttribute('href'), location.href).href;
                        const resp = await fetch(mfUrl, { credentials: 'include' });
                        if (resp.ok) {
                            const data = await resp.json();
                            if (Array.isArray(data.icons)) {
                                for (const ic of data.icons) {
                                    if (ic && ic.src) add(new URL(ic.src, mfUrl).href, parseSize(ic.sizes) || 192);
                                }
                            }
                        }
                    } catch {}
                }
                out.sort((a, b) => b.size - a.size);
                return [...new Set(out.map(x => x.url))].slice(0, 8);
            })()";

            string raw = await core.ExecuteScriptAsync(js);
            if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(raw);
                if (parsed != null) candidateUrls.AddRange(parsed);
            }
        }
        catch { }

        var bestFromUrls = await DownloadBestIconStreamAsync(candidateUrls, pageUri);
        if (bestFromUrls != null) return bestFromUrls;

        var byUrl = await FetchHighResIconByUrlAsync(pageUri);
        if (byUrl != null) return byUrl;

        try
        {
            return await core.GetFaviconAsync(Microsoft.Web.WebView2.Core.CoreWebView2FaviconImageFormat.Png);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Headless high-resolution icon fetcher for background upgrades of existing WebApps.
    /// </summary>
    public static async Task<Stream?> FetchHighResIconByUrlAsync(Uri startUri)
    {
        var candidates = new List<string>();
        try
        {
            string html = await _iconHttp.GetStringAsync(startUri);
            var linkMatches = System.Text.RegularExpressions.Regex.Matches(
                html,
                @"<link\b[^>]*rel\s*=\s*[""']([^""']*(?:apple-touch-icon|icon|manifest)[^""']*)[""'][^>]*>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var scored = new List<(string Url, int Score)>();
            string? manifestUrl = null;

            foreach (System.Text.RegularExpressions.Match m in linkMatches)
            {
                string tag = m.Value;
                string rel = m.Groups[1].Value.ToLowerInvariant();
                var hrefM = System.Text.RegularExpressions.Regex.Match(tag, @"href\s*=\s*[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!hrefM.Success) continue;
                if (!Uri.TryCreate(startUri, hrefM.Groups[1].Value, out var abs)) continue;

                if (rel.Contains("manifest"))
                {
                    manifestUrl ??= abs.AbsoluteUri;
                    continue;
                }

                int score = rel.Contains("apple-touch-icon") ? 180 : 32;
                var sizeM = System.Text.RegularExpressions.Regex.Match(tag, @"sizes\s*=\s*[""'](\d+)x(\d+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (sizeM.Success && int.TryParse(sizeM.Groups[1].Value, out int px)) score = Math.Max(score, px);
                scored.Add((abs.AbsoluteUri, score));
            }

            if (!string.IsNullOrEmpty(manifestUrl))
            {
                try
                {
                    string mfJson = await _iconHttp.GetStringAsync(manifestUrl);
                    using var doc = JsonDocument.Parse(mfJson);
                    if (doc.RootElement.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Array)
                    {
                        var mfBase = new Uri(manifestUrl);
                        foreach (var ic in icons.EnumerateArray())
                        {
                            if (!ic.TryGetProperty("src", out var srcEl)) continue;
                            string? src = srcEl.GetString();
                            if (string.IsNullOrWhiteSpace(src) || !Uri.TryCreate(mfBase, src, out var abs)) continue;
                            int score = 192;
                            if (ic.TryGetProperty("sizes", out var szEl))
                            {
                                var sm = System.Text.RegularExpressions.Regex.Match(szEl.GetString() ?? "", @"(\d+)x(\d+)");
                                if (sm.Success && int.TryParse(sm.Groups[1].Value, out int px)) score = px;
                            }
                            scored.Add((abs.AbsoluteUri, score));
                        }
                    }
                }
                catch { }
            }

            foreach (var item in scored.OrderByDescending(x => x.Score).Select(x => x.Url).Distinct(StringComparer.OrdinalIgnoreCase).Take(6))
                candidates.Add(item);
        }
        catch { }

        // Always append reliable high-res fallbacks
        string host = startUri.Host;
        candidates.Add($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=256");
        candidates.Add($"https://icons.duckduckgo.com/ip3/{Uri.EscapeDataString(host)}.ico");
        candidates.Add($"{startUri.Scheme}://{startUri.Authority}/apple-touch-icon.png");
        candidates.Add($"{startUri.Scheme}://{startUri.Authority}/favicon.ico");

        return await DownloadBestIconStreamAsync(candidates.Distinct(StringComparer.OrdinalIgnoreCase), startUri);
    }

    private static async Task<Stream?> DownloadBestIconStreamAsync(IEnumerable<string> urls, Uri referer)
    {
        byte[]? bestBytes = null;
        int bestPixels = 0;

        foreach (string url in urls)
        {
            if (string.IsNullOrWhiteSpace(url) || url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                req.Headers.Referrer = referer;
                using var resp = await _iconHttp.SendAsync(req);
                if (!resp.IsSuccessStatusCode) continue;

                byte[] data = await resp.Content.ReadAsByteArrayAsync();
                if (data.Length < 64 || data.Length > 4 * 1024 * 1024) continue;

                int maxDim = ProbeMaxBitmapDimension(data);
                if (maxDim <= 0) continue;

                if (maxDim > bestPixels)
                {
                    bestPixels = maxDim;
                    bestBytes = data;
                }

                // 192px+ is already crisp for 256px Windows ICO — stop early
                if (bestPixels >= 192) break;
            }
            catch { }
        }

        return bestBytes != null ? new MemoryStream(bestBytes) : null;
    }

    private static int ProbeMaxBitmapDimension(byte[] raw)
    {
        try
        {
            using var ms = new MemoryStream(raw);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int max = 0;
            foreach (var f in decoder.Frames)
            {
                int dim = Math.Max(f.PixelWidth, f.PixelHeight);
                if (dim > max) max = dim;
            }
            return max;
        }
        catch
        {
            return 0;
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
                frame = decoder.Frames
                    .OrderByDescending(f => f.PixelWidth * f.PixelHeight)
                    .First();
            }

            if (frame.PixelWidth < 16 || frame.PixelHeight < 16)
            {
                LogService.Write(LogTag, $"Favicon too small ({frame.PixelWidth}x{frame.PixelHeight}). Using Horizon icon.");
                return false;
            }

            var images = new List<(int Size, byte[] Png)>();
            foreach (int size in new[] { 256, 128, 64, 48, 32, 16 })
            {
                images.Add((size, RenderSquarePng(frame, size)));
            }

            byte[] ico = BuildIco(images);
            string tmp = iconPath + ".tmp";
            File.WriteAllBytes(tmp, ico);
            File.Move(tmp, iconPath, true);
            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
            catch { }
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