using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class FavoritesManagerWindow : Window
{
    public sealed class CategorySummaryRow
    {
        public string Name { get; init; } = "";
        public int Count { get; init; }
    }

    public sealed class FavoriteRowView
    {
        public PinItem Source { get; }
        public string DisplayTitle { get; }
        public string Subtitle { get; }
        public string CategoryName { get; }
        public ImageSource? Icon { get; }
        public string FallbackEmoji => string.IsNullOrWhiteSpace(Source.IconEmoji) ? "\u2B50" : Source.IconEmoji;
        public Visibility FallbackVisibility => Icon == null || !string.IsNullOrWhiteSpace(Source.IconEmoji) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CatBadgeVisibility => string.IsNullOrWhiteSpace(Source.Category) ? Visibility.Collapsed : Visibility.Visible;

        public FavoriteRowView(PinItem item, ImageSource? icon)
        {
            Source = item;
            DisplayTitle = string.IsNullOrWhiteSpace(item.Name) ? item.Url : item.Name;
            Subtitle = item.Url;
            CategoryName = string.IsNullOrWhiteSpace(item.Category) ? "Uncategorized" : item.Category;
            Icon = icon;
        }
    }

    private static FavoritesManagerWindow? _instance;
    private static readonly FaviconConverter _faviconConv = new();

    private readonly MainWindow _mainWindow;
    private string? _selectedCategoryFilter;

    public static void ShowSingleton(MainWindow owner)
    {
        if (_instance != null)
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            _instance.TxtSearch.Focus();
            return;
        }

        var win = new FavoritesManagerWindow(owner) { Owner = owner };
        _instance = win;
        win.Closed += (_, _) => _instance = null;
        win.Show();
    }

    private FavoritesManagerWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshView();
            TxtSearch.Focus();
        };
    }

    private void SyncAndSave()
    {
        SettingsService.Save();
        _mainWindow.RightSidebar?.RefreshPinsPublic();
        RefreshView();
    }

    private void RefreshView()
    {
        var all = SettingsService.Current.PinnedUrls;
        string query = (TxtSearch.Text ?? "").Trim();
        BtnClearSearch.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var cats = all
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "Uncategorized" : p.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategorySummaryRow { Name = g.Key, Count = g.Count() })
            .ToList();
        LstCategories.ItemsSource = cats;
        BtnResetCat.Visibility = string.IsNullOrEmpty(_selectedCategoryFilter) ? Visibility.Collapsed : Visibility.Visible;

        IEnumerable<PinItem> filtered = all;
        if (!string.IsNullOrEmpty(_selectedCategoryFilter))
        {
            filtered = filtered.Where(p => string.Equals(
                string.IsNullOrWhiteSpace(p.Category) ? "Uncategorized" : p.Category.Trim(),
                _selectedCategoryFilter,
                StringComparison.OrdinalIgnoreCase));
        }

        if (query.Length > 0)
        {
            filtered = filtered.Where(p =>
                (p.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (p.Url ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (p.Category ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (TabMostUsed.IsChecked == true || RbSortUsed.IsChecked == true)
            filtered = filtered.OrderByDescending(p => p.OpenCount);
        else if (RbSortName.IsChecked == true)
            filtered = filtered.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var rows = filtered.Select(p => new FavoriteRowView(p, ResolveIcon(p.Url))).ToList();
        var view = CollectionViewSource.GetDefaultView(rows);
        view.GroupDescriptions.Clear();
        if (TabByCategory.IsChecked == true)
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FavoriteRowView.CategoryName)));

        LstFavorites.ItemsSource = view;
        TxtEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtStats.Text = $"{rows.Count} of {all.Count} favorites";
    }

    private static ImageSource? ResolveIcon(string url)
    {
        try
        {
            return _faviconConv.Convert(url, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
        }
        catch { return null; }
    }

    private void OpenFavorite(PinItem pin)
    {
        if (string.IsNullOrWhiteSpace(pin.Url)) return;
        pin.OpenCount++;
        pin.LastOpened = DateTime.Now;
        SettingsService.Save();
        _mainWindow.CreateNewTab(pin.Url, activate: true);
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void Sort_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();
    private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearch.Text = ""; TxtSearch.Focus(); }

    private void LstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstCategories.SelectedItem is CategorySummaryRow row)
        {
            _selectedCategoryFilter = row.Name;
            RefreshView();
        }
    }

    private void BtnResetCat_Click(object sender, RoutedEventArgs e)
    {
        _selectedCategoryFilter = null;
        LstCategories.SelectedItem = null;
        RefreshView();
    }

    private void LstFavorites_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int count = LstFavorites.SelectedItems.Count;
        PnlBulkBar.Visibility = count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        TxtBulkCount.Text = $"{count} items selected";
    }

    private void LstFavorites_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstFavorites.SelectedItem is FavoriteRowView row) OpenFavorite(row.Source);
    }

    private void BtnRowOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FavoriteRowView row) OpenFavorite(row.Source);
    }

    private void BtnRowUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FavoriteRowView row) return;
        var list = SettingsService.Current.PinnedUrls;
        int idx = list.IndexOf(row.Source);
        if (idx > 0)
        {
            list.RemoveAt(idx);
            list.Insert(idx - 1, row.Source);
            RbSortManual.IsChecked = true;
            SyncAndSave();
        }
    }

    private void BtnRowDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FavoriteRowView row) return;
        var list = SettingsService.Current.PinnedUrls;
        int idx = list.IndexOf(row.Source);
        if (idx >= 0 && idx < list.Count - 1)
        {
            list.RemoveAt(idx);
            list.Insert(idx + 1, row.Source);
            RbSortManual.IsChecked = true;
            SyncAndSave();
        }
    }

    private void BtnRowEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FavoriteRowView row) return;
        var edited = ShowEditDialog("Edit Favorite", row.Source.Name, row.Source.Url, row.Source.Category, row.Source.IconEmoji);
        if (edited == null) return;
        row.Source.Name = edited.Value.Name;
        row.Source.Url = edited.Value.Url;
        row.Source.Category = edited.Value.Category;
        row.Source.IconEmoji = edited.Value.Emoji;
        SyncAndSave();
    }

    private void BtnRowDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FavoriteRowView row)
        {
            SettingsService.Current.PinnedUrls.Remove(row.Source);
            SyncAndSave();
        }
    }

    private void BtnPinCurrent_Click(object sender, RoutedEventArgs e)
    {
        var browser = _mainWindow.CurrentBrowser;
        if (browser?.MainWebView == null || browser.IsProcessCrashed || browser.MainWebView.Source == null) return;
        string url = browser.MainWebView.Source.ToString();
        string title = "";
        try { title = browser.MainWebView.CoreWebView2?.DocumentTitle ?? ""; } catch { }
        _mainWindow.RightSidebar?.AddPin(url, title);
        RefreshView();
    }

    private void BtnAddFavorite_Click(object sender, RoutedEventArgs e)
    {
        var edited = ShowEditDialog("Add Favorite", "", "https://", _selectedCategoryFilter is "Uncategorized" or null ? "" : _selectedCategoryFilter, "");
        if (edited == null || string.IsNullOrWhiteSpace(edited.Value.Url)) return;
        SettingsService.Current.PinnedUrls.Add(new PinItem
        {
            Name = string.IsNullOrWhiteSpace(edited.Value.Name) ? edited.Value.Url : edited.Value.Name,
            Url = edited.Value.Url,
            Category = edited.Value.Category,
            IconEmoji = edited.Value.Emoji
        });
        SyncAndSave();
    }

    private void BtnBulkOpen_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in LstFavorites.SelectedItems.OfType<FavoriteRowView>().Take(15))
            OpenFavorite(row.Source);
    }

    private void BtnBulkCategory_Click(object sender, RoutedEventArgs e)
    {
        var items = LstFavorites.SelectedItems.OfType<FavoriteRowView>().ToList();
        if (items.Count == 0) return;
        var edited = ShowEditDialog("Set Category", "(multiple)", "(unchanged)", "", "");
        if (edited == null) return;
        foreach (var r in items) r.Source.Category = edited.Value.Category;
        SyncAndSave();
    }

    private void BtnBulkDelete_Click(object sender, RoutedEventArgs e)
    {
        var items = LstFavorites.SelectedItems.OfType<FavoriteRowView>().ToList();
        foreach (var r in items) SettingsService.Current.PinnedUrls.Remove(r.Source);
        SyncAndSave();
    }

    private (string Name, string Url, string Category, string Emoji)? ShowEditDialog(string title, string name, string url, string cat, string emoji)
    {
        bool multi = name == "(multiple)";
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            Height = multi ? 175 : 335,
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
        TextBox tbCat = MakeBox(cat);
        TextBox tbEmoji = MakeBox(emoji);
        if (!multi)
        {
            root.Children.Add(new TextBlock { Text = "Name", FontSize = 11.5 });
            root.Children.Add(tbName);
            root.Children.Add(new TextBlock { Text = "URL", FontSize = 11.5 });
            root.Children.Add(tbUrl);
            root.Children.Add(new TextBlock { Text = "Icon Emoji (optional)", FontSize = 11.5 });
            root.Children.Add(tbEmoji);
        }
        root.Children.Add(new TextBlock { Text = "Category", FontSize = 11.5 });
        root.Children.Add(tbCat);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Save", Width = 80, Height = 30, IsDefault = true };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        root.Children.Add(btns);
        dlg.Content = root;
        return dlg.ShowDialog() == true ? (tbName.Text.Trim(), tbUrl.Text.Trim(), tbCat.Text.Trim(), tbEmoji.Text.Trim()) : null;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { TxtSearch.Focus(); TxtSearch.SelectAll(); e.Handled = true; }
    }
}
