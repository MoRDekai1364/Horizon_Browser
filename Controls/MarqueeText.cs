using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Horizon.Stealth.Controls;

public static class MarqueeText
{
    public const double PixelsPerSecond = 80.0;
    public const double HoldSeconds = 0.5;
    public const double ExitSeconds = 0.3;

    public static double Measure(TextBlock textBlock, string? text = null)
    {
        var formatted = new FormattedText(
            text ?? textBlock.Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
            textBlock.FontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(textBlock).PixelsPerDip);
        return formatted.Width;
    }

    public static bool IsNeeded(double textWidth, double containerWidth)
    {
        return textWidth > containerWidth;
    }

    public static double TravelSeconds(double textWidth, double containerWidth)
    {
        return (textWidth + containerWidth) / PixelsPerSecond;
    }

    public static void Start(TranslateTransform transform, double textWidth, double containerWidth)
    {
        double travel = TravelSeconds(textWidth, containerWidth);

        var animation = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(containerWidth, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-textWidth, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(travel))));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(containerWidth, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(travel + HoldSeconds))));

        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    public static void Stop(TranslateTransform transform)
    {
        var back = new DoubleAnimation(0, new Duration(TimeSpan.FromSeconds(ExitSeconds)));
        transform.BeginAnimation(TranslateTransform.XProperty, back);
    }

    public static void Reset(TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
    }
}