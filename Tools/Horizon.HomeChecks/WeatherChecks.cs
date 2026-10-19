using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Horizon.Stealth;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;

internal static partial class Program
{
    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }

    private static object StaticWeather(string name, params object?[] args) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;

    private static void CheckWeather()
    {
        SettingsService.Current.WidgetWeatherCity = "";
        typeof(AstronomyService).GetField("_stormAttempt", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, DateTimeOffset.UtcNow);
        var body = new StackPanel();
        var cards = new[] { "MoonPhase", "SunProgress", "MagneticStorms" }
            .Select(mode => (AstronomyWidget)StaticWeather("BuildWeatherAstronomyCard", mode)).ToArray();
        foreach (var card in cards) body.Children.Add(card);
        body.Children.Add(new Border { Height = 600 });
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var win = new Window { Content = scroll, Width = 430, Height = 440, Left = -12000, Top = -12000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        ContrastGuard.SetMode(win, ContrastMode.Off);
        ContrastGuard.Initialize();
        try
        {
            win.Show(); Pump(200);
            foreach (var card in cards)
            {
                Check(card.Embedded && !card.OpenOnClick && card.Cursor == Cursors.Arrow, "Weather cards are passive");
                Check(!VisualChildren<ScrollViewer>(card).Any(), "Embedded cards have no nested scroll viewer");
                int windows = Application.Current.Windows.Count;
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
                card.RaiseEvent(click);
                Check(!click.Handled && Application.Current.Windows.Count == windows, "Card click does not consume input or open a window");
            }
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent };
            Field<TextBlock>(cards[0], "_value").RaiseEvent(wheel); Pump(100);
            Check(scroll.VerticalOffset > 0, "Wheel over moon card scrolls the weather pane");

            foreach (int code in new[] { 0, 1, 2, 3, 45, 61, 71, 95 })
            {
                var icon = (FrameworkElement)StaticWeather("BuildWeatherConditionIcon", code, 46.0, null);
                Check(icon is TextBlock text && text.Background == null, "Static transparent weather icon " + code);
                icon.Measure(new Size(46, 46)); icon.Arrange(new Rect(0, 0, 46, 46));
                var bitmap = new RenderTargetBitmap(46, 46, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(icon);
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
                Check(pixel[3] == 0, "Weather icon corner is transparent " + code);
            }

            var semantic = WeatherAppearance.SemanticBrush(Colors.Orange);
            foreach (bool dark in new[] { false, true, false })
            {
                Color average = dark ? Color.FromRgb(24, 25, 32) : Colors.White;
                WeatherBridge.PublishTheme(average, average, Colors.White, Colors.LightSkyBlue, Colors.White, Colors.LightGray, dark, true);
                win.Background = new SolidColorBrush(WeatherAppearance.PanelColor);
                foreach (var card in cards) Invoke(card, "Refresh");
                Pump(200);
                var expected = WeatherAppearance.TextBrush.Color;
                foreach (var card in cards)
                {
                    var text = Field<TextBlock>(card, "_value");
                    Check(ReferenceEquals(text.Foreground, WeatherAppearance.TextBrush), "Cards use the weather palette");
                    Check(dark ? expected.R > 220 : expected.R < 40, "Text follows background brightness");
                    for (int refresh = 0; refresh < 3; refresh++) Invoke(card, "Refresh");
                    ContrastGuard.RequestRefresh(); Pump(220);
                    Check(((SolidColorBrush)text.Foreground).Color == expected, "Timer/data/contrast refresh cannot flip weather text");
                }
                Check(WeatherAppearance.Contrast(expected, WeatherAppearance.PanelColor) >= 7, "Primary weather text contrast");
                Check(WeatherAppearance.Contrast(semantic.Color, WeatherAppearance.PanelColor) >= 4.5, "Semantic temperature/chart colors stay readable");
                scroll.ScrollToTop(); Pump(80);
                var rendered = new RenderTargetBitmap((int)scroll.ActualWidth, (int)scroll.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(scroll);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rendered));
                var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "weather-artifacts");
                System.IO.Directory.CreateDirectory(directory);
                using var output = System.IO.File.Create(System.IO.Path.Combine(directory, dark ? "weather-dark.png" : "weather-light.png"));
                encoder.Save(output);
            }
            Console.WriteLine("PASS: embedded scrolling, passive cards, static transparent icons and stable light/dark weather colors");
        }
        finally { win.Close(); }
    }
}
