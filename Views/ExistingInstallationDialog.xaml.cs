using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class ExistingInstallationDialog : Window
{
    private readonly List<HorizonInstallationInfo> _installations;
    private bool _isRestoring;

    public ExistingInstallationDialog(List<HorizonInstallationInfo>? installations = null)
    {
        InitializeComponent();
        _installations = installations ?? InstallationDiscoveryService.DiscoverInstallations();
        PopulateCards();
    }

    private void PopulateCards()
    {
        PanelInstallations.Children.Clear();

        if (_installations.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "No other Horizon installations detected on this machine.",
                Foreground = new SolidColorBrush(Color.FromRgb(143, 163, 191)),
                FontSize = 12,
                Margin = new Thickness(0, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            PanelInstallations.Children.Add(emptyText);
            return;
        }

        foreach (var inst in _installations)
        {
            PanelInstallations.Children.Add(CreateCard(inst));
        }
    }

    private Border CreateCard(HorizonInstallationInfo inst)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1C, 0x2B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x35, 0x50)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();

        // Top line: Name & Version
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = inst.DisplayName,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF6, 0xFF))
        };
        Grid.SetColumn(titleBlock, 0);

        var versionBlock = new TextBlock
        {
            Text = $"v{inst.FullVersionString}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD9, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(versionBlock, 1);

        headerGrid.Children.Add(titleBlock);
        headerGrid.Children.Add(versionBlock);
        stack.Children.Add(headerGrid);

        // Path
        var pathBlock = new TextBlock
        {
            Text = inst.Path,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0x93, 0xAE)),
            Margin = new Thickness(0, 2, 0, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = inst.Path
        };
        stack.Children.Add(pathBlock);

        // Data Badges
        var badgesPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        AddBadge(badgesPanel, "Vault", inst.HasVault);
        AddBadge(badgesPanel, "Bookmarks", inst.HasBookmarks);
        AddBadge(badgesPanel, "Cookies", inst.HasCookies);
        AddBadge(badgesPanel, "History", inst.HasHistory);
        AddBadge(badgesPanel, "Settings", inst.HasSettings);
        if (inst.HasProfileData)
        {
            AddBadge(badgesPanel, $"Profile ({inst.FormattedDataSize})", true);
        }
        if (inst.HasNotes)
        {
            AddBadge(badgesPanel, "Notes", true);
        }
        stack.Children.Add(badgesPanel);

        // Action Button
        var btnRestore = new Button
        {
            Content = $"Restore Data from Version {inst.Version}",
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0xD9, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x1A)),
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 7, 16, 7)
        };
        btnRestore.Click += async (s, e) => await PerformRestoreAsync(inst);
        stack.Children.Add(btnRestore);

        card.Child = stack;
        return card;
    }

    private static void AddBadge(WrapPanel panel, string label, bool available)
    {
        var badge = new Border
        {
            Background = available
                ? new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xD9, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x18, 0x7E, 0x93, 0xAE)),
            BorderBrush = available
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xD9, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x30, 0x40, 0x58)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 4)
        };

        var text = new TextBlock
        {
            Text = $"{(available ? "✓ " : "– ")}{label}",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = available
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xD9, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x60, 0x75, 0x90))
        };

        badge.Child = text;
        panel.Children.Add(badge);
    }

    private async Task PerformRestoreAsync(HorizonInstallationInfo inst)
    {
        if (_isRestoring) return;

        var confirm = MessageBox.Show(
            $"Restore all profile data, passwords, bookmarks, and settings from:\n\n{inst.Path} (v{inst.FullVersionString})?\n\nA safety backup of your current data will be created first.",
            "Confirm Migration & Restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        // Dismiss this installation so it is never prompted again
        try
        {
            string normPath = InstallationDiscoveryService.NormalizePath(inst.Path);
            if (!SettingsService.Current.DismissedInstallationPaths.Any(p => string.Equals(InstallationDiscoveryService.NormalizePath(p), normPath, StringComparison.OrdinalIgnoreCase)))
            {
                SettingsService.Current.DismissedInstallationPaths.Add(normPath);
                SettingsService.Save();
            }
        }
        catch { }

        // 1. Prefer reliable out-of-process restore engine (releases all WebView2 locks)
        if (BackupService.LaunchExternalRestore(inst.Path))
        {
            return;
        }

        // 2. Fallback to in-process restore
        _isRestoring = true;
        PanelProgress.Visibility = Visibility.Visible;
        BtnClose.IsEnabled = false;
        BtnDontAskAgain.IsEnabled = false;

        bool success = await InstallationDiscoveryService.RestoreDataFromInstallationAsync(inst, (msg, pct) =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtProgressStatus.Text = msg;
                ProgressBarRestore.Value = pct;
            });
        });

        _isRestoring = false;

        if (success)
        {
            MessageBox.Show(
                $"Data migration and restore from v{inst.FullVersionString} completed successfully!\n\nHorizon will now restart to apply the restored profile.",
                "Restore Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            App.Restart();
        }
        else
        {
            BtnClose.IsEnabled = true;
            BtnDontAskAgain.IsEnabled = true;
            MessageBox.Show("Restoration encountered an error. Check the logs for details.", "Restore Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDontAskAgain_Click(object sender, RoutedEventArgs e)
    {
        foreach (var inst in _installations)
        {
            string normPath = InstallationDiscoveryService.NormalizePath(inst.Path);
            if (!SettingsService.Current.DismissedInstallationPaths.Any(p => string.Equals(InstallationDiscoveryService.NormalizePath(p), normPath, StringComparison.OrdinalIgnoreCase)))
            {
                SettingsService.Current.DismissedInstallationPaths.Add(normPath);
            }
        }
        SettingsService.Save();
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
