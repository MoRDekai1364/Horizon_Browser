using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Horizon.Stealth.Views;

public class CitationBuilderWindow : Window
{
    private string _url = "";
    private string _title = "";
    private string _author = "";
    private string _siteName = "";
    private string _date = "";
    private string _activeFormat = "APA 7";

    private TextBox _outputBox = null!;
    private TextBox _txtAuthor = null!;
    private TextBox _txtTitle = null!;
    private TextBox _txtSite = null!;
    private TextBox _txtDate = null!;
    private TextBlock _statusBlock = null!;

    public CitationBuilderWindow(string title, string url, string author = "", string siteName = "", string date = "")
    {
        _title = string.IsNullOrWhiteSpace(title) ? "Web Document" : title.Trim();
        _url = string.IsNullOrWhiteSpace(url) ? "https://example.com" : url.Trim();
        _author = string.IsNullOrWhiteSpace(author) ? "Author, A." : author.Trim();
        _siteName = string.IsNullOrWhiteSpace(siteName) ? ExtractDomain(url) : siteName.Trim();
        _date = string.IsNullOrWhiteSpace(date) ? DateTime.Now.ToString("yyyy, MMMM d") : date.Trim();

        Title = "Instant Citation Builder";
        Width = 620;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        BuildUI();
        UpdateCitationOutput();
    }

    private static string ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host.Replace("www.", "");
        }
        catch
        {
            return "Website";
        }
    }

    private void BuildUI()
    {
        var shellBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x14, 0x19)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20)
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Metadata Fields
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Format Pills
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Citation Output
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Actions

        // 1. Header
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        headerGrid.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

        var titleBlock = new TextBlock
        {
            Text = "\U0001F4DD Instant Citation Builder",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerGrid.Children.Add(titleBlock);

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
        headerGrid.Children.Add(closeBtn);

        Grid.SetRow(headerGrid, 0);
        rootGrid.Children.Add(headerGrid);

        // 2. Metadata Inputs
        var metaPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        Grid InputRow(string label, out TextBox box, string val)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            g.Children.Add(lbl);

            box = new TextBox
            {
                Text = val,
                FontSize = 11.5,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0x00, 0x00)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4)
            };
            box.TextChanged += (s, e) => UpdateCitationOutput();
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            return g;
        }

        metaPanel.Children.Add(InputRow("Author:", out _txtAuthor, _author));
        metaPanel.Children.Add(InputRow("Page Title:", out _txtTitle, _title));
        metaPanel.Children.Add(InputRow("Site Name:", out _txtSite, _siteName));
        metaPanel.Children.Add(InputRow("Date:", out _txtDate, _date));

        Grid.SetRow(metaPanel, 1);
        rootGrid.Children.Add(metaPanel);

        // 3. Format Selectors
        var formatPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        string[] formats = { "APA 7", "MLA 9", "Chicago 17", "Harvard", "BibTeX" };

        foreach (var fmt in formats)
        {
            var btnFmt = new Button
            {
                Content = fmt,
                Height = 26,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                Background = fmt == _activeFormat ? new SolidColorBrush(Color.FromArgb(0x40, 0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                BorderBrush = fmt == _activeFormat ? new SolidColorBrush(Color.FromArgb(0x80, 0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                Foreground = Brushes.White
            };
            btnFmt.Click += (s, e) =>
            {
                _activeFormat = fmt;
                foreach (var child in formatPanel.Children)
                {
                    if (child is Button b)
                    {
                        bool isSel = b.Content?.ToString() == _activeFormat;
                        b.Background = isSel ? new SolidColorBrush(Color.FromArgb(0x40, 0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
                        b.BorderBrush = isSel ? new SolidColorBrush(Color.FromArgb(0x80, 0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                    }
                }
                UpdateCitationOutput();
            };
            formatPanel.Children.Add(btnFmt);
        }

        Grid.SetRow(formatPanel, 2);
        rootGrid.Children.Add(formatPanel);

        // 4. Citation Output Area
        _outputBox = new TextBox
        {
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x45, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16),
            FontFamily = new FontFamily("Consolas, Segoe UI")
        };
        Grid.SetRow(_outputBox, 3);
        rootGrid.Children.Add(_outputBox);

        // 5. Actions Bar
        var actionsGrid = new Grid();
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _statusBlock = new TextBlock
        {
            Text = "Select formatting style above and copy formatted bibliography.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_statusBlock, 0);
        actionsGrid.Children.Add(_statusBlock);

        var buttonsStack = new StackPanel { Orientation = Orientation.Horizontal };

        var btnExport = new Button
        {
            Content = "\U0001F4BE Save File",
            Height = 32,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 11.5,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White
        };
        btnExport.Click += (s, e) => ExportCitationToFile();
        buttonsStack.Children.Add(btnExport);

        var btnCopy = new Button
        {
            Content = "\U0001F4CB Copy Citation",
            Height = 32,
            Padding = new Thickness(18, 0, 18, 0),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x50, 0x4A, 0x9E, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4A, 0x9E, 0xFF)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White
        };
        btnCopy.Click += (s, e) =>
        {
            try
            {
                Clipboard.SetText(_outputBox.Text);
                _statusBlock.Text = "\u2713 Copied citation to clipboard!";
                _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE));
            }
            catch { }
        };
        buttonsStack.Children.Add(btnCopy);

        Grid.SetColumn(buttonsStack, 1);
        actionsGrid.Children.Add(buttonsStack);

        Grid.SetRow(actionsGrid, 4);
        rootGrid.Children.Add(actionsGrid);

        shellBorder.Child = rootGrid;
        Content = shellBorder;
    }

    private void UpdateCitationOutput()
    {
        string author = _txtAuthor?.Text.Trim() ?? _author;
        string title = _txtTitle?.Text.Trim() ?? _title;
        string site = _txtSite?.Text.Trim() ?? _siteName;
        string date = _txtDate?.Text.Trim() ?? _date;
        string accessedDate = DateTime.Now.ToString("MMMM d, yyyy");
        int year = DateTime.Now.Year;

        if (int.TryParse(date.Split(',')[0].Trim(), out int parsedYear))
        {
            year = parsedYear;
        }

        string result = _activeFormat switch
        {
            "APA 7" => $"{author} ({year}). {title}. {site}. {_url}",
            "MLA 9" => $"{author} \"{title}.\" {site}, {date}, {_url}. Accessed {accessedDate}.",
            "Chicago 17" => $"{author} \"{title}.\" {site}. Last modified {date}. {_url}.",
            "Harvard" => $"{author} ({year}) '{title}', {site}. Available at: {_url} (Accessed: {accessedDate}).",
            "BibTeX" => $"@misc{{citation_{year},\n  author = {{{author}}},\n  title = {{{{{title}}}}},\n  howpublished = {{\\url{{{_url}}}}},\n  year = {{{year}}},\n  note = {{Accessed: {DateTime.Now:yyyy-MM-dd}}}\n}}",
            _ => $"{author} ({year}). {title}. {_url}"
        };

        if (_outputBox != null)
        {
            _outputBox.Text = result;
        }
    }

    private void ExportCitationToFile()
    {
        try
        {
            string ext = _activeFormat == "BibTeX" ? ".bib" : ".txt";
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Horizon_Citations");
            Directory.CreateDirectory(dir);
            string safeTitle = string.Concat(_title.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            if (safeTitle.Length > 24) safeTitle = safeTitle[..24];
            string file = Path.Combine(dir, $"Citation_{safeTitle}{ext}");
            File.WriteAllText(file, _outputBox.Text);
            _statusBlock.Text = $"\u2713 Saved to Documents/Horizon_Citations/";
            _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE));
        }
        catch (Exception ex)
        {
            _statusBlock.Text = $"Export error: {ex.Message}";
        }
    }
}
