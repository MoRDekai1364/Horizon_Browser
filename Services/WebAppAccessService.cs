using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Horizon.Stealth.Services;

public sealed class WebAppExtensionAccess
{
    public bool Allowed { get; set; } = true;
    public List<string> DeniedPermissions { get; set; } = new();
}

public sealed class WebAppAccessPolicy
{
    public int Version { get; set; } = 1;
    public bool ExtensionsEnabled { get; set; } = true;
    public List<string> DeniedPermissionTypes { get; set; } = new();
    public Dictionary<string, WebAppExtensionAccess> Extensions { get; set; } = new();
}

public sealed class WebAppExtensionInfo
{
    public string FolderName { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
}

public static class WebAppAccessService
{
    public const string SiteAccess = "site_access";

    private const string PolicyFileName = "access.json";
    private const string ExtensionCopyFolder = "ext";
    private const int HashBytes = 6;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static readonly HashSet<string> SiteApiPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "activeTab",
        "scripting"
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        [SiteAccess] = "Page access: read and change the pages of this Web App",
        ["tabs"] = "Tabs: see page titles and addresses",
        ["cookies"] = "Cookies: read and change cookies",
        ["storage"] = "Storage: save the extension's own data",
        ["unlimitedStorage"] = "Unlimited storage for the extension's data",
        ["webRequest"] = "Network monitoring: observe requests made by the page",
        ["webRequestBlocking"] = "Network blocking: block or modify requests",
        ["declarativeNetRequest"] = "Content blocking rules: block or redirect requests",
        ["declarativeNetRequestWithHostAccess"] = "Content blocking rules with site access",
        ["declarativeNetRequestFeedback"] = "Content blocking feedback: see which rules matched",
        ["clipboardRead"] = "Clipboard: read what you copied",
        ["clipboardWrite"] = "Clipboard: write to your clipboard",
        ["notifications"] = "Notifications: show desktop notifications",
        ["history"] = "History: read and change browsing history",
        ["bookmarks"] = "Bookmarks: read and change bookmarks",
        ["downloads"] = "Downloads: start and manage downloads",
        ["geolocation"] = "Location: use your location",
        ["management"] = "Extension management: see and control other extensions",
        ["nativeMessaging"] = "Native apps: talk to programs on your PC",
        ["contextMenus"] = "Context menu: add right-click menu items",
        ["webNavigation"] = "Navigation: observe page navigation",
        ["alarms"] = "Alarms: run scheduled tasks",
        ["identity"] = "Identity: sign in with accounts",
        ["idle"] = "Idle: detect when you are away",
        ["proxy"] = "Proxy: control network proxy settings",
        ["privacy"] = "Privacy: change privacy settings",
        ["tts"] = "Speech: read text aloud",
        ["offscreen"] = "Hidden pages: run background documents",
        ["sidePanel"] = "Side panel: show a side panel",
        ["background"] = "Background: keep running in the background",
        ["topSites"] = "Top sites: see your most visited sites",
        ["sessions"] = "Sessions: see recently closed tabs and devices",
        ["contentSettings"] = "Content settings: change site content settings",
        ["debugger"] = "Debugger: attach to pages for debugging",
        ["fontSettings"] = "Fonts: change font settings",
        ["search"] = "Search: run searches with the default engine"
    };

    public static string PolicyPath(string id) => Path.Combine(WebAppService.GetAppDir(id), PolicyFileName);

    public static string ProfileKey(string id)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes((id ?? "").ToLowerInvariant()));
        return "webapp_" + Convert.ToHexString(hash, 0, HashBytes).ToLowerInvariant();
    }

    public static string Describe(string permission)
        => Labels.TryGetValue(permission, out var label) ? label : permission;

    public static bool RequiresIsolation(WebAppAccessPolicy policy)
        => !policy.ExtensionsEnabled
           || policy.DeniedPermissionTypes.Count > 0
           || policy.Extensions.Values.Any(e => !e.Allowed || e.DeniedPermissions.Count > 0);

    public static WebAppAccessPolicy Load(string? id)
    {
        if (!WebAppService.IsValidId(id)) return new WebAppAccessPolicy();
        string path = PolicyPath(id!);
        if (!File.Exists(path)) return new WebAppAccessPolicy();
        try
        {
            var policy = JsonSerializer.Deserialize<WebAppAccessPolicy>(File.ReadAllText(path, Encoding.UTF8));
            return policy == null ? new WebAppAccessPolicy { ExtensionsEnabled = false } : Normalize(policy);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppAccessService.Load");
            return new WebAppAccessPolicy { ExtensionsEnabled = false };
        }
    }

    public static bool Save(string id, WebAppAccessPolicy policy)
    {
        try
        {
            if (!WebAppService.IsValidId(id)) return false;
            Directory.CreateDirectory(WebAppService.GetAppDir(id));
            string path = PolicyPath(id);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Normalize(policy), JsonOpts), Encoding.UTF8);
            File.Move(temp, path, true);
            LogService.Write("WEBAPP", $"Access policy saved for {id}. Restricted={RequiresIsolation(policy)}");
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppAccessService.Save");
            return false;
        }
    }

    public static List<WebAppExtensionInfo> ListInstalledExtensions()
    {
        var list = new List<WebAppExtensionInfo>();
        try
        {
            string root = ExtensionService.InstallRoot;
            if (!Directory.Exists(root)) return list;
            foreach (string dir in Directory.GetDirectories(root))
            {
                var info = ReadInfo(dir);
                if (info == null) continue;
                var record = ExtensionService.Find(info.FolderName);
                if (record != null && !record.Enabled) continue;
                list.Add(info);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppAccessService.ListInstalledExtensions");
        }
        return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static WebAppExtensionInfo? ReadInfo(string extensionDir)
    {
        try
        {
            string manifestPath = Path.Combine(extensionDir, "manifest.json");
            if (!File.Exists(manifestPath)) return null;
            var root = ParseManifest(manifestPath);
            if (root == null) return null;

            string folder = Path.GetFileName(extensionDir).ToLowerInvariant();
            string name = ExtensionService.Find(folder)?.Name ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                name = AsString(root["name"]);
                if (name.StartsWith("__MSG_", StringComparison.Ordinal)) name = "";
            }
            if (string.IsNullOrWhiteSpace(name)) name = folder;

            bool siteAccess = root.ContainsKey("content_scripts")
                              || root.ContainsKey("host_permissions")
                              || root.ContainsKey("optional_host_permissions");

            var apiPermissions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { "permissions", "optional_permissions" })
            {
                if (root[key] is not JsonArray array) continue;
                foreach (var item in array)
                {
                    string value = AsString(item);
                    if (value.Length == 0) continue;
                    if (IsHostPattern(value) || SiteApiPermissions.Contains(value)) siteAccess = true;
                    else apiPermissions.Add(value);
                }
            }

            var info = new WebAppExtensionInfo { FolderName = folder, Name = name, FullPath = extensionDir };
            if (siteAccess) info.Permissions.Add(SiteAccess);
            info.Permissions.AddRange(apiPermissions);
            return info;
        }
        catch (Exception ex)
        {
            LogService.Write("WEBAPP", "Could not read extension manifest in '" + extensionDir + "': " + ex.Message);
            return null;
        }
    }

    public static string? PrepareExtension(string id, string sourceDir, string folderName, WebAppAccessPolicy policy)
    {
        try
        {
            if (!policy.ExtensionsEnabled) return null;

            string key = folderName.ToLowerInvariant();
            policy.Extensions.TryGetValue(key, out var access);
            if (access != null && !access.Allowed) return null;

            var denied = new HashSet<string>(policy.DeniedPermissionTypes, StringComparer.OrdinalIgnoreCase);
            if (access != null)
            {
                foreach (string permission in access.DeniedPermissions) denied.Add(permission);
            }

            if (denied.Count == 0) return sourceDir;

            var manifestInfo = new FileInfo(Path.Combine(sourceDir, "manifest.json"));
            if (!manifestInfo.Exists) return null;

            string signature = manifestInfo.LastWriteTimeUtc.Ticks + "|" + manifestInfo.Length + "|" +
                               string.Join(",", denied.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)), 0, HashBytes).ToLowerInvariant();

            string root = Path.Combine(WebAppService.GetAppDir(id), ExtensionCopyFolder);
            string destination = Path.Combine(root, key + "_" + hash);
            if (Directory.Exists(destination))
            {
                RemoveStaleCopies(root, key, destination);
                return destination;
            }

            Directory.CreateDirectory(root);
            string temp = destination + ".tmp";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            CopyDirectory(sourceDir, temp);
            RewriteManifest(Path.Combine(temp, "manifest.json"), denied);
            Directory.Move(temp, destination);
            RemoveStaleCopies(root, key, destination);
            LogService.Write("WEBAPP", $"Prepared restricted copy of '{folderName}' for {id}. Denied={string.Join(",", denied)}");
            return destination;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppAccessService.PrepareExtension");
            return null;
        }
    }

    private static WebAppAccessPolicy Normalize(WebAppAccessPolicy policy)
    {
        var result = new WebAppAccessPolicy { ExtensionsEnabled = policy.ExtensionsEnabled };

        foreach (string permission in policy.DeniedPermissionTypes ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(permission)) continue;
            if (result.DeniedPermissionTypes.Contains(permission.Trim(), StringComparer.OrdinalIgnoreCase)) continue;
            result.DeniedPermissionTypes.Add(permission.Trim());
        }

        foreach (var pair in policy.Extensions ?? new Dictionary<string, WebAppExtensionAccess>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
            var access = new WebAppExtensionAccess { Allowed = pair.Value.Allowed };
            foreach (string permission in pair.Value.DeniedPermissions ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(permission)) continue;
                if (access.DeniedPermissions.Contains(permission.Trim(), StringComparer.OrdinalIgnoreCase)) continue;
                access.DeniedPermissions.Add(permission.Trim());
            }
            result.Extensions[pair.Key.Trim().ToLowerInvariant()] = access;
        }

        return result;
    }

    private static JsonObject? ParseManifest(string path)
    {
        var node = JsonNode.Parse(
            File.ReadAllText(path),
            null,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return node as JsonObject;
    }

    private static string AsString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static bool IsHostPattern(string value)
        => value == "<all_urls>" || value.Contains("://", StringComparison.Ordinal);

    private static void RewriteManifest(string manifestPath, HashSet<string> denied)
    {
        var root = ParseManifest(manifestPath) ?? throw new InvalidOperationException("Invalid manifest.");
        bool denySite = denied.Contains(SiteAccess);

        FilterPermissionArray(root, "permissions", denied, denySite);
        FilterPermissionArray(root, "optional_permissions", denied, denySite);

        if (denySite)
        {
            root.Remove("content_scripts");
            root.Remove("host_permissions");
            root.Remove("optional_host_permissions");
        }

        File.WriteAllText(manifestPath, root.ToJsonString(JsonOpts), new UTF8Encoding(false));
    }

    private static void FilterPermissionArray(JsonObject root, string key, HashSet<string> denied, bool denySite)
    {
        if (root[key] is not JsonArray array) return;

        var kept = new JsonArray();
        foreach (var item in array)
        {
            string value = AsString(item);
            if (value.Length > 0)
            {
                if (denied.Contains(value)) continue;
                if (denySite && (IsHostPattern(value) || SiteApiPermissions.Contains(value))) continue;
            }
            kept.Add(item?.DeepClone());
        }
        root[key] = kept;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void RemoveStaleCopies(string root, string key, string keep)
    {
        try
        {
            foreach (string dir in Directory.GetDirectories(root))
            {
                if (string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileName(dir);
                if (name.Length != key.Length + 1 + HashBytes * 2) continue;
                if (!name.StartsWith(key + "_", StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(dir, true); } catch { }
            }
        }
        catch { }
    }
}
