using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Services;
using Horizon.Stealth.Views;

namespace Horizon.Stealth.Controls;

public partial class Omnibox : UserControl
{
    public event EventHandler<string>? NavigateRequested;
    public event EventHandler<string>? ContainerCategoryChanged;
    public event EventHandler? DesaturateRequested;

    private readonly List<string> _customCategories = new() { "Default", "Personal", "Work", "Finance", "Social" };

    public Omnibox()
    {
        InitializeComponent();
        Loaded += Omnibox_Loaded;
    }

    private void Omnibox_Loaded(object sender, RoutedEventArgs e)
    {
    }

    public void SetText(string text)
    {
        UrlInput.Text = text;
    }

    public static string BuildNavigationUrl(string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrEmpty(raw)) return SettingsService.Current.HomePage;

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https" ||
             uri.Scheme == "about" || uri.Scheme == "data" || uri.Scheme == "horizon" ||
             uri.Scheme == "chrome-extension" || uri.Scheme == "file"))
            return raw;

        if (!raw.Contains(' ') && (raw.Contains('.') || raw.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            return "https://" + raw;

        string template = SettingsService.Current.SearchEngineUrl;
        if (string.IsNullOrWhiteSpace(template))
            template = "https://alohafind.com/search/?q={query}";

        return template.Replace("{query}", Uri.EscapeDataString(raw));
    }

    private void UrlInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateRequested?.Invoke(this, BuildNavigationUrl(UrlInput.Text));
        }
    }

    private void BtnSecurityShield_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PopupSecurityMenu.IsOpen = !PopupSecurityMenu.IsOpen;
    }

    private void ContainerChip_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Border border && border.Tag is string tag)
        {
            foreach (var child in PnlContainerChips.Children)
            {
                if (child is Border b && b.Tag is string bTag)
                {
                    bool active = bTag == tag;
                    b.Background = active ? new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
                    b.BorderBrush = active ? new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                    if (b.Child is TextBlock tb)
                        tb.Foreground = active ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                }
            }
            ContainerCategoryChanged?.Invoke(this, tag);
        }
    }

    private void BtnEditCategories_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        // Prompt dialog to rename or manage categories
        var inputWin = new Window
        {
            Title = "Edit Container Categories",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x24)),
            Foreground = Brushes.White,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize
        };
        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(new TextBlock { Text = "Rename or add categories (comma-separated):", FontSize = 11, Foreground = Brushes.LightGray, Margin = new Thickness(0,0,0,6) });
        var tb = new TextBox { Text = string.Join(", ", _customCategories), Background = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x34)), Foreground = Brushes.White, Padding = new Thickness(6,4,6,4) };
        stack.Children.Add(tb);
        var btn = new Button { Content = "Save Categories", Height = 26, Margin = new Thickness(0,10,0,0), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromRgb(0x00, 0xAA, 0xCC)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        btn.Click += (_, _) =>
        {
            var parts = tb.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            _customCategories.Clear();
            foreach (var p in parts) _customCategories.Add(p.Trim());
            RefreshContainerChips();
            inputWin.Close();
        };
        stack.Children.Add(btn);
        inputWin.Content = stack;
        inputWin.ShowDialog();
    }

    private void BtnAddCategory_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        BtnEditCategories_Click(sender, e);
    }

    private void RefreshContainerChips()
    {
        PnlContainerChips.Children.Clear();
        foreach (var cat in _customCategories)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = Cursors.Hand,
                Tag = cat
            };
            b.MouseLeftButtonUp += ContainerChip_Click;
            b.Child = new TextBlock { Text = cat, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)) };
            PnlContainerChips.Children.Add(b);
        }
        var addB = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 4, 4),
            Cursor = Cursors.Hand
        };
        addB.MouseLeftButtonUp += BtnAddCategory_Click;
        addB.Child = new TextBlock { Text = "+ New", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)) };
        PnlContainerChips.Children.Add(addB);
    }

    private void BtnBurnerEmail_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        string randomId = Guid.NewGuid().ToString("N")[..8];
        string burner = $"temp_{randomId}@dropmail.xyz";
        Clipboard.SetText(burner);
        MessageBox.Show($"Generated burner email copied to clipboard:\n\n{burner}", "Horizon Aegis Privacy", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnDesaturatePage_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        DesaturateRequested?.Invoke(this, EventArgs.Empty);
    }
}
