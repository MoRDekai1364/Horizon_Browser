using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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

    private const int MaxColumns = 6;
    private const int MinRows = 2;
    private const int MaxRows = 6;
    private const double MaxCardWidth = 260;

    public static readonly DependencyProperty ViewportBudgetProperty = DependencyProperty.RegisterAttached(
        "ViewportBudget", typeof(double), typeof(PuzzlePanel), new PropertyMetadata(0.0, OnViewportBudgetChanged));

    private static readonly DependencyProperty HostPanelProperty = DependencyProperty.RegisterAttached(
        "HostPanel", typeof(PuzzlePanel), typeof(PuzzlePanel), new PropertyMetadata(null));

    public static double GetViewportBudget(DependencyObject d) => (double)d.GetValue(ViewportBudgetProperty);
    public static void SetViewportBudget(DependencyObject d, double value) => d.SetValue(ViewportBudgetProperty, value);

    private static void OnViewportBudgetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Resnap(d as ScrollViewer);
    }

    public static void Resnap(ScrollViewer? host)
    {
        if (host?.GetValue(HostPanelProperty) is PuzzlePanel panel) panel.Snap();
    }

    private readonly List<Rect> _slots = new();
    private double _packedWidth = -1;
    private double _packedUsedWidth = -1;
    private bool _uniform;
    private double _cellW;
    private double _cellH;
    private int _cols = 1;
    private double _snapPitch = -1;
    private ScrollViewer? _host;

    public PuzzlePanel()
    {
        Loaded += (_, _) => AttachHost();
    }

    private void AttachHost()
    {
        DependencyObject? node = VisualTreeHelper.GetParent(this);
        while (node != null && node is not ScrollViewer) node = VisualTreeHelper.GetParent(node);
        if (node is not ScrollViewer host) return;
        _host = host;
        host.SetValue(HostPanelProperty, this);
        Snap();
    }

    private void Snap()
    {
        if (_host == null || !_uniform || _cellH <= 0) return;
        double budget = GetViewportBudget(_host);
        if (budget <= 0) return;
        double pitch = _cellH + Gap;
        int rows = Math.Max(MinRows, Math.Min(MaxRows, (int)Math.Floor((budget + Gap) / pitch)));
        double target = rows * pitch - Gap + 4;
        if (Math.Abs(_host.MaxHeight - target) > 0.5) _host.MaxHeight = target;
    }

    private bool IsUniform()
    {
        return ItemsControl.GetItemsOwner(this) is ItemsControl owner && GroupState.GetIsFolder(owner);
    }

    private Size MeasureUniform(double width)
    {
        double gap = Gap;
        double cellW = 0;
        double cellH = 0;
        int collapsedCount = 0;
        var cardConstraint = new Size(MaxCardWidth, double.PositiveInfinity);

        foreach (UIElement child in InternalChildren)
        {
            if (!GroupState.GetIsCollapsed(child)) continue;
            child.Measure(cardConstraint);
            cellW = Math.Max(cellW, child.DesiredSize.Width);
            cellH = Math.Max(cellH, child.DesiredSize.Height);
            collapsedCount++;
        }

        int cols = 1;
        if (collapsedCount > 0 && cellW > 0)
        {
            int fit = (int)Math.Floor((width + gap) / (cellW + gap));
            cols = Math.Max(1, Math.Min(Math.Min(fit, MaxColumns), collapsedCount));
        }

        var expandedConstraint = new Size(width, double.PositiveInfinity);
        foreach (UIElement child in InternalChildren)
            if (!GroupState.GetIsCollapsed(child)) child.Measure(expandedConstraint);

        _cellW = cellW;
        _cellH = cellH;
        _cols = cols;
        Size size = PackUniform(width);

        if (cellH > 0 && Math.Abs(_snapPitch - (cellH + gap)) > 0.5)
        {
            _snapPitch = cellH + gap;
            Dispatcher.BeginInvoke(new Action(Snap), DispatcherPriority.Loaded);
        }

        return size;
    }

    private Size PackUniform(double width)
    {
        _slots.Clear();
        double gap = Gap;
        double y = 0;
        int c = 0;
        double used = _cellW > 0 ? _cols * _cellW + (_cols - 1) * gap : 0;
        var expanded = new List<int>();

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];
            Size desired = child.DesiredSize;
            if (GroupState.GetIsCollapsed(child))
            {
                _slots.Add(new Rect(c * (_cellW + gap), y, _cellW, _cellH));
                c++;
                if (c >= _cols)
                {
                    c = 0;
                    y += _cellH + gap;
                }
            }
            else
            {
                if (c > 0)
                {
                    c = 0;
                    y += _cellH + gap;
                }
                _slots.Add(new Rect(0, y, desired.Width, desired.Height));
                expanded.Add(i);
                used = Math.Max(used, desired.Width);
                y += desired.Height + gap;
            }
        }

        foreach (int i in expanded)
        {
            Rect slot = _slots[i];
            _slots[i] = new Rect((used - slot.Width) / 2.0, slot.Y, slot.Width, slot.Height);
        }

        double height = 0;
        foreach (Rect slot in _slots) height = Math.Max(height, slot.Bottom);

        _packedWidth = width;
        _packedUsedWidth = used;
        return new Size(used, height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width)
            ? MaxRowWidth
            : Math.Min(availableSize.Width, MaxRowWidth);

        _uniform = IsUniform();
        if (_uniform) return MeasureUniform(width);

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
        if (!reusable)
        {
            if (_uniform) PackUniform(width); else Pack(width);
        }

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
        DependencyProperty.RegisterAttached("IsCollapsed", typeof(bool), typeof(GroupState),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static bool GetIsCollapsed(DependencyObject d) => (bool)d.GetValue(IsCollapsedProperty);
    public static void SetIsCollapsed(DependencyObject d, bool value) => d.SetValue(IsCollapsedProperty, value);

    public static readonly DependencyProperty IsFolderProperty =
        DependencyProperty.RegisterAttached("IsFolder", typeof(bool), typeof(GroupState), new PropertyMetadata(false));

    public static bool GetIsFolder(DependencyObject d) => (bool)d.GetValue(IsFolderProperty);
    public static void SetIsFolder(DependencyObject d, bool value) => d.SetValue(IsFolderProperty, value);
}

public enum GroupCollapseMode
{
    Full,
    FirstRow,
    Stack,
    Mini,
    Folder
}

public static class HomeRowLimits
{
    public const int MinTotalRows = 2;
    public const int MaxTotalRows = 8;
    public const double RowHeight = 171;
    public const double ReservedHeight = 300;

    public static int Ceiling(double availableHeight)
    {
        int fit = (int)Math.Floor(Math.Max(0, availableHeight - ReservedHeight) / RowHeight);
        return Math.Clamp(fit, MinTotalRows, MaxTotalRows);
    }

    public static (int Favorites, int Bookmarks) Resolve(int favorites, int bookmarks, double availableHeight)
    {
        int ceiling = Ceiling(availableHeight);
        favorites = Math.Clamp(favorites, 1, ceiling - 1);
        bookmarks = Math.Clamp(bookmarks, 1, ceiling - 1);
        while (favorites + bookmarks > ceiling)
        {
            if (favorites >= bookmarks) favorites--;
            else bookmarks--;
        }
        return (favorites, bookmarks);
    }
}

public static class FolderFallback
{
    public const int MinFoldersForUsage = 3;
    public const int MinItemsInFolder = 2;

    public static bool ShouldUse(int itemCount, int capacity, int thresholdPercent, IReadOnlyList<(int Items, long Opens, bool Eligible)> folders)
    {
        if (itemCount > capacity) return true;
        if (thresholdPercent <= 0 || folders.Count < MinFoldersForUsage) return false;
        long total = folders.Sum(f => f.Opens);
        if (total <= 0) return false;
        return folders.Any(f => f.Eligible && f.Items >= MinItemsInFolder && f.Opens * 100 >= total * thresholdPercent);
    }
}

public class RowsPanel : Panel
{
    public const int MinColumns = 3;
    public const int MaxColumns = 6;
    public const double CellWidth = 131;

    public static int ColumnsFor(double width)
    {
        return Math.Clamp((int)Math.Floor(width / CellWidth), MinColumns, MaxColumns);
    }

    public static readonly DependencyProperty MaxRowsProperty = DependencyProperty.RegisterAttached(
        "MaxRows", typeof(int), typeof(RowsPanel),
        new FrameworkPropertyMetadata(2, OnMaxRowsChanged));

    public static int GetMaxRows(DependencyObject d) => (int)d.GetValue(MaxRowsProperty);
    public static void SetMaxRows(DependencyObject d, int value) => d.SetValue(MaxRowsProperty, value);

    private int _columns = MinColumns;
    private int _shown;
    private double _cellHeight;

    private static void OnMaxRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (FindPanel(d) is RowsPanel panel) panel.InvalidateMeasure();
    }

    private static RowsPanel? FindPanel(DependencyObject node)
    {
        if (node is RowsPanel panel) return panel;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var found = FindPanel(VisualTreeHelper.GetChild(node, i));
            if (found != null) return found;
        }
        return null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        int maxRows = Math.Max(1, owner != null ? GetMaxRows(owner) : 2);
        double width = double.IsInfinity(availableSize.Width) ? MaxColumns * CellWidth : availableSize.Width;
        _columns = ColumnsFor(width);
        int capacity = _columns * maxRows;
        _shown = Math.Min(InternalChildren.Count, capacity);

        double cellHeight = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (i >= _shown)
            {
                child.Visibility = Visibility.Collapsed;
                continue;
            }
            child.Visibility = Visibility.Visible;
            child.Measure(new Size(CellWidth, double.PositiveInfinity));
            cellHeight = Math.Max(cellHeight, child.DesiredSize.Height);
        }

        _cellHeight = cellHeight;
        int rows = _shown == 0 ? 0 : (_shown + _columns - 1) / _columns;
        return new Size(Math.Min(_columns, Math.Max(_shown, 1)) * CellWidth, rows * cellHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (i >= _shown)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            int row = i / _columns;
            int col = i % _columns;
            int inRow = Math.Min(_columns, _shown - row * _columns);
            double offset = (finalSize.Width - inRow * CellWidth) / 2;
            child.Arrange(new Rect(offset + col * CellWidth, row * _cellHeight, CellWidth, _cellHeight));
        }
        return finalSize;
    }
}

public class AutoGridPanel : Panel
{
    public const int StackDepth = 3;
    public const double StackOffset = 12;
    private const double HiddenOffset = -10000;
    private const int MiniThreshold = 9;
    private const int MiniFullTiles = 8;
    private const int MiniClusterMax = 9;
    private const double FolderCellWidth = 100;
    private const int FolderFullTiles = 3;
    private const int FolderClusterMax = 4;
    private static readonly ScaleTransform FolderScale = CreateScale(0.46);
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

    private static void SetHit(UIElement child, bool value)
    {
        if (child.IsHitTestVisible != value) child.IsHitTestVisible = value;
    }

    private Size FolderSize(int count)
    {
        return count <= FolderFullTiles
            ? new Size(count * _cell.Width, _cell.Height)
            : new Size(2 * _cell.Width, 2 * _cell.Height);
    }

    private void ArrangeFolder(Size finalSize)
    {
        int count = InternalChildren.Count;
        Size size = FolderSize(count);
        double offsetX = Math.Max(0, (finalSize.Width - size.Width) / 2.0);
        bool cluster = count > FolderFullTiles + 1;
        int fullTiles = cluster ? FolderFullTiles : count;
        int shown = cluster ? Math.Min(FolderClusterMax, count - FolderFullTiles) : 0;
        double clusterX = offsetX + _cell.Width;
        double clusterY = _cell.Height;
        double slotWidth = _cell.Width / 2.0;
        double slotHeight = _cell.Height / 2.0;

        for (int i = 0; i < count; i++)
        {
            UIElement child = InternalChildren[i];
            Panel.SetZIndex(child, 0);

            if (i < fullTiles)
            {
                ResetTransform(child);
                SetHit(child, true);
                double x = count <= FolderFullTiles ? i * _cell.Width : (i % 2) * _cell.Width;
                double y = count <= FolderFullTiles ? 0 : (i / 2) * _cell.Height;
                child.Arrange(new Rect(offsetX + x, y, _cell.Width, _cell.Height));
                continue;
            }

            int k = i - fullTiles;
            SetHit(child, false);
            if (k < shown)
            {
                if (!ReferenceEquals(child.RenderTransform, FolderScale)) child.RenderTransform = FolderScale;
                child.Arrange(new Rect(clusterX + (k % 2) * slotWidth, clusterY + (k / 2) * slotHeight, _cell.Width, _cell.Height));
            }
            else
            {
                ResetTransform(child);
                child.Arrange(new Rect(HiddenOffset, HiddenOffset, _cell.Width, _cell.Height));
            }
        }
    }

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
            GroupCollapseMode.Folder => count <= FolderFullTiles ? _cell.Height : 2 * _cell.Height,
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

        if (Mode == GroupCollapseMode.Folder)
        {
            double folderHeight = 0;
            var folderConstraint = new Size(FolderCellWidth, double.PositiveInfinity);
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(folderConstraint);
                folderHeight = Math.Max(folderHeight, child.DesiredSize.Height);
            }

            _cell = new Size(FolderCellWidth, folderHeight);
            return FolderSize(count);
        }

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
        if (Mode == GroupCollapseMode.Folder)
        {
            ArrangeFolder(finalSize);
            return finalSize;
        }

        foreach (UIElement child in InternalChildren) SetHit(child, true);

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
