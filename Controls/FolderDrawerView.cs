using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Horizon.Stealth.Controls;

public sealed class DrawerItem
{
    public string Name { get; init; } = "";
    public string Emoji { get; init; } = "";
    public ImageSource? Icon { get; init; }
    public object? Source { get; init; }
}

public sealed class DrawerGroup
{
    public string Name { get; init; } = "";
    public List<DrawerItem> Items { get; init; } = new();
}

public sealed class OpenFolderView : UserControl
{
    private const double Tile = 64;
    private const double IconSize = 40;
    private const double Gap = 16;
    private const double DotsH = 20;
    private const int AnimationMs = 300;

    private readonly DrawerGroup _group;
    private readonly Grid _viewport;
    private readonly StackPanel _strip;
    private readonly TranslateTransform _slide = new();
    private readonly StackPanel _dots;
    private readonly DispatcherTimer _timer;

    private int _page;
    private int _pageCount;
    private double _pageWidth;
    private bool _animating;

    public event Action<DrawerItem>? ItemActivated;
    public event Action? CloseRequested;

    public OpenFolderView(DrawerGroup group)
    {
        _group = group;

        var title = new TextBlock
        {
            Text = group.Name,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(36, 0, 36, 0)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var closeGlyph = new TextBlock
        {
            Text = "\u2715",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeGlyph.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
        var close = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Close",
            Child = closeGlyph
        };
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseRequested?.Invoke();
        };

        var header = new Grid { Height = 36, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(title);
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

        _dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Hidden
        };

        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DotsH) });
        Grid.SetRow(header, 0);
        Grid.SetRow(_viewport, 1);
        Grid.SetRow(_dots, 2);
        layout.Children.Add(header);
        layout.Children.Add(_viewport);
        layout.Children.Add(_dots);
        Content = layout;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Relayout();
        };
        _viewport.SizeChanged += (_, _) =>
        {
            _timer.Stop();
            _timer.Start();
        };
        bool queued = false;
        _viewport.SizeChanged += (_, _) =>
        {
            if (_pageCount == 0)
            {
                Relayout();
                return;
            }
            if (queued) return;
            queued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                queued = false;
                Relayout();
            }));
        };
        PreviewMouseWheel += Folder_PreviewMouseWheel;
    }

    private void Relayout()
    {
        double width = _viewport.ActualWidth;
        double height = _viewport.ActualHeight;
        if (width <= 0 || height <= 0) return;

        int cols = Math.Max(1, (int)Math.Floor(width / (Tile + Gap)));
        int rows = Math.Max(1, (int)Math.Floor(height / (Tile + Gap)));
        int perPage = cols * rows;
        int count = _group.Items.Count;

        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _animating = false;
        _strip.Children.Clear();
        _pageWidth = width;
        _pageCount = Math.Max(1, (int)Math.Ceiling(count / (double)perPage));
        _page = Math.Min(_page, _pageCount - 1);
        _slide.X = -_page * width;

        for (int p = 0; p < _pageCount; p++)
        {
            var rowsHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            int start = p * perPage;
            int end = Math.Min(start + perPage, count);
            for (int rowStart = start; rowStart < end; rowStart += cols)
            {
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                int rowEnd = Math.Min(rowStart + cols, end);
                for (int i = rowStart; i < rowEnd; i++) rowPanel.Children.Add(BuildTile(_group.Items[i]));
                rowsHost.Children.Add(rowPanel);
            }
            _strip.Children.Add(new Grid { Width = width, Height = height, Children = { rowsHost } });
        }

        UpdateDots();
    }

    private FrameworkElement BuildTile(DrawerItem item)
    {
        FrameworkElement glyph;
        if (!string.IsNullOrEmpty(item.Emoji))
        {
            glyph = new TextBlock
            {
                Text = item.Emoji,
                FontSize = IconSize * 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            glyph = new Image
            {
                Source = item.Icon,
                Width = IconSize,
                Height = IconSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        var tile = new Border
        {
            Width = Tile,
            Height = Tile,
            Margin = new Thickness(Gap / 2),
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Cursor = Cursors.Hand,
            ToolTip = item.Name,
            Child = glyph
        };
        tile.SetResourceReference(Border.BackgroundProperty, "HomeAccentBrush");
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ItemActivated?.Invoke(item);
        };
        return tile;
    }

    private void UpdateDots()
    {
        _dots.Children.Clear();
        if (_pageCount <= 1)
        {
            _dots.Visibility = Visibility.Hidden;
            return;
        }

        _dots.Visibility = Visibility.Visible;
        for (int i = 0; i < _pageCount; i++)
        {
            bool active = i == _page;
            var dot = new Ellipse
            {
                Width = active ? 8 : 6,
                Height = active ? 8 : 6,
                Margin = new Thickness(3, 0, 3, 0),
                Opacity = active ? 0.95 : 0.35,
                Cursor = Cursors.Hand,
                Tag = i
            };
            dot.SetResourceReference(Shape.FillProperty, "HomeTextBrush");
            dot.MouseLeftButtonUp += Dot_Click;
            _dots.Children.Add(dot);
        }
    }

    private void Dot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement dot && dot.Tag is int target)
        {
            e.Handled = true;
            GoTo(target);
        }
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
    }

    private void Folder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (_pageCount <= 1) return;
        GoTo(_page + (e.Delta < 0 ? 1 : -1));
    }
}

public sealed class FolderDrawerView : UserControl
{
    private const int MinN = 3;
    private const int MaxN = 6;
    private const int OneRowMax = FolderCardView.OneRowMax;
    private int _sizeEvents;
    private bool _relayoutQueued;
    private int _relayoutDone;
    private const double DotsH = 20;
    private const int AnimationMs = 300;

    private readonly TextBlock _title;
    private readonly Grid _viewport;
    private readonly StackPanel _strip;
    private readonly TranslateTransform _slide = new();
    private readonly StackPanel _dots;
    private readonly DispatcherTimer _timer;

    private List<DrawerGroup> _groups = new();
    private int _page;
    private int _pageCount;
    private double _pageWidth;
    private bool _animating;

    public event Action<DrawerGroup, Rect>? FolderOpenRequested;
    public event Action<DrawerItem>? ItemActivated;

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public FolderDrawerView()
    {
        _title = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        _strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _slide
        };

        _dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = DotsH,
            Visibility = Visibility.Collapsed
        };

        _viewport = new Grid { ClipToBounds = true };
        _viewport.Children.Add(_strip);
        _viewport.Children.Add(_dots);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_title, 0);
        Grid.SetRow(_viewport, 1);
        layout.Children.Add(_title);
        layout.Children.Add(_viewport);

        Content = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Child = layout
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Relayout();
        };
        _viewport.SizeChanged += (_, _) =>
        {
            _timer.Stop();
            _timer.Start();
        };
        _viewport.SizeChanged += (_, _) =>
        {
            _sizeEvents++;
            if (_pageCount == 0)
            {
                Relayout();
                return;
            }
            if (_relayoutQueued) return;
            _relayoutQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _relayoutQueued = false;
                Relayout();
            }));
        };
        PreviewMouseWheel += Drawer_PreviewMouseWheel;
    }

    public string DebugInfo()
    {
        return "groups=" + _groups.Count
            + " items=" + _groups.Sum(g => g.Items.Count)
            + " viewport=" + _viewport.ActualWidth.ToString("0") + "x" + _viewport.ActualHeight.ToString("0")
            + " pages=" + _pageCount
            + " stripChildren=" + _strip.Children.Count
            + " visible=" + IsVisible
            + " opacity=" + Opacity.ToString("0.##")
            + " sizeEvents=" + _sizeEvents
            + " relayouts=" + _relayoutDone;
    }

    public void SetGroups(IReadOnlyList<DrawerGroup> groups)
    {
        _groups = groups.Where(g => g.Items.Count > 0).ToList();
        Relayout();
    }

    private static (double W, double H) CellSize(int count, int n) => FolderCardView.CellSize(count, n);

    private static List<List<int>> PackRows((double W, double H)[] cells, double width)
    {
        var rows = new List<List<int>>();
        var current = new List<int>();
        double x = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (current.Count > 0 && x + cells[i].W > width)
            {
                rows.Add(current);
                current = new List<int>();
                x = 0;
            }
            current.Add(i);
            x += cells[i].W;
        }
        if (current.Count > 0) rows.Add(current);
        return rows;
    }

    private static double TotalHeight(List<List<int>> rows, (double W, double H)[] cells)
    {
        double total = 0;
        foreach (var row in rows) total += row.Max(i => cells[i].H);
        return total;
    }

    private void Grow(int[] ns, (double W, double H)[] cells, double width, double height)
    {
        var stuck = new bool[ns.Length];
        while (true)
        {
            int pick = -1;
            int most = 0;
            for (int i = 0; i < ns.Length; i++)
            {
                int count = _groups[i].Items.Count;
                if (stuck[i] || count <= OneRowMax || ns[i] >= MaxN) continue;
                int hidden = count - (ns[i] * ns[i] - 1);
                if (hidden > most)
                {
                    most = hidden;
                    pick = i;
                }
            }
            if (pick < 0) return;

            var previous = cells[pick];
            ns[pick]++;
            cells[pick] = CellSize(_groups[pick].Items.Count, ns[pick]);
            if (cells[pick].W > width || TotalHeight(PackRows(cells, width), cells) > height)
            {
                ns[pick]--;
                cells[pick] = previous;
                stuck[pick] = true;
            }
        }
    }

    private (int[] Sizes, List<List<List<int>>> Pages, bool Paged) Plan(double width, double height)
    {
        int count = _groups.Count;
        var ns = Enumerable.Repeat(MinN, count).ToArray();
        var cells = new (double W, double H)[count];
        for (int i = 0; i < count; i++) cells[i] = CellSize(_groups[i].Items.Count, ns[i]);

        var rows = PackRows(cells, width);
        if (TotalHeight(rows, cells) <= height)
        {
            Grow(ns, cells, width, height);
            rows = PackRows(cells, width);
            return (ns, new List<List<List<int>>> { rows }, false);
        }

        double pageHeight = height - DotsH;
        var pages = new List<List<List<int>>>();
        var page = new List<List<int>>();
        double used = 0;
        foreach (var row in rows)
        {
            double rowHeight = row.Max(i => cells[i].H);
            if (page.Count > 0 && used + rowHeight > pageHeight)
            {
                pages.Add(page);
                page = new List<List<int>>();
                used = 0;
            }
            page.Add(row);
            used += rowHeight;
        }
        if (page.Count > 0) pages.Add(page);
        return (ns, pages, true);
    }

    private void Relayout()
    {
        double width = _viewport.ActualWidth;
        double height = _viewport.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var plan = Plan(width, height);
        double pageHeight = plan.Paged ? height - DotsH : height;

        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _animating = false;
        _strip.Children.Clear();
        _pageWidth = width;
        _relayoutDone++;
        _pageCount = plan.Pages.Count;
        _page = Math.Min(_page, Math.Max(_pageCount - 1, 0));
        _slide.X = -_page * width;

        foreach (var pageRows in plan.Pages)
        {
            var rowsHost = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            foreach (var row in pageRows)
            {
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                foreach (int index in row) rowPanel.Children.Add(CreateCard(_groups[index], plan.Sizes[index]));
                rowsHost.Children.Add(rowPanel);
            }
            _strip.Children.Add(new Grid { Width = width, Height = pageHeight, Children = { rowsHost } });
        }

        UpdateDots();
    }

    private void UpdateDots()
    {
        _dots.Children.Clear();
        if (_pageCount <= 1)
        {
            _dots.Visibility = Visibility.Collapsed;
            return;
        }

        _dots.Visibility = Visibility.Visible;
        for (int i = 0; i < _pageCount; i++)
        {
            bool active = i == _page;
            var dot = new Ellipse
            {
                Width = active ? 8 : 6,
                Height = active ? 8 : 6,
                Margin = new Thickness(3, 0, 3, 0),
                Opacity = active ? 0.95 : 0.35,
                Cursor = Cursors.Hand,
                Tag = i
            };
            dot.SetResourceReference(Shape.FillProperty, "HomeTextBrush");
            dot.MouseLeftButtonUp += Dot_Click;
            _dots.Children.Add(dot);
        }
    }

    private void Dot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement dot && dot.Tag is int target)
        {
            e.Handled = true;
            GoTo(target);
        }
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
    }

    private void Drawer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pageCount <= 1) return;
        int target = _page + (e.Delta < 0 ? 1 : -1);
        if (target < 0 || target >= _pageCount) return;
        e.Handled = true;
        GoTo(target);
    }

    private FolderCardView CreateCard(DrawerGroup group, int n)
    {
        var card = new FolderCardView(group, n);
        card.ItemActivated += item => ItemActivated?.Invoke(item);
        card.OpenRequested += source =>
        {
            Rect bounds = IsAncestorOf(source)
                ? new Rect(source.TranslatePoint(new Point(0, 0), this), new Size(source.ActualWidth, source.ActualHeight))
                : new Rect(0, 0, ActualWidth, ActualHeight);
            FolderOpenRequested?.Invoke(group, bounds);
        };
        return card;
    }


}