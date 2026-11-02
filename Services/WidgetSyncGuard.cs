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



        void ReconcileText()

        {

            if (string.IsNullOrEmpty(textBlock.Text)) return;

            double availW = container.ActualWidth > 0 ? container.ActualWidth : container.Width;

            if (double.IsNaN(availW) || availW <= 10.0) return;



            // Subtract text margins if any

            availW -= (textBlock.Margin.Left + textBlock.Margin.Right);

            if (availW <= 10.0) return;



            var fit = HorizonCore.ComputeTextFit(

                availW,

                textBlock.Text,

                minFontSize,

                maxFontSize,

                avgCharWidthAt12Pt);



            textBlock.FontSize = fit.OptimalFontSize;

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



        container.SizeChanged += (_, _) => ReconcileText();

        container.Loaded += (_, _) => ReconcileText();

        var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

        dpd?.AddValueChanged(textBlock, (_, _) => ReconcileText());

        ReconcileText();

    }



    /// <summary>

    /// Registers a TextBox with adaptive typography scaling so text scales smoothly

    /// with the parent container width.

    /// </summary>

    public static void RegisterTextBoxFit(

        TextBox textBox,

        FrameworkElement container,

        double minFontSize,

        double maxFontSize,

        double avgCharWidthAt12Pt = 0.0)

    {

        if (textBox == null || container == null) return;



        void ReconcileTextBox()

        {

            string sample = string.IsNullOrEmpty(textBox.Text) ? "Search here or enter URL" : textBox.Text;

            double availW = container.ActualWidth > 0 ? container.ActualWidth : container.Width;

            if (double.IsNaN(availW) || availW <= 10.0) return;



            availW -= (textBox.Margin.Left + textBox.Margin.Right);

            if (availW <= 10.0) return;



            var fit = HorizonCore.ComputeTextFit(

                availW,

                sample,

                minFontSize,

                maxFontSize,

                avgCharWidthAt12Pt);



            textBox.FontSize = fit.OptimalFontSize;

        }



        container.SizeChanged += (_, _) => ReconcileTextBox();

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

