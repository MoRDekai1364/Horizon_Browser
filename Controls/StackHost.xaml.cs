using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private bool _switching;
    private bool _expanded;
    private double _restWidth = double.NaN;
    private double _restHeight = double.NaN;

    public event Action<int>? SelectionChanged;
    public event Action<bool>? ExpandedChanged;

    public bool WheelSwitchEnabled { get; set; } = true;
    public int Count => _items.Count;
    public int SelectedIndex => _index;
    public bool IsExpanded => _expanded;

    public StackHost()
    {
        InitializeComponent();
        Outgoing.RenderTransform = new TranslateTransform();
        Incoming.RenderTransform = new TranslateTransform();
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
        if (index < 0 || index >= _items.Count || index == _index || _switching) return;

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
                Width = double.NaN;
                Height = double.NaN;
            };
        }

        BeginAnimation(WidthProperty, widthAnimation);
        BeginAnimation(HeightProperty, heightAnimation);
    }

    private void UpdateDots()
    {
        Dots.Children.Clear();
        Dots.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        Brush brush = TryFindResource("HomeTextBrush") as Brush ?? Brushes.White;

        for (int i = 0; i < _items.Count; i++)
        {
            bool active = i == _index;
            var dot = new Ellipse
            {
                Width = active ? 8 : 6,
                Height = active ? 8 : 6,
                Margin = new Thickness(0, 3, 0, 3),
                Fill = brush,
                Opacity = active ? 0.95 : 0.35,
                Cursor = Cursors.Hand,
                Tag = i,
                ToolTip = _items[i].Title
            };
            dot.MouseLeftButtonUp += Dot_Click;
            Dots.Children.Add(dot);
        }
    }

    private void Dot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement dot && dot.Tag is int target)
        {
            Go(target, target > _index ? 1 : -1);
            e.Handled = true;
        }
    }

    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!WheelSwitchEnabled || _items.Count < 2 || e.Handled) return;
        e.Handled = true;
        if (e.Delta > 0) Previous(); else Next();
    }
}