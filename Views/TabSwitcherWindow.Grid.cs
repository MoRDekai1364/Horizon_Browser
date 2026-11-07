using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth.Views;

public partial class TabSwitcherWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    private const double TileWidth  = 270;
    private const double TileHeight = 190;
    private const double TileMargin = 8;
    private const double OuterMargin = 0;
    private const double BleedPad = 48;

    private bool _gridMode;
    private UIElement? _listContent;
    private ScrollViewer? _gridScroll;
    private WrapPanel? _gridPanel;
    private readonly Dictionary<TabViewModel, Image> _gridThumbs = new();
    private int _gridBuildVersion;

    private BitmapSource? _snapshot;
    private Border? _snapshotWall;
    private Rect _screenBoundsPx = new Rect(0, 0, 0, 0);
    private Rect _workAreaPx = new Rect(0, 0, 0, 0);
    private double _dpiScale = 1.0;

    internal static BitmapSource? CaptureScreenArea(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;

        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            hBitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero) return null;

            IntPtr old = SelectObject(memDc, hBitmap);
            bool ok = BitBlt(memDc, 0, 0, width, height, screenDc, x, y, 0x00CC0020u | 0x40000000u);
            SelectObject(memDc, old);
            if (!ok) return null;

            BitmapSource full = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var half = new TransformedBitmap(full, new ScaleTransform(0.5, 0.5));
            half.Freeze();
            return half;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void AttachSnapshotBackdrop()
    {
        var mainWin = Application.Current?.MainWindow;
        IntPtr hwnd = mainWin != null ? new WindowInteropHelper(mainWin).Handle : IntPtr.Zero;

        try
        {
            var screen = hwnd != IntPtr.Zero
                ? System.Windows.Forms.Screen.FromHandle(hwnd)
                : (System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0]);
            var bounds = screen.Bounds;
            var work = screen.WorkingArea;
            _screenBoundsPx = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            _workAreaPx = new Rect(work.X, work.Y, work.Width, work.Height);
            _dpiScale = mainWin != null ? VisualTreeHelper.GetDpi(mainWin).DpiScaleX : 1.0;
        }
        catch
        {
            _screenBoundsPx = new Rect(0, 0, 0, 0);
            _workAreaPx = new Rect(0, 0, 0, 0);
            _dpiScale = 1.0;
        }

        if (SettingsService.Current.DisableBlurEffects)
        {
            CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x12, 0x12));
            return;
        }

        if (_screenBoundsPx.Width <= 0 || _screenBoundsPx.Height <= 0) return;

        _snapshot = CaptureScreenArea(
            (int)_screenBoundsPx.X, (int)_screenBoundsPx.Y,
            (int)_screenBoundsPx.Width, (int)_screenBoundsPx.Height);
        if (_snapshot == null) return;

        CardBaseBg.Background = Brushes.Transparent;

        _snapshotWall = new Border
        {
            Margin = new Thickness(-BleedPad),
            Background = new ImageBrush(_snapshot)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox
            },
            Effect = new BlurEffect
            {
                Radius = 36,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            }
        };

        var tint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xA6, 0x0E, 0x0E, 0x0E))
        };

        var host = new Grid();
        host.Children.Add(_snapshotWall);
        host.Children.Add(tint);
        GlassBackdropHost.Children.Add(host);

        _updateGlassAction = UpdateSnapshotCrop;
        LocationChanged += (_, _) => UpdateSnapshotCrop();
        _detachGlassBackdrop = () =>
        {
            _updateGlassAction = null;
            _snapshot = null;
            _snapshotWall = null;
        };
    }

    private void UpdateSnapshotCrop()
    {
        if (_snapshotWall?.Background is not ImageBrush brush) return;
        if (_screenBoundsPx.Width <= 0 || _screenBoundsPx.Height <= 0) return;
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;

        double sc = _dpiScale <= 0 ? 1.0 : _dpiScale;
        double cardW = CardClipHost.ActualWidth  > 0 ? CardClipHost.ActualWidth  : Math.Max(1.0, Width  - 2.0 * OuterMargin);
        double cardH = CardClipHost.ActualHeight > 0 ? CardClipHost.ActualHeight : Math.Max(1.0, Height - 2.0 * OuterMargin);

        double leftPx = (Left + OuterMargin - BleedPad) * sc - _screenBoundsPx.X;
        double topPx  = (Top  + OuterMargin - BleedPad) * sc - _screenBoundsPx.Y;
        double wPx    = (cardW + 2.0 * BleedPad) * sc;
        double hPx    = (cardH + 2.0 * BleedPad) * sc;

        double x0 = Math.Clamp(leftPx / _screenBoundsPx.Width, 0.0, 1.0);
        double x1 = Math.Clamp((leftPx + wPx) / _screenBoundsPx.Width, 0.0, 1.0);
        double y0 = Math.Clamp(topPx / _screenBoundsPx.Height, 0.0, 1.0);
        double y1 = Math.Clamp((topPx + hPx) / _screenBoundsPx.Height, 0.0, 1.0);

        if (x1 - x0 < 0.001 || y1 - y0 < 0.001) return;
        brush.Viewbox = new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    private void ApplyGeometry()
    {
        double sc = _dpiScale <= 0 ? 1.0 : _dpiScale;
        double workW = _workAreaPx.Width / sc;
        double workH = _workAreaPx.Height / sc;

        double w = _gridMode ? 1280 : 1000;
        double h = _gridMode ? 760 : 580;

        if (workW > 0 && workH > 0)
        {
            w = Math.Min(w, workW);
            h = Math.Min(h, workH);
            Width = w;
            Height = h;
            Left = _workAreaPx.X / sc + (workW - w) / 2.0;
            Top = _workAreaPx.Y / sc + (workH - h) / 2.0;
        }
        else
        {
            Width = w;
            Height = h;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        UpdateSnapshotCrop();
    }

    private void InitGridMode()
    {
        var content = RootDock.Children[RootDock.Children.Count - 1];
        RootDock.Children.Remove(content);

        _gridPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(6)
        };
        _gridScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Padding = new Thickness(6),
            Content = _gridPanel,
            Visibility = Visibility.Collapsed
        };

        var host = new Grid();
        host.Children.Add(content);
        host.Children.Add(_gridScroll);
        RootDock.Children.Add(host);
        _listContent = content;

        _gridMode = SettingsService.Current.TabSwitcherGridMode;
        Loaded += (_, _) => TxtHintBar.Text += "  \u00B7  Ctrl+G: layout";
        ApplyMode();
    }

    private void ApplyMode()
    {
        if (_listContent == null || _gridScroll == null) return;

        _listContent.Visibility = _gridMode ? Visibility.Collapsed : Visibility.Visible;
        _gridScroll.Visibility  = _gridMode ? Visibility.Visible   : Visibility.Collapsed;
        BtnModeToggle.Content   = _gridMode ? "List" : "Grid";
        BtnModeToggle.ToolTip   = _gridMode ? "Switch to list layout (Ctrl+G)" : "Switch to grid layout (Ctrl+G)";

        ApplyGeometry();

        if (_gridMode)
        {
            RebuildGrid();
            ScrollGridTileIntoView(_selectedIndex);
        }
        else if (_visibleTabs.Count > 0)
        {
            _ = LoadPreviewAsync(_selectedIndex);
        }
    }

    private void ToggleMode()
    {
        _gridMode = !_gridMode;
        ApplyMode();
    }

    private void BtnModeToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleMode();
    }

    private void BtnModeToggle_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        bool defaultGrid = SettingsService.Current.TabSwitcherGridMode;
        var cm = MakeDarkContextMenu(BtnModeToggle);

        var miList = MakeDarkMenuItem((defaultGrid ? "\u2003" : "\u2713") + "  Set List as default");
        miList.Click += (_, _) => SetDefaultMode(false);
        cm.Items.Add(miList);

        var miGrid = MakeDarkMenuItem((defaultGrid ? "\u2713" : "\u2003") + "  Set Grid as default");
        miGrid.Click += (_, _) => SetDefaultMode(true);
        cm.Items.Add(miGrid);

        cm.IsOpen = true;
    }

    private void SetDefaultMode(bool grid)
    {
        SettingsService.Current.TabSwitcherGridMode = grid;
        SettingsService.Save();

        if (_gridMode != grid)
        {
            _gridMode = grid;
            ApplyMode();
        }
    }

    private int GridColumns()
    {
        double w = _gridScroll != null && _gridScroll.ViewportWidth > 0
            ? _gridScroll.ViewportWidth
            : Math.Max(1.0, ActualWidth - 2.0 * OuterMargin);
        return Math.Max(1, (int)Math.Floor((w - 24.0) / (TileWidth + 2.0 * TileMargin)));
    }

    private bool HandleModeKeys(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (e.Key == Key.G && ctrl)
        {
            ToggleMode();
            e.Handled = true;
            return true;
        }

        if (_gridMode && (e.Key == Key.Up || e.Key == Key.Down))
        {
            if (_visibleTabs.Count > 0)
            {
                int cols = GridColumns();
                int target = _selectedIndex + (e.Key == Key.Down ? cols : -cols);
                if (target >= 0 && target < _visibleTabs.Count) SelectIndex(target);
            }
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void RebuildGridIfActive()
    {
        if (_gridMode && _gridPanel != null) RebuildGrid();
    }

    private void RebuildGrid()
    {
        if (_gridPanel == null) return;

        _gridPanel.Children.Clear();
        _gridThumbs.Clear();
        int version = ++_gridBuildVersion;

        for (int i = 0; i < _visibleTabs.Count; i++)
            _gridPanel.Children.Add(BuildTile(_visibleTabs[i], i));

        HighlightGridTiles();
        _ = LoadGridThumbnailsAsync(version);
    }

    private Border BuildTile(TabViewModel tab, int index)
    {
        var tile = new Border
        {
            Tag             = index,
            Width           = TileWidth,
            Height          = TileHeight,
            Margin          = new Thickness(TileMargin),
            Padding         = new Thickness(8),
            CornerRadius    = new CornerRadius(10),
            Cursor          = Cursors.Hand,
            Background      = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1.5)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Height = 22 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = BuildTileIcon(tab);
        Grid.SetColumn(icon, 0);
        header.Children.Add(icon);

        var title = new TextBlock
        {
            Text              = tab.DisplayTitle,
            Foreground        = Brushes.White,
            FontFamily        = new FontFamily("Segoe UI"),
            FontSize          = 12,
            FontWeight        = FontWeights.SemiBold,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 4, 0)
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (tab.IsPlayingAudio) badges.Children.Add(Badge(tab.HasVideo ? "\U0001F3AC" : "\U0001F50A"));
        if (tab.IsMuted)        badges.Children.Add(Badge("\U0001F507"));
        if (tab.IsSleeping)     badges.Children.Add(Badge("\U0001F4A4"));
        if (tab.IsLoading)      badges.Children.Add(Badge("\u27F3", Color.FromRgb(0x88, 0xCC, 0xFF)));
        if (tab.IsActiveDownload) badges.Children.Add(Badge("\u2B07"));
        Grid.SetColumn(badges, 2);
        header.Children.Add(badges);

        var closeBtn = new Button
        {
            Content           = "\u2715",
            FontSize          = 10,
            Width             = 18,
            Height            = 18,
            Padding           = new Thickness(0),
            Margin            = new Thickness(4, 0, 0, 0),
            Background        = Brushes.Transparent,
            BorderThickness   = new Thickness(0),
            Foreground        = new SolidColorBrush(Color.FromRgb(0xEE, 0x66, 0x66)),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor            = Cursors.Hand,
            Opacity           = 0.0,
            ToolTip           = "Close tab (Del)"
        };
        closeBtn.Click += (_, e) => { e.Handled = true; CloseTabFromSwitcher(tab); };
        Grid.SetColumn(closeBtn, 3);
        header.Children.Add(closeBtn);

        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var thumbImage = new Image
        {
            Stretch             = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(thumbImage, BitmapScalingMode.HighQuality);
        if (tab.Thumbnail != null) thumbImage.Source = tab.Thumbnail;
        _gridThumbs[tab] = thumbImage;

        var thumbHost = new Border
        {
            Margin       = new Thickness(0, 8, 0, 0),
            CornerRadius = new CornerRadius(6),
            Background   = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00)),
            ClipToBounds = true,
            Child        = thumbImage
        };
        Grid.SetRow(thumbHost, 1);
        layout.Children.Add(thumbHost);

        tile.Child = layout;

        tile.MouseEnter += (_, _) =>
        {
            int idx = (int)tile.Tag;
            if (idx != _selectedIndex && !_multiSelected.Contains(idx))
                tile.Background = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
            closeBtn.Opacity = 1.0;
        };
        tile.MouseLeave += (_, _) =>
        {
            HighlightGridTiles();
            closeBtn.Opacity = 0.0;
        };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            HandleTileClick((int)tile.Tag);
        };
        tile.MouseRightButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            e.Handled = true;
            int idx = (int)tile.Tag;
            if (_multiSelected.Count >= 2 && _multiSelected.Contains(idx))
                ShowSwitcherMultiContextMenu(tile);
            else
                ShowSwitcherTabContextMenu(tile, tab);
        };

        return tile;
    }

    private static FrameworkElement BuildTileIcon(TabViewModel tab)
    {
        ImageSource? icon = null;
        try
        {
            var conv = new Horizon.Stealth.Controls.FaviconConverter();
            var iconObj = conv.Convert(tab.Url, typeof(ImageSource), string.Empty, System.Globalization.CultureInfo.InvariantCulture);
            if (iconObj is ImageSource src)
            {
                icon = src;
            }
            else if (iconObj is string iconUrl && Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.DecodePixelWidth = 24;
                bmp.EndInit();
                icon = bmp;
            }
        }
        catch { }

        if (icon != null)
        {
            return new Image
            {
                Source            = icon,
                Width             = 18,
                Height            = 18,
                Stretch           = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        string label = (!string.IsNullOrWhiteSpace(tab.DomainTitle) ? tab.DomainTitle : (!string.IsNullOrWhiteSpace(tab.DisplayTitle) ? tab.DisplayTitle : "H")).Trim();
        string initial = label.Length > 0 ? label.Substring(0, 1).ToUpperInvariant() : "H";
        Color[] palette =
        {
            Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x05, 0x96, 0x69),
            Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0xDB, 0x27, 0x77),
            Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0x08, 0x91, 0xB2)
        };
        Color c = palette[Math.Abs(label.GetHashCode()) % palette.Length];

        return new Border
        {
            Width             = 18,
            Height            = 18,
            CornerRadius      = new CornerRadius(5),
            Background        = new SolidColorBrush(Color.FromArgb(0x55, c.R, c.G, c.B)),
            BorderBrush       = new SolidColorBrush(Color.FromArgb(0x88, c.R, c.G, c.B)),
            BorderThickness   = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text                = initial,
                FontFamily          = new FontFamily("Segoe UI"),
                FontWeight          = FontWeights.Bold,
                FontSize            = 10,
                Foreground          = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            }
        };
    }

    private void HandleTileClick(int index)
    {
        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

        if (shift)
        {
            if (!ctrl) _multiSelected.Clear();
            int anchor = _anchorIndex >= 0 ? _anchorIndex : _selectedIndex;
            for (int j = Math.Min(anchor, index); j <= Math.Max(anchor, index); j++) _multiSelected.Add(j);
            _selectedIndex = index;
            HighlightRow(_selectedIndex, null);
            UpdateActionBar();
            return;
        }

        if (ctrl)
        {
            if (!_multiSelected.Remove(index))
            {
                _multiSelected.Add(index);
                _anchorIndex = index;
            }
            _selectedIndex = index;
            HighlightRow(_selectedIndex, null);
            UpdateActionBar();
            return;
        }

        _multiSelected.Clear();
        _anchorIndex   = index;
        _selectedIndex = index;
        UpdateActionBar();
        SelectIndex(index);
        Commit();
    }

    private void HighlightGridTiles()
    {
        if (_gridPanel == null) return;

        foreach (var child in _gridPanel.Children)
        {
            if (child is not Border tile || tile.Tag is not int idx) continue;

            bool isCursor   = idx == _selectedIndex;
            bool isSelected = _multiSelected.Contains(idx);

            if (isCursor)
            {
                tile.Background  = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0xCC, 0x66));
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0xFF, 0x88));
            }
            else if (isSelected)
            {
                tile.Background  = new SolidColorBrush(Color.FromArgb(0x28, 0x33, 0x88, 0xFF));
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x44, 0xAA, 0xFF));
            }
            else
            {
                tile.Background  = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            }
        }
    }

    private void ScrollGridTileIntoView(int index)
    {
        if (_gridPanel == null || index < 0 || index >= _gridPanel.Children.Count) return;
        if (_gridPanel.Children[index] is FrameworkElement el) el.BringIntoView();
    }

    private void UpdateGridThumbnail(TabViewModel tab, ImageSource thumbnail)
    {
        if (_gridThumbs.TryGetValue(tab, out var img)) img.Source = thumbnail;
    }

    private async Task LoadGridThumbnailsAsync(int version)
    {
        var pending = _visibleTabs.ToList();
        foreach (var tab in pending)
        {
            if (_committed || version != _gridBuildVersion) return;
            if (!_gridThumbs.TryGetValue(tab, out var img) || img.Source != null) continue;

            try
            {
                ImageSource? src = tab.Thumbnail;
                if (src == null) src = await _captureFunc(tab);
                if (_committed || version != _gridBuildVersion) return;
                if (src != null) img.Source = src;
            }
            catch { }
        }
    }
}
