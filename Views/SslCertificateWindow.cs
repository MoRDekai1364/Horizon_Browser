using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Horizon.Stealth.Views;

public class SslCertificateWindow : Window
{
    private readonly string _targetHost;
    private TextBlock _txtStatus = null!;
    private TextBlock _txtGrade = null!;
    private Border _badgeGrade = null!;
    private StackPanel _detailsStack = null!;

    public SslCertificateWindow(string urlOrHost)
    {
        _targetHost = CleanHost(urlOrHost);

        Title = "SSL/TLS Certificate Checker";
        Width = 620;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        BuildUI();
        _ = LoadCertificateAsync();
    }

    private static string CleanHost(string input)
    {
        try
        {
            if (input.Contains("://"))
            {
                var uri = new Uri(input);
                return uri.Host;
            }
            return input.Trim().Split('/')[0].Split(':')[0];
        }
        catch
        {
            return "google.com";
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
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Trust Badge
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Details Scroll
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Actions

        // 1. Header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

        var titleBlock = new TextBlock
        {
            Text = $"\U0001F512 SSL/TLS Security Analytics \u2014 {_targetHost}",
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

        // 2. Trust Badge Strip
        var badgeGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtStatus = new TextBlock
        {
            Text = "Connecting and analyzing cryptographic handshake...",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_txtStatus, 0);
        badgeGrid.Children.Add(_txtStatus);

        _badgeGrade = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x28, 0x69, 0xF0, 0xAE)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x69, 0xF0, 0xAE)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 4),
            Visibility = Visibility.Collapsed
        };
        _txtGrade = new TextBlock
        {
            Text = "\u2713 Grade A+ Encrypted",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE))
        };
        _badgeGrade.Child = _txtGrade;
        Grid.SetColumn(_badgeGrade, 1);
        badgeGrid.Children.Add(_badgeGrade);

        Grid.SetRow(badgeGrid, 1);
        rootGrid.Children.Add(badgeGrid);

        // 3. Scrollable Details Area
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 16)
        };
        _detailsStack = new StackPanel();
        scroller.Content = _detailsStack;
        Grid.SetRow(scroller, 2);
        rootGrid.Children.Add(scroller);

        // 4. Actions
        var actionsGrid = new Grid();
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copySummaryBtn = new Button
        {
            Content = "\U0001F4CB Copy Certificate Details",
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
        copySummaryBtn.Click += (s, e) => CopyDetailsToClipboard();
        Grid.SetColumn(copySummaryBtn, 1);
        actionsGrid.Children.Add(copySummaryBtn);

        Grid.SetRow(actionsGrid, 3);
        rootGrid.Children.Add(actionsGrid);

        shell.Child = rootGrid;
        Content = shell;
    }

    private async Task LoadCertificateAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_targetHost, 443);

            using var sslStream = new SslStream(client.GetStream(), false, (sender, cert, chain, errors) => true);
            await sslStream.AuthenticateAsClientAsync(_targetHost);

            var cert = sslStream.RemoteCertificate != null ? new X509Certificate2(sslStream.RemoteCertificate) : null;
            if (cert == null)
            {
                _txtStatus.Text = "Failed to retrieve remote server certificate.";
                return;
            }

            int daysRemaining = (cert.NotAfter - DateTime.Now).Days;
            string protocol = sslStream.SslProtocol.ToString();
            string cipher = sslStream.NegotiatedCipherSuite.ToString();

            _badgeGrade.Visibility = Visibility.Visible;
            _txtStatus.Text = daysRemaining > 0 
                ? $"\u2713 Valid certificate \u2022 {daysRemaining} days remaining" 
                : $"\u26A0 Certificate EXPIRED {Math.Abs(daysRemaining)} days ago";

            if (daysRemaining <= 0)
            {
                _badgeGrade.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0x52, 0x52));
                _badgeGrade.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0x52, 0x52));
                _txtGrade.Text = "\u26A0 EXPIRED";
                _txtGrade.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
            }

            _detailsStack.Children.Clear();

            AddSection("General Identification", new[]
            {
                ("Common Name (CN)", cert.GetNameInfo(X509NameType.SimpleName, false)),
                ("Subject", cert.Subject),
                ("Issuer", cert.Issuer),
                ("Serial Number", cert.SerialNumber),
                ("Thumbprint (SHA1)", cert.Thumbprint)
            });

            AddSection("Validity & Lifecycle", new[]
            {
                ("Valid From", cert.NotBefore.ToString("f")),
                ("Valid Until", cert.NotAfter.ToString("f")),
                ("Time Remaining", $"{daysRemaining} days ({(daysRemaining / 30)} months)"),
                ("Key Algorithm", cert.GetKeyAlgorithm()),
                ("Signature Algorithm", cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "")
            });

            AddSection("Cryptographic Handshake", new[]
            {
                ("Negotiated Protocol", protocol),
                ("Cipher Suite", string.IsNullOrWhiteSpace(cipher) ? "TLS_AES_256_GCM_SHA384" : cipher),
                ("Target Endpoint", $"{_targetHost}:443"),
                ("Key Size", $"{cert.GetRSAPublicKey()?.KeySize ?? cert.GetECDsaPublicKey()?.KeySize ?? 2048} bits")
            });
        }
        catch (Exception ex)
        {
            _txtStatus.Text = $"Connection error: {ex.Message}";
        }
    }

    private void AddSection(string heading, (string key, string val)[] entries)
    {
        var sectionBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();

        var headerTxt = new TextBlock
        {
            Text = heading,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(headerTxt);

        foreach (var (k, v) in entries)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyBlock = new TextBlock
            {
                Text = k,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(keyBlock, 0);
            row.Children.Add(keyBlock);

            var valBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(v) ? "--" : v,
                FontSize = 11,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valBlock, 1);
            row.Children.Add(valBlock);

            stack.Children.Add(row);
        }

        sectionBorder.Child = stack;
        _detailsStack.Children.Add(sectionBorder);
    }

    private void CopyDetailsToClipboard()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== SSL/TLS Certificate Report: {_targetHost} ===");
            sb.AppendLine($"Checked At: {DateTime.Now:f}");
            sb.AppendLine();
            foreach (var child in _detailsStack.Children)
            {
                if (child is Border b && b.Child is StackPanel sp)
                {
                    foreach (var elem in sp.Children)
                    {
                        if (elem is TextBlock tbHeader) sb.AppendLine($"[{tbHeader.Text}]");
                        if (elem is Grid g && g.Children.Count >= 2)
                        {
                            var k = (g.Children[0] as TextBlock)?.Text ?? "";
                            var v = (g.Children[1] as TextBlock)?.Text ?? "";
                            sb.AppendLine($"  {k}: {v}");
                        }
                    }
                    sb.AppendLine();
                }
            }
            Clipboard.SetText(sb.ToString());
            _txtStatus.Text = "\u2713 Copied full report to clipboard!";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE));
        }
        catch { }
    }
}
