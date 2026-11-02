using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Horizon.Stealth.Views;

public class TextMinerWindow : Window
{
    private readonly string _pageTitle;
    private readonly string _rawText;
    private readonly List<KeyValuePair<string, int>> _topKeywords = new();
    private int _totalWords = 0;
    private int _uniqueWords = 0;
    private double _readingTimeMins = 0;
    private string _readabilityGrade = "";

    public TextMinerWindow(string pageTitle, string bodyText)
    {
        _pageTitle = string.IsNullOrWhiteSpace(pageTitle) ? "Active Webpage" : pageTitle.Trim();
        _rawText = bodyText ?? "";

        Title = "Text Mining & Keyword Density Plotter";
        Width = 640;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        AnalyzeText();
        BuildUI();
    }

    private void AnalyzeText()
    {
        if (string.IsNullOrWhiteSpace(_rawText))
        {
            _totalWords = 0;
            _uniqueWords = 0;
            _readabilityGrade = "N/A";
            return;
        }

        var words = Regex.Matches(_rawText.ToLowerInvariant(), @"\b[a-z]{3,}\b")
                         .Select(m => m.Value)
                         .ToList();

        _totalWords = words.Count;
        _readingTimeMins = Math.Max(0.5, Math.Round(_totalWords / 220.0, 1));

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "of", "to", "a", "in", "that", "is", "was", "for", "on", "are", "with",
            "as", "at", "be", "this", "have", "from", "or", "by", "one", "had", "not", "but", "what",
            "all", "were", "we", "when", "your", "can", "said", "there", "use", "an", "each", "which",
            "she", "do", "how", "their", "if", "will", "up", "other", "about", "out", "many", "then",
            "them", "these", "so", "some", "her", "would", "make", "like", "him", "into", "time",
            "has", "look", "two", "more", "write", "go", "see", "number", "no", "way", "could",
            "people", "my", "than", "first", "water", "been", "call", "who", "oil", "its", "now",
            "find", "long", "down", "day", "did", "get", "come", "made", "may", "part"
        };

        var filtered = words.Where(w => !stopWords.Contains(w)).ToList();
        _uniqueWords = filtered.Distinct().Count();

        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in filtered)
        {
            freq[w] = freq.TryGetValue(w, out int c) ? c + 1 : 1;
        }

        _topKeywords.AddRange(freq.OrderByDescending(kv => kv.Value).Take(14));

        // Automated Readability Index (ARI)
        int sentences = Math.Max(1, Regex.Matches(_rawText, @"[.!?]+").Count);
        int letters = Regex.Matches(_rawText, @"[a-zA-Z0-9]").Count;

        if (_totalWords > 0)
        {
            double ari = 4.71 * ((double)letters / _totalWords) + 0.5 * ((double)_totalWords / sentences) - 21.43;
            int grade = (int)Math.Round(ari);
            _readabilityGrade = grade switch
            {
                <= 6 => $"Grade {grade} (Elementary)",
                <= 8 => $"Grade {grade} (Middle School)",
                <= 12 => $"Grade {grade} (High School)",
                <= 14 => $"Grade {grade} (College Prep)",
                _ => $"Grade {grade}+ (Post-Graduate / Academic)"
            };
        }
        else
        {
            _readabilityGrade = "Standard";
        }
    }

    private void BuildUI()
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
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // KPI Stat Cards
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Keyword Density Chart
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Actions

        // 1. Header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

        var titleBlock = new TextBlock
        {
            Text = $"\U0001F9E0 Text Mining & Keyword Density Plotter",
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

        // 2. KPI Stat Cards (4 columns)
        var kpiGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

        Border KpiCard(string label, string val, string colorHex, int col)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0x00, 0x00)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 3 ? 0 : 4, 0)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 0, 0, 2) });
            sp.Children.Add(new TextBlock { Text = val, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = (Brush)new BrushConverter().ConvertFromString(colorHex)! });
            b.Child = sp;
            Grid.SetColumn(b, col);
            return b;
        }

        kpiGrid.Children.Add(KpiCard("TOTAL WORDS", $"{_totalWords:N0}", "#4A9EFF", 0));
        kpiGrid.Children.Add(KpiCard("UNIQUE VOCAB", $"{_uniqueWords:N0}", "#69F0AE", 1));
        kpiGrid.Children.Add(KpiCard("EST. READ TIME", $"{_readingTimeMins} min", "#FFD54F", 2));
        kpiGrid.Children.Add(KpiCard("READABILITY LEVEL", _readabilityGrade, "#FF7043", 3));

        Grid.SetRow(kpiGrid, 1);
        rootGrid.Children.Add(kpiGrid);

        // 3. Keyword Density Bars Scroll Area
        var chartBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var chartStack = new StackPanel();

        var chartHeader = new TextBlock
        {
            Text = "TOP ACADEMIC KEYWORDS & OCCURRENCE DENSITY",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        chartStack.Children.Add(chartHeader);

        int maxFreq = _topKeywords.Count > 0 ? _topKeywords.Max(x => x.Value) : 1;

        foreach (var (word, count) in _topKeywords)
        {
            double pct = (double)count / Math.Max(1, _totalWords) * 100.0;
            double barRatio = (double)count / maxFreq;

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // Word
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Bar
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); // Count & %

            var wordBlock = new TextBlock
            {
                Text = word,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(wordBlock, 0);
            row.Children.Add(wordBlock);

            var barTrack = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(4),
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            };
            var barFill = new Border
            {
                Background = new LinearGradientBrush(Color.FromRgb(0x4A, 0x9E, 0xFF), Color.FromRgb(0x00, 0xE5, 0xFF), 0),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(6, barRatio * 320)
            };
            barTrack.Child = barFill;
            Grid.SetColumn(barTrack, 1);
            row.Children.Add(barTrack);

            var countBlock = new TextBlock
            {
                Text = $"{count}x ({pct:F1}%)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(countBlock, 2);
            row.Children.Add(countBlock);

            chartStack.Children.Add(row);
        }

        if (_topKeywords.Count == 0)
        {
            chartStack.Children.Add(new TextBlock
            {
                Text = "No distinctive text content extracted from active tab.",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 10, 0, 10)
            });
        }

        scroller.Content = chartStack;
        chartBorder.Child = scroller;
        Grid.SetRow(chartBorder, 2);
        rootGrid.Children.Add(chartBorder);

        // 4. Actions
        var actionsGrid = new Grid();
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusBlock = new TextBlock
        {
            Text = $"Document: {_pageTitle}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(statusBlock, 0);
        actionsGrid.Children.Add(statusBlock);

        var copyBtn = new Button
        {
            Content = "\U0001F4CB Copy Density Report",
            Height = 32,
            Padding = new Thickness(16, 0, 16, 0),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x50, 0x4A, 0x9E, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4A, 0x9E, 0xFF)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White
        };
        copyBtn.Click += (s, e) =>
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== Text Mining & Readability Report ===");
                sb.AppendLine($"Document: {_pageTitle}");
                sb.AppendLine($"Total Words: {_totalWords}");
                sb.AppendLine($"Unique Vocabulary: {_uniqueWords}");
                sb.AppendLine($"Estimated Read Time: {_readingTimeMins} min");
                sb.AppendLine($"Readability Index: {_readabilityGrade}");
                sb.AppendLine();
                sb.AppendLine("Top Recurring Keywords:");
                foreach (var (w, c) in _topKeywords)
                {
                    sb.AppendLine($"  {w}: {c} times ({(double)c / Math.Max(1, _totalWords) * 100.0:F1}%)");
                }
                Clipboard.SetText(sb.ToString());
                statusBlock.Text = "\u2713 Copied density report to clipboard!";
                statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE));
            }
            catch { }
        };
        Grid.SetColumn(copyBtn, 1);
        actionsGrid.Children.Add(copyBtn);

        Grid.SetRow(actionsGrid, 3);
        rootGrid.Children.Add(actionsGrid);

        shell.Child = rootGrid;
        Content = shell;
    }
}
