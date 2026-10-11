using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Horizon.Stealth.Controls;

internal static class PageDiag
{
    private const int MaxPages = 12;

    internal static string Ink(FrameworkElement element)
    {
        double width = element.ActualWidth;
        double height = element.ActualHeight;
        if (width < 1 || height < 1) return "n/a";
        double scale = Math.Min(1.0, 160.0 / Math.Max(width, height));
        int pw = Math.Max(1, (int)Math.Round(width * scale));
        int ph = Math.Max(1, (int)Math.Round(height * scale));
        try
        {
            var brush = new VisualBrush(element)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, width, height),
                Stretch = Stretch.Fill
            };
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(brush, null, new Rect(0, 0, pw, ph));
            }
            var bitmap = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[pw * ph * 4];
            bitmap.CopyPixels(pixels, pw * 4, 0);
            int inked = 0;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 8) inked++;
            }
            return (100.0 * inked / (pw * ph)).ToString("0") + "%";
        }
        catch (Exception ex)
        {
            return "err:" + ex.GetType().Name;
        }
    }

    private static string Images(DependencyObject root)
    {
        int total = 0;
        int ready = 0;
        CountImages(root, ref total, ref ready);
        return ready + "/" + total;
    }

    private static void CountImages(DependencyObject node, ref int total, ref int ready)
    {
        if (node is Image image)
        {
            total++;
            if (image.Source is BitmapSource bitmap ? bitmap.PixelWidth > 0 : image.Source != null) ready++;
        }
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) CountImages(VisualTreeHelper.GetChild(node, i), ref total, ref ready);
    }

    private static string Chain(DependencyObject start)
    {
        var sb = new StringBuilder(" chain:");
        double effective = 1;
        DependencyObject? current = start;
        for (int depth = 0; current != null && depth < 60; depth++)
        {
            if (current is UIElement element)
            {
                effective *= element.Opacity;
                bool notable = element.Opacity < 0.999
                    || element.Visibility != Visibility.Visible
                    || element.Effect != null
                    || element.CacheMode != null
                    || element.OpacityMask != null
                    || element.Clip != null;
                if (notable)
                {
                    sb.Append(" [").Append(element.GetType().Name);
                    if (element is FrameworkElement named && !string.IsNullOrEmpty(named.Name)) sb.Append(':').Append(named.Name);
                    sb.Append(" op=").Append(element.Opacity.ToString("0.##"));
                    if (element.Visibility != Visibility.Visible) sb.Append(" vis=").Append(element.Visibility);
                    if (element.Effect != null) sb.Append(" fx=").Append(element.Effect.GetType().Name);
                    if (element.CacheMode != null) sb.Append(" cache");
                    if (element.OpacityMask != null) sb.Append(" mask");
                    if (element.Clip != null) sb.Append(" clip");
                    sb.Append(']');
                }
            }
            current = current is Visual ? VisualTreeHelper.GetParent(current) : null;
        }
        return sb.Append(" effOp=").Append(effective.ToString("0.###")).ToString();
    }

    public static string Describe(Panel strip, FrameworkElement viewport, double pageWidth, int page, bool animating)
    {
        var sb = new StringBuilder();
        sb.Append(" pageWidth=").Append(pageWidth.ToString("0"));
        sb.Append(" animating=").Append(animating);
        int count = Math.Min(strip.Children.Count, MaxPages);
        for (int p = 0; p < count; p++)
        {
            double x = double.NaN;
            int tiles = 0;
            int shown = 0;
            double minOpacity = 1;
            if (strip.Children[p] is Grid pageGrid)
            {
                try
                {
                    x = pageGrid.TranslatePoint(new Point(0, 0), viewport).X;
                }
                catch (InvalidOperationException)
                {
                }
                if (pageGrid.Children.Count > 0 && pageGrid.Children[0] is Panel host)
                {
                    foreach (UIElement row in host.Children)
                    {
                        if (row is not Panel rowPanel) continue;
                        foreach (UIElement tile in rowPanel.Children)
                        {
                            tiles++;
                            if (tile is FrameworkElement fe && fe.IsVisible && fe.ActualWidth > 0 && fe.ActualHeight > 0) shown++;
                            minOpacity = Math.Min(minOpacity, tile.Opacity);
                        }
                    }
                }
            }
            sb.Append(" [p").Append(p).Append(p == page ? "*" : "").Append(" x=").Append(double.IsNaN(x) ? "?" : x.ToString("0"));
            sb.Append(" tiles=").Append(tiles).Append(" shown=").Append(shown).Append(" minOp=").Append(minOpacity.ToString("0.##")).Append(']');
        }
        if (strip.Children.Count > count) sb.Append(" +").Append(strip.Children.Count - count).Append(" more");
        if (page >= 0 && page < strip.Children.Count) sb.Append(Chain(strip.Children[page]));
        if (page >= 0 && page < strip.Children.Count && strip.Children[page] is FrameworkElement activePage)
            sb.Append(" ink[page=").Append(Ink(activePage)).Append(" viewport=").Append(Ink(viewport)).Append("] imgs=").Append(Images(activePage));
        return sb.ToString();
    }
}