using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Horizon.Stealth.Controls;

internal static class PageDiag
{
    private const int MaxPages = 12;

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
        return sb.ToString();
    }
}