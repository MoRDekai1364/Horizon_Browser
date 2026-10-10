using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    public FolderCardView(DrawerGroup group, int n)
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
        Cursor = Cursors.Hand;
        Children.Add(card);
        Children.Add(name);
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenRequested?.Invoke(this);
        };
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
