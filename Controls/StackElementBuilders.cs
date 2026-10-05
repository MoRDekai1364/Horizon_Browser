using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Input;
using System.Windows.Threading;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Controls;

internal sealed class StackWebHost : Border, IDisposable
{
    private const int MaxLive = 3;
    private const string MobileUa = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Mobile Safari/537.36";
    private static readonly List<StackWebHost> Live = new();

    private readonly StackElementData _data;
    private readonly StackWebProfile _profile;
    private readonly Grid _body = new();
    private readonly Border _badge = new();
    private readonly TextBlock _badgeText = new();
    private readonly DispatcherTimer? _refreshTimer;
    private WebView2? _view;
    private bool _initializing;
    private bool _suspended;
    private bool _dirty;
    private bool _disposed;

    public StackWebHost(StackElementData data)
    {
        _data = data;
        _profile = StackElementCatalog.ProfileFor(data);

        CornerRadius = new CornerRadius(22);
        Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10);

        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(4, 0, 4, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = data.Icon,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 16,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var title = new TextBlock
        {
            Text = data.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
        Grid.SetColumn(title, 1);

        _badgeText.Foreground = Brushes.White;
        _badgeText.FontSize = 11;
        _badgeText.FontWeight = FontWeights.Bold;
        _badge.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        _badge.CornerRadius = new CornerRadius(9);
        _badge.Padding = new Thickness(7, 1, 7, 1);
        _badge.Visibility = Visibility.Collapsed;
        _badge.Child = _badgeText;
        Grid.SetColumn(_badge, 2);

        header.Children.Add(icon);
        header.Children.Add(title);
        header.Children.Add(_badge);
        _body.Children.Add(header);
        Child = _body;

        if (data.RefreshMinutes > 0)
        {
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(data.RefreshMinutes) };
            _refreshTimer.Tick += (_, __) => RefreshTick();
            _refreshTimer.Start();
        }

        IsVisibleChanged += (_, e) =>
        {
            if (_disposed) return;
            if ((bool)e.NewValue) Activate();
            else Deactivate();
        };
    }

    private void RefreshTick()
    {
        if (_disposed) return;
        if (_view?.CoreWebView2 != null && IsVisible && !_suspended)
        {
            try { _view.CoreWebView2.Reload(); }
            catch (Exception ex) { LogService.RecordCrash(ex, "StackWebHost.Refresh"); }
        }
        else
        {
            _dirty = true;
        }
    }

    private void RegisterLive()
    {
        Live.Remove(this);
        Live.Add(this);
        while (Live.Count > MaxLive)
        {
            var victim = Live.FirstOrDefault(h => h != this && !h.IsVisible);
            if (victim == null) break;
            victim.ReleaseView();
        }
    }

    private void ReleaseView()
    {
        Live.Remove(this);
        if (_view == null) return;
        try
        {
            _body.Children.Remove(_view);
            _view.Dispose();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StackWebHost.ReleaseView");
        }
        _view = null;
        _suspended = false;
        _dirty = false;
        _initializing = false;
    }

    private async void Activate()
    {
        try
        {
            RegisterLive();
            if (_view == null)
            {
                _view = new WebView2();
                Grid.SetRow(_view, 1);
                _body.Children.Add(_view);
            }
            if (_initializing) return;

            if (_view.CoreWebView2 == null)
            {
                await InitAsync(_view);
                return;
            }

            if (_suspended)
            {
                _view.CoreWebView2.Resume();
                _suspended = false;
            }
            if (_dirty)
            {
                _dirty = false;
                _view.CoreWebView2.Reload();
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StackWebHost.Activate");
        }
    }

    private async void Deactivate()
    {
        try
        {
            var view = _view;
            if (view?.CoreWebView2 == null || _suspended) return;
            await Task.Delay(150);
            if (_disposed || IsVisible || !ReferenceEquals(view, _view) || view.CoreWebView2 == null) return;
            if (await view.CoreWebView2.TrySuspendAsync()) _suspended = true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StackWebHost.Deactivate");
        }
    }

    private async Task InitAsync(WebView2 view)
    {
        _initializing = true;
        try
        {
            var env = Horizon.Stealth.Core.StealthEnvironment.Instance;
            if (env == null) { ShowFailure("Browser engine is not ready."); return; }

            bool isolated = _data.LoginProfile == StackLoginProfile.Isolated;
            if (isolated)
            {
                var options = env.CreateCoreWebView2ControllerOptions("stack_" + _data.Id, false);
                await view.EnsureCoreWebView2Async(env, options);
            }
            else
            {
                await view.EnsureCoreWebView2Async(env);
            }

            if (_disposed || !ReferenceEquals(view, _view)) return;

            var core = view.CoreWebView2;
            await Horizon.Stealth.Core.StealthEnvironment.ApplyStealthStrategies(core);
            if (_profile.MobileUserAgent) core.Settings.UserAgent = MobileUa;
            if (isolated && _profile.PreferDark) core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;

            if (_profile.HideCss.Length > 0)
            {
                string css = System.Text.Json.JsonSerializer.Serialize(_profile.HideCss);
                string script = "(function(){var css=" + css + ";var add=function(){if(document.getElementById('hz-stack-opt'))return;var s=document.createElement('style');s.id='hz-stack-opt';s.textContent=css;(document.head||document.documentElement).appendChild(s);};document.addEventListener('DOMContentLoaded',add);if(document.readyState!=='loading')add();})();";
                await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            }

            core.DocumentTitleChanged += (_, __) => UpdateBadge(core.DocumentTitle);
            core.NavigationCompleted += (_, __) =>
            {
                if (!_disposed && _view != null) _view.ZoomFactor = _profile.Zoom;
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!string.IsNullOrWhiteSpace(e.Uri)) core.Navigate(e.Uri);
            };

            core.Navigate(_data.Url);
            LogService.Write("Stack", $"Web element started: {_data.Title} (isolated={isolated}, template={_data.SourceId})");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StackWebHost.Init");
            ShowFailure("Could not load this element.");
        }
        finally
        {
            _initializing = false;
        }
    }

    private void UpdateBadge(string? title)
    {
        var match = System.Text.RegularExpressions.Regex.Match(title ?? "", @"[\(\[](\d{1,4})[\)\]]");
        if (!match.Success)
        {
            _badge.Visibility = Visibility.Collapsed;
            return;
        }
        _badgeText.Text = match.Groups[1].Value;
        _badge.Visibility = Visibility.Visible;
    }

    private void ShowFailure(string message)
    {
        if (_view != null)
        {
            _body.Children.Remove(_view);
            try { _view.Dispose(); } catch { }
            _view = null;
        }
        var text = new TextBlock
        {
            Text = message,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
        Grid.SetRow(text, 1);
        _body.Children.Add(text);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Stop();
        ReleaseView();
    }
}

public static class StackElementBuilders
{
    private static readonly Dictionary<StackElementKind, Func<StackElementData, FrameworkElement?>> _factories = new();
    private static readonly Dictionary<string, Func<StackElementData, FrameworkElement?>> _sourceFactories = new();

    public static void Register(StackElementKind kind, Func<StackElementData, FrameworkElement?> factory)
    {
        _factories[kind] = factory;
    }

    public static void RegisterSource(StackElementKind kind, string sourceId, Func<StackElementData, FrameworkElement?> factory)
    {
        _sourceFactories[kind + ":" + sourceId] = factory;
    }

    public static void RegisterBuiltInWidgets()
    {
        RegisterSource(StackElementKind.Widget, "cpu", d => BuildLiveCard(d, () =>
        {
            if (Application.Current?.MainWindow is not MainWindow main) return ("--", "Unavailable");
            return (main.GetCpuPercent().ToString("F1") + "%", "Horizon CPU usage");
        }, 1000));

        RegisterSource(StackElementKind.Widget, "ram", d => BuildLiveCard(d, () =>
        {
            if (Application.Current?.MainWindow is not MainWindow main) return ("--", "Unavailable");
            return (main.GetCachedRamMb() + " MB", "Horizon memory");
        }, 2000));

        RegisterSource(StackElementKind.Widget, "notifications", d => BuildLiveCard(d, () =>
        {
            int unread = NotificationCenterService.UnreadCount;
            var latest = NotificationCenterService.History.Count > 0 ? NotificationCenterService.History[0] : null;
            return (unread.ToString(), latest != null && !string.IsNullOrWhiteSpace(latest.Title) ? latest.Title : "No notifications");
        }, 1000));
    }

    public static void RegisterWebElements()
    {
        Register(StackElementKind.Site, d => new StackWebHost(d));
        Register(StackElementKind.Template, d => new StackWebHost(d));
    }

    public static void RegisterLaunchWidgets()
    {
        RegisterSource(StackElementKind.Widget, "notes", d => BuildOpenCard(d, "No notes yet", () =>
        {
            if (Application.Current?.MainWindow is not MainWindow main) return "";
            string text = main.GetNotesPreview();
            return string.Join("\n", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(6));
        }, 2000));

        RegisterSource(StackElementKind.Widget, "calculator", d => BuildOpenCard(d, "Standard, scientific and AI calculator", null, 0));
        RegisterSource(StackElementKind.Widget, "converter", d => BuildOpenCard(d, "Unit and currency converter", null, 0));
    }

    private static FrameworkElement BuildOpenCard(StackElementData data, string hint, Func<string>? read, int intervalMs)
    {
        var icon = new TextBlock
        {
            Text = data.Icon,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 30,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var title = new TextBlock
        {
            Text = data.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var body = new TextBlock
        {
            Text = hint,
            FontSize = 13,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 150,
            Margin = new Thickness(0, 14, 0, 0),
            TextAlignment = read == null ? TextAlignment.Center : TextAlignment.Left
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var openHint = new TextBlock
        {
            Text = "Click to open",
            FontSize = 11,
            Opacity = 0.45,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        openHint.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(icon);
        panel.Children.Add(title);
        panel.Children.Add(body);
        panel.Children.Add(openHint);

        var card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Cursor = Cursors.Hand,
            Child = panel
        };

        card.MouseLeftButtonUp += (_, __) =>
        {
            if (Application.Current?.MainWindow is MainWindow main) main.OpenStackWidgetWindow(data.SourceId);
        };

        if (read == null) return card;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };

        void Update()
        {
            try
            {
                string text = read();
                body.Text = string.IsNullOrWhiteSpace(text) ? hint : text;
            }
            catch (Exception ex)
            {
                timer.Stop();
                LogService.RecordCrash(ex, "StackElementBuilders.OpenCard");
            }
        }

        timer.Tick += (_, __) => Update();
        card.Loaded += (_, __) =>
        {
            Update();
            timer.Start();
        };
        card.Unloaded += (_, __) => timer.Stop();
        return card;
    }

    public static void RegisterLaunchWidgets()
    {
        RegisterSource(StackElementKind.Widget, "notes", d => BuildOpenCard(d, "No notes yet", () =>
        {
            if (Application.Current?.MainWindow is not MainWindow main) return "";
            string text = main.GetNotesPreview();
            return string.Join("\n", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(6));
        }, 2000));

        RegisterSource(StackElementKind.Widget, "calculator", d => BuildOpenCard(d, "Standard, scientific and AI calculator", null, 0));
        RegisterSource(StackElementKind.Widget, "converter", d => BuildOpenCard(d, "Unit and currency converter", null, 0));
    }

    private static FrameworkElement BuildOpenCard(StackElementData data, string hint, Func<string>? read, int intervalMs)
    {
        var icon = new TextBlock
        {
            Text = data.Icon,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 30,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var title = new TextBlock
        {
            Text = data.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var body = new TextBlock
        {
            Text = hint,
            FontSize = 13,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 150,
            Margin = new Thickness(0, 14, 0, 0),
            TextAlignment = read == null ? TextAlignment.Center : TextAlignment.Left
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var openHint = new TextBlock
        {
            Text = "Click to open",
            FontSize = 11,
            Opacity = 0.45,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        openHint.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(icon);
        panel.Children.Add(title);
        panel.Children.Add(body);
        panel.Children.Add(openHint);

        var card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Cursor = Cursors.Hand,
            Child = panel
        };

        card.MouseLeftButtonUp += (_, __) =>
        {
            if (Application.Current?.MainWindow is MainWindow main) main.OpenStackWidgetWindow(data.SourceId);
        };

        if (read == null) return card;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };

        void Update()
        {
            try
            {
                string text = read();
                body.Text = string.IsNullOrWhiteSpace(text) ? hint : text;
            }
            catch (Exception ex)
            {
                timer.Stop();
                LogService.RecordCrash(ex, "StackElementBuilders.OpenCard");
            }
        }

        timer.Tick += (_, __) => Update();
        card.Loaded += (_, __) =>
        {
            Update();
            timer.Start();
        };
        card.Unloaded += (_, __) => timer.Stop();
        return card;
    }

    private static FrameworkElement BuildLiveCard(StackElementData data, Func<(string Main, string Sub)> read, int intervalMs)
    {
        var icon = new TextBlock
        {
            Text = data.Icon,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 30,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var title = new TextBlock
        {
            Text = data.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.7,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var value = new TextBlock
        {
            Text = "--",
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        value.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var sub = new TextBlock
        {
            Text = "",
            FontSize = 12,
            Opacity = 0.6,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(icon);
        panel.Children.Add(title);
        panel.Children.Add(value);
        panel.Children.Add(sub);

        var card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Child = panel
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };

        void Update()
        {
            try
            {
                var reading = read();
                value.Text = reading.Main;
                sub.Text = reading.Sub;
            }
            catch (Exception ex)
            {
                timer.Stop();
                LogService.RecordCrash(ex, "StackElementBuilders.LiveCard");
            }
        }

        timer.Tick += (_, __) => Update();
        card.Loaded += (_, __) =>
        {
            Update();
            timer.Start();
        };
        card.Unloaded += (_, __) => timer.Stop();
        return card;
    }

    public static FrameworkElement Build(StackElementData data)
    {
        try
        {
            if (_sourceFactories.TryGetValue(data.Kind + ":" + data.SourceId, out var bySource))
            {
                var view = bySource(data);
                if (view != null) return view;
            }
            if (_factories.TryGetValue(data.Kind, out var byKind))
            {
                var view = byKind(data);
                if (view != null) return view;
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StackElementBuilders.Build");
        }
        return BuildPlaceholder(data);
    }

    public static FrameworkElement BuildPlaceholder(StackElementData data, string textBrushKey = "HomeTextBrush")
    {
        string subtitle;
        if (data.Kind == StackElementKind.Widget)
        {
            subtitle = "Widget: " + (StackElementCatalog.Find(data.Kind, data.SourceId)?.Name ?? data.SourceId);
        }
        else
        {
            string host = Uri.TryCreate(data.Url, UriKind.Absolute, out var uri) ? uri.Host : data.Url;
            string prefix = data.Kind == StackElementKind.Template
                ? (StackElementCatalog.Find(data.Kind, data.SourceId)?.Name ?? "Template") + ": "
                : "";
            subtitle = prefix + host;
        }

        var icon = new TextBlock
        {
            Text = data.Icon,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 34,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, textBrushKey);

        var title = new TextBlock
        {
            Text = data.Title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, textBrushKey);

        var sub = new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Opacity = 0.6,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, textBrushKey);

        var note = new TextBlock
        {
            Text = "Content not available yet",
            FontSize = 11,
            Opacity = 0.4,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, textBrushKey);

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(icon);
        panel.Children.Add(title);
        panel.Children.Add(sub);
        panel.Children.Add(note);

        return new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Child = panel
        };
    }
}
