using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Data;
using System.ComponentModel;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class HomePageView : UserControl
{
    private DispatcherTimer? _clockTimer;
    private static readonly Random _rng = new Random();
    private static readonly Dictionary<string, Color> _wallpaperColorCache = new();
    private static readonly Dictionary<string, BitmapImage> _wallpaperBitmapCache = new();
    private int _wallpaperLoadToken = 0;
    private static readonly SemaphoreSlim _wallpaperPreloadGate = new SemaphoreSlim(1, 1);
    private bool _isContentVisible = false;
    private bool _wallpaperInitialized = false;
    private bool _favoritesExpanded = false;
    private bool _bookmarksExpanded = false;
    private bool _favoritesAnimating = false;
    private bool _bookmarksAnimating = false;
    private string _favoritesSig = "";
    private string _bookmarksSig = "";
    private readonly HashSet<string> _collapsedFavoriteGroups = new();
    private readonly HashSet<string> _collapsedBookmarkGroups = new();
    private readonly HashSet<string> _openFavoriteGroups = new();
    private readonly HashSet<string> _openBookmarkGroups = new();
    private readonly HashSet<GroupItem> _animatingGroups = new();
    private DispatcherTimer? _inactivityTimer;
    private Color _lastPillBg = Color.FromArgb(0x80, 0x00, 0x00, 0x00);
    private Color? _lastAdaptiveAvgColor = null;
    private Color _lastSearchTextColor = Colors.White;

    public Color SearchBarTextColor => _lastSearchTextColor;
    private double _homeVizFade = 0.0;
    private RadialGradientBrush? _vizBrushTL, _vizBrushTR, _vizBrushBL, _vizBrushBR;
    private bool _vizPulseRunning;

    private readonly HomeGlassInlineLayer _homeGlass;

    public event Action<string>? NavigateRequested;

    public HomePageView()
    {
        InitializeComponent();
        _homeGlass = new HomeGlassInlineLayer(RootHomeGrid, BgImageBrush, BgVideoElement);
        _homeGlass.Register(SearchBoxBorder);
        _homeGlass.Register(SearchBlurPill);
        _homeGlass.Register(FavBookmarksIslandBorder);
        _homeGlass.Register(PnlBatteryPill);
        _homeGlass.Register(BtnChangeWallpaperBg);
        Loaded += (_, __) => WeatherBridge.SetWallpaperSurface(RootHomeGrid);
        Unloaded += (_, __) =>
        {
            if (WeatherBridge.WallpaperSurface == RootHomeGrid)
                WeatherBridge.SetWallpaperSurface(null);
        };
        PnlClockWeather.SizeChanged += (_, __) => UpdateClockWeatherIslandBounds();
        SizeChanged += (_, __) => ApplyScrollCap();
        PnlClockWeather.SizeChanged += (_, __) => ReanchorInactivityMedia();
        IsVisibleChanged += (_, e) =>
        {
            if (!IsVisible) return;
            RefreshSearchEngineList();
            RefreshFavorites();
            RefreshBookmarks();
            WeatherBridge.SetWallpaperSurface(RootHomeGrid);
            if (BgImageBrush.ImageSource is BitmapSource ownBitmap) WeatherBridge.SetWallpaper(ownBitmap);
            if (_lastAdaptiveAvgColor.HasValue) PublishWeatherTheme(_lastAdaptiveAvgColor.Value);
        };
        MouseMove += (_, _) => RegisterUserActivity();
        MouseEnter += (_, _) => RegisterUserActivity();
        PreviewKeyDown += (_, e) =>
        {
            RegisterUserActivity();
            if (e.Key == Key.Escape && _inFocusMode)
            {
                ExitWidgetFocusMode();
                e.Handled = true;
            }
        };
        PreviewMouseDown += (_, _) => RegisterUserActivity();
    }

    private bool _inFocusMode = false;
    private string _focusWidgetName = "";

    private void AnimateElementOpacity(UIElement target, double to, TimeSpan duration)
    {
        var anim = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        anim.CurrentTimeInvalidated += (_, _) => _homeGlass.Invalidate();
        anim.Completed += (_, _) => _homeGlass.Invalidate();
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void CollapseContainer(UIElement target, TimeSpan duration)
    {
        target.IsHitTestVisible = false;
        var anim = new DoubleAnimation
        {
            To = 0.0,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) =>
        {
            if (target is FrameworkElement fe) fe.Visibility = Visibility.Collapsed;
        };
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void RevealContainer(FrameworkElement target, double to, TimeSpan duration)
    {
        target.Visibility = Visibility.Visible;
        AnimateElementOpacity(target, to, duration);
    }

    private double ComputeExpandedScrollMaxHeight()
    {
        double topMargin = PnlSearchArea.Margin.Top;
        double searchBarHeight = SearchBoxBorder.ActualHeight > 0 ? SearchBoxBorder.ActualHeight : 48;
        double containerTopGap = 10;
        double toggleButtonReserve = 12 + 40;
        double bottomBreathingRoom = 24;

        double reserved = topMargin + searchBarHeight + containerTopGap + toggleButtonReserve + bottomBreathingRoom;
        double available = RootHomeGrid.ActualHeight - reserved;
        return Math.Max(160, available);
    }

    private void FadeMoveSearchArea(Action applyNewLayout, TimeSpan duration)
    {
        PnlSearchArea.RenderTransform = null;

        var fadeOut = new DoubleAnimation
        {
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(120)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) =>
        {
            applyNewLayout();
            PnlSearchArea.UpdateLayout();

            var fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = new Duration(duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            PnlSearchArea.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        PnlSearchArea.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void EnterWidgetFocusMode(string widgetName)
    {
        if (_inFocusMode && _focusWidgetName == widgetName) return;
        _inFocusMode = true;
        _focusWidgetName = widgetName;
        _inactivityTimer?.Stop();

        var transformDuration = TimeSpan.FromMilliseconds(220);
        var moveDuration = TimeSpan.FromMilliseconds(260);

        AnimateElementOpacity(PnlClockWeather, 0.0, transformDuration);
        PnlClockWeather.IsHitTestVisible = false;
        if (ClockWeatherHitOverlay != null) ClockWeatherHitOverlay.IsHitTestVisible = false;
        AnimateElementOpacity(ClockWeatherIslandBorder, 0.0, transformDuration);
        AnimateElementOpacity(BtnHomeSettings, 0.0, transformDuration);
        BtnHomeSettings.IsHitTestVisible = false;
        AnimateElementOpacity(BtnChangeWallpaper, 0.0, transformDuration);
        BtnChangeWallpaper.IsHitTestVisible = false;

        if (widgetName == "Favorites")
        {
            CollapseContainer(PnlBookmarksContainer, transformDuration);
            _favoritesExpanded = true;
            PnlFavoritesContainer.Margin = new Thickness(0, 10, 0, 0);
            if (ScvFavorites != null)
            {
                PuzzlePanel.SetViewportBudget(ScvFavorites, 0);
                ScvFavorites.MaxHeight = ComputeExpandedScrollMaxHeight();
            }
            TxtToggleFavorites.Text = "▲ Show less";
            RefreshFavorites();
        }
        else
        {
            CollapseContainer(PnlFavoritesContainer, transformDuration);
            _bookmarksExpanded = true;
            PnlBookmarksContainer.Margin = new Thickness(0, 10, 0, 0);
            if (ScvBookmarks != null)
            {
                PuzzlePanel.SetViewportBudget(ScvBookmarks, 0);
                ScvBookmarks.MaxHeight = ComputeExpandedScrollMaxHeight();
            }
            TxtToggleBookmarks.Text = "▲ Show less";
            RefreshBookmarks();
        }

        CmbSearchEngine.Visibility = Visibility.Collapsed;
        CmbCategoryFilter.Visibility = Visibility.Visible;
        TxtHomeSearchPlaceholder.Text = "Search " + widgetName.ToLower() + "...";
        TxtHomeSearch.Text = "";

        var categories = new List<string> { "All Categories" };
        if (widgetName == "Favorites")
        {
            var distinct = SettingsService.Current.PinnedUrls
                .Select(p => string.IsNullOrWhiteSpace(p.Category) ? "General" : p.Category)
                .Distinct()
                .OrderBy(c => c);
            categories.AddRange(distinct);
        }
        else
        {
            categories.Add("Recent");
            categories.Add("Older");
        }

        CmbCategoryFilter.SelectionChanged -= CmbCategoryFilter_SelectionChanged;
        CmbCategoryFilter.ItemsSource = categories;
        CmbCategoryFilter.SelectedIndex = 0;
        CmbCategoryFilter.SelectionChanged += CmbCategoryFilter_SelectionChanged;

        // Phase 2: reposition only after the transform (fade/collapse) has settled.
        var repositionTimer = new DispatcherTimer { Interval = transformDuration };
        repositionTimer.Tick += (_, _) =>
        {
            repositionTimer.Stop();
            FadeMoveSearchArea(() =>
            {
                Grid.SetColumnSpan(PnlSearchArea, 3);
                Grid.SetColumn(PnlSearchArea, 0);
                double fullWidth = RootHomeGrid.ActualWidth > 0 ? RootHomeGrid.ActualWidth : ActualWidth;
                PnlSearchArea.MaxWidth = Math.Max(fullWidth - 40, 900);
                SearchBoxBorder.MaxWidth = Math.Min(fullWidth - 100, 1100);
                PnlFavoritesContainer.Margin = new Thickness(0, 28, 0, 0);
                PnlBookmarksContainer.Margin = new Thickness(0, 28, 0, 0);
            }, moveDuration);
        };
        repositionTimer.Start();
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject? target)
    {
        while (node != null)
        {
            if (node == target) return true;
            if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                node = VisualTreeHelper.GetParent(node);
            else
                node = LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    private void RootHomeGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_inFocusMode) return;

        var source = e.OriginalSource as DependencyObject;
        if (IsDescendantOf(source, PnlSearchArea)) return;
        if (IsDescendantOf(source, RootHomeGrid.ContextMenu)) return;

        var parentPopup = VisualTreeHelper.GetParent(source) as System.Windows.Controls.Primitives.Popup;
        if (parentPopup != null || source is MenuItem) return;

        if (_focusWidgetName == "Favorites")
        {
            if (IsDescendantOf(source, PnlFavoritesContainer) || IsDescendantOf(source, BtnToggleFavorites)) return;
        }
        else if (_focusWidgetName == "Bookmarks")
        {
            if (IsDescendantOf(source, PnlBookmarksContainer) || IsDescendantOf(source, BtnToggleBookmarks)) return;
        }

        ExitWidgetFocusMode();
        e.Handled = true;
    }

    private void ExitWidgetFocusMode()
    {
        if (!_inFocusMode) return;
        _inFocusMode = false;
        _focusWidgetName = "";
        _inactivityTimer?.Start();

        var moveDuration = TimeSpan.FromMilliseconds(220);
        var transformDuration = TimeSpan.FromMilliseconds(250);

        FadeMoveSearchArea(UpdateSearchAreaLayout, moveDuration);

        PnlFavoritesContainer.Margin = new Thickness(0, 30, 0, 0);
        PnlBookmarksContainer.Margin = new Thickness(0, 40, 0, 0);

        AnimateElementOpacity(PnlClockWeather, 1.0, transformDuration);
        PnlClockWeather.IsHitTestVisible = true;
        if (ClockWeatherHitOverlay != null) ClockWeatherHitOverlay.IsHitTestVisible = true;
        AnimateElementOpacity(ClockWeatherIslandBorder, _isContentVisible ? 0.85 : 0.0, transformDuration);
        AnimateElementOpacity(BtnHomeSettings, 1.0, transformDuration);
        BtnHomeSettings.IsHitTestVisible = true;
        AnimateElementOpacity(BtnChangeWallpaper, 1.0, transformDuration);
        BtnChangeWallpaper.IsHitTestVisible = true;

        RevealContainer(PnlFavoritesContainer, 1.0, transformDuration);
        PnlFavoritesContainer.IsHitTestVisible = true;
        RevealContainer(PnlBookmarksContainer, 1.0, transformDuration);
        PnlBookmarksContainer.IsHitTestVisible = true;

        _favoritesExpanded = false;
        _bookmarksExpanded = false;
        ApplyScrollCap();
        if (TxtToggleFavorites != null) TxtToggleFavorites.Text = "▼ Show more";
        if (TxtToggleBookmarks != null) TxtToggleBookmarks.Text = "▼ Show more";

        CmbSearchEngine.Visibility = Visibility.Visible;
        CmbCategoryFilter.Visibility = Visibility.Collapsed;
        TxtHomeSearchPlaceholder.Text = "Search here ...";
        TxtHomeSearch.Text = "";

        RefreshFavorites();
        RefreshBookmarks();
    }

    private void BtnToggleFavorites_Click(object sender, RoutedEventArgs e)
    {
        if (_favoritesAnimating) return;
        if (_inFocusMode)
        {
            ExitWidgetFocusMode();
        }
        else
        {
            EnterWidgetFocusMode("Favorites");
        }
    }

    private void BtnToggleBookmarks_Click(object sender, RoutedEventArgs e)
    {
        if (_bookmarksAnimating) return;
        if (_inFocusMode)
        {
            ExitWidgetFocusMode();
        }
        else
        {
            EnterWidgetFocusMode("Bookmarks");
        }
    }

    private void WidgetContainer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_inFocusMode || FullStyle || e.Delta >= 0) return;
        if (sender == PnlFavoritesContainer && SettingsService.Current.PinnedUrls.Count > 6)
        {
            EnterWidgetFocusMode("Favorites");
        }
        else if (sender == PnlBookmarksContainer && BookmarkService.Items.Count > 6)
        {
            EnterWidgetFocusMode("Bookmarks");
        }
    }

    private void CmbCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_inFocusMode) return;
        if (_focusWidgetName == "Favorites") RefreshFavorites();
        else if (_focusWidgetName == "Bookmarks") RefreshBookmarks();
    }

    private void HomeScrollBar_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ScrollBar bar && bar.TemplatedParent is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(e.NewValue);
        }
    }

    private bool _winStateHooked = false;

    private void UpdateSearchAreaLayout()
    {
        double totalWidth = RootHomeGrid.ActualWidth > 0 ? RootHomeGrid.ActualWidth : ActualWidth;
        if (totalWidth <= 0) return;

        if (totalWidth < 600)
        {
            Grid.SetRow(PnlSearchArea, 1);
            Grid.SetColumn(PnlSearchArea, 0);
            Grid.SetColumnSpan(PnlSearchArea, 3);
            ColRightBalance.Width = new GridLength(0);
            PnlClockWeather.Margin = new Thickness(16, 24, 16, 0);
            PnlSearchArea.Margin = new Thickness(10, 20, 10, 0);
            double maxMobileWidth = Math.Max(200.0, totalWidth - 20.0);
            PnlSearchArea.MaxWidth = maxMobileWidth;
            SearchBoxBorder.MaxWidth = Math.Min(900.0, maxMobileWidth);
            return;
        }

        Grid.SetRow(PnlSearchArea, 0);
        Grid.SetColumn(PnlSearchArea, 1);
        Grid.SetColumnSpan(PnlSearchArea, 1);
        PnlClockWeather.Margin = new Thickness(40, 50, 40, 0);

        double leftColWidth = 0;
        if (PnlClockWeather.Visibility == Visibility.Visible)
        {
            leftColWidth = PnlClockWeather.ActualWidth + PnlClockWeather.Margin.Left + PnlClockWeather.Margin.Right;
            if (leftColWidth <= 80) leftColWidth = 430.0;
        }

        const double minMiddleWidth = 520.0;
        const double fallbackBuffer = 20.0;

        double availableMiddleWidth = totalWidth - leftColWidth - leftColWidth;

        if (availableMiddleWidth >= minMiddleWidth)
        {
            ColRightBalance.Width = new GridLength(leftColWidth);
        }
        else
        {
            double remaining = totalWidth - leftColWidth - fallbackBuffer;
            if (remaining >= 360.0)
            {
                ColRightBalance.Width = new GridLength(fallbackBuffer);
                availableMiddleWidth = remaining;
            }
            else
            {
                ColRightBalance.Width = new GridLength(0);
                availableMiddleWidth = Math.Max(240.0, totalWidth - leftColWidth);
            }
        }

        double maxAllowedWidth = Math.Max(240.0, availableMiddleWidth - 20.0);
        PnlSearchArea.MaxWidth = maxAllowedWidth;

        double searchMax = Math.Max(240.0, Math.Min(900.0, maxAllowedWidth));
        SearchBoxBorder.MaxWidth = searchMax;
        PnlSearchArea.Margin = new Thickness(10, 60, 10, 0);
    }

    private void HomePageView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSearchAreaLayout();
        if (!_winStateHooked)
        {
            var win = Window.GetWindow(this);
            if (win != null)
            {
                win.StateChanged += (_, _) => UpdateSearchAreaLayout();
                _winStateHooked = true;
            }
            SizeChanged += (_, _) => UpdateSearchAreaLayout();
        }

        UpdateClock();
        RefreshCalendar();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => { UpdateClock(); RefreshCalendar(); };
        _clockTimer.Start();

        WeatherBridge.Updated += RefreshWeather;
        RefreshWeather();

        MediaBridge.Updated += RefreshMedia;
        RefreshMedia();

        InitVizBrushes();
        MediaBridge.GradientUpdated += DrawHomeVisualizer;
        DrawHomeVisualizer();

        CalendarBridge.Updated += RefreshCalendar;

        BatteryBridge.Start();
        BatteryBridge.Updated += RefreshBattery;
        RefreshBattery();

        DownloadsBridge.Updated += RefreshDownloads;
        RefreshDownloads();

        InitVpnAdBlockWidget();

        BookmarkService.OnUpdated += RefreshBookmarks;
        RefreshBookmarks();

        RefreshFavorites();

        RefreshSearchEngineList();
        if (!_wallpaperInitialized)
        {
            _wallpaperInitialized = true;
            ApplyWallpaper();
        }
        InitDefaultLayoutSets();
        ApplySectionAndWidgetVisibility();
        UpdateClockWeatherIslandBounds();
        ApplyActiveLayoutMatrix(false);
        ApplyFont();
        TxtHomeSearchPlaceholder.Visibility = Visibility.Visible;

        foreach (var elem in new UIElement[] { BtnHomeSettings, PnlSearchArea, SearchBlurPill })
        {
            elem.Opacity = 0.0;
            elem.IsHitTestVisible = false;
        }
        _isContentVisible = false;

        _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SettingsService.Current.HomeInactivityTimeoutSeconds) };
        _inactivityTimer.Tick += (_, _) =>
        {
            _inactivityTimer?.Stop();
            if (_isContentVisible)
            {
                AnimateInteractiveContent(false);
            }
        };

        if (IsMouseOver)
        {
            RegisterUserActivity();
        }
    }

    private void RegisterUserActivity()
    {
        _inactivityTimer?.Stop();
        _inactivityTimer?.Start();

        if (!_isContentVisible)
        {
            AnimateInteractiveContent(true);
        }
    }

    private void AnimateWidgetContent(UIElement target, Action updateAction, Action onComplete)
    {
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) =>
        {
            updateAction();

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };

            fadeIn.Completed += (_, _) => { onComplete(); };
            target.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        target.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void AnimateDouble(Animatable target, DependencyProperty prop, double to, TimeSpan duration, IEasingFunction easing)
    {
        var anim = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(duration),
            EasingFunction = easing
        };
        anim.CurrentTimeInvalidated += (_, _) => _homeGlass.Invalidate();
        anim.Completed += (_, _) => _homeGlass.Invalidate();
        target.BeginAnimation(prop, anim);
    }

    private void AnimateInteractiveContent(bool show)
    {
        _isContentVisible = show;
        if (_inEditMode) return;

        var duration = TimeSpan.FromMilliseconds(show ? 350 : 750);
        var opacityEase = new QuadraticEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var transformEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opacityAnim = new DoubleAnimation
        {
            To = show ? 1.0 : 0.0,
            Duration = new Duration(duration),
            EasingFunction = opacityEase
        };

        foreach (var elem in new UIElement[] { BtnHomeSettings, PnlSearchArea, SearchBlurPill })
        {
            elem.IsHitTestVisible = show;
            elem.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        ApplyActiveLayoutMatrix(!show, duration, transformEase);
    }

    private void ApplyFont()
    {
        var fontName = SettingsService.Current.HomeFontFamily;
        if (string.IsNullOrWhiteSpace(fontName)) fontName = "Segoe UI";

        var font = new FontFamily(fontName);

        foreach (var tb in new TextBlock[]
        {
            TblClock, TblWeather, TblWeatherCity, TblCalendarDate,
            TblCalendarPrev, TblCalendarNext, TblMediaTitle, TblMediaArtist
        })
        {
            tb.FontFamily = font;
            tb.FontWeight = FontWeights.Bold;
        }

        TxtHomeSearch.FontFamily = font;
        TxtHomeSearch.FontWeight = FontWeights.Bold;
    }

    private static System.Windows.Media.Effects.DropShadowEffect MakeOutline(Color textColor)
    {
        RgbToHsl(textColor, out _, out _, out double lum);
        Color outlineColor = lum > 0.55 ? Colors.Black : Colors.White;

        return new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = outlineColor,
            Direction = 0,
            ShadowDepth = 0,
            BlurRadius = 6,
            Opacity = 0.95,
            RenderingBias = RenderingBias.Performance
        };
    }

    public void ApplySectionAndWidgetVisibility()
    {
        var s = SettingsService.Current;

        PnlClockWeather.Visibility = s.HomeShowClockWeatherSection ? Visibility.Visible : Visibility.Collapsed;
        PnlSearchArea.Visibility   = s.HomeShowSearchSection ? Visibility.Visible : Visibility.Collapsed;

        TblClock.Visibility = s.HomeShowClock ? Visibility.Visible : Visibility.Collapsed;

        var weatherVis = s.HomeShowWeather ? Visibility.Visible : Visibility.Collapsed;
        TblWeather.Visibility = weatherVis;
        TblWeatherCity.Visibility = weatherVis;

        PnlCalendar.Visibility = s.HomeShowCalendarWidget ? Visibility.Visible : Visibility.Collapsed;
        PnlFavoritesContainer.Visibility = s.HomeShowFavoritesWidget ? Visibility.Visible : Visibility.Collapsed;
        PnlBookmarksContainer.Visibility = s.HomeShowBookmarksWidget ? Visibility.Visible : Visibility.Collapsed;
        PnlBatteryPill.Visibility = (s.HomeShowBatteryWidget && BatteryBridge.HasBattery) ? Visibility.Visible : Visibility.Collapsed;

        RefreshMedia();
        RefreshDownloads();
        UpdateSearchAreaLayout();
    }

    private static readonly string[] WallpaperImageExts = { ".png", ".jpg", ".jpeg", ".bmp" };
    private static readonly string[] WallpaperVideoExts = { ".mp4" };

    public static string WallpaperFolder =>
        !string.IsNullOrWhiteSpace(SettingsService.Current.WallpaperFolderPath)
            ? SettingsService.Current.WallpaperFolderPath
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallpapers");

    private static bool IsWallpaperFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return WallpaperImageExts.Contains(ext) || WallpaperVideoExts.Contains(ext);
    }

    private static string GetFirstWallpaperInFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return "";
            var files = Directory.GetFiles(folder).Where(IsWallpaperFile).ToArray();
            if (files.Length == 0) return "";
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files[0];
        }
        catch
        {
            return "";
        }
    }

    private static string GetNextShuffledWallpaper(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return "";
            var all = Directory.GetFiles(folder).Where(IsWallpaperFile).ToArray();
            if (all.Length == 0) return "";

            string queueFile = Path.Combine(folder, ".wallpaper_queue.txt");
            var queue = new List<string>();

            if (File.Exists(queueFile))
            {
                queue = File.ReadAllLines(queueFile)
                            .Where(f => File.Exists(f) && IsWallpaperFile(f))
                            .ToList();
            }

            if (queue.Count == 0)
            {
                queue = all.OrderBy(_ => Guid.NewGuid()).ToList();
            }

            string selected = queue[0];
            queue.RemoveAt(0);

            if (queue.Count == 0)
            {
                queue = all.OrderBy(_ => Guid.NewGuid()).ToList();
                if (queue.Count > 1 && queue[0] == selected)
                {
                    var temp = queue[0];
                    queue[0] = queue[1];
                    queue[1] = temp;
                }
            }

            File.WriteAllLines(queueFile, queue);

            return selected;
        }
        catch
        {
            var fallback = Directory.Exists(folder) ? Directory.GetFiles(folder).Where(IsWallpaperFile).ToArray() : Array.Empty<string>();
            return fallback.Length > 0 ? fallback[_rng.Next(fallback.Length)] : "";
        }
    }

    private static string ResolveWallpaperPath()
    {
        string folder = WallpaperFolder;
        var s = SettingsService.Current;

        try
        {
            switch (s.WallpaperMode)
            {
                case "Random":
                {
                    return GetNextShuffledWallpaper(folder);
                }
                case "Custom":
                {
                    var list = s.WallpaperCustomList;
                    if (list == null || list.Count == 0) return "";

                    int nextIndex = s.WallpaperOrder == "Random"
                        ? _rng.Next(list.Count)
                        : (s.WallpaperCustomIndex + 1) % list.Count;
                    s.WallpaperCustomIndex = nextIndex;
                    SettingsService.Save();

                    return Path.Combine(folder, list[nextIndex]);
                }
                default: // Fixed
                {
                    if (!string.IsNullOrEmpty(s.WallpaperFileName))
                        return Path.Combine(folder, s.WallpaperFileName);

                    return GetFirstWallpaperInFolder(folder);
                }
            }
        }
        catch
        {
            return GetFirstWallpaperInFolder(folder);
        }
    }

    private bool _wallpaperApplyBusy = false;

    private void BtnChangeWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (_wallpaperApplyBusy) return;
        ApplyWallpaper();
    }

    private static bool TryGetCachedWallpaper(string path, out BitmapImage bmp, out Color avg)
    {
        bmp = null!;
        avg = default;

        if (!File.Exists(path)) return false;

        string cacheKey = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;

        lock (_wallpaperBitmapCache)
        {
            if (!_wallpaperBitmapCache.TryGetValue(cacheKey, out var cachedBmp)) return false;
            bmp = cachedBmp;
        }

        lock (_wallpaperColorCache)
        {
            _wallpaperColorCache.TryGetValue(path, out avg);
        }

        return true;
    }

    private void CrossfadeWallpaper(ImageSource? previous, ImageSource? next)
    {
        if (previous == null || next == null || ReferenceEquals(previous, next)) return;
        var ghost = new System.Windows.Shapes.Rectangle
        {
            IsHitTestVisible = false,
            Fill = new ImageBrush(previous) { Stretch = Stretch.UniformToFill, Opacity = BgImageBrush.Opacity }
        };
        RootHomeGrid.Children.Insert(0, ghost);
        var anim = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(300)))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        anim.Completed += (_, _) => RootHomeGrid.Children.Remove(ghost);
        ghost.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private async void ApplyWallpaper()
    {
        if (_wallpaperApplyBusy) return;
        _wallpaperApplyBusy = true;

        try
        {
            string path = ResolveWallpaperPath();
            int myToken = ++_wallpaperLoadToken;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                BgVideoElement.Stop();
                BgVideoElement.Visibility = Visibility.Collapsed;
                BgVideoElement.Source = null;
                WeatherBridge.ClearTheme();
                BgImageBrush.ImageSource = null;
                ResetHomeColorsToDefault();
                return;
            }

            BgImageBrush.BeginAnimation(Brush.OpacityProperty, null);
            BgVideoElement.BeginAnimation(UIElement.OpacityProperty, null);

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (WallpaperVideoExts.Contains(ext))
            {
                BgImageBrush.ImageSource = null;
                BgVideoElement.Opacity = 1.0;
                BgVideoElement.Source = new Uri(path, UriKind.Absolute);
                BgVideoElement.Visibility = Visibility.Visible;
                BgVideoElement.Volume = 0;
                BgVideoElement.MediaEnded += (s, e) => { BgVideoElement.Position = TimeSpan.Zero; BgVideoElement.Play(); };
                BgVideoElement.MediaOpened += BgVideoElement_MediaOpened_SampleColor;
                BgVideoElement.Play();
            }
            else
            {
                BgVideoElement.Stop();
                BgVideoElement.Visibility = Visibility.Collapsed;

                if (TryGetCachedWallpaper(path, out var cachedBmp, out var cachedAvg))
                {
                    CrossfadeWallpaper(BgImageBrush.ImageSource, cachedBmp);
                    BgImageBrush.ImageSource = cachedBmp;
                    WeatherBridge.SetWallpaperSurface(RootHomeGrid);
                    WeatherBridge.SetWallpaper(cachedBmp);
                    ApplyAdaptiveColors(cachedAvg);
                }
                else
                {
                    var (bmp, avgColor) = await Task.Run(() => LoadWallpaperAndSample(path));

                    if (myToken != _wallpaperLoadToken) return;

                    CrossfadeWallpaper(BgImageBrush.ImageSource, bmp);
                    BgImageBrush.ImageSource = bmp;
                    WeatherBridge.SetWallpaperSurface(RootHomeGrid);
                    WeatherBridge.SetWallpaper(bmp);
                    ApplyAdaptiveColors(avgColor);
                }
            }

            BgImageBrush.Opacity = SettingsService.Current.BackgroundOpacity;
            PreloadUpcomingWallpapers();
        }
        finally
        {
            _wallpaperApplyBusy = false;
        }
    }

    private static void PreloadUpcomingWallpapers()
    {
        if (!_wallpaperPreloadGate.Wait(0)) return;

        Task.Run(() =>
        {
            try
            {
                string folder = WallpaperFolder;
                var s = SettingsService.Current;
                var upcoming = new List<string>();

                switch (s.WallpaperMode)
                {
                    case "Random":
                    {
                        string queueFile = Path.Combine(folder, ".wallpaper_queue.txt");
                        if (File.Exists(queueFile))
                        {
                            var lines = File.ReadAllLines(queueFile)
                                            .Where(f => File.Exists(f) && IsWallpaperFile(f))
                                            .Take(2);
                            upcoming.AddRange(lines);
                        }
                        break;
                    }
                    case "Custom":
                    {
                        var list = s.WallpaperCustomList;
                        if (list != null && list.Count > 0)
                        {
                            if (s.WallpaperOrder == "Random")
                            {
                                var randomItems = list.Where(f => File.Exists(Path.Combine(folder, f)))
                                                      .OrderBy(_ => Guid.NewGuid())
                                                      .Take(2)
                                                      .Select(f => Path.Combine(folder, f));
                                upcoming.AddRange(randomItems);
                            }
                            else
                            {
                                int idx1 = (s.WallpaperCustomIndex + 1) % list.Count;
                                int idx2 = (s.WallpaperCustomIndex + 2) % list.Count;
                                upcoming.Add(Path.Combine(folder, list[idx1]));
                                upcoming.Add(Path.Combine(folder, list[idx2]));
                            }
                        }
                        break;
                    }
                }

                foreach (var path in upcoming)
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        string ext = Path.GetExtension(path).ToLowerInvariant();
                        if (!WallpaperVideoExts.Contains(ext))
                        {
                            LoadWallpaperAndSample(path);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                _wallpaperPreloadGate.Release();
            }
        });
    }

    private static (BitmapImage bmp, Color avg) LoadWallpaperAndSample(string path)
    {
        string cacheKey = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;

        lock (_wallpaperBitmapCache)
        {
            if (_wallpaperBitmapCache.TryGetValue(cacheKey, out var cachedBmp))
            {
                Color cachedAvg;
                lock (_wallpaperColorCache)
                {
                    _wallpaperColorCache.TryGetValue(path, out cachedAvg);
                }
                return (cachedBmp, cachedAvg);
            }
        }

        var display = new BitmapImage();
        display.BeginInit();
        display.UriSource = new Uri(path, UriKind.Absolute);
        display.DecodePixelWidth = 3840;
        display.CacheOption = BitmapCacheOption.OnLoad;
        display.EndInit();
        display.Freeze();

        lock (_wallpaperBitmapCache)
        {
            if (_wallpaperBitmapCache.Count >= 8)
            {
                var oldestKey = _wallpaperBitmapCache.Keys.First();
                _wallpaperBitmapCache.Remove(oldestKey);
            }
            _wallpaperBitmapCache[cacheKey] = display;
        }

        Color avg;
        lock (_wallpaperColorCache)
        {
            if (_wallpaperColorCache.TryGetValue(path, out var cached))
            {
                avg = cached;
                return (display, avg);
            }
        }

        var sample = new BitmapImage();
        sample.BeginInit();
        sample.UriSource = new Uri(path, UriKind.Absolute);
        sample.DecodePixelWidth = 32;
        sample.CacheOption = BitmapCacheOption.OnLoad;
        sample.EndInit();
        sample.Freeze();

        avg = SampleAverageColor(sample);

        lock (_wallpaperColorCache)
        {
            if (_wallpaperColorCache.Count >= 8)
            {
                var oldestColorKey = _wallpaperColorCache.Keys.First();
                _wallpaperColorCache.Remove(oldestColorKey);
            }
            _wallpaperColorCache[path] = avg;
        }

        return (display, avg);
    }

    private void BgVideoElement_MediaOpened_SampleColor(object? sender, RoutedEventArgs e)
    {
        BgVideoElement.MediaOpened -= BgVideoElement_MediaOpened_SampleColor;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (s, args) =>
        {
            timer.Stop();
            try
            {
                int w = (int)BgVideoElement.NaturalVideoWidth;
                int h = (int)BgVideoElement.NaturalVideoHeight;
                if (w <= 0 || h <= 0) return;

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(BgVideoElement);
                rtb.Freeze();

                WeatherBridge.SetWallpaper(rtb);
                var avg = SampleAverageColor(rtb);
                ApplyAdaptiveColors(avg);
            }
            catch { }
        };
        timer.Start();
    }

    private static Color SampleAverageColor(BitmapSource bmp)
    {
        try
        {
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            if (w <= 0 || h <= 0) return Color.FromRgb(40, 40, 40);

            int stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);

            long r = 0, g = 0, b = 0;
            int count = w * h;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                b += pixels[i];
                g += pixels[i + 1];
                r += pixels[i + 2];
            }

            return Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }
        catch
        {
            return Color.FromRgb(40, 40, 40);
        }
    }

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;

        if (max == min)
        {
            h = 0; s = 0;
            return;
        }

        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h *= 60;
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        h = h % 360; if (h < 0) h += 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = l - c / 2.0;
        double r, g, b;

        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static void PublishWeatherTheme(Color avg)
    {
        RgbToHsl(avg, out double hue, out double sat, out double lum);
        bool dark = lum < 0.5;
        bool adaptive = SettingsService.Current.HomeAdaptiveColorsEnabled;

        double pillSat = Math.Min(Math.Max(sat, 0.4225), 0.6175);
        double pillLum = dark ? 0.46 : 0.68;
        Color pillHue = HslToRgb(hue, pillSat, pillLum);
        Color pill = Color.FromArgb(TintService.Apply(dark ? (byte)0x8C : (byte)0x99), pillHue.R, pillHue.G, pillHue.B);

        Color accent = adaptive
            ? HslToRgb(hue, Math.Min(Math.Max(sat, 0.35), 0.7), 0.78)
            : Color.FromRgb(0xCC, 0xCC, 0xCC);
        Color secondary = adaptive
            ? HslToRgb(hue, Math.Min(Math.Max(sat, 0.25), 0.55), 0.60)
            : Color.FromRgb(0x95, 0x95, 0x95);
        Color subtle = adaptive
            ? HslToRgb(hue, Math.Min(Math.Max(sat, 0.30), 0.55), 0.72)
            : Color.FromRgb(0xA0, 0xA0, 0xA0);

        WeatherBridge.PublishTheme(avg, pill, Colors.White, accent, secondary, subtle, dark, adaptive);
    }

    public static async Task EnsureWeatherThemeAsync()
    {
        if (WeatherBridge.HasTheme) return;
        try
        {
            string folder = WallpaperFolder;
            string path = "";
            if (!string.IsNullOrEmpty(SettingsService.Current.WallpaperFileName))
                path = Path.Combine(folder, SettingsService.Current.WallpaperFileName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                path = GetFirstWallpaperInFolder(folder);

            if (string.IsNullOrEmpty(path) || !File.Exists(path)
                || WallpaperVideoExts.Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                if (!WeatherBridge.HasTheme) WeatherBridge.ClearTheme();
                return;
            }

            var (bmp, avg) = await Task.Run(() => LoadWallpaperAndSample(path));
            if (WeatherBridge.HasTheme) return;
            WeatherBridge.SetWallpaper(bmp);
            PublishWeatherTheme(avg);
        }
        catch
        {
            if (!WeatherBridge.HasTheme) WeatherBridge.ClearTheme();
        }
    }

    private void ResetHomeColorsToDefault()
    {
        TblClock.Foreground = Brushes.White;
        TblClock.Effect = MakeOutline(Colors.White);

        var weatherColor = Color.FromRgb(0xCC, 0xCC, 0xCC);
        TblWeather.Foreground = new SolidColorBrush(weatherColor);
        TblWeather.Effect = MakeOutline(weatherColor);

        var citySubColor = Color.FromRgb(0xA0, 0xA0, 0xA0);
        TblWeatherCity.Foreground = new SolidColorBrush(citySubColor);
        TblWeatherCity.Effect = MakeOutline(citySubColor);

        var dateColor = Color.FromRgb(0xC5, 0xC5, 0xC5);
        TblCalendarDate.Foreground = new SolidColorBrush(dateColor);
        TblCalendarDate.Effect = MakeOutline(dateColor);

        var prevColor = Color.FromRgb(0x95, 0x95, 0x95);
        TblCalendarPrev.Foreground = new SolidColorBrush(prevColor);
        TblCalendarPrev.Effect = MakeOutline(prevColor);

        var nextColor = Color.FromRgb(0x8f, 0xd6, 0xa0);
        TblCalendarNext.Foreground = new SolidColorBrush(nextColor);
        TblCalendarNext.Effect = MakeOutline(nextColor);

        var mediaColor = Color.FromRgb(0xEE, 0xEE, 0xEE);
        TblMediaTitle.Foreground = new SolidColorBrush(mediaColor);
        TblMediaTitle.Effect = MakeOutline(mediaColor);

        SetHomeTextColor(Colors.White);
        TxtHomeSearch.CaretBrush = Brushes.White;
        TxtHomeSearch.Effect = MakeOutline(Colors.White);

        var searchBgBrush = CreateSearchBarBackgroundBrush(Color.FromArgb(0xD9, 0x1A, 0x1A, 0x1A));
        var searchBorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        
        SearchBoxBorder.Background = searchBgBrush;
        SearchBoxBorder.BorderBrush = searchBorderBrush;
        PnlBatteryPill.Background = searchBgBrush;
        PnlBatteryPill.BorderBrush = searchBorderBrush;
        
        Resources["SharedSearchBgBrush"] = searchBgBrush;
        Resources["SharedSearchBorderBrush"] = searchBorderBrush;

        Color defaultPillBg = Color.FromArgb(0x40, 0x00, 0x00, 0x00);
        Brush defaultPillTextBrush = GetContrastingTextBrush(defaultPillBg);
        CmbSearchEngine.Background = new SolidColorBrush(defaultPillBg);
        CmbSearchEngine.BorderBrush = CreateFadingBorderBrush(((SolidColorBrush)defaultPillTextBrush).Color);
        CmbSearchEngine.Foreground = defaultPillTextBrush;

        ApplyThemedButton(BtnToggleFavorites, defaultPillBg);
        ApplyThemedButton(BtnToggleBookmarks, defaultPillBg);
        ApplyThemedButton(BtnMediaPrev, defaultPillBg);
        ApplyThemedButton(BtnMediaPlayPause, defaultPillBg);
        ApplyThemedButton(BtnMediaNext, defaultPillBg);
        ApplyThemedButton(BtnMediaMute, defaultPillBg);
        ApplyThemedButton(BtnMediaAudioOnly, defaultPillBg);
        Resources["HomeAccentBrush"] = new SolidColorBrush(defaultPillBg);
        Resources["HomeTileFadeBrush"] = CreateTileFadeBrush(defaultPillBg);
        _lastPillBg = defaultPillBg;

        ApplySettingsButtonColor(defaultPillBg);
    }

    private void SetHomeTextColor(Color color)
    {
        TxtHomeSearch.Foreground = new SolidColorBrush(color);
        Resources["HomeTextBrush"] = new SolidColorBrush(color);
    }

    private void ApplySettingsButtonColor(Color bg)
    {
        Brush text = GetContrastingTextBrush(bg);
        BtnHomeSettingsBg.Background = new SolidColorBrush(bg);
        BtnHomeSettingsBg.BorderBrush = CreateFadingBorderBrush(((SolidColorBrush)text).Color);
        BtnHomeSettingsBg.BorderThickness = new Thickness(1);
        BtnHomeSettingsIcon.Foreground = text;
    }

    // Picks pure white or pure black against a given background color, based on that
    // color's own luminance — independent of wallpaper brightness, so the pill's text
    // always contrasts with the pill itself rather than with the wallpaper.
    private static Brush GetContrastingTextBrush(Color bg)
    {
        RgbToHsl(bg, out _, out _, out double lum);
        return lum > 0.55 ? Brushes.Black : Brushes.White;
    }

    public static Brush CreateSearchBarBackgroundBrush(Color mainColor)
    {
        double luminance = (0.299 * mainColor.R + 0.587 * mainColor.G + 0.114 * mainColor.B) / 255.0;
        int shift = luminance > 0.5 ? -34 : 34;
        Color secondary = Color.FromArgb(
            mainColor.A,
            (byte)Math.Clamp(mainColor.R + shift, 0, 255),
            (byte)Math.Clamp(mainColor.G + shift, 0, 255),
            (byte)Math.Clamp(mainColor.B + shift, 0, 255));

        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(mainColor, 0.0),
                new GradientStop(mainColor, 0.68),
                new GradientStop(secondary, 1.0),
            }
        };
    }

    private static Brush CreateFadingBorderBrush(Color c)
    {
        return new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
    }

    private static Brush CreateTileFadeBrush(Color accent)
    {
        return new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.75,
            RadiusY = 0.75,
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x99, 0x00, 0x00, 0x00), 0.0),
                new GradientStop(Color.FromArgb(0x40, accent.R, accent.G, accent.B), 0.55),
                new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0),
            }
        };
    }

    // Shared themed-pill application: same accent color, contrast text, fading border —
    // used for every button on the homepage so they all re-tint together.
    private static void ApplyThemedButton(Button b, Color bg)
    {
        Brush text = GetContrastingTextBrush(bg);
        b.Background = new SolidColorBrush(bg);
        b.Foreground = text;
        b.BorderBrush = CreateFadingBorderBrush(((SolidColorBrush)text).Color);
        b.BorderThickness = new Thickness(2);
    }

    public void RefreshAdaptiveTint()
    {
        if (_lastAdaptiveAvgColor.HasValue)
            ApplyAdaptiveColors(_lastAdaptiveAvgColor.Value);
    }

    private void ApplyAdaptiveColors(Color avg)
    {
        _lastAdaptiveAvgColor = avg;
        PublishWeatherTheme(avg);
        RgbToHsl(avg, out double hue, out double sat, out double lum);
        if (IsVisible) PublishWeatherTheme(avg);
        bool darkWallpaperForContrast = lum < 0.5;
        Color searchTextColor = darkWallpaperForContrast ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);
        Color searchPlaceholderColor = darkWallpaperForContrast ? Color.FromRgb(0xBB, 0xBB, 0xBB) : Color.FromRgb(0x55, 0x55, 0x55);
        _lastSearchTextColor = searchTextColor;

        SetHomeTextColor(searchTextColor);
        TxtHomeSearch.CaretBrush = new SolidColorBrush(searchTextColor);
        TxtHomeSearch.Effect = MakeOutline(searchTextColor);
        TxtHomeSearchPlaceholder.Foreground = new SolidColorBrush(searchPlaceholderColor);

        // Colorful pill: hue/saturation taken from the wallpaper's dominant color, lightness
        // tuned for a dark or light wallpaper so the pill still sits legibly on the search bar.
        double pillSat = Math.Min(Math.Max(sat, 0.4225), 0.6175);
        double pillLum = darkWallpaperForContrast ? 0.46 : 0.68;
        Color pillHueColor = HslToRgb(hue, pillSat, pillLum);
        byte pillAlpha = TintService.Apply(darkWallpaperForContrast ? (byte)0x8C : (byte)0x99);
        Color pillBg = Color.FromArgb(pillAlpha, pillHueColor.R, pillHueColor.G, pillHueColor.B);
        Brush pillTextBrush = GetContrastingTextBrush(pillBg);
        CmbSearchEngine.Background = new SolidColorBrush(pillBg);
        CmbSearchEngine.BorderBrush = CreateFadingBorderBrush(((SolidColorBrush)pillTextBrush).Color);
        CmbSearchEngine.Foreground = pillTextBrush;

        ApplyThemedButton(BtnToggleFavorites, pillBg);
        ApplyThemedButton(BtnToggleBookmarks, pillBg);
        ApplyThemedButton(BtnMediaPrev, pillBg);
        ApplyThemedButton(BtnMediaPlayPause, pillBg);
        ApplyThemedButton(BtnMediaNext, pillBg);
        ApplyThemedButton(BtnMediaMute, pillBg);
        ApplyThemedButton(BtnMediaAudioOnly, pillBg);
        Resources["HomeAccentBrush"] = new SolidColorBrush(pillBg);
        Resources["HomeTileFadeBrush"] = CreateTileFadeBrush(pillBg);
        _lastPillBg = pillBg;

        if (!SettingsService.Current.HomeAdaptiveColorsEnabled)
        {
            ResetHomeColorsToDefault();
            return;
        }

        bool darkWallpaper = darkWallpaperForContrast;

        Color baseColor = darkWallpaper ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);
        Color accentPrimary = HslToRgb(hue, Math.Min(Math.Max(sat, 0.35), 0.7), darkWallpaper ? 0.78 : 0.30);
        Color accentSecondary = HslToRgb(hue, Math.Min(Math.Max(sat, 0.25), 0.55), darkWallpaper ? 0.60 : 0.42);
        Color accentSubtle = HslToRgb(hue, Math.Min(Math.Max(sat, 0.30), 0.55), darkWallpaper ? 0.72 : 0.35);

        TblClock.Foreground = new SolidColorBrush(baseColor);
        TblClock.Effect = MakeOutline(baseColor);

        TblWeather.Foreground = new SolidColorBrush(accentPrimary);
        TblWeather.Effect = MakeOutline(accentPrimary);

        TblWeatherCity.Foreground = new SolidColorBrush(accentSubtle);
        TblWeatherCity.Effect = MakeOutline(accentSubtle);

        TblCalendarDate.Foreground = new SolidColorBrush(accentSubtle);
        TblCalendarDate.Effect = MakeOutline(accentSubtle);

        TblCalendarPrev.Foreground = new SolidColorBrush(accentSubtle);
        TblCalendarPrev.Effect = MakeOutline(accentSubtle);

        TblCalendarNext.Foreground = new SolidColorBrush(accentSecondary);
        TblCalendarNext.Effect = MakeOutline(accentSecondary);

        TblMediaTitle.Foreground = new SolidColorBrush(baseColor);
        TblMediaTitle.Effect = MakeOutline(baseColor);

        byte bgAlpha = TintService.Apply(0xD9);
        Color searchBg = darkWallpaper ? Color.FromArgb(bgAlpha, 0x1A, 0x1A, 0x1A) : Color.FromArgb(bgAlpha, 0xFF, 0xFF, 0xFF);
        
        var searchBgBrush = CreateSearchBarBackgroundBrush(searchBg);
        var searchBorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accentPrimary.R, accentPrimary.G, accentPrimary.B));
        
        SearchBoxBorder.Background = searchBgBrush;
        SearchBoxBorder.BorderBrush = searchBorderBrush;
        PnlBatteryPill.Background = searchBgBrush;
        PnlBatteryPill.BorderBrush = searchBorderBrush;
        
        Resources["SharedSearchBgBrush"] = searchBgBrush;
        Resources["SharedSearchBorderBrush"] = searchBorderBrush;

        ApplySettingsButtonColor(pillBg);
    }

    private void RefreshSearchEngineList()
    {
        var engines = new List<SearchEngineEntry>(SettingsService.BuiltInSearchEngines);
        var currentUrl = SettingsService.Current.SearchEngineUrl;
        var match = engines.FirstOrDefault(x => x.Url == currentUrl);

        if (match == null && !string.IsNullOrWhiteSpace(currentUrl))
        {
            // User set a custom URL in SettingsWindow — surface it here too instead of
            // silently snapping back to a built-in engine.
            match = new SearchEngineEntry { Name = "Custom…", Url = currentUrl, BuiltIn = false };
            engines.Add(match);
        }

        CmbSearchEngine.SelectionChanged -= CmbSearchEngine_SelectionChanged;
        CmbSearchEngine.ItemsSource = engines;
        CmbSearchEngine.SelectedItem = match ?? engines.FirstOrDefault();
        CmbSearchEngine.SelectionChanged += CmbSearchEngine_SelectionChanged;
    }

    private void CmbSearchEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbSearchEngine.SelectedItem is SearchEngineEntry engine)
        {
            SettingsService.Current.SearchEngine = engine.Name;
            SettingsService.Current.SearchEngineUrl = engine.Url;
            SettingsService.Save();
        }
    }

    private void HomePageView_Loaded_RefreshOnReturn(object sender, RoutedEventArgs e) => RefreshSearchEngineList();

    private void HomePageView_Unloaded(object sender, RoutedEventArgs e)
    {
        _clockTimer?.Stop();
        _clockTimer = null;
        _inactivityTimer?.Stop();
        _inactivityTimer = null;
        MediaBridge.GradientUpdated -= DrawHomeVisualizer;
        WeatherBridge.Updated -= RefreshWeather;
        MediaBridge.Updated -= RefreshMedia;
        CalendarBridge.Updated -= RefreshCalendar;
        DownloadsBridge.Updated -= RefreshDownloads;
        BookmarkService.OnUpdated -= RefreshBookmarks;
    }

    private DispatcherTimer? _battPillFlashTimer;
    private bool             _battPillFlashOn = false;
    private const int        BattPillRedThreshold = 20;

    private static Color BattPillColorFor(int percent)
    {
        Color Lerp(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }
        var green  = Color.FromRgb(0x34, 0xd3, 0x99);
        var yellow = Color.FromRgb(0xf5, 0xc9, 0x4c);
        var red    = Color.FromRgb(0xf0, 0x5a, 0x5a);
        if (percent >= 50) return Lerp(yellow, green, (percent - 50) / 50.0);
        return Lerp(red, yellow, percent / 50.0);
    }

    private void RefreshBattery()
    {
        Dispatcher.Invoke(() =>
        {
            bool show = SettingsService.Current.HomeShowBatteryWidget && BatteryBridge.HasBattery;
            PnlBatteryPill.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show)
            {
                _battPillFlashTimer?.Stop();
                _battPillFlashTimer = null;
                return;
            }

            int percent = Math.Clamp(BatteryBridge.Percent, 0, 100);
            string icon = BatteryBridge.IsCharging ? "⚡" : "🔋";
            TxtBatteryPill.Text = $"{icon} {percent}%";

            double containerW = (PnlBatteryPill.ActualWidth > 0 ? PnlBatteryPill.ActualWidth : 76) - 2;
            var color = BattPillColorFor(percent);
            BatteryPillFill.Width      = containerW * (percent / 100.0);
            BatteryPillFill.Background = new SolidColorBrush(color);

            if (percent <= BattPillRedThreshold && !BatteryBridge.IsCharging)
            {
                if (_battPillFlashTimer == null)
                {
                    _battPillFlashOn = false;
                    _battPillFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    _battPillFlashTimer.Tick += (s, e) =>
                    {
                        _battPillFlashOn = !_battPillFlashOn;
                        BatteryPillFill.Opacity = _battPillFlashOn ? 0.35 : 1.0;
                    };
                    _battPillFlashTimer.Start();
                }
            }
            else
            {
                _battPillFlashTimer?.Stop();
                _battPillFlashTimer = null;
                BatteryPillFill.Opacity = 1.0;
            }
        });
    }

    private void PnlBatteryPill_Click(object sender, MouseButtonEventArgs e)
    {
        BatteryBridge.Refresh();

        var mainWin = Window.GetWindow(this) as MainWindow;
        if (mainWin == null) return;

        try
        {
            var method = typeof(MainWindow).GetMethod("OpenBatteryMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(mainWin, null);
        }
        catch { }
    }

    private static string FormatBattTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";

    private void RefreshDownloads()
    {
        Dispatcher.Invoke(() =>
        {
            var active = DownloadsBridge.ActiveDownloads;
            PnlDownloads.Visibility = (SettingsService.Current.HomeShowDownloadsWidget && active.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
            IcnDownloads.ItemsSource = null;
            IcnDownloads.ItemsSource = active;
        });
    }

    private DispatcherTimer? _vpnAdBlockTimer;
    private long _vpnAdBlockLastBytes;
    private DateTime _vpnAdBlockLastSampleUtc;

    private void InitVpnAdBlockWidget()
    {
        VpnRelayService.StateChanged += (state, msg) => Dispatcher.BeginInvoke(RefreshVpnAdBlockWidget);

        _vpnAdBlockLastBytes = VpnRelayService.TotalBytes;
        _vpnAdBlockLastSampleUtc = DateTime.UtcNow;

        _vpnAdBlockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _vpnAdBlockTimer.Tick += (s, e) => RefreshVpnAdBlockWidget();
        _vpnAdBlockTimer.Start();

        RefreshVpnAdBlockWidget();
    }

    private void RefreshVpnAdBlockWidget()
    {
        var show = SettingsService.Current.HomeShowVpnAdBlockWidget;
        PnlVpnAdBlock.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var state = VpnRelayService.State;
        var connected = state == VpnRelayState.Connected;

        TblVpnAdBlockStatus.Text = state switch
        {
            VpnRelayState.Connected  => "VPN connected",
            VpnRelayState.Connecting => "VPN connecting...",
            VpnRelayState.Fallback   => "VPN fallback (direct)",
            VpnRelayState.Blocked    => "VPN blocked",
            _                        => "VPN off"
        };

        var country = VpnRelayService.ActiveProfile?.Country;
        TblVpnAdBlockCountry.Text = string.IsNullOrEmpty(country) ? "" : $"Server country: {country}";
        TblVpnAdBlockCountry.Visibility = (connected && !string.IsNullOrEmpty(country)) ? Visibility.Visible : Visibility.Collapsed;

        var now = DateTime.UtcNow;
        var nowBytes = VpnRelayService.TotalBytes;
        var elapsed = (now - _vpnAdBlockLastSampleUtc).TotalSeconds;

        if (connected && elapsed > 0.5)
        {
            var deltaBytes = nowBytes - _vpnAdBlockLastBytes;
            var bps = deltaBytes / elapsed;
            TblVpnAdBlockSpeed.Text = $"{FormatSpeed(bps)}";
            TblVpnAdBlockSpeed.Visibility = Visibility.Visible;
        }
        else
        {
            TblVpnAdBlockSpeed.Visibility = Visibility.Collapsed;
        }

        _vpnAdBlockLastBytes = nowBytes;
        _vpnAdBlockLastSampleUtc = now;
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        var bits = bytesPerSecond * 8.0;
        if (bits >= 1_000_000) return $"{bits / 1_000_000:0.0} Mbps";
        if (bits >= 1_000) return $"{bits / 1_000:0} Kbps";
        return $"{bits:0} bps";
    }

    private void RefreshBookmarks()
    {
        Dispatcher.Invoke(() =>
        {
            var all = BookmarkService.Items.AsEnumerable();
            if (_inFocusMode && _focusWidgetName == "Bookmarks")
            {
                string filterText = (TxtHomeSearch.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(filterText))
                {
                    all = all.Where(b => b.Title.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                                         b.Url.Contains(filterText, StringComparison.OrdinalIgnoreCase));
                }
                string selectedCat = CmbCategoryFilter.SelectedItem as string ?? "All Categories";
                if (selectedCat == "Recent")
                {
                    all = all.OrderByDescending(b => b.DateAdded);
                }
                else if (selectedCat == "Older")
                {
                    all = all.OrderBy(b => b.DateAdded);
                }
            }

            var list = all.ToList();
            bool hasEnoughItems = BookmarkService.Items.Count > 6;
            ApplyScrollCap();
            if ((hasEnoughItems && !FullStyle) || (_inFocusMode && _focusWidgetName == "Bookmarks"))
            {
                BtnToggleBookmarks.Visibility = Visibility.Visible;
                TxtToggleBookmarks.Text = _bookmarksExpanded ? "▲ Show less" : "▼ Show more";
            }
            else
            {
                BtnToggleBookmarks.Visibility = Visibility.Collapsed;
            }

            var displayList = list;
            string groupingMode = SettingsService.Current.HomeBookmarkGrouping;
            bool oldestFirst = _inFocusMode && _focusWidgetName == "Bookmarks" && (CmbCategoryFilter.SelectedItem as string) == "Older";
            List<BookmarkItem> ordered;
            if (groupingMode == "Single")
            {
                foreach (var b in displayList) b.GroupKey = "All bookmarks";
                ordered = (oldestFirst
                    ? displayList.OrderBy(b => b.DateAdded)
                    : displayList.OrderByDescending(b => b.DateAdded)).ToList();
            }
            else if (groupingMode == "RecentOlder")
            {
                ordered = BuildRecentOlder(displayList, oldestFirst);
            }
            else
            {
                var domainCounts = BookmarkService.Items
                    .GroupBy(b => DomainOf(b.Url))
                    .ToDictionary(g => g.Key, g => g.Count());
                foreach (var b in displayList)
                {
                    string domain = DomainOf(b.Url);
                    b.GroupKey = domainCounts.TryGetValue(domain, out int domainCount) && domainCount > 1 ? domain : "Other";
                }

                ordered = displayList
                    .GroupBy(b => b.GroupKey)
                    .OrderBy(g => g.Key == "Other" ? 1 : 0)
                    .ThenByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(g => g)
                    .ToList();
            }

            string bookmarksSig = groupingMode + "\u0003" + SettingsService.Current.HomeGroupStyle + "\u0003" + string.Join("\u0001", ordered.Select(b => b.GroupKey + "\u0002" + b.Name + "\u0002" + b.Url + "\u0002" + b.IconPath));
            if (bookmarksSig == _bookmarksSig && IcnBookmarks.ItemsSource != null) return;
            _bookmarksSig = bookmarksSig;

            GroupState.SetIsFolder(IcnBookmarks, FolderStyle);
            var bookmarksView = new CollectionViewSource { Source = ordered }.View;
            bookmarksView.GroupDescriptions.Clear();
            bookmarksView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BookmarkItem.GroupKey)));
            IcnBookmarks.ItemsSource = bookmarksView;
        });
    }

    private static List<BookmarkItem> BuildRecentOlder(List<BookmarkItem> items, bool oldestFirst)
    {
        var allDates = BookmarkService.Items.Select(b => b.DateAdded).ToList();
        if (allDates.Count == 0) return items;
        DateTime newest = allDates.Max();
        DateTime oldest = allDates.Min();
        DateTime cutoff = oldest + TimeSpan.FromTicks((newest - oldest).Ticks / 2);
        string newerTitle = NewerGroupTitle(newest - cutoff);
        foreach (var b in items) b.GroupKey = b.DateAdded >= cutoff ? newerTitle : "Older";
        var newer = items.Where(b => b.DateAdded >= cutoff);
        var older = items.Where(b => b.DateAdded < cutoff);
        var result = oldestFirst
            ? newer.OrderBy(b => b.DateAdded).Concat(older.OrderBy(b => b.DateAdded))
            : newer.OrderByDescending(b => b.DateAdded).Concat(older.OrderByDescending(b => b.DateAdded));
        return result.ToList();
    }

    private static string NewerGroupTitle(TimeSpan span)
    {
        double days = Math.Max(span.TotalDays, 0);
        double value;
        string unit;
        if (days < 14) { value = days; unit = "day"; }
        else if (days <= 56) { value = days / 7; unit = "week"; }
        else if (days <= 24 * 30.4375) { value = days / 30.4375; unit = "month"; }
        else if (days <= 20 * 365.25) { value = days / 365.25; unit = "year"; }
        else if (days <= 100 * 365.25) { value = days / 3652.5; unit = "decade"; }
        else { value = days / 36525; unit = "millennium"; }
        int n = Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
        if (n == 1) return "Newer (last " + unit + ")";
        string plural = unit == "millennium" ? "millennia" : unit + "s";
        return "Newer (last " + n + " " + plural + ")";
    }

    private static string GroupKeyOf(GroupItem group)
    {
        return (group.Content as CollectionViewGroup)?.Name?.ToString() ?? "";
    }

    private ItemsControl? OwnerItemsControl(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is ItemsControl ic && (ic == IcnColumns || ic == IcnBookmarks)) return ic;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private void GroupItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not GroupItem group) return;
        var owner = OwnerItemsControl(group);
        if (owner == null) return;
        GroupState.SetIsFolder(group, FolderStyle);
        if (!FullStyle && IsGroupCollapsed(owner, GroupKeyOf(group)))
            ApplyGroupVisual(group, true);
    }

    private bool IsGroupCollapsed(ItemsControl owner, string key)
    {
        if (FolderStyle) return !(owner == IcnColumns ? _openFavoriteGroups : _openBookmarkGroups).Contains(key);
        return (owner == IcnColumns ? _collapsedFavoriteGroups : _collapsedBookmarkGroups).Contains(key);
    }

    private void SetGroupCollapsed(ItemsControl owner, string key, bool collapse)
    {
        if (FolderStyle)
        {
            var open = owner == IcnColumns ? _openFavoriteGroups : _openBookmarkGroups;
            if (collapse) open.Remove(key); else open.Add(key);
            return;
        }
        var collapsedSet = owner == IcnColumns ? _collapsedFavoriteGroups : _collapsedBookmarkGroups;
        if (collapse) collapsedSet.Add(key); else collapsedSet.Remove(key);
    }

    private static DoubleAnimation GroupHeightAnimation(double to)
    {
        return new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(300)))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
    }

    private static bool FullStyle => SettingsService.Current.HomeGroupStyle == "Full";

    private static bool FolderStyle => SettingsService.Current.HomeGroupStyle == "Folder";

    private void ApplyScrollCap()
    {
        if (_inFocusMode || ScvFavorites == null || ScvBookmarks == null) return;
        if (FolderStyle)
        {
            double budget = Math.Max(160, ComputeExpandedScrollMaxHeight() / 2);
            PuzzlePanel.SetViewportBudget(ScvFavorites, budget);
            PuzzlePanel.SetViewportBudget(ScvBookmarks, budget);
            PuzzlePanel.Resnap(ScvFavorites);
            PuzzlePanel.Resnap(ScvBookmarks);
            return;
        }
        if (FullStyle)
        {
            double cap = Math.Max(160, ComputeExpandedScrollMaxHeight() / 2);
            ScvFavorites.MaxHeight = cap;
            ScvBookmarks.MaxHeight = cap;
        }
        else
        {
            ScvFavorites.MaxHeight = 320;
            ScvBookmarks.MaxHeight = 220;
        }
    }

    private static GroupCollapseMode? CollapseModeFromSetting()
    {
        return SettingsService.Current.HomeGroupStyle switch
        {
            "FirstRow" => GroupCollapseMode.FirstRow,
            "Stack" => GroupCollapseMode.Stack,
            "Mini" => GroupCollapseMode.Mini,
            "Folder" => GroupCollapseMode.Folder,
            _ => null
        };
    }

    private static AutoGridPanel? FindAutoGrid(DependencyObject node)
    {
        if (node is AutoGridPanel panel) return panel;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var found = FindAutoGrid(VisualTreeHelper.GetChild(node, i));
            if (found != null) return found;
        }
        return null;
    }

    private void ApplyGroupVisual(GroupItem group, bool collapsed)
    {
        GroupState.SetIsCollapsed(group, collapsed);
        if (group.Template?.FindName("GroupItemsClip", group) is not Border clip) return;
        var mode = collapsed ? CollapseModeFromSetting() : null;
        var panel = FindAutoGrid(clip);
        if (panel != null) panel.Mode = mode ?? GroupCollapseMode.Full;
        clip.Visibility = collapsed && mode == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AnimateGroup(GroupItem group, bool collapse)
    {
        if (group.Template?.FindName("GroupItemsClip", group) is not Border clip || clip.Child is not FrameworkElement content)
        {
            ApplyGroupVisual(group, collapse);
            return;
        }

        void Finish()
        {
            clip.BeginAnimation(FrameworkElement.HeightProperty, null);
            clip.ClearValue(FrameworkElement.HeightProperty);
            _animatingGroups.Remove(group);
        }

        var panel = FindAutoGrid(content);

        if (collapse)
        {
            var mode = CollapseModeFromSetting();
            double from = clip.ActualHeight;
            double to = mode != null && panel != null ? panel.HeightFor(mode.Value) : 0;
            if (from < 1 || to >= from - 1)
            {
                ApplyGroupVisual(group, true);
                return;
            }
            _animatingGroups.Add(group);
            clip.Height = from;
            var anim = GroupHeightAnimation(to);
            anim.Completed += (_, _) =>
            {
                ApplyGroupVisual(group, true);
                Finish();
            };
            clip.BeginAnimation(FrameworkElement.HeightProperty, anim);
        }
        else
        {
            _animatingGroups.Add(group);
            double from = clip.Visibility == Visibility.Visible ? clip.ActualHeight : 0;
            clip.Height = from;
            ApplyGroupVisual(group, false);
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double target = content.DesiredSize.Height;
            if (target <= from + 1)
            {
                Finish();
                return;
            }
            var anim = GroupHeightAnimation(target);
            anim.Completed += (_, _) => Finish();
            clip.BeginAnimation(FrameworkElement.HeightProperty, anim);
        }
    }

    private void GroupHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (_inEditMode || FullStyle || sender is not DependencyObject header) return;
        e.Handled = ToggleGroupFrom(header);
    }

    private void FolderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (_inEditMode || !FolderStyle || !ReferenceEquals(e.OriginalSource, sender) || sender is not DependencyObject card) return;
        e.Handled = ToggleGroupFrom(card);
    }

    private bool ToggleGroupFrom(DependencyObject origin)
    {
        DependencyObject? node = origin;
        GroupItem? group = null;
        while (node != null && group == null)
        {
            group = node as GroupItem;
            node = VisualTreeHelper.GetParent(node);
        }
        if (group == null) return false;
        var owner = OwnerItemsControl(group);
        if (owner == null) return false;
        if (_animatingGroups.Contains(group)) return true;
        string key = GroupKeyOf(group);
        bool collapse = !IsGroupCollapsed(owner, key);
        SetGroupCollapsed(owner, key, collapse);
        AnimateGroup(group, collapse);
        return true;
    }

    private static string DomainOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return "Other";
        string host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.") ? host.Substring(4) : host;
    }

    private void RefreshFavorites()
    {
        Dispatcher.Invoke(() =>
        {
            var all = SettingsService.Current.PinnedUrls.AsEnumerable();
            if (_inFocusMode && _focusWidgetName == "Favorites")
            {
                string filterText = (TxtHomeSearch.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(filterText))
                {
                    all = all.Where(p => p.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                                         p.Url.Contains(filterText, StringComparison.OrdinalIgnoreCase));
                }
                string selectedCat = CmbCategoryFilter.SelectedItem as string ?? "All Categories";
                if (selectedCat != "All Categories")
                {
                    all = all.Where(p => (string.IsNullOrWhiteSpace(p.Category) ? "General" : p.Category) == selectedCat);
                }
            }

            var list = all.ToList();
            bool hasEnoughItems = SettingsService.Current.PinnedUrls.Count > 6;
            ApplyScrollCap();
            if ((hasEnoughItems && !FullStyle) || (_inFocusMode && _focusWidgetName == "Favorites"))
            {
                BtnToggleFavorites.Visibility = Visibility.Visible;
                TxtToggleFavorites.Text = _favoritesExpanded ? "▲ Show less" : "▼ Show more";
            }
            else
            {
                BtnToggleFavorites.Visibility = Visibility.Collapsed;
            }

            var displayList = list;
            string favoritesSig = SettingsService.Current.HomeGroupStyle + "\u0003" + string.Join("\u0001", displayList.Select(p => p.Name + "\u0002" + p.Url + "\u0002" + p.Category + "\u0002" + p.IconPath + "\u0002" + p.IconEmoji));
            if (favoritesSig == _favoritesSig && IcnColumns.ItemsSource != null) return;
            _favoritesSig = favoritesSig;
            GroupState.SetIsFolder(IcnColumns, FolderStyle);
            var view = new CollectionViewSource { Source = displayList }.View;
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(PinItem.Category), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription(nameof(PinItem.Name), ListSortDirection.Ascending));
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PinItem.Category)));
            IcnColumns.ItemsSource = view;
        });
    }

    private void RefreshMedia()
    {
        Dispatcher.Invoke(() =>
        {
            PnlMedia.Visibility = (SettingsService.Current.HomeShowMediaWidget && MediaBridge.HasMedia) ? Visibility.Visible : Visibility.Collapsed;
            TblMediaTitle.Text = string.IsNullOrEmpty(MediaBridge.Title) ? "Nothing playing" : MediaBridge.Title;
            TblMediaArtist.Text = MediaBridge.Artist;
            TblMediaArtist.Visibility = string.IsNullOrWhiteSpace(MediaBridge.Artist) ? Visibility.Collapsed : Visibility.Visible;
            BtnMediaPlayPause.Content = MediaBridge.IsPlaying ? "⏸" : "▶";
            BtnMediaMute.Content = MediaBridge.IsMuted ? "🔇" : "🔊";
            BtnMediaAudioOnly.Visibility = MediaBridge.HasVideo ? Visibility.Visible : Visibility.Collapsed;
            BtnMediaAudioOnly.Content = MediaBridge.IsAudioOnly ? "🖼" : "🎧";
            BtnMediaAudioOnly.ToolTip = MediaBridge.IsAudioOnly ? "Show video" : "Audio only";

            HomeVizCanvas.Visibility = HomeVisualizerAllowed() ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private static bool HomeVisualizerAllowed()
    {
        if (!SettingsService.Current.HomeVisualizerEnabled) return false;
        if (!MediaBridge.IsPlaying) return false;

        switch (SettingsService.Current.HomeVisualizerScope)
        {
            case "MediaTabOnly":
                return MediaBridge.IsAudioOnly;
            case "SelectedWebsites":
                return SettingsService.Current.HomeVisualizerWebsites.Any(host =>
                    !string.IsNullOrWhiteSpace(host) &&
                    MediaBridge.SourceHost.IndexOf(host, StringComparison.OrdinalIgnoreCase) >= 0);
            default:
                return true; // "Everywhere"
        }
    }

    private RadialGradientBrush MakeVizBrush(Point corner)
    {
        var b = new RadialGradientBrush { Center = corner, GradientOrigin = corner, RadiusX = 1.0, RadiusY = 1.0 };
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 0.5));
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 0.75));
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        return b;
    }

    private void InitVizBrushes()
    {
        if (_vizBrushTL != null) return;
        _vizBrushTL = MakeVizBrush(new Point(0, 0));
        _vizBrushBL = MakeVizBrush(new Point(0, 1));
        _vizBrushTR = MakeVizBrush(new Point(1, 0));
        _vizBrushBR = MakeVizBrush(new Point(1, 1));
        VizCornerTL.Fill = _vizBrushTL;
        VizCornerBL.Fill = _vizBrushBL;
        VizCornerTR.Fill = _vizBrushTR;
        VizCornerBR.Fill = _vizBrushBR;
        VizCornerTL.RenderTransform = new ScaleTransform(1.0, 1.0);
        VizCornerTR.RenderTransform = new ScaleTransform(1.0, 1.0);
        VizCornerBL.RenderTransform = new ScaleTransform(1.0, 1.0);
        VizCornerBR.RenderTransform = new ScaleTransform(1.0, 1.0);
        HomeVizCanvas.SizeChanged += (_, _) => SizeVizArcs();
        HomeVizCanvas.IsVisibleChanged += (_, e) => SetVizPulse((bool)e.NewValue);
        SizeVizArcs();
    }

    private void SizeVizArcs()
    {
        double w = HomeVizCanvas.ActualWidth;
        double h = HomeVizCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double s = Math.Min(420.0, Math.Min(w, h) * 0.35);
        foreach (var r in new[] { VizCornerTL, VizCornerTR, VizCornerBL, VizCornerBR })
        {
            r.Width = s;
            r.Height = s;
        }
        LogService.Debug("viz", () => $"Arcs sized size={s:F0} canvas={w:F0}x{h:F0}");
    }

    private void SetVizPulse(bool on)
    {
        if (on == _vizPulseRunning) return;
        _vizPulseRunning = on;

        var corners = new (FrameworkElement El, double Seconds, double Phase)[]
        {
            (VizCornerTL, 2.4, 0.0),
            (VizCornerTR, 3.0, 0.8),
            (VizCornerBL, 3.6, 1.6),
            (VizCornerBR, 2.8, 2.2)
        };

        foreach (var c in corners)
        {
            if (c.El.RenderTransform is not ScaleTransform st) continue;

            if (!on)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                c.El.BeginAnimation(UIElement.OpacityProperty, null);
                continue;
            }

            var duration = new Duration(TimeSpan.FromSeconds(c.Seconds));
            var begin = TimeSpan.FromSeconds(-c.Phase);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            DoubleAnimation Breathe(double from, double to) => new DoubleAnimation(from, to, duration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
                BeginTime = begin
            };

            st.BeginAnimation(ScaleTransform.ScaleXProperty, Breathe(0.92, 1.06));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, Breathe(0.92, 1.06));
            c.El.BeginAnimation(UIElement.OpacityProperty, Breathe(0.75, 1.0));
        }

        LogService.Debug("viz", () => $"Pulse {(on ? "started" : "stopped")}");
    }

    private void DrawHomeVisualizer()
    {
        if (!HomeVisualizerAllowed())
        {
            HomeVizCanvas.Visibility = Visibility.Collapsed;
            _homeVizFade = 0.0;
            return;
        }

        HomeVizCanvas.Visibility = Visibility.Visible;
        _homeVizFade = Math.Min(1.0, _homeVizFade + 0.04);

        Color c0 = MediaBridge.GradientC0;
        Color c1 = MediaBridge.GradientC1;
        Color c2 = MediaBridge.GradientC2;
        double mid = Math.Max(0.15, Math.Min(0.85, MediaBridge.GradientMidOffset));
        byte a0 = (byte)(255 * _homeVizFade);
        byte a1 = (byte)(150 * _homeVizFade);

        UpdateVizBrush(_vizBrushTL, c0, c1, c2, mid, a0, a1);
        UpdateVizBrush(_vizBrushBL, c0, c1, c2, mid, a0, a1);
        UpdateVizBrush(_vizBrushTR, c0, c1, c2, mid, a0, a1);
        UpdateVizBrush(_vizBrushBR, c0, c1, c2, mid, a0, a1);
    }

    private static void UpdateVizBrush(RadialGradientBrush? brush, Color c0, Color c1, Color c2, double mid, byte a0, byte a1)
    {
        if (brush == null) return;
        brush.GradientStops[0].Color  = Color.FromArgb(a0, c0.R, c0.G, c0.B);
        brush.GradientStops[1].Color  = Color.FromArgb(a1, c1.R, c1.G, c1.B);
        brush.GradientStops[1].Offset = mid;
        brush.GradientStops[2].Offset = mid + (1.0 - mid) * 0.5;
        brush.GradientStops[2].Color  = Color.FromArgb((byte)(a1 * 0.35), (byte)((c1.R + c2.R) / 2), (byte)((c1.G + c2.G) / 2), (byte)((c1.B + c2.B) / 2));
        brush.GradientStops[3].Color  = Color.FromArgb(0, c2.R, c2.G, c2.B);
    }

    private void BtnMediaPlayPause_Click(object sender, RoutedEventArgs e) => MediaBridge.SendCommand("PLAYPAUSE");
    private void BtnMediaPrev_Click(object sender, RoutedEventArgs e) => MediaBridge.SendCommand("PREV");
    private void BtnMediaNext_Click(object sender, RoutedEventArgs e) => MediaBridge.SendCommand("NEXT");
    private void BtnMediaMute_Click(object sender, RoutedEventArgs e) => MediaBridge.SendCommand("MUTE");
    private void BtnMediaAudioOnly_Click(object sender, RoutedEventArgs e) => MediaBridge.SendCommand("AUDIOONLY");
    private void TblMediaTitle_Click(object sender, MouseButtonEventArgs e) => MediaBridge.SendCommand("RETURNTAB");

    private double _mediaMarqueeTextWidth = -1;
    private double _mediaMarqueeContainerWidth = -1;

    private void TblMediaTitle_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMediaTitleMarquee();
    }

    private void UpdateMediaTitleMarquee()
    {
        double textWidth = Horizon.Stealth.Controls.MarqueeText.Measure(TblMediaTitle);
        double containerWidth = MediaTitleClip.MaxWidth;

        if (!Horizon.Stealth.Controls.MarqueeText.IsNeeded(textWidth, containerWidth))
        {
            _mediaMarqueeTextWidth = -1;
            _mediaMarqueeContainerWidth = -1;
            Horizon.Stealth.Controls.MarqueeText.Reset(MediaTitleTT);
            return;
        }

        if (textWidth == _mediaMarqueeTextWidth && containerWidth == _mediaMarqueeContainerWidth) return;

        _mediaMarqueeTextWidth = textWidth;
        _mediaMarqueeContainerWidth = containerWidth;
        Horizon.Stealth.Controls.MarqueeText.Start(MediaTitleTT, textWidth, containerWidth);
    }

    private void WidgetClockWeather_Click(object sender, MouseButtonEventArgs e)
    {
        var mainWin = Window.GetWindow(this) as MainWindow;
        if (mainWin == null) return;

        // If the user clicked directly on the weather text or city text, open the weather detail popup
        if ((e.OriginalSource is TextBlock tb && (tb == TblWeather || tb == TblWeatherCity)) || 
            (e.OriginalSource is FrameworkElement fe && fe.Tag?.ToString() == "WeatherOverlay"))
        {
            try
            {
                var method = typeof(MainWindow).GetMethod("OpenWeatherDetailPopup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(mainWin, null);
            }
            catch { }
        }
        else
        {
            // Otherwise, open the clock mode menu
            try
            {
                var method = typeof(MainWindow).GetMethod("OpenClockModeMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(mainWin, null);
            }
            catch { }
        }
        e.Handled = true;
    }

    private void WidgetVpnAdBlock_Click(object sender, MouseButtonEventArgs e)
    {
        var win = new Horizon.Stealth.Views.VpnControlCenterWindow { Owner = Window.GetWindow(this) };
        win.Show();
        e.Handled = true;
    }

    private void WidgetCalendar_Click(object sender, MouseButtonEventArgs e)
    {
        var mainWin = Window.GetWindow(this) as MainWindow;
        if (mainWin != null)
        {
            try
            {
                var method = typeof(MainWindow).GetMethod("OpenCalendarWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(mainWin, null);
            }
            catch { }
        }
        e.Handled = true;
    }

    private void WidgetMedia_Click(object sender, MouseButtonEventArgs e)
    {
        MediaBridge.SendCommand("RETURNTAB");
        e.Handled = true;
    }

    private void UpdateClock()
    {
        TblClock.Text = DateTime.Now.ToString("HH:mm");
    }

    private void RefreshCalendar()
    {
        Dispatcher.Invoke(() =>
        {
            TblCalendarDate.Text = DateTime.Now.ToString("dddd, d MMMM yyyy");

            var prev = CalendarBridge.GetPreviousEvent();
            TblCalendarPrev.Visibility = prev != null ? Visibility.Visible : Visibility.Collapsed;
            if (prev != null) TblCalendarPrev.Text = $"Last: {prev.Title} ({prev.Start:HH:mm})";

            var next = CalendarBridge.GetNextEvent();
            TblCalendarNext.Visibility = next != null ? Visibility.Visible : Visibility.Collapsed;
            if (next != null) TblCalendarNext.Text = $"Next: {next.Title} ({next.Start:HH:mm})";
        });
    }

    private void RefreshWeather()
    {
        Dispatcher.Invoke(() =>
        {
            TblWeather.Text = WeatherBridge.WeatherText;
            TblWeatherCity.Text = WeatherBridge.City;
        });
    }

    private const double MobileModeMinGap = 24;

    private void TxtHomeSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = TxtHomeSearch.Text ?? "";
        TxtHomeSearchPlaceholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_inFocusMode)
        {
            if (_focusWidgetName == "Favorites") RefreshFavorites();
            else if (_focusWidgetName == "Bookmarks") RefreshBookmarks();
        }
    }

    private void TxtHomeSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _inFocusMode)
        {
            ExitWidgetFocusMode();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;

        if (_inFocusMode)
        {
            if (_focusWidgetName == "Favorites")
            {
                var first = IcnColumns.Items.Cast<object>().FirstOrDefault() as PinItem;
                if (first != null && !string.IsNullOrWhiteSpace(first.Url))
                    NavigateRequested?.Invoke(first.Url);
            }
            else if (_focusWidgetName == "Bookmarks")
            {
                var first = IcnBookmarks.Items.Cast<object>().FirstOrDefault() as BookmarkItem;
                if (first != null && !string.IsNullOrWhiteSpace(first.Url))
                    NavigateRequested?.Invoke(first.Url);
            }
            e.Handled = true;
            return;
        }

        var query = TxtHomeSearch.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return;

        string url;
        if (Uri.TryCreate(query, UriKind.Absolute, out var direct) && (direct.Scheme == "http" || direct.Scheme == "https"))
        {
            url = query;
        }
        else
        {
            var template = SettingsService.Current.SearchEngineUrl;
            if (string.IsNullOrWhiteSpace(template))
                template = "https://alohafind.com/search/?q={query}";
            url = template.Replace("{query}", Uri.EscapeDataString(query));
        }

        NavigateRequested?.Invoke(url);
    }

    private void Pin_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PinItem pin && !string.IsNullOrWhiteSpace(pin.Url))
        {
            pin.OpenCount++;
            pin.LastOpened = DateTime.Now;
            SettingsService.Save();
            NavigateRequested?.Invoke(pin.Url);
        }
    }

    private void Bookmark_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BookmarkItem bookmark && !string.IsNullOrWhiteSpace(bookmark.Url))
        {
            BookmarkService.RecordOpen(bookmark);
            NavigateRequested?.Invoke(bookmark.Url);
        }
    }

    private static void StylePillButton(Button b, Color bg)
    {
        Brush text = GetContrastingTextBrush(bg);
        Brush fadingBorder = CreateFadingBorderBrush(((SolidColorBrush)text).Color);

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(bg));
        border.SetValue(Border.BorderBrushProperty, fadingBorder);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        var root = new FrameworkElementFactory(typeof(Grid));
        root.AppendChild(border);
        root.AppendChild(content);

        template.VisualTree = root;
        b.Template = template;
        b.Foreground = text;
        b.BorderThickness = new Thickness(0);
    }

    private static void StyleRoundedTextBox(TextBox tb)
    {
        var template = new ControlTemplate(typeof(TextBox));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.SetValue(FrameworkElement.NameProperty, "PART_ContentHost");
        host.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0));
        border.AppendChild(host);
        template.VisualTree = border;
        tb.Template = template;
        tb.BorderThickness = new Thickness(0);
    }

    private static Window BuildHomeDialogShell(Window owner, string title, out StackPanel content)
    {
        var dlg = new Window
        {
            Title                 = title,
            Width                 = 340,
            Height                = 190,
            WindowStyle           = WindowStyle.None,
            AllowsTransparency    = true,
            Background            = Brushes.Transparent,
            ResizeMode            = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner                 = owner,
        };

        var sp = new StackPanel { Margin = new Thickness(14) };
        var shell = new Border
        {
            CornerRadius    = new CornerRadius(16),
            Background      = new SolidColorBrush(Color.FromArgb(0xD9, 0x1A, 0x1A, 0x1A)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child           = sp,
        };

        const double backdropBlurRadius = 32;
        var backdropBrush = new ImageBrush();
        var backdropBase = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(16) };
        var backdropBlur = new Border
        {
            Margin = new Thickness(-backdropBlurRadius),
            Background = backdropBrush,
            Effect = new BlurEffect
            {
                Radius = backdropBlurRadius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            }
        };
        var backdropLayer = new Grid { ClipToBounds = true, IsHitTestVisible = false };
        backdropLayer.Children.Add(backdropBase);
        backdropLayer.Children.Add(backdropBlur);
        backdropLayer.OpacityMask = new VisualBrush(new Border
        {
            Width = dlg.Width,
            Height = dlg.Height,
            CornerRadius = new CornerRadius(16),
            Background = Brushes.Black
        })
        { Stretch = Stretch.None };

        var root = new Grid();
        root.Children.Add(backdropLayer);
        root.Children.Add(shell);
        dlg.Content = root;

        var unbindBackdrop = WidgetBackdropService.Bind(dlg, backdropBrush);
        dlg.Closed += (_, _) => unbindBackdrop();

        content = sp;
        return dlg;
    }

    private void MenuAddFavorite_Click(object sender, RoutedEventArgs e)
    {
        var dlg = BuildHomeDialogShell(Window.GetWindow(this), "Add Favorite", out var sp);

        var lblName = new TextBlock { Text = "Name", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) };
        var txName  = new TextBox  { Height = 28, FontSize = 13 };
        StyleRoundedTextBox(txName);

        var lblUrl  = new TextBlock { Text = "URL",  Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 11, Margin = new Thickness(0, 8, 0, 3) };
        var txUrl   = new TextBox  { Height = 28, FontSize = 13 };
        StyleRoundedTextBox(txUrl);

        var btnRow    = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var btnCancel = new Button { Content = "CANCEL", Width = 68, Height = 26, Margin = new Thickness(0, 0, 6, 0), FontWeight = FontWeights.Bold };
        var btnSave   = new Button { Content = "SAVE",   Width = 68, Height = 26, FontWeight = FontWeights.Bold };
        StylePillButton(btnCancel, Color.FromRgb(0x22, 0x22, 0x22));
        StylePillButton(btnSave, _lastPillBg);

        btnCancel.Click += (_, _) => dlg.Close();
        btnSave.Click += (_, _) =>
        {
            string url  = txUrl.Text.Trim();
            string name = txName.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;
            if (string.IsNullOrEmpty(name)) name = url;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;

            if (!SettingsService.Current.PinnedUrls.Any(p => p.Url == url))
            {
                SettingsService.Current.PinnedUrls.Add(new PinItem { Name = name, Url = url });
                SettingsService.Save();
                RefreshFavorites();
            }

            dlg.Close();
        };

        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnSave);
        sp.Children.Add(lblName);
        sp.Children.Add(txName);
        sp.Children.Add(lblUrl);
        sp.Children.Add(txUrl);
        sp.Children.Add(btnRow);
        txName.Focus();
        dlg.ShowDialog();
    }

    private void MenuAddBookmark_Click(object sender, RoutedEventArgs e)
    {
        var dlg = BuildHomeDialogShell(Window.GetWindow(this), "Add Bookmark", out var sp);

        var lblName = new TextBlock { Text = "Title", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) };
        var txName  = new TextBox  { Height = 28, FontSize = 13 };
        StyleRoundedTextBox(txName);

        var lblUrl  = new TextBlock { Text = "URL",   Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 11, Margin = new Thickness(0, 8, 0, 3) };
        var txUrl   = new TextBox  { Height = 28, FontSize = 13 };
        StyleRoundedTextBox(txUrl);

        var btnRow    = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var btnCancel = new Button { Content = "CANCEL", Width = 68, Height = 26, Margin = new Thickness(0, 0, 6, 0), FontWeight = FontWeights.Bold };
        var btnSave   = new Button { Content = "SAVE",   Width = 68, Height = 26, FontWeight = FontWeights.Bold };
        StylePillButton(btnCancel, Color.FromRgb(0x22, 0x22, 0x22));
        StylePillButton(btnSave, _lastPillBg);

        btnCancel.Click += (_, _) => dlg.Close();
        btnSave.Click += (_, _) =>
        {
            string url  = txUrl.Text.Trim();
            string name = txName.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;
            if (string.IsNullOrEmpty(name)) name = url;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;

            BookmarkService.Add(new BookmarkItem { Title = name, Url = url, DateAdded = DateTime.Now });
            RefreshBookmarks();
            dlg.Close();
        };

        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnSave);
        sp.Children.Add(lblName);
        sp.Children.Add(txName);
        sp.Children.Add(lblUrl);
        sp.Children.Add(txUrl);
        sp.Children.Add(btnRow);
        txName.Focus();
        dlg.ShowDialog();
    }

    private void MenuEnterEditMode_Click(object sender, RoutedEventArgs e)
    {
        EnterEditMode();
    }

    private void BtnHomeSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new HomePageSettingsWindow();
        win.Owner = Window.GetWindow(this);
        win.TintStrengthChanged += RefreshAdaptiveTint;
        win.ShowDialog();
        win.TintStrengthChanged -= RefreshAdaptiveTint;
        if (_inactivityTimer != null)
            _inactivityTimer.Interval = TimeSpan.FromSeconds(SettingsService.Current.HomeInactivityTimeoutSeconds);
        ApplySectionAndWidgetVisibility();
        if (win.WallpaperChanged)
        {
            ApplyWallpaper();
        }
        ApplyFont();
        RefreshFavorites();
        RefreshBookmarks();
    }

    private bool _inEditMode = false;
    private Point _dragStartPoint;
    private UIElement? _draggedWidget;
    private bool _editingInactivityLayout = false;

    private void InitDefaultLayoutSets()
    {
        foreach (var existingSet in SettingsService.Current.SavedLayoutSets)
        {
            if (existingSet.NormalLayout != null && !existingSet.NormalLayout.Widgets.Any(w => w.WidgetId == "VpnAdBlock"))
                existingSet.NormalLayout.Widgets.Add(new WidgetPosition { WidgetId = "VpnAdBlock", Row = 99, Column = 0 });

            if (existingSet.InactivityLayout != null && !existingSet.InactivityLayout.Widgets.Any(w => w.WidgetId == "VpnAdBlock"))
                existingSet.InactivityLayout.Widgets.Add(new WidgetPosition { WidgetId = "VpnAdBlock", Row = 99, Column = 0 });
        }

        if (SettingsService.Current.SavedLayoutSets.Count == 0)
        {
            var def = new LayoutSet { Name = "Default Layout" };
            def.NormalLayout.Widgets = new List<WidgetPosition>
            {
                new WidgetPosition { WidgetId = "ClockWeather", Row = 0, Column = 0 },
                new WidgetPosition { WidgetId = "Calendar", Row = 1, Column = 0 },
                new WidgetPosition { WidgetId = "Events", Row = 1, Column = 0 },
                new WidgetPosition { WidgetId = "Downloads", Row = 2, Column = 0 },
                new WidgetPosition { WidgetId = "VpnAdBlock", Row = 3, Column = 0 },
                new WidgetPosition { WidgetId = "Media", Row = 4, Column = 0 },
                new WidgetPosition { WidgetId = "Favorites", Row = 1, Column = 1 },
                new WidgetPosition { WidgetId = "Bookmarks", Row = 2, Column = 1 }
            };
            def.InactivityLayout.Widgets = new List<WidgetPosition>
            {
                new WidgetPosition { WidgetId = "ClockWeather", Row = 0, Column = 1 },
                new WidgetPosition { WidgetId = "Calendar", Row = 0, Column = 1 },
                new WidgetPosition { WidgetId = "Events", Row = 0, Column = 0 },
                new WidgetPosition { WidgetId = "Downloads", Row = 1, Column = 0 },
                new WidgetPosition { WidgetId = "VpnAdBlock", Row = 2, Column = 0 },
                new WidgetPosition { WidgetId = "Media", Row = 3, Column = 0 },
                new WidgetPosition { WidgetId = "Favorites", Row = 1, Column = 1 },
                new WidgetPosition { WidgetId = "Bookmarks", Row = 2, Column = 1 }
            };
            SettingsService.Current.SavedLayoutSets.Add(def);
            SettingsService.Current.ActiveLayoutSetId = def.Id;
            SettingsService.Save();
        }
    }

    public void EnterEditMode()
    {
        if (_inEditMode) return;
        if (_inFocusMode) ExitWidgetFocusMode();
        _inEditMode = true;
        _inactivityTimer?.Stop();

        EditModeOverlay.Visibility = Visibility.Visible;
        CmbLayoutSet.ItemsSource = null;
        CmbLayoutSet.ItemsSource = SettingsService.Current.SavedLayoutSets;
        var active = SettingsService.Current.SavedLayoutSets.FirstOrDefault(l => l.Id == SettingsService.Current.ActiveLayoutSetId);
        CmbLayoutSet.SelectedItem = active ?? SettingsService.Current.SavedLayoutSets.FirstOrDefault();

        AnimateInteractiveContent(true);
        ApplyActiveLayoutMatrix(_editingInactivityLayout);
    }

    private void BtnExitEditMode_Click(object sender, RoutedEventArgs e)
    {
        _inEditMode = false;
        EditModeOverlay.Visibility = Visibility.Collapsed;
        DropIndicatorBorder.Visibility = Visibility.Collapsed;
        SettingsService.Save();
        _editingInactivityLayout = false;
        ApplyActiveLayoutMatrix(false);
        _inactivityTimer?.Start();
    }

    private void EditState_Checked(object sender, RoutedEventArgs e)
    {
        if (EditModeOverlay == null || !_inEditMode) return;
        _editingInactivityLayout = RbEditInactivity.IsChecked == true;
        ApplyActiveLayoutMatrix(_editingInactivityLayout);
    }

    private void CmbLayoutSet_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbLayoutSet.SelectedItem is LayoutSet set)
        {
            SettingsService.Current.ActiveLayoutSetId = set.Id;
            SettingsService.Save();
            ApplyActiveLayoutMatrix(_editingInactivityLayout);
        }
    }

    private void BtnNewLayoutSet_Click(object sender, RoutedEventArgs e)
    {
        var active = SettingsService.Current.SavedLayoutSets.FirstOrDefault(l => l.Id == SettingsService.Current.ActiveLayoutSetId);
        var newSet = new LayoutSet { Name = "Custom Layout " + (SettingsService.Current.SavedLayoutSets.Count + 1) };
        if (active != null)
        {
            newSet.NormalLayout.Widgets = active.NormalLayout.Widgets.Select(w => new WidgetPosition { WidgetId = w.WidgetId, Row = w.Row, Column = w.Column }).ToList();
            newSet.InactivityLayout.Widgets = active.InactivityLayout.Widgets.Select(w => new WidgetPosition { WidgetId = w.WidgetId, Row = w.Row, Column = w.Column }).ToList();
        }
        SettingsService.Current.SavedLayoutSets.Add(newSet);
        SettingsService.Current.ActiveLayoutSetId = newSet.Id;
        SettingsService.Save();
        CmbLayoutSet.ItemsSource = null;
        CmbLayoutSet.ItemsSource = SettingsService.Current.SavedLayoutSets;
        CmbLayoutSet.SelectedItem = newSet;
    }

    private void BtnDeleteLayoutSet_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsService.Current.SavedLayoutSets.Count <= 1) return;
        if (CmbLayoutSet.SelectedItem is LayoutSet set)
        {
            SettingsService.Current.SavedLayoutSets.Remove(set);
            var next = SettingsService.Current.SavedLayoutSets.FirstOrDefault();
            if (next != null) SettingsService.Current.ActiveLayoutSetId = next.Id;
            SettingsService.Save();
            CmbLayoutSet.ItemsSource = null;
            CmbLayoutSet.ItemsSource = SettingsService.Current.SavedLayoutSets;
            CmbLayoutSet.SelectedItem = next;
        }
    }

    private const double ClockWeatherIslandPadding = 10.0;

    private void UpdateClockWeatherIslandBounds()
    {
        if (ClockWeatherIslandBorder == null || PnlClockWeather == null) return;
        if (PnlClockWeather.ActualWidth <= 0 || PnlClockWeather.ActualHeight <= 0) return;

        ClockWeatherIslandBorder.Width = PnlClockWeather.ActualWidth + ClockWeatherIslandPadding * 2;
        ClockWeatherIslandBorder.Height = PnlClockWeather.ActualHeight + ClockWeatherIslandPadding * 2;

        if (ZoneClockMode != null)
        {
            ZoneClockMode.Width = TblClock.ActualWidth;
            ZoneClockMode.Height = TblClock.ActualHeight;
            Canvas.SetLeft(ZoneClockMode, ClockWeatherIslandPadding);
            Canvas.SetTop(ZoneClockMode, ClockWeatherIslandPadding);
        }
        if (ZoneWeatherPopup != null)
        {
            ZoneWeatherPopup.Width = Math.Max(TblWeather.ActualWidth, TblWeatherCity.ActualWidth) + 20;
            ZoneWeatherPopup.Height = TblWeather.ActualHeight + TblWeatherCity.ActualHeight + 10;
            Canvas.SetLeft(ZoneWeatherPopup, ClockWeatherIslandPadding);
            Canvas.SetTop(ZoneWeatherPopup, ClockWeatherIslandPadding + TblClock.ActualHeight);
        }
        if (ZoneCalendar != null && PnlCalendar.Visibility == Visibility.Visible)
        {
            ZoneCalendar.Width = PnlCalendar.ActualWidth;
            ZoneCalendar.Height = PnlCalendar.ActualHeight;
            Canvas.SetLeft(ZoneCalendar, ClockWeatherIslandPadding);
            try
            {
                var t = PnlCalendar.TransformToVisual(PnlClockWeather);
                Canvas.SetTop(ZoneCalendar, t.Transform(new Point(0, 0)).Y + ClockWeatherIslandPadding);
            }
            catch { }
        }

        if (ZoneMedia != null && PnlMedia.Visibility == Visibility.Visible)
        {
            ZoneMedia.Width = Math.Max(MediaTitleClip.ActualWidth, TblMediaArtist.ActualWidth);
            ZoneMedia.Height = TblMediaTitle.ActualHeight + (TblMediaArtist.Visibility == Visibility.Visible ? TblMediaArtist.ActualHeight : 0);
            Canvas.SetLeft(ZoneMedia, ClockWeatherIslandPadding);
            try
            {
                var t = PnlMedia.TransformToVisual(PnlClockWeather);
                Canvas.SetTop(ZoneMedia, t.Transform(new Point(0, 0)).Y + ClockWeatherIslandPadding);
            }
            catch { }
        }

        if (ClockWeatherHitOverlay != null)
        {
            double overlayHeight = PnlClockWeather.ActualHeight;
            if (PnlMedia.Visibility == Visibility.Visible && PnlMediaButtons != null)
            {
                try
                {
                    var t = PnlMediaButtons.TransformToVisual(PnlClockWeather);
                    overlayHeight = t.Transform(new Point(0, 0)).Y;
                }
                catch { }
            }
            ClockWeatherHitOverlay.Height = overlayHeight + ClockWeatherIslandPadding * 2;
        }
    }

    private void SetClockWeatherIslandVisible(bool visible)
    {
        if (ClockWeatherIslandBorder == null) return;
        double target = visible ? 0.85 : 0.0;

        if (visible) UpdateClockWeatherIslandBounds();

        var duration = TimeSpan.FromMilliseconds(visible ? 260 : 130);
        var ease = new QuadraticEase { EasingMode = visible ? EasingMode.EaseOut : EasingMode.EaseIn };

        if (Math.Abs(ClockWeatherIslandBorder.Opacity - target) >= 0.01)
        {
            var anim = new DoubleAnimation
            {
                To = target,
                Duration = new Duration(duration),
                EasingFunction = ease
            };
            anim.CurrentTimeInvalidated += (_, _) => _homeGlass.Invalidate();
            anim.Completed += (_, _) => _homeGlass.Invalidate();
            ClockWeatherIslandBorder.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }

    private bool _layoutInactive;

    private double GetMediaTopLeftOffsetY(bool forceLayout)
    {
        if (PnlMedia.Visibility != Visibility.Visible) return 0.0;
        if (forceLayout && IsLoaded) PnlClockWeather.UpdateLayout();
        return -VisualTreeHelper.GetOffset(PnlMedia).Y;
    }

    private void ReanchorInactivityMedia()
    {
        if (!_layoutInactive || PnlMedia.Visibility != Visibility.Visible) return;
        double y = GetMediaTopLeftOffsetY(false);
        if (Math.Abs(MediaTrans.Y - y) < 0.5) return;
        AnimateDouble(MediaTrans, TranslateTransform.YProperty, y, TimeSpan.FromMilliseconds(250), new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    private void ApplyActiveLayoutMatrix(bool useInactivity, TimeSpan? animDuration = null, IEasingFunction? easing = null)
    {
        var activeSet = SettingsService.Current.SavedLayoutSets.FirstOrDefault(l => l.Id == SettingsService.Current.ActiveLayoutSetId);
        if (activeSet == null) return;

        var profile = useInactivity ? activeSet.InactivityLayout : activeSet.NormalLayout;
        if (profile == null || profile.Widgets.Count == 0) return;

        SetClockWeatherIslandVisible(!useInactivity && !_inEditMode && _isContentVisible);
        _layoutInactive = useInactivity;

        var map = new Dictionary<string, (UIElement Elem, TranslateTransform? Trans, ScaleTransform? Scale)>
        {
            { "ClockWeather", (PnlClockWeatherSub, ClockTrans, ClockScale) },
            { "Calendar", (PnlCalendar, CalendarTrans, CalendarScale) },
            { "Events", (PnlCalendarEvents, EventsTrans, EventsScale) },
            { "Downloads", (PnlDownloads, DownloadsTrans, DownloadsScale) },
            { "VpnAdBlock", (PnlVpnAdBlock, VpnAdBlockTrans, VpnAdBlockScale) },
            { "Media", (PnlMedia, MediaTrans, MediaScale) },
            { "Favorites", (PnlFavoritesContainer, null, null) },
            { "Bookmarks", (PnlBookmarksContainer, null, null) }
        };

        double col0Center = RootHomeGrid.ActualWidth * 0.22;
        double col1Center = RootHomeGrid.ActualWidth * 0.50;
        double col2Center = RootHomeGrid.ActualWidth * 0.78;

        foreach (var wp in profile.Widgets)
        {
            if (!map.TryGetValue(wp.WidgetId, out var tuple)) continue;

            double targetX = 0, targetY = 0;
            double targetScale = useInactivity ? 1.15 : 1.0;

            if (useInactivity && tuple.Trans != null && tuple.Scale != null)
            {
                double centerBase = wp.Column == 0 ? col0Center : (wp.Column == 2 ? col2Center : col1Center);
                targetX = centerBase - col0Center;
                targetY = wp.Row * 110.0;
                if (wp.WidgetId == "Media")
                {
                    targetX = 0;
                    targetY = GetMediaTopLeftOffsetY(true);
                }

                if (animDuration.HasValue && easing != null)
                {
                    AnimateDouble(tuple.Trans, TranslateTransform.XProperty, targetX, animDuration.Value, easing);
                    AnimateDouble(tuple.Trans, TranslateTransform.YProperty, targetY, animDuration.Value, easing);
                    AnimateDouble(tuple.Scale, ScaleTransform.ScaleXProperty, targetScale, animDuration.Value, easing);
                    AnimateDouble(tuple.Scale, ScaleTransform.ScaleYProperty, targetScale, animDuration.Value, easing);
                }
                else
                {
                    tuple.Trans.X = targetX;
                    tuple.Trans.Y = targetY;
                    tuple.Scale.ScaleX = targetScale;
                    tuple.Scale.ScaleY = targetScale;
                }
            }
            else
            {
                if (tuple.Trans != null && tuple.Scale != null)
                {
                    if (animDuration.HasValue && easing != null)
                    {
                        AnimateDouble(tuple.Trans, TranslateTransform.XProperty, 0.0, animDuration.Value, easing);
                        AnimateDouble(tuple.Trans, TranslateTransform.YProperty, 0.0, animDuration.Value, easing);
                        AnimateDouble(tuple.Scale, ScaleTransform.ScaleXProperty, 1.0, animDuration.Value, easing);
                        AnimateDouble(tuple.Scale, ScaleTransform.ScaleYProperty, 1.0, animDuration.Value, easing);
                    }
                    else
                    {
                        tuple.Trans.X = 0;
                        tuple.Trans.Y = 0;
                        tuple.Scale.ScaleX = 1.0;
                        tuple.Scale.ScaleY = 1.0;
                    }
                }
            }
        }

        _homeGlass.Invalidate();
    }



    private void Widget_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_inEditMode) return;
        _dragStartPoint = e.GetPosition(this);
        _draggedWidget = sender as UIElement;
    }

    private void Widget_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_inEditMode || e.LeftButton != MouseButtonState.Pressed || _draggedWidget == null) return;
        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            string? tag = (_draggedWidget as FrameworkElement)?.Tag as string;
            if (tag == "WeatherOverlay") tag = "ClockWeather";
            
            if (!string.IsNullOrEmpty(tag))
            {
                DragDrop.DoDragDrop(_draggedWidget, new DataObject("WidgetId", tag), DragDropEffects.Move);
            }
        }
    }

    private void EditModeOverlay_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("WidgetId"))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;

        Point p = e.GetPosition(RootHomeGrid);
        double w = RootHomeGrid.ActualWidth;
        double h = RootHomeGrid.ActualHeight;

        int col = p.X < w * 0.33 ? 0 : (p.X > w * 0.66 ? 2 : 1);
        int row = p.Y < h * 0.33 ? 0 : (p.Y > h * 0.66 ? 2 : 1);

        double cellW = w / 3.0;
        double cellH = h / 3.0;

        DropIndicatorBorder.Width = cellW - 20;
        DropIndicatorBorder.Height = cellH - 20;
        DropIndicatorBorder.Margin = new Thickness(col * cellW + 10, row * cellH + 10, 0, 0);
        DropIndicatorBorder.Visibility = Visibility.Visible;
    }

    private void EditModeOverlay_Drop(object sender, DragEventArgs e)
    {
        DropIndicatorBorder.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent("WidgetId")) return;
        string? widgetId = e.Data.GetData("WidgetId") as string;
        if (string.IsNullOrEmpty(widgetId)) return;

        Point p = e.GetPosition(RootHomeGrid);
        int col = p.X < RootHomeGrid.ActualWidth * 0.33 ? 0 : (p.X > RootHomeGrid.ActualWidth * 0.66 ? 2 : 1);
        int row = p.Y < RootHomeGrid.ActualHeight * 0.33 ? 0 : (p.Y > RootHomeGrid.ActualHeight * 0.66 ? 2 : 1);

        var activeSet = SettingsService.Current.SavedLayoutSets.FirstOrDefault(l => l.Id == SettingsService.Current.ActiveLayoutSetId);
        if (activeSet != null)
        {
            var profile = _editingInactivityLayout ? activeSet.InactivityLayout : activeSet.NormalLayout;
            var target = profile.Widgets.FirstOrDefault(w => w.WidgetId == widgetId);
            if (target != null)
            {
                target.Column = col;
                target.Row = row;
            }
            else
            {
                profile.Widgets.Add(new WidgetPosition { WidgetId = widgetId, Column = col, Row = row });
            }
            SettingsService.Save();
            ApplyActiveLayoutMatrix(_editingInactivityLayout);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && t.Name == name) return t;
            var result = FindVisualChild<T>(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            var wallLayer = menu.Template.FindName("WallLayer", menu) as Border;
            var glassHost = menu.Template.FindName("GlassHost", menu) as Grid;
            SetupGlassMenu(menu, wallLayer, glassHost);
        }
    }

    private void Submenu_Opened(object sender, EventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.Popup popup && popup.Child is FrameworkElement child)
        {
            var wallLayer = FindVisualChild<Border>(child, "WallLayer");
            var glassHost = FindVisualChild<Grid>(child, "GlassHost");
            SetupGlassMenu(popup, wallLayer, glassHost);
        }
    }

    private void SetupGlassMenu(FrameworkElement root, Border? wallLayer, Grid? glassHost)
    {
        if (wallLayer == null || glassHost == null) return;
        
        // Popups operate inside a separate invisible overlay window managed by WPF.
        var win = Window.GetWindow(root) ?? PresentationSource.FromVisual(root)?.RootVisual as Window;
        if (win == null) return;

        var wallBrush = new ImageBrush();
        wallLayer.Background = wallBrush;

        var unbind = WidgetBackdropService.Bind(win, wallBrush, overlap =>
        {
            if (overlap == null)
            {
                glassHost.Visibility = Visibility.Collapsed;
                return;
            }
            glassHost.Clip = new RectangleGeometry(overlap.Value);
            glassHost.Visibility = Visibility.Visible;
        });

        Action apply = () => {
            wallLayer.Opacity = WeatherBridge.ThemeWallpaper != null
                ? Math.Clamp(0.55 * SettingsService.Current.BackgroundOpacity, 0.0, 1.0)
                : 0.0;
        };
        
        apply();
        Action handler = () => root.Dispatcher.BeginInvoke(apply);
        WeatherBridge.ThemeUpdated += handler;

        if (root is ContextMenu cm)
        {
            RoutedEventHandler? closedHandler = null;
            closedHandler = (s, args) =>
            {
                cm.Closed -= closedHandler;
                WeatherBridge.ThemeUpdated -= handler;
                unbind();
            };
            cm.Closed += closedHandler;
        }
        else if (root is System.Windows.Controls.Primitives.Popup pp)
        {
            EventHandler? closedHandler = null;
            closedHandler = (s, args) =>
            {
                pp.Closed -= closedHandler;
                WeatherBridge.ThemeUpdated -= handler;
                unbind();
            };
            pp.Closed += closedHandler;
        }
    }
}