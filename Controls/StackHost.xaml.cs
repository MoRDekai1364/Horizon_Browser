using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Horizon.Stealth.Controls;

public partial class StackHost : UserControl
{
    private const int AnimationMs = 300;
    private const double SlideFraction = 0.35;

    private sealed class StackItem
    {
        public string Title { get; init; } = "";
        public FrameworkElement Content { get; init; } = null!;
    }

    private readonly List<StackItem> _items = new();
    private int _index = -1;
    private readonly PagerDots _pager = new(Orientation.Vertical);
    private bool _switching;
    private bool _expanded;
    private double _restWidth = double.NaN;
    private double _restHeight = double.NaN;
    private bool _overlayOpen;
    private double _baseWidth = double.NaN;
    private double _baseHeight = double.NaN;
    private const double InPlaceDim = 0.45;
    private bool _inPlace;
    private Rect _inPlaceOrigin;
    private readonly ScaleTransform _inPlaceScale = new();
    private readonly TranslateTransform _inPlaceMove = new();

    public event Action<int>? SelectionChanged;
    public event Action<bool>? ExpandedChanged;
    public event Action? OverlayClosed;

    public bool WheelSwitchEnabled { get; set; } = true;
    public int Count => _items.Count;
    public int SelectedIndex => _index;
    public bool IsExpanded => _expanded;
    public bool IsOverlayOpen => _overlayOpen;

    public StackHost()
    {
        InitializeComponent();
        Outgoing.RenderTransform = new TranslateTransform();
        Incoming.RenderTransform = new TranslateTransform();
        _pager.PageRequested += target => Go(target, target > _index ? 1 : -1);
        Dots.Children.Add(_pager);
        Loaded += (_, _) => UpdateDots();
    }

    public void AddElement(string title, FrameworkElement content)
    {
        _items.Add(new StackItem { Title = title, Content = content });
        if (_index < 0) ShowImmediate(0);
        else UpdateDots();
    }

    public void RemoveElement(FrameworkElement content)
    {
        int removed = _items.FindIndex(i => ReferenceEquals(i.Content, content));
        if (removed < 0) return;

        if (_switching) FinishSwitch();

        bool wasSelected = removed == _index;
        if (wasSelected) Incoming.Content = null;
        _items.RemoveAt(removed);

        if (_items.Count == 0)
        {
            _index = -1;
            UpdateDots();
            return;
        }

        if (wasSelected) ShowImmediate(Math.Min(removed, _items.Count - 1));
        else
        {
            if (removed < _index) _index--;
            UpdateDots();
        }
    }

    public void ReplaceElement(FrameworkElement oldContent, string title, FrameworkElement newContent)
    {
        int index = _items.FindIndex(i => ReferenceEquals(i.Content, oldContent));
        if (index < 0) return;

        if (_switching) FinishSwitch();

        _items[index] = new StackItem { Title = title, Content = newContent };
        if (index == _index)
        {
            Incoming.Content = null;
            Incoming.Content = newContent;
        }
        UpdateDots();
    }

    public void ShowOverlay(FrameworkElement content)
    {
        if (_overlayOpen)
        {
            OverlayContent.Content = content;
            return;
        }

        if (_switching) FinishSwitch();
        _overlayOpen = true;
        OverlayContent.Content = content;
        Overlay.Visibility = Visibility.Visible;

        var duration = new Duration(TimeSpan.FromMilliseconds(AnimationMs));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        Incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
        Overlay.Focus();
    }

    public void ShowInPlace(FrameworkElement content, Rect origin)
    {
        if (_overlayOpen)
        {
            OverlayContent.Content = content;
            return;
        }

        if (_switching) FinishSwitch();
        double width = Math.Max(1, Viewport.ActualWidth);
        double height = Math.Max(1, Viewport.ActualHeight);
        double startX = Math.Max(0.05, origin.Width / width);
        double startY = Math.Max(0.05, origin.Height / height);
        _overlayOpen = true;
        _inPlace = true;
        _inPlaceOrigin = origin;
        OverlayContent.Content = content;

        var transforms = new TransformGroup();
        transforms.Children.Add(_inPlaceScale);
        transforms.Children.Add(_inPlaceMove);
        OverlayContent.RenderTransformOrigin = new Point(0, 0);
        OverlayContent.RenderTransform = transforms;
        Overlay.BeginAnimation(OpacityProperty, null);
        Overlay.Opacity = 1;
        Overlay.Visibility = Visibility.Visible;

        var duration = new Duration(TimeSpan.FromMilliseconds(AnimationMs));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        _inPlaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(startX, 1, duration) { EasingFunction = ease });
        _inPlaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(startY, 1, duration) { EasingFunction = ease });
        _inPlaceMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(origin.X, 0, duration) { EasingFunction = ease });
        _inPlaceMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(origin.Y, 0, duration) { EasingFunction = ease });
        OverlayContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        OverlayBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, InPlaceDim, duration) { EasingFunction = ease });
        Incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
        Overlay.Focus();
    }

    private void CloseInPlace()
    {
        _inPlace = false;
        var duration = new Duration(TimeSpan.FromMilliseconds(AnimationMs));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        double width = Math.Max(1, Viewport.ActualWidth);
        double height = Math.Max(1, Viewport.ActualHeight);
        _inPlaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Math.Max(0.05, _inPlaceOrigin.Width / width), duration) { EasingFunction = ease });
        _inPlaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(Math.Max(0.05, _inPlaceOrigin.Height / height), duration) { EasingFunction = ease });
        _inPlaceMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_inPlaceOrigin.X, duration) { EasingFunction = ease });
        _inPlaceMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_inPlaceOrigin.Y, duration) { EasingFunction = ease });
        OverlayContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
        OverlayBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });

        var restore = new DoubleAnimation(1, duration) { EasingFunction = ease };
        restore.Completed += (_, _) =>
        {
            if (_overlayOpen) return;
            Incoming.BeginAnimation(OpacityProperty, null);
            Incoming.Opacity = 1;
            _inPlaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _inPlaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _inPlaceMove.BeginAnimation(TranslateTransform.XProperty, null);
            _inPlaceMove.BeginAnimation(TranslateTransform.YProperty, null);
            OverlayContent.BeginAnimation(OpacityProperty, null);
            OverlayContent.Opacity = 1;
            OverlayContent.RenderTransform = null;
            OverlayBackdrop.BeginAnimation(OpacityProperty, null);
            OverlayBackdrop.Opacity = 1;
            Overlay.Opacity = 0;
            Overlay.Visibility = Visibility.Collapsed;
            OverlayContent.Content = null;
        };
        Incoming.BeginAnimation(OpacityProperty, restore);
    }

    public void CloseOverlay()
    {
        if (!_overlayOpen) return;
        _overlayOpen = false;
        if (_inPlace)
        {
            CloseInPlace();
            OverlayClosed?.Invoke();
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(AnimationMs));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        Incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });

        var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (_overlayOpen) return;
            Overlay.BeginAnimation(OpacityProperty, null);
            Overlay.Opacity = 0;
            Overlay.Visibility = Visibility.Collapsed;
            OverlayContent.Content = null;
            Incoming.BeginAnimation(OpacityProperty, null);
            Incoming.Opacity = 1;
        };
        Overlay.BeginAnimation(OpacityProperty, fade);
        OverlayClosed?.Invoke();
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseOverlay();
    }

    private void OverlayBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseOverlay();
    }

    public void Select(FrameworkElement content)
    {
        int target = _items.FindIndex(i => ReferenceEquals(i.Content, content));
        if (target >= 0) Go(target, target > _index ? 1 : -1);
    }

    public void Next()
    {
        if (_items.Count < 2) return;
        Go((_index + 1) % _items.Count, 1);
    }

    public void Previous()
    {
        if (_items.Count < 2) return;
        Go((_index - 1 + _items.Count) % _items.Count, -1);
    }

    public void SetRestSize(double width, double height)
    {
        _baseWidth = width;
        _baseHeight = height;
        if (_expanded) return;
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        Width = width;
        Height = height;
    }

    public void ExpandTo(double width, double height)
    {
        if (!_expanded)
        {
            _restWidth = ActualWidth;
            _restHeight = ActualHeight;
        }
        _expanded = true;
        AnimateSize(width, height, false);
        ExpandedChanged?.Invoke(true);
    }

    public void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        AnimateSize(_restWidth, _restHeight, true);
        ExpandedChanged?.Invoke(false);
    }

    private void Go(int index, int direction)
    {
        if (index < 0 || index >= _items.Count || index == _index || _switching || _overlayOpen) return;

        _switching = true;
        double offset = Math.Max(ActualHeight, 1) * SlideFraction;

        var outgoingMove = (TranslateTransform)Outgoing.RenderTransform;
        var incomingMove = (TranslateTransform)Incoming.RenderTransform;

        object? previous = Incoming.Content;
        Incoming.Content = null;
        Outgoing.Content = previous;
        Incoming.Content = _items[index].Content;

        Outgoing.Opacity = 1;
        outgoingMove.Y = 0;
        Incoming.Opacity = 0;
        incomingMove.Y = direction * offset;

        _index = index;
        UpdateDots();
        SelectionChanged?.Invoke(index);

        var duration = new Duration(TimeSpan.FromMilliseconds(AnimationMs));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        var fadeOut = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fadeOut.Completed += (_, _) => FinishSwitch();

        Outgoing.BeginAnimation(OpacityProperty, fadeOut);
        Incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
        outgoingMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-direction * offset, duration) { EasingFunction = ease });
        incomingMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
    }

    private void FinishSwitch()
    {
        if (!_switching) return;
        _switching = false;

        var outgoingMove = (TranslateTransform)Outgoing.RenderTransform;
        var incomingMove = (TranslateTransform)Incoming.RenderTransform;

        Outgoing.BeginAnimation(OpacityProperty, null);
        Incoming.BeginAnimation(OpacityProperty, null);
        outgoingMove.BeginAnimation(TranslateTransform.YProperty, null);
        incomingMove.BeginAnimation(TranslateTransform.YProperty, null);

        Outgoing.Content = null;
        Outgoing.Opacity = 1;
        Incoming.Opacity = 1;
        outgoingMove.Y = 0;
        incomingMove.Y = 0;
    }

    private void ShowImmediate(int index)
    {
        Incoming.Content = _items[index].Content;
        Incoming.Opacity = 1;
        _index = index;
        UpdateDots();
        SelectionChanged?.Invoke(index);
    }

    private void AnimateSize(double width, double height, bool release)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(AnimationMs));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        var widthAnimation = new DoubleAnimation(ActualWidth, width, duration) { EasingFunction = ease };
        var heightAnimation = new DoubleAnimation(ActualHeight, height, duration) { EasingFunction = ease };

        if (release)
        {
            heightAnimation.Completed += (_, _) =>
            {
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                Width = _baseWidth;
                Height = _baseHeight;
            };
        }

        BeginAnimation(WidthProperty, widthAnimation);
        BeginAnimation(HeightProperty, heightAnimation);
    }

    private void UpdateDots()
    {
        Dots.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _pager.SetState(_items.Count, _index, _items.Select(item => item.Title).ToList());
    }

    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!WheelSwitchEnabled || _items.Count < 2 || e.Handled || _overlayOpen) return;
        e.Handled = true;
        if (e.Delta > 0) Previous(); else Next();
    }
}