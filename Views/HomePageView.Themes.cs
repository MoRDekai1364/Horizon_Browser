using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Horizon.Stealth.Core;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class HomePageView : UserControl
{
    private string _activeTheme = "Default";

    public void ApplyHomePageTheme(string? themeName = null)
    {
        themeName ??= SettingsService.Current?.HomePageTheme ?? "Default";
        _activeTheme = themeName;

        // Toggle theme hosts
        if (GridHomeLayout != null)
            GridHomeLayout.Visibility = (themeName == "Default") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_CyberTerminal != null)
            ThemeHost_CyberTerminal.Visibility = (themeName == "CyberTerminal") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_MetroMatrix != null)
            ThemeHost_MetroMatrix.Visibility = (themeName == "MetroMatrix") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_RustNeoCanvas != null)
            ThemeHost_RustNeoCanvas.Visibility = (themeName == "RustNeoCanvas") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_RetroCRT != null)
            ThemeHost_RetroCRT.Visibility = (themeName == "RetroCRT") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_EditorialPaper != null)
            ThemeHost_EditorialPaper.Visibility = (themeName == "EditorialPaper") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_StarshipLCARS != null)
            ThemeHost_StarshipLCARS.Visibility = (themeName == "StarshipLCARS") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_ZenGarden != null)
            ThemeHost_ZenGarden.Visibility = (themeName == "ZenGarden") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_Win95RetroDesk != null)
            ThemeHost_Win95RetroDesk.Visibility = (themeName == "Win95RetroDesk") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_ImperialCitadel != null)
            ThemeHost_ImperialCitadel.Visibility = (themeName == "ImperialCitadel") ? Visibility.Visible : Visibility.Collapsed;
        if (ThemeHost_CelestialObservatory != null)
            ThemeHost_CelestialObservatory.Visibility = (themeName == "CelestialObservatory") ? Visibility.Visible : Visibility.Collapsed;

        // Hide default bottom docks when any alternate theme is active so they never overlap the theme's own dock
        bool isDefault = (themeName == "Default");
        if (PnlBottomRightButtons != null)
            PnlBottomRightButtons.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;
        if (PnlBottomLeftButtons != null)
            PnlBottomLeftButtons.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;

        switch (themeName)
        {
            case "CyberTerminal":
                PopulateCyberTerminal();
                break;
            case "MetroMatrix":
                PopulateMetroMatrix();
                break;
            case "RustNeoCanvas":
                PopulateRustNeoCanvas();
                break;
            case "RetroCRT":
                PopulateRetroCRT();
                break;
            case "EditorialPaper":
                PopulateEditorialPaper();
                break;
            case "StarshipLCARS":
                PopulateStarshipLCARS();
                break;
            case "ZenGarden":
                PopulateZenGarden();
                break;
            case "Win95RetroDesk":
                PopulateWin95RetroDesk();
                break;
            case "ImperialCitadel":
                PopulateImperialCitadel();
                break;
            case "CelestialObservatory":
                PopulateCelestialObservatory();
                break;
            case "Default":
            default:
                UpdateSearchAreaLayout();
                break;
        }
    }

    private List<(string Title, string Url)> GetThemeShortcuts()
    {
        var list = new List<(string Title, string Url)>();
        var pins = SettingsService.Current?.PinnedUrls;
        if (pins != null && pins.Count > 0)
        {
            foreach (var p in pins)
            {
                if (!string.IsNullOrWhiteSpace(p.Url))
                    list.Add((string.IsNullOrWhiteSpace(p.Name) ? p.Url : p.Name, p.Url));
            }
        }

        if (list.Count == 0)
        {
            list.Add(("YouTube", "https://www.youtube.com"));
            list.Add(("GitHub", "https://github.com"));
            list.Add(("Reddit", "https://www.reddit.com"));
            list.Add(("Wikipedia", "https://www.wikipedia.org"));
            list.Add(("Twitter / X", "https://x.com"));
            list.Add(("Discord", "https://discord.com"));
        }

        return list;
    }

    private void ExecuteThemeSearch(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        string url = Horizon.Stealth.Controls.Omnibox.BuildNavigationUrl(query);
        NavigateRequested?.Invoke(url);
    }

    public void UpdateThemeClocks()
    {
        var now = DateTime.Now;
        if (TxtCyberClock != null && ThemeHost_CyberTerminal?.Visibility == Visibility.Visible)
            TxtCyberClock.Text = $"[CLOCK: {now:HH:mm:ss}]";
        if (TxtRustClock != null && ThemeHost_RustNeoCanvas?.Visibility == Visibility.Visible)
            TxtRustClock.Text = $"{now:HH:mm}";
        if (TxtCrtScore != null && ThemeHost_RetroCRT?.Visibility == Visibility.Visible)
            TxtCrtScore.Text = $"{now:HHmmss}";
        if (TxtLcarsStardate != null && ThemeHost_StarshipLCARS?.Visibility == Visibility.Visible)
            TxtLcarsStardate.Text = $"STARDATE: {now:yy}{now.DayOfYear}.{now.Hour}";
        if (TxtZenClock != null && ThemeHost_ZenGarden?.Visibility == Visibility.Visible)
            TxtZenClock.Text = $"{now:HH:mm}";
        if (TxtWin95Clock != null && ThemeHost_Win95RetroDesk?.Visibility == Visibility.Visible)
            TxtWin95Clock.Text = $"{now:t}";
        if (TxtImperialClock != null && ThemeHost_ImperialCitadel?.Visibility == Visibility.Visible)
            TxtImperialClock.Text = $"{now:HH:mm:ss}";
        if (TxtCelestialClock != null && ThemeHost_CelestialObservatory?.Visibility == Visibility.Visible)
            TxtCelestialClock.Text = $"{now:HH:mm}";

        if (!string.IsNullOrEmpty(_activeTheme) && _activeTheme != "Default")
            UpdateThemeParityLiveState(_activeTheme);
    }

    // =========================================================================
    // THEME 2: CYBER TERMINAL
    // =========================================================================
    private void PopulateCyberTerminal()
    {
        AttachThemeParityShell(ThemeHost_CyberTerminal, "CyberTerminal", TxtCyberSearch);

        if (TxtCyberClock != null)
            TxtCyberClock.Text = $"[CLOCK: {DateTime.Now:HH:mm:ss}]";

        if (TxtCyberTelemetry != null)
        {
            try
            {
                long memMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                TxtCyberTelemetry.Text = $"[RAM: {memMb}MB // AEGIS ACTIVE]";
            }
            catch
            {
                TxtCyberTelemetry.Text = "[RAM: 280MB // AEGIS ACTIVE]";
            }
        }

        if (TxtCyberWeather != null)
        {
            string wx = !string.IsNullOrEmpty(WeatherBridge.WeatherText) ? WeatherBridge.WeatherText : "Weather";
            TxtCyberWeather.Text = $"[WX: {wx.ToUpperInvariant()}]";
        }

        if (PnlCyberShortcuts != null)
        {
            PnlCyberShortcuts.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtCyberSearch));
            if (items.Count == 0)
            {
                PnlCyberShortcuts.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("CyberTerminal")));
                return;
            }
            int index = 1;
            foreach (var sc in items)
            {
                var btn = new Button
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x10, 0x17)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x66)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0x22)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 10, 10),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Cursor = Cursors.Hand,
                    Content = $"[{index}] {sc.Title.ToUpperInvariant()} -> {sc.Url}",
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };
                string targetUrl = sc.Url;
                btn.Click += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlCyberShortcuts.Children.Add(btn);
                index++;
            }
        }
    }

    private void CyberSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtCyberSearch?.Text);
            e.Handled = true;
        }
    }

    private void BtnCyberSearch_Click(object sender, RoutedEventArgs e)
    {
        ExecuteThemeSearch(TxtCyberSearch?.Text);
    }

    // =========================================================================
    // THEME 3: METRO MATRIX
    // =========================================================================
    private static readonly Color[] MetroTileColors = new[]
    {
        Color.FromRgb(0x00, 0x78, 0xD7), // Windows Blue
        Color.FromRgb(0x10, 0x7C, 0x41), // Xbox Green
        Color.FromRgb(0xD8, 0x3B, 0x01), // Orange
        Color.FromRgb(0x88, 0x17, 0x98), // Purple
        Color.FromRgb(0xE8, 0x11, 0x23), // Red
        Color.FromRgb(0x00, 0x82, 0x72), // Teal
        Color.FromRgb(0x00, 0x4E, 0x8C), // Navy
        Color.FromRgb(0xB4, 0x00, 0x9E)  // Magenta
    };

    private void PopulateMetroMatrix()
    {
        AttachThemeParityShell(ThemeHost_MetroMatrix, "MetroMatrix", TxtMetroSearch);

        if (TxtMetroWeatherTemp != null && !string.IsNullOrEmpty(WeatherBridge.WeatherText))
            TxtMetroWeatherTemp.Text = WeatherBridge.WeatherText;

        if (TxtMetroMediaTitle != null)
            TxtMetroMediaTitle.Text = string.IsNullOrEmpty(MediaBridge.Title) ? "Horizon Audio Engine" : MediaBridge.Title;
        if (TxtMetroMediaArtist != null)
            TxtMetroMediaArtist.Text = string.IsNullOrEmpty(MediaBridge.Artist) ? "Ready to play" : MediaBridge.Artist;

        if (PnlMetroShortcuts != null)
        {
            PnlMetroShortcuts.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtMetroSearch));
            if (items.Count == 0)
            {
                PnlMetroShortcuts.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("MetroMatrix")));
                return;
            }
            int colorIdx = 0;
            foreach (var sc in items)
            {
                var bg = MetroTileColors[colorIdx % MetroTileColors.Length];
                colorIdx++;

                var border = new Border
                {
                    Width = 140,
                    Height = 100,
                    Background = new SolidColorBrush(bg),
                    Margin = new Thickness(0, 0, 10, 10),
                    CornerRadius = new CornerRadius(0),
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url,
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };

                var grid = new Grid { Margin = new Thickness(10) };
                var favImg = CreateThemeFaviconImage(sc.Url, 28);
                if (favImg != null)
                {
                    favImg.HorizontalAlignment = HorizontalAlignment.Right;
                    favImg.VerticalAlignment = VerticalAlignment.Top;
                    grid.Children.Add(favImg);
                }
                var txt = new TextBlock
                {
                    Text = sc.Title,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                grid.Children.Add(txt);
                border.Child = grid;

                string targetUrl = sc.Url;
                border.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlMetroShortcuts.Children.Add(border);
            }
        }
    }

    private void MetroSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtMetroSearch?.Text);
            e.Handled = true;
        }
    }

    private void BtnMetroSearch_Click(object sender, RoutedEventArgs e)
    {
        ExecuteThemeSearch(TxtMetroSearch?.Text);
    }

    // =========================================================================
    // THEME 4: RUST NEO-CANVAS
    // =========================================================================
    private void PopulateRustNeoCanvas()
    {
        AttachThemeParityShell(ThemeHost_RustNeoCanvas, "RustNeoCanvas", TxtRustSpotlightSearch);

        if (TxtRustClock != null)
            TxtRustClock.Text = DateTime.Now.ToString("HH:mm");

        if (TxtRustWeather != null)
        {
            string wx = !string.IsNullOrEmpty(WeatherBridge.WeatherText) ? WeatherBridge.WeatherText : "Weather";
            TxtRustWeather.Text = wx;
        }

        if (CanvasRustDock != null)
        {
            CanvasRustDock.Children.Clear();
            var (shortcuts, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtRustSpotlightSearch));
            if (shortcuts.Count == 0)
            {
                var notice = MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("RustNeoCanvas"));
                CanvasRustDock.Children.Add(notice);
                CanvasRustDock.Width = 360;
                CanvasRustDock.Height = 48;
                return;
            }

            // Target card dimensions matching original homepage (77x77 icon + label)
            const double itemW = 96.0;
            const double itemH = 112.0;
            const double targetGap = 16.0;

            // Dynamic container width based on viewport
            double availableW = Math.Clamp(RootHomeGrid?.ActualWidth > 0 ? RootHomeGrid.ActualWidth * 0.65 : 780.0, 480.0, 960.0);

            // Compute 2D grid positions natively in Rust horizon_core.dll
            var rects = HorizonCore.ComputeHomepageLayout(availableW, 4000.0, shortcuts.Count, itemW, itemH, targetGap);
            if (rects.Length == 0) return;

            double minX = double.MaxValue;
            double maxRight = 0;
            double maxBottom = 0;
            for (int i = 0; i < rects.Length && i < shortcuts.Count; i++)
            {
                if (rects[i].X < minX) minX = rects[i].X;
                if (rects[i].X + rects[i].Width > maxRight) maxRight = rects[i].X + rects[i].Width;
                if (rects[i].Y + rects[i].Height > maxBottom) maxBottom = rects[i].Y + rects[i].Height;
            }
            if (minX == double.MaxValue) minX = 0;

            for (int i = 0; i < rects.Length && i < shortcuts.Count; i++)
            {
                var rect = rects[i];
                var sc = shortcuts[i];

                var itemPanel = new StackPanel
                {
                    Width = itemW,
                    Height = itemH,
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url,
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };

                // 77x77 frosted capsule icon box matching original homepage
                var iconBox = new Border
                {
                    Width = 77,
                    Height = 77,
                    CornerRadius = new CornerRadius(19),
                    Background = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var iconHost = new Grid();
                var letter = new TextBlock
                {
                    Text = sc.Title.Length > 0 ? sc.Title.Substring(0, 1).ToUpperInvariant() : "\u2022",
                    Foreground = Brushes.White,
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconHost.Children.Add(letter);
                var favImg = CreateThemeFaviconImage(sc.Url, 40, letter);
                if (favImg != null)
                    iconHost.Children.Add(favImg);
                iconBox.Child = iconHost;

                var label = new TextBlock
                {
                    Text = sc.Title,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = itemW,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                itemPanel.Children.Add(iconBox);
                itemPanel.Children.Add(label);

                // Exact subpixel coordinates calculated by Rust solver (normalized to center cleanly)
                Canvas.SetLeft(itemPanel, rect.X - minX);
                Canvas.SetTop(itemPanel, rect.Y);

                string targetUrl = sc.Url;
                itemPanel.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                CanvasRustDock.Children.Add(itemPanel);
            }

            CanvasRustDock.Width = Math.Max(0, maxRight - minX);
            CanvasRustDock.Height = maxBottom;
        }
    }

    private void RustSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtRustSpotlightSearch?.Text);
            e.Handled = true;
        }
    }

    // =========================================================================
    // THEME 5: RETRO CRT
    // =========================================================================
    private void PopulateRetroCRT()
    {
        AttachThemeParityShell(ThemeHost_RetroCRT, "RetroCRT", TxtCrtSearch);

        if (TxtCrtScore != null)
            TxtCrtScore.Text = $"{DateTime.Now:HHmmss}";

        if (PnlCrtShortcuts != null)
        {
            PnlCrtShortcuts.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtCrtSearch));
            if (items.Count == 0)
            {
                PnlCrtShortcuts.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("RetroCRT")));
                return;
            }
            int idx = 1;
            foreach (var sc in items)
            {
                var b = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x14, 0x08)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x00)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 10, 10),
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url,
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };

                var txt = new TextBlock
                {
                    Text = $"[DISK_{idx}] {sc.Title.ToUpperInvariant()}.DAT",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x00)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12
                };
                b.Child = txt;

                string targetUrl = sc.Url;
                b.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlCrtShortcuts.Children.Add(b);
                idx++;
            }
        }
    }

    private void CrtSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtCrtSearch?.Text);
            e.Handled = true;
        }
    }

    // =========================================================================
    // THEME 6: EDITORIAL PAPER
    // =========================================================================
    private void PopulateEditorialPaper()
    {
        AttachThemeParityShell(ThemeHost_EditorialPaper, "EditorialPaper", TxtEditorialSearch);

        if (TxtEditorialIssue != null)
            TxtEditorialIssue.Text = $"VOL. XXIV // {DateTime.Now:MMMM d, yyyy}".ToUpperInvariant();

        if (PnlEditorialIndex != null)
        {
            PnlEditorialIndex.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtEditorialSearch));
            if (items.Count == 0)
            {
                PnlEditorialIndex.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("EditorialPaper")));
                return;
            }
            int chapter = 1;
            foreach (var sc in items)
            {
                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 6, 0, 6),
                    Cursor = Cursors.Hand,
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };

                var prefix = new TextBlock
                {
                    Text = $"{chapter:D2}. ",
                    FontFamily = new FontFamily("Georgia"),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x00, 0x00))
                };

                var title = new TextBlock
                {
                    Text = $"{sc.Title} \u2014 {sc.Url}",
                    FontFamily = new FontFamily("Georgia"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x21)),
                    TextDecorations = TextDecorations.Underline
                };

                sp.Children.Add(prefix);
                sp.Children.Add(title);

                string targetUrl = sc.Url;
                sp.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlEditorialIndex.Children.Add(sp);
                chapter++;
            }
        }
    }

    private void EditorialSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtEditorialSearch?.Text);
            e.Handled = true;
        }
    }

    // =========================================================================
    // THEME 7: STARSHIP LCARS
    // =========================================================================
    private static readonly Color[] LcarsColors = new[]
    {
        Color.FromRgb(0xFF, 0x99, 0x00), // Federation Gold
        Color.FromRgb(0xCC, 0x99, 0xCC), // Lilac
        Color.FromRgb(0x99, 0x99, 0xFF), // Warp Blue
        Color.FromRgb(0xFF, 0x99, 0x66), // Salmon
        Color.FromRgb(0xCC, 0x66, 0x99)  // Burgundy
    };

    private void PopulateStarshipLCARS()
    {
        AttachThemeParityShell(ThemeHost_StarshipLCARS, "StarshipLCARS", TxtLcarsSearch);

        if (TxtLcarsStardate != null)
            TxtLcarsStardate.Text = $"STARDATE: {DateTime.Now:yy}{DateTime.Now.DayOfYear}.{DateTime.Now.Hour}";

        if (PnlLcarsPills != null)
        {
            PnlLcarsPills.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtLcarsSearch));
            if (items.Count == 0)
            {
                PnlLcarsPills.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("StarshipLCARS")));
                return;
            }
            int cIdx = 0;
            int code = 101;
            foreach (var sc in items)
            {
                var col = LcarsColors[cIdx % LcarsColors.Length];
                cIdx++;

                var pill = new Border
                {
                    Background = new SolidColorBrush(col),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(18, 8, 18, 8),
                    Margin = new Thickness(0, 0, 12, 12),
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url,
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };

                var txt = new TextBlock
                {
                    Text = $"47-{code}  {sc.Title.ToUpperInvariant()}",
                    FontFamily = new FontFamily("Arial Black"),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                };
                pill.Child = txt;

                string targetUrl = sc.Url;
                pill.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlLcarsPills.Children.Add(pill);
                code++;
            }
        }
    }

    private void LcarsSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtLcarsSearch?.Text);
            e.Handled = true;
        }
    }

    // =========================================================================
    // THEME 8: ZEN GARDEN
    // =========================================================================
    private void PopulateZenGarden()
    {
        AttachThemeParityShell(ThemeHost_ZenGarden, "ZenGarden", TxtZenSearch);

        if (TxtZenClock != null)
            TxtZenClock.Text = DateTime.Now.ToString("HH:mm");
        if (TxtZenBreath != null)
        {
            string wx = !string.IsNullOrEmpty(WeatherBridge.WeatherText) ? WeatherBridge.WeatherText + "  \u00B7  " : "";
            TxtZenBreath.Text = $"{wx}{DateTime.Now:dddd, MMMM d}";
        }

        if (PnlZenStones != null)
        {
            PnlZenStones.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtZenSearch));
            if (items.Count == 0)
            {
                PnlZenStones.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("ZenGarden")));
                return;
            }
            foreach (var sc in items)
            {
                var stone = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(22),
                    Padding = new Thickness(16, 9, 18, 9),
                    Margin = new Thickness(0, 0, 12, 12),
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url,
                    ContextMenu = BuildThemeItemContextMenu(sc),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 16,
                        ShadowDepth = 3,
                        Direction = 270,
                        Color = Color.FromRgb(0x40, 0x55, 0x45),
                        Opacity = 0.14
                    }
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var favImg = CreateThemeFaviconImage(sc.Url, 16);
                if (favImg != null)
                {
                    favImg.Margin = new Thickness(0, 0, 8, 0);
                    sp.Children.Add(favImg);
                }
                sp.Children.Add(new TextBlock
                {
                    Text = sc.Title,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 13.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x23, 0x30, 0x26)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                stone.Child = sp;

                string targetUrl = sc.Url;
                stone.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlZenStones.Children.Add(stone);
            }
        }
    }

    private void BtnZenQuickAdd_Click(object sender, RoutedEventArgs e)
    {
        PromptAddFavoriteFromTheme();
    }

    private void ZenSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtZenSearch?.Text);
            e.Handled = true;
        }
    }

    // =========================================================================
    // THEME 9: WIN95 RETRO DESK
    // =========================================================================
    private void PopulateWin95RetroDesk()
    {
        AttachThemeParityShell(ThemeHost_Win95RetroDesk, "Win95RetroDesk", TxtWin95Search);

        if (TxtWin95Clock != null)
            TxtWin95Clock.Text = $"{DateTime.Now:t}";

        if (PnlWin95Icons != null)
        {
            PnlWin95Icons.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtWin95Search));
            if (items.Count == 0)
            {
                PnlWin95Icons.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("Win95RetroDesk")));
                return;
            }
            foreach (var sc in items)
            {
                var item = new StackPanel
                {
                    Width = 84,
                    Margin = new Thickness(10),
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url,
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };

                var iconBox = new Border
                {
                    Width = 38,
                    Height = 38,
                    Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var iconHost = new Grid();
                var iconGlyph = new TextBlock
                {
                    Text = "\U0001F5A5",
                    FontSize = 20,
                    Foreground = Brushes.Yellow,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconHost.Children.Add(iconGlyph);
                var favImg = CreateThemeFaviconImage(sc.Url, 24, iconGlyph);
                if (favImg != null)
                    iconHost.Children.Add(favImg);
                iconBox.Child = iconHost;

                var label = new TextBlock
                {
                    Text = sc.Title,
                    FontFamily = new FontFamily("MS Sans Serif, Tahoma, Arial"),
                    FontSize = 11,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                item.Children.Add(iconBox);
                item.Children.Add(label);

                string targetUrl = sc.Url;
                item.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlWin95Icons.Children.Add(item);
            }
        }
    }

    private void Win95Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtWin95Search?.Text);
            e.Handled = true;
        }
    }

    private void BtnWin95Search_Click(object sender, RoutedEventArgs e)
    {
        ExecuteThemeSearch(TxtWin95Search?.Text);
    }

    // =========================================================================
    // THEME 10: IMPERIAL CITADEL (Original Medieval RTS Stone & Gold HUD)
    // =========================================================================
    private void PopulateImperialCitadel()
    {
        AttachThemeParityShell(ThemeHost_ImperialCitadel, "ImperialCitadel", TxtImperialSearch);

        if (ImgImperialBg != null && ImgImperialBg.Source == null)
        {
            try
            {
                string bgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallpapers", "imperial_citadel_bg.jpg");
                if (System.IO.File.Exists(bgPath))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(bgPath, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    ImgImperialBg.Source = bmp;
                }
            }
            catch { }
        }

        if (TxtImperialClock != null)
            TxtImperialClock.Text = DateTime.Now.ToString("HH:mm:ss");
        if (TxtImperialDate != null)
            TxtImperialDate.Text = $"IMPERIAL AGE  \u2022  {DateTime.Now:MMMM d, yyyy}".ToUpperInvariant();
        if (TxtImperialWeather != null)
            TxtImperialWeather.Text = "\u2600 " + (string.IsNullOrEmpty(WeatherBridge.WeatherText) ? "Clear Skies" : WeatherBridge.WeatherText);
        if (TxtImperialRam != null)
        {
            try { TxtImperialRam.Text = $"\u2694 RAM: {Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)} MB"; }
            catch { TxtImperialRam.Text = "\u2694 RAM: Ready"; }
        }
        if (TxtImperialPop != null)
        {
            int total = (SettingsService.Current?.PinnedUrls?.Count ?? 0) + (BookmarkService.Items?.Count ?? 0);
            TxtImperialPop.Text = $"\u2605 Realm: {total}";
        }

        if (PnlImperialCrests != null)
        {
            PnlImperialCrests.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtImperialSearch));
            if (items.Count == 0)
            {
                PnlImperialCrests.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("ImperialCitadel")));
                return;
            }

            foreach (var sc in items)
            {
                var crest = new Border
                {
                    Width = 148,
                    Height = 88,
                    Margin = new Thickness(8),
                    CornerRadius = new CornerRadius(5),
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0x2B, 0x21, 0x17),
                        Color.FromRgb(0x1B, 0x14, 0x0E),
                        90),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xA2, 0x4A)),
                    BorderThickness = new Thickness(1.5),
                    Padding = new Thickness(10, 8, 10, 8),
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url,
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };

                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var iconHost = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
                var fallback = new TextBlock
                {
                    Text = "\u269C",
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xC8, 0x69)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconHost.Children.Add(fallback);
                var favImg = CreateThemeFaviconImage(sc.Url, 28, fallback);
                if (favImg != null)
                    iconHost.Children.Add(favImg);
                sp.Children.Add(iconHost);
                sp.Children.Add(new TextBlock
                {
                    Text = sc.Title,
                    FontFamily = new FontFamily("Georgia"),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xE6, 0xC8)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 124
                });

                crest.Child = sp;
                string targetUrl = sc.Url;
                crest.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlImperialCrests.Children.Add(crest);
            }
        }
    }

    private void ImperialSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtImperialSearch?.Text);
            e.Handled = true;
        }
    }

    private void BtnImperialSearch_Click(object sender, RoutedEventArgs e)
    {
        ExecuteThemeSearch(TxtImperialSearch?.Text);
    }

    private double _lastCelestialW;
    private double _lastCelestialH;

    private void ThemeHost_CelestialObservatory_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ThemeHost_CelestialObservatory?.Visibility == Visibility.Visible)
            RenderCelestialBackdrop();
    }

    private void RenderCelestialBackdrop()
    {
        if (CanvasCelestialSky == null || ThemeHost_CelestialObservatory == null) return;
        double w = ThemeHost_CelestialObservatory.ActualWidth;
        double h = ThemeHost_CelestialObservatory.ActualHeight;
        if (w < 100 || h < 100) return;
        if (Math.Abs(w - _lastCelestialW) < 4 && Math.Abs(h - _lastCelestialH) < 4 && CanvasCelestialSky.Children.Count > 0)
            return;

        _lastCelestialW = w;
        _lastCelestialH = h;
        CanvasCelestialSky.Children.Clear();

        double cx = w * 0.5;
        double cy = h * 0.48;
        double outerR = Math.Min(w, h) * 0.44;

        // 1. Seeded spectral starfield (180 stars: blue-white, pure white, warm gold, soft cyan)
        var rng = new Random(4711);
        Color[] starColors =
        {
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xCF, 0xE8, 0xFF),
            Color.FromRgb(0xFF, 0xE6, 0xB8),
            Color.FromRgb(0xB8, 0xD8, 0xFF),
            Color.FromRgb(0xF5, 0xD0, 0xFE)
        };

        for (int i = 0; i < 185; i++)
        {
            double sx = rng.NextDouble() * w;
            double sy = rng.NextDouble() * h;
            double sz = rng.NextDouble() < 0.14 ? (2.2 + rng.NextDouble() * 1.6) : (0.9 + rng.NextDouble() * 1.4);
            byte alpha = (byte)(75 + rng.Next(180));
            var col = starColors[rng.Next(starColors.Length)];

            var dot = new Ellipse
            {
                Width = sz,
                Height = sz,
                Fill = new SolidColorBrush(Color.FromArgb(alpha, col.R, col.G, col.B))
            };
            Canvas.SetLeft(dot, sx - sz / 2);
            Canvas.SetTop(dot, sy - sz / 2);
            CanvasCelestialSky.Children.Add(dot);

            // Add subtle 4-point diffraction cross-flares on the brightest stars
            if (sz >= 3.1)
            {
                double flare = sz * 4.2;
                var hLine = new Line
                {
                    X1 = sx - flare, Y1 = sy, X2 = sx + flare, Y2 = sy,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x66, col.R, col.G, col.B)),
                    StrokeThickness = 0.8
                };
                var vLine = new Line
                {
                    X1 = sx, Y1 = sy - flare, X2 = sx, Y2 = sy + flare,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x66, col.R, col.G, col.B)),
                    StrokeThickness = 0.8
                };
                CanvasCelestialSky.Children.Add(hLine);
                CanvasCelestialSky.Children.Add(vLine);
            }
        }

        // 2. Named constellations (normalized coordinates) with fine star-chart lines
        void DrawConstellation(string name, (double X, double Y)[] pts, (int A, int B)[] edges)
        {
            var lineBrush = new SolidColorBrush(Color.FromArgb(0x36, 0x93, 0xC5, 0xFD));
            foreach (var (a, b) in edges)
            {
                CanvasCelestialSky.Children.Add(new Line
                {
                    X1 = pts[a].X * w, Y1 = pts[a].Y * h,
                    X2 = pts[b].X * w, Y2 = pts[b].Y * h,
                    Stroke = lineBrush,
                    StrokeThickness = 0.9
                });
            }
            foreach (var p in pts)
            {
                var star = new Ellipse { Width = 4.2, Height = 4.2, Fill = new SolidColorBrush(Color.FromArgb(0xE8, 0xE8, 0xF4, 0xFF)) };
                Canvas.SetLeft(star, p.X * w - 2.1);
                Canvas.SetTop(star, p.Y * h - 2.1);
                CanvasCelestialSky.Children.Add(star);
            }
            var lbl = new TextBlock
            {
                Text = name,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xD4, 0xAF, 0x37))
            };
            Canvas.SetLeft(lbl, pts[0].X * w + 8);
            Canvas.SetTop(lbl, pts[0].Y * h - 14);
            CanvasCelestialSky.Children.Add(lbl);
        }

        // Cassiopeia (top-left)
        DrawConstellation("CASSIOPEIA",
            new[] { (0.07, 0.22), (0.11, 0.29), (0.15, 0.25), (0.19, 0.32), (0.23, 0.24) },
            new[] { (0, 1), (1, 2), (2, 3), (3, 4) });

        // Orion (right sky)
        DrawConstellation("ORION",
            new[] { (0.78, 0.24), (0.86, 0.26), (0.80, 0.37), (0.82, 0.38), (0.84, 0.39), (0.77, 0.51), (0.87, 0.53) },
            new[] { (0, 2), (1, 4), (2, 3), (3, 4), (2, 5), (4, 6), (0, 1), (5, 6) });

        // Ursa Major / Big Dipper (bottom-left)
        DrawConstellation("URSA MAJOR",
            new[] { (0.06, 0.72), (0.10, 0.69), (0.14, 0.71), (0.18, 0.75), (0.23, 0.74), (0.24, 0.81), (0.19, 0.82) },
            new[] { (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 3) });

        // 3. Graduated Brass Astrolabe / Armillary Sphere Rings
        var brassSoft = new SolidColorBrush(Color.FromArgb(0x30, 0xD4, 0xAF, 0x37));
        var brassBright = new SolidColorBrush(Color.FromArgb(0x52, 0xD4, 0xAF, 0x37));
        var cyanSoft = new SolidColorBrush(Color.FromArgb(0x26, 0x60, 0xCD, 0xFF));

        void AddCircle(double r, Brush stroke, double thickness, double[]? dash = null)
        {
            var el = new Ellipse { Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = thickness };
            if (dash != null) el.StrokeDashArray = new DoubleCollection(dash);
            Canvas.SetLeft(el, cx - r);
            Canvas.SetTop(el, cy - r);
            CanvasCelestialSky.Children.Add(el);
        }

        AddCircle(outerR, brassBright, 1.2);
        AddCircle(outerR * 0.94, brassSoft, 0.8);
        AddCircle(outerR * 0.72, cyanSoft, 1.0, new[] { 4.0, 6.0 });
        AddCircle(outerR * 0.48, brassSoft, 0.9);

        // Tilted ecliptic armillary ellipse
        var ecliptic = new Ellipse
        {
            Width = outerR * 2.15,
            Height = outerR * 0.82,
            Stroke = new SolidColorBrush(Color.FromArgb(0x36, 0xD4, 0xAF, 0x37)),
            StrokeThickness = 1.1,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-22)
        };
        Canvas.SetLeft(ecliptic, cx - ecliptic.Width / 2);
        Canvas.SetTop(ecliptic, cy - ecliptic.Height / 2);
        CanvasCelestialSky.Children.Add(ecliptic);

        // 72 degree tick marks around the outer astrolabe ring (every 5°)
        for (int deg = 0; deg < 360; deg += 5)
        {
            double rad = deg * Math.PI / 180.0;
            bool major = deg % 30 == 0;
            double r1 = outerR * (major ? 0.94 : 0.968);
            double r2 = outerR;
            CanvasCelestialSky.Children.Add(new Line
            {
                X1 = cx + Math.Cos(rad) * r1,
                Y1 = cy + Math.Sin(rad) * r1,
                X2 = cx + Math.Cos(rad) * r2,
                Y2 = cy + Math.Sin(rad) * r2,
                Stroke = major ? brassBright : brassSoft,
                StrokeThickness = major ? 1.3 : 0.7
            });
        }

        // Cardinal degree labels (N / E / S / W)
        var cardinals = new[] { ("N 0\u00B0", -90.0), ("E 90\u00B0", 0.0), ("S 180\u00B0", 90.0), ("W 270\u00B0", 180.0) };
        foreach (var (label, angleDeg) in cardinals)
        {
            double rad = angleDeg * Math.PI / 180.0;
            var tb = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xD4, 0xAF, 0x37))
            };
            Canvas.SetLeft(tb, cx + Math.Cos(rad) * (outerR + 18) - 14);
            Canvas.SetTop(tb, cy + Math.Sin(rad) * (outerR + 14) - 7);
            CanvasCelestialSky.Children.Add(tb);
        }

        // Glowing planetary markers perched on the orbital rings
        var planets = new[]
        {
            (Angle: -38.0, R: outerR * 0.72, Size: 9.0, Color: Color.FromRgb(0x60, 0xCD, 0xFF)),
            (Angle: 142.0, R: outerR * 0.94, Size: 11.0, Color: Color.FromRgb(0xF0, 0xC8, 0x69)),
            (Angle: 218.0, R: outerR * 0.48, Size: 7.0, Color: Color.FromRgb(0xC0, 0x84, 0xFC))
        };
        foreach (var p in planets)
        {
            double rad = p.Angle * Math.PI / 180.0;
            double px = cx + Math.Cos(rad) * p.R;
            double py = cy + Math.Sin(rad) * p.R;
            var halo = new Ellipse
            {
                Width = p.Size * 2.6,
                Height = p.Size * 2.6,
                Fill = new SolidColorBrush(Color.FromArgb(0x38, p.Color.R, p.Color.G, p.Color.B))
            };
            Canvas.SetLeft(halo, px - halo.Width / 2);
            Canvas.SetTop(halo, py - halo.Height / 2);
            CanvasCelestialSky.Children.Add(halo);

            var body = new Ellipse
            {
                Width = p.Size,
                Height = p.Size,
                Fill = new SolidColorBrush(p.Color)
            };
            Canvas.SetLeft(body, px - p.Size / 2);
            Canvas.SetTop(body, py - p.Size / 2);
            CanvasCelestialSky.Children.Add(body);
        }
    }

    // =========================================================================
    // THEME 11: CELESTIAL OBSERVATORY (Midnight Indigo & Brass Astrolabe)
    // =========================================================================
    private void PopulateCelestialObservatory()
    {
        AttachThemeParityShell(ThemeHost_CelestialObservatory, "CelestialObservatory", TxtCelestialSearch);
        RenderCelestialBackdrop();

        if (TxtCelestialClock != null)
            TxtCelestialClock.Text = DateTime.Now.ToString("HH:mm");
        if (TxtCelestialSky != null)
        {
            string wx = !string.IsNullOrEmpty(WeatherBridge.WeatherText) ? WeatherBridge.WeatherText : "Clear Night Sky";
            TxtCelestialSky.Text = $"{wx}  \u2022  {DateTime.Now:dddd, MMM d}";
        }

        if (PnlCelestialCards != null)
        {
            PnlCelestialCards.Children.Clear();
            var (items, _, emptyMsg) = GetThemeActiveItems(GetActiveThemeSearchQuery(TxtCelestialSearch));
            if (items.Count == 0)
            {
                PnlCelestialCards.Children.Add(MakeThemeEmptyNotice(emptyMsg, GetThemeStyleSpec("CelestialObservatory")));
                return;
            }

            foreach (var sc in items)
            {
                var card = new Border
                {
                    Width = 148,
                    Height = 86,
                    Margin = new Thickness(8),
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x0C, 0x15, 0x2B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xD4, 0xAF, 0x37)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 8, 10, 8),
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url,
                    ContextMenu = BuildThemeItemContextMenu(sc)
                };

                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var iconHost = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
                var fallback = new TextBlock
                {
                    Text = "\u2726",
                    FontSize = 18,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x37)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconHost.Children.Add(fallback);
                var favImg = CreateThemeFaviconImage(sc.Url, 26, fallback);
                if (favImg != null)
                    iconHost.Children.Add(favImg);

                sp.Children.Add(iconHost);
                sp.Children.Add(new TextBlock
                {
                    Text = sc.Title,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 124
                });

                card.Child = sp;
                string targetUrl = sc.Url;
                card.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlCelestialCards.Children.Add(card);
            }
        }
    }

    private void CelestialSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteThemeSearch(TxtCelestialSearch?.Text);
            e.Handled = true;
        }
    }

    private void BtnCelestialSearch_Click(object sender, RoutedEventArgs e)
    {
        ExecuteThemeSearch(TxtCelestialSearch?.Text);
    }
}
