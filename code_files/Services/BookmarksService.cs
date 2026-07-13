using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Horizon.Stealth.Services;

public class BookmarkItem
{
    public string Name { get; set; } = "";
    public string Title { get => Name; set => Name = value; }
    public string Url { get; set; } = "";
    public DateTime DateAdded { get; set; } = DateTime.Now;
    public string IconPath { get; set; } = "";
}

public static class BookmarkService
{
    private static List<BookmarkItem> _items = new();
    private static readonly string _dataPath = Path.Combine(ConfigService.UserDataRoot, "bookmarks.json");

    public static event Action? OnUpdated;

    public static IReadOnlyList<BookmarkItem> Items => _items;

    public static void Initialize()
    {
        Load();
        LogService.Write("BOOKMARKS", $"Initialized. Loaded {_items.Count} bookmarks.");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            () => OnUpdated?.Invoke());
    }

    

    /// <summary>
    /// Copies a user-chosen icon file into HorizonData/FavouriteIcons/ and returns the stored path.
    /// </summary>
    public static string CopyFavouriteIconToData(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return string.Empty;

        string iconDir = Path.Combine(ConfigService.UserDataRoot, "FavouriteIcons");
        Directory.CreateDirectory(iconDir);

        string ext      = Path.GetExtension(sourcePath);
        string fileName = Guid.NewGuid().ToString("N") + ext;
        string destPath = Path.Combine(iconDir, fileName);

        File.Copy(sourcePath, destPath, overwrite: true);
        return destPath;
    }

    public static bool ExtractFromDefaultBrowser()
    {
        int totalImported = 0;
        string tempPath = Path.Combine(Path.GetTempPath(), $"hz_bkm_{Guid.NewGuid()}.json");

        try
        {
            var browserInfo = BrowserDetectionService.DetectDefaultBrowser();
            if (string.IsNullOrEmpty(browserInfo.UserDataPath)) 
            {
                LogService.Write("BOOKMARKS", "Failed to detect default browser User Data path.");
                return false;
            }

            string bookmarksPath = Path.Combine(browserInfo.UserDataPath, "Default", "Bookmarks");
            
            if (!File.Exists(bookmarksPath))
            {
                bookmarksPath = Path.Combine(browserInfo.UserDataPath, "Bookmarks");
            }

            if (!File.Exists(bookmarksPath))
            {
                LogService.Write("BOOKMARKS", $"Bookmarks file not found at: {bookmarksPath}");
                return false;
            }

            File.Copy(bookmarksPath, tempPath, true);

            string json = File.ReadAllText(tempPath);
            
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("roots", out JsonElement roots))
            {
                if (roots.TryGetProperty("bookmark_bar", out JsonElement bar))
                    totalImported += ExtractJsonNode(bar);
                if (roots.TryGetProperty("other", out JsonElement other))
                    totalImported += ExtractJsonNode(other);
                if (roots.TryGetProperty("synced", out JsonElement synced))
                    totalImported += ExtractJsonNode(synced);
            }

            if (totalImported > 0)
            {
                Save();
                OnUpdated?.Invoke();
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BookmarkService.ExtractFromDefaultBrowser");
            return false;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    public static int ImportHtml(string filePath)
	{
		int count = 0;
		try
		{
			string destPath = Path.Combine(ConfigService.UserDataRoot, Path.GetFileName(filePath));
			File.Copy(filePath, destPath, true);

			string html = File.ReadAllText(destPath);
			var regex = new Regex("(?i)<a\\s+[^>]*href=\"([^\"]+)\"[^>]*>([^<]+)</a>");
			var matches = regex.Matches(html);

			foreach (Match match in matches)
			{
				if (match.Groups.Count >= 3)
				{
					string url = match.Groups[1].Value.Trim();
					string name = match.Groups[2].Value.Trim();

					if (!string.IsNullOrEmpty(url) && !IsDuplicate(url))
					{
						_items.Add(new BookmarkItem { Url = url, Name = name });
						count++;
					}
				}
			}

			if (count > 0)
			{
				Save();
				OnUpdated?.Invoke();
			}
		}
		catch (Exception ex)
		{
			LogService.RecordCrash(ex, "BookmarkService.ImportHtml");
		}
		return count;
	}

    private static int ExtractJsonNode(JsonElement node)
    {
        int count = 0;
        if (node.TryGetProperty("type", out JsonElement typeElement))
        {
            if (typeElement.GetString() == "url")
            {
                string url = node.GetProperty("url").GetString() ?? "";
                string name = node.GetProperty("name").GetString() ?? "";

                if (!string.IsNullOrEmpty(url) && !IsDuplicate(url))
                {
                    _items.Add(new BookmarkItem { Url = url, Name = name });
                    count++;
                }
            }
            else if (typeElement.GetString() == "folder" && node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                {
                    count += ExtractJsonNode(child);
                }
            }
        }
        else if (node.TryGetProperty("children", out JsonElement rootChildren))
        {
            foreach (JsonElement child in rootChildren.EnumerateArray())
            {
                count += ExtractJsonNode(child);
            }
        }
        return count;
    }

    public static void Add(BookmarkItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Url)) return;
        if (IsDuplicate(item.Url)) return;
        _items.Add(item);
        Save();
        OnUpdated?.Invoke();
    }

    public static void Remove(BookmarkItem item)
    {
        if (_items.Contains(item))
        {
            _items.Remove(item);
            Save();
            OnUpdated?.Invoke();
        }
    }

    private static bool IsDuplicate(string url)
    {
        return _items.Any(x => x.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_items);
            File.WriteAllText(_dataPath, json);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BookmarkService.Save");
        }
    }

    public static void Load()
	{
		if (File.Exists(_dataPath))
		{
			try
			{
				var json = File.ReadAllText(_dataPath);
				var data = JsonSerializer.Deserialize<List<BookmarkItem>>(json);
				if (data != null) _items = data;
				return;
			}
			catch (Exception ex)
			{
				LogService.RecordCrash(ex, "BookmarkService.Load");
			}
		}

		var htmlFile = Directory.GetFiles(ConfigService.UserDataRoot, "*.html").FirstOrDefault();
		if (htmlFile != null)
		{
			ImportHtml(htmlFile);
		}
	}
}