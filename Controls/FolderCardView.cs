using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Horizon.Stealth.Controls;

public sealed class FolderCardView : StackPanel
{
    public const int OneRowMax = 3;
    public const int ClusterMax = 4;
    public const double Tile = 48;
    public const double Gap = 5;
    public const double MiniTile = 22;
    public const double MiniGap = 4;
    public const double CardChrome = 18;
    public const double CellMargin = 6;
    public const double NameGap = 6;
    public const double NameH = 20;

    private Canvas? _cluster;

    public DrawerGroup Group { get; }

    public event Action<FolderCardView>? OpenRequested;
    public event Action<DrawerItem>? ItemActivated;

    public static (double W, double H) CellSize(int count, int n)
    {
        bool oneRow = count <= OneRowMax;
        int cols = oneRow ? count : n;
        int rows = oneRow ? 1 : n;
        double w = cols * Tile + (cols - 1) * Gap + CardChrome + CellMargin * 2;
        double h = rows * Tile + (rows - 1) * Gap + CardChrome + NameGap + NameH + CellMargin * 2;
        return (w, h);
    }

    public FolderCardView(DrawerGroup group, int n, bool clusterOnly = false)
    {
        Group = group;
        int count = group.Items.Count;
        bool oneRow = count <= OneRowMax;
        int cols = oneRow ? count : n;
        int rows = oneRow ? 1 : n;
        int fullLimit = oneRow ? count : n * n - 1;
        int full = Math.Min(count, fullLimit);

        double width = cols * Tile + (cols - 1) * Gap;
        double height = rows * Tile + (rows - 1) * Gap;
        var canvas = new Canvas { Width = width, Height = height };

        for (int i = 0; i < full; i++)
        {
            var tile = BuildTile(group.Items[i], Tile, 30, 12, true);
            Canvas.SetLeft(tile, (i % cols) * (Tile + Gap));
            Canvas.SetTop(tile, (i / cols) * (Tile + Gap));
            canvas.Children.Add(tile);
        }

        if (!oneRow && count > fullLimit)
        {
            int extra = Math.Min(ClusterMax, count - fullLimit);
            var cluster = new Canvas { Width = Tile, Height = Tile, IsHitTestVisible = false };
            _cluster = cluster;
            if (clusterOnly)
            {
                cluster.IsHitTestVisible = true;
                cluster.Background = Brushes.Transparent;
                cluster.Cursor = Cursors.Hand;
                cluster.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    OpenRequested?.Invoke(this);
                };
            }
            for (int k = 0; k < extra; k++)
            {
                var mini = BuildTile(group.Items[fullLimit + k], MiniTile, 14, 6, false);
                Canvas.SetLeft(mini, (k % 2) * (MiniTile + MiniGap));
                Canvas.SetTop(mini, (k / 2) * (MiniTile + MiniGap));
                cluster.Children.Add(mini);
            }
            Canvas.SetLeft(cluster, (n - 1) * (Tile + Gap));
            Canvas.SetTop(cluster, (n - 1) * (Tile + Gap));
            canvas.Children.Add(cluster);
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = canvas
        };

        var name = new TextBlock
        {
            Text = group.Name,
            FontSize = 14,
            Height = NameH,
            Opacity = 0.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width + CardChrome,
            Margin = new Thickness(0, NameGap, 0, 0)
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        Margin = new Thickness(CellMargin);
        Background = Brushes.Transparent;
        Children.Add(card);
        Children.Add(name);
        if (!clusterOnly)
        {
            Cursor = Cursors.Hand;
            MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenRequested?.Invoke(this);
            };
        }
    }

    public Rect ClusterBounds(Visual relativeTo)
    {
        if (_cluster == null) return new Rect(0, 0, 1, 1);
        return _cluster.TransformToAncestor(relativeTo).TransformBounds(new Rect(0, 0, _cluster.Width, _cluster.Height));
    }

    private FrameworkElement BuildTile(DrawerItem item, double size, double iconSize, double radius, bool interactive)
    {
        FrameworkElement glyph = TileFallback.Glyph(item, iconSize);

        var tile = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            IsHitTestVisible = interactive,
            Child = glyph
        };
        TileFallback.Paint(tile, item);

        if (interactive)
        {
            tile.Cursor = Cursors.Hand;
            tile.ToolTip = item.Name;
            tile.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ItemActivated?.Invoke(item);
            };
        }
        return tile;
    }
}

public sealed class FolderCardHost : WrapPanel
{
    private const double CardScale = 1.25;
    private const int CardGridSize = 2;

    public event Action<DrawerItem>? ItemActivated;
    public event Action<DrawerGroup, FolderCardView>? FolderOpenRequested;
    public event Action? RowsChanged;

    private Size _lastSignature;

    public FolderCardHost()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
    }

    public void SetGroups(IReadOnlyList<DrawerGroup> groups)
    {
        Children.Clear();
        foreach (var group in groups)
        {
            var card = new FolderCardView(group, CardGridSize, true)
            {
                LayoutTransform = new ScaleTransform(CardScale, CardScale)
            };
            card.ItemActivated += item => ItemActivated?.Invoke(item);
            card.OpenRequested += source => FolderOpenRequested?.Invoke(group, source);
            Children.Add(card);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        var signature = new Size(Math.Round(arranged.Width), Math.Round(DesiredSize.Height));
        if (signature != _lastSignature)
        {
            _lastSignature = signature;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => RowsChanged?.Invoke()));
        }
        return arranged;
    }

    public double SnapHeight(double budget, bool atLeastFirstRow)
    {
        double total = ActualHeight;
        if (total <= 0 || Children.Count == 0) return budget;
        if (total <= budget) return total;
        var tops = new List<double>();
        foreach (UIElement child in Children)
        {
            if (child is not FrameworkElement card) continue;
            double top = Math.Round(card.TranslatePoint(new Point(0, 0), this).Y);
            if (!tops.Contains(top)) tops.Add(top);
        }
        tops.Sort();
        double fit = 0;
        double firstBottom = 0;
        for (int i = 0; i < tops.Count; i++)
        {
            double bottom = i + 1 < tops.Count ? tops[i + 1] : total;
            if (i == 0) firstBottom = bottom;
            if (bottom <= budget + 0.5) fit = bottom;
        }
        if (fit > 0) return fit;
        return atLeastFirstRow ? firstBottom : budget;
    }
}

internal static class TileFallback
{
    public static bool NeedsLetter(DrawerItem item) => string.IsNullOrEmpty(item.Emoji) && item.Icon == null;

    public static FrameworkElement Glyph(DrawerItem item, double iconSize)
    {
        if (!string.IsNullOrEmpty(item.Emoji))
        {
            return new TextBlock
            {
                Text = item.Emoji,
                FontSize = iconSize * 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        if (item.Icon != null)
        {
            return new Image
            {
                Source = item.Icon,
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new TextBlock
        {
            Text = LetterOf(item.Name),
            FontSize = iconSize * 0.72,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static void Paint(Border tile, DrawerItem item)
    {
        if (NeedsLetter(item)) tile.Background = ColorFor(item.Name);
        else tile.SetResourceReference(Border.BackgroundProperty, "HomeAccentBrush");
    }

    private static string LetterOf(string name)
    {
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        }
        return "?";
    }

    private static Brush ColorFor(string name)
    {
        int hash = 17;
        foreach (char c in name.ToLowerInvariant()) hash = unchecked(hash * 31 + c);
        double hue = Math.Abs(hash % 360);
        var brush = new SolidColorBrush(FromHsl(hue, 0.55, 0.40));
        brush.Freeze();
        return brush;
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double segment = hue / 60.0;
        double x = chroma * (1 - Math.Abs(segment % 2 - 1));
        double r = 0, g = 0, b = 0;
        if (segment < 1) { r = chroma; g = x; }
        else if (segment < 2) { r = x; g = chroma; }
        else if (segment < 3) { g = chroma; b = x; }
        else if (segment < 4) { g = x; b = chroma; }
        else if (segment < 5) { r = x; b = chroma; }
        else { r = chroma; b = x; }
        double m = lightness - chroma / 2;
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

public sealed class FolderSheetHost : Grid
{
    private const double OpenSeconds = 0.3;

    private readonly Border _scrim;
    private readonly Border _sheet;
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _move = new();
    private Rect _origin;
    private bool _open;
    private bool _closing;

    public event Action? Closed;

    public IList<UIElement> Underlays { get; } = new List<UIElement>();

    public FolderSheetHost()
    {
        Visibility = Visibility.Collapsed;
        Focusable = true;
        FocusVisualStyle = null;

        _scrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            Opacity = 0
        };

        _sheet = new Border
        {
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            RenderTransformOrigin = new Point(0, 0)
        };
        _sheet.SetResourceReference(Border.BackgroundProperty, "SharedSearchBgBrush");
        _sheet.SetResourceReference(Border.BorderBrushProperty, "SharedSearchBorderBrush");
        var transforms = new TransformGroup();
        transforms.Children.Add(_scale);
        transforms.Children.Add(_move);
        _sheet.RenderTransform = transforms;

        Children.Add(_scrim);
        Children.Add(_sheet);

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    public CornerRadius CornerRadius
    {
        get => _sheet.CornerRadius;
        set
        {
            _sheet.CornerRadius = value;
            _scrim.CornerRadius = value;
        }
    }

    public bool IsOpen => _open && !_closing;

    public FrameworkElement? Content => _sheet.Child as FrameworkElement;

    public void Open(FrameworkElement content, Rect origin)
    {
        _closing = false;
        if (_open)
        {
            _sheet.Child = content;
            return;
        }

        _open = true;
        _origin = origin;
        _sheet.Child = content;
        Visibility = Visibility.Visible;
        UpdateLayout();

        double width = Math.Max(1, ActualWidth);
        double height = Math.Max(1, ActualHeight);
        double startX = Math.Clamp(origin.Width / width, 0.05, 1);
        double startY = Math.Clamp(origin.Height / height, 0.05, 1);

        ClearAnimations();
        _scale.ScaleX = startX;
        _scale.ScaleY = startY;
        _move.X = origin.X;
        _move.Y = origin.Y;
        _sheet.Opacity = 0;
        _scrim.Opacity = 0;

        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, SnapMotion.Move(1));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, SnapMotion.Move(1));
        _move.BeginAnimation(TranslateTransform.XProperty, SnapMotion.Move(0));
        _move.BeginAnimation(TranslateTransform.YProperty, SnapMotion.Move(0));
        _sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.18)));
        _scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(OpenSeconds)));
        foreach (var underlay in Underlays)
        {
            underlay.IsHitTestVisible = false;
            underlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.2)));
        }
        Focus();
    }

    public void Close()
    {
        if (!_open || _closing) return;
        _closing = true;

        double width = Math.Max(1, ActualWidth);
        double height = Math.Max(1, ActualHeight);
        double endX = Math.Clamp(_origin.Width / width, 0.05, 1);
        double endY = Math.Clamp(_origin.Height / height, 0.05, 1);
        var duration = new Duration(TimeSpan.FromSeconds(OpenSeconds));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(endX, duration) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(endY, duration) { EasingFunction = ease });
        _move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_origin.X, duration) { EasingFunction = ease });
        _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_origin.Y, duration) { EasingFunction = ease });
        _sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
        foreach (var underlay in Underlays)
            underlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });

        var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (!_closing) return;
            Finish();
        };
        _scrim.BeginAnimation(OpacityProperty, fade);
    }

    private void Finish()
    {
        ClearAnimations();
        foreach (var underlay in Underlays)
        {
            underlay.BeginAnimation(OpacityProperty, null);
            underlay.Opacity = 1;
            underlay.IsHitTestVisible = true;
        }
        Visibility = Visibility.Collapsed;
        _sheet.Child = null;
        _open = false;
        _closing = false;
        Closed?.Invoke();
    }

    private void ClearAnimations()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _move.BeginAnimation(TranslateTransform.XProperty, null);
        _move.BeginAnimation(TranslateTransform.YProperty, null);
        _sheet.BeginAnimation(OpacityProperty, null);
        _scrim.BeginAnimation(OpacityProperty, null);
        _sheet.Opacity = 1;
        _scrim.Opacity = 1;
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
        _move.X = 0;
        _move.Y = 0;
    }
}
