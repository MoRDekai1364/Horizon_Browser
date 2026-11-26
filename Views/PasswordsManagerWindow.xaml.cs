using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;
using Microsoft.Win32;

namespace Horizon.Stealth.Views;

public partial class PasswordsManagerWindow : Window
{
    public sealed class SiteSummaryRow
    {
        public string Domain { get; init; } = "";
        public int Count { get; init; }
        public ImageSource? Icon { get; init; }
    }

    public sealed class PasswordRowView
    {
        public VaultItem Source { get; }
        public string DisplayTitle { get; }
        public string Domain { get; }
        public string UsernameDisplay { get; }
        public string PasswordDisplay { get; }
        public ImageSource? Icon { get; }
        public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

        public PasswordRowView(VaultItem item, ImageSource? icon, bool revealed)
        {
            Source = item;
            Domain = ExtractDomain(item.Url);
            DisplayTitle = !string.IsNullOrWhiteSpace(item.Title) ? item.Title : (!string.IsNullOrEmpty(Domain) ? Domain : item.Url);
            UsernameDisplay = string.IsNullOrWhiteSpace(item.Username) ? "(no username)" : item.Username;
            PasswordDisplay = revealed ? item.Password : "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
            Icon = icon;
        }
    }

    private static PasswordsManagerWindow? _instance;
    private static readonly FaviconConverter _faviconConv = new();

    private readonly MainWindow _mainWindow;
    private readonly HashSet<VaultItem> _revealed = new();
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

        var win = new PasswordsManagerWindow(owner) { Owner = owner };
        _instance = win;
        win.Closed += (_, _) => _instance = null;
        win.Show();
    }

    private PasswordsManagerWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        VaultService.OnUpdated += OnVaultUpdated;
        Closed += (_, _) => VaultService.OnUpdated -= OnVaultUpdated;
        Loaded += (_, _) =>
        {
            RefreshView();
            TxtSearch.Focus();
        };
    }

    private void OnVaultUpdated()
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
        bool locked = VaultService.IsLocked;
        PnlLocked.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        BtnLockToggle.Content = locked ? "\U0001F513 Unlock Vault" : "\U0001F512 Lock Vault";

        if (locked)
        {
            LstPasswords.ItemsSource = null;
            LstSites.ItemsSource = null;
            TxtStats.Text = "Vault locked";
            return;
        }

        var all = VaultService.Items.ToList();
        string query = (TxtSearch.Text ?? "").Trim();
        BtnClearSearch.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var sites = all
            .Select(v => (Item: v, Domain: ExtractDomain(v.Url)))
            .Where(x => !string.IsNullOrEmpty(x.Domain))
            .GroupBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => new SiteSummaryRow { Domain = g.Key, Count = g.Count(), Icon = ResolveIcon(g.First().Item.Url) })
            .ToList();
        LstSites.ItemsSource = sites;
        BtnResetSite.Visibility = string.IsNullOrEmpty(_selectedDomainFilter) ? Visibility.Collapsed : Visibility.Visible;

        var reusedPasswords = all
            .Where(v => !string.IsNullOrEmpty(v.Password))
            .GroupBy(v => v.Password)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        IEnumerable<VaultItem> filtered = all;
        if (RbHealthWeak.IsChecked == true)
            filtered = filtered.Where(v => (v.Password ?? "").Length < 8);
        else if (RbHealthReused.IsChecked == true)
            filtered = filtered.Where(v => reusedPasswords.Contains(v.Password));

        if (!string.IsNullOrEmpty(_selectedDomainFilter))
            filtered = filtered.Where(v => string.Equals(ExtractDomain(v.Url), _selectedDomainFilter, StringComparison.OrdinalIgnoreCase));

        if (query.Length > 0)
        {
            filtered = filtered.Where(v =>
                (v.Title ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (v.Url ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (v.Username ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered
            .OrderBy(v => ExtractDomain(v.Url), StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Username, StringComparer.OrdinalIgnoreCase)
            .Select(v => new PasswordRowView(v, ResolveIcon(v.Url), _revealed.Contains(v)))
            .ToList();

        var view = CollectionViewSource.GetDefaultView(rows);
        view.GroupDescriptions.Clear();
        if (TabBySite.IsChecked == true)
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PasswordRowView.Domain)));

        LstPasswords.ItemsSource = view;
        TxtEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtStats.Text = $"{rows.Count} of {all.Count} saved logins";
    }

    private static string ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (!url.Contains("://")) url = "https://" + url;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.TrimStart('w', '.') : "";
    }

    private static ImageSource? ResolveIcon(string url)
    {
        try
        {
            if (!url.Contains("://")) url = "https://" + url;
            return _faviconConv.Convert(url, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
        }
        catch { return null; }
    }

    private void FlashStatus(string msg) => TxtStatus.Text = msg;

    private void ViewMode_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshView(); }
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();
    private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearch.Text = ""; TxtSearch.Focus(); }

    private void LstSites_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSites.SelectedItem is SiteSummaryRow row)
        {
            _selectedDomainFilter = row.Domain;
            RefreshView();
        }
    }

    private void BtnResetSite_Click(object sender, RoutedEventArgs e)
    {
        _selectedDomainFilter = null;
        LstSites.SelectedItem = null;
        RefreshView();
    }

    private void BtnRowReveal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PasswordRowView row) return;
        if (!_revealed.Add(row.Source)) _revealed.Remove(row.Source);
        RefreshView();
    }

    private void BtnRowCopyUser_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PasswordRowView row)
        {
            try { Clipboard.SetText(row.Source.Username); FlashStatus("Username copied to clipboard"); } catch { }
        }
    }

    private void BtnRowCopyPass_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PasswordRowView row)
        {
            try { Clipboard.SetText(row.Source.Password); FlashStatus("Password copied to clipboard"); } catch { }
        }
    }

    private void BtnRowOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PasswordRowView row) return;
        string url = row.Source.Url;
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.Contains("://")) url = "https://" + url;
        _mainWindow.CreateNewTab(url, activate: true);
    }

    private void BtnRowEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PasswordRowView row) return;
        var res = ShowCredentialDialog("Edit Login", row.Source.Title, row.Source.Url, row.Source.Username, row.Source.Password);
        if (res == null) return;
        VaultService.UpdateItem(row.Source, res.Value.Title, res.Value.Url, res.Value.User, res.Value.Pass);
    }

    private void BtnRowDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PasswordRowView row) return;
        if (MessageBox.Show(this, $"Delete login for \"{row.DisplayTitle}\"?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            VaultService.Remove(row.Source);
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var res = ShowCredentialDialog("Add Login", "", "https://", "", GenerateStrongPassword());
        if (res == null || string.IsNullOrWhiteSpace(res.Value.Url)) return;
        VaultService.Add(res.Value.Url, res.Value.User, res.Value.Pass, res.Value.Title);
    }

    private void BtnLockToggle_Click(object sender, RoutedEventArgs e)
    {
        if (VaultService.IsLocked) VaultService.Unlock();
        else { _revealed.Clear(); VaultService.Lock(); }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "CSV Files (*.csv)|*.csv", Title = "Import Passwords CSV" };
        if (dlg.ShowDialog(this) == true) VaultService.ImportCsv(dlg.FileName);
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv", FileName = "horizon_vault_export.csv", Title = "Export Credentials (UNENCRYPTED)" };
        if (dlg.ShowDialog(this) == true) VaultService.ExportCsv(dlg.FileName);
    }

    private static string GenerateStrongPassword(int length = 18)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*-_";
        var bytes = RandomNumberGenerator.GetBytes(length);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private (string Title, string Url, string User, string Pass)? ShowCredentialDialog(string header, string title, string url, string user, string pass)
    {
        var dlg = new Window
        {
            Title = header,
            Width = 450,
            Height = 360,
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

        var tbTitle = MakeBox(title);
        var tbUrl = MakeBox(url);
        var tbUser = MakeBox(user);
        var tbPass = MakeBox(pass);

        root.Children.Add(new TextBlock { Text = "Title", FontSize = 11.5 });
        root.Children.Add(tbTitle);
        root.Children.Add(new TextBlock { Text = "Website URL", FontSize = 11.5 });
        root.Children.Add(tbUrl);
        root.Children.Add(new TextBlock { Text = "Username / Email", FontSize = 11.5 });
        root.Children.Add(tbUser);

        var passHeader = new DockPanel();
        var genBtn = new Button { Content = "Generate", Padding = new Thickness(8, 2, 8, 2), FontSize = 11, Cursor = Cursors.Hand };
        genBtn.Click += (_, _) => tbPass.Text = GenerateStrongPassword();
        DockPanel.SetDock(genBtn, Dock.Right);
        passHeader.Children.Add(genBtn);
        passHeader.Children.Add(new TextBlock { Text = "Password", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(passHeader);
        root.Children.Add(tbPass);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Save", Width = 80, Height = 30, IsDefault = true };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        root.Children.Add(btns);
        dlg.Content = root;
        return dlg.ShowDialog() == true ? (tbTitle.Text.Trim(), tbUrl.Text.Trim(), tbUser.Text.Trim(), tbPass.Text) : null;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { TxtSearch.Focus(); TxtSearch.SelectAll(); e.Handled = true; }
    }
}
