using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace Horizon.Stealth.Services;

public class VaultItem
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; 
    public DateTime DateAdded { get; set; } = DateTime.Now;
}

public static class VaultService
{
    private static List<VaultItem> _items = new();
    private static readonly string _vaultPath = Path.Combine(ConfigService.UserDataRoot, "vault.dat");

    public static event Action? OnUpdated;

    public static bool IsLocked { get; private set; } = false;
    public static IReadOnlyList<VaultItem> Items => _items;

    public static void Initialize()
    {
        Load();
        LogService.Write("VAULT", $"Initialized. Loaded {_items.Count} credentials.");
        OnUpdated?.Invoke();
    }

    public static void Add(string url, string user, string pass, string title = "")
    {
        if (IsDuplicate(url, user)) return;

        _items.Add(new VaultItem { Title = title, Url = url, Username = user, Password = pass });
        Save();
        OnUpdated?.Invoke();
    }

    public static void Remove(VaultItem item)
    {
        if (_items.Contains(item))
        {
            _items.Remove(item);
            Save();
            OnUpdated?.Invoke();
        }
    }

    public static void UpdateItem(VaultItem item, string title, string url, string user, string pass)
    {
        item.Title = title;
        item.Url = url;
        item.Username = user;
        item.Password = pass;
        Save();
        OnUpdated?.Invoke();
    }

    public static void Lock()
    {
        _items.Clear();
        IsLocked = true;
        try { System.Windows.Clipboard.Clear(); } catch { }
        OnUpdated?.Invoke();
    }

    public static void Unlock()
    {
        Load();
        OnUpdated?.Invoke();
    }

    public static void ImportCsv(string filePath)
    {
        try
        {
            var lines = File.ReadAllLines(filePath);
            int count = 0;
            int skipped = 0;
            var csvSplit = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

            // ── Detect column layout from header row ─────────────────────────
            int titleIdx = -1, urlIdx = 0, userIdx = 1, passIdx = 2;
            int startLine = 0;

            if (lines.Length > 0)
            {
                var hdr = csvSplit.Split(lines[0]).Select(CleanField).ToArray();

                int FindCol(params string[] names)
                {
                    foreach (var n in names)
                        for (int i = 0; i < hdr.Length; i++)
                            if (hdr[i].Equals(n, StringComparison.OrdinalIgnoreCase)) return i;
                    return -1;
                }

                int ti = FindCol("name", "title", "label");
                int ui = FindCol("url", "website", "login_uri", "uri", "origin");
                int un = FindCol("username", "login_username", "user", "email", "login");
                int pw = FindCol("password", "login_password", "pass");

                if (ui >= 0 || pw >= 0) // looks like a named header row
                {
                    titleIdx  = ti >= 0 ? ti  : -1;
                    urlIdx    = ui >= 0 ? ui  : 0;
                    userIdx   = un >= 0 ? un  : 1;
                    passIdx   = pw >= 0 ? pw  : 2;
                    startLine = 1;
                }
            }

            for (int li = startLine; li < lines.Length; li++)
            {
                var parts = csvSplit.Split(lines[li]);
                int need  = Math.Max(urlIdx, Math.Max(userIdx, passIdx));
                if (parts.Length <= need) continue;

                string title = titleIdx >= 0 && titleIdx < parts.Length ? CleanField(parts[titleIdx]) : "";
                string url   = CleanField(parts[urlIdx]);
                string user  = userIdx < parts.Length ? CleanField(parts[userIdx]) : "";
                string pass  = CleanField(parts[passIdx]);

                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(pass)) continue;

                if (IsDuplicate(url, user)) { skipped++; continue; }

                _items.Add(new VaultItem { Title = title, Url = url, Username = user, Password = pass });
                count++;
            }

            Save();
            OnUpdated?.Invoke();
            MessageBox.Show($"Import Complete.\n\nNew: {count}\nSkipped (Duplicates): {skipped}", "Horizon Vault");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VaultService.ImportCsv");
            MessageBox.Show("Import failed. Check logs.", "Error");
        }
    }

    public static void ExportCsv(string filePath)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("name,url,username,password");
            foreach (var item in _items)
            {
                sb.AppendLine($"\"{EscapeCsv(item.Title)}\",\"{EscapeCsv(item.Url)}\",\"{EscapeCsv(item.Username)}\",\"{EscapeCsv(item.Password)}\"");
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Successfully exported {_items.Count} credentials.", "Horizon Vault");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VaultService.ExportCsv");
            MessageBox.Show("Export failed. Check logs.", "Error");
        }
    }

    private static bool IsDuplicate(string url, string user)
    {
        return _items.Any(x => x.Url.Equals(url, StringComparison.OrdinalIgnoreCase) && 
                               x.Username.Equals(user, StringComparison.OrdinalIgnoreCase));
    }

    private static string EscapeCsv(string field)
    {
        return (field ?? "").Replace("\"", "\"\"");
    }

    private static string CleanField(string field)
    {
        string cleaned = field.Trim();
        if (cleaned.StartsWith("\"") && cleaned.EndsWith("\""))
        {
            cleaned = cleaned.Substring(1, cleaned.Length - 2);
            cleaned = cleaned.Replace("\"\"", "\"");
        }
        return cleaned;
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_items);
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_vaultPath, encrypted);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "VaultService.Save");
        }
    }

    private static void Load()
    {
        if (!File.Exists(_vaultPath)) return;

        try
        {
            var encrypted = File.ReadAllBytes(_vaultPath);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(bytes);
            
            var data = JsonSerializer.Deserialize<List<VaultItem>>(json);
            if (data != null)
            {
                foreach (var item in data)
                {
                    bool titleLooksLikeUrl = item.Title.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                         || item.Title.Contains(".")  && !item.Title.Contains(" ");
                    bool urlIsEmpty = string.IsNullOrEmpty(item.Url);
                    bool urlLooksLikeUsername = !string.IsNullOrEmpty(item.Url)
                                             && !item.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                             && !item.Url.Contains("/");

                    if (titleLooksLikeUrl || urlIsEmpty || urlLooksLikeUsername)
                    {
                        item.Password  = item.Username;
                        item.Username  = item.Url;
                        item.Url       = item.Title;
                        item.Title     = "";
                    }
                }
                _items = data;
                IsLocked = false;
                Save();
            }
        }
        catch (Exception)
        {
            LogService.Write("VAULT-ERR", "Failed to decrypt vault. Data may be corrupt or from another user.");
        }
    }
    
    public static VaultItem? FindMatch(string currentUrl)
    {
        if (Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri))
        {
            return _items.FirstOrDefault(x =>
            {
                if (Uri.TryCreate(x.Url, UriKind.Absolute, out var itemUri))
                    return currentUri.Host.EndsWith(itemUri.Host, StringComparison.OrdinalIgnoreCase) ||
                           itemUri.Host.EndsWith(currentUri.Host, StringComparison.OrdinalIgnoreCase);
                return currentUrl.Contains(x.Url, StringComparison.OrdinalIgnoreCase);
            });
        }
        return _items.FirstOrDefault(x => currentUrl.Contains(x.Url, StringComparison.OrdinalIgnoreCase));
    }

    public static void WipeAndImport(string filePath)
    {
        _items.Clear();
        ImportCsv(filePath);
    }
}