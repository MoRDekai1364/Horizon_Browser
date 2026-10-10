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

        double target = index * Cell + (Cell - Thumb) / 2;
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
        _cells.Children.Clear();
        for (int i = 0; i < count; i++)
        {
            int target = i;
            var dot = new Ellipse
            {
                Width = Dot,
                Height = Dot,
                Opacity = 0.35,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            dot.SetResourceReference(Shape.FillProperty, "HomeTextBrush");
            var cell = new Border
            {
                Width = Cell,
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
        double offset = (Cell - Thumb) / 2;
        Canvas.SetLeft(_thumb, _orientation == Orientation.Horizontal ? 0 : offset);
        Canvas.SetTop(_thumb, _orientation == Orientation.Horizontal ? offset : 0);
        _move.X = 0;
        _move.Y = 0;
    }
}
