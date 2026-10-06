using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

public sealed class FolderDrawerView : UserControl
{
    private const int GridSize = 3;
    private const int OneRowMax = 3;
    private const int ClusterMax = 4;
    private const double Tile = 52;
    private const double Gap = 6;
    private const double MiniTile = 24;
    private const double MiniGap = 4;

    private readonly TextBlock _title;
    private readonly WrapPanel _cards;

    public event Action<DrawerGroup>? FolderOpenRequested;
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

        _cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _cards
        };
        scroll.PreviewMouseWheel += Scroll_PreviewMouseWheel;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_title, 0);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(_title);
        layout.Children.Add(scroll);

        Content = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Child = layout
        };
    }

    public void SetGroups(IReadOnlyList<DrawerGroup> groups)
    {
        _cards.Children.Clear();
        foreach (var group in groups)
        {
            if (group.Items.Count == 0) continue;
            _cards.Children.Add(BuildFolder(group));
        }
    }

    private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableHeight > 0) return;
        if (viewer.Parent is not UIElement parent) return;
        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = viewer
        });
    }

    private FrameworkElement BuildFolder(DrawerGroup group)
    {
        int count = group.Items.Count;
        bool oneRow = count <= OneRowMax;
        int cols = oneRow ? count : GridSize;
        int rows = oneRow ? 1 : GridSize;
        int fullLimit = oneRow ? count : GridSize * GridSize - 1;
        int full = Math.Min(count, fullLimit);

        double width = cols * Tile + (cols - 1) * Gap;
        double height = rows * Tile + (rows - 1) * Gap;
        var canvas = new Canvas { Width = width, Height = height };

        for (int i = 0; i < full; i++)
        {
            var tile = BuildTile(group.Items[i], Tile, 32, 13, true);
            Canvas.SetLeft(tile, (i % cols) * (Tile + Gap));
            Canvas.SetTop(tile, (i / cols) * (Tile + Gap));
            canvas.Children.Add(tile);
        }

        if (!oneRow && count > fullLimit)
        {
            int extra = Math.Min(ClusterMax, count - fullLimit);
            var cluster = new Canvas { Width = Tile, Height = Tile, IsHitTestVisible = false };
            for (int k = 0; k < extra; k++)
            {
                var mini = BuildTile(group.Items[fullLimit + k], MiniTile, 15, 7, false);
                Canvas.SetLeft(mini, (k % 2) * (MiniTile + MiniGap));
                Canvas.SetTop(mini, (k / 2) * (MiniTile + MiniGap));
                cluster.Children.Add(mini);
            }
            Canvas.SetLeft(cluster, (GridSize - 1) * (Tile + Gap));
            Canvas.SetTop(cluster, (GridSize - 1) * (Tile + Gap));
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
            Opacity = 0.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width + 16,
            Margin = new Thickness(0, 6, 0, 0)
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var cell = new StackPanel
        {
            Margin = new Thickness(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        cell.Children.Add(card);
        cell.Children.Add(name);
        cell.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            FolderOpenRequested?.Invoke(group);
        };
        return cell;
    }

    private FrameworkElement BuildTile(DrawerItem item, double size, double iconSize, double radius, bool interactive)
    {
        FrameworkElement glyph;
        if (!string.IsNullOrEmpty(item.Emoji))
        {
            glyph = new TextBlock
            {
                Text = item.Emoji,
                FontSize = iconSize * 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            glyph = new Image
            {
                Source = item.Icon,
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

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
        tile.SetResourceReference(Border.BackgroundProperty, "HomeAccentBrush");

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