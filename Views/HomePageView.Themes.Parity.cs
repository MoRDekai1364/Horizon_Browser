using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class HomePageView
{
    private sealed class ThemeStyleSpec
    {
        public string ThemeKey { get; init; } = "";
        public string FontFamily { get; init; } = "Segoe UI";
        public Color SurfaceBg { get; init; }
        public Color BorderColor { get; init; }
        public Color AccentColor { get; init; }
        public Color AccentFg { get; init; } = Colors.White;
        public Color TextColor { get; init; } = Colors.White;
        public Color MutedColor { get; init; } = Color.FromRgb(0xB0, 0xB8, 0xC4);
        public double CornerRadius { get; init; } = 12;
        public double ButtonCornerRadius { get; init; } = 8;
        public bool IsLight { get; init; }
    }

    private sealed class ThemeItemEntry
    {
        public string Title { get; init; } = "";
        public string Url { get; init; } = "";
        public bool IsBookmark { get; init; }
        public PinItem? Pin { get; init; }
        public BookmarkItem? Bookmark { get; init; }
    }

    private string _themeListMode = "Favorites"; // "Favorites" or "Bookmarks"
    private readonly HashSet<TextBox> _hookedThemeSearchBoxes = new();
    private readonly Dictionary<string, (Grid Overlay, TextBlock Clock, TextBlock Weather, TextBlock Media, Button PlayBtn, TextBlock Telemetry, Button AdGuardBtn, Button FavTab, Button BkmTab)> _themeOverlays = new();

    private static readonly string[] ThemeSearchEngines = { "Google", "DuckDuckGo", "Bing", "Brave", "Ecosia", "AlohaFind" };

    private static ThemeStyleSpec GetThemeStyleSpec(string theme) => theme switch
    {
        "CyberTerminal" => new ThemeStyleSpec
        {
            ThemeKey = "CyberTerminal",
            FontFamily = "Consolas",
            SurfaceBg = Color.FromArgb(0xEC, 0x0A, 0x0F, 0x16),
            BorderColor = Color.FromRgb(0x00, 0x77, 0x33),
            AccentColor = Color.FromRgb(0x00, 0xFF, 0x66),
            AccentFg = Colors.Black,
            TextColor = Color.FromRgb(0x00, 0xFF, 0x66),
            MutedColor = Color.FromRgb(0x55, 0xAA, 0x77),
            CornerRadius = 4,
            ButtonCornerRadius = 2
        },
        "MetroMatrix" => new ThemeStyleSpec
        {
            ThemeKey = "MetroMatrix",
            FontFamily = "Segoe UI",
            SurfaceBg = Color.FromArgb(0xEE, 0x12, 0x12, 0x18),
            BorderColor = Color.FromRgb(0x00, 0x78, 0xD7),
            AccentColor = Color.FromRgb(0x00, 0x78, 0xD7),
            AccentFg = Colors.White,
            TextColor = Colors.White,
            MutedColor = Color.FromRgb(0xB8, 0xC8, 0xDC),
            CornerRadius = 0,
            ButtonCornerRadius = 0
        },
        "RustNeoCanvas" => new ThemeStyleSpec
        {
            ThemeKey = "RustNeoCanvas",
            FontFamily = "Segoe UI",
            SurfaceBg = Color.FromArgb(0xCC, 0x12, 0x15, 0x1E),
            BorderColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
            AccentColor = Color.FromRgb(0x60, 0xCD, 0xFF),
            AccentFg = Colors.Black,
            TextColor = Colors.White,
            MutedColor = Color.FromRgb(0xCB, 0xD5, 0xE1),
            CornerRadius = 18,
            ButtonCornerRadius = 12
        },
        "RetroCRT" => new ThemeStyleSpec
        {
            ThemeKey = "RetroCRT",
            FontFamily = "Consolas",
            SurfaceBg = Color.FromArgb(0xEE, 0x16, 0x10, 0x06),
            BorderColor = Color.FromRgb(0xFF, 0xB0, 0x00),
            AccentColor = Color.FromRgb(0xFF, 0xB0, 0x00),
            AccentFg = Colors.Black,
            TextColor = Color.FromRgb(0xFF, 0xC9, 0x4D),
            MutedColor = Color.FromRgb(0xB8, 0x82, 0x20),
            CornerRadius = 3,
            ButtonCornerRadius = 2
        },
        "EditorialPaper" => new ThemeStyleSpec
        {
            ThemeKey = "EditorialPaper",
            FontFamily = "Georgia",
            SurfaceBg = Color.FromArgb(0xF6, 0xF5, 0xF0, 0xE6),
            BorderColor = Color.FromRgb(0x8B, 0x00, 0x00),
            AccentColor = Color.FromRgb(0x8B, 0x00, 0x00),
            AccentFg = Colors.White,
            TextColor = Color.FromRgb(0x22, 0x1D, 0x1A),
            MutedColor = Color.FromRgb(0x64, 0x58, 0x50),
            CornerRadius = 4,
            ButtonCornerRadius = 3,
            IsLight = true
        },
        "StarshipLCARS" => new ThemeStyleSpec
        {
            ThemeKey = "StarshipLCARS",
            FontFamily = "Arial",
            SurfaceBg = Color.FromArgb(0xF2, 0x08, 0x08, 0x0C),
            BorderColor = Color.FromRgb(0xFF, 0x99, 0x00),
            AccentColor = Color.FromRgb(0xFF, 0x99, 0x00),
            AccentFg = Colors.Black,
            TextColor = Color.FromRgb(0xFF, 0xCC, 0x66),
            MutedColor = Color.FromRgb(0xCC, 0x99, 0xCC),
            CornerRadius = 18,
            ButtonCornerRadius = 14
        },
        "ZenGarden" => new ThemeStyleSpec
        {
            ThemeKey = "ZenGarden",
            FontFamily = "Segoe UI",
            SurfaceBg = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF),
            BorderColor = Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF),
            AccentColor = Color.FromRgb(0x4F, 0x6F, 0x52),
            AccentFg = Colors.White,
            TextColor = Color.FromRgb(0x1F, 0x2D, 0x23),
            MutedColor = Color.FromRgb(0x4E, 0x5F, 0x52),
            CornerRadius = 22,
            ButtonCornerRadius = 16,
            IsLight = true
        },
        "Win95RetroDesk" => new ThemeStyleSpec
        {
            ThemeKey = "Win95RetroDesk",
            FontFamily = "Tahoma",
            SurfaceBg = Color.FromRgb(0xC0, 0xC0, 0xC0),
            BorderColor = Color.FromRgb(0x40, 0x40, 0x40),
            AccentColor = Color.FromRgb(0x00, 0x00, 0x80),
            AccentFg = Colors.White,
            TextColor = Colors.Black,
            MutedColor = Color.FromRgb(0x33, 0x33, 0x33),
            CornerRadius = 0,
            ButtonCornerRadius = 0,
            IsLight = true
        },
        "ImperialCitadel" => new ThemeStyleSpec
        {
            ThemeKey = "ImperialCitadel",
            FontFamily = "Georgia",
            SurfaceBg = Color.FromArgb(0xEE, 0x1D, 0x16, 0x0F),
            BorderColor = Color.FromRgb(0xC8, 0xA2, 0x4A),
            AccentColor = Color.FromRgb(0x7A, 0x1C, 0x1C),
            AccentFg = Color.FromRgb(0xF5, 0xE2, 0xB0),
            TextColor = Color.FromRgb(0xF0, 0xE4, 0xCC),
            MutedColor = Color.FromRgb(0xBF, 0xA6, 0x7A),
            CornerRadius = 4,
            ButtonCornerRadius = 3
        },
        "CelestialObservatory" => new ThemeStyleSpec
        {
            ThemeKey = "CelestialObservatory",
            FontFamily = "Segoe UI",
            SurfaceBg = Color.FromArgb(0xCC, 0x0B, 0x12, 0x24),
            BorderColor = Color.FromArgb(0x55, 0xD4, 0xAF, 0x37),
            AccentColor = Color.FromRgb(0xD4, 0xAF, 0x37),
            AccentFg = Color.FromRgb(0x09, 0x0E, 0x1C),
            TextColor = Color.FromRgb(0xE8, 0xF0, 0xFF),
            MutedColor = Color.FromRgb(0x9A, 0xB0, 0xD4),
            CornerRadius = 16,
            ButtonCornerRadius = 12
        },
        _ => new ThemeStyleSpec
        {
            ThemeKey = theme,
            SurfaceBg = Color.FromArgb(0xDD, 0x14, 0x16, 0x1E),
            BorderColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
            AccentColor = Color.FromRgb(0x60, 0xCD, 0xFF),
            AccentFg = Colors.Black
        }
    };

    private string GetActiveThemeSearchQuery(TextBox? searchBox)
        => (searchBox?.Text ?? "").Trim();

    private void HookThemeSearchBox(TextBox? box, string themeKey)
    {
        if (box == null || !_hookedThemeSearchBoxes.Add(box)) return;
        box.TextChanged += (_, _) =>
        {
            if (_activeTheme == themeKey)
                RepopulateActiveTheme();
        };
    }

    public void RefreshActiveNonDefaultTheme()
    {
        if (string.IsNullOrEmpty(_activeTheme) || _activeTheme == "Default") return;
        Dispatcher.BeginInvoke(new Action(RepopulateActiveTheme));
    }

    private void RepopulateActiveTheme()
    {
        switch (_activeTheme)
        {
            case "CyberTerminal":  PopulateCyberTerminal(); break;
            case "MetroMatrix":    PopulateMetroMatrix(); break;
            case "RustNeoCanvas":  PopulateRustNeoCanvas(); break;
            case "RetroCRT":       PopulateRetroCRT(); break;
            case "EditorialPaper": PopulateEditorialPaper(); break;
            case "StarshipLCARS":  PopulateStarshipLCARS(); break;
            case "ZenGarden":      PopulateZenGarden(); break;
            case "Win95RetroDesk": PopulateWin95RetroDesk(); break;
            case "ImperialCitadel": PopulateImperialCitadel(); break;
            case "CelestialObservatory": PopulateCelestialObservatory(); break;
        }
    }

    private (List<ThemeItemEntry> Items, bool IsTotalEmpty, string EmptyMessage) GetThemeActiveItems(string query)
    {
        var list = new List<ThemeItemEntry>();
        bool isBkm = _themeListMode == "Bookmarks";

        if (isBkm)
        {
            var allBkm = BookmarkService.Items ?? new List<BookmarkItem>();
            bool totalEmpty = allBkm.Count == 0;
            IEnumerable<BookmarkItem> filtered = allBkm;
            if (query.Length > 0)
            {
                filtered = filtered.Where(b =>
                    (b.Title ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (b.Url ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
            }
            foreach (var b in filtered.Take(48))
            {
                if (string.IsNullOrWhiteSpace(b.Url)) continue;
                list.Add(new ThemeItemEntry
                {
                    Title = string.IsNullOrWhiteSpace(b.Title) ? b.Url : b.Title,
                    Url = b.Url,
                    IsBookmark = true,
                    Bookmark = b
                });
            }
            string emptyMsg = (query.Length > 0 && !totalEmpty)
                ? $"\"{query}\" bookmark not found."
                : "No bookmarks yet. Press Ctrl+D on any page to add one.";
            return (list, totalEmpty, emptyMsg);
        }
        else
        {
            var pins = SettingsService.Current?.PinnedUrls ?? new List<PinItem>();
            bool totalEmpty = pins.Count == 0;
            IEnumerable<PinItem> filtered = pins;
            if (query.Length > 0)
            {
                filtered = filtered.Where(p =>
                    (p.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (p.Url ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
            }
            foreach (var p in filtered.Take(48))
            {
                if (string.IsNullOrWhiteSpace(p.Url)) continue;
                list.Add(new ThemeItemEntry
                {
                    Title = string.IsNullOrWhiteSpace(p.Name) ? p.Url : p.Name,
                    Url = p.Url,
                    IsBookmark = false,
                    Pin = p
                });
            }
            string emptyMsg = (query.Length > 0 && !totalEmpty)
                ? $"\"{query}\" favorite not found."
                : "No favorites yet. Press Ctrl+D on any page to add one.";
            return (list, totalEmpty, emptyMsg);
        }
    }

    private ContextMenu BuildThemeItemContextMenu(ThemeItemEntry entry)
    {
        var menu = new ContextMenu();

        var miOpen = new MenuItem { Header = "Open" };
        miOpen.Click += (_, _) => NavigateRequested?.Invoke(entry.Url);
        menu.Items.Add(miOpen);

        var miCopy = new MenuItem { Header = "Copy URL" };
        miCopy.Click += (_, _) => { try { Clipboard.SetText(entry.Url); } catch { } };
        menu.Items.Add(miCopy);

        menu.Items.Add(new Separator());

        var miRemove = new MenuItem { Header = entry.IsBookmark ? "Delete Bookmark" : "Remove Favorite" };
        miRemove.Click += (_, _) =>
        {
            try
            {
                if (entry.IsBookmark && entry.Bookmark != null)
                {
                    BookmarkService.Remove(entry.Bookmark);
                    RefreshBookmarks();
                }
                else if (entry.Pin != null)
                {
                    SettingsService.Current.PinnedUrls.Remove(entry.Pin);
                    SettingsService.Save();
                    RefreshFavorites();
                }
                RepopulateActiveTheme();
            }
            catch { }
        };
        menu.Items.Add(miRemove);
        return menu;
    }

    private static Image? CreateThemeFaviconImage(string url, double size, UIElement? fallbackToHide = null)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || System.Net.IPAddress.TryParse(uri.Host, out _))
                return null;

            string iconUrl = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(uri.Host)}&sz=64";
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(iconUrl, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = (int)Math.Ceiling(size * 2);
            bmp.EndInit();

            var img = new Image
            {
                Source = bmp,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            void RevealIfReady()
            {
                if (bmp.PixelWidth > 1 && bmp.PixelHeight > 1)
                {
                    img.Visibility = Visibility.Visible;
                    if (fallbackToHide != null) fallbackToHide.Visibility = Visibility.Collapsed;
                }
            }

            if (!bmp.IsDownloading)
            {
                RevealIfReady();
            }
            else
            {
                bmp.DownloadCompleted += (_, _) => RevealIfReady();
                bmp.DownloadFailed += (_, _) =>
                {
                    img.Visibility = Visibility.Collapsed;
                    if (fallbackToHide != null) fallbackToHide.Visibility = Visibility.Visible;
                };
            }

            return img;
        }
        catch
        {
            return null;
        }
    }

    private void PromptAddFavoriteFromTheme()
    {
        try
        {
            var dlg = new Window
            {
                Title = "Add Favorite",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x20)),
                Foreground = Brushes.White
            };

            var sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(new TextBlock { Text = "Site Name", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var txtName = new TextBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x34)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x50, 0x66)), Margin = new Thickness(0, 0, 0, 12) };
            sp.Children.Add(txtName);

            sp.Children.Add(new TextBlock { Text = "URL (e.g. https://github.com)", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var txtUrl = new TextBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x34)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x50, 0x66)), Margin = new Thickness(0, 0, 0, 16) };
            sp.Children.Add(txtUrl);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            btnCancel.Click += (_, _) => dlg.Close();
            var btnAdd = new Button { Content = "Add Favorite", Padding = new Thickness(14, 6, 14, 6), Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
            btnAdd.Click += (_, _) =>
            {
                string rawUrl = (txtUrl.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(rawUrl)) return;
                if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    rawUrl = "https://" + rawUrl;
                string name = (txtName.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = Uri.TryCreate(rawUrl, UriKind.Absolute, out var u) ? u.Host : rawUrl;

                SettingsService.Current.PinnedUrls.Add(new PinItem { Name = name, Url = rawUrl });
                SettingsService.Save();
                _themeListMode = "Favorites";
                RefreshFavorites();
                RepopulateActiveTheme();
                dlg.Close();
            };
            btns.Children.Add(btnCancel);
            btns.Children.Add(btnAdd);
            sp.Children.Add(btns);
            dlg.Content = sp;
            dlg.ShowDialog();
        }
        catch { }
    }

    private void OpenWeatherPopupFromTheme()
    {
        try
        {
            if (Application.Current?.MainWindow is MainWindow mw)
            {
                var method = typeof(MainWindow).GetMethod("OpenWeatherDetailPopup",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(mw, null);
            }
        }
        catch { }
    }

    private void OpenVpnCenterFromTheme()
    {
        try
        {
            var win = new VpnControlCenterWindow { Owner = Window.GetWindow(this) };
            win.Show();
        }
        catch { }
    }

    private void OpenBrowserSettingsFromTheme()
    {
        try
        {
            var win = new SettingsWindow { Owner = Window.GetWindow(this) };
            win.Show();
        }
        catch { }
    }

    /// <summary>
    /// Attaches a theme-styled top Mode/Engine bar (Favorites | Bookmarks | Widgets | + Add | Search Engine)
    /// and a live bottom Utility Dock (Clock | Weather | Media Prev/Play/Next | Battery/RAM | AdGuard | VPN | Wallpaper | Themes | Settings)
    /// into any non-default ThemeHost Grid so every theme is 100% usable.
    /// </summary>
    private void AttachThemeParityShell(Grid? themeHost, string themeKey, TextBox? searchBox)
    {
        if (themeHost == null) return;
        HookThemeSearchBox(searchBox, themeKey);

        var spec = GetThemeStyleSpec(themeKey);

        if (!_themeOverlays.TryGetValue(themeKey, out var entry))
        {
            var overlay = new Grid { IsHitTestVisible = true };
            Panel.SetZIndex(overlay, 50);

            // Span all rows & columns of the ThemeHost so it floats cleanly without shifting existing layout
            int rowCount = Math.Max(1, themeHost.RowDefinitions.Count);
            int colCount = Math.Max(1, themeHost.ColumnDefinitions.Count);
            Grid.SetRow(overlay, 0);
            Grid.SetColumn(overlay, 0);
            Grid.SetRowSpan(overlay, rowCount);
            Grid.SetColumnSpan(overlay, colCount);

            // ── TOP-RIGHT MODE & ENGINE BAR ───────────────────────────────────────
            var topBar = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 12, 18, 0),
                Background = new SolidColorBrush(spec.SurfaceBg),
                BorderBrush = new SolidColorBrush(spec.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(spec.CornerRadius),
                Padding = new Thickness(8, 5, 8, 5)
            };

            var topStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var btnFav = MakeThemeButton("Favorites", spec, active: true);
            var btnBkm = MakeThemeButton("Bookmarks", spec, active: false);
            var btnWidgets = MakeThemeButton("\u25A6 Widgets", spec, active: false);
            var btnAdd = MakeThemeButton("+ Add", spec, active: false);

            btnFav.Click += (_, _) => { _themeListMode = "Favorites"; RepopulateActiveTheme(); };
            btnBkm.Click += (_, _) => { _themeListMode = "Bookmarks"; RepopulateActiveTheme(); };
            btnWidgets.Click += (s, _) => OpenWidgetBoardMenu(s as FrameworkElement);
            btnAdd.Click += (_, _) => PromptAddFavoriteFromTheme();

            var cmbEngine = new ComboBox
            {
                Width = 104,
                Height = 26,
                Margin = new Thickness(6, 0, 0, 0),
                FontFamily = new FontFamily(spec.FontFamily),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Search Engine"
            };
            foreach (string eng in ThemeSearchEngines) cmbEngine.Items.Add(eng);
            string curEng = SettingsService.Current?.SearchEngine ?? "Google";
            cmbEngine.SelectedItem = ThemeSearchEngines.FirstOrDefault(x => x.Equals(curEng, StringComparison.OrdinalIgnoreCase)) ?? "Google";
            cmbEngine.SelectionChanged += (_, _) =>
            {
                if (cmbEngine.SelectedItem is string sel)
                {
                    SettingsService.Current.SearchEngine = sel;
                    SettingsService.Save();
                }
            };

            topStack.Children.Add(btnFav);
            topStack.Children.Add(btnBkm);
            topStack.Children.Add(btnWidgets);
            topStack.Children.Add(btnAdd);
            topStack.Children.Add(cmbEngine);
            topBar.Child = topStack;
            overlay.Children.Add(topBar);

            // ── BOTTOM LIVE UTILITY DOCK ──────────────────────────────────────────
            var bottomBar = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(16, 0, 16, themeKey == "Win95RetroDesk" ? 42 : 14),
                Background = new SolidColorBrush(spec.SurfaceBg),
                BorderBrush = new SolidColorBrush(spec.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(spec.CornerRadius),
                Padding = new Thickness(12, 6, 12, 6)
            };

            var dockStack = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var txtClock = new TextBlock
            {
                FontFamily = new FontFamily(spec.FontFamily),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(spec.AccentColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            dockStack.Children.Add(txtClock);

            var txtWeather = new TextBlock
            {
                FontFamily = new FontFamily(spec.FontFamily),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(spec.TextColor),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0),
                ToolTip = "Click to open Weather Forecast"
            };
            txtWeather.MouseLeftButtonUp += (_, _) => OpenWeatherPopupFromTheme();
            dockStack.Children.Add(txtWeather);

            var btnPrev = MakeThemeButton("\u23EE", spec, false, compact: true);
            btnPrev.ToolTip = "Previous track";
            btnPrev.Click += (_, _) => MediaBridge.SendCommand("PREV");

            var btnPlay = MakeThemeButton("\u25B6", spec, false, compact: true);
            btnPlay.ToolTip = "Play / Pause";
            btnPlay.Click += (_, _) => MediaBridge.SendCommand("PLAYPAUSE");

            var btnNext = MakeThemeButton("\u23ED", spec, false, compact: true);
            btnNext.ToolTip = "Next track";
            btnNext.Click += (_, _) => MediaBridge.SendCommand("NEXT");

            var txtMedia = new TextBlock
            {
                FontFamily = new FontFamily(spec.FontFamily),
                FontSize = 11,
                Foreground = new SolidColorBrush(spec.MutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 10, 0)
            };
            dockStack.Children.Add(btnPrev);
            dockStack.Children.Add(btnPlay);
            dockStack.Children.Add(btnNext);
            dockStack.Children.Add(txtMedia);

            var txtTelemetry = new TextBlock
            {
                FontFamily = new FontFamily(spec.FontFamily),
                FontSize = 11,
                Foreground = new SolidColorBrush(spec.MutedColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            dockStack.Children.Add(txtTelemetry);

            var btnAdGuard = MakeThemeButton("AdGuard", spec, SettingsService.Current.AdGuardEnabled);
            btnAdGuard.Click += (_, _) =>
            {
                SettingsService.Current.AdGuardEnabled = !SettingsService.Current.AdGuardEnabled;
                SettingsService.Save();
                UpdateThemeParityLiveState(themeKey);
            };
            dockStack.Children.Add(btnAdGuard);

            var btnVpn = MakeThemeButton("VPN", spec, false);
            btnVpn.Click += (_, _) => OpenVpnCenterFromTheme();
            dockStack.Children.Add(btnVpn);

            var btnUtilities = MakeThemeButton("\u26A1 Utilities", spec, false);
            btnUtilities.Click += (s, e) => BtnBottomUtilities_Click(s, e);
            dockStack.Children.Add(btnUtilities);

            var btnWall = MakeThemeButton("Wallpaper", spec, false);
            btnWall.Click += (s, e) => BtnChangeWallpaper_Click(s, e);
            dockStack.Children.Add(btnWall);

            var btnThemes = MakeThemeButton("Themes", spec, false);
            btnThemes.Click += (s, e) => BtnHomeSettings_Click(s, e);
            dockStack.Children.Add(btnThemes);

            var btnSettings = MakeThemeButton("\u2699 Settings", spec, false);
            btnSettings.Click += (_, _) => OpenBrowserSettingsFromTheme();
            dockStack.Children.Add(btnSettings);

            bottomBar.Child = dockStack;
            overlay.Children.Add(bottomBar);

            themeHost.Children.Add(overlay);
            entry = (overlay, txtClock, txtWeather, txtMedia, btnPlay, txtTelemetry, btnAdGuard, btnFav, btnBkm);
            _themeOverlays[themeKey] = entry;
        }

        UpdateThemeParityLiveState(themeKey);
    }

    private void UpdateThemeParityLiveState(string themeKey)
    {
        if (!_themeOverlays.TryGetValue(themeKey, out var ui)) return;
        var spec = GetThemeStyleSpec(themeKey);

        int favCount = SettingsService.Current?.PinnedUrls?.Count ?? 0;
        int bkmCount = BookmarkService.Items?.Count ?? 0;
        ui.FavTab.Content = $"Favorites ({favCount})";
        ui.BkmTab.Content = $"Bookmarks ({bkmCount})";
        StyleThemeButton(ui.FavTab, spec, active: _themeListMode == "Favorites");
        StyleThemeButton(ui.BkmTab, spec, active: _themeListMode == "Bookmarks");

        ui.Clock.Text = DateTime.Now.ToString("HH:mm:ss");
        ui.Weather.Text = "\u26C5 " + (string.IsNullOrEmpty(WeatherBridge.WeatherText) ? "Weather" : WeatherBridge.WeatherText);
        ui.PlayBtn.Content = MediaBridge.IsPlaying ? "\u23F8" : "\u25B6";
        ui.Media.Text = string.IsNullOrEmpty(MediaBridge.Title) ? "No media" : MediaBridge.Title;

        try
        {
            long ramMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            string bat = BatteryBridge.HasBattery ? $" | \U0001F50B {BatteryBridge.Percent}%" : "";
            ui.Telemetry.Text = $"RAM {ramMb}MB{bat}";
        }
        catch { }

        bool ag = SettingsService.Current.AdGuardEnabled;
        ui.AdGuardBtn.Content = ag ? "\U0001F6E1 AdGuard ON" : "\U0001F6E1 AdGuard OFF";
        StyleThemeButton(ui.AdGuardBtn, spec, active: ag);
    }

    private static Button MakeThemeButton(string text, ThemeStyleSpec spec, bool active, bool compact = false)
    {
        var btn = new Button
        {
            Content = text,
            FontFamily = new FontFamily(spec.FontFamily),
            FontSize = compact ? 11 : 11.5,
            FontWeight = FontWeights.SemiBold,
            Padding = compact ? new Thickness(6, 3, 6, 3) : new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 5, 0),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1)
        };
        StyleThemeButton(btn, spec, active);

        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(spec.ButtonCornerRadius));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(cp);

        btn.Template = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        return btn;
    }

    private static void StyleThemeButton(Button btn, ThemeStyleSpec spec, bool active)
    {
        if (active)
        {
            btn.Background = new SolidColorBrush(spec.AccentColor);
            btn.Foreground = new SolidColorBrush(spec.AccentFg);
            btn.BorderBrush = new SolidColorBrush(spec.AccentColor);
        }
        else
        {
            btn.Background = new SolidColorBrush(spec.IsLight
                ? Color.FromArgb(0x14, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            btn.Foreground = new SolidColorBrush(spec.TextColor);
            btn.BorderBrush = new SolidColorBrush(spec.IsLight
                ? Color.FromArgb(0x38, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }
    }

    private static TextBlock MakeThemeEmptyNotice(string message, ThemeStyleSpec spec)
    {
        return new TextBlock
        {
            Text = message,
            FontFamily = new FontFamily(spec.FontFamily),
            FontSize = 13,
            Foreground = new SolidColorBrush(spec.MutedColor),
            Margin = new Thickness(8, 12, 8, 12),
            TextWrapping = TextWrapping.Wrap
        };
    }
}
