using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Horizon.Stealth.Controls;

public static class SnapMotion
{
    public const double Seconds = 0.4;
    public const double StretchSeconds = 0.12;
    public static readonly KeySpline Glide = new(0.175, 0.885, 0.32, 1.1);

    public static DoubleAnimationUsingKeyFrames Move(double to)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Seconds)), Glide));
        return animation;
    }

    public static DoubleAnimationUsingKeyFrames Stretch(double peak)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(StretchSeconds)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Seconds)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        return animation;
    }
}

public sealed class PagerDots : Grid
{
    public const double Cell = 24;
    private const double Dot = 8;
    private const double Thumb = 10;
    private const double StretchPeak = 1.8;

    private readonly StackPanel _cells;
    private readonly Ellipse _thumb;
    private readonly TranslateTransform _move = new();
    private readonly ScaleTransform _stretch = new();
    private Orientation _orientation;
    private int _count;
    private int _index = -1;

    public event Action<int>? PageRequested;

    public PagerDots() : this(Orientation.Horizontal)
    {
    }

    public PagerDots(Orientation orientation)
    {
        _orientation = orientation;
        _cells = new StackPanel { Orientation = orientation };

        _thumb = new Ellipse
        {
            Width = Thumb,
            Height = Thumb,
            Opacity = 0.95,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        _thumb.SetResourceReference(Shape.FillProperty, "HomeTextBrush");
        var transforms = new TransformGroup();
        transforms.Children.Add(_stretch);
        transforms.Children.Add(_move);
        _thumb.RenderTransform = transforms;

        var layer = new Canvas { IsHitTestVisible = false };
        layer.Children.Add(_thumb);
        PlaceThumbOnCrossAxis();

        Children.Add(_cells);
        Children.Add(layer);
    }

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value) return;
            _orientation = value;
            _cells.Orientation = value;
            PlaceThumbOnCrossAxis();
            int count = _count;
            int index = _index;
            _count = 0;
            _index = -1;
            SetState(count, index);
        }
    }

    private double _activeCell = Cell;
    private double _activeThumb = Thumb;

    public void SetState(int count, int index, IReadOnlyList<string>? tips = null)
    {
        bool rebuilt = count != _count;
        if (rebuilt) Rebuild(count);

        if (tips != null && tips.Count == count)
        {
            for (int i = 0; i < count; i++) ((Border)_cells.Children[i]).ToolTip = tips[i];
        }

        index = Math.Clamp(index, 0, Math.Max(count - 1, 0));
        bool animate = !rebuilt && IsLoaded && _index >= 0 && index != _index;
        _index = index;

        double target = index * _activeCell + (_activeCell - _activeThumb) / 2;
        var moveProperty = _orientation == Orientation.Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var stretchProperty = _orientation == Orientation.Horizontal ? ScaleTransform.ScaleXProperty : ScaleTransform.ScaleYProperty;

        if (!animate)
        {
            _move.BeginAnimation(moveProperty, null);
            _stretch.BeginAnimation(stretchProperty, null);
            if (_orientation == Orientation.Horizontal) _move.X = target; else _move.Y = target;
            _stretch.ScaleX = 1;
            _stretch.ScaleY = 1;
            return;
        }

        _move.BeginAnimation(moveProperty, SnapMotion.Move(target));
        _stretch.BeginAnimation(stretchProperty, SnapMotion.Stretch(StretchPeak));
    }

    private void Rebuild(int count)
    {
        _count = count;
        _activeCell = count <= 10 ? Cell : Math.Clamp(Math.Floor(240.0 / count), 8.0, Cell);
        double activeDot = count <= 10 ? Dot : Math.Clamp(_activeCell - 4.0, 4.0, Dot);
        _activeThumb = count <= 10 ? Thumb : Math.Clamp(_activeCell - 2.0, 6.0, Thumb);
        _thumb.Width = _activeThumb;
        _thumb.Height = _activeThumb;
        PlaceThumbOnCrossAxis();

        _cells.Children.Clear();
        for (int i = 0; i < count; i++)
        {
            int target = i;
            var dot = new Ellipse
            {
                Width = activeDot,
                Height = activeDot,
                Opacity = 0.35,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            dot.SetResourceReference(Shape.FillProperty, "HomeTextBrush");
            var cell = new Border
            {
                Width = _activeCell,
                Height = Cell,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = dot
            };
            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                PageRequested?.Invoke(target);
            };
            _cells.Children.Add(cell);
        }
    }

    private void PlaceThumbOnCrossAxis()
    {
        double offset = (Cell - _activeThumb) / 2;
        Canvas.SetLeft(_thumb, _orientation == Orientation.Horizontal ? 0 : offset);
        Canvas.SetTop(_thumb, _orientation == Orientation.Horizontal ? offset : 0);
        _move.X = 0;
        _move.Y = 0;
    }
}

public sealed class SegmentThumb
{
    private const double StretchPeak = 1.12;

    private readonly Border _thumb;
    private readonly Panel _host;
    private readonly TranslateTransform _move = new();
    private readonly ScaleTransform _stretch = new();
    private FrameworkElement? _segment;
    private bool _placed;

    public SegmentThumb(Border thumb, Panel host)
    {
        _thumb = thumb;
        _host = host;
        thumb.HorizontalAlignment = HorizontalAlignment.Left;
        thumb.VerticalAlignment = VerticalAlignment.Top;
        thumb.IsHitTestVisible = false;
        thumb.RenderTransformOrigin = new Point(0.5, 0.5);
        var transforms = new TransformGroup();
        transforms.Children.Add(_stretch);
        transforms.Children.Add(_move);
        thumb.RenderTransform = transforms;
        host.SizeChanged += (_, _) => Place(false);
    }

    public void Select(FrameworkElement segment, bool animate)
    {
        _segment = segment;
        Place(animate && _placed);
    }

    private void Place(bool animate)
    {
        if (_segment == null || _segment.ActualHeight <= 0 || !_host.IsLoaded) return;
        double top = _segment.TranslatePoint(new Point(0, 0), _host).Y;
        double left = _segment.TranslatePoint(new Point(0, 0), _host).X;
        double height = _segment.ActualHeight;
        double width = _segment.ActualWidth;

        if (!animate)
        {
            _move.BeginAnimation(TranslateTransform.YProperty, null);
            _move.BeginAnimation(TranslateTransform.XProperty, null);
            _thumb.BeginAnimation(FrameworkElement.HeightProperty, null);
            _thumb.BeginAnimation(FrameworkElement.WidthProperty, null);
            _stretch.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _move.Y = top;
            _move.X = left;
            _thumb.Height = height;
            _thumb.Width = width;
            _stretch.ScaleY = 1;
            _placed = true;
            return;
        }

        _move.BeginAnimation(TranslateTransform.YProperty, SnapMotion.Move(top));
        _move.BeginAnimation(TranslateTransform.XProperty, SnapMotion.Move(left));
        _thumb.BeginAnimation(FrameworkElement.HeightProperty, SnapMotion.Move(height));
        _thumb.BeginAnimation(FrameworkElement.WidthProperty, SnapMotion.Move(width));
        _stretch.BeginAnimation(ScaleTransform.ScaleYProperty, SnapMotion.Stretch(StretchPeak));
    }
}
