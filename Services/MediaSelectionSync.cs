using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Stealth.Services;

/// <summary>A file inside the media library. Collection "" = loose file in the library root.
/// Stored by name, never by absolute path, so the library can move with the user data folder.</summary>
public sealed class MediaRef
{
    public string Collection { get; set; } = "";
    public string FileName   { get; set; } = "";
}

/// <summary>Allowed values for the *Source and *PlayOrder settings.</summary>
public static class MediaSource
{
    /// <summary>One file (Wallpaper/StartupVideo Single).</summary>
    public const string Single = "Single";
    /// <summary>A hand-picked list of files (Selection), possibly from several collections.</summary>
    public const string Selection = "Selection";
    /// <summary>Every file of one collection (ActiveCollection; "" = loose files).</summary>
    public const string Collection = "Collection";

    public const string Random = "Random";
    public const string Sequential = "Sequential";

    public static bool IsValidSource(string? s)
        => s == Single || s == Selection || s == Collection;

    public static bool IsValidOrder(string? s)
        => s == Random || s == Sequential;
}

/// <summary>
/// Settings glue for the media library:
///  - MapLegacy: one-time conversion of the old Wallpaper*/StartupVideo* fields (config version 5 -> 6).
///  - Normalize: repairs null/invalid values after loading a settings file.
///  - Attach: keeps the saved selection valid when a collection is renamed or deleted.
/// Legacy fields are left untouched (old UI/readers keep working until Phases 3, 6 and 7).
/// </summary>
public static class MediaSelectionSync
{
    private const string KindWallpaper = "W";
    private const string KindVideo = "V";
    private static bool _attached;

    // ------------------------------------------------------------------ legacy -> new

    public static void MapLegacy(SettingsData d)
    {
        try
        {
            // Wallpapers. Old files were flat, so their names map straight onto loose files of the library root.
            d.WallpaperSource = MediaSource.Collection;
            d.WallpaperActiveCollection = "";
            d.WallpaperPlayOrder = MediaSource.Random;
            d.WallpaperPlayIndex = -1;
            d.WallpaperSingle = new MediaRef();
            d.WallpaperSelection = new List<MediaRef>();

            if (d.WallpaperMode == "Fixed" && !string.IsNullOrWhiteSpace(d.WallpaperFileName))
            {
                d.WallpaperSource = MediaSource.Single;
                d.WallpaperSingle = new MediaRef { Collection = "", FileName = d.WallpaperFileName };
            }
            else if (d.WallpaperMode == "Custom" && d.WallpaperCustomList != null && d.WallpaperCustomList.Count > 0)
            {
                d.WallpaperSource = MediaSource.Selection;
                d.WallpaperSelection = d.WallpaperCustomList
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => new MediaRef { Collection = "", FileName = n })
                    .ToList();
                d.WallpaperPlayOrder = MediaSource.IsValidOrder(d.WallpaperOrder) ? d.WallpaperOrder : MediaSource.Sequential;
                d.WallpaperPlayIndex = d.WallpaperCustomIndex;
            }

            // Startup videos.
            d.StartupVideoSource = MediaSource.Single;
            d.StartupVideoActiveCollection = "";
            d.StartupVideoPlayOrder = MediaSource.Sequential;
            d.StartupVideoPlayIndex = -1;
            d.StartupVideoSingle = new MediaRef { Collection = "", FileName = string.IsNullOrWhiteSpace(d.StartupVideoFileName) ? "Animate_this_image.mp4" : d.StartupVideoFileName };
            d.StartupVideoSelection = new List<MediaRef>();

            if (d.StartupVideoMode == "Random")
            {
                d.StartupVideoSource = MediaSource.Collection;
                d.StartupVideoPlayOrder = MediaSource.Random;
            }
            else if (d.StartupVideoMode == "Custom" && d.StartupVideoCustomList != null && d.StartupVideoCustomList.Count > 0)
            {
                d.StartupVideoSource = MediaSource.Selection;
                d.StartupVideoSelection = d.StartupVideoCustomList
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => new MediaRef { Collection = "", FileName = n })
                    .ToList();
                d.StartupVideoPlayOrder = MediaSource.IsValidOrder(d.StartupVideoOrder) ? d.StartupVideoOrder : MediaSource.Sequential;
                d.StartupVideoPlayIndex = d.StartupVideoCustomIndex;
            }

            LogService.Write("SETTINGS", $"Media selection migrated: wallpaper={d.WallpaperSource}, video={d.StartupVideoSource}");
        }
        catch (Exception ex)
        {
            LogService.Write("SETTINGS", "MapLegacy failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ repair

    public static void Normalize(SettingsData d)
    {
        d.WallpaperActiveCollection ??= "";
        d.WallpaperSingle ??= new MediaRef();
        d.WallpaperSelection ??= new List<MediaRef>();
        d.StartupVideoActiveCollection ??= "";
        d.StartupVideoSingle ??= new MediaRef();
        d.StartupVideoSelection ??= new List<MediaRef>();
        d.MediaSourcePaths ??= new Dictionary<string, string>();

        d.WallpaperSelection.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.FileName));
        d.StartupVideoSelection.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.FileName));

        if (!MediaSource.IsValidSource(d.WallpaperSource)) d.WallpaperSource = MediaSource.Collection;
        if (!MediaSource.IsValidSource(d.StartupVideoSource)) d.StartupVideoSource = MediaSource.Single;
        if (!MediaSource.IsValidOrder(d.WallpaperPlayOrder)) d.WallpaperPlayOrder = MediaSource.Random;
        if (!MediaSource.IsValidOrder(d.StartupVideoPlayOrder)) d.StartupVideoPlayOrder = MediaSource.Sequential;

        // A mode that points at nothing falls back to the whole loose-files collection.
        if (d.WallpaperSource == MediaSource.Single && string.IsNullOrWhiteSpace(d.WallpaperSingle.FileName))
            d.WallpaperSource = MediaSource.Collection;
        if (d.WallpaperSource == MediaSource.Selection && d.WallpaperSelection.Count == 0)
            d.WallpaperSource = MediaSource.Collection;
        if (d.StartupVideoSource == MediaSource.Selection && d.StartupVideoSelection.Count == 0)
            d.StartupVideoSource = MediaSource.Collection;
    }

    // ------------------------------------------------------------------ keep selection valid on rename/delete

    public static void Attach()
    {
        if (_attached) return;
        _attached = true;

        WallpaperLibraryService.Library.CollectionRenamed += (o, n) => OnRenamed(KindWallpaper, o, n);
        WallpaperLibraryService.Library.CollectionDeleted += name => OnDeleted(KindWallpaper, name);
        StartupVideoLibraryService.Library.CollectionRenamed += (o, n) => OnRenamed(KindVideo, o, n);
        StartupVideoLibraryService.Library.CollectionDeleted += name => OnDeleted(KindVideo, name);
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void OnRenamed(string kind, string oldName, string newName)
    {
        try
        {
            var d = SettingsService.Current;
            if (kind == KindWallpaper)
            {
                if (Same(d.WallpaperActiveCollection, oldName)) d.WallpaperActiveCollection = newName;
                if (Same(d.WallpaperSingle.Collection, oldName)) d.WallpaperSingle.Collection = newName;
                foreach (var r in d.WallpaperSelection.Where(r => Same(r.Collection, oldName))) r.Collection = newName;
            }
            else
            {
                if (Same(d.StartupVideoActiveCollection, oldName)) d.StartupVideoActiveCollection = newName;
                if (Same(d.StartupVideoSingle.Collection, oldName)) d.StartupVideoSingle.Collection = newName;
                foreach (var r in d.StartupVideoSelection.Where(r => Same(r.Collection, oldName))) r.Collection = newName;
            }

            string oldPrefix = kind + ":" + oldName + "/";
            string newPrefix = kind + ":" + newName + "/";
            foreach (var key in d.MediaSourcePaths.Keys.Where(k => k.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                string value = d.MediaSourcePaths[key];
                d.MediaSourcePaths.Remove(key);
                d.MediaSourcePaths[newPrefix + key.Substring(oldPrefix.Length)] = value;
            }

            SettingsService.Save();
        }
        catch (Exception ex) { LogService.Write("SETTINGS", "Media rename sync failed: " + ex.Message); }
    }

    private static void OnDeleted(string kind, string name)
    {
        try
        {
            var d = SettingsService.Current;
            if (kind == KindWallpaper)
            {
                if (Same(d.WallpaperActiveCollection, name)) d.WallpaperActiveCollection = "";
                if (Same(d.WallpaperSingle.Collection, name))
                {
                    d.WallpaperSingle = new MediaRef();
                    if (d.WallpaperSource == MediaSource.Single) d.WallpaperSource = MediaSource.Collection;
                }
                d.WallpaperSelection.RemoveAll(r => Same(r.Collection, name));
                d.WallpaperPlayIndex = -1;
            }
            else
            {
                if (Same(d.StartupVideoActiveCollection, name)) d.StartupVideoActiveCollection = "";
                if (Same(d.StartupVideoSingle.Collection, name))
                {
                    d.StartupVideoSingle = new MediaRef();
                    if (d.StartupVideoSource == MediaSource.Single) d.StartupVideoSource = MediaSource.Collection;
                }
                d.StartupVideoSelection.RemoveAll(r => Same(r.Collection, name));
                d.StartupVideoPlayIndex = -1;
            }

            string prefix = kind + ":" + name + "/";
            foreach (var key in d.MediaSourcePaths.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                d.MediaSourcePaths.Remove(key);

            Normalize(d);
            SettingsService.Save();
        }
        catch (Exception ex) { LogService.Write("SETTINGS", "Media delete sync failed: " + ex.Message); }
    }

    // ------------------------------------------------------------------ "remember original location" (opt-in)

    /// <summary>Stores where a file came from, but only if the user ticked "remember original location".</summary>
    public static void RememberSource(bool wallpaper, string collection, string fileName, string originalPath)
    {
        var d = SettingsService.Current;
        if (!d.MediaRememberSourcePaths) return;
        d.MediaSourcePaths[Key(wallpaper, collection, fileName)] = originalPath;
    }

    public static string? GetRememberedSource(bool wallpaper, string collection, string fileName)
        => SettingsService.Current.MediaSourcePaths.TryGetValue(Key(wallpaper, collection, fileName), out var p) ? p : null;

    private static string Key(bool wallpaper, string collection, string fileName)
        => (wallpaper ? KindWallpaper : KindVideo) + ":" + collection + "/" + fileName;
}
