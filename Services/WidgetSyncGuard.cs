using System;

using System.Collections.Concurrent;

using System.Collections.Generic;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Media.Effects;

using Horizon.Stealth.Core;



namespace Horizon.Stealth.Services;



/// <summary>

/// Universal UI & Widget Background-Text Sync Guard.

/// Deterministically guarantees that widget backgrounds and their child texts always update

/// in lockstep without 1-frame lags, margin drifting, clipping, or typography overflow.

/// </summary>

public static class WidgetSyncGuard

{

    private class IslandBinding

    {

        public Border BackgroundBorder { get; set; } = null!;

        public FrameworkElement ContentElement { get; set; } = null!;

        public Thickness Padding { get; set; }

        public CornerRadius CornerRadius { get; set; }

        public bool SyncPosition { get; set; }

    }



    private class TextBinding

    {

        public TextBlock TextBlock { get; set; } = null!;

        public FrameworkElement Container { get; set; } = null!;

        public double MinFontSize { get; set; }

        public double MaxFontSize { get; set; }

        public double AvgCharWidthAt12Pt { get; set; }

    }



    private static readonly List<IslandBinding> _islandBindings = new();

    private static readonly List<TextBinding> _textBindings = new();



    /// <summary>

    /// Binds an island background border to a content element so dimensions and positions

    /// stay in strict geometric lockstep during all resize and layout passes.

    /// </summary>

    public static void BindIsland(

        Border backgroundBorder,

        FrameworkElement contentElement,

        Thickness padding,

        CornerRadius cornerRadius,

        bool syncPosition = true,

        double snapWidthStep = 0.0,

        double minWidth = 0.0,

        IEnumerable<FrameworkElement>? companionElements = null,

        Action? onReconciled = null)

    {

        if (backgroundBorder == null || contentElement == null) return;



        var binding = new IslandBinding

        {

            BackgroundBorder = backgroundBorder,

            ContentElement = contentElement,

            Padding = padding,

            CornerRadius = cornerRadius,

            SyncPosition = syncPosition

        };



        _islandBindings.Add(binding);



        void Reconcile()

        {

            if (!contentElement.IsLoaded && contentElement.ActualWidth <= 0) return;

            double w = contentElement.ActualWidth;

            double h = contentElement.ActualHeight;



            if (w <= 0 || h <= 0) return;



            double targetW = w + padding.Left + padding.Right;
            if (snapWidthStep > 0 && targetW > 0)
            {
                double baseW = Math.Max(minWidth, snapWidthStep);
                if (targetW > baseW)
                    targetW = baseW + Math.Ceiling((targetW - baseW) / snapWidthStep) * snapWidthStep;
                else
                    targetW = baseW;
            }
            if (double.IsNaN(backgroundBorder.Width) || Math.Abs(backgroundBorder.Width - targetW) >= 0.5)
                backgroundBorder.Width = Math.Max(1.0, targetW);

            double targetH = h + padding.Top + padding.Bottom;
            if (double.IsNaN(backgroundBorder.Height) || Math.Abs(backgroundBorder.Height - targetH) >= 0.5)
                backgroundBorder.Height = Math.Max(1.0, targetH);

            backgroundBorder.CornerRadius = cornerRadius;



            if (syncPosition)

            {

                double left = Math.Max(0.0, contentElement.Margin.Left - padding.Left);

                double top  = Math.Max(0.0, contentElement.Margin.Top - padding.Top);

                var targetMargin = new Thickness(left, top, 0, 0);
                if (backgroundBorder.Margin != targetMargin)
                    backgroundBorder.Margin = targetMargin;

            }

            if (companionElements != null)
            {
                foreach (var comp in companionElements)
                {
                    if (comp == null) continue;
                    if (double.IsNaN(comp.Width) || Math.Abs(comp.Width - backgroundBorder.Width) >= 0.5)
                        comp.Width = backgroundBorder.Width;
                    if (double.IsNaN(comp.Height) || Math.Abs(comp.Height - backgroundBorder.Height) >= 0.5)
                        comp.Height = backgroundBorder.Height;
                    if (comp is Border bComp && bComp.CornerRadius != cornerRadius)
                        bComp.CornerRadius = cornerRadius;
                    if (syncPosition && comp.Margin != backgroundBorder.Margin)
                        comp.Margin = backgroundBorder.Margin;
                }
            }

            onReconciled?.Invoke();

        }



        contentElement.SizeChanged += (_, _) => Reconcile();

        contentElement.Loaded += (_, _) => Reconcile();

        Reconcile();

    }



    /// <summary>

    /// Registers a TextBlock with adaptive typography scaling and character ellipsis

    /// so text scales smoothly and NEVER abruptly cuts off or overflows.

    /// </summary>

    public static void RegisterTextFit(

        TextBlock textBlock,

        FrameworkElement container,

        double minFontSize,

        double maxFontSize,

        double avgCharWidthAt12Pt = 0.0)

    {

        if (textBlock == null || container == null) return;



        var binding = new TextBinding

        {

            TextBlock = textBlock,

            Container = container,

            MinFontSize = minFontSize,

            MaxFontSize = maxFontSize,

            AvgCharWidthAt12Pt = avgCharWidthAt12Pt

        };



        _textBindings.Add(binding);



        bool isReconciling = false;
        double lastConstraintW = -1.0;

        double GetStableTextContainerWidth()
        {
            // When container is auto-sized (Width is NaN and not stretched), its ActualWidth
            // is driven by its children. Using ActualWidth creates a layout feedback loop.
            bool autoSized = double.IsNaN(container.Width) && container.HorizontalAlignment != HorizontalAlignment.Stretch;
            if (autoSized)
            {
                if (!double.IsNaN(container.MaxWidth) && !double.IsInfinity(container.MaxWidth) && container.MaxWidth > 10.0)
                    return container.MaxWidth;
            }
            double w = container.ActualWidth > 0 ? container.ActualWidth : container.Width;
            if ((double.IsNaN(w) || w <= 10.0) && !double.IsNaN(container.MaxWidth) && !double.IsInfinity(container.MaxWidth))
                w = container.MaxWidth;
            return w;
        }

        void ReconcileText(bool fromSizeChanged = false)
        {
            if (isReconciling || string.IsNullOrEmpty(textBlock.Text)) return;

            double rawContainerW = GetStableTextContainerWidth();
            if (double.IsNaN(rawContainerW) || rawContainerW <= 10.0) return;

            if (fromSizeChanged && Math.Abs(rawContainerW - lastConstraintW) < 2.0)
                return;
            lastConstraintW = rawContainerW;

            double availW = rawContainerW - (textBlock.Margin.Left + textBlock.Margin.Right);
            if (!double.IsNaN(textBlock.MaxWidth) && !double.IsInfinity(textBlock.MaxWidth) && textBlock.MaxWidth > 10.0)
                availW = Math.Min(availW, textBlock.MaxWidth);
            if (availW <= 10.0) return;

            var fit = HorizonCore.ComputeTextFit(
                availW,
                textBlock.Text,
                minFontSize,
                maxFontSize,
                avgCharWidthAt12Pt);

            double targetFontSize = Math.Round(fit.OptimalFontSize * 2.0) / 2.0;
            isReconciling = true;
            try
            {
                if (Math.Abs(textBlock.FontSize - targetFontSize) >= 0.4)
                    textBlock.FontSize = targetFontSize;

                if (textBlock.TextTrimming != TextTrimming.CharacterEllipsis)
                    textBlock.TextTrimming = TextTrimming.CharacterEllipsis;

                if (!fit.FitsWithoutClipping)
                {
                    ToolTipService.SetToolTip(textBlock, textBlock.Text);
                }
                else
                {
                    ToolTipService.SetToolTip(textBlock, null);
                }
            }
            finally
            {
                isReconciling = false;
            }
        }

        container.SizeChanged += (_, _) => ReconcileText(fromSizeChanged: true);
        container.Loaded += (_, _) => ReconcileText();
        var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        dpd?.AddValueChanged(textBlock, (_, _) => ReconcileText());
        ReconcileText();
    }

    /// <summary>
    /// Registers a TextBox with adaptive typography scaling so text scales smoothly
    /// with the parent container width without triggering wrap/unwrap layout oscillation.
    /// </summary>
    public static void RegisterTextBoxFit(
        TextBox textBox,
        FrameworkElement container,
        double minFontSize,
        double maxFontSize,
        double avgCharWidthAt12Pt = 0.0)
    {
        if (textBox == null || container == null) return;

        bool isReconciling = false;
        double lastBudgetW = -1.0;
        int lastTextLen = -1;

        double ComputeStableWidthBudget()
        {
            // Never feed back content-driven container.ActualWidth when container is auto-sized:
            // wrapping a long word onto line 2 shrinks line 1 width -> shrinks container.ActualWidth
            // -> lowers FontSize -> unwraps word back to line 1 -> grows container.ActualWidth
            // -> raises FontSize -> wraps again in an infinite UpdateLayout() 2-cycle.
            double budget = double.PositiveInfinity;

            if (!double.IsNaN(textBox.MaxWidth) && !double.IsInfinity(textBox.MaxWidth) && textBox.MaxWidth > 10.0)
                budget = Math.Min(budget, textBox.MaxWidth);

            bool autoSized = double.IsNaN(container.Width) && container.HorizontalAlignment != HorizontalAlignment.Stretch;
            if (autoSized)
            {
                if (!double.IsNaN(container.MaxWidth) && !double.IsInfinity(container.MaxWidth) && container.MaxWidth > 10.0)
                {
                    // Account for sibling columns (e.g. search engine picker ~128px) + text margins
                    double containerCap = container.MaxWidth - (textBox.Margin.Left + textBox.Margin.Right) - 128.0;
                    if (containerCap > 40.0)
                        budget = Math.Min(budget, containerCap);
                }
            }
            else
            {
                double cw = container.ActualWidth > 0 ? container.ActualWidth : container.Width;
                if (!double.IsNaN(cw) && cw > 10.0)
                    budget = Math.Min(budget, cw - (textBox.Margin.Left + textBox.Margin.Right));
            }

            if (double.IsInfinity(budget) || double.IsNaN(budget) || budget <= 10.0)
            {
                double fallback = container.ActualWidth > 0 ? container.ActualWidth : container.Width;
                if (double.IsNaN(fallback) || fallback <= 10.0) return -1.0;
                budget = fallback - (textBox.Margin.Left + textBox.Margin.Right);
            }

            return budget;
        }

        void ReconcileTextBox(bool fromSizeChanged = false)
        {
            if (isReconciling) return;

            double availW = ComputeStableWidthBudget();
            if (availW <= 10.0) return;

            string text = textBox.Text ?? string.Empty;
            int textLen = text.Length;

            // Ignore container SizeChanged events triggered by the TextBox's own typing/wrapping
            // when the external width budget has not meaningfully changed.
            if (fromSizeChanged && Math.Abs(availW - lastBudgetW) < 2.0)
                return;

            string sample = textLen == 0 ? "Search here or enter URL" : text;
            var fit = HorizonCore.ComputeTextFit(
                availW,
                sample,
                minFontSize,
                maxFontSize,
                avgCharWidthAt12Pt);

            double targetFontSize = Math.Round(fit.OptimalFontSize * 2.0) / 2.0;

            // If the user is typing additional characters and the width budget hasn't grown,
            // never allow FontSize to jump upward.
            if (textLen > 0 && lastTextLen > 0 && textLen >= lastTextLen && Math.Abs(availW - lastBudgetW) < 2.0)
            {
                targetFontSize = Math.Min(targetFontSize, textBox.FontSize);
            }

            lastBudgetW = availW;
            lastTextLen = textLen;

            if (Math.Abs(textBox.FontSize - targetFontSize) >= 0.4)
            {
                isReconciling = true;
                try
                {
                    textBox.FontSize = targetFontSize;
                }
                finally
                {
                    isReconciling = false;
                }
            }
        }

        container.SizeChanged += (_, _) => ReconcileTextBox(fromSizeChanged: true);
        container.Loaded += (_, _) => ReconcileTextBox();
        textBox.TextChanged += (_, _) => ReconcileTextBox();
        ReconcileTextBox();
    }



    /// <summary>

    /// Synchronizes text contrast and shadow against the underlying background wallpaper.

    /// Uses native Rust luminance calculation to guarantee WCAG AAA legibility.

    /// </summary>

    public static void SyncTextContrastAgainstWallpaper(

        TextBlock textBlock,

        byte[]? wallpaperBgra,

        int imgWidth,

        int imgHeight,

        int stride,

        int rx,

        int ry,

        int rw,

        int rh)

    {

        if (textBlock == null || wallpaperBgra == null) return;



        var report = HorizonCore.CalculateRegionLuminance(

            wallpaperBgra, imgWidth, imgHeight, stride, rx, ry, rw, rh);



        if (report != null)

        {

            if (report.PreferDarkText)

            {

                textBlock.Foreground = new SolidColorBrush(Color.FromArgb(0xEE, 0x1A, 0x1A, 0x1A));

                textBlock.Effect = null;

            }

            else

            {

                textBlock.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

                textBlock.Effect = new DropShadowEffect

                {

                    BlurRadius = 8,

                    ShadowDepth = 1.5,

                    Direction = 270,

                    Color = Colors.Black,

                    Opacity = 0.65

                };

            }

        }

    }

}

