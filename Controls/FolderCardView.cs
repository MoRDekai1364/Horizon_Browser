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

    public static int FullLimit(int count, int n)
    {
        return Dims(count, n).FullCount;
    }

    public static (int Cols, int Rows, int FullCount) Dims(int count, int n)
    {
        if (count <= OneRowMax) return (count, 1, count);
        if (count <= n * n)
        {
            int cols = Math.Min(count == 4 ? 2 : n, n);
            return (cols, (int)Math.Ceiling(count / (double)cols), count);
        }
        return (n, n, n * n - 1);
    }

    public static (double W, double H) CellSize(int count, int n)
    {
        var (cols, rows, _) = Dims(count, n);
        double w = cols * Tile + (cols - 1) * Gap + CardChrome + CellMargin * 2;
        double h = rows * Tile + (rows - 1) * Gap + CardChrome + NameGap + NameH + CellMargin * 2;
        return (w, h);
    }

    public FolderCardView(DrawerGroup group, int n, bool clusterOnly = false)
    {
        Group = group;
        int count = group.Items.Count;
        var (cols, rows, fullLimit) = Dims(count, n);
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

        if (count > fullLimit)
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
            Canvas.SetLeft(cluster, (cols - 1) * (Tile + Gap));
            Canvas.SetTop(cluster, (rows - 1) * (Tile + Gap));
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

public sealed class StartMenuView : UserControl
{
    private const int CardGridSize = 2;
    private const double CardScale = 1.25;
    private const double DotsH = PagerDots.Cell;
    private const int AnimationMs = 300;

    private readonly Grid _root;
    private readonly Grid _viewport;
    private readonly StackPanel _strip;
    private readonly TranslateTransform _slide = new();
    private readonly PagerDots _dots;
    private readonly TextBox _filter;
    private readonly FolderSheetHost _sheet = new();
    private List<DrawerGroup> _all = new();
    private List<DrawerGroup> _shown = new();
    private int _page;
    private int _pageCount;
    private double _pageWidth;
    private bool _animating;
    private bool _queued;

    public event Action<DrawerItem>? ItemActivated;
    public event Action? CloseRequested;

    public StartMenuView(string titleText)
    {
        var title = new TextBlock
        {
            Text = titleText,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(14, 0, 14, 0)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var back = RoundButton("\u2039", 20, new Thickness(0, -3, 0, 0), "Back", () =>
        {
            if (_sheet.IsOpen) _sheet.Close();
            else CloseRequested?.Invoke();
        });
        var close = RoundButton("\u2715", 12, new Thickness(0), "Close", () => CloseRequested?.Invoke());

        _filter = new TextBox
        {
            Width = 260,
            Height = 28,
            FontSize = 14,
            Padding = new Thickness(10, 0, 10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(12, 0, 12, 0),
            ToolTip = "Filter"
        };
        _filter.SetResourceReference(TextBox.ForegroundProperty, "HomeTextBrush");
        _filter.SetResourceReference(TextBox.CaretBrushProperty, "HomeTextBrush");
        _filter.TextChanged += (_, _) =>
        {
            _page = 0;
            ApplyFilter();
        };

        var header = new Grid { Height = 36, Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(back, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(_filter, 2);
        Grid.SetColumn(close, 3);
        header.Children.Add(back);
        header.Children.Add(title);
        header.Children.Add(_filter);
        header.Children.Add(close);

        _strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _slide
        };
        _viewport = new Grid { ClipToBounds = true };
        _viewport.Children.Add(_strip);

        _dots = new PagerDots(Orientation.Horizontal)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Hidden
        };
        _dots.PageRequested += GoTo;

        _root = new Grid { Margin = new Thickness(28, 20, 28, 12) };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DotsH) });
        Grid.SetRow(header, 0);
        Grid.SetRow(_viewport, 1);
        Grid.SetRow(_dots, 2);
        Grid.SetRow(_sheet, 1);
        Grid.SetRowSpan(_sheet, 2);
        _sheet.CornerRadius = new CornerRadius(18);
        _sheet.Underlays.Add(_viewport);
        _sheet.Underlays.Add(_dots);
        _root.Children.Add(header);
        _root.Children.Add(_viewport);
        _root.Children.Add(_dots);
        _root.Children.Add(_sheet);
        Content = _root;

        _viewport.SizeChanged += (_, _) =>
        {
            if (_pageCount == 0)
            {
                Relayout();
                return;
            }
            if (_queued) return;
            _queued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _queued = false;
                Relayout();
            }));
        };

        PreviewMouseWheel += (_, e) =>
        {
            if (_sheet.IsOpen) return;
            e.Handled = true;
            if (_pageCount <= 1) return;
            GoTo(_page + (e.Delta < 0 ? 1 : -1));
        };
    }

    public void SetGroups(IReadOnlyList<DrawerGroup> groups)
    {
        _all = groups.ToList();
        ApplyFilter();
    }

    private static Border RoundButton(string glyph, double fontSize, Thickness glyphMargin, string tip, Action onClick)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontSize = fontSize,
            Margin = glyphMargin,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
        var button = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = text
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return button;
    }

    private void ApplyFilter()
    {
        string query = (_filter.Text ?? "").Trim();
        if (query.Length == 0)
        {
            _shown = _all.ToList();
        }
        else
        {
            var result = new List<DrawerGroup>();
            foreach (var group in _all)
            {
                if (group.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(group);
                    continue;
                }
                var items = group.Items.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                if (items.Count > 0) result.Add(new DrawerGroup { Name = group.Name, Items = items });
            }
            _shown = result;
        }
        _pageCount = 0;
        Relayout();
    }

    private void Relayout()
    {
        double width = _viewport.ActualWidth;
        double height = _viewport.ActualHeight;
        if (width <= 0 || height <= 0) return;

        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _animating = false;
        _strip.Children.Clear();
        _pageWidth = width;

        if (_shown.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Nothing to show",
                FontSize = 16,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
            _pageCount = 1;
            _page = 0;
            _slide.X = 0;
            _strip.Children.Add(new Grid { Width = width, Height = height, Children = { empty } });
            UpdateDots();
            return;
        }

        double cellW = 0;
        double cellH = 0;
        foreach (var group in _shown)
        {
            var size = FolderCardView.CellSize(group.Items.Count, CardGridSize);
            cellW = Math.Max(cellW, size.W * CardScale);
            cellH = Math.Max(cellH, size.H * CardScale);
        }

        int cols = Math.Max(1, (int)Math.Floor(width / cellW));
        int rows = Math.Max(1, (int)Math.Floor(height / cellH));
        int perPage = cols * rows;
        _pageCount = Math.Max(1, (int)Math.Ceiling(_shown.Count / (double)perPage));
        _page = Math.Min(_page, _pageCount - 1);
        _slide.X = -_page * width;

        for (int p = 0; p < _pageCount; p++)
        {
            var host = new WrapPanel
            {
                Width = cols * cellW,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            int start = p * perPage;
            int end = Math.Min(start + perPage, _shown.Count);
            for (int i = start; i < end; i++) host.Children.Add(BuildCell(_shown[i], cellW, cellH));
            _strip.Children.Add(new Grid { Width = width, Height = height, Children = { host } });
        }

        UpdateDots();
    }

    private FrameworkElement BuildCell(DrawerGroup group, double cellW, double cellH)
    {
        var card = new FolderCardView(group, CardGridSize, true)
        {
            LayoutTransform = new ScaleTransform(CardScale, CardScale),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        card.ItemActivated += item => ItemActivated?.Invoke(item);
        card.OpenRequested += source => OpenSheet(group, source);
        return new Grid { Width = cellW, Height = cellH, Children = { card } };
    }

    private void OpenSheet(DrawerGroup group, FolderCardView source)
    {
        var view = new OpenFolderView(group);
        view.CloseRequested += () => _sheet.Close();
        view.ItemActivated += item => ItemActivated?.Invoke(item);
        Point offset = _sheet.TranslatePoint(new Point(0, 0), _root);
        Rect bounds = source.ClusterBounds(_root);
        _sheet.Open(view, new Rect(bounds.X - offset.X, bounds.Y - offset.Y, bounds.Width, bounds.Height));
    }

    private void UpdateDots()
    {
        if (_pageCount <= 1)
        {
            _dots.Visibility = Visibility.Hidden;
            return;
        }

        _dots.Visibility = Visibility.Visible;
        _dots.SetState(_pageCount, _page);
    }

    private void GoTo(int target)
    {
        target = Math.Clamp(target, 0, Math.Max(_pageCount - 1, 0));
        if (target == _page || _animating) return;

        _page = target;
        UpdateDots();
        _animating = true;
        var animation = new DoubleAnimation(-_page * _pageWidth, TimeSpan.FromMilliseconds(AnimationMs))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) =>
        {
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = -_page * _pageWidth;
            _animating = false;
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, animation);
        HomeGlassMotion.Pulse(AnimationMs);
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
        Horizon.Stealth.Services.HomeGlass.SetExclude(tile, true);
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
        HomeGlassMotion.Pulse((int)(OpenSeconds * 1000));
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
        HomeGlassMotion.Pulse((int)(OpenSeconds * 1000));
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
