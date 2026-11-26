using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;
using Microsoft.Win32;

namespace Horizon.Stealth.Views;

public partial class HistoryManagerWindow : Window
{
    public sealed class HistoryRowView
    {
        public HistoryItem Source { get; }
        public string DisplayTitle { get; }
        public string Domain { get; }
        public string Subtitle { get; }
        public string TimeDisplay { get; }
        public string DateGroup { get; }
        public ImageSource? Icon { get; }
        public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

        public HistoryRowView(HistoryItem item, ImageSource? icon)
        {
            Source = item;
            DisplayTitle = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;
            Domain = ExtractDomain(item.Url);
            Subtitle = string.IsNullOrEmpty(Domain) ? item.Url : $"{Domain}  \u00B7  {item.Url}";
            TimeDisplay = FormatRelativeTime(item.Visited);
            DateGroup = ClassifyDateGroup(item.Visited);
            Icon = icon;
        }
    }

    public sealed class SiteSummaryRow
    {
        public string Domain { get; init; } = "";
        public int Count { get; init; }
        public ImageSource? Icon { get; init; }
    }

    private static HistoryManagerWindow? _instance;
    private static readonly FaviconConverter _faviconConv = new();

    private readonly MainWindow _mainWindow;
    private string? _selectedDomainFilter;
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

        var win = new HistoryManagerWindow(owner) { Owner = owner };
        _instance = win;
        win.Closed += (_, _) => _instance = null;
        win.Show();
    }

    private HistoryManagerWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        HistoryService.HistoryUpdated += OnHistoryUpdated;
        Closed += (_, _) => HistoryService.HistoryUpdated -= OnHistoryUpdated;
        Loaded += (_, _) =>
        {
            RefreshView();
            TxtSearch.Focus();
        };
    }

    private void OnHistoryUpdated(object? sender, EventArgs e)
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
        var all = HistoryService.Items.ToList();
        string query = (TxtSearch.Text ?? "").Trim();
        BtnClearSearch.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 1. Update Top Sites rail
        var siteGroups = all
            .Select(h => (Item: h, Domain: ExtractDomain(h.Url)))
            .Where(x => !string.IsNullOrEmpty(x.Domain))
            .GroupBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(30)
            .Select(g => new SiteSummaryRow
            {
                Domain = g.Key,
                Count = g.Count(),
                Icon = ResolveIcon(g.First().Item.Url)
            })
            .ToList();
        LstTopSites.ItemsSource = siteGroups;

        BtnClearSiteFilter.Visibility = string.IsNullOrEmpty(_selectedDomainFilter) ? Visibility.Collapsed : Visibility.Visible;
        BtnDeleteDomain.Visibility = string.IsNullOrEmpty(_selectedDomainFilter) ? Visibility.Collapsed : Visibility.Visible;

        // 2. Apply Time Range + Site + Search filters
        DateTime today = DateTime.Today;
        DateTime yesterday = today.AddDays(-1);
        DateTime weekAgo = today.AddDays(-7);

        IEnumerable<HistoryItem> filtered = all;

        if (RbRangeToday.IsChecked == true)
            filtered = filtered.Where(h => h.Visited.Date == today);
        else if (RbRangeYesterday.IsChecked == true)
            filtered = filtered.Where(h => h.Visited.Date == yesterday);
        else if (RbRangeWeek.IsChecked == true)
            filtered = filtered.Where(h => h.Visited.Date >= weekAgo);
        else if (RbRangeOlder.IsChecked == true)
            filtered = filtered.Where(h => h.Visited.Date < weekAgo);

        if (!string.IsNullOrEmpty(_selectedDomainFilter))
            filtered = filtered.Where(h => string.Equals(ExtractDomain(h.Url), _selectedDomainFilter, StringComparison.OrdinalIgnoreCase));

        if (query.Length > 0)
        {
            filtered = filtered.Where(h =>
                (h.Title ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (h.Url ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered
            .OrderByDescending(h => h.Visited)
            .Take(2000)
            .Select(h => new HistoryRowView(h, ResolveIcon(h.Url)))
            .ToList();

        var view = CollectionViewSource.GetDefaultView(rows);
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(
            TabBySite.IsChecked == true ? nameof(HistoryRowView.Domain) : nameof(HistoryRowView.DateGroup)));

        LstHistory.ItemsSource = view;
        TxtEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        int uniqueSites = all.Select(h => ExtractDomain(h.Url)).Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        TxtStats.Text = rows.Count == all.Count
            ? $"{all.Count:N0} visits across {uniqueSites:N0} sites"
            : $"Showing {rows.Count:N0} of {all.Count:N0} visits ({uniqueSites:N0} sites total)";
    }

    private static ImageSource? ResolveIcon(string url)
    {
        try { return _faviconConv.Convert(url, typeof(ImageSource), null!, CultureInfo.InvariantCulture) as ImageSource; }
        catch { return null; }
    }

    private static string ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return "";
    }

    private static string ClassifyDateGroup(DateTime dt)
    {
        DateTime d = dt.Date;
        DateTime today = DateTime.Today;
        if (d == today) return "Today";
        if (d == today.AddDays(-1)) return "Yesterday";
        if (d >= today.AddDays(-7)) return d.ToString("dddd, MMM d");
        return d.ToString("MMMM yyyy");
    }

    private static string FormatRelativeTime(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago \u00B7 {dt:HH:mm}";
        if (dt.Date == DateTime.Today) return dt.ToString("HH:mm");
        return dt.ToString("MMM d, HH:mm");
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key == Key.F || e.Key == Key.L))
        {
            TxtSearch.Focus();
            TxtSearch.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete && !TxtSearch.IsKeyboardFocused && LstHistory.SelectedItems.Count > 0)
        {
            BtnBulkDelete_Click(sender, e);
            e.Handled = true;
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();
    private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearch.Text = ""; TxtSearch.Focus(); }
    private void ViewMode_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void Range_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }

    private void LstTopSites_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstTopSites.SelectedItem is SiteSummaryRow site)
        {
            _selectedDomainFilter = site.Domain;
            RefreshView();
        }
    }

    private void BtnClearSiteFilter_Click(object sender, RoutedEventArgs e)
    {
        _selectedDomainFilter = null;
        LstTopSites.SelectedItem = null;
        RefreshView();
    }

    private void LstHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int count = LstHistory.SelectedItems.Count;
        PnlBulkBar.Visibility = count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        TxtBulkCount.Text = $"{count} items selected";
    }

    private void LstHistory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstHistory.SelectedItem is HistoryRowView row)
        {
            _mainWindow.CreateNewTab(row.Source.Url, activate: true);
            Close();
        }
    }

    private void BtnRowOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is HistoryRowView row)
        {
            _mainWindow.CreateNewTab(row.Source.Url, activate: true);
            Close();
        }
    }

    private void BtnRowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is HistoryRowView row)
        {
            HistoryService.Remove(row.Source.Url);
        }
    }

    private void LstHistory_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (LstHistory.SelectedItem is not HistoryRowView row) return;
        var menu = new ContextMenu();

        var miOpen = new MenuItem { Header = "Open in New Tab" };
        miOpen.Click += (_, _) => { _mainWindow.CreateNewTab(row.Source.Url, activate: true); Close(); };
        menu.Items.Add(miOpen);

        var miBg = new MenuItem { Header = "Open in Background Tab" };
        miBg.Click += (_, _) => _mainWindow.CreateNewTab(row.Source.Url, activate: false);
        menu.Items.Add(miBg);

        var miCopy = new MenuItem { Header = "Copy URL" };
        miCopy.Click += (_, _) => { try { Clipboard.SetText(row.Source.Url); } catch { } };
        menu.Items.Add(miCopy);

        var miBookmark = new MenuItem { Header = "Add to Bookmarks" };
        miBookmark.Click += (_, _) => BookmarkService.Add(new BookmarkItem { Name = row.DisplayTitle, Url = row.Source.Url });
        menu.Items.Add(miBookmark);

        menu.Items.Add(new Separator());

        var miDelete = new MenuItem { Header = "Delete Entry" };
        miDelete.Click += (_, _) => HistoryService.Remove(row.Source.Url);
        menu.Items.Add(miDelete);

        if (!string.IsNullOrEmpty(row.Domain))
        {
            var miDeleteSite = new MenuItem { Header = $"Delete All from {row.Domain}" };
            miDeleteSite.Click += (_, _) =>
            {
                var urls = HistoryService.Items
                    .Where(h => string.Equals(ExtractDomain(h.Url), row.Domain, StringComparison.OrdinalIgnoreCase))
                    .Select(h => h.Url)
                    .Distinct()
                    .ToList();
                foreach (var u in urls) HistoryService.Remove(u);
            };
            menu.Items.Add(miDeleteSite);
        }

        menu.IsOpen = true;
    }

    private void BtnBulkOpen_Click(object sender, RoutedEventArgs e)
    {
        var items = LstHistory.SelectedItems.OfType<HistoryRowView>().Take(15).ToList();
        for (int i = 0; i < items.Count; i++)
            _mainWindow.CreateNewTab(items[i].Source.Url, activate: i == 0);
        Close();
    }

    private void BtnBulkBookmark_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in LstHistory.SelectedItems.OfType<HistoryRowView>().ToList())
            BookmarkService.Add(new BookmarkItem { Name = row.DisplayTitle, Url = row.Source.Url });
        MessageBox.Show(this, "Selected pages added to Bookmarks.", "Horizon History", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnBulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var urls = LstHistory.SelectedItems.OfType<HistoryRowView>().Select(r => r.Source.Url).Distinct().ToList();
        foreach (var u in urls) HistoryService.Remove(u);
    }

    private void BtnDeleteDomain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedDomainFilter)) return;
        string dom = _selectedDomainFilter;
        if (MessageBox.Show(this, $"Delete all history entries from {dom}?", "Horizon History",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var urls = HistoryService.Items
            .Where(h => string.Equals(ExtractDomain(h.Url), dom, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Url)
            .Distinct()
            .ToList();
        foreach (var u in urls) HistoryService.Remove(u);
        _selectedDomainFilter = null;
        RefreshView();
    }

    private void BtnReopenClosed_Click(object sender, RoutedEventArgs e)
    {
        if (!_mainWindow.ReopenLastClosedTab())
        {
            MessageBox.Show(this, "No recently closed tabs available to reopen.", "Horizon History", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnClearMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void AddRange(string label, TimeSpan? span)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) =>
            {
                if (span == null)
                {
                    if (MessageBox.Show(this, "Clear ALL browsing history? This cannot be undone.", "Clear History",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        HistoryService.Clear();
                    return;
                }
                DateTime cutoff = DateTime.Now - span.Value;
                var toRemove = HistoryService.Items.Where(h => h.Visited >= cutoff).Select(h => h.Url).Distinct().ToList();
                foreach (var u in toRemove) HistoryService.Remove(u);
            };
            menu.Items.Add(mi);
        }

        AddRange("Clear Last Hour", TimeSpan.FromHours(1));
        AddRange("Clear Last 24 Hours", TimeSpan.FromHours(24));
        AddRange("Clear Last 7 Days", TimeSpan.FromDays(7));
        menu.Items.Add(new Separator());
        AddRange("Clear Everything...", null);

        menu.PlacementTarget = BtnClearMenu;
        menu.IsOpen = true;
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Export Browsing History",
            Filter = "JSON File (*.json)|*.json|CSV File (*.csv)|*.csv",
            FileName = $"horizon_history_{DateTime.Now:yyyyMMdd}"
        };
        if (sfd.ShowDialog(this) != true) return;

        try
        {
            var items = HistoryService.Items.ToList();
            if (sfd.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder("Title,URL,Visited\n");
                foreach (var h in items)
                {
                    string t = "\"" + (h.Title ?? "").Replace("\"", "\"\"") + "\"";
                    string u = "\"" + (h.Url ?? "").Replace("\"", "\"\"") + "\"";
                    sb.AppendLine($"{t},{u},{h.Visited:o}");
                }
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
            }
            else
            {
                File.WriteAllText(sfd.FileName,
                    JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);
            }
            MessageBox.Show(this, $"Exported {items.Count:N0} history entries.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed: " + ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
