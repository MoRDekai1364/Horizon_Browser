using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Horizon.Stealth.Services;

public class SitePermissionEntry
{
    public string Origin { get; set; } = "";
    public bool Allow { get; set; } = false;
    public DateTime LastRequested { get; set; } = DateTime.Now;
}

public static class SiteNotificationPermissionService
{
    private static readonly string _folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HorizonData");
    private static readonly string _path = Path.Combine(_folder, "notification_permissions.json");

    public static Dictionary<string, SitePermissionEntry> Entries { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);

            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<Dictionary<string, SitePermissionEntry>>(json);
                if (data != null) Entries = data;
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);
            File.WriteAllText(_path, JsonSerializer.Serialize(Entries));
        }
        catch { }
    }

    public static bool GetAllow(string origin)
    {
        return Entries.TryGetValue(origin, out var e) && e.Allow;
    }

    public static void RecordRequest(string origin)
    {
        if (string.IsNullOrEmpty(origin)) return;

        if (!Entries.ContainsKey(origin))
        {
            Entries[origin] = new SitePermissionEntry { Origin = origin, Allow = false, LastRequested = DateTime.Now };
            Save();
        }
        else
        {
            Entries[origin].LastRequested = DateTime.Now;
        }
    }

    public static void SetAllow(string origin, bool allow)
    {
        if (!Entries.TryGetValue(origin, out var e))
        {
            e = new SitePermissionEntry { Origin = origin };
            Entries[origin] = e;
        }
        e.Allow = allow;
        e.LastRequested = DateTime.Now;
        Save();
    }

    public static void Remove(string origin)
    {
        if (Entries.Remove(origin)) Save();
    }
}