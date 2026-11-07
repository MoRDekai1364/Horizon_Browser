using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Stealth.Services;

/// <summary>One media file inside a library (loose file or inside a collection).</summary>
public sealed record MediaItem(string Name, string FullPath, long SizeBytes, DateTime LastWriteUtc);

/// <summary>A collection = one sub-folder of the library root.</summary>
public sealed record MediaCollection(string Name, string FullPath, int ItemCount);

public readonly record struct MediaProgress(int Done, int Total, string CurrentFile);

public sealed class MediaImportResult
{
    /// <summary>Source path -> destination path (inside the library). Phase 2/4 may persist the source
    /// only when the user explicitly opted in to "remember original location".</summary>
    public List<KeyValuePair<string, string>> Copied { get; } = new();
    public List<string> Skipped { get; } = new();
    public List<(string Path, string Error)> Failed { get; } = new();
    public bool Cancelled { get; set; }
    /// <summary>Collection the files went into ("" = loose files in the library root).</summary>
    public string CollectionName { get; set; } = "";
}

/// <summary>
/// Folder-based media library: &lt;Root&gt;\*.ext are loose items, &lt;Root&gt;\&lt;Name&gt;\*.ext are collections.
/// Only one folder level is a collection; deeper folders are ignored by listings.
/// No UI, no settings access - Phase 2+ build on top of this.
/// </summary>
public sealed class MediaLibrary
{
    public const int MaxNameLength = 60;
    private const string MigrationMarker = ".library_migrated";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON","PRN","AUX","NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    private readonly Func<string> _rootProvider;
    private readonly HashSet<string> _exts;
    private readonly object _gate = new();

    /// <summary>Raised after any mutation (add/remove/rename/delete/import).</summary>
    public event Action? Changed;
    /// <summary>(oldName, newName) - lets the settings layer keep an active selection valid.</summary>
    public event Action<string, string>? CollectionRenamed;
    public event Action<string>? CollectionDeleted;

    public MediaLibrary(Func<string> rootProvider, IEnumerable<string> extensions)
    {
        _rootProvider = rootProvider;
        _exts = new HashSet<string>(extensions.Select(e => e.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Library root. Resolved on every call because ConfigService.UserDataRoot can change at startup.</summary>
    public string Root
    {
        get
        {
            string r = _rootProvider();
            Directory.CreateDirectory(r);
            return r;
        }
    }

    public bool IsMediaFile(string path)
        => !string.IsNullOrEmpty(path) && _exts.Contains(Path.GetExtension(path));

    // ---------------------------------------------------------------- names / paths

    /// <summary>Makes a user-typed collection name safe. Returns "" when nothing usable remains.</summary>
    public static string SanitizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw.Trim())
            sb.Append(Array.IndexOf(invalid, c) >= 0 || char.IsControl(c) ? '_' : c);

        string s = sb.ToString().Trim('.', ' ');
        if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength).Trim('.', ' ');
        if (s.Length == 0 || s.All(c => c == '_')) return "";
        if (ReservedNames.Contains(s) || ReservedNames.Contains(Path.GetFileNameWithoutExtension(s))) s = "_" + s;
        return s;
    }

    private static bool IsSafeExistingName(string name)
        => !string.IsNullOrWhiteSpace(name)
           && name != "." && name != ".."
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>"" = root (loose files). Throws on traversal / invalid names.</summary>
    private string CollectionPath(string name)
    {
        string root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(name)) return root;
        if (!IsSafeExistingName(name)) throw new ArgumentException("Invalid collection name.", nameof(name));

        string full = Path.GetFullPath(Path.Combine(root, name));
        string? parent = Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Collection must be a direct sub-folder of the library.", nameof(name));
        return full;
    }

    public bool CollectionExists(string name)
    {
        try { return string.IsNullOrEmpty(name) || Directory.Exists(CollectionPath(name)); }
        catch { return false; }
    }

    /// <summary>First free "Name", "Name (2)", "Name (3)"... Call under _gate when you also create the folder.</summary>
    private string UniqueCollectionNameUnsafe(string baseName)
    {
        string root = Root;
        string candidate = baseName;
        int n = 2;
        while (Directory.Exists(Path.Combine(root, candidate)) || File.Exists(Path.Combine(root, candidate)))
        {
            string suffix = $" ({n++})";
            int keep = Math.Max(1, MaxNameLength - suffix.Length);
            candidate = (baseName.Length > keep ? baseName.Substring(0, keep).TrimEnd('.', ' ') : baseName) + suffix;
        }
        return candidate;
    }

    private static string UniqueFilePath(string folder, string fileName)
    {
        string safe = Path.GetFileName(fileName);
        string stem = Path.GetFileNameWithoutExtension(safe);
        string ext = Path.GetExtension(safe);
        string candidate = Path.Combine(folder, safe);
        int n = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
            candidate = Path.Combine(folder, $"{stem} ({n++}){ext}");
        return candidate;
    }

    // ---------------------------------------------------------------- listing

    public IReadOnlyList<MediaCollection> ListCollections()
    {
        var list = new List<MediaCollection>();
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(Root))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith('.')) continue;
                try
                {
                    var attr = File.GetAttributes(dir);
                    if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                }
                catch { continue; }
                list.Add(new MediaCollection(name, dir, CountItems(dir)));
            }
        }
        catch (Exception ex) { LogService.Write("MediaLibrary", "ListCollections failed: " + ex.Message); }
        return list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Items of a collection; "" = loose files in the library root.</summary>
    public IReadOnlyList<MediaItem> ListItems(string collectionName)
    {
        var items = new List<MediaItem>();
        try
        {
            string folder = CollectionPath(collectionName);
            if (!Directory.Exists(folder)) return items;
            foreach (string f in Directory.EnumerateFiles(folder))
            {
                if (!IsMediaFile(f)) continue;
                try
                {
                    var fi = new FileInfo(f);
                    items.Add(new MediaItem(fi.Name, fi.FullName, fi.Length, fi.LastWriteTimeUtc));
                }
                catch { /* file vanished */ }
            }
        }
        catch (Exception ex) { LogService.Write("MediaLibrary", "ListItems failed: " + ex.Message); }
        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private int CountItems(string folder)
    {
        try { return Directory.EnumerateFiles(folder).Count(IsMediaFile); }
        catch { return 0; }
    }

    /// <summary>Resolves (collection, fileName) from settings to a real file; false if it no longer exists.</summary>
    public bool TryResolve(string collectionName, string fileName, out string fullPath)
    {
        fullPath = "";
        try
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName)) return false;
            string p = Path.Combine(CollectionPath(collectionName), fileName);
            if (!File.Exists(p) || !IsMediaFile(p)) return false;
            fullPath = p;
            return true;
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- collection CRUD

    /// <summary>Creates a collection. Throws InvalidOperationException (user-facing message) on bad/duplicate name.</summary>
    public string CreateCollection(string desiredName)
    {
        string name = SanitizeName(desiredName);
        if (name.Length == 0) throw new InvalidOperationException("Please enter a valid collection name.");
        lock (_gate)
        {
            string path = CollectionPath(name);
            if (Directory.Exists(path) || File.Exists(path))
                throw new InvalidOperationException($"A collection named \"{name}\" already exists.");
            Directory.CreateDirectory(path);
        }
        Changed?.Invoke();
        return name;
    }

    /// <summary>Renames a collection and returns the final name. Case-only renames are supported.</summary>
    public string RenameCollection(string oldName, string desiredNewName)
    {
        if (string.IsNullOrEmpty(oldName)) throw new InvalidOperationException("The loose-files area cannot be renamed.");
        string newName = SanitizeName(desiredNewName);
        if (newName.Length == 0) throw new InvalidOperationException("Please enter a valid collection name.");

        lock (_gate)
        {
            string src = CollectionPath(oldName);
            string dst = CollectionPath(newName);
            if (!Directory.Exists(src)) throw new InvalidOperationException("That collection no longer exists.");
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) return newName;

            bool caseOnly = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (Directory.Exists(dst) || File.Exists(dst)))
                throw new InvalidOperationException($"A collection named \"{newName}\" already exists.");

            if (caseOnly)
            {
                string tmp = src + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".rename";
                Directory.Move(src, tmp);
                Directory.Move(tmp, dst);
            }
            else
            {
                Directory.Move(src, dst);
            }
        }
        CollectionRenamed?.Invoke(oldName, newName);
        Changed?.Invoke();
        return newName;
    }

    public void DeleteCollection(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("The loose-files area cannot be deleted.");
        lock (_gate)
        {
            string path = CollectionPath(name);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        CollectionDeleted?.Invoke(name);
        Changed?.Invoke();
    }

    public void DeleteItem(string collectionName, string fileName)
    {
        if (!TryResolve(collectionName, fileName, out string path)) return;
        lock (_gate) { File.Delete(path); }
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- scanning

    /// <summary>All media files in a folder tree (depth-limited, access errors ignored). Used by drag-and-drop.</summary>
    public IReadOnlyList<string> ScanFolder(string folder, int maxDepth = 4)
    {
        var result = new List<string>();
        try { ScanInto(folder, 0, maxDepth, result); }
        catch (Exception ex) { LogService.Write("MediaLibrary", "ScanFolder failed: " + ex.Message); }
        return result.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ScanInto(string folder, int depth, int maxDepth, List<string> output)
    {
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
        foreach (string f in Directory.EnumerateFiles(folder, "*", opts))
            if (IsMediaFile(f)) output.Add(f);

        if (depth >= maxDepth) return;
        foreach (string d in Directory.EnumerateDirectories(folder, "*", opts))
            ScanInto(d, depth + 1, maxDepth, output);
    }

    /// <summary>True if the folder (or its sub-folders) holds at least one supported file.</summary>
    public bool FolderHasMedia(string folder)
    {
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 4 };
            return Directory.EnumerateFiles(folder, "*", opts).Any(IsMediaFile);
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- import (copy into user data)

    /// <summary>Copies files into a collection ("" = loose). Creates the collection if missing.
    /// Name clashes get " (2)", " (3)" suffixes; nothing is overwritten. Cancel-safe (no half-copied files).</summary>
    public async Task<MediaImportResult> ImportFilesAsync(
        IEnumerable<string> sources, string collectionName,
        IProgress<MediaProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new MediaImportResult { CollectionName = collectionName };

        string destFolder;
        lock (_gate)
        {
            destFolder = CollectionPath(collectionName);
            Directory.CreateDirectory(destFolder);
        }

        var files = new List<string>();
        foreach (string s in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(s) && IsMediaFile(s)) files.Add(s); else result.Skipped.Add(s);
        }

        int done = 0;
        foreach (string src in files)
        {
            if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
            progress?.Report(new MediaProgress(done, files.Count, Path.GetFileName(src)));

            try
            {
                string srcDir = Path.GetDirectoryName(Path.GetFullPath(src))!.TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(srcDir, destFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    result.Skipped.Add(src); // already inside this collection
                }
                else
                {
                    string dest = await CopyIntoAsync(src, destFolder, ct).ConfigureAwait(false);
                    result.Copied.Add(new KeyValuePair<string, string>(src, dest));
                }
            }
            catch (OperationCanceledException) { result.Cancelled = true; break; }
            catch (Exception ex) { result.Failed.Add((src, ex.Message)); }

            done++;
        }

        progress?.Report(new MediaProgress(done, files.Count, ""));
        if (result.Copied.Count > 0) Changed?.Invoke();
        return result;
    }

    /// <summary>Drag-drop choice A: the dropped folder becomes a new collection. The name is made unique automatically.</summary>
    public async Task<MediaImportResult> ImportFolderAsCollectionAsync(
        string folder, string? desiredName = null,
        IProgress<MediaProgress>? progress = null, CancellationToken ct = default)
    {
        var files = ScanFolder(folder);
        if (files.Count == 0) return new MediaImportResult();

        string baseName = SanitizeName(desiredName);
        if (baseName.Length == 0) baseName = SanitizeName(new DirectoryInfo(folder).Name);
        if (baseName.Length == 0) baseName = "Collection";

        string finalName;
        lock (_gate)
        {
            finalName = UniqueCollectionNameUnsafe(baseName);
            Directory.CreateDirectory(CollectionPath(finalName));
        }

        var result = await ImportFilesAsync(files, finalName, progress, ct).ConfigureAwait(false);

        if (result.Copied.Count == 0)
        {
            lock (_gate)
            {
                try
                {
                    string p = CollectionPath(finalName);
                    if (Directory.Exists(p) && !Directory.EnumerateFileSystemEntries(p).Any()) Directory.Delete(p);
                }
                catch { }
            }
            Changed?.Invoke();
        }
        return result;
    }

    /// <summary>Drag-drop choice B: extract every image from the folder into an existing collection ("" = loose).</summary>
    public Task<MediaImportResult> ImportFolderImagesAsync(
        string folder, string targetCollection,
        IProgress<MediaProgress>? progress = null, CancellationToken ct = default)
        => ImportFilesAsync(ScanFolder(folder), targetCollection, progress, ct);

    private async Task<string> CopyIntoAsync(string src, string destFolder, CancellationToken ct)
    {
        // ".part" has no media extension and starts with '.', so listings never show a half-copied file.
        string temp = Path.Combine(destFolder, "." + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
            }

            lock (_gate)
            {
                string dest = UniqueFilePath(destFolder, Path.GetFileName(src));
                File.Move(temp, dest);
                return dest;
            }
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    // ---------------------------------------------------------------- one-time migration from the old exe-side folder

    /// <summary>True when the legacy folder holds media and this library has not been seeded yet.</summary>
    public bool NeedsMigration(string legacyFolder)
    {
        try
        {
            if (!Directory.Exists(legacyFolder)) return false;
            if (File.Exists(Path.Combine(Root, MigrationMarker))) return false;
            return FolderHasMedia(legacyFolder);
        }
        catch { return false; }
    }

    /// <summary>Copies loose legacy files into the root and each legacy sub-folder into a collection of the same name
    /// (merged if it exists). The legacy folder is never modified. Writes a marker only on full completion,
    /// so an interrupted run resumes; already-copied files are skipped by name+size.</summary>
    public async Task<MediaImportResult> MigrateFromFolderAsync(
        string legacyFolder, IProgress<MediaProgress>? progress = null, CancellationToken ct = default)
    {
        var total = new MediaImportResult();
        if (!NeedsMigration(legacyFolder)) return total;

        var plan = new List<(string Collection, string File)>();
        foreach (string f in Directory.EnumerateFiles(legacyFolder).Where(IsMediaFile))
            plan.Add(("", f));
        foreach (string dir in Directory.EnumerateDirectories(legacyFolder))
        {
            string name = SanitizeName(Path.GetFileName(dir));
            if (name.Length == 0) continue;
            foreach (string f in Directory.EnumerateFiles(dir).Where(IsMediaFile))
                plan.Add((name, f));
        }

        int done = 0;
        foreach (var (collection, file) in plan)
        {
            if (ct.IsCancellationRequested) { total.Cancelled = true; return total; }
            progress?.Report(new MediaProgress(done++, plan.Count, Path.GetFileName(file)));

            try
            {
                string destFolder;
                lock (_gate) { destFolder = CollectionPath(collection); Directory.CreateDirectory(destFolder); }

                string existing = Path.Combine(destFolder, Path.GetFileName(file));
                if (File.Exists(existing) && new FileInfo(existing).Length == new FileInfo(file).Length)
                {
                    total.Skipped.Add(file); // copied by an earlier, interrupted run
                    continue;
                }

                string dest = await CopyIntoAsync(file, destFolder, ct).ConfigureAwait(false);
                total.Copied.Add(new KeyValuePair<string, string>(file, dest));
            }
            catch (OperationCanceledException) { total.Cancelled = true; return total; }
            catch (Exception ex) { total.Failed.Add((file, ex.Message)); }
        }

        if (total.Failed.Count == 0)
        {
            try { File.WriteAllText(Path.Combine(Root, MigrationMarker), DateTime.UtcNow.ToString("o")); } catch { }
        }
        Changed?.Invoke();
        return total;
    }
}
