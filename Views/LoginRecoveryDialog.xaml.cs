using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Services;
using Microsoft.Win32;

namespace Horizon.Stealth.Views;

public partial class LoginRecoveryDialog : Window
{
    private BrowserInfo _detectedBrowser = new();
    private readonly string _targetDomain;

    public bool BypassProtectionsRequested { get; private set; }
    public string? SelectedJsonFilePath { get; private set; }

    public LoginRecoveryDialog(string targetDomain)
    {
        InitializeComponent();
        _targetDomain = string.IsNullOrWhiteSpace(targetDomain) ? "current site" : targetDomain;
        TxtDomainSubtitle.Text = $"Target domain: {_targetDomain}";
        DetectBrowser();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void DetectBrowser()
    {
        _detectedBrowser = BrowserDetectionService.DetectDefaultBrowser();
        TxtDetectedBrowser.Text = $"Source: {_detectedBrowser.Name}";
        TxtStep1.Text = $"\u2022 Ensure you are signed into {_targetDomain} in {_detectedBrowser.Name} (or import a cookies.json file).";
        TxtStep2.Text = $"\u2022 {_detectedBrowser.Name} ({_detectedBrowser.ProcessName}.exe) must be closed while syncing.";

        if (!string.IsNullOrEmpty(_detectedBrowser.ProcessName) &&
            BrowserDetectionService.IsBrowserRunning(_detectedBrowser.ProcessName))
        {
            BtnForceCloseAndSync.Visibility = Visibility.Visible;
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            TxtStatus.Text = $"{_detectedBrowser.Name} is currently open. Close it or click 'Close Browser & Sync'.";
        }
    }

    private void BtnSafeMode_Click(object sender, RoutedEventArgs e)
    {
        BypassProtectionsRequested = true;
        DialogResult = true;
        Close();
    }

    private void BtnImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Select cookies JSON file for {_targetDomain}",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) == true)
        {
            SelectedJsonFilePath = dlg.FileName;
            DialogResult = true;
            Close();
        }
    }

    private async void BtnForceCloseAndSync_Click(object sender, RoutedEventArgs e)
    {
        BtnSync.IsEnabled = false;
        BtnForceCloseAndSync.IsEnabled = false;
        TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        TxtStatus.Text = $"Closing {_detectedBrowser.Name}...";

        await Task.Run(() =>
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(_detectedBrowser.ProcessName))
                {
                    try { p.Kill(); p.WaitForExit(2000); } catch { }
                }
            }
            catch { }
        });

        await Task.Delay(350);
        BtnForceCloseAndSync.Visibility = Visibility.Collapsed;
        TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
        TxtStatus.Text = $"{_detectedBrowser.Name} closed. Syncing session tokens...";
        await Task.Delay(250);

        DialogResult = true;
        Close();
    }

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        TxtStatus.Text = "Verifying browser state...";
        BtnSync.IsEnabled = false;

        await Task.Delay(250);

        if (!string.IsNullOrEmpty(_detectedBrowser.ProcessName) &&
            BrowserDetectionService.IsBrowserRunning(_detectedBrowser.ProcessName))
        {
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            TxtStatus.Text = $"{_detectedBrowser.Name} is still running. Close it or click 'Close Browser & Sync'.";
            BtnForceCloseAndSync.Visibility = Visibility.Visible;
            BtnSync.IsEnabled = true;
            return;
        }

        TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
        TxtStatus.Text = "Browser unlocked. Harvesting session tokens...";

        await Task.Delay(250);

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
