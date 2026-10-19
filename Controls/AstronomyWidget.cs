using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Controls;

public sealed class SunProgressView : FrameworkElement
{
    public bool UseWeatherPalette { get; set; }
    private double? _progress;
    private bool _isDay;
    public void SetState(double? progress, bool isDay)
    {
        _progress = progress;
        _isDay = isDay;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double pad = Math.Min(8, ActualHeight / 3), width = Math.Max(0, ActualWidth - 2 * pad);
        double baseline = ActualHeight - pad, amplitude = Math.Max(0, ActualHeight - 2 * pad);
        var muted = new SolidColorBrush(UseWeatherPalette ? WeatherAppearance.MutedBrush.Color : WeatherBridge.ThemeAccentSubtle) { Opacity = 0.55 };
        var accent = UseWeatherPalette ? WeatherAppearance.AccentBrush : new SolidColorBrush(WeatherBridge.ThemeAccent);
        Point At(double fraction) => new(pad + width * fraction, baseline - amplitude * Math.Sin(Math.PI * fraction));
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(At(0), false, false);
            for (int i = 1; i <= 40; i++) context.LineTo(At(i / 40.0), true, false);
        }
        dc.DrawLine(new Pen(muted, 1), new Point(pad, baseline), new Point(pad + width, baseline));
        dc.DrawGeometry(null, new Pen(muted, 1.5), arc);
        if (_progress.HasValue)
            dc.DrawEllipse(_isDay ? accent : muted, new Pen(accent, 1), At(Math.Clamp(_progress.Value, 0, 1)), pad * 0.65, pad * 0.65);
    }
}

public sealed class AstronomyWidget : UserControl
{
    private readonly Border _card = new();
    private readonly StackPanel _body = new();
    private readonly TextBlock _icon = new() { FontFamily = new FontFamily("Segoe UI Emoji") };
    private readonly TextBlock _title = new();
    private readonly TextBlock _value = new();
    private readonly TextBlock _detail = new();
    private readonly TextBlock _source = new();
    private readonly SunProgressView _sun = new() { Height = 54, Margin = new Thickness(0, 8, 0, 4) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private string _mode = "MoonPhase";
    private bool _compact;
    private bool _embedded;
    private bool _useWeatherPalette;
    private bool _openOnClick = true;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public string Mode { get => _mode; set { _mode = value; Refresh(); } }
    public bool Compact { get => _compact; set { _compact = value; Refresh(); } }
    public bool OpenOnClick { get => _openOnClick; set { _openOnClick = value; Cursor = value && !Embedded ? Cursors.Hand : Cursors.Arrow; } }
    public bool UseWeatherPalette { get => _useWeatherPalette; set { _useWeatherPalette = value; Refresh(); } }
    public bool Embedded
    {
        get => _embedded;
        set
        {
            if (_embedded == value) return;
            _embedded = value;
            Content = null;
            _scroll.Content = null;
            if (value) Content = _card;
            else { _scroll.Content = _card; Content = _scroll; }
            Cursor = OpenOnClick && !value ? Cursors.Hand : Cursors.Arrow;
            Refresh();
        }
    }

    public AstronomyWidget()
    {
        _card.CornerRadius = new CornerRadius(22);
        _card.BorderThickness = new Thickness(1);
        _card.Child = _body;
        _body.Children.Add(_icon);
        _body.Children.Add(_title);
        _body.Children.Add(_value);
        _body.Children.Add(_sun);
        _body.Children.Add(_detail);
        _body.Children.Add(_source);
        foreach (var text in new[] { _title, _value, _detail, _source })
        {
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Thickness(0, 4, 0, 0);
        }
        _title.FontWeight = FontWeights.SemiBold;
        _scroll.Content = _card;
        Content = _scroll;
        Cursor = Cursors.Hand;
        MouseLeftButtonUp += (_, e) =>
        {
            if (Embedded || !OpenOnClick || Application.Current?.MainWindow is not MainWindow main) return;
            main.OpenAstronomyWindow(Mode);
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            AstronomyService.Updated += Refresh;
            WeatherBridge.ThemeUpdated += Refresh;
            LocalizationService.Instance.LanguageChanged += Refresh;
            if (IsVisible) { RefreshAndFetch(); _timer.Start(); }
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            AstronomyService.Updated -= Refresh;
            WeatherBridge.ThemeUpdated -= Refresh;
            LocalizationService.Instance.LanguageChanged -= Refresh;
        };
        _timer.Tick += (_, _) => RefreshAndFetch();
        IsVisibleChanged += (_, _) =>
        {
            if (IsLoaded && IsVisible) { RefreshAndFetch(); _timer.Start(); }
            else _timer.Stop();
        };
        Refresh();
    }

    private void RefreshAndFetch()
    {
        Refresh();
        _ = AstronomyService.EnsureAsync(SettingsService.Current.WidgetWeatherCity);
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Refresh)); return; }
        _card.Padding = new Thickness(Compact ? 10 : 16);
        bool weatherPalette = Embedded || UseWeatherPalette;
        _card.Background = weatherPalette ? WeatherAppearance.CardBrush : new SolidColorBrush(Color.FromArgb(0x38, WeatherBridge.ThemeAverage.R, WeatherBridge.ThemeAverage.G, WeatherBridge.ThemeAverage.B));
        _card.BorderBrush = weatherPalette ? WeatherAppearance.BorderBrush : new SolidColorBrush(Color.FromArgb(0x33, WeatherBridge.ThemeAccent.R, WeatherBridge.ThemeAccent.G, WeatherBridge.ThemeAccent.B));
        _sun.UseWeatherPalette = weatherPalette;
        _icon.FontSize = Compact ? 26 : 44;
        _value.FontSize = Compact ? 16 : 24;
        _title.FontSize = Compact ? 12 : 14;
        _detail.FontSize = 12;
        _source.FontSize = 10;
        _detail.Opacity = weatherPalette ? 1 : 0.85;
        _source.Opacity = weatherPalette ? 0.85 : 0.65;
        foreach (var text in new[] { _icon, _title, _value, _detail, _source })
            text.Foreground = weatherPalette ? WeatherAppearance.TextBrush : new SolidColorBrush(WeatherBridge.ThemeText);
        if (weatherPalette)
        {
            _detail.Foreground = WeatherAppearance.MutedBrush;
            _source.Foreground = WeatherAppearance.MutedBrush;
        }
        var now = DateTimeOffset.UtcNow;
        _sun.Visibility = Mode == "SunProgress" ? Visibility.Visible : Visibility.Collapsed;
        if (Mode == "SunProgress")
        {
            var state = AstronomyService.SunAt(AstronomyService.Solar, now);
            _title.Text = AstronomyService.Text("Astronomy.SunProgress", "Sunrise & sunset");
            _icon.Text = state.IsDay ? "☀" : "☾";
            _value.Text = state.Status + (state.IsDay && state.Progress.HasValue ? $" · {state.Progress:P0}" : "");
            _sun.SetState(state.Progress, state.IsDay);
            _detail.Text = $"↑ {state.Sunrise}   ↓ {state.Sunset}\n{state.Detail}";
            _source.Text = AstronomyService.Solar is { } solar ? solar.City + " · Open-Meteo" : "Open-Meteo";
        }
        else if (Mode == "MagneticStorms")
        {
            _title.Text = AstronomyService.Text("Astronomy.MagneticStorms", "Magnetic storms");
            _icon.Text = "🧲";
            _value.Text = AstronomyService.StormText(now);
            _detail.Text = AstronomyService.Text("Astronomy.GlobalObservation", "Global planetary Kp · observed activity");
            _source.Text = AstronomyService.Storm is { } storm ? $"NOAA SWPC · {storm.ObservedUtc:yyyy-MM-dd HH:mm} UTC" : "NOAA SWPC";
        }
        else
        {
            var moon = AstronomyService.MoonAt(now);
            _title.Text = AstronomyService.Text("Astronomy.MoonPhase", "Moon phase");
            _icon.Text = moon.Icon;
            _value.Text = moon.Name;
            _detail.Text = string.Format(AstronomyService.Text("Astronomy.Illumination", "{0:P0} illuminated"), moon.Illumination);
            _source.Text = AstronomyService.Text("Astronomy.Approximation", "Calculated locally · approximate");
        }
        ToolTip = _value.Text + "\n" + _detail.Text;
    }
}
