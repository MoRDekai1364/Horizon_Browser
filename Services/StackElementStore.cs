using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Horizon.Stealth.Services;

public enum StackElementKind
{
    Widget = 0,
    Site = 1,
    Template = 2
}

public enum StackLoginProfile
{
    Shared = 0,
    Isolated = 1
}

public class StackElementData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public StackElementKind Kind { get; set; } = StackElementKind.Site;
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Url { get; set; } = "";
    public int RefreshMinutes { get; set; }
    public StackLoginProfile LoginProfile { get; set; } = StackLoginProfile.Shared;
    public string UserCss { get; set; } = "";
}

public sealed class StackCatalogEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "";
    public StackElementKind Kind { get; init; }
}

public sealed class StackWebProfile
{
    public bool MobileUserAgent { get; init; }
    public double Zoom { get; init; } = 1.0;
    public bool PreferDark { get; init; }
    public string HideCss { get; init; } = "";
}

public static class StackElementCatalog
{
    private static StackCatalogEntry W(string id, string name, string icon) =>
        new() { Id = id, Name = name, Icon = icon, Kind = StackElementKind.Widget };

    private static StackCatalogEntry T(string id, string name, string icon) =>
        new() { Id = id, Name = name, Icon = icon, Kind = StackElementKind.Template };

    public static IReadOnlyList<StackCatalogEntry> Widgets { get; } = new List<StackCatalogEntry>
    {
        W("cpu", "CPU Usage", "\u26A1"),
        W("ram", "RAM Usage", "\U0001F4BE"),
        W("notifications", "Notifications", "\U0001F514"),
        W("notes", "Notes", "\U0001F4DD"),
        W("calculator", "Calculator", "\U0001F9EE"),
        W("converter", "Converter", "\u21C4"),
        W("calendar", "Calendar", "\U0001F5D3")
    };

    public static IReadOnlyList<StackCatalogEntry> Templates { get; } = new List<StackCatalogEntry>
    {
        T("mail", "Mail", "\u2709"),
        T("videos", "Videos", "\u25B6"),
        T("music", "Music", "\U0001F3A7"),
        T("streaming_gaming", "Streaming & Gaming", "\U0001F3AE"),
        T("movies", "Movies", "\U0001F3AC"),
        T("torrent_p2p", "Torrent & P2P", "\U0001F4E5"),
        T("office", "Office", "\U0001F4BC"),
        T("ai", "AI", "\U0001F916"),
        T("info_research", "Info & Research", "\U0001F50D"),
        T("chat", "Chat & Messaging", "\U0001F4AC"),
        T("calendar_planner", "Calendar & Planner", "\U0001F5D3"),
        T("news", "News", "\U0001F4F0"),
        T("developer", "Developer", "\U0001F6E0"),
        T("cloud_files", "Cloud & Files", "\u2601"),
        T("social", "Social feeds", "\U0001F465"),
        T("shopping", "Shopping & Price watch", "\U0001F6D2"),
        T("translate", "Translate & Dictionary", "\U0001F310"),
        T("maps_travel", "Maps & Travel", "\U0001F5FA"),
        T("notes", "Notes & Scratchpad", "\U0001F4DD")
    };

    public static IReadOnlyList<StackCatalogEntry> For(StackElementKind kind) => kind switch
    {
        StackElementKind.Widget => Widgets,
        StackElementKind.Template => Templates,
        _ => Array.Empty<StackCatalogEntry>()
    };

    private const string CommonHide =
        "[class*='cookie-banner' i], [id*='cookie-banner' i], [class*='cookie-consent' i], [id*='cookie-consent' i], " +
        ".cc-window, #onetrust-banner-sdk, #onetrust-consent-sdk, [class*='newsletter-popup' i], " +
        "[class*='app-banner' i], [class*='smartbanner' i], .smartbanner { display: none !important; }";

    private static StackWebProfile P(double zoom, bool mobile = false, bool dark = false, bool hide = true) =>
        new() { Zoom = zoom, MobileUserAgent = mobile, PreferDark = dark, HideCss = hide ? CommonHide : "" };

    private static readonly Dictionary<string, StackWebProfile> TemplateProfiles = new()
    {
        ["mail"] = P(0.85, dark: true),
        ["videos"] = P(0.8, mobile: true),
        ["music"] = P(0.85, dark: true),
        ["streaming_gaming"] = P(0.8),
        ["movies"] = P(0.8),
        ["torrent_p2p"] = P(0.85, hide: false),
        ["office"] = P(0.8),
        ["ai"] = P(0.9, dark: true),
        ["info_research"] = P(0.9, mobile: true),
        ["chat"] = P(0.85, dark: true),
        ["calendar_planner"] = P(0.8),
        ["news"] = P(0.9, mobile: true),
        ["developer"] = P(0.85, dark: true),
        ["cloud_files"] = P(0.85),
        ["social"] = P(0.9, mobile: true),
        ["shopping"] = P(0.9, mobile: true),
        ["translate"] = P(0.95, mobile: true),
        ["maps_travel"] = P(0.85),
        ["notes"] = P(0.9, dark: true)
    };

    private static readonly (string Host, string Template)[] DetectionMap =
    {
        ("mail.google.com", "mail"), ("gmail.com", "mail"), ("outlook.live.com", "mail"), ("outlook.com", "mail"), ("outlook.office.com", "mail"),
        ("outlook.office365.com", "mail"), ("calendar.proton.me", "calendar_planner"), ("drive.proton.me", "cloud_files"), ("mail.proton.me", "mail"), ("proton.me", "mail"), ("mail.yahoo.com", "mail"),
        ("music.youtube.com", "music"), ("open.spotify.com", "music"), ("spotify.com", "music"), ("soundcloud.com", "music"),
        ("deezer.com", "music"), ("music.apple.com", "music"),
        ("youtube.com", "videos"), ("youtu.be", "videos"), ("vimeo.com", "videos"), ("dailymotion.com", "videos"),
        ("twitch.tv", "streaming_gaming"), ("store.steampowered.com", "streaming_gaming"), ("steamcommunity.com", "streaming_gaming"),
        ("netflix.com", "movies"), ("imdb.com", "movies"), ("themoviedb.org", "movies"), ("max.com", "movies"), ("disneyplus.com", "movies"),
        ("docs.google.com", "office"), ("sheets.google.com", "office"), ("slides.google.com", "office"), ("office.com", "office"),
        ("chatgpt.com", "ai"), ("chat.openai.com", "ai"), ("claude.ai", "ai"), ("gemini.google.com", "ai"), ("perplexity.ai", "ai"), ("copilot.microsoft.com", "ai"),
        ("wikipedia.org", "info_research"), ("scholar.google.com", "info_research"), ("arxiv.org", "info_research"),
        ("web.whatsapp.com", "chat"), ("web.telegram.org", "chat"), ("discord.com", "chat"), ("messenger.com", "chat"), ("app.slack.com", "chat"), ("teams.microsoft.com", "chat"), ("meet.google.com", "chat"),
        ("calendar.google.com", "calendar_planner"),
        ("github.com", "developer"), ("gitlab.com", "developer"), ("stackoverflow.com", "developer"),
        ("drive.google.com", "cloud_files"), ("dropbox.com", "cloud_files"), ("onedrive.live.com", "cloud_files"),
        ("reddit.com", "social"), ("x.com", "social"), ("twitter.com", "social"), ("facebook.com", "social"), ("instagram.com", "social"),
        ("translate.google.com", "translate"), ("deepl.com", "translate"),
        ("maps.google.com", "maps_travel"), ("openstreetmap.org", "maps_travel"),
        ("keep.google.com", "notes"), ("notion.so", "notes"),
        ("news.google.com", "news"), ("bbc.com", "news"), ("bbc.co.uk", "news"), ("cnn.com", "news"), ("reuters.com", "news"), ("nytimes.com", "news")
    };

    public static StackWebProfile ProfileFor(StackElementData data)
    {
        if (data.Kind == StackElementKind.Template && TemplateProfiles.TryGetValue(data.SourceId, out var profile)) return profile;
        return new StackWebProfile();
    }

    public static StackCatalogEntry? DetectTemplate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string text = url.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        string host = uri.Host.ToLowerInvariant();
        foreach (var item in DetectionMap)
        {
            if (host == item.Host || host.EndsWith("." + item.Host))
                return Find(StackElementKind.Template, item.Template);
        }
        return null;
    }

    public static StackCatalogEntry? Find(StackElementKind kind, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return For(kind).FirstOrDefault(e => e.Id == id);
    }
}

public static class StackElementStore
{
    private static readonly object _lock = new();
    private static readonly string _storePath = Path.Combine(ConfigService.UserDataRoot, "stack_elements.json");
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static List<StackElementData> _elements = new();
    private static bool _loaded;

    public static event Action? Changed;

    public static IReadOnlyList<StackElementData> Elements
    {
        get
        {
            EnsureLoaded();
            lock (_lock) return _elements.Select(Clone).ToList();
        }
    }

    public static StackElementData? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        EnsureLoaded();
        lock (_lock)
        {
            var found = _elements.FirstOrDefault(e => e.Id == id);
            return found == null ? null : Clone(found);
        }
    }

    public static bool Validate(StackElementData data, out string error)
    {
        error = "";
        if (data == null) { error = "Element is null."; return false; }

        bool needsSource = data.Kind == StackElementKind.Widget || data.Kind == StackElementKind.Template;
        bool needsUrl = data.Kind == StackElementKind.Site || data.Kind == StackElementKind.Template;

        StackCatalogEntry? entry = null;
        if (needsSource)
        {
            entry = StackElementCatalog.Find(data.Kind, data.SourceId);
            if (entry == null) { error = "Choose an item from the list."; return false; }
        }

        if (needsUrl)
        {
            string url = (data.Url ?? "").Trim();
            if (url.Length == 0) { error = "Web address is required."; return false; }
            if (!url.Contains("://")) url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(uri.Host)
                || (!uri.Host.Contains('.') && uri.Host != "localhost"))
            {
                error = "Web address is not valid.";
                return false;
            }
            data.Url = url;
        }
        else
        {
            data.Url = "";
        }

        string title = (data.Title ?? "").Trim();
        if (title.Length == 0)
        {
            if (entry != null) title = entry.Name;
            else if (Uri.TryCreate(data.Url, UriKind.Absolute, out var u)) title = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
        }
        if (title.Length == 0) { error = "Name is required."; return false; }
        if (title.Length > 40) title = title.Substring(0, 40);
        data.Title = title;

        string userCss = (data.UserCss ?? "").Trim();
        if (userCss.Length > 4000) userCss = userCss.Substring(0, 4000);
        data.UserCss = data.Kind == StackElementKind.Widget ? "" : userCss;

        string icon = (data.Icon ?? "").Trim();
        if (icon.Length == 0) icon = entry?.Icon ?? "\U0001F310";
        if (icon.Length > 8) icon = icon.Substring(0, 8);
        data.Icon = icon;

        if (needsUrl)
        {
            data.RefreshMinutes = Math.Clamp(data.RefreshMinutes, 0, 1440);
        }
        else
        {
            data.RefreshMinutes = 0;
            data.LoginProfile = StackLoginProfile.Shared;
        }

        if (string.IsNullOrWhiteSpace(data.Id)) data.Id = Guid.NewGuid().ToString("N");
        return true;
    }

    public static bool TryAddOrUpdate(StackElementData data, out string error)
    {
        if (!Validate(data, out error)) return false;
        EnsureLoaded();
        lock (_lock)
        {
            int idx = _elements.FindIndex(e => e.Id == data.Id);
            if (idx >= 0) _elements[idx] = Clone(data);
            else _elements.Add(Clone(data));
        }
        bool saved = Save();
        if (!saved) error = "Failed to write stack elements. See log.";
        else LogService.Write("Stack", $"Element stored: {data.Title} (kind={data.Kind}, source={data.SourceId}, id={data.Id})");
        if (saved) Changed?.Invoke();
        return saved;
    }

    public static bool Remove(string id)
    {
        EnsureLoaded();
        bool removed;
        lock (_lock) removed = _elements.RemoveAll(e => e.Id == id) > 0;
        if (!removed) return false;
        bool saved = Save();
        LogService.Write("Stack", $"Element removed: {id}");
        if (saved) Changed?.Invoke();
        return saved;
    }

    private static StackElementData Clone(StackElementData e) => new()
    {
        Id = e.Id,
        Kind = e.Kind,
        Title = e.Title,
        Icon = e.Icon,
        SourceId = e.SourceId,
        Url = e.Url,
        RefreshMinutes = e.RefreshMinutes,
        LoginProfile = e.LoginProfile,
        UserCss = e.UserCss
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
                var json = File.ReadAllText(_storePath);
                var data = JsonSerializer.Deserialize<List<StackElementData>>(json, _json);
                if (data != null) _elements = data.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Id)).ToList();
                LogService.Write("Stack", $"Stack elements loaded: {_elements.Count}");
            }
            catch (Exception ex)
            {
                _elements = new List<StackElementData>();
                LogService.RecordCrash(ex, "StackElementStore.Load");
            }
        }
    }

    private static bool Save()
    {
        try
        {
            List<StackElementData> snapshot;
            lock (_lock) snapshot = _elements.Select(Clone).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            string tmp = _storePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, _json));
            File.Move(tmp, _storePath, true);
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StackElementStore.Save");
            return false;
        }
    }
}
