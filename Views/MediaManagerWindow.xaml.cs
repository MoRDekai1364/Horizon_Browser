using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public enum MediaKind { Wallpaper, StartupVideo }

/// <summary>One thumbnail in the grid.</summary>
public sealed class MediaTile : INotifyPropertyChanged
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsVideo { get; }

    private BitmapSource? _thumb;
    private bool _active;

    public MediaTile(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
        IsVideo = string.Equals(Path.GetExtension(fullPath), ".mp4", StringComparison.OrdinalIgnoreCase);
    }

    public BitmapSource? Thumbnail
    {
        get => _thumb;
        set { _thumb = value; Raise(nameof(Thumbnail)); Raise(nameof(PlaceholderText)); }
    }

    public bool IsActive
    {
        get => _active;
        set { _active = value; Raise(nameof(IsActive)); Raise(nameof(BadgeVisibility)); }
    }

    public Visibility BadgeVisibility => _active ? Visibility.Visible : Visibility.Collapsed;
    public string PlaceholderText => IsVideo ? "\u25B6 VIDEO" : (_thumb == null ? "\u2026" : "");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>One row of the collections rail. Name "" = loose files in the library root.</summary>
public sealed class CollectionRow
{
    public string Name { get; }
    public string Display { get; }
    public string CountText { get; }
    public string ActiveMark { get; }

    public CollectionRow(string name, string looseName, int count, bool active)
    {
        Name = name;
        Display = name.Length == 0 ? looseName : name;
        CountText = count.ToString();
        ActiveMark = active ? "\u25B6" : "";
    }
}

/// <summary>Everything that differs between the Wallpapers tab and the Startup Video tab:
/// which library, which settings fields, and the wording.</summary>
internal sealed class MediaKindProfile
{
    public MediaKind Kind { get; }
    public MediaKindProfile(MediaKind kind) { Kind = kind; }

    private static SettingsData D => SettingsService.Current;
    public bool IsWallpaper => Kind == MediaKind.Wallpaper;

    // library + wording
    public MediaLibrary Library => IsWallpaper ? WallpaperLibraryService.Library : StartupVideoLibraryService.Library;
    public string KeyPrefix => IsWallpaper ? "W" : "V";
    public string HeaderTitle => IsWallpaper ? "WALLPAPERS //" : "STARTUP VIDEOS //";
    public string HeaderSub => IsWallpaper ? "Pick a wallpaper or a rotation" : "Pick the video played when the browser starts";
    public string LooseName => IsWallpaper ? "Loose wallpapers" : "Loose videos";
    public string Singular => IsWallpaper ? "wallpaper" : "video";
    public string Plural => IsWallpaper ? "wallpapers" : "videos";
    public string AddText => IsWallpaper ? "+ Add images..." : "+ Add videos...";
    public string SetSingleText => IsWallpaper ? "Set as wallpaper" : "Set as startup video";
    public string PlayText => IsWallpaper ? "\u25B6 Play collection" : "\u25B6 Use collection";
    public string FileFilter => IsWallpaper
        ? "Wallpapers|*.png;*.jpg;*.jpeg;*.bmp;*.mp4|All files|*.*"
        : "Videos|*.mp4|All files|*.*";
    public string DropHint => IsWallpaper
        ? "Drop image files (png, jpg, bmp, mp4) or a folder that contains images."
        : "Drop video files (mp4) or a folder that contains videos.";
    public string DropTitle => IsWallpaper ? "Drop images or a folder to add them" : "Drop videos or a folder to add them";

    // migration of the old exe-side folders
    public bool MigrationPending => IsWallpaper ? WallpaperResolver.MigrationPending : StartupVideoResolver.MigrationPending;
    public Task EnsureMigrationAsync(IProgress<MediaProgress> p)
        => IsWallpaper ? WallpaperResolver.EnsureMigrationAsync(p) : StartupVideoResolver.EnsureMigrationAsync(p);

    // selection settings
    public string Source
    {
        get => IsWallpaper ? D.WallpaperSource : D.StartupVideoSource;
        set { if (IsWallpaper) D.WallpaperSource = value; else D.StartupVideoSource = value; }
    }
    public string ActiveCollection
    {
        get => IsWallpaper ? D.WallpaperActiveCollection : D.StartupVideoActiveCollection;
        set { if (IsWallpaper) D.WallpaperActiveCollection = value; else D.StartupVideoActiveCollection = value; }
    }
    public MediaRef Single
    {
        get => IsWallpaper ? D.WallpaperSingle : D.StartupVideoSingle;
        set { if (IsWallpaper) D.WallpaperSingle = value; else D.StartupVideoSingle = value; }
    }
    public List<MediaRef> Selection
    {
        get => IsWallpaper ? D.WallpaperSelection : D.StartupVideoSelection;
        set { if (IsWallpaper) D.WallpaperSelection = value; else D.StartupVideoSelection = value; }
    }
    public string PlayOrder
    {
        get => IsWallpaper ? D.WallpaperPlayOrder : D.StartupVideoPlayOrder;
        set { if (IsWallpaper) D.WallpaperPlayOrder = value; else D.StartupVideoPlayOrder = value; }
    }
    public int PlayIndex
    {
        get => IsWallpaper ? D.WallpaperPlayIndex : D.StartupVideoPlayIndex;
        set { if (IsWallpaper) D.WallpaperPlayIndex = value; else D.StartupVideoPlayIndex = value; }
    }
}

/// <summary>
/// Fullscreen media manager with two tabs: Wallpapers and Startup Video.
/// Collections rail + thumbnail grid, add / delete / rename, set a single item, rotate a selection,
/// play a whole collection, drag and drop (see MediaManagerWindow.DragDrop.cs).
/// </summary>
public partial class MediaManagerWindow : Window
{
    private static readonly SemaphoreSlim ThumbGate = new(3);

    private MediaKindProfile _p = new(MediaKind.Wallpaper);
    private readonly ObservableCollection<CollectionRow> _rows = new();
    private readonly ObservableCollection<MediaTile> _tiles = new();
    private CancellationTokenSource? _thumbCts;
    private string _currentCollection = "";
    private bool _busy;
    private bool _loadingUi;

    private MediaLibrary Lib => _p.Library;

    /// <summary>Which tab opens first. Set before ShowDialog.</summary>
    public MediaKind InitialKind { get; set; } = MediaKind.Wallpaper;

    /// <summary>True when the wallpaper selection changed; the caller should re-apply the wallpaper after closing.</summary>
    public bool WallpaperChanged { get; private set; }

    /// <summary>Raised after each change that affects which wallpaper is shown (for live apply).</summary>
    public event Action? WallpaperApplied;

    public MediaManagerWindow()
    {
        InitializeComponent();

        // A borderless maximized WPF window would cover the taskbar.
        var area = SystemParameters.WorkArea;
        MaxWidth = area.Width;
        MaxHeight = area.Height;

        ListCollections.ItemsSource = _rows;
        ListTiles.ItemsSource = _tiles;

        Loaded += async (_, __) => await OnLoadedAsync();
        Closed += (_, __) => { try { _thumbCts?.Cancel(); } catch { } try { _importCts?.Cancel(); } catch { } };
    }

    // ------------------------------------------------------------------ startup / tabs

    private async Task OnLoadedAsync()
    {
        _p = new MediaKindProfile(InitialKind);
        ApplyKindUi();

        _loadingUi = true;
        ChkRemember.IsChecked = SettingsService.Current.MediaRememberSourcePaths;
        _loadingUi = false;

        await PrepareLibraryAsync();
        RefreshCollections("", reloadTiles: true);
    }

    private async Task PrepareLibraryAsync()
    {
        if (!_p.MigrationPending) return;

        var profile = _p;
        SetBusy(true, $"Preparing the {profile.Singular} library (first run only)...");
        try
        {
            var progress = new Progress<MediaProgress>(p => TxtStatus.Text = $"Copying {profile.Plural}... {p.Done}/{p.Total}");
            await profile.EnsureMigrationAsync(progress);
        }
        catch (Exception ex)
        {
            LogService.Write("MEDIA", "Manager migration failed: " + ex.Message);
        }
        SetBusy(false, profile.MigrationPending
            ? $"Some {profile.Plural} could not be copied. Check the log, then reopen this window to retry."
            : "");
    }

    private async Task SwitchKindAsync(MediaKind kind)
    {
        if (_busy || _p.Kind == kind) { SyncTabs(); return; }

        try { _thumbCts?.Cancel(); } catch { }
        _tiles.Clear();
        _p = new MediaKindProfile(kind);
        _currentCollection = "";
        ApplyKindUi();

        await PrepareLibraryAsync();
        RefreshCollections("", reloadTiles: true);
        SetStatus("");
    }

    private void TabWallpapers_Click(object sender, RoutedEventArgs e) => _ = SwitchKindAsync(MediaKind.Wallpaper);
    private void TabVideos_Click(object sender, RoutedEventArgs e) => _ = SwitchKindAsync(MediaKind.StartupVideo);

    private void SyncTabs()
    {
        TabWallpapers.IsChecked = _p.IsWallpaper;
        TabVideos.IsChecked = !_p.IsWallpaper;
    }

    /// <summary>Applies the wording of the active tab and loads its play order.</summary>
    private void ApplyKindUi()
    {
        SyncTabs();
        TxtHeaderTitle.Text = _p.HeaderTitle;
        TxtHeaderSub.Text = _p.HeaderSub;
        Title = _p.IsWallpaper ? "Wallpaper Manager" : "Startup Video Manager";
        BtnAdd.Content = _p.AddText;
        BtnSetSingle.Content = _p.SetSingleText;
        BtnSetSingle.ToolTip = $"Select one {_p.Singular} (or double-click it)";
        BtnPlayCollection.Content = _p.PlayText;
        BtnPlayCollection.ToolTip = $"Use every {_p.Singular} of the selected collection";
        TxtDropTitle.Text = _p.DropTitle;

        _loadingUi = true;
        CmbOrder.SelectedIndex = _p.PlayOrder == MediaSource.Sequential ? 1 : 0;
        _loadingUi = false;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        PnlBody.IsEnabled = !busy;
        ProgBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (status != null) TxtStatus.Text = status;
    }

    private void SetStatus(string text) => TxtStatus.Text = text;

    // ------------------------------------------------------------------ helpers

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private bool IsTileActive(string collection, string fileName)
    {
        if (!SettingsService.Current.UseMediaLibrary) return false;
        switch (_p.Source)
        {
            case MediaSource.Single:
                return Same(_p.Single.Collection, collection) && Same(_p.Single.FileName, fileName);
            case MediaSource.Selection:
                return _p.Selection.Any(r => Same(r.Collection, collection) && Same(r.FileName, fileName));
            default:
                return Same(_p.ActiveCollection, collection);
        }
    }

    private string CurrentOrder => CmbOrder.SelectedIndex == 1 ? MediaSource.Sequential : MediaSource.Random;

    private bool EnsureReady()
    {
        if (!_p.MigrationPending) return true;
        MessageBox.Show(this,
            $"The {_p.Singular} library is not ready yet because some of the original files could not be copied.\n\nClose and reopen this window to retry.",
            "Media", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    // ------------------------------------------------------------------ lists

    private void RefreshCollections(string select, bool reloadTiles)
    {
        _loadingUi = true;
        try
        {
            bool collectionMode = SettingsService.Current.UseMediaLibrary && _p.Source == MediaSource.Collection;

            _rows.Clear();
            _rows.Add(new CollectionRow("", _p.LooseName, Lib.ListItems("").Count,
                collectionMode && string.IsNullOrEmpty(_p.ActiveCollection)));
            foreach (var c in Lib.ListCollections())
                _rows.Add(new CollectionRow(c.Name, _p.LooseName, c.ItemCount, collectionMode && Same(_p.ActiveCollection, c.Name)));

            var sel = _rows.FirstOrDefault(r => Same(r.Name, select)) ?? _rows[0];
            ListCollections.SelectedItem = sel;
            _currentCollection = sel.Name;
        }
        finally { _loadingUi = false; }

        if (reloadTiles) LoadTiles();
        else
        {
            foreach (var t in _tiles) t.IsActive = IsTileActive(_currentCollection, t.Name);
            UpdateButtons();
        }
    }

    private void LoadTiles()
    {
        try { _thumbCts?.Cancel(); } catch { }
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;

        _tiles.Clear();
        var items = Lib.ListItems(_currentCollection);
        foreach (var it in items)
        {
            var tile = new MediaTile(it.Name, it.FullPath) { IsActive = IsTileActive(_currentCollection, it.Name) };
            _tiles.Add(tile);
            _ = LoadThumbAsync(tile, ct);
        }

        TxtCollectionTitle.Text = _currentCollection.Length == 0 ? _p.LooseName : _currentCollection;
        TxtCollectionCount.Text = items.Count == 1 ? "1 item" : $"{items.Count} items";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        int sel = ListTiles.SelectedItems.Count;
        bool loose = _currentCollection.Length == 0;
        BtnSetSingle.IsEnabled = sel == 1;
        BtnUseSelection.IsEnabled = sel >= 2;
        BtnRemove.IsEnabled = sel >= 1;
        BtnPlayCollection.IsEnabled = _tiles.Count > 0;
        BtnRenameCollection.IsEnabled = !loose;
        BtnDeleteCollection.IsEnabled = !loose;
    }

    // Thumbnails are decoded small (320 px wide) so a grid of 8K wallpapers stays light.
    // The wallpaper that is actually applied is always decoded at full resolution elsewhere.
    private static async Task LoadThumbAsync(MediaTile tile, CancellationToken ct)
    {
        if (tile.IsVideo) return;
        bool entered = false;
        try
        {
            await ThumbGate.WaitAsync(ct);
            entered = true;
            var bmp = await Task.Run(() => DecodeThumb(tile.FullPath), ct);
            if (!ct.IsCancellationRequested) tile.Thumbnail = bmp;
        }
        catch (OperationCanceledException) { }
        catch { }
        finally { if (entered) ThumbGate.Release(); }
    }

    private static BitmapSource? DecodeThumb(string path)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(path);
            b.DecodePixelWidth = 320;
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ selection / applying

    private void ListCollections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        if (ListCollections.SelectedItem is not CollectionRow row) return;
        _currentCollection = row.Name;
        LoadTiles();
    }

    private void ListTiles_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void ListTiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_busy || ListTiles.SelectedItems.Count != 1) return;
        if (e.OriginalSource is DependencyObject src && ItemsControl.ContainerFromElement(ListTiles, src) is ListBoxItem)
            SetSingle();
    }

    private void BtnSetSingle_Click(object sender, RoutedEventArgs e) => SetSingle();

    private void SetSingle()
    {
        if (!EnsureReady()) return;
        if (ListTiles.SelectedItem is not MediaTile tile) return;

        _p.Source = MediaSource.Single;
        _p.Single = new MediaRef { Collection = _currentCollection, FileName = tile.Name };
        Commit(_p.IsWallpaper ? "Wallpaper set." : "Startup video set.");
    }

    private void BtnUseSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureReady()) return;
        var refs = ListTiles.SelectedItems.Cast<MediaTile>()
            .OrderBy(t => _tiles.IndexOf(t))
            .Select(t => new MediaRef { Collection = _currentCollection, FileName = t.Name })
            .ToList();
        if (refs.Count < 2) return;

        _p.Source = MediaSource.Selection;
        _p.Selection = refs;
        _p.PlayOrder = CurrentOrder;
        _p.PlayIndex = -1;
        Commit($"Rotating {refs.Count} selected {_p.Plural} ({CurrentOrder.ToLowerInvariant()}).");
    }

    private void BtnPlayCollection_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureReady()) return;
        if (_tiles.Count == 0)
        {
            MessageBox.Show(this, $"This collection has no {_p.Plural} yet.", "Media", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _p.Source = MediaSource.Collection;
        _p.ActiveCollection = _currentCollection;
        _p.PlayOrder = CurrentOrder;
        _p.PlayIndex = -1;
        string name = _currentCollection.Length == 0 ? _p.LooseName : _currentCollection;
        Commit($"Using collection \"{name}\" ({CurrentOrder.ToLowerInvariant()}).");
    }

    private void Commit(string status)
    {
        var d = SettingsService.Current;
        d.UseMediaLibrary = true;
        MediaSelectionSync.Normalize(d);
        SettingsService.Save();

        if (_p.IsWallpaper)
        {
            WallpaperChanged = true;
            try { WallpaperApplied?.Invoke(); } catch { }
        }

        RefreshCollections(_currentCollection, reloadTiles: false);
        SetStatus(status);
    }

    private void CmbOrder_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || !IsLoaded) return;
        _p.PlayOrder = CurrentOrder;
        SettingsService.Save();
    }

    private void ChkRemember_Click(object sender, RoutedEventArgs e)
    {
        var d = SettingsService.Current;
        d.MediaRememberSourcePaths = ChkRemember.IsChecked == true;
        if (!d.MediaRememberSourcePaths) d.MediaSourcePaths.Clear(); // forget everything stored so far
        SettingsService.Save();
        SetStatus(d.MediaRememberSourcePaths
            ? "Original file locations will be remembered for newly added files."
            : "Original file locations are no longer remembered.");
    }

    // ------------------------------------------------------------------ adding files

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = _p.IsWallpaper ? "Add wallpapers" : "Add startup videos",
            Multiselect = true,
            Filter = _p.FileFilter
        };
        if (dlg.ShowDialog(this) != true) return;
        await ImportAsync(dlg.FileNames);
    }

    /// <summary>Copies files into the current collection.</summary>
    public async Task ImportAsync(IEnumerable<string> files)
    {
        if (_busy) return;
        SetBusy(true, $"Adding {_p.Plural}...");
        string finalStatus = "";
        try
        {
            string label = _p.Plural;
            var progress = new Progress<MediaProgress>(p => TxtStatus.Text = $"Adding {label}... {p.Done}/{p.Total}");
            var res = await Lib.ImportFilesAsync(files, _currentCollection, progress);

            var d = SettingsService.Current;
            if (d.MediaRememberSourcePaths && res.Copied.Count > 0)
            {
                foreach (var pair in res.Copied)
                    MediaSelectionSync.RememberSource(_p.IsWallpaper, _currentCollection, Path.GetFileName(pair.Value), pair.Key);
                SettingsService.Save();
            }

            finalStatus = $"Added {res.Copied.Count}";
            if (res.Skipped.Count > 0) finalStatus += $", skipped {res.Skipped.Count}";
            if (res.Failed.Count > 0) finalStatus += $", failed {res.Failed.Count}";
            if (res.Cancelled) finalStatus += " (cancelled)";
            finalStatus += ".";

            if (res.Failed.Count > 0)
            {
                string detail = string.Join("\n", res.Failed.Take(5).Select(f => Path.GetFileName(f.Path) + ": " + f.Error));
                MessageBox.Show(this, "Some files could not be added:\n\n" + detail, "Media", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "MediaManager.Import");
            finalStatus = "Adding files failed.";
        }
        finally
        {
            SetBusy(false, finalStatus);
        }
        RefreshCollections(_currentCollection, reloadTiles: true);
    }

    // ------------------------------------------------------------------ deleting items

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        var tiles = ListTiles.SelectedItems.Cast<MediaTile>().ToList();
        if (tiles.Count == 0) return;

        var answer = MessageBox.Show(this,
            tiles.Count == 1
                ? $"Delete \"{tiles[0].Name}\" from the browser's {_p.Singular} storage?"
                : $"Delete {tiles.Count} {_p.Plural} from the browser's {_p.Singular} storage?",
            $"Delete {_p.Plural}", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var d = SettingsService.Current;
        bool touchedActive = false;
        foreach (var t in tiles)
        {
            if (IsTileActive(_currentCollection, t.Name)) touchedActive = true;
            try { Lib.DeleteItem(_currentCollection, t.Name); }
            catch (Exception ex) { LogService.Write("MEDIA", "Delete failed: " + ex.Message); continue; }

            d.MediaSourcePaths.Remove(_p.KeyPrefix + ":" + _currentCollection + "/" + t.Name);
            _p.Selection.RemoveAll(r => Same(r.Collection, _currentCollection) && Same(r.FileName, t.Name));
            if (Same(_p.Single.Collection, _currentCollection) && Same(_p.Single.FileName, t.Name))
                _p.Single = new MediaRef();
        }

        MediaSelectionSync.Normalize(d);
        SettingsService.Save();
        if (touchedActive && _p.IsWallpaper) WallpaperChanged = true;

        RefreshCollections(_currentCollection, reloadTiles: true);
        SetStatus(tiles.Count == 1 ? "Deleted." : $"{tiles.Count} items deleted.");
    }

    // ------------------------------------------------------------------ collections

    private void BtnNewCollection_Click(object sender, RoutedEventArgs e)
    {
        string? name = Prompt("New collection", "Name of the new collection:", "");
        if (name == null) return;
        try
        {
            string final = Lib.CreateCollection(name);
            RefreshCollections(final, reloadTiles: true);
            SetStatus($"Collection \"{final}\" created.");
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "New collection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "MediaManager.CreateCollection");
            MessageBox.Show(this, "The collection could not be created:\n" + ex.Message, "New collection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnRenameCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCollection.Length == 0) return;
        string? name = Prompt("Rename collection", "New name:", _currentCollection);
        if (name == null) return;
        try
        {
            string final = Lib.RenameCollection(_currentCollection, name);
            RefreshCollections(final, reloadTiles: true);
            SetStatus($"Collection renamed to \"{final}\".");
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Rename collection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "MediaManager.RenameCollection");
            MessageBox.Show(this, "The collection could not be renamed:\n" + ex.Message, "Rename collection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnDeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCollection.Length == 0) return;
        string name = _currentCollection;

        var answer = MessageBox.Show(this,
            $"Delete the collection \"{name}\" and all {_tiles.Count} {_p.Singular}(s) in it?\n\nThis removes the files from the browser's {_p.Singular} storage.",
            "Delete collection", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            bool wasInUse = SettingsService.Current.UseMediaLibrary && _tiles.Any(t => t.IsActive);
            Lib.DeleteCollection(name); // MediaSelectionSync cleans the saved selection through the library event
            if (wasInUse && _p.IsWallpaper) WallpaperChanged = true;
            RefreshCollections("", reloadTiles: true);
            SetStatus($"Collection \"{name}\" deleted.");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "MediaManager.DeleteCollection");
            MessageBox.Show(this, "The collection could not be deleted:\n" + ex.Message, "Delete collection", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshCollections("", reloadTiles: true);
        }
    }

    // ------------------------------------------------------------------ small input dialog

    private string? Prompt(string title, string label, string initial)
    {
        Brush bg = TryFindResource("Brush_DeepBackground") as Brush ?? Brushes.Black;
        Brush fg = TryFindResource("Brush_SoftText") as Brush ?? Brushes.White;

        var box = new TextBox
        {
            Text = initial,
            Height = 30,
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 14),
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = MediaLibrary.MaxNameLength
        };
        var ok = new Button { Content = "OK", Width = 84, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 84, Height = 30, IsCancel = true };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = fg, FontSize = 13 });
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = title,
            Owner = this,
            Content = panel,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = bg
        };
        ok.Click += (_, __) => win.DialogResult = true;
        win.Loaded += (_, __) => { box.Focus(); box.SelectAll(); };

        return win.ShowDialog() == true ? box.Text : null;
    }

    // ------------------------------------------------------------------ closing

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
