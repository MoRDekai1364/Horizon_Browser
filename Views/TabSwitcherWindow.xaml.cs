using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Horizon.Stealth.ViewModels;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class TabSwitcherWindow : Window
{
    // ── Win32 ──────────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern short GetKeyState(int vKey);
    private const int VK_CONTROL = 0x11;
    private static bool IsCtrlDown() => (GetKeyState(VK_CONTROL) & 0x8000) != 0;

    // ── State ──────────────────────────────────────────────────────────────────
    private List<TabViewModel>                       _tabs;
    private List<TabViewModel>                       _visibleTabs = new();
    private readonly Func<TabViewModel, Task<ImageSource?>> _captureFunc;
    private DispatcherTimer?                         _pollTimer;
    private int    _selectedIndex;
    private bool   _committed;
    private bool   _isPersistent;
    private string _filterText  = "";
    private readonly HashSet<int> _multiSelected = new(); // indices into _visibleTabs
    private int _anchorIndex = -1;
    private Action? _detachGlassBackdrop;
    private Action? _updateGlassAction;

    // ── Events ─────────────────────────────────────────────────────────────────
    public event Action<int>?          TabCommitted;
    public event Action<TabViewModel>? TabCloseRequested;
    public event Action?               Cancelled;

    public event Action<TabViewModel>?                TabDuplicateRequested;
    public event Action<TabViewModel>?                TabRenameRequested;
    public event Action<TabViewModel>?                TabChangePositionRequested;
    public event Action<TabViewModel>?                TabBringToTopRequested;
    public event Action<TabViewModel>?                TabMuteToggleRequested;
    public event Action<TabViewModel>?                TabSleepToggleRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiDuplicateRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiRenameRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiBringToTopRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiSendToEndRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiGroupRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiSleepAllRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiWakeAllRequested;

    // ── Construction ──────────────────────────────────────────────────────────
    public TabSwitcherWindow(List<TabViewModel> tabs, int initialIndex,
                             Func<TabViewModel, Task<ImageSource?>> captureFunc,
                             bool persistent = false)
    {
        InitializeComponent();

        _tabs          = tabs;
        _visibleTabs   = new List<TabViewModel>(tabs);
        _captureFunc   = captureFunc;
        _isPersistent  = persistent;
        _selectedIndex = Math.Clamp(initialIndex, 0, Math.Max(0, tabs.Count - 1));
        _anchorIndex   = _selectedIndex;

        TxtTabSwitcherHdr.Text = $"{tabs.Count} tab{(tabs.Count == 1 ? "" : "s")}";

        CardClipHost.Loaded      += (_, _) => UpdateCardClip();
        CardClipHost.SizeChanged += (_, _) => UpdateCardClip();

        AttachSnapshotBackdrop();
        Closed += (_, _) =>
        {
            _detachGlassBackdrop?.Invoke();
            _detachGlassBackdrop = null;
        };

        BuildTabList();
        HighlightRow(_selectedIndex, null);
        _ = LoadPreviewAsync(_selectedIndex);
        WidgetSyncGuard.RegisterTextFit(TxtPreviewTitle, this, minFontSize: 10, maxFontSize: 13);
        InitGridMode();

        if (_isPersistent)
        {
            ResizeMode                  = ResizeMode.CanResize;
            TxtCtrlTabLabel.Text        = "TABS";
            TxtHintBar.Text             = "\u2191\u2193/click: switch  \u00B7  Ctrl+click: select  \u00B7  Shift+click: range  \u00B7  Ctrl+A: all  \u00B7  Del: close  \u00B7  Esc: dismiss";
            BtnSwitcherClose.Visibility = Visibility.Visible;
            Loaded += (_, _) => TxtSearch.Focus();
        }
        else
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _pollTimer.Tick += (_, _) => { if (!IsCtrlDown()) Commit(); };
            _pollTimer.Start();
        }
    }

    private void UpdateCardClip()
    {
        if (CardClipHost.ActualWidth > 0 && CardClipHost.ActualHeight > 0)
        {
            CardClipHost.Clip = new RectangleGeometry(
                new Rect(0, 0, CardClipHost.ActualWidth, CardClipHost.ActualHeight), 16, 16);
        }
        _updateGlassAction?.Invoke();
    }

    private static bool IsHomeActive()
    {
        if (Application.Current?.MainWindow is MainWindow mainWin)
        {
            return mainWin.CurrentBrowser?.IsHomeActive == true;
        }
        return false;
    }

    private void AttachGlassBackdrop()
    {
        const double outerMargin = 32.0;
        const double bleedPad    = 48.0;

        var wallBrush = new ImageBrush();
        bool isFlat = SettingsService.Current.DisableBlurEffects;
        var wallLayer = new Border
        {
            Margin = new Thickness(-bleedPad),
            Background = wallBrush,
            Effect = isFlat ? null : new BlurEffect
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

        Rect? lastOverlap = null;
        void UpdateGlass(Rect? overlap)
        {
            lastOverlap = overlap;
            bool flat = SettingsService.Current.DisableBlurEffects;
            wallLayer.Effect = flat ? null : new BlurEffect
            {
                Radius = 32,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            };
            if (flat || !IsHomeActive() || overlap == null)
            {
                glassHost.Visibility = Visibility.Collapsed;
                return;
            }

            double winW  = ActualWidth  > 0 ? ActualWidth  : Width;
            double winH  = ActualHeight > 0 ? ActualHeight : Height;
            double cardW = CardClipHost.ActualWidth  > 0 ? CardClipHost.ActualWidth  : Math.Max(1.0, winW - 2.0 * outerMargin);
            double cardH = CardClipHost.ActualHeight > 0 ? CardClipHost.ActualHeight : Math.Max(1.0, winH - 2.0 * outerMargin);

            var cardBoundsInCard = new Rect(0, 0, cardW, cardH);
            var overlapInCard = new Rect(
                overlap.Value.X - outerMargin,
                overlap.Value.Y - outerMargin,
                overlap.Value.Width,
                overlap.Value.Height);
            overlapInCard.Intersect(cardBoundsInCard);

            if (overlapInCard.IsEmpty || overlapInCard.Width < 1 || overlapInCard.Height < 1)
            {
                glassHost.Visibility = Visibility.Collapsed;
                return;
            }

            var wp = WeatherBridge.ThemeWallpaper;
            wallBrush.ImageSource = wp;
            if (wp != null)
            {
                var winUv = WidgetBackdropService.GetUvRect(this);
                double fx = outerMargin / Math.Max(1.0, winW);
                double fy = outerMargin / Math.Max(1.0, winH);
                double fw = Math.Max(0.01, cardW / Math.Max(1.0, winW));
                double fh = Math.Max(0.01, cardH / Math.Max(1.0, winH));
                var cardUv = new Rect(
                    winUv.X + winUv.Width * fx,
                    winUv.Y + winUv.Height * fy,
                    winUv.Width * fw,
                    winUv.Height * fh);
                HomeGlassService.ApplyBleedBrushCrop(wallBrush, cardUv, cardW, cardH, bleedPad);
            }

            glassHost.Clip = new RectangleGeometry(cardBoundsInCard, 16, 16);
            glassHost.Visibility = Visibility.Visible;
        }

        var unbind = WidgetBackdropService.Bind(this, new ImageBrush(), overlap =>
        {
            UpdateGlass(overlap);
        });

        void Apply()
        {
            bool isHome = IsHomeActive();
            bool flat = SettingsService.Current.DisableBlurEffects;
            if (!isHome || flat || WeatherBridge.ThemeWallpaper == null)
            {
                glassHost.Visibility = Visibility.Collapsed;
                wallLayer.Opacity = 0.0;
            }
            else
            {
                wallLayer.Opacity = Math.Clamp(0.55 * SettingsService.Current.BackgroundOpacity, 0.0, 1.0);
                UpdateGlass(lastOverlap);
            }

            byte bgAlpha = (byte)Math.Max((int)TintService.Apply(0xD0), 0xA0);
            Color searchBg = ThemeService.IsLight
                ? Color.FromArgb(Math.Min(bgAlpha, (byte)0xB8), 0xF4, 0xF6, 0xF8)
                : WeatherBridge.ThemeDarkWallpaper
                    ? Color.FromArgb(bgAlpha, 0x14, 0x14, 0x14)
                    : Color.FromArgb(bgAlpha, 0x22, 0x22, 0x22);
            tintLayer.Background = HomePageView.CreateSearchBarBackgroundBrush(searchBg);
        }

        Apply();
        _updateGlassAction = () => UpdateGlass(lastOverlap);
        Action handler = () => Dispatcher.BeginInvoke(new Action(Apply));
        WeatherBridge.ThemeUpdated += handler;
        Controls.HomeGlassMotion.BlurSettingChanged += handler;
        _detachGlassBackdrop = () =>
        {
            _updateGlassAction = null;
            WeatherBridge.ThemeUpdated -= handler;
            Controls.HomeGlassMotion.BlurSettingChanged -= handler;
            unbind();
        };
    }

    // ── Public navigation (called from MainWindow) ─────────────────────────────
    public void Step(int direction)
    {
        if (_visibleTabs.Count == 0) return;
        int next = (_selectedIndex + direction + _visibleTabs.Count) % _visibleTabs.Count;
        SelectIndex(next);
    }

    public void CommitCurrent() => Commit();
    public void CancelSelf()    => Cancel();

    // ── Build list ────────────────────────────────────────────────────────────
    private void CloseTabFromSwitcher(TabViewModel tab)
    {
        TabCloseRequested?.Invoke(tab);
        _tabs.Remove(tab);
        _visibleTabs.Remove(tab);
        _multiSelected.Clear();
        _anchorIndex = -1;
        TxtTabSwitcherHdr.Text = $"{_tabs.Count} tab{(_tabs.Count == 1 ? "" : "s")}";
        if (_tabs.Count == 0) { _committed = true; Close(); return; }
        _selectedIndex = Math.Clamp(_selectedIndex, 0, _visibleTabs.Count - 1);
        BuildTabList();
        HighlightRow(_selectedIndex, null);
        _ = LoadPreviewAsync(_selectedIndex);
        UpdateActionBar();
    }

    // Closes all currently multi-selected tabs, or (if invert=true) all EXCEPT them.
    private void CloseSelectedTabs(bool invert = false)
    {
        var toClose = _visibleTabs
            .Where((_, i) => invert ? !_multiSelected.Contains(i) : _multiSelected.Contains(i))
            .ToList();

        foreach (var tab in toClose)
        {
            TabCloseRequested?.Invoke(tab);
            _tabs.Remove(tab);
        }

        _multiSelected.Clear();
        _anchorIndex = -1;

        _visibleTabs = string.IsNullOrEmpty(_filterText)
            ? new List<TabViewModel>(_tabs)
            : _tabs.FindAll(t =>
                  t.DisplayTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                  t.Url.Contains(_filterText, StringComparison.OrdinalIgnoreCase)          ||
                  t.DomainTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

        TxtTabSwitcherHdr.Text = $"{_tabs.Count} tab{(_tabs.Count == 1 ? "" : "s")}";

        if (_tabs.Count == 0) { _committed = true; Close(); return; }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, _visibleTabs.Count - 1);
        BuildTabList();
        HighlightRow(_selectedIndex, null);
        _ = LoadPreviewAsync(_selectedIndex);
        UpdateActionBar();
    }

    private void BuildTabList()
    {
        TabListPanel.Children.Clear();
        RebuildGridIfActive();

        for (int i = 0; i < _visibleTabs.Count; i++)
        {
            var tab      = _visibleTabs[i];
            int rowIndex = i;

            // Outer row — holds the left accent bar + content
            var row = new Border
            {
                Tag             = rowIndex,
                Cursor          = Cursors.Hand,
                Background      = Brushes.Transparent,
                BorderBrush     = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Margin          = new Thickness(4, 2, 4, 2),
                Padding         = new Thickness(6, 6, 6, 6)
            };

            // Close button — declared early so hover handlers can reference it
            var closeBtn = new Button
            {
                Content           = "\u2715",
                FontSize          = 10,
                Width             = 18, Height = 18,
                Padding           = new Thickness(0),
                Margin            = new Thickness(6, 0, 2, 0),
                Background        = Brushes.Transparent,
                BorderThickness   = new Thickness(0),
                Foreground        = new SolidColorBrush(Color.FromRgb(0xEE, 0x66, 0x66)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor            = Cursors.Hand,
                Opacity           = 0.0,
                ToolTip           = "Close tab (Del)"
            };
            closeBtn.Click += (_, e) => { e.Handled = true; CloseTabFromSwitcher(tab); };

            row.MouseEnter += (_, _) =>
            {
                bool isCursor   = (int)row.Tag == _selectedIndex;
                bool isSelected = _multiSelected.Contains((int)row.Tag);
                if (!isCursor && !isSelected)
                {
                    row.Background  = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                    row.BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
                }
                closeBtn.Opacity = 1.0;
            };
            row.MouseLeave += (_, _) =>
            {
                bool isCursor   = (int)row.Tag == _selectedIndex;
                bool isSelected = _multiSelected.Contains((int)row.Tag);
                if (!isCursor && !isSelected)
                {
                    row.Background  = Brushes.Transparent;
                    row.BorderBrush = Brushes.Transparent;
                }
                closeBtn.Opacity = 0.0;
            };
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.Handled) return; // close button already handled
                bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

                if (ctrl && shift)
                {
                    int anchor = _anchorIndex >= 0 ? _anchorIndex : _selectedIndex;
                    int lo = Math.Min(anchor, rowIndex);
                    int hi = Math.Max(anchor, rowIndex);
                    for (int j = lo; j <= hi; j++) _multiSelected.Add(j);
                    _selectedIndex = rowIndex;
                    HighlightRow(_selectedIndex, null);
                    UpdateActionBar();
                    _ = LoadPreviewAsync(_selectedIndex);
                }
                else if (ctrl)
                {
                    if (_multiSelected.Contains(rowIndex))
                        _multiSelected.Remove(rowIndex);
                    else
                    {
                        _multiSelected.Add(rowIndex);
                        _anchorIndex = rowIndex;
                    }
                    _selectedIndex = rowIndex;
                    HighlightRow(_selectedIndex, null);
                    UpdateActionBar();
                    _ = LoadPreviewAsync(_selectedIndex);
                }
                else if (shift)
                {
                    _multiSelected.Clear();
                    int anchor = _anchorIndex >= 0 ? _anchorIndex : _selectedIndex;
                    int lo = Math.Min(anchor, rowIndex);
                    int hi = Math.Max(anchor, rowIndex);
                    for (int j = lo; j <= hi; j++) _multiSelected.Add(j);
                    _selectedIndex = rowIndex;
                    HighlightRow(_selectedIndex, null);
                    UpdateActionBar();
                    _ = LoadPreviewAsync(_selectedIndex);
                }
                else
                {
                    _multiSelected.Clear();
                    _anchorIndex   = rowIndex;
                    _selectedIndex = rowIndex;
                    UpdateActionBar();
                    SelectIndex(rowIndex);
                    Commit();
                }
            };

            row.MouseRightButtonUp += (_, e2) =>
            {
                if (e2.Handled) return;
                e2.Handled = true;
                if (_multiSelected.Count >= 2 && _multiSelected.Contains(rowIndex))
                    ShowSwitcherMultiContextMenu(row);
                else
                    ShowSwitcherTabContextMenu(row, tab);
            };

            // Grid: accent indicator | inner content
            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accent = new Border
            {
                Background        = Brushes.Transparent,
                CornerRadius      = new CornerRadius(2),
                Margin            = new Thickness(0, 2, 4, 2),
                Tag               = "accent"
            };
            Grid.SetColumn(accent, 0);
            rowGrid.Children.Add(accent);

            // Inner content area
            var inner = new Grid { Margin = new Thickness(6, 4, 6, 4) };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Favicon or Monogram badge
            var iconContainer = new Border
            {
                Width             = 24,
                Height            = 24,
                CornerRadius      = new CornerRadius(6),
                Background        = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                BorderBrush       = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                BorderThickness   = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds      = true
            };

            ImageSource? resolvedIcon = null;
            try
            {
                var conv = new Horizon.Stealth.Controls.FaviconConverter();
                var iconObj = conv.Convert(tab.Url, typeof(ImageSource), string.Empty, System.Globalization.CultureInfo.InvariantCulture);
                if (iconObj is ImageSource src)
                {
                    resolvedIcon = src;
                }
                else if (iconObj is string iconUrl && Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = uri;
                    bmp.DecodePixelWidth = 24;
                    bmp.EndInit();
                    resolvedIcon = bmp;
                }
            }
            catch { }

            if (resolvedIcon != null)
            {
                iconContainer.Child = new Image
                {
                    Source            = resolvedIcon,
                    Width             = 16, Height = 16,
                    Stretch           = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
            }
            else
            {
                string letter = (!string.IsNullOrWhiteSpace(tab.DomainTitle) ? tab.DomainTitle : (!string.IsNullOrWhiteSpace(tab.DisplayTitle) ? tab.DisplayTitle : "H")).Trim();
                string initial = letter.Length > 0 ? letter.Substring(0, 1).ToUpperInvariant() : "H";
                int hash = Math.Abs(letter.GetHashCode());
                Color[] monogramColors = {
                    Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x05, 0x96, 0x69),
                    Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0xDB, 0x27, 0x77),
                    Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0x08, 0x91, 0xB2)
                };
                Color monoBg = monogramColors[hash % monogramColors.Length];
                iconContainer.Background = new SolidColorBrush(Color.FromArgb(0x55, monoBg.R, monoBg.G, monoBg.B));
                iconContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, monoBg.R, monoBg.G, monoBg.B));
                iconContainer.Child = new TextBlock
                {
                    Text                = initial,
                    FontFamily          = new FontFamily("Segoe UI"),
                    FontWeight          = FontWeights.Bold,
                    FontSize            = 11,
                    Foreground          = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                };
            }

            Grid.SetColumn(iconContainer, 0);
            inner.Children.Add(iconContainer);

            // Title + domain stack
            var textStack = new StackPanel
            {
                Margin            = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            textStack.Children.Add(new TextBlock
            {
                Text         = tab.DisplayTitle,
                Foreground   = Brushes.White,
                FontFamily   = new FontFamily("Segoe UI"),
                FontSize     = 12,
                FontWeight   = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth     = 175
            });
            textStack.Children.Add(new TextBlock
            {
                Text         = tab.DomainTitle,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                FontFamily   = new FontFamily("Segoe UI"),
                FontSize     = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth     = 175
            });
            Grid.SetColumn(textStack, 1);
            inner.Children.Add(textStack);

            // Status badges
            var badges = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (tab.IsPlayingAudio)
                badges.Children.Add(Badge(tab.HasVideo ? "\U0001F3AC" : "\U0001F50A"));
            if (tab.IsMuted)
                badges.Children.Add(Badge("\U0001F507"));
            if (tab.IsSleeping)
                badges.Children.Add(Badge("\U0001F4A4"));
            if (tab.IsLoading)
                badges.Children.Add(Badge("\u27F3", Color.FromRgb(0x88, 0xCC, 0xFF)));
            if (tab.IsActiveDownload)
                badges.Children.Add(Badge("\u2B07"));
            Grid.SetColumn(badges, 2);
            inner.Children.Add(badges);

            Grid.SetColumn(closeBtn, 3);
            inner.Children.Add(closeBtn);

            Grid.SetColumn(inner, 1);
            rowGrid.Children.Add(inner);

            row.Child = rowGrid;
            TabListPanel.Children.Add(row);
        }
    }

    private void UpdateActionBar()
    {
        MultiActionBar.Visibility = Visibility.Collapsed;
    }

    private void BtnCloseSelected_Click(object sender, RoutedEventArgs e) => CloseSelectedTabs(invert: false);
    private void BtnCloseOthers_Click(object sender, RoutedEventArgs e)   => CloseSelectedTabs(invert: true);

    private static TextBlock Badge(string text, Color? color = null) => new TextBlock
    {
        Text       = text,
        FontSize   = 10,
        FontFamily = new FontFamily("Segoe UI"),
        Margin     = new Thickness(2, 0, 0, 0),
        Foreground = new SolidColorBrush(color ?? Colors.White)
    };

    // ── Selection ─────────────────────────────────────────────────────────────
    private void SelectIndex(int index)
    {
        _selectedIndex = Math.Clamp(index, 0, _visibleTabs.Count - 1);
        HighlightRow(_selectedIndex, null);
        ScrollRowIntoView(_selectedIndex);
        _ = LoadPreviewAsync(_selectedIndex);
    }

    private void HighlightRow(int selectedIndex, Border? previousRow)
    {
        HighlightGridTiles();
        foreach (var child in TabListPanel.Children)
        {
            if (child is not Border row || row.Tag is not int rowIdx) continue;

            bool isCursor   = rowIdx == selectedIndex;
            bool isSelected = _multiSelected.Contains(rowIdx);

            if (isCursor)
            {
                row.Background  = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0xCC, 0x66));
                row.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0xFF, 0x88));
                row.BorderThickness = new Thickness(1);
            }
            else if (isSelected)
            {
                row.Background  = new SolidColorBrush(Color.FromArgb(0x28, 0x33, 0x88, 0xFF));
                row.BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0x44, 0xAA, 0xFF));
                row.BorderThickness = new Thickness(1);
            }
            else
            {
                row.Background  = Brushes.Transparent;
                row.BorderBrush = Brushes.Transparent;
                row.BorderThickness = new Thickness(1);
            }

            // Update left accent bar
            if (row.Child is Grid rg && rg.Children.Count > 0 &&
                rg.Children[0] is Border accentBar)
            {
                if (isCursor)
                    accentBar.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
                else if (isSelected)
                    accentBar.Background = new SolidColorBrush(Color.FromRgb(0x44, 0xAA, 0xFF));
                else
                    accentBar.Background = Brushes.Transparent;
            }
        }
    }

    private void ScrollRowIntoView(int index)
    {
        ScrollGridTileIntoView(index);
        if (index < 0 || index >= TabListPanel.Children.Count) return;
        if (TabListPanel.Children[index] is FrameworkElement el)
            el.BringIntoView();
    }

    // ── Preview loader ────────────────────────────────────────────────────────
    private int _previewRequestVersion;

    public void NotifyThumbnailReady(TabViewModel tab, ImageSource thumbnail)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => NotifyThumbnailReady(tab, thumbnail));
            return;
        }
        UpdateGridThumbnail(tab, thumbnail);
        if (_committed || !IsVisible) return;
        if (_selectedIndex >= 0 && _selectedIndex < _visibleTabs.Count &&
            ReferenceEquals(_visibleTabs[_selectedIndex], tab))
        {
            TxtPreviewTitle.Text        = tab.DisplayTitle;
            PreviewImage.Source         = thumbnail;
            PnlPreviewStatus.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadPreviewAsync(int index)
    {
        if (index < 0 || index >= _visibleTabs.Count) return;
        var tab = _visibleTabs[index];
        int reqVersion = ++_previewRequestVersion;

        TxtPreviewTitle.Text = tab.DisplayTitle;

        if (tab.Thumbnail != null)
        {
            PreviewImage.Source         = tab.Thumbnail;
            PnlPreviewStatus.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewImage.Source         = null;
        PnlPreviewStatus.Visibility = Visibility.Visible;
        TxtPreviewStatus.Text       = "Loading live webpage preview\u2026";

        try
        {
            var img = await _captureFunc(tab);
            if (_committed || reqVersion != _previewRequestVersion || index != _selectedIndex)
                return;

            if (img != null)
            {
                PreviewImage.Source         = img;
                PnlPreviewStatus.Visibility = Visibility.Collapsed;
                return;
            }

            TxtPreviewStatus.Text = tab.IsLoading
                ? $"Loading {tab.DomainTitle}\u2026"
                : "Rendering live preview\u2026";

            await Task.Delay(1200);
            if (_committed || reqVersion != _previewRequestVersion || index != _selectedIndex || PreviewImage.Source != null)
                return;

            var retryImg = tab.Thumbnail ?? await _captureFunc(tab);
            if (_committed || reqVersion != _previewRequestVersion || index != _selectedIndex)
                return;

            if (retryImg != null)
            {
                PreviewImage.Source         = retryImg;
                PnlPreviewStatus.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtPreviewStatus.Text = tab.DisplayTitle;
            }
        }
        catch
        {
            if (reqVersion == _previewRequestVersion && index == _selectedIndex)
                TxtPreviewStatus.Text = "Preview unavailable";
        }
    }

    // ── Commit / Cancel ───────────────────────────────────────────────────────
    private void Commit()
    {
        if (_committed) return;
        _committed = true;
        _pollTimer?.Stop();

        if (_visibleTabs.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _visibleTabs.Count)
        {
            int originalIdx = _tabs.IndexOf(_visibleTabs[_selectedIndex]);
            TabCommitted?.Invoke(originalIdx >= 0 ? originalIdx : _selectedIndex);
        }

        Close();
    }

    private void Cancel()
    {
        if (_committed) return;
        _committed = true;
        _pollTimer?.Stop();
        Cancelled?.Invoke();
        Close();
    }

    // ── Keyboard / Focus ──────────────────────────────────────────────────────
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

        if (HandleModeKeys(e)) return;

        switch (e.Key)
        {
            case Key.Escape:
                if (_multiSelected.Count > 0)
                {
                    _multiSelected.Clear();
                    _anchorIndex = -1;
                    HighlightRow(_selectedIndex, null);
                    UpdateActionBar();
                    e.Handled = true; return;
                }
                Cancel(); e.Handled = true; return;

            case Key.Return:
            case Key.Space:
                Commit(); e.Handled = true; return;

            case Key.Tab when ctrl:
                Step(shift ? -1 : +1); e.Handled = true; return;

            case Key.Down:
            case Key.Right:
                Step(+1); e.Handled = true; return;

            case Key.Up:
            case Key.Left:
                Step(-1); e.Handled = true; return;

            case Key.Delete:
                if (_multiSelected.Count > 1)
                    CloseSelectedTabs();
                else if (_visibleTabs.Count > 0)
                    CloseTabFromSwitcher(_visibleTabs[_selectedIndex]);
                e.Handled = true; return;

            case Key.LeftCtrl:
            case Key.RightCtrl:
                return;

            case Key.A when ctrl:
                for (int i = 0; i < _visibleTabs.Count; i++) _multiSelected.Add(i);
                if (_visibleTabs.Count > 0) _anchorIndex = 0;
                HighlightRow(_selectedIndex, null);
                UpdateActionBar();
                e.Handled = true; return;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_committed) return;
        Cancel();
    }

    private void BtnSwitcherClose_Click(object sender, RoutedEventArgs e)
    {
        Cancel();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filterText  = TxtSearch.Text.Trim();
        _visibleTabs = string.IsNullOrEmpty(_filterText)
            ? new List<TabViewModel>(_tabs)
            : _tabs.FindAll(t =>
                  t.DisplayTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                  t.Url.Contains(_filterText, StringComparison.OrdinalIgnoreCase)          ||
                  t.DomainTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

        _selectedIndex = 0;
        _multiSelected.Clear();
        _anchorIndex = -1;
        BuildTabList();
        UpdateActionBar();

        if (_visibleTabs.Count > 0)
        {
            HighlightRow(0, null);
            _ = LoadPreviewAsync(0);
        }
        else
        {
            PreviewImage.Source         = null;
            PnlPreviewStatus.Visibility = Visibility.Visible;
            TxtPreviewStatus.Text       = "No matching tabs";
        }
    }

    // ── Dark context-menu helpers ──────────────────────────────────────────────
    private ContextMenu MakeDarkContextMenu(UIElement target) => new ContextMenu
    {
        PlacementTarget = target,
        Placement       = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        Background      = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
        BorderBrush     = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        Padding         = new Thickness(4, 4, 4, 4)
    };

    private static MenuItem MakeDarkMenuItem(string header, bool isEnabled = true)
    {
        var mi = new MenuItem
        {
            Header          = header,
            IsEnabled       = isEnabled,
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(12, 6, 12, 6),
            FontFamily      = new FontFamily("Segoe UI"),
            FontSize        = 12
        };
        mi.Resources[SystemColors.HighlightBrushKey]     = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        mi.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
        mi.Resources[SystemColors.MenuHighlightBrushKey] = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        return mi;
    }

    private static Separator MakeDarkSeparator() => new Separator
    {
        Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
        Margin     = new Thickness(8, 3, 8, 3)
    };

    private void ShowSwitcherTabContextMenu(UIElement target, TabViewModel tab)
    {
        var cm = MakeDarkContextMenu(target);

        var miDup = MakeDarkMenuItem("\U0001F4CB  Duplicate Tab");
        miDup.Click += (_, _) => TabDuplicateRequested?.Invoke(tab);
        cm.Items.Add(miDup);

        var miRen = MakeDarkMenuItem("\U0001F3F7  Rename Tab\u2026");
        miRen.Click += (_, _) => TabRenameRequested?.Invoke(tab);
        cm.Items.Add(miRen);

        var miPos = MakeDarkMenuItem("\u2195  Change Tab Position");
        miPos.Click += (_, _) => TabChangePositionRequested?.Invoke(tab);
        cm.Items.Add(miPos);

        var miTop = MakeDarkMenuItem("\u2B06  Bring Tab to Top");
        miTop.Click += (_, _) => TabBringToTopRequested?.Invoke(tab);
        cm.Items.Add(miTop);

        cm.Items.Add(MakeDarkSeparator());

        var miMute = MakeDarkMenuItem(tab.IsMuted ? "\U0001F507  Unmute Tab" : "\U0001F50A  Mute Tab");
        miMute.Click += (_, _) => TabMuteToggleRequested?.Invoke(tab);
        cm.Items.Add(miMute);

        var miSleep = MakeDarkMenuItem(
            tab.IsSleeping ? "\u2600  Wake Tab" : "\U0001F4A4  Sleep Tab",
            isEnabled: tab.IsSleeping || (!tab.IsPlayingAudio && !tab.NeverSleep));
        miSleep.Click += (_, _) => TabSleepToggleRequested?.Invoke(tab);
        cm.Items.Add(miSleep);

        cm.Items.Add(MakeDarkSeparator());

        var miClose = MakeDarkMenuItem("\u2715  Close Tab");
        miClose.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0x77, 0x77));
        miClose.Click += (_, _) => CloseTabFromSwitcher(tab);
        cm.Items.Add(miClose);

        int othersCount = _visibleTabs.Count - 1;
        var miOthers = MakeDarkMenuItem($"\u22A0  Close Others ({othersCount})", isEnabled: othersCount > 0);
        miOthers.Foreground = othersCount > 0
            ? new SolidColorBrush(Color.FromRgb(0xEE, 0x77, 0x77))
            : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        miOthers.Click += (_, _) =>
        {
            var toClose = _visibleTabs.Where(t => t != tab).ToList();
            foreach (var t in toClose)
            {
                TabCloseRequested?.Invoke(t);
                _tabs.Remove(t);
            }
            _multiSelected.Clear();
            _anchorIndex   = _selectedIndex;
            _visibleTabs = string.IsNullOrEmpty(_filterText)
                ? new List<TabViewModel>(_tabs)
                : _tabs.FindAll(tt =>
                    tt.DisplayTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                    tt.Url.Contains(_filterText, StringComparison.OrdinalIgnoreCase)          ||
                    tt.DomainTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase));
            TxtTabSwitcherHdr.Text = $"{_tabs.Count} tab{(_tabs.Count == 1 ? "" : "s")}";
            if (_tabs.Count == 0) { _committed = true; Close(); return; }
            _selectedIndex = Math.Clamp(_selectedIndex, 0, _visibleTabs.Count - 1);
            BuildTabList();
            HighlightRow(_selectedIndex, null);
            _ = LoadPreviewAsync(_selectedIndex);
            UpdateActionBar();
        };
        cm.Items.Add(miOthers);

        cm.IsOpen = true;
    }

    private void ShowSwitcherMultiContextMenu(UIElement target)
    {
        var selected = _multiSelected
            .Where(i => i >= 0 && i < _visibleTabs.Count)
            .OrderBy(i => i)
            .Select(i => _visibleTabs[i])
            .ToList();

        if (selected.Count == 0) return;

        var cm = MakeDarkContextMenu(target);

        var miHeader = MakeDarkMenuItem($"{selected.Count} TABS SELECTED", isEnabled: false);
        miHeader.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x99, 0xCC));
        miHeader.FontWeight = FontWeights.Bold;
        cm.Items.Add(miHeader);
        cm.Items.Add(MakeDarkSeparator());

        var miDup = MakeDarkMenuItem("\U0001F4CB  Duplicate All");
        miDup.Click += (_, _) => MultiDuplicateRequested?.Invoke(selected);
        cm.Items.Add(miDup);

        var miRen = MakeDarkMenuItem("\U0001F3F7  Rename All\u2026");
        miRen.Click += (_, _) => MultiRenameRequested?.Invoke(selected);
        cm.Items.Add(miRen);

        cm.Items.Add(MakeDarkSeparator());

        var miTop = MakeDarkMenuItem("\u2B06  Bring All to Top");
        miTop.Click += (_, _) => MultiBringToTopRequested?.Invoke(selected);
        cm.Items.Add(miTop);

        var miEnd = MakeDarkMenuItem("\u2B07  Send All to End");
        miEnd.Click += (_, _) => MultiSendToEndRequested?.Invoke(selected);
        cm.Items.Add(miEnd);

        var miGrp = MakeDarkMenuItem("\U0001F4E6  Group Selected Tabs");
        miGrp.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xEE, 0xBB));
        miGrp.Click += (_, _) => MultiGroupRequested?.Invoke(selected);
        cm.Items.Add(miGrp);

        cm.Items.Add(MakeDarkSeparator());

        var miSleep = MakeDarkMenuItem("\U0001F4A4  Sleep All Selected");
        miSleep.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xFF));
        miSleep.Click += (_, _) => MultiSleepAllRequested?.Invoke(selected);
        cm.Items.Add(miSleep);

        var miWake = MakeDarkMenuItem("\u2600  Wake All Selected");
        miWake.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xFF, 0xAA));
        miWake.Click += (_, _) => MultiWakeAllRequested?.Invoke(selected);
        cm.Items.Add(miWake);

        cm.Items.Add(MakeDarkSeparator());

        var miClose = MakeDarkMenuItem($"\u2715  Close {selected.Count} Selected");
        miClose.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0x77, 0x77));
        miClose.Click += (_, _) => CloseSelectedTabs(invert: false);
        cm.Items.Add(miClose);

        int othersCount = _visibleTabs.Count - selected.Count;
        var miOthers = MakeDarkMenuItem($"\u22A0  Close Others ({othersCount})", isEnabled: othersCount > 0);
        miOthers.Foreground = othersCount > 0
            ? new SolidColorBrush(Color.FromRgb(0xEE, 0x77, 0x77))
            : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        miOthers.Click += (_, _) => CloseSelectedTabs(invert: true);
        cm.Items.Add(miOthers);

        cm.IsOpen = true;
    }
}
