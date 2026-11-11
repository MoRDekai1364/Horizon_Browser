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
            TxtCyberClock.Text = $"[CLOCK: {now:HH:mm:ss} UTC+2]";
        if (TxtCrtScore != null && ThemeHost_RetroCRT?.Visibility == Visibility.Visible)
            TxtCrtScore.Text = $"{now:HHmmss}";
        if (TxtZenClock != null && ThemeHost_ZenGarden?.Visibility == Visibility.Visible)
            TxtZenClock.Text = $"{now:HH:mm}";
        if (TxtWin95Clock != null && ThemeHost_Win95RetroDesk?.Visibility == Visibility.Visible)
            TxtWin95Clock.Text = $"{now:t}";
    }

    // =========================================================================
    // THEME 2: CYBER TERMINAL
    // =========================================================================
    private void PopulateCyberTerminal()
    {
        if (TxtCyberClock != null)
            TxtCyberClock.Text = $"[CLOCK: {DateTime.Now:HH:mm:ss} UTC+2]";

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
            string wx = !string.IsNullOrEmpty(WeatherBridge.WeatherText) ? WeatherBridge.WeatherText : "7°C Overcast";
            TxtCyberWeather.Text = $"[WX: {wx.ToUpperInvariant()} // 1014hPa]";
        }

        if (PnlCyberShortcuts != null)
        {
            PnlCyberShortcuts.Children.Clear();
            var shortcuts = GetThemeShortcuts();
            int index = 1;
            foreach (var sc in shortcuts)
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
                    Content = $"[{index}] {sc.Title.ToUpperInvariant()} -> {sc.Url}"
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
        if (TxtMetroWeatherTemp != null && !string.IsNullOrEmpty(WeatherBridge.WeatherText))
            TxtMetroWeatherTemp.Text = WeatherBridge.WeatherText;

        if (TxtMetroMediaTitle != null)
            TxtMetroMediaTitle.Text = string.IsNullOrEmpty(MediaBridge.Title) ? "Horizon Audio Engine" : MediaBridge.Title;
        if (TxtMetroMediaArtist != null)
            TxtMetroMediaArtist.Text = string.IsNullOrEmpty(MediaBridge.Artist) ? "Ready to play" : MediaBridge.Artist;

        if (PnlMetroShortcuts != null)
        {
            PnlMetroShortcuts.Children.Clear();
            var shortcuts = GetThemeShortcuts();
            int colorIdx = 0;
            foreach (var sc in shortcuts)
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
                    Cursor = Cursors.Hand
                };

                var grid = new Grid { Margin = new Thickness(10) };
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
        if (TxtRustClock != null)
            TxtRustClock.Text = DateTime.Now.ToString("HH:mm");

        if (TxtRustWeather != null)
        {
            string wx = !string.IsNullOrEmpty(WeatherBridge.WeatherText) ? WeatherBridge.WeatherText : "7\u00B0C Overcast";
            TxtRustWeather.Text = wx;
        }

        if (CanvasRustDock != null)
        {
            CanvasRustDock.Children.Clear();
            var shortcuts = GetThemeShortcuts();
            if (shortcuts.Count == 0) return;

            // Target card dimensions matching original homepage (77x77 icon + label)
            const double itemW = 96.0;
            const double itemH = 112.0;
            const double targetGap = 16.0;

            // Dynamic container width based on viewport
            double availableW = Math.Clamp(RootHomeGrid?.ActualWidth > 0 ? RootHomeGrid.ActualWidth * 0.65 : 780.0, 480.0, 960.0);

            // Compute 2D grid positions natively in Rust horizon_core.dll
            var rects = HorizonCore.ComputeHomepageLayout(availableW, 4000.0, shortcuts.Count, itemW, itemH, targetGap);
            if (rects.Length == 0) return;

            double maxRight = 0;
            double maxBottom = 0;

            for (int i = 0; i < rects.Length && i < shortcuts.Count; i++)
            {
                var rect = rects[i];
                var sc = shortcuts[i];

                if (rect.X + rect.Width > maxRight) maxRight = rect.X + rect.Width;
                if (rect.Y + rect.Height > maxBottom) maxBottom = rect.Y + rect.Height;

                var itemPanel = new StackPanel
                {
                    Width = itemW,
                    Height = itemH,
                    Cursor = Cursors.Hand,
                    ToolTip = sc.Title + "\n" + sc.Url
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

                var letter = new TextBlock
                {
                    Text = sc.Title.Length > 0 ? sc.Title.Substring(0, 1).ToUpperInvariant() : "\u2022",
                    Foreground = Brushes.White,
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBox.Child = letter;

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

                // Exact subpixel coordinates calculated by Rust solver
                Canvas.SetLeft(itemPanel, rect.X);
                Canvas.SetTop(itemPanel, rect.Y);

                string targetUrl = sc.Url;
                itemPanel.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                CanvasRustDock.Children.Add(itemPanel);
            }

            CanvasRustDock.Width = maxRight;
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
        if (TxtCrtScore != null)
            TxtCrtScore.Text = $"{DateTime.Now:HHmmss}";

        if (PnlCrtShortcuts != null)
        {
            PnlCrtShortcuts.Children.Clear();
            var shortcuts = GetThemeShortcuts();
            int idx = 1;
            foreach (var sc in shortcuts)
            {
                var b = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x14, 0x08)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x00)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 10, 10),
                    Cursor = Cursors.Hand
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
        if (TxtEditorialIssue != null)
            TxtEditorialIssue.Text = $"VOL. XXIV // {DateTime.Now:MMMM d, yyyy}".ToUpperInvariant();

        if (PnlEditorialIndex != null)
        {
            PnlEditorialIndex.Children.Clear();
            var shortcuts = GetThemeShortcuts();
            int chapter = 1;
            foreach (var sc in shortcuts)
            {
                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 6, 0, 6),
                    Cursor = Cursors.Hand
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
        if (TxtLcarsStardate != null)
            TxtLcarsStardate.Text = $"STARDATE: {DateTime.Now:yy}{DateTime.Now.DayOfYear}.{DateTime.Now.Hour}";

        if (PnlLcarsPills != null)
        {
            PnlLcarsPills.Children.Clear();
            var shortcuts = GetThemeShortcuts();
            int cIdx = 0;
            int code = 101;
            foreach (var sc in shortcuts)
            {
                var col = LcarsColors[cIdx % LcarsColors.Length];
                cIdx++;

                var pill = new Border
                {
                    Background = new SolidColorBrush(col),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(18, 8, 18, 8),
                    Margin = new Thickness(0, 0, 12, 12),
                    Cursor = Cursors.Hand
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
        if (TxtZenClock != null)
            TxtZenClock.Text = DateTime.Now.ToString("HH:mm");

        if (PnlZenStones != null)
        {
            PnlZenStones.Children.Clear();
            var shortcuts = GetThemeShortcuts();
            foreach (var sc in shortcuts)
            {
                var stone = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x23, 0x2A, 0x25)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0x3F, 0x37)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(22),
                    Padding = new Thickness(20, 10, 20, 10),
                    Margin = new Thickness(0, 0, 12, 12),
                    Cursor = Cursors.Hand
                };

                var txt = new TextBlock
                {
                    Text = sc.Title,
                    FontFamily = new FontFamily("Segoe UI Light"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD8, 0xD0))
                };
                stone.Child = txt;

                string targetUrl = sc.Url;
                stone.MouseLeftButtonUp += (_, _) => NavigateRequested?.Invoke(targetUrl);
                PnlZenStones.Children.Add(stone);
            }
        }
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
        if (TxtWin95Clock != null)
            TxtWin95Clock.Text = $"{DateTime.Now:t}";

        if (PnlWin95Icons != null)
        {
            PnlWin95Icons.Children.Clear();
            var shortcuts = GetThemeShortcuts();
            foreach (var sc in shortcuts)
            {
                var item = new StackPanel
                {
                    Width = 80,
                    Margin = new Thickness(10),
                    Cursor = Cursors.Hand
                };

                var iconBox = new Border
                {
                    Width = 36,
                    Height = 36,
                    Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var iconGlyph = new TextBlock
                {
                    Text = "\U0001F5A5", // Computer glyph
                    FontSize = 20,
                    Foreground = Brushes.Yellow,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBox.Child = iconGlyph;

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
}
