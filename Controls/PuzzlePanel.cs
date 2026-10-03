using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Horizon.Stealth.Controls;

public class PuzzlePanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(PuzzlePanel),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty MaxRowWidthProperty = DependencyProperty.Register(
        nameof(MaxRowWidth), typeof(double), typeof(PuzzlePanel),
        new FrameworkPropertyMetadata(1500.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double MaxRowWidth
    {
        get => (double)GetValue(MaxRowWidthProperty);
        set => SetValue(MaxRowWidthProperty, value);
    }

    private readonly List<Rect> _slots = new();
    private double _packedWidth = -1;
    private double _packedUsedWidth = -1;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width)
            ? MaxRowWidth
            : Math.Min(availableSize.Width, MaxRowWidth);

        var constraint = new Size(width, double.PositiveInfinity);
        foreach (UIElement child in InternalChildren)
            child.Measure(constraint);

        return Pack(width);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = Math.Min(Math.Max(finalSize.Width, 1), MaxRowWidth);
        bool reusable = _slots.Count == InternalChildren.Count
            && (Math.Abs(width - _packedWidth) <= 0.5 || Math.Abs(width - _packedUsedWidth) <= 0.5);
        if (!reusable) Pack(width);

        for (int i = 0; i < InternalChildren.Count && i < _slots.Count; i++)
            InternalChildren[i].Arrange(_slots[i]);

        return finalSize;
    }

    private Size Pack(double maxWidth)
    {
        _slots.Clear();
        double gap = Gap;
        double usedWidth = 0;
        double usedHeight = 0;
        var candidates = new List<double>();

        foreach (UIElement child in InternalChildren)
        {
            Size size = child.DesiredSize;

            candidates.Clear();
            candidates.Add(0);
            foreach (Rect placed in _slots)
                candidates.Add(placed.Right + gap);

            double bestX = 0;
            double bestY = double.MaxValue;

            foreach (double x in candidates)
            {
                if (x > 0 && x + size.Width > maxWidth + 0.5) continue;

                double y = 0;
                foreach (Rect placed in _slots)
                {
                    if (x < placed.Right + gap - 0.01 && x + size.Width > placed.Left - gap + 0.01)
                        y = Math.Max(y, placed.Bottom + gap);
                }

                if (y < bestY - 0.01 || (Math.Abs(y - bestY) <= 0.01 && x < bestX))
                {
                    bestY = y;
                    bestX = x;
                }
            }

            var slot = new Rect(bestX, bestY, size.Width, size.Height);
            _slots.Add(slot);
            usedWidth = Math.Max(usedWidth, slot.Right);
            usedHeight = Math.Max(usedHeight, slot.Bottom);
        }

        _packedWidth = maxWidth;
        _packedUsedWidth = usedWidth;
        return new Size(usedWidth, usedHeight);
    }
}

public static class GroupState
{
    public static readonly DependencyProperty IsCollapsedProperty =
        DependencyProperty.RegisterAttached("IsCollapsed", typeof(bool), typeof(GroupState), new PropertyMetadata(false));

    public static bool GetIsCollapsed(DependencyObject d) => (bool)d.GetValue(IsCollapsedProperty);
    public static void SetIsCollapsed(DependencyObject d, bool value) => d.SetValue(IsCollapsedProperty, value);
}

public enum GroupCollapseMode
{
    Full,
    FirstRow,
    Stack,
    Mini
}

public class AutoGridPanel : Panel
{
    public const int StackDepth = 3;
    public const double StackOffset = 12;
    private const double HiddenOffset = -10000;
    private const int MiniThreshold = 9;
    private const int MiniFullTiles = 8;
    private const int MiniClusterMax = 9;
    private static readonly ScaleTransform MiniScale2 = CreateScale(0.46);
    private static readonly ScaleTransform MiniScale3 = CreateScale(0.3);

    private static ScaleTransform CreateScale(double value)
    {
        var transform = new ScaleTransform(value, value);
        transform.Freeze();
        return transform;
    }

    private static void ResetTransform(UIElement child)
    {
        if (child.RenderTransform != Transform.Identity) child.RenderTransform = Transform.Identity;
    }

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(GroupCollapseMode), typeof(AutoGridPanel),
        new FrameworkPropertyMetadata(GroupCollapseMode.Full, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public GroupCollapseMode Mode
    {
        get => (GroupCollapseMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private int _columns = 1;
    private Size _cell;

    private void ArrangeMini(Size finalSize)
    {
        double offsetX = Math.Max(0, (finalSize.Width - 3 * _cell.Width) / 2.0);
        int shown = Math.Min(MiniClusterMax, InternalChildren.Count - MiniFullTiles);
        int g = shown <= 4 ? 2 : 3;
        ScaleTransform scale = g == 2 ? MiniScale2 : MiniScale3;
        double clusterX = offsetX + 2 * _cell.Width;
        double clusterY = 2 * _cell.Height;
        double slotWidth = _cell.Width / g;
        double slotHeight = _cell.Height / g;

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];
            Panel.SetZIndex(child, 0);

            if (i < MiniFullTiles)
            {
                ResetTransform(child);
                child.Arrange(new Rect(offsetX + (i % 3) * _cell.Width, (i / 3) * _cell.Height, _cell.Width, _cell.Height));
                continue;
            }

            int k = i - MiniFullTiles;
            if (k < shown)
            {
                if (!ReferenceEquals(child.RenderTransform, scale)) child.RenderTransform = scale;
                child.Arrange(new Rect(clusterX + (k % g) * slotWidth, clusterY + (k / g) * slotHeight, _cell.Width, _cell.Height));
            }
            else
            {
                ResetTransform(child);
                child.Arrange(new Rect(HiddenOffset, HiddenOffset, _cell.Width, _cell.Height));
            }
        }
    }

    public double HeightFor(GroupCollapseMode mode)
    {
        int count = InternalChildren.Count;
        if (count == 0) return 0;
        return mode switch
        {
            GroupCollapseMode.FirstRow => _cell.Height,
            GroupCollapseMode.Stack => _cell.Height + (Math.Min(StackDepth, count) - 1) * StackOffset,
            GroupCollapseMode.Mini => count > MiniThreshold
                ? 3 * _cell.Height
                : Math.Ceiling(count / (double)ColumnsFor(count)) * _cell.Height,
            _ => double.NaN
        };
    }

    public static int ColumnsFor(int count)
    {
        if (count <= 1) return 1;
        if (count <= 4) return 2;
        if (count <= 9) return 3;
        if (count <= 16) return 4;
        if (count <= 25) return 5;
        return 6;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = InternalChildren.Count;
        if (count == 0) return new Size(0, 0);

        double cellWidth = 0;
        double cellHeight = 0;
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(infinite);
            cellWidth = Math.Max(cellWidth, child.DesiredSize.Width);
            cellHeight = Math.Max(cellHeight, child.DesiredSize.Height);
        }

        _cell = new Size(cellWidth, cellHeight);
        bool mini = Mode == GroupCollapseMode.Mini && count > MiniThreshold;
        _columns = mini ? 3 : ColumnsFor(count);

        if (!mini && !double.IsInfinity(availableSize.Width) && cellWidth > 0)
        {
            int fit = (int)Math.Floor(availableSize.Width / cellWidth);
            _columns = Math.Max(1, Math.Min(_columns, fit));
        }

        if (Mode == GroupCollapseMode.Stack)
        {
            int shown = Math.Min(StackDepth, count);
            return new Size(cellWidth + (shown - 1) * StackOffset, cellHeight + (shown - 1) * StackOffset);
        }

        if (mini) return new Size(3 * cellWidth, 3 * cellHeight);

        int rows = Mode == GroupCollapseMode.FirstRow ? 1 : (int)Math.Ceiling(count / (double)_columns);
        return new Size(_columns * cellWidth, rows * cellHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Mode == GroupCollapseMode.Stack)
        {
            int shown = Math.Min(StackDepth, InternalChildren.Count);
            double stackWidth = _cell.Width + (shown - 1) * StackOffset;
            double stackX = Math.Max(0, (finalSize.Width - stackWidth) / 2.0);

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                UIElement child = InternalChildren[i];
                ResetTransform(child);
                if (i < shown)
                {
                    Panel.SetZIndex(child, shown - i);
                    child.Arrange(new Rect(stackX + i * StackOffset, i * StackOffset, _cell.Width, _cell.Height));
                }
                else
                {
                    Panel.SetZIndex(child, 0);
                    child.Arrange(new Rect(HiddenOffset, HiddenOffset, _cell.Width, _cell.Height));
                }
            }

            return finalSize;
        }

        if (Mode == GroupCollapseMode.Mini && InternalChildren.Count > MiniThreshold)
        {
            ArrangeMini(finalSize);
            return finalSize;
        }

        double offsetX = Math.Max(0, (finalSize.Width - _columns * _cell.Width) / 2.0);

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];
            int row = i / _columns;
            int column = i % _columns;
            Panel.SetZIndex(child, 0);
            ResetTransform(child);
            if (Mode == GroupCollapseMode.FirstRow && row > 0)
                child.Arrange(new Rect(HiddenOffset, HiddenOffset, _cell.Width, _cell.Height));
            else
                child.Arrange(new Rect(offsetX + column * _cell.Width, row * _cell.Height, _cell.Width, _cell.Height));
        }

        return finalSize;
    }
}
