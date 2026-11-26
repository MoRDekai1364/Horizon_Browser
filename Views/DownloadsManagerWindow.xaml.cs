using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class DownloadsManagerWindow : Window
{
    public sealed class PlaceItem
    {
        public string Icon { get; init; } = "\U0001F4C1";
        public string Label { get; init; } = "";
        public string Path { get; init; } = "";
    }

    public sealed class ActiveDlRow
    {
        public string FileName { get; init; } = "";
        public double Percent { get; init; }
        public string PercentText => $"{Percent:0}%";
    }

    public sealed class FsRowView
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public bool IsFolder { get; init; }
        public bool IsDrive { get; init; }
        public long SizeBytes { get; init; }
        public DateTime Modified { get; init; }
        public string Icon { get; init; } = "\U0001F4C4";
        public string TypeLabel { get; init; } = "";
        public string SizeDisplay { get; init; } = "";
        public string ModifiedDisplay { get; init; } = "";
    }

    private enum SortKind { Date, Name, Size }

    private static DownloadsManagerWindow? _instance;
    private const string DrivesSentinel = "__DRIVES__";

    private readonly MainWindow _mainWindow;
    private readonly Stack<string> _backStack = new();
    private string _currentPath;
    private SortKind _sortKind = SortKind.Date;
    private bool _sortAsc = false;
    private bool _suppressPlaceSelect;

    private static readonly HashSet<string> _docExts = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".txt", ".md", ".rtf", ".xls", ".xlsx", ".csv", ".ppt", ".pptx" };
    private static readonly HashSet<string> _imgExts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico" };
    private static readonly HashSet<string> _mediaExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".mp3", ".wav", ".flac", ".ogg", ".m4a" };
    private static readonly HashSet<string> _archExts = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".rar", ".7z", ".tar", ".gz", ".exe", ".msi", ".msix" };

    public static void ShowSingleton(MainWindow owner, string? initialPath = null)
    {
        if (_instance != null)
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            if (!string.IsNullOrEmpty(initialPath)) _instance.NavigateTo(initialPath, pushBack: true);
            _instance.Activate();
            _instance.TxtSearch.Focus();
            return;
        }

        var win = new DownloadsManagerWindow(owner, initialPath) { Owner = owner };
        _instance = win;
        win.Closed += (_, _) => _instance = null;
        win.Show();
    }

    private DownloadsManagerWindow(MainWindow mainWindow, string? initialPath)
    {
        _mainWindow = mainWindow;
        _currentPath = !string.IsNullOrWhiteSpace(initialPath)
            ? initialPath
            : SettingsService.Current.DownloadsPath;
        InitializeComponent();

        DownloadsBridge.Updated += OnDownloadsUpdated;
        Closed += (_, _) => DownloadsBridge.Updated -= OnDownloadsUpdated;
        Loaded += (_, _) =>
        {
            PopulatePlaces();
            RefreshActiveDownloads();
            RefreshDirectory();
            TxtSearch.Focus();
        };
    }

    private void OnDownloadsUpdated()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshActiveDownloads();
            if (string.Equals(_currentPath, SettingsService.Current.DownloadsPath, StringComparison.OrdinalIgnoreCase))
                RefreshDirectory();
        }));
    }

    private void RefreshActiveDownloads()
    {
        var active = DownloadsBridge.ActiveDownloads
            .Select(d => new ActiveDlRow { FileName = d.FileName, Percent = d.Percent })
            .ToList();
        LstActiveDownloads.ItemsSource = active;
        PnlActiveDownloads.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulatePlaces()
    {
        var places = new List<PlaceItem>();
        void AddIfExists(string icon, string label, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                places.Add(new PlaceItem { Icon = icon, Label = label, Path = path });
        }

        AddIfExists("\u2B07", "Downloads", SettingsService.Current.DownloadsPath);
        AddIfExists("\U0001F5A5", "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        AddIfExists("\U0001F4C4", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddIfExists("\U0001F3B5", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        AddIfExists("\U0001F5BC", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        AddIfExists("\U0001F3AC", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        foreach (var pinned in SettingsService.Current.PinnedFsLocations)
        {
            if (Directory.Exists(pinned))
                places.Add(new PlaceItem { Icon = "\U0001F4CC", Label = Path.GetFileName(pinned.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : pinned, Path = pinned });
        }

        places.Add(new PlaceItem { Icon = "\U0001F4BB", Label = "This PC (All Drives)", Path = DrivesSentinel });
        foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
        {
            string vol = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel;
            places.Add(new PlaceItem { Icon = "\U0001F4BD", Label = $"{d.Name.TrimEnd('\\')} ({vol})", Path = d.RootDirectory.FullName });
        }

        _suppressPlaceSelect = true;
        LstPlaces.ItemsSource = places;
        _suppressPlaceSelect = false;
    }

    private void NavigateTo(string path, bool pushBack)
    {
        if (pushBack && !string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
            _backStack.Push(_currentPath);
        _currentPath = path;
        if (TabFileBrowser != null && path != SettingsService.Current.DownloadsPath)
            TabFileBrowser.IsChecked = true;
        RefreshDirectory();
    }

    private void RefreshDirectory()
    {
        string query = (TxtSearch.Text ?? "").Trim();
        BtnClearSearch.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnBack.IsEnabled = _backStack.Count > 0;
        BtnUp.IsEnabled = _currentPath != DrivesSentinel && Directory.GetParent(_currentPath) != null;

        bool isDrives = _currentPath == DrivesSentinel;
        TxtCurrentPath.Text = isDrives ? "This PC \u2014 All Drives" : _currentPath;

        var rows = new List<FsRowView>();

        if (isDrives)
        {
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
            {
                string free = FormatSize(d.AvailableFreeSpace);
                string total = FormatSize(d.TotalSize);
                rows.Add(new FsRowView
                {
                    Name = $"{d.Name} {(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel)}",
                    FullPath = d.RootDirectory.FullName,
                    IsFolder = true,
                    IsDrive = true,
                    SizeBytes = d.TotalSize,
                    Icon = "\U0001F4BD",
                    TypeLabel = $"{d.DriveType} \u00B7 {free} free of {total}",
                    SizeDisplay = total,
                    ModifiedDisplay = ""
                });
            }
        }
        else if (Directory.Exists(_currentPath))
        {
            try
            {
                var di = new DirectoryInfo(_currentPath);
                foreach (var dir in di.GetDirectories().Take(800))
                {
                    if ((dir.Attributes & FileAttributes.Hidden) != 0 && (dir.Attributes & FileAttributes.System) != 0)
                        continue;
                    rows.Add(new FsRowView
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsFolder = true,
                        SizeBytes = 0,
                        Modified = dir.LastWriteTime,
                        Icon = "\U0001F4C1",
                        TypeLabel = "Folder",
                        SizeDisplay = "",
                        ModifiedDisplay = dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                    });
                }

                foreach (var f in di.GetFiles().Take(1500))
                {
                    if ((f.Attributes & FileAttributes.Hidden) != 0 && (f.Attributes & FileAttributes.System) != 0)
                        continue;
                    rows.Add(new FsRowView
                    {
                        Name = f.Name,
                        FullPath = f.FullName,
                        IsFolder = false,
                        SizeBytes = f.Length,
                        Modified = f.LastWriteTime,
                        Icon = GetFileIcon(f.Extension),
                        TypeLabel = string.IsNullOrEmpty(f.Extension) ? "File" : $"{f.Extension.TrimStart('.').ToUpperInvariant()} File",
                        SizeDisplay = FormatSize(f.Length),
                        ModifiedDisplay = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                TxtStats.Text = "Access denied to this folder.";
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "DownloadsManagerWindow.RefreshDirectory");
            }
        }

        // Apply type filter
        IEnumerable<FsRowView> filtered = rows;
        if (RbTypeFolders.IsChecked == true)
            filtered = filtered.Where(r => r.IsFolder);
        else if (RbTypeDocs.IsChecked == true)
            filtered = filtered.Where(r => !r.IsFolder && _docExts.Contains(Path.GetExtension(r.Name)));
        else if (RbTypeImages.IsChecked == true)
            filtered = filtered.Where(r => !r.IsFolder && _imgExts.Contains(Path.GetExtension(r.Name)));
        else if (RbTypeMedia.IsChecked == true)
            filtered = filtered.Where(r => !r.IsFolder && _mediaExts.Contains(Path.GetExtension(r.Name)));
        else if (RbTypeArchives.IsChecked == true)
            filtered = filtered.Where(r => !r.IsFolder && _archExts.Contains(Path.GetExtension(r.Name)));

        if (query.Length > 0)
            filtered = filtered.Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (!isDrives)
        {
            filtered = _sortKind switch
            {
                SortKind.Name => _sortAsc
                    ? filtered.OrderBy(r => r.IsFolder ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    : filtered.OrderBy(r => r.IsFolder ? 0 : 1).ThenByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase),
                SortKind.Size => _sortAsc
                    ? filtered.OrderBy(r => r.IsFolder ? 0 : 1).ThenBy(r => r.SizeBytes)
                    : filtered.OrderBy(r => r.IsFolder ? 0 : 1).ThenByDescending(r => r.SizeBytes),
                _ => _sortAsc
                    ? filtered.OrderBy(r => r.IsFolder ? 0 : 1).ThenBy(r => r.Modified)
                    : filtered.OrderBy(r => r.IsFolder ? 0 : 1).ThenByDescending(r => r.Modified),
            };
        }

        var finalRows = filtered.ToList();
        LstFiles.ItemsSource = finalRows;
        TxtEmpty.Visibility = finalRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        int foldersCount = finalRows.Count(r => r.IsFolder);
        int filesCount = finalRows.Count - foldersCount;
        long totalBytes = finalRows.Where(r => !r.IsFolder).Sum(r => r.SizeBytes);
        TxtStats.Text = $"{foldersCount} folder(s), {filesCount} file(s) \u00B7 {FormatSize(totalBytes)}";

        bool isPinned = SettingsService.Current.PinnedFsLocations.Contains(_currentPath, StringComparer.OrdinalIgnoreCase);
        BtnPinFolder.Content = isPinned ? "\U0001F4CC Unpin Folder" : "\U0001F4CC Pin Current Folder";
        BtnPinFolder.IsEnabled = !isDrives && Directory.Exists(_currentPath);
        UpdateSortLabels();
    }

    private void UpdateSortLabels()
    {
        BtnSortDate.Content = _sortKind == SortKind.Date ? "Date \u2022" : "Date";
        BtnSortName.Content = _sortKind == SortKind.Name ? "Name \u2022" : "Name";
        BtnSortSize.Content = _sortKind == SortKind.Size ? "Size \u2022" : "Size";
        BtnSortDir.Content = _sortAsc ? "\u2191" : "\u2193";
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (TabDownloads.IsChecked == true)
            NavigateTo(SettingsService.Current.DownloadsPath, pushBack: true);
        else if (_currentPath == SettingsService.Current.DownloadsPath)
            NavigateTo(DrivesSentinel, pushBack: true);
    }

    private void TypeFilter_Changed(object sender, RoutedEventArgs e) => RefreshDirectory();
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshDirectory();
    private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearch.Text = ""; TxtSearch.Focus(); }

    private void LstPlaces_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPlaceSelect) return;
        if (LstPlaces.SelectedItem is PlaceItem place)
            NavigateTo(place.Path, pushBack: true);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_backStack.TryPop(out var prev))
        {
            _currentPath = prev;
            RefreshDirectory();
        }
    }

    private void BtnUp_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == DrivesSentinel) return;
        var parent = Directory.GetParent(_currentPath);
        if (parent != null)
            NavigateTo(parent.FullName, pushBack: true);
        else
            NavigateTo(DrivesSentinel, pushBack: true);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshDirectory();
    private void BtnShowDrives_Click(object sender, RoutedEventArgs e) => NavigateTo(DrivesSentinel, pushBack: true);

    private void BtnSortDate_Click(object sender, RoutedEventArgs e) { _sortKind = SortKind.Date; RefreshDirectory(); }
    private void BtnSortName_Click(object sender, RoutedEventArgs e) { _sortKind = SortKind.Name; RefreshDirectory(); }
    private void BtnSortSize_Click(object sender, RoutedEventArgs e) { _sortKind = SortKind.Size; RefreshDirectory(); }
    private void BtnSortDir_Click(object sender, RoutedEventArgs e) { _sortAsc = !_sortAsc; RefreshDirectory(); }

    private void LstFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int count = LstFiles.SelectedItems.Count;
        PnlBulkBar.Visibility = count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        TxtBulkCount.Text = $"{count} items selected";
    }

    private void OpenItem(FsRowView row)
    {
        if (row.IsFolder)
        {
            NavigateTo(row.FullPath, pushBack: true);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(row.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open file:\n{ex.Message}", "Downloads & File Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstFiles.SelectedItem is FsRowView row) OpenItem(row);
    }

    private void BtnRowOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FsRowView row) OpenItem(row);
    }

    private void BtnRowReveal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FsRowView row)
        {
            try
            {
                string arg = row.IsDrive ? row.FullPath : $"/select,\"{row.FullPath}\"";
                Process.Start("explorer.exe", arg);
            }
            catch { }
        }
    }

    private void BtnRowRename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FsRowView row || row.IsDrive) return;
        string? newName = PromptText("Rename Item", "Enter new name:", row.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == row.Name) return;

        try
        {
            string dir = Path.GetDirectoryName(row.FullPath) ?? _currentPath;
            string dest = Path.Combine(dir, newName.Trim());
            if (row.IsFolder) Directory.Move(row.FullPath, dest);
            else File.Move(row.FullPath, dest);
            RefreshDirectory();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Rename failed:\n{ex.Message}", "Downloads & File Browser", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRowDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FsRowView row || row.IsDrive) return;
        if (MessageBox.Show(this, $"Delete \"{row.Name}\"?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        DeletePath(row);
        RefreshDirectory();
    }

    private static void DeletePath(FsRowView row)
    {
        try
        {
            if (row.IsFolder && Directory.Exists(row.FullPath))
                Directory.Delete(row.FullPath, recursive: true);
            else if (!row.IsFolder && File.Exists(row.FullPath))
                File.Delete(row.FullPath);
        }
        catch { }
    }

    private void BtnNewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == DrivesSentinel || !Directory.Exists(_currentPath)) return;
        string? name = PromptText("New Folder", "Folder name:", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            Directory.CreateDirectory(Path.Combine(_currentPath, name.Trim()));
            RefreshDirectory();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not create folder:\n{ex.Message}", "Downloads & File Browser", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string target = _currentPath == DrivesSentinel ? "explorer.exe" : _currentPath;
            if (_currentPath == DrivesSentinel) Process.Start("explorer.exe");
            else Process.Start("explorer.exe", target);
        }
        catch { }
    }

    private void BtnSetDownloadsDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Default Downloads Folder",
            InitialDirectory = Directory.Exists(SettingsService.Current.DownloadsPath)
                ? SettingsService.Current.DownloadsPath
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dlg.ShowDialog(this) == true && Directory.Exists(dlg.FolderName))
        {
            SettingsService.Current.DownloadsPath = dlg.FolderName;
            SettingsService.Save();
            PopulatePlaces();
            NavigateTo(dlg.FolderName, pushBack: true);
        }
    }

    private void BtnPinFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == DrivesSentinel || !Directory.Exists(_currentPath)) return;
        var list = SettingsService.Current.PinnedFsLocations;
        int idx = list.FindIndex(p => string.Equals(p, _currentPath, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) list.RemoveAt(idx);
        else list.Add(_currentPath);
        SettingsService.Save();
        PopulatePlaces();
        RefreshDirectory();
    }

    private void BtnBulkOpen_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in LstFiles.SelectedItems.OfType<FsRowView>().Where(r => !r.IsFolder).Take(10))
            OpenItem(row);
    }

    private void BtnBulkCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var paths = LstFiles.SelectedItems.OfType<FsRowView>().Select(r => r.FullPath).ToList();
        if (paths.Count > 0)
            try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); } catch { }
    }

    private void BtnBulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var items = LstFiles.SelectedItems.OfType<FsRowView>().Where(r => !r.IsDrive).ToList();
        if (items.Count == 0) return;
        if (MessageBox.Show(this, $"Delete {items.Count} selected items permanently?", "Confirm Bulk Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        foreach (var item in items) DeletePath(item);
        RefreshDirectory();
    }

    private string? PromptText(string title, string label, string initial)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 420,
            Height = 175,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = TryFindResource("Brush_DeepBackground") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black,
            Foreground = TryFindResource("Brush_Text") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White
        };
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 8) });
        var tb = new TextBox
        {
            Text = initial,
            Height = 32,
            Padding = new Thickness(8, 5, 8, 5),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x22, 0x2D)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x55, 0x68))
        };
        root.Children.Add(tb);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "OK", Width = 80, Height = 30, IsDefault = true };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        root.Children.Add(btns);
        dlg.Content = root;
        tb.SelectAll();
        tb.Focus();
        return dlg.ShowDialog() == true ? tb.Text : null;
    }

    private static string GetFileIcon(string ext)
    {
        if (_docExts.Contains(ext)) return "\U0001F4C4";
        if (_imgExts.Contains(ext)) return "\U0001F5BC";
        if (_mediaExts.Contains(ext)) return "\U0001F3AC";
        if (_archExts.Contains(ext)) return "\U0001F4E6";
        return "\U0001F4DD";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.##} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { TxtSearch.Focus(); TxtSearch.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Back && !TxtSearch.IsFocused) { BtnBack_Click(sender, e); e.Handled = true; }
    }
}
