using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;
using Microsoft.Win32;

namespace Horizon.Stealth.Views;

public partial class BookmarksManagerWindow : Window
{
    public sealed class FolderSummaryRow
    {
        public string Name { get; init; } = "";
        public int Count { get; init; }
    }

    public sealed class BookmarkRowView
    {
        public BookmarkItem Source { get; }
        public string DisplayTitle { get; }
        public string Subtitle { get; }
        public string FolderName { get; }
        public ImageSource? Icon { get; }
        public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FolderBadgeVisibility => string.IsNullOrWhiteSpace(Source.Category) ? Visibility.Collapsed : Visibility.Visible;

        public BookmarkRowView(BookmarkItem item, ImageSource? icon)
        {
            Source = item;
            DisplayTitle = string.IsNullOrWhiteSpace(item.Name) ? item.Url : item.Name;
            Subtitle = item.Url;
            FolderName = string.IsNullOrWhiteSpace(item.Category) ? "Uncategorized" : item.Category;
            Icon = icon;
        }
    }

    private static BookmarksManagerWindow? _instance;
    private static readonly FaviconConverter _faviconConv = new();

    private readonly MainWindow _mainWindow;
    private string? _selectedFolderFilter;
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

        var win = new BookmarksManagerWindow(owner) { Owner = owner };
        _instance = win;
        win.Closed += (_, _) => _instance = null;
        win.Show();
    }

    private BookmarksManagerWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        BookmarkService.OnUpdated += OnBookmarksUpdated;
        Closed += (_, _) => BookmarkService.OnUpdated -= OnBookmarksUpdated;
        Loaded += (_, _) =>
        {
            RefreshView();
            TxtSearch.Focus();
        };
    }

    private void OnBookmarksUpdated()
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
        var all = BookmarkService.Items.ToList();
        string query = (TxtSearch.Text ?? "").Trim();
        BtnClearSearch.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var folders = all
            .GroupBy(b => string.IsNullOrWhiteSpace(b.Category) ? "Uncategorized" : b.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FolderSummaryRow { Name = g.Key, Count = g.Count() })
            .ToList();
        LstFolders.ItemsSource = folders;
        BtnResetFolder.Visibility = string.IsNullOrEmpty(_selectedFolderFilter) ? Visibility.Collapsed : Visibility.Visible;

        IEnumerable<BookmarkItem> filtered = all;
        if (!string.IsNullOrEmpty(_selectedFolderFilter))
        {
            filtered = filtered.Where(b => string.Equals(
                string.IsNullOrWhiteSpace(b.Category) ? "Uncategorized" : b.Category.Trim(),
                _selectedFolderFilter,
                StringComparison.OrdinalIgnoreCase));
        }

        if (query.Length > 0)
        {
            filtered = filtered.Where(b =>
                (b.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (b.Url ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (b.Category ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (TabMostVisited.IsChecked == true || RbSortVisits.IsChecked == true)
            filtered = filtered.OrderByDescending(b => b.OpenCount).ThenByDescending(b => b.DateAdded);
        else if (RbSortName.IsChecked == true)
            filtered = filtered.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase);
        else
            filtered = filtered.OrderByDescending(b => b.DateAdded);

        var rows = filtered.Select(b => new BookmarkRowView(b, ResolveIcon(b.Url))).ToList();
        var view = CollectionViewSource.GetDefaultView(rows);
        view.GroupDescriptions.Clear();
        if (TabByFolder.IsChecked == true)
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BookmarkRowView.FolderName)));

        LstBookmarks.ItemsSource = view;
        TxtEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtStats.Text = $"{rows.Count} of {all.Count} bookmarks";
    }

    private static ImageSource? ResolveIcon(string url)
    {
        try
        {
            var img = _faviconConv.Convert(url, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
            return img;
        }
        catch { return null; }
    }

    private void OpenBookmark(BookmarkItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Url)) return;
        BookmarkService.RecordOpen(item);
        _mainWindow.CreateNewTab(item.Url, activate: true);
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void Sort_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();
    private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearch.Text = ""; TxtSearch.Focus(); }

    private void LstFolders_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstFolders.SelectedItem is FolderSummaryRow row)
        {
            _selectedFolderFilter = row.Name;
            RefreshView();
        }
    }

    private void BtnResetFolder_Click(object sender, RoutedEventArgs e)
    {
        _selectedFolderFilter = null;
        LstFolders.SelectedItem = null;
        RefreshView();
    }

    private void LstBookmarks_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int count = LstBookmarks.SelectedItems.Count;
        PnlBulkBar.Visibility = count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        TxtBulkCount.Text = $"{count} items selected";
    }

    private void LstBookmarks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstBookmarks.SelectedItem is BookmarkRowView row) OpenBookmark(row.Source);
    }

    private void BtnRowOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BookmarkRowView row) OpenBookmark(row.Source);
    }

    private void BtnRowPin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BookmarkRowView row) return;
        if (!SettingsService.Current.PinnedUrls.Any(p => string.Equals(p.Url, row.Source.Url, StringComparison.OrdinalIgnoreCase)))
        {
            SettingsService.Current.PinnedUrls.Add(row.Source.AsPin);
            SettingsService.Save();
            _mainWindow.RightSidebar?.RefreshPinsPublic();
        }
    }

    private void BtnRowEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BookmarkRowView row) return;
        var edited = ShowEditDialog("Edit Bookmark", row.Source.Name, row.Source.Url, row.Source.Category);
        if (edited == null) return;
        row.Source.Name = edited.Value.Name;
        row.Source.Url = edited.Value.Url;
        row.Source.Category = edited.Value.Folder;
        BookmarkService.SaveBookmarks();
        RefreshView();
    }

    private void BtnRowDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BookmarkRowView row)
            BookmarkService.Remove(row.Source);
    }

    private void BtnAddBookmark_Click(object sender, RoutedEventArgs e)
    {
        var edited = ShowEditDialog("Add Bookmark", "", "https://", _selectedFolderFilter is "Uncategorized" or null ? "" : _selectedFolderFilter);
        if (edited == null || string.IsNullOrWhiteSpace(edited.Value.Url)) return;
        BookmarkService.Add(new BookmarkItem
        {
            Name = string.IsNullOrWhiteSpace(edited.Value.Name) ? edited.Value.Url : edited.Value.Name,
            Url = edited.Value.Url,
            Category = edited.Value.Folder,
            DateAdded = DateTime.Now
        });
    }

    private void BtnBulkOpen_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in LstBookmarks.SelectedItems.OfType<BookmarkRowView>().Take(15))
            OpenBookmark(row.Source);
    }

    private void BtnBulkMove_Click(object sender, RoutedEventArgs e)
    {
        var items = LstBookmarks.SelectedItems.OfType<BookmarkRowView>().ToList();
        if (items.Count == 0) return;
        var edited = ShowEditDialog("Move Selected to Folder", "(multiple)", "(unchanged)", "");
        if (edited == null) return;
        foreach (var r in items) r.Source.Category = edited.Value.Folder;
        BookmarkService.SaveBookmarks();
        RefreshView();
    }

    private void BtnBulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var items = LstBookmarks.SelectedItems.OfType<BookmarkRowView>().ToList();
        foreach (var r in items) BookmarkService.Remove(r.Source);
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "HTML Bookmarks (*.html;*.htm)|*.html;*.htm", Title = "Import Bookmarks" };
        if (dlg.ShowDialog(this) == true)
        {
            int count = BookmarkService.ImportHtml(dlg.FileName);
            MessageBox.Show(this, $"Imported {count} bookmark(s).", "Bookmarks", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnImportDefault_Click(object sender, RoutedEventArgs e)
    {
        bool ok = BookmarkService.ExtractFromDefaultBrowser();
        MessageBox.Show(this, ok ? "Imported bookmarks from your default browser." : "No new bookmarks found in default browser.", "Bookmarks", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "HTML Bookmarks (*.html)|*.html", FileName = "horizon_bookmarks.html" };
        if (dlg.ShowDialog(this) != true) return;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1><TITLE>Bookmarks</TITLE><H1>Bookmarks</H1><DL><p>");
        foreach (var b in BookmarkService.Items)
            sb.AppendLine($"    <DT><A HREF=\"{System.Net.WebUtility.HtmlEncode(b.Url)}\">{System.Net.WebUtility.HtmlEncode(b.Name)}</A>");
        sb.AppendLine("</DL><p>");
        File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
    }

    private (string Name, string Url, string Folder)? ShowEditDialog(string title, string name, string url, string folder)
    {
        bool multi = name == "(multiple)";
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            Height = multi ? 175 : 275,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = TryFindResource("Brush_DeepBackground") as Brush ?? Brushes.Black,
            Foreground = TryFindResource("Brush_Text") as Brush ?? Brushes.White
        };
        var root = new StackPanel { Margin = new Thickness(18) };
        TextBox MakeBox(string val) => new()
        {
            Text = val,
            Height = 30,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 4, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x2D)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x68))
        };

        TextBox tbName = MakeBox(name);
        TextBox tbUrl = MakeBox(url);
        TextBox tbFolder = MakeBox(folder);
        if (!multi)
        {
            root.Children.Add(new TextBlock { Text = "Title", FontSize = 11.5 });
            root.Children.Add(tbName);
            root.Children.Add(new TextBlock { Text = "URL", FontSize = 11.5 });
            root.Children.Add(tbUrl);
        }
        root.Children.Add(new TextBlock { Text = "Folder / Category", FontSize = 11.5 });
        root.Children.Add(tbFolder);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Save", Width = 80, Height = 30, IsDefault = true };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        root.Children.Add(btns);
        dlg.Content = root;
        return dlg.ShowDialog() == true ? (tbName.Text.Trim(), tbUrl.Text.Trim(), tbFolder.Text.Trim()) : null;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { TxtSearch.Focus(); TxtSearch.SelectAll(); e.Handled = true; }
    }
}
