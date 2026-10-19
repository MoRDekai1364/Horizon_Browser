using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

    private Action? _detachGlassBackdrop;

    public HomePageSettingsWindow()
    {
        InitializeComponent();
        MaxHeight = Math.Min(MaxHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 60.0));
        SizeChanged += (_, __) => KeepInWorkArea();
        Loaded += HomePageSettingsWindow_Loaded;
        Loaded += (_, __) => BuildGlassBackdrop();
        Loaded += (_, __) => ClampToWorkArea();
        Loaded += (_, __) => ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Closed += (_, __) =>
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _detachGlassBackdrop?.Invoke();
        };
    }

    private void OnLanguageChanged()
    {
        Dispatcher.InvokeAsync(ApplyLocalization);
    }

    private void PreSizeTabs()
    {
        SizeToContent = SizeToContent.Manual;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Math.Max(Width, 780), Math.Max(MinWidth, area.Width - 60.0));
        Height = Math.Min(Math.Max(Height, 640), Math.Max(MinHeight, area.Height - 60.0));
        UpdateLayout();
    }

    private void KeepInWorkArea()
    {
        if (double.IsNaN(Top) || double.IsNaN(Left)) return;
        var area = SystemParameters.WorkArea;
        double top = Math.Min(Math.Max(Top, area.Top), Math.Max(area.Top, area.Bottom - ActualHeight));
        double left = Math.Min(Math.Max(Left, area.Left), Math.Max(area.Left, area.Right - ActualWidth));
        if (Math.Abs(top - Top) > 0.5) Top = top;
        if (Math.Abs(left - Left) > 0.5) Left = left;
    }

    private void ClampToWorkArea()
    {
        const double edgeMargin = 60.0;
        var area = SystemParameters.WorkArea;
        MaxWidth = Math.Min(MaxWidth, Math.Max(MinWidth, area.Width - edgeMargin));
        MaxHeight = Math.Min(MaxHeight, Math.Max(MinHeight, area.Height - edgeMargin));
        PreSizeTabs();
        UpdateLayout();
        Top = Math.Min(Math.Max(Top, area.Top), Math.Max(area.Top, area.Bottom - ActualHeight));
        Left = Math.Min(Math.Max(Left, area.Left), Math.Max(area.Left, area.Right - ActualWidth));
    }

    private void BuildGlassBackdrop()
    {
        var wallBrush = new ImageBrush();
        var wallLayer = new Border
        {
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1.08, 1.08),
            Background = wallBrush,
            Effect = new BlurEffect
            {
                Radius = 32,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            }
        };
        var tintLayer = new Border();
        var glassHost = new Grid { Visibility = Visibility.Collapsed };
        glassHost.Children.Add(wallLayer);
        glassHost.Children.Add(tintLayer);
        GlassBackdropHost.Children.Add(glassHost);

        var unbind = WidgetBackdropService.Bind(this, wallBrush, overlap =>
        {
            if (overlap == null)
            {
                glassHost.Visibility = Visibility.Collapsed;
                return;
            }
            glassHost.Clip = new RectangleGeometry(overlap.Value);
            glassHost.Visibility = Visibility.Visible;
        });

        void Apply()
        {
            byte bgAlpha = (byte)Math.Max((int)TintService.Apply(0xD9), 0xB0);
            Color searchBg = WeatherBridge.ThemeDarkWallpaper
                ? Color.FromArgb(bgAlpha, 0x1A, 0x1A, 0x1A)
                : Color.FromArgb(bgAlpha, 0xFF, 0xFF, 0xFF);
            tintLayer.Background = HomePageView.CreateSearchBarBackgroundBrush(searchBg);
            wallLayer.Opacity = WeatherBridge.ThemeWallpaper != null
                ? Math.Clamp(0.55 * SettingsService.Current.BackgroundOpacity, 0.0, 1.0)
                : 0.0;
        }

        Apply();
        Action handler = () => Dispatcher.BeginInvoke(new Action(Apply));
        WeatherBridge.ThemeUpdated += handler;
        _detachGlassBackdrop = () => { WeatherBridge.ThemeUpdated -= handler; unbind(); };
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

    private static readonly string[] GroupStyleChoices = { "Folder cards", "Header only", "First row", "Stacked preview", "Mini folder", "Full (all icons, no collapsing)" };
    private static readonly string[] GroupStyleValues  = { "Folder", "HeaderOnly", "FirstRow", "Stack", "Mini", "Full" };

    private static readonly string[] BookmarkGroupingChoices = { "By website", "Recent / Older", "One block, newest first" };
    private static readonly string[] BookmarkGroupingValues  = { "Domain", "RecentOlder", "Single" };

    private static readonly string[] FolderThresholdChoices = { "Off", "20%", "30%", "40%", "50%" };
    private static readonly int[] FolderThresholdValues  = { 0, 20, 30, 40, 50 };

    private static readonly string[] FolderOpenModeChoices = { "Popup", "In-place expand" };
    private static readonly string[] FolderOpenModeValues  = { "Popup", "InPlace" };

    private static readonly string[] VisualizerScopeChoices = { "Everywhere", "Media Tab Only", "Selected Websites" };
    private static readonly string[] VisualizerScopeValues  = { "Everywhere", "MediaTabOnly", "SelectedWebsites" };

    private static readonly string[] WallpaperCustomOrderChoices = { "Sequential", "Random" };
    private static readonly string[] WallpaperCustomOrderValues  = { "Sequential", "Random" };

    private readonly Dictionary<object, string> _originalTexts = new();
    private readonly Dictionary<FrameworkElement, string> _originalToolTips = new();

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        // Window title
        if (!_originalTexts.TryGetValue(this, out var origTitle))
        {
            origTitle = Title;
            _originalTexts[this] = origTitle;
        }
        Title = loc.GetString(origTitle, origTitle);

        LocalizeElementTree();
        ReloadLocalizedComboBoxes();
    }

    private static IEnumerable<DependencyObject> GetChildrenAggressive(DependencyObject parent)
    {
        if (parent == null) yield break;
        if (parent is ContentControl cc && cc.Content is DependencyObject cDo) yield return cDo;
        else if (parent is Decorator dec && dec.Child is DependencyObject dChild) yield return dChild;
        else if (parent is Panel p) { foreach (UIElement child in p.Children) yield return child; }
        else if (parent is ItemsControl ic) { foreach (var item in ic.Items) if (item is DependencyObject iDo) yield return iDo; }

        foreach (var child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject d) yield return d;
    }

    private static IEnumerable<T> FindDescendantsAggressive<T>(DependencyObject root, HashSet<DependencyObject>? visited = null) where T : DependencyObject
    {
        visited ??= new HashSet<DependencyObject>();
        if (root == null || !visited.Add(root)) yield break;

        if (root is T match) yield return match;

        foreach (var child in GetChildrenAggressive(root))
        {
            foreach (var descendant in FindDescendantsAggressive<T>(child, visited))
                yield return descendant;
        }
    }

    private void LocalizeElementTree()
    {
        var loc = LocalizationService.Instance;
        var elementsToLocalize = new HashSet<FrameworkElement>();

        foreach (var el in FindDescendantsAggressive<FrameworkElement>(this))
            elementsToLocalize.Add(el);

        if (TabsHomeSettings != null)
        {
            foreach (var item in TabsHomeSettings.Items)
            {
                if (item is TabItem tab)
                {
                    elementsToLocalize.Add(tab);
                    if (tab.Content is DependencyObject contentDo)
                    {
                        foreach (var el in FindDescendantsAggressive<FrameworkElement>(contentDo))
                            elementsToLocalize.Add(el);
                    }
                }
            }
        }

        foreach (var el in elementsToLocalize)
        {
            // ToolTip
            if (el.ToolTip is string tt && !string.IsNullOrWhiteSpace(tt))
            {
                if (!_originalToolTips.TryGetValue(el, out var origTt))
                {
                    origTt = tt;
                    _originalToolTips[el] = origTt;
                }
                string trans = loc.GetString(origTt, origTt);
                if (trans != tt) el.ToolTip = trans;
            }

            // TabItem Header
            if (el is TabItem tabItem && tabItem.Header is string headerStr && !string.IsNullOrWhiteSpace(headerStr))
            {
                if (!_originalTexts.TryGetValue(tabItem, out var origHeader))
                {
                    origHeader = headerStr;
                    _originalTexts[tabItem] = origHeader;
                }
                string trans = loc.GetString(origHeader, origHeader);
                if (trans != headerStr) tabItem.Header = trans;
            }

            // TextBlock
            if (el is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                if (tb != TblTintStrengthValue && tb != TblInactivityTimeoutValue && tb != TblWallpaperFolderPath)
                {
                    if (tb.GetBindingExpression(TextBlock.TextProperty) == null)
                    {
                        if (!_originalTexts.TryGetValue(tb, out var origText))
                        {
                            origText = tb.Text;
                            _originalTexts[tb] = origText;
                        }
                        string trans = loc.GetString(origText, origText);
                        if (trans != tb.Text) tb.Text = trans;
                    }
                }
            }

            // ContentControl (CheckBox, RadioButton, Button)
            if (el is ContentControl cc && cc.Content is string contentStr && !string.IsNullOrWhiteSpace(contentStr))
            {
                if (cc.GetBindingExpression(ContentControl.ContentProperty) == null)
                {
                    if (!_originalTexts.TryGetValue(cc, out var origContent))
                    {
                        origContent = contentStr;
                        _originalTexts[cc] = origContent;
                    }
                    string trans = loc.GetString(origContent, origContent);
                    if (trans != contentStr) cc.Content = trans;
                }
            }

            // ContextMenu items
            if (el.ContextMenu != null)
            {
                foreach (var item in el.ContextMenu.Items)
                {
                    if (item is MenuItem mi && mi.Header is string miHeader && !string.IsNullOrWhiteSpace(miHeader))
                    {
                        if (!_originalTexts.TryGetValue(mi, out var origMi))
                        {
                            origMi = miHeader;
                            _originalTexts[mi] = origMi;
                        }
                        string trans = loc.GetString(origMi, origMi);
                        if (trans != miHeader) mi.Header = trans;
                    }
                }
            }
        }
    }

    private void ReloadLocalizedComboBoxes()
    {
        var loc = LocalizationService.Instance;
        bool prevLoading = _loadingToggles;
        bool prevLoadingWp = _loadingWallpaperUi;
        _loadingToggles = true;
        _loadingWallpaperUi = true;

        var s = SettingsService.Current;

        int prevGroupStyle = CmbGroupStyle.SelectedIndex;
        CmbGroupStyle.ItemsSource = GroupStyleChoices.Select(c => loc.GetString(c, c)).ToList();
        CmbGroupStyle.SelectedIndex = prevGroupStyle >= 0 ? prevGroupStyle : 0;

        int prevBookmarkGrouping = CmbBookmarkGrouping.SelectedIndex;
        CmbBookmarkGrouping.ItemsSource = BookmarkGroupingChoices.Select(c => loc.GetString(c, c)).ToList();
        CmbBookmarkGrouping.SelectedIndex = prevBookmarkGrouping >= 0 ? prevBookmarkGrouping : 0;

        int prevFolderOpenMode = CmbFolderOpenMode.SelectedIndex;
        CmbFolderOpenMode.ItemsSource = FolderOpenModeChoices.Select(c => loc.GetString(c, c)).ToList();
        CmbFolderOpenMode.SelectedIndex = prevFolderOpenMode >= 0 ? prevFolderOpenMode : 0;

        int prevVisualizerScope = CmbVisualizerScope.SelectedIndex;
        CmbVisualizerScope.ItemsSource = VisualizerScopeChoices.Select(c => loc.GetString(c, c)).ToList();
        CmbVisualizerScope.SelectedIndex = prevVisualizerScope >= 0 ? prevVisualizerScope : 0;

        int prevWallpaperOrder = CmbWallpaperCustomOrder.SelectedIndex;
        CmbWallpaperCustomOrder.ItemsSource = WallpaperCustomOrderChoices.Select(c => loc.GetString(c, c)).ToList();
        CmbWallpaperCustomOrder.SelectedIndex = prevWallpaperOrder >= 0 ? prevWallpaperOrder : 0;

        if (string.IsNullOrWhiteSpace(s.WallpaperFolderPath))
        {
            TblWallpaperFolderPath.Text = loc.GetString("Default (app folder)", "Default (app folder)");
        }

        _loadingToggles = prevLoading;
        _loadingWallpaperUi = prevLoadingWp;
    }

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

        var loc = LocalizationService.Instance;
        CmbVisualizerScope.ItemsSource = VisualizerScopeChoices.Select(c => loc.GetString(c, c)).ToList();
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
        ChkWidgetMoonPhase.IsChecked = s.HomeShowMoonPhase;
        ChkWidgetSunProgress.IsChecked = s.HomeShowSunProgress;
        ChkWidgetMedia.IsChecked     = s.HomeShowMediaWidget;
        ChkWidgetCalendar.IsChecked  = s.HomeShowCalendarWidget;
        ChkWidgetDownloads.IsChecked = s.HomeShowDownloadsWidget;
        ChkWidgetFavorites.IsChecked = s.HomeShowFavoritesWidget;
        ChkWidgetBookmarks.IsChecked = s.HomeShowBookmarksWidget;
        ChkWidgetBattery.IsChecked   = s.HomeShowBatteryWidget;
        ChkRightColumn.IsChecked     = s.HomeShowRightColumn;
        ChkRightFavorites.IsChecked  = s.HomeRightColumnFavorites;
        ChkRightBookmarks.IsChecked  = s.HomeRightColumnBookmarks;
        ChkRightFavorites.IsEnabled  = s.HomeShowRightColumn;
        ChkRightBookmarks.IsEnabled  = s.HomeShowRightColumn;
        RefreshRightElementsList();

        var loc = LocalizationService.Instance;
        CmbGroupStyle.ItemsSource = GroupStyleChoices.Select(c => loc.GetString(c, c)).ToList();
        int groupStyleIdx = Array.IndexOf(GroupStyleValues, s.HomeGroupStyle);
        CmbGroupStyle.SelectedIndex = groupStyleIdx >= 0 ? groupStyleIdx : 0;

        CmbBookmarkGrouping.ItemsSource = BookmarkGroupingChoices.Select(c => loc.GetString(c, c)).ToList();
        int bookmarkGroupingIdx = Array.IndexOf(BookmarkGroupingValues, s.HomeBookmarkGrouping);
        CmbBookmarkGrouping.SelectedIndex = bookmarkGroupingIdx >= 0 ? bookmarkGroupingIdx : 0;

        int rowChoices = Horizon.Stealth.Controls.HomeRowLimits.Ceiling(SystemParameters.WorkArea.Height) - 1;
        var rowOptions = Enumerable.Range(1, rowChoices).Select(n => n.ToString()).ToList();
        CmbFavoriteRows.ItemsSource = rowOptions;
        CmbFavoriteRows.SelectedIndex = Math.Clamp(s.HomeFavoriteRows, 1, rowChoices) - 1;
        CmbBookmarkRows.ItemsSource = rowOptions.ToList();
        CmbBookmarkRows.SelectedIndex = Math.Clamp(s.HomeBookmarkRows, 1, rowChoices) - 1;

        CmbFolderThreshold.ItemsSource = FolderThresholdChoices;
        int folderThresholdIdx = Array.IndexOf(FolderThresholdValues, s.HomeFolderThreshold);
        CmbFolderThreshold.SelectedIndex = folderThresholdIdx >= 0 ? folderThresholdIdx : 2;

        CmbFolderOpenMode.ItemsSource = FolderOpenModeChoices.Select(c => loc.GetString(c, c)).ToList();
        int folderOpenModeIdx = Array.IndexOf(FolderOpenModeValues, s.HomeFolderOpenMode);
        CmbFolderOpenMode.SelectedIndex = folderOpenModeIdx >= 0 ? folderOpenModeIdx : 0;

        _loadingToggles = false;
    }

    private void SectionToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles) return;
        SettingsService.Current.HomeShowClockWeatherSection = ChkSectionClockWeather.IsChecked == true;
        SettingsService.Current.HomeShowSearchSection       = ChkSectionSearch.IsChecked == true;
        SettingsService.Save();
    }

    private void RightColumnToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles) return;
        var s = SettingsService.Current;
        s.HomeShowRightColumn = ChkRightColumn.IsChecked == true;
        s.HomeRightColumnFavorites = ChkRightFavorites.IsChecked == true;
        s.HomeRightColumnBookmarks = ChkRightBookmarks.IsChecked == true;
        ChkRightFavorites.IsEnabled = s.HomeShowRightColumn;
        ChkRightBookmarks.IsEnabled = s.HomeShowRightColumn;
        SettingsService.Save();
    }

    private void RefreshRightElementsList()
    {
        ListRightElements.ItemsSource = StackElementStore.Elements.ToList();
    }

    private void BtnRightElementRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ListRightElements.SelectedItem is not StackElementData data) return;
        var loc = LocalizationService.Instance;
        string prompt = LocalizationService.Format("Remove \"{0}\"?", data.Title);
        if (prompt == "Remove \"{0}\"?") prompt = $"Remove \"{data.Title}\"?";
        string title = loc.GetString("Remove element", "Remove element");
        var answer = MessageBox.Show(this, prompt, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        StackElementStore.Remove(data.Id);
        RefreshRightElementsList();
    }

    private void WidgetToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles) return;
        SettingsService.Current.HomeShowClock           = ChkWidgetClock.IsChecked == true;
        SettingsService.Current.HomeShowWeather         = ChkWidgetWeather.IsChecked == true;
        SettingsService.Current.HomeShowMoonPhase = ChkWidgetMoonPhase.IsChecked == true;
        SettingsService.Current.HomeShowSunProgress = ChkWidgetSunProgress.IsChecked == true;
        SettingsService.Current.HomeShowMediaWidget     = ChkWidgetMedia.IsChecked == true;
        SettingsService.Current.HomeShowCalendarWidget  = ChkWidgetCalendar.IsChecked == true;
        SettingsService.Current.HomeShowDownloadsWidget = ChkWidgetDownloads.IsChecked == true;
        SettingsService.Current.HomeShowFavoritesWidget = ChkWidgetFavorites.IsChecked == true;
        SettingsService.Current.HomeShowBookmarksWidget = ChkWidgetBookmarks.IsChecked == true;
        SettingsService.Current.HomeShowBatteryWidget   = ChkWidgetBattery.IsChecked == true;
        SettingsService.Save();
    }

    private void CmbGroupStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingToggles) return;
        int idx = CmbGroupStyle.SelectedIndex;
        if (idx < 0 || idx >= GroupStyleValues.Length) return;
        SettingsService.Current.HomeGroupStyle = GroupStyleValues[idx];
        SettingsService.Save();
    }

    private void CmbFavoriteRows_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingToggles) return;
        int idx = CmbFavoriteRows.SelectedIndex;
        if (idx < 0) return;
        SettingsService.Current.HomeFavoriteRows = idx + 1;
        SettingsService.Save();
    }

    private void CmbBookmarkRows_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingToggles) return;
        int idx = CmbBookmarkRows.SelectedIndex;
        if (idx < 0) return;
        SettingsService.Current.HomeBookmarkRows = idx + 1;
        SettingsService.Save();
    }

    private void CmbFolderOpenMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingToggles) return;
        int idx = CmbFolderOpenMode.SelectedIndex;
        if (idx < 0 || idx >= FolderOpenModeValues.Length) return;
        SettingsService.Current.HomeFolderOpenMode = FolderOpenModeValues[idx];
        SettingsService.Save();
    }

    private void CmbFolderThreshold_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingToggles) return;
        int idx = CmbFolderThreshold.SelectedIndex;
        if (idx < 0 || idx >= FolderThresholdValues.Length) return;
        SettingsService.Current.HomeFolderThreshold = FolderThresholdValues[idx];
        SettingsService.Save();
    }

    private void CmbBookmarkGrouping_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingToggles) return;
        int idx = CmbBookmarkGrouping.SelectedIndex;
        if (idx < 0 || idx >= BookmarkGroupingValues.Length) return;
        SettingsService.Current.HomeBookmarkGrouping = BookmarkGroupingValues[idx];
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

        var loc = LocalizationService.Instance;
        CmbWallpaperCustomOrder.ItemsSource = WallpaperCustomOrderChoices.Select(c => loc.GetString(c, c)).ToList();
        int orderIdx = Array.IndexOf(WallpaperCustomOrderValues, s.WallpaperOrder);
        CmbWallpaperCustomOrder.SelectedIndex = orderIdx >= 0 ? orderIdx : 0;

        ListWallpaperCustom.ItemsSource = s.WallpaperCustomList.ToList();

        TblWallpaperFolderPath.Text = string.IsNullOrWhiteSpace(s.WallpaperFolderPath)
            ? loc.GetString("Default (app folder)", "Default (app folder)")
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
        if (_loadingWallpaperUi || _loadingToggles) return;
        int idx = CmbWallpaperCustomOrder.SelectedIndex;
        if (idx >= 0 && idx < WallpaperCustomOrderValues.Length)
        {
            SettingsService.Current.WallpaperOrder = WallpaperCustomOrderValues[idx];
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
            BtnSaveHomepage.Content = LocalizationService.Instance.GetString("Save as New Homepage", "Save as New Homepage");
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
            BtnSaveHomepage.Content = LocalizationService.Instance.GetString("Update Homepage", "Update Homepage");
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
