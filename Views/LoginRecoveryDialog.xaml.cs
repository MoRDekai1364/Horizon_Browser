using System.Windows;
using System.Threading.Tasks;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class LoginRecoveryDialog : Window
{
    private BrowserInfo _detectedBrowser = new BrowserInfo();
    private string _targetDomain;

    public LoginRecoveryDialog(string targetDomain)
    {
        InitializeComponent();
        _targetDomain = targetDomain;
        DetectBrowser();
    }

    private void DetectBrowser()
    {
        _detectedBrowser = BrowserDetectionService.DetectDefaultBrowser();
        
        TxtStep1.Text = $"Open {_detectedBrowser.Name} and ensure you are logged into {_targetDomain}.";
        TxtStep2.Text = $"Close {_detectedBrowser.ProcessName}.exe completely. (Required to unlock session).";
    }

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Foreground = System.Windows.Media.Brushes.Gray;
        TxtStatus.Text = "Verifying process state...";
        BtnSync.IsEnabled = false;

        await Task.Delay(500);

        if (BrowserDetectionService.IsBrowserRunning(_detectedBrowser.ProcessName))
        {
            TxtStatus.Foreground = System.Windows.Media.Brushes.Crimson;
            TxtStatus.Text = $"{_detectedBrowser.Name} is still running. Please close it.";
            BtnSync.IsEnabled = true;
            return;
        }

        TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        TxtStatus.Text = "Browser closed. Starting harvester...";
        
        await Task.Delay(500); 
        
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}