using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class ExtensionsManagerWindow : Window
{
    public sealed class ExtensionRowView
    {
        public ExtensionRecord Source { get; }
        public string DisplayName => string.IsNullOrWhiteSpace(Source.Name) ? Source.Id : Source.Name;
        public string VersionText => string.IsNullOrWhiteSpace(Source.Version) ? "v1.0" : $"v{Source.Version}";
        public string Description => string.IsNullOrWhiteSpace(Source.Description) ? Source.Id : Source.Description;
        public string IconEmoji => string.IsNullOrWhiteSpace(Source.Icon) ? "\U0001F9E9" : Source.Icon;
        public ImageSource? IconImage { get; }
        public Visibility FallbackVisibility => IconImage == null ? Visibility.Visible : Visibility.Collapsed;

        public string StatusText => Source.Enabled ? "ENABLED" : "DISABLED";
        public Brush StatusBg { get; }
        public Brush StatusFg { get; }
        public string ToggleLabel => Source.Enabled ? "Disable" : "Enable";
        public string PinLabel { get; }

        public ExtensionRowView(ExtensionRecord rec, ImageSource? img, bool isPinned)
        {
            Source = rec;
            IconImage = img;
            StatusBg = new SolidColorBrush(rec.Enabled ? Color.FromArgb(0x30, 0x22, 0xC5, 0x5E) : Color.FromArgb(0x30, 0xEF, 0x44, 0x44));
            StatusFg = new SolidColorBrush(rec.Enabled ? Color.FromRgb(0x4A, 0xDE, 0x80) : Color.FromRgb(0xF8, 0x71, 0x71));
            PinLabel = isPinned ? "\U0001F4CC Unpin" : "\U0001F4CC Pin";
        }
    }

    private static ExtensionsManagerWindow? _instance;

    private readonly MainWindow _mainWindow;
    private bool _refreshQueued;

    public static void ShowSingleton(MainWindow owner)
    {
        if (_instance != null)
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            _instance.TxtSearch.Focus();
            return;
        }

        var win = new ExtensionsManagerWindow(owner) { Owner = owner };
        _instance = win;
        win.Closed += (_, _) => _instance = null;
        win.Show();
    }

    private ExtensionsManagerWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        ExtensionService.CatalogChanged += OnCatalogChanged;
        Closed += (_, _) => ExtensionService.CatalogChanged -= OnCatalogChanged;
        Loaded += (_, _) =>
        {
            RefreshView();
            TxtSearch.Focus();
        };
    }

    private void OnCatalogChanged()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _refreshQueued = false;
            RefreshView();
        }));
    }

    private void RefreshView()
    {
        var all = ExtensionService.All.ToList();
        string query = (TxtSearch.Text ?? "").Trim();
        BtnClearSearch.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var pinnedModes = SettingsService.Current.WidgetModes
            .Where(m => m.StartsWith("Ext:", StringComparison.OrdinalIgnoreCase))
            .Select(m => m[4..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<ExtensionRecord> filtered = all;
        if (TabEnabled.IsChecked == true) filtered = filtered.Where(e => e.Enabled);
        else if (TabDisabled.IsChecked == true) filtered = filtered.Where(e => !e.Enabled);

        if (RbFilterPinned.IsChecked == true)
            filtered = filtered.Where(e => pinnedModes.Contains(e.Id));

        if (query.Length > 0)
        {
            filtered = filtered.Where(e =>
                (e.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (e.Description ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (e.Id ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (RbSortState.IsChecked == true)
            filtered = filtered.OrderByDescending(e => e.Enabled).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
        else if (RbSortDate.IsChecked == true)
            filtered = filtered.OrderByDescending(e => e.InstalledAt);
        else
            filtered = filtered.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

        var rows = filtered
            .Select(e => new ExtensionRowView(e, LoadImage(e.IconPath), pinnedModes.Contains(e.Id)))
            .ToList();

        LstExtensions.ItemsSource = rows;
        TxtEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int enabledCount = all.Count(e => e.Enabled);
        TxtStats.Text = $"{rows.Count} shown \u00B7 {enabledCount} enabled / {all.Count} total";
    }

    private static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 32;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();
    private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearch.Text = ""; TxtSearch.Focus(); }

    private void BtnWebStore_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.CreateNewTab("https://chromewebstore.google.com/", activate: true);
        Close();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ExtensionService.InstallRoot);
            Process.Start("explorer.exe", ExtensionService.InstallRoot);
        }
        catch { }
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        ExtensionService.EnsureInstalled();
        RefreshView();
    }

    private void BtnRowToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExtensionRowView row) return;
        bool next = !row.Source.Enabled;
        if (next && ExtensionService.ModifiesShortcuts(ExtensionService.GetInstallFolder(row.Source.Id), row.Source.Id, row.Source.Name, row.Source.Description))
        {
            bool? perm = ExtensionService.GetShortcutPermission(row.Source.Id);
            if (perm == null)
                ExtensionService.PromptShortcutPermission(row.Source.Id, row.Source.Name, this);
        }
        ExtensionService.SetEnabled(row.Source.Id, next);
        RefreshView();
    }

    private void BtnRowPin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExtensionRowView row) return;
        string modeKey = $"Ext:{row.Source.Id}";
        var modes = SettingsService.Current.WidgetModes;
        int idx = modes.FindIndex(m => string.Equals(m, modeKey, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) modes.RemoveAt(idx);
        else modes.Add(modeKey);
        SettingsService.Save();
        RefreshView();
    }

    private void BtnRowFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExtensionRowView row) return;
        try
        {
            string folder = ExtensionService.GetInstallFolder(row.Source.Id);
            Process.Start("explorer.exe", Directory.Exists(folder) ? folder : ExtensionService.InstallRoot);
        }
        catch { }
    }

    private void BtnRowRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExtensionRowView row) return;
        if (MessageBox.Show(this, $"Uninstall \"{row.DisplayName}\"?", "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        ExtensionService.Uninstall(row.Source.Id);
        RefreshView();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { TxtSearch.Focus(); TxtSearch.SelectAll(); e.Handled = true; }
    }
}
