using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Horizon.Stealth.Services;

public class HistoryItem
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime Visited { get; set; }
    public DateTime VisitTime { get => Visited; set => Visited = value; }
}


public static class HistoryService
{
    public static event EventHandler? HistoryUpdated;

    private static string DataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Horizon_Browser", "HorizonData");

    private static string DataFile => Path.Combine(DataRoot, "history.json");

    private static string LegacyFile =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");

    private static List<HistoryItem> _items = new();


    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);

            if (!File.Exists(DataFile) && File.Exists(LegacyFile))
            {
                File.Copy(LegacyFile, DataFile);
                LogService.Write("HISTORY", "Migrated history.json → HorizonData\\history.json");
            }

            if (!File.Exists(DataFile)) return;

            string json = File.ReadAllText(DataFile);
            _items = JsonSerializer.Deserialize<List<HistoryItem>>(json)
                     ?? new List<HistoryItem>();

            LogService.Write("HISTORY", $"Loaded {_items.Count} history entries.");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "HistoryService.Load");
            _items = new List<HistoryItem>();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            string json = JsonSerializer.Serialize(_items,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataFile, json);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "HistoryService.Save");
        }
    }

    public static void Add(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        _items.RemoveAll(h => h.Url == url);
        _items.Insert(0, new HistoryItem { Url = url, Title = title, Visited = DateTime.Now });

        // Keep last 500 entries
        if (_items.Count > 99999)
            _items = _items.Take(99999).ToList();

        Save();
        HistoryUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static void Remove(string url)
    {
        int removed = _items.RemoveAll(h => h.Url == url);
        if (removed > 0) { Save(); HistoryUpdated?.Invoke(null, EventArgs.Empty); }
    }

    public static void Clear()
    {
        _items.Clear();
        Save();
        HistoryUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static IEnumerable<HistoryItem> GetRecent() => _items;
}