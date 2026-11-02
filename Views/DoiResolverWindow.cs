using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Horizon.Stealth.Views;

public class DoiResolverWindow : Window
{
    private readonly Action<string> _openTabAction;
    private TextBox _txtDoi = null!;
    private TextBlock _txtStatus = null!;

    public DoiResolverWindow(string initialUrlOrText, Action<string> openTabAction)
    {
        _openTabAction = openTabAction;

        string detectedDoi = ExtractDoi(initialUrlOrText);

        Title = "DOI / Paywall Resolver";
        Width = 580;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        BuildUI(detectedDoi);
    }

    private static string ExtractDoi(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var match = Regex.Match(input, @"10\.\d{4,9}/[-._;()/:A-Za-z0-9]+");
        return match.Success ? match.Value.TrimEnd('.', ',', ')') : "";
    }

    private void BuildUI(string detectedDoi)
    {
        var shell = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x14, 0x19)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22)
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Input Box
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Resolver Buttons
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer / Status

        // 1. Header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

        var titleBlock = new TextBlock
        {
            Text = "\U0001F513 DOI & Academic Paywall Resolver",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(titleBlock);

        var closeBtn = new Button
        {
            Content = "\u2715",
            Width = 28,
            Height = 28,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeBtn.Click += (s, e) => Close();
        header.Children.Add(closeBtn);

        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // 2. Input Box
        var inputStack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        var lbl = new TextBlock
        {
            Text = "Enter or paste Digital Object Identifier (DOI) or paper URL:",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 6)
        };
        inputStack.Children.Add(lbl);

        _txtDoi = new TextBox
        {
            Text = string.IsNullOrEmpty(detectedDoi) ? "10.1038/s41586-020-2649-2" : detectedDoi,
            FontSize = 12.5,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontFamily = new FontFamily("Consolas, Segoe UI")
        };
        inputStack.Children.Add(_txtDoi);

        Grid.SetRow(inputStack, 1);
        rootGrid.Children.Add(inputStack);

        // 3. Resolvers Buttons Area
        var buttonsStack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        Button ResolverBtn(string name, string desc, string badge, string colorHex, Action onClick)
        {
            var btn = new Button
            {
                Height = 44,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12, 0, 12, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0x00, 0x00)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) });
            Grid.SetColumn(sp, 0);
            g.Children.Add(sp);

            var b = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                Background = new SolidColorBrush(Color.FromArgb(0x28, 0x4A, 0x9E, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x4A, 0x9E, 0xFF)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Child = new TextBlock { Text = badge, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)new BrushConverter().ConvertFromString(colorHex)! };
            Grid.SetColumn(b, 1);
            g.Children.Add(b);

            btn.Content = g;
            btn.Click += (s, e) => onClick();
            return btn;
        }

        buttonsStack.Children.Add(ResolverBtn("Unpaywall Open Access Engine", "Resolves legal open-access mirrors, pre-prints, and institutional repositories.", "FREE & LEGAL", "#69F0AE", () =>
        {
            string clean = ExtractDoi(_txtDoi.Text);
            if (string.IsNullOrEmpty(clean)) clean = _txtDoi.Text.Trim();
            string target = $"https://oadoi.org/{clean}";
            _openTabAction(target);
            Close();
        }));

        buttonsStack.Children.Add(ResolverBtn("PubMed Central (PMC) Archive", "Searches National Library of Medicine for NIH public access manuscripts.", "NATIONAL REPO", "#4A9EFF", () =>
        {
            string clean = ExtractDoi(_txtDoi.Text);
            if (string.IsNullOrEmpty(clean)) clean = _txtDoi.Text.Trim();
            string target = $"https://pubmed.ncbi.nlm.nih.gov/?term={Uri.EscapeDataString(clean)}";
            _openTabAction(target);
            Close();
        }));

        buttonsStack.Children.Add(ResolverBtn("arXiv / OpenAlex Pre-Prints", "Searches preprint servers for accepted authors' manuscripts before paywalls.", "PRE-PRINT", "#FFD54F", () =>
        {
            string clean = ExtractDoi(_txtDoi.Text);
            if (string.IsNullOrEmpty(clean)) clean = _txtDoi.Text.Trim();
            string target = $"https://openalex.org/works?search={Uri.EscapeDataString(clean)}";
            _openTabAction(target);
            Close();
        }));

        buttonsStack.Children.Add(ResolverBtn("Sci-Hub Global Mirror", "Direct DOI lookup on decentralized open scientific paper mirror.", "DIRECT MIRROR", "#FF7043", () =>
        {
            string clean = ExtractDoi(_txtDoi.Text);
            if (string.IsNullOrEmpty(clean)) clean = _txtDoi.Text.Trim();
            string target = $"https://sci-hub.se/{clean}";
            _openTabAction(target);
            Close();
        }));

        Grid.SetRow(buttonsStack, 2);
        rootGrid.Children.Add(buttonsStack);

        // 4. Status Bar
        _txtStatus = new TextBlock
        {
            Text = "Select your preferred open access source above to open full paper in a new tab.",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_txtStatus, 3);
        rootGrid.Children.Add(_txtStatus);

        shell.Child = rootGrid;
        Content = shell;
    }
}
