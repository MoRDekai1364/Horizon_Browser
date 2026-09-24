using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class HomePageSettingsWindow : Window
{
    public event Action? TintStrengthChanged;
    public bool WallpaperChanged { get; private set; } = false;
    private int _editingHomepageIndex = -1;
    private bool _loadingWallpaperUi = false;

    private static readonly string[] WallpaperExts = { ".png", ".jpg", ".jpeg", ".bmp", ".mp4" };

    public HomePageSettingsWindow()
    {
        InitializeComponent();
        Loaded += HomePageSettingsWindow_Loaded;
    }

    private bool _loadingToggles = false;

    private void HomePageSettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshHomepageList();
        CmbHomepage.Text = SettingsService.Current.HomePage;
        LoadWallpaperUi();
        LoadSectionWidgetUi();
        RefreshShortcutsList();
        LoadAppearanceUi();
    }

    private static readonly string[] HomeFontChoices =
    {
        "Segoe UI", "Segoe UI Light", "Segoe UI Semibold", "Segoe Print",
        "Calibri", "Cambria", "Arial", "Georgia", "Consolas", "Trebuchet MS"
    };

    private static readonly string[] VisualizerScopeChoices = { "Everywhere", "Media Tab Only", "Selected Websites" };
    private static readonly string[] VisualizerScopeValues  = { "Everywhere", "MediaTabOnly", "SelectedWebsites" };

    private void LoadAppearanceUi()
    {
        _loadingToggles = true;

        CmbHomeFont.ItemsSource = HomeFontChoices;
        var current = SettingsService.Current.HomeFontFamily;
        CmbHomeFont.SelectedItem = HomeFontChoices.Contains(current) ? current : HomeFontChoices[0];

        ChkAdaptiveColors.IsChecked = SettingsService.Current.HomeAdaptiveColorsEnabled;

        SldTintStrength.Value = SettingsService.Current.HomeTintStrength;
        TblTintStrengthValue.Text = $"{SettingsService.Current.HomeTintStrength:0}%";

        SldInactivityTimeout.Value = SettingsService.Current.HomeInactivityTimeoutSeconds;
        TblInactivityTimeoutValue.Text = $"{SettingsService.Current.HomeInactivityTimeoutSeconds:0}s";

        ChkVisualizerEnabled.IsChecked = SettingsService.Current.HomeVisualizerEnabled;

        CmbVisualizerScope.ItemsSource = VisualizerScopeChoices;
        int scopeIdx = Array.IndexOf(VisualizerScopeValues, SettingsService.Current.HomeVisualizerScope);
        CmbVisualizerScope.SelectedIndex = scopeIdx >= 0 ? scopeIdx : 0;

        RefreshVisualizerWebsitesList();
        UpdateVisualizerWebsitesVisibility();

        _loadingToggles = false;
    }

    private void RefreshVisualizerWebsitesList()
    {
        ListVisualizerWebsites.ItemsSource = null;
        ListVisualizerWebsites.ItemsSource = SettingsService.Current.HomeVisualizerWebsites;
    }

    private void UpdateVisualizerWebsitesVisibility()
    {
        PnlVisualizerWebsites.Visibility =
            SettingsService.Current.HomeVisualizerScope == "SelectedWebsites" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VisualizerEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles) return;
        SettingsService.Current.HomeVisualizerEnabled = ChkVisualizerEnabled.IsChecked == true;
        SettingsService.Save();
    }

    private void CmbVisualizerScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingToggles) return;
        int idx = CmbVisualizerScope.SelectedIndex;
        if (idx < 0 || idx >= VisualizerScopeValues.Length) return;
        SettingsService.Current.HomeVisualizerScope = VisualizerScopeValues[idx];
        SettingsService.Save();
        UpdateVisualizerWebsitesVisibility();
    }

    private void ListVisualizerWebsites_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListVisualizerWebsites.SelectedItem is string site)
            TxtVisualizerWebsite.Text = site;
    }

    private void BtnVisualizerWebsiteAdd_Click(object sender, RoutedEventArgs e)
    {
        var site = TxtVisualizerWebsite.Text?.Trim();
        if (string.IsNullOrEmpty(site)) return;
        if (!SettingsService.Current.HomeVisualizerWebsites.Contains(site))
            SettingsService.Current.HomeVisualizerWebsites.Add(site);

        SettingsService.Save();
        RefreshVisualizerWebsitesList();
        TxtVisualizerWebsite.Text = "";
    }

    private void BtnVisualizerWebsiteRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ListVisualizerWebsites.SelectedItem is not string site) return;
        SettingsService.Current.HomeVisualizerWebsites.Remove(site);
        SettingsService.Save();
        RefreshVisualizerWebsitesList();
        TxtVisualizerWebsite.Text = "";
    }

    private void CmbHomeFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingToggles) return;
        if (CmbHomeFont.SelectedItem is string font)
        {
            SettingsService.Current.HomeFontFamily = font;
            SettingsService.Save();
        }
    }

    private void AdaptiveColors_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles) return;
        SettingsService.Current.HomeAdaptiveColorsEnabled = ChkAdaptiveColors.IsChecked == true;
        SettingsService.Save();
    }

    private void TintStrength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingToggles || TblTintStrengthValue == null) return;
        SettingsService.Current.HomeTintStrength = SldTintStrength.Value;
        TblTintStrengthValue.Text = $"{SldTintStrength.Value:0}%";
        SettingsService.Save();
        TintStrengthChanged?.Invoke();
    }

    private void InactivityTimeout_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Guard: Slider.Minimum being set during XAML parse coerces Value and
        // fires this before InitializeComponent finishes connecting the other
        // named elements (e.g. TblInactivityTimeoutValue is still null here).
        if (_loadingToggles || TblInactivityTimeoutValue == null) return;
        SettingsService.Current.HomeInactivityTimeoutSeconds = SldInactivityTimeout.Value;
        TblInactivityTimeoutValue.Text = $"{SldInactivityTimeout.Value:0}s";
        SettingsService.Save();
    }

    private void LoadSectionWidgetUi()
    {
        _loadingToggles = true;
        var s = SettingsService.Current;

        ChkSectionClockWeather.IsChecked = s.HomeShowClockWeatherSection;
        ChkSectionSearch.IsChecked       = s.HomeShowSearchSection;

        ChkWidgetClock.IsChecked     = s.HomeShowClock;
        ChkWidgetWeather.IsChecked   = s.HomeShowWeather;
        ChkWidgetMedia.IsChecked     = s.HomeShowMediaWidget;
        ChkWidgetCalendar.IsChecked  = s.HomeShowCalendarWidget;
        ChkWidgetDownloads.IsChecked = s.HomeShowDownloadsWidget;
        ChkWidgetFavorites.IsChecked = s.HomeShowFavoritesWidget;
        ChkWidgetBookmarks.IsChecked = s.HomeShowBookmarksWidget;
        ChkWidgetBattery.IsChecked   = s.HomeShowBatteryWidget;

        _loadingToggles = false;
    }

    private void SectionToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles) return;
        SettingsService.Current.HomeShowClockWeatherSection = ChkSectionClockWeather.IsChecked == true;
        SettingsService.Current.HomeShowSearchSection       = ChkSectionSearch.IsChecked == true;
        SettingsService.Save();
    }

    private void WidgetToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles) return;
        SettingsService.Current.HomeShowClock           = ChkWidgetClock.IsChecked == true;
        SettingsService.Current.HomeShowWeather         = ChkWidgetWeather.IsChecked == true;
        SettingsService.Current.HomeShowMediaWidget     = ChkWidgetMedia.IsChecked == true;
        SettingsService.Current.HomeShowCalendarWidget  = ChkWidgetCalendar.IsChecked == true;
        SettingsService.Current.HomeShowDownloadsWidget = ChkWidgetDownloads.IsChecked == true;
        SettingsService.Current.HomeShowFavoritesWidget = ChkWidgetFavorites.IsChecked == true;
        SettingsService.Current.HomeShowBookmarksWidget = ChkWidgetBookmarks.IsChecked == true;
        SettingsService.Current.HomeShowBatteryWidget   = ChkWidgetBattery.IsChecked == true;
        SettingsService.Save();
    }

    private void RefreshShortcutsList()
    {
        ListShortcuts.ItemsSource = null;
        ListShortcuts.ItemsSource = SettingsService.Current.PinnedUrls;
    }

    private void ListShortcuts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListShortcuts.SelectedItem is not PinItem pin) return;
        TxtShortcutName.Text  = pin.Name;
        TxtShortcutUrl.Text   = pin.Url;
        TxtShortcutEmoji.Text = pin.IconEmoji;
    }

    private void ClearShortcutFields()
    {
        TxtShortcutName.Text = "";
        TxtShortcutUrl.Text = "";
        TxtShortcutEmoji.Text = "";
    }

    private void BtnShortcutAdd_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtShortcutName.Text?.Trim();
        var url  = TxtShortcutUrl.Text?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;

        SettingsService.Current.PinnedUrls.Add(new PinItem
        {
            Name = name,
            Url = url,
            IconEmoji = TxtShortcutEmoji.Text?.Trim() ?? ""
        });

        SettingsService.Save();
        RefreshShortcutsList();
        ClearShortcutFields();
    }

    private void BtnShortcutUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (ListShortcuts.SelectedItem is not PinItem pin) return;

        pin.Name      = TxtShortcutName.Text?.Trim() ?? pin.Name;
        pin.Url       = TxtShortcutUrl.Text?.Trim() ?? pin.Url;
        pin.IconEmoji = TxtShortcutEmoji.Text?.Trim() ?? "";

        SettingsService.Save();
        RefreshShortcutsList();
    }

    private void BtnShortcutRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ListShortcuts.SelectedItem is not PinItem pin) return;

        SettingsService.Current.PinnedUrls.Remove(pin);
        SettingsService.Save();
        RefreshShortcutsList();
        ClearShortcutFields();
    }

    private void BtnShortcutMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (ListShortcuts.SelectedItem is not PinItem pin) return;
        var list = SettingsService.Current.PinnedUrls;
        int idx = list.IndexOf(pin);
        if (idx <= 0) return;

        (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]);
        SettingsService.Save();
        RefreshShortcutsList();
        ListShortcuts.SelectedItem = pin;
    }

    private void BtnShortcutMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (ListShortcuts.SelectedItem is not PinItem pin) return;
        var list = SettingsService.Current.PinnedUrls;
        int idx = list.IndexOf(pin);
        if (idx < 0 || idx >= list.Count - 1) return;

        (list[idx + 1], list[idx]) = (list[idx], list[idx + 1]);
        SettingsService.Save();
        RefreshShortcutsList();
        ListShortcuts.SelectedItem = pin;
    }

    private void LoadWallpaperUi()
    {
        _loadingWallpaperUi = true;

        Directory.CreateDirectory(HomePageView.WallpaperFolder);

        var files = Directory.GetFiles(HomePageView.WallpaperFolder)
            .Where(f => WallpaperExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(Path.GetFileName)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CmbWallpaperFixedFile.ItemsSource = files;
        var s = SettingsService.Current;
        CmbWallpaperFixedFile.SelectedItem = files.FirstOrDefault(f => f == s.WallpaperFileName);

        CmbWallpaperCustomOrder.ItemsSource = new[] { "Sequential", "Random" };
        CmbWallpaperCustomOrder.SelectedItem = s.WallpaperOrder;

        ListWallpaperCustom.ItemsSource = s.WallpaperCustomList.ToList();

        TblWallpaperFolderPath.Text = string.IsNullOrWhiteSpace(s.WallpaperFolderPath)
            ? "Default (app folder)"
            : s.WallpaperFolderPath;

        switch (s.WallpaperMode)
        {
            case "Random": RbWallpaperRandom.IsChecked = true; break;
            case "Custom": RbWallpaperCustom.IsChecked = true; break;
            default:       RbWallpaperFixed.IsChecked  = true; break;
        }

        _loadingWallpaperUi = false;
    }

    private void BtnOpenWallpaperFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(HomePageView.WallpaperFolder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HomePageView.WallpaperFolder) { UseShellExecute = true });
    }

    private void BtnChooseWallpaperFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Wallpapers Folder" };
        if (dlg.ShowDialog() != true) return;

        SettingsService.Current.WallpaperFolderPath = dlg.FolderName;
        SettingsService.Save();
        WallpaperChanged = true;
        LoadWallpaperUi();
    }

    private void BtnResetWallpaperFolder_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.WallpaperFolderPath = "";
        SettingsService.Save();
        WallpaperChanged = true;
        LoadWallpaperUi();
    }

    private void BtnAddWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Wallpaper",
            Filter = "Images and Video (*.png;*.jpg;*.jpeg;*.bmp;*.mp4)|*.png;*.jpg;*.jpeg;*.bmp;*.mp4",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        Directory.CreateDirectory(HomePageView.WallpaperFolder);
        foreach (var file in dlg.FileNames)
        {
            string dest = Path.Combine(HomePageView.WallpaperFolder, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        WallpaperChanged = true;
        LoadWallpaperUi();
    }

    private void WallpaperMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingWallpaperUi) return;

        if (RbWallpaperFixed.IsChecked == true) SettingsService.Current.WallpaperMode = "Fixed";
        else if (RbWallpaperRandom.IsChecked == true) SettingsService.Current.WallpaperMode = "Random";
        else if (RbWallpaperCustom.IsChecked == true) SettingsService.Current.WallpaperMode = "Custom";

        SettingsService.Save();
        WallpaperChanged = true;
    }

    private void CmbWallpaperFixedFile_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingWallpaperUi) return;
        if (CmbWallpaperFixedFile.SelectedItem is string file)
        {
            SettingsService.Current.WallpaperFileName = file;
            SettingsService.Save();
            WallpaperChanged = true;
        }
    }

    private void CmbWallpaperCustomOrder_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingWallpaperUi) return;
        if (CmbWallpaperCustomOrder.SelectedItem is string order)
        {
            SettingsService.Current.WallpaperOrder = order;
            SettingsService.Save();
            WallpaperChanged = true;
        }
    }

    private void BtnWallpaperCustomAdd_Click(object sender, RoutedEventArgs e)
    {
        if (CmbWallpaperFixedFile.SelectedItem is not string file) return;
        if (!SettingsService.Current.WallpaperCustomList.Contains(file))
            SettingsService.Current.WallpaperCustomList.Add(file);

        SettingsService.Save();
        WallpaperChanged = true;
        ListWallpaperCustom.ItemsSource = SettingsService.Current.WallpaperCustomList.ToList();
    }

    private void BtnWallpaperCustomRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ListWallpaperCustom.SelectedItem is not string file) return;
        SettingsService.Current.WallpaperCustomList.Remove(file);

        SettingsService.Save();
        WallpaperChanged = true;
        ListWallpaperCustom.ItemsSource = SettingsService.Current.WallpaperCustomList.ToList();
    }

    private void RefreshHomepageList()
    {
        CmbHomepage.ItemsSource = null;
        CmbHomepage.ItemsSource = SettingsService.Current.SavedHomepages;
    }

    private void BtnSaveHomepage_Click(object sender, RoutedEventArgs e)
    {
        var text = CmbHomepage.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (_editingHomepageIndex >= 0 && _editingHomepageIndex < SettingsService.Current.SavedHomepages.Count)
        {
            SettingsService.Current.SavedHomepages[_editingHomepageIndex] = text;
            _editingHomepageIndex = -1;
            BtnSaveHomepage.Content = "Save as New Homepage";
        }
        else if (!SettingsService.Current.SavedHomepages.Contains(text))
        {
            SettingsService.Current.SavedHomepages.Add(text);
        }

        SettingsService.Current.HomePage = text;
        SettingsService.Save();
        RefreshHomepageList();
        CmbHomepage.Text = text;
    }

    private void MenuEditHomepage_Click(object sender, RoutedEventArgs e)
    {
        if (CmbHomepage.SelectedItem is string selected)
        {
            _editingHomepageIndex = SettingsService.Current.SavedHomepages.IndexOf(selected);
            CmbHomepage.Text = selected;
            BtnSaveHomepage.Content = "Update Homepage";
        }
    }

    private void MenuDeleteHomepage_Click(object sender, RoutedEventArgs e)
    {
        if (CmbHomepage.SelectedItem is string selected)
        {
            SettingsService.Current.SavedHomepages.Remove(selected);
            SettingsService.Save();
            RefreshHomepageList();
            CmbHomepage.Text = SettingsService.Current.HomePage;
        }
    }

    private void BtnCloseHomeSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CmbHomepage.Text))
        {
            SettingsService.Current.HomePage = CmbHomepage.Text.Trim();
            SettingsService.Save();
        }
        Close();
    }
}