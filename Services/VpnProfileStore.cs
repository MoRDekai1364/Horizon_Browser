using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Horizon.Stealth.Services;

public enum VpnUpstreamType
{
    Http = 0,
    Socks5 = 1
}

public class VpnProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Server";
    public VpnUpstreamType Type { get; set; } = VpnUpstreamType.Socks5;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1080;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Source { get; set; } = "Custom";

    [JsonIgnore]
    public bool HasCredentials => !string.IsNullOrEmpty(Username);

    public override string ToString() => $"{Name} ({Host}:{Port})";
}

public static class VpnProfileStore
{
    private static readonly object _lock = new();
    private static readonly string _storePath = Path.Combine(ConfigService.UserDataRoot, "vpn_profiles.dat");
    private static readonly string _presetPath = Path.Combine(ConfigService.UserDataRoot, "vpn_presets.json");
    private static List<VpnProfile> _profiles = new();
    private static bool _loaded;

    public static event Action? Changed;

    public static IReadOnlyList<VpnProfile> Profiles
    {
        get
        {
            EnsureLoaded();
            lock (_lock) return _profiles.Select(Clone).ToList();
        }
    }

    public static VpnProfile? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        EnsureLoaded();
        lock (_lock)
        {
            var found = _profiles.FirstOrDefault(p => p.Id == id);
            return found == null ? null : Clone(found);
        }
    }

    public static bool TryAddOrUpdate(VpnProfile profile, out string error)
    {
        if (!Validate(profile, out error)) return false;
        EnsureLoaded();
        lock (_lock)
        {
            int idx = _profiles.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0) _profiles[idx] = Clone(profile);
            else _profiles.Add(Clone(profile));
        }
        bool saved = Save();
        if (!saved) error = "Failed to write encrypted profile store. See log.";
        else LogService.Write("VPN", $"Profile stored: {profile.Name} ({profile.Host}:{profile.Port}, {profile.Type}, source={profile.Source})");
        if (saved) Changed?.Invoke();
        return saved;
    }

    public static bool Remove(string id)
    {
        EnsureLoaded();
        bool removed;
        lock (_lock) removed = _profiles.RemoveAll(p => p.Id == id) > 0;
        if (!removed) return false;
        bool saved = Save();
        LogService.Write("VPN", $"Profile removed: {id}");
        if (saved) Changed?.Invoke();
        return saved;
    }

    public static IReadOnlyList<VpnProfile> LoadPresets()
    {
        try
        {
            if (!File.Exists(_presetPath)) return Array.Empty<VpnProfile>();
            var json = File.ReadAllText(_presetPath);
            var list = JsonSerializer.Deserialize<List<VpnProfile>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });
            if (list == null) return Array.Empty<VpnProfile>();

            var valid = new List<VpnProfile>();
            foreach (var p in list)
            {
                p.Source = "Preset";
                if (Validate(p, out string err)) valid.Add(p);
                else LogService.Write("VPN", $"Preset skipped ({p.Name}): {err}");
            }
            LogService.Write("VPN", $"Presets loaded: {valid.Count}/{list.Count}");
            return valid;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VpnProfileStore.LoadPresets");
            return Array.Empty<VpnProfile>();
        }
    }

    public static bool Validate(VpnProfile profile, out string error)
    {
        error = "";
        if (profile == null) { error = "Profile is null."; return false; }
        if (string.IsNullOrWhiteSpace(profile.Name)) { error = "Name is required."; return false; }
        if (string.IsNullOrWhiteSpace(profile.Host)) { error = "Host is required."; return false; }

        var host = profile.Host.Trim();
        if (host.Contains("://") || host.Contains('/') || host.Contains(' ') || host.Contains(':') && !host.StartsWith("["))
        {
            error = "Host must be a bare hostname or IP (no scheme, path, port or spaces).";
            return false;
        }
        if (Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown)
        {
            error = "Host is not a valid hostname or IP.";
            return false;
        }
        if (profile.Port < 1 || profile.Port > 65535) { error = "Port must be 1-65535."; return false; }
        if (profile.Type == VpnUpstreamType.Socks5 && string.IsNullOrEmpty(profile.Username) != string.IsNullOrEmpty(profile.Password))
        {
            error = "Username and password must both be set or both empty.";
            return false;
        }
        profile.Host = host;
        profile.Name = profile.Name.Trim();
        return true;
    }

    private static VpnProfile Clone(VpnProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Type = p.Type,
        Host = p.Host,
        Port = p.Port,
        Username = p.Username,
        Password = p.Password,
        Source = p.Source
    };

    private static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(_storePath)) return;
                var encrypted = File.ReadAllBytes(_storePath);
                var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(bytes);
                var data = JsonSerializer.Deserialize<List<VpnProfile>>(json);
                if (data != null) _profiles = data;
                LogService.Write("VPN", $"Profile store loaded: {_profiles.Count} profile(s)");
            }
            catch (Exception ex)
            {
                _profiles = new List<VpnProfile>();
                LogService.RecordCrash(ex, "VpnProfileStore.Load");
            }
        }
    }

    private static bool Save()
    {
        try
        {
            List<VpnProfile> snapshot;
            lock (_lock) snapshot = _profiles.Select(Clone).ToList();
            Directory.CreateDirectory(ConfigService.UserDataRoot);
            var json = JsonSerializer.Serialize(snapshot);
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var tmp = _storePath + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            File.Move(tmp, _storePath, true);
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VpnProfileStore.Save");
            return false;
        }
    }
}