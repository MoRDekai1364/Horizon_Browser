using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Stealth.Services;

public class DownloadItem
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public double? PercentOverride { get; set; }
    public bool IsComplete { get; set; }
    public bool IsFailed { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;

    public double Percent => PercentOverride ?? (TotalBytes > 0 ? (double)BytesReceived / TotalBytes * 100.0 : 0);
}

public static class DownloadsBridge
{
    private static readonly Dictionary<string, DownloadItem> _items = new();

    public static event Action? Updated;

    public static IReadOnlyList<DownloadItem> ActiveDownloads =>
        _items.Values.Where(d => !d.IsComplete && !d.IsFailed).OrderBy(d => d.StartedAt).ToList();

    public static IReadOnlyList<DownloadItem> RecentDownloads =>
        _items.Values.OrderByDescending(d => d.StartedAt).Take(10).ToList();

    public static void Start(string id, string fileName)
    {
        _items[id] = new DownloadItem { Id = id, FileName = fileName };
        Updated?.Invoke();
    }

    public static void UpdateProgress(string id, long bytesReceived, long totalBytes, string filePath)
    {
        if (!_items.TryGetValue(id, out var item)) return;
        item.BytesReceived = bytesReceived;
        item.TotalBytes = totalBytes;
        item.FilePath = filePath;
        Updated?.Invoke();
    }

    public static void UpdatePercent(string id, double percent)
    {
        if (!_items.TryGetValue(id, out var item)) return;
        item.PercentOverride = percent;
        Updated?.Invoke();
    }

    public static void Complete(string id, string filePath)
    {
        if (!_items.TryGetValue(id, out var item)) return;
        item.IsComplete = true;
        item.FilePath = filePath;
        item.PercentOverride = 100;
        Updated?.Invoke();
    }

    public static void Fail(string id)
    {
        if (!_items.TryGetValue(id, out var item)) return;
        item.IsFailed = true;
        Updated?.Invoke();
    }
}