using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Input;
using System.Windows.Threading;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Controls;

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
